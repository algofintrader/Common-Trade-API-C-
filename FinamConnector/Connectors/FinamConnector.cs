using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Finam.TradeApi.Grpc.V1;
using Finam.TradeApi.Proto.V1;
using FinamClient;
using FinamConnector.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Order = FinamConnector.Models.Order;
using OrderBookRow = FinamConnector.Models.OrderBookRow;
using OrderStatus = FinamConnector.Models.OrderStatus;

namespace FinamConnector.Connectors
{

    [DataContract]
    public enum CandleInterval
    {
        [EnumMember]
        M1,
        [EnumMember]
        M5,
        [EnumMember]
        M15,
        [EnumMember]
        H1
    }
   

    /// <summary>
    /// Коннектор к Finam Trade API
    /// </summary>
    public class FinamConnector : IConnector
    {
        private ConcurrentDictionary<string, Money> _moneys = new();
        public IDictionary<string, Money> Moneys => _moneys;

        
       // public IDictionary<string, Position> Positions => _positons;

        private ConcurrentDictionary<string, Order> _order = new();
        public IDictionary<string, Order> Orders => _order;

        public event Action? UpdateMoneys;
        public event Action <ConcurrentDictionary<string, Position>,string>? UpdatedPositions;
        public event Action<Order>? UpdateOrder;
        public event Action<OrderBook>? UpdateOrderBook;
        public event Action<FinInfo>? UpdateFinInfo;
        public event Action<Exception>? Error;

        public event Action<string>? NewLogMessage;
        public event Action<string>? NewCriticalError;

        public  FinamApi _internalFinamApi;
        private readonly Config _config;
        private readonly BlockingCollection<Event> _events = new();
        private Timer? _timerUpdateExtraData;
        private bool _isPortfolioAvailable;
        public int StopTimer { get; set; }
        public int PortfolioTimer { get; set; }


        /// <summary>
        /// Инструменты 
        /// </summary>
        public ConcurrentDictionary<string, Security> Instruments = new ConcurrentDictionary<string, Security>();


         public FinamConnector(params object[] args)
        {
            _config = (Config)args[0];

            _internalFinamApi = new FinamApi(_config.Token!);

            _internalFinamApi.FailedStopOrder += FailedStopOrder;

            _internalFinamApi.NewLog +=(log)=>
            {
                NewLogMessage?.Invoke(log);
            };

           // _internalFinamApi.EventResponse += x => _events.Add(x);

           // Task.Run(ProcessEvents);
        }

        

        IDictionary<string, Models.Order> IConnector.Orders => throw new NotImplementedException();

        private void AddStopManually(NewStopResult r,double price)
         {

             try
             {

                 ActiveStops.Add(new Stop()
                 {
                     StopId = r.StopId,
                     ClientId = r.ClientId,
                     StopLoss = new StopLoss() { ActivationPrice =price },
                 });
             }
             catch (Exception ex)
             {
                NewLogMessage?.Invoke("ошибка - Проблема добавления стопа в ручную " + ex.Message);
             }
         }

         private object stoplocker = new();
         private void FailedStopOrder(NewStopRequest stopRequest)
         {
             lock (stoplocker)
             {
                 CreateTimerAndStart(() =>
                 {
                     try
                     {
                         //добавить цену после тестирования!!!!
                         var key = stopRequest.ClientId + stopRequest.SecurityCode + stopRequest.BuySell +
                                   stopRequest.StopLoss.ActivationPrice;

                         if (!StopsPlacingAgain.ContainsKey(key))
                             StopsPlacingAgain.Add(key, 0);

                         StopsPlacingAgain[key]++;
                         var amount = StopsPlacingAgain[key];

                         if (StopsPlacingAgain[key] > maxstopattempts)
                         {
                             NewLogMessage?.Invoke($" {stopRequest.SecurityCode} Достингнуто максимальное количество попыток {amount} выставления стопа. Больше попыток не будет");
                             NewCriticalError?.Invoke($"Стоп не выставлен после огромного количества попыток {stopRequest.SecurityCode} ");
                         }
                         else
                         {
                             NewLogMessage?.Invoke(
                                 $"{stopRequest.SecurityCode} Повторная попытка выставления {amount} key {key}");
                             var r = _internalFinamApi.PlaceRepeatStopOrder(stopRequest).Result;

                             if (r != null)
                             {
                                 StopsPlacingAgain.Remove(key);
                                 AddStopManually(r, stopRequest.StopLoss.ActivationPrice);
                             }
                         }
                     }
                     catch (Exception ex)
                     {
                         NewLogMessage?.Invoke("ошибка" + ex.Message);
                     }

                 }, 3000, false);
                 

             }
         }



       public async Task<Candle[]> GetCandles( string code, CandleInterval timeframe)
        {
            try
            {

                if (!Instruments.ContainsKey(code))
                {
                    Debug.WriteLine($"Код для инструмента не найден! {code}");
                    return null;
                }

                var sec_board = Instruments[code].Board;
                //Debug.WriteLine($"Заказываю свечки! {code} {sec_board} {timeframe}");

                // string time = fromdateTime.ToUniversalTime().ToString("yyyy-MM-dd THH:mm:ssZ");
                string time = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd THH:mm:ssZ");

                int interval = 100;

                HttpWebRequest request = (HttpWebRequest)WebRequest
                    .Create($"https://trade-api.finam.ru/public/api/v1/intraday-candles?SecurityBoard={sec_board}" +
                    $"&SecurityCode={code}" +
                    $"&TimeFrame={timeframe}" +
                    $"&Interval.To={time}&Interval.Count={interval}");

                request.Method = "GET";
                request.Headers["X-API-KEY"] = _config.Token;

                var response1 = await request.GetResponseAsync();
                var response = (HttpWebResponse)response1;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    using (StreamReader streamReader = new StreamReader(response.GetResponseStream()))
                    {
                        string responseText = streamReader.ReadToEnd();

                        var result = JsonConvert.DeserializeObject<CandleRequest>(responseText, new JsonSerializerSettings
                        {
                            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                            DateParseHandling = DateParseHandling.None,
                            Converters =
                                {
                                 new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal },

                                },

                        });

                        // foreach (var candle in result.Data.Candles)
                        {


                            //Debug.WriteLine($"by{candle.Close.Num} | {candle.Close.Scale}  {candle.Timestamp.LocalDateTime}");
                            //  Debug.WriteLine($"{candle.Close.Value}  {candle.Timestamp.LocalDateTime}") ;
                        }
                        // Debug.WriteLine($"Получен ответ {code}");
                        return result.Data.Candles;
                        //var timecandle = result.Data.Candles.LastOrDefault().Timestamp.LocalDateTime;

                    }
                }
                else
                {
                    NewLogMessage?.Invoke("свечки ошибка " + response.StatusDescription);
                    return null;
                    //return "Error: " + response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                NewLogMessage?.Invoke("свечки ошибка " + ex.Message);
                return null;
            }

        }


        event Action IConnector.UpdatePositions
        {
            add
            {
                throw new NotImplementedException();
            }

            remove
            {
                throw new NotImplementedException();
            }
        }

        event Action<Models.Order> IConnector.UpdateOrder
        {
            add
            {
                throw new NotImplementedException();
            }

            remove
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// метод помощник
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private int getDecimalCount(decimal val)
        {
            int i = 0;
            while (Math.Round(val, i) != val)
                i++;
            return i;
        }

        List<System.Timers.Timer> Timers = new();

        private TimeOnly start = new TimeOnly(10, 0, 0);
        private TimeOnly stop = new TimeOnly(23, 59, 59);
        private void CreateTimerAndStart(Action method, int ms,bool repeat = true, TimeOnly[] timerperiods = null)
        {
            var timer = new System.Timers.Timer(ms) {AutoReset = repeat};
            timer.Elapsed += (s, e) =>
            {

                var timeonlynow = new TimeOnly(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);

                var weekened = DateTime.Now.DayOfWeek == DayOfWeek.Saturday ||
                               DateTime.Now.DayOfWeek == DayOfWeek.Sunday;

                if (timeonlynow >= start && timeonlynow <= stop && !weekened)
                    method?.Invoke();
                else
                {
                   Debug.WriteLine("Не рабочее время!");
                }
            };
            timer.Start();
        }

        //public ConcurrentDictionary<long, Stop> ActiveStopOrders = new ConcurrentDictionary<long, Stop>();

        /// <summary>
        /// Активные стоп ордера... 
        /// </summary>
        public List <Stop> ActiveStops = new List<Stop>();
       
        public async Task Connect()
        {
            try
            {
                NewLogMessage("Получаю инструменты");
                GetSecuritiesResult securities = await _internalFinamApi.GetSecuritiesAsync();
                
                if(securities == null)
                {
                    NewLogMessage("ошибка -> Список инструментов пуст! Скорее всего превышен лимит!");
                    NewCriticalError("Критическая ошибка -> Список инструментов пуст! Скорее всего превышен лимит!");
                }

                if(securities!=null)
                foreach (var sec in securities.Securities)
                {
                        if (sec.Market == Market.Stock || sec.Market == Market.Forts || sec.Market == Market.Spbex)
                        {
                           // Debug.WriteLine(sec.Code + " | " + sec.Board + " | " + sec.Market);

                            if (!Instruments.TryAdd(sec.Code, sec))
                            {
                                Debug.WriteLine("ОДИНАКОВЫЙ!" + sec.Code + " | " + sec.Board + " | " + sec.Market);
                            }
                        }
                }

                Debug.WriteLine($"Получено {Instruments.Count} инструментов");
                Debug.WriteLine("Получаю данные по портфелю");
                NewLogMessage?.Invoke("Получаю данные по портфелю");

              
                if (_isPortfolioAvailable)
                {
                    NewLogMessage?.Invoke("Получаю заявки");
                    await UpdateOrdersAsync().ConfigureAwait(false);
                }

                NewLogMessage?.Invoke("Подписываюсь на данные");

               // пока не уверен что вообще мне это нужно
               
               //await _internalFinamApi.SubscribeOrderTradeAsync(new[] { _config.ClientId }).ConfigureAwait(false);

               
                CreateTimerAndStart(
                   async () =>
                   {

                       List<Stop> activeStopOrders = new();
                       foreach (var clientId in _config.ClientIds)
                       {

                           var res = await _internalFinamApi.GetStopsAsync(clientId, true, false, false)
                               .ConfigureAwait(false);

                           if (res != null)
                           {
                               foreach (var stop in res.Stops.ToList())
                               {
                                   Debug.WriteLine(
                                       $" {stop.SecurityCode} Добавлен стоп {stop.StopId} {stop.ClientId} {stop.StopLoss.ActivationPrice}");
                                   //ActiveStopOrders.TryAdd(stop.StopId, stop);
                                   activeStopOrders.Add(stop);
                               }

                               //если стопы пришли... то обновляем
                               ActiveStops = activeStopOrders.ToList();
                           }
                           else
                           {
                               // если нет, то ниче не делаем. 
                               NewLogMessage?.Invoke("ошибка -> Пришел пустой ответ на стопы!");

                           }

                       }

                       Debug.WriteLine($" ---------------------------");

                   }, StopTimer);

                CreateTimerAndStart(
                  async () =>
                  {
                      await UpdatePortfolioAsync();

                      //todo вернуть 1500 
                  }, PortfolioTimer);

             


                //foreach (var item in listAll)
                //{
                // await _client.SubscribeOrderBookAsync(item.Board, item.Code).ConfigureAwait(false);
                //}

                //_timerUpdateExtraData = new(x => Task.Run(UpdateExtraData), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
                NewLogMessage?.Invoke("Готов к работе");


                var timer = new System.Timers.Timer(2000) { AutoReset = false };
                timer.Elapsed += (s, e) =>
                {
                   // Internal.PlaceStopOrder(_config.ClientId, "TQBR", "SBER", false, 10, 270);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
            }
        }

        public void Dispose()
        {
            _moneys.Clear();
           
            _order.Clear();

            Timers.ForEach(t => t.Stop());
        }

        private async Task UpdatePortfolioAsync()
        {
            try
            {
                

                foreach (var clientid in _config.ClientIds)
                {
                    var portfolio = await _internalFinamApi.GetPortfolioAsync(clientid).ConfigureAwait(false);

                    /*
                    _moneys.Clear();
                    foreach (var item in portfolio.Currencies)
                    {
                        var money = new Money
                        {
                            Currency = item.Name,
                            Balance = item.Balance.Normalize(),
                        };
                        if (money.Balance < 0) continue;
                        _moneys[money.Currency] = money;
                    }

                    UpdateMoneys?.Invoke();*/


                    if (portfolio != null)
                    {
                        ConcurrentDictionary<string, Position> _positons = new();

                        foreach (var item in portfolio.Positions)
                        {
                            Debug.WriteLine($"поза {item.SecurityCode} |{item.Market} | pos = {item.Balance}");

                            var position = new Position
                            {
                                Symbol = item.SecurityCode,
                                Market = item.Market.ToString(),
                                Balance = item.Balance,
                                Profit = item.Profit.Normalize(),
                                ClientID = clientid,
                                AveragePrice = item.AveragePrice,
                                CurrentPrice = item.CurrentPrice,

                            };
                            //pos.SecCode +"|" + pos.ClientCode
                          
                           _positons.TryAdd(position.Symbol + "|" + position.ClientID, position);
                        }

                        UpdatedPositions?.Invoke(_positons, clientid);
                    }

                    Debug.WriteLine($"------------------------------------");

                }

                

            }
            catch (Exception ex)
            {
                NewLogMessage?.Invoke("ошибка -> поз " + ex.Message);
            }

          
        }

        private async Task UpdateOrdersAsync()
        {
            /*
            var orders = await _internalFinamApi.GetOrdersAsync(_config!.ClientId!).ConfigureAwait(false);

            _order.Clear();
            foreach (var item in orders.Orders)
            {
                var order = new Order
                {
                    Id = item.TransactionId.ToString(),
                    Date = (item.CreatedAt ?? item.AcceptedAt)?.ToDateTime().ToLocalTime() ?? DateTime.Now,
                    Symbol = item.SecurityCode,
                    Status = ToOrderStatus(item.Status),
                    Side = ToOrderSide(item.BuySell),
                    Price = item.Price,
                    Quantity = item.Quantity,
                    RestQuantity = item.Balance,
                };
                _order[order.Id] = order;
                UpdateOrder?.Invoke(order);
            }
            */
        }

        public async Task<NewOrderResult> SendOrderAsync(string account, string symbol, BuySell direction, double quantity, double price)
        {
            if(!Instruments.ContainsKey(symbol))
            {
                NewLogMessage?.Invoke("Инструмент не найден в списках");
                return null;
            }

            var secboard = Instruments[symbol].Board;

            return await _internalFinamApi.NewOrderAsync(account, secboard, symbol, direction, (int)quantity, price);
        }

        private int maxstopattempts = 10;
        private Dictionary<string, int> StopsPlacingAgain = new Dictionary<string, int>();
        public async Task<NewStopResult> PlaceStopOrder(string sec, BuySell direction,int quantity,double price, string _clientId)
        {

            
            try
            {

                var instrument = Instruments[sec];
                var secboard = Instruments[sec].Board;

                //финам присылает странный минимальный шаг цены 
                //var priceStep = Instruments[sec].LotSize;
                
                #region пробный код для работы с шагом цены... 
                /*
                string startI = "1";
                for (int i = 0; i < Instruments[sec].Decimals; i++)
                {
                    startI += "0";
                }

                double priceStep = 0;
                try
                {
                     priceStep = Instruments[sec].MinStep / double.Parse(startI);
                }
                catch (Exception ex)
                {

                }
                double price1 = 0;
                if (priceStep != 0)
                {
                     price1 = Math.Round(price, Instruments[sec].Decimals);
                    var rest = price1 % priceStep;
                    //var rest = price % priceStep;

                    Debug.WriteLine("остаток "+ rest.ToString());

                    if (direction == BuySell.Buy)
                        price1 += rest;
                    else
                        price1 -= rest;

                    Debug.WriteLine("Цена " + rest);

                }*/
                #endregion

                var algoprice = price;

                NewLogMessage?.Invoke($"{sec} Выставляю стоп ->  " + algoprice);

                if (instrument.Market == Market.Stock && instrument.LotSize!=0)
                {
                    quantity = quantity / instrument.LotSize;
                }

                return await _internalFinamApi.PlaceStopOrder(_clientId, secboard, sec, direction, quantity, algoprice);
                
            }
            catch (Exception ex)
            {
                NewLogMessage?.Invoke(ex.Message);
                Debug.WriteLine(ex.Message);
                return null;
            }
        }

        public async Task<CancelStopResult> CancelStopOrder(int stopid,string clientId)
        {
            return  await _internalFinamApi.CancelStopOrder(clientId, stopid);
        }

        public async Task CancelOrderAsync(Order order, string clientID)
        {
            
            await _internalFinamApi.CancelOrderAsync(clientID, int.Parse(order.Id))
            .ContinueWith(t =>
            {
                if (t.Exception != null)
                    throw t.Exception;
            });
        }

        private void ProcessEvents()
        {
            foreach (var ev in _events.GetConsumingEnumerable())
            {
                try
                {
                    if (ev.OrderBook != null)
                    {
                        var key = $"{ev.OrderBook.SecurityBoard}:{ev.OrderBook.SecurityCode}";
                        var orderBook = new OrderBook
                        {
                            SecBoard = ev.OrderBook.SecurityBoard,
                            SecCode = ev.OrderBook.SecurityCode,
                            Bids = ev.OrderBook.Bids.Select(x => new OrderBookRow(true, x.Price, x.Quantity)).ToArray(),
                            Asks = ev.OrderBook.Asks.Select(x => new OrderBookRow(false, x.Price, x.Quantity)).ToArray(),
                        };
                        UpdateOrderBook?.Invoke(orderBook);
                    }

                    if (ev.Order != null)
                    {
                        var order = new Order
                        {
                            Id = ev.Order.TransactionId.ToString(),
                            Date = (ev.Order.CreatedAt ?? ev.Order.AcceptedAt)?.ToDateTime().ToLocalTime() ?? DateTime.Now,
                            Symbol = ev.Order.SecurityCode,
                            Status = ToOrderStatus(ev.Order.Status),
                            Side = ToOrderSide(ev.Order.BuySell),
                            Price = ev.Order.Price,
                            Quantity = ev.Order.Quantity,
                            RestQuantity = ev.Order.Balance,
                        };

                        _order[order.Id] = order;
                        UpdateOrder?.Invoke(order);
                    }


                    
                    if (ev.Trade != null)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                await UpdatePortfolioAsync().ConfigureAwait(false);
                            }
                            catch { }
                        });
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
                }
            }
        }

        private async Task UpdateExtraData()
        {
            try
            {
                var client = new HttpClient();
                var baseUrl = "https://iss.moex.com/iss/engines";
                var pameteres = "?iss.meta=off&iss.only=marketdata&marketdata.columns=BOARDID,SECID,VALTODAY,VOLTODAY";
                await UpdateByUrl($"{baseUrl}/stock/markets/shares/boards/TQBR/securities.xml{pameteres}");
                await UpdateByUrl($"{baseUrl}/currency/markets/selt/boards/CETS/securities.xml{pameteres}");
                await UpdateByUrl($"{baseUrl}/futures/markets/forts/boards/RFUD/securities.xml{pameteres}");

                async Task UpdateByUrl(string url)
                {
                    var res = await client.GetStringAsync(url).ConfigureAwait(false);

                    var xDoc = new XmlDocument();
                    xDoc.LoadXml(res);
                    var rows = xDoc.SelectNodes("//row");
                    if (rows == null)
                        return;
                    foreach (XmlElement row in rows)
                    {
                        var boardid = row.GetAttribute("BOARDID").Replace("RFUD", "FUT");
                        var secid = row.GetAttribute("SECID");
                        var valtoday = row.GetAttribute("VALTODAY");
                        var voltoday = row.GetAttribute("VOLTODAY");

                        if (!double.TryParse(valtoday, out var nValtoday))
                            continue;

                        if (!double.TryParse(voltoday, out var nVoltoday))
                            continue;

                        var finInfo = new FinInfo
                        {
                            Board = boardid,
                            Symbol = secid,
                            Valtoday = nValtoday,
                            Voltoday = nVoltoday,
                        };
                        UpdateFinInfo?.Invoke(finInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static OrderSide ToOrderSide(Finam.TradeApi.Proto.V1.BuySell side)
        {
            return side == BuySell.Buy ? OrderSide.Buy : OrderSide.Sell;
        }

        private static OrderStatus ToOrderStatus(Finam.TradeApi.Proto.V1.OrderStatus status)
        {
            return status switch
            {
                Finam.TradeApi.Proto.V1.OrderStatus.Unspecified => OrderStatus.None,
                Finam.TradeApi.Proto.V1.OrderStatus.None => OrderStatus.None,
                Finam.TradeApi.Proto.V1.OrderStatus.Active => OrderStatus.Active,
                Finam.TradeApi.Proto.V1.OrderStatus.Cancelled => OrderStatus.Cancelled,
                Finam.TradeApi.Proto.V1.OrderStatus.Matched => OrderStatus.Executed,
                _ => throw new NotImplementedException(),
            };
        }


       

    }
}

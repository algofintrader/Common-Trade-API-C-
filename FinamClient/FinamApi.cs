using Finam.TradeApi.Grpc.V1;
using Finam.TradeApi.Proto.V1;
using Grpc.Core;
using Grpc.Net.Client;
using System.Diagnostics;
using static Finam.TradeApi.Grpc.V1.Events;
using static Finam.TradeApi.Grpc.V1.Orders;
using static Finam.TradeApi.Grpc.V1.Portfolios;
using static Finam.TradeApi.Grpc.V1.Securities;
using static Finam.TradeApi.Grpc.V1.Stops;
using static Finam.TradeApi.Proto.V1.Candles;

namespace FinamClient
{
    /// <summary>
    /// Класс взаимодействия с Finam Trade Api по протоколу gRPC.
    /// Документация: https://finamweb.github.io/trade-api-docs/
    /// </summary>
    public class FinamApi
    {
        public event Action<Event>? EventResponse;

        private readonly GrpcChannel _channel;
        private readonly SecuritiesClient _securitiesClient;
        private readonly PortfoliosClient _portfoliosClient;
        private readonly EventsClient _eventsClient;
        private readonly OrdersClient _ordersClient;
        private readonly StopsClient _stopsClient;
        private readonly CandlesClient _candlesClient;

        private  Metadata _metadata => new()
        {
            { "X-Api-Key", _token }
        };
        private readonly AsyncDuplexStreamingCall<SubscriptionRequest, Event> _eventsStream;
        private readonly object _lock = new();
        private int _requestCounter;

        public Action<string> NewLog;


        private string _token;

        /// <summary>
        /// Создать класс FinamApi
        /// </summary>
        /// <param name="token">Токен авторизации</param>
        /// <param name="url">Точка входа (url)</param>
        public FinamApi(string token, string url = "https://trade-api.finam.ru")
        {
            _token = token;

            _channel = GrpcChannel.ForAddress(url);
            

            _securitiesClient = new SecuritiesClient(_channel){};
            _portfoliosClient = new PortfoliosClient(_channel);
            _eventsClient = new EventsClient(_channel);
            _ordersClient = new OrdersClient(_channel);
            _stopsClient = new StopsClient(_channel);
            _candlesClient = new CandlesClient(_channel);

            //_eventsStream = _eventsClient.GetEvents(_metadata);
            //RunStream(_eventsStream.ResponseStream);
        }

        /// <summary>
        /// Получение списка инструментов
        /// </summary>
        public async Task<GetSecuritiesResult> GetSecuritiesAsync()
        {
            try
            {
                var res = await _securitiesClient.GetSecuritiesAsync(new GetSecuritiesRequest(), _metadata).ConfigureAwait(false);
                return res;
            }
            catch (Exception ex)
            {
               NewLog?.Invoke("инструменты ошибка " + ex.Message);
               return null;
               
            }
        }

        /// <summary>
        /// тестовый 
        /// </summary>
        /// <returns></returns>
        public async Task<CandleResultRequest>GetCandles()
        {
          

            var r = _candlesClient.GetIntradayCandles(
                new GetCandlesRequest()
                {
                    Count = 100,
                    From = "2023-12-07T08:20:47Z",
                    To = "",
                    SecurityBoard = "TQBR",
                    SecurityCode = "SBER",
                    TimeFrame = "M5",

                }, _metadata);

            return r;
        }

        /// <summary>
        /// Получение портфеля
        /// </summary>
        public async Task<GetPortfolioResult> GetPortfolioAsync(string clientId, bool includeCurrencies = true, 
            bool includeMaxBuySell = true, bool includeMoney = true, bool includePositions = true)
        {

            try
            {
                var res = await _portfoliosClient.GetPortfolioAsync(new GetPortfolioRequest()
                {
                    ClientId = clientId,
                    Content = new PortfolioContent()
                    {
                        IncludeCurrencies = includeCurrencies,
                        IncludeMaxBuySell = includeMaxBuySell,
                        IncludeMoney = includeMoney,
                        IncludePositions = includePositions,
                    }
                }, _metadata).ConfigureAwait(false);
                return res;
            }
            catch (Exception ex)
            {
                NewLog?.Invoke("портфели ошибка " + ex.Message);
                Debug.WriteLine(ex.Message);
                return null;
            }
        }




        /// <summary>
        /// Получение заявок
        /// </summary>
        public async Task<GetOrdersResult> GetOrdersAsync(string clientId, bool includeActive = true,
            bool includeCanceled = true, bool includeMatched = true)
        {
            var res = await _ordersClient.GetOrdersAsync(new GetOrdersRequest()
            {
                ClientId= clientId,
                IncludeActive = includeActive,
                IncludeCanceled = includeCanceled,
                IncludeMatched = includeMatched,
            }, _metadata).ConfigureAwait(false);
            return res;
        }

        /// <summary>
        /// Выставление новой заявки
        /// </summary>
        public async Task<NewOrderResult> NewOrderAsync(string clientId, string secBoard, string secCode, 
            BuySell direction, int quantity, double? price)
        {
            try
            {
                var res = await _ordersClient.NewOrderAsync(new NewOrderRequest
                {
                    ClientId = clientId,
                    SecurityBoard = secBoard,
                    SecurityCode = secCode,
                    BuySell = direction,
                    Quantity = quantity,
                    Price = price,
                    Property = OrderProperty.PutInQueue,

                }, _metadata).ConfigureAwait(false);
                return res;
            }
            catch (Exception ex)
            {
                NewLog?.Invoke($"Ошибка выставления заявки {ex.Message}") ;
                return null;
            }
        }

        public Action<NewStopRequest> FailedStopOrder;
        public async Task<NewStopResult> PlaceStopOrder(string clientId, string secBoard, string secCode,
            BuySell direction, int quantity, double? price)
        {

            // var clientid =
            NewStopRequest stoprequest=null;
           try
           {
                stoprequest = new NewStopRequest
               {
                   ClientId = clientId,
                   SecurityBoard = secBoard,
                   SecurityCode = secCode,
                   BuySell = direction,
                   ValidBefore = new OrderValidBefore() { Type = OrderValidBeforeType.TillCancelled },
                   
                   StopLoss = new StopLoss()
                   {
                       ActivationPrice = (double)price,
                       MarketPrice = true,
                       Price = 0,
                      
                       //todo вернуть 
                       //Time = 100,

                       Quantity = new StopQuantity()
                       {
                           Units = StopQuantityUnits.Lots,
                           Value = quantity
                       },

                   },


               };
               return await _stopsClient.NewStopAsync(stoprequest, _metadata);
           }
           catch (Exception ex)
           {
               NewLog?.Invoke($"{secCode} выставление ошибка " + ex.Message);
               if (stoprequest != null)
               {
                   NewLog?.Invoke($"{secCode} Отправляем на повторное выставление -> 3 сек " + ex.Message);
                   FailedStopOrder?.Invoke(stoprequest);
               }
               Debug.WriteLine(ex.Message);
               return null;
           }

        }

        public async Task<NewStopResult> PlaceRepeatStopOrder(NewStopRequest stoprequest)
        {
            try
            {
                //stoprequest.StopLoss.ActivationPrice = 2.0230341231;
                var r=  await _stopsClient.NewStopAsync(stoprequest, _metadata);

                if (r != null)
                {
                    NewLog($"{r.SecurityCode} / {r.ClientId} / Повторное выставление стопа успешно!");

                }
                
                return r;

            }
            catch (Exception ex)
            {
                NewLog?.Invoke($"{stoprequest.SecurityCode} выставление ошибка " + ex.Message);
                if (stoprequest != null)
                {
                    NewLog?.Invoke($"{stoprequest.SecurityCode} Отправляем на повторное выставление " + ex.Message);
                    FailedStopOrder?.Invoke(stoprequest);
                }
                Debug.WriteLine(ex.Message);
                return null;

            }

        }

        public async Task<CancelStopResult> CancelStopOrder(string clientid, int stopid)
        {
            try
            {
                return await _stopsClient.CancelStopAsync(new CancelStopRequest()
                {
                    ClientId = clientid,
                    StopId = stopid,
                }, _metadata);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                NewLog?.Invoke("отмена ордера ошибка " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Отмена заявки
        /// </summary>
        public async Task<CancelOrderResult> CancelOrderAsync(string clientId, int transactionId)
        {
            var res = await _ordersClient.CancelOrderAsync(new CancelOrderRequest
            {
                ClientId = clientId,
                TransactionId = transactionId
            }, _metadata).ConfigureAwait(false);
            return res;
        }

        /// <summary>
        /// Получение стоп-заявок
        /// </summary>
        public async Task<GetStopsResult> GetStopsAsync(string clientId, bool includeActive = true,
            bool includeCanceled = true, bool IncludeExecuted = true)
        {
            try
            {
                var res = await _stopsClient.GetStopsAsync(new GetStopsRequest()
                {
                    ClientId = clientId,
                    IncludeActive = includeActive,
                    IncludeCanceled = includeCanceled,
                    IncludeExecuted = IncludeExecuted,
                }, _metadata).ConfigureAwait(false);

                return res;
            }
            catch (Exception ex)
            {
                NewLog?.Invoke("получение стопов ошибка " + ex.Message);
                Debug.WriteLine(ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Подписка на биржевой стакан
        /// </summary>
        public async Task SubscribeOrderBookAsync(string secBoard, string secCode, string? requestId = null)
        {
            await _eventsStream.RequestStream.WriteAsync(new SubscriptionRequest()
            {
                OrderBookSubscribeRequest = new OrderBookSubscribeRequest
                {
                    RequestId = requestId ?? GetRandomId(),
                    SecurityBoard = secBoard,
                    SecurityCode = secCode
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Удаление подписки на биржевой стакан
        /// </summary>
        public async Task UnsubscribeOrderBookAsync(string secBoard, string secCode, string? requestId = null)
        {
            await _eventsStream.RequestStream.WriteAsync(new SubscriptionRequest()
            {
                OrderBookUnsubscribeRequest = new OrderBookUnsubscribeRequest
                {
                    RequestId = requestId ?? GetRandomId(),
                    SecurityBoard = secBoard,
                    SecurityCode = secCode
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Подписка на заявки и сделки
        /// </summary>
        public async Task SubscribeOrderTradeAsync(IEnumerable<string> cliendIds, bool includeOrders = true,
            bool includeTrades = true, string? requestId = null)
        {
            await _eventsStream.RequestStream.WriteAsync(new SubscriptionRequest()
            {
                OrderTradeSubscribeRequest = new OrderTradeSubscribeRequest
                {
                    RequestId = requestId ?? GetRandomId(),
                    ClientIds = { cliendIds },
                    IncludeOrders = includeOrders,
                    IncludeTrades = includeTrades,
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Удаление подписки на заявки и сделки
        /// </summary>
        public async Task UnsubscribeOrderTradeAsync(string requestId)
        {
            await _eventsStream.RequestStream.WriteAsync(new SubscriptionRequest()
            {
                OrderTradeUnsubscribeRequest = new OrderTradeUnsubscribeRequest
                {
                    RequestId = requestId
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Получить следующий уникальный id для запроса
        /// </summary>
        public string GetRandomId()
        {
            lock (_lock)
            {
                var res = $"{DateTime.Now:yyMMddHHmmss}_{(_requestCounter++ % 1000).ToString().PadLeft(3, '0')}";
                return res;
            }
        }

        private void RunStream(IAsyncStreamReader<Event> stream)
        {
            Task.Factory.StartNew(async () =>
            {
                await foreach (var response in stream.ReadAllAsync().ConfigureAwait(false))
                {
                    EventResponse?.Invoke(response);
                }
            }, TaskCreationOptions.LongRunning);
        }
    }
}

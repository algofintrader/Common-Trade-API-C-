using FinamConnector.Models;
using System.Diagnostics;
using System.Windows;

namespace FinamExample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Config _config;
        private FinamConnector.Connectors.FinamConnector _connector;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InsideGraphic(Action actionInLog)
        {
            if (Application.Current != null)
                Application.Current.Dispatcher.BeginInvoke(new Action(() => { actionInLog?.Invoke(); }));
        }

        public async void LogMessage(string message)
        {

            try
            {

                //if (message == _prevLogmessage) return;

                var dt = DateTime.Now;
                var datetime = dt.ToString("H:mm:ss.fff");
                var logmessage = datetime + " | " + message;


                InsideGraphic(new Action(() =>
                    {
                        LogTextBox.AppendText(logmessage + Environment.NewLine);
                        LogTextBox.ScrollToEnd();
                    }));

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }

        private void StartStopAll_Click(object sender, RoutedEventArgs e)
        {
             _config = new Config()
            {
                //Ключ не бесконечный, работает какое то время.. 
                Token = "CAEQxoWuAhoY/ky12uZYIubcy65GP28dOVcGYI0TZlR9",
                //Работаем со множественными кодами, код вы можете получить у Common Trade API
                ClientIds = new List<string>()
                {

                    {"408868****"}, //заменить на свой!

                    //можно добавить еще счета
                   // {"408****"},
                   // {"408***" }
                },
            };


            _connector = new FinamConnector.Connectors.FinamConnector(_config)
            {
                //каждые 3 секунды обновляем стопы
                StopTimer = 3000,
                //каждые 5 секунд обновляем позиции
                PortfolioTimer = 5000,
            };

            _connector.UpdatedPositions += (positions, clientID) =>
            {
                //обновление по позициям. 
                foreach(var pos in positions)
                {
                    decimal currentprice = (decimal)pos.Value.CurrentPrice ;
                    Debug.WriteLine($" {pos.Value.Symbol} Поза {pos.Value.Balance} clientID ={pos.Value.ClientID} Цена {pos.Value.CurrentPrice}");
                }
            };

            _connector.Error += error =>
            {
                LogMessage($"Error (ошибка)-> {error.Message}");
            };
            _connector.NewCriticalError += (message) =>
            {
                LogMessage(message);
            };
            _connector.NewLogMessage += LogMessage;

            _connector.Connect();
        }

        /// <summary>
        /// Отменить все активные стоп ордера
        /// </summary>
        /// <returns></returns>
        private async Task TryToFindAndCancelStopOrders()
        {
            try
            {
                //пример с фильтрацией 
                //var stoporders = _connector.ActiveStops.ToList().Where(s => s.SecurityCode == Symbol && s.ClientId == ClientId);

                var stoporders = _connector.ActiveStops.ToList();

                if (stoporders != null)
                    foreach (var stop in stoporders)
                    {
                        var r = await _connector.CancelStopOrder(stop.StopId, stop.ClientId);
                        if (r != null)
                            LogMessage($"Стоп успешно удален {r.StopId} | clientID = {r.ClientId}");

                        if (r == null)
                            LogMessage($"Стоп НЕ БЫЛ удален {stop.StopId} | clientID = {stop.ClientId}");
                    }
            }
            catch (Exception ex)
            {
                LogMessage("ошибка ->" + ex.Message);
            }
        }

        private async void SendOrder_Click(object sender, RoutedEventArgs e)
        {
            if (_connector == null)
                return;

            //Клиент ID можно взять из конфига... тут прописан просто напрямик
            var res = await _connector.SendOrderAsync("408868R3LLJ", "SNGS", Finam.TradeApi.Proto.V1.BuySell.Buy, 1,30);

            if (res != null)
            {
                LogMessage($"Результат выставление заявки {res.TransactionId}");
            }
        }


    }
}
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Finam.TradeApi.Proto.V1;
using FinamConnector.Models;

namespace FinamConnector.Connectors
{
    /// <summary>
    /// Интерфейс взаимодействия с брокером
    /// </summary>
    public interface IConnector : IDisposable
    {
        /// <summary>Событие обновления списка денег</summary>
        event Action UpdateMoneys;

        /// <summary>Событие обновления списка позиций</summary>
        event Action UpdatePositions;

        /// <summary>Событие обновления заявки</summary>
        event Action<Models.Order> UpdateOrder;

        /// <summary>Событие обновления стакана</summary>
        event Action<OrderBook> UpdateOrderBook;

        /// <summary>Событие обновления информации по инструменту</summary>
        event Action<FinInfo> UpdateFinInfo;

        /// <summary>Ошибка в коннекторе</summary>
        event Action<Exception> Error;
        
        /// <summary>Сообщение из коннектора</summary>
        event Action<string> NewLogMessage;


        /// <summary>Деньги</summary>
        IDictionary<string, Money> Moneys { get; }

        /// <summary>Позиции</summary>
        //IDictionary<string, Position> Positions { get; }

        /// <summary>Заявки</summary>
        IDictionary<string, Models.Order> Orders { get; }


        /// <summary>Подключение, получение данных, подписка на данные</summary>
        Task Connect();

        /// <summary>Отправить заявку</summary>
      Task<NewOrderResult> SendOrderAsync(string account,string symbol, BuySell direction, double quantity, double price);

        /// <summary>Отменить заявку</summary>
        Task CancelOrderAsync(Models.Order order, string clientId);
    }
}

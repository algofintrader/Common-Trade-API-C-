using System.Collections.Generic;

namespace FinamConnector.Models
{
    public class Config
    {
        /// <summary>Токен от Trade API</summary>
        public string Token { get; set; }

        /// <summary>Торговый код клиента</summary>
        public List <string> ClientIds { get; set; }
    }
}

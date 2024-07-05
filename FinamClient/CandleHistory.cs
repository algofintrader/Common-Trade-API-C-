using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StockSharp.Messages;
using System.Runtime.CompilerServices;
//using DataType = System.ComponentModel.DataAnnotations.DataType;

namespace FinamClient
{

    public static class StringExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (string left, string right) SplitAt(this string text, int index) =>
            (text.Substring(0, index), text.Substring(index));
    }
    /// <summary>
    /// У меня не получилось почему то сделать нормальный proto запрос
    /// поэтому я сделал обычный 
    /// </summary>

    public partial class CandleRequest
    {
        [JsonProperty("data")]
        public Data Data { get; set; }

        [JsonProperty("error")]
        public object Error { get; set; }
    }

    public partial class Data
    {
        [JsonProperty("candles")]
        public Candle[] Candles { get; set; }
    }

    public partial class Candle : ICandleMessage
    {
        public void SetCandle()
        {
            DataType = DataType.CandleTimeFrame;
            State = CandleStates.Finished;

            OpenTime = CloseTime = HighTime = LowTime = Timestamp;

            OpenPrice = Open.Value;
            HighPrice = High.Value;
            LowPrice = Low.Value;
            ClosePrice = Close.Value;

            TotalVolume = Volume;
        }

        [JsonProperty("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonProperty("open")]
        public CandleValue Open { get; set; }

        [JsonProperty("close")]
        public CandleValue Close { get; set; }

        [JsonProperty("high")]
        public CandleValue High { get; set; }

        [JsonProperty("low")]
        public CandleValue Low { get; set; }

        [JsonProperty("volume")]
        public long Volume { get; set; }

        public SecurityId SecurityId { get; set; }
        public long SeqNum { get; set; }
        public DataType BuildFrom { get; set; }
        public DateTimeOffset LocalTime { get; }
        public DateTimeOffset ServerTime { get; set; }


        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }


        public DateTimeOffset OpenTime { get; set; }
        public DateTimeOffset CloseTime { get; set; }
        public DateTimeOffset HighTime { get; set; }
        public DateTimeOffset LowTime { get; set; }
        public CandleStates State { get; set; }
        public IEnumerable<CandlePriceLevel> PriceLevels { get; set; }
        public DataType DataType { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal? OpenVolume { get; set; }
        public decimal? CloseVolume { get; set; }
        public decimal? HighVolume { get; set; }
        public decimal? LowVolume { get; set; }
        public decimal TotalVolume { get; set; }
        public decimal? RelativeVolume { get; set; }
        public decimal? BuyVolume { get; set; }
        public decimal? SellVolume { get; set; }
        public int? TotalTicks { get; set; }
        public int? UpTicks { get; set; }
        public int? DownTicks { get; set; }
        public decimal? OpenInterest { get; set; }
    }

    public partial class CandleValue
    {
        [JsonProperty("num")]
        public long Num { get; set; }

        [JsonProperty("scale")]
        public long Scale { get; set; }

        public decimal Value
        {
            get
            {

                var priceString = Num.ToString();

                if (Scale != 0)
                {
                    long maxlength = priceString.Length;
                    string finalpricestring = "";

                    try
                    {

                        if (Scale < maxlength)
                        {
                            var (left, right) = priceString.SplitAt((int)(maxlength - Scale));
                            finalpricestring = left + "." + right;
                        }
                        else //scale >= maxlength
                        {
                            var diff = Scale - maxlength;

                            finalpricestring += "0.";

                            for (int i = 0; i < diff; i++)
                            {
                                finalpricestring += "0";
                            }

                            finalpricestring += priceString;
                        }



                    }
                    catch (Exception ex)
                    {

                    }


                    return decimal.Parse(finalpricestring, new CultureInfo("en-US"));
                }


                return Num;


            }
        }
    }


    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

   
}

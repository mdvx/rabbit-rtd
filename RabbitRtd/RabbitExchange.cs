using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitRtd
{
    class RabbitExchange
    {
        public readonly string Exchange;
        public readonly string Type;
        public readonly bool Durable;
        public readonly bool AutoDelete;
        public readonly IDictionary<string, object> Arguments;

        public RabbitExchange(string exchangeString)
        {
            //  [name[:type[:durability[:auto-delete[:arguments]]]]]
            var arr = exchangeString.Split(':');
            Exchange = arr.Length > 0 ? arr[0] : string.Empty;
            Type = arr.Length > 1 ? arr[1] : "direct";
            Durable = arr.Length > 2 ? Boolean.Parse(arr[2]) : false;
            AutoDelete = arr.Length > 3 ? Boolean.Parse(arr[3]) : false;
            Arguments = arr.Length > 4 ? JObject.Parse(arr[4]).ToObject<Dictionary<string, object>>() : null;
        }
    }
}

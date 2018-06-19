using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitRtd
{
    class RabbitQueue
    {
        public readonly string Name;
        public readonly bool Durable;
        public readonly bool Exclusive;
        public readonly bool AutoDelete;
        public readonly IDictionary<string, object> Arguments;

        public RabbitQueue(string str)
        {
            //  [name[:type[:durability[:auto-delete[:arguments]]]]]
            var arr = str.Split(':');
            Name = arr.Length > 0 ? arr[0] : string.Empty;
            Durable = arr.Length > 1 ? Boolean.Parse(arr[1]) : true;
            Exclusive = arr.Length > 2 ? Boolean.Parse(arr[2]) : false;
            AutoDelete = arr.Length > 3 ? Boolean.Parse(arr[3]) : false;
            Arguments = arr.Length > 4 ? JObject.Parse(arr[4]).ToObject<Dictionary<string, object>>() : null;
        }
    }
}

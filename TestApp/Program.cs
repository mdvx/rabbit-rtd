using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NLog;
using RabbitMQ.Client;
using RabbitRtd;

namespace RabbitRtd
{
    class Program : IRtdUpdateEvent
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        [STAThread]
        public static void Main (string[] args)
        {
            var me = new Program();

            IRtdUpdateEvent me2 = me;
            //me2.HeartbeatInterval = 15;  // is this seconds or milliseconds?
            
            me.Run();
        }

        IRtdServer _rtd;
        bool consoleAppTest = false;   // false: test with excel, true: test with console app
        Random random = new Random();

        void Run ()
        {
            _rtd = new RabbitRtdServer();
            _rtd.ServerStart(this);

            CancellationTokenSource cts = new CancellationTokenSource();

            for (int i = 0; i < 3; i++)
            {
                var rk = "ROUTING_KEY_" + i;
                var b = i % 2 == 0;
                Task.Run(() => PublishRabbit("EXCHANGE", rk, "FIELD", b, cts.Token));

                if (consoleAppTest)
                    Sub("EXCHANGE", rk, "FIELD");
            }

            // Start up a Windows message pump and spin forever.
            Dispatcher.Run();
        }
        void PublishRabbit(string exchange, string routingKey, string field, bool json, CancellationToken cts)
        {
            try
            {
                var factory = new ConnectionFactory() { HostName = "localhost" };
                IConnection connection = factory.CreateConnection();

                using (var channel = connection.CreateModel())
                {
                    channel.ExchangeDeclare(exchange: exchange, type: "topic", autoDelete: true);
                    //channel.BasicQos = 100;

                    var padding = new String('x', 200);

                    int l = 0;
                    while (!cts.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref l);

                        var str = json ? String.Format("{{ \"rk\": \"{0}\", \"{1}\": {2}, \"padding\": \"{3}\"}}", routingKey, field, l,padding)   // alternate between JSON
                                       : String.Format($"{routingKey} => {field}: {l} {padding}");         // not JSON

                        channel.BasicPublish(exchange: exchange,
                            routingKey: routingKey,
                            basicProperties: null,
                            mandatory: true,
                            body: Encoding.ASCII.GetBytes(str));

                        if (l % 4999 == 0) {  // 4999 is prime
                            Logger.Debug("sending " + str.Substring(0,Math.Min(75,str.Length)));
                            Console.Write($"{l}\r");

                            var d = random.NextDouble();
                            var e = random.Next(5);
                            var r = d * Math.Pow(10, e);  // r should fall between 0 and 4*100,000

                            padding = new String('x', (int)r);
                        }
                    }

                    channel.Close(); 
                }
            }
            catch (Exception e)
            {
                Logger.Error(e,"SubscribeRabbit");
            }
        }

        int _topic;
        void Sub (string exchange, string routingKey, string field)
        {
            Console.WriteLine($"Subscribing: topic={_topic}, exchange={exchange}, routingKey={routingKey}, field={field}");
            
            var a = new[]
                    {
                        "localhost",
                        exchange,
                        routingKey,
                        field
                    };

            Array crappyArray = a;

            bool newValues = false;
            _rtd.ConnectData(_topic++, ref crappyArray, ref newValues);
        }


        void IRtdUpdateEvent.UpdateNotify ()
        {
            Console.WriteLine("UpdateNotified called ---------------------");

            int topicCount = 0;
            var values = _rtd.RefreshData(ref topicCount);

            for (int i = 0; i < topicCount; ++i)
            {
                Console.WriteLine( values.GetValue(0, i).ToString() + '\t' + values.GetValue(1, i).ToString());
            }
        }

        int IRtdUpdateEvent.HeartbeatInterval { get; set; }
        
        void IRtdUpdateEvent.Disconnect ()
        {
            Logger.Debug("Disconnect called.");
        }
    }
}

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RabbitMQ.Client;
using RabbitRtd;

namespace TestApp
{
    class Program : IRtdUpdateEvent
    {
        [STAThread]
        public static void Main (string[] args)
        {
            var me = new Program();

            IRtdUpdateEvent me2 = me;
            me2.HeartbeatInterval = 100;
            
            me.Run();
        }

        IRtdServer _rtd;

        void Run ()
        {
            _rtd = new RabbitRtdServer();
            _rtd.ServerStart(this);

            CancellationTokenSource cts = new CancellationTokenSource();

            for (int i = 0; i < 3; i++)
            {
                var rk = "ROUTING_KEY_" + i;
                Task.Run(() => PublishRabbit("EXCHANGE", rk, "FIELD", cts.Token));

                if (false)
                    Sub("EXCHANGE", rk, "FIELD");
            }

            // Start up a Windows message pump and spin forever.
            Dispatcher.Run();
        }
        void PublishRabbit(string exchange, string routingKey, string field, CancellationToken cts)
        {
            try
            {
                var factory = new ConnectionFactory() { HostName = "localhost" };
                IConnection connection = factory.CreateConnection();

                using (var channel = connection.CreateModel())
                {
                    channel.ExchangeDeclare(exchange: exchange, type: "topic", autoDelete: true);
                    //channel.BasicQos = 100;

                    int l = 0;
                    while (!cts.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref l);

                        var str = String.Format("{{ rk: '{0}', {1}: {2} }}", routingKey, field, l);
                        channel.BasicPublish(exchange: exchange,
                            routingKey: routingKey,
                            basicProperties: null,
                            mandatory: true,
                            body: Encoding.ASCII.GetBytes(str));

                        Console.WriteLine("sending " + str);

                        Thread.Sleep(500);
                    }

                    channel.Close();
                }
            }
            catch (Exception e)
            {
                ESLog.Error("SubscribeRabbit", e);
            }
        }

        int _topic;
        void Sub (string exchange, string routingKey, string field)
        {
            Console.WriteLine("Subscribing: topic={0}, exchange={1}, routingKey={2}, field={3}", _topic, exchange, routingKey, field);
            
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
                Console.WriteLine("{0}\t{1}", values.GetValue(0, i), values.GetValue(1, i));
            }
        }

        int IRtdUpdateEvent.HeartbeatInterval { get; set; }
        
        void IRtdUpdateEvent.Disconnect ()
        {
            Console.WriteLine("Disconnect called.");
        }
    }
}

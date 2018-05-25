using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RabbitRtd
{
    [
        Guid("F4B38467-3B63-49AC-8898-D09B2F68235A"),
        // This is the string that names RTD server.
        // Users will use it from Excel: =RTD("crypto",, ....)
        ProgId("rabbit")
    ]
    public class RabbitRtdServer : IRtdServer
    {
        IRtdUpdateEvent _callback;
        DispatcherTimer _timer;
        readonly SubscriptionManager _subMgr;


        public RabbitRtdServer ()
        {
            _subMgr = new SubscriptionManager();
        }
        // Excel calls this. It's an entry point. It passes us a callback
        // structure which we save for later.
        int IRtdServer.ServerStart (IRtdUpdateEvent callback)
        {
            _callback = callback;

            // We will throttle out updates so that Excel can keep up.
            // It is also important to invoke the Excel callback notify
            // function from the COM thread. System.Windows.Threading' 
            // DispatcherTimer will use COM thread's message pump.
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50); // this needs to be very frequent
            _timer.Tick += TimerElapsed;
            _timer.Start();

            return 1;
        }

        // Excel calls this when it wants to shut down RTD server.
        void IRtdServer.ServerTerminate ()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
        }

        // Excel calls this when it wants to make a new topic subscription.
        // topicId becomes the key representing the subscription.
        // String array contains any aux data user provides to RTD macro.
        object IRtdServer.ConnectData (int topicId,
                                       ref Array strings,
                                       ref bool newValues)
        {
            newValues = true;

            // We assume 3 strings: Origin, Instrument, Field
            if (strings.Length == 4)
            {
                // Crappy COM-style arrays...
                string host = strings.GetValue(0).ToString();
                string exchange = strings.GetValue(1).ToString();
                string instrument = strings.GetValue(2).ToString();
                string field = strings.GetValue(3).ToString();

                lock (_subMgr)
                {
                    // Let's use Empty strings for now
                    _subMgr.Subscribe(
                        topicId,
                        exchange,
                        instrument, 
                        field);
                }

                CancellationTokenSource cts = new CancellationTokenSource();
                Task.Run(() => SubscribeRabbit(host, exchange, instrument, field, cts));

                return SubscriptionManager.UninitializedValue;
            }

            return "ERROR: Expected: host, queue, topic, field";
        }

        private void SubscribeRabbit(string host, string exchange, string routingKey, string field, CancellationTokenSource cts)
        {
            var rtdTopicString = SubscriptionManager.FormatPath(exchange, routingKey, field);

            var factory = new ConnectionFactory() { HostName = host };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.ExchangeDeclare(exchange: exchange, type: "fanout");

                var queueName = channel.QueueDeclare().QueueName;
                channel.QueueBind(queue: queueName,
                                  exchange: exchange,
                                  routingKey: routingKey);

                var consumer = new EventingBasicConsumer(channel);
                consumer.Received += (model, ea) =>
                {
                    var json = Encoding.UTF8.GetString(ea.Body);
                    var data = JsonConvert.DeserializeObject<Dictionary<String,String>>(json);

                    _subMgr.Set(rtdTopicString, data[field]);

                };
                channel.BasicConsume(queue: queueName,
                                     autoAck: true,
                                     consumer: consumer);

                while (!cts.Token.IsCancellationRequested)
                    Thread.Sleep(1000);
            }
        }

        // Excel calls this when it wants to cancel subscription.
        void IRtdServer.DisconnectData (int topicId)
        {
            lock (_subMgr)
            {
                _subMgr.Unsubscribe(topicId);
            }
        }

        // Excel calls this every once in a while.
        int IRtdServer.Heartbeat ()
        {
            return 1;
        }

        // Excel calls this to get changed values. 
        Array IRtdServer.RefreshData (ref int topicCount)
        {
            var updates = GetUpdatedValues();
            topicCount = updates.Count;

            object[,] data = new object[2, topicCount];

            for (int i = 0; i < topicCount; ++i)
            {
                SubscriptionManager.UpdatedValue info = updates[i];

                data[0, i] = info.TopicId;
                data[1, i] = info.Value;
            }

            return data;
        }
        
        // Helper function which checks if new data is available and,
        // if so, notifies Excel about it.
        private void TimerElapsed (object sender, EventArgs e)
        {
            bool wasMarketDataUpdated;

            lock (_subMgr)
            {
                wasMarketDataUpdated = _subMgr.IsDirty;
            }

            if (wasMarketDataUpdated)
            {
                // Notify Excel that Market Data has been updated
                _callback.UpdateNotify();
            }
        }

        List<SubscriptionManager.UpdatedValue> GetUpdatedValues ()
        {
            lock (_subMgr)
            {
                return _subMgr.GetUpdatedValues();
            }
        }
    }

}

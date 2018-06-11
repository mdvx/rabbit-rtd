﻿using Newtonsoft.Json;
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
        Dictionary<string, IConnection> _connections = new Dictionary<string, IConnection>();

        private const string CLOCK = "CLOCK";
        private const string LAST_RTD = "LAST_RTD";

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
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            _timer = dispatcherTimer;
            _timer.Interval = TimeSpan.FromMilliseconds(33); // this needs to be very frequent
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

            lock(_subMgr)
            {
                _callback.UpdateNotify();
            }
            //Thread.Sleep(2000);
        }

        // Excel calls this when it wants to make a new topic subscription.
        // topicId becomes the key representing the subscription.
        // String array contains any aux data user provides to RTD macro.
        object IRtdServer.ConnectData (int topicId, ref Array strings, ref bool newValues)
        {
            if (strings.Length == 1)
            {
                string host = strings.GetValue(0).ToString().ToUpperInvariant();

                switch (host)
                {
                    case CLOCK:
                        lock (_subMgr)
                            _subMgr.Subscribe(topicId, CLOCK);

                        return DateTime.Now.ToLocalTime();

                    case LAST_RTD:
                        lock (_subMgr)
                            _subMgr.Subscribe(topicId, LAST_RTD);

                        return DateTime.Now.ToLocalTime();
                        //return SubscriptionManager.UninitializedValue;
                }
                return "ERROR: Expected: CLOCK or host, exchange, routingKey, field";
            }
            else if (strings.Length >= 3)
            {
                newValues = true;

                // Crappy COM-style arrays...
                string host = strings.GetValue(0).ToString();
                string exchange = strings.GetValue(1).ToString();
                string routingKey = strings.GetValue(2).ToString();
                string field = strings.Length >= 4 ? strings.GetValue(3).ToString() : null;

                CancellationTokenSource cts = new CancellationTokenSource();
                Task.Run(() => SubscribeRabbit(topicId, host, exchange, routingKey, field, cts.Token));

                return SubscriptionManager.UninitializedValue;
            }

            newValues = false;

            return "ERROR: Expected: CLOCK or host, exchange, routingKey, field";
        }

        private void SubscribeRabbit(int topicId, string host, string exchange, string routingKey, string field, CancellationToken cts)
        {
            try
            {
                lock (_subMgr)
                {
                    if (_subMgr.Subscribe(topicId, host, exchange, routingKey, field))
                        return; // already subscribed 
                }

                if (!_connections.TryGetValue(host, out IConnection connection))
                {
                    var factory = new ConnectionFactory() { HostName = host };
                    connection = factory.CreateConnection();
                    lock (_connections)
                    {
                        _connections[host] = connection;
                    }
                }

                using (var channel = connection.CreateModel())
                {
                    channel.ExchangeDeclare(exchange: exchange, type: "topic", autoDelete: true);
                    //channel.BasicQos = 100;

                    var queueName = channel.QueueDeclare().QueueName;
                    channel.QueueBind(queue: queueName,
                                      exchange: exchange,
                                      routingKey: routingKey);

                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        if (ea.RoutingKey.Equals(routingKey))
                        {
                            var str = Encoding.UTF8.GetString(ea.Body);
                            var rtdSubTopic = SubscriptionManager.FormatPath(host, exchange, routingKey);

                            _subMgr.Set(rtdSubTopic, str);

                            try
                            {
                                var json = JsonConvert.DeserializeObject<Dictionary<String, String>>(str);

                                foreach (string field_in in json.Keys)
                                {
                                    var rtdTopicString = SubscriptionManager.FormatPath(host, exchange, routingKey, field_in);
                                    _subMgr.Set(rtdTopicString, json[field_in]);
                                }
                            }
                            catch (Exception e)
                            {
                                _subMgr.Set(rtdSubTopic, e.Message);
                            }
                        }
                    };
                    channel.BasicConsume(queue: queueName,
                                         autoAck: true,
                                         consumer: consumer);

                    while (!cts.IsCancellationRequested)
                        Thread.Sleep(1000);

                    channel.Close();
                }
            }
            catch(Exception e)
            {
                ESLog.Error("SubscribeRabbit", e);
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
            bool wasDataUpdated;

            lock (_subMgr)
            {
                wasDataUpdated = _subMgr.IsDirty;
            }

            if (wasDataUpdated)
            {
                // Notify Excel that Market Data has been updated
                _subMgr.Set(LAST_RTD, DateTime.Now.ToLocalTime());
                _callback.UpdateNotify();
            }

            if (_subMgr.Set(CLOCK, DateTime.Now.ToLocalTime()))
                _callback.UpdateNotify();
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

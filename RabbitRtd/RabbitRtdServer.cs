using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
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
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private IRtdUpdateEvent _callback;
        private bool _isExcelNotifiedOfUpdates = false;
        private object _notifyLock = new object();
        private readonly SubscriptionManager _subMgr;
        private Dictionary<string, Uri> hostUriMap = new Dictionary<string, Uri>();  // quick lookup of existing host strings
        private Dictionary<Uri, IConnection> _connections = new Dictionary<Uri, IConnection>();

        // stats
        private DateTime _startTime;
        private long _refreshes;
        private int _updatesTopic, _refreshesTopic, _distinctUpdatesTopic, _startTimeTopic;

        private const string CLOCK = "RTD_CLOCK";
        private const string LAST_RTD = "RTD_LAST";
        private const string START_TIME = "RTD_START_TIME";
        private const string UPDATES = "RTD_UPDATES";
        private const string REFRESHES = "RTD_REFRESHES";
        private const string DISTINCT = "RTD_DISTINCT";

        public RabbitRtdServer ()
        {
            _subMgr = new SubscriptionManager( () => {
                if (!_isExcelNotifiedOfUpdates)
                {
                    lock (_notifyLock)                     // do this before calling updateNotify
                        _isExcelNotifiedOfUpdates = true;  // so next update will reset this to FALSE even if excel has NOT called RefreshData

                    _callback.UpdateNotify();
                }
            });
        }
        // Excel calls this. It's an entry point. It passes us a callback
        // structure which we save for later.
        int IRtdServer.ServerStart (IRtdUpdateEvent callback)
        {
            _callback = callback;
            _startTime = DateTime.Now.ToLocalTime();

            lock (_notifyLock)
                _isExcelNotifiedOfUpdates = false;

            return 1;
        }

        // Excel calls this when it wants to shut down RTD server.
        void IRtdServer.ServerTerminate ()
        {
            //_subMgr.UnsubscribeAll();
        }

        // Excel calls this when it wants to make a new topic subscription.
        // topicId becomes the key representing the subscription.
        // String array contains any aux data user provides to RTD macro.
        object IRtdServer.ConnectData (int topicId, ref Array strings, ref bool newValues)
        {
            newValues = true;

            if (strings.Length == 1)
            {
                switch(strings.GetValue(0).ToString().ToUpperInvariant())
                {
                    case START_TIME:
                        _startTimeTopic = topicId;
                        return _startTime;
                    case UPDATES:
                        _updatesTopic = topicId;
                        return _subMgr.UpdateCount;
                    case DISTINCT:
                        _distinctUpdatesTopic = topicId;
                        return _subMgr.DistinctUpdateCount;
                    case REFRESHES:
                        _refreshesTopic = topicId;
                        return _refreshes;
                }
                return $"Expected: {START_TIME} {UPDATES} {DISTINCT} {REFRESHES}";
            }
            else if (strings.Length > 2)
            {
                newValues = true;

                // Crappy COM-style arrays...
                string host = strings.GetValue(0).ToString();
                string exchange = strings.GetValue(1).ToString();
                string queue = strings.Length > 2 ?  strings.GetValue(2).ToString() : null;
                string routingKey = strings.Length > 3 ? strings.GetValue(3).ToString() : null;
                string field = strings.Length > 4 ? strings.GetValue(4).ToString() : null;

                return PreSubscribeRabbit(topicId, host, exchange, queue, routingKey, field);
            }

            return "Expected: host, exchange, routingKey, field";
        }

        private object PreSubscribeRabbit(int topicId, string host, string exchangeString, string queue, string routingKey, string field)
        {
            if (!hostUriMap.TryGetValue(host, out Uri hostUri))
            {
                //{ Scheme = "amqp", UserName = "guest", Password = "guest", Host = "localhost", Port = 5672 };
                //[amqp://][userName:password@]hostName[:portNumber][/virtualHost]
                UriBuilder b1 = new UriBuilder(host);
                UriBuilder b2 = new UriBuilder()
                {
                    Scheme = b1.Scheme == "http" ? "amqp" : b1.Scheme,  // TODO: FIX
                    Host = b1.Host ?? "localhost",
                    Port = b1.Port == 80 ? 5672 : b1.Port,  // TODO: FIX
                    UserName = b1.UserName ?? "guest",
                    Password = b1.Password ?? "guest",
                    Path = b1.Path
                };

                hostUri = b2.Uri;

                lock (hostUriMap)
                    hostUriMap[host] = hostUri;
            }
            RabbitExchange rabbitExchange = new RabbitExchange(exchangeString);

            lock (_subMgr)
            {
                if (_subMgr.Subscribe(topicId, hostUri, rabbitExchange.Exchange, routingKey, field))
                    return _subMgr.GetValue(topicId); // already subscribed 
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            Task.Run(() => SubscribeRabbit(topicId, hostUri, rabbitExchange, queue, routingKey, field, cts.Token));
            return SubscriptionManager.UninitializedValue;
        }
        private void SubscribeRabbit(int topicId, Uri hostUri, RabbitExchange rabbitExchange, string queue , string routingKey, string field, CancellationToken cts)
        {
            try
            {
                if (!_connections.TryGetValue(hostUri, out IConnection connection))
                {
                    var factory = new ConnectionFactory() { Uri = hostUri };
                    connection = factory.CreateConnection();

                    lock (_connections)
                        _connections[hostUri] = connection;
                }

                using (var channel = connection.CreateModel())
                {
                    //channel.BasicQos = 100;

                    channel.ExchangeDeclare( 
                        rabbitExchange.Exchange,
                        rabbitExchange.Type, 
                        rabbitExchange.Durable,
                        rabbitExchange.AutoDelete,
                        rabbitExchange.Arguments);

                    var queueName = string.IsNullOrWhiteSpace(queue) ? channel.QueueDeclare().QueueName : queue; //  queue ?? 
                    channel.QueueBind(queue: queueName,
                                      exchange: rabbitExchange.Exchange,
                                      routingKey: routingKey);

                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        if (ea.RoutingKey.Equals(routingKey))
                        {
                            var rtdSubTopic = SubscriptionManager.FormatPath(hostUri, rabbitExchange.Exchange, routingKey);

                            try
                            {
                                var str = Encoding.UTF8.GetString(ea.Body);

                                _subMgr.Set(rtdSubTopic, str);

                                if (str.StartsWith("{"))
                                {
                                    var jo = JsonConvert.DeserializeObject<Dictionary<String, String>>(str);

                                    foreach (string field_in in jo.Keys)
                                    {
                                        var rtdTopicString = SubscriptionManager.FormatPath(hostUri, rabbitExchange.Exchange, routingKey, field_in);
                                        _subMgr.Set(rtdTopicString, jo[field_in]);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                _subMgr.Set(rtdSubTopic, e.Message);
                                Logger.Error(e, "SubscribeRabbit.Received");
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
                Logger.Error(e, "SubscribeRabbit");
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
            return 1;  // Just ACK this
        }

        // Excel calls this to get changed values. 
        Array IRtdServer.RefreshData (ref int topicCount)
        {
            _refreshes++;

            var updates = GetUpdatedValues();
            lock (_notifyLock)
                _isExcelNotifiedOfUpdates = false;

            const int STATS_COUNT = 4;
            topicCount = updates.Count + STATS_COUNT;

            object[,] data = new object[2, topicCount];
            data[0, 0] = _startTimeTopic;  // TODO: check stats are subscribed
            data[1, 0] = _startTime;
            data[0, 1] = _updatesTopic;
            data[1, 1] = _subMgr.UpdateCount;
            data[0, 2] = _distinctUpdatesTopic;
            data[1, 2] = _subMgr.DistinctUpdateCount;
            data[0, 3] = _refreshesTopic;
            data[1, 3] = _refreshes;

            int i = STATS_COUNT;
            foreach(var info in updates)
            {
                data[0, i] = info.TopicId;
                data[1, i] = info.Value;

                i++;
            }

            return data;
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

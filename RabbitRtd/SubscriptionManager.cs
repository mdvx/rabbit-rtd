using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace RabbitRtd
{
    public class SubscriptionManager
    {
        public static readonly string UninitializedValue = "<?>";

        readonly Dictionary<string, SubInfo> _subByRtdPath;
        readonly Dictionary<string, SubInfo> _subByRabbitPath;
        readonly Dictionary<int, SubInfo> _subByTopicId;
        readonly Dictionary<int, SubInfo> _dirtyMap;
        readonly Action _onDirty;
        public long UpdateCount = 0;  // Number of times Set was called for Subscribed topics
        public long DistinctUpdateCount = 0;  // Number of times, the value Set changed

        public SubscriptionManager(Action onDirty)
        {
            _subByRabbitPath = new Dictionary<string, SubInfo>();
            _subByRtdPath = new Dictionary<string, SubInfo>();
            _subByTopicId = new Dictionary<int, SubInfo>();
            _dirtyMap = new Dictionary<int, SubInfo>();
            _onDirty = onDirty;
        }

        public bool IsDirty {
            get {
                return _dirtyMap.Count > 0;
            }
        }

        public bool Subscribe(int topicId, string topic)
        {
            string path = topic;
            var subInfo = new SubInfo(topicId, path);
            _subByTopicId.Add(topicId, subInfo);
            _subByRtdPath.Add(path, subInfo);
            return true;
        }
        public bool Subscribe(int topicId, Uri hostUri, string exchange, string routingKey, string field)
        {
            var topicPath = FormatPath(hostUri, exchange, routingKey, field);
            var rabbitPath = FormatPath(hostUri, exchange, routingKey);

            var alreadySubscribed = false;

            if (_subByTopicId.TryGetValue(topicId, out SubInfo subInfo))
            {
                alreadySubscribed = true;
                subInfo.AddField(field);
            }
            else
            {
                subInfo = new SubInfo(topicId, rabbitPath);
                subInfo.AddField(field);
                _subByTopicId[topicId] = subInfo;
            }

            _subByRabbitPath[rabbitPath] = subInfo;
            _subByRtdPath[topicPath] = subInfo;

            return alreadySubscribed;
        }

        public void Unsubscribe(int topicId)
        {
            if (_subByTopicId.TryGetValue(topicId, out SubInfo subInfo))
            {
                _subByTopicId.Remove(topicId);
                _subByRtdPath.Remove(subInfo.Path);
            }
        }

        public object GetValue(int topicId)
        {
            if (_subByTopicId.TryGetValue(topicId, out SubInfo sub))
                return sub.Value;

            return UninitializedValue;
        }

        public List<UpdatedValue> GetUpdatedValues()
        {
            var updated = new List<UpdatedValue>(_dirtyMap.Count);

            lock (_dirtyMap) { 
                foreach (var subInfo in _dirtyMap.Values)
                {
                    updated.Add(new UpdatedValue(subInfo.TopicId, subInfo.Value));
                }
                _dirtyMap.Clear();
            }

            return updated;
        }

        public bool Set(string path, object value)
        {
            if (_subByRtdPath.TryGetValue(path, out SubInfo subInfo))
            {
                UpdateCount++;

                if (value != subInfo.Value)
                {
                    if (value is string)
                    {
                        var str = value as string;
                        if (str.Length > 32767)
                            subInfo.Value = $"Error: string too long for Excel ({str.Length} > 32767)";
                        else
                            subInfo.Value = value;
                    }
                    else
                        subInfo.Value = value;

                    lock (_dirtyMap)
                    {
                        _dirtyMap[subInfo.TopicId] = subInfo;
                        _onDirty?.Invoke();
                    }
                    DistinctUpdateCount++;
                    return true;
                }
            }
            return false;
        }

        [DebuggerStepThrough]
        public static string FormatPath(Uri host, string exchange, string routingKey, string field=null)
        {
            return string.Format("{0}/{1}/{2}/{3}",
                                host.Host.ToUpperInvariant(),
                                exchange.ToUpperInvariant(),
                                routingKey.ToUpperInvariant(),
                                field);
        }

        public class SubInfo
        {
            public int TopicId { get; private set; }
            public string Path { get; private set; }
            public HashSet<string> Fields { get; private set; }

            public object Value { get; set; }

            public SubInfo(int topicId, string path, object value)
            {
                TopicId = topicId;
                Path = path;
                Value = value;
                Fields = new HashSet<string>();
            }
            public SubInfo(int topicId, string path) : this(topicId, path, UninitializedValue)
            {
            }
            public void AddField(string field)
            {
                Fields.Add(field);
            }
            public override string ToString()
            {
                return $"SubInfo topic={TopicId} path={Path} value={Value}";
            }
        }
        public struct UpdatedValue  // Immutable
        {
            public int TopicId { get; private set; }
            public object Value { get; private set; }

            public UpdatedValue(int topicId, object value)
            {
                TopicId = topicId;

                if (value is String)
                {
                   if (Decimal.TryParse(value.ToString(), out Decimal dec))
                        Value = dec;
                    else
                        Value = value;

                    if (dec > 1500_000_000_000 && dec < 1600_000_000_000)
                        Value = DateTimeOffset
                            .FromUnixTimeMilliseconds(Decimal.ToInt64(dec))
                            .DateTime
                            .ToLocalTime();
                }
                else
                {
                    Value = value;
                }
            }
        }
    }

}

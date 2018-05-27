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

        readonly Dictionary<string, SubInfo> _subByPath;
        readonly Dictionary<string, SubInfo> _subByRabbitPath;
        readonly Dictionary<int, SubInfo> _subByTopicId;

        
        public SubscriptionManager()
        {
            _subByRabbitPath = new Dictionary<string, SubInfo>();
            _subByPath = new Dictionary<string, SubInfo>();
            _subByTopicId = new Dictionary<int, SubInfo>();
        }

        public bool IsDirty { get; private set; }

        public bool Subscribe(int topicId, string host, string exchange, string routingKey, string field)
        {
            var rabbitPath = FormatPath(host, exchange, routingKey);
            var rtdPath = FormatPath(host, exchange, routingKey, field);

            var alreadySubscribed = false;

            if (_subByRabbitPath.TryGetValue(rabbitPath, out SubInfo subInfo))
            {
                alreadySubscribed = true;
                subInfo.addField(field);
            }
            else
            {
                subInfo = new SubInfo(topicId, rabbitPath);
                subInfo.addField(field);
                _subByRabbitPath.Add(rabbitPath, subInfo);
            }

            SubInfo rtdSubInfo = new SubInfo(topicId, rtdPath);
            _subByTopicId.Add(topicId, rtdSubInfo);
            _subByPath.Add(rtdPath, rtdSubInfo);

            return alreadySubscribed;
        }

        public void Unsubscribe(int topicId)
        {
            if (_subByTopicId.TryGetValue(topicId, out SubInfo subInfo))
            {
                _subByTopicId.Remove(topicId);
                _subByPath.Remove(subInfo.Path);
            }
        }

        public List<UpdatedValue> GetUpdatedValues()
        {
            var updated = new List<UpdatedValue>(_subByTopicId.Count);

            // For simplicity, let's just do a linear scan
            foreach (var subInfo in _subByTopicId.Values)
            {
                if (subInfo.IsDirty)
                {
                    updated.Add(new UpdatedValue(subInfo.TopicId, subInfo.Value));
                    subInfo.IsDirty = false;
                }
            }

            IsDirty = false;

            return updated;
        }

        public void Set(string path, object value)
        {
            if (_subByPath.TryGetValue(path, out SubInfo subInfo))
            {
                if (value != subInfo.Value)
                {
                    subInfo.Value = value;
                    IsDirty = true;
                }
            }
        }

        internal void Invalidate()
        {
            var updated = new List<UpdatedValue>(_subByTopicId.Count);
            foreach (var subInfo in _subByTopicId.Values)
            {
                if (subInfo.IsDirty)
                {
                    updated.Add(new UpdatedValue(subInfo.TopicId, "<Dead>"));
                    subInfo.IsDirty = false;
                }
            }
            IsDirty = true;
        }

        [DebuggerStepThrough]
        public static string FormatPath(string host, string exchange, string routingKey)
        {
            return string.Format("{0}/{1}/{2}",
                                 host.ToUpperInvariant(),
                                 exchange.ToUpperInvariant(),
                                 routingKey.ToUpperInvariant());
        }
        [DebuggerStepThrough]
        public static string FormatPath(string host, string exchange, string routingKey, string field)
        {
            return string.Format("{0}/{1}", FormatPath(host, exchange, routingKey), field);
        }

        public class SubInfo
        {
            public int TopicId { get; private set; }
            public string Path { get; private set; }
            public HashSet<string> Fields { get; private set; }

            private object _value;

            public object Value
            {
                get { return _value; }
                set
                {
                    _value = value;
                    IsDirty = true;
                }
            }

            public bool IsDirty { get; set; }

            public SubInfo(int topicId, string path)
            {
                TopicId = topicId;
                Path = path;
                Value = UninitializedValue;
                IsDirty = false;
                Fields = new HashSet<string>();
            }
            public void addField(string field)
            {
                Fields.Add(field);
            }
        }
        public struct UpdatedValue
        {
            public int TopicId { get; private set; }
            public object Value { get; private set; }

            public UpdatedValue(int topicId, object value) : this()
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

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
        readonly Dictionary<int, SubInfo> _subByTopicId;

        public SubscriptionManager()
        {
            _subByPath = new Dictionary<string, SubInfo>();
            _subByTopicId = new Dictionary<int, SubInfo>();
        }

        public bool IsDirty { get; private set; }

        public void Subscribe(int topicId, string origin, string instrument, string field)
        {
            var subInfo = new SubInfo(
                topicId,
                FormatPath(origin, instrument, field));

            _subByTopicId.Add(topicId, subInfo);
            _subByPath.Add(subInfo.Path, subInfo);
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
        public static string FormatPath(string origin, string instrument, string field)
        {
            return string.Format("{0}/{1}/{2}",
                                 origin.ToUpperInvariant(),
                                 instrument.ToUpperInvariant(),
                                 field.ToUpperInvariant());
        }

        public class SubInfo
        {
            public int TopicId { get; private set; }
            public string Path { get; private set; }

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
            }
        }
        public struct UpdatedValue
        {
            public int TopicId { get; private set; }
            public object Value { get; private set; }

            public UpdatedValue(int topicId, object value) : this()
            {
                TopicId = topicId;

                if (Decimal.TryParse(value.ToString(), out Decimal dec))
                    Value = dec;
                else
                    Value = value;
            }
        }
    }

}

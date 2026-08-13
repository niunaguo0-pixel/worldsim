namespace WorldSim.Presentation
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WorldSim.Simulation.Core;

    public enum GenerationTimelineKind : byte
    {
        Death = 0,
        Inheritance = 1,
        Milestone = 2
    }

    /// <summary>由确定性 SimEvent 派生的只读世代时间轴节点。</summary>
    public readonly struct GenerationTimelineNode
    {
        public readonly int GameMonth;
        public readonly int SourceId;
        public readonly string TemplateId;
        public readonly double Magnitude;
        public readonly GenerationTimelineKind Kind;

        public GenerationTimelineNode(SimEvent simEvent, GenerationTimelineKind kind)
        {
            GameMonth = simEvent.gameMonth;
            SourceId = simEvent.sourceId;
            TemplateId = simEvent.templateId;
            Magnitude = simEvent.magnitude;
            Kind = kind;
        }
    }

    /// <summary>
    /// 只读、增量消费世代事件。事件源在读档后被替换时会重新扫描，
    /// 但已消费的稳定事件键不会再次生成表现节点。
    /// </summary>
    public sealed class GenerationTimelinePresenter
    {
        public const string DeathTemplateId = "civ.individual.death";
        public const string InheritanceTemplateId = "civ.individual.inheritance";
        public const string MilestoneTemplateId = "civ.generation.milestone";

        private readonly List<GenerationTimelineNode> _nodes = new List<GenerationTimelineNode>();
        private readonly ReadOnlyCollection<GenerationTimelineNode> _readOnlyNodes;
        private readonly HashSet<EventKey> _consumed = new HashSet<EventKey>();
        private object _eventSource;
        private int _nextEventIndex;

        public GenerationTimelinePresenter()
        {
            _readOnlyNodes = _nodes.AsReadOnly();
        }

        public IReadOnlyList<GenerationTimelineNode> Nodes => _readOnlyNodes;

        /// <returns>本次新增的时间轴节点数。</returns>
        public int Consume(IReadOnlyList<SimEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            int start = ReferenceEquals(_eventSource, events) && events.Count >= _nextEventIndex
                ? _nextEventIndex
                : 0;
            int added = 0;
            for (int i = start; i < events.Count; i++)
            {
                SimEvent simEvent = events[i];
                if (!TryGetKind(simEvent.templateId, out GenerationTimelineKind kind)) continue;
                if (!_consumed.Add(new EventKey(simEvent))) continue;
                _nodes.Add(new GenerationTimelineNode(simEvent, kind));
                added++;
            }

            _eventSource = events;
            _nextEventIndex = events.Count;
            return added;
        }

        private static bool TryGetKind(string templateId, out GenerationTimelineKind kind)
        {
            if (string.Equals(templateId, DeathTemplateId, StringComparison.Ordinal))
            {
                kind = GenerationTimelineKind.Death;
                return true;
            }
            if (string.Equals(templateId, InheritanceTemplateId, StringComparison.Ordinal))
            {
                kind = GenerationTimelineKind.Inheritance;
                return true;
            }
            if (string.Equals(templateId, MilestoneTemplateId, StringComparison.Ordinal))
            {
                kind = GenerationTimelineKind.Milestone;
                return true;
            }
            kind = default;
            return false;
        }

        private readonly struct EventKey : IEquatable<EventKey>
        {
            private readonly int _gameMonth;
            private readonly SimEventCategory _category;
            private readonly int _sourceId;
            private readonly string _templateId;
            private readonly long _magnitudeBits;

            public EventKey(SimEvent simEvent)
            {
                _gameMonth = simEvent.gameMonth;
                _category = simEvent.category;
                _sourceId = simEvent.sourceId;
                _templateId = simEvent.templateId;
                _magnitudeBits = BitConverter.DoubleToInt64Bits(simEvent.magnitude);
            }

            public bool Equals(EventKey other)
            {
                return _gameMonth == other._gameMonth
                    && _category == other._category
                    && _sourceId == other._sourceId
                    && string.Equals(_templateId, other._templateId, StringComparison.Ordinal)
                    && _magnitudeBits == other._magnitudeBits;
            }

            public override bool Equals(object obj) => obj is EventKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _gameMonth;
                    hash = (hash * 397) ^ (int)_category;
                    hash = (hash * 397) ^ _sourceId;
                    hash = (hash * 397) ^ (_templateId == null ? 0 : StringComparer.Ordinal.GetHashCode(_templateId));
                    hash = (hash * 397) ^ _magnitudeBits.GetHashCode();
                    return hash;
                }
            }
        }
    }
}

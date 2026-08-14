namespace WorldSim.Narrative
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WorldSim.Simulation.Core;

    /// <summary>
    /// S6 涌现叙事引擎：增量消费 SimEvent → 编年史 + 关键个体。
    /// 副作用只进本引擎内部状态，绝不回写 WorldState。
    /// </summary>
    public sealed class EmergentNarrativeEngine
    {
        private readonly List<ChronicleEntry> _entries = new List<ChronicleEntry>();
        private readonly ReadOnlyCollection<ChronicleEntry> _readOnlyEntries;
        private readonly Dictionary<int, ActorAgg> _actors = new Dictionary<int, ActorAgg>();
        private readonly HashSet<EventKey> _seen = new HashSet<EventKey>();
        private object _eventSource;
        private int _nextEventIndex;

        public EmergentNarrativeEngine()
        {
            _readOnlyEntries = _entries.AsReadOnly();
        }

        public IReadOnlyList<ChronicleEntry> Chronicle => _readOnlyEntries;
        public int EntryCount => _entries.Count;
        public int NotableActorCount => _actors.Count;

        /// <returns>本次新增编年条目数（含复合模式条）。</returns>
        public int Consume(IReadOnlyList<SimEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            int start = ReferenceEquals(_eventSource, events) && events.Count >= _nextEventIndex
                ? _nextEventIndex
                : 0;

            var batch = new List<SimEvent>();
            int added = 0;
            for (int i = start; i < events.Count; i++)
            {
                SimEvent simEvent = events[i];
                if (!_seen.Add(new EventKey(simEvent))) continue;

                ChronicleEntry entry = NarrativeTemplateCatalog.FromEvent(simEvent);
                _entries.Add(entry);
                TrackActor(entry);
                batch.Add(simEvent);
                added++;
            }

            int beforePatterns = _entries.Count;
            NarrativePatternDetector.Detect(batch, _entries);
            for (int i = beforePatterns; i < _entries.Count; i++)
                TrackActor(_entries[i]);
            added += _entries.Count - beforePatterns;

            _eventSource = events;
            _nextEventIndex = events.Count;
            return added;
        }

        /// <summary>按显著性分数取前 N 个关键源（稳定：同分按 SourceId 升序）。</summary>
        public List<NotableActor> GetTopNotableActors(int maxCount)
        {
            if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            var list = new List<NotableActor>(_actors.Count);
            foreach (var kv in _actors)
            {
                ActorAgg a = kv.Value;
                list.Add(new NotableActor(
                    kv.Key,
                    a.EventCount,
                    a.CriticalCount,
                    a.LastMonth,
                    a.LastTemplateId,
                    a.Score));
            }

            list.Sort(CompareActors);
            if (list.Count > maxCount)
                list.RemoveRange(maxCount, list.Count - maxCount);
            return list;
        }

        /// <summary>取最近若干条编年（不含截断已有存储）。</summary>
        public List<ChronicleEntry> GetRecentEntries(int maxCount)
        {
            if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            int start = Math.Max(0, _entries.Count - maxCount);
            var result = new List<ChronicleEntry>(Math.Max(0, _entries.Count - start));
            for (int i = start; i < _entries.Count; i++)
                result.Add(_entries[i]);
            return result;
        }

        public void Reset()
        {
            _entries.Clear();
            _actors.Clear();
            _seen.Clear();
            _eventSource = null;
            _nextEventIndex = 0;
        }

        private void TrackActor(ChronicleEntry entry)
        {
            if (!_actors.TryGetValue(entry.SourceId, out ActorAgg agg))
                agg = new ActorAgg();

            agg.EventCount++;
            if (entry.Significance >= ChronicleSignificance.Critical)
                agg.CriticalCount++;
            agg.LastMonth = entry.GameMonth;
            agg.LastTemplateId = entry.TemplateId;
            agg.Score += SignificanceWeight(entry.Significance);
            if (entry.IsComposite)
                agg.Score += 2.0;
            _actors[entry.SourceId] = agg;
        }

        private static double SignificanceWeight(ChronicleSignificance significance)
        {
            switch (significance)
            {
                case ChronicleSignificance.Low: return 0.5;
                case ChronicleSignificance.Normal: return 1.0;
                case ChronicleSignificance.High: return 2.5;
                case ChronicleSignificance.Critical: return 5.0;
                default: return 1.0;
            }
        }

        private static int CompareActors(NotableActor a, NotableActor b)
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            return a.SourceId.CompareTo(b.SourceId);
        }

        private struct ActorAgg
        {
            public int EventCount;
            public int CriticalCount;
            public int LastMonth;
            public string LastTemplateId;
            public double Score;
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

            public bool Equals(EventKey other) =>
                _gameMonth == other._gameMonth
                && _category == other._category
                && _sourceId == other._sourceId
                && string.Equals(_templateId, other._templateId, StringComparison.Ordinal)
                && _magnitudeBits == other._magnitudeBits;

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

namespace WorldSim.Presentation
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WorldSim.Simulation.Core;

    public enum TimeSeason : byte
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3,
    }

    /// <summary>
    /// 表现层只读时间投影。所有集合均在构造时复制，消费者不能借此修改 WorldState。
    /// </summary>
    public readonly struct TimeViewSnapshot
    {
        private static readonly IReadOnlyList<SimEvent> EmptyEvents =
            new ReadOnlyCollection<SimEvent>(new SimEvent[0]);

        public int MonthIndex { get; }
        public int GameYear { get; }
        public int MonthOfYear { get; }
        public TimeSeason Season { get; }
        public int EraIndex { get; }
        public bool IsPaused { get; }
        public int SpeedMultiplier { get; }
        public bool ShowHighSpeedHint { get; }
        public double Population { get; }
        public double FoodReserve { get; }
        public int PendingCount { get; }
        public IReadOnlyList<SimEvent> Events { get; }

        public TimeViewSnapshot(
            int monthIndex,
            int eraIndex,
            bool isPaused,
            int speedMultiplier,
            double population,
            double foodReserve,
            int pendingCount,
            IReadOnlyList<SimEvent> events)
        {
            if (monthIndex < 0) throw new ArgumentOutOfRangeException(nameof(monthIndex));
            if (pendingCount < 0) throw new ArgumentOutOfRangeException(nameof(pendingCount));

            MonthIndex = monthIndex;
            GameYear = monthIndex / 12 + 1;
            MonthOfYear = monthIndex % 12 + 1;
            Season = (TimeSeason)((monthIndex % 12) / 3);
            EraIndex = eraIndex;
            IsPaused = isPaused;
            SpeedMultiplier = speedMultiplier;
            ShowHighSpeedHint = speedMultiplier >= 5;
            Population = population;
            FoodReserve = foodReserve;
            PendingCount = pendingCount;
            Events = CopyEvents(events);
        }

        private static IReadOnlyList<SimEvent> CopyEvents(IReadOnlyList<SimEvent> events)
        {
            if (events == null || events.Count == 0) return EmptyEvents;

            var copy = new SimEvent[events.Count];
            for (int i = 0; i < events.Count; i++)
                copy[i] = events[i];
            return new ReadOnlyCollection<SimEvent>(copy);
        }
    }

    /// <summary>向只读表现层提供当前时间投影。</summary>
    public interface ITimePresentationSource
    {
        TimeViewSnapshot TimeSnapshot { get; }
    }

    /// <summary>表现层唯一允许调用的时间控制命令。</summary>
    public interface ITimeControlSink
    {
        void SetPaused(bool paused);
        void SetSpeedMultiplier(int speedMultiplier);
    }

    /// <summary>
    /// 追加型 SimEvent 列表的增量游标。源列表缩短时钳制游标，不重复消费保留事件。
    /// </summary>
    public sealed class TimeEventCursor
    {
        private int _position;

        public int Position => _position;

        public IReadOnlyList<SimEvent> Consume(IReadOnlyList<SimEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (_position > events.Count) _position = events.Count;
            if (_position == events.Count) return Array.Empty<SimEvent>();

            int count = events.Count - _position;
            var slice = new SimEvent[count];
            for (int i = 0; i < count; i++)
                slice[i] = events[_position + i];
            _position = events.Count;
            return slice;
        }

        public void Reset(int position = 0)
        {
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            _position = position;
        }

        public void SeekToEnd(IReadOnlyList<SimEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            _position = events.Count;
        }
    }

    /// <summary>把可变 WorldState 投影为不可变、只读的时间视图。</summary>
    public sealed class TimePresentationModel
    {
        private readonly TimeEventCursor _eventCursor = new TimeEventCursor();

        public TimeEventCursor EventCursor => _eventCursor;

        public TimeViewSnapshot Capture(WorldState world, int pendingCount)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            double population = 0.0;
            for (int i = 0; i < world.Settlements.Count; i++)
                population += world.Settlements[i].population;

            double foodReserve = 0.0;
            for (int i = 0; i < world.Resources.Count; i++)
            {
                if (string.Equals(world.Resources[i].name, "Food", StringComparison.Ordinal))
                    foodReserve += world.Resources[i].currentAmount;
            }

            return new TimeViewSnapshot(
                world.Time.monthIndex,
                world.EraIndex,
                world.Time.paused,
                world.Time.speedMultiplier,
                population,
                foodReserve,
                pendingCount,
                _eventCursor.Consume(world.Events));
        }
    }
}

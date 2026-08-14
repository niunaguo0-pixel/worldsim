namespace WorldSim.Narrative
{
    /// <summary>关键个体/政体追踪条目（由事件源 ID 聚合）。</summary>
    public readonly struct NotableActor
    {
        public readonly int SourceId;
        public readonly int EventCount;
        public readonly int CriticalCount;
        public readonly int LastMonth;
        public readonly string LastTemplateId;
        public readonly double Score;

        public NotableActor(
            int sourceId,
            int eventCount,
            int criticalCount,
            int lastMonth,
            string lastTemplateId,
            double score)
        {
            SourceId = sourceId;
            EventCount = eventCount;
            CriticalCount = criticalCount;
            LastMonth = lastMonth;
            LastTemplateId = lastTemplateId ?? "";
            Score = score;
        }
    }
}

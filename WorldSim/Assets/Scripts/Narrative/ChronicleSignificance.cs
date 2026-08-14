namespace WorldSim.Narrative
{
    /// <summary>编年史条目显著性（供 S8 过滤与高亮；不入月哈希）。</summary>
    public enum ChronicleSignificance : byte
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
}

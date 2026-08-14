namespace WorldSim.Narrative
{
    using WorldSim.Simulation.Core;

    /// <summary>
    /// S6 派生编年史条目：只读呈现态，不回写 WorldState（架构 §2.6 / §5.1）。
    /// </summary>
    public readonly struct ChronicleEntry
    {
        public readonly int GameMonth;
        public readonly SimEventCategory Category;
        public readonly int SourceId;
        public readonly string TemplateId;
        public readonly string Title;
        public readonly string Body;
        public readonly double Magnitude;
        public readonly ChronicleSignificance Significance;
        public readonly bool IsComposite;

        public ChronicleEntry(
            int gameMonth,
            SimEventCategory category,
            int sourceId,
            string templateId,
            string title,
            string body,
            double magnitude,
            ChronicleSignificance significance,
            bool isComposite = false)
        {
            GameMonth = gameMonth;
            Category = category;
            SourceId = sourceId;
            TemplateId = templateId ?? "";
            Title = title ?? "";
            Body = body ?? "";
            Magnitude = magnitude;
            Significance = significance;
            IsComposite = isComposite;
        }
    }
}

namespace WorldSim.Presentation
{
    using System;

    public enum CameraLodLevel
    {
        Individual,
        Settlement,
        Civilization,
        GenerationOverview
    }

    /// <summary>相机距离到纯表现 LOD 的无状态映射；不读取也不修改模拟状态。</summary>
    public static class CameraLodPolicy
    {
        public const float IndividualMaxDistance = 6f;
        public const float SettlementMaxDistance = 11f;
        public const float CivilizationMaxDistance = 18f;

        public static CameraLodDecision Evaluate(float cameraDistance)
        {
            if (float.IsNaN(cameraDistance) || float.IsInfinity(cameraDistance) || cameraDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(cameraDistance));

            if (cameraDistance <= IndividualMaxDistance)
                return new CameraLodDecision(CameraLodLevel.Individual, "近景个体", true, false, false, false);
            if (cameraDistance <= SettlementMaxDistance)
                return new CameraLodDecision(CameraLodLevel.Settlement, "聚落", true, true, false, false);
            if (cameraDistance <= CivilizationMaxDistance)
                return new CameraLodDecision(CameraLodLevel.Civilization, "文明聚合", false, false, true, true);

            return new CameraLodDecision(CameraLodLevel.GenerationOverview, "世代概览", false, false, true, true);
        }
    }

    public readonly struct CameraLodDecision
    {
        public CameraLodDecision(
            CameraLodLevel level,
            string label,
            bool showEntityDetails,
            bool showSettlementLabel,
            bool showAggregateStatistics,
            bool reduceMotion)
        {
            Level = level;
            Label = label;
            ShowEntityDetails = showEntityDetails;
            ShowSettlementLabel = showSettlementLabel;
            ShowAggregateStatistics = showAggregateStatistics;
            ReduceMotion = reduceMotion;
        }

        public CameraLodLevel Level { get; }
        public string Label { get; }
        public bool ShowEntityDetails { get; }
        public bool ShowSettlementLabel { get; }
        public bool ShowAggregateStatistics { get; }
        public bool ReduceMotion { get; }
    }
}

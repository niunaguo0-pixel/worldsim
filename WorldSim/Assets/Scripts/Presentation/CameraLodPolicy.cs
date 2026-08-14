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

    /// <summary>
    /// 相机距离 → 纯表现 LOD（含地球 mesh 精度）；不读取也不修改模拟状态。
    /// P4：缩放/平移读 LOD 决定渲染精度；迟滞避免边界抖动。
    /// </summary>
    public static class CameraLodPolicy
    {
        public const float IndividualMaxDistance = 6f;
        public const float SettlementMaxDistance = 11f;
        public const float CivilizationMaxDistance = 18f;
        /// <summary>进出档位迟滞带宽（距离单位），抑制边界抖动。</summary>
        public const float HysteresisBand = 0.75f;

        public static CameraLodDecision Evaluate(float cameraDistance) =>
            ForLevel(LevelAt(cameraDistance));

        /// <summary>带迟滞：仅当越过上一档边界 ± band 才切档。</summary>
        public static CameraLodDecision EvaluateWithHysteresis(
            float cameraDistance,
            CameraLodLevel previousLevel)
        {
            if (float.IsNaN(cameraDistance) || float.IsInfinity(cameraDistance) || cameraDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(cameraDistance));

            CameraLodLevel raw = LevelAt(cameraDistance);
            if (raw == previousLevel)
                return ForLevel(previousLevel);

            float exit = ExitThreshold(previousLevel);
            if (raw > previousLevel)
            {
                // 拉远：需越过上一档上界 + band
                if (cameraDistance < exit + HysteresisBand)
                    return ForLevel(previousLevel);
            }
            else
            {
                // 拉近：需越过上一档下界 - band
                float enter = EnterThreshold(previousLevel);
                if (cameraDistance > enter - HysteresisBand)
                    return ForLevel(previousLevel);
            }

            return ForLevel(raw);
        }

        public static CameraLodDecision ForLevel(CameraLodLevel level)
        {
            switch (level)
            {
                case CameraLodLevel.Individual:
                    return new CameraLodDecision(
                        CameraLodLevel.Individual, "近景个体",
                        showEntityDetails: true, showSettlementLabel: false,
                        showAggregateStatistics: false, reduceMotion: false,
                        meshLonSegments: 180, meshLatSegments: 90,
                        elevationScale: 0.18f, allowAutoRotate: false);
                case CameraLodLevel.Settlement:
                    return new CameraLodDecision(
                        CameraLodLevel.Settlement, "聚落",
                        showEntityDetails: true, showSettlementLabel: true,
                        showAggregateStatistics: false, reduceMotion: false,
                        meshLonSegments: 120, meshLatSegments: 60,
                        elevationScale: 0.14f, allowAutoRotate: false);
                case CameraLodLevel.Civilization:
                    return new CameraLodDecision(
                        CameraLodLevel.Civilization, "文明聚合",
                        showEntityDetails: false, showSettlementLabel: false,
                        showAggregateStatistics: true, reduceMotion: true,
                        meshLonSegments: 90, meshLatSegments: 45,
                        elevationScale: 0.10f, allowAutoRotate: true);
                default:
                    return new CameraLodDecision(
                        CameraLodLevel.GenerationOverview, "世代概览",
                        showEntityDetails: false, showSettlementLabel: false,
                        showAggregateStatistics: true, reduceMotion: true,
                        meshLonSegments: 60, meshLatSegments: 30,
                        elevationScale: 0.06f, allowAutoRotate: true);
            }
        }

        private static CameraLodLevel LevelAt(float cameraDistance)
        {
            if (float.IsNaN(cameraDistance) || float.IsInfinity(cameraDistance) || cameraDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(cameraDistance));

            if (cameraDistance <= IndividualMaxDistance) return CameraLodLevel.Individual;
            if (cameraDistance <= SettlementMaxDistance) return CameraLodLevel.Settlement;
            if (cameraDistance <= CivilizationMaxDistance) return CameraLodLevel.Civilization;
            return CameraLodLevel.GenerationOverview;
        }

        private static float ExitThreshold(CameraLodLevel level)
        {
            switch (level)
            {
                case CameraLodLevel.Individual: return IndividualMaxDistance;
                case CameraLodLevel.Settlement: return SettlementMaxDistance;
                case CameraLodLevel.Civilization: return CivilizationMaxDistance;
                default: return float.PositiveInfinity;
            }
        }

        private static float EnterThreshold(CameraLodLevel level)
        {
            switch (level)
            {
                case CameraLodLevel.Settlement: return IndividualMaxDistance;
                case CameraLodLevel.Civilization: return SettlementMaxDistance;
                case CameraLodLevel.GenerationOverview: return CivilizationMaxDistance;
                default: return 0f;
            }
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
            bool reduceMotion,
            int meshLonSegments,
            int meshLatSegments,
            float elevationScale,
            bool allowAutoRotate)
        {
            Level = level;
            Label = label;
            ShowEntityDetails = showEntityDetails;
            ShowSettlementLabel = showSettlementLabel;
            ShowAggregateStatistics = showAggregateStatistics;
            ReduceMotion = reduceMotion;
            MeshLonSegments = meshLonSegments;
            MeshLatSegments = meshLatSegments;
            ElevationScale = elevationScale;
            AllowAutoRotate = allowAutoRotate;
        }

        public CameraLodLevel Level { get; }
        public string Label { get; }
        public bool ShowEntityDetails { get; }
        public bool ShowSettlementLabel { get; }
        public bool ShowAggregateStatistics { get; }
        public bool ReduceMotion { get; }
        /// <summary>P4：地球经度分段（渲染精度）。</summary>
        public int MeshLonSegments { get; }
        /// <summary>P4：地球纬度分段（渲染精度）。</summary>
        public int MeshLatSegments { get; }
        public float ElevationScale { get; }
        public bool AllowAutoRotate { get; }
    }
}

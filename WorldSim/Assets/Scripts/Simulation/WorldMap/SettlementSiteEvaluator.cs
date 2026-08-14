namespace WorldSim.Simulation.WorldMap
{
    using System;
    using WorldSim.Simulation.Core.WorldGeography;

    public readonly struct SettlementSiteScore
    {
        public readonly bool IsHabitable;
        public readonly double Score;
        public readonly string Reason;
        public SettlementSiteScore(bool isHabitable, double score, string reason)
        {
            IsHabitable = isHabitable; Score = score; Reason = reason;
        }
    }

    /// <summary>
    /// 定居点选址评估 (Task 4 校准: 真实 0.5° 数据下陆地 slope max≈7°, 原 18° 阈值永不触发,
    /// 故地形过陡由 elevation>3500 分支承载; slope 降为 6° 软门, 使最陡的真实格仍可被标不可居,
    /// 同时保留 elevation 硬门)。
    /// </summary>
    public static class SettlementSiteEvaluator
    {
        public static SettlementSiteScore Evaluate(IWorldGeography geography, int tileId)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            var tile = geography.GetTile(tileId);
            if (!tile.IsLand || tile.Biome == BiomeType.Ocean || tile.Biome == BiomeType.Ice)
                return new SettlementSiteScore(false, 0, "water-or-ice");
            if (tile.Slope > 6 || tile.ElevationMeters > 3500)
                return new SettlementSiteScore(false, 0, "terrain-too-steep");
            double score = 0.25;
            if (geography.HasWaterNearby(tileId)) score += 0.35;
            if (tile.Biome == BiomeType.Grassland || tile.Biome == BiomeType.TemperateForest ||
                tile.Biome == BiomeType.Savanna || tile.Biome == BiomeType.Wetland) score += 0.25;
            score += Math.Max(0, 0.15 - tile.Slope / 60.0);
            return new SettlementSiteScore(score >= 0.45, Math.Min(1, score), score >= 0.45 ? "habitable" : "marginal");
        }
    }

    public enum NaturalBoundaryType : byte { None, River, Mountain, Coast }

    public static class NaturalBoundaryClassifier
    {
        /// <summary>Task 4 校准: 真实 0.5° 数据下 slope max≈7°, 原 ≥20° 阈值永不触发;
        /// 山脉自然边界由 elevation≥2200 承载, slope 降为 ≥5° 辅助判定。</summary>
        public static NaturalBoundaryType Classify(IWorldGeography geography, int tileId)
        {
            if (geography.HasRiver(tileId)) return NaturalBoundaryType.River;
            if (geography.GetElevation(tileId) >= 2200 || geography.GetSlope(tileId) >= 5)
                return NaturalBoundaryType.Mountain;
            if (geography.HasCoast(tileId)) return NaturalBoundaryType.Coast;
            return NaturalBoundaryType.None;
        }
    }
}

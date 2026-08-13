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

    public static class SettlementSiteEvaluator
    {
        public static SettlementSiteScore Evaluate(IWorldGeography geography, int tileId)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            var tile = geography.GetTile(tileId);
            if (!tile.IsLand || tile.Biome == BiomeType.Ocean || tile.Biome == BiomeType.Ice)
                return new SettlementSiteScore(false, 0, "water-or-ice");
            if (tile.Slope > 18 || tile.ElevationMeters > 3500)
                return new SettlementSiteScore(false, 0, "terrain-too-steep");
            double score = 0.25;
            if (geography.HasWaterNearby(tileId)) score += 0.35;
            if (tile.Biome == BiomeType.Grassland || tile.Biome == BiomeType.TemperateForest ||
                tile.Biome == BiomeType.Savanna || tile.Biome == BiomeType.Wetland) score += 0.25;
            score += Math.Max(0, 0.15 - tile.Slope / 120.0);
            return new SettlementSiteScore(score >= 0.45, Math.Min(1, score), score >= 0.45 ? "habitable" : "marginal");
        }
    }

    public enum NaturalBoundaryType : byte { None, River, Mountain, Coast }

    public static class NaturalBoundaryClassifier
    {
        public static NaturalBoundaryType Classify(IWorldGeography geography, int tileId)
        {
            if (geography.HasRiver(tileId)) return NaturalBoundaryType.River;
            if (geography.GetElevation(tileId) >= 2200 || geography.GetSlope(tileId) >= 20)
                return NaturalBoundaryType.Mountain;
            if (geography.HasCoast(tileId)) return NaturalBoundaryType.Coast;
            return NaturalBoundaryType.None;
        }
    }
}

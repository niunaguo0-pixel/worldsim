namespace WorldSim.Simulation.WorldMap
{
    using System;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>稳定、表驱动的 climate/elevation/latitude → biome 分类。
    /// Task 4 校准: 真实 Köppen 0.5° 数据下 Highland 由 elevation≥2500 触发, Alpine 由 elevation≥3000 触发;
    /// slope 在 0.5° 格宽下动态范围很小 (陆地 max≈7°), 故地形阈值由 elevation 分支承载,
    /// slope 仅作软惩罚 (见 SettlementSiteEvaluator)。</summary>
    public static class BiomeClassifier
    {
        public static BiomeType Classify(bool isLand, ClimateZone climate, double elevationMeters,
            double latitude, double rainfallMm)
        {
            if (!isLand) return BiomeType.Ocean;
            if (elevationMeters >= 3000) return BiomeType.Alpine;
            switch (climate)
            {
                case ClimateZone.Polar: return BiomeType.Ice;
                case ClimateZone.Subpolar: return Math.Abs(latitude) >= 67 ? BiomeType.Tundra : BiomeType.BorealForest;
                case ClimateZone.Arid: return rainfallMm < 350 ? BiomeType.Desert : BiomeType.Grassland;
                case ClimateZone.Tropical: return rainfallMm >= 1600 ? BiomeType.TropicalRainforest : BiomeType.Savanna;
                case ClimateZone.Subtropical: return rainfallMm < 500 ? BiomeType.Desert : BiomeType.TemperateForest;
                case ClimateZone.Highland: return BiomeType.Alpine; // 2500–2999m 由 Highland 气候分支; ≥3000m 由高程分支 (Task 4 校准注释)
                default: return rainfallMm >= 700 ? BiomeType.TemperateForest : BiomeType.Grassland;
            }
        }

        public static ClimateZone LatitudeDefault(double latitude, double elevationMeters, double rainfallMm)
        {
            double abs = Math.Abs(latitude);
            if (elevationMeters >= 2500) return ClimateZone.Highland;
            if (abs >= 75) return ClimateZone.Polar;
            if (abs >= 58) return ClimateZone.Subpolar;
            if (rainfallMm < 300) return ClimateZone.Arid;
            if (abs <= 23.5) return ClimateZone.Tropical;
            if (abs <= 35) return ClimateZone.Subtropical;
            return ClimateZone.Temperate;
        }
    }
}

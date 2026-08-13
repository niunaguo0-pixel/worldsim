namespace WorldSim.Simulation.WorldMap
{
    using System;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>稳定、表驱动的 climate/elevation/latitude → biome 分类。</summary>
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
                case ClimateZone.Highland: return BiomeType.Alpine;
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

namespace WorldSim.Presentation
{
    using UnityEngine;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>
    /// Epic 6 P2：林绘澄美术圣经统一色板（微缩沙盘手绘温度）。
    /// 不随地理模式切换；渲染只消费表现层快照派生色。
    /// </summary>
    public static class NprDioramaPalette
    {
        public static readonly Color EarthOchre = Hex(0xC4, 0xA3, 0x5A);
        public static readonly Color Sienna = Hex(0x8B, 0x5E, 0x3C);
        public static readonly Color SageGreen = Hex(0x5B, 0x8C, 0x5A);
        public static readonly Color RockGray = Hex(0x6B, 0x6B, 0x6B);
        public static readonly Color WaterBlue = Hex(0x5E, 0x7A, 0x8C);
        public static readonly Color DeepBrown = Hex(0x3A, 0x2A, 0x1A);
        public static readonly Color WarmGold = Hex(0xD4, 0xA8, 0x4B);
        public static readonly Color SettlementFill = Hex(0xD4, 0xA8, 0x4B);

        public static Color ColorForTile(WorldTileData tile)
        {
            if (tile == null || !tile.IsLand)
                return WaterBlue;
            switch (tile.Biome)
            {
                case BiomeType.Ice: return new Color(0.86f, 0.90f, 0.92f);
                case BiomeType.Tundra: return Mix(RockGray, SageGreen, 0.35f);
                case BiomeType.BorealForest: return Mix(SageGreen, DeepBrown, 0.35f);
                case BiomeType.TemperateForest: return SageGreen;
                case BiomeType.Grassland: return Mix(SageGreen, EarthOchre, 0.45f);
                case BiomeType.Desert: return EarthOchre;
                case BiomeType.Savanna: return Mix(EarthOchre, Sienna, 0.4f);
                case BiomeType.TropicalRainforest: return Mix(SageGreen, DeepBrown, 0.25f);
                case BiomeType.Alpine: return Mix(RockGray, Sienna, 0.4f);
                case BiomeType.Wetland: return Mix(WaterBlue, SageGreen, 0.55f);
                default: return EarthOchre;
            }
        }

        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        private static Color Hex(byte r, byte g, byte b) =>
            new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}

namespace WorldSim.Simulation.Core.WorldGeography
{
    using System;
    using System.Collections.Generic;

    public enum BiomeType : byte
    {
        Ocean = 0,
        Ice = 1,
        Tundra = 2,
        BorealForest = 3,
        TemperateForest = 4,
        Grassland = 5,
        Desert = 6,
        Savanna = 7,
        TropicalRainforest = 8,
        Alpine = 9,
        Wetland = 10
    }

    public enum ClimateZone : byte
    {
        Polar = 0,
        Subpolar = 1,
        Temperate = 2,
        Arid = 3,
        Subtropical = 4,
        Tropical = 5,
        Highland = 6
    }

    /// <summary>数据精度 LOD；与相机渲染 LOD 完全独立。</summary>
    public enum MapLodLevel : byte { High = 0, Mid = 1, Low = 2 }

    public readonly struct GeoCoordinate : IEquatable<GeoCoordinate>
    {
        public readonly double Latitude;
        public readonly double Longitude;

        public GeoCoordinate(double latitude, double longitude)
        {
            Latitude = Math.Max(-90.0, Math.Min(90.0, latitude));
            Longitude = NormalizeLongitude(longitude);
        }

        public static double NormalizeLongitude(double longitude)
        {
            double value = longitude % 360.0;
            if (value < -180.0) value += 360.0;
            if (value >= 180.0) value -= 360.0;
            return value;
        }

        public bool Equals(GeoCoordinate other) =>
            Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude);
        public override bool Equals(object obj) => obj is GeoCoordinate other && Equals(other);
        public override int GetHashCode() => Latitude.GetHashCode() * 397 ^ Longitude.GetHashCode();
    }

    public sealed class WorldTileData
    {
        public int TileId;
        public GeoCoordinate Coordinate;
        public bool IsLand;
        public BiomeType Biome;
        public ClimateZone Climate;
        public double ElevationMeters;
        public double Slope;
        public double BaseTemperatureC;
        public double BaseRainfallMm;
        public bool HasCoast;
        public bool HasWater;
        public bool HasRiver;
        public MapLodLevel Lod;
        public bool IsInterpolated;
    }

    /// <summary>存档中的静态 bundle 引用；不重复写入全球 tile。</summary>
    public sealed class WorldMapChunkRef
    {
        public string ChunkId = "";
        public MapLodLevel Lod;
        public string RelativePath = "";
        public string Checksum = "";
    }

    /// <summary>动态地貌覆盖；静态数据仍由 bundle checksum 定位。</summary>
    public sealed class WorldTileOverride
    {
        public int TileId;
        public bool HasElevation;
        public double ElevationMeters;
        public bool HasBiome;
        public BiomeType Biome;
    }

    /// <summary>Core 持久 DTO；枚举以整数保存以避免 Core→WorldMap 反向依赖。</summary>
    public sealed class WorldMapConfigSnapshot
    {
        public int StartEra;
        public int StartMode;
        public int BorderYear;
        public bool UseRealBorders;
        /// <summary>BorderView 以整数持久化 (0=DeFactoControl, 1=SovereigntyClaims); Schema 7+.</summary>
        public int BorderView;
        public double StartRegionCenterLat;
        public double StartRegionCenterLon;
        public double StartRegionRadiusDeg;
    }

    public sealed class WorldMapState
    {
        public string GeoDataBuild = "";
        public string ConfigKey = "";
        public string ManifestChecksum = "";
        public WorldMapConfigSnapshot Config = new WorldMapConfigSnapshot();
        public List<WorldMapChunkRef> StaticChunks = new List<WorldMapChunkRef>();
        public List<WorldTileOverride> DynamicOverrides = new List<WorldTileOverride>();
    }
}

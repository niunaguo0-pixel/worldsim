namespace WorldSim.Simulation.Core.WorldGeography
{
    using System.Collections.Generic;

    /// <summary>S2/S3 唯一只读地理入口；实现留在 WorldMap，模拟不得改写静态地貌。</summary>
    public interface IWorldGeography
    {
        WorldTileData GetTile(int worldTileId);
        WorldTileData GetTile(GeoCoordinate coordinate, MapLodLevel preferredLod = MapLodLevel.High);
        int GetTileId(GeoCoordinate coordinate, MapLodLevel lod = MapLodLevel.High);
        BiomeType GetBiome(int worldTileId);
        double GetElevation(int worldTileId);
        bool HasWaterNearby(int worldTileId);
        double GetSlope(int worldTileId);
        ClimateZone GetClimate(int worldTileId);
        bool HasCoast(int worldTileId);
        bool HasRiver(int worldTileId);
        MapLodLevel GetLod(int worldTileId);
        IReadOnlyList<int> GetRiverTiles();
        IReadOnlyList<int> GetMountainBoundaryTiles(double minimumElevationMeters);
        IReadOnlyList<int> GetCoastBoundaryTiles();
    }
}

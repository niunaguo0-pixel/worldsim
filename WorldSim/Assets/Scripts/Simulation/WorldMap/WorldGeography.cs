namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core.WorldGeography;

    public sealed class WorldGeography : IWorldGeography
    {
        private readonly Dictionary<int, WorldTileData> _tiles = new Dictionary<int, WorldTileData>();
        private readonly Dictionary<int, WorldTileOverride> _overrides = new Dictionary<int, WorldTileOverride>();

        public WorldGeography(IEnumerable<WorldMapBundle> bundles, IEnumerable<WorldTileOverride> overrides = null)
        {
            if (bundles == null) throw new ArgumentNullException(nameof(bundles));
            foreach (var bundle in bundles)
                MergeBundle(bundle, preferExisting: false);
            if (overrides != null)
                foreach (var item in overrides)
                    _overrides[item.TileId] = item;
        }

        /// <summary>
        /// S5-3：异步远域 Low 装载完成后合并；preferExisting=true 时不覆盖已有 High/Mid tile。
        /// </summary>
        public void MergeBundle(WorldMapBundle bundle, bool preferExisting = true)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            foreach (var pair in bundle.Tiles)
            {
                if (preferExisting && _tiles.ContainsKey(pair.Key)) continue;
                _tiles[pair.Key] = pair.Value;
            }
        }

        /// <summary>已物化 tile 数（测试/诊断用，不进月哈希）。</summary>
        public int MaterializedTileCount => _tiles.Count;

        public bool TryGetExactTile(int worldTileId, out WorldTileData tile)
        {
            if (_tiles.TryGetValue(worldTileId, out tile))
            {
                tile = ApplyOverride(tile);
                return true;
            }
            tile = null;
            return false;
        }

        public WorldTileData GetTile(int worldTileId)
        {
            if (_tiles.TryGetValue(worldTileId, out WorldTileData tile)) return ApplyOverride(tile);
            GeoCoordinate coordinate = EquirectangularProjection.ToCoordinate(worldTileId);
            EquirectangularProjection.DecodeTileId(worldTileId, out MapLodLevel lod, out _, out _);
            return GetTile(coordinate, lod);
        }

        public WorldTileData GetTile(GeoCoordinate coordinate, MapLodLevel preferredLod = MapLodLevel.High)
        {
            for (int value = (int)preferredLod; value <= (int)MapLodLevel.Low; value++)
            {
                int id = GetTileId(coordinate, (MapLodLevel)value);
                if (_tiles.TryGetValue(id, out WorldTileData found)) return ApplyOverride(found);
            }
            return LatitudeFallback(coordinate);
        }

        public int GetTileId(GeoCoordinate coordinate, MapLodLevel lod = MapLodLevel.High) =>
            EquirectangularProjection.ToTileId(coordinate, lod);
        public BiomeType GetBiome(int worldTileId) => GetTile(worldTileId).Biome;
        public double GetElevation(int worldTileId) => GetTile(worldTileId).ElevationMeters;
        public bool HasWaterNearby(int worldTileId) { var t = GetTile(worldTileId); return t.HasWater || t.HasCoast || t.HasRiver; }
        public double GetSlope(int worldTileId) => GetTile(worldTileId).Slope;
        public ClimateZone GetClimate(int worldTileId) => GetTile(worldTileId).Climate;
        public bool HasCoast(int worldTileId) => GetTile(worldTileId).HasCoast;
        public bool HasRiver(int worldTileId) => GetTile(worldTileId).HasRiver;
        public MapLodLevel GetLod(int worldTileId) => GetTile(worldTileId).Lod;

        public IReadOnlyList<int> GetRiverTiles() => Select(t => t.HasRiver);
        public IReadOnlyList<int> GetMountainBoundaryTiles(double minimumElevationMeters) =>
            Select(t => t.IsLand && t.ElevationMeters >= minimumElevationMeters);
        public IReadOnlyList<int> GetCoastBoundaryTiles() => Select(t => t.HasCoast);

        private IReadOnlyList<int> Select(Func<WorldTileData, bool> predicate)
        {
            var ids = new List<int>();
            foreach (var pair in _tiles)
                if (predicate(pair.Value)) ids.Add(pair.Key);
            ids.Sort();
            return ids;
        }

        private WorldTileData ApplyOverride(WorldTileData source)
        {
            if (!_overrides.TryGetValue(source.TileId, out WorldTileOverride item)) return source;
            return new WorldTileData
            {
                TileId = source.TileId, Coordinate = source.Coordinate, IsLand = source.IsLand,
                Biome = item.HasBiome ? item.Biome : source.Biome, Climate = source.Climate,
                ElevationMeters = item.HasElevation ? item.ElevationMeters : source.ElevationMeters,
                Slope = source.Slope, BaseTemperatureC = source.BaseTemperatureC,
                BaseRainfallMm = source.BaseRainfallMm, HasCoast = source.HasCoast,
                HasWater = source.HasWater, HasRiver = source.HasRiver, Lod = source.Lod,
                IsInterpolated = source.IsInterpolated
            };
        }

        private static WorldTileData LatitudeFallback(GeoCoordinate coordinate)
        {
            double rainfall = Math.Max(100, 1200 - Math.Abs(coordinate.Latitude) * 10);
            ClimateZone climate = BiomeClassifier.LatitudeDefault(coordinate.Latitude, 0, rainfall);
            int id = EquirectangularProjection.ToTileId(coordinate, MapLodLevel.Low);
            return new WorldTileData
            {
                TileId = id, Coordinate = coordinate, IsLand = false, HasWater = true,
                Biome = BiomeType.Ocean, Climate = climate, ElevationMeters = -1000,
                Slope = 0, BaseTemperatureC = 28 - Math.Abs(coordinate.Latitude) * 0.55,
                BaseRainfallMm = rainfall, Lod = MapLodLevel.Low, IsInterpolated = true
            };
        }
    }
}

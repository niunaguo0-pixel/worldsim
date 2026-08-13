namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.WorldGeography;

    public sealed class WorldMapBuildResult
    {
        public GeoBundleManifest Manifest;
        public WorldGeography Geography;
    }

    public static class WorldMapFactory
    {
        /// <summary>确定性启动路径：全球 Low 与起始区域 High 都在返回前可用。</summary>
        public static WorldMapBuildResult Build(string geoRoot, WorldInitConfig config, WorldState world = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var manifest = ReadManifestAndVerify(geoRoot, config);
            var loaded = LoadBundlesForStartRegion(manifest, geoRoot, config);
            var geography = new WorldGeography(loaded, world?.Map?.DynamicOverrides);
            AssignWorldMapState(world, manifest, config, geography);
            return new WorldMapBuildResult { Manifest = manifest, Geography = geography };
        }

        /// <summary>
        /// 存档加载后的 Geography 显式重建路径 (Task 4): 从已持久化的 WorldMapState
        /// (静态 chunk 引用 + 动态覆盖) 重读 Low 全量与起始区域 High, 重建只读 Geography,
        /// 防止依赖系统 NRE 或静默回退。Mid/表现层数据不写回模拟态。
        /// </summary>
        public static WorldGeography RebuildGeography(WorldState world, string geoRoot)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Map == null || world.Map.StaticChunks == null || world.Map.StaticChunks.Count == 0)
                throw new InvalidOperationException("WorldMapState has no static chunks to rebuild from");
            var config = new WorldInitConfig
            {
                GeoDataBuild = world.Map.GeoDataBuild,
                StartRegionCenterLat = world.Map.Config.StartRegionCenterLat,
                StartRegionCenterLon = world.Map.Config.StartRegionCenterLon,
                StartRegionRadiusDeg = world.Map.Config.StartRegionRadiusDeg
            };
            var manifest = new GeoBundleManifest();
            manifest.Chunks.AddRange(world.Map.StaticChunks);
            var loaded = LoadBundlesForStartRegion(manifest, geoRoot, config);
            var geography = new WorldGeography(loaded, world.Map.DynamicOverrides);
            world.Geography = geography;
            return geography;
        }

        private static GeoBundleManifest ReadManifestAndVerify(string geoRoot, WorldInitConfig config)
        {
            string manifestPath = Path.Combine(geoRoot, "manifest.txt");
            var manifest = WorldMapBundleReader.ReadManifest(manifestPath);
            if (!string.IsNullOrEmpty(config.GeoDataBuild) &&
                !string.Equals(config.GeoDataBuild, manifest.BuildId, StringComparison.Ordinal))
                throw new InvalidDataException("Requested geoDataBuild does not match bundle: " + config.GeoDataBuild);
            config.GeoDataBuild = manifest.BuildId;
            return manifest;
        }

        private static List<WorldMapBundle> LoadBundlesForStartRegion(GeoBundleManifest manifest, string geoRoot, WorldInitConfig config)
        {
            var loaded = new List<WorldMapBundle>();
            foreach (var chunk in manifest.Chunks)
            {
                if (chunk.Lod != MapLodLevel.Low && chunk.Lod != MapLodLevel.High) continue;
                string fullPath = Path.Combine(geoRoot, chunk.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                VerifyChecksum(fullPath, chunk.Checksum);
                WorldMapBundle bundle;
                if (chunk.Lod == MapLodLevel.High)
                {
                    double cLat = config.StartRegionCenterLat;
                    double cLon = config.StartRegionCenterLon;
                    double radius = Math.Max(0.5, config.StartRegionRadiusDeg);
                    // 流式读取: 只物化起始区域内的 High tile, 不先物化整个 High 网格 (Task 4)
                    bundle = WorldMapBundleReader.ReadBundle(fullPath, c => WithinStartRegion(c, cLat, cLon, radius));
                }
                else
                {
                    bundle = WorldMapBundleReader.ReadBundle(fullPath);
                }
                loaded.Add(bundle);
            }
            return loaded;
        }

        private static bool WithinStartRegion(GeoCoordinate c, double centerLat, double centerLon, double radius)
        {
            double dLat = c.Latitude - centerLat;
            double dLon = EquirectangularProjection.WrappedLongitudeDistance(c.Longitude, centerLon);
            dLon *= Math.Cos((c.Latitude + centerLat) * 0.5 * Math.PI / 180.0);
            return Math.Sqrt(dLat * dLat + dLon * dLon) <= radius;
        }

        private static void AssignWorldMapState(WorldState world, GeoBundleManifest manifest, WorldInitConfig config, WorldGeography geography)
        {
            if (world == null) return;
            world.Geography = geography;
            world.Map.GeoDataBuild = manifest.BuildId;
            world.Map.ConfigKey = config.PresetKey ?? "custom";
            world.Map.ManifestChecksum = manifest.ManifestChecksum;
            world.Map.Config = new WorldMapConfigSnapshot
            {
                StartEra = (int)config.StartEra,
                StartMode = (int)config.StartMode,
                BorderYear = config.BorderYear,
                UseRealBorders = config.UseRealBorders,
                BorderView = (int)config.BorderView,
                StartRegionCenterLat = config.StartRegionCenterLat,
                StartRegionCenterLon = config.StartRegionCenterLon,
                StartRegionRadiusDeg = config.StartRegionRadiusDeg
            };
            world.Map.StaticChunks = new List<WorldMapChunkRef>(manifest.Chunks);
        }

        public static void VerifyChecksum(string path, string expected)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            string actual = ToHex(sha.ComputeHash(stream));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Geo bundle checksum mismatch: " + path);
        }

        private static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 15];
            }
            return new string(chars);
        }
    }
}

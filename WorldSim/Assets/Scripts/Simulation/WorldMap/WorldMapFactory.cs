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
        /// <summary>S5-3：管理远域 Low 异步装载；可为 null（仅当未启动延迟装载）。</summary>
        public WorldMapLodStreamer LodStreamer;
    }

    public static class WorldMapFactory
    {
        /// <summary>
        /// S5-3 确定性启动：同步 High（起始区）+ 焦点 Mid；远域 Low 异步延迟装载，不阻塞返回。
        /// 需要完整远域时调用 result.LodStreamer.EnsureFarFieldLoaded()。
        /// </summary>
        public static WorldMapBuildResult Build(string geoRoot, WorldInitConfig config, WorldState world = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var manifest = ReadManifestAndVerify(geoRoot, config);
            var critical = WorldMapLodStreamer.LoadCriticalBundles(manifest, geoRoot, config);
            var geography = new WorldGeography(critical, world?.Map?.DynamicOverrides);
            var streamer = new WorldMapLodStreamer();
            streamer.BeginDeferredFarField(manifest, geoRoot, geography);
            AssignWorldMapState(world, manifest, config, geography);
            return new WorldMapBuildResult
            {
                Manifest = manifest,
                Geography = geography,
                LodStreamer = streamer
            };
        }

        /// <summary>
        /// Epic 7 SV2：存档加载后重建 Geography（High + 焦点 Mid 同步；远域 Low 异步）。
        /// 立即返回，不阻塞逻辑 tick；需要完整远域时调用 result.LodStreamer.EnsureFarFieldLoaded()。
        /// </summary>
        public static WorldMapBuildResult RebuildGeographyDeferred(WorldState world, string geoRoot)
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
            var critical = WorldMapLodStreamer.LoadCriticalBundles(manifest, geoRoot, config);
            var geography = new WorldGeography(critical, world.Map.DynamicOverrides);
            var streamer = new WorldMapLodStreamer();
            streamer.BeginDeferredFarField(manifest, geoRoot, geography);
            world.Geography = geography;
            return new WorldMapBuildResult
            {
                Manifest = manifest,
                Geography = geography,
                LodStreamer = streamer
            };
        }

        /// <summary>
        /// 存档加载后重建 Geography：先同步 High+焦点 Mid，再等待远域 Low 完成，
        /// 保证读档续跑与 Replay 腿看到完整远域（逻辑 tick 本身不做 IO）。
        /// </summary>
        public static WorldGeography RebuildGeography(WorldState world, string geoRoot)
        {
            var result = RebuildGeographyDeferred(world, geoRoot);
            result.LodStreamer.EnsureFarFieldLoaded();
            return result.Geography;
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

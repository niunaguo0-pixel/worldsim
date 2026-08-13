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
            string manifestPath = Path.Combine(geoRoot, "manifest.txt");
            var manifest = WorldMapBundleReader.ReadManifest(manifestPath);
            if (!string.IsNullOrEmpty(config.GeoDataBuild) &&
                !string.Equals(config.GeoDataBuild, manifest.BuildId, StringComparison.Ordinal))
                throw new InvalidDataException("Requested geoDataBuild does not match bundle: " + config.GeoDataBuild);
            config.GeoDataBuild = manifest.BuildId;

            var loaded = new List<WorldMapBundle>();
            foreach (var chunk in manifest.Chunks)
            {
                if (chunk.Lod != MapLodLevel.Low && chunk.Lod != MapLodLevel.High) continue;
                string fullPath = Path.Combine(geoRoot, chunk.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                VerifyChecksum(fullPath, chunk.Checksum);
                var bundle = WorldMapBundleReader.ReadBundle(fullPath);
                if (bundle.Lod == MapLodLevel.High)
                    bundle = KeepStartRegion(bundle, config);
                loaded.Add(bundle);
            }
            var geography = new WorldGeography(loaded, world?.Map?.DynamicOverrides);
            if (world != null)
            {
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
                    StartRegionCenterLat = config.StartRegionCenterLat,
                    StartRegionCenterLon = config.StartRegionCenterLon,
                    StartRegionRadiusDeg = config.StartRegionRadiusDeg
                };
                world.Map.StaticChunks = new List<WorldMapChunkRef>(manifest.Chunks);
            }
            return new WorldMapBuildResult { Manifest = manifest, Geography = geography };
        }

        public static void VerifyChecksum(string path, string expected)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            string actual = ToHex(sha.ComputeHash(stream));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Geo bundle checksum mismatch: " + path);
        }

        private static WorldMapBundle KeepStartRegion(WorldMapBundle source, WorldInitConfig config)
        {
            var result = new WorldMapBundle
            {
                BuildId = source.BuildId, Lod = source.Lod, Width = source.Width, Height = source.Height
            };
            double radius = Math.Max(0.5, config.StartRegionRadiusDeg);
            foreach (var pair in source.Tiles)
            {
                var c = pair.Value.Coordinate;
                double dLat = c.Latitude - config.StartRegionCenterLat;
                double dLon = EquirectangularProjection.WrappedLongitudeDistance(c.Longitude, config.StartRegionCenterLon);
                dLon *= Math.Cos((c.Latitude + config.StartRegionCenterLat) * 0.5 * Math.PI / 180.0);
                if (Math.Sqrt(dLat * dLat + dLon * dLon) <= radius)
                    result.Tiles[pair.Key] = pair.Value;
            }
            return result;
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

namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using WorldSim.Simulation.Core.WorldGeography;

    public sealed class GeoBundleManifest
    {
        public string SchemaVersion = "";
        public string BuildId = "";
        public string Fidelity = "";
        public string ManifestChecksum = "";
        public string SourcesLockSha256 = "";
        public readonly Dictionary<string, string> Conversion = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<WorldMapChunkRef> Chunks = new List<WorldMapChunkRef>();
        public readonly List<GeoBundleAssetRef> Assets = new List<GeoBundleAssetRef>();
    }

    public sealed class GeoBundleAssetRef
    {
        public string RelativePath = "";
        public string Checksum = "";
    }

    public sealed class WorldMapBundle
    {
        public string BuildId = "";
        public MapLodLevel Lod;
        public int Width;
        public int Height;
        public readonly Dictionary<int, WorldTileData> Tiles = new Dictionary<int, WorldTileData>();
    }

    /// <summary>读取可离线复现的 gzip 二进制派生包；不依赖 Unity。</summary>
    public static class WorldMapBundleReader
    {
        private const int Magic = 0x31475357; // WSG1 little-endian

        public static GeoBundleManifest ReadManifest(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Geo manifest missing", path);
            var result = new GeoBundleManifest();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals);
                string value = line.Substring(equals + 1);
                if (key == "schemaVersion") result.SchemaVersion = value;
                else if (key == "buildId") result.BuildId = value;
                else if (key == "fidelity") result.Fidelity = value;
                else if (key == "manifestChecksum") result.ManifestChecksum = value;
                else if (key == "sourcesLockSha256") result.SourcesLockSha256 = value;
                else if (key.StartsWith("conversion.", StringComparison.Ordinal))
                {
                    result.Conversion[key.Substring("conversion.".Length)] = value;
                }
                else if (key == "chunk")
                {
                    string[] p = value.Split('|');
                    if (p.Length != 4) throw new InvalidDataException("Malformed chunk line: " + line);
                    result.Chunks.Add(new WorldMapChunkRef
                    {
                        ChunkId = p[0],
                        Lod = (MapLodLevel)Enum.Parse(typeof(MapLodLevel), p[1], true),
                        RelativePath = p[2],
                        Checksum = p[3]
                    });
                }
                else if (key == "asset")
                {
                    string[] p = value.Split('|');
                    if (p.Length != 2) throw new InvalidDataException("Malformed asset line: " + line);
                    result.Assets.Add(new GeoBundleAssetRef
                    {
                        RelativePath = p[0],
                        Checksum = p[1]
                    });
                }
            }
            if (string.IsNullOrEmpty(result.BuildId) || result.Chunks.Count == 0)
                throw new InvalidDataException("Incomplete geo manifest: " + path);
            return result;
        }

        public static WorldMapBundle ReadBundle(string path) => ReadBundle(path, false, null);

        /// <summary>
        /// 流式读取: 对 High LOD 只保留 keepPredicate(coordinate) 为真的 tile, 避免先把整个
        /// High 网格物化进内存 (Task 4). Low/Mid 始终全量保留。predicate 为 null 时等价于全量。
        /// </summary>
        public static WorldMapBundle ReadBundle(string path, Func<GeoCoordinate, bool> keepPredicate)
            => ReadBundle(path, keepPredicate != null, keepPredicate);

        private static WorldMapBundle ReadBundle(string path, bool usePredicate, Func<GeoCoordinate, bool> keepPredicate = null)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Geo bundle missing", path);
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            if (reader.ReadInt32() != Magic) throw new InvalidDataException("Bad geo bundle magic: " + path);
            int version = reader.ReadInt32();
            if (version != 1) throw new InvalidDataException("Unsupported geo bundle version " + version);
            var bundle = new WorldMapBundle
            {
                Lod = (MapLodLevel)reader.ReadByte(),
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                BuildId = reader.ReadString()
            };
            int count = reader.ReadInt32();
            if (count != bundle.Width * bundle.Height) throw new InvalidDataException("Geo tile count mismatch");
            bool filterHigh = usePredicate && bundle.Lod == MapLodLevel.High;
            for (int i = 0; i < count; i++)
            {
                byte flags = reader.ReadByte();
                var biome = (BiomeType)reader.ReadByte();
                var climate = (ClimateZone)reader.ReadByte();
                double elevation = reader.ReadInt16();
                double slope = reader.ReadByte() / 10.0;
                double temperature = reader.ReadInt16() / 10.0;
                double rainfall = reader.ReadUInt16();
                int x = i % bundle.Width;
                int y = i / bundle.Width;
                int id = EquirectangularProjection.EncodeTileId(bundle.Lod, x, y);
                var coordinate = EquirectangularProjection.ToCoordinate(id);
                if (filterHigh && !keepPredicate(coordinate)) continue;
                bundle.Tiles[id] = new WorldTileData
                {
                    TileId = id,
                    Coordinate = coordinate,
                    IsLand = (flags & 1) != 0,
                    HasCoast = (flags & 2) != 0,
                    HasWater = (flags & 4) != 0,
                    HasRiver = (flags & 8) != 0,
                    IsInterpolated = (flags & 16) != 0,
                    Biome = biome,
                    Climate = climate,
                    ElevationMeters = elevation,
                    Slope = slope,
                    BaseTemperatureC = temperature,
                    BaseRainfallMm = rainfall,
                    Lod = bundle.Lod
                };
            }
            return bundle;
        }
    }
}

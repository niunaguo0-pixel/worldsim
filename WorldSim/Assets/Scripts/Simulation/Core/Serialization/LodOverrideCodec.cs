namespace WorldSim.Simulation.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>
    /// Epic 7 SV1：DynamicOverrides LOD 分区编解码。
    /// High 逐 tile 全量；Mid/Low 按父级 Low 格聚合压缩（有损）。
    /// TileId 编解码与 WorldMap.EquirectangularProjection 对齐。
    /// </summary>
    public static class LodOverrideCodec
    {
        public static void WritePartitioned(
            DeterministicBinaryWriter w,
            IReadOnlyList<WorldTileOverride> overrides)
        {
            var high = new List<WorldTileOverride>();
            var midLow = new List<WorldTileOverride>();
            if (overrides != null)
            {
                foreach (var item in overrides)
                {
                    MapLodLevel lod = LodOf(item.TileId);
                    if (lod == MapLodLevel.High) high.Add(item);
                    else midLow.Add(item);
                }
            }

            high.Sort((a, b) => a.TileId.CompareTo(b.TileId));
            w.WriteInt32(high.Count);
            foreach (var item in high)
                WriteFullOverride(w, item);

            var aggregates = AggregateToLowParents(midLow);
            aggregates.Sort((a, b) => a.ParentLowTileId.CompareTo(b.ParentLowTileId));
            w.WriteInt32(aggregates.Count);
            foreach (var agg in aggregates)
            {
                w.WriteInt32(agg.ParentLowTileId);
                w.WriteInt32(agg.SampleCount);
                w.WriteBool(agg.HasElevation);
                w.WriteDouble(agg.ElevationMeters);
                w.WriteBool(agg.HasBiome);
                w.WriteByte((byte)agg.Biome);
            }
        }

        public static List<WorldTileOverride> ReadPartitioned(DeterministicBinaryReader r)
        {
            var result = new List<WorldTileOverride>();
            int highCount = r.ReadInt32();
            for (int i = 0; i < highCount; i++)
                result.Add(ReadFullOverride(r));

            int aggCount = r.ReadInt32();
            for (int i = 0; i < aggCount; i++)
            {
                int parentLow = r.ReadInt32();
                r.ReadInt32(); // sampleCount（诊断用，展开不需要）
                bool hasElev = r.ReadBool();
                double elev = r.ReadDouble();
                bool hasBiome = r.ReadBool();
                var biome = (BiomeType)r.ReadByte();
                result.Add(new WorldTileOverride
                {
                    TileId = parentLow,
                    HasElevation = hasElev,
                    ElevationMeters = elev,
                    HasBiome = hasBiome,
                    Biome = biome
                });
            }

            result.Sort((a, b) => a.TileId.CompareTo(b.TileId));
            return result;
        }

        public static void WriteFlat(
            DeterministicBinaryWriter w,
            IReadOnlyList<WorldTileOverride> overrides)
        {
            var changes = new List<WorldTileOverride>(overrides ?? Array.Empty<WorldTileOverride>());
            changes.Sort((a, b) => a.TileId.CompareTo(b.TileId));
            w.WriteInt32(changes.Count);
            foreach (var item in changes)
                WriteFullOverride(w, item);
        }

        public static List<WorldTileOverride> ReadFlat(DeterministicBinaryReader r)
        {
            int count = r.ReadInt32();
            var list = new List<WorldTileOverride>(count);
            for (int i = 0; i < count; i++)
                list.Add(ReadFullOverride(r));
            return list;
        }

        /// <summary>测试用：对 Mid/Low 列表做与落盘相同的聚合展开。</summary>
        public static List<WorldTileOverride> CompressExpandForTest(IReadOnlyList<WorldTileOverride> overrides)
        {
            using var w = new DeterministicBinaryWriter();
            WritePartitioned(w, overrides);
            using var r = new DeterministicBinaryReader(w.ToArray());
            return ReadPartitioned(r);
        }

        private static List<AggregatedOverride> AggregateToLowParents(List<WorldTileOverride> midLow)
        {
            var buckets = new Dictionary<int, AggAcc>();
            foreach (var item in midLow)
            {
                int parent = ToParentLowTileId(item.TileId);
                if (!buckets.TryGetValue(parent, out AggAcc acc))
                {
                    acc = new AggAcc();
                    buckets[parent] = acc;
                }
                acc.SampleCount++;
                if (item.HasElevation)
                {
                    acc.HasElevation = true;
                    acc.ElevSum += item.ElevationMeters;
                    acc.ElevSamples++;
                }
                if (item.HasBiome)
                {
                    acc.HasBiome = true;
                    if (!acc.BiomeVotes.TryGetValue(item.Biome, out int votes))
                        votes = 0;
                    acc.BiomeVotes[item.Biome] = votes + 1;
                }
            }

            var list = new List<AggregatedOverride>(buckets.Count);
            foreach (var pair in buckets)
            {
                AggAcc acc = pair.Value;
                double elev = 0;
                if (acc.ElevSamples > 0)
                    elev = DeterminismMath.Quantize(acc.ElevSum / acc.ElevSamples, 1);
                BiomeType biome = BiomeType.Ocean;
                if (acc.HasBiome)
                {
                    int best = -1;
                    foreach (var vote in acc.BiomeVotes)
                    {
                        if (vote.Value > best || (vote.Value == best && (byte)vote.Key < (byte)biome))
                        {
                            best = vote.Value;
                            biome = vote.Key;
                        }
                    }
                }
                list.Add(new AggregatedOverride
                {
                    ParentLowTileId = pair.Key,
                    SampleCount = acc.SampleCount,
                    HasElevation = acc.HasElevation,
                    ElevationMeters = elev,
                    HasBiome = acc.HasBiome,
                    Biome = biome
                });
            }
            return list;
        }

        private static void WriteFullOverride(DeterministicBinaryWriter w, WorldTileOverride item)
        {
            w.WriteInt32(item.TileId);
            w.WriteBool(item.HasElevation);
            w.WriteDouble(item.ElevationMeters);
            w.WriteBool(item.HasBiome);
            w.WriteByte((byte)item.Biome);
        }

        private static WorldTileOverride ReadFullOverride(DeterministicBinaryReader r) =>
            new WorldTileOverride
            {
                TileId = r.ReadInt32(),
                HasElevation = r.ReadBool(),
                ElevationMeters = r.ReadDouble(),
                HasBiome = r.ReadBool(),
                Biome = (BiomeType)r.ReadByte()
            };

        public static MapLodLevel LodOf(int tileId)
        {
            int band = tileId / 1000000;
            if (band < 1 || band > 3) throw new ArgumentOutOfRangeException(nameof(tileId));
            return (MapLodLevel)(band - 1);
        }

        public static int ToParentLowTileId(int tileId)
        {
            Decode(tileId, out MapLodLevel lod, out int x, out int y);
            if (lod == MapLodLevel.Low)
                return tileId;
            double lon = -180.0 + (x + 0.5) * 360.0 / Width(lod);
            double lat = 90.0 - (y + 0.5) * 180.0 / Height(lod);
            return ToTileId(lat, lon, MapLodLevel.Low);
        }

        private static int Width(MapLodLevel lod) =>
            lod == MapLodLevel.High ? 720 : lod == MapLodLevel.Mid ? 360 : 180;

        private static int Height(MapLodLevel lod) => Width(lod) / 2;

        private static void Decode(int tileId, out MapLodLevel lod, out int x, out int y)
        {
            int band = tileId / 1000000;
            if (band < 1 || band > 3) throw new ArgumentOutOfRangeException(nameof(tileId));
            lod = (MapLodLevel)(band - 1);
            int local = tileId % 1000000;
            int width = Width(lod);
            x = local % width;
            y = local / width;
        }

        private static int ToTileId(double lat, double lon, MapLodLevel lod)
        {
            int width = Width(lod);
            int height = Height(lod);
            lon = GeoCoordinate.NormalizeLongitude(lon);
            int x = Math.Min(width - 1, Math.Max(0, (int)Math.Floor((lon + 180.0) / 360.0 * width)));
            int y = Math.Min(height - 1, Math.Max(0, (int)Math.Floor((90.0 - lat) / 180.0 * height)));
            return ((int)lod + 1) * 1000000 + y * width + x;
        }

        private sealed class AggAcc
        {
            public int SampleCount;
            public bool HasElevation;
            public double ElevSum;
            public int ElevSamples;
            public bool HasBiome;
            public readonly Dictionary<BiomeType, int> BiomeVotes = new Dictionary<BiomeType, int>();
        }

        private struct AggregatedOverride
        {
            public int ParentLowTileId;
            public int SampleCount;
            public bool HasElevation;
            public double ElevationMeters;
            public bool HasBiome;
            public BiomeType Biome;
        }
    }
}

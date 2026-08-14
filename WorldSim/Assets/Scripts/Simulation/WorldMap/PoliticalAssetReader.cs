namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Text;

    /// <summary>WSP1 (WorldSim Political v1) 国别/主权/争议/城市资产读取器 (Task 4).
    /// 与 tools/geo/political_binary.py 的二进制布局逐字段对齐；gzip 容器解压后读取。
    /// 不编造裁决: 争议区按源 TYPE/NOTE_ADM0/NOTE_BRK 与 claimant (admin/sovereign) 原样保留。
    /// </summary>
    public static class PoliticalAssetReader
    {
        public const uint Magic = 0x31505357; // "WSP1" little-endian
        public const byte FormatVersion = 1;
        public const int SupportedBorderYear = 2026;

        public static PoliticalAsset Read(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Political asset missing", path);
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);
            return ReadCore(reader, path);
        }

        /// <summary>从已解压的字节流读取 WSP1 (供测试与内存往返).</summary>
        public static PoliticalAsset ReadBytes(byte[] payload)
        {
            using var ms = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            return ReadCore(reader, "<bytes>");
        }

        private static PoliticalAsset ReadCore(BinaryReader reader, string source)
        {
            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad WSP1 magic: 0x{magic:X8} at {source}");
            byte version = reader.ReadByte();
            if (version != FormatVersion)
                throw new InvalidDataException($"Unsupported WSP1 version {version} at {source}");
            ushort borderYear = reader.ReadUInt16();
            if (borderYear != SupportedBorderYear)
                throw new NotSupportedException(
                    $"borderYear {borderYear} is not supported: WSP1 is a {SupportedBorderYear} " +
                    $"snapshot of Natural Earth current data, not a historical border product; " +
                    $"only {SupportedBorderYear} is allowed until later tasks add historical snapshots.");
            string buildId = reader.ReadString(); // .NET 7-bit 变长前缀 UTF-8
            // M3: 计数字段与 Python struct.pack("<IIII") 无符号 u32 对齐 (political_binary.py:304,326)
            uint nDeFacto = reader.ReadUInt32();
            uint nSovereignty = reader.ReadUInt32();
            uint nDisputed = reader.ReadUInt32();
            uint nCities = reader.ReadUInt32();

            var asset = new PoliticalAsset
            {
                BorderYear = borderYear,
                BuildId = buildId
            };
            for (uint i = 0; i < nDeFacto; i++)
                asset.DeFactoCountries.Add(ReadCountry(reader));
            for (uint i = 0; i < nSovereignty; i++)
                asset.SovereigntyClaims.Add(ReadCountry(reader));
            for (uint i = 0; i < nDisputed; i++)
                asset.DisputedAreas.Add(ReadDisputed(reader));
            for (uint i = 0; i < nCities; i++)
                asset.Cities.Add(ReadCity(reader));
            // Important 3: 尾随字节 fail-closed, 与 Python political_binary.py:341-342 跨实现一致。
            // GZipStream 不可寻址 (CanSeek=false, Position/Length 抛 NotSupportedException),
            // 故分两路: 可寻址流 (MemoryStream/ReadBytes) 用 Position/Length 报告字节数;
            // 不可寻址流 (gzip) 用 ReadByte 探测 — 读到字节 = 有尾随, EndOfStream = 恰好读完。
            if (reader.BaseStream.CanSeek)
            {
                long trailing = reader.BaseStream.Length - reader.BaseStream.Position;
                if (trailing > 0)
                    throw new InvalidDataException(
                        $"trailing {trailing} bytes in WSP1 payload at {source}");
            }
            else
            {
                try { reader.ReadByte(); throw new InvalidDataException(
                    $"trailing bytes in WSP1 payload at {source}"); }
                catch (EndOfStreamException) { /* exactly consumed */ }
            }
            return asset;
        }

        private static string Fixed3(BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(3);
            if (b.Length != 3) throw new EndOfStreamException("WSP1 fixed3 truncated");
            return Encoding.ASCII.GetString(b);
        }

        private static List<GeoRing> ReadRings(BinaryReader reader)
        {
            int ringCount = reader.ReadInt32();
            var rings = new List<GeoRing>(ringCount);
            for (int r = 0; r < ringCount; r++)
            {
                int pointCount = reader.ReadInt32();
                var points = new List<GeoPoint>(pointCount);
                for (int p = 0; p < pointCount; p++)
                {
                    double lon = reader.ReadDouble();
                    double lat = reader.ReadDouble();
                    points.Add(new GeoPoint(lon, lat));
                }
                rings.Add(new GeoRing(points));
            }
            return rings;
        }

        private static PoliticalCountryRecord ReadCountry(BinaryReader reader)
        {
            return new PoliticalCountryRecord
            {
                StableId = Fixed3(reader),
                AdminId = Fixed3(reader),
                SovereignId = Fixed3(reader),
                IsoA3Eh = Fixed3(reader),
                Name = reader.ReadString(),
                NameLong = reader.ReadString(),
                SovereignName = reader.ReadString(),
                Continent = reader.ReadString(),
                RegionUn = reader.ReadString(),
                Subregion = reader.ReadString(),
                FeatureClass = reader.ReadString(),
                Type = reader.ReadString(),
                NoteAdm0 = reader.ReadString(),
                WikidataId = reader.ReadString(),
                PopEst = reader.ReadInt64(),
                Rings = ReadRings(reader)
            };
        }

        private static PoliticalDisputedRecord ReadDisputed(BinaryReader reader)
        {
            return new PoliticalDisputedRecord
            {
                StableId = Fixed3(reader),
                AdminId = Fixed3(reader),
                SovereignId = Fixed3(reader),
                IsoA3Eh = Fixed3(reader),
                Name = reader.ReadString(),
                NameLong = reader.ReadString(),
                AdminName = reader.ReadString(),
                SovereignName = reader.ReadString(),
                Type = reader.ReadString(),
                NoteAdm0 = reader.ReadString(),
                NoteBrk = reader.ReadString(),
                WikidataId = reader.ReadString(),
                PopEst = reader.ReadInt64(),
                Rings = ReadRings(reader)
            };
        }

        private static PoliticalCityRecord ReadCity(BinaryReader reader)
        {
            return new PoliticalCityRecord
            {
                StableId = reader.ReadInt64(),
                Name = reader.ReadString(),
                NameAscii = reader.ReadString(),
                FeatureClass = reader.ReadString(),
                AdminId = Fixed3(reader),
                SovereignId = Fixed3(reader),
                AdminName = reader.ReadString(),
                SovereignName = reader.ReadString(),
                Scalerank = reader.ReadInt32(),
                NatScale = reader.ReadInt32(),
                IsCapital = reader.ReadByte(),
                IsWorldCity = reader.ReadByte(),
                IsMegaCity = reader.ReadByte(),
                PopMax = reader.ReadInt64(),
                PopMin = reader.ReadInt64(),
                Longitude = reader.ReadDouble(),
                Latitude = reader.ReadDouble(),
                WikidataId = reader.ReadString()
            };
        }
    }

    public readonly struct GeoPoint
    {
        public readonly double Longitude;
        public readonly double Latitude;
        public GeoPoint(double lon, double lat) { Longitude = lon; Latitude = lat; }
    }

    public sealed class GeoRing
    {
        public readonly List<GeoPoint> Points;
        public GeoRing(List<GeoPoint> points) { Points = points; }
        public int Count => Points.Count;
    }

    public sealed class PoliticalCountryRecord
    {
        public string StableId = "";
        public string AdminId = "";
        public string SovereignId = "";
        public string IsoA3Eh = "";
        public string Name = "";
        public string NameLong = "";
        public string SovereignName = "";
        public string Continent = "";
        public string RegionUn = "";
        public string Subregion = "";
        public string FeatureClass = "";
        public string Type = "";
        public string NoteAdm0 = "";
        public string WikidataId = "";
        public long PopEst;
        public List<GeoRing> Rings = new List<GeoRing>();
    }

    public sealed class PoliticalDisputedRecord
    {
        public string StableId = "";
        public string AdminId = "";
        public string SovereignId = "";
        public string IsoA3Eh = "";
        public string Name = "";
        public string NameLong = "";
        public string AdminName = "";
        public string SovereignName = "";
        public string Type = "";
        public string NoteAdm0 = "";
        public string NoteBrk = "";
        public string WikidataId = "";
        public long PopEst;
        public List<GeoRing> Rings = new List<GeoRing>();
    }

    public sealed class PoliticalCityRecord
    {
        public long StableId;
        public string Name = "";
        public string NameAscii = "";
        public string FeatureClass = "";
        public string AdminId = "";
        public string SovereignId = "";
        public string AdminName = "";
        public string SovereignName = "";
        public int Scalerank;
        public int NatScale;
        public byte IsCapital;
        public byte IsWorldCity;
        public byte IsMegaCity;
        public long PopMax;
        public long PopMin;
        public double Longitude;
        public double Latitude;
        public string WikidataId = "";
    }

    public sealed class PoliticalAsset
    {
        public int BorderYear;
        public string BuildId = "";
        public readonly List<PoliticalCountryRecord> DeFactoCountries = new List<PoliticalCountryRecord>();
        public readonly List<PoliticalCountryRecord> SovereigntyClaims = new List<PoliticalCountryRecord>();
        public readonly List<PoliticalDisputedRecord> DisputedAreas = new List<PoliticalDisputedRecord>();
        public readonly List<PoliticalCityRecord> Cities = new List<PoliticalCityRecord>();

        /// <summary>按 BorderView 选择国家视图 (de-facto 或 sovereignty), 已按 (stableId, name) 排序.</summary>
        public IReadOnlyList<PoliticalCountryRecord> CountriesByView(BorderView view) =>
            view == BorderView.SovereigntyClaims ? SovereigntyClaims : DeFactoCountries;
    }
}

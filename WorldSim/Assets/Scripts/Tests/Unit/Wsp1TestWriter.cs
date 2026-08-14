// Task 4 测试专用: 合成 WSP1 字节流 (与 tools/geo/political_binary.py 布局对齐),
// 供 PoliticalAssetReader 往返/聚合/争议标记测试使用, 不依赖真实缓存。

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WorldSim.Tests.Unit
{
    internal static class Wsp1TestWriter
    {
        public const uint Magic = 0x31505357;
        public const byte FormatVersion = 1;
        public const ushort BorderYear = 2026;

        public sealed class Country
        {
            public string StableId, AdminId, SovereignId, IsoA3Eh;
            public string Name, NameLong, SovereignName, Continent, RegionUn, Subregion;
            public string FeatureClass, Type, NoteAdm0, WikidataId;
            public long PopEst;
            public List<(double lon, double lat)> Ring = new List<(double, double)>();
        }
        public sealed class Disputed
        {
            public string StableId, AdminId, SovereignId, IsoA3Eh;
            public string Name, NameLong, AdminName, SovereignName;
            public string Type, NoteAdm0, NoteBrk, WikidataId;
            public long PopEst;
            public List<(double lon, double lat)> Ring = new List<(double, double)>();
        }
        public sealed class City
        {
            public long StableId;
            public string Name, NameAscii, FeatureClass, AdminId, SovereignId;
            public string AdminName, SovereignName, WikidataId;
            public int Scalerank, NatScale;
            public byte IsCapital, IsWorldCity, IsMegaCity;
            public long PopMax, PopMin;
            public double Lon, Lat;
        }
        public sealed class Asset
        {
            public List<Country> DeFacto = new List<Country>();
            public List<Country> Sovereignty = new List<Country>();
            public List<Disputed> Disputed = new List<Disputed>();
            public List<City> Cities = new List<City>();
        }

        public static byte[] Serialize(Asset asset, string buildId)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(BorderYear);
            WriteString(w, buildId);
            w.Write(asset.DeFacto.Count);
            w.Write(asset.Sovereignty.Count);
            w.Write(asset.Disputed.Count);
            w.Write(asset.Cities.Count);
            foreach (var c in asset.DeFacto) WriteCountry(w, c);
            foreach (var c in asset.Sovereignty) WriteCountry(w, c);
            foreach (var d in asset.Disputed) WriteDisputed(w, d);
            foreach (var c in asset.Cities) WriteCity(w, c);
            w.Flush();
            return ms.ToArray();
        }

        private static void WriteString(BinaryWriter w, string s) => w.Write(s ?? "");
        private static void WriteFixed3(BinaryWriter w, string s)
        {
            byte[] b = new byte[3];
            byte[] src = Encoding.ASCII.GetBytes(s ?? "   ");
            for (int i = 0; i < 3 && i < src.Length; i++) b[i] = src[i];
            w.Write(b);
        }
        private static void WriteRing(BinaryWriter w, List<(double lon, double lat)> ring)
        {
            w.Write(1);
            w.Write(ring.Count);
            foreach (var p in ring) { w.Write(p.lon); w.Write(p.lat); }
        }
        private static void WriteCountry(BinaryWriter w, Country c)
        {
            WriteFixed3(w, c.StableId); WriteFixed3(w, c.AdminId);
            WriteFixed3(w, c.SovereignId); WriteFixed3(w, c.IsoA3Eh);
            WriteString(w, c.Name); WriteString(w, c.NameLong); WriteString(w, c.SovereignName);
            WriteString(w, c.Continent); WriteString(w, c.RegionUn); WriteString(w, c.Subregion);
            WriteString(w, c.FeatureClass); WriteString(w, c.Type); WriteString(w, c.NoteAdm0);
            WriteString(w, c.WikidataId);
            w.Write(c.PopEst);
            WriteRing(w, c.Ring);
        }
        private static void WriteDisputed(BinaryWriter w, Disputed d)
        {
            WriteFixed3(w, d.StableId); WriteFixed3(w, d.AdminId);
            WriteFixed3(w, d.SovereignId); WriteFixed3(w, d.IsoA3Eh);
            WriteString(w, d.Name); WriteString(w, d.NameLong); WriteString(w, d.AdminName);
            WriteString(w, d.SovereignName); WriteString(w, d.Type); WriteString(w, d.NoteAdm0);
            WriteString(w, d.NoteBrk); WriteString(w, d.WikidataId);
            w.Write(d.PopEst);
            WriteRing(w, d.Ring);
        }
        private static void WriteCity(BinaryWriter w, City c)
        {
            w.Write(c.StableId);
            WriteString(w, c.Name); WriteString(w, c.NameAscii); WriteString(w, c.FeatureClass);
            WriteFixed3(w, c.AdminId); WriteFixed3(w, c.SovereignId);
            WriteString(w, c.AdminName); WriteString(w, c.SovereignName);
            w.Write(c.Scalerank); w.Write(c.NatScale);
            w.Write(c.IsCapital); w.Write(c.IsWorldCity); w.Write(c.IsMegaCity);
            w.Write(c.PopMax); w.Write(c.PopMin);
            w.Write(c.Lon); w.Write(c.Lat);
            WriteString(w, c.WikidataId);
        }
    }
}

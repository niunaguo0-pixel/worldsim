using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Civilization;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.Time;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    public partial class WorldMapTask4Tests
    {
        // ---------- Geography 重建 (存档加载后) ----------

        [Test]
        public void RebuildGeography_RestoresReadOnlyGeographyAfterLoad()
        {
            string root = RealGeoRoot();
            if (root == null) Assert.Ignore("build/geo-task4-full 不存在, 跳过真实地理测试");
            var world = new WorldState(11);
            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent", StartRegionCenterLat = 33,
                StartRegionCenterLon = 44, StartRegionRadiusDeg = 8
            };
            WorldMapFactory.Build(root, cfg, world);
            // Important 1: 挂 CivilizationSimEngine 让 Geography 进入模拟 (水邻增长加成)
            CivilizationSimEngine.AttachTo(world);
            byte[] snap = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(snap);
            Assert.IsNull(loaded.Geography, "Load 后 Geography 应为 null (transient)");
            var geo = WorldMapFactory.RebuildGeography(loaded, root);
            Assert.IsNotNull(geo);
            Assert.AreSame(geo, loaded.Geography);
            var near = geo.GetTile(new GeoCoordinate(33, 44), MapLodLevel.High);
            Assert.AreEqual(MapLodLevel.High, near.Lod);
            var far = geo.GetTile(new GeoCoordinate(-20, -60), MapLodLevel.High);
            Assert.AreEqual(MapLodLevel.Low, far.Lod);
            // 验证重建后 CivilizationSimEngine 可读 Geography (水邻查询不 NRE)
            CivilizationSimEngine.AttachTo(loaded);
            var orch = new SimOrchestrator(loaded);
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.IsNotNull(loaded.Geography, "推进后 Geography 仍可用");
        }

        // ---------- 四向 Replay (真实地理, 1×/20×, 存读档续跑) ----------

        [Test]
        public void Replay_FourWay_RealGeography_HashStable()
        {
            string root = RealGeoRoot();
            if (root == null) Assert.Ignore("build/geo-task4-full 不存在, 跳过真实地理测试");
            const ulong seed = 0xC0FFEE;
            const int endMonth = 40;
            // 四向: {1×, 20×} × {无存档, 存读档续跑}; 同速比较避免捕获粒度差异
            var baseline1x = CaptureMonthlyHashes(seed, 1, endMonth, root, saveAt: -1);
            var path4_1x = CaptureMonthlyHashes(seed, 1, endMonth, root, saveAt: 20);
            var baseline20x = CaptureMonthlyHashes(seed, 20, endMonth, root, saveAt: -1);
            var path4_20x = CaptureMonthlyHashes(seed, 20, endMonth, root, saveAt: 20);

            Assert.AreEqual(baseline1x.Count, path4_1x.Count, "1× 路径④ 月数一致");
            for (int i = 0; i < baseline1x.Count; i++)
                Assert.AreEqual(baseline1x[i], path4_1x[i], $"1× 路径④ 分叉于哈希序列索引 {i}");
            Assert.AreEqual(baseline20x.Count, path4_20x.Count, "20× 路径④ 月数一致");
            for (int i = 0; i < baseline20x.Count; i++)
                Assert.AreEqual(baseline20x[i], path4_20x[i], $"20× 路径④ 分叉于哈希序列索引 {i}");
            // 跨速: 1× 与 20× 终态月哈希一致 (确定契约 §3)
            Assert.AreEqual(baseline1x[baseline1x.Count - 1], baseline20x[baseline20x.Count - 1],
                "1× 与 20× 终态月哈希必须一致");
        }

        // Important 1 回归证明: 存读档 + RebuildGeography 后哈希与无存档腿一致;
        // 且 Geography 确实进入哈希 (不重建时存读档腿哈希会分叉, 证明修复有效)。
        [Test]
        public void Replay_SaveLoad_RebuildGeography_KeepsHashAlignedAndGeographyMatters()
        {
            string root = RealGeoRoot();
            if (root == null) Assert.Ignore("build/geo-task4-full 不存在, 跳过真实地理测试");
            const ulong seed = 0xBEEF;
            const int endMonth = 30;
            // 腿 A: 全程带地理, 不存读档
            var legA = CaptureMonthlyHashes(seed, 1, endMonth, root, saveAt: -1);
            // 腿 B: 存读档 + RebuildGeography (CaptureMonthlyHashes 已接线)
            var legB = CaptureMonthlyHashes(seed, 1, endMonth, root, saveAt: 15);
            Assert.AreEqual(legA.Count, legB.Count, "两腿月数一致");
            for (int i = 0; i < legA.Count; i++)
                Assert.AreEqual(legA[i], legB[i], $"存读档+重建腿与无存档腿分叉于索引 {i}");
            // 反证: 存读档但不重建地理 → 哈希必分叉 (Geography 确实影响哈希)
            var legC = CaptureMonthlyHashesNoRebuild(seed, 1, endMonth, root, saveAt: 15);
            Assert.AreNotEqual(legA[legA.Count - 1], legC[legC.Count - 1],
                "存读档不重建地理时终态哈希必须分叉, 证明 Geography 进入哈希");
        }

        // ---------- StreamingChunkCache 不改模拟哈希 ----------

        [Test]
        public async System.Threading.Tasks.Task PresentationChunkCache_DoesNotChangeHash_RealGeography()
        {
            string root = RealGeoRoot();
            if (root == null) Assert.Ignore("build/geo-task4-full 不存在, 跳过真实地理测试");
            var world = new WorldState(10);
            WorldMapFactory.Build(root, new WorldInitConfig
            {
                PresetKey = "fertile_crescent", StartRegionCenterLat = 33,
                StartRegionCenterLon = 44, StartRegionRadiusDeg = 8
            }, world);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            var cache = new WorldMapChunkCache();
            await cache.LoadPresentationAsync("mid", Path.Combine(root, "mid-global.wgeo.gz"));
            ulong after = WorldStateSerializer.ComputeMonthlyHash(world);
            Assert.AreEqual(before, after, "表现层 chunk 缓存不得改变模拟哈希");
        }

        // ---------- helpers ----------

        private static Wsp1TestWriter.Asset SampleAsset()
        {
            var a = new Wsp1TestWriter.Asset();
            var usa = new Wsp1TestWriter.Country
            {
                StableId = "USA", AdminId = "USA", SovereignId = "US1", IsoA3Eh = "USA",
                Name = "United States", NameLong = "United States of America",
                SovereignName = "United States of America", Continent = "North America",
                RegionUn = "Americas", Subregion = "Northern America",
                FeatureClass = "Admin-0 country", Type = "Sovereign country",
                NoteAdm0 = "", WikidataId = "Q30", PopEst = 331000000
            };
            usa.Ring.Add((-100.0, 40.0)); usa.Ring.Add((-90.0, 40.0)); usa.Ring.Add((-100.0, 30.0));
            var can = new Wsp1TestWriter.Country
            {
                StableId = "CAN", AdminId = "CAN", SovereignId = "US1", IsoA3Eh = "CAN",
                Name = "Canada", NameLong = "Canada", SovereignName = "United States of America",
                Continent = "North America", RegionUn = "Americas", Subregion = "Northern America",
                FeatureClass = "Admin-0 country", Type = "Sovereign country",
                NoteAdm0 = "", WikidataId = "Q16", PopEst = 38000000
            };
            can.Ring.Add((-120.0, 60.0)); can.Ring.Add((-60.0, 60.0)); can.Ring.Add((-60.0, 50.0));
            a.DeFacto.Add(can); a.DeFacto.Add(usa);

            var us1 = new Wsp1TestWriter.Country
            {
                StableId = "US1", AdminId = "US1", SovereignId = "US1", IsoA3Eh = "US1",
                Name = "United States", NameLong = "United States of America",
                SovereignName = "United States of America", Continent = "North America",
                RegionUn = "Americas", Subregion = "Northern America",
                FeatureClass = "Sovereignty", Type = "Sovereign country",
                NoteAdm0 = "", WikidataId = "Q30", PopEst = 369000000
            };
            us1.Ring.Add((-120.0, 60.0)); us1.Ring.Add((-60.0, 60.0)); us1.Ring.Add((-90.0, 30.0));
            a.Sovereignty.Add(us1);

            var kashmir = new Wsp1TestWriter.Disputed
            {
                StableId = "KAS", AdminId = "IND", SovereignId = "PAK", IsoA3Eh = "-99",
                Name = "Kashmir", NameLong = "Kashmir Region", AdminName = "India",
                SovereignName = "Pakistan", Type = "Disputed",
                NoteAdm0 = "Disputed by India and Pakistan", NoteBrk = "Claimed by both",
                WikidataId = "Q3737", PopEst = 12000000
            };
            kashmir.Ring.Add((75.0, 35.0)); kashmir.Ring.Add((80.0, 35.0)); kashmir.Ring.Add((78.0, 33.0));
            a.Disputed.Add(kashmir);

            var dc = new Wsp1TestWriter.City
            {
                StableId = 1, Name = "Washington D.C.", NameAscii = "Washington D.C.",
                FeatureClass = "Admin-0 capital", AdminId = "USA", SovereignId = "US1",
                AdminName = "United States", SovereignName = "United States of America",
                Scalerank = 0, NatScale = 1, IsCapital = 1, IsWorldCity = 1, IsMegaCity = 0,
                PopMax = 689545, PopMin = 601723, Lon = -77.0, Lat = 38.9, WikidataId = "Q61"
            };
            var ottawa = new Wsp1TestWriter.City
            {
                StableId = 2, Name = "Ottawa", NameAscii = "Ottawa",
                FeatureClass = "Admin-0 capital", AdminId = "CAN", SovereignId = "US1",
                AdminName = "Canada", SovereignName = "United States of America",
                Scalerank = 0, NatScale = 1, IsCapital = 1, IsWorldCity = 0, IsMegaCity = 0,
                PopMax = 994837, PopMin = 878110, Lon = -75.7, Lat = 45.4, WikidataId = "Q1930"
            };
            a.Cities.Add(dc); a.Cities.Add(ottawa);
            return a;
        }

        private static void WriteGzip(string path, byte[] payload)
        {
            using var raw = File.Create(path);
            using var gz = new GZipStream(raw, System.IO.Compression.CompressionLevel.Optimal);
            gz.Write(payload, 0, payload.Length);
        }

        /// <summary>定位 build/geo-task4-full (gitignored, 本地构建); 不存在返回 null.</summary>
        private static string RealGeoRoot()
        {
            // Application.dataPath = WorldSim/Assets; 项目根 = 上两级
            string projRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string candidate = Path.Combine(projRoot, "build", "geo-task4-full");
            return Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "manifest.txt"))
                ? candidate : null;
        }

        private static List<ulong> CaptureMonthlyHashes(ulong seed, int speed, int endMonth, string geoRoot, int saveAt)
        {
            var hashes = new List<ulong>();
            var world = WorldState.CreateMinimalSlice(seed, speed);
            world.InterventionLog.Add(new InterventionRecord(5, "nudge.eco"));
            // Important 1 修复: 四腿都带真实地理 — Build 把 Low 全量 + 起始区域 High 装入 world.Map
            // (StaticChunks + Config), 并赋 world.Geography; 不 Build 则存读档两腿 loaded.Geography==null,
            // CivilizationSimEngine 静默跳过水邻增长加成, 哈希不反映真实地理依赖。
            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent",
                StartRegionCenterLat = 33,
                StartRegionCenterLon = 44,
                StartRegionRadiusDeg = 8
            };
            WorldMapFactory.Build(geoRoot, cfg, world);
            // 挂 CivilizationSimEngine 让 Geography (水邻增长 ±0.003 / slope / IsLand) 进入月哈希
            CivilizationSimEngine.AttachTo(world);
            var orch = new SimOrchestrator(world);
            int lastEmitted = -1;
            float dt = 1f;
            while (world.Time.monthIndex < endMonth)
            {
                orch.Update(dt);
                if (world.Time.monthIndex != lastEmitted)
                {
                    lastEmitted = world.Time.monthIndex;
                    hashes.Add(WorldStateSerializer.ComputeMonthlyHash(world));
                    if (saveAt >= 0 && lastEmitted == saveAt)
                    {
                        byte[] snap = WorldStateSerializer.Save(world);
                        world = WorldStateSerializer.Load(snap);
                        // Important 1 修复: load 后 Geography==null (transient), 必须显式重建
                        WorldMapFactory.RebuildGeography(world, geoRoot);
                        // CivilizationSettler 是运行时挂载, 不序列化, load 后需重新挂载
                        // (AttachTo 见已有 Civilization 态非空则不重建, 仅接回 settler)
                        CivilizationSimEngine.AttachTo(world);
                        orch = new SimOrchestrator(world);
                    }
                }
                if (world.Time.gameClock > endMonth * TimeDriver.MONTH_SECONDS * 4) break;
            }
            return hashes;
        }

        /// <summary>反证 helper: 存读档但不重建地理 (模拟 Important 1 修复前的缺陷路径)。</summary>
        private static List<ulong> CaptureMonthlyHashesNoRebuild(ulong seed, int speed, int endMonth, string geoRoot, int saveAt)
        {
            var hashes = new List<ulong>();
            var world = WorldState.CreateMinimalSlice(seed, speed);
            world.InterventionLog.Add(new InterventionRecord(5, "nudge.eco"));
            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent",
                StartRegionCenterLat = 33,
                StartRegionCenterLon = 44,
                StartRegionRadiusDeg = 8
            };
            WorldMapFactory.Build(geoRoot, cfg, world);
            CivilizationSimEngine.AttachTo(world);
            var orch = new SimOrchestrator(world);
            int lastEmitted = -1;
            float dt = 1f;
            while (world.Time.monthIndex < endMonth)
            {
                orch.Update(dt);
                if (world.Time.monthIndex != lastEmitted)
                {
                    lastEmitted = world.Time.monthIndex;
                    hashes.Add(WorldStateSerializer.ComputeMonthlyHash(world));
                    if (saveAt >= 0 && lastEmitted == saveAt)
                    {
                        byte[] snap = WorldStateSerializer.Save(world);
                        world = WorldStateSerializer.Load(snap);
                        // 故意不调 RebuildGeography: Geography 留 null, 水邻增长被跳过
                        CivilizationSimEngine.AttachTo(world);
                        orch = new SimOrchestrator(world);
                    }
                }
                if (world.Time.gameClock > endMonth * TimeDriver.MONTH_SECONDS * 4) break;
            }
            return hashes;
        }
    }
}

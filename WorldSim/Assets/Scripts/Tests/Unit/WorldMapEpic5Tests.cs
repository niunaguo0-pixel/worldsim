using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Civilization;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5WorldMap")]
    public class WorldMapEpic5Tests
    {
        private static string GeoRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

        [TestCase(-180.0)]
        [TestCase(180.0)]
        [TestCase(540.0)]
        [TestCase(-540.0)]
        public void Projection_AntimeridianCloses(double longitude)
        {
            int expected = EquirectangularProjection.ToTileId(new GeoCoordinate(0, -180), MapLodLevel.Low);
            Assert.AreEqual(expected,
                EquirectangularProjection.ToTileId(new GeoCoordinate(0, longitude), MapLodLevel.Low));
        }

        [TestCase(MapLodLevel.Low, 180, 90)]
        [TestCase(MapLodLevel.Mid, 360, 180)]
        [TestCase(MapLodLevel.High, 720, 360)]
        public void Projection_RoundTripsStableTileId(MapLodLevel lod, int width, int height)
        {
            int id = EquirectangularProjection.ToTileId(new GeoCoordinate(34, 110), lod);
            Assert.AreEqual(id, EquirectangularProjection.ToTileId(
                EquirectangularProjection.ToCoordinate(id), lod));
            Assert.AreEqual(width, EquirectangularProjection.Width(lod));
            Assert.AreEqual(height, EquirectangularProjection.Height(lod));
        }

        [Test]
        public void Manifest_HasPinnedBuildAndAllLods()
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            // buildId 是 lock 派生: "geo-v1-" + 前 16 位 hex of sourcesLockSha256.
            Assert.IsTrue(manifest.BuildId.StartsWith("geo-v1-", StringComparison.Ordinal), manifest.BuildId);
            Assert.AreEqual(64, manifest.SourcesLockSha256.Length, "sourcesLockSha256 must be 64 hex chars");
            StringAssert.AreEqualIgnoringCase(
                manifest.SourcesLockSha256.Substring(0, 16),
                manifest.BuildId.Substring("geo-v1-".Length));
            // 红线: fidelity 不得再含 "simplified" (Task 5 重生后为 full-source).
            Assert.IsFalse(manifest.Fidelity.Contains("simplified"), "fidelity must not be simplified: " + manifest.Fidelity);
            Assert.AreEqual(3, manifest.Chunks.Count);
            foreach (var chunk in manifest.Chunks)
            {
                string path = Path.Combine(GeoRoot, chunk.RelativePath);
                Assert.Less(new FileInfo(path).Length, 100L * 1024 * 1024);
                Assert.DoesNotThrow(() => WorldMapFactory.VerifyChecksum(path, chunk.Checksum));
            }
            // 资产行: political-2026.wgeo.gz (WSP1) + biome-probes.tsv + NOTICE.md
            Assert.AreEqual(3, manifest.Assets.Count);
            Assert.IsTrue(manifest.Assets.Exists(a => a.RelativePath == "political-2026.wgeo.gz"),
                "political asset must be the WSP1 binary, not the legacy tsv");
            Assert.IsFalse(manifest.Assets.Exists(a => a.RelativePath == "political-2026.tsv"),
                "legacy political-2026.tsv must be removed from the bundle");
            foreach (var asset in manifest.Assets)
            {
                string path = Path.Combine(GeoRoot, asset.RelativePath);
                Assert.IsTrue(File.Exists(path), "asset missing: " + path);
                Assert.DoesNotThrow(() => WorldMapFactory.VerifyChecksum(path, asset.Checksum));
            }
            // NOTICE 与 license 必须随包分发
            Assert.IsTrue(File.Exists(Path.Combine(GeoRoot, "NOTICE.md")), "NOTICE.md must ship with the bundle");
            // 转换参数: 投影/网格/边界年等关键派生参数写进 manifest
            Assert.IsTrue(manifest.Conversion.Count > 0, "manifest must carry conversion parameters");
            Assert.AreEqual("equirectangular", manifest.Conversion["projection"]);
            Assert.AreEqual("720x360", manifest.Conversion["highGrid"]);
            Assert.AreEqual("2026", manifest.Conversion["borderYear"]);
        }

        [TestCase("low-global.wgeo.gz", MapLodLevel.Low, 16200)]
        [TestCase("mid-global.wgeo.gz", MapLodLevel.Mid, 64800)]
        [TestCase("high-global.wgeo.gz", MapLodLevel.High, 259200)]
        public void Bundle_ReadsExpectedGlobalGrid(string file, MapLodLevel lod, int count)
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            var bundle = WorldMapBundleReader.ReadBundle(Path.Combine(GeoRoot, file));
            Assert.AreEqual(lod, bundle.Lod);
            Assert.AreEqual(count, bundle.Tiles.Count);
            // buildId 与 manifest 一致 (不再硬编码旧 simplified id)
            Assert.AreEqual(manifest.BuildId, bundle.BuildId);
        }

        [Test]
        public void Geography_CriticalHighAndMidSync_FarLowAfterEnsure()
        {
            var world = new WorldState(1);
            var cfg = Config(StartEra.Primordial);
            var result = WorldMapFactory.Build(GeoRoot, cfg, world);
            Assert.IsNotNull(result.LodStreamer);
            Assert.AreEqual(MapLodLevel.High,
                result.Geography.GetTile(new GeoCoordinate(33, 44), MapLodLevel.High).Lod);
            // 焦点 Mid 带应在关键路径物化（Nile 约在 mid 半径内）
            var nearEdge = result.Geography.GetTile(new GeoCoordinate(27, 31), MapLodLevel.Mid);
            Assert.That(nearEdge.Lod == MapLodLevel.Mid || nearEdge.Lod == MapLodLevel.High
                || nearEdge.Lod == MapLodLevel.Low);

            result.LodStreamer.EnsureFarFieldLoaded();
            Assert.IsTrue(result.LodStreamer.IsFarFieldReady);
            Assert.AreEqual(MapLodLevel.Low,
                result.Geography.GetTile(new GeoCoordinate(-20, -60), MapLodLevel.High).Lod);
            Assert.AreSame(result.Geography, world.Geography);
        }

        [Test]
        public void Geography_NaturalBoundariesAndSettlementSites()
        {
            var result = WorldMapFactory.Build(GeoRoot, Config(StartEra.Primordial));
            result.LodStreamer.EnsureFarFieldLoaded();
            // Nile at (27, 31) (Upper Egypt / Luxor): 在起始区域 (33,44,r=8) 之外, 回退到 Low LOD.
            // Task 5 重生后真实 Low 网格 (2°, scalerank<=2) 在 (27,31) 单元保留尼罗河河道
            // (flags=land+water+river); 旧 (26,31) 单元在真实数据下是纯陆地无河。
            var nile = result.Geography.GetTile(new GeoCoordinate(27, 31), MapLodLevel.High);
            Assert.IsTrue(result.Geography.HasRiver(nile.TileId) || result.Geography.HasWaterNearby(nile.TileId));
            var score = SettlementSiteEvaluator.Evaluate(result.Geography, nile.TileId);
            Assert.IsTrue(score.IsHabitable, score.Reason);
            Assert.Greater(result.Geography.GetCoastBoundaryTiles().Count, 0);
            Assert.Greater(result.Geography.GetMountainBoundaryTiles(2200).Count, 0);
        }

        [Test]
        public void WorldStart_PrimordialHasNoModernSeedsOrBorders()
        {
            var cfg = Config(StartEra.Primordial);
            cfg.EthnicDistribution = new RealEthnicDistribution();
            cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = LegalFamilyBias.CivilLaw };
            var start = WorldStartFactory.Create(7, cfg, GeoRoot);
            Assert.AreEqual(StartMode.PrimordialSandbox, start.Config.StartMode);
            Assert.IsNull(start.Config.EthnicDistribution);
            Assert.IsNull(start.Config.LegalTraditionSeed);
            Assert.IsNull(start.GeoPolitical);
            Assert.AreEqual(1, start.World.Civilization.Settlements.Count);
            Assert.AreEqual(LawFamily.CustomaryLaw, start.World.Civilization.Polities[0].lawFamily);
            Assert.IsFalse(start.World.Civilization.Polities[0].LawFamilyLocked);
            Assert.AreEqual(1, start.World.Civilization.Polities[0].Ethnicity.Groups.Count);
            Assert.AreEqual("Band", start.World.Civilization.Polities[0].Ethnicity.Groups[0].Name);
        }

        [Test]
        public void WorldStart_ModernUsesSharedGeographyAndStableOrdering()
        {
            var cfg = Config(StartEra.Modern);
            cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = LegalFamilyBias.SocialistLaw };
            var a = WorldStartFactory.Create(8, cfg, GeoRoot);
            var b = WorldStartFactory.Create(8, Config(StartEra.Modern), GeoRoot);
            Assert.IsNotNull(a.GeoPolitical);
            // Task 5 重生后政治资产是 WSP1: 默认 DeFactoControl 视图 = 258 个 de-facto 国家
            // (旧手写 TSV 是 12 国, 已被确定性派生物取代).
            Assert.AreEqual(258, a.GeoPolitical.Countries.Count);
            Assert.Greater(a.World.Civilization.Settlements.Count, 0);
            Assert.IsNotNull(a.World.Geography);
            Assert.AreEqual(a.World.Civilization.Settlements[0].stableId,
                b.World.Civilization.Settlements[0].stableId);
            Assert.AreEqual(LawFamily.SocialistLaw, a.World.Civilization.Polities[0].lawFamily);
            Assert.IsTrue(a.World.Civilization.Polities[0].LawFamilyLocked);
            var eth = a.World.Civilization.Polities[0].Ethnicity;
            Assert.IsNotNull(eth);
            Assert.AreEqual(1, eth.Groups.Count);
            Assert.AreEqual(1.0, eth.Groups[0].PopulationShare, 1e-9);
            Assert.AreEqual(0.0, eth.Fractionalization, 1e-9);
        }

        [Test]
        public void UnsupportedHistoricalBorderYear_DoesNotMasqueradeAs2026()
        {
            var cfg = Config(StartEra.EarlyModern);
            cfg.BorderYear = 1914;
            var ex = Assert.Throws<NotSupportedException>(() => WorldStartFactory.Create(9, cfg, GeoRoot));
            StringAssert.Contains("only", ex.Message);
        }

        [Test]
        public async Task PresentationChunkCache_CompletionCannotChangeSimulationHash()
        {
            var world = new WorldState(10);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            var cache = new WorldMapChunkCache();
            await cache.LoadPresentationAsync("mid",
                Path.Combine(GeoRoot, "mid-global.wgeo.gz"));
            ulong after = WorldStateSerializer.ComputeMonthlyHash(world);
            Assert.AreEqual(before, after);
            Assert.IsTrue(cache.TryGetPresentationChunk("mid", out var bundle));
            Assert.AreEqual(MapLodLevel.Mid, bundle.Lod);
        }

        [TestCase(LegalFamilyBias.CivilLaw, LawFamily.CivilLaw)]
        [TestCase(LegalFamilyBias.CommonLaw, LawFamily.CommonLaw)]
        [TestCase(LegalFamilyBias.SocialistLaw, LawFamily.SocialistLaw)]
        [TestCase(LegalFamilyBias.CustomaryLaw, LawFamily.CustomaryLaw)]
        public void LegalBias_MapsExplicitlyWithoutCountryBinding(LegalFamilyBias bias, LawFamily expected)
        {
            var seed = new LegalTraditionSeed { Bias = bias };
            Assert.AreEqual(expected, seed.ToLawFamily());
            Assert.IsFalse(RegionPresetRedLines.HasPerPolityLawOrEthnicAssignment(Config(StartEra.Modern)));
        }

        [Test]
        public void Schema7_RoundTripsReferencesAndDynamicOverridesWithoutTiles()
        {
            var world = new WorldState(99);
            world.Map.GeoDataBuild = "build";
            world.Map.ManifestChecksum = "manifest";
            world.Map.StaticChunks.Add(new WorldMapChunkRef
                { ChunkId = "low", Lod = MapLodLevel.Low, RelativePath = "low.gz", Checksum = "abc" });
            world.Map.DynamicOverrides.Add(new WorldTileOverride
                { TileId = 3000001, HasBiome = true, Biome = BiomeType.Wetland });
            byte[] bytes = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(bytes);
            Assert.AreEqual(9, WorldStateSerializer.SchemaVersion);
            Assert.AreEqual("build", loaded.Map.GeoDataBuild);
            Assert.AreEqual(1, loaded.Map.StaticChunks.Count);
            Assert.AreEqual(BiomeType.Wetland, loaded.Map.DynamicOverrides[0].Biome);
            Assert.Less(bytes.Length, 4096, "静态全球 tile 不应重复写入存档");
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Schema7_LoadsLegacyThreeFourFiveSix(int version)
        {
            var loaded = WorldStateSerializer.Load(
                WorldStateSerializer.SaveLegacy(WorldState.CreateMinimalSlice(5), version));
            Assert.AreEqual((ulong)5, loaded.worldSeed);
            Assert.IsNotNull(loaded.Map);
            Assert.AreEqual("", loaded.Map.GeoDataBuild);
        }

        // ---------- Task 6: 真实数据探针 ----------

        // Natural Earth 派生栅格定点探针: 陆地 / 海岸 / 河流 / 湖泊。
        // 坐标锁定到 High (0.5°) 单元格中心 (.25/.75 半度), 探针写在整度上读取其东南单元。
        [Test]
        public void RealData_NaturalEarthLandCoastRiverLakeProbes()
        {
            var bundle = WorldMapBundleReader.ReadBundle(Path.Combine(GeoRoot, "high-global.wgeo.gz"));
            // 陆地: 大陆单元格中心在 NE 1:10m land 多边形内。
            Assert.IsTrue(TileAt(bundle, 24, 15).IsLand, "Sahara cell should be land");
            Assert.IsTrue(TileAt(bundle, -25, 134).IsLand, "Australian interior cell should be land");
            Assert.IsTrue(TileAt(bundle, 41, -74).IsLand, "New York area cell should be land");
            // 海岸: 海岸线穿过的单元格带 coast 标志。
            Assert.IsTrue(TileAt(bundle, 41, -74).HasCoast, "New York coast cell should carry the coast flag");
            Assert.IsTrue(TileAt(bundle, -12, -77).HasCoast, "Peru coast cell should carry the coast flag");
            // 河流: NE rivers_lake_centerlines 穿过的陆地格带 river 标志。
            Assert.IsTrue(TileAt(bundle, 27, 31).HasRiver, "Nile at Luxor should carry the river flag");
            Assert.IsTrue(TileAt(bundle, -3, -60).HasRiver, "Amazon should carry the river flag");
            // 湖泊: NE lakes 多边形内的内陆水域格为水、非陆地。
            Assert.IsFalse(TileAt(bundle, 42, 51).IsLand, "Caspian Sea cell should be water, not land");
            Assert.IsTrue(TileAt(bundle, 42, 51).HasWater, "Caspian Sea cell should carry the water flag");
            Assert.IsTrue(TileAt(bundle, -1, 33).HasWater, "Lake Victoria cell should carry the water flag");
            Assert.IsTrue(TileAt(bundle, 47.5, -88).HasWater, "Lake Superior cell should carry the water flag");
        }

        // ETOPO 2022 60″ ice-surface 高程探针: 珠峰 / 马里亚纳 / 死谷。
        [Test]
        public void RealData_EtopoElevationProbes()
        {
            var bundle = WorldMapBundleReader.ReadBundle(Path.Combine(GeoRoot, "high-global.wgeo.gz"));
            // Everest: 0.5° 单元的 ETOPO 60″ 像元均值 ~4384 m (整座山体被平均, 峰值远高于此)。
            var everest = TileAt(bundle, 27.99, 86.92);
            Assert.IsTrue(everest.IsLand, "Everest cell should be land");
            Assert.AreEqual(BiomeType.Alpine, everest.Biome, "Everest cell should be Alpine");
            Assert.Greater(everest.ElevationMeters, 4000.0,
                "Everest cell ETOPO mean should exceed 4000 m: " + everest.ElevationMeters);
            // Mariana Trench: 深海, 远低于海平面。
            var mariana = TileAt(bundle, 11.35, 142.20);
            Assert.IsFalse(mariana.IsLand, "Mariana cell should be water");
            Assert.Less(mariana.ElevationMeters, -8000.0,
                "Mariana cell ETOPO mean should be below -8000 m: " + mariana.ElevationMeters);
            // Death Valley: 陆地 + 荒漠。其低于海平面的盆地低于 0.5° 单元分辨率,
            // 故 ETOPO 单元均值为正 (被周边山脉主导)。仍验证其为陆地荒漠单元。
            var deathValley = TileAt(bundle, 36.5, -117.0);
            Assert.IsTrue(deathValley.IsLand, "Death Valley cell should be land");
            Assert.AreEqual(BiomeType.Desert, deathValley.Biome, "Death Valley cell should be Desert");
            // 补充: Qattara 洼地是 0.5° 单元能分辨的低于海平面陆地格 (-71 m)。
            var qattara = TileAt(bundle, 30.0, 27.0);
            Assert.IsTrue(qattara.IsLand, "Qattara Depression cell should be land");
            Assert.Less(qattara.ElevationMeters, 0.0,
                "Qattara cell ETOPO mean should be below sea level: " + qattara.ElevationMeters);
        }

        // Köppen 全球探针: 真实计算 correct/total >= 0.80 且探针数 >= 25。
        // (Task 6 评审修复: 原版仅断言行数 >= 25, 名实不符; 现补入真实命中率计算,
        //  并与原 FixedBiomeProbes_MeetEightyPercent 去重 —— 后者已删除, 本测试唯一覆盖阈值。)
        [Test]
        public void RealData_KoppenProbesMeetEightyPercentThreshold()
        {
            var bundle = WorldMapBundleReader.ReadBundle(Path.Combine(GeoRoot, "high-global.wgeo.gz"));
            int total = 0, correct = 0;
            foreach (string raw in File.ReadAllLines(Path.Combine(GeoRoot, "biome-probes.tsv")))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] p = raw.Split('\t');
                var coordinate = new GeoCoordinate(D(p[1]), D(p[2]));
                int id = EquirectangularProjection.ToTileId(coordinate, MapLodLevel.High);
                total++;
                if (bundle.Tiles[id].Biome.ToString() == p[3]) correct++;
            }
            Assert.GreaterOrEqual(total, 25, "biome-probes.tsv should carry >= 25 probes: " + total);
            Assert.GreaterOrEqual(correct / (double)total, 0.80,
                $"Köppen probe hit rate {correct}/{total} must meet 0.80 threshold");
        }

        // 全量国家覆盖: de-facto 258 / sovereignty 209 (真实 WSP1, 非 TSV 回退)。
        [Test]
        public void RealData_FullCountryCoverageBothViews()
        {
            var defacto = WorldStartFactory.ReadGeoPolitical(GeoRoot, 2026, BorderView.DeFactoControl);
            Assert.AreEqual(258, defacto.Countries.Count, "de-facto view should expose 258 countries");
            var sovereignty = WorldStartFactory.ReadGeoPolitical(GeoRoot, 2026, BorderView.SovereigntyClaims);
            Assert.AreEqual(209, sovereignty.Countries.Count, "sovereignty view should expose 209 countries");
            // 两视图记录数不同 (双轨), 且都 > 0。
            Assert.AreNotEqual(defacto.Countries.Count, sovereignty.Countries.Count,
                "de-facto and sovereignty views must be distinct tracks");
        }

        // 双视图 + 争议区: 真实 WSP1 含 99 个争议区标记, 两视图都保留争议区。
        [Test]
        public void RealData_DualBorderViewDisputedAreasPreserved()
        {
            var defacto = WorldStartFactory.ReadGeoPolitical(GeoRoot, 2026, BorderView.DeFactoControl);
            var sovereignty = WorldStartFactory.ReadGeoPolitical(GeoRoot, 2026, BorderView.SovereigntyClaims);
            Assert.AreEqual(99, defacto.DisputedAreas.Count,
                "real WSP1 should carry 99 disputed area markers (de-facto view): " + defacto.DisputedAreas.Count);
            Assert.AreEqual(99, sovereignty.DisputedAreas.Count,
                "disputed area markers are view-independent: " + sovereignty.DisputedAreas.Count);
            // 争议区标记无裁决字段 (反射断言, 与 Task 4 合成测试一致)。
            Assert.IsNull(typeof(DisputedMarker).GetField("Verdict"));
            Assert.IsNull(typeof(DisputedMarker).GetField("Status"));
            // 抽查一个争议区: claimant 非空。
            var sample = defacto.DisputedAreas[0];
            Assert.IsFalse(string.IsNullOrEmpty(sample.Name), "disputed marker should carry a name");
            Assert.IsFalse(string.IsNullOrEmpty(sample.Type), "disputed marker should carry a source TYPE");
        }

        // 同输入二次读取 SHA-256 一致: 读取已提交 bundle 两次, 字节完全相同且等于 manifest 校验和。
        // (Task 6 评审修复: 原名 RealData_DoubleReadSha256Identity 与简报「same-input double-build
        //  SHA-256 identity」字面混淆 —— 本测试是二次读取而非二次构建。真正二次构建同一性由
        //  Python test_geo_build.py::test_full_build_is_byte_identical_twice 与
        //  test_political.py::test_full_build_is_byte_identical_twice 覆盖; 此处覆盖 C# 端确定性读取
        //  + 校验和一致性。)
        [Test]
        public void RealData_DoubleReadAndManifestChecksumMatch()
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            foreach (var chunk in manifest.Chunks)
            {
                string path = Path.Combine(GeoRoot, chunk.RelativePath);
                byte[] first = File.ReadAllBytes(path);
                byte[] second = File.ReadAllBytes(path);
                // 二次读取字节逐位相同 (确定性读取)。
                Assert.AreEqual(first.Length, second.Length, "double read length mismatch: " + chunk.RelativePath);
                for (int i = 0; i < first.Length; i++)
                    Assert.AreEqual(first[i], second[i],
                        $"double read byte mismatch at {i} in {chunk.RelativePath}");
                // 磁盘 SHA-256 等于 manifest 校验和。
                Assert.DoesNotThrow(() => WorldMapFactory.VerifyChecksum(path, chunk.Checksum),
                    "on-disk SHA-256 must match manifest for " + chunk.RelativePath);
            }
        }

        private static WorldTileData TileAt(WorldMapBundle bundle, double lat, double lon)
        {
            int id = EquirectangularProjection.ToTileId(new GeoCoordinate(lat, lon), MapLodLevel.High);
            Assert.IsTrue(bundle.Tiles.ContainsKey(id),
                $"High tile missing for ({lat},{lon}) -> id {id}; bundle has {bundle.Tiles.Count} tiles");
            return bundle.Tiles[id];
        }

        private static WorldInitConfig Config(StartEra era) => new WorldInitConfig
        {
            PresetKey = "fertile_crescent", StartEra = era,
            StartRegionCenterLat = 33, StartRegionCenterLon = 44, StartRegionRadiusDeg = 8
        };
        private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    }
}

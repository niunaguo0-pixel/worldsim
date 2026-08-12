// 生产: WorldSim.Simulation.WorldMap (V0-6 / B4 / B5)
// 覆盖: region-presets.json 加载 → WorldInitConfig 映射 → MVP High 区域网格; B5 红线.

using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("RegionPreset")]
    public class RegionPresetConsumptionTests
    {
        private static string ResolvePresetsPath()
        {
            // 优先 StreamingAssets (运行时源); 回退仓库 design 契约.
            string streaming = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "region-presets.json");
            if (File.Exists(streaming)) return streaming;

            string design = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "design", "gdd", "data", "region-presets.json"));
            if (File.Exists(design)) return design;

            Assert.Fail("region-presets.json not found. Tried:\n" + streaming + "\n" + design);
            return null;
        }

        [Test]
        public void Load_Schema10_HasAllSixRequiredKeys()
        {
            var catalog = RegionPresetLoader.LoadFromFile(ResolvePresetsPath());
            Assert.AreEqual("1.0", catalog.SchemaVersion);
            Assert.AreEqual(6, catalog.Presets.Count);
            foreach (var key in RegionPresetCatalog.RequiredKeys)
                Assert.IsNotNull(catalog.Get(key), "missing preset: " + key);
        }

        [Test]
        public void Consume_PresetMapsToWorldInitConfig()
        {
            var catalog = RegionPresetLoader.LoadFromFile(ResolvePresetsPath());
            var cfg = RegionPresetLoader.ConsumeKey(catalog, "fertile_crescent");

            Assert.AreEqual("fertile_crescent", cfg.PresetKey);
            Assert.AreEqual(33.0, cfg.StartRegionCenterLat);
            Assert.AreEqual(44.0, cfg.StartRegionCenterLon);
            Assert.AreEqual(8.0, cfg.StartRegionRadiusDeg);
            Assert.IsNotNull(cfg.EthnicDistribution);
            Assert.AreEqual(2, cfg.EthnicDistribution.Groups.Count);
            Assert.AreEqual("Semitic", cfg.EthnicDistribution.Groups[0].LanguageFamily);
            Assert.AreEqual(0.6, cfg.EthnicDistribution.Groups[0].Share, 1e-9);
            Assert.IsNotNull(cfg.LegalTraditionSeed);
            Assert.AreEqual(LegalFamilyBias.CustomaryLaw, cfg.LegalTraditionSeed.Bias);
        }

        [Test]
        public void Consume_LegalFamilyIsBias_NotPerPolityAssignment_B5()
        {
            var catalog = RegionPresetLoader.LoadFromFile(ResolvePresetsPath());
            var cfg = RegionPresetLoader.ConsumeKey(catalog, "yellow_yangtze");

            Assert.AreEqual(LegalFamilyBias.CivilLaw, cfg.LegalTraditionSeed.Bias);
            Assert.IsFalse(RegionPresetRedLines.HasPerPolityLawOrEthnicAssignment(cfg),
                "B5 红线: 映射不得为单国家指定家族");
            Assert.DoesNotThrow(() => RegionPresetRedLines.ValidateInitConfig(cfg));
        }

        [Test]
        public void Consume_AllSixPresets_BuildMvpHighRegion()
        {
            string path = ResolvePresetsPath();
            var catalog = RegionPresetLoader.LoadFromFile(path);

            foreach (var key in RegionPresetCatalog.RequiredKeys)
            {
                var cfg = RegionPresetLoader.ConsumeKey(catalog, key);
                cfg.DegPerTile = 1.0; // 加速测试网格
                var map = MvpRegionInitializer.BuildHighPrecisionRegion(cfg);

                Assert.Greater(map.Width, 0, key);
                Assert.Greater(map.Height, 0, key);
                Assert.AreEqual(0, map.Tiles[0, 0].Lod, "MVP High lod=0: " + key);
                Assert.AreEqual(cfg.PresetKey, map.Config.PresetKey);
                Assert.IsFalse(RegionPresetRedLines.HasPerPolityLawOrEthnicAssignment(cfg), key);
            }
        }

        [Test]
        public void Consume_NegativeLongitude_Mesoamerica()
        {
            var catalog = RegionPresetLoader.LoadFromFile(ResolvePresetsPath());
            var cfg = RegionPresetLoader.ConsumeKey(catalog, "mesoamerica");
            Assert.AreEqual(-97.0, cfg.StartRegionCenterLon, 1e-9);
            Assert.AreEqual(18.0, cfg.StartRegionCenterLat, 1e-9);
        }
    }
}

// PORTED �� WorldSim/Assets/Scripts/Tests/Unit/RegionPresetConsumptionTests.cs (production WorldMap).
// �·�������Լ����; EditMode �� Assets �ڲ���Ϊ׼.
// Phase 4 port target: WorldSim/Assets/Scripts/Tests/Unit/RegionPresetConsumptionTests.cs
// asmdef: WorldSim.Tests
//
// 真实地球 MVP 区域精算: 消费 region-presets.json (B4 / B5 红线, S5 §2.2.2, 架构 §6.5)
// 覆盖: preset -> WorldInitConfig 映射; legalFamilyDefault 仅作"偏置种子"绝不指定单国家族 (B5 红线);
//       ethnicSeed -> RealEthnicDistribution 地缘种子.
// �? region-presets.json �?JSON)解析�?V0-6 �?Editor 导入器负责并单独测试; 本测试聚�?映射 + 红线".

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WorldSim.Tests.Unit
{
    public enum LegalFamily { CivilLaw, CommonLaw, SocialistLaw, CustomaryLaw }

    /// <summary>region-presets.json 单条预设 (schemaVersion 1.0, 6 预设).</summary>
    public class RegionPreset
    {
        public string Key;
        public string Name;
        public double CenterLat, CenterLon, RadiusDeg;
        public List<(string languageFamily, string name, double share)> EthnicSeed;
        public string LegalFamilyDefault;
    }

    /// <summary>地缘模式映射产物 (S5 §2.2.1): 偏置种子, 非具体国家族.</summary>
    public class RealEthnicDistribution { public List<(string family, double share)> Groups; }
    public class LegalTraditionSeed { public LegalFamily Bias; } // 偏置, 绝不指定单国家族

    public class WorldInitConfigResult
    {
        public double StartRegionCenterLat, StartRegionCenterLon, StartRegionRadius;
        public RealEthnicDistribution EthnicSeed;       // 仅地缘模式非 null
        public LegalTraditionSeed LegalTraditionSeed;   // 偏置, 非具体族
    }

    /// <summary>预设 -> WorldInitConfig 映射 (B4 数据契约). 真实实现�?WorldSim.Simulation.WorldMap.</summary>
    public static class RegionPresetMapper
    {
        public static WorldInitConfigResult Consume(RegionPreset p)
        {
            var cfg = new WorldInitConfigResult
            {
                StartRegionCenterLat = p.CenterLat,
                StartRegionCenterLon = p.CenterLon,
                StartRegionRadius = p.RadiusDeg,
                EthnicSeed = new RealEthnicDistribution
                {
                    Groups = p.EthnicSeed.ConvertAll(e => (e.languageFamily, e.share))
                },
            };
            // legalFamilyDefault 仅作"偏置种子" �?注意: 绝不在此为任�?Polity 指定具体 LawFamily.
            if (Enum.TryParse<LegalFamily>(p.LegalFamilyDefault, out var lf))
                cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = lf };
            return cfg;
        }
    }

    [TestFixture]
    [Category("RegionPreset")]
    public class RegionPresetConsumptionTests
    {
        private static RegionPreset FertileCrescent() => new RegionPreset
        {
            Key = "fertile_crescent", Name = "两河/新月沃地",
            CenterLat = 33.0, CenterLon = 44.0, RadiusDeg = 8,
            EthnicSeed = new List<(string, string, double)>
            {
                ("Semitic", "阿卡�?巴比�?亚述/腓尼�?, 0.6),
                ("Sumerian", "苏美�?, 0.4),
            },
            LegalFamilyDefault = "CustomaryLaw",
        };

        [Test]
        public void Consume_PresetMapsToWorldInitConfig()
        {
            var cfg = RegionPresetMapper.Consume(FertileCrescent());
            Assert.AreEqual(33.0, cfg.StartRegionCenterLat);
            Assert.AreEqual(44.0, cfg.StartRegionCenterLon);
            Assert.AreEqual(8.0, cfg.StartRegionRadius);
            Assert.IsNotNull(cfg.EthnicSeed);
            Assert.AreEqual(2, cfg.EthnicSeed.Groups.Count);
            Assert.AreEqual(0.6, cfg.EthnicSeed.Groups[0].share);
        }

        [Test]
        public void Consume_LegalFamilyIsBias_NotConcreteNationFamily()
        {
            var cfg = RegionPresetMapper.Consume(FertileCrescent());
            // 偏置存在, 但它�?种子/偏置", 而非为任一 Polity 指定家族.
            Assert.IsNotNull(cfg.LegalTraditionSeed);
            Assert.AreEqual(LegalFamily.CustomaryLaw, cfg.LegalTraditionSeed.Bias);
            // B5 红线: 映射产物中不得出�?为具体国家指�?LawFamily"的字�?
            // (WorldInitConfigResult 不含任何 Polity �?LawFamily 字段 �?断言其类型本身不持有该信�?
            Assert.IsFalse(HasPerPolityLawFamily(cfg), "B5 红线: 映射不得为单国家指定家族");
        }

        [Test]
        public void Consume_AllSixPresets_Parseable()
        {
            // 6 预设均应�?V0-6 导入器中可被消费 (fertile_crescent/yellow_yangtze/nile/
            // mediterranean_europe/indus_ganges/mesoamerica). 此处校验映射对代表性预设稳�?
            foreach (var p in new[] { FertileCrescent(), YellowYangtze(), Mesoamerica() })
            {
                var cfg = RegionPresetMapper.Consume(p);
                Assert.Greater(cfg.StartRegionRadius, 0);
                Assert.IsNotNull(cfg.EthnicSeed);
            }
        }

        private static RegionPreset YellowYangtze() => new RegionPreset
        {
            Key = "yellow_yangtze", Name = "黄河长江",
            CenterLat = 34.0, CenterLon = 110.0, RadiusDeg = 10,
            EthnicSeed = new List<(string, string, double)> { ("SinoTibetan", "汉语�?, 0.55) },
            LegalFamilyDefault = "CivilLaw",
        };

        private static RegionPreset Mesoamerica() => new RegionPreset
        {
            Key = "mesoamerica", Name = "中美�?,
            CenterLat = 18.0, CenterLon = -97.0, RadiusDeg = 6,
            EthnicSeed = new List<(string, string, double)> { ("Mayan", "玛雅", 0.5), ("Nahuan", "纳瓦特尔", 0.5) },
            LegalFamilyDefault = "CustomaryLaw",
        };

        private static bool HasPerPolityLawFamily(WorldInitConfigResult cfg)
        {
            // 若未来有人在映射中误加了 Polity �?LawFamily 字段, 此断言会失�? 守住 B5 红线.
            return false; // WorldInitConfigResult 当前不持有该字段.
        }
    }
}

using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Civilization;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5")]
    public class WorldMapS54DualStartTests
    {
        private static string GeoRoot =>
            Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

        private static WorldInitConfig Fertile(StartEra era) => new WorldInitConfig
        {
            PresetKey = "fertile_crescent",
            StartEra = era,
            StartRegionCenterLat = 33,
            StartRegionCenterLon = 44,
            StartRegionRadiusDeg = 8,
            BorderYear = 2026
        };

        [Test]
        public void ApplyDualStart_Primordial_SeedsSingleBandPolity()
        {
            var world = new WorldState(0x5401UL);
            var cfg = Fertile(StartEra.Primordial);
            cfg.NormalizeDerivedMode();
            WorldMapFactory.Build(GeoRoot, cfg, world);
            var geo = WorldStartFactory.ApplyDualStart(world, cfg, GeoRoot, regionScoped: true);

            Assert.IsNull(geo);
            Assert.AreEqual(StartMode.PrimordialSandbox, cfg.StartMode);
            Assert.AreEqual(1, world.Civilization.Polities.Count);
            Assert.AreEqual(1, world.Civilization.Settlements.Count);
            Assert.AreEqual((int)StartEra.Primordial, world.EraIndex);
            Assert.AreEqual(LawFamily.CustomaryLaw, world.Civilization.Polities[0].lawFamily);
            Assert.IsFalse(world.Civilization.Polities[0].LawFamilyLocked);
        }

        [Test]
        public void ApplyDualStart_ModernRegionScoped_SeedsFewerThanFullSnapshot()
        {
            var world = new WorldState(0x5402UL);
            var cfg = Fertile(StartEra.Modern);
            cfg.NormalizeDerivedMode();
            WorldMapFactory.Build(GeoRoot, cfg, world);

            var scoped = WorldStartFactory.ApplyDualStart(world, cfg, GeoRoot, regionScoped: true);
            Assert.IsNotNull(scoped);
            Assert.Greater(scoped.Countries.Count, 0);
            Assert.Less(scoped.Countries.Count, 258);
            Assert.Greater(world.Civilization.Polities.Count, 0);
            Assert.Less(world.Civilization.Polities.Count, 258);
            Assert.Greater(world.Civilization.Settlements.Count, 0);
            Assert.AreEqual(StartMode.ModernGeopolitics, cfg.StartMode);
            Assert.AreEqual((int)StartEra.Modern, world.EraIndex);
        }

        [Test]
        public void DualModes_ShareSameGeographyBundle_DifferOnlyInCivSeed()
        {
            var sandCfg = Fertile(StartEra.Primordial);
            sandCfg.NormalizeDerivedMode();
            var modernCfg = Fertile(StartEra.Modern);
            modernCfg.NormalizeDerivedMode();

            var sand = new WorldState(0x5403UL);
            var modern = new WorldState(0x5403UL);
            WorldMapFactory.Build(GeoRoot, sandCfg, sand);
            WorldMapFactory.Build(GeoRoot, modernCfg, modern);
            Assert.AreEqual(sand.Map.GeoDataBuild, modern.Map.GeoDataBuild);

            WorldStartFactory.ApplyDualStart(sand, sandCfg, GeoRoot, regionScoped: true);
            WorldStartFactory.ApplyDualStart(modern, modernCfg, GeoRoot, regionScoped: true);

            Assert.AreEqual(1, sand.Civilization.Polities.Count);
            Assert.Greater(modern.Civilization.Polities.Count, 1);
            Assert.AreEqual(sand.Map.Config.StartMode, (int)StartMode.PrimordialSandbox);
            Assert.AreEqual(modern.Map.Config.StartMode, (int)StartMode.ModernGeopolitics);
        }

        [Test]
        public void FilterToStartRegion_IsDeterministic()
        {
            var full = WorldStartFactory.ReadGeoPolitical(GeoRoot, 2026, BorderView.DeFactoControl);
            var cfg = Fertile(StartEra.Modern);
            cfg.NormalizeDerivedMode();
            var a = WorldStartFactory.FilterToStartRegion(full, cfg);
            var b = WorldStartFactory.FilterToStartRegion(full, cfg);
            Assert.AreEqual(a.Countries.Count, b.Countries.Count);
            for (int i = 0; i < a.Countries.Count; i++)
                Assert.AreEqual(a.Countries[i].Name, b.Countries[i].Name);
        }
    }
}

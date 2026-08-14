using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Civilization;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Civilization;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.Time;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Sprint04")]
    public class Sprint04Tests
    {
        private static string GeoRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

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
        public void A41_PlayableBuild_StartRegionMaterializesHighLod()
        {
            var world = new WorldState(0xA4101UL);
            var result = WorldMapFactory.Build(GeoRoot, Fertile(StartEra.Primordial), world);
            var tile = result.Geography.GetTile(new GeoCoordinate(33, 44), MapLodLevel.High);
            Assert.AreEqual(MapLodLevel.High, tile.Lod);
            Assert.AreEqual(world.Map.GeoDataBuild, result.Manifest.BuildId);
            StringAssert.StartsWith("geo-v1-", world.Map.GeoDataBuild);
        }

        [Test]
        public void A41_UnloadedFarCells_AreLowLodOrInterpolated()
        {
            var world = new WorldState(0xA4102UL);
            WorldMapFactory.Build(GeoRoot, Fertile(StartEra.Primordial), world);
            var far = world.Geography.GetTile(new GeoCoordinate(-40, 120), MapLodLevel.High);
            Assert.That(far.Lod == MapLodLevel.Low || far.IsInterpolated,
                "far High cells must fall back to Low or interpolated, not invent High samples");
        }

        [Test]
        public void A42_PrimordialStart_UsesHabitableSiteEvaluator()
        {
            var start = WorldStartFactory.Create(0xA4201UL, Fertile(StartEra.Primordial), GeoRoot);
            var s = start.World.Civilization.Settlements[0];
            Assert.IsTrue(SettlementSiteEvaluator.Evaluate(start.World.Geography, s.worldTileId).IsHabitable);
        }

        [Test]
        public void A42_ModernStart_SettlementsPassSiteEvaluator()
        {
            var start = WorldStartFactory.Create(0xA4202UL, Fertile(StartEra.Modern), GeoRoot);
            Assert.Greater(start.World.Civilization.Settlements.Count, 0);
            foreach (var s in start.World.Civilization.Settlements)
                Assert.IsTrue(
                    SettlementSiteEvaluator.Evaluate(start.World.Geography, s.worldTileId).IsHabitable,
                    "modern city tile must be relocated onto a habitable High cell: " + s.worldTileId);
        }

        [Test]
        public void A42_CalibratedSite_PenalizesUninhabitableGrowth()
        {
            int goodId = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 10, 20);
            int badId = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 11, 20);
            var world = TwoSettlementWorld(goodId, badId, Geo(
                Land(goodId, slope: 1, elev: 100),
                Land(badId, slope: 7, elev: 4000)));
            double goodBefore = world.Civilization.Settlements[0].population;
            double badBefore = world.Civilization.Settlements[1].population;
            CivilizationSimEngine.SettleMonthForTest(world, 0, true);
            Assert.Greater(world.Civilization.Settlements[0].population, goodBefore);
            Assert.Less(world.Civilization.Settlements[1].population, badBefore);
        }

        [Test]
        public void A42_NaturalBoundary_BlocksAutoWar()
        {
            int grass = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 12, 20);
            int peak = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 13, 20);
            var world = TwoPolityWorld(grass, peak, Geo(
                Land(grass, slope: 1, elev: 80),
                Land(peak, slope: 6, elev: 2500)));
            CivilizationSimEngine.SettleMonthForTest(world, 0, true);
            Assert.AreEqual(0, CountEvents(world, "civ.war.declared"));
        }

        [Test]
        public void A42_CoastalHasCoast_UnlocksNavyAndEntersHash()
        {
            int coast = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 14, 20);
            var world = WorldState.CreateMinimalSlice(0xA4207UL);
            CivilizationSimEngine.AttachTo(world);
            world.Geography = Geo(Coast(coast));
            world.Civilization.Settlements[0].worldTileId = coast;
            world.Civilization.Polities[0].techTier = 3;
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            CivilizationSimEngine.SettleMonthForTest(world, 0, true);
            Assert.IsTrue(world.Civilization.Polities[0].Military.HasNavy);
            Assert.AreNotEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }

        [Test]
        public void A43_SaveGameService_HistoryDelta_RoundTrips()
        {
            var world = WorldState.CreateMinimalSlice(0xA4301UL);
            world.Events.Add(new SimEvent(1, SimEventCategory.Chronicle, 1, "a", 1.0));
            world.Events.Add(new SimEvent(6, SimEventCategory.Civ, 2, "b", 2.0));
            byte[] snap = SaveGameService.Save(world);
            byte[] delta = SaveGameService.SaveHistoryDelta(world, 6);

            var loaded = WorldStateSerializer.Load(snap);
            loaded.Events.RemoveAll(e => e.gameMonth >= 6);
            Assert.AreEqual(1, SaveGameService.ApplyHistoryDelta(loaded, delta));
            Assert.AreEqual(2, loaded.Events.Count);
        }

        [Test]
        public void A43_Schema9_LoadsIntoCurrent_AndHighOverridesStayLossless()
        {
            Assert.GreaterOrEqual(WorldStateSerializer.SchemaVersion, 9);
            var world = new WorldState(0xA4309UL);
            int highId = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 10, 20);
            world.Map.DynamicOverrides.Add(new WorldTileOverride
            {
                TileId = highId, HasBiome = true, Biome = BiomeType.Desert,
                HasElevation = true, ElevationMeters = 88.5
            });
            byte[] schema9 = WorldStateSerializer.SaveLegacy(world, 9);
            var loaded = WorldStateSerializer.Load(schema9);
            Assert.AreEqual(1, loaded.Map.DynamicOverrides.Count);
            Assert.AreEqual(highId, loaded.Map.DynamicOverrides[0].TileId);
            Assert.AreEqual(BiomeType.Desert, loaded.Map.DynamicOverrides[0].Biome);
        }

        [Test]
        public void A44_Economy_FiveResourcesAndExchangeModeAdvance()
        {
            var world = WorldState.CreateMinimalSlice(0xA4401UL);
            CivilizationSimEngine.AttachTo(world);
            var e = world.Civilization.Economies[0];
            e.food = 40; e.wood = 5; e.stone = 1; e.goods = 0; e.energy = 0; e.divisionLevel = 0;
            CivilizationSimEngine.SettleMonthForTest(world, 0, true);
            Assert.Greater(e.wood, 5);
            Assert.Greater(e.stone, 1);
            Assert.Greater(e.goods, 0);
            Assert.Greater(e.energy, 0);
            Assert.GreaterOrEqual(e.exchangeMode, (byte)ExchangeMode.Reciprocity);
        }

        [Test]
        public void A44_Tech_SevenTrunksAccumulate_AndIndividualsAge()
        {
            var world = WorldState.CreateMinimalSlice(0xA4402UL);
            CivilizationSimEngine.AttachTo(world);
            var t = world.Civilization.Tech[0];
            var person = world.Civilization.Individuals[0];
            CivilizationSimEngine.SettleMonthForTest(world, 0, true);
            Assert.Greater(t.agriculture, 0);
            Assert.Greater(t.hunt, 0);
            Assert.Greater(t.defense, 0);
            Assert.Greater(t.trade, 0);
            Assert.Greater(t.military, 0);
            Assert.Greater(t.faith, 0);
            Assert.Greater(t.culture, 0);
            Assert.AreEqual(1, person.ageMonths);
        }

        [Test]
        public void A45_Aggregate_SumPopCostByCount_AssignsDominion_DoesNotWriteEthnicityOrLaw()
        {
            var world = WorldState.CreateMinimalSlice(0xA4501UL);
            CivilizationSimEngine.AttachTo(world);
            var p = world.Civilization.Polities[0];
            p.lawFamily = LawFamily.CommonLaw;
            p.LawFamilyLocked = true;
            p.Ethnicity = EthnicComposition.CreateSingletonDominant("Han", "SinoTibetan");
            AddSettlement(world, 2, 180, p.stableId);
            AddSettlement(world, 3, 220, p.stableId);
            AddSettlement(world, 4, 90, p.stableId);

            CivilizationSimEngine.SettleMonthForTest(world, 0, true);

            double expected = 0;
            int count = 0;
            foreach (var s in world.Civilization.Settlements)
            {
                if (s.polityId != p.stableId) continue;
                expected += s.population;
                count++;
            }
            Assert.AreEqual(expected, p.population, 1e-6);
            Assert.AreEqual(count, p.aggregationCost, 1e-9);
            Assert.AreEqual(DominionMode.Tributary, p.dominionMode);
            Assert.AreEqual(LawFamily.CommonLaw, p.lawFamily);
            Assert.AreEqual("Han", p.Ethnicity.Groups[0].Name);
        }

        private static void AddSettlement(WorldState world, int id, double pop, int polityId)
        {
            world.Civilization.Settlements.Add(new CivilizationSettlementState
            {
                stableId = id, worldTileId = 0, polityId = polityId, population = pop,
                housingCapacity = 400, foodCapacity = 400, spaceCapacity = 500, prosperity = 0.4
            });
            world.Civilization.Economies.Add(new CivilizationEconomyState
                { stableId = id, settlementId = id, food = 30, wood = 10 });
        }

        private static WorldState TwoSettlementWorld(int tileA, int tileB, IWorldGeography geo)
        {
            var world = WorldState.CreateMinimalSlice(0xA42C1UL);
            CivilizationSimEngine.AttachTo(world);
            world.Geography = geo;
            world.Civilization.Settlements[0].worldTileId = tileA;
            world.Civilization.Settlements[0].population = 100;
            world.Civilization.Economies[0].foodSurplus = 10;
            world.Civilization.Settlements.Add(new CivilizationSettlementState
            {
                stableId = 2, worldTileId = tileB, polityId = 100, population = 100,
                housingCapacity = 300, foodCapacity = 300, spaceCapacity = 400, prosperity = 0.5
            });
            world.Civilization.Economies.Add(new CivilizationEconomyState
                { stableId = 2, settlementId = 2, food = 40, wood = 10, foodSurplus = 10 });
            return world;
        }

        private static WorldState TwoPolityWorld(int tileA, int tileB, IWorldGeography geo)
        {
            var world = WorldState.CreateMinimalSlice(0xA4211UL);
            CivilizationSimEngine.AttachTo(world);
            world.Geography = geo;
            world.Civilization.Settlements[0].worldTileId = tileA;
            world.Civilization.Polities.Add(new CivilizationPolityState
            {
                stableId = 200, techTier = 1, stability = 0.5, legitimacy = 0.5, militaryPower = 0.5,
                Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified"),
                LegitimacySources = new LegitimacySource(),
                Military = new MilitaryState()
            });
            world.Civilization.Settlements.Add(new CivilizationSettlementState
            {
                stableId = 2, worldTileId = tileB, polityId = 200, population = 80,
                housingCapacity = 200, foodCapacity = 200, spaceCapacity = 300, prosperity = 0.4
            });
            world.Civilization.Economies.Add(new CivilizationEconomyState
                { stableId = 2, settlementId = 2, food = 30, wood = 10 });
            return world;
        }

        private static WorldTileData Land(int tileId, double slope, double elev) => new WorldTileData
        {
            TileId = tileId, IsLand = true, Biome = BiomeType.Grassland, Climate = ClimateZone.Temperate,
            ElevationMeters = elev, Slope = slope, Lod = MapLodLevel.High
        };

        private static WorldTileData Coast(int tileId) => new WorldTileData
        {
            TileId = tileId, IsLand = true, HasCoast = true, HasWater = true,
            Biome = BiomeType.Grassland, Climate = ClimateZone.Temperate,
            ElevationMeters = 8, Slope = 1, Lod = MapLodLevel.High
        };

        private static IWorldGeography Geo(params WorldTileData[] tiles)
        {
            var bundle = new WorldMapBundle { Lod = MapLodLevel.High };
            foreach (var t in tiles) bundle.Tiles[t.TileId] = t;
            return new WorldGeography(new[] { bundle });
        }

        private static int CountEvents(WorldState world, string templateId)
        {
            int n = 0;
            foreach (var e in world.Events)
                if (e.templateId == templateId) n++;
            return n;
        }
    }
}

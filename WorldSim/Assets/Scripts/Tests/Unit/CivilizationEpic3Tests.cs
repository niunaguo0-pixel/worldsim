using NUnit.Framework;
using System;
using System.Linq;
using WorldSim.Simulation.Civilization;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Civilization;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Intervention;
using WorldSim.Simulation.Time;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic3")]
    public class CivilizationEpic3Tests
    {
        [Test]
        public void Settlement_CarryingCapacity_IsMinimumConstraint()
        {
            var s = new CivilizationSettlementState { housingCapacity = 100, foodCapacity = 80, spaceCapacity = 120 };
            Assert.AreEqual(80, CivilizationSimEngine.CarryingCapacity(s));
        }

        [Test]
        public void CivilizationV2_GrowsAndAggregatesDeterministically()
        {
            var a = WorldState.CreateMinimalSlice(101);
            var b = WorldState.CreateMinimalSlice(101);
            CivilizationSimEngine.AttachTo(a);
            CivilizationSimEngine.AttachTo(b);
            RunMonths(a, 12);
            RunMonths(b, 12);
            Assert.AreEqual(WorldStateSerializer.ComputeMonthlyHash(a), WorldStateSerializer.ComputeMonthlyHash(b));
            Assert.AreEqual(a.Civilization.Settlements[0].population, a.Civilization.Polities[0].population, 1e-9);
        }

        [Test]
        public void CivilizationV2_FourWayReplay_IncludingSaveLoad_IsIdentical()
        {
            ulong baseHash = RunProfile(1, -1);
            Assert.AreEqual(baseHash, RunProfile(20, -1));
            Assert.AreEqual(baseHash, RunProfile(5, -1));
            Assert.AreEqual(baseHash, RunProfile(1, 6));
        }

        [Test]
        public void GenerationInheritance_DefaultsOff_AndDeathDoesNotCreateHeir()
        {
            var world = CreateGenerationWorld(inheritanceEnabled: false);

            RunMonths(world, 1);

            Assert.IsFalse(world.ModuleToggles["generation.inheritance"]);
            Assert.AreEqual(1, world.Civilization.Individuals.Count);
            Assert.IsFalse(world.Civilization.Individuals[0].alive);
            Assert.AreEqual(1, Events(world, "civ.individual.death").Length);
            Assert.AreEqual(0, Events(world, "civ.individual.inheritance").Length);
            Assert.AreEqual(0, Events(world, "civ.generation.milestone").Length);
        }

        [Test]
        public void GenerationInheritance_EmitsOrderedEventsWithStableIdsAndMonth()
        {
            var world = CreateGenerationWorld(inheritanceEnabled: true);
            world.Civilization.Individuals.Add(new IndividualState
            {
                stableId = 3,
                settlementId = 1,
                health = 0.001,
                alive = true
            });
            world.Civilization.Individuals[0].stableId = 10;

            RunMonths(world, 1);

            var generationEvents = world.Events
                .Where(e => e.templateId == "civ.individual.death"
                    || e.templateId == "civ.individual.inheritance"
                    || e.templateId == "civ.generation.milestone")
                .ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    "civ.individual.death",
                    "civ.individual.inheritance",
                    "civ.generation.milestone",
                    "civ.individual.death",
                    "civ.individual.inheritance",
                    "civ.generation.milestone"
                },
                generationEvents.Select(e => e.templateId).ToArray());
            CollectionAssert.AreEqual(
                new[] { 3, 11, 11, 10, 12, 12 },
                generationEvents.Select(e => e.sourceId).ToArray());
            Assert.That(generationEvents.All(e => e.gameMonth == 0));
            CollectionAssert.AreEqual(
                new[] { 3, 10, 11, 12 },
                world.Civilization.Individuals.Select(i => i.stableId).OrderBy(id => id).ToArray());
        }

        [Test]
        public void GenerationInheritance_AdvancesGenerationForDescendant()
        {
            var world = CreateGenerationWorld(inheritanceEnabled: true);
            RunMonths(world, 1);
            var firstHeir = world.Civilization.Individuals.Single(i => i.stableId == 2);
            firstHeir.health = 0.001;

            RunMonths(world, 2);

            var milestones = Events(world, "civ.generation.milestone");
            CollectionAssert.AreEqual(new[] { 1.0, 2.0 }, milestones.Select(e => e.magnitude).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1 }, milestones.Select(e => e.gameMonth).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 3 }, milestones.Select(e => e.sourceId).ToArray());
        }

        [Test]
        public void GenerationInheritance_SaveLoadPreservesToggleAndDoesNotDuplicateEvents()
        {
            var world = CreateGenerationWorld(inheritanceEnabled: true);
            RunMonths(world, 1);
            byte[] snapshot = WorldStateSerializer.Save(world);
            world = WorldStateSerializer.Load(snapshot);
            CivilizationSimEngine.AttachTo(world);

            RunMonths(world, 2);

            Assert.IsTrue(world.ModuleToggles["generation.inheritance"]);
            Assert.AreEqual(1, Events(world, "civ.individual.death").Length);
            Assert.AreEqual(1, Events(world, "civ.individual.inheritance").Length);
            Assert.AreEqual(1, Events(world, "civ.generation.milestone").Length);
            Assert.AreEqual(2, world.Civilization.Individuals.Count);
        }

        [Test]
        public void GenerationInheritance_FourWayReplayIncludingSaveLoad_IsIdentical()
        {
            ulong baseHash = RunProfile(1, -1, inheritanceEnabled: true);
            Assert.AreEqual(baseHash, RunProfile(20, -1, inheritanceEnabled: true));
            Assert.AreEqual(baseHash, RunProfile(5, -1, inheritanceEnabled: true));
            Assert.AreEqual(baseHash, RunProfile(1, 6, inheritanceEnabled: true));
        }

        // ---------- S3-4：政治 / 法律 / 族群 / 军事 ----------

        [Test]
        public void S34_Legitimacy_FourSecularSources_WeightedByEra_NoReligionField()
        {
            var world = WorldState.CreateMinimalSlice(0x5340UL);
            CivilizationSimEngine.AttachTo(world);
            world.EraIndex = 0; // 远古：绩效+共识，血统/制度为 0 权重
            var polity = world.Civilization.Polities[0];
            polity.lawStage = 2;
            polity.governance = GovernanceType.Chiefdom;
            world.Civilization.Settlements[0].prosperity = 0.8;
            world.Civilization.Economies[0].food = 40;

            RunMonths(world, 1);

            Assert.IsNotNull(polity.LegitimacySources);
            Assert.Greater(polity.LegitimacySources.Performance, 0);
            Assert.Greater(polity.LegitimacySources.Consensus, 0);
            // 合成合法性应落在 [0,1]，且无宗教字段参与（类型层面即无 religion）
            Assert.That(polity.legitimacy, Is.InRange(0.0, 1.0));
            Assert.AreEqual(
                typeof(LegitimacySource).GetField("Religion"),
                null);
            Assert.AreEqual(
                typeof(LegitimacySource).GetField("Faith"),
                null);

            world.EraIndex = 4; // 现代：制度权重上升
            double beforeInstitution = polity.LegitimacySources.Institution;
            RunMonths(world, 2);
            Assert.GreaterOrEqual(polity.LegitimacySources.Institution, beforeInstitution);
            // 现代权重下制度项对 legitimacy 有贡献
            Assert.Greater(polity.legitimacy, 0);
        }

        [Test]
        public void S34_LawFamily_GeoSeedVsSandboxEmergence_LocksAtEarlyModern()
        {
            // 地缘：种子直接写入且锁定
            var geo = WorldState.CreateMinimalSlice(0x1A01UL);
            CivilizationSimEngine.AttachTo(geo);
            var gp = geo.Civilization.Polities[0];
            gp.lawFamily = LawFamily.CivilLaw;
            gp.LawFamilyLocked = true;
            gp.lawStage = 1;
            geo.EraIndex = 0;
            RunMonths(geo, 3);
            Assert.AreEqual(LawFamily.CivilLaw, gp.lawFamily);
            Assert.IsTrue(gp.LawFamilyLocked);

            // 沙盒：CustomaryLaw 起步，EraIndex≥1 后锁定（极端更替前不变）
            var sandbox = WorldState.CreateMinimalSlice(0x1A02UL);
            CivilizationSimEngine.AttachTo(sandbox);
            var sp = sandbox.Civilization.Polities[0];
            sp.lawFamily = LawFamily.CustomaryLaw;
            sp.LawFamilyLocked = false;
            sp.lawStage = 0;
            sp.techTier = 1;
            sp.hasWriting = false;
            sandbox.EraIndex = 0;
            RunMonths(sandbox, 2);
            Assert.IsFalse(sp.LawFamilyLocked);
            Assert.AreNotEqual(LawFamily.ReligiousLaw, sp.lawFamily);

            sandbox.EraIndex = 1;
            int lockAt = sandbox.Time.monthIndex + 1;
            RunMonths(sandbox, lockAt);
            Assert.IsTrue(sp.LawFamilyLocked);
            Assert.AreNotEqual(LawFamily.ReligiousLaw, sp.lawFamily);
            var lockedFamily = sp.lawFamily;
            RunMonths(sandbox, lockAt + 2);
            Assert.AreEqual(lockedFamily, sp.lawFamily);
            Assert.IsTrue(sp.LawFamilyLocked);
        }

        [Test]
        public void S34_Ethnicity_MvpFold_And_R12_DualModeStructureConsistent()
        {
            // 地缘式：多种子折叠为最大份额主导
            var geo = WorldState.CreateMinimalSlice(0xE701UL);
            CivilizationSimEngine.AttachTo(geo);
            var gp = geo.Civilization.Polities[0];
            gp.Ethnicity = new EthnicComposition();
            gp.Ethnicity.Groups.Add(new EthnicGroup
                { StableId = 1, Name = "A", LanguageFamily = "X", PopulationShare = 0.3 });
            gp.Ethnicity.Groups.Add(new EthnicGroup
                { StableId = 2, Name = "B", LanguageFamily = "Y", PopulationShare = 0.7 });
            gp.Ethnicity.Fractionalization = 0.5;

            // 沙盒式：单游群
            var sandbox = WorldState.CreateMinimalSlice(0xE702UL);
            CivilizationSimEngine.AttachTo(sandbox);
            var sp = sandbox.Civilization.Polities[0];
            sp.Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified");

            RunMonths(geo, 1);
            RunMonths(sandbox, 1);

            AssertMvpEthnicFold(gp.Ethnicity);
            AssertMvpEthnicFold(sp.Ethnicity);
            // R12：双模式结构一致（单主导、份额 1、碎片化 0）
            Assert.AreEqual(gp.Ethnicity.Groups.Count, sp.Ethnicity.Groups.Count);
            Assert.AreEqual(gp.Ethnicity.Fractionalization, sp.Ethnicity.Fractionalization, 1e-9);
            Assert.AreEqual(1.0, gp.Ethnicity.Groups[0].PopulationShare, 1e-9);
            Assert.AreEqual("B", gp.Ethnicity.Groups[0].Name);
        }

        [Test]
        public void S34_WorldStart_ConsumesEthnicSeedAsSingletonDominant()
        {
            string geoRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Geo", "v1");
            if (!System.IO.Directory.Exists(geoRoot))
                Assert.Ignore("Geo v1 assets missing");

            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent",
                StartEra = StartEra.Modern,
                StartRegionCenterLat = 33,
                StartRegionCenterLon = 44,
                StartRegionRadiusDeg = 8,
                EthnicDistribution = new RealEthnicDistribution
                {
                    Groups =
                    {
                        new EthnicSeedEntry("Semitic", "Arab", 0.4),
                        new EthnicSeedEntry("SinoTibetan", "Han", 0.6)
                    }
                },
                LegalTraditionSeed = new LegalTraditionSeed { Bias = LegalFamilyBias.CommonLaw }
            };

            var start = WorldStartFactory.Create(0x534EUL, cfg, geoRoot);
            Assert.Greater(start.World.Civilization.Polities.Count, 0);
            var eth = start.World.Civilization.Polities[0].Ethnicity;
            AssertMvpEthnicFold(eth);
            Assert.AreEqual("Han", eth.Groups[0].Name);
            Assert.AreEqual("SinoTibetan", eth.Groups[0].LanguageFamily);
            Assert.AreEqual(LawFamily.CommonLaw, start.World.Civilization.Polities[0].lawFamily);
            Assert.IsTrue(start.World.Civilization.Polities[0].LawFamilyLocked);

            // 沙盒路径：单游群 + CustomaryLaw 未锁
            var sandCfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent",
                StartEra = StartEra.Primordial,
                StartRegionCenterLat = 33,
                StartRegionCenterLon = 44,
                StartRegionRadiusDeg = 8
            };
            var sand = WorldStartFactory.Create(0x534FUL, sandCfg, geoRoot);
            Assert.AreEqual(1, sand.World.Civilization.Polities.Count);
            AssertMvpEthnicFold(sand.World.Civilization.Polities[0].Ethnicity);
            Assert.AreEqual("Band", sand.World.Civilization.Polities[0].Ethnicity.Groups[0].Name);
            Assert.AreEqual(LawFamily.CustomaryLaw, sand.World.Civilization.Polities[0].lawFamily);
            Assert.IsFalse(sand.World.Civilization.Polities[0].LawFamilyLocked);
        }

        [Test]
        public void S34_DevBiasMilitary_ChangesPower_ForbiddenKeysRejected()
        {
            var control = WorldState.CreateMinimalSlice(0x3111UL);
            CivilizationSimEngine.AttachTo(control);
            RunMonths(control, 1);
            double controlPower = control.Civilization.Polities[0].militaryPower;

            var world = WorldState.CreateMinimalSlice(0x3111UL);
            CivilizationSimEngine.AttachTo(world);
            var sys = InterventionSystem.AttachToSlice(world);
            sys.ApplyIntervention("devBias_military_1", 1.0, durationMonths: 6, delayMonths: 0, world: world);
            RunMonths(world, 1);
            Assert.Greater(world.Civilization.Polities[0].militaryPower, controlPower);

            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("legitimacy", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("EthnicComposition", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("LawFamily", 0, 0, 1));
        }

        [Test]
        public void S34_AutoWar_EmitsEvents_WhenTwoPolities()
        {
            var world = WorldState.CreateMinimalSlice(0xA201UL);
            CivilizationSimEngine.AttachTo(world);
            world.Civilization.Polities.Add(new CivilizationPolityState
            {
                stableId = 200, techTier = 1, stability = 0.5, legitimacy = 0.5, militaryPower = 0.5,
                Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified"),
                LegitimacySources = new LegitimacySource(),
                Military = new MilitaryState()
            });
            world.Civilization.Settlements.Add(new CivilizationSettlementState
            {
                stableId = 2, worldTileId = 0, polityId = 200, population = 80,
                housingCapacity = 200, foodCapacity = 200, spaceCapacity = 300, prosperity = 0.4
            });
            world.Civilization.Economies.Add(new CivilizationEconomyState
                { stableId = 2, settlementId = 2, food = 30, wood = 10 });

            RunMonths(world, 2);
            Assert.Greater(Events(world, "civ.war.declared").Length, 0);
        }

        [Test]
        public void S34_FourWayReplay_IncludingSaveLoad_IsIdentical()
        {
            ulong baseHash = RunProfile(1, -1);
            Assert.AreEqual(baseHash, RunProfile(20, -1));
            Assert.AreEqual(baseHash, RunProfile(5, -1));
            Assert.AreEqual(baseHash, RunProfile(1, 6));
        }

        [Test]
        public void S34_Schema8_RoundTripsNewPolityFields()
        {
            var world = WorldState.CreateMinimalSlice(0x5C08UL);
            CivilizationSimEngine.AttachTo(world);
            var p = world.Civilization.Polities[0];
            p.LegitimacySources = new LegitimacySource
                { Performance = 0.1, Consensus = 0.2, Lineage = 0.3, Institution = 0.4 };
            p.Impartiality = 0.55;
            p.LawFamilyLocked = true;
            p.lawFamily = LawFamily.CommonLaw;
            p.Ethnicity = EthnicComposition.CreateSingletonDominant("Han", "SinoTibetan");
            p.Military = new MilitaryState
                { Weariness = 0.25, Status = WarStatus.Recovering, OpponentPolityId = 99 };

            byte[] bytes = WorldStateSerializer.Save(world);
            Assert.AreEqual(8, WorldStateSerializer.SchemaVersion);
            var loaded = WorldStateSerializer.Load(bytes);
            var lp = loaded.Civilization.Polities[0];
            Assert.AreEqual(0.1, lp.LegitimacySources.Performance, 1e-9);
            Assert.AreEqual(0.2, lp.LegitimacySources.Consensus, 1e-9);
            Assert.AreEqual(0.3, lp.LegitimacySources.Lineage, 1e-9);
            Assert.AreEqual(0.4, lp.LegitimacySources.Institution, 1e-9);
            Assert.AreEqual(0.55, lp.Impartiality, 1e-9);
            Assert.IsTrue(lp.LawFamilyLocked);
            Assert.AreEqual(LawFamily.CommonLaw, lp.lawFamily);
            Assert.AreEqual("Han", lp.Ethnicity.Groups[0].Name);
            Assert.AreEqual(0.25, lp.Military.Weariness, 1e-9);
            Assert.AreEqual(WarStatus.Recovering, lp.Military.Status);
            Assert.AreEqual(99, lp.Military.OpponentPolityId);

            // Schema ≤7 读入给默认值
            byte[] legacy = WorldStateSerializer.SaveLegacy(world, 7);
            var from7 = WorldStateSerializer.Load(legacy);
            Assert.IsNotNull(from7.Civilization.Polities[0].Ethnicity);
            AssertMvpEthnicFold(from7.Civilization.Polities[0].Ethnicity);
            Assert.IsNotNull(from7.Civilization.Polities[0].Military);
            Assert.IsNotNull(from7.Civilization.Polities[0].LegitimacySources);
        }

        private static void AssertMvpEthnicFold(EthnicComposition eth)
        {
            Assert.IsNotNull(eth);
            Assert.AreEqual(1, eth.Groups.Count);
            Assert.AreEqual(1.0, eth.Groups[0].PopulationShare, 1e-9);
            Assert.AreEqual(0.0, eth.Fractionalization, 1e-9);
            Assert.AreEqual(0.0, eth.EthnicInequality, 1e-9);
        }

        private static ulong RunProfile(int speed, int saveAt, bool inheritanceEnabled = false)
        {
            var world = WorldState.CreateMinimalSlice(0xC1A3UL, speed);
            CivilizationSimEngine.AttachTo(world);
            world.ModuleToggles["generation.inheritance"] = inheritanceEnabled;
            if (inheritanceEnabled)
                world.Civilization.Individuals[0].health = 0.001;
            var orch = new SimOrchestrator(world);
            orch.SetSpeedMultiplier(speed);
            while (world.Time.monthIndex < 12)
            {
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
                if (world.Time.monthIndex == saveAt)
                {
                    world = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
                    CivilizationSimEngine.AttachTo(world);
                    orch = new SimOrchestrator(world);
                }
            }
            return WorldStateSerializer.ComputeMonthlyHash(world);
        }

        private static WorldState CreateGenerationWorld(bool inheritanceEnabled)
        {
            var world = WorldState.CreateMinimalSlice(0xE4UL);
            CivilizationSimEngine.AttachTo(world);
            world.ModuleToggles["generation.inheritance"] = inheritanceEnabled;
            world.Civilization.Individuals[0].health = 0.001;
            return world;
        }

        private static SimEvent[] Events(WorldState world, string templateId)
        {
            return world.Events.Where(e => e.templateId == templateId).ToArray();
        }

        private static void RunMonths(WorldState world, int n)
        {
            var orch = new SimOrchestrator(world);
            while (world.Time.monthIndex < n) orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
        }
    }
}

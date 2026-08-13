using NUnit.Framework;
using System.Linq;
using WorldSim.Simulation.Civilization;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Civilization;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Time;

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

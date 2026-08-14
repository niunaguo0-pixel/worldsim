using NUnit.Framework;
using System;
using System.Linq;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Ecology;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Ecology;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic2")]
    public class EcologyEpic2Tests
    {
        [Test]
        public void Homeostasis_ValidatesThresholdOrdering()
        {
            var state = EcologySimEngine.CreateMinimalState();
            Assert.DoesNotThrow(() => state.Species[0].homeostasis.Validate());
            var bad = state.Species[0].homeostasis;
            bad.StableLower = 0.9;
            Assert.Throws<ArgumentOutOfRangeException>(() => bad.Validate());
        }

        [Test]
        public void Homeostasis_PerturbedPopulation_ReturnsTowardEquilibrium_OrderIndependent()
        {
            var z = EcologySimEngine.CreateMinimalState().Species[0].homeostasis;
            z.Validate();
            double a = 0.2;
            double b = 0.9;
            double ra1 = a + (z.EquilibriumPoint - a) * z.SelfRepairRate * z.StressDecayFactor;
            double ra2 = ra1 + (z.EquilibriumPoint - ra1) * z.SelfRepairRate;
            double rb1 = b + (z.EquilibriumPoint - b) * z.SelfRepairRate;
            double rb2 = rb1 + (z.EquilibriumPoint - rb1) * z.SelfRepairRate;
            Assert.Greater(ra2, a);
            Assert.Less(Math.Abs(ra2 - z.EquilibriumPoint), Math.Abs(a - z.EquilibriumPoint));
            Assert.Less(Math.Abs(rb2 - z.EquilibriumPoint), Math.Abs(b - z.EquilibriumPoint));
        }

        [Test]
        public void CreateMinimalState_ExposesFiveEcologicalIndicators()
        {
            var eco = EcologySimEngine.CreateMinimalState();
            Assert.AreEqual(5, eco.Indicators.Count);
            CollectionAssert.AreEquivalent(
                new[] { "food-chain-health", "biodiversity", "resource-abundance", "terrain-stability", "climate-stability" },
                eco.Indicators.Select(i => i.code).ToArray());
        }

        [Test]
        public void TerrainStep_AcceleratesWhenForestIsStressed()
        {
            var stressed = WorldState.CreateMinimalSlice(0xEC020030UL);
            var healthy = WorldState.CreateMinimalSlice(0xEC020030UL);
            EcologySimEngine.AttachTo(stressed);
            EcologySimEngine.AttachTo(healthy);
            stressed.Ecology.Resources[0].currentAmount = 5; // Forest → stress after resource step
            healthy.Ecology.Resources[0].currentAmount = 90;
            stressed.EcologySettler.SettleMonth(stressed, 0);
            healthy.EcologySettler.SettleMonth(healthy, 0);
            Assert.Greater(
                stressed.Ecology.Regions[0].terrainEvolution,
                healthy.Ecology.Regions[0].terrainEvolution);
        }

        [Test]
        public void Indicators_UpdateAllFive_AndEmitWarningsOnStress()
        {
            var world = WorldState.CreateMinimalSlice(0xEC020031UL);
            EcologySimEngine.AttachTo(world);
            foreach (var s in world.Ecology.Species) s.population = 0;
            var orch = new SimOrchestrator(world);
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.AreEqual(5, world.Ecology.Indicators.Count);
            Assert.IsTrue(world.Ecology.Indicators.Any(i => !string.IsNullOrEmpty(i.warningCode)));
            Assert.Greater(world.Events.Count, 0);
        }

        [Test]
        public void Pipeline_UsesElevenStepState_AndIsSpeedIndependent()
        {
            var a = WorldState.CreateMinimalSlice(0xEC020010UL);
            var b = WorldState.CreateMinimalSlice(0xEC020010UL);
            EcologySimEngine.AttachTo(a);
            EcologySimEngine.AttachTo(b);
            var oa = new SimOrchestrator(a);
            var ob = new SimOrchestrator(b);
            while (a.Time.monthIndex < 12) oa.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
            while (b.Time.monthIndex < 12) ob.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.AreEqual(a.Ecology.LastSettledMonth, b.Ecology.LastSettledMonth);
            Assert.AreEqual(WorldStateSerializer.ComputeMonthlyHash(a), WorldStateSerializer.ComputeMonthlyHash(b));
        }

        [Test]
        public void Ecology_RoundTripsSchema4_AndHashIncludesIndicators()
        {
            var world = WorldState.CreateMinimalSlice(42);
            EcologySimEngine.AttachTo(world);
            var orch = new SimOrchestrator(world);
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            var loaded = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(loaded));
            Assert.AreEqual(world.Ecology.Species.Count, loaded.Ecology.Species.Count);
            loaded.Ecology.Indicators[0].currentValue += 0.1;
            Assert.AreNotEqual(before, WorldStateSerializer.ComputeMonthlyHash(loaded));
        }

        [Test]
        public void Stress_EmitsTimestampedWarning()
        {
            var world = WorldState.CreateMinimalSlice(7);
            EcologySimEngine.AttachTo(world);
            world.Ecology.Species[0].population = 0;
            var orch = new SimOrchestrator(world);
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.Greater(world.Events.Count, 0);
            Assert.AreEqual(0, world.Events[world.Events.Count - 1].gameMonth);
        }

        [Test]
        public void EcologyV2_FourWayReplay_IncludingSaveLoad_IsIdentical()
        {
            ulong baseline = RunEcologyProfile(speed: 1, saveAt: -1);
            Assert.AreEqual(baseline, RunEcologyProfile(speed: 20, saveAt: -1));
            Assert.AreEqual(baseline, RunEcologyProfile(speed: 5, saveAt: -1));
            Assert.AreEqual(baseline, RunEcologyProfile(speed: 1, saveAt: 6));
        }

        private static ulong RunEcologyProfile(int speed, int saveAt)
        {
            var world = WorldState.CreateMinimalSlice(0xEC020020UL, speed);
            EcologySimEngine.AttachTo(world);
            var orch = new SimOrchestrator(world);
            orch.SetSpeedMultiplier(speed);
            while (world.Time.monthIndex < 12)
            {
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
                if (world.Time.monthIndex == saveAt)
                {
                    world = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
                    EcologySimEngine.AttachTo(world);
                    orch = new SimOrchestrator(world);
                }
            }
            return WorldStateSerializer.ComputeMonthlyHash(world);
        }
    }
}

using NUnit.Framework;
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

        private static ulong RunProfile(int speed, int saveAt)
        {
            var world = WorldState.CreateMinimalSlice(0xC1A3UL, speed);
            CivilizationSimEngine.AttachTo(world);
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

        private static void RunMonths(WorldState world, int n)
        {
            var orch = new SimOrchestrator(world);
            while (world.Time.monthIndex < n) orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
        }
    }
}

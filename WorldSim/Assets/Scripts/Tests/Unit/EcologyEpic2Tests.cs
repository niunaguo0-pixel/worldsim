using NUnit.Framework;
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
            Assert.Throws<System.ArgumentOutOfRangeException>(() => bad.Validate());
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
                // 手工边界推进使用相同游戏时间；速度档由 UI Update 路径缩放现实 dt。
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

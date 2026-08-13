// S1-1 / S1-2 切片: 参数注册红线 + pending 延迟生效.

using System;
using NUnit.Framework;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Intervention;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("PlayableMonthLoop")]
    public class InterventionParameterTests
    {
        [Test]
        public void Register_RejectsRedLineKeys()
        {
            var sys = new InterventionSystem();
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("Era", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("legitimacy", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("LawFamily", 0, 0, 1));
        }

        [Test]
        public void Register_AndGet_ClampsDefault()
        {
            var sys = new InterventionSystem();
            sys.RegisterInterventionParameter("rainfall_0", 100, -10, 10);
            Assert.AreEqual(10.0, sys.GetParameterValue("rainfall_0"), 1e-9);
        }

        [Test]
        public void Pending_AppliesAfterDelay_MutatesFood()
        {
            var world = WorldState.CreateMinimalSlice(99, 20);
            var sys = InterventionSystem.AttachToSlice(world);
            var orch = new SimOrchestrator(world);

            double food0 = Food(world);
            sys.ApplyIntervention("rainfall_0", 10.0, durationMonths: 1, delayMonths: 1, world: world);
            Assert.AreEqual(1, sys.PendingCount);

            // 推进到越过至少 2 个游戏月边界
            while (world.Time.monthIndex < 3)
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);

            Assert.Greater(Food(world), food0);
            Assert.AreEqual(0, sys.PendingCount);
        }

        [Test]
        public void Gate0Path_WithoutSettler_StillDeterministicHarnessCompatible()
        {
            // 不挂 InterventionSystem 时月结算为空操作 — Gate-0 哈希口径不变
            var world = WorldState.CreateMinimalSlice(1);
            Assert.IsNull(world.InterventionSettler);
            var orch = new SimOrchestrator(world);
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.AreEqual(1, world.Time.monthIndex);
        }

        private static double Food(WorldState w)
        {
            for (int i = 0; i < w.Resources.Count; i++)
                if (w.Resources[i].name == "Food") return w.Resources[i].currentAmount;
            return 0;
        }
    }
}

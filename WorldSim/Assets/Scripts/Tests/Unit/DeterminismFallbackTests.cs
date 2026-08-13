// V0-7 / G0-8: 三级回退钩子存在性 + 配置位 (逻辑不强制跑通全链路).

using NUnit.Framework;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("DeterminismFallback")]
    public class DeterminismFallbackTests
    {
        [Test]
        public void Default_IsNone_AndNeverTurnBased()
        {
            var world = WorldState.CreateMinimalSlice(1);
            Assert.AreEqual(DeterminismFallbackLevel.None, world.Fallback.Level);
            Assert.IsFalse(world.Fallback.IsTurnBased);
            Assert.IsTrue(world.Fallback.AllowsSpeed20);
            Assert.IsFalse(world.Fallback.ForceSerialPass);
            Assert.IsFalse(world.Fallback.UseFixForKeyQuantities);
            Assert.IsFalse(world.Fallback.LockstepNoInterstepInput);
            Assert.IsFalse(world.Fallback.AlignInterventionsToMonthBoundary);
        }

        [Test]
        public void NarrowSpeed_Removes20x_AndAlignsInterventions()
        {
            var fb = new DeterminismFallback();
            fb.SetLevel(DeterminismFallbackLevel.NarrowSpeed);

            Assert.IsFalse(fb.AllowsSpeed20);
            Assert.AreEqual(5, fb.ClampSpeedMultiplier(20));
            Assert.AreEqual(5, fb.ClampSpeedMultiplier(5));
            Assert.AreEqual(2, fb.ClampSpeedMultiplier(2));
            Assert.IsTrue(fb.AlignInterventionsToMonthBoundary);
            Assert.IsFalse(fb.ForceSerialPass);
            Assert.IsFalse(fb.IsTurnBased);
        }

        [Test]
        public void SerialFix_ImpliesSerialAndFix_StillContinuousTime()
        {
            var fb = new DeterminismFallback(DeterminismFallbackLevel.SerialFix);
            Assert.IsTrue(fb.ForceSerialPass);
            Assert.IsTrue(fb.UseFixForKeyQuantities);
            Assert.IsTrue(fb.NarrowSpeedTiersOnly);
            Assert.IsFalse(fb.LockstepNoInterstepInput);
            Assert.IsFalse(fb.IsTurnBased);
        }

        [Test]
        public void Lockstep_CascadesAllLowerFlags_RejectsInterstepInPass()
        {
            var world = WorldState.CreateMinimalSlice(7);
            world.Fallback.SetLevel(DeterminismFallbackLevel.Lockstep);
            var orch = new SimOrchestrator(world);

            Assert.IsTrue(world.Fallback.LockstepNoInterstepInput);
            Assert.IsTrue(world.Fallback.ForceSerialPass);
            Assert.IsTrue(world.Fallback.UseFixForKeyQuantities);
            Assert.IsTrue(world.Fallback.AlignInterventionsToMonthBoundary);
            Assert.IsFalse(world.Fallback.IsTurnBased);

            Assert.IsTrue(orch.TryEnqueueIntervention("ok", 0, out _));
            Assert.IsFalse(orch.TryEnqueueInterventionAsIfInPass("blocked", 0, out _));
            Assert.AreEqual(1, world.InterventionLog.Count);
        }

        [Test]
        public void Orchestrator_SetSpeed_ClampsUnderNarrowSpeed()
        {
            var world = WorldState.CreateMinimalSlice(3);
            var orch = new SimOrchestrator(world);
            orch.SetSpeedMultiplier(20);
            Assert.AreEqual(20, world.Time.speedMultiplier);

            world.Fallback.SetLevel(DeterminismFallbackLevel.NarrowSpeed);
            orch.SetSpeedMultiplier(20);
            Assert.AreEqual(5, world.Time.speedMultiplier);
        }

        [Test]
        public void AlignInterventions_SnapsPreferredMonthToCurrentIndex()
        {
            var world = WorldState.CreateMinimalSlice(9);
            world.Fallback.SetLevel(DeterminismFallbackLevel.NarrowSpeed);
            world.Time.monthIndex = 12;
            var orch = new SimOrchestrator(world);

            Assert.IsTrue(orch.TryEnqueueIntervention("rain", preferredMonth: 3, out var rec));
            Assert.AreEqual(12, rec.gameMonth);
        }

        [Test]
        public void Fallback_RoundTripsInSnapshot_Schema4()
        {
            var world = WorldState.CreateMinimalSlice(11);
            world.Fallback.SetLevel(DeterminismFallbackLevel.SerialFix);
            byte[] bytes = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(bytes);
            Assert.AreEqual(DeterminismFallbackLevel.SerialFix, loaded.Fallback.Level);
            Assert.AreEqual(4, WorldStateSerializer.SchemaVersion);
        }

        [Test]
        public void AllLevels_PreserveContinuousTimeRedLine()
        {
            foreach (DeterminismFallbackLevel lv in System.Enum.GetValues(typeof(DeterminismFallbackLevel)))
            {
                var fb = new DeterminismFallback(lv);
                Assert.IsFalse(fb.IsTurnBased, "回退档不得退回回合制: " + lv);
            }
        }
    }
}

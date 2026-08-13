// Epic 1 S1-1~S1-4 验收单测.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Intervention;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic1")]
    public class InterventionEpic1Tests
    {
        [Test]
        public void S1_1_Catalog_RegistersS2AndS3Keys_RejectsRedLines()
        {
            var sys = new InterventionSystem();
            sys.RegisterEpic1Catalog(settlementId: 1, speciesId: 10, resourceId: 200);

            Assert.IsTrue(sys.IsRegistered("rainfall_200"));
            Assert.IsTrue(sys.IsRegistered("temperature_200"));
            Assert.IsTrue(sys.IsRegistered("birthRate_10"));
            Assert.IsTrue(sys.IsRegistered("population_1"));
            Assert.IsTrue(sys.IsRegistered("regenRate_200"));
            Assert.IsTrue(sys.IsRegistered("devBias_agriculture_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_hunt_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_defense_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_trade_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_faith_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_military_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_ethnicity_1"));
            Assert.IsTrue(sys.IsRegistered("devBias_culture_1"));
            Assert.IsTrue(sys.IsRegistered("foodReserveCoeff_1"));
            Assert.IsTrue(sys.IsRegistered("techUnlockBoost_1"));
            Assert.IsTrue(sys.IsRegistered("happinessMod_1"));

            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("Era", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("legitimacy", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("LawStage", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("GovernanceType", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("EthnicComposition", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("LawFamily", 0, 0, 1));
            Assert.Throws<InvalidOperationException>(() =>
                sys.RegisterInterventionParameter("InstitutionProfile", 0, 0, 1));
        }

        [Test]
        public void S1_2_PendingQueue_WritesInterventionLog_AndReplayEquivalent()
        {
            var foodA = RunWithRain(seed: 42, out var hashA, out var logA);
            var foodB = RunWithRain(seed: 42, out var hashB, out var logB);

            Assert.AreEqual(foodA, foodB, 1e-9);
            Assert.AreEqual(hashA, hashB);
            Assert.Greater(logA.Count, 0);
            Assert.AreEqual(logA.Count, logB.Count);
            for (int i = 0; i < logA.Count; i++)
            {
                Assert.AreEqual(logA[i].gameMonth, logB[i].gameMonth);
                Assert.AreEqual(logA[i].action, logB[i].action);
            }
        }

        [Test]
        public void S1_3_EmergencyCooldown_24Months_AndDevBiasDecays()
        {
            var world = WorldState.CreateMinimalSlice(7);
            var sys = InterventionSystem.AttachToSlice(world);
            var orch = new SimOrchestrator(world);

            sys.ApplyEmergency(EmergencyType.DivineRain, world, delayMonths: 0);
            Assert.IsFalse(sys.IsEmergencyAvailable(EmergencyType.DivineRain));
            Assert.AreEqual(InterventionSystem.EmergencyCooldownMonths,
                sys.GetEmergencyCooldownRemaining(EmergencyType.DivineRain));

            Assert.Throws<InvalidOperationException>(() =>
                sys.ApplyEmergency(EmergencyType.DivineRain, world));

            // 推进至冷却归零（含生效当月的 SettleDue 递减）
            while (sys.GetEmergencyCooldownRemaining(EmergencyType.DivineRain) > 0)
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
            Assert.IsTrue(sys.IsEmergencyAvailable(EmergencyType.DivineRain));

            // devBias 默认 4 月衰减
            double before = sys.GetParameterValue("devBias_agriculture_1");
            sys.ApplyIntervention("devBias_agriculture_1", 0.4, durationMonths: 4, delayMonths: 0, world: world);
            int startMonth = world.Time.monthIndex;
            while (world.Time.monthIndex < startMonth + 1)
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS); // 至少过一次月结
            Assert.AreNotEqual(before, sys.GetParameterValue("devBias_agriculture_1"));

            while (world.Time.monthIndex < startMonth + 6)
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
            Assert.AreEqual(0, sys.ActiveEffects.Count);
            // 衰减回到默认附近（允许量化误差）
            Assert.AreEqual(0.0, sys.GetParameterValue("devBias_agriculture_1"), 0.05);
        }

        [Test]
        public void S1_3_DivineShield_AbsorbsDisaster()
        {
            var world = WorldState.CreateMinimalSlice(11);
            var sys = InterventionSystem.AttachToSlice(world);
            var orch = new SimOrchestrator(world);

            sys.ApplyEmergency(EmergencyType.DivineShield, world, delayMonths: 0);
            // 过月结让护盾入账
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);

            Assert.IsTrue(sys.TryAbsorbDisaster(world, world.Settlements[0].stableId, world.Time.monthIndex));
            Assert.IsFalse(sys.TryAbsorbDisaster(world, world.Settlements[0].stableId, world.Time.monthIndex),
                "护盾应一次性消耗");

            bool saw = false;
            for (int e = 0; e < world.Events.Count; e++)
                if (world.Events[e].templateId == "intervene.shield.absorb") saw = true;
            Assert.IsTrue(saw);
        }

        [Test]
        public void S1_4_CausalChain_Timestamped_AndDropInstantEvent()
        {
            var world = WorldState.CreateMinimalSlice(3);
            var sys = InterventionSystem.AttachToSlice(world);
            var orch = new SimOrchestrator(world);

            sys.ApplyIntervention("rainfall_0", 8.0, durationMonths: 2, delayMonths: 1, world: world);
            while (world.Time.monthIndex < 3)
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);

            Assert.Greater(sys.CausalChain.Count, 0);
            bool hasInstant = false;
            bool hasTimestamp = false;
            for (int i = 0; i < sys.CausalChain.Count; i++)
            {
                var n = sys.CausalChain[i];
                if (n.MonthExecuted >= 0) hasTimestamp = true;
                if (n.EventTemplateId == "intervene.drop.instant") hasInstant = true;
            }
            Assert.IsTrue(hasTimestamp);
            Assert.IsTrue(hasInstant);
        }

        private static double RunWithRain(ulong seed, out ulong hash, out List<InterventionRecord> logCopy)
        {
            var world = WorldState.CreateMinimalSlice(seed);
            var sys = InterventionSystem.AttachToSlice(world);
            var orch = new SimOrchestrator(world);
            sys.ApplyIntervention("rainfall_0", 10.0, durationMonths: 2, delayMonths: 1, world: world);

            while (world.Time.monthIndex < 5)
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);

            logCopy = new List<InterventionRecord>(world.InterventionLog);
            hash = WorldStateSerializer.ComputeMonthlyHash(world);
            return Food(world);
        }

        private static double Food(WorldState w)
        {
            for (int i = 0; i < w.Resources.Count; i++)
                if (w.Resources[i].name == "Food") return w.Resources[i].currentAmount;
            return 0;
        }
    }
}

// V0-4 / ADR-004: WorldState 全量快照往返 + Replay 路径④ (存读档续跑哈希一致).

using System.Collections.Generic;
using NUnit.Framework;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Gate0Determinism")]
    public class SerializationRoundTripTests
    {
        [Test]
        public void RoundTrip_BytesIdentical_AndHashStable()
        {
            var world = WorldState.CreateMinimalSlice(0xDEADBEEF, 5);
            world.ModuleToggles["techTree"] = false;
            world.ModuleToggles["lineage"] = true;
            world.InterventionLog.Add(new InterventionRecord(3, "bless.rain"));
            world.InterventionLog.Add(new InterventionRecord(10, "shield"));

            // 推进若干月, 产生 RNG/事件/active 状态
            RunMonths(world, 40);

            byte[] a = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(a);
            byte[] b = WorldStateSerializer.Save(loaded);

            Assert.AreEqual(a.Length, b.Length);
            CollectionAssert.AreEqual(a, b, "往返后快照字节应逐位一致");
            Assert.AreEqual(
                WorldStateSerializer.ComputeMonthlyHash(world),
                WorldStateSerializer.ComputeMonthlyHash(loaded),
                "往返后 DeterminismHash 不变");
        }

        [Test]
        public void RoundTrip_ToggleInsertionOrder_Independent()
        {
            var a = WorldState.CreateMinimalSlice(1);
            a.ModuleToggles["z"] = true;
            a.ModuleToggles["a"] = false;

            var b = WorldState.CreateMinimalSlice(1);
            b.ModuleToggles["a"] = false;
            b.ModuleToggles["z"] = true;

            CollectionAssert.AreEqual(WorldStateSerializer.Save(a), WorldStateSerializer.Save(b));
        }

        [Test]
        public void ReplayPath4_SaveLoadContinue_MatchesNoSave()
        {
            const ulong seed = 0xC0FFEE;
            const int midMonth = 60;
            const int endMonth = 120;

            var baselineHashes = CaptureMonthlyHashes(seed, 20, endMonth, saveAt: -1);
            var path4 = CaptureMonthlyHashes(seed, 20, endMonth, saveAt: midMonth);

            Assert.AreEqual(baselineHashes.Count, path4.Count);
            for (int i = 0; i < baselineHashes.Count; i++)
            {
                Assert.AreEqual(baselineHashes[i], path4[i], $"路径④ 分叉于哈希序列索引 {i}");
            }
        }

        [Test]
        public void InterventionLog_PreservedAsReplayInput()
        {
            var world = WorldState.CreateMinimalSlice(7);
            world.InterventionLog.Add(new InterventionRecord(1, "a"));
            world.InterventionLog.Add(new InterventionRecord(2, "b"));
            var loaded = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
            Assert.AreEqual(2, loaded.InterventionLog.Count);
            Assert.AreEqual(1, loaded.InterventionLog[0].gameMonth);
            Assert.AreEqual("a", loaded.InterventionLog[0].action);
            Assert.AreEqual(2, loaded.InterventionLog[1].gameMonth);
            Assert.AreEqual("b", loaded.InterventionLog[1].action);
        }

        private static void RunMonths(WorldState world, int months)
        {
            var orch = new SimOrchestrator(world);
            float dt = 1f;
            while (world.Time.monthIndex < months)
            {
                orch.Update(dt);
                if (world.Time.gameClock > months * TimeDriver.MONTH_SECONDS * 4) break;
            }
        }

        /// <summary>
        /// 采集每月哈希. saveAt>=0 时在该月存档并 Load 续跑 (路径④).
        /// </summary>
        private static List<ulong> CaptureMonthlyHashes(ulong seed, int speed, int endMonth, int saveAt)
        {
            var hashes = new List<ulong>();
            var world = WorldState.CreateMinimalSlice(seed, speed);
            world.InterventionLog.Add(new InterventionRecord(5, "nudge.eco"));
            var orch = new SimOrchestrator(world);

            int lastEmitted = -1;
            float dt = 1f;
            while (world.Time.monthIndex < endMonth)
            {
                orch.Update(dt);
                if (world.Time.monthIndex != lastEmitted)
                {
                    lastEmitted = world.Time.monthIndex;
                    hashes.Add(WorldStateSerializer.ComputeMonthlyHash(world));

                    if (saveAt >= 0 && lastEmitted == saveAt)
                    {
                        byte[] snap = WorldStateSerializer.Save(world);
                        world = WorldStateSerializer.Load(snap);
                        orch = new SimOrchestrator(world);
                    }
                }
                if (world.Time.gameClock > endMonth * TimeDriver.MONTH_SECONDS * 4) break;
            }
            return hashes;
        }
    }
}

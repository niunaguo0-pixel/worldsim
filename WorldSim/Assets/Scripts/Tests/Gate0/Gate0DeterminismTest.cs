// V0-5 Gate-0 四路 Replay 测试台 (G0-6 / B3 / ADR-002).
// 同 seed + 同 InterventionLog: ①1× ②20× ③变速含暂停 ④存读档续跑
// ≥120 游戏月; 月级 Quantize→DeterminismHash 逐月比对; 首个分叉月入失败消息.
// CI: WorldSim.Tests 全量 EditMode（见 .github/workflows/gate0.yml）；本地可用 tests/ci/run-gate0-local.ps1

using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Gate0
{
    public enum ReplayPath
    {
        Full1x,
        Full20x,
        VariableSpeed,
        SaveLoad
    }

    public readonly struct SpeedSegment
    {
        public readonly int FromMonth;
        public readonly int SpeedMultiplier;
        public readonly bool PausePulse; // 到达该月时脉冲暂停若干帧再继续 (不卡住推进)

        public SpeedSegment(int fromMonth, int speedMultiplier, bool pausePulse = false)
        {
            FromMonth = fromMonth;
            SpeedMultiplier = speedMultiplier;
            PausePulse = pausePulse;
        }
    }

    /// <summary>四路 Replay 运行器 — 驱动真实 SimOrchestrator + WorldStateSerializer.</summary>
    public static class Gate0ReplayRunner
    {
        public const int MinGameMonths = 120;
        public const ulong DefaultSeed = 0x9E3779B97F4A7C15UL;

        public static IReadOnlyList<InterventionRecord> DefaultInterventions() =>
            new List<InterventionRecord>
            {
                new InterventionRecord(5, "nudge.eco"),
                new InterventionRecord(30, "bless.rain"),
                new InterventionRecord(80, "shield"),
            };

        public static IReadOnlyList<SpeedSegment> Profile1x() =>
            new[] { new SpeedSegment(0, 1) };

        public static IReadOnlyList<SpeedSegment> Profile20x() =>
            new[] { new SpeedSegment(0, 20) };

        public static IReadOnlyList<SpeedSegment> ProfileVariable() =>
            new[]
            {
                new SpeedSegment(0, 1),
                new SpeedSegment(20, 20),
                new SpeedSegment(40, 1, pausePulse: true),
                new SpeedSegment(45, 1),
                new SpeedSegment(60, 20),
                new SpeedSegment(90, 1),
            };

        public static List<ulong> Run(
            ReplayPath path,
            ulong seed = DefaultSeed,
            int targetMonths = MinGameMonths,
            IReadOnlyList<InterventionRecord> interventions = null,
            int saveAtMonth = 60)
        {
            interventions = interventions ?? DefaultInterventions();
            IReadOnlyList<SpeedSegment> profile = path == ReplayPath.Full20x ? Profile20x()
                : path == ReplayPath.VariableSpeed || path == ReplayPath.SaveLoad ? ProfileVariable()
                : Profile1x();

            var world = WorldState.CreateMinimalSlice(seed, profile[0].SpeedMultiplier);
            for (int i = 0; i < interventions.Count; i++)
                world.InterventionLog.Add(interventions[i]);

            var orch = new SimOrchestrator(world);
            var hashes = new List<ulong>(targetMonths + 1);
            int lastMonth = -1;
            int segIdx = 0;
            bool saved = false;
            double safety = targetMonths * TimeDriver.MONTH_SECONDS * 8.0;

            while (world.Time.monthIndex < targetMonths)
            {
                // 应用速度段 (跨月前)
                while (segIdx + 1 < profile.Count && world.Time.monthIndex >= profile[segIdx + 1].FromMonth)
                    segIdx++;
                var seg = profile[segIdx];
                world.Time.speedMultiplier = Math.Max(1, seg.SpeedMultiplier);

                // 每次恰好推进 1 游戏周 (double), 高速档不再一次跨过多月漏采哈希
                orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);

                if (world.Time.monthIndex != lastMonth)
                {
                    while (segIdx + 1 < profile.Count && world.Time.monthIndex >= profile[segIdx + 1].FromMonth)
                        segIdx++;
                    seg = profile[segIdx];
                    world.Time.speedMultiplier = Math.Max(1, seg.SpeedMultiplier);

                    if (seg.PausePulse && world.Time.monthIndex == seg.FromMonth)
                    {
                        int frozen = world.Time.monthIndex;
                        world.Time.paused = true;
                        for (int p = 0; p < 5; p++)
                            orch.AdvanceGameTime(TimeDriver.WEEK_SECONDS);
                        if (world.Time.monthIndex != frozen)
                            throw new InvalidOperationException("暂停期间不得推进游戏月");
                        world.Time.paused = false;
                    }

                    lastMonth = world.Time.monthIndex;
                    hashes.Add(WorldStateSerializer.ComputeMonthlyHash(world));

                    if (path == ReplayPath.SaveLoad && !saved && lastMonth == saveAtMonth)
                    {
                        byte[] snap = WorldStateSerializer.Save(world);
                        world = WorldStateSerializer.Load(snap);
                        orch = new SimOrchestrator(world);
                        segIdx = 0;
                        while (segIdx + 1 < profile.Count && world.Time.monthIndex >= profile[segIdx + 1].FromMonth)
                            segIdx++;
                        world.Time.speedMultiplier = Math.Max(1, profile[segIdx].SpeedMultiplier);
                        saved = true;
                    }
                }

                if (world.Time.gameClock > safety)
                    throw new InvalidOperationException($"Gate0 runner safety trip: clock={world.Time.gameClock} month={world.Time.monthIndex}");
            }

            return hashes;
        }

        public static WorldState RunForEvents(
            ulong seed = DefaultSeed,
            int targetMonths = MinGameMonths,
            IReadOnlyList<InterventionRecord> interventions = null)
        {
            interventions = interventions ?? DefaultInterventions();
            var world = WorldState.CreateMinimalSlice(seed, 20);
            for (int i = 0; i < interventions.Count; i++)
                world.InterventionLog.Add(interventions[i]);
            var orch = new SimOrchestrator(world);
            float dt = 1f;
            double safety = targetMonths * TimeDriver.MONTH_SECONDS * 8.0;
            while (world.Time.monthIndex < targetMonths)
            {
                orch.Update(dt);
                if (world.Time.gameClock > safety) break;
            }
            return world;
        }

        /// <summary>逐月比对; 分叉时返回首个分叉月 (1-based 月序号=哈希索引对应的 monthIndex).</summary>
        public static bool TryFindFirstDivergence(
            IReadOnlyList<ulong> baseline,
            IReadOnlyList<ulong> other,
            out int firstDivergingMonth,
            out string detail)
        {
            int n = Math.Min(baseline.Count, other.Count);
            for (int i = 0; i < n; i++)
            {
                if (baseline[i] != other[i])
                {
                    firstDivergingMonth = i; // hashes[i] 对应刚到达的 monthIndex (==i 若从 0 起每月一条)
                    detail = $"首个分叉月 monthIndex={i}: baseline=0x{baseline[i]:X16} other=0x{other[i]:X16}";
                    return true;
                }
            }
            if (baseline.Count != other.Count)
            {
                firstDivergingMonth = n;
                detail = $"哈希序列长度分叉: baseline={baseline.Count} other={other.Count}";
                return true;
            }
            firstDivergingMonth = -1;
            detail = "无分叉";
            return false;
        }
    }

    [TestFixture]
    [Category("Gate0Determinism")]
    public class Gate0DeterminismTest
    {
        [Test]
        public void FourWayReplay_MonthlyHashes_Identical()
        {
            // 回退档若误开 NarrowSpeed，20× 会被夹成 5×，四路仍可能绿但测不到真 20×
            var probe = WorldState.CreateMinimalSlice(1);
            Assert.AreEqual(DeterminismFallbackLevel.None, probe.Fallback.Level,
                "Gate-0 四路必须在 Fallback=None 下跑，禁止 CI 静默降速");

            var h1 = Gate0ReplayRunner.Run(ReplayPath.Full1x);
            var h20 = Gate0ReplayRunner.Run(ReplayPath.Full20x);
            var hVar = Gate0ReplayRunner.Run(ReplayPath.VariableSpeed);
            var hSave = Gate0ReplayRunner.Run(ReplayPath.SaveLoad);

            Assert.GreaterOrEqual(h1.Count, Gate0ReplayRunner.MinGameMonths, "① 至少 120 个月哈希");

            AssertNoDiverge("①vs②", h1, h20);
            AssertNoDiverge("①vs③", h1, hVar);
            AssertNoDiverge("①vs④", h1, hSave);
        }

        [Test]
        public void FourWayReplay_CoversWarDisasterEra()
        {
            var world = Gate0ReplayRunner.RunForEvents();
            bool war = false, disaster = false, era = false;
            foreach (var e in world.Events)
            {
                if (e.category == SimEventCategory.War) war = true;
                if (e.category == SimEventCategory.Disaster) disaster = true;
                if (e.category == SimEventCategory.Era) era = true;
            }
            Assert.IsTrue(war, "≥120 月须含 ≥1 战事");
            Assert.IsTrue(disaster, "≥120 月须含 ≥1 灾害");
            Assert.IsTrue(era, "≥120 月须含 ≥1 时代过渡");
            Assert.GreaterOrEqual(world.Time.monthIndex, Gate0ReplayRunner.MinGameMonths);
        }

        [Test]
        public void FourWayReplay_SameSeedAndInterventionLog()
        {
            // 契约输入不变量: 四路共享同一 seed + InterventionLog
            var iv = Gate0ReplayRunner.DefaultInterventions();
            var a = Gate0ReplayRunner.Run(ReplayPath.Full1x, interventions: iv);
            var b = Gate0ReplayRunner.Run(ReplayPath.Full20x, interventions: iv);
            AssertNoDiverge("same-input 1×/20×", a, b);
        }

        [Test]
        public void DivergenceReport_MentionsFirstMonth()
        {
            // 人为制造分叉序列, 验证报告格式含首个分叉月
            var a = new List<ulong> { 1, 2, 3, 4 };
            var b = new List<ulong> { 1, 2, 9, 4 };
            Assert.IsTrue(Gate0ReplayRunner.TryFindFirstDivergence(a, b, out int month, out string detail));
            Assert.AreEqual(2, month);
            StringAssert.Contains("monthIndex=2", detail);
        }

        private static void AssertNoDiverge(string label, IReadOnlyList<ulong> a, IReadOnlyList<ulong> b)
        {
            if (Gate0ReplayRunner.TryFindFirstDivergence(a, b, out _, out string detail))
                Assert.Fail($"[{label}] Gate-0 分叉: {detail}");
        }
    }
}

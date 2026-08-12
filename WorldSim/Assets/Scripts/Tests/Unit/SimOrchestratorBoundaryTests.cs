// 算法独立边界证明 + 真实 SimOrchestrator 1×/20× 集成 (V0-3 / G0-1/2/3).

using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Slice;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    public enum BoundaryKind { Week, Month }

    public readonly struct BoundaryEvent
    {
        public readonly int GameMonth;
        public readonly BoundaryKind Kind;
        public BoundaryEvent(int gameMonth, BoundaryKind kind) { GameMonth = gameMonth; Kind = kind; }
    }

    /// <summary>
    /// 边界序列生成器 — 复刻 architecture §3.3 (整数序号派生).
    /// </summary>
    public static class BoundarySequencer
    {
        public const float MonthSeconds = 1.0f;
        public const float WeekSeconds = MonthSeconds / 4.0f;

        public static List<BoundaryEvent> Run(float speedMultiplier, int totalMonths, float dtReal = 1.0f)
        {
            var events = new List<BoundaryEvent>();
            float gameClock = 0f;
            int lastMonthEmitted = 0;

            while (lastMonthEmitted < totalMonths)
            {
                float target = gameClock + dtReal * speedMultiplier;
                while (true)
                {
                    int nextWeekIdx = (int)Math.Floor(gameClock / WeekSeconds) + 1;
                    int nextMonthIdx = (int)Math.Floor(gameClock / MonthSeconds) + 1;
                    float nextWeek = nextWeekIdx * WeekSeconds;
                    float nextMonth = nextMonthIdx * MonthSeconds;
                    float next = Math.Min(nextWeek, nextMonth);
                    if (next > target) break;

                    gameClock = next;
                    if (next == nextWeek) events.Add(new BoundaryEvent(nextMonthIdx - 1, BoundaryKind.Week));
                    if (next == nextMonth)
                    {
                        events.Add(new BoundaryEvent(nextMonthIdx, BoundaryKind.Month));
                        lastMonthEmitted = nextMonthIdx;
                    }
                }
                gameClock = target;
            }
            return events;
        }
    }

    [TestFixture]
    [Category("Gate0Determinism")]
    public class SimOrchestratorBoundaryTests
    {
        [Test]
        public void BoundarySequence_1x_And_20x_Identical()
        {
            var s1 = BoundarySequencer.Run(1f, 120);
            var s20 = BoundarySequencer.Run(20f, 120);
            Assert.AreEqual(s1.Count, s20.Count);
            for (int i = 0; i < s1.Count; i++)
            {
                Assert.AreEqual(s1[i].GameMonth, s20[i].GameMonth, $"事件 {i} 月序");
                Assert.AreEqual(s1[i].Kind, s20[i].Kind, $"事件 {i} 类型");
            }
        }

        [Test]
        public void BoundarySequence_SameTick_WeekBeforeMonth()
        {
            var seq = BoundarySequencer.Run(5f, 3);
            for (int i = 0; i < seq.Count - 1; i++)
            {
                if (seq[i].GameMonth == seq[i + 1].GameMonth && seq[i + 1].Kind == BoundaryKind.Month)
                    Assert.AreEqual(BoundaryKind.Week, seq[i].Kind, "同刻 week 必须先于 month");
            }
        }

        [Test]
        public void BoundarySequence_MonthCount_MatchesTarget()
        {
            var seq = BoundarySequencer.Run(20f, 120);
            int months = 0;
            foreach (var e in seq) if (e.Kind == BoundaryKind.Month) months++;
            Assert.GreaterOrEqual(months, 120);
        }

        [Test]
        public void ProductionOrchestrator_1x_And_20x_SameMonthAndEvents()
        {
            var e1 = RunSlice(1, 120);
            var e20 = RunSlice(20, 120);

            Assert.AreEqual(e1.MonthIndex, e20.MonthIndex);
            Assert.AreEqual(e1.WeekIndex, e20.WeekIndex);
            Assert.AreEqual(e1.EraIndex, e20.EraIndex);
            Assert.AreEqual(e1.Events.Count, e20.Events.Count, "事件数应一致");
            for (int i = 0; i < e1.Events.Count; i++)
            {
                Assert.AreEqual(e1.Events[i].gameMonth, e20.Events[i].gameMonth, $"事件 {i} 月");
                Assert.AreEqual(e1.Events[i].category, e20.Events[i].category, $"事件 {i} 类");
                Assert.AreEqual(e1.Events[i].sourceId, e20.Events[i].sourceId, $"事件 {i} 源");
                Assert.AreEqual(e1.Events[i].templateId, e20.Events[i].templateId, $"事件 {i} 模板");
                Assert.AreEqual(e1.Events[i].magnitude, e20.Events[i].magnitude, $"事件 {i} 量");
            }
        }

        [Test]
        public void ProductionOrchestrator_120Months_TriggersWarDisasterEra()
        {
            var snap = RunSlice(20, 120);
            bool war = false, disaster = false, era = false;
            foreach (var e in snap.Events)
            {
                if (e.category == SimEventCategory.War) war = true;
                if (e.category == SimEventCategory.Disaster) disaster = true;
                if (e.category == SimEventCategory.Era) era = true;
            }
            Assert.IsTrue(war, "≥120 月应触发 ≥1 战事");
            Assert.IsTrue(disaster, "≥120 月应触发 ≥1 灾害");
            Assert.IsTrue(era, "≥120 月应触发 ≥1 时代过渡");
            Assert.GreaterOrEqual(snap.MonthIndex, 120);
        }

        [Test]
        public void ProductionOrchestrator_HeadlessInstantiate_EmptyWorld()
        {
            var w = new WorldState(1);
            var orch = new SimOrchestrator(w);
            orch.Update(0.5f);
            Assert.AreEqual(0, w.Time.monthIndex);
            Assert.NotNull(orch);
        }

        private static SliceSnapshot RunSlice(int speed, int months)
        {
            var world = WorldState.CreateMinimalSlice(0xC0FFEE, speed);
            var orch = new SimOrchestrator(world);
            double need = months * TimeDriver.MONTH_SECONDS + 0.01;
            // 用较大 dtReal 减少循环次数; 速度倍率已写入 TimeDriver
            float dt = 1.0f;
            while (world.Time.monthIndex < months)
            {
                orch.Update(dt);
                // 安全阀: 避免死循环
                if (world.Time.gameClock > need * 2) break;
            }
            return new SliceSnapshot
            {
                MonthIndex = world.Time.monthIndex,
                WeekIndex = world.Time.weekIndex,
                EraIndex = world.EraIndex,
                Events = new List<SimEvent>(world.Events)
            };
        }

        private sealed class SliceSnapshot
        {
            public int MonthIndex;
            public int WeekIndex;
            public int EraIndex;
            public List<SimEvent> Events;
        }
    }
}

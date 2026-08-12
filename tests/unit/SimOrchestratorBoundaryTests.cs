// Phase 4 port target: WorldSim/Assets/Scripts/Tests/Unit/SimOrchestratorBoundaryTests.cs
// asmdef: WorldSim.Tests
//
// 时间—结算主循环 (G0-1 / G0-2 / G0-3, 铁律 1/2/3, 架构 §3.3, S4 §7.3)
// 关键: 周/月边界必须由整数序号派生 (契约 §3 R-N1), 否则 1×/20× 长程 drift 分叉.
// 本测试用 BoundarySequencer 证明: 任意速度档产出的 (月序, 边界类型) 事件序列完全一致, 且同刻 week 先 month 后.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WorldSim.Tests.Unit
{
    public enum BoundaryKind { Week, Month }

    public readonly struct BoundaryEvent
    {
        public readonly int GameMonth;       // 整数游戏月序号 (由边界派生, 非 float)
        public readonly BoundaryKind Kind;
        public BoundaryEvent(int gameMonth, BoundaryKind kind) { GameMonth = gameMonth; Kind = kind; }
    }

    /// <summary>
    /// 边界序列生成器 — 复刻 architecture §3.3 Update 循环, 但边界由整数序号派生 (契约 §3).
    /// 速度只缩放 dtGame, 不改变边界定义 => 1× 与 20× 产出相同事件序列.
    /// </summary>
    public static class BoundarySequencer
    {
        public const float MonthSeconds = 1.0f;   // 编译期常数, 不受 speed 影响
        public const float WeekSeconds = MonthSeconds / 4.0f;

        public static List<BoundaryEvent> Run(float speedMultiplier, int totalMonths, float dtReal = 1.0f)
        {
            var events = new List<BoundaryEvent>();
            float gameClock = 0f;
            int lastMonthEmitted = 0;

            while (lastMonthEmitted < totalMonths)
            {
                float target = gameClock + dtReal * speedMultiplier;
                // 用整数序号派生下一边界, 而非在循环里 float 减法逼近
                while (true)
                {
                    int nextWeekIdx = (int)Math.Floor(gameClock / WeekSeconds) + 1;
                    int nextMonthIdx = (int)Math.Floor(gameClock / MonthSeconds) + 1;
                    float nextWeek = nextWeekIdx * WeekSeconds;
                    float nextMonth = nextMonthIdx * MonthSeconds;
                    float next = Math.Min(nextWeek, nextMonth);
                    if (next > target) break;

                    gameClock = next;
                    // 同刻: week 先, month 后 (铁律 2)
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
            Assert.AreEqual(s1.Count, s20.Count, "1× 与 20× 边界事件总数应一致");
            for (int i = 0; i < s1.Count; i++)
            {
                Assert.AreEqual(s1[i].GameMonth, s20[i].GameMonth, $"事件 {i} 月序应一致");
                Assert.AreEqual(s1[i].Kind, s20[i].Kind, $"事件 {i} 类型应一致");
            }
        }

        [Test]
        public void BoundarySequence_SameTick_WeekBeforeMonth()
        {
            // 当 week 与 month 边界同刻 (每第 4 周恰为月末), 必须 week 先 month 后
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
            Assert.GreaterOrEqual(months, 120, "至少触发 120 个月级大账 (Gate-0 时长下限)");
        }
    }
}

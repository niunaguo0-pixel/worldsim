namespace WorldSim.Simulation.Core
{
    using System;

    /// <summary>
    /// S4 时间心跳数据 (确定性关键). 边界由整数序号派生 (R-N1), 见架构 §3.2 / §9.6 / 契约 §3.
    /// 纯 System.*. 注意: Update 推进逻辑在 WorldSim.Simulation.Time.SimOrchestrator,
    /// 本结构仅承载确定性状态 + 编译期常数 (避免 Time -> Core 循环依赖).
    /// </summary>
    public struct TimeDriver
    {
        /// <summary>编译期常数: 1 游戏月对应的游戏秒. 不受 speedMultiplier 影响 (契约 §3).</summary>
        public const double MONTH_SECONDS = 2.0;

        /// <summary>1 游戏周 = 1/4 月 (编译期常数).</summary>
        public const double WEEK_SECONDS = MONTH_SECONDS / 4.0;

        public double gameClock;     // 连续游戏时钟 (游戏秒, double 杜绝 float 累加器漂移)
        public int monthIndex;       // 已通过的月边界数 (整数序号, 月边界派生源)
        public int weekIndex;        // 已通过的周边界数 (整数序号, 周边界派生源)
        public int speedMultiplier;  // 1 / 2 / 5 / 20
        public bool paused;

        public TimeDriver(int speedMultiplier = 1, bool paused = false)
        {
            gameClock = 0.0;
            monthIndex = 0;
            weekIndex = 0;
            this.speedMultiplier = speedMultiplier;
            this.paused = paused;
        }
    }
}

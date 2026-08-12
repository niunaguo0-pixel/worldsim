namespace WorldSim.Simulation.Core
{
    using System;

    /// <summary>SimEvent 类别 (确定性产出 -> 叙事/UI).</summary>
    public enum SimEventCategory : byte
    {
        Ecology = 0,
        Civ = 1,
        War = 2,
        Disaster = 3,
        Era = 4,
        Chronicle = 5,
    }

    /// <summary>
    /// 确定性事件: pass 内有序产出 (稳定 ID 序), 挂当月快照.
    /// S6/S8 为纯消费者, 副作用只进各自状态, 不回写 WorldState (架构 §5.1 / 红线).
    /// 纯 System.*.
    /// </summary>
    public struct SimEvent
    {
        public int gameMonth;            // 游戏月时间戳 (锚定因果链)
        public SimEventCategory category;
        public int sourceId;             // 来源实体稳定 ID
        public string templateId;        // 叙事模板标识
        public double magnitude;         // 量化后的指标量 (入哈希前已 Quantize)

        public SimEvent(int gameMonth, SimEventCategory category, int sourceId, string templateId, double magnitude)
        {
            this.gameMonth = gameMonth;
            this.category = category;
            this.sourceId = sourceId;
            this.templateId = templateId;
            this.magnitude = magnitude;
        }
    }

    /// <summary>
    /// 干预记录 (按游戏月时间戳, 非现实时间). 对应架构 §2.1 InterventionLog.
    /// Replay 输入: 同 worldSeed + 同 InterventionLog => 演化逐月一致.
    /// </summary>
    public readonly struct InterventionRecord
    {
        public readonly int gameMonth;
        public readonly string action;

        public InterventionRecord(int gameMonth, string action)
        {
            this.gameMonth = gameMonth;
            this.action = action;
        }
    }
}

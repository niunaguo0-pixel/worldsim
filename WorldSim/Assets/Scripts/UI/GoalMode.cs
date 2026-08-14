namespace WorldSim.UI
{
    /// <summary>
    /// New Game 第 ⑤ 项「目标模式」——属 S8/概念文档，不进入 WorldInitConfig（S5 GDD §2.2.1）。
    /// </summary>
    public enum GoalMode : byte
    {
        /// <summary>沙盒·无胜利条件（默认）。</summary>
        SandboxNoVictory = 0,
        /// <summary>里程碑·城邦或王国级。</summary>
        MilestonePolity = 1,
        /// <summary>自定义目标（占位，判定逻辑后续接概念 §3.4.1）。</summary>
        Custom = 2
    }
}

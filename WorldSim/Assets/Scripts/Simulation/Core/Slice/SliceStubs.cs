namespace WorldSim.Simulation.Core.Slice
{
    using System;

    /// <summary>
    /// 切片最小聚落桩 (V0-3): 仅承载确定性事件序列所需字段, 非完整 S3 机制.
    /// 真实 Settlement 在 Epic 3 (S3-1/2) 实现; 本批足以产生月/周事件.
    /// </summary>
    public sealed class SettlementStub
    {
        public int stableId;
        public string name;
        public double population;
        public bool isAtWar;
        public bool underDisaster;
        public bool constructionActive;
        public int warMonths;
        public int disasterMonths;
        public int constructionMonths;
    }

    /// <summary>切片最小物种桩 (V0-3): 承载生态稳态/灾害触发所需字段, 非完整 S2 机制.</summary>
    public sealed class SpeciesStub
    {
        public int stableId;
        public string name;
        public double population;
        public int stressMonths;
    }

    /// <summary>切片最小政体桩 (V0-3): 承载发展值/时代过渡阈值, 非完整 S3 机制.</summary>
    public sealed class PolityStub
    {
        public int stableId;
        public string name;
        public double development; // 驱动时代过渡阈值
    }
}

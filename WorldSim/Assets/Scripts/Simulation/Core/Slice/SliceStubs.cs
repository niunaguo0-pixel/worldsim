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
        public double growthRate;
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

    /// <summary>
    /// 切片最小政体桩 — 时代门闩对齐 GDD S3 v1.4.4（禁绝对人口）:
    /// TechTier + 持续食物盈余月数 + pop/CC 利用率 + 分工/法制/文字 stub.
    /// 完整 16 步文明月账归 Epic 3.
    /// </summary>
    public sealed class PolityStub
    {
        public int stableId;
        public string name;
        public double population;
        public double aggregateOutput;
        public double aggregateMilitaryPower;
        public double aggregateStability;
        public int techTier;
        public int sustainedSurplusMonths;
        public double capacityUtilization; // pop / carryingCapacity(tech,land)
        public int divisionDepth;
        public int lawStage;
        public bool hasWriting;
    }

    /// <summary>切片资源桩 — 入月哈希 (契约 §2.2/§2.3).</summary>
    public sealed class ResourceStub
    {
        public int stableId;
        public string name;
        public double currentAmount;
    }

    /// <summary>
    /// Epic 0 切片时代门槛表 (GDD §2.0.2 v1.4.4 子集).
    /// 绝不含 requiredPopulation / 绝对人口阈值.
    /// </summary>
    public readonly struct EraGateSpec
    {
        public readonly int RequiredTechTier;
        public readonly int MinSustainedSurplusMonths;
        public readonly double MinCapacityUtilization;
        public readonly int MinDivisionDepth;
        public readonly int MinLawStage;
        public readonly bool RequiresWriting;

        public EraGateSpec(
            int requiredTechTier,
            int minSustainedSurplusMonths,
            double minCapacityUtilization,
            int minDivisionDepth,
            int minLawStage,
            bool requiresWriting)
        {
            RequiredTechTier = requiredTechTier;
            MinSustainedSurplusMonths = minSustainedSurplusMonths;
            MinCapacityUtilization = minCapacityUtilization;
            MinDivisionDepth = minDivisionDepth;
            MinLawStage = minLawStage;
            RequiresWriting = requiresWriting;
        }
    }

    /// <summary>切片时代门闩判定 — 纯函数, 便于单测.</summary>
    public static class EraGate
    {
        // 目标时代索引 = EraIndex+1 时用的门槛; 表长允许多次跃迁覆盖 Gate-0 ≥120 月
        public static readonly EraGateSpec[] NextEraGates =
        {
            // → 远古晚期 / 古代早期
            new EraGateSpec(2, 6, 0.45, 1, 0, false),
            // → 古代
            new EraGateSpec(3, 8, 0.50, 2, 1, true),
            // → 中古 stub
            new EraGateSpec(4, 10, 0.55, 3, 2, true),
            // → 近代 stub
            new EraGateSpec(5, 12, 0.60, 4, 3, true),
        };

        public static bool Meets(PolityStub p, EraGateSpec g)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (p.techTier < g.RequiredTechTier) return false;
            if (p.sustainedSurplusMonths < g.MinSustainedSurplusMonths) return false;
            if (p.capacityUtilization < g.MinCapacityUtilization) return false;
            if (p.divisionDepth < g.MinDivisionDepth) return false;
            if (p.lawStage < g.MinLawStage) return false;
            if (g.RequiresWriting && !p.hasWriting) return false;
            return true;
        }

        public static bool TryGetNextGate(int currentEraIndex, out EraGateSpec gate)
        {
            if (currentEraIndex < 0 || currentEraIndex >= NextEraGates.Length)
            {
                gate = default;
                return false;
            }
            gate = NextEraGates[currentEraIndex];
            return true;
        }
    }
}

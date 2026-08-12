namespace WorldSim.Simulation.Core
{
    using System;

    /// <summary>
    /// Gate-0 / G0-8 / ADR-002 / 架构 §4.4 三级回退档位.
    /// 默认 None（不启用）; 仅人工或 CI 在分叉时显式降级.
    /// 底线: 任意档位均保留连续时间体感, 绝不退回月历回合制.
    /// </summary>
    public enum DeterminismFallbackLevel : int
    {
        /// <summary>正常: 1×/2×/5×/20× 全开; float+Quantize; 允许步间输入.</summary>
        None = 0,
        /// <summary>回退1: 收窄速度档（去 20×）; 干预对齐月边界.</summary>
        NarrowSpeed = 1,
        /// <summary>回退2: 在回退1 之上 — pass 内强制串行 + 关键量 Fix 定点.</summary>
        SerialFix = 2,
        /// <summary>回退3: 在回退2 之上 — 确定性 lockstep, 步间不接收输入.</summary>
        Lockstep = 3,
    }

    /// <summary>
    /// 三级回退策略钩子 (V0-7). 配置位 + 查询 API; 默认不触发.
    /// </summary>
    public sealed class DeterminismFallback
    {
        public static readonly int[] FullSpeedTiers = { 1, 2, 5, 20 };
        public static readonly int[] NarrowSpeedTiers = { 1, 2, 5 };

        public DeterminismFallbackLevel Level { get; private set; }

        public DeterminismFallback(DeterminismFallbackLevel level = DeterminismFallbackLevel.None)
        {
            Level = level;
        }

        /// <summary>显式降级/恢复. 仅供人工诊断或 CI 配置调用, 模拟永不自动触发.</summary>
        public void SetLevel(DeterminismFallbackLevel level)
        {
            if (!Enum.IsDefined(typeof(DeterminismFallbackLevel), level))
                throw new ArgumentOutOfRangeException(nameof(level));
            Level = level;
        }

        public bool AllowsSpeed20 => Level == DeterminismFallbackLevel.None;
        public bool NarrowSpeedTiersOnly => Level >= DeterminismFallbackLevel.NarrowSpeed;
        public bool AlignInterventionsToMonthBoundary => Level >= DeterminismFallbackLevel.NarrowSpeed;
        public bool ForceSerialPass => Level >= DeterminismFallbackLevel.SerialFix;
        public bool UseFixForKeyQuantities => Level >= DeterminismFallbackLevel.SerialFix;
        public bool LockstepNoInterstepInput => Level >= DeterminismFallbackLevel.Lockstep;

        /// <summary>红线断言: 任意回退档都不是回合制.</summary>
        public bool IsTurnBased => false;

        public int MaxAllowedSpeed => AllowsSpeed20 ? 20 : 5;

        /// <summary>将请求速度夹到当前档位允许集合; 未知值向下取最近合法档.</summary>
        public int ClampSpeedMultiplier(int requested)
        {
            int[] allowed = NarrowSpeedTiersOnly ? NarrowSpeedTiers : FullSpeedTiers;
            if (requested <= 0) return 1;
            int best = allowed[0];
            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == requested) return requested;
                if (allowed[i] <= requested) best = allowed[i];
            }
            return best;
        }

        public bool IsSpeedAllowed(int speed)
        {
            int[] allowed = NarrowSpeedTiersOnly ? NarrowSpeedTiers : FullSpeedTiers;
            for (int i = 0; i < allowed.Length; i++)
                if (allowed[i] == speed) return true;
            return false;
        }
    }
}

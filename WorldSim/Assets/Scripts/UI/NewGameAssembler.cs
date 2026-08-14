namespace WorldSim.UI
{
    using System;
    using WorldSim.Simulation.WorldMap;

    /// <summary>
    /// 将 New Game 草稿装配为 WorldInitConfig。
    /// GoalMode 故意不写入 config（S5 不读）；由 GameSession 另行持有。
    /// </summary>
    public static class NewGameAssembler
    {
        public static readonly int[] SuggestedBorderYears = { 2026, 1945, 1914 };

        public static WorldInitConfig Assemble(NewGameDraft draft, RegionPresetCatalog catalog)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrEmpty(draft.PresetKey))
                throw new ArgumentException("PresetKey required", nameof(draft));

            WorldInitConfig cfg = RegionPresetLoader.ConsumeKey(catalog, draft.PresetKey);
            cfg.StartEra = draft.StartEra;
            cfg.BorderYear = draft.BorderYear;
            cfg.NormalizeDerivedMode();

            if (cfg.StartMode == StartMode.ModernGeopolitics)
            {
                if (!draft.UsePresetLegalBias)
                    cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = draft.LegalBiasOverride };
                // UsePresetLegalBias：保留 Consume 时从预设映射的 LegalTraditionSeed
            }

            RegionPresetRedLines.ValidateInitConfig(cfg);
            return cfg;
        }

        public static string DescribeMode(WorldInitConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            return cfg.StartMode == StartMode.PrimordialSandbox
                ? "远古沙盒（无真实国界）"
                : "当今地缘政治（国界年 " + cfg.BorderYear + "）";
        }

        public static string GoalModeLabel(GoalMode mode)
        {
            switch (mode)
            {
                case GoalMode.SandboxNoVictory: return "沙盒·无胜利条件";
                case GoalMode.MilestonePolity: return "里程碑·城邦/王国";
                case GoalMode.Custom: return "自定义目标";
                default: return mode.ToString();
            }
        }
    }
}

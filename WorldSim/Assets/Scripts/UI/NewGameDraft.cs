namespace WorldSim.UI
{
    using System.Collections.Generic;
    using WorldSim.ModularToggle;
    using WorldSim.Simulation.WorldMap;

    /// <summary>New Game 面板草稿态（地理 4 项 + 目标 1 项 + S7 模块）。</summary>
    public sealed class NewGameDraft
    {
        public StartEra StartEra = StartEra.Modern;
        public string PresetKey = "fertile_crescent";
        public int BorderYear = 2026;
        public bool UsePresetLegalBias = true;
        public LegalFamilyBias LegalBiasOverride = LegalFamilyBias.CustomaryLaw;
        public GoalMode GoalMode = GoalMode.SandboxNoVictory;
        public ulong WorldSeed = 42;
        /// <summary>玩家面向模块开关（不含 ecology.v2 / civilization.v2 引擎轨）。</summary>
        public Dictionary<string, bool> ModuleSelections = ModularToggleService.CapturePlayerFacingDefaults();

        public static NewGameDraft CreateDefaults() => new NewGameDraft();
    }
}

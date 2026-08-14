namespace WorldSim.UI
{
    using WorldSim.Simulation.WorldMap;

    /// <summary>New Game 面板草稿态（地理 4 项 + 目标 1 项）。</summary>
    public sealed class NewGameDraft
    {
        public StartEra StartEra = StartEra.Modern;
        public string PresetKey = "fertile_crescent";
        public int BorderYear = 2026;
        public bool UsePresetLegalBias = true;
        public LegalFamilyBias LegalBiasOverride = LegalFamilyBias.CustomaryLaw;
        public GoalMode GoalMode = GoalMode.SandboxNoVictory;
        public ulong WorldSeed = 42;

        public static NewGameDraft CreateDefaults() => new NewGameDraft();
    }
}

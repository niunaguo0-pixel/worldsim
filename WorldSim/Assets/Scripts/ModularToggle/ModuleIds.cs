namespace WorldSim.ModularToggle
{
    /// <summary>规范模块键（写入 WorldState.ModuleToggles，排序后入档）。</summary>
    public static class ModuleIds
    {
        public const string EcologyV2 = "ecology.v2";
        public const string CivilizationV2 = "civilization.v2";
        public const string GenerationInheritance = "generation.inheritance";
        public const string TechTree = "tech.tree";
        public const string SettlementMulti = "settlement.multi";
        public const string PoliticsStructure = "politics.structure";
        public const string ReligionSystem = "religion.system";
        public const string CultureSystem = "culture.system";
        public const string LawSystem = "law.system";
        public const string EthnicitySystem = "ethnicity.system";
        public const string MilitarySystem = "military.system";
    }

    public enum ModulePreset : byte
    {
        /// <summary>MVP：可选子系统默认关；引擎轨默认关直至 Attach。</summary>
        MvpMinimal = 0,
        /// <summary>挂载正式文明引擎后的兼容默认：子系统全开（保持既有 Epic3 行为）。</summary>
        AttachedCivilization = 1,
        /// <summary>核心层全开（性能预算 / 完整玩法）。</summary>
        CoreFullyOpen = 2
    }

    public enum ModuleCategory : byte
    {
        Engine = 0,
        Civilization = 1,
        Ecology = 2,
        Time = 3
    }
}

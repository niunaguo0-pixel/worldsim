namespace WorldSim.ModularToggle
{
    using System;
    using System.Collections.Generic;

    /// <summary>S7 模块目录：权威键表 + MVP 默认值。</summary>
    public static class ModuleCatalog
    {
        private static readonly ModuleDefinition[] Definitions =
        {
            new ModuleDefinition(ModuleIds.EcologyV2, "生态引擎 v2", "正式 S2 生态月结", false, ModuleCategory.Ecology, false),
            new ModuleDefinition(ModuleIds.CivilizationV2, "文明引擎 v2", "正式 S3 十六步月结", false, ModuleCategory.Civilization, false),
            new ModuleDefinition(ModuleIds.GenerationInheritance, "世代传承", "个体死亡后继承与世代里程碑", false, ModuleCategory.Time, true),
            new ModuleDefinition(ModuleIds.TechTree, "科技树", "科技积累与解锁步进", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.SettlementMulti, "多聚落", "多聚落扩张与跨聚落战事", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.PoliticsStructure, "政治结构", "政治演变与更替", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.ReligionSystem, "宗教", "信仰与宗教步进", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.CultureSystem, "文化", "文化特质步进", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.LawSystem, "法律", "法律家族与法制步进", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.EthnicitySystem, "族群", "族群构成步进", false, ModuleCategory.Civilization, true),
            new ModuleDefinition(ModuleIds.MilitarySystem, "军事", "军力与战争步进", false, ModuleCategory.Civilization, true),
        };

        private static readonly Dictionary<string, ModuleDefinition> ById = BuildIndex();

        public static IReadOnlyList<ModuleDefinition> All => Definitions;

        public static IEnumerable<ModuleDefinition> PlayerFacing
        {
            get
            {
                for (int i = 0; i < Definitions.Length; i++)
                    if (Definitions[i].PlayerFacing)
                        yield return Definitions[i];
            }
        }

        public static bool TryGet(string id, out ModuleDefinition definition) =>
            ById.TryGetValue(id, out definition);

        public static bool Contains(string id) => ById.ContainsKey(id);

        public static bool DefaultEnabled(string id) =>
            ById.TryGetValue(id, out var d) && d.DefaultEnabled;

        private static Dictionary<string, ModuleDefinition> BuildIndex()
        {
            var map = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < Definitions.Length; i++)
                map[Definitions[i].Id] = Definitions[i];
            return map;
        }
    }
}

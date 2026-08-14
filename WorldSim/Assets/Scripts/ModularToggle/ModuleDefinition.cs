namespace WorldSim.ModularToggle
{
    /// <summary>单个可开关模块的目录条目。</summary>
    public sealed class ModuleDefinition
    {
        public ModuleDefinition(
            string id,
            string displayNameZh,
            string descriptionZh,
            bool defaultEnabled,
            ModuleCategory category,
            bool playerFacing)
        {
            Id = id;
            DisplayNameZh = displayNameZh;
            DescriptionZh = descriptionZh;
            DefaultEnabled = defaultEnabled;
            Category = category;
            PlayerFacing = playerFacing;
        }

        public string Id { get; }
        public string DisplayNameZh { get; }
        public string DescriptionZh { get; }
        public bool DefaultEnabled { get; }
        public ModuleCategory Category { get; }
        /// <summary>是否出现在 New Game「模块化开关」面板。</summary>
        public bool PlayerFacing { get; }
    }
}

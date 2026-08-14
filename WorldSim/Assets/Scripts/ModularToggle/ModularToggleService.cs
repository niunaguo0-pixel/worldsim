namespace WorldSim.ModularToggle
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;

    /// <summary>
    /// S7 模块化开关服务：写入 WorldState.ModuleToggles（确定性态，入月哈希）。
    /// </summary>
    public static class ModularToggleService
    {
        /// <summary>补齐目录中缺失的键（不覆盖已有值）。</summary>
        public static void EnsureKeys(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.ModuleToggles == null)
                world.ModuleToggles = new Dictionary<string, bool>();

            foreach (var def in ModuleCatalog.All)
            {
                if (!world.ModuleToggles.ContainsKey(def.Id))
                    world.ModuleToggles[def.Id] = def.DefaultEnabled;
            }
        }

        public static void Set(WorldState world, string id, bool enabled)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!ModuleCatalog.Contains(id))
                throw new ArgumentException("Unknown module id: " + id, nameof(id));
            EnsureKeys(world);
            world.ModuleToggles[id] = enabled;
        }

        public static bool IsEnabled(WorldState world, string id)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.ModuleToggles != null && world.ModuleToggles.TryGetValue(id, out bool enabled))
                return enabled;
            return ModuleCatalog.DefaultEnabled(id);
        }

        public static void ApplyPreset(WorldState world, ModulePreset preset)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            EnsureKeys(world);

            switch (preset)
            {
                case ModulePreset.MvpMinimal:
                    ApplyAllDefaults(world);
                    break;
                case ModulePreset.AttachedCivilization:
                    ApplyAllDefaults(world);
                    world.ModuleToggles[ModuleIds.CivilizationV2] = true;
                    EnableCivilizationSubsystems(world, enabled: true);
                    // 世代传承保持默认关，除非调用方显式打开
                    world.ModuleToggles[ModuleIds.GenerationInheritance] = false;
                    break;
                case ModulePreset.CoreFullyOpen:
                    foreach (var def in ModuleCatalog.All)
                        world.ModuleToggles[def.Id] = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>把玩家面向开关写入世界（引擎轨由 Attach 决定）。</summary>
        public static void ApplyPlayerFacing(WorldState world, IReadOnlyDictionary<string, bool> selections)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            EnsureKeys(world);
            if (selections == null) return;
            foreach (var kv in selections)
            {
                if (!ModuleCatalog.TryGet(kv.Key, out var def) || !def.PlayerFacing)
                    continue;
                world.ModuleToggles[kv.Key] = kv.Value;
            }
        }

        public static Dictionary<string, bool> CapturePlayerFacingDefaults()
        {
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var def in ModuleCatalog.PlayerFacing)
                map[def.Id] = def.DefaultEnabled;
            return map;
        }

        public static IReadOnlyList<string> ListUnknownKeys(WorldState world)
        {
            if (world?.ModuleToggles == null) return Array.Empty<string>();
            var unknown = new List<string>();
            foreach (var key in world.ModuleToggles.Keys)
            {
                if (!ModuleCatalog.Contains(key))
                    unknown.Add(key);
            }
            unknown.Sort(StringComparer.Ordinal);
            return unknown;
        }

        private static void ApplyAllDefaults(WorldState world)
        {
            foreach (var def in ModuleCatalog.All)
                world.ModuleToggles[def.Id] = def.DefaultEnabled;
        }

        private static void EnableCivilizationSubsystems(WorldState world, bool enabled)
        {
            world.ModuleToggles[ModuleIds.TechTree] = enabled;
            world.ModuleToggles[ModuleIds.SettlementMulti] = enabled;
            world.ModuleToggles[ModuleIds.PoliticsStructure] = enabled;
            world.ModuleToggles[ModuleIds.ReligionSystem] = enabled;
            world.ModuleToggles[ModuleIds.CultureSystem] = enabled;
            world.ModuleToggles[ModuleIds.LawSystem] = enabled;
            world.ModuleToggles[ModuleIds.EthnicitySystem] = enabled;
            world.ModuleToggles[ModuleIds.MilitarySystem] = enabled;
        }
    }
}

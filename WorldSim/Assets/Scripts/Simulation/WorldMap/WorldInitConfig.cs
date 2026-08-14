namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core.Civilization;

    public enum StartEra : byte { Primordial = 0, EarlyModern = 1, Modern = 2 }
    public enum StartMode : byte { PrimordialSandbox = 0, ModernGeopolitics = 1 }

    /// <summary>
    /// 国界视图选择 (Task 4): DeFactoControl 用 NE admin-0 countries (258 单元),
    /// SovereigntyClaims 用 NE admin-0 sovereignty (209 主权). 争议区按源标记保留,
    /// 不编造裁决。默认 DeFactoControl。
    /// </summary>
    public enum BorderView : byte { DeFactoControl = 0, SovereigntyClaims = 1 }

    /// <summary>法系偏置枚举 (仅作 legalTraditionSeed, 绝不绑定单国家族 — B5).</summary>
    public enum LegalFamilyBias
    {
        CivilLaw = 0,
        CommonLaw = 1,
        SocialistLaw = 2,
        CustomaryLaw = 3,
    }

    /// <summary>ethnicSeed 单条: 语系/名称/份额 (地缘种子, 非国家 ID).</summary>
    public readonly struct EthnicSeedEntry
    {
        public readonly string LanguageFamily;
        public readonly string Name;
        public readonly double Share;

        public EthnicSeedEntry(string languageFamily, string name, double share)
        {
            LanguageFamily = languageFamily;
            Name = name;
            Share = share;
        }
    }

    /// <summary>region-presets.json 单条预设 (schemaVersion 1.0).</summary>
    public sealed class RegionPreset
    {
        public string Key;
        public string Name;
        public double CenterLat;
        public double CenterLon;
        public double RadiusDeg;
        public List<EthnicSeedEntry> EthnicSeed = new List<EthnicSeedEntry>();
        public string LegalFamilyDefault;
    }

    /// <summary>地缘民族分布种子 (S5): 仅 languageFamily+share, 无国家级绑定.</summary>
    public sealed class RealEthnicDistribution
    {
        public List<EthnicSeedEntry> Groups = new List<EthnicSeedEntry>();
    }

    /// <summary>法系传统偏置种子 — Bias only, 不为任一 Polity 指定 LawFamily.</summary>
    public sealed class LegalTraditionSeed
    {
        public LegalFamilyBias Bias;

        public LawFamily ToLawFamily()
        {
            switch (Bias)
            {
                case LegalFamilyBias.CivilLaw: return LawFamily.CivilLaw;
                case LegalFamilyBias.CommonLaw: return LawFamily.CommonLaw;
                case LegalFamilyBias.SocialistLaw: return LawFamily.SocialistLaw;
                case LegalFamilyBias.CustomaryLaw: return LawFamily.CustomaryLaw;
                default: throw new ArgumentOutOfRangeException(nameof(Bias), Bias, "Unsupported legal family bias");
            }
        }
    }

    /// <summary>
    /// New Game 装配配置 (架构 §9.1 / ADR-004).
    /// 禁止字段: 任何 per-polity lawFamily / ethnicGroup 指定 (B5 红线).
    /// </summary>
    public sealed class WorldInitConfig
    {
        public string PresetKey;
        public StartEra StartEra = StartEra.Modern;
        public StartMode StartMode = StartMode.ModernGeopolitics;
        public int BorderYear = 2026;
        public bool UseRealBorders = true;
        /// <summary>国界视图 (Task 4): 默认 DeFactoControl, 可切到 SovereigntyClaims.</summary>
        public BorderView BorderView = BorderView.DeFactoControl;
        public string GeoDataBuild = "";
        public double StartRegionCenterLat;
        public double StartRegionCenterLon;
        public double StartRegionRadiusDeg;
        public RealEthnicDistribution EthnicDistribution;
        public LegalTraditionSeed LegalTraditionSeed;
        /// <summary>MVP 区域网格分辨率 (度/格); High 精度切片默认 0.5°.</summary>
        public double DegPerTile = 0.5;

        public void NormalizeDerivedMode()
        {
            UseRealBorders = StartEra != global::WorldSim.Simulation.WorldMap.StartEra.Primordial;
            StartMode = UseRealBorders
                ? global::WorldSim.Simulation.WorldMap.StartMode.ModernGeopolitics
                : global::WorldSim.Simulation.WorldMap.StartMode.PrimordialSandbox;
            if (!UseRealBorders) BorderYear = 0;
            if (StartMode == global::WorldSim.Simulation.WorldMap.StartMode.PrimordialSandbox)
            {
                EthnicDistribution = null;
                LegalTraditionSeed = null;
            }
        }
    }

    /// <summary>简化 WorldTile (V0-6 MVP High 区; 完整 S5 管线后续扩展).</summary>
    public sealed class WorldTile
    {
        public int LatIdx;
        public int LonIdx;
        public double Lat;
        public double Lon;
        public double Elevation;   // 简化: 由纬度/距中心推导
        public byte BiomeId;       // 0 荒漠 1 草原 2 森林 3 水域邻域
        public byte Lod;           // 0=High
        public bool HasCoast;
    }

    /// <summary>MVP 高精度起始区域地图.</summary>
    public sealed class MvpRegionMap
    {
        public WorldInitConfig Config;
        public WorldTile[,] Tiles;
        public int Width;
        public int Height;
        public double MinLat, MaxLat, MinLon, MaxLon;
    }
}

namespace WorldSim.Simulation.Core
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core.Ecology;
    using WorldSim.Simulation.Core.Civilization;
    using WorldSim.Simulation.Core.Random;
    using WorldSim.Simulation.Core.Slice;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>
    /// WorldState 聚合根 — 确定性状态唯一真相源 (架构 §2.1 / §9.6).
    /// Epic 0 切片含最小 S2/S3 桩; 完整 11/16 步业务在 Epic 2/3.
    /// 纯 System.*.
    /// </summary>
    public sealed class WorldState
    {
        public ulong worldSeed;
        public TimeDriver Time;
        public RngRegistry Rng;
        public List<SettlementStub> Settlements;
        public List<SpeciesStub> Species;
        public List<PolityStub> Polities;
        public List<ResourceStub> Resources;
        /// <summary>S2 正式生态态；默认不开启以保持 Epic 0 Gate-0 基线。</summary>
        public EcologyState Ecology;
        /// <summary>S3 正式文明态；默认关闭以冻结 Gate-0 切片路径。</summary>
        public CivilizationState Civilization;
        public List<SimEvent> Events;
        public StableIdSet ActiveEntities;
        public List<InterventionRecord> InterventionLog;
        public int EraIndex;
        /// <summary>模块开关 (世代传承/科技树/…); 键排序后写入快照.</summary>
        public Dictionary<string, bool> ModuleToggles;
        /// <summary>G0-8 三级回退钩子; 默认 None, 不自动触发.</summary>
        public DeterminismFallback Fallback;
        /// <summary>可持久地图态：仅配置、静态 bundle 引用与动态覆盖。</summary>
        public WorldMapState Map;
        /// <summary>运行时只读地理服务；transient，不直接序列化。</summary>
        public IWorldGeography Geography;
        /// <summary>可选 S1 月结算器 (可玩月循环 / InterventionSystem).</summary>
        public IMonthlyInterventionSettler InterventionSettler;
        public IMonthlyEcologySettler EcologySettler;
        public IMonthlyCivilizationSettler CivilizationSettler;

        public WorldState(ulong worldSeed, int speedMultiplier = 1)
        {
            this.worldSeed = worldSeed;
            Time = new TimeDriver(speedMultiplier);
            Rng = new RngRegistry(worldSeed);
            Settlements = new List<SettlementStub>();
            Species = new List<SpeciesStub>();
            Polities = new List<PolityStub>();
            Resources = new List<ResourceStub>();
            Ecology = new EcologyState();
            Civilization = new CivilizationState();
            Events = new List<SimEvent>();
            ActiveEntities = new StableIdSet();
            InterventionLog = new List<InterventionRecord>();
            EraIndex = 0;
            ModuleToggles = new Dictionary<string, bool>();
            ModuleToggles["generation.inheritance"] = false;
            Fallback = new DeterminismFallback(DeterminismFallbackLevel.None);
            Map = new WorldMapState();
            Geography = null;
            InterventionSettler = null;
            EcologySettler = null;
            CivilizationSettler = null;
        }

        /// <summary>
        /// V0-3 切片工厂: 1 聚落 + 2 物种 + 1 政体 + 1 资源, 足以在 ≥120 月触发战事/灾害/时代过渡.
        /// 时代门闩用 TechTier/盈余/利用率, 不用绝对人口 (S3 v1.4.4).
        /// </summary>
        public static WorldState CreateMinimalSlice(ulong worldSeed, int speedMultiplier = 1)
        {
            var w = new WorldState(worldSeed, speedMultiplier);
            w.Settlements.Add(new SettlementStub
            {
                stableId = 1,
                name = "Alpha",
                population = 100.0,
                growthRate = 0.01
            });
            w.Species.Add(new SpeciesStub
            {
                stableId = 10,
                name = "Prey",
                population = 500.0
            });
            w.Species.Add(new SpeciesStub
            {
                stableId = 11,
                name = "Predator",
                population = 80.0
            });
            w.Polities.Add(new PolityStub
            {
                stableId = 100,
                name = "PolityA",
                population = 100.0,
                aggregateOutput = 10.0,
                aggregateMilitaryPower = 1.0,
                aggregateStability = 0.5,
                techTier = 1,
                sustainedSurplusMonths = 0,
                capacityUtilization = 0.30,
                divisionDepth = 0,
                lawStage = 0,
                hasWriting = false
            });
            w.Resources.Add(new ResourceStub
            {
                stableId = 200,
                name = "Food",
                currentAmount = 50.0
            });
            w.ModuleToggles["ecology.v2"] = false;
            w.ModuleToggles["civilization.v2"] = false;
            return w;
        }
    }
}

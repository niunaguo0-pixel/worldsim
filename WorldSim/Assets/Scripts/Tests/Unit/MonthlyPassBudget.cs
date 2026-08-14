namespace WorldSim.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using WorldSim.Simulation.Civilization;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Civilization;
    using WorldSim.Simulation.Core.Ecology;
    using WorldSim.Simulation.Ecology;
    using WorldSim.Simulation.Intervention;
    using WorldSim.Simulation.Time;
    using WorldSim.Simulation.WorldMap;

    /// <summary>
    /// Epic 8 T3 / B6：构造「核心层全开」世界并实测单月推进耗时。
    /// 预算：架构 §7.5 单次月级大账 &lt; 50ms（含同期周结，串行路径）。
    /// </summary>
    public static class MonthlyPassBudget
    {
        public const double BudgetMilliseconds = 50.0;
        public const int DefaultSettlementCount = 32;
        public const int DefaultWarmupMonths = 4;
        public const int DefaultSampleMonths = 16;

        public static WorldState CreateCoreFullyOpen(
            ulong seed,
            int settlementCount = DefaultSettlementCount,
            string geoRoot = null)
        {
            if (settlementCount < 1) throw new ArgumentOutOfRangeException(nameof(settlementCount));

            var world = WorldState.CreateMinimalSlice(seed);
            if (!string.IsNullOrEmpty(geoRoot))
            {
                var cfg = new WorldInitConfig
                {
                    PresetKey = "fertile_crescent",
                    StartEra = StartEra.Primordial,
                    StartRegionCenterLat = 33,
                    StartRegionCenterLon = 44,
                    StartRegionRadiusDeg = 8
                };
                WorldMapFactory.Build(geoRoot, cfg, world);
            }

            InterventionSystem.AttachToSlice(world);
            EcologySimEngine.AttachTo(world);
            CivilizationSimEngine.AttachTo(world);
            ExpandCivilization(world, settlementCount);
            ExpandEcology(world, Math.Max(4, settlementCount / 8));

            // 激活半数聚落，迫使周级子结算有实质遍历（切片桩 underDisaster）
            world.ActiveEntities = new StableIdSet();
            for (int i = 0; i < world.Civilization.Settlements.Count; i++)
            {
                var s = world.Civilization.Settlements[i];
                if ((s.stableId & 1) == 1)
                {
                    world.ActiveEntities.Add(s.stableId);
                    EnsureSliceSettlement(world, s.stableId, disaster: true);
                }
            }

            return world;
        }

        /// <summary>预热后对 AdvanceGameTime(1 月) 取中位耗时（毫秒）。</summary>
        public static double MeasureMedianMonthMilliseconds(
            WorldState world,
            int warmupMonths = DefaultWarmupMonths,
            int sampleMonths = DefaultSampleMonths)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (sampleMonths < 1) throw new ArgumentOutOfRangeException(nameof(sampleMonths));

            var orch = new SimOrchestrator(world);
            for (int i = 0; i < warmupMonths; i++)
                orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);

            var samples = new List<double>(sampleMonths);
            for (int i = 0; i < sampleMonths; i++)
            {
                var sw = Stopwatch.StartNew();
                orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            return samples[samples.Count / 2];
        }

        public static double MeasureMaxMonthMilliseconds(
            WorldState world,
            int warmupMonths = DefaultWarmupMonths,
            int sampleMonths = DefaultSampleMonths)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var orch = new SimOrchestrator(world);
            for (int i = 0; i < warmupMonths; i++)
                orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);

            double max = 0;
            for (int i = 0; i < sampleMonths; i++)
            {
                var sw = Stopwatch.StartNew();
                orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
                sw.Stop();
                if (sw.Elapsed.TotalMilliseconds > max)
                    max = sw.Elapsed.TotalMilliseconds;
            }
            return max;
        }

        private static void ExpandCivilization(WorldState world, int settlementCount)
        {
            var civ = world.Civilization;
            int baseTile = civ.Settlements.Count > 0 ? civ.Settlements[0].worldTileId : 0;
            int polityCount = Math.Max(2, settlementCount / 8);

            for (int p = 1; p < polityCount; p++)
            {
                int pid = 100 + p;
                civ.Polities.Add(new CivilizationPolityState
                {
                    stableId = pid,
                    techTier = 1 + (p % 3),
                    stability = 0.5,
                    legitimacy = 0.4,
                    militaryPower = 1,
                    Ethnicity = EthnicComposition.CreateSingletonDominant("Band" + p, "Unclassified", p + 1),
                    LegitimacySources = new LegitimacySource(),
                    Military = new MilitaryState()
                });
                civ.Tech.Add(new TechProgressState { stableId = p + 1, polityId = pid });
            }

            for (int i = civ.Settlements.Count; i < settlementCount; i++)
            {
                int sid = i + 1;
                int polityId = 100 + (i % polityCount);
                civ.Settlements.Add(new CivilizationSettlementState
                {
                    stableId = sid,
                    worldTileId = baseTile,
                    polityId = polityId,
                    population = 80 + i * 3,
                    housingCapacity = 300,
                    foodCapacity = 250,
                    spaceCapacity = 500,
                    prosperity = 0.45
                });
                civ.Economies.Add(new CivilizationEconomyState
                {
                    stableId = sid,
                    settlementId = sid,
                    food = 20 + i,
                    wood = 8
                });
                civ.Individuals.Add(new IndividualState
                {
                    stableId = sid,
                    settlementId = sid,
                    alive = true,
                    health = 1
                });
                EnsureSliceSettlement(world, sid, disaster: false);
            }
        }

        private static void ExpandEcology(WorldState world, int regionCount)
        {
            var eco = world.Ecology;
            var zone = new HomeostasisZone
            {
                CriticalLower = 0.1, StableLower = 0.3, EquilibriumPoint = 0.6,
                StableUpper = 0.85, CriticalUpper = 1.2, SelfRepairRate = 0.15,
                StressDecayFactor = 0.4, StressDurationLimit = 6
            };
            int baseTile = eco.Regions.Count > 0 ? eco.Regions[0].worldTileId : 0;
            int nextSpecies = 1000;
            int nextResource = 2000;
            int nextIndicator = 3000;
            int nextLink = 4000;

            for (int r = eco.Regions.Count; r < regionCount; r++)
            {
                int rid = r + 1;
                eco.Regions.Add(new EcologyRegionState
                {
                    stableId = rid,
                    worldTileId = baseTile,
                    baseRainfall = 800,
                    baseTemperature = 18
                });
                int plantId = nextSpecies++;
                int herbId = nextSpecies++;
                int carnId = nextSpecies++;
                eco.Species.Add(new EcologySpeciesState
                {
                    stableId = plantId, regionId = rid, name = "Plant" + rid,
                    trophicLevel = SpeciesTrophicLevel.Plant, population = 500,
                    birthRate = 0.2, deathRate = 0.05, carryingCapacity = 1000,
                    climateSensitivity = 0.5, homeostasis = zone
                });
                eco.Species.Add(new EcologySpeciesState
                {
                    stableId = herbId, regionId = rid, name = "Herb" + rid,
                    trophicLevel = SpeciesTrophicLevel.Herbivore, population = 120,
                    birthRate = 0.12, deathRate = 0.04, carryingCapacity = 400,
                    climateSensitivity = 0.6, homeostasis = zone
                });
                eco.Species.Add(new EcologySpeciesState
                {
                    stableId = carnId, regionId = rid, name = "Carn" + rid,
                    trophicLevel = SpeciesTrophicLevel.Carnivore, population = 25,
                    birthRate = 0.08, deathRate = 0.03, carryingCapacity = 100,
                    climateSensitivity = 0.4, homeostasis = zone
                });
                eco.FoodChain.Add(new FoodChainLink
                {
                    stableId = nextLink++, predatorId = herbId, preyId = plantId,
                    predationRate = 0.04, dependencyRatio = 1
                });
                eco.FoodChain.Add(new FoodChainLink
                {
                    stableId = nextLink++, predatorId = carnId, preyId = herbId,
                    predationRate = 0.03, dependencyRatio = 1
                });
                eco.Resources.Add(new RenewableResourceState
                {
                    stableId = nextResource++, regionId = rid, kind = ResourceKind.Forest,
                    currentAmount = 70, maxAmount = 100, regenRate = 3, homeostasis = zone
                });
                eco.Indicators.Add(new EcologicalIndicatorState
                {
                    stableId = nextIndicator++, regionId = rid,
                    code = "food-chain-health", currentValue = 1, previousValue = 1
                });
            }
        }

        private static void EnsureSliceSettlement(WorldState world, int stableId, bool disaster)
        {
            for (int i = 0; i < world.Settlements.Count; i++)
            {
                if (world.Settlements[i].stableId != stableId) continue;
                world.Settlements[i].underDisaster = disaster;
                world.Settlements[i].disasterMonths = disaster ? 3 : 0;
                return;
            }
            world.Settlements.Add(new WorldSim.Simulation.Core.Slice.SettlementStub
            {
                stableId = stableId,
                name = "S" + stableId,
                population = 100,
                underDisaster = disaster,
                disasterMonths = disaster ? 3 : 0
            });
        }
    }
}

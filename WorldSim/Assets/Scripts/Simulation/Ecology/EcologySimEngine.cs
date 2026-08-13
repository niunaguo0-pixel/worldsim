namespace WorldSim.Simulation.Ecology
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Ecology;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.Slice;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>
    /// S2 十一步确定性生态月结。所有集合以 stableId 排序；
    /// 地貌步骤在 MVP 中仅推进进度，不依赖 Unity 或帧时间。
    /// </summary>
    public sealed class EcologySimEngine : IMonthlyEcologySettler
    {
        private const double Epsilon = 0.0001;

        public static EcologySimEngine AttachTo(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Ecology == null || world.Ecology.Species.Count == 0)
                world.Ecology = CreateMinimalState(world.Geography);
            world.ModuleToggles["ecology.v2"] = true;
            var engine = new EcologySimEngine();
            world.EcologySettler = engine;
            return engine;
        }

        public void SettleMonth(WorldState world, int month)
        {
            if (world == null || world.Ecology == null) return;
            var eco = world.Ecology;
            if (eco.LastSettledMonth == month) return;

            // 1 读取已在 S1 生效的参数；2 由 monthIndex 派生季节。
            ApplyInterventionParameters(world, eco);
            eco.CurrentSeason = (Season)((month / 3) % 4);
            // 3 植物；4 食草；5 食肉；6 资源。
            StepSpecies(eco, SpeciesTrophicLevel.Plant, month, world);
            StepSpecies(eco, SpeciesTrophicLevel.Herbivore, month, world);
            StepSpecies(eco, SpeciesTrophicLevel.Carnivore, month, world);
            StepResources(eco, month);
            // 7 地貌；8 稳态已随上面各步更新；9 相变；10 指标；11 事件。
            StepTerrain(eco);
            UpdateIndicators(eco);
            EmitWarnings(world, eco, month);
            eco.LastSettledMonth = month;
        }

        private static void ApplyInterventionParameters(WorldState world, EcologyState eco)
        {
            var source = world.InterventionSettler as IInterventionParameterSource;
            if (source == null) return;
            var regions = Sorted(eco.Regions, x => x.stableId);
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (source.TryGetParameterValue("rainfall_" + r.stableId, out double rain))
                    r.rainfallModifier = Q(rain);
                if (source.TryGetParameterValue("temperature_" + r.stableId, out double temp))
                    r.temperatureModifier = Q(temp);
            }
            var species = Sorted(eco.Species, x => x.stableId);
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                if (source.TryGetParameterValue("birthRate_" + s.stableId, out double birth))
                    s.birthRate = Q(Math.Max(0.0, s.birthRate + birth));
                if (source.TryGetParameterValue("population_" + s.stableId, out double pop))
                    s.population = Q(Math.Max(0.0, s.population + pop));
            }
            var resources = Sorted(eco.Resources, x => x.stableId);
            for (int i = 0; i < resources.Count; i++)
            {
                var r = resources[i];
                if (source.TryGetParameterValue("regenRate_" + r.stableId, out double regen))
                    r.regenRate = Q(Math.Max(0.0, r.regenRate + regen));
            }
        }

        private static void StepSpecies(EcologyState eco, SpeciesTrophicLevel level, int month, WorldState world)
        {
            var list = Sorted(eco.Species, s => s.stableId);
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.trophicLevel != level || s.phase == PhaseState.Collapsed) continue;
                s.homeostasis.Validate();
                var region = Region(eco, s.regionId);
                double climate = ClimateFactor(eco.CurrentSeason, s.climateSensitivity, region);
                double food = FoodAvailability(eco, s);
                double predation = PredationPressure(eco, s.stableId);
                double capacity = Math.Max(1.0, s.carryingCapacity);
                double delta = ((s.birthRate * climate * food) - (s.deathRate + predation)) *
                    s.population * (1.0 - s.population / capacity);
                s.population = Q(Math.Max(0.0, s.population + delta));
                double ratio = RepairRatio(s.homeostasis, SafeRatio(s.population, capacity));
                s.population = Q(ratio * capacity);
                UpdateZone(s.homeostasis, ratio, ref s.zone, ref s.stressMonths, ref s.phase);
            }
        }

        private static void StepResources(EcologyState eco, int month)
        {
            var list = Sorted(eco.Resources, r => r.stableId);
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r.phase == PhaseState.Collapsed) continue;
                r.homeostasis.Validate();
                double factor = r.zone == EcologyZone.Stress ? r.homeostasis.StressDecayFactor : 1.0;
                r.currentAmount = Q(Math.Max(0.0, Math.Min(r.maxAmount,
                    r.currentAmount + r.regenRate * factor - r.harvestRate)));
                double ratio = RepairRatio(r.homeostasis, SafeRatio(r.currentAmount, r.maxAmount));
                r.currentAmount = Q(ratio * r.maxAmount);
                UpdateZone(r.homeostasis, ratio,
                    ref r.zone, ref r.stressMonths, ref r.phase);
            }
        }

        private static void StepTerrain(EcologyState eco)
        {
            var regions = Sorted(eco.Regions, r => r.stableId);
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.terrainPhase == PhaseState.Collapsed) continue;
                r.terrainEvolution = Q(Math.Min(1.0, r.terrainEvolution + 0.002));
            }
        }

        private static void UpdateIndicators(EcologyState eco)
        {
            var indicators = Sorted(eco.Indicators, x => x.stableId);
            for (int i = 0; i < indicators.Count; i++)
            {
                var x = indicators[i];
                x.previousValue = x.currentValue;
                double stable = 0.0;
                int count = 0;
                foreach (var s in eco.Species)
                {
                    if (s.regionId != x.regionId) continue;
                    count++;
                    stable += s.zone == EcologyZone.Stable ? 1.0 : s.zone == EcologyZone.Stress ? 0.5 : 0.0;
                }
                x.currentValue = Q(count == 0 ? 0.0 : stable / count);
                x.zone = x.currentValue < 0.25 ? EcologyZone.PhaseTransition :
                    x.currentValue < 0.75 ? EcologyZone.Stress : EcologyZone.Stable;
                x.stressMonths = x.zone == EcologyZone.Stable ? 0 : x.stressMonths + 1;
                x.warningCode = x.zone == EcologyZone.PhaseTransition ? "ecology.warning.critical" :
                    x.stressMonths >= 3 ? "ecology.warning.emergency" :
                    x.zone == EcologyZone.Stress ? "ecology.warning.stress" : "";
            }
        }

        private static void EmitWarnings(WorldState world, EcologyState eco, int month)
        {
            var indicators = Sorted(eco.Indicators, x => x.stableId);
            for (int i = 0; i < indicators.Count; i++)
            {
                var x = indicators[i];
                if (string.IsNullOrEmpty(x.warningCode)) continue;
                world.Events.Add(new SimEvent(month, SimEventCategory.Ecology,
                    x.stableId, x.warningCode, x.currentValue));
            }
        }

        private static void UpdateZone(HomeostasisZone z, double value, ref EcologyZone zone,
            ref int stressMonths, ref PhaseState phase)
        {
            zone = z.Classify(value);
            if (zone == EcologyZone.PhaseTransition)
            {
                phase = PhaseState.Collapsed;
                return;
            }
            if (zone == EcologyZone.Stress) stressMonths++;
            else stressMonths = 0;
            if (stressMonths >= z.StressDurationLimit)
            {
                zone = EcologyZone.PhaseTransition;
                phase = PhaseState.Degraded;
            }
        }

        private static double RepairRatio(HomeostasisZone z, double value)
        {
            var zone = z.Classify(value);
            if (zone == EcologyZone.PhaseTransition) return value;
            double rate = z.SelfRepairRate * (zone == EcologyZone.Stress ? z.StressDecayFactor : 1.0);
            return Q(value + (z.EquilibriumPoint - value) * rate);
        }

        private static double ClimateFactor(Season season, double sensitivity, EcologyRegionState r)
        {
            double seasonal = season == Season.Spring ? 1.1 : season == Season.Summer ? 1.0 :
                season == Season.Autumn ? 0.9 : 0.7;
            double climate = 1.0 + (r.rainfallModifier * 0.01) - (Math.Abs(r.temperatureModifier) * 0.01);
            return Math.Max(0.0, seasonal * (1.0 + (climate - 1.0) * sensitivity));
        }

        private static double FoodAvailability(EcologyState eco, EcologySpeciesState s)
        {
            if (s.trophicLevel == SpeciesTrophicLevel.Plant) return 1.0;
            double available = 0.0;
            foreach (var link in eco.FoodChain)
                if (link.predatorId == s.stableId)
                    available += Population(eco, link.preyId) * link.dependencyRatio;
            return Math.Min(1.5, Math.Max(0.05, available / Math.Max(1.0, s.carryingCapacity)));
        }

        private static double PredationPressure(EcologyState eco, int preyId)
        {
            double pressure = 0.0;
            foreach (var link in eco.FoodChain)
                if (link.preyId == preyId)
                    pressure += Population(eco, link.predatorId) * link.predationRate / 100.0;
            return pressure;
        }

        private static double Population(EcologyState eco, int id)
        {
            for (int i = 0; i < eco.Species.Count; i++)
                if (eco.Species[i].stableId == id) return eco.Species[i].population;
            return 0.0;
        }

        private static EcologyRegionState Region(EcologyState eco, int id)
        {
            for (int i = 0; i < eco.Regions.Count; i++)
                if (eco.Regions[i].stableId == id) return eco.Regions[i];
            throw new InvalidOperationException("生态物种引用了不存在区域: " + id);
        }

        private static List<T> Sorted<T>(List<T> source, Func<T, int> id)
        {
            var copy = new List<T>(source);
            copy.Sort((a, b) => id(a).CompareTo(id(b)));
            return copy;
        }
        private static double SafeRatio(double a, double b) => b <= Epsilon ? 0.0 : a / b;
        private static double Q(double value) => DeterminismMath.Quantize(value, 3);

        public static EcologyState CreateMinimalState(IWorldGeography geography = null)
        {
            var z = new HomeostasisZone
            {
                CriticalLower = 0.1, StableLower = 0.3, EquilibriumPoint = 0.6,
                StableUpper = 0.85, CriticalUpper = 1.2, SelfRepairRate = 0.15,
                StressDecayFactor = 0.4, StressDurationLimit = 6
            };
            var state = new EcologyState();
            int worldTileId = ResolveDefaultTile(geography);
            double rain = geography == null ? 1 : geography.GetTile(worldTileId).BaseRainfallMm;
            double temp = geography == null ? 20 : geography.GetTile(worldTileId).BaseTemperatureC;
            state.Regions.Add(new EcologyRegionState { stableId = 1, worldTileId = worldTileId, baseRainfall = rain, baseTemperature = temp });
            state.Species.Add(new EcologySpeciesState { stableId = 10, regionId = 1, name = "Plant", trophicLevel = SpeciesTrophicLevel.Plant, population = 600, birthRate = .2, deathRate = .05, carryingCapacity = 1000, climateSensitivity = .5, homeostasis = z });
            state.Species.Add(new EcologySpeciesState { stableId = 11, regionId = 1, name = "Herbivore", trophicLevel = SpeciesTrophicLevel.Herbivore, population = 150, birthRate = .12, deathRate = .04, carryingCapacity = 400, climateSensitivity = .6, homeostasis = z });
            state.Species.Add(new EcologySpeciesState { stableId = 12, regionId = 1, name = "Carnivore", trophicLevel = SpeciesTrophicLevel.Carnivore, population = 30, birthRate = .08, deathRate = .03, carryingCapacity = 100, climateSensitivity = .4, homeostasis = z });
            state.FoodChain.Add(new FoodChainLink { stableId = 100, predatorId = 11, preyId = 10, predationRate = .04, dependencyRatio = 1 });
            state.FoodChain.Add(new FoodChainLink { stableId = 101, predatorId = 12, preyId = 11, predationRate = .03, dependencyRatio = 1 });
            state.Resources.Add(new RenewableResourceState { stableId = 200, regionId = 1, kind = ResourceKind.Forest, currentAmount = 70, maxAmount = 100, regenRate = 3, homeostasis = z });
            state.Resources.Add(new RenewableResourceState { stableId = 201, regionId = 1, kind = ResourceKind.Fishery, currentAmount = 65, maxAmount = 100, regenRate = 2, homeostasis = z });
            state.Indicators.Add(new EcologicalIndicatorState { stableId = 300, regionId = 1, code = "food-chain-health", currentValue = 1 });
            return state;
        }

        private static int ResolveDefaultTile(IWorldGeography geography)
        {
            if (geography == null) return 0;
            var tile = geography.GetTile(new GeoCoordinate(33, 44), MapLodLevel.High);
            return tile.TileId;
        }
    }
}

namespace WorldSim.Simulation.Core.Ecology
{
    using System;
    using System.Collections.Generic;

    public enum EcologyZone : byte { Stable, Stress, PhaseTransition }
    public enum SpeciesTrophicLevel : byte { Plant, Herbivore, Carnivore }
    public enum ResourceKind : byte { Forest, Fishery }
    public enum Season : byte { Spring, Summer, Autumn, Winter }
    public enum PhaseState : byte { None, Degraded, Collapsed }

    /// <summary>三段式稳态参数和纯函数更新规则。</summary>
    public struct HomeostasisZone
    {
        public double StableLower, StableUpper, CriticalLower, CriticalUpper, EquilibriumPoint;
        public double SelfRepairRate, StressDecayFactor;
        public int StressDurationLimit;

        public void Validate()
        {
            if (double.IsNaN(EquilibriumPoint) || double.IsInfinity(EquilibriumPoint) ||
                CriticalLower > StableLower || StableLower > EquilibriumPoint ||
                EquilibriumPoint > StableUpper || StableUpper > CriticalUpper ||
                SelfRepairRate < 0 || StressDecayFactor < 0 || StressDurationLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(HomeostasisZone));
        }

        public EcologyZone Classify(double value)
        {
            if (value < CriticalLower || value > CriticalUpper) return EcologyZone.PhaseTransition;
            if (value < StableLower || value > StableUpper) return EcologyZone.Stress;
            return EcologyZone.Stable;
        }
    }

    public sealed class EcologyRegionState
    {
        public int stableId;
        public int worldTileId;
        public double baseRainfall, baseTemperature;
        public double rainfallModifier, temperatureModifier;
        public double terrainEvolution;
        public PhaseState terrainPhase;
    }

    public sealed class EcologySpeciesState
    {
        public int stableId, regionId;
        public string name;
        public SpeciesTrophicLevel trophicLevel;
        public double population, birthRate, deathRate, carryingCapacity;
        public double climateSensitivity;
        public HomeostasisZone homeostasis;
        public EcologyZone zone;
        public int stressMonths;
        public PhaseState phase;
    }

    public sealed class FoodChainLink
    {
        public int stableId, predatorId, preyId;
        public double predationRate, dependencyRatio;
    }

    public sealed class RenewableResourceState
    {
        public int stableId, regionId;
        public ResourceKind kind;
        public double currentAmount, maxAmount, regenRate, harvestRate;
        public HomeostasisZone homeostasis;
        public EcologyZone zone;
        public int stressMonths;
        public PhaseState phase;
    }

    public sealed class EcologicalIndicatorState
    {
        public int stableId, regionId;
        public string code;
        public double currentValue, previousValue;
        public EcologyZone zone;
        public int stressMonths;
        public string warningCode;
    }

    /// <summary>S2 唯一正式生态态；在 ecology.v2 打开时由 Ecology 程序集推进。</summary>
    public sealed class EcologyState
    {
        public const int ModelVersion = 1;
        public List<EcologyRegionState> Regions = new List<EcologyRegionState>();
        public List<EcologySpeciesState> Species = new List<EcologySpeciesState>();
        public List<FoodChainLink> FoodChain = new List<FoodChainLink>();
        public List<RenewableResourceState> Resources = new List<RenewableResourceState>();
        public List<EcologicalIndicatorState> Indicators = new List<EcologicalIndicatorState>();
        public Season CurrentSeason;
        public int LastSettledMonth = -1;
    }
}

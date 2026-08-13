namespace WorldSim.Simulation.Core.Civilization
{
    using System.Collections.Generic;

    public enum SettlementTier : byte { Village, Town, City, Metro }
    public enum GovernanceType : byte { CustomaryCouncil, Chiefdom, CityState, Kingdom }
    public enum LawFamily : byte { CustomaryLaw, CivilLaw, CommonLaw, ReligiousLaw }
    public enum TitleTier : byte { None, Chief, King, Emperor }
    public enum ScaleTier : byte { Local, Regional, Continental, Global }
    public enum DominionMode : byte { None, Direct, Tributary, Federal }

    public sealed class CivilizationSettlementState
    {
        public int stableId, worldTileId, polityId;
        public double population, housingCapacity, foodCapacity, spaceCapacity, prosperity;
        public SettlementTier tier;
        public bool agricultureZone, housingZone, storageZone;
    }

    public sealed class CivilizationPolityState
    {
        public int stableId, techTier, sustainedSurplusMonths, divisionDepth, lawStage;
        public double population, output, militaryPower, stability, legitimacy, capacityUtilization;
        public bool hasWriting;
        public GovernanceType governance;
        public LawFamily lawFamily;
        public TitleTier titleTier;
        public ScaleTier scaleTier;
        public DominionMode dominionMode;
        public double aggregationCost;
    }

    public sealed class CivilizationEconomyState
    {
        public int stableId, settlementId;
        public double food, wood, stone, goods, energy, foodSurplus, divisionLevel;
        public byte exchangeMode;
    }

    public sealed class TechProgressState
    {
        public int stableId, polityId;
        public double agriculture, hunt, defense, trade, faith, military, culture;
    }

    public sealed class IndividualState
    {
        public int stableId, settlementId, ageMonths;
        public double health;
        public byte occupation;
        public bool alive;
    }

    public sealed class CivilizationState
    {
        public const int ModelVersion = 1;
        public List<CivilizationSettlementState> Settlements = new List<CivilizationSettlementState>();
        public List<CivilizationPolityState> Polities = new List<CivilizationPolityState>();
        public List<CivilizationEconomyState> Economies = new List<CivilizationEconomyState>();
        public List<TechProgressState> Tech = new List<TechProgressState>();
        public List<IndividualState> Individuals = new List<IndividualState>();
        public int LastSettledMonth = -1;
        public double EcoImpactCoefficient = 1.0;
    }
}

namespace WorldSim.Simulation.Core.Civilization
{
    using System;
    using System.Collections.Generic;

    public enum SettlementTier : byte { Village, Town, City, Metro }
    public enum GovernanceType : byte { CustomaryCouncil, Chiefdom, CityState, Kingdom }
    /// <summary>ReligiousLaw 仅兼容旧档；种子与涌现路径只产出四大家族。</summary>
    public enum LawFamily : byte { CustomaryLaw, CivilLaw, CommonLaw, ReligiousLaw, SocialistLaw }
    public enum TitleTier : byte { None, Chief, King, Emperor }
    public enum ScaleTier : byte { Local, Regional, Continental, Global }
    public enum DominionMode : byte { None, Direct, Tributary, Federal }
    public enum ExchangeMode : byte { Reciprocity, Tribute, Market, Monetary, Industrial, ServiceInfo }
    public enum WarStatus : byte { Idle, AtWar, Recovering }

    /// <summary>合法性四项世俗来源（GDD §2.4）；不含宗教项。</summary>
    public sealed class LegitimacySource
    {
        public double Performance;
        public double Consensus;
        public double Lineage;
        public double Institution;
    }

    public sealed class EthnicGroup
    {
        public int StableId;
        public string Name = "";
        public string LanguageFamily = "";
        public double PopulationShare;
    }

    /// <summary>族群构成；MVP 强制单主导：groups.Count==1、share==1、fractionalization==0。</summary>
    public sealed class EthnicComposition
    {
        public List<EthnicGroup> Groups = new List<EthnicGroup>();
        public double Fractionalization;
        public double Polarization;
        public double EthnicInequality;

        public static EthnicComposition CreateSingletonDominant(
            string name, string languageFamily, int stableId = 1)
        {
            var c = new EthnicComposition
            {
                Fractionalization = 0,
                Polarization = 0,
                EthnicInequality = 0
            };
            c.Groups.Add(new EthnicGroup
            {
                StableId = stableId,
                Name = name ?? "Band",
                LanguageFamily = languageFamily ?? "Unclassified",
                PopulationShare = 1.0
            });
            return c;
        }

        /// <summary>强制折叠为单主导；取最大份额族群，丢弃其余。</summary>
        public void EnforceMvpFold()
        {
            if (Groups == null) Groups = new List<EthnicGroup>();
            if (Groups.Count == 0)
            {
                Groups.Add(new EthnicGroup
                {
                    StableId = 1, Name = "Band", LanguageFamily = "Unclassified", PopulationShare = 1.0
                });
            }
            else if (Groups.Count > 1)
            {
                EthnicGroup dominant = Groups[0];
                for (int i = 1; i < Groups.Count; i++)
                    if (Groups[i].PopulationShare > dominant.PopulationShare
                        || (Groups[i].PopulationShare == dominant.PopulationShare
                            && Groups[i].StableId < dominant.StableId))
                        dominant = Groups[i];
                Groups.Clear();
                Groups.Add(dominant);
            }
            Groups[0].PopulationShare = 1.0;
            Fractionalization = 0;
            EthnicInequality = 0;
        }
    }

    public sealed class MilitaryState
    {
        public double Weariness;
        public WarStatus Status;
        public int OpponentPolityId;
        /// <summary>S5-2 / S3：沿海 + 军事科技达标后解锁海军；入月哈希。</summary>
        public bool HasNavy;
    }

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
        /// <summary>S3-4: 合法性四来源；null 时月结按默认填充。</summary>
        public LegitimacySource LegitimacySources = new LegitimacySource();
        /// <summary>S3-4: 族群构成；MVP 单主导。</summary>
        public EthnicComposition Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified");
        /// <summary>S3-4: 军事疲劳与战况；军力仍用 militaryPower。</summary>
        public MilitaryState Military = new MilitaryState();
        /// <summary>法律公正度，由 lawStage 派生供给 institution。</summary>
        public double Impartiality;
        /// <summary>沙盒近代锁定后为 true。</summary>
        public bool LawFamilyLocked;
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
        public const int ModelVersion = 2; // S3-4
        public List<CivilizationSettlementState> Settlements = new List<CivilizationSettlementState>();
        public List<CivilizationPolityState> Polities = new List<CivilizationPolityState>();
        public List<CivilizationEconomyState> Economies = new List<CivilizationEconomyState>();
        public List<TechProgressState> Tech = new List<TechProgressState>();
        public List<IndividualState> Individuals = new List<IndividualState>();
        public int LastSettledMonth = -1;
        public double EcoImpactCoefficient = 1.0;
    }
}

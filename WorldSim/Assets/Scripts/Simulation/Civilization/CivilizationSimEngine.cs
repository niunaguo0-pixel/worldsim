namespace WorldSim.Simulation.Civilization
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Civilization;
    using WorldSim.Simulation.Core.Ecology;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>S3 固定十六步文明月结；关闭 civilization.v2 时完全不参与 Gate-0 桩路径。</summary>
    public sealed class CivilizationSimEngine : IMonthlyCivilizationSettler
    {
        public static CivilizationSimEngine AttachTo(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Civilization == null || world.Civilization.Settlements.Count == 0)
                world.Civilization = CreateMinimalState(world.Geography);
            world.ModuleToggles["civilization.v2"] = true;
            var engine = new CivilizationSimEngine();
            world.CivilizationSettler = engine;
            return engine;
        }

        public void SettleMonth(WorldState world, int month)
        {
            var civ = world.Civilization;
            if (civ == null || civ.LastSettledMonth == month) return;
            // 1-16 顺序为确定性契约；各关闭模块保留稳定空步骤。
            ApplyInterventionBias(world, civ);        // 1
            ApplyEcologyModifiers(world, civ);        // 2
            StepIndividuals(world, civ, month);       // 3
            StepEconomy(civ);                         // 4
            StepSettlements(world, civ);              // 5
            StepTechnology(civ);                      // 6
            StepSociety(civ);                         // 7
            StepReligion(civ);                        // 8
            StepCulture(civ);                         // 9
            StepEthnicity(civ);                       // 10
            StepLaw(civ);                             // 11
            StepPolitics(world, civ, month);          // 12
            StepMilitary(world, civ, month);          // 13
            StepEra(world, civ, month);               // 14
            AggregatePolities(civ);                   // 15
            EmitEvents(world, civ, month);            // 16
            civ.LastSettledMonth = month;
        }

        private static void ApplyInterventionBias(WorldState world, CivilizationState civ)
        {
            var source = world.InterventionSettler as IInterventionParameterSource;
            if (source == null) return;
            foreach (var economy in Sorted(civ.Economies, x => x.stableId))
            {
                if (source.TryGetParameterValue("foodReserveCoeff_" + economy.settlementId, out double coeff))
                    economy.food = Q(Math.Max(0, economy.food * coeff));
            }
            foreach (var tech in Sorted(civ.Tech, x => x.stableId))
            {
                if (source.TryGetParameterValue("techUnlockBoost_" + tech.polityId, out double boost))
                    tech.agriculture = Q(tech.agriculture + boost);
            }
            // S3-4：玩家军事仅经 devBias_military_{settlementId} 偏移军力
            foreach (var settlement in Sorted(civ.Settlements, x => x.stableId))
            {
                if (!source.TryGetParameterValue("devBias_military_" + settlement.stableId, out double bias))
                    continue;
                var polity = PolityFor(civ, settlement.polityId);
                if (polity == null) continue;
                polity.militaryPower = Q(Math.Max(0, polity.militaryPower + bias * 0.1));
            }
        }

        private static void ApplyEcologyModifiers(WorldState world, CivilizationState civ)
        {
            if (world.Ecology == null || world.Ecology.Indicators.Count == 0) return;
            double health = world.Ecology.Indicators[0].currentValue;
            foreach (var e in civ.Economies)
                e.food = Q(Math.Max(0, e.food * (0.5 + health * 0.5)));
        }

        private static void StepIndividuals(WorldState world, CivilizationState civ, int month)
        {
            bool inheritanceEnabled = IsModuleEnabled(world, "generation.inheritance");
            int nextStableId = inheritanceEnabled ? NextIndividualStableId(civ) : 0;
            foreach (var individual in Sorted(civ.Individuals, x => x.stableId))
            {
                if (!individual.alive) continue;
                individual.ageMonths++;
                individual.health = Q(Math.Max(0, individual.health - 0.001));
                if (individual.health > 0) continue;
                individual.alive = false;
                world.Events.Add(new SimEvent(
                    month,
                    SimEventCategory.Civ,
                    individual.stableId,
                    "civ.individual.death",
                    individual.ageMonths));
                if (!inheritanceEnabled) continue;
                var heir = new IndividualState
                {
                    stableId = nextStableId++,
                    settlementId = individual.settlementId,
                    ageMonths = 0,
                    health = 1,
                    occupation = individual.occupation,
                    alive = true
                };
                int generation = GenerationOf(world, individual.stableId) + 1;
                civ.Individuals.Add(heir);
                world.Events.Add(new SimEvent(
                    month,
                    SimEventCategory.Civ,
                    heir.stableId,
                    "civ.individual.inheritance",
                    individual.stableId));
                world.Events.Add(new SimEvent(
                    month,
                    SimEventCategory.Chronicle,
                    heir.stableId,
                    "civ.generation.milestone",
                    generation));
            }
        }

        private static void StepEconomy(CivilizationState civ)
        {
            foreach (var e in Sorted(civ.Economies, x => x.stableId))
            {
                e.food = Q(e.food + 8.0 - 5.0);
                e.wood = Q(e.wood + 1.0);
                e.foodSurplus = Q(e.food - 20.0);
                e.divisionLevel = Q(Math.Max(0, e.foodSurplus / 20.0));
                e.exchangeMode = (byte)(e.divisionLevel >= 2 ? 1 : 0);
            }
        }

        private static void StepSettlements(WorldState world, CivilizationState civ)
        {
            foreach (var s in Sorted(civ.Settlements, x => x.stableId))
            {
                var e = EconomyFor(civ, s.stableId);
                double cc = CarryingCapacity(s);
                double growth = e != null && e.foodSurplus > 0 ? 0.015 : -0.01;
                if (world.Geography != null)
                {
                    var tile = world.Geography.GetTile(s.worldTileId);
                    if (!tile.IsLand || tile.Slope > 20) growth -= 0.05;
                    else if (world.Geography.HasWaterNearby(s.worldTileId)) growth += 0.003;
                }
                if (s.population > cc) growth -= 0.04;
                s.population = Q(Math.Max(0, s.population * (1 + growth)));
                s.prosperity = Q(Math.Max(0, Math.Min(1, s.prosperity + growth)));
                s.tier = s.population >= 10000 ? SettlementTier.Metro : s.population >= 2000 ? SettlementTier.City :
                    s.population >= 500 ? SettlementTier.Town : SettlementTier.Village;
                s.agricultureZone = s.tier >= SettlementTier.Village;
                s.housingZone = s.tier >= SettlementTier.Town;
                s.storageZone = s.tier >= SettlementTier.City;
            }
        }

        private static void StepTechnology(CivilizationState civ)
        {
            foreach (var t in Sorted(civ.Tech, x => x.stableId))
            {
                var p = PolityFor(civ, t.polityId);
                if (p == null) continue;
                t.agriculture = Q(t.agriculture + 0.03);
                if (t.agriculture >= 1.0 && p.techTier < 8) p.techTier++;
                p.hasWriting |= p.techTier >= 3;
            }
        }

        private static void StepSociety(CivilizationState civ) { }
        private static void StepReligion(CivilizationState civ) { }
        private static void StepCulture(CivilizationState civ) { }

        /// <summary>⑩ 族群：MVP 强制单主导折叠；ethnicInequality 恒 0 写入稳定度路径。</summary>
        private static void StepEthnicity(CivilizationState civ)
        {
            foreach (var p in Sorted(civ.Polities, x => x.stableId))
            {
                if (p.Ethnicity == null)
                    p.Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified");
                p.Ethnicity.EnforceMvpFold();
                p.Ethnicity.EthnicInequality = 0;
            }
        }

        /// <summary>⑪ 法律：lawStage 推进 + 沙盒家族涌现（近代锁定）+ impartiality 供给。</summary>
        private static void StepLaw(CivilizationState civ)
        {
            foreach (var p in Sorted(civ.Polities, x => x.stableId))
            {
                p.lawStage = Math.Min(5, p.lawStage + 1);
                p.Impartiality = Q(p.lawStage / 5.0);
                if (p.lawFamily == LawFamily.ReligiousLaw)
                    p.lawFamily = LawFamily.CustomaryLaw; // 不进合法性路径
                if (!p.LawFamilyLocked)
                    p.lawFamily = EmergeSecularLawFamily(p);
            }
        }

        /// <summary>沙盒涌现：近代前可演进；调用方在 StepPolitics/Era 后由 LockLawFamilies 锁定。</summary>
        private static LawFamily EmergeSecularLawFamily(CivilizationPolityState p)
        {
            if (p.techTier >= 6) return LawFamily.SocialistLaw;
            if (p.lawStage >= 4 && p.governance == GovernanceType.Kingdom) return LawFamily.CivilLaw;
            if (p.lawStage >= 3 && p.hasWriting) return LawFamily.CommonLaw;
            return LawFamily.CustomaryLaw;
        }

        /// <summary>⑫ 政治：四来源按 EraIndex 权重合成；制度项仅来自法律层。</summary>
        private static void StepPolitics(WorldState world, CivilizationState civ, int month)
        {
            GetLegitimacyWeights(world.EraIndex, out double wp, out double wc, out double wl, out double wi);
            foreach (var p in Sorted(civ.Polities, x => x.stableId))
            {
                if (p.LegitimacySources == null) p.LegitimacySources = new LegitimacySource();
                double prosperity = AverageProsperity(civ, p.stableId);
                double foodRatio = AverageFoodRatio(civ, p.stableId);
                p.LegitimacySources.Performance = Q(Clamp01(0.5 * prosperity + 0.5 * foodRatio));
                p.LegitimacySources.Consensus = Q(Clamp01(ConsensusFromGovernance(p.governance)));
                p.LegitimacySources.Lineage = Q(Clamp01(LineageFromGovernance(p.governance)));
                // institution ← lawStage + impartiality（唯一制度合法性来源）
                p.LegitimacySources.Institution = Q(Clamp01(0.5 * (p.lawStage / 5.0) + 0.5 * p.Impartiality));

                p.legitimacy = Q(Clamp01(
                    p.LegitimacySources.Performance * wp
                    + p.LegitimacySources.Consensus * wc
                    + p.LegitimacySources.Lineage * wl
                    + p.LegitimacySources.Institution * wi));

                double ethPenalty = p.Ethnicity?.EthnicInequality ?? 0;
                p.stability = Q(Clamp01((p.stability + p.legitimacy) * 0.5 - ethPenalty * 0.1
                    - (p.Military?.Weariness ?? 0) * 0.05));

                // MVP：不演进九治理形态，仅保持 lawStage 门槛的粗映射
                if (p.governance != GovernanceType.CustomaryCouncil)
                    p.governance = p.lawStage >= 3 ? GovernanceType.Kingdom : GovernanceType.Chiefdom;

                if (p.legitimacy < 0.3 && p.stability < 0.4)
                {
                    world.Events.Add(new SimEvent(month, SimEventCategory.Civ, p.stableId,
                        "civ.polity.turnover", p.legitimacy));
                    p.stability = Q(Math.Min(1, p.stability + 0.15));
                    p.legitimacy = Q(Math.Min(1, p.legitimacy + 0.1));
                }
            }

            // 沙盒：EraIndex 进入 EarlyModern(≥1) 后锁定 LawFamily
            if (world.EraIndex >= 1)
            {
                foreach (var p in Sorted(civ.Polities, x => x.stableId))
                {
                    if (p.LawFamilyLocked) continue;
                    if (p.lawFamily == LawFamily.ReligiousLaw)
                        p.lawFamily = LawFamily.CustomaryLaw;
                    p.LawFamilyLocked = true;
                }
            }
        }

        private static void GetLegitimacyWeights(int eraIndex,
            out double wp, out double wc, out double wl, out double wi)
        {
            int era = eraIndex < 0 ? 0 : (eraIndex > 4 ? 4 : eraIndex);
            switch (era)
            {
                case 0: wp = 0.60; wc = 0.40; wl = 0.00; wi = 0.00; break; // 远古
                case 1: wp = 0.40; wc = 0.20; wl = 0.40; wi = 0.00; break; // 古代
                case 2: wp = 0.30; wc = 0.10; wl = 0.40; wi = 0.20; break; // 中古
                case 3: wp = 0.30; wc = 0.20; wl = 0.20; wi = 0.30; break; // 近代
                default: wp = 0.30; wc = 0.35; wl = 0.00; wi = 0.35; break; // 现代
            }
        }

        private static double ConsensusFromGovernance(GovernanceType g)
        {
            switch (g)
            {
                case GovernanceType.CustomaryCouncil: return 0.85;
                case GovernanceType.Chiefdom: return 0.55;
                case GovernanceType.CityState: return 0.70;
                default: return 0.40; // Kingdom
            }
        }

        private static double LineageFromGovernance(GovernanceType g)
        {
            switch (g)
            {
                case GovernanceType.Kingdom: return 0.80;
                case GovernanceType.Chiefdom: return 0.55;
                case GovernanceType.CityState: return 0.35;
                default: return 0.15; // CustomaryCouncil
            }
        }

        private static double AverageProsperity(CivilizationState civ, int polityId)
        {
            double sum = 0; int n = 0;
            foreach (var s in civ.Settlements)
                if (s.polityId == polityId) { sum += s.prosperity; n++; }
            return n == 0 ? 0.5 : sum / n;
        }

        private static double AverageFoodRatio(CivilizationState civ, int polityId)
        {
            double sum = 0; int n = 0;
            foreach (var e in civ.Economies)
            {
                var s = SettlementFor(civ, e.settlementId);
                if (s == null || s.polityId != polityId) continue;
                sum += Clamp01(e.food / 40.0);
                n++;
            }
            return n == 0 ? 0.5 : sum / n;
        }

        /// <summary>⑬ 军事：基线增长 + ≥2 Polity 简式 ratio 自动开战/结算；无开战指令 API。</summary>
        private static void StepMilitary(WorldState world, CivilizationState civ, int month)
        {
            foreach (var p in Sorted(civ.Polities, x => x.stableId))
            {
                if (p.Military == null) p.Military = new MilitaryState();
                p.militaryPower = Q(Math.Max(0, p.militaryPower + 0.1));
                if (p.Military.Status == WarStatus.Recovering)
                {
                    p.Military.Weariness = Q(Math.Max(0, p.Military.Weariness - 0.05));
                    if (p.Military.Weariness <= 0.05)
                    {
                        p.Military.Status = WarStatus.Idle;
                        p.Military.OpponentPolityId = 0;
                        p.Military.Weariness = 0;
                    }
                }
            }

            var polities = Sorted(civ.Polities, x => x.stableId);
            if (polities.Count < 2) return;

            // 简式：按稳定 ID 序两两配对自动开战/结算
            for (int i = 0; i + 1 < polities.Count; i += 2)
            {
                var a = polities[i];
                var b = polities[i + 1];
                if (a.Military == null) a.Military = new MilitaryState();
                if (b.Military == null) b.Military = new MilitaryState();

                if (a.Military.Status == WarStatus.Idle && b.Military.Status == WarStatus.Idle)
                {
                    a.Military.Status = WarStatus.AtWar;
                    b.Military.Status = WarStatus.AtWar;
                    a.Military.OpponentPolityId = b.stableId;
                    b.Military.OpponentPolityId = a.stableId;
                    world.Events.Add(new SimEvent(month, SimEventCategory.War, a.stableId,
                        "civ.war.declared", b.stableId));
                }

                if (a.Military.Status != WarStatus.AtWar || b.Military.Status != WarStatus.AtWar)
                    continue;

                double powerA = Math.Max(1e-6, a.militaryPower);
                double powerB = Math.Max(1e-6, b.militaryPower);
                double ratio = powerA / powerB;
                const double winThreshold = 1.5;
                double casualty = Clamp01(Math.Abs(powerA - powerB) / Math.Max(powerA, powerB));
                a.militaryPower = Q(Math.Max(0, a.militaryPower * (1 - casualty * 0.2)));
                b.militaryPower = Q(Math.Max(0, b.militaryPower * (1 - casualty * 0.2)));
                a.Military.Weariness = Q(Clamp01(a.Military.Weariness + 0.08));
                b.Military.Weariness = Q(Clamp01(b.Military.Weariness + 0.08));

                if (ratio > winThreshold || ratio < 1.0 / winThreshold)
                {
                    int winnerId = ratio > winThreshold ? a.stableId : b.stableId;
                    world.Events.Add(new SimEvent(month, SimEventCategory.War, winnerId,
                        "civ.war.resolved", ratio));
                    a.Military.Status = WarStatus.Recovering;
                    b.Military.Status = WarStatus.Recovering;
                    a.stability = Q(Clamp01(a.stability - a.Military.Weariness * 0.1));
                    b.stability = Q(Clamp01(b.stability - b.Military.Weariness * 0.1));
                }
            }
        }

        private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
        private static void StepEra(WorldState world, CivilizationState civ, int month)
        {
            foreach (var p in civ.Polities)
            {
                p.capacityUtilization = Q(p.population / Math.Max(1, TotalCapacity(civ, p.stableId)));
                if (p.techTier >= 2 && p.sustainedSurplusMonths >= 3 && p.capacityUtilization >= 0.2 && world.EraIndex == 0)
                {
                    world.EraIndex++;
                    world.Events.Add(new SimEvent(month, SimEventCategory.Era, p.stableId, "civ.era.transition", p.capacityUtilization));
                }
            }
        }
        private static void AggregatePolities(CivilizationState civ)
        {
            foreach (var p in Sorted(civ.Polities, x => x.stableId))
            {
                double pop = 0, output = 0; int count = 0;
                foreach (var s in civ.Settlements) if (s.polityId == p.stableId) { pop += s.population; count++; }
                foreach (var e in civ.Economies) { var s = SettlementFor(civ, e.settlementId); if (s != null && s.polityId == p.stableId) output += e.food + e.wood; }
                p.population = Q(pop); p.output = Q(output); p.aggregationCost = Q(count * 1.0);
                p.scaleTier = count >= 8 ? ScaleTier.Continental : count >= 3 ? ScaleTier.Regional : ScaleTier.Local;
                p.titleTier = p.techTier >= 4 ? TitleTier.King : TitleTier.Chief;
            }
        }
        private static void EmitEvents(WorldState world, CivilizationState civ, int month)
        {
            foreach (var p in civ.Polities)
                if (p.stability < 0.3) world.Events.Add(new SimEvent(month, SimEventCategory.Civ, p.stableId, "civ.stability.warning", p.stability));
        }

        public static double CarryingCapacity(CivilizationSettlementState s) => Math.Min(s.housingCapacity, Math.Min(s.foodCapacity, s.spaceCapacity));
        private static double TotalCapacity(CivilizationState c, int polityId) { double n = 0; foreach (var s in c.Settlements) if (s.polityId == polityId) n += CarryingCapacity(s); return n; }
        private static CivilizationEconomyState EconomyFor(CivilizationState c, int id) { foreach (var x in c.Economies) if (x.settlementId == id) return x; return null; }
        private static CivilizationPolityState PolityFor(CivilizationState c, int id) { foreach (var x in c.Polities) if (x.stableId == id) return x; return null; }
        private static CivilizationSettlementState SettlementFor(CivilizationState c, int id) { foreach (var x in c.Settlements) if (x.stableId == id) return x; return null; }
        private static bool IsModuleEnabled(WorldState world, string key) => world.ModuleToggles.TryGetValue(key, out bool enabled) && enabled;
        private static int GenerationOf(WorldState world, int individualId)
        {
            int generation = 0;
            foreach (var simEvent in world.Events)
                if (simEvent.sourceId == individualId
                    && simEvent.templateId == "civ.generation.milestone"
                    && simEvent.magnitude > generation)
                    generation = (int)simEvent.magnitude;
            return generation;
        }
        private static int NextIndividualStableId(CivilizationState c)
        {
            int max = 0;
            foreach (var individual in c.Individuals)
                if (individual.stableId > max) max = individual.stableId;
            if (max == int.MaxValue) throw new InvalidOperationException("Individual stable ID space exhausted.");
            return max + 1;
        }
        private static List<T> Sorted<T>(List<T> xs, Func<T, int> id) { var a = new List<T>(xs); a.Sort((x, y) => id(x).CompareTo(id(y))); return a; }
        private static double Q(double x) => DeterminismMath.Quantize(x, 3);

        public static CivilizationState CreateMinimalState(IWorldGeography geography = null)
        {
            var c = new CivilizationState();
            int tileId = ResolveDefaultTile(geography);
            c.Settlements.Add(new CivilizationSettlementState { stableId = 1, worldTileId = tileId, polityId = 100, population = 100, housingCapacity = 300, foodCapacity = 250, spaceCapacity = 500, prosperity = .5 });
            c.Polities.Add(new CivilizationPolityState
            {
                stableId = 100, techTier = 1, stability = .5, legitimacy = .4, militaryPower = 1,
                Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified"),
                LegitimacySources = new LegitimacySource(),
                Military = new MilitaryState()
            });
            c.Economies.Add(new CivilizationEconomyState { stableId = 1, settlementId = 1, food = 30, wood = 10 });
            c.Tech.Add(new TechProgressState { stableId = 1, polityId = 100 });
            c.Individuals.Add(new IndividualState { stableId = 1, settlementId = 1, alive = true, health = 1 });
            return c;
        }

        private static int ResolveDefaultTile(IWorldGeography geography)
        {
            if (geography == null) return 0;
            var candidates = new[]
            {
                new GeoCoordinate(33, 44), new GeoCoordinate(34, 110),
                new GeoCoordinate(26, 31), new GeoCoordinate(25, 78)
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                var tile = geography.GetTile(candidates[i], MapLodLevel.High);
                if (tile.IsLand && tile.Biome != BiomeType.Ice && tile.Slope < 20) return tile.TileId;
            }
            return geography.GetTile(candidates[0], MapLodLevel.Low).TileId;
        }
    }
}

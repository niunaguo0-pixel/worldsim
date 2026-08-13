namespace WorldSim.Simulation.Civilization
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Civilization;
    using WorldSim.Simulation.Core.Ecology;
    using WorldSim.Simulation.Core.Math;

    /// <summary>S3 固定十六步文明月结；关闭 civilization.v2 时完全不参与 Gate-0 桩路径。</summary>
    public sealed class CivilizationSimEngine : IMonthlyCivilizationSettler
    {
        public static CivilizationSimEngine AttachTo(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Civilization == null || world.Civilization.Settlements.Count == 0)
                world.Civilization = CreateMinimalState();
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
            StepIndividuals(civ);                     // 3
            StepEconomy(civ);                         // 4
            StepSettlements(civ);                     // 5
            StepTechnology(civ);                      // 6
            StepSociety(civ);                         // 7
            StepReligion(civ);                        // 8
            StepCulture(civ);                         // 9
            StepEthnicity(civ);                       // 10
            StepLaw(civ);                             // 11
            StepPolitics(civ);                        // 12
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
        }

        private static void ApplyEcologyModifiers(WorldState world, CivilizationState civ)
        {
            if (world.Ecology == null || world.Ecology.Indicators.Count == 0) return;
            double health = world.Ecology.Indicators[0].currentValue;
            foreach (var e in civ.Economies)
                e.food = Q(Math.Max(0, e.food * (0.5 + health * 0.5)));
        }

        private static void StepIndividuals(CivilizationState civ)
        {
            foreach (var i in Sorted(civ.Individuals, x => x.stableId))
                if (i.alive) { i.ageMonths++; i.health = Q(Math.Max(0, i.health - 0.001)); if (i.health <= 0) i.alive = false; }
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

        private static void StepSettlements(CivilizationState civ)
        {
            foreach (var s in Sorted(civ.Settlements, x => x.stableId))
            {
                var e = EconomyFor(civ, s.stableId);
                double cc = CarryingCapacity(s);
                double growth = e != null && e.foodSurplus > 0 ? 0.015 : -0.01;
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
        private static void StepEthnicity(CivilizationState civ) { } // MVP 单主导族群
        private static void StepLaw(CivilizationState civ)
        {
            foreach (var p in civ.Polities) { p.lawStage = Math.Min(5, p.lawStage + 1); p.lawFamily = LawFamily.CustomaryLaw; }
        }
        private static void StepPolitics(CivilizationState civ)
        {
            foreach (var p in civ.Polities)
            {
                p.legitimacy = Q(Math.Min(1, p.legitimacy + 0.02 + p.lawStage * 0.002));
                p.stability = Q(Math.Min(1, (p.stability + p.legitimacy) * 0.5));
                p.governance = p.lawStage >= 3 ? GovernanceType.Kingdom : GovernanceType.Chiefdom;
            }
        }
        private static void StepMilitary(WorldState world, CivilizationState civ, int month)
        {
            foreach (var p in civ.Polities) p.militaryPower = Q(p.militaryPower + 0.1);
        }
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
        private static List<T> Sorted<T>(List<T> xs, Func<T, int> id) { var a = new List<T>(xs); a.Sort((x, y) => id(x).CompareTo(id(y))); return a; }
        private static double Q(double x) => DeterminismMath.Quantize(x, 3);

        public static CivilizationState CreateMinimalState()
        {
            var c = new CivilizationState();
            c.Settlements.Add(new CivilizationSettlementState { stableId = 1, worldTileId = 200, polityId = 100, population = 100, housingCapacity = 300, foodCapacity = 250, spaceCapacity = 500, prosperity = .5 });
            c.Polities.Add(new CivilizationPolityState { stableId = 100, techTier = 1, stability = .5, legitimacy = .4, militaryPower = 1 });
            c.Economies.Add(new CivilizationEconomyState { stableId = 1, settlementId = 1, food = 30, wood = 10 });
            c.Tech.Add(new TechProgressState { stableId = 1, polityId = 100 });
            c.Individuals.Add(new IndividualState { stableId = 1, settlementId = 1, alive = true, health = 1 });
            return c;
        }
    }
}

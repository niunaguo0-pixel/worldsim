namespace WorldSim.Simulation.Intervention
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.Slice;

    /// <summary>
    /// Epic 1 干预系统 (S1-1~S1-4):
    /// 参数注册红线、pending 延迟、持续衰减、紧急干预 24 月冷却、因果链事件.
    /// </summary>
    public sealed class InterventionSystem : IInterventionTarget, IMonthlyInterventionSettler, IInterventionParameterSource
    {
        public const int EmergencyCooldownMonths = 24;
        public const int DefaultDevBiasDurationMonths = 4; // 3–5 月衰减区间中值

        private static readonly string[] DevBiasAxes =
        {
            "agriculture", "hunt", "defense", "trade",
            "faith", "military", "ethnicity", "culture"
        };

        private static readonly HashSet<string> ForbiddenExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "Era", "legitimacy", "LawStage", "GovernanceType",
            "EthnicComposition", "LawFamily", "InstitutionProfile",
            "era", "EraIndex"
        };

        private readonly Dictionary<string, InterventionParamDef> _defs =
            new Dictionary<string, InterventionParamDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _values =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<PendingIntervention> _pending = new List<PendingIntervention>();
        private readonly List<ActiveInterventionEffect> _active = new List<ActiveInterventionEffect>();
        private readonly List<CausalChainNode> _causal = new List<CausalChainNode>();
        private readonly int[] _emergencyCooldown = new int[3];
        private readonly HashSet<int> _disasterShields = new HashSet<int>();
        private readonly HashSet<string> _appliedLogKeys = new HashSet<string>(StringComparer.Ordinal);
        private int _nextInterventionId = 1;

        public IReadOnlyList<PendingIntervention> Pending => _pending;
        public IReadOnlyList<ActiveInterventionEffect> ActiveEffects => _active;
        public IReadOnlyList<CausalChainNode> CausalChain => _causal;

        public int PendingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _pending.Count; i++)
                    if (!_pending[i].Applied) n++;
                return n;
            }
        }

        public static InterventionSystem AttachToSlice(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var sys = new InterventionSystem();
            sys.RegisterEpic1Catalog(settlementId: 1, speciesId: 10, resourceId: 200);
            world.InterventionSettler = sys;
            return sys;
        }

        /// <summary>S1-1 完整参数目录（切片实体 ID）.</summary>
        public void RegisterEpic1Catalog(int settlementId, int speciesId, int resourceId)
        {
            // S2
            RegisterInterventionParameter("rainfall_" + resourceId, 0.0, -50.0, 50.0);
            RegisterInterventionParameter("temperature_" + resourceId, 0.0, -20.0, 20.0);
            RegisterInterventionParameter("birthRate_" + speciesId, 0.0, -0.5, 0.5);
            RegisterInterventionParameter("population_" + settlementId, 0.0, -1000.0, 1000.0);
            RegisterInterventionParameter("regenRate_" + resourceId, 0.0, -10.0, 10.0);

            // S3 devBias + coeffs
            for (int i = 0; i < DevBiasAxes.Length; i++)
                RegisterInterventionParameter("devBias_" + DevBiasAxes[i] + "_" + settlementId, 0.0, -1.0, 1.0);

            RegisterInterventionParameter("foodReserveCoeff_" + settlementId, 1.0, 0.1, 5.0);
            RegisterInterventionParameter("techUnlockBoost_" + settlementId, 0.0, 0.0, 10.0);
            RegisterInterventionParameter("happinessMod_" + settlementId, 0.0, -1.0, 1.0);

            // 可玩月循环兼容短名（映射到切片 ID）
            RegisterAliasIfMissing("rainfall_0", "rainfall_" + resourceId);
            RegisterAliasIfMissing("temperature_0", "temperature_" + resourceId);
            RegisterAliasIfMissing("birthRate_10", "birthRate_" + speciesId);
            RegisterAliasIfMissing("population_1", "population_" + settlementId);
            RegisterAliasIfMissing("regenRate_200", "regenRate_" + resourceId);
            RegisterAliasIfMissing("devBias_agriculture_1", "devBias_agriculture_" + settlementId);
            RegisterAliasIfMissing("foodReserveCoeff_1", "foodReserveCoeff_" + settlementId);
        }

        private void RegisterAliasIfMissing(string alias, string canonical)
        {
            if (_defs.ContainsKey(alias)) return;
            if (!_defs.TryGetValue(canonical, out var def)) return;
            _defs[alias] = new InterventionParamDef(alias, def.DefaultValue, def.Min, def.Max);
            if (!_values.ContainsKey(alias))
                _values[alias] = _values[canonical];
        }

        public bool CanRegister(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (ForbiddenExact.Contains(key)) return false;
            if (key.StartsWith("Era", StringComparison.Ordinal)) return false;
            if (key.StartsWith("legitimacy", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.StartsWith("LawStage", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.StartsWith("GovernanceType", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.StartsWith("EthnicComposition", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.StartsWith("EthnicNationalism", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.StartsWith("LawFamily", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.StartsWith("Institution", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public void RegisterInterventionParameter(string key, double defaultValue, double min, double max)
        {
            if (!CanRegister(key))
                throw new InvalidOperationException("红线: 拒绝注册派生态参数 " + key);
            if (max < min) throw new ArgumentException("max < min");
            double def = Clamp(defaultValue, min, max);
            _defs[key] = new InterventionParamDef(key, def, min, max);
            if (!_values.ContainsKey(key))
                _values[key] = def;
        }

        public bool IsRegistered(string key) => _defs.ContainsKey(key);

        public double GetParameterValue(string key)
        {
            if (!_values.TryGetValue(key, out double v))
                throw new KeyNotFoundException("未注册参数: " + key);
            return v;
        }

        public bool TryGetParameterValue(string key, out double value)
        {
            return _values.TryGetValue(key, out value);
        }

        public int GetEmergencyCooldownRemaining(EmergencyType type) =>
            _emergencyCooldown[(int)type];

        public bool IsEmergencyAvailable(EmergencyType type) =>
            _emergencyCooldown[(int)type] <= 0;

        public void ApplyIntervention(string key, double delta, int durationMonths)
        {
            ApplyIntervention(key, delta, durationMonths, delayMonths: 0, world: null);
        }

        public void ApplyIntervention(string key, double delta, int durationMonths, int delayMonths, WorldState world)
        {
            if (!_defs.ContainsKey(key))
                throw new KeyNotFoundException("未注册参数: " + key);
            if (delayMonths < 0) throw new ArgumentOutOfRangeException(nameof(delayMonths));

            int duration = durationMonths;
            if (duration <= 0 && key.StartsWith("devBias_", StringComparison.Ordinal))
                duration = DefaultDevBiasDurationMonths;
            duration = Math.Max(0, duration);

            int effective = delayMonths;
            if (world != null)
                effective = world.Time.monthIndex + delayMonths;

            int id = _nextInterventionId++;
            _pending.Add(new PendingIntervention
            {
                EffectiveMonth = effective,
                Key = key,
                Delta = delta,
                DurationMonths = duration,
                Applied = false,
                InterventionId = id
            });

            if (world != null)
            {
                string action = key + ":" + DeterminismMath.Quantize(delta, 3).ToString("G17");
                world.InterventionLog.Add(new InterventionRecord(effective, action));
            }
        }

        /// <summary>S1-3 紧急干预：冷却中抛异常.</summary>
        public void ApplyEmergency(EmergencyType type, WorldState world, int delayMonths = 0)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int idx = (int)type;
            if (_emergencyCooldown[idx] > 0)
                throw new InvalidOperationException("紧急干预冷却中剩余月: " + _emergencyCooldown[idx]);

            int effective = world.Time.monthIndex + Math.Max(0, delayMonths);
            string key = "emergency." + type;
            int id = _nextInterventionId++;
            _pending.Add(new PendingIntervention
            {
                EffectiveMonth = effective,
                Key = key,
                Delta = 1.0,
                DurationMonths = 0,
                Applied = false,
                InterventionId = id
            });
            world.InterventionLog.Add(new InterventionRecord(effective, key));
            _emergencyCooldown[idx] = EmergencyCooldownMonths;
        }

        public void SettleDue(WorldState world, int month)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            // 冷却递减（S4 时钟驱动）
            for (int i = 0; i < _emergencyCooldown.Length; i++)
            {
                if (_emergencyCooldown[i] > 0)
                    _emergencyCooldown[i]--;
            }

            // pending 到期
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.Applied || p.EffectiveMonth != month) continue;
                ApplyPending(world, month, p);
                p.Applied = true;
                _pending[i] = p;
            }

            // 预置 InterventionLog（Gate-0 别名 / 无 pending 覆盖）
            for (int i = 0; i < world.InterventionLog.Count; i++)
            {
                var rec = world.InterventionLog[i];
                if (rec.gameMonth != month) continue;
                string dedupe = month + "|" + i + "|" + rec.action;
                if (!_appliedLogKeys.Add(dedupe)) continue;
                ApplyLogAction(world, month, rec.action, interventionId: 0);
            }

            TickActiveEffects(world, month);
        }

        public bool TryAbsorbDisaster(WorldState world, int settlementStableId, int month)
        {
            if (!_disasterShields.Remove(settlementStableId)) return false;
            world.Events.Add(new SimEvent(month, SimEventCategory.Civ, settlementStableId,
                "intervene.shield.absorb", 1.0));
            RecordCausal(0, month, "emergency.DivineShield", "intervene.shield.absorb", 1.0);
            return true;
        }

        private void ApplyPending(WorldState world, int month, PendingIntervention p)
        {
            if (p.Key.StartsWith("emergency.", StringComparison.Ordinal))
            {
                ApplyEmergencyEffect(world, month, p.Key, p.InterventionId);
                return;
            }

            ApplyEffect(world, month, p.Key, p.Delta, p.DurationMonths, p.InterventionId);
        }

        private void ApplyEmergencyEffect(WorldState world, int month, string key, int interventionId)
        {
            if (key.EndsWith("DivineRain", StringComparison.Ordinal))
            {
                NudgeFood(world, 40.0);
                ClearSpeciesStress(world);
                for (int i = 0; i < world.Settlements.Count; i++)
                {
                    world.Settlements[i].underDisaster = false;
                    world.Settlements[i].disasterMonths = 0;
                }
                Emit(world, month, SimEventCategory.Ecology, 200, "intervene.emergency.rain", 40.0, interventionId, key);
            }
            else if (key.EndsWith("DivineShield", StringComparison.Ordinal))
            {
                if (world.Settlements.Count > 0)
                    _disasterShields.Add(world.Settlements[0].stableId);
                Emit(world, month, SimEventCategory.Civ, 1, "intervene.emergency.shield", 1.0, interventionId, key);
            }
            else if (key.EndsWith("LifeSpring", StringComparison.Ordinal))
            {
                // 恢复约 50% 粮储：按当前量补一半缺口到「翻倍」即 +50% of current
                for (int i = 0; i < world.Resources.Count; i++)
                {
                    if (world.Resources[i].name != "Food") continue;
                    double cur = world.Resources[i].currentAmount;
                    world.Resources[i].currentAmount = DeterminismMath.Quantize(cur * 1.5, 3);
                    Emit(world, month, SimEventCategory.Civ, 200, "intervene.emergency.spring",
                        DeterminismMath.Quantize(cur * 0.5, 3), interventionId, key);
                    break;
                }
            }
        }

        private void ApplyEffect(WorldState world, int month, string key, double delta, int durationMonths, int interventionId)
        {
            var def = _defs[key];
            double next = Clamp(_values[key] + delta, def.Min, def.Max);
            _values[key] = DeterminismMath.Quantize(next, 3);

            if (durationMonths > 0)
            {
                _active.Add(new ActiveInterventionEffect
                {
                    Key = key,
                    AppliedDelta = delta,
                    DecayPerMonth = delta / durationMonths,
                    RemainingMonths = durationMonths,
                    SourceMonth = month,
                    InterventionId = interventionId
                });
            }

            MutateWorldFromKey(world, month, key, delta, interventionId);
        }

        private void TickActiveEffects(WorldState world, int month)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                if (a.RemainingMonths <= 0)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                // 衰减写回参数（向默认靠拢）
                if (_defs.TryGetValue(a.Key, out var def) && _values.ContainsKey(a.Key))
                {
                    double v = _values[a.Key] - a.DecayPerMonth;
                    _values[a.Key] = DeterminismMath.Quantize(Clamp(v, def.Min, def.Max), 3);
                }

                a.RemainingMonths--;
                if (a.RemainingMonths <= 0)
                {
                    world.Events.Add(new SimEvent(month, SimEventCategory.Civ, 0,
                        "intervene.decay." + a.Key, 0.0));
                    _active.RemoveAt(i);
                }
                else
                {
                    _active[i] = a;
                }
            }
        }

        private void ApplyLogAction(WorldState world, int month, string action, int interventionId)
        {
            if (string.IsNullOrEmpty(action)) return;

            if (action.StartsWith("emergency.", StringComparison.Ordinal))
            {
                // 仅当本月无同名 pending 已处理
                if (!PendingCovers(month, action))
                    ApplyEmergencyEffect(world, month, action, interventionId);
                return;
            }

            int colon = action.IndexOf(':');
            if (colon > 0)
            {
                string key = action.Substring(0, colon);
                if (_defs.ContainsKey(key) &&
                    double.TryParse(action.Substring(colon + 1),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                {
                    if (!PendingCovers(month, key))
                        ApplyEffect(world, month, key, d, 0, interventionId);
                    return;
                }
            }

            if (action == "bless.rain" || action.StartsWith("rainfall", StringComparison.Ordinal))
            {
                NudgeFood(world, 15.0);
                ClearSpeciesStress(world);
                Emit(world, month, SimEventCategory.Ecology, 200, "intervene.rain", 15.0, interventionId, action);
            }
            else if (action == "nudge.eco")
            {
                foreach (var sp in world.Species)
                    sp.population = DeterminismMath.Quantize(sp.population + 20.0, 0);
                Emit(world, month, SimEventCategory.Ecology, 10, "intervene.eco", 20.0, interventionId, action);
            }
            else if (action == "shield")
            {
                if (world.Settlements.Count > 0)
                    _disasterShields.Add(world.Settlements[0].stableId);
                Emit(world, month, SimEventCategory.Civ, 1, "intervene.shield", 1.0, interventionId, action);
            }
        }

        private bool PendingCovers(int month, string key)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.EffectiveMonth == month && p.Key == key) return true;
            }
            return false;
        }

        private void MutateWorldFromKey(WorldState world, int month, string key, double delta, int interventionId)
        {
            if (key.StartsWith("rainfall", StringComparison.Ordinal) ||
                key.StartsWith("regenRate", StringComparison.Ordinal))
            {
                double amt = Math.Abs(delta) > 0 ? Math.Abs(delta) : 5.0;
                NudgeFood(world, amt);
                // S1-4: 即时落点 + 渐变响应事件
                Emit(world, month, SimEventCategory.Ecology, 200, "intervene.drop.instant", amt, interventionId, key);
                Emit(world, month, SimEventCategory.Ecology, 200, "intervene." + key, DeterminismMath.Quantize(delta, 3), interventionId, key);
            }
            else if (key.StartsWith("population", StringComparison.Ordinal) ||
                     key.StartsWith("birthRate", StringComparison.Ordinal))
            {
                if (world.Settlements.Count > 0)
                {
                    var s = world.Settlements[0];
                    s.population = DeterminismMath.Quantize(Math.Max(0, s.population + Math.Abs(delta) * 10.0), 0);
                    if (world.Polities.Count > 0)
                        world.Polities[0].population = s.population;
                }
                Emit(world, month, SimEventCategory.Civ, 1, "intervene." + key, DeterminismMath.Quantize(delta, 3), interventionId, key);
            }
            else if (key.StartsWith("devBias", StringComparison.Ordinal) ||
                     key.StartsWith("foodReserve", StringComparison.Ordinal) ||
                     key.StartsWith("techUnlock", StringComparison.Ordinal) ||
                     key.StartsWith("happinessMod", StringComparison.Ordinal))
            {
                if (world.Polities.Count > 0)
                {
                    var p = world.Polities[0];
                    p.aggregateStability = DeterminismMath.Quantize(
                        Math.Min(1.0, p.aggregateStability + 0.05), 3);
                    if (key.StartsWith("techUnlock", StringComparison.Ordinal))
                        p.techTier = Math.Min(8, p.techTier + 1);
                }
                Emit(world, month, SimEventCategory.Civ, 100, "intervene." + key, DeterminismMath.Quantize(delta, 3), interventionId, key);
            }
            else if (key.StartsWith("temperature", StringComparison.Ordinal))
            {
                Emit(world, month, SimEventCategory.Ecology, 0, "intervene." + key, DeterminismMath.Quantize(delta, 3), interventionId, key);
            }
        }

        private void Emit(WorldState world, int month, SimEventCategory cat, int sourceId,
            string templateId, double magnitude, int interventionId, string actionKey)
        {
            world.Events.Add(new SimEvent(month, cat, sourceId, templateId, magnitude));
            RecordCausal(interventionId, month, actionKey, templateId, magnitude);
        }

        private void RecordCausal(int interventionId, int month, string actionKey, string templateId, double magnitude)
        {
            _causal.Add(new CausalChainNode
            {
                InterventionId = interventionId,
                MonthExecuted = month,
                ActionKey = actionKey ?? "",
                EventTemplateId = templateId ?? "",
                Magnitude = magnitude
            });
        }

        private static void NudgeFood(WorldState world, double amount)
        {
            for (int i = 0; i < world.Resources.Count; i++)
            {
                if (world.Resources[i].name == "Food")
                {
                    world.Resources[i].currentAmount = DeterminismMath.Quantize(
                        world.Resources[i].currentAmount + amount, 3);
                    return;
                }
            }
        }

        private static void ClearSpeciesStress(WorldState world)
        {
            for (int i = 0; i < world.Species.Count; i++)
                world.Species[i].stressMonths = 0;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}

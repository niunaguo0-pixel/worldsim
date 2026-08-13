namespace WorldSim.Simulation.Time
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.Random;
    using WorldSim.Simulation.Core.Slice;

    /// <summary>
    /// 双频混合结算编排器 (架构 §3 / S4 §2.2,§2.7,§7.3). 确定性, 引擎无关.
    /// 月级大账 (全量) + 周级子结算 (仅 activeEntities) 按边界时间戳升序合并;
    /// 同刻 week 先 month 后 (铁律 2). 边界由整数月/周序号派生 (R-N1), 禁止 float 累加器减法.
    /// V0-7: 尊重 WorldState.Fallback 三级回退钩子 (默认 None).
    /// </summary>
    public sealed class SimOrchestrator
    {
        private readonly WorldState _world;
        private bool _inSettlementPass;

        public SimOrchestrator(WorldState world) { _world = world; }

        public DeterminismFallback Fallback => _world.Fallback;
        public bool IsInSettlementPass => _inSettlementPass;

        /// <summary>
        /// 连续时钟推进. 速度档只缩放 dtGame, 不改变单个 pass 内容 (R14 前提).
        /// 边界由整数序号派生 => 1×/20× 长程无 drift 分叉 (G0-2 / 契约 §3).
        /// </summary>
        public void Update(float dtReal)
        {
            if (_world.Time.paused) return;
            EnforceFallbackSpeedClamp();
            AdvanceGameTime((double)dtReal * _world.Time.speedMultiplier);
        }

        /// <summary>暂停/继续. UI 仅驱动此旗标, 不持有游戏态.</summary>
        public void SetPaused(bool paused)
        {
            _world.Time.paused = paused;
        }

        public bool IsPaused => _world.Time.paused;

        /// <summary>设置速度档; 受回退1 收窄约束 (去 20×).</summary>
        public void SetSpeedMultiplier(int requested)
        {
            _world.Time.speedMultiplier = _world.Fallback.ClampSpeedMultiplier(requested);
        }

        private void EnforceFallbackSpeedClamp()
        {
            int clamped = _world.Fallback.ClampSpeedMultiplier(_world.Time.speedMultiplier);
            if (clamped != _world.Time.speedMultiplier)
                _world.Time.speedMultiplier = clamped;
        }

        /// <summary>
        /// 尝试登记干预. 回退1+ 对齐月边界; 回退3 步间拒绝输入.
        /// </summary>
        public bool TryEnqueueIntervention(string action, int preferredMonth, out InterventionRecord record)
        {
            if (_world.Fallback.LockstepNoInterstepInput && _inSettlementPass)
            {
                record = default;
                return false;
            }

            int month = preferredMonth;
            if (_world.Fallback.AlignInterventionsToMonthBoundary)
                month = Math.Max(preferredMonth, _world.Time.monthIndex);

            record = new InterventionRecord(month, action ?? "");
            _world.InterventionLog.Add(record);
            return true;
        }

        /// <summary>
        /// 单测专用: 模拟处于结算 pass 内时的干预受理 (验证回退3 步间拒收).
        /// </summary>
        public bool TryEnqueueInterventionAsIfInPass(string action, int preferredMonth, out InterventionRecord record)
        {
            bool prev = _inSettlementPass;
            _inSettlementPass = true;
            try { return TryEnqueueIntervention(action, preferredMonth, out record); }
            finally { _inSettlementPass = prev; }
        }

        /// <summary>
        /// 按游戏时间推进 (double, 无 float 乘速误差). Gate-0 / 测试台用此保证不跨周漏采.
        /// </summary>
        public void AdvanceGameTime(double dtGame)
        {
            if (_world.Time.paused) return;
            if (dtGame <= 0) return;
            EnforceFallbackSpeedClamp();
            ref var td = ref _world.Time;
            double target = td.gameClock + dtGame;

            while (true)
            {
                double nextMonth = (td.monthIndex + 1) * TimeDriver.MONTH_SECONDS;
                double nextWeek = (td.weekIndex + 1) * TimeDriver.WEEK_SECONDS;
                double next = Math.Min(nextWeek, nextMonth);
                if (next > target) break;

                td.gameClock = next;
                if (next == nextWeek) { RunWeeklySubSettlement(); td.weekIndex++; }
                if (next == nextMonth) { RunMonthlySettlementPass(); td.monthIndex++; }
            }
            td.gameClock = target;
        }

        // ---------- 月级大账: S1(干预) -> S2(生态) -> S3(文明) -> 重算 active (架构 §3.4) ----------

        private void RunMonthlySettlementPass()
        {
            _inSettlementPass = true;
            try
            {
                // 回退2: ForceSerialPass — 当前切片本就串行; 旗标供并行引入后强制关 Job.
                if (_world.Fallback.ForceSerialPass) { /* serial path asserted */ }

                int month = _world.Time.monthIndex;
                ApplyDueInterventions(month);   // S1 干预结算 (桩)
                StepEcology(month);             // S2 生态月结 (桩)
                StepCivilization(month);        // S3 文明月结 (桩)
                RecountActiveEntities();        // 末步重算 activeEntities
            }
            finally
            {
                _inSettlementPass = false;
            }
        }

        // 周级子结算: 仅 activeEntities, 稳定 ID 升序遍历 (铁律 3, §3.5)
        private void RunWeeklySubSettlement()
        {
            foreach (var id in _world.ActiveEntities.SortedStableIds())
            {
                for (int i = 0; i < _world.Settlements.Count; i++)
                {
                    var s = _world.Settlements[i];
                    if (s.stableId == id) WeeklyRefreshSettlement(s);
                }
            }
        }

        private void WeeklyRefreshSettlement(SettlementStub s)
        {
            if (s.isAtWar) { s.warMonths--; if (s.warMonths <= 0) s.isAtWar = false; }
            if (s.underDisaster) { s.disasterMonths--; if (s.disasterMonths <= 0) s.underDisaster = false; }
            if (s.constructionActive) { s.constructionMonths--; if (s.constructionMonths <= 0) s.constructionActive = false; }
        }

        // ---------- S1 干预结算 ----------

        private void ApplyDueInterventions(int month)
        {
            _world.InterventionSettler?.SettleDue(_world, month);
        }

        // ---------- S2 生态月结 (桩) ----------

        private void StepEcology(int month)
        {
            // 双轨接入：默认沿用 Gate-0 V0 桩；正式 S2 由 Ecology 挂钩接管。
            if (_world.ModuleToggles.TryGetValue("ecology.v2", out bool enabled) &&
                enabled && _world.EcologySettler != null)
            {
                _world.EcologySettler.SettleMonth(_world, month);
                return;
            }
            var rng = _world.Rng.GetStream("ecology"); // class 引用, NextU64 就地推进
            foreach (var sp in SortedByStableId(_world.Species))
            {
                ulong r = rng.NextU64();
                double delta = ((double)(r % 13) - 6.0) * 5.0; // 确定性 [-30, +30]
                bool declined = delta < 0.0;
                if (_world.Fallback.UseFixForKeyQuantities)
                {
                    Fix pop = Fix.FromDouble(sp.population);
                    Fix d = Fix.FromDouble(delta);
                    sp.population = DeterminismMath.Quantize(Math.Max(0.0, (pop + d).ToDouble()), 0);
                }
                else
                {
                    sp.population = DeterminismMath.Quantize(Math.Max(0.0, sp.population + delta), 0);
                }

                // 稳态桩: 连续 3 月衰退累计生态压力 → 灾害 (覆盖周级通道)
                if (declined) sp.stressMonths++; else if (sp.stressMonths > 0) sp.stressMonths--;
                if (sp.stressMonths >= 3)
                {
                    bool anyDisaster = false;
                    for (int i = 0; i < _world.Settlements.Count; i++)
                        if (_world.Settlements[i].underDisaster) { anyDisaster = true; break; }
                    if (!anyDisaster && _world.Settlements.Count > 0)
                    {
                        sp.stressMonths = 0;
                        var target = _world.Settlements[0];
                        // S1-3 神佑护盾: 吸收本月灾害
                        if (_world.InterventionSettler != null &&
                            _world.InterventionSettler.TryAbsorbDisaster(_world, target.stableId, month))
                        {
                            // 事件由 settler 写入
                        }
                        else
                        {
                            target.underDisaster = true;
                            target.disasterMonths = 4;
                            _world.Events.Add(new SimEvent(month, SimEventCategory.Disaster, sp.stableId,
                                "ecology.disaster", DeterminismMath.Quantize(sp.population, 0)));
                        }
                    }
                }
            }
        }

        // ---------- S3 文明月结 (桩) ----------

        private void StepCivilization(int month)
        {
            var warRng = _world.Rng.GetStream("war");
            var civRng = _world.Rng.GetStream("civ");

            foreach (var s in SortedByStableId(_world.Settlements))
            {
                ulong wr = warRng.NextU64();
                if (!s.isAtWar && (wr % 100) < 8)
                {
                    s.isAtWar = true;
                    s.warMonths = 6;
                    _world.Events.Add(new SimEvent(month, SimEventCategory.War, s.stableId,
                        "civ.war", DeterminismMath.Quantize(s.population, 0)));
                }
            }

            // S3 v1.4.4: 时代门闩读 TechTier + 持续盈余 + pop/CC + 制度 stub，禁绝对人口
            foreach (var p in SortedByStableId(_world.Polities))
            {
                ulong cr = civRng.NextU64();
                // 切片生长：缓慢抬科技/利用率/盈余，保证 ≥120 月内可跃迁，且不读裸人口阈值
                if ((cr % 5) == 0 && p.techTier < 8)
                    p.techTier++;
                if ((cr % 7) == 0 && p.divisionDepth < 6)
                    p.divisionDepth++;
                if ((cr % 11) == 0 && p.lawStage < 5)
                    p.lawStage++;
                if (!p.hasWriting && p.techTier >= 3 && (cr % 13) == 0)
                    p.hasWriting = true;

                double utilGain = 0.01 + ((double)(cr % 50)) / 5000.0; // ~[0.01, 0.02]
                p.capacityUtilization = DeterminismMath.Quantize(
                    Math.Min(1.0, p.capacityUtilization + utilGain), 3);
                p.sustainedSurplusMonths++;

                double outGain = 0.5 + ((double)(cr % 100)) / 200.0;
                p.aggregateOutput = DeterminismMath.Quantize(p.aggregateOutput + outGain, 0);
                p.aggregateMilitaryPower = DeterminismMath.Quantize(
                    p.aggregateMilitaryPower + ((cr % 3) == 0 ? 1.0 : 0.0), 0);
                p.aggregateStability = DeterminismMath.Quantize(
                    Math.Min(1.0, p.aggregateStability + 0.002), 3);

                // 聚落人口仅作体量参考，绝不参与 EraGate
                foreach (var s in SortedByStableId(_world.Settlements))
                {
                    s.population = DeterminismMath.Quantize(s.population * (1.0 + s.growthRate), 0);
                    p.population = DeterminismMath.Quantize(s.population, 0);
                }

                foreach (var r in SortedByStableId(_world.Resources))
                {
                    r.currentAmount = DeterminismMath.Quantize(r.currentAmount + 0.5, 3);
                }

                if (!EraGate.TryGetNextGate(_world.EraIndex, out var gate))
                    continue;
                if (!EraGate.Meets(p, gate))
                    continue;

                _world.EraIndex++;
                _world.Events.Add(new SimEvent(month, SimEventCategory.Era, p.stableId,
                    "civ.era", DeterminismMath.Quantize(p.capacityUtilization, 3)));
            }
        }

        // ---------- 末步: 重算 activeEntities ----------

        private void RecountActiveEntities()
        {
            _world.ActiveEntities = new StableIdSet();
            for (int i = 0; i < _world.Settlements.Count; i++)
            {
                var s = _world.Settlements[i];
                if (s.isAtWar || s.underDisaster || s.constructionActive)
                    _world.ActiveEntities.Add(s.stableId);
            }
        }

        // ---------- 稳定 ID 序辅助 (铁律 3) ----------

        private static List<T> SortedByStableId<T>(List<T> items) where T : class
        {
            var list = new List<T>(items);
            list.Sort((a, b) => StableIdOf(a).CompareTo(StableIdOf(b)));
            return list;
        }

        private static int StableIdOf<T>(T item)
        {
            if (item is SettlementStub s) return s.stableId;
            if (item is SpeciesStub sp) return sp.stableId;
            if (item is ResourceStub res) return res.stableId;
            if (item is PolityStub p) return p.stableId;
            return 0;
        }
    }
}

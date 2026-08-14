namespace WorldSim.Simulation.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.Random;
    using WorldSim.Simulation.Core.Slice;
    using WorldSim.Simulation.Core.Ecology;
    using WorldSim.Simulation.Core.Civilization;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>
    /// WorldState 全量二进制快照 (ADR-004 选项 1 / V0-4 / Epic 7).
    /// 显式小端自定义 writer; 集合排序后写; 含 RngRegistry 256-bit + InterventionLog + 时钟 + ModuleToggles.
    /// Schema 9：LOD 分区 DynamicOverrides + 生态指标 delta；历史层增量见 HistoryDeltaCodec.
    /// </summary>
    public static class WorldStateSerializer
    {
        public const int SchemaVersion = 9; // Epic 7 SV1: LOD 覆盖分区 + Indicators delta
        public const int Magic = 0x57534D31; // 'WSM1'

        public static byte[] Save(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            using var w = new DeterministicBinaryWriter();
            WriteAll(w, world, SchemaVersion);
            return w.ToArray();
        }

        /// <summary>迁移/回归工具：导出 3–8 格式，验证 Schema 9 的向后读取路径。</summary>
        public static byte[] SaveLegacy(WorldState world, int targetSchemaVersion)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (targetSchemaVersion < 3 || targetSchemaVersion > 8)
                throw new ArgumentOutOfRangeException(nameof(targetSchemaVersion));
            using var w = new DeterministicBinaryWriter();
            WriteAll(w, world, targetSchemaVersion);
            return w.ToArray();
        }

        public static WorldState Load(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            using var r = new DeterministicBinaryReader(data);

            int magic = r.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad WorldState magic: 0x{magic:X8}");
            int ver = r.ReadInt32();
            if (ver < 3 || ver > SchemaVersion)
                throw new InvalidDataException($"Unsupported schemaVersion {ver}, expected 3..{SchemaVersion}");

            ulong seed = r.ReadUInt64();
            var world = new WorldState(seed);

            double gameClock = r.ReadDouble();
            int monthIndex = r.ReadInt32();
            int weekIndex = r.ReadInt32();
            int speed = r.ReadInt32();
            bool paused = r.ReadBool();
            world.Time = new TimeDriver(speed, paused);
            world.Time.gameClock = gameClock;
            world.Time.monthIndex = monthIndex;
            world.Time.weekIndex = weekIndex;

            world.EraIndex = r.ReadInt32();
            world.Fallback.SetLevel((DeterminismFallbackLevel)r.ReadInt32());

            int toggleCount = r.ReadInt32();
            world.ModuleToggles.Clear();
            for (int i = 0; i < toggleCount; i++)
            {
                string k = r.ReadString();
                bool v = r.ReadBool();
                world.ModuleToggles[k] = v;
            }

            world.Settlements = ReadSettlements(r);
            world.Species = ReadSpecies(r);
            world.Polities = ReadPolities(r);
            world.Resources = ReadResources(r);
            if (ver >= 4)
                world.Ecology = ReadEcology(r, ver);
            if (ver >= 5)
                world.Civilization = ReadCivilization(r, ver);
            if (ver >= 6)
                world.Map = ReadWorldMapState(r, ver);

            int eventCount = r.ReadInt32();
            world.Events = new List<SimEvent>(eventCount);
            for (int i = 0; i < eventCount; i++)
            {
                int month = r.ReadInt32();
                var cat = (SimEventCategory)r.ReadByte();
                int src = r.ReadInt32();
                string tid = r.ReadString();
                double mag = r.ReadDouble();
                world.Events.Add(new SimEvent(month, cat, src, tid, mag));
            }

            int activeCount = r.ReadInt32();
            world.ActiveEntities = new StableIdSet();
            for (int i = 0; i < activeCount; i++)
                world.ActiveEntities.Add(r.ReadInt32());

            int ivCount = r.ReadInt32();
            world.InterventionLog = new List<InterventionRecord>(ivCount);
            for (int i = 0; i < ivCount; i++)
            {
                int month = r.ReadInt32();
                string action = r.ReadString();
                world.InterventionLog.Add(new InterventionRecord(month, action));
            }

            int rngLen = r.ReadInt32();
            var rngBytes = new byte[rngLen];
            for (int i = 0; i < rngLen; i++) rngBytes[i] = r.ReadByte();
            world.Rng = new RngRegistry(seed);
            using (var ms = new MemoryStream(rngBytes))
            using (var br = new BinaryReader(ms))
                world.Rng.LoadState(br);

            return world;
        }

        /// <summary>
        /// 月级确定性指标哈希 (契约 §2.3): Quantize 后 FNV-1a-64; 供 Gate-0 / 路径④比对.
        /// 含产出/军力/稳定度/TechTier/资源量；禁绝对人口作时代钥匙（人口仍入哈希作观测指标）.
        /// </summary>
        public static ulong ComputeMonthlyHash(WorldState world)
        {
            using var w = new DeterministicBinaryWriter();

            foreach (var kv in world.Rng.StreamsOrdered())
            {
                w.WriteUInt64(kv.Key);
                var (a, b, c, d) = kv.Value.State256;
                w.WriteUInt64(a); w.WriteUInt64(b); w.WriteUInt64(c); w.WriteUInt64(d);
            }

            foreach (var p in SortedCopy(world.Polities, x => x.stableId))
            {
                w.WriteInt32(p.stableId);
                w.WriteDouble(DeterminismMath.Quantize(p.population, 0));
                w.WriteDouble(DeterminismMath.Quantize(p.aggregateOutput, 0));
                w.WriteDouble(DeterminismMath.Quantize(p.aggregateMilitaryPower, 0));
                w.WriteDouble(DeterminismMath.Quantize(p.aggregateStability, 3));
                w.WriteInt32(p.techTier);
                w.WriteInt32(p.sustainedSurplusMonths);
                w.WriteDouble(DeterminismMath.Quantize(p.capacityUtilization, 3));
                w.WriteInt32(p.divisionDepth);
                w.WriteInt32(p.lawStage);
                w.WriteBool(p.hasWriting);
            }

            foreach (var s in SortedCopy(world.Settlements, x => x.stableId))
            {
                w.WriteInt32(s.stableId);
                w.WriteDouble(DeterminismMath.Quantize(s.population, 0));
                w.WriteDouble(DeterminismMath.Quantize(s.growthRate, 3));
                w.WriteBool(s.isAtWar);
                w.WriteBool(s.underDisaster);
                w.WriteBool(s.constructionActive);
            }

            foreach (var sp in SortedCopy(world.Species, x => x.stableId))
            {
                w.WriteInt32(sp.stableId);
                w.WriteDouble(DeterminismMath.Quantize(sp.population, 0));
                w.WriteInt32(sp.stressMonths);
            }

            foreach (var res in SortedCopy(world.Resources, x => x.stableId))
            {
                w.WriteInt32(res.stableId);
                w.WriteDouble(DeterminismMath.Quantize(res.currentAmount, 3));
            }

            WriteEcologyHash(w, world.Ecology);
            WriteCivilizationHash(w, world.Civilization);
            WriteWorldMapHash(w, world.Map);

            w.WriteInt32(world.Time.monthIndex);
            w.WriteInt32(world.EraIndex);

            return DeterminismMath.DeterminismHash(w.ToArray());
        }

        private static void WriteAll(DeterministicBinaryWriter w, WorldState world, int schemaVersion)
        {
            w.WriteInt32(Magic);
            w.WriteInt32(schemaVersion);
            w.WriteUInt64(world.worldSeed);

            w.WriteDouble(world.Time.gameClock);
            w.WriteInt32(world.Time.monthIndex);
            w.WriteInt32(world.Time.weekIndex);
            w.WriteInt32(world.Time.speedMultiplier);
            w.WriteBool(world.Time.paused);

            w.WriteInt32(world.EraIndex);
            w.WriteInt32((int)world.Fallback.Level);

            var toggleKeys = new List<string>(world.ModuleToggles.Keys);
            toggleKeys.Sort(StringComparer.Ordinal);
            w.WriteInt32(toggleKeys.Count);
            for (int i = 0; i < toggleKeys.Count; i++)
            {
                string k = toggleKeys[i];
                w.WriteString(k);
                w.WriteBool(world.ModuleToggles[k]);
            }

            WriteSettlements(w, SortedCopy(world.Settlements, s => s.stableId));
            WriteSpecies(w, SortedCopy(world.Species, s => s.stableId));
            WritePolities(w, SortedCopy(world.Polities, p => p.stableId));
            WriteResources(w, SortedCopy(world.Resources, r => r.stableId));
            if (schemaVersion >= 4) WriteEcology(w, world.Ecology, schemaVersion);
            if (schemaVersion >= 5) WriteCivilization(w, world.Civilization, schemaVersion);
            if (schemaVersion >= 6) WriteWorldMapState(w, world.Map, schemaVersion);

            var events = new List<SimEvent>(world.Events);
            events.Sort(CompareEvents);
            w.WriteInt32(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                w.WriteInt32(e.gameMonth);
                w.WriteByte((byte)e.category);
                w.WriteInt32(e.sourceId);
                w.WriteString(e.templateId);
                w.WriteDouble(e.magnitude);
            }

            var active = world.ActiveEntities.SortedStableIds();
            w.WriteInt32(active.Count);
            for (int i = 0; i < active.Count; i++) w.WriteInt32(active[i]);

            w.WriteInt32(world.InterventionLog.Count);
            // 命令日志保序（ADR-004）：按追加序写，不为哈希重排因果
            for (int i = 0; i < world.InterventionLog.Count; i++)
            {
                var iv = world.InterventionLog[i];
                w.WriteInt32(iv.gameMonth);
                w.WriteString(iv.action);
            }

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                world.Rng.SaveState(bw);
                bw.Flush();
                byte[] rngBytes = ms.ToArray();
                w.WriteInt32(rngBytes.Length);
                for (int i = 0; i < rngBytes.Length; i++) w.WriteByte(rngBytes[i]);
            }
        }

        private static void WriteSettlements(DeterministicBinaryWriter w, List<SettlementStub> list)
        {
            w.WriteInt32(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                w.WriteInt32(s.stableId);
                w.WriteString(s.name);
                w.WriteDouble(s.population);
                w.WriteDouble(s.growthRate);
                w.WriteBool(s.isAtWar);
                w.WriteBool(s.underDisaster);
                w.WriteBool(s.constructionActive);
                w.WriteInt32(s.warMonths);
                w.WriteInt32(s.disasterMonths);
                w.WriteInt32(s.constructionMonths);
            }
        }

        private static List<SettlementStub> ReadSettlements(DeterministicBinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<SettlementStub>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new SettlementStub
                {
                    stableId = r.ReadInt32(),
                    name = r.ReadString(),
                    population = r.ReadDouble(),
                    growthRate = r.ReadDouble(),
                    isAtWar = r.ReadBool(),
                    underDisaster = r.ReadBool(),
                    constructionActive = r.ReadBool(),
                    warMonths = r.ReadInt32(),
                    disasterMonths = r.ReadInt32(),
                    constructionMonths = r.ReadInt32()
                });
            }
            return list;
        }

        private static void WriteSpecies(DeterministicBinaryWriter w, List<SpeciesStub> list)
        {
            w.WriteInt32(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                w.WriteInt32(s.stableId);
                w.WriteString(s.name);
                w.WriteDouble(s.population);
                w.WriteInt32(s.stressMonths);
            }
        }

        private static List<SpeciesStub> ReadSpecies(DeterministicBinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<SpeciesStub>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new SpeciesStub
                {
                    stableId = r.ReadInt32(),
                    name = r.ReadString(),
                    population = r.ReadDouble(),
                    stressMonths = r.ReadInt32()
                });
            }
            return list;
        }

        private static void WritePolities(DeterministicBinaryWriter w, List<PolityStub> list)
        {
            w.WriteInt32(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                w.WriteInt32(p.stableId);
                w.WriteString(p.name);
                w.WriteDouble(p.population);
                w.WriteDouble(p.aggregateOutput);
                w.WriteDouble(p.aggregateMilitaryPower);
                w.WriteDouble(p.aggregateStability);
                w.WriteInt32(p.techTier);
                w.WriteInt32(p.sustainedSurplusMonths);
                w.WriteDouble(p.capacityUtilization);
                w.WriteInt32(p.divisionDepth);
                w.WriteInt32(p.lawStage);
                w.WriteBool(p.hasWriting);
            }
        }

        private static List<PolityStub> ReadPolities(DeterministicBinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<PolityStub>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new PolityStub
                {
                    stableId = r.ReadInt32(),
                    name = r.ReadString(),
                    population = r.ReadDouble(),
                    aggregateOutput = r.ReadDouble(),
                    aggregateMilitaryPower = r.ReadDouble(),
                    aggregateStability = r.ReadDouble(),
                    techTier = r.ReadInt32(),
                    sustainedSurplusMonths = r.ReadInt32(),
                    capacityUtilization = r.ReadDouble(),
                    divisionDepth = r.ReadInt32(),
                    lawStage = r.ReadInt32(),
                    hasWriting = r.ReadBool()
                });
            }
            return list;
        }

        private static void WriteResources(DeterministicBinaryWriter w, List<ResourceStub> list)
        {
            w.WriteInt32(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var res = list[i];
                w.WriteInt32(res.stableId);
                w.WriteString(res.name);
                w.WriteDouble(res.currentAmount);
            }
        }

        private static List<ResourceStub> ReadResources(DeterministicBinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<ResourceStub>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new ResourceStub
                {
                    stableId = r.ReadInt32(),
                    name = r.ReadString(),
                    currentAmount = r.ReadDouble()
                });
            }
            return list;
        }

        private static void WriteEcology(DeterministicBinaryWriter w, EcologyState eco, int schemaVersion)
        {
            eco = eco ?? new EcologyState();
            w.WriteByte((byte)eco.CurrentSeason);
            w.WriteInt32(eco.LastSettledMonth);
            WriteRegions(w, SortedCopy(eco.Regions, x => x.stableId));
            WriteEcoSpecies(w, SortedCopy(eco.Species, x => x.stableId));
            WriteLinks(w, SortedCopy(eco.FoodChain, x => x.stableId));
            WriteEcoResources(w, SortedCopy(eco.Resources, x => x.stableId));
            WriteIndicators(w, SortedCopy(eco.Indicators, x => x.stableId), schemaVersion);
        }

        private static EcologyState ReadEcology(DeterministicBinaryReader r, int schemaVersion)
        {
            var e = new EcologyState { CurrentSeason = (Season)r.ReadByte(), LastSettledMonth = r.ReadInt32() };
            e.Regions = ReadRegions(r); e.Species = ReadEcoSpecies(r); e.FoodChain = ReadLinks(r);
            e.Resources = ReadEcoResources(r); e.Indicators = ReadIndicators(r, schemaVersion);
            return e;
        }

        private static void WriteRegions(DeterministicBinaryWriter w, List<EcologyRegionState> xs)
        {
            w.WriteInt32(xs.Count); foreach (var x in xs) { w.WriteInt32(x.stableId); w.WriteInt32(x.worldTileId); w.WriteDouble(x.baseRainfall); w.WriteDouble(x.baseTemperature); w.WriteDouble(x.rainfallModifier); w.WriteDouble(x.temperatureModifier); w.WriteDouble(x.terrainEvolution); w.WriteByte((byte)x.terrainPhase); }
        }
        private static List<EcologyRegionState> ReadRegions(DeterministicBinaryReader r)
        {
            int n = r.ReadInt32(); var xs = new List<EcologyRegionState>(n); for (int i = 0; i < n; i++) xs.Add(new EcologyRegionState { stableId = r.ReadInt32(), worldTileId = r.ReadInt32(), baseRainfall = r.ReadDouble(), baseTemperature = r.ReadDouble(), rainfallModifier = r.ReadDouble(), temperatureModifier = r.ReadDouble(), terrainEvolution = r.ReadDouble(), terrainPhase = (PhaseState)r.ReadByte() }); return xs;
        }
        private static void WriteZone(DeterministicBinaryWriter w, HomeostasisZone z)
        {
            w.WriteDouble(z.StableLower); w.WriteDouble(z.StableUpper); w.WriteDouble(z.CriticalLower); w.WriteDouble(z.CriticalUpper); w.WriteDouble(z.EquilibriumPoint); w.WriteDouble(z.SelfRepairRate); w.WriteDouble(z.StressDecayFactor); w.WriteInt32(z.StressDurationLimit);
        }
        private static HomeostasisZone ReadZone(DeterministicBinaryReader r) => new HomeostasisZone { StableLower = r.ReadDouble(), StableUpper = r.ReadDouble(), CriticalLower = r.ReadDouble(), CriticalUpper = r.ReadDouble(), EquilibriumPoint = r.ReadDouble(), SelfRepairRate = r.ReadDouble(), StressDecayFactor = r.ReadDouble(), StressDurationLimit = r.ReadInt32() };
        private static void WriteEcoSpecies(DeterministicBinaryWriter w, List<EcologySpeciesState> xs)
        {
            w.WriteInt32(xs.Count); foreach (var x in xs) { w.WriteInt32(x.stableId); w.WriteInt32(x.regionId); w.WriteString(x.name); w.WriteByte((byte)x.trophicLevel); w.WriteDouble(x.population); w.WriteDouble(x.birthRate); w.WriteDouble(x.deathRate); w.WriteDouble(x.carryingCapacity); w.WriteDouble(x.climateSensitivity); WriteZone(w, x.homeostasis); w.WriteByte((byte)x.zone); w.WriteInt32(x.stressMonths); w.WriteByte((byte)x.phase); }
        }
        private static List<EcologySpeciesState> ReadEcoSpecies(DeterministicBinaryReader r)
        {
            int n = r.ReadInt32(); var xs = new List<EcologySpeciesState>(n); for (int i = 0; i < n; i++) xs.Add(new EcologySpeciesState { stableId = r.ReadInt32(), regionId = r.ReadInt32(), name = r.ReadString(), trophicLevel = (SpeciesTrophicLevel)r.ReadByte(), population = r.ReadDouble(), birthRate = r.ReadDouble(), deathRate = r.ReadDouble(), carryingCapacity = r.ReadDouble(), climateSensitivity = r.ReadDouble(), homeostasis = ReadZone(r), zone = (EcologyZone)r.ReadByte(), stressMonths = r.ReadInt32(), phase = (PhaseState)r.ReadByte() }); return xs;
        }
        private static void WriteLinks(DeterministicBinaryWriter w, List<FoodChainLink> xs) { w.WriteInt32(xs.Count); foreach (var x in xs) { w.WriteInt32(x.stableId); w.WriteInt32(x.predatorId); w.WriteInt32(x.preyId); w.WriteDouble(x.predationRate); w.WriteDouble(x.dependencyRatio); } }
        private static List<FoodChainLink> ReadLinks(DeterministicBinaryReader r) { int n = r.ReadInt32(); var xs = new List<FoodChainLink>(n); for (int i = 0; i < n; i++) xs.Add(new FoodChainLink { stableId = r.ReadInt32(), predatorId = r.ReadInt32(), preyId = r.ReadInt32(), predationRate = r.ReadDouble(), dependencyRatio = r.ReadDouble() }); return xs; }
        private static void WriteEcoResources(DeterministicBinaryWriter w, List<RenewableResourceState> xs) { w.WriteInt32(xs.Count); foreach (var x in xs) { w.WriteInt32(x.stableId); w.WriteInt32(x.regionId); w.WriteByte((byte)x.kind); w.WriteDouble(x.currentAmount); w.WriteDouble(x.maxAmount); w.WriteDouble(x.regenRate); w.WriteDouble(x.harvestRate); WriteZone(w, x.homeostasis); w.WriteByte((byte)x.zone); w.WriteInt32(x.stressMonths); w.WriteByte((byte)x.phase); } }
        private static List<RenewableResourceState> ReadEcoResources(DeterministicBinaryReader r) { int n = r.ReadInt32(); var xs = new List<RenewableResourceState>(n); for (int i = 0; i < n; i++) xs.Add(new RenewableResourceState { stableId = r.ReadInt32(), regionId = r.ReadInt32(), kind = (ResourceKind)r.ReadByte(), currentAmount = r.ReadDouble(), maxAmount = r.ReadDouble(), regenRate = r.ReadDouble(), harvestRate = r.ReadDouble(), homeostasis = ReadZone(r), zone = (EcologyZone)r.ReadByte(), stressMonths = r.ReadInt32(), phase = (PhaseState)r.ReadByte() }); return xs; }
        private static void WriteIndicators(DeterministicBinaryWriter w, List<EcologicalIndicatorState> xs, int schemaVersion)
        {
            w.WriteInt32(xs.Count);
            foreach (var x in xs)
            {
                w.WriteInt32(x.stableId);
                w.WriteInt32(x.regionId);
                w.WriteString(x.code);
                if (schemaVersion >= 9)
                {
                    double delta = DeterminismMath.Quantize(x.currentValue - x.previousValue, 3);
                    w.WriteDouble(delta);
                    w.WriteDouble(x.previousValue);
                }
                else
                {
                    w.WriteDouble(x.currentValue);
                    w.WriteDouble(x.previousValue);
                }
                w.WriteByte((byte)x.zone);
                w.WriteInt32(x.stressMonths);
                w.WriteString(x.warningCode);
            }
        }

        private static List<EcologicalIndicatorState> ReadIndicators(DeterministicBinaryReader r, int schemaVersion)
        {
            int n = r.ReadInt32();
            var xs = new List<EcologicalIndicatorState>(n);
            for (int i = 0; i < n; i++)
            {
                int stableId = r.ReadInt32();
                int regionId = r.ReadInt32();
                string code = r.ReadString();
                double a = r.ReadDouble();
                double previous = r.ReadDouble();
                double current = schemaVersion >= 9 ? previous + a : a;
                xs.Add(new EcologicalIndicatorState
                {
                    stableId = stableId,
                    regionId = regionId,
                    code = code,
                    currentValue = current,
                    previousValue = previous,
                    zone = (EcologyZone)r.ReadByte(),
                    stressMonths = r.ReadInt32(),
                    warningCode = r.ReadString()
                });
            }
            return xs;
        }
        private static void WriteEcologyHash(DeterministicBinaryWriter w, EcologyState eco)
        {
            eco = eco ?? new EcologyState();
            w.WriteByte((byte)eco.CurrentSeason); w.WriteInt32(eco.LastSettledMonth);
            foreach (var s in SortedCopy(eco.Species, x => x.stableId)) { w.WriteInt32(s.stableId); w.WriteDouble(DeterminismMath.Quantize(s.population, 3)); w.WriteByte((byte)s.zone); w.WriteInt32(s.stressMonths); w.WriteByte((byte)s.phase); }
            foreach (var r in SortedCopy(eco.Resources, x => x.stableId)) { w.WriteInt32(r.stableId); w.WriteDouble(DeterminismMath.Quantize(r.currentAmount, 3)); w.WriteByte((byte)r.zone); w.WriteInt32(r.stressMonths); w.WriteByte((byte)r.phase); }
            foreach (var i in SortedCopy(eco.Indicators, x => x.stableId)) { w.WriteInt32(i.stableId); w.WriteDouble(DeterminismMath.Quantize(i.currentValue, 3)); w.WriteByte((byte)i.zone); w.WriteInt32(i.stressMonths); }
        }

        private static void WriteCivilization(DeterministicBinaryWriter w, CivilizationState c, int schemaVersion)
        {
            c = c ?? new CivilizationState(); w.WriteInt32(c.LastSettledMonth); w.WriteDouble(c.EcoImpactCoefficient);
            var ss = SortedCopy(c.Settlements, x => x.stableId); w.WriteInt32(ss.Count);
            foreach (var s in ss) { w.WriteInt32(s.stableId); w.WriteInt32(s.worldTileId); w.WriteInt32(s.polityId); w.WriteDouble(s.population); w.WriteDouble(s.housingCapacity); w.WriteDouble(s.foodCapacity); w.WriteDouble(s.spaceCapacity); w.WriteDouble(s.prosperity); w.WriteByte((byte)s.tier); w.WriteBool(s.agricultureZone); w.WriteBool(s.housingZone); w.WriteBool(s.storageZone); }
            var ps = SortedCopy(c.Polities, x => x.stableId); w.WriteInt32(ps.Count);
            foreach (var p in ps)
            {
                w.WriteInt32(p.stableId); w.WriteInt32(p.techTier); w.WriteInt32(p.sustainedSurplusMonths); w.WriteInt32(p.divisionDepth); w.WriteInt32(p.lawStage);
                w.WriteDouble(p.population); w.WriteDouble(p.output); w.WriteDouble(p.militaryPower); w.WriteDouble(p.stability); w.WriteDouble(p.legitimacy); w.WriteDouble(p.capacityUtilization);
                w.WriteBool(p.hasWriting); w.WriteByte((byte)p.governance); w.WriteByte((byte)p.lawFamily); w.WriteByte((byte)p.titleTier); w.WriteByte((byte)p.scaleTier); w.WriteByte((byte)p.dominionMode); w.WriteDouble(p.aggregationCost);
                if (schemaVersion >= 8) WritePolityS34(w, p);
            }
            var es = SortedCopy(c.Economies, x => x.stableId); w.WriteInt32(es.Count);
            foreach (var e in es) { w.WriteInt32(e.stableId); w.WriteInt32(e.settlementId); w.WriteDouble(e.food); w.WriteDouble(e.wood); w.WriteDouble(e.stone); w.WriteDouble(e.goods); w.WriteDouble(e.energy); w.WriteDouble(e.foodSurplus); w.WriteDouble(e.divisionLevel); w.WriteByte(e.exchangeMode); }
            var ts = SortedCopy(c.Tech, x => x.stableId); w.WriteInt32(ts.Count); foreach (var t in ts) { w.WriteInt32(t.stableId); w.WriteInt32(t.polityId); w.WriteDouble(t.agriculture); w.WriteDouble(t.hunt); w.WriteDouble(t.defense); w.WriteDouble(t.trade); w.WriteDouble(t.faith); w.WriteDouble(t.military); w.WriteDouble(t.culture); }
            var ins = SortedCopy(c.Individuals, x => x.stableId); w.WriteInt32(ins.Count); foreach (var i in ins) { w.WriteInt32(i.stableId); w.WriteInt32(i.settlementId); w.WriteInt32(i.ageMonths); w.WriteDouble(i.health); w.WriteByte(i.occupation); w.WriteBool(i.alive); }
        }
        private static void WritePolityS34(DeterministicBinaryWriter w, CivilizationPolityState p)
        {
            var src = p.LegitimacySources ?? new LegitimacySource();
            w.WriteDouble(src.Performance); w.WriteDouble(src.Consensus); w.WriteDouble(src.Lineage); w.WriteDouble(src.Institution);
            w.WriteDouble(p.Impartiality); w.WriteBool(p.LawFamilyLocked);
            var eth = p.Ethnicity ?? EthnicComposition.CreateSingletonDominant("Band", "Unclassified");
            eth.EnforceMvpFold();
            w.WriteInt32(eth.Groups.Count);
            for (int i = 0; i < eth.Groups.Count; i++)
            {
                var g = eth.Groups[i];
                w.WriteInt32(g.StableId); w.WriteString(g.Name ?? ""); w.WriteString(g.LanguageFamily ?? ""); w.WriteDouble(g.PopulationShare);
            }
            w.WriteDouble(eth.Fractionalization); w.WriteDouble(eth.Polarization); w.WriteDouble(eth.EthnicInequality);
            var mil = p.Military ?? new MilitaryState();
            w.WriteDouble(mil.Weariness); w.WriteByte((byte)mil.Status); w.WriteInt32(mil.OpponentPolityId);
        }
        private static CivilizationState ReadCivilization(DeterministicBinaryReader r, int schemaVersion)
        {
            var c = new CivilizationState { LastSettledMonth = r.ReadInt32(), EcoImpactCoefficient = r.ReadDouble() }; int n = r.ReadInt32();
            for (int i = 0; i < n; i++) c.Settlements.Add(new CivilizationSettlementState { stableId=r.ReadInt32(), worldTileId=r.ReadInt32(), polityId=r.ReadInt32(), population=r.ReadDouble(), housingCapacity=r.ReadDouble(), foodCapacity=r.ReadDouble(), spaceCapacity=r.ReadDouble(), prosperity=r.ReadDouble(), tier=(SettlementTier)r.ReadByte(), agricultureZone=r.ReadBool(), housingZone=r.ReadBool(), storageZone=r.ReadBool() });
            n=r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var p = new CivilizationPolityState
                {
                    stableId=r.ReadInt32(), techTier=r.ReadInt32(), sustainedSurplusMonths=r.ReadInt32(), divisionDepth=r.ReadInt32(), lawStage=r.ReadInt32(),
                    population=r.ReadDouble(), output=r.ReadDouble(), militaryPower=r.ReadDouble(), stability=r.ReadDouble(), legitimacy=r.ReadDouble(), capacityUtilization=r.ReadDouble(),
                    hasWriting=r.ReadBool(), governance=(GovernanceType)r.ReadByte(), lawFamily=(LawFamily)r.ReadByte(), titleTier=(TitleTier)r.ReadByte(),
                    scaleTier=(ScaleTier)r.ReadByte(), dominionMode=(DominionMode)r.ReadByte(), aggregationCost=r.ReadDouble()
                };
                if (schemaVersion >= 8) ReadPolityS34(r, p);
                else EnsurePolityS34Defaults(p);
                c.Polities.Add(p);
            }
            n=r.ReadInt32(); for(int i=0;i<n;i++) c.Economies.Add(new CivilizationEconomyState { stableId=r.ReadInt32(), settlementId=r.ReadInt32(), food=r.ReadDouble(), wood=r.ReadDouble(), stone=r.ReadDouble(), goods=r.ReadDouble(), energy=r.ReadDouble(), foodSurplus=r.ReadDouble(), divisionLevel=r.ReadDouble(), exchangeMode=r.ReadByte() });
            n=r.ReadInt32(); for(int i=0;i<n;i++) c.Tech.Add(new TechProgressState { stableId=r.ReadInt32(), polityId=r.ReadInt32(), agriculture=r.ReadDouble(), hunt=r.ReadDouble(), defense=r.ReadDouble(), trade=r.ReadDouble(), faith=r.ReadDouble(), military=r.ReadDouble(), culture=r.ReadDouble() });
            n=r.ReadInt32(); for(int i=0;i<n;i++) c.Individuals.Add(new IndividualState { stableId=r.ReadInt32(), settlementId=r.ReadInt32(), ageMonths=r.ReadInt32(), health=r.ReadDouble(), occupation=r.ReadByte(), alive=r.ReadBool() });
            return c;
        }
        private static void ReadPolityS34(DeterministicBinaryReader r, CivilizationPolityState p)
        {
            p.LegitimacySources = new LegitimacySource
            {
                Performance = r.ReadDouble(), Consensus = r.ReadDouble(),
                Lineage = r.ReadDouble(), Institution = r.ReadDouble()
            };
            p.Impartiality = r.ReadDouble();
            p.LawFamilyLocked = r.ReadBool();
            int gCount = r.ReadInt32();
            var eth = new EthnicComposition();
            for (int i = 0; i < gCount; i++)
            {
                eth.Groups.Add(new EthnicGroup
                {
                    StableId = r.ReadInt32(), Name = r.ReadString(), LanguageFamily = r.ReadString(),
                    PopulationShare = r.ReadDouble()
                });
            }
            eth.Fractionalization = r.ReadDouble();
            eth.Polarization = r.ReadDouble();
            eth.EthnicInequality = r.ReadDouble();
            eth.EnforceMvpFold();
            p.Ethnicity = eth;
            p.Military = new MilitaryState
            {
                Weariness = r.ReadDouble(),
                Status = (WarStatus)r.ReadByte(),
                OpponentPolityId = r.ReadInt32()
            };
        }
        private static void EnsurePolityS34Defaults(CivilizationPolityState p)
        {
            if (p.LegitimacySources == null) p.LegitimacySources = new LegitimacySource();
            if (p.Ethnicity == null) p.Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified");
            else p.Ethnicity.EnforceMvpFold();
            if (p.Military == null) p.Military = new MilitaryState();
        }
        private static void WriteCivilizationHash(DeterministicBinaryWriter w, CivilizationState c)
        {
            c=c??new CivilizationState(); w.WriteInt32(c.LastSettledMonth); w.WriteDouble(DeterminismMath.Quantize(c.EcoImpactCoefficient,3));
            foreach(var s in SortedCopy(c.Settlements,x=>x.stableId)){w.WriteInt32(s.stableId);w.WriteDouble(DeterminismMath.Quantize(s.population,0));w.WriteByte((byte)s.tier);w.WriteDouble(DeterminismMath.Quantize(s.prosperity,3));}
            foreach(var p in SortedCopy(c.Polities,x=>x.stableId))
            {
                w.WriteInt32(p.stableId);w.WriteDouble(DeterminismMath.Quantize(p.population,0));w.WriteDouble(DeterminismMath.Quantize(p.output,3));w.WriteDouble(DeterminismMath.Quantize(p.stability,3));w.WriteInt32(p.techTier);w.WriteInt32(p.lawStage);w.WriteByte((byte)p.governance);
                w.WriteByte((byte)p.lawFamily); w.WriteBool(p.LawFamilyLocked);
                w.WriteDouble(DeterminismMath.Quantize(p.legitimacy,3)); w.WriteDouble(DeterminismMath.Quantize(p.militaryPower,3));
                var src = p.LegitimacySources ?? new LegitimacySource();
                w.WriteDouble(DeterminismMath.Quantize(src.Performance,3)); w.WriteDouble(DeterminismMath.Quantize(src.Consensus,3));
                w.WriteDouble(DeterminismMath.Quantize(src.Lineage,3)); w.WriteDouble(DeterminismMath.Quantize(src.Institution,3));
                var eth = p.Ethnicity ?? EthnicComposition.CreateSingletonDominant("Band","Unclassified");
                eth.EnforceMvpFold();
                w.WriteInt32(eth.Groups.Count);
                for (int i = 0; i < eth.Groups.Count; i++)
                {
                    w.WriteInt32(eth.Groups[i].StableId); w.WriteString(eth.Groups[i].Name ?? "");
                    w.WriteString(eth.Groups[i].LanguageFamily ?? "");
                    w.WriteDouble(DeterminismMath.Quantize(eth.Groups[i].PopulationShare,3));
                }
                w.WriteDouble(DeterminismMath.Quantize(eth.Fractionalization,3));
                w.WriteDouble(DeterminismMath.Quantize(eth.Polarization,3));
                w.WriteDouble(DeterminismMath.Quantize(eth.EthnicInequality,3));
                var mil = p.Military ?? new MilitaryState();
                w.WriteDouble(DeterminismMath.Quantize(mil.Weariness,3)); w.WriteByte((byte)mil.Status); w.WriteInt32(mil.OpponentPolityId);
            }
        }

        private static void WriteWorldMapState(DeterministicBinaryWriter w, WorldMapState map, int schemaVersion)
        {
            map = map ?? new WorldMapState();
            w.WriteString(map.GeoDataBuild ?? "");
            w.WriteString(map.ConfigKey ?? "");
            w.WriteString(map.ManifestChecksum ?? "");
            var config = map.Config ?? new WorldMapConfigSnapshot();
            w.WriteInt32(config.StartEra);
            w.WriteInt32(config.StartMode);
            w.WriteInt32(config.BorderYear);
            w.WriteBool(config.UseRealBorders);
            // Schema 7+: BorderView 紧跟在 UseRealBorders 之后; Schema 6 不写
            if (schemaVersion >= 7) w.WriteInt32(config.BorderView);
            w.WriteDouble(config.StartRegionCenterLat);
            w.WriteDouble(config.StartRegionCenterLon);
            w.WriteDouble(config.StartRegionRadiusDeg);
            var chunks = new List<WorldMapChunkRef>(map.StaticChunks ?? new List<WorldMapChunkRef>());
            chunks.Sort((a, b) => string.CompareOrdinal(a.ChunkId, b.ChunkId));
            w.WriteInt32(chunks.Count);
            foreach (var chunk in chunks)
            {
                w.WriteString(chunk.ChunkId ?? "");
                w.WriteByte((byte)chunk.Lod);
                w.WriteString(chunk.RelativePath ?? "");
                w.WriteString(chunk.Checksum ?? "");
            }
            if (schemaVersion >= 9)
                LodOverrideCodec.WritePartitioned(w, map.DynamicOverrides);
            else
                LodOverrideCodec.WriteFlat(w, map.DynamicOverrides);
        }

        private static WorldMapState ReadWorldMapState(DeterministicBinaryReader r, int schemaVersion)
        {
            var map = new WorldMapState
            {
                GeoDataBuild = r.ReadString(),
                ConfigKey = r.ReadString(),
                ManifestChecksum = r.ReadString(),
                Config = new WorldMapConfigSnapshot
                {
                    StartEra = r.ReadInt32(),
                    StartMode = r.ReadInt32(),
                    BorderYear = r.ReadInt32(),
                    UseRealBorders = r.ReadBool(),
                    BorderView = schemaVersion >= 7 ? r.ReadInt32() : 0,
                    StartRegionCenterLat = r.ReadDouble(),
                    StartRegionCenterLon = r.ReadDouble(),
                    StartRegionRadiusDeg = r.ReadDouble()
                }
            };
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
                map.StaticChunks.Add(new WorldMapChunkRef
                {
                    ChunkId = r.ReadString(), Lod = (MapLodLevel)r.ReadByte(),
                    RelativePath = r.ReadString(), Checksum = r.ReadString()
                });
            map.DynamicOverrides = schemaVersion >= 9
                ? LodOverrideCodec.ReadPartitioned(r)
                : LodOverrideCodec.ReadFlat(r);
            return map;
        }

        private static void WriteWorldMapHash(DeterministicBinaryWriter w, WorldMapState map)
        {
            map = map ?? new WorldMapState();
            w.WriteString(map.GeoDataBuild ?? "");
            w.WriteString(map.ManifestChecksum ?? "");
            var config = map.Config ?? new WorldMapConfigSnapshot();
            w.WriteInt32(config.StartEra);
            w.WriteInt32(config.StartMode);
            w.WriteInt32(config.BorderYear);
            w.WriteBool(config.UseRealBorders);
            w.WriteInt32(config.BorderView); // Task 4: border view 进入稳定月哈希
            w.WriteDouble(DeterminismMath.Quantize(config.StartRegionCenterLat, 4));
            w.WriteDouble(DeterminismMath.Quantize(config.StartRegionCenterLon, 4));
            w.WriteDouble(DeterminismMath.Quantize(config.StartRegionRadiusDeg, 4));
            var changes = new List<WorldTileOverride>(map.DynamicOverrides ?? new List<WorldTileOverride>());
            changes.Sort((a, b) => a.TileId.CompareTo(b.TileId));
            foreach (var item in changes)
            {
                w.WriteInt32(item.TileId);
                w.WriteBool(item.HasElevation);
                w.WriteDouble(DeterminismMath.Quantize(item.ElevationMeters, 1));
                w.WriteBool(item.HasBiome);
                w.WriteByte((byte)item.Biome);
            }
        }

        private static List<T> SortedCopy<T>(List<T> src, Func<T, int> idOf)
        {
            var list = new List<T>(src);
            list.Sort((a, b) => idOf(a).CompareTo(idOf(b)));
            return list;
        }

        private static int CompareEvents(SimEvent a, SimEvent b)
        {
            int c = a.gameMonth.CompareTo(b.gameMonth);
            if (c != 0) return c;
            c = ((byte)a.category).CompareTo((byte)b.category);
            if (c != 0) return c;
            c = a.sourceId.CompareTo(b.sourceId);
            if (c != 0) return c;
            return string.CompareOrdinal(a.templateId, b.templateId);
        }
    }
}

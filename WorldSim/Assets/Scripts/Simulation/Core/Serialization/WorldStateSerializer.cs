namespace WorldSim.Simulation.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Math;
    using WorldSim.Simulation.Core.Random;
    using WorldSim.Simulation.Core.Slice;

    /// <summary>
    /// WorldState 全量二进制快照 (ADR-004 选项 1 / V0-4).
    /// 显式小端自定义 writer; 集合排序后写; 含 RngRegistry 256-bit + InterventionLog + 时钟 + ModuleToggles.
    /// LOD 分块 / delta 历史层在 Epic 7 补全; 本批切片态全量入档即可支撑 Gate-0 路径④.
    /// </summary>
    public static class WorldStateSerializer
    {
        public const int SchemaVersion = 2;
        public const int Magic = 0x57534D31; // 'WSM1'

        public static byte[] Save(WorldState world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            using var w = new DeterministicBinaryWriter();
            WriteAll(w, world);
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
            if (ver != SchemaVersion)
                throw new InvalidDataException($"Unsupported schemaVersion {ver}, expected {SchemaVersion}");

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
        /// 月级确定性指标哈希 (契约 §2): Quantize 后 FNV-1a-64; 供 Gate-0 / 路径④比对.
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
                w.WriteDouble(DeterminismMath.Quantize(p.development, 3));
            }

            foreach (var s in SortedCopy(world.Settlements, x => x.stableId))
            {
                w.WriteInt32(s.stableId);
                w.WriteDouble(DeterminismMath.Quantize(s.population, 0));
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

            w.WriteInt32(world.Time.monthIndex);
            w.WriteInt32(world.EraIndex);

            return DeterminismMath.DeterminismHash(w.ToArray());
        }

        private static void WriteAll(DeterministicBinaryWriter w, WorldState world)
        {
            w.WriteInt32(Magic);
            w.WriteInt32(SchemaVersion);
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
                w.WriteDouble(p.development);
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
                    development = r.ReadDouble()
                });
            }
            return list;
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

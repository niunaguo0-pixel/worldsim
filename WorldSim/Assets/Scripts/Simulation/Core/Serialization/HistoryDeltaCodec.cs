namespace WorldSim.Simulation.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WorldSim.Simulation.Core;

    /// <summary>
    /// Epic 7 SV1：历史层（编年史事件）增量 delta 编解码，供 autosave 尾部追加。
    /// 主快照仍保留全量 Events（Gate-0 路径④）；本 codec 不改月哈希。
    /// </summary>
    public static class HistoryDeltaCodec
    {
        public const int Magic = 0x4844_4C54; // 'HDLT'
        public const int Version = 1;

        /// <summary>编码 gameMonth &gt;= sinceMonthInclusive 的事件（稳定排序后写）。</summary>
        public static byte[] Encode(IReadOnlyList<SimEvent> events, int sinceMonthInclusive)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var selected = new List<SimEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].gameMonth >= sinceMonthInclusive)
                    selected.Add(events[i]);
            }
            selected.Sort(CompareEvents);

            using var w = new DeterministicBinaryWriter();
            w.WriteInt32(Magic);
            w.WriteInt32(Version);
            w.WriteInt32(sinceMonthInclusive);
            w.WriteInt32(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                var e = selected[i];
                w.WriteInt32(e.gameMonth);
                w.WriteByte((byte)e.category);
                w.WriteInt32(e.sourceId);
                w.WriteString(e.templateId ?? "");
                w.WriteDouble(e.magnitude);
            }
            return w.ToArray();
        }

        /// <summary>将 delta 事件合并进 WorldState.Events（去重：同月/类/源/模板/量级跳过）。</summary>
        public static int Apply(WorldState world, byte[] deltaBytes)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (deltaBytes == null) throw new ArgumentNullException(nameof(deltaBytes));

            using var r = new DeterministicBinaryReader(deltaBytes);
            int magic = r.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad HistoryDelta magic: 0x{magic:X8}");
            int ver = r.ReadInt32();
            if (ver != Version)
                throw new InvalidDataException($"Unsupported HistoryDelta version {ver}");
            r.ReadInt32(); // sinceMonthInclusive
            int count = r.ReadInt32();
            if (world.Events == null)
                world.Events = new List<SimEvent>();

            int added = 0;
            for (int i = 0; i < count; i++)
            {
                var e = new SimEvent(
                    r.ReadInt32(),
                    (SimEventCategory)r.ReadByte(),
                    r.ReadInt32(),
                    r.ReadString(),
                    r.ReadDouble());
                if (ContainsEquivalent(world.Events, e)) continue;
                world.Events.Add(e);
                added++;
            }
            world.Events.Sort(CompareEvents);
            return added;
        }

        private static bool ContainsEquivalent(List<SimEvent> events, SimEvent e)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var x = events[i];
                if (x.gameMonth == e.gameMonth
                    && x.category == e.category
                    && x.sourceId == e.sourceId
                    && string.Equals(x.templateId, e.templateId, StringComparison.Ordinal)
                    && x.magnitude == e.magnitude)
                    return true;
            }
            return false;
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

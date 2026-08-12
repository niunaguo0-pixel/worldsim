namespace WorldSim.Simulation.Core.Random
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using WorldSim.Simulation.Core.Math;

    /// <summary>
    /// RNG 分流入档 (铁律 4 / G0-4 / R-N3). 每子系统持有由 worldSeed 派生的独立流,
    /// 每条流 256-bit 状态随 WorldState 序列化. 纯 System.*.
    /// </summary>
    public sealed class RngRegistry
    {
        private readonly ulong _worldSeed;
        private readonly Dictionary<ulong, Xoshiro256> _streams = new Dictionary<ulong, Xoshiro256>();

        public RngRegistry(ulong worldSeed) { _worldSeed = worldSeed; }

        /// <summary>
        /// 派生并缓存一条流: streamId = Fnv1a64(seedBytesLE + tagUtf8Bytes).
        /// 返回同一 class 实例引用，NextU64 就地推进. 禁用 System.Random.
        /// </summary>
        public Xoshiro256 GetStream(string systemTag)
        {
            ulong id = DeriveStreamId(_worldSeed, systemTag);
            if (!_streams.TryGetValue(id, out var rng))
            {
                rng = new Xoshiro256(id);
                _streams[id] = rng;
            }
            return rng;
        }

        public int StreamCount => _streams.Count;

        /// <summary>按 streamId 升序枚举 (确定性遍历, 铁律 3).</summary>
        public IEnumerable<KeyValuePair<ulong, Xoshiro256>> StreamsOrdered()
        {
            var ids = new List<ulong>(_streams.Keys);
            ids.Sort();
            foreach (var id in ids)
                yield return new KeyValuePair<ulong, Xoshiro256>(id, _streams[id]);
        }

        /// <summary>
        /// streamId 派生: FNV-1a-64 over (worldSeed LE 8 字节 + tag UTF-8 字节).
        /// 与契约 §2.3 显式小端一致；tag 用 UTF-8 避免 char→byte 截断非 ASCII.
        /// </summary>
        public static ulong DeriveStreamId(ulong worldSeed, string systemTag)
        {
            byte[] tagBytes = systemTag == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(systemTag);
            var bytes = new byte[8 + tagBytes.Length];
            DeterminismMath.WriteUInt64LE(bytes, 0, worldSeed);
            if (tagBytes.Length > 0)
                Buffer.BlockCopy(tagBytes, 0, bytes, 8, tagBytes.Length);
            return DeterminismMath.Fnv1a64(bytes);
        }

        /// <summary>
        /// 序列化全部流 256-bit 状态 (按 streamId 升序). V0-2 可用；V0-4 纳入全量快照.
        /// </summary>
        public void SaveState(BinaryWriter w)
        {
            var ids = new List<ulong>(_streams.Keys);
            ids.Sort();
            w.Write(ids.Count);
            foreach (var id in ids)
            {
                w.Write(id);
                var (a, b, c, d) = _streams[id].State256;
                w.Write(a); w.Write(b); w.Write(c); w.Write(d);
            }
        }

        /// <summary>读档恢复全部流 (与 SaveState 对称).</summary>
        public void LoadState(BinaryReader r)
        {
            _streams.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                ulong id = r.ReadUInt64();
                var a = r.ReadUInt64(); var b = r.ReadUInt64(); var c = r.ReadUInt64(); var d = r.ReadUInt64();
                var rng = new Xoshiro256(0);
                rng.Restore(a, b, c, d);
                _streams[id] = rng;
            }
        }
    }
}

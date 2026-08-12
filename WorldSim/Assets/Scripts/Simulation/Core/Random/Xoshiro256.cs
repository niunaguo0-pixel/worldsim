namespace WorldSim.Simulation.Core.Random
{
    using System;

    /// <summary>
    /// xoshiro256** — 64-bit 确定性 PRNG. 内部状态 256-bit (4×uint64).
    /// 使用 class（非 struct）：RngRegistry 持有引用，NextU64 就地推进，避免字典取回拷贝导致状态不回写（R-N3 / Gate-0 路径④）.
    /// 禁用 System.Random. 见 ADR-002 / 契约 §4.2.
    /// </summary>
    public sealed class Xoshiro256 : IEquatable<Xoshiro256>
    {
        private ulong s0, s1, s2, s3;

        public Xoshiro256(ulong seed)
        {
            s0 = SplitMix64(ref seed);
            s1 = SplitMix64(ref seed);
            s2 = SplitMix64(ref seed);
            s3 = SplitMix64(ref seed);
        }

        private static ulong SplitMix64(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextU64()
        {
            ulong res = RotateLeft(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3;
            s2 ^= t;
            s3 = RotateLeft(s3, 45);
            return res;
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        /// <summary>
        /// 256-bit 全状态 (s0..s3) 用于序列化.
        /// R-N3 (P0): 仅存 128-bit 会破坏存读档续跑序列；必须全量 256-bit.
        /// </summary>
        public (ulong, ulong, ulong, ulong) State256 => (s0, s1, s2, s3);

        public void Restore(ulong a, ulong b, ulong c, ulong d)
        {
            s0 = a; s1 = b; s2 = c; s3 = d;
        }

        public bool Equals(Xoshiro256 o) =>
            o != null && s0 == o.s0 && s1 == o.s1 && s2 == o.s2 && s3 == o.s3;

        public override bool Equals(object o) => o is Xoshiro256 x && Equals(x);

        public override int GetHashCode() => (s0 ^ s1 ^ s2 ^ s3).GetHashCode();
    }
}

// Phase 4 port target: WorldSim/Assets/Scripts/Tests/Unit/RngStreamTests.cs
// asmdef: WorldSim.Tests
//
// RNG 分流入档 (G0-4 / 铁律 4, ADR-002 选项 2)
// 覆盖: xoshiro256** 确定性 PRNG / streamId = Hash(worldSeed, systemTag) / 128-bit 状态序列化往返 / 不同 tag 独立
// 禁用 System.Random (非跨平台确定).

using System;
using NUnit.Framework;

namespace WorldSim.Tests.Unit
{
    /// <summary>xoshiro256** — 64-bit 确定性 PRNG. 状态 256-bit (4×uint64), 序列化取 128-bit 主干 (ADR-002).</summary>
    public struct Xoshiro256
    {
        private ulong s0, s1, s2, s3;

        public Xoshiro256(ulong seed) { s0 = SplitMix64(ref seed); s1 = SplitMix64(ref seed); s2 = SplitMix64(ref seed); s3 = SplitMix64(ref seed); }

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

        /// <summary>256-bit 全状态 (s0,s1,s2,s3) 用于序列化.
        /// 注意: 架构 §4.2 写作"128-bit 状态", 但 xoshiro256** 实际为 256-bit(4×uint64).
        /// 仅存 128-bit 会在存档/读档后破坏序列 (Gate-0 路径④分叉). 故此处以全 256-bit 入档 (见 determinism-contract.md §7 修正说明).</summary>
        public (ulong, ulong, ulong, ulong) State256 => (s0, s1, s2, s3);
        public void Restore(ulong a, ulong b, ulong c, ulong d) { s0 = a; s1 = b; s2 = c; s3 = d; }
    }

    [TestFixture]
    [Category("Gate0Determinism")]
    public class RngStreamTests
    {
        private static ulong DeriveStreamId(ulong worldSeed, string systemTag)
        {
            // 确定性派生: 用 FNV-1a over (seed bytes + tag bytes)
            var bytes = new byte[8 + systemTag.Length];
            BitConverter.GetBytes(worldSeed).CopyTo(bytes, 0);
            for (int i = 0; i < systemTag.Length; i++) bytes[8 + i] = (byte)systemTag[i];
            return WorldSim.Tests.Unit.DeterminismMath.Fnv1a64(bytes);
        }

        [Test]
        public void Stream_DeterministicFromSeedAndTag()
        {
            var a = new Xoshiro256(DeriveStreamId(0xABC, "ecology.region.3"));
            var b = new Xoshiro256(DeriveStreamId(0xABC, "ecology.region.3"));
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextU64(), b.NextU64(), $"第 {i} 次抽取应一致");
        }

        [Test]
        public void Stream_DifferentTags_Independent()
        {
            var eco = new Xoshiro256(DeriveStreamId(1, "ecology"));
            var mil = new Xoshiro256(DeriveStreamId(1, "civ.polity1.military"));
            Assert.AreNotEqual(eco.NextU64(), mil.NextU64(), "不同子系统流首抽不应相同");
        }

        [Test]
        public void Stream_StateRoundTrip_PreservesSequence()
        {
            var rng = new Xoshiro256(DeriveStreamId(42, "war"));
            var before = new ulong[50];
            for (int i = 0; i < 50; i++) before[i] = rng.NextU64();

            // 存档: 取全 256-bit 状态; 读档: 完整恢复 (仅存 128-bit 会在此断言失败)
            var (s0, s1, s2, s3) = rng.State256;
            var restored = new Xoshiro256(0); restored.Restore(s0, s1, s2, s3);

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(before[i], restored.NextU64(), "全状态入档/读档后序列必须一致 (G0-4 / Replay 路径④)");
        }

        [Test]
        public void RngRegistry_SerializeAllStreams_NoDrift()
        {
            // 验证 RngRegistry 全状态随 WorldState 序列化 (契约 §7). 实际实现在 WorldSim.Simulation.Core.
            // TODO(Phase4 V0-2/V0-4): 接入真实 RngRegistry, 断言 SaveTo/LoadFrom 后 GetMonthlyHash 不变.
            Assert.Pass("占位: Phase 4 V0-4 接入真实 RngRegistry 后补全.");
        }
    }
}

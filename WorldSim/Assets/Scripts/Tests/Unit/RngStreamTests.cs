// 引用生产 Xoshiro256 / RngRegistry / DeterminismMath.
// 覆盖: 确定性 / 分流 / 256-bit 往返 / GetStream 就地推进回写 (R-N3).

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using WorldSim.Simulation.Core.Math;
using WorldSim.Simulation.Core.Random;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Gate0Determinism")]
    public class RngStreamTests
    {
        private static ulong DeriveStreamId(ulong worldSeed, string systemTag)
        {
            // 与 RngRegistry.DeriveStreamId 逐位一致
            byte[] tagBytes = systemTag == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(systemTag);
            var bytes = new byte[8 + tagBytes.Length];
            DeterminismMath.WriteUInt64LE(bytes, 0, worldSeed);
            if (tagBytes.Length > 0)
                Buffer.BlockCopy(tagBytes, 0, bytes, 8, tagBytes.Length);
            return DeterminismMath.Fnv1a64(bytes);
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
            for (int i = 0; i < 50; i++) rng.NextU64();

            var (s0, s1, s2, s3) = rng.State256;
            var restored = new Xoshiro256(0);
            restored.Restore(s0, s1, s2, s3);

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(rng.NextU64(), restored.NextU64(), "全状态入档/读档后序列必须一致");
        }

        [Test]
        public void RngRegistry_GetStream_MutatesSharedState()
        {
            // class 引用: 两次 GetStream 拿到同一实例, 推进必须共享
            var reg = new RngRegistry(0xABCDEF);
            var a = reg.GetStream("ecology");
            a.NextU64();
            var b = reg.GetStream("ecology");
            Assert.AreSame(a, b);
            var after = b.NextU64();

            var fresh = new Xoshiro256(DeriveStreamId(0xABCDEF, "ecology"));
            fresh.NextU64();
            Assert.AreEqual(fresh.NextU64(), after, "第二次抽取应接续第一次之后");
        }

        [Test]
        public void RngRegistry_StreamDeterministic_AndStateRoundTrip()
        {
            var reg = new RngRegistry(0xABCDEF);
            var s1 = reg.GetStream("ecology.region.3");
            for (int i = 0; i < 30; i++) s1.NextU64();

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                reg.SaveState(w);

            var loaded = new RngRegistry(0xABCDEF);
            ms.Position = 0;
            using (var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
                loaded.LoadState(r);

            var a = reg.GetStream("ecology.region.3");
            var b = loaded.GetStream("ecology.region.3");
            for (int i = 0; i < 30; i++)
                Assert.AreEqual(a.NextU64(), b.NextU64(), "RngRegistry 全状态往返后序列一致");
        }

        [Test]
        public void RngRegistry_DeriveStreamId_MatchesHelper()
        {
            Assert.AreEqual(
                DeriveStreamId(42, "war"),
                RngRegistry.DeriveStreamId(42, "war"));
        }
    }
}

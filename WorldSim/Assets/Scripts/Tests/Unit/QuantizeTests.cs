// 引用生产 WorldSim.Simulation.Core.Math / Random (单一真相源).
// 覆盖: Quantize / Fix / DeterminismHash (FNV-1a-64, 禁 string.GetHashCode) / 小端写入.

using System;
using NUnit.Framework;
using WorldSim.Simulation.Core.Math;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Gate0Determinism")]
    public class QuantizeTests
    {
        [Test]
        public void Quantize_TruncatesToDecimals_NoAccumulationDrift()
        {
            // 0.125 在 IEEE754 可精确表示；每次写回 Quantize(3) 后不应漂移
            double acc = 0;
            for (int i = 0; i < 1000; i++) acc = DeterminismMath.Quantize(acc + 0.125, 3);
            Assert.AreEqual(125.0, acc, 1e-9);
        }

        [Test]
        public void Quantize_PopulationInteger_NoFraction()
        {
            Assert.AreEqual(12345.0, DeterminismMath.Quantize(12345.6789, 0));
            Assert.AreEqual(0.0, DeterminismMath.Quantize(0.4, 0));
        }

        [Test]
        public void DeterminismHash_SameInput_SameOutput()
        {
            var a = DeterminismMath.Fnv1a64(new byte[] { 1, 2, 3, 4 });
            var b = DeterminismMath.Fnv1a64(new byte[] { 1, 2, 3, 4 });
            Assert.AreEqual(a, b);
        }

        [Test]
        public void DeterminismHash_OrderMatters_StableOrderingRequired()
        {
            var ab = DeterminismMath.Fnv1a64(new byte[] { 1, 2 });
            var ba = DeterminismMath.Fnv1a64(new byte[] { 2, 1 });
            Assert.AreNotEqual(ab, ba);
        }

        [Test]
        public void Fix_AdditiveExact_NoFloatDrift()
        {
            // 0.25 = 1/4 在 Q32.32 精确可表；4 次相加 = 1.0
            var a = Fix.FromDouble(0.25);
            var sum = new Fix { Raw = 0 };
            for (int i = 0; i < 4; i++) sum = sum + a;
            Assert.AreEqual(1.0, sum.ToDouble(), 1e-12);
        }

        [Test]
        public void WriteUInt64LE_ExplicitLittleEndian()
        {
            var buf = new byte[8];
            DeterminismMath.WriteUInt64LE(buf, 0, 0x0102030405060708UL);
            Assert.AreEqual(0x08, buf[0]);
            Assert.AreEqual(0x07, buf[1]);
            Assert.AreEqual(0x01, buf[7]);
        }
    }
}

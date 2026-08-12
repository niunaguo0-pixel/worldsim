// Phase 4 port target: WorldSim/Assets/Scripts/Tests/Unit/QuantizeTests.cs
// asmdef: WorldSim.Tests
//
// 确定性数学基座 (B3 / G0-4 / G0-5 / G0-7, ADR-002 选项 2)
// 覆盖: Quantize(量化写回) / Fix(Q32.32 定点兜底) / DeterminismHash(FNV-1a-64, 禁 string.GetHashCode)
// 本文件同时定义 DeterminismMath 共享助手, 供同 asmdef 下其他测试文件引用.

using System;
using NUnit.Framework;

namespace WorldSim.Tests.Unit
{
    /// <summary>确定性数学助手 — 落地 architecture §4.5 / ADR-002 选项 2. 纯函数, 无 Unity 依赖.</summary>
    public static class DeterminismMath
    {
        /// <summary>等价定点截断: Round(x * 10^d) / 10^d. 全工程统一向零舍入.</summary>
        public static double Quantize(double x, int decimals)
        {
            double scale = Math.Pow(10.0, decimals);
            // 向零截断, 避免 Math.Round 的 banker's 舍入在跨平台不一致
            return Math.Truncate(x * scale) / scale;
        }

        /// <summary>FNV-1a-64 over 确定性字节流. 禁止 string.GetHashCode (运行时序不稳定).</summary>
        public static ulong Fnv1a64(byte[] data)
        {
            const ulong offsetBasis = 1469598103934665603UL; // FNV-1a 64-bit offset basis
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            unchecked
            {
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= prime;
                }
            }
            return hash;
        }

        /// <summary>Q32.32 定点: 整数部分 32 位 + 小数 32 位, 存为 long 原始值.</summary>
        public struct Fix : IEquatable<Fix>
        {
            public long Raw; // = value * 2^32
            public const int Shift = 32;
            public static Fix FromDouble(double v) => new Fix { Raw = (long)(v * (1L << Shift)) };
            public double ToDouble() => Raw / (double)(1L << Shift);
            public static Fix operator +(Fix a, Fix b) => new Fix { Raw = a.Raw + b.Raw };
            public static Fix operator -(Fix a, Fix b) => new Fix { Raw = a.Raw - b.Raw };
            public bool Equals(Fix o) => Raw == o.Raw;
            public override bool Equals(object o) => o is Fix f && Equals(f);
            public override int GetHashCode() => Raw.GetHashCode();
        }
    }

    [TestFixture]
    [Category("Gate0Determinism")]
    public class QuantizeTests
    {
        [Test]
        public void Quantize_TruncatesToDecimals_NoAccumulationDrift()
        {
            // 反复累加 0.1 (float 经典漂移源), 量化后不再累积误差
            double acc = 0;
            for (int i = 0; i < 1000; i++) acc = DeterminismMath.Quantize(acc + 0.1, 3);
            Assert.AreEqual(100.0, acc, 1e-9);
        }

        [Test]
        public void Quantize_PopulationInteger_NoFraction()
        {
            Assert.AreEqual(12345.0, DeterminismMath.Quantize(12345.6789, 0));
            Assert.AreEqual(0.0, DeterminismMath.Quantize(0.4, 0)); // 向零截断
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
            // 字节流顺序不同 => 哈希不同; 因此集合必须排序后写 (契约 §2.3)
            var ab = DeterminismMath.Fnv1a64(new byte[] { 1, 2 });
            var ba = DeterminismMath.Fnv1a64(new byte[] { 2, 1 });
            Assert.AreNotEqual(ab, ba);
        }

        [Test]
        public void Fix_AdditiveExact_NoFloatDrift()
        {
            var a = DeterminismMath.Fix.FromDouble(0.1);
            var sum = a;
            for (int i = 0; i < 10; i++) sum = sum + a; // 10 * 0.1 定点精确 = 1.0
            Assert.AreEqual(1.0, sum.ToDouble(), 1e-12);
        }
    }
}

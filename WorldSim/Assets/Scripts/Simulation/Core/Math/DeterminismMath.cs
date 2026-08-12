namespace WorldSim.Simulation.Core.Math
{
    using System;

    /// <summary>
    /// 确定性数学基座 (ADR-002 选项 2: float + 禁 fast-math + 量化写回 + Fix 兜底).
    /// 纯 System.*, 零 UnityEngine 依赖. 单一真相源对齐 tests/contracts/determinism-contract.md §2.
    /// </summary>
    public static class DeterminismMath
    {
        /// <summary>
        /// 等价定点截断: Truncate(x * 10^d) / 10^d, 向零舍入 (全工程统一).
        /// 用于消除尾差累积 (指标哈希 / 跨月持久化累加量). 见契约 §2.2.
        /// </summary>
        public static double Quantize(double x, int decimals)
        {
            if (decimals < 0) decimals = 0;
            long scale = Pow10(decimals);
            double s = (double)scale;
            return Math.Truncate(x * s) / s;
        }

        // 10^decimals 以整数累积, 避免 Math.Pow 的浮点取整不确定性 (decimals 落在 [0,15] 内精确可表).
        private static long Pow10(int decimals)
        {
            long s = 1L;
            for (int i = 0; i < decimals; i++) s *= 10L;
            return s;
        }

        /// <summary>
        /// FNV-1a-64 over 确定性字节流. 禁止 string.GetHashCode (运行时不稳定的哈希). 见契约 §2.4.
        /// </summary>
        public static ulong Fnv1a64(byte[] data)
        {
            const ulong offsetBasis = 1469598103934665603UL;
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

        /// <summary>确定性指标哈希包装: 对确定性字节流计算 FNV-1a-64 (契约 §2.3 / §2.4).</summary>
        public static ulong DeterminismHash(byte[] buffer) => Fnv1a64(buffer);

        /// <summary>显式小端写入 UInt64（契约 §2.3；不依赖 BitConverter 主机序）.</summary>
        public static void WriteUInt64LE(byte[] dest, int offset, ulong value)
        {
            dest[offset] = (byte)value;
            dest[offset + 1] = (byte)(value >> 8);
            dest[offset + 2] = (byte)(value >> 16);
            dest[offset + 3] = (byte)(value >> 24);
            dest[offset + 4] = (byte)(value >> 32);
            dest[offset + 5] = (byte)(value >> 40);
            dest[offset + 6] = (byte)(value >> 48);
            dest[offset + 7] = (byte)(value >> 56);
        }

        /// <summary>显式小端写入 Int32.</summary>
        public static void WriteInt32LE(byte[] dest, int offset, int value)
        {
            unchecked
            {
                uint u = (uint)value;
                dest[offset] = (byte)u;
                dest[offset + 1] = (byte)(u >> 8);
                dest[offset + 2] = (byte)(u >> 16);
                dest[offset + 3] = (byte)(u >> 24);
            }
        }
    }
}

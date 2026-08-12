namespace WorldSim.Simulation.Core.Math
{
    using System;

    /// <summary>
    /// Q32.32 定点数: 整数部分 32 位 + 小数 32 位, 存为 long 原始值 (Raw = value * 2^32).
    /// 回退 2 / 跨平台 Replay 兜底 (ADR-002 选项 2). 纯 System.*.
    /// </summary>
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

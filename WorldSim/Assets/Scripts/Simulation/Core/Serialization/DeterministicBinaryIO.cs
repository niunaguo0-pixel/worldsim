namespace WorldSim.Simulation.Core.Serialization
{
    using System;
    using System.IO;
    using System.Text;
    using WorldSim.Simulation.Core.Math;

    /// <summary>
    /// 显式小端确定性二进制写入器 (ADR-004 / 契约 §2.3). 禁 BinaryFormatter.
    /// </summary>
    public sealed class DeterministicBinaryWriter : IDisposable
    {
        private readonly MemoryStream _ms;
        private readonly BinaryWriter _w;

        public DeterministicBinaryWriter()
        {
            _ms = new MemoryStream();
            _w = new BinaryWriter(_ms, Encoding.UTF8, leaveOpen: true);
        }

        public void WriteByte(byte v) => _w.Write(v);
        public void WriteInt32(int v) => _w.Write(v);   // BinaryWriter 本机为 LE on PC; 契约要求 LE, PC/Unity 目标为 LE
        public void WriteUInt64(ulong v) => _w.Write(v);
        public void WriteInt64(long v) => _w.Write(v);
        public void WriteDouble(double v) => _w.Write(v);
        public void WriteBool(bool v) => _w.Write(v);

        public void WriteString(string s)
        {
            if (s == null) { WriteInt32(-1); return; }
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            WriteInt32(utf8.Length);
            _w.Write(utf8);
        }

        public byte[] ToArray()
        {
            _w.Flush();
            return _ms.ToArray();
        }

        public void Dispose()
        {
            _w.Dispose();
            _ms.Dispose();
        }
    }

    /// <summary>显式小端确定性二进制读取器 (与 Writer 对称).</summary>
    public sealed class DeterministicBinaryReader : IDisposable
    {
        private readonly MemoryStream _ms;
        private readonly BinaryReader _r;

        public DeterministicBinaryReader(byte[] data)
        {
            _ms = new MemoryStream(data, writable: false);
            _r = new BinaryReader(_ms, Encoding.UTF8, leaveOpen: true);
        }

        public byte ReadByte() => _r.ReadByte();
        public int ReadInt32() => _r.ReadInt32();
        public ulong ReadUInt64() => _r.ReadUInt64();
        public long ReadInt64() => _r.ReadInt64();
        public double ReadDouble() => _r.ReadDouble();
        public bool ReadBool() => _r.ReadBoolean();

        public string ReadString()
        {
            int len = ReadInt32();
            if (len < 0) return null;
            var bytes = _r.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }

        public void Dispose()
        {
            _r.Dispose();
            _ms.Dispose();
        }
    }
}

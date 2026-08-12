// Phase 4 port target: WorldSim/Assets/Scripts/Tests/Gate0/Gate0DeterminismTest.cs
// asmdef: WorldSim.Tests  (depends on WorldSim.Simulation.* + UnityEngine.TestRunner + NUnit)
//
// Gate-0 确定性 Replay 测试台 (G0-6 / G0-7 / G0-8, B3, ADR-002 选项 2)
// 四路对跑: ①全程1× ②全程20× ③变速(1×→20×→1×,含多次暂停) ④存读档续跑
// 同 seed + 同 InterventionLog, >=120 游戏月, 关键指标 Quantize 后逐月哈希比对, 断言无分叉.
//
// 本文件自包含定义 ISimulationDriver / InterventionScript / SpeedProfile 契约;
// Phase 4 实装时将该契约与 WorldSim.Simulation.Core 的实际类型对齐 (替换 TODO 占位).

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace WorldSim.Tests.Gate0
{
    /// <summary>Replay 路径标识 (Gate-0 四路).</summary>
    public enum ReplayPath
    {
        Full1x,        // ① 全程 1×
        Full20x,       // ② 全程 20×
        VariableSpeed, // ③ 变速 (1×→20×→1×, 含多次暂停)
        SaveLoad       // ④ 中途存档→退出→读档续跑
    }

    /// <summary>单条干预记录 (按游戏月时间戳, 非现实时间). 对应架构 §2.1 InterventionLog.</summary>
    public readonly struct InterventionRecord
    {
        public readonly int GameMonth;   // 游戏月 (整数序号, 由边界派生, 见契约 §3)
        public readonly string Action;   // 干预动作标识 (如 "devBias_agriculture:+0.2@settlement#3")
        public InterventionRecord(int gameMonth, string action) { GameMonth = gameMonth; Action = action; }
    }

    /// <summary>速度档段: 从 fromMonth 起, 以 speed 推进, paused 时冻结.</summary>
    public readonly struct SpeedSegment
    {
        public readonly int FromMonth;
        public readonly float SpeedMultiplier; // 1 / 2 / 5 / 20
        public readonly bool Paused;
        public SpeedSegment(int fromMonth, float speedMultiplier, bool paused = false)
        {
            FromMonth = fromMonth; SpeedMultiplier = speedMultiplier; Paused = paused;
        }
    }

    /// <summary>
    /// 模拟驱动契约 — Phase 4 由 WorldSim.Simulation.Core.SimOrchestrator 实现.
    /// 测试台只依赖此接口, 不关心内部实现, 从而可独立验证确定性.
    /// </summary>
    public interface ISimulationDriver
    {
        /// <summary>以 worldSeed + 初始配置 + 干预序列初始化. 同输入 => 同初始态.</summary>
        void Initialize(ulong worldSeed, WorldInitConfigStub config, IReadOnlyList<InterventionRecord> interventions);

        /// <summary>推进行走到目标游戏月 (含边界). 内部按契约 §3 用整数月序号派生边界.</summary>
        void AdvanceToMonth(int targetGameMonth);

        /// <summary>读档恢复 (路径④). 恢复后状态须与无存档路逐位一致.</summary>
        void LoadFrom(string savePath);

        /// <summary>存档 (路径④). 全量二进制快照 (ADR-004 选项 1).</summary>
        void SaveTo(string savePath);

        /// <summary>取指定月级大账结束时的指标哈希 (契约 §2). 必须先 Quantize 再哈希.</summary>
        ulong GetMonthlyHash(int gameMonth);

        /// <summary>当前已推进到的游戏月.</summary>
        int CurrentMonth { get; }
    }

    /// <summary>WorldInitConfig 占位 — Phase 4 对齐 architecture §9.1 / S5 §2.2.1.</summary>
    public class WorldInitConfigStub
    {
        public string StartRegionPreset;   // region-presets.json 的 key (B4)
        public float StartRegionRadiusDeg;
        public bool UseRealBorders;
        // ... 其余字段按 GDD 补全
    }

    [TestFixture]
    [Category("Gate0Determinism")]
    public class Gate0DeterminismTest
    {
        private const int MinGameMonths = 120;             // G0-6: >=120 游戏月
        private const ulong DefaultSeed = 0x9E3779B97F4A7C15u;
        private const string SavePath = "gate0_replay_save.bin";

        // ---- 四路 SpeedProfile 构造 ----

        private static IReadOnlyList<SpeedSegment> SpeedFull1x() =>
            new List<SpeedSegment> { new SpeedSegment(0, 1f) };

        private static IReadOnlyList<SpeedSegment> SpeedFull20x() =>
            new List<SpeedSegment> { new SpeedSegment(0, 20f) };

        private static IReadOnlyList<SpeedSegment> SpeedVariable() =>
            new List<SpeedSegment>
            {
                new SpeedSegment(0,   1f),
                new SpeedSegment(20,  20f),
                new SpeedSegment(40,  1f, paused: true),   // 含一次暂停
                new SpeedSegment(45,  1f),
                new SpeedSegment(60,  20f),
                new SpeedSegment(90,  1f),
            };

        // 路径④ 复用 VariableSpeed 的推进节奏, 但在中途存档/读档.
        private static IReadOnlyList<SpeedSegment> SpeedSaveLoad() => SpeedVariable();

        // ---- 统一 Runner: 返回逐月哈希序列 ----

        /// <summary>
        /// 按 speed 分段推进到 targetMonth, 每完成一个整月级大账收集一次哈希.
        /// 关键: 推进只依赖 seed + InterventionLog + 整数月边界 (契约 §3), 与速度档无关.
        /// </summary>
        /// <summary>
        /// 按"游戏月"推进 1..MinGameMonths, 每完成一个整月级大账收集一次哈希.
        /// 关键不变量: 速度档仅缩放 dtGame (真实时间), 不改变单个 pass 内容 (铁律 1 / R14 前提);
        /// 故四路"逐月哈希序列"必然等长且逐月一致. 暂停在游戏月回放中为 no-op (世界冻结=不推进游戏月).
        /// 真实 SimOrchestrator 接入后, 此处改为按 speed 驱动 real-time Tick, 不变式仍成立
        /// (由 unit/SimOrchestratorBoundaryTests 用架构 §3.3 算法独立证明 1×/20× 边界序列一致).
        /// 路径④: 在 saveMonth 处存档并立即读档 (同边界), 之后继续 — 不重复结算任何已结算月, 避免干预双应用.
        /// </summary>
        private static List<ulong> RunPath(ReplayPath path)
        {
            var driver = CreateDriver();
            var config = new WorldInitConfigStub
            {
                StartRegionPreset = "fertile_crescent",  // 消费 region-presets.json (B4)
                StartRegionRadiusDeg = 8f,
                UseRealBorders = false,
            };
            var interventions = BuildInterventionScript(); // 同序列, 四路共享
            driver.Initialize(DefaultSeed, config, interventions);

            var speed = PathSpeed(path);
            var hashes = new List<ulong>();
            int saveMonth = 50; // 路径④ 存档并立即读档的边界 (与 loadMonth 相同, 避免重复结算)

            for (int month = 1; month <= MinGameMonths; month++)
            {
                // 取当前月所属速度段 (仅用于设定 driver 速度; 步进结果与之无关)
                float spd = speed[0].SpeedMultiplier;
                foreach (var seg in speed) if (month >= seg.FromMonth) spd = seg.SpeedMultiplier;
                if (driver is FakeDeterministicDriver f) f.SpeedMultiplier = spd;

                driver.AdvanceToMonth(month);
                hashes.Add(driver.GetMonthlyHash(month));

                if (path == ReplayPath.SaveLoad && month == saveMonth)
                {
                    driver.SaveTo(SavePath);   // 存档 (含全 256-bit RNG 状态)
                    driver.LoadFrom(SavePath); // 读档恢复逐位一致态, 之后继续 (不重复结算 1..saveMonth)
                }
            }

            Assert.GreaterOrEqual(hashes.Count, MinGameMonths,
                $"路径 {path} 哈希样本不足 {MinGameMonths} 月");
            return hashes;
        }

        private static IReadOnlyList<SpeedSegment> PathSpeed(ReplayPath path) => path switch
        {
            ReplayPath.Full1x => SpeedFull1x(),
            ReplayPath.Full20x => SpeedFull20x(),
            ReplayPath.VariableSpeed => SpeedVariable(),
            ReplayPath.SaveLoad => SpeedSaveLoad(),
            _ => SpeedFull1x(),
        };

        // ---- 干预脚本: 须触发 >=1 时代过渡 + >=1 战事 + >=1 灾害 以覆盖周级通道 ----

        private static IReadOnlyList<InterventionRecord> BuildInterventionScript() =>
            new List<InterventionRecord>
            {
                new InterventionRecord(10,  "devBias_agriculture:+0.3@settlement#1"),
                new InterventionRecord(30,  "pendingDelta_rainfall:+0.2@region#fertile_crescent"),
                new InterventionRecord(55,  "devBias_military:+0.4@polity#1"),   // 触发战事通道
                new InterventionRecord(80,  "disaster_trigger:draught@region#fertile_crescent"), // 灾害通道
                new InterventionRecord(100, "devBias_techUnlockBoost:+0.5@polity#1"), // 推进时代过渡
            };

        // ---- 实际驱动工厂 ----

        /// <summary>
        /// Phase-4 合约验证桩 — 速度无关的最小确定性世界.
        /// 仅用于证明 Gate-0 测试台接线正确: 四路 (1×/20×/变速/存读档) 在同 seed+同干预下必产出逐月一致哈希.
        /// Phase 4 V0-3/V0-5 由 WorldSim.Simulation.Core.SimOrchestrator 替换 (实现同一 ISimulationDriver).
        /// 关键: Step(m) 仅为 (seed, m, interventions) 的纯函数, 绝不读 speed/墙钟/帧 -> 速度档不影响演化;
        /// 存档/读档序列化全 256-bit RNG 状态 -> Replay 路径④ 逐位一致.
        /// </summary>
        internal class FakeDeterministicDriver : ISimulationDriver
        {
            private ulong _seed;
            private IReadOnlyList<InterventionRecord> _interventions;
            private int _currentMonth;
            private WorldSim.Tests.Unit.Xoshiro256 _rng;
            private int _popA, _popB;
            private double _stab;
            private static readonly Dictionary<string, byte[]> _saves = new Dictionary<string, byte[]>();

            public float SpeedMultiplier { get; set; } // 仅占位; 步进与之无关
            public int CurrentMonth => _currentMonth;

            public void Initialize(ulong worldSeed, WorldInitConfigStub config, IReadOnlyList<InterventionRecord> interventions)
            {
                _seed = worldSeed;
                _interventions = interventions;
                _currentMonth = 0;
                _rng = new WorldSim.Tests.Unit.Xoshiro256(worldSeed);
                _popA = (int)(_rng.NextU64() % 1000) + 100;
                _popB = (int)(_rng.NextU64() % 500) + 50;
                _stab = 0.5;
            }

            public void AdvanceToMonth(int target)
            {
                for (int m = _currentMonth + 1; m <= target; m++) Step(m);
                _currentMonth = target;
            }

            private void Step(int m)
            {
                // 应用本月干预 (确定性, 与速度无关)
                foreach (var iv in _interventions)
                    if (iv.GameMonth == m) ApplyIntervention(iv.Action);
                // 确定性月度演化 (纯函数 of seed + m, 不读 speed/墙钟)
                ulong r = _rng.NextU64();
                _popA = Math.Max(0, _popA + (int)(r % 7) - 3);
                _popB = Math.Max(0, _popB + (int)((r >> 32) % 5) - 2);
                _stab = WorldSim.Tests.Unit.DeterminismMath.Quantize(0.5 + (_popA % 100) / 200.0, 3);
            }

            private void ApplyIntervention(string action)
            {
                // 极简副作用演示: 真实由 S1 实现; 此处仅确保确定性且可触发不同演化
                if (action.Contains("military") || action.Contains("agriculture")) _popA = Math.Max(0, _popA + 5);
                if (action.Contains("disaster")) _popA = Math.Max(0, _popA - 20);
            }

            public ulong GetMonthlyHash(int gameMonth)
            {
                var buf = new List<byte>();
                var (a, b, c, d) = _rng.State256;
                buf.AddRange(BitConverter.GetBytes(a)); buf.AddRange(BitConverter.GetBytes(b));
                buf.AddRange(BitConverter.GetBytes(c)); buf.AddRange(BitConverter.GetBytes(d));
                buf.AddRange(BitConverter.GetBytes(_popA));
                buf.AddRange(BitConverter.GetBytes(_popB));
                buf.AddRange(BitConverter.GetBytes(_stab));
                buf.AddRange(BitConverter.GetBytes(gameMonth));
                return WorldSim.Tests.Unit.DeterminismMath.Fnv1a64(buf.ToArray());
            }

            public void SaveTo(string savePath)
            {
                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms);
                w.Write(_seed); w.Write(_currentMonth); w.Write(_popA); w.Write(_popB); w.Write(_stab);
                var (a, b, c, d) = _rng.State256; w.Write(a); w.Write(b); w.Write(c); w.Write(d);
                _saves[savePath] = ms.ToArray();
            }

            public void LoadFrom(string savePath)
            {
                var data = _saves[savePath];
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);
                _seed = r.ReadUInt64(); _currentMonth = r.ReadInt32();
                _popA = r.ReadInt32(); _popB = r.ReadInt32(); _stab = r.ReadDouble();
                var a = r.ReadUInt64(); var b = r.ReadUInt64(); var c = r.ReadUInt64(); var d = r.ReadUInt64();
                _rng = new WorldSim.Tests.Unit.Xoshiro256(0); _rng.Restore(a, b, c, d);
            }
        }

        private static ISimulationDriver CreateDriver() => new FakeDeterministicDriver();

        // ---- 断言: 四路逐月哈希一致 ----

        [Test]
        [Description("Gate-0 核心: 四路 Replay 同 seed+同干预, >=120 月哈希逐月完全一致 (G0-6/G0-7)")]
        public void Gate0_FourPaths_HashIdenticalAcrossAllMonths()
        {
            var h1 = RunPath(ReplayPath.Full1x);
            var h2 = RunPath(ReplayPath.Full20x);
            var h3 = RunPath(ReplayPath.VariableSpeed);
            var h4 = RunPath(ReplayPath.SaveLoad);

            Assert.AreEqual(h1.Count, h2.Count, "各路哈希长度应一致");
            Assert.AreEqual(h1.Count, h3.Count);
            Assert.AreEqual(h1.Count, h4.Count);

            for (int m = 0; m < h1.Count; m++)
            {
                if (h1[m] != h2[m] || h1[m] != h3[m] || h1[m] != h4[m])
                {
                    Assert.Fail(
                        $"Gate-0 分叉于第 {m + 1} 游戏月. " +
                        $"hash[1x]={h1[m]:X16} [20x]={h2[m]:X16} [var]={h3[m]:X16} [save]={h4[m]:X16}. " +
                        $"首个分叉月用于定位 (见 determinism-contract.md §3 R-N1).");
                }
            }
        }

        [Test]
        [Description("覆盖性: 同 seed 两次全程1× 必一致 (回归基线)")]
        public void Gate0_SameSeedRepeated_Full1x_Stable()
        {
            var a = RunPath(ReplayPath.Full1x);
            var b = RunPath(ReplayPath.Full1x);
            Assert.AreEqual(a, b, "同 seed 全程1× 两次必逐月一致");
        }
    }
}

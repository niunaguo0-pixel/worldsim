# WorldSim — 测试框架脚手架（Phase 4 预制作）

> 工程视角测试框架。**Gate-0 是第一垂直切片**：确定性模拟核心 + 连续时间双频结算 + 四路 Replay 哈希逐月一致 + 真实地球 MVP 区域精算（消费 `region-presets.json`）。
> 本目录是 **workspace 层规范与骨架**，Phase 4 实装时整体移植进 Unity 工程的 `WorldSim/Assets/Scripts/Tests/`（asmdef `WorldSim.Tests`，EditMode headless）。
> 所有确定性验收以 [`contracts/determinism-contract.md`](contracts/determinism-contract.md) 为单一真相源。

---

## 1. 目录结构

```
tests/
├── README.md                      # 本文件
├── contracts/
│   └── determinism-contract.md    # 确定性契约：指标哈希集合 / Quantize 精度 / 哈希算法 / 排序规则 / G0↔测试映射
├── gate0/
│   └── Gate0DeterminismTest.cs    # 四路 Replay 测试台（1×/20×/变速/存读档，≥120 游戏月逐月哈希比对）
├── unit/                          # 单元测试骨架（按子系统分组）
│   ├── QuantizeTests.cs
│   ├── RngStreamTests.cs
│   ├── SimOrchestratorBoundaryTests.cs
│   ├── SerializationRoundTripTests.cs
│   ├── StableIdOrderingTests.cs
│   └── RegionPresetConsumptionTests.cs
├── ci/
│   ├── version-pins.json              # Unity + Burst 固定 pin（B2/B8 单一真相源）
│   ├── assert-burst-pinned.ps1        # V0-8：断言 ProjectVersion + manifest 直接依赖 Burst
│   ├── check-sim-asmdef.ps1           # V0-1：asmdef 边界
│   └── asmdef-boundary-check.md       # 模拟核心零 UnityEngine.CoreModule 静态检查说明
└── (CI 工作流见仓库根 .github/workflows/gate0.yml)
```

---

## 2. 运行方式

### 2.1 本地（Unity CLI，headless）
```bash
UNITY="C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Unity.exe"
PROJ="C:/Users/guowang/Desktop/11/WorldSim"

# 仅跑 Gate-0 确定性门禁
"$UNITY" -batchmode -nographics -projectPath "$PROJ" \
  -runTests -testPlatform EditMode -testFilter Gate0Determinism \
  -testResults gate0.xml -logFile gate0.log -quit
```

### 2.2 CI（GitHub Actions，ADR-003 选项 A）
`.github/workflows/gate0.yml`：
1. **pin-versions**（ubuntu）：`version-pins.json` ↔ env；`assert-burst-pinned.ps1`；**`check-sim-asmdef.ps1`（G0-1）**。
2. **gate0**（**self-hosted Windows X64**）：`resolve-unity.ps1` 双重确认 Hub + 注册表 `Installer\Unity 6000.0.81f1` → 全量 `WorldSim.Tests` EditMode（不再用过窄 `-testFilter`）→ 上传 artifact。
3. 哈希分叉或 `total < 30` 则 CI 红。

本地一键：
```powershell
powershell -File tests/ci/run-gate0-local.ps1
```

仅解析 Unity：
```powershell
powershell -File tests/ci/resolve-unity.ps1 -UnityVersion 6000.0.81f1
```

---

## 3. 测试分层

| 层 | 文件 | 覆盖 |
|----|------|------|
| **Gate-0 门禁** | `gate0/Gate0DeterminismTest.cs` | 四路 Replay 逐月哈希一致（G0-6/7/8、B3） |
| **确定性数学基座** | `unit/QuantizeTests.cs`, `unit/RngStreamTests.cs` | Quantize/Fix、RNG 分流 xoshiro256**、确定性哈希（G0-4/5/7、B3、ADR-002） |
| **时间—结算主循环** | `unit/SimOrchestratorBoundaryTests.cs`, `unit/StableIdOrderingTests.cs` | 双频按边界时间戳升序合并、稳定 ID 排序遍历（G0-1/2/3、ADR-001） |
| **序列化** | `unit/SerializationRoundTripTests.cs` | 全量二进制快照往返逐位一致、RNG 状态入档（G0-4、ADR-004） |
| **真实地球** | `unit/RegionPresetConsumptionTests.cs` | 消费 `region-presets.json` + 绝不指定单国家族红线（B4、B5） |
| **架构边界** | `ci/asmdef-boundary-check.md` | 模拟核心零 `UnityEngine.CoreModule`（ADR-001） |

---

## 4. 移植说明（→ Unity 工程）

| workspace 文件 | Unity 工程目标 | 备注 |
|----------------|----------------|------|
| `gate0/*.cs` | `WorldSim/Assets/Scripts/Tests/Gate0/` | asmdef `WorldSim.Tests` 依赖全体 Sim + `UnityEngine.TestRunner` + `NUnit` |
| `unit/*.cs` | `WorldSim/Assets/Scripts/Tests/Unit/` | 同上 |
| `contracts/determinism-contract.md` | 保留 workspace（规范）/ 同时作为 `WorldSim/Assets/Tests/Docs/` | 单一真相源，双份同步 |

> 骨架中的 `ISimulationDriver` / `InterventionScript` / `SpeedProfile` / `DeterminismHash` 等类型在 Gate-0 测试台内自包含定义，移植时与 `WorldSim.Simulation.Core` 的实际类型对齐即可（替换 TODO 占位）。

---

## 5. 关键约束（红线）

- **禁止 `string.GetHashCode` / `Dictionary` 自然迭代序 / `System.Random`** 进入确定性路径（架构 §4 / S4 §7.3）。
- **指标入哈希前必须 `Quantize`**（消除尾差累积，ADR-002 选项 2）。
- **所有集合遍历前按稳定 ID 升序**（铁律 3）。
- **RNG 抽取只在 pass 内、稳定 ID 序下发生**（铁律 4）。
- **表现插值绝不回写逻辑态**（架构 §2.2 / §3.6）。

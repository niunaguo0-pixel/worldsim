# WorldSim — 确定性契约（Determinism Contract）

> **单一真相源**：本文件定义 WorldSim 确定性验证的指标集、量化精度、哈希算法、遍历排序规则，以及 Gate-0 门禁项（G0-1~8）、Phase 4 入口条件（B2/B3/B4）到测试与 CI 的映射。
> 所有 Epic/Story 的确定性相关验收均以此为准。任何 PR 不得弱化本契约（静态检查 + CI 强制）。
> 关联：架构 §4（确定性）/ S4 §7.3（五条铁律 + Gate-0 规范）/ ADR-002（选项 2：float + 禁 fast-math + 量化写回 + `Fix` 兜底）/ ADR-004（选项 1：全量快照 + 命令日志 + LOD 分块 + delta）/ 控制清单（G0 / B2–B4）。

---

## 1. 五条确定性铁律（不可违背）

| # | 铁律 | 落地规则 |
|----|------|---------|
| 1 | 固定步长唯一时间源 | 所有子系统只读 `gameClock` 与 pass 序号；**禁** `Time.deltaTime` / 墙钟 / 帧计数 / 系统时间 |
| 2 | 双频 pass 按边界时间戳升序合并 | 一次 `Update` 跨多个周/月边界时，按游戏时间轴排序依次执行；**同刻 week 先、month 后**（架构 §3.3） |
| 3 | 遍历顺序必须稳定 | `activeEntities` 及所有聚落/政体/物种集合遍历前 `SortedByStableId`；**禁** 依赖字典/哈希集自然序 |
| 4 | RNG 按子系统分流且状态入档 | 每子系统独立流（xoshiro256**，`streamId = Hash(worldSeed, systemTag)`）；**禁** `System.Random`；状态随 `WorldState` 序列化 |
| 5 | 禁不确定并发与浮点漂移 | 并行仅叶子、固定块序归约、禁 fast-math；关键累加量 `Quantize` 写回 |

---

## 2. 指标哈希规范（Gate-0 G0-6/7）

### 2.1 触发时机
每个**月级大账结束**时，对当前 `WorldState` 计算一次 `monthlyHash(month)`。指标在**量化后**进入哈希字节流。

### 2.2 量化精度（Quantize decimals，初值，playtest 校准，见 §5）
| 指标类别 | 字段示例 | 初值 decimals |
|----------|---------|--------------|
| 人口类 | `Polity.population`, `Settlement.population`, `Species.population`, `Individual` 聚合 | 0（整数） |
| 产出/军力类 | `aggregateOutput`, `aggregateMilitaryPower` | 0 |
| 稳定度/比例类 | `aggregateStability`, `happinessMod`, `devBias_*` | 3 |
| 资源类 | `Resource.currentAmount`, `regenRate` | 3 |
| 科技类 | `TechTier` 数值, `knowledgeAccumulated` | 0 / 3 |
| RNG 状态 | `RngRegistry` 每条流 256-bit 状态（4×uint64，R-N3） | 不经 Quantize，原样字节 |

> 量化规则：`Quantize(x, d) = Truncate(x * 10^d) / 10^d`（**向零截断**，全工程统一；此前草稿写作 `Round`，现与 production 对齐为向零，避免 banker's 跨平台不一致，ADR-002 选项 2）。量化在「写回指标哈希」与「跨月持久化累加量」两处执行（ADR-002 选项 2）。

### 2.3 字节流构造（确定性写入，ADR-004 约束）

> 与 `WorldStateSerializer.ComputeMonthlyHash`（SchemaVersion≥3）字段集对齐。实现为单一真相源；改哈希字段须先改本表并过四路 Replay。

```
byte[] BuildDeterministicBuffer(WorldState ws):
    buf = []
    // 1) RNG 全状态（先写，最敏感）
    for stream in ws.RngRegistry.streams.OrderBy(s => s.streamId):   // 稳定 ID 序
        buf += stream.streamId                     // ulong streamId
        buf += stream.state256                     // 32 字节 (4×uint64) 原样, R-N3 必须全 256-bit
    // 2) 政体（稳定 ID 升序）— 含 EraGate v1.4.4 观测字段；人口入哈希作观测，不作时代钥匙
    for p in ws.Polities.OrderBy(p => p.stableId):
        buf += p.stableId
        buf += Quantize(p.population, 0)
        buf += Quantize(p.aggregateOutput, 0)
        buf += Quantize(p.aggregateMilitaryPower, 0)
        buf += Quantize(p.aggregateStability, 3)
        buf += p.techTier                          // int
        buf += p.sustainedSurplusMonths            // int
        buf += Quantize(p.capacityUtilization, 3)
        buf += p.divisionDepth                     // int
        buf += p.lawStage                          // int
        buf += p.hasWriting                        // bool
    // 3) 聚落（稳定 ID 升序）
    for s in ws.Settlements.OrderBy(s => s.stableId):
        buf += s.stableId
        buf += Quantize(s.population, 0)
        buf += Quantize(s.growthRate, 3)
        buf += s.isAtWar / s.underDisaster / s.constructionActive  // bool×3
    // 4) 物种（稳定 ID 升序）
    for sp in ws.Species.OrderBy(sp => sp.stableId):
        buf += sp.stableId
        buf += Quantize(sp.population, 0)
        buf += sp.stressMonths                     // int
    // 5) 资源（稳定 ID 升序）
    for r in ws.Resources.OrderBy(r => r.stableId):
        buf += r.stableId
        buf += Quantize(r.currentAmount, 3)
    // 6) 时钟 + 时代索引（整数月，非 float）
    buf += ws.Time.monthIndex
    buf += ws.EraIndex
    return buf
```
- 所有数值**显式小端、固定字段顺序、固定宽度**；集合**先排序后写**（禁止字典遍历序）。
- `monthIndex` / `EraIndex` 以整数参与哈希（由边界序号派生，见 §3），避免 float 累加器 drift 污染哈希。
- `InterventionLog` 是**命令日志保序序列**（ADR-004），入快照时按追加序写，**不**为哈希排序打乱因果；月哈希不包含干预日志全文（已由 RNG/态间接覆盖）。
- Schema 5 起，`CivilizationState` 以稳定 ID 序参与月哈希：聚落人口/尺度/繁荣度，以及政体人口、产出、稳定度、科技、法律与治理字段；经济、技术、个体全态入档。正式文明必须通过 1×、20×、变速暂停、存读档四路 Replay。
- **Schema 7（Epic 5 Task 4/6）真实地理参与月哈希**：`WorldMapState` 进入 `WriteWorldMapHash`，含 `GeoDataBuild`（lock 派生 buildId，即静态源版本）、`ManifestChecksum`、`WorldMapConfigSnapshot`（`StartEra`/`StartMode`/`BorderYear`/`UseRealBorders`/`BorderView`/起始区域中心与半径，量化 4 位）以及 `DynamicOverrides`（按 `TileId` 升序，量化后入哈希）。`BorderView`（DeFactoControl/SovereigntyClaims）进入稳定月哈希，故双视图切换改变哈希。表现层 `WorldMapChunkCache` 不进哈希（测试断言）。真实地理（水邻增长 ±0.003 / slope / IsLand）通过 `CivilizationSimEngine.StepSettlements` 进入月哈希；存读档后必须 `WorldMapFactory.RebuildGeography` 重建 transient `WorldGeography`，否则 Geography=null 时水邻增长被静默跳过、哈希分叉（反证测试 `Replay_SaveLoad_RebuildGeography_KeepsHashAlignedAndGeographyMatters`）。
- **Schema 8（Epic 3 S3-4）核心层政治/法律/族群/军事**：`CivilizationPolityState` 新增合法性四来源（`LegitimacySource`：performance/consensus/lineage/institution，无宗教项）、`EthnicComposition`（MVP 单主导折叠）、`MilitaryState`（weariness/warStatus/opponent）、`Impartiality`、`LawFamilyLocked`。月哈希纳入上述字段（量化 3 位）；读 Schema ≤7 时确定性默认值回填。`LawFamily.ReligiousLaw` 仅兼容旧档，种子与涌现路径不产出。
- **S5-3 LOD 异步延迟装载**：`WorldMapFactory.Build` 同步物化 High（起始区逐 tile）+ 焦点 Mid；远域 Low 经 `WorldMapLodStreamer` 后台装载并 `MergeBundle(preferExisting)`，不阻塞逻辑返回。`RebuildGeography` 在返回前 `EnsureFarFieldLoaded` 以保证存读档 Replay 远域完整。表现层 `WorldMapChunkCache` 仍不进月哈希。

### 2.4 哈希算法
- **FNV-1a-64** over 上述字节流（Epic 0 / Gate-0 唯一算法）。
- **xxHash64** 仅作架构备选，**未实现**；在引入前须改契约并过四路 Replay，禁止文档与代码口径分裂。
- **禁止** `string.GetHashCode`（运行时不稳定的哈希）。
- `monthlyHash = FNV1a64(buf)`（ulong）。

---

## 3. 时间—结算边界规则（铁律 1/2 的关键实现约束）

> **R-N1（核心确定性风险，须由 V0-3 落实）**：周/月边界**必须从整数序号派生**，不得用 float 累加器减法在循环里逐步逼近。否则 1× 与 20× 在长程（≥120 月）因 float 舍入差产生边界错位，导致分叉。
```
// 正确：边界由整数序号派生
monthIndex = floor(gameClock / MONTH_SECONDS)
nextMonthBoundary = (monthIndex + 1) * MONTH_SECONDS
weekIndex  = floor(gameClock / WEEK_SECONDS)
nextWeekBoundary  = (weekIndex + 1) * WEEK_SECONDS
// Update 循环按 min(nextWeek, nextMonth) 升序合并；同刻 week 先 month 后
```
- `MONTH_SECONDS` / `WEEK_SECONDS` 为**编译期常数**（不受 speedMultiplier 影响）；speed 只缩放 `dtGame`，不改变边界定义。
- `gameClock` 推进为 `gameClock += dtReal * speedMultiplier`，但边界判定一律走整数序号，从源头消除 1×/20× drift。

---

## 4. Gate-0 四路 Replay 规范（G0-6）

| 路 | 速度档 | 说明 |
|----|--------|------|
| ① | 全程 1× | 基准 |
| ② | 全程 20× | 极速，验证速度倍率不改模拟粒度 |
| ③ | 变速 1×→20×→1×（含多次暂停） | 验证暂停/变速不改变演化 |
| ④ | 中途存档→退出→读档续跑 | 验证 ADR-004 快照往返逐位一致 |

- **输入**：同 `worldSeed` + 同 `InterventionLog`（按**游戏月时间戳**记录的干预序列）。
- **时长**：**≥120 游戏月**，含 ≥1 时代过渡 + ≥1 战事 + ≥1 灾害（确保周级子结算通道被覆盖）。
- **比对**：逐月比对四路 `monthlyHash` 序列；任一月分叉即失败，输出**首个分叉月**。
- **断言**：四路序列完全一致（无容差，ADR-002 选项 2 非容差比对）。

---

## 5. Quantize 精度校准流程（待 playtest，初值见 §2.2）
1. V0-2 落地 `Quantize` + `Fix` 兜底。
2. 用四路 Replay 跑通首版精度；若出现「量化过粗导致真实差异被抹平」或「过细仍 drift 分叉」，调 decimals。
3. 最终精度以本文件 §2.2 表格为准（单一可调中心），不允许各子系统私自改精度。

---

## 6. G0 / B2 / B3 / B4 → 测试/CI 映射

### 6.1 Gate-0 门禁项（G0-1~8）
| 门禁项 | 落地测试 / 代码 | 文件 |
|--------|----------------|------|
| G0-1 固定步长唯一时间源 | 静态审计：`WorldSim.Simulation.*` 零 `Time.deltaTime`/墙钟引用 | `ci/asmdef-boundary-check.md` + CI 静态扫描 |
| G0-2 双频按边界时间戳升序合并 | `SimOrchestratorBoundaryTests`：1× 与 20× 产出同一边界/事件序列 | `unit/SimOrchestratorBoundaryTests.cs` |
| G0-3 稳定 ID 排序遍历 | `StableIdOrderingTests`：排序后序与插入序无关 | `unit/StableIdOrderingTests.cs` |
| G0-4 RNG 分流入档 | `RngStreamTests`（确定性 + 状态往返）+ `SerializationRoundTripTests`（状态入档） | `unit/RngStreamTests.cs`, `unit/SerializationRoundTripTests.cs` |
| G0-5 禁不确定并发与浮点漂移 | `QuantizeTests` + 并行仅叶子约束（CI 静态/Doc） | `unit/QuantizeTests.cs` |
| G0-6 四路 Replay 哈希一致 | `Gate0DeterminismTest` + `Replay_FourWay_RealGeography_HashStable` + `Replay_SaveLoad_RebuildGeography_KeepsHashAlignedAndGeographyMatters` | `WorldSim/Assets/Scripts/Tests/Gate0/Gate0DeterminismTest.cs`, `unit/WorldMapTask4Tests_RealGeo.cs` |
| G0-7 哈希函数确定 | `DeterminismHash` 单测（禁 `string.GetHashCode`） | `WorldSim/Assets/Scripts/Tests/Unit/QuantizeTests.cs` / 契约 §2.4 |
| G0-8 三级回退可用 | 回退钩子存在性（`Fix` 全局切换 / 速度档收窄 / lockstep） | Epic 0 V0-7 |

### 6.2 Phase 4 入口条件（B2/B3/B4）
| 条件 | 负责 Story | 测试/CI 证据 |
|------|-----------|--------------|
| **B2** CI 锁同 Unity + 同 Burst | V0-8 ✅ | `gate0.yml` job `pin-versions` + `tests/ci/assert-burst-pinned.ps1`；`version-pins.json` + manifest `com.unity.burst: 1.8.30` |
| **B3** Quantize+确定性哈希+Gate-0 入 CI | V0-2 / V0-4 / V0-5 / V0-9 ✅ | `gate0.yml` job `gate0` 跑全量 `WorldSim.Tests` EditMode，分叉即红，上传 artifact |
| **B4** 真实地球管线 + region-presets 消费 | V0-6 / S5-1（MVP）；完整 DEM → Epic 5；真实地理入哈希 → Task 4/6 | `RegionPresetConsumptionTests` + `assert-region-presets-synced.ps1`；MVP 高程为公式，非真实 DEM；真实地理探针 `RealData_*` + 双视图 + 重建 + 四路 Replay 见 `unit/WorldMapEpic5Tests.cs` / `unit/WorldMapTask4Tests_RealGeo.cs` |

---

## 7. 序列化约束（ADR-004 选项 1，G0-4）
- 全量二进制快照：显式小端自定义 writer，**禁 `BinaryFormatter`**。
- 必须入档：`WorldState` 全量 + `RngRegistry` 全状态 + `gameClock`/累加器 + `moduleToggles` + `InterventionLog`。
- 字典/集合**排序后写**；读档逐位恢复 → 续跑等价于无存档路（Replay 路径④）。
- LOD 分块：High 逐 tile，Mid/Low 聚合；历史层 delta 追加（`schemaVersion` 迁移预留）。
- **RNG 状态入档精度修正（重要）**：架构 §4.2 写作「每条 RNG 流 128-bit 状态」，**但 xoshiro256** 实际为 256-bit（4×uint64）**。仅存 128-bit（s0,s1）会在存档/读档后破坏序列，导致 Gate-0 路径④分叉。因此**每条流必须序列化全 256-bit 状态**（见 `tests/unit/RngStreamTests.cs` 的 `State256` + `Restore`）。该修正不影响 ADR-002 决策本身（仍用 xoshiro256**），仅细化入档字段宽度。

---

## 8. 红线（架构 §2.7，任何测试不得假设其被违反）
- 模拟核心零 `UnityEngine.CoreModule`（GameObject/MonoBehaviour/Transform/Time）。
- UI 不持有游戏状态；干预唯一入口（`IInterventionTarget`）；地理只读（`IWorldGeography`）。
- S6/S8 消费 `SimEvent`，**不回写** `WorldState`。
- 表现插值绝不回写逻辑态。

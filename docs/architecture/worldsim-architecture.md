---
项目名: WorldSim
文档名: 技术架构文档（Architecture Document）
版本: v1.0.2（文档一致性修正：补 §11 B8 入口条件 + 新增 §12 AS-2 URP 渲染顺序假设）
日期: 2026-08-12
作者: 程基岩 (Cheng Jiyan) — 工程负责人 / 主程序
阶段: Phase 3 — 技术搭建（入口 / P0）
评审强度: Full
输入文档:
  - design/concept/game-concept.md v1.4.1
  - design/gdd/systems-index.md v1.1.10
  - design/gdd/intervention-system.md (S1) v1.1.4
  - design/gdd/ecology-sim-engine.md (S2) v1.0.1
  - design/gdd/civilization-system.md (S3) v1.4.3
  - design/gdd/time-progression.md (S4) v1.0.3
  - design/gdd/world-map-generator.md (S5) v1.0.6
  - design/gdd/data/region-presets.json（起始区域数据契约）
  - docs/unity-setup-complete.md
状态: 定稿（Accepted，2026-08-12；ADR-001~004 已接受并锁定，与本文一致）
变更摘要: v1.0.1（由 v1.0.0 定稿升版，纯文档一致性修正，未改架构决策）——R-N1 与 R-N3 已实质修订并落地正文：R-N1（月/周边界由整数序号 monthIndex/weekIndex 派生，替代 float 累加器减法，杜绝长程 ≥120 游戏月 1×/20× 舍入漂移与 Gate-0 路径分叉）落地 §3.2/§3.3/§9.6/§9.7；R-N3（xoshiro256** 内部状态 4×uint64=256-bit 全量随 WorldState 序列化，仅存 128-bit 会破坏 Gate-0 路径④存读档续跑）落地 §4.2/§6.1/§9.6/§9.7。ADR-001~004 与分层架构不变。v1.0.2（由 v1.0.1 文档一致性修正，未改架构决策）——①补 §11：Phase 4 入口条件由"B2–B4"扩至"B2–B4 / B8"，与 control-checklist.md v1.0.1 §C/B8/§D 对齐（B8=R-N2：com.unity.burst 固定版本入 manifest + CI assert-burst-pinned）；②新增 §12「URP 渲染顺序假设（AS-2）」：确认 AS-2 世界层灾害图标 Overlay 在 URP 后处理之后绘制的技术可行性（Render Objects Renderer Feature + AfterRenderingPostProcessing + 专用 Rendering Layer）。
---

# WorldSim 技术架构文档（主架构）

> 本文是 Phase 3 技术搭建的**入口架构资产**，决定后续所有实现方向，优先级 **P0**。
> 它把 Phase 2 的 5 份系统 GDD（S1–S5）+ 概念文档收敛为一份可落地的工程蓝图：
> **系统分层、时间—结算主循环、确定性（R13 / Gate-0）、事件与数据流、分层存档、性能策略、Unity 工程结构、S1–S5 接口契约**。
> 关联交付：`docs/architecture/adr/`（ADR-001~004）、`architecture-review.md`、`control-checklist.md`。
> **不在本文范围**：具体实现代码（Phase 4 预制作）、美术规格、测试用例、系统设计文档本身。

---

## 0. 关键设计约束（架构必须遵守，不可违背）

1. **连续实时时钟，无回合制**：暂停 + 1×/2×/5×/20× 加速；固定步长月级结算 pass（由 S4 驱动）。底层保留月级 pass，但玩家无回合。
2. **混合结算架构**：全局月级大账（约 90% 算力，遍历全量聚落/文明总账）+ 事件驱动周级子结算（仅 `activeEntities` 脏集合，轻量增量，不全局重算）。双频 pass 按边界时间戳升序合并执行。
3. **人口尺度层级**：聚落层跑个体/代理微观模拟；国家/政体层仅聚合统计（总人口 = Σ 聚落）；聚落分档 村/镇/市/都市圈（单聚落可至千万级，不封顶 5 万）。
4. **确定性 R13 = P0，Gate-0 首道里程碑门**：Replay 四路对跑（同 seed + 同干预序列；1× / 20× / 变速 / 存读档；≥120 游戏月；关键指标哈希逐月比对）。五条确定性铁律：固定步长唯一时间源 / 双频 pass 按边界时间戳升序合并 / 遍历用稳定 ID 排序 / RNG 分流入档 / 禁不确定并发与浮点漂移。三级回退：降速度档 → 串行化定点化 → lockstep（均保留连续时间体感，绝不退回回合制）。
5. **真实地球地理**：Natural Earth GeoJSON 海岸线 + 高程 + 气候为基底，全球尺度；MVP 区域精算、其余低精度 LOD。
6. **干预系统**：无限次、异步延迟生效、自然后果制衡；不下场红线（玩家不操控个体，只做间接干预）。
7. **模块化开关**：世代传承 / 科技树 / 多聚落 / 政治结构 MVP 可关。
8. **视觉**：全时代统一微缩沙盘手绘风（低多边形 + 手绘纹理 + NPR），无时代风格切换。

---

## 1. 架构总览与分层

WorldSim 由 **6 个逻辑层 + 1 个横切叙事层** 组成。核心原则是：**模拟核心（Simulation Core）引擎无关、确定性强、零 `UnityEngine` 场景依赖**；Unity 只在 Runtime/Presentation/UI 层做"胶水"与"渲染"。

```
┌──────────────────────────────────────────────────────────────────────┐
│                          表现 / 交互层 (Unity-only)                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────────────┐  │
│  │  Runtime 胶水     │  │  Presentation    │  │  UI / HUD (S8, uGUI)    │  │
│  │ SimulationRunner │  │  Camera(S9)/NPR  │  │  面板 / 干预光标 / 编年史 │  │
│  │ (MonoBehaviour)  │  │  TileMesh 渲染   │  │  时间轴 / 预警卡          │  │
│  └────────┬────────┘  └────────┬────────┘  └───────────┬────────────┘  │
│           │ 订阅 SimEvent       │ 读 WorldView 快照      │ 订阅 SimEvent       │
├───────────┼────────────────────┼───────────────────────┼────────────────┤
│  ┌────────▼────────────────────▼───────────────────────▼──────────┐   │
│  │                    叙事层 (S6 涌现叙事引擎)                        │   │
│  │   消费 SimEvent → 事件检测 / 编年史 / 关键个体追踪                  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
├──────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │         模拟核心 (Simulation Core) — 引擎无关 · 确定性 · 可重放     │  │
│  │  WorldState(聚合根)                                              │  │
│  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐   │  │
│  │  │ S4 时间 │ │ S2 生态 │ │ S3 文明 │ │ S1 干预 │ │ S5 世界地图   │   │  │
│  │  │ 心跳    │ │        │ │        │ │        │ │ (空间基底)     │   │  │
│  │  └───┬────┘ └───┬────┘ └───┬────┘ └───┬────┘ └──────┬───────┘   │  │
│  │      └──────────┴──── SimOrchestrator(月级大账 16+11 步) ─┘       │  │
│  │  RngRegistry(分流入档) · Fixed/Quantized Math · SaveLoad 序列化    │  │
│  └─────────────────────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────┤
│  横切：S7 模块化开关框架(配置) · S10 存档(读写WorldState) · Editor工具   │
└──────────────────────────────────────────────────────────────────────┘
```

**确定性边界（红线）**：`WorldSim.Simulation.*` 全部程序集**只能引用 `UnityEngine.Mathematics` 与 `System.*`，禁止引用 `UnityEngine.CoreModule`（GameObject/MonoBehaviour/Transform/Time）**。任何依赖墙钟/帧率/Transform 的逻辑都不得进入模拟核心。判定"某段逻辑是否确定性安全"的唯一标准：**它能否在没有任何 Unity 场景、无任何渲染的情况下被单元测试重放**。

---

## 2. 系统分层与模块边界

### 2.1 模拟核心（Simulation Core，引擎无关，确定性）
- **`WorldState` 聚合根**：持有全部可序列化确定性状态（地理网格、所有聚落/政体/个体、生态种群/资源、干预 pending 队列、RNG 流、时钟与边界序号（monthIndex/weekIndex）、模块开关、编年史外壳）。是存档与 Replay 的唯一真相源。
- **S4 时间（心跳）**：`TimeDriver` 维护 `gameClock` / `monthIndex` / `weekIndex` / `speedMultiplier` / `paused`（边界由整数序号派生，R-N1）；驱动双频 pass 合并调度（见 §3）。
- **S2 生态**：`EcologySim` 持有 `WorldTile` 生态态、物种、资源、稳态区间；实现 `IInterventionTarget`（rainfall/temperature/birthRate…）。
- **S3 文明**：`CivilizationSim` 持有 `Settlement` / `Polity` / `Individual` / 十一子系统状态；实现 `IInterventionTarget`（devBias_*…）。
- **S1 干预**：`InterventionSystem` 持有 `pendingQueue` 与 `InterventionLog`（按游戏月时间戳记录已生效干预，供 Replay）；解析延迟、写 `pendingDelta` / `devBias_*`。
- **S5 世界地图**：`WorldMapGen` + `WorldGeography` 实现 `IWorldGeography` 只读查询；持有 `WorldTile[,]` 网格与（地缘模式）`GeoPoliticalInit`；导入 `region-presets.json`。
- **`SimOrchestrator`**：月级大账的确定性流水线编排器（见 §3.4），按固定顺序调用 S1→S2→S3 子 pass，并重新评估 `activeEntities`。

### 2.2 表现层（Runtime / Presentation，Unity-only）
- `SimulationRunner`（MonoBehaviour）：从 `UnityEngine.Update(dtReal)` 取真实帧时间 → 交给 `TimeDriver` → 收集本帧产生的 `SimEvent`/世界快照 → 派发到 Unity 事件总线。
- `Presentation`：相机（S9）缩放/平移联动 LOD；NPR 微缩沙盘渲染（低多边形 + 手绘纹理 + URP）；`WorldView` 读取 `WorldState` 的**表现副本**（由快照插值得到）渲染实体。
- **表现插值**：逻辑态只在月/周边界跳变；个体位置、资源视觉量在边界间平滑插值，制造"连续时间体感"，但插值**绝不回写逻辑态**。

### 2.3 UI / HUD（S8，uGUI）
- 订阅 `SimEvent`（预警卡、编年史条目、干预反馈）；展示生态/文明/威胁三面板；干预光标与面板（S1 §5）；时间轴（S4 ↔ S8）。
- **UI 不持有游戏状态**：只读 `WorldView` 快照与事件，所有写入经 `InterventionSystem` 入口。

### 2.4 存档（S10）
- 读写 `WorldState`：分层（确定性态 / 个体层 / 历史层）、增量 delta、二进制主存 + JSON 配置（见 §6）。

### 2.5 编辑器工具（Editor-only）
- New Game 配置装配、`region-presets.json` 导入器、CLI 构建脚本（`BuildScript.cs`）、Gate-0 确定性测试驱动（见 §8、ADR-003）。

### 2.6 叙事层（S6）
- 消费确定性 `SimEvent`，做事件检测/编年史/关键个体追踪；纯只读派生，不产生逻辑副作用（保证确定性不被叙事污染）。

### 2.7 模块边界规则
| 规则 | 说明 |
|------|------|
| 确定性边界 | 模拟核心零 `UnityEngine.CoreModule`；所有逻辑可头less重放 |
| 单向依赖 | Core ← {Ecology,Civilization,Intervention,Time,WorldMap} ← {Runtime,Narrative} ← {Presentation,UI,SaveLoad}；Editor/Tests 依赖全体 |
| 事件只读派生 | S6/S8 消费 `SimEvent`，不得反向写 `WorldState` |
| 干预唯一入口 | 所有玩家写入只经 `InterventionSystem`；`Era/legitimacy/LawStage/GovernanceType/EthnicComposition/LawFamily/InstitutionProfile` 不注册为可干预参数（S3 §4.1 红线） |
| 地理只读 | S5 是空间基底生产者，除存档外不消费其他系统回写 |

---

## 3. 时间—结算主循环（连续时钟 + 双频混合结算调度）

### 3.1 连续时钟（S4 §2.1）
`gameClock`（游戏秒）由 `dtReal * speedMultiplier` 推进；`paused` 时冻结。季节/年份/时代由累计游戏月推算（3 月=季，12 月=年）。**无玩家回合**。

### 3.2 固定步长双频边界派生（S4 §2.2 / §2.7，R-N1 修订：由整数序号派生，非 float 累加器减法）
```
float gameClock;            // 连续游戏时钟（游戏秒）
int   monthIndex;           // 已通过的月边界数（整数序号，月边界派生源）
int   weekIndex;            // 已通过的周边界数（整数序号，周边界派生源）
const float MONTH_SECONDS;  // 1 游戏月对应的游戏秒（基准×速度倍率）
const float WEEK_SECONDS = MONTH_SECONDS / 4;
StableSet activeEntities;   // 脏标记集合（按稳定 ID 有序）
// Gate-0 防分叉（R-N1，P0）：月/周边界由 monthIndex/weekIndex 整数序号派生
//   nextMonthBoundary = (monthIndex + 1) * MONTH_SECONDS
//   nextWeekBoundary  = (weekIndex  + 1) * WEEK_SECONDS
// 循环中比较 gameClock < nextBoundary；禁止用 float 累加器减法
// （monthAccumulator -= MONTH_SECONDS）逼近边界，否则长程（≥120 游戏月）
// 1×/20× 因舍入差产生边界错位，导致 Gate-0 Replay 分叉。
```
- 月级大账：每累计 1 游戏月，遍历全量（人口/国土/科技/国运），~90% 算力。
- 周级子结算：每累计 1/4 月，仅遍历 `activeEntities`（交战军队/受灾聚落/建设中的城市），轻量增量刷新，~10%（整体约纯月级 1.5–2×）。

### 3.3 双频 pass 按边界时间戳升序合并执行（铁律 2，修正 S4 §2.2；R-N1 边界由整数序号派生）
```
void Update(float dtReal) {
    if (paused) return;
    float target = gameClock + dtReal * speedMultiplier;
    while (true) {
        // R-N1（Gate-0 防分叉）：边界由整数序号派生，禁止累加器减法，杜绝长程舍入漂移
        float nextMonth = (monthIndex + 1) * MONTH_SECONDS;  // 整数月序号派生，无漂移
        float nextWeek  = (weekIndex  + 1) * WEEK_SECONDS;   // 整数周序号派生，无漂移
        float next = min(nextWeek, nextMonth);
        if (next > target) break;
        gameClock = next;
        if (next == nextWeek)  { RunWeeklySubSettlement(SortedByStableId(activeEntities)); weekIndex++; }  // 同刻 week 先
        if (next == nextMonth) { RunMonthlySettlementPass();                                 monthIndex++; } // 同刻 month 后
    }
    gameClock = target;  // 剩余不足一个边界的时间仅推进表现层（插值）
}
```
> 高速（20×）下一次 `Update` 跨多个周/月边界时，顺序由游戏时间轴唯一确定；同刻固定 week 先于 month，消除"先跑完所有周再跑月"引入的分叉。

### 3.4 月级大账流水线顺序（SimOrchestrator，对齐 S4 §4.3 + S3 §4.3 + S2 §4.3）
每个累计 1 游戏月的月级大账：
1. **S1 干预结算**：读取 `pendingQueue`，延迟到期的干预生效 → 写 `pendingDelta` / `devBias_*` 到 S2/S3 注册参数；记录 `InterventionLog{gameMonth, action}`（Replay 用）。
2. **S2 生态月结（11 步）**：应用 S1 参数 → 推进季节 → 植物/食草/食肉结算 → 资源再生 → 地貌演变 → 稳态区间 → 相变 → 生态指标 → 输出生态事件。
3. **S3 文明月结（16 步）**：①读 S1 devBias ②读 S2 生态修正 ③个体层(needs/生死/关系/记忆) ④经济层 ⑤聚落层 ⑥科技层 ⑦社会层 ⑧宗教层 ⑨文化层 ⑩族群层 ⑪法律层 ⑫政治层(合法性/稳定度/演进) ⑬军事层(军费/战争自动结算/战损) ⑭时代层(门槛/过渡/ecoImpactCoefficient) ⑮国家层聚合(Σ聚落) ⑯输出事件。
4. **S4 计数**：每 3 月切换季节；累计年/时代过渡；推进紧急干预冷却、干预效果衰减、`stressMonths` 等。
5. **重新评估 `activeEntities`**：月级大账末重算激活标记（交战/受灾/建设），静默实体移出脏集合。
6. **事件派发**：`SimEvent` → S6（叙事）/ S8（UI）。

> 因果依据（沿用 S3 §4.3）：法律(⑪)先于政治(⑫)（制度合法性由法制供给）；族群(⑩)在文化(⑨)后、法律(⑪)前（polarization/ethnicInequality 当月政治与军事前完成）；时代(⑭)末位（读本月全量结果）。

### 3.5 周级子结算（仅 activeEntities，稳定 ID 排序）
`RunWeeklySubSettlement(sortedActive)`：按稳定 ID 升序遍历，轻量刷新交战军队资源流/行军、受灾聚落灾害衰减、建设城市进度；**不重算人口/国土/科技/国运**。遍历前必须 `SortedByStableId`（铁律 3，修正 `HashSet` 迭代序不确定）。

### 3.6 速度档与表现插值
- 1×/2×/5×/20× 仅缩放 `dtGame`，**不改变单个 pass 内容**（R14 前提）。
- 表现层在月/周边界间插值（个体位置、资源视觉量、相机），制造连续体感；插值不回写逻辑态。

---

## 4. 确定性与 RNG 管理（R13 / Gate-0）

### 4.1 五条确定性铁律（落地 S4 §7.3）
1. **固定步长是唯一时间源**：所有子系统只读 `gameClock` 与 pass 序号；禁止读 `Time.deltaTime`/墙钟/帧计数/系统时间。
2. **双频 pass 按边界时间戳升序合并**（§3.3）。
3. **遍历顺序必须稳定**：`activeEntities` 及所有聚落/政体/物种集合遍历前按稳定 ID 升序；禁止依赖字典/哈希集自然序。
4. **RNG 按子系统分流且状态入档**（§4.2）。
5. **禁不确定并发与浮点漂移**：并行归约须固定块序合并；禁用 fast-math；关键累加量定点化或量化写回（§4.5）。

### 4.2 RNG 分流入档（铁律 4）
- 每个子系统持有由 `worldSeed` 派生的独立流：`streamId = Hash(worldSeed, systemTag)`，如 `ecology.region.{id}`、`civ.{polityId}.military`、`civ.{polityId}.tech`、`disaster`、`war`。
- 采用确定性 PRNG（输出字宽 64-bit，如 **xoshiro256** 内部状态 256-bit（4×uint64）/ splitmix64），**禁用 `System.Random`**（非跨平台确定）。
- 所有 RNG 抽取仅在月/周 pass 内、稳定 ID 序下发生；**存档序列化每条流的全部内部状态**（256-bit 状态：4×uint64，s0..s3），否则读档分叉。
- `RngRegistry` 统一持有并随 `WorldState` 序列化；表现层（视觉抖动等）使用独立非确定性流，不影响逻辑。

> **R-N3（P0，ADR-002 选项 2 正确实现细节）**：xoshiro256** 的内部状态是 **4 个 uint64 = 256-bit**（非 128-bit）。存档**必须序列化全部 4 个 uint64（s0..s3）**；若仅存 128-bit（2 个 uint64）会破坏序列，导致 **Gate-0 路径④（存读档续跑）与无存档路哈希分叉**。此宽度与 ADR-002 选项 2（float + 禁 fast-math + 量化写回 + `Fix` 兜底）一致——存档"全状态"即指 256-bit 全量。

### 4.3 Replay 框架（命令日志 + 月边界快照 + 指标哈希）
- **输入**：`worldSeed` + `moduleToggles` + `InterventionLog`（按**游戏月时间戳**记录的干预序列，非现实时间）。
- **四路对跑**（Gate-0 规范）：①全程 1× ②全程 20× ③中途变速(1×→20×→1×，含多次暂停) ④中途存档→退出→读档续跑。
- **指标哈希**：每月级大账结束，对关键指标（各 `Polity.population`/总产出/总军力/稳定度、各物种 `population`、资源 `currentAmount`、科技层级、EraGate 观测字段、`RngRegistry` 全状态）先**量化到固定精度**再计算稳定哈希（**FNV-1a-64** over 确定性字节流；xxHash 未实现）。
- **比对**：逐月比对四路哈希序列，任一月分叉即失败，并输出首个分叉月用于定位。

### 4.4 Gate-0 落地路径（P0 首道里程碑门）
- 实现顺序：先以**串行月级 pass（无 Job 并行）**跑通四路 Replay，哈希逐月一致 → 通过 Gate-0 → 再逐步引入并行（回退 2 路径）。
- **三级回退**（均未通过时按序降级，绝不回回合制）：
  | 级别 | 措施 | 代价 |
  |------|------|------|
  | 回退 1 | 收窄速度档（去 20×，留 1/2/5），干预对齐月边界 | 失极速快进，体验损失小 |
  | 回退 2 | pass 内全串行化（关并行归约），关键浮点量定点化 | 性能降，靠混合结算+LOD 补偿 |
  | 回退 3 | 确定性 lockstep：表现插值，模拟严格离散步进、步间不接收输入 | 干预即时感降（落到下一月边界） |

### 4.5 浮点策略（已采纳 ADR-002 选项 2：float + 禁 fast-math + 量化写回 + `Fix` 兜底）
- **MVP / Gate-0（PC x64 单平台）**：受控 IEEE-754 float + **禁用 fast-math** + `UnityEngine.Mathematics` 确定性运算即可逐位一致（同 Unity 版本、同 Burst 设置）。已落地为 ADR-002 选项 2：**非全局定点、非容差比对**；跨平台 / 回退 2 由 `Fix` 兜底。
- **量化写回边界**：所有进入指标哈希、所有跨月持久化的累加量，在写回前 `Quantize(x, decimals)`（等价定点截断），消除尾差累积。
- **定点兜底（`Fix` 类型，Q 格式如 32.32）**：保留于 `WorldSim.Simulation.Core.Math` 供回退 2 使用；对争议累加量（人口增长率、资源再生、Logistic 种群）直接以 `Fix` 运算，彻底消除跨平台漂移。
- **跨平台（Mac/ARM）Replay**：一旦需要，全局切换 `Fix`；PC 单平台 Gate-0 不必强制。

---

## 5. 事件总线 / 数据流

### 5.1 SimEvent 总线（确定性产出 → 叙事/UI）
```
struct SimEvent {
    long gameMonth;            // 游戏月时间戳（锚定因果链）
    EventCategory category;    // Ecology / Civ / War / Disaster / Era / Chronicle
    int sourceId;              // 来源实体稳定 ID
    string templateId;         // 叙事模板（S6 匹配）
    Dictionary<string,float> metrics;
}
```
- 模拟核心在 pass 内**有序**产出 `SimEvent` 列表（按稳定 ID 序），挂到当月快照；Runtime 派发到 S6/S8。
- S6/S8 为纯消费者，副作用只进各自状态（编年史、UI），**不回写 `WorldState`**。

### 5.2 接口契约（设计红线）
- **`IInterventionTarget`**（S1 定义，S2/S3 实现）：`RegisterInterventionParameter` / `ApplyIntervention(paramKey, delta, durationMonths)` / `GetParameterValue`。玩家写入**只**经此接口。
- **`IWorldGeography`**（S5 定义，只读）：`GetBiome` / `GetElevation` / `GetClimate` / `HasCoast` / `GetRivers` / `GetLOD`。S2/S3/S9 唯一地理查询入口（R10 一致性保证）。

### 5.3 devBias_* 参数清单（S3 §4.1，间接干预入口）
`devBias_agriculture/hunt/defense/trade/faith/military/ethnicity/culture`（聚落级，[-1,+1]，3–5 月衰减）+ `foodReserveCoeff` / `techUnlockBoost` / `happinessMod`。**不可干预派生状态**：`Era` / `legitimacy` / `LawStage` / `GovernanceType` / `EthnicComposition` / `LawFamily` / `InstitutionProfile`。

### 5.4 数据流图
```
[玩家输入]→UI→InterventionSystem.pendingQueue
        ↓ (延迟生效, 写 devBias_*/pendingDelta)
WorldState ──SimOrchestrator──→ S2(生态态) → S3(文明态) → 世代/国家聚合
        ↓                              ↑ IWorldGeography(只读)        ↑ IInterventionTarget
   S5 WorldTile/GeoPolitical    S5 空间基底 ─────────────────────────┘
        ↓ SimEvent(有序)
   S6(叙事) + S8(UI)  ──(只读派生)──┐
        ↓ WorldView 快照(插值)        ┴→ Presentation/Runtime 渲染
```

---

## 6. 世界状态序列化与分层存档（已采纳 ADR-004 选项 1：全量二进制快照 + 命令日志 + LOD 分块 + delta；呼应存读档 Replay）

### 6.1 分层
| 层 | 内容 | 频率 |
|----|------|------|
| 确定性态层 | `WorldState` 全量（地理网格 + 聚落/政体/个体 + 生态 + RNG 流 + 时钟/monthIndex/weekIndex + 模块开关 + `InterventionLog`） | 月边界快照 + 读档全量 |
| 个体层 | `Individual` 关系/记忆/情感（聚落微观细节） | 随确定性态一并序列化（MVP 折叠为骨架） |
| 历史层 | 编年史（S6 输出事件序列） | 追加写，增量 |

### 6.2 快照 + 命令日志（支持 Replay 路径④）
- **全量快照**：月边界序列化 `WorldState` → 读档恢复**逐位一致**状态 → 续跑与无存档路一致（Gate-0 路径④成立）。
- **命令日志**：`InterventionLog`（游戏月时间戳）随档保存；变速/暂停由时钟驱动，与日志无关。
- 存读档 Replay 等价性证明：`WorldState` 是完整确定性状态 ⇒ 同 seed + 同 `InterventionLog` + 同 `moduleToggles` ⇒ 演化逐月一致。

### 6.3 格式
- **二进制主存**：确定性态用显式端序（小端）自定义 writer，避免 `BinaryFormatter`（不安全/不确定）；关键量先 `Quantize`。
- **JSON 配置**：`WorldInitConfig` / `region-presets.json` / 模块开关 / 元数据（人读、调试友好）。
- **增量 delta**：历史层与高频变化（生态指标）用 delta 追加，降低 IO。

### 6.4 WorldTile 网格分层序列化（LOD）
- 全球 `WorldTile[,]` 体量大；按 LOD 分块：High 区逐 tile 全量，Mid/Low 区聚合压缩；读档先载 High + 焦点 Mid，远域 Low 延迟装载（异步，不阻塞逻辑）。

### 6.5 对齐 region-presets.json（数据契约）
- `region-presets.json`（schemaVersion 1.0）是**起始区域预设表**（6 个真实区块：中心 lat/lon、半径°、ethnicSeed[languageFamily,name,share]、legalFamilyDefault）。
- 映射：`WorldInitConfig.startRegionCenter/Radius` ← 预设 `center/radiusDeg`；`ethnicSeed` ← 预设 `ethnicSeed`（地缘模式，S5 空间映射为 `RealEthnicDistribution`）；`legalTraditionSeed` ← 预设 `legalFamilyDefault`（偏置，绝不指定单国家族）。
- 该 JSON 由 Editor 工具导入，不参与逐月模拟，仅作 New Game 初始化源（见 §9.1）。

---

## 7. 性能策略

### 7.1 混合结算调度（核心杠杆）
- 全局月级大账 ~90% 算力遍历全量；周级子结算 ~10% 仅脏集合。静默实体跳过周结算 → 多文明并存时算力可控（约纯月级 1.5–2×）。

### 7.2 LOD（两层）
- **模拟 LOD（尺度跃迁 S4 §2.4）**：聚落层微观（个体/代理），国家层**仅聚合统计**（不逐人模拟）。这是性能天花板的关键——成本绑定**聚落数**而非人口数。
- **地理 LOD（S5 §2.3）**：High（起始区域逐 tile）/ Mid（相机附近区域级）/ Low（其余大陆级）。全球网格下月 tick 预算受 Low 聚合粒度保护。

### 7.3 ECS / JobSystem / Burst 利用与确定性并行约束
- **MVP / Gate-0**：串行月级 pass（无 Job），确定性最干净。
- **核心层扩展**：真正" embarrassingly parallel "的叶子计算（同区域内逐 tile 生态更新、逐个体 needs 更新）可用 `IJobParallelFor` + Burst，但**必须满足**：固定块序归约 + 禁用 fast-math + 可选 `Fix`；且**仅在 Gate-0 通过后**启用（回退 2 路径）。
- **ECS 取舍（已采纳 ADR-001 方案 C）**：不采用全量 DOTS ECS 作为 Spine——chunk 迭代序非确定、结构变更破坏重放，与 Gate-0 冲突；采用数据导向结构体 + 显式有序流水线更契合月级因果耦合。

### 7.4 人口尺度分层聚合（性能天花板的解法）
- 单聚落可千万级，但国家聚合只读 Σ 聚落 → 月度宏观 pass 成本 = O(聚落数 + 活跃生态区 + 政体数)，与"单聚落 5 万 vs 1 千万"无关。此设计使千万~十亿级国家不引发逐 NPC 性能灾难（T1/T4 响应）。

### 7.5 预算表（目标，待 playtest 校准）
| 指标 | 目标 | 备注 |
|------|------|------|
| 实时帧率 | 60 FPS（1×/2×/5×）；≥30 FPS（20×） | 表现层 |
| 单次月级大账 | < 50 ms（核心层全开） | 超则收紧 LOD / 启用 Job(回退2) |
| 1× 月耗时 | 2–5 秒现实（基准，可调） | S4 §2.1 |
| 20× 月密度 | ≤10 游戏月/现实秒 | 单次 pass 预算内则实时 |

---

## 8. Unity 工程结构（Assembly 划分、目录约定、CLI 构建与 CI）

> 用户**不操作 Unity Hub GUI**，一切走 Unity CLI + unity mcp（见 `docs/unity-setup-complete.md`）。以下结构据此设计。

### 8.1 Assembly 划分（asmdef）

> **Epic 0 现状**：规划 **14** 个；已落地 **9**（6× `WorldSim.Simulation.*` + Presentation + Editor + Tests）。`Narrative` / `SaveLoad` / `ModularToggle` / `Runtime` / `UI` 等在后续 Epic 按需拆出，本节表格仍是目标架构，不是「已全部建文件」清单。

| 程序集 | 依赖 | 说明 |
|--------|------|------|
| `WorldSim.Simulation.Core` | Unity.Mathematics, System | WorldState / RngRegistry / Fix / Math / SimOrchestrator / TimeDriver（**零 CoreModule**） |
| `WorldSim.Simulation.Ecology` | Core | S2 |
| `WorldSim.Simulation.Civilization` | Core, Ecology | S3（含十一子系统） |
| `WorldSim.Simulation.Intervention` | Core | S1 |
| `WorldSim.Simulation.Time` | Core | S4 时间数学/边界派生/季节/尺度跃迁 |
| `WorldSim.Simulation.WorldMap` | Core | S5 + IWorldGeography + region 导入 |
| `WorldSim.Narrative` | Core, Sim Events | S6 |
| `WorldSim.SaveLoad` | Core | S10 序列化 |
| `WorldSim.ModularToggle` | Core | S7 模块开关配置 |
| `WorldSim.Runtime` | 全部 Sim + Unity(全) | SimulationRunner / 事件桥 / 场景引导 |
| `WorldSim.Presentation` | Runtime, URP | Camera(S9) / NPR / TileMesh |
| `WorldSim.UI` | Runtime, uGUI | S8 HUD |
| `WorldSim.Editor` | 全体 + Editor | BuildScript / region 导入器 / New Game 装配（UNITY_EDITOR） |
| `WorldSim.Tests` | 全体 Sim + TestFramework | Gate-0 确定性 + 单元（EditMode） |

### 8.2 关键目录约定
```
WorldSim/
  Assets/Scripts/
    Simulation/{Core,Ecology,Civilization,Intervention,Time,WorldMap}
    Narrative/  SaveLoad/  ModularToggle/
    Runtime/  Presentation/  UI/
    Editor/  Tests/
  Assets/StreamingAssets/  region-presets.json  (New Game 初始化源)
  Packages/  ProjectSettings/
```

### 8.3 CLI 构建管线（用户走 CLI）
```
# 编辑器路径（unity-setup-complete.md 已确认）
UNITY="C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Unity.exe"
PROJ="C:/Users/guowang/Desktop/11/WorldSim"

# 构建 Win64 Player（无 GUI、批处理）
"$UNITY" -batchmode -nographics -projectPath "$PROJ" \
  -executeMethod WorldSim.Editor.BuildScript.BuildWin64 \
  -logFile build.log -quit

# 运行 Gate-0 确定性测试（EditMode，headless；推荐全量 WorldSim.Tests）
"$UNITY" -batchmode -nographics -projectPath "$PROJ" \
  -runTests -testPlatform EditMode -assemblyNames WorldSim.Tests \
  -testResults gate0.xml -logFile gate0.log -quit
```
- `BuildScript.cs`（`WorldSim.Editor`）暴露静态入口 `BuildWin64()`（`-executeMethod WorldSim.Editor.BuildScript.BuildWin64`），读取启用场景出包到仓库根 `Builds/Win64/`。

### 8.4 CI 管线（已采纳 ADR-003 选项 A：本地 CLI 脚本 + GitHub Actions 自动门禁；Gate-0 确定性门禁）
- 本地 CLI 脚本 + GitHub Actions：`pin-versions`（Burst/asmdef/presets）→ self-hosted Windows 全量 `WorldSim.Tests` EditMode → **哈希分叉则 CI 红**，阻断合入。
- Gate-0 门禁项见 `control-checklist.md`；CI 即把该清单自动化；完全 headless，符合用户"走 CLI 不碰 Hub"。
- 自托管 job 使用 `shell: powershell`（Windows PowerShell 5.1）；勿依赖本机未装的 `pwsh`。

### 8.5 CLI 约束
- 禁止依赖 Hub GUI 的任何交互（如 Package Manager 弹窗）；包版本锁定于 `Packages/manifest.json`（已确认：URP 17.0.4 / AI Navigation 2.0.0 / Input System 1.8.1 / Timeline 1.8.6 / uGUI 2.0.0）。
- 所有工程操作（建场景、装配 prefab、导入 region）经 unity mcp 或 Editor 脚本完成。

---

## 9. 与 S1–S5 的接口契约（数据结构对齐 region-presets.json）

### 9.1 S5 ↔ region-presets.json
```csharp
// region-presets.json 契约（schemaVersion 1.0）
struct RegionPreset {
    string key; string name;
    GeoCoord center;          // { lat, lon }
    float radiusDeg;
    List<EthnicSeedEntry> ethnicSeed;   // { languageFamily, name, share }
    string legalFamilyDefault;          // CivilLaw/CommonLaw/SocialistLaw/CustomaryLaw
}
// New Game 装配：preset → WorldInitConfig
struct WorldInitConfig {
    Era startEra; Vector2 startRegionCenter; float startRegionRadius; string startRegionPreset;
    int borderYear; bool useRealBorders; StartMode mode; string geoDataBuild;
    RealEthnicDistribution? ethnicSeed;     // 地缘模式：由 preset.ethnicSeed 空间映射
    LegalFamily? legalTraditionSeed;       // 地缘模式：由 preset.legalFamilyDefault 偏置
    // 注意：目标模式(GoalMode) 不进 WorldInitConfig（概念 §3.4.1，S5 §2.2.1 ⑤ 归属声明）
}
```

### 9.2 S5 → S2/S3/S9：IWorldGeography（只读）
```csharp
interface IWorldGeography {
    BiomeType GetBiome(Vector2 pos); float GetElevation(Vector2 pos);
    ClimateZone GetClimate(Vector2 pos); bool HasCoast(int settlementId);
    List<Vector2> GetRivers(); LODLevel GetLOD(Vector2 pos);
}
struct WorldTile { int id,latIdx,lonIdx; bool isLand; BiomeType biome;
    float elevation; ClimateZone climate; float baseTemp,baseRain; bool hasCoast; LODLevel lod; }
struct WorldMapState { WorldInitConfig config; WorldTile[,] tiles; GeoPoliticalInit? geoPolities; }
```

### 9.3 S1 ↔ S2/S3：IInterventionTarget
```csharp
interface IInterventionTarget {
    void RegisterInterventionParameter(string key, float def, float min, float max);
    void ApplyIntervention(string key, float delta, int durationMonths); // pendingDelta 由 S4 时钟结算
    float GetParameterValue(string key);
}
// S2 注册：rainfall_{regionId}/temperature_{regionId}/birthRate_{speciesId}/population_*/regenRate_*
// S3 注册：devBias_{agriculture|hunt|defense|trade|faith|military|ethnicity|culture}_{sid}
//        + foodReserveCoeff_*/techUnlockBoost_*/happinessMod_*
```

### 9.4 S2 生态（核心数据）
`Species{id,name,type,biome[],population,birthRate,deathRate,carryingCapacity,climateSensitivity,HomeostasisZone,stressMonths}`；
`FoodChainLink{predatorId,preyId,predationRate,dependencyRatio}`；
`RenewableResource{type,currentAmount,maxAmount,regenRate,harvestRate,zone}`；
`HomeostasisZone{stableLower,stableUpper,criticalLower,criticalUpper,equilibrium,selfRepairRate,stressDecayFactor,stressDurationLimit,transitionType}`；
`EcologicalIndicator{type,currentValue,trend,zoneStatus,monthsInStress,warning}`。
（均对齐 S2 §2）

### 9.5 S3 文明（核心数据，对齐 S3 §2）
`Settlement{id,name,location,population,carryingCapacity,zones[],prosperity,settlementLevel,growthRate}`（分档 村/镇/市/都市圈）；
`Polity{governanceType,eraContext,rulerId,stability,legitimacy,sources,scaleTier,internalTitleTier,externalTitleTier,suzerainId,vassalIds,centralization,tributeRate,loyaltyToSuzerain,dominionMode,sovereigntyDeJure,asymmetricDependency,allegiances[],internalTier,memberSettlementIds,totalPopulation,aggregateOutput,aggregateMilitaryPower,aggregateStability}`；
`Individual{id,ageMonths,familyId,health,skill,occupation,happiness,literacy,relations[],memories[],needs}`；
`Economy{stocks[],laborAllocation,tradeVolume,divisionLevel,exchangeMode}`；
`TechNode{id,name,prerequisites,knowledgeCost,tier,minEra,effects[]}`；
`CultureProfile{languageFamily,aestheticTradition,socialStructureOrientation,coreValueAxes[]}`；
`LawCode{stage,lawFamily,norms[],enforcement,impartiality,separationOfPowers}`；
`MilitaryState{militaryPower,militaryTechLevel,defenseLevel,hasNavy,warStatus,warWeariness,supplyMonths}`；
`EthnicComposition{groups[],dominanceIndex,fractionalization,polarization,ethnicInequality,mixingRate}`。

### 9.6 S4 时间（核心数据，确定性关键）
`TimeDriver{gameClock, monthIndex, weekIndex, speedMultiplier, paused}`（monthIndex/weekIndex 为已通过月/周边界整数序号，边界派生源，随档序列化；取代易漂移的 float 累加器，R-N1）；
`RngRegistry{ Dictionary<string, RngState> streams }`（每条 `RngState` = 256-bit 内部状态（4×uint64），随档序列化；R-N3）；
`InterventionLog{ List<InterventionRecord{gameMonth, action}> }`。

### 9.7 确定性契约汇总（必须入档 / 必须稳定排序）
- **必须入档**：`WorldState` 全量 + `RngRegistry` 全状态（256-bit 全量，R-N3）+ `gameClock`/monthIndex/weekIndex（整数序号，R-N1）+ `moduleToggles` + `InterventionLog`。
- **必须稳定排序遍历**：`activeEntities`、所有 `Settlement`/`Polity`/`Species`/`EthnicGroup` 集合（按稳定 ID 升序）。
- **禁止**：读 `Time.deltaTime`/墙钟/帧计数；字典/哈希集自然迭代序；fast-math；跨 pass 的非确定性并发。

---

## 10. 风险与架构应对（映射 T1–T6, R13/R14）

| 风险 | 架构应对 |
|------|---------|
| T1 大量实体性能 | 数据导向 + 尺度聚合（§7.4）+ 混合结算 + LOD；Job 仅叶子（回退2） |
| T2 存档复杂度 | 分层 + 增量 + 二进制量化（§6）；WorldState 聚合根单一真相源 |
| T3 AI 寻路开销 | 聚落内局部简化寻路 + 兽群 flocking；不在模拟核心（表现层） |
| T4 生态计算开销 | tick-based 月结（非每帧）+ 区域级 LOD + 活跃脏集合 |
| T5 尺度平滑切换 | LOD 渐进 + 国家层标注"聚合统计" + 相机联动（§7.2） |
| T6 叙事存储检索 | S6 消费 SimEvent，月结后扫描，事件检测模式（§5.1） |
| R13 确定性 | §4 全文 + Gate-0（P0 首门） |
| R14 加速涌现突变 | 加速不改模拟粒度；预警以游戏月计；UI 高速强化（S4 §6.1） |

---

## 11. 附录：与 Phase 4 预制作的关系
本文定义"做什么/怎么分层/确定性怎么保"。Phase 4 预制作（实际写代码）的入口条件见 `control-checklist.md`：`ADR-001~004` 已于 2026-08-12 经用户拍板接受（仿真范式=C / 确定性数学=2 / CLI·CI=A / 序列化=1），B1 已解决，B2–B4 / B8 列为 Phase 4 预制作入口条件须排期；Gate-0 串行路径仍须 Phase 4 跑通。本文不预写任何实现文件。

## 12. URP 渲染顺序假设（AS-2 世界层灾害图标 Overlay）

- **来源**：美术圣经 v1.0.1 §7.1 / AS-2（P0 安全强制项）。art-director 假设：世界层（L6 Overlay）灾害/危机图标"暖白底板 `#F5F0E8` + 深褐描边 `#3A2A1A`"渲染于**后处理之后**，不被季节/灾害调色（URP Volume 的 Color Adjustments / Color Filter LUT）侵蚀，从而在任何大地色底上保持 ≥3:1 图形对比（WCAG 1.4.11）。
- **技术判定：已确认技术可行（成熟 URP 方案）**。在 URP（项目固定版本 17.0.4，见 §8.5 / 美术圣经 §8.2）下，使指定几何在**后处理之后**绘制的成熟机制为：
  - 使用 URP **Renderer Feature：`Render Objects`**，将其 **Event** 设为 **`AfterRenderingPostProcessing`**（该 `RenderPassEvent` 在 URP 后处理栈执行完毕、最终输出前触发）。
  - 通过 **Filters → Layer Mask / Rendering Layer Mask** 将该 Feature 限定到**专用 Sorting Layer / Rendering Layer**（与默认世界层、UI 层隔离），仅重绘灾害图标几何。
  - 图标几何须作为**世界空间实体**（Sprite/Quad，挂在专用 Rendering Layer）而非默认的 Screen Space - Overlay uGUI——后者由 Canvas 单独合成、不走 Render Objects 重绘路径；要让"世界层图标随地图位置锚定且仍躲过后处理"，必须走专用 Rendering Layer + `Render Objects(AfterRenderingPostProcessing)` 路径。
- **不侵蚀的保证**：该路径下图标在色彩分级 / 灾害偏色 Volume **之后**绘制，季节与灾害调色对其无效，双极兜底对比由实色底板+描边锁定；与美术圣经 §5.3"后处理只调世界渲染层、UI 对比由实色卡保障"及 §8.4 可访问性覆盖层"优先级高于季节/灾害调色"同源一致。
- **设计假设 / 待验证项（Sprint 01）**：
  - 图标系统最终落在"uGUI 世界空间 Canvas"还是"Sprite/Quad + 专用 Rendering Layer"——前者默认在透明 pass 受后处理影响，须显式改为专用 Rendering Layer 才能被 `Render Objects(AfterRenderingPostProcessing)` 命中。此集成选择须在 **Sprint 01（建议挂到灾害/危机图标表现 Story，V0-? 待 Sprint 01 计划落点）** 经原型验证：在季节 + 灾害调色 Volume 激活下截图比对，确认图标对比不受侵蚀、且世界锚定稳定。
  - 该验证属表现层（Phase 4）实现动作，本文仅作技术确认，不预写代码。
- **关联**：美术圣经 v1.0.1（AS-2 / §5.3 / §8.4）、control-checklist.md（AS-2 为 P0 安全项）、asset-spec.md v1.0.1（`UI_ICO_` 资产前缀）。

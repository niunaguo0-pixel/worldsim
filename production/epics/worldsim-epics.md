---
项目名: WorldSim
文档名: Phase 4 预制作 — Epic / Story 拆分（工程视角）
版本: v0.2.0
日期: 2026-08-14
作者: 程基岩 (Cheng Jiyan) — 工程负责人 / 主程序
阶段: Phase 4 预制作（进行中）
输入文档:
  - docs/architecture/worldsim-architecture.md v1.0.0
  - docs/architecture/adr/ADR-001~004（已接受）
  - docs/architecture/control-checklist.md v1.1.0（Phase 3 PASS / Phase 4 OPEN）
  - docs/architecture/architecture-review.md（CONCERNS 收敛为 B2–B4）
  - production/phase4-gate.md v1.2.0
  - production/sprint-02-plan.md（已结项）
  - production/sprint-03-plan.md（已结项）
  - design/gdd/time-progression.md (S4) / civilization-system.md (S3) / world-map-generator.md (S5)
  - design/gdd/data/region-presets.json（schemaVersion 1.0）
  - docs/unity-setup-complete.md（Unity 6000.0.81f1 / CLI）
状态: Phase 4 进行中 — Epic 0 Done；Sprint 02–03 CLOSED；下一 Sprint 04 待开
---

# WorldSim — Phase 4 预制作 Epic / Story 拆分

> 本文是工程视角的 Phase 4 实装拆解：**确定性垂直切片优先**（Gate-0 首道门），再按系统铺开。
> **不写设计 / 美术 / UX 规格**——所有 Story 均为工程实现项，验收标准对齐 GDD 需求 ID 与架构/ADR 契约。
> 测试：**可执行测试**在 `WorldSim/Assets/Scripts/Tests/`；契约与 CI 在 `tests/`（`tests/contracts/determinism-contract.md`、`tests/ci/`、`.github/workflows/gate0.yml`）。`tests/unit`/`tests/gate0` 仅占位说明，无双源 `.cs`。
>
> **2026-08-14 对账**：Epic 0 = **Done**；Sprint 02 VS-8 = **CLOSED**（PR #7）；Sprint 03 S2/S3+P3 = **CLOSED**（PR #9）。Epic 2/3 Must 已收口；**下一 Sprint 04 = Epic 5 全分辨率地球 + Epic 7 LOD 存档 + S3-3/5**（§11）。

---

## 0. 阅读说明

### 0.1 MoSCoW 优先级
| 标记 | 含义 | 说明 |
|------|------|------|
| **Must** | 必须做（本阶段不可省） | 缺失则 Gate-0 或对应系统无法成立 |
| **Should** | 应当做 | 强烈建议；若工期紧张可拆到下一 Sprint，但需留接口 |
| **Could** | 可以做 | 锦上添花 / 回退钩子 / 非阻塞 |
| **Won't** | 本阶段不做 | 明确推迟（列原因，避免范围蔓延） |

### 0.2 映射图例
- **B2 / B3 / B4**：`control-checklist.md` Phase 4 入口条件。
- **G0-1~G0-8**：Gate-0 CI 门禁项（确定性八条）。
- **ADR-001~004**：已接受架构决策（C 数据导向 / 2 float+量化+Fix / A CLI+CI / 1 全量快照+命令日志+LOD）。
- **Sx §y.z**：对应 GDD 章节，保证需求可追溯。

### 0.3 垂直切片铁律（来自主理人指令 + 架构 §4 / S4 §7.3）
1. **Gate-0 是第一垂直切片**：先跑通「确定性模拟核心 + 连续时间双频结算 + 四路 Replay 哈希逐月一致」，再铺其它系统。
2. **真实地球 MVP 区域精算必须能消费 `region-presets.json`**（B4 数据契约，切片内即兑现）。
3. **存档 = ADR-004 选项 1**：全量二进制快照 + 命令日志 + LOD 分块 + delta（Replay 路径④ 依赖其往返）。
4. **CI = ADR-003 选项 A**：GitHub Actions + Unity CLI，Gate-0 自动门禁（B2 锁版本 + B3 哈希门禁）。

---

## 1. Epic 总览（建议实装顺序）

| Epic | 名称 | 范围 | 关键依赖 | 建议 Sprint | 2026-08-14 注记 |
|------|------|------|---------|------------|-----------------|
| **Epic 0** | **确定性垂直切片（Gate-0 门）** | SimOrchestrator + S4 双频混合结算 + 四路 Replay + region-presets 消费 + CI 门禁 | 无（地基） | **Sprint 1（Must）** | **Done** — Sprint 01 结项 |
| Epic 1 | S1 干预系统 | IInterventionTarget 注册 / 异步 pending / InterventionLog / 紧急干预 | Epic 0（确定性核心） | Sprint 2 | **代码已部分落地**；Sprint 02 主攻 VS-8，非重做干预 |
| Epic 2 | S2 生态模拟引擎 | WorldTile 生态态 / 11 步月结 / 地貌季节 / 灾害预警 | Epic 0 | Sprint 3 | **Sprint 03 CLOSED** — Must S2-1/2 已收口（PR #9） |
| Epic 3 | S3 文明发展 | 聚落成长+尺度层级 / 16 步月结 / 经济科技个体 / 国家聚合 | Epic 0, Epic 2 | Sprint 3–4 | **Sprint 03 CLOSED Must**；S3-3/4/5 → Sprint 4–5 |
| Epic 4 | S4 时间表现层 | 速度控件 / 时间轴 HUD / 尺度跃迁联动（时间核心已在 Epic 0） | Epic 0 | Sprint 2 | **代码已部分落地**（Formal HUD / 变速） |
| Epic 5 | S5 世界地图（完整管线） | Natural Earth 导入 / 生物群系 / LOD 分块 / 双开局模式（region 消费在 Epic 0） | Epic 0 | Sprint 4 | **代码已部分落地**（geo-v1 + S5-4 双开局） |
| Epic 6 | 表现层（Runtime/Presentation/Camera） | SimulationRunner 胶水 / NPR 渲染 / 表现插值 / 相机 LOD | Epic 0 | Sprint 2–5 | VS-8 已收；**P3 Civ/Eco 采样已收**（PR #9） |
| Epic 7 | 存档（S10，完整） | LOD 分块序列化 / delta 增量 / 异步延迟装载（快照往返已在 Epic 0） | Epic 0 | Sprint 4 | 待排 |
| Epic 8 | 测试基础设施（持续） | 单元/集成/性能回归；Gate-0 门禁维护；确定性契约演进 | 贯穿 | Sprint 1+ 持续 | 进行中 |

> **说明**：Epic 0 内的 `序列化往返` 与 `真实地球 region 消费` 是 Gate-0 路径④与 B4 的最小兑现；Epic 7 / Epic 5 再补全「完整 LOD 分块 / 全分辨率数据管线」。
> **Sprint 02**：`production/sprint-02-plan.md` — 已结项。  
> **Sprint 03**：`production/sprint-03-plan.md` — 已结项。  
> **Sprint 04**：待开 — Epic 5 S5-1/2 + Epic 7 SV1 + S3-3/5（§11 原排期）。
---

## 2. Epic 0 — 确定性垂直切片（Gate-0 门）【Sprint 1 · Must】

> **本 Epic 是 P0 第一道里程碑门。完成判据 = `gate0` CI 全绿（四路 Replay ≥120 游戏月哈希逐月一致）+ region-presets 被消费 + 真实地球 MVP 区域精算可初始化。**
> 为在切片内即可触发「周级子结算通道」与「时代过渡」，切片内包含**最小 S2/S3 确定性桩**（1 聚落 + 少量物种 + 可编排的战事/灾害/建设干预），**仅够产生月/周事件，不实现完整机制**——完整机制在 Epic 2/3。

### Story V0-1 — 模拟核心 Assembly 边界与零 CoreModule 静态检查
- **优先级**：Must ｜ **映射**：ADR-001（C 数据导向）/ 控制清单 §D.4
- **需求追溯**：架构 §1（确定性边界红线）/ §8.1（14 个 asmdef）
- **验收标准**：
  1. 创建 `WorldSim.Simulation.Core / Ecology / Civilization / Intervention / Time / WorldMap` 等 asmdef，**全部仅引用 `UnityEngine.Mathematics` + `System.*`，CI 静态检查零 `UnityEngine.CoreModule`（GameObject/MonoBehaviour/Transform/Time）引用**。
  2. 静态检查以 Editor 脚本 + CI 步骤形式落地（`tests/ci` 或 `WorldSim.Editor` 内），失败即阻断合入。
  3. 空 WorldState 聚合根 + 空 SimOrchestrator 可 headless 编译并在 EditMode 实例化。
- **测试证据**：`tests/ci/asmdef-boundary-check`（静态引用扫描）；CI 步骤 `check-sim-asmdef`。

### Story V0-2 — 确定性数学基座（Quantize / Fix / 确定性哈希 / RNG 分流）
- **优先级**：Must ｜ **映射**：B3 / G0-4 / G0-5 / G0-7 / ADR-002（选项 2）
- **需求追溯**：架构 §4.2（RNG 分流 xoshiro256**）/ §4.5（Quantize + Fix 兜底）/ §4.3（确定性哈希 **FNV-1a-64**）/ S4 §7.3 铁律 4、5
- **验收标准**：
  1. `Quantize(double/decimal, int decimals)` 与 `Fix`（Q 格式 32.32）落于 `WorldSim.Simulation.Core.Math`；关键累加量写回前经 `Quantize`。
  2. `RngRegistry`：每子系统独立流 `streamId = Hash(worldSeed, systemTag)`；PRNG 用 xoshiro256**（禁用 `System.Random`）；每条流 **256-bit** 全状态可（反）序列化。
  3. `DeterminismHash`：**FNV-1a-64** over **确定性字节流**（显式小端、固定字段顺序、集合先排序后写）；**禁用 `string.GetHashCode`**；xxHash 未实现。
  4. `DeterminismHash` 与 `Quantize` 输出在 PC x64 同 Unity/Burst 下逐位一致（单测覆盖）。
- **测试证据**：`WorldSim/Assets/Scripts/Tests/Unit/QuantizeTests.cs`、`RngStreamTests.cs`、`tests/contracts/determinism-contract.md`。

### Story V0-3 — SimOrchestrator + S4 双频混合结算时间驱动
- **优先级**：Must ｜ **映射**：G0-1 / G0-2 / G0-3 / ADR-001
- **需求追溯**：架构 §3.2–3.5（双频累加器 / 边界时间戳升序合并 / 稳定 ID 排序）/ S4 §2.2, §2.7, §7.3 铁律 1–3
- **验收标准**：
  1. `TimeDriver{gameClock, monthAccumulator, weekAccumulator, speedMultiplier, paused}`；`Update(dtReal)` 用 §3.3 修正伪代码：**按边界时间戳升序合并，同刻 week 先 month 后**。
  2. **关键确定性约束**：周/月边界必须从**整数月/周序号**派生（`nextBoundary = baseTime + index * STEP`），**禁止**用 float 累加器减法在循环里逐步逼近——否则 1×/20× 长程 drift 导致边界错位（见 §7 新 P0 风险 R-N1）。
  3. `activeEntities` 及所有聚落/政体/物种集合遍历前 `SortedByStableId`（铁律 3）；`HashSet` 迭代序不得直接进逻辑。
  4. `SimOrchestrator` 月级大账顺序对齐 S3 §4.3（16 步）+ S2（11 步）+ S1（干预结算）；末步重算 `activeEntities`。
  5. 切片内最小 S2/S3 桩：1 聚落 + 少量物种 + 月/周事件产出，足以在 ≥120 月内触发 ≥1 时代过渡、≥1 战事、≥1 灾害（覆盖周级通道）。
- **测试证据**：`WorldSim/Assets/Scripts/Tests/Unit/SimOrchestratorBoundaryTests.cs`（1× 与 20× 产出**完全一致**的边界/事件序列）、`StableIdOrderingTests.cs`。

### Story V0-4 — 序列化往返（ADR-004 全量快照 + 命令日志）
- **优先级**：Must ｜ **映射**：B3 / G0-4（RNG 状态入档）/ ADR-004（选项 1）
- **需求追溯**：架构 §6.1–6.3（分层 / 全量二进制 / 命令日志）/ S4 §7.3 Replay 路径④
- **验收标准**：
  1. `WorldState` 全量 → 二进制（显式小端自定义 writer，**禁 `BinaryFormatter`**）；所有字典/集合**排序后写**（保确定序）。
  2. 序列化含：`WorldState` 全量 + `RngRegistry` 全状态 + `gameClock`/累加器 + `moduleToggles` + `InterventionLog`。
  3. 读档恢复**逐位一致**状态；续跑演化与无存档路逐月哈希一致（Gate-0 路径④成立）。
  4. `InterventionLog`（按游戏月时间戳）随档保存，作为 Replay 输入。
- **测试证据**：`WorldSim/Assets/Scripts/Tests/Unit/SerializationRoundTripTests.cs`（往返后 `DeterminismHash` 不变；四路之一用存读档）。

### Story V0-5 — Gate-0 四路 Replay 测试台
- **优先级**：Must ｜ **映射**：B3 / G0-6 / G0-7 / G0-8（钩子前置）
- **需求追溯**：架构 §4.3（四路对跑规范）/ S4 §7.3 Gate-0 测试规范 / 控制清单 G0-6
- **验收标准**：
  1. 同 `worldSeed` + 同 `InterventionLog`：①全程 1× ②全程 20× ③中途变速（1×→20×→1×，含多次暂停）④中途存档→退出→读档续跑。
  2. 时长 **≥120 游戏月**，含 ≥1 时代过渡 + ≥1 战事 + ≥1 灾害。
  3. 每月级大账结束对关键指标（各 `Polity.population`/总产出/总军力/稳定度、各 `Species.population`、`Resource.currentAmount`、科技层级、`RngRegistry` 全状态）**先 Quantize 再 DeterminismHash**，逐月比对四路序列。
  4. 任一月分叉即失败，并输出**首个分叉月**用于定位；断言无分叉。
  5. 测试以 EditMode headless 运行；CI 跑**全量** `WorldSim.Tests`（不再用过窄 `-testFilter Gate0Determinism`）。
- **测试证据**：`WorldSim/Assets/Scripts/Tests/Gate0/Gate0DeterminismTest.cs`（四类 SpeedProfile + 统一哈希比对器）。

### Story V0-6 — 真实地球 MVP 区域精算：消费 `region-presets.json`
- **优先级**：Must ｜ **映射**：B4 / ADR-004（region 契约）/ S5 §2.2.2
- **需求追溯**：架构 §6.5（region-presets 数据契约）/ S5 §2.2.2 起始区域预设表 / `design/gdd/data/region-presets.json`
- **验收标准**：
  1. Editor/初始化器读取 `region-presets.json`（schemaVersion 1.0，6 预设：fertile_crescent / yellow_yangtze / nile / mediterranean_europe / indus_ganges / mesoamerica）。
  2. 预设 → `WorldInitConfig`：`center/radiusDeg` → `startRegionCenter/Radius`；`ethnicSeed` → `RealEthnicDistribution`（地缘模式空间映射）；`legalFamilyDefault` → `legalTraditionSeed` **偏置（绝不指定单国家族）**。
  3. **MVP 区域精算**：按 (lat,lon,radius) 初始化 High 精度起始区域网格；**Epic 0 切片用公式高程/生物群系（非真实 DEM / 非 Natural Earth 1:50m）**；完整海岸线+DEM 管线归 Epic 5 / S5-1。
  4. **红线落地**：空间映射只给「偏置/种子」，不得为任一国家/聚落指定具体 `lawFamily`/`ethnicGroup`（B5 红线前置校验，单测覆盖）。
- **测试证据**：`WorldSim/Assets/Scripts/Tests/Unit/RegionPresetConsumptionTests.cs`（消费契约 + 红线断言）。

### Story V0-7 — 三级回退钩子（不强制启用）
- **优先级**：Could ｜ **映射**：G0-8 / ADR-002（回退路径）
- **需求追溯**：架构 §4.4（三级回退）/ S4 §7.3 回退表
- **验收标准**：
  1. 实现三档切换钩子：回退1 收窄速度档（去 20×）/ 回退2 pass 内全串行+关键量 `Fix` / 回退3 确定性 lockstep（步间不接收输入）。
  2. 默认均不触发；仅当 Gate-0 分叉时由人工/CI 显式降级，绝不退回回合制。
- **测试证据**：钩子存在性 + 配置位单测（逻辑不强制跑通）。

### Story V0-8 — CI 固定版本锁（B2）
- **优先级**：Must ｜ **映射**：B2 / ADR-003
- **需求追溯**：控制清单 B2 / 架构 §8.4 / `docs/unity-setup-complete.md`（Unity 6000.0.81f1）
- **验收标准**：
  1. CI 锁定 **Unity 6000.0.81f1**（编辑器官方版本号 env 固定，非 `latest`）。
  2. **Burst 版本锁**：**已落地** — `Packages/manifest.json` 直接依赖 `com.unity.burst: 1.8.30`；CI `assert-burst-pinned.ps1` + `version-pins.json` 断言（B2/B8）。
  3. 编辑器版本与 manifest 声明一致，CI 首次配置可复现。
- **测试证据**：`.github/workflows/gate0.yml` 的 `pin-versions` 步骤 + manifest 断言。

### Story V0-9 — Gate-0 CI 自动门禁（B3）
- **优先级**：Must ｜ **映射**：B3 / ADR-003（选项 A）
- **需求追溯**：控制清单 G0 全项 / 架构 §8.4（headless 全量 EditMode，分叉即红）
- **验收标准**：
  1. `.github/workflows/gate0.yml`：`pin-versions` → self-hosted Windows 全量 `-assemblyNames WorldSim.Tests` EditMode → **哈希分叉则 CI 红、阻断合入**（`shell: powershell`，勿依赖未装的 `pwsh`）。
  2. 门禁项 G0-1~G0-8 全部由 V0-1~V0-5/V0-7 的测试覆盖并接入 CI。
  3. gate0.xml 作为 artifact 上传；首个分叉月信息进入日志。
- **测试证据**：`.github/workflows/gate0.yml`（见交付物 3）。

> **Epic 0 完成门（Sprint 1 出口）**：V0-1~V0-6、V0-8、V0-9 全绿 ⇒ Gate-0 通过 ⇒ 解锁 Epic 1–7 实装。V0-7 为 Could，可顺延。

---

## 3. Epic 1 — S1 干预系统

> 干预是玩家**唯一**写入入口（架构 §2.7 红线）。参数注册接口在 V0-2/V0-3 已留 `IInterventionTarget` 骨架。

### Story S1-1 — IInterventionTarget 参数注册与 ApplyIntervention
- **优先级**：Must ｜ **映射**：ADR-001 / S1 §4.1 / 架构 §9.3
- **验收标准**：S2 注册 `rainfall_/temperature_/birthRate_/population_/regenRate_`；S3 注册 `devBias_{agriculture|hunt|defense|trade|faith|military|ethnicity|culture}_{sid}` + `foodReserveCoeff_/techUnlockBoost_/happinessMod_*`；`RegisterInterventionParameter(key,def,min,max)` / `ApplyIntervention(key,delta,durationMonths)` / `GetParameterValue(key)` 实现；不可干预派生状态（Era/legitimacy/LawStage/GovernanceType/EthnicComposition/LawFamily/InstitutionProfile）**拒绝注册**（断言）。
- **测试证据**：`tests/unit/InterventionParameterTests.cs`（注册/范围钳制/红线拒绝）。

### Story S1-2 — 异步延迟 pending 队列 + InterventionLog
- **优先级**：Must ｜ **映射**：B3 / S4 §2.3 / 架构 §2.1
- **验收标准**：干预进入 `pendingQueue`，按声明延迟（游戏月）在后续月级大账生效，写 `pendingDelta`/`devBias_*`；每次生效记录 `InterventionLog{gameMonth, action}`（游戏月时间戳，非现实时间）；日志随档序列化（V0-4）。
- **测试证据**：单测断言同 `InterventionLog` 在同 seed 下必然复现同演化（Replay 等价）。

### Story S1-3 — 紧急干预冷却与衰减
- **优先级**：Should ｜ **映射**：S1 §2.3 / S4 §2.3
- **验收标准**：紧急干预（天降甘霖/神佑护盾/生命之泉）24 游戏月冷却，由 S4 时钟驱动递减；`devBias_*` 3–5 月衰减曲线实现。

### Story S1-4 — 干预落点生态响应（表现/逻辑桥）
- **优先级**：Could ｜ **映射**：S1 §5 / 架构 §5.4
- **验收标准**：资源投放落点即时生成、生态响应随 pass 渐变；因果链节点以游戏时间戳锚定（供 S6/S8 呈现）。

---

## 4. Epic 2 — S2 生态模拟引擎

### Story S2-1 — WorldTile 生态态 + 种群/食物链/资源/稳态区间
- **优先级**：Must ｜ **映射**：S2 §2 / 架构 §9.4
- **验收标准**：`Species/ FoodChainLink/ RenewableResource/ HomeostasisZone/ EcologicalIndicator` 数据模型落地；稳态区间（stableLower~Upper, criticalLower~Upper, equilibrium, selfRepairRate）实现；实现 `IInterventionTarget`（rainfall/temperature/birthRate…）。
- **测试证据**：单测稳态自修复（扰动后回到 equilibrium，不依赖帧序）。

### Story S2-2 — 11 步生态月结流水线
- **优先级**：Must ｜ **映射**：G0-2（顺序）/ S2 §4.3 / 架构 §3.4(2)
- **验收标准**：11 步（应用 S1 参数→季节→植物/食草/食肉→资源再生→地貌→稳态→相变→指标→事件）按固定序在 SimOrchestrator 月级大账内执行；稳定 ID 序遍历。
- **测试证据**：流水线与 V0-3 边界合并协同的单测。

### Story S2-3 — 地貌演变 + 季节推进
- **优先级**：Should ｜ **映射**：S2 §2.6 / S4 §2.1（每 3 月切季）
- **验收标准**：季节由累计游戏月推算（3 月=季，12 月=年）；地貌演变速率乘以 `EraState.ecoImpactCoefficient`。

### Story S2-4 — 灾害/应力预警 + 前兆
- **优先级**：Should ｜ **映射**：R3 / S2 §6
- **验收标准**：越过稳态临界触发相变 + 灾害前兆事件（供 S6/S8 预警）；`stressMonths` 计数。

---

## 5. Epic 3 — S3 文明发展

### Story S3-1 — 聚落成长 + 人口尺度层级
- **优先级**：Must ｜ **映射**：S3 §2.1 / 架构 §2.1（村/镇/市/都市圈，不封顶 5 万）
- **验收标准**：`Settlement` 分档（村~都市圈）；承载力 = min(住房,粮储,空间)；阈值解锁 zone；人口受食物盈余驱动；选址约束读 `IWorldGeography`（邻近 water/坡度）。
- **测试证据**：单测成长曲线在固定 seed 下确定；尺度跃迁不破坏聚合。

### Story S3-2 — 16 步文明月结流水线
- **优先级**：Must ｜ **映射**：G0-2 / G0-3 / S3 §4.3 / 架构 §3.4(3)（因果序：法律⑪先于政治⑫、族群⑩在文化⑨后法律⑪前、时代⑭末位）
- **验收标准**：16 步按固定序执行；稳定 ID 序遍历所有聚落/政体；末位国家聚合（Σ 聚落）。
- **测试证据**：单测因果序不可交换（交换 ⑪/⑫ 必分叉断言）。

### Story S3-3 — 经济/科技/个体层
- **优先级**：Should ｜ **映射**：S3 §2.2/2.3/2.6
- **验收标准**：`Economy`（五种资源 + 分工 depth + 交换形态演进）；`TechNode` 七主干线累积解锁；`Individual` 连续生命周期（ageMonths、死亡/继承视作开关，Sprint 5 深化）。

### Story S3-4 — 政治/法律/族群/军事子系统（核心层）
- **优先级**：Should ｜ **映射**：S3 §2.4/2.9/2.11/2.10 / R11/R12
- **验收标准**：合法性四项世俗来源；`LawFamily` 枚举（地缘模式种子，沙盒涌现）；族群双路径（地缘种子/沙盒涌现，MVP 折叠单主导族群）；军事/战争自动结算（玩家仅注入 `devBias_military`，不下场）。
- **测试证据**：族群 MVP 折叠断言 + 双模式行为一致（R12）。

### Story S3-5 — 国家/政体聚合（Σ 聚落）
- **优先级**：Should ｜ **映射**：S3 §2.12 / 架构 §7.4（成本绑定聚落数而非人口数）
- **验收标准**：`Polity.totalPopulation = Σ settlements.population`；聚合统计不逐人模拟；TitleTier × ScaleTier × DominionMode 三轴；红线：不指定民族/法律。

---

## 6. Epic 4 — S4 时间表现层（时间核心已在 Epic 0）

### Story S4-1 — 速度档 UI 控件 + 时间轴 HUD
- **优先级**：Must ｜ **映射**：S4 §2.1 / S8 §4
- **验收标准**：暂停 + 1×/2×/5×/20× 控件；时间轴（游戏年/季/月）；仅暴露 pause/speed 给 UI（架构 §2.3 UI 不持有状态）。

### Story S4-2 — 尺度跃迁 LOD 联动相机
- **优先级**：Should ｜ **映射**：S4 §2.4 / S9
- **验收标准**：聚焦聚落→微观模拟，拉远→聚合统计；相机焦点联动 LOD（表现层，不回写逻辑态）。

### Story S4-3 — 世代传承连续生命周期（表现）
- **优先级**：Could ｜ **映射**：S4 §2.5 / S6
- **验收标准**：死亡/继承事件以游戏时间戳锚定；文明级世代节点作为叙事里程碑（S6 消费，不回写）。

---

## 7. Epic 5 — S5 世界地图（完整数据管线）

> **注意**：`region-presets.json` 消费与 MVP 区域精算已在 **Epic 0 V0-6（B4 最小兑现）**；本 Epic 补全全分辨率真实地球管线。

### Story S5-1 — 真实地球数据导入管线
- **优先级**：Must ｜ **映射**：B4 / S5 §2.1 / WM2
- **验收标准**：Natural Earth 1:50m 海岸线/河湖 + 低精度高程（ETOPO1/NASADEM 简化）+ 简化 Köppen 气候 → `WorldTile[,]`（equirectangular 投影，latIdx/lonIdx）；缺失数据以邻近插值/纬度气候默认填充，标 `lod=Low`。
- **测试证据**：单测投影映射正确性 + 抽样 biome 与真实吻合（≥80% 抽样，S5 V1）。

### Story S5-2 — 生物群系推导 + 选址可行性 + 自然边界
- **优先级**：Should ｜ **映射**：S5 §2.5 / S3 §2.1
- **验收标准**：`biome = f(elevation, latitude, climate)` 映射表；选址判定（邻近 water/坡度阈值/可居群系）；河流/山脉作为自然边界；沿海 `hasCoast` 解锁海军。

### Story S5-3 — LOD 分块（High/Mid/Low）+ 异步延迟装载
- **优先级**：Should ｜ **映射**：WM2 / 架构 §6.4 / ADR-004（LOD 分块）
- **验收标准**：High 区逐 tile；Mid/Low 聚合压缩；读档先载 High + 焦点 Mid，远域 Low 异步延迟装载（不阻塞逻辑）。

### Story S5-4 — 双开局模式（远古沙盒 / 当今地缘政治）
- **优先级**：Could ｜ **映射**：S5 §2.2 / R10
- **验收标准**：两模式仅差异在 `WorldInitConfig`（谁初始化/时代起点/国界年份 `borderYear`）；共享同一 `IWorldGeography`；`GeoPoliticalInit`（真实国界多边形 + 城市）初始化 S3 `Polity`。

---

## 8. Epic 6 — 表现层（Runtime / Presentation / Camera）

### Story P1 — SimulationRunner 胶水
- **优先级**：Must ｜ **映射**：架构 §2.2 / §8.1
- **验收标准**：`SimulationRunner`(MonoBehaviour) 从 `Update(dtReal)` 取真实帧时间→交 `TimeDriver`→收集本帧 `SimEvent`/快照→派发 Unity 事件总线；零逻辑计算在 MonoBehaviour 内。

### Story P2 — NPR 微缩沙盘渲染（低多边形 + 手绘纹理 + URP）
- **优先级**：Should ｜ **映射**：概念 5.1 / 架构 §2.2（全时代统一画风）
- **验收标准**：由林绘澄美术圣经驱动；不随地理模式切换；渲染只读 `WorldView` 快照。

### Story P3 — 表现插值（边界间平滑，不回写逻辑态）
- **优先级**：Should ｜ **映射**：架构 §2.2 / §3.6（插值不回写）
- **验收标准**：个体位置/资源视觉量/相机在月周边界间插值；插值结果**绝不回写** `WorldState`（确定性红线）。

### Story P4 — 相机缩放/平移 LOD 联动
- **优先级**：Could ｜ **映射**：S9 / S4 §2.4
- **验收标准**：相机缩放/平移读 LOD 决定渲染精度；地球尺度平滑过渡。

---

## 9. Epic 7 — 存档（S10，完整）

> **快照往返已在 Epic 0 V0-4（Replay 路径④ 依赖）**；本 Epic 补全 LOD 分块 / delta / 异步装载。

### Story SV1 — LOD 分块序列化 + delta 增量
- **优先级**：Should ｜ **映射**：ADR-004 / 架构 §6.3–6.4 / WM2
- **验收标准**：High 区逐 tile 全量，Mid/Low 聚合压缩；历史层/高频生态指标 delta 追加；`schemaVersion` 迁移预留。

### Story SV2 — 异步延迟装载（不阻塞逻辑）
- **优先级**：Could ｜ **映射**：架构 §6.4
- **验收标准**：读档先载 High + 焦点 Mid，远域 Low 异步装载；逻辑 tick 不被 IO 阻塞。

---

## 10. Epic 8 — 测试基础设施（持续）

- **T1**（Must）：维护 Gate-0 四路 Replay 作为常驻 CI 门禁（V0-5/V0-9 已建，持续演进指标集）。
- **T2**（Should）：单元回归随各 Epic 增长（`tests/unit/` 按子系统分组）。
- **T3**（Should）：性能回归——核心层全开月级 pass 预算 <50ms（B6，需 profiler 实测，回退2 杠杆）。
- **T4**（Could）：跨平台 Replay（Mac/ARM）触发 `Fix` 全局切换的回归（ADR-002 兜底）。

---

## 11. 冲刺边界建议（Sprint Plan）

| Sprint | 范围 | 出口判据 |
|--------|------|---------|
| **Sprint 1（Gate-0 垂直切片）** | **Epic 0 全 Must（V0-1~V0-6, V0-8, V0-9）** | Gate-0 CI 全绿（四路 ≥120 月哈希逐月一致）；region-presets 被消费；真实地球 MVP 区域可初始化 |
| Sprint 2 | Epic 4（S4-1）+ Epic 1（S1-1/2）+ Epic 6（P1）+ Epic 8（T1/T2 启动） | 单聚落可暂停/变速/干预，UI 可见时间轴；干预参数可注册与延迟生效 |
| Sprint 3 | Epic 2（S2-1/2）+ Epic 3（S3-1/2）+ Epic 6（P3） | 月级大账跑完整 S2(11)+S3(16) 流水线；表现插值不回写；确定性仍绿 |
| Sprint 4 | Epic 5（S5-1/2）+ Epic 7（SV1）+ Epic 3（S3-3/5） | 全分辨率真实地球网格；完整存档 LOD 分块；国家聚合 |
| Sprint 5 | Epic 3（S3-4）+ Epic 5（S5-3/4）+ Epic 6（P2/P4）+ Epic 8（T3/T4） | 核心层全开；性能预算验证；双开局模式；叙事/相机联动 |

> Sprint 1 **必须是 Gate-0 垂直切片**——主理人指令硬性要求先跑通确定性核心再铺系统。任何 Sprint 1 范围蔓延（提前做 S2/S3 完整机制）都违反 R13 P0 精神。

---

## 12. B2 / B3 / B4 排期（明确落点）

> 三者均为 Phase 4 **入口条件**（控制清单 C 类 / D 类），必须在本文件落到具体 Story 与依赖。

### B2 — CI 锁定同 Unity 版本 + 同 Burst 设置
- **负责 Story**：**V0-8（Must）** + V0-9（CI 门禁载体）。
- **依赖**：无前置；但依赖 `docs/unity-setup-complete.md` 已确认的 Unity 6000.0.81f1。
- **关键阻塞（见 §7 R-N2）**：**已闭环** — `Packages/manifest.json` 直接依赖 `com.unity.burst: 1.8.30`；CI `assert-burst-pinned` + `version-pins.json` 断言。
- **验收标准**：CI env 固定 `UNITY_VERSION=6000.0.81f1`；`manifest.json` 声明 `com.unity.burst@1.8.30`；CI 步骤 `assert-burst-pinned` 通过；Gate-0 在同版本下复现。
- **排期**：Sprint 1 第 1 周（与 V0-9 同批进 CI）。

### B3 — Quantize + 确定性指标哈希 + Gate-0 测试入 CI
- **负责 Story**：**V0-2（Quantize/Fix/哈希/RNG）** + **V0-4（序列化，RNG 状态入档）** + **V0-5（四路 Replay 测试台）** + **V0-9（CI 门禁）**。
- **依赖**：V0-2 → V0-4 → V0-5 → V0-9（流水线顺序）。
- **验收标准**：`Quantize` + `DeterminismHash`（**FNV-1a-64**，禁 `string.GetHashCode`）落地并被 V0-5 调用；四路 Replay ≥120 月逐月哈希一致；G0-1~G0-8 全部由测试覆盖并接入 CI；分叉即红、阻断合入。
- **排期**：Sprint 1 第 1–2 周（数学基座 V0-2 先就位，再串 V0-4/5/9）。

### B4 — 真实地球数据获取/导入管线 + region-presets 消费
- **负责 Story**：**V0-6（Must，切片内最小兑现：消费 region-presets.json + MVP 区域精算）** + **Epic 5 S5-1（Must，全分辨率管线）** + S5-3（LOD 分块）。
- **依赖**：V0-6 无前置（仅读 JSON + 简化真实数据）；S5-1 依赖 Epic 0 的 `WorldTile` 结构与 `IWorldGeography`。
- **验收标准**：`region-presets.json`(schemaVersion 1.0, 6 预设) 被消费 → `WorldInitConfig`（center/radius/ethnicSeed/legalTraditionSeed 偏置）；MVP 区域 High 精度初始化；B5 红线（绝不指定单国家族）在导入器单测覆盖；S5-1 全分辨率 Natural Earth 导入 + LOD 分块。
- **排期**：V0-6 在 **Sprint 1（与 Gate-0 同批，B4 入口条件）**；S5-1/S5-3 在 **Sprint 4**。

> **B2/B3/B4 关系**：B3（确定性门禁）是三者内核，B2（版本锁）是 B3 可复现的前提，B4（真实地球）是 MVP 真实地图的入口。三者都在 Sprint 1 启动，B2/B3 于 Sprint 1 出口闭环，B4 的「消费契约」于 Sprint 1 闭环、「完整管线」于 Sprint 4 完成。

---

## 13. 跨 Epic 确定性契约（单一真相源）

- **确定性契约文档**：`tests/contracts/determinism-contract.md` —— 定义指标哈希集合、Quantize 精度、哈希算法、遍历排序规则，以及 G0-1~8、B2/B3/B4 到测试/CI 的映射。所有 Epic 的确定性相关验收均以该契约为准。
- **不可违背的铁律**（架构 §4.1，任何 Epic 的 PR 不得违反）：固定步长唯一时间源 / 双频按边界时间戳升序合并 / 稳定 ID 排序遍历 / RNG 分流入档 / 禁不确定并发与浮点漂移。
- **红线**（架构 §2.7）：模拟核心零 `UnityEngine.CoreModule`；UI 不持有游戏状态；干预唯一入口；地理只读；S6/S8 事件只读派生。

---

## 14. 假设与待主理人确认项

1. **切片内的「最小 S2/S3 桩」粒度**：本文假设切片用 1 聚落 + 少量物种 + 可编排战事/灾害即可满足「≥1 时代过渡 + ≥1 战事 + ≥1 灾害」触发周级通道；若需更真实触发条件，Epic 2/3 的 Must Story 可能需前移——请主理人确认切片桩范围。
2. **Burst 版本**：**已闭环** — `com.unity.burst: 1.8.30` 直依 + CI pin（原 R-N2）。
3. **量化精度（Quantize decimals）**：各指标的具体小数位需在 V0-2 实现时由 playtest 校准（过粗丢信息、过细仍漂移）；初值建议人口/军力/产出 Quantize 到整数或 1 位、稳定度/资源到 3 位——以 `determinism-contract.md` 为可调中心。
4. **CI Runner 形态**：`.github/workflows/gate0.yml` 按「自带 Unity + 预装 6000.0.81f1 的 self-hosted Windows runner」撰写；runner 当前可手动 `run.cmd` 接单（装服务另议）。

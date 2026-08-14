---
项目名: WorldSim
文档名: Phase 4 预制作 · Sprint 04 计划（全分辨率地球消费 + 存档 LOD + S3-3/5）
版本: v1.1.0
日期: 2026-08-14
作者: 游承峰（主理人）
阶段: Phase 4 — 预制作
输入文档:
  - production/phase4-gate.md
  - production/sprint-03-plan.md（已结项）
  - production/epics/worldsim-epics.md（S5-1/2、SV1、S3-3/5）
  - design/gdd/world-map-generator.md §2.1 / §2.5
  - design/gdd/civilization-system.md §2.2 / §2.3 / §2.6 / §2.12
  - docs/architecture/adr/ADR-004-serialization-lod.md
  - tests/contracts/determinism-contract.md
状态: 已结项（2026-08-14）— PR #12 合入 main；Gate-0 2/2 绿；下一冲刺见 epics §11 Sprint 5
---

# Phase 4 预制作 · Sprint 04（地球消费 + 存档 LOD + 国家聚合）

> **结项说明（2026-08-14）**：A4-1~A4-6 已在 `cursor/sprint04-earth-lod` 落地并经 PR #12 合入 `main`@`38850fb`。Gate-0 证据：https://github.com/niunaguo0-pixel/worldsim/actions/runs/31795841296（PR）/ https://github.com/niunaguo0-pixel/worldsim/actions/runs/31796139708（main 合并后）。本地 EditMode **266/266**。下一冲刺：S3-4 核心层 + P2/P4 + T3/T4（S5-3/4 已落地，见 `production/epics/worldsim-epics.md` §11 Sprint 5）。

## 1. 目标（Why）

Sprint 03 已收口 S2(11)+S3(16) 月账与 P3。Sprint 04 把 **已锁定的 geo-v1 真实地球** 与 **Schema 9 LOD/delta codec** 接到可玩与可存路径上，并加深 **S3-3 经济/科技/个体** 与 **S3-5 政体 Σ 聚合**，使「全分辨率网格可结算、存档按 LOD 压缩、国家是聚落之和」可测且 Gate-0 仍绿。

本 Sprint **不重做导入管线**。Natural Earth v5.1.2 + ETOPO 2022 60″ + Köppen V3 已钉死为 `geo-v1-5f31a1c377dc947d`（High 720×360 / Mid 360×180 / Low 180×90）。缺口在消费、选址/自然边界入文明、codec 接入存读档、以及经济七线/三轴聚合从薄步变成可验收逻辑。

## 2. 范围边界（In / Out）

| In（本 Sprint 必须） | Out（本 Sprint 不做） |
|---|---|
| S5-1 **消费** High 格：可玩开局走 geo-v1 High（区域精算仍裁剪）；`biome-probes` ≥80% 抽样锁定 | 新 geo 源 / 重跑 `build_geo.py` / 把 High 提到 60″ 原生格 |
| S5-2 选址可行性接入建城；河/山/岸作自然边界；沿海 `hasCoast` 解锁海军 | S5-3 异步 LOD 装载（已落地 `WorldMapLodStreamer`）/ S5-4 双开局（已可玩） |
| SV1：`LodOverrideCodec` + `HistoryDeltaCodec` 接入存读档；High 全量 / Mid-Low 聚合往返；schema 迁移预留可测 | SV2 远域异步装载打磨（与 S5-3 重叠，归 Sprint 5） |
| S3-3 五种资源月结 + 交换形态演进 + 七主干线累积；个体 `ageMonths` 生命周期可哈希 | S3-4 政治/法律/族群/军事核心层全开（Sprint 5） |
| S3-5 `Polity.population = Σ settlements`；成本绑聚落数；TitleTier × ScaleTier × DominionMode | 粒子预算 UI / AX-2 / P2·P4 相机联动 / 删历史功能分支 |

## 3. Story 清单

| Story | 简述 | 验收 | 追溯 | 结项 |
|---|---|---|---|---|
| **A4-1** | S5-1 全分辨率消费 | 可玩路径读 `StreamingAssets/Geo/v1` High；缺数据格 `lod=Low` 或 `IsInterpolated`；探针抽样与真实吻合 ≥80%；**不改** `sources.lock.json` / `buildId` | epics S5-1 / B4 | ✅ |
| **A4-2** | S5-2 选址 + 自然边界 | 建城走 `SettlementSiteEvaluator`；`NaturalBoundaryClassifier` 约束政体邻接；`hasCoast` 解锁海军（可哈希）；坡度阈值沿用 0.5° 校准（>6° / 海拔>3500） | epics S5-2 / S5 §2.5 | ✅ |
| **A4-3** | SV1 LOD 存档 | 存档 High 逐 tile、Mid/Low 聚合；历史层 `HistoryDeltaCodec` 可追加；读档后路径④ Replay 仍绿；`schemaVersion` 8→10 迁移单测 | epics SV1 / ADR-004 | ✅ |
| **A4-4** | S3-3 经济科技个体 | `food/wood/stone/goods/energy` 均参与月结；`exchangeMode` 随分工演进；七主干线（农/猎/防/贸/信/军/文）可累积解锁；个体死亡/继承开关不破坏稳定 ID | epics S3-3 / S3 §2.2–2.6 | ✅ |
| **A4-5** | S3-5 国家聚合 | `population = Σ`；`aggregationCost ∝ 聚落数`（非人口）；三轴赋值（含 `DominionMode`）；聚合路径不写民族/法律 | epics S3-5 / 架构 §7.4 | ✅ |
| **A4-6** | 回归门禁 | EditMode 266/266；Gate-0 CI 2/2；`GATE0_MIN` 上调至 259/130 | Epic 8 / 契约 | ✅ |

## 4. 已有基座（勿重做）

- geo-v1 离线包：`high-global.wgeo.gz` / `mid-global.wgeo.gz` / `low-global.wgeo.gz` + `political-2026.wgeo.gz`；CI 校验 `buildId` 与 checksum
- `WorldGeography` / `WorldMapLodStreamer` / `SettlementSiteEvaluator` / `NaturalBoundaryClassifier`（Task 4 已按 0.5° 坡度校准）
- `WorldStateSerializer` Schema 10（9→10 加 `HasNavy`；仍可读 3–9）+ `LodOverrideCodec` + `HistoryDeltaCodec` 已接入 `SaveGameService`
- `StepEconomy` 五资源 + 交换形态；七科技线累积；`AggregatePolities` 赋 `DominionMode`；沿海海军入月哈希
- Gate-0 四路 Replay + `worldsim-pc` runner；本地 EditMode 基线 **266**

## 5. 验收标准（DoD）

1. ✅ 新档可玩路径消费 geo-v1 High；选址/自然边界/沿海海军可测；不改锁定源与 `buildId`。
2. ✅ 存读档：High 全量 + Mid/Low 聚合往返；历史 delta 可追加；路径④ 哈希仍一致。
3. ✅ 五种资源与七科技线进入月哈希；政体三轴可测；聚合成本随聚落数而非人口。
4. ✅ `main` tip Gate-0：**V0-8 + V0-9 = success（2/2）**。

## 6. 风险

| 风险 | 缓解 |
|---|---|
| 把 High 720×360 全量物化进每月哈希 → 超时/分叉 | 逻辑只哈希动态覆盖 + 聚落触及格；静态 bundle 以 `buildId` 入档 — **已验证绿** |
| 0.5° 坡度动态范围不足，旧 18°/20° 阈值永不触发 | 沿用 Task 4 校准（坡 6°/5° + 海拔硬门）— **建城/成长已改校准阈值** |
| Schema 9 往返与指标绝对值冲突 | 指标继续写量化绝对值；Schema 10 仅追加 `HasNavy` — **8/9 旧档仍可读** |
| 七科技线同时推进导致时代门误触发 | 七线可累积；不改已废除的 `requiredPopulation` — **已验证绿** |

## 7. 工程记录

分支：`cursor/sprint04-earth-lod`（已合入 `main`，PR #12）。

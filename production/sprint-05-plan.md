---
项目名: WorldSim
文档名: Phase 4 预制作 · Sprint 05 计划（S3-4 核心层 + P2/P4 + T3/T4）
版本: v1.0.0
日期: 2026-08-14
作者: 游承峰（主理人）
阶段: Phase 4 — 预制作
输入文档:
  - production/phase4-gate.md
  - production/sprint-04-plan.md（已结项）
  - production/epics/worldsim-epics.md（S3-4、S5-3/4、P2/P4、T3/T4、SV2）
  - design/gdd/civilization-system.md §2.4 / §2.9 / §2.10 / §2.11
  - design/art/art-bible.md（NPR 微缩沙盘）
  - docs/architecture/adr/ADR-002-determinism-rng.md
  - tests/contracts/determinism-contract.md
状态: Sprint 05 OPEN（文档门；代码另开 `cursor/sprint05-core-layer`）
---

# Phase 4 预制作 · Sprint 05（核心层全开 + 相机/叙事 + 性能预算）

## 1. 目标（Why）

Sprint 04 已收口 geo-v1 High 消费、SV1 存档 LOD、S3-3/5。Sprint 05 把 **S3-4 政治/法律/族群/军事** 从单测加深到**默认可玩核心层全开**，补 **SV2 读档异步** 与 **P2/P4 相机/叙事接线**，并在加深后重验 **T3 &lt;50ms** 与 **T4 Fix 切换**。Gate-0 仍须 2/2 绿。

本 Sprint **不重做** 已落地的 S5-3 构建路径、S5-4 双开局、NPR 色板/地球材质、`CameraLodPolicy`、`PerformanceBudgetTests` 骨架。S5-3/4 只补缺口。

## 2. 范围边界（In / Out）

| In（本 Sprint 必须） | Out（本 Sprint 不做） |
|---|---|
| S3-4：默认可玩走 `ecology.v2` + `civilization.v2` + 多聚落；合法性四来源 / LawFamily / 族群 MVP 折叠 / 战争自动结算可测 | 经济制度谱（Labor/Economy/Admin）新枚举；封贡网络深化（`DominionMode` 已有） |
| SV2 缺口：读档先 High + 焦点 Mid；远域 Low 异步、不阻塞逻辑 tick | 重写 `WorldMapLodStreamer` 构建路径；新 geo 源 / `buildId` |
| S5-4：只确认双开局差异仅在 `WorldInitConfig`（不重做 `GeoPoliticalInit`） | 粒子预算 UI / AX-2 / Comprehensive 可访问性 |
| P2：可玩沙盘只读 `PresentationWorldView`；全时代统一 NPR（不随远古/地缘切风格） | 重做色板 / `NprMaterialFactory` / TEX_DETAIL 探针 |
| P4：缩放/平移驱动渲染精度 + 焦点带 Mid；LOD **不回写** `WorldState` | 新相机系统；删历史功能分支 |
| T3：核心层全开后月级 pass 中位仍 &lt;50ms；回退 2 同预算。T4：同平台 `Fix` 切换单测（无 Mac runner） | Mac/ARM CI runner；跨机 Replay 宣称绿 |

## 3. Story 清单

| Story | 简述 | 验收 | 追溯 |
|---|---|---|---|
| **A5-1** | S3-4 核心层全开 | 新档默认可玩走 v2 + 多聚落；四项世俗合法性入月哈希且无宗教项；`LawFamily` 地缘锁定 vs 沙盒涌现；族群 MVP 单主导折叠 + R12 双模式结构一致；战争自动结算，玩家仅 `devBias_military`。已有 `CivilizationEpic3Tests` S34_* 加深到默认路径，不重写数据模型 | epics S3-4 / S3 §2.4/2.9/2.10/2.11 |
| **A5-2** | S5-3/SV2 缺口 + S5-4 核验 | 读档不阻塞逻辑 tick；远域 Low 可后至。S5-4：`StartEra`/`borderYear` 只进 `WorldInitConfig`，共享 `IWorldGeography`。不重做 High+焦点 Mid 的 Build 契约 | epics S5-3 / SV2 / S5-4 |
| **A5-3** | P2 NPR 可玩接线 | 可玩场景渲染只读 WorldView；远古/地缘同画风。已有 `NprDioramaPalette` / `WorldSim_NprEarth` 勿重做 | epics P2 / 架构 §2.2 |
| **A5-4** | P4 相机 LOD | 缩放/平移改网格精度与实体/聚合标签；哈希不变。已有 `CameraLodPolicy` 滞后与 `ApplyRenderLod` 勿重做；补焦点变化请求地图 Mid 带 | epics P4 / S4-2 |
| **A5-5** | T3/T4 预算与 Fix | `MonthlyPassBudget` 在 S3-4 加深后中位 &lt;50ms；`SerialFix` 同预算。T4：切换 `UseFixForKeyQuantities` 的同平台 Replay 单测，不宣称 Mac CI | epics T3/T4 / B6 / ADR-002 |
| **A5-6** | 回归门禁 | EditMode 全绿；Gate-0 CI 2/2；必要时上调 `GATE0_MIN` | Epic 8 / 契约 |

## 4. 已有基座（勿重做）

- S3-4 数据与单测：`LegitimacySource`（无宗教项）、`LawFamily`/`LawFamilyLocked`、`EthnicComposition` MVP 折叠、`MilitaryState`/`devBias_military`、`CivilizationEpic3Tests` S34_*
- S5-3：`WorldMapLodStreamer` 同步 High + 焦点 Mid、远域 Low 异步；`WorldMapS53LodStreamTests`
- S5-4：`WorldStartFactory` / `GeoPoliticalInit`；远古沙盒 vs 当今地缘已可玩
- P2/P4：`NprDioramaPalette`、`WorldMapPresenter`、`CameraLodController.ApplyRenderLod`、VS-8
- T3：`PerformanceBudgetTests`（含真实 geo 与 SerialFix）；T4 钩子：`DeterminismFallback.UseFixForKeyQuantities`
- Schema 10；Gate-0 四路 Replay；本地 EditMode 基线 **266**；`GATE0_MIN=259` / CI `130`

## 5. 验收标准（DoD）

1. 新档默认可玩核心层全开（v2 + 多聚落）；S3-4 四子系统可测且不指定民族/法律。
2. 读档关键路径不阻塞；远域 Low 后至；双开局不重做、行为仍只差 `WorldInitConfig`。
3. NPR 只读 WorldView、不随模式切风格；相机 LOD 不改变月哈希。
4. 核心层全开月级 pass 中位 &lt;50ms；`main` tip Gate-0：**V0-8 + V0-9 = success（2/2）**。

## 6. 风险

| 风险 | 缓解 |
|---|---|
| S3-4 加深把 T3 推过 50ms | 先加深再重跑 `PerformanceBudgetTests`；超预算用回退 2 / 收紧 LOD，不改哈希指标 |
| 读档误同步等待全量 Low | 沿用 Build 契约：关键路径只 High+焦点 Mid；Ensure 与 tick 解耦 |
| 相机 LOD 回写逻辑态 | 表现层只读快照；单测哈希前后一致 |
| 把 T4 理解成必须上 Mac CI | 本 Sprint 只交同平台 Fix 切换测；跨机 Replay 明确 Out |

## 7. 工程启动

1. 本文档合入 `main` 后另开 **`cursor/sprint05-core-layer`** 写代码。
2. 推送 / PR / 合并均须 Checks **2/2 绿**；`worldsim-pc` online；禁止默认 `--admin` 合入。

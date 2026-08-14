---
项目名: WorldSim
文档名: Phase 4 预制作 · Sprint 03 计划（S2/S3 月账满流水线 + P3）
版本: v1.0.0
日期: 2026-08-14
作者: 游承峰（主理人）
阶段: Phase 4 — 预制作
输入文档:
  - production/phase4-gate.md
  - production/sprint-02-plan.md（已结项）
  - production/epics/worldsim-epics.md（S2-1/2、S3-1/2、P3）
  - design/gdd/ecology-sim-engine.md §4.3
  - design/gdd/civilization-system.md §4.3
  - tests/contracts/determinism-contract.md
状态: Sprint 03 实现中（代码分支 `cursor/sprint03-monthly-ledger`）
---

# Phase 4 预制作 · Sprint 03（月账满流水线）

## 1. 目标（Why）

Sprint 02 已收口 VS-8。Sprint 03 把 **Epic 0 切片桩 / 薄步** 推到 **可验收的 S2(11)+S3(16) 月级大账**：生态与文明因果序可测、默认走 v2 引擎、表现插值采样对齐且永不回写，Gate-0 仍绿。

## 2. 范围边界（In / Out）

| In（本 Sprint 必须） | Out（本 Sprint 不做） |
|---|---|
| S2-1 生态态/稳态/食物链/资源契约收口 + 确定性单测 | 完整粒子预算 UI / AX-2 音频 |
| S2-2 11 步满流水线：地貌触发、5 指标+预警、`harvestRate←S3`、默认 `ecology.v2` | 新 geo 源 / 全分辨率地球扩容（归 Sprint 4） |
| S3-1 聚落成长/承载力/村–都市圈 + 地理选址约束加深 | S3-3/4/5 经济七线/政治军事核心层全开（Should，后移） |
| S3-2 16 步因果序契约（⑪≺⑫ 等）+ ⑦–⑨ 最小可哈希逻辑 + 生态修正真实化 | 删历史功能分支 |
| P3 WorldView 采样 Civ/Eco v2；插值永不回写；Gate-0 仍绿 | Comprehensive 可访问性 / CVD 图案资产全集 |

## 3. Story 清单

| Story | 简述 | 验收 | 追溯 |
|---|---|---|---|
| **A3-1** | S2-1 生态态契约 | Species / FoodChain / Resource / Homeostasis / Indicator 可测；稳态自修复不依赖帧序 | epics S2-1 / ecology §2 |
| **A3-2** | S2-2 十一步行 | 11 步固定序；⑦地貌有触发；⑩五指标+预警；默认 `ecology.v2`；稳定 ID 遍历 | epics S2-2 / §4.3 |
| **A3-3** | S3-1 聚落尺度 | 村~都市圈分档；承载力；食物驱动成长；选址读 `IWorldGeography` | epics S3-1 |
| **A3-4** | S3-2 十六步行 | 16 步固定序；⑦–⑨ 非空可哈希（或稳定空步+序测）；⑪≺⑫ 交换必分叉；生态修正真实化 | epics S3-2 / §4.3 |
| **A3-5** | P3 插值对齐 | `PresentationWorldView` 采样 Civ/Eco v2；插值不回写 `WorldState`；既有单测扩展 | epics P3 |
| **A3-6** | 回归门禁 | EditMode 全绿；Gate-0 CI 2/2；必要时上调 `GATE0_MIN` | Epic 8 / 契约 |

## 4. 已有基座（勿重做）

- `EcologySimEngine` / `CivilizationSimEngine` 已挂 11/16 步序（多步仍薄或空）
- `SimOrchestrator` + `ecology.v2` / `civilization.v2` 开关
- `PresentationInterpolator` + `PresentationInterpolationTests`（哈希不回写已绿）
- Gate-0 四路 Replay + `worldsim-pc` runner

## 5. 验收标准（DoD）

1. 默认新档走 v2 月账；11+16 步序单测锁定（含因果序不可交换）。
2. ⑦地貌 / ⑩五指标 / ⑦–⑨ 社会宗教文化 至少达到「可哈希最小逻辑」。
3. 表现采样跟 Civ/Eco v2；插值路径不改变月哈希。
4. `main` tip Gate-0：**V0-8 + V0-9 = success（2/2）**。

## 6. 风险

| 风险 | 缓解 |
|---|---|
| 加深业务导致 Gate-0 分叉 | 每 Story 小步合入；先序测再加深量 |
| v2 与 Slice 桩双轨漂移 | 本 Sprint 默认 v2；桩仅保留 Gate-0 兼容路径并注明 |
| P3 采样扩容性能 | 只读快照字段；不入模拟核心 |

## 7. 工程启动

1. 本文档合入 `main` 后另开 **`cursor/sprint03-monthly-ledger`** 写代码。
2. 推送 / PR / 合并均须 Checks **2/2 绿**；`worldsim-pc` online；禁止默认 `--admin` 合入。

---
项目名: WorldSim
文档名: Phase 4 预制作阶段门记录
版本: v1.2.0
日期: 2026-08-14
作者: 游承峰（主理人）/ 程基岩（工程）
阶段: Phase 4 — 预制作 OPEN
关联: docs/architecture/control-checklist.md / production/sprint-01-plan.md / production/sprint-02-plan.md / production/sprint-03-plan.md
状态: 生效
---

# Phase 4 预制作阶段门记录

## 1. 判定

| 门 | 结论 | 日期 |
|----|------|------|
| Phase 3 出口（control-checklist A–E / D.1–D.9） | **PASS** | 2026-08-14 |
| Phase 4 预制作 | **OPEN** | 2026-08-14 |
| Sprint 01（Epic 0 / Gate-0） | **CLOSED** | 2026-08-14 |
| Sprint 02（VS-8 可访问性 Standard） | **CLOSED** | 2026-08-14 |
| Sprint 03（S2/S3 月账 + P3） | **CLOSED** | 2026-08-14 |
| Sprint 04 | **待开** — 全分辨率地球 + 存档 LOD + S3-3/5（见 epics §11） | — |

## 2. PASS 证据

### 2.1 Gate-0 / CI

- 本地：`tests/ci/run-gate0-local.ps1` 全绿（基线 **254** EditMode；`GATE0_MIN=247`）。
- CI：`worldsim-pc` 自托管 runner online 后，main 上 Gate-0 成功示例：
  - https://github.com/niunaguo0-pixel/worldsim/actions/runs/31781780787 （PR #7 Sprint 02）
  - https://github.com/niunaguo0-pixel/worldsim/actions/runs/31788570658 （PR #9 Sprint 03）
  - https://github.com/niunaguo0-pixel/worldsim/actions/runs/31788896739 （PR #9 合并后 main）
- Workflow：`.github/workflows/gate0.yml`（V0-8 ubuntu + V0-9 self-hosted；`GATE0_MIN_TESTS=118`）。

### 2.2 控制清单入口条件

`docs/architecture/control-checklist.md` §D.1–D.9 全部 ✅。

### 2.3 ADR

U1–U4 已于 2026-08-12 接受。

## 3. Sprint 01 结项（V0-1 ~ V0-9）

| Story | 状态 |
|-------|------|
| V0-1 ~ V0-9 | ✅ |

详情见 `production/sprint-01-plan.md`。

## 4. Sprint 02 结项（VS-8 / A2-1 ~ A2-6）

| Story | 状态 |
|-------|------|
| A2-1 ~ A2-6 | ✅ |

证据：PR #7 → `63ed91b`。详情见 `production/sprint-02-plan.md`。

## 5. Sprint 03 结项（S2/S3 月账 + P3 / A3-1 ~ A3-6）

| Story | 状态 |
|-------|------|
| A3-1 S2-1 生态态契约 | ✅ |
| A3-2 S2-2 十一步满流水线（五指标 / 地貌触发 / harvestRate←S3） | ✅ |
| A3-3 S3-1 聚落尺度 / 承载力 / 档位 | ✅ |
| A3-4 S3-2 十六步因果序（⑦–⑨ 可哈希；⑪≺⑫ 交换分叉测） | ✅ |
| A3-5 P3 表现采样 Civ/Eco v2 | ✅ |
| A3-6 Gate-0 回归（254/254；min 上调） | ✅ |

证据：PR #9 → `main`@`2bcdef4`；run https://github.com/niunaguo0-pixel/worldsim/actions/runs/31788570658 。详情见 `production/sprint-03-plan.md`。

## 6. Sprint 04 目标（一句话 · 待开计划）

**Epic 5 全分辨率地球网格 + Epic 7 存档 LOD 分块 + S3-3/5 国家聚合/经济深化**（epics §11 Sprint 4 原排期）。

不做：粒子预算 UI、AX-2、删历史分支。

权威计划：待新建 `production/sprint-04-plan.md`（文档门另开 PR）。

## 7. 备注

- Epic 2/3 Must（S2-1/2、S3-1/2）+ P3 已在 Sprint 03 收口；S3-3/4/5 Should 留 Sprint 4–5。
- Runner：`C:\actions-runner\worldsim`；`worldsim-pc` online。
- **分支检查铁律**：推送/PR/合并须 V0-8+V0-9 均 success（2/2）；禁止默认 `--admin`。

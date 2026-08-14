---
项目名: WorldSim
文档名: Phase 4 预制作阶段门记录
版本: v1.0.0
日期: 2026-08-14
作者: 游承峰（主理人）/ 程基岩（工程）
阶段: Phase 4 — 预制作 OPEN
关联: docs/architecture/control-checklist.md v1.1.0 / production/sprint-01-plan.md / production/sprint-02-plan.md
状态: 生效
---

# Phase 4 预制作阶段门记录

## 1. 判定

| 门 | 结论 | 日期 |
|----|------|------|
| Phase 3 出口（control-checklist A–E / D.1–D.9） | **PASS** | 2026-08-14 |
| Phase 4 预制作 | **OPEN** | 2026-08-14 |
| Sprint 01（Epic 0 / Gate-0） | **CLOSED** | 2026-08-14 |
| Sprint 02 | **OPEN** — VS-8 可访问性 Standard | 2026-08-14 |

## 2. PASS 证据

### 2.1 Gate-0 / CI

- 本地：`tests/ci/run-gate0-local.ps1` 全绿（近期基线 ≥236，含可访问性设置壳单测）。
- CI：`worldsim-pc` 自托管 runner online 后，main 上 Gate-0 成功示例：
  - https://github.com/niunaguo0-pixel/worldsim/actions/runs/31778937726 （PR #4 合并后）
  - https://github.com/niunaguo0-pixel/worldsim/actions/runs/31780120095 （PR #5 合并后）
- Workflow：`.github/workflows/gate0.yml`（V0-8 ubuntu + V0-9 self-hosted）；geo LF 强制步骤已入。

### 2.2 控制清单入口条件

`docs/architecture/control-checklist.md` §D.1–D.9 全部 ✅（B2/B3/B4/B8、asmdef、BuildScript、region-presets、S7、NPR+减少动态壳、Burst pin）。

### 2.3 ADR

U1–U4 已于 2026-08-12 接受（ADR-001 C / ADR-002 选项 2 / ADR-003 A / ADR-004 选项 1）。

## 3. Sprint 01 结项（V0-1 ~ V0-9）

| Story | 状态 |
|-------|------|
| V0-1 asmdef 边界 | ✅ |
| V0-2 确定性数学基座 | ✅ |
| V0-3 SimOrchestrator 双频 | ✅ |
| V0-4 序列化往返 | ✅ |
| V0-5 Gate-0 四路 Replay | ✅ |
| V0-6 region-presets / 地球 MVP | ✅（并超前完成完整 geo-v1 + S5-4） |
| V0-7 三级回退（Could） | ✅ |
| V0-8 Burst/Unity pin + CI | ✅ |
| V0-9 Gate-0 CI 门禁 | ✅ |

详情见 `production/sprint-01-plan.md`（状态：已结项）。

## 4. Sprint 02 目标（一句话）

**VS-8 可访问性 Standard 收口（AX-1 P0）**：在已有「减少动态」壳与 AS-4 接线之上，补高对比 / 脉冲=0 / LOD 0.5s / CVD 占位 / 字体缩放，并配单测。

不做：完整粒子系统、音频 AX-2、新 geo 源、删除历史功能分支。

权威计划：`production/sprint-02-plan.md`。

## 5. 备注

- Epic 1/4/5/6 部分 Story 已在 Phase 3 尾声超前落地；Sprint 02 不以重做干预/HUD/地球为主，而以 VS-8 收口为主（见 `production/epics/worldsim-epics.md` 总览注记）。
- Runner：`C:\actions-runner\worldsim`；登录自启 `WorldSim-GitHub-Actions-Runner`；`git config --global core.autocrlf false`。

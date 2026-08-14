---
项目名: WorldSim
文档名: Phase 4 预制作 · Sprint 02 计划（VS-8 可访问性 Standard）
版本: v1.1.0
日期: 2026-08-14
作者: 游承峰（主理人）
阶段: Phase 4 — 预制作
输入文档:
  - production/phase4-gate.md
  - design/art/asset-spec.md §7.3–7.6 / §8.3 VS-8
  - design/art/art-bible.md §8.4
  - docs/art/accessibility-grade.md v1.1.0
  - WorldSim AccessibilitySettings / DioramaGradeMath（已有减少动态壳）
状态: 已结项（2026-08-14）— VS-8 Standard 已合入 main（PR #7）；下一冲刺见 sprint-03-plan.md
---

# Phase 4 预制作 · Sprint 02（VS-8 可访问性 Standard）

> **结项说明（2026-08-14）**：A2-1~A2-6 已在 `cursor/sprint02-vs8-access` 落地并经 PR #7 合入 `main`。Gate-0 证据：https://github.com/niunaguo0-pixel/worldsim/actions/runs/31781780787 。下一冲刺：`production/sprint-03-plan.md`（S2/S3 月账满流水线 + P3）。

## 1. 目标（Why）

Sprint 01 已把门禁与可玩切片立住。Sprint 02 把 **AX-1 / VS-8** 从「减少动态开关 + AS-4 钳制」补到 **Standard 档可玩设置面**：高对比、无闪烁脉冲、LOD 过渡、CVD 占位、字体缩放——保证「读世界」对光敏/对比度/色觉缺陷玩家可达。

## 2. 范围边界（In / Out）

| In（本 Sprint 必须） | Out（本 Sprint 不做） |
|---|---|
| 高对比主题开关 → Volume / UI 最小接线（§7.3） | 完整粒子系统实装与预算档 UI |
| 减少动态 ON → 危机脉冲幅度 = 0（语义保留图标+文字） | 音频 AX-2 冗余通道 |
| 减少动态 ON → LOD cross-fade 0.5s 且禁止 pop | 新 geo 源 / 全球管线扩容 |
| CVD 模式占位开关 + 图案层钩子（UI+标志位可薄） | 删除历史功能分支 |
| 全局字体缩放 75%–150% → Formal HUD / 设置壳 | Comprehensive 档细粒度定制 |
| 单测 + Gate-0 `$min` 上调；checklist / VS-8 勾选 | 重做已落地的干预/双开局/NPR 探针 |

## 3. Story 清单

| Story | 简述 | 验收 | 追溯 | 结项 |
|---|---|---|---|---|
| **A2-1** | 高对比主题 | OFF 默认；ON 时 Volume/UI 对比度参数按 §7.3 最小集生效 | asset-spec §7.3 / §7.6 | ✅ |
| **A2-2** | 脉冲归零 | `ReduceMotion` ON → 脉冲幅度 0；图标+文字+边框仍可读 | AX-1 / §7.4 ① | ✅ |
| **A2-3** | LOD 过渡 | `ReduceMotion` ON → cross-fade **0.5s**、禁 pop；OFF 保持现有行为 | §7.4 ⑤ / asset-spec LOD | ✅ |
| **A2-4** | CVD 占位 | 设置壳开关 + `AccessibilitySettings.CvdMode`；图案层钩子可空实现但可测 | §7.6 / VS-11 薄接口 | ✅ |
| **A2-5** | 字体缩放 | 滑块 75%–150%；Formal HUD / 设置壳字号随缩放 | §7.6 / Basic→Standard | ✅ |
| **A2-6** | 回归门禁 | 单测覆盖开关默认值/持久化/与 AS-4 交互；`run-gate0-local` `$min` 上调 | Epic 8 | ✅ |

## 4. 已有基座（勿重做）

- `AccessibilitySettings` + PlayerPrefs + IMGUI 设置壳
- `DioramaGradeMath.TransitionSeconds` AS-4：1.5s / 减少动态 2.5s
- `ApplyReduceMotion` Bloom×0.5
- `FormalGameHud`「可访问性」入口

## 5. 验收标准（DoD）

1. ✅ 设置壳可独立开关：减少动态、高对比、CVD、字体缩放；重启后 Prefs 恢复。
2. ✅ 减少动态 ON 时：灾害调色 ≥2.5s、Bloom 减半、脉冲 0、LOD 过渡 ≥0.5s。
3. ✅ EditMode 单测全绿；Gate-0 本地与 CI（`worldsim-pc`）全绿。
4. ✅ `control-checklist` / asset-spec VS-8 标注本 Sprint 已收口项。

## 6. 风险

| 风险 | 缓解 |
|---|---|
| OS reduce-motion 误作唯一源 | 继续仅作首次 Prefs 建议；游戏内自实现（圣经 §8.4） |
| 高对比与季节 Volume 打架 | 可访问性覆盖层 priority 高于季节/灾害（asset-spec） |
| CVD 无图案资产 | 本 Sprint 只交开关+钩子；VS-11 图案集后续 Sprint |

## 7. 工程启动

分支：`cursor/sprint02-vs8-access`（已合入 `main`，PR #7）。

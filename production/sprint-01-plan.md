---
项目名: WorldSim
文档名: Phase 4 预制作 · 首个冲刺计划（Sprint 01）
版本: v1.0.0
日期: 2026-08-12
作者: 游承峰（Yoan Summit）— 游戏开发工作室主理人
阶段: Phase 4 — 预制作
输入文档: 
  - docs/architecture/worldsim-architecture.md v1.0.2
  - docs/architecture/adr/ADR-001~004（已接受）
  - docs/architecture/control-checklist.md v1.0.1
  - production/epics/worldsim-epics.md
  - design/ux/ux-spec.md v1.0.0
  - design/art/asset-spec.md v1.0.0
  - design/art/art-bible.md v1.0.2
  - docs/art/accessibility-grade.md v1.1.0
状态: 已结项（2026-08-14）— Gate-0 已绿；Phase 4 预制作 OPEN
---

# Phase 4 预制作 · 首个冲刺计划（Sprint 01）

> **结项说明（2026-08-14）**：本冲刺（Epic 0 / V0-1~V0-9）已在代码与 CI 兑现，Gate-0 全绿。阶段门记录见 `production/phase4-gate.md`。Sprint 02（VS-8）已结项；当前冲刺见 `production/sprint-03-plan.md`。

## 1. 目标（Why）

Sprint 01 是 WorldSim 的**确定性 Gate-0 垂直切片**：在不碰完整 S1/S2/S3 业务逻辑的前提下，先把"引擎无关的确定性模拟核心 + Unity 胶水层"跑通，让四路 Replay（1×/20×/变速/存读档）在 ≥120 游戏月内逐月哈希一致。**Gate-0 门禁全绿 = 解锁 Epic 1–7（系统实现）的通行证**。

> 这与架构 ADR-001 方案 C、ADR-002 选项 2、ADR-003 选项 A、ADR-004 选项 1 直接对应。

## 2. 范围边界（In / Out）

| In（本次 Sprint 必须完成） | Out（本次 Sprint 不做） |
|---|---|
| 模拟核心 `WorldSim.Simulation.*` asmdef 与 UnityEngine 边界 | S1 完整干预业务逻辑 |
| `SimOrchestrator` + S4 双频混合结算（整数序号派生边界） | S2 完整生态业务逻辑 |
| 确定性数学基座（Quantize / Fix / FNV-1a-64 / xoshiro256** 256-bit 分流） | S3 完整文明业务逻辑 |
| 序列化往返（二进制快照 + 命令日志 + LOD 分块 + delta） | 完整 UI/HUD 美术实现 |
| Gate-0 四路 Replay 测试台 + CI 自动门禁 | S6 涌现叙事内容生成 |
| 真实地球 MVP 区域精算（消费 `region-presets.json`） | 全球真实数据完整管线 |

## 3. Story 清单（来自 `production/epics/worldsim-epics.md` Epic 0）

| Story | 简述 | 验收标准 | 关联 |
|---|---|---|---|
| **V0-1** | 模拟核心 asmdef 边界 | 编译通过；`WorldSim.Simulation` 不引用 `UnityEngine.CoreModule` | ADR-001 C |
| **V0-2** | 确定性数学基座 | `Quantize`/`Fix`/`DeterminismHash`/`RngRegistry` 单元测试通过；RNG 256-bit 全量序列化 | ADR-002 / R-N3 |
| **V0-3** | `SimOrchestrator` + 双频结算 | 边界由整数月/周序号派生；稳定 ID 排序；week 先于同刻 month；1×/20× 边界序列一致 | ADR-001 / R-N1 |
| **V0-4** | 序列化往返 | 快照+命令日志可完整往返；Replay 路径④通过 | ADR-004 |
| **V0-5** | Gate-0 四路 Replay 测试台 | 同 seed+同干预，1×/20×/变速/存读档，≥120 游戏月，关键指标哈希逐月一致，断言无分叉 | B3 |
| **V0-6** | 真实地球 MVP 区域精算 | 能正确加载 `region-presets.json` 6 个预设并生成最小地图基底 | B4 |
| **V0-8** | CI 固定版本锁 | CI env 固定 Unity 6000.0.81f1；`assert-burst-pinned` 步骤入 CI | B2 / B8 |
| **V0-9** | Gate-0 CI 自动门禁 | `.github/workflows/gate0.yml` 跑通 V0-5；红即阻塞合并 | B3 / ADR-003 A |

> V0-7（三级回退钩子）= Could，可在 Gate-0 通过后顺延。

## 4. Phase 4 入口条件（B2/B3/B4/B8）排期

这些来自 `docs/architecture/control-checklist.md`，必须在 Sprint 01 闭环：

- **B2**：CI 锁同 Unity/Burst 版本 → V0-8
- **B3**：Quantize + 确定性指标哈希 + Gate-0 四路 Replay 入 CI → V0-2 / V0-3 / V0-5 / V0-9
- **B4**：真实地球数据管线 + `region-presets.json` 消费 → V0-6
- **B8**：`com.unity.burst` 固定版本入 manifest + CI `assert-burst-pinned` → V0-8

## 5. 验收标准（Definition of Done）

1. `Tests/Gate0/Gate0Determinism` 四路 Replay 在本地与 CI 均绿。
2. `Tests/Unit/Determinism` 下 Quantize / RNG / 边界 / 稳定 ID / 序列化往返全部绿。
3. `Packages/manifest.json` 含固定版本 `com.unity.burst`；CI 中 `assert-burst-pinned` 通过。
4. `region-presets.json` 6 个预设可在最小地图场景中被加载、序列化往返后一致。
5. 代码审查通过：无 `UnityEngine` 进入 `WorldSim.Simulation`；无 `float` 边界减法；无 `string.GetHashCode`。

## 6. 已知风险与缓解

| 风险 | 严重度 | 来源 | 缓解 |
|---|---|---|---|
| R-N1 浮点累加器边界漂移 | **P0** | engineering-lead 发现 | 已修：边界改由整数月/周序号派生；V0-3 验收标准强制。 |
| R-N2 Burst 未入 manifest | **P0** | engineering-lead 发现 | V0-8 固定版本入 manifest + CI `assert-burst-pinned`。 |
| R-N3 RNG 只存 128-bit 破坏序列 | **P0** | engineering-lead 发现 | 已修：xoshiro256** 256-bit 全量入档；V0-2 验收标准强制。 |
| 真实地球数据获取延迟 | P1 | B4 | V0-6 仅做 MVP 区域精算；完整全球管线排 Sprint 4。 |
| AX-2 音频冗余未补 | P1 | art-director | 视觉/文字冗余已占位；Sprint 02 排 audio-director。 |
| S6 涌现叙事未实现 | P1 | design-strategist | 事件/编年史内容生成排 Sprint 03+；UX 已预留接口。 |

## 7. 下游依赖与协同

- **UX 规格** `design/ux/ux-spec.md` v1.0.0：指导 Sprint 02 表现层开发，尤其是干预光标、GoalMode UI、AX-2 灾害预警冗余。
- **资产规格** `design/art/asset-spec.md` v1.0.0：MVP 190 条资产，VS-1~VS-12 优先，AS-2（P0）不可后补。
- **美术圣经** `design/art/art-bible.md` v1.0.1：AS-2 世界层灾害图标双极兜底已升格 P0。
- **可访问性** `docs/art/accessibility-grade.md` v1.1.0：MVP=Basic / 核心=Standard 目标不变。

## 8. 阶段门（Phase 4 → Phase 5）

**出口条件**（全部满足方可进入 Phase 5 制作）：
1. Gate-0 门禁全绿（Sprint 01 完成）。
2. Epic 1–7 Story 拆分完成且与 ADR/B 项映射清晰。
3. MVP 资产清单锁定（asset-spec）且 VS-1~VS-6 已排入 Sprint 02。
4. B2/B3/B4/B8 全部闭环。
5. 无新增 P0 文档/工程不一致。

## 9. 下一步动作

**已完成（2026-08-14）**：V0-1→V0-9 流水线与 Gate-0 CI 已闭环。

**下一动作**：执行 `production/sprint-02-plan.md`（VS-8 可访问性 Standard 收口）。工程分支建议：`cursor/sprint02-vs8-access`。

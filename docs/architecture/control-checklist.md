---
项目名: WorldSim
文档名: Phase 3 出口控制清单（Control Checklist）
版本: v1.6.0
日期: 2026-08-14
作者: 程基岩 / 游承峰（阶段门）
阶段: Phase 3 出口 PASS → Phase 4 预制作 OPEN
关联: worldsim-architecture.md / adr/ADR-001~004 / architecture-review.md / production/phase4-gate.md
变更摘要: v1.6.0（2026-08-14）——Sprint 05 **OPEN**（`production/sprint-05-plan.md`）。v1.5.0——Sprint 04 CLOSED。v1.4.0——Sprint 04 OPEN。
---

# Phase 3 出口控制清单（Control Checklist）

> 本清单是 Phase 3 技术搭建的**出口门**：进入 Phase 4 预制作前，下列四类项须全部满足。
> **2026-08-14 判定：PASS** — 已进入 Phase 4 预制作（见 `production/phase4-gate.md`）。
> 优先级：P0（Gate-0 确定性）> ADR 用户确认 > 架构评审阻塞项清零 > Phase 4 入口条件就绪。

---

## A. Gate-0 确定性 CI 门禁项（P0 — S4 §7.3）

| # | 门禁项 | 通过标准 | 状态 |
|---|--------|---------|------|
| G0-1 | 固定步长唯一时间源 + 模拟核心零引擎引用 | `check-sim-asmdef.ps1`：Simulation 无 `using UnityEngine` / `noEngineReferences=true`；已入 `gate0.yml` pin-versions | ✅ Sprint 1 |
| G0-2 | 双频 pass 按边界时间戳升序合并 | `SimOrchestrator` 整数 month/week 边界；week 先 month 后 | ✅ V0-3 |
| G0-3 | 稳定 ID 排序遍历 | `SortedByStableId` / `StableIdSet`；单测覆盖 | ✅ V0-2/V0-3 |
| G0-4 | RNG 分流入档 | xoshiro256** class；256-bit 全状态随档；禁 `System.Random` | ✅ V0-2/V0-4 |
| G0-5 | 禁不确定并发与浮点漂移 | Gate-0 串行；`Quantize` 写回；回退 2 留 `Fix` | ✅ 切片期 |
| G0-6 | 四路 Replay 哈希一致 | 同 seed+干预：1×/20×/变速/存读档；≥120 月；`Gate0DeterminismTest` | ✅ V0-5 |
| G0-7 | 哈希函数确定 | FNV-1a-64 over 确定性字节流；禁 `string.GetHashCode` | ✅ V0-2 |
| G0-8 | 三级回退可用 | `DeterminismFallback` 默认 None；NarrowSpeed/SerialFix/Lockstep 钩子+单测 | ✅ V0-7 |

> CI：`.github/workflows/gate0.yml`
> 1. `pin-versions`（ubuntu）：`assert-burst-pinned` + `check-sim-asmdef` + `assert-region-presets-synced`
> 2. `gate0`（**self-hosted Windows X64**，`shell: powershell`）：`resolve-unity.ps1` → 全量 `WorldSim.Tests` EditMode → 上传 `gate0.xml`
> 本地：`tests/ci/run-gate0-local.ps1`（含 presets 同步，`GATE0_MIN` 见脚本）
> Runner：`worldsim-pc` @ `C:\actions-runner\worldsim`；登录自启任务 `WorldSim-GitHub-Actions-Runner`；本机 `git config --global core.autocrlf false`（geo SHA 依赖 LF）。Windows 服务仍可选升级。

---

## B. ADR 待用户确认项（已清零：4/4 已接受，2026-08-12）

| # | ADR | 采纳选项 | 状态 |
|---|-----|---------|------|
| U1 | ADR-001 仿真范式 | C 确定性数据导向核心+有序流水线 | ✅ 已接受 |
| U2 | ADR-002 确定性数学 | 2 float+禁 fast-math+量化写回+`Fix` 兜底 | ✅ 已接受 |
| U3 | ADR-003 CLI/CI | A 本地 CLI + GitHub Actions（Gate-0 自动门禁） | ✅ 已接受 |
| U4 | ADR-004 序列化 | 1 全量二进制快照+命令日志+LOD/delta（后者延后） | ✅ 已接受 |

---

## C. 架构评审阻塞项（来自 architecture-review.md §5）

| # | 阻塞项 | 状态 | 阻断 Phase 4？ | 归属 |
|---|--------|------|---------------|------|
| B1 | ADR-002 选项 2 确认 | ✅ 已解决 | 原：是 | 用户 |
| B2 | CI 锁定同 Unity + 同 Burst | ✅ V0-8（`version-pins.json` + Burst 1.8.30 直依） | 是（已闭环） | 工程 |
| B3 | Quantize + 哈希 + Gate-0 入 CI | ✅ V0-5/V0-9（全量 EditMode + 自托管） | 是（已闭环） | 工程 |
| B4 | 真实地球管线 + region-presets 消费 | ✅ Epic 5：geo-v1 全源管线 + S5-3 LOD + S5-4 双开局接入可玩 StartWorld（区域裁剪地缘种子） | 是（已闭环） | Phase 4 |
| B5 | 空间映射「绝不指定单国家族」红线 | ✅ V0-6 `RegionPresetRedLines` | 建议 | Phase 4 |
| B6 | 核心层全开月级 pass 预算 profiler | 建议 | 否 | Phase 4：**T3 已落** `PerformanceBudgetTests`（中位 &lt;50ms） |
| B7 | AI Navigation 不入模拟核心 | 建议 | 建议 | Phase 4 |
| B8 | `com.unity.burst` 固定版本入 manifest + CI assert | ✅ V0-8 | 是（已闭环） | 工程 |

---

## D. Phase 4 预制作入口条件

进入 Phase 4 前须满足：

1. ✅ A 类 Gate-0（G0-1~G0-8）代码通过；CI 接 `gate0.yml`（需自托管 Windows runner 预装 Unity 6000.0.81f1）
2. ✅ B 类 ADR（U1–U4）用户确认
3. ✅ C 类：B1/B2/B3/B8/B4 ✅（完整地球管线 + 双开局可玩接入）
4. ✅ `WorldSim.Simulation.*` asmdef + CI `check-sim-asmdef`
5. ✅ `WorldSim.Editor.BuildScript.cs`（ADR-003；`BuildWin64` headless）
6. ✅ region-presets 导入/消费（V0-6）；StreamingAssets 与 design 由 `assert-region-presets-synced.ps1` 守住
7. ✅ 模块化开关框架（S7）完整配置 — `WorldSim.ModularToggle` 目录/预设 + New Game 面板 + 文明子步骤门控
8. ✅ NPR 微缩沙盘渲染原型 — P2 地球+色板+rim；打磨：四季 Volume / AS-2 Overlay / 旱灾偏色+AS-4；手绘 TEX_DETAIL 探针 + NprWater；减少动态壳；**Sprint 02 VS-8：高对比 / 脉冲=0 / LOD 0.5s / CVD 钩子 / 字体缩放**
9. ✅ `com.unity.burst@1.8.30` 直依 + `assert-burst-pinned`（lock 中他包写的 ≥1.8.29 仅为下限，解析为 1.8.30）

> 环境：Unity **6000.0.81f1**；Hub：`C:\Program Files\Unity\Hub\Editor\6000.0.81f1`；注册表：`HKLM\SOFTWARE\Unity Technologies\Installer\Unity 6000.0.81f1`（`Location x64`）。

---

## E. Phase 3 交付物清单

- [x] `docs/architecture/worldsim-architecture.md`
- [x] `docs/architecture/adr/ADR-001` … `ADR-004`
- [x] `docs/architecture/architecture-review.md`
- [x] `docs/architecture/control-checklist.md`（本文）
- [x] Epic 0 代码：V0-1~V0-9（含 V0-7 Could）
- [x] `.github/workflows/gate0.yml` + `tests/ci/*`

---

## F. Phase 4 状态（2026-08-14）

| 项 | 值 |
|----|-----|
| Phase 3 出口 | **PASS** |
| Phase 4 预制作 | **OPEN** |
| 入口条件复核 | 2026-08-14（§D.1–D.9 全 ✅） |
| 当前 Sprint | **Sprint 05 OPEN** — S3-4 + P2/P4 + T3/T4（S5-3/4 只补缺口；见 `production/sprint-05-plan.md`） |
| Sprint 01 | **CLOSED** — Gate-0 已绿（见 `production/sprint-01-plan.md`） |
| Sprint 02 | **CLOSED** — VS-8 Standard（PR #7 / `production/sprint-02-plan.md`） |
| Sprint 03 | **CLOSED** — S2/S3 月账 + P3（PR #9 / `production/sprint-03-plan.md`） |
| Sprint 04 | **CLOSED** — 地球消费 + 存档 LOD + S3-3/5（PR #12 / `production/sprint-04-plan.md`） |
| Runner | `worldsim-pc` online；登录任务 `WorldSim-GitHub-Actions-Runner`；`core.autocrlf=false` |
| 权威阶段门记录 | `production/phase4-gate.md` |
| 分支 Checks | tip 须 V0-8+V0-9 **success（2/2）**；合入后等绿；禁止默认 `--admin` |

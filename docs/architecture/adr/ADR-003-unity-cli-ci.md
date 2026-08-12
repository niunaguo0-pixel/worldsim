---
ADR: 003
标题: Unity 工程与 CLI 构建 / CI 管线
状态: 已接受（Accepted，2026-08-12 用户拍板）
日期: 2026-08-12
作者: 程基岩
关联: docs/unity-setup-complete.md / 概念文档 §6.1（Unity 6 LTS 6000.0.81f1, URP）/ 用户"走 CLI 不碰 Hub"
---

# ADR-003 Unity 工程与 CLI 构建 / CI 管线

> **决策（2026-08-12，主理人游承峰转呈用户拍板）：采纳选项 A —— 本地 CLI 脚本 + GitHub Actions 自动门禁。**
> 理由：完全 headless、Gate-0 四路 Replay 自动门禁入 CI、可复现可审计，且与用户"走 CLI 不碰 Hub"一致。

## 背景（Context）
用户明确：**不操作 Unity Hub GUI，一切走 Unity CLI + unity mcp**（`docs/unity-setup-complete.md` 已确认编辑器 6000.0.81f1、CLI v1.0.0-beta.3、MCP 待 Trust）。同时 Gate-0 确定性测试必须自动化进 CI 门禁。工程结构须为"CLI-only + 可 headless 测试"服务。

## 决策（Decision）
- **Assembly 划分**：按 `worldsim-architecture.md §8.1` 拆 14 个 asmdef；`WorldSim.Simulation.*` 零 `UnityEngine.CoreModule` 依赖；`WorldSim.Editor` 持 CLI 构建脚本与 region 导入器；`WorldSim.Tests` 持 Gate-0 确定性测试（EditMode，headless）。
- **CLI 构建**：`-batchmode -nographics -executeMethod WorldSim.Editor.BuildScript.BuildWin64` 出 Win64 Player。
- **CI 门禁**：headless 跑 `Gate0Determinism` EditMode 测试（四路 Replay），哈希分叉即红、阻断合入。
- **包版本锁定**：`Packages/manifest.json` 已锁（URP 17.0.4 / AI Navigation 2.0.0 / Input System 1.8.1 / Timeline 1.8.6 / uGUI 2.0.0），不依赖 Hub 弹窗安装。

## 被否决备选（Rejected Alternatives）

> 选项 A 已采纳（见上方决策与 §决策）。以下为被否决 / 延展的备选方案及不采用理由。

### 选项 B — 仅本地 CLI 手动构建（无 CI）
- 优点：最简单。
- 缺点：Gate-0 确定性无自动门禁，靠人工跑测试 → 易漏检分叉，违反 P0 精神。
- 结论：不足，至少需本地脚本化 Gate-0 校验。

### 选项 C — Unity Cloud Build / DevOps 托管
- 结论：可用但不必需；当前以本地 CLI + 脚本为主，Cloud Build 留作延展。

## 后果（Consequences）
- 正向：全流程 CLI 可完成（建场景/装配/构建/测试）；Gate-0 自动门禁；可 headless 重放利于确定性 CI。
- 负向：所有工程操作须走 mcp/Editor 脚本，不能依赖 GUI 习惯（如拖 prefab 装配需在 Editor 脚本或 mcp 完成）；CI runner 首次配置有环境成本。
- 约束：`WorldSim.Editor.BuildScript.BuildWin64` 已落地（`Assets/Scripts/Editor/BuildScript.cs`）；region 导入器随 V0-6；CI 锁定同 Unity + Burst（见 ADR-002 / V0-8）。

## 采纳记录（已拍板）
- [x] 已采纳 **选项 A（本地 CLI 脚本 + GitHub Actions，Gate-0 自动门禁）**（2026-08-12，主理人游承峰转呈用户拍板）。
- 历史风险说明（已不触发）：若仅用本地手动 CLI（选项 B），Gate-0 确定性门禁需人工坚守，P0 风险上升；当前已落地选项 A，并保留本地 `gate0` 校验脚本作为兜底。

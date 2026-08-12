# 模拟核心零 UnityEngine.CoreModule 静态检查（G0-1 / ADR-001）

> 关联：`worldsim-architecture.md §1`（确定性边界红线）/ §8.1（asmdef 划分）/ `control-checklist.md` G0-1 / `ADR-001`（方案 C）。
> 目标：保证 `WorldSim.Simulation.*` 全部程序集**只引用 `UnityEngine.Mathematics` + `System.*`**，绝不可引用 `UnityEngine.CoreModule`（`GameObject` / `MonoBehaviour` / `Transform` / `Time` / `Physics` 等场景/帧依赖）。

## 1. 为什么必须查

架构判定"某段逻辑是否确定性安全"的唯一标准：**它能否在没有任何 Unity 场景、无任何渲染的情况下被单元测试重放**（架构 §1）。一旦模拟核心引用 `UnityEngine.CoreModule`，就隐含了对场景/帧/墙钟的依赖，破坏 headless 重放与 Gate-0 确定性。

## 2. 检查方式（两层）

### 2.1 asmdef 依赖约束（编译期）
每个 `WorldSim.Simulation.*.asmdef` 的 `references` 仅含：
- `Unity.Mathematics`（com.unity.mathematics）
- 其它 `WorldSim.Simulation.*`
- `UnityEngine` 程序集**整体不引用**；尤其排除 `UnityEngine.CoreModule`。

> 注意：`UnityEngine` 主程序集与 `UnityEngine.CoreModule` 在 Unity 内部是拆分 asmdef；要在 asmdef 层屏蔽 CoreModule，需**不引用**包含它的上层包，或显式只依赖 `UnityEngine.Mathematics` 子 asmdef。Phase 4 V0-1 在创建 Simulation asmdef 时落实。

### 2.2 静态扫描（CI 强制）
在 CI 增加步骤，扫描 `WorldSim/Assets/Scripts/Simulation/**/*.cs`，断言**不存在**以下 using / 符号：
- `using UnityEngine;` 下的 `GameObject` / `MonoBehaviour` / `Transform` / `Time` / `Physics` / `Debug`(可选放行 `UnityEngine.Debug`? 建议也禁，改用自写日志) 。
- 直接引用 `UnityEngine.Time.deltaTime` / `Time.realtimeSinceStartup` / `System.DateTime.Now` 作为逻辑输入。

允许：`using Unity.Mathematics;`、`using System.*;`、`UnityEngine.Debug`（仅限诊断，不进逻辑路径，建议加 `[Conditional]`）。

### 2.3 判定
- 扫描发现违例 → CI 红、阻断合入（与 Gate-0 同等级）。
- 放行需经工程负责人评审并将例外登记到本文件附录。

## 3. 与 Gate-0 的关系
G0-1（固定步长唯一时间源）由本检查在编译/CI 层兜底——任何 `Time.deltaTime` 引用会在合并前被拦下，从根上杜绝铁律 1 违例。

## 4. 附录：例外登记（默认空）
| 文件 | 符号 | 理由 | 审批 |
|------|------|------|------|
| — | — | — | — |

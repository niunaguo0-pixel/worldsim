---
ADR: 001
标题: 仿真架构范式（ECS/DOTS vs 经典数据导向 OOP）
状态: 已接受（Accepted，2026-08-12 用户拍板）
日期: 2026-08-12
作者: 程基岩
关联: S4 §7.3 铁律 / 概念文档 T1 / 系统索引 R13
---

# ADR-001 仿真架构范式

> **决策（2026-08-12，主理人游承峰转呈用户拍板）：采纳方案 C —— 确定性数据导向核心 + 显式有序流水线。**
> 理由：引擎无关模拟核心零 `UnityEngine` 场景依赖，月级因果流水线天然映射 S3/S4 有序 pass，Gate-0 确定性路径最干净、可 headless 重放与 CI 门禁；放弃全量 ECS/DOTS 与纯 MonoBehaviour OOP。

## 背景（Context）
WorldSim 的模拟核心是"连续时钟 + 固定步长月级大账 + 事件驱动周级子结算"（S4 §2.2/§2.7），并非每帧逐实体的行为模拟。同时 **R13 确定性为 P0（Gate-0）**，要求同 seed + 同干预序列在 1×/20×/变速/存读档下逐月哈希一致。叠加人口尺度层级（单聚落千万级、国家聚合），架构范式直接决定三件事：**确定性可达性、性能上限、可维护性**。

候选范式：
- **A. 全量 Unity DOTS/ECS**（Entities + Jobs + Burst）
- **B. 经典 MonoBehaviour OOP**（GameObject 每聚落/每个体）
- **C. 引擎无关确定性数据导向核心 + 显式有序流水线（推荐）**

## 决策（Decision）
**采用方案 C（推荐）**：模拟核心为引擎无关、确定性的数据导向 C#（`WorldSim.Simulation.*`，零 `UnityEngine.CoreModule` 依赖），以 `WorldState` 聚合根为唯一状态容器，由 `SimOrchestrator` 按固定顺序驱动月级大账（S1→S2→S3 子 pass）+ 周级子结算（稳定 ID 序脏集合）。叶子级可并行计算（同区域内逐 tile 生态、逐个体 needs）在 **Gate-0 通过后**经 `IJobParallelFor`+Burst 启用，且受回退 2 约束（固定块序归约 + 禁 fast-math + 可选 `Fix`）。

不采用方案 A 作为 Spine、不采用方案 B。

## 被否决备选（Rejected Alternatives）

> 方案 C 已采纳（见上方决策与 §决策）。以下为被否决的备选方案及不采用理由。

### 选项 A — 全量 Unity DOTS/ECS
- 优点：Burst 编译 + Job 并行性能上限最高；内存布局 SoA，缓存友好；天然适配"大量实体"。
- 缺点：**与 Gate-0 确定性冲突**——ECS chunk 迭代顺序跨运行/读档非确定，结构变更（add/remove component）引入非确定重排；Entity 查询需显式 `EntityQuery` + 排序才能稳定；整套体系把"月级因果耦合流水线"扭曲为并行 chunk 模型，反而难保证 S3 §4.3 的 16 步有序因果。学习曲线与确定性调试成本高。
- 结论：仅适合"无因果耦合、可独立处理"的叶子计算；不宜作 Spine。

### 选项 B — 经典 MonoBehaviour OOP
- 优点：最易上手，Unity 原生。
- 缺点：**确定性破产**（`Update` 顺序依赖场景挂载、float 漂移、GameObject 生命周期不可控）+ **性能破产**（T1：千级 NPC 逐帧 MonoBehaviour 不可行）+ 无法 headless 重放（依赖场景）。
- 结论：仅用于表现层（Runtime/Presentation），不进入模拟核心。

## 后果（Consequences）
- 正向：Gate-0 确定性路径最干净（串行月级 pass 即可逐位一致）；架构清晰分"确定性核心 / Unity 胶水"两层；可测试性高。
- 负向：放弃了 ECS 在"海量同构实体每帧处理"上的极致性能；若未来出现"每帧需处理百万级同构实体"的需求（目前设计不存在——国家层仅聚合、个体仅在聚落层微观），需局部引入 ECS/Job（回退 2 同机制）。
- 约束：`WorldSim.Simulation.*` 程序集 CI 中加入"禁止引用 UnityEngine.CoreModule"的规则检查（可用 asmdef 依赖约束 + Editor 静态分析）。

## 采纳记录（已拍板）
- [x] 已采纳 **方案 C**：确定性数据导向核心 + 显式有序流水线，叶子并行留待 Gate-0 通过后启用（2026-08-12，主理人游承峰转呈用户拍板）。
- 历史风险说明（已不触发）：若坚持方案 A（全量 ECS），Gate-0 确定性需额外投入"排序 EntityQuery + 禁用结构变更 + 定点化"，且 S3 16 步因果流水线实现复杂度显著上升，可能拖延 P0 门；MVP 阶段不采用。

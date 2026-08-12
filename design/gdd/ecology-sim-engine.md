---
项目名: WorldSim
文档名: 生态模拟引擎 GDD（Ecology Simulation Engine GDD）
版本: v1.0.1
日期: 2026-08-12
作者: 文策渊 (Vince Coyer) — 游戏策划与叙事设计师
阶段: Phase 2 — 系统设计
输入文档: systems-index.md v1.1.0 + game-concept.md v1.2.0 + intervention-system.md v1.1.0
状态: 草稿（待评审）
攻关风险: R3（涌现不可控性与世界崩坏）
v1.0.1 变更摘要: 时间模型对齐 S4 连续时间推进——将"以月为 tick 单位/每月 tick/月度 tick"统一改为"连续时间下的固定步长月级结算（由 S4 实时时钟+加速驱动，每累计 1 游戏月执行一次，即原 monthly tick 内容，但与玩家回合解耦）"；稳态区间（HomeostasisZone）/ R3 机制完全不动。
---

# 生态模拟引擎 GDD

## 1. 系统概述（System Overview）

**一句话定义**：生态模拟引擎是世界自运转的生命系统底座——在连续时间（由 S4 实时时钟+加速驱动）下，以固定步长月级结算（每累计 1 游戏月执行一次，即原 monthly tick 内容，但与玩家回合解耦）模拟种群繁衍、食物链博弈、资源再生、地貌演变与四季循环，通过"稳态区间"机制确保生态系统在正常范围内自我修复、被推过临界点后才发生不可逆相变，从而解决涌现不可控性（R3）。

**设计目标**：
- 让世界"活着"——玩家不干预时生态自运转，干预后生态产生可追踪的连锁响应（对应 D1 干预涟漪、D3 生态稳态与相变）
- 为 R3 提供系统性解法——稳态区间机制让世界不会"莫名其妙地崩溃"，给玩家足够的预警窗口和挽回手段
- 为 S1 干预系统提供可被调节的参数接口——S2 实现 `IInterventionTarget`，注册降雨量、温度、物种出生率等可干预参数
- 为 S3 文明发展系统提供生态基底——土壤肥力、森林存量、水系分布决定文明的资源产出

**与概念文档的对齐**：
- 概念文档 3.1.2 定义了生态模拟引擎的五大机制（种群动态/食物链/资源再生/地貌演变/四季系统）
- 概念文档 D3 描述了生态稳态与相变的预期涌现行为——本 GDD 将其机制化
- 概念文档 R3（P0）是本 GDD 首攻风险——稳态区间 + 预警 + 紧急干预三层解法
- 概念文档 4.1/4.2 定义 MVP 为三级食物链 + 基础四季 + 基础资源再生，核心层扩展到多级食物链 + 地貌演变 + 完整生态指标
- 概念文档 3.1.4 连续时间推进（实时时钟+暂停+加速 1×/2×/5×/20×；底层固定步长月级结算；无玩家回合）

---

## 2. 机制设计（Mechanics）

### 2.1 生态稳态区间机制（Ecological Homeostasis Zone）— R3 核心解法

**设计理由**：纯涌现系统可能"自己走向毁灭"（R3）。稳态区间机制让每个生态子系统在正常区间内自我修复，只有被外力推过临界点才发生不可逆相变。玩家有充足的预警窗口和挽回机会。

每个生态子系统（如某区域的食物链、某片森林的资源量）拥有三段式状态：

| 状态区间 | 数值范围 | 系统行为 | UI 表现 |
|----------|---------|---------|---------|
| **稳态区（Stable）** | 指标 ∈ [下限, 上限] | 负反馈自修复——指标向中值回归 | 青绿色，无预警 |
| **应力区（Stress）** | 指标 ∈ [临界下限, 下限) 或 (上限, 临界上限] | 自修复速率衰减，系统不稳定但仍可恢复 | 暖金色，预警提示 |
| **相变区（Phase Transition）** | 指标 < 临界下限 或 > 临界上限 | 不可逆相变——系统跳转到新状态 | 朱砂红，灾害事件触发 |

**关键参数设计**：

```
// 稳态区间参数模板
struct HomeostasisZone {
    float stableLower;        // 稳态下限（如：种群密度 0.3）
    float stableUpper;        // 稳态上限（如：种群密度 0.8）
    float criticalLower;      // 临界下限（如：种群密度 0.1）——低于则相变
    float criticalUpper;      // 临界上限（如：种群密度 1.2）——高于则相变
    float equilibriumPoint;   // 平衡点（自修复目标值，如：0.5）
    float selfRepairRate;     // 自修复速率（每月向平衡点回归的比例，如 0.15/月）
    float stressDecayFactor;  // 应力区自修复衰减系数（如 0.4——应力区修复速率仅 40%）
    int   stressDurationLimit; // 应力区持续月数上限（如 6 月——超过则强制相变）
    PhaseTransitionType transitionType; // 相变类型（枚举）
}

// 相变类型枚举
enum PhaseTransitionType {
    ForestToDesert,     // 森林 → 荒漠（过度砍伐/旱灾）
    LakeToDryBed,       // 湖泊 → 干涸河床（长期缺水）
    GrasslandToDesert,  // 草原 → 荒漠化（过度放牧）
    SpeciesExtinction,  // 物种灭绝（种群密度低于临界）
    SpeciesOvergrowth,  // 物种爆发（种群密度高于临界，引发食物链失衡）
    FloodplainToSwamp   // 洪泛区 → 沼泽化（长期过湿）
}
```

**自修复公式**（固定步长月级结算 pass）：

```
// 稳态区：向平衡点线性回归
if (value >= stableLower && value <= stableUpper):
    value += (equilibriumPoint - value) * selfRepairRate

// 应力区：衰减速率回归，且累计应力月数
else if (value >= criticalLower && value < stableLower) || (value > stableUpper && value <= criticalUpper):
    value += (equilibriumPoint - value) * selfRepairRate * stressDecayFactor
    stressMonths++
    if (stressMonths >= stressDurationLimit):
        triggerPhaseTransition()  // 应力持续过久 → 强制相变

// 相变区：触发不可逆相变
else:
    triggerPhaseTransition()
```

**设计意图**：
- **三段式而非二值**：应力区是"黄灯"——系统不稳定但还能救回来，给玩家 3-6 个月的反应窗口
- **自修复不是满血**：稳态区每月回归 15%，意味着从极端到平衡需要约 5-6 个月——玩家有时间观察和决策，但不是瞬间恢复
- **应力持续限制**：即使数值没到临界，长期应力也会相变——模拟现实中"长期亚健康最终崩溃"的生态规律

### 2.2 种群动态（Population Dynamics）

每个物种在每个固定步长月级结算 pass 结算一次种群数量，受食物可得性、捕食压力、气候适宜度驱动。

```
// 物种定义
struct Species {
    int id;
    string name;              // 物种名称
    SpeciesType type;         // 类型：Plant / Herbivore / Carnivore
    BiomeType[] habitatBiomes;// 栖息地貌类型
    float population;         // 当前种群数量（区域级）
    float birthRate;          // 月出生率（基础值，受干预可调）
    float deathRate;          // 月死亡率（基础值）
    float carryingCapacity;   // 环境承载力（由食物/空间决定）
    float climateSensitivity; // 气候敏感度（0-1，越高受温/雨影响越大）
    HomeostasisZone zone;     // 种群稳态区间
    int   consecutiveStressMonths; // 连续应力月数（用于相变判定）
}
```

**种群结算公式**（固定步长月级结算 pass）：

```
// Logistic 增长模型 + 捕食压力 + 气候修正
float climateFactor = ComputeClimateFactor(species.climateSensitivity, region.climate);
float foodAvailability = ComputeFoodAvailability(species, region);
float predationPressure = ComputePredationPressure(species, region);

float effectiveBirthRate = species.birthRate * climateFactor * foodAvailability;
float effectiveDeathRate = species.deathRate + predationPressure;

float newPopulation = species.population + 
    (effectiveBirthRate - effectiveDeathRate) * species.population * 
    (1 - species.population / species.carryingCapacity);

// 更新稳态区间状态
UpdateHomeostasisZone(species.zone, newPopulation, species.consecutiveStressMonths);
```

### 2.3 食物链（Food Chain）

**MVP 三级**：植物 → 食草动物 → 食肉动物。**核心层多级**：支持 4-5 级链式关系。

```
// 食物链关系
struct FoodChainLink {
    int predatorSpeciesId;    // 捕食者
    int preySpeciesId;        // 猎物
    float predationRate;      // 每月捕食率（每只捕食者每月吃多少猎物）
    float dependencyRatio;    // 食物依赖比（捕食者对该猎物的依赖程度 0-1）
}

// 食物链健康度（生态指标之一）
// 计算：基于各级物种的种群密度与稳态状态
float ComputeFoodChainHealth(Region region) {
    float plantHealth = GetZoneStatus(region.plants) == Stable ? 1.0 : 0.5;
    float herbivoreHealth = GetZoneStatus(region.herbivores) == Stable ? 1.0 : 0.5;
    float carnivoreHealth = GetZoneStatus(region.carnivores) == Stable ? 1.0 : 0.5;
    return (plantHealth + herbivoreHealth + carnivoreHealth) / 3.0;
}
```

**连锁崩溃规则**：当某一环进入相变区（如植物灭绝），依赖它的上一环在 1-2 个月内被迫进入应力区（食物短缺），再 2-3 个月后若无人为干预则连锁相变。这给了玩家 **3-5 个月的预警窗口**来挽回。

### 2.4 资源再生（Resource Regeneration）

```
// 可再生资源
struct RenewableResource {
    ResourceType type;        // Forest / Fishery / Soil / Water
    float currentAmount;      // 当前储量
    float maxAmount;          // 最大储量
    float regenRate;          // 月再生率（如森林 0.05/月 = 5%）
    float harvestRate;        // 月采集率（由文明系统 S3 驱动）
    HomeostasisZone zone;     // 资源稳态区间
}
```

**再生规则**：
- `currentAmount > stableLower`：按 `regenRate` 每月恢复
- `currentAmount` 在应力区：再生速率衰减 60%，且持续 6 个月进入相变（森林 → 荒漠，渔场 → 枯竭）
- `currentAmount` 在相变区：触发不可逆退化——森林变为荒漠后不再自然恢复森林

### 2.5 地貌演变（Terrain Evolution）— 核心层

缓慢但不可逆的地表变迁，以季节（3 月）为最小变化单位：

| 地貌演变 | 触发条件 | 过程时长 | 结果 |
|----------|---------|---------|------|
| 森林退缩 | 伐木量 > 再生量持续 12 个月 | 12-24 个月 | 森林 → 草原 → 荒漠（逐级退化） |
| 河流改道 | 地震事件或长期洪涝 | 6-12 个月 | 原河道干涸，新河道形成 |
| 荒漠化 | 区域降雨量 < 临界值持续 12 个月 | 12-24 个月 | 草原/森林 → 荒漠 |
| 绿洲化 | 玩家持续增雨 + 水源投放 | 18-36 个月 | 荒漠 → 草原（需持续维护） |

### 2.6 四季循环（Seasonal Cycle）

```
// 季节定义（连续时间：每累计 3 游戏月 = 1 季）
enum Season { Spring, Summer, Autumn, Winter }
// 春 = 1-3 月，夏 = 4-6 月，秋 = 7-9 月，冬 = 10-12 月

struct SeasonalProfile {
    Season season;
    float tempModifier;       // 温度偏移（春 +0, 夏 +15°C, 秋 +0, 冬 -15°C）
    float rainfallModifier;   // 降雨系数（春 1.2, 夏 0.8, 秋 1.0, 冬 0.4）
    float plantGrowthFactor;  // 植物生长系数（春 1.5, 夏 1.2, 秋 0.8, 冬 0.1）
    bool  hibernationActive;  // 冬眠期（食草/食肉动物出生率 ×0.3）
}
```

四季驱动：物种迁徙倾向（冬季食草动物向温暖区域移动）、作物生长周期（春秋种植/夏季生长/秋季收获/冬季休眠）、灾害窗口（夏季旱灾高发、冬季寒潮）。

### 2.7 生态指标体系（Ecological Indicator System）

用于 UI 呈现与灾害预警，每个固定步长月级结算 pass 后更新：

```
// 生态指标（区域级，每月更新）
struct EcologicalIndicator {
    IndicatorType type;       // Biodiversity / FoodChainHealth / ResourceAbundance / TerrainStability / ClimateStability
    float currentValue;       // 当前值 (0-1)
    TrendDirection trend;     // Rising / Falling / Stable
    ZoneStatus zoneStatus;    // Stable / Stress / Critical
    int   monthsInStress;     // 连续应力月数
    string warningMessage;    // 预警文案（如"食草动物种群连续 3 月下降，食物链面临失衡风险"）
}

enum IndicatorType {
    Biodiversity,       // 生物多样性（物种数量与均匀度）
    FoodChainHealth,    // 食物链健康度（各级稳态状态综合）
    ResourceAbundance,  // 资源丰度（可再生资源储量综合）
    TerrainStability,   // 地貌稳定性（地貌演变进度综合）
    ClimateStability    // 气候稳定性（温/雨偏离正常值的程度）
}
```

**预警规则**：当任一指标进入应力区时，UI 推送预警通知（朱砂色标记）；连续 3 个月应力则升级为紧急预警；进入相变区则触发灾害事件。

---

## 3. 动态行为（Dynamics）

### 3.1 预期涌现行为

**D3 — 生态稳态与相变（Ecological Homeostasis & Phase Transition）**
> 食肉动物多了 → 食草动物减少 → 食肉动物因食物短缺而饿死 → 食草动物恢复 → 新平衡。这是稳态区内的负反馈自修复。但如果玩家连续投放狼群将食肉动物推过临界上限，食草动物被吃到灭绝（相变），食肉动物随后也因食物链断裂而灭绝——不可逆的连锁崩溃。

**GDD 对策**：稳态区间机制确保正常波动自修复；只有外力（干预/灾害）将指标推过临界值才触发相变。预警系统在应力区就提示玩家"食草动物种群快速下降"，给玩家 3-5 个月反应窗口。

**D1 — 干预涟漪在生态层的传导**
> 玩家向上游降雨 → 植物生长加速 → 食草动物食物充足 → 出生率上升 → 食肉动物跟随增长 → 三级种群同时扩张 → 资源消耗加剧 → 若超出承载力则进入应力区。

**GDD 对策**：涟漪传导不是瞬时的——每级传导延迟约 1-2 个月，三级传导需 3-5 个月。玩家可在此期间观察到趋势并调整干预。生态指标的趋势箭头（Rising/Falling）帮助玩家预判方向。

**E1 — 季节性种群波动（Seasonal Population Fluctuation）**
> 春季植物疯长 → 食草动物春季繁殖高峰 → 夏季食肉动物活跃捕食 → 秋季种群达到峰值 → 冬季大量个体因寒冷和食物短缺死亡 → 来年春季重新增长。这是稳态区内的正常周期波动，不触发相变。

**GDD 对策**：稳态区间的上下限设计足够宽，容纳正常的季节波动。只有当波动叠加外力（干预/灾害）超出临界值时才触发相变。

**E2 — 资源枯竭-恢复循环（Resource Depletion-Recovery Cycle）**
> 文明伐木量 > 森林再生量 → 森林储量下降 → 进入应力区 → 玩家引导文明减少伐木或投放新森林 → 森林缓慢恢复 → 回到稳态区。如果玩家不管，森林 6 个月后相变为草原，不可逆。

### 3.2 边界情况

| 边界情况 | 处理方式 |
|----------|---------|
| 所有物种同时进入相变区（生态全面崩溃） | 触发"世界死亡"事件——游戏失败条件之一（对应概念文档核心循环失败条件） |
| 玩家通过干预将某物种推到极高后突然停止干预 | 种群超出承载力后自然回落（Logistic 模型的过冲-修正），可能回弹到应力区 |
| 冬季极端低温叠加旱灾 | 气候因子叠加惩罚——植物生长系数 ×0.1 ×0.5 = 极端抑制，食草动物大量死亡，触发食物链应力预警 |
| 某区域所有食肉动物灭绝 | 食草动物失去捕食压力 → 种群爆发 → 超过承载力 → 过食导致植物退化 → 最终食草动物也因食物短缺而崩溃（反转式相变） |
| 玩家在应力区紧急干预挽回 | 允许——应力区是"可挽回"的，干预将指标拉回稳态区后自修复恢复正常速率 |

---

## 4. 系统交互（System Interactions）

### 4.1 IInterventionTarget 接口实现

S2 生态模拟引擎实现 S1 定义的 `IInterventionTarget` 接口，注册可被干预的生态参数：

```
// S2 实现 IInterventionTarget
class EcologySimEngine : IInterventionTarget {
    // 参数注册表——存储所有可被 S1 干预的生态参数
    Dictionary<string, EcoParameter> registeredParameters;

    void RegisterInterventionParameter(string paramKey, float defaultValue, float min, float max) {
        // 注册可干预参数
        // paramKey 示例："rainfall_region_3", "temperature_region_3", 
        //               "species_birthrate_deer", "species_population_wolf"
        registeredParameters[paramKey] = new EcoParameter(defaultValue, min, max);
    }

    void ApplyIntervention(string paramKey, float deltaValue, int durationMonths) {
        // S1 调用：修改生态参数
        // 效果在固定步长月级结算 pass 时结算，非即时生效（由 S4 连续时钟驱动）
        EcoParameter param = registeredParameters[paramKey];
        param.pendingDelta += deltaValue;
        param.remainingDuration = durationMonths;
    }

    float GetParameterValue(string paramKey) {
        // S1 / S8 调用：查询当前参数值（用于 UI 显示与干预预览）
        return registeredParameters[paramKey].currentValue;
    }
}

// S2 注册的可干预参数清单
// ┌─────────────────────────────┬──────────────┬─────────┬────────┐
// │ paramKey                    │ default      │ min     │ max    │
// ├─────────────────────────────┼──────────────┼─────────┼────────┤
// │ rainfall_{regionId}         │ 1.0 (系数)   │ 0.0     │ 3.0    │
// │ temperature_{regionId}      │ 0.0 (偏移°C) │ -10.0   │ +10.0  │
// │ birthRate_{speciesId}       │ 0.1 (基础值) │ 0.0     │ 0.5    │
// │ population_{speciesId}      │ (当前值)     │ 0.0     │ ∞      │
// │ regenRate_{resourceType}    │ 0.05/月      │ 0.0     │ 0.3    │
// └─────────────────────────────┴──────────────┴─────────┴────────┘
```

### 4.2 交互关系

| 交互系统 | 方向 | 内容 |
|----------|------|------|
| S1 干预系统 | S1 → S2 | 气候调节修改降雨/温度参数；物种迁移修改种群位置；灾厄/恩赐影响种群动态；S1 的反噬效果（洪涝/疫病）通过 S2 的生态连锁实现 |
| S1 反噬机制 | S2 → S1 | S2 的生态连锁反应是 S1 反噬的"执行器"——S1 检测到过度干预后，通过 S2 的参数接口施加负面连锁（如洪涝降低土壤肥力、疫病提升死亡率） |
| S3 文明发展系统 | S2 → S3 | S2 向 S3 输出：土壤肥力（决定农业产出）、森林存量（决定建材）、渔场储量（决定渔获）、水系分布（决定聚落选址可行性） |
| S3 文明发展系统 | S3 → S2 | S3 向 S2 输入：伐木量/采集量/渔获量（驱动资源再生模型的 harvestRate）、开垦面积（驱动地貌演变） |
| S4 时间推进系统 | S4 → S2 | 每累计 1 游戏月驱动 S2 结算 pass（原月度 tick，与玩家回合解耦）；推进季节更替（每累计 3 游戏月切换季节）；推进地貌演变的季节计数器 |
| S6 涌现叙事引擎 | S2 → S6 | S2 输出生态事件（物种灭绝、地貌相变、资源枯竭、种群爆发）作为叙事素材 |
| S8 UI/HUD 系统 | S2 → S8 | S2 输出生态指标（5 项）+ 稳态区间状态 + 预警信息供 UI 呈现 |

### 4.3 固定步长月级结算 pass 顺序（由 S4 连续时间驱动）

每累计 1 游戏月（由 S4 累加器触发，与玩家回合解耦），S2 按以下顺序结算（确保因果一致性）：

```
// 月级结算 pass 流水线
1. 读取 S1 干预效果 → 应用待生效的参数变更（降雨/温度/出生率调整）
2. 推进季节（每 3 月切换）→ 更新 SeasonalProfile
3. 结算植物种群（气候因子 + 降雨 + 资源再生）
4. 结算食草动物（食物可得性 + 捕食压力 + 气候因子）
5. 结算食肉动物（食物可得性 + 气候因子）
6. 结算资源再生（harvestRate 来自 S3 + regenRate）
7. 推进地貌演变（检查触发条件）
8. 更新稳态区间（所有物种/资源的 zone 状态 + 应力月数）
9. 检查相变触发（临界值/应力持续上限）
10. 更新生态指标（5 项指标 + 趋势 + 预警）
11. 输出事件 → S6（叙事引擎）/ S8（UI 预警）
```

---

## 5. 范围分层（Scope Layering）

### 5.1 MVP 范围

| 维度 | MVP 实现 |
|------|---------|
| 食物链 | 三级：植物 → 食草动物 → 食肉动物，每地貌 2-3 种代表物种 |
| 种群动态 | Logistic 增长模型 + 捕食压力 + 气候因子 |
| 资源再生 | 森林 + 渔场两种可再生资源，基础再生/采集模型 |
| 四季循环 | 4 季节切换，温/雨/植物生长系数变化，冬眠机制 |
| 稳态区间 | 完整实现——三段式状态（稳态/应力/相变）+ 自修复 + 预警。**这是 R3 的 MVP 验证核心** |
| 生态指标 | 3 项：FoodChainHealth / ResourceAbundance / ClimateStability |
| 地貌演变 | 不实现（核心层引入） |
| IInterventionTarget | 注册 3 项参数：rainfall / temperature / birthRate |
| 预警 | 应力区预警通知 + 相变事件 |

### 5.2 核心层范围

| 维度 | 核心层实现 |
|------|-----------|
| 食物链 | 多级（4-5 级），支持杂食性物种、多猎物依赖 |
| 种群动态 | 增加迁徙倾向（季节性迁徙）、物种间竞争关系 |
| 资源再生 | 增加土壤肥力、水资源，完整再生/退化模型 |
| 四季循环 | 增加季节性灾害窗口（夏旱/冬寒/春秋疫病） |
| 稳态区间 | 增加应力持续上限的渐进式恶化（应力区每月小幅恶化而非固定 6 月后跳变） |
| 生态指标 | 完整 5 项（+Biodiversity / TerrainStability） |
| 地貌演变 | 完整实现——森林退缩/河流改道/荒漠化/绿洲化 |
| IInterventionTarget | 注册全部参数（含 species population / regenRate） |
| 预警 | 多级预警（趋势预警 → 应力预警 → 相变预警），灾难前兆事件 |

### 5.3 延展层方向

- 物种演化（性状漂移、自然选择驱动变异）
- 驯化与育种（野生动物 → 家畜/作物）
- 极端地貌生态（火山、冰川群系）
- 生态链入侵（外来物种破坏本地食物链）

---

## 6. 风险与未决问题

### 6.1 本系统特有风险

| # | 风险 | 级别 | 说明 | 待解方向 |
|---|------|------|------|---------|
| EC1 | 稳态区间参数调优 | P0 | stableLower/Upper/criticalLower/Upper/selfRepairRate 的初始值如果偏差大，要么世界太脆弱（动不动相变），要么太稳定（干预无感） | MVP 原型阶段以"3 级食物链 + 降雨干预"为测试床，观察无干预时世界能自运转多久（目标 ≥ 24 个月不自然崩溃），以及干预后多久产生可见响应（目标 1-2 个月可见趋势变化） |
| EC2 | 相变的不可逆性与玩家公平感 | P0 | 玩家可能感到"我只是多砍了点树，森林怎么就永久变荒漠了？"——相变的不可逆性可能产生挫败感 | 1) 应力区预警给玩家 3-6 个月窗口；2) 相变不是瞬间跳变——有 12-24 个月的渐进过程（森林→草原→荒漠逐级退化），每级都可挽回；3) 极端情况下 S1 紧急干预（天降甘霖）可部分逆转 |
| EC3 | 生态连锁的蝴蝶效应不可预测 | P1 | 干预一个参数可能通过食物链传导产生设计师未预期的远期后果 | 1) 生态指标的趋势箭头帮助玩家预判方向；2) 因果链追踪（与 S1 共享）记录传导路径；3) playtest 观察玩家是否能学会"小步干预→观察→再调整"的节奏 |
| EC4 | 固定步长月级结算的计算量 | P1 | 完整四层运转时，每区域每物种每结算 pass 结算一次种群动态+稳态+指标，地图大时计算量可观 | 1) 固定步长批量计算（非每帧）；2) 区域级 LOD——远离聚落的区域用简化模型（只算种群总量不算个体）；3) 评估 DOTS/ECS 架构（技术风险 T1/T4 关联） |

### 6.2 与 P0/P1 风险的关联

| 概念文档风险 | 本 GDD 的攻关措施 |
|-------------|------------------|
| **R3** 涌现不可控性与世界崩坏 | **三层解法**：① 稳态区间机制——正常波动自修复，只有外力推过临界点才相变；② 生态指标预警系统——应力区即预警，给玩家 3-6 个月反应窗口；③ 与 S1 紧急干预配合——当世界走向崩溃时，玩家有天降甘霖等紧急手段力挽狂澜 |
| **R1** 间接控制 agency 感 | S2 的参数接口让 S1 的干预有真实的生态响应——降雨后植物 1 个月内可见变化，食草动物 2-3 个月后跟随波动，因果链可追踪 |
| **R2** 干预与涌现性矛盾 | S2 的稳态区间是 R2 的"弹簧"——适度干预被自修复吸收（无后果），过度干预推过临界则相变（有后果）。平衡点从世界复杂性中涌现 |

---

## 7. 验证标准（Validation Criteria）

### 7.1 MVP 验证假设

| # | 假设 | 验证方法 | 通过标准 | 不通过时的调整方向 |
|---|------|---------|---------|-------------------|
| V1 | 无干预时世界能自运转不崩溃 | 原型跑 50 局无玩家干预的自动模拟，每局 24 个月 | ≥ 80% 的局在 24 个月内未发生全面生态崩溃（允许局部相变） | 拓宽稳态区间（提高 criticalLower / 降低 criticalUpper）；提高 selfRepairRate |
| V2 | 玩家干预后 1-2 个月内可见生态响应 | Playtest 观察：玩家降雨后是否能通过生态指标/视觉变化感知到效果 | ≥ 70% 的玩家在干预后 2 个月内主动注意到生态变化 | 缩短响应延迟（从 1-2 月缩短为 1 月）；增强指标变化的视觉表现力 |
| V3 | 稳态区间预警给玩家足够反应时间 | Playtest 观察：当某指标进入应力区后，玩家是否在相变前采取了行动 | ≥ 60% 的应力区预警被玩家响应并成功挽回（拉回稳态区） | 延长 stressDurationLimit（如从 6 月改为 9 月）；增强预警提示强度 |
| V4 | 相变不可逆时玩家感到公平而非挫败 | Playtest 提问：当森林相变为荒漠后，玩家是否理解"是因为我过度砍伐"而非"系统随机惩罚" | ≥ 50% 的相变事件被玩家正确归因到自己的行为 | 增强相变前兆的视觉提示（森林逐渐变稀疏）；在相变事件文案中明确标注原因 |
| V5 | 三级食物链能产生有意义的连锁波动 | 原型数据观察：干预顶级捕食者（食肉动物）后，是否能看到食草动物和植物的连锁响应 | 3 级连锁响应在 3-5 个月内可观测（数据曲线有明显传导） | 调整 predationRate 和 dependencyRatio；缩短传导延迟 |

### 7.2 验证时机

- V1-V5 均在 **MVP 原型阶段** 验证（3-4 周内）
- V1 是最关键的前提——如果无干预时世界都不能自运转，说明稳态参数需要根本性调整
- V3 + V4 共同验证 R3 是否被有效攻关——预警窗口 + 公平感缺一不可

---

## 8. 评审检查清单

| # | 检查项 | 通过标准 | 状态 |
|---|--------|---------|------|
| EC-C1 | 系统概述 | 一句话定义 + 设计目标 + 概念文档对齐（3.1.2/D3/R3/4.1-4.2/3.1.4） | ✅ 已完成 |
| EC-C2 | 稳态区间机制 | 三段式状态定义 + 参数设计（区间边界/自修复速率/相变阈值）+ 自修复公式 + 相变类型枚举 | ✅ 已完成 |
| EC-C3 | 五大机制覆盖 | 种群动态/食物链/资源再生/地貌演变/四季循环各有数据模型与规则 | ✅ 已完成 |
| EC-C4 | 数据模型 | Species / FoodChainLink / RenewableResource / HomeostasisZone / EcologicalIndicator 等 struct 定义 | ✅ 已完成 |
| EC-C5 | IInterventionTarget 实现 | S2 实现接口 + 注册参数清单（rainfall/temperature/birthRate/population/regenRate） | ✅ 已完成 |
| EC-C6 | 生态指标体系 | 5 项指标定义 + 趋势/状态/预警字段 + 预警规则 | ✅ 已完成 |
| EC-C7 | 涌现行为预测 | ≥ 4 个预期涌现行为 + 边界情况处理 | ✅ 已完成 |
| EC-C8 | 系统交互 | 与 S1/S3/S4/S6/S8 的交互关系 + 固定步长月级结算 pass 顺序 | ✅ 已完成 |
| EC-C9 | 范围分层 | MVP/核心层/延展层各有明确范围边界，MVP 含稳态区间核心 | ✅ 已完成 |
| EC-C10 | R3 攻关 | 三层解法（稳态区间 + 预警系统 + 紧急干预配合）+ 与 S1 反噬机制衔接 | ✅ 已完成 |
| EC-C11 | 验证标准 | ≥ 4 个 MVP 假设（实际 5 个）+ 验证方法 + 通过标准 + 不通过调整方向 | ✅ 已完成 |
| EC-C12 | 时间单位一致 | 所有时间引用以"连续时间下的固定步长月级结算 pass"为单位（持续月数/冷却/应力窗口）；"月"为模拟粒度但非玩家回合 | ✅ v1.0.1 对齐 S4 连续时间 |

---

> **文策渊注**：S2 生态模拟引擎 GDD 的核心设计赌注是"稳态区间机制"——它同时服务 R3（防止世界不可控崩溃）和 R2（干预有后果但不过度惩罚）。三层解法的逻辑是：① 稳态区自修复让世界有韧性；② 应力区预警给玩家反应窗口；③ 相变不可逆但渐进（12-24 个月逐级退化）且可被紧急干预部分逆转。MVP 最关键的验证是 V1——无干预时世界能否自运转 24 个月不崩溃。如果 V1 不通过，说明稳态参数需要根本性调整，后续所有验证都失去基础。建议 MVP 原型优先实现：三级食物链 + 降雨/温度干预接口 + 稳态区间 + 3 项生态指标 + 预警通知——这 5 个要素覆盖了 R3 攻关的全部验证需求。

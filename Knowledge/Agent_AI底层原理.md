# Agent AI 底层原理

> 基于反编译 `TaleWorlds.MountAndBlade.dll`、`TaleWorlds.Core.dll`、`TaleWorlds.CampaignSystem.dll` 的分析。
> 配合 [Agent AI 冲突解决与接管策略](Agent_AI冲突解决与接管策略.md) 阅读。

---

## 一、架构总览：两个 AI 域 + 三层 AI 栈

### 1.1 两个独立域

| 域 | 所在层 | 核心类 | 空间 | 用途 |
|---|--------|--------|------|------|
| **MobilePartyAI** | 大地图（Campaign） | `MobilePartyAi` | 2D NavMesh | 部队行军、围城、劫掠、巡逻 |
| **AgentAI** | 任务场景（Mission） | `Agent` + `HumanAIComponent` | 3D NavMesh | 战斗、攻城、城镇内 NPC 行为 |

### 1.2 三层 AI 栈

```
┌─────────────────────────────────────────────┐
│  战略层 (Strategic)                           │
│  MobilePartyAi → AiBehavior 枚举              │
│  决定："这支部队该去哪？围城还是巡逻？"          │
├─────────────────────────────────────────────┤
│  战术层 (Tactical)                            │
│  TeamAIComponent → FormationAI → Behavior     │
│  决定："左翼冲锋，右翼坚守，弓兵放箭"            │
├─────────────────────────────────────────────┤
│  个体层 (Individual Agent)                    │
│  Agent + HumanAIComponent + CommonAIComponent │
│  决定："我该砍谁？该挡哪边？该跑还是打？"        │
└─────────────────────────────────────────────┘
```

---

## 二、Agent 是什么：复合 GameEntity 装配管线

Agent 不是简单对象，而是**多层嵌套的 GameEntity 组合体**：

```
Agent
 ├── BodyProperties          (FaceKey, Age, 肤色等)
 ├── DrivenProperties        (重力/速度/碰撞 — 84个参数)
 ├── HumanAIComponent        (AI行为子组件 — 行为曲线+物体交互)
 ├── CommonAIComponent       (士气/恐慌/撤退/坐骑预留)
 ├── AgentVisuals            (可视化子GameEntity)
 │    ├── EquipmentData      (武器/盔甲数据)
 │    ├── Monster            (HP/体重/移动速度参数)
 │    ├── Skeleton
 │    │    ├── Mesh          (蒙皮模型)
 │    │    ├── BoundingBox   (物理包围盒)
 │    │    ├── Bone
 │    │    │    ├── RagdollBody   (布娃娃物理)
 │    │    │    ├── CollisionBody (HitBox碰撞体)
 │    │    │    └── Joint         (D6Joint物理关节)
 │    └── SpawnEquipment     (绑定在手部骨骼上的武器Entity)
```

### 2.1 装配数据流

```
lords.xml (race属性)
  → monsters.xml (MonsterId → HP/体重/移动速度)
    → monster_usage_sets.xml (骨骼动画集: 跳跃/骑马/坐姿/战斗动作)
  → skins.xml (头/肢体 mesh 按 race + faceKey 查询)

初始化时序:
  MissionAgentHandler.SpawnWanderingAgent()
    → Agent.Initialize(bodyProperties, monster, equipment, ...)
      → HumanAIComponent.Initialize() — 注册到 AgentComponent 列表
      → CommonAIComponent.Initialize() — 初始化士气/恐慌
      → Mission.Current.OnAgentCreated(agent) — 触发所有 MissionLogic 回调
```

**关键含义**：Agent 的视觉、物理、AI 三层解耦。替换 AI 组件不影响视觉，这是 mod 自定义 AI 的基础。

### 2.2 关键枚举

```csharp
// Agent 状态
public enum AgentState { None, Active, Routed, Unconscious, Killed, Deleted }

// 能力标志 (位掩码)
[Flags] public enum AgentFlag : uint {
    Mountable, CanJump, CanRear, CanAttack, CanDefend,
    CanCharge, CanClimbLadders, CanSprint, IsHumanoid (0x800),
    CanGetScared, CanRide, CanWieldWeapon, CanCrouch,
    CanGetAlarmed, CanWander, CanKick, CanRetreat, ...
}

// 装备槽位
public enum EquipmentIndex {
    Weapon0..3, ExtraWeaponSlot,        // 武器 0-4
    Head, Body, Leg, Gloves, Cape,      // 防具
    Horse = 10, HorseHarness = 11       // 坐骑
}
```

---

## 三、五层 AI 控制参数体系

控制一个 Agent 需要操作**五个正交维度**：

### 3.1 ControllerType —— 谁说了算

```csharp
public enum ControllerType { None, AI, Player }
```

- **`AI`**：引擎全权控制 — 自动寻敌、攻击、格挡
- **`Player`**：键盘鼠标驱动
- **`None`**：无人控制 — mod 完全自主编程的入口

设为 `None` 后，`MovementFlags` 和 `EventControlFlags` 直接生效，引擎 AI 完全退场。

### 3.2 AIStateFlag —— 行为状态位掩码（含 AlarmStateMask 战斗姿态）

```csharp
[Flags] public enum AIStateFlag : uint {
    None = 0, Cautious = 1, PatrollingCautious = 2, Alarmed = 3,
    Paused = 8, UseObjectMoving = 0x10, UseObjectUsing = 0x20, UseObjectWaiting = 0x40,
    ColumnwiseFollow = 0x100, AlarmStateMask = 3   // 🔴 低 2 位是独立"战斗姿态"字段
}
```

| Flag | 效果 |
|------|------|
| `None` | 无视敌军，不主动攻击 |
| `Alarmed` | 警戒 — **自动调整 lookDirection 寻找攻击目标** |
| `Cautious` | 谨慎模式 |
| `PatrollingCautious` | 巡逻谨慎 |
| `Paused` | 暂停 AI 思考 |
| `UseObjectMoving` | 正在走向椅子/器械 |
| `UseObjectUsing` | 正在使用椅子/器械 |
| `Guard` / `ColumnwiseFollow` | 阵型跟随 |

**🔴 低 2 位（`& AlarmStateMask`）是"战斗姿态"字段**，判定接口：

```csharp
IsAlarmStateNormal()  → (AIStateFlags & AlarmStateMask) == 0      // 正常
IsCautious()          → (AIStateFlags & AlarmStateMask) == 1      // 谨慎
IsPatrollingCautious()→ (AIStateFlags & AlarmStateMask) == 2      // 巡逻谨慎
IsAlarmed()           → (AIStateFlags & AlarmStateMask) == 3      // 警戒（主动寻敌）
SetAlarmState(AIStateFlag.Cautious/PatrollingCautious/Alarmed)    // 设置姿态
```

**只有 `Alarmed` 才主动寻敌攻击**；Cautious 系姿态下 AI 不主动出手——这是实现「认输 NPC 只防御不进攻」的开关之一（见 5.2）。其余高位 flag 是行为状态，可组合。

### 3.3 AIScriptedFrameFlags —— 脚本化移动控制

```csharp
[Flags] public enum AIScriptedFrameFlags {
    None = 0,
    GoToPosition = 1,        // 有脚本化目标位置(锁)
    NoAttack = 2,            // 不攻击
    ConsiderRotation = 4,    // 到达后考虑朝向
    NeverSlowDown = 8,       // 跑步前进
    DoNotRun = 0x10,         // 走路前进
    GoWithoutMount = 0x20,   // 下马
    RangerCanMoveForClearTarget = 0x80,
    InConversation = 0x100,  // 对话模式
    Crouch = 0x200
}
```

当调用 `agent.SetScriptedPosition(ref pos, false, flags)` 时：
1. 引擎接管 Agent 的**移动控制**
2. 通过 3D NavMesh 计算最短路径
3. 每帧自动更新位置，直到到达目标
4. `NoAttack` 阻止路上自动攻击
5. `NeverSlowDown` / `DoNotRun` 控制速度

**本质**：不是逐帧操控，而是**给引擎一个目标位置合同**，引擎负责寻路+避障+动画。

**关键守卫** — `CanBeAssignedForScriptedMovement()`：
```csharp
public bool CanBeAssignedForScriptedMovement() {
    return IsActive() && IsAIControlled && !IsDetachedFromFormation
        && !IsRunningAway
        && (GetScriptedFlags() & AIScriptedFrameFlags.GoToPosition) == 0  // 还没有脚本化目标
        && !InteractingWithAnyGameObject();  // 没有在跟物体交互
}
```

返回 false 时不应再调 `SetScriptedPosition`，否则原生引擎可能覆盖或冲突。

### 3.4 SetAIBehaviorParams —— 战斗行为曲线

`HumanAIComponent.BehaviorValues` 是**分段线性函数**，定义"距离 → 攻击意愿"映射：

```csharp
public struct BehaviorValues {
    float y1;       // x=0 处的值
    float x2, y2;   // 第一个拐点
    float x3, y3;   // 第二个拐点
    // GetValueAt(x) 在 (0,y1)→(x2,y2)→(x3,y3) 之间线性插值
}
```

**7 种行为类型**，每种一条独立曲线：

| AISimpleBehaviorKind | 含义 | Default 预设曲线 |
|---------------------|------|------------------|
| `GoToPos` | 向目标移动的积极性 | (0,3)→(7,5)→(20,6) |
| `Melee` | 近战攻击意愿 | (0,8)→(7,4)→(20,1) |
| `Ranged` | 远程攻击意愿 | (0,2)→(7,4)→(20,5) |
| `ChargeHorseback` | 骑马冲锋意愿 | (0,2)→(25,5)→(30,5) |
| `RangedHorseback` | 骑马射箭意愿 | (0,2)→(15,6.5)→(30,5.5) |
| `AttackEntityMelee` | 近战攻击实体 | (0,5)→(12,7.5)→(30,4) |
| `AttackEntityRanged` | 远程攻击实体 | (0,5.5)→(12,8)→(30,4.5) |

引擎每帧用**当前距离**查 7 条曲线得 7 个权重，选最高的执行。

**预设集**（`BehaviorValueSet`，v1.4.7 反编译实测曲线）：不同战术场景切换不同预设。

| 预设 | 用途 | Melee 曲线（近战攻击意愿） | 其余要点 |
|------|------|---------------------------|----------|
| `Default` | 默认 | (0,8)→(7,4)→(20,1) | 标准战斗 |
| `DefensiveArrangementMove` | **盾墙/圆阵/方阵**（官方"防守"预设） | **(0,4)→(5,0)→(20,0)** | 🔴 5m 开外攻击权重=0，Ranged/骑马系全 0 |
| `Follow` | 跟随阵型 | (0,6)→(7,4)→(20,0) | Ranged/骑马系全 0 |
| `DefaultMove` | 常规移动 | (0,8)→(7,5)→(20,0.01) | Ranged≈0 |
| `Charge` | 冲锋 | (0,8)→(7,4)→(20,1) | RangedHorseback 加强 |
| `DefaultDetached` | **脱队单兵**（`IsDetachedFromFormation`） | (0,8)→(7,4)→(20,1) | 默认值 |

**切换机制**（重要，mod 覆盖曲线会被它踩掉）：
- `HumanAIComponent.RefreshBehaviorValues(移动指令, 阵型指令)` 按阵型指令切预设 → `SetBehaviorValueSet(...)` 写 `_behaviorValues` 数组并置 `_hasNewBehaviorValues`
- `HumanAIComponent.OnTickParallel` 每帧 `SyncBehaviorParamsIfNecessary()` → 有变更才推送 `Agent.SetAllBehaviorParams(_behaviorValues)` → native
- **🔴 只有"变更时"才推送**：阵型外（`Formation == null` / 脱队）没人调 RefreshBehaviorValues，直接调 `Agent.SetAIBehaviorParams` 直推 native 后**不会被覆盖**；战场阵型里则可能被刷回预设
- `HumanAIComponent.OverrideBehaviorParams(kind, y1,x2,y2,x3,y3)`：覆盖单条曲线并标记 `_lastBehaviorValueSet = Overriden`（此后预设切换不再生效，直到显式切回）

### 3.5 DrivenProperties —— 84 个微调参数（攻防概率）

🔴 **名称实测修正**：v1.4.7 `TaleWorlds.Core.dll` 枚举里有两个"攻击"参数，别混：

```csharp
public enum DrivenProperty {
    // 格挡/招架（决定"挡不挡得住"）
    AIBlockOnDecideAbility = 5,      // 格挡能力
    AIParryOnDecideAbility = 6,      // 招架能力
    AIUseShieldAgainstEnemyMissileProbability = 7,
    AIAttackOnParryChance = 8,       // 招架后反击概率
    AIAttackOnParryTiming = 9,
    AIDecideOnAttackChance = 10,     // 🔴 主动攻击决策概率（认输时清零它）
    AIParryOnAttackAbility = 11,     // 被攻击时的招架能力
    AiKick = 12,                     // 踢腿频率
    AIParryOnAttackingContinueAbility = 17,
    AIAttackOnDecideChance = 36,     // 攻击决策时机（另一回事）
    AttributeCourage = 91,           // 勇气（影响逃跑判定）
    // ... 共 84 个（含 SwingSpeedMultiplier、ArmorEncumbrance、MountManeuver 等）
}
```

**读写路径**（v1.4.7 实测）：

```csharp
// 写：走 AgentDrivenProperties（公开 setter）→ 然后显式推送
agent.AgentDrivenProperties.AIBlockOnDecideAbility = 100f;   // 或 SetStat(DrivenProperty.X, v)
agent.UpdateCustomDrivenProperties();   // 🔴 推当前数组原样上 native —— 用这个！
// agent.UpdateAgentProperties()         // ⚠️ 会按装备/技能重算覆盖你的自定义值，
                                        //    恢复原状时用它（一键还原官方值）
// 读
agent.GetAgentDrivenPropertyValue(DrivenProperty.AIAttackOnDecideChance);
```

### 3.6 攻防判定语义：Block（格挡）vs Parry（招架）🔴

**引擎层就有区分**：`ActionStage` 枚举里是 `Defend` 与 `DefendParry` 两个独立阶段，对应两套判定。

| | `AIBlockOnDecideAbility`=5（格挡） | `AIParryOnDecideAbility`=6（招架） |
|---|---|---|
| 动作 | 朝攻击方向**提前**举武器/盾牌架住 | 攻击**即将命中瞬间**（窄时间窗）格挡 = 完美格挡 |
| 时机 | 命中前任意时刻 | 最后 ~0.2s 内（`AiAttackOnParryTiming`=9 调窗口） |
| 效果 | 伤害归零；盾牌格挡耗盾耐久 | 伤害归零 + **攻击者被弹开硬直、连招打断** + 招架方获得快速反击窗口；盾牌完美格挡不耗耐久 |
| 失败 | 方向错 → 中刀 | 时机错 → 退化为普通格挡或中刀 |
| 定位 | 基础防御（会不会防） | 高级形态（会不会卡时机） |

**🔴 判定顺序**：先决定防不防（block），再决定卡不卡时机（parry）——两个都要拉高才有"一直格挡"；只拉 parry 不拉 block，AI 可能干脆不举防。

**兄弟参数全貌（数值已实测）**：

| 参数 | 值 | 作用 |
|------|----|------|
| `AiTryChamberAttackOnDecide` | 7 | 拼刀（chamber，以攻代守）概率 |
| `AIAttackOnParryChance` | 8 | **招架成功后反击概率（独立攻击路径！）** |
| `AiAttackOnParryTiming` | 9 | 招架时机窗口 |
| `AIParryOnAttackAbility` | 11 | 攻击途中收招招架的能力 |
| `AIParryOnAttackingContinueAbility` | 17 | 招架后连续攻击能力 |
| `AiParryDecisionChangeValue` | 27 | 招架决策阈值偏移 |
| `AISetNoDefendTimerAfterParryingAbility` | 48 | 招架成功后对方短暂"不设防"惩罚窗口（招架强的引擎证据） |

**🔴 反直觉坑**：反击走 `AIAttackOnParryChance`（招架成功触发），**不经过** `AIDecideOnAttackChance`（主动攻击）——格挡/招架拉满后，只清 `AIDecideOnAttackChance` 挡不住反击。要让 NPC"只防守永不出手"，三个都要清零（见 5.8 配方 ④）。

---

## 四、Native C++ 引擎：C# 只是薄封装

**最关键的认知**：大量核心逻辑在 native C++ 中，C# 层只是传参接口。

```
C# 层                           Native C++ 层
─────────────────────────       ─────────────────────────────────
Agent.SetScriptedPosition()  →  MBAPI.IMBAgent.SetScriptedPosition()
Agent.AIStateFlags { set }   →  MBAPI.IMBAgent.SetAIStateFlags()
Agent.SetAIBehaviorParams()  →  内部行为曲线查表 + 决策
Agent.UpdateDrivenProperties →  MBAPI.IMBAgent.UpdateDrivenProperties()
Agent.SetTargetAgent()       →  MBAPI.IMBAgent.SetTargetAgent()
```

**推论**：
- `ilspycmd` 反编译**只能看到调用上下文和参数用法**，看不到内部实现
- **控制台指令是最佳参考** — `agent.goto` 等指令的代码路径直接展示官方 API 正确调用方式
- **不能仅凭 API 名字推断行为** — 必须反编译看实际调用链

---

## 五、战斗 AI 决策流

每一帧的战斗 AI 经历以下决策链（🔴 第 3~6 层核心决策在 native C++，C# 只有 API 面）：

```
1. Team 判定       → 敌我关系（native 团队图）
2. 战斗姿态         → Alarm State（AlarmFactor 状态机，见 5.2）
3. 行为权重计算     → 7 条 BehaviorValues 曲线按距离查权重，最高者当选
4. 目标选择         → 视野/威胁度 → SetTargetAgent
5. 攻防决策         → DrivenProperties 概率微调（Block/Parry/反击/踢腿）
6. 动作输出         → MovementControlFlag 位掩码 → 动画系统
```

### 5.1 第 1 层：Team 判定

- `Team.IsEnemyOf(otherTeam)` → `MBTeam.IsEnemyOf(otherTeam.MBTeam)`——**native 团队关系图**（敌/友/中立由引擎维护），C# 无内部实现可看
- `Agent.IsEnemyOf(agent)` / `IsFriendOf(agent)` 是同一张图的 agent 级快捷接口
- 玩家视角三分：`TeamSideEnum` = `PlayerTeam` / `PlayerAllyTeam` / `EnemyTeam`
- **🔴 改敌我必须双向**：`team.SetIsEnemyOf(other, true)` + `other.SetIsEnemyOf(team, true)` 各调一遍（本 mod CombatManager.SetEnemy 已是官方双写写法）；单向设置会被 native 图覆盖
- 中立/静态团队不参与战斗判定（如 Mission 的静态物体团队）

### 5.2 第 2 层：战斗姿态（Alarm State）——🔴 完整状态机（AlarmedBehaviorGroup 反编译实测）

`AlarmFactor`（0~2 浮点，每帧积分）驱动 4 态转移：

| 姿态 | AlarmStateMask 值 | 进入条件 | 行为表现 |
|------|------------------|----------|----------|
| `None`（正常） | 0 | `AlarmFactor < 0.0001` | 不寻敌 |
| `Cautious`（谨慎） | 1 | `AlarmFactor ≥ 1`（视觉/声音/飞矢/外部注入） | CautiousBehavior 激活：举武器守卫 + 环视动画，**不主动攻击** |
| `PatrollingCautious`（巡逻谨慎） | 2 | `AlarmFactor ≥ 2` 且看见尸体（`hasVisualOnCorpse`） | CautiousBehavior 强化：每帧 `SetWeaponGuard` + 巡逻嫌疑点（官方守卫用法） |
| `Alarmed`（警戒） | 3 | `AlarmFactor ≥ 2` 且看见敌人；或 `≥ 1` 且看见敌人且敌人贴身（<1m） | **唯一主动寻敌攻击的姿态**；喊叫传播警报 |

**AlarmFactor 积分来源**（全部实测）：
- **视觉** `GetVisualFactor`：椭圆视线锥（水平 ~85.5°、垂直 ~51.3° 半角）× 距离衰减 `575/(5 + d²·1.1)` × 日夜光照（白天 0.7/1.0，夜晚 0.2/0.15）× 装备潜行加成 × 蹲姿（×0.8~0.9）× StealthBox 遮蔽；贴身（<6.5×胶囊半径）×15；看见敌人 ×1，看见玩家友军 ×0.5；看见尸体 → 尸体路径升级
- **声音** `GetSoundFactor`：移动速度（跑>走>潜行）、地形/涉水（×4）、距离衰减 `20 + d²·2.5`；另有 `Mission.AddSoundAlarmFactorToAgents`（半径 `√(level²/0.7 - 20)`）——**喊叫/动静会传播给附近 agent**
- **飞矢** `HandleMissiles`：导弹轨迹最近点 < 阈值 → 加 AlarmFactor
- **外部注入** `AddAlarmFactor(2f, pos)`：直接拉满（越狱守卫、本 mod 目击系统可复用）
- 衰减速率：`PatrollingCautious` **0.025/s**（很慢），其余 0.125/s（可移动）或 0.08/s（不可移动）；上限 2；**跌破 1 时重置为 0.3 防抖**（防止临界抖动）
- 冷静流程：姿态非 normal 持续 10s 后，每秒检查 15m 内无战斗源（`IsNearDanger`）→ 解除脚本化移动
- 激活外围效果：换 `AlarmedActionSetCode` 动作集、`SetWatchState(2)`、`Navigator.SetItemsVisibility(false)`、解除移动锁；警报广播：Alarmed 后每 10s `MakeVoice(Yell)` + `AddSoundAlarmFactorToAgents(10f)`
- 前置条件：agent 需带 `CanGetAlarmed|CanWander` 类 flag（AgentFlag 0x14000）且 AI 控制才会跑状态机

**🔴 mod 影响面**：`Alarmed` 才是"开战"信号——本 mod 目击→围观→质问的流程走的是自己的 `AgentBrain`（SuspendVanillaAI 冻结了行为组），不经过此状态机；但如果想让某 NPC 表现"警惕但不打"，直接 `SetAlarmState(Cautious/PatrollingCautious)` 即可（见 5.8 认输配方）。

### 5.3 第 3 层：行为权重计算

- 每帧用当前到目标距离查 7 条 `BehaviorValues` 曲线（分段线性插值），**最高权重行为当选**（native 执行，C# 只暴露 `SetAIBehaviorParams` 推曲线）
- **算例（Default 预设）**：`Melee(x) = 8 - 4x/7` vs `GoToPos(x) = 3 + 2x/7`，交叉点 **~5.8m**——敌人 5.8m 内倾向出手，5.8m 外倾向接近。这是原版近战"贴脸就打、远了靠拢"的量化解释
- **🔴 认输配方的隐藏坑**：官方防守预设 `DefensiveArrangementMove` 贴身时 `Melee(0)=4 > GoToPos(0)=3`——**盾墙兵贴身照样出手**。所以 5.8 配方必须把 Melee 曲线 y1 也清零，否则投降者贴脸就挥刀
- 曲线全 0 的边界行为（native 无行为可选）未实测，慎用；配合脚本化锁位（`SetScriptedPosition`）更稳

### 5.4 第 4 层：目标选择

- 目标由 **native 每帧维护**，C# 暴露的面：
  - `GetTargetAgent()` / `SetTargetAgent(agent)`（传 null 清除）
  - `GetLastTargetVisibilityState()` → `AITargetVisibilityState`：`NotChecked / TargetIsNotSeen / TargetIsClear / FriendInWay / CantShootInThatDir`——**不是 TargetIsClear 即视为失明，native 会换目标**
  - `GetAttackDirection()` / `GetDefendMovementFlag()` / `GetMissileRange()` / `UnderAttackType`（`NotUnderAttack / UnderMeleeAttack / UnderRangedAttack`）
- 可见性判定是 native 视线检测（射线+遮挡），C# 侧没有逐帧可见性查询入口（除 `Navigator.CanSeeAgent`）
- mod 能做的：接管目标（`SetTargetAgent`）、清目标（null）、读状态判断"它现在看没看见我"

### 5.5 第 5 层：攻防决策

- 全部由 DrivenProperties 概率化（详见 3.5 / 3.6：Block vs Parry 判定顺序、**反击走独立路径 `AIAttackOnParryChance`**）
- 输入状态：`UnderAttackType`（被近战/远程攻击中）、`GetDefendMovementFlag()`（当前防御方向）
- 决策 → 动作阶段机 `ActionStage`：攻击 `AttackReady → AttackQuickReady → AttackRelease`；防御 `Defend → DefendParry`（招架是防御的独立阶段）

### 5.6 第 6 层：动作输出（MovementControlFlag 位掩码）

```csharp
public enum MovementControlFlag : uint {   // v1.4.7 实测数值
    None = 0, Forward = 1, Backward = 2, StrafeRight = 4, StrafeLeft = 8,
    TurnRight = 0x10, TurnLeft = 0x20,
    AttackLeft = 0x40, AttackRight = 0x80, AttackUp = 0x100, AttackDown = 0x200,
    DefendLeft = 0x400, DefendRight = 0x800, DefendUp = 0x1000, DefendDown = 0x2000,
    DefendAuto = 0x4000, DefendBlock = 0x8000, Action = 0x10000,
    AttackMask = 0x3C0, DefendMask = 0x7C00, DefendDirMask = 0x3C00, MoveMask = 0x3F
}
```

- AI 每帧把决策写成 `MovementFlags`（`MBAPI.SetMovementFlags`），动画系统读位掩码播放对应动作（攻击/防御/移动方向）
- 方向映射：`AttackDirectionToMovementFlag(UsageDirection)` / `DefendDirectionToMovementFlag(...)` / `MovementFlagToDirection(...)`（三者均为 C# 公开静态/实例方法）
- `DefendAuto` / `DefendBlock` 是防御特化位：自动选方向防御 / 强制格挡——**想强制 NPC "架着防御"时可考虑直接写 MovementFlags 测试**（⚠️ 覆盖 AI 输入需先确认 ControllerType 语义，未实测）

### 5.7 两个关键回调的区别

| 回调 | 触发条件 | 职责 |
|------|----------|------|
| `MissionLogic.OnRegisterBlow` | 攻击判定注册（伤害为 0 也触发，和平区域也触发） | **攻击意图检测**：广播事件、触发敌对 |
| `MissionLogic.OnAgentHit` | 实际造成伤害时（伤害 > 0） | **伤害处理**：切磋虚拟血量、死亡收集 |

在和平城镇挥刀 → `OnRegisterBlow` 点火，`OnAgentHit` 不点火（引擎拦截了伤害）。

### 5.8 认输/投降姿态 —— 让 NPC 只格挡不进攻（🔴 官方用法验证过的配方）

**需求场景**：NPC 认输后站定，玩家补刀时它举武器格挡/招架，但**绝不出手还击**。

**原版先例（反编译验证）**：
- `SandBox.Missions.AgentBehaviors.CautiousBehavior`：当 agent 姿态为 `Cautious`/`PatrollingCautious` 时每帧 `SetWeaponGuard((UsageDirection)3)`（AttackRight 守卫）+ 警戒环视动画（`act_guard_cautious_look_around_1`）+ 持续保持武器出鞘
- `PrisonBreakMissionController.SwitchToPhase2`（v1.4.7 行 26090）：越狱成功后守卫 `SetAlarmState((AIStateFlag)2)` = **PatrollingCautious** —— 官方"守卫不主动进攻"的现成用法

**配方（4 步组合拳）**：

```csharp
// ① 姿态：Cautious(1) 或 PatrollingCautious(2) —— 不主动寻敌
agent.SetAlarmState(AIStateFlag.Cautious);

// ② 举武器守卫（原版 CautiousBehavior 每帧调用；解复用 UsageDirection.None=-1）
agent.SetWeaponGuard(UsageDirection.AttackRight);   // 3；防御向还有 DefendUp..DefendRight=4..7、DefendAny=8

// ③ 攻击意愿曲线清零（Agent.SetAIBehaviorParams 直推 native，立即生效）
agent.SetAIBehaviorParams(HumanAIComponent.AISimpleBehaviorKind.Melee,  0f, 5f, 0f, 20f, 0f);
agent.SetAIBehaviorParams(HumanAIComponent.AISimpleBehaviorKind.Ranged, 0f, 5f, 0f, 20f, 0f);
// ... ChargeHorseback / RangedHorseback 同理

// ④ 格挡/招架概率拉满 + 全部攻击路径归零（🔴 反击/拼刀是独立路径，漏一条就会出手）
agent.AgentDrivenProperties.AIBlockOnDecideAbility = 100f;           // 格挡能力
agent.AgentDrivenProperties.AIParryOnDecideAbility = 100f;           // 招架能力（依赖格挡判定）
agent.AgentDrivenProperties.AIDecideOnAttackChance = 0f;             // 主动攻击概率
agent.AgentDrivenProperties.AIAttackOnParryChance = 0f;              // 🔴 招架成功后反击概率
agent.AgentDrivenProperties.AiTryChamberAttackOnDecide = 0f;         // 🔴 拼刀（chamber）
agent.AgentDrivenProperties.AIParryOnAttackingContinueAbility = 0f;  // 🔴 招架后连续攻击
agent.AgentDrivenProperties.AiKick = 0f;                             // 不踢腿
agent.UpdateCustomDrivenProperties();   // 🔴 推当前数组；别用 UpdateAgentProperties()（会重算覆盖）
```

**恢复原状**（谈判破裂/重回战斗）：

```csharp
agent.SetAlarmState(AIStateFlag.Alarmed);
agent.SetWeaponGuard(UsageDirection.None);
agent.SetAIBehaviorParams(Melee, 8f, 7f, 4f, 20f, 1f);   // Default 曲线原值
agent.UpdateAgentProperties();   // 一键重算回装备/技能真实驱动属性
```

**🔴 三个坑**：
1. **阵型踩曲线**：战场阵型中阵型指令变化 → `RefreshBehaviorValues` 把 ③ 刷回预设。RealScene 的 NPC `Formation == null`（`MovePrepare` 已置空）不受影响；阵型内用需每帧重设或先 `agent.Formation = null` 脱队。
2. **`SetWeaponGuard` 只是静态姿态**，不保证挡住挥砍；真正的格挡判定靠 native（④ 的概率）——两者一起用才像"会防守的投降者"。
3. **对话期间 vs 对话后**：`StayAction` 现在的实现是 `SetScriptedPosition(NoAttack|InConversation)` + `SetMaximumSpeedLimit(0)` 原地钉死——保证不进攻，但锁死状态下 native 格挡反应大概率被抑制（需进游戏实测）。要"格挡不进攻"应换掉原地锁死，改为站定 + 上述 4 步。

---

## 六、导航网格（NavMesh）

### 6.1 大地图 2D NavMesh

```
CampaignVec2 {
    float x, y;
    PathFaceRecord path;  // 当前所在导航面
}
```

每个 navmesh face 有 `TerrainType`（森林/沙漠/平原），影响移动速度。`MobileParty.DoUpdatePosition` 每帧根据 `AiBehavior` + `PathFaceRecord` 计算最短路径。

### 6.2 Mission 3D NavMesh

关键 API：

```csharp
Scene.GetNavMeshFaceIndex(ref pos, out faceIndex);
Scene.GetPathBetweenAIFaces(face1, face2, ...);
Scene.GetLastPointOnNavigationMeshFromWorldPositionToDestination(...);
Scene.AreFacesOnSameIsland(face1, face2);  // 同一连通岛?
Scene.GetPathDistanceBetweenAIFaces(...);  // 路径距离
```

**坑点**：`GetFaceIndex().IsValid()` 只检查面存在，**不检查可达性**。山顶/隔水区域也有合法 navmesh 面但和定居点不连通。正确做法：`AreFacesOnSameIsland` + `GetPathDistanceBetweenAIFaces` 双验证。

---

## 七、LivingWorldNpcs 自定义 AI 架构

本 mod 在引擎之上构建了**事件驱动的行为队列系统**：

```
AgentAIController (MissionLogic, 单例)
  │
  ├── OnAgentCreated → 为每个 NPC 创建 AgentBrain
  ├── OnMissionTick → 遍历 brain.Tick(dt)
  ├── SendEventToAgent(agent, "事件名", args) — 单播
  └── BroadcastEventInRange(center, radius, "事件名", args) — 区域广播
        │
        ▼
     AgentBrain (每个 NPC 一个)
       │
       ├── Queue<IAtomicAction> 行为队列 (FIFO)
       ├── ReceiveEvent(AIEvent) → 转译为原子 Action 序列入队
       ├── Tick(dt) → 执行当前 Action / 完成后出队
       └── DecideDefaultBehavior() → 队列空时回到哨位
             │
             ▼
         IAtomicAction 实现 (见 AtomicAction.cs)
         ├── MoveToPositionAction   (ScriptedMoveToPoint)
         ├── FollowAgentAction      (极坐标跟随+防抖buffer)
         ├── FightEnemyAction       (CombatManager.StartFight)
         ├── LookAtAction           (agent.SetLookAgent)
         ├── PlayAnimAction         (AgentControlHelper.SetPose)
         ├── StayAction             (park在原地, IsFinished永不true)
         ├── DrawWeaponAction       (agent.TryToWieldWeaponInSlot)
         ├── TurnToDirectionAction  (逐帧SetMovementDirection)
         ├── ForceTalkAction        (触发对话管线)
         ├── PrepareOpeningAction   (LLM 生成开场白)
         └── ReactionDecisionAction (延迟回调, 围观群众错开反应)
```

### 7.1 事件投递示例——目击犯罪

```
1. AttackTriggerMissionLogic.OnRegisterBlow
   └── 检测到玩家攻击 NPC

2. AgentAIController.BroadcastEventInRange(案发位置, 半径, "WitnessCrime")

3. GroupStageManager.PrecalculateAllocations()
   └── 为每个目击者计算围观站位 (三层同心环 + 射线墙检)
   └── 错开反应时间 (身份/距离/视角 加权)

4. 每个目击者的 AgentBrain.ReceiveEvent()
   ├── WitnessCrime_GatherOnLook:
   │     ReactionDecisionAction(延迟)
   │       → MoveToPositionAction(围观位)
   │       → LookAtAction(盯着凶手)
   │       → ForceTalkAction(质问)
   └── WitnessCrime_StayStare:
         ReactionDecisionAction(延迟) → StayAction(原地盯着)
```

---

## 八、核心原理总结

```
                    ┌──────────────────┐
                    │   ControllerType  │  ← 谁控制这个 Agent?
                    │   AI/Player/None  │
                    └────────┬─────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
         AI 模式         Player 模式     None 模式
              │                            │
    ┌─────────┴─────────┐          ┌──────┴──────┐
    │  AIStateFlag      │          │ 直接操控     │
    │  (Alarmed/None..) │          │ MovementFlag │
    ├───────────────────┤          │ EventFlag    │
    │  BehaviorValues   │          └─────────────┘
    │  (7条距离-意愿曲线) │
    ├───────────────────┤
    │  DrivenProperties │
    │  (84个微调参数)    │
    ├───────────────────┤
    │  ScriptedMovement │  ← 脚本化移动覆盖战斗 AI
    │  (SetScriptedPos) │
    └─────────┬─────────┘
              │
    ┌─────────┴─────────┐
    │   Native C++ 引擎   │  ← 真正的 AI 计算在这里
    │   MBAPI.IMBAgent   │
    │   寻路/避障/动画/战斗 │
    └────────────────────┘
```

**一句话**：Agent AI = **ControllerType 决定控制源** × **AIStateFlag 决定行为倾向** × **BehaviorValues 决定战斗意愿** × **DrivenProperties 微调概率** × **ScriptedMovement 覆盖自由战斗**。所有 C# 调用最终汇入 `MBAPI.IMBAgent`，引擎在 C++ 层完成真正的 NavMesh 寻路、行为树决策和动画混合。

---

## 九、原版跟随三件套（FollowAgentBehavior 丝滑移动移植配方）

> 完整链路见 [原版场景跟随系统分析](原版场景跟随系统分析.md)（Campaign 名单 + Mission FollowAgentBehavior 两段式）。本节是 **v1.4.7 反编译实测的"丝滑"三件套**，可直接移植进本项目 `FollowAgentAction`（`AI/Actions/AtomicAction.cs`，极坐标跟随+防抖 buffer 那一版）。

原版跟随者和我们一样**没有自研寻路**——每帧算一个"理想落脚点"，然后交引擎寻路。丝滑的差距在三个机制：

### 9.1 移动指令：`Navigator.SetTargetFrame`（替代裸 SetScriptedPosition）

```csharp
// AgentNavigator（SandBox.dll，CampaignAgentComponent.AgentNavigator）
public void SetTargetFrame(WorldPosition position, float rotation,
    float rangeThreshold = 1f, float rotationThreshold = -10f,
    AIScriptedFrameFlags flags = 0, bool disableClearTargetWhenTargetIsReached = false);
```

`rangeThreshold` = **到达圈半径**：引擎把目标当圆处理，进圈自然减速停止，到达后（`IsTargetReached()`）自动清目标。裸 `SetScriptedPosition` 没有这套"到达圈+自动释放"语义。

### 9.2 动态限速（丝滑核心，每帧执行）

```csharp
// FollowAgentBehavior.MoveToFollowingAgent：速度被钳制为"剩余距离 + 目标速度"
ownerAgent.SetMaximumSpeedLimit(距目标距离 - rangeThreshold + 目标速度, false);
```

接近时速度随剩余距离收缩 → 永不冲过头、不抖动、跟随目标速度。本项目 `FollowAgentAction` 目前是全速追到 stopDistance 内再 `ClearTargetFrame()` 硬停，差距就在这。

### 9.3 到达释放协议

```csharp
if (Navigator.TargetPosition.IsValid && Navigator.IsTargetReached())
{
    agent.DisableScriptedMovement();
    agent.SetMaximumSpeedLimit(-1f, false);   // -1 = 恢复默认
    // 同时记下到达时的实际距离作 idleDistance（防抖，避免微动重启）
}
```

### 9.4 其余可搬细节

- **目标点每帧重算**：速度方向外推（`Velocity` 非零用速度方向，否则用 `GetMovementDirection()`）+ 前后/左右偏移（徒步 0.6/1.0m，骑马 1.25/1.5m，按所在侧）
- **多跟随者排队**：遍历 Mission.Agents 找同目标的其他 FollowAgentBehavior，按人数向外推（+0.6m 纵 / +1m 横）——多人不重叠
- **邻近避让**：`AgentProximityMap.BeginSearch(目标点, 0.5f)` 槽位有人 → 退回直接跟目标点
- **navmesh+视线校验**：`GetNavMesh() != 0` && `IsLineToPointClear(身体胶囊半径)`，不通偏移 1.5m 再试，再不通直接跟
- **Fight 状态**：5m 内有敌人 → `SetWatchState(2)` + `Navigator.ClearTarget()` 停下警戒，敌人消失恢复

**🔴 与本 mod Brain 的关系**：原版跟随挂在 `DailyBehaviorGroup`，与 `SuspendVanillaAI`（AiSuspendPatch 拦 `RefreshBehaviorGroups`）冲突——Brain 队列内**不能直接挂** FollowAgentBehavior。方案：① 演出式跟随（FollowAgentAction）把三件套搬进来（有 `AgentNavigator` 时走 SetTargetFrame，战斗单位无 navigator 回退 SetScriptedPosition）；② 常驻跟随（陪逛/遇敌停手）不 Suspend 该 agent，直接用原版 `AddBehavior<FollowAgentBehavior>()` + `SetScriptedBehavior` + `SetTargetAgent`（PrisonBreakMissionController 越狱犯人跟随为官方范本）。

---

## 相关文件

- `TaleWorlds.MountAndBlade.dll` → `Agent` (4967行), `HumanAIComponent` (805行), `CommonAIComponent`, `AgentComponent`, `AgentDrivenProperties`
- `TaleWorlds.Core.dll` → `AgentState`, `AgentFlag`, `DrivenProperty`（含数值枚举）, `EquipmentIndex`, `WeaponClass`
- `TaleWorlds.CampaignSystem.dll` → `MobilePartyAi`, `AiBehavior`, `Settlement`, `Location`, `SetPartyAiAction`
- `SandBox.dll` → `MissionAgentHandler`, `TownCenterMissionController`, `DailyBehaviorGroup`, `AgentNavigator`（`SetTargetFrame`）, `FollowAgentBehavior`（跟随三件套范本）, `CautiousBehavior`（守卫姿态范本）, `PrisonBreakMissionController`（守卫 PatrollingCautious + 犯人跟随范本）
- 本项目 → `Core/AgentControlHelper.cs`, `AI/AgentBrain.cs`, `AI/Actions/AtomicAction.cs`（FollowAgentAction / StayAction）, `Combat/CombatManager.cs`（认输流程入口）

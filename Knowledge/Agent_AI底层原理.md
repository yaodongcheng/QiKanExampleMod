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

### 3.2 AIStateFlag —— 行为状态位掩码

```csharp
[Flags] public enum AIStateFlag : uint {
    None, Cautious, Alarmed, Paused,
    UseObjectMoving, UseObjectUsing, UseObjectWaiting,
    Guard, ColumnwiseFollow
}
```

| Flag | 效果 |
|------|------|
| `None` | 无视敌军，不主动攻击 |
| `Alarmed` | 警戒 — **自动调整 lookDirection 寻找攻击目标** |
| `Cautious` | 谨慎模式 |
| `Paused` | 暂停 AI 思考 |
| `UseObjectMoving` | 正在走向椅子/器械 |
| `UseObjectUsing` | 正在使用椅子/器械 |
| `Guard` / `ColumnwiseFollow` | 阵型跟随 |

这些是**位掩码**，可以组合。引擎每帧检查这些 flag 决定 Agent 的基础行为倾向。

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

**预设集**（`BehaviorValueSet`）：`Default`, `DefensiveArrangementMove`, `Follow`, `DefaultMove`, `Charge`, `DefaultDetached`。不同战术场景切换不同预设。

### 3.5 DrivenProperties —— 84 个微调参数

```csharp
public enum DrivenProperty {
    // 格挡相关
    AIBlockOnDecideAbility, AIParryOnDecideAbility, AIUseShieldAgainstEnemyMissileProbability,
    // 攻击相关
    AIAttackOnDecideChance, AiAttackOnParryChance, AiAttackOnParryTiming,
    AiAttackingShieldDefenseChance, AiAttackingShieldDefenseTimer,
    // 射击相关
    AiShootFreq, AiWaitBeforeShootFactor, AiRangedHorsebackMissileRange,
    AiRangerLeadErrorMin/Max, AiRangerVerticalErrorMultiplier, AiRangerHorizontalErrorMultiplier,
    // 移动相关
    AiCheckMovementIntervalFactor, AiMovementDelayFactor, AiMinimumDistanceToContinueFactor,
    // 其他
    AiKick, AiSpeciesIndex, ArmorEncumbrance, WeaponsEncumbrance,
    SwingSpeedMultiplier, ThrustOrRangedReadySpeedMultiplier, HandlingMultiplier,
    MountManeuver, MountSpeed, AttributeCourage, AttributeRiding, AttributeShield,
    // ... 共 84 个
}
```

通过 `agent.UpdateDrivenProperties(float[84])` 一次性写入 native 层，直接影响每帧 AI 决策概率。

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

每一帧的战斗 AI 经历以下决策链：

```
1. Team 判定
   └── Agent.Team.IsEnemyOf(other.Team) → 是否敌对

2. 行为权重计算
   └── HumanAIComponent 遍历 7 种 AISimpleBehaviorKind
       └── 每条曲线用 BehaviorValues 查当前距离的权重
       └── 最高权重行为当选

3. 目标选择
   └── 扫描视野内的敌对 Agent
   └── 按距离/威胁度排序 → SetTargetAgent(selected)

4. 攻防决策 (DrivenProperties 参数化)
   ├── 攻击: AIAttackOnDecideChance 决定概率
   ├── 格挡: AIBlockOnDecideAbility 决定能力
   ├── 反击: AIAttackOnParryChance 决定时机
   └── 踢腿: AiKick 决定频率

5. 动作输出
   └── MovementControlFlag 位掩码写入
       (AttackLeft | DefendRight | Forward ...)
   └── 动画系统读取 flag 播放对应动画
```

### 5.1 两个关键回调的区别

| 回调 | 触发条件 | 职责 |
|------|----------|------|
| `MissionLogic.OnRegisterBlow` | 攻击判定注册（伤害为 0 也触发，和平区域也触发） | **攻击意图检测**：广播事件、触发敌对 |
| `MissionLogic.OnAgentHit` | 实际造成伤害时（伤害 > 0） | **伤害处理**：切磋虚拟血量、死亡收集 |

在和平城镇挥刀 → `OnRegisterBlow` 点火，`OnAgentHit` 不点火（引擎拦截了伤害）。

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

## 相关文件

- `TaleWorlds.MountAndBlade.dll` → `Agent` (4967行), `HumanAIComponent` (805行), `CommonAIComponent`, `AgentComponent`
- `TaleWorlds.Core.dll` → `AgentState`, `AgentFlag`, `DrivenProperty`, `EquipmentIndex`, `WeaponClass`
- `TaleWorlds.CampaignSystem.dll` → `MobilePartyAi`, `AiBehavior`, `Settlement`, `Location`, `SetPartyAiAction`
- `SandBox.dll` → `MissionAgentHandler`, `TownCenterMissionController`, `DailyBehaviorGroup`, `AgentNavigator`, `AgentBehavior`
- 本项目 → `Core/AgentControlHelper.cs`, `AI/AgentBrain.cs`, `AI/Actions/AtomicAction.cs`, `Combat/CombatManager.cs`

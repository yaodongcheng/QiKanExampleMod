# Agent AI 冲突解决与接管策略

> 配合 [Agent AI 底层原理](Agent_AI底层原理.md) 阅读。
> 核心问题：当 mod 自定义的 AgentBrain/AtomicAction 与原生 AgentNavigator/DailyBehaviorGroup 同时控制一个 Agent 时如何不打架。

**本 mod 已提供 `SuspendVanillaAI` / `ResumeVanillaAI` API，AgentBrain 在有 Action 时自动压制原版 AI。** 如果你只是使用 AgentBrain 做行为链，不需要手工调用——已经集成好了。详见本文第四节。

---

## 一、城镇 NPC 初始行为全链路

### 1.1 从 Location 到 Agent

```
玩家进入城镇
  │
  ▼
TownCenterMissionController.AfterStart()
  │
  ├── SpawnPlayer()
  └── MissionAgentHandler.SpawnLocationCharacters()
        │
        ├── ① 从 Location.GetCharacterList() 取 NPC 列表
        │     (Location = "center" / "tavern" / "lordshall" / "alley" …)
        │
        ├── ② 对每个 LocationCharacter 找 UsableMachine：
        │     ├── SpecialTargetTag → scene 中对应 tag
        │     ├── "npc_common_limited" tag
        │     ├── "npc_common" tag
        │     ├── 任意 UsablePoint
        │     └── 找到 → SpawnWanderingAgentWithUsableMachine(椅子/站立点)
        │         没找到 → SpawnWanderingAgentWithInitialFrame(默认出生点)
        │
        └── ③ SimulateAgent() — 预模拟 35-50 次 Tick(0.1s)
              └── AgentNavigator.Tick() 提前跑 3-5 秒
              └── NPC 在玩家看到之前已经走到椅子/站立点并坐下
```

### 1.2 核心类

| 类 | 位置 | 作用 |
|----|------|------|
| **`Location`** | `CampaignSystem.dll` | 一个城镇区域。持有 `List<LocationCharacter>` |
| **`LocationComplex`** | `CampaignSystem.dll` | 一个城镇的全部 Location 的字典 |
| **`MissionAgentHandler`** | `SandBox.dll` | 场景加载时读所有 `UsableMachine`，分配 NPC 座位/站立点 |
| **`AgentNavigator`** | `SandBox.dll` | 每个 NPC 挂一个。运行时驱动 NPC 在椅子/桌子/门之间移动 |
| **`CampaignAgentComponent`** | `SandBox.dll` | 挂载在 Agent 上的组件，持有 `AgentNavigator` |

### 1.3 AgentNavigator 状态机

```
          SetTarget(usableMachine)
  NoTarget ──────────────────────────→ GoToTarget
                                           │
                                   导航到达 target 范围
                                           │
                                           ▼
                                     AtTargetPosition
                                           │
                                    UseGameObject()
                                           │
                                           ▼
                                      UseMachine
                                   (坐在椅子上/靠在墙边)
                                           │
                                   15 秒后 DailyBehaviorGroup.Think() 重新选
                                           │
                                   StopUsingGameObject()
                                           │
                                           ▼
                                      NoTarget → 循环
```

### 1.4 DailyBehaviorGroup — 行为选举机制

每个 NPC 的 `AgentNavigator` 内部包含 `DailyBehaviorGroup`，管理一组 `AgentBehavior`：

```
DailyBehaviorGroup.Tick()
  │
  ├── 如果 ScriptedBehavior 已激活 → 执行它，跳过选举
  └── 否则 → Think() 每 15 秒跑一次
        ├── 遍历所有 Behavior，调 GetAvailability() → float 分数
        ├── 最高分当选
        └── 激活该 Behavior 的 Tick()
```

| Behavior | 作用 |
|----------|------|
| `WalkingBehavior` | 随机走到场景里的空闲椅子/站立点 |
| `ChangeLocationBehavior` | 走到通道门，换区域 (center → tavern) |
| `FollowAgentBehavior` | 跟随特定 Agent（同伴/护卫），距离 > 4m+ 触发移动 |

**这就是 NPC "闲逛"机制**：每 15 秒 `WalkingBehavior` 和 `ChangeLocationBehavior` 竞争，赢家驱动 Agent 走到新位置。

---

## 二、"Come Here 走远了再回来" 根因分析

### 2.1 问题场景

玩家按 G 叫远处 NPC 过来 → NPC 先往反方向走一段（去椅子/桌子），再转回来走向玩家。

### 2.2 冲突时间线

```
T+0.0s  玩家按 G
        ├── InteractionMissionView 检测 agent 不"自然站立"（IsUsingGameObject = true）
        ├── SendEventToAgent("ComeHere")
        └── AgentBrain.ClearAllActions()
              └── SetScriptedPosition(当前位置, DoNotRun|NoAttack) — 想锁住 NPC

T+0.1s  原版 DailyBehaviorGroup.Think()  —— 仍在运行！
        ├── WanderingBehavior.GetAvailability() 返回高分
        ├── 选中远处角落的椅子
        └── AgentNavigator.SetTarget(远处椅子)
              └── HumanAIComponent.MoveToUsableGameObject()
                    └── SetScriptedPosition(椅子位置) → 覆盖了你的锁！

T+0.2s  FollowAgentAction.OnStart()
        ├── MovePrepare(npc)
        │     ├── StopUsingGameObject(false)     ← 开始站起来
        │     └── await Task.Delay(2000)          ← 🔥 2秒真空期！
        └── NPC 正在执行原版给的目标：走向远处的椅子

T+2.0s  MovePrepare 完成
        └── ScriptedMoveToPoint(玩家位置)
              └── SetScriptedPosition(玩家位置)
              此时 NPC 已走到椅子附近，又要走回来
              → 玩家看到 "走远了再回来"
```

### 2.3 三个根因

**根因 1：两套系统同时抢 Agent**

`AgentBrain.Tick()` 和 `AgentNavigator.Tick()` 在每一帧**同时运行**。`DailyBehaviorGroup` 每 15 秒重新选举行为，覆盖脚本化移动目标。

**根因 2：`StopUsingGameObject` 内部调用了 `DisableScriptedMovement()`**

```csharp
// Agent.StopUsingGameObjectAux() 内部调用链:
DisableScriptedMovement();  // 清除所有脚本化移动目标
// 然后 AfterStoppedUsingMissionObject() 可能重新 attach 到 formation
```

`ClearAllActions` 刚设的"锁在原地"被 `MovePrepare` 里的 `StopUsingGameObject` 清掉。两者互相抵消。

**根因 3：2 秒 async 延迟是真空期**

`MovePrepare` 里的 `await Task.Delay(2000)` 期间，代码完全没有控制 Agent。2 秒足够 `DailyBehaviorGroup` 跑一轮 `Think()` 并发出新的移动指令。

---

## 三、解决方案

### 3.1 核心原则

> **接手 Agent 控制权前，必须先让原版系统"放手"，而且原子完成"放手 + 接管"，中间不能有空窗期。**

### 3.2 方案 A：接管前清除 AgentNavigator 目标（推荐、最小改动）

```csharp
// 在接管 NPC 之前，先让 AgentNavigator 放手：
var navigator = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
if (navigator != null)
{
    // 关键：SetTarget(null) 内部会：
    //   1. 调 HumanAIComponent.MoveToClear() — 清除"移动中"状态
    //   2. 停止寻路
    //   3. 清空 behavior group 的当前 target
    // 效果：下一次 Think() 等 15 秒 — 足够你接管
    navigator.SetTarget(null);
}

// 然后立即设脚本化锁（两步之间零延迟）
agent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
WorldPosition lockPos = agent.GetWorldPosition();
agent.SetScriptedPosition(ref lockPos, false,
    Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.NoAttack);
```

**为什么有效**：`navigator.SetTarget(null)` 内部调 `HumanAIComponent.MoveToClear()`（比 `StopUsingGameObject` 更干净），然后 `DailyBehaviorGroup` 下次 `Think()` 要等 15 秒——足够完成接管。

### 3.3 方案 B：CanBeAssignedForScriptedMovement 守卫

```csharp
if (!agent.CanBeAssignedForScriptedMovement())
{
    // 强制释放
    var navigator = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
    navigator?.SetTarget(null);
    agent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
    agent.DisableScriptedMovement();
    agent.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);
    agent.SetMaximumSpeedLimit(-1f, false);
}

// 现在可以安全设目标
agent.SetScriptedPosition(ref targetPos, false, flags);
```

`CanBeAssignedForScriptedMovement()` 返回 false 的条件：
- `GoToPosition` flag 已设置
- `IsUsingGameObject` 或 `IsInterestedInAnyGameObject`
- `IsRunningAway`
- `IsDetachedFromFormation`

### 3.4 方案 C：消除 2 秒异步真空期 — 同步版 MovePrepare

```csharp
public static void MovePrepareSync(Agent npcAgent)
{
    // 1. 先让 AgentNavigator 放手
    var nav = npcAgent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
    nav?.SetTarget(null);

    // 2. 同步释放游戏物体（不做两阶段动画过渡）
    if (npcAgent.IsUsingGameObject)
    {
        npcAgent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
    }

    // 3. 清干净状态
    npcAgent.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);
    npcAgent.DisableScriptedMovement();
    npcAgent.ClearTargetFrame();
    npcAgent.SetLookAgent(null);
    npcAgent.SetMaximumSpeedLimit(-1f, false);

    // 4. 确保 AI 控制
    V.SetAgentAI(npcAgent);

    // 5. 立即锁定当前位置
    WorldPosition lockPos = npcAgent.GetWorldPosition();
    npcAgent.SetScriptedPosition(ref lockPos, false,
        Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.NoAttack);
}
```

**代价**：NPC 从椅子上"瞬移站起来"（跳过站起动画）。对于"叫过来"场景，即时响应 > 动画平滑。如需保留动画，把 `FollowAgentAction` 的 `_timer` 初值设 0，让 `MovePrepare` 做完立即发 `SetScriptedPosition`。

### 3.5 方案 D：走官方通路 — DailyBehaviorGroup.SetScriptedBehavior

原版同伴跟随用的是**融入 DailyBehaviorGroup 体系**的做法，不走裸调 `SetScriptedPosition`：

```csharp
var agentNavigator = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
var dailyGroup = agentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
if (dailyGroup != null)
{
    var followBehavior = dailyGroup.GetBehavior<FollowAgentBehavior>();
    if (followBehavior == null)
    {
        dailyGroup.AddBehavior<FollowAgentBehavior>();
        followBehavior = dailyGroup.GetBehavior<FollowAgentBehavior>();
    }
    followBehavior.SetTargetAgent(Agent.Main);
    dailyGroup.SetScriptedBehavior<FollowAgentBehavior>();
}
```

这样 `DailyBehaviorGroup` 自己知道"现在在跟随"，`WanderingBehavior` 和 `ChangeLocationBehavior` 的 `GetAvailability()` 自动返回 0。**不存在第二套系统，自然不打架**。

**限制**：`FollowAgentBehavior` 的跟随距离阈值 > 4m，有自有状态机（Idle → OnMove → Fight），无法精确控制 0.5m 对话距离。

### 3.6 方案 E：SuspendVanillaAI / ResumeVanillaAI ✅（已实现，推荐）

**原理**：设置 `daily.IsActive = false` 禁用 DailyBehaviorGroup，通过 `SuspendedAgentIndices`（`HashSet<int>`）标记挂起。`AiSuspendPatch`（Harmony Prefix 拦截 `AgentNavigator.RefreshBehaviorGroups`）检查该集合——命中则跳过，防止 Navigator 每 1 秒重新激活。Resume 时从集合移除 + `daily.IsActive = true` + `ForceThink(0f)`。

详见本文第四节的完整 API 文档和源码。

---

## 四、SuspendVanillaAI / ResumeVanillaAI API（已集成）

### 4.1 原理

**Suspend**：设置 `daily.IsActive = false` 禁用 DailyBehaviorGroup，并通过 `SuspendedAgentIndices`（`HashSet<int>`）标记该 Agent 已挂起。`AiSuspendPatch`（Harmony Prefix 拦截 `AgentNavigator.RefreshBehaviorGroups`）检查该集合——命中则跳过刷新，防止 Navigator 每 1 秒重新激活 DailyBehaviorGroup。

**Resume**：从 `SuspendedAgentIndices` 移除标记，设置 `daily.IsActive = true` + `ForceThink(0f)` 立即选举行为。

```csharp
// AI/AgentBrain.cs

/// 挂起的 Agent 索引集合。AiSuspendPatch 检查此集合，命中则跳过 RefreshBehaviorGroups。
public static readonly HashSet<int> SuspendedAgentIndices = new HashSet<int>();

/// 暂停原版 AgentNavigator / DailyBehaviorGroup 对该 Agent 的控制。幂等。
private bool SuspendVanillaAI()
{
    if (!SuspendedAgentIndices.Add(Owner.Index))
        return true; // 已在集合中，幂等

    var nav = Owner.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
    if (nav == null) return false;

    var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
    if (daily != null && daily.IsActive)
        daily.IsActive = false;

    return true;
}

/// 恢复原版 AgentNavigator / DailyBehaviorGroup 的控制。
private void ResumeVanillaAI()
{
    if (!Owner.IsActive()) return;
    if (!SuspendedAgentIndices.Remove(Owner.Index))
        return; // 没被 Suspend 过，不碰原版 AI

    var nav = Owner.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
    if (nav == null) return;

    var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
    if (daily != null && !daily.IsActive)
    {
        daily.IsActive = true;
        daily.ForceThink(0f);  // 同步触发 WalkingBehavior 选举
    }
}
```

```csharp
// AI/AiSuspendPatch.cs — Harmony Prefix 拦截
[HarmonyPatch(typeof(AgentNavigator), "RefreshBehaviorGroups")]
public static class AiSuspendPatch
{
    public static bool Prefix(AgentNavigator __instance)
    {
        return !AgentBrain.SuspendedAgentIndices.Contains(__instance.OwnerAgent.Index);
    }
}
```

### 4.2 自动集成：AgentBrain 已自动化

**你不需要手工调用！** 触发点如下：

```
接管（Suspend）：
  EnqueueAction() → 空脑转有 Action 时自动调一次 SuspendVanillaAI
  （内部幂等 — 已经 suspended 的 Agent 不会重复操作）

释放（Resume）：
  路径 1: EndInteraction → ForceUnlockAgent → ResumeVanillaAI
  路径 2: DecideDefaultBehavior（非护卫 + 脑空）→ ResumeVanillaAI（兜底）

清理：
  AgentAIController.OnAgentDeleted → CleanupSuspendedAgent（防止泄漏）
```

**完整生命周期**：

```
NPC 自由巡逻（DailyBehaviorGroup 在 Navigator 列表中，脑空）
  │
  │  玩家互动 → ComeHere → ClearAllActions → EnqueueAction ×4
  │    └── 第一个 EnqueueAction → SuspendVanillaAI（移除 DailyBehaviorGroup）
  ▼
脑接管：Action 链执行，原版 AI 已移除，不会干扰
  │
  │  对话结束 → EndInteraction
  ▼
ClearAllActions → ForceUnlockAgent → ResumeVanillaAI
  → DailyBehaviorGroup 放回列表 + IsActive = true + ForceThink(0f)
  → Brain 空 → 下一帧 DecideDefaultBehavior
    ├── 护卫: Enqueue FollowAgentAction → SuspendVanillaAI(跟随模式接管)
    └── 非护卫: ResumeVanillaAI（空操作）+ 原版 AI 自由巡逻
```

### 4.3 手工调用场景

如果你在 AgentBrain 之外直接操作 Agent，可以手工调用：

```csharp
// 接管
AgentControlHelper.SuspendVanillaAI(agent);
agent.SetScriptedPosition(ref targetPos, false, flags);

// ... 你的逻辑 ...

// 释放
AgentControlHelper.ResumeVanillaAI(agent);
```

---

## 五、分层接管流程（底层细节）

> 日常开发不需要关心这些——AgentBrain 已自动集成 SuspendVanillaAI。
> 以下是底层实现细节，供扩展新 Action 或调试时参考。

```
第 0 层：接管前
  ├── SuspendVanillaAI(agent)               ← 清空原版目标 + 禁用 DailyBehaviorGroup
  └── agent.StopUsingGameObject(true)       ← 同步释放

第 1 层：锁定
  ├── ClearAllActions()                  ← 停掉当前 Action
  └── SetScriptedPosition(当前位置)       ← 锁住 (DoNotRun|NoAttack)
       DailyBehaviorGroup 15 秒 CD 保证不会马上覆盖

第 2 层：移动
  ├── FollowAgentAction                  ← 每 200ms 重发 SetScriptedPosition
  └── 超时 8s → 直接传送兜底

第 3 层：到位锁定
  └── MoveEndAndInteractPrepare          ← DoNotRun|NoAttack|InConversation
       安全对话

第 4 层：释放
  └── ForceUnlockAgent                   ← 全部清空
       DisableScriptedMovement + SetScriptedFlags(None)
       原版 AgentNavigator 恢复接管，NPC 回到闲逛
```

### 关键改进点

| 问题 | 自动化解决方案 |
|------|--------------|
| 两套 AI 同时跑 | `AgentBrain.Tick()` 有 Action 时每帧调 `SuspendVanillaAI(Owner)` |
| 接管完原版又抢回去 | `SuspendVanillaAI` 每帧重申，对抗 Navigator 的 1 秒重激活 |
| 恢复原版 AI | `EndInteraction` 事件 → `ResumeVanillaAI(Owner)` → `dailyGroup.ForceThink(0f)` 立即闲逛 |
| 非城镇 NPC | `SuspendVanillaAI` 返回 false，安全无操作 |

---

## 六、三层清理函数的分工

| 函数 | 力度 | 调用时机 | 做的事 |
|------|------|----------|--------|
| **`MovePrepare`** | 中 | Action.OnStart (移动前) | 停止用物体 → 清阵型/target → 恢复 AI |
| **`ClearAllActions`** | 轻 | ReceiveEvent (新事件打断当前链) | 当前 Action.OnEnd → 清队列 → 锁原地 (DoNotRun\|NoAttack) |
| **`ForceUnlockAgent`** | 重 | EndInteraction / StopAndReset | DisableScriptedMovement → 清全部 flag → 恢复速度 → 停止用物体 |

**顺序约定**：
1. `MovePrepare` 在移动 Action 开始时调用（准备出发）
2. `ClearAllActions` 在新事件打断时调用（锁住，等新 Action 入队）
3. `ForceUnlockAgent` 在完全释放 NPC 时调用（回到原版 AI 接管）

---

## 七、关键 API 速查

```csharp
// 原版 AI 状态查询
agent.IsUsingGameObject                          // bool — 正在用椅子/器械?
agent.IsAIControlled                             // bool — AI 控制?
agent.CanBeAssignedForScriptedMovement()         // bool — 可以安全接管?

// AgentNavigator 控制
var nav = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
nav.SetTarget(null);                             // 清空目标，暂停原版 AI
nav.GetBehaviorGroup<DailyBehaviorGroup>();      // 拿行为组
dailyGroup.SetScriptedBehavior<FollowAgentBehavior>(); // 走官方通路

// 脚本化移动
agent.SetScriptedPosition(ref pos, false, flags);
agent.SetScriptedPositionAndDirection(ref pos, dir, false, flags);
agent.DisableScriptedMovement();                 // 清除脚本化目标
agent.SetScriptedFlags(AIScriptedFrameFlags.None);
agent.SetMaximumSpeedLimit(-1f, false);          // 恢复速度

// 游戏物体
agent.StopUsingGameObject(true, None);           // 同步强制释放
agent.StopUsingGameObject(false, None);          // 异步开始退出动画

// 目标/朝向
agent.SetTargetAgent(target);
agent.SetLookAgent(target);
agent.ClearTargetFrame();
```

---

## 八、战斗后 NPC 卡死不动的根因：WatchState 残留

### 8.1 现象

玩家攻击 NPC → 战斗 → 认输/对话结束 → NPC 原地不动，不再闲逛。

### 8.2 根因链路

`CombatManager.StartFight` 中调了 `agent.SetWatchState(Agent.WatchState.Alarmed)`，但 `EndFight` 没有重置。导致：

```
CurrentWatchState = Alarmed (值 2，由 StartFight 设置，未清除)
  │
  ▼
RefreshBehaviorGroups 遍历各 Group 的 GetScore():
  ├── AlarmedBehaviorGroup.GetScore()
  │     └── CurrentWatchState == Alarmed → return 1f  ← 碾压！
  ├── DailyBehaviorGroup.GetScore()
  │     └── return 0.5f
  └── 结果: ActivateGroup(Alarmed) → daily.IsActive = false
        │
        ▼
      AlarmedBehaviorGroup.OnActivate()
        └── IsAgentAggressive(OwnerAgent) → 有武器 → true
              → DisableCalmDown = true  ← 永不冷静！
```

**关键源码**（`AlarmedBehaviorGroup.GetScore`，反编译自 `SandBox.dll`）：

```csharp
public override float GetScore(bool isSimulation)
{
    if ((int)base.OwnerAgent.CurrentWatchState == 2)  // Alarmed
    {
        if (!DisableCalmDown && _alarmedTimer.ElapsedTime > 10f && ...)
        {
            // 冷静动画 → 动画结束 → return 0f  (DisabledCalmDown=true 跳过此路径)
        }
        return 1f;  // 永远 1.0，Daily 的 0.5 根本抢不过
    }
    if (IsNearDanger()) { AlarmAgent(agent); return 1f; }
    return 0f;
}
```

`OnActivate` 中 `DisableCalmDown` 的判定：

```csharp
if (Navigator.MemberOfAlley != null || MissionFightHandler.IsAgentAggressive(base.OwnerAgent))
    DisableCalmDown = true;

// IsAgentAggressive: 有武器 → true（村民的农具也算）
```

### 8.3 修复

在 `CombatManager.EndFight` 中重置 WatchState：

```csharp
agent.SetWatchState(Agent.WatchState.Patrolling);  // 值 0，正常巡逻态
```

`Agent.WatchState` 枚举值：`Patrolling`(0) / `Cautious`(1) / `Alarmed`(2)。

修复后 `RefreshBehaviorGroups` 的分逻辑：

```
CurrentWatchState = Patrolling → AlarmedBehaviorGroup.GetScore()
  → 路径2: IsNearDanger() → GetClosestAlarmSource()
    → MissionFightHandler.IsThereActiveFight() → false（我们没用官方战斗系统）
    → return null → IsNearDanger() = false
  → return 0f  ← 不抢了

DailyBehaviorGroup.GetScore() = 0.5f  ← 胜出
→ ActivateGroup(Daily) → NPC 恢复闲逛
```

### 8.4 为什么 SuspendVanillaAI 挡不住

Suspend 期间 `SuspendedAgentIndices` 拦截了 `RefreshBehaviorGroups`，Alarmed 的分数再高也执行不到。但 `PostConversationCleanup` → `ResumeVanillaAI` → `SuspendedAgentIndices.Remove` 后，下一帧 `RefreshBehaviorGroups` 就会执行——此时 WatchState 还是 Alarmed，立刻被抢走。Suspend 只是延迟了问题，没有消除根因。

### 8.5 🔴 WatchState 不能绕过

我们尝试过**两边都注释掉**（StartFight 不设 Alarm，EndFight 不恢复）——NPC 直接无法战斗。这说明 `SetWatchState(Alarmed)` 被 **native C++ 引擎底层**用于开关战斗 AI，不是简单的标签。虽然 C# 层能看到的行为选举（`AlarmedBehaviorGroup.GetScore`）被我们的 Harmony Suspend 拦截了，但引擎内部在 Agent 控制器级别还有其他检查路径——不设 Alarm 就不允许攻击/锁定敌人。

**结论**：`SetWatchState(Alarmed)` 必须设（战斗需要），但必须在 `EndFight` 恢复为 `Patrolling`（否则 Resume 后被 AlarmedBehaviorGroup 抢走控制权）。**对称成对，不可省略任一端。**

Suspend 期间 `SuspendedAgentIndices` 拦截了 `RefreshBehaviorGroups`，Alarmed 的分数再高也执行不到。但 `PostConversationCleanup` → `ResumeVanillaAI` → `SuspendedAgentIndices.Remove` 后，下一帧 `RefreshBehaviorGroups` 就会执行——此时 WatchState 还是 Alarmed，立刻被抢走。Suspend 只是延迟了问题，没有消除根因。

---

## 相关文件

- 本项目 → `Core/AgentControlHelper.cs` — MovePrepare, ForceUnlockAgent, ScriptedMoveToPoint, MoveEndAndInteractPrepare
- 本项目 → `AI/AgentBrain.cs` — ClearAllActions, ReceiveEvent, DecideDefaultBehavior
- 本项目 → `AI/Actions/AtomicAction.cs` — FollowAgentAction (极坐标防抖), MoveToPositionAction, StayAction
- 本项目 → `Interaction/InteractionMissionView.cs` — PrepareAgentForConversation, "ComeHere" 触发
- 本项目 → `Combat/CombatManager.cs` — StartFight / EndFight（WatchState 管理）
- `SandBox.dll` → `MissionAgentHandler`, `AgentNavigator`, `DailyBehaviorGroup`, `WalkingBehavior`, `AlarmedBehaviorGroup`, `FightBehavior`
- `SandBox.dll` → `MissionFightHandler` — `IsThereActiveFight()`, `IsAgentAggressive()`, `GetDangerSources()`

# KCD2 水准 — 警戒质问对话后果设计

## 背景

NPC 警戒值系统的 L3 质问对话目前生成的选项（"关你什么事？（挑衅）"、"我走了。"）
缺乏实质性的 gameplay 后果。玩家可以威胁村民、检定失败、然后淡定走人——严重破坏沉浸感。

本次计划对标 KCD2，从四个维度补齐后果。

## 核心架构约束

**所有行为必须走 Event → ReceiveEvent → EnqueueAction 标准管道。**
Patch 层只负责发事件，不得直接调 `CombatManager.StartFight` 或 `brain.StartXXX`。
战斗本身是 `FightEnemyAction`（已有轮子），围堵是 `FollowAgentAction` + `AlertForceConversationAction`（已有轮子），
全部通过 Brain 的事件处理器入队执行。

---

## 改动一：威胁失败 → NPC 进战斗

**为什么不能直接在 OnFail 里开打？** Intent 处理器在对话中间执行，对话 UI 还开着。
也不该在 Patch 里直接调 `CombatManager.StartFight`——战斗是行为，行为走 Event。

**方案**：
1. `ThreatIntent.OnFail` 设静态标记 `PendingCombatAgent`
2. 对话关闭后，Patch 向该 NPC 的 Brain **发送 `"DeferredCombat"` 事件**（用 `SendEventToAgent`，不走广播）
3. Brain 收到事件 → `ClearAllActions` → `EnqueueAction(new FightEnemyAction(Agent.Main))`
4. `FightEnemyAction.OnStart` 内部调 `CombatManager.StartFight`（已有，不改）
5. NPC 打玩家 → 玩家还手命中 → `AttackTriggerMissionLogic.OnRegisterBlow` 触发广播（改动二启用）→ 周围村民来帮忙

**注意**：Patch 里 `EndInteraction` 刚清理完动作队列 + resume 了原版 AI，紧接着的 `"DeferredCombat"` 事件会重新 `ClearAllActions`（空操作）+ 入队 `FightEnemyAction`，下一帧 Tick 执行。无冲突。

### 涉及文件

**`AccountabilityIntents.cs` — `ThreatIntent`**：
- 新增 `internal static Agent PendingCombatAgent`
- `OnFail`（Alert 上下文）：设标记，删 `TryRaiseNearbyAlert`（已有 `event_agent_damaged` 广播负责群起）

**`CrimeDialogueBuilder.cs` — `BuildTransitionsByIntent`（Deter 分支）**：
- Threat 选项 `NextNode` 从 `"continue_chat"` 改 `""`（威胁完就关对话，无论输赢）

**`ConversationEntryPatch.cs` — `ResetCrimeDialogueOnConversationEndPatch.Postfix`**：
- 在已有清理之后追加：
  ```csharp
  var combatAgent = ThreatIntent.PendingCombatAgent;
  if (combatAgent != null)
  {
      ThreatIntent.PendingCombatAgent = null;
      AgentAIController.Instance?.SendEventToAgent(
          combatAgent, "DeferredCombat", Agent.Main);
  }
  ```

**`AgentBrain.cs` — `ReceiveEvent`**：
- 新增 `"DeferredCombat"` 处理器：
  ```csharp
  if (aiEvent.EventType == "DeferredCombat")
  {
      var target = aiEvent.Args[0] as Agent;
      if (target == null || target == Owner) return;
      InteractedAgent = target;
      ClearAllActions();
      EnqueueAction(new FightEnemyAction(target));
  }
  ```

### 流程

```
玩家选"关你什么事？" → 检定 → 失败
  → NPC回应"来人！" → 对话关闭
  → Patch: SendEventToAgent(npc, "DeferredCombat", Agent.Main)
  → Brain: ClearAllActions → EnqueueAction(FightEnemyAction(玩家))
  → FightEnemyAction.OnStart → CombatManager.StartFight(npc, 玩家)
  → NPC攻击玩家 → 玩家还手命中 → OnRegisterBlow广播(改动二)
  → 附近村民 → FightEnemyAction(玩家) → 群起攻之
```

---

## 改动二：启用战斗范围广播

**现状**：`AttackTriggerMissionLogic.OnRegisterBlow` 第 231 行被注释掉。
玩家打村民，只有受害者自己反应，围观群众不知道。

### 涉及文件

**`Combat/AttackTriggerMissionLogic.cs`**：
- 新增冷却字典：`static Dictionary<(int, int), float> _lastBroadcastTime`（同一对 3 秒内最多一次）
- 第 231 行取消注释，范围 100→25m，加冷却守卫

**`AgentBrain.cs` — `event_agent_damaged` 处理器**：
- `shouldHelp` 判定通过后，**先 BubbleSay 一句参战理由**（走 `NpcSpeech.csv` + `PlaceholderResolver` 标准管道），再入队 `FightEnemyAction`：
  ```csharp
  if (shouldHelp)
  {
      string templateId = Owner == victim
          ? "CombatJoin_Victim"
          : "CombatJoin_Bystander";
      string line = NpcSpeechResolver.Resolve(templateId,
          speaker: (Owner.Character as CharacterObject)?.HeroObject,
          listener: Hero.MainHero);
      BubbleSay(line ?? (Owner == victim ? "你敢打我？！" : "你敢动我们村的人？！"));
      // 已有逻辑：清动作 + FightEnemyAction
      ...
  }
  ```
- 不需要在这里加视线检查——`BroadcastEventInRange` 已经过滤掉了看不见的 Agent（见下）。

**`AgentAIController.cs` — `BroadcastEventInRange`**：
- 在通用事件分发循环中，对 `event_agent_damaged` 加视线过滤。看不见 attacker（玩家）的 NPC 直接跳过，不收到事件：
  ```csharp
  // event_agent_damaged 需要视线：看不见攻击者的 NPC 不通知
  if (eventType == "event_agent_damaged" && !NpcSightSystem.CanNpcSeePlayer(agent))
      continue;
  ```
  `WitnessCrime` 走特殊分支（GroupStageManager），不受影响。`EndInteraction` 等也不受影响。

**`NpcSpeech.csv`**：
- 新增两行参战台词模板（`{SPEAKER_SELF}` / `{SPEAKER_PLAYER_ADDR}` 走 `AttitudeSystem` → `Settings` 世界观参数化）：
  ```csv
  CombatJoin_Victim,{SPEAKER_PLAYER_ADDR}！你敢打{SPEAKER_SELF}？！,rage
  CombatJoin_Bystander,{SPEAKER_PLAYER_ADDR}！你敢动我们村的人？！,rage
  ```

### 流程

```
玩家攻击村民A → OnRegisterBlow
  → SendEventToAgent(村民A, "event_agent_damaged")    // 受害者自己
  → BroadcastEventInRange(25m, "event_agent_damaged")  // 围观群众
    → shouldHelp(同族/同阵营) → FightEnemyAction → 群起
```

---

## 改动三：转身就走 → NPC 呼救围堵 + 重新质问

**方案**：
1. `WalkAwayIntent.OnInstant` 设 `PendingEscalationAgent`
2. 对话关闭后，Patch 做两件事：
   - **广播** `"WitnessCrime"` `(criminal: Agent.Main, judge: escalationAgent)` — **复用现有围观管道**。`GroupStageManager` 算站位 → `WitnessCrime_GatherOnLook`/`StayStare`，附近 NPC 自动围过来盯着玩家。judge 自动被排除不收自己的广播。
   - **点对点** `SendEventToAgent(escalationAgent, "ReEngageConfrontation")` — 原 NPC 重新入队质问行为链
3. 第二次质问 `AlertForceConversationAction(escalated: true)`，选项里没有"我走了"

### 涉及文件

**`AccountabilityIntents.cs` — `WalkAwayIntent`**：
- 新增 `internal static Agent PendingEscalationAgent`
- `OnInstant`（Alert 上下文，无事件）：设标记，关系 -5

**`ConversationEntryPatch.cs` — `ResetCrimeDialogueOnConversationEndPatch.Postfix`**：
- 追加：
  ```csharp
  var escalationAgent = WalkAwayIntent.PendingEscalationAgent;
  if (escalationAgent != null)
  {
      WalkAwayIntent.PendingEscalationAgent = null;
      // 复用 WitnessCrime 管道 → GroupStageManager → GatherOnLook/StayStare
      AgentAIController.Instance?.BroadcastEventInRange(
          escalationAgent.Position, 25f, "WitnessCrime", Agent.Main, escalationAgent);
      // 原 NPC 重新质问
      AgentAIController.Instance?.SendEventToAgent(
          escalationAgent, "ReEngageConfrontation", Agent.Main);
  }
  ```

**`AgentBrain.cs` — `ReceiveEvent`**：
- 新增 `"ReEngageConfrontation"` 处理器（不需要新增 `"VillagerCallForHelp"`——围观逻辑完全复用 `WitnessCrime_GatherOnLook`/`StayStare`）：
  ```csharp
  if (aiEvent.EventType == "ReEngageConfrontation")
  {
      var player = Agent.Main;
      if (player == null) return;
      if (ConfrontingBrain != null && ConfrontingBrain != this) return;
      ClearAllActions();
      InteractedAgent = player;
      ConfrontingBrain = this;
      EnqueueAction(new FollowAgentAction(player, false, radius: 2f, stopDistance: 1.5f));
      EnqueueAction(new LookAtAction(player, 0.0f));
      EnqueueAction(new AlertForceConversationAction(escalated: true));
      EnqueueAction(new StayAction(player));
  }
  ```

**`AtomicAction.cs` — `AlertForceConversationAction`**：
- 新增 `_escalated` 字段 + 构造函数重载，`OnStart` 传给 `BuildAlertInterceptScript(escalated: _escalated)`

**`CrimeDialogueBuilder.cs` — `BuildAlertInterceptScript`**：
- 新增 `bool escalated = false` 参数
- `continue_chat`：escalated 时"我走了"替换成"(威胁)谁拦着我就杀谁" + 投降

### 流程

```
玩家选"我走了"
  → WalkAwayIntent.OnInstant → 设 PendingEscalationAgent
  → 对话关闭 → Patch:
    → BroadcastEventInRange("WitnessCrime", Agent.Main, escalationAgent)
      → GroupStageManager → GatherOnLook/StayStare → NPC围过来盯着
    → SendEventToAgent(npc, "ReEngageConfrontation")
      → Brain: ClearAllActions → 入队 Follow→LookAt→AlertForceConversation(escalated)→Stay
  → NPC追上玩家 → 再次对话 → 没有"我走了"
```

---

## 改动四：束手就擒（赔钱 / 坐牢二选一）

**KCD2 模式**：被抓住后要么交罚款走人，要么没钱就蹲几天 settlement 地牢。

### 涉及文件

**`CrimeDialogueBuilder.cs`**：Deter + escalated 路径加两个投降子选项：
```csharp
transitions.Add(new() { 
    PlayerLine = "我认罚。（100 第纳尔）", 
    NpcResponse = r.Resolve("扰乱治安，罚款100第纳尔。算你识相。别再来了。"), 
    Action = "INTENT:PayRestitution", ActionParam = "alert_fine", NextNode = "" 
});
transitions.Add(new() { 
    PlayerLine = "我没钱。要抓就抓吧。", 
    NpcResponse = r.Resolve("没钱还敢闹事？！来人，把他关进地牢！"), 
    Action = "INTENT:SurrenderJail", NextNode = "" 
});
```

**`AccountabilityIntents.cs` — `PayRestitutionIntent`**（已有，修改 `Evaluate` + 加 `OnInstant`）：
- `Evaluate`：Alert 上下文（`ctx.ActiveEvent == null && ctx.IsInMission`）也返回 `Eligibility.Show()`，不再 `Hide()`
- 新增 `OnInstant` 覆盖（即时类路径，`ActionParam == "alert_fine"` 时触发，不走检定）：
  ```csharp
  public override void OnInstant(IntentContext ctx)
  {
      if (ctx.ActionParam == "alert_fine")
      {
          int fine = 100;
          GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, fine);
          var npc = ctx.Hero ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
          if (npc is Hero n)
              ChangeRelationAction.ApplyPlayerRelation(n, -3, false, true);
          var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
          brain?.ClearAllAlerts();
          AgentBrain.ConfrontingBrain = null;
      }
      else
      {
          base.OnInstant(ctx); // 走默认事件赔偿逻辑
      }
  }
  ```
  - 复用 `GiveGoldAction.ApplyBetweenCharacters`（与 `PayRestitutionIntent.OnSuccess` 同一 API，不重复造轮子）
  - `Goal` 非 null 时走检定路径（原逻辑不动），`OnInstant` 只处理 `alert_fine` 这个无检定分支

**`AccountabilityIntents.cs`** — 新增 `SurrenderJailIntent : IntentBase`：
- `Goal = null`（即时类，无检定）
- `OnInstant`：扣 200 第纳尔（抄没），关系 -10，清警戒，释放质问锁，时间跳过 0.5-1 天（`CampaignTime.IncrementSimTime`），传送到村外

**`AgentBrain.cs`**：新增 `ClearAllAlerts()`

**`IntentRegistry.cs`**：注册 `SurrenderJailIntent`

---

## 验证方式

1. **威胁→战斗**：进村拔刀 → L3 质问 → 选"关你什么事？" → 检定失败 → NPC 砍你 → 村民围过来
2. **战斗广播**：打村民 → 附近同村 NPC 过来帮忙
3. **转身→围堵**：被质问 → "我走了" → NPC 喊人 → 村民围过来 → NPC 重新追上对话 → 没有"我走了"
4. **投降**："束手就擒" → 扣钱扣关系 → NPC 放你走

---

## 文件修改清单

| 文件 | 改动 |
|------|------|
| `AccountabilityIntents.cs` | ThreatIntent: +PendingCombatAgent, OnFail设标记, 删TryRaiseNearbyAlert。WalkAwayIntent: +PendingEscalationAgent, OnInstant设标记。PayRestitutionIntent: Evaluate加Alert上下文 +OnInstant(alert_fine)。新增 SurrenderJailIntent |
| `ConversationEntryPatch.cs` | Postfix: 检查PendingCombatAgent→SendEvent("DeferredCombat")。检查PendingEscalationAgent→Broadcast("WitnessCrime")+SendEvent("ReEngageConfrontation") |
| `AttackTriggerMissionLogic.cs` | 取消注释 line 231, 100m→25m, +3秒冷却 |
| `AgentAIController.cs` | BroadcastEventInRange: event_agent_damaged 时加 NpcSightSystem 视线过滤 |
| `AgentBrain.cs` | +"DeferredCombat"处理器, +"ReEngageConfrontation"处理器, +"event_agent_damaged" BubbleSay, +ClearAllAlerts |
| `AtomicAction.cs` | AlertForceConversationAction: +_escalated字段, 构造函数重载, 传给BuildAlertInterceptScript |
| `CrimeDialogueBuilder.cs` | BuildAlertInterceptScript: +escalated参数。BuildTransitionsByIntent: Threat的NextNode→""。Deter/escalated: +投降(赔钱/坐牢)。continue_chat: escalated→拔剑替代我走了 |
| `NpcSpeech.csv` | +CombatJoin_Victim, +CombatJoin_Bystander |
| `IntentRegistry.cs` | 注册 SurrenderJailIntent |

## 实施顺序

1. **改动二**（战斗广播）— 最简单，取消注释
2. **改动四**（投降）— 独立 Intent
3. **改动一**（威胁→战斗）— 依赖改动二
4. **改动三**（围堵升级）— 依赖改动二 + 新事件

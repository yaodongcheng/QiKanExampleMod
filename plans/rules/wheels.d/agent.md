# agent — 轮子速查分卷（wheels.md 索引导航）
## NPC 动作 / 走位 / 朝向 — `Core/AgentControlHelper.cs`

**做 NPC 演出、移动、锁定一律走这里**，不要直接调 `Agent.SetScriptedPosition` 等裸 API。

```csharp
// 动画
AgentControlHelper.SetPose(agent, actionId);  GetPose(agent);  IsPlayingPose(agent, actionId);
// 强制动画（绕过 action_set 限制，临时切换到 as_human_warrior）
AgentControlHelper.ForcePlayAction(agent, actionId, restoreAfter = false);
// 移动（async 自动寻路+等待）
await AgentControlHelper.MoveTo(agent, targetVec, targetDir, stopDistance = 0.5f);
await AgentControlHelper.MoveToActor(npc, actor, stopDistance = 0.5f);
await AgentControlHelper.MovePrepare(npc);          // 移动前清 AI/停交互
AgentControlHelper.MoveEndAndInteractPrepare(npc[, initPos]);  // 到位后锁定进对话
// 朝向 / 锁定
AgentControlHelper.LookAtAgent(agent, target);  StopLooking(agent);
AgentControlHelper.FaceToActor(turnAgent, targetAgent);
AgentControlHelper.ForceUnlockAgent(agent);  StopAndReset(agent);  // 恢复自由
// 人形判定（人类或儿童 human_child——引擎把儿童排除在 IsHuman 外，玩家认知里小孩也是人）
AgentControlHelper.IsHumanOrChild(agent);     // 所有「人形角色」判定统一用它，见「引擎级非战斗人员」专节
// 信息抽取（拼 prompt 用）
AgentControlHelper.GetPartyInfo(hero);  GetBagInfo(hero, IsPrompt = false);
// 资源操作（铁律4 —— 金钱=特殊物品，三类各有纪律，禁止裸调 ChangeHeroGold/ItemRoster.AddToCounts）
// ① 转移 Transfer（守恒，贿赂/罚款/赏赐/买卖）—— null 任一端 = 对接「世界」（收发②）
int g = AgentControlHelper.TransferGold(from, to, amount, notify = true);   // 不足自动截断、绝不变负，返回实际值
int n = AgentControlHelper.TransferItems(from, to, item, count);            // item 可传 ItemObject 或 EquipmentElement(保品质)
AgentControlHelper.SetGold(hero, targetGold, notify = false);               // 绝对赋值（剧本/调试上帝指令，非守恒）
// ③ 转换 Convert（按配方非守恒，守卫+原子；引擎外自定义资源走 onConverted 钩子）
bool ok = AgentControlHelper.TryConvert(owner, inputs, outputs, onConverted);
//   inputs/outputs = IList<ResourceCost>；ResourceCost.Gold(n) / ResourceCost.Of(item, n)
//   例：吃苹果回饱腹 → TryConvert(player, [ResourceCost.Of(apple,1)], null, () => satiety += 10)
AgentControlHelper.HasResource(owner, ResourceCost.Of(item, n));            // 单项库存校验
// 婚姻
AgentControlHelper.ApplyMarriageLogic(h1, h2);  OnPlayerSelect_MarryNewLover(newLover);
```


---

## AgentBrain 行为队列 — `AI/`

每个 NPC 一个 `AgentBrain`（按 `Agent.Index` 存于 `AgentAIController`），用 `IAtomicAction` 队列做行为链。

**加一个新 NPC 行为的正确姿势**：

```csharp
// 1. 实现接口（放进 AI/Actions/AtomicAction.cs）
public interface IAtomicAction {
    void OnStart(Agent agent);
    void OnTick(Agent agent, float dt);
    bool IsFinished(Agent agent);
    void OnEnd(Agent agent);
}

// 2. 触发行为 — ⚠️ 外部代码只能通过事件投递，禁止直接操作 Brain
AgentAIController.Instance.SendEventToAgent(target, "事件名", args);
// AgentBrain.ReceiveEvent 内部自行管理 EnqueueAction / ClearAllActions / Suspend / Resume
// EnqueueAction、ClearAllActions 均为 private，外部不可调用
```

- **已有的 Action（先复用，别重写）**：`FollowAgentAction`、`MoveToPositionAction`、`LookAtAction`、`TurnToDirectionAction`、`PlayAnimAction`、`FightEnemyAction`、`DrawWeaponAction`、`StayAction`、`ForceTalkAction`、`PrepareOpeningAction`、`ReactionDecisionAction`、`FleeFromAction`（儿童恐惧逃离，见下方专节）。
- **什么才该放进原子 Action 库**：只有**高可复用**（多种行为链都会用到，如移动、朝向、播放动画）或**不可再拆分**（最小行为单元，拆了就没意义）的行为，才进 `AtomicAction.cs`。一次性的、只服务某个具体玩法的复合流程**不要**塞进来——那应该是「多个原子 Action 入队组合」。
- 复杂行为 = 多个原子 Action 入队组合，而不是写一个大 Action。


---

## 引擎级非战斗人员（儿童 human_child）— `AI/Actions/AtomicAction.cs` + `AI/AgentBrain.cs`

**引擎把儿童排除在 `Agent.IsHuman` 之外**（无 IsHumanoid 标志、非战斗人员设定），但玩家认知里小孩也是人：对话/警戒/感知/战斗事件必须与大人同等对待。凡原本判定 `agent.IsHuman` 且语义为「人形角色」的地方统一改用 `IsHumanOrChild`；凡「进入战斗」流程对儿童替换为恐惧逃离。

```csharp
// ① 人形判定（AgentControlHelper.IsHumanOrChild — 已接入 AgentAIController/NpcSightSystem/
//    AttackTriggerMissionLogic/InteractionMissionView/VisualCommands 全部替换点）
AgentControlHelper.IsHumanOrChild(agent);
//    = agent.IsHuman || agent.Monster?.StringId?.Contains("child") == true（null-safe）

// ② 儿童身份判定（AgentBrain.IsChildOwner — Monster StringId 含 "child" 即儿童，
//    不写死 "human_child"，兼容其他 mod 的儿童 monster 命名）
bool isChild = Owner != null && Owner.Monster != null && Owner.Monster.StringId?.Contains("child") == true;

// ③ 儿童逃离动作：远离威胁 8~14m ±45° 抖动，walk 逃跑，跑完恢复原版 AI
EnqueueAction(new FleeFromAction(threatAgent));
//   OnStart 照动物挣脱轮子（StealManager.OnAnimalStruggleFlee）：6 次随机方向取第一个 navmesh
//   有效点（V.NavMesh 版本封装），兜底直线逃离（引擎自动修正 navmesh）
//   OnTick 每 200ms 刷新 ScriptedMoveToPoint(isRun:false)（as_human_child 无 run 动画）
//   OnEnd → AgentControlHelper.ForceUnlockAgent（恢复原版 AI，不像 MoveToPositionAction 锁定进对话）
```

**儿童不参战三处替换点**（`AgentBrain.ReceiveEvent`，儿童一律 `FleeFromAction` 替代 `FightEnemyAction`）：

| 事件 | 大人行为 | 儿童行为 |
|------|---------|---------|
| `order_attack`（玩家下令攻击） | `FightEnemyAction` | `FleeFromAction` |
| `DeferredCombat`（威胁失败延迟开战） | `FightEnemyAction` | `FleeFromAction` |
| 护主参战（`event_agent_damaged` 旁观者/受害者） | `FightEnemyAction` + CombatJoin 台词 | `FleeFromAction` + 求救台词（`LWN_brain_child_flee`） |

**击晕免疫判定同样用 `Contains("child")` 而非 `== "human_child"`**（`InteractionMissionView`）：child monster 骨骼比例（臂长 0.6/眼高 1.2）与 adult 不同，`death_fall_front` 动画无法在其骨架播放，成功率强制 0（100% 免疫）。精确匹配会漏掉其他 mod 的儿童命名。

**文件位置**：`Core/AgentControlHelper.cs`（IsHumanOrChild）、`AI/AgentBrain.cs`（IsChildOwner + 三处替换）、`AI/Actions/AtomicAction.cs`（FleeFromAction）

---


---

## SetPartyAiAction — Party AI 控制

**不用再裸调 `SetMoveGoToSettlement` + 猜测 `SetDoNotMakeNewDecisions` 了。** 用原生 Action，全部搭配 `SetDoNotMakeNewDecisions(true)` 锁死。

```csharp
// 行军（泛用，不区分敌友）
party.Ai.SetMoveGoToSettlement(targetSettlement);
party.Ai.SetDoNotMakeNewDecisions(true);

// 巡逻（已到达后围城阶段）
SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, settlement);
party.Ai.SetDoNotMakeNewDecisions(true);

// 🔥 攻击：按定居点类型选择
if (settlement.IsVillage)
    SetPartyAiAction.GetActionForRaidingSettlement(party, settlement);     // 劫掠村庄
else if (settlement.IsFortification)
    SetPartyAiAction.GetActionForBesiegingSettlement(party, settlement);   // 围攻城堡/城镇
party.Ai.SetDoNotMakeNewDecisions(true);

// 追击指定部队
SetPartyAiAction.GetActionForEngagingParty(party, targetParty);
party.Ai.SetDoNotMakeNewDecisions(true);

// 其他可用：GetActionForDefendingSettlement / GetActionForEscortingParty / GetActionForGoingAroundParty / GetActionForVisitingSettlement
```

**发现方式**：`campaign.ai_raid_village` / `campaign.ai_siege_settlement` 等控制台指令 → `ilspycmd | grep` → 找到 `SetPartyAiAction`。

**文件位置**：`TaleWorlds.CampaignSystem.dll` → `SetPartyAiAction`（全局命名空间，using TaleWorlds.CampaignSystem 即可）。


---

## Agent 脚本化移动 — `SetScriptedPosition`（含 agent.goto）

**`agent.goto` 控制台指令**的底层实现。我们已封装在 `AgentControlHelper.MoveTo` 里，不要再裸调。

```csharp
// ✅ 走封装（自动寻路 + 等待）
await AgentControlHelper.MoveTo(agent, targetVec, targetDir, stopDistance = 0.5f);

// ❌ 禁止裸调
agent.SetScriptedPosition(ref pos, ...);  // 绕过寻路，不处理 AI 状态
agent.SetScriptedPositionAndDirection(...);
```

**控制台对照**：`agent.goto [AgentIndex] [X] [Y] [Z]` → 内部调 `MBAPI.IMBAgent.SetScriptedPosition`。C# 层只能看到函数签名，实现是 native C++。

**文件位置**：`Core/AgentControlHelper.cs`（已封装），底层 `TaleWorlds.MountAndBlade.dll` → `Agent.SetScriptedPosition`。

---

| 需求 | 继承的基类 | 范本文件 |
|------|-----------|---------|
| 战斗内每帧逻辑 / 监听 Agent 生灭 | `MissionLogic` | `AI/AgentAIController.cs` |
| 战斗内 UI 图层（Gauntlet） | `MissionView` | `Interaction/InteractionMissionView.cs` |
| UI 数据绑定 | `ViewModel` | `Interaction/StoryDialogVM.cs` |
| 大地图事件 / 存档 | `CampaignBehaviorBase` | `Core/MyBehavior.cs`、`Story/StoryContext.cs` |
| 自定义可存档类型 | `SaveableTypeDefiner` | `Story/StoryContext.cs`（SaveDefiner） |

存档：字段加 `[SaveableField(n)]`，`CampaignBehaviorBase.SyncData(IDataStore)` 里 `dataStore.SyncData("key", ref field)`，自定义类型在 `SaveDefiner` 注册。


---

## 战斗回调职责划分

引擎两个 hit 回调语义不同，**不要都往里塞**：

| 回调 | 触发条件 | 职责 |
|------|----------|------|
| `MissionLogic.OnRegisterBlow` | 攻击判定注册（伤害为 0 也触发，和平区域也触发） | **攻击意图检测**：广播事件、触发敌对、开战信号 |
| `MissionLogic.OnAgentHit` | 实际造成伤害时（伤害 > 0） | **伤害处理**：切磋虚拟血量、死亡收集、伤害统计 |

- 和平城镇挥刀 → `OnRegisterBlow` 点火，`OnAgentHit` 不点火（引擎拦截了伤害）
- **Team 切换不要在手写回调里做**，交给 `FightEnemyAction` → `CombatManager.StartFight` 管道处理
- 见 `Combat/AttackTriggerMissionLogic.cs` 为实际落地案例


---

## 警戒值系统（NpcSightSystem 维护）

```csharp
// 查询/操作
float val = NpcSightSystem.GetAlertValue(npc);  // 不存在返回 0
NpcSightSystem.AddAlertPulse(npc, amount);       // 一次性脉冲（不走 dt）

// 内部计算（OnMissionTick 中每秒触发）：
// 能看到玩家 → dt * (IdentityValue + ActionSuspiciousValue)
// 看不到玩家 → dt * (-DecayRate)
// IdentityValue: 0.15 (敌) / 0 (其他)
// ActionSuspiciousValue: 0.15 (蹲下) / 0 (正常)
// DecayRate: 0.15/s
// 脉冲事件: +2.0 (击晕/偷窃/攻击友军)
```

**文件位置**：`AI/NpcSightSystem.cs`（`_alertValues` 字典 + `GetAlertValue`/`AddAlertPulse`/`UpdateAlertValue`/`CleanupDeadAlertEntries`）。

# UI 交互模式


---

## 类型定义 — `AI/AlertTypes.cs`

```csharp
// 玩家行为分类（警戒值累加维度）
public enum PlayerActionType { Crouching, WeaponDrawn, StealUIOpen, Steal, AttackAlly, Knockout }
// 警戒阶段（UI 颜色 + NPC 行为分级）
public enum AlarmPhase { Normal, Suspicious, Cautious, Alarmed }
// L3 质问意图
public enum NpcInterceptIntent { Deter, Search, Recover, Stop }
// 对话模式开关
public enum AlertDialogueMode { StoryVM, VanillaConversation }
// 警戒条目（值 + 脉冲上下文）
public struct AlertEntry { float Value; string TargetName; string ItemName; }
```

**文件位置**：`AI/AlertTypes.cs`


---

## AgentBrain 警戒值字段与方法 — `AI/AgentBrain.cs`

警戒值状态从 `NpcSightSystem` 迁移到每个 `AgentBrain` 实例。每个 NPC 独立维护自己对玩家的警戒值明细。

```csharp
// ── 公开查询 ──
brain.AlertValue     // float — 所有条目的总和
brain.AlertPhase     // AlarmPhase — 由 AlertValue 自动计算
brain.PrimaryAction  // PlayerActionType? — 当前最高警戒值的来源
brain.IsInCombat     // bool — 是否处于战斗行为（当前或排队）；HUD 用它做战斗中警戒眼抑制（配合 Mission 级 IsInteractionDisabled）

// ── 脉冲操作 ──
brain.AddAlert(PlayerActionType.Steal, 2.0f);  // 加值（持续累加或脉冲）

// ── BubbleSay ──
brain.BubbleSay("文本");  // 通用冒泡说话入口
```

**认知更新**：节流循环（默认 100ms），`Tick` → `UpdateAlertCognition` → 可见→累加 / 不可见→按比例衰减 → `CheckPhaseTransition` → 阶段穿越发事件。

**阶段穿越事件**（在 `ReceiveEvent` 中平级处理）：
- `"BecomeSuspicious"` → `BubbleSayOnce`
- `"BecomeCautious"` → `LookAtAction(Agent.Main, 2.0f)` + `BubbleSayOnce`
- `"BecomeAlarmed"` → `StartL3Confrontation()`（脉冲抑制检查）
- `"CalmDown"` → 清理 bubbled 记录 + 行为链清理

**L3 质问**：`StartL3Confrontation` 按 `Settings.Instance.AlertDialogueMode` 分叉：
- `StoryVM`（默认）→ `PrepareOpeningAction` → `ForceTalkAction` → StoryDialogVM
- `VanillaConversation` → `AlertForceConversationAction` → `CrimeDialogueBuilder.BuildAlertInterceptScript` → `DialogueInjector.InjectScript` → 原版对话 UI

**`WitnessCrime_GatherOnLook` 犯罪类型分类**（`ProcessEvent` 中，criminal==玩家时）：
1. `IsKnockedOut(victim)` → `PlayerActionType.Knockout` + `ConfrontationType.Stop`
2. `CombatManager.IsAgentFightingPlayer(victim)` 或 `IsPlayerInCombat` → `PlayerActionType.AttackAlly` + `ConfrontationType.Stop`（斗殴，非偷窃）
3. 其余 → `PlayerActionType.Steal` + `ConfrontationType.Recover`（兜底：偷窃）

**文件位置**：`AI/AgentBrain.cs`（新增约 250 行警戒相关代码）


---

## NpcSightSystem 清理

旧 `_alertValues` 字典、`GetAlertValue`、`AddAlertPulse`、`GetAllAlertValues`、`UpdateAlertValue`、`CleanupDeadAlertEntries` 全部删除。`NpcSightSystem` 回归纯感知工具——只回答"能不能看到"，不维护认知状态。

**文件位置**：`AI/NpcSightSystem.cs`（删除约 100 行警戒值相关代码）

## 密谋命令系统接线（2026-08-07，详见 planner.md）

- `AgentBrain.ReceiveEvent` 新增事件分支：`order_execute_plan`（收 plan JSON → `PlanExecutor.Create` → `SetNpcIntent(ExecutingCommand)` → `ClearAllActions` → `EnqueueAction(new ExecutePlanAction(executor))`；收尾 OnFinished → 恢复 Following，仅当意图仍为 ExecutingCommand）与 `plan_decision`（ReactiveAgent 决策结果 → 转发 `executor.NotifyDecisionEvent`）。
- `RunReactiveAction(IAtomicAction)`：ReactiveAgent 反应通道（清当前行为并入队反应动作；brain 事件处理的内部扩展）。
- `EnqueueActionInternal`/`ClearAllActionsInternal`：plan_debug 调试专用（绕开事件闸门）。
- `AgentAIController.OnMissionTick` 末尾加 `PlanExecutor.TickAll(dt)`（执行器统一驱动）；`OnRemoveBehavior` 加 `PlanExecutor.ShutdownAll()`。
- 执行器挂接 = IAtomicAction（`ExecutePlanAction`，IsFinished = 计划完成）；执行器本体独立 tick——护主/战斗 ClearAllActions 踢掉队列项不影响执行器继续运行。

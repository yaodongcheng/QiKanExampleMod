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

- **已有的 Action（先复用，别重写）**：`FollowAgentAction`、`MoveToPositionAction`（含 `FleeFrom` 逃跑工厂——原 FleeFromAction/ReactiveFleeAction/ReactiveReturnPostAction 已并入，2026-08-11）、`LookAtAction`、`TurnToDirectionAction`、`PlayAnimAction`、`FightEnemyAction`、`DrawWeaponAction`、`StayAction`、`ForceTalkAction`、`PrepareOpeningAction`、`ReactionDecisionAction`。
- **什么才该放进原子 Action 库**：只有**高可复用**（多种行为链都会用到，如移动、朝向、播放动画）或**不可再拆分**（最小行为单元，拆了就没意义）的行为，才进 `AtomicAction.cs`。一次性的、只服务某个具体玩法的复合流程**不要**塞进来——那应该是「多个原子 Action 入队组合」。
- 复杂行为 = 多个原子 Action 入队组合，而不是写一个大 Action。

### 🔴 移动目标类型分派（找 agent 只允许 FollowAgentAction）— 2026-08-11 用户裁定

**规则**：
- 目标 = **确定坐标点**（逃跑点/回岗点/围观位/物件/区域/query）→ `MoveToPositionAction`（快照寻路到点）
- 目标 = **agent**（找人/找玩家/调查某人）→ **只允许 `FollowAgentAction`**（`keepFollow:false, stopDistance=within`）——追踪式追到身边，目标在动也不走空点。
- **禁止**对 agent 目标截位置快照走 MoveToPositionAction（PlanExecutor `move_to` 分支曾犯此错——agent 走开就走到空点，2026-08-11 已修；ReactiveAgent `investigate` 反应同规修正）。

```csharp
// PlanExecutor.move_to 步骤分派范本（PlanExecutor.cs）
if (ResolveStepAgent(step, cursor, out Agent target) && target != cursor.Agent)
    cursor.SubAction = new FollowAgentAction(target, false, stopDistance: within, keepFollow: false);
else {
    ResolveStepTarget(step, cursor, out Vec3 pos, out Vec2 dir); // 坐标点/self
    cursor.SubAction = new MoveToPositionAction(pos, dir, false, within);
}
```

**注意**：
- `FollowAgentAction` 新增 `EndBehavior` 收尾参数（2026-08-11，同 MoveToPositionAction）：`Unlock` = 解锁回原版 AI（调查/回岗类）；默认 `InteractPrepare` = 准备互动（持续跟随/对峙类）。
- keepFollow=false 的 5s 追赶瞬移兜底 = "追不上贴到身边"语义（区别于 MoveToPositionAction 的卡死瞬移）；目标快速移动（骑马玩家）时可能触发，观感突兀需调 `_maxTime`。
- 契约文档：`plans/im-command-action-upgrade.md` §5.4 目标类型路由。
- **文件位置**：`Planner/PlanExecutor.cs`（move_to 分派）、`Planner/ReactiveAgent.cs`（investigate）、`AI/Actions/AtomicAction.cs`（FollowAgentAction/MoveToPositionAction）。


---

## 🔴 战斗结果 → 当事人记忆 + 队伍广播（FightEnemyAction.OnEnd）— 2026-08-11

**解决**：玩家参与的战斗结束后 NPC 不知道结果（LLM 对"切磋结果怎么样"只能瞎编——实机 11:36:18 NPC 答"多半是您占了上风"实际玩家已阵亡）。

```csharp
// FightEnemyAction.OnEnd 开头调用（_targetEnemy 尚未置空）：
RecordFightResultIfPlayerInvolved(agent);   // AtomicAction.cs 内私有方法，模式如下
```

**模式**（两条腿，缺一不可）：
1. **当事人确定性记忆**：`AllNpcMemoryManager.GetMemory(heroId)?.RecordDynamicMemory("刚与{对手}交手，我赢了。")` — 保证**当事人**（如切磋对手）在私聊/当面对话回答正确（进 prompt【近期回忆】）。
2. **队伍感知广播**：`ImEventBroadcaster.BroadcastPlayerEvent(won ? "battle_win" : "battle_lose", "主公方才与{对手}交手，落败被打晕了过去")` — 队伍群聊议论（LLM 评论 + 参与度记忆 + 30% 接话；`custom.im_test_event` 同入口，防刷屏闸门 180s/300s/每日10条）。

**判定与边界**：
- 胜负判定：一方倒下（`!IsActive() || Health <= 0`）即定局；双方都站着（打断/撤退）不记录。
- 只处理玩家参与的战斗（`_targetEnemy == Agent.Main`）。
- ⚠️ **执行者倒下时 OnEnd 不触发**（AgentAIController 只 tick 活跃 Owner，AgentAIController.cs:217）→ "玩家胜、执行者败"的输家记忆/胜利广播天然缺位，可接受不作补救。
- 触发点**不要**用 `CampaignEvents.OnPlayerBattleEnd`——那是大地图 MapEvent 事件，Mission 内切磋倒下不触发；Mission 战斗必须在自己战斗链的收尾点（如 OnEnd）补调。
- 描述带对手名让 LLM 评论更具体；描述文本是 LLM prompt 材料（豁免铁律 13）。


---

## 🔴 玩家 agent 永不进入 mod AI 管线（AgentBrain 玩家排除纪律）— 2026-08-09

**问题**：玩家被当 NPC 处理 —— `OnAgentCreated` 给玩家也注册了 AgentBrain → 玩家被打时 `event_agent_damaged` 直发到玩家脑 → 护主/参战链（`Owner == victim` → shouldHelp）触发 → BubbleSay NPC 参战台词（"你这小子！你敢打本官？！"，PlaceholderResolver 按 speaker=玩家 填自称/称呼）+ `SuspendVanillaAI` 禁用玩家 DailyBehaviorGroup。`Brain.Tick` 有 `Owner == Agent.Main` 守卫所以 FightEnemyAction 永不执行，但 **Suspend 已生效且永不撤销**（只有行为结束才 Resume）→ 玩家整场 Mission 无法移动（致命 bug）。

**三层防线**（缺一不可，别只补一层）：

```csharp
// ① 根因：玩家永不注册 brain（AgentAIController.OnAgentCreated）
if (agent.IsMainAgent) return;   // 在 IsHumanOrChild 检查之前

// ② 事件源：受害者在场直发（AttackTriggerMissionLogic.OnRegisterBlow）排除玩家
if (!victim.IsMainAgent)
    AgentAIController.Instance.SendEventToAgent(victim, "event_agent_damaged", attacker, victim);

// ③ 纵深防御：AgentBrain.ReceiveEvent 顶部 + SuspendVanillaAI 顶部
if (Owner == Agent.Main) return;         // ReceiveEvent（IsInteractionDisabled 之后）
if (Owner == Agent.Main) return false;   // SuspendVanillaAI
```

**注意**：
- 引擎事件广播路径（`BroadcastEventInRange`）本来就有 `brain.Owner == Agent.Main` 过滤；漏网的是**直发**（`SendEventToAgent(victim, …)`）——排查时两个入口都查。
- `GetBrainForAgent(Agent.Main)` 返回 null：唯一业务调用（`PlanExecutor.IsPlayerInCombat`）null-safe，其余调用全是 `?.`，玩家无脑后行为不变。
- Tick 的玩家守卫**不是**完整防护——ReceiveEvent 与 Tick 是两条独立入口，只守 Tick 拦不住事件处理链。

**文件位置**：`AI/AgentAIController.cs`（OnAgentCreated）、`Combat/AttackTriggerMissionLogic.cs`（OnRegisterBlow）、`AI/AgentBrain.cs`（ReceiveEvent / SuspendVanillaAI）


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
// 🔴 2026-08-11 参数化合并：原 FleeFromAction 已并入 MoveToPositionAction 的逃跑工厂
EnqueueAction(MoveToPositionAction.FleeFrom(Owner, threatAgent));
//   FleeFrom 工厂照动物挣脱轮子（StealManager.OnAnimalStruggleFlee）：6 次随机方向取第一个
//   navmesh 有效点（V.NavMesh 版本封装），兜底直线逃离（引擎自动修正 navmesh）
//   工厂产出 = MoveToPositionAction(walk, stopDistance 1f, 固定超时 10s 不瞬移,
//   skipGetupDelay 立即动, EndBehavior.Unlock 恢复原版 AI, 旁白"吓得逃走了")
//   恐慌逃跑（原 ReactiveFleeAction）= 同工厂参数化（外部给点/run/15s/2f），也已并入本类
```

**儿童不参战三处替换点**（`AgentBrain.ReceiveEvent`，儿童一律 `MoveToPositionAction.FleeFrom` 替代 `FightEnemyAction`）：

| 事件 | 大人行为 | 儿童行为 |
|------|---------|---------|
| `order_attack`（玩家下令攻击） | `FightEnemyAction` | `MoveToPositionAction.FleeFrom(Owner, target)` |
| `DeferredCombat`（威胁失败延迟开战） | `FightEnemyAction` | `MoveToPositionAction.FleeFrom(Owner, target)` |
| 护主参战（`event_agent_damaged` 旁观者/受害者） | `FightEnemyAction` + CombatJoin 台词 | `MoveToPositionAction.FleeFrom(Owner, attacker)` + 求救台词（`LWN_brain_child_flee`） |

**击晕免疫判定同样用 `Contains("child")` 而非 `== "human_child"`**（`InteractionMissionView`）：child monster 骨骼比例（臂长 0.6/眼高 1.2）与 adult 不同，`death_fall_front` 动画无法在其骨架播放，成功率强制 0（100% 免疫）。精确匹配会漏掉其他 mod 的儿童命名。

**文件位置**：`Core/AgentControlHelper.cs`（IsHumanOrChild）、`AI/AgentBrain.cs`（IsChildOwner + 三处替换）、`AI/Actions/AtomicAction.cs`（MoveToPositionAction.FleeFrom 工厂）

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

## 个体战斗统一入口 — `AgentBrain.StartCombatAgainst(target)`

**所有"对指定 Agent 开战"的路径走同一入口**（2026-08-09 提取），禁止各自复制 DeferredCombat 分支代码。事件分支与直调同源：

```csharp
// AgentBrain 内部（private）。DeferredCombat 事件分支 与 StartL3CombatJoin 同源调用。
StartCombatAgainst(target);
// 行为链：SetNpcIntent(Fighting) + 推进 PendingWorldEvent → Confrontation（缘由 "a fight broke out"，
//         进赔款涨价说明）+ ClearAllActions + ForceUnlockAgent + 儿童恐惧逃离守卫 + FightEnemyAction
```

- `DeferredCombat` 事件分支（威胁失败延迟开战 / 拔剑路径对话关闭后由 ConversationEntryPatch 发送）→ `StartCombatAgainst`
- `StartL3CombatJoin`（`BecomeAlarmed` 直调；MCM 开关 `AlarmedDirectCombat` 开启后警戒拉满不走质问）→ `StartCombatAgainst(player)`
- `FightEnemyAction.OnStart` 内部自带 `ForceUnlockAgent`（注释：各事件处理器无需补）——调用点显式调用是冗余无害

**🔴 坑：`order_attack` 广播无友方过滤** — `BroadcastEventInRange` 只有 active/距离/楼层/exclude 过滤，接收端（order_attack 分支）也没有友方守卫；且玩家命令随从攻击（`InteractionController` / `PlanExecutor` / `SocialIntents`）也走同一事件 → **友方过滤只能放调用点 exclude，不能放接收端**（接收端过滤会废掉"命令随从攻击"）。已定决策（2026-08-09）：警戒拉满 / 拔剑路径**均不广播** order_attack（广播会波及玩家随从、战斗规模失控），只对单个目标走 DeferredCombat。

**文件位置**：`AI/AgentBrain.cs`（StartCombatAgainst / StartL3CombatJoin / DeferredCombat 分支）


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
- `"BecomeAlarmed"` → 默认 `StartL3Confrontation()`（脉冲抑制检查在前）；MCM 开关 `AlarmedDirectCombat` 开启 → `StartL3CombatJoin()` 跳过质问直接开战（见「个体战斗统一入口」节）
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

- `AgentBrain.ReceiveEvent` 新增事件分支：`order_execute_plan`（收 plan JSON → `PlanExecutor.Create` → `SetNpcIntent(ExecutingCommand)` → `ClearAllActions` → 🔴 单脑化重构后直接 `executor.Start(Owner)` 不入队；收尾 OnFinished → 意图复位 None，仅当意图仍为 ExecutingCommand）与 `plan_decision`（ReactiveAgent 决策结果 → 转发 `executor.NotifyDecisionEvent`）。
- `RunReactiveAction(IAtomicAction)`：ReactiveAgent 反应通道（清当前行为并入队反应动作；brain 事件处理的内部扩展）。
- `ClearAllActions`（internal）：plan_debug 调试直接调用（绕开事件闸门）；纯入队入口收敛为 `EnqueuePlanAction`（原 `EnqueueActionInternal` 与纯透传壳 `ClearAllActionsInternal` 均在 2026-08-11 删除——无空判/守卫/组合的壳不保留）。
- `AgentAIController.OnMissionTick` 末尾加 `PlanExecutor.TickAll(dt)`（执行器统一驱动）；`OnRemoveBehavior` 加 `PlanExecutor.ShutdownAll()`。
- 🔴 单脑化重构（2026-08-11）：行为步骤由执行器 `EnqueuePlanAction` 逐个入队（生命周期归脑，D4b），不再有 `ExecutePlanAction` 占位；脑 Tick 空脑分支加 ExecutingCommand 意图空窗守卫（D2）；动作完成 100ms 轮询三路径判定（IsFinished / IsActionAlive 外部清除 / RequestInterrupt）。完整纪律见 planner.md「执行器挂接」条目。

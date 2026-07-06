# 已造轮子速查

> **总纲：加功能前先查本表。命中就复用，不要重写。**
> 路径均相对 `ExampleModVS/ExampleMod/ExampleMod/`。签名为核实后的真实签名。

---

## 配置与世界观 — `Core/Settings.cs`

全局配置单例 + 世界观参数化。

```csharp
Settings.Instance.IsLLMReady           // LLM 总闸（三字段非空才 true）
Settings.Instance.LLMBaseUrl/LLMApiKey/LLMModel
Settings.Instance.WorldDescription     // 默认卡拉迪亚，TaikouContent 注入战国
Settings.Instance.EraDescription / SpeechStyle / WarriorTerms / FemaleSelfAddress
Settings.Reload();                     // 重载 config.json
```

世界观相关字串**只能**从这里取，禁止硬编码（见 [worldview.md](worldview.md)）。需要新 flavor 字段就往 Settings 加，默认值给卡拉迪亚版。

## LLM 调用 — `LLM/LLMService.cs`

单例，内置 3 次重试、HttpClient 复用。

```csharp
await LLMService.Instance.ChatAsync(systemPrompt, max_tokens = 150, needJson = true);  // 通用
await LLMService.Instance.SummarizeAsync(systemPrompt);    // 记忆总结（短）
await LLMService.Instance.MergeMemoryAsync(systemPrompt);  // 远期记忆合并
LLMService.CleanJson(raw);             // 静态，剥离 markdown ```json 包裹
```

调用前查 `IsLLMReady`；返回的 JSON 必须防御性处理（见 [defensive-coding.md](defensive-coding.md)）。

## Prompt 构建 — `LLM/PromptBuilder.cs`

按场景的静态 prompt 工厂。加新对话场景 = 在这里加一个 `BuildXxxPrompt` 静态方法，**不要在业务代码里拼 prompt 字串**。现有方法覆盖：开场冲突、技能检定结果、闲聊、谈判（核心）、社交事件分析、记忆长期化、对话总结、导演梗概、演出脚本生成。

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

## 记忆系统三件套 — `Memory/`

```csharp
// 入口：拿某 NPC 的记忆系统（惰性创建）
SingNpcMemorySystem mem = AllNpcMemoryManager.GetMemoryForAgent(agent);
SingNpcMemorySystem mem = AllNpcMemoryManager.GetMemory(stringId);   // 按英雄 id
AllNpcMemoryManager.ClearTemporaryMemories();   // 清临时士兵记忆，防泄漏
```

- `SingNpcMemorySystem`：单 NPC 的 `RecentHistory`（对话）/`DynamicMemories`（近期）/`PermanentMemory`（远期）/`GlobalNews`/`CurrentNegotiationState`/`KnownEvents`。
- `NPCProfile`：人设容器。`GetPersonaPrompt()` 聚合全部人设；`CalCurrentMotivation()` 推动机；`CalculateEstimatedValue()` 算身价；`GetCloseRelations(...)` 取关系网。
- 给 NPC 加新「记忆维度 / 人设字段」时往这三件套加，不要另起 NPC 数据类。

## 设计数据加载 — `Data/DesignDataLoad.cs`

通用 CSV ORM。**新增可配置设计数据走 CSV，不要硬编码进 .cs**。

```csharp
DataTable t = GameDatabase.Heroes;            // 已有表：Heroes/Music/TagPoint/Camera/Emotion
DynamicRecord r = t.GetByID("hero_001");      // 按英文 ID
DynamicRecord r = t.GetByScriptName("某中文名"); // 按中文名（反向索引）
r.GetString(key)/GetInt(key)/GetFloat(key)/GetBool(key)/GetList(key);
GameDatabase.Initialize();                    // Mod A 启动时
GameDatabase.LoadTablesFromPath(path);        // 内容包注入入口（TaikouContent 用）
```

## 日志 — `Debug/DebugLogger.cs`

```csharp
DebugLogger.Log("消息");   // 线程安全，落盘到 Configs/StoryEngine_RuntimeLog.txt
```

## 安全建部队 — `Core/SafeLordPartyComponent.cs`

为英雄创建独立部队时用它做最小 PartyComponent（带 null 防护），避免裸建 component 崩溃。

## 大地图 Party 出生点验证 — `WorldEvent/WorldEventSimulator.FindReachableSpawnPosition`

在大地图上为 party 选定出生点时，**必须验证出生点到目标定居点的寻路可达性**。仅检查 navmesh 面是否有效（`GetFaceIndex().IsValid()`）不够——山顶/隔水区域也有 navmesh 面，但和定居点不连通，mouse 光标会变禁用圈。

三层验证管线：

```csharp
// 入口：只需传目标定居点，内部自动生成多方向候选 + 验证
Vec2 spawnPos = FindReachableSpawnPosition(targetSettlement);

// 内部验证：
// ① GetLastPointOnNavigationMeshFromPositionToDestination — 几何投影到 navmesh
// ② AreFacesOnSameIsland(projectedFace, settlementFace) — 🔑 同一连通岛？（排除隔山/隔水）
// ③ GetPathDistanceBetweenAIFaces(projectedFace, settlementFace, …, 100f, out pathDist) — 🔑 寻路距离合理？
// ④ pathDist <= straightDist * 3 — 排除绕远路的孤立路径
// 候选：定居点周围 3 圈 × 8 个方向（18/30/42 单位半径），取第一个通过全部验证的
// 兜底：GetAccessiblePointNearPosition(settlementPos, 30f) — 引擎原生
```

**禁止**在任何 party 出生点计算中裸用 `GetFaceIndex().IsValid()` 作为可达性判断——这与鼠标变禁用图标不是同一套逻辑。鼠标禁用图标用的是 `AreFacesOnSameIsland`。辅助 party 的 `NearInstigator`/`BetweenParties` 用 `GetAccessiblePointNearPosition` 即可（参照物是已有 party，本身在可达区域）。

## 通知防刷屏冷却 — `WorldEvent/WorldEventDirector`

高频检查（如 `OnCampaignTick` 每 2 秒扫一次）里推送通知时，**必须加 per-event 冷却字典**，否则玩家站在事件附近每 2 秒弹一次同一条通知。

```csharp
// 字段
private static readonly Dictionary<string, float> _interceptCooldowns = new Dictionary<string, float>();
private const float INTERCEPT_COOLDOWN_DAYS = 0.15f; // ~3.6 小时

// 使用
if (_interceptCooldowns.TryGetValue(selected.EventId, out float lastDay)
    && currentDay - lastDay < INTERCEPT_COOLDOWN_DAYS)
    return; // 冷却中，跳过
_interceptCooldowns[selected.EventId] = currentDay;

// 定期清理过期记录（>1 天），防止内存泄漏
var expired = _interceptCooldowns.Where(kv => currentDay - kv.Value > 1f).Select(kv => kv.Key).ToList();
foreach (var key in expired) _interceptCooldowns.Remove(key);
```

---

# 三大可扩展引擎（核心资产 —— 加玩法优先挂这里）

## Story 命令引擎 — `Story/`

JSON 脚本驱动的剧情演出引擎，**命令模式**。`CommandManager`（注册分发）+ `StoryEngine`（栈式执行）+ `VisualCommands`/`SystemCommands`/`LogicCommands`（指令实现）+ `StageDirector`（站位/出入场）。

**加一条新剧情指令的正确姿势**：

```csharp
public delegate bool CommandHandler(ScriptNode node, StoryEngine engine);

// 1. 写 handler（放进对应的 Visual/System/Logic Commands 类）
public static bool HandleMyCmd(ScriptNode node, StoryEngine engine) { /* ... */ return true; }

// 2. 在 CommandManager.RegisterAll() 里注册
Register("我的指令", VisualCommands.HandleMyCmd);
```

- 返回值约定：`true` = 阻塞、等玩家输入/动画；`false` = 立即执行下一行。
- **禁止**改 `CommandManager.Execute` 或写 if-else 指令链——只 `Register`。
- 已有指令：對話/自語/旁白/對話選擇、人物登场/退场/別、選擇、變量賦值/分歧/更新/代入、ＢＧＭ變更/ＳＥ開始/進入設施 等。

LLM 自动生成剧本走 `Story/AIStoryGenerator.cs`（`StartGeneration` → 后台 `GenerateTaskAsync` → `PromptBuilder.BuildDirectorPrompt`/`BuildShowPrompt` → `AIStoryAdapt` 转成 `ScriptNode[]` → `StoryEngine.StartEvent`）。

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

// 2. 入队 / 触发
AgentBrain brain = AgentAIController.GetBrainForAgent(agent);
brain.EnqueueAction(new MyAction(...));
brain.ClearAllActions();   // 打断当前行为链
AgentAIController.Instance.SendEventToAgent(target, "事件名", args);  // 经事件投递
```

- **已有的 Action（先复用，别重写）**：`FollowAgentAction`、`MoveToPositionAction`、`LookAtAction`、`TurnToDirectionAction`、`PlayAnimAction`、`FightEnemyAction`、`DrawWeaponAction`、`StayAction`、`ForceTalkAction`、`PrepareOpeningAction`、`ReactionDecisionAction`。
- **什么才该放进原子 Action 库**：只有**高可复用**（多种行为链都会用到，如移动、朝向、播放动画）或**不可再拆分**（最小行为单元，拆了就没意义）的行为，才进 `AtomicAction.cs`。一次性的、只服务某个具体玩法的复合流程**不要**塞进来——那应该是「多个原子 Action 入队组合」。
- 复杂行为 = 多个原子 Action 入队组合，而不是写一个大 Action。

---

## NPC 互动意图引擎 — `Interaction/Intents/`

NPC 互动菜单（求婚/招募/策反/送礼/招募入伍/寒暄…）的**注册式**引擎。每个意图 = 一个类，只声明三件事：**资格(Evaluate)、目标(Goal)、成败后果(OnInstant/OnSuccess/OnFail)**；通用机制（成功率公式、掷骰、冷却、台词、置灰）在共享层。无 LLM 时对抗意图走 C# 单次检定，有 LLM 时走谈判盘。详见 [plans/no-llm-interaction.md](../no-llm-interaction.md)。

**加一个新互动选项的正确姿势**：

```csharp
// 1. 写一个意图类（放进 Interaction/Intents/ 的某个分组文件）
public class MyIntent : IntentBase {
    public override InteractionOptionType Type => InteractionOptionType.Xxx;
    public override InteractionCategory Category => InteractionCategory.Social;
    public override string DisplayName => "【我的选项】...";
    public override NegotiationGoalType? Goal => NegotiationGoalType.Xxx; // null = 即时类(不掷骰)
    public override NegotiationTactic Tactic => NegotiationTactic.Flatter; // 对抗类用，决定查哪个属性

    public override Eligibility Evaluate(IntentContext ctx) =>      // 三态
        !ctx.IsHero ? Eligibility.Hide()
      : ctx.Relation < 0 ? Eligibility.Grey("关系不够")
      : Eligibility.Show();

    public override void OnInstant(IntentContext ctx) { /* 即时类结算 */ }
    public override void OnSuccess(IntentContext ctx) { /* 对抗类成功 */ }
    // OnFail 基类默认：掉好感 + 进冷却，通常无需 override
}

// 2. 在 IntentRegistry.RegisterDefaults() 注册一行（内容包可在自己代码里 IntentRegistry.Register）
Register(new MyIntent());
```

- **禁止**改 `RegisterAllOptions`（已删）或在 `InteractionOptionManager` 写 if-else——只 `Register`。`InteractionOptionManager.BuildOptionVMs` 是薄壳，自动把可见意图转成 `StoryOptionVM`（含成功率预览/置灰）。
- `IntentContext`（`IntentContext.Build(agent, controller)`）：开对话时一次性算好身份/关系——`IsHero/Relation/SameFaction/EnemyFaction/IsLiege/IsClanLeader/IsWanderer/IsMarried/OppositeSex/PlayerHasNoKingdom/IsMySoldier/IsEnemyAgent/IsRecruitableCivilian`，及 `OnCooldown(goal)/CooldownDaysLeft(goal)`。资格判定直接读这些，别在意图里重复取数。
- **单次检定公式** `SingleRollResolver.Compute(ctx, goal, tactic, offerValue)`：复用 `NegotiationState`(难度/开局/性格Trait) + `SkillCheckSystem.CalculateSkillCheck`(技能胜率) + `NegotiationRegistry.CalculateMultiplier`(性格倍率)，**不要新造第4套公式**。
- **失败冷却** `IntentCooldownStore.IsOnCooldown/Set/DaysLeft`（per NPC+目标，经 `MyBehavior.SyncData` 跨存档）。
- **模板台词** `DialogueTemplateHelper.Get(...)`：查 CSV（`ModuleData/DesignData/Dialogue.csv`，ID=`{Goal}_{Success/Fail}`），世界观占位符走 `Settings.Instance`，空表自动兜底。
- **已有意图（先复用，别重写）**：求婚/送礼/茶席/切磋（Social）、登庸/劝诱倒戈/策反/要军资/仕官（Diplomacy）、情报/命令/跟随/寒暄/离开（General）、应募入伍（RecruitSoldier，普通平民花钱招文化基础兵+魅力砍价）。
- **结算后果统一走 `ActionHandler`**（LLM 路径也用），别在意图里裸调单边 API；金钱进出走 `AgentControlHelper`。

---

# Campaign Action 类 — 官方游戏状态变更 API

**所有对游戏世界产生影响的"动作"都应以 `*Action` 静态类为入口。** 这些是 TaleWorlds 官方的封装层，内部处理了事件广播、日志、校验、连锁反应。禁止绕过它们直接操作底层数据结构。

查找规则：需要做什么 → `ilspycmd TaleWorlds.CampaignSystem.dll | grep "class.*Action"` → 找到对应的类 → `ilspycmd -t` 看签名。

全部 60 个 Action 类位于 `TaleWorlds.CampaignSystem`（通过 `.csproj` 引用 `TaleWorlds.CampaignSystem.dll` 即可使用）：

| 类别 | Action 类 | 用途 |
|------|----------|------|
| **Party AI** | `SetPartyAiAction` | 🆕 命令 party 执行特定行为（巡逻/劫掠/围城/追击/护送/拜访） |
| **Hero 生死/状态** | `KillCharacterAction`, `MakeHeroFugitiveAction`, `DisableHeroAction`, `EndCaptivityAction` | 杀角色、设为逃犯、禁用、结束囚禁 |
| **Hero 移动/归属** | `TeleportHeroAction`, `AddHeroToPartyAction`, `RemoveCompanionAction`, `AddCompanionAction`, `AdoptHeroAction`, `TakePrisonerAction`, `TransferPrisonerAction` | 传送英雄、加入部队、移除同伴、收养、俘虏 |
| **关系/婚姻** | `ChangeRelationAction`, `ChangeRomanticStateAction`, `MarriageAction`, `MakePregnantAction`, `ApplyHeirSelectionAction` | 改关系、改恋爱状态、结婚、怀孕 |
| **声望/犯罪** | `GainRenownAction`, `ChangeCrimeRatingAction`, `PayForCrimeAction` | 加声望、改犯罪等级、付赎金 |
| **经济** | `GiveGoldAction`, `GiveItemAction`, `SellGoodsForTradeAction`, `SellItemsAction`, `SellPrisonersAction`, `BribeGuardsAction` | 给钱、给物品、交易、贿赂 |
| **定居点** | `EnterSettlementAction`, `LeaveSettlementAction`, `ChangeOwnerOfSettlementAction`, `ClaimSettlementAction`, `ChangeVillageStateAction`, `ChangeGovernorAction`, `IncreaseSettlementHealthAction`, `LeaveTroopsToSettlementAction` | 进出定居点、改所有权、改村庄状态、改总督 |
| **工坊** | `ChangeOwnerOfWorkshopAction`, `ChangeProductionTypeOfWorkshopAction`, `InitializeWorkshopAction` | 工坊所有权/类型 |
| **战争/外交** | `DeclareWarAction`, `MakePeaceAction`, `BeHostileAction`, `StartBattleAction`, `LiftSiegeAction`, `SiegeAftermathAction`, `BreakInOutBesiegedSettlementAction` | 宣战/和平/敌对/开战/围城 |
| **军团** | `GatherArmyAction`, `DisbandArmyAction` | 集结/解散军团 |
| **家族/王国** | `ChangeClanLeaderAction`, `ChangeClanInfluenceAction`, `ChangeKingdomAction`, `GainKingdomInfluenceAction`, `DestroyClanAction`, `DestroyKingdomAction` | 改族长/影响力/王国、摧毁家族/王国 |
| **Party** | `DestroyPartyAction`, `DisbandPartyAction`, `MergePartiesAction` | 摧毁/解散/合并部队 |

**⚠️ 铁律 4 的补充**：`GiveGoldAction.ApplyBetweenCharacters` 是官方 API，但**仍要通过 `AgentControlHelper.TransferGold` 调用**（确保守恒/日志/世界观参数化）。同样 `GiveItemAction` → `AgentControlHelper.TransferItems`。Action 类是我们写 helper 时的参考，不是绕过的借口。

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

## 战斗回调职责划分

引擎两个 hit 回调语义不同，**不要都往里塞**：

| 回调 | 触发条件 | 职责 |
|------|----------|------|
| `MissionLogic.OnRegisterBlow` | 攻击判定注册（伤害为 0 也触发，和平区域也触发） | **攻击意图检测**：广播事件、触发敌对、开战信号 |
| `MissionLogic.OnAgentHit` | 实际造成伤害时（伤害 > 0） | **伤害处理**：切磋虚拟血量、死亡收集、伤害统计 |

- 和平城镇挥刀 → `OnRegisterBlow` 点火，`OnAgentHit` 不点火（引擎拦截了伤害）
- **Team 切换不要在手写回调里做**，交给 `FightEnemyAction` → `CombatManager.StartFight` 管道处理
- 见 `Combat/AttackTriggerMissionLogic.cs` 为实际落地案例

## 大世界地图对话 → 真对话 Mission 接入

**咽喉补丁 `CampaignMapConversation.OpenConversation` + inquiry 分流 + 真 Mission + 自定义对话管线复用。**

覆盖场景：玩家在大世界沙盘遇到中立/未开战部队时，弹 inquiry 让玩家选「原版对话 / 新版对话」，选新版则开真对话 Mission（真实 Agent + MissionScreen），自动触发本 mod 的 `InteractionMissionView` 自定义对话管线，零重构复用现有 Agent 演出/镜头/意图引擎。

```csharp
// 1. 设置静态标志（在 inquiry 回调里，开 mission 前）
MapEncounterDialogState.Active = true;
MapEncounterDialogState.Partner = conversationPartnerData.Character;
CampaignMission.OpenConversationMission(p, q);   // 开真对话 mission

// 2. Harmony 拦截咽喉（自动生效，PatchAll 注册）
[HarmonyPatch(typeof(CampaignMapConversation), nameof(CampaignMapConversation.OpenConversation))]
public static class ConversationEntryPatch
{
    [HarmonyPrefix]  // 大地图遇敌 → 弹 inquiry 分流
    [HarmonyPostfix] // 定居点对话 → 犯罪事件注入
}

// 3. Harmony 抑制原版 ConversationMissionLogic.OnMissionTick（仅对我们的 mission）
[HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
public static class SuppressVanillaConversationMissionPatch
{
    [HarmonyPrefix]
    public static bool Prefix() => !MapEncounterDialogState.Active; // Active → 跳过原版 tick
}

// 4. InteractionMissionView 自动触发 + 收尾（已在 OnMissionTick/OnDialogueEnded/Finalize 中集成）
//    - OnMissionTick：检测 Active → 按 Partner CharacterObject 在 Mission.Current.Agents 中精确定位 partner Agent
//    - StartFreeConversationFlow(partnerAgent)：复用现有对话管线（VM/控制器/镜头/意图引擎）
//    - OnDialogueEnded：MapEventHelper.OnConversationEnd() → Mission.Current.EndMission() → 回大地图
//    - OnMissionScreenFinalize：安全清标志（防 ESC 退出泄漏）
```

**关键文件**：`Interaction/Dialogue/MapEncounterDialogState.cs`（静态标志）、`Interaction/Dialogue/ConversationEntryPatch.cs`（对话入口统一拦截 + 犯罪对话注入 + 原版 tick 抑制）、`Interaction/InteractionMissionView.cs`（自动触发/收尾）。

**边界**：只对 Hero 生效（无 Hero 放行原版）；仅自家的 conversation mission 抑制（静态 gate）；settlement 内点 NPC / 请求会面不受影响；LLM 路径走 `IsLLMReady` 总闸。

---

# 世界事件引擎 — `WorldEvent/`

## 架构

四层：**模拟器**（DailyTick 生成事件 + party）→ **数据库**（Event CRUD + JSON 持久化）→ **导演**（五种推送控制可见性）+ **通知控制器**（NinjaReport → Inquiry 书信）→ **宿敌追踪**（交手记录 → 伤疤 → 复仇）。

## 核心入口

```csharp
// 生成管线（WorldEventSimulator.TryGenerateNewEvent）
// ① 动机驱动真人冲突（优先！）
TryGenerateMotivatedEvent()  // 扫 Hero 关系/仇恨/性格 → 真人冲突（NobleConflict/Betrayal/Assassination…）
// ② 回落随机事件
roll → 选类型 → 选定居点 → 选人 → SpawnEventParty → 存入 DB

// 动机扫描四层：跨clan仇恨 → 同clan内斗 → 经济冲突 → 野心扩张

// 征用真人部队（WorldEventSimulator.SpawnEventParty）
// instigator 正带队 → 调遣真实部队（LeaderHero、Scouting=300加速、不改位置）
// instigator 在定居点 → 新建 party（定位目标附近）
// 到场才触发后果：CheckEventPartyArrivals → dist < 3 单位 → ApplyExpiryConsequences

// 控制台调试
custom.worldevent_list       // 列出所有活跃事件
custom.worldevent_force [类型] [严重度]  // 强制生成（默认BanditRaid）
custom.worldevent_status     // 内部状态
```

## 给世界事件加新婚事件的正确姿势

1. 在 `WorldEventType` 枚举加类型
2. 在 `WorldEventConfig` 静态构造里 `Register(new WorldEventConfig{...})`
3. 在 `WorldEventDirector` / `WorldEventNotificationController` 的 switch 里加对应文本
4. 在 `Narrative.csv` 加 `WorldEvent_Greeting_{Type}_Victim` / `Instigator` 条目

---

# AgentHUD — 3D 角色头上通用 HUD 系统

**原地升级替换旧 `BubbleSay*` 系统**。为所有 Human Agent 提供统一的 3D 头上 HUD，管理五大元素：名字、说话冒泡、血条、伤害数字、警戒眼睛。

## 核心入口

```csharp
// 获得单例（MissionView）
AgentHudMissionView.Instance

// 让 Agent 说话（原 BubbleSayMissionView.AgentBubbleSay）
AgentHudMissionView.AgentSay(agent, text);         // 静态快捷方法
AgentHudMissionView.AgentSay(stringId, text);

// 确保 Agent 有 HUD（延迟创建策略：有内容要显示才创建 VM）
AgentHudMissionView.Instance.EnsureHud(agent);

// 控制台
custom.agentHud_say <agentStringId> <text>
```

**文件位置**：`AgentHUD/AgentHudMissionView.cs`（MissionView）、`AgentHUD/AgentHudVM.cs`（VM）、`AgentHUD/AgentHudCollectionVM.cs`（MBBindingList 容器）、`GUI/Prefabs/AgentHudNearby.xml`（Gauntlet Prefab）。

## 五大元素与显隐规则

| 元素 | VM 属性 | 显隐条件 | 持续时间 | FOV |
|------|---------|----------|----------|:---:|
| **名字** | `ShowName` + `AgentName` | ShowSpeech \|\| ShowHealth \|\| ShowDamage | 跟随触发元素 | ✅ |
| **说话** | `ShowSpeech` + `SpeechText` | `Speak(text)` 调用 | `4s + text.Length * 0.1s` | ✅ |
| **血条** | `ShowHealth` + `CurrentHealthWidth` | 拔武器/战斗中/血量<95%/警戒态 | 持续（条件消失隐藏） | ✅ |
| **伤害** | `ShowDamage` + `DamageText` | 受伤害瞬间 | 2s | ✅ |
| **警戒** | `ShowAlert` + `AlertFillHeight/EyeBgColor/EyeFillColor` | 警戒值 > 0 | 持续（归零隐藏） | ❌ **豁免** |

**警戒 FOV 豁免**：警戒眼睛不受 FOV 角度限制——NPC 在玩家身后盯你，更该知道。屏幕外时 clamp 到边缘做方向指示。名字只在 FOV 内显示（ShowAlert 不触发名字），玩家转身面对 NPC 后名字浮现。

**名字总领规则**：`ShowName = ShowSpeech || ShowHealth || ShowDamage`（不含 ShowAlert）。

**容器可见性**：`IsVisible = ShowName || ShowAlert`（警戒眼睛可独立触发容器显示）。

## 性能：距离分级

| 距离 | 范围 | 更新频率 | 做什么 |
|------|------|----------|--------|
| **近** | ≤ 15m | 每 10 帧 | 完整：血条 + 警戒值 + 说话 + 坐标 |
| **中** | 15m ~ 50m | 每 30 帧 | 仅警戒值 + 坐标 |
| **远** | > 50m | 不处理 | 不创建 HUD / 隐藏 |

**延迟创建**：不是一开始就给所有 Agent 创建 HUD，而是按需创建（有警戒值/说话/战斗 → 创建）。

## AgentHudVM 关键属性

```csharp
// 注入数据（由 MissionView 调用）
hud.AlertValue = NpcSightSystem.GetAlertValue(agent);  // 警戒值 0~2+，每帧注入
hud.UpdateLogic();   // 低频：血量/血条条件/名字总领（近距10帧/中距30帧）
hud.UpdateFrame(dt); // 高频：动画插值 + 计时器（每帧）
hud.Speak(text);     // 说话入口

// 可绑定属性（DataSourceProperty）
PosX, PosY, Scale, IsVisible, BubbleWidth, BubbleHeight,
AgentName, ShowName,
SpeechText, ShowSpeech,
CurrentHealthWidth, ShowHealth,
DamageText, ShowDamage,
AlertFillHeight, ShowAlert, EyeBgColor, EyeFillColor
```

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

## NinjaNotification → Inquiry 书信流

**一切重要通知的标准流**：右侧悬浮环（hover 一行摘要）→ 点击弹 Inquiry 书信（详情 + 双按钮）。

```csharp
// 不要直接往 NinjaNotification 塞长文本！走这个模式：
string shortSummary = "⚠ 雷别莱特村 · 匪患";  // 一行，hover 显示
string fullBody = "德瑟特·哈米尔正带人劫掠…";   // 详情，Inquiry 显示

NinjaNotificationManager.Show(shortSummary, () =>
{
    InformationManager.ShowInquiry(new InquiryData(
        "标题", fullBody,
        hasOk, hasCancel, "去看看", "知道了",
        onOk, onCancel));
});
```

**关键文件**：`Notify/NinjaNotificationMissionView.cs`（管理器）、`Notify/NinjaNotificationVM.cs`（VM）、`GUI/Prefabs/CustomNotify.xml`（Prefab）。

## KCD2 式轮次对话

**所有 NPC 交互统一流程**：NPC 先说开场白（右侧无选项）→ 玩家点"继续" → 选项出现。

```csharp
// StartInteraction 模式：
_vm.Show(name, openingLine);       // NPC 说话
_vm.AreOptionsVisible = false;      // 隐藏选项

_vm.OnClickContinue = () =>         // 玩家点"继续"
{
    RefreshInitialOptions();         // 选项出现
};
```

**关键文件**：`Interaction/StoryDialogVM.cs`（`OnClickContinue` 回调 + `ShowContinueHint` 属性）、`Interaction/InteractionController.cs`（`StartInteraction`）。

---

# 日志纪律

**铁律**：① `DebugLogger.Log` 只记录玩家可感知的事 + 关键后台状态变更。② Per-NPC 循环日志是垃圾——每轮扫描最多一条汇总。③ 错误始终记录。

```csharp
// ✅ 好的日志
DebugLogger.Log($"[Player] NinjaReport: {summary}");            // 玩家看到通知
DebugLogger.Log($"[Player] Inquiry: '去看看' — {loc}");          // 玩家做出选择
DebugLogger.Log($"[Player] Talk to: {npcName}");                 // 玩家跟谁说话
DebugLogger.Log($"[WorldEvent] New event: {type} at {loc}");     // 事件创建
DebugLogger.Log($"[WorldEvent] Motivated conflict: A → B");     // 真人冲突
DebugLogger.Log($"[CommissionIssue] {settlement}: scanned {n} NPCs, created {m} issues"); // 轮次汇总

// ❌ 垃圾日志（每条占一行，2500行淹没13行有用信息）
// GetAvailableDefs / HasCommissionsFor / OnCheckForIssue — 逐NPC日志全砍
```

**日志前缀约定**：`[Player]` = 玩家感知事件，`[WorldEvent]` = 世界事件生命周期，`[Commission*]` = 委托系统关键节点。

---

# 村庄动物偷窃与库存同步 — `Stealth/VillageAnimalTracker.cs` + `Interaction/InteractionMissionView.cs`

场景动物（羊/牛/猪/鹅/鸡）偷窃系统，带持久化追踪、自然恢复、ItemRoster 自动同步、价格修正。

## 动物识别 — `InteractionMissionView.IsAnimalAgent` / `GetLivestockItemForAnimal`

```csharp
// 判断是否为动物 Agent（村庄场景中 IsHuman=false 的牲畜）
InteractionMissionView.IsAnimalAgent(agent);  // → bool

// Monster.StringId → 牲畜 ItemObject 静态缓存（两轮查找：精确 ID + 遍历 Animal 类型 + 兜底名字匹配）
ItemObject item = InteractionMissionView.GetLivestockItemForAnimal(monsterId, animalName);
```

动物 monster ID 白名单：`sheep`, `cow`, `hog`, `goose`, `chicken`（`InteractionMissionView.AnimalMonsters`）。

## 偷窃持久化 — `VillageAnimalTracker`

三层持久化数据（按 `"settlementId|monsterId"` 为 key，JSON 序列化，`MyBehavior.SyncData` 跨存档）：

```csharp
// 记录偷窃
VillageAnimalTracker.RecordTheft(settlementId, monsterId, count = 1);
// 查询被偷数
int stolen = VillageAnimalTracker.GetStolenCount(settlementId, monsterId);
// 每日自然恢复（每种每天恢复 1 只）
VillageAnimalTracker.DecayDaily();  // MyBehavior.DailyTick 中调用

// 缓存/读取场景自然生成数（首次进场景时记录）
VillageAnimalTracker.SetNaturalCount(settlementId, monsterId, count);
int natural = VillageAnimalTracker.GetNaturalCount(settlementId, monsterId);
bool has = VillageAnimalTracker.HasNaturalCache(settlementId);
```

## ItemRoster 补足 — `InteractionMissionView.TopUpRosterToNaturalCounts`

```csharp
// 按缓存自然数补足村庄 ItemRoster：expected = naturalCount - stolenCount，只补不删
// 可从场景进入或村庄菜单调用（无场景依赖）
InteractionMissionView.TopUpRosterToNaturalCounts(settlement);
```

## 偷动物 — `InteractionMissionView.TryStealAnimal` (async)

```csharp
// 异步：面向动物 → ForcePlayAction("act_pickup_down_begin") → 等待 400ms
// → 查找物品（GetLivestockItemForAnimal 缓存）→ 加入玩家背包
// → settlement.ItemRoster.AddToCounts(item, -1)（铁律 4.② Sink）
// → VillageAnimalTracker.RecordTheft → animal.FadeOut → ForcePlayAction("act_pickup_down_end")
// 带 _isStealingAnimal 并发守卫
```

## 动物近距离检测

`ProcessAgentCandidate` 中 `NpcSightSystem.IsPlayerSeeing` 对动物永远返回 false（`TickTrackedTarget` 过滤非人类 Agent），动物跳过此预检，只依赖距离+点积判定。

## 价格修正 — `VillageAnimalPricePatch`

非本地特产动物（不在 `Village.VillageType.Productions` 中）：买入 5 倍、卖出 0.3 倍。只对玩家交易生效。

```csharp
// Harmony Postfix on VillageMarketData.GetPrice(EquipmentElement, MobileParty, bool, PartyBase)
// 自动生效，PatchAll 注册
```

## 触发点一览

| 触发时机 | 补丁 / 方法 | 作用 |
|----------|------------|------|
| 进村庄场景 | `SyncSceneAnimalsWithInventory` (MissionView.OnMissionTick 首帧) | 缓存自然数 + 裁剪被偷动物 + 补 Roster |
| 开村庄菜单 | `VillageMenuAnimalPatch` (Harmony Postfix on `GameMenu.SwitchToMenu("village")`) | 补 Roster（读缓存，不进场景也能触发） |
| 交易界面打开 | `TradeScreenAnimalLoggerPatch` (Harmony Prefix on `InventoryManager.OpenScreenAsTrade`) | 打印 ItemRoster 动物日志 |
| 价格查询 | `VillageAnimalPricePatch` (Harmony Postfix on `VillageMarketData.GetPrice`) | 非本地动物价格修正 |

**文件位置**：`Stealth/VillageAnimalTracker.cs`、`Interaction/InteractionMissionView.cs`（`SyncSceneAnimalsWithInventory` / `TryStealAnimal` / `TopUpRosterToNaturalCounts` / `ProcessAgentCandidate` 及全部 Patch 类）、`Core/MyBehavior.cs`（`DailyTick` 衰减 + `SyncData` 持久化）。

---

# 版本兼容层 — `Core/VersionCompat.cs`

**同一份源码，双版本编译。** `V` 静态类封装了 v1.2.12 ↔ Latest 的全部 API 差异。每一对 API 差异用一个 `V.xxx()` 方法封装，内部 `#if !MB2_V1212` / `#else` 分支。

**使用纪律**：
- 凡是两个版本 API 不一样的调用，**一律走 `V.xxx()`，禁止在业务代码里裸写 `#if !MB2_V1212`**（除非是 Harmony 补丁或结构级差异）
- 新加 V 方法后**必须两个配置都编译通过**
- 版本宏 `MB2_V1212` / `MB2_V146` 由 csproj 读 `Version.xml` 自动定义，不要手动定义

```csharp
// ── 位置（v1.2.12: .Position2D / Latest: .GetPosition2D）
V.Pos(party)              // Vec2 — MobileParty
V.Pos(settlement)         // Vec2 — Settlement
V.SetPos(party, pos)      // void — 设置 party 位置

// ── 部队移动（v1.2.12: party.Ai.SetMove* / Latest: party.SetMove*）
V.SetMoveTo(party, pos)           V.SetMoveEngage(party, target)
V.SetMoveToTown(party, settlement) V.SetMovePatrol(party, pos)
V.SetMoveEscort(party, target)     V.MoveTarget(party) → MobileParty

// ── 部队生命周期
V.MakeParty(id, component)          // CreateParty 3参/2参
V.DelParty(party)                   // RemoveParty / DestroyPartyAction
V.InitPartyPos(party, template, pos) // InitializeMobilePartyAtPosition Vec2/CampaignVec2
V.SetPartyName(party, name)         // SetCustomName / Party.SetCustomName

// ── Agent 控制
V.IsAgentAI(agent) → bool           V.SetAgentAI(agent)
V.IsAgentPlayer(agent) → bool       V.SetAgentPlayer(agent)

// ── 武器 / 动作
V.MainWpn(agent) → EquipmentIndex   V.OffWpn(agent) → EquipmentIndex
V.ActName(agent, channelIndex = 0) → string

// ── UI
V.NewLayer(order, name = null) → GauntletLayer  // 构造参数顺序反了
V.LoadMov(layer, name, vm)                       // 返回类型不同，v1.2.12 存 object

// ── 其他
V.GetStartTime() → CampaignTime      V.KingdomStr(kingdom) → float
V.EmptyText() → TextObject           V.NavMesh(scene, pos, out faceIndex) → bool
V.JoinDefect(clan, from, to)         V.GetEnemyKingdoms(kingdom) → IEnumerable<Kingdom>
```

**文件位置**：`Core/VersionCompat.cs`（约 420 行）。

**版本参考 DLL**：`Modules/1.2.12DLL/` 和 `Modules/1.4.6DLL/` 存放了另一版本的 DLL 副本，**仅 `ilspycmd` 反编译用，不参与编译**。在 v1.2.12 电脑上开发时查 `1.4.6DLL/` 看 Latest API，反之亦然。方法：`ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "Method"`。

---

# csproj 版本自动检测

**一个 `Debug` 配置通吃两台电脑**，无需手动切换。原理：编译时读 `$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml`，根据其中的版本号自动定义宏。

```xml
<!-- 读 Version.xml -->
<MB2_VersionFile>$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml</MB2_VersionFile>
<MB2_VersionFileContent Condition="Exists('$(MB2_VersionFile)')">$([System.IO.File]::ReadAllText('$(MB2_VersionFile)'))</MB2_VersionFileContent>

<!-- 按版本号定义精确宏 -->
<DefineConstants Condition="$(MB2_VersionFileContent.Contains('v1.2.12'))">$(DefineConstants);MB2_V1212</DefineConstants>
<DefineConstants Condition="$(MB2_VersionFileContent.Contains('v1.4.6'))">$(DefineConstants);MB2_V146</DefineConstants>
```

**结果**：
| 电脑 | Version.xml | 定义的宏 |
|------|-----------|---------|
| v1.2.12 | `v1.2.12` | `DEBUG;TRACE;MB2_V1212` |
| v1.4.6  | `v1.4.6`  | `DEBUG;TRACE;MB2_V146` |

**新增版本**：TaleWorlds 出新版本时，在 csproj 里加一行 `MB2_VXXX` 宏即可。代码里用 `#if !MB2_V1212` 判断"比 v1.2.12 新"，用 `#if MB2_V146` 判断"恰好 v1.4.6"。

`Debug_v1.2.12` 保留作为手动兜底（强制 v1.2.12，不读 Version.xml）。

**文件位置**：`ExampleMod.csproj` PropertyGroup 段。

---

# Harmony 补丁版本兼容

Harmony 补丁在 `PatchAll()` 时如果找不到目标方法会**直接抛异常崩溃**。跨版本时必须处理：

1. **方法消失了** → `#if MB2_V1212` / `#else` 写两套，各版本补各自的目标
2. **方法签名变了** → 同上，用 `#if` 分支写不同的 Prefix/Postfix 参数
3. **编译时找不到类型**（如全局命名空间 vs using 冲突）→ 用 `AccessTools.Method("TypeName:MethodName")` 动态查找
4. **类型所在子命名空间变了** → `typeof()` 用完全限定名 + `#if` 分支

```csharp
// 场景 1：两版本各补各的方法
#if MB2_V1212
[HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
public static class DebugCrashPatch
{
    public static void Prefix(MobileParty __instance, PartyTemplateObject pt, int troopNumberLimit) { ... }
}
#else
[HarmonyPatch]
public static class DebugCrashPatch
{
    // AccessTools 运行时查找，绕过编译时类型不可见
    private static MethodBase TargetMethod() => AccessTools.Method("MobilePartyHelper:FillPartyManuallyAfterCreation");
    public static void Prefix(MobileParty mobileParty, PartyTemplateObject partyTemplate, int desiredMenCount) { ... }
}
#endif

// 场景 2：方法拆分了，每个版本补一个
#if MB2_V1212
[HarmonyPatch(typeof(AgentInteractionInterfaceVM), "SetAgent")]
public static class ChangeInteractionTextPatch
{
    public static void Postfix(AgentInteractionInterfaceVM __instance, Agent focusedAgent)
    {
        __instance.SecondaryInteractionMessage = "";
        __instance.PrimaryInteractionMessage = "";
    }
}
#else
[HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM), "SetHumanAgent")]
public static class ChangeInteractionTextPatch
{
    public static void Postfix(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM __instance, Agent focusedAgent)
    {
        // ⚠️ 不能 Clear()！ResetFocus() 会按索引访问 [0]/[1]，列表空了就 ArgumentOutOfRangeException
        __instance.PrimaryInteractionMessages?.ApplyActionOnAllItems(x => x.ResetData());
        __instance.SecondaryInteractionMessages?.Clear(); // Secondary 安全，只被 .Count 检查
    }
}
#endif
```

**排查方法**：`ilspycmd <DLL> -t <TypeName> | grep "方法名"` 确认目标方法在两个版本中的存在性和签名。如果 `typeof()` 编译报错但 ilspycmd 确定类型存在（可能是命名空间遮蔽），用 `AccessTools.Method` 绕过。

**注意**：编译通过不代表运行时能跑——Harmony 是运行时绑定的。必须在目标版本的实际游戏里测试。

**MBBindingList 坑点**：v1.4.6 里 `PrimaryInteractionMessages` / `SecondaryInteractionMessages` 从 `string` 变成了 `MBBindingList<T>`。清空内容时**不能 `Clear()`**——后续代码（如 `ResetFocus()`）可能按索引 `[0]`/`[1]` 直接访问，列表空了直接 `ArgumentOutOfRangeException`。正确做法是 `ApplyActionOnAllItems(x => x.ResetData())` 清空内容但保留占位。

---

# 原版对话流注入 — `Interaction/Dialogue/DialogueInjector.cs`

**JSON 驱动的原版 `ConversationManager` 对话注入器。当 NPC 对话需要走原版 UI（而不是 StoryDialogVM）时，优先用 JSON 注入，禁止硬编码 `DialogFlow` 链式调用。**

## 设计原则

| 场景 | 对话 UI | 何时用 |
|------|---------|--------|
| **Quest / Issue 对话** | 🔴 原版 `ConversationManager` + JSON 注入 | 任务接取、进行中讨论、任务目标对话——老玩家熟悉的原版体验 |
| **闲聊 / 自由对话** | `StoryDialogVM`（已有轮子） | 非任务场景的 NPC 互动、LLM 自由生成 |

**优先原版**：凡是能挂到 `hero_main_options` / `issue_offer` / `quest_offer` token 的，走 JSON 注入。只有原版 token 体系覆盖不了的场景（如大地图偶遇、无 Hero 的平民）才用 StoryDialogVM。

## JSON 格式

文件放在 `ModuleData/DesignData/Dialogues/*.json`。

```json
{
  "InjectAtToken": null,           // 挂载点: null="hero_main_options", "quest_offer", "issue_offer"
  "EntryOption": "（闲聊）…",       // NPC 主菜单里的入口选项文本。缺省用文件名。
  "EntryTurn": "start",            // 从哪个 turn 开始
  "turns": [
    {
      "Id": "start",               // 唯一标识（可被 NextTurn 引用）
      "SpeakerIndex": 0,           // 谁说（0=对话中的第一个 NPC）
      "NpcLine": "啊，你来得正好！",
      "Options": [
        {
          "PlayerLine": "什么怪事？",
          "NpcResponse": "最近夜里总有人……",
          "NextTurn": "more_detail",   // 选此选项后跳转的 turn Id。null=关闭对话
          "Action": "NONE",            // INCREASE_RELATION / DECREASE_RELATION / GIVE_GOLD / TAKE_GOLD
          "ActionValue": 0
        }
      ]
    }
  ]
}
```

**Turn 图结构**：`Id` = 节点标识，`NextTurn` = 边。不同选项可以指向完全不同的后续 turn。引擎运行时：`TurnToken(fileTag, turnId) → "lwnpc_<文件名>_<turnId>"` 作为 ConversationManager token。

## 核心 API

```csharp
// 从 JSON 文件注入到当前 NPC 对话树
DialogueInjector.InjectFromJson(jsonPath);    // → string 结果描述

// 清除所有注入
DialogueInjector.ClearAll();

// 文件查找（ModuleData/DesignData/Dialogues/ → Configs/）
DialogueInjector.FindJsonFile(fileName);      // → 完整路径 or null
DialogueInjector.GetSearchPathsDescription(fileName);
```

## 控制台指令

```
custom.inject_dialogue test_talk       → 加载并注入 test_talk.json
custom.inject_dialogue my_quest.json   → 加载并注入 my_quest.json
custom.inject_dialogue clear           → 清除所有注入
```

注入时机：对话开始前（大地图上、进村前）随时可跑。下次跟任意 NPC 交谈时，入口选项出现在 NPC 主菜单。

## 底层原理

直接操作 `ConversationManager` 的 `_sentences` 表（token 状态机），**不依赖 `DialogFlow` 建造者**。每个 turn 注册为：
1. NPC 台词 → `AddDialogLineMultiAgent`（非玩家句子，引擎自动播）
2. 玩家选项 → `AddPlayerLine`（通过 `DialogFlow` 薄壳）
3. NPC 回应 → `AddDialogLineMultiAgent`（输出到 `NextTurn` 的 token 或 `close_window`）

清理：`RemoveRelatedLines(owner)` 按归属哨兵批量删除，不动原版对话。

**文件位置**：`Interaction/Dialogue/DialogueInjector.cs`（注入引擎）、`Debug/MyCommands.cs`（`InjectDialogueFromJson` 薄壳指令）。JSON 示例：`ModuleData/DesignData/Dialogues/test_talk.json`。

---

# 🆕 意图/行动/任务统一重构（2026-07-04 新增）

## IntentBase 新 API — NPC 与玩家平权

```csharp
// 意图来源（Player / Npc / Both）
public virtual IntentSource Source => IntentSource.Both;

// NPC 意图响应的事件类型白名单
public virtual string[] TriggerEvents => Array.Empty<string>();

// 深度事件匹配（EventType 匹配后，检查 Args 是否满足条件）
public virtual bool CanHandle(AIEvent aiEvent, IntentContext ctx) => true;

// OnInstant / OnSuccess / OnFail 不变，保留 imperative 风格
public virtual void OnInstant(IntentContext ctx) { }
public virtual void OnSuccess(IntentContext ctx) { }
public virtual void OnFail(IntentContext ctx) { /* 基类默认：掉好感 + 进冷却 */ }


**文件位置**：`Interaction/Intents/IntentBase.cs`

## IntentContext.BuildForNpc — NPC 视角上下文

```csharp
// NPC 视角构建：NPC 发起意图时的上下文（交互目标是玩家）
var ctx = IntentContext.BuildForNpc(npcAgent, npcHero);
// 返回 null = 无法发起意图（无 Agent 也无 Hero）
// ctx.NpcLevel: None / AgentOnly（仅 Mission 行为）/ Full（有 Hero，完整功能）
```

**文件位置**：`Interaction/Intents/IntentContext.cs`

## IntentRegistry 新方法 — NPC 意图查询

```csharp
// 取 NPC 可发起的意图（Source 含 Npc 标志，且 Evaluate 通过）
IntentRegistry.GetNpcInitiatives(ctx);

// 按类名查找 NPC 意图
IntentRegistry.FindNpcIntent("GuardInterceptIntent");
```

**文件位置**：`Interaction/Intents/IntentRegistry.cs`

## AgentBrain 新事件分发 — IntentRegistry 优先 + 兜底

```csharp
// ReceiveEvent 改造：先查 IntentRegistry（MatchesEvent 两层匹配）
// → 命中 → intent.OnInstant(ctx) → AgentBrain 入队 IAtomicAction
// → 未命中 → HandleLegacyAtomicAction（旧 if/else 兜底）

// MatchesEvent：① TriggerEvents 白名单 ② intent.CanHandle(aiEvent, ctx) 深度匹配
```

**文件位置**：`AI/AgentBrain.cs`

## 新建 NPC 意图类 — NpcInitiativeIntents.cs

7 个 NPC 主动意图类，按 IntentBase 格式：`NewsConflictIntent` / `GuardInterceptIntent` / `CrimeAccusationIntent` / `RevengeIntent` / `GreetingIntent`（Both）/ `OfficialBusinessIntent` / `CrushIntent`

每个实现 `TriggerEvents` + `CanHandle` + `OnInstant`（创建 PrepareOpeningAction）。

**文件位置**：`Interaction/Intents/NpcInitiativeIntents.cs`


## InteractionOptionType 扩展 — 追责类型合并

```csharp
// 新增 15 个 InteractionOptionType 值（从 AccountabilityOptionType 迁移）：
PayRestitution / CharmDefense / FrameSuspect / Threat / Investigate / Confess /
SilenceWitness / LeadRetaliation / WorkOffDebt /
BetrayQuest / InnocenceProof / Settle / AcceptBountyQuest / LureArrest / Arrest

// 新增 InteractionCategory.Accountability
// AccountabilityOptionType 枚举已删除
// InteractionOptionCategoryMap 已补全新分类映射
```

**文件位置**：`Interaction/InteractionOptionManager.cs` / `Interaction/Intents/AccountabilityIntents.cs`

## SettlementHonorStore — 独立文件

从 `InteractionOptionManager.cs` 末尾抽出到独立文件 `Interaction/SettlementHonorStore.cs`（纯数据存储，与交互管理解耦）。

## 叙事迁移 — QuestManager 硬编码字串清理

`QuestManager.GetQuestDescription()` 的 ~120 行日本战国硬编码字串已替换为通用简化描述。`GetQuestTitle()` 同步清理。叙事全部走 `NarrativeResolver` → CSV 管道。

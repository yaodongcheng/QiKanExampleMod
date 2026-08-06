# intents — 轮子速查分卷（wheels.md 索引导航）
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

# 🔴 对话 Intent 的场景判别铁律 — 真场景 vs 大地图临时对话 Mission

**解决什么问题**：对话 Intent 的 `OnInstant/OnSuccess/OnFail` 常按「在不在 Mission 里」分流后果（当场开打 vs 召唤复仇队、现场价 vs 远程价、FadeOut vs 直接消失）。但 `ctx.IsInMission`（= `Mission.Current != null`）**区分不了真场景和大地图的临时对话 Mission**——后者也是真 Mission，只是光秃秃的对话场景。写任何对话 Intent 必须先想清楚后果发生在三种场合的哪一种：

| 场合 | `Mission.Current` | `ConversationMissionLogic` 行为 | 特征 |
|------|------|:---:|------|
| ① 真场景 Mission（村庄/城镇/酒馆漫游） | 非 null | **无**（城镇中心 27 个行为里没有它） | 周围有村民/守卫/道具，能场景内开打、围堵、围观 |
| ② 大地图临时对话 Mission | 非 null | **有** | `OpenConversationMission` 开的对话场景（仅 5 个行为），只有对话双方，没有"村子" |
| ③ 纯大地图对话（inquiry，无 Mission） | null | — | 无 Agent 层，只能动 Campaign 层状态 |

**判别范式（已封装在 IntentContext）**：

```csharp
// ✅ 后果依赖「周围有其他 NPC / 场景道具」（开打、围堵、广播 order_attack、叫守卫、现场价）：
if (ctx.InRealScene) { ... }   // = IsInMission && !IsTempConversationMission
//    在 ② 里漏掉这个排除 → 对着空场景广播，叙事和机制双出戏

// ✅ 后果只作用在对话对象自己身上（FadeOut、TakePrisoner）：
//    ctx.Agent != null 即可，不用排 ②（临时 Mission 里 Agent 真实存在）
```

**`IsTempConversationMission` 是原生判别**（反编译核实）：临时对话 Mission 的行为列表带 `ConversationMissionLogic`，真场景 Mission 没有；引擎随 Mission 生灭维护，无静态标志泄漏风险，且**原版地图对话同样覆盖**（不限于本 mod 的遭遇管线）。

```csharp
// IntentContext 构造时一次性算好（IntentContext.cs）：
IsTempConversationMission =
    Mission.Current?.GetMissionBehavior<ConversationMissionLogic>() != null;  // SandBox.Conversation.MissionLogics
```

⚠️ `MapEncounterDialogState.Active` **不是**场景判别器，别再用它干这个——它只是「本 mod 遭遇对话管线」的闸门（抑制原版 ConversationMissionLogic tick、定位 Partner），原版地图对话时它是 false，但场合②的语义依然成立。

**新 Intent 自查**：
1. 这个 Intent 的后果依赖「周围有其他 NPC/场景」吗？→ 是则条件必须用 `ctx.InRealScene`，禁止裸写 `ctx.IsInMission`
2. 在 ② 里的替代后果形态是什么？（范本：拔剑 → 真场景当场开打 / ②③ 召唤大地图复仇队，**二者只取其一**，不叠加）
3. 玩家可见文案（DisplayMessage/Inquiry）在三种场合下分别读得通吗？（"围了过来"在 ② 里出戏）

**落地范本**：`FightVillagersIntent.OnInstant`（AccountabilityIntents.cs）——`ctx.InRealScene` 分流场景内开打 vs 召唤复仇队。

**存量审计（2026-07-28 已完成）**：全部 `ctx.IsInMission` / `Mission.Current != null` 用点已逐一过堂——🔴 真修 2 处（PayRestitution 现场价 ②中错算 2 倍、CrimeDialogueBuilder 威胁失败"来人！"②中没人可来）；🟡 自文档化 8 处（Alert 质问类/WalkAway 围堵挣脱/Comply/CombatSurrender，②中本不可达，统一改 `ctx.InRealScene`）；✅ 保留 1 处（LureArrest FadeOut 只动对话对象自己，②中正确）。新代码一律用 `ctx.InRealScene`，别再引入裸 `ctx.IsInMission` 场景判断。

**文件位置**：`Interaction/Intents/IntentContext.cs`（`IsInMission` / `IsTempConversationMission` / `InRealScene`）、`Interaction/Intents/AccountabilityIntents.cs`（FightVillagersIntent 范本）、`Interaction/Dialogue/MapEncounterDialogState.cs`（管线闸门，非判别器）

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


---

## Intent Tactic 必须响应 ActionParam——不同手段不能共用同一技能

**同一个 Intent 被多次复用时（通过 `ActionParam` 区分手段），其 `Tactic` 和 `GetOfferValue` 必须根据 `ActionParam` 动态选择。禁止所有手段共用同一个固定的 `Tactic`。**

### 问题场景

```csharp
// ❌ 对话层区分了手段，但 Intent 层不区分
// 对话："（给些钱）…"→ActionParam="bribe"  /  "（威胁）…"→ActionParam="threat"
// Intent：两者都走 Tactic = Flatter → Charm 检定
// 结果：拿刀威胁目击者 → 魅力检定。这说不通。

public class SilenceWitnessIntent : IntentBase
{
    public override NegotiationTactic Tactic => NegotiationTactic.Flatter; // 写死
    // 没有 override GetOfferValue → 永远返回 0f
}
```

**为什么这样不对**：
- 塞钱封口 → 应该看玩家出价是否够高（`GetOfferValue`）
- 拿刀威胁 → 应该看玩家流氓习气（`Tactic = Threaten → Roguery`）
- 写死 `Flatter` + `GetOfferValue = 0` → 两个完全不同的手段，变成了同一个魅力检定

### 正确做法

利用 `Evaluate` 先于检定执行的事实，在 `Evaluate` 中根据 `ctx.ActionParam` 缓存状态，`Tactic` 和 `GetOfferValue` 返回缓存值：

```csharp
public class SilenceWitnessIntent : IntentBase
{
    private NegotiationTactic _tactic = NegotiationTactic.Flatter;
    private float _offerValue = 0f;

    public override NegotiationTactic Tactic => _tactic;
    public override float GetOfferValue(IntentContext ctx) => _offerValue;

    public override Eligibility Evaluate(IntentContext ctx)
    {
        // ... 基础 eligibility 检查 ...

        switch (ctx.ActionParam)
        {
            case "bribe":
                _tactic = NegotiationTactic.Bribe;       // 贿赂 → Charm
                _offerValue = 0.3f;                      // 小额献礼加成
                break;
            case "threat":
                _tactic = NegotiationTactic.Threaten;     // 威胁 → Roguery
                _offerValue = 0f;                         // 威胁不靠出价
                break;
            default:
                _tactic = NegotiationTactic.Flatter;
                _offerValue = 0f;
                break;
        }
        return Eligibility.Show();
    }
}
```

### 调用时序保证

`Evaluate` → `Tactic` / `GetOfferValue` → `SimpleCompute` 的执行顺序是有保证的：

```
DialogueInjector.ExecuteIntentAction():
  Evaluate(ctx)           ← 第一步：缓存 _tactic / _offerValue
  intent.Tactic           ← 第二步：读取缓存值（传给 SimpleCompute）
  intent.GetOfferValue()  ← 第二步：读取缓存值
  SimpleCompute(...)      ← 第三步：公式计算
```

### 自查

新增/修改复用了 `ActionParam` 的 Intent 时问自己：

1. **不同 ActionParam 对应不同的玩家手段吗？** → 如果是（给钱 vs 威胁 vs 说服），**Tactic 必须不同**。
2. **涉及金钱/物品付出的手段有 `GetOfferValue` 吗？** → 如果没有，出价不参与检定，玩家付多付少没区别。
3. **默认分支（ActionParam == null）的 Tactic 合理吗？** → 必须有合理兜底。


---

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


---

## IntentContext.BuildForNpc — NPC 视角上下文

```csharp
// NPC 视角构建：NPC 发起意图时的上下文（交互目标是玩家）
var ctx = IntentContext.BuildForNpc(npcAgent, npcHero);
// 返回 null = 无法发起意图（无 Agent 也无 Hero）
// ctx.NpcLevel: None / AgentOnly（仅 Mission 行为）/ Full（有 Hero，完整功能）
```

**文件位置**：`Interaction/Intents/IntentContext.cs`


---

## IntentRegistry 新方法 — NPC 意图查询

```csharp
// 取 NPC 可发起的意图（Source 含 Npc 标志，且 Evaluate 通过）
IntentRegistry.GetNpcInitiatives(ctx);

// 按类名查找 NPC 意图
IntentRegistry.FindNpcIntent("GuardInterceptIntent");
```

**文件位置**：`Interaction/Intents/IntentRegistry.cs`


---

## AgentBrain 新事件分发 — IntentRegistry 优先 + 兜底

```csharp
// ReceiveEvent 改造：先查 IntentRegistry（MatchesEvent 两层匹配）
// → 命中 → intent.OnInstant(ctx) → AgentBrain 入队 IAtomicAction
// → 未命中 → HandleLegacyAtomicAction（旧 if/else 兜底）

// MatchesEvent：① TriggerEvents 白名单 ② intent.CanHandle(aiEvent, ctx) 深度匹配
```

**文件位置**：`AI/AgentBrain.cs`


---

## 新建 NPC 意图类 — NpcInitiativeIntents.cs

7 个 NPC 主动意图类，按 IntentBase 格式：`NewsConflictIntent` / `GuardInterceptIntent` / `CrimeAccusationIntent` / `RevengeIntent` / `GreetingIntent`（Both）/ `OfficialBusinessIntent` / `CrushIntent`

每个实现 `TriggerEvents` + `CanHandle` + `OnInstant`（创建 PrepareOpeningAction）。

**文件位置**：`Interaction/Intents/NpcInitiativeIntents.cs`



---

## InteractionOptionType 扩展 — 追责类型合并

```csharp
// 新增 14 个 InteractionOptionType 值（从 AccountabilityOptionType 迁移）：
PayRestitution / CharmDefense / FrameSuspect / Threat / Investigate / Confess /
SilenceWitness / LeadRetaliation /
BetrayQuest / InnocenceProof / Settle / AcceptBountyQuest / LureArrest / Arrest
// （WorkOffDebt 干活抵债已于 2026-08-03 删除——入口从未接入对话，属死代码，勿复活）

// 新增 InteractionCategory.Accountability
// AccountabilityOptionType 枚举已删除
// InteractionOptionCategoryMap 已补全新分类映射
```

**文件位置**：`Interaction/InteractionOptionManager.cs` / `Interaction/Intents/AccountabilityIntents.cs`


---

## SettlementHonorStore — 独立文件

从 `InteractionOptionManager.cs` 末尾抽出到独立文件 `Interaction/SettlementHonorStore.cs`（纯数据存储，与交互管理解耦）。

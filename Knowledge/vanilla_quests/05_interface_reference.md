# 05 — 完整接口/API 参考

> **每个 API 含**：签名、参数说明、返回值、所属 DLL、调用范例、参考任务。
> **使用方式**：Ctrl+F 搜索功能关键词 → 找到 API → copy 签名。

---

## 目录

1. [QuestBase — 任务基类 API](#1-questbase--任务基类-api)
2. [IssueBase — 问题基类 API](#2-issuebase--问题基类-api)
3. [表现力 API（追踪/日志/提示）](#3-表现力-api)
4. [经济 API（金币/物品/部队）](#4-经济-api)
5. [NPC API（创建/装备/关系）](#5-npc-api)
6. [大地图 AI API（部队操控）](#6-大地图-ai-api)
7. [事件系统 API（订阅/分发）](#7-事件系统-api)
8. [对话系统 API（DialogFlow/Persuasion）](#8-对话系统-api)
9. [结算 API（完成/失败/背叛）](#9-结算-api)
10. [资源管理 API（互斥/冷却/标记）](#10-资源管理-api)

---

## 1. QuestBase — 任务基类 API

**DLL**: `TaleWorlds.CampaignSystem.dll`
**命名空间**: `TaleWorlds.CampaignSystem`

### 生命周期

```csharp
// 构造函数中调用 — 自动注册事件 + 调用 OnStartQuest
void InitializeQuestOnCreation();

// 构造函数中调用 — 注册对话流
abstract void SetDialogs();

// 构造函数中调用 — 订阅 Campaign 事件
abstract void RegisterEvents();

// 任务激活回调
abstract void OnStartQuest();

// 每小时回调
abstract void HourlyTick();

// 从存档加载后回调
virtual void InitializeQuestOnGameLoad();
```

### 完成方式

```csharp
// 成功
void CompleteQuestWithSuccess();

// 取消（战争/村庄被毁/NPC死亡等外部原因）
void CompleteQuestWithCancel(TextObject log);

// 失败（玩家被打败等）
void CompleteQuestWithFail(TextObject log);

// 超时
void CompleteQuestWithTimeOut(TextObject log);

// 背叛（玩家选择背叛任务发布者）
void CompleteQuestWithBetrayal(TextObject log);
```

### 日志

```csharp
// 离散进度条
JournalLog AddDiscreteLog(
    TextObject taskStartText,    // 任务开始描述
    TextObject progressText,     // 进度描述
    int currentProgress,         // 初始值
    int targetProgress);         // 目标值

// 普通日志（无进度条）
void AddLog(TextObject text);
```

### 追踪

```csharp
// 添加大地图追踪标记
void AddTrackedObject(object obj);
// obj 类型: MobileParty, Hero, Settlement, ItemObject

// 检查是否已追踪
bool IsTracked(object obj);

// 移除追踪（通常不需要手动调用，Quest 完成自动清理）
void RemoveTrackedObject(object obj);
```

### 属性

```csharp
Hero QuestGiver { get; }                  // 任务发布者
CampaignTime QuestDueTime { get; }        // 时限
int RewardGold { get; }                   // 奖励金
MBList<JournalLog> JournalEntries { get; } // 日志列表
bool IsOngoing { get; }                   // 任务是否进行中
int RelationshipChangeWithQuestGiver { get; set; } // 结算关系变更
```

### 对话流

```csharp
protected DialogFlow OfferDialogFlow;      // 接任务对话
protected DialogFlow DiscussDialogFlow;    // 进行中对话
protected DialogFlow QuestCharacterDialogFlow; // 目标 NPC 对话
```

---

## 2. IssueBase — 问题基类 API

**DLL**: `TaleWorlds.CampaignSystem.dll`
**命名空间**: `TaleWorlds.CampaignSystem.Issues`

### 必须实现

```csharp
abstract TextObject IssueBriefByIssueGiver { get; }
abstract TextObject IssueAcceptByPlayer { get; }
abstract TextObject IssueQuestSolutionExplanationByIssueGiver { get; }
abstract TextObject IssueQuestSolutionAcceptByPlayer { get; }
abstract TextObject Title { get; }
abstract TextObject Description { get; }
abstract QuestBase GenerateIssueQuest(string questId);
abstract IssueFrequency GetFrequency();
abstract bool CanPlayerTakeQuestConditions(
    out PreconditionFlags flags,
    out TextObject refusalText,
    Hero issueGiver);
abstract bool IssueStayAliveConditions();
abstract void HourlyTick();
```

### 可选重写（三种解决路径）

```csharp
// Alternative Solution
virtual bool IsThereAlternativeSolution => false;
virtual int AlternativeSolutionBaseNeededMenCount => 5;
virtual int AlternativeSolutionBaseDurationInDaysInternal => 6;
virtual TextObject IssueAlternativeSolutionExplanationByIssueGiver => TextObject.Empty;
virtual TextObject IssueAlternativeSolutionAcceptByPlayer => TextObject.Empty;
virtual TextObject IssueAlternativeSolutionResponseByIssueGiver => TextObject.Empty;
virtual TextObject AlternativeSolutionStartLog => null;
virtual TextObject IssueAlternativeSolutionSuccessLog => null;
virtual TextObject IssueAlternativeSolutionFailLog => null;

// Lord Solution
virtual bool IsThereLordSolution => false;
virtual Hero CounterOfferHero { get; protected set; }
virtual int NeededInfluenceForLordSolution => 10;
virtual TextObject IssueLordSolutionExplanationByIssueGiver => TextObject.Empty;
virtual TextObject IssueLordSolutionAcceptByPlayer => TextObject.Empty;
virtual TextObject IssueLordSolutionResponseByIssueGiver => TextObject.Empty;
virtual bool LordSolutionCondition(out TextObject explanation) { explanation = null; return true; }

// CounterOffer 文本
virtual TextObject IssueLordSolutionCounterOfferBriefByOtherNpc => TextObject.Empty;
virtual TextObject IssueLordSolutionCounterOfferExplanationByOtherNpc => TextObject.Empty;
virtual TextObject IssueLordSolutionCounterOfferAcceptByPlayer => TextObject.Empty;
virtual TextObject IssueLordSolutionCounterOfferDeclineByPlayer => TextObject.Empty;
virtual TextObject IssueLordSolutionCounterOfferAcceptResponseByOtherNpc => TextObject.Empty;
virtual TextObject IssueLordSolutionCounterOfferDeclineResponseByOtherNpc => TextObject.Empty;

// 奖励/效果/冷却
virtual int RewardGold => 0;
virtual bool IssueQuestCanBeDuplicated => false;
virtual float GetIssueEffectAmountInternal(IssueEffect effect) => 0f;
virtual int RelationshipChangeWithIssueOwner { get; protected set; }

// 钩子
virtual void AfterIssueCreation() { }
```

---

## 3. 表现力 API

### AddTrackedObject
```csharp
// QuestBase 方法
void AddTrackedObject(object obj);
void RemoveTrackedObject(object obj);
bool IsTracked(object obj);
```
**DLL**: CampaignSystem
**参数**: obj — MobileParty / Hero / Settlement / ItemObject
**参考**: MerchantNeedsHelpWithOutlaws, EscortMerchantCaravan, ScoutEnemyGarrisons

### MBInformationManager.AddQuickInformation
```csharp
// 静态方法
static void MBInformationManager.AddQuickInformation(TextObject text);
```
**DLL**: CampaignSystem
**效果**: 屏幕上方弹出简短提示
**参考**: MerchantNeedsHelpWithOutlaws, NearbyBanditBase

### QuestHelper.AddMapArrowFromPointToTarget
```csharp
// 静态方法
static void QuestHelper.AddMapArrowFromPointToTarget(Vec2 from, Vec2 to);
```
**DLL**: CampaignSystem
**参考**: ScoutEnemyGarrisons

### VisualTrackerManager
```csharp
// 通过 QuestManager 间接使用
// QuestManager.Instance.TrackedObjects — 管理所有 Quest 的追踪对象
```
**DLL**: CampaignSystem

### JournalLog.UpdateCurrentProgress
```csharp
// JournalLog 实例方法
void UpdateCurrentProgress(int newValue);
```
**DLL**: CampaignSystem
**效果**: 更新进度条数字，达到目标时自动标记完成

---

## 4. 经济 API

### GiveGoldAction.ApplyBetweenCharacters
```csharp
// 静态方法
static void GiveGoldAction.ApplyBetweenCharacters(
    Hero fromHero,    // null = 虚空生成 (Grant)
    Hero toHero,      // null = 虚空销毁 (Sink)
    int amount,
    bool disableNotification = false);
```
**DLL**: CampaignSystem
**铁律 4**: 转移类必须用此 API，禁止裸调 Hero.ChangeHeroGold
**参考**: 几乎所有任务

### ChangeRelationAction.ApplyPlayerRelation
```csharp
static void ChangeRelationAction.ApplyPlayerRelation(
    Hero targetHero,
    int amount,
    bool showQuickNotification = true);
```
**DLL**: CampaignSystem

### TraitLevelingHelper.OnIssueSolvedThroughQuest
```csharp
static void TraitLevelingHelper.OnIssueSolvedThroughQuest(
    Hero questGiver,
    params Tuple<TraitObject, int>[] traits);
// 示例: new Tuple<TraitObject, int>(DefaultTraits.Honor, 30)
```
**DLL**: CampaignSystem
**参考**: RevenueFarming, SnareTheWealthy, FamilyFeud

### ItemRoster.AddToCounts
```csharp
// ItemRoster 实例方法
void AddToCounts(
    ItemObject item,
    int count,
    EquipmentElement? overriddenElement = null);
// 正数 = 添加, 负数 = 移除
```
**DLL**: Core (TaleWorlds.Core)
**铁律 4**: 必须配对使用或标注 Grant/Sink
**参考**: HeadmanNeedsGrain, VillageNeedsTools

### ChangeCrimeRatingAction.Apply
```csharp
static void ChangeCrimeRatingAction.Apply(
    IFaction faction,
    float amount,
    bool showQuickNotification = true);
```
**DLL**: CampaignSystem
**参考**: RevenueFarming（私吞税收 CrimeRating +45）

---

## 5. NPC API

### HeroCreator.CreateSpecialHero
```csharp
static Hero HeroCreator.CreateSpecialHero(
    CharacterObject template,
    Settlement bornSettlement,
    Clan clan = null,
    IFaction faction = null,
    int age = -1);
// 返回: Hero（未注册到世界，HiddenInEncylopedia = true 隐藏）
```
**DLL**: CampaignSystem
**参考**: NotableWantsDaughterFound（创建女儿+恶棍）, FamilyFeud（创建犯人）

### Hero 关系设置
```csharp
// 家庭关系
hero.Father = fatherHero;
hero.Mother = motherHero;
hero.Spouse = spouseHero;

// 百科隐藏
hero.CharacterObject.HiddenInEncylopedia = true;

// 职业设置
hero.SetNewOccupation(Occupation.Notable);  // 设为 RuralNotable

// AI 设置
hero.SetMortality(immortal: true);  // 不死
```
**DLL**: CampaignSystem

### CivilianEquipment.AddEquipmentToSlotWithoutAgent
```csharp
// Equipment 实例方法
void AddEquipmentToSlotWithoutAgent(
    EquipmentIndex slot,       // Weapon0, Weapon1, ..., Body, Head, Leg, ...
    EquipmentElement element); // new EquipmentElement(itemObject)
```
**DLL**: Core
**参考**: FamilyFeud（犯人装备匕首）

### MBObjectManager — 动态资源查找
```csharp
// 按 ID 查找（可能被 mod 屏蔽返回 null）
MBObjectManager.Instance.GetObject<CharacterObject>("townsman_vlandia");

// 按条件遍历所有已注册对象（不受 mod 屏蔽）
MBObjectManager.Instance.GetObject<CharacterObject>(
    obj => obj.IsHero && obj.Culture.StringId == "vlandia");

// 泛型支持: ItemObject, CharacterObject, Settlement, CultureObject 等
```
**DLL**: ObjectSystem (TaleWorlds.ObjectSystem)
**铁律 5**: 必须两轮查找策略

---

## 6. 大地图 AI API

### SetPartyAiAction — 部队 AI 操控
```csharp
// ① 围绕定居点巡逻
static void SetPartyAiAction.GetActionForPatrollingAroundSettlement(
    MobileParty mobileParty,
    Settlement settlement);

// ② 前往定居点
static void SetPartyAiAction.GetActionForGoingToSettlement(
    MobileParty mobileParty,
    Settlement settlement);

// ③ 跟随部队
static void SetPartyAiAction.GetActionForEscortingParty(
    MobileParty followerParty,
    MobileParty targetParty);

// ④ 攻击部队
static void SetPartyAiAction.GetActionForAttackingParty(
    MobileParty attackerParty,
    MobileParty targetParty);
```
**DLL**: CampaignSystem

### 锁定 AI 决策
```csharp
// 防止部队的默认 AI 覆盖你的指令
mobileParty.Ai.SetDoNotMakeNewDecisions(true);

// 解锁（任务完成/取消时恢复）
mobileParty.Ai.SetDoNotMakeNewDecisions(false);
```
**DLL**: CampaignSystem
**参考**: MerchantNeedsHelpWithOutlaws

### MobileParty 创建
```csharp
// 创建自定义部队
static MobileParty MobileParty.CreateParty(
    string stringId,
    PartyTemplateObject template,
    Hero leaderHero = null);
```
**DLL**: CampaignSystem

### BanditPartyComponent — 创建匪徒部队
```csharp
// 创建匪徒部队（非静态，需反编译确认具体签名）
static MobileParty BanditPartyComponent.CreateBanditParty(
    string stringId,
    Settlement spawnSettlement,
    Clan banditClan,
    Hideout hideout,
    int troopCount);
```
**DLL**: CampaignSystem
**参考**: EscortMerchantCaravan

---

## 7. 事件系统 API

### CampaignEvents — 常用事件订阅

```csharp
// 全部通过 AddNonSerializedListener 订阅
// 参数: (object listener, Action<T...> handler)

// 部队消灭
CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(
    this, (MobileParty destroyedParty, PartyBase destroyerParty) => { ... });

// 进入定居点
CampaignEvents.SettlementEntered.AddNonSerializedListener(
    this, (MobileParty party, Settlement settlement, Hero hero) => { ... });

// 离开定居点
CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(
    this, (MobileParty party, Settlement settlement) => { ... });

// 宣战
CampaignEvents.WarDeclared.AddNonSerializedListener(
    this, (IFaction faction1, IFaction faction2) => { ... });

// 家族换阵营
CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(
    this, (Clan clan, Kingdom old, Kingdom new, ...) => { ... });

// 村庄被劫
CampaignEvents.VillageBeingRaided.AddNonSerializedListener(
    this, (Village village) => { ... });

// 大地图战斗开始
CampaignEvents.MapEventStarted.AddNonSerializedListener(
    this, (MapEvent mapEvent, PartyBase attacker, PartyBase defender) => { ... });

// 每小时每支部队（★ 最常用）
CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(
    this, (MobileParty party) => { ... });

// 每日每支部队
CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(
    this, (MobileParty party) => { ... });

// 匪徒被招募
CampaignEvents.BanditPartyRecruited.AddNonSerializedListener(
    this, (MobileParty banditParty) => { ... });

// 定居点易主
CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(
    this, (Settlement settlement, bool openToClaim, Hero newOwner, ...) => { ... });
```
**DLL**: CampaignSystem (全部)
**参考**: MerchantNeedsHelpWithOutlaws (9 个事件), RevenueFarming (5 个)

### 注销
```csharp
// Quest 完成时自动清理（不需要手动注销）
// QuestBase.FinalizeQuest() 内部处理
```

---

## 8. 对话系统 API

### DialogFlow 构建
```csharp
DialogFlow.CreateDialogFlow(string tokenId, int priority = 100)
    .NpcLine(TextObject npcLine)
    .PlayerLine(TextObject playerLine)
    .Condition(Func<bool> condition)
    .Consequence(OnConsequenceDelegate consequence)
    .CloseDialog();

// NpcLine 变体（带变量）
.NpcLine("{=TOKEN}文本 {VARIABLE} 更多文本",
    new[] { someCondition },
    new[] { someConsequence })
```
**DLL**: CampaignSystem

### ConversationManager — 注册自定义 DialogFlow
```csharp
// 注册额外的对话流（除了 Offer/Discuss）
Campaign.Current.ConversationManager.AddDialogFlow(dialogFlow);

// 移除（任务结束时）
Campaign.Current.ConversationManager.RemoveDialogFlow(dialogFlow);
```
**DLL**: CampaignSystem
**参考**: FamilyFeud (10 个), NotableWantsDaughterFound (8 个)

### PersuasionTask — 说服系统
```csharp
var task = new PersuasionTask(targetHero)
{
    ReservationType = PersuasionTask.PersuasionReservationType.Issue,
    Difficulty = 5,  // 越高越难
    BlockArgument = new PersuasionBlockArgument(/* ... */),
};

// 触发说服对话
Campaign.Current.ConversationManager.StartPersuasion(
    difficulty,
    task,
    onSuccess: () => { /* 说服成功 */ },
    onFail: () => { /* 说服失败 */ });
```
**DLL**: CampaignSystem
**参考**: NotableWantsDaughterFound (Diff=5), FamilyFeud (Diff=4)

---

## 9. 结算 API

### Quest 结算
```csharp
// 5 种结算方式
void CompleteQuestWithSuccess();
void CompleteQuestWithCancel(TextObject log);
void CompleteQuestWithFail(TextObject log);
void CompleteQuestWithTimeOut(TextObject log);
void CompleteQuestWithBetrayal(TextObject log);
```
**DLL**: CampaignSystem

### 超时前钩子
```csharp
// QuestBase 虚方法 — 超时前最后一刻的操作
virtual void OnBeforeTimedOut(
    ref bool completeWithSuccess,
    ref bool doNotResolveTheQuest)
{
    // 默认: completeWithSuccess = false, doNotResolveTheQuest = false
    // 设为 true 阻止自动超时 → 可以弹出选择框让玩家手动选择
}
```
**DLL**: CampaignSystem
**参考**: RevenueFarming（超时弹出"交出/私吞"选择框）

### Issue 结算
```csharp
// Issue 成功完成
void CompleteIssueWithQuest();

// Issue 超时
void CompleteIssueWithTimedOut();

// Issue 条件失效
void CompleteIssueWithStayAliveConditionsFailed();

// Issue 清理
void IssueFinalized();  // 自动清理追踪/对话/冷却
```
**DLL**: CampaignSystem

### ShowQuestResolvePopUp
```csharp
// 弹出选择框（如"交出税收"or"私吞"）
void ShowQuestResolvePopUp();
```
**DLL**: CampaignSystem
**参考**: RevenueFarming

---

## 10. 资源管理 API

### BusyHideouts — 藏身处互斥
```csharp
// HashSet<Settlement>
Campaign.Current.BusyHideouts.Add(hideoutSettlement);
Campaign.Current.BusyHideouts.Remove(hideoutSettlement);
bool isBusy = Campaign.Current.BusyHideouts.Contains(hideoutSettlement);
```
**DLL**: CampaignSystem
**参考**: MerchantNeedsHelpWithOutlaws, NearbyBanditBase

### IsCurrentlyUsedByAQuest — 部队任务标记
```csharp
// MobileParty 属性
mobileParty.IsCurrentlyUsedByAQuest = true;

// 扫描时跳过已占用的部队
if (mobileParty.IsCurrentlyUsedByAQuest) return;
```
**DLL**: CampaignSystem
**参考**: MerchantNeedsHelpWithOutlaws

### IssueCoolDownData — 冷却管理
```csharp
// IssuesCampaignBehavior 内部管理
// IssueManager 中的冷却数据结构
// 冷却天数: IssueModel.IssueOwnerCoolDownInDays (默认 30)
```
**DLL**: CampaignSystem

### GameMenu.SwitchToMenu — 菜单切换
```csharp
// 触发特定 GameMenu（用于 VillageEvent 等场景）
GameMenu.SwitchToMenu(string menuId);
// 常用 menuId:
//   "village_deliver_grain" — 交付谷物
//   "village_collect_revenue" — 开始收税
//   "village_event_*" — 随机村庄事件
```
**DLL**: CampaignSystem
**参考**: RevenueFarming（VillageEvent 菜单）, HeadmanNeedsGrain

---

## 附录 A：TextObject 常用模式

```csharp
// 带变量的本地化文本
new TextObject("{=TOKEN_ID}默认文本")
    .SetTextVariable("COUNT", count)
    .SetTextVariable("TOTAL", total)
    .SetTextVariable("GOLD", gold);

// 嵌入 Hero 链接（点击可打开百科）
new TextObject("{=...}{HERO.LINK}委托你...")
    .SetCharacterProperties("HERO", hero.CharacterObject);

// 嵌入 Settlement 链接
new TextObject("{=...}前往{SETTLEMENT}")
    .SetTextVariable("SETTLEMENT", settlement.Name);
```

## 附录 B：MBRandom 辅助方法

```csharp
// 加权随机
MBRandom.ChooseWeighted<T>(IList<T> list, IList<float> weights);

// 从列表中随机取元素
list.GetRandomElementWithPredicate(x => condition(x));

// 随机浮点数
MBRandom.RandomFloat;           // [0, 1)
MBRandom.RandomFloatRanged(min, max);

// 随机整数
MBRandom.RandomInt(min, max);   // [min, max]
```

## 附录 C：SaveableField 存档

```csharp
// 所有需要跨存档持久化的字段必须标记
[SaveableField(uniqueId)]
private int _myField;
// uniqueId 必须在 Issue/Quest 内唯一，通常用 10, 20, 30, ... 步进

// 属性也可以用
[SaveableProperty(uniqueId)]
public int MyProperty { get; private set; }

// 注意：JournalLog 等引擎对象也需要 [SaveableField] 才能正确恢复
```
**DLL**: CampaignSystem

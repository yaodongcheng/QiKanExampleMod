# 骑砍2 原版 Quest 源码级案例分析

> **📁 更高规格的完整分析见：[vanilla_quests/](vanilla_quests/README.md)** — 40+ 任务全目录、可复用模式提取、完整 API 参考。
> 基于 `ilspycmd` 反编译 `TaleWorlds.CampaignSystem.dll` + `SandBox.dll` 的源码级分析。
> 每个案例覆盖完整调用链路：触发 → Issue → Quest → 事件驱动进度 → 结算。

---

## 目录

1. [通用架构速览](#一通用架构速览)
2. [案例1: MerchantNeedsHelpWithOutlaws — 清剿匪徒（讨伐类）](#二案例1-merchantneedshelpwithoutlaws)
3. [案例2: NotableWantsDaughterFound — 寻找女儿（寻人/说服类）](#三案例2-notablewantsdaughterfound)
4. [案例3: FamilyFeud — 家族世仇（帮派/道德抉择/Lord Solution）](#四案例3-familyfeud)
5. [案例4: RevenueFarming — 包税权（经济/征税类）](#五案例4-revenuefarming)
6. [案例5: EscortMerchantCaravan — 护送商队（护送类）](#六案例5-escortmerchantcaravan)
7. [五种案例横向对比](#七五种案例横向对比)
8. [扩展到自定义 Issue/Quest 的设计模板](#八扩展到自定义-issuequest-的设计模板)

---

## 一、通用架构速览

### 1.1 三层结构

每个原版任务由**三个类**组成，全部定义在**同一个 `CampaignBehaviorBase` 文件**中：

```
XxxIssueQuestBehavior : CampaignBehaviorBase     ← 触发调度 + 注册
├── [嵌套类] XxxIssue : IssueBase                ← 世界中的"问题"
└── [嵌套类] XxxIssueQuest : QuestBase           ← 玩家接取后的"任务"
```

### 1.2 Issue 基类核心契约

```csharp
public abstract class IssueBase : MBObjectBase
{
    // ═══ 必须实现的抽象成员 ═══
    abstract TextObject IssueBriefByIssueGiver;             // NPC开场："我有个麻烦..."
    abstract TextObject IssueQuestSolutionExplanationByIssueGiver; // NPC解释任务
    abstract TextObject IssueQuestSolutionAcceptByPlayer;   // 玩家接受台词
    abstract TextObject Title;                              // 任务标题
    abstract TextObject Description;                        // 任务简述
    abstract QuestBase GenerateIssueQuest(string questId);  // 工厂方法
    abstract IssueFrequency GetFrequency();                 // VeryCommon/Common/Rare
    abstract bool CanPlayerTakeQuestConditions(...);        // 前置条件
    abstract bool IssueStayAliveConditions();               // Issue是否仍然有效
    abstract void HourlyTick();

    // ═══ 可选重写的关键成员 ═══
    virtual int RewardGold => ...;                          // 金币奖励公式
    virtual bool IsThereAlternativeSolution => false;       // 是否支持派人代办
    virtual bool IsThereLordSolution => false;              // 是否支持领主解决
    virtual int AlternativeSolutionBaseNeededMenCount => ...;
    virtual int AlternativeSolutionBaseDurationInDaysInternal => ...;
    virtual float GetIssueEffectAmountInternal(IssueEffect); // 未解决时的负面效果
}
```

### 1.3 Quest 基类核心契约

```csharp
public abstract class QuestBase : MBObjectBase
{
    // ═══ 必须实现的抽象成员 ═══
    abstract void SetDialogs();         // 注册 OfferDialogFlow / DiscussDialogFlow
    abstract void RegisterEvents();     // ★ 核心：订阅 Campaign 事件
    abstract void OnStartQuest();       // 任务激活回调
    abstract void HourlyTick();

    // ═══ 关键基础设施 ═══
    JournalLog AddDiscreteLog(TextObject, TextObject, int current, int target); // 进度条日志
    void AddLog(TextObject);            // 普通日志
    void AddTrackedObject(object);      // 地图追踪标记

    // ═══ 五种完成方式 ═══
    void CompleteQuestWithSuccess();
    void CompleteQuestWithCancel(TextObject);
    void CompleteQuestWithFail(TextObject);
    void CompleteQuestWithTimeOut(TextObject);
    void CompleteQuestWithBetrayal(TextObject);
}
```

### 1.4 通用调用链

```
IssuesCampaignBehavior 每日 Tick
  → CampaignEventDispatcher.OnCheckForIssue(hero)
    → XxxIssueQuestBehavior.OnCheckForIssue(hero)         ← 检查NPC类型/冷却/前置条件
      → IssueManager.AddPotentialIssueData(hero, factory)
        → IssueManager.CreateNewIssue(pid, hero)           ← NPC头顶出现 "!"

玩家进入定居点 → 与NPC对话
  → ConversationManager 检测 issue_offer token
  → 显示 "Is there anything I can do for you?"
  → 玩家接取 → IssueManager.StartIssueQuest(hero)
    → Issue.StartIssueWithQuest()
      → GenerateIssueQuest(questId)                       ← 工厂方法创建 Quest
      → 构造函数 → SetDialogs()                           ← 注册对话流（含 QuestAcceptedConsequences 委托）
  → 对话系统回调 QuestAcceptedConsequences()
    → quest.StartQuest()
      → OnStartQuest() + RegisterEvents()               ← 事件订阅激活

游戏运行中（事件驱动）:
  → 各事件回调 (MapEventEnded / SettlementEntered / MobilePartyDestroyed / ...)
    → 更新进度 → AddQuestStepLog()
      → 达到目标 → SuccessConsequences()
        → CompleteQuestWithSuccess()
```

### 1.5 事件驱动进度更新机制

Quest 运行期间**不靠轮询**，而是靠发布-订阅（pub/sub）模式被动驱动。每个 Quest 在 `StartQuest()` 时独立向全局 `CampaignEvents` 注册监听器，事件触发时全体广播，各 Quest 自行过滤。

**架构**：

```
                               CampaignEventDispatcher
                               (全局单例，事件总线)
                              ┌───────────────────────┐
  DestroyPartyAction.Apply()──│→ OnMobilePartyDestroyed│
  MapEvent.End() ─────────────│→ OnMapEventEnded       │
  Player enters settlement ───│→ OnSettlementEntered   │
  ...                         │   ...                  │
                              └───┬───────┬───────┬───┘
                                  │       │       │
                          ┌───────┘       │       └───────┐
                          ▼               ▼               ▼
                    Quest A 回调    Quest B 回调    Quest C 回调
                    (护送商队)      (清剿匪徒)      (包税权)
                         │               │               │
                    if (不是我的     if (不在       if (不关心
                      商队) return;   _validList)     此事件) return;
                                       return;
```

**以 `MobilePartyDestroyed` 为例的完整触发链**：

```
【触发源】任何人消灭一支部队
  → DestroyPartyAction.ApplyInternal(destroyerParty, destroyedParty)
    → destroyedParty.RemoveParty()                       ← 从世界移除
    → CampaignEventDispatcher.Instance.OnMobilePartyDestroyed(...)
                                                        ← 广播给所有订阅者

【分发】CampaignEventDispatcher
  → for each registered listener:
      listener.OnMobilePartyDestroyed(mobileParty, destroyerParty)

【过滤】各 Quest 自行判断相关性
  → Outlaws Quest:
      if (destroyerParty != MainParty) return;           // 非玩家击杀，不管
      if (!_validPartiesList.Contains(party)) return;    // 不是目标匪徒，不管
      _destroyedPartyCount++;                            // ★ 命中，推进进度
      AddQuestStepLog();
```

**`DestroyPartyAction` 的三个公开入口**（无论什么原因消灭部队，最终都汇入同一个广播点）：

| 入口 | 触发场景 |
|------|---------|
| `DestroyPartyAction.Apply(destroyer, destroyedParty)` | 大地图战斗一方被消灭 |
| `DestroyPartyAction.ApplyForDisbanding(party, settlement)` | 部队解散 |
| 领袖死亡 → `DisableHeroAction` → `DestroyPartyAction.Apply(null, party)` | 英雄死亡 |

**过滤是关键**：每个 Quest 回调的第一件事就是判断"这跟我有没有关系"。O(1) 判断（`Contains` / 字段比较），不相关的立即 return。全局广播 + 本地过滤，省去了事件路由层。

**注销**：Quest 完成/失败/取消时，`QuestBase.FinalizeQuest()` 清理事件订阅，不再接收广播。

---

## 二、案例1: MerchantNeedsHelpWithOutlaws

### 2.1 任务概述

| 属性 | 值 |
|------|-----|
| **类型** | 讨伐/清剿类 |
| **发布者** | 城镇 Merchant / Notable |
| **目标** | 消灭 N 队匪徒（N = 2 + 6 × 难度系数） |
| **时限** | 20 天（Quest）/ 15 天（Issue） |
| **Alternative** | 支持（需 Tactics/Scouting 120, T2+ 士兵） |
| **Lord Solution** | 不支持 |
| **所属 DLL** | `TaleWorlds.CampaignSystem.dll` |

### 2.2 Issue 关键设计

```csharp
// 自定义存档字段：关联的藏身处
[SaveableField(10)] private Hideout RelatedHideout;

// 难度驱动的目标数量
private int TotalPartyCount => (int)(2f + 6f * base.IssueDifficultyMultiplier);

// 难度驱动的奖励
protected override int RewardGold => (int)(400f + 1500f * base.IssueDifficultyMultiplier);

// Issue 效果：未解决 → 定居点繁荣 -0.2, 领主权力 -0.1, 治安 -1
protected override float GetIssueEffectAmountInternal(IssueEffect issueEffect)
{
    if (issueEffect == DefaultIssueEffects.SettlementProsperity) return -0.2f;
    if (issueEffect == DefaultIssueEffects.IssueOwnerPower) return -0.1f;
    if (issueEffect == DefaultIssueEffects.SettlementSecurity) return -1f;
    return 0f;
}

// 构造函数：把关联藏身处标记为"已被占用"（防止其他任务冲突）
public MerchantNeedsHelpWithOutlawsIssue(Hero issueOwner, Hideout relatedHideout)
    : base(issueOwner, CampaignTime.DaysFromNow(15f))
{
    RelatedHideout = relatedHideout;
    Campaign.Current.BusyHideouts.Add(relatedHideout.Settlement); // ★ 关键：资源互斥
}
```

### 2.3 Quest 关键设计

**存档字段（全部 `[SaveableField]`）**：
```csharp
int _totalPartyCount;              // 需要消灭的匪徒总数
int _destroyedPartyCount;          // 已消灭数
int _recruitedPartyCount;          // 已招募数（Roguery 技能可招募匪徒）
List<MobileParty> _validPartiesList; // 有效的目标匪徒列表
Hideout _relatedHideout;
JournalLog _questProgressLogTest;  // 进度条日志引用
```

**事件订阅（`RegisterEvents`）** — 订阅了 9 个事件：
```csharp
protected override void RegisterEvents()
{
    CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, HourlyTickParty);
    CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
    CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
    CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
    CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageRaided);
    CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
    CampaignEvents.BanditPartyRecruited.AddNonSerializedListener(this, OnBanditPartyRecruited);
    CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
    CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
}
```

**核心玩法逻辑 — `HourlyTickParty`**：
```csharp
// 每小时扫描：发现委托人定居点附近的匪徒 → 加入目标列表 → 地图追踪
// 距离判定：mobileParty.Position2D.DistanceSquared(questGiver.CurrentSettlement.Position2D) <= 1600f
// AI 操控：将匪徒 patrol 在定居点周围，阻止其离开
private void HourlyTickParty(MobileParty mobileParty)
{

    //当一支部队正在大地图上参与战斗时，MapEvent 不为 null。MapEvent 对象在战斗发起时创建并赋给双方所有参战方。从 PlayerEncounter.StartBattleInternal() 可以看到，任何战斗场景——野战、劫掠、攻城、藏身处战——都会创建对应的 MapEvent：
    //mobileParty.IsCurrentlyUsedByAQuest  它只是一个 bool 标记，任何 Quest 都可以调用。原版里到处都在用：

    if (!base.IsOngoing || !mobileParty.IsBandit || mobileParty.MapEvent != null
        || !mobileParty.MapFaction.IsBanditFaction || mobileParty.IsCurrentlyUsedByAQuest)


        return;

    if (mobileParty.Position2D.DistanceSquared(base.QuestGiver.CurrentSettlement.Position2D) <= 1600f)
    {
        if (!_validPartiesList.Contains(mobileParty))
        {
            if (!IsTracked(mobileParty)) AddTrackedObject(mobileParty);
            _validPartiesList.Add(mobileParty);
            // ★ 将匪徒锁定在定居点附近
            //修改匪徒行为是围绕定居点巡逻
            SetPartyAiAction.GetActionForPatrollingAroundSettlement(mobileParty, ...);
            mobileParty.Ai.SetDoNotMakeNewDecisions(true);
        }
    }
}
```

**核心玩法逻辑 — `MobilePartyDestroyed`**：
```csharp
// 玩家消灭了目标列表中的匪徒 → 进度 +1
private void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
{
    if (destroyerParty == PartyBase.MainParty && _validPartiesList.Contains(mobileParty))
    {
        _destroyedPartyCount++;
        AddQuestStepLog();
    }
}
```

**进度更新 — `AddQuestStepLog`**：
```csharp
private void AddQuestStepLog()
{
    _questProgressLogTest.UpdateCurrentProgress(_questPartyProgress); // = _destroyed + _recruited
    if (_questPartyProgress >= _totalPartyCount)
    {
        SuccessConsequences();   // ← 触发结算
        return;
    }
    // 快速信息提示："你已消灭 X/Y 队匪徒"
    MBInformationManager.AddQuickInformation(textObject);
}
```

**结算 — `SuccessConsequences`**：
```csharp
private void SuccessConsequences()
{
    // ★ 根据完成方式选择不同的结算文本
    if (_destroyedPartyCount == _totalPartyCount)
        AddLog(_successQuestLogText1);  // 全灭
    else if (_recruitedPartyCount != 0 && _recruitedPartyCount < _totalPartyCount)
        AddLog(_successQuestLogText2);  // 部分招募
    else
        AddLog(_successQuestLogText3);  // 全部招募

    RelationshipChangeWithQuestGiver = 3;
    GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, RewardGold);
    // 定居点增益
    base.QuestGiver.CurrentSettlement.Town.Security += 5f;
    base.QuestGiver.CurrentSettlement.Town.Prosperity += 5f;
    CompleteQuestWithSuccess();
}
```

### 2.4 完整调用链

```
【触发】
IssuesCampaignBehavior.OnSettlementTick()
  → OnCheckForIssue(hero)  ← hero = 有 Infested Hideout 附近的 Notable
  → 条件: Notable? 附近有Hideout? 冷却已过? 定居点Security<80?
  → IssueManager.AddPotentialIssueData(hero,
        factory: (pid, h) => new MerchantNeedsHelpWithOutlawsIssue(h, nearestHideout))
  → IssueManager.CreateNewIssue() → NPC 头顶 "!"

【接取】
玩家对话 → IssueManager.StartIssueQuest(hero)
  → GenerateIssueQuest(questId)
    → new MerchantNeedsHelpWithOutlawsIssueQuest(
        questId, giver, 20天, reward, totalPartyCount, relatedHideout)
    → 构造函数: 初始化 _validPartiesList + AddHideoutPartiesToValidPartiesList()
    → SetDialogs() + InitializeQuestOnCreation()
  → 对话系统回调 QuestAcceptedConsequences()
    → StartQuest() → OnStartQuest() + RegisterEvents() 订阅9个事件
    → AddDiscreteLog(任务开始文本, "消灭匪徒", 0, totalPartyCount)

【执行 — 事件驱动】
HourlyTickParty()        ← 每小时发现新匪徒 → 加入列表 → 锁定在定居点周围
MobilePartyDestroyed()   ← 玩家消灭匪徒 → _destroyedPartyCount++ → AddQuestStepLog()
BanditPartyRecruited()   ← Roguery招募匪徒 → _recruitedPartyCount++ → AddQuestStepLog()
WarDeclared / VillageRaided / ClanChangedKingdom → CompleteQuestWithCancel()

【结算 — 三种文本】
_destroyedPartyCount == total → "已消灭全部匪徒"
部分招募                    → "消灭部分+招募部分"
全部招募                    → "全部招募入队"

→ RelationshipChangeWithQuestGiver = 3
→ GiveGoldAction(questGiver, player, rewardGold)
→ 定居点 Security+5, Prosperity+5
→ CompleteQuestWithSuccess() → IssueFinalized()
```

### 2.5 设计要点

1. **动态目标发现**：`HourlyTickParty` 不是一次性生成的固定列表，而是每小时扫描附近匪徒加入列表。这让任务目标数量可以动态扩大。
2. **多种完成路径**：消灭（_destroyedPartyCount）和招募（_recruitedPartyCount）两种方式都能推进，不同路径有不同结算文本。
3. **资源互斥**：`BusyHideouts.Add()` 防止其他任务（NearbyBanditBase 等）同时使用同一个藏身处。
4. **匪徒 AI 操控**：通过 `SetPartyAiAction.GetActionForPatrollingAroundSettlement` + `SetDoNotMakeNewDecisions(true)` 将匪徒锁定在任务区域内。

---

## 三、案例2: NotableWantsDaughterFound

### 3.1 任务概述

| 属性 | 值 |
|------|-----|
| **类型** | 寻人/说服/战斗混合类 |
| **发布者** | 村庄 RuralNotable |
| **目标** | 寻找被拐走的女儿 → 击败恶棍 → 说服女儿回家 |
| **时限** | 19 天（Quest）/ 30 天（Issue） |
| **Alternative** | 支持（需 Charm/Scouting 120, T2+ 士兵） |
| **Lord Solution** | 不支持 |
| **所属 DLL** | `SandBox.dll` |
| **独特机制** | 侦察技能检定、Persuasion 说服系统、多结局 |

### 3.2 Issue 关键设计

```csharp
// 奖励公式
protected override int RewardGold => 500 + MathF.Round(1200f * IssueDifficultyMultiplier);

// 构造函数：只有时限，无额外自定义数据
public NotableWantsDaughterFoundIssue(Hero issueOwner)
    : base(issueOwner, CampaignTime.DaysFromNow(30f)) { }

// 前置条件
protected override bool CanPlayerTakeQuestConditions(...)
{
    // 关系 >= -10 且未交战
    bool flag2 = issueGiver.GetRelationWithPlayer() >= -10f
        && !issueGiver.CurrentSettlement.MapFaction.IsAtWarWith(Hero.MainHero.MapFaction);
    flag = flag2 ? PreconditionFlags.None
                 : (issueGiver.MapFaction.IsAtWarWith(...) ? AtWar : Relation);
}
```

### 3.3 Quest 关键设计

**存档字段**（比 MerchantNeedsHelpWithOutlaws 多得多）：
```csharp
Hero _daughterHero;                   // 女儿（动态创建的临时 Hero）
Hero _rogueHero;                      // 恶棍（动态创建的临时 Hero）
Agent _daughterAgent;                 // Mission 中的女儿 Agent
Agent _rogueAgent;                    // Mission 中的恶棍 Agent
bool _isQuestTargetMission;           // 是否进入任务专属 Mission
bool _didPlayerBeatRouge;             // 是否击败了恶棍
bool _exitedQuestSettlementForTheFirstTime;
bool _isTrackerLogAdded;              // 是否已添加追踪日志
bool _isDaughterPersuaded;            // 是否说服成功
bool _isDaughterCaptured;             // 女儿是否被抓
bool _acceptedDaughtersEscape;        // 是否接受了女儿逃跑
Village _targetVillage;               // 目标村庄（恶棍躲藏地）
bool _villageIsRaidedTalkWithDaughter; // 村庄被劫时的对话变化
Dictionary<Village, bool> _villagesAndAlreadyVisitedBooleans; // ★ 线索收集系统
Dictionary<string, CharacterObject> _rogueCharacterBasedOnCulture; // 按文化选恶棍模板
bool _playerDefeatedByRogue;
PersuasionTask _task;                 // ★ 说服任务对象
float _questDifficultyMultiplier;     // 难度系数
```

**构造函数 — 动态创建 NPC**：
```csharp
public NotableWantsDaughterFoundIssueQuest(...)
{
    _questDifficultyMultiplier = issueDifficultyMultiplier;
    // 1. 选一个目标村庄（委托人的 BoundVillages 中随机一个）
    _targetVillage = questGiver.CurrentSettlement.Village.Bound.BoundVillages
        .GetRandomElementWithPredicate(x => x != questGiver.CurrentSettlement.Village);

    // 2. 按 culture 建立恶棍模板字典
    _rogueCharacterBasedOnCulture["khuzait"] = steppe_bandits.Culture.BanditBoss;
    _rogueCharacterBasedOnCulture["vlandia"] = mountain_bandits.Culture.BanditBoss;
    // ... 6种 culture

    // 3. ★ 动态创建女儿 Hero（18~25岁随机年龄）
    _daughterHero = HeroCreator.CreateSpecialHero(
        notableTemplate, questGiver.HomeSettlement, ...);
    _daughterHero.CharacterObject.HiddenInEncylopedia = true;
    _daughterHero.Father = questGiver;  // ★ 设定父女关系

    // 4. ★ 动态创建恶棍 Hero
    _rogueHero = HeroCreator.CreateSpecialHero(
        GetRogueCharacterBasedOnCulture(questGiver.Culture), ...);
}
```

**事件订阅**：
```csharp
// (反编译代码中 RegisterEvents 通过基类 InitializeQuestOnCreation 自动调用)
// 关键事件包括：
// - SettlementEntered / OnSettlementLeft
// - 多个自定义 DialogFlow（恶棍对话/女儿对话/说服后对话/村庄被劫对话等）
```

**对话系统 — 多分支**：
```csharp
protected override void SetDialogs()
{
    // 1. 标准 Offer + Discuss Dialogs
    OfferDialogFlow = DialogFlow.CreateDialogFlow("issue_classic_quest_start")
        .NpcLine("谢谢你的帮助...请找到我女儿").Condition(未击败恶棍)
        .Consequence(QuestAcceptedConsequences).CloseDialog();

    // 2. ★ 额外的 6 个自定义 DialogFlow（通过 Campaign.Current.ConversationManager.AddDialogFlow 注册）
    Campaign.Current.ConversationManager.AddDialogFlow(GetRougeDialogFlow());
    Campaign.Current.ConversationManager.AddDialogFlow(GetDaughterAfterFightDialog());
    Campaign.Current.ConversationManager.AddDialogFlow(GetDaughterAfterAcceptDialog());
    Campaign.Current.ConversationManager.AddDialogFlow(GetDaughterAfterPersuadedDialog());
    Campaign.Current.ConversationManager.AddDialogFlow(GetDaughterDialogWhenVillageRaid());
    Campaign.Current.ConversationManager.AddDialogFlow(GetRougeAfterAcceptDialog());
    Campaign.Current.ConversationManager.AddDialogFlow(GetRogueAfterPersuadedDialog());
}
```

### 3.4 完整调用链

```
【触发】
NotableWantsDaughterFoundIssueBehavior.OnCheckForIssue(hero)
  → 条件: RuralNotable? 关系>=-10? 村庄>=3个BoundVillages? Notable的慈悲/慷慨特质<0?
  → IssueManager.AddPotentialIssueData(hero,
        factory: (pid, h) => new NotableWantsDaughterFoundIssue(h))

【接取】
GenerateIssueQuest(questId)
  → new NotableWantsDaughterFoundIssueQuest(...)
    → 构造函数: 动态创建 _daughterHero + _rogueHero（HeroCreator.CreateSpecialHero）
    → SetDialogs() 注册 8 个 DialogFlow

【执行 — 多阶段流程】
阶段 A: 找线索
  - 玩家离开委托人定居点
  - 两个路径:
    路径1: Scout 检定（DoesMainPartyHasEnoughScoutingSkill >= 150 × difficulty）
      → 显示追踪日志，直接知道目标村庄
    路径2: 逐个访问附近村庄
      → _villagesAndAlreadyVisitedBooleans 记录访问状态
      → 每个村庄有机会发现线索

阶段 B: 三方对话（恶棍 + 女儿 + 玩家）
  - 到达目标村庄 → 进入 Mission，恶棍和女儿以 LocationCharacter 形式放置
  - 三方靠近时自动触发多角色对话（`multi_character_conversation_on_condition`）
  - 恶棍开场: "你是谁？{委托人}派来的赏金猎人？听着，我们没做错什么，这女人和我相爱，我没强迫她。"
  - 女儿接话: "他说得对！我是自愿跟他走的。我爱我爹/娘，但他/她太专横了。如果你相信自由和爱情，请放过我们。"
  - ★ 玩家四选一:
    【选 1: 放他们私奔】"我理解。既然如此，我放你们走。"
      → Rogue: "谢谢你的善解人意。走，亲爱的，趁别的猎犬还没嗅到我们的气味..."
      → _acceptedDaughtersEscape = true
      → Quest COMPLETES WITH FAIL（任务失败！）
      → 后果: 关系 -10, 委托人定居点 Security -5, Prosperity -5
      → ★ 这是隐藏的道德抉择——良心 vs 契约，你成全了恋人但失信于委托人
    【选 2: 质疑强迫】"我怎么知道你不是在强迫她？"
      → 女儿求情: "求你了，我真的爱他，请别挡我们的路。"
      → 玩家说: "我答应了你爹/娘要带你回去。"
      → 女儿揭露: "他/她才不是伤心我走了！他/她把我许给了盟友的儿子，这才是他/她怕的。"
      → 进入 Persuasion 检定（难度 5）
        → 成功: _isDaughterPersuaded = true → 女儿自愿回家 → 任务成功
        → 失败: 回到三选一（杀恶棍 or 放私奔）
    【选 3: 答应过委托人】"但我答应了你爹/娘要带你回去。"
      → 同选 2，转到 Persuasion
    【选 4: 杀恶棍】"我想唯一的办法就是杀了这个小白脸。"
      → Rogue 挑战决斗
      → 子选项 A: "这将是一场屠杀，但我无所谓。" → 玩家+同伴群殴 Rogue
      → 子选项 B: "我接受你的决斗。" → 1v1 公平决斗
      → 赢 → _didPlayerBeatRouge = true, _isDaughterCaptured = true → 任务成功
      → 输 → _playerDefeatedByRogue = true → 任务失败

阶段 C: Mission 结束结算
  - OnMissionEnded 中检查四个 flag:
    _isDaughterPersuaded → 成功
    _acceptedDaughtersEscape → ★ 失败（但你选择了善良）
    _isDaughterCaptured → 成功（武力抓回）
    _playerDefeatedByRogue → 失败

阶段 D: 特殊场景
  - 村庄被劫掠 (_villageIsRaidedTalkWithDaughter) → 对话变化（"你父亲派我来找你的""哦感谢上帝！我看见可怕的事情了..."）
  - 对话后直接 ApplySuccessConsequences → 成功
  - 女儿被抓 (_isDaughterCaptured) → 对话变化

【结算】
成功:
  → ApplySuccessRewards()  // GainRenown +2, IssueOwner +10Power, 关系+10, 定居点Security+10
  → CompleteQuestWithSuccess()

失败（被恶棍击败）:
  → AddLog(_playerDefeatedByRogueLogText)
  → CompleteQuestWithFail()

超时 / 战争 / 村庄被毁:
  → CompleteQuestWithCancel(对应文本)
```

### 3.5 设计要点

1. **动态 NPC 创建**：`HeroCreator.CreateSpecialHero` 创建临时 Hero，不污染世界 NPC 池，`HiddenInEncylopedia = true` 防止玩家看到。
2. **线索收集系统**：`_villagesAndAlreadyVisitedBooleans` 是一个简单的状态机——每个村庄是否已访问，未访问的村庄可能藏着线索。
3. **技能检定作为可选路径**：高 Scout 技能可以跳过逐个村庄搜索的直接发现恶棍藏身处。
4. **Persuasion 接管**：用 `PersuasionTask` 对象接管对话引擎，而不是硬编码对话选项。
5. **Mission 内 Agent 操控**：虽然不是大地图事件，但通过创建特殊 Mission 并在其中放置 Agent 来实现巷战。
6 几个可复用的表现力接口：AddTrackedObject MBInformationManager.AddQuickInformation  QuestHelper.AddMapArrowFromPointToTarget

---

## 四、案例3: FamilyFeud

### 4.1 任务概述

| 属性 | 值 |
|------|-----|
| **类型** | 帮派/道德抉择/护送混合类 |
| **发布者** | 村庄 RuralNotable（地主） |
| **目标** | 保护犯了杀人罪的年轻族人免受对方家族复仇 |
| **时限** | 20 天（Quest）/ 30 天（Issue） |
| **Alternative** | 支持（需 Athletics/Charm 120, T2+ 士兵） |
| **Lord Solution** | ★ 支持！（原版 40 种任务中唯一有 Lord Solution 的） |
| **所属 DLL** | `SandBox.dll` |
| **独特机制** | CounterOffer、背叛、Mission 巷战、Persuasion |

### 4.2 Issue 关键设计

```csharp
// 自定义字段
[SaveableField(10)] private Settlement _targetVillage;     // 复仇方所在村庄
[SaveableField(20)] private Hero _targetNotable;           // 复仇方领袖

// ★ Lord Solution 的 CounterOffer Hero
[SaveableProperty(30)] public override Hero CounterOfferHero { get; protected set; }

// 影响力消耗
public override int NeededInfluenceForLordSolution => 20;

// 三种解决路径都支持
public override bool IsThereAlternativeSolution => true;
public override bool IsThereLordSolution => true;  // ★ 唯一！

// Lord Solution 条件：玩家必须是定居点领主
public override bool LordSolutionCondition(out TextObject explanation)
{
    if (IssueOwner.CurrentSettlement.OwnerClan == Clan.PlayerClan)
    { explanation = TextObject.Empty; return true; }
    explanation = new TextObject("你需要是这个定居点的主人！");
    return false;
}

// ★ 设置 CounterOfferHero（AfterIssueCreation 钩子）
protected override void AfterIssueCreation()
{
    CounterOfferHero = IssueOwner.CurrentSettlement.Notables
        .FirstOrDefault(x => x.CharacterObject.IsHero
            && x.CharacterObject.HeroObject != IssueOwner);
}
```

### 4.3 Lord Solution 的 CounterOffer 机制

这是原版任务系统中最复杂的道德抉择设计之一：

```
玩家选择 Lord Solution
  → Issue.StartIssueWithLordSolution()
  → 状态变为 SolvingWithLordSolution
  → 监听 BeforeGameMenuOpenedEvent
  → 玩家下次进入大地图时自动触发 CounterOfferHero 对话

CounterOfferHero 说：
  "{TARGET_NOTABLE}的侄子杀了我的族人！血债必须血偿！请大人允许我们复仇。"

玩家二选一:
  ├─ 接受 CounterOffer:
  │   → LordSolutionConsequenceWithAcceptCounterOffer()
  │   → 背叛原 NPC (Honor -50)
  │   → 关系: 原NPC -10, CounterOfferHero +5
  │   → 定居点 Prosperity -5, Security -5
  │
  └─ 拒绝 CounterOffer:
      → LordSolutionConsequenceWithRefuseCounterOffer()
      → 调用 ApplySuccessRewards()（同 Quest Solution）
      → 消耗影响力，获得奖励，关系+10
```

### 4.4 Quest 关键设计

**存档字段**：
```csharp
Settlement _targetSettlement;        // 目标村庄
Hero _targetNotable;                 // 复仇方领袖
Hero _culprit;                       // 犯人（动态创建）
bool _culpritJoinedPlayerParty;      // 犯人是否加入了玩家部队
bool _checkForMissionEvents;         // 是否在 Mission 中
int _rewardGold;
bool _isCulpritDiedInMissionFight;
bool _isPlayerKnockedOutMissionFight;
bool _isNotableKnockedDownInMissionFight;
bool _conversationAfterFightIsDone;
bool _persuationInDoneAndSuccessfull;
bool _playerBetrayedCulprit;         // ★ 玩家是否背叛了犯人
List<LocationCharacter> _notableThugs; // 复仇方的打手
PersuasionTask _task;                // 说服任务
```

**构造函数 — 动态创建犯人**：
```csharp
public FamilyFeudIssueQuest(...)
{
    // 创建犯人：按目标定居点 culture 生成 townsman
    _culprit = HeroCreator.CreateSpecialHero(
        MBObjectManager.Instance.GetObject<CharacterObject>(
            "townsman_" + targetSettlement.Culture.StringId),
        targetSettlement, null, null, -1);
    _culprit.SetNewOccupation(Occupation.Notable);   // 设为 RuralNotable
    // 给犯人装备一把匕首
    _culprit.CivilianEquipment.AddEquipmentToSlotWithoutAgent(
        EquipmentIndex.Weapon0, new EquipmentElement(pugio));
}
```

**对话系统 — 10 个独立 DialogFlow**：
```csharp
private void InitializeQuestDialogs()
{
    // 每个 DialogFlow 对应一个对话场景
    Campaign.Current.ConversationManager.AddDialogFlow(GetCulpritDialogFlow());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableThugDialogFlow());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableDialogFlowBeforeTalkingToCulprit());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableDialogFlowAfterTalkingToCulprit());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableDialogFlowAfterKillingCulprit());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableDialogFlowAfterPlayerBetrayCulprit());
    Campaign.Current.ConversationManager.AddDialogFlow(GetCulpritDialogFlowAfterCulpritJoin());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableDialogFlowAfterNotableKnockdown());
    Campaign.Current.ConversationManager.AddDialogFlow(GetNotableDialogFlowAfterQuestEnd());
    Campaign.Current.ConversationManager.AddDialogFlow(GetCulpritDialogFlowAfterQuestEnd());
}
```

### 4.5 完整调用链

```
【触发】
FamilyFeudIssueBehavior.OnCheckForIssue(hero)
  → 条件: RuralNotable? 关系>=-10? 附近有另一个村庄有 Notable?
  → 选 targetNotable 和 targetVillage
  → IssueManager.AddPotentialIssueData(hero,
        factory: (pid, h) => new FamilyFeudIssue(h, targetNotable, targetVillage))
  → AfterIssueCreation() → 设置 CounterOfferHero

【三种解决方案】
Quest Solution:      玩家亲自保护犯人
Alternative Solution:派同伴+士兵保护（3+5×难度系数）
Lord Solution:       以领主身份命令对方接受血钱（消耗20影响力）
                       → CounterOffer → 背叛 or 拒绝二选一

【Quest 执行流程】
阶段 A: 找犯人
  → 在委托人定居点找到 _culprit
  → 对话 → 说服他跟你走
  → _culpritJoinedPlayerParty = true
  → _culprit 加入玩家部队

阶段 B: 三方对话 + 道德抉择（先选再打）
  → 带犯人去 targetVillage
  → 进入 Mission（alley_2 场景），三方 Agent 自动触发对话
  → Notable 开场: "就是他？他别想活着离开！"
  → 玩家三选一:
    【选 A: 死保犯人】"他受我保护，我们是来和平谈判的"
      → Notable: "你包庇杀人犯就跟杀人犯同罪！给我一起杀了！"
      → 进入战斗: 玩家 + 犯人 vs Notable + 打手（公平 2vN）
      → 结果:
        · 赢（Notable 被打倒）→ _isNotableKnockedDownInMissionFight = true → 成功
        · 输（犯人被杀）→ _isCulpritDiedInMissionFight = true → 失败
    【选 B: Persuasion 检定】"你这是违法"
      → 进入 Persuasion 对话（PersuasionDifficulty = 4）
      → 成功 → _persuationInDoneAndSuccessfull = true → 和平解决
      → 失败 → 回到三选一（只能选 A 或 C）
    【选 C: 出卖犯人】"你说的对，你可以伸张正义"
      → Notable: "就知道你是个通情达理的人"
      → 犯人惨叫: "什么？你要让我在这儿被杀？我族不会忘记今天的！"
      → _playerBetrayedCulprit = true
      → ★ 玩家被移入 SpectatorTeam（观众席），坐看犯人被 Notable+打手群殴致死
      → 犯人必死 → 进入背叛结算

阶段 C: 战后收尾对话（根据战斗结果触发对应 DialogFlow）
  → _isCulpritDiedInMissionFight && !_playerBetrayedCulprit
    → Notable: "行了，正义已伸张，我们没恩怨了"
  → _isCulpritDiedInMissionFight && _playerBetrayedCulprit
    → Notable: "就知道你是个通情达理的人"（但与委托人结仇）
  → FightEnded && !_persuationInDoneAndSuccessfull
    → Notable 认怂: "行了行了，不追究了，让我走"

【结算】
成功:
  → ApplySuccessRewards()
    → GainRenown +1, IssueOwner关系+10, targetNotable关系-5
    → 定居点 Security+10
  → CompleteQuestWithSuccess()

背叛:
  → CulpritDiedInNotableFightFail()
    → 关系: IssueOwner -10
  → CompleteQuestWithBetrayal()

犯人被杀:
  → AddLog(CulpritDiedQuestFail)
  → CompleteQuestWithFail()
```

### 4.6 设计要点

1. **CounterOffer 机制**：`CounterOfferHero` 属性 + `AfterIssueCreation` 钩子是 Lord Solution 的关键——不是所有任务都有这个。
2. **多 Agent Mission 场景**：在 alley_2 场景中放置多个 LocationCharacter（犯人、Notable、打手），Agent 之间可以战斗。
3. **Persuasion 接管 Mission 结果**：战斗不一定打死人——可以通过 PersuasionTask 和平解决。
4. **动态 Hero 创建**：和 NotableWantsDaughterFound 一样，用 `HeroCreator.CreateSpecialHero` 创建临时 NPC。
5. **`HiddenInEncylopedia`**：动态创建的 Hero 不显示在百科中，防止玩家困惑。
6 可复用接口 _culprit.CivilianEquipment.AddEquipmentToSlotWithoutAgent(
        EquipmentIndex.Weapon0, new EquipmentElement(pugio));
---

## 五、案例4: RevenueFarming

### 5.1 任务概述

| 属性 | 值 |
|------|-----|
| **类型** | 经济/征税类 |
| **发布者** | 城镇/城堡的领主（Lord） |
| **目标** | 走访领主的所有村庄收税，收集够指定数额后交给领主 |
| **时限** | 20 天 |
| **Alternative** | 不支持 |
| **Lord Solution** | 不支持 |
| **所属 DLL** | `TaleWorlds.CampaignSystem.dll` |
| **独特机制** | 每小时收税进度条（"挂机收税"）、村庄随机事件、背叛选项 |

### 5.2 Issue 关键设计

```csharp
[SaveableField(1)] private Settlement _targetSettlement;  // 目标城镇

// ★ 奖励不是固定的——玩家收集的钱减去交给领主的份额 = 玩家利润
protected override int RewardGold => 0;  // 基础奖励为0！盈利靠差价

// 领主索要的总金额 = 所有村庄 (Hearth × 4 ÷ 3)
protected int TotalRequestedDenars
{
    get
    {
        int num = 0;
        foreach (Village v in _targetSettlement.BoundVillages)
            if (!v.Settlement.IsRaided && !v.Settlement.IsUnderRaid)
                num += (int)(v.Hearth * 4f);
        return num / 3;
    }
}

// 前置条件：关系>=-10 + 未交战 + 至少40个健康士兵
protected override bool CanPlayerTakeQuestConditions(...)
{
    if (MobileParty.MainParty.MemberRoster.TotalHealthyCount < 40)
        flags |= PreconditionFlags.NotEnoughTroops;
}
```

### 5.3 Quest 关键设计

**辅助数据结构**：
```csharp
// 每个村庄的收税进度
public class RevenueVillage
{
    public readonly Village Village;
    public readonly int TargetAmount;     // 目标金额 = Hearth × 4
    public int CollectedAmount;           // 已收金额
    public int HourlyGain;               // 每小时收税速度 = TargetAmount / 10
    public bool EventOccurred;            // 是否触发了随机事件
    public bool IsRaided;
    private float _customProgress;        // 额外进度（事件奖励等）

    public float CollectProgress =>
        (CollectedAmount == 0 ? 0f : (float)CollectedAmount / TargetAmount) + _customProgress;
}

// 随机村庄事件
public class VillageEvent
{
    public readonly string Id;           // 事件对应的 GameMenu ID
    public readonly string MainEventText; // 事件文本
    public TextObject MainLog;
    public List<VillageEventOptionData> OptionConditionsAndConsequences; // 选项+后果
}
```

**Quest 存档字段**：
```csharp
int _totalRequestedDenars;                     // 领主索要的总金额
List<RevenueVillage> _revenueVillages;          // 所有需要收税的村庄
bool CollectingRevenues;                        // 是否正在收税
Dictionary<string, bool> _currentVillageEvents; // 事件触发标记
bool _allRevenuesAreCollected;                  // 所有村庄收税完成
JournalLog _questProgressLog;                   // 进度条
```

**核心玩法 — `HourlyTick` 挂机收税**：
```csharp
protected override void HourlyTick()
{
    if (base.IsOngoing)
    {
        // 检查是否所有村庄都完成了
        if (!_allRevenuesAreCollected && _revenueVillages.All(x => x.GetIsCompleted()))
            OnAllRevenuesAreCollected();

        // ★ 如果正在收税，持续推进
        if (CollectingRevenues)
            ProgressRevenueCollectionForVillage();
    }
}

// 每小时给玩家金币，推进收集进度
private void ProgressRevenueCollectionForVillage()
{
    RevenueVillage village = FindCurrentRevenueVillage();
    // 当进度达到 30% 时触发随机事件
    if (!village.EventOccurred && village.CollectProgress >= 0.3f)
    {
        // 从未触发的事件池中随机选一个
        var randomEvent = _currentVillageEvents
            .Where(x => !x.Value && behavior._villageEvents.Any(y => y.Id == x.Key))
            .GetRandomElementInefficiently();
        _currentVillageEvents[randomEvent.Key] = true;
        behavior.OnVillageEventWithIdSpawned(randomEvent.Key);
        GameMenu.SwitchToMenu(randomEvent.Key);  // ★ 切换到事件菜单
    }
    else
    {
        // 普通收税：每小时增加金币
        village.CollectedAmount += village.HourlyGain;
        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, village.HourlyGain);
        if (village.GetIsCompleted())
            SetVillageAsCompleted(village);
    }
}
```

**事件驱动**：
```csharp
protected override void RegisterEvents()
{
    CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
    CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
    CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageRaid);
    CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
    CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
}

// 村庄被劫 → 该村庄免收，总额减少，进度条自动 +1
private void OnVillageRaid(Village village)
{
    RevenueVillage rv = _revenueVillages.FirstOrDefault(x => x.Village.Id == village.Id);
    if (rv != null && !rv.IsRaided)
    {
        _totalRequestedDenars -= rv.TargetAmount / 3;
        rv.IsRaided = true;
        _questProgressLog.UpdateCurrentProgress(_questProgressLog.CurrentProgress + 1);
        // 如果所有村庄都被劫了 → 任务取消
        if (_revenueVillages.All(x => x.IsRaided))
            CompleteQuestWithCancel(...);
    }
}
```

**结算 — 道德抉择**：
```csharp
// ★ 超时时不直接失败，而是弹出选择框
protected override void OnBeforeTimedOut(ref bool completeWithSuccess, ref bool doNotResolveTheQuest)
{
    RelationshipChangeWithQuestGiver = -5;
    TraitLevelingHelper.OnIssueSolvedThroughQuest(QuestGiver,
        new Tuple<TraitObject, int>(DefaultTraits.Honor, -30));
    if (Hero.MainHero.Gold >= _totalRequestedDenars)
    {
        ShowQuestResolvePopUp();  // ← 弹出: "交出税收" or "私吞"
        doNotResolveTheQuest = true; // 暂停自动超时
    }
}

// 交出税收 → 成功（Honor +30）
private void QuestCompletedWithSuccess()
{
    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, _totalRequestedDenars);
    TraitLevelingHelper.OnIssueSolvedThroughQuest(QuestGiver,
        new Tuple<TraitObject, int>(DefaultTraits.Honor, 30));
    RelationshipChangeWithQuestGiver = 5;
    CompleteQuestWithSuccess();
}

// 私吞税收 → 背叛（Honor -100, CrimeRating +45, 关系 -15）
private void QuestCompletedWithBetray()
{
    ChangeCrimeRatingAction.Apply(QuestGiver.MapFaction, 45f);
    TraitLevelingHelper.OnIssueSolvedThroughQuest(QuestGiver,
        new Tuple<TraitObject, int>(DefaultTraits.Honor, -100));
    RelationshipChangeWithQuestGiver = -15;
    CompleteQuestWithBetrayal();
}
```

### 5.4 完整调用链

```
【触发】
RevenueFarmingIssueBehavior.OnCheckForIssue(hero)
  → 条件: hero是Lord? hero有封地(城镇/城堡)? hero有BoundVillages? 未交战?
  → IssueManager.AddPotentialIssueData(hero,
        factory: (pid, h) => new RevenueFarmingIssue(h, targetSettlement))

【接取】
GenerateIssueQuest(questId)
  → 遍历 BoundVillages → 为每个村庄创建 RevenueVillage
  → _totalRequestedDenars = 所有村庄 TargetAmount ÷ 3
  → new RevenueFarmingIssueQuest(...)

【执行】
A. 玩家进入村庄
   → GameMenu 检测 village_collect_revenue
   → CollectingRevenues = true
   → 开始 HourlyTick 挂机收税

B. 每小时:
   → village.CollectedAmount += HourlyGain
   → GiveGoldAction(null, player, HourlyGain)  // 金币实时到账
   → 进度到 30% → 触发随机 VillageEvent
     → GameMenu.SwitchToMenu("village_event_xxx")
     → 玩家做选择 → 事件后果（额外进度/损失/战斗）

C. 村庄被劫:
   → OnVillageRaid() → 兔收该村庄，总额减少

D. 所有村庄完成:
   → _allRevenuesAreCollected = true
   → 日志提示 "领主想要 X 第纳尔，你可以交给领主也可以私吞"

【结算】
找领主交付:
  → 手上有足够的钱 → "交出税收" / "私吞" 二选一
  → 交出: Honor+30, 关系+5, CompleteQuestWithSuccess()
  → 私吞: Honor-100, CrimeRating+45, 关系-15, CompleteQuestWithBetrayal()

超时:
  → OnBeforeTimedOut → ShowQuestResolvePopUp()
  → 玩家仍可选择交出/私吞
```

### 5.5 设计要点

1. **"挂机"收税**：不是立即完成——需要玩家实际待在村庄里，每小时推进进度。
2. **随机事件注入**：`_currentVillageEvents` + `VillageEvent` 提供了一个随机事件系统，30% 进度时触发。
3. **道德抉择有重大后果**：私吞 = Honor -100（游戏内最大的 Honor 惩罚之一）+ CrimeRating +45（可能导致宣战）。
4. **动态总额**：`_totalRequestedDenars` 会因村庄被劫而减少——不是固定数值。
5. **无 Alternative**：这个任务不能派人代办，因为核心玩法就是挂机等待。

---

## 六、案例5: EscortMerchantCaravan

### 6.1 任务概述

| 属性 | 值 |
|------|-----|
| **类型** | 护送/跟随类 |
| **发布者** | 城镇 Merchant |
| **目标** | 护送商队安全访问 N 个定居点（N 随机 3~10） |
| **时限** | 30 天 |
| **奖励** | 每日 250+1000×难度倍率（总上限 8000） |
| **Alternative** | 支持（需 Scouting/Riding 120, T2+ 士兵） |
| **Lord Solution** | 不支持 |
| **所属 DLL** | `TaleWorlds.CampaignSystem.dll` |
| **独特机制** | 动态生成商队部队、匪徒伏击生成、共享冷却 |

### 6.2 Issue 关键设计

```csharp
// 每日报酬（不是固定总额！）
protected int DailyQuestRewardGold => 250 + MathF.Ceiling(1000f * IssueDifficultyMultiplier);

// 总报酬 = 每日 × 随机天数（3~10天），上限 8000
protected override int RewardGold =>
    Math.Min(DailyQuestRewardGold * _companionRewardRandom, 8000);

// 冷却共享
// 此任务 + SnareTheWealthy + CaravanAmbush 共享冷却
// （通过 IssuesCampaignBehavior 中的 IssueCoolDownData 管理）

// 条件检查
public override bool IssueStayAliveConditions()
{
    // 商人拥有的商队 < 2 且定居点治安 < 80 时任务才存在
    if (IssueOwner.OwnedCaravans.Count < 2)
        return IssueOwner.CurrentSettlement.Town.Security <= 80f;
    return false;
}
```

### 6.3 Quest 关键设计

**存档字段**：
```csharp
int _requiredSettlementNumber;           // 需要访问的定居点数量
List<Settlement> _visitedSettlements;     // 已访问的定居点
MobileParty _questCaravanMobileParty;     // ★ 商队部队
MobileParty _questBanditMobileParty;      // ★ 匪徒伏击部队
float _difficultyMultiplier;
bool _isPlayerNotifiedForDanger;          // 是否已警告玩家危险临近
MobileParty _otherBanditParty;            // 第二队匪徒
int _questBanditPartyFollowDuration;      // 匪徒跟踪持续天数
int _otherBanditPartyFollowDuration;
int _daysSpentForEscorting = 1;           // 已护送天数
int _caravanWaitedInSettlementForHours;   // 商队在定居点等待的时间
bool _questBanditPartyAlreadyAttacked;    // 匪徒是否已攻击过
```

**核心玩法 — 匪徒伏击生成**：
```csharp
// (反编译代码中，EscortMerchantCaravanIssueQuest 的 RegisterEvents
//  订阅了 SettlementEntered、HourlyTickParty 等事件)

// 匪徒生成逻辑：
// - 在商队周围 80 距离内 spawn
// - 匪徒数量 = min(40, (玩家兵力 + 商队兵力) × 0.7)
// - 匪徒会跟踪商队 _questBanditPartyFollowDuration 天
// - 如果玩家在一定时间内没消灭匪徒 → 匪徒主动攻击商队
```

### 6.4 完整调用链

```
【触发】
EscortMerchantCaravanIssueQuestBehavior.OnCheckForIssue(hero)
  → 条件: Merchant? 关系>=-10? 未交战? 玩家兵力>=20? 定居点Security<=80?
  → IssueManager.AddPotentialIssueData(hero,
        factory: (pid, h) => new EscortMerchantCaravanIssue(h))

【接取】
GenerateIssueQuest(questId)
  → _companionRewardRandom = Random(3, 10)  ← 决定护送多久
  → SetDialogs()
  → QuestAcceptedConsequences()
    → StartQuest()
    → 生成商队 MobileParty（_questCaravanMobileParty）
    → 商队 AI：访问目标定居点列表
    → AddTrackedObject(商队)
    → 日志: "商队已出发，目的地: {SETTLEMENT}"

【执行 — 大地图护送】
A. 商队在大地图移动
   → AI 自动前往目标定居点
   → 玩家需要跟随

B. 匪徒伏击
   → HourlyTick 检测商队周围
   → Spawn 匪徒部队在商队附近
   → 匪徒跟踪 N 天 → 超时则主动攻击商队
   → 玩家需要在匪徒攻击前消灭它们

C. 到达定居点
   → OnSettlementEntered(商队, settlement)
   → _visitedSettlements.Add(settlement)
   → 检查是否达到 _requiredSettlementNumber

【结算】
商队访问了足够多的定居点:
  → 报酬 = DailyQuestRewardGold × _daysSpentForEscorting
  → RelationshipChangeWithQuestGiver = 5
  → GiveGoldAction(...)
  → CompleteQuestWithSuccess()

商队被摧毁:
  → CompleteQuestWithFail()

超时:
  → CompleteQuestWithTimeOut()
```

---

## 七、五种案例横向对比

| 维度 | Outlaws | DaughterFound | FamilyFeud | RevenueFarming | CaravanEscort |
|------|---------|---------------|------------|----------------|---------------|
| **玩法类型** | 讨伐清剿 | 寻人+说服 | 护送+巷战+道德 | 挂机收税+道德 | 护送跟随 |
| **目标数量** | 动态(N=2+6×diff) | 1个恶棍 | 1个犯人 | N个村庄 | N个定居点 |
| **动态NPC创建** | 无 | 女儿+恶棍 | 犯人 | 无 | 无 |
| **Alternative** | ✓ | ✓ | ✓ | ✗ | ✓ |
| **Lord Solution** | ✗ | ✗ | ✓(唯一) | ✗ | ✗ |
| **Persuasion** | ✗ | ✓(说服女儿) | ✓(说服Notable) | ✗ | ✗ |
| **Mission巷战** | ✗ | ✓(vs恶棍) | ✓(alley_2) | ✗ | ✗ |
| **随机事件** | ✗ | ✗ | ✗ | ✓(VillageEvent) | ✗ |
| **道德抉择** | ✗ | ✗ | ✓(背叛犯人) | ✓(私吞税收) | ✗ |
| **大地图部队** | 复用已有匪徒 | 无 | 无 | 无 | 动态生成商队+匪徒 |
| **进度模式** | 事件驱动(击杀) | 阶段驱动(找→打→说服) | 阶段驱动(找→护送→打→抉择) | 时间驱动(挂机) | 事件驱动(到达) |
| **DLL** | CampaignSystem | SandBox | SandBox | CampaignSystem | CampaignSystem |

### 7.1 按复杂度分级

| 等级 | 代表任务 | 代码量估算 | 关键复杂度来源 |
|------|---------|-----------|--------------|
| **简单** | RevenueFarming | ~400行 | 挂机收税 + 随机事件 |
| **中等** | Outlaws | ~500行 | 动态目标发现 + AI操控 |
| **中等** | CaravanEscort | ~550行 | 部队生成 + 匪徒AI |
| **复杂** | DaughterFound | ~650行 | 动态NPC + Persuasion + 多结局 |
| **极复杂** | FamilyFeud | ~800行 | 10个DialogFlow + Lord Solution + CounterOffer + Mission巷战 |

---

## 八、扩展到自定义 Issue/Quest 的设计模板

### 8.1 最小可行模板

```csharp
// 文件: MyNewIssueBehavior.cs
public class MyNewIssueBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.OnCheckForIssueEvent.AddNonSerializedListener(this, OnCheckForIssue);
    }

    private void OnCheckForIssue(Hero hero)
    {
        // ① NPC 类型检查
        if (hero.Occupation != Occupation.Merchant) return;

        // ② 冷却检查
        // ③ 前置条件检查
        // ④ 注册
        Campaign.Current.IssueManager.AddPotentialIssueData(hero,
            new PotentialIssueData(
                (pid, h) => new MyNewIssue(h, /* 自定义参数 */),
                typeof(MyNewIssue),
                IssueBase.IssueFrequency.Common));
    }

    public override void SyncData(IDataStore dataStore) { }

    // ═══ Issue ═══
    public class MyNewIssue : IssueBase
    {
        [SaveableField(10)] private SomeData _data;

        public MyNewIssue(Hero owner, SomeData data)
            : base(owner, CampaignTime.DaysFromNow(15f)) { _data = data; }

        // 必须实现:
        public override TextObject IssueBriefByIssueGiver => new TextObject("{=...}");
        public override TextObject IssueQuestSolutionExplanationByIssueGiver => new TextObject("{=...}");
        public override TextObject IssueQuestSolutionAcceptByPlayer => new TextObject("{=...}");
        public override TextObject Title => new TextObject("{=...}");
        public override TextObject Description => new TextObject("{=...}");
        protected override QuestBase GenerateIssueQuest(string questId)
            => new MyNewQuest(questId, IssueOwner, ...);
        public override IssueFrequency GetFrequency() => IssueFrequency.Common;
        protected override bool CanPlayerTakeQuestConditions(...) { ... }
        public override bool IssueStayAliveConditions() => IssueOwner?.IsAlive == true;
        protected override void HourlyTick() { }
        protected override void OnGameLoad() { }

        // 可选:
        protected override int RewardGold => 500 + (int)(1000 * IssueDifficultyMultiplier);
        public override bool IsThereAlternativeSolution => true;
    }

    // ═══ Quest ═══
    public class MyNewQuest : QuestBase
    {
        [SaveableField(10)] private int _targetCount;
        [SaveableField(20)] private int _currentCount;
        [SaveableField(30)] private JournalLog _progressLog;

        public MyNewQuest(...) : base(questId, giver, duration, reward)
        {
            SetDialogs();
            InitializeQuestOnCreation(); // 自动调 RegisterEvents + OnStartQuest
        }

        protected override void SetDialogs()
        {
            OfferDialogFlow = DialogFlow.CreateDialogFlow("issue_classic_quest_start")
                .NpcLine("好的，拜托你了...")
                .Condition(() => Hero.OneToOneConversationHero == QuestGiver)
                .Consequence(QuestAcceptedConsequences)
                .CloseDialog();
        }

        protected override void RegisterEvents()
        {
            CampaignEvents.Xxx.AddNonSerializedListener(this, OnXxx);
        }

        protected override void OnStartQuest()
        {
            _progressLog = AddDiscreteLog(startText, "进度", 0, _targetCount);
        }

        private void QuestAcceptedConsequences()
        {
            StartQuest();
            _progressLog = AddDiscreteLog(startText, "进度", 0, _targetCount);
        }

        private void OnXxx(/* event args */)
        {
            _currentCount++;
            _progressLog.UpdateCurrentProgress(_currentCount);
            if (_currentCount >= _targetCount)
                SuccessConsequences();
        }

        private void SuccessConsequences()
        {
            RelationshipChangeWithQuestGiver = 3;
            GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, RewardGold);
            CompleteQuestWithSuccess();
        }

        protected override void HourlyTick() { }
        protected override void InitializeQuestOnGameLoad() { SetDialogs(); }
    }
}
```

### 8.2 常见模式速查

| 需求 | 参考案例 | 关键做法 |
|------|---------|---------|
| 动态生成目标列表 | Outlaws | `HourlyTickParty` 扫描 → 加入列表 → AI 操控 |
| 创建临时 NPC | DaughterFound / FamilyFeud | `HeroCreator.CreateSpecialHero` + `HiddenInEncylopedia` |
| 说服系统 | DaughterFound / FamilyFeud | `PersuasionTask` + `PersuasionDifficulty` |
| 多结局 | DaughterFound / FamilyFeud | 多种 `SuccessConsequences` / `FailQuest` 路径 |
| 道德抉择 | FamilyFeud / RevenueFarming | `_playerBetrayed` flag + `CompleteQuestWithBetrayal()` |
| 挂机进度 | RevenueFarming | `HourlyTick` + `GiveGoldAction` 实时到账 |
| 随机事件 | RevenueFarming | `VillageEvent` + `_currentVillageEvents` 字典 |
| CounterOffer | FamilyFeud | `CounterOfferHero` + `AfterIssueCreation` + `LordSolutionConsequence` |
| AI 操控大地图部队 | Outlaws / CaravanEscort | `SetPartyAiAction` + `SetDoNotMakeNewDecisions` |
| 资源互斥 | Outlaws | `BusyHideouts.Add()` 防止多任务抢资源 |
| Mission 巷战 | DaughterFound / FamilyFeud | 在 alley_2 / village 场景中放置 Agent |

### 8.3 与我们 Commission 管线的对照

我们的 `CommissionQuest` 已经将上述模式中的大部分抽象为数据驱动：

| 原版模式 | 我们的 CommissionQuest 对应 |
|----------|---------------------------|
| 每个任务独立的 Issue + Quest 子类 | `CommissionData` + `CommissionDef` 模板 |
| 每个任务独立的 `SetDialogs()` | `CommissionIntent` 的三层叙事 fallback |
| 每个任务独立的 `RegisterEvents()` | `CommissionQuest.RegisterEvents()` 按 `CommissionCategory` switch |
| 每个任务独立的进度更新 | `UpdateProgress()` + `AddDiscreteLog` |
| `HeroCreator.CreateSpecialHero` | 直接使用 `WorldEventDatabase` 中的真实 Hero |
| `PersuasionTask` | 未实现（可扩展） |
| `VillageEvent` 随机事件 | `ComplicationTable` + `JourneyEvents` |
| CounterOffer / 背叛 | 未实现（可扩展） |

---

## 附录：关键类型速查

| 类型/方法 | 用途 | DLL |
|-----------|------|-----|
| `IssueBase` | 问题基类 | CampaignSystem |
| `QuestBase` | 任务基类 | CampaignSystem |
| `IssueManager` | Issue 生命周期管理 | CampaignSystem |
| `QuestManager` | Quest 追踪/日志 | CampaignSystem |
| `IssuesCampaignBehavior` | 每日调度 Issue 生成 | CampaignSystem |
| `CampaignEventDispatcher.OnCheckForIssueEvent` | Issue 触发事件 | CampaignSystem |
| `HeroCreator.CreateSpecialHero()` | 创建临时 NPC | CampaignSystem |
| `DialogFlow.CreateDialogFlow()` | 对话流构建器 | CampaignSystem |
| `PersuasionTask` | 说服检查 | CampaignSystem |
| `SetPartyAiAction` | 大地图部队 AI 操控 | CampaignSystem |
| `DefaultIssueEffects` | 内置 IssueEffect 集合 | CampaignSystem |
| `QuestHelper.CheckRosterForAlternativeSolution()` | Alternative 部队检查 | CampaignSystem |
| `TraitLevelingHelper.OnIssueSolvedThroughQuest()` | 特质变更 | CampaignSystem |

---

## 九、绕过对话系统接取 Quest：反射现状与接口方案

### 9.1 为什么原版必须用反射

我们的 `CommissionIntent` 绕过了原版对话系统（`ConversationManager` → `DialogFlow.Consequence` 委托链），直接调用 `IssueManager.StartIssueQuest(hero)` 创建 Quest 对象。但 `StartIssueQuest` 只调了 `GenerateIssueQuest` → `InitializeQuestOnCreation()`，**不会自动调用 `QuestAcceptedConsequences()`**——这个方法在原版流程中是由对话系统在玩家点击"接取"后通过委托触发的。

**现状**（`CommissionIntent.InvokeQuestAcceptedConsequences`）：

```csharp
foreach (var methodName in new[] { "QuestAcceptedConsequences", "OnQuestAccepted", "HandleQuestAccepted" })
{
    var method = questType.GetMethod(methodName,
        BindingFlags.NonPublic | BindingFlags.Instance);
    if (method != null && method.GetParameters().Length == 0)
    {
        method.Invoke(quest, null);
        return true;
    }
}
```

**为什么反射是正确的选择**：

| 因素 | 现实 |
|------|------|
| 所有 40 种原版 Quest | 方法全是 `private`，无基类虚方法 |
| 分布 | 两个 DLL（`CampaignSystem.dll` 33 个 + `SandBox.dll` 7 个） |
| 命名不统一 | `QuestAcceptedConsequences` 34 个 + `OnQuestAccepted` 6 个 |
| 调用频率 | 每个任务一生只调用一次（接取时），反射开销可忽略 |
| Harmony Reverse Patch | 需要 40 个 patch 类，维护成本爆炸 |

**结论：反射不是 workaround，而是这个约束条件下的最优解。**

### 9.2 自定义 Quest 的接口方案

如果你自己设计 Quest，可以让它实现一个接口，`CommissionIntent` 优先走接口，没实现再用反射兜底：

```csharp
// 定义接口（放在 LivingWorldNpcs 项目中）
public interface IQuestAcceptedConsequences
{
    void QuestAcceptedConsequences();
}

// 你的自定义 Quest 实现它
public class MyCustomQuest : QuestBase, IQuestAcceptedConsequences
{
    public void QuestAcceptedConsequences()  // ← public，实现接口
    {
        StartQuest();
        this._questProgressLogTest = AddDiscreteLog(...);
    }

    protected override void SetDialogs()
    {
        this.OfferDialogFlow = DialogFlow.CreateDialogFlow("issue_classic_quest_start", 100)
            .NpcLine(...)
            .Condition(() => Hero.OneToOneConversationHero == base.QuestGiver)
            .Consequence(new OnConsequenceDelegate(this.QuestAcceptedConsequences))
            // ↑ 同时兼容原版对话系统和我们的接口方案——同一个方法
            .CloseDialog();
    }
}
```

**CommissionIntent 改造**（接口优先 + 反射兜底）：

```csharp
private static bool InvokeQuestAcceptedConsequences(QuestBase quest)
{
    // ① 优先：走接口（自定义 Quest，零反射）
    if (quest is IQuestAcceptedConsequences q)
    {
        q.QuestAcceptedConsequences();
        return true;
    }

    // ② 兜底：反射（原版 40 种 Quest）
    var questType = quest.GetType();
    foreach (var methodName in new[] { "QuestAcceptedConsequences", "OnQuestAccepted", "HandleQuestAccepted" })
    {
        var method = questType.GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method != null && method.GetParameters().Length == 0)
        {
            method.Invoke(quest, null);
            return true;
        }
    }
    return false;
}
```

**为什么不能是 `protected`**：`protected` 只允许继承链（子类调父类）访问。`CommissionIntent` 不继承你的 Quest 子类，即使方法标为 `protected`，编译器照样拒绝。必须 `public` + 接口。

### 9.3 访问修饰符对比

```csharp
// 假设 quest 是 MyCustomQuest 的实例
quest.QuestAcceptedConsequences();

// private   → ❌ 编译报错（只有 MyCustomQuest 类内部能调）
// protected → ❌ 编译报错（只有 MyCustomQuest + 它的子类能调）
// public    → ✅ 谁都能调
// 接口      → ✅ 任何有接口引用的地方都能调，且无需知道具体 Quest 类型
```

# 02 — Issue→Quest 架构深度解析

> 原版骑砍2 的 NPC 委托任务使用 **Issue/Quest 双层模型**。本文聚焦架构层面的设计决策和可复用结构，完整调用链见 [../quest_example.md](../quest_example.md)。

---

## 目录

1. [双层模型：Issue 与 Quest 的分离哲学](#一双层模型issue-与-quest-的分离哲学)
2. [三层类结构：Behavior→Issue→Quest](#二三层类结构behaviorissuequest)
3. [事件总线：全局广播 + 本地过滤](#三事件总线全局广播--本地过滤)
4. [三种解决路径](#四三种解决路径)
5. [冷却与频率系统](#五冷却与频率系统)
6. [IssueEffect 惩罚系统](#六issueeffect-惩罚系统)
7. [前置条件系统（PreconditionFlags）](#七前置条件系统)
8. [对话集成（Token 体系）](#八对话集成)
9. [存档体系](#九存档体系)

---

## 一、双层模型：Issue 与 Quest 的分离哲学

### 核心洞察

原版系统最优雅的设计是 **Issue 不依赖玩家存在**：

```
┌──────────────────────────────────────────────────────────┐
│  Issue (IssueBase) — "世界中的问题"                       │
│  · NPC 有 Issue 不等于玩家接到了任务                       │
│  · Issue 有独立的生命周期（过期/条件失效）                   │
│  · AI 领主也能接 Issue（理论上）                           │
│  · 未解决的 Issue 对定居点产生负面效果（IssueEffect）        │
├──────────────────────────────────────────────────────────┤
│  Quest (QuestBase) — "玩家的任务"                         │
│  · 玩家专属，QuestManager 追踪                             │
│  · 有任务日志（JournalLog）、子步骤（QuestTaskBase）        │
│  · 5 种完成方式：Success/Cancel/Fail/Timeout/Betrayal     │
└──────────────────────────────────────────────────────────┘
```

### 生命周期

```
Issue 生成  →  Issue 待接（NPC头顶"!"）→  玩家接取  →  Quest 激活  →  Quest 结算  →  Issue 清理
    ↓                    ↓                    ↓              ↓              ↓              ↓
 OnCheckForIssue    IssueState.Ongoing   GenerateIssueQuest  RegisterEvents  CompleteQuest  IssueFinalized
```

### 对我们 Commission 的启示

我们的 `CommissionHubIssue` / `CommissionQuest` 直接对标这层分离——Issue 是"世界中有委托可接"，Quest 是"玩家正在执行的委托"。

---

## 二、三层类结构：Behavior→Issue→Quest

每个原版任务由**三个类**组成，全部定义在**同一个 `CampaignBehaviorBase` 文件**中：

```
XxxIssueQuestBehavior : CampaignBehaviorBase     ← 第 1 层：触发调度 + 注册
├── [嵌套类] XxxIssue : IssueBase                ← 第 2 层：世界中的问题
└── [嵌套类] XxxIssueQuest : QuestBase           ← 第 3 层：玩家的任务
```

### 第 1 层：CampaignBehavior — 触发调度器

```csharp
public class MyNewIssueBehavior : CampaignBehaviorBase
{
    // 注册到 Campaign 事件
    public override void RegisterEvents()
    {
        CampaignEvents.OnCheckForIssueEvent.AddNonSerializedListener(
            this, OnCheckForIssue);
    }

    // 触发检查
    private void OnCheckForIssue(Hero hero)
    {
        // ① 检查 NPC 类型是否匹配（Occupation / IsLord / IsGangLeader ...）
        // ② 检查冷却（IssueCoolDownData）
        // ③ 检查前置条件（CanPlayerTakeQuestConditions）
        // ④ 注册 PotentialIssueData
        Campaign.Current.IssueManager.AddPotentialIssueData(hero,
            new PotentialIssueData(
                (pid, h) => new MyNewIssue(h, /* 参数 */),
                typeof(MyNewIssue),
                IssueBase.IssueFrequency.Common));
    }

    public override void SyncData(IDataStore dataStore) { }
}
```

**关键**：`OnCheckForIssue` 只是"提名"，不是"创建"。IssueManager 根据频率权重和配额最终决定是否创建。

### 第 2 层：Issue — 问题定义

Issue 的核心职责：
1. **定义文本**（IssueBrief / Title / Description / 各种 Accept 文本）
2. **定义奖励公式**（RewardGold）
3. **前置条件**（CanPlayerTakeQuestConditions）
4. **工厂方法**（GenerateIssueQuest）
5. **定义可选解决路径**（IsThereAlternativeSolution / IsThereLordSolution）
6. **定义未解决的负面效果**（GetIssueEffectAmountInternal）

### 第 3 层：Quest — 任务执行

Quest 的核心职责：
1. **状态管理**（存档字段、IsOngoing）
2. **事件订阅**（RegisterEvents）
3. **进度追踪**（AddDiscreteLog / AddTrackedObject）
4. **对话流**（SetDialogs）
5. **结算**（CompleteQuestWithSuccess / Cancel / Fail / Timeout / Betrayal）

### 三层间的数据流

```
Behavior.OnCheckForIssue(hero)
  → 检查 NPC 类型 / 冷却 / 前置条件
  → IssueManager.AddPotentialIssueData(hero, factory)
  → Issue 对象创建（IssuesCampaignBehavior 调度）
  → Issue 在世界上存在（可能被接取，也可能过期消失）

玩家接取:
  → Issue.StartIssueWithQuest()
  → Issue.GenerateIssueQuest(questId)  ← 第2层调第3层的工厂
  → Quest 构造 → SetDialogs() → InitializeQuestOnCreation()
  → Quest.StartQuest() → OnStartQuest() + RegisterEvents()
```

---

## 三、事件总线：全局广播 + 本地过滤

### 架构

```
                       CampaignEventDispatcher（全局单例）
                              ┌───────────────────┐
  触发源（任意系统）──────────→│  OnMobilePartyDestroyed│
                              │  OnSettlementEntered    │
                              │  OnWarDeclared          │
                              │  ...                    │
                              └───┬───────┬───────┬───┘
                                  │       │       │
                          ┌───────┘       │       └───────┐
                          ▼               ▼               ▼
                    Quest A 回调    Quest B 回调    Quest C 回调
                    (护送商队)      (清剿匪徒)      (包税权)
                         │               │               │
                    过滤: 不是    过滤: 不在     过滤: 不关心
                    我的商队?      _validList?    此事件?
                    → return;     → return;      → return;
```

### 铁律：过滤优先

每个事件回调的**第一行必须是过滤**：

```csharp
private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
{
    if (!base.IsOngoing) return;                           // ① 任务是否活跃
    if (destroyer != PartyBase.MainParty) return;            // ② 是否玩家所为
    if (!_validPartiesList.Contains(party)) return;          // ③ 是否为目标任务
    // ④ 处理逻辑...
}
```

### 为什么不用路由层

原版选择"全局广播 + 本地过滤"而非"事件路由层"（如按 PartyId 分发），原因：
- 路由层的索引维护成本高（加入/移除/变更都要更新索引）
- 大多数事件回调是 O(1) 过滤（引用比较 / Contains）
- 活跃 Quest 数量通常 < 10 个，广播开销可忽略

---

## 四、三种解决路径

### Quest Solution（亲自执行）

标准玩家流程：接任务 → 大地图/场景执行 → 结算。

**启动链**：
```
IssueManager.StartIssueQuest(hero)
  → Issue.StartIssueWithQuest()
  → GenerateIssueQuest(questId)  // 工厂创建 Quest
  → Quest 构造 → SetDialogs() → InitializeQuestOnCreation()
  → 对话系统回调 QuestAcceptedConsequences()
  → quest.StartQuest()
```

### Alternative Solution（派人代办）

玩家派同伴 + 士兵去解决。倒计时结束自动结算。

**核心公式**（`DefaultIssueModel`）：

```
成功率 = 1 - min((需求技能 - 同伴技能) × 0.5 / 100, 0.9)
       ≈ 同伴技能越高，成功率越高（上限 90%）

伤亡率 = clamp(ceil(需求部队 × value), 1, 需求部队)
       where value = clamp((需求技能 / 同伴技能) × 0.1, 0.2, 0.8)

实际天数 = 基础天数 + 2 × clamp(需求技能 / 同伴技能, 0, 10)

实际部队 = 基础部队 × clamp(需求技能 / 同伴技能, 0.2, 1.2)
```

**关键**：同伴技能是核心变量——影响成功率、伤亡率、耗时、需求部队数四个维度。

**Issue 层需要重写**：
```csharp
public override bool IsThereAlternativeSolution => true;
protected override int AlternativeSolutionBaseNeededMenCount => 5;
protected override int AlternativeSolutionBaseDurationInDaysInternal => 6;
// 需求技能由 IssueModel 从 CompanionSkillRewardXP 推导
```

### Lord Solution（领主帮忙）

仅 FamilyFeudIssue 有此功能。消耗影响力请附近领主解决。

**需要的 Issue 层重写**：
```csharp
public override bool IsThereLordSolution => true;
public override int NeededInfluenceForLordSolution => 20;
public override Hero CounterOfferHero { get; protected set; }
public override bool LordSolutionCondition(out TextObject explanation) { ... }
```

**CounterOffer 流**：
```
玩家选择 Lord Solution
  → Issue.StartIssueWithLordSolution()
  → 监听 BeforeGameMenuOpenedEvent
  → CounterOfferHero 主动对话 → 提条件
  → 接受: 背叛原 NPC → 关系/特质惩罚
  → 拒绝: 消耗影响力 → 获得奖励
```

---

## 五、冷却与频率系统

### Issue 生成概率（指数衰减）

```csharp
// IssuesCampaignBehavior.GetIssueGenerationChance:
float GetIssueGenerationChance(int current, int max)
{
    float ratio = 1f - (float)current / max;
    return 0.3f * ratio * ratio;  // 二次衰减
}
// 0 → 30%, 50%满 → 7.5%, 越近上限越低
```

### 频率加权选择

```csharp
// IssueFrequency → 分值: VeryCommon=6, Common=3, Rare=1
// 评分: score = frequencyRatio × (1 + bonus - existingRatio/frequencyRatio)
// 选择: 定居点用加权随机, 家族用最高分优先

// 惩罚 "过度代表" 的类型：某类 Issue 已在世界上太多 → 该类型评分下降
```

### 冷却

- **NPC 冷却**：完成 Issue 后 30 天（`IssueModel.IssueOwnerCoolDownInDays`）
- **共享冷却**：`SnareTheWealthy` / `EscortMerchantCaravan` / `CaravanAmbush` 共享
- **管理**：`IssuesCampaignBehavior` 内部用 `IssueCoolDownData` 数据结构

---

## 六、IssueEffect 惩罚系统

未解决的 Issue 对定居点产生持续负面效果：

```csharp
// IssueBase 中
public float GetActiveIssueEffectAmount(IssueEffect effect)
{
    if (_issueState != IssueState.Ongoing) return 0f; // Issue 已被接取 → 无惩罚
    return GetIssueEffectAmountInternal(effect);       // 各 Issue 各自实现
}

// 示例：MerchantNeedsHelpWithOutlaws
protected override float GetIssueEffectAmountInternal(IssueEffect effect)
{
    if (effect == DefaultIssueEffects.SettlementProsperity) return -0.2f;
    if (effect == DefaultIssueEffects.IssueOwnerPower)     return -0.1f;
    if (effect == DefaultIssueEffects.SettlementSecurity)   return -1f;
    return 0f;
}
```

**应用时机**：
- 玩家进入定居点时实时计算（累加所有 Notable 的 Issue Effect）
- Alternative 模式：返回前 1 天清除 Effect

---

## 七、前置条件系统

每个 Issue 通过 `CanPlayerTakeQuestConditions` 返回拒绝原因：

| Flag | 含义 | 典型条件 |
|------|------|---------|
| `AtWar` | 在交战国 | `faction1.IsAtWarWith(faction2)` |
| `NotInSameFaction` | 非同阵营 | 雇佣兵检查 |
| `MainHeroIsKingdomLeader` | 玩家是国王 | 某些任务国王不适合接 |
| `ClanTier` | 家族等级低 | `Clan.PlayerClan.Tier < N` |
| `Renown` | 声望不够 | `Hero.MainHero.Clan.Renown < N` |
| `Relation` | 关系太差 | `hero.GetRelationWithPlayer() < -10` |
| `Skill` | 技能不够 | 特定技能 < 阈值 |
| `Money` | 钱不够 | `Hero.MainHero.Gold < N` |
| `Wounded` | 玩家受伤 | `Hero.MainHero.IsWounded` |
| `NotEnoughTroops` | 兵力不足 | `MobileParty.MainParty.MemberRoster.TotalHealthyCount < N` |

---

## 八、对话集成

### 5 个 Token

| Token | 触发时机 | 管理方 |
|-------|---------|--------|
| `hero_main_options` | NPC 主菜单 | ConversationManager |
| `issue_offer` | "有什么我可以帮忙的吗？" | IssueManager |
| `issue_classic_quest_start` | "我亲自去" 选项 | 各 Issue.OfferDialogFlow |
| `issue_discuss_alternative_solution` | "换个方式" 选项 | IssueManager |
| `quest_offer` | 进行中 Quest 的对话 | QuestManager |

### 优先级

```
进入对话:
  1. 先检查 NPC 是否有 Quest（进行中任务）→ quest_offer token
  2. 再检查 NPC 是否有 Issue（可用任务）→ issue_offer token
  3. 最后显示常规对话选项
```

### 绕过对话系统接取（反射 / 接口）

我们的 `CommissionIntent` 绕过了对话系统。详见 [../quest_example.md#九绕过对话系统接取-quest反射现状与接口方案](../quest_example.md#九绕过对话系统接取-quest反射现状与接口方案)。

核心结论：反射是此约束条件下的最优解，自定义 Quest 应实现 `IQuestAcceptedConsequences` 接口走零反射路径。

---

## 九、存档体系

### SaveableField / SaveableProperty

```csharp
// 所有需要跨存档持久化的字段必须标记
[SaveableField(10)]   // uniqueId 在类内唯一
private int _targetCount;

[SaveableProperty(25)]
public CampaignTime ReturnTime { get; private set; }
```

**唯一 ID 范围**：
- Issue 层：1-99（通常 10, 20, 30...）
- Quest 层：1-99（通常 10, 20, 30...）
- 两层的 ID 互相独立（不同类）

**特殊对象存档**：
- `JournalLog` 需要 `[SaveableField]` 标记
- `CampaignTime` 用 `[SaveableProperty]` 标记
- `List<T>` / `Dictionary<K,V>` 中 T 必须是 MBObjectBase 子类或基本类型

### InitializeQuestOnGameLoad

从存档恢复时需要重新注册对话流（因为 `SetDialogs` 中注册到 `ConversationManager` 的对象不会自动恢复）：

```csharp
protected override void InitializeQuestOnGameLoad()
{
    SetDialogs();  // 重新注册 DialogFlow
    // 事件订阅不需要重新注册（CampaignEvents 通过 AddNonSerializedListener 注册的会自动恢复）
}
```

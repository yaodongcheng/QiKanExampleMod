# 04 — 可复用模式目录

> **使用方式**：想实现某种功能 → 看对应模式章节 → 找参考任务 → copy 接口签名。
> 每个模式包含：**解决什么问题 / 关键 API / 参考任务 / 调用范例**。

---

## 目录

1. [表现力模式（Presentation）](#一表现力模式)
2. [进度追踪模式（Progress）](#二进度追踪模式)
3. [NPC 创建与管理模式](#三npc-创建与管理模式)
4. [事件驱动模式（Event）](#四事件驱动模式)
5. [经济与资源转移模式](#五经济与资源转移模式)
6. [道德抉择与背叛模式](#六道德抉择与背叛模式)
7. [大地图部队 AI 操控模式](#七大地图部队-ai-操控模式)
8. [对话流模式（DialogFlow）](#八对话流模式)
9. [资源互斥与冷却模式](#九资源互斥与冷却模式)
10. [Mission 场景模式](#十mission-场景模式)
11. [综合范本：原版任务的设计骨架](#十一综合范本原版任务的设计骨架)

---

## 一、表现力模式

**解决什么问题**：让玩家知道任务目标在哪、进度如何、发生了什么。

### 1.1 地图追踪标记 — `AddTrackedObject`

**用途**：在大地图上为任务目标添加追踪圆圈标记。

```csharp
// QuestBase 内置方法（protected）
void AddTrackedObject(object obj);

// 支持的类型：
//   MobileParty — 追踪部队（商队/匪徒/逃兵）
//   Hero         — 追踪人物（恶棍/女儿/仇敌）
//   Settlement   — 追踪定居点（目标村庄/城镇/藏身处）
//   ItemObject   — 追踪物品（罕见，如特殊武器）

// 移除追踪（Quest 完成/失败时自动清理）
void RemoveTrackedObject(object obj);
```

**参考任务**：
| 任务 | 追踪什么 |
|------|---------|
| EscortMerchantCaravan | 商队 MobileParty |
| NotableWantsDaughterFound | 女儿 Hero（找到后）、恶棍 Hero |
| MerchantNeedsHelpWithOutlaws | 匪徒 MobileParty（动态加入） |
| LordWantsRivalCaptured | 仇敌 Hero |
| ScoutEnemyGarrisons | 3 个敌方 Settlement |
| NearbyBanditBase | 藏身处 Settlement |

**调用范例**（来自 MerchantNeedsHelpWithOutlaws）：
```csharp
// 每小时发现新匪徒 → 加入追踪
private void HourlyTickParty(MobileParty mobileParty)
{
    if (!IsTracked(mobileParty))
        AddTrackedObject(mobileParty);  // ★ 大地图出现圆圈标记
    _validPartiesList.Add(mobileParty);
}
```

### 1.2 快速信息提示 — `MBInformationManager.AddQuickInformation`

**用途**：在屏幕上方弹出简短提示（"已消灭 3/5 队匪徒"）。

```csharp
// 静态方法
MBInformationManager.AddQuickInformation(TextObject text);

// 典型用法：每次进度更新时弹出
MBInformationManager.AddQuickInformation(
    new TextObject("{=...}你已消灭 {COUNT}/{TOTAL} 队匪徒")
        .SetTextVariable("COUNT", _destroyedPartyCount)
        .SetTextVariable("TOTAL", _totalPartyCount));
```

**参考任务**：MerchantNeedsHelpWithOutlaws（每次击杀）、RevenueFarming（每次收税推进）、NearbyBanditBase

**调用时机**：在 `AddQuestStepLog` / `UpdateCurrentProgress` 之后立即调用，形成"进度条跳动 + 文字提示"的双重反馈。

### 1.3 地图箭头 — `QuestHelper.AddMapArrowFromPointToTarget`

**用途**：从当前位置画一个指向目标位置的地图箭头（常用于多目标侦察类任务）。

```csharp
// QuestHelper 静态方法
// 参数：起点坐标 → 终点坐标 → 箭头颜色？
QuestHelper.AddMapArrowFromPointToTarget(
    Vec2 fromPoint,
    Vec2 toPoint);
```

**参考任务**：
| 任务 | 箭头用途 |
|------|---------|
| ScoutEnemyGarrisons | 从玩家位置指向 3 个敌方驻军目标 |
| TheSpyParty | 指向举办比武大会的城镇 |

**调用范例**（来自 ScoutEnemyGarrisons）：
```csharp
private void AddMapArrows()
{
    var playerPos = MobileParty.MainParty.Position2D;
    if (_settlement1 != null)
        QuestHelper.AddMapArrowFromPointToTarget(playerPos, _settlement1.Position2D);
    if (_settlement2 != null)
        QuestHelper.AddMapArrowFromPointToTarget(playerPos, _settlement2.Position2D);
    if (_settlement3 != null)
        QuestHelper.AddMapArrowFromPointToTarget(playerPos, _settlement3.Position2D);
}
```

### 1.4 离散进度日志 — `AddDiscreteLog`

**用途**：在任务日志面板创建带进度条的条目（"消灭匪徒 3/5"）。

```csharp
// QuestBase 方法
JournalLog AddDiscreteLog(
    TextObject taskStartText,   // 任务开始时的描述
    TextObject progressText,    // 进度描述（如"消灭匪徒"）
    int currentProgress,        // 当前值
    int targetProgress,         // 目标值
    LogType type = LogType.Discreate); // 离散（整数步进）vs 连续（百分比）

// 更新进度
journalLog.UpdateCurrentProgress(newValue);

// 进度完成 → 自动标记为完成状态
```

**参考任务**：几乎所有原版任务都用这个。

**调用范例**：
```csharp
protected override void OnStartQuest()
{
    _questProgressLog = AddDiscreteLog(
        new TextObject("{=...}去{VILLAGE}附近消灭匪徒"),
        new TextObject("{=...}已消灭匪徒"),
        0,
        _totalPartyCount);
}

private void AddQuestStepLog()
{
    _questProgressLog.UpdateCurrentProgress(_destroyedPartyCount + _recruitedPartyCount);
    MBInformationManager.AddQuickInformation(textObject); // ★ 配合快速提示
    if (_questPartyProgress >= _totalPartyCount)
        SuccessConsequences();
}
```

### 1.5 普通任务日志 — `AddLog`

**用途**：在任务日志面板添加纯文本条目（无进度条）。

```csharp
// QuestBase 方法
void AddLog(TextObject text);
```

**典型场景**：
- 任务开始时："商人请求你消灭附近的匪徒"
- 阶段切换时："你已经找到了恶棍的藏身处"
- 结算时："你已经成功消灭了所有匪徒"（不同结算文本使用不同 AddLog）

### 1.6 综合表现力方案：挂机收税进度条

**场景**：RevenueFarming 的"每小时给玩家金币 + 推进进度条"。

**涉及的接口全链路**：
```
HourlyTick()
  → village.CollectedAmount += village.HourlyGain         // 数据推进
  → GiveGoldAction.ApplyBetweenCharacters(null, player, gain) // 金币实时到账
  → if (30%进度) → 
      → VillageEvent 随机事件触发                        // 玩法注入
      → GameMenu.SwitchToMenu(eventId)                   // 弹出事件菜单
  → if (100%进度) →
      → AddLog("村庄税收完成")                            // 日志
      → MBInformationManager.AddQuickInformation(...)    // 屏幕提示
  → _questProgressLog.UpdateCurrentProgress(...)         // 进度条更新
```

**关键设计**：
1. 数据层：`RevenueVillage.CollectedAmount`（存档字段，持久化）
2. 金流层：`GiveGoldAction(null, player, hourlyGain)` — 实时给钱，不是结算时一次性给
3. 反馈层：`AddDiscreteLog` 的进度条 + `MBInformationManager.AddQuickInformation` 的屏幕提示
4. 变量层：`HourlyGain = TargetAmount / 10` — 让 10 小时收完一个村庄
5. 事件层：30% 进度时注入随机事件，打破单调等待

---

## 二、进度追踪模式

### 2.1 离散计数型（Discrete Counter）

**场景**：消灭 N 个目标 / 交付 N 个物品 / 访问 N 个定居点。

**数据流**：
```
事件触发 → counter++ → UpdateCurrentProgress(counter) → if (counter >= target) → 完成
```

**参考任务**：
- MerchantNeedsHelpWithOutlaws：击杀计数（_destroyedPartyCount）
- HeadmanNeedsGrain：交付计数（_delieveredGrainCount）
- EscortMerchantCaravan：访问定居点计数（_visitedSettlements.Count）
- RaidAnEnemyTerritory：劫掠村庄计数（_raidedVillages.Count）

**存档字段范本**：
```csharp
[SaveableField(10)] private int _targetCount;
[SaveableField(20)] private int _currentCount;
[SaveableField(30)] private JournalLog _progressLog;
```

### 2.2 阶段驱动型（Stage-driven）

**场景**：任务有多个阶段，每个阶段完成后切换到下一阶段。

**数据流**：
```
阶段A完成 → 切换日志/追踪 → 阶段B开始 → ... → 最终阶段 → 结算
```

**状态机实现方式**：
- NotableWantsDaughterFound：`_isTrackerLogAdded` → `_didPlayerBeatRouge` → `_isDaughterPersuaded`
- FamilyFeud：`_culpritJoinedPlayerParty` → Fight 阶段 → 三选一 → 收尾对话
- LordsNeedsTutor：找到教师 → 说服 → 护送

**设计诀窍**：
- 每个阶段用一个 bool 存档字段标记
- 阶段切换时 `RemoveTrackedObject` 旧目标 + `AddTrackedObject` 新目标
- 阶段切换时用 `AddLog` 更新日志文本

### 2.3 挂机型（Hanging/Timed Progress）

**场景**：不是靠事件触发推进，而是靠时间流逝自动推进。

**数据流**：
```
HourlyTick → 推进数值 → 实时给奖励 → 更新进度条 → 到达阈值 → 触发事件/完成
```

**参考任务**：RevenueFarming（收税每小时的 HourlyGain）

**核心 API**：
```csharp
protected override void HourlyTick()
{
    if (!base.IsOngoing) return;
    
    // 数据推进
    _progressValue += _hourlyRate;
    
    // 实时奖励
    GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, _hourlyRate);
    
    // 进度条
    _progressLog.UpdateCurrentProgress(_progressValue);
    
    // 检查触发点
    if (_progressValue >= _triggerThreshold && !_eventTriggered)
    {
        _eventTriggered = true;
        TriggerRandomEvent();
    }
    
    // 检查完成
    if (_progressValue >= _targetValue)
        SuccessConsequences();
}
```

**进度速度设计**：
- RevenueFarming: `HourlyGain = TargetAmount / 10`（10 小时收完一个村庄）
- 对于更短的挂机：`HourlyGain = TargetAmount / 5`（5 小时）
- 对于更长的挂机：`HourlyGain = TargetAmount / 20`（20 小时）

### 2.4 多路径贡献型（Multi-path Contribution）

**场景**：任务进度可以通过多种方式推进，每种方式贡献不同。

**参考任务**：MerchantNeedsHelpWithOutlaws — 消灭（+1）+ 招募（+1）

**数据流**：
```csharp
private int _questPartyProgress => _destroyedPartyCount + _recruitedPartyCount;

// 路径A：消灭
private void MobilePartyDestroyed(...)
{
    _destroyedPartyCount++;
    AddQuestStepLog();
}

// 路径B：招募
private void OnBanditPartyRecruited(...)
{
    _recruitedPartyCount++;
    AddQuestStepLog();
}

// 统一检查
private void AddQuestStepLog()
{
    _questProgressLog.UpdateCurrentProgress(_questPartyProgress);
    if (_questPartyProgress >= _totalPartyCount)
        SuccessConsequences();
}
```

**结算时根据路径分布选择不同文本**：
```csharp
if (_destroyedPartyCount == _totalPartyCount)
    AddLog(_successQuestLogText1);  // 全灭
else if (_recruitedPartyCount != 0)
    AddLog(_successQuestLogText2);  // 部分招募
else
    AddLog(_successQuestLogText3);  // 全部招募
```

---

## 三、NPC 创建与管理模式

### 3.1 动态临时 NPC — `HeroCreator.CreateSpecialHero`

**用途**：为任务创建不会永久存在于游戏世界的临时 NPC。

```csharp
// 签名
Hero HeroCreator.CreateSpecialHero(
    CharacterObject template,      // NPC 模板（如 "townsman_vlandia"）
    Settlement bornSettlement,     // 出生定居点
    Clan clan = null,              // 所属家族（null = 无家族）
    IFaction faction = null,       // 阵营
    int age = -1);                 // 年龄（-1 = 随机成年）

// 创建后必须：
hero.CharacterObject.HiddenInEncylopedia = true;  // 不污染百科
hero.SetMortality(immortal: true);                // 任务期间不死（可选）
```

**参考任务**：
| 任务 | 创建的 NPC | 模板 |
|------|-----------|------|
| NotableWantsDaughterFound | 女儿 Hero（18~25岁） | notableTemplate |
| NotableWantsDaughterFound | 恶棍 Hero | banditBoss (按 culture) |
| FamilyFeud | 犯人 Hero | townsman_ + culture |
| ProdigalSon | 浪子 Hero | lord 模板（子类型） |

**完整创建范本**（来自 NotableWantsDaughterFound）：
```csharp
// 创建女儿
_daughterHero = HeroCreator.CreateSpecialHero(
    notableTemplate,
    questGiver.HomeSettlement,
    null, null,
    MBRandom.RandomInt(18, 25));       // 随机 18~25 岁
_daughterHero.CharacterObject.HiddenInEncylopedia = true;
_daughterHero.Father = questGiver;      // 设定父女关系

// 创建恶棍（按 culture 选择模板）
var rogueTemplate = questGiver.Culture.StringId switch
{
    "khuzait" => steppe_bandits.Culture.BanditBoss,
    "vlandia" => mountain_bandits.Culture.BanditBoss,
    // ... 6 种 culture
};
_rogueHero = HeroCreator.CreateSpecialHero(rogueTemplate, targetVillage);
_rogueHero.CharacterObject.HiddenInEncylopedia = true;
```

### 3.2 临时装备 NPC — `CivilianEquipment.AddEquipmentToSlotWithoutAgent`

**用途**：给动态创建的 NPC 穿装备（在 Mission 中会被渲染）。

```csharp
// 给犯人装备一把匕首
_culprit.CivilianEquipment.AddEquipmentToSlotWithoutAgent(
    EquipmentIndex.Weapon0,
    new EquipmentElement(pugio));    // pugio = ItemObject
```

**参考任务**：FamilyFeud（犯人装备匕首）

**注意**：
- 只设置 `CivilianEquipment`（平民装备），Mission 场景（城镇内）使用这个
- `BattleEquipment` 用于大地图战斗
- 如果不设置装备，NPC 在 Mission 中会是裸体

### 3.3 NPC 关系设定

**用途**：设定动态 NPC 之间的家庭/社会关系。

```csharp
_daughterHero.Father = questGiver;    // 父女关系
// 其他可用关系（通过 Hero 属性）：
// hero.Mother, hero.Spouse, hero.Clan
```

**注意**：`Father`/`Mother`/`Spouse` 关系设定后，游戏对话系统会自动识别（"你父亲让我来找你"等文本）。

---

## 四、事件驱动模式

### 4.1 事件订阅 — `CampaignEvents.Xxx.AddNonSerializedListener`

**用途**：订阅游戏全局事件，在事件触发时执行逻辑。

```csharp
protected override void RegisterEvents()
{
    // 标准模板 —— 订阅所有相关事件
    CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
    CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
    CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
    CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
    CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
    CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageRaided);
    CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
    CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, HourlyTickParty);
    CampaignEvents.BanditPartyRecruited.AddNonSerializedListener(this, OnBanditPartyRecruited);
    CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
    CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
    // ... 按需订阅
}
```

### 4.2 事件过滤模板

**铁律**：事件回调的第一件事必须是过滤 —— 判断"这跟我有没有关系"。

```csharp
// 模板 A：检查 destroyer / target 是否匹配
private void OnMobilePartyDestroyed(MobileParty destroyedParty, PartyBase destroyerParty)
{
    // ① 检查任务是否活跃
    if (!base.IsOngoing) return;
    
    // ② 检查是否玩家所为
    if (destroyerParty != PartyBase.MainParty) return;
    
    // ③ 检查是否是目标任务
    if (!_validPartiesList.Contains(destroyedParty)) return;
    
    // ④ 推进进度
    _destroyedPartyCount++;
    AddQuestStepLog();
}

// 模板 B：检查 Settlement / Hero 是否匹配
private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
{
    if (!base.IsOngoing) return;
    if (party != MobileParty.MainParty) return;
    if (settlement != _targetSettlement) return;
    
    // 触发阶段推进
    OnPlayerArrivedAtTarget();
}
```

**O(1) 过滤技巧**：
- 预存 `HashSet<MobileParty>` / `List<MobileParty>`，用 `Contains()` O(1) 判断
- 直接比较引用（`destroyerParty == PartyBase.MainParty`），不比较 ID
- 不相关立即 return，不做任何计算

### 4.3 常用的 12 个 CampaignEvents

| 事件 | 触发时机 | 常见用途 |
|------|---------|---------|
| `MobilePartyDestroyed` | 任意部队被消灭 | 讨伐任务进度 |
| `SettlementEntered` | 部队进入定居点 | 护送到达、侦察完成、交付物品 |
| `OnSettlementLeftEvent` | 部队离开定居点 | 阶段切换触发 |
| `WarDeclared` | 宣战 | 取消相关任务 |
| `OnClanChangedKingdomEvent` | 家族换阵营 | 取消/变更任务 |
| `VillageBeingRaided` | 村庄被劫掠 | 目标村庄失效、减免税收 |
| `MapEventStarted` | 大地图战斗开始 | 记录战斗参与者 |
| `HourlyTickPartyEvent` | 每小时每支部队 | 动态目标发现、AI 操控 |
| `DailyTickPartyEvent` | 每天每支部队 | 每日状态更新 |
| `BanditPartyRecruited` | 匪徒被招募 | 招募路线进度 |
| `OnSettlementOwnerChangedEvent` | 定居点易主 | 任务目标可能失效 |
| `OnNewIssueCreated` | 新 Issue 生成 | 资源互斥检测 |

### 4.4 事件注销

**Quest 完成/失败/取消时自动注销** — `QuestBase.FinalizeQuest()` 会清理所有通过 `AddNonSerializedListener` 注册的监听器。不需要手动注销。

---

## 五、经济与资源转移模式

### 5.1 金钱流转 — `GiveGoldAction.ApplyBetweenCharacters`

**铁律 4 红线**：**禁止业务层裸调 `Hero.ChangeHeroGold`**。转移类操作必须走 `GiveGoldAction`，含完整日志和事件广播。

```csharp
// ① 双方转移（守恒）：from → to
GiveGoldAction.ApplyBetweenCharacters(fromHero, toHero, amount);
// from = null → 凭空生成（Grant，合法但需标注虚空来源）
// to = null   → 凭空销毁（Sink，合法但需标注虚空去向）

// ② 奖励玩家（从虚空生成）
GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, rewardGold);

// ③ 玩家支付（向虚空销毁）
GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost);
```

**原版使用场景**：
| 场景 | from | to | 说明 |
|------|------|----|------|
| 任务奖励 | null | Hero.MainHero | Grant（虚空生成，合法） |
| 收税到账 | null | Hero.MainHero | Grant（HourlyTick 实时给钱） |
| 交税给领主 | Hero.MainHero | null | Sink（虚空销毁，合法） |
| 玩家买谷物 | Hero.MainHero | 商人 | Transfer（守恒） |
| 支付赎金 | Hero.MainHero | GangLeader | Transfer（守恒） |

### 5.2 物品转移 — `ItemRoster.AddToCounts`

**铁律 4 红线**：**禁止业务层裸调 `ItemRoster.AddToCounts`**。必须配对减方和加方（Transfer），或用 null 标注 Grant/Sink。

```csharp
// Transfer（守恒）：从玩家背包移到任务物品清单
playerParty.ItemRoster.AddToCounts(item, -count);
_targetSettlement.ItemRoster.AddToCounts(item, count); // 或 questItemList

// Grant（虚空生成，给玩家）
playerParty.ItemRoster.AddToCounts(item, count, null); // from=null

// Sink（虚空销毁，从玩家移除）
playerParty.ItemRoster.AddToCounts(item, -count, null); // to=null
```

**参考任务**：
| 任务 | 物品流 |
|------|--------|
| HeadmanNeedsGrain | 玩家购买谷物 → 交付给村庄（Transfer） |
| VillageNeedsTools | 玩家购买工具 → 交付（Transfer） |
| GangLeaderNeedsToOffloadStolenGoods | 赃物从帮派转移给玩家 → 玩家卖出（Transfer chain） |

### 5.3 部队成员转移

**用途**：从玩家部队移出/移入士兵。

```csharp
// 玩家部队 → 目标
MobileParty.MainParty.MemberRoster.AddToCounts(character, -count);
targetParty.MemberRoster.AddToCounts(character, count);

// Grant（招募到玩家部队）
MobileParty.MainParty.MemberRoster.AddToCounts(character, count, insertAtFront: false);
```

**参考任务**：
- LandlordTrainingForRetainers：借出士兵 → N 天后返还
- LordNeedsGarrisonTroops：移交士兵给驻军
- GangLeaderNeedsRecruits：交付新兵

### 5.4 关系变更 — `RelationshipChangeWithQuestGiver`

**用途**：结算时自动变更与任务发布者的关系。

```csharp
// QuestBase 内置属性，结算时框架自动处理
RelationshipChangeWithQuestGiver = 5;   // 正面
RelationshipChangeWithQuestGiver = -15; // 负面（背叛）

// 手动变更关系（与第三方 NPC）
ChangeRelationAction.ApplyPlayerRelation(targetHero, amount);
```

### 5.5 特质变更 — `TraitLevelingHelper.OnIssueSolvedThroughQuest`

**用途**：任务结算时变更玩家特质（Honor/Mercy/Generosity/Valor/Calculating）。

```csharp
// 正面特质
TraitLevelingHelper.OnIssueSolvedThroughQuest(questGiver,
    new Tuple<TraitObject, int>(DefaultTraits.Honor, 30));

// 负面特质
TraitLevelingHelper.OnIssueSolvedThroughQuest(questGiver,
    new Tuple<TraitObject, int>(DefaultTraits.Honor, -100));
```

**参考任务**：RevenueFarming（私吞 Honor -100，交出 Honor +30）、SnareTheWealthy

---

## 六、道德抉择与背叛模式

### 6.1 简单背叛 — `CompleteQuestWithBetrayal`

**场景**：玩家选择背叛任务发布者，走另一条结算路径。

```csharp
// 设置背叛结算
private void BetrayQuestGiver()
{
    _playerBetrayed = true;                          // 标记
    RelationshipChangeWithQuestGiver = -10;           // 关系惩罚
    TraitLevelingHelper.OnIssueSolvedThroughQuest(    // 特质惩罚
        QuestGiver, new Tuple<TraitObject, int>(DefaultTraits.Honor, -50));
    CompleteQuestWithBetrayal();
}
```

**参考任务**：RevenueFarming、RivalGangMovingIn

### 6.2 CounterOffer 机制（收买）

**场景**：第三方 NPC 提出条件，如果接受则背叛原 NPC。

**需要的 Issue 层钩子**：
```csharp
// IssueBase 中
public virtual Hero CounterOfferHero { get; protected set; }
public virtual TextObject IssueLordSolutionCounterOfferBriefByOtherNpc { get; }
public virtual TextObject IssueLordSolutionCounterOfferExplanationByOtherNpc { get; }
public virtual TextObject IssueLordSolutionCounterOfferAcceptByPlayer { get; }
public virtual TextObject IssueLordSolutionCounterOfferDeclineByPlayer { get; }

// AfterIssueCreation 钩子 — 设置 CounterOfferHero
protected override void AfterIssueCreation()
{
    CounterOfferHero = IssueOwner.CurrentSettlement.Notables
        .FirstOrDefault(x => x != IssueOwner);
}

// Lord Solution 条件
public override bool LordSolutionCondition(out TextObject explanation) { ... }
public override int NeededInfluenceForLordSolution => 20;
```

**完整 CounterOffer 对话流**（来自 FamilyFeud）：
```csharp
// 选择 Lord Solution → 等待 BeforeGameMenuOpenedEvent
// → CounterOfferHero 主动找你对话：
//   "{TARGET_NOTABLE}的侄子杀了我的族人！血债必须血偿！请大人允许我们复仇。"
// → 玩家选择:
//   Accept: LordSolutionConsequenceWithAcceptCounterOffer()
//     → 背叛原 NPC → Honor -50, 关系 -10 (原) +5 (CounterOffer)
//   Decline: LordSolutionConsequenceWithRefuseCounterOffer()
//     → 保持原承诺 → 消耗影响力 → 获得奖励
```

### 6.3 三选一关键时刻（Moment of Choice）

**场景**：在一个关键时刻让玩家做三方抉择（不是二元选择）。

**参考任务**：SnareTheWealthy

```
帮派头目安排你假扮护卫混入商队
  → 商队到达预设伏击点 → 帮派伏击部队出现
  → 三选一：
    A. 帮商队打帮派（背叛帮派头目）→ Honor++, 关系--
    B. 帮帮派抢商人（完成原任务）   → Honor--, CrimeRating++
    C. 两边都杀独吞全部货物          → 最大利益, 最大道德代价
```

**实现方式**：在 Mission 场景中通过 DialogFlow 弹出三选项，每个选项绑定不同的 Consequence 委托。

---

## 七、大地图部队 AI 操控模式

### 7.1 让部队在指定位置巡逻

```csharp
// 让部队围绕定居点巡逻
SetPartyAiAction.GetActionForPatrollingAroundSettlement(
    mobileParty, settlement);
mobileParty.Ai.SetDoNotMakeNewDecisions(true);  // ★ 锁定 AI，禁止自由决策
```

**参考任务**：MerchantNeedsHelpWithOutlaws、ExtortionByDeserters

**关键**：`SetDoNotMakeNewDecisions(true)` 防止部队的默认 AI 覆盖你的指令。

### 7.2 让部队前往指定定居点

```csharp
SetPartyAiAction.GetActionForGoingToSettlement(
    mobileParty, targetSettlement);
```

**参考任务**：EscortMerchantCaravan（商队自动导航）、HeadmanNeedsToDeliverAHerd（牲畜群）

### 7.3 动态生成部队

```csharp
// 创建商队
MobileParty caravan = MobileParty.CreateParty(
    "escort_merchant_caravan_" + questId,
    new PartyTemplateObject(),  // 或 null
    null);                      // 领袖 Hero（可选）

// 创建匪徒伏击部队
MobileParty bandits = BanditPartyComponent.CreateBanditParty(
    "quest_bandits_" + questId,
    questGiver.CurrentSettlement,
    banditClan,
    hideout,
    troopCount);
```

**参考任务**：EscortMerchantCaravan（商队 + 匪徒）、HeadmanNeedsToDeliverAHerd（牲畜群）

### 7.4 标记部队为任务专用

```csharp
mobileParty.IsCurrentlyUsedByAQuest = true;
```

**用途**：防止其他系统（如其他任务）干扰这支部队。`MerchantNeedsHelpWithOutlaws` 在 `HourlyTickParty` 中检查 `!mobileParty.IsCurrentlyUsedByAQuest` 防止重复占用。

---

## 八、对话流模式

### 8.1 标准接任务对话

```csharp
protected override void SetDialogs()
{
    // ① 接任务对话
    OfferDialogFlow = DialogFlow.CreateDialogFlow("issue_classic_quest_start")
        .NpcLine("{=...}好的，拜托你了...")
        .Condition(() => Hero.OneToOneConversationHero == QuestGiver)
        .Consequence(QuestAcceptedConsequences)  // ★ 委托链
        .CloseDialog();
    
    // ② 进行中对话（可选）
    DiscussDialogFlow = DialogFlow.CreateDialogFlow("issue_discuss")
        .NpcLine("{=...}事情办得怎么样了？")
        .Condition(() => Hero.OneToOneConversationHero == QuestGiver)
        .CloseDialog();
}
```

### 8.2 注册额外 DialogFlow

**用途**：除了 Offer/Discuss，注册自定义对话场景（如与任务目标 NPC 对话）。

```csharp
private void InitializeQuestDialogs()
{
    // 每个额外的对话场景注册为一个独立的 DialogFlow
    Campaign.Current.ConversationManager.AddDialogFlow(GetCulpritDialogFlow());
    Campaign.Current.ConversationManager.AddDialogFlow(GetTargetNotableDialogFlow());
    Campaign.Current.ConversationManager.AddDialogFlow(GetDaughterPersuadedDialog());
    // ... 最多 10 个（FamilyFeud）
}

private DialogFlow GetCulpritDialogFlow()
{
    return DialogFlow.CreateDialogFlow("family_feud_culprit_talk")
        .NpcLine("{=...}你就是被派来保护我的人？")
        .Condition(() => Hero.OneToOneConversationHero == _culprit
            && !_culpritJoinedPlayerParty)
        .PlayerLine("{=...}跟我走，我会保护你")
        .Consequence(() => {
            _culpritJoinedPlayerParty = true;
            _culprit.SetHeroEncyclopediaTextAndLinks(null);
        })
        .CloseDialog();
}
```

### 8.3 Persuasion 说服接管对话

```csharp
// 创建 PersuasionTask 接管对话引擎
_task = new PersuasionTask(Hero.OneToOneConversationHero)
{
    ReservationType = PersuasionTask.PersuasionReservationType.Issue,
    Difficulty = 5,  // 越高越难
    BlockArgument = new PersuasionBlockArgument(...),
    // ... 设置说服参数
};

// 在 DialogFlow 中触发说服
.Consequence(() => {
    Campaign.Current.ConversationManager.StartPersuasion(
        PersuasionDifficulty.Medium,
        _task,
        onSuccess: () => { _isDaughterPersuaded = true; },
        onFail: () => { _acceptedDaughtersEscape = true; });
})
```

**参考任务**：NotableWantsDaughterFound（说服女儿，Diff=5）、FamilyFeud（说服 Notable，Diff=4）

---

## 九、资源互斥与冷却模式

### 9.1 藏身处互斥 — `BusyHideouts`

```csharp
// 构造函数中标记藏身处被占用
Campaign.Current.BusyHideouts.Add(relatedHideout.Settlement);

// Issue 失效时释放（框架自动处理或手动）
// IssueFinalized() → 清理
```

**参考任务**：
- MerchantNeedsHelpWithOutlaws（占用藏身处，防止 NearbyBanditBase 冲突）
- NearbyBanditBase（检查 `BusyHideouts` 是否已有该藏身处）

### 9.2 共享冷却 — `IssueCoolDownData`

```csharp
// 三个关卡任务共享冷却（IssuesCampaignBehavior 内部）
// SnareTheWealthy / EscortMerchantCaravan / CaravanAmbush
// 完成任何一个 → 三个全部进入 30 天冷却
```

### 9.3 部队任务标记 — `IsCurrentlyUsedByAQuest`

```csharp
// 标记部队已被任务占用
mobileParty.IsCurrentlyUsedByAQuest = true;

// 其他任务在扫描可用的匪徒时跳过
if (mobileParty.IsCurrentlyUsedByAQuest) return;
```

---

## 十、Mission 场景模式

### 10.1 创建任务专属 Mission

**场景**：玩家进入特定场景（巷战/村斗）执行任务。

**参考任务**：FamilyFeud（alley_2 场景）、NotableWantsDaughterFound（村庄场景）

**关键步骤**：
1. 在 `SettlementEntered` 事件中检测条件
2. 使用 `EncounterManager.StartSettlementEncounter` 触发 Mission
3. 在 Mission 中放置 Agent（动态创建的 NPC）
4. 监听 Mission 事件（Agent 死亡/击倒/对话触发）
5. 根据 Mission 结果推进 Quest 状态

### 10.2 Mission 中 Agent 剧情保护

```csharp
// 防止剧情 NPC 在开打前被误杀
_daughterAgent.SetMortality(immortal: true);
_rogueAgent.SetMortality(immortal: true);
Agent.SetMortality(immortal: true);  // 对自己也生效
```

**注意**：剧情保护应在该 NPC 需要被杀死时取消（如战斗结束后）。

### 10.3 玩家移入 SpectatorTeam

**用途**：让玩家坐看剧情战斗（不参与战斗）。

```csharp
// FamilyFeud 中使用：玩家选择出卖犯人后
// 玩家被移到 SpectatorTeam → 旁观犯人被群殴
Mission.Current.SetPlayerTeamToSpectator();
// 或
Mission.Current.PlayerTeam = Mission.Current.SpectatorTeam;
```

---

## 十一、综合范本：原版任务的设计骨架

### 11.1 任务三层的标准模板

```csharp
// ══════════════════════════════════════════════════════
// 第 1 层：CampaignBehavior — 触发调度器
// ══════════════════════════════════════════════════════
public class MyNewIssueBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.OnCheckForIssueEvent.AddNonSerializedListener(this, OnCheckForIssue);
    }

    private void OnCheckForIssue(Hero hero)
    {
        // ① NPC 类型 → ② 冷却 → ③ 前置条件 → ④ 注册
        if (!ConditionsHold(hero)) return;
        
        Campaign.Current.IssueManager.AddPotentialIssueData(hero,
            new PotentialIssueData(
                (pid, h) => new MyNewIssue(h, /* 参数 */),
                typeof(MyNewIssue),
                IssueBase.IssueFrequency.Common));
    }

    // ═══════════════════════════════════════════════
    // 第 2 层：Issue — 世界中的问题
    // ═══════════════════════════════════════════════
    public class MyNewIssue : IssueBase
    {
        [SaveableField(10)] private int _customData;

        public MyNewIssue(Hero owner, int data)
            : base(owner, CampaignTime.DaysFromNow(15f))
        {
            _customData = data;
        }

        // 必须实现
        public override TextObject IssueBriefByIssueGiver => new TextObject("{=...}");
        public override TextObject IssueQuestSolutionExplanationByIssueGiver => new TextObject("{=...}");
        public override TextObject IssueQuestSolutionAcceptByPlayer => new TextObject("{=...}");
        public override TextObject Title => new TextObject("{=...}");
        public override TextObject Description => new TextObject("{=...}");
        public override IssueFrequency GetFrequency() => IssueFrequency.Common;
        public override bool IssueStayAliveConditions() => IssueOwner?.IsAlive == true;
        protected override bool CanPlayerTakeQuestConditions(...) { /* ... */ return true; }
        protected override void HourlyTick() { }
        protected override QuestBase GenerateIssueQuest(string questId)
            => new MyNewQuest(questId, IssueOwner, _customData);

        // 可选重写
        protected override int RewardGold => 500 + (int)(1000 * IssueDifficultyMultiplier);
        public override bool IsThereAlternativeSolution => true;
        public override bool IsThereLordSolution => false;
        protected override int AlternativeSolutionBaseNeededMenCount => 5;
        protected override int AlternativeSolutionBaseDurationInDaysInternal => 6;

        // IssueEffect 惩罚
        protected override float GetIssueEffectAmountInternal(IssueEffect effect)
        {
            if (effect == DefaultIssueEffects.SettlementSecurity) return -0.5f;
            return 0f;
        }
    }

    // ═══════════════════════════════════════════════
    // 第 3 层：Quest — 玩家的任务
    // ═══════════════════════════════════════════════
    public class MyNewQuest : QuestBase
    {
        [SaveableField(10)] private int _targetCount;
        [SaveableField(20)] private int _currentCount;
        [SaveableField(30)] private JournalLog _progressLog;

        public MyNewQuest(string questId, Hero giver, int target)
            : base(questId, giver, CampaignTime.DaysFromNow(20f), 500)
        {
            _targetCount = target;
            SetDialogs();
            InitializeQuestOnCreation();  // ★ 自动调 RegisterEvents + OnStartQuest
        }

        protected override void SetDialogs()
        {
            OfferDialogFlow = DialogFlow.CreateDialogFlow("issue_classic_quest_start")
                .NpcLine("{=...}拜托你了...").Condition(() => Hero.OneToOneConversationHero == QuestGiver)
                .Consequence(QuestAcceptedConsequences).CloseDialog();
        }

        protected override void RegisterEvents()
        {
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        }

        protected override void OnStartQuest()
        {
            _progressLog = AddDiscreteLog(startLog, progressLog, 0, _targetCount);
            AddTrackedObject(QuestGiver);
        }

        private void QuestAcceptedConsequences()
        {
            StartQuest();
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (!base.IsOngoing) return;
            if (destroyer != PartyBase.MainParty) return;
            if (!IsTarget(party)) return;

            _currentCount++;
            _progressLog.UpdateCurrentProgress(_currentCount);
            MBInformationManager.AddQuickInformation(new TextObject("{=...}"));

            if (_currentCount >= _targetCount) SuccessConsequences();
        }

        private void SuccessConsequences()
        {
            RelationshipChangeWithQuestGiver = 5;
            GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, RewardGold);
            CompleteQuestWithSuccess();
        }

        private void OnWarDeclared(IFaction f1, IFaction f2)
        {
            if (f1 == QuestGiver.MapFaction || f2 == QuestGiver.MapFaction)
                CompleteQuestWithCancel(new TextObject("{=...}"));
        }

        protected override void HourlyTick() { }
        protected override void InitializeQuestOnGameLoad() { SetDialogs(); }
    }
}
```

### 11.2 设计诀窍速查

| 需求 | 做法 | 参考 |
|------|------|------|
| 动态目标列表 | `HourlyTickParty` + `validPartiesList` | Outlaws |
| 任务期间实时给奖励 | `HourlyTick` + `GiveGoldAction` | RevenueFarming |
| 防止玩家反复刷 | IssueCoolDownData + 共享冷却 | SnareTheWealthy |
| 支持多完成路径 | 多个 counter + 统一 progress getter | Outlaws |
| 不同路径不同结算文本 | `if/else` counter 分布 → 不同 `AddLog` | Outlaws |
| 任务中注入随机事件 | 进度阈值 + `GameMenu.SwitchToMenu` | RevenueFarming |
| 防止多个任务抢资源 | `BusyHideouts.Add()` / `IsCurrentlyUsedByAQuest` | Outlaws + BanditBase |
| 超时不直接失败 | `OnBeforeTimedOut` + `ShowQuestResolvePopUp` | RevenueFarming |
| 多种方式完成 | Alternative/Lord Solution 可选 | FamilyFeud |
| LLM 生成叙事文本 | 替代 TextObject 静态文本 | 我们的 CommissionNarrative |
| NPC 记忆任务历史 | HeroMemory 替代冰冷 Relation+5 | SingNpcMemorySystem |

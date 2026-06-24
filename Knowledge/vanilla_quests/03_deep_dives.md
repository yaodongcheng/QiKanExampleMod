# 03 — 精选深度分析

> 覆盖 15 个机械最独特的原版任务。前 5 个（★）在 [../quest_example.md](../quest_example.md) 已有完整调用链分析，此处提供摘要 + 链接。后 10 个为新增深度分析。

---

## 目录

1. [★ MerchantNeedsHelpWithOutlaws — 清剿匪徒](#1-merchantneedshelpwithoutlaws--清剿匪徒)
2. [★ NotableWantsDaughterFound — 寻找女儿](#2-notablewantsdaughterfound--寻找女儿)
3. [★ FamilyFeud — 家族世仇](#3-familyfeud--家族世仇)
4. [★ RevenueFarming — 包税权](#4-revenuefarming--包税权)
5. [★ EscortMerchantCaravan — 护送商队](#5-escortmerchantcaravan--护送商队)
6. [TheSpyParty — 间谍潜入（布尔推理）](#6-thespyparty--间谍潜入)
7. [SnareTheWealthy — 诱捕富商（三方道德抉择）](#7-snarethewealthy--诱捕富商)
8. [ProdigalSon — 浪子回头（多路径营救）](#8-prodigalson--浪子回头)
9. [BettingFraud — 竞技场欺诈调查](#9-bettingfraud--竞技场欺诈调查)
10. [RivalGangMovingIn — 帮派入侵](#10-rivalgangmovingin--帮派入侵)
11. [ScoutEnemyGarrisons — 侦察敌方驻军](#11-scoutenemygarrisons--侦察敌方驻军)
12. [LordWantsRivalCaptured — 活捉仇敌](#12-lordwantsrivalcaptured--活捉仇敌)
13. [LandLordNeedsManualLaborers — 劳工需求](#13-landlordneedsmanuallaborers--劳工需求)
14. [GangLeaderNeedsWeapons — 武器收集与守卫躲避](#14-gangleaderneedsweapons--武器收集与守卫躲避)
15. [LandlordTrainingForRetainers — 训练家丁](#15-landlordtrainingforretainers--训练家丁)

---

## 1. MerchantNeedsHelpWithOutlaws — 清剿匪徒

> **完整分析**：[../quest_example.md#二案例1-merchantneedshelpwithoutlaws](../quest_example.md#二案例1-merchantneedshelpwithoutlaws)

### 机械签名

| 属性 | 值 |
|------|-----|
| **核心循环** | HourlyTickParty 扫描 → 加入目标列表 → 玩家击杀 → 进度+1 |
| **动态目标** | 非固定列表，每小时扫描附近匪徒加入 |
| **多路径** | 消灭 + Roguery 招募 |
| **AI 操控** | `SetPartyAiAction` + `SetDoNotMakeNewDecisions(true)` 锁定匪徒 |
| **事件数** | 9 个（最多之一） |
| **资源互斥** | `BusyHideouts.Add()` |

### 可复用诀窍

1. **动态目标发现** = HourlyTick + 条件过滤 + 列表追加 + 地图追踪
2. **多路径进度** = 多个 counter → 统一 progress getter → 不同结算文本
3. **AI 锁定** = 先设 AI action + 再禁自由决策（两步必须同时）

---

## 2. NotableWantsDaughterFound — 寻找女儿

> **完整分析**：[../quest_example.md#三案例2-notablewantsdaughterfound](../quest_example.md#三案例2-notablewantsdaughterfound)

### 机械签名

| 属性 | 值 |
|------|-----|
| **核心循环** | 找线索 → 定位恶棍 → Mission 战斗 → Persuasion 说服 |
| **动态 NPC** | `HeroCreator.CreateSpecialHero` 创建女儿 + 恶棍 |
| **线索系统** | `_villagesAndAlreadyVisitedBooleans` 状态机 |
| **技能捷径** | Scout ≥ 150×难度 → 跳过搜索 |
| **说服** | PersuasionTask, Difficulty = 5 |
| **多结局** | 说服成功/女儿逃跑/被抓/村庄被劫 |

### 可复用诀窍

1. **线索状态机** = Dictionary<Village, bool> 标记访问状态 + Scout 检定跳过
2. **动态 NPC 关系** = `hero.Father = questGiver` → 对话系统自动识别
3. **Mission Agent 剧情保护** = `SetMortality(immortal: true)` 防误杀

---

## 3. FamilyFeud — 家族世仇

> **完整分析**：[../quest_example.md#四案例3-familyfeud](../quest_example.md#四案例3-familyfeud)

### 机械签名

| 属性 | 值 |
|------|-----|
| **核心循环** | 找犯人 → 护送 → Mission 巷战 → 三选一道德抉择 |
| **DialogFlow 数** | 10 个（最多） |
| **Lord Solution** | 唯一有此功能的任务 |
| **CounterOffer** | `CounterOfferHero` + `AfterIssueCreation` |
| **Mission** | alley_2 场景 + 三方 Agent + Persuasion + SpectatorTeam |
| **结算路径** | 5 种（成功/背叛/犯人死/玩家倒/超时） |

### 可复用诀窍

1. **CounterOffer 完整链** = `CounterOfferHero` 属性 + `AfterIssueCreation` 钩子 + `LordSolutionCondition`
2. **SpectatorTeam** = 让玩家旁观剧情战斗
3. **多 DialogFlow** = 每个对话场景独立注册，按条件触发

---

## 4. RevenueFarming — 包税权

> **完整分析**：[../quest_example.md#五案例4-revenuefarming](../quest_example.md#五案例4-revenuefarming)

### 机械签名

| 属性 | 值 |
|------|-----|
| **核心循环** | 进入村庄 → 开始收税 → HourlyTick 挂机 → 随机事件 → 交付/私吞 |
| **进度类型** | 挂机型（时间驱动，非事件驱动） |
| **辅助结构** | `RevenueVillage` 类（TargetAmount / CollectedAmount / HourlyGain / EventOccurred） |
| **随机事件** | `VillageEvent` + 30% 进度触发 |
| **奖励方式** | 实时给金币（非结算一次性） |
| **道德抉择** | 交出（Honor+30）vs 私吞（Honor-100, CrimeRating+45） |
| **超时处理** | `OnBeforeTimedOut` → `ShowQuestResolvePopUp`（不直接失败） |

### 可复用诀窍

1. **挂机进度** = HourlyTick + 实时 GiveGold + 进度阈值触发事件
2. **RevenueVillage 模式** = 目标量 / 小时速率 / 收集量 / 事件标记
3. **超时弹出框** = `OnBeforeTimedOut` + `doNotResolveTheQuest = true` + `ShowQuestResolvePopUp`

---

## 5. EscortMerchantCaravan — 护送商队

> **完整分析**：[../quest_example.md#六案例5-escortmerchantcaravan](../quest_example.md#六案例5-escortmerchantcaravan)

### 机械签名

| 属性 | 值 |
|------|-----|
| **核心循环** | 商队导航 → 匪徒伏击生成 → 消灭匪徒 → 到达定居点 |
| **动态部队** | 生成商队 + 匪徒部队 |
| **匪徒 AI** | 跟踪 _followDuration 天后主动攻击 |
| **共享冷却** | 与 SnareTheWealthy / CaravanAmbush 三合一 |

### 可复用诀窍

1. **动态生成商队** = `MobileParty.CreateParty` + AI 导航
2. **伏击生成** = 基于玩家兵力计算匪徒数量 + 跟随倒计时
3. **共享冷却** = IssueCoolDownData 中关联多个 Issue 类型

---

## 6. TheSpyParty — 间谍潜入

**原版唯一的布尔推理系统**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | 拥有城镇的 Lord |
| **目标** | 在比武大会中找出间谍 |
| **时限** | 5 天（最短！） |
| **频率** | Rare |
| **所属 DLL** | SandBox.dll |

### 核心数据结构 — SuspectNpc

```csharp
// 每个参赛者是一个 SuspectNpc
public struct SuspectNpc
{
    public CharacterObject Character;
    public bool Hair;       // 间谍有/没有特殊发型
    public bool Beard;      // 间谍有/没有胡子
    public bool Markings;   // 间谍有/没有纹身
    public bool BigSword;   // 间谍佩大剑
}

// 真正的间谍由 Quest 生成时随机确定一个 SuspectNpc
// 玩家通过与 NPC 交谈逐步获得 4 个 bool 的真值
```

### 游戏流程

```
【阶段 A: 收集线索】
参加比武大会 → 与多个 NPC 交谈
每个 NPC 透露 4 个 bool 中的一个：
  "我听说间谍有特殊发型"        → Hair = true/false
  "不像有胡子的人"              → Beard = true/false
  "身上有纹身"                  → Markings = true/false
  "佩着一把大剑"                → BigSword = true/false

【阶段 B: 画像匹配】
收集足够线索后 → 将 4 个 bool 与所有比赛选手比对
  → 匹配度最高的那个 = 嫌疑人

【阶段 C: 指认】
上前指认:
  → 指认正确 → 进入战斗（vs 间谍）
  → 指认错误 → 关系惩罚 + 真正的间谍逃走
```

### 设计要点

1. **4-bool 推理**虽简单但有效——不是因为逻辑难，而是因为线索藏在 NPC 对话里，玩家要自己记住/记录
2. **时限极短**（5 天）——如果比武大会过期还没找出间谍，任务失败
3. **这是原版唯一有"玩家靠自己推理"元素的任务**（其他任务都是做什么/去哪/杀谁）
4. **情报来自渠道**的设计完全符合我们的[叙事设计铁律](../../plans/rules/narrative-design.md)

### 对我们 Commission 的启示

- `SuspectNpc` 的 bool 匹配可以被泛化为 **"线索→推理→指认"三层结构**
- LLM 可以生成任意数量的线索 bool（不限于 4 个）
- 这个模式可以直接移植到侦探类委托中

---

## 7. SnareTheWealthy — 诱捕富商

**原版道德后果最复杂的支线**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | 城镇 GangLeader |
| **目标** | 假扮护卫混入商队，帮帮派抢劫 |
| **时限** | 20天 |
| **频率** | Rare |
| **所属 DLL** | SandBox.dll |
| **背叛** | ✓（三选一关键时刻） |

### 游戏流程

```
【阶段 A: 卧底潜入】
帮派头目告诉你 → "有个肥羊商人要出发了"
  → 去找商人 → 假装应聘护卫
  → 商人给你报酬日结（和 EscortMerchantCaravan 类似的开局）

【阶段 B: 跟随商队】
  → 你作为"护卫"跟随商队出发
  → 大地图保护商队（表面工作）
  → 实际上你的雇主是帮派头目

【阶段 C: 三选一关键时刻 ⚡】
商队到达预设伏击点 → 帮派伏击部队出现！

  ┌─ 选项 A: 帮商队打帮派 ——————————————————
  │  背叛帮派头目
  │  · 战斗: 你 + 商队 vs 帮派
  │  · 结果: Honor++, 商人感激（给报酬）
  │  · 后果: GangLeader关系 -10
  │
  ├─ 选项 B: 帮帮派抢商人 ——————————————————
  │  完成任务
  │  · 战斗: 你 + 帮派 vs 商队
  │  · 结果: Honor--, CriminalRating++
  │  · 后果: 获得帮派承诺的报酬
  │
  └─ 选项 C: 两边都杀 ————————————————————
     背叛所有人
     · 战斗: 你 vs 帮派 + 商队（三方混战）
     · 结果: Honor --(最大), CriminalRating ++(最大)
     · 收益: 独吞全部货物（最大利益）
```

### 设计要点

1. **假身份"护卫"**让玩家有代入感——你口头上是保护商队，实际心怀鬼胎
2. **三选一而非二选一**：有了"两边都杀"的选项，玩家体验远超简单的"帮A还是帮B"
3. **每个选择都有不同的战斗配置**：帮商队 → 2vN（你+商队 vs 帮派），帮帮派 → 2vN（你+帮派 vs 商队），两边都杀 → 1vAll
4. **共享冷却**：与 EscortMerchantCaravan / CaravanAmbush 三合一冷却（防止玩家同时刷护送+抢劫）

### 对我们 Commission 的启示

- 假身份机制 = LLM 叙事可以生成"卧底"委托
- 三选一关键时刻 = 可以用 `ComplicationTable` 在任务中途注入道德抉择
- 不同选择不同战斗配置 = 同一 Mission 中根据选择切换 Team 分配

---

## 8. ProdigalSon — 浪子回头

**多路径营救 + Persuasion + 护送三合一**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | Lord（儿子被扣的领主） |
| **目标** | 赎出因赌博欠债被扣留的年轻族人 |
| **时限** | 20天(Quest) / 45天(Issue) |
| **频率** | Rare |
| **所属 DLL** | SandBox.dll |

### 三条解决路径

```
领主儿子欠 GangLeader 赌债 → 被扣留

路径 A: 付赎金（最简单）
  玩家出钱赎回 → 带儿子回家 → 领主报销（可能部分）

路径 B: 说服威胁（Persuasion）
  找 GangLeader 谈判:
    成功 → GangLeader 放人（可能减免部分债务）
    失败 → GangLeader 拒绝 → 只能选 A 或 C

路径 C: 暴力解救（Roguery / 战斗）
  潜入 GangLeader 据点 → Mission 巷战 → 救出儿子
    成功 → 儿子获救，但 GangLeader 关系暴跌
    失败 → 儿子可能受伤/被杀
```

### 存档字段（推测）

```csharp
Hero _targetHero;          // 被扣留的儿子（动态创建或已有 Hero）
Hero _gangLeader;          // 扣留人的帮派头目
Settlement _targetSettlement; // 扣留地点
int _ransomAmount;         // 赎金金额
bool _isRescued;           // 是否已救出
bool _isPaid;              // 是否已付赎金
bool _isPersuaded;         // 是否说服成功
```

### 设计要点

1. **三条路径覆盖"给钱/说服/打"三种玩家偏好**——没有哪个路径是唯一正确的
2. **每个路径有不同的风险和回报**：付钱最安全但最贵，说服最省但可能失败，暴力最危险但最英雄
3. **成功后的护送阶段**：救出儿子后还需要护送到领主城堡（可能遭遇追兵）

---

## 9. BettingFraud — 竞技场欺诈调查

**CounterOffer + 竞技场系统集成**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | 城镇 Merchant |
| **目标** | 调查竞技场赌注欺诈 |
| **时限** | 20天 |
| **频率** | Rare |
| **所属 DLL** | CampaignSystem.dll |

### 核心机制

```
① 进入竞技场 → 参加多场比赛
② 观察异常 → 某个选手的赔率和实力不匹配
③ 与 NPC 交谈收集证据 → 多个证人提供线索
④ CounterOffer: 赌场老板（_counterOfferNotable）贿赂你
   · 接受 → _counterOfferConversationDone = true
      → 背叛原任务发布者
   · 拒绝 → 继续调查
⑤ 指认嫌疑人 → 战斗
```

### CounterOffer 机制详解

```csharp
// Issue 层设置 CounterOffer
[SaveableField(100)] private Hero _counterOfferNotable;
[SaveableField(40)] private bool _counterOfferConversationDone;

// CounterOfferNotable 是城镇中的另一个 Notable（赌场老板）
// 当玩家接近真相时 → CounterOfferNotable 主动对话
// 接受 → 任务变为背叛状态 → CompleteQuestWithBetrayal()
```

### 设计要点

1. **CounterOffer 不是 Lord Solution 的专属**——BettingFraud 证明了 CounterOffer 可以在 Quest 执行中途触发
2. `_fixedTournamentCount` 存档字段控制竞技场比赛轮数
3. `_minorOffensiveCount` 记录玩家在调查中冒犯了多少人（影响最终关系）

---

## 10. RivalGangMovingIn — 帮派入侵

**Mission 巷战 + 背叛选项**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | GangLeader |
| **目标** | 帮帮派头目清除入侵的敌对帮派 |
| **时限** | 20天(Quest) / 30天(Issue) |
| **频率** | Common |
| **所属 DLL** | SandBox.dll |
| **背叛** | ✓（加入敌对帮派） |

### 核心机制

```
① 在定居点找到敌对帮派成员
② 进入 Mission 巷战（alley/backstreet 场景）
③ 带领本地帮派成员 vs 敌对帮派
④ 战斗中 → 敌对帮派头目提出收买：
   "加入我们，给你双倍报酬！"
⑤ 选择:
   · 拒绝 → 完成战斗 → 原任务结算
   · 接受 → 背叛 → 加入敌对帮派一方 → 关系重置
```

**与 FamilyFeud 的区别**：
- FamilyFeud: 三方会谈 → 三选一 → 后续流程有 10 个 DialogFlow
- RivalGangMovingIn: 直接开打 → 战斗中收买 → 只有 2 个结局

---

## 11. ScoutEnemyGarrisons — 侦察敌方驻军

**多目标追踪 + 地图箭头**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | Lord |
| **目标** | 侦察 3 个敌方定居点的驻军 |
| **时限** | 30天 |
| **频率** | Common |
| **所属 DLL** | CampaignSystem.dll |

### 核心机制

```csharp
// 存档字段
[SaveableField(10)] private Settlement _settlement1;
[SaveableField(20)] private Settlement _settlement2;
[SaveableField(30)] private Settlement _settlement3;

// 3 个目标从敌国定居点中随机选择
// 每个目标需要玩家亲自到达（SettlementEntered 触发）
// 到达即算侦察完成，不需要战斗
```

### 关键 API 组合

```csharp
// ① 追踪 3 个目标
AddTrackedObject(_settlement1);
AddTrackedObject(_settlement2);
AddTrackedObject(_settlement3);

// ② 地图箭头（从玩家位置指向目标）
QuestHelper.AddMapArrowFromPointToTarget(
    MobileParty.MainParty.Position2D,
    _settlement1.Position2D);

// ③ 到达检测
private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
{
    if (party != MobileParty.MainParty) return;
    if (settlement == _settlement1 || settlement == _settlement2 || settlement == _settlement3)
    {
        _scoutedCount++;
        RemoveTrackedObject(settlement); // 已侦察，移除追踪
        // 如果王国有变 → 可能减少目标数
    }
}
```

### 设计要点

1. **最简单的多目标追踪案例**：AddTrackedObject × 3 + 到达即完成
2. **地图箭头是此任务的标志性表现**：`QuestHelper.AddMapArrowFromPointToTarget`
3. **动态目标数**：如果定居点被征服 → 目标可能从 3 变 2

---

## 12. LordWantsRivalCaptured — 活捉仇敌

**必须活捉（不能杀）的讨伐任务**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | Lord |
| **目标** | 活捉指定敌方领主 |
| **时限** | 30天 |
| **频率** | Rare |
| **所属 DLL** | CampaignSystem.dll |

### 核心机制

```csharp
// 追踪的是 Hero 而非 MobileParty
AddTrackedObject(_rivalHero);  // ★ 地图追踪敌方领主

// 活捉检测
private void OnPrisonerTaken(PartyBase prisoner, PartyBase capturer)
{
    if (capturer != PartyBase.MainParty) return;
    if (prisoner.Leader == _rivalHero)
    {
        _isCaptured = true;
        // 检查 Hero.IsPrisoner 确认存活
        if (_rivalHero.IsPrisoner)
            SuccessConsequences();
        else
            CompleteQuestWithFail(); // 打死了 → 失败
    }
}
```

### 设计要点

1. **追踪的是 Hero 不是 Party**：`AddTrackedObject(Hero)` 直接追踪人物
2. **不能杀只能活捉**：`Hero.IsPrisoner` 检查是关键——打死算失败
3. **交付俘虏**：俘虏在玩家部队中 → 找 QuestGiver 对话 → 俘虏转移

---

## 13. LandLordNeedsManualLaborers — 劳工需求

**俘虏驱动（非招募驱动）的交付任务**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | Headman |
| **目标** | 俘虏 N 个士兵交付给地主当劳工 |
| **时限** | 20天(Quest) / 25天(Issue) |
| **频率** | Rare |
| **所属 DLL** | CampaignSystem.dll |

### 核心机制

```
① 去大地图找战斗 → 俘虏敌方士兵
② 俘虏必须是健康的（!isWounded）
③ 带回村庄交付 → 俘虏从 PrisonRoster 移除
④ 交付的俘虏 → 转为地主村庄的民兵（Militia）
```

**与 GangLeaderNeedsRecruits 的关键区别**：
- Recruits：招募新兵交付（MemberRoster）
- ManualLaborers：**俘虏**交付（PrisonRoster）

### 对 Commission 的启示

- 区分"需要招募的兵"vs"需要俘虏的兵"，这是两个完全不同的玩家行为路径
- 前者鼓励去村庄招募 → 后者鼓励去大地图打战

---

## 14. GangLeaderNeedsWeapons — 武器收集与守卫躲避

**带守卫躲避判定的物品收集任务**

### 存档字段（来自 auto-generation metadata）

```csharp
[SaveableField(10)] private int _randomForRequiredWeaponClass;  // WeaponClass 索引
[SaveableField(20)] private int _requestedWeaponAmount;          // 需求数量
[SaveableField(30)] private bool _playerDodgedGuards;           // ★ 是否避开了守卫
[SaveableField(40)] private int _collectedItemAmount;            // 已收集数量
[SaveableField(50)] private bool _lowCrimeRatingWillBeApplied;   // 低犯罪惩罚
[SaveableField(60)] private bool _highCrimeRatingWillBeApplied;  // 高犯罪惩罚
[SaveableField(71)] private List<ItemObject> _weaponsThatGuardTook; // 被守卫没收的武器
```

### 核心机制

```
① GangLeader 指定需要的武器类别（随机 WeaponClass）
② 玩家收集该类别的武器（购买/打造/掠夺）
③ 收集到一定数量后 → 运送给 GangLeader
④ 运送过程中有两个路径：
   正门: 可能被守卫检查 → _playerDodgedGuards 判定
     · 成功躲避 → 安全交货
     · 被抓住 → _weaponsThatGuardTook 没收部分武器 + CrimeRating
   后巷: 安全但帮派抽成更高？
```

### 设计要点

1. **守卫躲避机制**：`_playerDodgedGuards` 布尔判定——只有 GuardedSettlement 有此机制
2. **被没收的武器列表**：`_weaponsThatGuardTook` 记录哪些武器被没收了（用于后续追回）
3. **高低犯罪分级**：`_lowCrimeRatingWillBeApplied` vs `_highCrimeRatingWillBeApplied`

---

## 15. LandlordTrainingForRetainers — 训练家丁

**部队借出 + 定时返还 + XP 奖励**

### 任务概述

| 属性 | 值 |
|------|-----|
| **发布者** | Headman |
| **目标** | 借出 N 个士兵给地主训练，等待 N 天后取回 |
| **时限** | 20天(Quest) / 25天(Issue) |
| **频率** | Rare |
| **所属 DLL** | CampaignSystem.dll |

### 核心机制

```
① 选择借出哪些士兵（从 MemberRoster 中选）
② 士兵从玩家部队移除 → 标记给地主
③ 设置 _returnTime = N 天后
④ 等待期间：
   · 士兵在地主那里（不可用）
   · 如果地主死亡/村庄被毁 → 任务失败，士兵损失
⑤ 到达 _returnTime → 士兵返回
   · 士兵获得 XP（可能升级）
   · 部分士兵可能受伤
```

### 设计要点

1. **借出不是永失**：士兵会回来——这与 LordNeedsGarrisonTroops（永久移交）不同
2. **定时返还**：`_returnTime` 控制返回时间——如果玩家在返还时间没回来取，地主会一直留着
3. **风险**：地主或村庄在期间被毁 → 士兵全损 → 任务失败

### 对我们 Commission 的启示

- "借出士兵 → 定时收回 → 士兵获得 XP"的循环 = 一个很好的训练类委托模板
- 与 Alternative Solution 的计算公式类似（成功率/伤亡基于技能）

---

## 深度分析横向对比

| 维度 | Outlaws | Daughter | FamilyFeud | Revenue | Caravan | SpyParty | Snare | Prodigal |
|------|---------|----------|------------|---------|---------|----------|-------|----------|
| **独特卖点** | 动态目标 | 动态NPC+线索 | Lord+CtrOffer | 挂机 | 匪徒AI | 布尔推理 | 三选一 | 三路径 |
| **DialogFlow数** | 2 | 8 | 10 | 2 | 2 | ~5 | ~5 | ~5 |
| **Persuasion** | ✗ | ✓ | ✓ | ✗ | ✗ | ✗ | ✗ | ✓ |
| **Mission** | ✗ | ✓ | ✓ | ✗ | ✗ | ✗ | ✓ | ✓ |
| **CounterOffer** | ✗ | ✗ | ✓ | ✗ | ✗ | ✓ | ✗ | ✗ |
| **动态NPC** | ✗ | ✓ | ✓ | ✗ | ✗ | ✗ | ✗ | ✓ |
| **随机事件** | ✗ | ✗ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ |
| **技能检定捷径** | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |

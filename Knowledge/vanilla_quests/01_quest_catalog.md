# 01 — 40+ 任务全目录

> 每个任务一张"快速参考卡"。含目标、触发条件、核心机制、关键存档字段、事件订阅、可复用接口。

---

## 目录

1. [分类总表](#分类总表)
2. [按玩法类型索引](#按玩法类型索引)
3. [快速参考卡](#快速参考卡)
   - [A. 村庄要人任务 (13)](#a-村庄要人任务)
   - [B. 城镇工匠/商人任务 (7)](#b-城镇工匠商人任务)
   - [C. 帮派头目任务 (6)](#c-帮派头目任务)
   - [D. 领主/贵族任务 (13)](#d-领主贵族任务)
   - [E. 通用/全局任务 (4)](#e-通用全局任务)

---

## 分类总表

| # | 任务名 | 发布者 | 类型 | 频率 | Alt | Lord | 背叛 | 复杂度 | DLL |
|---|--------|--------|------|------|-----|------|------|--------|-----|
| 1 | HeadmanNeedsGrain | Headman | 收集/交付 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 2 | HeadmanNeedsToDeliverAHerd | Headman | 护送/跟随 | Common | ✓ | ✗ | ✗ | 中等 | CS |
| 3 | HeadmanVillageNeedsDraughtAnimals | Headman | 收集/交付 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 4 | VillageNeedsTools | Headman | 收集/交付 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 5 | VillageNeedsCraftingMaterials | Headman | 收集/交付 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 6 | LandlordNeedsAccessToVillageCommons | Headman | 说服/调解 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 7 | LandLordNeedsManualLaborers | Headman | 俘虏/收集 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 8 | LandLordTheArtOfTheTrade | Headman | 经济/谈判 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 9 | LandlordTrainingForRetainers | Headman | 训练/军事 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 10 | ExtortionByDeserters | Headman | 讨伐/清剿 | Common | ✓ | ✗ | ✗ | 中等 | CS |
| 11 | FamilyFeud | Headman | 帮派/道德 | Rare | ✓ | ✓ | ✓ | 🔴极复杂 | SB |
| 12 | NotableWantsDaughterFound | Headman | 寻人/说服 | Rare | ✓ | ✗ | ✗ | 🔴复杂 | SB |
| 13 | RuralNotableInnAndOut | Headman | 经济/追回 | Common | ✗ | ✗ | ✗ | 中等 | SB |
| 14 | ArtisanCantSellProductsAtAFairPrice | Artisan | 经济/贸易 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 15 | ArtisanOverpricedGoods | Artisan | 经济/垄断 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 16 | EscortMerchantCaravan | Merchant | 护送/跟随 | Common | ✓ | ✗ | ✗ | 中等 | CS |
| 17 | CaravanAmbush | Merchant | 救援/战斗 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 18 | BettingFraud | Merchant | 侦探/调查 | Rare | ✗ | ✗ | ✗ | 复杂 | CS |
| 19 | RevenueFarming | Lord | 经济/征税 | Rare | ✗ | ✗ | ✓ | 中等 | CS |
| 20 | GangLeaderNeedsRecruits | GangLeader | 收集/交付 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 21 | GangLeaderNeedsSpecialWeapons | GangLeader | 收集/交付 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 22 | GangLeaderNeedsToOffloadStolenGoods | GangLeader | 经济/销赃 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 23 | GangLeaderNeedsWeapons | GangLeader | 收集/交付 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 24 | RivalGangMovingIn | GangLeader | 帮派/巷战 | Common | ✓ | ✗ | ✓ | 复杂 | SB |
| 25 | SnareTheWealthy | GangLeader | 帮派/道德 | Rare | ✗ | ✗ | ✓ | 🔴极复杂 | SB |
| 26 | LordNeedsHorses | Lord | 收集/交付 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 27 | LordsNeedsTutor | Lord | 找人/教育 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 28 | LordWantsRivalCaptured | Lord | 活捉/讨伐 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 29 | LadysKnightOut | Lord | 护送/护卫 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 30 | ProdigalSon | Lord | 营救/谈判 | Rare | ✗ | ✗ | ✗ | 复杂 | SB |
| 31 | TheSpyParty | Lord | 侦探/推理 | Rare | ✗ | ✗ | ✗ | 🔴极复杂 | SB |
| 32 | ArmyNeedsSupplies | Lord | 军事/收集 | Common | ✓ | ✗ | ✗ | 中等 | CS |
| 33 | ScoutEnemyGarrisons | Lord | 军事/侦察 | Common | ✗ | ✗ | ✗ | 中等 | CS |
| 34 | RaidAnEnemyTerritory | Lord | 军事/劫掠 | Common | ✓ | ✗ | ✗ | 中等 | CS |
| 35 | TheConquestOfSettlement | Lord | 军事/征服 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 36 | LordNeedsGarrisonTroops | Lord | 军事/驻军 | Common | ✓ | ✗ | ✗ | 简单 | CS |
| 37 | LesserNobleRevolt | Lord | 讨伐/镇压 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 38 | NearbyBanditBase | 通用 | 讨伐/藏身处 | Common | ✓ | ✗ | ✗ | 中等 | CS |
| 39 | MerchantArmyOfPoachers | 通用 | 讨伐/清剿 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 40 | Smugglers | 通用 | 侦探/调查 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 41 | CapturedByBountyHunters | 通用 | 营救/说服 | Rare | ✗ | ✗ | ✗ | 中等 | CS |
| 42 | LandLordCompanyOfTrouble | 通用 | 讨伐/佣兵 | Rare | ✓ | ✗ | ✗ | 中等 | CS |
| 43 | MerchantNeedsHelpWithOutlaws | Merchant | 讨伐/清剿 | Common | ✓ | ✗ | ✗ | 中等 | CS |

> **图例**：CS = CampaignSystem.dll, SB = SandBox.dll, Alt = Alternative Solution, Lord = Lord Solution

---

## 按玩法类型索引

### 讨伐/清剿 (8)
NearbyBanditBase, ExtortionByDeserters, MerchantNeedsHelpWithOutlaws, MerchantArmyOfPoachers, LandLordCompanyOfTrouble, LesserNobleRevolt, RaidAnEnemyTerritory, TheConquestOfSettlement

### 收集/交付 (12)
HeadmanNeedsGrain, VillageNeedsTools, VillageNeedsCraftingMaterials, HeadmanVillageNeedsDraughtAnimals, LordNeedsHorses, GangLeaderNeedsRecruits, GangLeaderNeedsSpecialWeapons, GangLeaderNeedsWeapons, GangLeaderNeedsToOffloadStolenGoods, ArmyNeedsSupplies, LordNeedsGarrisonTroops, LandLordNeedsManualLaborers

### 护送/跟随 (4)
EscortMerchantCaravan, HeadmanNeedsToDeliverAHerd, LadysKnightOut, CaravanAmbush

### 寻人/说服 (4)
NotableWantsDaughterFound, LordWantsRivalCaptured, ProdigalSon, LordsNeedsTutor

### 侦探/调查 (3)
TheSpyParty, BettingFraud, Smugglers

### 帮派/道德 (3)
FamilyFeud, RivalGangMovingIn, SnareTheWealthy

### 经济/贸易 (6)
RevenueFarming, ArtisanCantSellProductsAtAFairPrice, ArtisanOverpricedGoods, LandLordTheArtOfTheTrade, RuralNotableInnAndOut, LandlordNeedsAccessToVillageCommons

### 军事/战略 (4)
ScoutEnemyGarrisons, LandlordTrainingForRetainers, ArmyNeedsSupplies, TheConquestOfSettlement

### 营救 (2)
CapturedByBountyHunters, ProdigalSon

---

## 快速参考卡

### A. 村庄要人任务

---

#### 1. HeadmanNeedsGrain — 村庄缺粮

| 属性 | 值 |
|------|-----|
| **目标** | 购买/收集 N 袋谷物种子（N = 5 + 15×难度）交付给村庄 |
| **时限** | 30天(Quest) / 30天(Issue) |
| **奖励** | 500 + 1500×难度 |
| **Alternative** | ✓（需 Trade/Charm 120, 5 + 5×难度 士兵, 6 + 9×难度 天） |

**核心机制**：
- 纯粹的"买东西→交货"模式，最简单的任务结构
- 谷物可以从任何城镇市场购买
- 交付时触发 `village_deliver_grain` GameMenu

**存档字段**：`_delieveredGrainCount` (int)

**事件订阅**：WarDeclared, ClanChangedKingdom, VillageBeingRaided, SettlementOwnerChanged

**可复用接口**：
- `GiveGoldAction.ApplyBetweenCharacters` — 玩家买谷物的资金流转
- `AddDiscreteLog` — 交付进度条
- GameMenu 切换：`GameMenu.SwitchToMenu("village_deliver_grain")`

---

#### 2. HeadmanNeedsToDeliverAHerd — 护送牲畜

| 属性 | 值 |
|------|-----|
| **目标** | 护送牲畜群从村庄到目标城镇 |
| **时限** | 30天(Quest) / 45天(Issue) |
| **奖励** | 1000 + 2000×难度 |
| **Alternative** | ✓（需 Riding/Scouting 120, 5 + 10×难度 士兵） |

**核心机制**：
- 动态生成牲畜部队（MobileParty），玩家需要跟随保护
- 牲畜部队设置 AI：`SetPartyAiAction.GetActionForGoingToSettlement(targetSettlement)`
- 如果牲畜部队被摧毁 → 任务失败

**存档字段**：`_herdParties` (List<MobileParty>), `_targetSettlement`, `_totalHerds`

**事件订阅**：MobilePartyDestroyed, SettlementEntered, HourlyTickParty, WarDeclared 等

**可复用接口**：
- `SetPartyAiAction.GetActionForGoingToSettlement` — AI 自动导航
- `AddTrackedObject` — 地图追踪牲畜部队
- `MobileParty.CreateParty` — 动态生成部队

---

#### 3. HeadmanVillageNeedsDraughtAnimals — 村庄需耕畜

| 属性 | 值 |
|------|-----|
| **目标** | 交付 N 头耕畜（SumpterHorse/Mule 等） |
| **时限** | 30天(Quest) / 30天(Issue) |
| **奖励** | 每头牲畜市场价×1.5 |
| **Alternative** | ✓（需 Riding/Charm 120） |

**核心机制**：
- 与 HeadmanNeedsGrain 同模式，只是目标物品从谷物变为耕畜
- 牲畜类型从 `DefaultItemCategories.PackAnimals` 中取
- 奖励按市场实时价格计算，非固定值

**存档字段**：`_requestedAnimalCount`, `_requestedAnimal`

**可复用接口**：
- `MBObjectManager.Instance.GetObject<ItemCategory>("pack_animals")` — 按类别查找
- `GiveGoldAction` — 按市场价结算

---

#### 4. VillageNeedsTools — 村庄需工具

| 属性 | 值 |
|------|-----|
| **目标** | 交付 N 件工具（N = 3 + 7×难度） |
| **时限** | 30天 |
| **奖励** | 800 + 3000×难度 |
| **Alternative** | ✓（需 Smithing/Charm 120） |

**核心机制**：
- 工具从 `DefaultItemCategories.Tools` 类别中取
- 玩家可从市场购买或自己打造

---

#### 5. VillageNeedsCraftingMaterials — 村庄需材料

| 属性 | 值 |
|------|-----|
| **目标** | 交付 N 件制作材料（黏土/羊毛/兽皮等） |
| **时限** | 30天 |
| **奖励** | 按材料市场价结算 |

**核心机制**：
- 需要的材料类型根据村庄主要产出动态选择（→ 村庄生产铁，就需要黏土做模具）
- 使用 `CraftingMaterialsItemCategory` 类别

---

#### 6. LandlordNeedsAccessToVillageCommons — 公地纠纷

| 属性 | 值 |
|------|-----|
| **目标** | 说服另一方允许地主使用村庄公地 |
| **时限** | 20天(Quest) / 20天(Issue) |
| **奖励** | 800 + 2000×难度 |
| **Alternative** | ✓（需 Charm 120） |

**核心机制**：
- 两方 NPC：地主 vs 占用公地的另一方
- 使用 **PersuasionTask** 进行说服检定
- 如果说服失败 → 可以选择暴力解决（进入 Mission 战斗）
- PersuasionDifficulty 受难度系数影响

**存档字段**：`_targetHero`, `_persuasionTask`, `_persuaded`

**可复用接口**：
- `PersuasionTask` — 说服系统
- `EncounterManager.StartSettlementEncounter` — 触发战斗

---

#### 7. LandLordNeedsManualLaborers — 需要劳工

| 属性 | 值 |
|------|-----|
| **目标** | 俘虏 N 个敌方士兵交付给地主当劳工 |
| **时限** | 20天(Quest) / 25天(Issue) |
| **奖励** | 1000 + 3000×难度 |
| **Alternative** | ✓（需 Roguery/Charm 120） |

**核心机制**：
- 要求玩家去抓俘虏（不是招募！）
- 需要俘虏在玩家队伍中且是健康的（`!isWounded`）
- 交付时俘虏从玩家部队移除 → 转为地主村庄的民兵

**存档字段**：`_requestedPrisonerCount`, `_delieveredPrisonerCount`

**可复用接口**：
- `MobileParty.MainParty.PrisonRoster` — 俘虏系统
- `AddDiscreteLog` — 交付进度

---

#### 8. LandLordTheArtOfTheTrade — 交易纠纷

| 属性 | 值 |
|------|-----|
| **目标** | 调解两个商人的交易纠纷 |
| **时限** | 20天 |
| **奖励** | 1000 + 2000×难度 |

**核心机制**：
- 玩家作为中间人调解两个 NPC 的纠纷
- 多种解决方案：给钱摆平 / 说服 / 威胁
- 每个方案有不同的后果（关系变化、金钱支出）

---

#### 9. LandlordTrainingForRetainers — 训练家丁

| 属性 | 值 |
|------|-----|
| **目标** | 借出 N 个士兵给地主训练，N 天后取回 |
| **时限** | 20天(Quest) / 25天(Issue) |
| **奖励** | 500 + 1500×难度 + 士兵获得 XP |

**核心机制**：
- 玩家借出自己的士兵给地主
- 士兵暂时从玩家部队移除
- N 天后 → 士兵返回 + XP + 可能升级
- 如果地主在期间死亡/村庄被毁 → 任务失败（士兵损失）

**存档字段**：`_disciplinedTroops` (List<CharacterObject>), `_returnTime`

**可复用接口**：
- `MobileParty.MemberRoster.AddToCounts` — 部队成员增减
- `DisableHeroAction` — 临时禁用 Hero

---

#### 10. ExtortionByDeserters — 逃兵勒索

| 属性 | 值 |
|------|-----|
| **目标** | 消灭勒索村庄的逃兵部队 |
| **时限** | 20天(Quest) / 30天(Issue) |
| **奖励** | 1500 + 2500×难度 |
| **Alternative** | ✓（需 Tactics/Leadership 120） |

**核心机制**：
- 动态生成逃兵部队（CharacterObject 模板 = deserters）
- 逃兵部队在村庄周围巡逻（`SetPartyAiAction.GetActionForPatrollingAroundSettlement`）
- 消灭即完成
- 类似 MerchantNeedsHelpWithOutlaws 的简化版

**存档字段**：`_deserterParties` (List<MobileParty>)

**可复用接口**：
- `SetPartyAiAction.GetActionForPatrollingAroundSettlement` + `SetDoNotMakeNewDecisions(true)` — AI 锁定
- `AddTrackedObject` — 地图追踪逃兵

---

#### 11. FamilyFeud — 家族世仇 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 保护犯了杀人罪的年轻族人免受对方家族复仇 |
| **时限** | 20天(Quest) / 30天(Issue) |
| **Lord Solution** | ✓（唯一！消耗 20 影响力） |
| **背叛** | ✓ |

**独特之处**：
- 原版 40 任务中 **唯一有 Lord Solution** 的任务
- 10 个独立 DialogFlow — 对话分支最复杂
- CounterOffer 机制：`CounterOfferHero` + `AfterIssueCreation` 钩子
- Mission 巷战（alley_2 场景）+ 三方 Agent + Persuasion + 道德三选一
- 5 种结算路径：成功/背叛/犯人被杀/玩家被击倒/超时

---

#### 12. NotableWantsDaughterFound — 寻找女儿 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 寻找被拐走的女儿 → 击败恶棍 → 说服女儿回家 |
| **时限** | 19天(Quest) / 30天(Issue) |
| **Persuasion** | ✓（Difficulty = 5） |

**独特之处**：
- **动态 NPC 创建**：`HeroCreator.CreateSpecialHero` 创建女儿 + 恶棍
- **线索收集系统**：`_villagesAndAlreadyVisitedBooleans` 状态机
- **技能检定捷径**：Scout ≥ 150×难度 → 跳过搜索直接定位
- Mission 内 Agent 操控 + Persuasion 接管对话
- 多结局：说服成功/女儿逃跑/被抓/村庄被劫

---

#### 13. RuralNotableInnAndOut — 酒后赌输地契

| 属性 | 值 |
|------|-----|
| **目标** | 前往目标定居点酒馆，找 Game Host 下棋赢回地契（或直接花钱买回） |
| **时限** | 14天(Quest) / 25天(Issue) |
| **奖励** | 全额（下棋赢）/ 800（花钱买，Lesser Reward） |

**核心机制**：
- 委托人（RuralNotable）在酒馆玩桌游时把地契输给了 Game Host（`Occupation == 14`）
- 玩家去目标定居点酒馆 → 找 Game Host 对话 → 两个路径：
  - **路径A：下棋赢回来** — 赌注 1000 第纳尔，`MissionBoardGameLogic.StartBoardGame()` 启动桌游 minigame（按文化选棋种），赢了拿回地契+全额奖励，输了可以再试一次（`_tryCount < 2`），输满2次任务失败
  - **路径B：直接花钱买** — 付 1000 第纳尔给 Game Host，跳过下棋，但只拿 Lesser Reward（固定 800 金币）
- 进入酒馆时 Game Host 被 `IsVisualTracked = true` 高亮标记
- 这是原版**唯一集成桌游 minigame 的任务**

**存档字段**：`_boardGameType`, `_tryCount`, `_playerWonTheGame`, `_checkForBoardGameEnd`, `_applyLesserReward`

**可复用接口**：
- `MissionBoardGameLogic.SetBoardGame()` / `StartBoardGame()` — 任务中嵌入桌游 minigame
- `CampaignEvents.OnPlayerBoardGameOverEvent` — 监听桌游结果
- `BoardGameCampaignBehavior.SetBetAmount()` — 设置赌注
- `locationCharacter.IsVisualTracked = true` — 酒馆内 NPC 高亮

---

### B. 城镇工匠/商人任务

#### 14. ArtisanCantSellProductsAtAFairPrice — 产品滞销

| 属性 | 值 |
|------|-----|
| **目标** | 帮工匠把产品卖到另一个城镇的好价钱 |
| **时限** | 20天(Quest) / 25天(Issue) |
| **奖励** | 市场价差额 × 产品数量 |

**核心机制**：
- 从工匠处接货 → 带到另一个城镇卖出
- 最低售价 = 工匠给的底价 × 1.2
- 玩家利润 = 实际售价 - 底价
- 如果到期没卖出 → 可以原价还给工匠（无惩罚）

**存档字段**：`_productCount`, `_fairPrice`, `_targetSettlement`

---

#### 15. ArtisanOverpricedGoods — 原料垄断

| 属性 | 值 |
|------|-----|
| **目标** | 打破垄断商人的原料垄断，帮工匠买到合理价格的原料 |
| **时限** | 20天 |
| **奖励** | 500 + 2000×难度 |

**核心机制**：
- 垄断商人囤积某种原料 → 工匠买不到
- 玩家可选路径：
  A. 说服垄断商降价（Persuasion）
  B. 从其他城镇买了带给工匠
  C. 威胁垄断商（Roguery 选项，可能触发战斗）

---

#### 16. EscortMerchantCaravan — 护送商队 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 护送商队访问 N 个定居点（N = 3~10） |
| **时限** | 30天 |
| **奖励** | 每日 250+1000×难度（上限 8000） |

**独特之处**：
- 动态生成商队 MobileParty + 匪徒伏击部队
- 匪徒跟踪机制：跟随 _followDuration 天后主动攻击
- 共享冷却：与 SnareTheWealthy / CaravanAmbush 三合一冷却

---

#### 17. CaravanAmbush — 商队遭伏击

| 属性 | 值 |
|------|-----|
| **目标** | 救援被伏击的商队 |
| **时限** | 15天（短！） |
| **奖励** | 2000 + 3000×难度 |

**核心机制**：
- 商队已被伏击 → 玩家需要在商队被全灭前赶到
- 到达后触发战斗：玩家 vs 伏击者
- 有倒计时压力（`HourlyTick` 检测商队存活）
- 与 EscortMerchantCaravan / SnareTheWealthy 共享冷却

**存档字段**：`_ambushedCaravan`, `_ambusherParty`

---

#### 18. BettingFraud — 竞技场赌注欺诈

| 属性 | 值 |
|------|-----|
| **目标** | 调查竞技场赌注欺诈，找到幕后黑手 |
| **时限** | 20天(Quest) / 20天(Issue) |
| **奖励** | 500 + 1500×难度 |

**核心机制**：
- 玩家参加竞技场比赛 → 观察异常行为
- 收集线索：与多个 NPC 对话
- 指认嫌疑人 → 如果指认正确，进入战斗
- 有 CounterOffer 机制（赌场老板尝试收买你）
- 如果接受贿赂 → 背叛原任务发布者

**存档字段**：`_counterOfferNotable`, `_counterOfferConversationDone`, `_fixedTournamentCount`

**可复用接口**：
- `CounterOfferHero` — 收买机制
- `TournamentGame` — 竞技场系统

---

#### 19. RevenueFarming — 包税权 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 走访领主的所有村庄收税，收集指定数额后交给领主 |
| **时限** | 20天 |
| **背叛** | ✓（私吞税收：Honor -100, CrimeRating +45） |

**独特之处**：
- **挂机收税进度条**：`HourlyTick` + `GiveGoldAction` 实时到账
- **RevenueVillage** 辅助数据结构：每小时收税速度 = TargetAmount / 10
- **VillageEvent 随机事件系统**：30% 进度时触发
- 超时不完全失败 → `OnBeforeTimedOut` 弹出选择框

---

### C. 帮派头目任务

#### 20. GangLeaderNeedsRecruits — 帮派需匪兵

| 属性 | 值 |
|------|-----|
| **目标** | 招募 N 个 **劫匪/强盗（Occupation.Bandit）** 加入部队，转交给 GangLeader（N = 6 + 10×难度） |
| **时限** | 30天(Quest) / 30天(Issue) |
| **奖励** | 2000 + 每人 100×Tier系数（Tier≤1=100, Tier≤3=150, Tier>3=200） |
| **Alternative** | ✓（需 Leadership/Roguery 120, T2+ 士兵 11+9×难度） |

**核心机制**：
- ★ 只能交付 `Occupation == Bandit` 的兵种（劫匪/土匪/强盗），普通招募兵不行
- 玩家必须去大地图打匪徒 → 用 Roguery 招募 → 带回交给 GangLeader
- 交付走 **PartyScreen**（`PartyScreenManager.OpenScreenWithCondition`）——左侧玩家部队，过滤只显示 Bandit
- 交付的士兵从玩家部队移除，加入 GangLeader 帮派
- 奖励按每个匪兵的 Tier 阶梯计价，不是固定值

**存档字段**：`_requestedRecruitCount`, `_deliveredRecruitCount`, `_rewardGold`, `_playerReachedRequestedAmount`

**可复用接口**：
- `PartyScreenManager.OpenScreenWithCondition` — 自定义过滤的部队转移 UI
- `character.Occupation == Occupation.Bandit` — 匪徒身份判定

---

#### 21. GangLeaderNeedsSpecialWeapons — 特殊武器需求

| 属性 | 值 |
|------|-----|
| **目标** | 获取 N 件特殊武器（特定 WeaponClass） |
| **时限** | 30天 |
| **奖励** | 市场价×1.5 |

**核心机制**：
- 需求特定 WeaponClass（如 TwoHandedSword / Bow）
- 玩家可购买/打造/掠夺
- 无 Alternative Solution（必须亲自去）

---

#### 22. GangLeaderNeedsToOffloadStolenGoods — 销赃

| 属性 | 值 |
|------|-----|
| **目标** | 帮帮派把赃物卖到外地 |
| **时限** | 20天 |
| **奖励** | 售价 × 40%（帮派抽 60%） |

**核心机制**：
- 接收赃物 → 带到目标城镇卖出
- 如果被守卫发现 → 触发犯罪惩罚
- 低犯罪率路径 vs 高风险高回报

---

#### 23. GangLeaderNeedsWeapons — 批量武器收集

| 属性 | 值 |
|------|-----|
| **目标** | 收集指定 WeaponClass 的武器 N 件 |
| **时限** | 30天 |
| **奖励** | 市场价×1.3 |

**核心机制**：
- 随机选择 WeaponClass → 收集该类别武器
- `_playerDodgedGuards` 机制：躲避守卫检查
- 路径选择：正门（可能被拦）vs 后巷（安全）

**存档字段**：`_randomForRequiredWeaponClass`, `_collectedItemAmount`, `_playerDodgedGuards`

---

#### 24. RivalGangMovingIn — 敌对帮派入侵

| 属性 | 值 |
|------|-----|
| **目标** | 帮帮派头目清除入侵的敌对帮派 |
| **时限** | 20天(Quest) / 30天(Issue) |
| **背叛** | ✓ |
| **Alternative** | ✓（需 Tactics/Roguery 120） |

**核心机制**：
- Mission 巷战：进入 alley/backstreet 场景
- 带领帮派成员 vs 敌对帮派
- 背叛选项：加入敌对帮派 → 获得更多钱但原关系暴跌

**存档字段**：`_rivalGangLeader`, `_rivalGangThugs`

---

#### 25. SnareTheWealthy — 诱捕富商 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 假扮护卫混入商队，配合帮派抢劫 |
| **时限** | 20天 |
| **背叛** | ✓（三选一：帮商人/帮帮派/两边都杀） |

**独特之处**：
- 原版道德后果最复杂的支线
- 三选一关键时刻：帮商人（Honor++）/ 帮帮派（Honor--, CrimeRating++）/ 两边都杀（最大利益最低道德）
- 假护卫身份 → 卧底机制

---

### D. 领主/贵族任务

#### 26. LordNeedsHorses — 领主需战马

| 属性 | 值 |
|------|-----|
| **目标** | 交付 N 匹战马（N = 5 + 5×难度） |
| **时限** | 30天(Quest) / 35天(Issue) |
| **Alternative** | ✓（需 Riding/Charm 120） |

**核心机制**：
- 战马类别：`DefaultItemCategories.Horse` 中 type = Horse 且 `IsMount = true`
- 不要驮马（SumpterHorse / Mule 不算）
- 每个物品有 `IsMount` 属性检查

---

#### 27. LordsNeedsTutor — 家庭教师

| 属性 | 值 |
|------|-----|
| **目标** | 找到并护送一位有指定技能要求的同伴/学者 |
| **时限** | 30天(Quest) / 35天(Issue) |

**核心机制**：
- 需要找指定技能 ≥ 需求的 NPC
- 在酒馆/各城镇搜索 → 找到后说服其跟你走
- 护送到领主城堡
- Persuasion + 护送两阶段

---

#### 28. LordWantsRivalCaptured — 活捉仇敌

| 属性 | 值 |
|------|-----|
| **目标** | 活捉指定的敌方领主（不能杀！） |
| **时限** | 30天 |
| **Alternative** | ✓（需 Riding/Scouting 120, 10+20×难度 士兵） |

**核心机制**：
- 目标 `_rivalHero` 在大地图上游荡
- ★ 必须活捉（`IsPrisoner`），打死不算
- 大地图追踪：`AddTrackedObject(rivalHero)`
- 找到 → 战斗 → 俘虏 → 交付

**存档字段**：`_rivalHero`, `_isCaptured`

**可复用接口**：
- `AddTrackedObject(Hero)` — 追踪 Hero 而非 MobileParty
- `Hero.IsPrisoner` — 俘虏状态检查

---

#### 29. LadysKnightOut — 贵妇出游

| 属性 | 值 |
|------|-----|
| **目标** | 护送贵妇安全访问目标定居点 |
| **时限** | 20天(Quest) / 45天(Issue) |
| **Alternative** | ✓（需 Charm/Riding 120） |

**核心机制**：
- 贵妇加入玩家部队（临时同伴）
- 护送到目标定居点 → 可能在路上遭遇伏击
- 贵妇有特殊的待遇需求（如不能受伤）
- 类似护送类但目标是 Hero 而非 MobileParty

---

#### 30. ProdigalSon — 浪子回头

| 属性 | 值 |
|------|-----|
| **目标** | 赎出因赌博欠债被扣留的领主儿子 |
| **时限** | 20天(Quest) / 45天(Issue) |

**核心机制**：
- 年轻族人被 GangLeader 扣留（欠赌债）
- 两个路径：
  A. 付赎金（直接给钱）
  B. 说服（Persuasion / 威胁）
  C. 暴力解救（进入 Mission 战斗）
- 成功后护送儿子回家

**存档字段**：`_targetHero`, `_targetSettlement`, `_prodigalSon`

---

#### 31. TheSpyParty — 间谍潜入 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 在比武大会中找出间谍 |
| **时限** | 5天（最短！） |
| **奖励** | 500 + 3000×难度 |

**独特之处**：
- 原版唯一使用 **疑似 NPC 布尔推理** 的任务
- `SuspectNpc` 结构体：Hair / Beard / Markings / BigSword — 4 个 bool
- 玩家与 NPC 交谈收集线索 → 构建画像 → 匹配所有选手 → 指认
- 指认错误 → 关系惩罚；指认正确 → 战斗

---

#### 32. ArmyNeedsSupplies — 军队需补给

| 属性 | 值 |
|------|-----|
| **目标** | 收集 N 单位食物交付给领主部队 |
| **时限** | 20天(Quest) / 30天(Issue) |
| **Alternative** | ✓ |

**核心机制**：
- 食物需求量 = 部队人数 × N 天消耗
- 不限食物种类，只要是 `IsFood` 即可
- 领主部队可能会在大地图上移动 → 玩家需要跟踪

**存档字段**：`_requestedFoodCount`, `_delieveredFoodCount`

---

#### 33. ScoutEnemyGarrisons — 侦察敌方驻军

| 属性 | 值 |
|------|-----|
| **目标** | 侦察 3 个敌方定居点的驻军情况 |
| **时限** | 30天(Quest) / 30天(Issue) |
| **奖励** | 500 + 1000×难度（每侦察一个） |

**核心机制**：
- 随机选择 3 个敌对王国的定居点（`_settlement1/2/3`）
- 玩家接近每个目标 → 自动侦察（`OnSettlementEntered`）
- 不需要战斗，只需要到达
- 如果某个定居点被征服 → 目标减少

**存档字段**：`_settlement1`, `_settlement2`, `_settlement3`, `_scoutedCount`

**可复用接口**：
- `QuestHelper.AddMapArrowFromPointToTarget` — 多目标地图箭头
- `AddTrackedObject(Settlement)` — 追踪定居点

---

#### 34. RaidAnEnemyTerritory — 袭击敌境

| 属性 | 值 |
|------|-----|
| **目标** | 劫掠 N 个敌方村庄（N = 1 + 2×难度） |
| **时限** | 30天 |
| **Alternative** | ✓（需 Tactics/Roguery 120） |

**核心机制**：
- 指定敌方王国 → 劫掠其任意村庄
- 每劫掠一个村庄 → `_raidedVillages` +1
- 单纯的事件驱动（`VillageBeingRaided` 监听）

**存档字段**：`_enemyKingdom`, `_raidedVillages` (List<Settlement>), `_raidedVillagesTrackLog`

---

#### 35. TheConquestOfSettlement — 征服定居点

| 属性 | 值 |
|------|-----|
| **目标** | 帮助领主征服指定定居点 |
| **时限** | 40天（较长！） |
| **奖励** | 大量金币+影响力+关系 |

**核心机制**：
- 目标是一个特定的敌方城堡/城镇
- 玩家需要参与攻城（或独立攻城）
- 成功后定居点归任务发布者所有
- 如果定居点被第三方征服 → 任务失败

---

#### 36. LordNeedsGarrisonTroops — 驻军补充

| 属性 | 值 |
|------|-----|
| **目标** | 交付 N 个士兵补充领主的驻军 |
| **时限** | 30天 |
| **Alternative** | ✓ |

**核心机制**：
- 类似 LandlordTrainingForRetainers，但士兵永久性转交
- 士兵从玩家部队转移到目标定居点的驻军中
- 不限兵种/阵营

---

#### 37. LesserNobleRevolt — 小贵族叛乱

| 属性 | 值 |
|------|-----|
| **目标** | 镇压/说服叛乱的小贵族 |
| **时限** | 20天 |
| **奖励** | 影响力 + 关系 + 金币 |

**核心机制**：
- 一个 Clan tier ≤ 3 的小贵族叛乱
- 两个解决路径：
  A. 军事镇压（消灭叛军部队）
  B. 说服其放弃叛乱（Persuasion）
- 类似 FamilyFeud 的简化版

---

### E. 通用/全局任务

#### 38. NearbyBanditBase — 附近强盗基地

| 属性 | 值 |
|------|-----|
| **目标** | 消灭附近藏身处中的所有强盗 |
| **时限** | 30天(Quest) / 45天(Issue) |
| **奖励** | 3000 + 5000×难度 |
| **Alternative** | ✓（需 Tactics/Scouting 120） |

**核心机制**：
- 选择离委托人定居点最近的藏身处（`Hideout`）
- 玩家进入藏身处 → Mission Scene（限定人数渗透战斗）
- 消灭所有强盗 → 完成
- ★ `BusyHideouts.Add()` 防止与 `MerchantNeedsHelpWithOutlaws` 冲突

**存档字段**：`_nearbyHideout`

**可复用接口**：
- `Campaign.Current.BusyHideouts` — 藏身处资源互斥
- `Hideout.IsTaken` — 藏身处是否已被其他任务占用

---

#### 39. MerchantArmyOfPoachers — 偷猎者军队

| 属性 | 值 |
|------|-----|
| **目标** | 消灭偷猎者部队 |
| **时限** | 25天 |
| **奖励** | 2000 + 4000×难度 |

**核心机制**：
- 类似 MerchantNeedsHelpWithOutlaws 但目标是偷猎者（Poachers）
- 偷猎者部队动态生成
- 消灭后 → 定居点 Food +5

---

#### 40. Smugglers — 走私者网络

| 属性 | 值 |
|------|-----|
| **目标** | 调查并摧毁走私者网络 |
| **时限** | 20天 |
| **奖励** | 1500 + 2000×难度 |

**核心机制**：
- 在定居点内与 NPC 交谈收集线索
- 找到走私者的秘密仓库/据点
- 进入 Mission → 消灭走私者守卫
- 有侦探元素 + 战斗

---

#### 41. CapturedByBountyHunters — 被赏金猎人追捕

| 属性 | 值 |
|------|-----|
| **目标** | 帮被赏金猎人追捕的 NPC 逃脱 |
| **时限** | 20天 |
| **奖励** | 1000 + 2000×难度 |

**核心机制**：
- NPC 主动找你求助（犯罪后被赏金猎人追捕）
- 选择帮还是不帮：
  A. 帮 → 与赏金猎人战斗 / 贿赂
  B. 不帮 → 交给赏金猎人拿赏金（背叛原 NPC）

---

#### 42. LandLordCompanyOfTrouble — 佣兵连闹事

| 属性 | 值 |
|------|-----|
| **目标** | 消灭在领地内闹事的佣兵连 |
| **时限** | 25天 |
| **Alternative** | ✓（需 Tactics/Leadership 120） |

**核心机制**：
- 标准讨伐模式：消灭目标部队
- 佣兵连（MercenaryCompany）在大地图上游荡
- 消灭后 → 定居点 Security +5

---

#### 43. MerchantNeedsHelpWithOutlaws — 清剿匪徒 🔴

> 详见 [03_deep_dives.md](03_deep_dives.md) — 完整深度分析

| 属性 | 值 |
|------|-----|
| **目标** | 消灭 N 队匪徒（N = 2 + 6×难度） |
| **时限** | 20天(Quest) / 15天(Issue) |
| **Alternative** | ✓（需 Tactics/Scouting 120） |

**独特之处**：
- **动态目标发现**：`HourlyTickParty` 每小时扫描附近匪徒
- **匪徒 AI 操控**：`SetPartyAiAction` + `SetDoNotMakeNewDecisions(true)` 锁定
- 两种完成路径：消灭 + Roguery 招募
- 9 个事件订阅（最多的事件监听数之一）

---

## 附录：复杂度排名 Top 10

| 排名 | 任务 | 复杂因素 |
|------|------|---------|
| 1 | **FamilyFeud** | 10 DialogFlow + Lord Solution + CounterOffer + Mission 巷战 + Persuasion + 道德三选一 |
| 2 | **SnareTheWealthy** | 卧底机制 + 三方道德抉择 + 背叛链条 |
| 3 | **TheSpyParty** | 4-bool 推理系统 + 嫌疑人画像匹配 + 比武大会集成 |
| 4 | **NotableWantsDaughterFound** | 动态 NPC 创建 + 线索收集 + Mission 战斗 + Persuasion + 多结局 |
| 5 | **RevenueFarming** | RevenueVillage 数据结构 + 挂机收税 + 随机事件 + 道德抉择 |
| 6 | **ProdigalSon** | 三路径（付赎金/说服/暴力）+ Mission 战斗 + 护送 |
| 7 | **EscortMerchantCaravan** | 动态生成商队 + 匪徒生成 + 跟随 AI + 共享冷却 |
| 8 | **BettingFraud** | 竞技场调查 + CounterOffer + 指认系统 |
| 9 | **MerchantNeedsHelpWithOutlaws** | 动态目标发现 + AI 操控 + 9 事件订阅 + 招募分支 |
| 10 | **RivalGangMovingIn** | Mission 巷战 + 背叛选项 + 帮派系统集成 |

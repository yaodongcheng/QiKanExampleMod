# 骑砍2 原版 40 任务 — 完整源码级分析

> **目标**：以最高规格覆盖原版全部任务的源码级分析，提炼可复用的接口、设计模式与实现范本。
> **数据来源**：`ilspycmd` 反编译 `TaleWorlds.CampaignSystem.dll`（33 个任务）+ `SandBox.dll`（7 个任务），共 40 个 Issue/Quest 对。
> **使用场景**：设计新任务/新委托时，先来这里查"原版有没有做过类似的"，再查 [patterns_catalog.md](patterns_catalog.md) 找现成接口。

---

## 文档导航

| 文档 | 内容 | 何时读 |
|------|------|--------|
| [README.md](README.md) | 本文件 — 总索引与导航 | 每次进这个文件夹先看 |
| [01_quest_catalog.md](01_quest_catalog.md) | **40 任务全目录** — 完整分类 + 每个任务的快速参考卡（目标/机制/接口/独特卖点） | 找灵感、查"什么任务做了什么" |
| [02_architecture.md](02_architecture.md) | **架构深度解析** — Issue→Quest 双层模型、事件总线、生命周期、冷却系统、三种解决路径 | 理解原版任务系统怎么运转 |
| [03_deep_dives.md](03_deep_dives.md) | **精选深度分析**（10-15 个机械最独特的任务）— 完整调用链、存档字段、事件订阅、关键设计诀窍 | 深入理解特定机制怎么实现 |
| [04_patterns_catalog.md](04_patterns_catalog.md) | **可复用模式目录** — 按功能分类：表现力/进度/NPC/事件/经济/道德抉择/部队AI/资源互斥 | **🔴 实现新功能前的第一站** |
| [05_interface_reference.md](05_interface_reference.md) | **完整接口/API 参考** — 每个可复用 API 的签名、参数说明、调用范例、所属 DLL | 写代码时 copy-paste |

---

## 快速路线图

### 我想知道"原版有没有做过 X 类型任务"
→ [01_quest_catalog.md](01_quest_catalog.md)，看分类表

### 我想实现"挂机收税"那种进度条
→ [04_patterns_catalog.md](04_patterns_catalog.md) → "进度模式" 章节，看 `HourlyTick` 持续收税模式

### 我想给任务加地图追踪标记
→ [04_patterns_catalog.md](04_patterns_catalog.md) → "表现力模式" → `AddTrackedObject` / `QuestHelper.AddMapArrowFromPointToTarget`

### 我想创建动态临时 NPC
→ [04_patterns_catalog.md](04_patterns_catalog.md) → "NPC 模式" → `HeroCreator.CreateSpecialHero`

### 我想实现"派人代办"（Alternative Solution）
→ [02_architecture.md](02_architecture.md) → "三种解决路径" → Alternative Solution

### 我想加道德抉择/背叛
→ [04_patterns_catalog.md](04_patterns_catalog.md) → "道德抉择模式"

### 我想知道某个 API 怎么调用
→ [05_interface_reference.md](05_interface_reference.md)，按功能搜索

---

## 40 任务总览速查

| # | 任务 | 类型 | 发布者 | DLL | 独特机制 |
|---|------|------|--------|-----|---------|
| 1 | ArmyNeedsSupplies | 军事/收集 | Lord | CampaignSystem | 批量交付食物、部队跟随 |
| 2 | ArtisanCantSellProductsAtAFairPrice | 经济/贸易 | Artisan | CampaignSystem | 价格谈判、多城镇比价 |
| 3 | ArtisanOverpricedGoods | 经济/垄断 | Artisan | CampaignSystem | 打破垄断、购买原料 |
| 4 | BettingFraud | 侦探/调查 | Merchant | CampaignSystem | 竞技场调查、NPC 指认、CounterOffer |
| 5 | CapturedByBountyHunters | 营救/说服 | 任意 Notable | CampaignSystem | NPC 被赏金猎人追捕、帮其逃脱 |
| 6 | CaravanAmbush | 救援/战斗 | Merchant | CampaignSystem | 商队遭伏击、时间紧迫 |
| 7 | EscortMerchantCaravan | 护送/跟随 | Merchant | CampaignSystem | 动态生成商队、匪徒伏击生成 |
| 8 | ExtortionByDeserters | 讨伐/清剿 | Headman | CampaignSystem | 逃兵勒索村庄 |
| 9 | GangLeaderNeedsRecruits | 收集/交付 | GangLeader | CampaignSystem | 招募新兵给帮派 |
| 10 | GangLeaderNeedsSpecialWeapons | 收集/交付 | GangLeader | CampaignSystem | 特殊武器需求 |
| 11 | GangLeaderNeedsToOffloadStolenGoods | 经济/销赃 | GangLeader | CampaignSystem | 销赃、犯罪风险 |
| 12 | GangLeaderNeedsWeapons | 收集/交付 | GangLeader | CampaignSystem | 批量武器收集、守卫躲避 |
| 13 | HeadmanNeedsGrain | 收集/交付 | Headman | CampaignSystem | 购买/收集谷物种子 |
| 14 | HeadmanNeedsToDeliverAHerd | 护送/跟随 | Headman | CampaignSystem | 护送牲畜群 |
| 15 | HeadmanVillageNeedsDraughtAnimals | 收集/交付 | Headman | CampaignSystem | 耕畜需求 |
| 16 | LadysKnightOut | 护送/护卫 | Lord | CampaignSystem | 护送贵妇出游 |
| 17 | LandLordCompanyOfTrouble | 讨伐/佣兵 | 任意 Notable | CampaignSystem | 佣兵连闹事、战斗 |
| 18 | LandlordNeedsAccessToVillageCommons | 说服/谈判 | Headman | CampaignSystem | 调解地主与村庄公地纠纷 |
| 19 | LandLordNeedsManualLaborers | 收集/俘虏 | Headman | CampaignSystem | 抓俘虏当劳工 |
| 20 | LandLordTheArtOfTheTrade | 经济/贸易 | Headman | CampaignSystem | 交易纠纷调解 |
| 21 | LandlordTrainingForRetainers | 训练/军事 | Headman | CampaignSystem | 训练家丁、部队借出 |
| 22 | LesserNobleRevolt | 讨伐/镇压 | Lord | CampaignSystem | 小贵族叛乱 |
| 23 | LordNeedsGarrisonTroops | 军事/驻军 | Lord | CampaignSystem | 补充驻军 |
| 24 | LordNeedsHorses | 收集/交付 | Lord | CampaignSystem | 战马需求 |
| 25 | LordsNeedsTutor | 找人/教育 | Lord | CampaignSystem | 找家庭教师、技能需求 |
| 26 | LordWantsRivalCaptured | 活捉/讨伐 | Lord | CampaignSystem | 活捉仇敌（不能杀） |
| 27 | MerchantArmyOfPoachers | 讨伐/清剿 | Merchant | CampaignSystem | 偷猎者军队 |
| 28 | MerchantNeedsHelpWithOutlaws | 讨伐/清剿 | Merchant | CampaignSystem | 动态目标发现、AI 操控匪徒 |
| 29 | NearbyBanditBase | 讨伐/藏身处 | 任意 Notable | CampaignSystem | 藏身处渗透战斗 |
| 30 | RaidAnEnemyTerritory | 军事/劫掠 | Lord | CampaignSystem | 劫掠敌境村庄 |
| 31 | RevenueFarming | 经济/征税 | Lord | CampaignSystem | **挂机收税进度条**、随机 VillageEvent、道德抉择 |
| 32 | ScoutEnemyGarrisons | 军事/侦察 | Lord | CampaignSystem | 侦察敌方驻军、3 个目标 |
| 33 | Smugglers | 侦探/调查 | Merchant | CampaignSystem | 走私者网络调查 |
| 34 | TheConquestOfSettlement | 军事/征服 | Lord | CampaignSystem | 征服定居点 |
| 35 | VillageNeedsCraftingMaterials | 收集/交付 | Headman | CampaignSystem | 制作材料需求 |
| 36 | VillageNeedsTools | 收集/交付 | Headman | CampaignSystem | 工具需求 |
| 37 | **FamilyFeud** | 帮派/道德 | Headman | **SandBox** | 10 个 DialogFlow、Lord Solution、CounterOffer、Mission 巷战 |
| 38 | **NotableWantsDaughterFound** | 寻人/说服 | Headman | **SandBox** | 动态 NPC 创建、Persuasion、线索收集、Scout 检定 |
| 39 | **ProdigalSon** | 营救/谈判 | Lord | **SandBox** | 浪子回头、欠债赎人 |
| 40 | **RivalGangMovingIn** | 帮派/巷战 | GangLeader | **SandBox** | 敌对帮派入侵、Mission 巷战、背叛选项 |
| 41 | **RuralNotableInnAndOut** | 经济/赌博 | Headman | **SandBox** | 酒后赌输地契、追回 |
| 42 | **SnareTheWealthy** | 帮派/道德 | GangLeader | **SandBox** | 假护卫真抢劫、三选一、道德后果最复杂 |
| 43 | **TheSpyParty** | 侦探/推理 | Lord | **SandBox** | 4 bool 嫌疑人画像、比武大会潜入、指认系统 |

> **注**：严格计数视 BettingFraud/GangLeaderNeedsWeapons/LordNeedsGarrisonTroops 的收录范围而定（部分文档以 40 约称）。实际可反编译到的完整 Issue+Quest 对为 43 个。SandBox 的 7 个（粗体）包含最复杂的机制。

---

## 关联文档

- 父级：[../原版骑砍2任务系统分析.md](../原版骑砍2任务系统分析.md) — 系统级全览
- 父级：[../quest_example.md](../quest_example.md) — 5 个案例源码级分析
- 项目规则：[../../plans/rules/wheels.md](../../plans/rules/wheels.md) — 已造轮子速查
- 叙事设计：[../../plans/rules/narrative-design.md](../../plans/rules/narrative-design.md) — 叙事铁律

# Quest 系统重新定位 — 融入原版 + 因果链设计

> **状态**：计划（未实施）
> **前提**：新 session 完整阅读本文件 + `plans/rules/wheels.md`。
> **取代**：`plans/npc-commissions.md`（原始 16 类委托设计）— 本计划大幅裁减重叠内容，重新定位为"原版增强层"。
> **关联**：`plans/unified-narrative-world-events.md`（WorldEvent 架构）、`Knowledge/原版骑砍2任务系统分析.md`（原版 Issue/Quest 参考）

---

## 一、核心决策

### 决策 1：不再绕过原版，改为融入+增强

**旧架构（问题）**：
```
WorldEventSimulator → CommissionIssueBehavior → CommissionHubIssue（信号 Issue）
→ CommissionIntent（绕过原版对话流）→ CommissionQuest（独立的 QuestBase）
```
与 `IssuesCampaignBehavior → 40 种 IssueBehavior → IssueManager → 原版 Quest` **完全并行，互相不知晓**。同一个 NPC 可能同时有两个 Issue。

**新架构（目标）**：
```
链首：IssuesCampaignBehavior（原版管线，正常运行）
  → 40 种 Issue → 原版 Quest
  → 玩家完成 / 失败 / 叛变
       ↓
链延续：QuestConsequenceResolver（我们重点做）
  → 查因果表 → 生成后续 WorldEvent
  → WorldEventDatabase.AddEvent
       ├─ 模板 party：spawn + AI 控制
       ├─ 真人 Hero：调战略参数 + 接管部队
       └─ SyncEventToNpcMemory → CurrentUrgentEvent
            ↓
      下一个 Tick：IssuesCampaignBehavior 扫描到此 NPC
       → NPC 有 CurrentUrgentEvent → 过滤日常类 Issue，生成关联类 Issue
       → 玩家接取 → 循环

展示层：
  ├─ 路径 A：原版 DialogFlow → 模板文本 → 原版 Quest
  └─ 路径 B：StoryDialog → CommissionIntent → NPC 记忆语境叙事 → 同一个原版 Quest
```

**分工**：链首由原版生成，链的延续由 `QuestConsequenceResolver` 生成。`WorldEventSimulator` 不再凭空制造链首事件，降为"因果引擎的执行层"——收到后续事件指令后 spawn party / 改关系 / 发通知。

### 决策 2：原版 40 种 Issue 全部复用，只补充原版没有的玩法

原版 `IssuesCampaignBehavior`、冷却系统、频率权重全部保留不动。LivingWorldNpcs 只在三个点介入：
- **生成前**：过滤 + 补充。NPC 有 `CurrentUrgentEvent` 时，阻止不相关的"日常经营"类 IssueBehavior 注入；同时注入 LWNPCS 独有类型参与原版评分竞争。**不动原版内部评分公式。**

  `CurrentUrgentEvent` 的来源（链的延续）：
  ```
  玩家完成原版 Quest
    → QuestConsequenceResolver 查因果表
    → 生成后续 WorldEvent → WorldEventDatabase.AddEvent
    → SyncEventToNpcMemory → 涉及 NPC 的 CurrentUrgentEvent 被设置
    → 下一个 Tick：IssuesCampaignBehavior 扫描到此 NPC
    → 过滤日常类 Issue，生成关联类 Issue → 玩家接取
  ```
  事件解决/过期后由 `ClearEventFromNpcMemory` 清除。
- **展示时**：StoryDialog 路径提供个性化叙事
- **完成后**：因果链引擎记录后果 + 生成后续事件

#### 为什么不 patch 原版内部评分公式

原版 `IssuesCampaignBehavior` 内部流程（经 ilspycmd 反编译确认，详见 `Knowledge/原版骑砍2任务系统分析.md` 三、3.1-3.5）：

1. **触发路径**：定居点路径（每天分摊到 N 个定居点，每 Tick 处理 1 个）和家族路径（每天每个家族 10-20% 的领主）

2. **生成概率**：`0.3 × (1 - currentCount/maxCount)²`，指数衰减，越接近上限越难生成

3. **评分竞争流程**（反编译确认）：
   ```
   IssuesCampaignBehavior.OnSettlementTick / DailyTickClan
     → 遍历 Hero → IssueManager.CheckForIssues(hero)
       → PrepareIssueArguments(hero)  // 清空临时列表
       → CampaignEventDispatcher.OnCheckForIssue(hero)  // 同步 multicast
         → 所有 40 个 IssueBehavior + 我们的 listener 同步响应
         → 各自调用 IssueManager.AddPotentialIssueData(hero, pid)
       → 返回 List<PotentialIssueData>  // 所有人的候选在同一个列表里
     → CalculateIssueScoreInternal(pid, totalDesiredIssueCount, totalFrequencyScore)
       频率评分：score = frequencyRatio × (1 + bonus - existingRatio/frequencyRatio)
       VeryCommon=6, Common=3, Rare=1
       已有过多的 Issue 类型被公式自动压低
     → 定居点路径: MBRandom.ChooseWeighted(list, weight=score)  // 加权随机
       家族路径:   list.OrderByDescending(x => x.Score).First()  // 最高分优先
     → IssueManager.CreateNewIssue(pid, hero)
   ```

4. **前置条件**：14 种 `PreconditionFlags`（`AtWar`, `Relation`, `ClanTier`, `Renown`, `Skill`, `Money` 等），各 IssueBehavior 重写 `CanPlayerTakeQuestConditions` 自行判断

**"过滤 + 补充"方案不需要动上面的评分公式**。原因是：

- **过滤**：Harmony prefix on `IssueManager.AddPotentialIssueData`。传入的 `PotentialIssueData.IssueType` 是具体 IssueBase 子类的 Type（如 `typeof(HeadmanNeedsGrainIssue)`）。检查 NPC 的 `CurrentUrgentEvent` 是否与此 Issue 类型语义冲突（如村庄正在被劫掠 → 跳过 `HeadmanNeedsGrain`）。冲突则 skip original method。注意：这里的目标不是"所有日常 Issue 都不许出现"，而是"与当前事件完全无关的 Issue 不在这个 NPC 身上出现"。
- **补充**：在 `OnCheckForIssueEvent` 中注入 LWNPCS 独有类型的 `PotentialIssueData`（频率=Common，与其他原版候选平等竞争）。这些数据进入同一个 `List<PotentialIssueData>`，走同一个评分公式。`IssuesCampaignBehavior` 不区分来源。

原版公式完全不动——过滤减少候选，补充增加候选，公式只在剩余的合理选项之间做频率平衡。

### 决策 3：CommissionCategory 从 16 种裁减到 5 种

删除 11 种与原版重叠或半成品，保留 3 种独有玩法，新增 2 种依赖新玩法系统的。

---

## 二、WorldEvent 战略一致性检查

### 原则

WorldEvent 通过战略一致性检查后才接管 Hero 部队。检查不过的事件要么被跳过，要么先调参数让战略层认同。

### 触发前检查流程

```
WorldEventSimulator 候选事件
  │
  ├─ IsGenericInstigator = true（是否为通用模版，即模板匪徒/天灾）
  │   → 直接通过，跳到执行
  │
  ├─ 涉及真人 Hero
  │   → 战略一致性评分（每项 +1 一致 / -1 冲突）：
  │   
  │   检查项 1：双方 relation < -10 → +1，否则 -1
  │   检查项 2：双方所属王国正在交战 → +1，否则 -1
  │   检查项 3：instigator trait 支持攻击行为
  │             (Mercy<0 或 Valor>0 或 Calculating>0) → +1，否则 -1
  │   
  │   score > 0 → 一致事件，优先触发
  │   score = 0 → 中立，次优先级
  │   score < 0 → 冲突事件，必须证明必要性：
  │     ├─ ConspiracyManager 要求的叙事线
  │     ├─ NemesisTracker 已排期的复仇
  │     └─ TutorialWindow 新手引导保障
  │     不满足 → 跳过不触发
  │
  └─ 通过 → 立即执行：
      ├─ 模板 party：spawn + AI 控制
      ├─ 真人 Hero：若 score < 0 则先调参数（ChangeRelationAction 等）让战略层认同
      │   然后征用/创建 Hero 部队 → V.SetMoveToTown（行军）+ SetDoNotMakeNewDecisions（锁定）
      │   到达目标 → CheckEventPartyArrivals → SetPartyAiAction（巡逻→劫掠/攻城）
      ├─ SyncEventToNpcMemory → CurrentUrgentEvent
      └─ 通知/推送立刻发出
```

**核心原则**：不是放弃接管，而是接管前确保战略层认同。战略层因为 relation / war state / trait 本就同意这个行为，导演只是加速并显式化。两个大脑发出同一个指令。

### 为何不放弃真人 Hero 劫持

- 原版战略层的决策周期是**天级**（DailyTick → KingdomDecision → Army 集结 → MobilePartyAi 重评估每 0.25 小时）。导演事件需要在**分钟级**让玩家看到世界动静
- 关系已调到位（战略层"愿意"打），但如果不接管，Hero 可能还在巡逻、围城、或者在另一个方向作战——需要 3-10 天才会自然移动到位
- 导演的作用：把"3 天后自然会发生的移动"压缩到"现在发生并立刻通知玩家"
- 到达目标后的后续行为（巡逻 → 开打）仍然由 `CheckEventPartyArrivals` → `SetPartyAiAction` 用原生 API 执行，与原版一致

### WorldEvent 与原版 Issue 的关系：物理事实 → 社会响应

WorldEvent **不直接生成 Issue**。它只制造"世界正在发生的事"（物理层），原版管线自己响应（社会层）：

```
因果引擎 → 生成 WorldEvent
  │
  ├─ 物理层（立即生效）：
  │   ├─ spawn party / 接管 Hero 部队 → 向目标行军
  │   └─ SyncEventToNpcMemory → 涉及 NPC 的 CurrentUrgentEvent 被设置
  │
  └─ 社会层（下一个 DailyTick）：
      └─ IssuesCampaignBehavior 扫描到有此 NPC
         → NPC 有 CurrentUrgentEvent → 过滤日常类 Issue
         → 生成关联的原版 Issue（如 BanditRaid → ExtortionByDeserters）
         → 玩家看到 ! → 接取原版 Quest
         → 完成后 → QuestConsequenceResolver → 新 WorldEvent → 循环
```

这和原版逻辑一致：原版也是先有"逃兵出现在村庄附近"（背后是概率），再有 `ExtortionByDesertersIssue`（村长的求助）。区别只在于"物理事实从哪来"——原版是随机概率，我们改成了因果引擎的产出。

### 攻城级事件（NobleConflict，贵族冲突）：启动原版军团系统

当前 `SpawnEventParty` 只处理单个 Hero 的 party。对于 `NobleConflict`（贵族冲突），解法是**利用原版 Army 集结机制**。其他 `RaidSettlement` 类型（`BanditRaid` 匪患劫掠、`DebtTrap` 债务陷阱）不涉及领主，走单 party + 辅助部队。

#### 事件规模分级（按 PartyBehavior）

| PartyBehavior | WorldEventType | 执行方式 |
|--------------|----------------|---------|
| `RaidSettlement` | `BanditRaid`（匪患劫掠）, `DebtTrap`（债务陷阱） | 单 party + 辅助部队 |
| `RaidSettlement` | `NobleConflict`（贵族冲突） | 创建原版 Army → 集结领主 → 军团行军 |
| `EngageTarget` | `Betrayal`（背叛）, `Assassination`（行刺） | 单 party |
| `PatrolNearTarget` | `Kidnapping`（绑架）, `RomanticConflict`（情仇）, `InheritanceDispute`（继承争端）, `Fugitive`（逃犯追捕）, `SacredTheft`（圣物失窃） | 单 party 在目标附近巡逻 |
| `NoParty` | `Famine`（饥荒）, `FalseAccusation`（冤案诬告）, `TradeDispute`（贸易争端） | 不生成 party |
| `ChasePlayer` | `NemesisRevenge`（宿敌复仇） | 追击 MainParty |

#### NobleConflict 流程

```
WorldEvent 候选: NobleConflict（Lord_A 进攻 Lord_B 的领地）
  │
  ├─ 战略一致性检查通过（score > 0，relation 已调低）
  │
  ├─ Lord_A 是 lord && 有所属 Kingdom
  │   → 创建原版 Army：
  │
  │   Army army = Lord_A.Clan.Kingdom.CreateArmy(
  │       Lord_A,                        // 军团长
  │       targetSettlement,              // 集结目标
  │       Army.ArmyTypes.Besieger        // 攻城型
  │   );
  │
  │   原版 Army 系统自动处理：
  │   ├─ 向同 Kingdom 领主发出集结令（Army.Gather）
  │   ├─ 各领主按距离/兵力/关系独立决策是否响应
  │   ├─ 响应的领主带队前往集结地点
  │   ├─ 部队陆续到达 → 军团兵力逐步增长
  │   └─ 军团长判定兵力足够 → 开始向目标行军
  │
  ├─ 导演通知玩家：
  │   "⚔ Lord_A 发布了集结令，正在召集军团向 Lord_B 的领地进发。
  │    目前已有 N 个领主响应，兵力约 X 人。"
  │
  ├─ Army 行军过程中：
  │   ├─ 导演不锁定单个 party 的 AI（Army 内部机制已确保向目标移动）
  │   ├─ 沿途可能遭遇拦截（原版 MobilePartyAi 自然响应）
  │   └─ 导演在关键节点推送：集结完成 / 抵达边境 / 开始围城
  │
  └─ 到达目标后：
      └─ 原版 Army 围城/突击逻辑接管
         （SetPartyAiAction.GetActionForBesiegingSettlement）
```

#### 与导演的关系

- **导演创建 Army，但不接管 Army 内部控制**。Army 的集结节奏、兵力判定、围城决策全部走原版。导演只在 Army 生命周期关键节点推送通知。
- **导演锁定的是目标**：`CreateArmy` 时指定 `targetSettlement`，军团长不会半路改主意去打别的城。但如何打（围城/突击/劝降）是原版 Army AI 的决定。
- **事件到期处理**：若 Army 在到达前解散（影响力耗尽/军团长被俘/内部争议），事件不直接标记为 Expired，而是：
  ```
  Army 解散
    → 导演检查解散原因
    ├─ 军团长被俘 → 军事行动失败 → 事件 Expired，通知玩家
    ├─ 影响力耗尽 → 降级为骚扰级：军团长单 party 继续（如果他还活着）
    └─ 内部争议 → 事件降级 + 通知："军团内部发生分歧——Lord_A 决定独自行动。"
  ```

#### 非军事级事件中的辅助部队

即使不是军事级事件，也可以 spawn **辅助部队**增加战斗真实感。现有 `AuxiliaryPartyConfig` 机制已支持：

```
BanditRaid（匪患劫掠）：
  ├─ 主 party：匪首带队冲向村庄
  └─ 辅助 party：1-2 股小匪帮在邻近路线巡逻（堵截过往商队）

NobleConflict（贵族冲突）：
  ├─ 主 Army：军团长 + 集结的领主
  └─ 辅助 party：SupplyConvoy（补给队，可被玩家拦截 → SupplyIntercept 委托）
                ScoutParty（斥候，增加视野范围）
```

辅助部队在 `WorldEventDatabase.AddEvent` 时同步 spawn，先于玩家接委托存在于世界。`CommissionQuest` 通过 `RoleTag` 查找复用。

#### 对现有代码的改动

1. `WorldEventSimulator.SpawnEventParty` 中增加军事级分叉：
   - 若 `config.EventType` 在军事级列表中且 `instigator != null && instigator.IsLord && instigator.Clan?.Kingdom != null`
   - → 调 `CreateArmy` 替代 `MobileParty.CreateParty`
   - → 记录 `army.StringId` 到 `WorldEventData`（新增字段 `ArmyId`）

2. `WorldEventData` 新增字段：
   ```csharp
   public string ArmyId;              // 关联的原版 Army.StringId
   public ArmyEventStatus ArmyStatus; // Active / Disbanded / Arrived
   ```

3. `WorldEventSimulator.DailyTick` 中新增 Army 状态监控：
   - 检查 `Army` 是否仍存在
   - 解散 → 按上述降级逻辑处理
   - 到达目标 → 进入 Patrol 阶段（现有逻辑复用）

---

## 三、CommissionCategory 审计结果

### 删除清单（与计划无关，仅记录决策）

#### A. 与原版重叠（9 种）— 删除原因：原版做得更好

| CommissionCategory | 原版对应 | 删除理由 |
|-------------------|---------|---------|
| `BountyHunt` | `VANILLA_LordWantsRivalCaptured` | 原版活捉/击杀 + DialogFlow |
| `HideoutClear` | `VANILLA_NearbyBanditBase` | 原版有藏身处场景，我们只监听 MapEvent |
| `CaravanEscort` | `VANILLA_EscortMerchantCaravan` | 原版有 voyaging 事件 + 伏击 |
| `EmergencyDelivery` | `VANILLA_ArmyNeedsSupplies` | 无额外玩法 |
| `SupplyEmergency` | `VANILLA_HeadmanNeedsGrain` | 无额外玩法 |
| `VillageDefense` | `VANILLA_ExtortionByDeserters` | 原版已有讨伐逃兵 |
| `HorseAcquisition` | `VANILLA_LordNeedsHorses` | Trade 砍价无实际机制 |
| `SupplyIntercept` | `VANILLA_RaidEnemyTerritory` | 原版已有截获 |
| `ProcurementAgent` | `VANILLA_ArtisanCantSell` | 等同 HorseAcquisition |

#### B. 半成品（4 种）— 删除原因：定义了但没做出来

| CommissionCategory | 宣称 | 实际 | 判断 |
|-------------------|------|------|------|
| `UndergroundFight` | 地下拳赛，押注 | 去竞技场赢一场 | 和原版 Tournament 无区别 |
| `ArenaSpecial` | 禁用盾牌，纯武器对决 | 连赢两场竞技场 | 无盾牌限制逻辑 |
| `LostItem` | Scout 搜索线索寻回 | 到目的地物品直接给 | 无搜索机制 |
| `TreasureHunt` | 藏宝图探索 | 到目的地宝藏直接给 | 无探索机制 |

### 保留清单

| CommissionCategory | 状态 | 后续工作 |
|-------------------|------|---------|
| `PrisonBreak` | ⚠️ 需完善 | 保留。三个方案(A贿赂/B潜入/C外交)只有日志文本，无实际 gameplay 实现 |
| `Theft` | 🔨 新建 | 依赖偷盗系统完工后新建 |
| `Scavenge` | 🔨 新建 | 依赖搜刮系统完工后新建 |

16 → 3。其余全部删除：

- `LegendaryHunt`：本质是 BountyHunt 数值放大 + 风味文本，"独有装备掉落"从未实现，`HandleBountyHuntVictory` 里无任何装备掉落逻辑
- `DecoyMission`：大地图追逃缺乏交互深度，玩家只有"跑（看速度）或打（看战力）"两个选项，与设计文案"边打边跑"矛盾；最优策略是带多兵反杀追兵，等价于普通战斗

---

## 四、Quest 全集

### A. 原版 40 种（直接复用）

#### 村庄要人（13 种）

| ID | 原版类名 | 简述 | 可叛变 | Alternative |
|----|---------|------|--------|-------------|
| `VANILLA_HeadmanNeedsGrain` | HeadmanNeedsGrainIssue | 村庄缺粮 | - | - |
| `VANILLA_HeadmanDeliverHerd` | HeadmanNeedsToDeliverAHerdIssue | 护送牲畜 | - | - |
| `VANILLA_VillageDraughtAnimals` | VillageNeedsDraughtAnimalsIssue | 需要耕畜 | - | - |
| `VANILLA_VillageNeedsTools` | VillageNeedsToolsIssue | 需要工具 | - | - |
| `VANILLA_VillageCraftingMaterials` | VillageNeedsCraftingMaterialsIssue | 需要制作材料 | - | - |
| `VANILLA_LandlordVillageCommons` | LandlordNeedsAccessToVillageCommonsIssue | 公地纠纷 | - | - |
| `VANILLA_LandlordManualLaborers` | LandLordNeedsManualLaborersIssue | 需要劳工 | - | - |
| `VANILLA_LandlordTradeArt` | LandLordTheArtOfTheTradeIssue | 交易纠纷 | - | - |
| `VANILLA_LandlordTraining` | LandlordTrainingForRetainersIssue | 训练家丁 | - | - |
| `VANILLA_ExtortionByDeserters` | ExtortionByDesertersIssue | 逃兵勒索 | - | ✅ |
| `VANILLA_FamilyFeud` | FamilyFeudIssue | 家族世仇 | ✅ | ✅ |
| `VANILLA_NotableDaughterFound` | NotableWantsDaughterFoundIssue | 寻找女儿 | - | - |
| `VANILLA_RuralNotableInnOut` | RuralNotableInnAndOutIssue | 酒后赌输地契 | - | - |

#### 城镇工匠/商人（6 种）

| ID | 原版类名 | 简述 | 可叛变 | Alternative |
|----|---------|------|--------|-------------|
| `VANILLA_ArtisanCantSell` | ArtisanCantSellProductsAtAFairPriceIssue | 产品滞销 | - | - |
| `VANILLA_ArtisanOverpricedGoods` | ArtisanOverpricedGoodsIssue | 原料垄断 | - | - |
| `VANILLA_EscortMerchantCaravan` | EscortMerchantCaravanIssue | 护送商队 | - | ✅ |
| `VANILLA_CaravanAmbush` | CaravanAmbushIssue | 商队遭伏击 | - | - |
| `VANILLA_BettingFraud` | BettingFraudIssue | 赌注欺诈 | - | - |
| `VANILLA_RevenueFarming` | RevenueFarmingIssue | 包税纠纷 | - | - |

#### 帮派头目（5 种）

| ID | 原版类名 | 简述 | 可叛变 | Alternative |
|----|---------|------|--------|-------------|
| `VANILLA_GangNeedsRecruits` | GangLeaderNeedsRecruitsIssue | 需要新兵 | - | - |
| `VANILLA_GangSpecialWeapons` | GangLeaderNeedsSpecialWeaponsIssue | 需要特殊武器 | - | - |
| `VANILLA_GangOffloadStolenGoods` | GangLeaderNeedsToOffloadStolenGoodsIssue | 销赃 | - | - |
| `VANILLA_RivalGangMovingIn` | RivalGangMovingInIssue | 敌对帮派入侵 | ✅ | - |
| `VANILLA_SnareTheWealthy` | SnareTheWealthyIssue | 诱捕富商 | ✅ | - |

#### 领主/贵族（10 种）

| ID | 原版类名 | 简述 | 可叛变 | Alternative |
|----|---------|------|--------|-------------|
| `VANILLA_LordNeedsHorses` | LordNeedsHorsesIssue | 需要战马 | - | - |
| `VANILLA_LordsNeedsTutor` | LordsNeedsTutorIssue | 需要家庭教师 | - | - |
| `VANILLA_LordWantsRivalCaptured` | LordWantsRivalCapturedIssue | 活捉仇敌 | - | - |
| `VANILLA_LadysKnightOut` | LadysKnightOutIssue | 贵妇出游 | - | - |
| `VANILLA_ProdigalSon` | ProdigalSonIssue | 浪子回头 | - | - |
| `VANILLA_TheSpyParty` | TheSpyPartyIssue | 间谍潜入 | - | - |
| `VANILLA_ArmyNeedsSupplies` | ArmyNeedsSuppliesIssue | 军队补给 | - | - |
| `VANILLA_ScoutEnemyGarrisons` | ScoutEnemyGarrisonsIssue | 侦察敌驻军 | - | - |
| `VANILLA_RaidEnemyTerritory` | RaidAnEnemyTerritoryIssue | 袭击敌境 | - | ✅ |
| `VANILLA_ConquestOfSettlement` | TheConquestOfSettlementIssue | 征服定居点 | - | - |

#### 通用/全局（6 种）

| ID | 原版类名 | 简述 | 可叛变 | Alternative |
|----|---------|------|--------|-------------|
| `VANILLA_NearbyBanditBase` | NearbyBanditBaseIssue | 清剿匪穴 | - | ✅ |
| `VANILLA_ArmyOfPoachers` | MerchantArmyOfPoachersIssue | 偷猎军队 | - | - |
| `VANILLA_Smugglers` | SmugglersIssue | 走私网络 | - | - |
| `VANILLA_CapturedByBountyHunters` | CapturedByBountyHuntersIssue | 赏金猎人的猎物 | - | - |
| `VANILLA_CompanyOfTrouble` | LandLordCompanyOfTroubleIssue | 佣兵闹事 | - | - |
| `VANILLA_LesserNobleRevolt` | LesserNobleRevoltIssue | 小贵族叛乱 | - | - |

### B. LivingWorldNpcs 新增（3 种）

| ID | Issue 类名 | 简述 | 委托人 |
|----|-----------|------|--------|
| `LWNPCS_PrisonBreak` | PrisonBreakIssue | 越狱营救（贿赂/潜入/外交） | GangLeader, Wanderer, Lord |
| `LWNPCS_Theft` | TheftCommissionIssue | 偷盗目标物品/NPC | GangLeader, Wanderer |
| `LWNPCS_Scavenge` | ScavengeIssue | 战后/灾后搜刮 | Wanderer, Headman |

---

## 五、因果链设计

### 链 1：匪穴清剿 → 区域安全变化

```
源: VANILLA_NearbyBanditBase

Success →
  ├─ VANILLA_ExtortionByDeserters  同区域30天抑制（不发生）
  ├─ VANILLA_EscortMerchantCaravan 同区域30天权重×2（商路安全了）
  └─ VANILLA_GangNeedsRecruits     15天后最近城镇（条件是匪首为named Hero且存活）

Fail →
  ├─ VANILLA_ExtortionByDeserters  同村severity+2，7天内强制生成
  └─ VANILLA_NearbyBanditBase      邻近村庄15-30天后新生成（匪帮扩张）
```

### 链 2：家族世仇 → 信任/仇恨分支

```
源: VANILLA_FamilyFeud

Success (Quest Solution说服) →
  ├─ VANILLA_LordNeedsHorses      委托人同clan成员10-20天后（家族信任你）
  └─ VANILLA_NotableDaughterFound 若世仇根源是失踪人口→根本原因委托浮现

Success (Lord Solution，拒绝CounterOffer) →
  └─ VANILLA_ScoutEnemyGarrisons  帮忙的领主7-15天后给委托

Betrayal (接受CounterOffer) →
  ├─ VANILLA_LordWantsRivalCaptured 委托人clan 5-15天后生成，目标=玩家
  ├─ VANILLA_ProdigalSon            受益方10-20天后（认为你腐败可用）
  └─ VANILLA_ExtortionByDeserters   委托人村庄15天后（无心管理）

Fail →
  ├─ VANILLA_LordWantsRivalCaptured 7-14天后，家族成员被杀→死者家族委托
  └─ 两家族同王国→可能触发VANILLA_LesserNobleRevolt
```

### 链 3：商队护送 → 市场连锁

```
源: VANILLA_EscortMerchantCaravan

Success →
  ├─ VANILLA_ArtisanOverpricedGoods 目的地城镇15天后（货到冲击物价）
  └─ VANILLA_RevenueFarming         委托人的城镇15-20天后（富了被盯上）

Fail →
  ├─ VANILLA_CaravanAmbush          同一路线7-15天后（匪帮扎了伏击点）
  └─ 若委托人Gold<5000→进入债务状态（关联VANILLA_LandlordTradeArt）

Betrayal →
  ├─ VANILLA_SnareTheWealthy        委托人雇帮派设局7-15天后，目标=玩家
  ├─ VANILLA_LordWantsRivalCaptured 若委托人是领主→额外生成
  └─ 该路线商人30天拒绝委托
```

### 链 4：帮派战争 → 地下洗牌

```
源: VANILLA_RivalGangMovingIn

Success →
  ├─ VANILLA_GangSpecialWeapons     委托人7天后（乘胜追击）
  └─ VANILLA_GangNeedsRecruits      敌对帮派10-20天后在邻近城镇招兵

Betrayal →
  ├─ VANILLA_LordWantsRivalCaptured 原委托人5-15天后悬赏玩家
  ├─ VANILLA_GangOffloadStolenGoods 原委托人城镇7天后（敌对帮派急于销赃）
  └─ 新雇主给委托时定金×2

Fail →
  ├─ VANILLA_GangOffloadStolenGoods 该城镇7天后（敌对帮派控制地盘）
  └─ VANILLA_GangNeedsRecruits      敌对帮派同城镇10天后巩固
```

### 链 5：寻找女儿 → 人口流动

```
源: VANILLA_NotableDaughterFound

Success (说服回家) →
  ├─ LWNPCS_PrisonBreak             恶棍被关押→同伙7-14天后越狱委托
  └─ VANILLA_HeadmanDeliverHerd     村庄Hearth+10→15-30天后卖牲畜

Success (女儿逃走) →
  └─ VANILLA_CapturedByBountyHunters 女儿变Wanderer→15-30天后在其他城镇被追捕

Fail →
  ├─ VANILLA_VillageNeedsTools      村庄15天后（委托人放弃管理）
  └─ VANILLA_FamilyFeud             10-20天后（委托人认定邻居告密）
```

### 链 6：间谍潜入 → 军事情报闭环

```
源: VANILLA_TheSpyParty

Success →
  └─ VANILLA_ScoutEnemyGarrisons    间谍faction 10-20天后报复侦察

Fail →
  ├─ VANILLA_RivalGangMovingIn      城镇安全-3→7-15天后
  ├─ VANILLA_Smugglers              同上
  └─ VANILLA_ConquestOfSettlement   20-30天后目标=该城镇（间谍方获得情报）
```

### 链 7：活捉仇敌 → 囚禁博弈

```
源: VANILLA_LordWantsRivalCaptured

Success (活捉) →
  ├─ LWNPCS_PrisonBreak             仇敌clan 7-14天后越狱委托
  │   ├─ 越狱成功→VANILLA_RaidEnemyTerritory 15天后目标=委托人村庄
  │   └─ 越狱失败→仇敌可能被处决→Nemesis
  └─ 仇敌clan与你的faction关系-30

Success (击杀) →
  ├─ VANILLA_ArmyNeedsSupplies      仇敌clan 7天内（备战）
  └─ VANILLA_RaidEnemyTerritory     15天内目标=你的faction领地

Fail →
  ├─ VANILLA_ScoutEnemyGarrisons    仇敌回clan→15天内侦察（警觉）
  └─ 同一委托人30天冷却
```

### 链 8：诱捕富商 → 三向分支

```
源: VANILLA_SnareTheWealthy

Success (帮帮派) →
  ├─ VANILLA_GangSpecialWeapons     帮派头目7-15天后
  └─ VANILLA_LordWantsRivalCaptured 被抢商人clan 10-20天后悬赏玩家

Betrayal (帮商人) →
  ├─ VANILLA_EscortMerchantCaravan  商人感激→10-15天后护送委托
  └─ VANILLA_RivalGangMovingIn      帮派头目成为Nemesis→10-20天后

Betrayal (两边通吃) →
  ├─ 双方各自生成Nemesis→VANILLA_LordWantsRivalCaptured
  └─ 恶名+5→该城镇正经委托权重-30%、帮派委托+30%
```

### 链 9：侦察 → 军事行动

```
源: VANILLA_ScoutEnemyGarrisons

Success →
  ├─ VANILLA_RaidEnemyTerritory     委托人3-7天后（情报生效）
  └─ VANILLA_ConquestOfSettlement   若城防空虚→7-15天后

Fail →
  └─ VANILLA_ArmyNeedsSupplies      被侦察城镇7-10天后加强防御→守军增加
```

### 链 10：叛变通用（所有可叛变Quest）

```
源: 任何调用了CompleteQuestWithBetrayal的Quest
    (VANILLA_FamilyFeud / VANILLA_SnareTheWealthy / VANILLA_RivalGangMovingIn)

Betrayal →
  ├─ VANILLA_LordWantsRivalCaptured 被背叛者5-15天后悬赏玩家（通过NemesisTracker）
  ├─ 被背叛者定居点口碑传播→30天内委托定金×1.5
  └─ 若恶名≥5→正经委托权重-30%、帮派委托+30%
```

### 链 11：逃兵勒索 → 匪帮—村庄攻防

```
源: VANILLA_ExtortionByDeserters

Success →
  └─ VANILLA_LordNeedsHorses        村庄安全恢复→lord 15-30天后正常委托

Fail →
  ├─ VANILLA_ExtortionByDeserters   同村15天后再次生成（逃兵觉得这里好欺负）
  └─ VANILLA_NearbyBanditBase       逃兵投靠匪穴→7-15天后匪穴活跃度+1
```

### 链 12：浪子回头 → 绑匪动态

```
源: VANILLA_ProdigalSon

Success →
  └─ VANILLA_HeadmanNeedsGrain      父亲恢复管理→10-20天后正常村庄委托

Fail →
  ├─ VANILLA_ExtortionByDeserters   绑匪失去筹码→7-15天后转直接勒索
  └─ VANILLA_LandlordTradeArt       父亲筹赎金变卖家产→10-15天后
```

### 链 13：酒馆赌输 → 债务下行

```
源: VANILLA_RuralNotableInnOut

Success →
  └─ VANILLA_NotableDaughterFound   赢家暴富→15-30天后女儿被盯上

Fail →
  └─ VANILLA_RevenueFarming         输家地契被收→包税人盯上→10-20天后
```

### 链 14：越狱成功/失败

```
源: LWNPCS_PrisonBreak

Success →
  ├─ VANILLA_LordWantsRivalCaptured 被救者有仇敌→15-30天后委托玩家
  └─ 关押城镇安全-2

Fail →
  ├─ 被救者可能被处决→其clan进入Nemesis
  └─ CriminalRating+ → 关押城镇守卫对玩家态度恶化
```

---

## 六、因果引擎数据结构

```csharp
/// <summary>任务因果后果解析器。所有原版+自定义Quest完成后统一走这里。</summary>
public static class QuestConsequenceResolver
{
    public enum QuestCompletionOutcome { Success, Fail, Betrayal, Timeout, Cancel }

    public struct FollowUpQuest
    {
        public string QuestId;           // 引用Quest全集ID（VANILLA_* / LWNPCS_*）
        public int DelayDaysMin, DelayDaysMax;
        public float Probability;        // 0~1
        public bool RequireTargetAlive;  // 源Quest的targetHero必须存活
        public bool RequireTargetDead;   // targetHero必须已死
        public bool RequireGiverAlive;   // 源Quest的questGiver必须存活
        public int? MinInfamy;           // 需要的最低恶名
    }

    /// <summary>主因果表：源QuestID → (完成方式 → 后续列表)</summary>
    private static readonly Dictionary<string, 
        Dictionary<QuestCompletionOutcome, List<FollowUpQuest>>> CausalityTable;

    /// <summary>主入口：Quest完成时由CampaignEventDispatcher触发</summary>
    public static void ResolveConsequences(
        string questId,                   // VANILLA_* 或 LWNPCS_*
        QuestCompletionOutcome outcome,
        string outcomeDetail,             // "Capture"/"Kill"/"Persuade"/"" 
        Hero questGiver,
        Hero targetHero,
        Settlement targetSettlement)
    {
        // 1. 查因果表 → 取 FollowUpQuest 列表
        // 2. 逐个检查条件 → 通过则排入 WorldEventDirector 调度队列
        // 3. 写 NPC 记忆（SingNpcMemorySystem）
        // 4. 调用 TrustSystem / InfamySystem / NemesisTracker
        // 5. 口碑传播（ReputationPropagation）
    }
}
```

#### 因果链数据驱动

因果链不写死在 C# 代码里。用 JSON 配置文件，`QuestConsequenceResolver` 启动时加载：

```json
[
  {
    "sourceQuest": "VANILLA_NearbyBanditBase",
    "outcome": "Success",
    "followUps": [
      { "quest": "VANILLA_ExtortionByDeserters", "action": "Suppress", "durationDays": 30 },
      { "quest": "VANILLA_EscortMerchantCaravan", "action": "BoostWeight", "multiplier": 2.0, "durationDays": 30 },
      { "quest": "VANILLA_GangNeedsRecruits", "delayMin": 7, "delayMax": 15, "probability": 0.7, "condition": "RequireTargetAlive" }
    ]
  },
  {
    "sourceQuest": "VANILLA_FamilyFeud",
    "outcome": "Betrayal",
    "followUps": [
      { "quest": "VANILLA_LordWantsRivalCaptured", "delayMin": 5, "delayMax": 15, "target": "Player" },
      { "quest": "VANILLA_ProdigalSon", "delayMin": 10, "delayMax": 20 },
      { "quest": "VANILLA_ExtortionByDeserters", "delayMin": 15, "delayMax": 15 }
    ]
  }
]
```

C# 侧只负责 `JsonConvert.DeserializeObject` + 查表 + 执行，不存任何因果逻辑。

#### 链长：每步只定义下一个，自然串联

JSON 里每条只定义**一步**：`(源 Quest, 完成方式) → [后续 Quest 列表]`。长度不由代码决定：

```
JSON 只存一步：
  VANILLA_NearbyBanditBase + Success → [VANILLA_GangNeedsRecruits]
  VANILLA_GangNeedsRecruits + Success → [VANILLA_GangSpecialWeapons]
  VANILLA_GangSpecialWeapons + Success → [VANILLA_GangOffloadStolenGoods]

运行时自然串联：
  玩家完成 NearbyBanditBase
    → 查表 → 生成 GangNeedsRecruits
    → 玩家完成 GangNeedsRecruits
      → 查表 → 生成 GangSpecialWeapons
      → 玩家完成 GangSpecialWeapons
        → 查表 → 生成 GangOffloadStolenGoods
        → 玩家完成 GangOffloadStolenGoods
          → 查表 → 无后续条目 → 断链
```

多步链的存在条件就是 JSON 里为每一步的产出都继续定义了规则。没定义就自然断链。

---

## 六、叙事分层：三种模式的文本来源

同一个 Quest，三种呈现方式共用同一个底层数据，文本来源不同：

### 模式 A：原版对话（无 LLM）

```
玩家用原版 "Is there anything I can do for you?" 接任务
  → IssueBase.IssueQuestSolutionExplanationByIssueGiver（原版 TextObject 模板）
  → QuestBase.OnCompleteWithSuccess 的固定 TextObject
  → 不做任何增强
```

### 模式 B：StoryDialog（无 LLM）

```
玩家用 StoryDialog "【闲聊】" 接任务
  → NarrativeResolver 查 Narrative.csv
     ├─ 匹配维度：QuestType + NPC_Personality + PlayerRelation + TrustLevel
     ├─ 命中 → 填充变量（含因果变量，见下）
     └─ 未命中 → 回退到模式 A 的原版 TextObject（兜底）
  → 完成时同样走 CSV 查 CommissionClosure 模板
```

CSV 模板结构（已有 `Narrative.csv` + `CommissionNarrative.csv`，需统一）：
```
QuestType, Personality, TrustLevel, Outcome, Text
BountyHunt, Aggressive, High, Success, "干得漂亮！{TARGET_NAME}那家伙..."
BountyHunt, Cautious, Low,  Success, "嗯……事办成了。这是你的报酬。"
```

**因果变量**（CSV 和 LLM 共用，由 `NarrativeResolver` 统一注入）：

| 变量 | 含义 | 来源 |
|------|------|------|
| `{PREVIOUS_QUEST}` | 上一个完成的委托简述 | 因果链 JSON 的源 Quest |
| `{CAUSE_HERO}` | 引发当前委托的关键人物 | WorldEvent.InstigatorHero |
| `{CAUSE_EVENT}` | 引发当前委托的事件简述 | WorldEvent.EventType |
| `{CHAIN_DEPTH}` | 这是因果链的第几步 | `QuestConsequenceResolver` 运行时计数 |
| `{NPC_MEMORY}` | 此 NPC 和玩家的上一次互动摘要 | `SingNpcMemorySystem` |

模板示例（传入因果上下文后）：
```
"{NPC_MEMORY}
 因为 {CAUSE_HERO} 的 {CAUSE_EVENT}，
 现在 {CAUSE_HERO} 的副手正在组织人手要把 {TARGET_NAME} 从监狱里劫出来。
 帮我阻止他们。"
```

### 模式 C：StoryDialog（有 LLM）

```
玩家用 StoryDialog "【闲聊】" 接任务
  → NarrativeResolver 先尝试 CSV（同模式 B）
  → CSV 命中 → 以 CSV 文本为基础，LLM 做风味增强（1-2 句追加）
  → CSV 未命中 → LLM 全量生成，prompt 包含：
     ├─ NPC 记忆上下文（上次互动、TrustLevel、关系变化）
     ├─ 因果链上下文（{PREVIOUS_QUEST}、{CAUSE_HERO}、{CAUSE_EVENT}、{CHAIN_DEPTH}）
     ├─ 当前 CurrentUrgentEvent（NPC 正在经历什么）
     ├─ Quest 类型和目标
     └─ Settings.Instance.WorldDescription（世界观 flavor）
  → LLM 不可用或超时 → 回退到 CSV → 回退到原版 TextObject
     ├─ Quest 类型和目标
     └─ Settings.Instance.WorldDescription（世界观 flavor）
  → LLM 不可用或超时 → 回退到 CSV → 回退到原版 TextObject
```

LLM prompt 模板示例：
```
你是 {NPC_NAME}（{PERSONALITY}，{OCCUPATION}）。
你和玩家的关系：{RELATION_DESC}，信任度：{TRUST_LEVEL}。
{?CHAIN_DEPTH > 1}之前玩家帮你做了 {PREVIOUS_QUEST}，因为那件事，{CAUSE_HERO} 的 {CAUSE_EVENT} 引发了现在的局面。{/CHAIN_DEPTH}
你当前正经历：{URGENT_EVENT_DESC}。
你需要玩家帮你：{QUEST_DESC}。
用第一人称，{WORLD_FLAVOR}风格，2-3句话请玩家帮忙。如果这是因果链的延续，自然提到前因后果。
```

### 三种模式的选择逻辑

```
玩家与 NPC 对话
  ├─ 走原版对话选项 → 模式 A（永远可用）
  └─ 走 StoryDialog "【闲聊】" → 
      ├─ Settings.Instance.IsLLMReady → 模式 C（LLM）
      └─ IsLLMReady = false → 模式 B（CSV 模板）
```

---

## 七、实施路线图

### Phase 1：清理 + 桥接（P0）

1. **删除 13 种 CommissionCategory** 对应的 CommissionDef、OnStart*、Handle* 方法、switch case 分支

2. **实现过滤（Harmony prefix）**：prefix `IssueManager.AddPotentialIssueData`。
   - 入参：`Hero hero, PotentialIssueData pid`，其中 `pid.IssueType` 是 `typeof(HeadmanNeedsGrainIssue)` 等具体类型
   - 逻辑：读 `AllNpcMemoryManager.GetMemory(hero.StringId)?.CurrentUrgentEvent`。若 NPC 有紧迫事件，当前 `IssueType` 与该事件语义冲突（需建一个 `IssueType → 适用/不适用` 的映射表），则 `return false` 跳过原始方法
   - 映射表示例：
     ```
     CurrentUrgentEvent: BanditRaid（匪患劫掠）
       → 允许: ExtortionByDeserters, NearbyBanditBase, VillageDefense
       → 阻止: HeadmanNeedsGrain, VillageNeedsTools, HeadmanDeliverHerd
         （村庄在被劫掠，不应发布日常经营类委托）
     ```
   - 若无紧迫事件 → 全通过，不干预

3. **实现补充**：在 `OnCheckForIssueEvent` 中注入 LWNPCS 独有类型（PrisonBreak/Theft/Scavenge）的 `PotentialIssueData`。频率用 `IssueFrequency.Common`（3 分），与原版 Common 类型平等竞争。**不在当前 Phase 做**——Theft/Scavenge 的 IssueBase 子类尚未实现，PrisonBreak 待完善。

4. **CommissionIntent 重写**：
   - 保留：检测 NPC 是否有委托可接 → 是 → 展示
   - 改变：不再调用 `CommissionGenerator.GenerateCommissions`（旧逻辑），改为：
     ```
     if (NPC 有原版 Issue) → 走原版路径：NarrativeResolver 包装叙事 → 玩家接取 → 原版 Quest.StartQuest()
     if (NPC 有 LWNPCS Issue) → 同样走原版管线（因为 LWNPCS Issue 也是 IssueBase 子类，已注册到 IssueManager）
     ```
   - `ConfirmCommissionIntent`（告示板→找委托人）逻辑保留，但底层从 `CommissionQuest` 换成原版 Quest
   - `CollectCommissionRewardIntent` 改为通用：检测原版 Quest 是否已完成但未领报酬

5. **WorldEvent 战略一致性前置**：在 `TryGenerateMotivatedEvent` / `TryGenerateEvent` 中插入战略一致性评分检查。一致事件优先触发；冲突事件需证明必要性。若不一致但必须触发，先调 `ChangeRelationAction` / `DeclareWarAction` 让战略层认同，再接管 Hero 部队执行。

### Phase 2：因果引擎（P0）

6. **实现 `QuestConsequenceResolver`**：监听 `CampaignEvents.QuestCompleted`，查因果表，生成后续事件

7. **活捉 vs 击杀检测**：在 `QuestCompleted` handler 中，取 Quest 关联的 `targetHero`：
   ```
   if (targetHero != null && targetHero.IsPrisoner && 
       MobileParty.MainParty?.PrisonRoster 包含 targetHero)
       → "Capture"
   else if (targetHero != null && !targetHero.IsAlive)
       → "Kill"
   else
       → ""  // 不适用细分
   ```
   对于 `LordWantsRivalCaptured` 这种原版就区分活捉/击杀的 Quest，`IsPrisoner` 状态在 `CompleteQuestWithSuccess` 之前已被设置，`QuestCompleted` 事件触发时状态已确定。

8. **实现因果表 14 链**：首发覆盖链 1-10（最关键的），链 11-14 后续补

9. **口碑传播 `ReputationPropagation`**：在定居点 Notable 间传播玩家行为

### Phase 3：独有玩法维护（P1）

8. **PrisonBreak 完善**：实现 A/B/C 三个方案的 gameplay 逻辑
9. **Theft Commission**：偷盗系统完工后新建
10. **Scavenge Commission**：搜刮系统完工后新建

### Phase 4：叙事分层（P2）

12. **统一 CSV 模板**：合并 `Narrative.csv` 和 `CommissionNarrative.csv` 为单表，覆盖 40 种原版 QuestType × 常见 Personality × TrustLevel 的组合
13. **LLM prompt 模板**：实现模式 C 的 prompt 组装（NPC 记忆上下文 + UrgentEvent + Quest 描述），走 `LLMService.ChatAsync`
14. **三层 fallback**：模式 C → 模式 B → 模式 A，每层失败自动降级

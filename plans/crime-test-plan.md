# 犯罪后果引擎 — 瀑布式测试计划

> **目标**：按玩家体验的时间顺序，逐环节验证数据产生和内容展示是否正常。
> **核心原则**：每一步都先在日志里印证数据正确，再看游戏表现。避免"写了一堆代码进游戏啥也看不到"。

---

## 背景

本地代码已完整实现 [crime-consequence-composable-v3.md](plans/crime-consequence-composable-v3.md) 的 Phase 1-4：
- **Phase 1**: WorldEvent 数据模型 + WorldEventStore + PlayerTheftLedger + InvestigationEngine + DailyTick
- **Phase 2**: AttitudeSystem (NpcStance/ComputeStance) + ResponseGenerator
- **Phase 3**: 17 个 AccountabilityIntents + DialogueInjector "INTENT:xxx" 委托 + 8 个 JSON 对话文件
- **Phase 4**: CommissionGenerator 从 WorldEvent 生成追责 Quest (TryGenerateAccountabilityQuest)

Phase 5 (LLM) 和 Phase 6 (新犯罪类型) 不在本次测试范围。

---

## 日志验证方法

所有代码路径都通过 `DebugLogger.Log()` 写入 `{模块根目录}/Debug/StoryEngine_RuntimeLog.txt`。
每条日志带 `[HH:mm:ss.fff]` 时间戳。

**验证方式**：每次操作后打开日志文件，搜索对应关键词确认数据流。

**重要**：每次启动游戏日志会被清空，所以每轮测试启动一次游戏即可。

---

## 测试流程一：偷动物（无目击）→ 被发现 → 调查 → 玩家介入

这是最完整的 happy path，覆盖从犯罪到结案的全部六个阶段。

### Step 1.1：进入村庄场景

| 项目 | 内容 |
|------|------|
| **玩家操作** | 大地图走到一个村庄，进入场景 |
| **预期游戏表现** | 正常进入村庄场景，能看到村民和动物 |
| **本步无日志** | — |

---

### Step 1.2：偷一只动物（无目击者）

| 项目 | 内容 |
|------|------|
| **玩家操作** | 靠近一只动物（羊/牛/猪），按交互键偷走 |
| **代码路径** | `InteractionMissionView.TryStealAnimal` → `RecordAnimalTheftCrime` |
| **产生的数据** | |
| | `WorldEvent` 创建（[WorldEvent.cs:1240-1272](ExampleModVS/ExampleMod/ExampleMod/Stealth/WorldEvent.cs#L1240-L1272)）： |
| | · EventId = `"theft_" + settlement.StringId + "_" + CampaignTime.Now.ToDays + "_" + 序号` |
| |   例：`"theft_village_ES3_15.3_1"` — 村庄ID_游戏天数_该村第N次偷窃（自增序号） |
| | · InitiatorId = `Hero.MainHero.StringId` — **玩家的 Hero.StringId**（如 `"main_hero"`） |
| | · Type = `EventType.Theft_Animal`, Severity = 30 |
| | · Stage = **Dormant**（无目击）/ **Active**（有目击）, SuspectHeroId = null（无目击）/ 玩家 ID（有目击） |
| | · InvestigationProgress = 0（无目击）/ 1.0（有目击）, PublicAwareness = 0（无目击）/ 0.5（有目击） |
| | `PlayerTheftLedger` 记账（[WorldEvent.cs:1276-1281](ExampleModVS/ExampleMod/ExampleMod/Stealth/WorldEvent.cs#L1276-L1281)）： |
| | · TheftRecord(victimHeroId=null, settlementId, itemId, count=1, locationName="在{村庄名}") |
| | 目击检测：`StealManager.GetWitnesses(Agent.Main, animal, maxDistance=20f)` |
| **日志关键词** | |
| | ✅ `[TryStealAnimal]` — 偷窃动作触发，含 animal monsterId 和对应 ItemObject |
| | ✅ `[TheftLedger] Recorded: {itemId} x1 from {settlementId}` — 账本记录 |
| | ✅ `[WorldEvent] New event: Theft_Animal id=theft_xxx settlement=xxx severity=30` — 事件创建 |
| | ✅ `[AnimalTheft] No witnesses.` — 无目击（Dormant 路径） |
| | ✅ `[AnimalTheft] Witnessed! N hero(es) + M villagers saw the theft. Suspect = Player.` — 有目击（Active 路径） |
| **验证点** | 日志中出现完整 EventId（含 settlement ID + 天数 + 随机数），InitiatorId 与 Hero.MainHero.StringId 一致 |

---

### Step 1.3：离开村庄，等待 DailyTick（过夜）

| 项目 | 内容 |
|------|------|
| **玩家操作** | 离开村庄，在大地图按空格快进到次日 DailyTick |
| **代码路径** | `MyBehavior.DailyTick` → `WorldEventStore.ProcessDaily` → `ProcessDormant` |
| **产生的数据** | `WorldEvent.Stage` = Dormant → **Emerging**, `PublicAwareness` = `Math.Max(0.1f, 当前值)` |
| | 普通案件：0 → 0.1；村庄警觉案件：0.3 → 0.3（不被覆盖） |
| | 有目击案件：Stage 直接是 Active，不经过此步 |
| | **同时**：WasBroadcast=false → `SocialEventManager.BroadcastWorldEvent` 传播一次 |
| | **同时**：`AdvanceInvestigation` 首次推进（后台调查开始） |
| **日志关键词** | |
| | ✅ `[WorldEvent] theft_xxx Stage → Emerging (discovered)` — 村民发现了 |
| | ✅ `[SocialEvent] BroadcastWorldEvent:` — 传播系统收到事件 |
| **验证点** | 日志中出现 "Stage → Emerging"；此时蓝色 `!` 已就绪，玩家进村即可见 |

> **关键区分**：`InvestigationProgress` 是**后台机制**——即使玩家不闻不问，NPC 自己也会慢慢查出嫌犯。
> 玩家不需要等它满——Emerging 阶段就可以进村接调查 Quest（蓝色 `!`），主动参与"找出嫌犯"。

---

### Step 1.4：大地图查看村庄 — 蓝色 `!` 出现（Emerging → 调查 Quest）

| 项目 | 内容 |
|------|------|
| **玩家操作** | 在大地图上靠近村庄（不需进入），观察村庄上的 `!` 标记 |
| **代码路径** | 原版 `OnCheckForIssue` 定期触发（大地图靠近定居点时） |
| | → `CommissionIssueBehavior.TryAddIssue(hero)` |
| | → `CommissionGenerator.HasCommissionsFor(hero)` → `IsAuthorityForActiveCrimeEvent(hero)` |
| | → `IssueManager.AddPotentialIssueData(hero, factory)` — **原版 API，非自定义** |
| | → `factory` → `new CommissionHubIssue(hero)` — 继承 `IssueBase`（原版基类） |
| | 判定条件：Hero 的定居点有活跃 WorldEvent（Stage != Dormant）且 Hero 是权威 NPC |
| **产生的数据** | CommissionData(WorldEventId=evt.EventId, TargetHero=**null**, Stage=Emerging) |
| **日志关键词** | |
| | ✅ `[CommissionGen] Accountability Investigation quest: hero=xxx event=theft_xxx stage=Emerging` — 后台 Quest 数据准备 |
| | ✅ `[CommissionIssue]` — Issue 信号刷新（如触发） |
| **预期游戏表现** | 村庄上出现**蓝色 `!`**，鼠标悬停可见提示 |
| **验证点** | `!` 在大地图可见（不依赖进村）；TargetHero 为 null（还不知道嫌犯） |

> **Issue 注册时机**：`OnCheckForIssue` 是原版事件，玩家在大地图靠近定居点或定期触发。
> 不需进村——WorldEvent Stage → Emerging 后，下次 `OnCheckForIssue` 触发时 `!` 就出现了。

---

### Step 1.5：进村与村长对话 — 验证动态对话注入

| 项目 | 内容 |
|------|------|
| **玩家操作** | 进入村庄场景，找 Headman notable 对话 |
| **代码路径** | `CampaignMapConversation.OpenConversation`（原版）→ `CrimeConversationPatch.Postfix` |
| | → `WorldEventStore.FindActive(settlementId)` — 查活跃犯罪事件 |
| | → `CrimeDialogueBuilder.BuildScript(speaker, listener)` — **运行时动态构建** |
| | → `BuildAuthorityScript` → Stage=Emerging 且未接任务 → `BuildDiscoveryTurn` |
| | → 对话文本由 `PlaceholderResolver` 动态填充：`{SpeakerEmotion}` `{CrimeScene}` `{CrimeVerbPast}` 等 |
| | → 每个 turn 的选项用 `INTENT:xxx` → `IntentRegistry.FindByName` |
| | → `DialogueInjector.InjectScript(script)` — 注入 ConversationManager |
| **日志关键词** | |
| | ✅ `[CrimePatch] Injected crime dialogue: event=theft_xxx stage=Emerging partner=村长 turns=N` |
| | ✅ `[Placeholder] NpcLine: "（{SpeakerEmotion}地）{TimeWord}{TargetSettlementName}的…" → "（叹了口气）前两天曹村的牲口圈牲口被偷了…"` |
| |   — **模板→结果**对照，一行看两个版本。有 context 标签的 `Resolve` 调用才会打印。 |
| | ✅ `[CrimeDialog] Turn[start] speaker=村长 stage=Emerging` — turn 标识 |
| | ✅ `[CrimeDialog]   NPC: "（叹了口气）前两天曹村的牲口圈…"` — 最终文本（同 Placeholder 的结果行） |
| | ✅ `[CrimeDialog]   Option: "我可以帮忙查查是谁干的" → INTENT:Investigate` |
| | ⚠️ `[Placeholder] UNRESOLVED in 'NpcLine': SpeakerEmotion` — 有占位符未替换 |
| **验证点** | 对比 `[Placeholder]` 的模板和结果：模板中的 `{xxx}` 在结果中应被替换为实际数据 |
| | 若结果中仍残留 `{xxx}` → 对应 `ResolveOne` 返回了 null，且会有 `UNRESOLVED` 日志 |

> **三条路径同一出口（CrimeDialogueBuilder.cs 注释）**：
> - 路径 A（静态调试）：手写 JSON → DialogueInjectScript
> - 路径 B（生产）：**游戏状态 → CrimeDialogueBuilder.BuildScript → DialogueInjectScript** ← 本次走这条
> - 路径 C（LLM 增强）：游戏状态 → LLM → DialogueInjectScript

---

### Step 1.5a：[分支] 玩家自首 — Emerging 阶段主动认罪

| 项目 | 内容 |
|------|------|
| **触发条件** | Step 1.5 对话中，玩家是贼（`InitiatorIsPlayer`），选"（低头）是我干的" (`INTENT:Confess`) |
| **玩家操作** | 在发现对话中选自首 → ConfessIntent.OnInstant 将 SuspectHeroId 设为玩家 → 进入 confess turn |
| **代码路径** | `BuildDiscoveryTurn` → 检测 `InitiatorIsPlayer` → 插入"主动认栽"选项 → `NextNode=confess` |
| | → `BuildConfessTurn`：三个选项：赔钱 / Charm辩护 / 转身走 |
| **预期游戏表现** | NPC 回应："你？！……好，既然自己认了，咱们可以商量。" |
| | 三个选项：① 赔钱 (`INTENT:PayRestitution`) ② Charm辩护 (`INTENT:CharmDefense`) ③ 转身走 |
| | 选任意选项后 → **收尾 turn**：NPC 最后一句（`{ConfrontClosingLine}`）→ 玩家点"……"→ 关闭窗口 |
| **日志关键词** | |
| | ✅ `[CrimeDialog] Turn[confess]` — 进入认罪 turn |
| | ✅ `[Accountability] Player paid restitution` — 选赔钱 → 结案 |
| | ✅ `[Accountability] Charm defense succeeded` — 选辩护成功 → 嫌犯降级 |
| **验证点** | 自首赔钱后 `Stage=Resolved, ResolvedBy="payment"`；辩护成功后 `SuspectHeroId=null, Stage→Emerging` |

---

### Step 1.5b：[分支] 玩家接调查任务 → 汇报调查 → 栽赃嫌犯

| 项目 | 内容 |
|------|------|
| **触发条件** | Step 1.5 对话中选"我可以帮忙查查是谁干的" (`INTENT:Investigate`) |
| **玩家操作** | 接任务 → 再次与村长对话（此时 `PlayerTookInvestigationQuest=true`） |
| **代码路径** | `BuildAuthorityScript` → Stage=Emerging + PlayerTookInvestigationQuest=true → `BuildReportTurn` |
| **产生的数据** | `WorldEvent.PlayerTookInvestigationQuest = true` |
| **日志关键词** | |
| | ✅ `[Accountability] Player accepted investigation quest for theft_xxx` |
| | ✅ `[CrimeDialog] Turn[start] speaker=村长 stage=Emerging` — 进入汇报 turn |
| | ✅ `[CrimeDialog]   NPC: "怎么样，查到什么了吗？"` |
| | ✅ `[CrimeDialog]   Option: "是附近藏身处的强盗干的！" → INTENT:FrameSuspect` |
| | ✅ `[CrimeDialog]   Option: "是 {TargetName} 干的——[出示{ItemName}]" → INTENT:FrameSuspect`（每件赃物一条——有证物时展开） |
| | ✅ `[CrimeDialog]   Option: "是 {TargetName} 干的。" → INTENT:FrameSuspect`（无证物时裸指控） |
| | ✅ `[CrimeDialog]   Option: "还没查到什么。"` |
| **预期游戏表现** | NPC 问"查到什么了吗？"，选项包含： |
| | ① "是强盗干的" (DC 40) |
| | ② 有证物的受害者——**按赃物展开**：每件赃物独立一行，"是张三干的——[出示匕首]""是张三干的——[出示银戒指]"。NPC 回应引用物品名："仔细看了看匕首……这确实是他的东西。" |
| | ③ 无证物的受害者——单条裸指控："是王五干的。" |
| | ④ "还没查到什么"（继续等待后台调查）|
| | 如果玩家是贼 → 额外显示"（低头）……是我干的"（同 Step 1.5a 自首） |
| **验证点** | 栽赃候选来自 `PlayerTheftLedger.GetFrameableTargets()`；有证物时每条选项由 `GetEvidenceItems(heroId)` 展开——一件赃物 = 一条选项；选项文本中嵌入物品名 |

### Step 1.5c：[分支] 栽赃成功 → 嫌犯锁定 → Stage → Active

| 项目 | 内容 |
|------|------|
| **触发条件** | Step 1.5b 中选栽赃选项，检定通过 |
| **玩家操作** | 选"是强盗干的"或"是{Hero}干的" |
| **代码路径** | `FrameSuspectIntent.OnSuccess` → `SuspectHeroId = targetId`, `InvestigationProgress = 1.0`, `Stage → Active` |
| **产生的数据** | Stage=Active, SuspectHeroId=被栽赃者（强盗/null/Hero） |
| **日志关键词** | |
| | ✅ `[SkillCheck] FrameSuspect | [单次检定] 目标=... 阈值=... 技能胜率=... 献礼占比=... 性格倍率=... → 成功率=... | 掷骰=通过` — **先看这行再判断** |
| | ✅ `[Accountability] Frame suspicion: {targetId} blamed for theft_xxx` |
| | ✅ `[WorldEvent] theft_xxx Stage: Emerging → Active` |
| **后续** | 嫌犯≠玩家 → 村长生成悬赏 Quest（黄色 `!`）→ 见 Step 1.7b / 测试流程四 |
| | 嫌犯=玩家（fail forward 两次失败后）→ 见 Step 1.7 |

### Step 1.5d：[分支] 栽赃失败 → fail forward

| 项目 | 内容 |
|------|------|
| **触发条件** | Step 1.5b 中选栽赃选项，检定失败 |
| **玩家操作** | 栽赃检定失败 |
| **代码路径** | `FrameSuspectIntent.OnFail` → `FailCount++` |
| **日志关键词** | |
| | ✅ `[SkillCheck] FrameSuspect | … 掷骰=失败` — **先看检定不等式，确认为什么不过** |
| | `FailCount=1`：无特殊日志，NPC 说"这次就算了，你再去查查" |
| | `FailCount>=2` 且玩家是真凶：`[Accountability] Frame suspicion failed twice — suspect reverts to player` |
| **验证点** | 第一次失败可再试；第二次失败嫌疑转回玩家 → 直接进 Step 1.7 对峙 |

---

### Step 1.6：[分支] 玩家不接任务 — 后台调查推进 → 嫌犯锁定

| 项目 | 内容 |
|------|------|
| **触发条件** | 玩家不接调查 Quest，在大地图继续等待 |
| **玩家操作** | 等待多个 DailyTick，直到 `InvestigationProgress >= 1.0` |
| **代码路径** | `ProcessEmerging` → `AdvanceInvestigation`（每日） → `TryLockSuspect` |
| **时间参数** | `AnimalTheft.BaseInvestigationRate = 0.25/天`；无目击无证据 ≈ 4 天到 1.0 |
| | 有目击者：+0.15/人 → 约 2.5 天；有证据：额外加成 |
| | 冷案超时：`InvestigationWindowDays = 7天`（超时 → Unsolved） |
| **产生的数据** | InvestigationProgress 满 → SuspectHeroId 填入 → Stage → **Active** |
| **日志关键词** | |
| | ✅ `[Investigation] theft_xxx DailyTick: progress=0.52 (+0.27) base=0.25 witness=0.00 evidence=0.00 closeness=0.00 counter=0.05 suspect=null` — **每日进度** |
| | 每个 DailyTick 都会输出一行，可以看到 progress 从 0 涨到 1.0 的全过程 |
| | ✅ `[Investigation] theft_xxx Suspect locked: {heroId}` — 进度满时锁定 |
| | 若锁定玩家 → Stage=Active, SuspectIsPlayer=true → 回村见 Step 1.7 |
| | 若无头绪 → `Cold case — no suspect identified` → Unsolved |
| **验证点** | 这是从蓝色 `!` 到黄色 `!` 的关键转折——嫌犯从"未知"变成"某人" |

---

### Step 1.7：嫌犯=玩家时 — 回村与村长对峙（黄色 `!`）

| 项目 | 内容 |
|------|------|
| **前置** | Step 1.6 后台调查锁定玩家，或 Step 1.5d 栽赃两次失败转回玩家 |
| **玩家操作** | 回村与村长对话（此时 Stage=**Active**, Suspect=**Player**） |
| **代码路径** | `BuildConfrontPlayerTurn` → 四个选项 |
| **预期游戏表现** | 村长头上出现**黄色 `!`**；冷脸："村里人都说是你干的"。四个选项： |
| | ① Charm 辩护 (`INTENT:CharmDefense`) |
| | ② 赔钱消灾 (`INTENT:PayRestitution`) |
| | ③ 威胁 (`INTENT:Threat`) |
| | ④ 转身走 (`NONE`) |
| | **收尾**：选任意选项后 → `confront_close` turn → NPC 最后一句（`{ConfrontClosingLine}`：按态度四档选不同台词）→ 玩家点"……"→ 关闭窗口 |
| **日志关键词** | |
| | ✅ `[CrimeDialog] Turn[start] speaker=村长 stage=Active` — 对峙 turn |
| | ✅ `[CrimeDialog]   NPC: "（冷冷地）你还敢来？…"` |
| | ✅ `[CrimeDialog]   Option: "你们搞错了…" → INTENT:CharmDefense` |
| | ❌ **不会出现** `[CommissionGen] Accountability Bounty quest:` — SuspectIsPlayer 时 TryGenerateAccountabilityQuest 直接 return null |

### Step 1.7b：[分支] 嫌犯≠玩家 — 悬赏 Quest（黄色 `!`）

| 项目 | 内容 |
|------|------|
| **前置** | Step 1.5c 栽赃成功（嫌犯=强盗/某人），或 Step 1.6 后台调查锁定 NPC |
| **玩家操作** | 与权威 NPC 对话（此时 Stage=**Active**, Suspect≠Player） |
| **代码路径** | `BuildBountyOfferTurn` → 悬赏选项 |
| **预期游戏表现** | NPC 告知嫌犯身份和赏金："查清楚了——是{SuspectDescription}干的。村上凑了{BountyAmount}第纳尔悬赏…" |
| | 选项：① 接悬赏 (`INTENT:AcceptBountyQuest`) ② 先想想 |
| **日志关键词** | |
| | ✅ `[CrimeDialog] Turn[start] speaker=村长 stage=Active` |
| | ✅ `[CrimeDialog]   NPC: "还记得前两天牲口被偷了的事吗？查清楚了…"` |
| | ✅ `[CrimeDialog]   Option: "我接这个悬赏！" → INTENT:AcceptBountyQuest` |
| | ✅ `[Accountability] Player accepted bounty quest for theft_xxx` |
| | ✅ `[CommissionGen] Accountability Bounty quest: hero=xxx suspect=xxx reward=xxx` |
| **后续** | 接悬赏 → 追捕 Quest 创建 → 大地图追踪目标 → 见测试流程四 |

---

### Step 1.8a：分支 — Charm 辩护成功

| 项目 | 内容 |
|------|------|
| **玩家操作** | 选"Charm 辩护" |
| **代码路径** | `CharmDefenseIntent.OnSuccess` → `WorldEventStore.OnCharmReprieve` |
| **产生的数据** | SuspectHeroId=null, PublicAwareness=0.5, Stage 退回 Emerging, CharmReprieveUsed=true |
| **日志关键词** | |
| | ✅ `[SkillCheck] CharmDefense | [单次检定] … 掷骰=通过` — **检定通过，确认五项输入合理** |
| | ✅ `[Accountability] Charm defense succeeded for theft_xxx — suspect downgraded` |
| | ✅ `[WorldEvent] theft_xxx Stage: Active → Emerging` |

### Step 1.8b：分支 — Charm 辩护失败

| 项目 | 内容 |
|------|------|
| **玩家操作** | 选"Charm 辩护"但检定失败 |
| **代码路径** | `CharmDefenseIntent.OnFail` |
| **产生的数据** | Trust -10, Stage → Confrontation |
| **日志关键词** | |
| | ✅ `[SkillCheck] CharmDefense | … 掷骰=失败` — **检定失败，看五项输入定位原因** |
| | ✅ `[Accountability] Charm defense failed — → Confrontation` |

### Step 1.8c：分支 — 赔钱消灾

| 项目 | 内容 |
|------|------|
| **玩家操作** | 选"赔钱消灾" |
| **代码路径** | `PayRestitutionIntent.OnInstant` → `WorldEventStore.OnPlayerPaidRestitution` |
| **产生的数据** | TransferGold(玩家→权威NPC), Stage=Resolved, ResolvedBy="payment", TheftLedger.MarkCleared |
| **日志关键词** | |
| | ✅ `[Accountability] Player paid restitution {amount} gold` |
| | ✅ `[WorldEvent] theft_xxx Stage → Resolved` |

### Step 1.8d：分支 — 威胁成功

| 项目 | 内容 |
|------|------|
| **玩家操作** | 选"威胁" |
| **代码路径** | `ThreatIntent.OnSuccess` → `WorldEventStore.OnIntimidated` |
| **产生的数据** | Stage=Resolved, ResolvedBy="intimidated", Infamy+1 |
| **日志关键词** | |
| | ✅ `[SkillCheck] Threat | [单次检定] … 掷骰=通过` — **先确认检定通过** |
| | ✅ `[Accountability] Threat succeeded for theft_xxx` |

---

### Step 1.9：嫌犯=玩家但不理会 → 报复

| 项目 | 内容 |
|------|------|
| **条件** | Stage=Active, SuspectIsPlayer, 10 天内不赔钱/不辩护/不威胁 |
| **玩家操作** | 在大地图等待 10 天 |
| **代码路径** | `ProcessActive` 超时检测 → `TransitionStage(Confrontation)` → `SpawnRetaliationParty` |
| **产生的数据** | RetaliationBudget 播种, RetaliationWaveCount=1, RetaliationSpawned=true |
| **日志关键词** | |
| | ✅ `[Retaliation] theft_xxx Wave 1: {size} men, cost={amount}, budget={remaining}` |
| **预期游戏表现** | 大地图出现 `"{村庄名}的复仇队"` 部队，追击玩家 |

---

## 测试流程二：偷动物（有目击）→ 当面对峙

### Step 2.1：偷动物时周围有村民

| 项目 | 内容 |
|------|------|
| **玩家操作** | 在村民附近偷动物（确保 `StealManager.GetWitnesses` 检测到目击者） |
| **代码路径** | `RecordAnimalTheftCrime` → wasWitnessed=true 分支 |
| **产生的数据** | WorldEvent.Stage=**Active** (跳过 Dormant+Emerging), SuspectHeroId=玩家, InvestigationProgress=1.0, EvidenceList 含 Witness 证据 |
| **日志关键词** | |
| | ✅ `[AnimalTheft] Witnessed! {N} hero(es) + {M} villagers saw the theft. Suspect = Player.` |
| | ✅ `[WorldEvent] New event: Theft_Animal` |

### Step 2.2：当面对峙

| 项目 | 内容 |
|------|------|
| **玩家操作** | 目击者喊叫 → 围观形成 → 权威 NPC 走向玩家 |
| **代码路径** | `DialogueInjector.InjectFromJson("crime_caught_in_act.json")` |
| **预期游戏表现** | 对峙对话四分支：① 当场赔钱(×2) ② 干活抵债 ③ 推开逃跑 ④ 拔剑 |

### Step 2.3a：当场赔钱

| 项目 | 内容 |
|------|------|
| **日志关键词** | `[Accountability]` PayOnTheSpotIntent → `OnPlayerPaidRestitution` |

### Step 2.3b：干活抵债

| 项目 | 内容 |
|------|------|
| **日志关键词** | `[Accountability] Work-off-debt accepted` → 3 天倒计时开始 |
| **后续验证** | 每天回村 → `[WorkOffDebt] Day N/3: Player at {settlement}` |
| | 违约 → `[WorkOffDebt] Breached! → Confrontation` |

### Step 2.3c：推开逃跑

| 项目 | 内容 |
|------|------|
| **日志关键词** | `[Accountability] Player fled confrontation` |

### Step 2.3d：拔剑

| 项目 | 内容 |
|------|------|
| **日志关键词** | `[Accountability] Player fought villagers — retaliation spawned` |

---

## 测试流程三：栽赃嫁祸

> 前置：玩家已偷动物（无目击），Stage=Emerging，接调查 Quest。

### Step 3.1：向村长汇报，选择栽赃

| 项目 | 内容 |
|------|------|
| **玩家操作** | 与村长对话 → 进入 crime_report.json 对话流 |
| **预期游戏表现** | 出现栽赃选项：① "是强盗干的" ② "是{账本Hero}干的" |
| **日志关键词** | `[DialogueInjector]` (INTENT:FrameSuspect) |

### Step 3.2：栽赃强盗

| 项目 | 内容 |
|------|------|
| **玩家操作** | 选"是强盗干的" → DC 40 |
| **代码路径** | `FrameSuspectIntent.OnSuccess` → SuspectHeroId = 附近藏身处强盗头子, InvestigationProgress=1.0, Stage → Active |
| **日志关键词** | |
| | ✅ `[SkillCheck] FrameSuspect | … 掷骰=通过` — **检定通过，阈值应约 40** |
| | ✅ `[Accountability] Frame suspicion: bandit blamed` |

### Step 3.3：栽赃具体人（有证物）

| 项目 | 内容 |
|------|------|
| **前置** | 玩家曾扒窃过某个 NPC，且背包仍持有该物品 |
| **玩家操作** | 选"是{Hero}干的——[出示{ItemName}]" → 证物加成（`GetOfferValue` 返回 0.6 而非 0.2） |
| **日志关键词** | |
| | ✅ `[SkillCheck] FrameSuspect | … 献礼占比=0.60 … 掷骰=通过` — **献礼占比应明显高于裸指控的 0.20** |
| | ✅ `[Accountability] Frame suspicion: {heroId} blamed` |

### Step 3.4：栽赃失败 → fail forward

| 项目 | 内容 |
|------|------|
| **玩家操作** | 栽赃检定失败 |
| **代码路径** | `FrameSuspectIntent.OnFail` → FailCount++ |
| **日志关键词** | |
| | ✅ `[SkillCheck] FrameSuspect | … 掷骰=失败` — **先看检定不等式确认为什么不过** |
| **验证点** | 两次栽赃失败后 SuspectHeroId 转回玩家 |

---

## 测试流程四：追捕 Quest（嫌犯≠玩家）

> 前置：Stage=Active, SuspectHeroId ≠ 玩家。

### Step 4.1：接悬赏 Quest

| 项目 | 内容 |
|------|------|
| **玩家操作** | 与权威 NPC 对话 → 接悬赏 |
| **代码路径** | `CommissionGenerator.TryGenerateAccountabilityQuest` (Stage=Active) → Bounty Quest |
| **日志关键词** | `[CommissionGen] Accountability Bounty quest: hero=xxx suspect=xxx reward=xxx` |
| **预期游戏表现** | 黄色 `!` 出现，对话中告知嫌犯身份和赏金 |

---

## 测试流程五：报复系统

### Step 5.1：报复部队 Spawn

| 项目 | 内容 |
|------|------|
| **代码路径** | `InvestigationEngine.SpawnRetaliationParty` |
| **日志关键词** | `[Retaliation] theft_xxx Wave 1: {N} men, cost={amount}, budget={remaining}` |
| **预期游戏表现** | 大地图部队 `"{村庄}的复仇队"` 出现，追击嫌犯 |

### Step 5.2：打赢第一波

| 项目 | 内容 |
|------|------|
| **代码路径** | `WorldEventStore.OnRetaliationDefeated` → `CheckBudgetAndRespawn` |
| **日志关键词** | RetaliationSpawned=false, Wave 2 spawn（如经费够） |
| | 经费不够 → `[Retaliation] Budget exhausted` → `PermanentEnemy=true, Stage=Resolved` |

### Step 5.3：打手小队（SendThugs）

| 项目 | 内容 |
|------|------|
| **代码路径** | `InvestigationEngine.SpawnThugParty` |
| **日志关键词** | `[Thugs] theft_xxx Spawned thug party: {N} men` |

### Step 5.4：上报领主（EscalateToLord）

| 项目 | 内容 |
|------|------|
| **代码路径** | `InvestigationEngine.EscalateToLord` |
| **日志关键词** | `[Escalate] theft_xxx escalated to lord {lord.Name}` |
| **验证点** | 创建新 WorldEvent(Type=EscalatedCrime, Severity+20) |

---

## 测试流程六：同村重复偷窃 + 村庄警觉

### Step 6.1：已 Resolved 后再偷同村

| 项目 | 内容 |
|------|------|
| **前置** | 前一案件 Resolved (SuspectIsPlayer=true) |
| **玩家操作** | 再偷同一村庄 |
| **代码路径** | `WorldEventStore.AddOrMerge` → `_villageAlertFlags` 检测 |
| **日志关键词** | `[WorldEvent] Village alert active for {settlement} — starting awareness=0.3` |
| **验证点** | 新案 PublicAwareness 起始 0.3（非 0.1） |

### Step 6.2：活跃案件期间再偷

| 项目 | 内容 |
|------|------|
| **前置** | 该村有活跃案件（Stage != Resolved） |
| **玩家操作** | 再偷同一村庄 |
| **代码路径** | `AddOrMerge` → `existing.Quantity += evt.Quantity` |
| **日志关键词** | `[WorldEvent] Merged theft into existing case — qty={N}` |
| **验证点** | Quantity 叠加，InvestigationProgress 不重置 |

---

## 测试流程七：冷案尾巴（Unsolved → VigilanteJustice）

| 项目 | 内容 |
|------|------|
| **前置** | Stage=Unsolved（调查超时未锁定嫌犯） |
| **玩家操作** | 等待 DailyTick 触发 15% 概率 |
| **代码路径** | `ProcessUnsolved` → `TriggerVigilanteJustice` |
| **日志关键词** | `[WorldEvent] Vigilante justice spawned: {scapegoat.Name} blamed for cold case` |
| **预期游戏表现** | 新 WorldEvent(Type=VigilanteJustice) 创建，村民迁怒无辜 NPC |

---

## 测试流程八：存档读档

### Step 8.1：多状态存档

| 项目 | 内容 |
|------|------|
| **玩家操作** | 在不同村庄制造不同阶段的事件（Dormant/Emerging/Active/Confrontation），存档 |
| **代码路径** | `MyBehavior.SyncData` → `WorldEventStore.Serialize` / `PlayerTheftLedger.Serialize` |
| **日志关键词** | `[WorldEventStore] Deserialized {N} events, {M} alerts` |
| **验证点** | 读档后 WorldEvent 全部字段恢复：Stage, SuspectHeroId, InvestigationProgress, EvidenceList, RetaliationBudget 等 |

---

## 测试流程九：扒窃 → PlayerTheftLedger

### Step 9.1：扒窃 NPC

| 项目 | 内容 |
|------|------|
| **玩家操作** | 在村庄场景中对 NPC 使用扒窃 |
| **代码路径** | `StealManager.StealSpecificItem` → `PlayerTheftLedger.Record` |
| **日志关键词** | `[TheftLedger] Recorded: {itemId} x1 from {victimHeroId}` |
| **验证点** | 账本记录 VictimHeroId（非 null，区别于偷动物） |

### Step 9.2：查看背包赃物标注

| 项目 | 内容 |
|------|------|
| **玩家操作** | 按 H 键查看背包 |
| **代码路径** | `AgentControlHelper.GetBagInfo` → `PlayerTheftLedger.GetSourceTag` |
| **预期游戏表现** | 赃物行尾显示 `⚠ 偷自 {地点}`；已赔偿物品显示 `已赔偿 ({地点})` |

---

## 测试执行顺序（瀑布式）

```
第一轮（核心 happy path — 偷→发现→蓝色!→接调查→栽赃强盗→悬赏→追捕）:
  1.1 → 1.2 → 1.3 → 1.4(蓝色!) → 1.5(对话) → 1.5b(接调查→汇报) → 1.5c(栽赃强盗成功)
  → 1.7b(黄色!悬赏) → 测试流程四(追捕)

第二轮（自首 — Emerging 阶段认罪赔钱结案）:
  1.1 → 1.2 → 1.3 → 1.4 → 1.5 → 1.5a(自首) → 1.8c(赔钱结案)

第三轮（目击对峙）:
  2.1 → 2.2 → 2.3a (当场赔钱)

第四轮（栽赃具体人 — 有证物加成）:
  1.1 → 1.2 → 1.3 → 1.4 → 1.5 → 1.5b → 3.3 (栽赃具体人+出示证物)

第五轮（不接任务→后台调查→嫌犯锁定→玩家被指控→不赔钱→报复）:
  1.2 → 1.3 → 1.4 → 1.5(不接任务离开) → 1.6(等待后台调查) → 1.7(黄色!对峙) → 1.9(报复)

第六轮（栽赃失败→fail forward→嫌疑转回玩家）:
  1.2 → 1.3 → 1.4 → 1.5 → 1.5b → 1.5d(第一次失败) → 1.5d(第二次失败→转回玩家) → 1.7

第七轮（重复偷窃+警觉）:
  1.9c(结案) → 6.1

第八轮（存档读档）:
  创建多种状态 → 存档 → 读档 → 验证

每轮之间重启游戏（清空日志），保持日志干净可读。
```

---

## 日志速查表

| 环节 | 搜索关键词 | 确认内容 |
|------|-----------|---------|
| 偷动物 | `[TryStealAnimal]` | 偷窃动作触发 |
| 账本记录 | `[TheftLedger] Recorded:` | ItemId, VictimHeroId/SettlementId |
| 事件创建 | `[WorldEvent] New event:` | Type, Stage, settlement |
| 目击结果 | `[AnimalTheft] Witnessed!` 或 `No witnesses` | 目击人数 |
| 村民发现 | `Stage → Emerging (discovered)` | Dormant→Emerging |
| 传播 | `[SocialEvent] BroadcastWorldEvent:` | 传播触发 |
| **每日调查进度** | `[Investigation] DailyTick:` | progress=N (+dailyAdvance) 分项拆解 |
| 调查锁定 | `[Investigation] Suspect locked:` | 嫌犯 ID |
| 冷案 | `Cold case — no suspect identified` | 无头绪→Unsolved |
| 对话注入 | `[DialogueInjector]` | JSON 加载/注入 |
| INTENT 执行 | `[Accountability]` | 各种 Intent 的 OnSuccess/OnFail/OnInstant |
| **技能检定** | `[SkillCheck] {IntentName}` | 阈值/技能胜率/献礼占比/性格倍率 → 成功率 + 掷骰通过/失败 |
| 赔钱 | `Player paid restitution` | 金额 |
| Charm | `Charm defense succeeded/failed` | 成败 |
| 栽赃 | `Frame suspicion:` | 目标 ID |
| 栽赃失败 | `Frame suspicion failed twice` | fail forward |
| 威胁 | `Threat succeeded/failed` | 成败 |
| 抵债 | `[WorkOffDebt]` | 天数/违约 |
| 报复生成 | `[Retaliation] Wave` | 波次/人数/费用 |
| 打手 | `[Thugs] Spawned thug party` | 人数 |
| 上报领主 | `[Escalate]` | 领主名 |
| 冷案迁怒 | `[WorldEvent] Vigilante justice spawned` | 替罪羊名 |
| 合并偷窃 | `[WorldEvent] Merged theft` | 叠加后数量 |
| 村庄警觉 | `Village alert active` | awareness=0.3 |
| 追责 Quest | `[CommissionGen] Accountability` | 类型/事件 ID |
| 存档 | `[WorldEventStore] Deserialized` | 事件数/警觉数 |

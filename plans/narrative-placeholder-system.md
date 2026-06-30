# 叙事占位符系统 — 动态对话的信息引擎

> **定位**：本文件是 [crime-consequence-composable-v3.md](crime-consequence-composable-v3.md) 第十部分"对话流设计"的配套实现规范。
> 定义①全部叙事场景的占位符词汇表（含精确查询来源）、②全部对话场景模板、③动态 DialogFlow 生成架构。
>
> **与 v3 的关系**：v3 定义"通用引擎怎么跑"，本文件定义"NPC 具体说什么、选项从哪来"。v3 的 JSON 骨架是静态示例，本文件的 `CrimeDialogueBuilder` 是运行时生成器。

---

## 一、占位符词汇表：全分类 + 精确查询来源

> **设计原则**：每个占位符 = 一个确定性的 C# 查询。无 LLM 时直接拼接输出，信息完整即可，不强求风味。有 LLM 时占位符 key-value 作为 prompt context 喂给 LLM。

### A. 事件事实（EventConfig 级 — 同类型事件共享）

| 占位符 | C# 查询来源 | 说明 | 偷牲口 | 暗杀 | 盗猎 |
|--------|------------|------|--------|------|------|
| `{EventTypeName}` | `evt.GetConfig().DisplayName` | 犯罪类型名 | "偷牲口" | "暗杀" | "盗猎" |
| `{CrimeVerb}` | `evt.GetConfig().FlavorProfile.CrimeVerb` | 犯罪行为动词短语 | "偷了" | "杀了" | "在猎场下了套" |
| `{CrimeVerbPast}` | `evt.GetConfig().FlavorProfile.CrimeVerbPast` | 过去式 | "牲口被偷了" | "人被杀了" | "猎场的猎物被偷了" |
| `{CrimeVerbGerund}` | `evt.GetConfig().FlavorProfile.CrimeVerbGerund` | 进行式 | "偷牲口" | "杀人" | "盗猎" |
| `{CrimeScene}` | `evt.GetConfig().FlavorProfile.CrimeScene` | 案发现场描述 | "牲口圈" | "{victim}家附近" | "领主猎场" |
| `{VictimLabel}` | `evt.GetConfig().VictimLabel` | 受害方标签 | "村子" | "死者家族" | "领主" |
| `{AuthorityRole}` | `evt.GetConfig().AuthorityRole` | 权威角色标签 | "村长" | "族长" | "领主" |
| `{SeverityWord}` | `Severity switch { <=30: "小事", <=50: "有点严重", <=70: "严重", <=85: "很严重", _: "天大的事" }` | 严重度口语化 | "小事" | "天大的事" | "有点严重" |
| `{DefaultPenalty}` | `evt.GetConfig().BaseRestitutionMultiplier` × 物品价值 | 默认赔偿额 | "×3" | "×50" | "×10" |

### B. 事件实例（WorldEvent 级 — 每个案件不同）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{EventId}` | `evt.EventId` | "evt_theft_village_a_42" |
| `{StolenItemName}` | `MBObjectManager.Instance.GetObject<ItemObject>(evt.TargetItemId)?.Name?.ToString()` | "羊" / "" (暗杀无物品) |
| `{StolenCount}` | `evt.Quantity.ToString()` | "3" / "" |
| `{StolenItemDesc}` | `$"{Quantity}只{ItemName}"` 或 `""` (暗杀等无物品犯罪) | "三只羊" / "" |
| `{TargetHeroName}` | `Hero.Find(evt.TargetHeroId)?.Name?.ToString()` | "老猎户克拉" / "" (偷村庄为空) |
| `{TargetHeroIdentity}` | `GetSocialIdentity(targetHero)` — 从 Occupation/Trait 推断 | "老猎户" / "铁匠" / "" |
| `{TargetSettlementName}` | `Settlement.Find(evt.TargetSettlementId)?.Name?.ToString()` | "青木村" |
| `{LocationDetail}` | `evt.LocationName` 字段 | "牲口圈" / "村口大路" / "猎场" |

### C. 时间（WorldEvent + CampaignTime）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{DaysSinceEvent}` | `(int)(CampaignTime.Days - evt.OccurredDay)` | "0" / "1" / "7" |
| `{TimeWord}` | `DiffDays switch { <0.5f: "刚才", <1.5f: "昨儿", <2.5f: "前天", <4f: "前几天", <7f: "上周", <14f: "前阵子", <30f: "上个月", _: "很久以前" }` | "昨儿" |
| `{DaysSinceDiscovery}` | `(int)(CampaignTime.Days - evt._stageEnteredDay[Emerging])` | "1" / "3" |
| `{DaysRemaining}` | `evt.GetConfig().InvestigationWindowDays - (int)(CampaignTime.Days - evt.OccurredDay)` | "5" / "0" |
| `{InvestigationDuration}` | `$"查了{DaysSinceDiscovery}天了"` | "查了2天了" |

### D. 公共认知（WorldEvent 认知字段）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{PublicAwarenessWord}` | `evt.PublicAwareness switch { <0.1f: "还没人知道", <0.2f: "私下在议论", <0.5f: "很多人都知道了", <0.8f: "传开了", _: "全社会都知道了" }` | "私下在议论" |
| `{InvestigationProgressWord}` | `evt.InvestigationProgress switch { <0.3f: "刚开始查", <0.6f: "正在查", <0.9f: "快查出来了", _: "查清楚了" }` | "快查出来了" |
| `{SuspectName}` | `evt.SuspectHeroId != null ? Hero.Find(evt.SuspectHeroId)?.Name?.ToString() : null` | "克拉" / null |
| `{SuspectIdentity}` | `GetSocialIdentity(suspectHero)` — 从 Occupation/Trait 推断 | "老猎户" / "流浪汉" / "商人" / "领主" |
| `{SuspectDescription}` | `{SuspectName}` + `{SuspectIdentity}` 拼接，null 时 = "不知道是谁" | "老猎户克拉" / "不知道是谁" |
| `{SuspectIsPlayer}` | `evt.SuspectHeroId == Hero.MainHero.StringId` | "true" / "false" |
| `{SuspectIsNpc}` | `evt.SuspectHeroId != null && evt.SuspectHeroId != Hero.MainHero.StringId` | "true" |
| `{SuspectIsUnknown}` | `evt.SuspectHeroId == null` | "true" |
| `{InitiatorIsPlayer}` | `evt.InitiatorId == Hero.MainHero.StringId` | "true" / "false" |
| `{PlayerIsAccused}` | `evt.SuspectHeroId == Hero.MainHero.StringId` | "true" |
| `{PlayerIsNotAccused}` | `evt.SuspectHeroId != Hero.MainHero.StringId` | "true" |

### E. 目击与证据（WorldEvent 证据字段）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{WitnessExist}` | `evt.WitnessCount > 0` | "true" |
| `{WitnessCount}` | `evt.WitnessCount.ToString()` | "2" |
| `{WitnessCountWord}` | `evt.WitnessCount switch { 0: "没人看见", 1: "有一个人看见了", _: $"有{WitnessCount}个人看见了" }` | "有两个人看见了" |
| `{PrimaryWitnessName}` | `evt.WitnessHeroIds?.FirstOrDefault() is string id && id != null ? Hero.Find(id)?.Name?.ToString() : ""` | "克拉" / "" |
| `{PrimaryWitnessIdentity}` | `GetSocialIdentity(primaryWitnessHero)` | "老猎户" / "铁匠" / "路过的商人" |
| `{PrimaryWitnessDesc}` | `{PrimaryWitnessName}` + `{PrimaryWitnessIdentity}` | "老猎户克拉" / "" |
| `{WitnessesSilenced}` | `evt.WitnessesSilenced` | "true" |
| `{EvidenceExist}` | `evt.EvidenceList?.Count > 0` | "true" |
| `{EvidenceCount}` | `evt.EvidenceList?.Count.ToString()` | "2" |
| `{TopEvidenceDesc}` | `evt.EvidenceList?.OrderByDescending(e => e.Strength).FirstOrDefault()?.SourceDescription` | "在牲口圈附近捡到的匕首" |
| `{TopEvidenceIsPhysical}` | `topEvidence?.Kind == EvidenceKind.Physical` | "true" |
| `{TopEvidenceIsWitness}` | `topEvidence?.Kind == EvidenceKind.Witness` | "true" |
| `{TopEvidenceIsCircumstantial}` | `topEvidence?.Kind == EvidenceKind.Circumstantial` | "true" |

### F. 说话者身份与态度（Speaker — 运行时计算）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{SpeakerName}` | `speaker.Name.ToString()` | "王伯" / "克拉" |
| `{SpeakerIdentity}` | `GetSocialIdentity(speaker)` | "村长" / "老猎户" / "族长" |
| `{SpeakerRole}` | `IsAuthority(speaker, evt) ? evt.GetConfig().AuthorityRole : GetGenericRole(speaker)` | "村长" / "目击者" / "嫌犯" |
| `{SpeakerSelfRef}` | `GetSelfReference(speaker)` — "老夫" / "我" / "本官" / "在下" | "老夫" / "我" |
| `{SpeakerPlayerAddr}` | `GetPlayerAddress(speaker, relation)` — "你" / "你这后生" / "阁下" / "大人" | "你" |
| `{SpeakerEmotion}` | `Stance.Outrage > 0.7f ? "愤怒" : Stance.Outrage > 0.3f ? "焦虑" : Stance.Fear > 0.5f ? "畏惧" : Stance.SelfInterest > 0.4f ? "意味深长" : Stance.Sympathy < -0.3f ? "温和" : "冷淡"` | "愤怒" |
| `{SpeakerEmotionIntensity}` | `max(Outrage, Fear, abs(Sympathy), SelfInterest)` | "0.7" |
| `{SpeakerAttitudeWord}` | `stance.TowardActor switch { Sympathetic: "同情", Understanding: "理解", Neutral: "无所谓", Disapproving: "不赞同", Angry: "愤怒", Vengeful: "仇恨" }` | "愤怒" |
| `{SpeakerOutrage}` | `stance.Outrage.ToString("F1")` | "0.7" |
| `{SpeakerFear}` | `stance.Fear.ToString("F1")` | "0.2" |
| `{SpeakerSympathy}` | `stance.Sympathy.ToString("F1")` | "-0.3" / "0.5" |
| `{SpeakerSelfInterest}` | `stance.SelfInterest.ToString("F1")` | "0.2" |
| `{SpeakerWillAct}` | `Math.Max(0, stance.Outrage - stance.Fear).ToString("F1")` | "0.5" |
| `{SpeakerIsAuthority}` | `IsAuthority(speaker, evt.TargetSettlementId)` | "true" |
| `{SpeakerIsWitness}` | `evt.WitnessHeroIds?.Contains(speaker.StringId) == true` | "true" |
| `{SpeakerIsSuspect}` | `evt.SuspectHeroId == speaker.StringId` | "true" |
| `{SpeakerIsVictim}` | `evt.TargetHeroId == speaker.StringId` | "true" |
| `{SpeakerRelationToPlayer}` | `speaker.GetRelationWith(Hero.MainHero).ToString()` | "10" / "-5" |
| `{SpeakerRelationWord}` | `relation switch { >=20: "关系不错", >=5: "认识", >=-5: "不熟", >=-20: "有点过节", _: "有仇" }` | "不熟" |
| `{SpeakerRelationToSuspect}` | `evt.SuspectHeroId != null ? speaker.GetRelationWith(Hero.Find(evt.SuspectHeroId)).ToString() : ""` | "15" / "-30" |
| `{SpeakerIsSuspectFriend}` | `relationToSuspect > 20` | "true" |
| `{SpeakerIsSuspectEnemy}` | `relationToSuspect < -20` | "true" |

### G. 听者身份（Listener — 通常是玩家）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{ListenerName}` | `listener.Name.ToString()` | "玩家名" |
| `{ListenerIsPlayer}` | `listener == Hero.MainHero` | "true" |
| `{ListenerIsThief}` | `evt.InitiatorId == listener.StringId` | "true" |
| `{ListenerIsSuspect}` | `evt.SuspectHeroId == listener.StringId` | "true" |
| `{ListenerIsDetective}` | `evt.InitiatorId != listener.StringId && listener == Hero.MainHero` | "true" |
| `{ListenerIsBystander}` | `listener 与事件无关` | "true" |
| `{ListenerRelationToAuthority}` | `listener.GetRelationWith(authorityNpc).ToString()` | "10" |

### H. 选项参数（每个对话选项独立计算 — Intent 提供）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{RestitutionCost}` | `ComputeRestitutionCost(evt, stage)` — 阶段2 ×3 / 阶段3 ×5 | "450" |
| `{RestitutionCostOnSpot}` | `ComputeRestitutionCost(evt, stage: "caught_in_act")` — 当场 ×2 | "300" |
| `{BountyAmount}` | `evt.GetConfig().BaseBountyPerUnit * evt.Quantity` | "500" |
| `{FineAmount}` | 罚金 = `RestitutionCost * 0.5` | "225" |
| `{AppeaseAmount}` | 安抚费 = `RestitutionCost * 0.3` | "135" |
| `{TotalCost}` | `RestitutionCost + FineAmount + AppeaseAmount` | "810" |
| `{SkillCheckDC}` | `SingleRollResolver.Compute(intent, ctx).DC` | "40" / "70" |
| `{SkillName}` | `intent.Tactic` 映射技能名 | "Charm" / "Roguery" |
| `{PlayerSkillValue}` | `Hero.MainHero.GetSkillValue(skill).ToString()` | "120" |
| `{SuccessChance}` | `SingleRollResolver.Compute(intent, ctx).SuccessChance.ToString("P0")` | "65%" |
| `{CanAfford}` | `Hero.MainHero.Gold >= totalCost` | "true" |
| `{CanAffordOnSpot}` | `Hero.MainHero.Gold >= onSpotCost` | "true" |
| `{HasEvidenceItem}` | 背包持有匹配 `PlayerTheftLedger` 的物品 | "true" |
| `{EvidenceItemName}` | 匹配到的 ItemObject.Name | "一把匕首" |
| `{FrameTargetName}` | 栽赃候选的 Hero.Name 或 "强盗" | "张三" |
| `{FrameTargetIdentity}` | 栽赃候选的社会身份 | "流浪汉" / "商人" |
| `{FrameTargetDC}` | `ComputeBaseDC(target)` | "55" |
| `{FrameTargetIsBandit}` | `targetId == "bandit"` | "true" |
| `{FrameTargetIsPowerful}` | `target.IsLord \|\| target.IsMerchant` | "true" |
| `{FailCount}` | `evt.FailCount.ToString()` | "1" / "2" |
| `{FailCountRemaining}` | `(2 - evt.FailCount).ToString()` | "1" / "0" |
| `{CharmReprieveUsed}` | `evt.CharmReprieveUsed` | "true" |

### I. 检定结果（运行时填入 — 对话执行后才确定）

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{RollSuccess}` | `roll.Success` | "true" |
| `{RollResultWord}` | `roll.Success ? "成功了" : "失败了"` | "成功了" |
| `{RollNpcReaction}` | 检定→NPC 回应文本（由 Intent.OnSuccess/OnFail 设置） | "他信了你的话" / "他没信" |
| `{NewSuspectName}` | 检定后 `evt.SuspectHeroId` 变化后的新值 | "强盗" / "张三" |
| `{NewStageWord}` | 检定后 `evt.Stage` 变化后的阶段描述 | "现在全村都在找他了" |

---

## 二、全部对话场景模板

> 每个模板 = 触发条件 + NPC 台词模板 + 可用选项（由 Intent 动态注入）。
> 按说话者身份分组。每个模板的占位符在运行时由 `PlaceholderResolver` 填充。

### 场景组织方式

```
说话者身份:
  ├─ A. Authority NPC（权威人物：村长/族长/领主）
  │     ├─ A1-A6:  Emerging（发现阶段）
  │     ├─ A7-A19: Active（嫌犯锁定阶段）
  │     ├─ A20-A28: Confrontation（报复阶段）
  │     └─ A29-A35: Resolved / Unsolved（终局）
  │
  ├─ W. Witness NPC（目击者）
  │     ├─ W1-W3: 玩家是贼，试图封口
  │     └─ W4-W6: 玩家是侦探，调查取证
  │
  ├─ S. Suspect NPC（嫌犯 — 当 Suspect≠Player）
  │     ├─ S1-S5: 追捕/诱捕/对峙
  │     └─ S6-S8: 背叛/击杀
  │
  ├─ V. Victim / Harmed Party（受害方）
  │     └─ V1-V4: 受害方叙事
  │
  ├─ B. Bystander NPC（普通村民/路人）
  │     └─ B1-B5: 市井流言
  │
  ├─ C. Companion（同伴）
  │     └─ C1-C3: 同伴反应
  │
  ├─ M. Mission Caught-in-Act（当场发现，Mission 内）
  │     └─ M1-M7: 围观→对峙→四分支
  │
  └─ R. Retaliation Party（报复部队遭遇）
        └─ R1-R5: 大地图遭遇→摊牌
```

---

### A. Authority NPC 场景

#### A1. Emerging — 首次接触（玩家问"出什么事了"）

**触发条件**: `Stage == Emerging && !PlayerHasInvestigationQuest`

```
NPC: "（{SpeakerEmotion}地）{TimeWord}{TargetSettlementName}的{CrimeScene}{CrimeVerbPast}，{StolenItemDesc}不见了。
     {InvestigationProgressWord}。{WitnessCountWord}，{SuspectDescription}。
     {SpeakerPlayerAddr}能帮忙查查吗？"

示例(偷牲口): "（焦虑地）昨儿青木村的牲口圈被偷了，三只羊不见了。刚开始查。没人看见，不知道是谁。你能帮忙查查吗？"
示例(暗杀):   "（愤怒地）昨儿村里出了人命——老猎户克拉被人杀了。刚开始查。有两个人看见了，还不知道是谁干的。你能帮忙查查吗？"
示例(盗猎):   "（焦虑地）前几天领主猎场的猎物被人偷了。正在查。没人看见，不知道是谁。你能帮忙查查吗？"

选项:
  [接调查Quest]  → ACTION:INTENT:Investigate
  [我还有事]     → ACTION:NONE → close_window
```

#### A2. Emerging — 玩家已接调查 Quest，找权威 NPC 汇报

**触发条件**: `Stage == Emerging && PlayerHasInvestigationQuest`

```
NPC: "怎么样，查到什么了吗？"

选项（由 FrameSuspectIntent.Evaluate 动态生成候选）:
  [是强盗干的]           → ACTION:INTENT:FrameSuspect:bandit → A3(A3a) 或 A4(A3a)
  [是{FrameTargetName}干的] → ACTION:INTENT:FrameSuspect:{TargetId} → A3(A3b) 或 A5
  [还没查到什么]          → NPC: "那你再去看看。{InvestigationProgressWord}。" → close_window
  [(若ListenerIsThief) 主动认栽] → A6
```

#### A3a. Emerging — 栽赃强盗，信了（DC 通过）

**触发条件**: 栽赃强盗 → 检定成功

```
NPC: "强盗偷牲口，天经地义。好，{SpeakerSelfRef}信你——就是他们干的！
     {SpeakerPlayerAddr}既然查出来了，去把强盗窝端了，{SpeakerSelfRef}必有重谢。"

选项:
  [我去端了强盗窝]  → ACTION:INTENT:FrameSuspect:bandit → SuspectHeroId=banditLeader → 接追捕Quest → close_window
  [我再想想]        → ACTION:NONE → close_window
```

#### A3b. Emerging — 栽赃具体人，有证物，信了

**触发条件**: 栽赃具体人 → `HasEvidenceItem=true` → 检定成功

```
NPC: "这件{EvidenceItemName}……{SpeakerPlayerAddr}是从哪找到的？
     （仔细看了看）这确实是{FrameTargetName}的东西。好，那就是他了！"

选项:
  [我去把他抓来]    → ACTION:INTENT:FrameSuspect:{TargetId}:WithEvidence → SuspectHeroId=target → 接追捕Quest
  [我再想想]        → ACTION:NONE → close_window
```

#### A3c. Emerging — 栽赃具体人，无证物裸过

**触发条件**: 栽赃具体人 → `HasEvidenceItem=false` → 检定成功

```
NPC: "……好吧。虽然{SpeakerPlayerAddr}拿不出证据，但{SpeakerSelfRef}信{SpeakerPlayerAddr}的判断。
     {FrameTargetName}……没想到是他。"

选项: 同 A3b（无 WithEvidence 标记）
```

#### A4. Emerging — 栽赃失败，第一次

**触发条件**: 栽赃检定失败 && `FailCount == 1`

```
NPC: "光凭嘴说可不行……（犹豫地看着{SpeakerPlayerAddr}）这次就算了，{SpeakerPlayerAddr}再去查查。"

选项:
  [换个人指]  → 回 A2 重新选（`FailCount` 保持为 1）
  [嘴硬坚持]  → 二次裸检定 → 成功→A3 / 又失败→A5
  [算了]      → ACTION:NONE → close_window（AI 调查继续推进）
```

#### A5. Emerging — 栽赃失败，第二次（fail forward）

**触发条件**: 栽赃检定失败 && `FailCount >= 2`

```
NPC: "{SpeakerPlayerAddr}一会指这个一会指那个……
     {(InitiatorIsPlayer) ? '该不会就是{SpeakerPlayerAddr}干的？' : '算了，这事{SpeakerSelfRef}们自己查。'}"

分支:
  玩家是贼 → "该不会就是你干的？" → 嫌疑转回玩家 → SuspectHeroId=玩家 → Stage=Active → A7
  玩家无辜 → "算了，我们自己查。" → Quest失败，Trust-5 → close_window（AI继续调查）
```

#### A6. Emerging — 玩家主动认栽（自己是贼）

**触发条件**: `InitiatorIsPlayer && ListenerIsThief`（条件可见）

```
NPC: "{SpeakerPlayerAddr}？！……（沉默片刻）好。既然{SpeakerPlayerAddr}自己认了，咱们可以商量。"

选项:
  [我愿意赔]        → ACTION:INTENT:PayRestitution → 赔偿 ×3 → Stage=Resolved
  [{CharmDefense}]  → ACTION:INTENT:CharmDefense（每案一次）→ 成功→A17 / 失败→A20
  [{Threat}]        → ACTION:INTENT:Threat → 成功→A27 / 失败→A20
```

---

#### A7. Active — 嫌犯=玩家，冷脸对峙

**触发条件**: `Stage == Active && SuspectHeroId == Player && SpeakerIsAuthority`

```
NPC: "（{SpeakerEmotion}地）{SpeakerPlayerAddr}还敢来？
     {PrimaryWitnessDesc != '' ? '{PrimaryWitnessDesc}{TimeWord}就来找{SpeakerSelfRef}，说亲眼瞧见是{SpeakerPlayerAddr}{CrimeVerb}。'
                               : '村里人都传开了，就是{SpeakerPlayerAddr}{CrimeVerb}。'}
     {SpeakerPlayerAddr}有什么要说的？"

选项:
  [{CharmDefense}]   → ACTION:INTENT:CharmDefense（条件: !CharmReprieveUsed）
  [{PayRestitution}] → ACTION:INTENT:PayRestitution（条件: CanAfford=true / Grey:CanAfford=false）
  [{Threat}]         → ACTION:INTENT:Threat（条件: Roguery >= 50）
  [{FrameSuspect}]   → ACTION:INTENT:FrameSuspect（条件: PlayerTheftLedger 有候选）
  [转身就走]         → ACTION:NONE → close_window（Stage 保持 Active，期限到→A20）
```

#### A8. Active — Charm 辩护成功

**触发条件**: A7 选 CharmDefense → 检定成功

```
NPC: "……{SpeakerPlayerAddr}说得也不是没道理。
     {SpeakerSelfRef}再查查。但{SpeakerPlayerAddr}别以为这就完了。"

效果: SuspectHeroId=null, PublicAwareness=0.5, Stage=Emerging, CharmReprieveUsed=true
```

#### A9. Active — Charm 辩护失败

**触发条件**: A7 选 CharmDefense → 检定失败

```
NPC: "够了！{SpeakerPlayerAddr}当{SpeakerSelfRef}是傻子吗？没得谈了！"

效果: Trust-10, Stage=Confrontation → A20
```

#### A10. Active — 赔钱了事

**触发条件**: A7 选 PayRestitution → OnInstant

```
NPC: "（数了数钱）{RestitutionCost}第纳尔……好吧。
     这事就算了了。但{SpeakerPlayerAddr}记住，{TargetSettlementName}不欢迎贼。"

效果: TransferGold(玩家→权威NPC, cost), Stage=Resolved, ResolvedBy="payment"
```

#### A11. Active — 威胁成功

**触发条件**: A7 选 Threat → 检定成功

```
NPC: "（环顾四周，压低声音）好……好。这事{SpeakerSelfRef}烂在肚子里。{SpeakerPlayerAddr}走吧。"

效果: 恶名+1, Trust暴跌, Stage=Resolved, ResolvedBy="intimidated"
```

#### A12. Active — 威胁失败

**触发条件**: A7 选 Threat → 检定失败

```
NPC: "敢威胁{SpeakerSelfRef}？！来人！"

效果: Stage=Confrontation → A20
```

---

#### A13. Active — 嫌犯≠玩家（NPC），提供悬赏 Quest

**触发条件**: `Stage == Active && SuspectHeroId != Player && SpeakerIsAuthority`

```
NPC: "还记得{TimeWord}{CrimeVerbPast}的事吗？
     查清楚了——是{SuspectDescription}干的。
     村上凑了{BountyAmount}第纳尔悬赏，谁把他抓回来就给谁。{SpeakerPlayerAddr}接不接？"

选项:
  [我接这个悬赏]  → ACTION:INTENT:AcceptBountyQuest → 接追捕 Quest
  [我先想想]     → ACTION:NONE → close_window
```

#### A14. Active — 玩家交付嫌犯（追捕 Quest 完成）

**触发条件**: 玩家回村交付活捉的嫌犯

```
NPC: "（{SpeakerEmotion}地）好！总算把这家伙抓到了。
     （转向{SuspectName}）{SuspectIsInnocent ? '你还有什么话说？' : '这回看你往哪跑！'}
     （对{SpeakerPlayerAddr}）这是{BountyAmount}第纳尔，{TargetSettlementName}的人都记着{SpeakerPlayerAddr}的好。"

效果: Trust+10~15, TransferGold(权威NPC→玩家, BountyAmount), Stage=Resolved
```

#### A15. Active — 玩家交付死嫌犯（追捕中杀了）

**触发条件**: 嫌犯在追捕中被杀 → 玩家回报

```
NPC: "{SpeakerPlayerAddr}把他杀了？！（叹气）{SpeakerSelfRef}说要活的……
     {(SuspectIsInnocent) ? '这下死无对证了……' : '也罢，死了也算有个交代。'}
     这是半额赏金，{SpeakerPlayerAddr}拿去吧。"

选项:
  [出示尸体信物]  → 半额报酬，Trust不变  → Stage=Resolved
  [老实说下手重了] → Trust-5              → Stage=Resolved
```

---

#### A16. Active — 栽赃大人物后的第二道坎

**触发条件**: 栽赃商人/领主 → belief 检定通过 → `IsPowerful=true`

```
NPC: "{FrameTargetName}？！{SpeakerPlayerAddr}确定？！他可是有头有脸的人……
     这事儿……要不咱还是算了吧。为了{CrimeScene}{CrimeVerbPast}，犯不着得罪他。"

选项:
  [Charm激将] "{SpeakerPlayerAddr}是{AuthorityRole}，村民看着{SpeakerPlayerAddr}呢。{SpeakerPlayerAddr}不替他们出头，谁出？"
    → 成功 → A18 / 失败 → A19
  [Roguery恐吓] "{SpeakerPlayerAddr}不抓他，回头他知道了{SpeakerPlayerAddr}在查他——{SpeakerPlayerAddr}猜他先找谁？"
    → 成功 → A18 / 失败 → A5(嫌疑转回玩家)
  [算了] → 村长压下案子，Trust-10 → Stage=Cold
```

#### A17. Active — 第二道坎通过

```
NPC: "（咬牙）好……干了。{SpeakerPlayerAddr}说的对，{SpeakerSelfRef}不能让人骑在头上。"

效果: SuspectHeroId=framedTarget, Stage=Active, 跳过阶段2个人追捕→直接进阶段3(全村组队)
```

#### A18. Active — 第二道坎失败

```
NPC: "{SpeakerPlayerAddr}站着说话不腰疼……这事{SpeakerSelfRef}压下了。
     以后少给{SpeakerSelfRef}惹事。"

效果: Trust-10, Stage=Cold（定时炸弹，以后可能被翻出来）
```

---

#### A19. Confrontation — 报复宣言（嫌犯=玩家 or NPC）

**触发条件**: `Stage == Confrontation && SpeakerIsAuthority`

```
NPC: "（{SpeakerEmotion}地）客客气气说话不管用，那就只能动手了。
     {(SuspectIsPlayer) ? '村里凑了钱，已经雇了人。{SpeakerPlayerAddr}躲得过初一躲不过十五。'
                         : '我们已经雇了人去抓{SuspectDescription}。'}
     {(SuspectIsNpc) ? '{SpeakerPlayerAddr}要是站在我们这边的，可以带他们去。' : ''}"

选项（嫌犯=玩家）:
  [{PayRestitution×5}] → ACTION:INTENT:PayRestitution ×5+罚金+安抚费（条件: CanAfford=true）
  [{Charm/Roguery说服}] → ACTION:INTENT:Settle（和解劝说，成功率低）→ 成功→A27 / 失败→Trust-15
  [我走了]             → ACTION:NONE → close_window（保持被追状态）

选项（嫌犯≠玩家）:
  [我带人去]     → ACTION:INTENT:LeadRetaliation → 接报复 Quest
  [我没空]       → ACTION:NONE → close_window
```

#### A20. Confrontation — 玩家投降（被报复部队抓住后）

**触发条件**: 玩家被报复部队俘虏 → 带回村庄 → 触发

```
NPC: "（{SpeakerEmotion}地）{SpeakerPlayerAddr}总算落{SpeakerSelfRef}手里了。
     {StolenItemDesc}加上雇人的花费，一共{TotalCost}第纳尔。交了这笔钱，再挨一顿罚，这事才算了。"

[cutscene 触发：示众/鞭笞/罚没（非致死）] → Stage=Resolved
```

---

#### A21. Resolved — 赔钱了结

```
NPC: "事已至此，{SpeakerPlayerAddr}记住教训。{TargetSettlementName}可不兴再出这种事。"
```

#### A22. Resolved — 抓到真贼

```
NPC: "多亏了{SpeakerPlayerAddr}！{TargetSettlementName}上下的感激，{SpeakerPlayerAddr}当得起。"
```

#### A23. Resolved — 冷案（7天没查出来）

```
NPC: "（{SpeakerEmotion}地）查了{InvestigationDuration}，什么也没查出来。算了，算我们倒霉。"
```

#### A24. Unsolved — 冷案尾巴（15% 概率触发迁怒）

**触发条件**: `Stage == Unsolved && Random(100) < 15`

```
NPC: "（低声）这事越想越不对劲……{SpeakerSelfRef}心里有个人选。{SpeakerPlayerAddr}帮{SpeakerSelfRef}看看？"

效果: 创建新 WorldEvent(Type=VigilanteJustice) → 无辜的人被盯上 → 涌现支线
```

#### A25. 跨案件 — 村庄警觉（再偷此村）

**触发条件**: `_villageAlertFlags[sid] == true && 新案件`

```
NPC: "（{SpeakerEmotion}地）又是{SpeakerPlayerAddr}？上次的事{SpeakerSelfRef}还记着呢。这次跑不掉了。"
```

---

### W. Witness NPC 场景

#### W1. Witness — 玩家是贼，试图封口（首次接触）

**触发条件**: `SpeakerIsWitness && !WitnessesSilenced && InitiatorIsPlayer`

```
NPC: "（{SpeakerEmotion}地）{SpeakerPlayerAddr}是来问{CrimeScene}的事？{SpeakerSelfRef}……{SpeakerSelfRef}确实看见了。"

选项:
  [{SilenceWitness: Bribe}]   → ACTION:INTENT:SilenceWitness:Bribe（Roguery，Gold>=bribeCost）
  [{SilenceWitness: Intimidate}] → ACTION:INTENT:SilenceWitness:Intimidate（Roguery DC，队伍规模加成）
  [{SilenceWitness: Appeal}]  → ACTION:INTENT:SilenceWitness:Charm（Charm DC，高关系加成）
  [当{SpeakerSelfRef}没来过]  → ACTION:NONE → close_window（目击者会去报告）
```

#### W2. Witness — 玩家是贼，封口成功

**触发条件**: W1 检定成功

```
NPC: "（低头）{SpeakerSelfRef}什么也没看见。{SpeakerPlayerAddr}走吧。"
```

#### W3. Witness — 玩家是贼，封口失败

**触发条件**: W1 检定失败

```
NPC: "（后退一步）{SpeakerPlayerAddr}威胁{SpeakerSelfRef}也没用！{SpeakerSelfRef}没做亏心事，不怕。"
效果: 目击者马上去报告 Authority → InvestigationProgress +0.2
```

#### W4. Witness — 玩家是侦探，调查取证

**触发条件**: `SpeakerIsWitness && !InitiatorIsPlayer && PlayerHasInvestigationQuest`

```
NPC: "（{SpeakerEmotion}地）{SpeakerSelfRef}{TimeWord}在{CrimeScene}附近看见了一个人……
     那人{GetWitnessDescription()}。{(EvidenceExist) ? '还在地上捡到了{TopEvidenceDesc}。' : ''}"

选项:
  [能说说那人的特征吗] → NPC描述嫌犯外貌/衣着
  [你认得那个人吗]     → {SuspectUnknown ? '不认得' : '好像是{SuspectDescription}'}
  [谢谢，我知道了]     → ACTION:NONE → close_window
```

#### W5. Witness — 已被封口

**触发条件**: `WitnessesSilenced && SpeakerIsWitness`

```
NPC: "（紧张地看了看四周）{SpeakerPlayerAddr}找错人了。{SpeakerSelfRef}什么也不知道。"
```

---

### S. Suspect NPC 场景（嫌犯≠玩家时）

#### S1. Suspect — 玩家接近嫌犯（追捕 Quest 进行中）

**触发条件**: `SpeakerIsSuspect && SuspectHeroId != Player && PlayerHasBountyQuest`

```
NPC: "（警惕地）{SpeakerPlayerAddr}盯着{SpeakerSelfRef}看什么？"

选项:
  [诱捕: 跟我走一趟村长找你]    → ACTION:INTENT:LureArrest → 成功→S3 / 失败→S2
  [直接动手: 背后击晕（不对话）] → 走潜行/击晕路线（不走对话）
  [{BetrayQuest: 快跑}]         → ACTION:INTENT:BetrayQuest → S6
  [没什么]                       → ACTION:NONE → close_window
```

#### S2. Suspect — 诱捕/抓捕失败（嫌犯反抗）

**触发条件**: LureArrest or Arrest 检定失败

```
NPC: "{(SuspectIsInnocent) ? '{SpeakerPlayerAddr}想干什么？！{SpeakerSelfRef}什么都没做！'
                           : '想抓{SpeakerSelfRef}？没那么容易！'}"
效果: 惊动(犯罪) / 逃脱 / 进入战斗
```

#### S3. Suspect — 诱捕成功（嫌犯信了）

**触发条件**: LureArrest 检定成功

```
NPC: "行，{SpeakerSelfRef}跟{SpeakerPlayerAddr}去。村长找{SpeakerSelfRef}什么事？"

效果: NPC 进玩家俘虏栏(无物理出手) → 回村交付
```

#### S4. Suspect — 被交付时喊冤（无辜被陷害）

**触发条件**: 交付嫌犯 && `SuspectIsInnocent && SuspectHeroId 是被栽赃的`

```
NPC: "（对村长）{SpeakerSelfRef}没偷！是{SpeakerPlayerAddr}陷害{SpeakerSelfRef}！

     （对{SpeakerPlayerAddr}）{SpeakerPlayerAddr}偷了{StolenItemDesc}栽到{SpeakerSelfRef}头上……{SpeakerSelfRef}跟{SpeakerPlayerAddr}没完。"

效果: HeroNemesisTracker 记宿敌。若栽赃证据弱（无证物裸过）→ 反噬窗口：嫌疑可能转回玩家
```

#### S5. Suspect — 真贼被交付

```
NPC: "（垂头）……算{SpeakerSelfRef}倒霉。"
```

#### S6. Suspect — 玩家背叛（告诉嫌犯快跑）

**触发条件**: 追捕 Quest 中选 BetrayQuest

```
NPC: "{(SuspectIsInnocent) ? '……{SpeakerPlayerAddr}为什么帮{SpeakerSelfRef}？'
                           : '谢了！{SpeakerSelfRef}欠你一个人情。'}"

{(PlayerConfesses: '是我陷害的你' && SuspectIsInnocent) ? '什么？！是{SpeakerPlayerAddr}？！'
                                                         : ''}

效果: Quest失败, Trust-15(村长)。若自曝→NemesisRecord。若村长怀疑→SuspectHeroId转回玩家
```

---

### V. Victim / Harmed Party 场景

#### V1. Victim — 受害方描述案情（玩家是侦探）

**触发条件**: `SpeakerIsVictim && !InitiatorIsPlayer && PlayerHasInvestigationQuest`

```
NPC: "（{SpeakerEmotion}地）{SpeakerPlayerAddr}是来帮{SpeakerSelfRef}的？
     {TimeWord}{CrimeScene}{CrimeVerbPast}……{GetVictimPersonalAccount()}"

选项:
  [有什么线索吗]     → NPC提供个人视角线索
  [{EvidenceExist}能看看吗] → 出示证据描述
  [我会查清楚的]     → ACTION:NONE → close_window
```

#### V2. Victim — 受害方怀疑玩家

**触发条件**: `SpeakerIsVictim && PlayerIsAccused`

```
NPC: "（{SpeakerEmotion}地瞪着{SpeakerPlayerAddr}）{SpeakerPlayerAddr}还敢来找{SpeakerSelfRef}？"
```

#### V3. Victim — 正义得到伸张后

```
NPC: "谢谢{SpeakerPlayerAddr}……{(SuspectWasKilled) ? '虽然人死不能复生' : '总算出了这口气'}。"
```

---

### B. Bystander NPC 场景

#### B1. Bystander — Emerging 阶段流言

**触发条件**: `Stage == Emerging && SpeakerIsBystander && PublicAwareness >= 0.1`

```
NPC: "（压低声音）{SpeakerPlayerAddr}听说了吗？{TargetSettlementName}的{CrimeScene}{CrimeVerbPast}！
     {(WitnessExist) ? '{PrimaryWitnessDesc}说他亲眼看见了。' : '谁干的还不知道。'}"

选项:
  [详细说说]   → NPC根据PublicAwareness透露更多
  [哦。]       → ACTION:NONE → close_window
```

#### B2. Bystander — Active 阶段流言

**触发条件**: `Stage == Active && PublicAwareness >= 0.5`

```
NPC: "听说了吗？是{SuspectDescription}干的！{(SuspectIsNpc) ? '村里悬赏{BountyAmount}第纳尔抓他呢。'
                                                             : '村长气坏了。'}"
```

#### B3. Bystander — Confrontation 阶段流言

```
NPC: "（紧张地）{TargetSettlementName}的人真动手了——雇了打手满世界找人。这事闹大了……"
```

#### B4. Bystander — 对玩家的态度（取决于 PlayerIsAccused）

```
NPC: "{(PlayerIsAccused) ? '（看到{SpeakerPlayerAddr}，往后退了一步）' : '（点了点头）'}……"
```

---

### C. Companion 场景

#### C1. Companion — 玩家犯罪后提醒

**触发条件**: `InitiatorIsPlayer && CompanionPresent && 进入受影响定居点`

```
NPC: "（低声）{SpeakerPlayerAddr}最好小心点。{TargetSettlementName}的人还在查{CrimeScene}的事。"
```

#### C2. Companion — 建议

```
NPC: "{(Stage == Emerging) ? '{SpeakerPlayerAddr}要不要趁他们还没查出来，先找{AuthorityRole}谈谈？'
                           : '{(PlayerIsAccused) ? '这下麻烦了……{SpeakerPlayerAddr}打算怎么办？'
                                                  : '这事跟咱们没关系，走吧。'}'}"
```

---

### M. Mission Caught-in-Act 场景（Mission 内触发，不走玩家主动交谈）

#### M1. Mission — 目击者喊叫

**触发**: 偷窃动作执行 → `StealManager.GetWitnesses` 检测到目击者

```
NPC（目击者）: "（{SpeakerEmotion}地喊）喂！{ListenerName}在{CurLocation}干什么！{CrimeVerbGerund}！快来人啊！{AuthorityRole}！"
```

#### M2. Mission — 村民围观

**触发**: 目击者喊叫后 → 周围 NPC 靠拢

```
[玩家失去控制 1.5s]
[周围村民: "怎么了？" "谁在偷东西？" "是{ListenerName}！"]
```

#### M3. Mission — 权威 NPC 到场对峙

**触发**: 围观形成后 → Authority NPC 走向玩家

```
NPC（Authority）: "（{SpeakerEmotion}地）{ListenerName}！{SpeakerSelfRef}亲眼看见了——{CrimeVerbGerund}。有什么话说？"

选项（四分支）:
  ① [赔钱当场 ×2]     → ACTION:INTENT:PayOnTheSpot（条件: CanAffordOnSpot）→ M4
  ② [干活抵债]         → ACTION:INTENT:WorkOffDebt → M5
  ③ [推开逃跑]         → ACTION:INTENT:FleeFromConfrontation → M6
  ④ [拔剑]             → ACTION:INTENT:FightVillagers → M7
```

#### M4. Mission — 当场赔钱

```
NPC（Authority）: "（数了数钱）{RestitutionCostOnSpot}第纳尔。算{ListenerName}识相。走吧，别再来了。"

效果: TransferGold(玩家→权威NPC, ×2), Stage=Resolved
```

#### M5. Mission — 干活抵债

```
NPC（Authority）: "好。{ListenerName}每天来村里干活，干满三天这事就算了。要是敢不来……哼。"

效果: 3天软约束，违约→Trust-20+Stage=Confrontation
```

#### M6. Mission — 推开逃跑（力量检定）

```
[力量检定: 成功→推开村民跑掉→WasWitnessed=true→直接进Active(Suspect=Player)]
           [失败→被围住+Trust-15→Stage=Confrontation]
```

#### M7. Mission — 拔剑

```
NPC（Authority）: "（后退一步）{ListenerName}疯了！拦住他！"

效果: 5~8村民 vs 玩家。赢→恶名+5,全村敌对,Stage=Confrontation。输→被俘→A20
```

---

### R. Retaliation Party 场景（大地图遭遇）

#### R1. Retaliation — 报复部队追上玩家

**触发**: 大地图 → `SetPartyAiAction.GetActionForEngagingParty` 触发遭遇

```
NPC（报复队长）: "{SpeakerSelfRef}是{TargetSettlementName}雇来的。{ListenerName}偷了{StolenItemDesc}，跟{SpeakerSelfRef}走一趟，还是让{SpeakerSelfRef}动手？"

选项:
  ① [打]       → 进入战斗 → 赢→R2 / 输→R3
  ② [投降]     → R3
  ③ [和解]     → ACTION:INTENT:Settle（Charm/Roguery，成功率低）→ 成功→R4 / 失败→进战斗
  ④ [逃]       → 靠速度/进城甩开（15天超时→R5）
```

#### R2. Retaliation — 打赢（不结案）

```
效果: 恶名+2, RetaliationWaveCount++, RetaliationBudget扣减 → 经费够→下一波更强 / 不够→R4
```

#### R3. Retaliation — 战败/投降

```
效果: 被俘→带回村庄→A20 cutscene
```

#### R4. Retaliation — 和解/经费耗尽

```
效果: Stage=Resolved, {(和解) ? Trust-15 : PermanentEnemy=true}
```

#### R5. Retaliation — 逃避成功（15天超时）

```
效果: Stage=Resolved, Trust-30, 恶名+3, "该文化圈传开"
```

---

## 三、动态 DialogFlow 生成架构

### 3.1 不依赖 JSON 文件

**核心决策**：预写 JSON 穷举不可行——游戏状态组合爆炸。改为 `CrimeDialogueBuilder` 在运行时从游戏状态构建 `DialogueInjectScript`，走与 `InjectFromJson` 完全相同的注册逻辑。

```
三条路径，同一出口：

路径 A（静态调试）: 手写 JSON → JsonConvert.Deserialize → DialogueInjectScript ─┐
路径 B（生产）:     游戏状态 → CrimeDialogueBuilder.BuildScript → DialogueInjectScript ─┤
路径 C（LLM增强）:  游戏状态 → LLM生成 JSON → DialogueInjectScript ──────────────────┤
                                                                                   ├→ DialogueInjector.InjectScript → ConversationManager
                                                                                   └→ (debug) DumpTempJson
```

### 3.2 `DialogueInjector` 改造

提取 `InjectFromJson` 的后半段（JSON解析之后的所有注册逻辑）为独立 public 方法：

```csharp
/// <summary>
/// 直接注入 DialogueInjectScript 对象（不经过 JSON 文件）。
/// 与 InjectFromJson 共享同一套 ConversationManager 注册逻辑。
/// </summary>
public static string InjectScript(DialogueInjectScript script, string debugLabel = null)
{
    // = 原 InjectFromJson 的 "确定注入起始 token" 到 return 之间的全部逻辑 =
    // 仅 fileTag 改用 debugLabel ?? $"dyn_{_injectedOwners.Count}"
}
```

### 3.3 `CrimeDialogueBuilder` 入口

```csharp
public static class CrimeDialogueBuilder
{
    /// <summary>
    /// 玩家对 NPC 点"交谈"时调用。
    /// 返回 null = 该 NPC 不需要注入犯罪对话。
    /// </summary>
    public static DialogueInjectScript BuildScript(Hero speaker, Hero listener)
    {
        // 1. 找到该定居点的活跃 WorldEvent
        var settlement = speaker.CurrentSettlement;
        if (settlement == null) return null;
        var evt = WorldEventStore.FindActive(settlement.StringId);
        if (evt == null) return null;

        // 2. 判断说话者身份 → 构建对应场景
        if (IsAuthority(speaker, evt))
            return BuildAuthorityScript(evt, speaker, listener);

        if (evt.WitnessHeroIds?.Contains(speaker.StringId) == true)
            return BuildWitnessScript(evt, speaker, listener);

        if (evt.SuspectHeroId == speaker.StringId)
            return BuildSuspectScript(evt, speaker, listener);

        if (evt.TargetHeroId == speaker.StringId)
            return BuildVictimScript(evt, speaker, listener);

        // Bystander
        return BuildBystanderScript(evt, speaker, listener);
    }

    private static DialogueInjectScript BuildAuthorityScript(
        WorldEvent evt, Hero speaker, Hero listener)
    {
        var r = new PlaceholderResolver(evt, speaker, listener);
        var ctx = BuildIntentContext(evt, speaker);
        var turns = new List<DialogueInjectTurn>();

        switch (evt.Stage)
        {
            case EventStage.Emerging:
                if (PlayerHasInvestigationQuest(evt))
                    BuildReportTurn(turns, r, ctx);
                else
                    BuildDiscoveryTurn(turns, r, ctx);
                break;

            case EventStage.Active:
                if (evt.SuspectHeroId == Hero.MainHero.StringId)
                    BuildConfrontPlayerTurn(turns, r, ctx);
                else
                    BuildBountyOfferTurn(turns, r, ctx);
                break;

            case EventStage.Confrontation:
                BuildRetaliationTurn(turns, r, ctx);
                break;

            case EventStage.Resolved:
                BuildResolvedTurn(turns, r, ctx);
                break;

            case EventStage.Unsolved:
                BuildUnsolvedTurn(turns, r, ctx);
                break;
        }

        return new DialogueInjectScript
        {
            EntryOption = r.Resolve("{SpeakerRole}，听说{TargetSettlementName}出了点事？"),
            EntryTurn = "start",
            Turns = turns
        };
    }

    // BuildWitnessScript, BuildSuspectScript, BuildVictimScript, BuildBystanderScript ...
}
```

### 3.4 `PlaceholderResolver` — 运行时解析

```csharp
public class PlaceholderResolver
{
    public WorldEvent Event;
    public Hero Speaker;
    public Hero Listener;
    public NpcStance Stance;  // lazy: AttitudeSystem.ComputeStance(Speaker, Event)

    public string Resolve(string template)
    {
        return Regex.Replace(template, @"\{(\w+)\}", match => {
            var key = match.Groups[1].Value;
            return ResolveOne(key) ?? match.Value;  // 未知占位符保留原样
        });
    }

    /// <summary>
    /// 导出全部占位符 key→value，供 LLM 使用。
    /// </summary>
    public Dictionary<string, string> ExportContext()
    {
        // 反射/遍历本文件第一章全部占位符 → 调对应 ResolveOne → 返回字典
    }

    private string ResolveOne(string key) => key switch
    {
        // 本文件第一章的全部占位符映射在此实现
        // ...
        _ => null
    };
}
```

### 3.5 选项动态生成 + Intent 过滤

每轮对话的选项不由 JSON 静态定义——由相关 Intent 的 `Evaluate` 在注入时动态决定：

```csharp
static List<DialogueInjectOption> BuildOptions(
    string[] intentNames, PlaceholderResolver r, IntentContext ctx)
{
    var options = new List<DialogueInjectOption>();

    foreach (var name in intentNames)
    {
        var intent = IntentRegistry.Find(name);
        if (intent == null) continue;

        var eligibility = intent.Evaluate(ctx);
        if (eligibility.State == EligState.Hidden) continue;

        var opt = new DialogueInjectOption
        {
            PlayerLine = intent.ResolvePlayerLine(r),
            Action = $"INTENT:{name}",
            IsGreyed = eligibility.State == EligState.Disabled,
            DisabledReason = eligibility.Reason,
        };

        if (intent.Goal != null)  // 检定类 Intent
        {
            opt.NextTurn = $"result_{name}";
            // 同时注册检定成功/失败两个分支 turn
            BuildSkillCheckResultTurns(turns, intent, r);
        }
        else
        {
            opt.NextTurn = "close_window";
        }

        options.Add(opt);
    }

    return options;
}
```

---

## 四、新增玩法时的扩展流程

> 当添加新犯罪类型（如走私 Smuggling、纵火 Arson）或新对话场景时，需要扩充的内容：

### 4.1 需要修改的位置

| 改什么 | 位置 | 说明 |
|--------|------|------|
| 新 `EventConfig` | `EventTemplates` 注册表 | 填写 `DisplayName`, `FlavorProfile`, `AuthorityRole`, `VictimLabel` 等 |
| 新占位符（如需要） | 本文件第一章 | 如果新玩法出现了现有占位符无法描述的信息维度 |
| 新场景模板（如需要） | 本文件第二章 | 如果新玩法出现了现有 50+ 场景无法覆盖的对话情境 |
| 新 Intent（如需要） | `AccountabilityIntents.cs` | 如果新玩法有独特的玩家操作 |
| `CrimeDialogueBuilder` | 新增 Build 方法 | 如果新场景需要新的 turn 构建逻辑 |

### 4.2 判断"是否需要新占位符/新场景模板"的决策树

```
新犯罪类型有现有占位符无法描述的信息维度？
  ├─ YES → 在本文件第一章增补占位符 + 在 PlaceholderResolver.ResolveOne 中实现
  └─ NO  → 不改占位符表

新犯罪类型有现有场景无法覆盖的对话情境？
  ├─ YES → 在本文件第二章增补场景模板 + 在 CrimeDialogueBuilder 中增加构建方法
  └─ NO  → 不改场景模板（现有模板通过 {CrimeVerb}/{CrimeScene}/{VictimLabel} 自然适配）
```

### 4.3 扩展 Skill

创建 `.claude/skills/narrative-placeholder-extension.md`：

```markdown
---
name: narrative-placeholder-extension
description: 为新犯罪类型/新玩法自动完善叙事占位符逻辑
---

## 触发条件

用户说"新增犯罪类型 {TypeName}"或"扩展叙事占位符"时调用。

## 流程

1. **读取现状**：
   - 读取 `plans/narrative-placeholder-system.md`（本文件）
   - 读取 `EventTemplates` 中已有的全部 `EventConfig`
   - 读取 `CrimeDialogueBuilder` 中已有的全部 `Build*` 方法

2. **分析新玩法**：
   - 新犯罪类型的独特信息维度是什么？（如"纵火"有"烧了什么建筑"、"走私"有"违禁品类型"）
   - 这些信息维度是否已被现有占位符覆盖？
   - 新出现的对话情境是否已被现有场景模板覆盖？

3. **产出检查清单**：
   - 列出需要新增的占位符（含 C# 查询来源）
   - 列出需要新增的场景模板（含触发条件 + NPC 台词模板 + 选项列表）
   - 列出需要新增的 Intent（如适用）
   - 列出 `CrimeDialogueBuilder` 需要新增的 `Build*` 方法

4. **验证覆盖**：
   - 对照 v2 的设计文档，确认每个交互节点都有对应的场景模板和占位符
   - 生成一个"叙事覆盖矩阵"：犯罪类型 × 阶段 × 说话者身份 → 场景模板编号
```

---

## 五、与 v3 文档的关系

| v3 (`crime-consequence-composable-v3.md`) | 本文件 |
|-------------------------------------------|--------|
| 定义引擎架构（六层管线） | 定义对话表现（NPC 具体说什么 + 选项从哪来） |
| `WorldEvent` 数据模型 | `PlaceholderResolver` — 数据→叙事文本的桥梁 |
| `AttitudeSystem.ComputeStance` | `{SpeakerEmotion}` `{SpeakerOutrage}` 等 — 态度→文本的映射 |
| `IntentBase.Evaluate/OnSuccess/OnFail` | 选项可见性 + 动态文本 + 检定结果文本 |
| `EventConfig` 配置层 | `{CrimeVerb}` `{AuthorityRole}` `{VictimLabel}` — 配置→文本的映射 |
| `ResponsePattern` 行动生成 | `Build*` 方法中的选项组合逻辑 |
| JSON 骨架示例 | 全部场景模板（运行时动态生成，不存 JSON） |

**引用方式**：v3 第十部分"对话流设计"末尾加一句：

> **对话模板和占位符系统的完整规范见 [narrative-placeholder-system.md](narrative-placeholder-system.md)。** 本文件的 JSON 骨架（`crime_confront_player.json` 等）是静态示例；生产环境由 `CrimeDialogueBuilder` 从游戏状态动态构建 `DialogueInjectScript`，经 `DialogueInjector.InjectScript` 注入 `ConversationManager`。占位符由 `PlaceholderResolver` 在注入时解析。

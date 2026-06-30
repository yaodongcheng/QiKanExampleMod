# 犯罪→反应 通用可组合引擎 — 设计提案

> **目的**：替代"每种犯罪写一套完整状态机"的范式，用可组合的通用层让新玩法 = 一行配置。
> **对标**：KCD2 / RDR2 的系统涌现范式——不是把所有分支写出来，而是让简单规则互动产生丰富体验。
> **前置阅读**：[village-theft-consequences-v2.md](village-theft-consequences-v2.md)（体验目标）、[rules/wheels.md](rules/wheels.md)（已有轮子，本文每层都标注了复用哪个轮子）、[crime_general.md](crime_general.md)（本文的配套实现细节——对话 JSON、Intent 类签名、文件变更清单、v2 覆盖核对）

---

## 零、问题诊断

当前 `village-theft-consequences-v2.md` 是一份**极其详尽且叙事自洽**的设计。但它暴露了一个架构问题：

```
每种犯罪 = 新 Case 类 + 新 3 Issue + 新 3 Quest + 新手写对话树 + 新报复机制
         = ~1285 行设计文档 + 数千行 C#
         = 与其他犯罪类型零复用
```

这不是设计者的错——在无 LLM 的约束下，手写分支是唯一保证质量的方式。但如果我们想在 10 种犯罪类型上都达到这个质量，就需要**抽象出通用层**。

同时，本项目已经积累了大量的基础设施（`NewsSpreadSystem`、`CommissionHubIssue`/`CommissionQuest`、`IntentBase`/`IntentRegistry`、`DialogueInjector`、`SingNpcMemorySystem`、`WorldEventSimulator`/`WorldEventDirector`、`HeroNemesisTracker`……）。通用引擎不是从零造新车——是把已有的轮子组装成一辆能跑所有犯罪类型的车。

---

## 一、核心洞察：所有"犯罪→反应"共享同一条管线的骨架

回头看偷动物设计，把具体内容剥掉，剩下的骨架是：

```
①事实记录 → ②认知传播 → ③态度形成 → ④行动生成 → ⑤玩家介入 → ⑥收束
   │            │            │            │            │
   │       谁知道了？    知道了的人    基于态度+人格   玩家可以在任意
   │       怎么知道的？  怎么看你？    产生什么行动？  节点插手改变走向
   │
 谁对谁做了什么？
 严重程度？有没有人看见？
```

这六个环节**对任何犯罪类型都适用**。区别只在于：
- 偷羊 vs 暗杀：严重度不同、证据类型不同
- 但"目击者→传闻传播→态度计算→行动选择"的**机制完全相同**

---

## 二、通用引擎的六层架构（每层标注已有轮子）

### 三层数据定位（先理解，再展开）

在进入六层之前，先理清三个数据层次谁存什么：

```
第 1 层: WorldEvent (统一事件模型，1 份)
    ┌─ 客观真实（系统知道，NPC 不知道）:
    │    InitiatorId = "player123"        ← 真凶，发生时写入，不可变
    └─ 公共认知（NPC 可见，随调查演进）:
         SuspectHeroId = null             ← 嫌犯（null=未知, 调查后可能错）
         PublicAwareness = 0.0            ← 0.1=村民发现, 0.5=有嫌犯, 1.0=锁定

第 2 层: KnownEvent (每 NPC 1 份，已有，不动)
         在 SingNpcMemorySystem.KnownEvents 中 — 🛞 已有轮子
         PerceivedSeverity + DecayCounter
         NPC 的"在乎程度"，不存嫌犯信息

第 3 层: NpcStance (实时计算，不持久化)
         从 KnownEvent + NPC 人格 + 关系 → 四个维度 → 行动
```

**关键区分**（解释 v2 的 Stage 1 → Stage 2 迁移）：

| 概念 | 存在哪里 | 例 |
|------|---------|-----|
| 事实（谁偷的） | `WorldEvent.InitiatorId` | `"player123"`（系统知道，NPC 不一定） |
| 公共认知（嫌犯是谁） | `WorldEvent.SuspectHeroId` | `null` → `"player123"`（调查后填入，可能错） |
| 公众知道"出事了"的程度 | `WorldEvent.PublicAwareness` | `0.0 → 1.0`（影响 Issue 是否出现） |
| NPC 对"这件事"有多在乎 | `KnownEvent.PerceivedSeverity` | `60`（影响 NPC 会不会行动）— 🛞 已有 |
| NPC 基于嫌犯+人格的态度 | `NpcStance`（实时计算） | `outrage=0.7, fear=0.2` |

Stage 1（Discovery）：`PublicAwareness > 0.1` → NPC 知道出事了 → Issue `!` 出现。但 `SuspectHeroId == null` → 调查 Quest 的目标是"查清是谁"。
Stage 2（SuspectIdentified）：`SuspectHeroId` 被填入 → NPC 知道嫌犯了 → 态度从"关切"变成"指向性愤怒" → 行动从"要求调查"变成"悬赏/追捕/报复"。

有些犯罪一发生嫌犯就公开（如当场目击）：此时 `SuspectHeroId` 在创建 `WorldEvent` 时就填入，跳过 Stage 1。

---

### 第 1 层：统一事件模型 — `WorldEvent`

**核心决策：一个 `WorldEvent` 模型统一所有"世界上发生的事"。** 合并两个现有概念：
- `WorldEventData`（AI 模拟事件的存储）→ AI 专用字段作为可空字段纳入统一模型
- `VillageTheftCase`（本设计原先的偷窃案件）→ 废弃，迁移到 `WorldEvent`
- `SocialEvent`（NewsSpreadSystem 的传播载体）→ `BroadcastEvent` 改为直接接收 `WorldEvent`，后续弃用 `SocialEvent` 类

```csharp
[Serializable]
public class WorldEvent
{
    // ═══ 身份 ═══
    public string EventId;
    public EventCategory Category;     // Crime / Social / World
    public EventType Type;             // Theft_Animal, Murder, Poaching, BanditRaid, Divorce...
    public int Severity;               // 0-100

    // ═══ 客观真实（发生时写入，不可变） ═══
    public string InitiatorId;         // 作案者（系统知道的真凶）
    public string TargetHeroId;        // 受害者 hero（null = 村庄/抽象实体）
    public string TargetSettlementId;
    public string TargetItemId;
    public int Quantity;
    public float OccurredDay;

    // ═══ 目击（发生时记录） ═══
    public List<string> WitnessHeroIds;              // notable 目击者
    public Dictionary<string, int> TemplateWitness;  // 没脸村民模板→人数
    public bool WitnessesSilenced;

    // ═══ 公共认知（随调查演进，NPC 可见） ═══
    public float PublicAwareness;      // 0→1 (0.1=被发现, 0.5=有嫌犯, 1.0=锁定)
    public string SuspectHeroId;       // 嫌犯（null=未知, 可被玩家误导改变）

    // ═══ 玩家介入 ═══
    public bool CharmReprieveUsed;     // Charm 辩护每案仅一次
    public int FailCount;              // 栽赃失败计数（2次→fail forward）

    // ═══ 报复 ═══
    public int RetaliationBudget;
    public int RetaliationWaveCount;
    public string RetaliationPartyId;
    public float RetaliationSpawnDay;
    public bool PermanentEnemy;

    // ═══ AI 模拟事件专用（玩家犯罪时 null/default） ═══
    public string GeneratedPartyId;    // MobileParty.StringId — 🛞 复用 WorldEventSimulator
    public float? DayLimit;
    public int EscalationCount;
    public string ConspiracyId;
    public string HiddenMastermindId;
    public Dictionary<string, string> AuxiliaryPartyIds;

    // ═══ 状态 ═══
    public EventStage Stage;           // Dormant → Emerging → Active → Confrontation → Resolved
    public string ResolvedBy;
    public bool WasBroadcast;          // 已喂给 NewsSpreadSystem（仅一次）
    public float LastUpdateDay;
}

public enum EventStage
{
    Dormant,           // 事实已记录但尚未被发现（次日村民发现）
    Emerging,          // 被发现，调查/传闻开始传播 — 映射 v2 Stage 1 Discovery
    Active,            // 嫌犯锁定，Issue 公开 — 映射 v2 Stage 2 SuspectIdentified
    Confrontation,     // 报复/追捕/对峙 — 映射 v2 Stage 3 Retaliation
    Resolved,          // 已解决（赔钱/抓到贼/报复完成/威胁成功）
    Unsolved               // 不了了之（调查超时，没查到是谁，无疾而终）
}
```

**🛞 已有轮子对接**：

| WorldEvent 的职责 | 已有系统 | 对接方式 |
|------------------|---------|---------|
| 事件存储 + 持久化 | `WorldEventDatabase`（已有） | 改为 `WorldEventStore` 薄壳，存 `List<WorldEvent>` JSON |
| 传播 | `NewsSpreadSystem`（已有） | `BroadcastEvent(WorldEvent)` 直接接收 |
| AI 模拟事件生成 | `WorldEventSimulator`（已有，不动） | 后续 PR 改为产出 `WorldEvent` |
| 玩家通知推送 | `WorldEventDirector`（已有，不动） | 扩展推送 `WorldEvent` 阶段变化通知 |
| 偷窃追踪 | `VillageAnimalTracker`（已有，不动） | `RecordTheft` 触发 → `WorldEvent` 创建 |

**迁移策略**：

| 组件 | 本次 | 后续 |
|------|------|------|
| 玩家犯罪行为 | ✅ 直接用 `WorldEvent` | — |
| `WorldEventData` | 保留不动 | 后续 PR 迁移到 `WorldEvent` |
| `WorldEventSimulator` | 保留不动 | 后续 PR 改为产出 `WorldEvent` |
| `WorldEventDatabase` | 改为 `WorldEventStore` 薄壳 | — |
| `SocialEvent`（NewsSpreadSystem） | `BroadcastEvent` 改为直接接收 `WorldEvent` | 后续 PR 弃用 `SocialEvent` 类 |
| `VillageTheftCase` | 废弃，迁移 | — |

---

### 第 2 层：认知传播 — 🛞 复用 `NewsSpreadSystem`，不造新轮子

**从"事实发生了"到"有人知道了"之间的传播模拟。已有 `NewsSpreadSystem` 完整覆盖此层。**

```csharp
// 🛞 已有：NewsSpreadSystem.BroadcastEvent
// 链路：BroadcastEvent → ProcessHeroRecursively → ReceiveNews → KnownEvent
// 递归传播，按关系强度衰减，自动跨定居点扩散

// 改动点：BroadcastEvent 改为直接接收 WorldEvent（原接收 SocialEvent）
// 传播逻辑完全不变，只是入参类型变了
public void BroadcastEvent(WorldEvent evt)
{
    // 1. 注册到全局库
    // 2. 初始传播源（受害者/目击者/权威人物）
    // 3. ProcessHeroRecursively 递归传播 — 🛞 已有，不动
    // 4. 生成 SpreadReport + ScreenPlayOutline — 🛞 已有，不动
}
```

**跟当前 v2 设计的区别**：v2 的 `AdvanceInvestigation` 调查公式是硬编码在 `VillageTheftIssueBehavior` 里的。通用层把它交给 `NewsSpreadSystem`——传播速度由 `Severity` + `WitnessCount` 决定，不再依赖犯罪类型。

**PublicAwareness 的推进**在 `WorldEventStore.ProcessDaily()` 中：

```csharp
// Dormant → Emerging: 次日村民发现
if (DaysSince(evt.OccurredDay) >= 1f)
{
    evt.Stage = EventStage.Emerging;
    evt.PublicAwareness = 0.1f;
}

// Emerging 阶段每日推进
if (!evt.WasBroadcast)
{
    NewsSpreadSystem.Instance.BroadcastEvent(evt); // 🛞 直接传 WorldEvent
    evt.WasBroadcast = true;
}
evt.PublicAwareness += GetDailySpreadRate(evt); // Severity × WitnessCount 驱动

// 超时 → 不了了之（时长随 severity：偷羊 7 天，暗杀 30 天）
float coldDays = 3f + (evt.Severity / 100f) * 27f; // severity 0→3天, 50→16天, 100→30天
if (DaysSince(evt.OccurredDay) > coldDays && evt.PublicAwareness < 1.0f)
    evt.Stage = EventStage.Unsolved;
```

---

### 第 3 层：态度形成 — 🛞 读 `SingNpcMemorySystem.KnownEvents`，新增轻量 `NpcStance` 计算

**给定一个 NPC + 一个 WorldEvent → 这个 NPC 对涉案各方的态度是什么？**

**这层是涌现感的核心来源。** 不是手写"村长在阶段 2 对玩家冷脸"，而是从 NPC 的属性**计算**出来。

```
KnownEvent（🛞 已有，存储层）          AttitudeSystem（🆕 新增，计算层）
─────────────────────────────        ─────────────────────────────
存什么：                              做什么：
  EventId — 知道哪件事                 拿 KnownEvent.PerceivedSeverity
  PerceivedSeverity — 多在乎            + NPC 人格 trait（荣誉/残忍/贪婪…）
  DecayCounter — 随时间淡忘             + 与作案者/受害者的关系
                                       + 身份（权威人物？作案者是大佬？）
只存数据，不做判断                     → 产出 NpcStance（四个维度 + 综合态度）
                                       → 喂给 ResponseGenerator 生成行动

纯函数，不持久化——每次需要时实时计算。
因为输入变了（玩家赔钱了/嫌犯换人了/关系变了），态度自然跟着变。
```

**🛞 已有轮子**：`SingNpcMemorySystem.KnownEvents` 已存储了每个 NPC 对事件的 `PerceivedSeverity`（在乎程度）。态度计算从它出发。

```csharp
public struct NpcStance
{
    public float Outrage;          // 0→1 "这事不能忍" — 驱动悬赏/报复
    public float Fear;             // 0→1 "惹不起" — 抑制行动，驱动上报/退缩
    public float Sympathy;         // -1→1 负=同情作案者(宽容/包庇), 正=同情受害者(加码追责)
    public float SelfInterest;     // 0→1 "我能得什么好处" — 驱动索贿/敲诈封口
    public Attitude TowardActor;   // 综合态度
}

public enum Attitude
{
    Sympathetic,     // 同情——"他做得对"
    Understanding,   // 理解——"事出有因"
    Neutral,         // 无所谓
    Disapproving,    // 不赞同——"不该这样"
    Angry,           // 愤怒——"必须惩罚"
    Vengeful         // 仇恨——"血债血偿"
}

public static class AttitudeSystem
{
    public static NpcStance ComputeStance(Hero npc, WorldEvent evt)
    {
        var stance = new NpcStance();

        // 1. 基础：从 KnownEvent.PerceivedSeverity 出发 — 🛞 已有
        //    PerceivedSeverity 已经编码了：事件客观严重度 × NPC 与 victim 的社交距离
        //    → victimRelation 不在此层重复计算
        var knownEvent = AllNpcMemoryManager.GetMemory(npc.StringId)
            ?.KnownEvents.FirstOrDefault(e => e.EventId == evt.EventId);
        float perceivedSeverity = knownEvent?.PerceivedSeverity ?? 0;

        // 2. 人格修正 — 🛞 读 NPCProfile（已有）
        var profile = AllNpcMemoryManager.GetMemory(npc.StringId)?._profile;
        float honorMod = profile?.PersonalityTraits?.Contains("Honorable") == true ? 0.2f : 0f;
        float mercyMod = profile?.PersonalityTraits?.Contains("Merciful") == true ? -0.15f : 0f;
        float greedyMod = profile?.PersonalityTraits?.Contains("Greedy") == true ? 0.25f : 0f;

        // 3. 关系修正 — 基于嫌犯（NPC 认为是谁干的），不是真凶（InitiatorId）
        //    SuspectHeroId 为 null（阶段1 调查中）→ 关系修正不适用
        float suspectRelation = evt.SuspectHeroId != null
            ? npc.GetRelationWith(evt.SuspectHeroId) : 0f;

        // 4. 身份修正
        bool isLocalAuthority = IsAuthority(npc, evt.TargetSettlementId);
        bool suspectIsPowerful = evt.SuspectHeroId != null && IsPowerful(evt.SuspectHeroId);

        // 5. 合成四个维度
        stance.Outrage = Math.Clamp(
            (perceivedSeverity / 100f) + honorMod
            + (isLocalAuthority ? 0.3f : 0f)
            + (suspectRelation < -20 ? 0.15f : 0f)   // 嫌犯是仇人 → 更愤怒
            - (suspectRelation > 20 ? 0.15f : 0f),    // 嫌犯是朋友 → 更宽容
            0f, 1f);

        stance.Fear = Math.Clamp(
            (suspectIsPowerful ? 0.4f : 0f)            // 嫌犯是大人物 → 忌惮
            + (evt.Severity >= 80 ? 0.3f : 0f),        // Capital severity 附加恐惧
            0f, 1f);

        // Sympathy: 负=同情嫌犯(朋友/宽容), 正=同情受害者
        // victim 关系不重复——PerceivedSeverity 已经表达了"站在受害者那边"的强度
        stance.Sympathy = Math.Clamp(
            mercyMod * 2f
            + (suspectRelation > 20 ? -0.3f : 0f)   // 嫌犯是朋友 → 同情他
            + (suspectRelation < -20 ? 0.2f : 0f),   // 嫌犯是仇人 → 不同情
            -1f, 1f);

        stance.SelfInterest = Math.Clamp(
            greedyMod
            + (isLocalAuthority && evt.SuspectHeroId != null ? 0.2f : 0f)  // 知道嫌犯是谁才好敲诈
            + (stance.Outrage < 0.4f ? 0.15f : 0f),
            0f, 1f);

        stance.TowardActor = ComputeAttitude(stance);
        return stance;
    }
}
```

**这才是涌现**：
- 同样是偷羊，荣誉感高的村长 → Vengeful、荣誉感低的村长 → Disapproving
- 同样是偷羊，嫌犯是村长的朋友 → Understanding（"肯定有苦衷"），嫌犯是村长的仇人 → Angry
- 同样是偷羊，嫌犯是流浪汉 → NPC 都想抓，嫌犯是领主 → NPC 想抓但不敢（Fear 高）
- 嫌犯还没确定（阶段1）→ Fear/Sympathy 不触发，只有纯粹的 Outrage
- **不需要手写分支**——这些差异从参数互动中自然产生

**四个维度如何产生不同行为**：

**四个维度如何驱动 NPC 行为**：

| NPC 的心理状态 | 判定条件 | 他会怎么做 | v2 里对应的体验 |
|--------------|---------|-----------|---------------|
| 很生气 + 不怕嫌犯 | `Outrage > 0.5 && WillAct > 0.3 && Fear < 0.5` | 直接动手——悬赏抓人、组织报复队 | Stage 2/3 追捕/报复部队 |
| 很生气 + 但忌惮嫌犯 | `Fear > 0.5 && Outrage > 0.5 && WillAct < 0.2` | 自己不敢上，上报给更大的人物 | "大人物第二道坎"：想动不敢动 |
| 不太气 + 同情嫌犯 | `Sympathy < -0.3 && Outrage < 0.6` | 私下放一马，不报案 | 关系好的 NPC 私下警告 |
| 站在受害者那边 | `Sympathy > 0.3` | 拒绝赔钱了事，坚持要从严处理 | 不接受和解，必须抓人 |
| 站在受害者那边 + 很生气 | `Sympathy > 0.3 && Outrage > 0.5` | 加码追责：赔偿×2、赏金×2 | 悬赏金额翻倍，威慑力陡增 |
| 贪心 + 不太气 | `SelfInterest > 0.4 && Outrage < 0.5` | 趁机敲一笔——"给钱我就当没看见" | 索贿封口 |
| 什么都无所谓 | `WillAct < 0.15 && SelfInterest < 0.3 && \|Sympathy\| < 0.2` | 懒得管，案子不了了之 | 七天查不出 → Unsolved |

> `WillAct = max(0, Outrage - Fear)` — 愤怒减去恐惧，就是 NPC 愿意为此出多少力。
| 全低 | 冷漠 | 冷案："查不出来，算了" |

---

### 第 4 层：行动生成 — 🛞 复用 `CommissionHubIssue` + `CommissionQuest`，非新建 Issue/Quest 类

**基于态度 → 生成 NPC 的具体行动。这些行动直接映射为 Issue/Quest。**

**🛞 核心决策：不复写 3 个 Issue + 3 个 Quest 类。直接用已有的 `CommissionHubIssue` 和 `CommissionQuest`，通过 `IsAccountabilityQuest` 标志 + `WorldEvent` 参数区分行为。**

#### Stage → Issue → Quest 一一对应（通用类，不绑定犯罪类型）

```
WorldEvent.Stage         Issue 类 (🛞 复用)       Quest 类型                        ! 颜色
──────────────────────────────────────────────────────────────────────────────────
Dormant                 无                       无                               无
Emerging                CommissionHubIssue       InvestigateCrimeQuest             蓝色
Active (Suspect=Player) CommissionHubIssue       无（对话选项替代）                  黄色
Active (Suspect≠Player) CommissionHubIssue       CommissionQuest(BountyHunt)        黄色
Confrontation           CommissionHubIssue       无 / CommissionQuest(报复变体)      红色
Resolved                无                       无                               无
Unsolved                    无                       无                               无
```

**加盗猎只需**：`EventTemplates.Register(Poaching)` + 一份盗猎对话 JSON。三个 Issue 类零改动。

#### ResponseGenerator（态度→行动模板选择）

> **概念对应**：`ResponsePattern` 是 NPC 的"行为空间"——面对一个 WorldEvent，这个 NPC 能做什么。和玩家的 `PlayerGeneratedOption`（🛞 已有，`Memory/PlayerGeneratedOption.cs`）是同一个概念，只是 actor 不同：玩家侧由 `IntentBase` 子类表达（`Evaluate` → 解锁选项），NPC 侧由 `ResponsePattern` 枚举表达（`ComputeStance` → 匹配行动模板）。玩家选项可以 LLM 生成（`PlayerGeneratedOption`），NPC 行动必须确定性——它驱动 Quest 生成。

```csharp
/// <summary>
/// NPC 对一个 WorldEvent 可采取的行动模板。
/// 和玩家的 PlayerGeneratedOption 是同一抽象——"行为空间"——只是 actor 不同。
/// </summary>
public enum ResponsePattern
{
    // 和解类
    DemandRestitution,     // 要求赔偿
    GoEasy,                // 宽容/包庇（"这次算了"）
    ExtortBribe,           // 索贿封口（"给钱我就当没看见"）

    // 法律类
    IssueBounty,           // 发布悬赏 — 🛞 复用 CommissionQuest(BountyHunt)
    ReportToLord,          // 上报领主

    // 对抗类
    SendThugs,             // 派打手教训
    LeadRetaliation,       // 组织报复队 — 🛞 复用 CommissionQuest + SpawnPursuerParty
    AmplifyPunishment,     // 加码追责 — Sympathy→受害者(+)，赔偿/赏金翻倍（不独立出现，附加在已有惩罚行动上）

    // 逃避类
    Intimidate,            // 被威胁后忍气吞声
    Indifferent            // 冷漠/不了了之
}

public static class ResponseGenerator
{
    /// <summary>
    /// 基于效用的行动选择：四个 stance 维度是 utility 输入，
    /// 每个 ResponsePattern 有自己的阈值条件（= 简化的 utility curve）。
    /// 不用传统"打分→排序"，而是"过门坎即解锁"——更可预测、更易调试。
    /// </summary>
    public static List<ResponsePattern> GenerateResponses(Hero authority, WorldEvent evt)
    {
        var stance = AttitudeSystem.ComputeStance(authority, evt);
        var actions = new List<ResponsePattern>();

        float willAct = Math.Max(0, stance.Outrage - stance.Fear);

        // 🔓 索贿封口 — SelfInterest↑ Outrage↓
        if (stance.SelfInterest > 0.4f && stance.Outrage < 0.5f)
            actions.Add(ResponsePattern.ExtortBribe);

        // 🔓 宽容/包庇 — Sympathy→作案者(-)
        if (stance.Sympathy < -0.3f && stance.Outrage < 0.6f)
            actions.Add(ResponsePattern.GoEasy);

        // 🔓 要求赔偿 — 有点生气，不太怕
        if (stance.Outrage > 0.3f && stance.Fear < 0.7f)
            actions.Add(ResponsePattern.DemandRestitution);

        // 🔓 发布悬赏 — 很生气，愿意动，不太怕
        if (stance.Outrage > 0.5f && willAct > 0.3f && stance.Fear < 0.5f)
            actions.Add(ResponsePattern.IssueBounty);

        // 🔓 Sympathy→受害者(+) — 加码追责（赔偿×2、赏金×2 等，由 Quest 生成层读取 stance 后自行加倍）
        //    不新增 ResponsePattern，而是给已有行动的强度乘系数
        if (stance.Sympathy > 0.3f)
            actions.Add(ResponsePattern.AmplifyPunishment);  // 信号：所有惩罚力度翻倍

        // 🔓 组织报复 — 非常生气 + 愿意动
        if (stance.Outrage > 0.7f && willAct > 0.5f)
            actions.Add(ResponsePattern.LeadRetaliation);

        // 🔓 忍气吞声 — Fear > Outrage
        if (stance.Fear > stance.Outrage)
            actions.Add(ResponsePattern.Intimidate);

        // 🔓 上报领主 — 生气但太怕（v2 的"大人物第二道坎"）
        if (stance.Fear > 0.5f && stance.Outrage > 0.5f && willAct < 0.2f)
            actions.Add(ResponsePattern.ReportToLord);

        // 🔓 冷漠 — 全低
        if (willAct < 0.15f && stance.SelfInterest < 0.3f && Math.Abs(stance.Sympathy) < 0.2f)
            actions.Add(ResponsePattern.Indifferent);

        return actions;
    }
}
```

**v2 阶段迁移的本质**：不是硬编码的枚举迁移，而是 `GenerateResponses` 的输出随 stance 变化自然演变——这就是 **utility-based AI**：四个维度是 utility 输入，每个 `ResponsePattern` 的阈值条件就是它的 utility curve。不用传统"逐个打分取最高"，而是"过门坎即解锁"——更可预测、更易调试。：
- 阶段 1: Outrage 低 → 只有 `DemandRestitution`
- 阶段 2: Outrage 中 → + `IssueBounty`
- 阶段 3: Outrage 高 → + `LeadRetaliation`，`DemandRestitution` 消失

#### Quest 生成（玩家接委托时）— 🛞 扩展 CommissionGenerator

```csharp
// CommissionGenerator.GenerateCommissions 扩展
var fact = WorldEventStore.FindActive(hero.CurrentSettlement.StringId);
if (fact != null)
{
    var data = new CommissionData
    {
        Category = CommissionCategory.BountyHunt,
        IsAccountabilityQuest = true,   // 🆕 新字段
        FactId = fact.EventId,          // 🆕 新字段
        QuestGiver = hero,
        TargetHero = Hero.Find(fact.SuspectHeroId),
        TimeRemainingHours = (fact.Stage == EventStage.Confrontation ? 15 : 10) * 24f,
    };
    new CommissionQuest($"crime_{fact.EventId}", data).StartQuest();
}
```

---

### 第 5 层：玩家介入 — 🛞 `IntentBase` 做逻辑，`DialogueInjector` JSON 做表现

**玩家在任意阶段的介入选项，由"玩家技能 + 当前 WorldEvent 状态 + 关键 NPC 态度"三者共同决定。**

**核心决策：`IntentBase` 和 `DialogueInjector` 不是二选一——是分层协作。**

```
DialogueInjector JSON（表现层）           IntentBase 子类（逻辑层）
────────────────────────────           ─────────────────────────
turn → Option                          class PayRestitutionIntent : IntentBase {
  PlayerLine: "这是赔偿，够不够？"         Evaluate(ctx) → Gold >= cost ? Show : Grey
  Action: "INTENT:PayRestitution"        OnInstant(ctx) → TransferGold → Resolved
  ActionValue: "{cost}"                }
       ↓                                     ↓
ExecuteAction 解析 "INTENT:xxx"         🛞 复用已有的 Evaluate/OnSuccess/OnFail 模式
  → 查 IntentRegistry 找对应 Intent      🛞 复用 SingleRollResolver 检定公式
  → 调 Evaluate 决定选项可见/灰掉          🛞 复用 IntentCooldownStore 冷却
  → 调 OnInstant / OnSuccess / OnFail   🛞 复用 AgentControlHelper 资源操作
```

**为什么这样做**：
- JSON 管"这句话怎么说、在哪段对话出现"——表现层
- Intent 管"这个选项有没有资格出现、成了怎样败了怎样"——逻辑层
- 同一个 Intent 既可以在 Quest 对话里用（JSON 驱动），也可以在自由对话里用（StoryDialogVM 驱动）——比如 `SilenceWitnessIntent`，既可以在调查 Quest 汇报环节触发，也可以在村里碰见证人闲聊时触发

**IntentContext 扩展**：加一个可空的 `WorldEvent` 字段。

```csharp
public class IntentContext
{
    // ... 现有字段 ...
    public WorldEvent ActiveEvent;  // 🆕 null = 非追责场景，Intent 走原有逻辑
}
```

**7 个追责 Intent（🛞 标准 IntentBase 子类）**：

```csharp
// 1. PayRestitutionIntent — 赔钱消灾
//    Evaluate: ActiveEvent != null && Gold >= cost（cost 从 ActiveEvent.Severity + stage 计算）
//    Cost 公式: 基础赔偿 = 物品价值 × BaseRestitutionMultiplier（阶段2: ×3, 阶段3: ×5 + 罚金）
//    Trade 讨价还价: 赔偿额 × (1 - TradeSkill * 0.005)，即 Trade 300 = 最高 15% 折扣
//    OnInstant: 🛞 AgentControlHelper.TransferGold(玩家→权威NPC, cost) → evt.ResolvedBy = "payment"

// 2. CharmDefenseIntent — 辩护（每案一次）
//    Evaluate: ActiveEvent != null && IsAccused && !ActiveEvent.CharmReprieveUsed
//    OnSuccess: evt.SuspectHeroId = null, evt.PublicAwareness = 0.5, CharmReprieveUsed = true
//    OnFail: Trust -10, evt.Stage = Confrontation

// 3. FrameSuspectIntent — 栽赃（含子选项）
//    Evaluate: ActiveEvent != null && PlayerTheftLedger 有候选
//    子选项: ① "是强盗干的" (DC 40) ② "是{账本Hero}干的" (DC 35-85)
//    栽赃完整逻辑: DC表 + [出示证物]+20 + fail forward(2次→转回玩家) + 大人物第二道坎
//
//    FrameSubOption（子选项数据，由 FrameSuspectIntent.Evaluate 动态生成候选列表）:
//    public class FrameSubOption
//    {
//        public string TargetId;        // "bandit" 或 heroId
//        public string DisplayName;     // "附近藏身处的强盗" / "{Hero.Name}"
//        public int BaseDC;             // 40-85（按目标身份自动计算：强盗40/流浪汉35/村民55/商人70/领主85）
//        public bool CanShowEvidence;   // 账本有该英雄的偷窃记录 + 背包仍有对应物品
//        public bool IsPowerful;        // 是否是大人物（商人/领主 → 触发第二道坎）
//    }

// 4. ThreatIntent — 威胁
//    Evaluate: ActiveEvent != null && IsAccused && Roguery >= 50
//    OnSuccess: 恶名+1, Trust暴跌, evt.ResolvedBy = "intimidated"
//    OnFail: evt.Stage = Confrontation

// 5. InvestigateIntent — 接调查 Quest
//    Evaluate: ActiveEvent != null && evt.Stage == Emerging

// 6. SilenceWitnessIntent — 收买/吓唬目击者
//    Evaluate: ActiveEvent != null && 存在未封口的目击者
//    可在 Quest 对话中（JSON）或自由对话中（StoryDialogVM）触发

// 7. LeadRetaliationIntent — 带队报复（嫌犯≠自己）
//    Evaluate: ActiveEvent != null && evt.Stage == Confrontation && SuspectHeroId != 玩家
```

**JSON 如何委托给 Intent**：

```json
{
  "PlayerLine": "这是赔偿，够不够？({cost} 第纳尔)",
  "Action": "INTENT:PayRestitution",
  "ActionValue": "{cost}"
}
```

```csharp
// DialogueInjector.ExecuteAction 扩展：
case string a when a.StartsWith("INTENT:"):
    var intentName = a.Split(':')[1];
    var intent = IntentRegistry.Find(intentName);  // 🛞 从已有注册表查找
    var ctx = IntentContext.Build(agent, controller);
    ctx.ActiveEvent = WorldEventStore.FindActive(settlement.Id);  // 🆕 注入事件上下文
    
    // Evaluate → 决定选项可见性（已在注册 JSON 前完成）
    // 检定 → 🛞 SingleRollResolver.Compute
    // 结果 → intent.OnSuccess(ctx) 或 intent.OnFail(ctx)
    break;
```

**JSON 注册时动态过滤**：注入对话前，先用每个 Intent 的 `Evaluate` 筛一遍——不可见的选项直接从 JSON turns 里移除，玩家看到的只有当下有资格选的。

---

### 第 6 层：事实模板 — `EventConfig`（配置层）

**这是新玩法的入口。加一种新犯罪类型 = 注册一个配置 + 实现它独有的几个钩子。**

```csharp
public class EventConfig
{
    public EventType Type;
    public EventCategory Category;
    public int DefaultSeverity;            // 0-100
    public string VictimLabel;             // "村庄" / "死者家族" / "领主猎场"
    public string AuthorityRole;           // "村长" / "族长" / "领主" — 决定谁出面、对话风格、可用手段

    // ── 传播 ──
    public float BaseSpreadRate;           // 基础认知传播速度

    // ── 经济 ──
    public int BaseRestitutionMultiplier;  // 基础赔偿倍数（×动物价值 / ×物品价值）
    public int BaseBountyPerUnit;          // 基础赏金/单位

    // ── 行为偏好（微调 stance→action 映射，不改阈值公式） ──
    // 用于区分"severity 相同但性质不同"的犯罪。例：偷羊 vs 偷窃圣物，severity 都是 30，
    // 但后者更倾向 ReportToLord 而非 DemandRestitution（权威人物觉得"这是渎神，不是钱能解决的"）。
    // 留空 = 完全由 stance 阈值驱动。填入 = 给定 stance 产出多个 action 时，优先显示这些。
    public List<ResponsePattern> PreferredResponses;

    // ── 唯一钩子：发现条件 ──
    // 通用框架只问这一个问题："这个犯罪被发现了没？"
    // 偷牲口 = 次日自动；暗杀 = 尸体被人发现 or 家属发现人不见了
    public Func<WorldEvent, bool> DiscoveryCheck;  // null = 默认次日发现
}
```

**每种犯罪的独特代码分布在各自自然的位置，不在 `EventConfig` 里**：

```
EventConfig          只放"通用框架需要知道"的参数 + 唯一钩子 DiscoveryCheck
                     ↓
① Mission 层作案    已经在各自的地方了：
                     偷牲口 → InteractionMissionView.TryStealAnimal（🛞 已有）
                     暗杀   → 战斗系统 / 潜行击杀（🛞 已有）
                     盗猎   → 新 MissionLogic：射杀动物+剥皮

② WorldEvent 创建   在作案代码的末尾各写一行：
                     WorldEventStore.Add(new WorldEvent { Type = Xxx, ... });
                     偷牲口写在 TryStealAnimal 末尾，暗杀写在 OnAgentKilled 监听里

③ 发现机制          EventConfig.DiscoveryCheck 委托
                     偷牲口 = null（默认次日） / 暗杀 = 尸体被发现检查

④⑤ 现场+证据       Step4+ 各写各的 MissionLogic（懒生成证据 GameEntity）
                     MVP 不做，纯对话

⑥ 特有 Intent       🛞 标准 IntentBase 子类，Evaluate 里检查 EventType：
                     class HideBodyIntent : IntentBase {
                         Evaluate → ctx.ActiveEvent?.Type == EventType.Murder ? Show() : Hide()
                     }
                     注册在 IntentRegistry.RegisterDefaults()

⑦ 对话风味          同一个 crime_*.json，文本里的 {VictimLabel} {AuthorityRole}
                     占位符自动替换——偷羊="村长"，暗杀="族长"
```

public static class EventTemplates
{
    public static readonly EventConfig AnimalTheft = new()
    {
        Type = EventType.Theft_Animal,
        Category = EventCategory.Crime,
        DefaultSeverity = 30,
        VictimLabel = "村庄",
        AuthorityRole = "村长",
        BaseSpreadRate = 0.1f,
        BaseRestitutionMultiplier = 3,
        BaseBountyPerUnit = 50,
        PreferredResponses = { ResponsePattern.DemandRestitution, ResponsePattern.IssueBounty },
    };

    public static readonly EventConfig Murder = new()
    {
        Type = EventType.Murder,
        Category = EventCategory.Crime,
        DefaultSeverity = 100,
        VictimLabel = "死者家族",
        AuthorityRole = "族长",
        BaseSpreadRate = 0.5f,
        BaseRestitutionMultiplier = 50,
        BaseBountyPerUnit = 5000,
        PreferredResponses = { ResponsePattern.ReportToLord, ResponsePattern.LeadRetaliation },
    };

    public static readonly EventConfig Poaching = new()
    {
        Type = EventType.Poaching,
        Category = EventCategory.Crime,
        DefaultSeverity = 50,
        VictimLabel = "领主猎场",
        AuthorityRole = "领主",
        BaseSpreadRate = 0.15f,
        BaseRestitutionMultiplier = 10,
        BaseBountyPerUnit = 200,
        PreferredResponses = { ResponsePattern.ReportToLord, ResponsePattern.IssueBounty },
    };
}
```

**同样的管线，不同的体验——severity 驱动一切**：

| | 偷牲口 (severity 30) | 盗猎 (severity 50) | 暗杀 (severity 100) |
|---|------|------|------|
| 谁来找你 | 村长 | 领主 | 族长 |
| 能赔钱吗 | ✅ Outrage 中低 → `DemandRestitution` 触发 | ⚠️ Outrage 中 → 可能触发，但态度硬 | ❌ Outrage 满 → `DemandRestitution` 不触发 |
| 不了了之 | ~11 天后 | ~16 天后 | ~30 天后 |
| 报复手段 | `IssueBounty` → 3 波报复队 | `IssueBounty` + 可能 `ReportToLord` | `LeadRetaliation` + `ReportToLord` 同时触发 |
| 玩家感受 | "偷了几只羊，赔就赔了" | "领主的东西也敢动？" | "这事没法善了……" |

差异全部来自 `DefaultSeverity` → stance → `GenerateResponses`，没有一个多余的配置开关。

**加新玩法的流程从"写一篇上千行的设计文档 + 多个 C# 类"变成了**：

```csharp
// 新玩法：盗猎
EventTemplates.Register(EventTemplates.Poaching);
// + 下面一节说的"每种犯罪不可替代的部分"
```

---

### 通用框架省了什么、省不了什么

诚实地说：通用框架管的是**"事情已经发生了 → NPC 怎么反应"**这条后续链。但犯罪的**"怎么发生的"**不在框架里——那是每种犯罪独有的。

```
                         ┌── 通用框架管这一段 ──┐
                         │                      │
  ①怎么作案 → ②怎么被发现 → ③调查 → ④嫌犯 → ⑤反应 → ⑥收束
     │              │         │                      │
     └── 每种犯罪 ──┘         └── 每种犯罪自己做的 ────┘
     自己实现                   （下面展开）
```

**通用框架省掉的部分**（以前每种犯罪都要手写一遍的）：

| 以前（v2 偷羊方案） | 现在 |
|------------------|------|
| `VillageTheftCase` 数据模型 | `WorldEvent` 通用 |
| `VillageTheftIssueBehavior` 阶段迁移 | `WorldEventStore.ProcessDaily` 通用 |
| `AdvanceInvestigation` 调查公式 | `NewsSpreadSystem` 传播（已有） |
| 3 个 Issue + 3 个 Quest 类 | `CommissionHubIssue` + `CommissionQuest`（已有） |
| 手写对话树 | `DialogueInjector` JSON（已有） |
| 手写 NPC 态度分支 | `AttitudeSystem` + `GenerateResponses` 通用 |
| 手写报复部队 spawn | `SpawnPursuerParty`（已有） |
| 手写玩家干预选项 | `IntentBase` 子类（已有模式） |

**框架省不了的部分**（每种犯罪必须自己做的、不可替代的）：

| 必须自己做的 | 偷牲口 | 暗杀 | 盗猎 |
|------------|--------|------|------|
| **① Mission 层作案方式** | 靠近动物→`ForcePlayAction("act_pickup_down_begin")`→加背包 | 战斗击杀 Agent | 射杀动物→剥皮/捡尸 |
| **② 作案后写入 WorldEvent** | `VillageAnimalTracker.RecordTheft` → 创建 `WorldEvent` | 监听 `OnAgentKilled` → 创建 `WorldEvent` | 监听动物死亡 → 创建 `WorldEvent` |
| **③ 发现机制** | 次日 DailyTick 村民发现 | 尸体被发现（有人路过 or 定时）或家属发现人不见了 | 猎场守卫巡逻发现 |
| **④ 犯罪现场** | 无——"圈里少了牲口"是抽象概念 | **有**——尸体位置就是现场，可以回去处理 | **有**——猎场里的尸体+箭矢，可以回去清理 |
| **⑤ 特有证据** | 无物理证据，只有目击者 | 尸体 + 凶器 + 血迹 | 动物尸体 + 箭矢（可追溯箭的归属） |
| **⑥ 特有玩家操作** | 栽赃（🛞 `FrameSuspectIntent` 通用，但栽赃候选来自 `PlayerTheftLedger`） | **藏尸**、**清理现场** | **清理证据**（拿走箭、埋尸体） |
| **⑦ 特有对话风味** | 村民集体焦虑 | 死者家属的悲痛/复仇欲 | 领主的权威被冒犯 |

**工作量估算**：

| | 偷牲口 | 暗杀 | 盗猎 |
|---|-------|------|------|
| ① 作案方式 | 🛞 已有（`TryStealAnimal`） | 🛞 已有（战斗系统） | 🆕 ~100 行（识别动物+剥皮） |
| ② WorldEvent 创建 | ~20 行（`RecordTheft` 尾加） | ~30 行（`OnAgentKilled` 监听） | ~30 行 |
| ③ 发现机制 | 🛞 通用（次日发现） | ~50 行（尸体被发现逻辑） | ~50 行（守卫巡逻发现） |
| ④⑤ 现场+证据 | 无需 | ~100 行（保留尸体+懒生成证据） | ~80 行（保留尸体+箭矢归属） |
| ⑥ 特有操作 | 🛞 通用（`FrameSuspectIntent`） | ~80 行（`HideBodyIntent`） | ~60 行（`CleanEvidenceIntent`） |
| ⑦ 对话 JSON | 🛞 通用（crime_*.json） | 换 flavor 文本 | 换 flavor 文本 |
| **框架省掉** | — | ~1500 行（3 Issue+3 Quest+态度+报复+对话） | ~1500 行 |
| **必须自己写** | ~20 行 | ~260 行 | ~320 行 |

**结论**：通用框架不是"加新犯罪 = 一行配置"的魔法——是**把 1500 行的重复劳动省掉了**。剩下的 20~320 行是每种犯罪真正独特的部分，省不掉的——也不该省，那才是玩法差异的来源。

---

## 三、对话流设计（🛞 唯一呈现引擎：`DialogueInjector`，多种数据源）

### 核心架构

```
数据源                               注入引擎                    呈现
───────                              ────────                    ────
JSON 文件 ──────────────┐
  · 对话骨架（turn 结构）  │
  · 固定文本（NPC 台词）   ├──→  DialogueInjector  ──→  ConversationManager
  · 静态选项              │     AddPlayerLine /           （原版对话 UI）
                          │     AddDialogLineMultiAgent
IntentBase 动态构建 ──────┘
  · Evaluate → 选项可见？
  · 动态 DC / 动态成本
  · 动态候选名单（栽赃目标）
```

**JSON 出骨架，Intent 出数据。** JSON 定义"这段对话有几个 turn、NPC 说什么"，但选项的可见性、DC、成本等**动态数据**由 Intent 的 `Evaluate` 在注入时实时计算填入。两者不冲突——JSON 是模板，Intent 是模板里的变量。

### 注入流程

```csharp
// DialogueInjector.InjectCrimeDialogue(headman, evt):
// 1. 加载 JSON 骨架（crime_confront_player.json 等）
// 2. 遍历每个 Option：
//    a. 如果 Action = "INTENT:Xxx" → IntentRegistry.Find(Xxx).Evaluate(ctx)
//       · Show → 保留选项，填入动态文本（DC、成本、候选名）
//       · Grey → 保留但灰掉，填入 DisabledReason
//       · Hide → 从 JSON 中移除这个选项
//    b. 如果 Action 不是 INTENT → 走 JSON 自己的 Condition 字段
// 3. 过滤后的 turns → AddPlayerLine / AddDialogLineMultiAgent → 注入原版对话树
```

### JSON 骨架（固定部分）+ Intent 数据（动态部分）

```json
{
  "Id": "confront_player",
  "SpeakerIndex": 0,
  "NpcLine": "（冷冷地看着你）你还敢来？村里人都说是你干的。你有什么要说的？",
  "Options": [
    {
      "PlayerLine": "你们搞错了。给我个机会说清楚。",
      "Action": "INTENT:CharmDefense"
    },
    {
      "PlayerLine": "这是赔偿，够不够？",
      "Action": "INTENT:PayRestitution"
    },
    {
      "PlayerLine": "你再说一遍？（手按在剑柄上）",
      "Action": "INTENT:Threat"
    },
    {
      "PlayerLine": "（转身就走）",
      "Action": "NONE"
    }
  ]
}
```

```json
// 栽赃——子选项由 Intent 动态生成
{
  "Id": "report_findings",
  "NpcLine": "怎么样？查到什么了吗？",
  "Options": [
    {
      "PlayerLine": "是附近藏身处的强盗干的！",
      "Action": "INTENT:FrameSuspect",
      "ActionValue": "bandit"
    },
    {
      "PlayerLine": "是 {TargetName} 干的。",
      "Action": "INTENT:FrameSuspect",
      "ActionValue": "{TargetId}",
      "RepeatFor": "FrameTargets"   // 🆕 动态展开：每个栽赃候选人生成一条选项
    }
  ]
}
```

`RepeatFor: "FrameTargets"` → 注入时 `FrameSuspectIntent.Evaluate` 返回候选名单（`List<FrameSubOption>`）→ 每个候选展开一条选项，文本里 `{TargetName}` 替换成对应名字。

**栽赃后续 turn（运行时分支）**：

```json
// ── 强盗分支：确认 turn ──
{
  "Id": "frame_bandit_confirm",
  "NpcLine": "强盗偷牲口，天经地义。好，我信你——就是他们干的！",
  "Options": [
    {
      "PlayerLine": "那我去把强盗窝端了。",
      "NpcResponse": "拜托了！抓到贼首，我必有重谢。",
      "Action": "INTENT:FrameSuspect",
      "ActionValue": "bandit"
    }
  ]
}

// ── 具体人分支：有/无证物分流 ──
{
  "Id": "frame_hero_present_evidence",
  "NpcLine": "这件东西……你是从哪找到的？",
  "Options": [
    {
      "PlayerLine": "[出示证物] 在他住处附近捡到的。",
      "NpcResponse": "（仔细看了看）……这确实是他的东西。好，那就是他了！",
      "Condition": "HasEvidence()",
      "Action": "INTENT:FrameSuspect",
      "ActionValue": "{TargetId}",
      "ActionParam": "WithEvidence"
    },
    {
      "PlayerLine": "我没证据，但我肯定是他。",
      "NpcResponse": "光凭嘴说可不行……（犹豫地看着你）",
      "Condition": "!HasEvidence()",
      "NextTurn": "frame_bare_roll"
    }
  ]
}

// ── 无证物裸过检定结果 ──
{
  "Id": "frame_bare_roll",
  "NpcLine": "{SkillCheckResult}",
  "Options": [
    {
      "PlayerLine": "{Success: 我就知道！/ Failure: 换个人指……}",
      "NpcResponse": "{Success: 好，我信你。/ Failure: 你越说越不对劲……}",
      "NextTurn": "{Success: close_window / Failure: fail_forward}",
      "Action": "{Success: INTENT:FrameSuspect / Failure: NONE}"
    }
  ]
}

// ── fail forward：栽赃失败分支（第一次 vs 第二次） ──
{
  "Id": "fail_forward",
  "NpcLine": "{FailCount == 1: '这次就算了，你再去查查。' / FailCount >= 2: '你一会指这个一会指那个……该不会就是你干的？'}",
  "Options": [
    {
      "PlayerLine": "{FailCount == 1: 我再查查 / FailCount >= 2: （语塞）}",
      "NpcResponse": "{FailCount == 1: 去吧。/ FailCount >= 2: 果然是你！（嫌疑转回玩家）}",
      "Action": "{FailCount == 1: NONE / FailCount >= 2: INTENT:SuspectPlayer}"
    }
  ]
}

// ── 玩家主动认栽（可选路径，IsPlayerThief() 条件可见） ──
{
  "Id": "confess_restitution",
  "NpcLine": "你？！……（沉默片刻）好。既然你自己认了，咱们可以商量。",
  "Options": [
    {
      "PlayerLine": "我愿意赔。",
      "Action": "INTENT:PayRestitution"
    }
  ]
}
```

> **这些 JSON turn 全部在 `crime_report.json` 中。** `{SkillCheckResult}` `{Success:.../Failure:...}` `{FailCount == 1:.../FailCount >= 2:...}` 由 `DialogueInjector` 在注入时根据运行时状态填充——JSON 是模板，运行时状态是变量。

### 数据源分工

| | JSON 负责 | Intent 负责 |
|---|---------|-----------|
| NPC 台词 | ✅ 固定文本 | — |
| 对话 turn 结构 | ✅ Id / NextTurn 跳转 | — |
| 选项可见性 | 静态 Condition | ✅ Evaluate(ctx) 动态判断 |
| 选项文本 | ✅ 骨架 + 占位符 | ✅ 动态填充（`{cost}` `{DC}` `{TargetName}`） |
| 技能 DC | — | ✅ 运行时计算 |
| 检定逻辑 | — | ✅ 🛞 SingleRollResolver |
| 结算效果 | — | ✅ OnSuccess / OnFail / OnInstant |
| 冷却 | — | ✅ 🛞 IntentCooldownStore |

### JSON 中的 Action 类型

```json
// 委托给 Intent（动态选项）：
{ "Action": "INTENT:PayRestitution" }
{ "Action": "INTENT:CharmDefense" }
{ "Action": "INTENT:Threat" }
{ "Action": "INTENT:FrameSuspect", "ActionValue": "bandit" }
{ "Action": "INTENT:SilenceWitness" }

// 直接动作（静态，不需要 Intent）：
{ "Action": "START_QUEST", "ActionValue": "InvestigateCrime" }
{ "Action": "START_QUEST", "ActionValue": "BountyHunt" }
{ "Action": "NONE" }
```

```csharp
// DialogueInjector.ExecuteAction 扩展：
case string a when a.StartsWith("INTENT:"):
    var intent = IntentRegistry.Find(a);
    var ctx = IntentContext.Build(agent, controller);
    ctx.ActiveEvent = WorldEventStore.FindActive(settlement.Id);
    
    if (intent is IInstantIntent instant)
        instant.OnInstant(ctx);
    else
        ResolveSkillCheck(intent, ctx);  // 🛞 SingleRollResolver → OnSuccess/OnFail
    break;
```

### 全部对话 JSON 文件

| 文件 | 场景 | 触发条件 |
|------|------|---------|
| `crime_discovery.json` | 阶段1: 调查 Quest 接取 | `Stage==Emerging` |
| `crime_report.json` | 阶段1: 汇报调查+栽赃（含 `RepeatFor` 动态候选） | 持有调查 Quest |
| `crime_confront_player.json` | 阶段2: 嫌犯=玩家，冷脸对峙 | `Stage==Active && Suspect==Player` |
| `crime_bounty_offer.json` | 阶段2: 嫌犯≠玩家，悬赏 Quest | `Stage==Active && Suspect!=Player` |
| `crime_retaliation.json` | 阶段3: 报复部队说明 | `Stage==Confrontation` |
| `crime_witness_silence.json` | 目击者收买/吓唬 | 存在未封口 notable 目击者 |

### 全部对话 JSON 文件清单

| 文件 | 场景 | 触发条件 |
|------|------|---------|
| `crime_discovery.json` | 阶段1: 调查 Quest 接取 | `Stage==Emerging` |
| `crime_report.json` | 阶段1: 汇报调查+栽赃 | 持有调查 Quest |
| `crime_confront_player.json` | 阶段2: 嫌犯=玩家，冷脸对峙 | `Stage==Active && Suspect==Player` |
| `crime_bounty_offer.json` | 阶段2: 嫌犯≠玩家，悬赏 Quest | `Stage==Active && Suspect!=Player` |
| `crime_retaliation.json` | 阶段3: 报复部队说明 | `Stage==Confrontation` |
| `crime_witness_silence.json` | 目击者收买/吓唬 | 存在未封口 notable 目击者 |

---

## 四、与 v2 设计的完整对照

以下逐条核对 v2 的每个体验点是否在新方案中覆盖。✅ = 覆盖，⚠️ = 部分覆盖需补充。

### 目击后果分流（v2 第零节）

| v2 体验 | 新方案实现 | 状态 |
|---------|-----------|------|
| 偷窃时检测目击者 | `StealManager.GetWitnesses`（🛞 已有）→ 写入 `WorldEvent.WitnessHeroIds + TemplateWitness` | ✅ |
| 有人目击 → ThiefHeroId 当场确定 | `WorldEvent.SuspectHeroId = Hero.MainHero` 直接写入，WasWitnessed → 跳阶段 | ✅ |
| 被当场抓住 → 当场对峙(mission内) | `InteractionMissionView` 目击后弹出即时对话（🛞 复用 DialogueInjector） | ⚠️ |
| 认错赔钱 → Resolved | 对峙对话中 `PayOnTheSpotIntent` | ⚠️ |
| 打翻村民逃跑 → 直接进阶段3 | 对峙对话选"动手" → `WorldEvent.Stage = Confrontation` | ⚠️ |
| 被村民制服 → 惩罚 cutscene | 复用 `scn_execution_notification` 场景模板，替换角色为玩家+村长 | ✅ |
| 没被当场抓住（跑掉了）→ 直接进阶段2 | `WorldEvent.Stage = Active`，`SuspectHeroId = 玩家` | ✅ |
| 没人目击 → 完整调查流程 | `WorldEvent.Stage = Dormant → Emerging → Active` 正常流转 | ✅ |

结论："当场抓住"的 mission 内即时事件需要在 `InteractionMissionView` 里加一段目击后对话逻辑。其余流转完全由 `WorldEvent.Stage` 状态机驱动。

### 三阶段 Issue-Quest 链（v2 第二节）

| v2 体验 | 新方案实现 | 状态 |
|---------|-----------|------|
| Stage 1 Discovery: 蓝色 !，调查 Quest | `CommissionHubIssue`（🛞 已有）+ `WorldEvent.Stage == Emerging` → 注册 Issue | ✅ |
| 玩家接调查 Quest → 处理证人 → 汇报 | `InvestigateIntent` → 对话流（🛞 DialogueInjector JSON） | ✅ |
| 玩家不接 → AI 自动每日掷骰推进 | `WorldEventStore.ProcessDaily` 推进 `PublicAwareness` | ✅ |
| 7 天冷案 → 草草结案 | `ProcessDaily` 检查超时 → `Stage = Unsolved` | ✅ |
| Stage 2 SuspectIdentified: 黄色 !，追捕 Quest | `WorldEvent.Stage == Active` → `CommissionHubIssue` + `CommissionQuest(BountyHunt)`（🛞 已有） | ✅ |
| 嫌犯=玩家 → 接不了 Quest，替代选项 | `PayRestitutionIntent` / `CharmDefenseIntent` / `ThreatIntent` | ✅ |
| 嫌犯≠玩家 → 正常追捕 Quest | `CommissionQuest(Category=BountyHunt, Target=SuspectHero)`（🛞 已有轮子） | ✅ |
| 追捕 Quest 的活捉机制 | `TryKnockoutAgent` + 倒地【俘虏】键 + `TakePrisonerAction`（🛞 wheels.md 已登记） | ✅ |
| Stage 3 Retaliation: 红色 !，报复 Quest | `WorldEvent.Stage == Confrontation` → `CommissionQuest` + 报复部队 spawn | ✅ |
| 嫌犯=玩家 → 报复部队追玩家 | `SpawnPursuerParty`（🛞 已有！`CommissionQuest` 的 DecoyMission 模式） | ✅ |
| 嫌犯≠玩家 → 带队报复 | `CommissionQuest(Category=VillageDefense 变体)` | ✅ |
| 经济消耗战（打赢不结案 + 经费递减） | `WorldEvent.RetaliationBudget` 每次扣减 + `PermanentEnemy` | ✅ |
| 被俘惩罚 cutscene | 复用已有过场动画系统（🛞 vanilla_cutscenes 参考） | ⚠️ |

### 路径 A：栽赃嫁祸（v2 第一节）

| v2 体验 | 新方案实现 | 状态 |
|---------|-----------|------|
| 栽赃候选名单由 PlayerTheftLedger 生成 | `PlayerTheftLedger.GetFrameableTargets()`（🛞 PlayerTheftLedger 新建） | ✅ |
| 目标 DC 表（强盗40/流浪汉35/村民55/商人70/领主85） | `FrameSuspectIntent.ComputeBaseDC(target)` 方法，按目标身份自动计算 | ✅ |
| [出示证物] 道具加成 +20 | 子选项中 `CanShowEvidence=true` → `playerPower += 20` | ✅ |
| 道具不消耗（村长只看一眼） | 出示动作不调用 TransferItems，只做判定 | ✅ |
| fail forward: 2次失败 → 按玩家身份分叉 | `FrameSuspectIntent.OnFail` 累加 `_failCount`，到达2次 → 分支 | ✅ |
| 大人物第二道坎（商人/领主） | DC过了→检查 `IsPowerful(target)` → `NeedsPush` 子状态 | ✅ |
| Charm 激将 / Roguery 恐吓二次检定 | `PushAuthorityIntent`（Charm）/ `IntimidateAuthorityIntent`（Roguery） | ✅ |
| 栽赃强盗→零后果（不出狱复仇） | 强盗无 HeroId → 🛞 `HeroNemesisTracker` 不记录 | ✅ |
| 栽赃具体人→出狱复仇 | 🛞 `QuestConsequenceResolver` → `HeroNemesisTracker.CreateRecord` | ✅ |

### 路径 B：Charm/赔钱/威胁（v2 第一节）

| v2 体验 | 新方案实现 | 状态 |
|---------|-----------|------|
| Charm 辩护（每案仅一次） | `CharmDefenseIntent` → `WorldEvent.CharmReprieveUsed` 守卫 | ✅ |
| 成功→嫌犯降级→回阶段1 | `SuspectHeroId = null`，`PublicAwareness = 0.5`，`Stage = Emerging` | ✅ |
| 失败→Trust -10→进阶段3 | 🛞 `TrustSystem.AddTrust(-10)`，`Stage = Confrontation` | ✅ |
| 赔钱消灾（×3 动物价值） | `PayRestitutionIntent` → 🛞 `TransferGold(玩家→村长, ×3)` | ✅ |
| 钱不够选项灰掉 | `Evaluate` 返回 `Grey("钱不够")` — 🛞 IntentBase 标准模式 | ✅ |
| 威胁（Roguery 检定） | `ThreatIntent` → 成功=恶名+1, Trust暴跌, Resolved | ✅ |

### 路径 C：报复部队（v2 第一节+第三节）

| v2 体验 | 新方案实现 | 状态 |
|---------|-----------|------|
| 报复部队在大地图追猎玩家 | 🛞 `CommissionQuest.SpawnPursuerParty` → `SetPartyAiAction.GetActionForEngagingParty` | ✅ |
| 部队命名 "{village}的复仇队" | 🛞 `V.SetPartyName` 模板 | ✅ |
| 打 → 赢不结案，恶名+2 | `OnRetaliationPartyDefeated` → 不调 `TransitionStage`，`InfamySystem.AddInfamy(2)` | ✅ |
| 赢后下一波更强更贵 | `RetaliationWaveCount++` → `GetWaveCost` 递增 | ✅ |
| 村庄金库见底停派 + PermanentEnemy | `RetaliationBudget` 扣到不够 → `PermanentEnemy = true` | ✅ |
| 投降/战败 → 被俘带回 → cutscene | 复用 `scn_execution_notification` 场景模板 | ✅ |
| 不打·和解（Charm/Roguery 劝说） | `SettleIntent`（Charm/Roguery 检定，愤怒中成功率更低） | ⚠️ |
| 不打·逃避（跑赢倒计时 15 天） | 部队 15 天超时自散（`CheckRetaliationTimeout`） | ✅ |
| 逃避代价：Trust -30, 恶名+3 | `OnRetaliationTimeout` → `TrustSystem.AddTrust(-30)`, `InfamySystem.AddInfamy(3)` | ✅ |

### 汇总

| 类别 | 数量 |
|------|------|
| ✅ 完全覆盖 | 40 项 |
| ⚠️ 部分覆盖（核心机制有，细节待开发） | 6 项 |
| ❌ 完全缺失 | 0 项 |

⚠️ 的 6 项全部属于已有轮子支撑、需开发但无架构风险的增量：
- "当面对峙"、"认错赔钱"、"打翻逃跑" — `InteractionMissionView` 目击对话扩展
- "被俘 cutscene" — 🛞 已有 `vanilla_cutscenes` 模板
- "和解劝说" — `SettleIntent`，🛞 `IntentBase` 标准模式
- "讨价还价/威胁加成" — 数值微调

---

## 五、LLM 在这一架构中的角色

在通用引擎架构中，LLM 的角色不是"替代系统"，而是**替代内容生成和判断逻辑**：

### LLM 不替代的部分（系统仍然需要）

- `WorldEvent` 数据模型和持久化
- 目击者检测（🛞 `StealManager.GetWitnesses`，依赖 Agent 位置和视线）
- 认知传播（🛞 `NewsSpreadSystem`，已有完整引擎）
- 态度系统的参数框架（人格/关系/利益的权重公式）
- `AgentControlHelper` 资源操作（🛞 铁律 4）
- Issue/Quest 的注册和生命周期管理（🛞 `CommissionHubIssue` / `CommissionQuest`）
- 报复部队的 AI 和战斗结算（🛞 `SpawnPursuerParty` + `SetPartyAiAction`）

### LLM 替代的部分

| 当前手写 | LLM 替代后 |
|---------|-----------|
| 对话树（手写分支条件） | LLM 接收 `{npc_stance, fact_summary, player_skills}` → 生成对话 + 选项 |
| 固定选项文案 | LLM 根据 NPC 人格生成符合其说话风格的选项文本 |
| Narrative.csv 模板填充 | LLM 生成叙事文本（暗探报告/酒馆传闻/村长台词） |
| 硬编码的技能 DC | LLM 根据情境判断难度（"这个村长特别顽固，Charm DC 应该高"） |
| 固定的事件后果 | LLM 提出 2-3 个合理后果，系统选择最符合游戏平衡的那个 |

### 关键架构决策：LLM 是"顾问"不是"引擎"

```
                    ┌─────────────┐
                    │   LLM 顾问   │  ← 生成文本、提议选项、判断难度
                    └──────┬──────┘
                           │ JSON 响应
                           ▼
┌──────────────────────────────────────────────────────┐
│                  确定性引擎                           │
│  · WorldEvent 状态机                                   │
│  · 态度计算（参数化公式）                              │
│  · 资源操作（AgentControlHelper）— 🛞 铁律 4          │
│  · Quest 生命周期管理 — 🛞 CommissionQuest            │
│  · 战斗/报复 AI — 🛞 SetPartyAiAction                 │
│                                                      │
│  引擎接收 LLM 的提议，但：                              │
│  · 校验 JSON 完整性（null guard）— 🛞 铁律 2           │
│  · 校验资源可行性（钱够不够？人是否存在？）              │
│  · 校验游戏平衡（不会让 DC 离谱）                      │
│  · LLM 不可用时 → 降级到确定性公式 — 🛞 铁律 1          │
└──────────────────────────────────────────────────────┘
```

**这恰好符合 CLAUDE.md 的铁律 1（LLM 不可用游戏不能崩）和铁律 2（LLM JSON 不可信任）。**

---

## 六、DailyTick 阶段推进（中枢神经）

```csharp
// WorldEventStore.ProcessDaily() 中的核心逻辑
foreach (var evt in _allEvents.Where(e => e.Stage != EventStage.Resolved && e.Stage != EventStage.Unsolved))
{
    switch (evt.Stage)
    {
        case EventStage.Dormant:
            // 次日村民发现
            if (DaysSince(evt.OccurredDay) >= 1f)
            {
                evt.Stage = EventStage.Emerging;
                evt.PublicAwareness = 0.1f;
                // → 下次玩家进村，CommissionHubIssue 注册，! 出现
            }
            break;

        case EventStage.Emerging:
            // 喂给 NewsSpreadSystem（仅一次）— 🛞 已有
            if (!evt.WasBroadcast)
            {
                NewsSpreadSystem.Instance.BroadcastEvent(evt);
                evt.WasBroadcast = true;
            }
            // 每日推进 PublicAwareness
            evt.PublicAwareness += GetDailySpreadRate(evt);
            // Awareness >= 1.0 且 SuspectHeroId 已确定 → 进 Active
            if (evt.PublicAwareness >= 1.0f && evt.SuspectHeroId != null)
                evt.Stage = EventStage.Active;
            // 7 天超时 → 不了了之
            if (DaysSince(evt.OccurredDay) > 7 && evt.PublicAwareness < 1.0f)
                evt.Stage = EventStage.Unsolved;
            break;

        case EventStage.Active:
            // 悬赏期限到 → 报复
            float deadline = evt.SuspectHeroId == Hero.MainHero.StringId ? 10f : 15f;
            if (DaysSince(stageEnteredDay) > deadline)
            {
                evt.Stage = EventStage.Confrontation;
                SpawnRetaliationParty(evt); // 🛞 复用 SpawnPursuerParty
            }
            break;

        case EventStage.Confrontation:
            // 报复部队超时 / 经费耗尽 → 结案
            if (evt.RetaliationBudget <= 0 || DaysSince(evt.RetaliationSpawnDay) > 15f)
                evt.Stage = EventStage.Resolved;
            break;
    }
}
```

**Issue 注册（玩家进村时）**— 🛞 扩展 `CommissionIssueBehavior.OnSettlementEntered`：

```csharp
var activeEvent = WorldEventStore.FindActive(settlement.StringId);
if (activeEvent != null && activeEvent.Stage != EventStage.Resolved && activeEvent.Stage != EventStage.Unsolved)
{
    var headman = settlement.Notables.FirstOrDefault(n => n.Occupation == Occupation.Headman);
    if (headman != null && headman.Issue == null)
    {
        var issue = new CommissionHubIssue(headman);  // 🛞 复用已有！
        Campaign.Current.IssueManager.AddPotentialIssueData(headman, ...);
    }
}
```

---

## 七、同村后续偷窃 + 村庄警觉

同一村庄可能被玩家多次偷窃。规则：

| 场景 | 行为 |
|------|------|
| 该村有**活跃案件**（`Stage != Resolved`） | **合并**：新偷窃叠加进现有案件（`Quantity += count`）。调查进度**不重置**——村民已经在查了 |
| 该村前一案件**已 Resolved 或 Unsolved** | **开新案**。但如果是因为玩家被 Resolved → `_villageAlertFlags[villageId] = true`，新案 `PublicAwareness` 起始 +0.3、村民对玩家初始态度 cold。Unsolved 不触发警觉 |
| 该村从未被偷过 | **开新案**，正常从 0 起步 |

**村庄警觉标记**（跨案件持久化）：
- 任意案件 Resolved 且嫌犯=玩家（赔钱/威胁成功/被报复）→ `_villageAlertFlags[villageId] = true`
- 案件 Unsolved（没查到是谁）→ 不触发警觉
- 警觉效果：下次该村被偷 → 新案 `PublicAwareness` 起始 +0.3、村民对玩家初始态度 cold

---

## 八、实施路线图

### Phase 1：数据层统一
- 新建 `WorldEvent.cs`（统一模型 + `WorldEventStore` + `PlayerTheftLedger` + `EventConfig`）
- `NewsSpreadSystem.BroadcastEvent` 改为直接接收 `WorldEvent`
- `MyBehavior.DailyTick` + `SyncData`（持久化 `List<WorldEvent>` JSON）
- **验证**: 偷羊 → `WorldEvent` 创建 → 🛞 `NewsSpreadSystem` 自动传播 → 村长 `KnownEvents` 有事件

### Phase 2：态度 + 行动
- 新建 `AttitudeSystem.cs`（`NpcStance` + `ComputeStance` + `ResponseGenerator`）
- 🛞 `SingNpcMemorySystem` 适配
- **验证**: `PerceivedSeverity` 变化 → `ResponseGenerator` 自动产生不同行动集合

### Phase 3：玩家介入
- 新建 `AccountabilityIntents.cs`（7 个 Intent，🛞 `IntentBase` 子类——逻辑层）
- 🛞 `IntentContext` 加 `ActiveEvent` 字段
- 🛞 `IntentRegistry` 注册
- 🛞 `DialogueInjector.ExecuteAction` 扩展 `"INTENT:xxx"` 委托
- 新建对话 JSON 文件（表现层）
- **验证**: 对话 JSON 注入前跑 Intent.Evaluate → 不可见选项被过滤 → 玩家只看到当下有资格的选项

### Phase 4：Quest 追责方向
- 🛞 `CommissionData` 加 `IsAccountabilityQuest` + `FactId`
- 🛞 `CommissionGenerator` 从 `WorldEvent` 生成追责 Quest
- **验证**: 不赔钱 → 报复部队自动 spawn → 大地图追击 → 战斗结算

### Phase 5：LLM 接入（顾问模式）
- LLM 生成对话文本 / 选项文案 / 叙事文本
- LLM 提议 DC / 后果选项 → 引擎校验后执行
- 🛞 `IsLLMReady` 关 → 回落确定性公式（铁律 1）

### Phase 6：扩展新犯罪类型（验证通用性）
- 用 `EventConfig` 注册 Poaching（盗猎）、Smuggling（走私）
- 预期：每种新类型 < 50 行配置，零新 C# 类

---

## 九、文件变更

### 新增

| 文件 | 内容 | 行数 |
|------|------|------|
| `Stealth/WorldEvent.cs` | `WorldEvent` 数据模型 + `EventStage` 枚举 + `WorldEventStore` 管理器 + `PlayerTheftLedger` + `EventConfig`/`EventTemplates` | ~300 |
| `Quests/WorldEvents/AttitudeSystem.cs` | `NpcStance` + `ComputeStance` + `ResponseGenerator` | ~150 |
| `Interaction/Intents/AccountabilityIntents.cs` | 7 个追责 Intent（🛞 `IntentBase` 子类，逻辑层） | ~250 |
| `ModuleData/DesignData/Dialogues/crime_discovery.json` | 阶段1: 调查 Quest 接取 | ~50 |
| `ModuleData/DesignData/Dialogues/crime_report.json` | 阶段1: 汇报调查+栽赃 | ~100 |
| `ModuleData/DesignData/Dialogues/crime_confront_player.json` | 阶段2: 嫌犯=玩家 | ~70 |
| `ModuleData/DesignData/Dialogues/crime_bounty_offer.json` | 阶段2: 嫌犯≠玩家 | ~30 |
| `ModuleData/DesignData/Dialogues/crime_retaliation.json` | 阶段3: 报复部队 | ~30 |
| `ModuleData/DesignData/Dialogues/crime_witness_silence.json` | 目击者收买/吓唬 | ~30 |

### 修改

| 文件 | 改动 | 行数 |
|------|------|------|
| `Social/SocialEventManager.cs` | `BroadcastEvent` 改为直接接收 `WorldEvent`（原 `SocialEvent`），后续弃用 `SocialEvent` 类 | ~10 |
| `Interaction/InteractionMissionView.cs` | `TryStealAnimal` 末尾加 `WorldEvent` 创建 + 🛞 `PlayerTheftLedger` 记账 + 目击两档记录 | ~40 |
| `Core/MyBehavior.cs` | `DailyTick` 加 `WorldEventStore.ProcessDaily()`；`SyncData` 加序列化 | ~15 |
| `Interaction/Intents/IntentContext.cs` | 加 `ActiveEvent` 字段（可空 WorldEvent） | ~10 |
| `Interaction/Intents/IntentRegistry.cs` | `RegisterDefaults` 加注册 7 个追责 Intent | ~7 |
| `Memory/SingNpcMemorySystem.cs` | `KnownEvents` 适配 `WorldEvent.EventId` | ~10 |
| `Quests/Commissions/CommissionData.cs` | 加 `IsAccountabilityQuest` / `FactId` | ~5 |
| `Quests/Commissions/CommissionGenerator.cs` | 从 `WorldEvent` 生成追责 Quest | ~40 |
| `Interaction/DialogueInjector.cs` | `ExecuteAction` 扩展支持追责动作（`FRAME_SUSPECT` / `PAY_RESTITUTION` / `SKILL_CHECK` 等） | ~60 |

### 不动（已有系统继续独立运行）

| 文件 | 原因 |
|------|------|
| `WorldEventSimulator.cs` | AI 模拟事件生成，不动（后续 PR 迁移） |
| `WorldEventDirector.cs` | 玩家发现事件推送，不动（后续扩展推送 WorldEvent 通知） |
| `WorldEventDatabase.cs` | 改为 `WorldEventStore` 薄壳后废弃 |
| `SocialEventManager.cs` 核心逻辑 | 🛞 `NewsSpreadSystem.BroadcastEvent` 传播引擎，不动 |
| `SingNpcMemorySystem.cs` 核心逻辑 | 🛞 KnownEvents 存储，不动 |

**总新增代码**: ~1,200 行（含 7 个 JSON 对话文件），对比 v2 原设计的 ~2,000+ 行 C# + 手写对话树。

---

## 十、验证方案

### 测试 A: 栽赃嫁祸
1. 偷羊（无目击）→ 回村 → 接调查
2. 向村长汇报 → 选"是强盗干的" → DC 40 裸过
3. → 嫌犯=强盗头子 → 接追捕 Quest → 清藏身处 → 报酬 + Trust+10
4. 重来一次 → 选"是{账本Hero}干的" → 出示证物 → 检定 → 嫌犯锁定
5. 如果指认商人/领主 → 出现第二道坎（激将/恐吓）

### 测试 B: 被查出来 → 摆平
1. 偷羊（有人目击）→ 不封口 → 调查锁定玩家
2. 回村对话 → 冷脸 → Charm 辩护（成功回退/失败进阶段3）
3. 或者直接赔钱了事（钱够/不够两种）

### 测试 C: 不理会 → 报复
1. 偷羊 → 被锁定 → 不赔钱不辩护
2. 报复部队 spawn → 大地图追击
3. 战斗: 打赢 → 下一波更强更多（经费递减）
4. 打到村庄没钱 → 永久敌对

### 测试 D: 跨案件
1. 偷羊被 Resolved（赔钱）→ 再偷同一村庄
2. 验证: 警觉标记 → 调查加速 → 村长初始 cold

### 测试 E: 存档读档
1. 各种状态 → 存档退出 → 读档
2. 验证: `WorldEvent` + `PlayerTheftLedger` + 态度全部恢复

### 测试 F: LLM 不可用
1. `IsLLMReady = false`
2. 所有 Intent 走 🛞 `SingleRollResolver.Compute`
3. 对话文案走 CSV 兜底

---

## 十一、与当前设计的对比

| 维度 | 当前设计（v2） | 通用引擎（本文） |
|------|--------------|----------------|
| 新犯罪类型成本 | ~1285 行设计 + 多个新类 | 一个 `EventConfig` 静态字段 |
| 阶段迁移 | 手写迁移表（6 个 if 分支） | 行动集合随态度自然变化 |
| NPC 反应 | 手写对话树分支 | 态度系统实时计算 |
| 玩家选项 | 手写枚举（4-6 个） | 🛞 `IntentBase.Evaluate` 自动解锁 |
| 证据系统 | `EvidencePointer`（耦合到 VillageTheftCase） | `EvidencePointer`（通用，属于 WorldEvent） |
| 报复机制 | 手写报复部队 spawn + 经济模型 | 🛞 复用 `SpawnPursuerParty` + 经费取自权威人物金库 |
| 叙事文本 | 手写 Narrative.csv 条目 | 🛞 模板 + 占位符 + LLM 填充 |
| Issue/Quest | 3 个新 Issue 类 + 3 个新 Quest 类 | 🛞 复用 `CommissionHubIssue` + `CommissionQuest`（通用类零改动） |
| 对话注入 | 手写 `DialogFlow` 链式调用 | 🛞 JSON 驱动 `DialogueInjector` |
| 玩家干预 | 手写对话选项分支 | 🛞 `IntentBase` 注册式（Evaluate/OnSuccess/OnFail） |
| 检定公式 | 手写 DC 计算 | 🛞 `SingleRollResolver.Compute`（复用已有） |

---

## 十二、开放问题

1. **态度系统的参数权重如何校准？** 需要大量 playtest 才能调出"像 KCD2"的感觉。初始值可以用启发式估计，后续迭代调整。

2. **同 victim 多事实聚合？** 村民不会为每只被偷的羊单独开一个案子。当前设计有"合并偷窃"逻辑。通用层需要更通用的"同 victim 多事件聚合"机制。

3. **LLM 生成的选项如何保证游戏性？** 不能让 LLM 每次都生成 20 个选项淹没玩家。需要设定"每次最多 4-5 个选项"的约束 + 选项优先级排序（由引擎层做，不是 LLM）。

4. **跟现有 Issue/Quest 架构的兼容？** 🛞 `CommissionHubIssue` + `CommissionQuest` 已经过验证。`IsAccountabilityQuest` 标志是轻量扩展，不影响现有委托生成。

5. **`EventConfig` 的复杂度边界？** 如果某种新犯罪类型的机制差异太大（如"暗杀需要隐身潜入" vs "偷羊只是靠近按 F"），通用 Quest 模板可能不够。需要定义清楚"通用层覆盖什么，特殊层覆盖什么"的边界——底线是：只要玩家介入方式走对话+检定+Quest，就通用；如果涉及新 Mission 机制（如潜行暗杀），Mission 层的行为还是需要单独开发。

6. **"当面对峙"的 mission 内即时对话**：目击者当场喊叫 → 周围 NPC 围观 → 玩家即时回应。这需要在 `InteractionMissionView` 中新增一段逻辑，但对话注入仍走 🛞 `DialogueInjector`。

---

## 十三、参考资料

- 当前偷窃后续设计：[village-theft-consequences-v2.md](village-theft-consequences-v2.md)
- 配套实现细节：[crime_general.md](crime_general.md)
- 已造轮子：[rules/wheels.md](rules/wheels.md)
- 设计哲学：[rules/design-philosophy.md](rules/design-philosophy.md)
- 叙事铁律：[rules/narrative-design.md](rules/narrative-design.md)
- 原版任务架构：[../Knowledge/原版骑砍2任务系统分析.md](../Knowledge/原版骑砍2任务系统分析.md)
- 原版对话流引擎：[../Knowledge/原版对话流引擎逆向分析.md](../Knowledge/原版对话流引擎逆向分析.md)
- 存档机制：[../Knowledge/存档机制深度解析.md](../Knowledge/存档机制深度解析.md)
- 击晕机制：[../Knowledge/击晕机制_引擎能力与实现踩坑.md](../Knowledge/击晕机制_引擎能力与实现踩坑.md)
- KCD2 系统涌现设计参考：KCD2 的犯罪系统就是一个通用引擎——偷窃/杀人/侵入的"被发现→调查→惩罚"走同一套管线，区别只在严重度参数，不在代码分支

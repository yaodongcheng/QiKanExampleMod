Ready for review
Select text to add comments on the plan
犯罪→反应 统一引擎 — 重构方案（基于 crime-consequence-composable.md 框架）
设计基础: [crime-consequence-composable.md](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\plans\crime-consequence-composable.md) 目标体验: 完整达到 [village-theft-consequences-v2.md](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\plans\village-theft-consequences-v2.md) 每个环节 核心原则: 合并相似类，彻底重构，不是建桥接

三层架构（已有轮子的定位）
第 1 层: WorldEvent ─── 1 份，存两套信息
    ┌─ 客观真实（系统知道，NPC 不知道）:
    │    ThiefHeroId = "player123"        ← 真凶，偷窃当下写入，不可变
    │
    └─ 公共认知（NPC 可见，随调查演进）:
         SuspectHeroId = null             ← 嫌犯（null=未知, 非null=已锁定）
                                             置信度由 PublicAwareness 表达(0.5=怀疑, 1.0=确认)
         PublicAwareness = 0.0            ← "出事了"的公众知晓度 + 调查进度
                                             0.1=被发现, 0.5=有怀疑对象, 1.0=嫌犯确认
         需要合并的: SocialEvent + WorldEventData + 待建的 WorldFact
         ↓ NewsSpreadSystem 传播（只传播"出事了"，不传播真凶）

第 2 层: KnownEvent ─── 每 NPC 1 份，NPC 的"在乎程度"
         "村长听说牲口被偷了，严重度 60，消息是二手"
         已有且完好: SingNpcMemorySystem.KnownEvents（PerceivedSeverity + DecayCounter）
         只存"知不知道出事了 + 有多在乎"，不存"谁干的"
         → NPC 要查"谁干的"需要通过 WorldEvent.SuspectHeroId（公共认知字段）

第 3 层: NpcStance ─── 态度→行动（实时计算）
         "村长查到嫌犯是 XX 后: outrage=0.7, fear=0.2 → 悬赏"
         新增: AttitudeSystem.ComputeStance + ResponseGenerator
关键区分：

概念	存在哪里	例
事实（谁偷的）	WorldEvent.ThiefHeroId	"player123"（系统知道，NPC 不一定）
公共认知（嫌犯是谁）	WorldEvent.SuspectHeroId / IdentifiedSuspectId	null → "player123"（调查后填入，可能错）
公众知道"出事了"的程度	WorldEvent.PublicAwareness	0.0 → 1.0（影响 Issue 是否出现）
NPC 对"这件事"有多在乎	KnownEvent.PerceivedSeverity	60（影响 NPC 会不会行动）
NPC 基于嫌犯+人格的态度	NpcStance（实时计算）	outrage=0.7, fear=0.2
这个区分解释了 v2 的 Stage 1 → Stage 2 迁移：

Stage 1（Discovery）：PublicAwareness > 0.1 → NPC 知道出事了 → Issue ! 出现。但 SuspectHeroId == null → 调查 Quest 的目标是"查清是谁"
Stage 2（SuspectIdentified）：SuspectHeroId 被填入 → NPC 知道嫌犯了 → 态度从"关切"变成"指向性愤怒" → 行动从"要求调查"变成"悬赏/追捕/报复"
有些委托一发生嫌犯就公开（如当场目击）：此时 SuspectHeroId 在创建 WorldEvent 时就填入，跳过 Stage 1。

一、核心决策：一个唯一的 WorldEvent 模型
所有"世界上发生的事"用一个模型。WorldEventData 的 AI 专用字段（GeneratedPartyId/DayLimit/EscalationCount/ConspiracyId/AuxiliaryPartyIds）作为可空字段纳入统一模型——玩家犯罪时这些字段为 null，AI 事件时填入。

统一模型:
  WorldEvent ─── 所有事件（玩家犯罪 + AI 模拟 + 社交丑闻）都用这一个
     │
     ├──→ NewsSpreadSystem (传播引擎)
     │       └──→ KnownEvent (每 NPC 的感知，已有不动)
     │
     ├──→ WorldEventStore (持久化 + DailyTick 推进)
     │
     ├──→ CommissionQuest (委托生成，现有 BountyHunt + 新增追责方向)
     │
     └──→ WorldEventSimulator (AI 事件生成，暂用 WorldEventData，后续迁移)
迁移策略：

组件	本次	后续
玩家犯罪行为	✅ 直接用 WorldEvent	—
WorldEventData	保留不动	后续 PR 迁移到 WorldEvent
WorldEventSimulator	保留不动	后续 PR 改为产出 WorldEvent
WorldEventDatabase	保留不动	后续 PR 改为 WorldEventStore 薄壳
SocialEvent（NewsSpreadSystem）	BroadcastEvent 扩展接受 WorldEvent	后续 PR 弃用 SocialEvent 类
统一的 WorldEvent（所有事件用这一个模型）
[Serializable]
public class WorldEvent
{
    // ═══ 身份 ═══
    public string EventId;
    public EventCategory Category;     // Crime / Social / World
    public EventType Type;             // Theft_Animal, Murder, Divorce, BanditRaid...
    public int Severity;               // 0-100

    // ═══ 客观真实（发生时写入，不可变） ═══
    public string InitiatorId;         // 作案者
    public string TargetHeroId;        // 受害者 hero（null = 村庄/抽象实体）
    public string TargetSettlementId;
    public string TargetItemId;
    public int Quantity;
    public float OccurredDay;

    // ═══ 目击 ═══
    public List<string> WitnessHeroIds;
    public Dictionary<string, int> TemplateWitness;
    public bool WitnessesSilenced;

    // ═══ 公共认知（随调查演进，NPC 可见） ═══
    public float PublicAwareness;      // 0→1（0.1=被发现, 0.5=有嫌犯, 1.0=锁定）
    public string SuspectHeroId;       // 嫌犯（null=未知）

    // ═══ 玩家介入 ═══
    public bool CharmReprieveUsed;

    // ═══ 报复 ═══
    public int RetaliationBudget;
    public int RetaliationWaveCount;
    public string RetaliationPartyId;
    public bool PermanentEnemy;

    // ═══ AI 模拟事件专用（玩家犯罪时 null/default） ═══
    public string GeneratedPartyId;    // MobileParty.StringId
    public float? DayLimit;
    public int EscalationCount;
    public string ConspiracyId;
    public string HiddenMastermindId;
    public Dictionary<string, string> AuxiliaryPartyIds;

    // ═══ 状态 ═══
    public EventStage Stage;           // Dormant → Emerging → Active → Confrontation → Resolved
    public string ResolvedBy;
    public bool WasBroadcast;
    public float LastUpdateDay;
}
二、六层框架的具体实现
第 1 层：WorldEventStore（统一事件存储 + 持久化）
public static class WorldEventStore
{
    private static List<WorldEvent> _allEvents;

    // CRUD
    public static void Add(WorldEvent evt);
    public static WorldEvent Find(string eventId);
    public static WorldEvent FindActive(string settlementId);
    public static List<WorldEvent> GetActiveEventsNear(Settlement s, float maxDist);

    // 每日推进
    public static void ProcessDaily();

    // 持久化
    public static string Serialize();
    public static void Deserialize(string json);
}
替代: WorldEventDatabase + WorldFactStore（两个合成一个）

第 2 层：NewsSpreadSystem（传播引擎，不动架构）
已有且足够好。BroadcastEvent → ProcessHeroRecursively → ReceiveNews → KnownEvent 的递归传播链路完全复用。

需要改的只有：BroadcastEvent 原来接收 SocialEvent，现在接收 WorldEvent（合并后）。

// 改动点：BroadcastEvent(WorldEvent evt)
// 原逻辑不变，只是入参类型变了
public void BroadcastEvent(WorldEvent evt)
{
    // 1. 注册到全局库
    // 2. 初始传播源（受害者/目击者/领导）
    // 3. ProcessHeroRecursively 递归传播
    // 4. 生成 SpreadReport + ScreenPlayOutline
}
第 3 层：AttitudeSystem（新增，轻量）
从 SingNpcMemorySystem 中提取，成为一个独立的静态计算方法：

public struct NpcStance
{
    public float Outrage;          // 0→1 "这事不能忍" — 驱动悬赏/报复
    public float Fear;             // 0→1 "惹不起" — 抑制行动，驱动上报/退缩
    public float Sympathy;         // -1→1 负=同情作案者(宽容/包庇), 正=同情受害者(加码追责)
    public float SelfInterest;     // 0→1 "我能得什么好处" — 驱动索贿/敲诈封口
    public Attitude TowardActor;   // 综合态度
}
四个维度如何产生不同行为：

参数组合	产生的行为	v2 体验映射
Outrage↑ Fear↓	悬赏/报复	Stage 2/3 追捕/报复部队
Outrage↑ Fear↑	上报领主	"大人物第二道坎"：想动不敢动
Outrage↓ Sympathy→作案者(-)	宽容/减罚/包庇	关系好的 NPC 私下警告，不报案
Sympathy→受害者(+)↑	加码追责	受害者是熟人 → 悬赏翻倍
SelfInterest↑ Outrage↓	索贿封口	"给钱我就当没看见"（不同于赔钱——私下交易）
全低	冷漠	冷案："查不出来，算了"
AttitudeSystem（第 3 层：态度计算）
public static class AttitudeSystem
{
    public static NpcStance ComputeStance(Hero npc, WorldEvent evt)
    {
        var stance = new NpcStance();

        // 1. 基础：从 KnownEvent.PerceivedSeverity 出发
        var knownEvent = AllNpcMemoryManager.GetMemory(npc.StringId)
            ?.KnownEvents.FirstOrDefault(e => e.EventId == evt.EventId);
        float perceivedSeverity = knownEvent?.PerceivedSeverity ?? 0;

        // 2. 人格修正
        var profile = AllNpcMemoryManager.GetMemory(npc.StringId)?._profile;
        float honorMod = profile?.PersonalityTraits?.Contains("Honorable") == true ? 0.2f : 0f;
        float mercyMod = profile?.PersonalityTraits?.Contains("Merciful") == true ? -0.15f : 0f;
        float greedyMod = profile?.PersonalityTraits?.Contains("Greedy") == true ? 0.25f : 0f;

        // 3. 关系修正
        float initiatorRelation = npc.GetRelationWith(evt.InitiatorId);
        float victimRelation = npc.GetRelationWith(evt.TargetHeroId);

        // 4. 身份修正
        bool isLocalAuthority = IsAuthority(npc, evt.TargetSettlementId);
        bool initiatorIsPowerful = IsPowerful(evt.InitiatorId);

        // 5. 合成四个维度
        stance.Outrage = Math.Clamp(
            (perceivedSeverity / 100f) + honorMod
            + (isLocalAuthority ? 0.3f : 0f)
            + (victimRelation > 20 ? 0.2f : 0f),
            0f, 1f);

        stance.Fear = Math.Clamp(
            (initiatorIsPowerful ? 0.4f : 0f)
            + (evt.Severity >= EventSeverity.Capital ? 0.3f : 0f),
            0f, 1f);

        // Sympathy: 负=同情作案者(熟人), 正=同情受害者
        stance.Sympathy = Math.Clamp(
            mercyMod * 2f
            + (initiatorRelation > 20 ? -0.3f : 0f)  // 作案者是朋友 → 同情
            + (victimRelation > 20 ? 0.3f : 0f)       // 受害者是朋友 → 同情
            + (initiatorRelation < -20 ? 0.2f : 0f),  // 作案者是仇人 → 不同情
            -1f, 1f);

        // SelfInterest: 贪婪性格 + 有机会敲诈
        stance.SelfInterest = Math.Clamp(
            greedyMod
            + (isLocalAuthority && evt.SuspectHeroId != null ? 0.2f : 0f)
            + (stance.Outrage < 0.4f ? 0.15f : 0f),  // 不太愤怒 → 更容易动私心
            0f, 1f);

        stance.TowardActor = ComputeAttitude(stance);
        return stance;
    }
}
        {
            > 0.7f => Attitude.Vengeful,
            > 0.5f => Attitude.Angry,
            > 0.3f => Attitude.Disapproving,
            > 0.1f => Attitude.Neutral,
            _ => initiatorRelation > 20 ? Attitude.Understanding : Attitude.Neutral
        };

        return stance;
    }
}
ResponseGenerator（第 4 层：态度→行动）
public static class ResponseGenerator
{
    public static List<ResponseAction> GenerateResponses(Hero authority, WorldEvent evt)
    {
        var stance = AttitudeSystem.ComputeStance(authority, evt);
        var actions = new List<ResponseAction>();

        float willAct = Math.Max(0, stance.Outrage - stance.Fear);

        // 🔓 索贿封口 — SelfInterest↑ Outrage↓
        if (stance.SelfInterest > 0.4f && stance.Outrage < 0.5f)
            actions.Add(ResponseAction.ExtortBribe);     // "给钱我就当没看见"

        // 🔓 宽容/包庇 — Sympathy→作案者(-)
        if (stance.Sympathy < -0.3f && stance.Outrage < 0.6f)
            actions.Add(ResponseAction.GoEasy);          // "这次算了，下不为例"

        // 🔓 要求赔偿 — 有点生气，不太怕
        if (stance.Outrage > 0.3f && stance.Fear < 0.7f)
            actions.Add(ResponseAction.DemandRestitution);

        // 🔓 发布悬赏 — 很生气，愿意动，不太怕
        if (stance.Outrage > 0.5f && willAct > 0.3f && stance.Fear < 0.5f)
            actions.Add(ResponseAction.IssueBounty);

        // 🔓 Sympathy→受害者(+) — 加码追责
        if (stance.Sympathy > 0.3f)
            // 所有惩罚类行动的力度翻倍（赔偿×2、赏金×2）

        // 🔓 组织报复 — 非常生气 + 愿意动
        if (stance.Outrage > 0.7f && willAct > 0.5f)
            actions.Add(ResponseAction.LeadRetaliation);

        // 🔓 忍气吞声 — Fear > Outrage
        if (stance.Fear > stance.Outrage)
            actions.Add(ResponseAction.Intimidate);

        // 🔓 上报领主 — 生气但太怕（v2 的"大人物第二道坎"）
        if (stance.Fear > 0.5f && stance.Outrage > 0.5f && willAct < 0.2f)
            actions.Add(ResponseAction.ReportToLord);

        // 🔓 冷漠 — 全低
        if (willAct < 0.15f && stance.SelfInterest < 0.3f && Math.Abs(stance.Sympathy) < 0.2f)
            actions.Add(ResponseAction.Indifferent);     // 冷案

        return actions;
    }
}
v2 体验对标：

"大人物第二道坎" = NeedsPush → 在 Intent 中解锁 激将/恐吓 选项
阶段迁移 = ResponseGenerator.GenerateResponses 的输出随 stance 变化自然演变
阶段1: Outrage 低 → 只有 DemandRestitution
阶段2: Outrage 中 → + IssueBounty
阶段3: Outrage 高 → + LeadRetaliation + DemandRestitution 消失
第 5 层：PlayerIntervention（玩家介入选项）
复用已有 Intent 引擎，新增 7 个 Intent（对应 v2 全部交互）：

// ── 通用追责 Intent（任何犯罪类型复用） ──

// 1. PayRestitutionIntent — 赔钱消灾
//    v2 对应: 阶段2/3 赔钱选项
//    Evaluate: ActiveEvent != null && IsAccused && Gold >= GetRestitutionCost()
//    Cost: 阶段2 = ×3, 阶段3 = ×5 + 罚金
//    OnInstant: TransferGold → Resolve("payment")

// 2. CharmDefenseIntent — 辩护（每案一次）
//    v2 对应: 阶段2 Charm 辩护
//    Evaluate: ActiveEvent != null && IsAccused && !ActiveEvent.CharmReprieveUsed
//    OnSuccess: SuspectHeroId = null, PublicAwareness = 0.5, CharmReprieveUsed = true
//    OnFail: Trust -10, 进 Confrontation

// 3. FrameSuspectIntent — 栽赃
//    v2 对应: 路径 A 栽赃嫁祸
//    Evaluate: ActiveEvent != null && 账本有候选
//    SubOptions 动态: ① "是附近强盗干的" + ② "是{账本Hero}干的"（按 DC 排序）
//    【关键】: 栽赃成功/失败走 v2 的完整逻辑 ——
//      - 目标 DC 表（强盗40 / 流浪汉35 / 村民55 / 商人70 / 领主85）
//      - [出示证物] 道具加成 +20
//      - fail forward: 2次失败 → 按玩家身份分叉
//      - 大人物第二道坎（商人/领主: 信念过了→激将/恐吓二次检定→失败压下案子）

// 4. ThreatIntent — 威胁
//    v2 对应: 阶段2 Roguery 威胁
//    Evaluate: ActiveEvent != null && IsAccused && Roguery >= 50
//    OnSuccess: Trust暴跌, 恶名+1, Resolve("intimidated")
//    OnFail: 直接进 Confrontation

// 5. InvestigateIntent — 接调查任务
//    v2 对应: 阶段1 接 InvestigateVillageTheftQuest
//    Evaluate: ActiveEvent != null && Stage == Emerging

// 6. SilenceWitnessIntent — 收买/吓唬目击者
//    v2 对应: 目击者封口
//    Evaluate: ActiveEvent != null && 存在未封口的 notable 目击者
//    Mission 内: 精确处理 notable 目击者（对话收买/吓唬）
//    Campaign 层: 聚合收买没脸村民（一次 Roguery 检定，WitnessesSilenced = true）

// 7. LeadRetaliationIntent — 带队报复（嫌犯≠自己时）
//    v2 对应: 阶段3 接 LeadRetaliationQuest
//    Evaluate: ActiveEvent != null && Stage == Confrontation && SuspectHeroId != 玩家
v2 的栽赃完整逻辑在 FrameSuspectIntent 中：

public class FrameSuspectIntent : IntentBase
{
    public override Eligibility Evaluate(IntentContext ctx)
    {
        if (ctx.ActiveEvent == null) return Eligibility.Hide();
        if (ctx.ActiveEvent.SuspectHeroId != Hero.MainHero.StringId) return Eligibility.Hide();
        // 检查是否还有证人未封口（如果有的话，栽赃可能被戳穿）
        // ...
        return Eligibility.Show();
    }

    public override void OnSuccess(IntentContext ctx)
    {
        // → 打开子选择（对话中）:
        //   ① "是附近藏身处的强盗干的" (DC 40, 不需证物)
        //   ② "是{账本Hero1}干的" (DC 35-85, 可出示证物)
        //   ...
        // 每个子选项走 v2 的检定模型:
        //   playerPower = Roguery折算 + 道具加成(如果有) + 封口加成(如果有)
        //   playerPower >= DC → 村长信了 → SuspectHeroId = 目标
        //   playerPower < DC → fail forward
    }
}

// FrameSubOption（子选项数据）
public class FrameSubOption
{
    public string TargetId;        // "bandit" 或 heroId
    public string DisplayName;     // "附近藏身处的强盗" / "{Hero.Name}"
    public int BaseDC;             // 40-85（从目标属性自动计算）
    public bool CanShowEvidence;   // 是否可以出示证物（账本有记录 + 背包仍有物品）
    public bool IsPowerful;        // 是否是大人物（触发第二道坎）
}
第 6 层：FactTemplate（配置层）
public static class EventTemplates
{
    public static readonly EventConfig AnimalTheft = new()
    {
        Type = EventType.Theft_Animal,
        Category = EventCategory.Crime,
        DefaultSeverity = EventSeverity.Minor,
        VictimType = VictimType.Settlement,
        AuthorityRole = Occupation.Headman,
        BaseSpreadRate = 0.1f,
        BaseRestitutionMultiplier = 3,
        BaseBountyPerUnit = 50,
        PreferredResponses = { ResponsePattern.DemandRestitution, ResponsePattern.IssueBounty },
    };

    public static readonly EventConfig Murder = new()
    {
        Type = EventType.Murder,
        Category = EventCategory.Crime,
        DefaultSeverity = EventSeverity.Capital,
        VictimType = VictimType.Hero,
        AuthorityRole = Occupation.Lord,
        BaseSpreadRate = 0.5f,
        BaseRestitutionMultiplier = 50,
        BaseBountyPerUnit = 5000,
        PreferredResponses = { ResponsePattern.ReportToLord, ResponsePattern.LeadRetaliation },
    };
}
二点五、Issue → Quest 生成链路（玩家可见的核心管线）
这是从"WorldEvent 数据存在"到"玩家看到 ! 并能接 Quest"的完整链路。

Stage → Issue → Quest 一一对应（通用 Issue 类，不绑定犯罪类型）
三个 Issue 类是通用的——它们读 WorldEvent 的 Stage 和 Category 来决定文本和颜色，不对"偷羊"做任何特殊处理。

WorldEvent.Stage         Issue 类 (通用)          Quest 类型                        ! 颜色
──────────────────────────────────────────────────────────────────────────────────
Dormant                 无                       无                               无
Emerging                CrimeDiscoveryIssue      InvestigateCrimeQuest             蓝色
Active (Suspect=Player) CrimeSuspectIssue        无（对话选项替代）                  黄色
Active (Suspect≠Player) CrimeSuspectIssue        CommissionQuest(BountyHunt)        黄色
Confrontation           CrimeRetaliationIssue    无 / CommissionQuest(报复变体)      红色
Resolved                无                       无                               无
加盗猎只需：EventTemplates.Register(Poaching) + 一份盗猎对话 JSON。三个 Issue 类零改动。

// 通用 Issue 示例：文本从 WorldEvent 数据动态生成
public class CrimeDiscoveryIssue : IssueBase
{
    private WorldEvent _evt;
    
    public override TextObject Title 
        => new TextObject(GetTitleForCategory(_evt.Category));  
        // Theft_Animal → "村庄失窃"  /  Poaching → "领主的猎物被盗"  /  Murder → "命案"
    
    protected override QuestBase GenerateIssueQuest(string questId)
        => new InvestigateCrimeQuest(questId, _evt);  // 通用调查 Quest
}
DailyTick 阶段推进
// WorldEventStore.ProcessDaily() 中的核心逻辑
foreach (var evt in _allEvents.Where(e => e.Stage != EventStage.Resolved))
{
    switch (evt.Stage)
    {
        case EventStage.Dormant:
            // 偷窃后 1-3 天村民发现（取决于 Severity）
            if (DaysSince(evt.OccurredDay) > GetDiscoveryDelay(evt))
            {
                evt.Stage = EventStage.Emerging;
                evt.PublicAwareness = 0.1f;
                // → 下次玩家进村，CommissionHubIssue 注册，! 出现
            }
            break;

        case EventStage.Emerging:
            // 喂给 NewsSpreadSystem（仅一次）
            if (!evt.WasBroadcast)
            {
                NewsSpreadSystem.Instance.BroadcastEvent(evt.ToSocialEvent());
                evt.WasBroadcast = true;
            }
            // 每日推进 PublicAwareness
            evt.PublicAwareness += GetDailySpreadRate(evt);
            // 7 天超时 → 冷案
            if (DaysSince(evt.OccurredDay) > 7 && evt.PublicAwareness < 1.0f)
                evt.Stage = EventStage.Resolved; // 冷案
            break;

        case EventStage.Active:
            // 悬赏期限到 → 报复
            float deadline = evt.SuspectHeroId == Hero.MainHero.StringId ? 10f : 15f;
            if (DaysSince(stageEnteredDay) > deadline)
                evt.Stage = EventStage.Confrontation;
            break;

        case EventStage.Confrontation:
            // 报复部队超时 / 经费耗尽 → 结案
            if (evt.RetaliationBudget <= 0 || DaysSince(evt.RetaliationSpawnDay) > 15f)
                evt.Stage = EventStage.Resolved;
            break;
    }
}
Issue 注册（玩家进村时）
// CommissionIssueBehavior.OnSettlementEntered 扩展
private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
{
    // ... 现有逻辑 ...
    
    // 新增：检查本村是否有活跃 WorldEvent
    var activeEvent = WorldEventStore.FindActive(settlement.StringId);
    if (activeEvent != null && activeEvent.Stage != EventStage.Resolved)
    {
        // 找到权威 NPC（村长/头人）
        var headman = settlement.Notables.FirstOrDefault(n => n.Occupation == Occupation.Headman);
        if (headman != null && headman.Issue == null)
        {
            var issue = new CommissionHubIssue(headman);  // 复用已有！
            Campaign.Current.IssueManager.AddPotentialIssueData(headman, ...);
            // → 村长头上出现 !
        }
    }
}
Quest 生成（玩家接委托时）
// CommissionGenerator.GenerateCommissions 扩展
if (urgentEvent == null)
{
    // 检查是否有从 WorldEvent 传播来的追责事件
    var fact = WorldEventStore.FindActive(hero.CurrentSettlement.StringId);
    if (fact != null)
    {
        var data = new CommissionData
        {
            Category = fact.SuspectHeroId == Hero.MainHero.StringId
                ? CommissionCategory.BountyHunt  // 追捕玩家（报复部队）
                : CommissionCategory.BountyHunt,  // 追捕嫌犯
            IsAccountabilityQuest = true,
            FactId = fact.EventId,
            QuestGiver = hero,
            TargetHero = Hero.Find(fact.SuspectHeroId),
            TimeRemainingHours = (fact.Stage == EventStage.Confrontation ? 15 : 10) * 24f,
        };
        new CommissionQuest($"theft_{fact.EventId}", data).StartQuest();
    }
}
二点六、对话流 JSON 设计（每个 v2 分支的对话结构）
以下用 DialogueInjector 的 JSON 格式，列出每个关键对话场景的 turn 结构。 DialogueInjector 的能力：AddPlayerLine / AddDialogLineMultiAgent / RemoveRelatedLines / owner 哨兵清理。

场景 1: 阶段 1 — 接调查 Quest
{
  "InjectAtToken": "hero_main_options",
  "Owner": "theft_{CaseId}",
  "turns": [
    {
      "Id": "discovery_start",
      "SpeakerIndex": 0,
      "NpcLine": "你来得正好……村里的牲口不知怎的少了好几只。我正挨家挨户问话，可到现在也没个头绪。",
      "Options": [
        {
          "PlayerLine": "我可以帮忙查查。",
          "NpcResponse": "当真？那太好了。你去四处看看，有什么发现回来告诉我。",
          "NextTurn": "close_window",
          "Action": "START_QUEST",
          "ActionValue": "InvestigateVillageTheft"
        },
        {
          "PlayerLine": "（如果玩家是贼，心虚）听说你们在找偷牲口的……",
          "NpcResponse": "是啊，也不知道是哪个缺德的。怎么，你有什么线索？",
          "NextTurn": "report_clues",
          "Action": "START_QUEST",
          "ActionValue": "InvestigateVillageTheft"
        },
        {
          "PlayerLine": "这事跟我没关系。",
          "NpcResponse": "也是……那你忙你的吧。不过要是听到什么风声，记得来告诉我。",
          "NextTurn": "close_window",
          "Action": "NONE"
        }
      ]
    }
  ]
}
场景 2: 阶段 1 — 汇报调查结果（栽赃核心）
{
  "InjectAtToken": "hero_main_options",
  "Owner": "theft_{CaseId}",
  "Condition": "HasActiveQuest('InvestigateVillageTheft')",
  "turns": [
    {
      "Id": "report_findings",
      "SpeakerIndex": 0,
      "NpcLine": "怎么样？查到什么了吗？",
      "Options": [
        {
          "PlayerLine": "是附近藏身处的强盗干的！",
          "NpcResponse": "强盗？！……倒也是，他们干得出来。你确定？",
          "NextTurn": "frame_bandit_confirm",
          "Condition": "Always()",
          "Action": "NONE"
        },
        {
          "PlayerLine": "是 {FrameTarget1.Name} 干的。",
          "NpcResponse": "{FrameTarget1.Name}？你有证据吗？",
          "NextTurn": "frame_hero_present_evidence",
          "Condition": "HasFrameTarget(1)",
          "Action": "NONE"
        },
        {
          "PlayerLine": "是 {FrameTarget2.Name} 干的。",
          "NpcResponse": "{FrameTarget2.Name}？他怎么会……你有什么凭据？",
          "NextTurn": "frame_hero_present_evidence",
          "Condition": "HasFrameTarget(2)",
          "Action": "NONE"
        },
        {
          "PlayerLine": "……没查到什么。",
          "NpcResponse": "唉。那算了，我再找别人问问吧。",
          "NextTurn": "close_window",
          "Action": "QUEST_FAIL"
        },
        {
          "PlayerLine": "（如果玩家是贼，主动认栽）……是我干的。我愿意赔。",
          "NpcResponse": "你？！……（沉默片刻）好。既然你自己认了，咱们可以商量。",
          "NextTurn": "confess_restitution",
          "Condition": "IsPlayerThief()",
          "Action": "NONE"
        }
      ]
    },
    {
      "Id": "frame_bandit_confirm",
      "SpeakerIndex": 0,
      "NpcLine": "强盗偷牲口，天经地义。好，我信你——就是他们干的！",
      "Options": [
        {
          "PlayerLine": "那我去把强盗窝端了。",
          "NpcResponse": "拜托了！抓到贼首，我必有重谢。",
          "NextTurn": "close_window",
          "Action": "FRAME_SUSPECT",
          "ActionValue": "bandit"
        }
      ]
    },
    {
      "Id": "frame_hero_present_evidence",
      "SpeakerIndex": 0,
      "NpcLine": "这件东西……你是从哪找到的？",
      "Options": [
        {
          "PlayerLine": "[出示证物] 在牲口棚附近捡到的。",
          "NpcResponse": "（仔细看了看）……这确实是他的东西。好，那就是他了！",
          "NextTurn": "frame_success",
          "Condition": "HasEvidence()",
          "Action": "FRAME_SUSPECT_WITH_EVIDENCE"
        },
        {
          "PlayerLine": "我没证据，但我肯定是他。",
          "NpcResponse": "光凭嘴说可不行……（犹豫地看着你）",
          "NextTurn": "frame_bare_roll",
          "Condition": "!HasEvidence()",
          "Action": "SKILL_CHECK",
          "ActionValue": "Roguery:{DC}"
        }
      ]
    },
    {
      "Id": "frame_bare_roll",
      "SpeakerIndex": 0,
      "NpcLine": "{SkillCheckResult}",
      "Options": [
        {
          "PlayerLine": "{Success: 我就知道！/ Failure: 换个人指……}",
          "NpcResponse": "{Success: 好，我信你。/ Failure: 你越说越不对劲……}",
          "NextTurn": "{Success: frame_success / Failure: fail_forward}",
          "Action": "{Success: FRAME_SUSPECT / Failure: FAIL_COUNT}"
        }
      ]
    },
    {
      "Id": "fail_forward",
      "SpeakerIndex": 0,
      "NpcLine": "{FailCount == 1: '这次就算了，你再去查查。' / FailCount >= 2: '你一会指这个一会指那个……该不会就是你干的？'}",
      "Options": [
        {
          "PlayerLine": "{FailCount == 1: 我再查查 / FailCount >= 2: （语塞）}",
          "NpcResponse": "{FailCount == 1: 去吧。/ FailCount >= 2: 果然是你！（嫌疑转回玩家）}",
          "NextTurn": "close_window",
          "Action": "{FailCount == 1: NONE / FailCount >= 2: SUSPECT_PLAYER}"
        }
      ]
    }
  ]
}
场景 3: 阶段 2 — 嫌犯=玩家，村长冷脸
{
  "InjectAtToken": "hero_main_options",
  "Owner": "theft_{CaseId}",
  "Condition": "WorldEvent.Stage == Active && WorldEvent.SuspectHeroId == Player",
  "turns": [
    {
      "Id": "confront_player",
      "SpeakerIndex": 0,
      "NpcLine": "（冷冷地看着你）你还敢来？村里人都说是你干的。你有什么要说的？",
      "Options": [
        {
          "PlayerLine": "你们搞错了。给我个机会说清楚。",
          "NpcResponse": "……行。你说吧。",
          "NextTurn": "charm_defense_roll",
          "Condition": "!WorldEvent.CharmReprieveUsed",
          "Action": "SKILL_CHECK",
          "ActionValue": "Charm:{DC}"
        },
        {
          "PlayerLine": "这是赔偿，够不够？({RestitutionCost} 第纳尔)",
          "NpcResponse": "（数了数钱）……好吧。这次就算了。但别让我再抓到。",
          "NextTurn": "close_window",
          "Condition": "PlayerGold >= RestitutionCost",
          "Action": "PAY_RESTITUTION"
        },
        {
          "PlayerLine": "这是赔偿，够不够？(钱不够)",
          "Condition": "PlayerGold < RestitutionCost",
          "Disabled": true,
          "DisabledReason": "你凑不出这笔钱"
        },
        {
          "PlayerLine": "你再说一遍？（手按在剑柄上）",
          "NpcResponse": "……（咽了口唾沫）没、没什么。你走吧。",
          "NextTurn": "threat_success",
          "Condition": "PlayerRoguery >= 50",
          "Action": "SKILL_CHECK",
          "ActionValue": "Roguery:{DC}"
        },
        {
          "PlayerLine": "（转身就走）",
          "NpcResponse": "你跑不掉的！我们走着瞧！",
          "NextTurn": "close_window",
          "Action": "ESCALATE_TO_CONFRONTATION"
        }
      ]
    },
    {
      "Id": "charm_defense_roll",
      "SpeakerIndex": 0,
      "NpcLine": "{CharmSuccess: '……也许真是我搞错了。你先回去吧，我再查查。' / CharmFail: '你撒谎！村里人都看见你了！'}",
      "Options": [
        {
          "PlayerLine": "{CharmSuccess: 谢谢。/ CharmFail: （无言以对）}",
          "NpcResponse": "{CharmSuccess: 走吧。但我会盯着你的。 / CharmFail: 来人！给我抓住他！}",
          "NextTurn": "close_window",
          "Action": "{CharmSuccess: CHARM_REPRIEVE / CharmFail: ESCALATE_TO_CONFRONTATION}"
        }
      ]
    },
    {
      "Id": "threat_success",
      "SpeakerIndex": 0,
      "NpcLine": "（村长低下头，不敢看你的眼睛）",
      "Options": [
        {
          "PlayerLine": "（满意地离开）",
          "NextTurn": "close_window",
          "Action": "THREATEN_SUCCESS"
        }
      ]
    }
  ]
}
场景 4: 阶段 2 — 嫌犯≠玩家，接追捕 Quest
{
  "InjectAtToken": "hero_main_options",
  "Owner": "theft_{CaseId}",
  "Condition": "WorldEvent.Stage == Active && WorldEvent.SuspectHeroId != Player",
  "turns": [
    {
      "Id": "bounty_offer",
      "SpeakerIndex": 0,
      "NpcLine": "查出来了——是 {Suspect.Name} 干的！我正在找人去把他抓回来。赏金 {Bounty} 第纳尔。",
      "Options": [
        {
          "PlayerLine": "交给我吧。我去把他抓回来。",
          "NpcResponse": "好！活要见人，死要见尸。不过活捉的话，赏金翻倍。",
          "NextTurn": "close_window",
          "Action": "START_QUEST",
          "ActionValue": "BountyHunt"
        },
        {
          "PlayerLine": "我不想掺和这事。",
          "NpcResponse": "行吧……我再找别人。",
          "NextTurn": "close_window",
          "Action": "NONE"
        }
      ]
    }
  ]
}
JSON 中的 Action 类型（扩展 DialogueInjector）
当前 DialogueInjector 的 ExecuteAction 只支持 INCREASE_RELATION / GIVE_GOLD 等基础操作。需要扩展为支持本系统的动作：

// 在 DialogueInjector.ExecuteAction 中扩展：
switch (action)
{
    case "START_QUEST":        StartQuest(actionValue); break;
    case "QUEST_FAIL":         FailCurrentQuest(); break;
    case "FRAME_SUSPECT":      evt.SuspectHeroId = actionValue; evt.PublicAwareness = 1.0f; break;
    case "FRAME_SUSPECT_WITH_EVIDENCE": evt.SuspectHeroId = actionValue; evt.PublicAwareness = 1.0f; break;
    case "FAIL_COUNT":         evt.FailCount++; CheckFailForward(evt); break;
    case "SUSPECT_PLAYER":     evt.SuspectHeroId = Hero.MainHero.StringId; break;
    case "SKILL_CHECK":        return ResolveSkillCheck(actionValue); // 返回 {SkillCheckResult} 文本
    case "PAY_RESTITUTION":    PayRestitution(evt); break;
    case "CHARM_REPRIEVE":     CharmReprieve(evt); break;
    case "ESCALATE_TO_CONFRONTATION": evt.Stage = EventStage.Confrontation; break;
    case "THREATEN_SUCCESS":   evt.ResolvedBy = "intimidated"; InfamySystem.AddInfamy(1); break;
}
全部 JSON 文件清单
文件	场景	触发条件
village_theft_discovery.json	阶段1: 调查 Quest 接取	Stage==Emerging
village_theft_report.json	阶段1: 汇报调查+栽赃	持有调查 Quest
village_theft_confront_player.json	阶段2: 嫌犯=玩家，冷脸对峙	Stage==Active && Suspect==Player
village_theft_bounty_offer.json	阶段2: 嫌犯≠玩家，悬赏 Quest	Stage==Active && Suspect!=Player
village_theft_retaliation.json	阶段3: 报复部队说明	Stage==Confrontation
village_theft_witness_silence.json	目击者收买/吓唬	存在未封口 notable 目击者
以下按照 v2 设计文档的原始顺序，逐一对照。✅ = 覆盖，⚠️ = 部分覆盖需补充，❌ = 当前方案缺失。

目击后果分流（v2 第零节）
v2 体验	新方案实现	状态
偷窃时检测目击者	StealManager.GetWitnesses（已有）→ 写入 WorldEvent.WitnessHeroIds + TemplateWitness	✅
有人目击 → ThiefHeroId 当场确定	WorldEvent.SuspectHeroId = Hero.MainHero 直接写入，WasWitnessed → 跳阶段	✅
被当场抓住 → 当场对峙(mission内)	新方案未覆盖。需要在 InteractionMissionView 目击后弹出即时对话（复用 DialogueInjector）	⚠️
认错赔钱 → Resolved	对峙对话中一个 Intent：PayOnTheSpotIntent	⚠️
打翻村民逃跑 → 直接进阶段3	对峙对话选"动手" → WorldEvent.Stage = Confrontation	⚠️
被村民制服 → 惩罚 cutscene	复用 scn_execution_notification 场景（#13），槽位0=玩家(平民装无武器)，槽位1=村长(去掉斧头)，文案改成示众羞辱而非处决，CampaignSceneNotificationHelper.CreateNotificationCharacterFromHero 替换角色	✅
没被当场抓住（跑掉了）→ 直接进阶段2	WorldEvent.Stage = Active，SuspectHeroId = 玩家	✅
没人目击 → 完整调查流程	WorldEvent.Stage = Dormant → Emerging → Active 正常流转	✅
结论："当场抓住"的 mission 内即时事件需要在 InteractionMissionView 里加一段目击后对话逻辑。其余流转完全由 WorldEvent.Stage 状态机驱动。

三阶段 Issue-Quest 链（v2 第二节）
v2 体验	新方案实现	状态
Stage 1 Discovery: 蓝色 !，调查 Quest	CommissionHubIssue（已有）+ WorldEvent.Stage == Emerging → 注册 Issue	✅
玩家接调查 Quest → 处理证人 → 汇报	InvestigateIntent → 对话流（DialogueInjector JSON）	✅
玩家不接 → AI 自动每日掷骰推进	WorldEventStore.ProcessDaily 推进 PublicAwareness	✅
7 天冷案 → 草草结案	ProcessDaily 检查超时 → IsColdCase = true → Resolve("cold")	✅
Stage 2 SuspectIdentified: 黄色 !，追捕 Quest	WorldEvent.Stage == Active → CommissionHubIssue + CommissionQuest(BountyHunt)	✅
嫌犯=玩家 → 接不了 Quest，替代选项	PayRestitutionIntent / CharmDefenseIntent / ThreatIntent	✅
嫌犯≠玩家 → 正常追捕 Quest	CommissionQuest(Category=BountyHunt, Target=SuspectHero)（已有轮子）	✅
追捕 Quest 的活捉机制	TryKnockoutAgent + 倒地【俘虏】键 + TakePrisonerAction（wheels.md 已登记）	✅
Stage 3 Retaliation: 红色 !，报复 Quest	WorldEvent.Stage == Confrontation → CommissionQuest + 报复部队 spawn	✅
嫌犯=玩家 → 报复部队追玩家	SpawnPursuerParty（已有！CommissionQuest 的 DecoyMission 模式）	✅
嫌犯≠玩家 → 带队报复	CommissionQuest(Category=VillageDefense 变体)	✅
经济消耗战（打赢不结案 + 经费递减）	WorldEvent.RetaliationBudget 每次扣减 + PermanentEnemy	✅
被俘惩罚 cutscene	新方案未覆盖。需复用已有过场动画系统（vanilla_cutscenes 参考）	⚠️
结论：三阶段流转的核心机制全覆盖。! 标记、AI 自动推进、冷案、报复部队、经济战都在方案中。"当面对峙"和"被俘 cutscene"是仅有的两个缺口，属于后续 Step4+ 增强。

路径 A：栽赃嫁祸（v2 第一节）
v2 体验	新方案实现	状态
栽赃候选名单由 PlayerTheftLedger 生成	PlayerTheftLedger.GetFrameableTargets()（新建）	✅
目标 DC 表（强盗40/流浪汉35/村民55/商人70/领主85）	FrameSuspectIntent 中的 ComputeBaseDC(target) 方法，按目标身份自动计算	✅
纯 Roguery 裸过 → 不需证物	DC 40 对高 Roguery 玩家可直接过	✅
[出示证物] 道具加成 +20	子选项中 CanShowEvidence=true → playerPower += 20	✅
道具不消耗（村长只看一眼）	出示动作不调用 TransferItems，只做判定	✅
fail forward: 2次失败 → 按玩家身份分叉	FrameSuspectIntent.OnFail 累加 _failCount，到达2次 → 分支	✅
失败→玩家是贼→嫌疑转回玩家	SuspectHeroId = Hero.MainHero	✅
失败→玩家无辜→Quest失败，降关系	Quest fail + ChangeRelationAction(-5)	✅
大人物第二道坎（商人/领主）	DC过了→检查 IsPowerful(target) → NeedsPush 子状态	✅
Charm 激将 / Roguery 恐吓二次检定	PushAuthorityIntent（Charm）/ IntimidateAuthorityIntent（Roguery）	✅
二次检定失败→案子被压下	WorldEvent.SuspectHeroId = null，Trust -10	✅
被戳穿（村长转述证人）	WorldEvent.WitnessHeroIds 非空 + WitnessesSilenced=false → 村长对话分支	✅
栽赃强盗→零后果（不出狱复仇）	强盗无 HeroId → HeroNemesisTracker 不记录	✅
栽赃具体人→出狱复仇	QuestConsequenceResolver → HeroNemesisTracker.CreateRecord（已有）	✅
选择策略深度在【选谁】不在【检定】	子选项列表自带 DC + 道具需求 + 后续形态 + 出狱后果（四列信息），玩家做的是权衡	✅
结论：路径 A 完整覆盖。这也是 v2 最精妙的部分——DC 表、道具加成、fail forward、第二道坎、转述戳穿，全部在 FrameSuspectIntent + 子选项中实现。

路径 B：Charm/赔钱/威胁（v2 第一节）
v2 体验	新方案实现	状态
Charm 辩护（每案仅一次）	CharmDefenseIntent → WorldEvent.CharmReprieveUsed 守卫	✅
成功→嫌犯降级→回阶段1	SuspectHeroId = null，PublicAwareness = 0.5，Stage = Emerging	✅
失败→Trust -10→进阶段3	TrustSystem.AddTrust(-10)，Stage = Confrontation	✅
二次被锁→Charm 选项消失	CharmReprieveUsed == true → Evaluate 返回 Hide()	✅
赔钱消灾（×3 动物价值）	PayRestitutionIntent → TransferGold(玩家→村长, ×3)，钱不够选项灰掉	✅
阶段3 赔钱更贵（×5 + 罚金 + 安抚费）	PayRestitutionIntent 内部判断 Stage == Confrontation → ×5	✅
Trade 技能影响赔偿额（讨价还价）	ComputeRestitution 内 Trade skill * 折扣系数	⚠️
威胁（Roguery 检定，黑道威慑）	ThreatIntent → 成功=恶名+1, Trust暴跌, Resolved；失败=激怒, 进阶段3	✅
威胁加成于队伍规模/恶名	ComputeIntimidateDC 考虑 MobileParty.MainParty.MemberRoster.TotalManCount	⚠️
结论：路径 B 全覆盖。Charm 每案一次、赔钱分阶段定价、威胁的恶名后果都在 Intent 中。

路径 C：报复部队（v2 第一节+第三节）
v2 体验	新方案实现	状态
报复部队在大地图追猎玩家	CommissionQuest.SpawnPursuerParty → SetPartyAiAction.GetActionForEngagingParty（已有）	✅
部队命名 "{village}的复仇队"	V.SetPartyName 模板	✅
部队规模 5-8 民兵 + 3-5 雇佣打手	SpawnPursuerParty 已有兵力配置	✅
打 → 赢不结案，恶名+2	OnRetaliationPartyDefeated → 不调 TransitionStage，InfamySystem.AddInfamy(2)	✅
赢后下一波更强更贵	RetaliationWaveCount++ → GetWaveCost 递增	✅
村庄金库见底停派 + PermanentEnemy	RetaliationBudget 扣到不够 → PermanentEnemy = true	✅
投降/战败 → 被俘带回 → cutscene	复用 scn_execution_notification 场景模板，替换角色为玩家+村长，文案改为示众羞辱（非处决），通过 CampaignSceneNotificationHelper.CreateNotificationCharacterFromHero 完成角色替换	✅
不打·和解（Charm/Roguery 劝说）	SettleIntent（Charm/Roguery 检定，愤怒中成功率更低）	⚠️
和解 = Trust -15 + ×5 + 罚金 + 安抚费	SettleIntent.OnSuccess → TransferGold + TrustSystem.AddTrust(-15)	✅
不打·逃避（跑赢倒计时 15 天）	部队 15 天超时自散（CheckRetaliationTimeout）	✅
逃避代价：Trust -30, 恶名+3	OnRetaliationTimeout → TrustSystem.AddTrust(-30), InfamySystem.AddInfamy(3)	✅
结论：路径 C 除了 cutscene 和解锁选项外全覆盖。报复部队 spawn、经济战、打/逃/投降的后果都有。

其他 v2 体验
v2 体验	新方案实现	状态
通知推送（暗探情报 + 酒馆传闻）	WorldEventDirector（已有）—扩展推送 WorldEvent 阶段变化	⚠️
叙事遵守铁律（情报来自渠道）	NarrativeResolver + Narrative.csv（已有）	✅
目击者封口（mission内 notable + campaign层模板）	SilenceWitnessIntent（Mission 内对话）+ 聚合检定（Campaign 层）	⚠️
村长转述证人（不拉人到现场）	WitnessHeroIds 非空 + WitnessesSilenced=false → 对话分支引用证人名字	✅
玩家自查 UI（按 H 看赃物来源）	NpcInfoVM 背包栏 + PlayerTheftLedger 注脚	⚠️
同村后续偷窃（警觉 + 调查加速）	_villageAlertFlags 字典 + 新案 PublicAwareness 起始 +0.3	✅
汇总
类别	数量
✅ 完全覆盖	40 项
⚠️ 部分覆盖（核心机制有，细节待开发）	6 项
❌ 完全缺失	0 项
⚠️ 的 6 项全部属于已有轮子支撑、需开发但无架构风险的增量：

"讨价还价"、"威胁加成" — 数值微调
"通知推送"、"目击封口"、"玩家自查UI"、"和解劝说" — 已有 WorldEventDirector/NpcInfoVM/DialogueInjector 等轮子，直接在上面加功能
四、文件变更
新增
文件	内容	行数
Stealth/WorldFact.cs	WorldFact 数据模型 + FactCategory/FactStage 枚举 + WorldFactStore 管理器 + PlayerTheftLedger + FactTemplate	~300
修改
文件	改动	行数
Social/SocialEventManager.cs	SocialEvent 增加 FactId 字段（可空，关联 WorldFact）；BroadcastEvent 对 WorldFact 来源的 SocialEvent 走特殊 Tags	~10
Interaction/InteractionMissionView.cs	TryStealAnimal 末尾加 WorldFact 创建 + PlayerTheftLedger 记账 + 目击两档记录	~40
Core/MyBehavior.cs	DailyTick 加 WorldFactStore.ProcessDaily()；SyncData 加 2 个 key	~15
Interaction/Intents/IntentContext.cs	加 ActiveFact / IsAccused 字段 + Build 中注入情境感知	~20
Interaction/Intents/IntentRegistry.cs	RegisterDefaults 加注册追责 Intent	~7
Memory/SingNpcMemorySystem.cs	加 GetDesiredActions 方法	~60
Quests/Commissions/CommissionData.cs	加 IsAccountabilityQuest / FactId	~5
Quests/Commissions/CommissionGenerator.cs	从 WorldFact 生成追责 Quest	~40
不动（已有系统继续独立运行）
WorldEventSimulator.cs — AI 模拟事件生成，不动
WorldEventDirector.cs — 玩家发现事件推送，不动（未来可扩展推送 WorldFact 通知）
WorldEventDatabase.cs — WorldEventData 存储，不动
SocialEventManager.cs 核心逻辑 — NewsSpreadSystem.BroadcastEvent 传播引擎，不动
新增对话 JSON
文件	内容
ModuleData/DesignData/Dialogues/village_theft_accountability.json	阶段2/3 追责对话（赔钱/辩护/栽赃/威胁）
总新增代码: ~500 行，对比 v2 原设计的 ~2000+ 行

六、实施顺序
Phase 1: 数据层统一（Step 1）
新建 WorldEvent.cs（统一模型 + WorldEventStore + PlayerTheftLedger + FactTemplate）
SocialEventManager.cs → BroadcastEvent 适配 WorldEvent
SingNpcMemorySystem.cs → KnownEvents 适配
MyBehavior.cs → DailyTick + SyncData
验证: 偷羊 → WorldEvent 创建 → NewsSpreadSystem 自动传播 → 村长 KnownEvents 有事件

Phase 2: 态度 + 行动（Step 2）
新建 AttitudeSystem.cs（NpcStance + ComputeStance + ResponseGenerator）
SingNpcMemorySystem.cs → GetDesiredActions
验证: PerceivedSeverity 变化 → GetDesiredActions 自动解锁新行动

Phase 3: 玩家介入（Step 3）
新建 AccountabilityIntents.cs（7 个 Intent）
IntentContext.cs → ActiveEvent 情境感知
IntentRegistry.cs → 注册
新建对话 JSON 文件
验证: 对话中看到全部 v2 选项（赔钱/辩护/栽赃+子选项/威胁/调查/封口）

Phase 4: Quest 追责方向（Step 4）
CommissionData.cs → IsAccountabilityQuest + EventId
CommissionGenerator.cs → 从 WorldEvent 生成追责 Quest
WorldEventDatabase.cs → 薄壳适配
验证: 不赔钱 → 报复部队自动 spawn → 大地图追击 → 战斗结算

Phase 5: LLM 集成（Step 5）
PlayerGeneratedOption 用于 Intent 文案生成
IsLLMReady 关 → 确定性公式回落
七、验证方案
完整体验测试（对照 v2 三条路径）
测试 A: 栽赃嫁祸

偷羊（无目击）→ 回村 → 接调查
向村长汇报 → 选"是强盗干的" → DC 40 裸过
→ 嫌犯=强盗头子 → 接追捕 Quest → 清藏身处 → 报酬 + Trust+10
重来一次 → 选"是{账本Hero}干的" → 出示证物 → 检定 → 嫌犯锁定
如果指认商人/领主 → 出现第二道坎（激将/恐吓）
测试 B: 被查出来 → 摆平

偷羊（有人目击）→ 不封口 → 调查锁定玩家
回村对话 → 冷脸 → Charm 辩护（成功回退/失败进阶段3）
或者直接赔钱了事（钱够/不够两种）
测试 C: 不理会 → 报复

偷羊 → 被锁定 → 不赔钱不辩护
报复部队 spawn → 大地图追击
战斗: 打赢 → 下一波更强更多（经费递减）
打到村庄没钱 → 永久敌对
测试 D: 跨案件

偷羊被 Resolved（赔钱）→ 再偷同一村庄
验证: 警觉标记 → 调查加速 → 村长初始 cold
测试 E: 存档读档

各种状态 → 存档退出 → 读档
验证: WorldEvent + TheftLedger + 态度全部恢复
测试 F: LLM 不可用

IsLLMReady = false
所有 Intent 走 SingleRollResolver
对话文案走 CSV 兜底
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════════
    // 第 1 层：统一事件模型 — WorldEvent
    //
    // 合并两个现有概念：
    //   - WorldEventData（AI 模拟事件的存储）→ AI 专用字段作为可空字段纳入统一模型
    //   - VillageTheftCase（v2 的偷窃案件）→ 废弃，迁移到 WorldEvent
    //
    // 三层数据定位：
    //   第 1 层: WorldEvent (统一事件模型，1 份) — 客观真实 + 公共认知
    //   第 2 层: KnownEvent (每 NPC 1 份) — SingNpcMemorySystem.KnownEvents
    //   第 3 层: NpcStance (实时计算，不持久化) — AttitudeSystem.ComputeStance
    // ═══════════════════════════════════════════════════════════════════

    #region Enums

    /// <summary>事件大类</summary>
    public enum EventCategory
    {
        Crime,      // 玩家/AI 犯罪行为
        Social,     // 社交事件（婚丧嫁娶等）
        World       // 世界事件（匪患/饥荒等）
    }

    /// <summary>具体事件类型</summary>
    public enum EventType
    {
        // ── 犯罪类 ──
        Theft_Animal,       // 偷牲口
        Theft_Pickpocket,   // 扒窃
        Murder,             // 暗杀
        Poaching,           // 盗猎
        Smuggling,          // 走私
        Arson,              // 纵火
        Misconduct,         // 行为不端（拔刀/威胁/斗殴/偷窃未遂等 Mission 内当场触发）

        // ── AI 模拟事件（后续迁移） ──
        BanditRaid,
        Kidnapping,
        Famine,
        Betrayal,
        DebtTrap,
        RomanticConflict,
        FalseAccusation,
        InheritanceDispute,
        Fugitive,
        TradeDispute,
        NobleConflict,
        SacredTheft,
        Assassination,
        NemesisRevenge,

        // ── 涌现事件 ──
        VigilanteJustice,   // 冷案尾巴：村民迁怒打错人
        EscalatedCrime,     // 上报领主后的升级事件
    }

    /// <summary>事件六阶段</summary>
    public enum EventStage
    {
        Dormant,           // 事实已记录但尚未被发现
        Emerging,          // 被发现，调查/传闻开始传播 — 映射 v2 Stage 1 Discovery
        Active,            // 嫌犯锁定，Issue 公开 — 映射 v2 Stage 2 SuspectIdentified
        Confrontation,     // 报复/追捕/对峙 — 映射 v2 Stage 3 Retaliation
        Resolved,          // 已解决
        Unsolved           // 不了了之（冷案）
    }

    /// <summary>证据类型</summary>
    public enum EvidenceKind
    {
        Witness,           // 目击者证词
        Physical,          // 实物证据（匕首、戒指、箭矢）
        Circumstantial,   // 间接证据
        Documentary        // 文书证据
    }

    /// <summary>事件叙事阶段 — 决定文案该用"正在发生"还是"已经发生"的时态</summary>
    public enum WorldEventPhase
    {
        Impending,      // 事件仍在进行中
        Consummated     // 事件后果已施加
    }

    #endregion

    [Serializable]
    public class EvidencePointer
    {
        public string EvidenceId;
        public string TargetId;            // 指向谁："bandit" 或 heroId
        public EvidenceKind Kind;
        public string ItemId;              // 物证时：ItemObject.StringId；目击时：null
        public float Strength;             // 0→1
        public string SourceDescription;   // UI 叙事用
        public bool AtScene;               // Step4+：场景中是否有物理表现
        public bool IsPlanted;             // 是否是栽赃放置的假证据
        public string PlantedByHeroId;     // 谁放的（栽赃者），null = 真实证据
        public float DiscoveredDay;
    }

    #region WorldEvent

    /// <summary>
    /// 统一偷窃真相记录。每一条记录回答：谁（InitiatorId）在何时（Day）从哪（VictimSettlementId/VictimHeroId）
    /// 偷了什么（ItemId）多少（Count）。同时承载 UI 数据（LocationName / IsCleared）。
    /// 全局存储在 TheftLedger，WorldEvent 仅保留 StolenItems 作为去规范化摘要。
    /// </summary>
    [Serializable]
    public class TheftTruthRecord
    {
        public string InitiatorId;         // Hero.StringId of the real thief
        public string VictimHeroId;        // pickpocket victim Hero.StringId (null = animal/abstract)
        public string VictimSettlementId;  // where the theft happened
        public string ItemId;              // ItemObject.StringId
        public int Count;
        public float Day;
        public string LocationName;        // UI narrative: "在曹村"
        public bool IsCleared;             // case resolved + player paid consequences
        public string WorldEventId;        // which WorldEvent this belongs to
    }

    [Serializable]
    public class WorldEvent
    {
        // ═══ 身份 ═══
        public string EventId;
        public EventCategory Category;
        public EventType Type;
        public int Severity;               // 0-100

        // ═══ 客观真实（发生时写入，不可变） ═══
        public string InitiatorId;         // 作案者 Hero.StringId（系统知道的真凶）
        public string TargetHeroId;        // 受害者 hero（null = 村庄/抽象实体）
        public string TargetSettlementId;
        public float OccurredDay;
        public string LocationName;        // "牲口圈" / "村口大路" — UI 叙事用

        // ═══ 多物品追踪 ═══
        /// <summary>被盗物品 → 数量。从 WitnessTestimonies 中 Steal 类 ActionRecord 聚合派生。
        /// gold 的 value = 面额总额；普通物品 = 件数（旧存档 Count=0 按 1 兜底）。</summary>
        [JsonIgnore]
        public Dictionary<string, int> StolenItems => WitnessTestimonies
            ?.SelectMany(t => t.Actions ?? Enumerable.Empty<ActionRecord>())
            .Where(a => a.ActionType == "Steal" && !string.IsNullOrEmpty(a.ItemId))
            .GroupBy(a => a.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Count > 0 ? a.Count : 1))
            ?? new Dictionary<string, int>();

        // ═══ 目击者证词（仅 Alarmed 阶段写入，替代旧 ActionBreakdown） ═══
        /// <summary>
        /// 每位目击者的独立证词。仅 Alarmed 阶段的 NPC 才写入（Cautious/Suspicious 不留下持久记录）。
        /// 合并时同目击者同名 ActionType 累加 AlertValue。
        /// </summary>
        public List<WitnessTestimony> WitnessTestimonies;

        // ═══ 目击（从 WitnessTestimonies 派生） ═══
        [JsonIgnore]
        public List<string> WitnessHeroIds => WitnessTestimonies
            ?.Where(t => t.WitnessHeroId != null)
            .Select(t => t.WitnessHeroId).Distinct().ToList() ?? new List<string>();

        [JsonIgnore]
        public Dictionary<string, int> TemplateWitness => WitnessTestimonies
            ?.Where(t => t.TemplateId != null)
            .GroupBy(t => t.TemplateId)
            .ToDictionary(g => g.Key, g => g.Count())
            ?? new Dictionary<string, int>();
        public bool WitnessesSilenced;

        [JsonIgnore]
        public int WitnessCount => (WitnessHeroIds?.Count ?? 0)
                                 + (WitnessesSilenced ? 0 : (TemplateWitness?.Values.Sum() ?? 0));

        // ═══ 公共认知（随调查演进） ═══
        public float PublicAwareness;      // 0→1
        public string SuspectHeroId;       // 嫌犯（null=未知）

        // ═══ 袭击/击晕记账 ═══
        /// <summary>袭击受害者的身价累计（第纳尔，原版俘虏赎金价，见 CrimePenaltyCalculator.EstimateVictimValue）。</summary>
        public int AssaultValue;
        /// <summary>袭击/击晕受害者名单（UI 叙事用，去重）。</summary>
        public List<string> AssaultVictimNames;

        /// <summary>袭击赔偿基础值 = 受害者身价（原版赎金价）累计。</summary>
        [JsonIgnore]
        public int AssaultRestitutionValue => AssaultValue;

        // ═══ 玩家介入 ═══
        public bool CharmReprieveUsed;
        public int FailCount;
        public bool PlayerPaidRestitution;
        public bool PlayerTookInvestigationQuest; // 玩家接受调查任务
        public bool PlayerTookBountyQuest;

        // ═══ 报价台账（涨价必须说得清缘由） ═══
        // 赔款金额随案件阶段上浮（见 CrimePenaltyCalculator.ComputeRestitution：
        // Emerging ×0.7 / Active ×1.0 / Confrontation ×1.7，跨两级就是 2.43 倍）。
        // 涨价本身是设计意图——拒赔、动手就该更贵——但玩家记住的是自己听过的那个数，
        // 所以必须留台账：他听过多少、中间他自己干了什么，才能在收钱的界面上把话说明白。
        /// <summary>NPC 第一次真的报给玩家的赔款（0 = 玩家从没听过价）。涨价对比的锚点。</summary>
        public int FirstQuotedAmount;
        /// <summary>第一次报价时的案件阶段（叙事用：跳了几级）。</summary>
        public EventStage FirstQuotedStage;
        /// <summary>最近一次报给玩家的赔款。</summary>
        public int LastQuotedAmount;
        /// <summary>价钱为什么涨 —— 按发生顺序记玩家自己干的事（"你转身就走，没给钱" / "你拔剑动手"）。</summary>
        public List<string> PriceEscalationReasons;

        // ═══ 报复 ═══
        public int RetaliationBudget;
        public int RetaliationWaveCount;
        public string RetaliationPartyId;
        public float RetaliationSpawnDay;
        public bool RetaliationSpawned;
        public bool PermanentEnemy;

        // ═══ AI 模拟事件专用（玩家犯罪时 null/default） ═══
        public string GeneratedPartyId;
        public float? DayLimit;
        public int EscalationCount;
        public string ConspiracyId;
        public string HiddenMastermindId;
        public Dictionary<string, string> AuxiliaryPartyIds;
        public bool IsGenericInstigator;          // 加害方是否为通用模板（无真实 Hero）
        public bool IsRedirectedExistingParty;    // 生成的 party 是否征用了真人部队（事件结束时不能删除）

        // ═══ 调查进度 ═══
        public float InvestigationProgress;     // 0→1
        public List<EvidencePointer> EvidenceList;

        // ═══ 状态 ═══
        public EventStage Stage;
        public string ResolvedBy;
        public bool WasBroadcast;
        public float LastUpdateDay;

        // 内部追踪字段（不序列化）
        [JsonIgnore] public float _stageEnteredDay;
        [JsonIgnore] public bool _coldCaseTailTriggered;
        [JsonIgnore] public bool _coldCaseTailRolled;
        [JsonIgnore] public float _workOffDebtDay;
        [JsonIgnore] public bool _workOffDebtAccepted;
        [JsonIgnore] public int _workOffDaysDone;
        [JsonIgnore] public bool _haggleAttempted;   // 砍价已尝试（同一对话内禁止重试）
        [JsonIgnore] public int _hagglePrice;        // 砍后价（0=还没砍成）。同一对话内重进 restitution_demand 时沿用此价

        // ═══ 辅助方法 ═══

        [JsonIgnore]
        public EventConfig Config => EventConfig.Get(Type);

        [JsonIgnore]
        public Settlement TargetSettlement => string.IsNullOrEmpty(TargetSettlementId)
            ? null : Settlement.Find(TargetSettlementId);

        [JsonIgnore]
        public Hero TargetHero => string.IsNullOrEmpty(TargetHeroId)
            ? null : Hero.FindFirst(h => h.StringId == TargetHeroId);

        [JsonIgnore]
        public bool SuspectIsPlayer => SuspectHeroId == Hero.MainHero?.StringId;

        [JsonIgnore]
        public bool InitiatorIsPlayer => InitiatorId == Hero.MainHero?.StringId;

        [JsonIgnore]
        public bool IsActive => Stage != EventStage.Resolved && Stage != EventStage.Unsolved;

        [JsonIgnore]
        public bool HasHiddenMastermind => !string.IsNullOrEmpty(HiddenMastermindId);

        [JsonIgnore]
        public Hero InstigatorHero => string.IsNullOrEmpty(InitiatorId)
            ? null : Hero.FindFirst(h => h.StringId == InitiatorId);

        [JsonIgnore]
        public MobileParty GeneratedParty
        {
            get
            {
                if (string.IsNullOrEmpty(GeneratedPartyId)) return null;
                foreach (var mp in Campaign.Current?.MobileParties ?? Enumerable.Empty<MobileParty>())
                    if (mp.StringId == GeneratedPartyId) return mp;
                return null;
            }
        }

        [JsonIgnore]
        public float ExpiryDay => OccurredDay + (DayLimit ?? 0);

        [JsonIgnore]
        public bool IsExpired => Campaign.Current != null && (float)CampaignTime.Now.ToDays > ExpiryDay;

        /// <summary>浅拷贝：新 EventId + 复制所有可序列化字段，集合浅克隆，追踪字段归零。</summary>
        public WorldEvent ShallowCopy(string newEventId)
        {
            return new WorldEvent
            {
                EventId = newEventId,
                Category = Category,
                Type = Type,
                Severity = Severity,
                InitiatorId = InitiatorId,
                TargetHeroId = TargetHeroId,
                TargetSettlementId = TargetSettlementId,
                OccurredDay = OccurredDay,
                LocationName = LocationName,
                WitnessTestimonies = WitnessTestimonies != null
                    ? new List<WitnessTestimony>(WitnessTestimonies) : null,
                WitnessesSilenced = WitnessesSilenced,
                PublicAwareness = PublicAwareness,
                SuspectHeroId = SuspectHeroId,
                CharmReprieveUsed = CharmReprieveUsed,
                FailCount = FailCount,
                PlayerPaidRestitution = PlayerPaidRestitution,
                PlayerTookInvestigationQuest = PlayerTookInvestigationQuest,
                PlayerTookBountyQuest = PlayerTookBountyQuest,
                RetaliationBudget = RetaliationBudget,
                RetaliationWaveCount = RetaliationWaveCount,
                RetaliationPartyId = RetaliationPartyId,
                RetaliationSpawnDay = RetaliationSpawnDay,
                RetaliationSpawned = RetaliationSpawned,
                PermanentEnemy = PermanentEnemy,
                GeneratedPartyId = GeneratedPartyId,
                DayLimit = DayLimit,
                EscalationCount = EscalationCount,
                ConspiracyId = ConspiracyId,
                HiddenMastermindId = HiddenMastermindId,
                AuxiliaryPartyIds = AuxiliaryPartyIds != null
                    ? new Dictionary<string, string>(AuxiliaryPartyIds) : null,
                IsGenericInstigator = IsGenericInstigator,
                IsRedirectedExistingParty = IsRedirectedExistingParty,
                InvestigationProgress = InvestigationProgress,
                EvidenceList = EvidenceList != null
                    ? new List<EvidencePointer>(EvidenceList) : null,
                Stage = Stage,
                ResolvedBy = ResolvedBy,
                // WasBroadcast / LastUpdateDay / _stageEnteredDay 等追踪字段归零
            };
        }

        /// <summary>获取被盗物品字典。</summary>
        [JsonIgnore]
        public Dictionary<string, int> StolenItemsSnapshot => StolenItems;

        /// <summary>被盗物品总"项数"（金只算一项，不混入面额——悬赏按件数定价用）</summary>
        [JsonIgnore]
        public int TotalStolenCount => StolenItemsSnapshot.Sum(kv => kv.Key == "gold" ? 1 : kv.Value);

        /// <summary>被盗物品总市值（物品市值 × 数量 + 金按面值计入）</summary>
        [JsonIgnore]
        public int TotalStolenValue
        {
            get
            {
                int total = 0;
                foreach (var kv in StolenItemsSnapshot)
                {
                    if (kv.Key == "gold") { total += kv.Value; continue; }
                    var item = MBObjectManager.Instance.GetObject<ItemObject>(kv.Key);
                    if (item != null) total += item.Value * kv.Value;
                }
                return total;
            }
        }

        /// <summary>
        /// 根据定居点类型返回合适的地点词：村里/镇上/堡里/当地。
        /// 用于替代各处硬编码的"村里"，使文案适配 Village/Town/Castle。
        /// </summary>
        [JsonIgnore]
        public string SettlementLocationWord
        {
            get
            {
                var s = TargetSettlement;
                if (s == null) return "当地";
                if (s.IsVillage) return "村里";
                if (s.IsTown) return "镇上";
                if (s.IsCastle) return "堡里";
                return "当地";
            }
        }

        /// <summary>
        /// 地点词 + 人：村里人/镇上人/堡里人/当地人。
        /// 用于"XX都知道了"等涉及当地居民的文案。
        /// </summary>
        [JsonIgnore]
        public string SettlementPeopleWord
        {
            get
            {
                var s = TargetSettlement;
                if (s == null) return "当地人";
                if (s.IsVillage) return "村里人";
                if (s.IsTown) return "镇上人";
                if (s.IsCastle) return "堡里人";
                return "当地人";
            }
        }

        /// <summary>
        /// 最优地点词：优先用具体子场景（酒馆里/地牢里），
        /// 未设置 LocationName 时回退到定居点级别（镇上/村里/堡里）。
        /// 用于 BuildDetailedHarmBreakdown 等描述案发地点的文案。
        /// </summary>
        [JsonIgnore]
        public string BestLocationWord
        {
            get
            {
                if (!string.IsNullOrEmpty(LocationName))
                    return LocationName;
                return SettlementLocationWord;
            }
        }

        /// <summary>
        /// 从 CampaignMission.Location.StringId 解析中文场景名（含后缀"里"）。
        /// 用于 Mission 内填充 LocationName 字段。
        /// </summary>
        public static string ResolveSceneLocationName(string locationStringId)
        {
            if (string.IsNullOrEmpty(locationStringId)) return null;

            // 室内场景 → 带"里"后缀
            if (locationStringId.Contains("tavern")) return "酒馆里";
            if (locationStringId.Contains("lordshall")) return "领主大厅里";
            if (locationStringId.Contains("prison") || locationStringId.Contains("dungeon")) return "地牢里";
            if (locationStringId.Contains("alley")) return "后巷里";
            if (locationStringId.Contains("arena")) return "竞技场里";

            // 室外场景 → 按定居点类型区分
            if (locationStringId == "center" || locationStringId.Contains("village"))
            {
                var s = Settlement.CurrentSettlement;
                if (s != null && s.IsVillage) return "村里";
                return null; // 城镇中心 → 回退到 SettlementLocationWord（"镇上"）
            }

            if (locationStringId.Contains("castle")) return "堡里";

            return null; // 未知场景 → 回退
        }

        [JsonIgnore]
        public WorldEventPhase Phase
        {
            get
            {
                if (Stage == EventStage.Resolved || Stage == EventStage.Unsolved)
                    return WorldEventPhase.Consummated;
                var party = GeneratedParty;
                var settlement = TargetSettlement;
                if (party != null && settlement != null && party.IsActive
                    && V.Pos(party).Distance(V.Pos(settlement)) < 3f)
                    return WorldEventPhase.Consummated;
                return WorldEventPhase.Impending;
            }
        }

        /// <summary>
        /// 构建玩家行为的中文描述（用于对话中替换笼统的"闹事"）。
        /// 从所有目击者的证词中聚合去重，按频次降序排列，支持 1-3 种行为的自然语言拼接。
        /// </summary>
        [JsonIgnore]
        public string ActionDescription
        {
            get
            {
                if (WitnessTestimonies == null || WitnessTestimonies.Count == 0)
                    return Config?.CrimeVerbGerund ?? "闹事";

                // 从所有目击者证词中聚合每种 ActionType 的总 AlertValue
                var agg = new Dictionary<string, float>();
                foreach (var t in WitnessTestimonies)
                {
                    if (t.Actions == null) continue;
                    foreach (var a in t.Actions)
                    {
                        agg.TryGetValue(a.ActionType, out float cur);
                        agg[a.ActionType] = cur + a.AlertValue;
                    }
                }

                var parts = new List<string>();
                foreach (var kv in agg.OrderByDescending(kv => kv.Value))
                {
                    string desc = kv.Key switch
                    {
                        "Crouching" => "鬼鬼祟祟蹲了半天",
                        "WeaponDrawn" => $"在{BestLocationWord}拔出武器",
                        "StealUIOpen" => "手脚不干净",
                        "Steal" => "偷了东西",
                        "AttackAlly" => "袭击别人",
                        "Knockout" => "把人打晕了",
                        _ => null
                    };
                    if (desc != null && kv.Value > 0) parts.Add(desc);
                }

                return parts.Count switch
                {
                    0 => Config?.CrimeVerbGerund ?? "闹事",
                    1 => parts[0],
                    2 => $"{parts[0]}，还{parts[1]}",
                    _ => $"{parts[0]}、{parts[1]}，还{parts[2]}"
                };
            }
        }

        /// <summary>获取辅助部队</summary>
        public MobileParty GetAuxiliaryParty(string roleTag)
        {
            if (AuxiliaryPartyIds == null || string.IsNullOrEmpty(roleTag)) return null;
            if (!AuxiliaryPartyIds.TryGetValue(roleTag, out string pid) || string.IsNullOrEmpty(pid)) return null;
            foreach (var mp in Campaign.Current?.MobileParties ?? Enumerable.Empty<MobileParty>())
                if (mp.StringId == pid) return mp;
            return null;
        }

        /// <summary>
        /// 构建逐项算账明细（NPC 情报边界版）。
        /// 旧案赃物 NPC 没看见是谁偷的 → "镇上丢了XX"（被动语态，地点词按定居点类型）；
        /// 袭击是当场抓住的 → "你把XX打晕了"（直指玩家）。
        /// 新旧两案合并时 NPC 说清"两笔账一起算"。
        /// </summary>
        public string BuildDetailedHarmBreakdown()
        {
            bool hasTheft = TotalStolenCount > 0;
            bool hasAssault = AssaultVictimNames?.Count > 0;
            string loc = BestLocationWord;

            string theftPart = hasTheft
                ? $"丢了{BuildStolenItemsDescription()}，市值{TotalStolenValue}第纳尔，一直没找到是谁干的"
                : "";
            string assaultPart = "";
            if (hasAssault)
            {
                string victimDesc = AssaultVictimNames.Count == 1
                    ? AssaultVictimNames[0]
                    : $"{string.Join("、", AssaultVictimNames)}等{AssaultVictimNames.Count}人";
                assaultPart = $"你把{victimDesc}打晕了，身价{AssaultRestitutionValue}第纳尔";
            }

            if (hasTheft && hasAssault)
                return $"前阵子{loc}{theftPart}。今天{assaultPart}——既然抓着的是你，两笔账一起算";
            if (hasTheft) return $"{loc}{theftPart}";
            if (hasAssault) return assaultPart;
            return "闹了事";
        }

        /// <summary>赔偿金额的明细解释（给玩家看为什么是这个数）</summary>
        public string GetRestitutionBreakdown()
        {
            var cfg = Config;
            if (cfg == null) return "赔100第纳尔。";

            string harm = BuildDetailedHarmBreakdown();
            int total = CrimePenaltyCalculator.ComputeCost(this, CostType.Restitution);
            string crimeGerund = cfg.CrimeVerbGerund ?? "犯事";
            int multiplier = cfg.BaseRestitutionMultiplier;
            string peopleWord = SettlementPeopleWord;

            if (Stage <= EventStage.Emerging)
                return $"{harm}。既然你自己认了，{crimeGerund}按规矩罚{multiplier}倍，一共{total}第纳尔。你认不认？";
            else if (Stage == EventStage.Active)
                return $"{harm}。{peopleWord}都知道了，{crimeGerund}按规矩罚{multiplier}倍，一共{total}第纳尔。你认不认？";
            else
                return $"{harm}。最后一次机会——{crimeGerund}按规矩罚{multiplier}倍，一共{total}第纳尔。否则后果自负。你认不认？";
        }

        // ═══════════════════════════════════════════════════════════════
        // 报价台账
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 记一笔"NPC 真的把这个数报给了玩家"。
        /// 只在台词/选项**确实显示出来**的时候调（NPC 开价节点的 LazyNpcLine、Alert 开场子树），
        /// 不要在构树阶段调 —— 玩家没听过的价不算报过价，否则事后"原本赔 X 就完了"是在编。
        /// </summary>
        public void RecordQuote(int amount)
        {
            if (amount <= 0) return;
            LastQuotedAmount = amount;
            if (FirstQuotedAmount > 0) return;

            FirstQuotedAmount = amount;
            FirstQuotedStage = Stage;
            DebugLogger.Log($"[Penalty] {EventId} first quote to player: {amount} @ {Stage}");
        }

        /// <summary>
        /// 记一条涨价缘由。玩家没听过价（<see cref="FirstQuotedAmount"/>==0）就不记 ——
        /// 没有对比锚点的时候，"因为你拔剑所以贵了"这句话玩家看不懂。
        /// 连续重复的同一条原因不重复记（同一次冲突可能触发多次阶段迁移尝试）。
        /// </summary>
        public void RecordPriceEscalation(string reason)
        {
            if (FirstQuotedAmount <= 0 || string.IsNullOrEmpty(reason)) return;

            PriceEscalationReasons = PriceEscalationReasons ?? new List<string>();
            if (PriceEscalationReasons.Count > 0
                && PriceEscalationReasons[PriceEscalationReasons.Count - 1] == reason) return;

            PriceEscalationReasons.Add(reason);
            DebugLogger.Log($"[Penalty] {EventId} price escalation #{PriceEscalationReasons.Count}: {reason}");
        }

        /// <summary>
        /// 涨价缘由说明（给玩家看的一段话）。
        /// 返回 null = 没什么要解释的（玩家没听过价 / 现价没比听过的价高）—— 调用方直接别显示。
        /// </summary>
        /// <param name="currentAmount">现在真要收的数</param>
        public string BuildPriceEscalationNote(int currentAmount)
        {
            if (FirstQuotedAmount <= 0) return null;
            // 5% 以内的差异属于阶段倍率/交易技能的正常抖动，不值得专门解释
            if (currentAmount <= (int)(FirstQuotedAmount * 1.05f)) return null;

            float times = currentAmount / (float)FirstQuotedAmount;
            string because = (PriceEscalationReasons?.Count > 0)
                ? string.Join("、", PriceEscalationReasons)
                : "事情一路闹大";

            return $"当初{because} —— 那时候赔 {FirstQuotedAmount} 就能了事，" +
                   $"现在他们开口要 {currentAmount}，翻了 {times:0.#} 倍。";
        }

        /// <summary>损失描述（赔偿对话主语）：赃物市值 + 袭击身价，合并成一句；啥都没有时回落旧文案。</summary>
        public string BuildLossDescription()
        {
            var parts = new List<string>();
            if (TotalStolenCount > 0)
                parts.Add($"{BuildStolenItemsDescription()}，市值{TotalStolenValue}第纳尔");
            if (AssaultVictimNames?.Count > 0)
                parts.Add($"{BuildAssaultVictimsDescription()}，身价{AssaultRestitutionValue}第纳尔");
            return parts.Count > 0 ? string.Join("；", parts) : "东西，市值0第纳尔";
        }

        /// <summary>袭击受害者的自然语言描述（用于赔偿/对话）</summary>
        public string BuildAssaultVictimsDescription()
        {
            var names = AssaultVictimNames;
            if (names == null || names.Count == 0) return "";
            if (names.Count == 1) return $"把{names[0]}打晕了";
            if (names.Count == 2) return $"把{names[0]}和{names[1]}打晕了";
            return $"把{names[0]}、{names[1]}等{names.Count}人打晕了";
        }

        // ═══════════════════════════════════════════════════════════════
        // 事实派生案情描述 — 统一入口
        // 所有玩家可见的案情文本（发现通知 / Issue / Quest / 传闻 / 对话）
        // 必须从事件记录的事实（袭击记账 + 赃物暗账）派生，禁止用 EventType
        // 静态模板（Config.CrimeVerb*）硬套——Misconduct 是万用容器类型，
        // 模板文案描述不了"击晕+搜刮"这类复合罪行。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>是否有袭击/击晕记账</summary>
        [JsonIgnore]
        public bool HasAssault => AssaultVictimNames?.Count > 0;

        /// <summary>
        /// 案件定性标签（标题/简述用）：刑案（伤人+失窃）/ 伤人案 / 失窃案 / 案件。
        /// </summary>
        [JsonIgnore]
        public string CaseLabel
        {
            get
            {
                bool hasStolen = TotalStolenCount > 0;
                if (HasAssault && hasStolen) return "刑案";
                if (HasAssault) return "伤人案";
                if (hasStolen) return "失窃案";
                return "案件";
            }
        }

        /// <summary>
        /// 案情事实句（村民视角，次日发现，不知是谁干的）：
        /// 有袭击+失窃 → "帝国农民被人打晕了，还少了一件扣带束腰衣等4项财物"
        /// 仅袭击 → "帝国农民被人打晕了"；仅失窃 → "少了一只羊"；都无 → 回落类型模板。
        /// 发现通知 / Issue 描述 / Quest 日志 / 传闻 / 对话占位符统一走这里。
        /// </summary>
        public string BuildDiscoveryFacts()
        {
            bool hasStolen = TotalStolenCount > 0;
            var names = AssaultVictimNames;

            if (HasAssault && hasStolen)
            {
                string victimPart = names.Count == 1
                    ? $"{names[0]}被人打晕了"
                    : $"有{names.Count}人被人打晕了";
                return $"{victimPart}，还少了{BuildStolenItemsDescription()}";
            }
            if (HasAssault)
            {
                return names.Count == 1
                    ? $"{names[0]}被人打晕了"
                    : $"有{names.Count}人被人打晕了";
            }
            if (hasStolen)
                return $"少了{BuildStolenItemsDescription()}";
            return Config?.CrimeVerbPast ?? "出了事";
        }

        /// <summary>构建被盗物品的自然语言描述（用于赔偿/对话）。
        /// 量词按物品类别：牲畜→只、装备/货物→件、金→"N第纳尔"；
        /// 3 种以上混合时尾巴泛称：全是牲畜叫"牲口"，否则叫"财物"。</summary>
        public string BuildStolenItemsDescription()
        {
            var items = StolenItemsSnapshot;
            if (items.Count == 0) return "东西";

            var parts = new List<string>();
            int totalCount = 0;      // 总项数（金算 1 项）
            bool hasAnimal = false;  // 含牲畜
            bool hasNonAnimal = false;

            foreach (var kv in items)
            {
                // 金钱 = 特殊物品（铁律 4）：按面额直呼，不占"件/只"量词
                if (kv.Key == "gold")
                {
                    parts.Add($"{kv.Value}第纳尔");
                    totalCount += 1;
                    hasNonAnimal = true;
                    continue;
                }
                var item = MBObjectManager.Instance.GetObject<ItemObject>(kv.Key);
                string name = item?.Name?.ToString() ?? kv.Key;
                bool isAnimal = item?.Type == ItemObject.ItemTypeEnum.Animal;
                if (isAnimal) hasAnimal = true; else hasNonAnimal = true;
                string unit = isAnimal ? "只" : "件";
                parts.Add(kv.Value == 1 ? $"一{unit}{name}" : $"{kv.Value}{unit}{name}");
                totalCount += kv.Value;
            }

            if (parts.Count == 1) return parts[0];
            if (parts.Count == 2) return $"{parts[0]}和{parts[1]}";
            // 3+ 种不同物品：列举前两项 + 泛称总量（纯牲畜才叫"牲口"）
            string tail = hasAnimal && !hasNonAnimal ? $"等{totalCount}只牲口" : $"等{totalCount}项财物";
            return $"{parts[0]}、{parts[1]}{tail}";
        }
    }

    #endregion

    #region EventConfig

    /// <summary>
    /// 一种犯罪类型的配置。新犯罪类型 = 一个 EventConfig 静态字段。
    /// </summary>
    public class EventConfig
    {
        public EventType Type;
        public EventCategory Category;
        public string DisplayName;             // "偷牲口" / "暗杀" / "盗猎"
        public int DefaultSeverity;            // 0-100
        public string VictimLabel;             // "村庄" / "死者家族" / "领主猎场"
        public string AuthorityRole;           // "村长" / "族长" / "领主"

        // ── Flavor 文本（对话占位符用） ──
        public string CrimeVerb;               // "偷了" / "杀了" / "在猎场下了套"
        public string CrimeVerbPast;           // "牲口被偷了" / "人被杀了"
        public string CrimeVerbGerund;         // "偷牲口" / "杀人" / "盗猎"
        public string CrimeScene;              // "牲口圈" / "{victim}家附近" / "领主猎场"

        // ── 传播 ──
        public float BaseSpreadRate = 0.1f;

        // ── 经济 ──
        public int BaseRestitutionMultiplier = 3;
        public int BaseBountyPerUnit = 50;

        // ── 调查 ──
        public float BaseInvestigationRate = 0.25f;
        public int InvestigationWindowDays = 7;

        // ── 行为偏好 ──
        public List<ResponsePattern> PreferredResponses = new List<ResponsePattern>();

        // ── 唯一钩子：发现条件（null = 默认次日发现） ──
        public Func<WorldEvent, bool> DiscoveryCheck;

        // ── 初始证据生成 ──
        public Func<WorldEvent, List<EvidencePointer>> GenerateInitialEvidence;

        private static readonly Dictionary<EventType, EventConfig> _registry
            = new Dictionary<EventType, EventConfig>();

        public static void Register(EventConfig config)
        {
            if (config != null)
                _registry[config.Type] = config;
        }

        public static EventConfig Get(EventType type)
        {
            _registry.TryGetValue(type, out var config);
            return config;
        }

        /// <summary>获取严重度口语化文字</summary>
        public static string GetSeverityWord(int severity)
        {
            return severity switch
            {
                <= 30 => "小事",
                <= 50 => "有点严重",
                <= 70 => "严重",
                <= 85 => "很严重",
                _ => "天大的事"
            };
        }

        static EventConfig()
        {
            Register(AnimalTheft);
            Register(Pickpocket);
            Register(Murder);
            Register(Poaching);
            Register(Misconduct);
        }

        public static readonly EventConfig AnimalTheft = new EventConfig
        {
            Type = EventType.Theft_Animal,
            Category = EventCategory.Crime,
            DisplayName = "偷牲口",
            DefaultSeverity = 30,
            VictimLabel = "村庄",
            AuthorityRole = "村长",
            CrimeVerb = "偷了",
            CrimeVerbPast = "牲口被偷了",
            CrimeVerbGerund = "偷牲口",
            CrimeScene = "牲口圈",
            BaseSpreadRate = 0.1f,
            BaseRestitutionMultiplier = 3,
            BaseBountyPerUnit = 50,
            BaseInvestigationRate = 0.25f,
            InvestigationWindowDays = 7,
            PreferredResponses = { ResponsePattern.DemandRestitution, ResponsePattern.IssueBounty },
            DiscoveryCheck = null,
            GenerateInitialEvidence = evt =>
            {
                var list = new List<EvidencePointer>();
                if (evt.WitnessHeroIds?.Count > 0)
                {
                    list.Add(new EvidencePointer
                    {
                        EvidenceId = $"{evt.EventId}_witness",
                        TargetId = evt.InitiatorId,
                        Kind = EvidenceKind.Witness,
                        Strength = 0.7f,
                        SourceDescription = $"目击者称看到有人在{evt.LocationName ?? "牲口圈"}附近鬼鬼祟祟",
                        DiscoveredDay = evt.OccurredDay
                    });
                }
                return list;
            }
        };

        public static readonly EventConfig Pickpocket = new EventConfig
        {
            Type = EventType.Theft_Pickpocket,
            Category = EventCategory.Crime,
            DisplayName = "扒窃",
            DefaultSeverity = 20,
            VictimLabel = "受害者",
            AuthorityRole = "镇长",
            CrimeVerb = "偷了",
            CrimeVerbPast = "随身财物被偷了",
            CrimeVerbGerund = "扒窃",
            CrimeScene = "市集",
            BaseSpreadRate = 0.08f,
            BaseRestitutionMultiplier = 2,
            BaseBountyPerUnit = 30,
            BaseInvestigationRate = 0.2f,
            InvestigationWindowDays = 5,
            PreferredResponses = { ResponsePattern.DemandRestitution, ResponsePattern.IssueBounty },
        };

        public static readonly EventConfig Murder = new EventConfig
        {
            Type = EventType.Murder,
            Category = EventCategory.Crime,
            DisplayName = "暗杀",
            DefaultSeverity = 100,
            VictimLabel = "死者家族",
            AuthorityRole = "族长",
            CrimeVerb = "杀了",
            CrimeVerbPast = "人被杀了",
            CrimeVerbGerund = "杀人",
            CrimeScene = "{victim}家附近",
            BaseSpreadRate = 0.5f,
            BaseRestitutionMultiplier = 50,
            BaseBountyPerUnit = 5000,
            BaseInvestigationRate = 0.3f,
            InvestigationWindowDays = 30,
            PreferredResponses = { ResponsePattern.ReportToLord, ResponsePattern.LeadRetaliation },
        };

        public static readonly EventConfig Poaching = new EventConfig
        {
            Type = EventType.Poaching,
            Category = EventCategory.Crime,
            DisplayName = "盗猎",
            DefaultSeverity = 50,
            VictimLabel = "领主猎场",
            AuthorityRole = "领主",
            CrimeVerb = "在猎场下了套",
            CrimeVerbPast = "猎场的猎物被偷了",
            CrimeVerbGerund = "盗猎",
            CrimeScene = "领主猎场",
            BaseSpreadRate = 0.15f,
            BaseRestitutionMultiplier = 10,
            BaseBountyPerUnit = 200,
            BaseInvestigationRate = 0.25f,
            InvestigationWindowDays = 16,
            PreferredResponses = { ResponsePattern.ReportToLord, ResponsePattern.IssueBounty },
        };

        /// <summary>
        /// 行为不端 — Mission 内当场触发（拔刀/威胁/斗殴/偷窃未遂等）。
        /// 与 Theft 系列不同：无被盗物品，Severity 由玩家行为动态计算。
        /// 这是 L3 警戒质问升级为持久 WorldEvent 的唯一类型。
        /// </summary>
        public static readonly EventConfig Misconduct = new EventConfig
        {
            Type = EventType.Misconduct,
            Category = EventCategory.Crime,
            DisplayName = "行为不端",
            DefaultSeverity = 25,
            VictimLabel = "村庄",
            AuthorityRole = "村长",
            CrimeVerb = "闹事",
            CrimeVerbPast = "有人在当地闹事",
            CrimeVerbGerund = "闹事",
            CrimeScene = "当地",
            BaseSpreadRate = 0.05f,
            BaseRestitutionMultiplier = 2,
            BaseBountyPerUnit = 30,
            BaseInvestigationRate = 0.15f,
            InvestigationWindowDays = 5,
            PreferredResponses = { ResponsePattern.DemandRestitution },
        };
    }

    #endregion

    #region WorldEventStore

    /// <summary>
    /// WorldEvent 存储器。替代原 WorldEventDatabase 的薄壳，
    /// 存 List&lt;WorldEvent&gt; JSON，通过 MyBehavior.SyncData 持久化。
    /// </summary>
    public static class WorldEventStore
    {
        private static List<WorldEvent> _allEvents = new List<WorldEvent>();
        private static Dictionary<string, bool> _villageAlertFlags = new Dictionary<string, bool>();

        /// <summary>
        /// 当世界事件的阶段发生变化时触发。
        /// 订阅者（CommissionIssueBehavior 等）可以立即刷新 ! 标记等 UI，
        /// 无需等待 DailyTick 或 SettlementEntered 事件。
        /// </summary>
        public static event Action<WorldEvent> OnEventStageChanged;

        public static IReadOnlyList<WorldEvent> AllEvents => _allEvents.AsReadOnly();
        public static IReadOnlyList<WorldEvent> ActiveEvents =>
            _allEvents.Where(e => e.IsActive).ToList().AsReadOnly();
        public static IReadOnlyList<WorldEvent> ResolvedEvents =>
            _allEvents.Where(e => e.Stage == EventStage.Resolved).ToList().AsReadOnly();
        public static int TotalEventCount => _allEvents.Count;
        public static int ActiveEventCount => _allEvents.Count(e => e.IsActive);

        /// <summary>查找指定定居点的活跃事件（同村同时最多一个活跃案件）。
        /// 自动包含 PendingWorldEvent 兜底——调用方无需手动组合双源查找。</summary>
        public static WorldEvent FindActive(string settlementId)
        {
            return _allEvents.FirstOrDefault(e =>
                e.TargetSettlementId == settlementId &&
                e.Stage != EventStage.Resolved &&
                e.Stage != EventStage.Unsolved)
                ?? MatchPending(settlementId);
        }

        /// <summary>查找指定定居点 + 类型的活跃事件。同村可同时存在多种类型（Misconduct + Theft_Animal 等）。
        /// 自动包含 PendingWorldEvent 兜底——调用方无需手动组合双源查找。</summary>
        public static WorldEvent FindActive(string settlementId, EventType type)
        {
            return _allEvents.FirstOrDefault(e =>
                e.TargetSettlementId == settlementId &&
                e.Type == type &&
                e.Stage != EventStage.Resolved &&
                e.Stage != EventStage.Unsolved)
                ?? MatchPending(settlementId, type);
        }

        /// <summary>查找指定定居点的活跃事件，附加自定义谓词过滤。
        /// 自动包含 PendingWorldEvent 兜底——谓词同时作用于持久化事件和 Pending。</summary>
        public static WorldEvent FindActive(string settlementId, Func<WorldEvent, bool> predicate)
        {
            return _allEvents.FirstOrDefault(e =>
                e.TargetSettlementId == settlementId &&
                e.Stage != EventStage.Resolved &&
                e.Stage != EventStage.Unsolved &&
                predicate(e))
                ?? MatchPending(settlementId, predicate: predicate);
        }

        /// <summary>PendingWorldEvent 兜底匹配：Mission 内刚检测到、尚未持久化的事件。</summary>
        private static WorldEvent MatchPending(string settlementId, EventType? type = null,
            Func<WorldEvent, bool> predicate = null)
        {
            var pending = AgentAIController.Instance?.PendingWorldEvent;
            if (pending == null) return null;
            if (pending.TargetSettlementId != settlementId) return null;
            if (pending.Stage == EventStage.Resolved || pending.Stage == EventStage.Unsolved) return null;
            if (type.HasValue && pending.Type != type.Value) return null;
            if (predicate != null && !predicate(pending)) return null;
            return pending;
        }

        /// <summary>按 EventId 查找</summary>
        public static WorldEvent Find(string eventId)
        {
            return _allEvents.FirstOrDefault(e => e.EventId == eventId);
        }

        /// <summary>按定居点查找所有事件（含已解决的）</summary>
        public static List<WorldEvent> FindBySettlement(string settlementId)
        {
            return _allEvents.Where(e => e.TargetSettlementId == settlementId).ToList();
        }

        /// <summary>获取指定 Hero 作为作案者的活跃事件</summary>
        public static List<WorldEvent> FindByInitiator(string heroId)
        {
            return _allEvents.Where(e => e.InitiatorId == heroId
                && e.Stage != EventStage.Resolved && e.Stage != EventStage.Unsolved).ToList();
        }

        /// <summary>添加或合并事件</summary>
        public static void AddOrMerge(WorldEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.EventId)) return;

            // 同定居点 + 同类型 + 同嫌疑人 + 同子场景 → 合并（场景不同则不合并）
            var existing = _allEvents.FirstOrDefault(e =>
                e.TargetSettlementId == evt.TargetSettlementId &&
                e.Type == evt.Type &&
                e.Stage != EventStage.Resolved &&
                e.Stage != EventStage.Unsolved &&
                e.SuspectHeroId == evt.SuspectHeroId &&
                e.LocationName == evt.LocationName);
            if (existing != null)
            {
                // 续档事件已就地更新（PendingWorldEvent 复用 store 中的同一对象）——自合并会双倍累计，直接跳过
                if (ReferenceEquals(existing, evt)) return;
                MergeWitnessTestimonies(existing, evt);
                return;
            }

            // 村庄警觉标记：前案 Resolved 且嫌犯=玩家 → 新案 PublicAwareness 起始 +0.3
            if (_villageAlertFlags.TryGetValue(evt.TargetSettlementId, out bool alert) && alert)
            {
                evt.PublicAwareness = 0.3f;
                DebugLogger.Log($"[WorldEvent] Village alert active for {evt.TargetSettlementId} — starting awareness={evt.PublicAwareness}");
            }

            _allEvents.Add(evt);
            // 初始化阶段进入日：TransitionStage 只在阶段迁移时被调用，不会为初始 Stage 赋值。
            if (evt._stageEnteredDay == 0f)
                evt._stageEnteredDay = evt.OccurredDay;

            var itemDesc = string.Join(", ", evt.StolenItems.Select(kv => $"{kv.Key}x{kv.Value}"));
            DebugLogger.Log($"[WorldEvent] New event: {evt.Type} id={evt.EventId} settlement={evt.TargetSettlementId} items=[{itemDesc}] culprit={evt.InitiatorId} stage={evt.Stage} suspect={evt.SuspectHeroId ?? "none"} witnesses={evt.WitnessHeroIds?.Count ?? 0}h+{(evt.TemplateWitness?.Sum(kv => kv.Value) ?? 0)}v severity={evt.Severity} occurredDay={evt.OccurredDay:F2}");
        }

        /// <summary>
        /// 将 incoming 合并进 existing：证词、Stage（取高）、Severity（累加）、LastUpdateDay。
        /// </summary>
        static void MergeWitnessTestimonies(WorldEvent existing, WorldEvent incoming)
        {
            if (incoming.WitnessTestimonies == null) return;
            existing.WitnessTestimonies = existing.WitnessTestimonies ?? new List<WitnessTestimony>();
            foreach (var inc in incoming.WitnessTestimonies)
            {
                bool isDark = inc.WitnessHeroId == null && inc.TemplateId == null; // 系统暗账
                var match = existing.WitnessTestimonies.FirstOrDefault(t =>
                    (inc.WitnessHeroId != null && t.WitnessHeroId == inc.WitnessHeroId) ||
                    (inc.TemplateId != null && t.TemplateId == inc.TemplateId) ||
                    (isDark && t.WitnessHeroId == null && t.TemplateId == null)); // 暗账归一
                if (match != null)
                {
                    // 同目击者：合并 Actions（同名 ActionType 累加 AlertValue）
                    match.Actions = match.Actions ?? new List<ActionRecord>();
                    if (inc.Actions != null)
                    {
                        foreach (var act in inc.Actions)
                        {
                            // Steal 记录条数 = 赃物数量（StolenItems 按条计数），
                            // 按 ActionType 合并会丢失数量/物品 → 暗账与偷窃记录一律原样追加
                            if (isDark || act.ActionType == "Steal")
                            {
                                match.Actions.Add(act);
                                continue;
                            }
                            var existingAct = match.Actions.FirstOrDefault(a => a.ActionType == act.ActionType);
                            if (existingAct != null)
                                existingAct.AlertValue += act.AlertValue;
                            else
                                match.Actions.Add(act);
                        }
                    }
                }
                else
                {
                    existing.WitnessTestimonies.Add(inc);
                }
            }

            // ── 事件级元数据合并 ──
            existing.Stage = (EventStage)Math.Max((int)existing.Stage, (int)incoming.Stage);
            existing.Severity = Math.Min(100, existing.Severity + incoming.Severity / 2);
            existing.LastUpdateDay = (float)CampaignTime.Now.ToDays;

            // 袭击记账合并：身价累计相加，名单去重并入
            if (incoming.AssaultValue > 0)
            {
                existing.AssaultValue += incoming.AssaultValue;
                if (incoming.AssaultVictimNames != null)
                {
                    existing.AssaultVictimNames = existing.AssaultVictimNames ?? new List<string>();
                    foreach (var n in incoming.AssaultVictimNames)
                        if (!string.IsNullOrEmpty(n) && !existing.AssaultVictimNames.Contains(n))
                            existing.AssaultVictimNames.Add(n);
                }
            }

            var itemSummary = string.Join(", ", existing.StolenItems.Select(kv => $"{kv.Key}x{kv.Value}"));
            DebugLogger.Log($"[WorldEvent] Merged into existing case {existing.EventId} (totalStolen={existing.TotalStolenCount}, items=[{itemSummary}], stage={existing.Stage})");
        }

        /// <summary>DailyTick 阶段推进</summary>
        public static void ProcessDaily()
        {
            float now = (float)CampaignTime.Now.ToDays;
            var activeEvents = _allEvents.Where(e => e.Stage != EventStage.Resolved && e.Stage != EventStage.Unsolved).ToList();
            DebugLogger.Log($"[WorldEvent] ProcessDaily: {activeEvents.Count} active event(s), day={now:F2}");
            foreach (var evt in activeEvents)
            {
                DebugLogger.Log($"[WorldEvent] ProcessDaily: id={evt.EventId} stage={evt.Stage} suspect={evt.SuspectHeroId ?? "none"} awareness={evt.PublicAwareness:F2} investProgress={evt.InvestigationProgress:F2} occurredDay={evt.OccurredDay:F2} now={now:F2} overnight={(int)now > (int)evt.OccurredDay}");
                evt.LastUpdateDay = now;
                switch (evt.Stage)
                {
                    case EventStage.Dormant:
                        ProcessDormant(evt, now);
                        break;
                    case EventStage.Emerging:
                        ProcessEmerging(evt, now);
                        break;
                    case EventStage.Active:
                        ProcessActive(evt, now);
                        break;
                    case EventStage.Confrontation:
                        ProcessConfrontation(evt, now);
                        break;
                }
            }

            // 清理冷案中的旧事件（保留最近 50 条 Resolved/Unsolved）
            var resolved = _allEvents.Where(e => e.Stage == EventStage.Resolved || e.Stage == EventStage.Unsolved).ToList();
            while (resolved.Count > 50)
            {
                var oldest = resolved.OrderBy(e => e.LastUpdateDay).First();
                _allEvents.Remove(oldest);
                resolved.Remove(oldest);
            }
        }

        private static void ProcessDormant(WorldEvent evt, float now)
        {
            var cfg = evt.Config;
            if (cfg == null) return;

            // 检查发现条件
            bool discovered;
            if (cfg.DiscoveryCheck != null)
                discovered = cfg.DiscoveryCheck(evt);
            else
                discovered = (int)now > (int)evt.OccurredDay;  // 过夜即发现（跨过午夜）

            if (discovered)
            {
                evt.PublicAwareness = Math.Max(0.1f, evt.PublicAwareness);  // 保底0.1，不覆盖已有警觉加成
                TransitionStage(evt, EventStage.Emerging);
            }
        }

        private static void ProcessEmerging(WorldEvent evt, float now)
        {
            // 喂给传播系统（仅一次）
            if (!evt.WasBroadcast)
            {
                try { SocialEventManager.BroadcastWorldEvent(evt); }
                catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Broadcast failed: {ex.Message}"); }
                evt.WasBroadcast = true;
            }

            // 每日推进 PublicAwareness
            evt.PublicAwareness += InvestigationEngine.GetDailySpreadRate(evt);

            // 每日推进调查
            InvestigationEngine.AdvanceInvestigation(evt);

            // 进度满 → 锁定嫌犯
            if (evt.InvestigationProgress >= 1.0f && evt.SuspectHeroId == null)
                InvestigationEngine.TryLockSuspect(evt);

            // 超时 → 不了了之
            var cfg = evt.Config;
            float coldDays = cfg?.InvestigationWindowDays ?? 7;
            if ((now - evt.OccurredDay) > coldDays && evt.InvestigationProgress < 1.0f)
            {
                TransitionStage(evt, EventStage.Unsolved);
            }

            // 权威 NPC 自主行动
            InvestigationEngine.ProcessAuthorityAction(evt);
        }

        private static void ProcessActive(WorldEvent evt, float now)
        {
            // ── 干活抵债每日跟踪 ──
            if (evt._workOffDebtAccepted && !evt.PlayerPaidRestitution)
            {
                // 检查玩家今天是否在目标村庄
                if (Hero.MainHero.CurrentSettlement?.StringId == evt.TargetSettlementId)
                {
                    evt._workOffDaysDone++;
                    DebugLogger.Log($"[WorkOffDebt] {evt.EventId} Day {evt._workOffDaysDone}/3: Player at {evt.TargetSettlementId}");
                }

                float daysPassed = now - evt._workOffDebtDay;
                if (daysPassed >= 3f)
                {
                    if (evt._workOffDaysDone >= 3)
                    {
                        // 履约 → 结案
                        OnPlayerPaidRestitution(evt);
                        evt.ResolvedBy = "work_off_debt";
                        DebugLogger.Log($"[WorkOffDebt] {evt.EventId} Completed — debt worked off");
                    }
                    else
                    {
                        // 违约 → Trust -20, Confrontation
                        var authority = GetAuthorityNpc(evt);
                        if (authority != null)
                            TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyPlayerRelation(authority, -20, false, true);
                        TransitionStage(evt, EventStage.Confrontation, null, "你没干完答应的活");
                        InvestigationEngine.SpawnRetaliationParty(evt);
                        DebugLogger.Log($"[WorkOffDebt] {evt.EventId} Breached! Only {evt._workOffDaysDone}/3 days. → Confrontation");
                    }
                    return;
                }
            }

            float deadline = evt.SuspectIsPlayer ? 10f : 15f;
            if ((now - evt._stageEnteredDay) > deadline && !evt.PlayerPaidRestitution && !evt._workOffDebtAccepted)
            {
                TransitionStage(evt, EventStage.Confrontation, null, "你一直拖着不给钱，他们不等了");
                InvestigationEngine.SpawnRetaliationParty(evt);
            }
            InvestigationEngine.ProcessAuthorityAction(evt);
        }

        private static void ProcessConfrontation(WorldEvent evt, float now)
        {
            if (!evt.RetaliationSpawned)
            {
                if (evt.RetaliationBudget > 0 && !evt.PermanentEnemy)
                    InvestigationEngine.CheckBudgetAndRespawn(evt);
                else
                {
                    TransitionStage(evt, EventStage.Resolved);
                    evt.ResolvedBy = "budget_depleted";
                }
            }
            else if ((now - evt.RetaliationSpawnDay) > 15f)
            {
                evt.RetaliationSpawned = false;
                evt.RetaliationPartyId = null;
                TransitionStage(evt, EventStage.Resolved);
                evt.ResolvedBy = "timeout";
            }
        }

        /// <summary>
        /// 阶段迁移（唯一入口）。
        /// 进入 Active 时必须传入 suspectHeroId；进入 Dormant/Emerging 时强制清空 SuspectHeroId。
        /// </summary>
        /// <param name="suspectHeroId">进入 Active/Confrontation 时锁定此 Hero 为嫌疑人。null 时尝试从 InitiatorId 推断。</param>
        /// <param name="escalationReason">
        /// **玩家自己干了什么导致这次升级**（"你转身就走，没给钱" / "你拔剑动手"）。
        /// 升级会把赔款金额往上顶（CrimePenaltyCalculator.ComputeRestitution 的阶段倍率），
        /// 所以每个玩家驱动的迁移都该带上原因 —— 事后收钱的界面要拿它跟玩家把账算清。
        /// 不传则按阶段落一句兜底原因。
        /// </param>
        public static void TransitionStage(WorldEvent evt, EventStage newStage, string suspectHeroId = null,
            string escalationReason = null)
        {
            if (evt.Stage == newStage) return;
            var oldStage = evt.Stage;

            // ── 阶段不变式守卫：SuspectHeroId 与 Stage 必须一致 ──
            switch (newStage)
            {
                case EventStage.Dormant:
                case EventStage.Emerging:
                    // 未锁定嫌疑人阶段：强制清空 SuspectHeroId
                    if (evt.SuspectHeroId != null)
                    {
                        DebugLogger.Log($"[WorldEvent] {evt.EventId} clearing SuspectHeroId={evt.SuspectHeroId} (entering {newStage})");
                        evt.SuspectHeroId = null;
                    }
                    break;
                case EventStage.Active:
                    // 嫌疑人锁定阶段：优先用传入值，否则从 InitiatorId 推断
                    if (!string.IsNullOrEmpty(suspectHeroId))
                    {
                        evt.SuspectHeroId = suspectHeroId;
                    }
                    else if (string.IsNullOrEmpty(evt.SuspectHeroId))
                    {
                        evt.SuspectHeroId = InferSuspect(evt);
                        DebugLogger.Log($"[WorldEvent] {evt.EventId} auto-assigned SuspectHeroId={evt.SuspectHeroId} (entering Active, no explicit suspect)");
                    }
                    break;
                case EventStage.Confrontation:
                    // Confrontation：如果传了 suspect 则更新
                    if (!string.IsNullOrEmpty(suspectHeroId))
                        evt.SuspectHeroId = suspectHeroId;
                    break;
                // Resolved / Unsolved：不约束 SuspectHeroId
            }

            evt.Stage = newStage;
            evt._stageEnteredDay = (float)CampaignTime.Now.ToDays;

            // ── 报价台账：阶段升级 = 赔款涨价，留下"是哪一步把价钱抬上去的" ──
            // 只在玩家已经听过价之后才记（RecordPriceEscalation 内部自己守卫）。
            if (newStage > oldStage && newStage <= EventStage.Confrontation)
                evt.RecordPriceEscalation(escalationReason ?? DefaultEscalationReason(newStage));

            // Resolved 时设置村庄警觉
            if (newStage == EventStage.Resolved && evt.SuspectIsPlayer)
            {
                _villageAlertFlags[evt.TargetSettlementId] = true;
            }

            // 过夜被发现（Dormant→Emerging）：通知作案玩家——村民知道丢了什么，还不知道是谁
            if (oldStage == EventStage.Dormant && newStage == EventStage.Emerging && evt.InitiatorIsPlayer)
            {
                try { WorldEventNotificationController.OnCrimeDiscovered(evt); }
                catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Discovery notify error: {ex.Message}"); }
            }

            // 冷案尾巴（一次性 15%）：村民迁怒打错人 mini-event，仅犯罪案件
            if (newStage == EventStage.Unsolved && evt.Category == EventCategory.Crime && !evt._coldCaseTailRolled)
            {
                evt._coldCaseTailRolled = true;
                if (new Random().Next(0, 100) < 15)
                {
                    evt._coldCaseTailTriggered = true;
                    try { InvestigationEngine.TriggerVigilanteJustice(evt); }
                    catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Vigilante tail error: {ex.Message}"); }
                }
            }

            DebugLogger.Log($"[WorldEvent] {evt.EventId} Stage: {oldStage} → {newStage}");
            OnEventStageChanged?.Invoke(evt);
        }

        /// <summary>
        /// 阶段升级的兜底涨价原因（调用方没给具体原因时用）。
        /// 措辞一律用"你…"—— 涨价是玩家自己的选择造成的，不是系统涨价。
        /// </summary>
        private static string DefaultEscalationReason(EventStage newStage)
        {
            switch (newStage)
            {
                case EventStage.Emerging:      return "事情被发现了";
                case EventStage.Active:        return "你没把这笔钱给出去，事情被摆上了明面";
                case EventStage.Confrontation: return "你跟他们动了手";
                default:                       return "事情又闹大了一层";
            }
        }

        /// <summary>
        /// 推断嫌疑人。优先级：
        /// 1. InitiatorId（Misconduct 事件始终是玩家）
        /// 2. TargetHeroId（受害者相关的敌对英雄）
        /// 3. 当前玩家
        /// </summary>
        private static string InferSuspect(WorldEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.InitiatorId))
                return evt.InitiatorId;
            if (!string.IsNullOrEmpty(evt.TargetHeroId))
                return evt.TargetHeroId;
            return Hero.MainHero?.StringId ?? "player";
        }

        /// <summary>玩家赔钱 → 结案</summary>
        public static void OnPlayerPaidRestitution(WorldEvent evt)
        {
            evt.PlayerPaidRestitution = true;
            TransitionStage(evt, EventStage.Resolved);
            evt.ResolvedBy = "payment";
        }

        /// <summary>Charm 辩护成功 → 嫌犯降级</summary>
        public static void OnCharmReprieve(WorldEvent evt)
        {
            evt.CharmReprieveUsed = true;
            evt.InvestigationProgress = 0.5f;
            evt.SuspectHeroId = null;
            evt.PublicAwareness = 0.5f;
            TransitionStage(evt, EventStage.Emerging);
        }

        /// <summary>嫌犯被交付 → 结案</summary>
        public static void OnSuspectDelivered(WorldEvent evt)
        {
            TransitionStage(evt, EventStage.Resolved);
            evt.ResolvedBy = "captured";
        }

        /// <summary>
        /// 玩家被当地人制服/扣押 → 结案。
        ///
        /// 与 OnSuspectDelivered 的区别：嫌犯就是玩家本人，且案件靠"吃罚金/坐几天"了结，
        /// 因此必须同时解除敌对状态（PermanentEnemy）并清掉报复经费和报复部队——
        /// 否则玩家出狱后仍会被同一批人无限追杀。
        /// </summary>
        /// <param name="paidFine">true = 交了罚金；false = 关满刑期后放出</param>
        public static void OnPlayerDetained(WorldEvent evt, bool paidFine)
        {
            if (evt == null) return;

            if (paidFine)
                evt.PlayerPaidRestitution = true;

            // 恩怨了结：撤销永久敌对 + 掏空报复经费
            evt.PermanentEnemy = false;
            evt.RetaliationBudget = 0;

            TransitionStage(evt, EventStage.Resolved);
            evt.ResolvedBy = paidFine ? "payment" : "detained";
            evt.LastUpdateDay = (float)CampaignTime.Now.ToDays;

            // 撤掉已经在路上的报复部队
            RemoveEventParty(evt);
            evt.RetaliationSpawned = false;
            evt.RetaliationPartyId = null;

            if (evt.TargetSettlement != null)
            {
                try { WorldEventSimulator.ModifyStability(evt.TargetSettlement, +1); }
                catch (Exception ex) { DebugLogger.Log($"[WorldEvent] ModifyStability error: {ex.Message}"); }
            }

            try { WorldEventNotificationController.OnEventResolved(evt); }
            catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Notification error: {ex.Message}"); }

            ClearEventFromNpcMemory(evt);

            DebugLogger.Log($"[WorldEvent] OnPlayerDetained: id={evt.EventId} paidFine={paidFine} → Resolved");
        }

        /// <summary>威胁成功 → 结案</summary>
        public static void OnIntimidated(WorldEvent evt)
        {
            TransitionStage(evt, EventStage.Resolved);
            evt.ResolvedBy = "intimidated";
        }

        /// <summary>报复部队被击败 — 不结案，扣经费</summary>
        public static void OnRetaliationDefeated(WorldEvent evt)
        {
            evt.RetaliationSpawned = false;
            evt.RetaliationPartyId = null;
            InvestigationEngine.CheckBudgetAndRespawn(evt);
        }

        /// <summary>获取权威 NPC（村长/族长/领主）</summary>
        public static Hero GetAuthorityNpc(WorldEvent evt)
        {
            var settlement = evt.TargetSettlement;
            if (settlement == null) return null;

            var cfg = evt.Config;
            if (cfg?.AuthorityRole == "领主" || cfg?.AuthorityRole == "族长")
            {
                // 领主/族长 = 定居点所属家族领袖
                var owner = settlement.OwnerClan?.Leader;
                if (owner != null) return owner;
            }

            // 默认：村长 = Headman notable
            return settlement.Notables?.FirstOrDefault(n =>
                n.Occupation == Occupation.Headman || n.Occupation == Occupation.RuralNotable);
        }

        /// <summary>初始化报复经费：Headman Gold + 村庄繁荣度折算</summary>
        public static int SeedRetaliationBudget(WorldEvent evt)
        {
            var headman = GetAuthorityNpc(evt);
            int headmanGold = headman?.Gold ?? 0;
            int hearthBonus = 0;
            if (evt.TargetSettlement?.Village != null)
                hearthBonus = (int)(evt.TargetSettlement.Village.Hearth * 0.5f);
            return Math.Max(500, headmanGold + hearthBonus);
        }

        /// <summary>添加 AI 模拟事件（不做合并，直接加入）</summary>
        public static void AddEvent(WorldEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.EventId)) return;
            if (_allEvents.Any(e => e.EventId == evt.EventId)) return;

            _allEvents.Add(evt);
            DebugLogger.Log($"[WorldEvent] New AI event: {evt.Type} id={evt.EventId} settlement={evt.TargetSettlementId}");

            try { WorldEventNotificationController.OnEventCreated(evt); }
            catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Notification error: {ex.Message}"); }

            SyncEventToNpcMemory(evt);
        }

        /// <summary>将事件标记为已解决</summary>
        public static void ResolveEvent(string eventId)
        {
            var evt = _allEvents.FirstOrDefault(e => e.EventId == eventId);
            if (evt == null) return;

            evt.Stage = EventStage.Resolved;
            evt.ResolvedBy = "resolved";
            evt.LastUpdateDay = (float)CampaignTime.Now.ToDays;

            RemoveEventParty(evt);
            DebugLogger.Log($"[WorldEvent] Resolved: {evt.Type} id={eventId}");

            if (evt.TargetSettlement != null)
                WorldEventSimulator.ModifyStability(evt.TargetSettlement, +1);

            try { WorldEventNotificationController.OnEventResolved(evt); }
            catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Notification error: {ex.Message}"); }

            ClearEventFromNpcMemory(evt);
        }

        /// <summary>将事件标记为到期未解决</summary>
        public static void ExpireEvent(string eventId)
        {
            var evt = _allEvents.FirstOrDefault(e => e.EventId == eventId);
            if (evt == null) return;

            evt.Stage = EventStage.Unsolved;
            evt.ResolvedBy = "expired";
            evt.LastUpdateDay = (float)CampaignTime.Now.ToDays;

            RemoveEventParty(evt);
            DebugLogger.Log($"[WorldEvent] Expired: {evt.Type} id={eventId}");

            ClearEventFromNpcMemory(evt);
        }

        /// <summary>将事件标记为升级（严重度+10）</summary>
        public static void EscalateEvent(string eventId)
        {
            var evt = _allEvents.FirstOrDefault(e => e.EventId == eventId);
            if (evt == null) return;

            evt.EscalationCount++;
            evt.Severity = Math.Min(100, evt.Severity + 10);
            evt.LastUpdateDay = (float)CampaignTime.Now.ToDays;
            DebugLogger.Log($"[WorldEvent] Escalated to severity {evt.Severity}: {evt.Type} id={eventId}");

            try { WorldEventNotificationController.OnEventEscalated(evt); }
            catch (Exception ex) { DebugLogger.Log($"[WorldEvent] Notification error: {ex.Message}"); }
        }

        /// <summary>清理已被击败的事件 party 或已死亡的 instigator</summary>
        public static void CleanupDefeatedParties()
        {
            var defeated = _allEvents
                .Where(e => e.IsActive)
                .Where(e =>
                {
                    if (!string.IsNullOrEmpty(e.GeneratedPartyId) && e.GeneratedParty == null)
                        return true;
                    if (e.AuxiliaryPartyIds != null && e.AuxiliaryPartyIds.Count > 0
                        && string.IsNullOrEmpty(e.GeneratedPartyId)
                        && e.AuxiliaryPartyIds.All(kvp => e.GetAuxiliaryParty(kvp.Key) == null))
                        return true;
                    if (!string.IsNullOrEmpty(e.InitiatorId) && !e.IsGenericInstigator)
                    {
                        var hero = e.InstigatorHero;
                        if (hero != null && !hero.IsAlive) return true;
                    }
                    return false;
                })
                .ToList();

            foreach (var evt in defeated)
                ResolveEvent(evt.EventId);

            if (defeated.Count > 0)
                DebugLogger.Log($"[WorldEvent] Auto-resolved {defeated.Count} events (party defeated or instigator died)");

            var ended = _allEvents.Where(e => e.Stage == EventStage.Resolved || e.Stage == EventStage.Unsolved)
                .OrderBy(e => e.LastUpdateDay).ToList();
            while (ended.Count > 50)
            {
                _allEvents.Remove(ended[0]);
                ended.RemoveAt(0);
            }
        }

        /// <summary>清理事件关联的 party</summary>
        private static void RemoveEventParty(WorldEvent evt)
        {
            try
            {
                if (evt.AuxiliaryPartyIds != null && evt.AuxiliaryPartyIds.Count > 0)
                {
                    int auxCount = evt.AuxiliaryPartyIds.Count;
                    foreach (var kvp in evt.AuxiliaryPartyIds.ToList())
                    {
                        if (!string.IsNullOrEmpty(kvp.Value))
                        {
                            var auxParty = evt.GetAuxiliaryParty(kvp.Key);
                            if (auxParty != null)
                            {
                                Campaign.Current?.VisualTrackerManager?.RemoveTrackedObject(auxParty, forceRemove: true);
                                V.DelParty(auxParty);
                            }
                        }
                        evt.AuxiliaryPartyIds.Remove(kvp.Key);
                    }
                    DebugLogger.Log($"[WorldEvent] Cleaned up {auxCount} auxiliary parties for {evt.EventId}");
                }

                if (evt.IsRedirectedExistingParty)
                {
                    var party = evt.GeneratedParty;
                    if (party != null)
                    {
                        party.Ai.SetDoNotMakeNewDecisions(false);
                        party.SetPartyUsedByQuest(false);
                        Campaign.Current?.VisualTrackerManager?.RemoveTrackedObject(party, forceRemove: true);
                    }
                }
                else
                {
                    var party = evt.GeneratedParty;
                    if (party != null)
                        Campaign.Current?.VisualTrackerManager?.RemoveTrackedObject(party, forceRemove: true);
                    V.DelParty(party);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] Failed to remove party {evt.GeneratedPartyId}: {ex.Message}");
            }
        }

        #region Query

        public static List<WorldEvent> GetActiveEventsNear(Settlement settlement, float maxDistance = 80f)
        {
            if (settlement == null) return new List<WorldEvent>();
            return _allEvents
                .Where(e => e.IsActive)
                .Where(e =>
                {
                    var target = e.TargetSettlement;
                    if (target == null) return false;
                    return V.Pos(target).Distance(V.Pos(settlement)) < maxDistance;
                })
                .ToList();
        }

        public static List<WorldEvent> GetActiveEventsOfType(EventType type)
        {
            return _allEvents.Where(e => e.IsActive && e.Type == type).ToList();
        }

        public static WorldEvent FindEvent(string eventId)
        {
            return _allEvents.FirstOrDefault(e => e.EventId == eventId);
        }

        public static List<WorldEvent> GetActiveEventsForTarget(string heroStringId)
        {
            if (string.IsNullOrEmpty(heroStringId)) return new List<WorldEvent>();
            return _allEvents.Where(e => e.IsActive && e.TargetHeroId == heroStringId).ToList();
        }

        #endregion

        #region NPC Memory Sync

        private static void SyncEventToNpcMemory(WorldEvent evt)
        {
            // CurrentUrgentEvent 将在 SingNpcMemorySystem 后续迁移中适配新模型
        }

        private static void ClearEventFromNpcMemory(WorldEvent evt)
        {
            // CurrentUrgentEvent 将在 SingNpcMemorySystem 后续迁移中适配新模型
        }

        #endregion

        #region Persistence (WorldEventStore)

        public static string Serialize()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "events", _allEvents },
                    { "alerts", _villageAlertFlags }
                };
                return JsonConvert.SerializeObject(data, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventStore] Serialize error: {ex.Message}");
                return "{}";
            }
        }

        public static void Deserialize(string json)
        {
            _allEvents.Clear();
            _villageAlertFlags.Clear();

            if (string.IsNullOrEmpty(json) || json == "{}") return;

            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (dict == null) return;

                if (dict.TryGetValue("events", out var events))
                    _allEvents = JsonConvert.DeserializeObject<List<WorldEvent>>(events.ToString())
                        ?? new List<WorldEvent>();

                if (dict.TryGetValue("alerts", out var alerts))
                    _villageAlertFlags = JsonConvert.DeserializeObject<Dictionary<string, bool>>(alerts.ToString())
                        ?? new Dictionary<string, bool>();

                DebugLogger.Log($"[WorldEventStore] Deserialized {_allEvents.Count} events, {_villageAlertFlags.Count} alerts");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventStore] Deserialize error: {ex.Message}");
                _allEvents = new List<WorldEvent>();
                _villageAlertFlags = new Dictionary<string, bool>();
            }
        }

        #endregion
    }

    #endregion

    #region TheftLedger

    /// <summary>
    /// 统一偷窃账本。全局存储所有 TheftTruthRecord，同时服务：
    /// ① WorldEvent 层（按 EventId 查询该事件的所有偷窃记录）
    /// ② 背包 UI（GetSourceTag — 标注赃物来源）
    /// ③ 栽赃系统（GetFrameableTargets / GetEvidenceItems — 栽赃候选+证物）
    /// ④ 序列化（SyncData 持久化）
    /// </summary>
    public static class TheftLedger
    {
        private static List<TheftTruthRecord> _records = new List<TheftTruthRecord>();

        /// <summary>记录一次偷窃</summary>
        public static TheftTruthRecord Record(string initiatorId, string victimHeroId, string settlementId,
            string itemId, int count, string locationName, string worldEventId = null)
        {
            var record = new TheftTruthRecord
            {
                InitiatorId = initiatorId,
                VictimHeroId = victimHeroId,
                VictimSettlementId = settlementId,
                ItemId = itemId,
                Count = count,
                Day = (float)CampaignTime.Now.ToDays,
                LocationName = locationName ?? "",
                IsCleared = false,
                WorldEventId = worldEventId,
            };
            _records.Add(record);
            DebugLogger.Log($"[TheftLedger] Recorded: {itemId} x{count} by {initiatorId} from {victimHeroId ?? settlementId}");
            return record;
        }

        /// <summary>当案件解决时清除对应定居点的所有未清记录</summary>
        public static void MarkCleared(string settlementId)
        {
            foreach (var r in _records.Where(r => r.VictimSettlementId == settlementId && !r.IsCleared))
                r.IsCleared = true;
        }

        /// <summary>获取指定 WorldEvent 的所有偷窃记录</summary>
        public static List<TheftTruthRecord> GetByEventId(string worldEventId)
        {
            return _records.Where(r => r.WorldEventId == worldEventId).ToList();
        }

        /// <summary>返回栽赃候选名单（含"强盗"默认项 + 有证物的受害者）</summary>
        public static List<FrameSubOption> GetFrameableTargets()
        {
            var candidates = new List<FrameSubOption>
            {
                new FrameSubOption
                {
                    TargetId = "bandit",
                    DisplayName = "附近藏身处的强盗",
                    BaseDC = 40,
                    CanShowEvidence = false,
                    IsPowerful = false,
                }
            };

            foreach (var record in _records.Where(r => !r.IsCleared && r.VictimHeroId != null))
            {
                var itemObj = MBObjectManager.Instance.GetObject<ItemObject>(record.ItemId);
                if (itemObj == null) continue;
                if (MobileParty.MainParty?.ItemRoster.GetItemNumber(itemObj) < 1) continue;
                var victim = Hero.FindFirst(h => h.StringId == record.VictimHeroId);
                if (victim == null) continue;

                candidates.Add(new FrameSubOption
                {
                    TargetId = record.VictimHeroId,
                    DisplayName = victim.Name.ToString(),
                    BaseDC = ComputeBaseDC(victim),
                    CanShowEvidence = true,
                    IsPowerful = victim.IsLord || victim.IsMerchant,
                });
            }
            return candidates;
        }

        private static int ComputeBaseDC(Hero target)
        {
            if (target.IsLord) return 85;
            if (target.IsMerchant) return 70;
            if (target.IsWanderer) return 35;
            if (target.Occupation == Occupation.Headman || target.Occupation == Occupation.RuralNotable) return 55;
            return 55;
        }

        /// <summary>
        /// 获取某物品在指定持有者背包中的赃物来源标注。
        /// 按来源聚合所有未清记录，每个来源标注各自数量。
        /// 例："⚠ 偷自 特维亚×1, 曹村×2"
        /// </summary>
        public static string GetSourceTag(string itemId, string ownerHeroId)
        {
            var records = _records.Where(r =>
                r.ItemId == itemId && !r.IsCleared && r.InitiatorId == ownerHeroId).ToList();
            if (records.Count == 0) return null;

            // 按来源聚合：同一 VictimHeroId 或 LocationName 合并数量
            var bySource = new Dictionary<string, int>();
            foreach (var r in records)
            {
                string key;
                if (!string.IsNullOrEmpty(r.VictimHeroId))
                {
                    var victim = Hero.FindFirst(h => h.StringId == r.VictimHeroId);
                    key = victim != null ? victim.Name.ToString() : r.VictimHeroId;
                }
                else
                {
                    key = r.LocationName;
                }
                bySource.TryGetValue(key, out int cur);
                bySource[key] = cur + r.Count;
            }

            var parts = bySource.Select(kv => $"{kv.Key}×{kv.Value}");
            return $"⚠ 偷自 {string.Join(", ", parts)}";
        }

        /// <summary>某英雄是否在账本中有未清记录</summary>
        public static bool HasRecordFor(string heroId)
        {
            return _records.Any(r => r.VictimHeroId == heroId && !r.IsCleared);
        }

        /// <summary>
        /// 获取可用于栽赃某英雄的证据物品列表（玩家背包仍持有且未清除的赃物）。
        /// 用于在 FrameSuspectIntent 中展开"出示哪件证物"子选项。
        /// </summary>
        public static List<EvidenceItem> GetEvidenceItems(string heroId)
        {
            var items = new List<EvidenceItem>();
            foreach (var record in _records.Where(r => r.VictimHeroId == heroId && !r.IsCleared))
            {
                var itemObj = MBObjectManager.Instance.GetObject<ItemObject>(record.ItemId);
                if (itemObj == null) continue;
                if (MobileParty.MainParty?.ItemRoster.GetItemNumber(itemObj) < 1) continue;
                items.Add(new EvidenceItem
                {
                    ItemId = record.ItemId,
                    ItemName = itemObj?.Name?.ToString() ?? record.ItemId,
                    LocationName = record.LocationName,
                    StolenDay = record.Day,
                });
            }
            return items;
        }

        #region Persistence

        public static string Serialize()
        {
            try { return JsonConvert.SerializeObject(_records, Formatting.None); }
            catch { return "[]"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                _records = JsonConvert.DeserializeObject<List<TheftTruthRecord>>(json) ?? new List<TheftTruthRecord>();
            }
            catch { _records = new List<TheftTruthRecord>(); }
        }

        #endregion
    }

    /// <summary>栽赃子选项数据</summary>
    public class FrameSubOption
    {
        public string TargetId;
        public string DisplayName;
        public int BaseDC;
        public bool CanShowEvidence;
        public bool IsPowerful;
    }

    /// <summary>单件证物——用于"出示哪件赃物"子选项展开</summary>
    public class EvidenceItem
    {
        public string ItemId;
        public string ItemName;          // "匕首" / "银戒指"
        public string LocationName;      // "曹村" — 偷自哪里
        public float StolenDay;
    }

    #endregion
}

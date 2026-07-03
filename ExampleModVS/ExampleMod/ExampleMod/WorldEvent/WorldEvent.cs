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
        public string TargetItemId;
        public int Quantity;
        public float OccurredDay;
        public string LocationName;        // "牲口圈" / "村口大路" — UI 叙事用

        // ═══ 目击（发生时记录） ═══
        public List<string> WitnessHeroIds;
        public Dictionary<string, int> TemplateWitness;  // 没脸村民模板→人数
        public bool WitnessesSilenced;

        [JsonIgnore]
        public int WitnessCount => (WitnessHeroIds?.Count ?? 0)
                                 + (WitnessesSilenced ? 0 : (TemplateWitness?.Values.Sum() ?? 0));

        // ═══ 公共认知（随调查演进） ═══
        public float PublicAwareness;      // 0→1
        public string SuspectHeroId;       // 嫌犯（null=未知）

        // ═══ 玩家介入 ═══
        public bool CharmReprieveUsed;
        public int FailCount;
        public bool PlayerPaidRestitution;
        public bool PlayerTookInvestigationQuest;
        public bool PlayerTookBountyQuest;

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
        [JsonIgnore] public float _workOffDebtDay;
        [JsonIgnore] public bool _workOffDebtAccepted;
        [JsonIgnore] public int _workOffDaysDone;

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

        /// <summary>获取辅助部队</summary>
        public MobileParty GetAuxiliaryParty(string roleTag)
        {
            if (AuxiliaryPartyIds == null || string.IsNullOrEmpty(roleTag)) return null;
            if (!AuxiliaryPartyIds.TryGetValue(roleTag, out string pid) || string.IsNullOrEmpty(pid)) return null;
            foreach (var mp in Campaign.Current?.MobileParties ?? Enumerable.Empty<MobileParty>())
                if (mp.StringId == pid) return mp;
            return null;
        }

        /// <summary>计算赔偿金额：基础物品价值 × 赔偿倍数（阶段不同倍数不同）</summary>
        public int ComputeRestitutionCost(EventStage? forStage = null)
        {
            var stage = forStage ?? Stage;
            var cfg = Config;
            if (cfg == null) return 100;

            int baseValue = 0;
            if (!string.IsNullOrEmpty(TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(TargetItemId);
                if (item != null) baseValue = item.Value * Quantity;
            }
            if (baseValue <= 0) baseValue = Severity * 10;

            float multiplier = stage switch
            {
                EventStage.Active => cfg.BaseRestitutionMultiplier,
                EventStage.Confrontation => cfg.BaseRestitutionMultiplier * 1.7f,
                _ => cfg.BaseRestitutionMultiplier * 0.7f  // Emerging / caught-in-act
            };

            // Trade skill discount (max 15%)
            float tradeDiscount = 1f - Math.Min(0.15f, Hero.MainHero.GetSkillValue(DefaultSkills.Trade) * 0.0005f);
            return (int)(baseValue * multiplier * tradeDiscount);
        }

        /// <summary>赔偿金额的明细解释（给玩家看为什么是这个数）</summary>
        public string GetRestitutionBreakdown()
        {
            var cfg = Config;
            if (cfg == null) return "赔100第纳尔。";

            string itemDesc = "东西";
            int baseValue = 0;
            if (!string.IsNullOrEmpty(TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(TargetItemId);
                if (item != null)
                {
                    baseValue = item.Value * Quantity;
                    itemDesc = item.Name?.ToString() ?? "东西";
                }
            }
            if (baseValue <= 0) baseValue = Severity * 10;

            int total = ComputeRestitutionCost();
            string crimeGerund = cfg.CrimeVerbGerund ?? "犯事";

            if (Stage <= EventStage.Emerging)
                return $"那只{itemDesc}，市值{baseValue}第纳尔。既然你自己认了，赔{total}第纳尔，这事就算了。你认不认？";
            else
                return $"那只{itemDesc}，市值{baseValue}第纳尔。{crimeGerund}按规矩要赔{total}第纳尔。你认不认？";
        }

        /// <summary>当场被抓时的赔偿（×2 而非 ×3）</summary>
        public int ComputeOnSpotCost()
        {
            var cfg = Config;
            if (cfg == null) return 100;
            int baseValue = 0;
            if (!string.IsNullOrEmpty(TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(TargetItemId);
                if (item != null) baseValue = item.Value * Quantity;
            }
            if (baseValue <= 0) baseValue = Severity * 10;
            return baseValue * 2;
        }

        /// <summary>获取悬赏金额</summary>
        public int ComputeBountyAmount()
        {
            var cfg = Config;
            if (cfg == null) return 500;
            return cfg.BaseBountyPerUnit * Math.Max(1, Quantity);
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

        /// <summary>查找指定定居点的活跃事件（同村同时最多一个活跃案件）</summary>
        public static WorldEvent FindActive(string settlementId)
        {
            return _allEvents.FirstOrDefault(e =>
                e.TargetSettlementId == settlementId &&
                e.Stage != EventStage.Resolved &&
                e.Stage != EventStage.Unsolved);
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

            // 同村有活跃案件 → 合并
            var existing = FindActive(evt.TargetSettlementId);
            if (existing != null && existing.Type == evt.Type)
            {
                existing.Quantity += evt.Quantity;
                // 合并目击者
                if (evt.WitnessHeroIds != null)
                {
                    foreach (var w in evt.WitnessHeroIds)
                        if (!existing.WitnessHeroIds.Contains(w))
                            existing.WitnessHeroIds.Add(w);
                }
                if (evt.TemplateWitness != null)
                {
                    foreach (var kv in evt.TemplateWitness)
                    {
                        existing.TemplateWitness.TryGetValue(kv.Key, out int cur);
                        existing.TemplateWitness[kv.Key] = cur + kv.Value;
                    }
                }
                // 不重置调查进度——村民已经在查了
                existing.LastUpdateDay = (float)CampaignTime.Now.ToDays;
                DebugLogger.Log($"[WorldEvent] Merged theft into existing case {existing.EventId} (qty={existing.Quantity})");
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
            // 事件直接创建为 Active 或更高阶段时（如有目击偷窃），_stageEnteredDay 默认为 0
            // 会导致 ProcessActive 立即触发 Confrontation（now - 0 >> deadline）。
            if (evt._stageEnteredDay == 0f)
                evt._stageEnteredDay = evt.OccurredDay;
            DebugLogger.Log($"[WorldEvent] New event: {evt.Type} id={evt.EventId} settlement={evt.TargetSettlementId} item={evt.TargetItemId} culprit={evt.InitiatorId} stage={evt.Stage} suspect={evt.SuspectHeroId ?? "none"} witnesses={evt.WitnessHeroIds?.Count ?? 0}h+{(evt.TemplateWitness?.Sum(kv => kv.Value) ?? 0)}v severity={evt.Severity} occurredDay={evt.OccurredDay:F2}");
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
                    case EventStage.Unsolved:
                        ProcessUnsolved(evt, now);
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
                evt.Stage = EventStage.Emerging;
                evt.PublicAwareness = Math.Max(0.1f, evt.PublicAwareness);  // 保底0.1，不覆盖已有警觉加成
                evt._stageEnteredDay = now;
                DebugLogger.Log($"[WorldEvent] {evt.EventId} Stage → Emerging (discovered)");
                OnEventStageChanged?.Invoke(evt);
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
                evt.Stage = EventStage.Unsolved;
                evt._stageEnteredDay = now;
                DebugLogger.Log($"[WorldEvent] {evt.EventId} Stage → Unsolved (cold case, {coldDays} days)");
                OnEventStageChanged?.Invoke(evt);
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
                        TransitionStage(evt, EventStage.Confrontation);
                        InvestigationEngine.SpawnRetaliationParty(evt);
                        DebugLogger.Log($"[WorkOffDebt] {evt.EventId} Breached! Only {evt._workOffDaysDone}/3 days. → Confrontation");
                    }
                    return;
                }
            }

            float deadline = evt.SuspectIsPlayer ? 10f : 15f;
            if ((now - evt._stageEnteredDay) > deadline && !evt.PlayerPaidRestitution && !evt._workOffDebtAccepted)
            {
                TransitionStage(evt, EventStage.Confrontation);
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
                    evt.Stage = EventStage.Resolved;
                    evt.ResolvedBy = "budget_depleted";
                    DebugLogger.Log($"[WorldEvent] {evt.EventId} Stage → Resolved (retaliation budget exhausted)");
                    OnEventStageChanged?.Invoke(evt);
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

        private static void ProcessUnsolved(WorldEvent evt, float now)
        {
            // 冷案尾巴：15% 概率触发迁怒 mini-event
            if (!evt._coldCaseTailTriggered && new Random().Next(0, 100) < 15)
            {
                evt._coldCaseTailTriggered = true;
                InvestigationEngine.TriggerVigilanteJustice(evt);
            }
        }

        /// <summary>阶段迁移（唯一入口）</summary>
        public static void TransitionStage(WorldEvent evt, EventStage newStage)
        {
            if (evt.Stage == newStage) return;
            evt.Stage = newStage;
            evt._stageEnteredDay = (float)CampaignTime.Now.ToDays;

            // Resolved 时设置村庄警觉
            if (newStage == EventStage.Resolved && evt.SuspectIsPlayer)
            {
                _villageAlertFlags[evt.TargetSettlementId] = true;
            }

            DebugLogger.Log($"[WorldEvent] {evt.EventId} Stage: {evt.Stage} → {newStage}");
            OnEventStageChanged?.Invoke(evt);
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

    #region PlayerTheftLedger

    /// <summary>一次偷窃的账本条目</summary>
    [Serializable]
    public class TheftRecord
    {
        public string VictimHeroId;         // 扒窃来源 hero；偷动物则为 null
        public string VictimSettlementId;   // 偷动物时的村庄
        public string ItemId;               // ItemObject.StringId
        public int Count;
        public float StolenDay;
        public string LocationName;         // "在{村庄}" — UI 叙事用
        public bool IsCleared;              // 案件 Resolved 且玩家已付出代价 → true
    }

    /// <summary>
    /// 玩家偷窃账本。记录每次偷窃的"谁→什么→在哪"。
    /// 两个用途：① 栽赃候选来源 ② 背包 UI 标注赃物来源
    /// </summary>
    public static class PlayerTheftLedger
    {
        private static List<TheftRecord> _records = new List<TheftRecord>();

        /// <summary>记录一次偷窃</summary>
        public static void Record(string victimHeroId, string settlementId, string itemId, int count, string locationName)
        {
            _records.Add(new TheftRecord
            {
                VictimHeroId = victimHeroId,
                VictimSettlementId = settlementId,
                ItemId = itemId,
                Count = count,
                StolenDay = (float)CampaignTime.Now.ToDays,
                LocationName = locationName ?? "",
                IsCleared = false
            });
            DebugLogger.Log($"[TheftLedger] Recorded: {itemId} x{count} from {victimHeroId ?? settlementId}");
        }

        /// <summary>当案件解决时清除对应记录</summary>
        public static void MarkCleared(string settlementId)
        {
            foreach (var r in _records.Where(r => r.VictimSettlementId == settlementId && !r.IsCleared))
                r.IsCleared = true;
        }

        /// <summary>返回栽赃候选名单</summary>
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

        /// <summary>获取玩家背包中某物品的赃物来源标注（人 > 地点）</summary>
        public static string GetSourceTag(string itemId)
        {
            var record = _records.FirstOrDefault(r => r.ItemId == itemId && !r.IsCleared);
            if (record == null) return null;

            // 优先显示受害者姓名（扒窃），其次显示地点（偷动物）
            if (!string.IsNullOrEmpty(record.VictimHeroId))
            {
                var victim = Hero.FindFirst(h => h.StringId == record.VictimHeroId);
                if (victim != null)
                    return $"⚠ 偷自 {victim.Name}";
            }
            return $"⚠ 偷自 {record.LocationName}";
        }

        /// <summary>获取某英雄是否在账本中（未被清除）</summary>
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
                // 玩家背包里还有这件赃物才能当证物
                var itemObj = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(record.ItemId);
                if (itemObj == null) continue;
                if (MobileParty.MainParty?.ItemRoster.GetItemNumber(itemObj) < 1) continue;
                items.Add(new EvidenceItem
                {
                    ItemId = record.ItemId,
                    ItemName = itemObj?.Name?.ToString() ?? record.ItemId,
                    LocationName = record.LocationName,
                    StolenDay = record.StolenDay,
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
                _records = JsonConvert.DeserializeObject<List<TheftRecord>>(json) ?? new List<TheftRecord>();
            }
            catch { _records = new List<TheftRecord>(); }
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

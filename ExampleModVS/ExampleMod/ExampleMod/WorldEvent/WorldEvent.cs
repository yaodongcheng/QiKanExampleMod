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
        /// <summary>被盗物品 → 数量。合并时累加同种物品、追加异种物品。</summary>
        public Dictionary<string, int> StolenItems;

        // ═══ 玩家行为分解（Misconduct 事件创建时从 AgentBrain._alertBreakdown 写入，合并时累加同种行为） ═══
        /// <summary>
        /// 玩家各类行为的累积警戒值。key = PlayerActionType 名称（"Crouching"/"WeaponDrawn"/"StealUIOpen"/"Steal"/"AttackAlly"/"Knockout"）。
        /// 由 AgentBrain.InitiateConfrontation 在 Mission 内传入，对话系统据此选择精准台词，而非笼统的"闹事"。
        /// 同村合并时累加同种行为的 alert 值。
        /// </summary>
        public Dictionary<string, float> ActionBreakdown;

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

        /// <summary>获取被盗物品字典。</summary>
        [JsonIgnore]
        public Dictionary<string, int> StolenItemsSnapshot => StolenItems ?? new Dictionary<string, int>();

        /// <summary>被盗物品总数量</summary>
        [JsonIgnore]
        public int TotalStolenCount => StolenItemsSnapshot.Values.Sum();

        /// <summary>被盗物品总市值（遍历所有物品 × 各自数量）</summary>
        [JsonIgnore]
        public int TotalStolenValue
        {
            get
            {
                int total = 0;
                foreach (var kv in StolenItemsSnapshot)
                {
                    var item = MBObjectManager.Instance.GetObject<ItemObject>(kv.Key);
                    if (item != null) total += item.Value * kv.Value;
                }
                return total;
            }
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
        /// 按 alert 值降序排列，支持 1-3 种行为的自然语言拼接。
        /// </summary>
        [JsonIgnore]
        public string ActionDescription
        {
            get
            {
                if (ActionBreakdown == null || ActionBreakdown.Count == 0)
                    return Config?.CrimeVerbGerund ?? "闹事";

                var parts = new List<string>();
                foreach (var kv in ActionBreakdown.OrderByDescending(kv => kv.Value))
                {
                    string desc = kv.Key switch
                    {
                        "Crouching" => "鬼鬼祟祟蹲了半天",
                        "WeaponDrawn" => "在村里拔刀",
                        "StealUIOpen" => "翻箱倒柜",
                        "Steal" => "偷了东西",
                        "AttackAlly" => "动手打人",
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

        /// <summary>计算赔偿金额：所有被盗物品总市值 × 赔偿倍数（阶段不同倍数不同）</summary>
        public int ComputeRestitutionCost(EventStage? forStage = null)
        {
            var stage = forStage ?? Stage;
            var cfg = Config;
            if (cfg == null) return 100;

            int baseValue = TotalStolenValue;
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

            string itemDesc = BuildStolenItemsDescription();
            int baseValue = TotalStolenValue;
            if (baseValue <= 0) baseValue = Severity * 10;

            int total = ComputeRestitutionCost();
            string crimeGerund = cfg.CrimeVerbGerund ?? "犯事";

            if (Stage <= EventStage.Emerging)
                return $"{itemDesc}，市值{baseValue}第纳尔。既然你自己认了，赔{total}第纳尔，这事就算了。你认不认？";
            else if (Stage == EventStage.Active)
                return $"{itemDesc}，市值{baseValue}第纳尔。村里人都知道了，{crimeGerund}按规矩要赔{total}第纳尔。你认不认？";
            else
                return $"{itemDesc}，市值{baseValue}第纳尔。最后一次机会——赔{total}第纳尔，否则后果自负。你认不认？";
        }

        /// <summary>构建被盗物品的自然语言描述（用于赔偿/对话）</summary>
        public string BuildStolenItemsDescription()
        {
            var items = StolenItemsSnapshot;
            if (items.Count == 0) return "东西";

            var parts = new List<string>();
            foreach (var kv in items)
            {
                var name = MBObjectManager.Instance.GetObject<ItemObject>(kv.Key)?.Name?.ToString() ?? kv.Key;
                parts.Add(kv.Value == 1 ? $"一只{name}" : $"{kv.Value}只{name}");
            }

            if (parts.Count == 1) return parts[0];
            if (parts.Count == 2) return $"{parts[0]}和{parts[1]}";
            // 3+ 种不同物品：列举前两项 + "等N只牲口"
            var total = items.Values.Sum();
            return $"{parts[0]}、{parts[1]}等{total}只牲口";
        }

        /// <summary>当场被抓时的赔偿（×2 而非 ×3）</summary>
        public int ComputeOnSpotCost()
        {
            var cfg = Config;
            if (cfg == null) return 100;
            int baseValue = TotalStolenValue;
            if (baseValue <= 0) baseValue = Severity * 10;
            return baseValue * 2;
        }

        /// <summary>获取悬赏金额</summary>
        public int ComputeBountyAmount()
        {
            var cfg = Config;
            if (cfg == null) return 500;
            return cfg.BaseBountyPerUnit * Math.Max(1, TotalStolenCount);
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
            CrimeVerbPast = "有人在村里闹事",
            CrimeVerbGerund = "闹事",
            CrimeScene = "村里",
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
                // ── 合并 StolenItems ──
                if (evt.StolenItems != null && evt.StolenItems.Count > 0)
                {
                    existing.StolenItems = existing.StolenItems ?? new Dictionary<string, int>();
                    foreach (var kv in evt.StolenItems)
                    {
                        existing.StolenItems.TryGetValue(kv.Key, out int cur);
                        existing.StolenItems[kv.Key] = cur + kv.Value;
                    }
                }

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

                var itemSummary = string.Join(", ", existing.StolenItemsSnapshot.Select(kv => $"{kv.Key}x{kv.Value}"));
                DebugLogger.Log($"[WorldEvent] Merged theft into existing case {existing.EventId} (totalStolen={existing.TotalStolenCount}, items=[{itemSummary}])");
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

            var itemDesc = string.Join(", ", evt.StolenItemsSnapshot.Select(kv => $"{kv.Key}x{kv.Value}"));
            DebugLogger.Log($"[WorldEvent] New event: {evt.Type} id={evt.EventId} settlement={evt.TargetSettlementId} items=[{itemDesc}] culprit={evt.InitiatorId} stage={evt.Stage} suspect={evt.SuspectHeroId ?? "none"} witnesses={evt.WitnessHeroIds?.Count ?? 0}h+{(evt.TemplateWitness?.Sum(kv => kv.Value) ?? 0)}v severity={evt.Severity} occurredDay={evt.OccurredDay:F2}");
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
            var oldStage = evt.Stage;
            evt.Stage = newStage;
            evt._stageEnteredDay = (float)CampaignTime.Now.ToDays;

            // Resolved 时设置村庄警觉
            if (newStage == EventStage.Resolved && evt.SuspectIsPlayer)
            {
                _villageAlertFlags[evt.TargetSettlementId] = true;
            }

            DebugLogger.Log($"[WorldEvent] {evt.EventId} Stage: {oldStage} → {newStage}");
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

        /// <summary>
        /// 创建或获取 Mission 内行为不端事件。
        /// 同一定居点同时最多一个 Misconduct 活跃事件，重复调用返回已有事件并合并目击者。
        /// 事件创建时的 Stage 为 Emerging（NPC 刚"发现"玩家的不当行为）。
        /// </summary>
        public static WorldEvent TryUpsertMisconductEvent(string settlementId, string initiatorId,
            int severity = 25, string locationName = null, Dictionary<string, float> actionBreakdown = null)
        {
            if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(initiatorId))
                return null;

            // 同村已有活跃的 Misconduct → 复用
            var existing = FindActive(settlementId);
            if (existing != null && existing.Type == EventType.Misconduct)
            {
                // 累计严重度
                existing.Severity = Math.Min(100, existing.Severity + severity / 2);
                // 合并行为分解
                if (actionBreakdown != null && actionBreakdown.Count > 0)
                {
                    existing.ActionBreakdown = existing.ActionBreakdown ?? new Dictionary<string, float>();
                    foreach (var kv in actionBreakdown)
                    {
                        existing.ActionBreakdown.TryGetValue(kv.Key, out float cur);
                        existing.ActionBreakdown[kv.Key] = cur + kv.Value;
                    }
                }
                existing.LastUpdateDay = (float)CampaignTime.Now.ToDays;
                DebugLogger.Log($"[WorldEvent] Reusing Misconduct {existing.EventId} severity={existing.Severity} actions={string.Join(",", existing.ActionBreakdown?.Select(kv=>$"{kv.Key}:{kv.Value:F1}")??Enumerable.Empty<string>())}");
                return existing;
            }

            var evt = new WorldEvent
            {
                EventId = $"misconduct_{settlementId}_{(int)CampaignTime.Now.ToHours}",
                Category = EventCategory.Crime,
                Type = EventType.Misconduct,
                Severity = severity,
                InitiatorId = initiatorId,
                TargetSettlementId = settlementId,
                OccurredDay = (float)CampaignTime.Now.ToDays,
                LocationName = locationName ?? settlementId,
                Stage = EventStage.Emerging,
                SuspectHeroId = initiatorId, // Misconduct 当场锁定嫌犯=玩家
                InvestigationProgress = 1.0f, // 当场目击，无需调查
                PublicAwareness = 0.3f,       // 围观者知道了
                WitnessHeroIds = new List<string>(),
                ActionBreakdown = actionBreakdown ?? new Dictionary<string, float>(),
                LastUpdateDay = (float)CampaignTime.Now.ToDays,
            };

            AddOrMerge(evt);
            DebugLogger.Log($"[WorldEvent] Created Misconduct {evt.EventId} settlement={settlementId} initiator={initiatorId} severity={severity}");
            return evt;
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

using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
    /// <summary>辅助部队文化来源。</summary>
    public enum AuxiliaryCultureSource
    {
        Instigator,  // 使用加害方的文化
        Victim,      // 使用受害方的文化
        Bandit,      // 匪徒文化
        Neutral,     // 中立（默认文化）
    }

    /// <summary>辅助部队所属方。</summary>
    public enum AuxiliaryFaction
    {
        Instigator,  // 属于加害方 faction
        Victim,      // 属于受害方 faction
        Bandit,      // 匪徒
        Neutral,     // 中立
    }

    /// <summary>
    /// 辅助部队配置。事件创建时同步 spawn，先于玩家接委托存在于世界上。
    /// CommissionQuest 通过 RoleTag 查找已有部队复用，不重复 spawn。
    /// </summary>
    public class AuxiliaryPartyConfig
    {
        /// <summary>角色标签（如 "SupplyConvoy" / "EvacuationConvoy"）。CommissionQuest 用此查找。</summary>
        public string RoleTag;
        /// <summary>部队名称模板。</summary>
        public string NameTemplate;
        /// <summary>AI 行为。</summary>
        public EventPartyBehavior Behavior;
        /// <summary>文化来源。</summary>
        public AuxiliaryCultureSource CultureSource;
        /// <summary>所属方。</summary>
        public AuxiliaryFaction Faction;
        /// <summary>兵力范围。</summary>
        public int MinTroops;
        public int MaxTroops;
        /// <summary>生成位置相对目标定居点的偏移方向（null = 随机）。</summary>
        public AuxiliarySpawnPosition SpawnPosition;
    }

    /// <summary>辅助部队生成位置策略。</summary>
    public enum AuxiliarySpawnPosition
    {
        Random,          // 目标定居点周围随机
        BetweenParties,  // 加害方和受害方之间（补给线）
        NearInstigator,  // 加害方附近（斥候等）
        NearTarget,      // 目标定居点附近
    }

    /// <summary>加害方来源策略。</summary>
    public enum InstigatorSource
    {
        /// <summary>附近 Hideout 的 bandit hero</summary>
        BanditHideout,
        /// <summary>敌对 faction 的真实领主</summary>
        EnemyLord,
        /// <summary>同城/同 clan 的关联者（背叛用）</summary>
        RelatedHero,
        /// <summary>城镇 GangLeader 或富商名人</summary>
        TownNotable,
        /// <summary>任意 bandit faction hero</summary>
        AnyBandit,
        /// <summary>无加害方（天灾/结构性）</summary>
        None,
        /// <summary>只能从 NemesisTracker 获取（宿敌复仇）</summary>
        Nemesis,
    }

    /// <summary>事件 party 的 AI 行为。</summary>
    public enum EventPartyBehavior
    {
        /// <summary>向目标定居点移动→劫掠</summary>
        RaidSettlement,
        /// <summary>向目标定居点移动→非敌对（送补给/撤离等）</summary>
        GoToSettlement,
        /// <summary>向目标 Hero 移动→交战</summary>
        EngageTarget,
        /// <summary>在目标附近巡逻</summary>
        PatrolNearTarget,
        /// <summary>追逐玩家</summary>
        ChasePlayer,
        /// <summary>不生成 party（无物理威胁）</summary>
        NoParty,
    }

    /// <summary>
    /// 世界事件类型配置。每种事件类型一条记录，WorldEventSimulator 按配置驱动生成。
    /// </summary>
    public class WorldEventConfig
    {
        public WorldEventType EventType;
        /// <summary>事件的情感/叙事标签</summary>
        public string EmotionTag;
        /// <summary>加害方来源策略</summary>
        public InstigatorSource InstigatorSource;
        /// <summary>找不到真人时是否允许通用模板</summary>
        public bool AllowGeneric;
        /// <summary>party AI 行为</summary>
        public EventPartyBehavior PartyBehavior;
        /// <summary>事件是否以 NamedHero 为目标（vs Settlement）</summary>
        public bool TargetsHero;
        /// <summary>匹配的委托类别（不分角色，旧字段保留兼容）。优先使用 InstigatorCommissions / VictimCommissions。</summary>
        public CommissionCategory[] MatchingCommissions;
        /// <summary>加害方可发布的委托类别（攻击性）。null/空 → 加害方不发布委托。</summary>
        public CommissionCategory[] InstigatorCommissions;
        /// <summary>受害方可发布的委托类别（防御性）。null/空 → 受害方不发布委托。</summary>
        public CommissionCategory[] VictimCommissions;
        /// <summary>辅助部队配置：事件创建时同步 spawn 的附属部队。null/空 = 无辅助部队。</summary>
        public AuxiliaryPartyConfig[] AuxiliaryParties;
        /// <summary>基础严重度范围</summary>
        public int MinSeverity;
        public int MaxSeverity;
        /// <summary>基础时限范围（天）</summary>
        public float MinDayLimit;
        public float MaxDayLimit;
        /// <summary>生成权重乘数（某些事件更稀有）</summary>
        public float WeightMultiplier;

        /// <summary>所有事件配置注册表。</summary>
        public static readonly List<WorldEventConfig> AllConfigs = new List<WorldEventConfig>();

        static WorldEventConfig()
        {
            // 1. BanditRaid — 匪患（匪徒不加害方委托，受害方防御性委托）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.BanditRaid,
                EmotionTag = "fear",
                InstigatorSource = InstigatorSource.BanditHideout,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.RaidSettlement,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.VillageDefense, CommissionCategory.HideoutClear },
                InstigatorCommissions = null, // 匪徒不会给玩家发委托
                VictimCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.VillageDefense, CommissionCategory.HideoutClear },
                MinSeverity = 2, MaxSeverity = 9,
                MinDayLimit = 3, MaxDayLimit = 10,
                WeightMultiplier = 1.5f, // 最常见
            });

            // 2. Kidnapping — 绑架（绑匪不加害方委托）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Kidnapping,
                EmotionTag = "urgency",
                InstigatorSource = InstigatorSource.BanditHideout,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.DecoyMission },
                InstigatorCommissions = null, // 绑匪不会给玩家发委托
                VictimCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.DecoyMission },
                MinSeverity = 4, MaxSeverity = 10,
                MinDayLimit = 2, MaxDayLimit = 7, // 时间紧迫
                WeightMultiplier = 0.8f,
            });

            // 3. Famine — 饥荒（天灾，无加害方）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Famine,
                EmotionTag = "despair",
                InstigatorSource = InstigatorSource.None,
                AllowGeneric = true, // 天灾无加害方
                PartyBehavior = EventPartyBehavior.NoParty,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.SupplyEmergency, CommissionCategory.ProcurementAgent },
                InstigatorCommissions = null, // 天灾无加害方
                VictimCommissions = new[] { CommissionCategory.SupplyEmergency, CommissionCategory.ProcurementAgent },
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 5, MaxDayLimit = 15,
                WeightMultiplier = 0.6f,
            });

            // 4. Betrayal — 背叛（背叛者不加害方委托）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Betrayal,
                EmotionTag = "anger",
                InstigatorSource = InstigatorSource.RelatedHero,
                AllowGeneric = false, // 必须有真实背叛者
                PartyBehavior = EventPartyBehavior.EngageTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.LostItem },
                InstigatorCommissions = null, // 背叛者不会发委托
                VictimCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.LostItem },
                MinSeverity = 5, MaxSeverity = 10,
                MinDayLimit = 3, MaxDayLimit = 10,
                WeightMultiplier = 0.5f,
            });

            // 5. DebtTrap — 债务陷阱
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.DebtTrap,
                EmotionTag = "despair",
                InstigatorSource = InstigatorSource.TownNotable,
                AllowGeneric = false, // 必须有真实债主
                PartyBehavior = EventPartyBehavior.RaidSettlement,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.ProcurementAgent },
                InstigatorCommissions = null, // 债主通常不雇外人催债
                VictimCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.ProcurementAgent },
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 5, MaxDayLimit = 14,
                WeightMultiplier = 0.7f,
            });

            // 6. RomanticConflict — 情仇（双方都可能雇人）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.RomanticConflict,
                EmotionTag = "passion",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.ArenaSpecial, CommissionCategory.DecoyMission },
                InstigatorCommissions = new[] { CommissionCategory.ArenaSpecial, CommissionCategory.DecoyMission },
                VictimCommissions = new[] { CommissionCategory.ArenaSpecial, CommissionCategory.DecoyMission },
                MinSeverity = 2, MaxSeverity = 6,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.4f,
            });

            // 7. FalseAccusation — 冤案（诬告方不加害方委托）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.FalseAccusation,
                EmotionTag = "injustice",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.NoParty, // 核心是调查
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.LostItem, CommissionCategory.PrisonBreak },
                InstigatorCommissions = null, // 诬告者不会雇外人帮自己栽赃
                VictimCommissions = new[] { CommissionCategory.LostItem, CommissionCategory.PrisonBreak },
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 5, MaxDayLimit = 14,
                WeightMultiplier = 0.5f,
            });

            // 8. InheritanceDispute — 继承争端（双方都可能雇人）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.InheritanceDispute,
                EmotionTag = "greed",
                InstigatorSource = InstigatorSource.RelatedHero,
                AllowGeneric = false, // 必须有真实争夺方
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.ProcurementAgent, CommissionCategory.ArenaSpecial },
                InstigatorCommissions = new[] { CommissionCategory.ProcurementAgent, CommissionCategory.ArenaSpecial },
                VictimCommissions = new[] { CommissionCategory.ProcurementAgent, CommissionCategory.ArenaSpecial },
                MinSeverity = 2, MaxSeverity = 7,
                MinDayLimit = 6, MaxDayLimit = 15,
                WeightMultiplier = 0.4f,
            });

            // 9. Fugitive — 逃犯/隐士（追捕方可雇人缉拿，逃犯可雇人掩护）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Fugitive,
                EmotionTag = "moral_grey",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.CaravanEscort, CommissionCategory.BountyHunt },
                InstigatorCommissions = new[] { CommissionCategory.BountyHunt }, // 追捕方：悬赏缉拿
                VictimCommissions = new[] { CommissionCategory.CaravanEscort },   // 逃犯：需要掩护撤离
                MinSeverity = 2, MaxSeverity = 6,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.5f,
            });

            // 10. TradeDispute — 贸易争端（加害方是垄断商，可雇人维持垄断）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.TradeDispute,
                EmotionTag = "greed",
                InstigatorSource = InstigatorSource.TownNotable,
                AllowGeneric = false,
                PartyBehavior = EventPartyBehavior.NoParty,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.SupplyEmergency, CommissionCategory.ProcurementAgent },
                InstigatorCommissions = new[] { CommissionCategory.SupplyIntercept, CommissionCategory.DecoyMission }, // 垄断商：拦截竞争货源、制造混乱
                VictimCommissions = new[] { CommissionCategory.SupplyEmergency, CommissionCategory.ProcurementAgent },  // 受害商人：求援
                MinSeverity = 1, MaxSeverity = 5,
                MinDayLimit = 5, MaxDayLimit = 15,
                WeightMultiplier = 0.6f,
            });

            // 11. NobleConflict — 贵族冲突（核心：双方都可雇人，委托对立）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.NobleConflict,
                EmotionTag = "pride",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = false, // 必须有真实敌对领主
                PartyBehavior = EventPartyBehavior.RaidSettlement,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.SupplyIntercept, CommissionCategory.DecoyMission },
                InstigatorCommissions = new[] { CommissionCategory.SupplyIntercept, CommissionCategory.DecoyMission },          // 攻击方：截补给、引开守军
                VictimCommissions = new[] { CommissionCategory.VillageDefense, CommissionCategory.CaravanEscort, CommissionCategory.SupplyEmergency }, // 防御方：守村、撤离、囤物资
                AuxiliaryParties = new[] {
                    new AuxiliaryPartyConfig {
                        RoleTag = "SupplyConvoy",
                        NameTemplate = "运往{TARGET}的补给队",
                        Behavior = EventPartyBehavior.PatrolNearTarget,
                        CultureSource = AuxiliaryCultureSource.Victim,
                        Faction = AuxiliaryFaction.Victim,
                        MinTroops = 4, MaxTroops = 8,
                        SpawnPosition = AuxiliarySpawnPosition.BetweenParties,
                    },
                },
                MinSeverity = 4, MaxSeverity = 10,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.5f,
            });

            // 12. SacredTheft — 圣物失窃（盗贼不加害方委托）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.SacredTheft,
                EmotionTag = "sacrilege",
                InstigatorSource = InstigatorSource.BanditHideout,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.LostItem, CommissionCategory.BountyHunt },
                InstigatorCommissions = null, // 盗贼不会发委托
                VictimCommissions = new[] { CommissionCategory.LostItem, CommissionCategory.BountyHunt },
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.4f,
            });

            // 13. Assassination — 行刺（刺客不加害方委托）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Assassination,
                EmotionTag = "shock",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.EngageTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.PrisonBreak },
                InstigatorCommissions = null, // 刺客不会发委托
                VictimCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.PrisonBreak }, // 幸存方：追凶、营救被牵连者
                MinSeverity = 6, MaxSeverity = 10, // 高严重度
                MinDayLimit = 1, MaxDayLimit = 5,   // 极其紧迫
                WeightMultiplier = 0.3f,             // 稀有
            });

            // 14. NemesisRevenge — 宿敌复仇（双方都可能雇人）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.NemesisRevenge,
                EmotionTag = "obsession",
                InstigatorSource = InstigatorSource.Nemesis,
                AllowGeneric = false, // 必须是真实宿敌
                PartyBehavior = EventPartyBehavior.ChasePlayer,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt },
                InstigatorCommissions = new[] { CommissionCategory.BountyHunt }, // 宿敌可能悬赏玩家
                VictimCommissions = new[] { CommissionCategory.BountyHunt },     // 玩家也可能悬赏宿敌
                MinSeverity = 5, MaxSeverity = 10,
                MinDayLimit = 1, MaxDayLimit = 10,
                WeightMultiplier = 0f, // 不随机生成
            });
        }

        private static void Register(WorldEventConfig config)
        {
            AllConfigs.Add(config);
        }

        /// <summary>按事件类型取配置。</summary>
        public static WorldEventConfig Get(WorldEventType type)
        {
            return AllConfigs.Find(c => c.EventType == type);
        }

        /// <summary>
        /// 按角色取匹配的委托类别列表。
        /// 优先使用 InstigatorCommissions / VictimCommissions，回退到 MatchingCommissions（兼容旧配置）。
        /// 返回 null/空 = 此角色不发布委托。
        /// </summary>
        public CommissionCategory[] GetCommissionsForRole(bool isVictim)
        {
            if (isVictim)
            {
                if (VictimCommissions != null && VictimCommissions.Length > 0)
                    return VictimCommissions;
                return MatchingCommissions; // 回退兼容
            }
            else
            {
                if (InstigatorCommissions != null && InstigatorCommissions.Length > 0)
                    return InstigatorCommissions;
                // 加害方没有专属列表 → 只有 MatchingCommissions 中有双方共用的情况才回退
                return MatchingCommissions;
            }
        }
    }
}

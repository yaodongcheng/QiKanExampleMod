using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
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
        /// <summary>匹配的委托类别</summary>
        public CommissionCategory[] MatchingCommissions;
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
            // 1. BanditRaid — 匪患
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.BanditRaid,
                EmotionTag = "fear",
                InstigatorSource = InstigatorSource.BanditHideout,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.RaidSettlement,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.VillageDefense, CommissionCategory.HideoutClear },
                MinSeverity = 2, MaxSeverity = 9,
                MinDayLimit = 3, MaxDayLimit = 10,
                WeightMultiplier = 1.5f, // 最常见
            });

            // 2. Kidnapping — 绑架
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Kidnapping,
                EmotionTag = "urgency",
                InstigatorSource = InstigatorSource.BanditHideout,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.DecoyMission },
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
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 5, MaxDayLimit = 15,
                WeightMultiplier = 0.6f,
            });

            // 4. Betrayal — 背叛
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Betrayal,
                EmotionTag = "anger",
                InstigatorSource = InstigatorSource.RelatedHero,
                AllowGeneric = false, // 必须有真实背叛者
                PartyBehavior = EventPartyBehavior.EngageTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.LostItem },
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
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 5, MaxDayLimit = 14,
                WeightMultiplier = 0.7f,
            });

            // 6. RomanticConflict — 情仇
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.RomanticConflict,
                EmotionTag = "passion",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.ArenaSpecial, CommissionCategory.DecoyMission },
                MinSeverity = 2, MaxSeverity = 6,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.4f,
            });

            // 7. FalseAccusation — 冤案
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.FalseAccusation,
                EmotionTag = "injustice",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.NoParty, // 核心是调查
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.LostItem, CommissionCategory.PrisonBreak },
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 5, MaxDayLimit = 14,
                WeightMultiplier = 0.5f,
            });

            // 8. InheritanceDispute — 继承争端
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.InheritanceDispute,
                EmotionTag = "greed",
                InstigatorSource = InstigatorSource.RelatedHero,
                AllowGeneric = false, // 必须有真实争夺方
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.ProcurementAgent, CommissionCategory.ArenaSpecial },
                MinSeverity = 2, MaxSeverity = 7,
                MinDayLimit = 6, MaxDayLimit = 15,
                WeightMultiplier = 0.4f,
            });

            // 9. Fugitive — 逃犯/隐士
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Fugitive,
                EmotionTag = "moral_grey",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.CaravanEscort, CommissionCategory.BountyHunt },
                MinSeverity = 2, MaxSeverity = 6,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.5f,
            });

            // 10. TradeDispute — 贸易争端
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.TradeDispute,
                EmotionTag = "greed",
                InstigatorSource = InstigatorSource.TownNotable,
                AllowGeneric = false,
                PartyBehavior = EventPartyBehavior.NoParty,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.SupplyEmergency, CommissionCategory.ProcurementAgent },
                MinSeverity = 1, MaxSeverity = 5,
                MinDayLimit = 5, MaxDayLimit = 15,
                WeightMultiplier = 0.6f,
            });

            // 11. NobleConflict — 贵族冲突
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.NobleConflict,
                EmotionTag = "pride",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = false, // 必须有真实敌对领主
                PartyBehavior = EventPartyBehavior.RaidSettlement,
                TargetsHero = false,
                MatchingCommissions = new[] { CommissionCategory.SupplyIntercept, CommissionCategory.DecoyMission },
                MinSeverity = 4, MaxSeverity = 10,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.5f,
            });

            // 12. SacredTheft — 圣物失窃
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.SacredTheft,
                EmotionTag = "sacrilege",
                InstigatorSource = InstigatorSource.BanditHideout,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.PatrolNearTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.LostItem, CommissionCategory.BountyHunt },
                MinSeverity = 3, MaxSeverity = 8,
                MinDayLimit = 4, MaxDayLimit = 12,
                WeightMultiplier = 0.4f,
            });

            // 13. Assassination — 行刺
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.Assassination,
                EmotionTag = "shock",
                InstigatorSource = InstigatorSource.EnemyLord,
                AllowGeneric = true,
                PartyBehavior = EventPartyBehavior.EngageTarget,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt, CommissionCategory.PrisonBreak },
                MinSeverity = 6, MaxSeverity = 10, // 高严重度
                MinDayLimit = 1, MaxDayLimit = 5,   // 极其紧迫
                WeightMultiplier = 0.3f,             // 稀有
            });

            // 14. NemesisRevenge — 宿敌复仇（由 HeroNemesisTracker 触发，不由 DailyTick 随机生成）
            Register(new WorldEventConfig
            {
                EventType = WorldEventType.NemesisRevenge,
                EmotionTag = "obsession",
                InstigatorSource = InstigatorSource.Nemesis,
                AllowGeneric = false, // 必须是真实宿敌
                PartyBehavior = EventPartyBehavior.ChasePlayer,
                TargetsHero = true,
                MatchingCommissions = new[] { CommissionCategory.BountyHunt },
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
    }
}

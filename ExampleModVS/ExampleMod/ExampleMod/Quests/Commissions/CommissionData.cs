using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>委托大类（对应六大玩法方向）</summary>
    public enum CommissionCategory
    {
        // A. 狩猎/追踪类
        BountyHunt,
        LegendaryHunt,
        HideoutClear,

        // B. 护送/运输类
        CaravanEscort,
        EmergencyDelivery,

        // C. 寻回/探索类
        LostItem,
        TreasureHunt,
        HorseAcquisition,

        // D. 竞技/战斗类
        UndergroundFight,
        VillageDefense,
        ArenaSpecial,

        // E. 隐秘/行动类
        PrisonBreak,
        SupplyIntercept,
        DecoyMission,

        // F. 经济/贸易类
        SupplyEmergency,
        ProcurementAgent,

        // G. 调查/情报类
        Investigation,
    }

    /// <summary>委托目标类型</summary>
    public enum CommissionTargetType
    {
        NamedHero,   // 必须有 HeroId 的指名目标
        Settlement,  // 地点目标
        Item,        // 物品目标
        Region,      // 区域目标
        Any,         // 无特定目标
    }

    /// <summary>多解法路径</summary>
    public enum ResolutionPath
    {
        Combat,     // 战力突破
        Stealth,    // 潜行智取
        Wealth,     // 财力解决
        Technical,  // 技术破局
        Social,     // 借力打力
    }

    /// <summary>委托难度分级（RDO 赏金分级 + TK5 功勋递进）</summary>
    public enum CommissionTier
    {
        Basic,      // $ 级 — 新手
        Skilled,    // $$ 级 — 熟练
        Expert,     // $$$ 级 — 高手
        Legendary,  // 传奇悬赏（唯一）
    }

    /// <summary>委托完成质量评级</summary>
    public enum CommissionGrade
    {
        Perfect,    // ⭐⭐⭐ 完美
        Good,       // ⭐⭐ 优良
        Passable,   // ⭐ 完成
        Failed,     // ✗ 失败
    }

    /// <summary>
    /// 委托模板定义（静态数据，不存档）。
    /// 每个 CommissionCategory 对应一个 Def，定义该类型委托的元数据。
    /// </summary>
    public class CommissionDef
    {
        public string Id;
        public CommissionCategory Category;
        public string TitleTemplate;          // "悬赏缉拿：{TARGET_NAME}"
        public string DescriptionTemplate;    // 风味文本模板
        public CommissionTargetType TargetType;
        public CommissionTier MinTier = CommissionTier.Basic;
        public float BaseDifficulty;
        public int BaseRewardGold;
        public int TimeLimitDays;
        public SkillObject PrimarySkill;      // 主技能（用于检定）
        public Occupation[] ValidGiverOccupations;  // 哪些 NPC 职业可以发布
        public ResolutionPath[] AvailablePaths;     // 可用解法

        /// <summary>所有委托模板注册表（阶段 1：4 种核心委托）</summary>
        public static readonly List<CommissionDef> AllDefs = new List<CommissionDef>();

        static CommissionDef()
        {
            // ── 1. 悬赏缉拿 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "bounty_hunt",
                Category = CommissionCategory.BountyHunt,
                // 悬赏缉拿标题模板（{TARGET_NAME} 由 GetFlavorDescription 填充）
                TitleTemplate = "Bounty Hunt: {TARGET_NAME}",
                // 悬赏缉拿风味描述模板（目标名占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "Rumor has it {TARGET_NAME} has been seen around here lately. Find and defeat him, dead or alive — though capture pays double.",
                TargetType = CommissionTargetType.NamedHero,
                BaseDifficulty = 0.3f,
                BaseRewardGold = 500,
                TimeLimitDays = 15,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.GangLeader, Occupation.Headman, Occupation.Artisan, Occupation.RuralNotable, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Wealth, ResolutionPath.Stealth },
            });

            // ── 2. 护卫商队 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "caravan_escort",
                Category = CommissionCategory.CaravanEscort,
                // 护卫商队标题模板（目的地占位）
                TitleTemplate = "Escort Caravan: Travel to {TARGET_NAME}",
                // 护卫商队风味描述模板（目的地占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "I have goods to ship to {TARGET_NAME}, but the roads are unsafe. Escort my caravan safely there and I'll pay you.",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.25f,
                BaseRewardGold = 400,
                TimeLimitDays = 10,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Headman, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Wealth, ResolutionPath.Technical },
            });

            // ── 3. 紧急供货 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "supply_emergency",
                Category = CommissionCategory.SupplyEmergency,
                // 紧急供货标题模板（物品/数量/目的地占位）
                TitleTemplate = "Emergency Supply: {ITEM_NAME} x{COUNT} to {TARGET_NAME}",
                // 紧急供货风味描述模板（目的地/物品/天数/数量占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "{TARGET_NAME} urgently needs {ITEM_NAME}! Deliver {COUNT} units of {ITEM_NAME} within {DAYS} days — the sooner, the better the pay.",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.2f,
                BaseRewardGold = 300,
                TimeLimitDays = 7,
                PrimarySkill = DefaultSkills.Trade,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.RuralNotable, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Wealth, ResolutionPath.Technical, ResolutionPath.Combat },
            });

            // ── 4. 地下拳赛 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "underground_fight",
                Category = CommissionCategory.UndergroundFight,
                // 地下拳赛标题模板（地点占位）
                TitleTemplate = "Underground Fight: Fight in {TARGET_NAME}",
                // 地下拳赛风味描述模板（地点占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "My people put money on the fight in {TARGET_NAME}'s arena, but our fighter got hurt. Take his place — win, and we split the prize.",
                TargetType = CommissionTargetType.Any,
                BaseDifficulty = 0.35f,
                BaseRewardGold = 350,
                TimeLimitDays = 5,
                PrimarySkill = DefaultSkills.OneHanded,
                ValidGiverOccupations = new[] { Occupation.GangLeader, Occupation.Merchant, Occupation.Wanderer },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Wealth },
            });

            // ── 阶段 2 ──

            // 5. 猎杀传奇匪首
            AllDefs.Add(new CommissionDef
            {
                Id = "legendary_hunt",
                Category = CommissionCategory.LegendaryHunt,
                // 猎杀传奇匪首标题模板（目标名占位）
                TitleTemplate = "Legendary Hunt: {TARGET_NAME}",
                // 猎杀传奇匪首风味描述模板（目标名占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "{TARGET_NAME} — that name is known to everyone around here. A bandit king who has terrorized these lands for years, carrying a unique piece of equipment. Defeat him: the gear is yours, plus a handsome reward.",
                TargetType = CommissionTargetType.NamedHero,
                BaseDifficulty = 0.7f,
                BaseRewardGold = 5000,
                TimeLimitDays = 20,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Lord, Occupation.GangLeader, Occupation.Headman },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Stealth, ResolutionPath.Wealth },
            });

            // 6. 村防应援
            AllDefs.Add(new CommissionDef
            {
                Id = "village_defense",
                Category = CommissionCategory.VillageDefense,
                // 村防应援标题模板（村子占位）
                TitleTemplate = "Village Defense: Protect {TARGET_NAME}",
                // 村防应援风味描述模板（村子占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "{TARGET_NAME} is about to be raided! Set up defenses before the bandits arrive and protect the villagers.",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.4f,
                BaseRewardGold = 600,
                TimeLimitDays = 5,
                PrimarySkill = DefaultSkills.Leadership,
                ValidGiverOccupations = new[] { Occupation.Headman, Occupation.RuralNotable, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Technical, ResolutionPath.Wealth },
            });

            // 7. 失物追寻
            AllDefs.Add(new CommissionDef
            {
                Id = "lost_item",
                Category = CommissionCategory.LostItem,
                // 失物追寻标题模板（物品占位）
                TitleTemplate = "Lost Item: Recover {ITEM_NAME}",
                // 失物追寻风味描述模板（物品/去向地点占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "My {ITEM_NAME} was stolen! Someone saw the thief heading toward {TARGET_NAME}. Get my property back and you'll be richly rewarded.",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.3f,
                BaseRewardGold = 300,
                TimeLimitDays = 10,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Headman, Occupation.Wanderer, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Technical, ResolutionPath.Wealth, ResolutionPath.Combat },
            });

            // 8. 越狱营救
            AllDefs.Add(new CommissionDef
            {
                Id = "prison_break",
                Category = CommissionCategory.PrisonBreak,
                // 越狱营救标题模板（目标名占位）
                TitleTemplate = "Prison Break: Rescue {TARGET_NAME}",
                // 越狱营救风味描述模板（目标名占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "My friend {TARGET_NAME} is locked up in a hostile town's prison. Get him out — bribe the guards or sneak into the dungeon.",
                TargetType = CommissionTargetType.NamedHero,
                BaseDifficulty = 0.5f,
                BaseRewardGold = 800,
                TimeLimitDays = 12,
                PrimarySkill = DefaultSkills.Roguery,
                ValidGiverOccupations = new[] { Occupation.GangLeader, Occupation.Wanderer, Occupation.Lord, Occupation.Merchant },
                AvailablePaths = new[] { ResolutionPath.Stealth, ResolutionPath.Wealth, ResolutionPath.Social },
            });

            // 9. 物资截获
            AllDefs.Add(new CommissionDef
            {
                Id = "supply_intercept",
                Category = CommissionCategory.SupplyIntercept,
                // 物资截获标题模板（目的地占位）
                TitleTemplate = "Supply Intercept: Cut Off Supplies to {TARGET_NAME}",
                // 物资截获风味描述模板（目的地占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "The enemy is shipping supplies to {TARGET_NAME}. Intercept them before they arrive — keep the goods, or hand them over for a reward.",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.45f,
                BaseRewardGold = 700,
                TimeLimitDays = 8,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Lord, Occupation.GangLeader, Occupation.Wanderer, Occupation.Merchant },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Stealth, ResolutionPath.Wealth },
            });

            // ── 阶段 3 ──

            // 10. 清剿匪穴
            AllDefs.Add(new CommissionDef
            {
                Id = "hideout_clear",
                Category = CommissionCategory.HideoutClear,
                // 清剿匪穴标题模板（附近地点占位）
                TitleTemplate = "Clear the Hideout: Den Near {TARGET_NAME}",
                // 清剿匪穴风味描述模板（地点占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "There's a bandit hideout near {TARGET_NAME} that keeps harrying passing caravans. Clear it out — assault by day, or sneak in at night.",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.35f,
                BaseRewardGold = 450,
                TimeLimitDays = 10,
                PrimarySkill = DefaultSkills.Tactics,
                ValidGiverOccupations = new[] { Occupation.Headman, Occupation.Lord, Occupation.Merchant, Occupation.GangLeader },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Stealth, ResolutionPath.Technical },
            });

            // 11. 限时运粮
            AllDefs.Add(new CommissionDef
            {
                Id = "emergency_delivery",
                Category = CommissionCategory.EmergencyDelivery,
                // 限时运粮标题模板（物品/数量/目的地占位）
                TitleTemplate = "Emergency Delivery: {ITEM_NAME} x{COUNT} to {TARGET_NAME}",
                // 限时运粮风味描述模板（目的地/物品/天数占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "{TARGET_NAME} has run out of food! Here's a batch of {ITEM_NAME} — deliver it within {DAYS} days. The more you carry, the higher the pay — but the load will slow you down.",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.3f,
                BaseRewardGold = 400,
                TimeLimitDays = 5,
                PrimarySkill = DefaultSkills.Riding,
                ValidGiverOccupations = new[] { Occupation.Headman, Occupation.RuralNotable, Occupation.Lord, Occupation.Merchant },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Technical, ResolutionPath.Wealth },
            });

            // 12. 寻宝
            AllDefs.Add(new CommissionDef
            {
                Id = "treasure_hunt",
                Category = CommissionCategory.TreasureHunt,
                // 寻宝标题模板（传说地点占位）
                TitleTemplate = "Treasure Hunt: The {TARGET_NAME} Treasure Legend",
                // 寻宝风味描述模板（地点占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "I got hold of a treasure map — the treasure is said to be buried near {TARGET_NAME}. I don't dare go alone — come with me and we split whatever we find.",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.4f,
                BaseRewardGold = 600,
                TimeLimitDays = 15,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Wanderer, Occupation.GangLeader, Occupation.Merchant, Occupation.Artisan },
                AvailablePaths = new[] { ResolutionPath.Technical, ResolutionPath.Combat, ResolutionPath.Wealth },
            });

            // 13. 寻购名马
            AllDefs.Add(new CommissionDef
            {
                Id = "horse_acquisition",
                Category = CommissionCategory.HorseAcquisition,
                // 寻购名马标题模板（马匹品种占位）
                TitleTemplate = "Acquire a Horse: {ITEM_NAME}",
                // 寻购名马风味描述模板（马匹品种占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "I want a {ITEM_NAME}. Horse prices differ between towns — compare prices and find the cheapest; whatever's left of the budget is yours.",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.25f,
                BaseRewardGold = 350,
                TimeLimitDays = 12,
                PrimarySkill = DefaultSkills.Trade,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Lord, Occupation.Wanderer },
                AvailablePaths = new[] { ResolutionPath.Wealth, ResolutionPath.Technical, ResolutionPath.Combat },
            });

            // 14. 竞技场特别赛
            AllDefs.Add(new CommissionDef
            {
                Id = "arena_special",
                Category = CommissionCategory.ArenaSpecial,
                // 竞技场特别赛标题模板（地点占位）
                TitleTemplate = "Arena Special: Consecutive Wins in {TARGET_NAME}",
                // 竞技场特别赛风味描述模板（地点占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "I've arranged a special-rules bout — no shields, pure skill. Win two straight fights in {TARGET_NAME}'s arena and we split the betting winnings.",
                TargetType = CommissionTargetType.Any,
                BaseDifficulty = 0.45f,
                BaseRewardGold = 500,
                TimeLimitDays = 8,
                PrimarySkill = DefaultSkills.OneHanded,
                ValidGiverOccupations = new[] { Occupation.GangLeader, Occupation.Wanderer, Occupation.Merchant },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Wealth },
            });

            // 15. 引开追兵
            AllDefs.Add(new CommissionDef
            {
                Id = "decoy_mission",
                Category = CommissionCategory.DecoyMission,
                // 引开追兵标题模板（无占位）
                TitleTemplate = "Decoy: Cover the Client's Escape",
                // 引开追兵风味描述模板（无占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "I'm being hunted! Draw the pursuers' attention with a small force while I escape. The longer you hold them, the higher the pay — but don't get caught.",
                TargetType = CommissionTargetType.Any,
                BaseDifficulty = 0.5f,
                BaseRewardGold = 700,
                TimeLimitDays = 3,
                PrimarySkill = DefaultSkills.Riding,
                ValidGiverOccupations = new[] { Occupation.Wanderer, Occupation.GangLeader, Occupation.Lord, Occupation.Merchant },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Stealth, ResolutionPath.Social },
            });

            // 16. 跨城代购
            AllDefs.Add(new CommissionDef
            {
                Id = "procurement_agent",
                Category = CommissionCategory.ProcurementAgent,
                // 跨城代购标题模板（物品占位）
                TitleTemplate = "Cross-Town Procurement: {ITEM_NAME}",
                // 跨城代购风味描述模板（物品占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "I need a {ITEM_NAME}, but I can't show my face in the market myself. Here's a budget — compare prices across towns; the less you spend, the more stays with you. If it's not on the market, negotiate with whoever owns one.",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.35f,
                BaseRewardGold = 500,
                TimeLimitDays = 15,
                PrimarySkill = DefaultSkills.Trade,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Lord, Occupation.GangLeader },
                AvailablePaths = new[] { ResolutionPath.Wealth, ResolutionPath.Technical, ResolutionPath.Combat },
            });

            // 17. 调查委托（犯罪事件 Emerging 阶段——查找嫌犯）
            AllDefs.Add(new CommissionDef
            {
                Id = "investigation",
                Category = CommissionCategory.Investigation,
                // 调查委托标题模板（案发地点占位）
                TitleTemplate = "Investigation: Theft at {TARGET_NAME}",
                // 调查委托风味描述模板（案发地点/天数占位，未接入消费点，暂为英文直译）
                DescriptionTemplate = "A theft has occurred in {TARGET_NAME} and the culprit is unknown. Gather clues within {DAYS} days to find the true culprit — question the locals or search the scene for evidence.",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.15f,
                BaseRewardGold = 200,
                TimeLimitDays = 7,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Headman, Occupation.RuralNotable, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Technical, ResolutionPath.Social },
            });
        }

        /// <summary>按 Category 查找模板</summary>
        public static CommissionDef GetByCategory(CommissionCategory cat)
        {
            return AllDefs.Find(d => d.Category == cat);
        }
    }

    /// <summary>
    /// 委托运行时数据（存档持久化）。
    /// 一个 CommissionQuest 持有一个 CommissionData。
    /// </summary>
    public class CommissionData
    {
        [SaveableField(20)] public string DefId;
        [SaveableField(21)] public CommissionCategory Category;
        [SaveableField(22)] public Hero QuestGiver;       // 真正的委托人
        [SaveableField(35)] public Hero BrokerHero;        // 告示板/中转人（可能与QuestGiver相同）
        [SaveableField(36)] public bool IsNarrativePhase;  // 是否还在"听故事"阶段，未正式启动
        [SaveableField(23)] public Hero TargetHero;
        [SaveableField(24)] public string TargetSettlementId;
        [SaveableField(25)] public string TargetItemId;
        [SaveableField(26)] public int TargetItemCount;
        [SaveableField(27)] public int NegotiatedReward;
        [SaveableField(28)] public int DepositAmount;
        [SaveableField(29)] public bool DepositRepaid;
        [SaveableField(30)] public float TimeRemainingHours;
        [SaveableField(31)] public int CurrentPhase;
        [SaveableField(32)] public int PhaseProgress;
        [SaveableField(33)] public ResolutionPath ChosenPath;
        [SaveableField(34)] public CommissionTier Tier;
        [SaveableField(50)] public bool IsObjectivesComplete;   // 目标已完成，等待领报酬
        [SaveableField(53)] public Hero RewardPayer;              // 结账人，null=默认用QuestGiver
        [SaveableField(60)] public string WorldEventId;           // 关联的 WorldEvent.EventId（统一事件模型）
        [SaveableField(61)] public bool IsGenericInstigator;      // 目标是否为通用模板（无真实 Hero）

        /// <summary>获取关联的模板定义</summary>
        public CommissionDef GetDef() => CommissionDef.GetByCategory(Category);

        /// <summary>获取风味描述（带模板填充）</summary>
        public string GetFlavorDescription()
        {
            // 优先：从关联的 WorldEvent 生成叙事标题（犯罪事件 Quest）
            if (!string.IsNullOrEmpty(WorldEventId))
            {
                var evt = WorldEventStore.FindEvent(WorldEventId);
                if (evt != null)
                {
                    string settlementName = "";
                    var s = Settlement.Find(evt.TargetSettlementId);
                    if (s != null) settlementName = s.Name.ToString();

                    switch (evt.Stage)
                    {
                        case EventStage.Emerging:
                            // 调查案标题：地点 + 案件定性（伤人+失窃=刑案 / 伤人案 / 失窃案）
                            return LWNTextHelper.ResolveCompound("LWN_commission_data_worldevent_investigation_title", "Investigation: {LOCATION} {CASE_LABEL}", ("LOCATION", settlementName), ("CASE_LABEL", evt.CaseLabel));
                        case EventStage.Active:
                        {
                            var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                            if (suspect != null)
                            {
                                if (evt.SuspectIsPlayer)
                                    // 标题：玩家被指控犯下此案
                                    return LWNTextHelper.ResolveCompound("LWN_commission_data_worldevent_accused_title", "Accused in the {LOCATION} Case", ("LOCATION", settlementName));
                                // 标题：悬赏缉拿嫌犯
                                return LWNTextHelper.ResolveCompound("LWN_commission_data_worldevent_bounty_title", "Bounty Hunt: {TARGET_NAME}", ("TARGET_NAME", suspect.Name.ToString()));
                            }
                            // 标题：追查嫌犯下落
                            return LWNTextHelper.ResolveCompound("LWN_commission_data_worldevent_hunt_title", "Hunting the {LOCATION} Case", ("LOCATION", settlementName));
                        }
                        case EventStage.Confrontation:
                            // 标题：案发城镇遭报复的危机
                            return LWNTextHelper.ResolveCompound("LWN_commission_data_worldevent_crisis_title", "Crisis: {LOCATION} Under Attack", ("LOCATION", settlementName));
                        default:
                            break;
                    }
                }
            }

            // 回退：静态模板（按委托类型 ID 查本地化模板，占位符由 TextObject 变量填充）
            var def = GetDef();
            if (def == null)
                // 无模板定义时的兜底标题
                return LWNTextHelper.ResolveText("LWN_commission_data_ongoing", "Commission in progress...");

            // 目标名占位取值（与旧 .Replace 逻辑一一对应）
            string targetName = null;
            if (TargetHero != null)
                targetName = TargetHero.Name.ToString();
            else if (!string.IsNullOrEmpty(TargetSettlementId) && Category == CommissionCategory.Investigation)
            {
                // Investigation: settlement IS the target（调查某地的案件）
                var s = Settlement.Find(TargetSettlementId);
                if (s != null) targetName = s.Name.ToString();
            }

            // 物品名/数量占位取值（仅当指定了物品目标）
            string itemName = null;
            string itemCount = null;
            if (!string.IsNullOrEmpty(TargetItemId))
            {
                var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(TargetItemId);
                itemName = item?.Name.ToString() ?? TargetItemId;
                itemCount = TargetItemCount.ToString();
            }

            // 剩余天数占位（向上取整）
            string daysLeft = ((int)(TimeRemainingHours / 24f) + 1).ToString();

            // 委托标题：XML 查中文 / 英文兜底，填充目标/物品/数量/天数
            return LWNTextHelper.ResolveCompound(
                // 委托标题 key 前缀：LWN_commission_data_
                "LWN_commission_data_" + def.Id + "_title",
                def.TitleTemplate,
                ("TARGET_NAME", targetName),
                ("ITEM_NAME", itemName),
                ("COUNT", itemCount),
                ("DAYS", daysLeft));
        }
    }

    /// <summary>
    /// 难度递进追踪：按委托类型记录完成次数和最高评级，
    /// 用于解锁更高难度的委托。
    /// </summary>
    public static class CommissionTierProgression
    {
        // Key: CommissionCategory -> completion count
        private static Dictionary<CommissionCategory, int> _completionCounts = new Dictionary<CommissionCategory, int>();
        // Key: CommissionCategory -> best grade achieved
        private static Dictionary<CommissionCategory, CommissionGrade> _bestGrades = new Dictionary<CommissionCategory, CommissionGrade>();
        // 按难度分级的完成数：Key = "Category_Tier"
        private static Dictionary<string, int> _tierCounts = new Dictionary<string, int>();

        public static void RecordCompletion(CommissionCategory category, CommissionTier tier, CommissionGrade grade)
        {
            if (!_completionCounts.ContainsKey(category))
                _completionCounts[category] = 0;
            _completionCounts[category]++;

            if (!_bestGrades.ContainsKey(category) || grade < _bestGrades[category])
                _bestGrades[category] = grade;

            string tierKey = $"{category}_{tier}";
            if (!_tierCounts.ContainsKey(tierKey))
                _tierCounts[tierKey] = 0;
            _tierCounts[tierKey]++;
        }

        public static CommissionTier GetAvailableTier(CommissionCategory category)
        {
            int basicDone = GetTierCount(category, CommissionTier.Basic);
            int skilledDone = GetTierCount(category, CommissionTier.Skilled);
            int expertDone = GetTierCount(category, CommissionTier.Expert);
            bool hasGood = _bestGrades.ContainsKey(category) && _bestGrades[category] <= CommissionGrade.Good;
            float scout = Hero.MainHero.GetSkillValue(DefaultSkills.Scouting);
            float roguery = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);

            if (expertDone >= 5 && hasGood && (scout >= 150 || roguery >= 150))
                return CommissionTier.Legendary;
            if (skilledDone >= 5 && hasGood)
                return CommissionTier.Expert;
            if (basicDone >= 3)
                return CommissionTier.Skilled;
            return CommissionTier.Basic;
        }

        private static int GetTierCount(CommissionCategory category, CommissionTier tier)
        {
            string key = $"{category}_{tier}";
            _tierCounts.TryGetValue(key, out int v);
            return v;
        }

        public static int GetCompletionsAtTier(CommissionCategory category, CommissionTier tier)
        {
            return GetTierCount(category, tier);
        }

        #region Persistence
        public static string Serialize()
        {
            try
            {
                var data = new Dictionary<string, int>();
                foreach (var kvp in _completionCounts)
                    data[kvp.Key.ToString()] = kvp.Value;
                return Newtonsoft.Json.JsonConvert.SerializeObject(data);
            }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (data == null) return;
                // 简化处理：反序列化后重建
                _completionCounts = new Dictionary<CommissionCategory, int>();
                _bestGrades = new Dictionary<CommissionCategory, CommissionGrade>();
            }
            catch
            {
                _completionCounts = new Dictionary<CommissionCategory, int>();
                _bestGrades = new Dictionary<CommissionCategory, CommissionGrade>();
            }
        }
        #endregion
    }
}

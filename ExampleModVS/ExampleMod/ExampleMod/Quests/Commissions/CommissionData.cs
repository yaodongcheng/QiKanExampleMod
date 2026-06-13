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
                TitleTemplate = "悬赏缉拿：{TARGET_NAME}",
                DescriptionTemplate = "有消息说{TARGET_NAME}最近在这一带出没。找到并击败他，死活不论——不过活捉的话报酬翻倍。",
                TargetType = CommissionTargetType.NamedHero,
                BaseDifficulty = 0.3f,
                BaseRewardGold = 500,
                TimeLimitDays = 15,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.GangLeader, Occupation.Headman, Occupation.Artisan },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Wealth, ResolutionPath.Stealth },
            });

            // ── 2. 护卫商队 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "caravan_escort",
                Category = CommissionCategory.CaravanEscort,
                TitleTemplate = "护卫商队：前往 {TARGET_NAME}",
                DescriptionTemplate = "我有一批货要运到{TARGET_NAME}，但路上不太平。你护送我的商队安全抵达，我付你报酬。",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.25f,
                BaseRewardGold = 400,
                TimeLimitDays = 10,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Headman },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Wealth, ResolutionPath.Technical },
            });

            // ── 3. 紧急供货 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "supply_emergency",
                Category = CommissionCategory.SupplyEmergency,
                TitleTemplate = "紧急供货：{ITEM_NAME} ×{COUNT} 送往 {TARGET_NAME}",
                DescriptionTemplate = "{TARGET_NAME}急缺{ITEM_NAME}！在{DAYS}天内送{COUNT}单位的{ITEM_NAME}过来，越快报酬越高。",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.2f,
                BaseRewardGold = 300,
                TimeLimitDays = 7,
                PrimarySkill = DefaultSkills.Trade,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.RuralNotable },
                AvailablePaths = new[] { ResolutionPath.Wealth, ResolutionPath.Technical, ResolutionPath.Combat },
            });

            // ── 4. 地下拳赛 ──
            AllDefs.Add(new CommissionDef
            {
                Id = "underground_fight",
                Category = CommissionCategory.UndergroundFight,
                TitleTemplate = "地下拳赛：在 {TARGET_NAME} 出战",
                DescriptionTemplate = "我的人在{TARGET_NAME}的竞技场下了注，但我们的拳手受伤了。你替他上场，赢了奖金对半分。",
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
                TitleTemplate = "猎杀传奇匪首：{TARGET_NAME}",
                DescriptionTemplate = "{TARGET_NAME}——这个名字在这一带无人不知。横行多年的匪王，身上带着一件独一无二的装备。击败他，装备归你，另有重赏。",
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
                TitleTemplate = "村防应援：保护 {TARGET_NAME}",
                DescriptionTemplate = "{TARGET_NAME}即将遭到劫掠！赶在匪徒到达之前布置防御，保护村民。",
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
                TitleTemplate = "失物追寻：找回 {ITEM_NAME}",
                DescriptionTemplate = "我的{ITEM_NAME}被偷了！最后有人看到小偷往{TARGET_NAME}方向跑了。帮我把东西找回来，必有重谢。",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.3f,
                BaseRewardGold = 300,
                TimeLimitDays = 10,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Headman, Occupation.Wanderer },
                AvailablePaths = new[] { ResolutionPath.Technical, ResolutionPath.Wealth, ResolutionPath.Combat },
            });

            // 8. 越狱营救
            AllDefs.Add(new CommissionDef
            {
                Id = "prison_break",
                Category = CommissionCategory.PrisonBreak,
                TitleTemplate = "越狱营救：救出 {TARGET_NAME}",
                DescriptionTemplate = "我的朋友{TARGET_NAME}被关在敌对城镇的监狱里。帮我把他救出来——你可以贿赂守卫，也可以潜入地牢。",
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
                TitleTemplate = "物资截获：拦截运往 {TARGET_NAME} 的补给",
                DescriptionTemplate = "敌方有一批补给正在运往{TARGET_NAME}。在它们到达之前截下来——物资归你处置，或者交给我换报酬。",
                TargetType = CommissionTargetType.Settlement,
                BaseDifficulty = 0.45f,
                BaseRewardGold = 700,
                TimeLimitDays = 8,
                PrimarySkill = DefaultSkills.Scouting,
                ValidGiverOccupations = new[] { Occupation.Lord, Occupation.GangLeader, Occupation.Wanderer },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Stealth, ResolutionPath.Wealth },
            });

            // ── 阶段 3 ──

            // 10. 清剿匪穴
            AllDefs.Add(new CommissionDef
            {
                Id = "hideout_clear",
                Category = CommissionCategory.HideoutClear,
                TitleTemplate = "清剿匪穴：{TARGET_NAME} 附近的藏身处",
                DescriptionTemplate = "{TARGET_NAME}附近有一个匪徒藏身处，不断骚扰过往商队。清理掉它——可以白天强攻，也可以夜间潜入。",
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
                TitleTemplate = "限时运粮：{ITEM_NAME} ×{COUNT} 送往 {TARGET_NAME}",
                DescriptionTemplate = "{TARGET_NAME}断粮了！这里有一批{ITEM_NAME}，{DAYS}天内送到。带得越多报酬越高——但载重会影响你的行军速度。",
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
                TitleTemplate = "寻宝：{TARGET_NAME} 的藏宝传说",
                DescriptionTemplate = "我搞到了一张藏宝图，据说宝物埋在{TARGET_NAME}附近。但我一个人不敢去——你陪我去，找到宝物对半分。",
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
                TitleTemplate = "寻购名马：{ITEM_NAME}",
                DescriptionTemplate = "我想要一匹{ITEM_NAME}。各大城镇的马市价格不同——帮我去比价找到最便宜的，预算省下来的部分归你。",
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
                TitleTemplate = "竞技场特别赛：在 {TARGET_NAME} 连胜",
                DescriptionTemplate = "我安排了一场特别规则的竞技——禁用盾牌，纯靠身手。在{TARGET_NAME}的竞技场连赢两场，押注赚的我们对半分。",
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
                TitleTemplate = "引开追兵：掩护委托人撤离",
                DescriptionTemplate = "我正在被追杀！你带少量兵力引开追兵的注意，我趁机逃跑。坚持的时间越长报酬越高——但千万别被追上了。",
                TargetType = CommissionTargetType.Any,
                BaseDifficulty = 0.5f,
                BaseRewardGold = 700,
                TimeLimitDays = 3,
                PrimarySkill = DefaultSkills.Riding,
                ValidGiverOccupations = new[] { Occupation.Wanderer, Occupation.GangLeader, Occupation.Lord },
                AvailablePaths = new[] { ResolutionPath.Combat, ResolutionPath.Stealth, ResolutionPath.Social },
            });

            // 16. 跨城代购
            AllDefs.Add(new CommissionDef
            {
                Id = "procurement_agent",
                Category = CommissionCategory.ProcurementAgent,
                TitleTemplate = "跨城代购：{ITEM_NAME}",
                DescriptionTemplate = "我需要一件{ITEM_NAME}，但不方便亲自出面。给你一笔预算，去各大城镇比价——花得越少，剩下的归你。如果市场上买不到，就去找拥有这件装备的人交涉。",
                TargetType = CommissionTargetType.Item,
                BaseDifficulty = 0.35f,
                BaseRewardGold = 500,
                TimeLimitDays = 15,
                PrimarySkill = DefaultSkills.Trade,
                ValidGiverOccupations = new[] { Occupation.Merchant, Occupation.Artisan, Occupation.Lord, Occupation.GangLeader },
                AvailablePaths = new[] { ResolutionPath.Wealth, ResolutionPath.Technical, ResolutionPath.Combat },
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

        /// <summary>获取关联的模板定义</summary>
        public CommissionDef GetDef() => CommissionDef.GetByCategory(Category);

        /// <summary>获取风味描述（带模板填充）</summary>
        public string GetFlavorDescription()
        {
            var def = GetDef();
            if (def == null) return "委托进行中...";

            string desc = def.TitleTemplate;
            if (TargetHero != null)
                desc = desc.Replace("{TARGET_NAME}", TargetHero.Name.ToString());
            else if (!string.IsNullOrEmpty(TargetSettlementId))
            {
                var s = Settlement.Find(TargetSettlementId);
                if (s != null) desc = desc.Replace("{TARGET_NAME}", s.Name.ToString());
            }

            if (!string.IsNullOrEmpty(TargetItemId))
            {
                var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(TargetItemId);
                desc = desc.Replace("{ITEM_NAME}", item?.Name.ToString() ?? TargetItemId);
                desc = desc.Replace("{COUNT}", TargetItemCount.ToString());
            }

            desc = desc.Replace("{DAYS}", ((int)(TimeRemainingHours / 24f) + 1).ToString());

            return desc;
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

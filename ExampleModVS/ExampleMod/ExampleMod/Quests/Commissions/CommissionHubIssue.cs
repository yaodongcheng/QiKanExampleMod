using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Issue 上下文：CommissionHubIssue 创建时携带，用于动态展示 Title/Description。
    /// 玩家在对话前就能看到与委托内容匹配的 Issue 标题，而非泛型"委托任务"。
    /// </summary>
    public struct CommissionIssueContext
    {
        // ── SaveableField 标记：存档修复阶段二——读档后 _context 需要还原（否则 Title 落默认"委托任务"）──
        [SaveableField(1)]
        public string CrimeEventId;
        /// <summary>犯罪事件阶段</summary>
        [SaveableField(2)]
        public EventStage CrimeEventStage;
        /// <summary>相关定居点名称（用于展示）</summary>
        [SaveableField(3)]
        public string SettlementName;
        /// <summary>嫌犯名称（Stage=Active 时有效）</summary>
        [SaveableField(4)]
        public string SuspectName;
        /// <summary>犯罪事件类型字符串（Theft_Animal / Theft_Pickpocket 等）</summary>
        [SaveableField(5)]
        public string CrimeEventType;

        /// <summary>世界事件驱动委托的事件类型字符串</summary>
        [SaveableField(6)]
        public string UrgentEventType;
        /// <summary>是否受害者视角（用于世界事件委托）</summary>
        [SaveableField(7)]
        public bool IsEventVictim;

        /// <summary>常规委托的主类别（非犯罪、非紧急事件时使用）</summary>
        /// <remarks>
        /// ⚠️ SaveSystem 不支持 Nullable 字段：空值 box 后为 null，序列化落 CustomStruct 兜底分支
        /// 触发 (int)Value 解箱 NRE（存档崩溃，见 plans/outnet_fix_plans/save-failure-fix.md）。
        /// 改为 枚举 + HasPrimaryCategory 标志，值语义不变。
        /// </remarks>
        [SaveableField(8)]
        public CommissionCategory PrimaryCategory;
        /// <summary>PrimaryCategory 是否有值（SaveSystem 无 Nullable 支持，用标志位替代）</summary>
        [SaveableField(13)]
        public bool HasPrimaryCategory;

        /// <summary>叙事：案件定性标签（"刑案"/"伤人案"/"失窃案"，事实派生）</summary>
        [SaveableField(9)]
        public string CaseLabel;
        /// <summary>叙事：案情事实句（袭击+失窃如实还原，事实派生）</summary>
        [SaveableField(10)]
        public string DiscoveryFacts;
        /// <summary>叙事：权威角色（动态，"村长"/"城主"/"堡主"，由 GetAuthorityRoleDisplayName 计算）</summary>
        [SaveableField(11)]
        public string AuthorityRole;
        /// <summary>叙事：目击人数</summary>
        [SaveableField(12)]
        public int WitnessCount;

        public bool IsCrimeEvent => !string.IsNullOrEmpty(CrimeEventId);
        public bool IsUrgentEvent => !string.IsNullOrEmpty(UrgentEventType);
    }

    /// <summary>
    /// 极简信号 Issue：不生成 Quest，不走 Issue 对话流，仅用做触发 NPC 头顶 ! 标记。
    /// 当玩家与该 NPC 对话时，走 Intent 系统的 CommissionIntent（不是 Issue 对话流）。
    /// </summary>
    public class CommissionHubIssue : IssueBase
    {
        // 存档修复：_context 必须持久化，否则读档后 Title/Description 丢失（落默认"委托任务"）
        [SaveableField(1)]
        private readonly CommissionIssueContext _context;

        public CommissionHubIssue(Hero issueOwner, CommissionIssueContext context)
            : base(issueOwner, CampaignTime.DaysFromNow(30f))
        {
            _context = context;
        }

        // ── 抽象属性实现（根据上下文动态展示）──
        public override TextObject IssueBriefByIssueGiver =>
            IssueBriefForContext();
        public override TextObject IssueAcceptByPlayer =>
            // 玩家接受委托的回应台词
            new TextObject(LWNTextHelper.ResolveText("LWN_issue_accept_player", "Let me see what commissions you have."));
        public override TextObject IssueQuestSolutionExplanationByIssueGiver =>
            DescriptionForContext();
        public override TextObject IssueQuestSolutionAcceptByPlayer =>
            // 玩家确认接取委托的选项文本
            new TextObject(LWNTextHelper.ResolveText("LWN_issue_accept_solution", "Take the commission"));
        public override bool IsThereAlternativeSolution => false;
        public override bool IsThereLordSolution => false;
        public override TextObject Title =>
            TitleForContext();
        public override TextObject Description =>
            DescriptionForContext();

        // ── 动态展示：根据上下文返回不同 Title / Brief / Description ──

        private TextObject TitleForContext()
        {
            if (_context.IsCrimeEvent)
            {
                switch (_context.CrimeEventStage)
                {
                    case EventStage.Emerging:
                    {
                        // 案件定性标签兜底（无标签时显示"案件"）
                        string caseLabel = _context.CaseLabel ?? LWNTextHelper.ResolveText("LWN_issue_case_label_fallback", "case");
                        // 委托标题：调查定居点案件
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_title_investigate",
                            "Investigate: {LOCATION} {CASE}",
                            ("LOCATION", _context.SettlementName),
                            ("CASE", caseLabel)));
                    }
                    case EventStage.Active:
                        if (!string.IsNullOrEmpty(_context.SuspectName))
                            // 委托标题：悬赏缉拿嫌犯
                            return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_title_bounty",
                                "Bounty: {TARGET}",
                                ("TARGET", _context.SuspectName)));
                        // 委托标题：追凶定居点案件
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_title_manhunt",
                            "Manhunt: The {LOCATION} case",
                            ("LOCATION", _context.SettlementName)));
                    case EventStage.Confrontation:
                        // 委托标题：定居点遭报复危机
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_title_retaliation",
                            "Crisis: {LOCATION} is under threat of retaliation",
                            ("LOCATION", _context.SettlementName)));
                    default:
                        // 委托标题：定居点案件
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_title_case",
                            "Case: {LOCATION}",
                            ("LOCATION", _context.SettlementName)));
                }
            }

            if (_context.IsUrgentEvent)
            {
                if (_context.HasPrimaryCategory)
                    return CategoryToTitle(_context.PrimaryCategory);
                // 委托标题：紧急委托
                return new TextObject(LWNTextHelper.ResolveText("LWN_issue_title_urgent", "Urgent commission"));
            }

            if (_context.HasPrimaryCategory)
                return CategoryToTitle(_context.PrimaryCategory);

            // 委托标题：通用委托任务
            return new TextObject(LWNTextHelper.ResolveText("LWN_issue_title_generic", "Commission"));
        }

        private TextObject IssueBriefForContext()
        {
            if (_context.IsCrimeEvent)
            {
                switch (_context.CrimeEventStage)
                {
                    case EventStage.Emerging:
                    {
                        // 案件定性标签兜底（无标签时显示"案件"）
                        string caseLabel = _context.CaseLabel ?? LWNTextHelper.ResolveText("LWN_issue_case_label_fallback", "case");
                        // 委托简报：调查定居点案件
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_brief_investigate",
                            "Investigate the {CASE} in {LOCATION}",
                            ("LOCATION", _context.SettlementName),
                            ("CASE", caseLabel)));
                    }
                    case EventStage.Active:
                        if (!string.IsNullOrEmpty(_context.SuspectName))
                            // 委托简报：缉拿嫌犯
                            return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_brief_bounty",
                                "Hunt down the suspect {TARGET}",
                                ("TARGET", _context.SuspectName)));
                        // 委托简报：追查定居点案件真凶
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_brief_manhunt",
                            "Track down the culprit in {LOCATION}",
                            ("LOCATION", _context.SettlementName)));
                    case EventStage.Confrontation:
                        // 委托简报：保卫定居点免受报复
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_brief_retaliation",
                            "Defend {LOCATION} from retaliation",
                            ("LOCATION", _context.SettlementName)));
                    default:
                        // 委托简报：有案件需要处理
                        return new TextObject(LWNTextHelper.ResolveText("LWN_issue_brief_case", "There is a case to handle"));
                }
            }

            if (_context.IsUrgentEvent)
                // 委托简报：有紧急委托需要帮手
                return new TextObject(LWNTextHelper.ResolveText("LWN_issue_brief_urgent", "An urgent commission needs a hand"));

            if (_context.HasPrimaryCategory)
                return CategoryToBrief(_context.PrimaryCategory);

            // 委托简报：有委托任务可接
            return new TextObject(LWNTextHelper.ResolveText("LWN_issue_brief_generic", "Commissions are available"));
        }

        private TextObject DescriptionForContext()
        {
            if (_context.IsCrimeEvent)
            {
                switch (_context.CrimeEventStage)
                {
                    case EventStage.Emerging:
                    {
                        // 案情从事实派生（袭击+失窃如实还原），不再用 EventType 静态模板拼接
                        // 案情事实句兜底（无事实时显示"出了案子"）
                        string facts = !string.IsNullOrEmpty(_context.DiscoveryFacts) ? _context.DiscoveryFacts : LWNTextHelper.ResolveText("LWN_issue_emerging_facts_fallback", "something has happened");
                        string witnessClause = _context.WitnessCount > 0
                            // 目击人数从句（有目击者）：{COUNT}人目击
                            ? LWNTextHelper.ResolveCompound("LWN_issue_emerging_witness_clause",
                                ", {COUNT} witnesses",
                                ("COUNT", _context.WitnessCount.ToString()))
                            // 目击人数从句（无目击者）：无人目击
                            : LWNTextHelper.ResolveText("LWN_issue_emerging_no_witness_clause", ", no witnesses");
                        return new TextObject(
                            // 案情描述：定居点 + 事实 + 目击情况 + 权威角色求助
                            LWNTextHelper.ResolveCompound("LWN_issue_emerging_desc",
                                "{LOCATION}: {FACTS}{WITNESS_CLAUSE}. {AUTHORITY} is looking for someone to help investigate.",
                                ("LOCATION", _context.SettlementName),
                                ("FACTS", facts),
                                ("WITNESS_CLAUSE", witnessClause),
                                ("AUTHORITY", GetAuthorityRoleText())));
                    }
                    case EventStage.Active:
                        if (!string.IsNullOrEmpty(_context.SuspectName))
                            // 案情描述：嫌犯已查明，权威角色悬赏缉拿
                            return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_desc_active_suspect",
                                "The case in {LOCATION} has taken shape — the suspect is {TARGET}. {AUTHORITY} offers a bounty for their capture.",
                                ("LOCATION", _context.SettlementName),
                                ("TARGET", _context.SuspectName),
                                ("AUTHORITY", GetAuthorityRoleText())));
                        // 案情描述：嫌犯已锁定但未查明身份
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_desc_active",
                            "The case in {LOCATION} has progressed — the suspect has been identified. Speak to this person for details.",
                            ("LOCATION", _context.SettlementName)));
                    case EventStage.Confrontation:
                        // 案情描述：定居点遭报复威胁急需应援
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_desc_confrontation",
                            "{LOCATION} is under threat of retaliation and urgently needs help.",
                            ("LOCATION", _context.SettlementName)));
                    default:
                        // 案情描述：定居点有案件待处理
                        return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_desc_case",
                            "There is a case to handle in {LOCATION}. Speak to this person for details.",
                            ("LOCATION", _context.SettlementName)));
                }
            }

            if (_context.IsUrgentEvent)
                // 案情描述：紧急委托需要帮手
                return new TextObject(LWNTextHelper.ResolveText("LWN_issue_desc_urgent",
                    "This person has an urgent commission and needs a hand. Speak to them for details."));

            if (_context.HasPrimaryCategory)
                // 案情描述：带委托类别标题的描述
                return new TextObject(LWNTextHelper.ResolveCompound("LWN_issue_desc_category",
                    "This person has a {TITLE} commission and needs a hand. Speak to them for details.",
                    ("TITLE", CategoryToTitle(_context.PrimaryCategory).ToString())));

            // 案情描述：常规委托通用描述
            return new TextObject(LWNTextHelper.ResolveText("LWN_issue_desc_generic",
                "This person has a commission and needs a hand. Speak to them for details."));
        }

        private string GetAuthorityRoleText()
        {
            // 权威角色动态显示名（村庄=村长/城镇=城主/城堡=堡主），
            // 已在 ResolveIssueContext 中通过 GetAuthorityRoleDisplayName 预计算
            if (_context.IsCrimeEvent && !string.IsNullOrEmpty(_context.AuthorityRole))
                return _context.AuthorityRole;
            // 权威角色兜底（非犯罪事件时显示"委托人"）
            return LWNTextHelper.ResolveText("LWN_issue_authority_role_fallback", "the client");
        }

        private static TextObject CategoryToTitle(CommissionCategory category)
        {
            switch (category)
            {
                // 委托类别标题：悬赏缉拿
                case CommissionCategory.BountyHunt: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_bounty_hunt", "Bounty hunting"));
                // 委托类别标题：猎杀传奇匪首
                case CommissionCategory.LegendaryHunt: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_legendary_hunt", "Hunt a legendary bandit chief"));
                // 委托类别标题：护卫商队
                case CommissionCategory.CaravanEscort: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_caravan_escort", "Escort a caravan"));
                // 委托类别标题：村防应援
                case CommissionCategory.VillageDefense: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_village_defense", "Defend a village"));
                // 委托类别标题：清剿匪穴
                case CommissionCategory.HideoutClear: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_hideout_clear", "Clear out a hideout"));
                // 委托类别标题：越狱营救
                case CommissionCategory.PrisonBreak: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_prison_break", "Prison rescue"));
                // 委托类别标题：紧急供货
                case CommissionCategory.SupplyEmergency: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_supply_emergency", "Urgent supply"));
                // 委托类别标题：限时运粮
                case CommissionCategory.EmergencyDelivery: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_emergency_delivery", "Timed grain delivery"));
                // 委托类别标题：失物追寻
                case CommissionCategory.LostItem: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_lost_item", "Find a lost item"));
                // 委托类别标题：寻宝
                case CommissionCategory.TreasureHunt: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_treasure_hunt", "Treasure hunt"));
                // 委托类别标题：寻购名马
                case CommissionCategory.HorseAcquisition: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_horse_acquisition", "Procure a fine horse"));
                // 委托类别标题：地下拳赛
                case CommissionCategory.UndergroundFight: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_underground_fight", "Underground fight"));
                // 委托类别标题：竞技场特别赛
                case CommissionCategory.ArenaSpecial: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_arena_special", "Arena special"));
                // 委托类别标题：物资截获
                case CommissionCategory.SupplyIntercept: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_supply_intercept", "Intercept supplies"));
                // 委托类别标题：引开追兵
                case CommissionCategory.DecoyMission: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_decoy_mission", "Lure away pursuers"));
                // 委托类别标题：跨城代购
                case CommissionCategory.ProcurementAgent: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_title_procurement_agent", "Cross-city procurement"));
                // 委托类别标题：通用委托任务
                default: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_title_generic", "Commission"));
            }
        }

        private static TextObject CategoryToBrief(CommissionCategory category)
        {
            switch (category)
            {
                // 委托类别简报：有悬赏缉拿委托
                case CommissionCategory.BountyHunt: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_bounty_hunt", "Bounty hunting commission available"));
                // 委托类别简报：有猎杀传奇匪首委托
                case CommissionCategory.LegendaryHunt: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_legendary_hunt", "Commission to hunt a legendary bandit chief available"));
                // 委托类别简报：有商队护卫委托
                case CommissionCategory.CaravanEscort: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_caravan_escort", "Caravan escort commission available"));
                // 委托类别简报：有村庄防卫委托
                case CommissionCategory.VillageDefense: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_village_defense", "Village defense commission available"));
                // 委托类别简报：有清剿匪穴委托
                case CommissionCategory.HideoutClear: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_hideout_clear", "Hideout clearing commission available"));
                // 委托类别简报：有越狱营救委托
                case CommissionCategory.PrisonBreak: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_prison_break", "Prison rescue commission available"));
                // 委托类别简报：有紧急供货委托
                case CommissionCategory.SupplyEmergency: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_supply_emergency", "Urgent supply commission available"));
                // 委托类别简报：有限时运粮委托
                case CommissionCategory.EmergencyDelivery: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_emergency_delivery", "Timed grain delivery commission available"));
                // 委托类别简报：有失物追寻委托
                case CommissionCategory.LostItem: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_lost_item", "Lost item search commission available"));
                // 委托类别简报：有寻宝委托
                case CommissionCategory.TreasureHunt: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_treasure_hunt", "Treasure hunt commission available"));
                // 委托类别简报：有寻购名马委托
                case CommissionCategory.HorseAcquisition: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_horse_acquisition", "Fine horse procurement commission available"));
                // 委托类别简报：有地下拳赛委托
                case CommissionCategory.UndergroundFight: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_underground_fight", "Underground fight commission available"));
                // 委托类别简报：有竞技场特别赛委托
                case CommissionCategory.ArenaSpecial: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_arena_special", "Arena special commission available"));
                // 委托类别简报：有物资截获委托
                case CommissionCategory.SupplyIntercept: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_supply_intercept", "Supply intercept commission available"));
                // 委托类别简报：有引开追兵委托
                case CommissionCategory.DecoyMission: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_decoy_mission", "Pursuer luring commission available"));
                // 委托类别简报：有跨城代购委托
                case CommissionCategory.ProcurementAgent: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_cat_brief_procurement_agent", "Cross-city procurement commission available"));
                // 委托类别简报：有委托任务可接
                default: return new TextObject(LWNTextHelper.ResolveText("LWN_issue_brief_generic", "Commissions are available"));
            }
        }

        protected override void OnGameLoad()
        {
        }

        protected override void HourlyTick()
        {
        }

        protected override QuestBase GenerateIssueQuest(string questId)
        {
            // 从 WorldEvent 生成追责 Quest——Quest 只能从 Issue 生，不能凭空出现
            var data = CommissionGenerator.TryGenerateAccountabilityQuest(IssueOwner);
            if (data == null) return null;

            string id = !string.IsNullOrEmpty(data.WorldEventId)
                ? $"crime_{data.WorldEventId}"
                : questId;
            var quest = new CommissionQuest(id, data);
            quest.StartQuest();
            DebugLogger.Log($"[CommissionHubIssue] GenerateIssueQuest: {id} category={data.Category} giver={IssueOwner?.Name}");
            return quest;
        }

        /// <summary>
        /// 🔑 公开入口：Intent 调用此方法触发 Issue→Quest 转换。
        /// 内部走 GenerateIssueQuest 创建 Quest → CompleteIssueWithQuest 解除 Issue。
        /// </summary>
        public CommissionQuest AcceptQuest()
        {
            // 只走官方 StartIssueWithQuest 建立 Issue→Quest 关联（内部 `IssueQuest = GenerateIssueQuest(...)`）：
            //  ① 读档时 QuestManager.OnGameLoaded 校验活动 quest 必须有关联 Issue，否则 CompleteQuestWithCancel
            //  ② issue 的完成交给 quest 结束时的官方 IssueManager.OnQuestCompleted 自动收尾（Success→CompleteIssueWithQuest）
            //     —— 绝不能在 quest 刚创建时就 CompleteIssueWithQuest：会立即发 IssueFinishedWithSuccess，
            //        让任务日志在 quest 进行中就显示[成功完成]（曾踩坑）。
            if (!StartIssueWithQuest()) return null;
            return IssueQuest as CommissionQuest;
        }

        public override IssueFrequency GetFrequency()
        {
            return IssueFrequency.Common;
        }

#if !MB2_V1212
        protected override bool CanPlayerTakeQuestConditions(Hero issueGiver,
            out PreconditionFlags flag, out Hero relationHero, out SkillObject skill, out int requiredGold)
#else
        protected override bool CanPlayerTakeQuestConditions(Hero issueGiver,
            out PreconditionFlags flag, out Hero relationHero, out SkillObject skill)
#endif
        {
            flag = PreconditionFlags.None;
            relationHero = issueGiver;
            skill = null;
#if !MB2_V1212
            requiredGold = 0;
#endif

            if (issueGiver == null)
            {
                flag = PreconditionFlags.Relation;
                return false;
            }
            return true;
        }

        public override bool IssueStayAliveConditions()
        {
            // 信号 Issue 在 NPC 存活且有委托可接时保持活跃
            return IssueOwner != null && IssueOwner.IsAlive
                && CommissionGenerator.HasCommissionsFor(IssueOwner, out int count)
                && count > 0;
        }

        protected override void CompleteIssueWithTimedOutConsequences()
        {
            // 信号 Issue 过期的清理已在 CommissionIssueBehavior 的 DailyTick 中处理
        }
    }

    /// <summary>
    /// CommissionIssueBehavior：监听 OnCheckForIssueEvent，
    /// 当 NPC 有可接委托时通过 CommissionHubIssue 触发原生 ! 标记。
    /// </summary>
    public class CommissionIssueBehavior : CampaignBehaviorBase
    {
        // 缓存：记录哪些 Hero 已被注册了 Issue（避免重复注册）
        private Dictionary<string, CommissionHubIssue> _activeIssues = new Dictionary<string, CommissionHubIssue>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnCheckForIssueEvent.AddNonSerializedListener(this, OnCheckForIssue);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            //先不靠玩家进入定居点触发，改为世界事件阶段变更时立即刷新 Issue 标记
           // CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            WorldEventStore.OnEventStageChanged += OnWorldEventStageChanged;
        }

        /// <summary>
        /// 玩家进入定居点时，为当前定居点的 Notable 刷新 Issue 信号。
        /// 犯罪事件（Theft/Murder/Poaching）：只有权威 NPC（Headman/族长/领主）显示 !。
        /// 常规委托：所有符合条件的 Notable 均可显示 !。
        /// </summary>
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != MobileParty.MainParty) return;
            if (settlement == null) return;

            DebugLogger.Log($"[CommissionIssue] Player entered {settlement.Name}, refreshing issue signals");

            int scanned = 0;
            int created = 0;

            // 先检查此定居点是否有活跃犯罪事件
            var crimeEvent = WorldEventStore.FindOnGoing(settlement.StringId);

            // Dormant 阶段：村民尚未发现犯罪，不应有任何 Issue。
            // 等 DailyTick → ProcessDormant 将 Stage 推进到 Emerging 后，
            // OnCheckForIssue 会自然为权威 NPC 创建 Issue。
            if (crimeEvent != null && crimeEvent.Stage == EventStage.Dormant)
            {
                DebugLogger.Log($"[CommissionIssue] {settlement.Name}: crime event {crimeEvent.EventId} is Dormant — skipping (villagers haven't discovered yet)");
                return;
            }

            // 活跃犯罪事件（Emerging / Active / Confrontation）：只有权威 NPC 显示 !
            if (crimeEvent != null)
            {
                var authority = WorldEventStore.GetAuthorityNpc(crimeEvent);
                if (authority != null && authority.IsAlive && authority != Hero.MainHero)
                {
                    scanned = 1;
                    // 清理已有原版 Issue（如 HeadmanNeedsToDeliverAHerdIssue）——
                    // 犯罪事件优先，原版日常委托不应在村有案件时继续挂 ! 标记。
                    if (authority.Issue != null && !(authority.Issue is CommissionHubIssue))
                    {
                        DebugLogger.Log($"[CommissionIssue] Crime event active for {settlement.Name} — clearing vanilla issue '{authority.Issue.GetType().Name}' from {authority.Name}");
                        Campaign.Current.IssueManager.DeactivateIssue(authority.Issue);
                    }
                    if (TryAddIssue(authority)) created = 1;
                }
            }
            else
            {
                // 非犯罪事件：遍历全部 Notable（常规委托 / 世界事件委托）
                foreach (var h in settlement.Notables)
                {
                    if (h == null || h == Hero.MainHero || !h.IsAlive) continue;
                    scanned++;
                    if (TryAddIssue(h)) created++;
                }
            }

            DebugLogger.Log($"[CommissionIssue] {settlement.Name}: scanned {scanned} notables, created {created} issues");
        }

        /// <summary>
        /// WorldEventStore.OnEventStageChanged 回调：世界事件的阶段发生变化时，立即刷新对应定居点的 Issue 标记。
        /// 不等玩家进入定居点、不等原版 DailyTick —— 确保村民发现犯罪后，大地图上立刻出现 !。
        /// </summary>
        private void OnWorldEventStageChanged(WorldEvent evt)
        {
            if (evt == null) return;
            if (string.IsNullOrEmpty(evt.TargetSettlementId)) return;

            var settlement = Settlement.Find(evt.TargetSettlementId);
            if (settlement == null) return;

            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority == null || !authority.IsAlive || authority == Hero.MainHero) return;

            DebugLogger.Log($"[CommissionIssue] StageChanged: event={evt.EventId} stage={evt.Stage} settlement={settlement.Name} — refreshing issue signals");

            if (evt.Stage == EventStage.Emerging || evt.Stage == EventStage.Active || evt.Stage == EventStage.Confrontation)
            {
                // 清除已有原版 Issue（犯罪事件优先于日常委托）
                if (authority.Issue != null && !(authority.Issue is CommissionHubIssue))
                {
                    DebugLogger.Log($"[CommissionIssue] StageChanged — clearing vanilla issue '{authority.Issue.GetType().Name}' from {authority.Name} for {evt.EventId}");
                    Campaign.Current.IssueManager.DeactivateIssue(authority.Issue);
                }

                // 如果已有 CommissionHubIssue（阶段变更需重建上下文），先清除再重建
                if (authority.Issue is CommissionHubIssue existingIssue)
                {
                    Campaign.Current.IssueManager.DeactivateIssue(existingIssue);
                    _activeIssues.Remove(authority.StringId);
                }

                // 阶段变更时（Emerging→Active/Confrontation），先完成旧的调查 Quest，
                // 释放 NPC 的委托槽位（MaxCommissionsPerNpc=1），再创建新阶段的 Issue。
                // 否则 HasCommissionsFor 会因为旧 Quest 仍在进行中而拒绝创建新 Issue。
                // —— EventStage.Active 一定是从 Emerging 变过来的（Dormant→Emerging 也一样但那时还没有 Quest），
                //    而 Confrontation 也可能从 Active 变过来，都需要清理旧 Quest。
                if (evt.Stage == EventStage.Active || evt.Stage == EventStage.Confrontation)
                {
                    CompleteOldInvestigationQuest(evt, authority);
                }

                TryAddIssue(authority);
            }
            else if (evt.Stage == EventStage.Resolved || evt.Stage == EventStage.Unsolved)
            {
                // 事件结束 → 通过 IssueManager 正确清除
                if (authority.Issue is CommissionHubIssue hubIssue)
                {
                    DebugLogger.Log($"[CommissionIssue] StageChanged — removing CommissionHubIssue from {authority.Name}, event {evt.EventId} resolved/unsolved");
                    Campaign.Current.IssueManager.DeactivateIssue(hubIssue);
                    _activeIssues.Remove(authority.StringId);
                }

                // 安全网：Hero.Issue 已被意外清除但 _activeIssues/IssueManager 仍有残留
                // （例如之前 CreateNewIssue 失败导致 Issue 处于半注册状态）
                if (_activeIssues.ContainsKey(authority.StringId))
                {
                    DebugLogger.Log($"[CommissionIssue] StageChanged — cleaning stale _activeIssues entry for {authority.Name}, event {evt.EventId} resolved/unsolved");
                    var staleIssue = _activeIssues[authority.StringId];
                    _activeIssues.Remove(authority.StringId);
                    try { Campaign.Current.IssueManager.DeactivateIssue(staleIssue); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 阶段变更前，完成该 WorldEvent 对应的旧 Investigation Quest。
        /// 必须在 TryAddIssue 之前调用，否则 HasCommissionsFor 会因为旧 Quest 仍在进行中
        /// 而拒绝创建新 Issue（MaxCommissionsPerNpc=1 的槽位被占用）。
        /// </summary>
        private static void CompleteOldInvestigationQuest(WorldEvent evt, Hero authority)
        {
            bool suspectIsPlayer = evt.SuspectHeroId == Hero.MainHero.StringId;
            foreach (var q in Campaign.Current.QuestManager.Quests)
            {
                if (q is CommissionQuest cq
                    && cq.IsOngoing
                    && cq.Data?.WorldEventId == evt.EventId
                    && cq.Data?.Category == CommissionCategory.Investigation
                    && cq.CommissionGiver == authority)
                {
                    cq.CompleteInvestigationExternally(suspectIsPlayer);
                    DebugLogger.Log($"[CommissionIssue] StageChanged — completed old investigation quest {cq.StringId} (suspectIsPlayer={suspectIsPlayer})");
                    break;
                }
            }
        }

        /// <summary>
        /// 原版 OnCheckForIssue 在游戏初始化时会对全大陆 Notable 触发。
        /// 过滤：只处理其定居点有活跃犯罪事件的权威 NPC——
        /// 非权威 Notable 不应为犯罪事件显示 ! 标记。
        /// </summary>
        private void OnCheckForIssue(Hero hero)
        {
            if (hero == null) return;

            var heroSettlement = hero.CurrentSettlement;
            if (heroSettlement == null) return;

            // 只有该定居点存在活跃 WorldEvent 时才处理
            var evt = WorldEventStore.FindOnGoing(heroSettlement.StringId);
            if (evt == null) return;

            // Dormant 阶段事件尚未被村民发现，不应触发 ! 标记
            if (evt.Stage == EventStage.Dormant || evt.Stage == EventStage.Resolved || evt.Stage == EventStage.Unsolved) return;

            // 犯罪事件：只有权威 NPC 才有资格亮 !
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != hero) return;

            // 清理已有原版 Issue（犯罪事件优先于日常委托）
            if (hero.Issue != null && !(hero.Issue is CommissionHubIssue))
            {
                DebugLogger.Log($"[CommissionIssue] OnCheckForIssue — clearing vanilla issue '{hero.Issue.GetType().Name}' from {hero.Name} for crime event {evt.EventId}");
                Campaign.Current.IssueManager.DeactivateIssue(hero.Issue);
            }

            TryAddIssue(hero);
        }

        private bool TryAddIssue(Hero hero)
        {
            if (hero == null) return false;
            if (hero == Hero.MainHero) return false;

            int maxQuests = TrustSystem.GetMaxConcurrentQuests(TrustSystem.GetTrust(hero));
            int activeCount = CommissionQuest.GetActiveCommissionCount();
            if (activeCount >= maxQuests) return false;

            if (!CommissionGenerator.HasCommissionsFor(hero, out int count) || count <= 0) return false;

            // 已有 CommissionHubIssue → 跳过
            if (hero.Issue is CommissionHubIssue) return false;

            // 已有原版 Issue → 不覆盖
            if (hero.Issue != null) return false;

            // 解析上下文：确定这个 Issue 代表什么类型的委托
            var context = ResolveIssueContext(hero);

            // 构造 CommissionHubIssue 并通过 IssueManager.CreateNewIssue 正式注册
            // ——同时完成 hero.Issue 赋值 + IssueManager 内部追踪（大地图 ! 依赖后者）
            // 对齐 IssueFactory.TryRegisterIssue 模式
            try
            {
                var issue = new CommissionHubIssue(hero, context);

                IssueBase Factory(in PotentialIssueData pid, Hero owner) => issue;
                var pid = new PotentialIssueData(
                    Factory,
                    typeof(CommissionHubIssue),
                    IssueBase.IssueFrequency.Common);

                if (!Campaign.Current.IssueManager.CreateNewIssue(pid, hero))
                {
                    DebugLogger.Log($"[CommissionIssue] CreateNewIssue rejected for {hero.Name} — skipping");
                    return false;
                }

                _activeIssues[hero.StringId] = issue;
                DebugLogger.Log($"[CommissionIssue] Assigned CommissionHubIssue to {hero.Name} ({hero.StringId})" +
                    (context.IsCrimeEvent
                        ? $" crimeEvent={context.CrimeEventId} stage={context.CrimeEventStage}"
                        : context.IsUrgentEvent
                            ? $" urgentEvent={context.UrgentEventType}"
                            : $" category={(context.HasPrimaryCategory ? context.PrimaryCategory.ToString() : "none")}"));
                return true;
            }
            catch (System.Exception ex)
            {
                DebugLogger.Log($"[CommissionIssue] Failed to assign issue to {hero.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析 Issue 上下文：判断此 NPC 的委托来源（犯罪事件 / 世界事件 / 常规委托），
        /// 用于 CommissionHubIssue 的动态 Title/Description 展示。
        /// </summary>
        private static CommissionIssueContext ResolveIssueContext(Hero hero)
        {
            var settlement = hero.CurrentSettlement ?? hero.HomeSettlement;
            // 定居点名称兜底（无法解析时显示"本地"）
            string settlementName = settlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_issue_settlement_fallback", "the local area");

            // 1. 犯罪事件：NPC 是对应定居点犯罪事件的权威人物
            var evt = WorldEventStore.FindOnGoing(settlement?.StringId);
            if (evt != null
                && evt.Stage != EventStage.Dormant
                && evt.Stage != EventStage.Resolved
                && evt.Stage != EventStage.Unsolved)
            {
                var authority = WorldEventStore.GetAuthorityNpc(evt);
                if (authority == hero)
                {
                    string suspectName = null;
                    if (!string.IsNullOrEmpty(evt.SuspectHeroId))
                    {
                        var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                        suspectName = suspect?.Name?.ToString();
                    }
                    return new CommissionIssueContext
                    {
                        CrimeEventId = evt.EventId,
                        CrimeEventStage = evt.Stage,
                        SettlementName = settlementName,
                        SuspectName = suspectName,
                        CrimeEventType = evt.Type.ToString(),
                        CaseLabel = evt.CaseLabel,
                        DiscoveryFacts = evt.BuildDiscoveryFacts(),
                        AuthorityRole = WorldEventStore.GetAuthorityRoleDisplayName(evt),
                        WitnessCount = evt.WitnessCount,
                    };
                }
            }

            // 2. 世界事件：NPC 自身有紧迫事件缠身
            var urgentEvent = AllNpcMemoryManager.GetMemory(hero.StringId)?.CurrentUrgentEvent;
            if (urgentEvent != null)
            {
                bool isVictim = urgentEvent.TargetHeroId == hero.StringId;
                var eventConfig = WorldEventConfig.Get(urgentEvent.Type);
                var roleCommissions = eventConfig?.GetCommissionsForRole(isVictim);
                var primaryCategory = roleCommissions?.FirstOrDefault();

                return new CommissionIssueContext
                {
                    SettlementName = settlementName,
                    UrgentEventType = urgentEvent.Type.ToString(),
                    IsEventVictim = isVictim,
                    PrimaryCategory = primaryCategory ?? default,
                    HasPrimaryCategory = primaryCategory != null,
                };
            }

            // 3. 常规委托：取第一个可用的委托类别作为展示
            var availableDefs = CommissionGenerator.GetAvailableDefsForHero(hero);
            var firstCategory = availableDefs?.FirstOrDefault()?.Category;

            return new CommissionIssueContext
            {
                SettlementName = settlementName,
                PrimaryCategory = firstCategory ?? default,
                HasPrimaryCategory = firstCategory != null,
            };
        }

        private void OnDailyTick()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _activeIssues)
            {
                var hero = Hero.FindFirst(h => h.StringId == kvp.Key);
                if (hero == null || !hero.IsAlive || !CommissionGenerator.HasCommissionsFor(hero, out _))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
            {
                _activeIssues.Remove(id);
                var hero = Hero.FindFirst(h => h.StringId == id);
                if (hero != null && hero.Issue is CommissionHubIssue issue)
                {
                    Campaign.Current.IssueManager.DeactivateIssue(issue);
                }
            }

            // 定期全量清理已死亡/不存在的 Hero 条目
            if (_activeIssues.Count > 100)
            {
                var allDead = new List<string>();
                foreach (var kvp in _activeIssues)
                {
                    var h = Hero.FindFirst(x => x.StringId == kvp.Key);
                    if (h == null || !h.IsAlive)
                        allDead.Add(kvp.Key);
                }
                foreach (var id in allDead)
                {
                    _activeIssues.Remove(id);
                    var hero = Hero.FindFirst(h => h.StringId == id);
                    if (hero != null && hero.Issue is CommissionHubIssue issue2)
                    {
                        Campaign.Current.IssueManager.DeactivateIssue(issue2);
                    }
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            // CommissionIssueBehavior 不需要持久化——委托状态由 CommissionQuest 管理
        }
    }
}

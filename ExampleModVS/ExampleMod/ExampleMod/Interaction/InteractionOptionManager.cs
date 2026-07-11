using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public enum InteractionOptionType
    {
        // 通用
        Chat,               // 寒暄/闲聊
        Leave,              // 离开
        Info,   //查看情报

        // 个人/社交
        ProposalMarriage,           // 求婚
        Gift,               // 送礼
        TeaCeremony,        // 茶席
        Spar,               // 手合 (切磋)
        StudySkill,         // 修业 (学习技能)

        // 公务 (对上级)
        ReportMission,      // 任务汇报 (提交主命)
        RequestFunds,       // 请求军资
        Resign,             // 请辞/下野

        //公务（还未入仕）
        RequestWork,     // 仕官

        // 委托/工作
        FindWork,        // 找工作（NPC委托）

        // 公务 (对同僚/下下属)
        Slander,            // 流言
        SolicitSupport,     // 拉拢 (请求支持)

        // 外交/谍报 (对外部)
        RecruitHero,        // 劝诱 (招募)
        Betrayal,           // 策反

        //特殊
        Assault,            // 斩杀 (街头袭击)
        Order,             // 命令（询问士兵状态，触发士兵对话）
        Order_Follow,        // 跟随（命令士兵跟随自己）
        RecruitSoldier,      // 招募平民入伍
        PersuadeSurrender,   // 劝降敌方士兵

        // ═══ 新增：问责/犯罪（从 AccountabilityOptionType 迁移） ═══
        PayRestitution,      // 赔钱消灾
        CharmDefense,        // Charm 辩护
        FrameSuspect,        // 栽赃嫁祸
        Threat,              // 威胁
        Investigate,         // 接调查 Quest
        Confess,             // 自首
        SilenceWitness,      // 封口目击者
        LeadRetaliation,     // 带队报复
        WorkOffDebt,         // 干活抵债
        BetrayQuest,         // 背叛 Quest
        InnocenceProof,      // 自证清白
        Settle,              // 和解劝说
        AcceptBountyQuest,   // 接悬赏 Quest
        ContinueChat,        // 继续聊（对话导航，事件Resolved后Hide）
        LureArrest,          // 诱捕
        Arrest,              // 直接抓捕
        SurrenderJail,       // 束手就擒坐牢（Alert 场景）
        Comply,              // 服从（收武器/停止可疑行为，Alert 场景）
    }

    /// <summary>
    /// 选项分类 (用于UI分组显示)
    /// </summary>
    public enum InteractionCategory
    {
        General,    // 基础
        Social,     // 社交/个人
        Official,   // 公务/主命
        Diplomacy,  // 外交/谋略
        Hostile,    // 敌对/暴力
        Accountability, // 🆕 犯罪追责
    }

    /// <summary>Type → Category 唯一权威映射。新 Type 在此加一行即可。</summary>
    public static class InteractionOptionCategoryMap
    {
        public static InteractionCategory GetCategory(InteractionOptionType type)
        {
            switch (type)
            {
                // 社交
                case InteractionOptionType.ProposalMarriage:
                case InteractionOptionType.Gift:
                case InteractionOptionType.TeaCeremony:
                case InteractionOptionType.Spar:
                case InteractionOptionType.StudySkill:
                    return InteractionCategory.Social;
                // 公务
                case InteractionOptionType.ReportMission:
                case InteractionOptionType.RequestFunds:
                case InteractionOptionType.Resign:
                case InteractionOptionType.RequestWork:
                case InteractionOptionType.Order:
                case InteractionOptionType.RecruitSoldier:
                case InteractionOptionType.FindWork:
                    return InteractionCategory.Official;
                // 外交
                case InteractionOptionType.RecruitHero:
                case InteractionOptionType.Betrayal:
                case InteractionOptionType.Slander:
                case InteractionOptionType.SolicitSupport:
                    return InteractionCategory.Diplomacy;
                // 敌对
                case InteractionOptionType.Assault:
                    return InteractionCategory.Hostile;
                // 🆕 犯罪追责
                case InteractionOptionType.PayRestitution:
                case InteractionOptionType.CharmDefense:
                case InteractionOptionType.FrameSuspect:
                case InteractionOptionType.Threat:
                case InteractionOptionType.Investigate:
                case InteractionOptionType.Confess:
                case InteractionOptionType.SilenceWitness:
                case InteractionOptionType.LeadRetaliation:
                case InteractionOptionType.WorkOffDebt:
                case InteractionOptionType.BetrayQuest:
                case InteractionOptionType.InnocenceProof:
                case InteractionOptionType.Settle:
                case InteractionOptionType.AcceptBountyQuest:
                case InteractionOptionType.LureArrest:
                case InteractionOptionType.Arrest:
                case InteractionOptionType.SurrenderJail:
                case InteractionOptionType.Comply:
                    return InteractionCategory.Accountability;
                // 通用（Chat / Leave / Info / Order_Follow / 及未来新增默认）
                default:
                    return InteractionCategory.General;
            }
        }
    }

    /// <summary>
    /// 薄壳：把意图注册表(IntentRegistry)的可见结果转成 UI 选项(StoryOptionVM)。
    /// 选项的「能不能选 / 成不成」逻辑全在各意图类里，这里只负责取数 + 转 VM + 显示成功率/置灰。
    /// 加新选项 = 新建意图类 + IntentRegistry 注册一行，无需改本文件。
    /// </summary>
    public class InteractionOptionManager
    {
        private readonly InteractionController _controller;

        public InteractionOptionManager(InteractionController ctrl)
        {
            _controller = ctrl;
            IntentRegistry.EnsureInitialized();
        }

        /// <summary>构建当前对话对象可见的选项 VM 列表（含置灰）。</summary>
        public StoryOptionVM[] BuildOptionVMs(Agent targetAgent, IntentSource sourceFilter = IntentSource.Player)
        {
            var ctx = new IntentContext(targetAgent, _controller);
            var visible = IntentRegistry.GetVisible(ctx);
            var vmList = new List<StoryOptionVM>();

            foreach (var pair in visible)
            {
                IntentBase intent = pair.Key;

                // 🆕 IntentSource 过滤：默认只显示玩家可用意图
                if ((intent.Source & sourceFilter) == 0)
                    continue;

                Eligibility elig = pair.Value;
                bool enabled = elig.State == EligState.Enabled;

                string text = intent.DisplayName;
                string predict = intent.ToolTip;

                // 对抗类且可用 → 点击前先算并显示成功率（太阁5 式预览）
                if (enabled && intent.Goal.HasValue)
                {
                    try
                    {
                        RollResult rr = SingleRollResolver.Compute(ctx, intent.Goal.Value, intent.Tactic, intent.GetOfferValue(ctx));
                        text = $"{intent.DisplayName}（{rr.Chance * 100f:0}%）";
                        predict = string.IsNullOrEmpty(intent.ToolTip)
                            ? $"成功率 {rr.Chance * 100f:0}%"
                            : $"{intent.ToolTip}\n成功率 {rr.Chance * 100f:0}%";
                    }
                    catch { }
                }

                if (!enabled)
                    text = "[不可用] " + intent.DisplayName;

                // 闭包捕获
                IntentBase capturedIntent = intent;
                IntentContext capturedCtx = ctx;

                var vm = new StoryOptionVM(
                    text,
                    () => _controller.DispatchIntent(capturedIntent, capturedCtx),
                    predict);
                vm.IsEnabled = enabled;
                vm.DisableReason = elig.Reason;
                vmList.Add(vm);
            }

            vmList.Reverse(); // 与既有 UI 渲染顺序保持一致
            return vmList.ToArray();
        }
    }
}

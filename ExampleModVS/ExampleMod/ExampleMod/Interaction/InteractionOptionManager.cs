using System.Collections.Generic;
using LivingWorldNpcs.Story;
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
        Hostile     // 敌对/暴力
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
        public StoryOptionVM[] BuildOptionVMs(Agent targetAgent)
        {
            var ctx = IntentContext.Build(targetAgent, _controller);
            var visible = IntentRegistry.GetVisible(ctx);
            var vmList = new List<StoryOptionVM>();

            foreach (var pair in visible)
            {
                IntentBase intent = pair.Key;
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

    /// <summary>
    /// 据点荣誉存储：按 Settlement.StringId 记录玩家在各据点的荣誉值。
    /// 持久化经 MyBehavior.SyncData（JSON 序列化）。
    /// Modify() 可正可负，后续坏事件扣、任务完成涨均走同一入口。
    /// </summary>
    public static class SettlementHonorStore
    {
        private static Dictionary<string, int> _honor = new Dictionary<string, int>();

        public static int Get(Settlement s)
        {
            if (s == null) return 0;
            return Get(s.StringId);
        }

        public static int Get(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return 0;
            _honor.TryGetValue(settlementId, out int v);
            return v;
        }

        public static void Modify(Settlement s, int delta)
        {
            if (s == null) return;
            int cur = Get(s.StringId);
            Set(s, cur + delta);
        }

        public static void Set(Settlement s, int value)
        {
            if (s == null) return;
            _honor[s.StringId] = value;
        }

        public static string Serialize()
        {
            try { return Newtonsoft.Json.JsonConvert.SerializeObject(_honor); }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                var d = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                _honor = d ?? new Dictionary<string, int>();
            }
            catch { _honor = new Dictionary<string, int>(); }
        }
    }
}

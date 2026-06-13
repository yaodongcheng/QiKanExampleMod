using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs.Story
{
    public class RequestCommissionIntent : IntentBase
    {
        private int _cachedCount = 0;

        public override InteractionOptionType Type => InteractionOptionType.FindWork;
        public override string DisplayName =>
            _cachedCount > 0
                ? $"【找工作】 打听委托（{_cachedCount}个可接）"
                : "【找工作】 打听委托";
        public override string ToolTip => "向对方打听是否有委托可接";
        public override float CooldownDays => 0.5f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.IsHero) return Eligibility.Hide();
            if (ctx.Hero == null) return Eligibility.Hide();
            if (!CommissionGenerator.HasCommissionsFor(ctx.Hero, out int count) || count <= 0)
            {
                _cachedCount = 0;
                return Eligibility.Hide();
            }
            _cachedCount = count;
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Hero == null) return;

            var commissions = CommissionGenerator.GenerateCommissions(ctx.Hero, 4);
            if (commissions == null || commissions.Count == 0)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"{ctx.Hero.Name} 目前没有合适的委托给你。"));
                return;
            }

            if (commissions.Count == 1)
                ShowConfirmInquiry(commissions, 0, ctx);
            else
                ShowOverviewThenBrowse(commissions, ctx);
        }

        private void ShowOverviewThenBrowse(List<CommissionData> commissions, IntentContext ctx)
        {
            string intro = CommissionNarrative.GetIntroduction();
            string status = CommissionNarrative.GetPlayerStatusHeader();

            string body = (intro != null ? intro + "\n\n" : "") + status;
            bool isBrokerBoard = commissions.Any(c => c.BrokerHero != null);
            body += isBrokerBoard
                ? $"📋 {ctx.Hero.Name} 的告示板（共 {commissions.Count} 条委托情报）：\n\n"
                : $"{ctx.Hero.Name} 的委托清单（共 {commissions.Count} 个）：\n\n";

            for (int i = 0; i < commissions.Count; i++)
            {
                var c = commissions[i];
                string days = ((int)(c.TimeRemainingHours / 24f) + 1).ToString();
                string tier = GetTierShortName(c.Tier);
                body += $"[{i + 1}] {tier} {c.GetFlavorDescription()}\n";
                if (c.BrokerHero != null && c.BrokerHero != c.QuestGiver)
                {
                    string giverLoc = c.QuestGiver?.CurrentSettlement?.Name?.ToString()
                        ?? c.QuestGiver?.HomeSettlement?.Name?.ToString() ?? "未知地点";
                    body += $"    委托人：{c.QuestGiver?.Name}（在 {giverLoc}）\n";
                }
                body += $"    报酬：{c.NegotiatedReward}G | 定金：{c.DepositAmount}G | 期限：{days}天\n\n";
            }
            body += isBrokerBoard
                ? "从告示板接取后，需先找到真正的委托人听取详情，再决定是否接。"
                : "点击「下一个」逐个浏览委托详情，选择接取或跳过。";

            InformationManager.ShowInquiry(new InquiryData(
                $"委托任务 — {ctx.Hero.Name}",
                body,
                true, true,
                "下一个 →", "取消",
                () => ShowConfirmInquiry(commissions, 0, ctx),
                null));
        }

        private void ShowConfirmInquiry(List<CommissionData> commissions, int index, IntentContext ctx)
        {
            if (index >= commissions.Count) return;
            var c = commissions[index];
            string tier = GetTierFullName(c.Tier);

            string info = $"委托 {index + 1}/{commissions.Count}\n\n" +
                          $"{c.GetFlavorDescription()}\n\n" +
                          $"难度：{tier}\n报酬：{c.NegotiatedReward} 第纳尔\n" +
                          $"定金：{c.DepositAmount} 第纳尔\n" +
                          $"期限：{((int)(c.TimeRemainingHours / 24f) + 1)} 天\n" +
                          $"委托人：{ctx.Hero.Name}\n\n" +
                          (commissions.Count > 1
                              ? "「接取」接受此委托 | 「下一个」查看其他 | 「取消」放弃"
                              : "「接取」接受此委托 | 「取消」放弃");

            string affirmText = "接取";
            string negText = commissions.Count > 1 && index < commissions.Count - 1 ? "下一个 →" : "取消";

            InformationManager.ShowInquiry(new InquiryData(
                $"委托详情 ({index + 1}/{commissions.Count})",
                info,
                true, true,
                affirmText, negText,
                () => AcceptCommission(c, ctx),
                () =>
                {
                    if (index < commissions.Count - 1)
                        ShowConfirmInquiry(commissions, index + 1, ctx);
                }));
        }

        private void AcceptCommission(CommissionData data, IntentContext ctx)
        {
            if (data == null || ctx.Hero == null) return;

            // ── 告示板接取：创建委托但进入叙事阶段，不启动 ──
            if (data.BrokerHero != null && data.BrokerHero != data.QuestGiver)
            {
                string giverLoc = data.QuestGiver?.CurrentSettlement?.Name?.ToString()
                    ?? data.QuestGiver?.HomeSettlement?.Name?.ToString() ?? "未知地点";

                data.IsNarrativePhase = true;
                string qId = $"commission_{data.DefId}_{data.QuestGiver.StringId}_{System.DateTime.Now.Ticks}";
                var pendingQuest = new CommissionQuest(qId, data);
                pendingQuest.StartQuest();
                pendingQuest.BeginNarrativePhase();

                InformationManager.DisplayMessage(new InformationMessage(
                    $"📋 委托情报已记录：{data.GetFlavorDescription()}", Colors.Green));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"去 {giverLoc} 找 {data.QuestGiver?.Name} 当面了解详情。", Colors.Cyan));
                return;
            }

            // ── 直接委托：正常启动 ──
            int trust = TrustSystem.GetTrust(ctx.Hero);
            int maxQuests = TrustSystem.GetMaxConcurrentQuests(trust);
            int activeCount = CommissionQuest.GetActiveCommissionCount();
            if (activeCount >= maxQuests)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"你已经有 {activeCount} 个进行中的委托（上限 {maxQuests}），先完成一些再来。", Colors.Red));
                return;
            }

            int finalReward = CommissionGenerator.NegotiateReward(data.NegotiatedReward, ctx.Hero);
            data.NegotiatedReward = finalReward;
            data.DepositAmount = (int)(finalReward * TrustSystem.GetDepositRatio(trust));
            data.IsNarrativePhase = false;

            int actualDeposit = AgentControlHelper.TransferGold(ctx.Hero, Hero.MainHero, data.DepositAmount);
            data.DepositAmount = actualDeposit;

            string questId = $"commission_{data.DefId}_{ctx.Hero.StringId}_{System.DateTime.Now.Ticks}";
            var quest = new CommissionQuest(questId, data);
            quest.StartQuest();

            string doneMsg = $"接取了委托：{data.GetFlavorDescription()}\n" +
                             $"定金 {actualDeposit} 第纳尔已到账。报酬 {finalReward} 第纳尔（完成时支付）。\n" +
                             $"当前活跃委托：{CommissionQuest.GetActiveCommissionCount()} 个";

            if (Settings.Instance.IsLLMReady)
                _ = EnhanceWithLLM(doneMsg);

            InformationManager.DisplayMessage(new InformationMessage(doneMsg, Colors.Green));
            InformationManager.DisplayMessage(
                new InformationMessage($"{ctx.Hero.Name}：这事就拜托你了！"));

            if (InfamySystem.Infamy >= 5)
                InformationManager.DisplayMessage(
                    new InformationMessage($"提示：你的恶名（{InfamySystem.Infamy}）可能影响日后接正经委托。", Colors.Yellow));
        }

        private static string GetTierShortName(CommissionTier tier)
        {
            if (tier == CommissionTier.Basic) return "$";
            if (tier == CommissionTier.Skilled) return "$$";
            if (tier == CommissionTier.Expert) return "$$$";
            if (tier == CommissionTier.Legendary) return "★★★★";
            return "$";
        }

        private static string GetTierFullName(CommissionTier tier)
        {
            if (tier == CommissionTier.Basic) return "$ 简单";
            if (tier == CommissionTier.Skilled) return "$$ 普通";
            if (tier == CommissionTier.Expert) return "$$$ 困难";
            if (tier == CommissionTier.Legendary) return "★★★★ 传奇";
            return "$ 简单";
        }

        private async System.Threading.Tasks.Task EnhanceWithLLM(string baseMessage)
        {
            try
            {
                string prompt = $"给这句委托确认消息加一点风味描写（保持简短，不要改核心信息）：\n{baseMessage}";
                string result = await LLMService.Instance.ChatAsync(prompt, 80, false);
                if (!string.IsNullOrEmpty(result))
                    InformationManager.DisplayMessage(new InformationMessage(result.Trim(), Colors.Cyan));
            }
            catch { }
        }
    }

    /// <summary>
    /// 确认委托 Intent：当玩家带着从告示板获取的情报找到真正的委托人时，
    /// 出现此选项——玩家听完委托人的前因后果后，决定接或不接。
    /// </summary>
    public class ConfirmCommissionIntent : IntentBase
    {
        private string _pendingDesc = "";

        public override InteractionOptionType Type => InteractionOptionType.FindWork;
        public override string DisplayName =>
            string.IsNullOrEmpty(_pendingDesc)
                ? "【委托详情】 听说是你需要帮手？"
                : $"【委托详情】 关于「{_pendingDesc}」";
        public override string ToolTip => "从告示板得知此人有委托，当面了解详情";
        public override float CooldownDays => 0f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.IsHero || ctx.Hero == null) return Eligibility.Hide();
            var pending = CommissionQuest.FindPendingCommissionForGiver(ctx.Hero);
            if (pending == null) return Eligibility.Hide();

            _pendingDesc = pending.Data?.GetFlavorDescription() ?? "委托";
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Hero == null) return;
            var pending = CommissionQuest.FindPendingCommissionForGiver(ctx.Hero);
            if (pending == null) return;

            var data = pending.Data;
            string story = GenerateNarrative(data);
            string giverName = ctx.Hero.Name != null ? ctx.Hero.Name.ToString() : "委托人";
            string info = $"{giverName} 看着你，开始讲述：\n\n" +
                          $"「{story}」\n\n" +
                          $"──\n报酬：{data.NegotiatedReward} 第纳尔\n" +
                          $"定金：{data.DepositAmount} 第纳尔（确认后支付）\n" +
                          $"期限：{((int)(data.TimeRemainingHours / 24f) + 1)} 天\n\n" +
                          $"听完这些，你决定——";

            InformationManager.ShowInquiry(new InquiryData(
                $"委托详情 — {giverName}",
                info,
                true, true,
                "接下委托", "婉拒",
                () =>
                {
                    int finalReward = CommissionGenerator.NegotiateReward(data.NegotiatedReward, ctx.Hero);
                    data.NegotiatedReward = finalReward;
                    int trust = TrustSystem.GetTrust(ctx.Hero);
                    data.DepositAmount = (int)(finalReward * TrustSystem.GetDepositRatio(trust));
                    pending.ConfirmQuest();
                    InformationManager.DisplayMessage(
                        new InformationMessage($"接取了委托：{data.GetFlavorDescription()} —— 报酬 {finalReward} 第纳尔。", Colors.Green));
                    InformationManager.DisplayMessage(
                        new InformationMessage($"{giverName}：拜托你了！"));
                },
                () =>
                {
                    pending.CompleteQuestWithFail();
                    InformationManager.DisplayMessage(
                        new InformationMessage($"你婉拒了 {giverName} 的委托。"));
                }));
        }

        private string GenerateNarrative(CommissionData data)
        {
            if (data == null) return "我需要有人帮我办一件事。";
            string targetName = data.TargetHero != null ? data.TargetHero.Name.ToString() : "目标";

            switch (data.Category)
            {
                case CommissionCategory.BountyHunt:
                    return $"那个叫 {targetName} 的家伙，最近在这一带作恶多端。"
                         + "我出赏金，你出力——把他揪出来。活的最好，死的也行。";
                case CommissionCategory.CaravanEscort:
                    return "我有一批货必须安全送到。路上盗匪猖獗，我一个人不敢走。"
                         + "你护送我的商队，平安到了我付你报酬。";
                case CommissionCategory.SupplyEmergency:
                    return "城里急缺物资，再不补给我这生意就做不下去了。"
                         + "帮我去别的城镇采购一批回来，越快报酬越高。";
                case CommissionCategory.UndergroundFight:
                    return "我在竞技场下了注，但我的拳手昨晚摔断了腿。"
                         + "你替我去打——赢了奖金对半分，输了算我的。";
                case CommissionCategory.VillageDefense:
                    return "匪徒盯上了我们的村子。他们已经在路上了！"
                         + "帮我们守住——你要能在半路截住他们更好。";
                case CommissionCategory.PrisonBreak:
                    return $"我的朋友 {targetName} 被关进了监狱。"
                         + "他不是罪犯，是被人陷害的。帮我把他弄出来。";
                case CommissionCategory.SupplyIntercept:
                    return "敌方的补给队正在运物资到前线。截下这批货——"
                         + "你可以交给我换报酬，也可以自己留着。";
                case CommissionCategory.DecoyMission:
                    return "有人在追杀我。他们快到了。"
                         + "你带少量人引开追兵的注意，我趁机跑——你坚持得越久，我逃得越远，报酬越高。";
                default:
                    return "这事说来话长……总之，我需要一个信得过的人帮我这个忙。报酬不会少你的。";
            }
        }
    }
}

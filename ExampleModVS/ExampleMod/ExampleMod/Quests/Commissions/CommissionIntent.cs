using System.Collections.Generic;
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
            if (ctx.Target == null) return Eligibility.Hide();
            if (!CommissionGenerator.HasCommissionsFor(ctx.Target, out int count) || count <= 0)
            {
                _cachedCount = 0;
                return Eligibility.Hide();
            }
            _cachedCount = count;
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Target == null) return;

            var commissions = CommissionGenerator.GenerateCommissions(ctx.Target, 4);
            if (commissions == null || commissions.Count == 0)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"{ctx.Target.Name} 目前没有合适的委托给你。"));
                return;
            }

            if (commissions.Count == 1)
                ShowConfirmInquiry(commissions, 0, ctx);
            else
                ShowOverviewThenBrowse(commissions, ctx);
        }

        private void ShowOverviewThenBrowse(List<CommissionData> commissions, IntentContext ctx)
        {
            // 首次介绍
            string intro = CommissionNarrative.GetIntroduction();
            string status = CommissionNarrative.GetPlayerStatusHeader();

            string body = (intro != null ? intro + "\n\n" : "") +
                          status +
                          $"{ctx.Target.Name} 的委托清单（共 {commissions.Count} 个）：\n\n";
            for (int i = 0; i < commissions.Count; i++)
            {
                var c = commissions[i];
                string days = ((int)(c.TimeRemainingHours / 24f) + 1).ToString();
                string tier = GetTierShortName(c.Tier);
                body += $"[{i + 1}] {tier} {c.GetFlavorDescription()}\n";
                body += $"    报酬：{c.NegotiatedReward}G | 定金：{c.DepositAmount}G | 期限：{days}天\n\n";
            }
            body += "点击「下一个」逐个浏览委托详情，选择接取或跳过。";

            InformationManager.ShowInquiry(new InquiryData(
                $"委托任务 — {ctx.Target.Name}",
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
                          $"委托人：{ctx.Target.Name}\n\n" +
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
            if (data == null || ctx.Target == null) return;

            // 检查并发上限（按 Trust 等级限制全局活跃数 + 按 NPC 限制发布数）
            int trust = TrustSystem.GetTrust(ctx.Target);
            int maxQuests = TrustSystem.GetMaxConcurrentQuests(trust);
            int activeCount = CommissionQuest.GetActiveCommissionCount();
            if (activeCount >= maxQuests)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"你已经有 {activeCount} 个进行中的委托（上限 {maxQuests}），先完成一些再来。", Colors.Red));
                return;
            }

            // 讨价还价
            int finalReward = CommissionGenerator.NegotiateReward(data.NegotiatedReward, ctx.Target);
            data.NegotiatedReward = finalReward;
            data.DepositAmount = (int)(finalReward * TrustSystem.GetDepositRatio(trust));

            // 定金到账
            int actualDeposit = AgentControlHelper.TransferGold(ctx.Target, Hero.MainHero, data.DepositAmount);
            data.DepositAmount = actualDeposit;

            // 创建并启动委托
            string questId = $"commission_{data.DefId}_{ctx.Target.StringId}_{System.DateTime.Now.Ticks}";
            var quest = new CommissionQuest(questId, data);
            quest.StartQuest();

            string confirmMsg = $"接取了委托：{data.GetFlavorDescription()}\n" +
                                $"定金 {actualDeposit} 第纳尔已到账。报酬 {finalReward} 第纳尔（完成时支付）。\n" +
                                $"当前活跃委托：{CommissionQuest.GetActiveCommissionCount()} 个";

            if (Settings.Instance.IsLLMReady)
                _ = EnhanceWithLLM(confirmMsg);

            InformationManager.DisplayMessage(new InformationMessage(confirmMsg, Colors.Green));
            InformationManager.DisplayMessage(
                new InformationMessage($"{ctx.Target.Name}：这事就拜托你了！"));

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
}

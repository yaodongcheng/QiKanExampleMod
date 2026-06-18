using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs.Story
{
    public class RequestCommissionIntent : IntentBase
    {
        private int _cachedCount = 0;
        private bool _hasUrgentEvent = false;

        public override InteractionOptionType Type => InteractionOptionType.FindWork;
        public override string DisplayName =>
            _hasUrgentEvent
                ? "【关于当前的事】 我能帮上什么忙？"
                : _cachedCount > 0
                    ? $"【找工作】 打听委托（{_cachedCount}个可接）"
                    : "【找工作】 打听委托";
        public override string ToolTip =>
            _hasUrgentEvent
                ? "对方正被事件困扰——询问需要你做什么"
                : "向对方打听是否有委托可接";
        public override float CooldownDays => 0.5f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.IsHero) return Eligibility.Hide();
            if (ctx.Hero == null) return Eligibility.Hide();
            _hasUrgentEvent = ctx.HasUrgentWorldEvent;
            if (!CommissionGenerator.HasCommissionsFor(ctx.Hero, out int count) || count <= 0)
            {
                _cachedCount = 0;
                return Eligibility.Hide();
            }
            _cachedCount = count;
            DebugLogger.Log($"[CommissionIntent] RequestCommission Evaluate: hero={ctx.Hero.Name} count={count} urgentEvent={_hasUrgentEvent} → Show");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Hero == null) return;

            var commissions = CommissionGenerator.GenerateCommissions(ctx.Hero, 4);
            DebugLogger.Log($"[CommissionIntent] RequestCommission OnInstant: hero={ctx.Hero.Name} generated={commissions?.Count ?? 0}");

            if (commissions == null || commissions.Count == 0)
            {
                // NPC 自然说没有委托
                if (ctx.Controller != null)
                    ctx.Controller.SceneSay("我这儿暂时没有需要帮手的活计。",
                        new StoryOptionVM("（离开）", () => ctx.Controller.CloseDialogue()));
                else
                    InformationManager.DisplayMessage(
                        new InformationMessage($"{ctx.Hero.Name} 目前没有合适的委托给你。"));
                return;
            }

            // 中转人（村长/酒馆老板/浪人）→ 开口介绍 + 弹窗告示板；直接委托人 → NPC 在对话里自己说
            bool isBroker = commissions[0].BrokerHero != null && commissions[0].BrokerHero != commissions[0].QuestGiver;
            if (isBroker && ctx.Controller != null)
                ShowBrokerFlow(commissions, ctx);
            else if (ctx.Controller == null)
                ShowCommissionLetter(commissions, 0, ctx);
            else
                ShowCommissionInDialogue(commissions, 0, ctx);
        }

        /// <summary>
        /// 中转人路径：中转人先开口介绍（SceneSay），再弹出告示板（Inquiry）列出周边委托。
        /// 中转人本人不是委托人，只是消息灵通的人。
        /// </summary>
        private void ShowBrokerFlow(List<CommissionData> commissions, IntentContext ctx)
        {
            var ic = ctx.Controller;
            string brokerName = ctx.Hero?.Name?.ToString() ?? "他";

            // 1. 中转人开口
            string intro = commissions.Count > 1
                ? "想找活干？这一带还真有几个人手头缺人。来，我给你念叨念叨——"
                : "想找活干？倒是有这么一桩，你听听看——";
            ic.SceneSay(intro);

            // 2. 弹出告示板（中转人嘴上说，手上把活计单子递给你）
            ShowCommissionLetter(commissions, 0, ctx);
        }

        /// <summary>
        /// 直接委托人路径：NPC 在对话里自己说出委托，选项行内呈现，不弹窗。
        /// </summary>
        private void ShowCommissionInDialogue(List<CommissionData> commissions, int index, IntentContext ctx)
        {
            var ic = ctx.Controller;
            if (ic == null) { ShowCommissionLetter(commissions, index, ctx); return; }
            if (index >= commissions.Count) { ic.CloseDialogue(); return; }

            var c = commissions[index];
            NPCProfile giverProfile = BuildProfileFromHero(c.QuestGiver);

            DebugLogger.Log($"[CommissionIntent] ShowCommissionInDialogue: before BuildOpening index={index} category={c.Category} giver={c.QuestGiver?.Name}");
            string narrative = CommissionNarrative.BuildOpening(c, giverProfile);
            DebugLogger.Log($"[CommissionIntent] ShowCommissionInDialogue: BuildOpening done, narrative len={narrative?.Length ?? 0}");

            string days = ((int)(c.TimeRemainingHours / 24f) + 1).ToString();
            string terms = $"（报酬 {c.NegotiatedReward} 第纳尔 · 期限 {days} 天"
                         + (c.DepositAmount > 0 ? $" · 定金 {c.DepositAmount}" : "") + "）";
            string line = $"{narrative}\n{terms}";

            var options = new List<StoryOptionVM>();
            options.Add(new StoryOptionVM("这活我接了", () =>
            {
                AcceptCommission(c, ctx);
                ic.CloseDialogue();
            }));
            if (index < commissions.Count - 1)
                options.Add(new StoryOptionVM("还有别的活吗？", () => ShowCommissionInDialogue(commissions, index + 1, ctx)));
            options.Add(new StoryOptionVM("我再想想", () => ic.CloseDialogue()));

            DebugLogger.Log($"[CommissionIntent] ShowCommissionInDialogue: before SceneSay line len={line?.Length ?? 0}");
            ic.SceneSay(line, options.ToArray());
            DebugLogger.Log($"[CommissionIntent] ShowCommissionInDialogue: after SceneSay OK");
        }

        /// <summary>
        /// 以"信"的格式逐条展示委托（替代原来的总览→逐条）。
        /// 每条委托以第一人称叙事呈现，左按钮"接取"，右按钮"下一个 →"（末尾"合上"）。
        /// </summary>
        private void ShowCommissionLetter(List<CommissionData> commissions, int index, IntentContext ctx)
        {
            if (index >= commissions.Count) return;
            var c = commissions[index];

            // 构建第一人称叙事：从 CSV 模板中匹配
            string giverName = c.QuestGiver?.Name?.ToString() ?? "委托人";
            NPCProfile giverProfile = BuildProfileFromHero(c.QuestGiver);
            string narrativeText = CommissionNarrative.BuildOpening(c, giverProfile);

            string tier = GetTierFullName(c.Tier);
            string days = ((int)(c.TimeRemainingHours / 24f) + 1).ToString();

            bool isBroker = c.BrokerHero != null && c.BrokerHero != c.QuestGiver;
            string headerLine = isBroker
                ? $"📜 委托 {index + 1}/{commissions.Count} —— {giverName} 来信"
                : $"📜 委托 {index + 1}/{commissions.Count} —— {giverName} 的委托";

            string letter = $"{headerLine}\n\n" +
                           $"「{narrativeText}」\n\n" +
                           $"──\n" +
                           $"难度：{tier}\n" +
                           $"报酬：{c.NegotiatedReward} 第纳尔\n" +
                           $"期限：{days} 天\n" +
                           (c.DepositAmount > 0 ? $"定金：{c.DepositAmount} 第纳尔\n" : "") +
                           (isBroker
                               ? $"\n委托人：{giverName}（在 {GetGiverLocation(c)}）\n接取后需先找到委托人当面了解详情。"
                               : "");

            bool isLast = index >= commissions.Count - 1;
            string negText = isLast ? "合上" : "下一个 →";

            InformationManager.ShowInquiry(new InquiryData(
                $"委托 — {ctx.Hero.Name}",
                letter,
                true, true,
                "接取",
                negText,
                () =>
                {
                    AcceptCommission(c, ctx);
                    // 中转人接取后，当面告诉你去哪找委托人
                    if (isBroker && ctx.Controller != null)
                    {
                        string giverName = c.QuestGiver?.Name?.ToString() ?? "那人";
                        string giverLoc = GetGiverLocation(c);
                        string dir = $"这桩活是 {giverName} 的事。你去 {giverLoc} 找他，当面把来龙去脉问清楚——他会告诉你具体情况。";
                        ctx.Controller.SceneSay(dir,
                            new StoryOptionVM("知道了", () => ctx.Controller.CloseDialogue()));
                    }
                },
                () =>
                {
                    if (!isLast)
                    {
                        ShowCommissionLetter(commissions, index + 1, ctx);
                    }
                    else
                    {
                        // 🐛 修复：「合上」后对话卡死——需要恢复选项或关闭对话
                        if (ctx.Controller != null)
                        {
                            string closeLine = isBroker
                                ? "这些就是眼下能打听到的活了。有想接的随时跟我说。"
                                : "你慢慢考虑，想好了随时来找我。";
                            ctx.Controller.SceneSay(closeLine,
                                new StoryOptionVM("我再看看", () => ctx.Controller.CloseDialogue()),
                                new StoryOptionVM("（离开）", () => ctx.Controller.CloseDialogue()));
                        }
                    }
                }));
        }

        private string GetGiverLocation(CommissionData c)
        {
            return c.QuestGiver?.CurrentSettlement?.Name?.ToString()
                ?? c.QuestGiver?.HomeSettlement?.Name?.ToString()
                ?? "未知地点";
        }

        /// <summary>
        /// 第一人称叙事生成（不走 CSV，直接用硬编码模板作为兜底）。
        /// 注意：如果 CSV 加载成功，CommissionNarrative.BuildOpening 会返回更丰富的文本。
        /// </summary>
        private string GenerateCommissionNarrative(CommissionData data)
        {
            if (data == null) return "我需要有人帮我办一件事。";
            string targetName = data.TargetHero != null ? data.TargetHero.Name.ToString() : "目标";
            string settlementName = !string.IsNullOrEmpty(data.TargetSettlementId)
                ? Settlement.Find(data.TargetSettlementId)?.Name?.ToString() ?? "目的地"
                : "某地";
            string itemName = !string.IsNullOrEmpty(data.TargetItemId)
                ? MBObjectManager.Instance.GetObject<ItemObject>(data.TargetItemId)?.Name?.ToString() ?? "某物"
                : "某物";

            switch (data.Category)
            {
                case CommissionCategory.BountyHunt:
                    return $"有个叫 {targetName} 的家伙最近在这一带作恶多端。我出赏金，你出力——把他揪出来，活的最好死的也行。";
                case CommissionCategory.VillageDefense:
                    return $"匪徒盯上了 {settlementName}！村里能打的都走了……帮我们守住——能在半路截住他们更好。";
                case CommissionCategory.CaravanEscort:
                    return $"我有一批货要运到 {settlementName}，但路上不太平。你护送我的商队，平安到了我付你报酬。";
                case CommissionCategory.SupplyEmergency:
                    return $"{settlementName} 急缺 {itemName}，再不补给生意就做不下去了。帮我去市场采购一批回来，越快报酬越高。";
                case CommissionCategory.PrisonBreak:
                    return $"我的朋友 {targetName} 被关在 {settlementName} 的监狱里——他是被冤枉的。帮我把他救出来。";
                case CommissionCategory.SupplyIntercept:
                    return $"敌方补给队正运物资去 {settlementName}。截下这批货——交给我换报酬，或者你自己留着。";
                case CommissionCategory.LegendaryHunt:
                    return $"{targetName}——横行多年的匪王，身上带着独一无二的装备。击败他，装备归你，另有重赏。";
                case CommissionCategory.LostItem:
                    return $"我的 {itemName} 被偷了！最后有人看见小偷往 {settlementName} 方向跑了。帮我把东西找回来，必有重谢。";
                case CommissionCategory.HideoutClear:
                    return $"{settlementName} 附近有个匪窝，商队都不敢走了。清理掉它——周围生意人都凑了份子。";
                case CommissionCategory.EmergencyDelivery:
                    return $"{settlementName} 断粮了！{((int)(data.TimeRemainingHours / 24f) + 1)} 天内把 {itemName} 送到——越快报酬越高。";
                case CommissionCategory.TreasureHunt:
                    return $"我搞到一张藏宝图，地方在 {settlementName} 附近——但我一个人不敢去。你陪我去，找到对半分。";
                case CommissionCategory.HorseAcquisition:
                    return $"我想要一匹 {itemName}。去各大城镇马市比价——预算省下来的都归你。";
                case CommissionCategory.ArenaSpecial:
                    return $"我在 {settlementName} 竞技场安排了一场特别赛——禁用盾牌，纯靠身手。连赢两场，押注赚的我们对半分。";
                case CommissionCategory.DecoyMission:
                    return $"有人在追杀我。你带少量人引开他们注意，我趁机跑——坚持越久报酬越高。";
                case CommissionCategory.ProcurementAgent:
                    return $"我需要一件 {itemName}，不方便亲自出面。给你预算，去比价——花得越少你赚得越多。";
                default:
                    return "这事说来话长……总之，我需要一个信得过的人帮我这个忙。报酬不会少你的。";
            }
        }

        private void AcceptCommission(CommissionData data, IntentContext ctx)
        {
            if (data == null || ctx.Hero == null) return;

            DebugLogger.Log($"[CommissionIntent] AcceptCommission: category={data.Category} giver={data.QuestGiver?.Name} broker={data.BrokerHero?.Name} narrativePhase={data.IsNarrativePhase}");

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

                DebugLogger.Log($"[CommissionIntent] Broker accept → narrative phase: questId={qId} giverLoc={giverLoc}");

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

            DebugLogger.Log($"[CommissionIntent] Direct accept: reward={finalReward} deposit={data.DepositAmount} trust={trust}");

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

        /// <summary>从 Hero 的游戏特质构建简易 PersonalityTraits 字符串（供 CSV 叙事匹配使用）</summary>
        public static NPCProfile BuildProfileFromHero(Hero hero)
        {
            var profile = new NPCProfile(hero);
            return profile;
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
            DebugLogger.Log($"[CommissionIntent] ConfirmCommission Evaluate: hero={ctx.Hero.Name} pending={_pendingDesc}");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Hero == null) return;
            var pending = CommissionQuest.FindPendingCommissionForGiver(ctx.Hero);
            if (pending == null) return;

            var data = pending.Data;
            string giverName = ctx.Hero.Name != null ? ctx.Hero.Name.ToString() : "委托人";
            var giverProfile = RequestCommissionIntent.BuildProfileFromHero(ctx.Hero);
            string story = CommissionNarrative.BuildOpening(data, giverProfile);
            string days = ((int)(data.TimeRemainingHours / 24f) + 1).ToString();
            string terms = $"（报酬 {data.NegotiatedReward} 第纳尔 · 期限 {days} 天"
                         + (data.DepositAmount > 0 ? $" · 定金 {data.DepositAmount}（确认后支付）" : "") + "）";
            string line = $"{story}\n{terms}";

            DebugLogger.Log($"[CommissionIntent] ConfirmCommission OnInstant: showing narrative for {data?.GetFlavorDescription()}");

            Action onAccept = () =>
            {
                int finalReward = CommissionGenerator.NegotiateReward(data.NegotiatedReward, ctx.Hero);
                data.NegotiatedReward = finalReward;
                int trust = TrustSystem.GetTrust(ctx.Hero);
                data.DepositAmount = (int)(finalReward * TrustSystem.GetDepositRatio(trust));
                pending.ConfirmQuest();
                DebugLogger.Log($"[CommissionIntent] ConfirmCommission accepted: {data.GetFlavorDescription()} reward={finalReward}");
                InformationManager.DisplayMessage(
                    new InformationMessage($"接取了委托：{data.GetFlavorDescription()} —— 报酬 {finalReward} 第纳尔。", Colors.Green));
            };
            Action onDecline = () =>
            {
                pending.CompleteQuestWithFail();
                DebugLogger.Log($"[CommissionIntent] ConfirmCommission declined: {data.GetFlavorDescription()}");
            };

            // NPC 当面讲述（走对话系统，不弹窗）
            if (ctx.Controller != null)
            {
                ctx.Controller.SceneSay(line,
                    new StoryOptionVM("这忙我帮了", () => { onAccept(); ctx.Controller.CloseDialogue(); }),
                    new StoryOptionVM("恕难从命", () => { onDecline(); ctx.Controller.CloseDialogue(); }));
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    $"委托详情 — {giverName}", $"「{story}」\n\n{terms}", true, true,
                    "接下委托", "婉拒", onAccept, onDecline));
            }
        }

        // 委托叙事统一走 CommissionNarrative.BuildOpening（CSV 驱动），此处不再硬编码。
    }

    /// <summary>
    /// 领取报酬 Intent：当玩家找到结账人时，如果委托目标已完成但未领取报酬，显示此选项。
    /// </summary>
    public class CollectCommissionRewardIntent : IntentBase
    {
        private CommissionQuest _foundQuest;

        public override InteractionOptionType Type => InteractionOptionType.Info;
        public override string DisplayName => "【领取报酬】 委托任务有结果了？";
        public override string ToolTip => "领取已完成委托的报酬";
        public override float CooldownDays => 0f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            _foundQuest = null;
            if (!ctx.IsHero || ctx.Hero == null) return Eligibility.Hide();

            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest is CommissionQuest cq
                    && cq.Data != null
                    && cq.Data.IsObjectivesComplete
                    && !cq.IsFinalized
                    && cq.Data.RewardPayer == ctx.Hero) // 精确匹配结账人
                {
                    _foundQuest = cq;
                    DebugLogger.Log($"[CommissionIntent] CollectReward Evaluate: hero={ctx.Hero.Name} quest={cq.Data.GetFlavorDescription()}");
                    return Eligibility.Show();
                }
                // 兜底：RewardPayer 为 null 时默认用 QuestGiver
                if (quest is CommissionQuest cq2
                    && cq2.Data != null
                    && cq2.Data.IsObjectivesComplete
                    && !cq2.IsFinalized
                    && cq2.Data.RewardPayer == null
                    && cq2.Data.QuestGiver == ctx.Hero)
                {
                    _foundQuest = cq2;
                    DebugLogger.Log($"[CommissionIntent] CollectReward Evaluate (default payer): hero={ctx.Hero.Name} quest={cq2.Data.GetFlavorDescription()}");
                    return Eligibility.Show();
                }
            }
            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (_foundQuest == null || ctx.Hero == null) return;

            var data = _foundQuest.Data;
            var giverProfile = RequestCommissionIntent.BuildProfileFromHero(ctx.Hero);

            // 用 CSV 模板生成结账叙事
            string closureText = CommissionNarrative.BuildClosure(data, giverProfile, giverProfile, _foundQuest.FinalGrade);
            string line = $"{closureText}\n（报酬 {data.NegotiatedReward} 第纳尔）";

            Action onCollect = () =>
            {
                _foundQuest.CompleteWithRewardCollection();
                DebugLogger.Log($"[CommissionIntent] CollectReward: player collected reward from {ctx.Hero.Name} for {data.GetFlavorDescription()}");
            };

            // NPC 当面致谢并结算（走对话系统，不弹窗）
            if (ctx.Controller != null)
            {
                ctx.Controller.SceneSay(line,
                    new StoryOptionVM("收下报酬", () => { onCollect(); ctx.Controller.CloseDialogue(); }),
                    new StoryOptionVM("（稍后再说）", () => ctx.Controller.CloseDialogue()));
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "领取报酬", $"「{closureText}」\n\n报酬：{data.NegotiatedReward} 第纳尔", true, true,
                    "领取", "稍后再说", onCollect, null));
            }
        }
    }
}

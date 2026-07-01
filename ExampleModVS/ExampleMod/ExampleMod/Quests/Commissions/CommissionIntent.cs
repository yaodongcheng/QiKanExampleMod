using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using LivingWorldNpcs;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 【已重写】"【找工作】打听委托" Intent。
    /// 不再调用 CommissionGenerator/CommissionQuest 旧管线。
    /// 改为：检测 NPC 的原版 Issue → StoryDialog 叙事包装 → StartIssueQuest 启动原版 Quest。
    /// 与原版对话 "Is there anything I can do for you?" 共享同一个底层 Quest。
    /// </summary>
    public class RequestCommissionIntent : IntentBase
    {
        private bool _hasUrgentEvent = false;
        private bool _hasIssue = false;

        public override InteractionOptionType Type => InteractionOptionType.FindWork;
        public override string DisplayName =>
            _hasUrgentEvent && !_hasIssue
                ? "【关于当前的事】 我能帮上什么忙？"
                : _hasIssue
                    ? "【找工作】 打听委托"
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

            // 新逻辑：检查 NPC 是否有原版 Issue（可接取状态）
            var issue = ctx.Hero.Issue;
            _hasIssue = issue != null && issue.IsOngoingWithoutQuest;
            _hasUrgentEvent = ctx.HasUrgentWorldEvent;

            if (!_hasIssue && !_hasUrgentEvent)
                return Eligibility.Hide();

            DebugLogger.Log($"[CommissionIntent] RequestCommission Evaluate: hero={ctx.Hero.Name} hasIssue={_hasIssue} urgentEvent={_hasUrgentEvent} → Show");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Hero == null) return;

            var issue = ctx.Hero.Issue;

            // 路径 A：NPC 有原版 Issue（可接取状态）
            if (issue != null && issue.IsOngoingWithoutQuest)
            {
                DebugLogger.Log($"[CommissionIntent] RequestCommission OnInstant: presenting vanilla issue type={issue.GetType().Name} giver={ctx.Hero.Name}");
                ShowVanillaIssueInDialogue(issue, ctx);
                return;
            }

            // 路径 B：NPC 有紧迫世界事件但无 Issue
            if (_hasUrgentEvent)
            {
                ShowUrgentEventSituation(ctx);
                return;
            }

            // 兜底：无可用委托
            if (ctx.Controller != null)
                ctx.Controller.SceneSay("我这儿暂时没有需要帮手的活计。",
                    new StoryOptionVM("（离开）", () => ctx.Controller.CloseDialogue()));
            else
                InformationManager.DisplayMessage(
                    new InformationMessage("我这儿暂时没有需要帮手的活计。"));
        }

        /// <summary>
        /// 路径 A：在 StoryDialog 中呈现原版 Issue。
        /// 叙事来源三层 fallback：CSV 模板 → LLM 生成 → 原版 TextObject 兜底。
        /// </summary>
        private void ShowVanillaIssueInDialogue(IssueBase issue, IntentContext ctx)
        {
            // 1. 读原版文本（去除格式标记）
            string vanillaExplanation = CleanIssueText(issue.IssueQuestSolutionExplanationByIssueGiver);
            string vanillaBrief = CleanIssueText(issue.IssueBriefByIssueGiver);
            string questTitle = issue.Title?.ToString() ?? issue.GetType().Name;
            string issueTypeName = issue.GetType().Name.Replace("Issue", "");

            // 2. 提取因果上下文
            var causalityCtx = QuestConsequenceResolver.ExtractCausalityContext(ctx.Hero);
            bool hasCausality = causalityCtx != null && causalityCtx.HasContext;

            // 3. 叙事生成（三层 fallback）
            //    注意：CSV 模板和 LLM 暂未覆盖全部 40 种 Quest，
            //    当前总是回退到 BuildFullIssueNarrative（Brief + Explanation 拼接）。
            string narrativeText = GenerateNarrative(issueTypeName, ctx, vanillaExplanation,
                questTitle, causalityCtx, hasCausality);
            // 如果 GenerateNarrative 返回的就是 vanillaExplanation（CSV/LLM 都没命中），
            // 用 BuildFullIssueNarrative 拼上 IssueBriefByIssueGiver 提供完整上下文。
            if (narrativeText == vanillaExplanation
                || string.IsNullOrEmpty(narrativeText)
                || narrativeText.Contains("{") || narrativeText.Contains("}"))
            {
                narrativeText = BuildFullIssueNarrative(issue);
            }

            // 4. 接取/拒绝行为定义
            Action onAccept = () =>
            {
                try
                {
                    QuestMemoryRecorderPatch.RecordQuestIssued(ctx.Hero);
                    bool started = Campaign.Current.IssueManager.StartIssueQuest(ctx.Hero);
                    if (started)
                    {
                        // StartIssueQuest → StartIssueWithQuest → GenerateIssueQuest 只 new 了 QuestBase 对象。
                        // QuestStates 枚举 Ongoing=0 是默认值，所以 quest.IsOngoing 从构造那一刻就是 true，
                        // 无法用它判断 StartQuest() 是否已被调用。必须无条件手动激活。
                        var quest = ctx.Hero.Issue?.IssueQuest;
                        if (quest != null && !Campaign.Current.QuestManager.Quests.Contains(quest))
                        {
                            // 必须调 QuestAcceptedConsequences：
                            //   1. StartQuest()            — public，但手动调完还得处理进度日志
                            //   2. AddDiscreteLog(...)     — public，但返回的 JournalLog 存入了私有字段
                            //   3. _questProgressLogTest   — private，AddQuestStepLog() 靠它更新进度
                            //
                            // AddLog 只能加一条静态文本，AddDiscreteLog 能显示 X/Y 进度条。
                            // 问题是自己调 AddDiscreteLog 拿到的对象跟 quest 内部的 _questProgressLogTest
                            // 不是同一个——quest 的事件处理器（MobilePartyDestroyed）更新的是私有字段里的那个。
                            // 所以自己调 AddDiscreteLog 只是加了条永远 0/2 不会动的进度条。
                            //
                            // 结论：进度条必须由 quest 自己的代码创建。反射是唯一的路。
                            InvokeQuestAcceptedConsequences(quest);
                            DebugLogger.Log($"[CommissionIntent] Quest activated via reflection: {quest.GetType().Name}");
                        }

                        DebugLogger.Log($"[CommissionIntent] Player accepted: {issueTypeName} from {ctx.Hero.Name} — questObj={quest?.GetType().Name ?? "null"}");

                        InformationManager.DisplayMessage(
                            new InformationMessage($"接取了委托：{questTitle}", Colors.Green));
                    }
                    else
                    {
                        DebugLogger.Log($"[CommissionIntent] StartIssueQuest returned FALSE for {issueTypeName} from {ctx.Hero.Name} — issue may already be solved or invalid");
                        InformationManager.DisplayMessage(
                            new InformationMessage($"接取委托失败：此委托可能已失效。", Colors.Red));
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[CommissionIntent] StartIssueQuest exception: {ex.Message}");
                    InformationManager.DisplayMessage(
                        new InformationMessage($"接取委托失败：{ex.Message}", Colors.Red));
                }

                if (ctx.Controller != null)
                    ctx.Controller.CloseDialogue();
            };
            Action onDecline = () =>
            {
                DebugLogger.Log($"[CommissionIntent] Player declined: {issueTypeName}");
                if (ctx.Controller != null)
                    ctx.Controller.CloseDialogue();
            };

            // 5. 展示：短文本直接展示选项，多句文本逐句展示
            if (ctx.Controller != null)
            {
                var sentences = SplitIntoSentences(narrativeText);
                if (sentences.Count <= 2)
                {
                    // 短文本（1-2 句）：直接展示 + 选项
                    string line = hasCausality
                        ? $"{narrativeText}\n\n（{questTitle}）"
                        : narrativeText;
                    ctx.Controller.SceneSay(line,
                        new StoryOptionVM("【接取】", onAccept),
                        new StoryOptionVM("【拒绝】", onDecline));
                }
                else
                {
                    // 多句文本（3+ 句，通常是原版 TextObject 兜底）：逐句展示
                    ShowSentenceBySentence(sentences, 0, questTitle, onAccept, onDecline, ctx);
                }
            }
            else
            {
                // 无对话控制器时走 Inquiry 弹窗
                InformationManager.ShowInquiry(new InquiryData(
                    $"委托 — {ctx.Hero.Name}",
                    $"「{narrativeText}」\n\n委托：{questTitle}",
                    true, true,
                    "接取", "拒绝",
                    onAccept, onDecline));
            }
        }

        /// <summary>
        /// 多句文本逐句展示。前 N-1 句点屏幕继续，最后一句带【接取】/【拒绝】。
        /// </summary>
        private static void ShowSentenceBySentence(
            List<string> sentences, int index, string questTitle,
            Action onAccept, Action onDecline, IntentContext ctx)
        {
            if (ctx.Controller?._vm == null || index >= sentences.Count)
            {
                onDecline();
                return;
            }

            if (index >= sentences.Count - 1)
            {
                // 最后一句：展示选项
                string finalLine = $"{sentences[index]}\n\n（{questTitle}）";
                ctx.Controller.SceneSay(finalLine,
                    new StoryOptionVM("【接取】", onAccept),
                    new StoryOptionVM("【拒绝】", onDecline));
            }
            else
            {
                // 中间句：不传 options → 点屏幕继续
                ctx.Controller.SceneSay(sentences[index]);
                ctx.Controller._vm.OnClickContinue = () =>
                    ShowSentenceBySentence(sentences, index + 1, questTitle, onAccept, onDecline, ctx);
            }
        }

        /// <summary>
        /// 按句子拆分文本。支持中英文标点。
        /// </summary>
        private static List<string> SplitIntoSentences(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Add(text ?? "");
                return result;
            }

            // 在标点后加分割标记，然后按标记切分
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                sb.Append(c);
                // 句子结束标点：. ! ? 。！？
                if (c == '.' || c == '!' || c == '?' || c == '。' || c == '！' || c == '？')
                {
                    // 检查后面是否跟着空格或结尾
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
            }

            // 剩余文本（没有结束标点的尾巴）
            string remainder = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(remainder))
            {
                if (result.Count > 0)
                    result[result.Count - 1] += " " + remainder; // 追加到最后一句
                else
                    result.Add(remainder);
            }

            // 去空句
            result.RemoveAll(s => string.IsNullOrWhiteSpace(s));

            return result.Count > 0 ? result : new List<string> { text };
        }

        /// <summary>
        /// 路径 B：NPC 被世界事件困扰但暂无 Issue 可接。
        /// 描述困境，让玩家感知到世界事件的影响。
        /// </summary>
        private void ShowUrgentEventSituation(IntentContext ctx)
        {
            var urgentEvent = ctx.Memory?.CurrentUrgentEvent;
            if (urgentEvent == null) return;

            bool isVictim = urgentEvent.TargetHeroId == ctx.Hero?.StringId;
            string instigatorName = urgentEvent.InstigatorHero?.Name?.ToString() ?? "他们";
            string victimName = urgentEvent.TargetHero?.Name?.ToString() ?? "我们";

            string eventDesc = urgentEvent.Type switch
            {
                EventType.BanditRaid => "匪帮正在劫掠这一带",
                EventType.Famine => "粮食短缺，日子越来越难过了",
                EventType.NobleConflict => "贵族之间的争端波及到了这里",
                EventType.DebtTrap => "有人欠了一屁股债，被追得紧",
                EventType.Kidnapping => "有人被绑了，整个镇子都人心惶惶",
                EventType.Betrayal => "出了叛徒，不知道还能信谁",
                EventType.TradeDispute => "商路上的争端让买卖越来越难做",
                _ => "最近发生了一些事"
            };

            string line = isVictim
                ? $"唉……{eventDesc}。{instigatorName}的事你应该也听说了。我现在自顾不暇，等这阵子过去再看有什么活计能派给你。"
                : $"我这边的事还没了结——{eventDesc}。等我把手头的事处理完，再看看有什么能让你帮忙的。";

            if (ctx.Controller != null)
                ctx.Controller.SceneSay(line,
                    new StoryOptionVM("（我理解，告辞）", () => ctx.Controller.CloseDialogue()));
            else
                InformationManager.DisplayMessage(new InformationMessage(line));
        }

        /// <summary>
        /// 三层 fallback 叙事生成：
        /// ① NarrativeResolver CSV 模板（按 issueTypeName + NPC性格 匹配）
        /// ② CSV 未命中 + IsLLMReady → LLM 生成（prompt 含 vanillaExplanation + 因果上下文）
        /// ③ 都不可用 → 直接返回 vanillaExplanation（原版 TextObject 兜底）
        /// </summary>
        private static string GenerateNarrative(
            string issueTypeName,
            IntentContext ctx,
            string vanillaExplanation,
            string questTitle,
            CausalityContext causalityCtx,
            bool hasCausality)
        {
            // ① 尝试 CSV 模板
            string csvText = TryResolveCsvNarrative(issueTypeName, ctx);
            if (!string.IsNullOrEmpty(csvText))
            {
                csvText = InjectCausalityVariables(csvText, causalityCtx, questTitle);
                return csvText;
            }

            // ② CSV 未命中 → LLM（如果可用）
            if (Settings.Instance.IsLLMReady)
            {
                string llmText = TryGenerateLlmNarrative(issueTypeName, ctx, vanillaExplanation,
                    questTitle, causalityCtx, hasCausality);
                if (!string.IsNullOrEmpty(llmText))
                    return llmText;
            }

            // ③ 兜底：原版 TextObject
            return !string.IsNullOrEmpty(vanillaExplanation)
                ? vanillaExplanation
                : $"我需要人帮个忙——是关于{questTitle}的事。";
        }

        /// <summary>
        /// 尝试从 Narrative.csv 匹配委托开场叙事。
        /// </summary>
        private static string TryResolveCsvNarrative(string issueTypeName, IntentContext ctx)
        {
            try
            {
                // 构建过滤器：按 QuestType + NPC性格（取 NPCProfile 的第一个性格标签）
                string personalityTraits = ctx.Profile?.PersonalityTraits ?? "";
                string personalityTag = "";
                if (!string.IsNullOrEmpty(personalityTraits))
                {
                    var traits = personalityTraits.Split(new[] { ',', '，' },
                        System.StringSplitOptions.RemoveEmptyEntries);
                    personalityTag = traits.Length > 0 ? traits[0].Trim() : "";
                }
                string trustLevel = TrustSystem.GetTrust(ctx.Hero) >= 10 ? "High" : "Low";

                var filters = new NarrativeFilters
                {
                    EventName = $"Commission_{issueTypeName}",
                    Category = "Commission",
                    Phase = "Opening",
                    PersonalityTrait = personalityTag,
                    TrustMin = trustLevel == "High" ? 10 : 0,
                    TrustMax = trustLevel == "High" ? 99 : 9,
                };

                var result = NarrativeResolver.Resolve(filters);
                if (result != null && !string.IsNullOrEmpty(result.Text))
                {
                    // 替换标准占位符（{PLAYER}, {NPC}, {WORLD}, {TERM_LORD} 等）
                    string text = NarrativeResolver.ApplyPlaceholders(result.Text, ctx.Hero, ctx.Agent);
                    return text;
                }
            }
            catch
            {
                // CSV 解析失败，静默降级
            }
            return null;
        }

        /// <summary>
        /// 尝试用 LLM 生成叙事。
        /// </summary>
        private static string TryGenerateLlmNarrative(
            string issueTypeName,
            IntentContext ctx,
            string vanillaExplanation,
            string questTitle,
            CausalityContext causalityCtx,
            bool hasCausality)
        {
            try
            {
                string npcName = ctx.Hero?.Name?.ToString() ?? "对方";
                string personality = ctx.Profile?.GetPersonaPrompt() ?? "";
                string relationDesc = ctx.Relation >= 10 ? "不错" : ctx.Relation >= 0 ? "一般" : "较差";

                string causalityPrompt = "";
                if (hasCausality && causalityCtx != null)
                {
                    causalityPrompt = $"\n之前玩家帮你做了 {causalityCtx.PreviousQuestId}，" +
                        $"因为那件事，{causalityCtx.CauseHeroName} 引发了现在的局面。请在叙事中自然地提到这个前因后果。";
                }

                string prompt =
                    $"你是{npcName}（{personality}）。你和玩家的关系：{relationDesc}。" +
                    $"你有一个委托需要玩家帮忙：{questTitle}。\n" +
                    $"任务原版说明：{vanillaExplanation}\n" +
                    $"{causalityPrompt}\n" +
                    $"用第一人称，{Settings.Instance.SpeechStyle}，2-3句话请玩家帮忙。" +
                    $"世界观背景：{Settings.Instance.WorldDescription}。";

                string result = LLMService.Instance.ChatAsync(prompt, 120, false)
                    .GetAwaiter().GetResult(); // 同步等待——此方法在 UI 线程调用但 LLM 必须阻塞

                if (!string.IsNullOrEmpty(result))
                {
                    string cleaned = LLMService.CleanJson(result);
                    return cleaned.Trim();
                }
            }
            catch
            {
                // LLM 调用失败，静默降级
            }
            return null;
        }

        /// <summary>
        /// 将因果变量注入叙事文本。
        /// </summary>
        private static string InjectCausalityVariables(
            string text, CausalityContext ctx, string questDesc)
        {
            if (ctx == null || !ctx.HasContext) return text;

            var replacements = new Dictionary<string, string>
            {
                { "{PREVIOUS_QUEST}", ctx.PreviousQuestId ?? "" },
                { "{CAUSE_HERO}", ctx.CauseHeroName ?? "某人" },
                { "{CAUSE_EVENT}", ctx.Summary ?? "" },
                { "{CHAIN_DEPTH}", ctx.ChainDepth > 0 ? ctx.ChainDepth.ToString() : "" },
                { "{QUEST_DESC}", questDesc },
            };

            foreach (var kvp in replacements)
            {
                text = text.Replace(kvp.Key, kvp.Value);
            }

            return text;
        }

       
        /// <summary>
        /// 反射调用 quest 的私有 QuestAcceptedConsequences()。
        /// 必须反射的原因：该方法内部调用 AddDiscreteLog() 返回的 JournalLog
        /// 存入了 quest 的私有字段 _questProgressLogTest。后续进度更新（MobilePartyDestroyed
        /// 等事件处理器）通过 AddQuestStepLog() 更新这个私有字段。我们自己调 AddDiscreteLog
        /// 拿到的 JournalLog 是另一个对象，不会随进度更新——任务面板会永远显示 0/2。
        /// </summary>
        private static bool InvokeQuestAcceptedConsequences(QuestBase quest)
        {
            try
            {
                var questType = quest.GetType();
                foreach (var methodName in new[] { "QuestAcceptedConsequences", "OnQuestAccepted", "HandleQuestAccepted" })
                {
                    var method = questType.GetMethod(methodName,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method != null && method.GetParameters().Length == 0)
                    {
                        method.Invoke(quest, null);
                        DebugLogger.Log($"[CommissionIntent] Invoked {methodName}() on {questType.Name} — journal entries: {quest.JournalEntries.Count}");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CommissionIntent] Failed to invoke QuestAcceptedConsequences: {ex.Message}");
                return false;
            }
        }

        private static string CleanIssueText(TextObject textObj)
        {
            if (textObj == null) return "";
            try
            {
                string text = textObj.ToString();
                if (string.IsNullOrEmpty(text)) return "";
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"\[if:[^\]]*\]|\[ib:[^\]]*\]|\[\?[^\]]*\]|\[\\?\]|{\.%}|{\\?}", "");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\{[A-Z_]+\}", "");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
                return text;
            }
            catch { return ""; }
        }

        /// <summary>
        /// 拼接 Issue 的公开属性构建完整叙事（无需反射）。
        /// IssueBriefByIssueGiver = 背景说明（通常很长很详细）
        /// IssueQuestSolutionExplanationByIssueGiver = 任务要求
        /// </summary>
        private static string BuildFullIssueNarrative(IssueBase issue)
        {
            string brief = CleanIssueText(issue.IssueBriefByIssueGiver);
            string explanation = CleanIssueText(issue.IssueQuestSolutionExplanationByIssueGiver);

            if (!string.IsNullOrEmpty(brief) && !string.IsNullOrEmpty(explanation))
                return $"{brief}\n{explanation}";

            return !string.IsNullOrEmpty(brief) ? brief
                : !string.IsNullOrEmpty(explanation) ? explanation
                : $"我需要人帮个忙——（{issue.Title}）";
        }
    }

    /// <summary>
    /// [已废弃] 确认委托 Intent。
    /// 原版 Quest 无两段式（告示板→找委托人）流程，ConfirmCommissionIntent 不再注册。
    /// 保留类定义以保持编译兼容——其他引用此类型的代码可能仍会编译通过。
    /// </summary>
    [Obsolete("原版 Quest 无两段式流程，ConfirmCommissionIntent 已废弃。类保留编译。")]
    public class ConfirmCommissionIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.FindWork;
        public override string DisplayName => "【委托详情】 听说是你需要帮手？";
        public override string ToolTip => "[已废弃]";
        public override float CooldownDays => 0f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            return Eligibility.Hide(); // 永远不显示
        }

        public override void OnInstant(IntentContext ctx)
        {
            // 不再执行任何逻辑
        }
    }

    /// <summary>
    /// 领取报酬 Intent（已泛化）。
    /// 检测 NPC 是否有已完成但未领报酬的委托（任意 Quest 类型）。
    /// </summary>
    public class CollectCommissionRewardIntent : IntentBase
    {
        private QuestBase _foundQuest;

        public override InteractionOptionType Type => InteractionOptionType.Info;
        public override string DisplayName => "【领取报酬】 委托任务有结果了？";
        public override string ToolTip => "领取已完成委托的报酬";
        public override float CooldownDays => 0f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            _foundQuest = null;
            if (!ctx.IsHero || ctx.Hero == null) return Eligibility.Hide();

            // 泛化：检查任意 Quest 是否已完成目标但未领报酬
            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest.IsFinalized) continue;
                if (quest.QuestGiver == null) continue;

                // 匹配结账人（优先精确匹配 QuestGiver）
                if (quest.QuestGiver != ctx.Hero) continue;

                // 检查是否是 CommissionQuest（旧系统——保留兼容）
                if (quest is CommissionQuest cq
                    && cq.Data != null
                    && cq.Data.IsObjectivesComplete
                    && !cq.IsFinalized
                    && cq.Data.RewardPayer == ctx.Hero)
                {
                    _foundQuest = cq;
                    DebugLogger.Log($"[CommissionIntent] CollectReward Evaluate (CommissionQuest): hero={ctx.Hero.Name} quest={cq.Data.GetFlavorDescription()}");
                    return Eligibility.Show();
                }

                // 检查 CommissionQuest 的兜底（RewardPayer 为 null）
                if (quest is CommissionQuest cq2
                    && cq2.Data != null
                    && cq2.Data.IsObjectivesComplete
                    && !cq2.IsFinalized
                    && cq2.Data.RewardPayer == null
                    && cq2.Data.QuestGiver == ctx.Hero)
                {
                    _foundQuest = cq2;
                    DebugLogger.Log($"[CommissionIntent] CollectReward Evaluate (CommissionQuest default payer): hero={ctx.Hero.Name}");
                    return Eligibility.Show();
                }
            }

            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (_foundQuest == null || ctx.Hero == null) return;

            if (_foundQuest is CommissionQuest cq && cq.Data != null)
            {
                // CommissionQuest 路径（旧系统兼容）
                var data = cq.Data;
                string closureText = cq.Data.GetFlavorDescription();
                string line = $"委托「{closureText}」已经办妥了。\n（报酬 {data.NegotiatedReward} 第纳尔）";

                Action onCollect = () =>
                {
                    cq.CompleteWithRewardCollection();
                    DebugLogger.Log($"[CommissionIntent] CollectReward: player collected reward from {ctx.Hero.Name}");
                    if (ctx.Controller != null)
                        ctx.Controller.CloseDialogue();
                };

                if (ctx.Controller != null)
                {
                    ctx.Controller.SceneSay(line,
                        new StoryOptionVM("收下报酬", onCollect),
                        new StoryOptionVM("（稍后再说）", () => ctx.Controller.CloseDialogue()));
                }
                else
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "领取报酬", $"「{line}」", true, true,
                        "领取", "稍后再说", onCollect, null));
                }
            }
            else
            {
                // 通用 Quest（兜底）
                string questTitle = _foundQuest.Title?.ToString() ?? "委托";
                int reward = _foundQuest.RewardGold;

                Action onCollect = () =>
                {
                    // 对非 CommissionQuest，调用 CompleteQuestWithSuccess 收尾
                    if (!_foundQuest.IsFinalized)
                        _foundQuest.CompleteQuestWithSuccess();
                    DebugLogger.Log($"[CommissionIntent] CollectReward: completed generic quest {questTitle}");
                    if (ctx.Controller != null)
                        ctx.Controller.CloseDialogue();
                };

                string line = $"委托「{questTitle}」已经办妥了。\n（报酬 {reward} 第纳尔）";

                if (ctx.Controller != null)
                {
                    ctx.Controller.SceneSay(line,
                        new StoryOptionVM("收下报酬", onCollect),
                        new StoryOptionVM("（稍后再说）", () => ctx.Controller.CloseDialogue()));
                }
                else
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "领取报酬", $"「{line}」", true, true,
                        "领取", "稍后再说", onCollect, null));
                }
            }
        }
    }
}

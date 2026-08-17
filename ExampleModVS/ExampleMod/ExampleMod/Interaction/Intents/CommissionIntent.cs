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

namespace LivingWorldNpcs
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
        public override string DisplayName
        {
            get
            {
                if (_hasUrgentEvent && !_hasIssue)
                {
                    // 委托入口名（事件困扰时）：询问能否帮上忙
                    return LWNTextHelper.ResolveText("LWN_intent_commission_urgent_name", "About current matters: How can I help?");
                }
                // 委托入口名（普通）：打听委托
                return LWNTextHelper.ResolveText("LWN_intent_commission_name", "Find work: Ask about commissions");
            }
        }
        public override string ToolTip
        {
            get
            {
                if (_hasUrgentEvent)
                {
                    // 委托入口提示（事件困扰时）：对方正被事件困扰，询问需要做什么
                    return LWNTextHelper.ResolveText("LWN_intent_commission_urgent_tooltip", "They are troubled by an event - ask what they need");
                }
                // 委托入口提示（普通）：打听是否有委托可接
                return LWNTextHelper.ResolveText("LWN_intent_commission_tooltip", "Ask if there is any commission to take");
            }
        }
        public override float CooldownDays => 0.5f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.IsHero) return Eligibility.Hide();
            if (ctx.Speaker == null) return Eligibility.Hide();

            // 新逻辑：检查 NPC 是否有原版 Issue（可接取状态）
            var issue = ctx.Speaker.Issue;
            _hasIssue = issue != null && issue.IsOngoingWithoutQuest;
            _hasUrgentEvent = ctx.HasUrgentWorldEvent;

            if (!_hasIssue && !_hasUrgentEvent)
                return Eligibility.Hide();

            DebugLogger.Log($"[CommissionIntent] RequestCommission Evaluate: hero={ctx.Speaker.Name} hasIssue={_hasIssue} urgentEvent={_hasUrgentEvent} → Show");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Speaker == null) return;

            var issue = ctx.Speaker.Issue;

            // 路径 A：NPC 有原版 Issue（可接取状态）
            if (issue != null && issue.IsOngoingWithoutQuest)
            {
                DebugLogger.Log($"[CommissionIntent] RequestCommission OnInstant: presenting vanilla issue type={issue.GetType().Name} giver={ctx.Speaker.Name}");
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
                // 兜底台词：暂无委托可接
                ctx.Controller.SceneSay(LWNTextHelper.ResolveText("LWN_intent_commission_no_work", "I have no work that needs a hand right now."),
                    // 兜底后的离开选项：玩家告辞
                    new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_leave_option", "(Leave)"), () => ctx.Controller.CloseDialogue()));
            else
                // 无对话控制器时的兜底飘字：暂无委托可接
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_intent_commission_no_work", "I have no work that needs a hand right now.")));
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
            var causalityCtx = QuestConsequenceResolver.ExtractCausalityContext(ctx.Speaker);
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
                    QuestMemoryRecorderPatch.RecordQuestIssued(ctx.Speaker);
                    bool started = Campaign.Current.IssueManager.StartIssueQuest(ctx.Speaker);
                    if (started)
                    {
                        // StartIssueQuest → StartIssueWithQuest → GenerateIssueQuest 只 new 了 QuestBase 对象。
                        // QuestStates 枚举 Ongoing=0 是默认值，所以 quest.IsOngoing 从构造那一刻就是 true，
                        // 无法用它判断 StartQuest() 是否已被调用。必须无条件手动激活。
                        var quest = ctx.Speaker.Issue?.IssueQuest;
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

                        DebugLogger.Log($"[CommissionIntent] Player accepted: {issueTypeName} from {ctx.Speaker.Name} — questObj={quest?.GetType().Name ?? "null"}");

                        // 接取委托成功飘字：提示已接下 {TITLE} 委托
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_commission_accepted", ("TITLE", questTitle)), Colors.Green));
                    }
                    else
                    {
                        DebugLogger.Log($"[CommissionIntent] StartIssueQuest returned FALSE for {issueTypeName} from {ctx.Speaker.Name} — issue may already be solved or invalid");
                        // 接取委托失败飘字：委托可能已失效
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_intent_commission_failed_expired", "Failed to accept the commission: it may no longer be available."), Colors.Red));
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[CommissionIntent] StartIssueQuest exception: {ex.Message}");
                    if (Settings.Instance.ShowDebugMessages)
                        // 本地化：LWN_intent_commission_failed_error（玩家可见文本）
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_commission_failed_error", ("MESSAGE", ex.Message)), Colors.Red));
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
                    string line;
                    if (hasCausality)
                    {
                        // 委托叙事展示：有前因时在正文后附任务标题
                        line = LWNTextHelper.ResolveCompound("LWN_intent_commission_line_with_quest",
                            ("NARRATIVE", narrativeText), ("QUEST", questTitle));
                    }
                    else
                    {
                        line = narrativeText;
                    }
                    ctx.Controller.SceneSay(line,
                        // 委托展示选项：接取委托
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_accept_option", "Accept"), onAccept),
                        // 委托展示选项：拒绝委托
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_decline_option", "Decline"), onDecline));
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
                // 委托弹窗标题：{NAME} 发布的委托
                string inquiryTitle = LWNTextHelper.ResolveCompound("LWN_intent_commission_inquiry_title", ("NAME", ctx.Speaker.Name.ToString()));
                // 委托弹窗正文：叙事 + 任务标题
                string inquiryBody = LWNTextHelper.ResolveCompound("LWN_intent_commission_inquiry_body",
                    ("NARRATIVE", narrativeText), ("QUEST", questTitle));
                // 委托弹窗按钮：接取
                string acceptBtn = LWNTextHelper.ResolveText("LWN_intent_commission_accept_option", "Accept");
                // 委托弹窗按钮：拒绝
                string declineBtn = LWNTextHelper.ResolveText("LWN_intent_commission_decline_option", "Decline");
                InformationManager.ShowInquiry(new InquiryData(inquiryTitle, inquiryBody, true, true, acceptBtn, declineBtn, onAccept, onDecline));
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
                // 逐句展示的最后一句：正文后附任务标题
                string finalLine = LWNTextHelper.ResolveCompound("LWN_intent_commission_line_with_quest",
                    ("NARRATIVE", sentences[index]), ("QUEST", questTitle));
                ctx.Controller.SceneSay(finalLine,
                    // 逐句展示最后一句的选项：接取委托
                    new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_accept_option", "Accept"), onAccept),
                    // 逐句展示最后一句的选项：拒绝委托
                    new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_decline_option", "Decline"), onDecline));
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

            bool isVictim = urgentEvent.TargetHeroId == ctx.Speaker?.StringId;
            string instigatorName = urgentEvent.InstigatorHero?.Name?.ToString()
                // 事件肇事者名字兜底：无名时用泛指称呼
                ?? LWNTextHelper.ResolveText("LWN_intent_commission_them", "They");
            string victimName = urgentEvent.TargetHero?.Name?.ToString()
                // 事件受害者名字兜底：无名时用自称称呼
                ?? LWNTextHelper.ResolveText("LWN_intent_commission_us", "us");

            string eventDesc = urgentEvent.Type switch
            {
                // 匪患事件描述：匪帮正在劫掠
                EventType.BanditRaid => LWNTextHelper.ResolveText("LWN_intent_commission_event_banditraid", "Bandits are raiding this area"),
                // 饥荒事件描述：粮食短缺
                EventType.Famine => LWNTextHelper.ResolveText("LWN_intent_commission_event_famine", "Food is scarce, and days grow harder"),
                // 贵族冲突事件描述：争端波及此地
                EventType.NobleConflict => LWNTextHelper.ResolveText("LWN_intent_commission_event_nobleconflict", "A feud among nobles has reached this place"),
                // 债务陷阱事件描述：有人被债主追逼
                EventType.DebtTrap => LWNTextHelper.ResolveText("LWN_intent_commission_event_debttrap", "Someone is drowning in debt, hounded by creditors"),
                // 绑架事件描述：人心惶惶
                EventType.Kidnapping => LWNTextHelper.ResolveText("LWN_intent_commission_event_kidnapping", "Someone has been seized - the whole town is on edge"),
                // 背叛事件描述：出了叛徒
                EventType.Betrayal => LWNTextHelper.ResolveText("LWN_intent_commission_event_betrayal", "A traitor has emerged - no one knows who to trust"),
                // 商路争端事件描述：买卖难做
                EventType.TradeDispute => LWNTextHelper.ResolveText("LWN_intent_commission_event_tradedispute", "Disputes on the trade routes are making business hard"),
                // 未知事件描述兜底
                _ => LWNTextHelper.ResolveText("LWN_intent_commission_event_default", "Something has happened recently")
            };

            string line;
            if (isVictim)
            {
                // 受害者委托台词：叙述困境并谢绝当前委托
                line = LWNTextHelper.ResolveCompound("LWN_intent_commission_urgent_victim_line",
                    ("EVENT_DESC", eventDesc), ("INSTIGATOR", instigatorName));
            }
            else
            {
                // 旁观者委托台词：叙述困境，稍后再谈委托
                line = LWNTextHelper.ResolveCompound("LWN_intent_commission_urgent_other_line",
                    ("EVENT_DESC", eventDesc));
            }

            if (ctx.Controller != null)
                ctx.Controller.SceneSay(line,
                    // 困境叙事的离开选项：表示理解并告辞
                    new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_understand_leave_option", "(I understand - farewell)"), () => ctx.Controller.CloseDialogue()));
            else
                InformationManager.DisplayMessage(new InformationMessage(line));
        }

        /// <summary>
        /// 三层 fallback 叙事生成：
        /// ① NarrativeResolver CSV 模板（按 issueTypeName + NPC性格 匹配）
        /// ② CSV 未命中 + IsLLMConfigured → LLM 生成（prompt 含 vanillaExplanation + 因果上下文）
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
            if (Settings.Instance.IsLLMConfigured)
            {
                string llmText = TryGenerateLlmNarrative(issueTypeName, ctx, vanillaExplanation,
                    questTitle, causalityCtx, hasCausality);
                if (!string.IsNullOrEmpty(llmText))
                    return llmText;
            }

            // ③ 兜底：原版 TextObject
            return !string.IsNullOrEmpty(vanillaExplanation)
                ? vanillaExplanation
                // 叙事生成兜底：无原版说明时直述需要帮忙的事
                : LWNTextHelper.ResolveCompound("LWN_intent_commission_narrative_fallback",
                    ("QUEST", questTitle));
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
                string trustLevel = TrustSystem.GetTrust(ctx.Speaker) >= 10 ? "High" : "Low";

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
                    string text = NarrativeResolver.ApplyPlaceholders(result.Text, ctx.Speaker, ctx.Agent);
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
                string npcName = ctx.Speaker?.Name?.ToString() ?? "对方";
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
                    $"用第一人称，{Settings.Instance.SpeechStyle}，2-3句话请玩家帮忙。";

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
                // 因果肇事者名字兜底：无名时用泛指称呼（注入玩家可见叙事）
                { "{CAUSE_HERO}", ctx.CauseHeroName ?? LWNTextHelper.ResolveText("LWN_intent_commission_cause_hero_fallback", "Someone") },
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
                // 完整叙事兜底：无任何说明时用任务标题兜底
                : LWNTextHelper.ResolveCompound("LWN_intent_commission_full_narrative_fallback",
                    ("QUEST", issue.Title?.ToString() ?? ""));
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
        // 已废弃委托详情意图名：询问是否需要帮手
        public override string DisplayName => LWNTextHelper.ResolveText("LWN_intent_commission_confirm_name", "Commission details: I heard you need a hand?");
        // 已废弃委托详情意图提示：占位说明
        public override string ToolTip => LWNTextHelper.ResolveText("LWN_intent_commission_confirm_tooltip", "[Deprecated]");
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
        // 领取报酬意图名：询问委托结果
        public override string DisplayName => LWNTextHelper.ResolveText("LWN_intent_commission_reward_name", "Collect reward: Is my commission done?");
        // 领取报酬意图提示：领取已完成委托的报酬
        public override string ToolTip => LWNTextHelper.ResolveText("LWN_intent_commission_reward_tooltip", "Collect the reward for a completed commission");
        public override float CooldownDays => 0f;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            _foundQuest = null;
            if (!ctx.IsHero || ctx.Speaker == null) return Eligibility.Hide();

            // 泛化：检查任意 Quest 是否已完成目标但未领报酬
            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest.IsFinalized) continue;
                if (quest.QuestGiver == null) continue;

                // 匹配结账人（优先精确匹配 QuestGiver）
                if (quest.QuestGiver != ctx.Speaker) continue;

                // 检查是否是 CommissionQuest（旧系统——保留兼容）
                if (quest is CommissionQuest cq
                    && cq.Data != null
                    && cq.Data.IsObjectivesComplete
                    && !cq.IsFinalized
                    && cq.Data.RewardPayer == ctx.Speaker)
                {
                    _foundQuest = cq;
                    DebugLogger.Log($"[CommissionIntent] CollectReward Evaluate (CommissionQuest): hero={ctx.Speaker.Name} quest={cq.Data.GetFlavorDescription()}");
                    return Eligibility.Show();
                }

                // 检查 CommissionQuest 的兜底（RewardPayer 为 null）
                if (quest is CommissionQuest cq2
                    && cq2.Data != null
                    && cq2.Data.IsObjectivesComplete
                    && !cq2.IsFinalized
                    && cq2.Data.RewardPayer == null
                    && cq2.Data.QuestGiver == ctx.Speaker)
                {
                    _foundQuest = cq2;
                    DebugLogger.Log($"[CommissionIntent] CollectReward Evaluate (CommissionQuest default payer): hero={ctx.Speaker.Name}");
                    return Eligibility.Show();
                }
            }

            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (_foundQuest == null || ctx.Speaker == null) return;

            if (_foundQuest is CommissionQuest cq && cq.Data != null)
            {
                // CommissionQuest 路径（旧系统兼容）
                var data = cq.Data;
                string closureText = cq.Data.GetFlavorDescription();
                // 旧委托系统结账台词：委托已办妥，列出报酬
                string line = LWNTextHelper.ResolveCompound("LWN_intent_commission_reward_line",
                    ("CLOSURE", closureText), ("REWARD", data.NegotiatedReward.ToString()));

                Action onCollect = () =>
                {
                    cq.CompleteWithRewardCollection();
                    DebugLogger.Log($"[CommissionIntent] CollectReward: player collected reward from {ctx.Speaker.Name}");
                    if (ctx.Controller != null)
                        ctx.Controller.CloseDialogue();
                };

                if (ctx.Controller != null)
                {
                    ctx.Controller.SceneSay(line,
                        // 结账选项：收下报酬
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_collect_option", "Take the reward"), onCollect),
                        // 结账选项：稍后再说
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_later_option", "(Later)"), () => ctx.Controller.CloseDialogue()));
                }
                else
                {
                    // 结账弹窗标题：领取报酬
                    string rewardTitle = LWNTextHelper.ResolveText("LWN_intent_commission_reward_title", "Collect reward");
                    // 结账弹窗正文：报酬叙事台词
                    string rewardBody = LWNTextHelper.ResolveCompound("LWN_intent_commission_reward_body", ("LINE", line));
                    // 结账弹窗按钮：领取
                    string collectBtn = LWNTextHelper.ResolveText("LWN_intent_commission_collect_option", "Take the reward");
                    // 结账弹窗按钮：稍后再说
                    string laterBtn = LWNTextHelper.ResolveText("LWN_intent_commission_later_option", "(Later)");
                    InformationManager.ShowInquiry(new InquiryData(rewardTitle, rewardBody, true, true, collectBtn, laterBtn, onCollect, null));
                }
            }
            else
            {
                // 通用 Quest（兜底）
                string questTitle = _foundQuest.Title?.ToString()
                    // 任务标题兜底：无标题时用通称
                    ?? LWNTextHelper.ResolveText("LWN_intent_commission_quest_fallback", "Quest");
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

                // 通用任务结账台词：委托已办妥，列出报酬
                string line = LWNTextHelper.ResolveCompound("LWN_intent_commission_reward_line_generic",
                    ("QUEST", questTitle), ("REWARD", reward.ToString()));

                if (ctx.Controller != null)
                {
                    ctx.Controller.SceneSay(line,
                        // 结账选项：收下报酬
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_collect_option", "Take the reward"), onCollect),
                        // 结账选项：稍后再说
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_commission_later_option", "(Later)"), () => ctx.Controller.CloseDialogue()));
                }
                else
                {
                    // 结账弹窗标题：领取报酬
                    string rewardTitle = LWNTextHelper.ResolveText("LWN_intent_commission_reward_title", "Collect reward");
                    // 结账弹窗正文：报酬叙事台词
                    string rewardBody = LWNTextHelper.ResolveCompound("LWN_intent_commission_reward_body", ("LINE", line));
                    // 结账弹窗按钮：领取
                    string collectBtn = LWNTextHelper.ResolveText("LWN_intent_commission_collect_option", "Take the reward");
                    // 结账弹窗按钮：稍后再说
                    string laterBtn = LWNTextHelper.ResolveText("LWN_intent_commission_later_option", "(Later)");
                    InformationManager.ShowInquiry(new InquiryData(rewardTitle, rewardBody, true, true, collectBtn, laterBtn, onCollect, null));
                }
            }
        }
    }
}

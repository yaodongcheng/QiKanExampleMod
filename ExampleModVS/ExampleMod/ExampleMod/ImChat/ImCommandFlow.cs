using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 命令模式（需求 7：接入密令系统，用户决策 2：模式切换 + 批准卡片）：
    /// 命令文本 → 复用密令 LLM 管线（SceneSnapshot + BuildPlanPrompt + ChatAsync → PlanResponse）
    /// → IM 消息流插入计划卡片（同意/拒绝按钮）→ 批准 → SendEventToAgent("order_execute_plan") 确定性执行
    /// → PlanExecutor 事件回报回 IM（系统消息，双渠道：原密信 DisplayMessage 保留）。
    ///
    /// 与 PlanCommandFlow 的关系：复用其 LLM 管线与执行入口（public/internal 静态 API），
    /// 但 IM 是独立状态机（不抢 PlanCommandFlow 的 _isActive——互斥用 IsActive 检查）。
    /// 密令是 Mission 级瞬态：命令消息不写 NPC 记忆（避免污染对话漏斗），但群聊 store 里的
    /// 命令文本/计划卡片/系统回报会随 lwn_im_group_* 存档（执行状态 ExecutorId 读档后保留——
    /// 执行器已不在场，卡片显示为「执行中」且无法中止，属已知取舍）。
    /// </summary>
    public static class ImCommandFlow
    {
        /// <summary>卡片 ExecutorId 语义常量：已拒绝。</summary>
        public const string Rejected = "rejected";
        /// <summary>卡片 ExecutorId 语义常量：已了结（完成/中止）。</summary>
        public const string Done = "done";
        /// <summary>卡片 ExecutorId 语义常量：🔴 2026-08-10（Q2）已修改（玩家点了修改，旧卡作废，等待新版计划）。</summary>
        public const string Superseded = "superseded";
        /// <summary>修改额度上限（Q2：复用 PlanReplan 的 ≤2 次语义，防玩家无限修改刷 LLM）。</summary>
        public const int MaxModifyCount = 2;

        private class PendingRequest
        {
            public ImConversation Conv;
            public string Command;
            public bool IsModify;       // 🔴 Q2：修改管线（新卡片带「修改版」标记）
            public int ModifyCount;     // 🔴 Q2：修改版计数
            // 🔴 2026-08-14（M4 风险审视 plan_needed）：随从战术方向（risk_analysis 第一人称）——
            // BuildPlanPrompt 单独成段【随从的打算】，不混入【命令】段（防"谁的命令"语义混淆）
            public string CompanionIntention;
            // 🔴 2026-08-15（目标唯一标记）：回复轮已解析目标（含 #N）——计划轮【目标指认】段直接引用
            public string ResolvedTargetText;
        }

        /// <summary>
        /// 澄清轮状态（🔴 Q1 IM 化：替代 vanilla 澄清输入框）：LLM 返回 questions 后挂起，
        /// 玩家在密令模式的下一句回复并入命令上下文继续生成计划（≤2 轮，与当面 Plot 语义一致）。
        /// </summary>
        private class PendingClarify
        {
            public ImConversation Conv;
            public string Command;   // 已合并的当前命令文本
            public int Round;        // 已完成澄清轮数（≥2 时再收到 questions → 诚实放弃）
        }

        // 🔴 2026-08-12（合并闲聊/计划模式）：执行期说话 → 计划调整（方案 A，用户裁定）。
        // 玩家在计划执行期间发消息，主线程捕获执行上下文（字符串快照），随闲聊回复管线
        // 传到后台 prompt 注入 → LLM 回包判定 adjust_plan → 主线程投递点转 RequestModify。
        // 只存字符串，无 Agent/native 句柄——后台线程安全。
        public class ImExecutionContext
        {
            public string ConvId;          // 执行中卡片所在会话
            public string ExecutorHeroId;  // 执行者（执行中卡片 ExecutorId）
            public string PlanSummary;     // 卡片 PlanSummary（计划摘要）
            public string CurrentStep;     // PlanExecutor.CurrentSummary（当前步骤摘要）
            public string Intent;          // 卡片 PlanIntent
        }

        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：会话计划状态（模式指示文本派生 + 输入路由）。
        /// 建议按钮待决不改变 phase（闲聊层）。优先级：Generating > Executing > PendingPlan > Chat。</summary>
        public enum ImSessionPhase { Chat, Generating, PendingPlan, Executing }

        // LLM 结果回主线程消费（PlanCommandFlow 同款 _pendingResult/_resultReady 模式）
        private static PendingRequest _pending;
        private static ImConversation _lastConv;   // 结果归属会话（FinishWith 清 _pending 后仍可定位）
        private static bool _resultReady;
        private static PlanResponse _pendingResult;
        private static PendingClarify _pendingClarify;
        private static string _lastCommand;        // 上一条玩家命令（澄清轮挂起基准）
        private static int _lastModifyCount;       // 本请求的修改版计数（新卡片标记用）

        // 等待执行器出现后补挂回报事件（executor 由 AgentBrain 异步 Create）
        private class PendingWire
        {
            public ImConversation Conv;
            public ImMessage Card;
            public string HeroId;
            public long StartMs;         // 墙钟起点（超时判定，帧数会随 fps/双 tick 漂移）
        }

        private static readonly List<PendingWire> _pendingWires = new List<PendingWire>();

        // 🔴 计划讲解（2026-08-11 用户裁定：按钮 = 确定性事件 → LLM 人话讲解，不靠玩家打字识别意图）。
        // LLM 回包在异步线程只入队（lock），主线程 Tick 消费：成功 = NPC 讲解消息上屏；失败 = 用计划摘要口述。
        private class ExplainJob
        {
            public string ConvId;
            public string SenderId, SenderName;
            public string Line;              // 讲解文本（null/空 = LLM 失败 → 摘要口述）
            public bool FoundIssue;          // 自查发现问题（结构化输出，写回卡片 → 重拟按钮显示条件）
            public ImMessage Card;           // 所属卡片（主线程写回 ReviewFoundIssue/ReviewLine）
            public string BubbleHeroId;      // 场景内冒泡口述的执行者 HeroId（主线程解析 Agent——后台线程禁碰 native 句柄）
            public Action<bool> OnDone;      // 主线程回调（Tick 消费时执行）
        }

        /// <summary>讲解 LLM 结构化输出（铁律 2：字段全 null-guard）。</summary>
        private class ExplainResult
        {
            [JsonProperty("line")]
            public string Line;

            [JsonProperty("found_issue")]
            public bool FoundIssue;
        }

        private static readonly List<ExplainJob> _explainQueue = new List<ExplainJob>();
        private static readonly object _explainLock = new object();

        /// <summary>是否有请求在途（互斥：一次只处理一条命令）。</summary>
        public static bool IsBusy => _pending != null || _resultReady;

        // ───────────────────────── 下达 ─────────────────────────

        /// <summary>玩家在密令模式发送命令文本 → 追加消息 + 计划生成（Mission）/ 规则解析计划（Campaign，Q5b）。
        /// 🔴 Q1：若存在本会话的澄清轮（_pendingClarify），本条消息作为澄清回答并入命令上下文重新生成。</summary>
        /// <param name="companionIntention">🔴 2026-08-14（M4）：随从战术方向（risk_analysis 第一人称，
        /// 随从自己说的话）——计划轮 prompt 单独成段【随从的打算】，计划由计划轮 LLM 决定（他说的不算）。
        /// 普通命令/「制定计划」按钮传 null（零行为变化）。</param>
        /// <param name="resolvedTargetText">🔴 2026-08-15（目标唯一标记）：回复轮已解析的目标（LLM
        /// action_target 原文，含 #N 如 "酒馆店主#3"）——计划轮【目标指认】段直接引用，不再二次解析
        /// 玩家原话（「酒馆老板」→「酒馆店主#3」映射固定）。</param>
        public static void RequestCommand(ImConversation conv, string command, string companionIntention = null,
            string resolvedTargetText = null)
        {
            if (conv == null || string.IsNullOrWhiteSpace(command)) return;
            // 门控复查（UI 已查，铁律 2 风格双保险）
            if (!ImChatView.IsCommandModeAvailable(conv))
            {
                // 提示：密令不可用
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_unavailable", "Command mode is unavailable here."));
                return;
            }
            if (Mission.Current != null && PlanCommandFlow.IsActiveForOtherConv(conv))
            {
                // 提示：另有密谋进行中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_plot_active", "Another secret order is already being discussed."));
                return;
            }
            if (Mission.Current != null && IsBusy)
            {
                // 提示：上一条命令处理中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_cmd_busy", "Still thinking about your previous order..."));
                return;
            }
            // 澄清轮：玩家回复并入命令上下文（替代 vanilla 澄清输入框，≤2 轮）
            string cmd = command.Trim();
            if (_pendingClarify != null && _pendingClarify.Conv?.Id == conv.Id)
            {
                cmd = $"{_pendingClarify.Command}（{cmd}）";  // lwn-ignore: A
                _pendingClarify.Command = cmd;
                _pendingClarify.Round++;
            }
            // 命令文本入会话（store；不写 NPC 记忆——密令是 Mission 级瞬态）
            // 🔴 2026-08-15（私聊消息顺序修复配套）：玩家消息已由 SendPlayerMessage 发送时写入 store
            //（私聊修复后同一命令会先经发送路径再走本方法）——写前查重防双写（同发送者同内容已有 → 跳过，
            // 含自动触发时序：store 末条可能是刚投递的随从台词，必须全列表扫描）；
            // 密令输入框/提议批准/needPlan 按钮等直通路径（消息未经发送路径）仍需写入。
            bool alreadyStored = ImChatStore.GetGroupMessages(conv.Id).Any(m => m != null
                && m.SenderHeroId == ImChatManager.PlayerId && m.Kind == ImMessageKind.Text
                && string.Equals(m.Content?.Trim(), cmd.Trim(), StringComparison.Ordinal));
            if (!alreadyStored)
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                    Hero.MainHero?.Name?.ToString() ?? "You", cmd, ImMessageKind.Text)
                {
                    ConvId = conv.Id,
                });
            if (Mission.Current == null)
            {
                // Campaign 大地图：规则解析计划（零 LLM；私聊有 party 的 Hero）
                _pendingClarify = null;   // Campaign 计划无澄清轮
                ImMarchOrder.RequestMarchOrder(conv, cmd);
                return;
            }
            _pending = new PendingRequest
            {
                Conv = conv,
                Command = cmd,
                CompanionIntention = companionIntention,
                ResolvedTargetText = resolvedTargetText,
            };
            // 生成中占位行（思考中文案，与「正在输入」同款）
            AppendGenerating(conv);
            _ = CallPlanAsync(_pending);
        }
        /// <summary>LLM 一次调用（与 PlanCommandFlow.CallPlanAsync 同管线，意图/词表复用其 internal API）。</summary>
        private static async Task CallPlanAsync(PendingRequest req)
        {
            PlanResponse response = null;
            try
            {
                if (!Settings.Instance.IsLLMConfigured) { FinishWith(req, null); return; }
                var snapshot = SceneSnapshot.Build(Mission.Current, agentLimit: 30);
                string persona = BuildPersona(req.Conv);
                string prompt = PromptBuilder.BuildPlanPrompt(
                    snapshot.ToPromptText(), req.Command, persona, "",
                    PlanCommandFlow.IntentTableForPrompt(), PlanCommandFlow.GrammarForPrompt(),
                    companionIntention: req.CompanionIntention,
                    resolvedTargetText: req.ResolvedTargetText);
                string json = await LLMService.Instance.ChatAsync(prompt, 4000, true, 0.4f, disableReasoning: true);
                string cleaned = LLMService.CleanJson(json);
                try { response = JsonConvert.DeserializeObject<PlanResponse>(cleaned); }
                catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 计划 JSON 解析失败: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] LLM 调用失败: {ex.Message}");
            }
            FinishWith(req, response);
        }
        private static void FinishWith(PendingRequest req, PlanResponse response)
        {
            _lastConv = req?.Conv;
            _lastCommand = req?.Command;   // 澄清轮挂起用（原命令基准）
            _lastModifyCount = req?.IsModify == true ? req.ModifyCount : 0;   // 修改版计数（新卡片标记）
            _pending = null;
            _pendingResult = response;
            _resultReady = true;
        }
        /// <summary>主线程消费（ImChatManager.Tick → 本方法）。</summary>
        public static void Tick()
        {
            if (_resultReady)
            {
                _resultReady = false;
                var response = _pendingResult;
                _pendingResult = null;
                var conv = _lastConv;
                _lastConv = null;
                int modifyCount = _lastModifyCount;
                _lastModifyCount = 0;
                // 占位行替换（无论成败：卡片上屏 / 失败系统消息）
                RemoveGenerating(conv);
                if (response == null)
                {
                    // 密令失败：计划生成失败
                    _pendingClarify = null;
                    // 本地化：LWN_im_cmd_fail（玩家可见文本）
                    PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
                    return;
                }
                // 澄清轮 IM 化（🔴 Q1）：NPC 问句 = 一条 NPC 消息，玩家回复并入命令上下文（RequestCommand 合并路径）。
                // 铁律 2：needs_clarification 标志位不可信——只有 questions 真的带候选才进入澄清轮。
                if (response.Questions != null && response.Questions.Count > 0)
                {
                    // 澄清超轮（Round ≥ 2）→ 诚实放弃
                    if (_pendingClarify != null && _pendingClarify.Conv?.Id == conv?.Id && _pendingClarify.Round >= 2)
                    {
                        _pendingClarify = null;
                        // 澄清超轮放弃
                        PostNpcMessage(conv, LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."));
                        return;
                    }
                    // 首轮澄清：挂起状态（原命令 = 上一条玩家命令），等玩家回复
                    if (_pendingClarify == null || _pendingClarify.Conv?.Id != conv?.Id)
                        _pendingClarify = new PendingClarify { Conv = conv, Command = _lastCommand, Round = 0 };
                    var q = response.Questions[0];
                    // 澄清轮默认问句
                    string qText = q?.Q ?? LWNTextHelper.ResolveText("LWN_plan_clarify_default", "What do you mean exactly?");
                    if (q != null && q.Options != null && q.Options.Count > 0)
                        qText += "\n" + string.Join(" / ", q.Options.Select(o => $"「{o}」"));  // lwn-ignore: A
                    PostNpcMessage(conv, qText);
                    return;
                }
                _pendingClarify = null;
                // 词表外/无计划 → 诚实拒绝（与 PlanCommandFlow 同语义）
                if (response.Plan == null || response.Intent == null
                    || string.IsNullOrEmpty(response.Intent.IntentType) || response.Intent.IntentType == "CUSTOM")
                {
                    PostSystem(conv, string.IsNullOrEmpty(response.Reply)
                        // 密令被拒：词表外/无计划
                        ? LWNTextHelper.ResolveText("LWN_im_cmd_rejected", "The companion does not understand this order.")
                        : response.Reply);
                    return;
                }
                // 🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡）：计划消息 = NPC 自述消息——
                // Sender = 随从（私聊）/通用发言人（群聊），Content = LLM 简述（原独立 narration 消息并入本条），
                // 按钮行渲染在气泡内、锚点跟随链最新消息（讲解后按钮移动）。
                // 🔴 2026-08-12（原「卡片去描述」）：陈述缺省 → 摘要兜底，保证上屏时总有一条 NPC 简述。
                string narration = !string.IsNullOrWhiteSpace(response.Narration)
                    ? response.Narration
                    // 本地化：LWN_plan_default_summary（玩家可见文本）
                    : (response.Plan?.Summary ?? LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?"));
                string summary = !string.IsNullOrEmpty(response.Plan.Summary) ? response.Plan.Summary
                    // 计划摘要缺省文案
                    : LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?");
                ResolveSpeaker(conv, out string heroId, out string senderName);
                var card = new ImMessage(heroId, senderName, narration, ImMessageKind.PlanCard)
                {
                    ConvId = conv?.Id ?? "",
                    ResponseJson = JsonConvert.SerializeObject(response,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                    PlanJson = JsonConvert.SerializeObject(response.Plan,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                    PlanSummary = summary,
                    PlanIntent = response.Intent.IntentType,
                    // 🔴 Q2/Q3：修改版标记 + 陈述 + C# 确定性详情（步骤/应急/安全网）
                    PlanModifyCount = modifyCount,
                    Narration = narration,
                    PlanDetailText = BuildPlanDetail(response.Plan),
                    // 🔴 2026-08-12：计划链锚点（按钮跟随链最新消息；讲解消息复制同 id）
                    ChainId = Guid.NewGuid().ToString(),
                };
                ImChatStore.AppendGroupMessage(card.ConvId, card);
                ImChatStore.IncUnread(card.ConvId);
                ImChatManager.BroadcastMessageArrived(conv);
            }
            // 执行器补挂回报事件（executor 异步创建，轮询重试 ~5s 墙钟超时）
            if (_pendingWires.Count > 0)
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                for (int i = _pendingWires.Count - 1; i >= 0; i--)
                {
                    var wire = _pendingWires[i];
                    if (Mission.Current == null || nowMs - wire.StartMs > 5000)
                    {
                        _pendingWires.RemoveAt(i);
                        continue;
                    }
                    var agent = FindAgentByHeroId(wire.HeroId);
                    var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
                    if (executor == null) continue;
                    SubscribeExecutor(executor, wire.Conv, wire.Card);
                    _pendingWires.RemoveAt(i);
                }
            }
            // 🔴 计划讲解投递（主线程：成功 = NPC 讲解消息上屏 [IM-Store 自动记录]；失败 = onDone(false) → 展开 C# 详情）
            if (_explainQueue.Count > 0)
            {
                List<ExplainJob> jobs;
                lock (_explainLock)
                {
                    jobs = new List<ExplainJob>(_explainQueue);
                    _explainQueue.Clear();
                }
                foreach (var job in jobs)
                {
                    if (job != null && !string.IsNullOrWhiteSpace(job.Line))
                        ImChatStore.AppendGroupMessage(job.ConvId,
                            new ImMessage(job.SenderId, job.SenderName, job.Line, ImMessageKind.Text)
                            {
                                // 🔴 2026-08-12：链标记——按钮锚点随讲解消息下移（讲解正文 = 链最新消息）
                                ChainId = job.Card?.ChainId,
                            });
                    // 🔴 2026-08-12：自查结果写回卡片（重拟按钮显示条件；重拟定向上下文）——主线程，安全
                    if (job?.Card != null)
                    {
                        job.Card.ReviewFoundIssue = job.FoundIssue;
                        job.Card.ReviewLine = job.Line;
                    }
                    // 🔴 2026-08-12：场景内执行者在场 → 冒泡口述（主线程解析 Agent + 说话并联——
                    // 后台线程禁碰 Agent native 句柄；远距离密信 = 仅聊天流）
                    if (job != null && !string.IsNullOrEmpty(job.Line) && !string.IsNullOrEmpty(job.BubbleHeroId)
                        && Mission.Current != null)
                    {
                        try
                        {
                            var agent = FindAgentByHeroId(job.BubbleHeroId);
                            if (agent != null && agent.IsActive())
                                SpeechChannel.Say(agent, job.Line, SpeechPriority.Dialogue,
                                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(agent), Agent.Main, "plan_report",
                                        // 本地化：LWN_im_btn_review（玩家可见文本）
                                        LWNTextHelper.ResolveText("LWN_im_btn_review", "Self-review")));
                        }
                        catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 讲解冒泡失败: {ex.Message}"); }
                    }
                    try { job?.OnDone?.Invoke(job != null && !string.IsNullOrWhiteSpace(job.Line)); } catch { }
                }
            }
        }
        // ───────────────────────── 批准/拒绝/中止 ─────────────────────────
        /// <summary>批准/拒绝计划卡片（用户决策 2：批准在 IM 内完成）。</summary>
        public static void Resolve(ImMessage msg, bool approve)
        {
            if (msg == null || !msg.IsPlanCard || !string.IsNullOrEmpty(msg.ExecutorId)) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            if (!approve)
            {
                msg.ExecutorId = Rejected;
                // 密令已撤回
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_cancelled", "Order cancelled."));
                // 🔴 Q1：拒绝 = 密谋输入阶段结束（玩家可立即在该会话重发新命令）
                PlanCommandFlow.End();
                // 🔴 2026-08-12（用户裁定）：拒绝 = 计划彻底抛弃——命令/陈述/卡片从 store 抹除，
                // 不再进入后续上下文（群聊【频道近期消息】）与 UI；私聊命令本就"不写 NPC 记忆"（Mission 级瞬态）。
                ScrubRejectedPlan(conv, msg);
                return;
            }
            PlanResponse response = null;
            try { response = JsonConvert.DeserializeObject<PlanResponse>(msg.ResponseJson); }
            catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 卡片响应解析失败: {ex.Message}"); }
            if (response == null || response.Plan == null)
            {
                msg.ExecutorId = Rejected;
                // 密令失败：卡片响应解析失败
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
                return;
            }
            // 执行者解析（Q5 多人协作）：PlanExecutor 原生一带多（subjects → 多 ActorCursor），
            // owner = 第一个执行者（SendEventToAgent 目标），回报消息列出全部执行者
            var executors = ResolveExecutors(conv, response);
            if (executors.Count == 0)
            {
                // 密令失败：无随从在场
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_no_companion", "No companion is present to carry out the order."));
                return;
            }
            Agent executor = executors[0];
            try
            {
                ApplyPlan(conv, executor, response, msg);
                var hero = (executor.Character as CharacterObject)?.HeroObject;
                msg.ExecutorId = hero?.StringId ?? executor.Name;
                string names = string.Join("、", executors.Select(a => a.Name?.ToString() ?? "?"));
                // 密令开始执行（{NAME} 支持多人："张三、李四"）
                PostSystem(conv, LWNTextHelper.ResolveCompound("LWN_im_cmd_started",
                    "{NAME} has begun carrying out the order.", ("NAME", names)));
                // 🔴 Q1：批准 = 密谋输入阶段结束（执行阶段独立，StopPlan 行按执行器状态显示）
                PlanCommandFlow.End();
                // 🔴 2026-08-12（用户反馈）：批准后关闭 IM 面板——玩家直接观察执行，
                // 不再被面板挡着；执行进度走执行摘要 HUD（AgentHud）与当面/密信回报。
                // HandlePlanAction 在 Resolve 返回后对 _vm 判空，Close 置空 _vm 安全。
                ImChatView.Close();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] 计划下发失败: {ex.Message}");
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
            }
        }
        /// <summary>中止执行中的计划（R3 停止键语义，远距离 IM 通道）。</summary>
        public static void Abort(ImMessage msg)
        {
            if (msg == null || !msg.IsPlanCard) return;
            if (string.IsNullOrEmpty(msg.ExecutorId) || msg.ExecutorId == Rejected || msg.ExecutorId == Done || msg.ExecutorId == Superseded) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            Agent agent = FindAgentByHeroId(msg.ExecutorId);
            var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
            if (executor != null)
            {
                executor.CancelByPlayer();
            }
            msg.ExecutorId = Done;
            // 密令中止
            PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_aborted", "The order has been called off."));
            // 🔴 Q1：中止 = 密谋输入阶段结束（幂等保险）
            PlanCommandFlow.End();
        }
        // ───────────────────────── 修改计划（🔴 Q2，2026-08-10）─────────────────────────
        /// <summary>
        /// 玩家修改计划（Q2）：卡片【修改】→ 输入框 → 修改意见 → 原命令 + 修改意见拼成新命令
        /// → 走同一条 LLM 计划管线 → 新 PlanCard（「修改版 vN」徽标）→ 再批准。
        /// <summary>修改额度：修改 ≤ MaxModifyCount（2 次，成功产出才消耗——复用 Replan 语义，防无限修改刷 LLM）。
        /// 覆盖两态：批准前（卡片待批）与执行中（先中止当前执行，CancelByPlayer 不触发 Replan 自动重入）。
        /// 🔴 2026-08-12（执行期调整 appendPlayerText）：群聊路径玩家消息已被 SendPlayerMessage 写入 store
        /// （公区事实源），adjust 触发时再追加会重复——传 false；私聊路径玩家消息走记忆层不在 store，传 true。</summary>
        public static void RequestModify(ImMessage msg, string text, bool appendPlayerText = true)
        {
            if (msg == null || !msg.IsPlanCard || string.IsNullOrWhiteSpace(text)) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            if (msg.PlanModifyCount >= MaxModifyCount)
            {
                // 修改额度用尽
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_exhausted", "The plan has been revised too many times. Approve it or start over."));
                return;
            }
            if (!ImChatView.IsCommandModeAvailable(conv))
            {
                // 提示：密令不可用
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_unavailable", "Command mode is unavailable here."));
                return;
            }
            if (Mission.Current != null && IsBusy)
            {
                // 提示：上一条命令处理中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_cmd_busy", "Still thinking about your previous order..."));
                return;
            }
            // 执行中 → 中止当前执行（玩家中止路径不触发 Replan 自动重入——OnAborted 仅内部 allowReplan 触发）
            if (IsExecuting(msg))
            {
                Agent agent = FindAgentByHeroId(msg.ExecutorId);
                var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
                if (executor != null) executor.CancelByPlayer();
            }
            // 旧卡片标记已修改（按钮全部消失；执行中的中止报告不覆盖此状态）
            msg.ExecutorId = Superseded;
            // 原命令：卡片前最近一条玩家命令消息（PlanCard 摘要不足语义，命令文本在 store 里）
            string original = FindOriginalCommand(conv, msg);
            string cmd = $"{original}（修改：{text.Trim()}）";  // lwn-ignore: A
            // 玩家修改意见入会话（store；不写 NPC 记忆——密令瞬态）
            if (appendPlayerText)
            {
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                    Hero.MainHero?.Name?.ToString() ?? "You", text.Trim(), ImMessageKind.Text)
                {
                    ConvId = conv.Id,
                });
            }
            // 本地化：修改重拟提示
            PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_pending", "The companion is working out a revised plan."));
            if (Mission.Current == null)
            {
                // Campaign 计划无修改（Campaign 侧规则解析，零 LLM）
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_need_mission", "You can only revise a plan while in the field."));
                return;
            }
            _pending = new PendingRequest
            {
                Conv = conv,
                Command = cmd,
                IsModify = true,
                ModifyCount = msg.PlanModifyCount + 1,
            };
            AppendGenerating(conv);
            _ = CallPlanAsync(_pending);
        }
        /// <summary>原命令文本：store 中卡片前最近一条玩家 Text 消息（命令文本确实入 store，RequestCommand 写入）。</summary>
        private static string FindOriginalCommand(ImConversation conv, ImMessage card)
        {
            if (conv == null || card == null) return "";
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m == card) break;
                if (m.SenderHeroId == ImChatManager.PlayerId && m.Kind == ImMessageKind.Text && !string.IsNullOrWhiteSpace(m.Content))
                    return m.Content;
            }
            return card.PlanSummary ?? "";
        }
        /// <summary>会话内最新一张待批计划卡片（ExecutorId 空）。🔴 2026-08-12（用户裁定：
        /// 修改按钮废除 → 输入框发送即修改）：待批卡片存在时，命令模式的发送走 RequestModify 而非新命令。</summary>
        public static ImMessage FindLatestPendingCard(ImConversation conv)
        {
            if (conv == null) return null;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m != null && m.IsPlanCard && string.IsNullOrEmpty(m.ExecutorId))
                    return m;
            }
            return null;
        }
        // ───────────────────────── 🔴 2026-08-12（合并闲聊/计划模式）：派生状态 + needPlan 建议 + 执行期调整 ─────────────────────────
        /// <summary>会话是否有挂起的澄清轮（计划生成在等玩家澄清回答——该会话的发送走 RequestCommand 合并，不掺建议）。</summary>
        public static bool HasPendingClarify(ImConversation conv)
        {
            return _pendingClarify != null && _pendingClarify.Conv?.Id == conv?.Id;
        }
        /// <summary>会话内最新一张执行中计划卡片（ExecutorId = 执行者 heroId）。</summary>
        public static ImMessage FindLatestExecutingCard(ImConversation conv)
        {
            if (conv == null) return null;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m != null && m.IsPlanCard && IsExecuting(m))
                    return m;
            }
            return null;
        }
        /// <summary>会话内最新一张**指定执行者**的执行中卡片（执行期调整定位用——ctx 带 heroId 无歧义，
        /// 防群聊多人协作时找错执行者；执行已了结/回报在途 → 返回 null，只回台词不产生孤儿修改链）。</summary>
        public static ImMessage FindLatestExecutingCardByHeroId(ImConversation conv, string heroId)
        {
            if (conv == null || string.IsNullOrEmpty(heroId)) return null;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m != null && m.IsPlanCard && IsExecuting(m) && m.ExecutorId == heroId)
                    return m;
            }
            return null;
        }
        /// <summary>会话是否有执行中计划（needPlan 建议抑制规则之一：执行期只走 adjustPlan 通道）。</summary>
        public static bool HasExecutingCard(ImConversation conv) => FindLatestExecutingCard(conv) != null;
        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：会话计划状态派生（模式指示文本 + 输入路由）。
        /// 优先级：Generating（最瞬态）> Executing > PendingPlan > Chat。建议按钮待决不改变 phase（闲聊层）。
        /// 旧卡（rejected/done/superseded）跳过——只有最新活动状态才反映到指示文本。</summary>
        public static ImSessionPhase GetPhase(ImConversation conv)
        {
            if (conv == null) return ImSessionPhase.Chat;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m == null) continue;
                if (m.IsGenerating) return ImSessionPhase.Generating;
                if (m.IsPlanCard && IsExecuting(m)) return ImSessionPhase.Executing;
                if (m.IsPlanCard && string.IsNullOrEmpty(m.ExecutorId)) return ImSessionPhase.PendingPlan;
            }
            return ImSessionPhase.Chat;
        }
        /// <summary>玩家发新消息 → 同会话全部待决建议按钮作废（ExecutorId="superseded"，按钮随锚点重算消失）。</summary>
        public static void InvalidateSuggestions(ImConversation conv)
        {
            if (conv == null) return;
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                foreach (var m in msgs)
                {
                    if (m != null && m.IsPlanSuggest && string.IsNullOrEmpty(m.ExecutorId))
                        m.ExecutorId = Superseded;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] InvalidateSuggestions 异常: {ex.Message}");
            }
        }
        /// <summary>🔴 2026-08-12（needPlan 建议 → 通用消息底部按钮，用户裁定不用计划卡片）：
        /// LLM 判定 need_plan 后，给**刚投递的 NPC 回复消息**打标（IsPlanSuggest + CommandText），
        /// 渲染复用既有 ShowCardBubble + 通用按钮行（制定计划/先不用）。主线程投递点调用
        /// （投递循环内顺序执行，store 最后一条 = 刚投递的 NPC 消息，无竞态）。
        /// 抑制规则（任一命中不打标，NPC 台词照常投递）：
        /// ① !IsCommandModeAvailable —— 一网打尽：附近/家族/王国频道、Plot 总闸、无 LLM、Campaign 无 party Hero
        /// ② IsBusy —— 计划生成中防并发（全局锁，保守）
        /// ③ HasPendingClarify —— 澄清轮挂起（回答走 RequestCommand 合并，不掺建议）
        /// ④ FindLatestExecutingCard —— 执行期只走 adjustPlan 通道</summary>
        /// <summary>needPlan 建议打标：给 NPC 回复消息挂「制定计划/先不用」按钮（玩家确认后才生成计划）。
        /// 🔴 2026-08-15（plan_needed 全手动裁定）：riskAnalysis 参数 = 随从战术方向（risk_analysis 原文）——
        /// plan_needed 场景由 RiskAssessor 调本方法挂按钮并随带战术方向，玩家点「制定计划」后
        /// RequestCommand(companionIntention) 进计划轮【随从的打算】段；普通 need_plan（无风险段）传 null。
        /// 🔴 2026-08-15（目标唯一标记）：resolvedTargetText = 回复轮 LLM 的 action_target（含 #N，如
        /// "酒馆店主#3"）——随按钮存储，计划轮【目标指认】段直接引用（不再二次解析玩家原话）。</summary>
        public static void TryAttachSuggestion(ImConversation conv, string heroId, string heroName, string playerText,
            string riskAnalysis = null, string resolvedTargetText = null)
        {
            try
            {
                if (conv == null || string.IsNullOrEmpty(heroId)) return;
                if (!ImChatView.IsCommandModeAvailable(conv)) return;
                if (IsBusy) return;
                if (HasPendingClarify(conv)) return;
                if (FindLatestExecutingCard(conv) != null) return;
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                ImMessage target = null;
                for (int i = msgs.Count - 1; i >= 0; i--)
                {
                    var m = msgs[i];
                    if (m != null && m.Kind == ImMessageKind.Text && m.SenderHeroId == heroId
                        && !string.IsNullOrWhiteSpace(m.Content))
                    {
                        target = m;
                        break;
                    }
                }
                if (target == null)
                {
                    DebugLogger.Log($"[ImCommandFlow] 建议打标失败: 找不到 {heroName} 刚投递的消息");
                    return;
                }
                InvalidateSuggestions(conv);   // 同会话旧待决建议作废，只留一张
                target.IsPlanSuggest = true;
                target.CommandText = string.IsNullOrWhiteSpace(playerText) ? target.Content : playerText.Trim();
                target.RiskAnalysisText = riskAnalysis;          // 🔴 2026-08-15：战术方向随按钮存储（全手动裁定）
                target.ResolvedTargetText = resolvedTargetText;  // 🔴 2026-08-15：已解析目标（含 #N）随按钮存储
                AutonomyProposal.Suppress(heroId);   // 本轮互斥：已有建议，不再投自主提议（防双卡）
                // 🔴 2026-08-15（按钮不显示根因修复）：打标发生在消息上屏之后——消息 VM 的
                // ShowCardBubble（卡片气泡容器）是计算属性，直接改字段不触发绑定刷新，按钮行
                //（容器内）不可见，直到切面板重开（全量重建）。这里通知 UI 立即重算锚点 + 广播形态。
                ImChatView.NotifyMessageShapeChanged(target);
                DebugLogger.Log($"[ImCommandFlow] needPlan 建议已打标: {heroName} → \"{target.Content}\"");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] TryAttachSuggestion 异常: {ex.Message}");
            }
        }
        /// <summary>🔴 2026-08-12（执行期说话 → 计划调整，方案 A）：主线程捕获执行上下文
        /// （纯字符串快照，后台线程安全）。无执行中计划返回 null。</summary>
        public static ImExecutionContext BuildExecutionContext(ImConversation conv)
        {
            if (conv == null || Mission.Current == null) return null;
            var card = FindLatestExecutingCard(conv);
            if (card == null) return null;
            string currentStep = "";
            try
            {
                Agent agent = FindAgentByHeroId(card.ExecutorId);
                var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
                currentStep = executor?.CurrentSummary ?? "";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] BuildExecutionContext 异常: {ex.Message}");
            }
            return new ImExecutionContext
            {
                ConvId = conv.Id,
                ExecutorHeroId = card.ExecutorId,
                PlanSummary = card.PlanSummary,
                CurrentStep = currentStep,
                Intent = card.PlanIntent,
            };
        }
        /// <summary>🔴 2026-08-12（执行期说话 → 计划调整）：LLM 判定 adjust_plan 后，主线程投递点调用。
        /// 全部前置复查（铁律 2 双保险）：Mission 才有执行器；IsBusy 防并发生成；按 heroId 复查执行中
        /// （执行已了结/回报在途 → 只回台词）；修改额度 ≤2（成功产出才消耗）。通过 → RequestModify
        /// 出「修改版」卡片 → 玩家批准 → 重执行（全程复用既有管线，CancelByPlayer 不触发 Replan 自动重入）。</summary>
        public static void TryAdjustFromExecution(ImExecutionContext ctx, string playerText)
        {
            try
            {
                if (ctx == null || string.IsNullOrWhiteSpace(playerText)) return;
                if (Mission.Current == null || IsBusy) return;
                var conv = ConversationOf(ctx.ConvId);
                if (conv == null) return;
                var card = FindLatestExecutingCardByHeroId(conv, ctx.ExecutorHeroId);
                if (card == null || !IsExecuting(card)) return;
                if (card.PlanModifyCount >= MaxModifyCount) return;
                RequestModify(card, playerText, appendPlayerText: conv.Type != ImConversationType.Direct);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] TryAdjustFromExecution 异常: {ex.Message}");
            }
        }
        /// <summary>
        /// 拒绝 = 抛弃计划（2026-08-12 用户裁定）：把本次计划交易从 store 整段抹除——
        /// 玩家命令 → NPC 陈述 → 计划卡片（含修改链：命令1→陈述1→卡片1(superseded)→…→当前卡片）。
        /// 群聊：不再进后续 LLM 的【频道近期消息】；私聊：UI 同步清掉（命令本就瞬态，不写 NPC 记忆）。
        /// 「密令已撤回」系统消息（在卡片之后）保留——通用通知，非计划内容。
        /// 🔴 追溯安全边界：只跨 superseded 卡片（同一条计划的修改链）；遇到「另一张待批卡片」
        /// （叠放命令）或「已执行卡片」（ExecutorId=随从）即停——那些不是被拒的这条计划。
        /// </summary>
        private static void ScrubRejectedPlan(ImConversation conv, ImMessage card)
        {
            if (conv == null || card == null) return;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            int cardIdx = msgs.FindIndex(m => m == card);
            if (cardIdx < 0) return;
            // 锚点 = 卡片前最近一条玩家命令（FindOriginalCommand 同源语义）
            int anchor = -1;
            for (int i = cardIdx - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m.SenderHeroId == ImChatManager.PlayerId && m.Kind == ImMessageKind.Text
                    && !string.IsNullOrWhiteSpace(m.Content))
                {
                    anchor = i;
                    break;
                }
            }
            if (anchor < 0) { ImChatStore.RemoveMessageRange(conv.Id, cardIdx, 1); return; }   // 防御：只删卡片
            // 向后追溯修改链（严格边界见方法注释）：
            // 新结构（2026-08-12）：命令 → 计划卡片（NPC 简述自述）→ [讲解消息]；修改链 =
            // 命令1 → 卡片1(superseded) → [讲解1] → 命令2(修改) → 卡片2 → [讲解2] …
            int start = anchor;
            while (start > 0)
            {
                var m = msgs[start - 1];
                if (m.IsGenerating) { start--; continue; }
                if (m.IsSystem) { start--; continue; }
                if (m.IsPlanCard && m.ExecutorId == Superseded) { start--; continue; }
                if (m.Kind == ImMessageKind.Text && m.SenderHeroId != ImChatManager.PlayerId
                    && msgs[start].IsPlanCard && msgs[start].ExecutorId == Superseded) { start--; continue; }   // 旧结构：陈述在卡片前
                if (m.Kind == ImMessageKind.Text && m.SenderHeroId == ImChatManager.PlayerId
                    && msgs[start].Kind == ImMessageKind.Text && msgs[start].SenderHeroId != ImChatManager.PlayerId
                    && start + 1 < msgs.Count && msgs[start + 1].IsPlanCard
                    && msgs[start + 1].ExecutorId == Superseded) { start--; continue; }                         // 旧结构：命令→陈述→卡片
                if (m.Kind == ImMessageKind.Text && m.SenderHeroId == ImChatManager.PlayerId
                    && msgs[start].IsPlanCard && msgs[start].ExecutorId == Superseded) { start--; continue; }   // 新结构：命令直接在卡片前
                // 🔴 2026-08-12：讲解消息（带 ChainId 的 NPC 文本）——仅当所属卡片已 superseded 才并入抹除
                //（叠放命令场景：讲解属于仍待批的卡片 → 保留，break 停在这里）
                if (m.Kind == ImMessageKind.Text && !string.IsNullOrEmpty(m.ChainId))
                {
                    ImMessage owner = null;
                    foreach (var x in msgs)
                    {
                        if (x != null && x.IsPlanCard && x.ChainId == m.ChainId) { owner = x; break; }
                    }
                    if (owner != null && owner.ExecutorId == Superseded) { start--; continue; }
                }
                break;
            }
            // 🔴 2026-08-12：向前追溯讲解消息（卡片后同链消息：详解正文）——拒绝 = 整条计划交易抹除。
            // 只扫连续段：中间插入其他消息（叠放命令的新链）即停——那些不是被拒的这条计划
            //（此时被拒卡片的按钮已因锚点规则隐藏，叠放 + 拒旧卡组合在 UI 上不可达，防御即可）。
            // 🔴 旧格式卡片无 ChainId（null == null 会误伤全表）——必须跳过。
            int end = cardIdx;
            if (!string.IsNullOrEmpty(card.ChainId))
            {
                for (int i = cardIdx + 1; i < msgs.Count; i++)
                {
                    if (msgs[i] != null && msgs[i].ChainId == card.ChainId) end = i;
                    else break;
                }
            }
            ImChatStore.RemoveMessageRange(conv.Id, start, end - start + 1);
            DebugLogger.Log($"[ImCommandFlow] 拒绝抛弃计划：抹除 store 消息 {end - start + 1} 条（会话 {conv.Id}）");
        }
        /// <summary>
        /// 重拟（🔴 2026-08-12 用户裁定：二次校验发现问题时给玩家"同命令重新生成"的出口）：
        /// 原命令原样重走一遍 LLM 计划管线（不合并修改意见——那是输入框发送的语义）。
        /// 与修改共用额度（PlanModifyCount ≤ 2，成功产出才消耗）——防无限重拟刷 LLM；
        /// 新卡片带「修改版 vN」徽标，旧卡片标记 superseded（按钮消失）。
        /// </summary>
        public static void RequestRegenerate(ImMessage card)
        {
            if (card == null || !card.IsPlanCard || !string.IsNullOrEmpty(card.ExecutorId)) return;
            var conv = ConversationOf(card.ConvId);
            if (conv == null) return;
            if (card.PlanModifyCount >= MaxModifyCount)
            {
                // 修改额度用尽
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_exhausted", "The plan has been revised too many times. Approve it or start over."));
                return;
            }
            if (!ImChatView.IsCommandModeAvailable(conv))
            {
                // 提示：密令不可用
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_unavailable", "Command mode is unavailable here."));
                return;
            }
            if (Mission.Current != null && IsBusy)
            {
                // 提示：上一条命令处理中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_cmd_busy", "Still thinking about your previous order..."));
                return;
            }
            // 旧卡片标记已重拟（按钮消失；与修改同语义）
            card.ExecutorId = Superseded;
            // 原命令原样重跑（不带修改意见——输入框发送才合并意见）
            string original = FindOriginalCommand(conv, card);
            if (string.IsNullOrWhiteSpace(original)) original = card.PlanSummary ?? "";
            // 🔴 2026-08-12（用户裁定：重拟文本与前次雷同）：讲解自查点名的问题作为定向上下文传入——
            // 同命令盲重roll 大概率产出相似计划；带上问题让 LLM 明确避开（重拟按钮仅在 ReviewFoundIssue=true 时显示，
            // 此时 ReviewLine 必有值）
            if (!string.IsNullOrWhiteSpace(card.ReviewLine))
                original = $"{original}（重拟要求：讲解自查发现「{card.ReviewLine}」——重新拟一个避开这些问题的方案）";  // lwn-ignore: A
            // 重拟提示
            PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_regenerating", "The companion is working out a new plan."));
            if (Mission.Current == null)
            {
                // Campaign 计划无重拟（Campaign 侧规则解析，零 LLM）
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_need_mission", "You can only revise a plan while in the field."));
                return;
            }
            _pending = new PendingRequest
            {
                Conv = conv,
                Command = original,
                IsModify = true,
                ModifyCount = card.PlanModifyCount + 1,
            };
            AppendGenerating(conv);
            _ = CallPlanAsync(_pending);
        }
        // ───────────────────────── 生成中占位行（🔴 2026-08-12：删进度条，文案与「正在输入」统一）─────────────────────────
        /// <summary>占位行：消息流内 NPC 思考气泡（🔴 2026-08-12：删进度条，文案与输入栏「正在输入」统一；
        /// 🔴 2026-08-12（用户裁定：融入 NPC 气泡）：Sender = 随从/通用发言人；
        /// 🔴 2026-08-12（用户反馈）：思考气泡不带名字前缀——正文纯「正在思考中…」（新 key），
        /// 名字行在 XML 中按 IsGenerating 隐藏；GenerateText 保留给旧存档渲染兜底）。</summary>
        private static void AppendGenerating(ImConversation conv)
        {
            if (conv == null) return;
            ResolveSpeaker(conv, out string heroId, out string senderName);
            // 思考中文案：无名字前缀（用户反馈——名字行/正文都去掉，微信「对方正在输入」语义）
            string thinkingText = LWNTextHelper.ResolveText("LWN_im_generating_plain", "Thinking...");
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(heroId, senderName, thinkingText, ImMessageKind.Generating)
            {
                ConvId = conv.Id,
                GenerateText = thinkingText,
            });
            ImChatManager.BroadcastMessageArrived(conv);
        }
        /// <summary>移除会话最后一条生成中占位（卡片上屏/失败时替换）。
        /// 🔴 2026-08-12 修复：GetGroupMessages 返回副本，直接 RemoveAt 只改副本——store 占位行残留，
        /// 导致「思考气泡 + 计划气泡」双显（hasGenerating 恒 true，转态重建不触发）。走 RemoveMessageRange 真删。</summary>
        private static void RemoveGenerating(ImConversation conv)
        {
            if (conv == null) return;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                if (msgs[i].IsGenerating)
                {
                    ImChatStore.RemoveMessageRange(conv.Id, i, 1);
                    return;
                }
            }
        }
        // ───────────────────────── 卡片详情（🔴 §3.2，C# 确定性渲染，不信任 LLM 文案）─────────────────────────
            // 本地化：动作名标签表（plan_step 记忆/详情渲染）
        /// <summary>动作名 → 本地化标签（LWN_plan_action_*；渲染的是实际会被执行的逻辑——PlanExecutor 同一份 JSON）。
        /// 🔴 2026-08-13：改为 internal static——ActionHandler 决策播报复用同表（闲聊动作码一致，防两份标签漂移）。
        /// 2026-08-13 重构：查 ActionRegistry 主表（FindByLabelCode，ByCode 优先回落别名；
        /// increase_relation→relation_up 由 LabelKey 承载，order_attack→"attack" 统一标签）；
        /// 未知码兜底 key 名 = 码本身（原行为保留）。</summary>
        internal static string PlanActionLabel(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            var spec = ActionRegistry.FindByLabelCode(action);
            if (spec != null && !string.IsNullOrEmpty(spec.LabelKey))
                // 本地化：LWN_plan_action_（玩家可见文本）
                return LWNTextHelper.ResolveText("LWN_plan_action_" + spec.LabelKey, spec.LabelFallback);
            // 未知码兜底（原行为保留：key 名 = 码本身）
            return LWNTextHelper.ResolveText("LWN_plan_action_" + action, action);
        }
        /// <summary>C# 确定性详情渲染：步骤列表 + 应急行（contingencies/on_timeout）+ 安全网（guardrails 摘要）。
        /// 玩家看到的 = 执行器跑的（杜绝演示计划 ≠ 执行计划）。</summary>
        private static string BuildPlanDetail(Plan plan)
        {
            if (plan == null) return "";
            var sb = new StringBuilder();
            // 步骤列表
            if (plan.Steps != null && plan.Steps.Count > 0)
            {
                // 详情标题：计划步骤
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_detail_steps", "Steps:"));
                for (int i = 0; i < plan.Steps.Count; i++)
                {
                    var s = plan.Steps[i];
                    if (s == null) continue;
                    string target = RenderStepTargetText(s);
                    string line = string.IsNullOrEmpty(target)
                        ? PlanActionLabel(s.Action)
                        : PlanActionLabel(s.Action) + " " + target;
                    sb.AppendLine($"  {i + 1}. {line}");
                    if (!string.IsNullOrEmpty(s.OnTimeout) && s.OnTimeout != "@abort_gracefully")
            // 本地化：详情超时行
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_plan_detail_timeout", "    timeout → {TARGET}", ("TARGET", s.OnTimeout)));
                }
            }
            // 应急行（contingencies）
            if (plan.Contingencies != null && plan.Contingencies.Count > 0)
            {
            // 本地化：详情应急安排标题
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_detail_contingencies", "Contingencies:"));
                foreach (var c in plan.Contingencies)
                {
                    if (c?.When == null || string.IsNullOrEmpty(c.Then)) continue;
                    string cond = RenderCondition(c.When);
                    if (string.IsNullOrEmpty(cond)) continue;
                    // 应急行：若 {COND} 则 {THEN}
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_plan_detail_contingency_line",
                        "  if {COND} then {THEN}", ("COND", cond), ("THEN", c.Then)));
                }
            }
            // 安全网摘要（guardrails：既有 R1-R7 由执行器内建，这里只提示关键语义）
            sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_detail_guardrail",
                "  Safety: unexpected threats pause the plan; the stop key recalls the companion."));
            return sb.ToString();
        }
        // ───────────────────────── 计划讲解（🔴 2026-08-11 用户裁定）─────────────────────────
        /// <summary>
        /// 计划自审（🔴 2026-08-11 用户裁定 → 2026-08-12 改名）：玩家点「计划自审」按钮（**确定性事件**，
        /// 不靠玩家打字让 LLM 识别「自审计划」意图）
        /// → 执行者 LLM 口语化讲解：要做什么、分几步、出岔子怎么办（步骤 + 异常条件，人话）。
        /// prompt 只喂 C# 确定性渲染的计划内容（<see cref="BuildPlanDetail"/>：动作标签表 + 目标 + 应急 +
        /// 安全网），纪律 = 只许转述（同 narration，防幻觉，铁律 2 延伸）。
        /// 🔴 2026-08-12（讲解 = 二次校验）：讲解 prompt 内置「讲解前自查」——计划者本人复盘（当事人视角，
        /// 非上帝视角）：步骤顺序/成功条件可达性/失败路径完备性/步骤矛盾。发现隐患 → 讲解开头点名，
        /// 玩家听完讲解再决定 同意/拒绝/修改。三层防线分工：语法结构 = 确定性 PlanValidator（生成时）；
        /// 语义可行性 = 本讲解轮（批准前，信息性，不硬门禁——硬门禁的 LLM 误报会卡住玩家）；
        /// 运行时 = Guardrail R1-R7 + Replan。
        /// 异步：回包入队（_explainQueue，lock 线程安全），主线程 Tick 消费——成功 = NPC 口述消息上屏
        /// （[IM-Store] 自动记录）+ 场景内冒泡；失败 = 用计划摘要口述（人话，**绝不展示 JSON 详情**）。
        /// 🔴 发言人与冒泡：讲解人 = 会话对方随从（原 bug：SenderName 用了卡片上的玩家名 →
        /// 讲解以玩家自己的气泡上屏，玩家以为按钮没用）；冒泡在主线程 Tick 执行（后台线程禁碰 Agent）。
        /// 讲解消息 = 聊天流，不写 NPC 记忆（同 narration 偏差②）；叙事 = 执行者自述（当事人，非上帝视角）。
        /// </summary>
        public static void RequestPlanExplain(ImMessage card, Action<bool> onDone)
        {
            if (card == null || !card.IsPlanCard) { try { onDone?.Invoke(false); } catch { } return; }
            Plan plan = null;
            try
            {
                string json = !string.IsNullOrEmpty(card.PlanJson) ? card.PlanJson : card.ResponseJson;
                if (!string.IsNullOrEmpty(json))
                    plan = JsonConvert.DeserializeObject<Plan>(LLMService.CleanJson(json));
            }
            catch { }
            if (plan == null) { try { onDone?.Invoke(false); } catch { } return; }   // 无计划可讲 → 失败
            string detail = BuildPlanDetail(plan);
            if (string.IsNullOrWhiteSpace(detail)) { try { onDone?.Invoke(false); } catch { } return; }
            // 🔴 2026-08-12：讲解人 = 会话对方随从（私聊 = PartnerHero；群聊 = 通用发言人兜底）。
            // 原实现用 card.SenderName（计划卡片 SenderHeroId=player）→ 讲解消息以玩家名义上屏 → 体验断裂。
            var conv = ConversationOf(card.ConvId);
            string heroId = conv?.Type == ImConversationType.Direct ? conv.PartnerHeroId : "";
            string senderName = "";
            if (conv?.Type == ImConversationType.Direct)
            {
                try
                {
                    senderName = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId)?.Name?.ToString() ?? "";
                }
                catch { }
            }
            if (string.IsNullOrEmpty(senderName))
                // 本地化：LWN_im_npc_companion（玩家可见文本）
                senderName = LWNTextHelper.ResolveText("LWN_im_npc_companion", "Companion");
            // prompt 归口 PromptBuilder（LLM prompt 单一事实源；讲解 = C# 确定性渲染 + 转述纪律 +
            // 二次校验——审查对照生成时同一份 LWN_plan_rules 纪律；原命令供"任务型 vs 保持型"判断）
            string original = FindOriginalCommand(conv, card);
            string prompt = PromptBuilder.BuildPrompt_PlanExplain(senderName, detail, original);
            async void Run()
            {
                string line = null;
                bool foundIssue = false;
                try
                {
                    string raw = await LLMService.Instance.ChatOnceAsync(prompt, 320, 0.7f, disableReasoning: true, timeoutMs: 8000);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // 🔴 2026-08-12 结构化输出 {"line","found_issue"}：重拟按钮显示条件的数据源。
                        // 铁律 2：解析失败 → 整段当台词、found_issue=false（不信任 LLM 结构）
                        try
                        {
                            var parsed = JsonConvert.DeserializeObject<ExplainResult>(LLMService.CleanJson(raw));
                            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Line))
                            {
                                line = DialogueComponent.Sanitize(parsed.Line, senderName);
                                foundIssue = parsed.FoundIssue;
                            }
                        }
                        catch { }
                        if (string.IsNullOrWhiteSpace(line))
                            line = DialogueComponent.Sanitize(raw, senderName);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImCommandFlow] 计划讲解失败: {ex.Message}");
                }
                // 🔴 2026-08-12：LLM 失败/超时/未配置 → 降级 = 计划摘要/陈述（卡片已有人话文本）口述，
                // **不再展开 C# JSON 详情**（用户裁定）。onDone(true) 让按钮正常复位。
                if (string.IsNullOrWhiteSpace(line))
                    line = !string.IsNullOrWhiteSpace(card.Narration)
                        ? card.Narration
                        : (!string.IsNullOrWhiteSpace(card.PlanSummary)
                            ? card.PlanSummary
                            // 本地化：LWN_plan_default_summary（玩家可见文本）
                            : LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?"));
                // 场景内冒泡由主线程 Tick 消费时执行（BubbleHeroId 传参；后台线程禁碰 Agent native 句柄）
                lock (_explainLock)
                    _explainQueue.Add(new ExplainJob
                    {
                        ConvId = card.ConvId,
                        SenderId = heroId,
                        SenderName = senderName,
                        Line = line,
                        FoundIssue = foundIssue,
                        Card = card,
                        BubbleHeroId = heroId,
                        OnDone = onDone,
                    });
            }
            Run();
        }
        private static string RenderStepTargetText(PlanStep s)
        {
            if (s == null || s.Target == null) return "";
            if (s.Target.Type == Newtonsoft.Json.Linq.JTokenType.String)
                return (string)s.Target ?? "";
            if (s.Target.Type == Newtonsoft.Json.Linq.JTokenType.Object && s.Target["query"] != null)
                return (string)s.Target["query"] ?? "";
            return "";
        }
        private static string RenderCondition(Condition c)
        {
            if (c == null || string.IsNullOrEmpty(c.Type)) return "";
            if (c.Type == "and" || c.Type == "or")
            {
                if (c.Conditions == null || c.Conditions.Count == 0) return "";
                return "(" + string.Join(c.Type == "and" ? " & " : " | ",
                    c.Conditions.Select(x => RenderCondition(x)).Where(x => !string.IsNullOrEmpty(x))) + ")";
            }
            return $"{c.Type}({c.A ?? ""},{c.B ?? ""}) {c.Op ?? ""} {c.Value}";
        }
        // ───────────────────────── 计划执行记忆纪律（🔴 §2.1 单向链条，2026-08-10）─────────────────────────
        /// <summary>
        /// 步骤执行完成 → 写执行者记忆（唯一写入点）。记忆只记录「实际发生过的事」：
        /// 计划生成/批准/修改/中止等元数据一律不写（树/网）；步骤完成按执行顺序逐条追加 = 单向链条。
        /// 渲染纯 C#（动作标签表 + 目标文本），不信任 LLM。role = "plan_step" 独立前缀，
        /// 私聊 UI 按 im_user/im_npc 过滤天然隔离；行进 LLM 上下文 → 后续对话 NPC 接得住。
        /// </summary>
        private static void WritePlanStepMemory(PlanExecutor executor, Agent agent, PlanStep step)
        {
            if (executor == null || agent == null || step == null) return;
            try
            {
                string action = PlanActionLabel(step.Action);
                string target = RenderStepTargetText(step);
            // 本地化：plan_step 记忆渲染
                string content = LWNTextHelper.ResolveCompound("LWN_plan_step_memory",
                    "By my lord's order, {ACTION} {TARGET}, done.", ("ACTION", action), ("TARGET", target)).Trim();
                if (string.IsNullOrWhiteSpace(content)) return;
                var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
                if (memory == null) return;
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                memory.AddHistory("plan_step", content, hero?.StringId ?? agent.Name);
                DebugLogger.Log($"[ImCommandFlow] plan_step 记忆写入: {agent.Name}: {content}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] plan_step 记忆写入异常: {ex.Message}");
            }
        }
        /// <summary>卡片是否执行中（中止按钮可见性；Superseded 已修改 = 非执行中）。</summary>
        public static bool IsExecuting(ImMessage msg)
        {
            return msg != null && msg.IsPlanCard
                && !string.IsNullOrEmpty(msg.ExecutorId)
                && msg.ExecutorId != Rejected && msg.ExecutorId != Done && msg.ExecutorId != Superseded;
        }
        /// <summary>执行器回报回 IM：executor 由 AgentBrain 异步 Create，走补挂队列（Tick 轮询）。</summary>
        private static void WireExecutorReports(ImConversation conv, Agent executorAgent, ImMessage card)
        {
            if (executorAgent == null) return;
            var hero = (executorAgent.Character as CharacterObject)?.HeroObject;
            string heroId = hero?.StringId ?? executorAgent.Name;
            _pendingWires.Add(new PendingWire
            {
                Conv = conv,
                Card = card,
                HeroId = heroId,
                StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }
        /// <summary>挂一次回报事件（OnFinished/OnAborted → IM 系统消息 + 卡片了结；原密信 DisplayMessage 保留双渠道）。
        /// 🔴 §2.1：OnStepCompleted 挂接——步骤完成写执行者记忆（plan_step 单向链条）。</summary>
        private static void SubscribeExecutor(PlanExecutor executor, ImConversation conv, ImMessage card)
        {
            executor.OnStepCompleted += (ex, agent, step) => WritePlanStepMemory(ex, agent, step);
            executor.OnFinished += ex =>
            {
                try
                {
                    card.ExecutorId = Done;
                    string report = !string.IsNullOrEmpty(ex?.EndMessage)
                        ? ex.EndMessage
                        // 密令完成回报
                        : LWNTextHelper.ResolveText("LWN_im_cmd_done", "The order has been carried out.");
                    PostSystem(conv, report);
                }
                catch (Exception e) { DebugLogger.Log($"[ImCommandFlow] 完成回报异常: {e.Message}"); }
            };
            executor.OnAborted += (ex, reason) =>
            {
                try
                {
                    card.ExecutorId = Done;
                    PostSystem(conv, string.IsNullOrEmpty(reason)
                        // 密令中止（执行器中止回报）
                        ? LWNTextHelper.ResolveText("LWN_im_cmd_aborted", "The order has been called off.")
                        : reason);
                }
                catch (Exception e) { DebugLogger.Log($"[ImCommandFlow] 中止回报异常: {e.Message}"); }
            };
        }
        /// <summary>下发执行（PlanCommandFlow.ApplyPlan 同款：反应计划 + 意图 target 解析 + SendEventToAgent）。</summary>
        private static void ApplyPlan(ImConversation conv, Agent companion, PlanResponse response, ImMessage card)
        {
            // 反应计划（ReactiveAgent 覆盖默认模板）
            if (response.Reactions != null)
            {
                foreach (var rp in response.Reactions)
                {
                    if (rp == null || string.IsNullOrEmpty(rp.Role)) continue;
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(rp.Role);
                    if (info?.Agent != null)
                        ReactiveAgent.ApplyPlan(info.Agent, rp);
                }
            }
            // 意图 target 解析（角色表注入）
            Agent target = null;
            var intent = response.Intent;
            if (intent != null)
            {
                string t = intent.GetTargetRef(out string _);
                if (!string.IsNullOrEmpty(t) && t != "player" && t != "self")
                {
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(t);
                    target = info?.Agent;
                }
            }
            string planJson = JsonConvert.SerializeObject(response.Plan,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            string intentType = intent?.IntentType;
            AgentAIController.Instance?.SendEventToAgent(companion, "order_execute_plan",
                planJson, intentType, target, card.Content);
            WireExecutorReports(conv, companion, card);
        }
        // ───────────────────────── 辅助 ─────────────────────────
        /// <summary>
        /// 执行者解析（Q5 多人协作）：私聊 = 该随从；队伍频道 = 意图 subjects（"你们/你俩"）逐个解析，
        /// 兜底第一个玩家队伍成员。返回列表（[0] = owner = SendEventToAgent 目标；其余 = 协作执行者，
        /// PlanExecutor.BuildCursors 会为每个 subjects 角色建独立 ActorCursor 一带多并行）。
        /// </summary>
        private static List<Agent> ResolveExecutors(ImConversation conv, PlanResponse response)
        {
            var list = new List<Agent>();
            if (Mission.Current == null) return list;
            if (conv.Type == ImConversationType.Direct)
            {
                var a = FindAgentByHeroId(conv.PartnerHeroId);
                if (a != null) list.Add(a);
                return list;
            }
            // 队伍频道：subjects 优先，兜底第一个玩家队伍成员
            Agent owner = null;
            if (response?.Intent?.Subjects != null)
            {
                foreach (var sub in response.Intent.Subjects)
                {
                    if (string.IsNullOrEmpty(sub)) continue;
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(sub);
                    if (info?.Agent != null && FriendlinessHelper.IsPlayerPartyMember(info.Agent))
                    {
                        if (owner == null) owner = info.Agent;
                        if (!list.Contains(info.Agent)) list.Add(info.Agent);
                    }
                }
            }
            if (owner == null)
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (FriendlinessHelper.IsPlayerPartyMember(a)) { owner = a; break; }
                }
            }
            if (owner != null && !list.Contains(owner)) list.Insert(0, owner);
            return list;
        }
        private static Agent FindAgentByHeroId(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || Mission.Current == null) return null;
            foreach (var a in Mission.Current.Agents)
            {
                var hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero != null && hero.StringId == heroId)
                    return a;
            }
            return null;
        }
        private static ImConversation ConversationOf(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return null;
            if (convId.StartsWith("direct_"))
                return ImChatManager.GetDirectConversation(convId.Substring("direct_".Length));
            return ImChatManager.GetGroupConversation(convId == ImChatStore.ChannelClan
                ? ImConversationType.Clan
                : convId == ImChatStore.ChannelKingdom ? ImConversationType.Kingdom : ImConversationType.Party);
        }
        private static string BuildPersona(ImConversation conv)
        {
            try
            {
                string masterName = Hero.MainHero?.Name?.ToString() ?? "";
                string heroName = "";
                if (conv.Type == ImConversationType.Direct)
                {
                    var hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId);
                    heroName = hero?.Name?.ToString() ?? "";
                }
                if (!string.IsNullOrWhiteSpace(heroName))
                    return $"你是 {heroName}，{masterName} 的随从。说话简短、恭敬、务实，像游戏里的随从。";  // lwn-ignore: A
                return $"你是 {masterName} 的随从。说话简短、恭敬、务实，像游戏里的随从。";  // lwn-ignore: A
            }
            catch { return "你是一名随从。说话简短、务实，像游戏里的随从。"; }  // lwn-ignore: A
        }
        private static void PostSystem(ImConversation conv, string content)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId, "System", content, ImMessageKind.System)
            {
                ConvId = conv.Id,
            });
            ImChatStore.IncUnread(conv.Id);
            ImChatManager.BroadcastMessageArrived(conv);
        }
        /// <summary>NPC 消息入会话（🔴 Q1 澄清轮问句用：带发言人的普通消息，走消息流管道）。
        /// 私聊 = 随从 Hero 名义；群聊 = 无 Hero 语义的通用发言人名义（当前密令只走私聊/队伍频道）。</summary>
        private static void PostNpcMessage(ImConversation conv, string content)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;
            ResolveSpeaker(conv, out string heroId, out string senderName);
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(heroId, senderName, content, ImMessageKind.Text)
            {
                ConvId = conv.Id,
            });
            ImChatStore.IncUnread(conv.Id);
            ImChatManager.BroadcastMessageArrived(conv);
        }
        /// <summary>会话发言人解析（🔴 2026-08-12 抽取，计划消息/占位/讲解/澄清共用）：
        /// 私聊 = 随从 Hero（Id + 名）；群聊 = 无 Hero 语义 → 通用发言人兜底（LWN_im_npc_companion）。</summary>
        private static void ResolveSpeaker(ImConversation conv, out string heroId, out string senderName)
        {
            heroId = conv?.Type == ImConversationType.Direct ? conv.PartnerHeroId : "";
            senderName = "";
            if (conv?.Type == ImConversationType.Direct)
            {
                try
                {
                    senderName = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId)?.Name?.ToString() ?? "";
                }
                catch { }
            }
            if (string.IsNullOrEmpty(senderName))
                // 本地化：通用发言人兜底名
                senderName = LWNTextHelper.ResolveText("LWN_im_npc_companion", "Companion");
        }
        private static void PostHint(ImConversation conv, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            InformationManager.DisplayMessage(new InformationMessage(content));
            PostSystem(conv, content);
        }
    }
}
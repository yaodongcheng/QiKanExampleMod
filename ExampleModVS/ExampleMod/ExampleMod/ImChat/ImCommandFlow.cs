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
            public long StartMs;        // 🔴 §3.3 进度条：墙钟起点（LLM 无真实进度，按已耗时映射阶段）
            public bool IsModify;       // 🔴 Q2：修改管线（新卡片带「修改版」标记）
            public int ModifyCount;     // 🔴 Q2：修改版计数
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

        /// <summary>是否有请求在途（互斥：一次只处理一条命令）。</summary>
        public static bool IsBusy => _pending != null || _resultReady;

        // ───────────────────────── 下达 ─────────────────────────

        /// <summary>玩家在密令模式发送命令文本 → 追加消息 + 计划生成（Mission）/ 行军令（Campaign，Q5b）。
        /// 🔴 Q1：若存在本会话的澄清轮（_pendingClarify），本条消息作为澄清回答并入命令上下文重新生成。</summary>
        public static void RequestCommand(ImConversation conv, string command)
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
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                Hero.MainHero?.Name?.ToString() ?? "You", cmd, ImMessageKind.Text)
            {
                ConvId = conv.Id,
            });

            if (Mission.Current == null)
            {
                // Campaign 大地图：行军令（规则解析，零 LLM；私聊有 party 的 Hero）
                _pendingClarify = null;   // 行军令无澄清轮
                ImMarchOrder.RequestMarchOrder(conv, cmd);
                return;
            }

            _pending = new PendingRequest
            {
                Conv = conv,
                Command = cmd,
                StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            // 🔴 §3.3：生成中占位行（阶段文案 + 模拟进度条）——LLM 5~15s 零反馈的焦虑缓冲
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
                    PlanCommandFlow.IntentTableForPrompt(), PlanCommandFlow.GrammarForPrompt());
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
            // 🔴 §3.3 生成中进度推进（LLM 无真实进度，游戏加载条同款模拟；阶段文案掩盖模拟单调）
            TickGeneratingProgress();

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

                // 🔴 §3.1 计划陈述：narration = 卡片上方的 NPC 消息（当事人自述，非上帝视角；null 兜底 = 摘要）
                string narration = !string.IsNullOrWhiteSpace(response.Narration) ? response.Narration : null;
                if (narration != null)
                    PostNpcMessage(conv, narration);

                // 计划卡片
                string summary = !string.IsNullOrEmpty(response.Plan.Summary) ? response.Plan.Summary
                    // 计划摘要缺省文案
                    : LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?");
                var card = new ImMessage(ImChatManager.PlayerId,
                    Hero.MainHero?.Name?.ToString() ?? "You", summary, ImMessageKind.PlanCard)
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
        /// 额度：修改 ≤ MaxModifyCount（2 次，成功产出才消耗——复用 Replan 语义，防无限修改刷 LLM）。
        /// 覆盖两态：批准前（卡片待批）与执行中（先中止当前执行，CancelByPlayer 不触发 Replan 自动重入）。
        /// </summary>
        public static void RequestModify(ImMessage msg, string text)
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
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                Hero.MainHero?.Name?.ToString() ?? "You", text.Trim(), ImMessageKind.Text)
            {
                ConvId = conv.Id,
            });
            // 本地化：修改重拟提示
            PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_pending", "The companion is working out a revised plan."));
            if (Mission.Current == null)
            {
                // 行军令无修改（Campaign 侧规则解析，零 LLM）
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_need_mission", "You can only revise a plan while in the field."));
                return;
            }

            _pending = new PendingRequest
            {
                Conv = conv,
                Command = cmd,
                StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
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

        // ───────────────────────── 生成中占位行（🔴 §3.3，2026-08-10）─────────────────────────

        /// <summary>占位行：消息流内灰色卡片（阶段文案 + 进度条；面板重开按已耗时重映射，纯展示值）。</summary>
        private static void AppendGenerating(ImConversation conv)
        {
            if (conv == null) return;
            // 本地化：生成中阶段文案（观察地形）
            string stage = LWNTextHelper.ResolveText("LWN_im_gen_observe", "Surveying the area...");
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId, "System", "", ImMessageKind.Generating)
            {
                ConvId = conv.Id,
                Progress = 0f,
                GenerateText = stage,
            });
            ImChatManager.BroadcastMessageArrived(conv);
        }

        /// <summary>移除会话最后一条生成中占位（卡片上屏/失败时替换）。</summary>
        private static void RemoveGenerating(ImConversation conv)
        {
            if (conv == null) return;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                if (msgs[i].IsGenerating)
                {
                    msgs.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>阶段化模拟进度（§3.3）：LLM 无真实进度，按已耗时映射阶段文案 + 进度。
        /// 0→10% 快照构建（秒级）；10→85% LLM 生成（每 1.5s +2~5% 抖动由整体线性模拟近似）；85→90 校验；封顶 90 等卡片。</summary>
        private static void TickGeneratingProgress()
        {
            if (_pending == null || Mission.Current == null) return;
            try
            {
                float elapsed = (float)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _pending.StartMs) / 1000f;
                float progress;
                string stage;
                if (elapsed < 1.2f)
                {
                    progress = elapsed / 1.2f * 10f;
            // 本地化：生成中阶段文案（观察地形）
                    stage = LWNTextHelper.ResolveText("LWN_im_gen_observe", "Surveying the area...");
                }
                else if (elapsed < 7f)
                {
                    progress = 10f + (elapsed - 1.2f) / 5.8f * 75f;
            // 本地化：生成中阶段文案（推演步骤）
                    stage = LWNTextHelper.ResolveText("LWN_im_gen_plan", "Working out the steps...");
                }
                else
                {
                    progress = 90f;   // 封顶：防长时间卡 99% 假象；卡片上屏瞬间 100%
            // 本地化：生成中阶段文案（核对细节）
                    stage = LWNTextHelper.ResolveText("LWN_im_gen_check", "Checking the details...");
                }
                // 写回 store（面板重开按已耗时重映射 = 读消息时重新计算，此处同步最新值供 UI 显示）
                var msgs = ImChatStore.GetGroupMessages(_pending.Conv.Id);
                for (int i = msgs.Count - 1; i >= 0; i--)
                {
                    var m = msgs[i];
                    if (m.IsGenerating)
                    {
                        m.Progress = progress;
                        m.GenerateText = stage;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] 进度推进异常: {ex.Message}");
            }
        }

        // ───────────────────────── 卡片详情（🔴 §3.2，C# 确定性渲染，不信任 LLM 文案）─────────────────────────

            // 本地化：动作名标签表（plan_step 记忆/详情渲染）
        /// <summary>动作名 → 本地化标签（LWN_plan_action_*；渲染的是实际会被执行的逻辑——PlanExecutor 同一份 JSON）。</summary>
        private static string PlanActionLabel(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            // 本地化：动作名标签表（plan_step 记忆/详情渲染）
            return LWNTextHelper.ResolveText("LWN_plan_action_" + action,
                action switch
                {
                    "move_to" => "move to", "follow" => "follow", "stop_following" => "stop following",
                    "order_attack" => "attack", "knockout" => "knock out", "lead" => "lead the way",
                    "face" => "face", "look_at" => "look at", "say_to" => "speak to", "wait" => "wait",
                    "emote" => "gesture", "make_noise" => "shout", "signal_player" => "signal",
                    "steal_attempt" => "steal from", "give_item" => "hand over", "give_gold" => "give gold",
                    "deliver_item" => "deliver", "shadow" => "shadow", "negotiate" => "negotiate",
                    "duel" => "duel", "end_plan" => "finish", _ => action,
                });
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
            string heroId = conv.Type == ImConversationType.Direct ? conv.PartnerHeroId : "";
            string senderName = "";
            if (conv.Type == ImConversationType.Direct)
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
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(heroId, senderName, content, ImMessageKind.Text)
            {
                ConvId = conv.Id,
            });
            ImChatStore.IncUnread(conv.Id);
            ImChatManager.BroadcastMessageArrived(conv);
        }

        private static void PostHint(ImConversation conv, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            InformationManager.DisplayMessage(new InformationMessage(content));
            PostSystem(conv, content);
        }
    }
}

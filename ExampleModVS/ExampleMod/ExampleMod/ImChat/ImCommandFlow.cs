using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private class PendingRequest
        {
            public ImConversation Conv;
            public string Command;
        }

        // LLM 结果回主线程消费（PlanCommandFlow 同款 _pendingResult/_resultReady 模式）
        private static PendingRequest _pending;
        private static ImConversation _lastConv;   // 结果归属会话（FinishWith 清 _pending 后仍可定位）
        private static bool _resultReady;
        private static PlanResponse _pendingResult;

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

        /// <summary>玩家在密令模式发送命令文本 → 追加消息 + 计划生成（Mission）/ 行军令（Campaign，Q5b）。</summary>
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
            if (Mission.Current != null && PlanCommandFlow.IsActive)
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

            // 命令文本入会话（store；不写 NPC 记忆——密令是 Mission 级瞬态）
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                Hero.MainHero?.Name?.ToString() ?? "You", command, ImMessageKind.Text)
            {
                ConvId = conv.Id,
            });

            if (Mission.Current == null)
            {
                // Campaign 大地图：行军令（规则解析，零 LLM；私聊有 party 的 Hero）
                ImMarchOrder.RequestMarchOrder(conv, command.Trim());
                return;
            }

            _pending = new PendingRequest { Conv = conv, Command = command.Trim() };
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

                if (response == null)
                {
                    // 密令失败：计划生成失败
                    PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
                    return;
                }

                // 澄清轮（v1 简化：不进入循环问答，直接把问题回给玩家，玩家重发命令）
                if (response.Questions != null && response.Questions.Count > 0)
                {
                    var q = response.Questions[0];
                    // 澄清轮缺省问句
                    string qText = q?.Q ?? LWNTextHelper.ResolveText("LWN_plan_clarify_default", "What do you mean exactly?");
                    if (q != null && q.Options != null && q.Options.Count > 0)
                        qText += "\n" + string.Join(" / ", q.Options.Select(o => $"「{o}」"));  // lwn-ignore: A
                    PostSystem(conv, qText);
                    return;
                }

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
            if (string.IsNullOrEmpty(msg.ExecutorId) || msg.ExecutorId == Rejected || msg.ExecutorId == Done) return;
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
        }

        /// <summary>卡片是否执行中（中止按钮可见性）。</summary>
        public static bool IsExecuting(ImMessage msg)
        {
            return msg != null && msg.IsPlanCard
                && !string.IsNullOrEmpty(msg.ExecutorId)
                && msg.ExecutorId != Rejected && msg.ExecutorId != Done;
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

        /// <summary>挂一次回报事件（OnFinished/OnAborted → IM 系统消息 + 卡片了结；原密信 DisplayMessage 保留双渠道）。</summary>
        private static void SubscribeExecutor(PlanExecutor executor, ImConversation conv, ImMessage card)
        {
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

        private static void PostHint(ImConversation conv, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            InformationManager.DisplayMessage(new InformationMessage(content));
            PostSystem(conv, content);
        }
    }
}

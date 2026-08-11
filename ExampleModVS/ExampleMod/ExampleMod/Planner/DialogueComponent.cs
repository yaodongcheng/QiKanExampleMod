using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 一次台词生成结果（§5.6 DialogueComponent 统一管线输出）。
    /// FromLlm = LLM 实时生成（写记忆）；false = 模板降级（不写记忆——模板是重复无个性内容，
    /// 写进记忆会稀释真实事件记忆、且污染续话轮询，2026-08-10 用户裁定，适用全部对话入口）。
    /// </summary>
    public class DialogueLine
    {
        public string Reply;
        public bool FromLlm;
        // 🔴 2026-08-10（§5.6）：动作决策（JSON 通道，与 IM 群聊同构）——说话带动作，空间由 ActionHandler 裁决
        public string ActionCode;
        public string ActionTarget;
        public string ActionLevel;
    }

    /// <summary>
    /// 统一对话实例（2026-08-11 架构收敛：一段对话的全部通用状态，两种驱动共用）。
    /// 驱动方式：执行器驱动（say_to：SayInlineState 持有 session 每帧调 OnTick）/
    ///           续话器驱动（social：注册表持有 session，TickContinuations 每帧调 OnTick）。
    /// </summary>
    public class DialogueSession
    {
        public Agent Initiator;         // 发起方（续话者）
        public Agent Target;            // 应答方
        public string Topic;            // 话题（"threat"/"praise"/计划 topic…）
        public List<string> Outline;    // 走向段（say_to 用；null = 自由对话）
        public int OutlineIndex;        // 当前走向段下标
        public int Round;               // 已续话轮数
        public float LastResponseAt;    // 目标最后回应时刻（超时判定；Mission 时间）
        public float LastResponseCheck; // 上次轮询游标（增量判断"对方回应了"）
        public IDialogueSlot Slot;      // 生命周期策略（差异化实现）
    }

    /// <summary>
    /// 对话生命周期插槽（2026-08-11 架构收敛：真实契约 = 三钩子，驱动无关，无伪实现）。
    /// 推进逻辑全在 OnTick 内（say_to 的 ChatPhase 时序 / social 的轮询跟进），
    /// 驱动者（执行器/续话器）只是每帧调 session.Slot.OnTick。
    /// 差异化矩阵：
    ///   SayToSlot  = 计划对话（走向推进 + 步骤收尾；BC-006 行为等价平移）；执行器驱动
    ///   SocialSlot = 威胁/NPC 闲聊跟进（自由跟进 2~3 轮收敛，60s 无回应超时；5.5 attacker 跟进缺）；续话器驱动
    /// </summary>
    public interface IDialogueSlot
    {
        /// <summary>对话启动钩子（注册时调用；say_to：初始化；social：轮次归零）。</summary>
        void OnStart(DialogueSession s);

        /// <summary>每帧推进（驱动钩子）：全部生命周期逻辑（续话/中止/收尾）在槽内自决。</summary>
        void OnTick(DialogueSession s, float dt);

        /// <summary>对话结束钩子（session 移除时调用；say_to：步骤收尾；social：无事——恩怨已写记忆）。</summary>
        void OnEnd(DialogueSession s);
    }

    /// <summary>
    /// 社交续话插槽（威胁/NPC 闲聊跟进）：发起方 A 说话 → 应答方 B respond → **A 轮询 B 的记忆跟进**
    /// ——对话链不再单轮即断（5.5 attacker 跟进缺）。自由跟进（无走向段），2~3 轮收敛，60s 无回应超时。
    /// 驱动：续话器注册表（DialogueComponent.TickContinuations 每帧调 OnTick）。
    /// </summary>
    public class SocialSlot : IDialogueSlot
    {
        public const int MaxSocialRounds = 3;      // 轮数上限（闲聊/对峙收敛，不无限聊）
        public const float NoResponseTimeoutS = 60f; // 对方不理超时

        public void OnStart(DialogueSession s)
        {
            s.Round = 0;
            s.LastResponseCheck = Mission.Current != null ? Mission.Current.CurrentTime : 0f;
        }

        /// <summary>每帧推进：终止检查（轮数/超时/对象失效）→ 轮询对方记忆 → 有回应则续话（生成 → 播放 → 广播）。</summary>
        public void OnTick(DialogueSession s, float dt)
        {
            try
            {
                if (s == null || s.Initiator == null || s.Target == null || !s.Initiator.IsActive())
                {
                    DialogueComponent.EndSession(s);
                    return;
                }
                // ③ 中止：轮数上限 / 对方不理超时
                if (s.Round >= MaxSocialRounds)
                {
                    DialogueComponent.EndSession(s);
                    return;
                }
                if (Mission.Current != null && s.LastResponseAt > 0f
                    && Mission.Current.CurrentTime - s.LastResponseAt > NoResponseTimeoutS)
                {
                    DialogueComponent.EndSession(s);
                    return;
                }
                // ② 续话：目标回应了？（增量轮询目标记忆：respond 成功后写 assistant）
                var targetMem = AllNpcMemoryManager.GetMemoryForAgent(s.Target);
                if (targetMem == null || targetMem.RecentHistory.Count <= s.LastResponseCheck) return;
                s.Round++;
                s.LastResponseCheck = targetMem.RecentHistory.Count;
                s.LastResponseAt = Mission.Current != null ? Mission.Current.CurrentTime : 0f;

                // 续话内容：发起方身份 + 话题延续 + 对方刚说（自由跟进）
                string initiatorName = s.Initiator.Name?.ToString() ?? "随从";
                string targetName = s.Target.Name?.ToString() ?? "对方";
                string identity = string.Format(
                    DialogueComponent.ResolvePrompt("LWN_plan_respond_identity_template", "你是{0}。{1}。"),
                    initiatorName, DialogueComponent.ResolvePrompt("LWN_trait_companion", "随从"));
                var initMem = AllNpcMemoryManager.GetMemoryForAgent(s.Initiator);
                string lastLine = initMem != null ? ReactiveAgent.GetLastLineWith(initMem, ReactiveAgent.GetAgentId(s.Target)) : "";
                _ = DialogueComponent.GenerateLine(
                    Settings.Instance?.WorldDescription ?? "", identity, "",
                    string.IsNullOrEmpty(s.Topic) ? "闲聊" : s.Topic, "",
                    "", targetName,
                    initMem != null ? PromptBuilder.GetPrompt_RespondContext(initMem, ReactiveAgent.GetAgentId(s.Target)) : "",
                    lastLine,
                    "LWN_plan_respond_rule",
                    "【要求】用一句话口语化对对方说（10-40 字），顺着对方的话接，像随口闲聊/对峙回话，直接说台词本身——不要引号、不要解释、不要动作描写。",
                    null, maxTokens: 80, timeoutMs: 4000)
                    .ContinueWith(t =>
                    {
                        string result = t.Status == TaskStatus.RanToCompletion && t.Result != null && t.Result.FromLlm
                            ? DialogueComponent.Sanitize(t.Result.Reply, initiatorName)
                            : null;
                        if (string.IsNullOrWhiteSpace(result) || s.Initiator == null || !s.Initiator.IsActive()) return;
                        // 续话 = 发起方再说话（播放 + 广播——对方 respond 新一轮）
                        AgentHudMissionView.AgentSay(s.Initiator, result);
                        DialogueComponent.HandleDialogue(s.Initiator, s.Target, s.Topic, result, null);
                        DebugLogger.Log($"[DialogueComponent] 社交续话（第 {s.Round} 轮）: {s.Initiator.Name}: {result}");
                    });
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueComponent] SocialSlot 异常: {ex.Message}");
            }
        }

        public void OnEnd(DialogueSession s)
        {
            // 无事：恩怨/关系效果已在过程中写记忆（THREATEN/PRAISE/SPREAD_RUMOR 的 Execute）
        }
    }

    /// <summary>
    /// 计划对话插槽（say_to 对话模式，BC-006 v3 → 2026-08-11 架构收敛）：ChatPhase 状态机
    /// 从 SayInlineState **行为等价平移**（相同的时序/延迟/轮询/终止），推进逻辑全在 OnTick 内。
    /// 驱动 = 执行器（SayInlineState 持有 Session 每帧调 Slot.OnTick；不注册续话器）。
    /// 特殊点：① 开场白（预写 text / LLM 生成）；② 续话 = 走向段推进（outline）；③ 中止 = 走向走完；
    /// ④ 结束 = 步骤完成（执行器推进）。时序字段（阶段/延迟/轮询游标）为 say_to 专属，保留槽内。
    /// </summary>
    public class SayToSlot : IDialogueSlot
    {
        private const float BroadcastDelayS = 2.5f;   // 台词播放后到广播的延迟（对方"听完"再响应）
        private const float ReplyDelayS = 2.5f;       // 对方回应后到续话的延迟（等对方冒泡播完）
        // 🔴 预生成流水线（2026-08-11 用户裁定：请求层与播放层剥离）：
        // LLM 生成耗时（timeoutMs 2000）隐藏在 ReplyDelayS（2500ms）播放等待里——回应内容写入记忆那一刻
        // 下一段上下文已确定，立即发请求；播放时机仍由播放层（ReplyDelay）自决，节奏不变。
        private enum ChatPhase { None, Opening, PendingBroadcast, WaitReply, ReplyDelay, Done }

        private readonly Agent _agent;
        private readonly Agent _target;
        private readonly PlanStep _step;
        private readonly PlanExecutor _executor;
        private readonly SingNpcMemorySystem _memory;   // 目标的三层记忆（对话双方共享；轮询尾部判断"对方回应了"）
        private ChatPhase _phase = ChatPhase.None;
        private bool _said;
        private int _lastSeenHistoryCount;
        private string _pendingLine;        // 预生成结果（WaitReply 阶段发出；LLM 成功/失败回调都必到——2s 预算 + 走向模板兜底）
        private int _pendingIndex = -1;     // 预生成的目标走向段（-1 = 没有下一段，对方回应后收尾）
        private float _noReplyTimer;        // 对方不理兜底（步骤超时豁免后防挂死，见 IsUnboundedStep）
        private float _broadcastTimer;
        private float _replyDelayTimer;
        private string _lastPlayedLine = "";
        public bool Finished { get; private set; }

        /// <summary>统一对话实例（SayInlineState 持有，每帧调 Session.Slot.OnTick）。</summary>
        public DialogueSession Session { get; private set; }

        public SayToSlot(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _step = step;
            _agent = cursor.Agent;
            _target = null;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolveAgent(refName, cursor.Agent, out _target))
                return;
            // 对话模式：outline 2+ 段 → 多轮对话；需要目标记忆驱动（轮询对方回应）
            var outline = step.IsChatMode ? step.OutlineSegments : null;
            if (outline != null)
            {
                _memory = AllNpcMemoryManager.GetMemoryForAgent(_target);
                if (_memory == null) return;
                _phase = ChatPhase.Opening;
                _lastSeenHistoryCount = _memory.RecentHistory.Count;
            }
            // 统一对话实例（走向段进 session；槽自引用）
            Session = new DialogueSession
            {
                Initiator = _agent,
                Target = _target,
                Topic = _step.Topic ?? _executor?.Summary,
                Outline = outline,
                Slot = this,
            };
        }

        public void OnStart(DialogueSession s) { }

        /// <summary>驱动钩子（SayInlineState 每帧调）：ChatPhase 状态机（BC-006 行为等价平移）。</summary>
        public void OnTick(DialogueSession s, float dt)
        {
            if (Finished || _phase == ChatPhase.None || s == null) return;
            switch (_phase)
            {
                case ChatPhase.Opening:
                    // 开场白：预写 text 直接播；否则 LLM 生成（异步，结果进 _pendingLine）
                    if (!_said)
                    {
                        _said = true;
                        if (!string.IsNullOrEmpty(_step.TextOrContent))
                        {
                            PlayLine(_step.TextOrContent);
                            _phase = ChatPhase.PendingBroadcast;
                            return;
                        }
                        // 开场 = outline[0]（寒暄段），不用占位符（修复 "（开场）" 硬编码 + outline[0] 浪费）
                        GenerateNextLine(0, s.Outline[0]);
                        return;
                    }
                    if (_pendingLine != null)
                    {
                        string line = _pendingLine;
                        _pendingLine = null;
                        PlayLine(line);
                        _phase = ChatPhase.PendingBroadcast;
                    }
                    break;

                case ChatPhase.PendingBroadcast:
                    // 台词冒泡播完后再广播（请求间隔 ≥3s，防网关隐性限速——日志实测密集请求全超时）
                    _broadcastTimer += dt;
                    if (_broadcastTimer >= BroadcastDelayS)
                    {
                        _broadcastTimer = 0f;
                        BroadcastSpokenTo(_lastPlayedLine, s.OutlineIndex < s.Outline.Count ? s.Outline[s.OutlineIndex] : null);
                        _phase = ChatPhase.WaitReply;
                        _lastSeenHistoryCount = _memory.RecentHistory.Count;
                        _noReplyTimer = 0f;   // 新一轮等待从零计（60s 兜底按轮次算，不跨轮累计）
                    }
                    break;

                case ChatPhase.WaitReply:
                    // 兜底：对方始终不理（respond 模板不写记忆/无人响应）→ 收尾（步骤超时豁免后唯一终止保证）
                    _noReplyTimer += dt;
                    if (_noReplyTimer >= SocialSlot.NoResponseTimeoutS)
                    {
                        _phase = ChatPhase.Done;
                        Finished = true;
                        break;
                    }
                    // 轮询目标记忆：respond（成功/降级都写入 history）→ 对方回应了
                    if (_memory.RecentHistory.Count > _lastSeenHistoryCount)
                    {
                        _lastSeenHistoryCount = _memory.RecentHistory.Count;
                        // 🔴 预生成：对方回应内容已写入记忆 → 下一段上下文确定，立即请求 LLM
                        //（生成耗时隐藏在 ReplyDelay 播放等待里；不再等 2.5s 才开始生成）
                        int next = s.OutlineIndex + 1;
                        if (next < s.Outline.Count)
                        {
                            _pendingIndex = next;
                            GenerateNextLine(next, s.Outline[next]);
                        }
                        // next >= Count → _pendingIndex 保持 -1：这是最后一段，回应后收尾
                        _phase = ChatPhase.ReplyDelay;
                    }
                    break;

                case ChatPhase.ReplyDelay:
                    // 等对方冒泡播完再续话（同样防密集请求；真人对话节奏）——播放时机由播放层自决
                    _replyDelayTimer += dt;
                    if (_replyDelayTimer >= ReplyDelayS)
                    {
                        _replyDelayTimer = 0f;
                        // 先查无下一段（末段已回应，未发预生成 → _pendingLine 恒 null，不能按"未回包"处理）
                        if (_pendingIndex < 0)
                        {
                            _phase = ChatPhase.Done;
                            Finished = true;
                            break;
                        }
                        if (_pendingLine == null) break;   // 预生成未回包（保底 2s 预算 + 走向模板兜底，必到）：再等
                        s.OutlineIndex = _pendingIndex;
                        _pendingIndex = -1;
                        _phase = ChatPhase.PendingBroadcast;
                        string line = _pendingLine;
                        _pendingLine = null;
                        PlayLine(line);
                    }
                    break;
            }
        }

        /// <summary>④ 结束决策：步骤收尾（与单句模式一致：仅 Finished，执行器 CompleteStep 推进）。</summary>
        public void OnEnd(DialogueSession s) { }

        /// <summary>播放随从台词（face + 冒泡）；广播延迟到 PendingBroadcast 阶段（间隔控制）。</summary>
        private void PlayLine(string line)
        {
            try
            {
                if (_target != null && _target.IsActive())
                    AgentControlHelper.FaceToActor(_agent, _target);
                if (!string.IsNullOrEmpty(line))
                    AgentHudMissionView.AgentSay(_agent, line);
                _lastPlayedLine = line;
                DebugLogger.Log($"[PlanExecutor] 对话模式 {_agent.Name} → {_target.Name}（第 {Session?.OutlineIndex + 1}/{Session?.Outline?.Count ?? 0} 段）: {line}");
            }
            catch { }
        }

        /// <summary>广播 spoken_to（ask:follow 照旧；统一入口 HandleDialogue 含旁观者 seen_speaking）。</summary>
        private void BroadcastSpokenTo(string line, string outlineStep)
        {
            // ask: follow → 广播 asked_to_follow(target)（ReactiveAgent"跟不跟"演算的触发词）
            if (!string.IsNullOrEmpty(_step.Ask) && _step.Ask == "follow" && _target != null)
                AgentAIController.Instance?.SendEventToAgent(_target, "asked_to_follow", _agent);
            // 通用 spoken_to 广播 + 旁观者 seen_speaking（统一入口；Args[2]=主题（对话模式用 topic，否则计划摘要））
            if (_target != null)
            {
                string topic = !string.IsNullOrEmpty(_step.Topic) ? _step.Topic : _executor?.Summary;
                DialogueComponent.HandleDialogue(_agent, _target, topic, line, outlineStep);
            }
        }

        /// <summary>对话模式：异步生成随从下一句（话题 + 走向段 + 双方历史；LLM 失败 → 走向模板兜底）。</summary>
        private void GenerateNextLine(int index, string outlineStep)
        {
            _pendingLine = null;
            ReactiveAgent.GenerateCompanionLine(_agent, _target, _memory, _step.Topic ?? _executor?.Summary,
                outlineStep, index, Session?.Outline?.Count ?? 0, result => _pendingLine = result);
        }
    }

    /// <summary>
    /// 对话流统一组件（im-command-action-upgrade.md §5.6，2026-08-10）：
    /// 统一**台词生成管线**——respond（场景应答）/ 随从续话（say_to 对话模式）/ 事件台词全部走
    /// <see cref="GenerateLine"/>，共享：prompt 骨架（世界观→身份→态度→主题→走向→对方→记忆→刚说→要求）
    /// + LLM 通道 + **动作决策（JSON，与 IM 群聊同构）** + 记忆纪律（LLM 才写记忆）。
    ///
    /// 统一边界（用户裁定 2026-08-10）：
    /// - 统一的是「说话」（台词+动作+记忆+播放出口 AgentSay → nearby 投影）；
    /// - 不统一的是「行为决策回流」（follow/refuse → plan_decision → 执行器 on_event 控制流，永远走 AIEvent）；
    /// - 编排器（SayInlineState 对话状态机）保留不动——编排与生成分离正是统一的意义。
    /// </summary>
    public static class DialogueComponent
    {
        /// <summary>
        /// 统一台词生成入口（fire-and-forget 前调用）：拼 prompt → LLM 生成 → 解析。
        /// needJson（actionSpace 非空）→ JSON 通道（npc_reply/npc_action/action_target/action_level），
        /// 解析失败 → 原文当台词、动作 NONE（降级链，与 IM 同构）。
        /// </summary>
        /// <param name="ruleKey">台词要求段（XML 单一事实源；respond 用 JSON 版纪律，续话用纯文本版）</param>
        /// <param name="ruleFallback">台词要求段兜底（缺 key 不崩）</param>
        /// <param name="actionSpace">动作空间段（ActionHandler.GetActionSpacePrompt 按空间裁剪；null = 纯文本通道）</param>
        public static async Task<DialogueLine> GenerateLine(
            string world, string identity, string attitude, string topic, string roundText,
            string outlineSection, string otherName, string history, string lastLine,
            string ruleKey, string ruleFallback, string actionSpace = null,
            int maxTokens = 220, int timeoutMs = 8000)
        {
            var line = new DialogueLine();
            try
            {
                var sb = new StringBuilder();
                // 世界观段（XML LWN_plan_section_world）
                sb.AppendLine(ResolvePrompt("LWN_plan_section_world", "【世界观】") + world);
                // 身份段（XML LWN_plan_respond_section_identity）
                sb.AppendLine(ResolvePrompt("LWN_plan_respond_section_identity", "【你的身份】") + identity);
                if (!string.IsNullOrEmpty(attitude))
                    sb.AppendLine(attitude);
                // 主题段（XML LWN_plan_respond_section_topic）+ 轮次
                sb.AppendLine(ResolvePrompt("LWN_plan_respond_section_topic", "【对话主题】") + topic + roundText);
                // 走向段（对话模式专用；XML LWN_plan_respond_section_outline）
                if (!string.IsNullOrEmpty(outlineSection))
                    sb.AppendLine(outlineSection);
                // 对方段（XML LWN_plan_respond_section_other）
                sb.AppendLine(ResolvePrompt("LWN_plan_respond_section_other", "【对方】") + otherName);
                // 记忆裁剪段（与对方相关的近期对话；PromptBuilder 既有）
                sb.AppendLine(history);
                // 对方刚说（记忆过滤后最后一句）
                sb.AppendLine(ResolvePrompt("LWN_plan_respond_section_last", "【对方刚说】") + lastLine);
                // 动作空间（§5.6：respond 带动作，与 IM 群聊同构；LLM 只看到当前空间合法动作）
                if (!string.IsNullOrEmpty(actionSpace))
                {
                    sb.AppendLine(actionSpace);
                    sb.AppendLine();
                }
                // 台词要求段（XML 单一事实源）
                sb.AppendLine(ResolvePrompt(ruleKey, ruleFallback));

                bool needJson = !string.IsNullOrEmpty(actionSpace);
                string raw = await LLMService.Instance.ChatOnceAsync(
                    sb.ToString(), maxTokens, 0.7f, disableReasoning: true, timeoutMs: timeoutMs, needJson: needJson);
                if (string.IsNullOrWhiteSpace(raw))
                    return line;   // FromLlm=false：调用方走模板降级

                if (needJson)
                {
                    // JSON 通道：解析 LLMResponse_Casual（复用 IM 群聊同款；失败 → 原文当台词、动作 NONE）
                    try
                    {
                        var resp = JsonConvert.DeserializeObject<LLMResponse_Casual>(LLMService.CleanJson(raw));
                        if (resp != null && !string.IsNullOrWhiteSpace(resp.NpcReply))
                        {
                            line.Reply = resp.NpcReply;
                            line.ActionCode = resp.NpcAction;
                            line.ActionTarget = resp.ActionTarget;
                            line.ActionLevel = resp.ActionLevel;
                            line.FromLlm = true;
                            return line;
                        }
                    }
                    catch { }
                    line.Reply = raw;
                    line.FromLlm = true;
                    return line;
                }

                line.Reply = raw;
                line.FromLlm = true;
                return line;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueComponent] 台词生成异常: {ex.Message}");
                return line;   // FromLlm=false：调用方走模板降级（铁律 1/2）
            }
        }

        /// <summary>对话台词 prompt 段读取（缺 key 返回兜底，不崩）。</summary>
        public static string ResolvePrompt(string key, string fallback)
        {
            string s = LWNTextHelper.ResolvePrompt(key);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        /// <summary>
        /// 🔴 统一对话发起入口（§5.6 HandleDialogue）：任何人向目标发起的场景对话都走这里——
        /// say_to 步骤、说话类对抗（威胁/当众夸/当众造谣）、附近频道玩家喊话。
        /// 内部：广播 spoken_to(requester, line, topic, outlineStep) + 旁观者插话（15m 内按距离加权概率）。
        /// 决策回流（行为类反应 → plan_decision）仍由 ReactiveAgent 演算走 AIEvent——本入口只管"说话"。
        /// </summary>
        public static void HandleDialogue(Agent requester, Agent target, string topic, string line, string outlineStep = null)
        {
            if (requester == null || target == null || Mission.Current == null) return;
            if (string.IsNullOrWhiteSpace(line)) return;
            try
            {
                AgentAIController.Instance?.SendEventToAgent(target, "spoken_to", requester, line, topic, outlineStep);
                // 旁观者插话（🔴 2026-08-11 用户裁定）：15m 内所有旁观者**独立按距离加权概率中签**广播
                // seen_speaking——距离越近概率越高（0m≈100%、15m≈0%，线性 + 5% 底限）：近的几乎必有机会
                // 插话、远的偶尔（围观现实感：离得近才听清才想搭话）。中签后 ReactiveAgent 演算才决定
                // 是否真的开口（respond/围观/无视）——多人同时中签同时开口概率低（双重衰减），可接受。
                // 位置基准 = 对话双方中点（requester 与 target 面对面时旁观者距谁近都算"在场"）
                Vec3 mid = (requester.Position + target.Position) * 0.5f;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || a == requester || a == target || !a.IsActive()) continue;
                    float dist = a.Position.Distance(mid);
                    if (dist > BystanderRadius) continue;
                    float chance = Math.Max(1f - dist / BystanderRadius, 0.05f);
                    if (MBRandom.RandomFloat < chance)
                        AgentAIController.Instance?.SendEventToAgent(a, "seen_speaking", requester, target, line);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueComponent] HandleDialogue 广播异常: {ex.Message}");
            }
        }

        /// <summary>旁观者插话半径（米，2026-08-11 用户裁定：15m，概率按距离线性衰减）。</summary>
        public const float BystanderRadius = 15f;

        // ═══════════════════════════════════════════════════════════
        // 续话器（2026-08-11 架构收敛）：活跃对话 session 注册表 + Tick 调度
        // 驱动续话器型 session（SocialSlot）：每帧调 Slot.OnTick，推进逻辑在槽内自决。
        // say_to（SayToSlot）由执行器驱动（不注册）；玩家发起无 session（PlayerLed 概念已删除）。
        // ═══════════════════════════════════════════════════════════

        private static readonly List<DialogueSession> _active = new List<DialogueSession>();
        private static readonly object _lock = new object();

        /// <summary>注册一个续话器驱动的对话 session（威胁/NPC 闲聊发起方视角；say_to 不注册——执行器驱动）。</summary>
        public static void RegisterSession(Agent initiator, Agent target, string topic, IDialogueSlot slot)
        {
            if (initiator == null || target == null || slot == null) return;
            if (Mission.Current == null) return;
            // 同对（发起方+应答方）已有活跃对话 → 不重复注册（防连发叠叠乐）
            lock (_lock)
            {
                foreach (var s in _active)
                {
                    if (s.Initiator == initiator && s.Target == target) return;
                }
                var session = new DialogueSession
                {
                    Initiator = initiator,
                    Target = target,
                    Topic = topic,
                    Slot = slot,
                };
                _active.Add(session);
                try { slot.OnStart(session); } catch { }
            }
            DebugLogger.Log($"[DialogueComponent] 注册对话 session: {initiator.Name} → {target.Name}（{topic}）");
        }

        /// <summary>主线程每帧驱动（AgentAIController.OnMissionTick 挂接）：活跃 session 的 Slot.OnTick（推进逻辑槽内自决）。</summary>
        public static void TickContinuations(float dt)
        {
            List<DialogueSession> pending = null;
            lock (_lock)
            {
                if (_active.Count == 0) return;
                pending = new List<DialogueSession>(_active);
            }
            foreach (var s in pending)
            {
                try
                {
                    if (Mission.Current == null || s.Initiator == null || !s.Initiator.IsActive())
                    {
                        EndSession(s);
                        continue;
                    }
                    s.Slot.OnTick(s, dt);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[DialogueComponent] 续话调度异常: {ex.Message}");
                }
            }
        }

        /// <summary>结束对话 session（槽内自决终止时调用；移除 + OnEnd 收尾）。</summary>
        public static void EndSession(DialogueSession s)
        {
            if (s == null) return;
            lock (_lock)
            {
                if (!_active.Remove(s)) return;
            }
            try { s.Slot.OnEnd(s); } catch { }
            DebugLogger.Log($"[DialogueComponent] 对话结束（{s.Slot.GetType().Name}）: {s.Initiator?.Name} ↔ {s.Target?.Name}");
        }

        /// <summary>Mission 结束清理（AgentAIController.OnRemoveBehavior）。</summary>
        public static void ClearContinuations()
        {
            lock (_lock)
            {
                foreach (var c in _active)
                {
                    try { c.Slot.OnEnd(c); } catch { }
                }
                _active.Clear();
            }
        }

        /// <summary>清理 LLM 常见画蛇添足（与 ImReplyService.SanitizeReply 同款）：首尾引号/「XX说：」前缀/换行折叠。</summary>
        public static string Sanitize(string text, string speakerName)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim().Trim('"', '“', '”', '「', '」');
            int colon = t.IndexOfAny(new[] { ':', '：' });
            if (colon > 0 && colon < 20)
            {
                string prefix = t.Substring(0, colon);
                if (prefix.Contains(speakerName) || prefix.Length <= 4)
                    t = t.Substring(colon + 1).Trim();
            }
            while (t.Contains("\n\n")) t = t.Replace("\n\n", "\n");
            if (t.Length > 200) t = t.Substring(0, 200);
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }
    }
}

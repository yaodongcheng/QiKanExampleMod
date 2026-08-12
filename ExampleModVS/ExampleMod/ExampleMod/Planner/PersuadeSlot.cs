using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PersuadeSlot.cs — 无导演说服会话（npc-dialogue-session-plan.md §5，M1）
    //
    // 会话容器负责（基础设施规则）：
    //   - 持有 话题/成员/轮次/倾向 agree/历史
    //   - 回合交替（发起者 ↔ 接受者，同人连说上限 1 句后让位）
    //   - 防刷屏闸门（SpeechChannel 全局闸门）、距离检测（>15m 终止）、打断检测（战斗/警戒/倒下）
    //   - 轮次上限（MaxPersuadeRounds=6）
    //   - 兑现（C# 阈值 → 行为 + plan_decision 回流，ISessionOutcome）
    // 会话容器**禁止**：决定台词内容/编排——每轮台词由 LLM 润色（§5.3：注入 agree/方向/身份）
    // 或 SessionDialogueTemplates 模板选择（§5.3.1：无 LLM 完整降级），机制共用。
    //
    // 核心分工：LLM 不决策，只是润色。是否响应/说服倾向/同意拒绝/执行什么行为——全部 C# 确定性。
    //
    // 两种驱动（与 SayToSlot/SocialSlot 同构）：
    //   - 执行器驱动：say_to persuade 模式 → SayInlineState 持有 Session 每帧调 Slot.OnTick
    //   - 续话器驱动：玩家喊话发起 → DialogueComponent.RegisterSession（TickContinuations 驱动）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>接受者的坚守 mind（§5.3，每会话一份；M3 可持久化到 Hero 记忆）。</summary>
    public class Stance
    {
        public float Resistance;        // 抵抗度 0~1：人格 duty/temper 高→高；gullibility 高→低；涉己度高→高
        public float TopicInvolvement;  // 话题涉己度 0~1（守卫被叫走 = 涉己高 → 难劝；问路 = 低 → 好劝）
        public float Agree;             // 当前倾向 0~1，会话内演化；初始按人格（偏拒~中立）

        /// <summary>按人格 × 意图 × 职业现算 stance（MVP 会话内临时；M3 持久化）。</summary>
        public static Stance FromPersonality(ReactivePersonality p, string intent, string occupation)
        {
            var s = new Stance();
            float duty = p?.Duty ?? 0.5f;
            float temper = p?.Temper ?? 0.5f;
            float gull = p?.Gullibility ?? 0.5f;
            s.TopicInvolvement = ComputeInvolvement(intent, occupation);
            // 抵抗度：duty/temper 高 → 高；gullibility 高 → 低；涉己度高 → 进一步抬高
            s.Resistance = MathF.Clamp(duty * 0.7f + temper * 0.3f - gull * 0.5f, 0f, 1f);
            s.Resistance = MathF.Clamp(s.Resistance * (1f + s.TopicInvolvement * 0.5f), 0f, 1f);
            // 初始 agree：0.5 - duty*0.25 + gullibility*0.15（守卫偏拒 ~0.32，村民中立 ~0.45）
            s.Agree = MathF.Clamp(0.5f - duty * 0.25f + gull * 0.15f, 0.2f, 0.55f);
            return s;
        }

        /// <summary>话题涉己度（意图分类 × 职业风味；move_req 叫走守卫 = 最高）。</summary>
        private static float ComputeInvolvement(string intent, string occupation)
        {
            string cat = SessionDialogueTemplates.Categorize(intent);
            float baseVal = cat switch
            {
                "move_req" => 0.6f,   // 被叫走/跟着走 = 涉己高
                "combat" => 0.8f,     // 武力冲突 = 最抗拒
                "affair" => 0.3f,     // 办件事 = 中等
                _ => 0.1f,            // 闲聊 = 低
            };
            // 职业风味：守卫在任何要求下都比平民更坚守（军务在身）
            if (string.Equals(occupation, "guard", StringComparison.OrdinalIgnoreCase))
                baseVal = Math.Min(1f, baseVal + 0.2f);
            return baseVal;
        }
    }

    /// <summary>会话参与者（层无关抽象：Mission=Agent / Campaign=Hero；铁律 8 模板 NPC 平权）。</summary>
    public class SessionActor
    {
        public string Id;        // Hero StringId / TEMP_AGENT_x / "player"
        public string Name;
        public Agent Agent;      // Mission 层实体（可 null）
        public Hero Hero;        // Campaign 层实体（可 null）

        public static SessionActor FromAgent(Agent a)
        {
            if (a == null) return null;
            return new SessionActor
            {
                Agent = a,
                Id = ReactiveAgent.GetAgentId(a),
                Name = a.Name?.ToString() ?? "?",
                Hero = (a.Character as CharacterObject)?.HeroObject,
            };
        }
    }

    /// <summary>会话兑现策略接口（§5.4.1，🔴 言行一致边界：Mission 兑现动作 / Campaign 兑现计划）。</summary>
    public interface ISessionOutcome
    {
        void OnAgree(SessionActor responder, DialogueSession session);   // 同意 → 对应行为/计划
        void OnRefuse(SessionActor responder, DialogueSession session);  // 拒绝 → 拒绝行为/消息
        void OnAbort(DialogueSession session);                           // 打断/超时 → 清理（不兑现）
    }

    /// <summary>Mission 层兑现适配器：兑现场景动作（follow + plan_decision 回流；BRING 语义）。</summary>
    public class MissionSessionOutcome : ISessionOutcome
    {
        public static readonly MissionSessionOutcome Instance = new MissionSessionOutcome();

        public void OnAgree(SessionActor responder, DialogueSession session)
        {
            try
            {
                var responderAgent = responder?.Agent;
                var initiator = session?.Initiator;
                if (responderAgent == null || !responderAgent.IsActive()) return;
                var brain = AgentAIController.GetBrainForAgent(responderAgent);
                if (brain == null) return;

                // 同意 → 跟发起者走一段（BRING/FOLLOW/GUIDE 语义；复用 follow_for_a_bit 拆步入队）+ 折返岗点
                var ra = ReactiveAgent.Get(responderAgent);
                // 🔴 折返点防御：说服会话不走 TryHandleEvent（岗位从未记录，PostPos=Zero）→
                // 用当前位置作折返基准，避免折返走向地图原点 (0,0,0)
                if (!ra.HasPost)
                {
                    ra.HasPost = true;
                    ra.PostPos = responderAgent.Position;
                }
                brain.RunReactiveAction(
                    new FollowAgentAction(initiator, run: false, optionalDuration: 14f),
                    new MoveToPositionAction(ra.PostPos, Vec2.Zero, run: false, stopDistance: 1.5f,
                        maxTime: 20f, skipGetupDelay: true,
                        endBehavior: MoveToPositionAction.EndBehavior.Unlock));
                // 决策结果回流（发起者执行器 on_event 控制流——既有链路零改动）
                if (initiator != null && initiator.IsActive())
                    AgentAIController.Instance?.SendEventToAgent(initiator, "plan_decision", "followed", responderAgent);
                DebugLogger.Log($"[Persuade] 同意兑现：{responderAgent.Name} 跟随 {initiator?.Name}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Persuade] OnAgree 异常: {ex.Message}");
            }
        }

        public void OnRefuse(SessionActor responder, DialogueSession session)
        {
            try
            {
                var responderAgent = responder?.Agent;
                var initiator = session?.Initiator;
                if (responderAgent == null || !responderAgent.IsActive()) return;
                // 拒绝台词（说话并联 Warning 优先级，不占队列；前因 = 会话拒绝兑现）
                SpeechChannel.Say(responderAgent,
                    LWNTextHelper.ResolveText("LWN_reactive_refuse", "No, I cannot do that."), SpeechPriority.Warning,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(responderAgent), initiator, "spoken_to", session?.Topic));
                if (initiator != null && initiator.IsActive())
                    AgentAIController.Instance?.SendEventToAgent(initiator, "plan_decision", "refused", responderAgent);
                DebugLogger.Log($"[Persuade] 拒绝兑现：{responderAgent.Name} 拒绝 {initiator?.Name}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Persuade] OnRefuse 异常: {ex.Message}");
            }
        }

        public void OnAbort(DialogueSession session)
        {
            // 打断/超时 → 不兑现（战斗类直接终止，避免"打到一半还答应跟你走"）
            DebugLogger.Log($"[Persuade] 会话中止（不兑现）：{session?.Initiator?.Name} ↔ {session?.Target?.Name}");
        }
    }

    /// <summary>
    /// 说服会话槽（§5.2 状态机 + §5.3 说服公式）：发起者每说一句 → 接受者独立判断（Δagree 演化）
    /// → 台词（LLM 润色 or 模板）→ 兑现检查。会话容器只做轮次/闸门/兑现，**不编排台词**。
    ///
    /// 驱动：执行器（say_to persuade 模式，SayInlineState 每帧调 OnTick）/
    ///       续话器（玩家喊话发起，RegisterSession + OnPlayerSays 注入玩家台词）。
    /// </summary>
    public class PersuadeSlot : IDialogueSlot
    {
        public const int MaxPersuadeRounds = 6;        // 轮次上限（"聊天不会太长"；复用 MaxDialogueRounds=6 精神）
        public const float SpeakGapS = 1.4f;           // 说话间隔（气泡并行，间隔仅为节奏）
        public const float IdleTimeoutS = 60f;         // 冷场超时（玩家不再说话 → 取整兑现）
        public const float InterruptDist = 15f;        // 距离上限（>15m 终止）
        public const float AgreeThreshold = 0.65f;     // 同意阈值
        public const float RefuseThreshold = 0.35f;    // 拒绝阈值

        private enum Phase { Idle, SpeakInit, WaitGap, SpeakResp, Ended }

        private readonly Agent _initiator;
        private readonly Agent _responder;
        private readonly string _intent;
        private readonly bool _playerDriven;    // 玩家发起（台词由 OnPlayerSays 注入，容器不生成发起者句）
        private readonly bool _autoDriveInit;   // say_to 发起：发起者句由容器自动生成（随从劝说）
        private readonly int _outlineCount;     // 论据充实度（plan outline 段数 → argumentBonus）
        private Phase _phase = Phase.Idle;
        private float _timer;
        private float _llmInitTimer;            // 发起者句 LLM 预算计时
        private string _pendingInitLine;        // 发起者句（LLM 异步结果）
        private string _pendingRespLine;        // 接受者句（LLM 异步结果）
        private string _lastInitLine = "";      // 发起者上一句（LLM 上下文）
        private string _lastRespLine = "";      // 接受者上一句
        private bool _initLineRequested;
        private bool _respLineRequested;
        private bool _ended;
        private readonly HashSet<string> _usedKeys = new HashSet<string>(StringComparer.Ordinal);  // 模板会话内去重
        private readonly string _occupation;    // 接受者职业（模板风味档）
        private bool _settled;                  // 已兑现（终结一次）

        public bool Finished { get; private set; }
        public DialogueSession Session { get; private set; }

        /// <summary>
        /// 创建说服会话。
        /// </summary>
        /// <param name="playerDriven">玩家为发起者：玩家台词外部注入（OnPlayerSays），容器不生成发起者句。</param>
        /// <param name="autoDriveInit">发起者为 NPC：容器自动生成发起者劝说句（随从劝说）。</param>
        /// <param name="outlineCount">发起者论据充实度（plan outline 段数；说服公式 argumentBonus）。</param>
        public PersuadeSlot(Agent initiator, Agent responder, string topic, string intent,
            ISessionOutcome outcome, bool playerDriven = false, bool autoDriveInit = true,
            int outlineCount = 0, ReactivePersonality respPersonality = null)
        {
            _initiator = initiator;
            _responder = responder;
            _intent = intent;
            _playerDriven = playerDriven;
            _autoDriveInit = autoDriveInit;
            _outlineCount = outlineCount;
            var ra = responder != null ? ReactiveAgent.Get(responder) : null;
            _occupation = ra != null ? ReactiveAgent.ClassifyOccupation(responder) : "default";

            Session = new DialogueSession
            {
                Initiator = initiator,
                Target = responder,
                Topic = topic,
                Intent = intent,
                Outcome = outcome ?? MissionSessionOutcome.Instance,
                IsPersuade = true,
                Slot = this,
            };
            Session.Stance = Stance.FromPersonality(respPersonality ?? ra?.Personality, intent, _occupation);
            Session.Round = 0;
            Session.LastActivityAt = Mission.Current != null ? Mission.Current.CurrentTime : 0f;
            // 🔴 自举启动：SayInlineState（执行器驱动）不调 OnStart——构造即进入 SpeakInit，
            // OnStart 对已启动状态幂等（只重置计时 + 日志）
            _phase = Phase.SpeakInit;
        }

        public void OnStart(DialogueSession s)
        {
            _phase = Phase.SpeakInit;
            _timer = 0f;
            DebugLogger.Log($"[Persuade] 会话开始：{_initiator?.Name} 劝说 {_responder?.Name}（话题={s?.Topic}，意图={_intent}，初始 agree={s?.Stance?.Agree:F2}）");
        }
        /// <summary>玩家喊话注入（playerDriven 模式的发起者台词；BroadcastPlayerCall 调用）。</summary>
        public void OnPlayerSays(string text)
        {
            if (_ended || !_playerDriven || string.IsNullOrWhiteSpace(text)) return;
            // 玩家台词 = 发起者句：立即推进
            _lastInitLine = text;
            Session.LastActivityAt = Mission.Current != null ? Mission.Current.CurrentTime : 0f;
            // 玩家模式：玩家说话 → 接受者回应（直接进入回应阶段；正在回应中则排队等待本轮结束）
            if (_phase == Phase.SpeakResp || _phase == Phase.WaitGap)
                return;
            _phase = Phase.SpeakResp;
            _timer = 0f;
            // 玩家台词不占 SpeechChannel（玩家冒泡由调用方已播）——只广播旁观者（听到的是玩家这句）
            _lastRespLine = text;
            BroadcastToBystanders();
            RequestResponderLine();
        }

        public void OnTick(DialogueSession s, float dt)
        {
            if (Finished || _ended || _phase == Phase.Idle) return;
            // 🔴 以自身 Session 为权威状态（注册表驱动的 session 与自身 Session 是同一对象——
            // RegisterSession 对 PersuadeSlot 直接注册 ps.Session；防御：不一致时用自身）
            var sess = Session ?? s;
            if (sess == null) return;
            try
            {
                // ① 打断检测（遗留问题 1 拍板：战斗类直接终止**不兑现**；警戒/倒下/距离同）
                if (CheckInterrupt())
                {
                    End(SettleKind.Abort);
                    return;
                }

                // ② 冷场超时（玩家不再说话；say_to 模式发起者自动驱动不受影响）
                if (_playerDriven && sess.IdleSeconds > IdleTimeoutS)
                {
                    // 超时取整兑现（§5.3 兜底：agree > 0.5 → 同意，否则拒绝）
                    End(SettleKind.RoundLimit);
                    return;
                }

                // ③ 发起者自动劝说句（say_to 模式：容器驱动发声——"随从在会话期间的劝说表现由会话容器驱动"）
                if (_phase == Phase.SpeakInit && !_playerDriven && _autoDriveInit)
                {
                    if (!_initLineRequested)
                    {
                        _initLineRequested = true;
                        RequestInitiatorLine();
                    }
                    _llmInitTimer += dt;
                    if (_pendingInitLine != null)
                    {
                        string line = _pendingInitLine;
                        _pendingInitLine = null;
                        _lastInitLine = line;
                        SpeechChannel.Say(_initiator, line, SpeechPriority.Dialogue,
                            SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(_initiator), _responder, "spoken_to", sess.Topic));
                        sess.LastActivityAt = Mission.Current != null ? Mission.Current.CurrentTime : 0f;
                        DebugLogger.Log($"[Persuade] 发起者（第 {sess.Round + 1} 轮）: {_initiator.Name}: {line}");
                        // 发起者句播完 → 等间隔 → 接受者回应
                        _phase = Phase.WaitGap;
                        _timer = 0f;
                        RequestResponderLine();
                    }
                    else if (_llmInitTimer >= 2.5f)
                    {
                        // LLM 预算耗尽 → 模板兜底（铁律 1：必到）
                        _pendingInitLine = SessionDialogueTemplates.Resolve(
                            sess.Stance?.Agree ?? 0.4f, "initiator", _intent, _occupation, sess.Round, _usedKeys)
                            ?? "";
                        if (string.IsNullOrEmpty(_pendingInitLine))
                            _pendingInitLine = "嗯……";
                    }
                }

                // ④ 接受者回应阶段
                if (_phase == Phase.WaitGap)
                {
                    _timer += dt;
                    if (_timer >= SpeakGapS)
                    {
                        _phase = Phase.SpeakResp;
                        _timer = 0f;
                    }
                }
                else if (_phase == Phase.SpeakResp)
                {
                    if (!_respLineRequested)
                    {
                        _respLineRequested = true;
                        RequestResponderLine();
                    }
                    if (_pendingRespLine != null)
                    {
                        string line = _pendingRespLine;
                        _pendingRespLine = null;
                        _lastRespLine = line;
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            SpeechChannel.Say(_responder, line, SpeechPriority.Dialogue,
                                SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(_responder), _initiator, "spoken_to", sess.Topic));
                            DebugLogger.Log($"[Persuade] 接受者（第 {sess.Round} 轮，agree={sess.Stance.Agree:F2}）: {_responder.Name}: {line}");
                        }
                        // 兑现检查（回应已播 → 看是否终结）
                        if (!CheckSettle())
                        {
                            // 未终结 → 下一轮：say_to 模式发起者继续说；玩家模式等待玩家下一句
                            _phase = _playerDriven ? Phase.Idle : Phase.SpeakInit;
                            _initLineRequested = false;
                            _respLineRequested = false;
                            _timer = 0f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Persuade] OnTick 异常: {ex.Message}");
                End(SettleKind.Abort);
            }
        }

        public void OnEnd(DialogueSession s)
        {
            // 终结清理（兑现已在 End 内完成）
            _ended = true;
        }

        // ═══════════════════════════════════════════════════════════
        // 接受者独立判断（§5.3 核心：C# 确定性 + LLM 只润色）
        // ═══════════════════════════════════════════════════════════

        /// <summary>请求接受者台词：Δagree 演化 → 兑现检查 → 台词（LLM 润色 or 模板）。</summary>
        private void RequestResponderLine()
        {
            _respLineRequested = true;
            var s = Session;
            if (s?.Stance == null) { _pendingRespLine = ""; return; }
            var stance = s.Stance;

            // ── Δagree 公式（§5.3，C# 确定性；发起者魅力 social → persuadePower）──
            var initPersonality = ReactiveAgent.Get(_initiator)?.Personality ?? new ReactivePersonality();
            float persuadePower = 0.15f + initPersonality.Social * 0.2f;
            float argumentBonus = _outlineCount * 0.03f;
            float resistance = stance.Resistance * 0.15f;
            int round = Math.Max(1, s.Round + 1);
            float decay = 1.2f * round;                                  // 轮次衰减（听多了免疫）
            float jitter = (MBRandom.RandomFloat - 0.5f) * 0.1f;          // 少量抖动
            float delta = (persuadePower + argumentBonus - resistance) / decay + jitter;
            stance.Agree = MathF.Clamp(stance.Agree + delta, 0f, 1f);
            s.Round++;
            s.LastActivityAt = Mission.Current != null ? Mission.Current.CurrentTime : 0f;

            DebugLogger.Log($"[Persuade] 第 {s.Round} 轮 Δagree={delta:F3} → agree={stance.Agree:F3}（power={persuadePower:F2} bonus={argumentBonus:F2} resist={resistance:F2}）");

            // ── 兑现检查（台词档位要与最终结果一致——先检查，已终结则台词播"结果句"）──
            if (CheckSettle())
                return;

            // ── 台词生成：LLM 润色（有配置走 2s 预算异步；无配置/失败 → 模板）──
            if (Settings.Instance.IsLLMConfigured && _responder != null && _responder.IsActive())
            {
                RequestLlmResponderLine();
            }
            else
            {
                // 🔴 铁律 1 完整降级：模板会话（多轮照常推进，文本生硬但流程完整）
                _pendingRespLine = SessionDialogueTemplates.Resolve(
                    stance.Agree, "responder", _intent, _occupation, s.Round, _usedKeys)
                    ?? "";
                if (string.IsNullOrEmpty(_pendingRespLine))
                    _pendingRespLine = "嗯。";
            }
        }

        /// <summary>接受者台词 LLM 润色（§5.3：注入 话题/轮次/agree 数值/方向/身份人格/对方上一句）。
        /// 动作空间按接受者 in-scene 注入（§5.4.2，复用 ActionHandler——LLM 只能在当前空间内选 action_code）。</summary>
        private void RequestLlmResponderLine()
        {
            var s = Session;
            string world = Settings.Instance?.WorldDescription ?? "";
            string occName = LWNTextHelper.ResolvePrompt("LWN_prompt_trait_occupation_" + _occupation);
            if (string.IsNullOrEmpty(occName)) occName = _occupation;
            string personality = ReactiveAgent.DescribePersonalityForPrompt(
                ReactiveAgent.Get(_responder)?.Personality);
            string identity = string.Format(
                DialogueComponent.ResolvePrompt("LWN_plan_respond_identity_template", "你是{0}。{1}。"),
                occName, personality);
            // 方向（Δ 正负 → 台词态度："你开始动摇" / "你态度坚决"）
            string direction = SessionDialogueTemplates.DescribeDirection(s.Stance.Agree);
            string otherName = _initiator?.Name?.ToString() ?? "对方";
            string lastLine = _lastInitLine;
            string actionSpace = ActionHandler.GetActionSpacePrompt(
                (_responder.Character as CharacterObject)?.HeroObject,
                (_initiator?.Character as CharacterObject)?.HeroObject,
                _responder);
            var mem = AllNpcMemoryManager.GetMemoryForAgent(_responder);
            string history = mem != null
                ? PromptBuilder.GetPrompt_RespondContext(mem, ReactiveAgent.GetAgentId(_initiator))
                : "";

            _ = DialogueComponent.GenerateLine(
                world, identity, direction,
                string.IsNullOrEmpty(s.Topic) ? "闲聊" : s.Topic,
                $"（第 {s.Round} 轮）", "",
                otherName + "（对方正在劝你）",
                history, lastLine,
                "LWN_plan_persuade_rule",
                "【要求】用一句话口语化回应对方的劝说（10-40 字），态度与你此刻的倾向一致：倾向答应就松动，倾向拒绝就推脱。直接说台词本身——不要引号、不要解释、不要动作描写。",
                actionSpace, maxTokens: 120, timeoutMs: 2000)
                .ContinueWith(t =>
                {
                    try
                    {
                        if (_ended) return;
                        string result = t.Status == TaskStatus.RanToCompletion && t.Result != null
                            ? DialogueComponent.Sanitize(t.Result.Reply, _responder?.Name?.ToString() ?? "")
                            : null;
                        if (!string.IsNullOrWhiteSpace(result) && t.Result != null && t.Result.FromLlm)
                        {
                            _pendingRespLine = result;
                            // 动作决策（空间由 ActionHandler 二次裁剪；场景外动作自动降级 NONE）
                            if (!string.IsNullOrEmpty(t.Result.ActionCode) && t.Result.ActionCode != "NONE")
                            {
                                try
                                {
                                    ActionHandler.HandleAction(t.Result.ActionCode,
                                        (_responder.Character as CharacterObject)?.HeroObject,
                                        (_initiator?.Character as CharacterObject)?.HeroObject,
                                        _responder, t.Result.ActionLevel, t.Result.ActionTarget, result);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            _pendingRespLine = SessionDialogueTemplates.Resolve(
                                Session?.Stance?.Agree ?? 0.4f, "responder", _intent, _occupation, Session?.Round ?? 1, _usedKeys)
                                ?? "嗯。";
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[Persuade] 接受者台词生成异常: {ex.Message}");
                        _pendingRespLine = SessionDialogueTemplates.Resolve(
                            Session?.Stance?.Agree ?? 0.4f, "responder", _intent, _occupation, Session?.Round ?? 1, _usedKeys)
                            ?? "嗯。";
                    }
                });
        }

        /// <summary>发起者劝说句（LLM 润色 or 模板 initiator 档；自动驱动模式）。</summary>
        private void RequestInitiatorLine()
        {
            var s = Session;
            if (Settings.Instance.IsLLMConfigured && _initiator != null && _initiator.IsActive())
            {
                string world = Settings.Instance?.WorldDescription ?? "";
                string initName = _initiator.Name?.ToString() ?? "随从";
                string identity = string.Format(
                    DialogueComponent.ResolvePrompt("LWN_plan_respond_identity_template", "你是{0}。{1}。"),
                    initName, DialogueComponent.ResolvePrompt("LWN_trait_companion", "随从"));
                var mem = AllNpcMemoryManager.GetMemoryForAgent(_initiator);
                string history = mem != null
                    ? PromptBuilder.GetPrompt_RespondContext(mem, ReactiveAgent.GetAgentId(_responder))
                    : "";
                _ = DialogueComponent.GenerateLine(
                    world, identity, "",
                    string.IsNullOrEmpty(s.Topic) ? "闲聊" : s.Topic,
                    $"（第 {s.Round + 1} 轮劝说）", "",
                    _responder?.Name?.ToString() ?? "对方",
                    history, _lastRespLine,
                    "LWN_plan_persuade_init_rule",
                    "【要求】用一句话继续劝说对方（10-40 字），根据对方的抗拒逐渐加码（讲道理/许诺/恳求），符合随从身份。直接说台词本身——不要引号、不要解释、不要动作描写。",
                    null, maxTokens: 100, timeoutMs: 2000)
                    .ContinueWith(t =>
                    {
                        try
                        {
                            if (_ended) return;
                            string result = t.Status == TaskStatus.RanToCompletion && t.Result != null
                                ? DialogueComponent.Sanitize(t.Result.Reply, _initiator?.Name?.ToString() ?? "")
                                : null;
                            if (!string.IsNullOrWhiteSpace(result) && t.Result != null && t.Result.FromLlm)
                                _pendingInitLine = result;
                            else
                                _pendingInitLine = SessionDialogueTemplates.Resolve(
                                    Session?.Stance?.Agree ?? 0.4f, "initiator", _intent, _occupation, Session?.Round ?? 1, _usedKeys)
                                    ?? "";
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[Persuade] 发起者台词生成异常: {ex.Message}");
                            _pendingInitLine = SessionDialogueTemplates.Resolve(
                                Session?.Stance?.Agree ?? 0.4f, "initiator", _intent, _occupation, Session?.Round ?? 1, _usedKeys)
                                ?? "";
                        }
                    });
            }
            else
            {
                _pendingInitLine = SessionDialogueTemplates.Resolve(
                    s.Stance?.Agree ?? 0.4f, "initiator", _intent, _occupation, s.Round, _usedKeys)
                    ?? "";
            }
        }

        /// <summary>兑现检查：agree 阈值 / 轮次上限（终结返回 true）。</summary>
        private bool CheckSettle()
        {
            var s = Session;
            if (s?.Stance == null) { End(SettleKind.Abort); return true; }
            if (s.Stance.Agree >= AgreeThreshold) { End(SettleKind.Agree); return true; }
            if (s.Stance.Agree <= RefuseThreshold) { End(SettleKind.Refuse); return true; }
            if (s.Round >= MaxPersuadeRounds) { End(SettleKind.RoundLimit); return true; }
            return false;
        }

        private enum SettleKind { Agree, Refuse, RoundLimit, Abort }

        /// <summary>会话终结：兑现 + 注销（幂等）。</summary>
        private void End(SettleKind kind)
        {
            if (_ended || _settled) return;
            _settled = true;
            _ended = true;
            Finished = true;
            var s = Session;
            if (s == null) return;
            var responder = SessionActor.FromAgent(s.Target);

            DebugLogger.Log($"[Persuade] 会话终结（{kind}）：{s.Initiator?.Name} ↔ {s.Target?.Name}，agree={s.Stance?.Agree:F2}，round={s.Round}");

            // 🔴 结果句（§5.3"台词档位要与最终结果一致"）：同意路径播同意档回应句——NPC 最后表态
            // 一句（SpeechChannel 并联不占队列，兑现行为随后执行：先表态、再行动，言行一致）。
            // 拒绝路径不播：OnRefuse 自带拒绝台词（LWN_reactive_refuse，Warning 优先级），避免双句。
            bool settleToAgree = kind == SettleKind.Agree
                || kind == SettleKind.RoundLimit && s.Stance != null && s.Stance.Agree > 0.5f;
            if (settleToAgree)
            {
                try
                {
                    string resultLine = SessionDialogueTemplates.Resolve(
                        1f, "responder", _intent, _occupation, s.Round, _usedKeys);
                    if (!string.IsNullOrWhiteSpace(resultLine))
                        SpeechChannel.Say(s.Target, resultLine, SpeechPriority.Dialogue,
                            SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(s.Target), s.Initiator, "spoken_to", s.Topic));
                }
                catch { }
            }

            if (kind == SettleKind.Agree || kind == SettleKind.RoundLimit && s.Stance.Agree > 0.5f)
            {
                s.Outcome?.OnAgree(responder, s);
            }
            else if (kind == SettleKind.Refuse || kind == SettleKind.RoundLimit)
            {
                s.Outcome?.OnRefuse(responder, s);
            }
            else
            {
                s.Outcome?.OnAbort(s);
            }

            // 会话终结 → 群聊议论（M2：话题 + 结果，参与度记忆 + 30% 接话）
            OnSettleNarration(s, kind);

            // 从续话器注销（续话器驱动时；执行器驱动时由执行器移除）
            DialogueComponent.EndSession(s);
        }

        /// <summary>终结判定：战斗/警戒/倒下/距离（遗留问题 1 拍板：战斗类直接终止不兑现）。</summary>
        private bool CheckInterrupt()
        {
            var s = Session;
            if (s == null) return true;
            if (_responder == null || !_responder.IsActive()) return true;
            if (_initiator != null && !_initiator.IsActive()) return true;
            var brain = AgentAIController.GetBrainForAgent(_responder);
            if (brain == null) return true;
            // 战斗（当前或排队）→ 打断（不兑现）
            if (brain.IsInCombat) return true;
            // 警戒 Alarmed → 打断（质问/对抗优先于会话）
            if (brain.AlertPhase >= AlarmPhase.Alarmed) return true;
            // 距离 > 15m → 打断
            if (_initiator != null && _initiator.IsActive())
            {
                try
                {
                    if (_responder.Position.Distance(_initiator.Position) > InterruptDist) return true;
                }
                catch { return true; }
            }
            return false;
        }

        /// <summary>旁观者广播（说话者视角：旁观者收到 seen_speaking；复用 HandleDialogue 的 15m 距离加权概率）。</summary>
        private void BroadcastToBystanders()
        {
            if (_responder == null || Mission.Current == null) return;
            try
            {
                Vec3 mid = (_initiator != null ? _initiator.Position : _responder.Position) + _responder.Position;
                mid *= 0.5f;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || a == _initiator || a == _responder || !a.IsActive()) continue;
                    float dist = a.Position.Distance(mid);
                    if (dist > DialogueComponent.BystanderRadius) continue;
                    float chance = Math.Max(1f - dist / DialogueComponent.BystanderRadius, 0.05f);
                    if (MBRandom.RandomFloat < chance)
                        AgentAIController.Instance?.SendEventToAgent(a, "seen_speaking", _initiator, _responder, _lastRespLine);
                }
            }
            catch { }
        }

        /// <summary>会话终结群聊议论（M2：ImEventBroadcaster 既有管线——话题 + 结果，参与度记忆 + 接话）。</summary>
        private void OnSettleNarration(DialogueSession s, SettleKind kind)
        {
            try
            {
                if (Mission.Current == null) return;
                string topic = string.IsNullOrEmpty(s.Topic) ? "对话" : s.Topic;
                string result = kind switch
                {
                    SettleKind.Agree => "谈拢了",
                    SettleKind.Refuse => "被拒绝了",
                    _ => "不欢而散",
                };
                // 描述走本地化（LWNTextHelper 铁律 13）；进群聊议论管线
                string desc = LWNTextHelper.ResolveCompound("LWN_dialog_settle_broadcast",
                    "在附近听到有人议论：{TOPIC}的事，好像{RESULT}。",
                    ("TOPIC", topic), ("RESULT", result));
                ImEventBroadcaster.BroadcastPlayerEvent("dialog_settle", desc);
            }
            catch { }
        }
    }
}

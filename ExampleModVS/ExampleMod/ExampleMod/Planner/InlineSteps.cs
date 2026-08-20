using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // InlineSteps.cs — 执行器内联步骤（不经过 IAtomicAction 队列的步骤）
    //
    // 设计：这些步骤的推进逻辑属于执行器（计时/广播/结算），
    // 不适合塞进原子行为库；每个步骤一个轻量状态机，由 ActorCursor 驱动。
    // 与 IAtomicAction 的界限：原子行为 = 可复用的引擎级动作；
    //              内联步骤 = 计划语法相关的编排逻辑。
    //
    // 🔴 单脑化重构（M0/D3）：内联分两类——
    //   行为性内联（lead/steal_attempt/knockout/emote，IsBehavioral=true）：直接驱动表现层
    //   （移动/姿态/击晕），经 InlinePlanAction 适配器（AtomicAction.cs）入队由脑驱动，
    //   中断语义与普通动作一致（生命周期归脑）；
    //   非行为性内联（say_to/wait/signal_player/end_plan/make_noise/give_*/deliver_*）：
    //   纯逻辑/通信，留在排序器侧直接驱动，Pause 时保留状态（恢复不重播/不重计时）。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>「迟迟不动手」的犹豫内心独白（括号包裹 = 说给自己听的心声；附近频道冒泡）。
    /// 🔴 2026-08-19（用户裁定）：偷窃绕后卡住（Behind 相位 3s）/ 等「没人看见」wait 卡住（5s）共用——
    /// 内容 = 当前会告发的目击者名单（GetWitnesses 已排除玩家/队友；被谁看见了说清楚）。
    /// 在线 = SayPolished LLM 润色（StimulusType=plan_command 命中润色分级），离线 = 模板直播（铁律 1 兜底）。</summary>
    internal static class StealHesitationMonologue
    {
        /// <summary>说一次（调用方保证节流：每卡住周期一次）。</summary>
        public static void Say(Agent agent)
        {
            try
            {
                if (agent == null || !agent.IsActive()) return;
                var witnesses = StealManager.GetWitnesses(agent, null, 15f, 120f);
                string fallback;
                if (witnesses.Count > 0)
                {
                    var names = witnesses
                        .Where(w => w != null && w.Name != null)
                        .Select(w => w.Name.ToString())
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct()
                        .Take(3)
                        .ToList();
                    // 本地化：犹豫独白-有目击者（玩家可见文本）
                    fallback = LWNTextHelper.ResolveCompound("LWN_npc_steal_monologue_spotted",
                        "({NAMES} keeps watching me… I can't get behind him.)",
                        ("NAMES", string.Join("、", names)));
                }
                else
                {
                    // 本地化：犹豫独白-无目击者（玩家可见文本）
                    fallback = LWNTextHelper.ResolveText("LWN_npc_steal_monologue_blocked",
                        "(I can't get behind him from here… need a better angle.)");
                }
                var ctx = SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(agent), null, "plan_command", null);
                ctx.Monologue = true;
                SpeechChannel.SayPolished(agent, fallback, SpeechPriority.Chat, ctx, 2f);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] 犹豫独白失败: {ex.Message}");
            }
        }
    }

    public interface IInlineStep
    {
        bool Ok { get; }        // 创建成功（目标可解析等）
        bool Finished { get; }  // 本步完成
        void OnTick(float dt);

        /// <summary>
        /// 请求中断（单脑化重构 M0/D3）：行为性内联经 InlinePlanAction 入队后由脑驱动，
        /// executor 侧任何迁移/终止（跳转/超时/Pause/中止）经 RequestInterrupt → 本方法传导到状态机。
        /// 标记中断使 Finished 立即为真（中断标记使动作 OnTick 直接结束、不会真执行）。
        /// 🔴 不可逆：Pause→Resume 必须重建全新状态机，复用旧实例 = 立即 IsFinished 死路。
        /// </summary>
        void Interrupt();

        /// <summary>中断标记（InlinePlanAction.IsFinished 判定用：Finished 或 Interrupted 即完成）。</summary>
        bool Interrupted { get; }

        /// <summary>
        /// 行为性内联标记（M0/D3）：直接驱动表现层（移动 ScriptedMoveToPoint / 姿态 SetPose /
        /// 击晕 ForcePlayAction）的内联——lead/steal_attempt/knockout/emote——必须经
        /// InlinePlanAction 入队由脑驱动（中断语义覆盖、无两个大脑打架）；
        /// 纯逻辑/通信内联（say_to/wait/signal_player/end_plan/make_noise/give_*/deliver_*）
        /// 留在排序器侧直接驱动。
        /// </summary>
        bool IsBehavioral { get; }
    }

    /// <summary>say_to：单句模式（text，现状）/ 对话模式（outline + topic，多轮 LLM 实时对话）/
    /// 🔴 M1 说服模式（persuade: true，npc-dialogue-session-plan.md §5）：多轮说服会话——
    /// 接受者 agree 逐轮演化，同意/拒绝兑现 + plan_decision 回流；无 LLM 走 XML 模板会话。
    /// 对话模式状态机平移为 SayToSlot（DialogueComponent）；说服模式 = PersuadeSlot。
    /// 🔴 2026-08-11（§5.6 四槽位体系）：对话模式状态机平移为 SayToSlot（DialogueComponent）——
    /// 本类变薄壳：单句模式保留原逻辑，对话模式委托 SayToSlot.Tick（BC-006 行为等价）。</summary>
    public class SayInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private readonly Agent _target;
        private readonly PlanStep _step;
        private readonly PlanExecutor _executor;
        private readonly SayToSlot _chatSlot;   // 🔴 对话模式插槽（2026-08-11：ChatPhase 状态机平移）
        private readonly PersuadeSlot _persuadeSlot; // 🔴 M1 说服模式插槽（多轮说服会话）
        private float _timer;
        private float _duration;
        private bool _said;
        private bool _broadcastDone;
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        // 非行为性内联：台词/广播 = 排序器侧编排（不进队列，不写移动/姿态表现层）
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现（非行为性内联不被 Pause/迁移中断——say_to 跨 Pause 保留状态不重播）。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }

        public SayInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _step = step;
            _agent = cursor.Agent;
            _target = null;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolveAgent(refName, cursor.Agent, out _target))
            {
                Ok = false;
                return;
            }
            Ok = true;
            // 🔴 M1 说服模式（persuade: true）：多轮说服会话（发起者=随从自动驱动，论据=outline 段数）
            if (step.Persuade)
            {
                _persuadeSlot = new PersuadeSlot(_agent, _target,
                    !string.IsNullOrEmpty(step.Topic) ? step.Topic : executor?.Summary,
                    (executor != null ? executor.IntentType.ToString() : null) ?? (string.IsNullOrEmpty(step.Ask) ? null : step.Ask),
                    MissionSessionOutcome.Instance,
                    playerDriven: false, autoDriveInit: true,
                    outlineCount: step.OutlineSegments?.Count ?? 0);
                return;
            }
            // 🔴 对话模式（outline 2+ 段）→ SayToSlot 插槽（状态机平移，行为等价）；单句模式保留原逻辑
            if (step.IsChatMode)
            {
                _chatSlot = new SayToSlot(executor, cursor, step);
                if (_chatSlot.Finished) { Finished = true; return; }
                return;
            }
            // 单句模式（现状不变）：冒泡时长按文本长度估算（"N 秒内必须播完"兜底由步骤 timeout 负责）
            float len = string.IsNullOrEmpty(step.TextOrContent) ? 0 : step.TextOrContent.Length;
            _duration = MathF.Min(MathF.Max(2f + len * 0.12f, 2f), step.TimeoutS > 0 ? step.TimeoutS : 6f);
        }

        public void OnTick(float dt)
        {
            if (Finished || !Ok) return;
            // 🔴 M1 说服模式 → PersuadeSlot 插槽驱动（多轮说服：agree 演化 → 兑现 → plan_decision 回流）
            if (_persuadeSlot != null)
            {
                var session = _persuadeSlot.Session;
                if (session != null && session.Slot != null)
                    session.Slot.OnTick(session, dt);
                if (_persuadeSlot.Finished)
                    Finished = true;
                return;
            }
            // 🔴 对话模式 → SayToSlot 插槽驱动（2026-08-11 架构收敛：统一 DialogueSession，每帧调 Slot.OnTick）
            if (_chatSlot != null)
            {
                var session = _chatSlot.Session;
                if (session != null && session.Slot != null)
                    session.Slot.OnTick(session, dt);
                if (_chatSlot.Finished)
                    Finished = true;   // 行为等价：对话模式完成仅置 Finished，执行器 CompleteStep 推进
                return;
            }
            // ── 单句模式（现状）──
            _timer += dt;
            if (!_said)
            {
                _said = true;
                try
                {
                    if (_target != null && _target.IsActive())
                        AgentControlHelper.FaceToActor(_agent, _target);
                    // 🔴 统一说话框架：say_to 单句模式（前因=plan step 台词）。
                    // 文本是计划期 LLM 生成的密令台词 → 不再 SayPolished（执行期重润色会偏离计划原意）
                    if (!string.IsNullOrEmpty(_step.TextOrContent))
                        SpeechChannel.Say(_agent, _step.TextOrContent, SpeechPriority.Dialogue,
                            SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(_agent), _target, "say_to", _step.Topic));
                }
                catch { }
            }
            if (_timer >= _duration)
            {
                if (!_broadcastDone)
                {
                    _broadcastDone = true;
                    BroadcastSpokenTo(_step.TextOrContent, null);
                    if (_executor != null)
                        _executor.NotifySayDone(_step, _target);
                }
                Finished = true;
            }
        }

        /// <summary>广播 spoken_to（Args[1]=随从台词、Args[2]=主题、Args[3]=当前走向段）；ask:follow 照旧。
        /// 🔴 §5.6（2026-08-10）：说话广播收敛到 DialogueComponent.HandleDialogue（含旁观者 seen_speaking 插话广播）。</summary>
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
    }

    /// <summary>wait：seconds（纯等待）/ until（等世界状态，由游标检查）/ 两者皆省 = 无限保持。</summary>
    public class WaitInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private float _timer;
        private bool _interrupted;
        private bool _monologueSaid;   // 🔴 2026-08-19：等「没人看见」卡住独白（每卡住周期一次）
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现（wait 跨 Pause 保留计时不重计）。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }

        public WaitInlineState(Agent agent, PlanStep step)
        {
            _agent = agent;
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            _timer += dt;
            if (_step.Seconds > 0f && _timer >= _step.Seconds) Finished = true;
            // 🔴 2026-08-19（用户裁定：迟迟不动手 → 附近频道内心独白）：等「没人看见」型 wait
            //（until = seeing(any, self)=false，如偷窃/击晕前等时机）卡住 5s → 说一次当前顾虑
            //（被谁看见了；括号 = 内心独白；在线 LLM 润色 / 离线模板，见 StealHesitationMonologue）
            if (!_monologueSaid && _timer > 5f && IsWaitingForNoWitness(_step))
            {
                _monologueSaid = true;
                StealHesitationMonologue.Say(_agent);
            }
            // until 由游标在 TickCursor 中统一检查；两者皆省 = 无限（结束 = R3 停止键）
        }

        /// <summary>结构判定：until 是否为「等没人看见我」（seeing(any, self)=false）——只有这种
        /// wait 卡住才说犹豫独白（其他 wait 是正常待命，不打扰）。</summary>
        private static bool IsWaitingForNoWitness(PlanStep step)
        {
            var u = step?.Until;
            return u != null
                && string.Equals(u.Type, "seeing", StringComparison.OrdinalIgnoreCase)
                && string.Equals(u.A, "any", StringComparison.OrdinalIgnoreCase)
                && string.Equals(u.Op, "false", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>signal_player：密信报告（非模态即时信号）。</summary>
    public class SignalInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly PlanStep _step;
        private bool _sent;
        private bool _interrupted;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }

        public SignalInlineState(PlanExecutor executor, PlanStep step)
        {
            _executor = executor;
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!_sent)
            {
                _sent = true;
                _executor?.SignalPlayer(_step.Text ?? "");
            }
            Finished = true;
        }
    }

    /// <summary>end_plan：收尾三路（result + report）。</summary>
    public class EndPlanInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly PlanStep _step;
        private bool _applied;
        private bool _interrupted;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }

        public EndPlanInlineState(PlanExecutor executor, PlanStep step)
        {
            _executor = executor;
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!_applied)
            {
                _applied = true;
                string result = _step.ResultString ?? "fail";
                _executor?.ApplyEndPlan(_step, result);
            }
            Finished = true;
        }
    }

    /// <summary>emote：语义标签 → 引擎动画（M5 验证动画表；失败降级无动作，不崩不穿模）。</summary>
    public class EmoteInlineState : IInlineStep
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> AnimMap =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "nod", "act_agree_1" },
                { "shake", "act_disagree_1" },
                { "wave", "act_wave_1" },
                { "cheer", "act_cheer_1" },
                { "bow", "act_bow_1" },
                { "shrug", "act_shrug_1" },
                { "point", "act_point_1" },
                { "threaten", "act_threaten_1" },
                { "disappointed", "act_defeat_1" },
            };

        private readonly Agent _agent;
        private float _timer;
        private bool _played;
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        // 🔴 行为性内联（M0/D3）：SetPose 直接驱动表现层 → 经 InlinePlanAction 入队由脑驱动
        public bool IsBehavioral => true;
        public bool Interrupted => _interrupted;
        public void Interrupt() { _interrupted = true; Finished = true; }

        public EmoteInlineState(Agent agent, PlanStep step)
        {
            _agent = agent;
            if (string.IsNullOrEmpty(step.Text) || !AnimMap.TryGetValue(step.Text, out string anim))
            {
                // 未知语义标签 → 降级无动作（装饰步骤不改世界状态，§4 emote 行）
                Ok = true;
                Finished = true;
                return;
            }
            Ok = true;
            try
            {
                AgentControlHelper.SetPose(agent, anim);
                _played = true;
            }
            catch
            {
                // 播放失败 → 降级无动作（不崩、不穿模）
                Ok = true;
                Finished = true;
            }
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            _timer += dt;
            if (!_played) { Finished = true; return; }
            // 动画播完（不再处于该 pose）或 2s 兜底
            if (_timer >= 2f)
            {
                try { AgentControlHelper.StopAndReset(_agent); } catch { }
                Finished = true;
            }
        }
    }

    /// <summary>crouch / stand：NPC 下蹲/站起（玩家 Z 键同机制）。
    /// 🔴 2026-08-14 两轮实机：`SetCrouchMode`（AIScriptedFrameFlags.Crouch）对**被脑 Suspend 的 NPC
    /// 不渲染**——flag 由 vanilla AI 的移动/动画选择系统消费（原版犯人蹲地 = vanilla AI 在跑，反编译
    /// SwitchPrisonerFollowingState 实锤），Suspend 后无人消费。改用 SetPose 直播蹲姿动画
    ///（act_crouch_walk_idle_unarmed，真蹲姿 ID——曾误判"蹲姿不存在"）；flag 保留为双保险（AI 恢复时语义一致）。
    /// 蹲姿保持到「站起」命令 / 移动指令覆盖（SetPose 被移动动画覆盖 = 自然起身）/ 脑接管清 flag。
    /// 免确认动作（RequiresConfirm 默认 false）：IM 下达立即执行。</summary>
    public class CrouchInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private readonly bool _crouch;
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        // 🔴 行为性内联（M0/D3）：SetPose 驱动表现层（姿态动画）→ 经 InlinePlanAction 入队由脑驱动
        public bool IsBehavioral => true;
        public bool Interrupted => _interrupted;
        public void Interrupt() { _interrupted = true; Finished = true; }

        public CrouchInlineState(Agent agent, PlanStep step)
        {
            _agent = agent;
            _crouch = step.Action == "crouch";
            if (agent == null || !agent.IsActive()) { Ok = false; return; }
            Ok = true;
            try
            {
                if (_crouch)
                {
                    AgentControlHelper.SetPose(agent, "act_crouch_walk_idle_unarmed");
                    agent.SetCrouchMode(true);   // flag 双保险（AI 消费路径；Suspend 下无副作用）
                    AgentBrain.SetCrouchPose(agent, true);   // 人工记录（native flag 对 Suspend NPC 不可信）
                }
                else
                {
                    AgentControlHelper.SetPose(agent, "act_walk_idle_unarmed");
                    agent.SetCrouchMode(false);
                    AgentBrain.SetCrouchPose(agent, false);
                }
            }
            catch { Finished = true; }   // 姿态设置失败 → 降级无动作（不崩；装饰性动作不改世界状态）
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            Finished = true;
        }
    }

    /// <summary>make_noise：喊叫 + 复用 WitnessCrime 围观聚集（criminal=随从 = 纯围观无犯罪副作用，§4）。</summary>
    public class MakeNoiseInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private float _timer;
        private bool _interrupted;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }
        // 非行为性：喊叫 + 事件广播 = 通信（不写移动/姿态表现层）
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }

        public MakeNoiseInlineState(PlanExecutor executor, ActorCursor cursor)
        {
            _agent = cursor.Agent;
            try
            {
                // 🔴 统一说话框架 + M4 双轨润色：喊叫台词（COMMOTION 引众围观；Warning 前因=make_noise）
                SpeechChannel.SayPolished(_agent, LWNTextHelper.ResolveText("LWN_plan_noise", "Hey! Over here! Look at this!"),
                    SpeechPriority.Warning,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(_agent), null, "make_noise", null));
                // 围观聚集：WitnessCrime 事件（criminal=self 非玩家 → 不加犯罪脉冲，纯围观）
                // 🔴 isCrime:false——随从喊一嗓子不能算犯罪（2026-08-13 suspect 化：仅围观不分类）
                AgentAIController.Instance?.BroadcastEventInRange(
                    _agent.Position, 20f, "WitnessCrime",
                    exclude: null, requireSight: false, isCrime: false,
                    _agent, null);
                DebugLogger.Log($"[PlanExecutor] make_noise: {_agent.Name} 引起围观");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] make_noise 失败: {ex.Message}");
            }
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            _timer += dt;
            if (_timer >= 2f) Finished = true;
        }
    }

    /// <summary>lead：带路（GUIDE）——朝目的地前进 + 定期回望（节奏同步在原子行为内部）。</summary>
    public class LeadInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private readonly Vec3 _destination;
        private readonly PlanExecutor _executor;
        private enum Phase { Moving, Waiting }
        private Phase _phase = Phase.Moving;
        private float _fixedTimer;
        private float _waitTimer;
        private bool _finishedFlag;
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished => _finishedFlag || _interrupted;
        // 🔴 行为性内联（M0/D3）：ScriptedMoveToPoint 直接驱动表现层 → 经 InlinePlanAction 入队由脑驱动
        public bool IsBehavioral => true;
        public bool Interrupted => _interrupted;
        /// <summary>中断：标记使 Finished 立即为真（脑下一帧见 IsFinished 自清出队，不再前进——无僵尸动作）。</summary>
        public void Interrupt() { _interrupted = true; }

        public LeadInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _agent = cursor.Agent;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolvePosition(refName, cursor.Agent, out _destination))
            {
                Ok = false;
                return;
            }
            Ok = true;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            var player = Agent.Main;
            if (_agent == null || !_agent.IsActive()) { _finishedFlag = true; return; }

            _fixedTimer += dt;
            switch (_phase)
            {
                case Phase.Moving:
                    // 前进（每 200ms 刷新目标点）
                    if (_fixedTimer >= 0.2f)
                    {
                        _fixedTimer = 0f;
                        try { AgentControlHelper.ScriptedMoveToPoint(_agent, _destination, false); } catch { }
                    }
                    // 到达目的地（GOAL：玩家到达）
                    if (player != null && player.IsActive() && player.Position.Distance(_destination) < 3f)
                    {
                        _finishedFlag = true;
                        return;
                    }
                    // 玩家跟丢（> 8m）→ 停下等
                    if (player != null && player.IsActive() && _agent.Position.Distance(player.Position) > 8f)
                    {
                        _phase = Phase.Waiting;
                        _waitTimer = 0f;
                        try { AgentControlHelper.FaceToActor(_agent, player); } catch { }
                    }
                    break;

                case Phase.Waiting:
                    _waitTimer += dt;
                    // 玩家跟上 → 继续
                    if (player != null && player.IsActive() && _agent.Position.Distance(player.Position) < 3f)
                    {
                        _phase = Phase.Moving;
                        _fixedTimer = 0f;
                        return;
                    }
                    // 等待超时 → 当面报告"你走不走啊" → 中止
                    if (_waitTimer > 15f)
                    {
                        _executor?.AbortWithReport(PlanTexts.LeadWaiting);
                        _finishedFlag = true;
                    }
                    break;
            }
        }
    }

    /// <summary>steal_attempt：NPC 侧偷窃原子（§4 两个变体 + §4.1 责权归属）。
    /// 物变体（箱子）：接近 → 蹲下 + Intent 显示 → 成功率公式 → 得手/摸空；目击 → 中断/问责玩家。
    /// 人变体（扒窃）：绕背定位（目标背后盲区 + 无目击者）→ 公式 → 从目标钱包守恒转移；无钱包 → 诚实 impossible。
    /// 🔴 2026-08-14（npc-risk-aware-planning.md M2d/M7）：
    ///   - 人变体按目标分叉：Hero 目标 → 现状（RecordStolenGold+StolenSource，give_gold 步骤 TransferGold
    ///     个人钱包）；模板 NPC 目标（无 Hero）→ StealPurseGold 钱袋路径（当场守恒移交，无尾步骤，_goldHanded 防双移交）
    ///   - 装备变体（variant="equipment"，M7 steal_equipment）：StealEquipmentForNpc 卸目标装备（武器槽优先）</summary>
    public class StealAttemptInlineState : IInlineStep
    {
        private enum AttemptPhase { Approach, Behind, Rolling, Settled }
        private AttemptPhase _phase = AttemptPhase.Approach;
        private float _timer;
        private string _resultKey;    // success / empty / impossible / interrupted
        private readonly PlanExecutor _executor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private readonly bool _variantItem;
        private readonly bool _variantEquipment;
        private readonly Random _rng = new Random();
        private float _amount;
        private bool _posed;         // 偷窃姿态已播（Rolling 一次性）
        private bool _interrupted;
        private string _stolenItemName;  // 🔴 2026-08-14（M7）：装备变体得手物品名（播报用）
        private bool _equipmentDetected; // 🔴 2026-08-14（M7）：装备变体失败 = 目标察觉（警戒脉冲 + 专属播报）
        private float _rollValue;        // 🔴 2026-08-14（M2a roll 透明）：掷点（d20：掷点 ≥ 门槛成功）
        private float _rollThreshold;    // 门槛（1 − 成功率）
        private bool _rollRecorded;      // roll 已记录（empty 播报带 ROLL/THRESHOLD，interrupted 无——没到判定环节）
        // 🔴 2026-08-19（用户质疑：单候选点正后方 navmesh 不可达时绕后永远无法执行，干等 8s 假失败）：
        // 绕后多候选点——当前选中点 + 选点节流时间戳（NavMesh 查询每帧做 4 次太贵，0.25s 重选一次，
        // 目标转身跟随走位；其余帧沿用选中点派发移动指令）
        private Vec3 _behindPick;
        private float _behindLastPick;
        // 🔴 2026-08-19（用户裁定：迟迟不动手 → 附近频道内心独白）：绕后卡住独白（每卡住周期一次）
        private bool _monologueSaid;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        // 🔴 行为性内联（M0/D3）：SetPose/ScriptedMoveToPoint 直接驱动表现层 → 经 InlinePlanAction 入队由脑驱动
        public bool IsBehavioral => true;
        public bool Interrupted => _interrupted;
        /// <summary>中断：标记使 Finished 立即为真（脑下一帧自清出队，不再执行偷窃动作）；顺带解除引擎蹲姿防残留。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; try { _agent?.SetCrouchMode(false); AgentBrain.SetCrouchPose(_agent, false); AgentControlHelper.SetPose(_agent, "act_walk_idle_unarmed"); } catch { } }
        public StealAttemptInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _agent = cursor.Agent;
            _step = step;
            _variantItem = step.Variant == "item";
            _variantEquipment = step.Variant == "equipment";
            // 目标解析（物 = 箱子物件；人 = 扒窃目标；装备变体 = 人目标）
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName)) { Ok = false; return; }
            bool targetOk = _variantItem
                ? executor.World.TryResolvePosition(refName, cursor.Agent, out _)
                : executor.World.TryResolveAgent(refName, cursor.Agent, out _);
            Ok = targetOk;
        }
        public void OnTick(float dt)
        {
            if (Finished || !Ok) return;
            _timer += dt;
            switch (_phase)
            {
                case AttemptPhase.Approach:
                    // 🔴 2026-08-13：原 SetPose("act_crouch") 双无效——action_types.xml 无此动作 ID
                    //（ActionIndexCache.Create 返回 act_none，SetPose 静默 return）+ 移动中会被覆盖。
                    // 蹲姿表意统一挪到 Rolling 阶段播有效动画（弯腰伸手）。
                    if (_variantItem)
                    {
                        // 物变体：接近目标物件（≤2m）
                        string refName = PlanRefUtil.Normalize(_step.Target, out string q);
                        if (q != null) refName = q;
                        if (!string.IsNullOrEmpty(refName) && _executor.World.TryResolvePosition(refName, _agent, out Vec3 pos))
                        {
                            if (_agent.Position.Distance(pos) > 2f)
                                AgentControlHelper.ScriptedMoveToPoint(_agent, pos, false);
                        }
                        if (_timer >= 2f) _phase = AttemptPhase.Rolling;
                    }
                    else
                    {
                        _phase = AttemptPhase.Behind;
                    }
                    break;
                case AttemptPhase.Behind:
                    // 人变体：绕背定位（目标背后盲区 + 可达）
                    if (!_executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent target))
                    {
                        _resultKey = "impossible";
                        _phase = AttemptPhase.Settled;
                        return;
                    }
                    if (!target.IsActive())
                    {
                        _resultKey = "impossible";
                        _phase = AttemptPhase.Settled;
                        return;
                    }
                    float dist = _agent.Position.Distance(target.Position);
                    bool behind = false;
                    try
                    {
                        Vec2 look = target.LookDirection.AsVec2.Normalized();
                        Vec2 toSelf = (_agent.Position - target.Position).AsVec2.Normalized();
                        behind = Vec2.DotProduct(look, toSelf) < -0.4f;
                    }
                    catch { }
                    if (behind && dist <= 2.5f)
                    {
                        _phase = AttemptPhase.Rolling;
                        _timer = 0f;
                    }
                    else if (_timer > 8f)
                    {
                        // 站位不可行 → 诚实报告（没地方下手）
                        _resultKey = "impossible";
                        _phase = AttemptPhase.Settled;
                    }
                    else
                    {
                        // 🔴 2026-08-19（用户裁定：迟迟不动手 → 附近频道内心独白）：绕后卡住 3s
                        // 没进 Rolling → 说一次当前顾虑（被谁看见了；括号 = 内心独白；在线 LLM
                        // 润色 / 离线模板，见 StealHesitationMonologue）——玩家不用干看随从站着不动
                        if (!_monologueSaid && _timer > 3f)
                        {
                            _monologueSaid = true;
                            StealHesitationMonologue.Say(_agent);
                        }
                        // 🔴 2026-08-14（实机：随从扒窃走到目标侧面就原地不动）：原接近点 = target.Position
                        // 直撞碰撞体停在侧/前方，且 ≤2.5m 不在背后时原实现不派发任何移动指令（死等 8s 超时假失败）。
                        // 绕背定位：目标点 = 目标正后方 ~2.2m（≤2.5m 判定圈内，每帧重算，目标转身跟随走位）。
                        // 🔴 2026-08-19（用户质疑：单候选点——正后方 navmesh 不可达（目标贴墙/靠柜台）时
                        // 绕后永远无法执行，干等 8s 假失败）：多候选点逐级尝试
                        //（正后 2.2m → 后左 45° 2.5m → 后右 45° 2.5m → 正后 3.5m），取第一个
                        // navmesh 可站立点；0.25s 节流重新选点（目标转身跟随），其余帧沿用选中点。
                        try
                        {
                            Vec3 look = new Vec3(target.LookDirection.X, target.LookDirection.Y, 0f);
                            Vec3 back = -look;
                            back.z = 0f;
                            if (back.LengthSquared < 0.0001f) back = new Vec3(1f, 0f, 0f);
                            back = back.NormalizedCopy();
                            Vec3 targetPos = target.Position;
                            if (_timer - _behindLastPick > 0.25f)
                            {
                                _behindLastPick = _timer;
                                Vec3 pick = targetPos + back * 2.2f;   // 兜底：默认正后方（不验证；8s 超时兜底诚实报告）
                                var scene = Mission.Current?.Scene;
                                var candidates = new[]
                                {
                                    (back, 2.2f),
                                    (RotateDir(back, 45f), 2.5f),
                                    (RotateDir(back, -45f), 2.5f),
                                    (back, 3.5f),
                                };
                                foreach (var (dir, d) in candidates)
                                {
                                    Vec3 p = targetPos + dir * d;
                                    if (scene != null && !V.NavMesh(scene, p, out _)) continue;
                                    pick = p;
                                    break;
                                }
                                _behindPick = pick;
                            }
                            AgentControlHelper.ScriptedMoveToPoint(_agent, _behindPick, dist > 5f);
                        }
                        catch { }
                    }
                    break;
                case AttemptPhase.Rolling:
                    {
                        // 🔴 2026-08-13（玩家反馈：NPC 扒窃无视觉动作）——玩家扒窃有 UI 条 + 慢动作 +
                        // 屏息叙事；NPC 原来 Rolling→Settled 全程站桩：玩家视角随从站着 3 秒然后报告"偷到了"。
                        // 2026-08-14 起改用引擎下蹲（SetCrouchMode，玩家 Z 键同机制），见下方姿态块注释；
                        // Settled 出口统一收姿。
                        if (!_posed)
                        {
                            _posed = true;
                            try
                            {
                                if (!_variantItem
                                    && _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent poseTarget))
                                    AgentControlHelper.FaceToActor(_agent, poseTarget);
                                // 🔴 2026-08-14（两轮实机：SetCrouchMode 对 Suspend 的 NPC 不渲染）——
                                // flag 需 vanilla AI 消费；改 SetPose 直播蹲姿（act_crouch_walk_idle_unarmed，
                                // 真蹲姿 ID），flag 双保险。背后站位 + 面向目标 = 真"蹲身摸口袋"。
                                // 原 act_pickup_down_begin 弯腰伸手是"没有蹲姿动画"的妥协（属误判）。
                                _agent.SetCrouchMode(true);
                                AgentBrain.SetCrouchPose(_agent, true);   // 人工记录（扒窃蹲姿，native flag 不可信）
                                AgentControlHelper.SetPose(_agent, "act_crouch_walk_idle_unarmed");
                            }
                            catch { }
                        }
                        // 起手延迟：蹲下 ~0.5s 后再摸口袋（对齐玩家扒窃条节奏；判定/结算时机同击晕 0.5s 起手）
                        if (_timer < 0.5f) return;
                        // 目击检查：有目击者（排除扒窃目标）→ 中断
                        var witnesses = StealManager.GetWitnesses(_agent, null, 15f, 120f);
                        if (!_variantItem && !_variantEquipment)
                        {
                            if (_executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent t))
                                witnesses.RemoveAll(w => w == t);
                        }
                        if (witnesses.Count > 0)
                        {
                            _resultKey = "interrupted";
                            _phase = AttemptPhase.Settled;
                            // 🔴 2026-08-20（感知管线统一重构，用户裁定）：未遂中断 = 悄悄收手，零警戒后果
                            // ——「可疑」由目击者 Brain 的蹲姿感知循环（[Brain-Crouch] 0.15/s）自行表达。
                            // 原 2026-08-14 直拍目击者 Steal=1.0 恰好越过 Cautious 阈值 → 全场「抓贼」冒泡
                            //（实机 2026-08-20：随从还没摸到任何东西，弓手就喊「你偷了gold」），且绕过
                            // 感知管线。取消。真正的目击问责只在 roll 成功后由 WitnessCrime 广播承担。
                            return;
                        }
                        // 成功率公式：随从 Roguery vs 目标警觉（§4）
                        // 🔴 2026-08-13（d20 风格全局统一）：掷点 ≥ 目标阈值成功（目标 = 1 − 成功率），概率不变
                        float chance = 0.5f;
                        try
                        {
                            var hero = (_agent.Character as CharacterObject)?.HeroObject;
                            if (hero != null)
                            {
                                float roguery = hero.GetSkillValue(DefaultSkills.Roguery);
                                chance = MathF.Min(0.85f, 0.3f + roguery / 300f * 0.55f);
                            }
                        }
                        catch { }
                        // 🔴 2026-08-14（M2a roll 透明）：掷点值必须与判定用同一个随机数——
                        // 先取 roll 再判定（success = roll ≥ threshold, threshold = 1 − chance）
                        float roll = (float)_rng.NextDouble();
                        _rollValue = roll;
                        _rollThreshold = 1f - chance;
                        _rollRecorded = true;
                        bool success = roll >= _rollThreshold;
                        if (_variantItem)
                        {
                            // 物变体：得手 = 箱子财物（记账语义；箱子无真实库存）
                            if (success)
                            {
                                _amount = 15f + (float)_rng.NextDouble() * 25f;
                                _executor.RecordStolenGold(_amount);
                                _executor.RecordStolenItem("chest_loot", PlanTexts.Loot);
                                _resultKey = "success";
                            }
                            else
                            {
                                _resultKey = "empty";
                            }
                        }
                        else if (_variantEquipment)
                        {
                            // 🔴 2026-08-14（M7 装备变体）：卸目标装备（武器槽优先，削攻最直观）——
                            // StealEquipmentForNpc = 玩家路径 StealSpecificItem 的镜像薄包装
                            //（守恒：目标装备层清空 ↔ 玩家队伍背包 +1；RecordStolen 归还复原共用）
                            if (success)
                            {
                                Agent targetEq = null;
                                _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out targetEq);
                                if (targetEq != null)
                                {
                                    string itemName = StealManager.StealEquipmentForNpc(targetEq);
                                    if (!string.IsNullOrEmpty(itemName))
                                    {
                                        _stolenItemName = itemName;
                                        _resultKey = "success";
                                    }
                                    else
                                    {
                                        _resultKey = "empty";   // 目标身上没有可卸的装备 → 诚实摸空
                                    }
                                }
                                else
                                {
                                    _resultKey = "impossible";
                                }
                            }
                            else
                            {
                                // 🔴 2026-08-14（M7）：装备变体失败 = 目标察觉 → 警戒脉冲（目标警觉，
                                // suspect 指向随从）+ 计划走 abort 撤退（既有路径）+ 专属播报
                                Agent targetEq2 = null;
                                _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out targetEq2);
                                if (targetEq2 != null)
                                {
                                    // 🔴 2026-08-20（感知管线统一重构，用户裁定）：目标察觉 = 受害者自己的脑
                                    // 处理（TheftVictimized 定向直发，量级查表 equipment_fail=2.0）；
                                    // 原直拍 tBrain.SetPulseTarget/AddAlert(2.0) 绕过感知管线，已收敛。
                                    AgentAIController.Instance?.SendEventToAgent(targetEq2, "TheftVictimized",
                                        _agent, targetEq2, "equipment_fail", null);
                                }
                                _resultKey = "empty";
                                _equipmentDetected = true;
                            }
                        }
                        else
                        {
                            // 人变体：得手 = 目标钱包守恒转移（目标有 Hero 才可转移）
                            Agent target2 = null;
                            _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out target2);
                            var targetHero = (target2?.Character as CharacterObject)?.HeroObject;
                            if (!success)
                            {
                                _resultKey = "empty";
                            }
                            else if (targetHero == null)
                            {
                                // 🔴 2026-08-14（M2d 修正）：模板 NPC 目标（无 Hero，如酒馆店主）→ 钱袋路径
                                // StealPurseGold 当场守恒移交（定居点金库扣、玩家钱包加）——分配金 > 0 → success
                                //（整袋端走，无需尾步骤）；分配金 == 0（钱被先摸走/池耗尽）→ 诚实 empty。
                                int purse = StealManager.StealPurseGold(target2);
                                if (purse > 0)
                                {
                                    _amount = purse;
                                    _resultKey = "success";
                                    _executor.MarkGoldHanded();   // 钱已当场移交 → 计划尾 give_gold 步骤跳过（防双移交）
                                }
                                else
                                {
                                    _resultKey = "empty";
                                }
                            }
                            else
                            {
                                _amount = 10f + (float)_rng.NextDouble() * 40f;
                                _executor.RecordStolenGold(_amount);
                                _executor.RecordStolenSource(targetHero);
                                _resultKey = "success";
                            }
                        }
                        // 目击问责（§4.1）：roll 后重新查目击——"得手时被看到"才问责玩家
                        // （roll 前被看到 = interrupted 已返回；此处是得手瞬间的目击者）
                        if (_resultKey == "success")
                        {
                            var finalWitnesses = StealManager.GetWitnesses(_agent, null, 15f, 120f);
                            if (!_variantItem)
                            {
                                if (_executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent t))
                                    finalWitnesses.RemoveAll(w => w == t);
                            }
                            if (finalWitnesses.Count > 0)
                            {
                                // 🔴 2026-08-20（感知管线统一重构，用户裁定）：目击者警戒改走 WitnessCrime
                                // 广播——每个目击者 Brain 自行分类加警戒（+3.0 + 围观 + suspect 化参战打随从）。
                                // 原直拍 SetPulseTarget/AddAlert(3.0) 绕过感知管线（且 targetName 传目击者
                                // 自己的名字是遗留笔误——把嫌疑人填进受害者字段）。广播侧视线过滤（锚点=作案
                                // 随从，15m/120°）与证词名单（GetWitnesses 同款判定）一致；目标背对随从
                                // 收不到广播 = 扒窃不被受害者当场察觉（体感察觉由 TheftVictimized 承担）。
                                Agent stolenFrom = null;
                                if (!_variantItem)
                                    _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out stolenFrom);
                                AgentAIController.Instance?.BroadcastEventInRange(
                                    _agent.Position, 15f, "WitnessCrime",
                                    exclude: null, requireSight: true, isCrime: true,
                                    _agent, stolenFrom);
                                var heroIds = new List<string>();
                                var templateWitness = new Dictionary<string, int>();
                                foreach (var w in finalWitnesses)
                                {
                                    // 🔴 2026-08-20：警戒脉冲已移除（见上）——foreach 只保留证词记账。
                                    var wh = (w.Character as CharacterObject)?.HeroObject;
                                    if (wh != null) heroIds.Add(wh.StringId);
                                    else if (w.Character != null) templateWitness[w.Character.StringId] = 1;
                                }
                                // 🔴 2026-08-14 嫌疑人单一事实源：证词只记账（偷了什么），嫌疑人由目击者脑内
                                // 警戒拉满时的 RegisterWitness 推导（TopSuspectAgent = 作案随从 _agent，
                                // 有名随从锁随从 Hero，无名随从保持 unknown）——不在此处传嫌疑人。
                                // 🔴 2026-08-14（M7）：装备变体证词记装备（itemId=物品 StringId），钱/物变体记 gold
                                if (_variantEquipment && !string.IsNullOrEmpty(_stolenItemName))
                                {
                                    AgentAIController.Instance?.RegisterTheftWitnesses(heroIds, templateWitness,
                                        _stolenItemName, _stolenItemName, targetName: _stolenItemName, count: 1);
                                }
                                else
                                {
                                    AgentAIController.Instance?.RegisterTheftWitnesses(heroIds, templateWitness,
                                        "gold", PlanTexts.Gold, targetName: PlanTexts.Gold, count: (int)_amount);
                                }
                                DebugLogger.Log($"[PlanExecutor] 随从偷窃被目击 → 证词入档（{finalWitnesses.Count} 名目击者，嫌疑人由目击者脑内推导）");
                            }
                            else if (!_variantItem)
                            {
                                // 无人目击扒窃 → 暗账（次日发现，保持 Dormant）
                                AgentAIController.Instance?.RegisterUnwitnessedTheft("gold", PlanTexts.Gold, count: (int)_amount);
                            }
                        }
                        // 保持蹲姿进 Settled（展示窗口）：收姿挪到 Settled 出口统一做——
                        // 原此处 StopAndReset 会把刚下蹲的 Crouch 位清掉（ForceUnlockAgent → SetScriptedFlags(None)）
                        _phase = AttemptPhase.Settled;
                        break;
                    }
                case AttemptPhase.Settled:
                    if (_timer >= 2.0f)
                    {
                        _executor.SetStepResultKey(_resultKey);
                        // 🔴 2026-08-14（M2a）：判定型动作有结局必须有玩家可见播报（对齐击晕 ReportResult，
                        // 补 2026-08-13 击晕已修而偷窃漏掉的同一类修复）
                        ReportResult();
                        // 收姿：解除引擎蹲姿 + 恢复站姿 idle（SetPose 播的蹲姿 StopAndReset 不清，须显式覆盖）+ 复位移动锁
                        try { _agent.SetCrouchMode(false); AgentBrain.SetCrouchPose(_agent, false); AgentControlHelper.SetPose(_agent, "act_walk_idle_unarmed"); AgentControlHelper.StopAndReset(_agent); } catch { }
                        Finished = true;
                    }
                    break;
            }
        }
        /// <summary>🔴 2026-08-14（M2a）：NPC 侧偷窃结局播报（判定/结算已在上方 Rolling 完成；此处只负责
        /// 玩家可见的成败播报）。播报纪律（铁律 17）：empty 带掷点/门槛（玩家要看到败在哪——与击晕失败
        /// 同款信息量）；interrupted 无 ROLL（没到判定环节）。</summary>
        private void ReportResult()
        {
            try
            {
                _executor.MarkResultBroadcast();   // 🔴 2026-08-14（M5）：结局已播 → Finish 不重复播
                string targetName = PlanRefUtil.Normalize(_step.Target, out _) ?? "";   // JToken → 文本（query 形态解包）
                try
                {
                    if (!_variantItem && _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent t))
                        targetName = t.Name?.ToString() ?? targetName;
                }
                catch { }
                string name = _agent?.Name?.ToString() ?? "";
                switch (_resultKey)
                {
                    case "success":
                        if (_variantEquipment)
                        {
                            InformationManager.DisplayMessage(
                                // 本地化：随从偷装备成功播报
                                new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_equip_success",
                                    "{NAME} slipped {ITEM} off {TARGET}.",
                                    ("NAME", name), ("TARGET", targetName), ("ITEM", _stolenItemName ?? "")), Colors.Green));
                        }
                        else
                        {
                            InformationManager.DisplayMessage(
                                // 本地化：随从偷钱成功播报（{GOLD}=货币单位）
                                new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_success",
                                    "{NAME} lifted {AMOUNT}{GOLD} off {TARGET}.",
                                    ("NAME", name), ("TARGET", targetName), ("AMOUNT", ((int)_amount).ToString()),
                                    // 本地化：货币单位词（金）
                                    ("GOLD", LWNTextHelper.ResolveText("LWN_action_gold_unit", " gold"))), Colors.Green));
                        }
                        break;
                    case "empty":
                        // 🔴 2026-08-14（M7）：装备变体失败 = 目标察觉（专属播报 + 警戒脉冲已在 Rolling 置）
                        if (_variantEquipment && _equipmentDetected)
                        {
                            InformationManager.DisplayMessage(
                                // 本地化：随从偷装备失败播报（目标察觉）
                                new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_equip_fail",
                                    "{NAME} tried to disarm {TARGET}, but {TARGET} noticed!",
                                    ("NAME", name), ("TARGET", targetName)), Colors.Red));
                        }
                        else if (_rollRecorded)
                        {
                            InformationManager.DisplayMessage(
                                // 本地化：随从偷窃摸空播报（带 roll 原因）
                                new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_fail_roll",
                                    "{NAME} found nothing on {TARGET}. (Rolled {ROLL} vs threshold {THRESHOLD})",
                                    ("NAME", name), ("TARGET", targetName),
                                    ("ROLL", $"{_rollValue * 100:F0}%"), ("THRESHOLD", $"{_rollThreshold:P0}")), Colors.Gray));
                        }
                        else
                        {
                            InformationManager.DisplayMessage(
                                // 本地化：随从偷窃摸空播报（无 roll——钱袋路径摸空/目标无财可摸）
                                new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_empty",
                                    "{NAME} found nothing on {TARGET}.",
                                    ("NAME", name), ("TARGET", targetName)), Colors.Gray));
                        }
                        break;
                    case "impossible":
                        InformationManager.DisplayMessage(
                            // 本地化：随从偷窃站位不可行播报（绕不到背后）
                            new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_impossible",
                                "{NAME} could not get behind {TARGET} to make a move.",
                                ("NAME", name), ("TARGET", targetName)), Colors.Gray));
                        break;
                    case "interrupted":
                        InformationManager.DisplayMessage(
                            // 本地化：随从偷窃被目击中断播报（未遂撤退）
                            new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_steal_interrupted",
                                "{NAME} was seen and backed off without making a move.",
                                ("NAME", name)), new Color(0.9f, 0.7f, 0.2f)));
                        break;
                }
                DebugLogger.Log($"[PlanExecutor] {name} 偷窃结局: {_resultKey}（目标 {targetName}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] steal 播报异常: {ex.Message}");
            }
        }

        /// <summary>2D 平面旋转（绕 Z 轴，角度制）——绕后候选点偏转（后左/后右 45°）用。</summary>
        private static Vec3 RotateDir(Vec3 dir, float degrees)
        {
            float rad = MathF.PI * degrees / 180f;
            float c = MathF.Cos(rad), s = MathF.Sin(rad);
            return new Vec3(dir.x * c - dir.y * s, dir.x * s + dir.y * c, 0f);
        }
    }
    /// <summary>give_item / give_gold：移交玩家（铁律 4 守恒；Item==null = 金钱走 Hero 转移）。
    /// give_gold "stolen"：从扒窃源 Hero 钱包守恒转移；物变体（箱子记账）→ 直接记到手。</summary>
    public class GiveInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly ActorCursor _cursor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private bool _applied;
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        // 非行为性：守恒转移 = 纯逻辑（不写移动/姿态表现层）
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }
        public GiveInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _cursor = cursor;
            _agent = cursor.Agent;
            _step = step;
            // 目标 = 玩家（铁律：移交对象是玩家）
            Ok = Agent.Main != null;
        }
        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!Ok)
            {
                // 创建失败（目标不可解析）→ 步骤失败，不静默成功（铁律 12 出口要有代价/结果）
                _executor.FailStep(_cursor, _step);
                Finished = true;
                return;
            }
            if (_applied) { Finished = true; return; }
            _applied = true;
            try
            {
                var playerHero = Hero.MainHero;
                if (_step.Action == "give_gold")
                {
                    // 🔴 2026-08-14（M2d 防双移交）：模板 NPC 钱袋路径已当场守恒移交
                    //（StealPurseGold 金库→玩家）→ 本步直接成功跳过，避免 give_gold 双给
                    if (_executor.GoldHanded) { Ok = true; Finished = true; return; }
                    float amount = PlanRefUtil.NumberOr(_step.Amount, 0f);
                    if (_step.Amount != null && _step.Amount.Type == Newtonsoft.Json.Linq.JTokenType.String
                        && (string)_step.Amount == "stolen")
                        amount = _executor.StolenGold;
                    if (amount <= 0f) { Ok = false; Finished = true; return; }   // 没摸到钱 → 该步失败
                    // 守恒转移：扒窃源 Hero 钱包 → 玩家钱包（铁律 4：一方扣一方加）
                    var sourceHero = _executor.StolenSource;
                    if (sourceHero != null && playerHero != null)
                    {
                        AgentControlHelper.TransferGold(sourceHero, playerHero, (int)amount);
                        Ok = true;
                    }
                    else if (playerHero != null)
                    {
                        // 物变体（箱子记账语义）：目标无真实钱包，财物已在 steal_attempt 记入世界侧
                        // （TheftLedger/暗账）——此处为 Grant（虚空 → 玩家，null 显式标注来源），合法非违规
                        AgentControlHelper.SetGold(playerHero, playerHero.Gold + (int)amount);
                        Ok = true;
                    }
                    else
                    {
                        Ok = false;
                    }
                }
                else if (_step.Action == "give_item")
                {
                    // 赃物移交（箱子记账语义：随从把偷到的财物当面交到玩家手上）
                    if (_executor.StolenItem != null)
                    {
                        // 🔴 统一说话框架 + M4 双轨润色：移交赃物台词（前因=give_item）
                        SpeechChannel.SayPolished(_agent, LWNTextHelper.ResolveText("LWN_plan_handover", "Here, take it."),
                            SpeechPriority.Dialogue,
                            SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(_agent), Agent.Main, "give_item", null));
                        InformationManager.DisplayMessage(new InformationMessage(
                            // 本地化：随从移交赃物提示
                            LWNTextHelper.ResolveCompound("LWN_plan_handover_msg", ("NAME", _agent.Name?.ToString() ?? ""), ("ITEM", _executor.StolenItem)), Colors.Green));
                        Ok = true;
                    }
                    else
                    {
                        DebugLogger.Log($"[PlanExecutor] give_item 无赃物记录（{_agent.Name}）→ 该步失败");
                        Ok = false;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] {_step.Action} 失败: {ex.Message}");
                Ok = false;
            }
            if (!Ok)
            {
                // 移交失败（没摸到钱/无源可转）→ 步骤失败路径（on_timeout/abort），不静默成功
                _executor.FailStep(_cursor, _step);
            }
            Finished = true;
        }
    }
    /// <summary>deliver_item：送物（v2；M4 后落地）。</summary>
    public class DeliverInlineState : IInlineStep
    {
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }
        public DeliverInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            Ok = false;   // 未实现 → 步骤失败走 on_timeout/失败路径，不静默
            DebugLogger.Log($"[PlanExecutor] deliver_item 在 v2 排期，当前失败");
        }
        public void OnTick(float dt) { Finished = true; }
    }
    /// <summary>knockout：背后击晕（复用击晕轮子：ForcePlayAction + event_agent_knocked_out + 袭击记账）。
    /// 成功 = 目标击晕（GOAL/knocked_out 谓词判定）；失败 = 目标反击（event_agent_damaged）→ 本步失败走 on_timeout/abort。</summary>
    public class KnockoutInlineState : IInlineStep
    {
        private enum KPhase { Approach, Strike, Settled }
        private KPhase _phase = KPhase.Approach;
        private float _timer;
        private bool _struck;
        private KnockoutFlow.RollResult _roll;   // 共享管线掷点结果（Strike 起手前判定，起手后结算）
        private readonly PlanExecutor _executor;
        private readonly ActorCursor _cursor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private bool _interrupted;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }
        // 🔴 行为性内联（M0/D3）：ScriptedMoveToPoint/ForcePlayAction 直接驱动表现层 → 经 InlinePlanAction 入队由脑驱动
        public bool IsBehavioral => true;
        public bool Interrupted => _interrupted;
        /// <summary>中断：标记使 Finished 立即为真（脑下一帧自清出队，不再执行击晕动作）。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }
        public KnockoutInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _cursor = cursor;
            _agent = cursor.Agent;
            _step = step;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolveAgent(refName, cursor.Agent, out _))
            {
                Ok = false;
                return;
            }
            Ok = true;
        }
        public void OnTick(float dt)
        {
            if (Finished || !Ok) return;
            _timer += dt;
            if (_agent == null || !_agent.IsActive()) { Finished = true; return; }
            string refName = PlanRefUtil.Normalize(_step.Target, out string q);
            if (q != null) refName = q;
            if (!_executor.World.TryResolveAgent(refName, _agent, out Agent target) || !target.IsActive())
            {
                // 目标已离场 → 本步失败（不静默）
                _executor.FailStep(_cursor, _step);
                Finished = true;
                return;
            }
            switch (_phase)
            {
                case KPhase.Approach:
                    // 接近目标（绕背盲区）
                    // 🔴 2026-08-13（通用接近语义）：ApproachAgent = 距离 >5m 跑、≤5m 走——
                    // 原实现全程走速，50 米目标撞 30s 默认超时（日志实锤「拖太久没成」）；
                    // 近身 5m 内放慢脚步（偷袭收势）。跑到位后转 Strike 由 ForcePlayAction 接管表现。
                    float dist = _agent.Position.Distance(target.Position);
                    if (dist > 1.8f)
                    {
                        AgentControlHelper.ApproachAgent(_agent, target);
                    }
                    else
                    {
                        _phase = KPhase.Strike;
                        _timer = 0f;
                    }
                    break;
                case KPhase.Strike:
                    if (!_struck)
                    {
                        _struck = true;
                        // 判定 + 挥击起手（共享管线 KnockoutFlow，NPC 上限 0.85；玩家路径同节奏：
                        // 播动画 → 起手延迟 → 结算。原来一到位就瞬间结算，玩家视角像隔空施法）。
                        _roll = KnockoutFlow.Roll(_agent, target, maxRate: 0.85f);
                        KnockoutFlow.PlayStrikeAnim(_agent, target);
                    }
                    if (_timer >= 0.5f)
                    {
                        // 挥击起手 ~0.5s（与玩家路径 400ms 同量级）→ 落点结算
                        _phase = KPhase.Settled;
                        _timer = 0f;
                        // 结算（记账/击晕落地/反击/目击广播——共享管线）+ NPC 视角播报
                        KnockoutFlow.Resolve(_agent, target, _roll);
                        ReportResult(target);
                    }
                    break;
                case KPhase.Settled:
                    // 判定：目标已击晕（GOAL 由谓词检查）；未击晕 → 反击已触发 → 本步失败
                    if (AgentBrain.IsKnockedOut(target))
                    {
                        _executor.IncrementKnockoutCount();
                        Finished = true;
                    }
                    else if (_timer >= 1.5f)
                    {
                        // 没打晕 → 目标已反击（event_agent_damaged）→ 计划性失败走 abort
                        _executor.FailStep(_cursor, _step);
                        Finished = true;
                    }
                    break;
            }
        }
        /// <summary>NPC 视角播报（结算已由 KnockoutFlow.Resolve 完成：记账/落地/反击/目击广播；
        /// 本方法只负责玩家可见的成败播报——第一人称 vs 第三人称文案差异留在壳层）。</summary>
        private void ReportResult(Agent target)
        {
            try
            {
                _executor.MarkResultBroadcast();   // 🔴 2026-08-14（M5）：结局已播 → Finish 不重复播
                if (_roll.Success)
                {
                    DebugLogger.Log($"[PlanExecutor] {_agent.Name} 击晕了 {target.Name}");
                    // 🔴 2026-08-13（玩家反馈）：NPC 执行击晕成败必须可见——玩家自己击晕有播报，
                    // 随从击晕原来只有 DebugLogger（日志实锤玩家分不清成功还是进战斗）
                    InformationManager.DisplayMessage(
                        // 本地化：随从击晕成功播报
                        new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_knockout_success",
                            "{NAME} knocked {TARGET} out from behind!",
                            ("NAME", _agent.Name?.ToString() ?? ""), ("TARGET", target.Name?.ToString() ?? "")), Colors.Green));
                }
                else if (_roll.IsChild)
                {
                    // 儿童免疫：管线内不反击、目标躲开 → 随从本步失败（Settled 判定走 abort）
                    DebugLogger.Log($"[PlanExecutor] {_agent.Name} 偷袭小孩失败，{target.Name} 躲开");
                    InformationManager.DisplayMessage(
                        // 本地化：小孩躲开击晕提示（与玩家路径同文案）
                        new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_target_dodged",
                            ("NAME", target.Name?.ToString() ?? "")), Colors.Gray));
                }
                else
                {
                    // 失败：目标察觉反击（反击事件已由 KnockoutFlow.Resolve 发出）
                    DebugLogger.Log($"[PlanExecutor] {_agent.Name} 击晕失败，{target.Name} 反击");
                    // 🔴 2026-08-13（玩家反馈）：失败 = 目标察觉反击 → 红字播报（随后计划 abort 还有
                    // 「打起来了，先撤！」黄字，两者并存：先见失败原因，再见撤离决定）
                    // 🔴 2026-08-13（roll 透明）：带 ROLL/THRESHOLD 参数——玩家要看到败在哪
                    //（掷点 94% < 目标 55%），与玩家自己击晕的失败播报同款信息量（d20：掷点 ≥ 目标成功）
                    InformationManager.DisplayMessage(
                        // 本地化：随从击晕失败播报（目标察觉反击 + roll 原因）
                        new InformationMessage(LWNTextHelper.ResolveCompound("LWN_npc_knockout_fail_retaliate",
                            "{NAME} failed to knock {TARGET} out — {TARGET} sensed it and strikes back! ({ROLL} < {THRESHOLD})",
                            ("NAME", _agent.Name?.ToString() ?? ""), ("TARGET", target.Name?.ToString() ?? ""),
                            ("ROLL", $"{_roll.Roll * 100:F0}%"), ("THRESHOLD", $"{_roll.Threshold:P0}")), Colors.Red));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] knockout 播报异常: {ex.Message}");
            }
        }
    }
    /// <summary>
    /// 🔴 2026-08-14（npc-risk-aware-planning.md M6）：ask_help 多随从分头配合——
    /// 执行人 A 的计划请求同袍 B 执行单个低危动作（引开/望风/手势示意），自己继续主任务。
    /// 通信类内联（非行为性，留排序器侧）：构造即发 assist_request 事件给目标 B 的 agent
    /// （Args：请求动作码 + 目标文本 + 请求者）；2s 收尾（请求已发出，等待由步骤 on_event
    /// 通道负责——事件消费在步骤级，与 inline 存活无关）。
    /// B 侧：AgentBrain 收到 assist_request → 白名单校验 → ChatActionFlow 单步执行 + 完成回执
    /// assist_done → A 的 ask_help/wait 步骤 on_event: assist_done 继续。
    /// 超时兜底：ask_help 步骤的 on_timeout 由计划轮生成时写好（prompt 示范），执行器无魔法。
    /// v1 白名单 = make_noise/follow/emote（低危单动作；配合者不生成计划、不风险审视）。
    /// </summary>
    public class AskHelpInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private float _timer;
        private bool _sent;
        private bool _interrupted;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }
        // 非行为性：通信（发事件）不写移动/姿态表现层
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }
        /// <summary>配合动作白名单（v1 刻意收窄：低危单动作——引开=喊叫 isCrime:false、望风=跟随、
        /// 手势=emote 白名单 9 动画。高危配合动作（帮忙击晕/偷）v2 再说）。</summary>
        public static readonly HashSet<string> AssistWhitelist = new HashSet<string>(StringComparer.Ordinal)
            { "make_noise", "follow", "emote" };
        public AskHelpInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _agent = cursor.Agent;
            _step = step;
            _sent = false;
            Ok = true;
        }
        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!_sent)
            {
                _sent = true;
                try
                {
                    // 白名单校验（请求动作码由 LLM 填 step.Variant——复用既有变体字段，零模型改动）
                    string assistAction = _step.Variant;
                    if (string.IsNullOrEmpty(assistAction) || !AssistWhitelist.Contains(assistAction))
                    {
                        DebugLogger.Log($"[PlanExecutor] ask_help 动作码非法/缺失: {assistAction ?? "null"}（白名单: make_noise/follow/emote）→ 等待 on_timeout 兜底");
                        return;
                    }
                    // 目标解析（同袍名字 → agent，快照同口径）
                    string refName = PlanRefUtil.Normalize(_step.Target, out string query);
                    if (query != null) refName = query;
                    if (!_executor.World.TryResolveAgent(refName, _agent, out Agent buddy) || buddy == null || !buddy.IsActive())
                    {
                        DebugLogger.Log($"[PlanExecutor] ask_help 目标解析失败: {refName} → 等待 on_timeout 兜底");
                        return;
                    }
                    // B 空闲校验（无计划/无战斗/非昏迷——忙碌 → 忽略 + on_timeout 兜底）
                    var buddyBrain = AgentAIController.GetBrainForAgent(buddy);
                    if (buddyBrain == null || buddyBrain.IsInCombat
                        || AgentBrain.IsKnockedOut(buddy)
                        || (buddyBrain.CurrentIntent != null && buddyBrain.CurrentIntent.Type == NpcIntentType.ExecutingCommand))
                    {
                        DebugLogger.Log($"[PlanExecutor] ask_help 目标 {buddy.Name} 忙碌（战斗/昏迷/执行中）→ 等待 on_timeout 兜底");
                        return;
                    }
                    // 发送配合请求（Args：请求动作码 + 目标文本 + 请求者 agent）
                    AgentAIController.Instance?.SendEventToAgent(buddy, "assist_request",
                        assistAction, _step.Target, _agent);
                    DebugLogger.Log($"[PlanExecutor] {_agent.Name} ask_help → {buddy.Name}（动作 {assistAction}，目标 {_step.Target}）");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[PlanExecutor] ask_help 发送失败: {ex.Message}");
                }
            }
            _timer += dt;
            // 2s 收尾：请求已发出（通信类动作不留存；assist_done 事件在步骤级消费，与 inline 存活无关）
            if (_timer >= 2f) Finished = true;
        }
    }

    /// <summary>
    /// 🔴 2026-08-15（ask_player 询问步骤）：向玩家投递密信决策卡（撤退/强制执行）后**保持等待**——
    /// 不自行收尾：玩家点击按钮 → 事件回投执行器（NotifyDecisionEvent）→ 本步骤 on_event 路由跳转
    ///（TickCursor 事件通道在 OnTick 前消费）；超时未答 → 步骤级超时（on_timeout / @abort_gracefully
    /// = 默认撤退语义）。与 AskHelpInlineState 同构：通信类内联（非行为性，排序器侧）。
    /// </summary>
    public class AskPlayerInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly PlanStep _step;
        private bool _sent;
        private bool _interrupted;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }
        public bool IsBehavioral => false;
        public bool Interrupted => _interrupted;
        /// <summary>防御实现。</summary>
        public void Interrupt() { _interrupted = true; Finished = true; }

        public AskPlayerInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!_sent)
            {
                _sent = true;
                _executor?.AskPlayer(_step.Text ?? "");
            }
            // 不 Finished：持续等待玩家决策（事件/超时由步骤级机制处理）
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SandBox.Conversation.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using SandBox;
using SandBox.Missions.AgentBehaviors;
#pragma warning disable CS0618 // Intentional migration: uses deprecated NpcInitiative + PrepareOpeningAction old ctors
namespace LivingWorldNpcs
{
    // 事件数据包，可以携带任何参数
    public struct AIEvent
    {
        /// <summary>字段初始化器需要显式构造函数（C# 12 struct 规则）；空构造 = 默认语义。</summary>
        public AIEvent() { }

        public string EventType; // 例如 "WitnessCrime", "AttackOrder"
        public object Sender;    // 谁发的
        public object[] Args;    // 参数 (目标ID, 坐标等)
        /// <summary>事件是否算犯罪（2026-08-13 泛化：true=任何嫌疑犯都走 WitnessCrime 分类；
        /// false=非犯罪事件仅围观）。仅两个调用点传 false：make_noise（喊一嗓子）、NPC 投降广播。</summary>
        public bool IsCrime = true;
    }

    public class AgentBrain
    {
        /// <summary>已暂停原版 AI 的 Agent.Index 集合。AiSuspendPatch 读取以拦截 Navigator。</summary>
        internal static readonly HashSet<int> SuspendedAgentIndices = new HashSet<int>();

        // 🔴 2026-08-16（K1 血线关切，事件驱动）：静态共享状态——多个随从同时收到玩家受击广播，
        // 第一个触发后档位标记挡住其余（单喊）；墙钟秒冷却跨 Mission 天然正确（Mission 时间会归零）；
        // 回血 ≥0.7 由事件检查自动重置（玩家满血受击 → 高血线分支清标记）。
        private static double CareCooldownUntilWall;
        private static bool CareLowTriggered;      // 挂彩档（<0.6）已触发
        private static bool CareHeavyTriggered;    // 重伤档（<0.35）已触发



        // ═══════════════════════════════════════════════════════════════
        // 🆕 NpcIntent — NPC 高层意图状态机
        // ═══════════════════════════════════════════════════════════════

        private NpcIntent _currentIntent = new NpcIntent(NpcIntentType.None);
        private NpcIntent _previousIntent;
        // 🔴 2026-08-15(用户需求:HUD 意图文本可排查):上次打日志的 HUD 文本——
        // SetNpcIntent 只在 HUD 渲染文本变化时才打日志(防高频重复刷屏)
        private string _lastIntentHudLog;

        /// <summary>NPC 当前高层意图。只读，变更必须走 SetNpcIntent。</summary>
        public NpcIntent CurrentIntent => _currentIntent;

        /// <summary>上一个意图。只读，用于回退（如 refuse 后回到 Fighting）或调试。</summary>
        public NpcIntent PreviousIntent => _previousIntent;

        /// <summary>
        /// 对话结束后需要统一清理（投降谈判成功路径：交钱/求饶成功/威胁成功/accept/humiliate/ransom）。
        /// EndConversation Postfix 检查此标记 → PostConversationCleanup() 清大脑 + 恢复原版 AI。
        /// 谈判破裂路径（event_surrender_refused）将此标记翻为 false，阻止 PostConversationCleanup 误清理
        /// 刚入队的 FightEnemyAction。
        /// </summary>
        internal bool PendingPostConversationCleanup;


        public Agent Owner { get; private set; }
        public SingNpcMemorySystem _memory;
        public Agent InteractedAgent { get; set; } // 最近一次交互的对象
        // --- 通用随从属性 ---
        public Agent Leader { get; private set; } // 我的老大是谁？
        private bool _isGuardMode = true; // 是否开启护卫模式

        // 动作队列：支持行为链，比如 [走到点] -> [看向玩家] -> [说话]
        private Queue<IAtomicAction> _actionQueue = new Queue<IAtomicAction>();
        private IAtomicAction _currentAction = null;
        public IAtomicAction CurrentAction => _currentAction;
        public bool IsInStayMode => IsCurrentOrPending<StayAction>();

        /// <summary>是否处于战斗行为（当前或排队）——HUD 用它决定战斗中不显示警戒眼</summary>
        public bool IsInCombat => IsCurrentOrPending<FightEnemyAction>();
                /// <summary>查询任意 Agent 是否处于击晕 StayAction 状态。</summary>
        /// <summary>是否处于击晕状态（专用标记，避免依赖 CurrentAction 时序问题）</summary>
        internal bool IsStunned;

        /// <summary>
        /// 当前有效行为：_currentAction 不为 null 就返回它，否则 fallback 到队列头。
        /// 代表"NPC 此刻在做什么或马上就要做什么"，用于需要读 Action 属性的场景。
        /// 返回 null 表示大脑完全空闲（无当前动作、无排队）。
        /// </summary>
        private IAtomicAction EffectiveAction
            => _currentAction ?? (_actionQueue.Count > 0 ? _actionQueue.Peek() : null);

        /// <summary>
        /// 判断 NPC 当前行为意图是否为指定类型。
        /// </summary>
        public bool IsCurrentOrPending<T>() where T : IAtomicAction
            => EffectiveAction is T;

        // ═══════════════════════════════════════════════════════════════
        // 🆕 警戒值系统（Phase 1-2）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>我对玩家的分类警戒值明细。一个字典替代原来的 _alertBreakdown + _pulseContext。</summary>
        private Dictionary<PlayerActionType, AlertEntry> _alertBreakdown = new Dictionary<PlayerActionType, AlertEntry>();

        /// <summary>上一帧的警戒阶段（用于检测穿越，包括向下穿越）</summary>
        private AlarmPhase _lastAlertPhase = AlarmPhase.Normal;

        /// <summary>脉冲抑制截止时间（Mission.Current.CurrentTime），0=无抑制</summary>
        private float _pulseSuppressedUntil;

        /// <summary>已触发过 BubbleSay 的 (Action, Phase) 组合。降级后清零对应条目，允许重新触发。</summary>
        private HashSet<(PlayerActionType, AlarmPhase)> _bubbledPhases = new HashSet<(PlayerActionType, AlarmPhase)>();

        /// <summary>警戒认知更新间隔（秒）。默认 0.1s = 100ms。设 0 退化为逐帧。</summary>
        private float _alertCognitionInterval = 0.1f;

        /// <summary>认知更新计时器（累积 dt），达到 _alertCognitionInterval 时触发一次 UpdateAlertCognition 然后归零。</summary>
        private float _alertCognitionTimer;

        // ── 全局质问锁（同一时间只有一个 NPC 能质问玩家）──
        /// <summary>当前正在质问玩家的 Brain。null 表示无人质问中。</summary>
        internal static AgentBrain ConfrontingBrain;

        /// <summary>当前 L3 质问关联的 WorldEvent ID（从 PendingWorldEvent 派生）。null = 非 Misconduct 路径。</summary>
        /// 这个和PendingConflict定位相同，需要合并
        public string CurrentMisconductEventId => AgentAIController.Instance?.PendingWorldEvent?.EventId;

        // ── 公开查询（AgentHudMissionView 每帧读 AlertValue / AlertPhase）──

        //实时计算的属性，而非存储字段
        public float AlertValue
        {
            get
            {
                float sum = 0f;
                foreach (var e in _alertBreakdown.Values) sum += e.Value;
                return sum;
            }
        }

        public AlarmPhase AlertPhase => AlertValue switch
        {
            >= 2.0f => AlarmPhase.Alarmed,  //警戒
            >= 1.0f => AlarmPhase.Cautious, //谨慎
            >= 0.25f => AlarmPhase.Suspicious, //好奇
            _ => AlarmPhase.Normal
        };

        /// <summary>当前最高警戒值对应的行为类型。BubbleSayOnce 内部用（阶段转换时调用，非每帧）。</summary>
        public PlayerActionType? PrimaryAction
        {
            get
            {
                if (_alertBreakdown.Count == 0) return null;
                PlayerActionType best = PlayerActionType.Crouching;
                float bestVal = -1f;
                foreach (var kv in _alertBreakdown)
                {
                    if (kv.Value.Value > bestVal)
                    {
                        bestVal = kv.Value.Value;
                        best = kv.Key;
                    }
                }
                return best;
            }
        }
        public IReadOnlyDictionary<PlayerActionType, AlertEntry> AlertBreakdown
        {
            get
            {
                if (_alertBreakdown.Count == 0) return null;
                return _alertBreakdown;
            }
        }

        public AgentBrain(Agent agent)
        {
            Owner = agent;
            _memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
        }
       
        public void SetGuardMode(bool isGuard)
        {
            _isGuardMode = isGuard;
        }

        /// <summary>儿童（monster StringId 含 "child"，如 human_child）引擎级非战斗人员：任何进入战斗的流程都替换为恐惧逃离，不参战。</summary>
        private bool IsChildOwner => Owner != null && Owner.Monster != null && Owner.Monster.StringId?.Contains("child") == true;
        public void SetLeader(Agent newLeader)
        {
            if (newLeader == Owner) return; // 不能认自己做老大
            Leader = newLeader;
        }
        
        /// <summary>
        /// 设置 NPC 当前意图，同时记录上一个意图。
        /// 所有意图变更必须走此方法，类内部也不允许直接写 _currentIntent。
        /// </summary>
        public void SetNpcIntent(NpcIntentType type, Agent target = null, ConfrontationType? interceptDetail = null, CommandIntentType? commandDetail = null)
        {
            _previousIntent = _currentIntent;
            _currentIntent = new NpcIntent(type, target, interceptDetail, commandDetail);
            // 🔴 2026-08-15(用户需求:HUD 意图文本可排查):意图变更打日志——记录 AgentHudVM 实际
            // 渲染的意图文本(它用 intent.ToString() 渲染 NpcIntentDebugText),HUD 文本变化才打
            // (意图类型/目标/参数任一变化都会改变显示,防高频重复刷屏)。
            try
            {
                string hudText = _currentIntent != null && _currentIntent.Type != NpcIntentType.None
                    ? _currentIntent.ToString() : "";
                if (hudText != _lastIntentHudLog)
                {
                    _lastIntentHudLog = hudText;
                    DebugLogger.Log($"[Brain-Intent] {Owner?.Name}(Idx={Owner?.Index}) 意图: {_previousIntent?.Type} → {type} | HUD 文本: \"{hudText}\"");
                }
            }
            catch { }
        }

        /// <summary>计划收尾 → 意图复位为 None（2026-08-11 修正：不再恢复 Following）。
        /// DecideDefaultBehavior 的护卫跟随逻辑已注释（跟随由原版玩家命令/原版 AI 接管），
        /// 恢复 Following 意图无对应动作，HUD 却显示"跟随中"——误导。计划结束 = 回到无意图状态，
        /// 脑空 → DecideDefaultBehavior → ResumeVanillaAI 由原版 AI 接管。
        /// 🔴 守卫保留：仅当没有新命令覆盖意图（仍为 ExecutingCommand）时才复位——计划收尾前
        /// 玩家已下新命令（order_follow 等）时不得覆盖新意图。</summary>
        internal void OnPlanExecutorFinished(PlanExecutor executor)
        {
            try
            {
                if (Owner == null || !Owner.IsActive()) return;
                if (_currentIntent != null && _currentIntent.Type == NpcIntentType.ExecutingCommand)
                    SetNpcIntent(NpcIntentType.None);
            }
            catch { }
        }

        /// <summary>
        /// 经历旁白写入（2026-08-11）：→ SingNpcMemorySystem.NarrationLog（会话级）。
        /// 只记真实发生的经历（出队翻译/事件事实）；内容 = 第一人称 LLM prompt 材料（豁免铁律 13），
        /// 不渲染为 IM 聊天行（GetDirectMessages 只认 im_user/im_npc 角色）。
        /// </summary>
        internal void RecordNarration(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || _memory == null) return;
            _memory.RecordNarration(line);
            DebugLogger.Log($"[Narration] {Owner?.Name}(Idx={Owner?.Index}) 旁白: {line}");
        }

        /// <summary>
        /// 动作开始执行时的经历翻译（2026-08-11）：只在动作真正开始执行时记录——**无幽灵**
        /// （队列里被 ClearAllActions 丢弃的动作永远不会出队）。
        /// 🔴 唯一调用方 = 脑队列出队（Tick 内）。单脑化重构（M2）后密谋子动作也走脑队列
        /// （ExecutePlanAction 占位已删，行为步骤由执行器 EnqueuePlanAction 入队），
        /// 执行器侧不再有 OnStart 旁路调用点。
        /// 旁白定义在**每个动作自身**（IAtomicAction.GetNarration，见 AtomicAction.cs 各动作定义处）——
        /// 机械动作返回 null 零噪声；新增动作只改自身定义处，本方法零改动。
        /// "被攻击"等事件事实不在此记录（事件层 handler 直记，见 event_agent_damaged）。
        /// </summary>
        internal void RecordActionNarration(IAtomicAction action)
        {
            if (action == null) return;
            string line = action.GetNarration(Owner);
            if (string.IsNullOrWhiteSpace(line)) return;
            RecordNarration(line);
        }


        /// <summary>ReactiveAgent 反应通道（§6）：清当前行为并入队反应动作。
        /// 支持多动作顺序组合（params，附章③ 2026-08-11：follow_for_a_bit 拆"跟走 + 折返"两步入队）。
        /// 与 ReceiveEvent 内联分支同权——ReactiveAgent 是 brain 事件处理的内部扩展。</summary>
        internal void RunReactiveAction(params IAtomicAction[] actions)
        {
            if (actions == null || actions.Length == 0 || !Owner.IsActive()) return;
            ClearAllActions();
            foreach (var a in actions)
            {
                if (a != null) EnqueueAction(a);
            }
        }

        /// <summary>计划行为步骤入队（PlanExecutor M2 用，单脑化重构 D1/D4b）：
        /// 执行器只负责排序，动作生命周期归脑——OnStart/OnTick/OnEnd/IsFinished 全由脑驱动。
        /// 🔴 纯入队唯一 internal 入口（2026-08-11）：重构前 plan_debug 用的 EnqueueActionInternal
        /// 在 plan_debug 改走 executor.Start 后失去调用方，已删除，不再开第二个纯入队薄壳。</summary>
        internal void EnqueuePlanAction(IAtomicAction action)
        {
            if (action == null) return;
            EnqueueAction(action);
        }

        /// <summary>动作是否仍由本脑持有（当前执行中或排队中）。PlanExecutor 外部清除检测用（D4）：
        /// 返回 false 且动作未完成 = 被 ClearAllActions 清掉（战斗/护主/击晕/搭话）= 计划中止。</summary>
        internal bool IsActionAlive(IAtomicAction action)
        {
            if (action == null) return false;
            if (_currentAction == action) return true;
            return _actionQueue.Contains(action);
        }

        public static bool IsKnockedOut(Agent agent)
        {
            if (agent == null) return false;
            var brain = AgentAIController.GetBrainForAgent(agent);
            // 优先检查专用标记（CurrentAction 可能尚未出队，有时序问题）
            if (brain?.IsStunned == true) return true;
            return brain?.CurrentAction is StayAction stay && stay.IsKnockout;
        }
        // --- 核心：决策中枢 ---
        public void ReceiveEvent(AIEvent aiEvent)
        {
            // 战斗模式下不处理任何事件——原生 AI 接管所有战斗行为
            if (Settings.Instance.IsInteractionDisabled())
                return;

            // 🔴 纵深防御：玩家不该有 brain（OnAgentCreated 已排除），万一存在（边缘路径）
            // 也绝不处理玩家的事件——护主/参战链会把玩家当 NPC：BubbleSay NPC 台词 +
            // Suspend 玩家导致整场无法移动（2026-08-09 致命 bug 修复）。
            if (Owner == Agent.Main)
                return;

            // 通用事件追踪；event_agent_damaged 由下方受害分支专用日志接管（含 victim/是否自己，
            // 2026-08-14 去重：原两条 [Brain-Receive] 同事件双打，噪音且难 grep）
            if (aiEvent.EventType != "event_agent_damaged")
                DebugLogger.Log($"[Brain-Receive] {Owner.Name}(Idx={Owner.Index}) 收到事件 '{aiEvent.EventType}' | 当前行为={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count} | 阶段={_lastAlertPhase}");

            // ── ReactiveAgent 触发词分发（密谋命令系统 §6）──
            // 被叫方/对手方的人格演算：speaker 请求 → 演算 → 反应动作 + 决策结果广播。
            // 任何触发词都被消费（不再走下方既有分支）。
            if (ReactiveAgent.IsTriggerEvent(aiEvent.EventType))
            {
                ReactiveAgent.TryHandleEvent(this, aiEvent);
                return;
            }

            if (aiEvent.EventType == "ComeHere")
            {
                Agent targetAgent = (Agent)aiEvent.Args[0];
                SetNpcIntent(NpcIntentType.Interacting, Agent.Main);
                // 🔴 统一说话框架 + M4 双轨润色：被喊名字回应（前因=spoken_to）
                SpeechChannel.SayPolished(Owner,
                    // 冒泡回复：被喊名字时的回应（{NAME}=喊话的人）
                    LWNTextHelper.ResolveCompound("LWN_brain_comehere_reply", ("NAME", targetAgent.Name)),
                    SpeechPriority.Dialogue,
                    SpeechContext.FromBrain(this, targetAgent, "spoken_to", null));
                InteractedAgent = targetAgent;
                ClearAllActions();
                EnqueueAction(new LookAtAction(targetAgent, 0.3f));
                // 🔴 目标=玩家但语义是"走到面前说话"，不可换 VanillaFollowAction（2026-08-13 用户裁定）：
                // ① WaitForAgentToSettle 依赖 CurrentAction is StayAction 判定到位，换瞬时动作会破坏对话前走位；
                // ② 对话结束 Resume 后原版跟随会接管 → NPC 每次被喊过来都永远跟随（错误语义）。
                EnqueueAction(new FollowAgentAction(targetAgent, false, radius: 2.0f, angleOffset: 0f, stopDistance: 1.0f));
                //EnqueueAction(new LookAtAction(targetAgent, 0.5f));
                EnqueueAction(new StayAction(targetAgent));
            }
            if(aiEvent.EventType == "order_follow")
            {
                Agent targetAgent = (Agent)aiEvent.Args[0];
                SetNpcIntent(NpcIntentType.Following, targetAgent);
                InteractedAgent = targetAgent;
                ClearAllActions();
                // 🔴 2026-08-13 用户裁定：跟随目标=玩家 → 挂原版持续跟随（FollowAgentBehavior 三连），
                // Brain 队列清空后依然跟随；解除靠 stop_following。目标非玩家 → 自研 keepFollow 跟随（防御）。
                EnqueueAction(targetAgent == Agent.Main
                    ? (IAtomicAction)new VanillaFollowAction(Agent.Main)
                    : new FollowAgentAction(targetAgent, run: true, keepFollow: true));

            }
            // ── 密谋命令系统：计划执行（§5.4 执行通道）──
            if (aiEvent.EventType == "order_execute_plan")
            {
                string planJson = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as string : null;
                if (string.IsNullOrEmpty(planJson)) return;
                string intentType = aiEvent.Args.Length > 1 ? aiEvent.Args[1] as string : null;
                Agent target = aiEvent.Args.Length > 2 ? aiEvent.Args[2] as Agent : null;

                Plan plan = null;
                try
                {
                    plan = JsonConvert.DeserializeObject<Plan>(LLMService.CleanJson(planJson));
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Brain] order_execute_plan 解析失败: {ex.Message}");
                    return;
                }
                var executor = PlanExecutor.Create(Owner, plan, intentType, null);
                if (executor == null)
                {
                    // 本地化：计划校验未通过 → 诚实回应（词表外命令拒绝）
                    // 🔴 统一说话框架 + M4 双轨润色：计划拒绝（前因=order_execute_plan）
                    SpeechChannel.SayPolished(Owner, LWNTextHelper.ResolveText("LWN_plan_reject", "I cannot do this."),
                        SpeechPriority.Dialogue,
                        SpeechContext.FromBrain(this, Agent.Main, "order_execute_plan", null));
                    return;
                }
                SetNpcIntent(NpcIntentType.ExecutingCommand, target,
                    commandDetail: PlanExecutor.ParseIntentType(intentType ?? plan?.Intent?.IntentType));
                InteractedAgent = target;
                ClearAllActions();
                // D1（单脑化重构）：占位动作 ExecutePlanAction 整个删除——执行器直接启动（不入队），
                // 行为步骤由执行器逐步入队（EnqueuePlanAction → 生命周期归脑）。
                // "计划执行中"哨兵 = ExecutingCommand 意图（D2 空窗守卫：脑空时不跑 DecideDefaultBehavior，
                // 收尾 OnPlanExecutorFinished 意图复位 None 自动放行）。
                executor.Start(Owner);
                // 计划收尾 → 意图复位 None；仅当没有新命令覆盖当前意图时
                var exRef = executor;
                executor.OnFinished += e => OnPlanExecutorFinished(exRef);
                // Replan 接线（原命令 + 意外重入，§7.2）
                string originalCommand = aiEvent.Args != null && aiEvent.Args.Length > 4 ? aiEvent.Args[4] as string : null;
                PlanReplan.Wire(executor, originalCommand, intentType);
            }
            // ── 密谋命令系统：ReactiveAgent 决策结果广播（§5.4 事件通道）──
            if (aiEvent.EventType == "plan_decision")
            {
                string decisionType = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as string : null;
                if (string.IsNullOrEmpty(decisionType)) return;
                var exec = PlanExecutor.GetExecutorFor(Owner);
                exec?.NotifyDecisionEvent(decisionType);
            }
            // ── 多随从分头配合（2026-08-14 M6，npc-risk-aware-planning.md）：──
            // 执行人 A 的 ask_help 步骤请求同袍 B 执行单个低危动作（白名单 make_noise/follow/emote）。
            // B 侧：空闲校验（无计划/无战斗/非昏迷）→ 冒泡「交给我」→ ChatActionFlow 单步执行
            //（复用免确认直发通道）→ 完成回调发 assist_done 回执给请求者 A（A 的步骤 on_event 继续）。
            // 忙碌 → 忽略（A 的 ask_help 步骤 on_timeout 兜底，计划轮生成时写好）。
            if (aiEvent.EventType == "assist_request")
            {
                string assistAction = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as string : null;
                string assistTarget = aiEvent.Args.Length > 1 ? aiEvent.Args[1] as string : null;
                Agent requester = aiEvent.Args.Length > 2 ? aiEvent.Args[2] as Agent : null;
                if (string.IsNullOrEmpty(assistAction)
                    || !AskHelpInlineState.AssistWhitelist.Contains(assistAction)) return;
                // 配合者空闲校验（v1 范围：不生成计划、不风险审视——白名单低危单动作）
                if (IsInCombat || AgentBrain.IsKnockedOut(Owner)
                    || (_currentIntent != null && _currentIntent.Type == NpcIntentType.ExecutingCommand))
                {
                    DebugLogger.Log($"[Brain] assist_request 忽略（{Owner.Name} 忙碌: combat={IsInCombat} knockedOut={AgentBrain.IsKnockedOut(Owner)}）→ 请求者 on_timeout 兜底");
                    return;
                }
                // 冒泡确认（think-aloud：B 侧配合可见）
                try
                {
                    // 本地化：配合接受台词「交给我」（B 侧冒泡）
                    SpeechChannel.SayPolished(Owner, LWNTextHelper.ResolveText("LWN_npc_assist_accept", "On it."),
                        SpeechPriority.Dialogue,
                        SpeechContext.FromBrain(this, requester, "assist_request", null));
                }
                catch { }
                DebugLogger.Log($"[Brain] {Owner.Name} 接受配合请求: {assistAction}（目标 {assistTarget ?? "-"}，请求者 {requester?.Name}）");
                // 单步执行（复用免确认直发通道）+ 完成回执 assist_done 给请求者
                var requesterRef = requester;
                ChatActionFlow.TryExecute(Owner, assistAction, assistTarget, null, null,
                    onFinished: actor =>
                    {
                        if (requesterRef != null && requesterRef.IsActive())
                        {
                            AgentAIController.Instance?.SendEventToAgent(requesterRef, "assist_done", actor);
                            DebugLogger.Log($"[Brain] {actor?.Name} 配合完成 → assist_done 回执给 {requesterRef.Name}");
                        }
                    });
                return;
            }
            // ── 配合完成回执转发（B → A 的执行器事件通道；A 的 ask_help/wait 步骤 on_event 消费）──
            if (aiEvent.EventType == "assist_done")
            {
                var exec = PlanExecutor.GetExecutorFor(Owner);
                exec?.NotifyDecisionEvent("assist_done");
                return;
            }
            if(aiEvent.EventType == "order_attack")
            {

                Agent targetAgent = aiEvent.Args[0] as Agent;

                if (targetAgent == null || targetAgent == Owner)
                    return;
                SetNpcIntent(NpcIntentType.Fighting, targetAgent);
                InteractedAgent = targetAgent;
                if (Settings.Instance.ShowDebugMessages)
                    // 本地化：攻击命令飘字（{OWNER} 开始攻击 {TARGET}）
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_brain_attack_order",
                        ("OWNER", Owner.Name.ToString()), ("TARGET", targetAgent.Name.ToString())), Colors.Red));
                ClearAllActions();
                // 儿童不参战：恐惧逃离
                if (IsChildOwner) { EnqueueAction(MoveToPositionAction.FleeFrom(Owner, targetAgent)); return; }
                EnqueueAction(new FightEnemyAction(targetAgent));
            }
            if (aiEvent.EventType == "duel")
            {
                // 🔴 2026-08-13 切磋分支（ActionRegistry.duel 发 "duel" 事件，与 order_attack 真打区分）：
                // FightEnemyAction(IsDuel=true) → OnStart 传 Peace:true → CombatManager.StartDuel
                // → AttackTriggerMissionLogic（双方 Invulnerable 底层无敌 + 真实血归零判负，点到为止）。
                // 不飘"开始攻击"红字（切磋非敌对）；判负由 EndDuel 统一收场（event_stop_combat）。
                Agent targetAgent = aiEvent.Args[0] as Agent;
                if (targetAgent == null || targetAgent == Owner) return;
                SetNpcIntent(NpcIntentType.Fighting, targetAgent);
                InteractedAgent = targetAgent;
                ClearAllActions();
                if (IsChildOwner) { EnqueueAction(MoveToPositionAction.FleeFrom(Owner, targetAgent)); return; }
                EnqueueAction(new FightEnemyAction(targetAgent, isDuel: true));
            }
            if (aiEvent.EventType == "event_stop_combat")
            {
                // 🔴 2026-08-13 切磋判负收场（AttackTriggerMissionLogic.EndDuel 发）：立即停战。
                // ClearAllActions → FightEnemyAction.OnEnd → CombatManager.EndFight
                //（归还原队 + WatchState 恢复 + 收刀），与投降停战同款清理但不开对话。
                SetNpcIntent(NpcIntentType.None);
                ClearAllActions();
                DebugLogger.Log($"[Brain-StopCombat] {Owner.Name}(Idx={Owner.Index}) 停战（切磋收场）");
            }
            if (aiEvent.EventType == "DeferredCombat")
            {
                var target = aiEvent.Args[0] as Agent;
                if (target == null || target == Owner) return;

                // 个体战斗复用统一入口（StartL3CombatJoin 同源调用）
                StartCombatAgainst(target);
                DebugLogger.Log($"[Brain-DeferredCombat] {Owner.Name}(Idx={Owner.Index}) 开始攻击 {target.Name}");
            }
            if (aiEvent.EventType == "event_agent_damaged")
            {
                var args = aiEvent.Args;
                if (args == null || args.Length < 2) return;

                Agent attacker = args[0] as Agent;
                Agent victim = args[1] as Agent;
                if (victim == null || attacker == null) return;
                if (!Owner.IsActive()) return;
                if(!attacker.IsActive()) return;
                if (attacker == Owner) return;
                if(!victim.IsActive()) return;
                if(attacker == victim) return;

                // 🔴 经历旁白（2026-08-11）：被攻击 = 事件事实（与击晕/认输同类，引擎确认的命中）。
                // 门控：正在交战时后续命中不记（交战开始已记录）；"被打了但没打起来"（攻击者逃跑）
                // 也覆盖——这是 flag 消费方案漏掉的情况。
                if (Owner == victim && !(EffectiveAction is FightEnemyAction))
                {
                    RecordNarration($"我遭到了{attacker.Name}的攻击");
                }

                // 受害者身份日志：区分自己是受害者（应反击）还是旁观者（看护主条件），排查小孩无法参战用
                DebugLogger.Log($"[Brain-Receive] {Owner.Name}(Idx={Owner.Index}) 收到事件 'event_agent_damaged' | victim={victim.Name}(Idx={victim.Index}) | 是否自己={Owner == victim} | 当前行为={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count} | 阶段={_lastAlertPhase}");

                // 🔴 2026-08-16（K1 血线关切，事件驱动重构，用户裁定）：受害者是玩家（主公被打）→ 血线关切。
                // 事件来源 = AttackTriggerMissionLogic.OnRegisterBlow 定向广播（15m 内队伍成员才收到）；
                // args[2] = 该击伤害（OnRegisterBlow 时血量未结算，Health - damage 预估结算后血线）。
                // 护主参战由下方既有 shouldHelp 逻辑负责（victim==Agent.Main → 帮）；本分支只负责说话关切。
                if (Owner != victim && victim == Agent.Main && attacker != Agent.Main)
                    CheckPlayerCareOnDamaged(victim, args.Length > 2 && args[2] is float dmg ? dmg : 0f);

                // 🔴 2026-08-12 停战检测：玩家在打自己 → 刷新 FightEnemyAction 最后受击时间
                // （玩家收刀 3s 停战的依据；被动反击专用，见 AtomicAction.FightEnemyAction）
                if (Owner == victim && attacker == Agent.Main && EffectiveAction is FightEnemyAction hitFight)
                    hitFight.NotifyHitByPlayer(Mission.Current?.CurrentTime ?? 0f);

                if (Settings.Instance.ShowDebugMessages)
                    // 伤害目击飘字：{ATTACKER} 对 {VICTIM} 造成了伤害
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_brain_damage_seen",
                        ("ATTACKER", attacker.Name.ToString()), ("VICTIM", victim.Name.ToString())), Colors.Yellow));
                // --- 核心护主逻辑 ---
                // 🆕 通用见义勇为（友方视角，玩家本人视为友方；不读开关）：
                //   victim 是玩家友方（含玩家本人被打）且 attacker 不是 → 帮（救自己人）；
                //   attacker 和 victim 都是友方（内斗）→ 不帮（不站队任何一方）；
                //   victim 不是友方（含玩家打外人）→ 不帮（犯罪后果走 WitnessCrime 警戒链）。

                bool shouldHelp = false;
                if (Owner == victim)
                {
                    // 受害者本人：任何人被打都反抗（含玩家侵害友方时受害者反抗——不豁免）
                    // 🔴 2026-08-20 用户裁定（同阵营误伤不反击）：攻击者非玩家且与我同阵营
                    // （同队/同家族/同王国，走 FriendlinessHelper.IsFriendlyBetween 共享管线）→
                    // 不反击，除非有明确攻击意图（引擎索敌锁定我 = 故意打我；误伤刮擦时锁的是
                    // 别人——实机：帝国熟练步兵与随从互殴的挥砍刮到帝国具装骑兵，骑兵被煽动反击）。
                    // 玩家攻击同阵营的照旧反击（下方 playerAttackedSelf 分支，不受本豁免影响）。
                    if (attacker != Agent.Main
                        && FriendlinessHelper.IsFriendlyBetween(attacker, Owner)
                        && attacker.GetTargetAgent() != Owner)
                    {
                        DebugLogger.Log($"[Brain-Chivalry] {Owner.Name}(Idx={Owner.Index}) 同阵营误伤（{attacker.Name} 未锁我为目标），不反击");
                        shouldHelp = false;
                    }
                    else
                    {
                        shouldHelp = true;
                    }
                }
                //护卫模式下，领导被攻击（NPC 领袖的贴身护卫，与友方判定无关）
                else if (Leader != null && victim == Leader )
                {
                   shouldHelp = true;
                }
                else if (Owner != victim
                    && (victim == Agent.Main || FriendlinessHelper.IsFriendlyToPlayer(victim))
                    && attacker != Agent.Main
                    && !FriendlinessHelper.IsFriendlyToPlayer(attacker))
                {
                    // 🆕 通用见义勇为：victim 是玩家友方（含玩家本人被打）而 attacker 不是 → 帮
                    shouldHelp = true;
                }


                // 见义勇为/护主/反抗：本脑准备攻击谁（排查参战逻辑用）
                if (shouldHelp)
                    DebugLogger.Log($"[Brain-Chivalry] {Owner.Name}(Idx={Owner.Index}) 准备攻击 {attacker.Name}(Idx={attacker.Index}) 救援 {victim.Name}(Idx={victim.Index})");
                else
                    DebugLogger.Log($"[Brain-Chivalry] {Owner.Name}(Idx={Owner.Index}) 不参战：victim={victim.Name}(Idx={victim.Index}) attacker={attacker.Name}(Idx={attacker.Index})");

                // 🔴 2026-08-13 切磋收场冷却（AttackTriggerMissionLogic.EndDuel 配套）：
                // 判负那一击的 event_agent_damaged 可能晚于停战事件到达——此时行为已被清空（null），
                // 受害者护主逻辑把"切磋对手的命中"误判为被袭击 → 骑士精神反击 → 以 Peace=false
                // 重新真打（无切磋登记、双方已恢复 Mortal）→ 一方被打死（实机 2026-08-13 15:21）。
                // 攻击者 = 刚结束的切磋对手（3s 窗口）→ 不反击，让切磋真正点到为止。
                // 第三方攻击照常反击；玩家 order_attack 走 "attack" 事件不经此路径，不受影响。
                if (Owner == victim
                    && AttackTriggerMissionLogic.Instance?.IsRecentDuelPair(attacker, Owner) == true)
                {
                    DebugLogger.Log($"[Brain-Chivalry] {Owner.Name}(Idx={Owner.Index}) 切磋收场冷却（{attacker.Name} 是刚结束的切磋对手），不反击");
                    return;
                }

                if (shouldHelp)
                {
                    if (EffectiveAction is FightEnemyAction currentFight)
                    {
                        // 如果我正在打的人（或马上要打的人），就是现在伤害老大的人
                        if (currentFight.TargetEnemy == attacker)
                        {
                            return;
                        }
                    }

                    // 🔴 2026-08-12 用户裁定（当事人梯度，与围观者共享警戒机制、不同梯度）：
                    // 玩家打我自己 → 第一刀不立即参战/不播参战宣言——只发 1.2 脉冲 → Cautious
                    // （疑惑 + 警告冒泡「住手！再打我可要动手了！」）；玩家第二刀叠加 2.4 → Alarmed
                    // → 立刻反击（无抑制，两刀就红）。节奏全权交给警戒系统。NPC 打我 → 立即反击（原逻辑）。
                    bool playerAttackedSelf = attacker == Agent.Main && Owner == victim;

                    // BubbleSay 参战理由（走 NpcSpeech.csv + PlaceholderResolver 标准管道；
                    // 🔴 2026-08-12 刺激=attacked/seen_crime → 润色 prompt 注入「你刚被打/目睹犯罪」处境；
                    // 受害者被玩家打的第一刀不播——疑惑冒泡由 Cautious 模板接管）
                    if (!playerAttackedSelf)
                    {
                        string line;
                        if (IsChildOwner)
                        {
                            // 儿童不参战：喊求救后逃离（不播大人参战台词）
                            line = LWNTextHelper.ResolveText("LWN_brain_child_flee", "Waaah! Run!!");
                        }
                        else
                        {
                            string templateId = Owner == victim
                                ? "CombatJoin_Victim"
                                : "CombatJoin_Bystander";
                            line = NpcSpeechResolver.Resolve(templateId,
                                speaker: (Owner.Character as CharacterObject)?.HeroObject,
                                listener: Hero.MainHero);
                            line ??= (Owner == victim
                                // 冒泡兜底：受害者参战台词（主文本走 NpcSpeech.csv，这里只兜底）
                                ? LWNTextHelper.ResolveText("LWN_brain_combatjoin_victim", "You dare strike me?!")
                                // 冒泡兜底：旁观者参战台词（主文本走 NpcSpeech.csv，这里只兜底）
                                : LWNTextHelper.ResolveText("LWN_brain_combatjoin_bystander", "You dare touch someone from our village?!"));
                        }
                        BubbleSay(line, Owner == victim ? "attacked" : "seen_crime", attacker);
                    }

                    //时序处理： 受到玩家攻击 → 警戒值脉冲（受害者 1.2/刀：第一刀 Cautious 疑惑，
                    // 第二刀 2.4 → Alarmed 反击——2026-08-12 用户裁定，原 3.0 一刀拉满直接开打）
                    if (attacker == Agent.Main)
                    {
                        // 先写脉冲上下文再加值：受害者 = 真受害者——本人被攻击 → 上下文指向本人，
                        // 队友豁免（AddAlert 内判定）因此不豁免；队友围观玩家打别人 → 上下文指向他人 → 豁免。
                        SetPulseTarget(PlayerActionType.AttackAlly, victim.Name, null, victim.Index);
                        if (AddAlert(PlayerActionType.AttackAlly, 1.2f))  // 队友围观豁免（false）→ 跳过阶段检查
                            CheckPhaseTransition();
                    }
                    // 玩家打非友方平民 → 走专用事件 'PlayerAttackedCivilian'（AttackTriggerMissionLogic 广播，
                    // 周围 15m 围观者消费 → AttackCivilian 脉冲；不在 damaged 里处理——damaged 只直发受害者）

                    // 🔴 已在战斗中（正在打别人）：只感知不换目标（2026-08-09 改）——
                    // 索敌交给原版 AI（扫描敌对 Agent 按距离/威胁度排序，见 Knowledge/Agent_AI底层原理.md）。
                    // 旧逻辑 ClearAllActions+重入队 → 多攻击者间来回切换的决策抖动；
                    // 且 CombatManager 的目标锁（SetTargetAgent）已移除，换目标由原版索敌节奏接管。
                    if (EffectiveAction is FightEnemyAction)
                        return;

                    // 🔴 玩家打我自己：不入队反击（第一刀留给 Cautious 疑惑；第二刀 Alarmed →
                    // StartL3CombatJoin 入队 canCease=true——收刀停战保持）。NPC 打我 → 立即反击。
                    if (playerAttackedSelf)
                        return;

                    SetNpcIntent(NpcIntentType.Fighting, attacker);
                    InteractedAgent = attacker;
                    ClearAllActions();
                    AgentControlHelper.ForceUnlockAgent(Owner); // ClearAllActions 会后置 DoNotRun|NoAttack，FightEnemyAction 需要清除
                    // 儿童不参战：恐惧逃离；大人才进战斗（被动反击 → 玩家收刀停战，见 FightEnemyAction 停战检测）
                    if (IsChildOwner)
                        EnqueueAction(MoveToPositionAction.FleeFrom(Owner, attacker));
                    else
                        EnqueueAction(new FightEnemyAction(attacker, canCeaseOnPlayerSheathe: true));
                }
            }
            if (aiEvent.EventType == "EndInteraction")
            {
                Agent target = (Agent)aiEvent.Args[0];
                if (InteractedAgent == target)
                {
                    SetNpcIntent(NpcIntentType.None);
                    ClearAllActions();
                    AgentControlHelper.ForceUnlockAgent(Owner);
                    ResumeVanillaAI();
                    InteractedAgent = null;
                }
            }
            // 🔴 2026-08-12（PlayerAttackedAlly）：玩家侵害友方（打随从/同伴/友军）→ 周围人警戒反应。
            // AttackTriggerMissionLogic 广播（15m）；日志实锤：之前只有受害者本人知道，卫兵只看到拔刀。
            // 玩家队伍成员（其他随从）排除——主公教训自己人是家事，信任主公（劝架另设）。
            // 🔴 2026-08-12（用户裁定：Cautious 劝阻 + Alarmed 参战）：脉冲 1.5（不是 3.0）——
            // 3.0 在 3s 抑制结束后仍 ≥2.0 自动 Alarmed（日志实锤：打一刀收手 3s 后卫兵照样围殴，
            // 劝阻形同虚设）。1.5 + 3s 抑制：单刀收手 → 衰减停在 Cautious（劝阻，不参战）；
            // 玩家继续打 → 每次命中重新广播叠加 + WeaponDrawn 持续 → 超 2.0 → Alarmed 执法参战。
            if (aiEvent.EventType == "PlayerAttackedAlly")
            {
                Agent criminal = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as Agent : null;
                Agent victim = aiEvent.Args != null && aiEvent.Args.Length > 1 ? aiEvent.Args[1] as Agent : null;
                if (criminal == null || victim == null || criminal == Owner || victim == Owner) return;
                if (IsPlayerTeammate(Owner)) return;
                SetPulseTarget(PlayerActionType.AttackAlly, victim.Name, null, victim.Index);
                RecordNarration($"我看见{criminal.Name}在打{victim.Name}");
                if (AddAlert(PlayerActionType.AttackAlly, 0.5f))   // 0.5/刀（2026-08-12 用户裁定：攻击频率快）：3s 劝阻窗口内叠到 ~1.5 → Cautious；持续打 4 刀+WeaponDrawn 才 Alarmed
                {
                    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
                    CheckPhaseTransition();
                }
                return;
            }
            // 🔴 2026-08-12（AttackCivilian）：玩家当街打非友方平民（AttackTriggerMissionLogic 广播，
            // 15m 围观者消费；暴徒豁免在广播侧过滤）。设计（用户裁定：劝阻→升级→参战）：
            // 脉冲 2.0 + 3s 抑制 → 只到 Cautious（BecomeCautious → 喝止冒泡「住手！」）；不听继续打 →
            // 抑制结束 + WeaponDrawn 持续累加 → Alarmed 参战；打一下就走 → 衰减回 Normal（只被喝止）。
            // 受害者本人不走本分支（damaged 直发已走反击链）；友方旁观者豁免由 AddAlert 内部判定。
            if (aiEvent.EventType == "PlayerAttackedCivilian")
            {
                Agent criminal = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as Agent : null;
                Agent victim = aiEvent.Args != null && aiEvent.Args.Length > 1 ? aiEvent.Args[1] as Agent : null;
                if (criminal == null || victim == null || criminal == Owner || victim == Owner) return;
                SetPulseTarget(PlayerActionType.AttackCivilian, victim.Name, null, victim.Index);
                RecordNarration($"我看见{criminal.Name}在街上打{victim.Name}");
                if (AddAlert(PlayerActionType.AttackCivilian, 0.5f))   // 0.5/刀（同 AttackAlly，2026-08-12 用户裁定）：第一刀 Suspicious 嘀咕 → 第二刀 Cautious 劝阻 → 持续打 Alarmed 参战
                {
                    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
                    CheckPhaseTransition();
                }
                return;
            }
            // 🔴 2026-08-20（感知管线统一重构，用户裁定：所有警戒值由 Brain 感知事件自行增加，
            // 执行端/UI 端禁止直接 AddAlert）：扒窃受害者「体感察觉」——被摸/被抓现行由受害者自己的
            // 脑处理（SendEventToAgent 定向直发，不经视线——体感不需要看；背对偷窃的受害者靠此感知）。
            // 量级按事件档位查表（原散落执行端的常量迁入）：perfect 0.1（隐约不对）/ normal 0.35（感觉被摸）/
            // fail 3.0（手滑被抓现行）/ equipment_fail 2.0（随从卸装备被目标察觉）。
            // 亲眼目击（旁观者）不在此分支——那是 WitnessCrime 广播的职责。
            if (aiEvent.EventType == "TheftVictimized")
            {
                var args = aiEvent.Args;
                if (args == null || args.Length < 3) return;
                Agent thief = args[0] as Agent;
                Agent victim = args[1] as Agent;
                string resultType = args[2] as string;
                if (victim == null || thief == null || victim != Owner) return;
                if (!Owner.IsActive()) return;
                string itemName = args.Length > 3 ? args[3] as string : null;
                float amount = resultType switch
                {
                    "perfect" => 0.1f,          // 完美窃取：隐约觉得不对（原 StealBarVM 0.1）
                    "normal" => 0.35f,          // 普通命中：感觉被摸（原 NormalHitVictimAlert）
                    "fail" => 3.0f,             // 手滑失误：当场被抓现行（原 MissVictimAlert）
                    "equipment_fail" => 2.0f,   // 卸装备失败：目标察觉（原 InlineSteps 2.0）
                    _ => 0.35f,
                };
                SetPulseTarget(PlayerActionType.Steal, victim.Name, itemName, victim.Index,
                    thief == Agent.Main ? -1 : thief.Index);
                RecordNarration($"我被{thief.Name}碰了衣兜");
                DebugLogger.Log($"[Brain-TheftVictim] {Owner.Name}(Idx={Owner.Index}) 察觉被偷（{resultType}，+{amount:F2}，窃贼={thief.Name}）");
                if (AddAlert(PlayerActionType.Steal, amount))
                {
                    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
                    CheckPhaseTransition();
                }
                return;
            }
            // 🔴 2026-08-20（感知管线统一重构，同 TheftVictimized 原则）：撬锁失误声响的「听觉感知」——
            // 听到可疑动静由目击者脑自行加警戒（广播经视线过滤：能看到噪音源才被惊动；队友排除）。
            // 原 StealBarVM 直拍 1.5（NoiseWitnessAlert）迁入本分支；0.5s 节流仍在 UI 端。
            if (aiEvent.EventType == "TheftNoise")
            {
                Agent source = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as Agent : null;
                if (source == null || source == Owner) return;
                if (IsPlayerTeammate(Owner)) return;
                SetPulseTarget(PlayerActionType.Steal, null, null, -1, source == Agent.Main ? -1 : source.Index);
                DebugLogger.Log($"[Brain-Noise] {Owner.Name}(Idx={Owner.Index}) 听到可疑声响（源={source.Name}，+1.5）");
                if (AddAlert(PlayerActionType.Steal, 1.5f))   // 撬锁失误：目击者 +1.5（Cautious 上沿，上前查看）
                {
                    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
                    CheckPhaseTransition();
                }
                return;
            }
            if (aiEvent.EventType == "WitnessCrime_GatherOnLook")
            {
                try
                {
                    //这里的类型转换如果有问题，会导致异常
                    Agent criminal = (Agent)aiEvent.Args[0];
                    Agent victim = (Agent)aiEvent.Args[1];
                    Vec3 assignedPos = (Vec3)aiEvent.Args[2];
                    Vec2 turnDir = (Vec2)aiEvent.Args[3];
                    float delay = GroupStageManager.CalculateReactionDelay(Owner, criminal, victim);

                    // 🆕 友方旁观者豁免（双向豁免核心，不读开关）：犯罪者=玩家友方（含玩家本人）对非友方犯罪，
                    // 友方旁观者无动于衷——不围观、不质问、不警戒（直接忽略本事件）。
                    // 受害者是玩家友方（开关开时玩家侵害友方）→ 不豁免，照常围观/质问。
                    // 🔴 2026-08-13 泛化：随从（玩家友方）犯法，其他友方围观者同样豁免（代表玩家阵营）。
                    if ((criminal == Agent.Main || FriendlinessHelper.IsFriendlyToPlayer(criminal)) && IsAllyBystander(victim)) return;

                    // ── 警戒脉冲：区分偷窃 vs 攻击 vs 击晕（criminal==玩家时）──
                    // 认输场景：受害者大脑的 PendingPostConversationCleanup 已置 true，
                    // 此时战斗已结束但围观不应被误判为偷窃——跳过犯罪分类，仅保留围观行为。
                    // victim 可为 null（保管箱偷窃抓现行）→ GetBrainForAgent 对 null 取 .Index 会 NRE，先判空
                    var victimBrain = victim != null ? AgentAIController.GetBrainForAgent(victim) : null;
                    bool isSurrenderScene = victimBrain?.PendingPostConversationCleanup == true;

                    // 🔴 2026-08-13 分类块泛化（任何人犯法闭环）：原 `criminal == Agent.Main` 门控只认玩家
                    // ——随从犯罪被目击拉满后会质问玩家（体验 bug）。泛化条件 = 犯罪者非自己 + 犯罪标记
                    // （make_noise/NPC 投降广播 isCrime=false 仅围观）。suspect 传 criminal.Index——
                    // 玩家作案时 == Agent.Main.Index，AlertTargetIsPlayer 判定自然成立。
                    if (criminal != null && criminal != Owner && aiEvent.IsCrime)
                    {
                        if (!isSurrenderScene)
                        {
                            // 先写脉冲上下文再加值：受害者 = 真受害者（victim.Index）。
                            // 队友豁免由 AddAlert 内部判定——队友围观（上下文 ≠ 本人）豁免不质问；
                            // 队友本人被侵害（上下文 = 本人）照常分类 + 指控。
                            if (IsKnockedOut(victim))
                            {
                                SetPulseTarget(PlayerActionType.Knockout, victim?.Name, null, victim?.Index ?? -1, criminal.Index);
                                RecordNarration($"我看见{criminal.Name}打晕了{victim?.Name ?? "人"}");
                                // 队友围观豁免（AddAlert 返回 false）→ 连带跳过质问意图
                                if (AddAlert(PlayerActionType.Knockout, 3.0f))
                                {
                                    _pulseSuppressedUntil = 0f; // 清除抑制，让 Alarmed 过渡正常触发
                                    SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: ConfrontationType.Stop);
                                }
                            }
                            else if (CombatManager.IsAgentFightingPlayer(victim) || CombatManager.IsPlayerInCombat)
                            {
                                // 斗殴/攻击：victim 正在和玩家战斗，不是偷窃
                                SetPulseTarget(PlayerActionType.AttackAlly, victim?.Name, null, victim?.Index ?? -1, criminal.Index);
                                RecordNarration($"我看见{criminal.Name}在袭击{victim?.Name ?? "人"}");
                                if (AddAlert(PlayerActionType.AttackAlly, 3.0f))  // 队友围观豁免 → 连带跳过质问意图
                                {
                                    _pulseSuppressedUntil = 0f;
                                    SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: ConfrontationType.Stop);
                                }
                            }
                            else
                            {
                                // 偷窃：立刻加警戒 + 3s 脉冲抑制
                                // （受害者直接指控，目击者抑制后逐步升级 → 围观后质问）
                                SetPulseTarget(PlayerActionType.Steal, victim?.Name, null, victim?.Index ?? -1, criminal.Index);
                                RecordNarration($"我看见{criminal.Name}在偷窃");
                                if (AddAlert(PlayerActionType.Steal, 3.0f))  // 队友围观豁免 → 连带跳过质问意图
                                {
                                    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
                                    SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: ConfrontationType.Recover);
                                }
                            }
                        }

                    }

                    ClearAllActions();
                    InteractedAgent = criminal;

                    // ── 角色分流 ──
                    if (Owner == victim && criminal == Agent.Main
                        && !IsKnockedOut(victim)
                        && !isSurrenderScene)
                    {
                        // 受害者：直接指控（击晕受害者跳过，event_agent_knocked_out 会最终覆盖）
                        var conflictData = new PendingConflict(
                    eventId: $"Theft_{TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours}",
                    // 冲突主题：当众行窃（对话开场 / 谈判上下文可见）
                    topicName: LWNTextHelper.ResolveText("LWN_brain_theft_topic", "Theft in public"),
                    // 冲突目标：要求 {NAME} 立刻归还财物并赔偿精神损失（UI 目标栏可见）
                    goalDesc: LWNTextHelper.ResolveCompound("LWN_brain_theft_goal", ("NAME", criminal.Name)),
                    severity: 70.0f,
                    type: NegotiationGoalType.ResolveConflict_Apology
                        );


                        EnqueueAction(new PrepareOpeningAction(InitiativeType.CrimeAccusation, conflictData));
                    }
                        EnqueueAction(new ReactionDecisionAction(delay, (agent) =>
                    {

                        EnqueueAction(new LookAtAction(criminal, 0.5f));
                        EnqueueAction(new MoveToPositionAction(assignedPos, turnDir));
                        if (Owner == victim)
                            EnqueueAction(new ForceTalkAction());
                        // 5. 待机
                        EnqueueAction(new StayAction(criminal));
                    }));
                    // 击晕：立即检查阶段穿越，确保 Alarmed 在衰减前触发
                    // （放在 ReactionDecisionAction 入队之后，让 L3 质问/参战覆盖围观动作）
                    // 🔴 2026-08-13 泛化：criminal 非玩家（随从击晕）同样立即检查——suspect 分支会参战
                    if (criminal != null && criminal != Owner && aiEvent.IsCrime && IsKnockedOut(victim))
                    {
                        CheckPhaseTransition();
                    }
                }
                catch(Exception )
                {
                   // DebugLogger.Log($"[严重错误] 处理 Agent {Owner.Name} 时发生异常: {ex.Message}\n堆栈: {ex.StackTrace}");
                }
            }
            else if (aiEvent.EventType == "WitnessCrime_StayStare")
            {
                try
                {
                    //这里的类型转换如果有问题，会导致异常
                    Agent thief = (Agent)aiEvent.Args[0];
                    Agent victim = (Agent)aiEvent.Args[1];
                    float delay = GroupStageManager.CalculateReactionDelay(Owner, thief, victim);

                    // 🆕 友方旁观者豁免（同 GatherOnLook）：犯罪者=玩家友方（含玩家本人）对非友方犯罪，友方旁观者无动于衷。
                    if ((thief == Agent.Main || FriendlinessHelper.IsFriendlyToPlayer(thief)) && IsAllyBystander(victim)) return;
                   // InformationManager.DisplayMessage(new InformationMessage($"{Owner.Name} 没抢到位置，原地吃瓜。"));
                    ClearAllActions();
                    InteractedAgent = thief;
                    EnqueueAction(new ReactionDecisionAction(delay, (agent) =>
                    {
                        EnqueueAction(new StayAction(thief));
                    }));
                }
                catch (Exception)
                {
                 //   DebugLogger.Log($"[严重错误] 处理 Agent {Owner.Name} 时发生异常: {ex.Message}\n堆栈: {ex.StackTrace}");
                }
            }
            else if (aiEvent.EventType == "event_agent_knocked_out")
            {
                // 被击晕：清除所有行为，StayAction 占位永不结束
                // EnqueueAction 自动 SuspendVanillaAI，StayAction 防止 Brain 自动 Resume
                // 经历旁白：ClearAllActions 前捕获战斗目标（之后 _currentAction 会被清空）
                string knockedBy = (_currentAction as FightEnemyAction)?.TargetEnemy?.Name?.ToString() ?? "人";
                RecordNarration($"我被{knockedBy}打晕了");
                SetNpcIntent(NpcIntentType.KnockedOut);
                IsStunned = true;
                ClearAllActions();
                EnqueueAction(new StayAction(null, false, isKnockout: true));
                // 🔴 2026-08-14 被捕随从击倒捕获（缓存方案）：脚本击晕不改引擎 AgentState，
                // OnAgentHit 的 Health<=0 捕不到——在击晕事件（Agent 存活的安全时机）标记 Down，
                // Mission 结束转押判定用。
                AttackTriggerMissionLogic.Instance?.NotifyAgentKnockedOut(Owner);
            }

            // ═══════════════════════════════════════════════════════════════
            // 🆕 NPC 开始看到玩家 → 概率冒泡问候（Phase 0 对齐）
            // 从 InteractionMissionView.OnNpcStartObservingPlayer 迁移至此。
            // BubbleSay 决策统一在 AgentBrain 内部，外部不直接调 AgentSay。
            // ═══════════════════════════════════════════════════════════════
            if (aiEvent.EventType == "StartObservingPlayer")
            {
                int honor = 0;
                if (Hero.MainHero.CurrentSettlement != null)
                    honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);

                // 概率 = clamp(0.10 + honor * 0.01, 0.02, 0.25)
                float prob = MathF.Clamp(0.05f + honor * 0.01f, 0.01f, 0.15f);
                if (MBRandom.RandomFloat >= prob) return;

                if (Settings.Instance.ShowDebugMessages)
                    // 冒泡问候判定飘字：{NAME} 决定向玩家打招呼（概率 {PROB}，声望 {HONOR}）
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_brain_bubble_greet_decided",
                        ("NAME", Owner.Name.ToString()), ("INDEX", Owner.Index.ToString()), ("PROB", $"{prob:P0}"), ("HONOR", honor.ToString()))));

                var factors = new DialogueFactors
                {
                    Honor = honor >= 5 ? HonorLevel.High : (honor <= -5 ? HonorLevel.Low : HonorLevel.Neutral),
                    Gender = (Owner.Character != null && Owner.Character.IsFemale) ? NpcGender.Female : NpcGender.Male,
                    Identity = NpcIdentity.Civilian
                };

                string emotion;
                string line = DialogueTemplateHelper.Get("BubbleGreet", factors, out emotion, null, Owner);
                if (!string.IsNullOrEmpty(line))
                {
                    if (Settings.Instance.ShowDebugMessages)
                        // 冒泡问候台词飘字：{NAME} 说出了问候语 {LINE}
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_brain_bubble_greet_line",
                            ("NAME", Owner.Name.ToString()), ("LINE", line))));
                    BubbleSay(line);
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // 🆕 警戒阶段穿越事件（Phase 2）
            // ═══════════════════════════════════════════════════════════════

            if (aiEvent.EventType == "BecomeSuspicious")
            {
                BubbleSayOnce(AlarmPhase.Suspicious);
            }
            if (aiEvent.EventType == "BecomeCautious")
            {
                // 🔴 2026-08-12 用户裁定：Cautious = 上前靠近 + 说话劝阻（不攻击）——卫兵持续站到
                // 玩家面前 4m 劝（FollowAgentAction：keepFollow 保持站位 + 抑制瞬移；angleOffset=0 =
                // 玩家朝向正前 = 摄像机视角内，玩家转头必看见；静止时自带 SetLookAgent 看着玩家）。
                // 玩家继续攻击 → 警戒涨到 Alarmed → 执法参战；玩家收手 → CalmDown(Normal) 清队列回岗。
                // 🔴 目标=玩家但语义是"面前 4m 站位劝阻"，不可换 VanillaFollowAction（2026-08-13 用户裁定）：
                // 原版跟随是贴人走，不是固定距离正面站位。
                // 🔴 2026-08-13 suspect 化：随从犯法被围观 → 对着嫌疑犯喝止（站位劝阻无对话 UI，铁律 18 允许）。
                Agent cautionTarget = TopSuspectAgent() ?? Agent.Main;
                if (cautionTarget == null) return;
                if (EffectiveAction == null || EffectiveAction is StayAction)
                {
                    EnqueueAction(new FollowAgentAction(cautionTarget, run: false, radius: 4f, angleOffset: 0f,
                        stopDistance: 3.5f, keepFollow: true,
                        endBehavior: MoveToPositionAction.EndBehavior.Unlock));
                }
                BubbleSayOnce(AlarmPhase.Cautious);
            }
            if (aiEvent.EventType == "BecomeAlarmed")
            {
                if (_pulseSuppressedUntil > 0 && Mission.Current?.CurrentTime < _pulseSuppressedUntil)
                    return;

                // 已经在战斗中（当前或队列中）→ 不中断，让 FightEnemyAction 自然运行到终止
                if (IsCurrentOrPending<FightEnemyAction>())
                    return;

                // 🔴 2026-08-13 suspect 分支（必须插在 IsPlayerInCombat 检查之前——玩家碰巧在战斗时
                // 随从犯罪会被抢走参战玩家）：顶条目嫌疑犯非玩家 → 直接对嫌疑犯执法参战（任何人犯法闭环）。
                // 嫌疑犯从 _alertBreakdown 顶条目推导（suspect 化），不依赖易被覆盖的 InteractedAgent。
                Agent suspect = TopSuspectAgent();
                if (suspect != null && suspect != Agent.Main)
                {
                    // 守卫执法冒泡：站住，{NAME}！（{NAME}=嫌疑犯）
                    BubbleSay(LWNTextHelper.ResolveCompound("LWN_brain_crime_shout", "Stop, {NAME}!", ("NAME", suspect.Name.ToString())), "seen_crime", suspect);
                    StartCombatAgainst(suspect);
                    // 🔴 逮捕登记（守卫执法语义 = 逮捕而非私刑；2026-08-14 重构）：
                    // 逮捕瞬间缓存 Hero 引用到 AttackTriggerMissionLogic（Agent 存活的安全时机），
                    // Mission 结束由它只读大地图数据转押——不再在 teardown 期读 Agent native 数据
                    //（实机 2026-08-14：被移除 Agent 的 IsActive() 抛 NRE，整环中断）。
                    // 随从被击倒且玩家不调停离场 → 转押定居点（Phase E）；调停 → Unregister。
                    AttackTriggerMissionLogic.Instance?.RegisterArrestedCompanion(suspect);
                    return;
                }

                // 🔴 2026-08-12 用户裁定：玩家已在战斗中 → 劝阻已失效（Cautious 上前劝过）→
                // 跳过质问（强制对话会打断战斗，体验差）直接拔刀参战（StartL3CombatJoin →
                // StartCombatAgainst → 执法到底 canCease=false）。原 return 导致卫兵喝止完就消失。
                if (CombatManager.IsPlayerInCombat)
                {
                    StartL3CombatJoin();
                    return;
                }

                // 🆕 MCM 开关：警戒拉满后直接战斗，不走质问
                // （复用 StartL3CombatJoin 战斗加入路径：推进 WorldEvent 到 Confrontation + 入队 FightEnemyAction）
                if (Settings.Instance.AlarmedDirectCombat)
                {
                    StartL3CombatJoin();
                    return;
                }

                StartL3Confrontation();
            }
            if (aiEvent.EventType == "CalmDown")
            {
                // 对话结束前不处理 CalmDown：投降/认输路径已设 PendingPostConversationCleanup，
                // ClearAllAlerts 会重置 _lastAlertPhase 防止误判，但以防万一这里也做守卫。
                // 对话结束后 PostConversationCleanup 统一清理，不应被 CalmDown 提前 ResumeVanillaAI。
                if (PendingPostConversationCleanup)
                    return;

                // 已在战斗中（当前或队列中） → 警戒值下降不应该中断战斗
                if (IsCurrentOrPending<FightEnemyAction>())
                    return;

                var fromPhase = (AlarmPhase)aiEvent.Args[0];
                var toPhase   = (AlarmPhase)aiEvent.Args[1];

                // 清除高位 bubbled 记录，允许重新升级后再次触发
                _bubbledPhases.RemoveWhere(k => k.Item2 > toPhase);

                // Alarmed→* 或 →Normal：完全清理行为链 + 警戒值归零
                if (fromPhase >= AlarmPhase.Alarmed || toPhase == AlarmPhase.Normal)
                {
                    SetNpcIntent(NpcIntentType.None);
                    ClearAllActions();
                    ClearAllAlerts(); // 警戒值归零，避免围观 NPC 衰减过程中重复升级
                    ResumeVanillaAI();
                }
                // Cautious→Suspicious：只取消 LookAt
                else if (fromPhase == AlarmPhase.Cautious && EffectiveAction is LookAtAction)
                {
                    AgentControlHelper.StopLooking(Owner);
                    if (_currentAction is LookAtAction)
                    {
                        // 正在执行中 → 标准中断
                        _currentAction.RequestInterrupt();
                    }
                    else
                    {
                        // 还在队列头没开始 → 直接 Dequeue 丢掉
                        _actionQueue.Dequeue();
                    }
                }
            }



            // ═══════════════════════════════════════════════════════════════
            // 🆕 战斗投降相关新事件
            // ═══════════════════════════════════════════════════════════════

            if (aiEvent.EventType == "event_npc_surrender")
            {
                // NPC 自己决定认输（残血触发）
                RecordNarration($"我向{Agent.Main?.Name?.ToString() ?? "对手"}认输了");
                SetNpcIntent(NpcIntentType.Surrendering, Agent.Main);
                // 🔴 2026-08-13（投降平权）：认输 = 立即停战收刀——原来只喊话 + 改意图标签，
                // FightEnemyAction 继续执行：NPC 一边喊"我投降"一边继续砍人（出戏）。
                // 与玩家投降路径对称：ClearAllActions → FightEnemyAction.OnEnd（EndFight + 收刀）
                // → StayAction 原地待命，等玩家处置（AcceptSurrender 对话 / 走开 / 再攻击则反击）。
                ClearAllActions();
                EnqueueAction(new StayAction(Agent.Main));
            }

            if (aiEvent.EventType == "event_player_surrendered")
            {
                // 玩家主动认输 → 立即停战，清掉 FightEnemyAction，进入 StayAction 原地待命。
                // 对话中谈拢了 → EndConversation 时 PostConversationCleanup 收尾。
                // 对话中谈崩了 → event_surrender_refused 会清 StayAction、重入 FightEnemyAction。
                RecordNarration($"{Agent.Main?.Name?.ToString() ?? "对方"}向我认输了");
                SetNpcIntent(NpcIntentType.None);
                ClearAllActions(); // 触发 FightEnemyAction.OnEnd → EndFight
                EnqueueAction(new StayAction(Agent.Main));
                PendingPostConversationCleanup = true;
                DebugLogger.Log($"[Brain-Surrender] {Owner.Name}(Idx={Owner.Index}) 玩家投降 — 停战 + StayAction（对话结束后统一恢复）");

                // 广播围观 + 启动对话
                var excludeSelf = new HashSet<Agent> { Owner };
                AgentAIController.Instance?.BroadcastEventInRange(
                    Agent.Main.Position, 20f, "WitnessCrime", excludeSelf, true, Agent.Main, Owner);
                ConversationEntryPatch._pendingTrigger = DialogueTrigger.PlayerSurrender;
                ConfrontingBrain = this;
                DebugLogger.Log($"[ConvLock] Acquire by {Owner.Name}(Idx={Owner.Index}) | reason=PlayerSurrender");
                var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
                conversationLogic?.StartConversation(Owner, true, false);
            }

            if (aiEvent.EventType == "event_surrender_accepted")
            {
                // 玩家接受 NPC 认输 → 立即停战，清掉 FightEnemyAction，进入 StayAction 原地待命。
                SetNpcIntent(NpcIntentType.None);
                ClearAllActions(); // 触发 FightEnemyAction.OnEnd → EndFight
                EnqueueAction(new StayAction(Agent.Main));
                PendingPostConversationCleanup = true;
                DebugLogger.Log($"[Brain-Surrender] {Owner.Name}(Idx={Owner.Index}) NPC投降被接受 — 停战 + StayAction（对话结束后统一恢复）");

                // 广播围观 + 启动对话
                // 🔴 isCrime:false——NPC 投降不是犯罪（2026-08-13 suspect 化：仅围观不分类）
                AgentAIController.Instance?.BroadcastEventInRange(
                    Owner.Position, 20f, "WitnessCrime",
                    exclude: null, requireSight: true, isCrime: false, Owner, Agent.Main);
                ConversationEntryPatch._pendingTrigger = DialogueTrigger.NpcSurrender;
                ConfrontingBrain = this;
                DebugLogger.Log($"[ConvLock] Acquire by {Owner.Name}(Idx={Owner.Index}) | reason=NpcSurrender");
                var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
                conversationLogic?.StartConversation(Owner, true, false);
            }

            if (aiEvent.EventType == "event_surrender_refused")
            {
                // 对话中谈崩了（威胁失败 / 拒绝 NPC 认输 / 拼死一战）→ 清 StayAction，重回战斗。
                SetNpcIntent(NpcIntentType.Fighting, Agent.Main);
                ClearAllActions(); // 触发 StayAction.OnEnd
                AgentControlHelper.ForceUnlockAgent(Owner); // ClearAllActions 会后置 DoNotRun|NoAttack，FightEnemyAction 需要清除
                EnqueueAction(new FightEnemyAction(Agent.Main));
                PendingPostConversationCleanup = false; // 已入队 FightEnemyAction，阻止 EndConversation 中的 PostConversationCleanup 误清理
                DebugLogger.Log($"[Brain-Surrender] {Owner.Name}(Idx={Owner.Index}) 投降谈判破裂 — 重回战斗");
            }


        } // ReceiveEvent

        // 辅助判断逻辑
       

        // --- 动作执行系统 ---
        private void EnqueueAction(IAtomicAction action)
        {
            DebugLogger.Log($"[Brain-Enqueue] {Owner.Name}(Idx={Owner.Index}) 入队 {action.GetType().Name} | 当前行为={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count}→{_actionQueue.Count + 1}");
            // 从空脑到有 Action 的转换：一次性接管原版 AI（SuspendVanillaAI 内部幂等）
            if (EffectiveAction == null)
            {
                SuspendVanillaAI();
            }
            _actionQueue.Enqueue(action);
        }

        /// 强行中断当前正在执行的 Action（不碰队列）。

        public void AbortCurrentAction()
        {
            if (_currentAction != null)
            {
                DebugLogger.Log($"[Brain-Abort] {Owner.Name}(Idx={Owner.Index}) 强制中断 {_currentAction.GetType().Name} | 队列剩余={_actionQueue.Count}");
                _currentAction.RequestInterrupt();
                // 下一帧 Tick 自动清理，队列不受影响
            }
        }

        /// <summary>清空当前动作 + 队列（lockPlace=true 时设 DoNotRun|NoAttack 脚本锁）。
        /// 🔴 internal（2026-08-11）：plan_debug 直接调用——原纯透传壳 ClearAllActionsInternal 已删
        /// （壳无空判/无守卫/无组合，只有可见性差异，属多余包装）。脑内部调用不受影响。</summary>
        internal void ClearAllActions(bool lockPlace = true)
        {
            bool hadActions = _currentAction != null || _actionQueue.Count > 0;
            DebugLogger.Log($"[Brain-Clear] {Owner.Name}(Idx={Owner.Index}) 清空动作 | 当前={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count} | hadActions={hadActions}");

            if (_currentAction != null) _currentAction.OnEnd(Owner);
            _currentAction = null;
            _actionQueue.Clear();

            if (hadActions && lockPlace)
            {
                // 只在确实清掉了 Action 时才设 DoNotRun 锁 + 清原生 AI 目标。
                // 空大脑（快速路径）NPC 的原生 AI 巡逻状态不应被干扰，
                // 否则 EndInteraction 后无法恢复巡逻。
                Owner.SetMaximumSpeedLimit(-1f, false);
                WorldPosition currentPos = Owner.GetWorldPosition();
                var lockFlags = Agent.AIScriptedFrameFlags.DoNotRun
                              | Agent.AIScriptedFrameFlags.NoAttack;
                Owner.SetScriptedPosition(ref currentPos, false, lockFlags);

                Owner.ResetEnemyCaches();
                Owner.ClearTargetFrame();
            }
            
        }

        /// <summary>暂停原版 AgentNavigator / DailyBehaviorGroup 对该 Agent 的控制。幂等。</summary>
        private bool SuspendVanillaAI()
        {
            // 🔴 永不 Suspend 玩家：控制权转移给 mod AI = 玩家整场无法移动（2026-08-09 致命 bug 修复）
            if (Owner == Agent.Main) return false;

            if (!SuspendedAgentIndices.Add(Owner.Index))
                return true; // 已在集合中，幂等

            DebugLogger.Log($"[AI-Debug] Suspend {Owner.Name} (Idx={Owner.Index}) | 集合size={SuspendedAgentIndices.Count} | 当前行为={_currentAction?.GetType().Name ?? "null"}");

            var nav = Owner.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            if (nav == null) return false;

            var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
            if (daily != null)
            {
                if (daily.IsActive)
                    daily.IsActive = false;
            }

            return true;
        }

        /// <summary>
        /// 恢复原版 AgentNavigator / DailyBehaviorGroup 的控制。
        /// 内部有 HashSet 守卫：没被 Suspend 过的 Agent 直接 return，每帧调用安全。
        /// </summary>
        private void ResumeVanillaAI()
        {
            if (!Owner.IsActive()) return;

            if (!SuspendedAgentIndices.Remove(Owner.Index))
                return; // 没被 Suspend 过，不碰原版 AI

            DebugLogger.Log($"[AI-Debug] Resume {Owner.Name} (Idx={Owner.Index}) | 集合size={SuspendedAgentIndices.Count}");

            var nav = Owner.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            if (nav == null) return;

            var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
            if (daily != null)
            {
                if (!daily.IsActive)
                {
                    daily.IsActive = true;
                    daily.ForceThink(0f);
                }
            }
        }

        /// <summary>
        /// 对话结束后统一清理：结束当前动作、解锁 Agent、恢复原版 DailyBehaviorGroup。
        /// 由 ConversationManager.EndConversation Patch 调用，替代原来散落在各个
        /// mid-conversation Intent handler 中的 ClearAllActions + ResumeVanillaAI。
        ///
        /// 幂等：重复调用安全（ClearAllActions 在空脑时是 no-op，
        /// ResumeVanillaAI 在未 Suspend 时直接 return）。
        /// </summary>
        public void PostConversationCleanup()
        {
            if (!Owner.IsActive()) return;

            PendingPostConversationCleanup = false;
            DebugLogger.Log($"[Brain-PostConvCleanup] {Owner.Name}(Idx={Owner.Index}) 对话结束清理 | 当前={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count}");

            // 安全收武器：确保 NPC 不会提着刀回归巡逻
            Owner.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            Owner.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);

            // 清理当前动作（直接结束，不走 ClearAllActions 的 DoNotRun 锁路径，
            // 因为 ForceUnlockAgent 接下来会统一做解锁，再加锁反而可能残留）
            ClearAllActions(lockPlace: false);
            ClearAllAlerts(); // 对话结束警戒值归零，避免 NPC 恢复正常后立刻重新质问
            AgentControlHelper.ForceUnlockAgent(Owner);
            ResumeVanillaAI();
            InteractedAgent = null;
        }

        /// <summary>Owner Agent 从 Mission 中删除时清理挂起状态。</summary>
        public void OnOwnerDeleted()
        {
            SuspendedAgentIndices.Remove(Owner.Index);
        }

        /// <summary>
        /// 脑空时的默认行为。
        /// ① 护卫 + 有老大 → FollowAgentAction（永久跟随）
        /// ② 其他 → 恢复原版 DailyBehaviorGroup
        ///          ResumeVanillaAI 内部有 HashSet 守卫：没 Suspend 过的直接 return，
        ///          所以每帧调用也是安全的（第一次释放后后续全是空操作）。
        /// </summary>
        private void DecideDefaultBehavior()
        {
            if (!Owner.IsActive()) return;
            ResumeVanillaAI();           
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 警戒值认知更新（Phase 1-2）
        // ═══════════════════════════════════════════════════════════════

        void UpdateAlertCognition(float dt)
        {
            // 全局质问锁：只要有人在质问玩家，不管是不是我自己，都不更新警戒值（因为玩家本身就相当于在暂停对话状态）
            if (ConfrontingBrain != null)
                return;

            // 自己正在与玩家交战中 → 警戒值已由 event_agent_damaged 脉冲拉满，不再更新
            if (CombatManager.IsAgentFightingPlayer(Owner))
                return;

            // 🆕 玩家队友豁免：队友看到玩家蹲下/拔刀/开偷窃UI 不涨警戒（信任玩家），
            // 残留值（如曾被玩家直接攻击的脉冲）照常衰减归零。
            if (IsPlayerTeammate(Owner))
            {
                DecayAlertBreakdown(dt);
                return;
            }

            // Npc看不到玩家 → 衰减
            bool canSeePlayer = NpcSightSystem.CanNpcSeePlayer(Owner);
            bool anySuspicious = false;

            // 🆕 可疑状态感知（2026-08-14 正规路线，用户裁定）：与玩家路径同构——sight 感知 + 读目标状态变量。
            // 目标列表 = NpcSightSystem.TrackedTargets（玩家自动注册 + 随从 OnAgentCreated 注册，预期 ≤5）。
            // 蹲姿：玩家读 Agent.Main.CrouchMode（vanilla AI 在跑，可信）/ NPC 读脑 CrouchPoseActive（人工记录，
            // 设置点同步写入——native CrouchMode 对 Suspend NPC 不可信，反编译实锤 MBAPI 读取）。
            // 拔刀：玩家读引擎事件源 AgentAIController.PlayerWeaponDrawn（OnMainAgentWieldedItemChange 驱动，
            // 无需每帧查武器）/ NPC 读脑 WeaponDrawnActive（各脑 100ms 自报）。
            // 先读状态（O(1) 不花钱），有可疑状态才做视线检查（RayCast 只对可疑者发生，通常 0-2 个）。
            // suspect：玩家 = -1（玩家语义，暖色警戒眼）；他人 = 该 agent Index（冷色眼，双色系）。
            // 独立于 canSeePlayer 门控：守卫可能看不到玩家、但看得到蹲在旁边/拔刀的随从。
            // 友方围观豁免已由上方 IsPlayerTeammate 兜底（其他随从看到随从蹲着/拔刀不涨警戒）。
            var sightTargets = NpcSightSystem.Instance?.TrackedTargets;
            if (sightTargets != null)
            {
                bool crouchHandled = false, weaponHandled = false;   // 同类型只跟第一个看到的人（原 break 语义）
                foreach (var t in sightTargets)
                {
                    if (t == null || !t.IsActive() || t == Owner) continue;
                    bool isPlayer = t == Agent.Main;
                    bool crouching = isPlayer ? t.CrouchMode
                        : AgentAIController.GetBrainForAgent(t)?.CrouchPoseActive == true;
                    bool weaponDrawn = isPlayer
                        ? (AgentAIController.Instance?.PlayerWeaponDrawn ?? false)
                        : AgentAIController.GetBrainForAgent(t)?.WeaponDrawnActive == true;
                    if ((!crouching || crouchHandled) && (!weaponDrawn || weaponHandled)) continue;
                    bool visible = isPlayer ? canSeePlayer
                        : NpcSightSystem.CanAgentSeeTarget(Owner, t, 15f, 120f);
                    if (!visible) continue;
                    int suspect = isPlayer ? -1 : t.Index;

                    if (crouching && !crouchHandled)
                    {
                        // 钉 suspect 上下文（非瞬时脉冲——SetPulseTarget 不加值，只写条目元数据；
                        // 加值是下方 AddAlert 的 0.15/s 持续小量）。不钉则条目默认 -1 = 玩家语义：
                        // 随从蹲着时警戒眼会错成暖色（针对玩家）、BecomeAlarmed 打错人、豁免判定失效。
                        SetPulseTarget(PlayerActionType.Crouching, t.Name?.ToString(), null, -1, suspect);
                        float crouchAmt = dt * 0.15f * GetAlertDistanceMultiplier(t);
                        AddAlert(PlayerActionType.Crouching, crouchAmt);
                        // 1s 闸门降频（DebugLogger 无条件写）：验证节奏下确认感知链路；附带 tracked 总数
                        // 定位「随从没注册进感知目标列表」型故障
                        float now = Mission.Current?.CurrentTime ?? 0f;
                        if (now - _lastCrouchLogTime >= 1f)
                        {
                            _lastCrouchLogTime = now;
                            DebugLogger.Log($"[Brain-Crouch] {Owner.Name}(Idx={Owner.Index}) 看到 {(isPlayer ? "玩家" : t.Name)}(Idx={t.Index}) 蹲着 → +{crouchAmt:F3} 警戒, suspect={(isPlayer ? "-1(玩家)" : t.Index.ToString())}, tracked={sightTargets.Count}");
                        }
                        crouchHandled = true;
                        anySuspicious = true;
                    }
                    if (weaponDrawn && !weaponHandled)
                    {
                        SetPulseTarget(PlayerActionType.WeaponDrawn, t.Name?.ToString(), null, -1, suspect);
                        AddAlert(PlayerActionType.WeaponDrawn, dt * 0.20f * GetAlertDistanceMultiplier(t));
                        weaponHandled = true;
                        anySuspicious = true;
                    }
                }
            }

            // 玩家开启偷窃UI（玩家专属 UI 通道，铁律 18 排除平权；随从"偷窃中" = 蹲姿感知已覆盖
            // + 得手被抓 3.0 脉冲 InlineSteps）
            if (canSeePlayer && StealManager.IsUIOpen)
            {
                AddAlert(PlayerActionType.StealUIOpen, dt * 0.30f * GetAlertDistanceMultiplier());
                anySuspicious = true;
            }
            // 没有任何可疑行为 → 衰减（收刀/站起来/关UI 后警戒值会下降，不再冻结）
            if (!anySuspicious)
                DecayAlertBreakdown(dt);
            // 阶段穿越检测（向上或向下）
            CheckPhaseTransition();
        }

        /// <summary>
        /// 🔴 2026-08-16（K1 血线关切，事件驱动重构，用户裁定）：玩家受击（event_agent_damaged 广播）
        /// → 血线关切冒泡。档位：&lt;0.6 挂彩 / &lt;0.35 重伤，每档触发一次；回血 ≥0.7 重置；冷却 90s
        /// （墙钟秒，跨 Mission 天然正确）；广播半径 15m 已保证距离上限（隔半个战场喊"主公挺住"出戏——
        /// 够不到 = 没看见）；多随从同时收到 → 静态档位标记保证单喊。
        /// 与 M（异步 LLM 情绪化长句）分工：K = 当场秒级确定性喊话，先到；M = 异步安抚，后到，互补不冲突。
        /// 🔴 跳过原因不落日志（用户裁定删除——事件驱动无每帧刷屏，触发才打 [Care] 触发行）。
        /// </summary>
        private void CheckPlayerCareOnDamaged(Agent player, float incomingDamage)
        {
            try
            {
                if (player == null || !player.IsActive()) return;
                double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now < CareCooldownUntilWall) return;
                // OnRegisterBlow 广播时血量未结算 → Health - damage 预估结算后血线（防单击大伤害漏检）
                float hp = MathF.Max(0f, player.Health - incomingDamage);
                float hpRatio = hp / MathF.Max(1f, player.HealthLimit);
                string line = null;
                string levelWord = "";
                if (hpRatio < 0.35f)
                {
                    if (CareHeavyTriggered) return;
                    CareHeavyTriggered = true;
                    CareLowTriggered = true;
                    levelWord = "重伤";
                    // 重伤档双台词随机（LWN_im_care_heavy / LWN_im_care_retreat——防固定句式重复）
                    line = MBRandom.RandomFloat < 0.5f
                        // 本地化：im_care_heavy（玩家可见文本）
                        ? LWNTextHelper.ResolveText("LWN_im_care_heavy", "Hold on, my lord!")
                        // 本地化：im_care_retreat（玩家可见文本）
                        : LWNTextHelper.ResolveText("LWN_im_care_retreat", "You are badly hurt, my lord - fall back!");
                }
                else if (hpRatio < 0.6f)
                {
                    if (CareLowTriggered) return;
                    CareLowTriggered = true;
                    levelWord = "挂彩";
                    // 本地化：LWN_im_care_low（玩家可见文本）
                    line = LWNTextHelper.ResolveText("LWN_im_care_low", "Careful, my lord!");
                }
                else
                {
                    // 回血 ≥0.7 重置档位（防贴脸反复刷屏）
                    if (hpRatio >= 0.7f)
                    {
                        CareLowTriggered = false;
                        CareHeavyTriggered = false;
                    }
                    return;
                }
                CareCooldownUntilWall = now + 90f;
                // 统一说话框架：关切 = 警戒级喊话（Warning 优先级，护主反应）
                SpeechChannel.Say(Owner, line, SpeechPriority.Warning,
                    SpeechContext.FromBrain(this, player, "player_in_danger", null));
                DebugLogger.Log($"[Care] {Owner.Name} 关切（{levelWord} hp={hpRatio:F2}）: {line}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Care] 血线关切失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 玩家友方判定：按 Settings.FriendlyRelationCriteria（config.json，默认同队伍 + 同家族，
        /// 可选同王国）判定 agent 是否被玩家视为友方。统一入口 = FriendlinessHelper.IsFriendlyToPlayer。
        /// 用途：警戒豁免（AddAlert / UpdateAlertCognition——友方看到玩家可疑行为不涨警戒）、
        /// 计划系统目标过滤（RuntimeWorldState——友方不列为攻击目标）等。
        /// 🔴 注意：随从关系自动建立（AgentAIController 补设 Leader，密谋入口依赖）请用
        /// FriendlinessHelper.IsPlayerPartyMember（严格同队伍）——家族成员不是随从，不得设 Leader。
        /// </summary>
        public static bool IsPlayerTeammate(Agent agent)
        {
            return FriendlinessHelper.IsFriendlyToPlayer(agent);
        }

        // 🔴 2026-08-14：人工蹲姿状态（用户裁定方案）——SetCrouchMode 的 flag 对**被脑 Suspend 的
        // NPC** 不可信：CrouchMode 属性反编译实锤 = MBAPI native 读取，flag 需 vanilla AI 消费（Suspend
        // 后无人消费，InlineSteps 两轮实机「不渲染」）。蹲姿设置点全在 mod 自己代码（CrouchInlineState
        // / 扒窃 Rolling），同步写本字段；感知侧（各脑 UpdateAlertCognition 遍历
        // NpcSightSystem.TrackedTargets）读它。玩家路径不经脑，
        // 仍走 Agent.Main.CrouchMode（玩家 vanilla AI 在跑，可信）。
        /// <summary>Owner 当前是否处于蹲姿（人工记录，与 SetCrouchMode 同步置/清）。</summary>
        public bool CrouchPoseActive;

        /// <summary>Owner 当前是否拔刀（各脑 100ms 自报，见 Tick）——目击者脑遍历 TrackedTargets 读本字段
        /// 感知"有人拔刀"（随从拔刀与玩家平权；玩家侧走引擎事件源 AgentAIController.PlayerWeaponDrawn）。</summary>
        public bool WeaponDrawnActive;
        private float _lastWpnRefreshTime = -1f;

        /// <summary>同步写入某 agent 的脑蹲姿状态（无脑 = 忽略；玩家无脑走 CrouchMode 属性路径）。</summary>
        public static void SetCrouchPose(Agent agent, bool crouching)
        {
            var brain = AgentAIController.GetBrainForAgent(agent);
            if (brain != null) brain.CrouchPoseActive = crouching;
        }

        // 蹲姿感知日志 1s 闸门（DebugLogger 无条件写，每帧刷屏会爆炸；只留验证节奏）
        private float _lastCrouchLogTime = -1f;

        /// <summary>
        /// 友方旁观者豁免判定（双向豁免核心，不读开关）：本脑（Owner）是玩家友方旁观者，
        /// 且受害者不是玩家友方 → 玩家对非友方犯罪时本脑应无动于衷（不围观/不质问/不警戒/不护外人）。
        /// 受害者是玩家友方（开关开时玩家侵害友方）→ 不豁免，照常反应。
        /// </summary>
        bool IsAllyBystander(Agent victim)
        {
            return victim != Owner
                && FriendlinessHelper.IsFriendlyToPlayer(Owner)
                && !FriendlinessHelper.IsFriendlyToPlayer(victim);
        }

        /// <summary>
        /// 距离倍率：NPC 离目标越近，警戒值涨得越快；越远涨得越慢。
        /// 0m→1.0x, 15m→0.0x, 线性插值。衰减不受此倍率影响。
        /// 缺省目标 = 玩家（拔刀/偷窃UI 等玩家侧行为沿用）；蹲姿感知传蹲着者本人
        /// （2026-08-14 泛化：随从蹲在 20m 外、守卫就站在随从身边 → 按随从距离算）。
        /// </summary>
        float GetAlertDistanceMultiplier(Agent target = null)
        {
            var t = target ?? Agent.Main;
            if (t == null || !t.IsActive()) return 1.0f;

            float dist = Owner.Position.Distance(t.Position);
            const float maxDist = 15f;
            float ratio = MathF.Clamp(dist / maxDist, 0f, 1f);
            return 1.0f - ratio;
        }

        //随时间自然衰减的警戒值
        void DecayAlertBreakdown(float dt)
        {
            if (_alertBreakdown.Count == 0) return;

            float alertTotal = AlertValue;  // Sum() 按需计算
            // 衰减 0.08/s（2026-08-12 用户裁定：原 0.15/s 过快）。依据（日志实锤）：
            // 旁观脉冲 0.5/刀 + 无抑制冻结 → 原速率下 0.5 掉回 Normal 只要 ~1.7s（Suspicious 阈值 0.25），
            // 守卫看着当街斗殴 3 次都来不及升级喝止（Cautious ≥ 1.0 永远够不到）。
            // 0.08/s：单刀 Suspicious 维持 ~3s（与 _pulseSuppressedUntil 3s 窗口同长）；
            // 3s 窗口内 2~3 刀可叠过 1.0 → Cautious 喝止。副作用：拔刀持续源净增益 0.20-0.08=0.12/s，
            // 拔刀 ~8s 达 Cautious（原 ~20s）——守卫生效更早，符合"多看了一眼就该警告"的节奏。
            float totalDecay = dt * 0.08f;
            if (alertTotal <= 0.0001f) { _alertBreakdown.Clear(); return; }

            var keys = new List<PlayerActionType>(_alertBreakdown.Keys);
            foreach (var key in keys)
            {
                var entry = _alertBreakdown[key];
                float proportion = entry.Value / alertTotal;
                entry.Value -= totalDecay * proportion;
                if (entry.Value <= 0.0001f)
                {
                    _alertBreakdown.Remove(key);  // 移除条目时 TargetName/ItemName 自动清理
                }
                else
                {
                    _alertBreakdown[key] = entry;  // struct 值类型，写回
                }
            }
        }

        //状态迁移检查
        void CheckPhaseTransition()
        {

            

            var newPhase = AlertPhase;

            // 脉冲抑制期间：阶段封顶 Cautious，防止 _lastAlertPhase 提前跳到 Alarmed
            // （抑制结束后下一次 CheckPhaseTransition 自然会推进到真实阶段）
            if (newPhase >= AlarmPhase.Alarmed
                && _pulseSuppressedUntil > 0
                && (Mission.Current?.CurrentTime ?? 0f) < _pulseSuppressedUntil)
            {
                newPhase = AlarmPhase.Cautious;
            }

            if (newPhase == _lastAlertPhase) return;

            if (newPhase > _lastAlertPhase)
            {
                // 🔑 新进入 Alarmed → 注册为目击者（先写证词，后触发质问）
                if (newPhase == AlarmPhase.Alarmed && _lastAlertPhase < AlarmPhase.Alarmed)
                {
                    AgentAIController.Instance?.RegisterWitness(this);
                }

                // 向上穿越：每个目标阶段一个独立事件
                string eventType = newPhase switch
                {
                    AlarmPhase.Suspicious => "BecomeSuspicious",
                    AlarmPhase.Cautious   => "BecomeCautious",
                    AlarmPhase.Alarmed    => "BecomeAlarmed",
                    _ => null
                };
                DebugLogger.Log($"[Brain-Phase] {Owner.Name}(Idx={Owner.Index}) 警戒上升: {_lastAlertPhase} → {newPhase} (警戒值={AlertValue:F2}) 因素=[{FormatBreakdown()}] → 发送 '{eventType}'");
                if (eventType != null)
                    ReceiveEvent(new AIEvent { EventType = eventType, Sender = this });
            }
            else
            {
                // 向下穿越：统一 CalmDown（带 from/to 供清理用）
                DebugLogger.Log($"[Brain-Phase] {Owner.Name}(Idx={Owner.Index}) 警戒下降: {_lastAlertPhase} → {newPhase} (警戒值={AlertValue:F2})");
                ReceiveEvent(new AIEvent
                {
                    EventType = "CalmDown",
                    Sender = this,
                    Args = new object[] { _lastAlertPhase, newPhase }
                });
            }

            _lastAlertPhase = newPhase;
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 警戒值操作（Phase 1）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>加警戒值。返回 false = 本次加值被队友豁免吞掉（调用方应据此跳过配套行为，如质问意图/阶段检查）。</summary>
        public bool AddAlert(PlayerActionType type, float amount)
        {
            // 🆕 玩家友方旁观者豁免（全项目唯一入口——StealBarVM/StealManager 等外部调用点自动覆盖，
            // 无需也不允许在各调用点重复写队友判断）：
            // 友方旁观者不因玩家的可疑/犯罪类行为涨警戒（信任玩家）；不读 AllowHostileOnAllies 开关。
            // 例外：脉冲上下文记录受害者是「玩家友方」（本人被侵害，或开关开时玩家侵害友方）→ 照常涨。
            // ⚠️ 前提约定：所有调用点先 SetPulseTarget 后 AddAlert（内部站点已统一此顺序）。
            // 🔴 2026-08-13 泛化（任何人犯法闭环）：suspect 存在且**非**玩家友方 → 不豁免——
            // 随从犯法被围观：其他随从（友方）豁免不涨警戒（代表玩家阵营）；非友方 NPC 照常涨。
            if (FriendlinessHelper.IsFriendlyToPlayer(Owner) && !IsVictimFriendlyPulse(type) && !IsSuspectHostile(type))
                return false;

            if (!_alertBreakdown.TryGetValue(type, out var entry))
                entry = new AlertEntry();

            entry.Value += amount;
            _alertBreakdown[type] = entry;  // struct 是值类型，写回
            return true;
        }

        /// <summary>
        /// 该类型条目的嫌疑犯是否「非玩家友方」——suspect 化后的豁免例外信号：
        /// suspect 存在且非玩家友方 → true（不豁免，任何人犯法都要涨警戒）；
        /// suspect 未知/玩家/玩家友方（随从）→ false（豁免：随从代表玩家阵营）。
        /// 语义矩阵：suspect 未知 → -1 → 玩家语义 → 豁免；suspect=玩家本人 → 豁免（玩家路径原逻辑）；
        /// suspect=随从（玩家友方）→ 豁免（玩家其他随从看到随从犯法不涨警戒）；
        /// suspect=非友方 NPC → 不豁免（任何人犯法闭环）。与 IsVictimFriendlyPulse OR 关系。
        /// </summary>
        bool IsSuspectHostile(PlayerActionType type)
        {
            if (!_alertBreakdown.TryGetValue(type, out var entry)) return false;
            if (entry.SuspectAgentIndex < 0) return false;   // -1 = 玩家语义
            Agent suspect = FindAgentByIndex(entry.SuspectAgentIndex);
            if (suspect == null) return false;
            return !FriendlinessHelper.IsFriendlyToPlayer(suspect);
        }

        /// <summary>
        /// 该类型条目的脉冲上下文是否指向「玩家友方」受害者——友方旁观者豁免的唯一例外信号：
        /// ① 受害者 = 本脑主人（玩家直接侵害本人，如被攻击/被扒窃抓到）→ 照常涨；
        /// ② 受害者是玩家友方（开关开时玩家侵害友方，如击晕随从）→ 旁观者照常涨（有反应）。
        /// </summary>
        bool IsVictimFriendlyPulse(PlayerActionType type)
        {
            if (Owner.Index < 0 || !_alertBreakdown.TryGetValue(type, out var entry)) return false;
            if (entry.TargetAgentIndex == Owner.Index) return true;
            if (entry.TargetAgentIndex < 0) return false;
            Agent victim = FindAgentByIndex(entry.TargetAgentIndex);
            return victim != null && FriendlinessHelper.IsFriendlyToPlayer(victim);
        }

        /// <summary>Mission 内按 Agent.Index 找 Agent（豁免判定用；遍历成本仅在「友方 Owner + 有脉冲目标」时发生）。</summary>
        static Agent FindAgentByIndex(int index)
        {
            var mission = Mission.Current;
            if (mission == null) return null;
            foreach (var a in mission.Agents)
                if (a.Index == index) return a;
            return null;
        }

        /// <summary>脉冲上下文：设置 AlertEntry 的 TargetName/TargetAgentIndex（不改变 Value，Value 由 AddAlert 加）。
        /// ⚠️ 约定：先 SetPulseTarget 后 AddAlert——AddAlert 内的队友豁免依赖此受害者上下文。
        /// <param name="suspectAgentIndex">嫌疑犯 Agent.Index；缺省 -1 = 玩家语义（AlertTargetIsPlayer 自然成立）。
        /// 非玩家犯法（随从偷窃/攻击）时传作案者 Index，警戒眼变冷色系、BecomeAlarmed 直接参战打嫌疑犯。</param></summary>
        public void SetPulseTarget(PlayerActionType type, string targetName, string itemName, int targetAgentIndex = -1,
            int suspectAgentIndex = -1)
        {
            if (!_alertBreakdown.TryGetValue(type, out var entry))
                entry = new AlertEntry();
            entry.TargetName = targetName;
            entry.ItemName = itemName;
            entry.TargetAgentIndex = targetAgentIndex;
            entry.SuspectAgentIndex = suspectAgentIndex;
            _alertBreakdown[type] = entry;
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 Suspect 化（2026-08-13：任何人犯法闭环）——警戒条目的嫌疑犯推导
        // ═══════════════════════════════════════════════════════════════

        /// <summary>顶条目（值最大）的嫌疑犯 Agent.Index；-1 = 未知/玩家语义。</summary>
        public int TopSuspectAgentIndex
        {
            get
            {
                if (_alertBreakdown.Count == 0) return -1;
                int bestIdx = -1;
                float bestVal = -1f;
                foreach (var kv in _alertBreakdown)
                {
                    if (kv.Value.Value > bestVal)
                    {
                        bestVal = kv.Value.Value;
                        bestIdx = kv.Value.SuspectAgentIndex;
                    }
                }
                return bestIdx;
            }
        }

        /// <summary>警戒是否针对玩家本人：suspect 未知（-1）或 = 玩家 → true（暖色系）；
        /// 随从犯法被围观 → false（冷青蓝色系，HUD 视觉区分「不是针对我」）。</summary>
        public bool AlertTargetIsPlayer
            => TopSuspectAgentIndex < 0
            || (Agent.Main != null && TopSuspectAgentIndex == Agent.Main.Index);

        /// <summary>顶条目的嫌疑犯 Agent（suspect 未知 → null）。Mission 内按 Index 查（复用 FindAgentByIndex）。</summary>
        public Agent TopSuspectAgent()
        {
            int idx = TopSuspectAgentIndex;
            return idx < 0 ? null : FindAgentByIndex(idx);
        }

        /// <summary>调停用：把全部条目的嫌疑犯重映射为玩家（玩家当众认领随从 → 守卫质问链随 suspect 指向玩家）。</summary>
        public void RemapSuspectToPlayer()
        {
            if (Agent.Main == null || _alertBreakdown.Count == 0) return;
            var keys = new List<PlayerActionType>(_alertBreakdown.Keys);
            foreach (var key in keys)
            {
                var entry = _alertBreakdown[key];
                entry.SuspectAgentIndex = Agent.Main.Index;
                _alertBreakdown[key] = entry;
            }
        }

        /// <summary>清空所有警戒值 + 释放质问锁（赔钱/坐牢后调用）</summary>
        /// <summary>格式化警戒因素明细，供日志输出。如 "偷窃=0.50, 蹲下=0.10"；有脉冲目标时追加目标名。</summary>
        string FormatBreakdown()
        {
            if (_alertBreakdown.Count == 0) return "无"; // lwn-ignore: A (debug internal)
            var parts = new List<string>();
            foreach (var kv in _alertBreakdown)
            {
                var entry = kv.Value;
                string detail = $"{kv.Key}={entry.Value:F2}";
                if (!string.IsNullOrEmpty(entry.TargetName))
                    detail += $"➔{entry.TargetName}";
                if (!string.IsNullOrEmpty(entry.ItemName))
                    detail += $":{entry.ItemName}";
                parts.Add(detail);
            }
            return string.Join(", ", parts);
        }

        public void ClearAllAlerts()
        {
            _alertBreakdown.Clear();
            _bubbledPhases.Clear();
            _pulseSuppressedUntil = 0f;
            _lastAlertPhase = AlarmPhase.Normal; // 重置阶段追踪，防止 CheckPhaseTransition 误判下降
            DebugLogger.Log($"[Brain-Alert] {Owner.Name}(Idx={Owner.Index}) ClearAllAlerts: 警戒值归零");
        }

        /// <summary>
        /// 结案广播清警戒：移除所有 TargetName 命中受害者名单的警戒条目（赔钱/坐牢/自首结案时调用）。
        /// 解决"玩家已付钱，其他目击者仍带着旧警戒值升级 Alarmed → 再次质问要账"问题——
        /// 只清本案相关条目，不误伤与本案无关的警戒（如玩家还在蹲着带来的 Crouching）。
        /// 返回是否有条目被清除。
        /// </summary>
        public bool ClearAlertsForVictimNames(HashSet<string> victimNames)
        {
            if (_alertBreakdown.Count == 0 || victimNames == null || victimNames.Count == 0) return false;

            var keys = _alertBreakdown
                .Where(kv => !string.IsNullOrEmpty(kv.Value.TargetName) && victimNames.Contains(kv.Value.TargetName))
                .Select(kv => kv.Key).ToList();
            if (keys.Count == 0) return false;

            foreach (var key in keys)
                _alertBreakdown.Remove(key);

            // 全部清空 → 重置阶段追踪，防止 CheckPhaseTransition 误判下降
            if (_alertBreakdown.Count == 0)
            {
                _bubbledPhases.Clear();
                _pulseSuppressedUntil = 0f;
                _lastAlertPhase = AlarmPhase.Normal;
            }
            DebugLogger.Log($"[Brain-Alert] {Owner.Name}(Idx={Owner.Index}) 结案清除 {keys.Count} 个警戒条目: [{string.Join(", ", keys)}]");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 BubbleSay（Phase 2）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>通用 BubbleSay 入口。传入已组装好的文本，直接显示冒泡。
        /// 🔴 2026-08-12 统一说话框架：收编进 SpeechChannel（说话并联，不占行动队列；前因=通用冒泡）。
        /// 🔴 2026-08-12 分级：stimulus 可注入处境（attacked/seen_crime 等）→ LLM 优先；
        /// 纯氛围冒泡（bubble）→ 直接模板（零延迟）。
        /// <param name="stimulus">刺激类型（润色 prompt 的「当前处境」段依据；bubble = 氛围冒泡走模板）</param>
        /// <param name="speaker">刺激源（对方 agent；润色 prompt 的「对方关系」段依据，如攻击者=主公）</param></summary>
        public void BubbleSay(string text, string stimulus = "bubble", Agent speaker = null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                SpeechChannel.SayPolished(Owner, text, SpeechPriority.Chat,
                    SpeechContext.FromBrain(this, speaker, stimulus, null), budgetS: 1.5f);
            }
        }

        /// <summary>
        /// 尝试对当前 phase + PrimaryAction 发 BubbleSay。
        /// 同 (action, phase) 组合只触发一次。降级后清空高位记录，重新升级可再次触发。
        /// </summary>
        void BubbleSayOnce(AlarmPhase phase)
        {
            var action = PrimaryAction;
            if (action == null) return;

            var key = (action.Value, phase);
            if (_bubbledPhases.Contains(key)) return;

            _bubbledPhases.Add(key);
            BubbleSay(ResolveAlertBubble(phase));
        }

        /// <summary>查 NpcSpeech.csv → 委托 PlaceholderResolver</summary>
        string ResolveAlertBubble(AlarmPhase phase)
        {
            var action = PrimaryAction;
            if (action == null) return null;

            string targetName = null, itemName = null;
            if (_alertBreakdown.TryGetValue(action.Value, out var entry))
            {
                // 如果受害者就是自己，用自称代替第三人称名字（用 Agent.Index 精确匹配）
                if (entry.TargetAgentIndex >= 0 && entry.TargetAgentIndex == Owner.Index)
                {
                    targetName = AttitudeSystem.GetSelfReference(
                        (Owner.Character as CharacterObject)?.HeroObject);
                }
                else
                {
                    targetName = entry.TargetName;
                }
                itemName = entry.ItemName;
            }

            // 所有占位符（含 {TARGET}/{ITEM}）统一走 PlaceholderResolver
            // variantSeed = Owner.Index：同一 agent 同一情景台词稳定（人格一致），不同 agent 选不同
            // 变体 → 同屏多 NPC 冒泡不再全员复读同一句（2026-08-12 日志实锤：4 个守卫同帧"怎么回事？！"）
            return NpcSpeechResolver.Resolve(
                $"AlertBubble_{action}_{phase}",
                speaker: (Owner.Character as CharacterObject)?.HeroObject,
                listener: TaleWorlds.CampaignSystem.Hero.MainHero,
                evt: null,
                targetName: targetName,
                itemName: itemName,
                speakerCharacter: Owner.Character as CharacterObject,
                variantSeed: (uint)Owner.Index
            );
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 L3 质问（Phase 3-4）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 对指定目标直接开战——个体战斗唯一入口，DeferredCombat 事件分支与 StartL3CombatJoin 同源复用：
        /// 推进 PendingWorldEvent 到 Confrontation + 清动作 + 解锁 + 入队 FightEnemyAction（儿童恐惧逃离）。
        /// </summary>
        void StartCombatAgainst(Agent target)
        {
            if (target == null || target == Owner) return;

            SetNpcIntent(NpcIntentType.Fighting, target);

            // 推进 PendingWorldEvent 到 Confrontation
            var pending = AgentAIController.Instance?.PendingWorldEvent;
            if (pending != null && pending.Stage < EventStage.Confrontation)
            {
                var existing = WorldEventStore.Find(pending.EventId);
                WorldEventStore.TransitionStage(existing ?? pending, EventStage.Confrontation,
                    Hero.MainHero?.StringId,
                    // 阶段升级原因：现场打了起来（会出现在赔款涨价说明里给玩家看）
                    LWNTextHelper.ResolveText("LWN_brain_escalation_fighting", "a fight broke out"));
            }

            InteractedAgent = target;
            ClearAllActions();
            AgentControlHelper.ForceUnlockAgent(Owner); // ClearAllActions 会后置 DoNotRun|NoAttack（FightEnemyAction.OnStart 亦有兜底）
            // 儿童不参战：恐惧逃离
            if (IsChildOwner) { EnqueueAction(MoveToPositionAction.FleeFrom(Owner, target)); return; }
            // 🔴 2026-08-12：脉冲受害者 == 自己（被打才还手）→ 玩家收刀可停战（被动反击）；
            // 旁观执法（见义勇为/Alarmed 管闲事）→ 执法到底。日志实锤：受害者被 AttackAlly 拉满
            // Alarmed → 走本路径入队 canCease=false → 玩家收刀对方不停手——受害者被误当执法者。
            bool selfIsVictim = _alertBreakdown.TryGetValue(PlayerActionType.AttackAlly, out var ae)
                && ae.TargetAgentIndex == Owner.Index;
            EnqueueAction(new FightEnemyAction(target, canCeaseOnPlayerSheathe: selfIsVictim));
        }

        /// <summary>
        /// L3 战斗加入：跳过质问，直接对玩家开战。
        /// 两条入口：① 玩家已在战斗中 → 不废话直接参战（BecomeAlarmed 内联判定）；
        /// ② MCM 开关「警戒拉满直接开战」→ 警戒 Alarmed 后不走质问，复用本路径。
        /// 只对自身开战（复用 StartCombatAgainst = DeferredCombat 分支同源），
        /// 不广播——警戒拉满是自己的事，不拖旁人下水（广播会波及玩家随从）。
        /// </summary>
        void StartL3CombatJoin()
        {
            Agent player = Agent.Main;
            if (player == null) return;

            StartCombatAgainst(player);
            DebugLogger.Log($"[Brain-Alarmed] {Owner.Name}(Idx={Owner.Index}) 跳过质问直接加入战斗 | AlertValue={AlertValue:F2} | 模式={Settings.Instance.AlarmedDirectCombat}");
        }

        /// <summary>
        /// L3 质问：NPC 主动质问玩家（ConfrontingBrain 锁 + AlertForceConversationAction → 原版对话流）。
        /// 🔴 internal（2026-08-13）：调停交互（ExecuteIntervene）复用本链质问玩家——守卫停战后
        /// 走同一条质问管线（现有链：Follow+LookAt+强制对话 → 质问脚本 → 赔偿子树）。
        /// </summary>
        internal void StartL3Confrontation()
        {
            Agent player = Agent.Main;
            if (player == null) return;

            // 全局质问锁：已有其他 NPC 在质问玩家 → 跳过
            if (ConfrontingBrain != null && ConfrontingBrain != this)
            {
                DebugLogger.Log($"[Brain-Lock] {Owner.Name}(Idx={Owner.Index}) 想质问玩家但 {ConfrontingBrain.Owner.Name}(Idx={ConfrontingBrain.Owner.Index}) 正在质问中，跳过");
                return;
            }

            ClearAllActions();
            InteractedAgent = player;

            // 根据 PrimaryAction 确定 ConfrontationType detail。
            // 若已有显式设置（如 WitnessCrime 路径已指定 Recover/Stop），优先保留。
            var existingDetail = _currentIntent.Type == NpcIntentType.Confronting
                ? _currentIntent.InterceptDetail
                : (ConfrontationType?)null;
            var detail = existingDetail ?? (PrimaryAction switch
            {
                PlayerActionType.Crouching or PlayerActionType.WeaponDrawn => ConfrontationType.Deter,
                PlayerActionType.StealUIOpen => ConfrontationType.Search,
                PlayerActionType.Steal => StealManager.HasStolenItemsFrom(Owner)
                    ? ConfrontationType.Recover   // 确实偷到了 → 追回赃物
                    : ConfrontationType.Deter,    // 偷窃未遂（红区手滑）→ 驱离警告
                PlayerActionType.AttackAlly or PlayerActionType.Knockout or PlayerActionType.AttackCivilian => ConfrontationType.Stop,
                PlayerActionType.SuspectFlee => ConfrontationType.Stop,
                _ => ConfrontationType.Deter
            });
            SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: detail);

            // 占领全局质问锁
            ConfrontingBrain = this;
            DebugLogger.Log($"[ConvLock] Acquire by {Owner.Name}(Idx={Owner.Index}) | reason=StartL3Confrontation");

            // RegisterWitness 已在 CheckPhaseTransition 进入 Alarmed 时调用，证词已入 PendingWorldEvent
            // 统一走 DialogueInjector 管道：CrimeDialogueBuilder 构建脚本 → DialogueInjector 注入 → 原版 ConversationManager
            // 🔴 目标=玩家但语义是"对峙走近"（随后 AlertForceConversationAction 强开对话），
            // 不可换 VanillaFollowAction（2026-08-13 用户裁定）：对话结束 Resume 后原版跟随会接管，
            // 对峙 NPC 会变成永久跟随。
            EnqueueAction(new FollowAgentAction(player, false, radius: 2f, angleOffset: 0f, stopDistance: 1.5f));
            EnqueueAction(new LookAtAction(player, 0.0f));
            EnqueueAction(new AlertForceConversationAction());
            //对话过程中本身就是持续的，不需要一个StayAction来占位
            //EnqueueAction(new StayAction(player));
        }
        public void Tick(float dt)
        {
            if(Owner == Agent.Main)
            {
                return;
            }

            // 战斗模式下 AgentBrain 不运行——原生 AI 接管所有战斗行为
            // （事件处理/行为队列/警戒认知/默认行为恢复 均无意义）
            if (Settings.Instance.IsInteractionDisabled())
                return;

            // 🔴 2026-08-14：自报武器状态（100ms 降频）——目击者脑遍历 TrackedTargets 读本字段
            // 感知"有人拔刀"（随从拔刀与玩家平权；玩家侧走引擎事件源 AgentAIController.PlayerWeaponDrawn，
            // 本字段只服务 NPC）。MainWpn 主手 / OffWpn 副手任一持械 = 拔刀。
            if (Mission.Current != null && Mission.Current.CurrentTime - _lastWpnRefreshTime >= 0.1f)
            {
                _lastWpnRefreshTime = Mission.Current.CurrentTime;
                WeaponDrawnActive = V.MainWpn(Owner) != EquipmentIndex.None
                    || V.OffWpn(Owner) != EquipmentIndex.None;
            }

            // 安全兜底：如果持锁者已不活跃，释放质问锁
            if (ConfrontingBrain == this && !Owner.IsActive())
            {
                ConfrontingBrain = null;
                DebugLogger.Log($"[ConvLock] Release by {Owner.Name}(Idx={Owner.Index}) | reason=OwnerInactive");
                return;
            }

            if (EffectiveAction == null)
            {
                // 🔴 空窗守卫（D2，单脑化重构）：计划执行中不恢复默认行为。
                // 执行器 100ms 轮询完成检测，动作完成瞬间脑就 OnEnd 出队，下一步计划动作
                // 要等下一轮轮询才入队——空窗期若跑 DecideDefaultBehavior（跟随/恢复原版 AI），
                // 跟随动作占住队头，下一步计划动作排在其后先执行跟随——比显式打架更隐蔽。
                // 哨兵 = ExecutingCommand 意图（order_execute_plan 设一次无二次设置；收尾意图复位 None 放行）。
                // null-guard 对齐本类既有模式（Owner 可能无意图）。
                if (_currentIntent?.Type != NpcIntentType.ExecutingCommand)
                    DecideDefaultBehavior();
            }


            // 如果当前没有动作，从队列取一个
            if (_currentAction == null && _actionQueue.Count > 0)
            {
                _currentAction = _actionQueue.Dequeue();
                DebugLogger.Log($"[Brain-Tick] {Owner.Name}(Idx={Owner.Index}) 开始执行 {_currentAction.GetType().Name} | 队列剩余={_actionQueue.Count}");
                _currentAction.OnStart(Owner);
                // 🔴 经历旁白（2026-08-11）：出队 = 动作真正开始执行 → 白名单翻译记录（无幽灵）
                RecordActionNarration(_currentAction);
            }


            // 执行当前动作
            if (_currentAction != null)
            {
                _currentAction.OnTick(Owner, dt);

                if (_currentAction.IsFinished(Owner))
                {
                    DebugLogger.Log($"[Brain-Tick] {Owner.Name}(Idx={Owner.Index}) 完成 {_currentAction.GetType().Name}");
                    _currentAction.OnEnd(Owner);
                    _currentAction = null; // 下一帧会取新的

                    // 战斗结束且无排队 → 清除 Fighting 意图
                    if (CurrentIntent.Type == NpcIntentType.Fighting && _actionQueue.Count == 0)
                    {
                        SetNpcIntent(NpcIntentType.None);
                    }
                }
            }

            // 警戒值更新
            _alertCognitionTimer += dt;
            if (_alertCognitionTimer >= _alertCognitionInterval)
            {
                UpdateAlertCognition(_alertCognitionTimer);  // 传入累积 dt，不是原始帧 dt
                _alertCognitionTimer = 0f;
            }
        }




    }
}
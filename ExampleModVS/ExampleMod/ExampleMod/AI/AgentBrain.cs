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
        public string EventType; // 例如 "WitnessCrime", "AttackOrder"
        public object Sender;    // 谁发的
        public object[] Args;    // 参数 (目标ID, 坐标等)
    }

    public class AgentBrain
    {
        /// <summary>已暂停原版 AI 的 Agent.Index 集合。AiSuspendPatch 读取以拦截 Navigator。</summary>
        internal static readonly HashSet<int> SuspendedAgentIndices = new HashSet<int>();

        /// <summary>查询任意 Agent 是否处于击晕 StayAction 状态。</summary>
        /// <summary>是否处于击晕状态（专用标记，避免依赖 CurrentAction 时序问题）</summary>
        internal bool IsStunned;

        // ═══════════════════════════════════════════════════════════════
        // 🆕 NpcIntent — NPC 高层意图状态机
        // ═══════════════════════════════════════════════════════════════

        private NpcIntent _currentIntent = new NpcIntent(NpcIntentType.None);
        private NpcIntent _previousIntent;

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

        /// <summary>
        /// 设置 NPC 当前意图，同时记录上一个意图。
        /// 所有意图变更必须走此方法，类内部也不允许直接写 _currentIntent。
        /// </summary>
        public void SetNpcIntent(NpcIntentType type, Agent target = null, ConfrontationType? interceptDetail = null, CommandIntentType? commandDetail = null)
        {
            _previousIntent = _currentIntent;
            _currentIntent = new NpcIntent(type, target, interceptDetail, commandDetail);
        }

        /// <summary>计划收尾 → 恢复默认意图（§10：值优先，Following）。仅当没有新命令覆盖时恢复。</summary>
        private void OnPlanExecutorFinished(PlanExecutor executor)
        {
            try
            {
                if (Owner == null || !Owner.IsActive()) return;
                if (_currentIntent != null && _currentIntent.Type == NpcIntentType.ExecutingCommand)
                {
                    if (Leader != null && Leader.IsActive())
                        SetNpcIntent(NpcIntentType.Following, Leader);
                    else
                        SetNpcIntent(NpcIntentType.None);
                }
                // 恢复默认行为的动作侧由 DecideDefaultBehavior 自动处理（脑空 → 护卫跟随/原版 AI）
            }
            catch { }
        }

        /// <summary>ReactiveAgent 反应通道（§6）：清当前行为并入队反应动作。
        /// 与 ReceiveEvent 内联分支同权——ReactiveAgent 是 brain 事件处理的内部扩展。</summary>
        internal void RunReactiveAction(IAtomicAction action)
        {
            if (action == null || !Owner.IsActive()) return;
            ClearAllActions();
            EnqueueAction(action);
        }

        /// <summary>计划调试入口（plan_debug 用）：与 order_execute_plan 分支同构但绕开事件闸门。</summary>
        internal void EnqueueActionInternal(IAtomicAction action)
        {
            if (action == null) return;
            EnqueueAction(action);
        }

        internal void ClearAllActionsInternal()
        {
            ClearAllActions();
        }

        internal void OnPlanFinishedDebug(PlanExecutor executor)
        {
            OnPlanExecutorFinished(executor);
        }

        public static bool IsKnockedOut(Agent agent)
        {
            if (agent == null) return false;
            var brain = AgentAIController.GetBrainForAgent(agent);
            // 优先检查专用标记（CurrentAction 可能尚未出队，有时序问题）
            if (brain?.IsStunned == true) return true;
            return brain?.CurrentAction is StayAction stay && stay.IsKnockout;
        }

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
        // --- 核心：决策中枢 ---
        public void ReceiveEvent(AIEvent aiEvent)
        {
            // 战斗模式下不处理任何事件——原生 AI 接管所有战斗行为
            if (Settings.Instance.IsInteractionDisabled())
                return;

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
                AgentHudMissionView.AgentSay(Owner,
                    // 冒泡回复：被喊名字时的回应（{NAME}=喊话的人）
                    LWNTextHelper.ResolveCompound("LWN_brain_comehere_reply", ("NAME", targetAgent.Name)));
                InteractedAgent = targetAgent;
                ClearAllActions();
                EnqueueAction(new LookAtAction(targetAgent, 0.3f));
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
                EnqueueAction(new FollowAgentAction(targetAgent, run: true,keepFollow:true));

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
                    AgentHudMissionView.AgentSay(Owner, LWNTextHelper.ResolveText("LWN_plan_reject", "I cannot do this."));
                    return;
                }
                SetNpcIntent(NpcIntentType.ExecutingCommand, target,
                    commandDetail: PlanExecutor.ParseIntentType(intentType ?? plan?.Intent?.IntentType));
                InteractedAgent = target;
                ClearAllActions();
                EnqueueAction(new ExecutePlanAction(executor));
                // 计划收尾 → 恢复默认（Following）；仅当没有新命令覆盖当前意图时
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
                if (IsChildOwner) { EnqueueAction(new FleeFromAction(targetAgent)); return; }
                EnqueueAction(new FightEnemyAction(targetAgent));
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

                // 受害者身份日志：区分自己是受害者（应反击）还是旁观者（看护主条件），排查小孩无法参战用
                DebugLogger.Log($"[Brain-Receive] {Owner.Name}(Idx={Owner.Index}) 收到事件 'event_agent_damaged' | victim={victim.Name}(Idx={victim.Index}) | 是否自己={Owner == victim} | 当前行为={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count} | 阶段={_lastAlertPhase}");

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
                    shouldHelp = true;
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


                

                    // BubbleSay 参战理由（走 NpcSpeech.csv + PlaceholderResolver 标准管道）
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
                    BubbleSay(line);

                    SetNpcIntent(NpcIntentType.Fighting, attacker);
                    InteractedAgent = attacker;
                    ClearAllActions();
                    AgentControlHelper.ForceUnlockAgent(Owner); // ClearAllActions 会后置 DoNotRun|NoAttack，FightEnemyAction 需要清除
                    // 儿童不参战：恐惧逃离；大人才进战斗
                    if (IsChildOwner)
                        EnqueueAction(new FleeFromAction(attacker));
                    else
                        EnqueueAction(new FightEnemyAction(attacker));

                    //时序处理： 受到玩家攻击 → 警戒值立即拉满（脉冲），不应慢慢爬
                    if (attacker == Agent.Main)
                    {
                        // 先写脉冲上下文再加值：受害者 = 真受害者——本人被攻击 → 上下文指向本人，
                        // 队友豁免（AddAlert 内判定）因此不豁免；队友围观玩家打别人 → 上下文指向他人 → 豁免。
                        SetPulseTarget(PlayerActionType.AttackAlly, victim.Name, null, victim.Index);
                        if (AddAlert(PlayerActionType.AttackAlly, 3.0f))  // 队友围观豁免（false）→ 跳过阶段检查
                            CheckPhaseTransition();
                    }
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

                    // 🆕 友方旁观者豁免（双向豁免核心，不读开关）：玩家对非友方犯罪，
                    // 友方旁观者无动于衷——不围观、不质问、不警戒（直接忽略本事件）。
                    // 受害者是玩家友方（开关开时玩家侵害友方）→ 不豁免，照常围观/质问。
                    if (criminal == Agent.Main && IsAllyBystander(victim)) return;

                    // ── 警戒脉冲：区分偷窃 vs 攻击 vs 击晕（criminal==玩家时）──
                    // 认输场景：受害者大脑的 PendingPostConversationCleanup 已置 true，
                    // 此时战斗已结束但围观不应被误判为偷窃——跳过犯罪分类，仅保留围观行为。
                    // victim 可为 null（保管箱偷窃抓现行）→ GetBrainForAgent 对 null 取 .Index 会 NRE，先判空
                    var victimBrain = victim != null ? AgentAIController.GetBrainForAgent(victim) : null;
                    bool isSurrenderScene = victimBrain?.PendingPostConversationCleanup == true;

                    if (criminal == Agent.Main)
                    {
                        if (!isSurrenderScene)
                        {
                            // 先写脉冲上下文再加值：受害者 = 真受害者（victim.Index）。
                            // 队友豁免由 AddAlert 内部判定——队友围观（上下文 ≠ 本人）豁免不质问；
                            // 队友本人被侵害（上下文 = 本人）照常分类 + 指控。
                            if (IsKnockedOut(victim))
                            {
                                SetPulseTarget(PlayerActionType.Knockout, victim?.Name, null, victim?.Index ?? -1);
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
                                SetPulseTarget(PlayerActionType.AttackAlly, victim?.Name, null, victim?.Index ?? -1);
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
                                SetPulseTarget(PlayerActionType.Steal, victim?.Name, null, victim?.Index ?? -1);
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
                    // （放在 ReactionDecisionAction 入队之后，让 L3 质问覆盖围观动作）
                    if (criminal == Agent.Main && IsKnockedOut(victim))
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

                    // 🆕 友方旁观者豁免（同 GatherOnLook）：玩家对非友方犯罪，友方旁观者无动于衷。
                    if (thief == Agent.Main && IsAllyBystander(victim)) return;
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
                SetNpcIntent(NpcIntentType.KnockedOut);
                IsStunned = true;
                ClearAllActions();
                EnqueueAction(new StayAction(null, false, isKnockout: true));
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
                // 大脑空闲（无当前动作且无排队）或只是待机 → 可以插入 LookAt
                if (EffectiveAction == null || EffectiveAction is StayAction)
                {
                    EnqueueAction(new LookAtAction(Agent.Main, 0.0f));
                    EnqueueAction(new StayAction(Agent.Main));
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

                // 玩家已经在战斗中 → 跳过质问，直接加入战斗
                //可能有时序问题
                if (CombatManager.IsPlayerInCombat )
                {
                    //通过别的方式进入战斗，就不在这里了
                    //StartL3CombatJoin();
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
                SetNpcIntent(NpcIntentType.Surrendering, Agent.Main);
            }

            if (aiEvent.EventType == "event_player_surrendered")
            {
                // 玩家主动认输 → 立即停战，清掉 FightEnemyAction，进入 StayAction 原地待命。
                // 对话中谈拢了 → EndConversation 时 PostConversationCleanup 收尾。
                // 对话中谈崩了 → event_surrender_refused 会清 StayAction、重入 FightEnemyAction。
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
                AgentAIController.Instance?.BroadcastEventInRange(
                    Owner.Position, 20f, "WitnessCrime", true, Owner, Agent.Main);
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

        private void ClearAllActions(bool lockPlace = true)
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

            //原版玩家本身就可以控制随从是否跟随自己，所以这里不需要管
            /*
            if (_isGuardMode && Leader != null && Leader.IsActive())
            {
                // keepFollow:true 必须带——否则动作到达 stopDistance 即自结束，
                // 下帧 Tick 又因 EffectiveAction==null 重新入队，形成每帧 入队→完成 循环刷屏。
                // keepFollow 时 IsFinished 恒 false，动作常驻直到被 ClearAllActions 等流程接管。
                EnqueueAction(new FollowAgentAction(Leader, run: true, keepFollow: true));
            }
            else
            {
                ResumeVanillaAI();
            }
            */
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
            if (!NpcSightSystem.CanNpcSeePlayer(Owner))
            {
                DecayAlertBreakdown(dt);
            }
            else
            {
                float distMult = GetAlertDistanceMultiplier();
                bool anySuspicious = false;
                //玩家下蹲状态
                if (Agent.Main.CrouchMode)
                {
                    AddAlert(PlayerActionType.Crouching, dt * 0.15f * distMult);
                    anySuspicious = true;
                }
                //玩家拔刀状态
                if (IsPlayerWeaponDrawn())
                {
                    AddAlert(PlayerActionType.WeaponDrawn, dt * 0.20f * distMult);
                    anySuspicious = true;
                }
                //玩家开启偷窃UI
                if (StealManager.IsUIOpen)
                {
                    AddAlert(PlayerActionType.StealUIOpen, dt * 0.30f * distMult);
                    anySuspicious = true;
                }
                // 没有任何可疑行为 → 衰减（收刀/站起来/关UI 后警戒值会下降，不再冻结）
                if (!anySuspicious)
                    DecayAlertBreakdown(dt);
            }
            // 阶段穿越检测（向上或向下）
            CheckPhaseTransition();
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

        //玩家拔刀状态：主手或副手有武器
        bool IsPlayerWeaponDrawn()
        {
            var main = Agent.Main;
            if (main == null) return false;
            // MainWpn 主手 OffWpn 副手
            return V.MainWpn(main) != EquipmentIndex.None
                || V.OffWpn(main) != EquipmentIndex.None;
        }

        /// <summary>
        /// 距离倍率：NPC 离玩家越近，警戒值涨得越快；越远涨得越慢。
        /// 0m→1.0x, 15m→0.0x, 线性插值。衰减不受此倍率影响。
        /// </summary>
        float GetAlertDistanceMultiplier()
        {
            var player = Agent.Main;
            if (player == null || !player.IsActive()) return 1.0f;

            float dist = Owner.Position.Distance(player.Position);
            const float maxDist = 15f;
            float t = MathF.Clamp(dist / maxDist, 0f, 1f);
            return 1.0f - t;
        }

        //随时间自然衰减的警戒值
        void DecayAlertBreakdown(float dt)
        {
            if (_alertBreakdown.Count == 0) return;

            float alertTotal = AlertValue;  // Sum() 按需计算
            float totalDecay = dt * 0.15f;
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
            if (FriendlinessHelper.IsFriendlyToPlayer(Owner) && !IsVictimFriendlyPulse(type))
                return false;

            if (!_alertBreakdown.TryGetValue(type, out var entry))
                entry = new AlertEntry();

            entry.Value += amount;
            _alertBreakdown[type] = entry;  // struct 是值类型，写回
            return true;
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
        /// ⚠️ 约定：先 SetPulseTarget 后 AddAlert——AddAlert 内的队友豁免依赖此受害者上下文。</summary>
        public void SetPulseTarget(PlayerActionType type, string targetName, string itemName, int targetAgentIndex = -1)
        {
            if (!_alertBreakdown.TryGetValue(type, out var entry))
                entry = new AlertEntry();
            entry.TargetName = targetName;
            entry.ItemName = itemName;
            entry.TargetAgentIndex = targetAgentIndex;
            _alertBreakdown[type] = entry;
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

        /// <summary>通用 BubbleSay 入口。传入已组装好的文本，直接显示冒泡。</summary>
        public void BubbleSay(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                DebugLogger.Log($"[BubbleSay] {Owner.Name}(Idx={Owner.Index}): \"{text}\"");
                AgentHudMissionView.AgentSay(Owner, text);
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
            return NpcSpeechResolver.Resolve(
                $"AlertBubble_{action}_{phase}",
                speaker: (Owner.Character as CharacterObject)?.HeroObject,
                listener: TaleWorlds.CampaignSystem.Hero.MainHero,
                evt: null,
                targetName: targetName,
                itemName: itemName,
                speakerCharacter: Owner.Character as CharacterObject
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
            if (IsChildOwner) { EnqueueAction(new FleeFromAction(target)); return; }
            EnqueueAction(new FightEnemyAction(target));
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

        void StartL3Confrontation()
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
                PlayerActionType.AttackAlly or PlayerActionType.Knockout => ConfrontationType.Stop,
                PlayerActionType.SuspectFlee => ConfrontationType.Stop,
                _ => ConfrontationType.Deter
            });
            SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: detail);

            // 占领全局质问锁
            ConfrontingBrain = this;
            DebugLogger.Log($"[ConvLock] Acquire by {Owner.Name}(Idx={Owner.Index}) | reason=StartL3Confrontation");

            // RegisterWitness 已在 CheckPhaseTransition 进入 Alarmed 时调用，证词已入 PendingWorldEvent
            // 统一走 DialogueInjector 管道：CrimeDialogueBuilder 构建脚本 → DialogueInjector 注入 → 原版 ConversationManager
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

            // 安全兜底：如果持锁者已不活跃，释放质问锁
            if (ConfrontingBrain == this && !Owner.IsActive())
            {
                ConfrontingBrain = null;
                DebugLogger.Log($"[ConvLock] Release by {Owner.Name}(Idx={Owner.Index}) | reason=OwnerInactive");
                return;
            }

            if (EffectiveAction == null)
            {
                DecideDefaultBehavior();
            }


            // 如果当前没有动作，从队列取一个
            if (_currentAction == null && _actionQueue.Count > 0)
            {
                _currentAction = _actionQueue.Dequeue();
                DebugLogger.Log($"[Brain-Tick] {Owner.Name}(Idx={Owner.Index}) 开始执行 {_currentAction.GetType().Name} | 队列剩余={_actionQueue.Count}");
                _currentAction.OnStart(Owner);
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
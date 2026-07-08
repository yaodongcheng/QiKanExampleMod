using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public bool IsInStayMode => _currentAction is StayAction;

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

        public AgentBrain(Agent agent)
        {
            Owner = agent;
            _memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
        }
       
        public void SetGuardMode(bool isGuard)
        {
            _isGuardMode = isGuard;
        }
        public void SetLeader(Agent newLeader)
        {
            if (newLeader == Owner) return; // 不能认自己做老大
            Leader = newLeader;
        }
        // --- 核心：决策中枢 ---
        public void ReceiveEvent(AIEvent aiEvent)
        {
            DebugLogger.Log($"[Brain-Receive] {Owner.Name}(Idx={Owner.Index}) 收到事件 '{aiEvent.EventType}' | 当前行为={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count} | 阶段={_lastAlertPhase}");
            if (aiEvent.EventType == "ComeHere")
            {
                Agent targetAgent = (Agent)aiEvent.Args[0];
                AgentHudMissionView.AgentSay(Owner, $"{targetAgent.Name},你在叫我吗？");
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
                InteractedAgent = targetAgent;
                ClearAllActions();
                EnqueueAction(new FollowAgentAction(targetAgent, run: true,keepFollow:true));

            }
            if(aiEvent.EventType == "order_attack")
            {
                
                Agent targetAgent = aiEvent.Args[0] as Agent;
                
                if (targetAgent == null || targetAgent == Owner)
                    return;
                InteractedAgent = targetAgent;
                InformationManager.DisplayMessage(new InformationMessage($"Agent {Owner.Name} 收到攻击命令，目标是 {targetAgent.Name}", Colors.Red));
                ClearAllActions();
                EnqueueAction(new FightEnemyAction(targetAgent));
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
                if (attacker == Leader) return;
                if(!victim.IsActive()) return;
                if(attacker == victim) return;

                var victimMemory = AllNpcMemoryManager.GetMemoryForAgent(victim);
                
                InformationManager.DisplayMessage(new InformationMessage($"AgentBrain - event_agent_damaged: {attacker.Name} 对 {victim.Name} 造成了伤害", Colors.Yellow));
                // --- 核心护主逻辑 ---

                bool shouldHelp = false;
                //护卫模式下，领导被攻击
                if ((Leader != null && victim == Leader && _isGuardMode) || Owner == victim)
                {
                   shouldHelp = true;
                }
                else if(Owner!=victim && victimMemory._profile.Clan == _memory._profile.Clan)
                {
                    if(victimMemory._profile.Clan == _memory._profile.Clan)                    
                        shouldHelp = true;
                    if(victimMemory._profile.Kingdom == _memory._profile.Kingdom)
                    {
                        if(victimMemory._profile.BaseHero!= null && _memory._profile.BaseHero != null)
                        {
                            if(victimMemory._profile.BaseHero.IsFriend(_memory._profile.BaseHero))
                            {
                                shouldHelp = true;
                            }
                        }
                    }
                }


                if (shouldHelp)
                {
                    if (_currentAction is FightEnemyAction currentFight)
                    {
                        // 如果我正在打的人，就是现在伤害老大的人
                        if (currentFight.TargetEnemy == attacker)
                        {
                            return;
                        }
                    }
                    InteractedAgent = attacker;
                    ClearAllActions();
                    EnqueueAction(new FightEnemyAction(attacker));
                }
            }
            if (aiEvent.EventType == "EndInteraction")
            {
                Agent target = (Agent)aiEvent.Args[0];
                if (InteractedAgent == target)
                {
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

                    // ── 🆕 警戒脉冲：所有目击者统一加值（criminal==玩家时）──
                    if (criminal == Agent.Main)
                    {
                        AddAlert(PlayerActionType.Steal, 2.0f);
                        SetPulseTarget(PlayerActionType.Steal, victim.Name, null);
                        _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
                    }

                    ClearAllActions();
                    InteractedAgent = criminal;

                    // ── 角色分流 ──
                    if (Owner == victim && criminal == Agent.Main)
                    {
                        // 受害者：直接指控
                        var conflictData = new PendingConflict(
                    eventId: $"Theft_{TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours}",
                    topicName: "当众行窃",
                    goalDesc: $"要求 {criminal.Name} 立刻归还财物并赔偿精神损失",
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
                ClearAllActions();
                EnqueueAction(new StayAction(null, false));
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

                InformationManager.DisplayMessage(new InformationMessage($"[冒泡问候] {Owner.Name} (Index:{Owner.Index}) 决定向你打招呼 (概率:{prob:P0}, 声望:{honor})"));

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
                    InformationManager.DisplayMessage(new InformationMessage($"[冒泡问候] {Owner.Name}: \"{line}\""));
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
                if (_currentAction == null || _currentAction is StayAction)
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
                StartL3Confrontation();
            }
            if (aiEvent.EventType == "CalmDown")
            {
                var fromPhase = (AlarmPhase)aiEvent.Args[0];
                var toPhase   = (AlarmPhase)aiEvent.Args[1];

                // 清除高位 bubbled 记录，允许重新升级后再次触发
                _bubbledPhases.RemoveWhere(k => k.Item2 > toPhase);

                // Alarmed→* 或 →Normal：完全清理行为链
                if (fromPhase >= AlarmPhase.Alarmed || toPhase == AlarmPhase.Normal)
                {
                    ClearAllActions();
                    ResumeVanillaAI();
                }
                // Cautious→Suspicious：只取消 LookAt
                else if (fromPhase == AlarmPhase.Cautious && _currentAction is LookAtAction)
                {
                    AgentControlHelper.StopLooking(Owner);
                    _currentAction.RequestInterrupt();
                    // 下一帧 Tick 走标准路径: IsFinished→true → OnEnd → _currentAction=null → dequeue next
                }
            }



        } // ReceiveEvent

        // 辅助判断逻辑
       

        // --- 动作执行系统 ---
        private void EnqueueAction(IAtomicAction action)
        {
            DebugLogger.Log($"[Brain-Enqueue] {Owner.Name}(Idx={Owner.Index}) 入队 {action.GetType().Name} | 当前行为={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count}→{_actionQueue.Count + 1}");
            // 从空脑到有 Action 的转换：一次性接管原版 AI（SuspendVanillaAI 内部幂等）
            if (_currentAction == null && _actionQueue.Count == 0)
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

        private void ClearAllActions()
        {
            bool hadActions = _currentAction != null || _actionQueue.Count > 0;
            DebugLogger.Log($"[Brain-Clear] {Owner.Name}(Idx={Owner.Index}) 清空动作 | 当前={_currentAction?.GetType().Name ?? "null"} | 队列={_actionQueue.Count} | hadActions={hadActions}");

            // 释放全局质问锁
            if (ConfrontingBrain == this)
            {
                ConfrontingBrain = null;
                DebugLogger.Log($"[Brain-Lock] {Owner.Name}(Idx={Owner.Index}) 质问锁已释放");
            }

            if (_currentAction != null) _currentAction.OnEnd(Owner);
            _currentAction = null;
            _actionQueue.Clear();

            //Owner.TryToSheathWeaponInHands();

            if (hadActions)
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

            nav.SetTarget(null);

            var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
            if (daily != null && daily.IsActive)
                daily.IsActive = false;

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

            Owner.DisableScriptedMovement();
            Owner.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);
            Owner.SetMaximumSpeedLimit(-1f, false);

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

            if (_isGuardMode && Leader != null && Leader.IsActive())
            {
                EnqueueAction(new FollowAgentAction(Leader, run: true));
            }
            else
            {
                ResumeVanillaAI();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 警戒值认知更新（Phase 1-2）
        // ═══════════════════════════════════════════════════════════════

        void UpdateAlertCognition(float dt)
        {
            // 全局质问锁：只要有人在质问玩家，不管是不是我自己，都不更新警戒值（因为玩家本身就相当于在暂停对话状态）
            if (ConfrontingBrain != null)
                return;
            // Npc看不到玩家
            if (!NpcSightSystem.CanNpcSeePlayer(Owner))
            {
                DecayAlertBreakdown(dt);
            }
            else
            {
                //玩家下蹲状态
                if (Agent.Main.CrouchMode)
                    AddAlert(PlayerActionType.Crouching, dt * 0.15f);
                //玩家拔刀状态
                if (IsPlayerWeaponDrawn())
                    AddAlert(PlayerActionType.WeaponDrawn, dt * 0.20f);
                //玩家开启偷窃UI
                if (StealManager.IsUIOpen)
                    AddAlert(PlayerActionType.StealUIOpen, dt * 0.30f);
            }
            // 阶段穿越检测（向上或向下）
            CheckPhaseTransition();
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
            if (newPhase == _lastAlertPhase) return;

            if (newPhase > _lastAlertPhase)
            {
                // 向上穿越：每个目标阶段一个独立事件
                string eventType = newPhase switch
                {
                    AlarmPhase.Suspicious => "BecomeSuspicious",
                    AlarmPhase.Cautious   => "BecomeCautious",
                    AlarmPhase.Alarmed    => "BecomeAlarmed",
                    _ => null
                };
                DebugLogger.Log($"[Brain-Phase] {Owner.Name}(Idx={Owner.Index}) 警戒上升: {_lastAlertPhase} → {newPhase} (警戒值={AlertValue:F2}) → 发送 '{eventType}'");
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

        public void AddAlert(PlayerActionType type, float amount)
        {
            if (!_alertBreakdown.TryGetValue(type, out var entry))
                entry = new AlertEntry();

            entry.Value += amount;
            _alertBreakdown[type] = entry;  // struct 是值类型，写回
        }

        /// <summary>脉冲上下文：设置 AlertEntry 的 TargetName（不改变 Value，Value 由 AddAlert 加）</summary>
        void SetPulseTarget(PlayerActionType type, string targetName, string itemName)
        {
            if (!_alertBreakdown.TryGetValue(type, out var entry))
                entry = new AlertEntry();
            entry.TargetName = targetName;
            entry.ItemName = itemName;
            _alertBreakdown[type] = entry;
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 BubbleSay（Phase 2）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>通用 BubbleSay 入口。传入已组装好的文本，直接显示冒泡。</summary>
        public void BubbleSay(string text)
        {
            if (!string.IsNullOrEmpty(text))
                AgentHudMissionView.AgentSay(Owner, text);
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
                targetName = entry.TargetName;
                itemName = entry.ItemName;
            }

            // 所有占位符（含 {TARGET}/{ITEM}）统一走 PlaceholderResolver
            return NpcSpeechResolver.Resolve(
                $"AlertBubble_{action}_{phase}",
                speaker: (Owner.Character as CharacterObject)?.HeroObject,
                listener: TaleWorlds.CampaignSystem.Hero.MainHero,
                evt: null,
                targetName: targetName,
                itemName: itemName
            );
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 L3 质问（Phase 3-4）
        // ═══════════════════════════════════════════════════════════════

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

            // 占领全局质问锁
            ConfrontingBrain = this;
            DebugLogger.Log($"[Brain-Lock] {Owner.Name}(Idx={Owner.Index}) 开始质问玩家 | 质问锁已占领");

            // 统一走 DialogueInjector 管道：CrimeDialogueBuilder 构建脚本 → DialogueInjector 注入 → 原版 ConversationManager
            // AlertForceConversationAction 对话期间持有，对话结束后由 ResetCrimeDialogueOnConversationEndPatch 广播 EndInteraction 清理
            EnqueueAction(new FollowAgentAction(player, false, radius: 2f, angleOffset: 0f, stopDistance: 1.5f));
            EnqueueAction(new LookAtAction(player, 0.0f));
            EnqueueAction(new AlertForceConversationAction());
            //还需要一个StayAction占位，防止对话期间 Brain 自动 ResumeVanillaAI
            EnqueueAction(new StayAction(player));
        }
        public void Tick(float dt)
        {
            if(Owner == Agent.Main)
            {
                return;
            }

            // 安全兜底：如果持锁者已不活跃，释放质问锁
            if (ConfrontingBrain == this && !Owner.IsActive())
            {
                ConfrontingBrain = null;
                DebugLogger.Log($"[Brain-Lock] {Owner.Name}(Idx={Owner.Index}) 已不活跃，强制释放质问锁");
                return;
            }

            if (_currentAction == null && _actionQueue.Count == 0)
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

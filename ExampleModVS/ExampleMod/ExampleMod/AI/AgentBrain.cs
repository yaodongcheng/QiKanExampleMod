using LivingWorldNpcs.Story;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
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
            // 在这里编写业务逻辑，根据 Agent 的身份（守卫、平民）对事件做出反应            

            // 示例：处理 "WitnessCrime" (目击犯罪) 事件
            InformationManager.DisplayMessage(new InformationMessage($"Agent {Owner.Name} 收到事件: {aiEvent.EventType}", Colors.Yellow));

            // 示例：处理 "ComeHere" 事件
            if (aiEvent.EventType == "ComeHere")
            {
                Agent targetAgent = (Agent)aiEvent.Args[0];
                BubbleSayMissionView.AgentBubbleSay(Owner, $"{targetAgent.Name},你在叫我吗？");
                InteractedAgent = targetAgent;
                ClearAllActions();
                EnqueueAction(new LookAtAction(targetAgent, 0.5f)); // 到达后看向玩家

                EnqueueAction(new FollowAgentAction(targetAgent, false, radius: 2.0f, angleOffset: 0f, stopDistance: 0.5f));

                EnqueueAction(new LookAtAction(targetAgent, 0.5f)); // 到达后看向玩家
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
                    AgentControlHelper.ResumeVanillaAI(Owner);
                    InteractedAgent = null;
                }
            }
            if (aiEvent.EventType == "WitnessCrime_GatherOnLook")
            {
                try
                {
                    //这里的类型转换如果有问题，会导致异常
                    Agent thief = (Agent)aiEvent.Args[0];
                    Agent victim = (Agent)aiEvent.Args[1];
                    Vec3 assignedPos = (Vec3)aiEvent.Args[2];
                    Vec2 turnDir = (Vec2)aiEvent.Args[3];
                    float delay = GroupStageManager.CalculateReactionDelay(Owner, thief, victim);

                    ClearAllActions();
                    InteractedAgent = thief;
                    if (Owner == victim)
                    {
                        var conflictData = new PendingConflict(
                eventId: $"Theft_{TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours}",
                topicName: "当众行窃",
                goalDesc: $"要求 {thief.Name} 立刻归还财物并赔偿精神损失",
                severity: 70.0f,
                type: NegotiationGoalType.ResolveConflict_Apology 
                    );


                        EnqueueAction(new PrepareOpeningAction(InitiativeType.CrimeAccusation, conflictData));
                    }
                        EnqueueAction(new ReactionDecisionAction(delay, (agent) =>
                    {
                        EnqueueAction(new LookAtAction(thief, 0.5f));
                        EnqueueAction(new MoveToPositionAction(assignedPos, turnDir));
                        if (Owner == victim)
                            EnqueueAction(new ForceTalkAction());
                        // 5. 待机
                        EnqueueAction(new StayAction(thief));
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
        
               

            
        }

        // 辅助判断逻辑
       

        // --- 动作执行系统 ---
        public void EnqueueAction(IAtomicAction action)
        {
            // 从空脑到有 Action 的转换：一次性接管原版 AI（SuspendVanillaAI 内部幂等）
            if (_currentAction == null && _actionQueue.Count == 0)
            {
                AgentControlHelper.SuspendVanillaAI(Owner);
            }
            _actionQueue.Enqueue(action);
        }

        public void ClearAllActions()
        {
            bool hadActions = _currentAction != null || _actionQueue.Count > 0;

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
        /// <summary>
        /// 脑空时的默认行为。只有两种情况：
        /// ① 护卫模式 + 有老大 → FollowAgentAction（永久跟随，脑持续有 Action）
        /// ② 其他 → 恢复原版 DailyBehaviorGroup，让 NPC 自由巡逻/闲逛/换区
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
                // 非护卫 / 老大没了 → 交还给原版 AI
                // ResumeVanillaAI 内部有 IsActive 守卫，重复调用是空操作
                AgentControlHelper.ResumeVanillaAI(Owner);
            }
        }
        public void Tick(float dt)
        {
            if(Owner == Agent.Main)
            {
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
                _currentAction.OnStart(Owner);
            }


            // 执行当前动作
            if (_currentAction != null)
            {
                _currentAction.OnTick(Owner, dt);

                if (_currentAction.IsFinished(Owner))
                {
                    _currentAction.OnEnd(Owner);
                    _currentAction = null; // 下一帧会取新的
                }
            }
        }




    }
}

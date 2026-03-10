using ExampleMod.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod
{
    public class AttackTriggerMissionLogic : MissionLogic
    {
        // 1. 添加静态实例，方便 UI 随时访问 (这是实现“简单”的关键)
        private HashSet<Agent> _deadAgents;
        public static AttackTriggerMissionLogic Instance { get; private set; }

        private Agent _agentA;
        private Agent _agentB;
        private bool _isDuelActive;

        // 用于存储切磋时的虚拟血量
        private float _agentA_VirtualHP = 100;
        private float _agentB_VirtualHP = 100;
        

        public IEnumerable<Agent> GetDeadAgentsRaw()
        {
            return _deadAgents;
        }


        public float? GetVirtualHealth(Agent agent)
        {
            if (!_isDuelActive || agent == null) return null;

            if (agent == _agentA) return _agentA_VirtualHP;
            if (agent == _agentB) return _agentB_VirtualHP;

            return null; // 不是切磋双方
        }
  
        public AttackTriggerMissionLogic(Agent a=null, Agent b = null)
        {
            Instance = this;
            _deadAgents = new HashSet<Agent>();
            if (a != null && b != null)
            {
                InitDuel(a, b);
            }
        }
        public void InitDuel(Agent a, Agent b)
        {
            if (_isDuelActive)
            {
                EndDuel(null); // 强制结束，无人胜出
            }

            _agentA = a;
            _agentB = b;
            _isDuelActive = true;
            // 初始化虚拟血量为当前的真实血量
            _agentA_VirtualHP = a.Health;
            _agentB_VirtualHP = b.Health;
        }
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectedAgentAffectsCalc, AgentState affectedAgentState, KillingBlow blow)
        {
            // 当Agent彻底从场景数据中移除时
            InformationManager.DisplayMessage(new InformationMessage($"Agent {affectedAgent.Name} 被移除，状态: {affectedAgentState}", Colors.Red));
           

        }

        [CommandLineFunctionality.CommandLineArgumentFunction("print_death", "custom")]
        public static string ExecutePrintDeath(List<string> args)
        {
            if (Mission.Current == null) return "Please Enter the mission First.";
            StringBuilder sb = new StringBuilder();
            var deaths = Instance.GetDeadAgentsRaw();
            sb.AppendLine($"death count {deaths.Count()}");
            foreach (var agent in deaths)
            {
                sb.AppendLine($"Agent Name: {agent.Name}, Health: {agent.Health}, Position: {agent.Position}");
            }

            return sb.ToString();
        }

        public override void OnAgentHit(Agent affectedAgent, Agent attackerAgent, in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {

            if (affectedAgent != null && attackerAgent != null && affectedAgent != attackerAgent)
            {
                // 如果你有 AgentAIController 单例，调用它广播事件
                // 参数：[0]受害者, [1]攻击者
                AgentAIController.Instance?.BroadcastEventInRange(affectedAgent.Position,100,"event_agent_damaged", attackerAgent, affectedAgent); // 范围广播
            }
            if (affectedAgent.Health<=0)
            {
                if(affectedAgent.IsHuman)
                {
                    lock (_deadAgents) // 简单的线程安全（虽然通常在主线程跑，但保险起见）
                    {
                        _deadAgents.Add(affectedAgent);
                        InformationManager.DisplayMessage(new InformationMessage($"Agent {affectedAgent.Name} 被加入尸体列表", Colors.Red));
                    }
                }
            }


            // 如果切磋已经结束，或者受伤的不是切磋双方，直接忽略
            if (!_isDuelActive || affectedAgent == null) return;
            if (affectedAgent != _agentA && affectedAgent != _agentB) return;

            // 获取本次攻击造成的伤害值
            float damage = blow.InflictedDamage;
            // 如果伤害是0（比如被格挡了），就不处理逻辑了
            if (damage <= 0) return;


            // 3. 处理受害者逻辑
            if (affectedAgent == _agentA)
            {
                // 扣除“虚拟血量”用于判定胜负
                _agentA_VirtualHP -= damage;

                InformationManager.DisplayMessage(new InformationMessage($"{attackerAgent.Name} 击中 {affectedAgent.Name}，伤害: {damage:F1}, 剩余虚拟血量: {_agentA_VirtualHP:F1}", Colors.Yellow));
            }
            else if (affectedAgent == _agentB)
            {
                _agentB_VirtualHP -= damage;
                InformationManager.DisplayMessage(new InformationMessage($"{attackerAgent.Name} 击中 {affectedAgent.Name}，伤害: {damage:F1}, 剩余虚拟血量: {_agentB_VirtualHP:F1}", Colors.Yellow));
            }

            // ==========================================
            // 4. 【关键步骤】伪无敌：把扣掉的血加回去
            // ==========================================
            // 防止 Agent 因为这一击真的死掉（如果当前血量足以承受这一击）
            if (affectedAgent.Health > 0)
            {
                // 计算回血后的值，不能超过血量上限
                float newHealth = Math.Min(affectedAgent.Health + damage, affectedAgent.HealthLimit);
                affectedAgent.Health = newHealth;
            }


            // 检查是否有人的虚拟血量归零
            if (_agentA_VirtualHP <= 0 || _agentB_VirtualHP <= 0)
            {
                EndDuel(loser: (_agentA_VirtualHP <= 0) ? _agentA : _agentB);
            }
        }
        private void EndDuel(Agent loser)
        {
            if (!_isDuelActive) return;
            _isDuelActive = false;

            // 处理胜负逻辑（如果有 loser）
            if (loser != null && _agentA != null && _agentB != null)
            {
                Agent winner = (loser == _agentA) ? _agentB : _agentA;
                InformationManager.DisplayMessage(new InformationMessage($"切磋结束，胜者: {winner.Name}", Colors.Green));
            }

            // 3. 恢复 AI 状态
            if (_agentA != null && _agentA.IsActive())
            {
                _agentA.SetTargetAgent(null);
                StopAgentCombat(_agentA);
                _agentA.SetMortalityState(Agent.MortalityState.Mortal);
            }

            if (_agentB != null && _agentB.IsActive())
            {
                _agentB.SetTargetAgent(null);
                StopAgentCombat(_agentB);
                _agentB.SetMortalityState(Agent.MortalityState.Mortal);
            }

            // 4. 【关键】清空引用，允许下一次攻击触发新的 Agent
            _agentA = null;
            _agentB = null;

        }

        private void StopAgentCombat(Agent agent)
        {
            if (agent == null) return;

            // 清除战斗 AI 标志
            agent.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.None);

            // 让他停下移动
            agent.SetMovementDirection(Vec2.Zero);
            agent.SetAttackState(0); // 停止攻击状态

            // 将队伍设为中立或移除队伍 (视具体情况而定，这里简单设为不攻击)
            // 最暴力的停止方法是暂时设为无 AI，然后再恢复
            // agent.Controller = Agent.ControllerType.None; 
        }




        // 当产生打击判定时触发（哪怕伤害为0）
        public override void OnRegisterBlow(Agent attacker, Agent victim, GameEntity realHitEntity, Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            base.OnRegisterBlow(attacker, victim, realHitEntity, b, ref collisionData, in attackerWeapon);

            // 基础校验
            if (attacker == null || victim == null) return;
            if (!attacker.IsMainAgent || !victim.IsHuman || victim.IsMainAgent) return;

            InformationManager.DisplayMessage(new InformationMessage($"OnRegisterBlow: {attacker.Name} 对 {victim.Name} 造成了伤害", Colors.Yellow));
            AgentAIController.Instance.SendEventToAgent(victim, "event_agent_damaged", attacker, victim);
            // 【场景 1】当前正在切磋中
            if (_isDuelActive)
            {
                // 如果被打的是当前的对手，或者玩家自己被打了 -> 属于正常战斗流程，不触发新逻辑
                if (victim == _agentB || victim == _agentA)
                {
                    return;
                }
                return;
            }
            if (attacker.Team != null && victim.Team != null && attacker.Team.IsEnemyOf(victim.Team))
            {
                // 已经是敌人了，这是一次正常的攻击，直接返回，不触发新战斗逻辑
                return;
            }



            

        
        }
    }
}

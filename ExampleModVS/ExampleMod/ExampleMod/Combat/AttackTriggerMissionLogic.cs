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

namespace LivingWorldNpcs
{
    /// <summary>
    /// 本 mod 的战斗监控总线（Mission 层唯一的伤害/死亡仲裁者）。
    /// 目前承载四件事，全部围绕 OnAgentHit / OnRegisterBlow / OnAgentRemoved 三个引擎钩子：
    /// ① 尸体登记（可搜刮列表）；
    /// ② 玩家命中记录（战场血条过滤）；
    /// ③ 切磋（Duel）虚拟血量 —— 打不死，虚拟血见底判胜负
    ///   （手法：引擎 HandleBlow 内 OnAgentHit 早于 `if (Health &lt; 1f) Die()`，在这里把血写回就能吃掉致命一击）；
    /// ④ 玩家在定居点里被打倒 → 结束 Mission 并把菜单落到定居点菜单，
    ///   由 <see cref="PlayerDetentionBehavior"/> 在那里给出"赔钱 / 认罚"选项。
    ///
    /// ④ 不碰玩家的生死判定：不设无敌、不改血量、不做虚拟血。玩家倒地本来就是引擎
    /// （SandboxAgentDecideKilledOrUnconsciousModel）的既有结果，我们只在倒地**之后**接手。
    /// </summary>
    public class AttackTriggerMissionLogic : MissionLogic
    {
        // 1. 添加静态实例，方便 UI 随时访问 (这是实现”简单”的关键)
        private HashSet<Agent> _deadAgents;
        public static AttackTriggerMissionLogic Instance { get; private set; }

        /// <summary>战场中玩家攻击过的 Agent Index 集合（用于血条过滤）</summary>
        private HashSet<int> _playerAttackedAgents = new HashSet<int>();

        private Agent _agentA;
        private Agent _agentB;
        private bool _isDuelActive;

        // 用于存储切磋时的虚拟血量
        private float _agentA_VirtualHP = 100;
        private float _agentB_VirtualHP = 100;

        /// <summary>战斗广播冷却字典：同一对 (attacker.Index, victim.Index) 3秒内最多广播一次</summary>
        private static Dictionary<(int, int), float> _lastEventDamagedBroadcast = new Dictionary<(int, int), float>();
        private const float EVENT_DAMAGED_BROADCAST_COOLDOWN = 3.0f;

        // ══════════════════ 玩家在定居点被打倒 ══════════════════
        /// <summary>已经处理过本次倒地（每个 Mission 只处理一次）</summary>
        private bool _playerDown = false;
        /// <summary>兜底 EndMission 的时间点（Mission.CurrentTime），-1 = 不需要</summary>
        private float _endMissionAt = -1f;

        /// <summary>倒地后到结束 Mission 的停顿（秒）——留时间看清倒地动画和提示。
        /// 必须小于 5s：原版 LeaveMissionLogic 在玩家倒地 5 秒后会自己 EndMission 并把
        /// 下一个菜单改成 settlement_player_unconscious，抢在它之前收场才能保住我们的落点。</summary>
        private const float KNOCKOUT_TO_MENU_DELAY = 2.0f;


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

        /// <summary>查询某 Agent 是否被玩家攻击过（战场血条过滤用）</summary>
        public bool IsAgentAttackedByPlayer(Agent agent)
        {
            return agent != null && _playerAttackedAgents.Contains(agent.Index);
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
            base.OnAgentRemoved(affectedAgent, affectedAgentAffectsCalc, affectedAgentState, blow);

            // 死亡的可靠信号：被击杀 / 击晕的人类计入可搜刮尸体列表。
            // 这是主入口（OnAgentHit 里的 Health<=0 只能兜住「最后一击恰好被本逻辑捕获」的情况，
            // 补刀、击晕、流血致死等都会漏）。_deadAgents 是 HashSet，重复 Add 自动去重。
            if (affectedAgent != null && affectedAgent.IsHuman
                && !affectedAgent.IsMainAgent // 玩家自己永远不进可搜刮尸体列表
                && (affectedAgentState == AgentState.Killed || affectedAgentState == AgentState.Unconscious))
            {
                lock (_deadAgents)
                {
                    _deadAgents.Add(affectedAgent);
                }
            }

            // 玩家自己被打昏 → 交给大地图扣押流程（被打死则是原版的战役结束，不接管）
            if (affectedAgent != null && affectedAgent.IsMainAgent
                && affectedAgentState == AgentState.Unconscious)
            {
                OnPlayerKnockedOut();
            }
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

            // 🆕 记录玩家实际命中过的敌方 Agent（战场血条过滤用）
            // 放在 OnAgentHit 而非 OnRegisterBlow：只有真正造成伤害才算，格挡/空挥不计
            if (attackerAgent != null && attackerAgent.IsMainAgent
                && affectedAgent != null && affectedAgent.IsHuman
                && !affectedAgent.IsMainAgent
                && attackerAgent.Team != null && affectedAgent.Team != null
                && attackerAgent.Team.IsValid && affectedAgent.Team.IsValid // Team.Invalid 单例 != null，IsEnemyOf 内部解引用 null mission 必 NRE
                && attackerAgent.Team.IsEnemyOf(affectedAgent.Team))
            {
                _playerAttackedAgents.Add(affectedAgent.Index);
            }

            //作用一，记录死人
            // 玩家自己永远不进可搜刮尸体列表：引擎在 OnAgentHit 之前就写过 Health，
            // 玩家吃到致命一击时这里会看到 Health<=0。
            if (affectedAgent.Health <= 0 && !affectedAgent.IsMainAgent)
            {
                if(affectedAgent.IsHuman)
                {
                    lock (_deadAgents) // 简单的线程安全（虽然通常在主线程跑，但保险起见）
                    {
                        _deadAgents.Add(affectedAgent);
                        if (Settings.Instance.ShowDebugMessages)
                            InformationManager.DisplayMessage(new InformationMessage(
                                LWNTextHelper.ResolveCompound("LWN_combat_body_added",
                                    "Agent {NAME} added to corpse list",
                                    ("NAME", affectedAgent.Name?.ToString() ?? "")),
                                Colors.Red));
                    }
                }
            }

            // 如果切磋已经结束，或者受伤的不是切磋双方，直接忽略
            if (!_isDuelActive || affectedAgent == null) return;
            if (affectedAgent != _agentA && affectedAgent != _agentB) return;

            //作用二：切磋特殊的虚拟血量处理，以下内容，只有在切磋中，并且受伤者是切磋双方时才会执行

            // 获取本次攻击造成的伤害值
            float damage = blow.InflictedDamage;
            // 如果伤害是0（比如被格挡了），就不处理逻辑了
            if (damage <= 0) return;


            // 3. 处理受害者逻辑
            if (affectedAgent == _agentA)
            {
                // 扣除“虚拟血量”用于判定胜负
                _agentA_VirtualHP -= damage;

                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveCompound("LWN_combat_duel_hit",
                            "{ATTACKER} hit {VICTIM}, damage: {DAMAGE}, remaining virtual HP: {HP}",
                            ("ATTACKER", attackerAgent.Name?.ToString() ?? ""),
                            ("VICTIM", affectedAgent.Name?.ToString() ?? ""),
                            ("DAMAGE", damage.ToString("F1")),
                            ("HP", _agentA_VirtualHP.ToString("F1"))),
                        Colors.Yellow));
            }
            else if (affectedAgent == _agentB)
            {
                _agentB_VirtualHP -= damage;
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveCompound("LWN_combat_duel_hit",
                            "{ATTACKER} hit {VICTIM}, damage: {DAMAGE}, remaining virtual HP: {HP}",
                            ("ATTACKER", attackerAgent.Name?.ToString() ?? ""),
                            ("VICTIM", affectedAgent.Name?.ToString() ?? ""),
                            ("DAMAGE", damage.ToString("F1")),
                            ("HP", _agentB_VirtualHP.ToString("F1"))),
                        Colors.Yellow));
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
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveCompound("LWN_combat_duel_end",
                            "Duel over, winner: {NAME}",
                            ("NAME", winner.Name?.ToString() ?? "")),
                        Colors.Green));
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

        // ══════════════════════════════════════════════════════════════════════
        // 玩家在定居点里被打倒
        //
        // 解决什么问题：玩家在定居点里跟村民/守卫动手打输了，原版只会走 SandBox 的
        // LeaveMissionLogic → "settlement_player_unconscious"（好心村民把你扶起来，
        // 什么都没发生），案件永远卡在 Confrontation。
        //
        // 本段只做两件事，不干涉玩家的生死判定（不设无敌、不改血量），也不手动收兵
        // （NPC 的 FightEnemyAction 见目标 !IsActive() 会自行结束并归队）：
        // ① 通知 PlayerDetentionBehavior "本村有一桩待了结的事"；
        // ② 把下一个菜单指到定居点菜单（village/town/castle）并结束 Mission
        //    —— 扣押的选项就注入在那个菜单上，不再另开自定义菜单。
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 玩家倒地。只有"本村存在以玩家为嫌犯的对峙案件"时才接管，
        /// 其余情况（战场、竞技场、随机打晕）一律交回原版。
        /// </summary>
        private void OnPlayerKnockedOut()
        {
            if (_playerDown) return;
            if (Campaign.Current == null) return;

            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return;

            // 本村 + 玩家是嫌犯 + 阶段已到 Confrontation/Active 的犯罪事件
            // FindOnGoing(predicate) 已内置 PendingWorldEvent 兜底
            WorldEvent evt = null;
            try
            {
                evt = WorldEventStore.FindOnGoing(settlement.StringId, e =>
                    e.SuspectIsPlayer
                    && (e.Stage == EventStage.Confrontation || e.Stage == EventStage.Active));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] FindHostileEventForPlayer error: {ex.Message}");
            }

            if (evt == null)
            {
                DebugLogger.Log("[Detention] Player down but no hostile case here → vanilla flow");
                return;
            }

            _playerDown = true;
            DebugLogger.Log($"[Detention] Player knocked out at {settlement.Name}, case={evt.EventId}");

            // 不需要在这里手动收兵：FightEnemyAction.OnTick 检测到目标 !IsActive() 就自己 _isFinished，
            // 其 OnEnd 会走 CombatManager.EndFight（归还原队伍 + WatchState=Patrolling）+ 收刀 + 清警戒，
            // 比手动 AbortCurrentAction 做得更全；归队后玩家队伍不再敌对，引擎自动掉目标。

            // 玩家在定居点被打倒的系统提示
            InformationManager.DisplayMessage(new InformationMessage(
                // 你被按在地上，武器被夺走了……
                LWNTextHelper.ResolveText("LWN_combat_knocked_down", "You are pinned to the ground, your weapon taken away..."),
                Colors.Red));

            // ① 交棒给大地图层
            try { PlayerDetentionBehavior.RequestDetention(settlement, evt); }
            catch (Exception ex) { DebugLogger.Log($"[Detention] RequestDetention failed: {ex.Message}"); }

            // ② 菜单落点定死在定居点菜单（否则原版会跳 settlement_player_unconscious
            //    那段"好心村民把你扶起来，什么都没发生"的文本，与刚发生的事自相矛盾）
            try
            {
                Campaign.Current.GameMenuManager?.SetNextMenu(
                    PlayerDetentionBehavior.SettlementMenuIdOf(settlement));
            }
            catch (Exception ex) { DebugLogger.Log($"[Detention] SetNextMenu failed: {ex.Message}"); }

            _endMissionAt = Mission.CurrentTime + KNOCKOUT_TO_MENU_DELAY;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // 抢在原版 LeaveMissionLogic之前离场
            if (!_playerDown || _endMissionAt < 0f) return;
            if (Mission.CurrentTime < _endMissionAt) return;
            _endMissionAt = -1f;

            try
            {
                if (Mission.Current != null && Mission.Current.CurrentState == Mission.State.Continuing)
                {
                    DebugLogger.Log("[Detention] EndMission → settlement menu");
                    Mission.Current.EndMission();
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[Detention] EndMission failed: {ex.Message}"); }
        }

        // 当产生打击判定时触发（哪怕伤害为0）
#if !MB2_V1212
        public override void OnRegisterBlow(Agent attacker, Agent victim, WeakGameEntity realHitEntity, Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
#else
        public override void OnRegisterBlow(Agent attacker, Agent victim, GameEntity realHitEntity, Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
#endif
        {
            base.OnRegisterBlow(attacker, victim, realHitEntity, b, ref collisionData, in attackerWeapon);

            // 基础校验
            if (attacker == null || victim == null) return;

            // 只要 attacker 或 victim 任意一方是玩家就打印
            if ((attacker.IsMainAgent || victim.IsMainAgent) && victim != attacker)
            {
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveCompound("LWN_combat_damage_log",
                            "AttackTriggerMissionLogic - OnRegisterBlow: {ATTACKER} dealt {DAMAGE} damage to {VICTIM}",
                            ("ATTACKER", attacker.Name?.ToString() ?? ""),
                            ("VICTIM", victim.Name?.ToString() ?? ""),
                            ("DAMAGE", b.InflictedDamage.ToString())),
                        Colors.Yellow));
            }

            if (!attacker.IsMainAgent || !victim.IsHuman || victim.IsMainAgent) return;


            if (victim != null && attacker != null && victim != attacker)
            {
                AgentAIController.Instance.SendEventToAgent(victim, "event_agent_damaged", attacker, victim);

                // 范围广播：周围 25m 内 NPC 收到 event_agent_damaged，同一对 3 秒内最多一次
                
                var key = (attacker.Index, victim.Index);
                float now = Mission.Current?.CurrentTime ?? 0f;
                if (!_lastEventDamagedBroadcast.TryGetValue(key, out float last) || now - last >= EVENT_DAMAGED_BROADCAST_COOLDOWN)
                {
                    _lastEventDamagedBroadcast[key] = now;
                    //暂时关闭广播
                    AgentAIController.Instance?.BroadcastEventInRange(victim.Position, 25f, "event_agent_damaged", true, attacker, victim);
                }

            }

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
            if (attacker.Team != null && victim.Team != null
                && attacker.Team.IsValid && victim.Team.IsValid // Team.Invalid 单例 != null，IsEnemyOf 内部解引用 null mission 必 NRE
                && attacker.Team.IsEnemyOf(victim.Team))
            {
                // 已经是敌人了，这是一次正常的攻击，直接返回，不触发新战斗逻辑
                return;
            }
        
        }
    }
}

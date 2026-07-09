using SandBox.Conversation.MissionLogics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public static class CombatManager
    {
        // --- 核心缓存：用于存储不同阵营ID对应的队伍 ---
        // Key: 阵营ID (如: 1=强盗, 2=守卫), Value: 对应的Team对象
        private static Dictionary<int, Team> _factionTeams = new Dictionary<int, Team>();

        /// <summary>正在与玩家交战的 Agent 集合。用于判断玩家是否在战斗中。</summary>
        private static HashSet<Agent> _agentsFightingPlayer = new HashSet<Agent>();

        /// <summary>Agent 进入战斗前的原始队伍。StartFight 移队前记录，EndFight 恢复。</summary>
        private static Dictionary<int, Team> _originalTeams = new Dictionary<int, Team>();

        /// <summary>
        /// 玩家是否正在战斗中。
        /// 每次读取自动清理已失效的 Agent（死亡/消失/场景卸载）。
        /// </summary>
        public static bool IsPlayerInCombat
        {
            get
            {
                RemoveDeadAndStaleAgents();
                return _agentsFightingPlayer.Count > 0;
            }
        }

        /// <summary>指定 Agent 是否正在与玩家交战。</summary>
        public static bool IsAgentFightingPlayer(Agent agent)
        {
            if (agent == null) return false;
            RemoveDeadAndStaleAgents();
            return _agentsFightingPlayer.Contains(agent);
        }

        /// <summary>清理已死亡/消失/场景卸载后的 Agent。安全处理 native 对象已销毁的情况。</summary>
        private static void RemoveDeadAndStaleAgents()
        {
            var dead = new List<Agent>();
            foreach (var a in _agentsFightingPlayer)
            {
                try
                {
                    if (a == null || !a.IsActive() || a.Health <= 0f)
                        dead.Add(a);
                }
                catch (NullReferenceException)
                {
                    // native 对象已被销毁（场景卸载），托管包装还在
                    dead.Add(a);
                }
            }
            foreach (var a in dead)
                _agentsFightingPlayer.Remove(a);
        }

        /// <summary>Mission 结束时清理所有缓存（Team 缓存 + 战斗 Agent 集合 + 原始队伍记录）。</summary>
        public static void OnMissionEnd()
        {
            _agentsFightingPlayer.Clear();
            _factionTeams.Clear();
            _originalTeams.Clear();
        }

        /// <summary>
        /// 注册一个正在与玩家交战的 Agent。
        /// FightEnemyAction.OnStart → CombatManager.StartFight → 涉及玩家时自动注册。
        /// </summary>
        public static void RegisterCombatant(Agent agent)
        {
            if (agent != null && agent.IsActive() && agent != Agent.Main)
            {
                _agentsFightingPlayer.Add(agent);
                DebugLogger.Log($"[CombatManager] RegisterCombatant: {agent.Name}(Idx={agent.Index}), total={_agentsFightingPlayer.Count}");
            }
        }

        /// <summary>
        /// 注销一个与玩家交战的 Agent。FightEnemyAction.OnEnd 中显式调用。
        /// 即时清理，不等 IsPlayerInCombat 的被动 RemoveWhere。
        /// </summary>
        public static void UnregisterCombatant(Agent agent)
        {
            if (agent != null && _agentsFightingPlayer.Remove(agent))
            {
                DebugLogger.Log($"[CombatManager] UnregisterCombatant: {agent.Name}(Idx={agent.Index}), total={_agentsFightingPlayer.Count}");
            }
        }

        /// <summary>
        /// 结束一场战斗：注销战斗者 + 把 NPC 移回进入战斗前的原始队伍。
        ///
        /// 必须成对调用 StartFight → EndFight，否则 NPC 留在敌对 Team 上，
        /// ResumeVanillaAI 后原版 AI 会继续攻击玩家。
        /// </summary>
        public static void EndFight(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return;

            // 1. 注销战斗者
            UnregisterCombatant(agent);

            // 2. 恢复到进入战斗前的原始队伍
            if (_originalTeams.TryGetValue(agent.Index, out var originalTeam)
                && originalTeam != null
                && agent.Team != originalTeam)
            {
                agent.SetTeam(originalTeam, true);
                DebugLogger.Log($"[CombatManager] EndFight: {agent.Name}(Idx={agent.Index}) restored to original team");
            }
            else
            {
                DebugLogger.Log($"[CombatManager] EndFight: {agent.Name}(Idx={agent.Index}) (no original team recorded or already on it)");
            }

            _originalTeams.Remove(agent.Index);
        }

        /// <summary>
        /// 让 agentB 加入战斗。
        /// </summary>
        /// <param name="agentA">当前的对手/目标（通常是玩家，用于确立初始敌对关系）</param>
        /// <param name="agentB">要加入战斗的人</param>
        /// <param name="factionId">
        /// 阵营ID：
        /// -1 : 独立单位（谁都打，像疯狗一样）
        /// 0  : 尝试加入玩家队伍（随从）
        /// 1, 2, 3... : 自定义阵营（如1=强盗, 2=守卫）。相同ID的Agent会自动成为队友。
        /// </param>
        public static void StartFight(Agent agentA, Agent agentB, int factionIdA = -1, int factionIdB = -1, bool Peace = false)
        {

            if (agentA == null || agentB == null || !agentA.IsActive() || !agentB.IsActive())
                return;

            // 玩家参与的战斗 → 注册战斗者
            if (agentA == Agent.Main)
                RegisterCombatant(agentB);
            else if (agentB == Agent.Main)
                RegisterCombatant(agentA);

            Mission mission = Mission.Current;

            var oldArbiter = AttackTriggerMissionLogic.Instance;
            if (oldArbiter != null && Peace)
            {
                oldArbiter.InitDuel(agentA, agentB);
            }

            // 1. 缓存清理：如果场景更换，旧的Team引用失效，必须清空
            CheckAndCleanCache(mission);

            // 2. 队伍分配：分别为 A 和 B 获取或创建队伍
            Team teamA = GetOrCreateTeam(mission, factionIdA, agentA);
            Team teamB = GetOrCreateTeam(mission, factionIdB, agentB);

            // 2.5 移队前记录原始队伍（EndFight 恢复用）
            if (agentA.Team != null) _originalTeams[agentA.Index] = agentA.Team;
            if (agentB.Team != null) _originalTeams[agentB.Index] = agentB.Team;

            // 3. 将 Agent 移入队伍 (如果他们不在该队伍中)
            if (agentA.Team != teamA) agentA.SetTeam(teamA, true);
            if (agentB.Team != teamB) agentB.SetTeam(teamB, true);

            // 4. 关系设定：确保不同阵营互为敌人 (包括对玩家)
            SetupEnemyRelations(teamA, teamB);

            // 5. AI 激活与状态重置 (你提供的逻辑 + 之前补充的逻辑)
            // 必须对 A 和 B 都执行，防止 A 还在看风景
            InitializeAgentCombatState(agentA, agentB); // 封装后的调用
            InitializeAgentCombatState(agentB, agentA);
        }


        private static void InitializeAgentCombatState(Agent actor, Agent enemy)
        {
            // 1. 激活 AI、举盾、警觉 (你提供的函数)
            ActivateFightMode(actor);

            // 2. 空手 agent 动态发一把近战武器（适配任意 mod 组合的物品池）
            if (!AgentHasAnyWeapon(actor))
            {
                AgentControlHelper.TryGiveAnyMeleeWeapon(actor);
            }

            // 3. 清除之前的脚本标志 (防止因剧情/脚本导致的呆立)
            actor.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.None);

            // 4. 强制仇恨锁定
            // 注意：只在敌对时锁定，否则可能导致友军互砍逻辑混乱
            if (actor.Team.IsEnemyOf(enemy.Team))
            {
                actor.SetTargetAgent(enemy);
            }
        }

        // --- 你提供的辅助函数 ---
        private static void ActivateFightMode(Agent agent)
        {
            if (agent != Agent.Main)
            {
                V.SetAgentAI(agent);
                agent.SetWatchState(Agent.WatchState.Alarmed);
            }
            // 只对有盾牌的 agent 强制举盾，避免无盾 agent 动画冲突导致卡死
            if (AgentHasShield(agent))
            {
                agent.EnforceShieldUsage(Agent.UsageDirection.DefendDown);
            }
        }

        private static bool AgentHasShield(Agent agent)
        {
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                MissionWeapon weapon = agent.Equipment[i];
                if (!weapon.IsEmpty && weapon.IsShield())
                    return true;
            }
            return false;
        }

        private static bool AgentHasAnyWeapon(Agent agent)
        {
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                if (!agent.Equipment[i].IsEmpty)
                    return true;
            }
            return false;
        }

        // --- 内部辅助逻辑 ---

        private static void CheckAndCleanCache(Mission mission)
        {
            // 如果字典里有东西，且第一个东西属于旧的Mission，说明切场景了，清空缓存
            if (_factionTeams.Count > 0 && (_factionTeams.First().Value == null || _factionTeams.First().Value.Mission != mission))
            {
                _factionTeams.Clear();
            }
        }

        private static Team GetOrCreateTeam(Mission mission, int factionId, Agent agent)
        {
            // 情况 0: 玩家阵营 (ID=0)，直接返回玩家队伍
            if (factionId == 0)
            {
                if (Mission.Current.MainAgent != null) return Mission.Current.MainAgent.Team;
                if (Mission.Current.PlayerTeam != null) return Mission.Current.PlayerTeam;
            }

            // 情况 1: 独立阵营 (ID=-1)，每次都新建，不存缓存 (像疯狗一样)
            if (factionId == -1)
            {
                return mission.Teams.Add(BattleSideEnum.None, 0xFFFFFF, 0xFFFFFF, null, true, false, true);
            }

            // 情况 2: 自定义阵营 (ID > 0)，查缓存
            if (_factionTeams.ContainsKey(factionId))
            {
                return _factionTeams[factionId];
            }
            else
            {
                // 创建新队伍并缓存
                // 为了区分颜色，可以写个简单的哈希算法生成颜色，这里暂用白色
                uint color = (uint)(0xFF0000 + (factionId * 50)); // 简单变色区分
                Team newTeam = mission.Teams.Add(BattleSideEnum.None, color, color, null, true, false, true);
                _factionTeams[factionId] = newTeam;

                // 新创建的阵营，默认要和玩家敌对 (看你需要，如果 factionId=0 是玩家队友则不需要)
                if (Mission.Current.PlayerTeam != null)
                {
                    newTeam.SetIsEnemyOf(Mission.Current.PlayerTeam, true);
                    Mission.Current.PlayerTeam.SetIsEnemyOf(newTeam, true);
                }

                return newTeam;
            }
        }

        private static void SetupEnemyRelations(Team teamA, Team teamB)
        {
            if (teamA == teamB) return; // 自己人，别开战

            // 互相设为敌人
            teamA.SetIsEnemyOf(teamB, true);
            teamB.SetIsEnemyOf(teamA, true);

            // 进阶：如果你希望新加入的阵营A，自动和缓存里的其他所有阵营（比如强盗、守卫）都敌对
            // 可以遍历 _factionTeams values 进行 SetIsEnemyOf
            foreach (var cachedTeam in _factionTeams.Values)
            {
                if (cachedTeam != teamA)
                {
                    teamA.SetIsEnemyOf(cachedTeam, true);
                    cachedTeam.SetIsEnemyOf(teamA, true);
                }
                if (cachedTeam != teamB)
                {
                    teamB.SetIsEnemyOf(cachedTeam, true);
                    cachedTeam.SetIsEnemyOf(teamB, true);
                }
            }
        }

        /// <summary>玩家向目标 NPC 认输</summary>
        public static void PlayerSurrenderToAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;
            string npcName = target.Name?.ToString() ?? "目标";

            // 广播围观事件：附近 NPC 停止战斗，围过来看
            AgentAIController.Instance?.BroadcastEventInRange(
                Agent.Main.Position, 25f, "WitnessCrime", false, Agent.Main, target);

            var script = new DialogueInjector.DialogueInjectScript
            {
                InjectAtToken = "start",
                EntryTurn = "player_lose",
                Turns = new List<DialogueInjector.DialogueInjectTurn>
                {
                    new DialogueInjector.DialogueInjectTurn
                    {
                        Id = "player_lose",
                        SpeakerIndex = 0,
                        NpcLine = "（喘着粗气，收起武器）哼，知道打不过了吧？把钱袋交出来，饶你一命。",
                        Options = new List<DialogueInjector.DialogueInjectOption>
                        {
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "……（交出钱袋）",
                                NpcResponse = "算你识相。下次长点眼力见，滚吧！",
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "pay"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "求你放过我，我只是路过……",
                                NpcResponseOnSuccess = "……啧，算你运气好。滚，别让我再看见你。",
                                NpcResponseOnFail = "废话少说！求饶？现在翻倍——400 第纳尔，一个子儿不能少！",
                                Action = "INTENT:PlayerSurrenderBeg",
                                ActionParam = "beg",
                                NextTurn = "",
                                NextTurnOnFail = "player_lose_counteroffer"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "你这条狗！杀了我你也别想好过！",
                                NpcResponseOnSuccess = "……疯子。滚，别让我再看见你。",
                                NpcResponseOnFail = "找死！！（暴怒地扑了上来）",
                                Action = "INTENT:PlayerSurrenderThreaten",
                                ActionParam = "threaten"
                            }
                        }
                    },
                    new DialogueInjector.DialogueInjectTurn
                    {
                        Id = "player_lose_counteroffer",
                        SpeakerIndex = 0,
                        NpcLine = "（冷笑）最后一次机会——400 第纳尔，或者咱们接着打。你选。",
                        Options = new List<DialogueInjector.DialogueInjectOption>
                        {
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "……（交出 400 第纳尔）",
                                NpcResponse = "算你识相。滚吧！",
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "counteroffer_beg"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "（拼死一战）",
                                NpcResponse = "好！那就打到你爬不起来！",
                                Action = "NONE",
                                NextTurn = ""
                            }
                        }
                    }
                }
            };

            string label = $"Surrender_Player_{target.Index}";
            DialogueInjector.InjectScriptAsOpening(script, label);

            var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
            conversationLogic?.StartConversation(target, true, false);

            DebugLogger.Log($"[Combat] 玩家向 {npcName} 认输");
        }

        /// <summary>接受目标 NPC 的认输请求</summary>
        public static void AcceptAgentSurrender(Agent target)
        {
            if (target == null || !target.IsActive()) return;
            string npcName = target.Name?.ToString() ?? "目标";

            // 广播围观事件：附近 NPC 停止战斗，围过来看 NPC 认输
            AgentAIController.Instance?.BroadcastEventInRange(
                target.Position, 25f, "WitnessCrime", false, target, Agent.Main);

            var script = new DialogueInjector.DialogueInjectScript
            {
                InjectAtToken = "start",
                EntryTurn = "npc_beg",
                Turns = new List<DialogueInjector.DialogueInjectTurn>
                {
                    new DialogueInjector.DialogueInjectTurn
                    {
                        Id = "npc_beg",
                        SpeakerIndex = 0,
                        NpcLine = "（丢下武器，踉跄后退，举起双手）别、别打了……我认输！",
                        Options = new List<DialogueInjector.DialogueInjectOption>
                        {
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "你走吧。",
                                NpcResponse = "多、多谢！我这就走……",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "accept"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "给我跪下磕头认错！",
                                NpcResponse = $"（{npcName}屈辱地跪倒在地，额头重重磕在地上……）",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "humiliate"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "把钱交出来，饶你一命。",
                                NpcResponse = "好、好……都给你！求你放过我……",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "ransom"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "太迟了。继续打！",
                                NpcResponse = $"不——！（{npcName}绝望地重新抓起武器）",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "refuse"
                            }
                        }
                    }
                }
            };

            string label = $"Surrender_NPC_{target.Index}";
            DialogueInjector.InjectScriptAsOpening(script, label);

            var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
            conversationLogic?.StartConversation(target, true, false);

            DebugLogger.Log($"[Combat] 玩家与投降的 {npcName} 开始对话");
        }
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ExampleMod
{
    public static class CombatManager
    {
        // --- 核心缓存：用于存储不同阵营ID对应的队伍 ---
        // Key: 阵营ID (如: 1=强盗, 2=守卫), Value: 对应的Team对象
        private static Dictionary<int, Team> _factionTeams = new Dictionary<int, Team>();

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

            // 2. 清除之前的脚本标志 (防止因剧情/脚本导致的呆立)
            actor.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.None);

            // 3. 强制仇恨锁定
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
                agent.Controller = Agent.ControllerType.AI;
                agent.SetWatchState(Agent.WatchState.Alarmed);
                // agent.EnforceShieldUsage(Agent.UsageDirection.DefendDown); // 原注释
            }
            // 强制举盾防御，增加存活率
            agent.EnforceShieldUsage(Agent.UsageDirection.DefendDown);
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
    }

}

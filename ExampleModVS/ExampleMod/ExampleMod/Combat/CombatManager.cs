using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public static class CombatManager
    {
        // --- 核心缓存：用于存储不同阵营ID对应的队伍 ---
        // Key: 阵营ID, Value: 对应的Team对象（遗留模型容器；EndFight 后成员回原队，队伍留缓存复用）
        //
        // 🎯 战斗 Team 模型（定居点场景；战斗/竞技场/训练场自动退回遗留模型）：
        //   场上角色分四类：玩家本人 / 玩家友方 / 敌方（受害人+目击+支持者）/ 旁观者。
        //   - 队2 _playerSideTeam：玩家侧容器——玩家+友方（开战 sweep-in 护主）+ 见义勇为帮手
        //   - 队3 _enemySideTeam：敌方容器——受害人/目击证人/支持者（shouldHelp 且 victim 非友方）
        //   - 队4 _opponentSideTeam：切磋对手容器——玩家侧内部互打（玩家 vs 自家护卫）。
        //     切磋时其他友方 sweep-out 回 PlayerTeam 旁观化，防围殴（1v1 隔离）。
        //   - PlayerTeam：旁观者——与队2/3/4 均不敌对，平民照常生活。
        //   - 阵营推导（StartFight 自动）：玩家/友方→队2；非友方且目标是玩家侧→队3；
        //     双方都玩家侧→队4 切磋；纯 NPC 战斗→遗留 faction 容器。
        //   - 生命周期：战斗计数器 + EndFight 归零全员还原（玩家+友方+敌方+切磋对手回各自原队）。
        //     成员死亡由 AttackTriggerMissionLogic 钩子提前收场，防计数泄漏。
        //   ⚠️ 已知代价：被袭者的原版队友视 ta 为敌（队3↔队2 敌对所致）；犯罪/目击/守卫反应
        //     由 Brain 系统驱动，不走团队关系。
        //   🔴 切磋"点到为止"（2026-08-13 已实现）：duel 事件 → FightEnemyAction(IsDuel)
        //     → StartFight(Peace:true) → RegisterDuel 登记双方；双方 Mortal 正常打，
        //     不死 = 判负回血抢占死亡判定 + EndDuel 收场窗口 Invulnerable 兜底。
        //     本模型只提供 1v1 隔离。
        private static Dictionary<int, Team> _factionTeams = new Dictionary<int, Team>();

        /// <summary>正在与玩家交战的 Agent 集合。用于判断玩家是否在战斗中。</summary>
        private static HashSet<Agent> _agentsFightingPlayer = new HashSet<Agent>();

        /// <summary>Agent 进入战斗前的原始队伍。StartFight 移队前记录，EndFight 恢复。</summary>
        private static Dictionary<int, Team> _originalTeams = new Dictionary<int, Team>();

        // --- 侧容器模型字段（定居点场景战斗/切磋用） ---
        /// <summary>队2：玩家侧容器（玩家+友方，开战 sweep-in 护主）。</summary>
        private static Team _playerSideTeam;
        /// <summary>队3：敌方容器（受害人/目击/支持者）。</summary>
        private static Team _enemySideTeam;
        /// <summary>队4：切磋对手容器（玩家侧内部互打，1v1 隔离）。</summary>
        private static Team _opponentSideTeam;
        /// <summary>侧容器模型活跃战斗数（重叠战斗不提前还原；归零全员还原）。</summary>
        private static int _sideFightCount;
        /// <summary>侧模型成员 Agent → 原队（玩家+友方+敌方+切磋对手）。</summary>
        private static Dictionary<Agent, Team> _sideFightMembers = new Dictionary<Agent, Team>();

        // --- 队伍变更日志门禁（AgentSetTeamLoggerPatch 用）：跳过出生初始化刷屏 ---
        /// <summary>Mission 开始后经过该秒数才记录 SetTeam（出生初始化通常在前 1~2 秒完成）。</summary>
        private const float TeamChangeLogDelay = 2.0f;
        /// <summary>当前 Mission 的起始时刻（Mission.CurrentTime 基准）。-1 = 未记录。</summary>
        private static float _missionStartTime = -1f;
        /// <summary>开场基线日志是否已打（每 Mission 一次）。</summary>
        private static bool _baselineLogged;

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

        /// <summary>Mission 结束时清理所有缓存（Team 缓存 + 战斗 Agent 集合 + 原始队伍记录 + 侧容器模型 + 日志门禁）。</summary>
        public static void OnMissionEnd()
        {
            _agentsFightingPlayer.Clear();
            _factionTeams.Clear();
            _originalTeams.Clear();
            _sideFightMembers.Clear();
            _sideFightCount = 0;
            _playerSideTeam = null;
            _enemySideTeam = null;
            _opponentSideTeam = null;
            // 日志门禁随 Mission 重置
            _missionStartTime = -1f;
            _baselineLogged = false;
        }

        // ═══════════════ 队伍变更日志门禁（MissionLogic.OnMissionTick 驱动） ═══════════════

        /// <summary>
        /// 每 tick 驱动门禁：记录 Mission 起始时刻；门禁开启（2 秒后）时打全场初始队伍基线。
        /// 由 AttackTriggerMissionLogic.OnMissionTick 调用。
        /// </summary>
        public static void OnCombatManagerTick(Mission mission)
        {
            if (mission == null) return;

            if (_missionStartTime < 0f)
            {
                _missionStartTime = mission.CurrentTime;
                _baselineLogged = false;
                DebugLogger.Log($"[CombatManager] TeamChange gate armed: mission start t={_missionStartTime:F2}, logging enabled after {TeamChangeLogDelay}s");
            }

            if (!_baselineLogged && mission.CurrentTime - _missionStartTime >= TeamChangeLogDelay)
            {
                _baselineLogged = true;
                LogTeamBaseline(mission);
            }
        }

        /// <summary>队伍变更日志是否放行：Mission 开始 2 秒后（出生初始化已结束）。AgentSetTeamLoggerPatch 用。</summary>
        public static bool ShouldLogTeamChange()
        {
            if (Mission.Current == null) return false;
            if (_missionStartTime < 0f) return false;
            return Mission.Current.CurrentTime - _missionStartTime >= TeamChangeLogDelay;
        }

        /// <summary>开场基线：全场所有 Agent 的初始队伍 Index（每 Mission 一条日志，与 [TeamChange] 流水对照用）。</summary>
        private static void LogTeamBaseline(Mission mission)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (var a in mission.Agents)
            {
                if (a == null) continue;
                try
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"{a.Index}:{a.Name}→{a.Team?.TeamIndex ?? -1}");
                    count++;
                }
                catch
                {
                    // 个别 Agent native 已销毁则跳过
                }
            }
            DebugLogger.Log($"[TeamBaseline] {count} agents: {sb}");
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
        /// 结束一场战斗：注销战斗者 + 还原队伍。
        ///
        /// 侧容器模型：计数减一，归零 → 全员还原（玩家+友方+敌方+切磋对手回各自原队）。
        /// 遗留模型：把该 NPC 移回进入战斗前的原始队伍。
        ///
        /// 必须成对调用 StartFight → EndFight，否则 NPC 留在敌对 Team 上，
        /// ResumeVanillaAI 后原版 AI 会继续攻击玩家。
        /// </summary>
        public static void EndFight(Agent agent)
        {
            if (agent == null) return;

            // 1. 注销战斗者
            UnregisterCombatant(agent);

            // 2. 侧容器模型：计数减一；归零 → 全员统一还原（侧模型成员不含死亡者——已死的不还原）
            if (_sideFightMembers.ContainsKey(agent))
            {
                if (_sideFightCount > 0) _sideFightCount--;
                if (_sideFightCount <= 0) RestoreSideFightMembers();
                return;
            }

            // 3. 遗留模型：恢复单个 agent
            if (!agent.IsActive()) return;
            // 3.5 重置 WatchState 回 Normal，防止 AlarmedBehaviorGroup 永远占着控制权
            //     （StartFight 设为了 Alarmed，不重置则 RefreshBehaviorGroups 永远选 Alarmed，
            //       DailyBehaviorGroup 永远拿不回控制权，NPC 卡死不动）
            agent.SetWatchState(Agent.WatchState.Patrolling);

            // ═══ 🔴【新增 2026-08-13】动作层收手（自 EndDuel 的 StopAgentCombat 收编）═══
            // EndFight 原本只还原状态（队伍/WatchState），不碰动作层。本块新增：
            // 清战斗 AI 标志 + 停移动 + 打断当前攻击动作 + 清索敌目标——"战斗结束 = 彻底收手"。
            // 影响面：EndFight 仅两个调用方——
            //   ① FightEnemyAction.OnEnd：目标死亡 / 玩家收刀停战 / 投降 / 切磋判负 / 命令打断 全路径；
            //   ② EndAllFightsWithPlayer：玩家被制服/投降/被俘的一键收场。
            // 对它们都是"更彻底收手"（断挥刀/停移动/清残留索敌），无方向性破坏；被新命令打断的
            // 场景，新 action 下一帧重新下指令，中间一帧停顿无感知。EndAllFightsWithPlayer 里原有
            // 显式 SetTargetAgent(null) 变冗余（幂等无害）。
            // ⚠️ 若实机发现某收场路径异常，删本块即回滚到旧行为（只还原状态，不碰动作层）。
            // ══════════════════════════════════════════════════════════════════════
            InterruptCombatMotion(agent);
            agent.SetTargetAgent(null);

            // 4. 恢复到进入战斗前的原始队伍
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

        /// <summary>战斗收场动作层打断（2026-08-13 自 AttackTriggerMissionLogic.StopAgentCombat 收编）：
        /// 清战斗 AI 标志 + 停移动 + 打断当前攻击动作。玩家控制 agent 不打断（攻击是输入驱动，SetAttackState
        /// 无效且可能干扰玩家输入）。</summary>
        private static void InterruptCombatMotion(Agent agent)
        {
            if (agent == null || !agent.IsActive() || agent == Agent.Main) return;
            agent.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.None);
            agent.SetMovementDirection(Vec2.Zero);
            agent.SetAttackState(0);
        }

        /// <summary>
        /// 快照当前所有正在与玩家交战的 Agent（返回副本，可安全在遍历中修改战斗集合）。
        /// </summary>
        public static List<Agent> GetAgentsFightingPlayer()
        {
            RemoveDeadAndStaleAgents();
            return _agentsFightingPlayer.ToList();
        }

        /// <summary>
        /// 一键收场：结束所有与玩家的战斗（玩家被制服/投降/被俘时用）。
        /// 逐个走 EndFight（归还原队伍 + 重置 WatchState），返回收队人数。
        /// </summary>
        public static int EndAllFightsWithPlayer()
        {
            var list = GetAgentsFightingPlayer();
            foreach (var a in list)
            {
                try
                {
                    EndFight(a);
                    a.SetTargetAgent(null);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[CombatManager] EndAllFightsWithPlayer error: {ex.Message}");
                }
            }
            _agentsFightingPlayer.Clear();
            DebugLogger.Log($"[CombatManager] EndAllFightsWithPlayer: {list.Count} agents stood down");
            return list.Count;
        }

        /// <summary>
        /// 让 agentB 加入战斗（A vs B）。
        ///
        /// 队伍管理双模型（详见类注释「战斗 Team 模型」）：
        /// - 侧容器模型（定居点场景，任一方是玩家/友方/已在侧容器）：队2 玩家侧 vs 队3 敌方；
        ///   玩家侧内部互打 → 队4 切磋（1v1 隔离）。旁观者留在 PlayerTeam，与侧容器均不敌对。
        /// - 遗留模型（战斗/竞技场/训练场，或纯 NPC 战斗）：玩家/友方锚定不动，
        ///   其余按 factionId 进自定义阵营（同 ID 同队）或独立队（每场新建）。
        /// 阵营推导自动完成，调用方无需关心 factionId（只在遗留模型生效）。
        /// </summary>
        /// <param name="agentA">当前的对手/目标（通常是玩家，用于确立初始敌对关系）</param>
        /// <param name="agentB">要加入战斗的人</param>
        /// <param name="factionIdA">
        /// 阵营ID（侧容器模型下自动推导，仅遗留模型生效）：
        /// -1 : 独立单位（谁都打，像疯狗一样；每场新建队伍，不缓存）
        /// 0  : 尝试加入玩家队伍（随从出战）
        /// 1, 2, 3... : 自定义阵营（相同ID的Agent会自动成为队友，缓存复用）
        /// </param>
        /// <param name="factionIdB">同 factionIdA，给 agentB 用。</param>
        public static void StartFight(Agent agentA, Agent agentB, int factionIdA = -1, int factionIdB = -1, bool Peace = false)
        {

            if (agentA == null || agentB == null || !agentA.IsActive() || !agentB.IsActive())
                return;

            // 🔴 2026-08-14 开战清除蹲姿：蹲姿感知读的是人工记录 CrouchPoseActive（native flag 对
            // Suspend NPC 不自动清，ForceUnlockAgent 也不会发生在被脑接管者身上），蹲着开打会让
            // Crouching 警戒因素在战斗中持续上涨（实机：阿速甘被质问开打后感知侧仍每轮 +警戒）。
            // 统一在 StartFight 收口起身——所有战斗入口（玩家/NPC/切磋/质问）都经过这里。
            if (agentA.CrouchMode) agentA.SetCrouchMode(false);
            if (agentB.CrouchMode) agentB.SetCrouchMode(false);
            AgentBrain.SetCrouchPose(agentA, false);
            AgentBrain.SetCrouchPose(agentB, false);

            DebugLogger.Log($"[CombatManager] StartFight: {agentA.Name}(Idx={agentA.Index}) vs {agentB.Name}(Idx={agentB.Index}), factionA={factionIdA}, factionB={factionIdB}, Peace={Peace}");

            // 玩家参与的战斗 → 注册战斗者
            if (agentA == Agent.Main)
                RegisterCombatant(agentB);
            else if (agentB == Agent.Main)
                RegisterCombatant(agentA);

            Mission mission = Mission.Current;
            if (mission == null) return;

            // 🔴 2026-08-13 切磋不死标记（Peace 参数驱动，跟随战斗发起方）：
            // 双方保持 Mortal 正常受击——血条真实掉落、伤害反馈全保留（实机证明
            // Invulnerable 在 native 层连伤害一起拦了，全程不掉血，不能用来开打）。
            // 不死由仲裁者保证：引擎 HandleBlow 内 OnAgentHit 早于 `if (Health < 1f) Die()`
            //（反编译确认），血归零瞬间判负回满血 → 引擎走不到 Die；判负后 EndDuel 再以
            // Invulnerable 兜底停战生效前的残余攻击（native 拦死）。
            // 判负双方登记给仲裁者（OnAgentHit 判负需要知道"切磋双方是谁"）。
            // 旧 InitDuel 虚拟血量方案已废弃禁止使用。
            if (Peace)
            {
                AttackTriggerMissionLogic.Instance?.RegisterDuel(agentA, agentB);
            }

            // 1. 缓存清理：如果场景更换，旧的Team引用失效，必须清空
            CheckAndCleanCache(mission);

            // 2. 模型选择：定居点场景 + 任一方是玩家侧（玩家/友方/已在侧容器）→ 侧容器模型
            int sideA = SideOf(agentA);
            int sideB = SideOf(agentB);
            // 🔴 2026-08-20 未知侧按对手推导（误伤反击/见义勇为等新参战者 side=0 时）：
            // 进哪边由对手决定——对手在玩家侧 → 自己进敌方；对手在敌方侧 → 自己进玩家侧。
            // 旧逻辑把 0 一律映射敌方容器，只在"新参战者打玩家侧"时碰巧正确；新参战者攻击
            // 已在敌方容器的目标时双方落同一队 → 引擎视作友军，锁了目标也打不出（实机：
            // 帝国具装骑兵 Chivalry 反击帝国熟练步兵，双双落敌方容器干瞪眼不打人）。
            if (sideA == 0 && sideB != 0) sideA = sideB == 1 ? 2 : 1;
            if (sideB == 0 && sideA != 0) sideB = sideA == 1 ? 2 : 1;
            if (!Settings.Instance.IsInteractionDisabled() && (sideA != 0 || sideB != 0))
            {
                StartSideFight(mission, agentA, agentB, sideA, sideB);
                return;
            }

            // 3. 遗留模型：锚定（玩家/友方不动）+ faction 容器（纯 NPC 战斗 / 战斗场景）
            StartLegacyFight(mission, agentA, agentB, factionIdA, factionIdB);
        }

        // ═══════════════ 侧容器模型（定居点场景：玩家 vs 其他人 / 玩家侧内部切磋） ═══════════════

        /// <summary>
        /// 侧容器模型开战。队2 玩家侧 vs 队3 敌方；双方都是玩家侧 → 队4 切磋（1v1）。
        /// 战斗时友方 sweep-in 护主参战；切磋时其他友方 sweep-out 旁观化（防围殴）。
        /// </summary>
        private static void StartSideFight(Mission mission, Agent agentA, Agent agentB, int sideA, int sideB)
        {
            Team playerSide = GetOrCreateSideTeam(mission, ref _playerSideTeam, 0x2A9DF4);    // 队2 蓝
            Team enemySide = GetOrCreateSideTeam(mission, ref _enemySideTeam, 0xE04444);      // 队3 红
            Team opponentSide = GetOrCreateSideTeam(mission, ref _opponentSideTeam, 0xF0A030); // 队4 橙

            // 2.5 无效组合（双方都在敌方侧）→ 拒绝
            if (sideA == 2 && sideB == 2)
            {
                DebugLogger.Log($"[CombatManager] SideFight refused: {agentA.Name} vs {agentB.Name}, sides=({sideA},{sideB})");
                return;
            }

            if (sideA == 1 && sideB == 1)
            {
                // 切磋：玩家侧内部互打——玩家留队2，对手进队4；其他友方旁观化（问题A：防围殴）
                DebugLogger.Log($"[CombatManager] Spar: {agentA.Name} vs {agentB.Name} (player-side duel, allies stand down)");
                Agent sparKeeper = (agentB == Agent.Main) ? agentB : agentA; // 玩家优先留队2
                Agent sparOpponent = (sparKeeper == agentA) ? agentB : agentA;
                RecordAndMove(sparKeeper, playerSide);
                RecordAndMove(sparOpponent, opponentSide);
                // 🔴 2026-08-13：sweep 必须排除切磋双方。旧逻辑只排除玩家本人（假设切磋必有玩家），
                // NPC vs NPC 切磋时 sparKeeper 不是玩家 → 刚进队2 就被扫回 PlayerTeam（日志实锤
                // team 3→1），队2 变空 → 队4 的对手找不到敌对目标，双方拔刀呆站谁也不打谁。
                SweepAlliesOutOfPlayerSide(mission, playerSide, sparKeeper, sparOpponent);
                SetupMutualHostility(playerSide, opponentSide);
            }
            else
            {
                // 战斗：玩家侧 vs 敌方——各自进队2/队3，友方 sweep-in 护主
                DebugLogger.Log($"[CombatManager] SideFight: {agentA.Name}→队{(sideA == 1 ? 2 : 3)} vs {agentB.Name}→队{(sideB == 1 ? 2 : 3)}");
                RecordAndMove(agentA, sideA == 1 ? playerSide : enemySide);
                RecordAndMove(agentB, sideB == 1 ? playerSide : enemySide);
                SweepAlliesIntoPlayerSide(mission, playerSide);
                SetupMutualHostility(playerSide, enemySide);
            }

            // 3. 计数器 + AI 激活（重叠战斗计数 > 1，不提前还原）
            _sideFightCount++;
            InitializeAgentCombatState(agentA, agentB);
            InitializeAgentCombatState(agentB, agentA);
        }

        /// <summary>
        /// 参战者的侧判定：1=玩家侧，2=敌方侧，0=未知（纯 NPC，走遗留模型）。
        /// 玩家/友方恒为玩家侧；已在侧容器上的按当前容器归类（重叠战斗不覆盖队伍）。
        /// </summary>
        private static int SideOf(Agent agent)
        {
            if (agent == Agent.Main || FriendlinessHelper.IsFriendlyToPlayer(agent)) return 1;
            if (agent.Team == null) return 0;
            if (agent.Team == _playerSideTeam) return 1;
            if (agent.Team == _enemySideTeam || agent.Team == _opponentSideTeam) return 2;
            return 0;
        }

        /// <summary>侧容器懒创建（每场景一次；场景更换后重建）。颜色区分：队2=蓝，队3=红，队4=橙。</summary>
        private static Team GetOrCreateSideTeam(Mission mission, ref Team field, uint color)
        {
            if (field == null || field.Mission != mission)
                field = mission.Teams.Add(BattleSideEnum.Attacker, color, color, null, true, false, true);
            return field;
        }

        /// <summary>
        /// 侧模型移队：只记录第一次的原队（重叠战斗不覆盖），EndFight 归零时统一还原。
        /// 玩家侧锚定到队2、敌方进队3/队4 都走这里。
        /// </summary>
        private static void RecordAndMove(Agent agent, Team team)
        {
            if (agent == null || team == null || agent.Team == team)
            {
                DebugLogger.Log($"[CombatManager] RecordAndMove SKIP: {agent?.Name ?? "null"}(Idx={agent?.Index ?? -1}), target=team{team?.TeamIndex ?? -1}, current=team{agent?.Team?.TeamIndex ?? -1}, reason={(agent == null ? "agent null" : team == null ? "team null" : "same team")}");
                return;
            }
            if (agent.Team != null && !_sideFightMembers.ContainsKey(agent))
                _sideFightMembers[agent] = agent.Team;

            // SetTeam 前一行日志：如果这里打了、DONE 没打 → SetTeam 抛异常（会被上层静默吞掉）
            DebugLogger.Log($"[CombatManager] RecordAndMove: {agent.Name}(Idx={agent.Index}) team {agent.Team?.TeamIndex ?? -1} → {team.TeamIndex}");
            try
            {
                agent.SetTeam(team, true);
                DebugLogger.Log($"[CombatManager] RecordAndMove DONE: {agent.Name}(Idx={agent.Index}) now on team {agent.Team?.TeamIndex ?? -1}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[严重错误][CombatManager] RecordAndMove SetTeam FAILED: {agent.Name}(Idx={agent.Index}) → team {team.TeamIndex}: {ex.Message}\n堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>战斗开战时把在场友方移入队2（护主参战）。跳过玩家本人与忙中（对话/互动）的友方。</summary>
        private static void SweepAlliesIntoPlayerSide(Mission mission, Team playerSide)
        {
            foreach (var ally in mission.Agents)
            {
                if (ally == null || !ally.IsActive() || ally == Agent.Main) continue;
                if (ally.IsUsingGameObject) continue; // 对话/互动中的友方不动
                if (!FriendlinessHelper.IsFriendlyToPlayer(ally)) continue;
                RecordAndMove(ally, playerSide);
            }
        }

        /// <summary>切磋开战时把队2上的其他友方移回 PlayerTeam（旁观化）——防友方扑上来围殴对手。
        /// 🔴 必须排除玩家本人：玩家刚被移入队2（切磋对手在对面的队4），sweep 会把玩家也旁观化，
        ///    导致队2 变空、切磋打不起来（2026-08-09 实测踩坑）。
        /// 🔴 2026-08-13 必须排除切磋双方：NPC vs NPC 切磋时 sparKeeper 不是玩家，
        ///    旧逻辑把 sparKeeper 也扫回 PlayerTeam（日志实锤），队2 空 → 对手无目标可打。</summary>
        private static void SweepAlliesOutOfPlayerSide(Mission mission, Team playerSide, Agent sparKeeper = null, Agent sparOpponent = null)
        {
            Team playerTeam = mission.PlayerTeam;
            if (playerTeam == null) return;
            foreach (var ally in mission.Agents)
            {
                if (ally == null || !ally.IsActive() || ally == Agent.Main) continue; // 玩家本人永不旁观化
                if (ally == sparKeeper || ally == sparOpponent) continue;              // 切磋双方永不旁观化
                if (ally.Team != playerSide) continue;
                if (ally.IsUsingGameObject) continue;
                RecordAndMove(ally, playerTeam);
            }
        }

        /// <summary>设置两队伍互敌（幂等，重复调用无害）。</summary>
        private static void SetupMutualHostility(Team teamA, Team teamB)
        {
            if (teamA == null || teamB == null || teamA == teamB) return;
            teamA.SetIsEnemyOf(teamB, true);
            teamB.SetIsEnemyOf(teamA, true);
            DebugLogger.Log($"[CombatManager] SetEnemy: {TeamLabel(teamA)} ↔ {TeamLabel(teamB)}");
        }

        /// <summary>日志用队伍名：侧容器用语义名，其余用引擎 TeamIndex（避免"队2/队3"代号与引擎编号混淆）。</summary>
        private static string TeamLabel(Team team)
        {
            if (team == null) return "null";
            if (team == _playerSideTeam) return $"玩家侧容器(team{team.TeamIndex})"; // lwn-ignore: A 日志用队名
            if (team == _enemySideTeam) return $"敌方容器(team{team.TeamIndex})"; // lwn-ignore: A 日志用队名
            if (team == _opponentSideTeam) return $"切磋对手容器(team{team.TeamIndex})"; // lwn-ignore: A 日志用队名
            return $"team{team.TeamIndex}";
        }

        /// <summary>侧模型战斗全部结束（计数归零）：全员还原原队 + 清警戒 + 动作层打断（玩家不设 WatchState、不打断）。</summary>
        private static void RestoreSideFightMembers()
        {
            foreach (var kv in _sideFightMembers)
            {
                var agent = kv.Key;
                try
                {
                    if (agent == null || !agent.IsActive() || agent.Team == kv.Value) continue;
                    agent.SetTeam(kv.Value, true);
                    if (agent != Agent.Main)
                    {
                        agent.SetWatchState(Agent.WatchState.Patrolling);
                        InterruptCombatMotion(agent);   // 侧模型收场同样做动作层打断（与 EndFight 遗留分支一致）
                        agent.SetTargetAgent(null);
                    }
                }
                catch (NullReferenceException)
                {
                    // native 对象已被销毁（场景卸载），托管包装还在
                }
            }
            _sideFightMembers.Clear();
            _sideFightCount = 0;
        }

        /// <summary>侧模型成员死亡/倒地（Mission 层 OnAgentRemoved 钩子调用）：战斗提前收场，防计数泄漏。</summary>
        public static void NotifySideMemberRemoved(Agent agent)
        {
            if (agent == null) return;
            if (!_sideFightMembers.ContainsKey(agent)) return;
            if (_sideFightCount > 0) _sideFightCount--;
            if (_sideFightCount <= 0) RestoreSideFightMembers();
        }

        /// <summary>遗留模型（战斗场景 / 纯 NPC 战斗）：锚定 + faction 容器。</summary>
        private static void StartLegacyFight(Mission mission, Agent agentA, Agent agentB, int factionIdA, int factionIdB)
        {
            // 1. 阵营解析：玩家/友方锚定原队，其余按 factionId 进自定义/独立阵营
            Team teamA = ResolveFightTeam(mission, agentA, factionIdA, out bool independentA);
            Team teamB = ResolveFightTeam(mission, agentB, factionIdB, out bool independentB);
            DebugLogger.Log($"[CombatManager] StartFight teams: {agentA.Name}→team{teamA?.TeamIndex ?? -1}{(independentA ? "(独立)" : "")} | {agentB.Name}→team{teamB?.TeamIndex ?? -1}{(independentB ? "(独立)" : "")}");

            // 2. 双方落点同队（同阵营内斗 / 锚定落空）→ 拒绝开战
            if (teamA == null || teamB == null || teamA == teamB)
            {
                DebugLogger.Log($"[CombatManager] StartFight refused: {agentA.Name} vs {agentB.Name} ended on the same team");
                return;
            }

            // 3. 移队前记录原始队伍（EndFight 恢复用）
            if (agentA.Team != null) _originalTeams[agentA.Index] = agentA.Team;
            if (agentB.Team != null) _originalTeams[agentB.Index] = agentB.Team;

            // 4. 将 Agent 移入队伍（玩家侧锚定后即为原队，不会真正移动）
            if (agentA.Team != teamA) agentA.SetTeam(teamA, true);
            if (agentB.Team != teamB) agentB.SetTeam(teamB, true);

            // 5. 关系设定：交战双方互敌；独立阵营额外敌视玩家队 + 全部缓存阵营
            SetupEnemyRelations(teamA, teamB, independentA, independentB, mission);

            // 6. AI 激活与状态重置 (你提供的逻辑 + 之前补充的逻辑)
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

            // 🔴 不再 SetTargetAgent 锁死目标（2026-08-09 改）：原版 AI 自带索敌
            // （扫描视野内敌对 Agent，按距离/威胁度排序选目标——见 Knowledge/Agent_AI底层原理.md）。
            // 锁谁他就只打谁 → 被第三方（如友方援护）攻击时无动于衷、只盯着锁定目标砍。
            // 队伍敌对关系由侧容器模型保证，原版索敌自然接管"谁近打谁、谁威胁大打谁"。
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
                // 侧容器与阵营缓存同生命周期：场景更换后旧 Team 引用失效
                _sideFightMembers.Clear();
                _sideFightCount = 0;
                _playerSideTeam = null;
                _enemySideTeam = null;
                _opponentSideTeam = null;
            }
        }

        /// <summary>
        /// 战斗阵营解析（锚点铁律：玩家本人 / 玩家友方永不移动、永不与玩家队敌对）。
        /// 其余 NPC 按 factionId 落阵营：0 = 玩家队（随从出战）、-1 = 独立队（每场新建，
        /// 敌视所有人）、&gt;0 = 自定义阵营（同 ID 同队，缓存复用，按 ID 变色区分）。
        /// 🔴 不再在创建时 SetIsEnemyOf(PlayerTeam)——敌对关系只由 SetupEnemyRelations 按场次设定。
        /// </summary>
        private static Team ResolveFightTeam(Mission mission, Agent agent, int factionId, out bool independent)
        {
            independent = false;

            // 锚点：玩家本人或友方 → 留在当前队伍（兜底玩家队）。
            // 和平场景当前队通常就是 PlayerTeam；锚定后玩家队只与「当前敌方队伍」敌对，
            // 友方/旁观者不会因阵营创建而变成玩家公敌。
            // 旧逻辑的 bug 链：新阵营创建即敌视 PlayerTeam + StartFight 把玩家本人也 SetTeam 进去
            // → 玩家队全体把玩家当敌人 → 随从的原版 AI 拔剑打主人。
            if (agent == Agent.Main || FriendlinessHelper.IsFriendlyToPlayer(agent))
                return agent.Team ?? mission.PlayerTeam ?? mission.MainAgent?.Team;

            // 情况 0: 玩家阵营 (ID=0)，直接返回玩家队伍（随从出战）
            if (factionId == 0)
            {
                if (mission.MainAgent != null) return mission.MainAgent.Team;
                if (mission.PlayerTeam != null) return mission.PlayerTeam;
            }

            // 情况 1: 独立阵营 (ID=-1)，每次都新建，不存缓存 (像疯狗一样)
            if (factionId == -1)
            {
                independent = true;
                return mission.Teams.Add(BattleSideEnum.Attacker, 0xFFFFFF, 0xFFFFFF, null, true, false, true);
            }

            // 情况 2: 自定义阵营 (ID > 0)，查缓存；没有则创建并缓存
            if (!_factionTeams.TryGetValue(factionId, out var team))
            {
                // 为了区分颜色，可以写个简单的哈希算法生成颜色，这里暂用白色→按 ID 变色
                uint color = (uint)(0xFF0000 + (factionId * 50)); // 简单变色区分
                team = mission.Teams.Add(BattleSideEnum.Attacker, color, color, null, true, false, true);
                _factionTeams[factionId] = team;
            }

            return team;
        }

        /// <summary>
        /// 本场战斗的敌对关系：只设交战双方队伍互敌。
        /// 独立阵营（-1）额外敌视玩家队与全部缓存阵营（疯狗语义）。
        /// 🔴 不做「新阵营创建即敌视玩家队」「全缓存阵营互敌」——那会让守卫类阵营永久成为
        /// 玩家公敌，或把无关阵营拖进战斗。阵营间持久关系（如守卫敌视强盗）属于阵营外交，
        /// 需要时另行设计。
        /// </summary>
        private static void SetupEnemyRelations(Team teamA, Team teamB, bool independentA, bool independentB, Mission mission)
        {
            if (teamA == null || teamB == null || teamA == teamB) return; // 自己人，别开战

            // 互相设为敌人
            teamA.SetIsEnemyOf(teamB, true);
            teamB.SetIsEnemyOf(teamA, true);
            DebugLogger.Log($"[CombatManager] SetEnemy(legacy): {TeamLabel(teamA)} ↔ {TeamLabel(teamB)}");

            // 独立阵营：敌视玩家队 + 所有缓存阵营
            if (independentA) MakeHostileToEveryone(teamA, mission);
            if (independentB) MakeHostileToEveryone(teamB, mission);
        }

        /// <summary>独立阵营（-1）专用：敌视玩家队 + 全部缓存阵营（疯狗谁都打）。</summary>
        private static void MakeHostileToEveryone(Team team, Mission mission)
        {
            var playerTeam = mission.PlayerTeam;
            if (playerTeam != null && playerTeam.IsValid && playerTeam != team)
            {
                team.SetIsEnemyOf(playerTeam, true);
                playerTeam.SetIsEnemyOf(team, true);
            }
            foreach (var cachedTeam in _factionTeams.Values)
            {
                if (cachedTeam == team) continue;
                team.SetIsEnemyOf(cachedTeam, true);
                cachedTeam.SetIsEnemyOf(team, true);
            }
            DebugLogger.Log($"[CombatManager] SetEnemy: {TeamLabel(team)} ↔ 玩家队+全部缓存阵营 (独立阵营)");
        }

        /// <summary>玩家向目标 NPC 认输。发事件给 Brain，Brain 全权负责停战/围观/启动对话。</summary>
        public static void PlayerSurrenderToAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;

            // 玩家收起武器
            Agent.Main?.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            Agent.Main?.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);

            DebugLogger.Log($"[Combat] 玩家向 {target.Name} 认输，玩家收起武器");
            AgentAIController.Instance?.SendEventToAgent(
                target, "event_player_surrendered", Agent.Main, target);
        }

        /// <summary>接受目标 NPC 的认输请求。发事件给 Brain，Brain 全权负责停战/围观/启动对话。</summary>
        public static void AcceptAgentSurrender(Agent target)
        {
            if (target == null || !target.IsActive()) return;

            // 玩家收起武器
            Agent.Main?.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            Agent.Main?.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);

            DebugLogger.Log($"[Combat] 玩家与投降的 {target.Name} 开始对话，玩家收起武器");
            AgentAIController.Instance?.SendEventToAgent(
                target, "event_surrender_accepted", Agent.Main, target);
        }
    }

}

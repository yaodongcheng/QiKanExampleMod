using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 本 mod 的战斗监控总线（Mission 层唯一的伤害/死亡仲裁者）。
    /// 目前承载五件事，全部围绕 OnAgentHit / OnRegisterBlow / OnAgentRemoved 三个引擎钩子：
    /// ① 尸体登记（可搜刮列表）；
    /// ② 玩家命中记录（战场血条过滤）；
    /// ③ 🔴【2026-08-13 重写】切磋（Duel）判负 —— 双方保持 Mortal 正常受击（血条真实掉落、
    ///   打击反馈全保留），不死由两层保证：主保证 = 引擎 HandleBlow 内 OnAgentHit 早于
    ///   `if (Health &lt; 1f) Die()`（反编译确认），血归零瞬间判负回满血 → 引擎走不到 Die；
    ///   兜底 = 判负后 EndDuel 立即设 Invulnerable（native 拦 Die）防停战生效前的残余攻击。
    ///   ⚠️ 实机证明 Invulnerable 在 native 层连伤害一起拦（全程不掉血），不能用于开打；
    ///   旧虚拟血量仲裁已彻底废弃：禁止调用 InitDuel/GetVirtualHealth；
    /// ④ 玩家在定居点里被打倒 → 结束 Mission 并把菜单落到定居点菜单，
    ///   由 <see cref="PlayerDetentionBehavior"/> 在那里给出"赔钱 / 认罚"选项。
    /// ⑤ 击杀回血 —— 玩家亲手击杀人类/儿童后回复 <see cref="HealValue"/> 血（MCM 选项
    ///   <see cref="Settings.HealOnKill"/>，默认开启）。
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

        // 🔴 新切磋仲裁双方（2026-08-13：Invulnerable 底层无敌方案；旧虚拟血量字段已删）
        private Agent _agentA;
        private Agent _agentB;
        private bool _isDuelActive;

        // 🔴 2026-08-13 崩溃修复（判负拆两段）：OnAgentHit（引擎 HandleBlow 栈内）只做保命
        //（回血 + Invulnerable），完整收场（停战事件 → Brain 链 → EndFight 的 SetTeam/SetAttackState
        // 等 native 重入 + 恢复 Mortal）延后到下一帧 OnMissionTick（正常 tick 栈）。
        // 实机（2026-08-13）：判负时在 blow 栈内同步收场 → System.AccessViolationException
        //（native 内存损坏；asyncAITick 后台线程并发 tick 同一 agent 的战斗状态）。
        private bool _pendingDuel;

        // 🔴 2026-08-13 切磋收场冷却（判负晚到伤害事件防反击）：EndDuel 记录刚结束的切磋对 + 结束时刻，
        // AgentBrain 据此在短窗口内不把"切磋对手的命中"当袭击反击——实机（2026-08-13 15:21）：
        // 判负后 4ms 的迟滞 event_agent_damaged 触发骑士精神反击 → 以 Peace=false 重新真打 → 一方被打死。
        // 只匹配切磋双方互打（第三方攻击照常反击）；玩家 order_attack 走 "attack" 事件不经骑士精神，不受影响。
        private Agent _duelEndA;
        private Agent _duelEndB;
        private float _duelEndTime = -100f;
        private const float DuelEndGuardS = 3f;

        /// <summary>战斗广播冷却字典：同一对 (attacker.Index, victim.Index) 3秒内最多广播一次</summary>
        private static Dictionary<(int, int), float> _lastEventDamagedBroadcast = new Dictionary<(int, int), float>();
        private const float EVENT_DAMAGED_BROADCAST_COOLDOWN = 3.0f;

        /// <summary>击杀回血回复量（⑤）</summary>
        private const int HealValue = 20;

        /// <summary>友方保护提示冷却：同一目标 2 秒内最多提示一次（防连续挥砍刷屏）。</summary>
        private static readonly Dictionary<int, float> _lastFriendlyBlockedHint = new Dictionary<int, float>();
        private const float FRIENDLY_BLOCKED_HINT_COOLDOWN = 2.0f;

        // ══════════════════ PlayerMissionEventLogic 并入（2026-08-16 用户裁定：Mission 层统一总线，
        // 删除独立 MissionLogic——所有功能归位到事件源）══════════════════
        // D1 mission 进出感知（首帧防重）
        private bool _reported;
        // K2 犯罪关切（静态延迟确认：记账瞬间证人可能尚未注册，下一帧再查）
        private static string _pendingCrimeWord;
        private static float _pendingCrimeCheckAt;
        private static bool _crimeCareUsed;  // 同场犯罪只关切一次
        // L1 战斗表现统计（Mission 期间累计；battle_win/lose 挂载点消费后清空）
        private static readonly Dictionary<string, int> _battleKills = new Dictionary<string, int>();
        private static readonly HashSet<string> _battleWounded = new HashSet<string>();
        private static readonly object _battleStatLock = new object();

        // ══════════════════ 被捕随从名单（Phase E 转押，2026-08-14 分阶段）══════════════════
        // 🔴 为什么缓存而非 teardown 期读 Agent：Mission 结束时部分 Agent 已被引擎提前移除
        //（native _statePointer 失效），IsActive() 抛 NRE（实机 2026-08-14）。
        // 两态模型（用户裁定）：① 注册 = 只列入名单（守卫开始执法 = 嫌疑人）；② Confirm =
        // 最终确认（Agent 移除时带 Unconscious = 被打倒过且离场时仍倒地——引擎对倒地者做
        // 身体清理，中途移除；站着离场的移除时是 Active，Confirm 不置位）。Mission 结束
        // 只对 Confirm=true 的进牢，其余仅作 WorldEvent 嫌疑人（犯罪系统既有职责，不坐牢）。
        /// <summary>被捕随从信息（键 = agent.Index，Mission 内唯一且 Agent 移除后索引仍可作键）。</summary>
        private sealed class ArrestedCompanionInfo
        {
            public Hero Hero;       // 阶段①：逮捕瞬间缓存（Hero 是大地图持久对象，场景结束不销毁）
            public bool Confirm;    // 阶段②：最终确认（OnAgentRemoved 移除时 Unconscious / 脚本击晕事件）
        }
        private readonly Dictionary<int, ArrestedCompanionInfo> _arrestedCompanions = new Dictionary<int, ArrestedCompanionInfo>();
        private readonly object _arrestedLock = new object();

        /// <summary>逮捕登记（AgentBrain BecomeAlarmed 嫌疑犯分支调用）：逮捕瞬间 Agent 存活，
        /// 缓存 Hero 引用（仅玩家随从 Hero 非空者；模板 NPC 无 Hero → 不登记，仅 Mission 层倒地）。</summary>
        public void RegisterArrestedCompanion(Agent agent)
        {
            if (agent == null) return;
            var hero = (agent.Character as CharacterObject)?.HeroObject;
            if (hero == null) return;
            if (!FriendlinessHelper.IsPlayerPartyMember(hero)) return;
            lock (_arrestedLock)
            {
                _arrestedCompanions[agent.Index] = new ArrestedCompanionInfo { Hero = hero };
            }
        }

        /// <summary>解除逮捕登记（玩家调停成功 → 转质问，不再转押）。</summary>
        public void UnregisterArrestedCompanion(Agent agent)
        {
            if (agent == null) return;
            lock (_arrestedLock) { _arrestedCompanions.Remove(agent.Index); }
        }

        /// <summary>击倒最终确认（脚本击晕路径）：AgentBrain 击晕事件（agent_knocked_out）时调用——
        /// 脚本击晕不改引擎 AgentState，OnAgentRemoved 的 Unconscious 判定确认不到，须在击晕事件
        ///（Agent 存活的安全时机）置 Confirm。</summary>
        public void NotifyAgentKnockedOut(Agent agent)
        {
            if (agent == null) return;
            lock (_arrestedLock)
            {
                if (_arrestedCompanions.TryGetValue(agent.Index, out var info))
                    info.Confirm = true;
            }
        }

        /// <summary>竞技场战斗判定（友方保护放行，2026-08-21）：竞技场大赛/决斗（Tournament/Duel MissionMode）
        /// 或竞技场练习（arena_ 场景，Mode=StartUp 不在 Mode 过滤内）。
        /// 🔴 为什么放行：竞技场队伍按轮次随机重组——社交关系（Party/Clan/好感）≠ 竞技场队伍，
        /// 同家族/高好感的对手会被 IsFriendlyToPlayer 误判友方 → 友方保护把伤害无效化（实机 bug：
        /// 竞技场里打"自己人"对手不受伤害）。口径与 Settings.IsInteractionDisabled() 的竞技场部分
        /// 一致（Mode + arena_ 前缀 + ArenaPracticeFightMissionController behavior，后者 #if 兜底
        /// 1.2.12 无 SandBox 引用）。</summary>
        private static bool IsArenaCombat()
        {
            if (Mission.Current == null) return false;
            if (Mission.Current.Mode == MissionMode.Duel || Mission.Current.Mode == MissionMode.Tournament) return true;
            if (Mission.Current.SceneName?.StartsWith("arena_") == true) return true;
#if !MB2_V1212
            // 竞技场练习 behavior 标志语义最准（覆盖 arena_ 前缀判不到的变体场景）；
            // 前缀兜底低版本（1.2.12 无 SandBox 引用）
            if (Mission.Current.HasMissionBehavior<SandBox.Missions.MissionLogics.Arena.ArenaPracticeFightMissionController>()) return true;
#endif
            return false;
        }

        /// <summary>友方保护拦截提示（反馈明确，铁律 13 本地化）：{NAME} 是自己人——你不能这么做。</summary>
        private static void ShowFriendlyBlockedHint(Agent target)
        {
            if (target == null) return;
            float now = Mission.Current?.CurrentTime ?? 0f;
            if (_lastFriendlyBlockedHint.TryGetValue(target.Index, out float last)
                && now - last < FRIENDLY_BLOCKED_HINT_COOLDOWN)
                return;
            _lastFriendlyBlockedHint[target.Index] = now;

            // 本地化：LWN_ui_name_target（玩家可见文本）
            string name = target.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target");
            InformationManager.DisplayMessage(new InformationMessage(
                // 本地化：LWN_ui_hostile_friendly_blocked（玩家可见文本）
                LWNTextHelper.ResolveCompound("LWN_ui_hostile_friendly_blocked",
                    "{NAME} is on your side — you can't do that.",
                    ("NAME", name)),
                Colors.Gray));
        }

        /// <summary>击杀回血判定模式（⑤）：false = 仅主角本人击杀回血（默认，主角的战场特性）；
        /// true = 放宽为「玩家当前控制的角色」——玩家倒地接管小兵后，替身击杀也回血。
        /// 内部开关（非 MCM）：内容包 / 调试代码按需设置。</summary>
        internal static bool HealPlayerControlledAgent = false;

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
                RegisterDuel(a, b);
            }
        }

        /// <summary>
        /// 切磋仲裁登记（2026-08-13）：记录切磋双方（OnAgentHit 判负检测用）。
        /// 双方保持 Mortal 正常打（血条真实掉落）；不死 = 判负回血抢占引擎死亡判定
        ///（OnAgentHit 早于 `if (Health &lt; 1f) Die()`）+ EndDuel 收场窗口 Invulnerable 兜底。
        /// ⚠️ 旧虚拟血量方案已彻底废弃：禁止调用 InitDuel/GetVirtualHealth。
        /// </summary>
        public void RegisterDuel(Agent a, Agent b)
        {
            if (a == null || b == null) return;
            // 强制结束上一场（无人胜出）：判负待收场（_pendingDuel）→ 先走完收场再覆盖引用；
            // 仅登记未判负 → 直接清状态（双方还在打，但新切磋接管仲裁）。
            // 🔴 2026-08-13 崩溃修复后：RegisterDuel 不在 blow 栈内（Brain "duel" 事件链），
            // 此处同步调 EndPendingDuel 线程安全。
            if (_isDuelActive) { _isDuelActive = false; _pendingDuel = true; EndPendingDuel(); }
            _agentA = a;
            _agentB = b;
            _isDuelActive = true;
            DebugLogger.Log($"[Duel] 切磋登记: {a.Name}(Idx={a.Index}) vs {b.Name}(Idx={b.Index})");
        }
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectedAgentAffectsCalc, AgentState affectedAgentState, KillingBlow blow)
        {


            base.OnAgentRemoved(affectedAgent, affectedAgentAffectsCalc, affectedAgentState, blow);

            if (Settings.Instance.IsInteractionDisabled())
                return;


            // 侧容器模型：成员死亡/倒地 → 战斗提前收场（防计数泄漏、玩家+友方滞留队2）
            // 死后 FightEnemyAction 不会 OnEnd，计数不归零则全员还原永远不会触发
            CombatManager.NotifySideMemberRemoved(affectedAgent);

            // ⑤ 击杀回血（MCM 选项 Settings.Instance.HealOnKill，默认开启）：
            //    玩家亲手击杀（或击倒）人类/儿童 → 回复固定血量，不超过血量上限。
            //    击杀者判定用「身份是主角 Hero」（IsPlayer）而非 IsMainAgent：
            //    玩家倒地后控制替身时，替身的击杀仍算主角的击杀。
            if (Settings.Instance.HealOnKill
                && IsValidKill(affectedAgent, affectedAgentAffectsCalc)
                && AgentControlHelper.IsHumanOrChild(affectedAgent)
                && IsPlayer(affectedAgentAffectsCalc))
            {
                float newHealth = MathF.Clamp(
                    affectedAgentAffectsCalc.Health + HealValue, 0, affectedAgentAffectsCalc.HealthLimit);
                affectedAgentAffectsCalc.Health = newHealth;
            }

            // 🔴 2026-08-16（L1 战斗统计并入，原 PlayerMissionEventLogic.OnAgentRemoved）：队伍成员亲手
            // 击杀（受害者非玩家、非友军）→ 计数（battle_win/lose 挂载点 TakeBattleKills 消费，L1 旁白素材）
            if (affectedAgent != null && affectedAgentAffectsCalc != null
                && affectedAgent != affectedAgentAffectsCalc && affectedAgent != Agent.Main
                && !FriendlinessHelper.IsFriendlyToPlayer(affectedAgent)
                && FriendlinessHelper.IsPlayerPartyMember(affectedAgentAffectsCalc))
            {
                var killerHero = (affectedAgentAffectsCalc.Character as CharacterObject)?.HeroObject;
                if (killerHero != null)
                {
                    lock (_battleStatLock)
                    {
                        _battleKills[killerHero.StringId] = _battleKills.TryGetValue(killerHero.StringId, out var k) ? k + 1 : 1;
                    }
                }
            }

            // 死亡的可靠信号：被击杀 / 击晕的人类计入可搜刮尸体列表。
            // 这是主入口（OnAgentHit 里的 Health<=0 只能兜住「最后一击恰好被本逻辑捕获」的情况，
            // 补刀、击晕、流血致死等都会漏）。_deadAgents 是 HashSet，重复 Add 自动去重。
            if (affectedAgent != null && AgentControlHelper.IsHumanOrChild(affectedAgent)
                && !affectedAgent.IsMainAgent // 玩家自己永远不进可搜刮尸体列表
                && (affectedAgentState == AgentState.Killed || affectedAgentState == AgentState.Unconscious))
            {
                lock (_deadAgents)
                {
                    _deadAgents.Add(affectedAgent);
                }
            }

            // 🆕 被捕随从最终确认（2026-08-14 分阶段方案，用户裁定）：
            // 阶段②——OnAgentRemoved 是「Agent 离开 Mission」的最终时刻，带引擎最终状态：
            //   Unconscious = 最终确认（被打倒者被引擎身体清理中途移除 / teardown 移除时仍倒地）；
            //   Active/其他 = 站着离场（打赢/逃跑/中途醒了）→ Confirm 不置位，不坐牢。
            // 脚本击晕路径（mod 脚本动画不改引擎 State，移除时仍报 Active）走 NotifyAgentKnockedOut。
            // Mission 正在结束且名单里有刚确认的 → 立即尝试转押（事件驱动，覆盖 Agent 在
            // behavior 拆除后移除的顺序；OnRemoveBehavior 只做主触发）。
            if (affectedAgent != null)
            {
                lock (_arrestedLock)
                {
                    if (_arrestedCompanions.TryGetValue(affectedAgent.Index, out var arrestedInfo)
                        && affectedAgentState == AgentState.Unconscious)
                    {
                        arrestedInfo.Confirm = true;
                    }
                }
                bool missionEnding = Mission.Current == null
                    || Mission.Current.CurrentState != Mission.State.Continuing;
                if (missionEnding)
                    TryTransferArrestedCompanions();
            }

            // 玩家自己被打昏 → 交给大地图扣押流程（被打死则是原版的战役结束，不接管）
            if (affectedAgent != null && affectedAgent.IsMainAgent
                && affectedAgentState == AgentState.Unconscious)
            {
                OnPlayerKnockedOut();
            }
        }

        //验证击杀是否有效,并且不是自杀
        private bool IsValidKill(Agent affectedAgent, Agent affectorAgent)
        {
            return affectedAgent != null && affectorAgent != null && affectedAgent != affectorAgent;
        }
        //验证击杀者是否为玩家：
        //  默认（HealPlayerControlledAgent=false）—— 身份判定，CharacterObject.IsPlayerCharacter 引擎原生
        //    （玩家倒地换人后替身击杀不算，回血是主角的战场特性）；
        //  HealPlayerControlledAgent=true —— 控制判定，玩家当前操控的角色（IsMainAgent）击杀都回血。
        private bool IsPlayer(Agent agent)
        {
            if (agent == null) return false;
            return HealPlayerControlledAgent
                ? agent.IsMainAgent
                : agent.Character?.IsPlayerCharacter == true;
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

            // 🔴 2026-08-16（L1 负伤统计并入，事件驱动替代 tick 轮询——与 K1 同思路）：队伍成员受击
            // 且血 <0.5 → 记负伤（battle_win/lose 挂载点 TakeBattleWounded 消费，L1 旁白素材）
            if (affectedAgent != null && affectedAgent != Agent.Main
                && affectedAgent.HealthLimit > 0f
                && affectedAgent.Health / affectedAgent.HealthLimit < 0.5f
                && FriendlinessHelper.IsPlayerPartyMember(affectedAgent))
            {
                var woundedHero = (affectedAgent.Character as CharacterObject)?.HeroObject;
                if (woundedHero != null)
                {
                    lock (_battleStatLock) { _battleWounded.Add(woundedHero.StringId); }
                }
            }

            // 🆕 友方保护（主动攻击拦截）：MCM 开关关闭（默认）且目标是玩家友方 →
            // 伤害无效化（镜像切磋虚拟血回血手法：引擎 HandleBlow 内 OnAgentHit 早于死亡判定，
            // 写回能吃掉致命一击）+ 冷却提示；不进入死亡登记/犯罪广播链。
            // 开关打开（允许对友方动手）→ 不拦，正常结算与后果。
            // 竞技场（IsArenaCombat）→ 不拦：竞技场队伍按轮次随机重组，社交友方 = 场上的对手。
            if (attackerAgent?.IsMainAgent == true && affectedAgent != null && !affectedAgent.IsMainAgent
                && !Settings.Instance.AllowHostileOnAllies
                && !IsArenaCombat()
                && FriendlinessHelper.IsFriendlyToPlayer(affectedAgent))
            {
                affectedAgent.Health = MathF.Min(affectedAgent.Health + blow.InflictedDamage, affectedAgent.HealthLimit);
                ShowFriendlyBlockedHint(affectedAgent);
                return;
            }

            // 🆕 记录玩家实际命中过的敌方 Agent（战场血条过滤用）
            // 放在 OnAgentHit 而非 OnRegisterBlow：只有真正造成伤害才算，格挡/空挥不计
            if (attackerAgent != null && attackerAgent.IsMainAgent
                && affectedAgent != null && AgentControlHelper.IsHumanOrChild(affectedAgent)
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
            // 🔴 2026-08-31（用户裁定）：非互动场景（战场等）不登记——与 OnAgentRemoved 的
            // 开头 return 同口径；若兜底入口不一起关，战场最后一击会走这里把尸体加回可搜刮列表。
            if (affectedAgent.Health <= 0 && !affectedAgent.IsMainAgent
                && !Settings.Instance.IsInteractionDisabled())
            {
                if (AgentControlHelper.IsHumanOrChild(affectedAgent))
                {
                    lock (_deadAgents) // 简单的线程安全（虽然通常在主线程跑，但保险起见）
                    {
                        _deadAgents.Add(affectedAgent);
                        if (Settings.Instance.ShowDebugMessages)
                            InformationManager.DisplayMessage(new InformationMessage(
                                // 本地化：LWN_combat_body_added（玩家可见文本）
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

            // 🔴 2026-08-13 切磋判负（替代虚拟血量，用户裁定禁止虚拟血量）：
            // 双方 Mortal 正常受击（血条真实掉落、打击反馈全保留——实机证明 Invulnerable
            // native 拦伤害，全程不掉血，不能用来开打）。引擎 HandleBlow 内 OnAgentHit 早于
            // `if (Health < 1f) Die()`（反编译确认）：血归零即判负，OnDuelLoser 里回满血 →
            // 引擎根本走不到 Die（不死主保证）。不做第二套数值、不每击回血——
            // 真实血量掉落即打击反馈，判负一次回血即复原。
            if (affectedAgent.Health <= 0f)
            {
                DebugLogger.Log($"[Duel] 判负: {affectedAgent.Name}(Idx={affectedAgent.Index}) 血量归零");
                OnDuelLoser(affectedAgent);
            }
        }

        /// <summary>
        /// 🔴 2026-08-13 崩溃修复：判负拆两段——本方法（OnAgentHit 内，引擎 HandleBlow 栈上）
        /// 只做**保命**（回血 + Invulnerable），完整收场延后到下一帧 <see cref="EndPendingDuel"/>
        ///（OnMissionTick 调用，正常 tick 栈）。
        /// 为什么拆：旧实现在此处同步发停战事件 → Brain 链（ClearAllActions → FightEnemyAction.OnEnd
        /// → CombatManager.EndFight → SetTeam/SetAttackState/SetMovementDirection/SetScriptedCombatFlags
        /// 等 native 重入）+ 恢复 Mortal，全部在 blow 处理栈内执行；asyncAITick 后台线程并发 tick
        /// 同一 agent 的战斗状态 → 实机 System.AccessViolationException（判负时刻崩溃，2026-08-13）。
        /// 拆段不改变安全语义：帧 N 内败者行为仍是 FightEnemyAction（晚到伤害事件的 chivalry 早退
        /// 条件成立，不反击）；帧 N+1 收场登记冷却 → 迟滞伤害事件被冷却拦截
        ///（原同步方案中"行为已清 + 晚到伤害"反而是反击链的触发窗口，见 IsRecentDuelPair）。
        /// </summary>
        private void OnDuelLoser(Agent loser)
        {
            if (!_isDuelActive) return;
            _isDuelActive = false;
            _pendingDuel = true;

            // 胜负播报（点到为止：无死亡，血条归零判负）——无条件显示（设计哲学①：反馈明确，
            // 玩家是观众时必须知道谁赢了）
            Agent winner = null;
            if (loser != null && _agentA != null && _agentB != null)
            {
                winner = (loser == _agentA) ? _agentB : _agentA;
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：LWN_combat_duel_end（玩家可见文本）
                    LWNTextHelper.ResolveCompound("LWN_combat_duel_end",
                        "The duel is over — winner: {NAME}",
                        ("NAME", winner.Name?.ToString() ?? "")),
                    Colors.Green));
            }
            DebugLogger.Log($"[Duel] 切磋结束: winner={winner?.Name ?? "?"}(Idx={winner?.Index ?? -1}) loser={loser?.Name ?? "?"}(Idx={loser?.Index ?? -1})（收场延后到下一帧）");

            // 保命（必须在 blow 栈内完成——引擎 HandleBlow 在 OnAgentHit 之后才检查
            // `if (Health < 1f) Die()`）：① 先设 Invulnerable（native 拦 Die，兜底窗口）② 回满血
            // → 引擎走不到 Die。双方同时无敌：收场前的 ~1 帧窗口内不会再判负（另一方的打击不掉血）。
            foreach (var duelist in new[] { _agentA, _agentB })
            {
                if (duelist == null || !AgentControlHelper.SafeIsActive(duelist)) continue;
                try { duelist.SetMortalityState(Agent.MortalityState.Invulnerable); } // ①
                catch (Exception ex) { DebugLogger.Log($"[Duel] 设无敌失败: {ex.Message}"); }
                duelist.Health = duelist.HealthLimit;                                   // ②
            }
        }

        /// <summary>
        /// 🔴 2026-08-13 崩溃修复：判负收场（下一帧主线程执行，脱离 blow 栈——见 OnDuelLoser 注释）。
        /// ① 发停战事件 → AgentBrain 清 FightEnemyAction → OnEnd → CombatManager.EndFight
        ///    （归还原队 + WatchState 恢复 + 收刀 + 动作层打断 + 清索敌——通用收场全在 EndFight）；
        /// ② 恢复 Mortal（Invulnerable 兜底窗口关闭）；③ 冷却登记 + 清引用。
        /// 调用点：OnMissionTick（判负下一帧）/ RegisterDuel（新切磋顶替未收场的旧切磋）。
        /// </summary>
        private void EndPendingDuel()
        {
            if (!_pendingDuel) return;
            _pendingDuel = false;

            // ① 发停战事件（Brain 链的 native 重入此刻在正常 tick 栈，线程安全）
            foreach (var duelist in new[] { _agentA, _agentB })
            {
                if (duelist == null) continue;
                if (AgentControlHelper.SafeIsActive(duelist))
                {
                    try { AgentAIController.Instance?.SendEventToAgent(duelist, "event_stop_combat", duelist); }
                    catch (Exception ex) { DebugLogger.Log($"[Duel] 停战事件失败: {ex.Message}"); }
                }
            }
            // ② 停战已生效，关闭兜底窗口
            foreach (var duelist in new[] { _agentA, _agentB })
            {
                if (duelist == null || !AgentControlHelper.SafeIsActive(duelist)) continue;
                try { duelist.SetMortalityState(Agent.MortalityState.Mortal); }
                catch (Exception ex) { DebugLogger.Log($"[Duel] 恢复 Mortal 失败: {ex.Message}"); }
            }

            // ③ 收场冷却登记：清引用前快照——供 AgentBrain 判断"迟滞伤害是否来自刚结束的切磋"
            _duelEndA = _agentA;
            _duelEndB = _agentB;
            _duelEndTime = Mission.Current?.CurrentTime ?? -100f;

            // 4. 【关键】清空引用，允许下一次攻击触发新的切磋
            _agentA = null;
            _agentB = null;
        }

        /// <summary>
        /// 🔴 2026-08-13 切磋收场冷却判定：a/b 是否"刚结束的切磋对"（3s 窗口内）。
        /// 判负那一击的 event_agent_damaged 晚于停战事件到达 → 受害者 Brain 行为已清空，
        /// 护主逻辑会把它当"被袭击" → 骑士精神反击 → 以 Peace=false 重新真打 → 一方被打死（实机）。
        /// 窗口只匹配切磋双方互打：第三方攻击照常反击；玩家 order_attack 走 "attack" 事件，
        /// 不经骑士精神，不受本冷却影响。
        /// </summary>
        public bool IsRecentDuelPair(Agent a, Agent b)
        {
            if (a == null || b == null || a == b) return false;
            if (_duelEndA == null || _duelEndB == null) return false;
            if (a != _duelEndA && a != _duelEndB) return false;
            if (b != _duelEndA && b != _duelEndB) return false;
            if (Mission.Current == null) return false;
            if (Mission.Current.CurrentTime - _duelEndTime > DuelEndGuardS) return false;
            return true;
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

            // 竞技场（Duel/Tournament/arena_* 场景）/ 战场（Battle/Deployment）等场景不接管：
            // 这些场景的非战斗互动已被 Settings.IsInteractionDisabled() 关闭，
            // 倒地一律交回原版流程（竞技场判负/战死），不触发"武器被夺走"扣押
            if (Settings.Instance.IsInteractionDisabled()) return;

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

            // 🔴 2026-08-16（PlayerMissionEventLogic 并入）：D1 mission 进出感知（首帧分类广播）+
            // K2 犯罪关切延迟确认——全部 try/catch 各段独立
            try { ReportMissionEntered(); } catch (Exception ex) { DebugLogger.Log($"[MissionSense] 首帧分类失败: {ex.Message}"); }
            try { CheckPendingCrimeCare(); } catch (Exception ex) { DebugLogger.Log($"[Care] 犯罪关切异常: {ex.Message}"); }

            // 🔴 2026-08-13 崩溃修复：判负收场延后帧（blow 栈内同步收场 → AccessViolation，见 OnDuelLoser 注释）
            EndPendingDuel();

            // 队伍变更日志门禁：记录 Mission 起始时刻；门禁开启时打全场初始队伍基线
            CombatManager.OnCombatManagerTick(Mission.Current);

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
                
                 // 本地化：LWN_combat_damage_log（玩家可见文本）
                 //   InformationManager.DisplayMessage(new InformationMessage(                       LWNTextHelper.ResolveCompound("LWN_combat_damage_log",                            "AttackTriggerMissionLogic - OnRegisterBlow: {ATTACKER} dealt {DAMAGE} damage to {VICTIM}",                            ("ATTACKER", attacker.Name?.ToString() ?? ""),                            ("VICTIM", victim.Name?.ToString() ?? ""),                            ("DAMAGE", b.InflictedDamage.ToString())),                        Colors.Yellow));
            }

            // 🆕 友方保护：玩家攻击友方 → 不广播 event_agent_damaged
            // （NPC 攻击者不适用，条件自带 IsMainAgent；伤害无效化在 OnAgentHit）。
            // 开关打开（允许对友方动手）→ 广播照常（友方受害者/旁观者正常反应）。
            // 竞技场（IsArenaCombat）→ 不拦：社交友方 = 场上对手（脑在 IsInteractionDisabled 下
            // 不处理事件，广播无接收者消费，无害）。
            if (attacker.IsMainAgent && !Settings.Instance.AllowHostileOnAllies
                && !IsArenaCombat() && FriendlinessHelper.IsFriendlyToPlayer(victim))
            {
                ShowFriendlyBlockedHint(victim);   // 反馈明确：拦截提示（2s 冷却，与 OnAgentHit 共享防刷屏）
                return;
            }

            // 人类或儿童（human_child）受害者都走正常事件链——小孩已注册 brain，与大人同等对待。
            // 动物等非人受害者保持原行为。
            if (!AgentControlHelper.IsHumanOrChild(victim)) return;


            if (victim != null && attacker != null && victim != attacker)
            {
                // 🔴 2026-08-16（K1 血线关切，事件驱动重构，用户裁定）：玩家受击 → 定向同步给 15m 内
                // 队伍成员（event_agent_damaged 既有事件链——AgentBrain 处理护主参战 + 血线关切）。
                // 只发队伍成员（BroadcastEventInRange 无身份过滤会惊动围观平民；不恢复全局广播——
                // 08-09 玩家脑 Suspend 裁定保持）。挂 OnRegisterBlow = 既有 NPC 受害直发同点（blow 生成栈，
                // 同步 ReceiveEvent 安全；OnAgentHit 是 HandleBlow 栈内，同步调 brain 有 native 重入风险，
                // 2026-08-13 教训）。血线预估：args[2] = blow.InflictedDamage——OnRegisterBlow 时血量未结算，
                // 随从用 Health - damage（预估 = 结算后血线，防单击大伤害漏检）。Health>0 守卫：致命一击倒地不喊。
                if (victim.IsMainAgent && victim.Health > 0f)
                {
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !AgentControlHelper.SafeIsActive(a) || a == victim || a == attacker) continue;
                        if (a.Position.DistanceSquared(victim.Position) > 15f * 15f) continue;
                        if (!FriendlinessHelper.IsPlayerPartyMember(a)) continue;
                        AgentAIController.Instance?.SendEventToAgent(a, "event_agent_damaged", attacker, victim, b.InflictedDamage);
                    }
                }

                // 🆕 直接受害者广播放行 NPC↔NPC：被打方的脑必须知道"谁在打我"才能转身还手。
                // （旧代码整条事件链被 `!attacker.IsMainAgent` 门控掐死 → NPC 战斗中第三方攻击
                //   完全无感，只会盯着开战时锁定的目标砍——学者背后捅纺织工 49 秒不回头，2026-08-09 实测）
                // 玩家是受害者 → 不直发：玩家没有 brain（OnAgentCreated 排除），且直发会触发
                // 玩家脑的护主/参战链（说 NPC 台词 + Suspend 玩家 → 整场无法移动，2026-08-09 修复）。
                if (!victim.IsMainAgent)
                    AgentAIController.Instance.SendEventToAgent(victim, "event_agent_damaged", attacker, victim);

                // 范围广播：周围 25m 内 NPC 收到 event_agent_damaged，同一对 3 秒内最多一次

                var key = (attacker.Index, victim.Index);
                float now = Mission.Current?.CurrentTime ?? 0f;
                if (!_lastEventDamagedBroadcast.TryGetValue(key, out float last) || now - last >= EVENT_DAMAGED_BROADCAST_COOLDOWN)
                {
                    _lastEventDamagedBroadcast[key] = now;
                    //暂时关闭广播
                 //   AgentAIController.Instance?.BroadcastEventInRange(victim.Position, 25f, "event_agent_damaged", false, attacker, victim);
                }

            }

            // 以下为玩家门控逻辑（切磋为废弃分支）
            if (!attacker.IsMainAgent || victim.IsMainAgent) return;

            // 🔴 2026-08-12（AttackCivilian）：玩家当街打非友方平民（暴徒豁免——原版语义打暴徒合法）→
            // 广播围观者脉冲（劝阻→升级→参战）。不恢复 event_agent_damaged 范围广播（曾触发玩家脑
            // 护主/参战链导致玩家无法移动，2026-08-09 修复），走专用轻事件：周围 15m 围观者收到后
            // 走 AttackCivilian 警戒脉冲（2.0 + 3s 抑制 → Cautious 喝止；不听继续打 → 升级 Alarmed 参战）。
            if (victim != null && !FriendlinessHelper.IsFriendlyToPlayer(victim))
            {
                bool isGangster = (victim.Character as CharacterObject)?.Occupation == Occupation.Gangster;
                if (!isGangster)
                    AgentAIController.Instance?.BroadcastEventInRange(victim.Position, 15f, "PlayerAttackedCivilian", false, attacker, victim);
            }
            // 🔴 2026-08-12（PlayerAttackedAlly）：玩家侵害友方（打随从/同伴/友军）→ 同样广播周围——
            // 日志实锤：之前只有受害者本人知道（damaged 直发），周围卫兵只看到拔刀、收刀后完全无反应。
            // 犯罪体系（WorldEvent）已记账，但 mod 层警戒反应缺位。队友旁观者不涨警戒（IsPlayerTeammate
            // 消费端排除；玩家教训自己人 = 家事，信任主公），卫兵/路人收到后喝止 → 升级参战。
            else if (victim != null && FriendlinessHelper.IsFriendlyToPlayer(victim))
            {
                AgentAIController.Instance?.BroadcastEventInRange(victim.Position, 15f, "PlayerAttackedAlly", false, attacker, victim);
            }

            // 👁 前置哨兵观测（2026-09-02）：玩家对人类的 0 伤害刀——补偿段若被条件挡掉，
            // 从这里看是哪条条件（格挡/招架/竞技场/总闸/友方/敌队）拦的。
            if (attacker.IsMainAgent && victim != null && b.InflictedDamage <= 0
                && AgentControlHelper.IsHumanOrChild(victim) && !IsArenaCombat() && !Settings.Instance.IsInteractionDisabled())
            {
                bool teamEnemy = attacker.Team != null && victim.Team != null
                    && attacker.Team.IsValid && victim.Team.IsValid && attacker.Team.IsEnemyOf(victim.Team);
                DebugLogger.Log($"[WarningStrike] 0伤害刀: {victim.Name}(Idx={victim.Index}) blockedShield={collisionData.AttackBlockedWithShield} backShield={collisionData.CollidedWithShieldOnBack} colRes={collisionData.CollisionResult} 敌队={teamEnemy} 友方={FriendlinessHelper.IsFriendlyToPlayer(victim)} attTVal={attacker.Team?.IsValid} vicT={victim.Team?.TeamIndex} vicTVal={victim.Team?.IsValid}");
            }

            // 🔴 2026-09-02 警告刀补偿（引擎伤害接口方案，用户裁定）：单机引擎对「非敌方人类」取消近战
            // 伤害（Mission.MeleeHitCallback → CancelsDamageAndBlocksAttackBecauseOfNonEnemyCase →
            // InflictedDamage=0；Agent.HandleBlow 里 `<=0` 直接 return）——玩家打和平村民永远不掉血，
            // 但 OnRegisterBlow 照常触发（上面旁白/警戒/围观全发）。此处用 AgentDamageHelper 补一条
            // 真伤害：模拟伤害掉血 + 血条反馈 + OnAgentHit 链（记身价、负伤统计），保命 clamp（最多
            // 降到 1 血）、不建敌队不进战斗（设计上后续警戒/质问/赔偿由脑流程处理）。
            if (victim != null && attacker.IsMainAgent && b.InflictedDamage <= 0
                && !collisionData.AttackBlockedWithShield && !collisionData.CollidedWithShieldOnBack
                // 格挡/招架（非盾挡）也走 0 伤害路径——不得补警告伤（挥击被对方弹开是有效防御）
                && collisionData.CollisionResult != CombatCollisionResult.Parried
                && collisionData.CollisionResult != CombatCollisionResult.Blocked
                && collisionData.CollisionResult != CombatCollisionResult.ChamberBlocked
                && !IsArenaCombat()
                // 总闸（与 OnAgentRemoved/OnPlayerKnockedOut 同口径）：战场/训练场/自定义战斗等
                // 交互关闭场景不补警告伤（原生战斗结算归属引擎，mod 侧不插一手）
                && !Settings.Instance.IsInteractionDisabled()
                && (!FriendlinessHelper.IsFriendlyToPlayer(victim) || Settings.Instance.AllowHostileOnAllies)
                // 🔴 2026-09-02 修正：村民 Team = Team.Invalid（TeamIndex -1，IsValid=false）——
                // 旧写法（Team.IsValid 前置 + IsEnemyOf 反推）把无效 team 一律挡掉，警告刀永远不触发；
                // 语义应为「不是明确敌队即放行」。IsValid 判定保留在 IsEnemyOf 调用前短路（防 NRE），
                // 无效/缺省 team → 不是敌队 → 放行（引擎取消伤害的正是这类场景）。
                && !(attacker.Team != null && victim.Team != null
                     && attacker.Team.IsValid && victim.Team.IsValid
                     && attacker.Team.IsEnemyOf(victim.Team)))
            {
                // 身价首记（同人重复打击不重复累计身价——身价是"受害者值多少"，不是"打了几刀"）
                bool firstRecord = !FriendlinessHelper.IsFriendlyToPlayer(victim);
                var pending = AgentAIController.Instance?.PendingWorldEvent;
                if (firstRecord && pending?.AssaultVictimNames != null)
                    firstRecord = !pending.AssaultVictimNames.Contains(victim.Name?.ToString());
                AgentDamageHelper.ApplyWarningStrike(victim, b, collisionData, firstRecord);
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

        // ══════════════════ 被捕随从转押（Phase E，2026-08-14 分阶段重构）══════════════════
        // 玩家离开场景（Mission 结束）时，名单里 Confirm=true 的随从 → 转押事件定居点牢房
        //（TakePrisonerAction 原版 hero 俘虏机制：进 settlement.PrisonRoster、从原队伍移除、
        // 原版 captivity 状态机接管）。事件保持 Active（嫌疑人=随从）——玩家可回定居点赎回。
        // 🔴 分阶段（用户裁定 2026-08-14）：① 逮捕启动 → RegisterArrestedCompanion 只列入名单；
        // ② OnAgentRemoved 移除时 Unconscious / 脚本击晕事件 → Confirm=true；③ Mission 结束
        //（OnRemoveBehavior 主触发 + OnAgentRemoved 事件驱动兜底）→ Confirm=true 的进牢，
        // 其余仅作 WorldEvent 嫌疑人（犯罪系统职责，不坐牢）。
        // 只读名单 + 大地图 Hero 数据，零 teardown 期 Agent native 访问（实机 2026-08-14：
        // teardown 期 IsActive() 对已移除 Agent 抛 NRE）。转押成功即移出名单（幂等防双押）。
        public override void OnRemoveBehavior()
        {
            base.OnRemoveBehavior();
            // 阶段③ 主触发：Mission 结束 → Confirm=true 的进牢；未确认的保留在名单
            //（其 OnAgentRemoved 可能在 behavior 拆除后才到，事件驱动路径会补转押）
            TryTransferArrestedCompanions();
        }

        /// <summary>阶段③ 转押（幂等）：名单里 Confirm=true 且存活且玩家随从 → 进牢，
        /// 成功转押即移出名单（防双押）；未确认条目保留（等 OnAgentRemoved 迟到确认）。
        /// 名单是 MissionLogic 实例字段，Mission 结束随实例回收，无泄漏。</summary>
        private void TryTransferArrestedCompanions()
        {
            if (Campaign.Current == null) return;
            List<ArrestedCompanionInfo> snapshot;
            lock (_arrestedLock)
            {
                if (_arrestedCompanions.Count == 0) return;
                snapshot = new List<ArrestedCompanionInfo>(_arrestedCompanions.Values);
            }

            var jailed = new List<ArrestedCompanionInfo>();
            foreach (var info in snapshot)
            {
                try
                {
                    if (info.Hero == null || !info.Confirm) continue;          // 未最终确认 → 仅嫌疑人，不坐牢
                    if (!info.Hero.IsAlive) continue;                           // 兜底：Hero 死亡系统接管
                    if (!FriendlinessHelper.IsPlayerPartyMember(info.Hero)) continue;

                    // 事件定居点：PendingWorldEvent（本场 Mission 的犯罪事件）→ 持久化 store 兜底
                    //（FindOnGoing 已内置 PendingWorldEvent 兜底，见铁律 9）
                    WorldEvent evt = null;
                    var pending = AgentAIController.Instance?.PendingWorldEvent;
                    if (pending != null && !string.IsNullOrEmpty(pending.TargetSettlementId))
                        evt = WorldEventStore.FindOnGoing(pending.TargetSettlementId);
                    else if (Settlement.CurrentSettlement != null)
                        evt = WorldEventStore.FindOnGoing(Settlement.CurrentSettlement.StringId);
                    if (evt == null || evt.TargetSettlement == null) continue;

                    var settlement = evt.TargetSettlement;
                    // 转押（原版 hero 俘虏机制）
                    TakePrisonerAction.Apply(settlement.Party, info.Hero);
                    // 注册到赎回菜单（CompanionDetentionBehavior）
                    CompanionDetentionBehavior.RegisterDetained(info.Hero, settlement, evt.EventId);

                    // 提示消息（铁律 13）：你的随从 {NAME} 被关进了 {SETTLEMENT} 的牢房。
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：LWN_ui_arrest_msg（玩家可见文本）
                        LWNTextHelper.ResolveCompound("LWN_ui_arrest_msg",
                            "Your companion {NAME} has been locked in the jail of {SETTLEMENT}.",
                            // 本地化：LWN_ui_name_target（玩家可见文本）
                            ("NAME", info.Hero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target")),
                            // 本地化：LWN_ui_detention_place_here（玩家可见文本）
                            ("SETTLEMENT", settlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_detention_place_here", "here"))),
                        Colors.Red));
                    jailed.Add(info);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Arrest] 转押失败: {ex.Message}");
                }
            }

            if (jailed.Count > 0)
            {
                lock (_arrestedLock)
                {
                    foreach (var key in _arrestedCompanions
                        .Where(kv => jailed.Contains(kv.Value))
                        .Select(kv => kv.Key).ToList())
                    {
                        _arrestedCompanions.Remove(key);
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 🔴 PlayerMissionEventLogic 并入（2026-08-16 用户裁定：删除独立 MissionLogic——
        // Mission 层统一走本总线，功能归位到事件源）
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>D1：Mission 首帧分类广播（实例级 _reported 防重；MissionLogic 随 Mission 销毁，恰一次）。
        /// 分类（全部 try/catch，确定性 C#）：settlement = Settlement.CurrentSettlement（反编译确认 =
        /// MainParty.CurrentSettlement）→ hideout/siege（攻守分流）/settlement（+子地点）；无 → battle
        /// （最近定居点锚点 + 双方参战人数）。mission_* 频次高 → chatComment=false 只感知（话题预算留给大事）。</summary>
        private void ReportMissionEntered()
        {
            if (_reported) return;
            _reported = true;
            // 🔴 跨 Mission 残留清理：旧 Mission 的犯罪延迟确认标记作废（新 Mission 的 PendingWorldEvent
            // 是新实例，旧犯罪不该在新 Mission 里触发关切）
            _pendingCrimeWord = null;
            try
            {
                if (Hero.MainHero == null || Campaign.Current == null) return;
                Settlement settlement = Settlement.CurrentSettlement;
                string key;
                string desc;
                string name = settlement?.Name?.ToString() ?? "";
                if (settlement != null)
                {
                    if (settlement.IsHideout)
                    {
                        key = "mission_hideout";
                        desc = $"主公闯进了一处藏身处（{name}）";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                    }
                    else if (settlement.SiegeEvent != null)
                    {
                        // 🔴 2026-08-16 审查修正：围城图标双方可见，攻守均为亲见——
                        // BesiegerCamp.LeaderParty == MainParty → 攻城方"随军攻打"；否则守城/援军
                        // "抵御围攻"（禁误报攻打自家城）。实锤：SiegeEvent.BesiegerCamp.LeaderParty（MobileParty）
                        key = "mission_siege";
                        bool attacker = settlement.SiegeEvent.BesiegerCamp?.LeaderParty == MobileParty.MainParty;
                        desc = attacker ? $"主公随军攻打 {name}" : $"主公在 {name} 抵御围攻";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                    }
                    else
                    {
                        key = "mission_settlement";
                        string locName = CampaignMission.Current?.Location?.Name?.ToString();
                        desc = string.IsNullOrEmpty(locName) ? $"主公进了 {name}" : $"主公进了 {name}（{locName}）";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                    }
                }
                else
                {
                    // 野战/无定居点关联场景（城门遇袭/村庄外等）：最近定居点锚点（方案 A helper 复用）
                    key = "mission_battle";
                    string near = WorldFactProvider.NearestSettlementName(15f);
                    desc = near != null ? $"主公在 {near} 附近的旷野与人交战" : "主公在荒野与人交战";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                    // 双方参战人数（信息面 #29/#33 增强，亲见级）：IsEnemyOf(Agent.Main) 为假 = 我方
                    try
                    {
                        int mine = 0, theirs = 0;
                        if (Mission.Current?.Agents != null)
                        {
                            foreach (var a in Mission.Current.Agents)
                            {
                                if (a == null || !AgentControlHelper.SafeIsActive(a)) continue;
                                if (Agent.Main != null && a.IsEnemyOf(Agent.Main)) theirs++;
                                else mine++;
                            }
                        }
                        int total = mine + theirs;
                        if (total > 0)
                            desc += $"，双方投入约 {total} 余人";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                    }
                    catch { /* 人数异常不加（try/catch） */ }
                }
                DebugLogger.Log($"[Sense] mission 分类: {key}: {desc}");
                ImEventBroadcaster.BroadcastPlayerEvent(key, desc, chatComment: false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MissionSense] mission 分类失败: {ex.Message}");
            }
        }

        /// <summary>K2 犯罪关切延迟确认（记账瞬间证人可能尚未注册，下一帧再查——原 PlayerMissionEventLogic
        /// tick 轮询迁移；_pendingCrimeWord 非空才工作，无犯罪记账时零开销）。</summary>
        private static void CheckPendingCrimeCare()
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingCrimeWord)) return;
                if (Mission.Current == null || Agent.Main == null || !AgentControlHelper.SafeIsActive(Agent.Main)) return;
                // 每帧确认窗口（记账后 ~2s 内有效）
                float now = Mission.Current.CurrentTime;
                if (_pendingCrimeCheckAt > 0f && now - _pendingCrimeCheckAt > 2f)
                {
                    _pendingCrimeWord = null;
                    return;
                }
                var pending = AgentAIController.Instance?.PendingWorldEvent;
                if (pending == null) return;
                bool hasWitness = pending.WitnessTestimonies?.Any(t => t != null
                    && (t.WitnessHeroId != null || t.TemplateId != null)) == true;
                if (!hasWitness)
                {
                    DebugLogger.Log($"[Care] 犯罪关切跳过：无目击者（没人看见 = 随从没理由急）");
                    _pendingCrimeWord = null;
                    return;
                }
                if (_crimeCareUsed) return;
                _crimeCareUsed = true;
                if (MBRandom.RandomFloat >= 0.5f)
                {
                    DebugLogger.Log($"[Care] 犯罪关切未中签（概率 0.5）");
                    _pendingCrimeWord = null;
                    return;
                }
                Agent nearest = FriendlinessHelper.FindNearestPartyMemberAgent(Agent.Main);
                if (nearest == null)
                {
                    DebugLogger.Log($"[Care] 犯罪关切跳过：无同场景队伍成员在场");
                    _pendingCrimeWord = null;
                    return;
                }
                // 本地化：LWN_im_care_crime（玩家可见文本；护主不告发——只关切/催促方向）
                string line = LWNTextHelper.ResolveText("LWN_im_care_crime", "Run, my lord! The guard saw it!");
                SpeechChannel.Say(nearest, line, SpeechPriority.Warning,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(nearest), Agent.Main, "crime_witnessed", null));
                DebugLogger.Log($"[Care] {nearest.Name} 犯罪关切: {line}");
                _pendingCrimeWord = null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Care] 犯罪关切失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 犯罪感知 + 犯罪当场关切（G3①/K2，2026-08-16）：犯罪记账瞬间（KnockoutFlow.Resolve /
        /// StealManager 记账调用处）调用。
        /// G3① 总是写：同场景队伍成员 RecordDynamicMemory（随从亲眼所见即亲历——"无第三方目击 →
        /// PendingWorldEvent 不激活"只意味着系统层面无 Alarmed/无犯罪事件后续反应（没人看见 = 世界层面
        /// 没发生），**不等于随从不感知**——叙事层面发生了，随从记忆照写）。
        /// K2 概率关切：PendingWorldEvent 有目击者（没人看见 → 随从没理由急）→ 概率 0.5 + 同场一次 →
        /// SpeechChannel 冒泡「主公快走，那守卫瞧见了！」（护主不告发——随从是同伙，看见守卫来抓
        /// 不会喊"抓小偷"，模板只允许关切/催促方向）。证人注册可能晚于记账 → 延迟确认在
        /// <see cref="CheckPendingCrimeCare"/>（记账后下一帧查）。
        /// 罪行词 = 记账 ActionType 既有词（Steal/AttackAlly/Knockout → 复用 LWN_crime_witness_act_*
        /// 描述模板，不新造罪行文案）。
        /// </summary>
        public static void ReportPlayerMisconduct(string actionTypeWord)
        {
            try
            {
                if (Hero.MainHero == null || Mission.Current == null) return;
                // 罪行描述（复用 WorldEvent 域既有描述模板）
                string crimeDesc = actionTypeWord switch
                {
                    // 本地化：crime_witness_act_steal（玩家可见文本）
                    "Steal" => LWNTextHelper.ResolveText("LWN_crime_witness_act_steal", "stole something"),
                    // 本地化：crime_witness_act_attack（玩家可见文本）
                    "AttackAlly" => LWNTextHelper.ResolveText("LWN_crime_witness_act_attack", "started a fight"),
                    // 本地化：crime_witness_act_knockout（玩家可见文本）
                    "Knockout" => LWNTextHelper.ResolveText("LWN_crime_witness_act_knockout", "knocked someone out"),
                    // 本地化：crime_witness_act_someone_stirring（玩家可见文本）
                    _ => LWNTextHelper.ResolveText("LWN_crime_witness_act_someone_stirring", "was making trouble"),
                };
                // 地点锚点（方案 A helper 复用）
                string near = WorldFactProvider.NearestSettlementName(15f);
                string desc = near != null ? $"主公刚刚{crimeDesc}（{near}附近）" : $"主公刚刚{crimeDesc}";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                // 🔴 2026-08-16（方案 Q 补漏，P2）：犯罪计数（画像统计【主公的成色】）——
                // 确定性聚合，与 battle_win/lose、imprison 同款挂钩（之前 RecordCrime 是死代码）
                PlayerImageStore.RecordCrime();
                // G3① 感知（总是，同场景随从——亲历者）
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                int written = 0;
                foreach (var m in members)
                {
                    if (m == null || m == Hero.MainHero) continue;
                    if (!ImChatManager.IsPresentInMission(m.StringId)) continue;
                    AllNpcMemoryManager.GetMemory(m.StringId)?.RecordDynamicMemory(desc);
                    written++;
                }
                DebugLogger.Log($"[Sense] 犯罪感知 {actionTypeWord}: 「{desc}」 → {written} 名同场景队伍成员");
                // K2 延迟确认登记（证人可能尚未注册 → CheckPendingCrimeCare 下一帧查）
                _pendingCrimeWord = actionTypeWord;
                _pendingCrimeCheckAt = Mission.Current.CurrentTime;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Misconduct] 犯罪感知失败: {ex.Message}");
            }
        }

        /// <summary>G6① 战利品感知（2026-08-16，信息面 #30）：玩家打开战利品挑选界面 →
        /// 同场景随从 RecordDynamicMemory（亲见——"主公正在翻拣战利品"）。挂载点 =
        /// InteractionMissionView 的 LootFlowSession.OpenPerson/OpenChest 调用处。</summary>
        public static void ReportLootOpen()
        {
            try
            {
                if (Mission.Current == null || Hero.MainHero == null) return;
                string desc = "主公正在翻拣战利品";  // lwn-ignore: A（记忆描述 → LLM prompt 材料，铁律 13 豁免）
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                int written = 0;
                foreach (var m in members)
                {
                    if (m == null || m == Hero.MainHero) continue;
                    if (!ImChatManager.IsPresentInMission(m.StringId)) continue;
                    AllNpcMemoryManager.GetMemory(m.StringId)?.RecordDynamicMemory(desc);
                    written++;
                }
                DebugLogger.Log($"[Sense] 战利品感知 → {written} 名同场景队伍成员");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Sense] 战利品感知失败: {ex.Message}");
            }
        }

        /// <summary>取并清空本场战斗的击杀统计（battle_win/lose 挂载点调用，L1 旁白素材）。</summary>
        public static Dictionary<string, int> TakeBattleKills()
        {
            lock (_battleStatLock)
            {
                var copy = new Dictionary<string, int>(_battleKills);
                _battleKills.Clear();
                return copy;
            }
        }

        /// <summary>取并清空本场战斗的负伤名单（battle_win/lose 挂载点调用，L1 旁白素材）。</summary>
        public static HashSet<string> TakeBattleWounded()
        {
            lock (_battleStatLock)
            {
                var copy = new HashSet<string>(_battleWounded);
                _battleWounded.Clear();
                return copy;
            }
        }
    }
}

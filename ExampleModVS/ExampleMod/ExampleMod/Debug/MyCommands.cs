using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.Engine;
using TaleWorlds.DotNet;
using TaleWorlds.ScreenSystem;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using psai.net;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using SandBox.Objects.Usables;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Party;
using Helpers;
using System.Reflection;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.SceneInformationPopupTypes;
using System.IO;

#if !MB2_V1212
using SandBox.Missions;
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
#endif

namespace LivingWorldNpcs
{
    public class MyCommands
    {

      

        [CommandLineFunctionality.CommandLineArgumentFunction("summon_npc", "custom")]
        public static string ExecuteSummonPureNpc(List<string> args)
        {
            // 1. 检查是否在场景中且主角存在
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must in mission";
            }
            // 2. 检查是否有参数输入
            if (args.Count == 0)
            {
                return "ERROR：PLEASE INPUT hero id";
            }
            

            for (int i = 0; i < args.Count; i++)
            {
                string heroId = args[i];
                HeroSpawnerMissionBehavior.TeleportExistingHeroById(heroId,2.0f);
            }

            return "spawn suscess";
        }


        //让两个人决斗
        [CommandLineFunctionality.CommandLineArgumentFunction("duel_npc", "custom")]
        public static string ExecuteDuel(List<string> args)
        {


            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: not in misson";
            }

            if (args.Count != 2)
            {
                string received = string.Join(", ", args);
                return $"error: params num is not 2 :{args.Count},received:[{received}]";
            }
            //基于ID搜索当前场景已经召唤的agent
            string targetId1 = args[0];
            string targetId2 = args[1];
            Agent agent1 = null;
            Agent agent2 = null;
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.Character != null && agent.IsHuman)
                {
                    if (targetId1 == "player")
                        agent1 = Agent.Main;
                    if (targetId2 == "player")
                        agent2 = Agent.Main;
                    if (agent.Character.StringId == targetId1 && agent1 == null)
                        agent1 = agent;
                    if (agent.Character.StringId == targetId2 && agent2 == null)
                        agent2 = agent;
                    if (agent1 != null && agent2 != null)
                        break;
                }
            }
            if (agent1 == null || agent2 == null)
            {
                ExecuteSummonPureNpc(args);
                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (agent.Character != null && agent.IsHuman)
                    {
                        if (agent.Character.StringId == targetId1 && agent1 == null)
                            agent1 = agent;
                        if (agent.Character.StringId == targetId2 && agent2 == null)
                            agent2 = agent;
                        if (agent1 != null && agent2 != null)
                            break;
                    }
                }
            }
            if (agent1 == null) return $"{targetId1} not find";
            if (agent2 == null) return $"{targetId2} not find";
            if(agent1 == agent2) return "can not duel with self";

            //AgentControlHelper.StartFight(agent1, agent2);
            CombatManager.StartFight(agent1, agent2, -1, -1, Peace: true);
            return "find success";
        }
       
        
        
       

        [CommandLineFunctionality.CommandLineArgumentFunction("do_anim", "custom")]
        public static string ExecuteDoAnim(List<string> args)
        {
            // 1. 检查是否在场景中且主角存在
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must be in a scene/mission to use this command.";
            }

            // 2. 参数：args[0] = 动作名（必填），args[1] = 可选角色 id（缺省 = 主角）
            if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                return "usage: custom.do_anim <actionName> [agentId]   e.g. custom.do_anim act_gun_reload";
            }

            string actionName = args[0];
            Agent executeAgent = Agent.Main;
            string note = string.Empty;
            if (args.Count >= 2 && !string.IsNullOrWhiteSpace(args[1]))
            {
                // 首参约定：解析不出目标就【回落主角】并注明，不报 not found
                var namedAgent = Mission.Current.Agents.FirstOrDefault(a => a.Character?.StringId == args[1]);
                if (namedAgent != null)
                    executeAgent = namedAgent;
                else
                    note = $" [note: no agent with id '{args[1]}' -> using main agent]";
            }

            // 3. 动作名 → 引擎索引。
            //    🔴 ActionIndexCache.Create 解析失败时【静默返回 act_none】（不抛异常）——
            //       必须显式判掉：动作没注册（action_types / action_sets / project.mbproj 三件套缺一）
            //       和"播不出来"在游戏里长得一模一样，不报出来就没法排查。
            ActionIndexCache actionIndex = ActionIndexCache.Create(actionName);
            if (actionIndex == ActionIndexCache.act_none)
            {
                return $"FAILED: action '{actionName}' is NOT registered (resolved to act_none). check: "
                     + "action_types.xml declares it + action_sets.xml binds it + project.mbproj registers both files.";
            }

            // 4. 动作时长 —— 引擎事实：装填时长 = 动画时长，这个数就是装填节奏本身。
            //    0.00s 通常意味着 action_sets 里那条 animation= 的 clip 名没解析上。
            float duration = MBActionSet.GetActionAnimationDuration(executeAgent.ActionSet, actionIndex);
            string durNote = duration > 0f ? string.Empty
                : " [note: duration 0.00s -> the clip name in action_sets.xml may not resolve]";

            try
            {
                // 🔴 第 8 个参数 `blendOutPeriodToNoAnim` 必须传 0（默认是 0.4 秒）。
                //    不传 = 动作走到尾声会被引擎用 0.4 秒淡出到「无动画」（拉回静止姿势）再弹回；
                //    逐帧取证实测：循环动作一圈里只有中段是真的在播（2026-09-22，见 anim_trace）。
                executeAgent.SetActionChannel(0, actionIndex, false, 0UL, 0f, 1f, -0.2f, 0f, 0f, false, -0.2f, 0, true);

                return $"OK: {executeAgent.Name} plays '{actionName}' (duration {duration:0.00}s){durNote}{note}";
            }
            catch (System.Exception e)
            {
                return "Error: " + e.Message;
            }
        }

        /// <summary>
        /// **动画元数据对照**（2026-09-24 立）—— 把一个动作背后的 clip 的**引擎侧元数据**打出来，
        /// 用来和原版对照。典型用法（查"为什么我们的动作在通道 1 不播"）：
        /// <code>
        /// custom.anim_meta act_command_unarmed    ← 原版：通道 1 能播的基准
        /// custom.anim_meta act_magic_idle         ← 我们：通道 1 不播
        /// custom.anim_meta act_fly_hovermove      ← 我们：通道 1 也不播（通道 0 能播）
        /// </code>
        /// 打的都是 `MBActionSet` 上的**公开**静态接口（`IMBAnimation` 那几个 Param1/2/3 是 internal，
        /// mod 程序集够不着 —— 那三个要看就得开 ModKit 的 clip 面板）。
        /// 🔴 重点看 **flags**：`anf_cyclic` 在不在。目前的怀疑 = 导入时被标了 cyclic 的 clip
        ///    在**通道 1（上身层）**上不被引擎驱动（原版 `anim_command_unarmed` 的 flags 里没有 cyclic）。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("anim_meta", "custom")]
        public static string ExecuteAnimMeta(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must be in a scene/mission to use this command.";
            }
            if (args.Count < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                return "usage: custom.anim_meta <actionName>";
            }
            string actionName = args[0].Trim();
            Agent agent = Agent.Main;
            ActionIndexCache idx = ActionIndexCache.Create(actionName);
            if (idx == ActionIndexCache.act_none)
            {
                return $"FAILED: action '{actionName}' is NOT registered (act_none).";
            }
            try
            {
                AnimFlags flags = MBActionSet.GetActionAnimationFlags(agent.ActionSet, idx);
                ulong f = (ulong)flags;
                string line = string.Format(CultureInfo.InvariantCulture,
                    "OK: action='{0}' clip='{1}' duration={2:0.00}s animIdx={3} flags=0x{4:X} "
                    + "[cyclic={5} lowerbody={6} all={7}] contToAction={8} blendOutStart={9:0.00} disp={10}",
                    actionName,
                    MBActionSet.GetActionAnimationName(agent.ActionSet, idx) ?? "?",
                    MBActionSet.GetActionAnimationDuration(agent.ActionSet, idx),
                    MBActionSet.GetAnimationIndexOfAction(agent.ActionSet, idx),
                    f,
                    (flags.HasAnyFlag(AnimFlags.anf_cyclic) ? 1 : 0),
                    (flags.HasAnyFlag(AnimFlags.anf_enforce_lowerbody) ? 1 : 0),
                    (flags.HasAnyFlag(AnimFlags.anf_enforce_all) ? 1 : 0),
                    MBActionSet.GetActionAnimationContinueToAction(agent.ActionSet, ActionIndexValueCache.Create(idx)).Index,
                    MBActionSet.GetActionBlendOutStartProgress(agent.ActionSet, idx),
                    MBActionSet.GetActionDisplacementVector(agent.ActionSet, idx));
                DebugLogger.Log("[Cmd] anim_meta -> " + line);
                return line;
            }
            catch (System.Exception e)
            {
                return "Error: " + e.Message;
            }
        }

        /// <summary>
        /// **按指定通道播动作**（2026-09-24 立）—— `custom.do_anim` 只能播 0 号通道（全身），
        /// 这条专门用来验「**上身叠加**」：通道 1 的动作**不该动腿**。
        ///
        /// 🔴🔴 **flag / prio 这些运行时参数引擎不读**（2026-09-25 实机定案：`lowerbody` / `all` / `prio=60`
        ///    三次全无反应，而同样的 flag 写进 clip 元数据立刻生效）⇒ 本命令的 flag 参数**只当"设动作进通道"用**，
        ///    **别拿它验 flag 语义**（要验/要改 = `tpaccli clipflags` / `clipprio`，见
        ///    [Knowledge/骑砍2动画通道与上下半身分层.md]）。
        ///    传了 flag 时返回里会带一条 warn 提醒。
        ///
        /// 通道的正确用法（结论）：**底层姿势（通道 0）靠 clip 的 `enforce_lowerbody` 保住腿，
        /// 上层动作（通道 1）什么都别勾** —— 详见同一份 Knowledge 文档。
        ///
        /// 用法（首参可弃：不是 0~3 的数字就当成动作名、通道回落 1）：
        ///   custom.anim_ch 1 act_magic_idle 1      → 上身**循环**播蓄力姿势（腿保持原样）
        ///   custom.anim_ch 1 act_magic_projectile  → 上身播一次释放姿势
        ///   custom.anim_ch act_magic_idle          → 同上（通道默认 1）
        ///
        /// 🔴 **返回里的时长就是判据**：时长 `0.00s` = 这个 action 在当前 action_set 里**解析不到**
        ///    （没注册 / clip 名写错 / 模块没加载）—— 所有"播不出来"长得都一样，只有这个数能分辨。
        ///    所以返回里同时给一条**已知能播的参照动作**（`act_fly_hovermove`）的时长做对照：
        ///    参照也是 0.00s ⇒ 是 action_set / 模块层面的问题；只有本条 0.00s ⇒ 是这条 clip 的绑定问题。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("anim_ch", "custom")]
        public static string ExecuteAnimChannel(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must be in a scene/mission to use this command.";
            }
            if (args.Count < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                return "usage: custom.anim_ch [channel] <actionName> [agentId|nearest] [noforce] [prio=NN] [<任意AnimFlags名>…]";
            }

            // 参数扫描（宽松：认不出的一律当动作名，只认第一段）
            int channel = 1;
            string actionName = null;
            Agent agent = Agent.Main;
            string agentNote = string.Empty;
            bool cyclic = false, lowerbody = false, enforceAll = false, ignorePriority = true;
            ulong extraFlags = 0UL;   // 任意 AnimFlags（见下面解析）
            int numericSeen = 0;
            int prioritySet = -1;     // prio=NN 传过才 >= 0（只用于回显）
            foreach (string rawArg in args)
            {
                string a = rawArg != null ? rawArg.Trim() : string.Empty;
                if (a.Length == 0) continue;
                int n;
                if (actionName == null && int.TryParse(a, out n) && n >= 0 && n <= 3 && numericSeen == 0)
                {
                    channel = n; numericSeen = 1; continue;      // 首参数字 = 通道
                }
                string low2 = a.ToLowerInvariant();
                if (low2 == "noforce") { ignorePriority = false; continue; }   // 原版通道 1 的调用风格
                // 🔴 **`prio=NN` = 直接给优先级**（写进 additionalFlags 低字节，引擎 `amf_priority_mask = 0xFF`）。
                //    用途：当场试"谁当主角"。飞行姿势默认 prio=0 ⇒ 会被挥手(2)/挥刀(10~15) 抢走腿；
                //    给它 prio=30 再挥手，腿就该保得住。0~255。
                if (low2.StartsWith("prio="))
                {
                    int pv;
                    if (int.TryParse(a.Substring(5), out pv) && pv >= 0 && pv <= 255)
                    {
                        extraFlags |= (ulong)(uint)pv;
                        prioritySet = pv;
                    }
                    continue;
                }
                // 🔴 **任意 AnimFlags 名字都能直接传**（大小写不敏感，可省 `anf_` 前缀）：认到就 OR 进去。
                //    例：`lowerbody` / `all` / `cyclic` / `affected_by_movement` / `enforce_root_rotation` / `allow_head_movement` …
                //    名字全表 = `TaleWorlds.MountAndBlade.AnimFlags`（反编译 AnimFlags.cs，与编辑器 clip 面板那一栏同源）。
                //    这条是给"通道 1 为什么只动上身"做矩阵测试用的：一次编译、所有 flag 组合当场试。
                AnimFlags parsedFlag;
                string flagName = low2.StartsWith("anf_") ? low2 : "anf_" + low2;
                if (System.Enum.TryParse<AnimFlags>(flagName, true, out parsedFlag) && parsedFlag != 0)
                {
                    extraFlags |= (ulong)parsedFlag;
                    if (parsedFlag == AnimFlags.anf_enforce_lowerbody) { lowerbody = true; }
                    if (parsedFlag == AnimFlags.anf_enforce_all) { enforceAll = true; }
                    continue;
                }
                if (actionName == null) { actionName = a; continue; }
                // 第二段之后的非关键字 = 目标 agent
                if (low2 == "main") { agent = Agent.Main; continue; }
                if (low2 == "nearest")
                {
                    Agent best = null;
                    float bestSq = 400f;   // 20 m 内
                    Vec3 here = Agent.Main.Position;
                    foreach (Agent other in Mission.Current.Agents)
                    {
                        if (other == null || other == Agent.Main || !AgentControlHelper.SafeIsActive(other)) continue;
                        float d = other.Position.DistanceSquared(here);
                        if (d < bestSq) { bestSq = d; best = other; }
                    }
                    if (best != null) { agent = best; }
                    else { agentNote = " [note: no nearby agent -> using main]"; }
                    continue;
                }
                Agent named = Mission.Current.Agents.FirstOrDefault(x => x.Character?.StringId == a);
                if (named != null) { agent = named; }
                else { agentNote = $" [note: no agent '{a}' -> using {agent.Name}]"; }
            }
            if (string.IsNullOrEmpty(actionName))
            {
                return "usage: custom.anim_ch [channel] <actionName> [agentId|nearest] [noforce] [prio=NN] [<任意AnimFlags名>…]";
            }

            ActionIndexCache idx = ActionIndexCache.Create(actionName);
            if (idx == ActionIndexCache.act_none)
            {
                return $"FAILED: action '{actionName}' is NOT registered (act_none). "
                     + "check: action_types.xml declares it + action_sets.xml binds it + module loaded.";
            }

            float duration = 0f, refDuration = 0f;
            try
            {
                duration = MBActionSet.GetActionAnimationDuration(agent.ActionSet, idx);
                ActionIndexCache refIdx = ActionIndexCache.Create("act_fly_hovermove");
                if (refIdx != ActionIndexCache.act_none)
                {
                    refDuration = MBActionSet.GetActionAnimationDuration(agent.ActionSet, refIdx);
                }
            }
            catch (System.Exception) { }

            try
            {
                ulong flags = extraFlags;
                if (cyclic) flags |= (ulong)AnimFlags.anf_cyclic;
                if (lowerbody) flags |= (ulong)AnimFlags.anf_enforce_lowerbody;
                if (enforceAll) flags |= (ulong)AnimFlags.anf_enforce_all;

                // 🔴 **返回值就是判据**：`SetActionChannel` 返回 bool —— false = 这一发**被引擎拒了**
                //    （优先级/通道占用/动作不适用），不是"播了但看不见"。原版欢呼也是这么用的：
                //    `if (!agent.SetActionChannel(1, ...)) { 做别的 }`（Ballista.cs:283）。
                bool accepted = agent.SetActionChannel(channel, idx, ignorePriority: ignorePriority, additionalFlags: flags,
                    blendInPeriod: 0.12f, blendOutPeriodToNoAnim: 0f);

                // 🔴 回读（比 ACCEPTED 更死）：收下之后通道里到底是不是这条动作、权重多少。
                //    weight≈0 = 引擎收下了但**没让它上屏**（被规则/别的系统压住）；匹配=False = 立刻被换掉。
                string back = string.Empty;
                try
                {
                    ActionIndexCache now = agent.GetCurrentAction(channel);
                    float weight = agent.GetActionChannelWeight(channel);
                    float prog = agent.GetCurrentActionProgress(channel);
                    back = $" after[matches={(now.Index == idx.Index ? 1 : 0)} setIdx={idx.Index} w={weight:0.00} prog={prog:0.00}]";
                }
                catch (System.Exception) { }
                back += "  " + Channels(agent);   // 🔴 两条通道的现状（判"通道 0 是被清空了还是只是没权重"）

                string durNote = duration > 0f ? string.Empty
                    : " [note: duration 0.00s -> this action does not resolve in the agent's current action_set]";
                string line = $"OK: agent={agent.Name} channel={channel} action='{actionName}' duration={duration:0.00}s"
                     + $" ref(act_fly_hovermove)={refDuration:0.00}s loop={(cyclic ? 1 : 0)}"
                     + $" lowerbody={(lowerbody ? 1 : 0)} all={(enforceAll ? 1 : 0)} force={(ignorePriority ? 1 : 0)}"
                     + (prioritySet >= 0 ? $" prio={prioritySet}" : "")
                     // 🔴 自我提醒（2026-09-25 实测）：flags / prio 这两个**运行时参数引擎不读** ——
                     //    `lowerbody` / `all` / `prio=60` 三次实机零反应；而写进 clip 元数据立刻生效。
                     //    留着参数只为"设动作进通道"，别再拿它验 flag 语义（要验用 `tpaccli clipflags`）。
                     + (extraFlags != 0UL
                        ? "  [warn: additionalFlags(flags/prio) are IGNORED by the engine - write flags into clip metadata instead (tpaccli clipflags)]"
                        : "")
                     + $" ACCEPTED={accepted}{back}{durNote}{agentNote}";
                DebugLogger.Log("[Cmd] anim_ch -> " + line);   // 进日志 ⇒ 控制台窗口窄看不全也不怕
                return line;
            }
            catch (System.Exception e)
            {
                return "Error: " + e.Message;
            }
        }

        /// <summary>
        /// **只读探针：两条动画通道各自在演什么**（2026-09-25 立）。
        ///
        /// 为什么需要：飞行中往通道 1 放挥手动作，腿会被它接管；而通道 0 写回去又"没反应"。
        /// 这两种情况**在画面上长得一模一样**，但引擎状态完全不同：
        ///   · 通道 0 被清成 act_none（= 没人驱动腿，是谁抢了腿另说）
        ///   · 通道 0 还挂着飞行姿势、只是 weight=0（= 引擎按层叠把腿让给了通道 1）
        /// 数字一出来就知道是哪种，不用再猜。
        ///
        /// 用法：`custom.anim_state [agentId]`（首参可弃，解析不出就回落主角）
        /// 返回：`ch0['动作名' idx=… w=通道权重 curW=当前动作权重 prog=进度 prio=优先级]  ch1[…]`
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("anim_state", "custom")]
        public static string ExecuteAnimState(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must be in a scene/mission to use this command.";
            }
            Agent agent = Agent.Main;
            string note = string.Empty;
            if (args.Count >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                Agent named = Mission.Current.Agents.FirstOrDefault(x => x.Character?.StringId == args[0]);
                if (named != null) { agent = named; }
                else { note = " [note: '" + args[0] + "' is not an agent id -> using main hero]"; }
            }

            string line = $"state: agent={agent.Name} isPlayer={ (agent == Agent.Main ? 1 : 0) }"
                        + $" aiming={(SpellCastInput.IsPlayerAiming ? 1 : 0)}  "
                        + Channels(agent) + note;
            DebugLogger.Log("[Cmd] anim_state -> " + line);
            return line;
        }

        /// <summary>
        /// 只读：把 0 / 1 两条动画通道的现状拼成一行（动作名 · 索引 · 通道权重 · 当前动作权重 · 进度 · 优先级）。
        /// 判据：`idx` 与刚才 `custom.anim_ch` 回显的 `setIdx` 一致 = 这条动作还在通道里。
        /// </summary>
        private static string Channels(Agent a)
        {
            if (a == null) { return "(no agent)"; }
            var sb = new System.Text.StringBuilder();
            for (int ch = 0; ch <= 1; ch++)
            {
                try
                {
                    ActionIndexCache cur = a.GetCurrentAction(ch);
                    sb.Append($"ch{ch}['{V.ActName(a, ch)}' idx={cur.Index} w={a.GetActionChannelWeight(ch):0.00}"
                              + $" curW={a.GetActionChannelCurrentActionWeight(ch):0.00}"
                              + $" prog={a.GetCurrentActionProgress(ch):0.00}"
                              + $" prio={a.GetCurrentActionPriority(ch)}]");
                }
                catch (System.Exception e) { sb.Append($"ch{ch}[err:{e.Message}]"); }
                if (ch == 0) { sb.Append("  "); }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 处决配对测试台（T17 带位移动画的实机检查）：把 interact 焦点上的 NPC 拉到玩家**正前方** [distance] 米、
        /// 让他**面朝玩家**，然后**同一帧**起播两条配对动画 —— 玩家播 `act_execution02`、他播 `act_executed02`。
        /// 两条都带位移（玩家前冲 3.68 m、受击方被推 1.2 m），所以必须同时起播才对得上。
        ///
        /// 用法（首参可弃：随手填个占位也能跑）：
        ///   custom.exec_pair                  → 目标 = interact 焦点（准星盯着的人），距离 2 m，演出相机 = sp_lordshall
        ///   custom.exec_pair 1                → '1' 解析不出 agent → 仍是 interact 焦点（返回里注明）
        ///   custom.exec_pair lord_1_1         → 按 Character.StringId 指定受击方
        ///   custom.exec_pair lord_1_1 2.5     → 再指定距离（钳在 0.5 ~ 5 m）
        ///   custom.exec_pair 1 2 none         → 第 3 参 = 相机模式：`engine`（默认，照抄开演那一刻的引擎机位）/
        ///                                       `none`（不接管）/ 其它 = Camera.csv 的模板名（如 sp_lordshall）
        /// 演出期间相机由 `SpringArmCameraView` 的跟随模式接管（每帧跟位置，见该文件"跟随模式"段）。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("exec_pair", "custom")]
        public static string ExecuteExecPair(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must be in a scene/mission to use this command.";
            }

            const string AttackerAction = "act_execution02";   // 玩家（攻击方）
            const string VictimAction = "act_executed02";      // 焦点 NPC（受击方）

            // 1. 受击方：可弃首参（解析不出就回落 interact 焦点，不报 not found）
            Agent victim = null;
            string note = string.Empty;
            if (args.Count >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                victim = Mission.Current.Agents.FirstOrDefault(a => a.Character?.StringId == args[0]);
                if (victim == null)
                    note = $" [note: no agent with id '{args[0]}' -> using interact focus]";
            }
            if (victim == null)
            {
                var view = InteractionMissionView.Instance;
                victim = view?.GetFocusdAgent() ?? view?.LastFocusedAgent;
            }

            if (victim == null)
                return "FAILED: no focused agent (aim at someone within interact range, or pass an agent id)" + note;
            if (victim == Agent.Main)
                return "FAILED: focused agent is the player himself";

            Agent attacker = Agent.Main;
            if (!AgentControlHelper.SafeIsActive(attacker) || !AgentControlHelper.SafeIsActive(victim))
                return "FAILED: attacker or victim is not active (dead / removed)";

            // 2. 距离（第 2 参，默认 2 m）；相机模式在第 3 参（见下方第 7 步）
            float distance = 2.0f;
            if (args.Count >= 2 && !string.IsNullOrWhiteSpace(args[1])
                && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                distance = MathF.Clamp(parsed, 0.5f, 5f);
            }

            // 3. 动作名先解析：解析失败 = act_none，引擎不报错、只是不播（与 custom.do_anim 同款检查）
            ActionIndexCache atkIndex = ActionIndexCache.Create(AttackerAction);
            ActionIndexCache vicIndex = ActionIndexCache.Create(VictimAction);
            if (atkIndex == ActionIndexCache.act_none)
                return $"FAILED: action '{AttackerAction}' is NOT registered (act_none). check Taikou action_types.xml / action_sets.xml";
            if (vicIndex == ActionIndexCache.act_none)
                return $"FAILED: action '{VictimAction}' is NOT registered (act_none). check Taikou action_types.xml / action_sets.xml";

            // 4. 站位：玩家正前方 distance 米、贴地。
            //    🔴 用【角色朝向】LookFrame.rotation.f，不是相机方向 —— 处决的位移是沿角色前方推的，
            //       按相机放会把目标放到"镜头看着的位置"，跟动画行进方向对不上。
            Vec3 forward = attacker.LookFrame.rotation.f;
            forward.z = 0f;
            if (forward.LengthSquared < 1e-4f)
            {
                forward = attacker.LookDirection;   // 兜底：角色帧取不到时
                forward.z = 0f;
            }
            if (forward.LengthSquared < 1e-4f)
                return "FAILED: cannot read player facing direction";
            forward.Normalize();

            Vec3 spot = attacker.Position + forward * distance;
            if (Mission.Current.Scene != null)
                spot.z = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(spot.x, spot.y, spot.z));

            // 5. 放下 + 让他面朝玩家。
            //    🔴 转朝向只能用 SetMovementDirection（LookDirection 赋值那条实测转不动）。
            victim.TeleportToPosition(spot);
            victim.SetMovementDirection(-1f * forward.AsVec2);

            // 6. 同帧起播（这就是"同时"）：
            //    玩家走 0 号通道，与 custom.do_anim 同一条调用（实测这条 clip 能推着玩家走 3.68 m）；
            //    受击方走 ForcePlayAction —— 它负责切到 as_human_warrior（村民/平民的 action_set 里没有我们的动作）
            //    并打断坐椅子之类的交互，否则引擎每帧会把动画覆盖回去。
            float atkDur = MBActionSet.GetActionAnimationDuration(attacker.ActionSet, atkIndex);
            attacker.SetActionChannel(0, atkIndex, false, 0UL, 0f, 1f, -0.2f, 0.4f, 0f, false, -0.2f, 0, true);
            AgentControlHelper.ForcePlayAction(victim, VictimAction);
            float vicDur = MBActionSet.GetActionAnimationDuration(victim.ActionSet, vicIndex);

            // 7. 演出相机：整段表演（含收尾一拍）把镜头接管过来跟着玩家走。
            //    为什么要"跟"：这条动画会把玩家往前推 3.68 m，镜头不跟人就走出画面。
            //    默认 = **照抄引擎相机开演那一刻的机位**（方向冻住、只跟位置）——
            //    观感是"视角还是你原来的视角，只是跟着人平移"，见 Camera/SpringArmCameraView.cs「跟随模式」段。
            string camMode = (args.Count >= 3 && !string.IsNullOrWhiteSpace(args[2])) ? args[2] : "engine";
            string camNote = " [camera: engine default]";
            if (camMode.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                camNote = " [camera: none]";
            }
            else
            {
                float hold = MathF.Max(atkDur, vicDur) + 1.0f;
                bool camOk;
                if (camMode.Equals("engine", StringComparison.OrdinalIgnoreCase))
                {
                    camOk = SpringArmCameraView.ApplyFollowFromEngineCamera(attacker, hold);
                    camNote = camOk
                        ? $" [camera: engine pose held, following you {hold:0.0}s]"
                        : " [camera FAILED: engine camera pose unavailable -> engine camera kept]";
                }
                else
                {
                    camOk = SpringArmCameraView.ApplyFollowTemplate(camMode, attacker, hold);
                    camNote = camOk
                        ? $" [camera: template '{camMode}' following you {hold:0.0}s]"
                        : $" [camera FAILED: template '{camMode}' not in Camera.csv -> engine camera kept]";
                }
            }

            string durNote = (atkDur > 0f && vicDur > 0f)
                ? string.Empty
                : " [note: duration 0.00s -> the clip name in action_sets.xml may not resolve]";

            return $"OK: {attacker.Name} plays '{AttackerAction}' ({atkDur:0.00}s) + {victim.Name} plays '{VictimAction}' ({vicDur:0.00}s)"
                 + $", victim placed {distance:0.0}m in front of you, facing you{note}{durNote}{camNote}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("print_npcs", "custom")]
        public static string ExecutePrintNpc(List<string> args)
        {
            if (Mission.Current == null) return "Please Enter the mission First.";
            int radius = 0;
            if (args.Count > 0)
            {
                string radiusStr = args[0];
                radius = int.Parse(radiusStr);
            }

            MBList<Agent> nearbyAgents = new MBList<Agent>();
            if (radius > 0)
            {
                Mission.Current.GetNearbyAgents(Agent.Main.Position.AsVec2, radius, nearbyAgents);
            }

            // ── 确定迭代源 ──
            var source = radius > 0
                ? nearbyAgents
                : new MBList<Agent>(Mission.Current.Agents);

            StringBuilder sb = new StringBuilder();
            int humanCount = 0;
            int animalCount = 0;
            int mountCount = 0;

            sb.AppendLine($"\n=== Agent Report ({source.Count} active, {Mission.Current.AllAgents.Count} total) ===");
            sb.AppendLine();

            foreach (Agent agent in source)
            {
                if (agent == null) continue;

                if (agent.IsHuman)
                {
                    humanCount++;
                    string name = agent.Name;
                    string id = agent.Character?.StringId ?? "?";

                    if (string.IsNullOrWhiteSpace(name) && agent.Character != null)
                        name = agent.Character.Name?.ToString() ?? "?";

                    if (string.IsNullOrWhiteSpace(name))
                        name = "(unnamed)";

                    sb.Append($"[H] {name}:{id}");
                }
                else
                {
                    string monster = agent.Monster?.StringId ?? "?";
                    string name = agent.Name;
                    string id = agent.Character?.StringId ?? "-";

                    if (string.IsNullOrWhiteSpace(name))
                        name = id != "-" ? id : $"(animal_{monster})";

                    if (monster.Contains("horse") || monster.Contains("camel") || monster.Contains("mule"))
                    {
                        mountCount++;
                        sb.Append($"[Mount] {name} monster={monster}");
                    }
                    else
                    {
                        animalCount++;
                        sb.Append($"[Animal] {name} monster={monster}");
                    }
                }

                sb.Append(" | ");
                if ((humanCount + animalCount + mountCount) % 3 == 0)
                    sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("--- Summary ---");
            sb.AppendLine($"Humans: {humanCount}");
            sb.AppendLine($"Animals: {animalCount}");
            sb.AppendLine($"Mounts: {mountCount}");

            if (animalCount == 0 && mountCount == 0)
                sb.AppendLine("(No animal/mount agents found)");

            string result = sb.ToString();
            DebugLogger.Log(result);
            return result;
        }


        /// <summary>
        /// 打印当前 Mission 的 Mode、SceneName、Settlement 及玩法级竞技场标志。
        /// 用法: custom.print_mission_mode
        /// 输出会同时写入 Debug/StoryEngine_RuntimeLog.txt
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("print_mission_mode", "custom")]
        public static string PrintMissionMode(List<string> args)
        {
            if (Mission.Current == null)
            {
                return "error: not in mission";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[Interaction] Mission mode: {Mission.Current.Mode}");
            sb.AppendLine($"[Interaction] SceneName: {Mission.Current.SceneName ?? "(null)"}");
            sb.AppendLine($"[Interaction] Settlement: {Settlement.CurrentSettlement?.StringId ?? "(null)"}");
#if !MB2_V1212
            // 玩法级竞技场标志（SandBox MissionLogic，场景加载即挂载，比 Mode 可靠）
            sb.AppendLine($"[Interaction] ArenaPracticeFight: {Mission.Current.HasMissionBehavior<SandBox.Missions.MissionLogics.Arena.ArenaPracticeFightMissionController>()}");
            sb.AppendLine($"[Interaction] Tournament: {Mission.Current.HasMissionBehavior<SandBox.Tournaments.MissionLogics.TournamentBehavior>()}");
            sb.AppendLine($"[Interaction] ArenaDuel: {Mission.Current.HasMissionBehavior<SandBox.Missions.MissionLogics.Arena.ArenaDuelMissionController>()}");
#endif
            string msg = sb.ToString();
            DebugLogger.Log(msg);
            return msg;
        }

        /// <summary>
        /// 打印当前 TopScreen 的类型名与调试信息（排查 UI 层归属/呼出按钮显隐用）。
        /// 用法: custom.print_topscreen
        /// 输出会同时写入 Debug/StoryEngine_RuntimeLog.txt
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("print_topscreen", "custom")]
        public static string PrintTopScreen(List<string> args)
        {
            var top = ScreenManager.TopScreen;
            string name = top?.GetType().Name ?? "null";
            bool active = false;
            try { active = top != null && top.IsActive; } catch { }
            int layers = top?.Layers?.Count ?? -1;
            string mission = Mission.Current?.Mode.ToString() ?? "null";
            string msg = $"[TopScreen] Name={name} Active={active} Layers={layers} FullScreenUI={UiFullScreenHelper.IsFullScreenUiOpen()} Mission={mission}";
            DebugLogger.Log(msg);
            return msg;
        }
        public static string ExecuteTeleportToNpc(List<string> args)
        {
            if (Mission.Current == null) return "Please Enter the mission First.";

            Agent.Main.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            return "success";
        }


      


        [CommandLineFunctionality.CommandLineArgumentFunction("look", "custom")]
        public static string ExecuteLook(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: enter a mission first.";
            }

            if (args.Count == 0)
            {
                return "usage: mount, npc [name], camera, or reset";
            }

            string targetType = args[0].ToLower();

            // --- 1. 看向坐骑 ---
            if (targetType == "mount")
            {
                if (Agent.Main.MountAgent != null)
                {
                    // 让主角的头锁定坐骑
                    Agent.Main.SetLookAgent(Agent.Main.MountAgent);
                    return "performed: gazing affectionately at your horse.";
                }
                return "error: you are not mounted, or your horse is not nearby.";
            }

            // --- 2. 看向特定 NPC ---
            else if (targetType == "npc")
            {
                if (args.Count < 2) return "error: enter part of an NPC name. e.g. custom.look npc lorden";

                string searchName = args[1];

                // 在当前战场的所有单位中查找名字匹配的人（排除自己）
                Agent targetAgent = Mission.Current.Agents
                    .FirstOrDefault(a => a != Agent.Main && a.Name.Contains(searchName) && AgentControlHelper.SafeIsActive(a));

                if (targetAgent != null)
                {
                    Agent.Main.SetLookAgent(targetAgent);
                    return $"performed: gazing at NPC '{targetAgent.Name}'.";
                }
                else
                {
                    return $"no NPC found matching '{searchName}'.";
                }
            }

            // --- 3. 看向镜头 (自拍/说话视角) ---
            else if (targetType == "camera")
            {
                // 获取当前摄像机的位置
                // 注意：这里我们只取一次位置，如果移动镜头，角色会盯着刚才那个点
                // 想要实时盯着镜头比较复杂，需要每帧更新，这里做静态摆拍
                if (true)
                {
                    Vec3 cameraPos = Mission.Current.GetCameraFrame().origin;

                    Agent.Main.SetLookToPointOfInterest(cameraPos);
                    return "performed: gazing at the camera (audience).";
                }
            }

            // --- 4. 重置视线 ---
            else if (targetType == "reset")
            {
                // 清除锁定，恢复鼠标控制视线
                Agent.Main.ResetLookAgent();
                return "performed: look reset, free control restored.";
            }

            return "unknown command. usage: mount, npc [name], camera, reset";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("cam_face", "custom")]
        public static string ExecuteCamFace(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null) return "error:no mission";

            // 【关键修正】: 不通过 Behavior 获取，而是直接通过 ScreenManager 获取当前屏幕
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;

            if (missionScreen == null) return "error: no mission screen";

            // 1. 获取主角眼睛的位置
            // 1. 确定目标点（玩家的眼睛）
            Vec3 targetPos = Agent.Main.LookFrame.origin;

            // 2. 确定摄像机的位置（玩家正前方 2.5 米，高度稍微抬高一点）
            // Agent.Main.LookDirection 是玩家脸朝向的方向
            Vec3 forwardDir = Agent.Main.LookDirection;
            forwardDir.Normalize(); // 标准化向量，确保长度为1

            // 位置计算：玩家位置 + (朝向向量 * 距离)
            Vec3 cameraPos = targetPos + (forwardDir * 2.5f);

            // 稍微把摄像机抬高一点 (0.5米)，形成一点点俯视感，这样更有电影感
            cameraPos.z += 0.5f;

            // 3. 【核心修正】手动计算“从摄像机看向玩家”的方向向量
            // 向量减法：终点 - 起点 = 指向终点的向量
            Vec3 directionFromCamToPlayer = targetPos - cameraPos;
            directionFromCamToPlayer.Normalize();

            // 4. 【核心修正】强制构建旋转矩阵
            // Mat3.CreateMat3WithForward(前向向量, 上方向量)
            // 我们明确告诉引擎：摄像机的“正前方(Y轴)”必须等于 directionFromCamToPlayer
            Mat3 rotation = Mat3.CreateMat3WithForward(in directionFromCamToPlayer);

            // 5. 组合成最终的坐标帧 (MatrixFrame = 旋转 + 位置)
            MatrixFrame camFrame = new MatrixFrame(rotation, cameraPos);

            // 6. 创建相机并赋值
            Camera customCam = Camera.CreateCamera();
            customCam.Frame = camFrame;
            missionScreen.CustomCamera = customCam;

            return "Camera see you now.";
        }

        // 恢复正常镜头
        // 命令: custom.cam_reset
        [CommandLineFunctionality.CommandLineArgumentFunction("cam_reset", "custom")]
        public static string ExecuteCamReset(List<string> args)
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (missionScreen != null)
            {
                // 设为 null 就会自动切回游戏默认视角
                missionScreen.CustomCamera = null;
                return "screen reset success。";
            }
            return "error:no screen";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("print_npc_move_info", "custom")]
        public static string ExecutePrintMoveInfo(List<string> args)
        {
            if(args.Count == 0)
            {
                return "error:no npc";
            }
            string stringId = args[0];
            //获取当前场景的id为stringId的agent

            Agent agent = null;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a.IsHuman && a.Character.StringId == stringId)
                    { agent = a; break; }
            }
            if (agent == null)
            {
                return $"error:no agent id = {stringId}";
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- 诊断 Agent: {agent.Name} ---");

            // 1. 基础控制状态
            sb.AppendLine($"IsActive: {AgentControlHelper.SafeIsActive(agent)}");
            sb.AppendLine($"Controller: {agent.Controller}"); // 应该是 AI
            sb.AppendLine($"State: {agent.State}"); // 应该是 Active

            // 2. 关键：是否正在使用物体（这是店主不动的最大嫌疑）
            sb.AppendLine($"IsUsingGameObject: {agent.IsUsingGameObject}");
            if (agent.IsUsingGameObject)
            {
                sb.AppendLine($"TargetObject: {agent.CurrentlyUsedGameObject}");
            }

            // 3. 关键：AI 脚本标志位（检查是否有 DisableMove 之类的标志）
            sb.AppendLine($"ScriptedFlags: {agent.GetScriptedFlags()}");

            // 4. 关键：当前的动作动画（检查是否卡在特殊动画里，如站岗、擦桌子）
            // 通道 0 是基础动作（站立/走/跑），通道 1 是上半身动作
            var action0 = agent.GetCurrentAction(0);
            var action1 = agent.GetCurrentAction(1);
            sb.AppendLine($"Action Ch0: {action0}");
            sb.AppendLine($"Action Ch1: {action1}");

            // 5. 移动能力检查
            sb.AppendLine($"MovementLockedState: {agent.MovementLockedState}"); // 这是一个属性，如果为 false，肯定动不了
            sb.AppendLine($"MovementFlags: {agent.MovementFlags}");

           

            return sb.ToString();
        }





        [CommandLineFunctionality.CommandLineArgumentFunction("playsound", "custom")]
        public static string ExecutePlaySound(List<string> args)
        {
            if (PsaiCore.Instance != null)
            {
                PsaiCore.Instance.StopMusic(true, 2.0f);
            }
            //SoundManager.SetListenerFrame(listenderFrame);
            string soundStr = "14_HYOUJOU";
            int soundIndex = SoundEvent.GetEventIdFromString(soundStr);
            SoundEvent sound = SoundEvent.CreateEvent(soundIndex, Mission.Current.Scene);
            sound.Play();
            //var result =PsaiCore.Instance.TriggerMusicTheme(1001, 10);

            return "psaicore success";


        }

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("check_agent_equip", "custom")]
        public static string CheckAgentEquip(List<string> args)
        {
            if (TaleWorlds.MountAndBlade.Mission.Current == null)
                return "Error: Mission is not running.";

            if (args.Count == 0)
                return "Usage: custom.check_agent_equip [AgentName]";

            string targetId = args[0];

            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.Character != null && agent.Character.StringId == targetId)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine($"--- Checking Agent: {agent.Name} ---");
                    sb.AppendLine($"State: {agent.State}, AI State Flag: {agent.AIStateFlags}");
                    // 检查 4 个武器槽位
                    for (int i = 0; i < 4; i++)
                    {
                        var eqIndex = (TaleWorlds.Core.EquipmentIndex)i;
                        var weapon = agent.Equipment[eqIndex];

                        if (!weapon.IsEmpty)
                        {
                            sb.AppendLine($"[Slot {i}] {weapon.Item.StringId} ({weapon.Item.Name})");
                            sb.AppendLine($"    - Type: {weapon.Item.PrimaryWeapon?.WeaponClass}");
                            sb.AppendLine($"    - Ammo Count: {weapon.Amount}/{weapon.ModifiedMaxAmount}"); // 关键检查点
                            sb.AppendLine($"      -> Weapon Class: {weapon.Item.PrimaryWeapon.WeaponClass}");
                            sb.AppendLine($"    - Requires Ammo Class: {weapon.Item.PrimaryWeapon.AmmoClass}");

                        }
                        else
                        {
                            sb.AppendLine($"[Slot {i}] Empty");
                        }
                    }
                    // 检查手里真正拿着啥
                    var wieldedMain = V.MainWpn(agent);
                    sb.AppendLine($"\nCurrently Wielding MainHand Index: {wieldedMain}");
                    return sb.ToString();
                }
            }
            return "Agent not found.";

        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("current_location", "custom")]
        public static string ListCurrentLocations(List<string> args)
        {
            // 1. 安全检查
            if (Campaign.Current == null || Hero.MainHero == null)
            {
                return "Error: Campaign or MainHero is null.";
            }

            if (Hero.MainHero.CurrentSettlement == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：LWN_cmd_not_in_settlement（玩家可见文本）
                    LWNTextHelper.ResolveText("LWN_cmd_not_in_settlement", "Error: you are not at a settlement."), Colors.Red));
                return "Error: Not in a settlement.";
            }

            Settlement settlement = Hero.MainHero.CurrentSettlement;
            LocationComplex complex = LocationComplex.Current;

            if (complex == null)
            {
                return "Error: LocationComplex is null.";
            }

            // 2. 准备输出内容
            // sbDisplay 用于左下角中文显示
            StringBuilder sbDisplay = new StringBuilder();
            sbDisplay.AppendLine($"\n=== [{settlement.Name}] 可用场景 ID ===");

            // sbConsole 用于控制台英文返回
            StringBuilder sbConsole = new StringBuilder();
            sbConsole.AppendLine($"\n=== Location List for {settlement.Name} ===");

            // 3. 遍历所有地点
            foreach (Location loc in complex.GetListOfLocations())
            {
                // loc.StringId 就是你在代码里需要用的 ID (例如 "lordshall")
                string id = loc.StringId;

                // 获取场景文件名 (0, 1, 2, 3 代表不同等级，通常取 1 或当前等级)
                string sceneName = loc.GetSceneName(settlement.Town != null ? settlement.Town.GetWallLevel() : 1);

                string infoLine = $"ID: [{id}]  --->  Scene: {sceneName}";

                sbDisplay.AppendLine(infoLine);
                sbConsole.AppendLine(infoLine);
            }

            sbDisplay.AppendLine("=================================");

            // 4. 分别输出
            // 中文输出到屏幕左下角
            InformationManager.DisplayMessage(new InformationMessage(sbDisplay.ToString(), Colors.Green));

            // 英文输出到控制台窗口
            return sbConsole.ToString() + "\nDone. Check the game log for details.";
        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("enter_lordshall", "custom")]
        public static string ExecuteEnterLordsHall(List<string> args)
        {
            // 获取当前定居点的 LocationComplex
            LocationComplex locationComplex = LocationComplex.Current;
            if (locationComplex != null)
            {
                // "lordshall" 是骑砍2中城主大厅的标准 ID
                Location lordsHall = locationComplex.GetLocationWithId("lordshall");
                Campaign.Current.GameMenuManager.NextLocation = lordsHall;
                Campaign.Current.GameMenuManager.PreviousLocation = locationComplex.GetLocationWithId("center");
                Mission.Current.EndMission();
                // 这会触发加载画面，并自动结束当前的 Mission
                ///      Campaign.Current.GameMenuManager.SetNextMenu("town_keep");
                return "success";

            }
            return "failure";
        }

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("find_chairs", "custom")]
        public static string FindAllChairs(List<string> strings)
        {
            if (Mission.Current == null)
                return "Error: You must be in a mission to use this command.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n=== Searching for Chairs (Sittable Objects) ===");

            int count = 0;

            // iterate through interactive objects only (MissionObjects)
            // This avoids printing static meshes like walls or ground.
            foreach (MissionObject obj in Mission.Current.MissionObjects)
            {
                // Check if the object is actually a Chair
                if (obj is Chair)
                {
                    count++;
#if !MB2_V1212
                    WeakGameEntity wge = obj.GameEntity;
                    Vec3 pos = wge.IsValid ? new Vec3() : Vec3.Zero;
#else
                    GameEntity entity = obj.GameEntity;
                    Vec3 pos = entity.GlobalPosition;
#endif
                    var tags = new List<string>();


                    sb.AppendLine($"[Found Chair #{count}]");
                    sb.AppendLine($"  Name: {"(LATEST)"}");
                    sb.Append("  Tags: ");
                    foreach (var tag in tags)
                    {
                        sb.Append($" {tag} ");
                    }

                    sb.Append("\n"); // Important: Use these tags to find it in code!
                    sb.AppendLine($"  Pos : {pos.x:F2}, {pos.y:F2}, {pos.z:F2}");
                    sb.AppendLine("--------------------------------");
                }
            }

            if (count == 0)
            {
                return "Result: No chairs found in this scene.";
            }

            // Output to game log (left bottom) as well, but keep it English as requested
            string resultMsg = $"Result: Found {count} chairs. Check console for details.";
            InformationManager.DisplayMessage(new InformationMessage(resultMsg, Colors.Green));

            return sb.ToString();
        }
        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("set_npc_to_hero", "custom")]
        public static string ExecuteSetNpcToHero(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";
            if (args.Count < 1) return "Usage: custom.set_npc_to_hero [HeroStringId]";

            string targetStringId = args[0];
            Hero targetHero = Campaign.Current.CampaignObjectManager.Find<Hero>(targetStringId);

            if (targetHero == null) return $"Error: Hero '{targetStringId}' not found.";
            if (!targetHero.IsAlive) return "Error: Hero is dead.";
            if (targetHero == Hero.MainHero) return "Error: Already this hero.";

            // =================================================================
            // 步骤 1：确保目标有队伍 (你的核心诉求)
            // =================================================================
            // 如果他在城里或者流浪，没有 MobileParty，夺舍后会变成幽灵。
            // 这里我们用 MobilePartyHelper 强行给他造一个队。
            if (targetHero.PartyBelongedTo == null)
            {
                // 注意：这里调用你提到的 MobilePartyHelper
                // 确保该函数可用，且参数正确
                MobilePartyHelper.SpawnLordParty(targetHero, targetHero.HomeSettlement ?? Settlement.All.FirstOrDefault());
            }

            // =================================================================
            // 步骤 2：政治身份修正 (防止 Kingdom 界面崩溃)
            // =================================================================
            // 原版逻辑规定：只有家族族长能打开王国界面。
            if (targetHero.Clan != null && targetHero.Clan.Leader != targetHero)
            {
                targetHero.Clan.SetLeader(targetHero);
            }

            // =================================================================
            // 步骤 3：官方夺舍 (参考 StartAsAnyone)
            // =================================================================
            // 这会自动切换控制权、UI、主队伍指针
            ChangePlayerCharacterAction.Apply(targetHero);

            // =================================================================
            // 步骤 4：后勤补给 (完全复刻 StartAsAnyone 的逻辑)
            // =================================================================
            // 夺舍后，现在的 MobileParty.MainParty 就是织田信长的队伍了
            MobileParty mainParty = MobileParty.MainParty;

            if (mainParty != null)
            {
                // 4.1 刷新相机
                MapState mapState = GameStateManager.Current.ActiveState as MapState;
                if (mapState != null)
                {
                    mapState.Handler.ResetCamera(true, true);
                    mapState.Handler.TeleportCameraToMainParty();
                }

                // 4.2 发放口粮 (参考代码逻辑：根据部队阶级发粮食，防止饿死)
                ItemObject grain = DefaultItems.Grain;
                ItemObject meat = DefaultItems.Meat;

                // 清空旧杂物 (可选，StartAsAnyone 做了这步)
                // mainParty.ItemRoster.Clear(); 

                foreach (var element in mainParty.MemberRoster.GetTroopRoster())
                {
                    int number = element.Number;
                    // StartAsAnyone 的算法：部队等级越高，发的粮食越多
                    int grainCount = (int)Math.Sqrt((double)element.Character.Tier) * (number / 2);
                    int meatCount = number / 3;

                    if (grainCount > 0) mainParty.ItemRoster.AddToCounts(grain, grainCount);
                    if (meatCount > 0) mainParty.ItemRoster.AddToCounts(meat, meatCount);
                }

                // 简单补底：如果算出来还是没吃的，强制给100个面包
                if (mainParty.ItemRoster.TotalFood == 0)
                {
                    mainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 100);
                }
            }

            return $"Success: You are now {targetHero.Name}!";
        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("teleport", "custom")]
        public static string TeleportAgent(List<string> args)
        {
            if (Mission.Current == null || Mission.Current.MainAgent == null)
                return "Error: Mission or MainAgent is null.";

            if (args.Count < 4)
            {
                return "Error: Usage format is 'drama.teleport x y z id' (4 arguments required).";
            }

            try
            {
                // Parse arguments
                float x = float.Parse(args[0]);
                float y = float.Parse(args[1]);
                float z = float.Parse(args[2]);
                string agentId = args[3];

                Agent teleAgent = Mission.Current.MainAgent;
                if (agentId != "player")
                    teleAgent = Mission.Current.Agents.FirstOrDefault(a => a.Character.StringId == agentId);
                if (teleAgent == null) { teleAgent = Mission.Current.MainAgent; }

                // Create target vector
                Vec3 targetPos = new Vec3(x, y, z);

                // Teleport
                teleAgent.TeleportToPosition(targetPos);

                return $"Success:{teleAgent.Name} Teleported to {x:F2}, {y:F2}, {z:F2}";
            }
            catch (FormatException)
            {
                return "Error: Invalid number format. Please use numbers (e.g., 100.5).";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("print_pos_dir", "custom")]
        public static string ExecutePrintPosDir(List<string> strings)
        {
            //参数一是stringId，没有的话默认用玩家 Pos是vec3，dir是vec2
            // 1. 检查是否在场景（Mission）中
            if (Mission.Current == null)
            {
                return "Error: You must be in a Mission (Scene) to use this command.";
            }

            Agent targetAgent = null;

            // 2. 确定目标 Agent
            if (strings == null || strings.Count == 0)
            {
                // --- 情况 A: 没有参数，默认获取玩家 ---
                targetAgent = Mission.Current.MainAgent;
                if (targetAgent == null)
                {
                    // 有时候玩家可能是自由摄像机模式，尝试获取由玩家控制的 Agent
                    if (Agent.Main != null) targetAgent = Agent.Main;
                    else return "Error: Player Agent not found (Are you in free camera mode?).";
                }
            }
            else
            {
                // --- 情况 B: 有参数，根据 StringId 查找 NPC ---
                string targetId = strings[0];

                // 遍历场景中所有 Agent 寻找匹配的 StringId
                foreach (Agent agent in Mission.Current.Agents)
                {
                    // 必须是人类，且拥有 Character 对象
                    if (agent.IsHuman && agent.Character != null)
                    {
                        // 比较 StringId (Hero 的 ID 或者兵种 ID)
                        if (agent.Character.StringId == targetId)
                        {
                            targetAgent = agent;
                            break;
                        }
                    }
                }

                if (targetAgent == null)
                {
                    targetAgent = Agent.Main;
                }
            }

            // 3. 获取坐标和朝向
            Vec3 pos = targetAgent.Position;

            // 获取面朝方向 (LookDirection 是 Vec3，转为 Vec2 通常用于 SetMovePos 或 Teleport)
            Vec2 dir = targetAgent.GetMovementDirection();
            dir.Normalize(); // 归一化，保证向量长度为 1

            // 4. 格式化输出 (保留3位小数，直接生成可用的 C# 代码字符串)
            string posStr = $"new Vec3({pos.x:F3}f, {pos.y:F3}f, {pos.z:F3}f)";
            string dirStr = $"new Vec2({dir.x:F3}f, {dir.y:F3}f)";

            // 5. 返回结果
            return $"\n--- Agent Data: {targetAgent.Name} ---\n" +
                   $"Position Code: {posStr}\n" +
                   $"Direction Code: {dirStr}\n" +
                   $"Raw Pos: {pos}\n" +
                   $"Raw Dir: {dir}\n" +
                   $"Location: {VisualCommands.GetCurrentLocationId()}\n" +
                   $"-----------------------------";
        }










        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("print_agent_anims", "custom")]
        public static string ListAgentActions(List<string> strings)
        {
            if (Mission.Current == null)
                return "Error: Mission is null.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n=== Agent Action Report ===");

            int count = 0;

            // Loop through all agents in the scene
            foreach (Agent agent in Mission.Current.Agents)
            {
                // Filter: Only show active humans (ignore horses and dead bodies to reduce spam)
                if (agent.IsHuman && AgentControlHelper.SafeIsActive(agent))
                {
                    count++;

                    string actionName0 = V.ActName(agent, 0);
                    string actionName1 = V.ActName(agent, 1);

                    // Monster / ActionSet: Defines the "class" of animations (e.g., human_warrior, human_lord)
                    string actionSetId = agent.Monster != null ? agent.Monster.StringId : "Unknown";

                    float distToPlayer = 0f;
                    if (Mission.Current.MainAgent != null)
                    {
                        distToPlayer = agent.Position.Distance(Mission.Current.MainAgent.Position);
                    }

                    sb.AppendLine($"[Agent #{agent.Index}] Name: {agent.Name}");
                    sb.AppendLine($"  Dist to Player: {distToPlayer:F1}m");
                    sb.AppendLine($"  ActionSet (Monster): {actionSetId}");
                    sb.AppendLine($"  Current Action Ch0 (Base): {actionName0}");

                    // Only print Channel 1 if it is actually playing something different
                    if (!string.IsNullOrEmpty(actionName1) && actionName1 != actionName0)
                    {
                        sb.AppendLine($"  Current Action Ch1 (Upper): {actionName1}");
                    }

                    // Check if they are using a GameObject (like a chair)
                    if (agent.CurrentlyUsedGameObject != null)
                    {
                        sb.AppendLine($"  Using Object: {agent.CurrentlyUsedGameObject.GameEntity.Name} (Type: {agent.CurrentlyUsedGameObject.GetType().Name})");
                    }

                    sb.AppendLine("---------------------------");
                }
            }

            if (count == 0) return "No active human agents found.";

            return sb.ToString();
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("check_all_variable_in_file", "custom")]
        public static string DumpVariables(List<string> strings)
        {
            if (GlobalVariableBehavior.Instance == null)
            {
                return "Error: GlobalVariableBehavior Instance null.";
            }

            try
            {
                // 1. 获取所有变量的字符串快照
                string dumpContent = GlobalVariableBehavior.Instance.DumpAllVariables();

                // 2. 确定保存路径
                // 通常保存在: 文档/Mount and Blade II Bannerlord/Configs/StoryEngine_Dump.txt
                string documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
                string savePath = System.IO.Path.Combine(documentsPath, "Mount and Blade II Bannerlord", "Configs", "StoryEngine_Dump.txt");

                // 确保目录存在
                string dir = System.IO.Path.GetDirectoryName(savePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 3. 写入文件 (强制使用 UTF-8 编码以支持中文)
                File.WriteAllText(savePath, dumpContent, Encoding.UTF8);

                // 4. 在游戏左下角提示 (这里支持中文)
                InformationManager.DisplayMessage(new InformationMessage($"[剧本] 变量已导出至: {savePath}", Color.FromUint(0x00FF00)));

                return "Success";
            }
            catch (Exception ex)
            {
                // 如果出错，打印错误信息
                return $"Error: {ex.Message}";
            }
        }



        [CommandLineFunctionality.CommandLineArgumentFunction("print_equip", "custom")]
        public static string PrintEquip(List<string> strings)
        {
            StringBuilder sb = new StringBuilder();
            CharacterObject targetChar = null;
            string stringId = "";
            if (Campaign.Current != null)
            {
                if (strings.Count >= 1)
                {
                    stringId = strings[0];
                    //基于stringId获取当前场景的Agent
                   
                    Agent agent = Mission.Current.Agents.FirstOrDefault(a => a.Character.StringId == stringId);
                    if (agent != null)
                    {
                        sb.AppendLine($">> Print {stringId} 's equipment:");
                        targetChar = agent.Character as CharacterObject;
                        AppendEquipmentToLog(sb, agent.SpawnEquipment, "Spawn Loadout");
                    }
                    else
                    {
                        sb.AppendLine($">> Can Not Find {stringId}  Print Player's equipment:");
                        AppendEquipmentToLog(sb, Agent.Main.SpawnEquipment, "Spawn Loadout");
                    }
                    
                }
                else
                {
                    sb.AppendLine($">> Can Not Find {stringId}  Print Player's equipment:");
                    AppendEquipmentToLog(sb, Agent.Main.SpawnEquipment, "Spawn Loadout");
                }
                return sb.ToString();
            }
            else
            {
                //大地图，就默认打主角吧
                targetChar = Hero.MainHero.CharacterObject;
                AppendEquipmentToLog(sb, targetChar.Equipment, "Battle Loadout");
                AppendEquipmentToLog(sb, targetChar.FirstCivilianEquipment, "Civilian Loadout");
                return sb.ToString();
            }
            
        }

        // Helper method to iterate through slots and format the string (English Only)
        private static void AppendEquipmentToLog(StringBuilder sb, Equipment equipment, string loadoutName)
        {
            sb.AppendLine($"\n--- {loadoutName} ---");

            if (equipment == null)
            {
                sb.AppendLine("Error: Equipment data is null.");
                return;
            }
            //Equipment battleEquip = Hero.MainHero.BattleEquipment;
            // Iterate through all equipment slots (Weapons, Head, Body, Leg, Gloves, Cape, Horse, Harness)
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                EquipmentIndex slotIndex = i;
                EquipmentElement element = equipment[slotIndex];

                if (!element.IsEmpty && element.Item != null)
                {
                    string slotName = slotIndex.ToString(); // Get Enum name (e.g., Head, Body, Leg)
                    string itemId = element.Item.StringId; // The ID you need

                    // Format: [SlotName] ItemID
                    sb.AppendLine($"[{slotName}] {itemId}");
                }
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("update_historyname", "custom")]
        public static string UpdateHistoryName(List<string> strings)
        {
            StoryEngine.ChangeNameBasedOnHistory();
            return "success";
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("divorce", "custom")]
        public static string ExecuteDivorce(List<string> strings)
        {
            string stringId = "";
            if (strings.Count > 0)
            {
                stringId = strings[0];
                Hero targetHero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == stringId);
                if (targetHero != null)
                {
                    targetHero.Spouse = null;
                    return "stringId divorce success";
                }

            }
            
            

                Hero.MainHero.Spouse = null;
                return "player divorce success";

        }

        /// <summary>
        /// 触发玩家处决指定 Hero 的过场动画。
        /// 用法: custom.execute_hero &lt;heroStringId&gt;
        /// 弹出确认窗口，确认后播放处决动画并真正杀死目标。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("execute_hero", "custom")]
        public static string ExecuteHeroCutscene(List<string> args)
        {
            if (Campaign.Current == null)
                return "Error: Campaign not loaded.";

            if (args.Count == 0)
                return "Usage: custom.execute_hero <heroStringId>\n  e.g. custom.execute_hero lord_4_1";

            string heroId = args[0];
            Hero targetHero = Campaign.Current.CampaignObjectManager.Find<Hero>(heroId);

            if (targetHero == null)
                return $"Error: Hero '{heroId}' not found.";

            if (!targetHero.IsAlive)
                return $"Error: Hero '{targetHero.Name}' is already dead.";

            if (targetHero == Hero.MainHero)
                return "Error: You cannot execute yourself.";
            

            HeroExecutionSceneNotificationData data = HeroExecutionSceneNotificationData
                .CreateForPlayerExecutingHero(targetHero, onAffirmativeAction: null);

            MBInformationManager.ShowSceneNotification(data);

            return $"Execution scene triggered for {targetHero.Name} ({heroId}).\nClick 'Execute' to proceed, or close the popup to cancel.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("ninja_report", "custom")]
        public static string ShowNinjaReport(List<string> strings)
        {
            // 🔴 2026-09-24 修：原实现要求"恰好 2 个参数、名字在第 2 格"（`if (strings.Count == 2) name = strings[1]`），
            //    —— 于是 `custom.ninja_report lwn_xxx`（1 个参数）会**静默回落到默认效果**，
            //    实测把"巨石撞石"当成了被测粒子（用户 2026-09-24 实机白烟+碎石）。
            //    现在按项目约定改：**首参可弃**（1 个参数时它就是名字；多个参数取最后一个），
            //    且**把解析结果打进返回文字**（`id -1` = 未注册；`id >= 0` = 认得）—— 判读不再靠肉眼。
            const string DefaultParticle = "psys_game_boulder_stone_coll";

            string requested = null;
            if (strings != null && strings.Count >= 1)
            {
                string last = strings[strings.Count - 1];
                if (!string.IsNullOrWhiteSpace(last)) requested = last.Trim();
            }

            string particleName = requested ?? DefaultParticle;
            int particleId = ParticleSystemManager.GetRuntimeIdByName(particleName);
            string note = $"[note: '{particleName}' -> id {particleId}]";
            if (particleId == -1 && requested != null)
            {
                particleId = ParticleSystemManager.GetRuntimeIdByName(DefaultParticle);
                note = $"[note: '{requested}' NOT REGISTERED (id -1) -> fell back to '{DefaultParticle}' (id {particleId})]";
            }

            Agent mainAgent = Mission.Current?.MainAgent;
            if (particleId != -1 && mainAgent != null)
            {
                try { Mission.Current.Scene.CreateBurstParticle(particleId, mainAgent.Frame); }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[NinjaReport] 生成粒子失败：{ex.GetType().Name} {ex.Message}");
                    return "ERROR: CreateBurstParticle failed (see log) " + note;
                }
            }

            NinjaNotificationManager.Show("忍者报告!", () => {
                // 这里写点击圆圈后要发生的事情
                // 比如：Mission.Current.SpawnNinja(...);
            });

            return "success " + note;
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("change_leader_by_id", "custom")]
        public static string ChangeLeaderById(List<string> strings)
        {
            // Check if Campaign is active
            if (Campaign.Current == null)
            {
                return "Error: Campaign system is not loaded.";
            }

            // Check argument count
            if (strings == null || strings.Count != 2)
            {
                return "Usage: campaign.change_leader_by_id [CurrentLeaderStringId] [NewLeaderStringId]";
            }

            string currentLeaderId = strings[0];
            string newLeaderId = strings[1];

            // Find the heroes by StringId
            Hero currentLeader = Campaign.Current.CampaignObjectManager.Find<Hero>(currentLeaderId);
            Hero newLeader = Campaign.Current.CampaignObjectManager.Find<Hero>(newLeaderId);

            // Validate Heroes
            if (currentLeader == null)
            {
                return "Error: Could not find a hero with StringId: " + currentLeaderId;
            }

            if (newLeader == null)
            {
                return "Error: Could not find a hero with StringId: " + newLeaderId;
            }

            // Validate Clan status
            Clan targetClan = currentLeader.Clan;

            if (targetClan == null)
            {
                return "Error: The hero '" + currentLeader.Name + "' does not belong to any clan.";
            }

            if (targetClan.Leader != currentLeader)
            {
                return "Error: The hero '" + currentLeader.Name + "' is not the leader of the clan '" + targetClan.Name + "'.";
            }

            if (!newLeader.IsAlive)
            {
                return "Error: The new leader candidate is dead.";
            }

            // Optional: Check if new leader is in the same clan (Safety check, though Action might handle it)
            if (newLeader.Clan != targetClan)
            {
                // You can decide to return an error or force move them. 
                // Standard logic implies leader should be in the clan.
                return "Error: The new leader '" + newLeader.Name + "' is not in the same clan (" + targetClan.Name + ").";
            }

            try
            {
                // Apply the action
                ChangeClanLeaderAction.ApplyWithSelectedNewLeader(targetClan, newLeader);
                return "Success: Clan '" + targetClan.Name + "' leader changed from '" + currentLeader.Name + "' to '" + newLeader.Name + "'.";
            }
            catch (System.Exception ex)
            {
                return "Exception occurred: " + ex.Message;
            }
        }

        /// <summary>
        /// 打印当前 Mission 中的所有 GameEntity。
        /// 用法:
        ///   custom.print_entities                    → 摘要：总数 + Tag 分布 + 最近 20 个实体
        ///   custom.print_entities animal             → 过滤 Tag 包含 "animal"
        ///   custom.print_entities goose              → 过滤 Name 包含 "goose"
        ///   custom.print_entities all 10             → 距离玩家 10 米内的所有实体
        ///   custom.print_entities animal 15          → Tag=animal 且距离 15 米内
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("print_entities", "custom")]
        public static string ExecutePrintEntities(List<string> args)
        {
            if (Mission.Current == null || Mission.Current.Scene == null)
                return "Error: not in mission.";

            string filter = null;
            float maxDist = -1f;

            if (args.Count >= 1 && args[0].ToLower() != "all")
                filter = args[0].ToLower();
            if (args.Count >= 2)
                float.TryParse(args[1], out maxDist);
            if (args.Count >= 1 && args[0].ToLower() == "all" && args.Count == 1)
                maxDist = -1f; // no filter, no distance = summary mode

            // ── 收集所有实体 ──
            List<GameEntity> allEntities = new List<GameEntity>();
            Mission.Current.Scene.GetEntities(ref allEntities);

            if (allEntities.Count == 0)
                return "No GameEntities found in this scene.";

            Vec3 playerPos = Agent.Main?.Position ?? Vec3.Zero;

            // ── 过滤 ──
            List<GameEntity> filtered = new List<GameEntity>();
            foreach (var e in allEntities)
            {
                if (e == null) continue;

                // 距离过滤
                if (maxDist > 0)
                {
                    float dist = e.GlobalPosition.Distance(playerPos);
                    if (dist > maxDist) continue;
                }

                // Tag/Name 过滤
                if (!string.IsNullOrEmpty(filter))
                {
                    bool tagMatch = false;
                    string[] tags = e.Tags;
                    if (tags != null)
                    {
                        foreach (var t in tags)
                        {
                            if (t.ToLower().Contains(filter))
                            { tagMatch = true; break; }
                        }
                    }
                    bool nameMatch = (e.Name?.ToLower().Contains(filter) ?? false);
                    if (!tagMatch && !nameMatch) continue;
                }

                filtered.Add(e);
            }

            // ── 构建 Tag 分布统计 ──
            Dictionary<string, int> tagCounts = new Dictionary<string, int>();
            foreach (var e in allEntities)
            {
                if (e?.Tags == null) continue;
                foreach (var t in e.Tags)
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    string tl = t.ToLower();
                    if (tagCounts.ContainsKey(tl))
                        tagCounts[tl]++;
                    else
                        tagCounts[tl] = 1;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n══════════ GameEntity Report ══════════");
            sb.AppendLine($"Total entities in scene: {allEntities.Count}");
            sb.Append($"Filter: ");
            if (!string.IsNullOrEmpty(filter)) sb.Append($"tag/name='{filter}' ");
            else sb.Append("none ");
            if (maxDist > 0) sb.Append($"dist<={maxDist}m ");
            else sb.Append("(all distances) ");
            sb.AppendLine();
            sb.AppendLine($"Matched: {filtered.Count}");

            // ── Tag 分布（Top 30） ──
            sb.AppendLine($"\n--- Tag Distribution (top 30 of {tagCounts.Count} unique) ---");
            int tagIdx = 0;
            foreach (var kv in tagCounts.OrderByDescending(kv => kv.Value).Take(30))
            {
                tagIdx++;
                sb.Append($"  [{kv.Key}]:{kv.Value}  ");
                if (tagIdx % 5 == 0) sb.AppendLine();
            }
            sb.AppendLine();

            // ── 详细列表（最多 50 个） ──
            int maxShow = 50;
            sb.AppendLine($"\n--- Matched Entities (showing {Math.Min(filtered.Count, maxShow)} of {filtered.Count}) ---");

            // 按距离排序
            var sorted = filtered
                .Select(e => new { Entity = e, Dist = e.GlobalPosition.Distance(playerPos) })
                .OrderBy(x => x.Dist)
                .Take(maxShow);

            int idx = 0;
            foreach (var item in sorted)
            {
                idx++;
                GameEntity e = item.Entity;
                string name = string.IsNullOrWhiteSpace(e.Name) ? "(unnamed)" : e.Name;
                string tags = e.Tags != null && e.Tags.Length > 0
                    ? string.Join(" ", e.Tags) : "(no tags)";
                Vec3 pos = e.GlobalPosition;
                int children = e.ChildCount;

                sb.Append($"[{idx}] {name}");
                sb.Append($"  dist={item.Dist:F1}m");
                sb.Append($"  pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})");
                if (children > 0) sb.Append($"  children={children}");
                sb.AppendLine();
                sb.AppendLine($"    tags: {tags}");
            }

            if (filtered.Count > maxShow)
                sb.AppendLine($"  ... and {filtered.Count - maxShow} more (use filter to narrow)");

            sb.AppendLine("══════════════════════════════════════════");

            string result = sb.ToString();
            DebugLogger.Log(result);
            return result;
        }

        /// <summary>
        /// 打印玩家正前方的实体，用于指认"我眼前这个模型到底是什么"。双保险：
        ///   ① 物理射线（原版交互聚焦同款 API）——命中有碰撞体的实体/地形
        ///   ② 视锥几何扫描——无视碰撞体，只要有 mesh 且在视锥内就列出（纯装饰道具也能认出）
        /// 用法:
        ///   custom.print_lookat              → 10m 内、±12° 视锥
        ///   custom.print_lookat 20           → 20m 内
        ///   custom.print_lookat 20 8         → 20m 内、±8° 视锥
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("print_lookat", "custom")]
        public static string ExecutePrintLookAt(List<string> args)
        {
            if (Mission.Current == null || Mission.Current.Scene == null || Agent.Main == null)
                return "Error: not in mission.";

            float maxDist = 10f;
            float halfAngleDeg = 12f;
            if (args.Count >= 1) float.TryParse(args[0], out maxDist);
            if (args.Count >= 2) float.TryParse(args[1], out halfAngleDeg);

            // 视线原点：优先实际相机（第三人称也准），兜底 Agent 眼睛位置
            Vec3 eye, dir;
            MissionScreen ms = ScreenManager.TopScreen as MissionScreen;
            if (ms?.CombatCamera != null)
            {
                eye = ms.CombatCamera.Position;
                dir = ms.CombatCamera.Direction;
            }
            else
            {
                eye = Agent.Main.GetEyeGlobalPosition();
                dir = Agent.Main.LookDirection;
            }
            dir = dir.NormalizedCopy();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n════════ LookAt Report ════════");
            sb.AppendLine($"eye=({eye.x:F1},{eye.y:F1},{eye.z:F1}) dir=({dir.x:F2},{dir.y:F2},{dir.z:F2}) maxDist={maxDist:F0}m halfAngle=±{halfAngleDeg:F0}°");

            // ── ① 物理射线 ──
            V.LookAtHit hit = V.RayCastLookAt(Mission.Current.Scene, eye, eye + dir * maxDist);
            if (hit.Hit)
            {
                if (hit.EntityName != null)
                    sb.AppendLine($"[Ray] '{hit.EntityName}' dist={hit.Distance:F2}m point=({hit.Point.x:F1},{hit.Point.y:F1},{hit.Point.z:F1}) prefab={hit.PrefabName ?? "-"} mesh={hit.MeshName ?? "-"}");
                else
                    sb.AppendLine($"[Ray] terrain/static (no entity) dist={hit.Distance:F2}m point=({hit.Point.x:F1},{hit.Point.y:F1},{hit.Point.z:F1})");
            }
            else sb.AppendLine("[Ray] no hit — 正前方没有带碰撞体的东西（装饰性 mesh 射线打不到，看下面视锥扫描）");

            // ── ② 视锥几何扫描（GetRootEntities + 递归，与宝箱扫描同一遍历，避免漏子实体）──
            var coneHits = new List<ConeHit>();
            var roots = NativeObjectArray.Create();
            Mission.Current.Scene.GetRootEntities(roots);
            float tanHalf = MathF.Tan(halfAngleDeg * MathF.PI / 180f);
            foreach (NativeObject obj in roots)
            {
                var root = obj as GameEntity;
                if (root != null)
                    CollectLookCone(root, eye, dir, maxDist, tanHalf, coneHits);
            }

            coneHits.Sort((a, b) => a.Along.CompareTo(b.Along));
            sb.AppendLine($"\n--- Cone scan (showing {Math.Min(coneHits.Count, 15)} of {coneHits.Count}) ---");
            int idx = 0;
            foreach (var c in coneHits.Take(15))
            {
                idx++;
                sb.AppendLine($"[{idx}] {c.Name}  dist={c.Along:F1}m  offAxis={c.Perp:F1}m  {GetEntityAssetStr(c.Entity)}");
            }
            if (coneHits.Count == 0)
                sb.AppendLine("(none — 视锥内没有任何带 mesh 的实体，试试加大距离/角度)");

            sb.AppendLine("════════════════════════════════");

            string result = sb.ToString();
            DebugLogger.Log(result);
            return result;
        }

        private struct ConeHit
        {
            public GameEntity Entity;
            public string Name;
            public float Along;   // 沿视线方向的距离
            public float Perp;    // 离视线中轴的垂直距离
        }

        /// <summary>递归收集视锥内的带 mesh 实体。判定：pivot 或首 mesh 包围盒任一角落入锥即算（大物件 pivot 偏移也能命中）。</summary>
        private static void CollectLookCone(GameEntity e, Vec3 eye, Vec3 dir, float maxDist, float tanHalf, List<ConeHit> results)
        {
            if (e == null) return;

            if (e.MultiMeshComponentCount > 0)
            {
                float bestAlong = float.MaxValue, bestPerp = 0f;
                MatrixFrame frame = e.GetGlobalFrame();

                TryConePoint(e.GlobalPosition, eye, dir, maxDist, tanHalf, ref bestAlong, ref bestPerp);
                try
                {
                    var mesh = e.GetMetaMesh(0)?.GetMeshAtIndex(0);
                    if (mesh != null)
                    {
                        Vec3 mn = mesh.GetBoundingBoxMin(), mx = mesh.GetBoundingBoxMax();
                        for (int c = 0; c < 8; c++)
                        {
                            Vec3 corner = new Vec3(
                                (c & 1) == 0 ? mn.x : mx.x,
                                (c & 2) == 0 ? mn.y : mx.y,
                                (c & 4) == 0 ? mn.z : mx.z);
                            TryConePoint(frame.TransformToParent(corner), eye, dir, maxDist, tanHalf, ref bestAlong, ref bestPerp);
                        }
                    }
                }
                catch (Exception) { /* bbox 读取失败不致命，pivot 已测过 */ }

                if (bestAlong < float.MaxValue)
                {
                    string name = string.IsNullOrWhiteSpace(e.Name) ? "(unnamed)" : e.Name;
                    results.Add(new ConeHit { Entity = e, Name = name, Along = bestAlong, Perp = bestPerp });
                }
            }

            for (int i = 0; i < e.ChildCount; i++)
                CollectLookCone(e.GetChild(i), eye, dir, maxDist, tanHalf, results);
        }

        private static void TryConePoint(Vec3 p, Vec3 eye, Vec3 dir, float maxDist, float tanHalf,
            ref float bestAlong, ref float bestPerp)
        {
            Vec3 v = p - eye;
            float along = Vec3.DotProduct(v, dir);
            if (along < 0.2f || along > maxDist) return;          // 太贴近相机或在身后/超程
            float perpSq = v.LengthSquared - along * along;
            float allowed = along * tanHalf;                       // 圆锥半径随距离放大
            if (perpSq > allowed * allowed) return;
            if (along < bestAlong) { bestAlong = along; bestPerp = MathF.Sqrt(MathF.Max(0f, perpSq)); }
        }

        /// <summary>取实体的外观资源名。entity.Name 是场景命名（作者可随意改），外观由 prefab/mesh 资源名决定。</summary>
        private static string GetEntityAssetStr(GameEntity e)
        {
            string prefab = null, mesh = null;
            try { prefab = e.GetPrefabName(); } catch (Exception) { /* 实体失效时跳过 */ }
            try { if (e.MultiMeshComponentCount > 0) mesh = e.GetMetaMesh(0)?.GetName(); } catch (Exception) { }
            return $"prefab={(string.IsNullOrEmpty(prefab) ? "-" : prefab)} mesh={(string.IsNullOrEmpty(mesh) ? "-" : mesh)}";
        }

        /// <summary>
        /// 在玩家面前生成指定 prefab，用于预览外观/选型（例如给保管箱挑模型）。
        /// 预览实体不入存档，离开场景重进即消失。
        /// 用法: custom.spawn_prefab bd_chest_a [dist=2]
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("spawn_prefab", "custom")]
        public static string ExecuteSpawnPrefab(List<string> args)
        {
            if (Mission.Current == null || Mission.Current.Scene == null || Agent.Main == null)
                return "Error: not in mission.";
            if (args.Count < 1 || string.IsNullOrWhiteSpace(args[0]))
                return "Usage: custom.spawn_prefab <prefabName> [dist]";

            string prefabName = args[0];
            float dist = 2f;
            if (args.Count >= 2) float.TryParse(args[1], out dist);

            if (!GameEntity.PrefabExists(prefabName))
                return $"Prefab '{prefabName}' not found (not loaded in this scene).";

            // 落点：玩家面前 dist 米处、与玩家同层地面（取水平朝向，避免低头看地时埋进地里）
            Vec3 look = Agent.Main.LookDirection;
            float hl = MathF.Sqrt(look.x * look.x + look.y * look.y);
            Vec3 fwd = hl > 1e-3f ? new Vec3(look.x / hl, look.y / hl, 0f) : new Vec3(0f, 1f, 0f);
            Vec3 pos = Agent.Main.Position + fwd * dist;

            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = pos;

            GameEntity entity = GameEntity.Instantiate(Mission.Current.Scene, prefabName, frame);
            if (entity == null)
                return $"Instantiate '{prefabName}' failed.";

            string msg = $"Spawned '{prefabName}' at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) {GetEntityAssetStr(entity)}";
            DebugLogger.Log($"[SpawnPrefab] {msg}");
            return msg;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("print_focus", "custom")]
        public static string ExecutePrintFocusDebug(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
                return "Error: not in mission.";

            MissionScreen ms = ScreenManager.TopScreen as MissionScreen;
            if (ms == null) return "Error: no MissionScreen.";
            Camera cam = ms.CombatCamera;
            if (cam == null) return "Error: no CombatCamera.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== Focus Debug ==========");

            Vec3 camPos = cam.Position;
            Vec3 camDir = cam.Direction;
            sb.AppendLine($"[Camera] Pos=({camPos.x:F2},{camPos.y:F2},{camPos.z:F2}) Dir=({camDir.x:F3},{camDir.y:F3},{camDir.z:F3})");

            // --- 每个尸体的 dot 明细 ---
            sb.AppendLine("--- Corpses dot detail (threshold=0.3, range=4m) ---");
            float maxDistSq = 16f;
            var corpses = AttackTriggerMissionLogic.Instance?.GetDeadAgentsRaw();
            int corpseIdx = 0;
            if (corpses != null)
            {
                foreach (Agent d in corpses)
                {
                    corpseIdx++;
                    if (d == null || !d.IsHuman) continue;
                    float distSq = d.Position.DistanceSquared(camPos);
                    if (distSq > maxDistSq)
                    {
                        sb.AppendLine($"  [{corpseIdx}] {d.Name} distSq={distSq:F1} > 16 -> REJECTED (too far)");
                        continue;
                    }
                    Vec3 tc = d.Position + new Vec3(0, 0, 0.8f);
                    Vec3 toTarget = tc - camPos;
                    toTarget.Normalize();
                    float dot = Vec3.DotProduct(camDir, toTarget);
                    string status = dot >= 0.3f ? "SUCCESS" : $"REJECTED (dot={dot:F3}<0.3)";
                    sb.AppendLine($"  [{corpseIdx}] {d.Name} dist={Math.Sqrt(distSq):F2}m dot={dot:F3} -> {status}");
                }
            }
            if (corpseIdx == 0) sb.AppendLine("  (no corpses)");
            sb.AppendLine();

            // --- GetFocusdAgent 最终结果 ---
            var view = InteractionMissionView.Instance;
            Agent focused = view?.GetFocusdAgent();
            sb.AppendLine($"[GetFocusdAgent] => {(focused != null ? $"{focused.Name} (id:{focused.Character?.StringId})" : "null")}");
            sb.AppendLine("==================================");

            return sb.ToString();
        }

#if !MB2_V1212
        // ═══════════════════════════════════════════════════════
        // 警戒/潜入 UI 调试指令（仅 1.4.6+）
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 调试 NPC 警戒/潜入 UI。
        /// 用法:
        ///   custom.stealth_debug              → 打印当前嫌疑度 + 守卫警戒状态
        ///   custom.stealth_debug 0.5          → 设 PlayerSuspiciousLevel=0.5 并打印
        ///   custom.stealth_debug 0.96         → 设 0.96 触发潜行模式 (阈值 0.95)
        ///   custom.stealth_debug reset        → 重置嫌疑度到 0，关闭潜行模式
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("stealth_debug", "custom")]
        public static string StealthDebug(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
                return "Error: not in mission.";

            var disguiseLogic = Mission.Current.GetMissionBehavior<DisguiseMissionLogic>();
            var failCounter = Mission.Current.GetMissionBehavior<StealthFailCounterMissionLogic>();

            if (disguiseLogic == null)
                return "Error: DisguiseMissionLogic not active. You must be in a hideout/disguise mission for the stealth system to be loaded.";

            // --- 处理参数：设置嫌疑度 ---
            if (args.Count >= 1)
            {
                string arg = args[0].ToLower();
                if (arg == "reset")
                {
                    disguiseLogic.PlayerSuspiciousLevel = 0f;
                    DebugLogger.Log("[StealthDebug] SuspiciousLevel reset to 0");
                }
                else if (float.TryParse(arg, out float val))
                {
                    val = MathF.Clamp(val, 0f, 1f);
                    disguiseLogic.PlayerSuspiciousLevel = val;
                    InformationManager.DisplayMessage(new InformationMessage(
                        val >= 0.95f
                            ? $"[StealthDebug] ⚠ SuspiciousLevel={val:F2} — STEALTH MODE ACTIVE"
                            : $"[StealthDebug] SuspiciousLevel={val:F2}"));
                }
                else
                {
                    return $"Error: unknown arg '{arg}'. Use a number (0~1) or 'reset'.";
                }
            }

            // --- 构建报告 ---
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("══════════════ Stealth Debug ══════════════");

            // 嫌疑度条
            float level = disguiseLogic.PlayerSuspiciousLevel;
            bool inStealth = disguiseLogic.IsInStealthMode;
            sb.AppendLine($"PlayerSuspiciousLevel: {level:F3} / 1.0  [threshold=0.95]");
            sb.Append("  [");
            int barLen = 40;
            int filled = (int)(level * barLen);
            for (int i = 0; i < barLen; i++)
                sb.Append(i < filled ? (i >= barLen * 0.95f ? '█' : '▓') : '░');
            sb.AppendLine($"] {(inStealth ? "⚠ STEALTH MODE" : "normal")}");

            // 失败倒计时
            if (failCounter != null)
            {
                float elapsed = failCounter.FailCounterElapsedTime;
                sb.AppendLine($"FailCounter: Active={failCounter.IsActive}  Elapsed={elapsed:F1}s / {failCounter.FailCounterSeconds}s");
            }

            // 守卫威胁信息
            var threatInfos = disguiseLogic.ThreatAgentInfos;
            sb.AppendLine($"\n--- Guard Threat Info ({threatInfos.Count} tracking) ---");
            if (threatInfos.Count == 0)
            {
                sb.AppendLine("  (no guards tracking player)");
            }
            else
            {
                foreach (var kv in threatInfos)
                {
                    Agent guard = kv.Key;
                    var info = kv.Value;
                    string offenseStr = info.OffenseType switch
                    {
                        StealthOffenseTypes.IsVisible => "👁 VISIBLE",
                        StealthOffenseTypes.IsInPersonalZone => "🚫 PERSONAL ZONE",
                        _ => "none"
                    };
                    string camSee = info.CanPlayerCameraSeeTheAgent ? "(on screen)" : "";

                    // 尝试获取该守卫的 AlarmFactor
                    string alarmStr = "";
#if false // TODO: CampaignAgentComponent doesn't exist in any known MB2 version — find correct API
                    var nav = guard.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
                    var alarmGroup = nav?.GetBehaviorGroup<AlarmedBehaviorGroup>();
                    if (alarmGroup != null)
                    {
                        float af = alarmGroup.AlarmFactor;
                        string stateStr = guard.IsAlarmed() ? "ALARMED" :
                                          guard.IsCautious() ? "CAUTIOUS" :
                                          guard.IsPatrollingCautious() ? "PATROL-CAUTIOUS" : "NORMAL";
                        alarmStr = $"  AlarmFactor={af:F2} [{stateStr}]";
                    }
#endif

                    sb.AppendLine($"  {guard.Name} ({guard.Character?.StringId ?? "?"}): {offenseStr} {camSee}{alarmStr}");
                }
            }

            // 守卫 AlarmedBehaviorGroup 总览
            sb.AppendLine($"\n--- All Guard Alarm States ---");
            int guardCount = 0;
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (!agent.IsHuman || agent.Team == null || agent.Team.IsPlayerAlly) continue;
                if (agent == Agent.Main) continue;

#if false // TODO: CampaignAgentComponent doesn't exist in any known MB2 version
                var nav = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
                var alarmGroup = nav?.GetBehaviorGroup<AlarmedBehaviorGroup>();
                if (alarmGroup == null) continue;

                guardCount++;
                float af = alarmGroup.AlarmFactor;
                string stateStr = agent.IsAlarmed() ? "ALARMED" :
                                  agent.IsCautious() ? "CAUTIOUS" :
                                  agent.IsPatrollingCautious() ? "PATROL-CAUTIOUS" : "NORMAL";
                string dnc = alarmGroup.DoNotCheckForAlarmFactorIncrease ? " [BLIND]" : "";
                sb.AppendLine($"  {agent.Name}: AF={af:F3} {stateStr}{dnc}");
#endif
            }
            if (guardCount == 0)
                sb.AppendLine("  (no guards with AlarmedBehaviorGroup found)");

            sb.AppendLine("══════════════════════════════════════════════");
            return sb.ToString();
        }

        /// <summary>
        /// 给任意 NPC 装上 AlarmedBehaviorGroup（警戒行为组）。
        /// 这是原版潜入系统的基础引擎——装上后 NPC 就能做视觉检测、累积 AlarmFactor。
        /// 用法: custom.stealth_arm_npc <npcStringId>
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("stealth_arm_npc", "custom")]
        public static string StealthArmNpc(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
                return "Error: not in mission.";

            if (args.Count < 1)
                return "Usage: custom.stealth_arm_npc <npcStringId>\n  e.g. custom.stealth_arm_npc villager_template_1";

            string targetId = args[0];
            Agent target = null;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a.IsHuman && a.Character?.StringId == targetId)
                { target = a; break; }
            }
            if (target == null)
                return $"Error: no human agent found with StringId='{targetId}'.";

#if false // TODO: CampaignAgentComponent doesn't exist in any known MB2 version
            var nav = target.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            if (nav == null)
                return $"Error: {target.Name} has no AgentNavigator (not a campaign agent?).";

            var existing = nav.GetBehaviorGroup<AlarmedBehaviorGroup>();
            if (existing != null)
            {
                return $"{target.Name} ({targetId}) already has AlarmedBehaviorGroup.\n" +
                       $"  AlarmFactor={existing.AlarmFactor:F3}\n" +
                       $"  DoNotCheck={existing.DoNotCheckForAlarmFactorIncrease}\n" +
                       $"  IsAlarmed={target.IsAlarmed()} IsCautious={target.IsCautious()} IsPatrollingCautious={target.IsPatrollingCautious()}";
            }

            // 装上 AlarmedBehaviorGroup（默认 DoNotCheckForAlarmFactorIncrease=true → 守卫"闭眼"）
            var group = nav.AddBehaviorGroup<AlarmedBehaviorGroup>();
            group.DoNotCheckForAlarmFactorIncrease = false; // 睁眼！开始检测
            group.DisableCalmDown = true;                    // 不自动冷静（调试用）

            return $"SUCCESS: {target.Name} ({targetId}) now has AlarmedBehaviorGroup.\n" +
                   $"  DoNotCheck=false (guard is WATCHING)\n" +
                   $"  DisableCalmDown=true (won't auto-calm down)\n" +
                   $"  AlarmFactor={group.AlarmFactor:F3}";
#endif
            return "Not available: CampaignAgentComponent not found in this game version.";
        }

        /// <summary>
        /// 手动设置 NPC 的警戒状态，测试不同级别的行为/动画变化。
        /// 用法:
        ///   custom.stealth_alarm <npcId> 0    → 正常（Normal）
        ///   custom.stealth_alarm <npcId> 1    → 怀疑/警戒（Cautious）
        ///   custom.stealth_alarm <npcId> 2    → 战斗（Alarmed）
        ///   custom.stealth_alarm <npcId> push <value>  → 累加 AlarmFactor
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("stealth_alarm", "custom")]
        public static string StealthAlarm(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
                return "Error: not in mission.";

            if (args.Count < 2)
                return "Usage:\n" +
                       "  custom.stealth_alarm <npcId> 0|1|2   → set alarm state\n" +
                       "  custom.stealth_alarm <npcId> push <val> → add to AlarmFactor";

            string targetId = args[0];
            Agent target = null;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a.IsHuman && a.Character?.StringId == targetId)
                { target = a; break; }
            }
            if (target == null)
                return $"Error: no human agent found with StringId='{targetId}'. Use custom.stealth_arm_npc first if needed.";

#if false // TODO: CampaignAgentComponent doesn't exist in any known MB2 version
            var nav = target.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            var group = nav?.GetBehaviorGroup<AlarmedBehaviorGroup>();
            if (group == null)
                return $"Error: {target.Name} has no AlarmedBehaviorGroup. Run custom.stealth_arm_npc {targetId} first.";

            string subCmd = args[1].ToLower();

            if (subCmd == "push" && args.Count >= 3 && float.TryParse(args[2], out float addVal))
            {
                // 累加 AlarmFactor——用 WorldPosition 指向玩家位置
                group.AddAlarmFactor(addVal, Agent.Main.GetWorldPosition());
                return $"{target.Name}: AlarmFactor += {addVal:F2} → now {group.AlarmFactor:F3}\n" +
                       $"  IsAlarmed={target.IsAlarmed()} IsCautious={target.IsCautious()}";
            }

            if (int.TryParse(subCmd, out int level))
            {
                switch (level)
                {
                    case 0:
                        group.ResetAlarmFactor();
                        target.SetAlarmState(Agent.AIStateFlag.None);
                        return $"{target.Name}: → NORMAL (AlarmFactor=0, state reset)";
                    case 1:
                        group.AddAlarmFactor(1.5f, Agent.Main.GetWorldPosition());
                        // AddAlarmFactor 内部会在 AlarmFactor>=1 时自动 SetAlarmState(Cautious)
                        return $"{target.Name}: → CAUTIOUS (AlarmFactor={group.AlarmFactor:F3})";
                    case 2:
                        group.AddAlarmFactor(2.5f, Agent.Main.GetWorldPosition());
                        target.SetAlarmState(Agent.AIStateFlag.Alarmed);
                        return $"{target.Name}: → ALARMED (AlarmFactor={group.AlarmFactor:F3})";
                    default:
                        return $"Error: level must be 0/1/2, got {level}";
                }
            }

            return $"Error: unknown sub-command '{args[1]}'. Use 0/1/2 or 'push <val>'.";
#endif
            return "Not available: CampaignAgentComponent not found in this game version.";
        }
#endif

        // ═══════════════════════════════════════════════════════
        // 世界事件调试指令
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 列出所有活跃世界事件。
        /// 用法: custom.worldevent_list
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldevent_list", "custom")]
        public static string ListWorldEvents(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";

            var active = WorldEventStore.ActiveEvents;
            if (active.Count == 0)
                return "No active world events. (Use worldevent_force to create one)";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n=== Active World Events ({active.Count}) ===");

            foreach (var e in active)
            {
                float daysLeft = e.ExpiryDay - (float)CampaignTime.Now.ToDays;
                string loc = e.TargetSettlement?.Name?.ToString() ?? "???";
                string target = e.TargetHero?.Name?.ToString() ?? "-";
                string instigator = e.IsGenericInstigator ? "generic" : (e.InstigatorHero?.Name?.ToString() ?? "generic");
                string daysStr = daysLeft > 0 ? $"{daysLeft:F1}d left" : "EXPIRED";

                sb.AppendLine($"  [{e.Type}] {loc} | target={target} instigator={instigator}");
                sb.AppendLine($"    sev={e.Severity}/10 {daysStr} party={e.GeneratedPartyId ?? "none"} id={e.EventId}");

                if (e.HasHiddenMastermind)
                    sb.AppendLine($"    ⚠ has hidden mastermind: {e.HiddenMastermindId}");
            }

            sb.AppendLine($"\nResolved: {WorldEventStore.ResolvedEvents.Count} | Expired: {WorldEventStore.ActiveEvents.Count(e => e.IsExpired)} | Total: {WorldEventStore.TotalEventCount}");

            return sb.ToString();
        }

        /// <summary>
        /// 强制生成一个世界事件（调试用）。
        /// 用法:
        ///   custom.worldevent_force                  → 生成 BanditRaid
        ///   custom.worldevent_force Kidnapping       → 生成 Kidnapping
        ///   custom.worldevent_force BanditRaid 8     → 生成 severity=8 的 BanditRaid
        /// 可用类型: BanditRaid Kidnapping Famine Betrayal DebtTrap RomanticConflict
        ///           FalseAccusation InheritanceDispute Fugitive TradeDispute
        ///           NobleConflict SacredTheft Assassination
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldevent_force", "custom")]
        public static string ForceWorldEvent(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";

            EventType type = EventType.BanditRaid;
            int severity = -1;

            if (args.Count >= 1)
            {
                if (!Enum.TryParse(args[0], true, out type))
                    return $"Error: Unknown event type '{args[0]}'. Valid: {string.Join(", ", Enum.GetNames(typeof(EventType)))}";
            }
            if (args.Count >= 2)
            {
                if (!int.TryParse(args[1], out severity) || severity < 1 || severity > 10)
                    return "Error: severity must be 1-10.";
            }

            string result = WorldEventSimulator.ForceGenerateEvent(type, severity);
            InformationManager.DisplayMessage(new InformationMessage($"[WorldEvent] Force generated: {result}"));
            return result;
        }

        /// <summary>
        /// 显示世界事件模拟器内部状态。
        /// 用法: custom.worldevent_status
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldevent_status", "custom")]
        public static string WorldEventStatus(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n=== WorldEvent Simulator Status ===");

            // 从 simulator 实例获取状态
            var simulator = Campaign.Current.GetCampaignBehavior<WorldEventSimulator>();
            if (simulator != null)
            {
                // 通过反射或公开属性获取内部状态...
                // 使用 WorldEventStore 的公开信息
            }

            float currentDay = (float)CampaignTime.Now.ToDays;
            sb.AppendLine($"Current Day: {currentDay:F1}");
            sb.AppendLine($"Active Events: {WorldEventStore.ActiveEvents.Count}");
            sb.AppendLine($"Total Events (all time): {WorldEventStore.TotalEventCount}");
            sb.AppendLine($"Resolved: {WorldEventStore.ResolvedEvents.Count}");
            sb.AppendLine($"Director Idle: {WorldEventDirector.IsIdle}");
            sb.AppendLine($"Director Last Commission Day: {WorldEventDirector.LastCommissionDay:F1}");

            // 统计各类型事件数
            var byType = WorldEventStore.ActiveEvents
                .GroupBy(e => e.Type)
                .ToDictionary(g => g.Key, g => g.Count());
            sb.AppendLine("\nActive by type:");
            foreach (var kv in byType)
                sb.AppendLine($"  {kv.Key}: {kv.Value}");

            // 宿敌
            var nemeses = HeroNemesisTracker.GetLivingNemeses();
            sb.AppendLine($"\nLiving Nemeses: {nemeses.Count}");
            foreach (var n in nemeses.Take(5))
                sb.AppendLine($"  {n.HeroName} Lv{(int)n.Level} encounters={n.TimesEncountered} scar={n.HasScar}");

            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════
        // 原版 Issue 调试指令
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 强制给指定 NPC 发布指定 Issue（绕过所有限制，调试用）。
        /// 用法:
        ///   custom.force_issue list                          → 列出所有可用的原版 Quest ID
        ///   custom.force_issue &lt;heroId&gt; &lt;questId&gt;            → 给指定 Hero 发布 Issue
        ///   custom.force_issue &lt;questId&gt;                     → 给当前定居点的第一个 Notable 发布
        ///   custom.force_issue &lt;heroId&gt; &lt;questId&gt; accept     → 发布并自动接取
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("force_issue", "custom")]
        public static string ExecuteForceIssue(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";

            // ── "list" 子命令：列出所有可用 Quest ID ──
            if (args.Count == 0 || args[0].ToLower() == "list")
            {
                return ListAllVanillaQuestIds();
            }

            // ── 解析参数 ──
            string heroId;
            string questId;
            bool autoAccept = false;

            if (args.Count >= 2)
            {
                heroId = args[0];
                questId = args[1];
                autoAccept = args.Count >= 3 && args[2].ToLower() == "accept";
            }
            else
            {
                // 单参数：questId，自动找当前定居点的 Notable
                heroId = null;
                questId = args[0];
            }

            // ── 查找目标 Hero ──
            Hero targetHero = null;
            if (!string.IsNullOrEmpty(heroId))
            {
                // ① 精确匹配 StringId
                targetHero = Campaign.Current.CampaignObjectManager.Find<Hero>(heroId);

                // ② 兜底：按名字包含匹配
                if (targetHero == null)
                {
                    targetHero = Hero.AllAliveHeroes
                        .FirstOrDefault(h => h.Name != null && h.Name.ToString().Contains(heroId));
                }
            }
            else
            {
                // ③ 自动从当前定居点找 Notable
                targetHero = FindNearbyNotable();
            }

            if (targetHero == null)
            {
                string nearbyId = FindNearbyNotable()?.StringId;
                string nearbyName = FindNearbyNotable()?.Name?.ToString();
                string nearbyInfo = nearbyId != null ? $"{nearbyName} ({nearbyId})" : "none";
                return $"Error: No target hero found.\n" +
                       $"Specify a Hero StringId (e.g. 'lord_1_1'), or enter a settlement to auto-detect.\n" +
                       $"Nearby notable: {nearbyInfo}\n" +
                       $"Use 'custom.force_issue list' to see all quest IDs.";
            }

            // ── 规范化 Quest ID ──
            string vanillaId = questId.StartsWith("VANILLA_") ? questId : $"VANILLA_{questId}";

            // 验证 ID 是否合法
            string issueTypeName = VanillaQuestMapping.GetIssueTypeNameForId(vanillaId);
            if (string.IsNullOrEmpty(issueTypeName))
            {
                return $"Error: Unknown quest ID '{questId}'.\n" +
                       $"Use 'custom.force_issue list' to see all available IDs.";
            }

            // ── 清理已有 Issue ──
            if (targetHero.Issue != null)
            {
                string oldType = targetHero.Issue.GetType().Name;
                if (!ForceClearIssue(targetHero))
                {
                    return $"Error: Failed to clear existing issue '{oldType}' from {targetHero.Name}.";
                }
                InformationManager.DisplayMessage(
                    new InformationMessage($"[force_issue] Cleared existing: {oldType}"));
            }

            // ── 创建新 Issue ──
            IssueBase issue = IssueFactory.CreateVanillaIssue(vanillaId, targetHero);

            // 兜底：直接构造 + 反射赋值（绕过 IssueManager 的 occupation 校验）
            if (issue == null)
            {
                issue = ForceCreateIssueDirect(vanillaId, targetHero);
            }

            if (issue == null)
            {
                return $"Error: Failed to create issue '{vanillaId}' for {targetHero.Name}.\n" +
                       $"The quest type may be incompatible with this NPC's occupation ({targetHero.Occupation}).\n" +
                       $"Try a different combination — use 'custom.force_issue list' to browse.";
            }

            StringBuilder result = new StringBuilder();
            result.AppendLine($"SUCCESS: {vanillaId}");
            result.AppendLine($"  Issuer: {targetHero.Name} ({targetHero.StringId})");
            result.AppendLine($"  Occupation: {targetHero.Occupation}");
            result.AppendLine($"  Settlement: {targetHero.CurrentSettlement?.Name?.ToString() ?? "none"}");

            // ── 自动接取 ──
            if (autoAccept)
            {
                bool started = Campaign.Current.IssueManager.StartIssueQuest(targetHero);
                if (started && targetHero.Issue?.IssueQuest != null)
                {
                    var quest = targetHero.Issue.IssueQuest;
                    TryInvokeQuestAcceptedConsequences(quest);
                    result.AppendLine($"  Quest ACCEPTED: {quest.GetType().Name}");
                    InformationManager.DisplayMessage(
                        new InformationMessage($"[force_issue] Quest accepted: {quest.Title}", Colors.Green));
                }
                else
                {
                    result.AppendLine("  Warning: Auto-accept failed. Try talking to the NPC manually.");
                }
            }
            else
            {
                result.AppendLine("  Go talk to the NPC to accept the quest!");
            }

            InformationManager.DisplayMessage(
                new InformationMessage($"[force_issue] {vanillaId} → {targetHero.Name}", Colors.Green));

            return result.ToString();
        }

        /// <summary>
        /// 列出所有可用的原版 Quest ID（分组展示）。
        /// </summary>
        private static string ListAllVanillaQuestIds()
        {
            // 使用 VanillaQuestMapping 的 IssueNameToId 反向构建分类
            var allIds = new List<string>();
            var type = typeof(VanillaQuestMapping);
            var field = type.GetField("IssueNameToId",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
            {
                var dict = field.GetValue(null) as System.Collections.Generic.Dictionary<string, string>;
                if (dict != null)
                {
                    allIds = dict.Values.OrderBy(s => s).ToList();
                }
            }

            if (allIds.Count == 0)
            {
                // Fallback: 硬编码列表
                allIds = new List<string>
                {
                    "VANILLA_ArmyNeedsSupplies", "VANILLA_ArmyOfPoachers",
                    "VANILLA_ArtisanCantSell", "VANILLA_ArtisanOverpricedGoods",
                    "VANILLA_BettingFraud", "VANILLA_CapturedByBountyHunters",
                    "VANILLA_CaravanAmbush", "VANILLA_CompanyOfTrouble",
                    "VANILLA_ConquestOfSettlement", "VANILLA_EscortMerchantCaravan",
                    "VANILLA_ExtortionByDeserters", "VANILLA_FamilyFeud",
                    "VANILLA_GangNeedsRecruits", "VANILLA_GangNeedsWeapons",
                    "VANILLA_GangOffloadStolenGoods", "VANILLA_GangSpecialWeapons",
                    "VANILLA_HeadmanDeliverHerd", "VANILLA_HeadmanNeedsGrain",
                    "VANILLA_LadysKnightOut", "VANILLA_LandlordManualLaborers",
                    "VANILLA_LandlordTradeArt", "VANILLA_LandlordTraining",
                    "VANILLA_LandlordVillageCommons", "VANILLA_LesserNobleRevolt",
                    "VANILLA_LordNeedsGarrisonTroops", "VANILLA_LordNeedsHorses",
                    "VANILLA_LordsNeedsTutor", "VANILLA_LordWantsRivalCaptured",
                    "VANILLA_MerchantNeedsHelpWithOutlaws", "VANILLA_NearbyBanditBase",
                    "VANILLA_NotableDaughterFound", "VANILLA_ProdigalSon",
                    "VANILLA_RaidEnemyTerritory", "VANILLA_RevenueFarming",
                    "VANILLA_RivalGangMovingIn", "VANILLA_RuralNotableInnOut",
                    "VANILLA_ScoutEnemyGarrisons", "VANILLA_Smugglers",
                    "VANILLA_SnareTheWealthy", "VANILLA_TheSpyParty",
                    "VANILLA_VillageCraftingMaterials", "VANILLA_VillageDraughtAnimals",
                    "VANILLA_VillageNeedsTools",
                };
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n=== Available Vanilla Quest IDs ({allIds.Count} total) ===");
            sb.AppendLine();
            sb.AppendLine("Usage: custom.force_issue <heroId> <questId>");
            sb.AppendLine("       custom.force_issue <heroId> <questId> accept  (auto-accept)");
            sb.AppendLine();
            sb.AppendLine("── 村庄要人 (Headman) ──");
            foreach (var id in allIds.Where(id =>
                id.Contains("Headman") || id.Contains("Village") ||
                id.Contains("Landlord") || id.Contains("Extortion") ||
                id.Contains("FamilyFeud") || id.Contains("NotableDaughter") ||
                id.Contains("RuralNotable")))
                sb.AppendLine($"  {id}");
            sb.AppendLine();
            sb.AppendLine("── 城镇工匠/商人 (Artisan/Merchant) ──");
            foreach (var id in allIds.Where(id =>
                id.Contains("Artisan") || id.Contains("EscortMerchant") ||
                id.Contains("CaravanAmbush") || id.Contains("BettingFraud") ||
                id.Contains("RevenueFarming")))
                sb.AppendLine($"  {id}");
            sb.AppendLine();
            sb.AppendLine("── 帮派头目 (GangLeader) ──");
            foreach (var id in allIds.Where(id =>
                id.Contains("Gang") || id.Contains("RivalGang") ||
                id.Contains("SnareTheWealthy")))
                sb.AppendLine($"  {id}");
            sb.AppendLine();
            sb.AppendLine("── 领主/贵族 (Lord) ──");
            foreach (var id in allIds.Where(id =>
                id.Contains("Lord") || id.Contains("Ladys") ||
                id.Contains("ProdigalSon") || id.Contains("TheSpyParty") ||
                id.Contains("ArmyNeeds") || id.Contains("ScoutEnemy") ||
                id.Contains("RaidAnEnemy") || id.Contains("ConquestOf") ||
                id.Contains("LesserNoble")))
                sb.AppendLine($"  {id}");
            sb.AppendLine();
            sb.AppendLine("── 通用/全局 (Any Notable) ──");
            foreach (var id in allIds.Where(id =>
                id.Contains("NearbyBandit") || id.Contains("ArmyOfPoachers") ||
                id.Contains("MerchantNeedsHelp") || id.Contains("Smugglers") ||
                id.Contains("CapturedByBounty") || id.Contains("CompanyOfTrouble")))
                sb.AppendLine($"  {id}");
            sb.AppendLine();
            sb.AppendLine("── NPC Occupation types for reference ──");
            sb.AppendLine("  Headman (village)  |  Merchant (town)  |  Artisan (town)");
            sb.AppendLine("  GangLeader (town)  |  Lord (any)       |  Wanderer");
            sb.AppendLine("=========================================");

            return sb.ToString();
        }

        /// <summary>
        /// 从当前定居点自动找一个 Notable 作为目标。
        /// </summary>
        private static Hero FindNearbyNotable()
        {
            if (Hero.MainHero?.CurrentSettlement == null) return null;

            var settlement = Hero.MainHero.CurrentSettlement;

            // 优先 Notables（包括 Headman, Merchant, Artisan, GangLeader）
            foreach (var h in settlement.Notables)
            {
                if (h != null && h != Hero.MainHero && h.IsAlive)
                    return h;
            }

            // 兜底：HeroesWithoutParty
            foreach (var h in settlement.HeroesWithoutParty)
            {
                if (h != null && h != Hero.MainHero && h.IsAlive
                    && !settlement.Notables.Contains(h))
                    return h;
            }

            return null;
        }

        /// <summary>
        /// 强制清除 Hero 身上的已有 Issue（反射操作，调试用）。
        /// </summary>
        private static bool ForceClearIssue(Hero hero)
        {
            try
            {
                // ① 尝试通过 Property setter
                var issueProp = typeof(Hero).GetProperty("Issue",
                    BindingFlags.Public | BindingFlags.Instance);
                if (issueProp != null && issueProp.CanWrite)
                {
                    issueProp.SetValue(hero, null);
                    return hero.Issue == null;
                }

                // ② 尝试内部 setter（NonPublic）
                if (issueProp != null)
                {
                    var setMethod = issueProp.GetSetMethod(true); // nonPublic
                    if (setMethod != null)
                    {
                        setMethod.Invoke(hero, new object[] { null });
                        return hero.Issue == null;
                    }
                }

                // ③ 尝试直接设 backing field
                foreach (var fieldName in new[] { "_issue", "_currentIssue", "issue" })
                {
                    var field = typeof(Hero).GetField(fieldName,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(hero, null);
                        return hero.Issue == null;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"[force_issue] ForceClearIssue exception: {ex.Message}", Colors.Red));
                return false;
            }
        }

        /// <summary>
        /// 兜底创建：直接反射构造 Issue + 强行赋予 Hero。
        /// 当 IssueFactory 走不通时使用（绕过 IssueManager 的 occupation 校验）。
        /// </summary>
        private static IssueBase ForceCreateIssueDirect(string vanillaId, Hero hero)
        {
            string issueTypeName = VanillaQuestMapping.GetIssueTypeNameForId(vanillaId);
            if (string.IsNullOrEmpty(issueTypeName)) return null;

            // 查找 Issue Type
            Type issueType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
                if (!asmName.StartsWith("TaleWorlds") && asmName != "SandBox"
                    && asmName != "SandBoxCore" && asmName != "StoryMode")
                    continue;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == issueTypeName && typeof(IssueBase).IsAssignableFrom(t))
                        {
                            issueType = t;
                            break;
                        }
                    }
                }
                catch { }
                if (issueType != null) break;
            }

            if (issueType == null) return null;

            try
            {
                // 尝试 Activator 构造 (Hero)
                var ctor = issueType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Hero) }, null);
                if (ctor == null)
                {
                    // 尝试两参数构造 (Hero, object)
                    ctor = issueType.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(Hero), typeof(object) }, null);
                }

                IssueBase issue = null;
                if (ctor != null)
                {
                    var paramCount = ctor.GetParameters().Length;
                    issue = paramCount == 1
                        ? ctor.Invoke(new object[] { hero }) as IssueBase
                        : ctor.Invoke(new object[] { hero, null }) as IssueBase;
                }
                else
                {
                    // 最后兜底：Activator.CreateInstance
                    issue = Activator.CreateInstance(issueType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new object[] { hero }, null) as IssueBase;
                }

                if (issue == null) return null;

                // 强行赋予 Hero（绕过 IssueManager 的校验）
                var issueProp = typeof(Hero).GetProperty("Issue",
                    BindingFlags.Public | BindingFlags.Instance);
                if (issueProp != null && issueProp.CanWrite)
                {
                    issueProp.SetValue(hero, issue);
                }
                else
                {
                    var setMethod = issueProp?.GetSetMethod(true);
                    setMethod?.Invoke(hero, new object[] { issue });
                }

                InformationManager.DisplayMessage(
                    new InformationMessage($"[force_issue] Force-created {issueTypeName} via direct reflection", Colors.Yellow));
                return hero.Issue;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"[force_issue] ForceCreateIssueDirect failed: {ex.Message}", Colors.Red));
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════
        // 对话注入调试指令
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 从 JSON 文件注入动态对话到当前 NPC 的对话树中。
        /// 用法:
        ///   custom.inject_dialogue my_dialogue.json    → 加载 my_dialogue.json
        ///   custom.inject_dialogue clear               → 清除所有注入的对话
        ///
        /// 文件查找顺序:
        ///   1. Modules/LivingWorldNpcs/ModuleData/DesignData/Dialogues/&lt;name&gt;.json
        ///   2. 文档/Mount and Blade II Bannerlord/Configs/&lt;name&gt;.json
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("inject_dialogue", "custom")]
        public static string InjectDialogueFromJson(List<string> args)
        {
            if (Campaign.Current == null)
                return "Error: Campaign not loaded.";

            if (args.Count >= 1 && args[0].ToLower() == "clear")
                return DialogueInjector.ClearAll();

            if (args.Count == 0)
                return "Usage: custom.inject_dialogue <jsonFileName>\n" +
                       "       custom.inject_dialogue clear\n\n" +
                       "File is loaded from:\n" +
                       "  Modules/LivingWorldNpcs/ModuleData/DesignData/Dialogues/<name>.json\n" +
                       "  or Documents/Mount and Blade II Bannerlord/Configs/<name>.json";

            string jsonPath = DialogueInjector.FindJsonFile(args[0]);
            if (jsonPath == null)
                return DialogueInjector.GetSearchPathsDescription(args[0]);

            return DialogueInjector.InjectFromJson(jsonPath);
        }

        /// <summary>
        /// 反射调用 quest 的私有 QuestAcceptedConsequences()，
        /// 确保任务日志和进度条正确初始化。
        /// </summary>
        private static bool TryInvokeQuestAcceptedConsequences(QuestBase quest)
        {
            try
            {
                var questType = quest.GetType();
                foreach (var methodName in new[] { "QuestAcceptedConsequences", "OnQuestAccepted", "HandleQuestAccepted" })
                {
                    var method = questType.GetMethod(methodName,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method != null && method.GetParameters().Length == 0)
                    {
                        method.Invoke(quest, null);
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 警戒值系统调试指令（Phase 5）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// custom.alert_status [agentStringId] — 查看某 NPC 的分类警戒值明细
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("alert_status", "custom")]
        public static string AlertStatusCommand(List<string> args)
        {
            if (Mission.Current == null) return "Error: Not in a mission.";

            Agent target = null;
            if (args.Count > 0)
            {
                string id = args[0];
                foreach (var agent in Mission.Current.Agents)
                {
                    if (agent.IsHuman && AgentControlHelper.SafeIsActive(agent) && agent.Character?.StringId == id)
                    { target = agent; break; }
                }
                if (target == null) return $"Agent '{id}' not found.";
            }
            else
            {
                // 默认显示离玩家最近的 NPC
                float minDist = float.MaxValue;
                foreach (var agent in Mission.Current.Agents)
                {
                    if (!agent.IsHuman || !AgentControlHelper.SafeIsActive(agent) || agent == Agent.Main) continue;
                    float d = agent.Position.DistanceSquared(Agent.Main.Position);
                    if (d < minDist) { minDist = d; target = agent; }
                }
                if (target == null) return "No NPC found.";
            }

            var brain = AgentAIController.GetBrainForAgent(target);
            if (brain == null) return $"{target.Name}: No brain.";

            float alertVal = brain.AlertValue;
            var phase = brain.AlertPhase;
            var primary = brain.PrimaryAction;

            var sb = new StringBuilder();
            sb.AppendLine($"=== {target.Name} Alert Status ===");
            sb.AppendLine($"Total: {alertVal:F3} | Phase: {phase} | Primary: {primary}");
            sb.AppendLine("Breakdown:");

            // 反射获取 _alertBreakdown (私有字段)
            var breakdownField = typeof(AgentBrain).GetField("_alertBreakdown",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (breakdownField?.GetValue(brain) is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var entryType = kv.Value.GetType();
                    float val = (float)entryType.GetField("Value").GetValue(kv.Value);
                    string tgt = (string)entryType.GetField("TargetName")?.GetValue(kv.Value) ?? "";
                    string item = (string)entryType.GetField("ItemName")?.GetValue(kv.Value) ?? "";
                    sb.AppendLine($"  {kv.Key}: {val:F3} | target={tgt} | item={item}");
                }
            }
            else
            {
                sb.AppendLine("  (empty or reflection failed)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// custom.alert_force_intercept [npcStringId] — 强制触发 L3 质问
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("alert_force_intercept", "custom")]
        public static string AlertForceInterceptCommand(List<string> args)
        {
            if (Mission.Current == null) return "Error: Not in a mission.";

            Agent target = null;
            if (args.Count > 0)
            {
                string id = args[0];
                foreach (var agent in Mission.Current.Agents)
                {
                    if (agent.IsHuman && AgentControlHelper.SafeIsActive(agent) && agent.Character?.StringId == id)
                    { target = agent; break; }
                }
            }
            if (target == null) return "No target NPC specified or found.";

            var brain = AgentAIController.GetBrainForAgent(target);
            if (brain == null) return $"{target.Name}: No brain.";

            // 强制加值到 Alarmed
            brain.AddAlert(PlayerActionType.Steal, 2.5f);
            brain.ReceiveEvent(new AIEvent { EventType = "BecomeAlarmed", Sender = brain });
            return $"{target.Name}: L3 confront forced. AlertValue={brain.AlertValue:F3}";
        }

        /// <summary>
        /// custom.alert_dialogue_mode [StoryVM|Vanilla] — 切换 L3 质问对话模式
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("alert_dialogue_mode", "custom")]
        public static string AlertDialogueModeCommand(List<string> args)
        {
            if (args.Count == 0) return $"Current: {Settings.Instance.AlertDialogueMode}";
            string mode = args[0];
            if (mode.Equals("StoryVM", StringComparison.OrdinalIgnoreCase))
            { Settings.Instance.AlertDialogueMode = AlertDialogueMode.StoryVM; return "Set to StoryVM."; }
            if (mode.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            { Settings.Instance.AlertDialogueMode = AlertDialogueMode.VanillaConversation; return "Set to VanillaConversation."; }
            return $"Unknown mode: {mode}. Use StoryVM or Vanilla.";
        }

        /// <summary>
        /// 打印 WorldEvent × 对应 Issue × 对应 Quest 的关联视图（存档修复调试用）。
        /// 用法: custom.worldevent_chain
        /// 用途：核对犯罪事件 → 权威 NPC 挂的 Issue → 接取的 Quest 三者链条是否完整
        /// （读档校验依赖 IssueQuest 关联，此命令可直接观察关联是否建立）。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldevent_chain", "custom")]
        public static string WorldEventChain(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n════════ WorldEvent × Issue × Quest ════════");

            // ── 1. 每个活跃 WorldEvent → 对应 Issue / Quest ──
            var active = WorldEventStore.ActiveEvents;
            if (active.Count == 0)
                sb.AppendLine("(无活跃 WorldEvent)");
            foreach (var e in active)
            {
                sb.AppendLine($"\n[Event] {e.Type} id={e.EventId}");
                sb.AppendLine($"   Stage={e.Stage} sev={e.Severity} settlement={e.TargetSettlementId ?? "-"} suspect={e.SuspectHeroId ?? "-"} witnesses={e.WitnessCount}");

                // 对应 Issue：该事件的权威 NPC 头顶挂的
                try
                {
                    var authority = WorldEventStore.GetAuthorityNpc(e);
                    if (authority != null)
                    {
                        var issue = authority.Issue;
                        if (issue is CommissionHubIssue hub)
                        {
                            // 反射读 _context（private readonly，命令场景可接受）
                            string crimeEvent = "?", stage = "?", suspect = "?";
                            try
                            {
                                var ctxField = typeof(CommissionHubIssue).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance);
                                var ctx = ctxField?.GetValue(hub);
                                crimeEvent = ctx?.GetType().GetField("CrimeEventId")?.GetValue(ctx) as string ?? "-";
                                stage = ctx?.GetType().GetField("CrimeEventStage")?.GetValue(ctx)?.ToString() ?? "-";
                                suspect = ctx?.GetType().GetField("SuspectName")?.GetValue(ctx) as string ?? "-";
                            }
                            catch { }
                            sb.AppendLine($"   Issue: {authority.Name} → CommissionHubIssue \"{hub.Title}\"");
                        }
                        else if (issue != null)
                        {
                            sb.AppendLine($"   Issue: {authority.Name} → [{issue.GetType().Name}] \"{issue.Title}\" IssueQuest={issue.IssueQuest?.StringId ?? "(null)"}");
                        }
                        else
                        {
                            sb.AppendLine($"   Issue: {authority.Name} → (无)");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"   Issue: (无权威 NPC)");
                    }
                }
                catch (Exception ex) { sb.AppendLine($"   Issue: 读取失败 {ex.Message}"); }

                // 对应 Quest：扫活动 quest 按 WorldEventId 匹配
                var related = Campaign.Current.QuestManager.Quests
                    .Where(q => GetQuestWorldEventId(q) == e.EventId)
                    .ToList();
                if (related.Count == 0)
                    sb.AppendLine($"   Quest: (无关联)");
                else
                    foreach (var q in related)
                        sb.AppendLine($"   Quest: [{q.GetType().Name}] \"{q.Title}\" id={q.StringId} ongoing={q.IsOngoing} finalized={q.IsFinalized}");
            }

            // ── 2. 活动 Quest 全览 ──
            sb.AppendLine("\n=== 活动 Quest（QuestManager.Quests）===");
            var quests = Campaign.Current.QuestManager.Quests;
            if (quests.Count == 0)
                sb.AppendLine("(无)");
            foreach (var q in quests)
            {
                string weId = GetQuestWorldEventId(q);
                sb.AppendLine($"  [{q.GetType().Name}] \"{q.Title}\" id={q.StringId} ongoing={q.IsOngoing} finalized={q.IsFinalized} worldEventId={weId ?? "-"} giver={q.QuestGiver?.Name?.ToString() ?? "-"}");
            }

            // ── 3. 活动 Issue 全览 ──
            sb.AppendLine("\n=== 活动 Issue（IssueManager.Issues）===");
            var issues = Campaign.Current.IssueManager.Issues;
            if (issues.Count == 0)
                sb.AppendLine("(无)");
            foreach (var kv in issues)
            {
                var iss = kv.Value;
                string questLink = iss.IssueQuest != null ? iss.IssueQuest.StringId : "(null)";
                sb.AppendLine($"  {kv.Key?.Name?.ToString() ?? "?"} → [{iss.GetType().Name}] \"{iss.Title}\" IssueQuest={questLink}");
            }

            sb.AppendLine("══════════════════════════════════════════");
            DebugLogger.Log(sb.ToString());
            return sb.ToString();
        }

        /// <summary>取 quest 关联的 WorldEventId（CommissionQuest 反射 _data.WorldEventId，其他类型返回 null）。</summary>
        private static string GetQuestWorldEventId(QuestBase q)
        {
            try
            {
                if (q is CommissionQuest cq)
                {
                    var dataField = typeof(CommissionQuest).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
                    var data = dataField?.GetValue(cq);
                    if (data != null)
                    {
                        var weField = data.GetType().GetField("WorldEventId");
                        return weField?.GetValue(data) as string;
                    }
                }
            }
            catch { }
            return null;
        }

        // ── 玩法键位热重载：改 config.json 后执行 custom.input_reload 立即生效（键位/按法/阈值）──
        [CommandLineFunctionality.CommandLineArgumentFunction("input_reload", "custom")]
        public static string ReloadInputBindings(List<string> args)
        {
            try
            {
                Settings.Reload();
                ModInput.RebuildBindings();
                // 刷新场景内全部按键提示与长按按法（交互列表/偷窃条按钮）
                InteractionMissionView.Instance?.RefreshAllBindingTexts();
                DebugLogger.Log("[InputReload] Bindings reloaded from config.json");
                return "Input bindings reloaded from config.json";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[InputReload] Error: {ex}");
                return "Input reload failed: " + ex.Message;
            }
        }

        /// <summary>模拟触发群聊事件话题（调试用，2026-08-10）：与真实事件同走 ImEventBroadcaster 入口。
        /// 用法：custom.im_test_event battle_win|battle_lose|imprison|release|quest|companion|raid|kingdom</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("im_test_event", "custom")]
        public static string ExecuteImTestEvent(List<string> args)
        {
            string type = args.Count > 0 ? args[0].ToLower() : "battle_win";
            string desc = args.Count > 1 ? string.Join(" ", args.Skip(1)) : null;
            desc ??= type switch
            {
                "battle_win" => "主公刚刚打赢了一场战斗，大获全胜",
                "battle_lose" => "主公刚刚打了一场败仗，吃了亏",
                "imprison" => "主公被俘了，如今身陷囹圄",
                "release" => "主公平安获释，重获自由",
                "quest" => "主公接下了一桩新差事",
                "companion" => "队伍里来了一位新人",
                "raid" => "咱们的村庄正在被洗劫",
                "kingdom" => "有一个王国覆灭了，天下震动",
                _ => "主公经历了一件大事",
            };
            ImEventBroadcaster.BroadcastPlayerEvent(type, desc);
            return "OK: simulated event '" + type + "' broadcast to party channel (NPC will speak; watch anti-spam cooldown).";
        }

        /// <summary>
        /// 自动世界观状态查询。
        /// 用法: custom.worldbg_status
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldbg_status", "custom")]
        public static string WorldBgStatus(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";
            var behavior = Campaign.Current.GetCampaignBehavior<WorldBackgroundBehavior>();
            string state = behavior != null ? behavior.CurrentState.ToString() : "(behavior not registered)";
            string fp = WorldBackgroundStore.Fingerprint ?? "";
            string currentFp = "";
            try { currentFp = WorldBackgroundProvider.GetFingerprint(); } catch { }
            bool fpMatch = !string.IsNullOrEmpty(fp) && fp == currentFp;
            var sb = new StringBuilder();
            sb.AppendLine("=== WorldBackground Status ===");
            sb.AppendLine($"State: {state}");
            sb.AppendLine($"Blob: {(string.IsNullOrEmpty(WorldBackgroundStore.Blob) ? "(empty)" : WorldBackgroundStore.Blob)}");
            sb.AppendLine($"BlobLen: {WorldBackgroundStore.Blob?.Length ?? 0}");
            sb.AppendLine($"Fingerprint: {fp}");
            sb.AppendLine($"CurrentFingerprint: {currentFp}");
            sb.AppendLine($"FingerprintMatch: {fpMatch}");
            sb.AppendLine($"IsLLMConfigured: {Settings.Instance?.IsLLMConfigured}");
            return sb.ToString();
        }

        /// <summary>
        /// 清空 blob 强制重新生成世界观。
        /// 用法: custom.worldbg_regenerate
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldbg_regenerate", "custom")]
        public static string WorldBgRegenerate(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";
            var behavior = Campaign.Current.GetCampaignBehavior<WorldBackgroundBehavior>();
            if (behavior == null) return "Error: WorldBackgroundBehavior not registered.";
            behavior.ForceRegenerate();
            return "OK: blob cleared, will regenerate on next tick (await [WorldBg] log).";
        }

        /// <summary>
        /// 转储世界观 blob 原文（验收用：查实时状态残留/点名在位者人名）。
        /// 用法: custom.worldbg_dump
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("worldbg_dump", "custom")]
        public static string WorldBgDump(List<string> args)
        {
            if (string.IsNullOrEmpty(WorldBackgroundStore.Blob))
                return "(blob empty: not generated / generation failed / LLM not configured)";
            return WorldBackgroundStore.Blob;
        }

        /// <summary>
        /// 导出指定 Hero（默认主角）的完整脸参数：BodyProperties 原文 + 反序列化后的 KeyWeights[320]。
        /// 用途：拿游戏进程内 native 解码（GetParamsFromKey）的结果，供 Blender 侧复原脸。
        /// 用法: custom.dump_facekey [heroStringId]   (不传参数 = 主角)
        /// 输出同时写入 Debug/StoryEngine_RuntimeLog.txt
        /// 已核实签名 (1.5.1)：FaceGenerationParams.KeyWeights = float[320]；
        ///   MBBodyProperties.GetParamsFromKey(ref FaceGenerationParams, BodyProperties, bool earsAreHidden, bool mouthHidden)
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_facekey", "custom")]
        public static string DumpFaceKey(List<string> args)
        {
            if (Campaign.Current == null || Hero.MainHero == null)
                return "Error: Campaign not loaded.";

            Hero target = Hero.MainHero;
            if (args.Count >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                target = Campaign.Current.CampaignObjectManager.Find<Hero>(args[0]);
                if (target == null)
                    return $"Error: Hero '{args[0]}' not found.";
            }

            CharacterObject charObj = target.CharacterObject;
            if (charObj == null || charObj.BodyPropertyRange == null)
                return $"Error: {target.Name} has no BodyPropertyRange.";
            // 定脸 NPC: BodyPropertyRange.Init(face, face) → min == max == 确定的脸；随机范围脸才 min!=max
            BodyProperties raw = charObj.BodyPropertyRange.BodyPropertyMin;
            FaceGenerationParams fgp = new FaceGenerationParams();
            try
            {
                TaleWorlds.MountAndBlade.MBBodyProperties.GetParamsFromKey(ref fgp, raw, false, false);
            }
            catch (Exception e)
            {
                return $"Error: GetParamsFromKey failed on this build: {e.Message}";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== FaceKey Dump: {target.Name} ({target.StringId}) ===");
            sb.AppendLine(raw.ToString());
            sb.AppendLine($"CurrentFaceTattoo={fgp.CurrentFaceTattoo}");
            for (int i = 0; i < fgp.KeyWeights.Length; i++)
            {
                sb.AppendLine($"[{i}] {fgp.KeyWeights[i]:F4}");
            }
            string msg = sb.ToString();
            DebugLogger.Log(msg);
            return msg;
        }

        // ================= 脸形参数实验：归零 / 置 basis / 套用指定 key / 还原 =================
        // 为什么需要：骑砍的脸形参数（BodyProperties 的 128 hex key → 320 个权重）会驱动我们网格的
        //   59 条形变通道。把权重调到「形变恰好为 0」就能看到网格的**基础形状**（= 源模型原样），
        //   用来判断「鼻子怪 / 脸被拉歪」到底是几何问题还是形变问题。
        //
        // 🔴 权重 0 ≠ 无形变：引擎的映射是 value = key_min + w×(key_max−key_min)（见 §2），
        //    所以 w=0 时形变 = key_min（多数通道非 0）。要真·basis 必须解 w = −key_min/(key_max−key_min)。
        //    → face_basis 走真·basis，face_zero 走「权重归零」那版（文档 §18.3 的原方案），两条都留着对比。
        //
        // 🔴 只对【主角】生效：CharacterObject.UpdatePlayerCharacterBodyProperties 内部判 IsPlayerCharacter。
        // 返回文本一律英文（控制台纪律）；详细权重表写 Debug/StoryEngine_RuntimeLog.txt。

        private const int FaceKeyWeightCount = 59;   // 0..58 = 脸形键；59..62 = weight/build/height/age（身体，不动）
        private static string _faceBackupHex = null; // face_restore 的原始 key（首次跑任一 face_* 时自动备份）
        private static string _faceBackupHero = null;

        private static string GetFaceTarget(List<string> args, out Hero hero, out string note)
        {
            hero = null;
            note = null;
            if (Campaign.Current == null || Hero.MainHero == null)
                return "Error: Campaign not loaded.";
            hero = Hero.MainHero;
            if (args != null && args.Count >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                // 🔴 第一个参数做成【可弃占位】：骑砍2 的 CommandLineArgumentFunction 在完全不填参数时
                //    可能根本不触发，所以玩家/调试时会随手给个 "1"。解析不出英雄就【回落到主角】并说明，
                //    绝不能报 not found 就完事（实测踩过：custom.face_basis 1 → "Hero '1' not found."）。
                Hero found = Campaign.Current.CampaignObjectManager.Find<Hero>(args[0]);
                if (found != null)
                    hero = found;
                else
                    note = $" [note: '{args[0]}' is not a hero id -> using main hero]";
            }
            CharacterObject co = hero.CharacterObject;
            if (co == null || co.BodyPropertyRange == null)
                return $"Error: {hero.Name} has no BodyPropertyRange.";
            if (!co.IsPlayerCharacter)
                return "Error: only the player character can be written back "
                     + "(UpdatePlayerCharacterBodyProperties requires IsPlayerCharacter).";
            return null;
        }

        /// <summary>按给定的 per-key 权重函数重算并写回主角的脸。</summary>
        private static string ApplyFaceWeights(Hero hero, Func<int, DeformKeyData, float, float> weightFor, string tag, string note)
        {
            CharacterObject co = hero.CharacterObject;
            BodyProperties cur = co.BodyPropertyRange.BodyPropertyMin;
            FaceGenerationParams fgp = new FaceGenerationParams();
            MBBodyProperties.GetParamsFromKey(ref fgp, cur, false, false);
            float[] w = fgp.KeyWeights;
            if (w == null || w.Length < FaceKeyWeightCount)
                return $"Error: KeyWeights unavailable (len={(w == null ? -1 : w.Length)}).";

            if (_faceBackupHex == null)                  // 第一次实验前先备份，供 face_restore
            {
                _faceBackupHex = cur.ToString();
                _faceBackupHero = hero.StringId;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== Face {tag}: {hero.Name} ({hero.StringId}) ===");
            sb.AppendLine($"key before : {cur}");
            sb.AppendLine("idx  key_min   key_max    tp     before -> after");
            for (int i = 0; i < FaceKeyWeightCount; i++)
            {
                DeformKeyData dkd = MBBodyProperties.GetDeformKeyData(i, fgp.CurrentRace, fgp.CurrentGender, (int)fgp.CurrentAge);
                float target = weightFor(i, dkd, w[i]);
                if (float.IsNaN(target) || float.IsInfinity(target))
                    target = w[i];                        // 算不出来就保持原样（防御）
                sb.AppendLine($"{i,3}  {dkd.KeyMin,8:F3}  {dkd.KeyMax,8:F3}  {dkd.KeyTimePoint,3}   {w[i]:F4} -> {target:F4}");
                w[i] = target;
            }

            BodyProperties np = cur;                      // 从原值起步：保留 weight/build/age 等动态属性
            MBBodyProperties.ProduceNumericKeyWithParams(fgp, false, false, ref np);
            co.UpdatePlayerCharacterBodyProperties(np, co.Race, co.IsFemale);
            sb.AppendLine($"key after  : {np}");
            DebugLogger.Log(sb.ToString());
            return $"OK ({tag}). before={cur} after={np}{note ?? ""} (full table in StoryEngine_RuntimeLog.txt)";
        }

        /// <summary>
        /// 把脸形权重调到「形变恰好 = 0」→ 看到网格的基础形状（= 源模型原样，不受任何拉杆影响）。
        /// 用法: custom.face_basis [heroStringId]   (不传 = 主角)
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("face_basis", "custom")]
        public static string FaceBasis(List<string> args)
        {
            Hero hero; string note; string err = GetFaceTarget(args, out hero, out note);
            if (err != null)
                return err;
            // value = key_min + w*(key_max-key_min) = 0  →  w = -key_min/(key_max-key_min)
            return ApplyFaceWeights(hero, (i, d, cur) =>
            {
                float span = d.KeyMax - d.KeyMin;
                if (Math.Abs(span) < 1e-6f)
                    return cur;                           // 该通道固定（min==max），不参与
                return -d.KeyMin / span;
            }, "basis", note);
        }

        /// <summary>
        /// 把脸形权重（0..58）直接归零 —— 文档 §18.3 记录的原始方案，与 face_basis 对比用。
        /// （注意：权重 0 时形变 = key_min，未必等于 0，所以未必是"原脸"。）
        /// 用法: custom.face_zero [heroStringId]
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("face_zero", "custom")]
        public static string FaceZero(List<string> args)
        {
            Hero hero; string note; string err = GetFaceTarget(args, out hero, out note);
            if (err != null)
                return err;
            return ApplyFaceWeights(hero, (i, d, cur) => 0f, "zero", note);
        }

        /// <summary>
        /// 直接套用一串 128 hex 的 key（存满意的脸 / 复现别人的脸 / 贴回备份）。
        /// 用法: custom.face_key &lt;128hex&gt; [heroStringId]
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("face_key", "custom")]
        public static string FaceKey(List<string> args)
        {
            if (args == null || args.Count < 1 || string.IsNullOrWhiteSpace(args[0]))
                return "Usage: custom.face_key <128-hex key> [heroStringId]";
            Hero hero;
            string note;
            string err = GetFaceTarget(args.Count >= 2 ? new List<string> { args[1] } : null, out hero, out note);
            if (err != null)
                return err;
            BodyProperties target;
            if (!BodyProperties.FromString(args[0], out target))
                return $"Error: cannot parse key '{args[0]}'.";
            CharacterObject co = hero.CharacterObject;
            if (_faceBackupHex == null)
            {
                _faceBackupHex = co.BodyPropertyRange.BodyPropertyMin.ToString();
                _faceBackupHero = hero.StringId;
            }
            co.UpdatePlayerCharacterBodyProperties(target, co.Race, co.IsFemale);
            DebugLogger.Log($"=== Face key applied: {hero.Name} ({hero.StringId}) ===\n{target}");
            return $"OK. key applied = {target}{note ?? ""}";
        }

        /// <summary>
        /// 还原成跑任一 face_* 命令之前的 key（本次游戏进程内有效；跨进程请用 face_key 贴回备份 hex）。
        /// 用法: custom.face_restore
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("face_restore", "custom")]
        public static string FaceRestore(List<string> args)
        {
            if (_faceBackupHex == null)
                return "Error: no backup in this session (a backup is taken automatically on the first face_* command).";
            Hero hero;
            string note;
            string err = GetFaceTarget(_faceBackupHero == null ? null : new List<string> { _faceBackupHero }, out hero, out note);
            if (err != null)
                return err;
            BodyProperties target;
            if (!BodyProperties.FromString(_faceBackupHex, out target))
                return $"Error: cannot parse backup '{_faceBackupHex}'.";
            CharacterObject co = hero.CharacterObject;
            co.UpdatePlayerCharacterBodyProperties(target, co.Race, co.IsFemale);
            DebugLogger.Log($"=== Face restored: {hero.Name} ({hero.StringId}) -> {target} ===");
            return $"OK. restored to {target}{note ?? ""}";
        }

        // ═══════════════════════════════════════════════════════════════
        // 飞天实验用最小指令（2026-09-18）
        // 两个正交的拨杆，交给人在游戏里人肉试：① 写高度  ② 改控制权。
        // 刻意做成"一次一发、不驻留状态"——目的是让每一次观察只对应一个动作。
        // 返回文本纯英文（铁律）；详情进 DebugLogger。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 设置玩家 agent 的高度（绝对 Z，米）。**只写一次**，返回写入前后的坐标。
        /// 用法：
        ///   custom.set_height 20    # 抬到绝对高度 20
        ///   custom.set_height +5    # 当前高度 +5（相对）
        ///   custom.set_height -5    # 当前高度 -5
        ///   custom.set_height       # 只报当前高度
        /// 注：readback 是**同一帧**读完的结果；后面帧会不会被引擎拉回去，看画面/再看一次本命令。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("set_height", "custom")]
        public static string SetPlayerHeight(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null)
                    return "[Height] no player agent (enter a mission first)";

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[Height] current pos=({player.Position.x:F1},{player.Position.y:F1},{player.Position.z:F2}) - usage: custom.set_height <z> | +d | -d";

                string raw = args[0].Trim();
                bool relative = raw.StartsWith("+") || raw.StartsWith("-");
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return $"[Height] '{raw}' is not a number -> ignored. current z={player.Position.z:F2} (usage: custom.set_height <z> | +d | -d)";

                Vec3 before = player.Position;
                float targetZ = relative ? before.z + v : v;
                player.TeleportToPosition(new Vec3(before.x, before.y, targetZ));
                Vec3 after = player.Position;
                string mountInfo2 = DescribeMount(player);

                DebugLogger.Log(
                    $"[HeightSpike] set z: target={targetZ:F2} before=({before.x:F1},{before.y:F1},{before.z:F2}) " +
                    $"readback=({after.x:F1},{after.y:F1},{after.z:F2}) onLand={player.IsOnLand()}{mountInfo2}");

                return $"[Height] target={targetZ:F2} readback z={after.z:F2} (same-frame; engine may pull it back later){mountInfo2}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[HeightSpike] failed: {ex}");
                return $"[Height] failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 骑乘状态描述（飞天实验用）：骑手和马**分别**报坐标——不然只看骑手看不出马跟没跟。
        /// 🔴 TeleportToPosition 的实现是先搬坐骑再搬骑手，两者可能不一致。
        /// </summary>
        private static string DescribeMount(Agent player)
        {
            try
            {
                Agent mount = player.MountAgent;
                if (mount == null)
                    return " (on foot)";
                Vec3 mp = mount.Position;
                return $" [MOUNTED riderZ={player.Position.z:F2} mount=({mp.x:F2},{mp.y:F2},{mp.z:F2}) mountOnLand={mount.IsOnLand()}]";
            }
            catch
            {
                return " [mount?]";
            }
        }

        /// <summary>
        /// 按**增量向量**移动玩家（一次一发）：x/y/z 各加一个偏移。
        /// 用法：
        ///   custom.move 10 0 0     # X +10
        ///   custom.move 0 0 5      # Z +5（抬升）
        ///   custom.move 0 -3 0     # Y -3
        ///   custom.move            # 只报当前坐标
        /// 省掉的参数按 0 算（custom.move 0 0 5 = 只抬 5 米）。
        /// 返回同帧 readback——立刻能看出引擎收没收。
        /// 🔴 与 custom.teleport 的区别：teleport 是绝对坐标（要自己算目标点），本条是增量。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("move", "custom")]
        public static string MovePlayerByDelta(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null)
                    return "[Move] no player agent (enter a mission first)";

                Vec3 before = player.Position;
                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[Move] current pos=({before.x:F2},{before.y:F2},{before.z:F2}) - usage: custom.move <dx> <dy> <dz> (missing = 0)";

                float[] d = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    if (i >= args.Count || string.IsNullOrWhiteSpace(args[i]))
                    {
                        d[i] = 0f;
                        continue;
                    }
                    if (!float.TryParse(args[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d[i]))
                    {
                        // 首参可弃：第一个参数就认不出 → 当占位符，回落成"只报坐标"
                        if (i == 0)
                            return $"[Move] '{args[i]}' is not a number -> ignored. current pos=({before.x:F2},{before.y:F2},{before.z:F2}) (usage: custom.move <dx> <dy> <dz>)";
                        d[i] = 0f;
                    }
                }

                Vec3 target = new Vec3(before.x + d[0], before.y + d[1], before.z + d[2]);
                player.TeleportToPosition(target);
                Vec3 after = player.Position;
                string mountInfo = DescribeMount(player);

                DebugLogger.Log(
                    $"[MoveSpike] d=({d[0]:F2},{d[1]:F2},{d[2]:F2}) from=({before.x:F1},{before.y:F1},{before.z:F2}) " +
                    $"target=({target.x:F1},{target.y:F1},{target.z:F2}) readback=({after.x:F1},{after.y:F1},{after.z:F2}) " +
                    $"onLand={player.IsOnLand()}{mountInfo}");

                return $"[Move] d=({d[0]:F2},{d[1]:F2},{d[2]:F2}) readback=({after.x:F2},{after.y:F2},{after.z:F2}) " +
                       $"(same-frame; engine may pull it back later){mountInfo}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MoveSpike] failed: {ex}");
                return $"[Move] failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 设置玩家 agent 的控制权。**只拨一次**，返回拨完的坐标与状态。
        /// 用法：
        ///   custom.set_controller player   # 还给玩家（恢复了控制就靠这条）
        ///   custom.set_controller ai       # 交给引擎 AI
        ///   custom.set_controller none     # 引擎 AI 完全退场（没人控制）
        ///   custom.set_controller          # 只报当前状态（= 这一族的 get）
        ///
        /// 2026-09-21 新增（飞行要用）——**关掉引擎的玩家输入通道**：
        ///   custom.set_controller input off  # MissionMainAgentController.IsDisabled = true
        ///                                    # 引擎不再读键盘写玩家移动输入 ⇒ 人不动
        ///                                    # 🔴 agent.Controller **不变**，仍是 Player
        ///   custom.set_controller input on   # 恢复
        ///
        /// 🔴 `input off` 与 `none` 的区别（容易混）：
        ///   `none`  = 改 **agent 的控制权归属**（Controller=None，引擎 AI 退场）
        ///   `input off` = 改 **引擎那个"读键盘"的模块**（MissionView 上的 IsDisabled），控制权归属不动
        ///
        /// 🔴 一律走 V.* 版本兼容封装（Agent.ControllerType 在 1.3+ 改名为顶级 AgentControllerType）。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("set_controller", "custom")]
        public static string SetPlayerController(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null)
                    return "[Ctrl] no player agent (enter a mission first)";

                // 🔴 控制权一行打头 —— 这是查这条命令时最想第一眼看到的东西。
                //    用 raw ControllerType，不用 V.IsAgentPlayer 那种派生布尔（那个看不出 None/AI 的区别）。
                var mainAgentCtrlType = player.Controller;

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[Ctrl] Controller={mainAgentCtrlType} playerControlled={V.IsAgentPlayer(player)} {EngineInputPart()} pos=({player.Position.x:F1},{player.Position.y:F1},{player.Position.z:F2}) - usage: custom.set_controller player|ai|none | input on|off";

                string mode = args[0].Trim().ToLowerInvariant();

                // input on|off —— 关/开引擎的玩家输入通道（不动 agent.Controller）
                // 🔴 2026-09-21 实机证伪：IsDisabled 是 MissionScreen.UpdateCamera **每帧自己管理**的标志
                //    （先置 true 再置回 false），我们设的值下一帧就被冲掉 ⇒ 这条路不管用。
                //    保留命令仅为取证/其它场景，**要冻人请用 custom.flight freeze ai / aipause / aidetach**。
                if (mode == "input")
                {
                    var engineCtrl = Mission.Current?.GetMissionBehavior<TaleWorlds.MountAndBlade.View.MissionViews.MissionMainAgentController>();
                    if (engineCtrl == null)
                        return "[Ctrl] MissionMainAgentController not found";

                    string sw = (args.Count >= 2) ? args[1].Trim().ToLowerInvariant() : string.Empty;
                    if (sw != "on" && sw != "off")
                        return $"[Ctrl] Controller={mainAgentCtrlType} usage: custom.set_controller input on|off (current {EngineInputPart()})";

                    engineCtrl.IsDisabled = sw == "off";
                    DebugLogger.Log($"[CtrlSpike] engineInput -> {sw}; IsDisabled={engineCtrl.IsDisabled} (set) playerControlled={V.IsAgentPlayer(player)}");

                    // 🔴 回显必须【设完之后重新读】—— 之前在赋值前就把字符串拼好了，打的是旧值（2026-09-21 实机踩）
                    return $"[Ctrl] Controller={mainAgentCtrlType} engineInput -> {sw}; {EngineInputPart()} "
                         + "| WARNING: engine resets IsDisabled every frame (MissionScreen.UpdateCamera) -> use 'custom.flight freeze ai' to actually freeze";
                }

                switch (mode)
                {
                    case "player":
                        V.SetPlayerControlFrozen(player, false);
                        break;
                    case "ai":
                        V.SetPlayerControlFrozen(player, true);
                        break;
                    case "none":
                        V.SetAgentControllerNone(player);
                        break;
                    default:
                        return $"[Ctrl] '{args[0]}' unknown -> ignored. use player | ai | none | input on|off (current Controller={mainAgentCtrlType})";
                }

                Vec3 p = player.Position;
                DebugLogger.Log(
                    $"[CtrlSpike] controller -> {mode}; now={player.Controller} pos=({p.x:F1},{p.y:F1},{p.z:F2}) " +
                    $"playerControlled={V.IsAgentPlayer(player)} onLand={player.IsOnLand()}");

                return $"[Ctrl] -> {mode}; Controller={player.Controller} playerControlled={V.IsAgentPlayer(player)} {EngineInputPart()} pos=({p.x:F1},{p.y:F1},{p.z:F2})";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CtrlSpike] failed: {ex}");
                return $"[Ctrl] failed: {ex.Message}";
            }
        }

        /// <summary>当前引擎玩家输入通道的开关状态（找不到那个 MissionView 时说清楚）。</summary>
        private static string EngineInputPart()
        {
            var engineCtrl = Mission.Current?.GetMissionBehavior<TaleWorlds.MountAndBlade.View.MissionViews.MissionMainAgentController>();
            return engineCtrl == null ? "engineInput=(n/a)" : $"engineInput.IsDisabled={(engineCtrl.IsDisabled ? 1 : 0)}";
        }

        /// <summary>
        /// 手动转玩家机身的水平朝向 —— **专测"到底哪个旋转接口能转得动"**（2026-09-21）。
        ///
        /// 背景：飞行时按 WASD 角色不转，怀疑和 `Controller=AI` + `SetIsAIPaused(true)` 有关。
        /// 与其在飞行链路里猜，不如把「写朝向」这一件事单独拎出来拨一下看效果。
        ///
        /// 用法：
        ///   custom.turntodir                # 查：当前朝向 + 控制权上下文
        ///   custom.turntodir &lt;角度&gt;         # 🔴 现行接口 SetMovementDirection(Vec2)（0=+X, 90=+Y，逆时针，单位度）
        ///   custom.turntodir vec &lt;x&gt; &lt;y&gt;     # 同上，直接给向量
        ///   custom.turntodir look &lt;角度&gt;    # 对照：LookDirection(Vec3) —— 🔴 已知转不动
        ///   custom.turntodir angle &lt;角度&gt;   # 对照：LookDirectionAsAngle(单浮点)
        ///
        /// 🔴 **飞行代码用的就是第一种**（`agent.SetMovementDirection(水平单位向量)`）。
        ///    本项目既有用法见 `Story/VisualCommands.cs:512`「强制说话者看向听者」——
        ///    `speakerAgent.SetMovementDirection(dirToListener.AsVec2)`。
        ///    `look` / `angle` 是 2026-09-21 试过但**转不动**的接口，留着手动复现。
        /// 🔴 一律走 `Agent.Main`（玩家）。模板 NPC 请用 `custom.print_npc_move_info` 那类带 agent 参数的命令。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("turntodir", "custom")]
        public static string TurnToDir(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null)
                    return "[TurnDir] no player agent (enter a mission first)";

                string ctx = $"Controller={player.Controller} IsAIControlled={(player.IsAIControlled ? 1 : 0)} IsPaused={(player.IsPaused ? 1 : 0)} IsMine={(player.IsMine ? 1 : 0)}";

                if (args == null || args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return "[TurnDir] " + ctx + " | " + DescribeLook(player)
                         + " | usage: custom.turntodir <deg> | look <deg> | angle <deg> | vec <x> <y>";

                string first = args[0].Trim().ToLowerInvariant();
                string before = DescribeLook(player);

                // vec x y —— 直接给水平向量
                if (first == "vec")
                {
                    if (args.Count < 3
                        || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vx)
                        || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vy))
                        return "[TurnDir] usage: custom.turntodir vec <x> <y>";

                    player.SetMovementDirection(new Vec2(vx, vy));
                    DebugLogger.Log($"[TurnDir] SetMovementDirection <- vec({vx},{vy}) | {ctx}");
                    return $"[TurnDir] SetMovementDirection <- vec({vx},{vy}) | {ctx} | before {before} | after {DescribeLook(player)}";
                }

                // angle <deg> —— 对照接口：单浮点角度
                if (first == "angle")
                {
                    if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float adeg))
                        return "[TurnDir] usage: custom.turntodir angle <deg>";

                    float arad = adeg * (MathF.PI / 180f);
                    player.LookDirectionAsAngle = arad;   // 🔴 单位按弧度试；若结果不对，改成直接喂 adeg
                    DebugLogger.Log($"[TurnDir] LookDirectionAsAngle <- {adeg}deg ({arad}rad) | {ctx}");
                    return $"[TurnDir] LookDirectionAsAngle <- {adeg}deg ({arad}rad) | before {before} | after {DescribeLook(player)}";
                }

                // look <deg> —— 对照接口（🔴 已知转不动，留着手动复现用）
                if (first == "look")
                {
                    if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ldeg))
                        return "[TurnDir] usage: custom.turntodir look <deg>";

                    float lrad = ldeg * (MathF.PI / 180f);
                    player.LookDirection = new Vec3((float)Math.Cos(lrad), (float)Math.Sin(lrad), 0f);
                    DebugLogger.Log($"[TurnDir] LookDirection(对照) <- {ldeg}deg | {ctx}");
                    return $"[TurnDir] LookDirection(对照) <- {ldeg}deg | {ctx} | before {before} | after {DescribeLook(player)}";
                }

                // <deg> —— 🔴 **现行接口 SetMovementDirection**（本项目既有用法：
                //         Story/VisualCommands.cs:512「强制说话者看向听者」）
                if (float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float deg))
                {
                    float rad = deg * (MathF.PI / 180f);
                    var v = new Vec2((float)Math.Cos(rad), (float)Math.Sin(rad));
                    player.SetMovementDirection(v);
                    DebugLogger.Log($"[TurnDir] SetMovementDirection <- {deg}deg ({v.x:F2},{v.y:F2}) | {ctx}");
                    return $"[TurnDir] SetMovementDirection <- {deg}deg ({v.x:F2},{v.y:F2}) | {ctx} | before {before} | after {DescribeLook(player)}";
                }

                // 首参可弃纪律：认不出就当占位符，回落查状态并注明
                return "[TurnDir] " + ctx + " | " + DescribeLook(player)
                     + $" | [note: '{args[0]}' is not an angle -> status] | usage: custom.turntodir <deg> | look <deg> | angle <deg> | vec <x> <y>";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[TurnDir] failed: {ex}");
                return $"[TurnDir] failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 把玩家当前的朝向读出来 —— **三个读数都打**，方便对照"哪个才是真的生效了"。
        /// · <c>MoveDir</c>：<c>GetMovementDirection()</c> —— 🔴 **现行接口 SetMovementDirection 的对应 getter，
        ///   写完之后该变的就是它**（yaw 是从向量反算的，方便跟命令参数对照）
        /// · <c>LookDir</c> / <c>LookDirAsAngle</c>：2026-09-21 试过但转不动的两条，留着对照
        /// 若旋转真的生效，写入之后 <c>MoveDir</c> 那一项应该变；**不变 = 这个接口在当前状态下也没用**
        /// （那时主要怀疑 <c>Controller=AI</c> + <c>IsAIPaused</c> 把原生转向一起冻了）。
        /// </summary>
        private static string DescribeLook(Agent player)
        {
            Vec2 move = player.GetMovementDirection();
            Vec3 look = player.LookDirection;
            float ang = player.LookDirectionAsAngle;
            float mYaw = (float)(Math.Atan2(move.y, move.x) * (180.0 / Math.PI));
            float lYaw = (float)(Math.Atan2(look.y, look.x) * (180.0 / Math.PI));
            return $"MoveDir=({move.x:F2},{move.y:F2}) yaw={mYaw:F0}deg | LookDir=({look.x:F2},{look.y:F2}) yaw={lYaw:F0}deg LookDirAsAngle={ang:F3}";
        }


    }

}


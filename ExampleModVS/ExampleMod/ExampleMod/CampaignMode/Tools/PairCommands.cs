using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LivingWorldNpcs;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    /// <summary>
    /// **双人动作调试台**（2026-10-19）：`custom.pair ...`。
    ///
    /// 纪律：返回文本**纯英文**；首参可弃（旧命令习惯随手填 `1` 当占位 ⇒ 默认回落到 `state`）。
    /// 这个命令族是验收入口，不是玩法入口；玩法入口（交互面板 / 计划动作）后面再接。
    /// </summary>
    public static class PairCommands
    {
        private static bool _verbose;

        /// <summary>与 `PairActionInstance.DefaultApproachMaxDistance` 同值（命令只做展示）。</summary>
        private const float MaxApproachDistance = 4.0f;

        [CommandLineFunctionality.CommandLineArgumentFunction("pair", "custom")]
        public static string Execute(List<string> args)
        {
            string note = string.Empty;
            string sub = args != null && args.Count > 0 ? (args[0] ?? "").Trim().ToLowerInvariant() : "state";
            if (sub.Length > 0 && char.IsDigit(sub[0]))
            {
                note = $" [note: '{sub}' is not a subcommand -> using state]";
                sub = "state";
            }

            try
            {
                switch (sub)
                {
                    case "list": return CmdList() + note;
                    case "verify": return CmdVerify(Arg(args, 1)) + note;
                    case "state": return CmdState() + note;
                    case "stance": return CmdStance(Arg(args, 1), Arg(args, 2)) + note;
                    case "start": return CmdStart(args) + note;
                    case "stop": return CmdStop(args) + note;
                    case "commit": return CmdCommit(Arg(args, 1)) + note;
                    case "bones": return CmdBones() + note;
                    case "npc": return CmdNpc(args) + note;
                    case "victim": return CmdVictim(args) + note;
                    case "handoff": return CmdHandoff(Arg(args, 1), Arg(args, 2)) + note;
                    case "dismount": return CmdDismount(Arg(args, 1)) + note;
                    case "trace": return CmdTrace(Arg(args, 1)) + note;
                    default:
                        return $"pair: unknown subcommand '{sub}' (use: list / verify / state / stance / start / stop / commit / bones / npc / victim / handoff / dismount / trace)";
                }
            }
            catch (Exception ex)
            {
                return $"pair: FAILED ({ex.GetType().Name}: {ex.Message})";
            }
        }

        // ───────────────────────────── 子命令 ─────────────────────────────

        private static string CmdList()
        {
            List<PairActionDef> all = PairActionTable.All();
            if (all.Count == 0)
                return "pair list: 0 actions (PairActions.csv missing or empty)";

            var sb = new StringBuilder();
            sb.Append("pair list: ").Append(all.Count).Append(" action(s)");
            foreach (PairActionDef d in all)
                sb.Append('\n').Append("  ").Append(PairActionTable.Describe(d));
            return sb.ToString();
        }

        private static string CmdVerify(string id)
        {
            List<PairActionDef> targets;
            if (!string.IsNullOrWhiteSpace(id))
            {
                if (!PairActionTable.TryGet(id, out PairActionDef one) || one == null)
                    return $"pair verify: '{id}' not found in PairActions.csv";
                targets = new List<PairActionDef> { one };
            }
            else
            {
                targets = PairActionTable.All();
            }

            if (targets.Count == 0)
                return "pair verify: 0 actions (PairActions.csv missing or empty)";

            var sb = new StringBuilder();
            foreach (PairActionDef d in targets)
            {
                bool ok = PairActionTable.Validate(d, out string error);
                string warn = PairActionTable.Warnings(d);
                sb.Append('\n').Append(ok ? "  OK   " : "  FAIL ").Append(d.Id);
                if (!ok)
                    sb.Append(" -- ").Append(error);
                if (!string.IsNullOrWhiteSpace(warn))
                    sb.Append("  [warn] ").Append(warn);

                sb.Append("  [").Append(PairActionTable.Describe(d)).Append(']');
                sb.Append("  sides: attacker=").Append(DescribeSide(d.Attacker))
                  .Append(" defender=").Append(DescribeSide(d.Defender));
            }
            return sb.ToString();
        }

        private static string DescribeSide(PairSideDef s)
        {
            if (s == null) return "null";
            string detail = s.Play == PairPlayKind.Action ? s.ActionId
                          : s.Play == PairPlayKind.Machine ? s.MachineName
                          : "none";
            string dur = string.Empty;
            if (s.Play == PairPlayKind.Action && !string.IsNullOrWhiteSpace(s.ActionId))
            {
                ActionIndexCache idx = ActionIndexCache.Create(s.ActionId);
                if (idx == ActionIndexCache.act_none || idx.Index <= 0)
                    dur = " -> NOT REGISTERED";
                else
                    dur = " -> ok";
            }
            return $"{s.Play}/{detail}{dur}";
        }

        private static string CmdState()
        {
            PairActionLogic logic = PairActionLogic.Current;
            if (logic == null)
                return "pair state: PairActionLogic not mounted (not in a mission?)";

            // 角色状态（倒地等）不在这里 —— 用 `custom.status state`。
            return logic.DescribeAll();
        }

        private static string CmdStance(string id, string targetId)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "pair stance: usage: custom.pair stance <id> [targetId]";
            if (!PairActionTable.TryGet(id, out PairActionDef def) || def == null)
                return $"pair stance: '{id}' not found in PairActions.csv";
            if (Mission.Current == null || Agent.Main == null)
                return "pair stance: must be in a mission with a main agent";

            Agent defender = ResolveAgent(targetId);
            if (defender == null)
                return "pair stance: no target (aim at someone, or pass a character StringId)";
            if (ReferenceEquals(defender, Agent.Main))
                return "pair stance: target is the player himself";

            if (!PairActionPose.TryComputeStartPose(def, Agent.Main, defender,
                    out Vec3 spot, out float aYaw, out float dYaw, out string why))
                return $"pair stance: FAILED to solve ({why})";

            float dist = PairActionPose.HorizontalDistance(Agent.Main.Position, spot);
            bool tooFar = dist > MaxApproachDistance;
            return $"pair stance {def.Id}: target='{PairActionPose.Describe(defender)}'\n"
                 + $"  stance=({def.Stance.Right:F3}, {def.Stance.Forward:F3}, {def.Stance.DirectionDeg:F1} deg)\n"
                 + $"  attacker spot=({spot.x:F2},{spot.y:F2},{spot.z:F2}) yaw={aYaw:F1} deg"
                 + $" defender yaw={dYaw:F1} deg\n"
                 + $"  distance={dist:F2} m max={MaxApproachDistance:F2} m -> "
                 + (tooFar ? "TOO FAR (walk closer or use 'start ... force')" : "OK");
        }

        private static string CmdStart(List<string> args)
        {
            string id = Arg(args, 1);
            if (string.IsNullOrWhiteSpace(id))
                return "pair start: usage: custom.pair start <id> [force] [end=hold|kill] [targetId]";

            if (!PairActionTable.TryGet(id, out PairActionDef def) || def == null)
                return $"pair start: '{id}' not found in PairActions.csv";
            if (PairActionLogic.Current == null)
                return "pair start: PairActionLogic not mounted (not in a mission?)";
            if (Mission.Current == null || Agent.Main == null)
                return "pair start: must be in a mission with a main agent";

            bool force = false;
            PairEndPolicy? endOverride = null;
            string targetId = null;
            ParseStartFlags(args, 2, out force, out endOverride, out targetId);

            Agent defender = ResolveAgent(targetId);
            if (defender == null)
                return "pair start: no target (aim at someone, or pass a character StringId)";
            if (ReferenceEquals(defender, Agent.Main))
                return "pair start: target is the player himself";

            if (!TryBuildDriver(def, def.Attacker, out IPairSideDriver aDriver, out string whyA))
                return $"pair start: attacker '{def.Attacker.Play}' not supported here ({whyA})";
            if (!TryBuildDriver(def, def.Defender, out IPairSideDriver dDriver, out string whyD))
                return $"pair start: defender '{def.Defender.Play}' not supported here ({whyD})";

            if (!PairActionLogic.Current.Begin(def, Agent.Main, defender, aDriver, dDriver, force, out string why))
                return $"pair start: FAILED ({why})";

            PairActionInstance inst = PairActionLogic.Current.FindByDefId(def.Id);
            if (inst != null && endOverride.HasValue)
                inst.EndPolicyOverride = endOverride;

            return $"pair start: OK '{def.Id}' attacker='{PairActionPose.Describe(Agent.Main)}' defender='{PairActionPose.Describe(defender)}'"
                 + (force ? " [force: approach distance ignored]" : "")
                 + (endOverride.HasValue ? $" [end={endOverride}]" : "")
                 + "\n" + CmdState();
        }

        private static string CmdStop(List<string> args)
        {
            if (PairActionLogic.Current == null)
                return "pair stop: PairActionLogic not mounted";
            if (Agent.Main == null)
                return "pair stop: no main agent";

            bool kill = false;
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    if (((args[i] ?? "").Trim()).Equals("end=kill", StringComparison.OrdinalIgnoreCase))
                        kill = true;
                }
            }

            PairActionInstance inst = PairActionLogic.Current.GetByAgent(Agent.Main);
            if (inst == null)
            {
                Agent focus = ResolveAgent(null);
                if (focus != null)
                    inst = PairActionLogic.Current.GetByAgent(focus);
            }
            if (inst != null)
            {
                if (kill)
                    inst.RequestKill("command");
                else
                    inst.RequestStop("command");

                return $"pair stop: requested on '{inst.Def.Id}' ({(kill ? "kill (skip exit animation)" : "exit animation")})\n"
                     + CmdState();
            }

            // 状态（倒地等）是角色自己的，不归 pair —— 用 `custom.status end|kill`。
            return "pair stop: no active pair for the player / focus (for statuses use 'custom.status end|kill')";
        }

        private static string CmdCommit(string id)
        {
            if (PairActionLogic.Current == null)
                return "pair commit: PairActionLogic not mounted";
            PairActionInstance inst = string.IsNullOrWhiteSpace(id)
                ? (PairActionLogic.Current.GetByAgent(Agent.Main) ?? PairActionLogic.Current.FindByAttacker(Agent.Main))
                : PairActionLogic.Current.FindByDefId(id);
            if (inst == null)
                return $"pair commit: no active pair '{(string.IsNullOrWhiteSpace(id) ? "(player)" : id)}'";

            Agent a = inst.Attacker;
            Agent d = inst.Defender;
            var sb = new StringBuilder();
            sb.Append("pair commit ").Append(inst.Def.Id).Append(':');
            if (a != null) sb.Append(" attacker=").Append(Vec(a.Position));
            if (d != null) sb.Append(" defender=").Append(Vec(d.Position));
            if (a != null && d != null)
            {
                float dist = PairActionPose.HorizontalDistance(a.Position, d.Position);
                Vec3 f = PairActionPose.ForwardOf(a, a.IsMount);
                float yaw = PairActionPose.YawDegOf(f);
                Vec3 rel = d.Position - a.Position;
                float right = Vec3.DotProduct(rel, PairActionPose.RightFromYaw(yaw));
                float forward = Vec3.DotProduct(rel, PairActionPose.ForwardFromYaw(yaw));
                sb.Append('\n').Append($"  relative=({right:F2}, {forward:F2}) dist={dist:F2} m");
            }
            sb.Append('\n').Append("  ").Append(inst.StatusLine());
            return sb.ToString();
        }

        /// <summary>
        /// **逻辑位置 vs 骨骼位置**（步 0 实验用）：同一帧打印两人的 `Agent.Position` 与**骨盆骨世界位置**。
        /// 判据：处决攻击方播完，agent 走 3.68 m、骨盆也走 3.68 m = 引擎把根轨消费掉了（单份）；
        /// 骨盆走 7.36 m = 根轨与 displacement 叠加（双份，要改用 in-place clip）。
        /// </summary>
        private static string CmdBones()
        {
            PairActionLogic logic = PairActionLogic.Current;
            if (logic == null)
                return "pair bones: PairActionLogic not mounted (not in a mission?)";

            var sb = new StringBuilder("pair bones:");
            AppendBone(sb, "player", Agent.Main);
            // 交互演完 pair 就解散了；此时处于状态（倒地）的人由 AgentStatus 持有，单独打一行。
            PairActionInstance inst = logic.GetByAgent(Agent.Main);
            if (inst != null)
            {
                AppendBone(sb, "attacker", inst.Attacker);
                AppendBone(sb, "defender", inst.Defender);
            }
            else
            {
                Agent held = ResolveStatusTarget();
                if (held != null)
                {
                    sb.Append(" (no active pair; status target)");
                    AppendBone(sb, "status", held);
                }
                else
                {
                    sb.Append(" (no pair for the player)");
                }
            }
            return sb.ToString();
        }

        private static void AppendBone(StringBuilder sb, string tag, Agent a)
        {
            if (a == null || !AgentControlHelper.SafeIsActive(a))
                return;
            try
            {
                Vec3 p = a.Position;
                bool ok = false;
                Vec3 w = Vec3.Zero;
                string how = "-";
                if (a.Monster != null)
                    ok = SpellCastInput.TryReadBoneWorld(a, a.Monster.PelvisBoneIndex, 10f, out w, out how);
                sb.Append('\n').Append($"  {tag}='{PairActionPose.Describe(a)}'")
                  .Append($" agent=({p.x:F2},{p.y:F2},{p.z:F2})")
                  .Append(ok ? $" pelvis=({w.x:F2},{w.y:F2},{w.z:F2}) [{how}]" : " pelvis=(read failed)");
            }
            catch (Exception ex)
            {
                sb.Append('\n').Append($"  {tag}: bone read failed ({ex.GetType().Name})");
            }
        }

        private static string CmdNpc(List<string> args)
        {
            string id = Arg(args, 1);
            if (string.IsNullOrWhiteSpace(id))
                return "pair npc: usage: custom.pair npc <id> [targetId] [end=hold|kill]";
            if (!PairActionTable.TryGet(id, out PairActionDef def) || def == null)
                return $"pair npc: '{id}' not found";
            if (PairActionLogic.Current == null || Mission.Current == null)
                return "pair npc: not in a mission";

            ParseStartFlags(args, 2, out _, out PairEndPolicy? endOverride, out string targetId);
            if (!endOverride.HasValue)
                endOverride = PairEndPolicy.Hold;   // 测试命令默认不杀 NPC；要杀请显式 end=kill

            Agent defender = ResolveAgent(targetId);
            if (defender == null) return "pair npc: no defender (aim at someone or pass an id)";
            Agent attacker = FindNearestNpc(defender, defender, Agent.Main, 15f);
            if (attacker == null) return "pair npc: no other human NPC within 15 m to act as attacker";

            if (!TryBuildDriver(def, def.Attacker, out IPairSideDriver aDriver, out string whyA))
                return $"pair npc: attacker side not supported ({whyA})";
            if (!TryBuildDriver(def, def.Defender, out IPairSideDriver dDriver, out string whyD))
                return $"pair npc: defender side not supported ({whyD})";

            if (!PairActionLogic.Current.Begin(def, attacker, defender, aDriver, dDriver, true, out string why))
                return $"pair npc: FAILED ({why})";

            PairActionInstance inst = PairActionLogic.Current.FindByDefId(def.Id);
            if (inst != null) inst.EndPolicyOverride = endOverride;

            return $"pair npc: OK '{def.Id}' attacker='{PairActionPose.Describe(attacker)}' defender='{PairActionPose.Describe(defender)}'"
                 + $" [end={endOverride}]\n" + CmdState();
        }

        private static string CmdVictim(List<string> args)
        {
            string id = Arg(args, 1);
            if (string.IsNullOrWhiteSpace(id))
                return "pair victim: usage: custom.pair victim <id> [attackerId] [end=hold|kill]";
            if (!PairActionTable.TryGet(id, out PairActionDef def) || def == null)
                return $"pair victim: '{id}' not found";
            if (PairActionLogic.Current == null || Mission.Current == null || Agent.Main == null)
                return "pair victim: not in a mission";

            ParseStartFlags(args, 2, out _, out PairEndPolicy? endOverride, out string attackerId);
            if (!endOverride.HasValue)
                endOverride = PairEndPolicy.Hold;   // 玩家当受击方：默认不杀玩家

            Agent attacker = ResolveAgent(attackerId);
            if (attacker == null)
                return "pair victim: no attacker (aim at an NPC or pass an id)";
            if (ReferenceEquals(attacker, Agent.Main))
                return "pair victim: attacker is the player himself";

            if (!TryBuildDriver(def, def.Attacker, out IPairSideDriver aDriver, out string whyA))
                return $"pair victim: attacker side not supported ({whyA})";
            if (!TryBuildDriver(def, def.Defender, out IPairSideDriver dDriver, out string whyD))
                return $"pair victim: defender side not supported ({whyD})";

            if (!PairActionLogic.Current.Begin(def, attacker, Agent.Main, aDriver, dDriver, true, out string why))
                return $"pair victim: FAILED ({why})";

            PairActionInstance inst = PairActionLogic.Current.FindByDefId(def.Id);
            if (inst != null) inst.EndPolicyOverride = endOverride;

            return $"pair victim: OK '{def.Id}' attacker='{PairActionPose.Describe(attacker)}' defender=player"
                 + $" [end={endOverride}]\n" + CmdState();
        }

        private static string CmdHandoff(string fromId, string horseId)
        {
            if (string.IsNullOrWhiteSpace(fromId))
                return "pair handoff: usage: custom.pair handoff <fromId> <horseId>";
            if (PairActionLogic.Current == null)
                return "pair handoff: PairActionLogic not mounted";

            PairActionInstance inst = PairActionLogic.Current.FindByDefId(fromId);
            if (inst == null)
                return $"pair handoff: no active pair '{fromId}'";

            Agent horse = ResolveAgent(horseId);
            if (horse == null) horse = InteractionMissionView.Instance?.GetFocusdAgent();
            if (horse == null || !AgentControlHelper.IsRideableMount(horse))
                return "pair handoff: no rideable horse (aim at one, or pass its character StringId)";

            // 优先走玩法适配器（它会按 CSV 的后继行 EnterEvent 让被扛者进挂接状态）。
            GrappleLogic grapple = GrappleLogic.Current;
            if (grapple != null)
            {
                string result = grapple.CarryPlaceOnHorse(horse);
                if (!result.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    return "pair handoff: " + result + "\n" + CmdState();
            }

            // 运行时级兜底：目标从本行 `HandoffTo` 读（不在代码里写死）。
            string successorId = inst.Def?.HandoffTo;
            if (string.IsNullOrWhiteSpace(successorId))
                return $"pair handoff: pair '{inst.Def?.Id}' has no HandoffTo in PairActions.csv";

            if (!inst.RequestHandoff(successorId, horse, out string why))
                return $"pair handoff: FAILED ({why})";

            return $"pair handoff: requested '{inst.Def.Id}' -> '{successorId}' on '{PairActionPose.Describe(horse)}'\n"
                 + CmdState();
        }

        private static string CmdDismount(string id)
        {
            if (PairActionLogic.Current == null)
                return "pair dismount: PairActionLogic not mounted";
            PairActionInstance inst = string.IsNullOrWhiteSpace(id)
                ? PairActionLogic.Current.FindFirstByStart("handoff")
                : PairActionLogic.Current.FindByDefId(id);
            if (inst == null)
                return "pair dismount: no handoff-started pair active";

            // 优先走玩法适配器（它会按 CSV 的 ExitEvent 让被扛者回站缚）。
            GrappleLogic grapple = GrappleLogic.Current;
            if (grapple != null)
            {
                string result = grapple.CarryTakeOffHorse();
                if (!result.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    return "pair dismount: " + result + "\n" + CmdState();
            }

            // 运行时级兜底：先放到坐骑右手侧地面（Z 由引擎按地形重算），再请求结束。
            try
            {
                Agent horse = inst.Attacker;
                Agent carried = inst.Defender;
                if (horse != null && carried != null && AgentControlHelper.SafeIsActive(horse))
                {
                    Vec3 f = PairActionPose.ForwardOf(horse, true);
                    Vec3 right = PairActionPose.RightFromYaw(PairActionPose.YawDegOf(f));
                    Vec3 p = horse.Position + right * 1.0f;
                    p.z = carried.Position.z;
                    carried.TeleportToPosition(p);
                }
            }
            catch (Exception) { }

            inst.RequestStop("dismount");
            return "pair dismount: requested\n" + CmdState();
        }

        private static string CmdTrace(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _verbose = value.Trim() == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase);
            return $"pair trace: {(_verbose ? "on" : "off")} (verbose flag; core logging is always on)";
        }

        // ───────────────────────────── 工具 ─────────────────────────────

        private static string Arg(List<string> args, int i)
            => args != null && i >= 0 && i < args.Count && !string.IsNullOrWhiteSpace(args[i])
                ? args[i].Trim()
                : null;

        /// <summary>解析 `[targetId] [force] [end=hold|kill]`（从 startIndex 起；顺序不限）。</summary>
        private static void ParseStartFlags(List<string> args, int startIndex,
            out bool force, out PairEndPolicy? endOverride, out string targetId)
        {
            force = false;
            endOverride = null;
            targetId = null;
            if (args == null)
                return;

            for (int i = startIndex; i < args.Count; i++)
            {
                string a = (args[i] ?? "").Trim();
                if (a.Length == 0) continue;
                if (a.Equals("force", StringComparison.OrdinalIgnoreCase)) { force = true; continue; }
                if (a.StartsWith("end=", StringComparison.OrdinalIgnoreCase))
                {
                    string v = a.Substring(4).Trim().ToLowerInvariant();
                    if (v == "hold") endOverride = PairEndPolicy.Hold;
                    else if (v == "kill") endOverride = PairEndPolicy.KillDefender;
                    continue;
                }
                if (targetId == null) targetId = a;
            }
        }

        /// <summary>状态命令的目标：优先"瞄着的那个人"，否则最近一个处于状态里的人。</summary>
        private static Agent ResolveStatusTarget()
        {
            AgentStatus status = AgentStatus.Current;
            if (status == null)
                return null;

            Agent focus = ResolveAgent(null);
            if (focus != null && status.IsInStatus(focus))
                return focus;

            return status.MostRecent();
        }

        /// <summary>按角色 StringId 找 agent；空 = 当前焦点 / 上一次焦点。状态命令也用它。</summary>
        internal static Agent ResolveAgent(string idOrNull)
        {
            if (Mission.Current == null)
                return null;

            if (!string.IsNullOrWhiteSpace(idOrNull))
            {
                Agent byId = Mission.Current.Agents.FirstOrDefault(
                    a => a != null && a.Character != null
                         && string.Equals(a.Character.StringId, idOrNull, StringComparison.OrdinalIgnoreCase));
                if (byId != null)
                    return byId;
            }

            // 交互焦点 = 唯一口径（`GetAimedAgent`：当帧焦点 → 上一帧缓存）
            return InteractionMissionView.Instance?.GetAimedAgent();
        }

        private static Agent FindNearestNpc(Agent around, Agent exclude1, Agent exclude2, float maxDist)
        {
            Agent best = null;
            float bestD = maxDist;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a == null || !AgentControlHelper.SafeIsActive(a)) continue;
                if (!a.IsHuman || a.IsMount) continue;
                if (ReferenceEquals(a, exclude1) || ReferenceEquals(a, exclude2)) continue;
                float d = a.Position.Distance(around.Position);
                if (d < bestD) { bestD = d; best = a; }
            }
            return best;
        }

        private static bool TryBuildDriver(PairActionDef def, PairSideDef side,
            out IPairSideDriver driver, out string why)
        {
            driver = null;
            why = "-";
            if (side == null)
            {
                why = "side is null";
                return false;
            }

            switch (side.Play)
            {
                case PairPlayKind.Action:
                    // 一次性动作等 clip 完整播完（余量 0）；否则处决的收尾（倒地）会被切掉。
                    driver = new DirectActionDriver(side, 0f);
                    return true;
                case PairPlayKind.Machine:
                    // 通用状态机驱动：按名字创建已注册的机器，用 Enter/ExitEvent 进出。
                    driver = new MachinePairDriver(side.MachineName, def.EnterEvent, def.ExitEvent);
                    return true;
                case PairPlayKind.None:
                    driver = new NoopPairSideDriver();
                    return true;
                default:
                    why = "unknown play kind";
                    return false;
            }
        }

        private static string Vec(Vec3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}

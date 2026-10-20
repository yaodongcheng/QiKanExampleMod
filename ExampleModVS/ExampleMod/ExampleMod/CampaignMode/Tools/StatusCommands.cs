using System;
using System.Collections.Generic;
using LivingWorldNpcs;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    /// <summary>
    /// **角色状态调试台**（2026-10-20）：`custom.status ...`。
    ///
    /// 状态是**角色自己的**（双人动作演完就解散，人还躺在状态里）⇒ 开关状态的命令归这里，
    /// 不归 `custom.pair`。当前唯一状态 = `downed`（被处决倒地），以后会有更多。
    ///
    /// 纪律：返回文本**纯英文**；首参可弃（数字 / 未知 → 回落到 `state`）。
    /// </summary>
    public static class StatusCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("status", "custom")]
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
                    case "state": return CmdState() + note;
                    case "end": return CmdEnd(Arg(args, 1), kill: false) + note;
                    case "kill": return CmdEnd(Arg(args, 1), kill: true) + note;
                    default:
                        return $"status: unknown subcommand '{sub}' (use: state / end / kill)";
                }
            }
            catch (Exception ex)
            {
                return $"status: FAILED ({ex.GetType().Name}: {ex.Message})";
            }
        }

        // ───────────────────────────── 子命令 ─────────────────────────────

        private static string CmdState()
        {
            AgentStatus status = AgentStatus.Current;
            if (status == null)
                return "status: AgentStatus not mounted (not in a mission?)";

            string text = status.DescribeAll();
            return string.IsNullOrEmpty(text) ? "status: none active" : text;
        }

        /// <summary>`custom.status end`（演退出动画）/ `custom.status kill`（直接结算）。</summary>
        private static string CmdEnd(string targetId, bool kill)
        {
            AgentStatus status = AgentStatus.Current;
            if (status == null)
                return "status: AgentStatus not mounted (not in a mission?)";
            if (Mission.Current == null)
                return "status: not in a mission";

            Agent target = ResolveTarget(status, targetId);
            if (target == null)
                return "status: no target (aim at someone in a status, or pass a character StringId)";

            string id = status.StatusIdOf(target) ?? "?";
            bool ok = kill ? status.RequestKill(target, "command") : status.RequestEnd(target);
            if (!ok)
                return $"status: '{PairActionPose.Describe(target)}' is not in a status";

            return $"status: {PairActionPose.Describe(target)} [{id}] -> "
                 + (kill ? "finished (killed, no exit animation)" : "ending (exit animation)")
                 + "\n" + CmdState();
        }

        // ───────────────────────────── 工具 ─────────────────────────────

        /// <summary>
        /// 目标：显式 id &gt; 瞄着的人 &gt; 最近一个处于状态的人。
        /// 显式给了 id 但他不在状态里 ⇒ 返回 null（不偷偷换成别人）。
        /// </summary>
        private static Agent ResolveTarget(AgentStatus status, string idOrNull)
        {
            Agent byId = PairCommands.ResolveAgent(idOrNull);
            if (!string.IsNullOrWhiteSpace(idOrNull))
                return byId != null && status.IsInStatus(byId) ? byId : null;

            if (byId != null && status.IsInStatus(byId))
                return byId;
            return status.MostRecent();
        }

        private static string Arg(List<string> args, int i)
            => args != null && i >= 0 && i < args.Count && !string.IsNullOrWhiteSpace(args[i])
                ? args[i].Trim()
                : null;
    }
}

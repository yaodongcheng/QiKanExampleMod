using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// 身高体检指令（2026-09-16）。游戏内 `~` 控制台调用（**必须先进入场景**，大地图不行）：
    ///   custom.height              # 报【玩家】的身高
    ///   custom.height all          # 报【最近的 10 个 agent】—— 拿我们的角色和原版 NPC 并排对照
    ///   custom.height &lt;名字子串&gt; # 按名字匹配（例：custom.height nobunaga）
    ///
    /// 🔴 为什么要有它：用户报「我们的角色都是 1.5 / 1.6 的小矮人」。
    ///    离线只能量到**绑定姿态**下的坐标（我们的头：眼球中心 1.6839 = 原版基准；甲 bbox z −0.04~1.76），
    ///    量不到「游戏里实际渲染出来多高」——因为角色的最终尺寸 = 骨架 × `Agent.AgentScale`
    ///    （由 BodyProperties 里编码的体型决定），而 `AgentScale` **只有运行期读得到**。
    ///    这条指令就是把它连同眼睛高度一起打出来，跟旁边站着的原版 NPC 直接比。
    ///
    /// 🔴 输出全英文（CLAUDE.md 工作流约定：控制台返回文本禁止中文）。
    /// 🔴 首参【可弃占位】：骑砍2 的 CommandLineArgumentFunction 在完全不填参数时可能根本不触发，
    ///    所以习惯随手给个 "1" —— 解析不出来一律回落到默认目标并注明，**绝不报错**。
    /// </summary>
    public class HeightReportCommands
    {
        // 原版基准（绑定位姿，从 core_game dump 的 head_male_a / head_female_a 量得）：
        //   眼高 1.6839（男）/ 1.6795（女）；眼高 ÷ 头顶高 = 0.930（男 1.6839/1.8107）
        private const float VanillaEyeHeightMale = 1.6839f;
        private const float VanillaEyeTopRatio = 0.930f;

        [CommandLineFunctionality.CommandLineArgumentFunction("height", "custom")]
        public static string HeightReport(List<string> args)
        {
            Mission mission = Mission.Current;
            if (mission == null)
                return "Error: no mission. Enter a scene first (this command reads agent runtime data, not the campaign map).";

            string raw = (args != null && args.Count >= 1) ? (args[0] ?? string.Empty).Trim() : string.Empty;
            var agents = new List<Agent>();
            string mode;

            if (raw.Length == 0 || raw == "1")
            {
                // 裸命令 / 占位符 → 玩家自己
                Agent me = Agent.Main;
                if (me == null)
                    return "Error: player agent not found (no Agent.Main in this mission).";
                agents.Add(me);
                mode = "player" + (raw == "1" ? " [note: '1' is a placeholder -> using player]" : string.Empty);
            }
            else if (raw.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Agent me = Agent.Main;
                Vec3 origin = me != null ? me.Position : Vec3.Zero;
                agents = mission.Agents
                    .Where(a => a != null && a.IsHuman && a.IsActive())
                    .OrderBy(a => a.Position.DistanceSquared(origin))
                    .Take(10)
                    .ToList();
                mode = "nearest 10 agents (nearest first)";
            }
            else
            {
                agents = mission.Agents
                    .Where(a => a != null && a.IsHuman && a.IsActive())
                    .Where(a => !string.IsNullOrEmpty(a.Name) && a.Name.IndexOf(raw, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(10)
                    .ToList();
                if (agents.Count == 0)
                {
                    Agent me = Agent.Main;
                    if (me == null)
                        return "Error: no agent matches '" + raw + "' and no player agent.";
                    agents.Add(me);
                    mode = "player [note: no agent name contains '" + raw + "' -> using player]";
                }
                else
                {
                    mode = "name contains '" + raw + "'";
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== height report (" + mode + ") ===");
            sb.AppendLine("  name                      scale   eyeH(m)  charId");
            foreach (Agent a in agents)
            {
                string nm = a.Name ?? "(null)";
                if (nm.Length > 24) nm = nm.Substring(0, 24);
                string cid = (a.Character != null) ? a.Character.StringId : "-";
                sb.AppendLine(string.Format("  {0,-24} {1,6:F3}  {2,7:F4}  {3}",
                    nm, a.AgentScale, a.GetEyeGlobalHeight(), cid));
            }

            // 参考行：拿最近的一个 agent 换算"估算总高"，并给出原版基准
            Agent first = agents.FirstOrDefault();
            if (first != null)
            {
                float eye = first.GetEyeGlobalHeight();
                float scale = first.AgentScale;
                sb.AppendLine(string.Format(
                    "  est.total height of [{0}] = eyeH / 0.930 = {1:F3} m   (AgentScale {2:F3})",
                    first.Name ?? "?", eye / VanillaEyeTopRatio, scale));
            }
            sb.AppendLine(string.Format(
                "  ref: vanilla head eye height (bind pose) = {0:F4} m ; eye/top ratio = {1:F3}",
                VanillaEyeHeightMale, VanillaEyeTopRatio));
            sb.Append("  note: run 'custom.height all' inside a scene with both our lords and vanilla NPCs to compare.");
            return sb.ToString();
        }
    }
}

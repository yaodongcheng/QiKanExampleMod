using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LivingWorldNpcs.Combat;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// 控制台入口 <c>custom.ice</c>（2026-10-07）—— **冰冻效果探针**，
    /// 本体在 <see cref="LivingWorldNpcs.Combat.IceFxBehavior"/>（`Combat/IceFxBehavior.cs`，文件头有完整原理）。
    ///
    /// <code>
    /// custom.ice                      查参数 + 用法（首参可弃：认不出的当目标，默认做 probe）
    /// custom.ice list    [半径]        列出身旁的人：编号 / 名字 / 距离（默认 20m，准星指着的那个会标出来）
    /// custom.ice probe   [目标]        只查不改：他身上能枚举到几个网格、材质叫什么、骨架/定格状态
    /// custom.ice freeze  on|off [目标]  只定格动作（暂停骨架动画 + 停 AI；目标是玩家时另冻控制权）
    /// custom.ice frost   on|off [目标]  只换材质（自发光 + 换 diffuse 贴图），可单独验
    /// custom.ice on      [目标]        freeze on + frost on
    /// custom.ice off     [目标]        还原（**不写目标 = 全部还原**，当"取消"用）
    /// custom.ice tint    R G B [强度]   冰的颜色（0~1）与自发光强度（0 = 不要发光）
    /// custom.ice tex     &lt;名字|none&gt;   冰贴图来源：贴图资源名或材质资源名（取它的 diffuse）
    /// </code>
    ///
    /// **怎么指定"哪个士兵"**（三选一）：
    ///   · **不写** —— 先看**准星指着谁**，没指着人就退到"最近的其他人"（多数时候够用）；
    ///   · `#12` 或 `12` —— 按 agent 编号（编号哪来？敲 `custom.ice list` 看）；
    ///   · `me` —— 玩家自己。
    ///
    /// 🔴 **一次实机跑完要看的四件事**（顺序即判据）：
    ///   ① `probe` 的 `subMeshes=0` ⇒ 角色网格枚举不到 ⇒ 这条路径作废（改走 ReplaceMeshWithMesh）；
    ///   ② `freeze on` 后**只有他停**，旁边的人照常走打；
    ///   ③ `frost on` 后他变了 —— 🔴 **同时看旁边穿同款甲的人有没有跟着变**（跟着变 = 网格共享，路子作废）；
    ///   ④ `off` 之后材质/动作**逐项回到原样**（有没有回不去的残留）。
    /// </summary>
    public static class IceFxCommands
    {
        /// <summary>准星射线打这么远（米）。</summary>
        private const float LookRayLength = 80f;

        [CommandLineFunctionality.CommandLineArgumentFunction("ice", "custom")]
        public static string ExecuteIce(List<string> args)
        {
            if (Mission.Current == null)
                return "error: must be in a scene/mission to use this command.";

            IceFxBehavior beh = IceFxBehavior.Instance;
            if (beh == null)
                return "error: IceFxBehavior is not registered in this mission (check MySubModule.OnMissionBehaviorInitialize).";

            List<string> a = args ?? new List<string>();
            string verb = "status";
            int targetIdx = 1;
            string note = string.Empty;

            if (a.Count >= 1 && !string.IsNullOrWhiteSpace(a[0]))
            {
                switch (a[0].Trim().ToLowerInvariant())
                {
                    case "probe": case "freeze": case "frost": case "head":
                    case "on": case "off": case "tint": case "tex": case "list": case "status":
                        verb = a[0].Trim().ToLowerInvariant();
                        break;
                    default:
                        // 首参可弃纪律：认不出的当目标，默认做 probe，并注明
                        verb = "probe";
                        targetIdx = 0;
                        note = $" [note: '{a[0]}' is not a verb -> treated as target for 'probe']";
                        break;
                }
            }

            switch (verb)
            {
                case "status":
                    return "OK (status). " + beh.Describe() + note;

                case "list":
                {
                    float radius = 20f;
                    if (a.Count > targetIdx && !string.IsNullOrWhiteSpace(a[targetIdx]))
                    {
                        if (TryFloat(a[targetIdx], out float rv) && rv > 0f) radius = rv;
                        else note += $" [note: '{a[targetIdx]}' is not a radius -> using {radius:F0}m]";
                    }
                    return ListNearby(radius, beh) + note;
                }

                case "probe":
                {
                    Agent t = ResolveTarget(a, targetIdx, out string tnote);
                    if (t == null) return "error: no target agent found (nobody else nearby?)." + note;
                    return beh.Probe(t) + tnote + note;
                }

                case "freeze":
                case "frost":
                case "head":
                {
                    bool on = true;
                    int idx = targetIdx + 1;
                    if (a.Count > targetIdx && !string.IsNullOrWhiteSpace(a[targetIdx]))
                    {
                        string s = a[targetIdx].Trim().ToLowerInvariant();
                        if (s == "on" || s == "1" || s == "true") { on = true; idx = targetIdx + 1; }
                        else if (s == "off" || s == "0" || s == "false") { on = false; idx = targetIdx + 1; }
                        else
                        {
                            // 认不出的当目标，状态默认 on
                            idx = targetIdx;
                            note += $" [note: '{a[targetIdx]}' is not on/off -> {verb} on]";
                        }
                    }

                    // 「off 且没给目标」= 把这一类全部还原（当"取消"用，避免转身就指错人）
                    bool targetGiven = a.Count > idx && !string.IsNullOrWhiteSpace(a[idx]);
                    if (!on && !targetGiven)
                    {
                        if (verb == "head") return beh.RemoveAllHeadShells() + note;
                        return beh.RestoreAll(freeze: verb == "freeze", frost: verb == "frost") + note;
                    }

                    Agent t = ResolveTarget(a, idx, out string tnote);
                    if (t == null) return "error: no target agent found (nobody else nearby?)." + note;

                    string r = verb == "freeze" ? beh.SetFreeze(t, on)
                             : verb == "frost" ? beh.SetFrost(t, on)
                             : beh.SetHeadShell(t, on);
                    return r + tnote + note;
                }

                case "on":
                {
                    // 🔴 不含冰头：`head` 是实验件（形状对不上、缩不了），要就单独敲 `custom.ice head on`
                    Agent t = ResolveTarget(a, targetIdx, out string tnote);
                    if (t == null) return "error: no target agent found (nobody else nearby?)." + note;
                    return beh.SetFreeze(t, true) + " || " + beh.SetFrost(t, true) + tnote + note;
                }

                case "off":
                {
                    // 不写目标 = 全部还原（同 freeze off / frost off 的口径）
                    if (a.Count <= targetIdx || string.IsNullOrWhiteSpace(a[targetIdx]))
                        return beh.RestoreAll(freeze: true, frost: true) + note;

                    Agent t = ResolveTarget(a, targetIdx, out string tnote);
                    if (t == null) return "error: no target agent found (nobody else nearby?)." + note;
                    return beh.SetFreeze(t, false) + " || " + beh.SetFrost(t, false) + " || " + beh.SetHeadShell(t, false)
                         + tnote + note;
                }

                case "tint":
                {
                    float r, g, b, glow = beh.Glow;
                    if (a.Count < 4 ||
                        !TryFloat(a[1], out r) || !TryFloat(a[2], out g) || !TryFloat(a[3], out b))
                    {
                        return "OK. " + beh.Describe()
                             + " [note: usage 'custom.ice tint R G B [glow]', R/G/B in 0..1]" + note;
                    }
                    if (a.Count >= 5 && TryFloat(a[4], out float gv)) glow = gv;
                    return beh.SetTint(r, g, b, glow) + note;
                }

                case "tex":
                {
                    if (a.Count < 2 || string.IsNullOrWhiteSpace(a[1]))
                        return "OK. " + beh.Describe() + " [note: usage 'custom.ice tex <resourceName|none>']" + note;
                    return beh.SetTex(a[1]) + note;
                }
            }

            return "OK (status). " + beh.Describe() + note;
        }

        // ── 身旁点名册 ────────────────────────────────────────────

        /// <summary>
        /// 列出身旁的人（编号 / 名字 / 距离）—— **"我要冻第几号"就靠这个**。
        /// 准星正指着的那个会标 `&lt;== crosshair`，被本命令处理过的会带 `[frozen]` / `[frosted]`。
        /// </summary>
        private static string ListNearby(float radius, IceFxBehavior beh)
        {
            Mission m = Mission.Current;
            Agent me = Agent.Main;
            if (m == null || me == null) return "error: no main agent.";

            Agent looked = FindLookedAt();
            List<KeyValuePair<float, Agent>> list = new List<KeyValuePair<float, Agent>>();
            foreach (Agent ag in m.Agents)
            {
                if (ag == null || ag == me) continue;
                if (!ag.IsActive() || !ag.IsHuman) continue;
                float d = ag.Position.Distance(me.Position);
                if (d > radius) continue;
                list.Add(new KeyValuePair<float, Agent>(d, ag));
            }
            list.Sort((x, y) => x.Key.CompareTo(y.Key));

            if (list.Count == 0)
                return $"OK (list). nobody else within {radius:F0}m -- walk closer or raise the radius (e.g. 'custom.ice list 50').";

            StringBuilder sb = new StringBuilder();
            sb.Append("OK (list). ").Append(list.Count).Append(" agent(s) within ").Append(radius.ToString("F0")).Append("m");
            if (looked != null) sb.Append(" | crosshair -> #").Append(looked.Index).Append(" '").Append(looked.Name).Append("'");
            sb.Append(" || ");

            int shown = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (shown >= 12) { sb.Append("+").Append(list.Count - shown).Append(" more "); break; }
                Agent ag = list[i].Value;
                sb.Append('#').Append(ag.Index).Append(" '").Append(ag.Name).Append("' ").Append(list[i].Key.ToString("F1")).Append("m");
                string tag = beh != null ? beh.TagOf(ag) : null;
                if (!string.IsNullOrEmpty(tag)) sb.Append(" [").Append(tag).Append(']');
                if (ag == looked) sb.Append(" <== crosshair");
                sb.Append(" | ");
                shown++;
            }
            sb.Append("| target syntax: '#<index>' / 'me' / (omitted = crosshair then nearest)");
            return sb.ToString();
        }

        /// <summary>
        /// 准星指着谁 —— 相机原点沿视线打一条 80m 的射线问引擎（`Mission.RayCastForClosestAgent`）。
        /// 方向走 <see cref="CameraLook.TryGet"/>（铁律 35：接管期间只有它算得对），
        /// 位置照读 `Mission.GetCameraFrame().origin`（位置两种相机下都对）。
        /// </summary>
        private static Agent FindLookedAt()
        {
            Mission m = Mission.Current;
            Agent me = Agent.Main;
            if (m == null || me == null) return null;
            try
            {
                Vec3 forward;
                if (!CameraLook.TryGet(out forward)) return null;
                Vec3 origin = m.GetCameraFrame().origin;
                Vec3 finish = origin + forward * LookRayLength;
                float dist;
                return m.RayCastForClosestAgent(origin, finish, out dist, me.Index, 0.15f);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[IceFx] 准星射线异常: {ex.Message}");
                return null;
            }
        }

        // ── 目标选择 ──────────────────────────────────────────────

        /// <summary>
        /// 目标写法：`me` / `player` / `main` = 玩家；`#12` 或 `12` = 按 Agent.Index；
        /// `look` = 准星指着的人；不写 / 认不出 = **准星 → 最近的其他人**（都没有才回落玩家，并注明）。
        /// </summary>
        private static Agent ResolveTarget(List<string> a, int idx, out string note)
        {
            note = string.Empty;
            string tok = (a != null && a.Count > idx && !string.IsNullOrWhiteSpace(a[idx])) ? a[idx].Trim() : null;
            bool explicitLook = false;

            if (!string.IsNullOrEmpty(tok))
            {
                string low = tok.ToLowerInvariant();
                if (low == "me" || low == "player" || low == "main" || low == "self")
                    return Agent.Main;

                if (low == "look" || low == "aim" || low == "crosshair")
                    explicitLook = true;
                else
                {
                    string num = low.StartsWith("#") ? low.Substring(1) : low;
                    if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    {
                        foreach (Agent ag in Mission.Current.Agents)
                        {
                            if (ag != null && ag.IsActive() && ag.Index == n) return ag;
                        }
                        note = $" [note: no active agent with index {n} -> using crosshair/nearest]";
                    }
                    else if (low != "near" && low != "nearest")
                    {
                        note = $" [note: '{tok}' is not a target -> using crosshair/nearest]";
                    }
                }
            }

            // 准星优先（不写目标时也走这条路）—— "冻住我面前这个"是最自然的手感
            Agent looked = FindLookedAt();
            if (looked != null && looked != Agent.Main) return looked;
            if (explicitLook)
                note += " [note: nothing under the crosshair -> using nearest]";

            Agent nearest = FindNearestOther();
            if (nearest != null) return nearest;

            note += " [note: nobody else nearby -> using main agent]";
            return Agent.Main;
        }

        private static Agent FindNearestOther()
        {
            Mission m = Mission.Current;
            Agent from = Agent.Main;
            if (m == null || from == null) return null;

            Agent best = null;
            float bestD = float.MaxValue;
            foreach (Agent ag in m.Agents)
            {
                if (ag == null || ag == from) continue;
                if (!ag.IsActive() || !ag.IsHuman) continue;
                if (ag.AgentVisuals == null || !ag.AgentVisuals.IsValid()) continue;
                float d = ag.Position.Distance(from.Position);
                if (d < bestD) { bestD = d; best = ag; }
            }
            return best;
        }

        private static bool TryFloat(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}

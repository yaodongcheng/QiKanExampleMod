using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using TaleWorlds.Library;

namespace LivingWorldNpcs.Animation
{
    /// <summary>
    /// **状态机定义从 XML 装载**（2026-09-25 立）—— 让"游戏加载的那份定义"和"可视化页读的那份定义"
    /// 是**同一个文件**，并且**改状态机不用重编译**。
    ///
    /// 🔴 **分工**（这是这次改造的核心约定）：
    ///   · **结构进 XML**：状态、动作名、循环性、时长、边的来源/目标/优先级、blend、边型、族名；
    ///   · **判据留 C#**：XML 里只写谓词名（`when="sprinting"`），真身在
    ///     <see cref="AnimConditions"/> 注册（见那边的说明——自己写表达式解析器 = 多一种静默失败）。
    ///   · **运行时旋钮留 C#**：默认过渡时长 / 核对周期 / 动作优先级仍是委托，指向各自的 Tuning 字段
    ///     （`custom.flight tune blend` 那些热调键继续有效）。**它们是"运行期调参"，不是结构。**
    ///
    /// 🔴 **校验是硬要求**：XML 里写错一个字，症状是"这条边永远不生效"——最难查的那种。
    ///   所以装载时**逐条校验并一次性报出全部问题**（状态名/目标/族成员/谓词名/数字），
    ///   校验不过**不注册**（状态机退回空机器：动画不播、其它照常，而不是带着半张错表跑）。
    ///
    /// 格式（与 `ModuleData/statemachine_flight.xml` 对照着看）：
    /// <code>
    /// &lt;state_machine name="flight"&gt;
    ///   &lt;families&gt;
    ///     &lt;family name="Prone" entry="fastmove"&gt;fastmove fastmoveStart dodgeL&lt;/family&gt;
    ///       ← 成员空格分隔；**entry = 容器（子状态机）的真实入口**：进容器落到哪个状态
    ///   &lt;/families&gt;
    ///   &lt;states&gt;
    ///     &lt;state name="idle" act="act_fly_idle"/&gt;                                ← 不带 duration = 循环
    ///     &lt;state name="fastmoveStart" act="act_fly_fastmove_start" duration="boostStartSeconds"/&gt;
    ///   &lt;/states&gt;
    ///   &lt;edges&gt;
    ///     &lt;edge from="Prone" to="dodgeL" when="dodge-left" blend="0.12"/&gt;   ← from = 状态 / 容器（容器 = 它里面任何状态）
    ///     &lt;edge from="outside" to="Prone" phase="true"/&gt;                    ← to = 状态 / 容器（容器 = 进容器，装载期解析成它的 entry）
    ///     &lt;edge from="Prone" to="fastmove" when="sprinting" after-finish="true"/&gt;
    ///   &lt;/edges&gt;
    /// &lt;/state_machine&gt;
    /// </code>
    /// `duration` / `blend` 既可以是数字（`1.033`），也可以是**命名标量**（`boostStartSeconds`，见
    /// <see cref="AnimConditions.RegisterParam"/>）。
    /// </summary>
    public static class AnimMachineLoader
    {
        /// <summary>
        /// 装载一台状态机。成功返回定义（调用方自己去 <see cref="AnimMachineRegistry.Register"/>）；
        /// **失败返回 null**，并把全部问题写进 <paramref name="error"/>（已同时打日志）。
        /// </summary>
        public static AnimMachineDef Load(string path, out string error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = "定义文件不存在: " + path;
                DebugLogger.Log("[Anim] " + error);
                return null;
            }

            XmlDocument doc = new XmlDocument();
            try
            {
                doc.Load(path);
            }
            catch (Exception ex)
            {
                error = "XML 解析失败: " + ex.Message;
                DebugLogger.Log("[Anim] " + error + "（" + path + "）");
                return null;
            }

            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "state_machine")
            {
                error = "根节点必须是 <state_machine>";
                DebugLogger.Log("[Anim] " + error + "（" + path + "）");
                return null;
            }

            string name = Attr(root, "name");
            if (string.IsNullOrEmpty(name))
            {
                error = "<state_machine> 缺 name 属性";
                DebugLogger.Log("[Anim] " + error);
                return null;
            }

            var problems = new List<string>();
            var families = new Dictionary<string, string[]>(StringComparer.Ordinal);
            // 🔴 容器（子状态机）的**真实入口**：进容器落到哪个状态。
            //    有了它，边就能直接指向容器（`to="Upright"`），**装载期**解析成这个状态
            //    ⇒ 运行时看到的仍是具体状态名，状态机热路径一行都不用改。
            var familyEntry = new Dictionary<string, string>(StringComparer.Ordinal);
            var states = new List<AnimState>();

            // ── 族 ──
            foreach (XmlNode node in root.SelectNodes("families/family"))
            {
                string famName = Attr(node, "name");
                if (string.IsNullOrEmpty(famName))
                {
                    problems.Add("<family> 缺 name");
                    continue;
                }
                if (families.ContainsKey(famName))
                {
                    problems.Add("族名重复: " + famName);
                    continue;
                }
                string[] members = (node.InnerText ?? "").Split(new[] { ' ', '\t', '\r', '\n' },
                                                                 StringSplitOptions.RemoveEmptyEntries);
                if (members.Length == 0)
                {
                    problems.Add("容器 '" + famName + "' 一个成员都没有");
                }
                families[famName] = members;
                string entryAttr = Attr(node, "entry");
                if (!string.IsNullOrEmpty(entryAttr)) familyEntry[famName] = entryAttr;
            }

            // ── 状态 ──
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode node in root.SelectNodes("states/state"))
            {
                string sn = Attr(node, "name");
                string act = Attr(node, "act");
                if (string.IsNullOrEmpty(sn) || string.IsNullOrEmpty(act))
                {
                    problems.Add("<state> 缺 name 或 act");
                    continue;
                }
                if (!seen.Add(sn))
                {
                    problems.Add("状态名重复: " + sn);
                    continue;
                }
                float duration = 0f;
                string durRaw = Attr(node, "duration");
                string nxt = Attr(node, "next");
                bool oneShot = !string.IsNullOrEmpty(durRaw);
                if (oneShot && !ResolveNumber(durRaw, out duration))
                {
                    problems.Add("状态 '" + sn + "' 的 duration='" + durRaw + "' 既不是数字、也不是已登记的命名标量");
                    continue;
                }
                if (!oneShot && !string.IsNullOrEmpty(nxt))
                {
                    problems.Add("状态 '" + sn + "' 是循环状态，不该写 next（next 只给一次性动作用）");
                    continue;
                }
                // 🔴 next = **演完兜底**：一次性动作播完、且没有任何边命中时回这里
                //    （条件优先、演完兜底 —— 所以入姿演完那一刻玩家已松手就会走普通边）
                states.Add(oneShot
                    ? AnimState.Once(sn, act, next: string.IsNullOrEmpty(nxt) ? null : nxt, duration: duration)
                    : AnimState.Loop(sn, act));
            }
            if (states.Count == 0)
            {
                problems.Add("一个状态都没解析出来");
            }
            var stateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (AnimState s in states)
            {
                stateNames.Add(s.Name);
            }

            // ── next 的目标必须是已声明的状态 ──
            foreach (AnimState st in states)
            {
                if (!string.IsNullOrEmpty(st.Next) && !stateNames.Contains(st.Next))
                {
                    problems.Add("状态 '" + st.Name + "' 的 next='" + st.Next + "' 不是已声明的状态");
                }
            }

            // ── 族成员必须是已声明的状态 ──
            foreach (KeyValuePair<string, string[]> kv in families)
            {
                foreach (string m in kv.Value)
                {
                    if (!stateNames.Contains(m))
                    {
                        problems.Add("容器 '" + kv.Key + "' 里的 '" + m + "' 不是已声明的状态");
                    }
                }
                if (familyEntry.TryGetValue(kv.Key, out string ent) && Array.IndexOf(kv.Value, ent) < 0)
                {
                    problems.Add("容器 '" + kv.Key + "' 的 entry='" + ent + "' 不是它的成员");
                }
            }

            // ── 边（**顺序 = 优先级**，按 XML 里的先后）──
            var edges = new List<AnimEdgeDef>();
            int idx = 0;
            foreach (XmlNode node in root.SelectNodes("edges/edge"))
            {
                idx++;
                string from = Attr(node, "from");
                string to = Attr(node, "to");
                string when = Attr(node, "when");
                string label = "#" + idx + " (" + from + " -> " + to + ")";

                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                {
                    problems.Add(label + " 缺 from / to");
                    continue;
                }
                bool phaseAttr = string.Equals(Attr(node, "phase"), "true", StringComparison.OrdinalIgnoreCase);
                // 🔴 `outside` = **机外**（非飞行那个外部状态）：只允许出现在**相位驱动**边上
                //    （起飞进机 = outside → hoverstart；落地出机 = superland → outside）。
                //    状态机不求值它，所以运行时零行为变化 —— 写出来只是让"从哪进、从哪出"诚实。
                if (to == "outside")
                {
                    if (!phaseAttr)
                    {
                        problems.Add(label + " 目标是 outside（机外），只允许用于相位驱动边");
                        continue;
                    }
                }
                else if (families.ContainsKey(to))
                {
                    // 🔴 目标写成**容器名** = 进入该容器，落到它的 entry（= UE 的子状态机 Entry）。
                    //    这里在**装载期**就解析成具体状态 ⇒ 运行时热路径零改动。
                    if (!familyEntry.TryGetValue(to, out string entryState))
                    {
                        problems.Add(label + " 的目标 '" + to + "' 是容器，但该容器没写 entry（进容器落到哪个状态）");
                        continue;
                    }
                    to = entryState;
                }
                else if (!stateNames.Contains(to))
                {
                    problems.Add(label + " 的目标 '" + to + "' 不是已声明的状态");
                    continue;
                }
                string[] srcs;
                if (from == "*")
                {
                    srcs = new[] { "*" };
                }
                else if (from == "outside")
                {
                    if (!phaseAttr)
                    {
                        problems.Add(label + " 来源是 outside（机外），只允许用于相位驱动边");
                        continue;
                    }
                    srcs = new[] { "outside" };
                }
                else if (families.TryGetValue(from, out string[] famMembers))
                {
                    srcs = famMembers;
                }
                else if (stateNames.Contains(from))
                {
                    srcs = new[] { from };
                }
                else
                {
                    problems.Add(label + " 的来源 '" + from + "' 既不是 * 、也不是状态或容器");
                    continue;
                }
                // 条件三选一：when= 命名谓词 / key=+key-mode= 单键 / anim=[+anim-lt=] 动画时间
                string keyAttr = Attr(node, "key");
                string keyMode = Attr(node, "key-mode");
                string animAttr = Attr(node, "anim");
                string animLt = Attr(node, "anim-lt");
                Func<AnimContext, bool> pred;
                string subError;
                if (!string.IsNullOrEmpty(keyAttr))
                {
                    if (!AnimPrimitives.TryBuildKey(keyAttr, keyMode, out pred, out subError))
                    {
                        problems.Add(label + " " + subError);
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(animAttr))
                {
                    if (!AnimPrimitives.TryBuildAnim(animAttr, animLt, out pred, out subError))
                    {
                        problems.Add(label + " " + subError);
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(when))
                {
                    if (!AnimConditions.TryGet(when, out pred))
                    {
                        problems.Add(label + " 引用了没登记的条件谓词 '" + when + "'");
                        continue;
                    }
                }
                else
                {
                    problems.Add(label + " 没有条件（when= / key= / anim= 三选一，不能都空着）");
                    continue;
                }
                float blend = -1f;
                string blendRaw = Attr(node, "blend");
                if (!string.IsNullOrEmpty(blendRaw) && !ResolveNumber(blendRaw, out blend))
                {
                    problems.Add(label + " 的 blend='" + blendRaw + "' 既不是数字、也不是已登记的命名标量");
                    continue;
                }
                bool afterFinish = string.Equals(Attr(node, "after-finish"), "true", StringComparison.OrdinalIgnoreCase);
                bool phaseForced = phaseAttr;
                edges.Add(new AnimEdgeDef(srcs, to, pred, blend, afterFinish, phaseForced));
            }

            // ── 悬空状态：既不是任何边的来源、也没人指向它（警告，不算错 —— 相位 Force 专用的状态就是这样）──
            var touched = new HashSet<string>(StringComparer.Ordinal);   // 相位驱动的边也算（它们写进定义就是为了这个）
            foreach (AnimEdgeDef e in edges)
            {
                if (e.To != "outside")
                {
                    touched.Add(e.To);
                }
                foreach (string s in e.From)
                {
                    touched.Add(s);
                }
            }
            foreach (AnimState s in states)
            {
                if (!touched.Contains(s.Name))
                {
                    DebugLogger.Log("[Anim:" + name + "] 提示：状态 '" + s.Name + "' 不在任何边上（只能由相位 Force 进？）");
                }
            }

            if (problems.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("[Anim] 状态机 '").Append(name).Append("' 定义有 ").Append(problems.Count).Append(" 处问题，**未注册**：");
                foreach (string p in problems)
                {
                    sb.Append("\n    · ").Append(p);
                }
                sb.Append("\n  （文件: ").Append(path).Append("）");
                error = sb.ToString();
                DebugLogger.Log(error);
                return null;
            }

            var def = new AnimMachineDef(name);
            foreach (AnimState s in states)
            {
                def.Add(s);
            }
            foreach (AnimEdgeDef e in edges)
            {
                def.AddEdge(e);
            }
            DebugLogger.Log("[Anim:" + name + "] 定义已从 XML 装载：" + states.Count + " 个状态 / "
                            + edges.Count + " 条边 / " + families.Count + " 个族（" + Path.GetFileName(path) + "）");
            return def;
        }

        /// <summary>数字 → 直接取值；否则查命名标量。都失败返回 false。</summary>
        private static bool ResolveNumber(string raw, out float value)
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
            return AnimConditions.TryGetParam(raw, out value);
        }

        private static string Attr(XmlNode node, string name)
        {
            XmlAttribute a = node.Attributes?[name];
            return a?.Value?.Trim() ?? string.Empty;
        }
    }
}

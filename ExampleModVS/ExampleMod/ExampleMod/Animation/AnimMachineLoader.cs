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
    ///     &lt;!-- 🔴 成员可以是**别的容器名** ⇒ 真嵌套；装载期递归展开成叶子状态 --&gt;
    ///     &lt;family name="Upright" entry="HoverBase"&gt;hoverstart HoverBase&lt;/family&gt;
    ///     &lt;family name="HoverBase" entry="hovermove"&gt;hovermove idle&lt;/family&gt;
    ///       ← 成员空格分隔；**entry = 容器（子状态机）的真实入口**：进容器落到哪个状态
    ///   &lt;/families&gt;
    ///   &lt;states&gt;
    ///     &lt;state name="idle" act="act_fly_idle"/&gt;                                ← 不带 once = 循环
    ///     &lt;state name="fastmoveStart" act="act_fly_fastmove_start" once="true" next="fastmove"/&gt;
    ///                        ← once = 一次性（播完去 next）；**不写时长** —— 长度由 clip 自己带
    ///   &lt;/states&gt;
    ///   &lt;edges&gt;
    ///     &lt;edge from="Prone" to="dodgeL" keys="Space+A"/&gt;                     ← from = 状态 / 容器（容器 = 它里面任何状态）
    ///     &lt;edge from="Prone" to="dodgeR" keys="Space+D" not="true"/&gt;          ← 组合键 + 取反（整条条件取反）
    ///     &lt;edge from="Prone" to="fastmove" anim="remaining" anim-rem-pct="15"/&gt; ← 剩余不足 15%（**百分比，不是秒**）
    ///     &lt;edge from="outside" to="hoverstart" when="takeoff-trigger" phase="true"/&gt; ← **相位接缝**：C# 起飞时 Force 进它
    ///     &lt;edge from="superland" to="outside" anim="remaining" anim-rem-pct="20" phase="true"/&gt;
    ///                        ← 出机接缝：`from` = 落地姿态、`anim-rem-pct` = 剩多少出机（C# 用 TryPhaseEnter/Exit 读）
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
                string onceRaw = Attr(node, "once");
                string nxt = Attr(node, "next");
                // 🔴 一次性 = `once="true"`。**长度不写** —— 长度由 clip 自己带，
                //    引擎给的是 0~1 的播放进度（见 AgentAnimStateMachine.CurrentRemainFrac / CurrentFinished）。
                //    `duration="…"`（秒 / 命名标量）保留成**可选覆盖**：只在"要主动截短 clip"时才写。
                if (!string.IsNullOrEmpty(onceRaw)
                    && !string.Equals(onceRaw, "true", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(onceRaw, "false", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add("状态 '" + sn + "' 的 once='" + onceRaw + "' 只认 true / false");
                    continue;
                }
                bool oneShot = string.Equals(onceRaw, "true", StringComparison.OrdinalIgnoreCase)
                               || !string.IsNullOrEmpty(durRaw);
                if (oneShot && !string.IsNullOrEmpty(durRaw) && !ResolveNumber(durRaw, out duration))
                {
                    problems.Add("状态 '" + sn + "' 的 duration='" + durRaw + "' 既不是数字、也不是已登记的命名标量"
                                 + "（**不写就行** —— 长度默认取 clip 自己的）");
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

            // 🔴 **入口解析器**：`to=容器` 时一路往下走，直到落到**状态**。
            //    嵌套之后 entry 可以指向**子容器** ⇒ 必须递归（不是查一次表）。
            Func<string, string> resolveEntry = fam =>
            {
                string cur = fam;
                for (int g = 0; g < 32 && families.ContainsKey(cur); g++)
                {
                    if (!familyEntry.TryGetValue(cur, out string nxt)) return null;
                    cur = nxt;
                }
                return cur;
            };

            // ── 容器（族）校验 + **递归展开成叶子状态** ──
            //    🔴 支持**嵌套**：`<family name="A">hovermove idle 子容器</family>` —— 成员可以是**容器名**。
            //    归属仍然**唯一**（递归），且**不许成环**。
            //    🔴 展开在**装载期**做完：`families[name]` 最终只装**叶子状态** ⇒
            //       下游（`from=容器` 取来源 / `to=容器` 取 entry / 运行时）**一个字都不用改**。
            {
                var ownerOf = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string[]> kv in families)
                {
                    if (kv.Key != null && stateNames.Contains(kv.Key))
                    {
                        problems.Add("容器名 '" + kv.Key + "' 和一个状态重名了（名字必须唯一）");
                    }
                    foreach (string m in kv.Value)
                    {
                        bool isState = stateNames.Contains(m), isFam = families.ContainsKey(m);
                        if (!isState && !isFam)
                        {
                            problems.Add("容器 '" + kv.Key + "' 里的 '" + m + "' 既不是已声明的状态、也不是已声明的容器");
                            continue;
                        }
                        if (string.Equals(m, kv.Key, StringComparison.Ordinal))
                        {
                            problems.Add("容器 '" + kv.Key + "' 把自己列成了成员");
                            continue;
                        }
                        if (ownerOf.TryGetValue(m, out string prev))
                        {
                            problems.Add("'" + m + "' 同时属于容器 '" + prev + "' 和 '" + kv.Key
                                         + "' —— 一个状态 / 容器只能归属一个容器");
                            continue;
                        }
                        ownerOf[m] = kv.Key;
                    }
                    if (familyEntry.TryGetValue(kv.Key, out string ent) && Array.IndexOf(kv.Value, ent) < 0)
                    {
                        problems.Add("容器 '" + kv.Key + "' 的 entry='" + ent + "' 不是它的成员");
                    }
                }

                // 递归展开（带环检测）：容器 → 它的全部**叶子状态**
                var leaves = new Dictionary<string, string[]>(StringComparer.Ordinal);
                Func<string, List<string>, List<string>> expand = null;
                expand = (name, stack) =>
                {
                    var outp = new List<string>();
                    if (!families.ContainsKey(name))
                    {
                        outp.Add(name);                       // 叶子：状态
                        return outp;
                    }
                    if (stack.Contains(name))
                    {
                        problems.Add("容器嵌套成环：" + string.Join(" → ", stack) + " → " + name);
                        return outp;
                    }
                    stack.Add(name);
                    foreach (string m in families[name])
                    {
                        outp.AddRange(expand(m, stack));
                    }
                    stack.RemoveAt(stack.Count - 1);
                    return outp;
                };
                foreach (KeyValuePair<string, string[]> kv in families)
                {
                    leaves[kv.Key] = expand(kv.Key, new List<string>()).ToArray();
                }
                foreach (KeyValuePair<string, string[]> kv in leaves)
                {
                    families[kv.Key] = kv.Value;              // 🔴 用叶子覆盖：下游一律不用改
                }
            }

            // entry 必须**最终落到一个状态**（可以一路指向子容器，只要链底是状态）
            foreach (KeyValuePair<string, string> kv in familyEntry)
            {
                string fin = resolveEntry(kv.Key);
                if (string.IsNullOrEmpty(fin) || !stateNames.Contains(fin))
                {
                    problems.Add("容器 '" + kv.Key + "' 的 entry='" + kv.Value
                                 + "' 最终没落到已声明的状态（嵌套链断了 / 没设 entry / 成环？）");
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
                    // 🔴 `to=容器` ⇒ 装载期解析成它的 entry；entry 可以是**子容器** ⇒ 递归到底
                    string resolved = resolveEntry(to);
                    if (string.IsNullOrEmpty(resolved))
                    {
                        problems.Add(label + " 的目标 '" + to + "' 是容器，但入口链没落到状态（没写 entry / 成环？）");
                        continue;
                    }
                    to = resolved;
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
                // 条件三选一：when= 命名谓词 / keys="A+B" 组合键 / anim=[+anim-rem-pct=] 动画时间
                // `not="true"` 可以**取反任意一种**（见下面统一包的那一层）
                string keysAttr = Attr(node, "keys");
                string animAttr = Attr(node, "anim");
                string animPct = Attr(node, "anim-rem-pct");
                string notRaw = Attr(node, "not");
                float remainPct = -1f;
                if (!string.IsNullOrEmpty(Attr(node, "anim-lt")))
                {
                    problems.Add(label + " 用了已废弃的 anim-lt（那是**秒**）；改成 anim-rem-pct=\"20\"（**百分比** 0~100）");
                    continue;
                }
                if (!string.IsNullOrEmpty(Attr(node, "key")) || !string.IsNullOrEmpty(Attr(node, "key-mode")))
                {
                    problems.Add(label + " 用了已废弃的 key= / key-mode=；改成 keys=\"A+B\"（+ 连 = 同时按着），"
                                 + "要按相反的情况加 not=\"true\"");
                    continue;
                }
                Func<AnimContext, bool> pred;
                string subError;
                if (!string.IsNullOrEmpty(keysAttr))
                {
                    if (!AnimPrimitives.TryBuildKeys(keysAttr, out pred, out subError))
                    {
                        problems.Add(label + " " + subError);
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(animAttr))
                {
                    if (!AnimPrimitives.TryBuildAnim(animAttr, animPct, out pred, out subError))
                    {
                        problems.Add(label + " " + subError);
                        continue;
                    }
                    // 🔴 相位边要用这个**数**（"剩多少出机"）—— 谓词只够判真假，判不出"还剩多少"。
                    //    （相位读它：见 AnimEdgeDef.RemainPct / AgentAnimStateMachine.TryPhaseExit）
                    if (string.Equals(animAttr.Trim(), "remaining", StringComparison.OrdinalIgnoreCase))
                    {
                        float tmp;
                        if (ResolveNumber(animPct, out tmp))
                        {
                            remainPct = tmp;
                        }
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
                    problems.Add(label + " 没有条件（when= / keys= / anim= 三选一，不能都空着）");
                    continue;
                }
                // 🔴 `not="true"` = **整条条件取反**（对 keys / anim / 命名谓词**一律适用**）——
                //    只在**这一处**包一层。别在每个 builder 里各写一份（那就是"同一个语义多处实现"，
                //    迟早有一处忘改 —— 本项目已经因为这种结构踩过好几次）。
                if (!string.IsNullOrEmpty(notRaw))
                {
                    bool neg;
                    if (!bool.TryParse(notRaw.Trim(), out neg))
                    {
                        problems.Add(label + " 的 not='" + notRaw + "' 只认 true / false");
                        continue;
                    }
                    if (neg)
                    {
                        Func<AnimContext, bool> inner = pred;
                        pred = c => !inner(c);
                    }
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
                AnimEdgeDef edgeDef = new AnimEdgeDef(srcs, to, pred, blend, afterFinish, phaseForced);
                edgeDef.RemainPct = remainPct;
                // 🔴 相位边要把 `when=` 的**名字**留着（普通边不用）：相位（C#）按时刻找状态 ——
                //    "落地这个时刻该 Force 进哪个状态" = 读 `when="land-trigger"` 那条边的 `to`
                //    （见 AgentAnimStateMachine.TryPhaseTarget）。
                if (phaseForced)
                {
                    edgeDef.WhenName = when;
                }
                edges.Add(edgeDef);
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

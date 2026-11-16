using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LivingWorldNpcs
{
    /// <summary>一次对话请求的上下文（P0-D.2）。只读，不执行效果。</summary>
    public sealed class StoryDialogueContext
    {
        public string DialogueId;
        public string TriggerId;
        public string Purpose = "talk";
        /// <summary>本次入口与条件/账目归属演员；各句说话者另由节点Speaker决定。</summary>
        public string ActorId;

        public override string ToString()
        {
            return (DialogueId ?? "-") + "/" + (TriggerId ?? "-") + " actor=" + (ActorId ?? "-");
        }
    }

    /// <summary>
    /// 剧情条件求值适配（P0-D.2）：把新注册表里的谓词接到**现有**求值器上。
    ///
    /// 🔴 三种结果必须分开：true / false / 错误。**错误不能被当成 false，更不能被 `!` 翻成 true。**
    /// 🔴 不复制第二套视线/距离/记忆判断：视线类（`can_see`）与个人知识（`knows`）在 D.2 尚未接线，
    ///    这里直接返回错误，宁可拒绝也不猜。
    /// </summary>
    public static class StoryDialogueFacts
    {
        /// <summary>阶段别名：数据里写人话，代码里是枚举。</summary>
        private static readonly Dictionary<string, StoryProbePhase> PhaseAliases =
            new Dictionary<string, StoryProbePhase>(StringComparer.OrdinalIgnoreCase)
            {
                { "first_meet", StoryProbePhase.FirstAttempt },
                { "jail", StoryProbePhase.Jail },
                { "second_meet", StoryProbePhase.Second },
                { "done", StoryProbePhase.Done },
            };

        /// <summary>
        /// 求值一个条件词（不含 `!`；取反由调用方在**确认没有错误之后**执行）。
        /// 返回 false 时 error 非空。
        /// </summary>
        public static bool TryEvaluate(string term, StoryDialogueContext ctx, out bool value, out string error)
        {
            value = false;
            error = null;
            if (string.IsNullOrWhiteSpace(term)) { error = "空条件"; return false; }

            int split = term.IndexOf(':');
            string name = split < 0 ? term : term.Substring(0, split);
            string arg = split < 0 ? null : term.Substring(split + 1);
            // 运算符式写法：gold>=100（与 item:xxx 这类冒号式共存）
            if (split < 0 && term.StartsWith("gold>=", StringComparison.Ordinal))
            {
                name = "gold>=";
                arg = term.Substring("gold>=".Length);
            }
            else if (split < 0 && term.StartsWith("step>=", StringComparison.Ordinal))
            {
                name = "step>=";
                arg = term.Substring("step>=".Length);
            }
            else if (split < 0 && term.IndexOf('@') > 0)
            {
                // 跨 NPC 引用：quest_accepted@pawnbroker
                int at = term.IndexOf('@');
                name = term.Substring(0, at);
                arg = term.Substring(at + 1);
            }

            if (name == "phase")
            {
                if (string.IsNullOrEmpty(arg)) { error = "phase 缺参数"; return false; }
                if (!PhaseAliases.TryGetValue(arg, out var phase))
                {
                    if (!Enum.TryParse(arg, true, out phase)) { error = "未知阶段 '" + arg + "'"; return false; }
                }
                value = StoryProbeState.Phase == phase;
                return true;
            }

            if (name == "errand_ready")
            {
                value = TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours >= StoryProbeState.Data.NextErrandHour;
                return true;
            }

            if (name == "can_redeem_keepsake")
            {
                value = StoryProbeState.HasRewardReceipt("pawnbroker", "keepsake-paid")
                        || (TaleWorlds.CampaignSystem.Hero.MainHero != null
                            && TaleWorlds.CampaignSystem.Hero.MainHero.Gold >= 30);
                return true;
            }

            if (name == "street_help_pending")
            {
                value = StoryProbeState.IsStreetEventResolved(1)
                        && !StoryProbeState.HasRewardReceipt("pawnbroker", "street-help");
                return true;
            }

            if (name == "step>=")
            {
                if (!int.TryParse(arg, out int step)) { error = "step>= 参数不是整数"; return false; }
                value = StoryProbeState.Step(ctx.ActorId) >= step;
                return true;
            }

            if (name == "event_active")
            {
                if (!int.TryParse(arg, out int eventId)) { error = "event_active 参数不是整数"; return false; }
                value = StoryProbeMissionBehavior.Current?.Director?.ActiveEventIndex == eventId;
                return true;
            }

            if (name == "can_see" || name == "knows")
            {
                error = "条件 '" + name + "' 尚未接入（D.2 不猜视线/个人知识）";
                return false;
            }

            if (name == "stripped_disarmed")
            {
                // P0-D.4 通用化：查**本次实际说话者**的四个武器槽，不再固定读铁头。
                var host = StoryProbeMissionBehavior.Current;
                var agent = host?.GetAgent(ctx.ActorId);
                if (agent == null)
                {
                    error = "stripped_disarmed: actor '" + (ctx.ActorId ?? "-") + "' 不在场";
                    return false;
                }
                value = StoryProbeMissionBehavior.IsReallyDisarmed(agent);
                return true;
            }

            if (name == "money_attitude")
            {
                if (string.IsNullOrEmpty(arg)) { error = "money_attitude 缺参数"; return false; }
                var def = StoryProbeDefs.GetNpc(ctx.ActorId);
                if (def == null) { error = "money_attitude: 未知 NPC '" + (ctx.ActorId ?? "-") + "'"; return false; }
                string want = arg.Trim().ToLowerInvariant();
                value = (want == "open" && def.Money == MoneyAttitude.Open)
                     || (want == "covert" && def.Money == MoneyAttitude.Covert)
                     || (want == "disdain" && def.Money == MoneyAttitude.Disdain);
                return true;
            }

            // 其余谓词沿用现有求值器（含 gold>= / item / asked / reward / quest_* / event_resolved 等）
            return TryDelegate(term, ctx, out value, out error);
        }

        private static bool TryDelegate(string term, StoryDialogueContext ctx, out bool value, out string error)
        {
            bool? result = StoryProbeDialogue.EvaluateTermForDialogue(term, ctx?.ActorId);
            if (!result.HasValue)
            {
                value = false;
                error = "条件 '" + term + "' 求值失败或未实现";
                return false;
            }
            value = result.Value;
            error = null;
            return true;
        }

        /// <summary>
        /// 求值一组条件（AND）。任一条件出错 ⇒ 整体出错（返回 false + error），**不是 false 值**。
        /// </summary>
        public static bool TryEvaluateAll(IEnumerable<string> conditions, StoryDialogueContext ctx,
            out bool value, out string error, out List<string> trail)
        {
            value = true;
            error = null;
            trail = new List<string>();
            if (conditions == null) return true;

            foreach (string raw in conditions)
            {
                string term = raw ?? "";
                bool negate = term.StartsWith("!", StringComparison.Ordinal);
                if (negate) term = term.Substring(1);

                if (!TryEvaluate(term, ctx, out bool v, out string err))
                {
                    value = false;
                    error = raw + " -> " + err;
                    trail.Add(raw + " = ERROR(" + err + ")");
                    return false;
                }
                bool effective = negate ? !v : v;
                trail.Add(raw + " = " + effective);
                if (!effective) { value = false; return true; }   // 合法 false，继续收集 trail
            }
            return true;
        }
    }

    public sealed class StoryDialogueResolution
    {
        public bool Ok;
        public StoryDialogueEntryRule Entry;
        public string Reason = "";
        public List<string> Trail = new List<string>();

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Ok ? "OK: resolved entry=" + Entry.Id + " node=" + Entry.Node
                        : "REJECTED: " + Reason);
            foreach (string line in Trail) sb.Append("\n  ").Append(line);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 入口仲裁（P0-D.2/D.3 的静态部分）：按触发器找入口规则 → 求值条件 → 优先序决胜。
    /// 🔴 不执行效果、不开窗；调用方拿到节点后再走唯一注入口。
    /// </summary>
    public static class StoryDialogueResolver
    {
        public static StoryDialogueResolution Resolve(string dialogueId, string triggerId, StoryDialogueContext ctx)
        {
            StoryDialogueLoader.EnsureLoaded();
            var result = new StoryDialogueResolution();
            if (!StoryDialogueRegistry.TryGet(dialogueId, out var def))
            {
                result.Reason = "没有已发布的对话定义 '" + dialogueId + "'";
                return result;
            }

            var candidates = def.Entries
                .Where(e => string.Equals(e.Trigger, triggerId, StringComparison.Ordinal))
                .OrderByDescending(e => e.Priority)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
            {
                result.Reason = "定义没有触发器 '" + triggerId + "' 的入口规则";
                return result;
            }

            foreach (var entry in candidates)
            {
                bool ok = StoryDialogueFacts.TryEvaluateAll(entry.Conditions, ctx, out bool value, out string error, out var trail);
                if (!ok)
                {
                    result.Trail.Add(entry.Id + ": " + error);
                    continue;   // 条件出错 ⇒ 这个入口不合格，但不把错误当 false 去选别的“侥幸”
                }
                if (!value)
                {
                    result.Trail.Add(entry.Id + ": conditions false");
                    continue;
                }
                result.Ok = true;
                result.Entry = entry;
                result.Trail.Add(entry.Id + ": MATCH (priority " + entry.Priority + ")");
                return result;
            }

            result.Reason = "所有候选入口都不满足（见 trail）";
            return result;
        }

        /// <summary>只按EntryActors找入口；第三人参与不占有其他演员的交谈定义。</summary>
        public static string FindDialogueForActor(string actorId, string triggerId)
        {
            StoryDialogueLoader.EnsureLoaded();
            foreach (var def in StoryDialogueRegistry.All)
            {
                if (!def.EntryActors.Contains(actorId)) continue;
                if (def.Triggers.Any(t => string.Equals(t.Id, triggerId, StringComparison.Ordinal)))
                    return def.DialogueId;
            }
            return null;
        }
    }
}

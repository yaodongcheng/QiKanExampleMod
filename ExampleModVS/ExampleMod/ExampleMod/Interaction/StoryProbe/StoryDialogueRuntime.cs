using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════════════════
    // P0-D.3 / P0-D.4（2026-11-15）对话运行期：请求 → 会话 → 提交 → 收据 → 结束
    //
    // 依据：总纲 §0.6.4
    //   D.3 请求携带来源/目的/事件/版本/参与者/取消令牌；同一遭遇去重、失败退避、
    //       只有唯一 StartConversation 注入口能开窗。
    //   D.4 节点进入 / 玩家选择 / 会话结束三个生命周期提交效果；文本读取无副作用；
    //       提交前复核；检定只掷一次；奖励/付款按收据防重。
    //
    // 🔴 纪律：
    //   · 具体剧情（谁、多少钱、哪个势力）**全部来自 JSON**；本文件只认注册表里的原子。
    //   · 未知效果/条件/参数在发布期已被拒绝；运行期再坏也不能半提交。
    //   · `Receipt=once` 的收据落存档（换会话/重载/重播都不重发）；
    //     `Receipt=session` 只在本次会话内防重；`never` 每次都提交。
    //   · 出错不猜：求值失败、角色缺失、钱不足都返回错误并让本次会话停止提交。
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class StoryDialogueRequest
    {
        public int Id;
        public string DialogueId;
        public string TriggerId;
        public string Purpose = "talk";
        public string Source = "script";
        public string ActorId;              // 实际说话者（登记 NPC id）
        public string EncounterKey;         // 同一次遭遇的稳定键（去重/重入）
        public int RuntimeToken;            // 请求发出时的 Mission token
        public int DefinitionVersion;
    }

    public sealed class StoryDialogueSession
    {
        public int Id;
        public StoryDialogueRequest Request;
        public StoryDialogueDef Definition;
        public StoryDialogueTriggerDef Trigger;
        public StoryDialogueEntryRule Entry;
        public float StartedAt;
        public bool Ended;
        public bool Failed;
        public string EndReason = "";
        public string CurrentNode = "";

        /// <summary>本次会话已进入过的节点（会话结束效果按它们收集）。</summary>
        public readonly HashSet<string> EnteredNodes = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>玩家实际选择过的选项（`nodeId/optionId`）；未选分支不得参与收尾结算。</summary>
        public readonly HashSet<string> SelectedOptions = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>本次会话内已提交的效果收据（`session`）。</summary>
        public readonly HashSet<string> SessionReceipts = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>本次会话的临时事实（如最近一次检定的结果）。</summary>
        public readonly Dictionary<string, string> Facts = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>已提交效果的可读轨迹（诊断用）。</summary>
        public readonly List<string> Committed = new List<string>();

        public string Key => (Definition?.DialogueId ?? "-") + "/" + (Request?.ActorId ?? "-");
    }

    /// <summary>
    /// 请求与会话的唯一入口。
    /// 🔴 单窗口：同一时间只允许一个剧情会话；开窗前复核资格。
    /// </summary>
    public static class StoryDialogueRuntime
    {
        private static int _nextRequestId;
        private static int _nextSessionId;

        private static StoryDialogueSession _current;
        private static StoryDialogueSession _last;

        /// <summary>`dialogueId|triggerId|actorId` → 上次请求时刻（冷却）。</summary>
        private static readonly Dictionary<string, float> _lastAttempt =
            new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>遭遇键 → 上次会话结束时刻（重入间隔）。</summary>
        private static readonly Dictionary<string, float> _lastEnded =
            new Dictionary<string, float>(StringComparer.Ordinal);

        public static StoryDialogueSession Current => _current;
        public static StoryDialogueSession Last => _last;
        public static bool HasActiveSession => _current != null && !_current.Ended;

        /// <summary>Mission 内时钟（秒）；没有宿主时退回 0，冷却只在同 Mission 内有效。</summary>
        public static float Now => StoryProbeMissionBehavior.Current?.Clock ?? 0f;

        public static void ResetRuntime(string reason)
        {
            if (_current != null)
            {
                _current.Ended = true;
                _current.EndReason = "runtime reset: " + reason;
                _last = _current;
                _current = null;
            }
            _lastAttempt.Clear();
            _lastEnded.Clear();
        }

        /// <summary>
        /// 发出一次对话请求。成功时 `session.Entry.Node` 就是数据裁决出的入口节点。
        /// 失败返回原因（unknown_trigger / cooldown / reentry / busy / no_entry / source_mismatch / not_published）。
        /// </summary>
        public static bool TryRequest(string dialogueId, string triggerId, string actorId,
            string source, string encounterKey, out StoryDialogueSession session, out string reason, bool force = false)
        {
            session = null;
            reason = null;

            if (StoryProbeSettings.Enabled == false) { reason = "story disabled"; return false; }
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) { reason = "no active story mission"; return false; }

            StoryDialogueLoader.EnsureLoaded();
            if (!StoryDialogueRegistry.TryGet(dialogueId, out var def) || def == null)
            {
                reason = "not_published: " + dialogueId;
                return false;
            }

            var trigger = def.Triggers.FirstOrDefault(t => string.Equals(t.Id, triggerId, StringComparison.Ordinal));
            if (trigger == null)
            {
                reason = "unknown_trigger: " + dialogueId + "/" + triggerId;
                return false;
            }
            if (!string.IsNullOrEmpty(source) && !string.Equals(trigger.Source, source, StringComparison.Ordinal))
            {
                reason = "source_mismatch: trigger expects " + trigger.Source + ", got " + source;
                return false;
            }
            if (string.IsNullOrEmpty(actorId) || !def.EntryActors.Contains(actorId))
            {
                reason = "actor_not_entry_actor: " + (actorId ?? "-");
                return false;
            }

            if (_current != null && !_current.Ended)
            {
                reason = "busy: session " + _current.Id + " owns the window";
                return false;
            }

            // P0-D.3 仲裁：玩家与实际处理案件的权威交谈时，让犯罪/投降入口先走。
            // 岗位自动质问（post_ai）不受影响；也不一概禁掉原版犯罪。
            if (string.Equals(trigger.Source, "player", StringComparison.Ordinal)
                && VanillaHasPriority(actorId, out string vanillaWhy))
            {
                reason = "vanilla_priority: " + vanillaWhy;
                DebugLogger.Log("[StoryDialogue] yield to vanilla: " + reason + " actor=" + actorId);
                return false;
            }

            string attemptKey = (dialogueId ?? "") + "|" + (triggerId ?? "") + "|" + (actorId ?? "");
            float now = Now;
            // force = 推回后的强制盘问（2026-11-19 用户裁定）：刚被守卫亲手挡下这一刻，
            // 触发器冷却与重入冷却都不该把这扇窗关掉。
            if (!force && trigger.CooldownSec > 0f && _lastAttempt.TryGetValue(attemptKey, out float lastTry)
                && now - lastTry < trigger.CooldownSec)
            {
                reason = string.Format(CultureInfo.InvariantCulture, "cooldown: {0:F1}s left",
                    trigger.CooldownSec - (now - lastTry));
                return false;
            }
            _lastAttempt[attemptKey] = now;

            string encounter = string.IsNullOrEmpty(encounterKey)
                ? (dialogueId + "|" + triggerId + "|" + actorId)
                : encounterKey;
            if (!force && trigger.ReentrySec > 0f && _lastEnded.TryGetValue(encounter, out float lastEnd)
                && now - lastEnd < trigger.ReentrySec)
            {
                reason = string.Format(CultureInfo.InvariantCulture, "reentry: {0:F1}s left",
                    trigger.ReentrySec - (now - lastEnd));
                return false;
            }

            var ctx = new StoryDialogueContext
            {
                DialogueId = dialogueId,
                TriggerId = triggerId,
                Purpose = trigger.Purpose,
                ActorId = actorId,
            };
            var resolution = StoryDialogueResolver.Resolve(dialogueId, triggerId, ctx);
            if (!resolution.Ok)
            {
                reason = "no_entry: " + resolution.Reason;
                return false;
            }

            // 节点准入：入口节点自己的 Requires 也必须成立（到达节点前核准入）。
            var entryNode = def.FindNode(resolution.Entry.Node);
            if (entryNode == null)
            {
                reason = "no_entry_node: " + resolution.Entry.Node;
                return false;
            }
            bool nodeOk = StoryDialogueFacts.TryEvaluateAll(entryNode.Requires, ctx, out bool nodeValue,
                out string nodeError, out var nodeTrail);
            if (!nodeOk)
            {
                reason = "entry_requires_error: " + nodeError;
                return false;
            }
            if (!nodeValue)
            {
                reason = "entry_requires_false";
                return false;
            }

            var request = new StoryDialogueRequest
            {
                Id = ++_nextRequestId,
                DialogueId = dialogueId,
                TriggerId = triggerId,
                Purpose = trigger.Purpose,
                Source = trigger.Source,
                ActorId = actorId,
                EncounterKey = encounter,
                RuntimeToken = StoryProbeState.RuntimeToken,
                DefinitionVersion = def.ContentVersion,
            };
            session = new StoryDialogueSession
            {
                Id = ++_nextSessionId,
                Request = request,
                Definition = def,
                Trigger = trigger,
                Entry = resolution.Entry,
                StartedAt = now,
                CurrentNode = resolution.Entry.Node,
            };
            _current = session;
            DebugLogger.Log("[StoryDialogue] session " + session.Id + " opened dialogue=" + dialogueId
                + " trigger=" + triggerId + " actor=" + actorId + " entry=" + resolution.Entry.Node
                + " v" + def.ContentVersion + " request=" + request.Id);
            return true;
        }

        /// <summary>注入前最后一道复核：请求仍是当前请求、token 未失效、演员还在场。</summary>
        public static bool ConfirmForOpen(StoryDialogueSession session, out string reason)
        {
            reason = null;
            if (session == null || session.Ended) { reason = "session missing/ended"; return false; }
            if (_current != session) { reason = "session replaced"; return false; }
            if (session.Request.RuntimeToken != StoryProbeState.RuntimeToken) { reason = "runtime token changed"; return false; }
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) { reason = "host inactive"; return false; }
            if (host.GetHero(session.Request.ActorId) == null && host.GetAgent(session.Request.ActorId) == null)
            {
                reason = "actor missing in mission: " + session.Request.ActorId;
                return false;
            }
            return true;
        }

        /// <summary>节点真正显示之前提交 `on_enter` 效果（同一会话同一节点只提交一次）。</summary>
        /// <remarks>文本读取是纯读；这里才是「这一节点真的到了」的唯一提交点，所以要核令牌与准入。</remarks>
        public static bool NotifyNodeShown(string nodeId)
        {
            var session = _current;
            if (session == null || session.Ended || string.IsNullOrEmpty(nodeId)) return true;
            if (session.Failed) return false;                       // 失败会话不再提交任何效果
            if (session.Request?.RuntimeToken != StoryProbeState.RuntimeToken)
            {
                session.Failed = true;
                session.EndReason = "node callback token stale: " + nodeId;
                DebugLogger.Log("[StoryDialogue] node callback REJECTED (stale token) " + nodeId);
                return false;
            }
            var node = session.Definition?.FindNode(nodeId);
            if (node == null) return true;
            var ctx = BuildContext(session);
            if (!StoryDialogueFacts.TryEvaluateAll(node.Requires, ctx, out bool allowed, out string evalError, out _))
            {
                session.Failed = true;
                session.EndReason = "node requires error: " + evalError;
                DebugLogger.Log("[StoryDialogue] node REJECTED " + nodeId + ": " + evalError);
                return false;
            }
            if (!allowed)
            {
                session.Failed = true;
                session.EndReason = "node requires false: " + nodeId;
                DebugLogger.Log("[StoryDialogue] node REJECTED (requires false) " + nodeId);
                return false;
            }
            session.CurrentNode = nodeId;
            session.EnteredNodes.Add(nodeId);
            return CommitEffects(node.OnEnter, "on_enter", "node:" + nodeId);
        }

        private static StoryDialogueContext BuildContext(StoryDialogueSession session)
            => new StoryDialogueContext
            {
                DialogueId = session?.Definition?.DialogueId,
                TriggerId = session?.Trigger?.Id,
                Purpose = session?.Trigger?.Purpose ?? "talk",
                ActorId = session?.Request?.ActorId,
            };

        /// <summary>
        /// 玩家选择了一个选项：**在真实提交点重核**令牌、当前节点、条件、演员与目标，再原子提交。
        /// 显示时的 UI 条件只用于可见性，不能代替这里。
        /// </summary>
        public static bool NotifyOptionSelected(string nodeId, string optionId, out string reason)
        {
            reason = null;
            var session = _current;
            if (session == null || session.Ended) { reason = "no active session"; return false; }
            // 本次会话已经失败（预检/提交/进入被拒）⇒ 后续选项一律不许再提交。
            if (session.Failed) { reason = "session already failed: " + (session.EndReason ?? "-"); return false; }
            if (session.Request?.RuntimeToken != StoryProbeState.RuntimeToken)
            { reason = "runtime token changed"; return false; }

            var node = session.Definition?.FindNode(nodeId);
            var menu = session.Definition?.OptionsNode(node);
            var option = menu?.Options?.FirstOrDefault(o => string.Equals(o.Id, optionId, StringComparison.Ordinal));
            if (option == null) { reason = "unknown option " + nodeId + "/" + optionId; return false; }
            // 2026-11-19 用户裁定：随机检定不设门槛 —— 属性差只体现为成功率（可为 0），不再拦选择。
            if (!string.Equals(session.CurrentNode, nodeId, StringComparison.Ordinal))
            { reason = "option is not from the current node: " + nodeId; return false; }

            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) { reason = "host inactive"; return false; }
            string actor = session.Request?.ActorId;
            if (host.GetHero(actor) == null && host.GetAgent(actor) == null)
            { reason = "actor missing in mission: " + (actor ?? "-"); return false; }

            var ctx = new StoryDialogueContext
            {
                DialogueId = session.Definition.DialogueId,
                TriggerId = session.Trigger?.Id,
                Purpose = session.Trigger?.Purpose ?? "talk",
                ActorId = actor,
            };
            // 当前节点自己的准入条件也要重核：显示时成立不代表提交时还成立。
            if (!StoryDialogueFacts.TryEvaluateAll(node.Requires, ctx, out bool nodeAllowed, out string nodeEvalError, out _))
            { reason = "node requires error: " + nodeEvalError; return false; }
            if (!nodeAllowed) { reason = "node requires no longer true: " + nodeId; return false; }
            if (!StoryDialogueFacts.TryEvaluateAll(menu.Requires, ctx, out bool menuAllowed, out _, out _) || !menuAllowed)
            { reason = "topic menu requires no longer true"; return false; }

            if (!StoryDialogueFacts.TryEvaluateAll(option.Requires, ctx, out bool allowed, out string evalError, out _))
            { reason = "requires error: " + evalError; return false; }
            if (!allowed) { reason = "requires no longer true"; return false; }

            string target = option.Check != null ? option.Check.Success : option.NextNode;
            if (target == nodeId || option.Check?.Fail == nodeId) { reason = "answer cannot repeat itself through inherited menu"; return false; }
            StoryDialogueNodeDef targetNode = null;
            if (!string.IsNullOrEmpty(target))
            {
                targetNode = session.Definition.FindNode(target);
                if (targetNode == null) { reason = "option target no longer exists: " + target; return false; }
                // 目标节点的准入条件在提交前一起核：不许“效果先落账、再发现进不去”。
                if (!StoryDialogueFacts.TryEvaluateAll(targetNode.Requires, ctx, out bool targetAllowed,
                        out string targetEvalError, out _))
                { reason = "target requires error: " + targetEvalError; return false; }
                if (!targetAllowed) { reason = "target requires no longer true: " + target; return false; }
            }
            if (option.Check != null)
            {
                string checkTarget = string.IsNullOrEmpty(option.Check.Target) || option.Check.Target == "npc"
                    || option.Check.Target == "questioner" ? actor : option.Check.Target;
                if (host.GetAgent(checkTarget) == null && host.GetHero(checkTarget) == null)
                { reason = "check target missing: " + checkTarget; return false; }
            }

            session.CurrentNode = nodeId;
            session.EnteredNodes.Add(nodeId);
            string selKey = menu.Id + "/" + optionId;
            session.SelectedOptions.Add(selKey);
            if (!CommitEffects(option.Effects, "on_select", "option:" + selKey))
            {
                session.SelectedOptions.Remove(selKey);   // 提交失败不留下“已选”痕迹
                reason = session.EndReason;
                return false;
            }
            return true;
        }

        /// <summary>会话结束：只结算已进入节点的节点级收尾效果和**已选分支**的收尾效果。</summary>
        public static void EndSession(string reason)
        {
            var session = _current;
            if (session == null) return;
            if (!session.Ended)
            {
                // 已经失败/取消的会话不再提交收尾效果：避免“前面失败了，后面还在发钱”。
                if (!session.Failed) CommitSessionEndEffects(session);
                session.Ended = true;
                session.EndReason = reason ?? "";
                _last = session;
                _current = null;
                string encounter = session.Request?.EncounterKey;
                if (!string.IsNullOrEmpty(encounter)) _lastEnded[encounter] = Now;
                DebugLogger.Log("[StoryDialogue] session " + session.Id + " ended (" + (reason ?? "-") + ")"
                    + " effects=" + session.Committed.Count + (session.Failed ? " FAILED" : ""));
            }
        }

        /// <summary>取消：与正常结束同一收尾路径，但不重复提交会发钱的会话结束效果。</summary>
        public static void CancelSession(string reason)
        {
            var session = _current;
            if (session == null) return;
            session.Failed = true;
            EndSession("cancelled: " + (reason ?? "-"));
        }

        private static void CommitSessionEndEffects(StoryDialogueSession session)
        {
            if (session?.Definition?.Nodes == null) return;
            foreach (string nodeId in session.EnteredNodes)
            {
                var node = session.Definition.FindNode(nodeId);
                if (node == null) continue;
                // 节点级收尾：只要到过这个节点就成立。
                CommitEffects(node.OnEnter.Where(e => e.Commit == "on_session_end").ToList(),
                    "on_session_end", "node:" + nodeId);
            }
            foreach (var node in session.Definition.Nodes)
            {
                string nodeId = node.Id;
                // 选项级收尾：只算玩家真正选过的那一条；未选分支零效果。
                foreach (var opt in node.Options)
                {
                    string key = nodeId + "/" + opt.Id;
                    if (!session.SelectedOptions.Contains(key)) continue;
                    CommitEffects(opt.Effects.Where(e => e.Commit == "on_session_end").ToList(),
                        "on_session_end", "option:" + key);
                }
            }
        }

        /// <summary>
        /// 一次提交 = 先逐项预检 → 再逐项执行 → 任一失败按逆序回滚 → 全部成功才落收据。
        /// 🔴 不允许“前一项扣了钱、后一项失败还留下半笔”。
        /// 🔴 回滚用「提交前快照」恢复，不用「相反操作」：本来就有的事实/账目必须原样回来
        ///    （反例：`mark` 原本就成立，反向 Unmark 会把它抹掉）。
        /// </summary>
        private static bool CommitEffects(List<StoryDialogueEffect> effects, string stage, string owner)
        {
            var session = _current;
            if (session == null || session.Ended || effects == null || effects.Count == 0) return true;
            // 失败/取消过的会话不再提交任何效果（进入失败后继续点选项反例）。
            if (session.Failed) return false;

            var planned = new List<(StoryDialogueEffect effect, string receipt)>();
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null) continue;
                if (!string.Equals(effect.Commit, stage, StringComparison.Ordinal)) continue;
                // 收据身份 = 动词 + 参数 + 阶段 + 收据类型 + **同名效果的序号**。
                // 不用列表下标：前面插入无关效果不该让同一笔付款再扣一次。
                string receipt = ReceiptKey(session, effect, owner, OccurrenceIndex(effects, i));
                if (effect.Receipt == "once" && StoryProbeState.HasDialogueReceipt(receipt))
                {
                    DebugLogger.Log("[StoryDialogue] effect skipped (receipt) " + receipt);
                    continue;
                }
                if (effect.Receipt == "session" && session.SessionReceipts.Contains(receipt))
                {
                    DebugLogger.Log("[StoryDialogue] effect skipped (session receipt) " + receipt);
                    continue;
                }
                if (!StoryDialogueEffectRuntime.TryPreflight(effect, session, out string preError))
                {
                    session.Failed = true;
                    session.EndReason = "effect " + effect.Id + " preflight failed: " + preError;
                    DebugLogger.Log("[StoryDialogue] effect REJECTED " + effect.Id + " @" + owner + ": " + preError);
                    return false;
                }
                planned.Add((effect, receipt));
            }
            if (planned.Count == 0) return true;

            var applied = new List<EffectBefore>();
            foreach (var item in planned)
            {
                var before = CaptureBefore(item.effect, session, item.receipt);
                if (!StoryDialogueEffectRuntime.TryCommit(item.effect, session, out string error))
                {
                    // 逆序回滚已经执行的项：先恢复事实/账目快照，再撤销真实资源（钱/物品/关系）。
                    for (int i = applied.Count - 1; i >= 0; i--)
                    {
                        RestoreBefore(applied[i]);
                        if (!StoryDialogueEffectRuntime.TryRollback(applied[i].Effect, session, out string rollbackError))
                            DebugLogger.Log("[StoryDialogue] rollback FAILED " + applied[i].Effect.Id + ": " + rollbackError);
                    }
                    session.Failed = true;
                    session.EndReason = "effect " + item.effect.Id + " failed: " + error;
                    DebugLogger.Log("[StoryDialogue] effect FAILED " + item.effect.Id + " @" + owner + ": " + error
                                    + " (rolled back " + applied.Count + ")");
                    return false;
                }
                applied.Add(before);
            }

            foreach (var item in applied)
            {
                if (item.Effect.Receipt == "once") StoryProbeState.RecordDialogueReceipt(item.Receipt);
                if (item.Effect.Receipt == "session") session.SessionReceipts.Add(item.Receipt);
                session.Committed.Add(owner + " -> " + item.Effect.Id + " (" + item.Effect.Receipt + ")");
                DebugLogger.Log("[StoryDialogue] effect ok " + item.Effect.Id + " @" + owner + " receipt=" + item.Effect.Receipt);
            }
            return true;
        }

        /// <summary>效果提交前的事实/账目快照（只覆盖可逆的账目类原子）。</summary>
        private sealed class EffectBefore
        {
            public StoryDialogueEffect Effect;
            public string Receipt;
            public string Actor, Flag;
            public bool Asked, Hazard, Clue;
            public int Step, BribePaid, CoinsFound, DebtCollected;
            public List<string> QuestDone;
            public Dictionary<string, string> QuestResolutions;
            public SingNpcMemorySystem FavorMemory;
            public List<SingNpcMemorySystem.FavorRecord> FavorRecords;
        }

        private static EffectBefore CaptureBefore(StoryDialogueEffect effect, StoryDialogueSession session, string receipt)
        {
            var before = new EffectBefore { Effect = effect, Receipt = receipt, Actor = session?.Request?.ActorId };
            string P(string key) => effect?.Params != null && effect.Params.TryGetValue(key, out var v) ? v : null;
            switch (effect?.Id)
            {
                case "favor_grant":
                    var hero = StoryProbeMissionBehavior.Current?.GetHero(P("npc"));
                    before.FavorMemory = hero != null ? AllNpcMemoryManager.GetMemory(hero.StringId) : null;
                    before.FavorRecords = before.FavorMemory?.SnapshotFavorRecords();
                    break;
                case "mark":
                    before.Flag = P("flag");
                    before.Asked = StoryProbeState.HasAsked(before.Actor, before.Flag);
                    before.Step = StoryProbeState.Step(before.Actor);
                    break;
                case "hazard":
                    before.Flag = P("flag");
                    before.Hazard = StoryProbeState.HasHazard(before.Flag);
                    break;
                case "clue":
                    before.Flag = P("clue");
                    before.Clue = StoryProbeState.HasClue(before.Flag);
                    break;
            }
            before.BribePaid = StoryProbeState.Data?.BribePaid ?? 0;
            before.CoinsFound = StoryProbeState.Data?.CoinsFound ?? 0;
            before.DebtCollected = StoryProbeState.Data?.DebtCollected ?? 0;
            if (StoryProbeState.Data != null)
            {
                before.QuestDone = new List<string>(StoryProbeState.Data.QuestDone ?? new List<string>());
                before.QuestResolutions = StoryProbeState.Data.QuestResolutions != null
                    ? new Dictionary<string, string>(StoryProbeState.Data.QuestResolutions)
                    : new Dictionary<string, string>();
            }
            return before;
        }

        /// <summary>把事实/账目恢复成提交前的样子（不是「反过来做一次」）。</summary>
        private static void RestoreBefore(EffectBefore before)
        {
            if (before?.Effect == null) return;
            switch (before.Effect.Id)
            {
                case "favor_grant":
                    before.FavorMemory?.RestoreFavorRecords(before.FavorRecords);
                    break;
                case "mark":
                    StoryProbeState.RestoreAsked(before.Actor, before.Flag, before.Asked);
                    StoryProbeState.RestoreStep(before.Actor, before.Step);
                    break;
                case "hazard":
                    StoryProbeState.RestoreHazard(before.Flag, before.Hazard);
                    break;
                case "clue":
                    StoryProbeState.RestoreClue(before.Flag, before.Clue);
                    break;
            }
            if (StoryProbeState.Data != null)
            {
                StoryProbeState.Data.BribePaid = before.BribePaid;
                StoryProbeState.Data.CoinsFound = before.CoinsFound;
                StoryProbeState.Data.DebtCollected = before.DebtCollected;
                if (before.QuestDone != null && StoryProbeState.Data.QuestDone != null)
                {
                    StoryProbeState.Data.QuestDone.Clear();
                    StoryProbeState.Data.QuestDone.AddRange(before.QuestDone);
                }
                if (before.QuestResolutions != null && StoryProbeState.Data.QuestResolutions != null)
                {
                    StoryProbeState.Data.QuestResolutions.Clear();
                    foreach (var kv in before.QuestResolutions)
                        StoryProbeState.Data.QuestResolutions[kv.Key] = kv.Value;
                }
            }
        }

        /// <summary>同名效果（同动词/参数/阶段/收据）在本列表里是第几次出现 —— 稳定防重身份。</summary>
        private static int OccurrenceIndex(List<StoryDialogueEffect> effects, int index)
        {
            var effect = effects[index];
            int seen = 0;
            for (int i = 0; i < index; i++)
            {
                var other = effects[i];
                if (other == null) continue;
                if (!string.Equals(other.Id, effect.Id, StringComparison.Ordinal)) continue;
                if (!string.Equals(other.Commit, effect.Commit, StringComparison.Ordinal)) continue;
                if (!string.Equals(other.Receipt, effect.Receipt, StringComparison.Ordinal)) continue;
                if (!SameParams(other.Params, effect.Params)) continue;
                seen++;
            }
            return seen;
        }

        private static bool SameParams(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            int an = a?.Count ?? 0, bn = b?.Count ?? 0;
            if (an != bn) return false;
            if (an == 0) return true;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out string other) || !string.Equals(other, kv.Value, StringComparison.Ordinal))
                    return false;
            return true;
        }

        /// <summary>
        /// 原版犯罪/投降入口优先：只阻止当前对象真正需要接管开场的剧情请求。
        /// 全城悬赏或无关案件不能让商贩、路人等所有剧情演员失去对话。
        /// </summary>
        private static bool VanillaHasPriority(string actorId, out string why)
        {
            why = null;
            if (ConversationEntryPatch._pendingTrigger != DialogueTrigger.Normal)
            {
                why = "pending " + ConversationEntryPatch._pendingTrigger;
                return true;
            }
            string settlement = StoryProbeSettings.SettlementId;
            if (string.IsNullOrEmpty(settlement)) return false;
            // 🔴 双源查询（铁律 9）：`WorldEventStore.FindOnGoing` 内部已含 PendingWorldEvent 兜底。
            // 只读 Pending 会漏掉已落库、仍在进行中的案子 —— 原版犯罪/投降入口就抢不到了。
            var evt = WorldEventStore.FindOnGoing(settlement);
            var speaker = StoryProbeMissionBehavior.Current?.GetHero(actorId);
            if (speaker == null || !CrimeDialogueBuilder.NeedsEarlyInjection(speaker, evt)) return false;
            why = "ongoing crime " + (evt.EventId ?? "-") + " at " + (evt.TargetSettlementId ?? "-");
            return true;
        }

        /// <summary>
        /// 收据身份 = 对话 + 来源节点/选项 + **同名效果序号** + 动作 token + 参数；
        /// 个人事实（mark/clue/hazard）再带实际演员，避免两名守卫互相吞收据。
        /// 🔴 序号不是列表下标：插入/换序无关效果不得让同一笔付款再扣一次。
        /// </summary>
        private static string ReceiptKey(StoryDialogueSession session, StoryDialogueEffect effect, string owner, int ordinal)
        {
            var sb = new StringBuilder();
            sb.Append(session.Definition.DialogueId).Append(':').Append(owner).Append(':').Append(ordinal)
              .Append(':').Append(effect.Id);
            if (effect.Id == "mark" || effect.Id == "clue" || effect.Id == "hazard")
                sb.Append(":actor=").Append(session.Request?.ActorId ?? "-");
            if (effect.Params != null)
                foreach (var kv in effect.Params.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
            return sb.ToString();
        }

        /// <summary>只读诊断：当前/最近一次会话，逐项可追溯。</summary>
        public static string Describe()
        {
            var s = _current ?? _last;
            if (s == null) return "OK: no dialogue session";
            var sb = new StringBuilder();
            sb.Append(s == _current ? "OK: active session " : "OK: last session ").Append(s.Id)
              .Append(" dialogue=").Append(s.Definition?.DialogueId ?? "-")
              .Append(" v").Append(s.Definition?.ContentVersion ?? 0)
              .Append(" trigger=").Append(s.Trigger?.Id ?? "-")
              .Append(" source=").Append(s.Request?.Source ?? "-")
              .Append(" actor=").Append(s.Request?.ActorId ?? "-")
              .Append(" entry=").Append(s.Entry?.Id ?? "-").Append("->").Append(s.Entry?.Node ?? "-")
              .Append(" node=").Append(s.CurrentNode ?? "-")
              .Append(" speaker=").Append(s.Definition?.FindNode(s.CurrentNode)?.Speaker ?? "-")
              .Append(" listener=").Append(s.Definition?.FindNode(s.CurrentNode)?.Listener ?? "-")
              .Append(" next=").Append(s.Definition?.FindNode(s.CurrentNode)?.NextNode ?? "options/close")
              .Append(" request=").Append(s.Request?.Id ?? 0)
              .Append(" token=").Append(s.Request?.RuntimeToken ?? 0)
              .Append(" ended=").Append(s.Ended)
              .Append(" failed=").Append(s.Failed);
            if (!string.IsNullOrEmpty(s.EndReason)) sb.Append(" reason=").Append(s.EndReason);
            foreach (string line in s.Committed) sb.Append('\n').Append("  ").Append(line);
            foreach (var kv in s.Facts) sb.Append("\n  fact ").Append(kv.Key).Append('=').Append(kv.Value);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 效果原子执行器（P0-D.4）。数据只写「已知原子 + 参数」，这里负责复核与落地。
    /// 🔴 所有落地都复用既有管线（转账 / 关系 / 战斗 / 物品 / 事件），不另造账。
    /// </summary>
    public static class StoryDialogueEffectRuntime
    {
        /// <summary>
        /// 纯预检：不落地任何世界变化。批量提交前先全部过一遍，坏输入/缺物品/缺角色在这里拒绝。
        /// </summary>
        public static bool TryPreflight(StoryDialogueEffect effect, StoryDialogueSession session, out string error)
        {
            error = null;
            if (effect == null || session == null) { error = "effect/session null"; return false; }
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) { error = "host inactive"; return false; }
            string actor = session.Request?.ActorId;
            string P(string key) => effect.Params != null && effect.Params.TryGetValue(key, out var v) ? v : null;

            switch (effect.Id)
            {
                // 🔴 事实类原子的参数名以 `TryCommit` 为准：mark/hazard 都读 `flag`，clue 读 `clue`。
                // 预检按「动作名」取参数会把合法 hazard 判成缺参数（2026-11-15 复核反例）。
                case "mark":
                case "hazard":
                    if (string.IsNullOrEmpty(P("flag"))) { error = effect.Id + ".flag missing"; return false; }
                    return true;
                case "clue":
                    if (string.IsNullOrEmpty(P("clue"))) { error = "clue.clue missing"; return false; }
                    return true;
                case "pay":
                    if (!TryInt(P("amount"), out int amount) || amount < 0) { error = "pay.amount invalid"; return false; }
                    string to = P("to");
                    string recipientId = string.IsNullOrEmpty(to) || to == "actor" || to == "questioner" ? actor : to;
                    if (host.GetHero(recipientId) == null) { error = "pay recipient missing: " + recipientId; return false; }
                    return true;
                case "relation":
                    if (string.IsNullOrEmpty(P("faction"))) { error = "relation.faction missing"; return false; }
                    if (!TryInt(P("delta"), out _)) { error = "relation.delta invalid"; return false; }
                    return true;
                case "give_item":
                    if (string.IsNullOrEmpty(P("item"))) { error = "give_item.item missing"; return false; }
                    int count = 1;
                    if (P("count") != null && (!TryInt(P("count"), out count) || count <= 0))
                    { error = "give_item.count invalid"; return false; }
                    if (!StoryProbeDefs.CanGiveItem(P("item"), count)) { error = "item cannot be granted: " + P("item"); return false; }
                    return true;
                case "take_item":
                {
                    if (string.IsNullOrEmpty(P("item"))) { error = "take_item.item missing"; return false; }
                    int takeCount = 1;
                    if (P("count") != null && (!TryInt(P("count"), out takeCount) || takeCount <= 0))
                    { error = "take_item.count invalid"; return false; }
                    if (StoryProbeDefs.FindItem(P("item")) == null)
                    { error = "take_item item not found: " + P("item"); return false; }
                    if (CountPlayerItem(P("item")) < takeCount)
                    { error = "player lacks item: " + P("item"); return false; }
                    return true;
                }
                case "take_carried":
                {
                    string npc = P("npc"), item = P("item");
                    if (string.IsNullOrEmpty(npc) || string.IsNullOrEmpty(item))
                    { error = "take_carried.npc/item missing"; return false; }
                    var carrier = host.GetAgent(npc);
                    if (carrier == null
                        || (!CarriesItem(carrier, item) && CountPlayerItem(item) < 1))
                    { error = "npc does not carry item: " + npc + "/" + item; return false; }
                    return true;
                }
                case "wear_item":
                {
                    if (string.IsNullOrEmpty(P("item"))) { error = "wear_item.item missing"; return false; }
                    if (StoryProbeDefs.FindItem(P("item")) == null)
                    { error = "wear_item item not found: " + P("item"); return false; }
                    if (P("slot") != null && !Enum.TryParse(P("slot"), true, out TaleWorlds.Core.EquipmentIndex _))
                    { error = "wear_item.slot invalid: " + P("slot"); return false; }
                    return true;
                }
                case "open_gate":
                    if (string.IsNullOrEmpty(P("route"))) { error = "open_gate.route missing"; return false; }
                    if (P("delta") != null && !TryInt(P("delta"), out _)) { error = "open_gate.delta invalid"; return false; }
                    return true;
                case "start_fight":
                {
                    string target = P("target");
                    if (string.IsNullOrEmpty(target) || target == "actor" || target == "questioner") target = actor;
                    if (string.IsNullOrEmpty(target) || host.GetAgent(target) == null)
                    { error = "start_fight target missing: " + (target ?? "-"); return false; }
                    if (P("mode") != null && P("mode") != "wound" && P("mode") != "mortal")
                    { error = "start_fight mode invalid"; return false; }
                    if (!string.IsNullOrEmpty(P("beneficiaries"))
                        && P("beneficiaries").Split('|').Any(id => host.GetAgent(id) == null))
                    { error = "start_fight beneficiary missing"; return false; }
                    if (P("resolve_event") != null && (!TryInt(P("resolve_event"), out int resolvedEvent)
                        || StoryProbeDefs.GetEvent(resolvedEvent) == null))
                    { error = "start_fight resolve_event missing"; return false; }
                    return true;
                }
                case "detain_player":
                {
                    string target = P("target");
                    if (string.IsNullOrEmpty(target) || target == "actor" || target == "questioner") target = actor;
                    if (!host.CanRequestPlayerDetention(target, P("mode")))
                    { error = "detain_player unavailable: " + (target ?? "-"); return false; }
                    return true;
                }
                case "skill_check":
                    if (string.IsNullOrEmpty(P("skill"))) { error = "skill_check.skill missing"; return false; }
                    return StoryProbeDefs.TryParseAbility(P("skill"), out _);
                case "resolve_event":
                    return TryInt(P("event"), out int evt) && evt > 0;
                case "collect_debt":
                    return TryInt(P("amount"), out int debt) && debt > 0;
                case "errand":
                    return TryInt(P("pay"), out int pay) && pay > 0;
                case "complete_quest":
                    return !string.IsNullOrEmpty(P("npc")) && !string.IsNullOrEmpty(P("resolution"))
                        && (P("if_accepted") == null || P("if_accepted") == "1");
                case "start_event":
                    return TryInt(P("event"), out int se) && se > 0;
                case "quest_accept":
                    return !string.IsNullOrEmpty(P("npc"));
                case "quest_turn_in":
                    return !string.IsNullOrEmpty(P("npc"));
                case "favor_grant":
                    // 同组前面的交差/事件效果尚未执行；预检只核结构和身份，提交时再核真实结果。
                    return host.GetHero(P("npc")) != null && TaleWorlds.CampaignSystem.Hero.MainHero != null
                        && TryInt(P("amount"), out int favorAmount) && favorAmount > 0
                        && !string.IsNullOrWhiteSpace(P("source")) && !string.IsNullOrWhiteSpace(P("key"))
                        && !string.IsNullOrWhiteSpace(P("fallback"))
                        && (P("quest") == null || StoryProbeDefs.GetNpc(P("quest")) != null)
                        && (P("event") == null || (TryInt(P("event"), out int favorEvent)
                            && StoryProbeDefs.GetEvent(favorEvent) != null));
                case "favor_spend":
                    // 消耗人情（余额在提交时核，不足则整条效果失败、效果不落地）。
                    return host.GetHero(P("npc")) != null && TaleWorlds.CampaignSystem.Hero.MainHero != null
                        && TryInt(P("amount"), out int favorCost) && favorCost > 0
                        && !string.IsNullOrWhiteSpace(P("source")) && !string.IsNullOrWhiteSpace(P("key"))
                        && !string.IsNullOrWhiteSpace(P("fallback"));
                case "debt_collect":
                    return !string.IsNullOrEmpty(P("from")) && !string.IsNullOrEmpty(P("quest"))
                           && TryInt(P("amount"), out int dc) && dc > 0;
                case "grant_gold":
                    return TryInt(P("amount"), out int gg) && gg > 0;
                case "reward_receipt":
                    return !string.IsNullOrEmpty(P("npc")) && !string.IsNullOrEmpty(P("key"));
                case "lesson_start":
                    if (host.Practice == null) { error = "lesson data unavailable"; return false; }
                    return host.Practice.CanStart(P("lesson"), out error);
                case "lesson_cancel": return true;
                case "message":
                    return !string.IsNullOrEmpty(P("key"));
                case "coop_contract":
                case "coop_recruit":
                {
                    var mode = P("mode");
                    if (string.IsNullOrEmpty(mode)) { error = effect.Id + ".mode missing"; return false; }
                    var eligibility = Planner.Cooperation.CoopRecruitment.Evaluate(actor);
                    if (!eligibility.Ok) { error = "recruit not eligible: " + eligibility.Reason; return false; }
                    if (effect.Id == "coop_recruit" && string.Equals(mode, "paid", StringComparison.OrdinalIgnoreCase)
                        && TaleWorlds.CampaignSystem.Hero.MainHero.Gold < eligibility.Price)
                    {
                        error = "gold insufficient";
                        return false;
                    }
                    return true;
                }
                case "coop_start":
                {
                    var behavior = Planner.Cooperation.CoopMissionBehavior.Current;
                    if (behavior == null) { error = "coop host unavailable"; return false; }
                    if (string.IsNullOrEmpty(P("role"))) { error = "coop_start.role missing"; return false; }
                    if (!Planner.Cooperation.CoopRecruitment.CanCooperate(actor, null, out error)) return false;
                    return true;
                }
                default:
                    return true;   // 迁移桥动词由既有管线自行复核
            }
        }

        /// <summary>回滚已经执行的项（只覆盖可逆的资源/事实；战斗/开门等不可逆项靠预检）。</summary>
        public static bool TryRollback(StoryDialogueEffect effect, StoryDialogueSession session, out string error)
        {
            error = null;
            var host = StoryProbeMissionBehavior.Current;
            if (effect == null || session == null || host == null) { error = "rollback context missing"; return false; }
            string actor = session.Request?.ActorId;
            string P(string key) => effect.Params != null && effect.Params.TryGetValue(key, out var v) ? v : null;
            try
            {
                switch (effect.Id)
                {
                    case "pay":
                    {
                        if (!TryInt(P("amount"), out int amount) || amount <= 0) return true;
                        string to = P("to");
                        string recipientId = string.IsNullOrEmpty(to) || to == "actor" || to == "questioner" ? actor : to;
                        var recipient = host.GetHero(recipientId);
                        return recipient != null && StoryProbeDialogue.RefundMoney(recipient, amount);
                    }
                    case "give_item":
                    {
                        int count = 1;
                        if (P("count") != null) TryInt(P("count"), out count);
                        return StoryProbeDefs.RemoveItem(TaleWorlds.CampaignSystem.Hero.MainHero, P("item"), count);
                    }
                    case "take_item":
                    {
                        int takeCount = 1;
                        if (P("count") != null) TryInt(P("count"), out takeCount);
                        return StoryProbeDefs.GiveItem(TaleWorlds.CampaignSystem.Hero.MainHero, P("item"), takeCount);
                    }
                    case "take_carried":
                    {
                        // 还原：从玩家背包收回，再放回 NPC 的空武器槽。
                        var carrier = host.GetAgent(P("npc"));
                        var obj = StoryProbeDefs.FindItem(P("item"));
                        if (carrier == null || obj == null
                            || !StoryProbeDefs.RemoveItem(TaleWorlds.CampaignSystem.Hero.MainHero, P("item"), 1)) return false;
                        var eq = carrier.SpawnEquipment.Clone();
                        int slot = -1;
                        for (int i = 0; i < 4; i++) if (eq[(EquipmentIndex)i].IsEmpty) { slot = i; break; }
                        if (slot < 0) return false;
                        eq[(EquipmentIndex)slot] = new EquipmentElement(obj);
                        carrier.UpdateSpawnEquipmentAndRefreshVisuals(eq);
                        carrier.UpdateAgentStats();
                        return true;
                    }
                    // mark / hazard / clue 由 CommitEffects 的「提交前快照」恢复（RestoreBefore）；
                    // 这里不再做反向操作 —— 原本就成立的事实被反向删掉才是错。
                    case "grant_gold":
                    {
                        // 凭空发钱：回滚就是把发出去的那笔退回“世界”。
                        if (!TryInt(P("amount"), out int amount) || amount <= 0) return true;
                        var player = TaleWorlds.CampaignSystem.Hero.MainHero;
                        if (player == null) return false;
                        return AgentControlHelper.TransferGold(player, (TaleWorlds.CampaignSystem.Hero)null, amount, notify: false) == amount;
                    }
                    case "debt_collect":
                    {
                        // 收债：钱从欠债人转到玩家；回滚原路退回，Quest 状态由快照/预检兜底。
                        if (!TryInt(P("amount"), out int amount) || amount <= 0) return true;
                        var debtor = host.GetHero(P("from"));
                        var player = TaleWorlds.CampaignSystem.Hero.MainHero;
                        if (debtor == null || player == null) return false;
                        return AgentControlHelper.TransferGold(player, debtor, amount, notify: false) == amount;
                    }
                    case "relation":
                    {
                        string faction = P("faction");
                        if (string.IsNullOrEmpty(faction) || !TryInt(P("delta"), out int delta)) return true;
                        StoryProbeState.AddFactionRelation(faction, -delta);
                        var rep = StoryProbeDefs.GetNpcByFaction(faction);
                        var hero = rep != null ? host.GetHero(rep.Id) : null;
                        if (hero != null && TaleWorlds.CampaignSystem.Hero.MainHero != null && delta != 0)
                            TaleWorlds.CampaignSystem.Actions.ChangeRelationAction
                                .ApplyRelationChangeBetweenHeroes(hero, TaleWorlds.CampaignSystem.Hero.MainHero, -delta);
                        return true;
                    }
                    default:
                        return true;   // 不可逆项不静默假装回滚
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool TryCommit(StoryDialogueEffect effect, StoryDialogueSession session, out string error)
        {
            error = null;
            if (effect == null || session == null) { error = "effect/session null"; return false; }
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) { error = "host inactive"; return false; }

            string actor = session.Request?.ActorId;
            string P(string key) => effect.Params != null && effect.Params.TryGetValue(key, out var v) ? v : null;

            try
            {
                switch (effect.Id)
                {
                    case "lesson_start":
                        if (host.Practice == null) { error = "lesson data unavailable"; return false; }
                        return host.Practice.Start(P("lesson"), out error);
                    case "lesson_cancel": host.ExistingPractice?.Stop("player cancelled"); return true;
                    case "mark":
                    {
                        string flag = P("flag");
                        if (string.IsNullOrEmpty(flag)) { error = "mark.flag missing"; return false; }
                        if (!string.IsNullOrEmpty(actor)) StoryProbeState.MarkAsked(actor, flag);
                        // 实际质问者（任意首遇登记守卫）答过话之后，才允许选硬闯。
                        if (flag == "met" && StoryProbeDefs.GetNpc(actor)?.FirstMeetGuard == true)
                            host.NotifyFirstGuardQuestioned();
                        return true;
                    }

                    case "hazard":
                    {
                        string flag = P("flag");
                        if (string.IsNullOrEmpty(flag)) { error = "hazard.flag missing"; return false; }
                        StoryProbeState.AddHazard(flag);
                        return true;
                    }

                    case "clue":
                    {
                        string clue = P("clue");
                        if (string.IsNullOrEmpty(clue)) { error = "clue.clue missing"; return false; }
                        StoryProbeState.AddClue(clue);
                        StoryProbeState.SetGoal("asked");
                        return true;
                    }

                    case "pay":
                    {
                        if (!TryInt(P("amount"), out int amount) || amount < 0) { error = "pay.amount invalid"; return false; }
                        string to = P("to");
                        string recipientId = string.IsNullOrEmpty(to) || to == "actor" || to == "questioner"
                            ? actor : to;

                        // 台词里写的收款人必须真的在场上；缺人不能拿玩家自己顶上。
                        var recipientHero = host.GetHero(recipientId);
                        if (recipientHero == null) { error = "pay recipient missing: " + recipientId; return false; }
                        if (!StoryProbeDialogue.TryPayMoney(recipientHero, amount)) { error = "payment failed"; return false; }

                        string reason = P("reason");
                        if (reason == "bribe") StoryProbeState.Data.BribePaid += amount;
                        session.Facts["last_pay:" + recipientId] = amount.ToString(CultureInfo.InvariantCulture);
                        return true;
                    }

                    case "relation":
                    {
                        string faction = P("faction");
                        if (string.IsNullOrEmpty(faction)) { error = "relation.faction missing"; return false; }
                        if (!TryInt(P("delta"), out int delta)) { error = "relation.delta invalid"; return false; }
                        StoryProbeState.AddFactionRelation(faction, delta);
                        var rep = StoryProbeDefs.GetNpcByFaction(faction);
                        var hero = rep != null ? host.GetHero(rep.Id) : null;
                        if (hero != null && Hero.MainHero != null && delta != 0)
                            TaleWorlds.CampaignSystem.Actions.ChangeRelationAction
                                .ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, delta);
                        return true;
                    }

                    case "give_item":
                    {
                        string item = P("item");
                        if (string.IsNullOrEmpty(item)) { error = "give_item.item missing"; return false; }
                        int count = 1;
                        if (P("count") != null && (!TryInt(P("count"), out count) || count <= 0))
                        { error = "give_item.count invalid"; return false; }
                        if (Hero.MainHero == null || !StoryProbeDefs.GiveItem(Hero.MainHero, item, count))
                        { error = "item not granted: " + item; return false; }
                        return true;
                    }

                    case "take_item":
                    {
                        string item = P("item");
                        if (string.IsNullOrEmpty(item)) { error = "take_item.item missing"; return false; }
                        int takeCount = 1;
                        if (P("count") != null && (!TryInt(P("count"), out takeCount) || takeCount <= 0))
                        { error = "take_item.count invalid"; return false; }
                        if (Hero.MainHero == null || !StoryProbeDefs.RemoveItem(Hero.MainHero, item, takeCount))
                        { error = "item not taken: " + item; return false; }
                        DebugLogger.Log("[StoryDialogue] take_item " + item + " x" + takeCount + " (handed over)");
                        return true;
                    }

                    case "take_carried":
                    {
                        string npc = P("npc"), item = P("item");
                        if (string.IsNullOrEmpty(npc) || string.IsNullOrEmpty(item))
                        { error = "take_carried.npc/item missing"; return false; }
                        var carrier = host.GetAgent(npc);
                        var obj = StoryProbeDefs.FindItem(item);
                        if (carrier == null || obj == null) { error = "take_carried carrier/item missing"; return false; }
                        int slot = -1;
                        for (int i = 0; i < 12; i++)
                            if (carrier.SpawnEquipment[(EquipmentIndex)i].Item == obj) { slot = i; break; }
                        if (slot < 0)
                        {
                            // 玩家已经先一步拿到（偷/搜刮/买过）——结算不再重复发，也不判失败。
                            DebugLogger.Log("[StoryDialogue] take_carried no-op; player already has " + item);
                            return true;
                        }
                        var eq = carrier.SpawnEquipment.Clone();
                        eq[(EquipmentIndex)slot] = EquipmentElement.Invalid;
                        carrier.UpdateSpawnEquipmentAndRefreshVisuals(eq);
                        carrier.UpdateAgentStats();
                        if (Hero.MainHero == null || !StoryProbeDefs.GiveItem(Hero.MainHero, item, 1))
                        { error = "take_carried transfer failed: " + item; return false; }
                        StoryProbeState.MarkAsked("carry", npc + ":" + item);
                        DebugLogger.Log("[StoryDialogue] take_carried " + item + " from " + npc + " slot=" + slot);
                        return true;
                    }

                    case "wear_item":
                    {
                        if (!StoryProbeMissionBehavior.WearStoryItem(P("item"), P("slot"), out string wearErr))
                        { error = wearErr ?? "wear_item failed"; return false; }
                        return true;
                    }

                    case "open_gate":
                    {
                        string route = P("route");
                        if (string.IsNullOrEmpty(route)) { error = "open_gate.route missing"; return false; }
                        host.OpenGate(route, "LWN_story_gate_open", "The way is clear.");
                        StoryProbeState.Data.ChosenApproach = route;
                        string faction = P("faction");
                        if (!string.IsNullOrEmpty(faction))
                        {
                            int delta = 0;
                            if (P("delta") != null && !TryInt(P("delta"), out delta))
                            { error = "open_gate.delta invalid"; return false; }
                            bool crime = string.Equals(P("crime"), "true", StringComparison.OrdinalIgnoreCase)
                                         || P("crime") == "1";
                            host.ResolveApproach(route, faction, delta, crime, P("hazard"), actor);
                        }
                        return true;
                    }

                    case "force_defeat":
                        host.PlayForcedDefeat();
                        return true;

                    case "start_fight":
                    {
                        string target = P("target");
                        if (string.IsNullOrEmpty(target) || target == "actor" || target == "questioner")
                            target = actor;
                        if (string.IsNullOrEmpty(target)) { error = "start_fight target missing"; return false; }
                        if (!StoryProbeDialogue.StartStoryFight(target, P("mode"), P("reason"), P("beneficiaries"),
                            P("start_line"), P("end_line"), P("resolve_event"))) { error = "fight refused: " + target; return false; }
                        return true;
                    }

                    case "detain_player":
                    {
                        string target = P("target");
                        if (string.IsNullOrEmpty(target) || target == "actor" || target == "questioner") target = actor;
                        if (!host.RequestPlayerDetention(target, P("mode")))
                        { error = "detention refused: " + (target ?? "-"); return false; }
                        return true;
                    }
                    case "accept_quest":
                    case "turn_in":
                    case "street_help_reward":
                    case "redeem_keepsake":
                    case "money_offer":
                    case "money_deal":
                        return StoryProbeDialogue.RunLegacyVerb(effect.Id, null);

                    case "complete_quest":
                    {
                        string npc = P("npc"), resolution = P("resolution");
                        if (P("if_accepted") == "1" && !StoryProbeState.IsQuestAccepted(npc)) return true;
                        if (string.IsNullOrEmpty(npc) || string.IsNullOrEmpty(resolution))
                        { error = "complete_quest needs npc+resolution"; return false; }
                        return StoryProbeDialogue.RunLegacyVerb("complete_quest", npc + "|" + resolution);
                    }

                    case "collect_debt":
                    {
                        if (!TryInt(P("amount"), out int amount) || amount <= 0)
                        { error = "collect_debt.amount invalid"; return false; }
                        return StoryProbeDialogue.RunLegacyVerb("collect_debt",
                            amount.ToString(CultureInfo.InvariantCulture));
                    }

                    case "errand":
                    {
                        if (!TryInt(P("pay"), out int pay) || pay <= 0)
                        { error = "errand.pay invalid"; return false; }
                        return StoryProbeDialogue.RunLegacyVerb("errand",
                            pay.ToString(CultureInfo.InvariantCulture));
                    }

                    case "start_event":
                    {
                        if (!TryInt(P("event"), out int evt) || evt <= 0)
                        { error = "start_event.event invalid"; return false; }
                        host.Director?.ForceEvent(evt);
                        return true;
                    }

                    case "quest_accept":
                    {
                        string npc = P("npc");
                        if (string.IsNullOrEmpty(npc)) { error = "quest_accept.npc missing"; return false; }
                        var nativeQuest = PrologueQuestLauncher.Start(npc);
                        if (nativeQuest == null) { error = "quest_accept failed: " + npc; return false; }
                        StoryProbeState.AcceptQuest(npc);
                        StoryProbeState.SetGoal("quest");
                        if (TryInt(P("solved_event"), out int solved) && solved > 0
                            && StoryProbeState.IsStreetEventResolved(solved)
                            && !StoryProbeDialogue.SyncResolvedStreetQuest(solved))
                        { error = "quest event sync failed: " + solved; return false; }
                        return true;
                    }

                    case "quest_turn_in":
                    {
                        string npc = P("npc");
                        if (string.IsNullOrEmpty(npc)) { error = "quest_turn_in.npc missing"; return false; }
                        StoryProbeDialogue.TurnInFor(npc);
                        return StoryProbeState.IsQuestTurnedIn(npc);
                    }

                    case "favor_grant":
                    {
                        if (!TryInt(P("amount"), out int amount) || amount <= 0)
                        { error = "favor_grant amount invalid"; return false; }
                        if (P("quest") != null && !StoryProbeState.IsQuestTurnedIn(P("quest")))
                        { error = "favor_grant quest not turned in"; return false; }
                        if (P("event") != null && (!TryInt(P("event"), out int evt)
                            || !StoryProbeState.IsStreetEventResolved(evt)))
                        { error = "favor_grant event not resolved"; return false; }
                        var hero = host.GetHero(P("npc"));
                        var memory = hero != null ? AllNpcMemoryManager.GetMemory(hero.StringId) : null;
                        if (AgentControlHelper.ChangePlayerFavor(memory, amount, "story:" + P("source"),
                            P("key"), P("fallback"))) return true;
                        error = "favor_grant rejected";
                        return false;
                    }

                    case "favor_spend":
                    {
                        if (!TryInt(P("amount"), out int cost) || cost <= 0)
                        { error = "favor_spend amount invalid"; return false; }
                        var spendHero = host.GetHero(P("npc"));
                        var spendMemory = spendHero != null ? AllNpcMemoryManager.GetMemory(spendHero.StringId) : null;
                        var player = TaleWorlds.CampaignSystem.Hero.MainHero;
                        if (spendMemory == null || player == null)
                        { error = "favor_spend npc missing"; return false; }
                        if (spendMemory.FavorBalance(player.StringId) < cost)
                        { error = "favor_spend insufficient balance"; return false; }
                        if (AgentControlHelper.ChangePlayerFavor(spendMemory, -cost, "story:" + P("source"),
                            P("key"), P("fallback"))) return true;
                        error = "favor_spend rejected";
                        return false;
                    }

                    case "debt_collect":
                    {
                        string from = P("from"), quest = P("quest");
                        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(quest))
                        { error = "debt_collect from/quest missing"; return false; }
                        if (!TryInt(P("amount"), out int amount) || amount <= 0)
                        { error = "debt_collect.amount invalid"; return false; }
                        var debtor = host.GetHero(from);
                        var player = TaleWorlds.CampaignSystem.Hero.MainHero;
                        if (debtor == null || player == null) { error = "debt_collect actor missing"; return false; }
                        int before = player.Gold;
                        int moved = AgentControlHelper.TransferGold(debtor, player, amount, notify: false);
                        if (moved != amount || player.Gold - before != amount)
                        { error = "debt_collect transfer failed"; return false; }
                        StoryProbeState.Data.DebtCollected += amount;
                        PrologueQuestLauncher.FindActive(quest)?.MarkObjectivesDone();
                        StoryProbeState.Data.QuestResolutions[quest] = P("resolution") ?? "charm";
                        StoryProbeState.CompleteQuest(quest);
                        return true;
                    }

                    case "grant_gold":
                    {
                        if (!TryInt(P("amount"), out int amount) || amount <= 0)
                        { error = "grant_gold.amount invalid"; return false; }
                        var player = TaleWorlds.CampaignSystem.Hero.MainHero;
                        if (player == null) { error = "grant_gold: no player"; return false; }
                        int before = player.Gold;
                        AgentControlHelper.TransferGold((TaleWorlds.CampaignSystem.Hero)null, player, amount, notify: false);
                        if (player.Gold - before != amount) { error = "grant_gold not applied"; return false; }
                        StoryProbeState.Data.CoinsFound += amount;
                        return true;
                    }

                    case "reward_receipt":
                        if (string.IsNullOrEmpty(P("npc")) || string.IsNullOrEmpty(P("key")))
                        { error = "reward_receipt npc/key missing"; return false; }
                        StoryProbeState.RecordRewardReceipt(P("npc"), P("key"));
                        return true;

                    case "message":
                        if (string.IsNullOrEmpty(P("key"))) { error = "message.key missing"; return false; }
                        StoryProbeDialogue.ShowMessageKey(P("key"));
                        return true;

                    case "coop_contract":
                    {
                        var mode = P("mode");
                        if (!string.Equals(mode, "temporary", StringComparison.OrdinalIgnoreCase))
                        { error = "coop_contract.mode must be temporary"; return false; }
                        var behavior = Planner.Cooperation.CoopMissionBehavior.Current;
                        double sim = behavior != null ? behavior.SimTime : 0;
                        return Planner.Cooperation.CoopRecruitment.TrySetTemporary(actor, P("purpose"), sim, out error);
                    }
                    case "coop_recruit":
                    {
                        var behavior = Planner.Cooperation.CoopMissionBehavior.Current;
                        double sim = behavior != null ? behavior.SimTime : 0;
                        var mode = P("mode") ?? string.Empty;
                        if (string.Equals(mode, "paid", StringComparison.OrdinalIgnoreCase))
                        {
                            return Planner.Cooperation.CoopRecruitment.TryCommitPaid(actor, sim, out error);
                        }
                        if (string.Equals(mode, "charm", StringComparison.OrdinalIgnoreCase))
                        {
                            return Planner.Cooperation.CoopRecruitment.TryCommitCharm(actor, sim, out error);
                        }
                        error = "coop_recruit.mode must be paid or charm";
                        return false;
                    }
                    case "coop_start":
                    {
                        var behavior = Planner.Cooperation.CoopMissionBehavior.Current;
                        if (behavior == null) { error = "coop host unavailable"; return false; }
                        var started = behavior.StartCooperation(actor, P("role"), out error, preview: true);
                        return started != null;
                    }

                    case "skill_check":
                    {
                        string tactic = P("tactic");
                        if (!StoryProbeDefs.TryParseDialogueTactic(tactic, out var parsed))
                        { error = "skill_check unknown tactic: " + tactic; return false; }
                        var roll = StoryProbeDefs.RollDialogueCheck(actor, parsed);
                        if (roll == null) { error = "skill_check target unavailable"; return false; }
                        try
                        {
                            TaleWorlds.Library.InformationManager.DisplayMessage(
                                new TaleWorlds.Library.InformationMessage(roll.PlayerMessage()));
                        }
                        catch { }
                        session.Facts["check:" + tactic] = roll.Success ? "success" : "fail";
                        return true;   // 检定本身总是已提交；成功/失败由数据路由
                    }

                    case "resolve_event":
                    {
                        if (!TryInt(P("event"), out int eventId) || eventId <= 0)
                        { error = "resolve_event.event invalid"; return false; }
                        if (StoryProbeState.IsStreetEventResolved(eventId)) return true;   // 幂等
                        if (host.Director?.ResolveCurrentEvent(eventId) != true)
                        { error = "event " + eventId + " is not the active event"; return false; }
                        if (!StoryProbeDialogue.SyncResolvedStreetQuest(eventId))
                        { error = "quest sync failed for event " + eventId; return false; }
                        return true;
                    }

                    default:
                        error = "unregistered effect '" + effect.Id + "'";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryInt(string text, out int value)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static int CountPlayerItem(string itemId)
        {
            var item = StoryProbeDefs.FindItem(itemId);
            var roster = Hero.MainHero?.PartyBelongedTo?.ItemRoster;
            return item != null && roster != null ? roster.GetItemNumber(item) : 0;
        }

        private static bool CarriesItem(Agent carrier, string itemId)
        {
            var item = StoryProbeDefs.FindItem(itemId);
            if (item == null) return false;
            for (int i = 0; i < 12; i++)
                if (carrier.SpawnEquipment[(EquipmentIndex)i].Item == item) return true;
            return false;
        }
    }
}

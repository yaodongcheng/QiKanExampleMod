using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章对话构建器（2026-11-12，plans/逆水寒511-序章对话树.md）。
    ///
    /// 分工：
    ///   · 数据 = `ModuleData/DesignData/Dialogues/story_probe_&lt;npc&gt;.json`（节点 / 选项 / 路由 / 条件 / 检定标签），
    ///     文本 = Languages XML 的 `LWN_story_*` 键（铁律 13）。
    ///   · 本类只做四件事：**挑入口节点 → 按状态过滤选项 → 交给 DialogueInjector 注入 → 执行 PROBE 动作/检定**。
    ///
    /// 🔴 注入时机：必须早于引擎评估 start token。统一由 ConversationEntryPatch 的
    /// `MissionConversationStartPatch.Prefix` 调 `TryInjectEarly(agent)`（全项目唯一收口）。
    /// </summary>
    public static class StoryProbeDialogue
    {
        /// <summary>调用方指定入口节点（事件接入 / 命令强制）；消费一次即清。</summary>
        public static string PendingEntryNode;

        private static string _lastTag;
        private static bool _actionFailed; // 本次原版对话付款失败后，所有后续节点动作都停止。
        private static int _conversationRound;

        /// <summary>本次注入对应的 NPC（动作执行时用来定位"谁在说话"）。</summary>
        public static string CurrentNpcId;
        private static readonly Dictionary<string, DialogueInjector.DialogueInjectScript> _cache = new Dictionary<string, DialogueInjector.DialogueInjectScript>();

        // ═══════════════════════════════════════════════════════════════
        // 入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// StartConversation 之前调用：给"这次对话的对象"注入序章对话树。
        /// 非序章 NPC / 未启用 / 加载失败一律静默返回（原版对话照常）。
        /// </summary>
        public static void TryInjectEarly(Agent agent)
        {
            try
            {
                string npcId = ResolveNpcId(agent);
                if (string.IsNullOrEmpty(npcId))
                {
                    // 不是剧情 NPC：清掉上次残留（对话激活中禁止删，交给 EndConversation）
                    if (_lastTag != null && !ConversationEntryPatch.IsConversationActive())
                    {
                        DialogueInjector.RemoveRelatedLines(_lastTag);
                        _lastTag = null;
                    }
                    return;
                }

                var script = Load(npcId);
                if (script == null || script.Nodes == null || script.Nodes.Count == 0)
                {
                    DebugLogger.Log($"[StoryProbe] no dialogue data for '{npcId}'");
                    PendingEntryNode = null;
                    return;
                }

                string entry = PendingEntryNode;
                PendingEntryNode = null;
                if (string.IsNullOrEmpty(entry)) entry = PickEntryNode(npcId, script);

                var built = BuildFiltered(script, npcId, entry);
                if (built == null)
                {
                    DebugLogger.Log($"[StoryProbe] build filtered script failed npc={npcId} entry={entry}");
                    return;
                }

                if (!ConversationEntryPatch.IsConversationActive())
                {
                    // 只在一段新 talk 开始清失败；同一对话节点重入不能恢复已失败付款的奖励资格。
                    _actionFailed = false;
                    unchecked { _conversationRound++; }
                    if (_lastTag != null) DialogueInjector.RemoveRelatedLines(_lastTag);
                    DialogueInjector.RemoveRelatedLines("story_probe_" + npcId);
                }

                // 两个钩子：检定与 PROBE 动作
                DialogueInjector.CustomCheck = RunCheck;
                DialogueInjector.CustomAction = HandleAction;

                CurrentNpcId = npcId;
                _lastTag = "story_probe_" + npcId;
                string result = DialogueInjector.InjectScript(built, _lastTag);
                DebugLogger.Log($"[StoryProbe] inject npc={npcId} entry={entry} nodes={built.Nodes.Count} -> {result}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] TryInjectEarly failed: {ex}");
                PendingEntryNode = null;
            }
        }

        /// <summary>对话结束（ConversationManager.EndConversation postfix 调用）：清理注入的台词，释放钩子。</summary>
        public static void OnConversationEnded()
        {
            try
            {
                if (_lastTag != null)
                {
                    DialogueInjector.RemoveRelatedLines(_lastTag);
                    _lastTag = null;
                }
                DialogueInjector.CustomCheck = null;
                DialogueInjector.CustomAction = null;
                PendingEntryNode = null;
                CurrentNpcId = null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] OnConversationEnded failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 身份 / 入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Agent → 序章 NPC id（查 Mission 行为里那张表；不是剧情 NPC 返回 null）。</summary>
        public static string ResolveNpcId(Agent agent)
        {
            if (agent == null) return null;
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) return null;
            foreach (var kv in host.AllAgents)
                if (kv.Value == agent) return kv.Key;
            return null;
        }

        /// <summary>入口优先级（对话树文档 §三）：交差 → 熟人 → 已搭话 → 从未见过。</summary>
        private static string PickEntryNode(string npcId, DialogueInjector.DialogueInjectScript script)
        {
            var def = StoryProbeDefs.GetNpc(npcId);
            if (def != null && def.IsBlocker)
            {
                string first = StoryProbeState.Phase == StoryProbePhase.FirstAttempt ? "blocker_first" : "blocker_open";
                return HasNode(script, first) ? first : npcId + "_open";
            }

            string want;
            if (StoryProbeState.IsQuestDone(npcId) && !StoryProbeState.IsQuestTurnedIn(npcId)) want = npcId + "_turnin";
            else if (StoryProbeState.IsQuestTurnedIn(npcId)) want = npcId + "_chat";
            else if (StoryProbeState.Step(npcId) >= 1) want = npcId + "_hub";
            else want = npcId + "_intro";

            if (HasNode(script, want)) return want;

            // 回落链：hub → intro → 第一个节点
            if (HasNode(script, npcId + "_hub")) return npcId + "_hub";
            if (HasNode(script, npcId + "_intro")) return npcId + "_intro";
            return script.Nodes[0].Id;
        }

        private static bool HasNode(DialogueInjector.DialogueInjectScript script, string id)
            => script?.Nodes != null && script.Nodes.Any(n => n != null && n.Id == id);

        // ═══════════════════════════════════════════════════════════════
        // 数据加载 + 过滤
        // ═══════════════════════════════════════════════════════════════

        public static string DialogueFolder
        {
            get
            {
                try
                {
                    var dll = typeof(StoryProbeDialogue).Assembly.Location;
                    var modDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)));
                    return Path.Combine(modDir ?? "", "ModuleData", "DesignData", "Dialogues");
                }
                catch { return null; }
            }
        }

        public static DialogueInjector.DialogueInjectScript Load(string npcId, bool forceReload = false)
        {
            if (!forceReload && _cache.TryGetValue(npcId, out var cached)) return cached;

            string folder = DialogueFolder;
            if (string.IsNullOrEmpty(folder)) return null;
            string path = Path.Combine(folder, "story_probe_" + npcId + ".json");
            if (!File.Exists(path))
            {
                DebugLogger.Log($"[StoryProbe] dialogue file missing: {path}");
                return null;
            }

            try
            {
                var script = JsonConvert.DeserializeObject<DialogueInjector.DialogueInjectScript>(File.ReadAllText(path));
                if (script?.Nodes == null) return null;
                _cache[npcId] = script;
                return script;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] dialogue parse failed {path}: {ex.Message}");
                return null;
            }
        }

        public static void ClearCache() => _cache.Clear();

        /// <summary>按当前状态过滤出一个可注入的副本（原数据不动）。</summary>
        private static DialogueInjector.DialogueInjectScript BuildFiltered(DialogueInjector.DialogueInjectScript src, string npcId, string entry)
        {
            var result = new DialogueInjector.DialogueInjectScript
            {
                Npc = npcId,
                EntryNode = entry,
                EntryOption = src.EntryOption,
                InjectAtToken = src.InjectAtToken,
                SkipVanillaOpening = false,
                Nodes = new List<DialogueInjector.DialogueNode>(),
            };

            foreach (var node in src.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Id)) continue;

                var copy = new DialogueInjector.DialogueNode
                {
                    Id = node.Id,
                    NpcKey = node.NpcKey,
                    NpcFallback = node.NpcFallback,
                    NpcLine = node.NpcLine,
                    NpcActions = node.NpcActions,
                    EnterAction = node.EnterAction,
                    EnterParam = node.EnterParam,
                    Transitions = new List<DialogueInjector.DialogueTransition>(),
                };

                // 进入节点时执行一次（第一次求值台词时触发）——用于"他说完这句就把东西给你"这类副作用
                if (!string.IsNullOrEmpty(node.EnterAction))
                {
                    string enterVerb = node.EnterAction;
                    string enterParam = node.EnterParam;
                    string key = node.NpcKey;
                    string fb = node.NpcFallback ?? node.NpcLine;
                    bool fired = false;
                    copy.LazyNpcLine = () =>
                    {
                        if (!fired)
                        {
                            fired = true;
                            try { HandleAction(enterVerb, enterParam); }
                            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] enter action {enterVerb} failed: {ex.Message}"); }
                        }
                        return LWNTextHelper.ResolveText(key, fb);
                    };
                }

                if (node.Transitions != null)
                {
                    foreach (var tr in node.Transitions)
                    {
                        if (tr == null) continue;
                        if (!EvaluateRequires(tr.Requires, npcId)) continue;
                        if (!string.IsNullOrEmpty(tr.Once) && StoryProbeState.HasAsked(npcId, tr.Once)) continue;

                        var tcopy = new DialogueInjector.DialogueTransition
                        {
                            PlayerKey = tr.PlayerKey,
                            PlayerFallback = tr.PlayerFallback,
                            PlayerLine = tr.PlayerLine,
                            CheckType = tr.CheckType,
                            CheckTag = tr.CheckTag,
                            Action = tr.Action,
                            ActionParam = tr.ActionParam,
                            NextNodeOnSuccess = tr.NextNodeOnSuccess,
                            NextNodeOnFail = tr.NextNodeOnFail,
                            Requires = tr.Requires,
                            Once = tr.Once,
                        };
                        copy.Transitions.Add(tcopy);
                    }
                }

                // 过滤后没选项 = 死胡同（引擎会卡在"继续"）→ 补一个"离开"直连关窗
                if (copy.Transitions.Count == 0)
                {
                    copy.Transitions.Add(new DialogueInjector.DialogueTransition
                    {
                        PlayerKey = "LWN_story_opt_leave",
                        PlayerFallback = "Leave.",
                        NextNodeOnSuccess = "",
                    });
                }

                // 台词配动作：动作写在**本节点**的 NpcActions 上（与行数一一对应）。
                // 第一次展示这行字之前触发一次，按行依次延迟播出（StoryLineActions.Play）。
                if (node.NpcActions != null && node.NpcActions.Length > 0)
                {
                    var actsToPlay = node.NpcActions;
                    string npcIdForActions = npcId;
                    var hostForActions = StoryProbeMissionBehavior.Current;
                    var innerLazy = copy.LazyNpcLine;
                    string plain = LWNTextHelper.ResolveText(node.NpcKey, node.NpcFallback ?? node.NpcLine);
                    bool actsFired = false;
                    copy.LazyNpcLine = () =>
                    {
                        if (!actsFired)
                        {
                            actsFired = true;
                            try
                            {
                                StoryLineActions.Play(hostForActions, hostForActions?.GetAgent(npcIdForActions), actsToPlay);
                            }
                            catch (Exception ex)
                            {
                                DebugLogger.Log($"[StoryLineActions] play on '{node.Id}' failed: {ex.Message}");
                            }
                        }
                        return innerLazy != null ? innerLazy() : plain;
                    };
                }

                result.Nodes.Add(copy);
            }

            // 入口节点必须存在
            if (!result.Nodes.Any(n => n.Id == entry))
            {
                DebugLogger.Log($"[StoryProbe] entry node '{entry}' filtered out for {npcId}; fallback to first node");
                if (result.Nodes.Count > 0) result.EntryNode = result.Nodes[0].Id;
                else return null;
            }

            // EntryOption 在 JSON 里写的是**本地化键**，入口行是玩家可见文本（铁律 13）→ 注入前解析成真文本
            if (string.IsNullOrEmpty(result.EntryOption))
                result.EntryOption = LWNTextHelper.ResolveText("LWN_story_entry_option", "Say a word.");
            else
                result.EntryOption = LWNTextHelper.ResolveText(result.EntryOption, null);

            return result;
        }

        /// <summary>条件表达式：逗号 = AND，`!` 前缀 = 取反。</summary>
        public static bool EvaluateRequires(string requires, string npcId)
        {
            if (string.IsNullOrWhiteSpace(requires)) return true;

            foreach (var raw in requires.Split(','))
            {
                string term = raw.Trim();
                if (term.Length == 0) continue;

                bool negate = term.StartsWith("!");
                if (negate) term = term.Substring(1);

                bool ok = EvaluateTerm(term, npcId);
                if (negate) ok = !ok;
                if (!ok) return false;
            }
            return true;
        }

        private static bool EvaluateTerm(string term, string npcId)
        {
            try
            {
                if (term == "disguised") return StoryProbeMissionBehavior.IsWearingStoryDisguise(false);
                if (term == "noble") return StoryProbeMissionBehavior.IsWearingStoryDisguise(true);
                if (term == "stripped") return StoryProbeState.Data.Stripped;
                // 跨 NPC 引用：quest_accepted@pawnbroker（"别人的委托"——收数人身上要用这个）
                if (term.StartsWith("quest_accepted@"))
                    return StoryProbeState.IsQuestAccepted(term.Substring("quest_accepted@".Length));
                if (term.StartsWith("quest_done@"))
                    return StoryProbeState.IsQuestDone(term.Substring("quest_done@".Length));
                if (term.StartsWith("quest_turned@"))
                    return StoryProbeState.IsQuestTurnedIn(term.Substring("quest_turned@".Length));

                if (term == "quest_accepted") return StoryProbeState.IsQuestAccepted(npcId);
                if (term == "quest_done") return StoryProbeState.IsQuestDone(npcId);
                if (term == "quest_turned") return StoryProbeState.IsQuestTurnedIn(npcId);
                if (term == "gate_open") return StoryProbeMissionBehavior.Current?.GateOpen == true;

                if (term.StartsWith("gold>=") && int.TryParse(term.Substring(6), out int gold))
                    return Hero.MainHero != null && Hero.MainHero.Gold >= gold;

                if (term.StartsWith("item:"))
                {
                    var item = StoryProbeDefs.FindItem(term.Substring(5));
                    return item != null && Hero.MainHero != null
                        && Hero.MainHero.PartyBelongedTo?.ItemRoster != null
                        && Hero.MainHero.PartyBelongedTo.ItemRoster.GetItemNumber(item) > 0;
                }

                // 守门人被扒空了主手（扒窃条的战果）—— 这条不读存档，读场上那个 agent
                if (term == "blocker_disarmed")
                {
                    var b = StoryProbeMissionBehavior.Current?.GetAgent("blocker");
                    return StoryProbeMissionBehavior.IsReallyDisarmed(b);
                }

                if (term.StartsWith("attitude:"))
                {
                    var d = StoryProbeDefs.GetNpc(npcId);
                    if (d == null) return false;
                    string want = term.Substring("attitude:".Length).ToLowerInvariant();
                    return (want == "open" && d.Money == MoneyAttitude.Open)
                        || (want == "covert" && d.Money == MoneyAttitude.Covert)
                        || (want == "disdain" && d.Money == MoneyAttitude.Disdain);
                }

                if (term.StartsWith("clue:")) return StoryProbeState.HasClue(term.Substring(5));
                if (term.StartsWith("hazard:")) return StoryProbeState.HasHazard(term.Substring(6));
                if (term.StartsWith("step>=") && int.TryParse(term.Substring(6), out int step))
                    return StoryProbeState.Step(npcId) >= step;

                DebugLogger.Log($"[StoryProbe] unknown requires term '{term}' -> treated as false");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] requires '{term}' failed: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 检定（DialogueInjector.CustomCheck 钩子）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>CheckTag = "&lt;npcId&gt;|&lt;ability&gt;"。掷一次、播报、返回成功与否。</summary>
        public static bool? RunCheck(DialogueInjector.DialogueTransition transition)
        {
            if (transition == null || string.IsNullOrEmpty(transition.CheckTag)) return null;

            string[] parts = transition.CheckTag.Split('|');
            if (parts.Length < 2) return null;

            string npcId = parts[0].Trim();
            if (!StoryProbeDefs.TryParseAbility(parts[1], out StoryAbility ability)) return null;

            StoryRoll roll = StoryProbeDefs.RollCheck(npcId, ability);
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(roll.PlayerMessage()));
            }
            catch { }
            return roll.Success;
        }

        // ═══════════════════════════════════════════════════════════════
        // 动作（DialogueInjector.CustomAction 钩子）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>动作入口：支持复合动作 `verb1+verb2` / `param1;param2`（按位置配对）。</summary>
        public static void HandleAction(string verb, string param)
        {
            if (_actionFailed || string.IsNullOrEmpty(verb) || StoryProbeState.Data.CancellationPending
                || !StoryProbeSettings.Enabled || StoryProbeMissionBehavior.Current?.Active != true) return;
            string[] verbs = verb.Split('+');
            // 单条动作自己的参数可能含分号（pay_to="thug;80"），不能当成复合动作拆丢金额。
            string[] pars = verbs.Length == 1 ? new[] { param }
                : string.IsNullOrEmpty(param) ? new string[0] : param.Split(';');
            for (int i = 0; i < verbs.Length; i++)
            {
                string p = i < pars.Length ? pars[i] : null;
                if (!Dispatch(verbs[i], p))
                {
                    if (!_actionFailed) FailStoryActionConversation(); // 接单/交差失败也不能继续成功回答节点的自定义奖励。
                    break;
                }
            }
        }

        private static bool FailStoryActionConversation()
        {
            _actionFailed = true;
            var cm = Campaign.Current?.ConversationManager;
            var host = StoryProbeMissionBehavior.Current;
            string npcId = CurrentNpcId;
            string ownerTag = _lastTag;
            int round = _conversationRound;
            Func<bool> stillOwnsConversation = () => cm != null && host != null && host.Active
                && StoryProbeMissionBehavior.Current == host && round == _conversationRound
                && _actionFailed && CurrentNpcId == npcId && !string.IsNullOrEmpty(npcId)
                && _lastTag == ownerTag && ownerTag == "story_probe_" + npcId
                && Campaign.Current?.ConversationManager == cm && cm.IsConversationInProgress
                && ResolveNpcId(cm.OneToOneConversationAgent as Agent) == npcId;
            try
            {
                if (!stillOwnsConversation()) return false;
                host.SchedulePublic(0f, () =>
                {
                    if (stillOwnsConversation()) cm.EndConversation();
                });
                // 复用 Injector 的 SkillCheck 路由写法，只改输出 token，不在 consequence 栈结束对话。
                // 普通付款的成功应答尚未选择时，这样阻止其显示；已显示的台词无法事后撤回。
                cm.ActiveToken = cm.GetStateIndex("close_window");
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] failed payment conversation cleanup deferred: {ex.Message}"); }
            return false;
        }

        private static bool TryPayMoney(Hero recipient, int amount)
        {
            var payer = Hero.MainHero;
            if (payer == null || recipient == null || recipient == payer || amount <= 0)
            {
                DebugLogger.Log("[StoryProbe] payment refused: real payer/recipient/positive amount required");
                Message("LWN_story_turnin_not_done", "That is not done yet.");
                return FailStoryActionConversation();
            }
            if (payer.Gold < amount)
            {
                Message("LWN_story_no_money", "You don't have enough.");
                return FailStoryActionConversation();
            }
            if ((long)recipient.Gold + amount > int.MaxValue)
            {
                Message("LWN_story_turnin_not_done", "That is not done yet.");
                return FailStoryActionConversation();
            }
            int payerBefore = payer.Gold;
            int recipientBefore = recipient.Gold;
            int transferred = 0;
            try { transferred = AgentControlHelper.TransferGold(payer, recipient, amount, notify: false); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] payment settlement exception: {ex.Message}"); }
            int debited = Math.Max(0, payerBefore - payer.Gold);
            int credited = Math.Max(0, recipient.Gold - recipientBefore);
            // 引擎可能结算后通知抛错；双方实际足额才认付款，不能只凭调用过或余额够。
            bool paid = debited == amount && credited == amount;
            if (!paid)
            {
                // 共享转移正常是原子且足额；异常的成对部分款退回，不能留下半笔钱却继续开门。
                int refund = Math.Min(amount, Math.Min(debited, credited));
                if (refund > 0)
                {
                    try { AgentControlHelper.TransferGold(recipient, payer, refund, notify: false); }
                    catch (Exception ex) { DebugLogger.Log($"[StoryProbe] partial payment refund failed: {ex.Message}"); }
                }
                Message("LWN_story_turnin_not_done", "That is not done yet.");
            }
            DebugLogger.Log($"[StoryProbe] payment actual={transferred} debit={debited} credit={credited} required={amount} paid={paid}");
            return paid || FailStoryActionConversation();
        }

        private static bool Dispatch(string verb, string param)
        {
            var host = StoryProbeMissionBehavior.Current;
            string currentNpc = CurrentNpcId;
            DebugLogger.Log($"[StoryProbe] action {verb} param={param ?? "-"} npc={currentNpc ?? "-"}");

            switch ((verb ?? "").ToLowerInvariant())
            {
                case "forced_fail":
                    host?.PlayForcedDefeat();
                    break;

                case "accept_quest":
                    if (!string.IsNullOrEmpty(currentNpc))
                    {
                        // 原生 Issue/Quest 确认创建后才提交剧情接单标志。
                        var nativeQuest = PrologueQuestLauncher.Start(currentNpc);
                        if (nativeQuest == null)
                        {
                            Message("LWN_story_turnin_not_done", "That is not done yet.");
                            return false;
                        }
                        StoryProbeState.AcceptQuest(currentNpc);
                        StoryProbeState.SetGoal("quest");
                        // 播报带上任务名与目标（QuestLogKey 此前没有消费者，接了也不说去哪）
                        var qdef = StoryProbeDefs.GetNpc(currentNpc);
                        string qtitle = qdef != null && !string.IsNullOrEmpty(qdef.QuestLogKey)
                            ? LWNTextHelper.ResolveText(qdef.QuestLogKey, currentNpc)
                            : currentNpc;
                        string qtarget = qdef != null && !string.IsNullOrEmpty(qdef.QuestTargetNpcId)
                            ? StoryProbeMissionBehavior.NpcName(qdef.QuestTargetNpcId)
                            : "";
                        Message("LWN_story_narr_quest_accepted", "You took the job.");
                        try
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                LWNTextHelper.ResolveCompound("LWN_story_narr_quest_taken",
                                    ("TASK", qtitle), ("TARGET", qtarget))));
                        }
                        catch { }
                    }
                    break;

                case "turn_in":
                    TurnIn(currentNpc);
                    if (string.IsNullOrEmpty(currentNpc) || !StoryProbeState.IsQuestTurnedIn(currentNpc)) return false;
                    break;

                case "pay_to":
                {
                    // param = "<npcId>;<amount>"：钱给到事件里的那个人（守恒转移）
                    string npcId = "pawnbroker";
                    int amount = 50;
                    if (!string.IsNullOrEmpty(param))
                    {
                        var parts = param.Split(';');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) npcId = parts[0].Trim();
                        if (parts.Length > 1) int.TryParse(parts[1], out amount);
                    }
                    if (!TryPayMoney(host?.GetHero(npcId), amount)) return false;
                    break;
                }

                case "open_gate_award":
                {
                    // 检定已经掷过（CustomCheck）→ 这里只落地，不再掷第二次。
                    // param = "approach|faction|delta"
                    string approach = "force", faction = "dockhands";
                    int delta = -20;
                    bool crime = false;
                    if (!string.IsNullOrEmpty(param))
                    {
                        var parts = param.Split('|');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) approach = parts[0].Trim();
                        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) faction = parts[1].Trim();
                        if (parts.Length > 2) int.TryParse(parts[2], out delta);
                        if (parts.Length > 3) crime = parts[3].Trim() == "1";
                    }
                    host?.OpenGate(approach, "LWN_story_gate_open", "The way is clear.");
                    host?.ResolveApproach(approach, faction, delta, crime, null);
                    break;
                }

                case "resolve_gate":
                {
                    if (!StoryProbeDefs.TryParseAbility(param, out StoryAbility ab)) ab = StoryAbility.Force;
                    var roll = StoryProbeDefs.RollCheck("blocker", ab);
                    if (roll.Success)
                    {
                        string approach = StoryProbeDefs.AbilityId(ab);
                        host?.OpenGate(approach, "LWN_story_gate_open", "The way is clear.");
                        host?.ResolveApproach(approach,
                            ab == StoryAbility.Charm ? "players" : ab == StoryAbility.Guile ? "nightwalkers" : "dockhands",
                            ab == StoryAbility.Force ? -20 : -5,
                            ab == StoryAbility.Force, null);
                    }
                    else
                    {
                        Message("LWN_story_check_fail", "It did not work.");
                    }
                    break;
                }

                case "open_gate":
                    host?.OpenGate(param ?? "unknown", "LWN_story_gate_open", "The way is clear.");
                    if (!string.IsNullOrEmpty(currentNpc))
                        StoryProbeState.SetStep(currentNpc, 4);
                    break;

                case "bribe":
                {
                    // 🔴 行贿也要过"这个人收不收钱"这道闸（2026-11-13 用户裁定）：
                    //    只有 MoneyAttitude.Open 的人接；Covert 要绕正当买卖，Disdain 直接翻脸。
                    {
                        var bd = StoryProbeDefs.GetNpc(currentNpc);
                        if (bd != null && bd.Money != MoneyAttitude.Open)
                        {
                            DebugLogger.Log($"[StoryProbe] bribe refused: {currentNpc} attitude={bd.Money}");
                            Message("LWN_story_money_refused", "He doesn't take money — not like this.");
                            return FailStoryActionConversation();
                        }
                    }

                    int amount = 100;
                    int.TryParse(param, out amount);
                    if (amount <= 0) amount = 100;
                    if (!TryPayMoney(host?.GetHero("blocker"), amount)) return false;
                    StoryProbeState.Data.BribePaid += amount; // 双方实际足额，才记打点、让路并结算路线。
                    host?.OpenGate("money", "LWN_story_gate_bribe", "He weighs the coins and steps aside.");
                    host?.ResolveApproach("money", "fixers", -5, false, "hazard_money_leverage");
                    break;
                }

                case "fight":
                {
                    string targetId = string.IsNullOrEmpty(param) ? currentNpc : param;
                    StartFight(targetId);
                    break;
                }

                case "met":
                    if (!string.IsNullOrEmpty(currentNpc)) StoryProbeState.MarkMet(currentNpc);
                    break;

                case "mark":
                    if (!string.IsNullOrEmpty(currentNpc) && !string.IsNullOrEmpty(param))
                        StoryProbeState.MarkAsked(currentNpc, param);
                    if (currentNpc == "blocker" && param == "met")
                        StoryProbeMissionBehavior.Current?.NotifyFirstGuardQuestioned();
                    break;

                case "clue":
                    StoryProbeState.AddClue(param);
                    StoryProbeState.SetGoal("asked");
                    break;

                case "pay":
                {
                    int amount = 30;
                    if (!int.TryParse(param, out amount) || amount < 0) amount = 30;
                    if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                    break;
                }

                case "offer_money":
                {
                    // 同一条「递钱」动作，按这个人对钱的态度给三种结果（2026-11-13 用户裁定）。
                    // Open=当面收（钱走人、给实话、你被拿捏）；Covert=推回来（他自己不收，得走正经买卖）；
                    // Disdain=把这当成侮辱（关系掉，钱一动不动）。
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def == null) return false;
                    int amount = def.Money == MoneyAttitude.Covert
                        ? Math.Max(1, def.MoneyCovertPrice) : Math.Max(1, def.MoneyPrice);

                    switch (def.Money)
                    {
                        case MoneyAttitude.Open:
                        {
                            if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                            StoryProbeState.AddHazard("hazard_money_leverage");
                            AdjustNpcRelation(currentNpc, -5);
                            ShowHint(def);
                            DebugLogger.Log($"[StoryProbe] money OPEN {currentNpc} took {amount}");
                            break;
                        }

                        case MoneyAttitude.Covert:
                            // 当面递钱：他手都不伸。钱一分没动 —— 要走下面那条「正经买卖」。
                            DebugLogger.Log($"[StoryProbe] money COVERT {currentNpc} refused direct cash");
                            return false;

                        default: // Disdain
                            AdjustNpcRelation(currentNpc, -10);
                            StoryProbeState.AddHazard("hazard_bribed_the_proud");
                            DebugLogger.Log($"[StoryProbe] money DISDAIN {currentNpc} insulted");
                            return false;
                    }
                    break;
                }

                case "money_deal":
                {
                    // Covert 的"正经买卖"：当/买 —— 钱照收，但面上是生意。
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def == null) return false;
                    int amount = Math.Max(1, def.MoneyCovertPrice);
                    if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                    StoryProbeState.AddHazard("hazard_money_leverage");
                    AdjustNpcRelation(currentNpc, -5);
                    ShowHint(def);
                    DebugLogger.Log($"[StoryProbe] money DEAL {currentNpc} took {amount} (covert)");
                    break;
                }

                case "money_hint":
                {
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def != null) ShowHint(def);
                    break;
                }

                case "favour":
                {
                    // param = "<faction>;<delta>"：事件里帮了谁，就给那家好感（§9 选择反馈）
                    string faction = "players";
                    int delta = 5;
                    if (!string.IsNullOrEmpty(param))
                    {
                        var parts = param.Split(';');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) faction = parts[0].Trim();
                        if (parts.Length > 1) int.TryParse(parts[1], out delta);
                    }
                    StoryProbeState.AddFactionRelation(faction, delta);
                    try
                    {
                        var rep = StoryProbeDefs.GetNpcByFaction(faction);
                        var hero = rep != null ? host?.GetHero(rep.Id) : null;
                        if (hero != null && Hero.MainHero != null)
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, delta);
                    }
                    catch (Exception ex) { DebugLogger.Log($"[StoryProbe] favour failed: {ex.Message}"); }
                    break;
                }

                case "hazard":
                    StoryProbeState.AddHazard(param);
                    break;

                case "collect_debt":
                {
                    int amount = 120;
                    if (int.TryParse(param, out int p) && p > 0) amount = p;
                    if (CollectDebt(amount))
                    {
                        StoryProbeState.CompleteQuest("pawnbroker");
                        PrologueQuestLauncher.FindActive("pawnbroker")?.MarkObjectivesDone();
                    }
                    else
                    {
                        Message("LWN_story_turnin_not_done", "That is not done yet.");
                        return false;
                    }
                    break;
                }

                case "errand":
                {
                    // 零活（§5.5）：跑腿 / 搬货 / 送信 —— 每次 20 第纳尔，2 小时冷却。
                    // 钱从"世界"发（Grant，铁律 4 的虚空来源合法情形；注释即标注）。
                    int pay = 50;
                    if (!string.IsNullOrEmpty(param)) int.TryParse(param, out pay);
                    if (pay <= 0) pay = 50;
                    double now = CampaignTime.Now.ToHours;
                    if (now < StoryProbeState.Data.NextErrandHour)
                    {
                        Message("LWN_story_errand_cooldown", "Nothing needs doing right now. Come back later.");
                        break;
                    }
                    var player = Hero.MainHero;
                    if (player == null || (long)player.Gold + pay > int.MaxValue) break;
                    int before = player.Gold;
                    try { AgentControlHelper.TransferGold((Hero)null, player, pay, notify: false); }
                    finally
                    {
                        int moved = Math.Min(pay, Math.Max(0, player.Gold - before));
                        StoryProbeState.Data.CoinsFound += moved;
                        if (moved > 0) StoryProbeState.Data.NextErrandHour = now + 1.0;
                    }
                    if (player.Gold - before == pay)
                        Message("LWN_story_errand_done", "You ran the errand. A few coins for it.");
                    break;
                }

                case "give_disguise":
                    GiveNobleDisguise();
                    break;

                case "give_items":
                    GiveRewards(param);
                    break;

                case "start_event":
                    if (int.TryParse(param, out int ev)) host?.Director?.ForceEvent(ev);
                    break;

                default:
                    DebugLogger.Log($"[StoryProbe] unknown action verb '{verb}'");
                    return false;
            }
            return true;
        }

        /// <summary>把关系记到当前对话的这个人身上（正负都行）。</summary>
        private static void AdjustNpcRelation(string npcId, int delta)
        {
            try
            {
                var host = StoryProbeMissionBehavior.Current;
                var hero = host?.GetHero(npcId);
                if (hero != null && Hero.MainHero != null && delta != 0)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, delta);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] AdjustNpcRelation({npcId}) failed: {ex.Message}"); }
        }

        /// <summary>收了钱之后多给你的那句实话（每个 NPC 一句，见 StoryNpcDef.MoneyHint*）。</summary>
        private static void ShowHint(StoryNpcDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.MoneyHintKey)) return;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText(def.MoneyHintKey, def.MoneyHintFallback)));
            }
            catch { }
        }

        private static void Message(string key, string fallback)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText(key, fallback)));
            }
            catch { }
        }

        /// <summary>交差：给奖励 + 关系 + 进度（§4.1）。</summary>
        private static bool CollectDebt(int amount)
        {
            var d = StoryProbeState.Data;
            var player = Hero.MainHero;
            var debtor = StoryProbeMissionBehavior.Current?.GetHero("debtor");
            if (player == null || debtor == null || amount <= 0) return false;
            int remaining = Math.Max(0, amount - d.DebtCollected);
            if (remaining == 0) return true;
            if ((long)player.Gold + remaining > int.MaxValue) return false;
            int before = player.Gold;
            try { AgentControlHelper.TransferGold(debtor, player, remaining, notify: false); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] debt collection failed: {ex.Message}"); }
            finally
            {
                int moved = Math.Min(remaining, Math.Max(0, player.Gold - before));
                d.DebtCollected += moved;
                d.CoinsFound += moved;
                DebugLogger.Log($"[StoryProbe] debt actually collected={moved} total={d.DebtCollected}/{amount}");
            }
            return d.DebtCollected >= amount;
        }

        private static bool GrantRewardItem(string npcId, string receipt, string itemId)
        {
            if (StoryProbeState.HasRewardReceipt(npcId, receipt)) return true;
            var player = Hero.MainHero;
            var roster = player?.PartyBelongedTo?.ItemRoster;
            var item = StoryProbeDefs.FindItem(itemId);
            // TransferItems 的 null roster 分支不能当作实际到货；必须验证真实接收背包。
            if (roster == null || item == null) return false;
            int before = roster.GetItemNumber(item);
            try { StoryProbeDefs.GiveItem(player, itemId); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] reward grant failed {npcId}/{itemId}: {ex.Message}"); }
            if (roster.GetItemNumber(item) <= before) return false;
            StoryProbeState.RecordRewardReceipt(npcId, receipt);
            return true;
        }

        private static void TurnIn(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            var def = StoryProbeDefs.GetNpc(npcId);
            if (def == null) return;

            if (!StoryProbeState.IsQuestDone(npcId))
            {
                Message("LWN_story_turnin_not_done", "That is not done yet.");
                return;
            }
            if (StoryProbeState.IsQuestTurnedIn(npcId))
            {
                Message("LWN_story_turnin_already", "We are square.");
                return;
            }

            var player = Hero.MainHero;
            var giver = StoryProbeMissionBehavior.Current?.GetHero(npcId);
            if (player?.PartyBelongedTo?.ItemRoster == null || giver == null) return;
            var nativeQuest = PrologueQuestLauncher.FindActive(npcId);
            if (nativeQuest == null && !StoryProbeState.HasRewardReceipt(npcId, "native-finished"))
                nativeQuest = PrologueQuestLauncher.Start(npcId);
            if (nativeQuest == null && !StoryProbeState.HasRewardReceipt(npcId, "native-finished")) return;
            var items = def.RewardItems ?? new string[0];
            // 所有资源先预检；缺一件时不先吃掉交差资格或收走账款。
            if (items.Any(id => StoryProbeDefs.FindItem(id) == null)
                || (!string.IsNullOrEmpty(def.AttrItemId) && StoryProbeDefs.FindItem(def.AttrItemId) == null))
            {
                Message("LWN_info_equipment_error", "Equipment unavailable.");
                return;
            }
            var d = StoryProbeState.Data;
            try
            {
                if (npcId == "pawnbroker")
                {
                    // 打倒 debtor 的替代路线也必须实际收账，不能把目标倒地当作凭空得到 120。
                    if (!CollectDebt(120)) { Message("LWN_story_turnin_not_done", "That is not done yet."); return; }
                    int owed = Math.Max(0, 120 - d.DebtRepaid);
                    if (player.Gold < owed) { Message("LWN_story_turnin_not_done", "That is not done yet."); return; }
                    int before = player.Gold;
                    try { AgentControlHelper.TransferGold(player, giver, owed, notify: false); }
                    finally { d.DebtRepaid += Math.Min(owed, Math.Max(0, before - player.Gold)); }
                    if (d.DebtRepaid < 120) return;
                    int fee = Math.Max(0, 30 - d.PawnbrokerRewardPaid);
                    before = player.Gold;
                    try { AgentControlHelper.TransferGold(giver, player, fee, notify: false); }
                    finally { d.PawnbrokerRewardPaid += Math.Min(fee, Math.Max(0, player.Gold - before)); }
                    if (d.PawnbrokerRewardPaid < 30) return;
                }
                for (int i = 0; i < items.Length; i++)
                {
                    string key = npcId == "widow" && items[i] == "fine_town_tunic"
                        ? "wardrobe" : "item:" + i + ":" + items[i];
                    if (!GrantRewardItem(npcId, key, items[i])) return;
                }
                if (!string.IsNullOrEmpty(def.AttrItemId)
                    && !GrantRewardItem(npcId, "attribute-item", def.AttrItemId)) return;
                if (npcId == "widow" && !GiveNobleDisguise()) return;
                if (!StoryProbeState.HasRewardReceipt(npcId, "skill-xp"))
                {
                    var skill = StoryProbeDefs.ResolveSkill(def.AttrSkill);
                    if (skill != null) player.AddSkillXp(skill, 40f);
                    StoryProbeState.RecordRewardReceipt(npcId, "skill-xp");
                }
                if (!StoryProbeState.HasRewardReceipt(npcId, "relation"))
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(giver, player, 10);
                    StoryProbeState.RecordRewardReceipt(npcId, "relation");
                }
                if (!StoryProbeState.HasRewardReceipt(npcId, "faction"))
                {
                    StoryProbeState.AddFactionRelation(def.Faction, 10);
                    StoryProbeState.RecordRewardReceipt(npcId, "faction");
                }
                if (!StoryProbeState.HasRewardReceipt(npcId, "native-finished"))
                {
                    try { nativeQuest.MarkObjectivesDone(); nativeQuest.Finish(); }
                    catch
                    {
                        // 完成回调抛错但原生任务已结算时，仍记录事实，不能另开任务重复发奖。
                        if (!nativeQuest.IsFinalized) throw;
                    }
                    StoryProbeState.RecordRewardReceipt(npcId, "native-finished");
                }
                StoryProbeState.TurnInQuest(npcId); // 所有应付项目成功后才提交总完成位。
                StoryProbeState.SetGoal("tool");
                Message("LWN_story_narr_turnin", "He pays up, and he remembers your face.");
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] turn_in deferred with receipts retained {npcId}: {ex.Message}"); }
        }

        /// <summary>贵族制服 + 通行文书：穿上就是"冒充"（不自动换装，玩家自己穿）。</summary>
        private static bool GiveNobleDisguise()
        {
            // 与 RewardItems 共用一张到货回执，不能在剧团交差时再发第二件衣服。
            if (!GrantRewardItem("widow", "wardrobe", "fine_town_tunic")) return false;
            bool first = !StoryProbeState.Data.NobleDisguise;
            StoryProbeState.Data.NobleDisguise = true;
            if (first) Message("LWN_story_got_disguise", "You have the fine clothes and the papers to go with them.");
            return true;
        }

        private static void GiveRewards(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return;
            var ids = csv.Split('|').Select(id => id.Trim()).Where(id => id.Length > 0).ToArray();
            if (Hero.MainHero?.PartyBelongedTo?.ItemRoster == null || ids.Any(id => StoryProbeDefs.FindItem(id) == null)) return;
            for (int i = 0; i < ids.Length; i++)
                if (!GrantRewardItem(CurrentNpcId ?? "scene", "give-items:" + csv + ":" + i, ids[i])) return;
        }

        /// <summary>非致命决斗：玩家 vs 目标（CombatManager 的 Peace 模式）。</summary>
        private static void StartFight(string npcId)
        {
            var host = StoryProbeMissionBehavior.Current;
            var target = host?.GetAgent(npcId);
            if (target == null || Agent.Main == null)
            {
                Message("LWN_story_fight_no_target", "There is no one to fight.");
                return;
            }
            try
            {
                if (host.RequestStoryFight(npcId))
                    DebugLogger.Log($"[StoryProbe] Brain duel queued after conversation vs {npcId}");
                else Message("LWN_story_fight_no_target", "There is no one to fight.");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] StartFight({npcId}) failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 验证 / 调试
        // ═══════════════════════════════════════════════════════════════

        /// <summary>离线自检：返回每个 NPC 的节点数 / 悬空引用（命令 `custom.story_probe dialogue` 用）。</summary>
        public static string ValidateAll()
        {
            var problems = new List<string>();
            int totalNodes = 0, totalOptions = 0;

            foreach (var def in StoryProbeDefs.Npcs)
            {
                var script = Load(def.Id, forceReload: true);
                if (script?.Nodes == null) { problems.Add($"{def.Id}: FILE MISSING"); continue; }

                var ids = new HashSet<string>(script.Nodes.Where(n => n?.Id != null).Select(n => n.Id));
                totalNodes += script.Nodes.Count;

                foreach (var node in script.Nodes)
                {
                    if (node?.Transitions == null) continue;
                    totalOptions += node.Transitions.Count;
                    foreach (var tr in node.Transitions)
                    {
                        if (!string.IsNullOrEmpty(tr.NextNodeOnSuccess) && !ids.Contains(tr.NextNodeOnSuccess))
                            problems.Add($"{def.Id}: {node.Id} -> missing '{tr.NextNodeOnSuccess}'");
                        if (!string.IsNullOrEmpty(tr.NextNodeOnFail) && !ids.Contains(tr.NextNodeOnFail))
                            problems.Add($"{def.Id}: {node.Id} -> missing fail '{tr.NextNodeOnFail}'");
                    }
                }

                if (!ids.Contains(script.EntryNode))
                    problems.Add($"{def.Id}: entry '{script.EntryNode}' missing");

                if (!ids.Any(i => i == def.Id + "_intro") && !def.IsBlocker)
                    problems.Add($"{def.Id}: no _intro node");
                if (!def.IsBlocker && !ids.Any(i => i == def.Id + "_hub"))
                    problems.Add($"{def.Id}: no _hub node");
            }

            string summary = $"npc={StoryProbeDefs.Npcs.Count} nodes={totalNodes} options={totalOptions} problems={problems.Count}";
            return problems.Count == 0 ? summary : summary + " ; " + string.Join(" ; ", problems.Take(20));
        }
    }
}

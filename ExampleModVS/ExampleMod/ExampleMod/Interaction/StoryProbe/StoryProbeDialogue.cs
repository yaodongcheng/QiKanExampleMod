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
                if (term == "disguised") return StoryProbeState.Data.Disguised;
                if (term == "noble") return StoryProbeState.Data.NobleDisguise;
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
            if (string.IsNullOrEmpty(verb)) return;
            string[] verbs = verb.Split('+');
            string[] pars = string.IsNullOrEmpty(param) ? new string[0] : param.Split(';');
            for (int i = 0; i < verbs.Length; i++)
            {
                string p = i < pars.Length ? pars[i] : null;
                Dispatch(verbs[i], p);
            }
        }

        private static void Dispatch(string verb, string param)
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
                    if (amount < 0) amount = 0;
                    if (Hero.MainHero.Gold < amount) { Message("LWN_story_no_money", "You don't have enough."); break; }
                    var to = host?.GetHero(npcId);
                    if (to != null) AgentControlHelper.TransferGold(Hero.MainHero, to, amount, notify: false);
                    else AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold - amount, notify: false);
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
                            break;
                        }
                    }

                    int amount = 100;
                    int.TryParse(param, out amount);
                    if (amount <= 0) amount = 100;
                    var hero = host?.GetHero("blocker");
                    if (Hero.MainHero.Gold >= amount)
                    {
                        if (hero != null) AgentControlHelper.TransferGold(Hero.MainHero, hero, amount, notify: false);
                        else AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold - amount, notify: false);
                        StoryProbeState.Data.BribePaid += amount;
                        host?.OpenGate("money", "LWN_story_gate_bribe", "He weighs the coins and steps aside.");
                        host?.ResolveApproach("money", "fixers", -5, false, "hazard_money_leverage");
                    }
                    else
                    {
                        Message("LWN_story_no_money", "You don't have enough.");
                    }
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
                    break;

                case "clue":
                    StoryProbeState.AddClue(param);
                    StoryProbeState.SetGoal("asked");
                    break;

                case "pay":
                {
                    int amount = 30;
                    if (!int.TryParse(param, out amount) || amount < 0) amount = 30;
                    var to = host?.GetHero(currentNpc);
                    if (Hero.MainHero.Gold < amount) { Message("LWN_story_no_money", "You don't have enough."); break; }
                    if (to != null) AgentControlHelper.TransferGold(Hero.MainHero, to, amount, notify: false);
                    else AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold - amount, notify: false);
                    break;
                }

                case "offer_money":
                {
                    // 同一条「递钱」动作，按这个人对钱的态度给三种结果（2026-11-13 用户裁定）。
                    // Open=当面收（钱走人、给实话、你被拿捏）；Covert=推回来（他自己不收，得走正经买卖）；
                    // Disdain=把这当成侮辱（关系掉，钱一动不动）。
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def == null) break;
                    int amount = def.Money == MoneyAttitude.Covert
                        ? Math.Max(1, def.MoneyCovertPrice) : Math.Max(1, def.MoneyPrice);

                    switch (def.Money)
                    {
                        case MoneyAttitude.Open:
                        {
                            if (Hero.MainHero.Gold < amount)
                            {
                                Message("LWN_story_no_money", "You don't have enough.");
                                break;
                            }
                            var to = host?.GetHero(currentNpc);
                            if (to != null) AgentControlHelper.TransferGold(Hero.MainHero, to, amount, notify: false);
                            else AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold - amount, notify: false);
                            StoryProbeState.AddHazard("hazard_money_leverage");
                            AdjustNpcRelation(currentNpc, -5);
                            ShowHint(def);
                            DebugLogger.Log($"[StoryProbe] money OPEN {currentNpc} took {amount}");
                            break;
                        }

                        case MoneyAttitude.Covert:
                            // 当面递钱：他手都不伸。钱一分没动 —— 要走下面那条「正经买卖」。
                            DebugLogger.Log($"[StoryProbe] money COVERT {currentNpc} refused direct cash");
                            break;

                        default: // Disdain
                            AdjustNpcRelation(currentNpc, -10);
                            StoryProbeState.AddHazard("hazard_bribed_the_proud");
                            DebugLogger.Log($"[StoryProbe] money DISDAIN {currentNpc} insulted");
                            break;
                    }
                    break;
                }

                case "money_deal":
                {
                    // Covert 的"正经买卖"：当/买 —— 钱照收，但面上是生意。
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def == null) break;
                    int amount = Math.Max(1, def.MoneyCovertPrice);
                    if (Hero.MainHero.Gold < amount)
                    {
                        Message("LWN_story_no_money", "You don't have enough.");
                        break;
                    }
                    var to = host?.GetHero(currentNpc);
                    if (to != null) AgentControlHelper.TransferGold(Hero.MainHero, to, amount, notify: false);
                    else AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold - amount, notify: false);
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
                    // 收账：debtor 把 120 第纳尔交出来（走守恒转移），老周的委托完成
                    var debtor = host?.GetHero("debtor");
                    int amount = 120;
                    if (int.TryParse(param, out int p) && p > 0) amount = p;
                    if (debtor != null)
                    {
                        int moved = AgentControlHelper.TransferGold(debtor, Hero.MainHero, amount, notify: false);
                        DebugLogger.Log($"[StoryProbe] collect_debt moved={moved}");
                    }
                    StoryProbeState.CompleteQuest("pawnbroker");
                    StoryProbeState.Data.CoinsFound += amount;
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
                    StoryProbeState.Data.NextErrandHour = now + 1.0;
                    StoryProbeState.Data.CoinsFound += pay;
                    AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold + pay, notify: false);
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
                    break;
            }
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

            StoryProbeState.TurnInQuest(npcId);
            StoryProbeState.AddFactionRelation(def.Faction, 10);
            StoryProbeState.SetGoal("tool");

            // 当铺老板：收上来的 120 是**他的账**，交差时还给他；他按说好的付 30 跑腿费。
            // （不加这段的话，文本说"这是你的，三十"，实际一分钱没动 —— 文案与账目对不上。）
            if (npcId == "pawnbroker")
            {
                var creditor = StoryProbeMissionBehavior.Current?.GetHero("pawnbroker");
                if (creditor != null && Hero.MainHero.Gold > 0)
                {
                    int owed = Math.Min(120, Hero.MainHero.Gold);
                    AgentControlHelper.TransferGold(Hero.MainHero, creditor, owed, notify: false);
                    DebugLogger.Log($"[StoryProbe] turn_in back to pawnbroker: {owed}");
                }
                AgentControlHelper.SetGold(Hero.MainHero, Hero.MainHero.Gold + 30, notify: false);   // 台词：「这是你的。三十。」
            }

            foreach (var itemId in def.RewardItems ?? new string[0])
                StoryProbeDefs.GiveItem(Hero.MainHero, itemId);

            // §5.6 属性道具：交差时一并给你（原版物品当载体）+ 一点对应技能经验。
            // 「偷」那条路仍走现成扒窃系统（扒身上的武器/护符）；这里保证"不偷也能有一点长进"。
            if (!string.IsNullOrEmpty(def.AttrItemId))
            {
                StoryProbeDefs.GiveItem(Hero.MainHero, def.AttrItemId);
                var skill = StoryProbeDefs.ResolveSkill(def.AttrSkill);
                if (skill != null)
                {
                    Hero.MainHero.AddSkillXp(skill, 40f);
                    Message("LWN_story_attr_item", "You learn something from it.");
                }
            }

            // 剧团交差 = 贵族制服 + 通行文书（“冒充”线的底子）
            if (npcId == "widow") GiveNobleDisguise();

            // 关系记到代表身上
            try
            {
                var host = StoryProbeMissionBehavior.Current;
                var hero = host?.GetHero(npcId);
                if (hero != null && Hero.MainHero != null)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, 10);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] turn_in relation failed: {ex.Message}"); }

            Message("LWN_story_narr_turnin", "He pays up, and he remembers your face.");
        }

        /// <summary>贵族制服 + 通行文书：穿上就是"冒充"（不自动换装，玩家自己穿）。</summary>
        private static void GiveNobleDisguise()
        {
            StoryProbeState.Data.NobleDisguise = true;
            StoryProbeDefs.GiveItem(Hero.MainHero, "fine_town_tunic");
            Message("LWN_story_got_disguise", "You have the fine clothes and the papers to go with them.");
        }

        private static void GiveRewards(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return;
            foreach (var id in csv.Split('|'))
                StoryProbeDefs.GiveItem(Hero.MainHero, id.Trim());
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
                CombatManager.StartFight(Agent.Main, target, Peace: true);
                DebugLogger.Log($"[StoryProbe] peace duel started vs {npcId}");
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

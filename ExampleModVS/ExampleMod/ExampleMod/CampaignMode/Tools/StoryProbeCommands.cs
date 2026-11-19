using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using LivingWorldNpcs.Planner.Cooperation;

namespace LivingWorldNpcs
{
    /// <summary>
    /// `custom.story_probe …` 序章验收命令（2026-11-12）。
    /// 🔴 纪律：返回文本**纯英文**；首个参数**可弃**（`custom.story_probe 1 status` 也能用）。
    /// 每个功能都必须能用一条命令自证（CLAUDE.md 工作流约定）。
    /// </summary>
    public static class StoryProbeCommands
    {
        private static readonly HashSet<string> Known =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "enable", "disable", "city", "status", "crime", "quests", "enter", "intro", "phase", "where", "spawn",
                "check", "roll", "learn", "resolve", "strip", "restore", "reset", "talk", "goal",
                "life", "lesson", "combat", "event", "dialogue", "goto", "abort", "money", "stage", "atom", "action", "reload", "hud",
                "script", "population", "layout", "intercept", "work", "group", "persona", "gate", "trace", "scene", "environment", "help", "cooperate",
            };

        [CommandLineFunctionality.CommandLineArgumentFunction("story_probe", "custom")]
        public static string StoryProbe(List<string> args)
        {
            try
            {
                int used;
                string sub = ReadSub(args, out used);
                if (string.IsNullOrEmpty(sub)) return HelpText();

                switch (sub)
                {
                    case "cooperate":
                        return CoopCommands.Handle(args, used);

                    case "enable":
                        Settings.Instance.StoryProbeEnabled = true;
                        return "OK: story_probe enabled (runtime only; set StoryProbeEnabled in config.json to persist). " +
                               "If you are already inside the town menu, leave and re-enter it to see the new option.";

                    case "disable":
                    {
                        Settings.Instance.StoryProbeEnabled = false;
                        bool settled = StoryProbeBehavior.CancelPrologue("disabled by command");
                        return settled ? "OK: story_probe disabled; runtime cancelled and escrow settled."
                            : "Pending: story_probe disabled; outstanding escrow/skills retained for retry.";
                    }

                    case "city":
                    {
                        string id = Arg(args, used, 0);
                        if (string.IsNullOrWhiteSpace(id)) return $"current city = {StoryProbeSettings.SettlementId}";
                        bool settled = StoryProbeBehavior.CancelPrologue("target city changed by command", restart: true);
                        Settings.Instance.StoryProbeSettlementId = id.Trim();
                        return $"{(settled ? "OK" : "Pending escrow restoration")}: target city = {StoryProbeSettings.SettlementId}";
                    }

                    case "status":
                        return "OK: " + StoryProbeState.Describe()
                             + " || steps: " + StoryProbeState.DescribeSteps()
                             + " || clues=" + StoryProbeState.ClueCount + " asked=" + StoryProbeState.AskedCount
                             + " || sentence: " + PlayerDetentionBehavior.DescribeSentence(StoryProbeState.Data.DetentionOwnerId)
                             + " || mission=" + (StoryProbeMissionBehavior.Current?.Active == true
                                 ? "active(" + StoryProbeMissionBehavior.Current.Phase + ")"
                                 : "none");

                    case "quests":
                        return PrologueQuestLauncher.Describe();

                    case "environment":
                        return EnvironmentCommands.Execute(args.Skip(used).ToList());

                    case "crime":
                    {
                        string townId = StoryProbeState.Data.StorySettlementId;
                        var town = Settlement.Find(string.IsNullOrEmpty(townId) ? StoryProbeSettings.SettlementId : townId);
                        var faction = town?.MapFaction;
                        return faction == null ? "NO: story town faction unavailable."
                            : FormattableString.Invariant($"OK: faction={faction.StringId} crimeRating={faction.MainHeroCrimeRating:F2}; sentence: {PlayerDetentionBehavior.DescribeSentence(StoryProbeState.Data.DetentionOwnerId)}");
                    }

                    case "enter":
                        StoryProbeBehavior.EnterTownCenter();
                        return "OK: entering town center (must be inside the target town's menu).";

                    case "intro":
                    {
                        var host = StoryProbeMissionBehavior.Current;
                        if (host != null && host.Active) { host.ReplayIntro(); return "OK: companion intro replayed."; }
                        StoryProbeBehavior.ShowIntroInquiry();
                        return "OK: intro inquiry shown.";
                    }

                    case "phase":
                    {
                        string p = (Arg(args, used, 0) ?? "").ToLowerInvariant();
                        if (p == "first")
                        {
                            return StoryProbeBehavior.CancelPrologue("phase first by command", restart: true)
                                ? "OK: cancelled safely; phase = FirstAttempt."
                                : "Pending: original belongings/skills still outstanding; phase not reset.";
                        }
                        if (p == "second")
                        {
                            return "Second entry is automatic after the fixed map sentence; this command does not skip detention. "
                                + PlayerDetentionBehavior.DescribeSentence(StoryProbeState.Data.DetentionOwnerId);
                        }
                        return $"Usage: custom.story_probe phase first|second (current={StoryProbeState.Phase})";
                    }

                    case "where":
                    {
                        var host = StoryProbeMissionBehavior.Current;
                        return host != null && host.Active ? "OK: " + host.DescribeWhere()
                                                           : "OK: story probe not active in this mission";
                    }

                    case "persona":
                    {
                        string id = Arg(args, used, 0);
                        var selected = StoryProbeDefs.Npcs.Where(n => string.IsNullOrEmpty(id)
                            || StoryProbeDefs.GetNpc(id) == null || n.Id == id);
                        var host = StoryProbeMissionBehavior.Current;
                        var report = new System.Text.StringBuilder("OK: cast personas (read only)");
                        foreach (var def in selected)
                        {
                            var hero = host?.GetHero(def.Id);
                            var agent = host?.GetAgent(def.Id);
                            var memory = hero != null ? AllNpcMemoryManager.GetMemory(hero.StringId) : null;
                            bool identityMatches = memory?._profile?.IdentityOverride
                                == LWNTextHelper.ResolveText(def.IdentityKey, def.IdentityFallback);
                            bool nameMatches = hero != null && hero.Name.ToString()
                                == LWNTextHelper.ResolveText(def.NameKey, def.NameFallback);
                            report.Append($"\n{def.Id}: name={def.NameFallback}; identity={def.IdentityFallback}; role={def.ReactiveRole}; "
                                + $"agent={(agent != null ? agent.Index.ToString() : "none")}; hero={hero?.StringId ?? "none"}; "
                                + $"nameMatches={nameMatches}; identityMatches={identityMatches}; history={memory?.RecentHistory?.Count ?? 0}");
                        }
                        return report.ToString();
                    }

                    case "spawn":
                    {
                        var host = StoryProbeMissionBehavior.Current;
                        if (host == null || !host.Active) return "Error: not in a story-probe mission.";
                        host.CommandSpawnCast();
                        return "OK: cast spawned. " + host.DescribeCast();
                    }

                    case "check":
                    {
                        string ab = Arg(args, used, 0);
                        if (!StoryProbeDefs.TryParseAbility(ab, out StoryAbility ability))
                            return "Usage: custom.story_probe check charm|guile|force";
                        var sb = new System.Text.StringBuilder();
                        sb.Append($"OK: player {StoryProbeDefs.AbilityId(ability)} = {StoryProbeDefs.PlayerAttr(ability):F0} |");
                        foreach (var def in StoryProbeDefs.Npcs)
                        {
                            float na = StoryProbeDefs.NpcAttr(def.Id, ability);
                            float chance = AgentStatsHelper.ComputeOpposedSuccessRate(StoryProbeDefs.PlayerAttr(ability), na);
                            sb.Append($" {def.Id}={na:F0}(chance {chance:P0}, threshold {(1f - chance):P0})");
                        }
                        return sb.ToString();
                    }

                    case "roll":
                    {
                        string ab = Arg(args, used, 0);
                        string kind;
                        if (StoryProbeDefs.TryParseAbility(ab, out StoryAbility ability)) kind = StoryProbeDefs.AbilityId(ability);
                        else if (StoryProbeDefs.TryParseDialogueTactic(ab, out var tactic)) kind = tactic.ToString();
                        else return "Usage: custom.story_probe roll <charm|guile|force|NegotiationTactic> <0-100>";
                        string val = Arg(args, used, 1);
                        if (!float.TryParse(val, out float pct) || float.IsNaN(pct) || float.IsInfinity(pct))
                            return "Usage: custom.story_probe roll <ability> <0-100> (forced next roll)";
                        StoryProbeState.Data.RollOverride = Math.Max(0f, Math.Min(100f, pct));
                        StoryProbeState.Data.RollOverrideAbility = kind;
                        return $"OK: next {kind} roll forced to {pct:F0}% (one use).";
                    }

                    case "learn":
                    {
                        string ab = Arg(args, used, 0);
                        if (!StoryProbeDefs.TryParseAbility(ab, out StoryAbility ability))
                            return "Usage: custom.story_probe learn charm|guile|force";
                        switch (ability)
                        {
                            case StoryAbility.Charm:
                                StoryProbeState.Data.NobleDisguise = true;
                                StoryProbeDefs.GiveItem(Hero.MainHero, "fine_town_tunic");
                                return "OK: learned charm route (noble clothes + papers granted).";
                            case StoryAbility.Guile:
                                StoryProbeDefs.GiveItem(Hero.MainHero, "lwn_grapple_rope");
                                StoryProbeDefs.GiveItem(Hero.MainHero, "lwn_grapple_hook");
                                return "OK: learned guile route (grapple rope + hook granted).";
                            default:
                                StoryProbeDefs.GiveItem(Hero.MainHero, "cleaver_2hsword_t3");
                                StoryProbeDefs.GiveItem(Hero.MainHero, "lwn_grapple_rope");
                                return "OK: learned force route (cleaver + rope granted).";
                        }
                    }

                    case "resolve":
                    {
                        string ab = Arg(args, used, 0);
                        if (!StoryProbeDefs.TryParseAbility(ab, out StoryAbility ability))
                            return "Usage: custom.story_probe resolve charm|guile|force";
                        var roll = StoryProbeDefs.RollCheck("blocker", ability);
                        var host = StoryProbeMissionBehavior.Current;
                        if (roll.Success)
                        {
                            host?.OpenGate(StoryProbeDefs.AbilityId(ability), "LWN_story_gate_open", "The way is clear.");
                            host?.ResolveApproach(StoryProbeDefs.AbilityId(ability),
                                ability == StoryAbility.Charm ? "players" : ability == StoryAbility.Guile ? "nightwalkers" : "fixers",
                                ability == StoryAbility.Force ? -20 : -5,
                                ability == StoryAbility.Force, null);
                        }
                        return (roll.Success ? "OK: " : "FAIL: ") + roll.Describe();
                    }

                    case "strip":
                    {
                        return "Disguise is equipped automatically after the fixed map sentence; this command does not skip detention. "
                            + PlayerDetentionBehavior.DescribeSentence(StoryProbeState.Data.DetentionOwnerId);
                    }

                    case "restore":
                        return (StoryProbeBehavior.CancelPrologue("restore by command")
                            ? "OK: cancelled and belongings restored. " : "Pending: outstanding escrow/skills retained. ") + StoryProbeState.Describe();

                    case "money":
                    {
                        // 每个人对钱的态度（验收用：一眼看出"给钱"谁能收、谁不能）
                        var sb = new System.Text.StringBuilder("OK: money attitudes |");
                        foreach (var d in StoryProbeDefs.Npcs)
                        {
                            string att;
                            switch (d.Money)
                            {
                                case MoneyAttitude.Open: att = "OPEN(" + d.MoneyPrice + ")"; break;
                                case MoneyAttitude.Covert: att = "COVERT(" + d.MoneyCovertPrice + ")"; break;
                                default: att = "DISDAIN"; break;
                            }
                            sb.Append($" {d.Id}={att}");
                        }
                        return sb.ToString();
                    }

                    case "gate":
                    {
                        // 通行权（实物凭据）验收：令牌在不在背包里 = 有没有被正式放行过。
                        string a1 = (Arg(args, used, 0) ?? "").Trim().ToLowerInvariant();
                        string passItem = StoryProbeTuning.Current?.Gate?.PassItem;
                        if (string.IsNullOrEmpty(passItem))
                            return "Error: Gate.PassItem is empty in StoryProbeTuning.json (pass token disabled).";

                        var item = StoryProbeDefs.FindItem(passItem);
                        var roster = PartyBase.MainParty?.ItemRoster;
                        int held = item != null && roster != null ? roster.GetItemNumber(item) : 0;

                        if (a1 == "grant")
                        {
                            if (held > 0) return $"OK: pass token already held ({passItem} x{held}).";
                            if (Hero.MainHero == null) return "Error: no main hero.";
                            return StoryProbeDefs.GiveItem(Hero.MainHero, passItem, 1)
                                ? $"OK: pass token granted ({passItem}); the next entry walks straight in."
                                : $"Error: grant failed (item '{passItem}' not registered, or roster unavailable).";
                        }
                        if (a1 == "revoke")
                        {
                            if (held <= 0) return $"OK: no pass token to revoke ({passItem}).";
                            return StoryProbeMissionBehavior.RevokeGatePass("command")
                                ? $"OK: pass token revoked ({passItem}, removed x{held})."
                                : "Error: revoke failed (see [StoryGate] lines in the runtime log).";
                        }

                        var host = StoryProbeMissionBehavior.Current;
                        var sb = new System.Text.StringBuilder("OK: gate pass");
                        sb.Append($"\n  item       : {passItem}");
                        sb.Append($"\n  held       : x{held} {(held > 0 ? "(authorized: guards wave you through)" : "(no authorization)")}");
                        var approaches = StoryProbeTuning.Current?.Gate?.PassApproaches;
                        sb.Append($"\n  approaches : {(approaches == null || approaches.Length == 0 ? "(none)" : string.Join(",", approaches))}");
                        sb.Append($"\n  phase      : {StoryProbeState.Phase}");
                        sb.Append($"\n  gateOpen   : {(host != null && host.Active ? host.GateOpen.ToString() : "n/a (no story mission)")}");
                        sb.Append($"\n  chosenPath : {StoryProbeState.Data.ChosenApproach ?? ""}");
                        sb.Append($"\n  legacyFlag : GateOpened={StoryProbeState.Data.GateOpened} (history only; not used as authority)");
                        // 岗位职责区逐人自检：谁在区里、看不看得见（看得见/看不见的确切原因）、离多远。
                        if (host != null && host.Active && Agent.Main != null)
                        {
                            float zoneRange = host.PostZoneRangeMeters;
                            sb.Append($"\n  postRange  : {zoneRange:F1}m");
                            foreach (var def in StoryProbeDefs.Npcs.Where(d => d != null && d.FirstMeetGuard))
                            {
                                var a = host.GetAgent(def.Id);
                                if (a == null || !AgentControlHelper.SafeIsActive(a))
                                { sb.Append($"\n  {def.Id}: no active agent"); continue; }
                                float dist = a.Position.Distance(Agent.Main.Position);
                                float dz = Math.Abs(a.Position.z - Agent.Main.Position.z);
                                bool inZone = host.IsPlayerInPostZone(a, zoneRange);
                                sb.Append($"\n  {def.Id}: dist={dist:F1} dz={dz:F2} inZone={inZone} sight={NpcSightSystem.ExplainSight(a, Agent.Main, zoneRange)}");
                            }
                        }
                        sb.Append("\n  usage      : custom.story_probe gate [grant|revoke]");
                        return sb.ToString();
                    }

                    case "abort":
                        return (StoryProbeBehavior.CancelPrologue("abort by command", restart: true)
                            ? "OK: cancelled safely; escrow settled. " : "Pending: cancellation retained outstanding escrow/skills. ") + StoryProbeState.Describe();

                    case "reset":
                        if (!StoryProbeBehavior.CancelPrologue("reset by command", restart: true))
                            return "Error: reset refused; original belongings or guard skill backup still outstanding.";
                        return "OK: story probe state reset after safe cancellation.";

                    case "talk":
                    {
                        string npc = NormalizeNpcArg(Arg(args, used, 0));
                        if (string.IsNullOrEmpty(npc)) return "Usage: custom.story_probe talk pawnbroker|widow|son|butcher|blocker|thug|debtor|rival|outlaw";
                        var host = StoryProbeMissionBehavior.Current;
                        var agent = host?.GetAgent(npc);
                        if (agent == null) return $"Error: '{npc}' is not in the scene (spawn first).";
                        host.StartStoryConversation(agent);
                        return $"OK: conversation started with {npc}.";
                    }

                    case "goal":
                    {
                        string v = Arg(args, used, 0);
                        string[] stages = { "enter", "asked", "quest", "tool", "done" };
                        if (int.TryParse(v, out int idx) && idx >= 0 && idx < stages.Length)
                        {
                            StoryProbeState.Data.GoalStage = stages[idx];
                            return $"OK: goal = {stages[idx]} ({LWNScreenGoalText.Resolve()})";
                        }
                        return $"Usage: custom.story_probe goal 0..4 (current={StoryProbeState.Goal})";
                    }

                    case "lesson":
                    {
                        var host = StoryProbeMissionBehavior.Current;
                        string action = Arg(args, used, 0);
                        if (string.IsNullOrEmpty(action) || action == "status") return host?.ExistingPractice?.Describe() ?? "OK: practice inactive";
                        if (action == "reload") return host?.Practice?.Reload() ?? "Error: lesson data unavailable.";
                        if (action == "cancel") { host?.ExistingPractice?.Stop("console cancelled"); return "OK: practice cancelled; permission revoked."; }
                        return "Usage: custom.story_probe lesson status|cancel|reload";
                    }

                    case "combat":
                        return "OK: " + (StoryProbeMissionBehavior.Current?.DescribeStoryFights() ?? CombatEncounter.Describe());
                    case "life":
                        return HandleLife(Arg(args, used, 0), Arg(args, used, 1), Arg(args, used, 2));

                    case "trace":
                    {
                        if (args.Any(arg => arg != null && arg.Any(ch => ch > 127)))
                            return "Error: ASCII arguments only. Trace selection unchanged.";
                        // Presentation trace (three layers: turn / move / anim). ASCII only, npc id only.
                        //   custom.story_probe trace pawnbroker   -> on for that npc
                        //   custom.story_probe trace list         -> who is traced
                        //   custom.story_probe trace off          -> all off
                        string a = (Arg(args, used, 0) ?? "").Trim();
                        string traceMode = (Arg(args, used, 1) ?? "").Trim();
                        if (a.Equals("screen", StringComparison.OrdinalIgnoreCase))
                        {
                            if (traceMode.Equals("off", StringComparison.OrdinalIgnoreCase))
                            {
                                return AgentFacingTrace.HandleCommand(new[] { "off" });
                            }
                            return AgentFacingTrace.HandleCommand(new[] { "status" })
                                + ". General entry: custom.look trace help";
                        }
                        if (string.IsNullOrEmpty(a))
                        {
                            var ids = new List<string>();
                            foreach (var n in StoryProbeDefs.Npcs) ids.Add(n.Id);
                            return "Usage: custom.story_probe trace <npcId> [screen] | trace screen off | trace list | trace off. ids: "
                                   + string.Join(",", ids);
                        }
                        if (a.Equals("off", StringComparison.OrdinalIgnoreCase))
                        {
                            AgentFacingTrace.Stop(); // 保留旧入口的同时关闭语义；通用监听不依赖序章。
                            return StoryPoseTrace.Clear();
                        }
                        if (a.Equals("list", StringComparison.OrdinalIgnoreCase))
                            return "OK: pose trace active = " + StoryPoseTrace.Describe() + "; screen = " + AgentFacingTrace.Describe();

                        // Tolerant first arg: a bare number is read as the agent index shown in the log (Idx=N).
                        if (int.TryParse(a, out int agentIndex))
                        {
                            foreach (var kv in StoryProbeMissionBehavior.Current?.AllAgents
                                     ?? new List<KeyValuePair<string, Agent>>())
                            {
                                if (kv.Value != null && kv.Value.Index == agentIndex)
                                    return traceMode.Equals("screen", StringComparison.OrdinalIgnoreCase)
                                        ? AgentFacingTrace.Enable(kv.Value) : StoryPoseTrace.Enable(kv.Key);
                            }
                            return "Error: no story npc with agent index " + agentIndex
                                   + " (try custom.story_probe trace list).";
                        }
                        string traceId = NormalizeNpcArg(a);
                        return traceMode.Equals("screen", StringComparison.OrdinalIgnoreCase)
                            ? AgentFacingTrace.Enable(StoryProbeMissionBehavior.Current?.GetAgent(traceId))
                            : StoryPoseTrace.Enable(traceId);
                    }

                    case "stage":
                        return HandleStage(Arg(args, used, 0), Arg(args, used, 1), Arg(args, used, 2), Arg(args, used, 3));

                    case "hud":
                    {
                        // 常驻任务卡（右上/左上）：现场切位置、强制显隐，不用改 JSON 重进场景
                        string a = (Arg(args, used, 0) ?? "").Trim().ToLowerInvariant();
                        switch (a)
                        {
                            case "on":
                                StoryGoalHudMissionView.ForceVisible = true;
                                return "OK: goal HUD forced ON (next tick).";
                            case "off":
                                StoryGoalHudMissionView.ForceVisible = false;
                                return "OK: goal HUD forced OFF (next tick).";
                            case "auto":
                                StoryGoalHudMissionView.ForceVisible = null;
                                return "OK: goal HUD back to auto (active prologue or completed in this mission).";
                            case "left":
                                StoryGoalHudMissionView.ForcePosition = "top-left";
                                return "OK: goal HUD position -> top-left.";
                            case "right":
                                StoryGoalHudMissionView.ForcePosition = "top-right";
                                return "OK: goal HUD position -> top-right.";
                            case "flash":
                                StoryGoalHudMissionView.RequestFlash = true;
                                return "OK: goal HUD tutorial highlight requested; no story state changed.";
                            default:
                                return $"OK: hud visible={StoryGoalHudMissionView.LastVisible} " +
                                       $"position={StoryGoalHudMissionView.LastPosition} " +
                                       $"highlight={StoryGoalHudMissionView.LastHighlight} " +
                                       $"fightPending={StoryProbeMissionBehavior.Current?.FirstGuardFightPending == true} " +
                                       $"defeated={StoryProbeMissionBehavior.Current?.FirstGuardDefeated == true} " +
                                       $"completedHere={StoryProbeMissionBehavior.Current?.GoalCompleted == true} " +
                                       $"crossed={StoryProbeMissionBehavior.Current?.PlayerHasCrossed == true} " +
                                       $"(usage: custom.story_probe hud on|off|auto|left|right|flash|status)";
                        }
                    }

                    case "reload":
                        // 策划改完 JSON 后：重读数值表 / 节奏常量 / 演出台本 / 对话树，不用重启游戏
                        StoryProbeDefs.ReloadCast();
                        StoryProbeTuning.Reload();
                        StoryStageDefs.ClearCache();
                        // P0-D.5：新格式对话定义（*.dialog.json）整批重载；坏文件保留旧版
                        string dialogueReload = StoryDialogueLoader.ReloadAll();
                        string journalReload = PrologueJournalQuest.Reload();
                        string cooperationReload = Planner.Cooperation.CoopMissionBehavior.Current?.ReloadContent()
                            ?? "cooperation content will load on next mission";
                        StoryV2Data.Reload();
                        return $"OK: reloaded StoryProbeCast/Tuning/Stage/Dialogues -- npcs={StoryProbeDefs.Npcs.Count} " +
                               $"events={StoryProbeDefs.Events.Count}; dialogue defs: {dialogueReload}; {journalReload}; layout rules: {StoryV2Data.Report}; " +
                               "active dialogue sessions retain their definitions; deferred layouts require reload on the map before the next entry.; " + cooperationReload;

                    case "action":
                    {
                        // 验证某个动作对某个剧情 NPC 到底能不能播（用户要求：必须真能播）
                        string npc = Arg(args, used, 0);
                        string act = Arg(args, used, 1);
                        if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(act))
                            return "Usage: custom.story_probe action <npcId> <actName>";
                        var h = StoryProbeMissionBehavior.Current;
                        var ag = h?.GetAgent(npc);
                        if (ag == null) return $"Error: npc '{npc}' is not in the scene.";
                        return StoryLineActions.Diagnose(ag, act);
                    }

                    case "atom":
                        return HandleAtom(args, used);

                    case "script":
                        return HandleScript(Arg(args, used, 0), Arg(args, used, 1));

                    case "population":
                        return HandlePopulation(Arg(args, used, 0));

                    case "layout":
                        return HandleLayout(Arg(args, used, 0));

                    case "scene":
                        // 场景实体清单：锚点（gate_entity / guard_spawn）找不到时，直接在游戏里看场景里有什么。
                        return HandleScene(Arg(args, used, 0), Arg(args, used, 1));

                    case "intercept":
                        return HandleIntercept(Arg(args, used, 0));

                    case "work":
                        return HandleWork(Arg(args, used, 0), Arg(args, used, 1));

                    case "group":
                        return HandleGroup(Arg(args, used, 0));

                    case "event":
                        return HandleEvent(Arg(args, used, 0), Arg(args, used, 1));

                    case "goto":
                    {
                        // 验收便利：把主队挪到目标城门口（省得在地图上找 Epicrotea）
                        var st = Settlement.Find(StoryProbeSettings.SettlementId);
                        if (st == null) return $"Error: settlement '{StoryProbeSettings.SettlementId}' not found.";
                        if (MobileParty.MainParty == null) return "Error: no main party.";
                        if (Mission.Current != null) return "Error: close the current scene first (goto works on the map only).";
                        if (V.TryMovePartyToSettlement(MobileParty.MainParty, st))
                            return $"OK: main party moved to {st.Name} ({st.StringId}). Enter the town from the map menu.";
                        return "Error: this game version does not allow moving the party from a command.";
                    }

                    case "dialogue":
                        return HandleDialogue(Arg(args, used, 0), Arg(args, used, 1), Arg(args, used, 2), Arg(args, used, 3));

                    default:
                        return HelpText();
                }
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 子命令
        // ═══════════════════════════════════════════════════════════════

        private static string HandleLife(string action, string a1, string a2)
        {
            var director = StoryProbeMissionBehavior.Current?.Director;
            switch ((action ?? "").ToLowerInvariant())
            {
                case "debug":
                    if (a1 == "on" || a1 == "off")
                        Settings.Instance.ShowPerformanceFacingDebugMessages = a1 == "on";
                    return "OK: performance facing screen debug=" + Settings.Instance.ShowPerformanceFacingDebugMessages
                        + " (runtime only; use custom.story_probe life debug on|off).";
                case "status":
                    return director == null ? "OK: no street-life director (not in second phase)"
                                            : "OK: " + director.Describe();

                case "list":
                    return director == null ? "OK: no director" : "OK: " + director.ListDefs();

                case "force":
                {
                    if (director == null) return "Error: no director.";
                    string npc = NormalizeNpcArg(a1);
                    if (string.IsNullOrEmpty(npc)) return "Usage: custom.story_probe life force <npc> <actionIndex>";
                    int idx = 0;
                    int.TryParse(a2, out idx);
                    director.ForceAmbient(npc, Math.Max(0, idx));
                    return $"OK: ambient forced {npc}[{idx}]";
                }

                case "say":
                    return director == null ? "Error: no director." : director.ForceSpeech(a1, a2);

                case "bark":
                    return director == null ? "Error: no director." : director.CueSpeech(a1);

                case "release":
                    return director == null ? "Error: no director." : director.ReleaseSpeech(a1);

                case "facing":
                {
                    var agent = StoryProbeMissionBehavior.Current?.GetAgent(a1 ?? "");
                    if (agent == null) return "Error: speaker unavailable.";
                    var brain = AgentAIController.GetBrainForAgent(agent);
                    var performance = brain?.CurrentAction as PerformanceAction;
                    var listener = performance?.SpeechListener;
                    string info = "OK: speaker=" + a1 + " action=" + (brain?.CurrentAction?.GetType().Name ?? "none")
                        + " control=" + (performance?.CanControl == true) + " listener="
                        + (listener == null ? "none" : listener == Agent.Main ? "player" : StoryProbeDialogue.ResolveNpcId(listener) ?? "other");
                    if (AgentControlHelper.SafeIsActive(listener))
                    {
                        Vec2 direction = (listener.Position - agent.Position).AsVec2;
                        Vec2 facing = agent.Frame.rotation.f.AsVec2;
                        if (direction.LengthSquared > 0.0001f && facing.LengthSquared > 0.0001f)
                        {
                            direction.Normalize(); facing.Normalize();
                            double dot = Math.Max(-1d, Math.Min(1d, direction.x * facing.x + direction.y * facing.y));
                            info += " bodyErrorDeg=" + (Math.Acos(dot) * 180d / Math.PI).ToString("F1");
                        }
                        info += " inBodyViewCone=" + NpcSightSystem.IsTargetInViewCone(agent, listener, useBodyFacing: true)
                            + " bodyTurnRequested=" + performance.BodyTurnRequested + " fov=" + NpcSightSystem.DefaultFovDegrees
                            + " gazeRequested=" + performance.CanControl + " owner=" + performance.OwnerToken;
                    }
                    return info + " perception=(" + NpcPerception.Describe(agent) + ")";
                }

                case "scene":
                {
                    if (director == null) return "Error: no director.";
                    if (!int.TryParse(a1, out int idx)) return "Usage: custom.story_probe life scene <1..6>";
                    StoryProbeDialogue.HandleAction("start_event", idx.ToString());
                    return $"OK: event {idx} forced.";
                }

                case "stop":
                    director?.StopAll();
                    return "OK: street life stopped.";

                case "resume":
                    director?.ResumeAll();
                    return "OK: street life resumed.";

                default:
                    return "Usage: custom.story_probe life status|list|debug on|off|force <npc> <i>|bark <npc>|say <speaker> <listener>|facing <npc>|release <npc>|scene <n>|stop|resume";
            }
        }

        private static string HandleEvent(string action, string a1)
        {
            var director = StoryProbeMissionBehavior.Current?.Director;
            switch ((action ?? "").ToLowerInvariant())
            {
                case "list":
                    return "OK: " + string.Join(" ; ", StoryProbeDefs.Events.Select(e =>
                        $"#{e.Index} {e.ActorA}{(e.TwoPerson ? "+" + e.ActorB : "")} host={e.HostNpcId} node={e.JoinNode} placement={e.Placement}"));

                case "force":
                {
                    if (!int.TryParse(a1, out int idx)) return "Usage: custom.story_probe event force <1..6>";
                    if (director == null) return "Error: no director (second phase only).";
                    director.ForceEvent(idx);
                    return $"OK: event {idx} forced.";
                }

                default:
                    return "Usage: custom.story_probe event list|force <1..6>";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 参数
        // ═══════════════════════════════════════════════════════════════

        /// <summary>子命令读取：首个 token 不认识 = 占位符，顺延到第二个（首参可弃纪律）。</summary>
        private static string ReadSub(List<string> args, out int used)
        {
            used = 0;
            if (args == null || args.Count == 0) return null;
            string a0 = (args[0] ?? "").Trim().ToLowerInvariant();
            if (Known.Contains(a0)) { used = 1; return a0; }
            if (args.Count >= 2 && Known.Contains((args[1] ?? "").Trim().ToLowerInvariant()))
            {
                used = 2;
                return args[1].Trim().ToLowerInvariant();
            }
            used = 1;
            return a0;
        }

        private static string Arg(List<string> args, int used, int index)
        {
            int i = used + index;
            if (args == null || i >= args.Count) return null;
            return args[i];
        }

        /// <summary>NPC 名字归一：接受 id / 中文称呼 / 序号。</summary>
        private static string NormalizeNpcArg(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string t = raw.Trim();
            foreach (var def in StoryProbeDefs.Npcs)
                if (string.Equals(def.Id, t, StringComparison.OrdinalIgnoreCase)) return def.Id;

            switch (t)
            {
                case "老板":
                case "当铺老板":
                    return "pawnbroker";
                case "寡妇":
                    return "widow";
                case "儿子":
                    return "son";
                case "屠户":
                    return "butcher";
                case "守门人":
                    return "blocker";
                case "打手":
                    return "thug";
                case "收数":
                    return "debtor";
                case "对台":
                    return "rival";
                case "野贼":
                    return "outlaw";
            }

            if (int.TryParse(t, out int idx) && idx >= 0 && idx < StoryProbeDefs.Npcs.Count)
                return StoryProbeDefs.Npcs[idx].Id;

            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // 演出台本（布景 / 摆人 / 镜头 / 冻解玩家）—— 2026-11-13
        // ═══════════════════════════════════════════════════════════════

        private static string HandleStage(string a0, string a1, string a2, string a3)
        {
            var host = StoryProbeMissionBehavior.Current;
            if (host == null) return "Error: not in a story probe mission (enter the town center first).";

            var runner = host.Stage;

            switch ((a0 ?? "").ToLowerInvariant())
            {
                case "":
                case "status":
                    if (runner == null) return "OK: stage not started yet. Run 'custom.story_probe stage begin' to start it here.";
                    return $"OK: stage active={runner.Active} beats={runner.BeatCount} next={runner.NextIndex} t={runner.Time:F2}s " +
                           $"anchors=[{string.Join(",", runner.AnchorNames.ToArray())}] " +
                           $"openingCameraReady={runner.OpeningCameraReady} cameraHolder={CameraService.Holder ?? "none"} " +
                           $"cameraCase={CameraService.CurrentCase} returning={CameraService.IsReturning} " +
                           $"blend={CameraService.Machine.BlendProgress:F2}";

                case "list":
                {
                    var script = StoryStageDefs.Load("StoryOpeningTimeline", forceReload: true);
                    if (script?.Beats == null) return "Error: StoryOpeningTimeline.json not found or empty (see log).";
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < script.Beats.Count; i++)
                    {
                        var b = script.Beats[i];
                        sb.Append(i).Append('@').Append(b.At.ToString("0.##")).Append("s ").Append(b.Cmd);
                        if (!string.IsNullOrEmpty(b.Actor)) sb.Append(' ').Append(b.Actor);
                        if (!string.IsNullOrEmpty(b.Id)) sb.Append(" id=").Append(b.Id);
                        if (!string.IsNullOrEmpty(b.Prefab)) sb.Append(" pf=").Append(b.Prefab);
                        if (!string.IsNullOrEmpty(b.Case)) sb.Append(" case=").Append(b.Case);
                        if (b.Cmd == "camera_cut" || b.Cmd == "camera_return")
                            sb.Append(" blend=").Append(b.BlendSeconds.ToString("0.##", CultureInfo.InvariantCulture));
                        if (!string.IsNullOrEmpty(b.Anchor)) sb.Append(" @").Append(b.Anchor);
                        sb.Append("; ");
                    }
                    return "OK: " + sb;
                }

                case "only":
                {
                    if (runner == null) return "Error: stage not started yet.";
                    int idx;
                    if (!int.TryParse(a1, out idx)) return "Usage: custom.story_probe stage only <index>";
                    return runner.RunSingle(idx) ? $"OK: ran beat {idx} (see log for result)." : $"Error: beat {idx} out of range.";
                }

                case "begin":
                    // 没有执行器也能建一个 —— 方便在当前场景直接重播整套布景/站位
                    return host.EnsureStage().Begin("StoryOpeningTimeline")
                        ? "OK: stage started (freeze -> props -> cast -> camera back -> release)."
                        : "Error: stage begin failed (StoryOpeningTimeline.json missing? see log).";

                case "skip":
                {
                    // 准备期跳过：先当场把布景/摆人做完并判定，再交还控制，不播开场。
                    string prepState = host.SkipStage();
                    return "OK: stage skipped (camera released, player unfrozen). prep=" + prepState;
                }

                case "reload":
                    StoryStageDefs.ClearCache();
                    return StoryStageDefs.Load("StoryOpeningTimeline", forceReload: true) != null
                        ? "OK: StoryOpeningTimeline.json reloaded (takes effect on next 'stage begin')."
                        : "Error: reload failed (see log).";

                case "anchor":
                    return "OK: anchor positions are printed to the log as [StoryStage] anchor '<name>' = (x,y,z).";

                default:
                    return "Usage: custom.story_probe stage list|only <n>|begin|skip|reload|status|anchor";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // P0 契约：原子支持表 / 夹具校验 / 数据发布（2026-11-15）
        //   · script list     —— 看注册表里有哪些原子、哪些已实现（拟实现的不许进正式数据）
        //   · script validate —— 跑 Debug/PlanExamples 下的 P0 夹具，正样例必须 PASS、负样例必须 FAIL
        //   · script status   —— 当前已发布的内容版本 + 上次校验结果
        // ═══════════════════════════════════════════════════════════════

        private static string _lastScriptReport;

        private static readonly Dictionary<string, string> ScriptFixtureExpectations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "STAGE_OK_two_guards.json", null },
            { "STAGE_BAD_unknown_token.json", "unknown_atom" },
            { "STAGE_BAD_roster_missing_actor.json", "actor_not_in_roster" },
            { "STAGE_BAD_dual_geometry.json", "dual_geometry" },
            { "STAGE_BAD_wait_no_timeout.json", "wait_no_timeout" },
            { "STAGE_BAD_empty_atom_args.json", "unknown_param" },
            { "STAGE_BAD_duplicate_step.json", "duplicate_id" },
            { "STAGE_BAD_unknown_role.json", "unknown_role" },
            { "STAGE_BAD_role_type.json", "wrong_type" },
            { "STAGE_BAD_arg_type.json", "wrong_type" },
        };

        private static string ModuleRoot()
        {
            try
            {
                var dll = typeof(StoryProbeCommands).Assembly.Location;
                return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)));
            }
            catch { return null; }
        }

        /// <summary>
        /// 选角表 ID 集合。夹具模式额外并入夹具选角表（gate_sentry 尚未进正式表），
        /// 正式模式**只认** ModuleData/DesignData/StoryProbeCast.json。
        /// </summary>
        private static List<string> CastIds(string moduleRoot, bool includeFixtureCast)
        {
            var ids = StoryLayoutParser.LoadCastIds(
                Path.Combine(moduleRoot, "ModuleData", "DesignData", "StoryProbeCast.json"));
            if (includeFixtureCast)
                foreach (string id in StoryLayoutParser.LoadCastIds(
                    Path.Combine(moduleRoot, "Debug", "PlanExamples", "STAGE_CAST_two_guards.json")))
                    if (!ids.Contains(id)) ids.Add(id);
            return ids;
        }

        private static string HandleScript(string a0, string a1)
        {
            string moduleRoot = ModuleRoot();
            if (string.IsNullOrEmpty(moduleRoot)) return "Error: cannot resolve module root.";

            switch ((a0 ?? "").ToLowerInvariant())
            {
                case "":
                case "status":
                    return StoryLayoutRegistry.Describe() + "\n" +
                           "loader: " + StoryV2Data.Report + "\n" +
                           (_lastScriptReport ?? "fixtures: run 'custom.story_probe script validate' to check the P0 fixtures.");

                case "list":
                    return StoryAtomRegistry.Describe();

                case "validate":
                    return string.IsNullOrWhiteSpace(a1) ? ValidateFixtures(moduleRoot) : ValidateOne(moduleRoot, a1.Trim());

                default:
                    return "Usage: custom.story_probe script list|status|validate [file]";
            }
        }

        private static string ValidateFixtures(string moduleRoot)
        {
            string folder = Path.Combine(moduleRoot, "Debug", "PlanExamples");
            if (!Directory.Exists(folder)) return "Error: fixture folder missing: " + folder;

            var files = Directory.GetFiles(folder, "STAGE_*.json")
                .Where(p => !Path.GetFileName(p).StartsWith("STAGE_CAST_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
            if (files.Count == 0) return "Error: no STAGE_*.json fixtures in " + folder;

            var ids = CastIds(moduleRoot, includeFixtureCast: true);
            var sb = new System.Text.StringBuilder();
            int checkedCount = 0, mismatches = 0;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in files)
            {
                var r = StoryLayoutParser.Parse(File.ReadAllText(path), path, ids, StoryValidationMode.Fixture);
                string name = Path.GetFileName(path);
                found.Add(name);
                string expected;
                bool matches = ScriptFixtureExpectations.TryGetValue(name, out expected)
                    && (expected == null ? r.Ok : !r.Ok && r.Issues.Any(i => i.Code == expected));
                if (matches && expected == null)
                    matches = StoryLayoutParser.Parse(File.ReadAllText(path), path, ids, StoryValidationMode.Formal)
                        .Issues.Any(i => i.Code == "unimplemented_atom");
                checkedCount++;
                if (!matches) mismatches++;
                sb.Append(matches ? "  MATCH " : "  MISMATCH ").Append(name)
                    .Append(" expected=").Append(expected ?? "valid fixture / formal rejection")
                    .Append(" actual=").Append(r.Ok ? "valid" : string.Join(",", r.Issues.Select(i => i.Code).Distinct()))
                    .Append('\n');
            }
            foreach (string required in ScriptFixtureExpectations.Keys.Where(n => !found.Contains(n)))
            {
                mismatches++;
                sb.Append("  MISSING ").Append(required).Append('\n');
            }
            _lastScriptReport = "fixtures: checked=" + checkedCount + " mismatches=" + mismatches;
            return (mismatches == 0 ? "OK: " : "Error: ") + _lastScriptReport + " (cast=" + ids.Count + ")\n" + sb;
        }

        private static string ValidateOne(string moduleRoot, string name)
        {
            bool explicitPath = name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0 || name.IndexOf(':') >= 0;
            string path = explicitPath
                ? name
                : Path.Combine(moduleRoot, "Debug", "PlanExamples",
                               name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
            if (!File.Exists(path)) return "Error: file not found: " + path;

            bool fixture = path.IndexOf("PlanExamples", StringComparison.OrdinalIgnoreCase) >= 0;
            var mode = fixture ? StoryValidationMode.Fixture : StoryValidationMode.Formal;
            var ids = CastIds(moduleRoot, fixture);

            StoryValidationResult result;
            bool published = false;
            bool validateOnly = fixture || Mission.Current != null;
            if (validateOnly)
                result = StoryLayoutParser.Parse(File.ReadAllText(path), path, ids, mode);
            else
                published = StoryLayoutRegistry.TryPublish(File.ReadAllText(path), path, ids, mode, out result);
            _lastScriptReport = result.Describe();
            if (fixture) return _lastScriptReport + "\nINFO: fixture validated only; current content was not changed.";
            if (validateOnly) return _lastScriptReport + "\nINFO: validated only during a mission; leave and re-enter the story scene to publish layout changes.";
            return published
                ? result.Describe() + "\nOK: published as current content version (any failure keeps the old one)."
                : result.Describe();
        }

        // ═══════════════════════════════════════════════════════════════
        // P1 人口限制诊断（2026-11-15）
        //   · population status —— 本 Mission 有没有剧情人口标记、各生成来源被拦了几次
        //   · population verify —— 现场点名：玩家 / 已登记演员 / 名单外对象
        // ═══════════════════════════════════════════════════════════════

        private static string HandlePopulation(string action)
        {
            switch ((action ?? "").ToLowerInvariant())
            {
                case "":
                case "status":
                    return StoryPopulationGuard.Describe();

                case "verify":
                {
                    var mission = Mission.Current;
                    if (mission == null) return "Error: not in a mission.";

                    var registered = new HashSet<Agent>();
                    var host = StoryProbeMissionBehavior.Current;
                    if (host != null && host.Active && ReferenceEquals(host.Mission, mission))
                    {
                        foreach (var pair in host.AllAgents)
                        {
                            var ag = pair.Value;
                            if (ag != null) registered.Add(ag);
                        }
                    }

                    int player = 0, cast = 0, mounts = 0, unregistered = 0;
                    var strangers = new List<string>();
                    foreach (var agent in mission.Agents)
                    {
                        if (agent == null || !AgentControlHelper.SafeIsActive(agent)) continue;
                        if (agent.IsMainAgent) { player++; continue; }
                        if (!agent.IsHuman || agent.IsMount) { mounts++; continue; }
                        if (registered.Contains(agent)) { cast++; continue; }
                        unregistered++;
                        if (strangers.Count < 10)
                            strangers.Add((agent.Character?.StringId ?? "?") + "#" + agent.Index);
                    }

                    var sb = new System.Text.StringBuilder();
                    bool guarded = StoryPopulationGuard.IsActive;
                    bool clean = player == 1 && mounts == 0 && unregistered == 0;
                    bool owned = host != null && host.Active && ReferenceEquals(host.Mission, mission);
                    sb.Append(guarded ? (clean && owned ? "OK: " : "Error: ") : "INFO: ")
                      .Append("population verify player=").Append(player)
                      .Append(" cast=").Append(cast)
                      .Append(" mounts_animals=").Append(mounts)
                      .Append(" unregistered=").Append(unregistered)
                      .Append(" guard=").Append(StoryPopulationGuard.IsActive ? "ACTIVE" : "inactive")
                      .Append(" scope=population_only roster=not_checked");
                    if (strangers.Count > 0) sb.Append("\n  unregistered: ").Append(string.Join(", ", strangers));
                    if (!StoryPopulationGuard.IsActive)
                        sb.Append("\n  note: this mission carries no story marker, so normal town population is expected here.");
                    return sb.ToString();
                }

                default:
                    return "Usage: custom.story_probe population status|verify";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // P3/P4 诊断（2026-11-15）：布局 / 岗位选人 / 工作点 / 群组
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// **场景实体清单**（2026-11-17）：锚点（`gate_entity` / `guard_spawn`）在场景里找不到时，
        /// 用它直接在游戏里看"这个场景到底有哪些实体、名字叫什么、带什么 tag、在哪个坐标"。
        /// 用法：`custom.story_probe scene list [filter]` —— 过滤词同时匹配 实体名 / prefab 名 / tag。
        /// 全部实体写进模块的 `Debug\scene_entities.txt`（制表符分隔，可直接用 Excel 打开），
        /// 控制台只回"总数 + 匹配数 + 前 12 条"，避免刷屏。
        /// 只读：不移动、不生成、不改配置/剧情/财物。
        /// </summary>
        private static string HandleScene(string action, string arg)
        {
            string sub = (action ?? "").Trim().ToLowerInvariant();
            if (sub.Length > 0 && sub != "list" && sub != "find")
                return "Usage: custom.story_probe scene list [filter]";

            var scene = Mission.Current?.Scene;
            if (scene == null) return "Error: no mission scene (enter the town center first).";

            var entities = new List<TaleWorlds.Engine.GameEntity>();
            try { scene.GetEntities(ref entities); }
            catch (Exception ex) { return "Error: cannot enumerate scene entities: " + ex.Message; }

            string filter = (arg ?? "").Trim();
            bool Match(string s) => !string.IsNullOrEmpty(s)
                && s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            var lines = new List<string> { "name\tprefab\tx,y,z\ttags" };
            var preview = new List<string>();
            int matched = 0;
            foreach (var e in entities)
            {
                if (e == null) continue;
                string name = "", prefab = "";
                string[] tags = new string[0];
                Vec3 pos = Vec3.Zero;
                try { name = e.Name ?? ""; } catch { }
                try { prefab = e.GetPrefabName() ?? ""; } catch { }
                try { tags = e.Tags ?? new string[0]; } catch { }
                try { pos = e.GlobalPosition; } catch { }

                if (filter.Length > 0 && !(Match(name) || Match(prefab) || tags.Any(Match))) continue;
                matched++;
                lines.Add(name + "\t" + prefab + "\t"
                          + pos.x.ToString("F1") + "," + pos.y.ToString("F1") + "," + pos.z.ToString("F1")
                          + "\t" + string.Join(" ", tags));
                if (preview.Count < 12) preview.Add(name + (prefab.Length == 0 ? "" : "[" + prefab + "]"));
            }

            string file;
            try
            {
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string moduleRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dllPath), "..", ".."));
                string debugDir = Path.Combine(moduleRoot, "Debug");
                if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);
                file = Path.Combine(debugDir, "scene_entities.txt");
                File.WriteAllText(file, string.Join(Environment.NewLine, lines), new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) { return "Error: cannot write dump: " + ex.Message; }

            string head = preview.Count == 0
                ? "(none)"
                : string.Join(" ; ", preview) + (matched > preview.Count ? " ..." : "");
            return "OK: entities=" + entities.Count + " matched=" + matched + " file=" + file + " ; " + head;
        }

        private static string HandleLayout(string a0)
        {
            string sub = (a0 ?? "").Trim().ToLowerInvariant();
            if (sub == "lint-file")
            {
                string folder = StoryProbeDataFiles.DesignDataFolder;
                if (string.IsNullOrEmpty(folder)) return "Error: no design data folder.";
                string path = Path.Combine(folder, "StoryScenePlan.json");
                if (!File.Exists(path)) return "Error: StoryScenePlan.json missing.";
                var ids = StoryLayoutParser.LoadCastIds(Path.Combine(folder, "StoryProbeCast.json"));
                var check = StoryLayoutParser.Parse(File.ReadAllText(path), path, ids, StoryValidationMode.Formal);
                return (check.Ok ? "OK: " : "Error: ") + "StoryScenePlan.json disk validation; "
                    + "contentVersion=" + (check.Data?.ContentVersion ?? 0)
                    + " issues=" + check.Issues.Count + " (published layout unchanged) "
                    + string.Join("; ", check.Issues.Select(i => i.Path + " [" + i.Code + "]"));
            }
            if (!string.IsNullOrEmpty(sub) && sub != "list" && sub != "verify" && sub != "snapshot" && sub != "prep")
                return "Usage: custom.story_probe layout list|verify|snapshot|prep|lint-file";
            if (sub == "prep")
            {
                var h = StoryProbeMissionBehavior.Current;
                return h?.Prep?.Describe() ?? "Error: no prep batch in this mission (enter the second phase first).";
            }
            if (!StoryV2Data.EnsurePublished()) return "Error: " + StoryV2Data.Report;
            var file = StoryLayoutRegistry.Published;
            if (file == null) return "Error: no published layout.";

            var sb = new System.Text.StringBuilder();
            if (sub == "verify")
            {
                string scene = Mission.Current?.SceneName;
                bool sceneOk = Mission.Current == null || string.Equals(scene, file.Scene, StringComparison.OrdinalIgnoreCase);
                sb.Append(sceneOk ? "OK: " : "Error: ").Append("scene published=").Append(file.Scene)
                  .Append(" mission=").Append(scene ?? "(map)");
                sb.Append("\n  contentVersion=").Append(file.ContentVersion)
                  .Append(" layouts=").Append(file.Layouts.Count)
                  .Append(" stages=").Append(file.Stages.Count)
                  .Append(" behaviors=").Append(file.Behaviors.Count)
                  .Append(" flows=").Append(file.Flows.Count)
                  .Append(" solved=").Append(StoryLayoutRegistry.Solved.Count);
                foreach (var layout in file.Layouts.Values)
                    sb.Append("\n  layout ").Append(layout.Id)
                      .Append(" anchors=").Append(layout.Anchors.Count)
                      .Append(" gates=").Append(layout.Gates.Count)
                      .Append(" props=").Append(layout.Props.Count)
                      .Append(" points=").Append(layout.Points.Count)
                      .Append(" (post=").Append(layout.Points.Count(x => x.Role == "post"))
                      .Append(", work=").Append(layout.Points.Count(x => x.Role == "work")).Append(")")
                      .Append(" standing=").Append(layout.Standing.Count);
                foreach (var p in StoryLayoutRegistry.Solved.Take(20))
                    sb.Append("\n  solved ").Append(p);
                sb.Append("\n  note: geometry still needs a real scene snapshot (V06); this command only verifies data/scene binding.");
                return sb.ToString();
            }

            if (sub == "snapshot")
            {
                var host = StoryProbeMissionBehavior.Current;
                sb.Append("OK: snapshot");
                if (host == null) { sb.Append(" (not in a mission; showing data only)"); }
                foreach (var stage in file.Stages.Values)
                {
                    if (!file.Layouts.TryGetValue(stage.Layout, out var layout)) continue;
                    sb.Append("\n  stage ").Append(stage.Id).Append(" layout=").Append(layout.Id)
                      .Append(" actors=").Append(stage.Actors.Count);
                    foreach (var s in layout.Standing)
                    {
                        string where = !string.IsNullOrEmpty(s.Point) ? "point:" + s.Point : "anchor";
                        var agent = host?.GetAgent(s.Actor);
                        sb.Append("\n    ").Append(s.Actor).Append(" -> ").Append(where);
                        if (host != null)
                            sb.Append(agent == null ? " [MISSING]" : " at " + agent.Position.ToString());
                    }
                }
                return sb.ToString();
            }

            sb.Append("OK: layouts=").Append(file.Layouts.Count);
            foreach (var layout in file.Layouts.Values)
            {
                sb.Append("\n  ").Append(layout.Id).Append(" v").Append(layout.Version)
                  .Append(" anchors=[").Append(string.Join(",", layout.Anchors.Keys)).Append("]")
                  .Append(" gates=[").Append(string.Join(",", layout.Gates.Keys)).Append("]")
                  .Append(" points=[").Append(string.Join(",", layout.Points.Select(p => p.Id))).Append("]");
            }
            foreach (var stage in file.Stages.Values)
                sb.Append("\n  stage ").Append(stage.Id).Append(" actors=[").Append(string.Join(",", stage.Actors)).Append("]");
            return sb.ToString();
        }

        private static string HandleIntercept(string a0)
        {
            string sub = (a0 ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(sub) && sub != "status")
                return "Usage: custom.story_probe intercept status";
            var host = StoryProbeMissionBehavior.Current;
            var sb = new System.Text.StringBuilder();
            if (host?.Posts != null) sb.Append(host.Posts.Describe());
            else
            {
                StoryV2Data.EnsurePublished();
                sb.Append("OK: no active story mission; data=").Append(StoryV2Data.Report);
            }
            sb.Append("\n").Append(StoryDialogueRuntime.Describe());
            sb.Append("\n").Append(StoryDialogueRegistry.Describe());
            return sb.ToString();
        }

        private static string HandleWork(string a0, string a1)
        {
            string sub = (a0 ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(sub) && sub != "status" && sub != "list" && sub != "force")
                return "Usage: custom.story_probe work status|list|force <npcId>";
            var host = StoryProbeMissionBehavior.Current;
            if (sub == "force")
            {
                if (string.IsNullOrWhiteSpace(a1)) return "Usage: custom.story_probe work force <npcId>";
                return host?.Work != null && host.Work.ForceMove(a1.Trim())
                    ? "OK: work slot reset for " + a1.Trim()
                    : "Error: no work slot for '" + a1.Trim() + "'.";
            }
            if (host?.Work != null)
            {
                string live = host.Work.Describe();
                if (sub != "status") return live;
                return live + "\n" + DataWorkList();
            }
            return DataWorkList();
        }

        private static string DataWorkList()
        {
            if (!StoryV2Data.EnsurePublished()) return "Error: " + StoryV2Data.Report;
            var file = StoryLayoutRegistry.Published;
            var sb = new System.Text.StringBuilder();
            int total = 0;
            foreach (var layout in file.Layouts.Values)
            {
                foreach (var w in layout.Points.Where(x => !string.IsNullOrEmpty(x.Action)))
                {
                    total++;
                    sb.Append("\n  ").Append(layout.Id).Append("/").Append(w.Id)
                      .Append(" prop=").Append(w.Prop ?? w.Place.Anchor ?? "-")
                      .Append(" side=").Append(w.Side)
                      .Append(" offset=").Append(w.Offset.ToString("F2"))
                      .Append(" tolerance=").Append(w.ArriveTolerance.ToString("F2"))
                      .Append(" action=").Append(w.Action ?? "-")
                      .Append(" occupants=0/").Append(w.MaxOccupants);
                    var presentation = file.PresentationFor(w.Action);
                    if (presentation != null)
                        sb.Append(" instrument=").Append(presentation.Instrument ?? "-")
                          .Append(" sounds=").Append(string.Join("|", presentation.Sounds))
                          .Append(" speed=").Append(presentation.Speed);
                }
            }
            return "OK: workpoints=" + total + " runtime=no_active_mission" + sb;
        }

        private static string HandleGroup(string a0)
        {
            string sub = (a0 ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(sub) && sub != "status")
                return "Usage: custom.story_probe group status";
            var host = StoryProbeMissionBehavior.Current;
            if (host == null) return "OK: no active story mission.";
            return (host.Director?.Describe() ?? "Street groups inactive.") + "\nGuard groups:\n" + (host.Posts?.Describe() ?? "none");
        }

        // ═══════════════════════════════════════════════════════════════
        // P0-D.1 对话定义契约（2026-11-15）
        //   · dialogue tree      —— 现有 9 份正式对话树的图校验（旧口径）
        //   · dialogue validate  —— story_dialogue/2 契约与 DIALOGUE_* 夹具（1 正 + 负例按错误码比对）
        //   · dialogue status    —— 已发布的对话定义集
        // ═══════════════════════════════════════════════════════════════

        private static string _lastDialogueReport;

        private static string HandleDialogue(string a0, string a1, string a2, string a3)
        {
            switch ((a0 ?? "").ToLowerInvariant())
            {
                case "":
                case "tree":
                    return "OK: " + StoryProbeDialogue.ValidateAll();

                case "validate":
                    return string.IsNullOrWhiteSpace(a1) ? ValidateDialogueFixtures() : ValidateDialogueOne(a1.Trim());

                case "resolve":
                {
                    if (string.IsNullOrWhiteSpace(a1) || string.IsNullOrWhiteSpace(a2))
                        return "Usage: custom.story_probe dialogue resolve <dialogueId> <triggerId> [actorId]";
                    var ctx = new StoryDialogueContext
                    {
                        DialogueId = a1.Trim(),
                        TriggerId = a2.Trim(),
                        ActorId = (a3 ?? "").Trim()
                    };
                    var resolution = StoryDialogueResolver.Resolve(ctx.DialogueId, ctx.TriggerId, ctx);
                    _lastDialogueReport = resolution.Describe();
                    return resolution.Describe();
                }

                case "trace":
                {
                    var cm = Campaign.Current?.ConversationManager;
                    string NativeId(IAgent agent) => agent != null && agent == Agent.Main ? "player"
                        : StoryProbeDialogue.ResolveNpcId(agent as Agent) ?? "-";
                    return StoryDialogueRuntime.Describe() + "\n  nativeSpeaker=" + NativeId(cm?.SpeakerAgent)
                        + " nativeListener=" + NativeId(cm?.ListenerAgent)
                        + "\n  " + StoryProbeDialogue.DescribeDialogueFacing();
                }

                case "locks":
                {
                    var prep = StoryProbeMissionBehavior.Current?.Prep;
                    if (prep?.Layout == null) return "OK: no prepared scene locks.";
                    var lines = new List<string> { "OK: scene lock state; read-only; no movement, unlock or reward." };
                    foreach (var prop in prep.Layout.Props.Where(p => p.Lock != null))
                    {
                        var rule = prop.Lock;
                        var ctx = new StoryDialogueContext { ActorId = rule.Actor };
                        bool valid = StoryDialogueFacts.TryEvaluateAll(rule.Requires, ctx, out bool allowed, out var error, out _);
                        lines.Add("  " + prop.Id + " prepared=" + prep.HasProp(prop.Id)
                            + " owner=" + rule.Actor + " pins=" + rule.Pins
                            + " allowed=" + (valid && allowed) + " opened=" + StoryProbeState.HasAsked(rule.Actor, rule.Flag)
                            + " flag=" + rule.Flag + (error == null ? "" : " error=" + error));
                    }
                    return string.Join("\n", lines);
                }

                case "options":
                {
                    var session = StoryDialogueRuntime.Current;
                    if (session == null) return "OK: no active story dialogue.";
                    var node = session.Definition.FindNode(session.CurrentNode);
                    if (node == null) return "OK: no shown node yet.";
                    var menu = session.Definition.OptionsNode(node);
                    var lines = new List<string> { "OK: node=" + node.Id + " menu=" + menu.Id
                        + " requires=" + string.Join(",", menu.Requires)
                        + " definition options (including condition-hidden options); previews only; no roll/effects." };
                    foreach (var option in session.Definition.OptionsFor(node))
                    {
                        if (option.NextNode == node.Id || option.Check?.Success == node.Id || option.Check?.Fail == node.Id) continue;
                        string target = option.Check?.Target;
                        if (string.IsNullOrEmpty(target) || target == "npc" || target == "questioner") target = session.Request.ActorId;
                        if (option.Check == null) lines.Add("  " + option.Id + " check=none hintKey=" + (option.HintKey ?? "default"));
                        else if (StoryProbeDefs.TryParseDialogueTactic(option.Check.Tactic, out var tactic))
                        {
                            var preview = StoryProbeDefs.PreviewDialogueCheck(target, tactic, option.Check.Capability);
                            lines.Add("  " + option.Id + " resolver=SimpleCompute tactic=" + tactic
                                + " target=" + target + " capability=" + preview?.Capability + " attribute=" + preview?.Ability + " skill=" + preview?.Skill?.StringId
                                + " player=" + preview?.PlayerAttr + " npc=" + preview?.NpcAttr
                                + " chance=" + (preview?.Chance * 100f)?.ToString("F1") + "%"
                                + " success=" + option.Check.Success + " fail=" + option.Check.Fail);
                        }
                    }
                    return string.Join("\n", lines);
                }

                case "turns":
                {
                    if (!StoryDialogueRegistry.TryGet(a1 ?? "", out var definition))
                        return "Usage: custom.story_probe dialogue turns <dialogueId> (published definition required)";
                    var lines = new List<string> { "OK: dialogue=" + definition.DialogueId + " v" + definition.ContentVersion };
                    foreach (var node in definition.Nodes)
                        lines.Add("  " + node.Id + " speaker=" + node.Speaker + " listener=" + node.Listener
                            + " text=" + node.TextKey + " next=" + (node.NextNode ?? "options/close")
                            + " menu=" + (node.OptionsFrom ?? node.Id)
                            + " options=" + definition.OptionsFor(node).Count());
                    return string.Join("\n", lines);
                }

                case "status":
                    return StoryDialogueRegistry.Describe() + "\n" +
                           (_lastDialogueReport ?? "fixtures: run 'custom.story_probe dialogue validate'");

                default:
                    return "Usage: custom.story_probe dialogue [tree]|validate [file]|resolve <dialogueId> <triggerId> [actorId]|turns <dialogueId>|options|trace|status";
            }
        }

        private static string ValidateDialogueFixtures()
        {
            string moduleRoot = ModuleRoot();
            if (string.IsNullOrEmpty(moduleRoot)) return "Error: cannot resolve module root.";
            string folder = Path.Combine(moduleRoot, "Debug", "PlanExamples");
            if (!Directory.Exists(folder)) return "Error: fixture folder missing: " + folder;

            var files = Directory.GetFiles(folder, "DIALOGUE_*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
            if (files.Count == 0) return "Error: no DIALOGUE_*.json fixtures in " + folder;

            var sb = new System.Text.StringBuilder();
            int pass = 0, fail = 0, mismatched = 0;
            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                bool expectOk = name.StartsWith("DIALOGUE_OK_", StringComparison.OrdinalIgnoreCase);
                var r = StoryDialogueParser.Parse(File.ReadAllText(path), path, formalMode: false);
                bool gotOk = r.Ok;
                if (gotOk == expectOk)
                {
                    if (expectOk) { pass++; sb.Append("  PASS ").Append(name).Append('\n'); }
                    else { fail++; sb.Append("  FAIL(expected) ").Append(name).Append(" -- ").Append(r.Issues[0]).Append('\n'); }
                }
                else
                {
                    mismatched++;
                    sb.Append("  MISMATCH ").Append(name)
                      .Append(expectOk ? " expected PASS but got issues: " : " expected FAIL but passed")
                      .Append(gotOk ? (r.Issues.Count > 0 ? r.Issues[0].ToString() : "") : "").Append('\n');
                }
            }
            _lastDialogueReport = "dialogues: pass=" + pass + " fail=" + fail + " mismatched=" + mismatched + " of " + files.Count;
            return "OK: dialogue fixtures pass=" + pass + " fail=" + fail + " mismatched=" + mismatched + " of " + files.Count
                 + "\n" + sb;
        }

        private static string ValidateDialogueOne(string name)
        {
            string moduleRoot = ModuleRoot();
            if (string.IsNullOrEmpty(moduleRoot)) return "Error: cannot resolve module root.";
            bool explicitPath = name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0 || name.IndexOf(':') >= 0;
            string path = explicitPath
                ? name
                : Path.Combine(moduleRoot, "Debug", "PlanExamples",
                               name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
            if (!File.Exists(path)) return "Error: file not found: " + path;

            bool fixture = path.IndexOf("PlanExamples", StringComparison.OrdinalIgnoreCase) >= 0;
            var result = StoryDialogueParser.Parse(File.ReadAllText(path), path, formalMode: !fixture);
            _lastDialogueReport = result.Describe();
            if (result.Ok && !fixture)
            {
                StoryDialogueRegistry.TryPublish(result.Data, path);
                return result.Describe() + "\nOK: published as current dialogue definition.";
            }
            return result.Describe();
        }

        /// <summary>
        /// 单发一个原子：`custom.story_probe atom prop_spawn bd_table_c gate 2.5 -1 90`
        /// 参数顺序见各原子的注释。**不依赖台本**，用于逐件调位置。
        /// </summary>
        private static string HandleAtom(List<string> args, int used)
        {
            var host = StoryProbeMissionBehavior.Current;
            if (host == null) return "Error: not in a story probe mission.";
            var runner = host.EnsureStage();

            string name = (Arg(args, used, 0) ?? "").ToLowerInvariant();
            var beat = new StageBeat();

            switch (name)
            {
                case "prop_spawn":
                    beat.Cmd = "prop_spawn";
                    beat.Prefab = Arg(args, used, 1);
                    beat.Anchor = Arg(args, used, 2);
                    beat.Fwd = ParseFloat(args, used, 3);
                    beat.Right = ParseFloat(args, used, 4);
                    beat.Yaw = ParseFloat(args, used, 5);
                    beat.Id = Arg(args, used, 6) ?? ("atom_" + (beat.Prefab ?? "prop"));
                    break;

                case "actor_place":
                    beat.Cmd = "actor_place";
                    beat.Actor = Arg(args, used, 1);
                    beat.Anchor = Arg(args, used, 2);
                    beat.Fwd = ParseFloat(args, used, 3);
                    beat.Right = ParseFloat(args, used, 4);
                    beat.Facing = ParseFloat(args, used, 5, -1f);
                    break;

                case "actor_action":
                    beat.Cmd = "actor_action";
                    beat.Actor = Arg(args, used, 1);
                    beat.Action = Arg(args, used, 2);
                    break;

                case "actor_say":
                    beat.Cmd = "actor_say";
                    beat.Actor = Arg(args, used, 1);
                    beat.TextKey = Arg(args, used, 2);
                    beat.Listener = Arg(args, used, 3);
                    break;

                case "camera_cut":
                    beat.Cmd = "camera_cut";
                    beat.Case = Arg(args, used, 1);
                    beat.Anchor = Arg(args, used, 2);
                    if (float.TryParse(Arg(args, used, 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float cutBlend))
                        beat.BlendSeconds = Math.Max(0f, cutBlend);
                    break;

                case "camera_return":
                    beat.Cmd = "camera_return";
                    if (float.TryParse(Arg(args, used, 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float returnBlend))
                        beat.BlendSeconds = Math.Max(0f, returnBlend);
                    break;

                case "player_freeze":
                    beat.Cmd = "player_freeze";
                    break;

                case "player_release":
                    beat.Cmd = "player_release";
                    break;

                default:
                    return "Usage: custom.story_probe atom <prop_spawn|actor_place|actor_action|actor_say|camera_cut|camera_return|player_freeze|player_release> ...";
            }

            runner.RunAtom(beat);
            return $"OK: atom '{beat.Cmd}' executed (result in log).";
        }

        private static float ParseFloat(List<string> args, int used, int index, float fallback = 0f)
        {
            float v;
            string raw = Arg(args, used, index);
            if (!string.IsNullOrWhiteSpace(raw)
                && float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        private static string HelpText()
        {
            return "custom.story_probe <sub> -- subs: enable | disable | city [id] | status | crime | quests | enter | intro | " +
                   "phase first|second | where | persona [actorId] | spawn | check <ability> | roll <ability> <0-100> | " +
                   "learn <ability> | resolve <ability> | strip | restore | reset | talk <npc> | goal <0..4> | " +
                   "gate [grant|revoke] | " +
                   "lesson status|cancel|reload | combat status | life debug on|off|status|list|force|say <speaker> <listener>|bark <npc>|facing <npc>|release <npc>|scene|stop|resume | event list|force <n> | dialogue | goto | abort | money | " +
                   "stage list|only <n>|begin|skip|reload|status|anchor | atom <name> <args> | action <npc> <act> | " +
                   "script list|status|validate [file] | population status|verify | " +
                   "layout list|verify|snapshot|prep|lint-file | environment status|time|weather|snowcover|set|reset|help | intercept status | work status|list|force <npc> | group status | reload | help";
        }
    }
}

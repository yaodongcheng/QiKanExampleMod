using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

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
                "enable", "disable", "city", "status", "enter", "intro", "phase", "where", "spawn",
                "check", "roll", "learn", "resolve", "strip", "restore", "reset", "talk", "goal",
                "life", "event", "dialogue", "goto", "abort", "money", "stage", "atom", "action", "reload", "hud", "help",
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
                        bool settled = StoryProbeBehavior.CancelPrologue("target city changed by command");
                        Settings.Instance.StoryProbeSettlementId = id.Trim();
                        return $"{(settled ? "OK" : "Pending escrow restoration")}: target city = {StoryProbeSettings.SettlementId}";
                    }

                    case "status":
                        return "OK: " + StoryProbeState.Describe()
                             + " || steps: " + StoryProbeState.DescribeSteps()
                             + " || clues=" + StoryProbeState.ClueCount + " asked=" + StoryProbeState.AskedCount
                             + " || mission=" + (StoryProbeMissionBehavior.Current?.Active == true
                                 ? "active(" + StoryProbeMissionBehavior.Current.Phase + ")"
                                 : "none");

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
                            return StoryProbeBehavior.CancelPrologue("phase first by command")
                                ? "OK: cancelled safely; phase = FirstAttempt."
                                : "Pending: original belongings/skills still outstanding; phase not reset.";
                        }
                        if (p == "second")
                        {
                            var host = StoryProbeMissionBehavior.Current;
                            if (host?.Active != true || host.Phase != StoryProbePhase.Jail
                                || !StoryProbeState.Data.JailSceneEntered)
                                return "Error: enter the real prison and reach a safe cell point before changing clothes.";
                            host.CompleteJailDisguise();
                            return StoryProbeState.Data.JailDone ? "OK: disguise completed; real return transition queued."
                                : "Pending: disguise not completed; prison phase retained.";
                        }
                        return $"Usage: custom.story_probe phase first|second (current={StoryProbeState.Phase})";
                    }

                    case "where":
                    {
                        var host = StoryProbeMissionBehavior.Current;
                        return host != null && host.Active ? "OK: " + host.DescribeWhere()
                                                           : "OK: story probe not active in this mission";
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
                            float chance = Math.Max(0.05f, Math.Min(0.95f,
                                StoryProbeDefs.PlayerAttr(ability) / (StoryProbeDefs.PlayerAttr(ability) + na)));
                            sb.Append($" {def.Id}={na:F0}(chance {chance:P0}, threshold {(1f - chance):P0})");
                        }
                        return sb.ToString();
                    }

                    case "roll":
                    {
                        string ab = Arg(args, used, 0);
                        if (!StoryProbeDefs.TryParseAbility(ab, out StoryAbility ability))
                            return "Usage: custom.story_probe roll charm|guile|force <0-100>";
                        string val = Arg(args, used, 1);
                        if (!float.TryParse(val, out float pct))
                            return "Usage: custom.story_probe roll <ability> <0-100> (forced next roll)";
                        StoryProbeState.Data.RollOverride = Math.Max(0f, Math.Min(100f, pct));
                        StoryProbeState.Data.RollOverrideAbility = StoryProbeDefs.AbilityId(ability);
                        return $"OK: next {StoryProbeDefs.AbilityId(ability)} roll forced to {pct:F0}% (one use).";
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
                        var host = StoryProbeMissionBehavior.Current;
                        if (host?.Active != true || host.Phase != StoryProbePhase.Jail || !StoryProbeState.Data.JailSceneEntered)
                            return "Error: disguise is only available at a safe point inside the real prison.";
                        host.CompleteJailDisguise();
                        return (StoryProbeState.Data.JailDone ? "OK: disguise completed. " : "Pending: disguise failed. ") + StoryProbeState.Describe();
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

                    case "abort":
                        return (StoryProbeBehavior.CancelPrologue("abort by command")
                            ? "OK: cancelled safely; escrow settled. " : "Pending: cancellation retained outstanding escrow/skills. ") + StoryProbeState.Describe();

                    case "reset":
                        if (!StoryProbeBehavior.CancelPrologue("reset by command"))
                            return "Error: reset refused; original belongings or guard skill backup still outstanding.";
                        if (StoryProbeState.Data.QuestAccepted.Any(id => PrologueQuestLauncher.FindActive(id) != null))
                            return "Error: reset refused while native prologue quests are active; use abort or complete them first.";
                        StoryProbeState.ResetAll(); // 新建战役仍走纯 static 清理；只有本命令先清债。
                        StoryProbeDialogue.ClearCache();
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

                    case "life":
                        return HandleLife(Arg(args, used, 0), Arg(args, used, 1), Arg(args, used, 2));

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
                                return "OK: goal HUD back to auto (prologue active only).";
                            case "left":
                                StoryGoalHudMissionView.ForcePosition = "top-left";
                                return "OK: goal HUD position -> top-left.";
                            case "right":
                                StoryGoalHudMissionView.ForcePosition = "top-right";
                                return "OK: goal HUD position -> top-right.";
                            default:
                                return $"OK: hud visible={StoryGoalHudMissionView.LastVisible} " +
                                       $"position={StoryGoalHudMissionView.LastPosition} " +
                                       $"(usage: custom.story_probe hud on|off|auto|left|right)";
                        }
                    }

                    case "reload":
                        // 策划改完 JSON 后：重读数值表 / 节奏常量 / 演出台本 / 对话树，不用重启游戏
                        StoryProbeDefs.ReloadCast();
                        StoryProbeTuning.Reload();
                        StoryStageDefs.ClearCache();
                        StoryProbeDialogue.ClearCache();
                        return $"OK: reloaded StoryProbeCast/Tuning/Stage/Dialogues -- npcs={StoryProbeDefs.Npcs.Count} " +
                               $"events={StoryProbeDefs.Events.Count}; run 'stage begin' to replay.";

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
                        return "OK: " + StoryProbeDialogue.ValidateAll();

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
                    return "Usage: custom.story_probe life status|list|force <npc> <i>|scene <n>|stop|resume";
            }
        }

        private static string HandleEvent(string action, string a1)
        {
            var director = StoryProbeMissionBehavior.Current?.Director;
            switch ((action ?? "").ToLowerInvariant())
            {
                case "list":
                    return "OK: " + string.Join(" ; ", StoryProbeDefs.Events.Select(e =>
                        $"#{e.Index} {e.ActorA}{(e.TwoPerson ? "+" + e.ActorB : "")} host={e.HostNpcId} node={e.JoinNode}"));

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
                           $"anchors=[{string.Join(",", runner.AnchorNames.ToArray())}]";

                case "list":
                {
                    var script = StoryStageDefs.Load("StoryStage", forceReload: true);
                    if (script?.Beats == null) return "Error: StoryStage.json not found or empty (see log).";
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < script.Beats.Count; i++)
                    {
                        var b = script.Beats[i];
                        sb.Append(i).Append('@').Append(b.At.ToString("0.##")).Append("s ").Append(b.Cmd);
                        if (!string.IsNullOrEmpty(b.Actor)) sb.Append(' ').Append(b.Actor);
                        if (!string.IsNullOrEmpty(b.Id)) sb.Append(" id=").Append(b.Id);
                        if (!string.IsNullOrEmpty(b.Prefab)) sb.Append(" pf=").Append(b.Prefab);
                        if (!string.IsNullOrEmpty(b.Case)) sb.Append(" case=").Append(b.Case);
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
                    return host.EnsureStage().Begin("StoryStage")
                        ? "OK: stage started (freeze -> props -> cast -> camera back -> release)."
                        : "Error: stage begin failed (StoryStage.json missing? see log).";

                case "skip":
                    if (runner == null) return "Error: stage not started yet.";
                    runner.Skip();
                    return "OK: stage skipped (camera released, player unfrozen).";

                case "reload":
                    StoryStageDefs.ClearCache();
                    return StoryStageDefs.Load("StoryStage", forceReload: true) != null
                        ? "OK: StoryStage.json reloaded (takes effect on next 'stage begin')."
                        : "Error: reload failed (see log).";

                case "anchor":
                    return "OK: anchor positions are printed to the log as [StoryStage] anchor '<name>' = (x,y,z).";

                default:
                    return "Usage: custom.story_probe stage list|only <n>|begin|skip|reload|status|anchor";
            }
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
                    break;

                case "camera_cut":
                    beat.Cmd = "camera_cut";
                    beat.Case = Arg(args, used, 1);
                    beat.Anchor = Arg(args, used, 2);
                    break;

                case "camera_return":
                    beat.Cmd = "camera_return";
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
            return "custom.story_probe <sub> -- subs: enable | disable | city [id] | status | enter | intro | " +
                   "phase first|second | where | spawn | check <ability> | roll <ability> <0-100> | " +
                   "learn <ability> | resolve <ability> | strip | restore | reset | talk <npc> | goal <0..4> | " +
                   "life status|list|force|scene|stop|resume | event list|force <n> | dialogue | goto | abort | money | " +
                   "stage list|only <n>|begin|skip|reload|status|anchor | atom <name> <args> | action <npc> <act> | reload | help";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// `custom.story_probe cooperate ...` 的实现：纯英文返回、首参可弃、只读命令不改变世界。
    /// 诊断命令只读；test/start 有明确副作用并在返回里写清楚。
    /// </summary>
    public static class CoopCommands
    {
        public const string Help =
            "Usage: custom.story_probe cooperate <status|trace [runId]|plan|catalog [filter]|reload|preview <role> [actorId]|view open|on|off|stop [activityId]|pause [activityId]|resume [activityId]|start <role> [actorId]|test list|test begin <caseId> <goap|htn|hybrid> <seed> [quick]|test end>";

        public static string Handle(List<string> args, int used)
        {
            try { return Ascii(HandleCore(args, used)); }
            catch (Exception ex)
            {
                DebugLogger.Log("[Coop] command failed: " + ex.GetType().Name + " " + ex.Message);
                return "FAIL: cooperation command failed; inspect the runtime log";
            }
        }
        private static string Ascii(string text)
        {
            var builder = new StringBuilder();
            foreach (var character in text ?? "")
                if (character < 128) builder.Append(character);
                else builder.Append("\\u").Append(((int)character).ToString("x4"));
            return builder.ToString();
        }

        private static string HandleCore(List<string> args, int used)
        {
            if (args != null) foreach (var arg in args) foreach (var ch in arg ?? "")
                if (ch > 127) return "NO: ASCII arguments required; selection unchanged";
            var sub = Arg(args, used, 0);
            if (string.IsNullOrEmpty(sub))
            {
                return Help;
            }
            switch (sub.ToLowerInvariant())
            {
                case "status":
                    return Status();
                case "trace":
                    return Trace(Arg(args, used, 1));
                case "plan":
                    return Plan();
                case "catalog":
                    return Catalog(Arg(args, used, 1));
                case "reload":
                    return CoopMissionBehavior.Current?.ReloadContent() ?? "NO: no mission cooperation host";
                case "view":
                    return View(Arg(args, used, 1));
                case "stop":
                    return Stop(Arg(args, used, 1));
                case "pause":
                    return Pause(Arg(args, used, 1));
                case "resume":
                    return Resume(Arg(args, used, 1));
                case "start":
                    return Start(Arg(args, used, 1), Arg(args, used, 2), true);
                case "preview":
                    return Start(Arg(args, used, 1), Arg(args, used, 2), true);
                case "test":
                    return Test(args, used);
                default:
                    return Status() + "\nNote: placeholder ignored.\n" + Help;
            }
        }

        private static string Status()
        {
            var behavior = CoopMissionBehavior.Current;
            if (behavior == null)
            {
                return "NO: no mission cooperation host (not in a mission)";
            }
            if (!string.IsNullOrEmpty(behavior.LoadError))
            {
                return "FAIL: content rejected: " + behavior.LoadError;
            }
            return behavior.StatusText();
        }

        private static string Trace(string activityId)
        {
            var behavior = CoopMissionBehavior.Current;
            return behavior == null ? "NO: no mission cooperation host" : behavior.TraceText(activityId);
        }

        private static string Plan()
        {
            var behavior = CoopMissionBehavior.Current;
            return behavior == null ? "NO: no mission cooperation host" : behavior.PlanText();
        }

        private static string Catalog(string filter)
        {
            var director = CoopDirector.Current;
            if (director == null || director.Catalog == null)
            {
                return "NO: no catalog (content not loaded)";
            }
            return director.Catalog.Describe(filter);
        }

        private static string View(string mode)
        {
            var behavior = CoopMissionBehavior.Current;
            if (behavior == null)
            {
                return "NO: no mission cooperation host";
            }
            switch ((mode ?? string.Empty).ToLowerInvariant())
            {
                case "open":
                    return ImChatView.ShowCooperationCard() ? "OK: cooperation plan card opened; display only" : "NO: plan card unavailable or UI blocked";
                case "on":
                    CoopHudBridge.ForceVisible = true;
                    CoopHudBridge.Hidden = false;
                    StoryGoalHudMissionView.ForceVisible = true;
                    CoopLog.Event("preview.shown").Value("channel", "hud").Detail("forced").Write();
                    return "OK: cooperation view forced on (display only)";
                case "off":
                    CoopHudBridge.ForceVisible = false;
                    CoopHudBridge.Hidden = true;
                    StoryGoalHudMissionView.ForceVisible = null;
                    return "OK: cooperation view off (display only; bodies keep acting)";
                default:
                    return "OK: view=" + (CoopHudBridge.ForceVisible ? "on" : "off");
            }
        }

        private static string Stop(string activityId)
        {
            var director = CoopDirector.Current;
            var behavior = CoopMissionBehavior.Current;
            if (director == null || behavior == null)
            {
                return "NO: no cooperation session";
            }
            string detail;
            if (string.IsNullOrEmpty(activityId))
            {
                var activity = FindActive(director);
                if (activity == null)
                {
                    return "NO: no active cooperation activity";
                }
                activityId = activity.Id;
            }
            bool ok = director.Stop(activityId, "player_cancelled", behavior.SimTime, out detail);
            return ok ? "OK: " + detail : "NO: " + detail;
        }

        private static string Pause(string activityId)
        {
            var director = CoopDirector.Current;
            var behavior = CoopMissionBehavior.Current;
            if (director == null || behavior == null) return "NO: no cooperation session";
            var activity = string.IsNullOrEmpty(activityId) ? FindActive(director) : director.Find(activityId);
            if (activity == null) return "NO: no active cooperation activity";
            string detail;
            bool ok = director.Pause(activity.Id, "player_modal", behavior.SimTime, out detail);
            return ok ? "OK: " + detail : "NO: " + detail;
        }

        private static string Resume(string activityId)
        {
            var director = CoopDirector.Current;
            var behavior = CoopMissionBehavior.Current;
            if (director == null || behavior == null) return "NO: no cooperation session";
            var activity = string.IsNullOrEmpty(activityId) ? FindPaused(director) : director.Find(activityId);
            if (activity == null) return "NO: no paused cooperation activity";
            string detail;
            bool ok = director.Resume(activity.Id, "player_resume", behavior.SimTime, out detail);
            return ok ? "OK: " + detail : "NO: " + detail;
        }

        private static CoopActivity FindPaused(CoopDirector director)
        {
            foreach (var activity in director.Activities)
            {
                if (activity.State == CoopActivityState.Paused) return activity;
            }
            return null;
        }

        private static CoopActivity FindActive(CoopDirector director)
        {
            foreach (var activity in director.Activities)
            {
                if (activity.IsActive)
                {
                    return activity;
                }
            }
            return null;
        }

        private static string Start(string role, string actorId, bool preview = false)
        {
            var behavior = CoopMissionBehavior.Current;
            if (behavior == null)
            {
                return "NO: no mission cooperation host";
            }
            if (string.IsNullOrEmpty(role))
            {
                return "NO: role required (companion|guard)\n" + Help;
            }
            if (string.IsNullOrEmpty(actorId))
            {
                var config = CoopDirector.Current != null ? CoopDirector.Current.Content : null;
                actorId = behavior.DefaultActorId();
            }
            if (string.IsNullOrEmpty(actorId))
            {
                return "NO: actor id required";
            }
            string reason;
            var activity = behavior.StartCooperation(actorId, role, out reason, preview: preview);
            if (activity == null)
            {
                return "NO: " + reason;
            }
            return "OK: started " + activity.Id + " for " + actorId + " role=" + role
                + " plan=" + string.Join(" -> ", activity.Plan != null ? activity.Plan.Steps.ToArray() : new string[0]);
        }

        private static string Test(List<string> args, int used)
        {
            var mode = Arg(args, used, 1);
            switch ((mode ?? string.Empty).ToLowerInvariant())
            {
                case "list":
                    return "OK: T01 first roll=1; T02 first roll=0; T03 all rolls=1; T04 first=1, second=0.\n"
                        + "  custom.story_probe cooperate test begin <T01|T02|T03|T04> <goap|htn|hybrid> <seed> [quick]\n"
                        + "  Debug build only. Requires a playable story scene; without quick, an agreed companion is required.\n"
                        + "  quick: prepares a temporary talk agreement for the configured candidate, bypassing quest/event prerequisites only for this diagnostic.\n"
                        + "  No quest completion, rewards, recruitment, movement or gate access is granted by preparation; body/control checks remain.\n"
                        + "  Temporary ledger and RNG are scoped; actual gate crossing changes story progress. No isolated map fixture is provided.";
                case "begin":
                    return TestBegin(Arg(args, used, 2), Arg(args, used, 3), Arg(args, used, 4), Arg(args, used, 5));
                case "end":
                    return TestEnd();
                default:
                    return Help;
            }
        }

        private static string TestBegin(string caseId, string algorithm, string seedText, string preparation)
        {
#if !DEBUG
            return "NO: cooperation diagnostics require a Debug build";
#else
            var behavior = CoopMissionBehavior.Current;
            if (behavior == null)
            {
                return "NO: no mission cooperation host";
            }
            if (!Enum.TryParse(algorithm, true, out CoopAlgorithm parsed) || !Enum.IsDefined(typeof(CoopAlgorithm), parsed))
                return "NO: algorithm must be goap, htn or hybrid";
            if (!int.TryParse(seedText, out int seed)) return "NO: integer seed required";
            bool quick = string.Equals(preparation, "quick", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(preparation) && !quick) return "NO: preparation must be quick or omitted";
            var upper = (caseId ?? string.Empty).ToUpperInvariant();
            if (upper != "T01" && upper != "T02" && upper != "T03" && upper != "T04")
            {
                return "NO: unknown fixture '" + caseId + "' (supported: T01, T02, T03, T04)";
            }
            var role = "companion";
            var config = CoopDirector.Current != null ? CoopDirector.Current.Content : null;
            var actorId = quick ? config?.Recruitment.Candidate : behavior.DefaultActorId();
            string reason;
            if (!CoopTestSession.Begin(upper, seed, out reason)) return "NO: " + reason;
            bool started = false;
            try
            {
                if (quick && !CoopTestSession.PrepareTemporary(actorId, out reason))
                    return "NO: quick preparation refused: " + reason;
                var activity = quick
                    ? behavior.StartPreparedDiagnostic(actorId, role, out reason, parsed)
                    : behavior.StartCooperation(actorId, role, out reason, parsed, preview: true);
                if (activity == null) return "NO: fixture could not start: " + reason;
                CoopTestSession.Bind(activity.Id);
                started = true;
                return "OK: " + upper + " started activity=" + activity.Id
                    + " actor=" + actorId + " algorithm=" + activity.Algorithm + " preparation=" + (quick ? "quick" : "existing_agreement")
                    + " injection=" + (upper == "T03" ? "all rolls=1" : upper == "T04" ? "first roll=1, second roll=0" : upper == "T02" ? "first roll=0" : "first roll=1")
                    + (quick ? "\n  Real participants prepared without startup sight requirement. Execution still checks real sight, bodies and duty." : string.Empty)
                    + "\n  Review the plan card and press Start. Actors wait for approval. Crossing changes story progress; test end restores only the diagnostic agreement, ledger and RNG.";
            }
            finally { if (!started) CoopTestSession.End(); }
#endif
        }

        private static string TestEnd()
        {
            CoopTestSession.End();
            return "OK: diagnostic activities ended; ledger and scoped RNG restored; story progress retained";
        }

        private static string Arg(List<string> args, int used, int index)
        {
            if (args == null)
            {
                return null;
            }
            int position = used + index;
            return position >= 0 && position < args.Count ? args[position] : null;
        }
    }

    /// <summary>HUD 桥：协作进展显示只改展示，不触发任何活动。</summary>
    public static class CoopHudBridge
    {
        public static bool ForceVisible;
        public static bool Hidden;
        public static string LastProgress = string.Empty;
    }

    /// <summary>当前场景诊断的单次注入与独立账本；绝不碰正常剧情RollOverride。</summary>
    public static class CoopTestSession
    {
        private static CoopLedger _original;
        private static CoopDirector _director;
        private static Random _random;
        private static float? _first;
        private static float? _second;
        private static bool _allSuccess;
        private static string _activityId;
#if DEBUG
        private static string _preparedActorId;
        private static TaleWorlds.CampaignSystem.Hero _preparedHero;
#endif
        private static readonly HashSet<string> _existing = new HashSet<string>(StringComparer.Ordinal);
        public static bool Begin(string caseId, int seed, out string reason)
        {
            reason = null;
            var director = CoopDirector.Current;
            if (_original != null) { reason = "diagnostic_already_running"; return false; }
            if (director == null) { reason = "no_session"; return false; }
            foreach (var activity in director.Activities)
                if (activity.IsActive) { reason = "stop_active_activities_before_diagnostic"; return false; }
            _existing.Clear();
            foreach (var activity in director.Activities) _existing.Add(activity.Id);
            _director = director;
            _original = director.SwapLedger(CoopLedgerData.From(director.Ledger).ToLedger());
            _random = new Random(seed);
            _first = caseId == "T02" ? 0f : 1f;
            _second = caseId == "T04" ? 0f : (float?)null;
            _allSuccess = caseId == "T03";
            CoopLog.Event("test.begin").Value("case", caseId).Raw("seed", seed).Write();
            return true;
        }
        public static void Bind(string activityId) { _activityId = activityId; }
        /// <summary>只在Debug诊断副本账本中准备搭话，不写工具任务／街头事件或正式招募状态。</summary>
        public static bool PrepareTemporary(string actorId, out string reason)
        {
            reason = null;
#if !DEBUG
            reason = "quick_requires_debug";
            return false;
#else
            if (_original == null || _director != CoopDirector.Current || _activityId != null)
            { reason = "no_unbound_diagnostic"; return false; }
            if (string.IsNullOrEmpty(actorId) || actorId != _director.Content?.Recruitment.Candidate)
            { reason = "not_diagnostic_candidate"; return false; }
            var hero = StoryProbeMissionBehavior.Current?.GetHero(actorId);
            if (hero == null) { reason = "hero_missing"; return false; }
            _preparedActorId = actorId;
            _preparedHero = hero;
            if (!CoopRecruitment.TrySetTemporary(actorId, "talk", CoopMissionBehavior.Current?.SimTime ?? 0,
                out reason, diagnostic: true))
            { _preparedActorId = null; _preparedHero = null; return false; }
            CoopLog.Event("test.prepared").Actor(actorId).Value("purpose", "talk")
                .Detail("diagnostic prerequisites only; quest/event/reward/recruitment/gate state unchanged").Write();
            return true;
#endif
        }
        internal static bool AllowsPreparedActor(string actorId, CoopActivity activity)
        {
#if !DEBUG
            return false;
#else
            return _original != null && _director == CoopDirector.Current && _preparedHero != null
                && actorId == _preparedActorId
                && StoryProbeMissionBehavior.Current?.GetHero(actorId) == _preparedHero
                && (activity == null || (_activityId == null || activity.Id == _activityId));
#endif
        }
        internal static bool AllowsParticipantPreparation(string actorId)
        {
#if !DEBUG
            return false;
#else
            return _activityId == null && AllowsPreparedActor(actorId, null);
#endif
        }
        public static bool Owns(string activityId) => _original != null && _activityId == activityId;
        public static float NextRoll(string activityId)
        {
            if (_original == null || _activityId != activityId) return TaleWorlds.Core.MBRandom.RandomFloat;
            if (_allSuccess)
            {
                CoopLog.Event("test.injection_consumed").Activity(activityId).Raw("roll", 1f).Write();
                return 1f;
            }
            if (_first.HasValue)
            {
                var value = _first.Value;
                _first = null;
                CoopLog.Event("test.injection_consumed").Activity(activityId).Raw("roll", value).Write();
                return value;
            }
            if (_second.HasValue)
            {
                var value = _second.Value;
                _second = null;
                CoopLog.Event("test.injection_consumed").Activity(activityId).Raw("roll", value).Write();
                return value;
            }
            return (float)_random.NextDouble();
        }
        public static void End()
        {
            if (_original == null) return;
            foreach (var activity in new List<CoopActivity>(_director.Activities))
                if (!_existing.Contains(activity.Id) && activity.IsActive)
                    _director.StopActivity(activity, "test_end", CoopMissionBehavior.Current?.SimTime ?? 0);
            _director.SwapLedger(_original);
            _original = null;
            _director = null;
            _activityId = null;
#if DEBUG
            _preparedActorId = null;
            _preparedHero = null;
#endif
            _random = null;
            _first = null;
            _second = null;
            _allSuccess = false;
            _existing.Clear();
        }
    }
}

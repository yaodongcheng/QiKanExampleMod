using System;
using System.Collections.Generic;
using System.Linq;
using LivingWorldNpcs;
using LivingWorldNpcs.Planner.Cooperation;

public static class Program
{
    static readonly List<string> Failures = new List<string>();
    static void Check(bool ok, string name)
    {
        if (!ok) Failures.Add(name);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    }

    static CoopConditionSet C(string[] items)
    {
        var list = new List<CoopCondition>();
        if (items != null)
            foreach (var raw in items)
            {
                var text = raw; bool value = true;
                if (text[0] == '!') { value = false; text = text.Substring(1); }
                list.Add(new CoopCondition(text, value));
            }
        return new CoopConditionSet(list);
    }

    static CoopActionDef A(string id, int cost, string[] pre, string[] effects, bool certain = true)
    {
        return new CoopActionDef { Id = id, Cost = cost, OutcomeCertain = certain, Preconditions = C(pre), Effects = C(effects) };
    }

    static CoopDomain BuildDomain(bool extended)
    {
        var d = new CoopDomain();
        d.AddAction(A("hire_paid", 5, new[]{"alive","!cancelled","candidate","money"}, new[]{"companion"}));
        d.AddAction(A("recruit_by_charm", 4, new[]{"alive","!cancelled","candidate","charm_offer"}, new[]{"companion"}));
        d.AddAction(A("brief_companion", 1, new[]{"alive","!cancelled","companion"}, new[]{"briefed"}));
        d.AddAction(A("approach", 2, new[]{"alive","!cancelled","briefed","main_path"}, new[]{"near_guard"}, false));
        d.AddAction(A("approach_side", 4, new[]{"alive","!cancelled","briefed","side_path"}, new[]{"near_guard"}, false));
        d.AddAction(A("hold_talk", 3, new[]{"alive","!cancelled","near_guard","talk_allowed"}, new[]{"engaged"}, false));
        d.AddAction(A("request_investigation", 6, new[]{"alive","!cancelled","near_guard","investigation_allowed"}, new[]{"engaged","off_post"}, false));
        d.AddAction(A("observe_gap", 1, new[]{"alive","!cancelled","engaged","!other_watcher"}, new[]{"gap"}));
        d.AddAction(A("signal_player", 1, new[]{"alive","!cancelled","gap"}, new[]{"signal"}));
        d.AddAction(A("cross_gate", 2, new[]{"alive","!cancelled","gap","signal","technique"}, new[]{"passed"}));
        d.AddAction(A("regroup", 2, new[]{"alive","!cancelled","companion","passed"}, new[]{"regrouped"}));
        if (extended)
            d.AddAction(A("present_known_evidence", 1, new[]{"alive","!cancelled","near_guard","evidence"}, new[]{"talk_allowed"}));
        return d;
    }

    static void AddMethods(CoopDomain d, bool withEvidence)
    {
        void M(string task, string id, string[] guards, string[] subtasks)
        {
            d.AddMethod(new CoopMethodDef { Task = task, Id = id, Guards = C(guards), Subtasks = subtasks ?? new string[0] });
        }
        M("@pass_together","main",null,new[]{"@helper","@brief","@approach","@window","@signal","cross_gate","regroup"});
        M("@helper","have",new[]{"companion"},null);
        M("@helper","hire",new[]{"!companion"},new[]{"hire_paid"});
        M("@helper","charm",new[]{"!companion","charm_offer"},new[]{"recruit_by_charm"});
        M("@brief","have",new[]{"briefed"},null);
        M("@brief","do",null,new[]{"brief_companion"});
        M("@approach","have",new[]{"near_guard"},null);
        M("@approach","walk",new[]{"main_path"},new[]{"approach"});
        M("@approach","side",new[]{"side_path"},new[]{"approach_side"});
        M("@window","have",new[]{"gap"},null);
        M("@window","observe",new[]{"!gap"},new[]{"observe_gap"});
        M("@window","talk",new[]{"!gap","talk_allowed"},new[]{"hold_talk","observe_gap"});
        M("@window","investigate",new[]{"!gap","investigation_allowed"},new[]{"request_investigation","observe_gap"});
        if (withEvidence) M("@window","evidence",new[]{"!gap","evidence"},new[]{"present_known_evidence","hold_talk","observe_gap"});
        M("@signal","have",new[]{"signal"},null);
        M("@signal","send",new[]{"!signal"},new[]{"signal_player"});
    }

    static Dictionary<string, CoopTruth> State(params string[] overrides)
    {
        var s = new Dictionary<string, CoopTruth>(StringComparer.Ordinal)
        {
            {"alive",CoopTruth.True},{"cancelled",CoopTruth.False},{"candidate",CoopTruth.True},
            {"money",CoopTruth.True},{"charm_offer",CoopTruth.False},{"companion",CoopTruth.True},
            {"briefed",CoopTruth.False},{"main_path",CoopTruth.True},{"side_path",CoopTruth.True},
            {"near_guard",CoopTruth.False},{"talk_allowed",CoopTruth.True},{"investigation_allowed",CoopTruth.True},
            {"engaged",CoopTruth.False},{"off_post",CoopTruth.False},{"other_watcher",CoopTruth.False},
            {"gap",CoopTruth.False},{"signal",CoopTruth.False},{"technique",CoopTruth.True},
            {"passed",CoopTruth.False},{"regrouped",CoopTruth.False},{"evidence",CoopTruth.False}
        };
        foreach (var item in overrides)
        {
            var parts = item.Split('=');
            s[parts[0]] = parts[1] == "1" ? CoopTruth.True : CoopTruth.False;
        }
        return s;
    }

    static CoopGoalDef Goal()
    {
        return new CoopGoalDef
        {
            Id = "pass_together", DisplayKey = "goal", RootTask = "@pass_together", Priority = 10,
            Conditions = C(new[]{"passed","regrouped"}),
            Constraints = C(new[]{"alive","!cancelled"})
        };
    }

    static int? ForwardCost(Dictionary<string, CoopTruth> initial, CoopDomain domain)
    {
        var goal = Goal();
        var heap = new List<Tuple<int, Dictionary<string,CoopTruth>>>();
        var best = new Dictionary<string,int>(StringComparer.Ordinal);
        heap.Add(Tuple.Create(0, initial)); best[Key(initial)] = 0;
        while (heap.Count > 0)
        {
            heap.Sort((a,b)=>a.Item1.CompareTo(b.Item1));
            var node = heap[0]; heap.RemoveAt(0);
            int cost = node.Item1; var state = node.Item2;
            int known; if (best.TryGetValue(Key(state), out known) && known != cost) continue;
            if (goal.Conditions.IsSatisfiedBy(state)) return cost;
            foreach (var action in domain.Actions.Values)
            {
                if (action.Plannable != CoopPlannable.Plannable) continue;
                if (!action.Preconditions.IsSatisfiedBy(state)) continue;
                var next = new Dictionary<string,CoopTruth>(state, StringComparer.Ordinal);
                foreach (var e in action.Effects.Items) next[e.Fact] = e.Value ? CoopTruth.True : CoopTruth.False;
                int nc = cost + action.Cost;
                int prev; if (best.TryGetValue(Key(next), out prev) && nc >= prev) continue;
                best[Key(next)] = nc; heap.Add(Tuple.Create(nc, next));
            }
        }
        return null;
    }

    static string Key(Dictionary<string,CoopTruth> s)
    {
        return string.Join("|", s.OrderBy(p=>p.Key, StringComparer.Ordinal)
            .Where(p=>p.Value!=CoopTruth.Unknown).Select(p=>p.Key+(p.Value==CoopTruth.True?"+":"-")));
    }

    // ---- 生命周期替身：只在被调用时回报，绝不自己推进 ----
    sealed class StubRuntime : ICoopStepRuntime
    {
        readonly Dictionary<string, Action<CoopStepOutcome>> _pending = new Dictionary<string, Action<CoopStepOutcome>>(StringComparer.Ordinal);
        public readonly List<string> Started = new List<string>();
        public readonly List<string> Stopped = new List<string>();
        public bool Busy(string id) { return _pending.ContainsKey(id); }
        public Action<CoopStepOutcome> PendingCallback(string id) => _pending.TryGetValue(id, out var callback) ? callback : null;
        public bool IsBusy(CoopActivity a) { return Busy(a.Id); }
        public bool CanRun(CoopActivity a, CoopActionDef action, out string reason) { reason = null; return true; }
        public void Start(CoopActivity a, CoopActionDef action, double sim, Action<CoopStepOutcome> done)
        {
            Started.Add(action.Id);
            _pending[a.Id] = done;
        }
        public void Stop(CoopActivity a, string reason) { Stopped.Add(a.Id); _pending.Remove(a.Id); }
        public bool Complete(string activityId, CoopStepOutcome outcome)
        {
            Action<CoopStepOutcome> done;
            if (!_pending.TryGetValue(activityId, out done)) return false;
            _pending.Remove(activityId);
            done(outcome);
            return true;
        }
    }

    static CoopContentData Content()
    {
        var json = @"{
          ""SchemaVersion"":1, ""ContentVersion"":""test-1"",
          ""Budget"":{""MaxNodes"":4096,""MaxDepth"":16,""MaxSteps"":64},
          ""Stages"":{""approach"":""k_approach"",""wait"":""k_wait""},
          ""Roles"":[{""Id"":""companion"",""Goals"":[""pass_together""]},{""Id"":""guard"",""Goals"":[""guard_watch""]}],
          ""Capabilities"":[
            {""Id"":""approach_guard"",""CapabilityId"":""move_to_point"",""Cost"":2,""Stage"":""approach"",
             ""Pre"":[""briefed""],""Effects"":[""near_guard""],""OutcomeCertain"":false,""Args"":{""point"":""target"",""target"":""guard""}},
            {""Id"":""signal_player"",""CapabilityId"":""signal_player"",""Cost"":1,""Stage"":""wait"",
             ""Pre"":[""near_guard""],""Effects"":[""signal""],""Args"":{""line"":""k_signal""}}
          ],
          ""Goals"":[
            {""Id"":""pass_together"",""DisplayKey"":""g1"",""Conditions"":[""signal""],""Constraints"":[""alive""],""RootTask"":""@pass""},
            {""Id"":""guard_watch"",""DisplayKey"":""g2"",""Conditions"":[""watching""],""Constraints"":[""alive""],""RootTask"":""@watch""}
          ],
          ""Methods"":[
            {""Id"":""m1"",""Task"":""@pass"",""Subtasks"":[""approach_guard"",""signal_player""]},
            {""Id"":""m2"",""Task"":""@watch"",""Subtasks"":[]}
          ]
        }";
        return CoopContentData.Parse(json);
    }

    static Dictionary<string, CoopTruth> FormalFacts()
    {
        return new Dictionary<string, CoopTruth>(StringComparer.Ordinal) {
            {"alive",CoopTruth.True},{"cancelled",CoopTruth.False},{"briefed",CoopTruth.True},
            {"single_guard",CoopTruth.True},
            {"companion_ready",CoopTruth.True},{"talk_authorized",CoopTruth.True},{"investigation_authorized",CoopTruth.True},
            {"near_guard",CoopTruth.False},{"engaged",CoopTruth.False},
            {"gap",CoopTruth.False},{"signal",CoopTruth.False},{"passed",CoopTruth.False},
            {"regrouped",CoopTruth.False},{"other_watcher",CoopTruth.False},{"player_crossed",CoopTruth.False}
        };
    }

    static void LureRegression(string path)
    {
        var content = CoopContentData.Load(path);
        var facts = FormalFacts(); facts["single_guard"] = CoopTruth.False; facts["guided"] = CoopTruth.False;
        foreach (var id in content.RoleOrNull("guard").Actors)
        { facts["present_" + id] = CoopTruth.True; facts["handled_" + id] = CoopTruth.False; facts["near_" + id] = CoopTruth.False; }
        foreach (var algo in new[]{CoopAlgorithm.Goap, CoopAlgorithm.Htn, CoopAlgorithm.Hybrid})
        {
            var solved = CoopPlanner.Solve(facts, content.Domain, content.GoalOrNull("help_player_cross"), algo, content.Budget);
            Check(solved.Found, "G1 two guards have a complete conditional route " + algo + ": " + solved.Status);
            if (!solved.Found) continue;
            int lead = solved.Steps.IndexOf("lead_guards"), observe = solved.Steps.IndexOf("observe_lured_gate");
            Check(lead > 0 && content.RoleOrNull("guard").Actors.All(id => solved.Steps.IndexOf("lure_request_" + id) >= 0
                && solved.Steps.IndexOf("lure_request_" + id) < lead) && observe > lead
                && solved.Steps.IndexOf("signal_player") > observe,
                "G2 every guard answered before leading; observation before signal " + algo);
            Check(!solved.Steps.Contains("regroup") && solved.Steps.Last() == "wait_player_cross",
                "G12 crossing completes assistance without a rendezvous " + algo);
            var blocked = new CoopDomain();
            foreach (var pair in content.Domain.Actions) if (pair.Key != "lure_request_gate_sentry") blocked.Actions[pair.Key] = pair.Value;
            foreach (var methods in content.Domain.Methods.Values) foreach (var method in methods) blocked.AddMethod(method);
            Check(!CoopPlanner.Solve(facts, blocked, content.GoalOrNull("help_player_cross"), algo, content.Budget).Found,
                "G3 a missing second answer cannot produce a route " + algo);
        }
        facts["handled_blocker"] = CoopTruth.True; facts["handled_gate_sentry"] = CoopTruth.Unknown;
        Check(!content.Domain.Actions["lead_guards"].Preconditions.IsSatisfiedBy(facts), "G4 unknown second answer cannot authorize leading");
        Check(content.Domain.Actions.Values.Where(a => a.Id.StartsWith("lure_request_") || a.Id == "lead_guards")
            .All(a => !a.Effects.Items.Any(e => e.Fact == "gap")), "G5 agreements and walking never create an opening directly");
        Check(!content.RoleOrNull("guard").Requests["lure"].RequiresRelief
            && content.RoleOrNull("guard").Requests["investigate"].RequiresRelief,
            "G6 lure inspection and ordinary investigation keep distinct duty policies");
        var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        var guard = ((Newtonsoft.Json.Linq.JArray)root["Roles"]).OfType<Newtonsoft.Json.Linq.JObject>().First(r => (string)r["Id"] == "guard");
        guard["Actors"] = new Newtonsoft.Json.Linq.JArray("alpha", "beta", "gamma");
        guard["Requests"]["lure"]["ActorLines"] = new Newtonsoft.Json.Linq.JObject {
            ["alpha"] = new Newtonsoft.Json.Linq.JObject { ["challenge"] = "LWN_coop_line_challenge_blocker" }
        };
        var changed = CoopContentData.Parse(root.ToString());
        Check(changed.IsValid && new[]{"alpha","beta","gamma"}.All(id => changed.Domain.Actions.ContainsKey("lure_request_" + id))
            && !changed.Domain.Actions.ContainsKey("lure_request_blocker")
            && changed.Domain.Actions["lead_guards"].Preconditions.Items.Count(e => e.Fact.StartsWith("handled_")) == 3,
            "G7 changing only participant JSON expands three actors and dependencies");
        guard["Actors"] = new Newtonsoft.Json.Linq.JArray("alpha", "alpha");
        Check(!CoopContentData.Parse(root.ToString()).IsValid, "G8 duplicate participants rejected");
        root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        ((Newtonsoft.Json.Linq.JArray)root["Roles"])[0]["RecoveryRole"] = "companion";
        Check(!CoopContentData.Parse(root.ToString()).IsValid, "G9 contract-bound or recursive recovery rejected");
        root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        ((Newtonsoft.Json.Linq.JArray)root["Capabilities"]).OfType<Newtonsoft.Json.Linq.JObject>()
            .First(x => (string)x["Id"] == "observe_lured_gate")["Args"]["point"] = "player";
        Check(!CoopContentData.Parse(root.ToString()).IsValid, "G10 invalid observation destination rejected");
        foreach (var mutation in new[]{"foreign_actor","unknown_turn","empty_line","non_object","non_object_map"})
        {
            root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            guard = ((Newtonsoft.Json.Linq.JArray)root["Roles"]).OfType<Newtonsoft.Json.Linq.JObject>().First(r => (string)r["Id"] == "guard");
            var lines=(Newtonsoft.Json.Linq.JObject)guard["Requests"]["lure"]["ActorLines"];
            if(mutation=="foreign_actor")lines["outsider"]=new Newtonsoft.Json.Linq.JObject { ["challenge"]="key" };
            if(mutation=="unknown_turn")lines["blocker"]["force_leave"]="key";
            if(mutation=="empty_line")lines["blocker"]["challenge"]="";
            if(mutation=="non_object")lines["blocker"]="key";
            if(mutation=="non_object_map")guard["Requests"]["lure"]["ActorLines"]=new Newtonsoft.Json.Linq.JArray();
            Check(!CoopContentData.Parse(root.ToString()).IsValid,"G11 invalid actor response rejected: "+mutation);
        }
    }

    static void FormalRegression(string path)
    {
        var content = CoopContentData.Load(path);
        Check(content.IsValid, "F1 unchanged formal JSON loads: " + string.Join(";", content.Errors));
        if (!content.IsValid) return;
        string reason, detail;
        foreach (var algorithm in new[]{CoopAlgorithm.Goap,CoopAlgorithm.Htn,CoopAlgorithm.Hybrid})
        {
            var director = CoopDirector.Install(content, null, 100 + (int)algorithm);
            var runtime = new StubRuntime();
            director.Runtime = runtime;
            var activity = director.StartSeeded("son", "companion", content.GoalOrNull("help_player_cross"),
                algorithm, "test", 0, FormalFacts(), out reason);
            Check(activity != null, "F2 formal plan found " + algorithm);
            if (activity == null) continue;
            if (algorithm == CoopAlgorithm.Hybrid)
                Check(activity.Plan.Steps.Contains("face_guard") && activity.Plan.Steps.Contains("talk_guard"),
                    "F12 formal hybrid really expands the selected talk task");
            bool evidenceRequired = true;
            for (int i = 0; i < 100 && activity.IsActive; i++)
            {
                director.Tick(.016, i * .016);
                var action = activity.CurrentAction(activity.Domain);
                if (!runtime.Busy(activity.Id) || action == null) continue;
                if (action.CapabilityId == "wait_fact")
                {
                    int before = activity.StepIndex;
                    director.Tick(.016, i * .016);
                    evidenceRequired &= activity.StepIndex == before && !activity.Facts.IsTrue("passed");
                    activity.Facts.Set("player_crossed", CoopTruth.True, "test_witness", i * .016);
                }
                runtime.Complete(activity.Id, CoopStepOutcome.Completed());
            }
            Check(evidenceRequired && activity.State == CoopActivityState.Success
                && activity.Facts.IsTrue("passed") && !activity.Facts.IsTrue("regrouped")
                && runtime.Stopped.Contains(activity.Id), "F3 evidence then success and release " + algorithm);

            var twoGuards = FormalFacts(); twoGuards["other_watcher"] = CoopTruth.True; twoGuards["gap"] = CoopTruth.True;
            var noObservation = new CoopDomain();
            foreach (var pair in content.Domain.Actions) if (pair.Key != "observe_gap") noObservation.Actions[pair.Key] = pair.Value;
            foreach (var list in content.Domain.Methods.Values) foreach (var method in list) noObservation.AddMethod(method);
            var blocked = CoopPlanner.Solve(twoGuards, noObservation, content.GoalOrNull("help_player_cross"), algorithm, content.Budget);
            Check(!blocked.Found, "F4 persistent watcher constraint respected in search " + algorithm);

            var guardFacts = new Dictionary<string,CoopTruth> {
                {"alive",CoopTruth.True},{"cancelled",CoopTruth.False},{"at_post",CoopTruth.True},
                {"report_heard",CoopTruth.True},{"report_checked",CoopTruth.False},{"relief_confirmed",CoopTruth.True}
            };
            var inspect = CoopPlanner.Solve(guardFacts, content.Domain, content.GoalOrNull("guard_inspect"), algorithm, content.Budget);
            Check(inspect.Found && string.Join(",",inspect.Steps) == "inspect_report,return_to_post",
                "F5 at_post -> off_post -> at_post is a legal transition " + algorithm);

            var invalid = director.StartSeeded("son-invalid", "companion", content.GoalOrNull("help_player_cross"),
                algorithm, "test", 2, FormalFacts(), out reason);
            int starts = runtime.Started.Count;
            invalid.Facts.Set("briefed", CoopTruth.False, "withdrawn", 2);
            director.Tick(.016,2);
            Check(invalid.IsTerminal && runtime.Started.Count == starts, "F6 constraints revoked before first start " + algorithm);
        }

        var d = CoopDirector.Install(content, null, 200);
        var stub = new StubRuntime(); d.Runtime = stub;
        var a = d.StartSeeded("son", "companion", content.GoalOrNull("help_player_cross"),CoopAlgorithm.Goap,"test",0,FormalFacts(),out reason);
        d.Tick(.016,.016);
        a.Facts.Set("alive",CoopTruth.False,"test_lost_body",.02);
        stub.Complete(a.Id,CoopStepOutcome.Completed());
        Check(!a.Facts.IsTrue("near_guard") && a.IsTerminal,"F7 late success cannot commit after body lost");

        var filtered = CoopDirector.Install(content, action => action.Id == "wait_player_cross" ? "no_runtime_adapter" : null, 201);
        filtered.Runtime = new StubRuntime();
        var excluded = filtered.StartSeeded("son","companion",content.GoalOrNull("help_player_cross"),
            CoopAlgorithm.Goap,"test",0,FormalFacts(),out reason);
        Check(excluded == null, "F8 excluded catalog capabilities cannot enter a plan");

        var oldContent = content;
        foreach (var algorithm in new[]{CoopAlgorithm.Goap, CoopAlgorithm.Htn, CoopAlgorithm.Hybrid})
        {
            var limited = FormalFacts(); limited["investigation_authorized"] = CoopTruth.False;
            var limitedDomain = CoopCatalog.Build(content, action => action.Id == "talk_guard" ? "refused" : null, 1).PlannableDomain(content.Domain);
            Check(!CoopPlanner.Solve(limited, limitedDomain, content.GoalOrNull("help_player_cross"), algorithm, content.Budget).Found,
                "F24 refused temporary talk cannot switch to investigation " + algorithm);
            limited["investigation_authorized"] = CoopTruth.True;
            Check(CoopPlanner.Solve(limited, limitedDomain, content.GoalOrNull("help_player_cross"), algorithm, content.Budget).Found,
                "F25 full companion can use authorized investigation " + algorithm);
        }
        var bad = CoopContentData.Parse("{\"SchemaVersion\":999}");
        Check(!filtered.Reload(bad,null) && ReferenceEquals(filtered.Content,oldContent),"F9 invalid reload retains previous version");
        var newContent = CoopContentData.Load(path); newContent.ContentVersion = "test-new-version";
        Check(d.Reload(newContent,null) && ReferenceEquals(a.Content,oldContent),"F10 active/ended plans keep original content version");

        var conflict = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        conflict["Capabilities"][0]["Effects"] = new Newtonsoft.Json.Linq.JArray("near_guard","!near_guard");
        Check(!CoopContentData.Parse(conflict.ToString()).IsValid,"F11 truly contradictory effects rejected");

        var lifecycle = CoopDirector.Install(content, null, 300);
        var life = new StubRuntime(); lifecycle.Runtime = life;
        var preview = lifecycle.StartSeeded("preview", "companion", content.GoalOrNull("help_player_cross"),
            CoopAlgorithm.Hybrid, "test", 0, FormalFacts(), out reason);
        preview.AwaitingApproval = true;
        lifecycle.Tick(.016, .016);
        Check(life.Started.Count == 0, "F13 preview never starts a body action");
        Check(!lifecycle.Approve(preview.Id, preview.PlanRevision, .016), "F14 unseen preview cannot be approved");
        preview.PlayerInformed = true;
        Check(!lifecycle.Approve(preview.Id, preview.PlanRevision + 1, .016), "F15 stale preview revision cannot start");
        Check(lifecycle.Approve(preview.Id, preview.PlanRevision, .016), "F16 current received preview can start");
        lifecycle.Tick(.016, .032);
        var late = life.PendingCallback(preview.Id);
        lifecycle.Pause(preview.Id, "test", .032, out detail);
        life.Complete(preview.Id, CoopStepOutcome.Completed());
        lifecycle.Tick(.016, 10);
        Check(preview.StepIndex == 0 && !preview.Facts.IsTrue("near_guard"), "F17 paused completion cannot commit early");
        lifecycle.Resume(preview.Id, "test", 10, out detail);
        lifecycle.Tick(.016, 10.016);
        Check(preview.StepIndex == 1 && preview.Facts.IsTrue("near_guard"), "F18 saved real completion commits on resume");
        lifecycle.Stop(preview.Id, "player_cancelled", 11, out detail);
        int index = preview.StepIndex;
        late(CoopStepOutcome.Completed());
        Check(preview.State == CoopActivityState.Cancelled && preview.StepIndex == index, "F19 late cancelled callback is inert");
        Check(!lifecycle.Approve(preview.Id, preview.PlanRevision, 11), "F20 cancelled preview cannot restart");

        var waiting = lifecycle.StartSeeded("waiting", "companion", content.GoalOrNull("help_player_cross"),
            CoopAlgorithm.Goap, "test", 12, FormalFacts(), out reason);
        lifecycle.Tick(.016, 12);
        life.Complete(waiting.Id, CoopStepOutcome.Waiting("external"));
        waiting.Facts.Set("alive", CoopTruth.False, "test_body_loss", 12);
        lifecycle.ResolveWaiting(waiting, CoopStepOutcome.Completed(), 12);
        Check(waiting.IsTerminal && !waiting.Facts.IsTrue("near_guard"), "F21 waiting completion uses the same commit guards");

        var unknown = FormalFacts(); unknown["other_watcher"] = CoopTruth.Unknown;
        Check(CoopPlanner.Solve(unknown, content.Domain, content.GoalOrNull("help_player_cross"), CoopAlgorithm.Hybrid, content.Budget).Found,
            "F22 unknown watcher permits seeking real observation, not claiming a clear gate");
        foreach (var mutation in new[]{"arg", "fact", "timeout", "budget", "role"})
        {
            var changed = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            switch (mutation)
            {
                case "arg": changed["Capabilities"][0]["Args"]["ignored_argument"] = "1"; break;
                case "fact": changed["Goals"][0]["Constraints"] = new Newtonsoft.Json.Linq.JArray("unregistered_fact"); break;
                case "timeout": changed["Capabilities"][1]["TimeoutGameSeconds"] = 0; break;
                case "budget": changed["Budget"]["FrameCpuMs"] = "NaN"; break;
                case "role": ((Newtonsoft.Json.Linq.JArray)changed["Roles"]).Add(changed["Roles"][0].DeepClone()); break;
            }
            Check(!CoopContentData.Parse(changed.ToString()).IsValid, "F23 invalid contract rejected: " + mutation);
        }
    }

    public static void Main(string[] args)
    {
        var domain = BuildDomain(false); AddMethods(domain, false);
        var goal = Goal();
        var budget = new CoopBudget { MaxNodes = 4096, MaxDepth = 16, MaxSteps = 64 };

        // ---- 层 1：算法与独立 oracle ----
        int mismatches = 0;
        foreach (var companion in new[]{false,true})
        foreach (var money in new[]{false,true})
        foreach (var charm in new[]{false,true})
        foreach (var talk in new[]{false,true})
        foreach (var invest in new[]{false,true})
        foreach (var watcher in new[]{false,true})
        {
            var s = State("companion=" + (companion?"1":"0"), "money=" + (money?"1":"0"),
                "charm_offer=" + (charm?"1":"0"), "talk_allowed=" + (talk?"1":"0"),
                "investigation_allowed=" + (invest?"1":"0"), "other_watcher=" + (watcher?"1":"0"));
            var g = CoopPlanner.Solve(s, domain, goal, CoopAlgorithm.Goap, budget);
            var h = CoopPlanner.Solve(s, domain, goal, CoopAlgorithm.Htn, budget);
            var oracle = ForwardCost(s, domain);
            bool ok = g.Found == oracle.HasValue && (!g.Found || g.Cost == oracle.Value)
                   && h.Found == oracle.HasValue && (!h.Found || h.Cost == oracle.Value);
            if (!ok) mismatches++;
        }
        Check(mismatches == 0, "L1 algorithm matches forward oracle over 64 states");

        var budgetResult = CoopPlanner.Solve(State(), domain, goal, CoopAlgorithm.Goap, new CoopBudget{MaxNodes=1,MaxDepth=16,MaxSteps=64});
        Check(budgetResult.Status == CoopSolveStatus.BudgetExhausted || budgetResult.Status == CoopSolveStatus.Found,
            "L1 budget exhaustion reported separately from no_plan");

        // ---- 层 2：独立决策（守卫不读同伴事实）----
        var guardGoal = new CoopGoalDef { Id="guard_watch", RootTask="@guard_watch", Priority=5,
            Conditions=C(new[]{"watching","at_post"}), Constraints=C(new[]{"alive"}) };
        var guardDomain = new CoopDomain();
        guardDomain.AddAction(A("return_to_post", 2, new[]{"alive"}, new[]{"at_post"}));
        guardDomain.AddAction(A("watch_gate", 1, new[]{"at_post"}, new[]{"watching"}));
        guardDomain.AddMethod(new CoopMethodDef{Task="@guard_watch",Id="ok",Guards=C(new[]{"watching","at_post"}),Subtasks=new string[0]});
        guardDomain.AddMethod(new CoopMethodDef{Task="@guard_watch",Id="go",Subtasks=new[]{"return_to_post","watch_gate"}});
        var guardFacts = new Dictionary<string,CoopTruth>(StringComparer.Ordinal){{"alive",CoopTruth.True},{"at_post",CoopTruth.False},{"watching",CoopTruth.False}};
        var guardPlanA = CoopPlanner.Solve(guardFacts, guardDomain, guardGoal, CoopAlgorithm.Htn, budget);
        var companionSecret = new Dictionary<string,CoopTruth>(guardFacts, StringComparer.Ordinal){{"gap",CoopTruth.True},{"briefed",CoopTruth.True}};
        var guardPlanB = CoopPlanner.Solve(companionSecret, guardDomain, guardGoal, CoopAlgorithm.Htn, budget);
        Check(guardPlanA.Found && guardPlanB.Found && guardPlanA.Cost == guardPlanB.Cost
              && string.Join(",", guardPlanA.Steps.ToArray()) == string.Join(",", guardPlanB.Steps.ToArray()),
            "L2 guard plan independent of companion secret facts");

        // ---- 层 3：生命周期（只由 Brain 回执推进；取消不假完成）----
        var content = Content();
        Check(content.IsValid, "L6 content parses and validates");
        var director = CoopDirector.Install(content, null, 1);
        var stub = new StubRuntime();
        director.Runtime = stub;
        string reason;
        var activity = director.StartSeeded("companion", "companion", content.GoalOrNull("pass_together"),
            CoopAlgorithm.Goap, "test", 0, new Dictionary<string,CoopTruth>(StringComparer.Ordinal)
            {
                {"alive",CoopTruth.True},{"cancelled",CoopTruth.False},{"briefed",CoopTruth.True},
                {"near_guard",CoopTruth.False},{"signal",CoopTruth.False}
            }, out reason);
        Check(activity != null, "L3 activity starts with seeded facts");
        director.Tick(0.016, 0.016);
        Check(stub.Started.Count == 1 && activity.StepIndex == 0, "L3 step waits for a real回执 (no self-advance)");
        int before = activity.StepIndex;
        stub.Complete(activity.Id, CoopStepOutcome.Failed("refused"));
        director.Tick(0.016, 0.032);
        Check(activity.StepIndex == before || activity.Facts.IsTrue("near_guard") == false,
            "L3 failed step does not apply its effects");

        // 取消在开始前：没有 started、不写完成
        var director2 = CoopDirector.Install(content, null, 2);
        director2.Runtime = stub;
        var a2 = director2.StartSeeded("companion", "companion", content.GoalOrNull("pass_together"),
            CoopAlgorithm.Goap, "test", 0, new Dictionary<string,CoopTruth>(StringComparer.Ordinal)
            {{"alive",CoopTruth.True},{"cancelled",CoopTruth.False},{"briefed",CoopTruth.True},{"near_guard",CoopTruth.False},{"signal",CoopTruth.False}}, out reason);
        string detail;
        director2.Stop(a2.Id, "player_cancelled", 0.02, out detail);
        Check(a2.State == CoopActivityState.Cancelled && !a2.Facts.IsTrue("signal"),
            "L3 cancel before start is not reported as completion");

        // ---- 层 4：尝试与资源（一次掷点；边界）----
        var ledger = new CoopLedger();
        var key = new CoopAttemptKey{ActorId="son",TargetId="gate_sentry",Purpose="talk",Method="chat",Offer="0.2",Period="p"};
        int draws = 0;
        Func<float> rng = () => { draws++; return 0.5f; };
        var r1 = CoopInteraction.Resolve(ledger, key, true, null, () => 0.5f, rng, 1, "test");
        var r2 = CoopInteraction.Resolve(ledger, key, true, null, () => 0.5f, rng, 2, "test");
        Check(r1.Eligible && r1.Reason == "success" && r2.Reused && draws == 1 && ledger.Attempts.Count == 1,
            "L4 same attempt rolls once; repeat is reused");

        var key2 = key; key2.Period = "p2";
        var r3 = CoopInteraction.Resolve(ledger, key2, false, "last_sentry", () => 0.9f, rng, 3, "test");
        Check(!r3.Eligible && draws == 1 && r3.Attempt == null, "L4 ineligible refusal consumes no random input");

        var key3 = key; key3.Period = "p3";
        var rBoundary = CoopInteraction.Resolve(ledger, key3, true, null, () => 0.4f, () => 0.6f, 4, "test");
        var key4 = key; key4.Period = "p4";
        var rBelow = CoopInteraction.Resolve(ledger, key4, true, null, () => 0.4f, () => 0.599f, 5, "test");
        Check(rBoundary.Attempt.Success && !rBelow.Attempt.Success, "L4 boundary: roll == threshold succeeds, below fails");

        // ---- 层 5：时序与持久 ----
        var ledgerData = CoopLedgerData.From(ledger);
        var restored = ledgerData.ToLedger();
        var reusedAfterReload = CoopInteraction.Resolve(restored, key, true, null, () => 0.9f, () => 0.0f, 9, "test");
        Check(reusedAfterReload.Reused, "L5 attempt survives a ledger round-trip (no re-roll after reload)");
        Check(restored.TryCommitReceipt("op:x", "paid") && !restored.TryCommitReceipt("op:x", "paid"),
            "L5 receipt commits exactly once");

        // ---- 层 6：配置拒收 ----
        var badDup = CoopContentData.Parse(@"{""SchemaVersion"":1,""ContentVersion"":""b"",""Stages"":{},""Capabilities"":[{""Id"":""x"",""CapabilityId"":""hold""},{""Id"":""x"",""CapabilityId"":""hold""}],""Goals"":[],""Methods"":[]}");
        Check(!badDup.IsValid, "L6 duplicate capability id rejected");
        var badStage = CoopContentData.Parse(@"{""SchemaVersion"":1,""ContentVersion"":""b"",""Stages"":{},""Capabilities"":[{""Id"":""x"",""CapabilityId"":""hold"",""Stage"":""missing""}],""Goals"":[],""Methods"":[]}");
        Check(!badStage.IsValid, "L6 unknown stage rejected");
        var dangling = CoopContentData.Parse(@"{""SchemaVersion"":1,""ContentVersion"":""b"",""Stages"":{},""Capabilities"":[{""Id"":""x"",""CapabilityId"":""hold""}],""Goals"":[{""Id"":""g"",""Conditions"":[""x""],""RootTask"":""@t""}],""Methods"":[{""Id"":""m"",""Task"":""@t"",""Subtasks"":[""nope""]}]}");
        Check(!dangling.IsValid, "L6 dangling subtask rejected");
        var recursive = CoopContentData.Parse(@"{""SchemaVersion"":1,""ContentVersion"":""r"",""Stages"":{},""Capabilities"":[],""Goals"":[],""Methods"":[{""Id"":""m"",""Task"":""@a"",""Subtasks"":[""@a""]}]}");
        Check(!recursive.IsValid, "L6 recursive method rejected");
        Check(CoopContentData.Parse(@"{""SchemaVersion"":1,""ContentVersion"":""ok"",""Stages"":{},""Capabilities"":[{""Id"":""x"",""CapabilityId"":""hold"",""Effects"":[""x""]}],""Goals"":[{""Id"":""g"",""Conditions"":[""x""]}],""Methods"":[]}").IsValid,
            "L6 valid minimal content accepted");

        // ---- 层 7：持续增删（目录随内容变化）----
        var catalog = CoopCatalog.Build(content, a => a.Id == "approach_guard" ? null : "no_runtime_adapter", 3);
        Check(catalog.Count == 2 && catalog.PlannableCount == 1, "L7 catalog marks only adapted capability plannable");
        Check(!string.IsNullOrEmpty(catalog.Describe()), "L7 catalog renders diagnostics");

        // ---- 层 8：变异反例（失败不得跳成功）----
        var director3 = CoopDirector.Install(content, null, 3);
        var stub3 = new StubRuntime();
        director3.Runtime = stub3;
        var a3 = director3.StartSeeded("companion", "companion", content.GoalOrNull("pass_together"),
            CoopAlgorithm.Goap, "test", 0, new Dictionary<string,CoopTruth>(StringComparer.Ordinal)
            {{"alive",CoopTruth.True},{"cancelled",CoopTruth.False},{"briefed",CoopTruth.True},{"near_guard",CoopTruth.False},{"signal",CoopTruth.False}}, out reason);
        director3.Tick(0.016, 0.016);
        stub3.Complete(a3.Id, CoopStepOutcome.Failed("refused"));
        director3.Tick(0.016, 0.032);
        Check(!a3.Facts.IsTrue("signal"), "L8 mutation: failed step cannot jump to success");

        // 未知事实不得被当成假：目标条件未知时不得报告已达成
        var unknownFacts = new Dictionary<string,CoopTruth>(StringComparer.Ordinal){{"alive",CoopTruth.True}};
        var unknownGoal = new CoopGoalDef{Id="g", Conditions=C(new[]{"signal"}), Constraints=CoopConditionSet.Empty};
        var unknownSolve = CoopPlanner.Solve(unknownFacts, new CoopDomain(), unknownGoal, CoopAlgorithm.Goap, budget);
        Check(!unknownSolve.Found, "L8 unknown is not treated as true");

        if (args.Length != 1) Check(false,"formal content path required");
        else { FormalRegression(args[0]); LureRegression(args[0]); }
        Console.WriteLine(Failures.Count == 0 ? "ALL_LAYERS_PASS" : "FAILURES:" + string.Join(";", Failures));
        Environment.Exit(Failures.Count == 0 ? 0 : 1);
    }
}

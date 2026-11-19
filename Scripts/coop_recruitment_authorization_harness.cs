using System;
using System.Collections.Generic;
using System.Linq;
using Ledger = LivingWorldNpcs.Planner.Cooperation.CoopLedger;

namespace LivingWorldNpcs.Planner.Cooperation
{
    // Native identity and scene state are test doubles; authorization methods are extracted from the actual source.
    public enum CoopContractMode { None, Temporary, Companion }
    public class Hero
    {
        public static Hero MainHero = new Hero { StringId = "player" };
        public string StringId = "hero";
        public object CharacterObject = new object();
        public bool IsPlayerCompanion, IsAlive = true, IsPrisoner;
        public Agent Body = new Agent();
        public Clan CompanionOf;
        public MobileParty PartyBelongedTo;
    }
    public class Clan { public static Clan PlayerClan = new Clan(); }
    public class MobileParty
    {
        public static MobileParty MainParty = new MobileParty();
        public HashSet<object> MemberRoster = new HashSet<object>();
    }
    public class CoopLedger
    {
        private readonly Dictionary<string, string> _relations = new Dictionary<string, string>();
        public string GetRelation(string hero, string key) => _relations.TryGetValue(hero + ":" + key, out var value) ? value : null;
        public void SetRelation(string hero, string key, string value) => _relations[hero + ":" + key] = value;
        public Ledger Copy()
        {
            var copy = new Ledger();
            foreach (var relation in _relations) copy._relations.Add(relation.Key, relation.Value);
            return copy;
        }
    }
    public class CoopLedgerData
    {
        private Ledger _copy;
        public static CoopLedgerData From(Ledger ledger) => new CoopLedgerData { _copy = ledger.Copy() };
        public Ledger ToLedger() => _copy;
    }
    public class CoopRecruitmentConfig
    {
        public string Candidate = "actor";
        public int PriceGold = 30;
        public int FavorCost = 5;
        public string FavorSourceKey = "LWN_use", FavorSourceFallback = "Gate assistance";
        public bool RequireQuestTurnedIn, RequireEventSettled;
    }
    public class NPCProfile { public int DebtOfGraditude; public NPCProfile(Hero hero) { } }
    public class SingNpcMemorySystem
    {
        public readonly NPCProfile _profile;
        private readonly object _lock = new object();
        public SingNpcMemorySystem(NPCProfile profile) { _profile = profile; }
        /* ACTUAL_FAVOR_MEMORY */
    }
    public static class AllNpcMemoryManager
    {
        private static readonly Dictionary<string,SingNpcMemorySystem> _memories = new Dictionary<string,SingNpcMemorySystem>();
        public static SingNpcMemorySystem GetMemory(string id)
        {
            if(!_memories.TryGetValue(id,out var memory))
            {
                memory=new SingNpcMemorySystem(new NPCProfile(null));
                memory.TryChangeFavor(Hero.MainHero.StringId,"fixture",5,"LWN_fixture","Fixture",0,0,out _);
                _memories[id]=memory;
            }
            return memory;
        }
    }
    public static class AgentControlHelper
    {
        public static bool ChangePlayerFavor(SingNpcMemorySystem memory,int amount,string operation,string key,string fallback,bool notify=true)
            => memory.TryChangeFavor(Hero.MainHero.StringId,operation,amount,key,fallback,0,0,out _);
    }
    public enum CoopTruth { Unknown, False, True }
    public class Constraints { public bool IsSatisfiedBy(IDictionary<string, CoopTruth> facts) => true; }
    public class CoopGoalDef { public string Id = "enter"; public int Priority; public Constraints Constraints = new Constraints(); }
    public class CoopRoleDef
    {
        public string Id = "companion", ParticipantRole = "guards", Algorithm = "hybrid";
        public bool RequiresContract = true;
        public List<string> Actors = new List<string>(), GoalIds = new List<string> { "enter" };
    }
    public class CoopContentData
    {
        public CoopRecruitmentConfig Recruitment = new CoopRecruitmentConfig();
        public bool IsValid = true;
        public List<CoopRoleDef> Roles = new List<CoopRoleDef> {
            new CoopRoleDef(), new CoopRoleDef { Id = "guards", Actors = new List<string> { "guard1", "guard2" } }
        };
        public CoopRoleDef RoleOrNull(string id) => Roles.Find(role => role.Id == id);
        public CoopGoalDef GoalOrNull(string id) => id == "enter" ? new CoopGoalDef() : null;
    }
    public class CoopDirector
    {
        public static CoopDirector Current = new CoopDirector();
        public string RunId = "scene";
        public Ledger Ledger = new Ledger();
        public CoopContentData Content = new CoopContentData();
        public List<CoopActivity> Activities = new List<CoopActivity>();
        public int Stopped;
        public Dictionary<string, CoopTruth> LastSeed;
        public CoopActivity StartSeeded(string actor, string role, CoopGoalDef goal, CoopAlgorithm algorithm,
            string source, double time, IDictionary<string, CoopTruth> facts, out string reason)
        {
            reason = null;
            LastSeed = new Dictionary<string, CoopTruth>(facts);
            var activity = new CoopActivity { Id = Activities.Count == 0 ? "command-quick" : "command-" + Activities.Count, IsActive = true, Algorithm = algorithm };
            Activities.Add(activity);
            return activity;
        }
        public Ledger SwapLedger(Ledger next) { var old = Ledger; Ledger = next; return old; }
        public void StopActivity(CoopActivity activity, string reason, double time) { activity.IsActive = false; Stopped++; }
        public void StopAll(string reason, double time)
        { foreach (var activity in Activities) if (activity.IsActive) StopActivity(activity, reason, time); }
        public static void Uninstall(int epoch) { Current = null; }
    }
    public enum CoopAlgorithm { Goap, Htn, Hybrid }
    public class CoopActivity { public string Id; public bool FavorCommitted, IsActive, AwaitingApproval; public CoopAlgorithm Algorithm; }
    public class Agent
    {
        public static Agent Main = new Agent();
        public bool Available = true, Visible, HasDutyPoint = true, HasBrain = true;
        public bool IsActive() => Available;
    }
    public static class AgentAIController
    {
        public static Agent GetBrainForAgent(Agent agent) => agent?.HasBrain == true ? agent : null;
    }
    public class MissionLogic
    {
        public int CreatedCalls, InitializedCalls, StartedCalls, RemovedCalls;
        public virtual void OnCreated() { CreatedCalls++; }
        public virtual void OnBehaviorInitialize() { InitializedCalls++; }
        public virtual void AfterStart() { StartedCalls++; }
        public virtual void OnRemoveBehavior() { RemovedCalls++; }
    }
    public class CoopMissionBehavior : MissionLogic
    {
        public static CoopMissionBehavior Current { get; private set; }
        public double SimTime => _simTime;
        private double _simTime = 0;
        private bool _installed;
        public int InstallCalls;
        public bool PublishedBeforeInstall;
        public CoopMissionRuntime Runtime = new CoopMissionRuntime();
        // Content installation and native execution remain doubles; lifecycle and command bodies are actual source.
        private void Install()
        {
            if (_installed) return;
            _installed = true; InstallCalls++;
            PublishedBeforeInstall = ReferenceEquals(Current, this);
        }
        public string DefaultActorId() => CoopDirector.Current?.Content.Recruitment.Candidate;
        /* ACTUAL_STARTUP_METHODS */
        /* ACTUAL_HOST_LIFECYCLE */
    }
    public static class DebugLogger { public static void Log(string message) { } }
    public static class CoopRequestRouter { public static void Reset() { } }
    public static class CoopView { public static void Reset() { } }
    public static class CoopCommands
    {
        public static string Begin(string fixture, string algorithm, string seed, string preparation)
            => TestBegin(fixture, algorithm, seed, preparation);
        /* ACTUAL_TEST_BEGIN */
    }
    public class CoopMissionRuntime
    {
        public static int SightCalls;
        public Agent ResolveAgent(string id) => StoryProbeMissionBehavior.Current.GetAgent(id);
        public static bool CanSeeWithin(Agent observer, Agent target, float radius)
        { SightCalls++; return observer?.Available == true && target?.Available == true && target.Visible; }
        public static bool BodyAvailable(Agent agent, out string reason)
        { reason = agent?.Available == true ? null : "body_unavailable"; return reason == null; }
    }
    public static class CoopLog
    {
        public class Row
        {
            public Row Actor(string value) => this;
            public Row Operation(string value) => this;
            public Row Activity(string value) => this;
            public Row ContractMode(string value) => this;
            public Row Value(string key, string value) => this;
            public Row Raw(string key, object value) => this;
            public Row SimTime(double value) => this;
            public Row Detail(string value) => this;
            public void Write() { }
        }
        public static Row Event(string value) => new Row();
    }
    public class StoryProbeMissionBehavior
    {
        public static StoryProbeMissionBehavior Current = new StoryProbeMissionBehavior();
        public Hero Hero;
        public Dictionary<string, Agent> Participants = new Dictionary<string, Agent> {
            { "guard1", new Agent() }, { "guard2", new Agent() }
        };
        public Hero GetHero(string id) => id == "actor" ? Hero : null;
        public Agent GetAgent(string id) => GetHero(id)?.Body ?? (Participants.TryGetValue(id, out var agent) ? agent : null);
    }
    public class Event
    {
        public string ActorA, ActorB, HostNpcId;
        public List<string> Members = new List<string>();
        public int Index;
    }
    public static class StoryProbeDefs { public static List<Event> Events = new List<Event>(); }
    public static class StoryProbeState
    {
        public static bool Settled = true, QuestTurnedIn = true;
        public static bool IsStreetEventResolved(int index) => Settled;
        public static bool IsQuestTurnedIn(string id) => QuestTurnedIn;
    }
    public static class CoopRecruitment
    {
        private const string ModeKey = "contract.mode", FavorKey = "contract.favor";
        public static CoopRecruitmentConfig Config => CoopDirector.Current?.Content?.Recruitment;
        /* ACTUAL_AUTHORIZATION_METHODS */
    }
    /* ACTUAL_DIAGNOSTIC_SESSION */
    public static class Program
    {
        private static int _checks;
        private static void Check(bool value, string label)
        {
            _checks++;
            if (!value) throw new Exception(label);
        }
        public static void Main()
        {
            CheckHostLifecycle();
            var hero = new Hero { IsPlayerCompanion = true, CompanionOf = Clan.PlayerClan, PartyBelongedTo = MobileParty.MainParty };
            StoryProbeMissionBehavior.Current.Hero = hero;
            MobileParty.MainParty.MemberRoster.Add(hero.CharacterObject);
            var director = CoopDirector.Current;
            director.Ledger.SetRelation(hero.StringId, "contract.mode", "Companion");
            string reason;
            Check(CoopRecruitment.CanRequestPurpose("actor", null, "investigate", out reason), "actual companion authority");
            hero.IsPlayerCompanion = false; hero.CompanionOf = null;
            Check(!CoopRecruitment.CanCooperate("actor", null, out reason), "stale ledger cannot recreate formal identity");
            director.Ledger.SetRelation(hero.StringId, "contract.mode", "Temporary");
            director.Ledger.SetRelation(hero.StringId, "contract.scene", "old");
            director.Ledger.SetRelation(hero.StringId, "contract.purpose", "talk");
            Check(!CoopRecruitment.CanCooperate("actor", null, out reason), "old scene agreement rejected");
            director.Ledger.SetRelation(hero.StringId, "contract.scene", director.RunId);
            Check(CoopRecruitment.CanRequestPurpose("actor", null, "talk", out reason), "agreed talk permitted");
            Check(!CoopRecruitment.CanRequestPurpose("actor", null, "investigate", out reason) && reason == "purpose_not_agreed", "investigation not agreed");
            Check(!CoopRecruitment.CanRequestPurpose("actor", null, "", out reason), "empty purpose rejected");
            Check(!CoopRecruitment.ConsumeFavor(hero,"talk",1),"consumption requires real activity identity");
            Check(AllNpcMemoryManager.GetMemory(hero.StringId).FavorBalance(Hero.MainHero.StringId)==5,
                "eligibility preview retains all five points");
            Check(CoopRecruitment.ConsumeFavor(hero,"talk",1,"first.attempt"),"real recruitment consumption uses numerical memory");
            Check(!CoopRecruitment.CanCooperate("actor", null, out reason), "used favor cannot start new activity");
            var activity = new CoopActivity { FavorCommitted = true };
            Check(CoopRecruitment.CanRequestPurpose("actor", activity, "talk", out reason), "started activity may finish agreed work");
            Check(!CoopRecruitment.CanRequestPurpose("actor", activity, "investigate", out reason), "committed favor does not widen purpose");
            StoryProbeDefs.Events.Add(new Event { Index = 4, ActorA = "actor" }); StoryProbeState.Settled = false;
            Check(!CoopRecruitment.CanCooperate("actor", activity, out reason), "unsettled event rejected");
            StoryProbeState.Settled = true; hero.IsPrisoner = true;
            Check(!CoopRecruitment.CanCooperate("actor", activity, out reason), "prisoner rejected");
            hero.IsPrisoner = false; hero.IsPlayerCompanion = true; hero.CompanionOf = Clan.PlayerClan;
            MobileParty.MainParty.MemberRoster.Clear();
            Check(!CoopRecruitment.CanCooperate("actor", activity, out reason), "formal identity requires main roster");
            MobileParty.MainParty.MemberRoster.Add(hero.CharacterObject); hero.PartyBelongedTo = new MobileParty();
            Check(!CoopRecruitment.CanCooperate("actor", activity, out reason), "elsewhere rejected");
            hero.PartyBelongedTo = MobileParty.MainParty; hero.IsAlive = false;
            Check(!CoopRecruitment.CanCooperate("actor", activity, out reason), "dead actor rejected");

            hero.IsAlive = true; hero.IsPlayerCompanion = false; hero.CompanionOf = null;
            director.Ledger = new Ledger(); director.Activities.Clear();
            var originalLedger = director.Ledger;
            director.Content.Recruitment.RequireQuestTurnedIn = true;
            director.Content.Recruitment.RequireEventSettled = true;
            StoryProbeState.QuestTurnedIn = false; StoryProbeState.Settled = false;
            Check(!CoopRecruitment.Evaluate("actor").Ok, "natural quest prerequisite retained");
            Check(!CoopRecruitment.Evaluate("actor", diagnostic: true).Ok, "diagnostic flag alone cannot bypass prerequisites");
            Check(!CoopTestSession.PrepareTemporary("actor", out reason), "quick preparation requires active diagnostic");
            var old = new CoopActivity { Id = "old", IsActive = false };
            director.Activities.Add(old);
            Check(CoopTestSession.Begin("T01", 32001, out reason), "diagnostic ledger started");
            Check(!ReferenceEquals(director.Ledger, originalLedger), "ledger is isolated copy");
#if DEBUG
            Check(!CoopTestSession.PrepareTemporary("other", out reason), "wrong candidate rejected");
            Check(CoopTestSession.PrepareTemporary("actor", out reason), "quick prepares actual temporary agreement");
            Check(!StoryProbeState.QuestTurnedIn && !StoryProbeState.Settled, "quick never completes quest or event");
            Check(originalLedger.GetRelation(hero.StringId, "contract.mode") == null, "preparation does not write original ledger");
            Check(!CoopRecruitment.Evaluate("actor").Ok, "natural recruitment remains gated inside quick test");
            Check(CoopRecruitment.CanRequestPurpose("actor", null, "talk", out reason), "prepared diagnostic can start talk");
            Check(!CoopRecruitment.CanRequestPurpose("actor", null, "investigate", out reason), "quick cannot authorize investigation");
            var quickActivity = new CoopActivity { Id = "quick", IsActive = true };
            director.Activities.Add(quickActivity); CoopTestSession.Bind(quickActivity.Id);
            Check(!CoopTestSession.PrepareTemporary("actor", out reason), "bound diagnostic cannot prepare again");
            Check(CoopTestSession.AllowsPreparedActor("actor", quickActivity), "scope allows bound diagnostic activity");
            Check(!CoopTestSession.AllowsPreparedActor("actor", new CoopActivity { Id = "other" }), "scope rejects other activity");
            Check(!CoopTestSession.AllowsPreparedActor("other", quickActivity), "scope rejects other actor");
            Check(CoopTestSession.NextRoll(quickActivity.Id) == 1f, "first success roll retained");
            Check(CoopTestSession.NextRoll(quickActivity.Id) < 1f, "success injection consumed once");
            Check(CoopRecruitment.ConsumeFavor(hero,"talk",2,quickActivity.Id),"diagnostic consumption uses isolated numerical memory");
            Check(!CoopRecruitment.CanCooperate("actor", null, out reason), "quick does not refresh spent favor");
            Check(AllNpcMemoryManager.GetMemory(hero.StringId).FavorBalance(Hero.MainHero.StringId)==0,
                "quick seed and spend never change real memory balance");
            quickActivity.FavorCommitted = true;
            Check(CoopRecruitment.CanRequestPurpose("actor", quickActivity, "talk", out reason), "committed diagnostic may finish talk");
            Check(!CoopRecruitment.CanRequestPurpose("actor", quickActivity, "investigate", out reason), "committed quick remains talk only");
            hero.IsPrisoner = true;
            Check(!CoopRecruitment.CanCooperate("actor", quickActivity, out reason), "quick retains prisoner rejection");
            hero.IsPrisoner = false;
            var replacementHero = new Hero(); StoryProbeMissionBehavior.Current.Hero = replacementHero;
            Check(!CoopTestSession.AllowsPreparedActor("actor", quickActivity), "scope bound to original real hero");
            StoryProbeMissionBehavior.Current.Hero = hero;
            director.Activities.Add(new CoopActivity { Id = "guard", IsActive = true });
#else
            Check(!CoopTestSession.PrepareTemporary("actor", out reason) && reason == "quick_requires_debug", "Release quick refused");
            Check(!CoopTestSession.AllowsPreparedActor("actor", null), "Release cannot bypass prerequisites");
#endif
            CoopTestSession.End();
            Check(ReferenceEquals(director.Ledger, originalLedger), "end restores exact original ledger");
            Check(!CoopTestSession.AllowsPreparedActor("actor", null), "end clears prerequisite exception");
            Check(CoopRecruitment.GetMode(hero) == CoopContractMode.None, "end removes synthetic agreement");
            Check(!CoopRecruitment.CanCooperate("actor", null, out reason), "normal authorization restored after end");
            Check(!StoryProbeState.QuestTurnedIn && !StoryProbeState.Settled, "cleanup leaves real prerequisites untouched");
#if DEBUG
            Check(director.Stopped == 2 && !old.IsActive, "end stops only new diagnostic activities including guard");
            hero.IsPrisoner = true;
            Check(CoopTestSession.Begin("T02", 32002, out reason), "new diagnostic after cleanup");
            Check(!CoopTestSession.PrepareTemporary("actor", out reason) && reason == "detained", "quick cannot prepare prisoner");
            Check(!CoopTestSession.AllowsPreparedActor("actor", null), "failed preparation clears scoped exception");
            CoopTestSession.End(); hero.IsPrisoner = false;
            hero.Body.Available = false;
            Check(CoopTestSession.Begin("T01", 1, out reason), "body rejection diagnostic begins");
            Check(!CoopTestSession.PrepareTemporary("actor", out reason), "quick cannot prepare unavailable body");
            CoopTestSession.End(); hero.Body.Available = true;
            originalLedger.SetRelation(hero.StringId, "contract.favor", "used:talk");
            Check(CoopTestSession.Begin("T02", 2, out reason), "used favor diagnostic begins");
            Check(CoopTestSession.PrepareTemporary("actor", out reason), "quick can use isolated credit after real favor spent");
            CoopTestSession.End();
            Check(originalLedger.GetRelation(hero.StringId, "contract.favor") == "used:talk", "used original favor unchanged");
            Check(AllNpcMemoryManager.GetMemory(hero.StringId).FavorBalance(Hero.MainHero.StringId)==0,
                "quick ended with real zero balance intact");
            foreach (var caseId in new[]{"T03","T04"})
            {
                Check(CoopTestSession.Begin(caseId,32003,out reason), "two-guard diagnostic begins " + caseId);
                var diagnostic=new CoopActivity {Id="two-guard-"+caseId,IsActive=true};
                director.Activities.Add(diagnostic);CoopTestSession.Bind(diagnostic.Id);
                Check(CoopTestSession.NextRoll(diagnostic.Id)==1f,"first guard injection " + caseId);
                Check(CoopTestSession.NextRoll(diagnostic.Id)==(caseId=="T03"?1f:0f),"second guard injection " + caseId);
                CoopTestSession.End();
                Check(!CoopTestSession.Owns(diagnostic.Id),"injection cleared " + caseId);
            }
#endif
            CheckParticipantStartup();
            Console.WriteLine("PASS: " + _checks + " actual recruitment/diagnostic checks (native identity and scene state are stubs)");
        }

        private static void CheckParticipantStartup()
        {
            var director = CoopDirector.Current;
            var host = StoryProbeMissionBehavior.Current;
            var behavior = CoopMissionBehavior.Current;
            director.Ledger = new Ledger(); director.Activities.Clear();
            host.Hero = new Hero { IsPlayerCompanion = true, CompanionOf = Clan.PlayerClan, PartyBelongedTo = MobileParty.MainParty };
            MobileParty.MainParty.MemberRoster.Add(host.Hero.CharacterObject);
            StoryProbeState.Settled = true; StoryProbeState.QuestTurnedIn = true;
            host.Participants["guard1"] = new Agent(); host.Participants["guard2"] = new Agent();
            string reason;
            Check(behavior.StartCooperation("actor", "companion", out reason, preview: true) == null
                && reason.StartsWith("participants_not_observed"), "ordinary preview retains current observation prerequisite");
            host.Participants["guard1"].Visible = true; host.Participants["guard2"].Visible = true;
            var normal = behavior.StartCooperation("actor", "companion", out reason, preview: true);
            Check(normal?.AwaitingApproval == true && director.LastSeed["present_guard2"] == CoopTruth.True,
                "ordinary observed preview uses actual startup and both participants");
            director.StopAll("test", 0);
            host.Participants["guard1"].Visible = false; host.Participants["guard2"].Visible = false;
#if DEBUG
            Check(behavior.StartPreparedDiagnostic("actor", "companion", out reason, CoopAlgorithm.Hybrid) == null
                && reason == "no_prepared_diagnostic", "participant preparation requires unbound quick session");
            host.Hero.IsPlayerCompanion = false; host.Hero.CompanionOf = null;
            var ledger = director.Ledger;
            int count = director.Activities.Count;
            int sightCalls = CoopMissionRuntime.SightCalls;
            var result = CoopCommands.Begin("T01", "hybrid", "32001", "quick");
            Check(result.StartsWith("OK: T01 started") && CoopMissionRuntime.SightCalls == sightCalls,
                "actual quick command needs no startup sight of either guard");
            Check(director.Activities.Count == count + 1 && director.Activities[count].AwaitingApproval,
                "quick creates exactly one pending approval activity");
            Check(director.LastSeed["present_guard1"] == CoopTruth.True && director.LastSeed["present_guard2"] == CoopTruth.True
                && director.LastSeed["handled_guard1"] == CoopTruth.False && director.LastSeed["handled_guard2"] == CoopTruth.False
                && director.LastSeed["single_guard"] == CoopTruth.False,
                "scene preparation retains both real unhandled guards");
            Check(director.LastSeed["near_guard1"] == CoopTruth.False && director.LastSeed["near_guard2"] == CoopTruth.False
                && director.LastSeed["gap"] == CoopTruth.False && director.LastSeed["signal"] == CoopTruth.False
                && director.LastSeed["passed"] == CoopTruth.False && !director.LastSeed.ContainsKey("regrouped")
                && director.LastSeed["other_watcher"] == CoopTruth.Unknown,
                "quick startup does not grant execution results or gate passage");
            var activityId = director.Activities[count].Id;
            Check(CoopTestSession.NextRoll(activityId) == 1f && CoopTestSession.NextRoll(activityId) < 1f,
                "startup leaves first injected roll unconsumed");
            Check(behavior.StartCooperation("actor", "companion", out reason, preview: true) == null
                && reason.StartsWith("participants_not_observed"), "ordinary entry does not borrow quick scene preparation");
            Check(!CoopTestSession.AllowsParticipantPreparation("actor")
                && behavior.StartPreparedDiagnostic("actor", "companion", out reason, CoopAlgorithm.Hybrid) == null,
                "bound diagnostic cannot prepare a second activity");
            CoopTestSession.End();
            Check(ReferenceEquals(director.Ledger, ledger) && !CoopTestSession.AllowsParticipantPreparation("actor"),
                "cleanup restores ledger and clears participant preparation scope");
            host.Hero.IsPlayerCompanion = true; host.Hero.CompanionOf = Clan.PlayerClan;
            Check(behavior.StartCooperation("actor", "companion", out reason, preview: true) == null
                && reason.StartsWith("participants_not_observed"), "ordinary observation restored after quick ends");
            host.Hero.IsPlayerCompanion = false; host.Hero.CompanionOf = null;
            foreach (var failure in new[] { "missing", "body", "duty", "brain" })
            {
                var guard = new Agent(); host.Participants["guard2"] = guard;
                if (failure == "missing") host.Participants.Remove("guard2");
                if (failure == "body") guard.Available = false;
                if (failure == "duty") guard.HasDutyPoint = false;
                if (failure == "brain") guard.HasBrain = false;
                count = director.Activities.Count;
                result = CoopCommands.Begin("T03", "hybrid", "32003", "quick");
                string expected = failure == "missing" || failure == "body" ? "participant_unavailable:guard2:" : "participant_without_duty:guard2";
                Check(result.StartsWith("NO: fixture could not start: " + expected), "quick refuses actual participant failure " + failure + ": " + result);
                Check(director.Activities.Count == count && ReferenceEquals(director.Ledger, ledger)
                    && !CoopTestSession.AllowsParticipantPreparation("actor"), "failed command cleans up without treating second guard as absent " + failure);
            }
            host.Participants["guard2"] = new Agent();
#else
            Check(!CoopTestSession.AllowsParticipantPreparation("actor"), "Release cannot prepare participant facts");
#endif
        }

        private static void CheckHostLifecycle()
        {
            Check(CoopMissionBehavior.Current == null, "no host before mission attachment");
            var late = new CoopMissionBehavior();
            Check(CoopMissionBehavior.Current == null, "constructor does not publish unattached host");
            // Real module-added sequence: AddMissionBehavior -> OnCreated -> AfterStart; no OnBehaviorInitialize.
            late.OnCreated();
            Check(ReferenceEquals(CoopMissionBehavior.Current, late), "late-added host registered before AfterStart");
            Check(late.CreatedCalls == 1 && late.InitializedCalls == 0 && late.InstallCalls == 0,
                "registration does not install content before actors are ready");
            late.AfterStart();
            Check(late.InstallCalls == 1 && late.PublishedBeforeInstall && late.StartedCalls == 1,
                "late-added host installs after registration without initialize callback");
            late.AfterStart();
            Check(late.InstallCalls == 1, "repeated AfterStart does not reinstall director");
            StoryProbeMissionBehavior.Current.Hero = new Hero();
            CoopDirector.Current.Content.Recruitment.RequireQuestTurnedIn = true;
            CoopDirector.Current.Content.Recruitment.RequireEventSettled = true;
            StoryProbeState.QuestTurnedIn = false; StoryProbeState.Settled = false;
            var ledger = CoopDirector.Current.Ledger;
            string result = CoopCommands.Begin("T01", "hybrid", "32001", "quick");
#if DEBUG
            Check(result.StartsWith("OK: T01 started") && result.Contains("algorithm=Hybrid preparation=quick"),
                "actual first quick command reaches registered late-added host");
            Check(CoopTestSession.Owns("command-quick") && !ReferenceEquals(CoopDirector.Current.Ledger, ledger),
                "first quick command binds diagnostic activity in copied ledger");
            Check(CoopDirector.Current.Activities[CoopDirector.Current.Activities.Count-1].AwaitingApproval,
                "quick command creates preview and waits for Start button");
#else
            Check(result == "NO: cooperation diagnostics require a Debug build", "Release command remains disabled");
            Check(ReferenceEquals(CoopDirector.Current.Ledger, ledger), "Release command does not prepare diagnostic state");
#endif
            var director = CoopDirector.Current;
            late.OnRemoveBehavior();
            Check(CoopMissionBehavior.Current == null && CoopDirector.Current == null && late.RemovedCalls == 1,
                "scene exit clears host and director and invokes base teardown");
            Check(ReferenceEquals(director.Ledger, ledger) && !CoopTestSession.Owns("command-quick"),
                "scene exit restores diagnostic ledger and clears activity binding");
            CoopDirector.Current = new CoopDirector();
            var early = new CoopMissionBehavior();
            early.OnCreated(); early.OnBehaviorInitialize(); early.AfterStart();
            Check(ReferenceEquals(CoopMissionBehavior.Current, early) && early.InstallCalls == 1 && early.InitializedCalls == 1,
                "predeclared mission behavior path still registers and installs once");
            early.OnRemoveBehavior();
            Check(CoopMissionBehavior.Current == null, "predeclared host removed cleanly");
            CoopDirector.Current = new CoopDirector();
            var next = new CoopMissionBehavior(); next.OnCreated(); next.AfterStart();
            Check(ReferenceEquals(CoopMissionBehavior.Current, next) && next.InstallCalls == 1,
                "next scene registers its own host");
            StoryProbeState.QuestTurnedIn = true; StoryProbeState.Settled = true;
        }
    }
}
namespace TaleWorlds.Core { public static class MBRandom { public static float RandomFloat => 0.5f; } }

// Tests link real gate methods and the entire HUD. Native engine boundaries are doubles.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LivingWorldNpcs;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace TaleWorlds.Library
{
    public struct Vec2 { public float x,y; public Vec2(float x,float y){this.x=x;this.y=y;} }
    public struct Vec3
    {
        public float x,y,z; public Vec3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vec3 operator +(Vec3 a,Vec3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vec3 operator *(Vec3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public float Distance(Vec3 b){var d=this-b;return MathF.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z);}
    }
    public class DataSourceProperty:Attribute {}
    public class ViewModel {protected void OnPropertyChanged(string s){} }
    public class InformationMessage {public InformationMessage(string s){} }
    public static class InformationManager {public static int Messages; public static void DisplayMessage(InformationMessage m){Messages++;} }
}
namespace TaleWorlds.MountAndBlade
{
    public class Mission {public static Mission Current=new();}
    public class Agent {public static Agent Main; public Vec3 Position; public bool CrouchMode,Active=true,Visible,KO; public float Health=100;}
}
namespace TaleWorlds.MountAndBlade.View.MissionViews
{
    public class MissionView
    {
        public Mission Mission=Mission.Current;
        public virtual void OnMissionScreenInitialize(){} public virtual void OnMissionTick(float dt){}
        public virtual void OnMissionScreenFinalize(){}
    }
}
namespace TaleWorlds.Engine.GauntletUI {public class GauntletLayer {public void LoadMovie(string name,object vm){} } }
namespace TaleWorlds.MountAndBlade.View.Screens
{public class MissionScreen {public void AddLayer(object l){} public void RemoveLayer(object l){} } }
namespace TaleWorlds.ScreenSystem {public static class ScreenManager {public static object TopScreen=new TaleWorlds.MountAndBlade.View.Screens.MissionScreen();} }
namespace LivingWorldNpcs.Planner.Cooperation
{
    public class CoopLog {public static int Discarded;public static CoopLog Event(string s){if(s=="view.discarded")Discarded++;return new();}public CoopLog Detail(string s)=>this;public CoopLog Reason(string s)=>this;public void Write(){} }
    public class CoopActivity {public bool IsActive=true,PlayerInformed=true;public string Id="activity";}
    public class CoopDirector {public static CoopDirector Current=new();public object Content=new();public List<CoopActivity> Activities=new(){new()};}
    public static class CoopHudBridge {public static bool Hidden;}
    public static class CoopView {public static string KnownProgress(string id)=>"Still holding the guards";}
    public class CoopMissionBehavior {public static CoopMissionBehavior Current=new();public int Crossings;public void NotifyPlayerCrossed(){Crossings++;} }
}
namespace LivingWorldNpcs
{
    public static class DebugLogger {public static void Log(string s){} }
    public static class V {public static TaleWorlds.Engine.GauntletUI.GauntletLayer NewLayer(int order,string name)=>new();}
    public static class LWNTextHelper {public static string ResolveText(string key,string fallback="")=>key;public static string ResolveCompound(string key,params (string,string)[] args)=>key;}
    public class ScreenGoalTuning {public bool Enabled=true;public int CardWidth=380,SideMargin=24,TopMargin=18;public string Position="top-right";public float PollSec=.25f,FlashSec=2.2f;}
    public class GateTuning {public float HalfWidthMeters=3,ClimbHalfWidthMeters=8,ClimbHeightMeters=2,CrossingMaxStepMeters=6,GroundToleranceMeters=1.5f,RecognizeDistanceMeters=12,PastMeters=1,GoalRadiusMeters=3;}
    public class StoryProbeTuning {public static StoryProbeTuning Current=new();public GateTuning Gate=new();public ScreenGoalTuning ScreenGoal=new();}
    public enum StoryProbePhase {FirstAttempt,Second,Done}
    public enum StoryAbility {Guile,Charm,Force}
    public class StoryData {public bool ApproachResolved,GoalReached,CancellationPending;public string ChosenApproach;}
    public static class StoryProbeState {public static StoryData Data=new();public static StoryProbePhase Phase=StoryProbePhase.Second;public static string Goal="tool";public static void SetGoal(string g){Goal=g;}public static bool HasClue(string s)=>false;public static bool IsQuestAccepted(string s)=>false;public static bool IsQuestTurnedIn(string s)=>false;public static void AddHazard(string s){} }
    public class NpcDef {public string Id,QuestLogKey,QuestTargetNpcId;public bool FirstMeetGuard=true;}
    public static class StoryProbeDefs
    {
        public static List<NpcDef> Npcs=new(){new(){Id="blocker"},new(){Id="gate_sentry"}};
        public static IEnumerable<NpcDef> Representatives=>Array.Empty<NpcDef>();
        public static int Rolls;public static bool Pass=true;
        public class Roll {public bool Success;public string PlayerMessage()=>"roll";}
        public static Roll RollCheck(string actor,StoryAbility ability){Rolls++;return new(){Success=Pass};}
        public static string AbilityDisplayKey(StoryAbility a)=>a.ToString();
    }
    public class AgentBrain {public bool IsPerforming,HasDutyPoint=true,IsStunned,IsStatusHeld;public static bool IsRoped(Agent a)=>false;public static bool IsKnockedOut(Agent a)=>a.KO;}
    public static class AgentAIController {public static Dictionary<Agent,AgentBrain> Brains=new();public static AgentBrain GetBrainForAgent(Agent a)=>a!=null&&Brains.TryGetValue(a,out var b)?b:null;}
    public static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a?.Active==true;}
    public static class NpcSightSystem {public static bool CanAgentSeeTarget(Agent a,Agent b,float distance)=>a?.Visible==true;}
    public class GrappleLogic {public static GrappleLogic Current;public Agent BoundTarget,CarriedTarget;}
    public enum SpeechPriority {Warning}
    public static class PrologueQuestLauncher {public static int Cancels;public static bool Success=true;public static bool CancelAll(){Cancels++;return Success;} }
    public static class PrologueJournalQuest {public static int Ticks;public static void Tick(){Ticks++;} }
    public class StoryProbeMissionBehavior
    {
        public static StoryProbeMissionBehavior Current;
        public Mission Mission=Mission.Current;
        public StoryProbePhase Phase=StoryProbePhase.Second;
        public bool Active=true,FirstGuardFightPending,FirstGuardDefeated,Restore=true;
        public int Restores,Stops;
        private bool _goalReached,_gateGeometryReady=true,_crossingDoor,_crossingWall,_crossedGate,_gateOpen,_playerCrossedNotified,_blockerWasDisarmed;
        private float _verticalGateSpeed,_time=10,_belongingsRestoreRetryAt;
        private Vec3 _gateCenter=new(0,0,0),_lastGatePosition=new(0,-1,0);
        public Vec2 Forward=new(0,1);public Vec3 GoalPos=new(0,8,0);
        public readonly Dictionary<string,Agent> Agents=new(){["blocker"]=new(),["gate_sentry"]=new()};
        public StoryProbeMissionBehavior(){Current=this;foreach(var a in Agents.Values)AgentAIController.Brains[a]=new(){IsPerforming=true};}
        public Agent GetAgent(string id)=>Agents.GetValueOrDefault(id);
        private bool IsBlockerWatching(Agent player)=>IsGateObserved(player);
        private bool IsReallyDisarmed(Agent a)=>false;
        private void LogGateEvaluation(Agent a,string kind){}
        private void SayOnAgent(Agent a,string key,string fallback,SpeechPriority priority){}
        private void EnforceGateBarrier(Agent player,float dt){} // native contact/physical barrier boundary
        private void OpenGate(string approach,string key,string fallback){_gateOpen=true;}
        private void ResolveApproach(string approach,string faction,int delta,bool crime,string hazard,string actor=null){StoryProbeState.Data.ApproachResolved=true;StoryProbeState.Data.ChosenApproach=approach;}
        private bool RestorePlayerBelongings(){Restores++;return Restore;}
        public void CancelRuntime(string reason){Active=false;Stops++;}
        public string PracticeObjectiveKey()=>null;public static string NpcName(string id)=>id;
        public void At(float x,float y,float z=0,bool crouch=true){Agent.Main.Position=new(x,y,z);Agent.Main.CrouchMode=crouch;TickPlayerGate(Agent.Main,.2f);}
        public void AdvanceTime(float dt){_time+=dt;}
        public void OpenOutside(){_gateOpen=true;StoryProbeState.Data.ApproachResolved=true;}
        /* PRODUCTION_GATE_METHODS */
    }
    /* PRODUCTION_GOAL_TEXT */
}
public static class Program
{
    private static int _checks;
    private static void Check(bool valid,string message){_checks++;if(!valid)throw new Exception(message);}
    private static StoryProbeMissionBehavior New()
    {
        Mission.Current=new();Agent.Main=new(){Position=new(0,-1,0),CrouchMode=true};
        StoryProbeState.Data=new();StoryProbeState.Phase=StoryProbePhase.Second;StoryProbeState.Goal="tool";
        StoryProbeDefs.Rolls=0;StoryProbeDefs.Pass=true;PrologueQuestLauncher.Cancels=0;PrologueQuestLauncher.Success=true;
        PrologueJournalQuest.Ticks=0;InformationManager.Messages=0;
        LivingWorldNpcs.Planner.Cooperation.CoopMissionBehavior.Current=new();
        StoryGoalHudMissionView.ForceVisible=null;return new();
    }
    private static StoryGoalHudVM Vm(StoryGoalHudMissionView hud)=>(StoryGoalHudVM)typeof(StoryGoalHudMissionView).GetField("_vm",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(hud);
    private static void Denied(Action<StoryProbeMissionBehavior> setup,float x,float y,float z,bool crouch,int rolls,string label)
    {
        var host=New();setup(host);host.At(x,y,z,crouch);
        Check(!host.PlayerHasCrossed&&!host.GoalCompleted&&!StoryProbeState.Data.ApproachResolved,label);
        Check(StoryProbeDefs.Rolls==rolls,label+" expected roll count");
    }
    public static void Main()
    {
        var performanceHost=New();performanceHost.At(0,.5f);
        Check(StoryProbeState.Data.ApproachResolved&&StoryProbeDefs.Rolls==1,"Performance must not suppress a legal crouched crossing");
        Denied(h=>{},0,-.1f,0,true,0,"not across gate");
        Denied(h=>{},0,.5f,0,false,0,"standing crossing cannot claim stealth");
        Denied(h=>h.GetAgent("blocker").Visible=true,0,.5f,0,true,0,"first watcher still sees player");
        Denied(h=>h.GetAgent("gate_sentry").Visible=true,0,.5f,0,true,0,"second watcher still sees player while performing");
        Denied(h=>StoryProbeDefs.Pass=false,0,.5f,0,true,1,"failed Guile cannot complete");
        Denied(h=>h.At(4,-1),4,.5f,0,true,0,"outside finite door width");
        Denied(h=>{},0,8,0,true,0,"teleport-size jump cannot complete");
        Denied(h=>{},0,.5f,2,true,0,"wrong ground level cannot claim stealth");
        var host=New();host.OpenOutside();Check(!host.PlayerHasCrossed,"opening a route outside is not a witnessed crossing");
        host=New();host.At(0,.5f);
        Check(StoryProbeState.Data.ApproachResolved&&StoryProbeDefs.Rolls==1,"Performance must not suppress a legal crouched crossing");
        Check(!host.PlayerHasCrossed&&!host.GoalCompleted,"door line alone is not safe inside");
        host.At(0,1.5f);Check(host.PlayerHasCrossed,"actual line plus inside distance confirms crossing");
        Check(LivingWorldNpcs.Planner.Cooperation.CoopMissionBehavior.Current.Crossings==1,"witness notification emitted once");
        host.At(0,3);Check(!host.GoalCompleted,"completion still requires verified inside goal area");
        var hud=new StoryGoalHudMissionView();hud.OnMissionScreenInitialize();hud.OnMissionTick(.3f);
        Check(Vm(hud).IsVisible&&Vm(hud).ShowProgress,"active cooperation HUD shows known progress");
        host.At(0,6);
        Check(host.GoalCompleted&&!host.Active&&StoryProbeState.Goal=="done","actual completion stops runtime and advances goal");
        Check(host.Restores==1&&PrologueQuestLauncher.Cancels==1&&PrologueJournalQuest.Ticks==1,"completion commits belongings, jobs and journal");
        hud.OnMissionTick(.01f);
        Check(Vm(hud).IsVisible&&Vm(hud).Objective=="LWN_story_goal_done","completed HUD stays visible and refreshes immediately");
        Check(Vm(hud).Title=="LWN_story_hud_done_title"&&!Vm(hud).ShowProgress,"completed title clears previous cooperation progress");
        Check(StoryGoalHudMissionView.LastHighlight,"completed objective restarts native highlight");
        Check(LivingWorldNpcs.Planner.Cooperation.CoopLog.Discarded>0,"cleared cooperation progress logs its removal");
        int messages=InformationManager.Messages;host.At(0,6);hud.OnMissionTick(.1f);
        Check(host.Restores==1&&PrologueJournalQuest.Ticks==1&&host.Stops==1&&InformationManager.Messages==messages,"repeated ticks do not repeat completion effects");
        StoryGoalHudMissionView.ForceVisible=false;hud.OnMissionTick(.1f);Check(!Vm(hud).IsVisible,"forced off retained");
        StoryGoalHudMissionView.ForceVisible=null;hud.OnMissionTick(.1f);Check(Vm(hud).IsVisible,"auto restores completed card");
        hud.OnMissionScreenFinalize();Check(!StoryGoalHudMissionView.LastVisible,"scene exit clears HUD diagnostics");
        Mission.Current=new();hud=new();hud.OnMissionScreenInitialize();hud.OnMissionTick(.3f);Check(!Vm(hud).IsVisible,"stale completed host cannot show in another mission");
        host=New();host.Active=false;hud=new();hud.OnMissionScreenInitialize();hud.OnMissionTick(.3f);Check(!Vm(hud).IsVisible,"paused inactive prologue cannot show completion");
        host.Active=true;host.FirstGuardFightPending=true;hud.OnMissionTick(.01f);Check(Vm(hud).Objective=="LWN_story_goal_guard_fight","real fight objective still takes priority");
        host.FirstGuardDefeated=true;hud.OnMissionTick(.01f);Check(Vm(hud).Objective=="LWN_story_goal_guard_defeated","defeat objective still takes priority");
        foreach(bool restoreFailure in new[]{true,false})
        {
            host=New();host.Restore=!restoreFailure;PrologueQuestLauncher.Success=restoreFailure;
            host.At(0,.5f);host.At(0,4);host.At(0,6);
            Check(!host.GoalCompleted&&host.Active&&StoryProbeState.Goal!="done"&&PrologueJournalQuest.Ticks==0,"failed cleanup cannot announce completion");
            host.Restore=true;PrologueQuestLauncher.Success=true;host.AdvanceTime(3);host.At(0,6);
            Check(host.GoalCompleted&&PrologueJournalQuest.Ticks==1,"cleanup failure retries and commits once");
        }
        Console.WriteLine("PASS: "+_checks+" production gate/completion/HUD checks; native engine boundaries are doubles");
    }
}

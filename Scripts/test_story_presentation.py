"""Run real performance leases, instrument presentation and SoundFx against native boundary stubs."""
from pathlib import Path
import copy
import json
import subprocess
from check_story_probe import action_presentation_issues, _game_root
from test_story_feedback import method

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/feedback_presentation_tests'


def main():
    definitions = json.loads((ROOT/'ModuleData/DesignData/StoryOpeningTimeline.json').read_text(encoding='utf-8'))['ActionPresentations']
    assert not action_presentation_issues(definitions, _game_root(str(ROOT)))
    for mutation in ('empty', 'speed', 'unknown', 'instrument', 'sound', 'type'):
        data = copy.deepcopy(definitions)
        definition = data['act_play_celtic_harp_stand']
        if mutation == 'empty': definition.clear()
        if mutation == 'speed': definition['Speed'] = 0
        if mutation == 'unknown': definition['Mystery'] = True
        if mutation == 'instrument': definition['Instrument'] = 'nay'
        if mutation == 'sound': definition['Sounds'] = ['event:/music/musicians/missing/00']
        if mutation == 'type': definition['Sounds'] = 'event:/music/musicians/sturgia/04'
        assert action_presentation_issues(data, _game_root(str(ROOT))), mutation
    atomic = (SRC/'AI/Actions/AtomicAction.cs').read_text(encoding='utf-8-sig')
    start = atomic.index('public sealed class PerformanceAction : IAtomicAction')
    stop = atomic.index('\n    /// <summary>', start)
    move_start = atomic.index('public class MoveToPositionAction : IAtomicAction')
    move_stop = atomic.index('\n    // 2.5', move_start)
    story_move = atomic[atomic.index('public class StoryMoveAction : IAtomicAction'):atomic.rindex('\n}')]
    performance = 'using System;using TaleWorlds.MountAndBlade;using TaleWorlds.Library;\nnamespace LivingWorldNpcs {\n' + atomic[start:stop] + '\n' + atomic[move_start:move_stop] + '\n' + story_move + '\n}'
    stub = r'''
using System;using System.Collections.Generic;using System.Linq;
using LivingWorldNpcs;using TaleWorlds.MountAndBlade;using TaleWorlds.Engine;using TaleWorlds.Library;using SandBox;using SandBox.Objects;
namespace TaleWorlds.Library {
 public struct Vec3 {public float x,y,z;public Vec3(float v){x=v;y=z=0;}public Vec3(float a,float b,float c){x=a;y=b;z=c;}public static Vec3 Zero=>new();public float LengthSquared=>x*x+y*y+z*z;public Vec3 NormalizedCopy()=>this*(1f/(float)Math.Sqrt(LengthSquared));public Vec2 AsVec2=>new(){x=x,y=y};public float Distance(Vec3 b)=>(float)Math.Sqrt((this-b).LengthSquared);public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);public static Vec3 operator +(Vec3 a,Vec3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vec3 operator *(Vec3 a,float f)=>new(a.x*f,a.y*f,a.z*f);}
 public struct Vec2 {public float x,y;public Vec2(float a,float b){x=a;y=b;}public static Vec2 Zero=>new();public float LengthSquared=>x*x+y*y;public void Normalize(){} }
 public struct Frame {public Rotation rotation;public Vec3 origin;}public struct Rotation {public Vec3 f;}
 public class InformationMessage {public InformationMessage(string s){} }public static class InformationManager {public static void DisplayMessage(InformationMessage m){} }
}
namespace TaleWorlds.Engine {
 public class Scene {}
 public class Skeleton {public float Speed=1;public void SetAnimationSpeedAtChannel(int c,float s){Speed=s;} }
 public static class GameEntity {public static HashSet<string> Prefabs=new(){"native_harp"};public static bool PrefabExists(string n)=>Prefabs.Contains(n);}
 public class SoundEvent {
  public static List<SoundEvent> Created=new();public static bool AllowPlay=true;public static int NativeTouches;
  public bool Playing,Valid=true;public int Stops,Releases;public Vec3 Position;
  public static int GetEventIdFromString(string n)=>n.StartsWith("missing")?-1:1;
  public static SoundEvent CreateEvent(int id,Scene s){var e=new SoundEvent();Created.Add(e);return e;}
  public void SetPosition(Vec3 p){NativeTouches++;Position=p;}public bool Play(){Playing=AllowPlay;return AllowPlay;}
  public bool IsPlaying(){NativeTouches++;return Playing;}
  public void Stop(){NativeTouches++;Stops++;Playing=false;Valid=false;}public void Release(){NativeTouches++;Releases++;Playing=false;}
  public Vec3 GetEventMinMaxDistance()=>new(30);
 }
}
namespace TaleWorlds.MountAndBlade {
 public abstract class AgentComponent {protected readonly Agent Agent;protected AgentComponent(Agent a){Agent=a;}public virtual void OnAgentRemoved(){}public virtual void OnComponentRemoved(){} }
 public class Mission {public static Mission Current;public Scene Scene=new();public Frame GetCameraFrame()=>new();}
 public class MBAgentVisuals {public bool Valid=true;public Skeleton Skeleton=new();public Skeleton GetSkeleton()=>Skeleton;public sbyte GetRealBoneIndex(int bone)=>(sbyte)bone;public bool IsValid()=>Valid;}
 public class Agent {public string Name="actor",Action="";public bool Active=true,IsUsingGameObject,CrouchMode;public int Teleports;public float Health=100;public Vec3 Position;public Frame Frame;public void TeleportToPosition(Vec3 p){Teleports++;Position=p;}
  public Mission Mission=Mission.Current;public MBAgentVisuals AgentVisuals=new();public CampaignAgentComponent Component=new();private List<AgentComponent> Extras=new();public T GetComponent<T>() where T:AgentComponent=>Component is T c?c:Extras.OfType<T>().FirstOrDefault();public void AddComponent(AgentComponent c){Extras.Add(c);}
  public List<(sbyte Bone,string Prefab,bool Visible)> RawPrefabs=new();public int NativePrefabTouches;public int FailVisibilityIndex=-1;
  public int AddSynchedPrefabComponentToBone(string prefab,sbyte bone){NativePrefabTouches++;RawPrefabs.Add((bone,prefab,true));return RawPrefabs.Count-1;}
  public bool IsSynchedPrefabComponentVisible(int index){NativePrefabTouches++;return RawPrefabs[index].Visible;}
  public void SetSynchedPrefabComponentVisibility(int index,bool visible){NativePrefabTouches++;if(visible&&index==FailVisibilityIndex)throw new InvalidOperationException("native visibility failure");var p=RawPrefabs[index];RawPrefabs[index]=(p.Bone,p.Prefab,visible);}
  public void SetActionChannel(int channel,ActionIndexCache a,bool ignorePriority=false,float blendInPeriod=0){Action=a.Name;AgentVisuals.Skeleton.Speed=1;}
 }
 public class ActionIndexCache {public string Name;public static ActionIndexCache act_none=new(){Name=""};public static ActionIndexCache Create(string s)=>new(){Name=s};}
 public struct MBActionSet {public bool IsValid=>true;public static MBActionSet GetActionSet(string s)=>new();public static bool CheckActionAnimationClipExists(MBActionSet s,ActionIndexCache a)=>a.Name.StartsWith("act_");}
}
namespace SandBox {
 public class CampaignAgentComponent : AgentComponent {public CampaignAgentComponent():base(null){}public AgentNavigator AgentNavigator=new();}
 public class AgentNavigator {
  public Dictionary<(sbyte,string),bool> Components=new();public int Creations;public bool FailAttach;
  public bool GetPrefabVisibility(sbyte b,string n)=>Components.TryGetValue((b,n),out var v)&&v;
  public void SetPrefabVisibility(sbyte b,string n,bool visible){if(visible&&FailAttach)throw new InvalidOperationException("attach failed");if(!Components.ContainsKey((b,n)))Creations++;Components[(b,n)]=visible;}
 }
}
namespace SandBox.Objects {public class InstrumentData {public string StringId,StandingAction,SittingAction;public List<(int,string)> InstrumentEntities=new();}}
namespace TaleWorlds.ObjectSystem {
 public class MBObjectManager {public static MBObjectManager Instance=new();public List<object> Objects=new();
  public T GetObject<T>(string id) where T:class=>Objects.OfType<T>().FirstOrDefault(o=>(o as InstrumentData)?.StringId==id);
  public T GetObject<T>(Func<T,bool> p) where T:class=>Objects.OfType<T>().FirstOrDefault(p);
 }
}
namespace TaleWorlds.CampaignSystem {
 public class Campaign {public static Campaign Current=new();public ConversationManager ConversationManager=new();}
 public class ConversationManager {public bool IsConversationInProgress;public bool IsConversationAgent(Agent a)=>false;}
}
namespace LivingWorldNpcs {
 public interface IAtomicAction {void OnStart(Agent a);void OnTick(Agent a,float dt);bool IsFinished(Agent a);void OnEnd(Agent a);void RequestInterrupt();string GetNarration(Agent a);}
 // This presentation harness does not exercise follow navigation; the real persistent
 // follow lifecycle is covered by test_cooperation_runtime.py.
 public class FollowAgentAction : MoveToPositionAction {
  public FollowAgentAction(Agent target,bool run,float stopDistance,bool keepFollow,MoveToPositionAction.EndBehavior endBehavior,bool allowTeleport)
   : base(target.Position,Vec2.Zero,run,stopDistance,endBehavior:endBehavior,allowTeleport:allowTeleport){}
 }
 public static class CompanionDetentionBehavior {public static bool IsDetained(Agent a)=>false;}
 public static class V {public static bool NavMesh(Scene s,Vec3 p,out int face){face=0;return true;}}
 public static class MBRandom {public static float RandomFloat=>.5f;}
 public static class StoryProbeTuning {public static Tuning Current=new();public class Tuning {public Events Events=new();}public class Events {public float ActorSpacingMeters=3,ActorApproachMeters=2;}}
 public class StoryEventDef {public string Placement="gather",ActionA="act_talk",ActionB="act_talk";public int Index=1;public string[] Members={"host","guest"};}
 public class EventHarness {public StoryEventDef _activeEvent=new();public StoryProbeMissionBehavior _host=new();public Dictionary<Agent,PerformanceAction> _eventActors=new();public Dictionary<Agent,Vec3> _eventGatherTargets=new();public bool _eventPrepared;public int Plays;
  string[] EventMembers(StoryEventDef e)=>e.Members;bool OwnsBody(Agent a)=>_eventActors.TryGetValue(a,out var p)&&p.CanControl;string ResolveNpcId(Agent a)=>_host.Agents.First(x=>x.Value==a).Key;
  void PlayLoopAction(Agent a,string action){Plays++;if(!_eventActors[a].PlayAction(action))throw new Exception("pose refused");}
  EVENT_PREPARE
  public void Prepare()=>PrepareEventActors();
 }
 public static class DebugLogger {public static void Log(string s){} }
 public static class ConversationEntryPatch {public static bool Active;public static bool IsConversationActive()=>Active;}
 public class AgentBrain {public Agent Owner;public bool IsBound,IsStatusHeld,IsStunned,IsInCombat;public PerformanceAction Lease;
  public static bool IsRoped(Agent a)=>false;public static bool IsHeldByStatus(Agent a)=>false;public static bool IsKnockedOut(Agent a)=>false;
  public bool OwnsPerformance(PerformanceAction p)=>Lease==p;public void PerformanceEnded(PerformanceAction p){if(Lease==p)Lease=null;}
 }
 public class AgentAIController {public static AgentAIController Instance=new();public static Dictionary<Agent,AgentBrain> Brains=new();
  public static AgentBrain GetBrainForAgent(Agent a)=>Brains.TryGetValue(a,out var b)?b:null;
  public void SendEventToAgent(Agent a,string e,params object[] args){
   if(e=="event_performance_begin"){var b=GetBrainForAgent(a);if(b==null){b=new(){Owner=a};Brains[a]=b;}var p=new PerformanceAction(b,(string)args[0],(Action<string>)args[2]);b.Lease=p;p.OnStart(a);((Action<PerformanceAction>)args[1])(p);}
   if(e=="event_performance_end")GetBrainForAgent(a)?.Lease?.Finish((string)args[1]);
  }
 }
 public static class AgentControlHelper {
  public static bool Blocked;public static int Moves,Holds,Unlocks;
  public static string GetPose(Agent a)=>null;public static bool MovePrepare(Agent a)=>true;
  public static void SmartMoveToPoint(Agent a,Vec3 target,bool run,float stopDistance){Moves++;if(!Blocked){var d=target-a.Position;if(d.LengthSquared>.01f)a.Position=a.Position+d.NormalizedCopy()*Math.Min(.3f,a.Position.Distance(target));}}
  public static void MoveEndAndInteractPrepare(Agent a){}public static void FaceToActor(Agent a,Agent b){}
  public static void LookAtAgent(Agent a,Agent b){}
  public static bool SafeIsActive(Agent a)=>a?.Active==true&&a.Mission==Mission.Current;
  public static bool IsPlayingPose(Agent a,string s)=>a.Action==s;
  public static void ForcePlayAction(Agent a,string s,float startProgress,float blendIn,float blendOutPeriodToNoAnim,bool restoreAfter){a.Action=s;a.AgentVisuals.Skeleton.Speed=1;}
  public static void SuspendNavigator(Agent a){}public static void HoldInPlace(Agent a,bool resetScriptedMovement){Holds++;}public static void ForceUnlockAgent(Agent a){Unlocks++;}
  public static void SetBodyDirection(Agent a,Vec2 direction){}
  public class FacingTracker {public void Tick(Agent a,Agent b,bool c,bool d,float dt,string owner){} }
 }
 public static class SpeechChannel {public static Agent GetPerformanceListener(Agent a,PerformanceAction p)=>null;}
 public static class NpcSightSystem {public const int DefaultFovDegrees=120;public static bool IsTargetInViewCone(Agent a,Agent b,bool useBodyFacing)=>true;}
 public class Settings {public static Settings Instance=new();public bool ShowPerformanceFacingDebugMessages;}
 public static class LWNTextHelper {public static string ResolveCompoundMixed(string k,string f,params (string,object)[] p)=>f;public static string ResolveText(string k,string f)=>f;}
 public class StoryProbeMissionBehavior {public bool IsPracticeActor(string id)=>false;public float Clock;public Prep Prep=new();public GroupDirector Director=new();public Dictionary<string,Agent> Agents=new();public Agent GetAgent(string id)=>Agents.TryGetValue(id,out var a)?a:null;}
 public class Prep {public Layout Layout=new();public bool TryGetStandingPosition(string actor,out Vec3 pos){pos=new(0);return true;}}
 public class GroupDirector {public bool IsEventParticipant(string actor)=>false;}
 public static class StoryV2Data {public static string Report="";public static bool EnsurePublished()=>true;}
 public class Standing {public string Actor,Point;}
 public class Point {public string Id,Action="act_harp";public float Facing;public int MaxOccupants=1;public float ArriveTolerance=.6f;}
 public class Layout {public List<Standing> Standing=new(){new(){Actor="worker",Point="work"}};public Point Point=new(){Id="work"};public Point FindPoint(string id)=>Point;}
 public class Stage {public string Layout="layout";}
 public class Plan {public Dictionary<string,Stage> Stages=new(){{"second",new()}};public Dictionary<string,Layout> Layouts=new(){{"layout",new()}};public ActionPresentationDef Definition;public ActionPresentationDef PresentationFor(string a)=>a=="act_harp"?Definition:null;}
 public static class StoryLayoutRegistry {public static Plan Published=new();}
 public class StageBeat {public string Actor,Action;}
 public class StageHarness {public StoryProbeMissionBehavior _host;public Dictionary<string,PerformanceAction> _actors=new();bool OwnsActor(string id)=>_actors.TryGetValue(id,out var p)&&p.CanControl;
 STAGE_PLAY_ACTION
 }
}
class Program {
 static int count;static void Check(bool b,string msg){if(!b)throw new Exception(msg);count++;}
 static ActionPresentationDef Def(string instrument="harp")=>new(){Instrument=instrument,Sounds=new[]{"native_harp_music"},Speed=.625f};
 static (Agent a,AgentBrain b,PerformanceAction p) Setup(){Mission.Current=new();ConversationEntryPatch.Active=false;SoundEvent.Created.Clear();SoundEvent.AllowPlay=true;
  TaleWorlds.ObjectSystem.MBObjectManager.Instance.Objects=new(){new InstrumentData{StringId="harp",StandingAction="act_harp",InstrumentEntities=new(){(7,"native_harp")}}};
  GameEntity.Prefabs=new(){"native_harp"};var a=new Agent();var b=new AgentBrain{Owner=a};var p=new PerformanceAction(b,"test",null);b.Lease=p;p.OnStart(a);return(a,b,p);}
 static void Play(Agent a,PerformanceAction p){Check(p.PlayAction("act_harp",Def()),"valid performance starts");p.OnTick(a,.1f);}
 static void Main(){
 var(a,b,p)=Setup();Play(a,p);var nav=a.Component.AgentNavigator;var sound=SoundEvent.Created.Single();
 Check(nav.GetPrefabVisibility(7,"native_harp"),"native instrument attaches to its registered bone");Check(sound.Playing,"real SoundFx creates and starts sound");Check(a.AgentVisuals.Skeleton.Speed==.625f,"track tempo applies to action");
 a.Position=new(37);p.OnTick(a,.1f);Check(sound.Position.x==37,"sound follows performer instead of player/camera");p.OnTick(a,.1f);Check(SoundEvent.Created.Count==1,"a playing track never stacks copies");
 sound.Playing=false;p.OnTick(a,.1f);Check(sound.Releases==1&&SoundEvent.Created.Count==2,"finished track releases then repeats");
 p.SetPaused(true);Check(!nav.GetPrefabVisibility(7,"native_harp")&&!SoundEvent.Created.Last().Playing,"pause hides harp and stops music");Check(a.AgentVisuals.Skeleton.Speed==1,"pause restores action speed");
 p.SetPaused(false);Play(a,p);Check(nav.Creations==1,"resume reuses cached prefab");p.PlayAction("act_talk");Check(!nav.GetPrefabVisibility(7,"native_harp")&&!SoundEvent.Created.Last().Playing,"changing action cleans its former instrument and music");
 (a,b,p)=Setup();Play(a,p);p.Finish("combat",false);Check(p.Ended&&!a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp")&&!SoundEvent.Created.Last().Playing,"interrupt with releaseBody=false still clears presentation");
 (a,b,p)=Setup();Play(a,p);a.Active=false;p.OnTick(a,.1f);Check(p.Ended&&!a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp")&&!SoundEvent.Created.Last().Playing,"death cleans valid corpse visual and independent sound");
 (a,b,p)=Setup();Play(a,p);b.IsBound=true;p.OnTick(a,.1f);Check(p.Ended&&!SoundEvent.Created.Last().Playing&&!a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp"),"binding interrupts instrument presentation");
 (a,b,p)=Setup();Play(a,p);ConversationEntryPatch.Active=true;p.OnTick(a,.1f);Check(p.Ended&&!SoundEvent.Created.Last().Playing,"conversation stops music");
 (a,b,p)=Setup();Play(a,p);a.Action="act_foreign";a.AgentVisuals.Skeleton.Speed=2;p.OnTick(a,.1f);Check(!SoundEvent.Created.Last().Playing&&!a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp")&&a.AgentVisuals.Skeleton.Speed==2,"foreign pose cleans presentation without changing new owner speed");
 (a,b,p)=Setup();Play(a,p);int before=SoundEvent.NativeTouches;Mission.Current=new();p.InvalidateManaged();Check(SoundEvent.NativeTouches==before,"scene switch drops stale native handles without calling them");
 (a,b,p)=Setup();p.OnStart(a);b.Lease=null;Check(!p.PlayAction("act_harp",Def())&&SoundEvent.Created.Count==0,"no control cannot attach or sound");
 (a,b,p)=Setup();GameEntity.Prefabs.Clear();Check(!p.PlayAction("act_harp",Def())&&a.Action==""&&SoundEvent.Created.Count==0,"missing prefab refuses empty plucking and rock fallback");
 (a,b,p)=Setup();var missing=Def();missing.Sounds=new[]{"missing_track"};Check(!p.PlayAction("act_harp",missing)&&a.Component.AgentNavigator.Creations==0,"missing event fails before attaching anything");
 (a,b,p)=Setup();Play(a,p);Check(p.PlayAction("act_harp",Def("renamed_by_mod")),"registered action match replaces unavailable configured instrument id");p.OnTick(a,.1f);Check(a.Component.AgentNavigator.Creations==1,"instrument fallback preserves correct prefab cache");
 (a,b,p)=Setup();a.Component.AgentNavigator.FailAttach=true;Check(!p.PlayAction("act_harp",Def())&&!SoundEvent.Created.Any(),"partial attach failure rolls back and never starts music");
 (a,b,p)=Setup();SoundEvent.AllowPlay=false;Check(SoundFx.Play3D("refused_play",a.Position)==null&&SoundEvent.Created.Last().Releases==1,"native Play false is reported and its handle released");
 (a,b,p)=Setup();Play(a,p);p.Finish("exit");var end=SoundEvent.Created.Last();int stops=end.Stops;p.Finish("exit twice");Check(end.Stops==stops,"repeated shutdown cannot stop/release twice");
 (a,b,p)=Setup();StoryLayoutRegistry.Published=new(){Definition=Def()};var host=new StoryProbeMissionBehavior();host.Agents["worker"]=a;var work=new StoryWorkDirector(host);
 Check(work.Begin("second"),"actual work director binds workpoint");work.Tick(.1f);work.Tick(.1f);var worker=AgentAIController.GetBrainForAgent(a).Lease;worker.OnTick(a,.1f);
 Check(a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp")&&SoundEvent.Created.Single().Playing,"real work controller consumes action presentation data");
 Check(work.Describe().Contains("presentation=playing attached=1"),"read-only work status exposes presentation state");
 work.YieldActor("worker","group");Check(!a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp")&&!SoundEvent.Created.Last().Playing,"work handoff removes its instrument and music");
 work.Tick(.1f);work.Tick(.1f);AgentAIController.GetBrainForAgent(a).Lease.OnTick(a,.1f);var workingSound=SoundEvent.Created.Last();Check(workingSound.Playing,"work resumes presentation after handoff");
 work.Begin("second");Check(!workingSound.Playing,"restarting work director releases old slots before clearing them");
 (a,b,p)=Setup();StoryLayoutRegistry.Published=new(){Definition=Def()};host=new();host.Agents["worker"]=a;var stage=new StageHarness{_host=host};stage._actors["worker"]=p;
 stage.Run(new(){Actor="worker",Action="act_harp"});p.OnTick(a,.1f);Check(SoundEvent.Created.Single().Playing&&a.Component.AgentNavigator.GetPrefabVisibility(7,"native_harp"),"actual opening actor_action consumes same presentation data");
 // Real independent StoryProbe actors have no CampaignAgentComponent or Navigator.
 (a,b,p)=Setup();a.Component=null;Play(a,p);
 Check(a.RawPrefabs.Single()==((sbyte)7,"native_harp",true)&&SoundEvent.Created.Single().Playing,"independently spawned actor plays harp without a campaign navigator");
 p.SetPaused(true);Check(!a.RawPrefabs.Single().Visible&&!SoundEvent.Created.Last().Playing,"bare actor pause cleans native prefab and music");
 p.SetPaused(false);Play(a,p);Check(a.RawPrefabs.Count==1,"bare actor pause/resume reuses original native prefab");p.Finish("opening finished");
 var secondLease=new PerformanceAction(b,"work-after-opening",null);b.Lease=secondLease;secondLease.OnStart(a);Play(a,secondLease);
 Check(a.RawPrefabs.Count==1&&a.RawPrefabs.Single().Visible,"opening to work handoff reuses actor-held cache across leases");secondLease.Finish("work yield");
 for(int i=0;i<20;i++){var repeated=new PerformanceAction(b,"repeat"+i,null);b.Lease=repeated;repeated.OnStart(a);if(!repeated.PlayAction("act_harp",Def()))throw new Exception("native repeat failed");repeated.OnTick(a,.1f);repeated.Finish("done");}
 Check(a.RawPrefabs.Count==1&&!a.RawPrefabs.Single().Visible&&SoundEvent.Created.All(s=>!s.Playing),"twenty native lease restarts neither stack instruments nor leave music");
 (a,b,p)=Setup();a.Component.AgentNavigator=null;Play(a,p);Check(a.RawPrefabs.Single().Visible,"component present but navigator absent uses native attachment too");p.PlayAction("act_talk");Check(!a.RawPrefabs.Single().Visible&&!SoundEvent.Created.Last().Playing,"bare actor changing pose cleans presentation");
 (a,b,p)=Setup();a.Component=null;Play(a,p);a.Active=false;p.OnTick(a,.1f);Check(p.Ended&&!a.RawPrefabs.Single().Visible&&!SoundEvent.Created.Last().Playing,"bare corpse visual and music are cleaned");
 (a,b,p)=Setup();a.Component=null;Play(a,p);int prefabTouches=a.NativePrefabTouches;before=SoundEvent.NativeTouches;Mission.Current=new();p.InvalidateManaged();Check(a.NativePrefabTouches==prefabTouches&&SoundEvent.NativeTouches==before,"bare actor scene switch never dereferences stale native attachment or sound");
 (a,b,p)=Setup();a.Component=null;GameEntity.Prefabs.Add("native_bow");var instrument=TaleWorlds.ObjectSystem.MBObjectManager.Instance.Objects.OfType<InstrumentData>().Single();instrument.InstrumentEntities.Add((8,"native_bow"));a.FailVisibilityIndex=1;
 Check(!p.PlayAction("act_harp",Def())&&a.RawPrefabs.Count==2&&a.RawPrefabs.All(x=>!x.Visible)&&SoundEvent.Created.Count==0,"second native piece failure rolls back both pieces without music");
 (a,b,p)=Setup();a.Component=null;StoryLayoutRegistry.Published=new(){Definition=Def()};host=new();host.Agents["worker"]=a;stage=new StageHarness{_host=host};stage._actors["worker"]=p;stage.Run(new(){Actor="worker",Action="act_harp"});p.OnTick(a,.1f);
 Check(a.RawPrefabs.Single().Visible&&SoundEvent.Created.Single().Playing,"real opening beat 17 succeeds for a bare independent actor");p.Finish("opening ended");work=new(host);Check(work.Begin("second"),"bare actor work controller starts");work.Tick(.1f);work.Tick(.1f);AgentAIController.GetBrainForAgent(a).Lease.OnTick(a,.1f);
 Check(a.RawPrefabs.Count==1&&a.RawPrefabs.Single().Visible&&SoundEvent.Created.Last().Playing,"real bare actor work resumes same native instrument after opening");
 int rawBefore=a.NativePrefabTouches;var status=work.Describe();Check(status.Contains("presentation=playing attached=1")&&a.NativePrefabTouches==rawBefore,"bare actor work status remains read-only");work.Stop("exit");Check(a.RawPrefabs.All(x=>!x.Visible)&&SoundEvent.Created.All(x=>!x.Playing),"bare actor work exit clears music and instrument");
 Console.WriteLine($"PASS: {count} presentation and actual lease lifecycle checks (native boundaries stubbed)");
 count=0;
 (a,b,p)=Setup();AgentControlHelper.Blocked=false;AgentControlHelper.Moves=0;AgentControlHelper.Holds=0;
 Check(p.BeginWalk(new Vec3(10),.5f,20),"lease accepts walking");Check(!p.PlayAction("act_harp",Def())&&!p.BeginWalk(new Vec3(20),.5f,20),"moving lease refuses pose or second walk");
 int holds=AgentControlHelper.Holds;p.OnTick(a,.3f);Check(a.Position.x>0&&a.Position.x<1&&p.IsWalking&&AgentControlHelper.Holds==holds,"walking advances gradually without stationary hold");
 for(int i=0;i<100&&p.IsWalking;i++)p.OnTick(a,.3f);Check(!p.IsWalking&&p.WalkResult==true&&a.Teleports==0,"actual movement arrives with successful receipt and no teleport");p.Finish("done");
 (a,b,p)=Setup();AgentControlHelper.Blocked=true;p.BeginWalk(new Vec3(10),.5f,20);for(int i=0;i<100&&p.IsWalking;i++)p.OnTick(a,.3f);Check(!p.IsWalking&&p.WalkResult==false&&a.Position.x==0&&a.Teleports==0,"stuck movement fails without teleporting");
 (a,b,p)=Setup();p.BeginWalk(new Vec3(10),.5f,20);p.SetPaused(true);Check(!p.IsWalking&&p.WalkResult==false&&p.Paused,"pause cancels actual walk");p.SetPaused(false);Check(p.CanControl,"unpause keeps valid lease but cannot revive cancelled walk");
 (a,b,p)=Setup();p.BeginWalk(new Vec3(10),.5f,20);int unlocks=AgentControlHelper.Unlocks;Mission.Current=new();p.OnTick(a,.3f);Check(p.Ended&&!p.IsWalking&&p.WalkResult==false&&AgentControlHelper.Unlocks==unlocks,"mission loss only invalidates walk without stale body writes");
 (a,b,p)=Setup();p.BeginWalk(new Vec3(10),.5f,20);b.IsBound=true;unlocks=AgentControlHelper.Unlocks;p.OnTick(a,.3f);Check(p.Ended&&!p.IsWalking&&AgentControlHelper.Unlocks==unlocks,"binding cancels walk without releasing binding");
 (a,b,p)=Setup();var h=new EventHarness();h._host.Agents["host"]=a;h._eventActors[a]=p;var guest=new Agent{Position=new Vec3(10)};var gb=new AgentBrain{Owner=guest};var gp=new PerformanceAction(gb,"guest",null);gb.Lease=gp;gp.OnStart(guest);h._host.Agents["guest"]=guest;h._eventActors[guest]=gp;AgentControlHelper.Blocked=false;
 h.Prepare();Check(gp.IsWalking&&!h._eventPrepared&&h.Plays==0&&guest.Teleports==0,"real event preparation walks remote member and holds dialogue/actions");
 for(int i=0;i<100&&!h._eventPrepared;i++){gp.OnTick(guest,.3f);h.Prepare();}Check(h._eventPrepared&&h.Plays==2&&guest.Teleports==0,"real event preparation starts poses only after arrival");
 gp.Finish("done");p.Finish("done");
 (a,b,p)=Setup();h=new();h._host.Agents["host"]=a;h._eventActors[a]=p;guest=new(){Position=new Vec3(10)};gb=new(){Owner=guest};gp=new(gb,"guest",null);gb.Lease=gp;gp.OnStart(guest);h._host.Agents["guest"]=guest;h._eventActors[guest]=gp;AgentControlHelper.Blocked=true;h.Prepare();for(int i=0;i<100&&gp.IsWalking;i++)gp.OnTick(guest,.3f);
 bool failed=false;try{h.Prepare();}catch(InvalidOperationException){failed=true;}Check(failed&&!h._eventPrepared&&h.Plays==0&&guest.Teleports==0,"real gathering failure cannot start group dialogue or move through wall");gp.Finish("failed");p.Finish("failed");
 (a,b,p)=Setup();h=new();h._activeEvent.Placement="standing";h._activeEvent.Members=new[]{"host"};h._host.Prep.Layout.Standing=new(){new(){Actor="host",Point="work"}};h._host.Agents["host"]=a;h._eventActors[a]=p;h.Prepare();Check(h._eventPrepared&&!p.IsWalking&&a.Teleports==0,"verified standing events remain in authored place without gathering");
 Console.WriteLine($"PASS: {count} actual walking and group arrival checks (native navigation boundary stubbed)");
 }
}
'''
    stage = (SRC/'Interaction/StoryProbe/StoryStageRunner.cs').read_text(encoding='utf-8-sig')
    stub = stub.replace('STAGE_PLAY_ACTION', method(stage, 'PlayAction') + '\npublic void Run(StageBeat b)=>PlayAction(b,0);')
    director = (SRC/'Interaction/StoryProbe/StreetLifeDirector.cs').read_text(encoding='utf-8-sig')
    stub = stub.replace('EVENT_PREPARE', method(director, 'PrepareEventActors'))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'Program.cs').write_text(stub, encoding='utf-8')
    (OUT/'PerformanceAction.cs').write_text(performance, encoding='utf-8')
    (OUT/'Presentation.cs').write_text((SRC/'Animation/AgentActionPresentation.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (OUT/'SoundFx.cs').write_text((SRC/'Combat/SoundFx.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (OUT/'Work.cs').write_text((SRC/'Interaction/StoryProbe/StoryWorkDirector.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (OUT/'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT/'Harness.csproj'), '--verbosity', 'quiet'], check=True)
    # Old code must reproduce today's regression, rather than merely test an always-present Navigator.
    current = (SRC/'Animation/AgentActionPresentation.cs').read_text(encoding='utf-8-sig')
    broken = current.replace('_navigator = _agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;',
                             '_navigator = _agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator; if (_navigator == null) return Fail("agent navigator unavailable");')
    assert broken != current
    (OUT/'Presentation.cs').write_text(broken, encoding='utf-8')
    negative = subprocess.run(['dotnet', 'run', '--project', str(OUT/'Harness.csproj'), '--verbosity', 'quiet'], capture_output=True, text=True)
    (OUT/'Presentation.cs').write_text(current, encoding='utf-8')
    assert negative.returncode != 0 and 'valid performance starts' in negative.stderr, negative.stderr[-1000:]
    print('PASS: requiring a Navigator reproduces the independent-actor regression')
    print('PASS: authoritative Native instrument data and 6 invalid configuration cases')


if __name__ == '__main__':
    main()

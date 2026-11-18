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
    performance = 'using System;using TaleWorlds.MountAndBlade;using TaleWorlds.Library;\nnamespace LivingWorldNpcs {\n' + atomic[start:stop] + '\n}'
    stub = r'''
using System;using System.Collections.Generic;using System.Linq;
using LivingWorldNpcs;using TaleWorlds.MountAndBlade;using TaleWorlds.Engine;using TaleWorlds.Library;using SandBox;using SandBox.Objects;
namespace TaleWorlds.Library {
 public struct Vec3 {public float x,y,z;public Vec3(float v){x=v;y=z=0;}public static Vec3 Zero=>new();public Vec2 AsVec2=>new(){x=x,y=y};public float Distance(Vec3 b)=>Math.Abs(x-b.x);public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.x-b.x);}
 public struct Vec2 {public float x,y;public Vec2(float a,float b){x=a;y=b;}public float LengthSquared=>x*x+y*y;public void Normalize(){} }
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
 public class Mission {public static Mission Current;public Scene Scene=new();public Frame GetCameraFrame()=>new();}
 public class MBAgentVisuals {public bool Valid=true;public Skeleton Skeleton=new();public Skeleton GetSkeleton()=>Skeleton;public sbyte GetRealBoneIndex(int bone)=>(sbyte)bone;public bool IsValid()=>Valid;}
 public class Agent {public string Name="actor",Action="";public bool Active=true;public float Health=100;public Vec3 Position;public Frame Frame;
  public Mission Mission=Mission.Current;public MBAgentVisuals AgentVisuals=new();public CampaignAgentComponent Component=new();public T GetComponent<T>() where T:class=>Component as T;
  public void SetActionChannel(int channel,ActionIndexCache a,bool ignorePriority=false,float blendInPeriod=0){Action=a.Name;AgentVisuals.Skeleton.Speed=1;}
 }
 public class ActionIndexCache {public string Name;public static ActionIndexCache act_none=new(){Name=""};public static ActionIndexCache Create(string s)=>new(){Name=s};}
 public struct MBActionSet {public bool IsValid=>true;public static MBActionSet GetActionSet(string s)=>new();public static bool CheckActionAnimationClipExists(MBActionSet s,ActionIndexCache a)=>a.Name.StartsWith("act_");}
}
namespace SandBox {
 public class CampaignAgentComponent {public AgentNavigator AgentNavigator=new();}
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
 public interface IAtomicAction {}
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
  public static bool SafeIsActive(Agent a)=>a?.Active==true&&a.Mission==Mission.Current;
  public static bool IsPlayingPose(Agent a,string s)=>a.Action==s;
  public static void ForcePlayAction(Agent a,string s,float startProgress,float blendIn,float blendOutPeriodToNoAnim,bool restoreAfter){a.Action=s;a.AgentVisuals.Skeleton.Speed=1;}
  public static void SuspendNavigator(Agent a){}public static void HoldInPlace(Agent a,bool resetScriptedMovement){}public static void ForceUnlockAgent(Agent a){}
  public static void SetBodyDirection(Agent a,Vec2 direction){}
  public class FacingTracker {public void Tick(Agent a,Agent b,bool c,bool d,float dt,string owner){} }
 }
 public static class SpeechChannel {public static Agent GetPerformanceListener(Agent a,PerformanceAction p)=>null;}
 public static class NpcSightSystem {public const int DefaultFovDegrees=120;public static bool IsTargetInViewCone(Agent a,Agent b,bool useBodyFacing)=>true;}
 public class Settings {public static Settings Instance=new();public bool ShowPerformanceFacingDebugMessages;}
 public static class LWNTextHelper {public static string ResolveCompoundMixed(string k,string f,params (string,object)[] p)=>f;public static string ResolveText(string k,string f)=>f;}
 public class StoryProbeMissionBehavior {public float Clock;public Prep Prep=new();public GroupDirector Director=new();public Dictionary<string,Agent> Agents=new();public Agent GetAgent(string id)=>Agents.TryGetValue(id,out var a)?a:null;}
 public class Prep {public bool TryGetStandingPosition(string actor,out Vec3 pos){pos=new(0);return true;}}
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
 Console.WriteLine($"PASS: {count} presentation and actual lease lifecycle checks (native boundaries stubbed)");
 }
}
'''
    stage = (SRC/'Interaction/StoryProbe/StoryStageRunner.cs').read_text(encoding='utf-8-sig')
    stub = stub.replace('STAGE_PLAY_ACTION', method(stage, 'PlayAction') + '\npublic void Run(StageBeat b)=>PlayAction(b,0);')
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'Program.cs').write_text(stub, encoding='utf-8')
    (OUT/'PerformanceAction.cs').write_text(performance, encoding='utf-8')
    (OUT/'Presentation.cs').write_text((SRC/'Animation/AgentActionPresentation.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (OUT/'SoundFx.cs').write_text((SRC/'Combat/SoundFx.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (OUT/'Work.cs').write_text((SRC/'Interaction/StoryProbe/StoryWorkDirector.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (OUT/'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT/'Harness.csproj'), '--verbosity', 'quiet'], check=True)
    print('PASS: authoritative Native instrument data and 6 invalid configuration cases')


if __name__ == '__main__':
    main()

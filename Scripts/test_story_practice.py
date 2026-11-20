"""Run production practice lifecycle against explicit native/UI/Brain test doubles."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape
import json
from copy import deepcopy
from check_story_probe import story_dialogue_issues, story_lesson_issues

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/story_practice_tests'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    code = r'''
using System; using System.Linq; using System.Collections.Generic;
using TaleWorlds.Library; using TaleWorlds.Core; using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem; using LivingWorldNpcs;
namespace TaleWorlds.SaveSystem {public class SaveableFieldAttribute:Attribute{public SaveableFieldAttribute(int id){}}}
namespace TaleWorlds.Localization {public class TextObject{string text;public TextObject(string t){text=t;}public override string ToString()=>text;}}
namespace TaleWorlds.InputSystem {public static class HotKeyManager{public static int GetHotKeyId(string category,int id)=>id;}}
namespace TaleWorlds.Library {
 public struct Vec2 {public float x,y;public Vec2(float x,float y){this.x=x;this.y=y;}}
 public struct Vec3 {public float x,y,z;public Vec3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vec3 Zero=>new();public Vec2 AsVec2=>new(x,y);public float Length=>MathF.Sqrt(x*x+y*y+z*z);public float LengthSquared=>x*x+y*y+z*z;public float Distance(Vec3 b)=>new Vec3(x-b.x,y-b.y,z-b.z).Length;public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);}
 public class InformationMessage {public InformationMessage(string text){}}
 public static class InformationManager {public static void DisplayMessage(InformationMessage m){}}
}
namespace TaleWorlds.Core {
 public enum EquipmentIndex {None=-1,Weapon0,Weapon1,Weapon2,Weapon3}
 public class ItemObject {public string StringId;}
 public struct EquipmentElement {public ItemObject Item;public EquipmentElement(ItemObject item){Item=item;}public bool IsEmpty=>Item==null;public static EquipmentElement Invalid=>new();}
 public class Equipment {EquipmentElement[] slots=new EquipmentElement[4];public EquipmentElement this[EquipmentIndex i]{get=>slots[(int)i];set=>slots[(int)i]=value;}public Equipment Clone(){var e=new Equipment();Array.Copy(slots,e.slots,4);return e;}}
 public static class HyperlinkTexts {public static string GetKeyHyperlinkText(int id)=>"F";}
}
namespace TaleWorlds.CampaignSystem {
 public class CampaignTime {public static CampaignTime Never=new();}
 public class Campaign {public static Campaign Current=new();public QuestManager QuestManager=new();}
 public class QuestManager {public List<QuestBase> Quests=new();}
 public class ItemRoster {public int GetItemNumber(ItemObject item)=>0;}
 public class HeroParty {public ItemRoster ItemRoster=new();}
 public class Hero {public static Hero MainHero=new();public HeroParty PartyBelongedTo=new();}
 public abstract class QuestBase {
 public bool IsFinalized;public string StringId;public bool Succeeded;public QuestBase(string id,object hero,CampaignTime time,int reward){StringId=id;}
 public virtual bool IsSpecialQuest=>true;public virtual bool IsRemainingTimeHidden=>true;public abstract TaleWorlds.Localization.TextObject Title{get;}
 protected abstract void SetDialogs();protected abstract void InitializeQuestOnGameLoad();protected abstract void HourlyTick();protected abstract void RegisterEvents();protected abstract void OnTimedOut();public abstract void OnFailed();protected virtual void OnStartQuest(){}
 public void StartQuest(){Campaign.Current.QuestManager.Quests.Add(this);OnStartQuest();}protected void AddLog(TaleWorlds.Localization.TextObject t){}protected void CompleteQuestWithCancel(){IsFinalized=true;}protected void CompleteQuestWithSuccess(){IsFinalized=true;Succeeded=true;}
 }
}
namespace TaleWorlds.MountAndBlade {
 public class Mission {public static Mission Current=new();public TaleWorlds.Engine.Scene Scene=new();}
 public class Rotation {public Vec3 f=new(1,0,0);}public class Frame {public Rotation rotation=new();}
 public class Agent {public static Agent Main;public bool Active=true,CrouchMode;public bool IsUsingGameObject=>false;public float Health=100;public Vec3 Position,Velocity;public Frame Frame=new();public Equipment SpawnEquipment=new();public void UpdateSpawnEquipmentAndRefreshVisuals(Equipment e){SpawnEquipment=e;}public void UpdateAgentStats(){}}
}
namespace TaleWorlds.Engine {
 public class GameEntity {public string Name;public string GetPrefabName()=>Name;public TaleWorlds.Library.Vec3 GlobalPosition=>new(434,402,74);public TaleWorlds.Library.Vec3 GlobalBoxMin=>new(432,400,74);public TaleWorlds.Library.Vec3 GlobalBoxMax=>new(436,404,80);}
 public class Scene {public void GetEntities(ref List<GameEntity> list){list.Add(new GameEntity{Name="empire_castle_wall_d_l3"});}}
 public struct WorldPosition {public WorldPosition(Scene scene,UIntPtr navMesh,TaleWorlds.Library.Vec3 position,bool hasValidZ){} public UIntPtr GetNavMesh()=>UIntPtr.Zero; public TaleWorlds.Library.Vec3 GetNavMeshVec3()=>new(0,0,0);}
}
namespace LivingWorldNpcs {
 public static class StoryProbeDataFiles {public static string DesignDataFolder=__DATA__;}
 public static class V {public static bool PathExists(TaleWorlds.Engine.WorldPosition a,TaleWorlds.Engine.WorldPosition b)=>true;}
 public static class LWNTextHelper {public static string ResolveText(string key,string fallback)=>key;public static string ResolveCompound(string key,string fallback,params (string,string)[] vars)=>key;}
 public static class DebugLogger {public static void Log(string text){}}
 public static class ConversationEntryPatch {public static bool Active;public static bool IsConversationActive()=>Active;}
 public static class StoryProbeDialogue {public static bool IsDialogueActiveNow => ConversationEntryPatch.Active;}
 public class Noise {public float OrdinaryFootstepRadiusMeters=2,RunSpeedMps=3.2f;}
 public class Tuning {public Noise Noise=new();}public static class StoryProbeTuning {public static Tuning Current=new();}
 public static class StoryGoalHudMissionView {public static bool RequestFlash;}
 public static class StoryProbeState {public static int RuntimeToken;public static HashSet<string> Flags=new();public static bool HasAsked(string a,string f)=>Flags.Contains(a+f);public static void MarkAsked(string a,string f)=>Flags.Add(a+f);}
 public static class StoryProbeDefs {public static Dictionary<string,ItemObject> Items=new();public static object GetNpc(string id)=>id=="son"?new object():null;public static ItemObject FindItem(string id){if(id=="missing-item")return null;if(!Items.ContainsKey(id))Items[id]=new(){StringId=id};return Items[id];}public static bool GiveItem(TaleWorlds.CampaignSystem.Hero hero,string id,int count=1)=>true;}
 public static class NpcSightSystem {public static bool Visible,Cone;public static bool CanAgentSeeTarget(Agent a,Agent b,float r)=>Visible&&a.Position.Distance(b.Position)<=r;public static bool IsTargetInViewCone(Agent a,Agent b,bool useBodyFacing)=>Cone;}
 public static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a?.Active==true;public static string GetPose(Agent a)=>"";public static bool ForceMoveTo(Agent a,Vec3 target,float stop,float seconds,Action<bool> done,Action<MoveToPositionAction> accepted=null){var m=new MoveToPositionAction();AgentAIController.Instance.Moves.Add((a,target,done,m));accepted?.Invoke(m);return true;}public static void StopLooking(Agent a){}public static void SetBodyDirection(Agent a,Vec2 d){a.Frame.rotation.f=new(d.x,d.y,0);}public static void HoldInPlace(Agent a,bool resetScriptedMovement=true){}}
 public class Brain {public bool IsInCombat,IsBound,IsStatusHeld,IsStunned;public bool HasDutyPoint;public Vec3 DutyPoint;public float DutyFacing=-1f;public void SetDutyPoint(Vec3 p,float f=-1f){HasDutyPoint=true;DutyPoint=p;DutyFacing=f;}public void ClearDutyPoint(){HasDutyPoint=false;}public void SuspendDutyReturn(bool b){}}
 public class MoveToPositionAction {public bool Interrupted;public void RequestInterrupt(){Interrupted=true;}}
 public class AgentAIController {public static AgentAIController Instance=new();public static Dictionary<Agent,Brain> Brains=new();public static Brain GetBrainForAgent(Agent a)=>a!=null&&Brains.TryGetValue(a,out var b)?b:null;public List<(Agent a,Vec3 target,Action<bool> done,MoveToPositionAction move)> Moves=new();
 public void SendEventToAgent(Agent a,string ev,Vec3 target,float distance,Action<bool> done,Action<MoveToPositionAction> accepted=null,float seconds=20){var m=new MoveToPositionAction();Moves.Add((a,target,done,m));accepted?.Invoke(m);}
 public void Arrive(int i=0,bool ok=true){var m=Moves[i];if(ok)m.a.Position=m.target;m.done(ok);}}
 public class PerformanceAction {public static bool AutoStart=true;public static PerformanceAction Last;public bool Started,Ended,Paused,AllowSpeechBodyTurn=true,AllowSpeechGaze=true;public bool CanControl=>Started&&!Ended&&!Paused&&!ConversationEntryPatch.Active;
 public static PerformanceAction TryAcquire(Agent a,string token,Action<string> onStopped=null,bool replaceAmbient=false){Last=new(){Started=AutoStart};return Last;}public void Release(string why){Ended=true;}public void InvalidateManaged(string why){Ended=true;}public void Pause(bool value){Paused=value;}}
 public class GrappleLogic {public static Dictionary<Agent,GrappleLogic> Sessions=new();public int WorldHitReceipt,PullFinishedReceipt;public Vec3 LastWorldHit,LastPullAnchor;public bool Released;public bool IsSessionBusy;public static bool ThrowAccepted=true;
 public static GrappleLogic EnsureFor(Agent a){if(!Sessions.ContainsKey(a))Sessions[a]=new();return Sessions[a];}public static GrappleLogic Ensure()=>EnsureFor(Agent.Main);public static GrappleLogic GetSession(Agent a)=>Sessions.GetValueOrDefault(a);public bool BeginNpcThrow(Vec3 aim,bool agent,bool autoPull,out string why){why="fixture";return ThrowAccepted;}public void Release(string why){Released=true;}}
 public static class StealManager {public static bool IsUIOpen;}
 public class Work {public void YieldActor(string actor,string reason){}}
 public class Director {public bool Event;public bool IsEventParticipant(string actor)=>Event;public void YieldActorForDialogue(Agent a){}}
 public enum SpeechPriority {Chat,Warning,Combat}
 public class StoryProbeMissionBehavior {public bool Active=true;public float Clock;public Work Work=new();public Director Director=new();public Agent Teacher;public List<(float delay,Action action)> Scheduled=new();public List<string> Conversations=new();public Agent GetAgent(string actor)=>Teacher;public void SayOnAgent(Agent a,string key,string fallback,Agent listener){}public void SayOnAgentText(Agent a,string text,SpeechPriority p,Agent listener,bool forwardToNearby=false,bool selfTalk=false,bool unbound=false){}public void SchedulePublic(float delay,Action action){Scheduled.Add((delay,action));}public void StartStoryConversation(Agent a,string entryNode=null,string triggerId="player_talk",string dialogueId=null,bool force=false){Conversations.Add(triggerId);}public void OpenGate(string approach,string key,string fallback){}}
}
class Program {
 static int count;static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
 static StoryProbeMissionBehavior h;static StoryPracticeRunner r;
 static void Setup(){Campaign.Current=new();Mission.Current=new();StoryProbeState.Flags.Clear();StoryProbeState.RuntimeToken++;Agent.Main=new(){Position=new(434.58f,399.35f,74.09f),CrouchMode=true};h=new(){Teacher=new(){Position=new(460,413,69.4f)}};AgentAIController.Instance=new();AgentAIController.Brains=new(){{h.Teacher,new()}};GrappleLogic.Sessions.Clear();GrappleLogic.ThrowAccepted=true;PerformanceAction.AutoStart=true;ConversationEntryPatch.Active=false;NpcSightSystem.Cone=NpcSightSystem.Visible=false;StealManager.IsUIOpen=false;r=new(h);}
 static void Start(string id="stealth_dagger"){if(!r.Start(id,out var why))throw new Exception("start failed: "+why);count++;r.Tick(.1f);}
 static void Ready(){AgentAIController.Instance.Arrive();r.Tick(.1f);h.Clock=3;Agent.Main.Position=new(434.58f,399.35f,74.09f);r.Tick(.1f);r.Tick(.1f);}
 static void Main(){
 Setup();ConversationEntryPatch.Active=true;Start();Check(AgentAIController.Instance.Moves.Count==0,"no movement before dialogue closes");ConversationEntryPatch.Active=false;r.Tick(.1f);Check(AgentAIController.Instance.Moves.Count==1,"move requested after dialogue");Check(!r.IsPracticeTarget(h.Teacher),"no practice permission while leading");
 AgentAIController.Instance.Arrive();PerformanceAction.AutoStart=false;r.Tick(.1f);h.Clock=3f;r.Tick(.1f);r.Tick(.1f);Check(!r.IsPracticeTarget(h.Teacher),"queued lease cannot authorize theft");PerformanceAction.Last.Started=true;r.Tick(.1f);h.Clock=3;Agent.Main.Position=new(434.58f,399.35f,74.09f);r.Tick(.1f);Check(r.AllowsTake(h.Teacher,EquipmentIndex.Weapon0),"silent rear approach authorizes exact dagger");Check(!r.AllowsTake(new Agent(),EquipmentIndex.Weapon0)&&!r.AllowsTake(h.Teacher,EquipmentIndex.Weapon1),"other targets and slots never authorized");
 NpcSightSystem.Cone=true;Check(!r.CanAttempt(h.Teacher),"front approach cannot open practice steal");NpcSightSystem.Cone=false;r.TickCompletion();Check(!StoryProbeState.HasAsked("son","lesson_stealth_done"),"position or possession alone does not finish");
 r.TakeCommitted(h.Teacher,EquipmentIndex.Weapon0);r.TickCompletion();Check(r.Active,"uncleared real slot cannot finish");h.Teacher.SpawnEquipment[EquipmentIndex.Weapon0]=EquipmentElement.Invalid;StealManager.IsUIOpen=true;r.TakeCommitted(h.Teacher,EquipmentIndex.Weapon0);r.TickCompletion();Check(r.Active,"successful take waits for modal cleanup");StealManager.IsUIOpen=false;r.TickCompletion();Check(!r.Active&&StoryProbeState.HasAsked("son","lesson_stealth_done")&&Campaign.Current.QuestManager.Quests.Last().Succeeded,"real receipt finishes task and marks completion");Check(!r.CanStart("stealth_dagger",out _),"completed exercise cannot farm dagger");
 Setup();Start();var stale=AgentAIController.Instance.Moves[0].done;var owned=AgentAIController.Instance.Moves[0].move;r.Stop("retry");Check(owned.Interrupted&&!r.IsPracticeTarget(h.Teacher),"stop cancels only owned movement and revokes permission");Start();stale(true);r.Tick(.1f);Check(!r.Describe().Contains("phase=lease"),"same-definition retry rejects previous callback");stale(false);r.Tick(.1f);Check(r.Active,"old failed callback cannot cancel new run");
 Setup();Start();Ready();h.Teacher.SpawnEquipment[EquipmentIndex.Weapon0]=EquipmentElement.Invalid;StealManager.IsUIOpen=true;r.TakeCommitted(h.Teacher,EquipmentIndex.Weapon0);Check(StoryProbeState.HasAsked("son","lesson_stealth_done"),"real transfer persists completion before modal cleanup");StoryPracticeQuest.CancelAll();Check(Campaign.Current.QuestManager.Quests.Last().Succeeded,"load cleanup preserves the committed native task success");r.Stop("cancel during modal");Check(Campaign.Current.QuestManager.Quests.Last().Succeeded&&!r.CanStart("stealth_dagger",out _),"cancelling after transfer preserves success and cannot grant another dagger");
 Setup();Start();Ready();r.Stop("cancel");r.Tick(.1f);var homeMove=AgentAIController.Instance.Moves.Last();Start();Check(homeMove.move.Interrupted,"new lesson cancels owned return-home movement");homeMove.done(false);r.Tick(.1f);Check(r.Active,"old return callback cannot cancel new exercise");
 Setup();Start();Ready();r.PracticeAttempt(h.Teacher,false,EquipmentIndex.Weapon0);Check(!r.CanAttempt(h.Teacher),"miss blocks reopening the practice steal until the retry window");h.Clock=7;r.Tick(.1f);Check(r.CanAttempt(h.Teacher),"failed practice can retry");
 Setup();Start();Ready();ConversationEntryPatch.Active=true;r.Tick(.1f);Check(!r.IsPracticeTarget(h.Teacher)&&r.Active,"conversation pauses authorization until actual reply");r.Stop("cancel");Check(h.Teacher.SpawnEquipment[EquipmentIndex.Weapon0].IsEmpty&&Campaign.Current.QuestManager.Quests.Last().IsFinalized,"cancel removes borrowed equipment and native task");
 foreach(string mode in new[]{"token","mission","player","death","combat"}){Setup();Start();Ready();switch(mode){case "token":StoryProbeState.RuntimeToken++;break;case "mission":Mission.Current=new();break;case "player":Agent.Main=new();break;case "death":h.Teacher.Health=0;break;case "combat":AgentAIController.Brains[h.Teacher].IsInCombat=true;break;}r.Tick(.1f);Check(!r.Active&&!r.IsPracticeTarget(h.Teacher),"revoke on "+mode);}
 Setup();Start();Ready();h.Clock=301;r.Tick(.1f);Check(r.Active,"lesson timeout keeps waiting instead of failing the task");
 Setup();Start("grapple_wall");Agent.Main.Position=new(433.58f,399.35f,74f);AgentAIController.Instance.Arrive();r.Tick(.1f);h.Clock=3;r.Tick(.1f);r.Tick(.1f);var demo=GrappleLogic.GetSession(h.Teacher);Check(r.Describe().Contains("phase=demo"),"demo request without world-hit receipt cannot proceed");demo.WorldHitReceipt++;demo.LastWorldHit=new(434,402,79.75f);demo.PullFinishedReceipt++;r.Tick(.1f);r.Tick(.1f);Check(demo.Released&&r.Describe().Contains("phase=practice"),"actual target hit and a real climb hand control to the player exercise");var player=GrappleLogic.GetSession(Agent.Main);r.Tick(.1f);Check(r.Active,"release or abort without finished receipt cannot pass");player.PullFinishedReceipt++;player.LastPullAnchor=new(999,0,0);r.Tick(.1f);Check(r.Active,"climb elsewhere cannot finish this lesson");player.PullFinishedReceipt++;player.LastPullAnchor=new(434,402,79.75f);r.Tick(.1f);Check(!r.Active&&Campaign.Current.QuestManager.Quests.Last().Succeeded,"fresh pull-finished receipt at taught anchor completes");
 Setup();Start("grapple_wall");Agent.Main.Position=new(433.58f,399.35f,74f);AgentAIController.Instance.Arrive();r.Tick(.1f);h.Clock=3;r.Tick(.1f);r.Tick(.1f);h.Clock=40;r.Tick(.1f);Check(r.Active&&!r.Describe().Contains("phase=practice"),"failed demo retries instead of failing the lesson");
 Setup();Start();string file=System.IO.Path.Combine(StoryProbeDataFiles.DesignDataFolder,"StoryLessons.json");string original=System.IO.File.ReadAllText(file),revision=r.Revision;foreach(var variant in new[]{original.Replace("\"Mode\": ","\"UnknownField\": 1, \"Mode\": "),original.Replace("\"Mode\": ","\"Mode\": \"invalid\", \"Mode\": "),original.Replace("\"Actor\": \"son\"","\"Actor\": \"missing-actor\""),original.Replace("\"Item\": \"gladius_b\"","\"Item\": \"missing-item\"")}){System.IO.File.WriteAllText(file,variant);bool rejected=false;try{r.Reload();}catch{rejected=true;}Check(rejected&&r.Revision==revision&&r.Active,"invalid reload preserves live definition");}System.IO.File.WriteAllText(file,original);r.Stop("fixture cleanup");
 Setup();h.Teacher.SpawnEquipment[EquipmentIndex.Weapon0]=new(StoryProbeDefs.FindItem("original"));Start();Ready();r.Stop("cancel");Check(h.Teacher.SpawnEquipment[EquipmentIndex.Weapon0].Item.StringId=="original","original weapons preserved across teaching");
 Console.WriteLine("PASS: "+count+" production practice lifecycle checks (native movement, grapple and UI are test doubles)");
 }
}
'''
    (OUT / 'StoryLessons.json').write_bytes((ROOT / 'ModuleData/DesignData/StoryLessons.json').read_bytes())
    code = code.replace('__DATA__', json.dumps(str(OUT)))
    (OUT / 'Program.cs').write_text(code, encoding='utf-8')
    source = escape(str(SRC / 'Interaction/StoryProbe/StoryPractice.cs'))
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="'+source+'"/><Reference Include="Newtonsoft.Json"><HintPath>'+escape(str(ROOT.parents[1]/"bin/Win64_Shipping_Client/Newtonsoft.Json.dll"))+'</HintPath></Reference></ItemGroup></Project>'
    (OUT / 'Harness.csproj').write_text(project, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)
    # A borrowed menu must remain a direct, valid menu with no own continuation or effects replay.
    data = json.loads((ROOT / 'ModuleData/DesignData/Dialogues/story_son.dialog.json').read_text(encoding='utf-8-sig'))
    node = next(n for n in data['Nodes'] if n.get('OptionsFrom'))
    for bad in ('missing', node['Id']):
        changed = deepcopy(data)
        next(n for n in changed['Nodes'] if n['Id'] == node['Id'])['OptionsFrom'] = bad
        assert any(i['code'] == 'bad_menu' for i in story_dialogue_issues(changed, formal=True))
    for field, value in [('NextNode', 'son_hub'), ('Options', [{'Id': 'fake'}])]:
        changed = deepcopy(data)
        next(n for n in changed['Nodes'] if n['Id'] == node['Id'])[field] = value
        assert any(i['code'] == 'bad_menu' for i in story_dialogue_issues(changed, formal=True))
    theft = (SRC / 'Stealth/StealManager.cs').read_text(encoding='utf-8-sig')
    assert 'bool training = practice?.IsPracticeTarget(agent) == true && practice.AllowsTake(agent, index);' in theft
    assert 'TransferItems(null, Hero.MainHero, itemToSteal, 1) != 1) return null;' in theft
    assert 'if (training) practice.TakeCommitted(agent, index);' in theft
    grapple = (SRC / 'Combat/GrappleLogic.cs').read_text(encoding='utf-8-sig')
    assert 'PullFinishedReceipt++; LastPullAnchor = _hook.AttachedPoint;' in grapple
    lessons = json.loads((ROOT / 'ModuleData/DesignData/StoryLessons.json').read_text(encoding='utf-8-sig'))
    for field, value in [('Actor', 'missing'), ('Mode', 'unknown'), ('Position', [0, 1]), ('TimeoutSec', float('inf')), ('Unexpected', 1), ('DoneKey', None), ('Item', ''), ('Kit', None)]:
        changed = deepcopy(lessons)
        changed[0][field] = value
        assert story_lesson_issues(changed, {'son'}), (field, value)
    print('PASS: 8 lesson negatives, 4 menu negatives and production theft/grapple receipt connections')


if __name__ == '__main__':
    main()


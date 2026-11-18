"""Execute production encounter arbitration; native agents/teams/vision are boundaries."""
from pathlib import Path
from copy import deepcopy
import json
import subprocess
from xml.sax.saxutils import escape
from check_story_probe import story_dialogue_issues
from test_story_feedback import method

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/story_combat_tests'


def data_checks():
    data = json.loads((ROOT / 'ModuleData/DesignData/Dialogues/story_thug.dialog.json').read_text(encoding='utf-8'))
    nodes = {n['Id']: n for n in data['Nodes']}
    for suffix, beneficiary, event in [('', 'pawnbroker', '1'), ('_butcher', 'butcher', '2'), ('_personal', None, None)]:
        challenge = nodes['thug_fight' + suffix]
        assert len(challenge['Options']) == 1
        answer = nodes[challenge['Options'][0]['NextNode']]
        fight = next(e for e in answer['OnEnter'] if e['Id'] == 'start_fight')
        assert fight['Commit'] == 'on_session_end' and fight['Params']['mode'] == 'wound'
        assert fight['Params'].get('beneficiaries') == beneficiary
        assert fight['Params'].get('resolve_event') == event
        assert not answer.get('Options')
    checked = 0
    for node in nodes.values():
        for option in node.get('Options', []):
            if option['Id'].startswith('fight'):
                checked += 1
                assert option['Check']['Capability'] == 'Intimidate' and option['Check']['Tactic'] == 'Threaten'
                assert option['PlayerKeyZh'].startswith('[威吓]')
                assert not option['Effects']
                assert ('!event_resolved:1' if option['Id'] == 'fight' else 'event_resolved:1') in option['Requires']
    assert checked == 15
    original = nodes['thug_fight_response']['OnEnter'][0]
    for key, value in [('mode', 'duel'), ('reason', 'secret'), ('beneficiaries', 'missing'), ('resolve_event', '0')]:
        broken = deepcopy(data)
        effect = next(n for n in broken['Nodes'] if n['Id'] == 'thug_fight_response')['OnEnter'][0]
        effect['Params'][key] = value
        assert story_dialogue_issues(broken, formal=True), key
    broken = deepcopy(data)
    complete = next(e for n in broken['Nodes'] for e in n.get('OnEnter', []) if e['Id'] == 'complete_quest')
    complete['Params']['if_accepted'] = 'yes'
    assert story_dialogue_issues(broken, formal=True)
    assert original['Params']['beneficiaries'] == 'pawnbroker'
    print('PASS: 15 intimidation routes, 3 response-before-combat outcomes and 5 invalid effect variants')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    stub = r'''
using System;using System.Linq;using System.Collections.Generic;using System.Reflection;
using LivingWorldNpcs;using TaleWorlds.MountAndBlade;using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
namespace HarmonyLib {public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string s){}}}
namespace TaleWorlds.Core {public enum AgentState {Active,Killed,Unconscious,Deleted}}
namespace TaleWorlds.MountAndBlade {
 public class Mission {public static Mission Current=new();}
 public class Team {public bool Hostile=true;public bool IsEnemyOf(Team t)=>Hostile&&t!=this;}
 public class Agent {public static Agent Main;public int Index;public string Name;public bool Active=true,IsMainAgent,KO;public float Health=100;public Team Team=new();public Mission Mission=Mission.Current;}
}
namespace TaleWorlds.CampaignSystem {public class Campaign {public static Campaign Current=new();public Conversations ConversationManager=new();}public class Conversations {public bool IsConversationInProgress;}}
namespace LivingWorldNpcs {
 public static class DebugLogger {public static void Log(string s){}}
 public static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a?.Active==true&&a.Mission==Mission.Current;}
 public class AgentBrain {public bool IsPerforming,IsInCombat;public List<string> Memories=new();public void RecordNarration(string s){Memories.Add(s);}public static bool IsKnockedOut(Agent a)=>a?.KO==true;public static bool IsRoped(Agent a)=>false;public static bool IsHeldByStatus(Agent a)=>false;}
 public class AgentAIController {public static AgentAIController Instance=new();public static Dictionary<Agent,AgentBrain> Brains=new();public static int Stops,Starts;public static bool ThrowStop,Reject,Queue;public static CombatEncounter Last;
  public static AgentBrain GetBrainForAgent(Agent a)=>a!=null&&Brains.TryGetValue(a,out var b)?b:null;
  public void SendEventToAgent(Agent a,string e,params object[] args){if(e=="combat_encounter"){Starts++;Last=(CombatEncounter)args[0];if(Reject)Last.Cancel();else if(!Queue)Last.Begin();return;}Stops++;if(ThrowStop)throw new Exception("UI");}
 }
 public static class NpcSightSystem {public static HashSet<Agent> Visible=new();public static bool CanAgentSeeTarget(Agent a,Agent b,float d,float f)=>Visible.Contains(a);}
 public enum SpeechPriority {Combat}
 public class SpeechContext {public string KnownConflict;public static SpeechContext FromBrain(AgentBrain b,Agent a,string t,string s)=>new();}
 public static class SpeechChannel {public static void SayPolished(Agent a,string t,SpeechPriority p,SpeechContext c){}}
 public static class LWNTextHelper {public static string ResolveText(string key,string fallback)=>key;public static string ResolveCompound(string key,params (string,string)[] args)=>key;public static string ResolveCompound(string key,string fallback,params (string,string)[] args)=>key;}
 public static class CombatManager {public static int Ends;public static void EndFight(Agent a){Ends++;}}
 public static class StoryProbeState {public static HashSet<string> Accepted=new();public static int Resolved;public static void ResolveStreetEvent(int e){Resolved=e;}public static bool IsQuestAccepted(string id)=>Accepted.Contains(id);public static bool IsQuestDone(string id)=>false;}
 public class Representative {public string Id,QuestTargetNpcId;}
 public static class StoryProbeDefs {public static Representative[] Representatives={new(){Id="butcher",QuestTargetNpcId="thug"}};public static object GetEvent(int id)=>id==1||id==2?new():null;}
 public static class StoryProbeTuning {public static Tuning Current=new();public class Tuning {public Performance Performance=new();}public class Performance {public float ActorReadyTimeoutSec=5;}}
 public class AttackTriggerMissionLogic {public static AttackTriggerMissionLogic Instance=new();}
 public class InformationMessage {public InformationMessage(string s){}}
 public static class InformationManager {public static int Messages;public static void DisplayMessage(InformationMessage m){Messages++;}}
 enum StoryProbePhase {Second}
 class Stage {public bool Active;}
 class Director {public int Resolved;public void ResolveCurrentEvent(int index){Resolved=index;}}
 class CombatHost {
  public static CombatHost Current;public bool Active=true;public StoryProbePhase Phase=StoryProbePhase.Second;public string _playerDetentionMode;public Stage _stage=new();public Director _director=new();public float _time;
  public Dictionary<string,Agent> Agents=new();private readonly HashSet<string> _fightRequests=new();private readonly Dictionary<string,float> _fightStartedAt=new();private readonly Dictionary<string,int> _duelTokens=new();private readonly Dictionary<string,Action<Agent>> _duelCallbacks=new();private int _duelSerial;public bool _pendingBlockerDown;public HashSet<string> _pendingQuestDone=new();
  REQUEST_CLASS
  private readonly Dictionary<string,FightRequest> _fightDefinitions=new();public int Pending=>_fightRequests.Count;
  public Agent GetAgent(string id)=>Agents.TryGetValue(id,out var a)?a:null;private string NpcName(string id)=>id;
  HOST_METHODS
  public void Pump()=>PumpStoryFights();
 }
}
class Program {
 static int checks,receipts;static Agent lost;
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);checks++;}
 static (Agent a,Agent b,Agent w,CombatEncounter e) Setup(bool wound=true){CombatEncounter.Clear(Mission.Current);Mission.Current=new();AgentAIController.Stops=0;AgentAIController.ThrowStop=false;CombatManager.Ends=0;receipts=0;lost=null;NpcSightSystem.Visible.Clear();AgentAIController.Brains.Clear();
  var a=new Agent{Name="a"};var b=new Agent{Name="player",IsMainAgent=true};var w=new Agent{Name="helped"};foreach(var x in new[]{a,b,w})AgentAIController.Brains[x]=new();
  var e=new CombatEncounter(a,b,wound,"LWN_reason",new[]{w},null,null,x=>{receipts++;lost=x;});return(a,b,w,e);}
 static AgentState Decide(Agent a){object[] args={Mission.Current,a,AgentState.Killed};typeof(CombatEncounterWoundPatch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);return(AgentState)args[2];}
 static void Main(){
 var(a,b,w,e)=Setup();e.Cancel();e.Cancel();CombatEncounter.Tick();Check(receipts==1&&lost==null&&CombatManager.Ends==0&&AgentAIController.Stops==0,"pre-start cancellation is one receipt with zero team/body cleanup");
 (a,b,w,e)=Setup();a.Team=b.Team;Check(!e.Begin(),"non-hostile teams cannot establish combat");e.Cancel();Check(!CombatEncounter.Owns(a),"rejected pair is not active");
 (a,b,w,e)=Setup();NpcSightSystem.Visible.Add(w);Check(e.Begin(),"hostile pair begins");Check(!e.Begin(),"duplicate begin refused");Check(CombatEncounter.Owns(a)&&CombatEncounter.Owns(b),"owns exact participants");
 Check(Decide(a)==AgentState.Unconscious&&Decide(b)==AgentState.Unconscious,"both native defeat decisions become wounded");Check(Decide(w)==AgentState.Killed,"unrelated death decision untouched");
 Check(CombatEncounter.KnownReason(a)=="LWN_reason"&&CombatEncounter.KnownReason(w)=="LWN_reason","participants and eyewitness beneficiary know cause");
 var outsider=new Agent{Name="outsider"};Check(CombatEncounter.KnownReason(outsider)==null,"outsider has no omniscient cause");
 Check(CombatEncounter.NoteHelpedAttack(w,a,b)&&CombatEncounter.NoteHelpedAttack(w,b,a),"beneficiary recognizes both directions of this exact conflict");Check(!CombatEncounter.NoteHelpedAttack(w,a,outsider),"another victim is not exempted");Check(!CombatEncounter.HelpsWitness(outsider,a,b),"unrelated witness remains alertable");
 a.Health=0;CombatEncounter.NotifyRemoved(a,AgentState.Unconscious);Check(receipts==0&&CombatManager.Ends==0,"native removal callback only records result");CombatEncounter.Tick();CombatEncounter.Tick();e.EndCombatant(a);
 Check(receipts==1&&lost==a&&CombatManager.Ends==1,"native down gives one actual result and one paired team return");Check(a.Health==0,"real damage never restored as sparring");Check(!CombatEncounter.Owns(a)&&!CombatEncounter.HelpsWitness(w,a,b),"closed conflict leaves no ownership or exemption");
 (a,b,w,e)=Setup();e.Begin();Check(CombatEncounter.KnownReason(w)==null&&!CombatEncounter.HelpsWitness(w,a,b),"distant or obstructed beneficiary cannot know or receive exemption");e.Cancel();Check(receipts==0&&AgentAIController.Stops==0,"started cancellation defers out of action cleanup stack");CombatEncounter.Tick();Check(receipts==1&&lost==null&&CombatManager.Ends==1,"normal tick cancels once");
 (a,b,w,e)=Setup();e.Begin();e.Cancel();a.Health=0;CombatEncounter.Tick();Check(lost==a&&receipts==1,"same-frame real defeat takes precedence over cancel");
 (a,b,w,e)=Setup();e.Begin();CombatEncounter.NotifyRemoved(a,AgentState.Deleted);CombatEncounter.Tick();Check(lost==null&&receipts==1,"alive departure does not pretend defeat");
 (a,b,w,e)=Setup();e.Begin();Mission.Current=new();CombatEncounter.Tick();Check(receipts==1&&lost==null&&CombatManager.Ends==0&&AgentAIController.Stops==0,"mission change never touches stale native participants");
 (a,b,w,e)=Setup(false);e.Begin();Check(Decide(a)==AgentState.Killed,"explicit mortal mode retains native fatal result");AgentAIController.ThrowStop=true;b.Health=0;CombatEncounter.Tick();Check(receipts==1&&lost==b&&CombatManager.Ends==1,"stop-event failure cannot lose result or cleanup");
 (a,b,w,e)=Setup();e.Begin();CombatEncounter.Clear(Mission.Current);Check(receipts==1&&!CombatEncounter.Owns(a)&&CombatManager.Ends==1,"mission removal clears active arbitration immediately");
 Console.WriteLine("PASS: "+checks+" production encounter lifecycle, injury arbitration and witness scope checks (native boundaries stubbed)");
 checks=0;
 (a,b,w,e)=Setup();Agent.Main=b;var host=new CombatHost();CombatHost.Current=host;host.Agents["thug"]=a;host.Agents["pawnbroker"]=w;AgentAIController.Starts=0;AgentAIController.Reject=false;AgentAIController.Queue=false;StoryProbeState.Accepted.Clear();StoryProbeState.Resolved=0;InformationManager.Messages=0;
 Check(host.RequestStoryFight("thug","wound","LWN_reason","pawnbroker",null,null,"1"),"valid data-driven request");Check(host.RequestStoryFight("thug")&&host.Pending==1,"repeated request cannot overwrite cause or enqueue twice");
 Campaign.Current.ConversationManager.IsConversationInProgress=true;host.Pump();Check(AgentAIController.Starts==0,"actual pump waits until response display and conversation end");Campaign.Current.ConversationManager.IsConversationInProgress=false;host._stage.Active=true;host.Pump();Check(AgentAIController.Starts==0,"actual pump waits for stage release");host._stage.Active=false;host.Pump();host.Pump();Check(AgentAIController.Starts==1,"actual receipt token prevents repeated enqueue");
 a.Health=0;CombatEncounter.NotifyRemoved(a,AgentState.Unconscious);CombatEncounter.Tick();Check(host.Pending==0&&StoryProbeState.Resolved==1&&host._director.Resolved==1&&host._pendingQuestDone.Count==0,"real win resolves specified current dispute but never unaccepted quest");
 (a,b,w,e)=Setup();Agent.Main=b;host=new();CombatHost.Current=host;host.Agents["thug"]=a;AgentAIController.Starts=0;AgentAIController.Queue=true;InformationManager.Messages=0;host.RequestStoryFight("thug");host.Pump();host._time=11;host.Pump();Check(host.Pending==0&&InformationManager.Messages==1&&CombatManager.Ends==0,"queued request timeout reports cancellation once without pretending battle");AgentAIController.Queue=false;
 host._time=12;host.RequestStoryFight("thug");AgentAIController.Reject=true;host.Pump();Check(host.Pending==0&&InformationManager.Messages==2,"brain rejection gives a visible cancellation and no lingering request");AgentAIController.Reject=false;
 host.RequestStoryFight("thug");AgentAIController.Brains[a].IsPerforming=true;host._time=23;host.Pump();Check(host.Pending==0&&InformationManager.Messages==3,"occupied actor cannot wait indefinitely or lose failure message");AgentAIController.Brains[a].IsPerforming=false;
 host.RequestStoryFight("thug");host.Pump();b.Health=0;CombatEncounter.NotifyRemoved(b,AgentState.Unconscious);CombatEncounter.Tick();Check(!host._pendingBlockerDown&&host._pendingQuestDone.Count==0,"player loss never grants victory progress");
 Console.WriteLine("PASS: "+checks+" actual response-waiting, queue/token, timeout and quest outcome checks (native boundaries stubbed)");
 }
}
'''
    (OUT / 'Program.cs').write_text(stub, encoding='utf-8')
    host_source = (SRC / 'Interaction/StoryProbe/StoryProbeMissionBehavior.cs').read_text(encoding='utf-8-sig')
    request_class = host_source[host_source.index('private sealed class FightRequest'):host_source.index('private readonly Dictionary<string, FightRequest>')]
    host_methods = '\n'.join(method(host_source, n) for n in ('RequestStoryFight', 'PumpStoryFights', 'ReportStoryFightCancelled', 'OnStoryDuelResolved'))
    (OUT / 'Program.cs').write_text(stub.replace('REQUEST_CLASS', request_class).replace('HOST_METHODS', host_methods), encoding='utf-8')
    (OUT / 'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="%s"/></ItemGroup></Project>' % escape((SRC / 'Combat/CombatEncounter.cs').as_posix()), encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)
    data_checks()


if __name__ == '__main__':
    main()

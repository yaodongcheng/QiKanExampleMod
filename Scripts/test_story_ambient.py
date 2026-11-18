"""Execute real daily speech scheduling and host delivery; native agents and UI are stubs."""
from pathlib import Path
import json
import subprocess
from test_story_feedback import method

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/feedback_ambient_tests'


def main():
    street = (SRC/'Interaction/StoryProbe/StreetLifeDirector.cs').read_text(encoding='utf-8-sig')
    host = (SRC/'Interaction/StoryProbe/StoryProbeMissionBehavior.cs').read_text(encoding='utf-8-sig')
    facts = (SRC/'Interaction/StoryProbe/StoryDialogueFacts.cs').read_text(encoding='utf-8-sig')
    code = r'''
using System;using System.Linq;using System.Collections.Generic;
struct Vec3 {public float x;public Vec3(float v){x=v;}public float Distance(Vec3 b)=>Math.Abs(x-b.x);}
class Agent {public static Agent Main=new(){Position=new(0)};public float Health=100;public Vec3 Position;}
class StreetIdleAction {} class ForeignAction {}
class Brain {public bool IsInCombat,IsStoryIntercepting,IsBound,IsStatusHeld,IsStunned,IsPerforming;public object CurrentAction=new StreetIdleAction();}
static class AgentAIController {public static Dictionary<Agent,Brain> Brains=new();public static Brain GetBrainForAgent(Agent a)=>Brains.TryGetValue(a,out var b)?b:null;}
static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a!=null;}
enum SpeechPriority {Chat}
struct SpeechContext {public Agent Listener;public string StimulusType;public static SpeechContext FromBrain(Brain b,Agent a,string s,object n,Agent l)=>new(){Listener=l,StimulusType=s};}
static class SpeechChannel {public static List<(Agent speaker,string key,SpeechContext context)> Said=new();public static void Say(Agent a,string t,SpeechPriority p,SpeechContext c,float n,bool f){Said.Add((a,t,c));}}
static class DebugLogger {public static void Log(string t){}}
static class LWNTextHelper {public static string ResolveText(string k,string f)=>k;}
class StoryDialogueContext {public string ActorId;}
class StoryDialogueFacts {public static HashSet<string> Done=new();public static bool TryEvaluate(string t,StoryDialogueContext c,out bool v,out string e){e=null;if(t.StartsWith("quest_done")){v=Done.Contains(c.ActorId);return true;}v=false;e="invalid";return false;}__FACTS__}
class SpeechDef {public List<string> Requires=new();public string Listener;public string[] LineKeys,LineFallbacks;}
class StreetLifeDef {public string NpcId;public List<SpeechDef> Speech=new();public float SpeechRadiusMeters=10,SpeechFirstDelayMinSec=2,SpeechFirstDelayMaxSec=8,SpeechGapMinSec=12,SpeechGapMaxSec=20;}
static class StoryProbeDefs {public static List<StreetLifeDef> StreetLife=new();public static StreetLifeDef GetStreetLife(string id)=>StreetLife.FirstOrDefault(d=>d.NpcId==id);}
static class StoryProbeTuning {public static StoryProbeTuningData Current=new();public class StoryProbeTuningData {public AmbientData Ambient=new();}public class AmbientData {public float SpeechGapSec=3.5f;}}
class Work {public HashSet<string> Working=new();public bool CanSpeakWhileWorking(string id)=>Working.Contains(id);}
class Host {public bool IsPracticeActor(string id)=>false;public Work Work=new();public Dictionary<string,Agent> Agents=new();public Agent GetAgent(string id)=>id!=null&&Agents.TryGetValue(id,out var a)?a:null;public Agent ResolveSpeechListener(string id)=>id=="player"?Agent.Main:GetAgent(id);__HOST__}
class Director {
private Host _host;private float _time=10,_nextAmbientSpeechAt;private int _nextSpeechDefIndex;private Random _rng=new(0);
private Dictionary<string,float> _nextSpeechAt=new();private Dictionary<string,int> _speechCursor=new();
private Dictionary<Agent,object> _eventActors=new(),_ambientActors=new();public bool Stopped;
public Director(Host h){_host=h;}public void Tick(float t){_time=t;TickAmbientSpeech();}public void Event(Agent a){_eventActors[a]=new();}
__DIRECTOR__
}
class Program {
static int count;static void Check(bool v,string label){if(!v)throw new Exception(label);count++;}
static (Host h,Director d,Agent a,Brain b,StreetLifeDef def) Setup(bool working=false){SpeechChannel.Said.Clear();StoryDialogueFacts.Done.Clear();Agent.Main=new(){Position=new(0)};var h=new Host();var a=new Agent{Position=new(3)};h.Agents["worker"]=a;var b=new Brain();AgentAIController.Brains=new(){{a,b}};var def=new StreetLifeDef{NpcId="worker",Speech=new(){new(){Requires=new(){"!quest_done"},LineKeys=new[]{"pending1","pending2"},LineFallbacks=new[]{"p1","p2"}},new(){Requires=new(){"quest_done"},LineKeys=new[]{"done1","done2"},LineFallbacks=new[]{"d1","d2"}}}};StoryProbeDefs.StreetLife=new(){def};if(working){h.Work.Working.Add("worker");b.CurrentAction=new ForeignAction();b.IsPerforming=true;}return(h,new Director(h),a,b,def);}
static void Main(){
var (h,d,a,b,def)=Setup();d.Tick(10);Check(SpeechChannel.Said.Count==1,"idle speech guaranteed when due");Check(SpeechChannel.Said[0].context.Listener==null&&SpeechChannel.Said[0].context.StimulusType=="story-self-talk","self talk has no implicit player listener");
d.Tick(11);Check(SpeechChannel.Said.Count==1,"person cooldown avoids spam");d.Tick(50);Check(SpeechChannel.Said[1].key=="pending2","lines rotate rather than repeatedly choosing first");
StoryDialogueFacts.Done.Add("worker");d.Tick(100);Check(SpeechChannel.Said.Last().key=="done1","resolved story selects different speech");
(h,d,a,b,def)=Setup(true);d.Tick(10);Check(SpeechChannel.Said.Count==1,"work performer speaks without releasing work lease");Check(b.IsPerforming&&b.CurrentAction is ForeignAction,"speech leaves work controller unchanged");
foreach(var state in new[]{"combat","intercept","bound","status","stunned","foreign","performance","event"}){
(h,d,a,b,def)=Setup();switch(state){case "combat":b.IsInCombat=true;break;case "intercept":b.IsStoryIntercepting=true;break;case "bound":b.IsBound=true;break;case "status":b.IsStatusHeld=true;break;case "stunned":b.IsStunned=true;break;case "foreign":b.CurrentAction=new ForeignAction();break;case "performance":b.IsPerforming=true;break;case "event":d.Event(a);break;}d.Tick(10);Check(SpeechChannel.Said.Count==0,"ambient yields to "+state);}
(h,d,a,b,def)=Setup();a.Position=new(10.01f);d.Tick(10);Check(SpeechChannel.Said.Count==0,"far player does not provoke speech");a.Position=new(10);d.Tick(10);Check(SpeechChannel.Said.Count==1,"range boundary is inclusive");
(h,d,a,b,def)=Setup();a.Health=0;d.Tick(10);Check(SpeechChannel.Said.Count==0,"dead actor silent");
(h,d,a,b,def)=Setup();Agent.Main=null;d.Tick(10);Check(SpeechChannel.Said.Count==0,"missing player safe");
(h,d,a,b,def)=Setup();def.Speech[0].Requires=new(){"!invalid"};d.Tick(10);Check(SpeechChannel.Said.Count==0,"invalid negated predicate never becomes allowed");
(h,d,a,b,def)=Setup();var other=new Agent{Position=new(30)};h.Agents["other"]=other;def.Speech[0].Listener="other";d.Tick(10);Check(SpeechChannel.Said.Count==0,"addressed ambient respects actual listener distance");other.Position=new(4);d.Tick(10);Check(SpeechChannel.Said.Count==1&&SpeechChannel.Said[0].context.Listener==other,"nearby explicit listener preserved");
(h,d,a,b,def)=Setup();d.Tick(10);d.CueSpeech("worker");d.Tick(10);Check(SpeechChannel.Said.Count==2,"diagnostic cue uses same speech path");a.Position=new(30);d.CueSpeech("worker");d.Tick(10);Check(SpeechChannel.Said.Count==2,"cue cannot bypass range");
(h,d,a,b,def)=Setup();var a2=new Agent{Position=new(4)};h.Agents["other"]=a2;AgentAIController.Brains[a2]=new();StoryProbeDefs.StreetLife.Add(new(){NpcId="other",Speech=def.Speech});d.Tick(10);d.Tick(11);Check(SpeechChannel.Said.Count==1,"global gap separates nearby speakers");d.Tick(14);Check(SpeechChannel.Said.Count==2,"second nearby speaker is not starved");
(h,d,a,b,def)=Setup();for(int i=1;i<8;i++){var actor=new Agent{Position=new(4)};h.Agents["worker"+i]=actor;AgentAIController.Brains[actor]=new();StoryProbeDefs.StreetLife.Add(new(){NpcId="worker"+i,Speech=def.Speech});}
for(int i=0;i<16;i++)d.Tick(10+i*3.5f);
Check(h.Agents.Values.All(actor=>SpeechChannel.Said.Count(s=>s.speaker==actor)==2),"eight due speakers get two turns each even when early actors' cooldowns expire mid-round");
Console.WriteLine($"PASS: {count} real daily speech scheduler/host checks (native agents and SpeechChannel presentation stubbed)");
}}
'''
    code = code.replace('__DIRECTOR__', '\n'.join(method(street, n) for n in ('SpeechDelay','TickAmbientSpeech','CueSpeech')))
    code = code.replace('__HOST__', method(host, 'SayOnAgent')).replace('__FACTS__', method(facts, 'TryEvaluateAll'))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'Program.cs').write_text(code,encoding='utf-8')
    (OUT/'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>',encoding='utf-8')
    subprocess.run(['dotnet','run','--project',str(OUT/'Harness.csproj'),'--verbosity','quiet'],check=True)
    print('PASS: dialogue-level pause gate and no body/gaze writes in speech scheduler')
    assert 'if (!inConversation)' in method(street,'Tick')
    for token in ('SetLookAgent','SetBodyDirection','ForcePlayAction','Release('):
        assert token not in method(street,'TickAmbientSpeech')


if __name__ == '__main__':
    main()

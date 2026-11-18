"""511 feedback regressions: real C# inquiry lifecycle and training resolver, native boundaries stubbed."""
from pathlib import Path
import copy
import importlib.util
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/feedback_21_26_tests'


def method(source, name):
    match = re.search(r'(?:public|private)\s+(?:static\s+)?[\w.<>?, ]+\s+' + name + r'\s*\(', source)
    assert match, name
    start = source.index('{', match.end())
    arrow = source.find('=>', match.end(), start)
    if arrow >= 0:
        return source[match.start():source.index(';', arrow) + 1]
    end, depth = start + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[match.start():end]


def data_checks():
    data = ROOT / 'ModuleData/DesignData'
    dialogues = [json.loads(p.read_text(encoding='utf-8-sig')) for p in (data / 'Dialogues').glob('*.json')]
    cn = {s.attrib['id']: s.attrib['text'] for s in ET.parse(ROOT / 'ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml').iter('string')}
    openings = {'pawnbroker', 'widow', 'butcher', 'son', 'rival', 'debtor', 'outlaw', 'thug'}
    for dialogue in dialogues:
        nodes = {n['Id']: n for n in dialogue['Nodes']}
        for actor in openings:
            if actor + '_intro' in nodes:
                assert nodes[actor + '_intro'].get('Options'), actor
                assert actor + '_intro_line1' not in nodes, actor
        for node in nodes.values():
            for option in node.get('Options', []):
                text = cn[option['PlayerKey']]
                if option['Id'] == 'leave': assert text == '[离开] 我先走了。'
                if option['Id'].startswith('back'): assert text == '[收住] 说点别的。'
                if not option.get('Check'): assert not re.match(r'^\[(威吓|扒窃|潜行|应战|制服|撬锁)\]', text), text
    opening = json.loads((data / 'StoryOpeningTimeline.json').read_text(encoding='utf-8'))['Beats']
    inquiries = [b for b in opening if b['Cmd'] == 'inquiry']
    cuts = [b for b in opening if b['Cmd'] == 'camera_cut']
    assert len(inquiries) == 6 and len(cuts) == 5
    assert inquiries[0]['At'] < cuts[0]['At'] and cuts[0]['Actor'] == 'blocker'
    assert inquiries[0]['AcceptKey'] == 'LWN_story_observe_start'
    assert all(b['AcceptKey'] == 'LWN_story_observe_continue' for b in inquiries[1:])
    assert all(b['CancelKey'] == 'LWN_story_observe_stop' for b in inquiries)
    assert all(b['BlendSeconds'] > 0 for b in cuts)
    for cut in cuts:
        end = next(b['At'] for b in inquiries if b['At'] > cut['At'])
        assert any(b['Cmd'] == 'actor_say' and b['Actor'] == cut['Actor'] and cut['At'] < b['At'] < end for b in opening)
    guard = next(d for d in dialogues if 'blocker_open' in {n['Id'] for n in d['Nodes']})
    check = next(o['Check'] for n in guard['Nodes'] if n['Id'] == 'blocker_open' for o in n['Options'] if o['Id'] == 'threat')
    assert check['MinimumGrowth'] == 15
    cast = json.loads((data / 'StoryProbeCast.json').read_text(encoding='utf-8'))
    assert next(n for n in cast['Npcs'] if n['Id'] == 'butcher')['AttributeRewards']['force'] == 15
    spec = importlib.util.spec_from_file_location('checker', ROOT / 'Scripts/check_story_probe.py')
    checker = importlib.util.module_from_spec(spec); spec.loader.exec_module(checker)
    # Registered in Cast is insufficient: this actor is absent from the running stage.
    plan = json.loads((data / 'StoryScenePlan.json').read_text(encoding='utf-8'))
    original_load = checker.load_data_json
    broken = json.loads((data / 'StoryOpeningTimeline.json').read_text(encoding='utf-8'))
    next(b for b in broken['Beats'] if b.get('TextKey') == 'LWN_story_observe_guard_bark')['Listener'] = 'gate_sentry'
    def replaced(path):
        return broken if str(path).endswith('StoryOpeningTimeline.json') else original_load(path)
    with patch.object(checker, 'load_data_json', replaced):
        errors = []
        checker.check_stage(str(ROOT), errors)
        assert any("Listener 'gate_sentry' is not in second_meet Actors" in e for e in errors), errors
    actors = {n['Id'] for n in cast['Npcs']}
    for life in cast['StreetLife']:
        assert not checker.ambient_speech_issues(life, actors), life['NpcId']
        assert len(life['Speech']) >= 2 and all(len(rule['LineKeys']) >= 2 for rule in life['Speech'])
        event = {'widow': 3, 'rival': 3, 'butcher': 2, 'thug': 2}.get(life['NpcId'])
        if event:
            # Solving the street dispute must stop its complaint even before accepting the separate commission.
            assert '!event_resolved:' + str(event) in life['Speech'][0]['Requires']
            assert any('event_resolved:' + str(event) in r['Requires'] for r in life['Speech'][1:])
        bad = copy.deepcopy(life); bad['Speech'][0]['Requires'] = ['!unknown_predicate']
        assert checker.ambient_speech_issues(bad, actors), 'unknown negated condition must be rejected'
    print('PASS: missing stage listener negative and eight state-dependent ambient tables')
    # The strict schema must reject every malformed prerequisite.
    for invalid in (-1, 101, True, '15', 15.5):
        changed = copy.deepcopy(guard)
        bad_check = next(o['Check'] for n in changed['Nodes'] if n['Id'] == 'blocker_open' for o in n['Options'] if o['Id'] == 'threat')
        bad_check['MinimumGrowth'] = invalid
        errors = checker.story_dialogue_issues(changed, formal=True)
        assert any('MinimumGrowth' in str(e) for e in errors), (invalid, errors)
    print('PASS: dialogue labels/openings, five authored observation targets, training data and five malformed prerequisite negatives')


def runtime_checks():
    stage = (SRC / 'Interaction/StoryProbe/StoryStageRunner.cs').read_text(encoding='utf-8-sig')
    defs = (SRC / 'Interaction/StoryProbe/StoryProbeDefs.cs').read_text(encoding='utf-8-sig')
    resolver = (SRC / 'Interaction/Intents/IntentRegistry.cs').read_text(encoding='utf-8-sig')
    stage_methods = '\n'.join(method(stage, name) for name in ('ShowInquiry', 'CloseInquiry', 'Stop', 'Tick', 'Execute', 'SayLine', 'OwnsActor'))
    defs_methods = '\n'.join(method(defs, name) for name in ('PlayerGrowth', 'MeetsCheckRequirement', 'PreviewDialogueCheck', 'RollDialogueCheck', 'TakeCheckRoll'))
    code = r'''
using System; using System.Linq; using System.Collections.Generic;
enum StoryAbility {Force,Guile,Charm} enum NegotiationTactic {Threaten,Plead}
class Skill {public string StringId="Charm";}
static class DefaultSkills {public static Skill Charm=new(),Roguery=new();}
class Hero {public static Hero MainHero=new();public int GetSkillValue(Skill s)=>20;}
class Agent {public static Agent Main=new();public float Health=100;}
static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a!=null;}
class Mission {public static Mission Current=new();}
class GameEntity {} struct Vec3 {}
class StoryNpcDef {public string Id;public Dictionary<string,int> AttributeRewards;}
class StoryRoll {public StoryAbility Ability;public string Capability,NpcId,Tactic;public Skill Skill;public float PlayerAttr,NpcAttr,Chance,Threshold,Roll;public bool Success,Eligible=true;public int MinimumGrowth,Growth;public string Describe()=>"roll";}
class IntentContext {public Hero Speaker;public IntentContext(Agent a,Hero speaker){Speaker=speaker;}}
static class SkillCheckSystem {public static Skill MapTacticToSkill(NegotiationTactic t)=>DefaultSkills.Charm;}
static class MathF {public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);}
static class MBRandom {public static int Calls;public static float RandomFloat {get{Calls++;return 1;}}}
static class DebugLogger {public static void Log(string text){}}
static class LWNTextHelper {public static string ResolveText(string key,string fallback)=>key??fallback;}
static class StoryProbeState {public static int RuntimeToken;public static HashSet<string> Completed=new();public static bool IsQuestTurnedIn(string id)=>Completed.Contains(id);public static bool IsRuntimeCurrent(int token)=>token==RuntimeToken;public static State Data=new();public class State {public float RollOverride=-1;public string RollOverrideAbility="";}}
class StoryProbeMissionBehavior {public static StoryProbeMissionBehavior Current=new();public int Says;public Dictionary<string,Agent> Agents=new();public Agent GetAgent(string id)=>id!=null&&Agents.TryGetValue(id,out var a)?a:null;public Agent ResolveSpeechListener(string id)=>id=="player"?Agent.Main:GetAgent(id);public void SayOnAgent(Agent a,string key,string fb,Agent listener,bool forwardToNearby){Says++;}public Hero GetHero(string id)=>null;}
static class SingleRollResolver {__CHANCE__ public static bool Roll(float chance,float roll)=>chance>0&&roll>=1-chance;public static StoryRoll SimpleCompute(IntentContext c,NegotiationTactic t)=>new(){Chance=.5f};}
static class StoryProbeDefs {public static List<StoryNpcDef> Npcs=new(){new(){Id="butcher",AttributeRewards=new(){{"force",15}}}};public static string AbilityId(StoryAbility a)=>a.ToString().ToLowerInvariant();public static float PlayerAttr(StoryAbility a)=>20+PlayerGrowth(a);public static float NpcAttr(string id,StoryAbility a)=>70;public static bool TryGetCapability(string c,out StoryAbility a){a=StoryAbility.Force;return c=="Intimidate";}__DEFS__}
class StageBeat {public float At,BlendSeconds=.8f,Seconds;public string Cmd,Actor,Listener,Case,TitleKey,TextKey,AcceptKey,CancelKey,Note;}
class StageScript {public string Script;public List<StageBeat> Beats=new();}
class PerformanceAction {public bool Ended,CanControl=true;public int Releases;public void Release(string s){Ended=true;Releases++;}}
static class StoryProbeTuning {public static Tuning Current=new();public class Tuning {public Performance Performance=new();}public class Performance {public float ActorReadyTimeoutSec=5;}}
class InquiryData {public Action Accept,Cancel;public InquiryData(string t,string b,bool y,bool n,string a,string c,Action yes,Action no){Accept=yes;Cancel=no;}}
static class InformationManager {public static List<InquiryData> Shown=new();public static int Hides;public static void ShowInquiry(InquiryData d)=>Shown.Add(d);public static void HideInquiry()=>Hides++;}
static class CameraService {public static string Owner;public static bool IsHeldBy(string owner)=>Owner==owner;public static MachineData Machine=new();public class MachineData {public float BlendProgress=1;}}
class StageHarness {
public bool _running=true,_actorsAcquired=true,_waitingInquiry,_waitingCamera,_openingCameraPending,OpeningCameraFailed;
public float _time,_readyWait,_openingCameraWait,_openingRetryAt,_returnBlendSeconds;public int _next,_runtimeToken,_inquiryRound;
public string _cameraOwner="test",_actorOwner="session",_actorInterrupted;public Mission _mission=Mission.Current;public Agent _frozenPlayer;
public Dictionary<string,PerformanceAction> _actors=new(){{"actor",new()}};public StageScript _script=new();public StageBeat _openingCameraBeat;public Func<bool> HoldWhile;
public int BeatCount=>_script.Beats.Count;public bool OpeningCameraReady=>true;public const float OpeningCameraRetryStepSec=.25f,OpeningCameraTimeoutSec=4;
public int Returns,Cuts;public int BodyLines=>_host.Says;private StoryProbeMissionBehavior _host=StoryProbeMissionBehavior.Current;public bool Frozen=true;
public void DisposeManaged(){_running=false;CloseInquiry();}public bool AcquireActors()=>true;public bool TryCutOpeningCamera(bool quiet)=>true;
void FreezePlayer(bool freeze){Frozen=freeze;_frozenPlayer=freeze?Agent.Main:null;}
void ReturnCamera(){Returns++;if(CameraService.Owner==_cameraOwner)CameraService.Owner=null;}
void CutCamera(StageBeat b){Cuts++;CameraService.Owner=_cameraOwner;CameraService.Machine.BlendProgress=0;_waitingCamera=true;}
void SpawnProp(StageBeat b,int i){}void PlaceActor(StageBeat b,int i){}void PlayAction(StageBeat b,int i){}
__STAGE__
}
class Program {
static int checks;static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;}
static StageBeat Prompt(float at)=>new(){At=at,Cmd="inquiry",TitleKey="title",TextKey="body",AcceptKey="yes",CancelKey="no"};
static StageHarness Setup(){CameraService.Owner=null;CameraService.Machine.BlendProgress=1;InformationManager.Shown.Clear();InformationManager.Hides=0;StoryProbeState.RuntimeToken=0;Mission.Current=new();StoryProbeMissionBehavior.Current=new();foreach(var id in new[]{__ACTORS__})StoryProbeMissionBehavior.Current.Agents[id]=new();var s=new StageHarness();foreach(var id in StoryProbeMissionBehavior.Current.Agents.Keys)s._actors[id]=new();s._script.Beats.AddRange(new[]{Prompt(0),new StageBeat{At=.1f,Cmd="camera_cut"},new StageBeat{At=.2f,Cmd="actor_say",Actor="blocker",Listener="player",TextKey="line"},Prompt(.3f),new StageBeat{At=.4f,Cmd="camera_cut"},Prompt(.5f),new StageBeat{At=.6f,Cmd="camera_return"}});return s;}
static StageHarness Observe(bool missing){var s=Setup();s._script.Beats=new(){__OBSERVATION_BEATS__};if(missing)s._script.Beats.First(b=>b.TextKey=="LWN_story_observe_guard_bark").Listener="gate_sentry";int answered=0;for(int i=0;i<600&&s._running;i++){CameraService.Machine.BlendProgress=1;s.Tick(.1f);if(s._waitingInquiry){InformationManager.Shown.Last().Accept();answered++;}}return s;}
static void Main(){
StoryProbeMissionBehavior.Current.Agents["guard"]=new();
StoryProbeState.Completed.Clear();StoryProbeState.Data.RollOverride=100;
var before=StoryProbeDefs.PreviewDialogueCheck("guard",NegotiationTactic.Threaten,"Intimidate",15);
Check(!before.Eligible&&before.Chance==0,"initial training prerequisite disables chance");
var roll=StoryProbeDefs.RollDialogueCheck("guard",NegotiationTactic.Threaten,"Intimidate",15);
Check(!roll.Success&&MBRandom.Calls==0&&StoryProbeState.Data.RollOverride==100,"100% forced roll cannot bypass training or be consumed");
StoryProbeState.Completed.Add("butcher");Check(StoryProbeDefs.PlayerGrowth(StoryAbility.Force)==15,"real completed job provides growth");
StoryProbeState.Completed.Add("butcher");Check(StoryProbeDefs.PlayerGrowth(StoryAbility.Force)==15,"repeated completion cannot stack training");
before=StoryProbeDefs.PreviewDialogueCheck("guard",NegotiationTactic.Threaten,"Intimidate",15);
Check(before.Eligible&&before.PlayerAttr==35&&Math.Abs(before.Chance-SingleRollResolver.SimpleChance(35,70))<.00001,"eligible preview uses training and existing shared chance");
roll=StoryProbeDefs.RollDialogueCheck("guard",NegotiationTactic.Threaten,"Intimidate",15);Check(roll.Success&&roll.Roll==1&&StoryProbeState.Data.RollOverride==-1,"qualified roll consumes override through normal resolver");
StoryProbeState.Data.RollOverride=0;roll=StoryProbeDefs.RollDialogueCheck("guard",NegotiationTactic.Threaten,"Intimidate",15);Check(!roll.Success,"training does not guarantee success");
Check(StoryProbeDefs.PreviewDialogueCheck("missing",NegotiationTactic.Threaten,"Intimidate",15)==null,"missing actor no check");
var s=Setup();s.Tick(20);var first=InformationManager.Shown[0];Check(s._next==1&&s.Cuts==0&&s._time==0,"initial popup preserves natural camera even after long frame");s.Tick(100);Check(s._next==1,"unanswered popup never advances timeline");
first.Accept();s.Tick(.1f);Check(s.Cuts==1&&s.BodyLines==0,"click begins exactly one camera transition");s.Tick(100);Check(s.BodyLines==0&&s._next==2,"no speech or next popup before blend completes");
CameraService.Machine.BlendProgress=1;s.Tick(.1f);s.Tick(.1f);Check(s.BodyLines==1&&InformationManager.Shown.Count==2,"speech and next inquiry follow completed camera transition");
first.Accept();Check(s._waitingInquiry,"old positive callback cannot dismiss a later inquiry");first.Cancel();Check(s._running&&s._waitingInquiry,"old cancel callback cannot abort a later inquiry");
var second=InformationManager.Shown[1];second.Cancel();Check(!s._running&&!s.Frozen&&CameraService.Owner==null&&s._actors.Count==0,"cancel restores camera/player and releases actors");second.Accept();s.Tick(100);Check(s.Cuts==1,"cancelled callback never resumes playback");
s=Setup();s.Tick(1);first=InformationManager.Shown[0];s.Stop("external cancellation");Check(InformationManager.Hides==1,"external cancellation closes owned popup");s._running=true;s._actorOwner="new session";s._waitingInquiry=true;first.Accept();Check(s._waitingInquiry,"prior session callback is invalid");
s=Setup();s.Tick(1);first=InformationManager.Shown[0];StoryProbeState.RuntimeToken++;first.Accept();Check(s._waitingInquiry,"reset token rejects outstanding choice");s.Tick(.1f);Check(!s._running,"reset stops active observation");
s=Setup();s.Tick(1);InformationManager.Shown[0].Accept();s.Tick(.1f);CameraService.Owner="foreign";s.Tick(.1f);Check(!s._running&&!s.Frozen,"camera ownership loss cancels without keeping player frozen");
s=Setup();s._actors["actor"].CanControl=false;s.Tick(6);Check(!s._running&&InformationManager.Shown.Count==0,"actors must acquire real control before any inquiry or scene action");
s=Setup();s.Tick(1);InformationManager.Shown[0].Accept();s.Tick(.1f);CameraService.Machine.BlendProgress=1;s.Tick(1);InformationManager.Shown[1].Accept();s.Tick(.1f);CameraService.Machine.BlendProgress=1;s.Tick(1);InformationManager.Shown[2].Accept();s.Tick(1);Check(!s._running&&!s.Frozen&&s.Cuts==2,"full observation naturally ends and returns control");
Console.WriteLine($"PASS: {checks} extracted C# training and inquiry lifecycle checks (native camera/agents/UI are stubs)");
 s=Observe(false);Check(!s._running&&!s.Frozen&&InformationManager.Shown.Count==6&&s.Cuts==5&&s.BodyLines==7,"all authored observation cameras, actual listener checks and six inquiries complete");
 s=Observe(true);Check(!s._running&&!s.Frozen&&InformationManager.Shown.Count==1&&s.Cuts==1&&s.BodyLines==0,"old missing listener reproduces stop before guard description");
 Console.WriteLine("PASS: full 50-beat observation and missing-listener runtime negative (real Tick/Execute/SayLine)");
}}
'''
    plan = json.loads((ROOT / 'ModuleData/DesignData/StoryScenePlan.json').read_text(encoding='utf-8'))
    beats = json.loads((ROOT / 'ModuleData/DesignData/StoryOpeningTimeline.json').read_text(encoding='utf-8'))['Beats']
    rendered = []
    for beat in beats:
        fields = ['%s=%sf' % (k, float(v)) if isinstance(v, (int, float)) else '%s=%s' % (k, json.dumps(v))
                  for k, v in beat.items() if k in ('At','BlendSeconds','Seconds','Cmd','Actor','Listener','Case','TitleKey','TextKey','AcceptKey','CancelKey','Note')]
        rendered.append('new StageBeat{' + ','.join(fields) + '}')
    code = code.replace('__STAGE__', stage_methods).replace('__DEFS__', defs_methods).replace('__CHANCE__', method(resolver, 'SimpleChance'))
    code = code.replace('__OBSERVATION_BEATS__', ','.join(rendered)).replace('__ACTORS__', ','.join(json.dumps(a) for a in plan['Stages']['second_meet']['Actors']))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'Program.cs').write_text(code, encoding='utf-8')
    (OUT / 'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)


if __name__ == '__main__':
    data_checks()
    runtime_checks()

"""511 feedback regressions: real C# inquiry lifecycle and training resolver, native boundaries stubbed."""
from pathlib import Path
import copy
import importlib.util
import json
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
from unittest.mock import patch
from check_story_probe import human_playable, _game_root

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
                if option['Id'].startswith('back'): assert text.startswith('[收住] ')
                if not option.get('Check'): assert not re.match(r'^\[(威吓|扒窃|潜行|应战|制服|撬锁)\]', text), text
    opening = json.loads((data / 'StoryOpeningTimeline.json').read_text(encoding='utf-8'))['Beats']
    inquiries = [b for b in opening if b['Cmd'] == 'inquiry']
    cuts = [b for b in opening if b['Cmd'] == 'camera_cut']
    assert len(inquiries) == 6 and len(cuts) == 5
    assert inquiries[0]['At'] < cuts[0]['At'] and cuts[0]['Actor'] == 'blocker'
    assert inquiries[0]['AcceptKey'] == 'LWN_story_observe_start'
    assert all(b['AcceptKey'] == 'LWN_story_observe_continue' for b in inquiries[1:-1])
    assert all(b['CancelKey'] == 'LWN_story_observe_stop' for b in inquiries[:-1])
    assert inquiries[-1]['AcceptKey'] == 'LWN_story_observe_finish'
    assert 'CancelKey' not in inquiries[-1]
    assert cn[inquiries[-1]['AcceptKey']] == '观察完毕'
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
    # 旧版守卫动作有动画映射、却没有动作名声明；不能把它算成可播。
    with tempfile.TemporaryDirectory(dir=ROOT / 'Debug/offline') as fixture:
        game = Path(fixture)
        native = game / 'Modules/Native/ModuleData'
        module = game / 'Modules/LivingWorldNpcs'
        native.mkdir(parents=True)
        (module / 'ModuleData').mkdir(parents=True)
        guard_action = 'act_main_story_troop_guard_idle_2'
        undeclared = 'act_stealth_mission_guard_look_around_patrolling_cautious_1'
        (native / 'action_types.xml').write_text(
            '<actions><action name="%s"/></actions>' % guard_action, encoding='utf-8')
        (native / 'action_sets.xml').write_text(
            '<action_sets><action_set id="as_human_warrior"><action type="%s" animation="anim_guard"/></action_set></action_sets>' % guard_action,
            encoding='utf-8')
        (module / 'ModuleData/action_types.xml').write_text(
            '<actions><action name="act_fixture_registered"/></actions>', encoding='utf-8')
        (module / 'ModuleData/action_sets.xml').write_text(
            '<action_sets><action_set id="as_human_warrior"><action type="%s" animation="anim_guard_idle_look_around_cautious_1"/><action type="act_fixture_registered" animation="anim_fixture"/></action_set></action_sets>' % undeclared,
            encoding='utf-8')
        actual = checker.human_playable(str(game), str(module))
        assert undeclared not in actual, actual
        assert {guard_action, 'act_fixture_registered'} <= actual, actual
    print('PASS: warrior mapping without action name declaration rejected; Native and module declarations accepted')
    # Registered in Cast is insufficient: this actor is absent from the running stage.
    plan = json.loads((data / 'StoryScenePlan.json').read_text(encoding='utf-8'))
    original_load = checker.load_data_json
    broken = json.loads((data / 'StoryOpeningTimeline.json').read_text(encoding='utf-8'))
    # 二次入城现在包含韩守义；负例须真的删掉出场演员再测试错误听话者。
    broken['Beats'] = [b for b in broken['Beats'] if not (b.get('Cmd') == 'actor_place' and b.get('Actor') == 'gate_sentry')]
    next(b for b in broken['Beats'] if b.get('TextKey') == 'LWN_story_observe_guard_bark')['Listener'] = 'gate_sentry'
    test_plan = copy.deepcopy(plan)
    test_plan['Stages']['second_meet']['Actors'].remove('gate_sentry')
    def replaced(path):
        if str(path).endswith('StoryScenePlan.json'): return test_plan
        return broken if str(path).endswith('StoryOpeningTimeline.json') else original_load(path)
    with patch.object(checker, 'load_data_json', replaced):
        errors = []
        checker.check_stage(str(ROOT), errors)
        assert any("Listener 'gate_sentry' is not in second_meet Actors" in e for e in errors), errors
    test_plan = plan
    authored = json.loads((data / 'StoryOpeningTimeline.json').read_text(encoding='utf-8'))
    for field in ('AcceptKey', 'TitleKey', 'TextKey'):
        broken = copy.deepcopy(authored)
        del next(b for b in reversed(broken['Beats']) if b['Cmd'] == 'inquiry')[field]
        with patch.object(checker, 'load_data_json', replaced):
            errors = []
            checker.check_stage(str(ROOT), errors)
            assert any(field in e for e in errors), (field, errors)
    print('PASS: final single-button inquiry accepted; checker rejects missing required inquiry fields')
    # Native职业集里存在，仍不能当作剧情演员的warrior集可播；删除补注册必须抓到。
    playable = checker.human_playable(checker._game_root(str(ROOT)), str(ROOT))
    errors = []
    checker.check_references(str(ROOT), errors, cast, {'Beats': []})
    assert not errors, errors
    with patch.object(checker, 'human_playable', return_value=playable - {'act_bar_talk_to_2', 'act_bullied'}):
        errors = []
        checker.check_references(str(ROOT), errors, cast, {'Beats': opening})
        debtor_beat = next(i for i, b in enumerate(opening) if b.get('Actor') == 'debtor' and b.get('Action') == 'act_bar_talk_to_2')
        assert any(f"stage[{debtor_beat}]" in e and "act_bar_talk_to_2" in e and "NPC cannot play it" in e for e in errors), errors
        assert any("event 5" in e and "act_bar_talk_to_2" in e and "NPC cannot play it" in e for e in errors), errors
        assert all(any("event %d" % index in e and "act_bullied" in e and "NPC cannot play it" in e for e in errors)
                   for index in (1, 4)), errors
        errors = []
        checker.check_stage_v2_formal(str(ROOT), errors)
        assert all(any('Behaviors.event_%d' % index in e and 'unplayable_action' in e for e in errors)
                   for index in (1, 4, 5)), errors
    assert not checker.behavior_action_issues(plan, playable)
    print(f'PASS: unregistered warrior actions rejected in opening beat {debtor_beat} and three group actions')
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
    stage_defs = (SRC / 'Interaction/StoryProbe/StoryStageDefs.cs').read_text(encoding='utf-8-sig')
    defs = (SRC / 'Interaction/StoryProbe/StoryProbeDefs.cs').read_text(encoding='utf-8-sig')
    resolver = (SRC / 'Interaction/Intents/IntentRegistry.cs').read_text(encoding='utf-8-sig')
    stage_methods = '\n'.join(method(stage, name) for name in ('ShowInquiry', 'CloseInquiry', 'Stop', 'ReportFailure', 'FailureActorName', 'Tick', 'Execute', 'SayLine', 'PlayAction', 'OwnsActor'))
    performance = (SRC / 'AI/Actions/AtomicAction.cs').read_text(encoding='utf-8-sig')
    defs_methods = '\n'.join(method(defs, name) for name in ('PlayerGrowth', 'MeetsCheckRequirement', 'PreviewDialogueCheck', 'RollDialogueCheck', 'TakeCheckRoll'))
    dialogue = (SRC / 'Interaction/StoryProbe/StoryProbeDialogue.cs').read_text(encoding='utf-8-sig')
    code = r'''
using System; using System.Linq; using System.Collections.Generic; using System.IO; using Newtonsoft.Json;
enum StoryAbility {Force,Guile,Charm} enum NegotiationTactic {Threaten,Plead}
class Skill {public string StringId="Charm",Name="Charm";}
static class DefaultSkills {public static Skill Charm=new(),Roguery=new();}
static class DefaultCharacterAttributes {public static string Vigor="Vigor",Control="Control";}
class Hero {public static Hero MainHero=new();public int GetSkillValue(Skill s)=>20;public int GetAttributeValue(string attr)=>2;}
class StoryDialogueOption {public string PlayerKey,HintKey;public CheckData Check;public class CheckData {public string Target,Tactic,Capability;public int MinimumGrowth;}}
class HintHarness {__HINT_METHODS__ public static string Line(StoryDialogueOption o)=>DescribeOptionLine(o);}
class Agent {public static Agent Main=new();public float Health=100;public void SetActionChannel(int channel,ActionIndexCache action,bool ignorePriority,float blendInPeriod){}}
static class AgentControlHelper {public static int ActionWrites;public static bool SafeIsActive(Agent a)=>a!=null;public static void ForcePlayAction(Agent a,string action,float startProgress,float blendIn,float blendOutPeriodToNoAnim,bool restoreAfter){ActionWrites++;}}
struct ActionIndexCache {public string Name;public static ActionIndexCache act_none=new(){Name="act_none"};public static ActionIndexCache Create(string name)=>new(){Name=name};}
class MBActionSet {public bool IsValid=true;public static HashSet<string> Playable=new(){__PLAYABLE_ACTIONS__};public static MBActionSet GetActionSet(string id)=>new();public static bool CheckActionAnimationClipExists(MBActionSet set,ActionIndexCache action)=>Playable.Contains(action.Name);}
class ActionPresentationDef {}
class AgentActionPresentation {public AgentActionPresentation(Agent a,Mission m,string owner){}public bool Start(string action,ActionPresentationDef def)=>true;}
class AgentBrain {public Agent Owner=new();}
static class StoryLayoutRegistry {public static Layout Published=new();public class Layout {public ActionPresentationDef PresentationFor(string action)=>null;}}
class Mission {public static Mission Current=new();}
class GameEntity {} struct Vec3 {}
class StoryNpcDef {public string Id,NameKey,NameFallback;public Dictionary<string,int> AttributeRewards;}
class StoryRoll {public StoryAbility Ability;public string Capability,NpcId,Tactic;public Skill Skill;public float PlayerAttr,NpcAttr,Chance,Threshold,Roll;public bool Success,Eligible=true;public int MinimumGrowth,Growth;public string Describe()=>"roll";}
class IntentContext {public Hero Speaker;public IntentContext(Agent a,Hero speaker){Speaker=speaker;}}
static class SkillCheckSystem {public static Skill MapTacticToSkill(NegotiationTactic t)=>DefaultSkills.Charm;}
static class MathF {public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);}
static class MBRandom {public static int Calls;public static float RandomFloat {get{Calls++;return 1;}}}
static class DebugLogger {public static void Log(string text){}}
static class LWNTextHelper {public static Dictionary<string,string> Texts=new(){__FAILURE_TEXTS__};public static string ResolveText(string key,string fallback)=>key!=null&&Texts.TryGetValue(key,out var text)?text:key??fallback;public static string ResolveCompound(string key,string fallback,params (string variable,string value)[] variables){string text=Texts.TryGetValue(key,out var value)?value:fallback;foreach(var pair in variables)text=text.Replace("{"+pair.variable+"}",pair.value??"");return text;}}
static class StoryProbeState {public static int RuntimeToken;public static HashSet<string> Completed=new();public static bool IsQuestTurnedIn(string id)=>Completed.Contains(id);public static bool IsRuntimeCurrent(int token)=>token==RuntimeToken;public static State Data=new();public class State {public float RollOverride=-1;public string RollOverrideAbility="";}}
class StoryProbeMissionBehavior {public static StoryProbeMissionBehavior Current=new();public int Says;public Dictionary<string,Agent> Agents=new();public Agent GetAgent(string id)=>id!=null&&Agents.TryGetValue(id,out var a)?a:null;public Agent ResolveSpeechListener(string id)=>id=="player"?Agent.Main:GetAgent(id);public void SayOnAgent(Agent a,string key,string fb,Agent listener,bool forwardToNearby){Says++;}public Hero GetHero(string id)=>null;}
static class SingleRollResolver {__CHANCE__ public static bool Roll(float chance,float roll)=>chance>0&&roll>=1-chance;public static StoryRoll SimpleCompute(IntentContext c,NegotiationTactic t)=>new(){Chance=.5f};}
static class StoryProbeDefs {public static List<StoryNpcDef> Npcs=new(){new(){Id="butcher",AttributeRewards=new(){{"force",15}}}};public static StoryNpcDef GetNpc(string id)=>Npcs.FirstOrDefault(n=>n.Id==id);public static string AbilityId(StoryAbility a)=>a.ToString().ToLowerInvariant();public static string AbilityDisplayKey(StoryAbility a)=>"LWN_story_ability_"+AbilityId(a);public static bool TryParseDialogueTactic(string t,out NegotiationTactic value)=>Enum.TryParse(t,out value);public static string CapabilityName(string c)=>c;public static float PlayerAttr(StoryAbility a)=>20+PlayerGrowth(a);public static float NpcAttr(string id,StoryAbility a)=>70;public static bool TryGetCapability(string c,out StoryAbility a){a=StoryAbility.Force;return c=="Intimidate";}__DEFS__}
class StageBeat {public float At,BlendSeconds=.8f,Seconds;public string Cmd,Actor,Action,Listener,Case,TitleKey,TextKey,AcceptKey,CancelKey,Note;}
class StageScript {public string Script;public List<StageBeat> Beats=new();}
static class StoryStageDefs {static Dictionary<string,StageScript> _cache=new();public static string DesignDataFolder=__FIXTURE_FOLDER__;__LOAD__}
class PerformanceAction {public bool Ended,IsWalking,CanControl=true;public int Releases;public AgentBrain _brain=new();public Mission _mission=Mission.Current;public string OwnerToken="observation";private AgentActionPresentation _presentation;public void Release(string s){Ended=true;Releases++;}__PERFORMANCE_PLAY__}
static class StoryProbeTuning {public static Tuning Current=new();public class Tuning {public Performance Performance=new();}public class Performance {public float ActorReadyTimeoutSec=5;}}
class InquiryData {public Action Accept,Cancel;public bool AcceptShown,CancelShown;public string AcceptLabel,CancelLabel;public InquiryData(string t,string b,bool y,bool n,string a,string c,Action yes,Action no){Accept=yes;Cancel=no;AcceptShown=y;CancelShown=n;AcceptLabel=a;CancelLabel=c;}}
class InformationMessage {public string Text;public InformationMessage(string text){Text=text;}}
static class InformationManager {public static List<InquiryData> Shown=new();public static List<string> Messages=new();public static int Hides;public static bool ThrowMessage;public static void ShowInquiry(InquiryData d)=>Shown.Add(d);public static void HideInquiry()=>Hides++;public static void DisplayMessage(InformationMessage message){if(ThrowMessage)throw new Exception("message UI unavailable");Messages.Add(message.Text);}}
static class CameraService {public static string Owner;public static bool IsHeldBy(string owner)=>Owner==owner;public static MachineData Machine=new();public class MachineData {public float BlendProgress=1;}}
class StageHarness {
private bool _failureReported;
public bool _running=true,_actorsAcquired=true,_waitingInquiry,_waitingCamera,_openingCameraPending,OpeningCameraFailed;
public float _time,_readyWait,_openingCameraWait,_openingRetryAt,_returnBlendSeconds;public int _next,_runtimeToken,_inquiryRound;
public string _cameraOwner="test",_actorOwner="session",_actorInterrupted;public Mission _mission=Mission.Current;public Agent _frozenPlayer;
public Dictionary<string,PerformanceAction> _actors=new(){{"actor",new()}};public StageScript _script=new();public StageBeat _openingCameraBeat;public Func<bool> HoldWhile;
public int BeatCount=>_script.Beats.Count;public bool OpeningCameraReady=>true;public const float OpeningCameraRetryStepSec=.25f,OpeningCameraTimeoutSec=4;
public int Returns,Cuts;public int BodyLines=>_host.Says;private StoryProbeMissionBehavior _host=StoryProbeMissionBehavior.Current;public bool Frozen=true;
public bool AcquireAllowed=true,OpeningAllowed=true,CutAllowed=true;public void DisposeManaged(){_running=false;CloseInquiry();}public bool AcquireActors()=>AcquireAllowed;public bool TryCutOpeningCamera(bool quiet)=>OpeningAllowed;
void FreezePlayer(bool freeze){Frozen=freeze;_frozenPlayer=freeze?Agent.Main:null;}
void ReturnCamera(){Returns++;if(CameraService.Owner==_cameraOwner)CameraService.Owner=null;}
void CutCamera(StageBeat b){if(!CutAllowed)throw new InvalidOperationException("missing camera case");Cuts++;CameraService.Owner=_cameraOwner;CameraService.Machine.BlendProgress=0;_waitingCamera=true;}
void SpawnProp(StageBeat b,int i){}void PlaceActor(StageBeat b,int i){}
__STAGE__
}
class Program {
static int checks;static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;}
static StageBeat Prompt(float at)=>new(){At=at,Cmd="inquiry",TitleKey="title",TextKey="body",AcceptKey="yes",CancelKey="no"};
static StageHarness Setup(){CameraService.Owner=null;CameraService.Machine.BlendProgress=1;InformationManager.Shown.Clear();InformationManager.Messages.Clear();InformationManager.ThrowMessage=false;InformationManager.Hides=0;AgentControlHelper.ActionWrites=0;StoryProbeState.RuntimeToken=0;Mission.Current=new();StoryProbeMissionBehavior.Current=new();foreach(var id in new[]{__ACTORS__})StoryProbeMissionBehavior.Current.Agents[id]=new();var s=new StageHarness();foreach(var id in StoryProbeMissionBehavior.Current.Agents.Keys)s._actors[id]=new(){_brain=new(){Owner=StoryProbeMissionBehavior.Current.Agents[id]}};s._script.Beats.AddRange(new[]{Prompt(0),new StageBeat{At=.1f,Cmd="camera_cut"},new StageBeat{At=.2f,Cmd="actor_say",Actor="blocker",Listener="player",TextKey="line"},Prompt(.3f),new StageBeat{At=.4f,Cmd="camera_cut"},Prompt(.5f),new StageBeat{At=.6f,Cmd="camera_return"}});return s;}
static StageHarness Observe(bool missing,bool holdLast=false){var s=Setup();s._script.Beats=new(){__OBSERVATION_BEATS__};if(missing){StoryProbeMissionBehavior.Current.Agents.Remove("gate_sentry");s._script.Beats.First(b=>b.TextKey=="LWN_story_observe_guard_bark").Listener="gate_sentry";}for(int i=0;i<600&&s._running;i++){CameraService.Machine.BlendProgress=1;s.Tick(.1f);if(s._waitingInquiry){if(holdLast&&InformationManager.Shown.Count==6)return s;InformationManager.Shown.Last().Accept();}}return s;}
static void Main(){
var loaded=StoryStageDefs.Load("valid",true);Check(loaded!=null&&loaded.Beats.Count==__BEAT_COUNT__&&loaded.Beats.Last(b=>b.Cmd=="inquiry").CancelKey==null,"production loader accepts authored final inquiry with no CancelKey");
StoryProbeMissionBehavior.Current.Agents["guard"]=new();StoryProbeState.Completed.Clear();StoryProbeState.Data.RollOverride=100;int diceBefore=MBRandom.Calls;
LWNTextHelper.Texts["prefixed"]="[Intimidate] step aside";LWNTextHelper.Texts["plain"]="step aside";LWNTextHelper.Texts["note"]="ask for passage";
var option=new StoryDialogueOption{PlayerKey="prefixed",HintKey="note",Check=new(){Target="guard",Tactic="Threaten",Capability="Intimidate",MinimumGrowth=15}};
var optionHint=HintHarness.DescribeOptionHint(option,"guard");
if(HintHarness.Line(option)!="[Intimidate] step aside"||!optionHint.Contains("(20)")||!optionHint.Contains("(70)")||!optionHint.Contains("2")||!optionHint.Contains("0/15")||!optionHint.Contains("0.0%")||MBRandom.Calls!=diceBefore||StoryProbeState.Data.RollOverride!=100)throw new Exception("actual structured hint must show basis/prerequisite and never roll; authored prefix exactly once");
option.PlayerKey="plain";if(!HintHarness.Line(option).StartsWith("[Intimidate]"))throw new Exception("unprefixed check receives one label");
option.Check=null;if(HintHarness.Line(option)!="step aside"||HintHarness.DescribeOptionHint(option,"guard")!="ask for passage")throw new Exception("ordinary conversation retains authored line and purpose");
Console.WriteLine("PASS: actual structured hint, ability basis, disabled probability, one prefix and no-roll query checks");
foreach(string name in new[]{"no_accept","no_title","no_text"})Check(StoryStageDefs.Load(name,true)==null,"production loader rejects missing required inquiry field: "+name);
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
var second=InformationManager.Shown[1];second.Cancel();Check(!s._running&&!s.Frozen&&CameraService.Owner==null&&s._actors.Count==0&&InformationManager.Messages.Count==0,"cancel restores camera/player and releases actors without an error message");second.Accept();s.Tick(100);Check(s.Cuts==1,"cancelled callback never resumes playback");
s=Setup();s.Tick(1);first=InformationManager.Shown[0];s.Stop("external cancellation");Check(InformationManager.Hides==1,"external cancellation closes owned popup");s._running=true;s._actorOwner="new session";s._waitingInquiry=true;first.Accept();Check(s._waitingInquiry,"prior session callback is invalid");
s=Setup();s.Tick(1);first=InformationManager.Shown[0];StoryProbeState.RuntimeToken++;first.Accept();Check(s._waitingInquiry,"reset token rejects outstanding choice");s.Tick(.1f);Check(!s._running&&InformationManager.Messages.Count==0,"reset stops active observation without an error message");
s=Setup();s.Tick(1);InformationManager.Shown[0].Accept();s.Tick(.1f);CameraService.Owner="foreign";s.Tick(.1f);Check(!s._running&&!s.Frozen&&InformationManager.Messages.Single().Contains("took over the camera"),"camera ownership loss releases player and displays its cause");
s=Setup();s._actors["actor"].CanControl=false;s.Tick(6);Check(!s._running&&InformationManager.Shown.Count==0&&InformationManager.Messages.Single().Contains("ready in time"),"actor readiness timeout displays its cause before any inquiry");
s=Setup();s.Tick(1);InformationManager.Shown[0].Accept();s.Tick(.1f);CameraService.Machine.BlendProgress=1;s.Tick(1);InformationManager.Shown[1].Accept();s.Tick(.1f);CameraService.Machine.BlendProgress=1;s.Tick(1);InformationManager.Shown[2].Accept();s.Tick(1);Check(!s._running&&!s.Frozen&&s.Cuts==2,"full observation naturally ends and returns control");
Console.WriteLine($"PASS: {checks} extracted C# training and inquiry lifecycle checks (native camera/agents/UI are stubs)");
 s=Observe(false,true);var last=InformationManager.Shown.Last();
 Check(InformationManager.Shown.Take(5).All(d=>d.AcceptShown&&d.CancelShown&&d.Cancel!=null),"earlier inquiries retain both choices");
 Check(last.AcceptShown&&!last.CancelShown&&last.Cancel==null&&last.CancelLabel==""&&last.AcceptLabel=="Observation complete","final inquiry has exactly one localized finish button and no cancel callback");
 s.Tick(100);Check(s._running&&s._waitingInquiry&&s.Frozen&&s.Returns==0&&CameraService.Owner==s._cameraOwner,"final inquiry waits without returning the camera or player early");
 last.Accept();s.Tick(1);Check(!s._running&&!s.Frozen&&s._next==s.BeatCount&&InformationManager.Shown.Count==6&&s.Cuts==5&&s.BodyLines==7&&AgentControlHelper.ActionWrites==14&&InformationManager.Messages.Count==0,"finish button completes all 14 actions, five cameras, seven lines and six inquiries and returns control");
 Check(MBActionSet.Playable.Remove("act_bar_talk_to_2"),"regression target was registered");s=Observe(false);
 Check(!s._running&&!s.Frozen&&s._next==__FAILED_NEXT__&&s.Cuts==0&&InformationManager.Shown.Count==0&&AgentControlHelper.ActionWrites==__PRIOR_ACTIONS__,"missing warrior mapping fails before initial inquiry or camera");
 Check(InformationManager.Messages.Count==1&&InformationManager.Messages[0].Contains("beat __FAILED_BEAT__")&&InformationManager.Messages[0].Contains("debtor")&&InformationManager.Messages[0].Contains("act_bar_talk_to_2"),"action failure displays actor, action and beat exactly once");
 s.Stop("failure seen twice","LWN_story_stage_failure_action");s.Tick(100);Check(InformationManager.Messages.Count==1,"repeat stop/tick does not repeat the failure message");
 MBActionSet.Playable.Add("act_bar_talk_to_2");s=Observe(false);
 Check(s._next==__BEAT_COUNT__&&s.Cuts==5&&InformationManager.Shown.Count==6,"restoring native mapping lets the same authored timeline finish");
 s=Observe(true);Check(!s._running&&!s.Frozen&&InformationManager.Shown.Count==1&&s.Cuts==1&&s.BodyLines==0&&InformationManager.Messages.Single().Contains("gate_sentry"),"old missing listener displays its cause before guard description");
 s=Setup();s._actorsAcquired=false;s.AcquireAllowed=false;s.Tick(.1f);Check(!s._running&&InformationManager.Messages.Single().Contains("acquire its actors"),"failed actor acquisition displays its cause");
 s=Setup();s._openingCameraPending=true;s.OpeningAllowed=false;s.Tick(5);Check(!s._running&&s.OpeningCameraFailed&&InformationManager.Messages.Single().Contains("camera transition failed"),"opening camera retries report only final failure");
 s=Setup();s._script.Beats=new(){new(){At=0,Cmd="camera_cut",Case="__missing_story_camera__"}};s.CutAllowed=false;s.Tick(.1f);Check(!s._running&&!s.Frozen&&CameraService.Owner==null&&InformationManager.Messages.Single().Contains("camera transition failed"),"camera-cut exception displays its cause and releases control without taking the camera");
 s=Setup();s._actorInterrupted="actor:attacked";s.Tick(.1f);Check(!s._running&&InformationManager.Messages.Single().Contains("interrupted"),"actor interruption displays its cause");
 s=Setup();InformationManager.ThrowMessage=true;s.Stop("test failure","LWN_story_stage_failure_exception");Check(!s._running&&!s.Frozen&&s._actors.Count==0,"notification failure cannot undo actor/player cleanup");
 s=Setup();Mission.Current=new();s.Stop("stale failure","LWN_story_stage_failure_exception");Check(InformationManager.Messages.Count==0,"old mission cannot display failure in the new mission");
 s=Setup();StoryProbeState.RuntimeToken++;s.Stop("stale failure","LWN_story_stage_failure_exception");Check(InformationManager.Messages.Count==0,"invalidated runtime cannot display stale failure");
 LWNTextHelper.Texts=new(){__CN_FAILURE_TEXTS__};StoryProbeDefs.Npcs.Add(new(){Id="debtor",NameKey="LWN_story_name_debtor"});s=Setup();
 s.Stop("action failed","LWN_story_stage_failure_action",new(){Actor="debtor",Action="act_bar_talk_to_2"},22);
 Check(InformationManager.Messages.Single().Contains(__CN_ACTOR_NAME__)&&InformationManager.Messages[0].Contains("act_bar_talk_to_2")&&!InformationManager.Messages[0].Contains("{ACTOR}")&&!InformationManager.Messages[0].Contains("LWN_"),"Chinese failure resolves actual localized actor name and action without leaking placeholders or keys");
 Console.WriteLine("PASS: localized failure notifications, one-shot reporting, quiet cancellation/reset/completion and cleanup even when message UI fails");
 Console.WriteLine("PASS: full __BEAT_COUNT__-beat observation, 14 real PlayAction calls, missing warrior mapping and missing-listener negatives (native camera/agents/UI stubbed)");
}}
'''
    plan = json.loads((ROOT / 'ModuleData/DesignData/StoryScenePlan.json').read_text(encoding='utf-8'))
    beats = json.loads((ROOT / 'ModuleData/DesignData/StoryOpeningTimeline.json').read_text(encoding='utf-8'))['Beats']
    failed_beat = next(i for i, b in enumerate(beats) if b.get('Actor') == 'debtor' and b.get('Action') == 'act_bar_talk_to_2')
    code = code.replace('__BEAT_COUNT__', str(len(beats))).replace('__FAILED_BEAT__', str(failed_beat))
    code = code.replace('__FAILED_NEXT__', str(failed_beat + 1)).replace('__PRIOR_ACTIONS__', str(sum(b.get('Cmd') == 'actor_action' for b in beats[:failed_beat])))
    rendered = []
    for beat in beats:
        fields = ['%s=%sf' % (k, float(v)) if isinstance(v, (int, float)) else '%s=%s' % (k, json.dumps(v))
                  for k, v in beat.items() if k in ('At','BlendSeconds','Seconds','Cmd','Actor','Action','Listener','Case','TitleKey','TextKey','AcceptKey','CancelKey','Note')]
        rendered.append('new StageBeat{' + ','.join(fields) + '}')
    code = code.replace('__STAGE__', stage_methods).replace('__DEFS__', defs_methods).replace('__CHANCE__', method(resolver, 'SimpleChance'))
    code = code.replace('__PERFORMANCE_PLAY__', method(performance, 'PlayAction'))
    code = code.replace('__HINT_METHODS__', method(dialogue, 'DescribeOptionLine') + '\n' + method(dialogue, 'DescribeOptionHint'))
    texts = {s.get('id'): s.get('text') for s in ET.parse(ROOT / 'ModuleData/Languages/std_LivingWorldNpcs_strings.xml').iter('string')
             if s.get('id', '').startswith(('LWN_story_stage_', 'LWN_story_observe_', 'LWN_story_hint_', 'LWN_story_option_', 'LWN_story_odds_', 'LWN_story_ability_'))}
    code = code.replace('__FAILURE_TEXTS__', ','.join('{%s,%s}' % (json.dumps(k), json.dumps(v)) for k, v in texts.items()))
    cn_texts = {s.get('id'): s.get('text') for s in ET.parse(ROOT / 'ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml').iter('string')
                if s.get('id', '').startswith(('LWN_story_stage_', 'LWN_story_name_'))}
    code = code.replace('__CN_FAILURE_TEXTS__', ','.join('{%s,%s}' % (json.dumps(k), json.dumps(v)) for k, v in cn_texts.items()))
    code = code.replace('__CN_ACTOR_NAME__', json.dumps(cn_texts['LWN_story_name_debtor']))
    code = code.replace('__PLAYABLE_ACTIONS__', ','.join(json.dumps(a) for a in sorted(human_playable(_game_root(str(ROOT)), str(ROOT)))))
    code = code.replace('__OBSERVATION_BEATS__', ','.join(rendered)).replace('__ACTORS__', ','.join(json.dumps(a) for a in plan['Stages']['second_meet']['Actors']))
    OUT.mkdir(parents=True, exist_ok=True)
    fixture = OUT / 'scripts'
    fixture.mkdir(exist_ok=True)
    authored = json.loads((ROOT / 'ModuleData/DesignData/StoryOpeningTimeline.json').read_text(encoding='utf-8'))
    (fixture / 'valid.json').write_text(json.dumps(authored), encoding='utf-8')
    for name, field in [('no_accept', 'AcceptKey'), ('no_title', 'TitleKey'), ('no_text', 'TextKey')]:
        invalid = copy.deepcopy(authored)
        del next(b for b in reversed(invalid['Beats']) if b['Cmd'] == 'inquiry')[field]
        (fixture / (name + '.json')).write_text(json.dumps(invalid), encoding='utf-8')
    code = code.replace('__FIXTURE_FOLDER__', json.dumps(str(fixture))).replace(
        '__LOAD__', '\n'.join(method(stage_defs, name) for name in ('Load', 'StableSortByAt')))
    (OUT / 'Program.cs').write_text(code, encoding='utf-8')
    newtonsoft = Path(_game_root(str(ROOT))) / 'bin/Win64_Shipping_Client/Newtonsoft.Json.dll'
    (OUT / 'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>%s</HintPath></Reference></ItemGroup></Project>' % escape(str(newtonsoft)), encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)


if __name__ == '__main__':
    data_checks()
    runtime_checks()

"""Execute production topic selection/end callbacks without native conversation UI."""
from pathlib import Path
import subprocess
from test_story_feedback import method

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod/Interaction/StoryProbe'
OUT = ROOT / 'Debug/offline/story_topic_tests'


def main():
    model = (SRC / 'StoryDialogueDefs.cs').read_text(encoding='utf-8-sig')
    runtime = (SRC / 'StoryDialogueRuntime.cs').read_text(encoding='utf-8-sig')
    code = r'''
using System;using System.Linq;using System.Collections.Generic;
class StoryDialogueEffect {public string Id,Commit="on_select";}
class StoryDialogueCheck {public string Capability,Target,Success,Fail;public int MinimumGrowth;}
class StoryDialogueOption {public string Id,NextNode;public StoryDialogueCheck Check;public List<string> Requires=new();public List<StoryDialogueEffect> Effects=new();}
class StoryDialogueNodeDef {public string Id,OptionsFrom;public List<string> Requires=new();public List<StoryDialogueOption> Options=new();public List<StoryDialogueEffect> OnEnter=new();}
class StoryDialogueDef {public string DialogueId="fixture";public List<StoryDialogueNodeDef> Nodes=new();public StoryDialogueNodeDef FindNode(string id)=>Nodes.FirstOrDefault(n=>n.Id==id);__MODEL__}
class Request {public int RuntimeToken;public string ActorId="teacher";}
class Trigger {public string Id="talk",Purpose="talk";}
class StoryDialogueSession {public bool Ended,Failed;public string EndReason,CurrentNode;public StoryDialogueDef Definition;public Request Request=new();public Trigger Trigger=new();public HashSet<string> EnteredNodes=new(),SelectedOptions=new();}
class StoryDialogueContext {public string DialogueId,TriggerId,Purpose,ActorId;}
class StoryProbeMissionBehavior {public static StoryProbeMissionBehavior Current=new();public bool Active=true;public object GetHero(string actor)=>new();public object GetAgent(string actor)=>new();}
static class StoryProbeState {public static int RuntimeToken;}
static class StoryProbeDefs {public static bool Trained=true;public static bool MeetsCheckRequirement(string cap,int growth)=>Trained;}
static class StoryDialogueFacts {public static bool Permit=true;public static bool TryEvaluateAll(IEnumerable<string> conditions,StoryDialogueContext ctx,out bool value,out string error,out List<string> trail){value=Permit||!conditions.Contains("permit");error=null;trail=new();return true;}}
static class Runtime {
 public static StoryDialogueSession _current;public static List<string> Applied=new();public static HashSet<string> Receipts=new();
 static bool CommitEffects(List<StoryDialogueEffect> effects,string stage,string owner){foreach(var e in effects.Where(e=>e.Commit==stage))if(Receipts.Add(owner+e.Id))Applied.Add(e.Id);return true;}
 __SELECT__
 __END__
 public static void Finish()=>CommitSessionEndEffects(_current);
}
class Program {
 static int count;static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
 static void Main(){
 var menu=new StoryDialogueNodeDef{Id="topics",OnEnter=new(){new(){Id="menu-entry",Commit="on_session_end"}},Options=new(){new(){Id="ask",NextNode="reply",Effects=new(){new(){Id="pay",Commit="on_select"},new(){Id="selected-end",Commit="on_session_end"}}},new(){Id="repeat",NextNode="answer"},new(){Id="unselected",NextNode="reply",Effects=new(){new(){Id="unselected-end",Commit="on_session_end"}}}}};
 var answer=new StoryDialogueNodeDef{Id="answer",OptionsFrom="topics",OnEnter=new(){new(){Id="answer-end",Commit="on_session_end"}}};var reply=new StoryDialogueNodeDef{Id="reply"};
 var def=new StoryDialogueDef{Nodes=new(){menu,answer,reply}};Runtime._current=new(){Definition=def,CurrentNode="answer"};
 Check(def.OptionsNode(answer)==menu&&def.OptionsFor(answer)==menu.Options,"answer resolves direct topic options");
 Check(Runtime.NotifyOptionSelected("answer","ask",out _),"select shared topic from displayed answer");Check(Runtime._current.CurrentNode=="answer"&&!Runtime._current.EnteredNodes.Contains("topics"),"selection never enters or displays source menu");Check(Runtime._current.SelectedOptions.Contains("topics/ask"),"canonical menu receipt survives inheritance");
 Runtime.Finish();Check(Runtime.Applied.Contains("answer-end")&&Runtime.Applied.Contains("selected-end")&&!Runtime.Applied.Contains("menu-entry")&&!Runtime.Applied.Contains("unselected-end"),"only displayed answer and selected branch end effects run");
 Runtime._current.CurrentNode="topics";Check(Runtime.NotifyOptionSelected("topics","ask",out _),"ordinary menu still works");Check(Runtime.Applied.Count(x=>x=="pay")==1,"shared and ordinary selections use the same economic receipt");
 Runtime._current.CurrentNode="answer";Check(!Runtime.NotifyOptionSelected("answer","repeat",out _),"inherited self-repeat is rejected");menu.Options.Add(new(){Id="failed-repeat",Check=new(){Success="reply",Fail="answer"}});Check(!Runtime.NotifyOptionSelected("answer","failed-repeat",out _),"check failure cannot loop into the same inherited answer");Check(!Runtime.NotifyOptionSelected("topics","ask",out _),"stale displayed node rejected");menu.Requires.Add("permit");StoryDialogueFacts.Permit=false;Check(!Runtime.NotifyOptionSelected("answer","ask",out _),"source menu eligibility rechecked at submit");StoryDialogueFacts.Permit=true;
 answer.Requires.Add("permit");StoryDialogueFacts.Permit=false;Check(!Runtime.NotifyOptionSelected("answer","ask",out _),"answer eligibility rechecked");StoryDialogueFacts.Permit=true;
 StoryProbeState.RuntimeToken++;Check(!Runtime.NotifyOptionSelected("answer","ask",out _),"stale runtime token rejected");StoryProbeState.RuntimeToken--;
 Runtime._current.Failed=true;Check(!Runtime.NotifyOptionSelected("answer","ask",out _),"failed session cannot submit another effect");
 Console.WriteLine("PASS: "+count+" production topic selection/end checks (native UI is a test boundary)");
 }
}
'''
    code = code.replace('__MODEL__', method(model, 'OptionsNode')+'\n'+method(model, 'OptionsFor'))
    code = code.replace('__SELECT__', method(runtime, 'NotifyOptionSelected'))
    code = code.replace('__END__', method(runtime, 'CommitSessionEndEffects'))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'Program.cs').write_text(code, encoding='utf-8')
    (OUT / 'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)


if __name__ == '__main__':
    main()

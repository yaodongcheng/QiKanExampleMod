"""Execute real perception, idle and hold code across gaze handoffs; native engine is stubbed."""
from pathlib import Path
import argparse
import json
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ExampleModVS/ExampleMod/ExampleMod'
OUT = ROOT / 'Debug/offline/story_perception_tests'


def block(source, pattern):
    match = re.search(pattern, source)
    assert match, pattern
    start = source.index('{', match.end())
    end, depth = start + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[match.start():end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--selftest', action='store_true', help='Prove regressions catch the previous gaze handoff defects')
    args = parser.parse_args()
    core = (SRC / 'Core/AgentControlHelper.cs').read_text(encoding='utf-8-sig')
    sight = (SRC / 'AI/NpcSightSystem.cs').read_text(encoding='utf-8-sig')
    street = (SRC / 'Interaction/StoryProbe/StreetLifeDirector.cs').read_text(encoding='utf-8-sig')
    atomic = (SRC / 'AI/Actions/AtomicAction.cs').read_text(encoding='utf-8-sig')
    helpers = '\n'.join(block(core, r'public static void ' + name + r'\([^\n]*\)')
                        for name in ('StopMovement', 'HoldInPlace', 'LookAtAgent', 'StopLooking', 'SetBodyDirection'))
    cone = block(sight, r'public static bool IsTargetInViewCone\([\s\S]*?\)')
    cone += '\n' + block(sight, r'private static float ViewConeDot\([\s\S]*?\)')
    idle = block(street, r'public sealed class StreetIdleAction : IAtomicAction')
    stay = block(atomic, r'public class StayAction : IAtomicAction')
    stubs = r'''
using System; using System.Collections.Generic; using TaleWorlds.Library; using TaleWorlds.MountAndBlade; using LivingWorldNpcs;
namespace TaleWorlds.Library {
 public struct Vec2 { public float x,y;public Vec2(float x,float y){this.x=x;this.y=y;}public static Vec2 Zero=>new(0,0);
 public float LengthSquared=>x*x+y*y;public void Normalize(){float n=MathF.Sqrt(LengthSquared);x/=n;y/=n;}public Vec2 Normalized(){var v=this;v.Normalize();return v;}
 public static float DotProduct(Vec2 a,Vec2 b)=>a.x*b.x+a.y*b.y;public static Vec2 operator -(Vec2 a,Vec2 b)=>new(a.x-b.x,a.y-b.y); }
 public struct Vec3 {public float x,y,z;public Vec3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public Vec2 AsVec2=>new(x,y);public float Length=>MathF.Sqrt(x*x+y*y+z*z);
 public float Distance(Vec3 b)=>(this-b).Length;public Vec3 NormalizedCopy(){float n=Length;return new(x/n,y/n,z/n);}public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);}
 public class Rotation {public Vec3 f=new(1,0,0);} public class Frame {public Rotation rotation=new();}
}
namespace TaleWorlds.MountAndBlade {
 public class Agent {public static Agent Main;public int Index;public string Name="npc";public bool Active=true,CrouchMode;public float Health=100;
 public Vec3 Position,Velocity,LookDirection=new(1,0,0);public Frame Frame=new();public Vec2 MovementInputVector;public int BodyWrites,GazeWrites,GazeClears,StopWrites;public float SpeedLimit;
 private Agent _look;public Agent GetLookAgent()=>_look;public void SetLookAgent(Agent a){_look=a;if(a==null)GazeClears++;else GazeWrites++;}
 public void SetMovementDirection(Vec2 d){BodyWrites++;Frame.rotation.f=new(d.x,d.y,0);}public void DisableScriptedMovement(){StopWrites++;}
 public void SetMaximumSpeedLimit(float s,bool _){SpeedLimit=s;}[Flags]public enum AIScriptedFrameFlags{None=0,InConversation=1}public AIScriptedFrameFlags Flags;public AIScriptedFrameFlags GetScriptedFlags()=>Flags;}
 public class Mission {public static Mission Current;public float CurrentTime;}
}
namespace LivingWorldNpcs {
 public interface IAtomicAction {void OnStart(Agent a);void OnTick(Agent a,float dt);void OnEnd(Agent a);bool IsFinished(Agent a);void RequestInterrupt();string GetNarration(Agent a);}
 public class AgentBrain {public Agent Owner;public IAtomicAction CurrentAction;public bool HasDutyPoint=true,IsPerforming,IsStoryIntercepting,IsInCombat,IsBound,IsStatusHeld,IsStunned,IsMovingOrTurning;
 public Vec2 DutyDirection=new(1,0);public Vec3 DutyPoint;public static bool IsRoped(Agent a)=>false;public static bool IsHeldByStatus(Agent a)=>false;public static bool IsKnockedOut(Agent a)=>false;}
 public static class AgentAIController {public static Dictionary<Agent,AgentBrain> Brains=new();public static AgentBrain GetBrainForAgent(Agent a)=>Brains.GetValueOrDefault(a);}
 public class Settings {public static Settings Instance=new();public bool Disabled,ShowPerformanceFacingDebugMessages;public bool IsInteractionDisabled()=>Disabled;}
 public class NoiseTuning {public float MaxAgeSec=1.5f,PublishIntervalSec=.4f,RunSpeedMps=3.2f,RunRadiusMeters=15,WalkRadiusMeters=6,GazeRadiusMeters=10,TurnHoldSec=6;public bool CrouchSilent=true;}
 public class AmbientTuning {public float FirstDelayMinSec=1,FirstDelayMaxSec=2,GapMinSec=1,GapMaxSec=2;}
 public class PerformanceTuning {public float AmbientPoseDurationSec=1;}
 public class StoryProbeTuning {public static StoryProbeTuning Current=new();public NoiseTuning Noise=new();public AmbientTuning Ambient=new();public PerformanceTuning Performance=new();}
 public class StreetLifeDef {public string NpcId,Listener;public string[] Actions,LineKeys,LineFallbacks;}
 public class Director {public bool Stopped;}
 public class StoryProbeMissionBehavior {public static StoryProbeMissionBehavior Current;public bool Active=true;public Director Director=new();public Agent Listener;public int Says;
 public Agent ResolveSpeechListener(string id)=>Listener;public void SayOnAgent(Agent a,string k,string f,Agent listener){Says++;}}
 public static class StoryProbeDialogue {public static string ResolveNpcId(Agent a)=>a==null?null:"npc";}
 public static class StoryPoseTrace {public static bool Any;public static bool IsOn(string s)=>false;public static void Anim(Agent a,string s,string owner){}public static void Turn(Agent a,string s,string mode){}}
 public static class DebugLogger {public static void Log(string s){}}
 public static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a?.Active==true;public static void ForcePlayAction(Agent a,string s){}public static void ClearPose(Agent a){}
 public static void StopAndReset(Agent a){}public static void FaceToActor(Agent a,Agent b){} __HELPERS__ }
 public static class NpcSightSystem {public const float DefaultFovDegrees=120f;private static float ViewConeThreshold(float degrees)=>(float)Math.Cos(degrees*Math.PI/360); __CONE__ }
 __IDLE__
 __STAY__
}
class Program {
 static int checks;static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static (AgentBrain,Agent) Setup(IAtomicAction action=null){Mission.Current=new();NpcPerception.Reset();AgentAIController.Brains.Clear();var a=new Agent{Index=2};var b=new AgentBrain{Owner=a,CurrentAction=action};AgentAIController.Brains[a]=b;Agent.Main=new Agent{Index=1,Position=new(20,0,0),CrouchMode=true};return(b,a);}
 static void Tick(AgentBrain b,float now){Mission.Current.CurrentTime=now;NpcPerception.Tick(.1f,new[]{b});}
 static void Main(){
 var(b,a)=Setup();a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null&&a.BodyWrites==0,"unregistered player gaze beyond radius must be released without body turn");
 (b,a)=Setup(new StayAction(null,false));a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null,"plain Stay accepts perception handoff and removes orphan gaze");
 (b,a)=Setup();Agent.Main.Position=new(-3,0,0);a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null&&a.BodyWrites==0,"unregistered gaze behind body must release while crouching silently");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);a.SetLookAgent(null);int writes=a.GazeWrites;Tick(b,1);Check(a.GetLookAgent()==Agent.Main&&a.GazeWrites==writes+1,"idle must reacquire gaze after another action clears native target");
 Agent.Main.Position=new(11,0,0);Tick(b,2);Check(a.GetLookAgent()==null,"reacquired player gaze releases when player leaves ten metres");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);var other=new Agent{Index=3};a.SetLookAgent(other);Agent.Main.Position=new(20,0,0);int clears=a.GazeClears;Tick(b,1);Check(a.GetLookAgent()==other&&a.GazeClears==clears,"old perception record must not clear another system's new listener");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);Agent.Main=null;Tick(b,1);Check(a.GetLookAgent()==null,"removed main agent releases actual owned target");
 (b,a)=Setup();a.SetLookAgent(other);Tick(b,0);Check(a.GetLookAgent()==other,"unowned NPC listener remains unchanged outside perception range");
 (b,a)=Setup();a.SetLookAgent(Agent.Main);b.IsPerforming=true;Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"performance can keep its intentional listener outside ordinary gaze range");
 b.IsPerforming=false;b.CurrentAction=new StayAction(null,false);Tick(b,1);Check(a.GetLookAgent()==null,"handoff from completed performance removes its unregistered distant player target");
 (b,a)=Setup();a.SetLookAgent(Agent.Main);a.Flags=Agent.AIScriptedFrameFlags.InConversation;Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"active dialogue remains owner of gaze");
 a.Flags=Agent.AIScriptedFrameFlags.None;Tick(b,1);Check(a.GetLookAgent()==null,"return from formal dialogue restores range gate");
 (b,a)=Setup(new StayAction(Agent.Main));a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"explicit action target cannot be cleared as plain ambient gaze");
 (b,a)=Setup();a.SetLookAgent(Agent.Main);b.IsMovingOrTurning=true;b.CurrentAction=new Moving();Tick(b,0);Check(a.GetLookAgent()==Agent.Main&&a.BodyWrites==0,"moving action retains its own distant target");
 foreach(int mode in new[]{0,1,2,3,4}){(b,a)=Setup();a.SetLookAgent(Agent.Main);b.IsInCombat=mode==0;b.IsBound=mode==1;b.IsStatusHeld=mode==2;b.IsStunned=mode==3;b.IsStoryIntercepting=mode==4;Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"busy action preserves gaze mode "+mode);}
 (b,a)=Setup();var idle=new StreetIdleAction("rival",new StreetLifeDef{Actions=Array.Empty<string>()},new Vec2(1,0));b.CurrentAction=idle;a.SetLookAgent(Agent.Main);idle.OnStart(a);Check(a.GetLookAgent()==null,"real StreetIdle OnStart clears predecessor gaze");
 idle.OnTick(a,.2f);idle.OnTick(a,.2f);Check(a.StopWrites==1&&a.BodyWrites==0,"real idle hold resets movement once and never writes body direction toward gaze");
 a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null,"real idle accepts unregistered gaze cleanup");
 (b,a)=Setup();Agent.Main.Position=new(10,0,0);Tick(b,0);Check(a.GetLookAgent()==Agent.Main&&a.BodyWrites==0,"ten metre boundary allows head only");
 Agent.Main.Position=new(10.01f,0,0);Tick(b,1);Check(a.GetLookAgent()==null,"just outside boundary releases head");
 (b,a)=Setup();Agent.Main.Position=new(0,11,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(2,0,0);Tick(b,0);Check(a.BodyWrites==0,"walking at eleven metres cannot turn body");
 Agent.Main.Velocity=new(4,0,0);Tick(b,1);Check(a.BodyWrites==1&&NpcPerception.Describe(a).Contains("noiseRadius=15.0"),"running inside fifteen metres turns body with diagnostic source and radius");
 Agent.Main.Position=new(0,16,0);Tick(b,4);Check(a.BodyWrites==1&&a.GetLookAgent()==null,"running beyond fifteen metres stops requesting body tracking");
 Tick(b,8);Check(a.BodyWrites==2&&a.Frame.rotation.f.x>.99,"sound expiry returns body to duty direction");
 (b,a)=Setup();Agent.Main.Position=new(0,20,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(4,0,0);Tick(b,0);Tick(b,1);Check(a.BodyWrites==0&&a.GetLookAgent()==null,"running at twenty metres causes no body or head tracking");
 (b,a)=Setup();var host=new StoryProbeMissionBehavior{Listener=other};StoryProbeMissionBehavior.Current=host;other.Position=new(20,0,0);
 idle=new StreetIdleAction("rival",new StreetLifeDef{Listener="widow",Actions=new[]{"act_argue"},LineKeys=new[]{"line"},LineFallbacks=new[]{"fallback"}},new Vec2(1,0));
 var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 typeof(StreetIdleAction).GetField("_rng",hidden).SetValue(idle,new BarkRandom());var pose=typeof(StreetIdleAction).GetMethod("PlayPose",hidden);
 pose.Invoke(idle,new object[]{a});Check(host.Says==0,"distant actual listener cannot trigger an ambient addressed line");
 other.Position=new(10,0,0);pose.Invoke(idle,new object[]{a});Check(host.Says==1,"ambient line addresses nearby listener at the range boundary");
 Check(a.BodyWrites==0&&a.GetLookAgent()==null,"ambient speech has no body or gaze control side effect");
 Console.WriteLine("PASS: "+checks+" actual perception/idle/hold handoff checks (native engine is stubbed)");
 }
 class Moving:IAtomicAction {public void OnStart(Agent a){}public void OnTick(Agent a,float d){}public void OnEnd(Agent a){}public bool IsFinished(Agent a)=>false;public void RequestInterrupt(){}public string GetNarration(Agent a)=>null;}
 class BarkRandom:Random {public override double NextDouble()=>.1;public override int Next(int max)=>0;}
}
'''
    program = stubs.replace('__HELPERS__', helpers).replace('__CONE__', cone).replace('__IDLE__', idle).replace('__STAY__', stay)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'Program.cs').write_text(program, encoding='utf-8')
    perception = str(SRC / 'AI/NpcPerception.cs').replace('&', '&amp;')
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="' + perception + '"/></ItemGroup></Project>'
    (OUT / 'Harness.csproj').write_text(project, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)
    if args.selftest:
        source = (SRC / 'AI/NpcPerception.cs').read_text(encoding='utf-8-sig')
        mutants = [
            ('distance handoff', source.replace('else if (wasLooking || idle && actualTarget != null && actualTarget == player)', 'else if (wasLooking)'), program, 'unregistered player gaze beyond radius'),
            ('target cache handoff', source.replace('if (wasLooking && actualTarget != ownedTarget)', 'if (false)'), program, 'idle must reacquire gaze'),
            ('idle action handoff', source, program.replace(idle, idle.replace('AgentControlHelper.StopLooking(agent);', '')), 'real StreetIdle OnStart clears predecessor gaze'),
        ]
        try:
            for label, variant, fixture, error in mutants:
                (OUT / 'Perception.cs').write_text(variant, encoding='utf-8')
                (OUT / 'Program.cs').write_text(fixture, encoding='utf-8')
                (OUT / 'Harness.csproj').write_text(project.replace(perception, 'Perception.cs'), encoding='utf-8')
                result = subprocess.run(['dotnet', 'run', '--project', str(OUT / 'Harness.csproj'), '--verbosity', 'quiet'], capture_output=True, text=True, encoding='utf-8', errors='replace')
                assert result.returncode != 0 and error in result.stdout + result.stderr, (label, result.stdout, result.stderr)
                print('PASS: negative regression reproduces ' + label)
        finally:
            (OUT / 'Program.cs').write_text(program, encoding='utf-8')
            (OUT / 'Harness.csproj').write_text(project, encoding='utf-8')
    cast = json.loads((ROOT / 'ModuleData/DesignData/StoryProbeCast.json').read_text(encoding='utf-8'))
    rival = next(d for d in cast['StreetLife'] if d['NpcId'] == 'rival')
    assert rival['Listener'] == 'widow', 'rival ambient argument must address the actual troupe leader'
    print('PASS: rival ambient listener matches the actual dispute partner')


if __name__ == '__main__':
    main()

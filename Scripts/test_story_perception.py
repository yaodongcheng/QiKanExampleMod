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
                        for name in ('StopMovement', 'HoldInPlace', 'LookAtAgent', 'StopLooking', 'SetBodyDirection', 'LookAtPointOfInterest', 'StopLookingAtPointOfInterest'))
    cone = block(sight, r'public static bool IsTargetInViewCone\([\s\S]*?\)')
    cone += '\n' + block(sight, r'private static float ViewConeDot\([\s\S]*?\)')
    idle = block(street, r'public sealed class StreetIdleAction : IAtomicAction')
    stay = block(atomic, r'public class StayAction : IAtomicAction')
    stubs = r'''
using System; using System.Collections.Generic; using TaleWorlds.Library; using TaleWorlds.MountAndBlade; using LivingWorldNpcs;
namespace TaleWorlds.Library {
 public struct Vec2 { public float x,y;public Vec2(float x,float y){this.x=x;this.y=y;}public static Vec2 Zero=>new(0,0);
 public float LengthSquared=>x*x+y*y;public float RotationInRadians=>MathF.Atan2(-x,y);public void Normalize(){float n=MathF.Sqrt(LengthSquared);x/=n;y/=n;}public Vec2 Normalized(){var v=this;v.Normalize();return v;}
 public static float DotProduct(Vec2 a,Vec2 b)=>a.x*b.x+a.y*b.y;public static Vec2 operator -(Vec2 a,Vec2 b)=>new(a.x-b.x,a.y-b.y); }
 public struct Vec3 {public float x,y,z;public Vec3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public Vec2 AsVec2=>new(x,y);public float Length=>MathF.Sqrt(x*x+y*y+z*z);
 public float Distance(Vec3 b)=>(this-b).Length;public Vec3 NormalizedCopy(){float n=Length;return new(x/n,y/n,z/n);}public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);}
 public class Rotation {public Vec3 f=new(1,0,0);} public class Frame {public Rotation rotation=new();}
}
namespace TaleWorlds.MountAndBlade {
 public class Team {public bool Enemy;public bool IsEnemyOf(Team other)=>Enemy;}
 public class Agent {public Team Team=new();public static Agent Main;public int Index;public string Name="npc";public bool Active=true,CrouchMode;public float Health=100;
 public Vec3 Position,Velocity,LookDirection=new(1,0,0);public Frame Frame=new();public Vec2 MovementInputVector;public int BodyWrites,GazeWrites,GazeClears,StopWrites,StandingWrites,PoseWrites,HeadWrites,HeadClears;public float SpeedLimit;public Vec2? StandingDirection;public Vec3? HeadPoint;
 private Agent _look;public Agent GetLookAgent()=>_look;public void SetLookAgent(Agent a){_look=a;if(a==null)GazeClears++;else GazeWrites++;}
 public void SetMovementDirection(Vec2 d){BodyWrites++;Frame.rotation.f=new(d.x,d.y,0);}public void DisableScriptedMovement(){StopWrites++;StandingDirection=null;Flags=AIScriptedFrameFlags.None;}
 public Vec3 GetWorldPosition()=>Position;public void SetScriptedPositionAndDirection(ref Vec3 p,float d,bool delay,AIScriptedFrameFlags flags){StandingWrites++;StandingDirection=new(-MathF.Sin(d),MathF.Cos(d));Flags=flags;}
 // Observed 19:15 log: persistent object gaze wins even when ConsiderRotation is present.
 public void NativeStep(){var d=_look!=null?(_look.Position-Position).AsVec2.Normalized():StandingDirection??Frame.rotation.f.AsVec2;Frame.rotation.f=new(d.x,d.y,0);}
 public Vec3 GetEyeGlobalPosition()=>new(Position.x,Position.y,Position.z+1.7f);public void SetLookToPointOfInterest(Vec3 point){HeadWrites++;HeadPoint=point;}public void DisableLookToPointOfInterest(){HeadClears++;HeadPoint=null;}
 public void SetMaximumSpeedLimit(float s,bool _){SpeedLimit=s;}[Flags]public enum AIScriptedFrameFlags{None=0,InConversation=1,DoNotRun=2,NoAttack=4}public AIScriptedFrameFlags Flags;public AIScriptedFrameFlags GetScriptedFlags()=>Flags;}
 public class Mission {public static Mission Current;public float CurrentTime;}
}
namespace LivingWorldNpcs {
 public interface IAtomicAction {void OnStart(Agent a);void OnTick(Agent a,float dt);void OnEnd(Agent a);bool IsFinished(Agent a);void RequestInterrupt();string GetNarration(Agent a);}
 public class AgentBrain {public Agent Owner;public IAtomicAction CurrentAction;public bool HasDutyPoint=true,IsPerforming,IsStoryIntercepting,IsInCombat,IsBound,IsStatusHeld,IsStunned,IsMovingOrTurning;
 public Vec2 DutyDirection=new(1,0);public Vec3 DutyPoint;public static bool IsRoped(Agent a)=>false;public static bool IsHeldByStatus(Agent a)=>false;public static bool IsKnockedOut(Agent a)=>false;}
 public static class AgentAIController {public static Dictionary<Agent,AgentBrain> Brains=new();public static AgentBrain GetBrainForAgent(Agent a)=>Brains.GetValueOrDefault(a);}
 public class Settings {public static Settings Instance=new();public bool Disabled,ShowPerformanceFacingDebugMessages;public bool IsInteractionDisabled()=>Disabled;}
 public class NoiseTuning {public float MaxAgeSec=1.5f,PublishIntervalSec=.4f,RunSpeedMps=3.2f,RunRadiusMeters=15,WalkRadiusMeters=6,GazeRadiusMeters=5,OrdinaryFootstepRadiusMeters=2,HostileGazeRadiusMeters=10,TurnHoldSec=6;public bool CrouchSilent=true;}
 public class AmbientTuning {public float ListenerRadiusMeters=10;public float FirstDelayMinSec=1,FirstDelayMaxSec=2,GapMinSec=1,GapMaxSec=2;}
 public class PerformanceTuning {public float AmbientPoseDurationSec=1;}
 public class StoryProbeTuning {public static StoryProbeTuning Current=new();public NoiseTuning Noise=new();public AmbientTuning Ambient=new();public PerformanceTuning Performance=new();}
 public class StreetLifeDef {public string NpcId,Listener;public string[] Actions,LineKeys,LineFallbacks;}
 public class Director {public bool Stopped;}
 public class StoryProbeMissionBehavior {public static StoryProbeMissionBehavior Current;public bool Active=true;public Director Director=new();public Agent Listener;public int Says;
 public Agent ResolveSpeechListener(string id)=>Listener;public void SayOnAgent(Agent a,string k,string f,Agent listener){Says++;}}
 public static class StoryProbeDialogue {public static string ResolveNpcId(Agent a)=>a==null?null:"npc";}
 public static class StoryPoseTrace {public static bool Any;public static bool IsOn(string s)=>false;public static void Anim(Agent a,string s,string owner){}public static void Turn(Agent a,string s,string mode){}}
 public static class DebugLogger {public static void Log(string s){}}
 public static class AgentControlHelper {public static bool SafeIsActive(Agent a)=>a?.Active==true;public static void ForcePlayAction(Agent a,string s){a.PoseWrites++;}public static void ClearPose(Agent a){}
 public static void StopAndReset(Agent a){StopMovement(a);}public static void FaceToActor(Agent a,Agent b){} __HELPERS__ }
 public static class NpcSightSystem {public const float DefaultFovDegrees=120f;private static float ViewConeThreshold(float degrees)=>(float)Math.Cos(degrees*Math.PI/360); __CONE__ }
 __IDLE__
 __STAY__
}
class Program {
 static int checks;static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static (AgentBrain,Agent) Setup(IAtomicAction action=null){Mission.Current=new();NpcPerception.Reset();AgentAIController.Brains.Clear();var a=new Agent{Index=2};var b=new AgentBrain{Owner=a,CurrentAction=action};AgentAIController.Brains[a]=b;Agent.Main=new Agent{Index=1,Position=new(20,0,0),CrouchMode=true};return(b,a);}
 static void Tick(AgentBrain b,float now){Mission.Current.CurrentTime=now;NpcPerception.Tick(.1f,new[]{b});}
 static bool HeadPlayer(Agent a)=>a.GetLookAgent()==null&&a.HeadPoint.HasValue&&NpcPerception.DescribeHeadTarget(a)=="player";
 static bool HeadNone(Agent a)=>a.GetLookAgent()==null&&!a.HeadPoint.HasValue&&NpcPerception.DescribeHeadTarget(a)=="none";
 static void Main(){
 var(b,a)=Setup();a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null&&a.BodyWrites==0,"unregistered player gaze beyond radius must be released without body turn");
 (b,a)=Setup(new StayAction(null,false));a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null,"plain Stay accepts perception handoff and removes orphan gaze");
 (b,a)=Setup();Agent.Main.Position=new(-3,0,0);a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null&&a.BodyWrites==0,"unregistered gaze behind body must release while crouching silently");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);a.DisableLookToPointOfInterest();int writes=a.HeadWrites;Tick(b,1);Check(HeadPlayer(a)&&a.HeadWrites==writes+1,"idle must refresh eye point after native target clears");
 Agent.Main.Position=new(11,0,0);Tick(b,2);Check(HeadNone(a),"reacquired eye point releases when player leaves five metres");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);var other=new Agent{Index=3};a.SetLookAgent(other);Agent.Main.Position=new(20,0,0);int clears=a.GazeClears;Tick(b,1);Check(a.GetLookAgent()==other&&a.GazeClears==clears,"old perception record must not clear another system's new listener");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);Agent.Main=null;Tick(b,1);Check(HeadNone(a),"removed main agent releases actual owned target");
 (b,a)=Setup();a.SetLookAgent(other);Tick(b,0);Check(a.GetLookAgent()==other,"unowned NPC listener remains unchanged outside perception range");
 (b,a)=Setup();a.SetLookAgent(Agent.Main);b.IsPerforming=true;Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"performance can keep its intentional listener outside ordinary gaze range");
 b.IsPerforming=false;b.CurrentAction=new StayAction(null,false);Tick(b,1);Check(a.GetLookAgent()==null,"handoff from completed performance removes its unregistered distant player target");
 (b,a)=Setup();a.SetLookAgent(Agent.Main);a.Flags=Agent.AIScriptedFrameFlags.InConversation;Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"active dialogue remains owner of gaze");
 a.Flags=Agent.AIScriptedFrameFlags.None;Tick(b,1);Check(a.GetLookAgent()==null,"return from formal dialogue restores range gate");
 (b,a)=Setup(new StayAction(Agent.Main));a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"explicit action target cannot be cleared as plain ambient gaze");
 (b,a)=Setup();a.SetLookAgent(Agent.Main);b.IsMovingOrTurning=true;b.CurrentAction=new Moving();Tick(b,0);Check(a.GetLookAgent()==Agent.Main&&a.BodyWrites==0,"moving action retains its own distant target");
 foreach(int mode in new[]{0,1,2,3,4}){(b,a)=Setup();a.SetLookAgent(Agent.Main);b.IsInCombat=mode==0;b.IsBound=mode==1;b.IsStatusHeld=mode==2;b.IsStunned=mode==3;b.IsStoryIntercepting=mode==4;Tick(b,0);Check(a.GetLookAgent()==Agent.Main,"busy action preserves gaze mode "+mode);}
 (b,a)=Setup();var idle=new StreetIdleAction("rival",new StreetLifeDef{Actions=Array.Empty<string>()},new Vec2(1,0));b.CurrentAction=idle;a.SetLookAgent(Agent.Main);idle.OnStart(a);Check(a.GetLookAgent()==null,"real StreetIdle OnStart clears predecessor gaze");
 idle.OnTick(a,.2f);idle.OnTick(a,.2f);Check(a.StopWrites==1&&a.BodyWrites==1,"real idle hold resets movement once and sets independent duty direction even when aligned");
 a.SetLookAgent(Agent.Main);Tick(b,0);Check(a.GetLookAgent()==null,"real idle accepts unregistered gaze cleanup");
 (b,a)=Setup();Agent.Main.Position=new(5,0,0);Tick(b,0);Check(HeadPlayer(a)&&a.BodyWrites==0&&a.Frame.rotation.f.x>.99f,"five metre boundary updates head point without body write");
 Agent.Main.Position=new(5.01f,0,0);Tick(b,1);Check(HeadNone(a),"just outside boundary releases head");
 (b,a)=Setup();Agent.Main.Position=new(0,11,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(2,0,0);Tick(b,0);Check(a.BodyWrites==0,"walking at eleven metres cannot turn body");
 Agent.Main.Velocity=new(4,0,0);Tick(b,1);Check(a.BodyWrites==0,"neutral NPC ignores running at eleven metres");a.Team.Enemy=true;Tick(b,2);Check(a.BodyWrites==1&&NpcPerception.Describe(a).Contains("noiseRadius=15.0"),"hostile running inside fifteen metres turns body with diagnostic source and radius");
 Agent.Main.Position=new(0,16,0);Tick(b,4);Check(a.BodyWrites==1&&a.GetLookAgent()==null,"running beyond fifteen metres stops requesting body tracking");
 Tick(b,9);Check(a.BodyWrites==2&&a.Frame.rotation.f.x>.99,"sound expiry returns body to duty direction");
 (b,a)=Setup();Agent.Main.Position=new(-2,0,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(4,0,0);Tick(b,0);Check(a.BodyWrites==1,"neutral hears running behind at two metre boundary");
 (b,a)=Setup();Agent.Main.Position=new(-2.01f,0,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(4,0,0);Tick(b,0);Check(a.BodyWrites==0,"neutral ignores running beyond two metres");
 (b,a)=Setup();Agent.Main.Position=new(2,0,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(4,0,0);Tick(b,0);Check(a.BodyWrites==0 && a.Frame.rotation.f.x>.99f && HeadPlayer(a),"front running acquires head point without body write");
 int bodyWrites=a.BodyWrites;Agent.Main.Position=new(3,1,0);Tick(b,1);Agent.Main.Position=new(3,-1,0);Tick(b,2);
 Check(a.BodyWrites==bodyWrites&&a.Frame.rotation.f.x>.99f&&a.Frame.rotation.f.y==0&&HeadPlayer(a),"moving within cone tracks eye point without body direction");
 Agent.Main.Position=new(0,3,0);Tick(b,3);Check(HeadNone(a)&&a.Frame.rotation.f.x>.99f,"leaving body cone clears eye point and preserves body direction");
 (b,a)=Setup();Agent.Main.Position=new(-1,0,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(2,0,0);Tick(b,0);Check(a.BodyWrites==0,"ordinary walking behind causes no body turn");
 (b,a)=Setup();Agent.Main.Position=new(-1,0,0);Agent.Main.Velocity=new(4,0,0);Tick(b,0);Check(a.BodyWrites==0,"crouch remains silent even with high velocity");
 (b,a)=Setup();NpcPerception.Hear(new Vec3(-4,0,0),6);Tick(b,0);Check(a.BodyWrites==1,"generic noises retain their propagation radius");
 (b,a)=Setup();a.Team.Enemy=true;Agent.Main.Position=new(10,0,0);Tick(b,0);Check(HeadPlayer(a),"hostile head point retains ten metres");
 (b,a)=Setup();Agent.Main.Position=new(0,20,0);Agent.Main.CrouchMode=false;Agent.Main.Velocity=new(4,0,0);Tick(b,0);Tick(b,1);Check(a.BodyWrites==0&&a.GetLookAgent()==null,"running at twenty metres causes no body or head tracking");
 (b,a)=Setup();var host=new StoryProbeMissionBehavior{Listener=other};StoryProbeMissionBehavior.Current=host;other.Position=new(20,0,0);
 idle=new StreetIdleAction("rival",new StreetLifeDef{Listener="widow",Actions=new[]{"act_argue"},LineKeys=new[]{"line"},LineFallbacks=new[]{"fallback"}},new Vec2(1,0));
 var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 typeof(StreetIdleAction).GetField("_rng",hidden).SetValue(idle,new BarkRandom());var pose=typeof(StreetIdleAction).GetMethod("PlayPose",hidden);
 pose.Invoke(idle,new object[]{a});Check(host.Says==0,"distant actual listener cannot trigger an ambient addressed line");
 other.Position=new(10,0,0);pose.Invoke(idle,new object[]{a});Check(host.Says==1,"ambient line addresses nearby listener at the range boundary");
 Check(a.BodyWrites==0&&a.GetLookAgent()==null,"ambient speech has no body or gaze control side effect");
 // Eye point lifecycle uses observed object-gaze precedence, not an imagined direction lock.
 (b,a)=Setup();idle=new StreetIdleAction("npc",new StreetLifeDef{Actions=new[]{"act_argue"}},new Vec2(1,0));b.CurrentAction=idle;
 StoryProbeMissionBehavior.Current=new();idle.OnStart(a);Agent.Main.Position=new(3,0,0);Tick(b,0);int contracts=a.StandingWrites;
 foreach(float y in new[]{1f,-1f,2f,-2f}){Agent.Main.Position=new(3,y,0);idle.OnTick(a,.2f);Tick(b,1);a.NativeStep();Check(a.Frame.rotation.f.x>.99f&&Math.Abs(a.Frame.rotation.f.y)<.001f,"idle no longer creates object gaze that drives native body");Check(HeadPlayer(a)&&a.HeadPoint.Value.y==y&&a.HeadPoint.Value.z==1.7f,"eye point follows real target eye coordinates");}
 Check(a.StandingWrites==0&&a.GazeWrites==0,"head point needs neither scripted direction lock nor object gaze");
 Check(!a.Flags.HasFlag(Agent.AIScriptedFrameFlags.InConversation),"idle never impersonates formal conversation");
 pose.Invoke(idle,new object[]{a});Check(a.PoseWrites==1&&!a.StandingDirection.HasValue,"idle gestures do not inherit a scripted standing direction");
 Agent.Main.Position=new(-3,0,0);a.NativeStep();Tick(b,2);Check(HeadNone(a)&&a.Frame.rotation.f.x>.99f,"silent player reaches rear cone and releases eye point");
 Agent.Main.Position=new(3,0,0);Tick(b,3);idle.OnEnd(a);Check(HeadNone(a),"idle hands back eye point before next action starts");
 (b,a)=Setup(new StayAction(null,false));b.CurrentAction.OnStart(a);Agent.Main.Position=new(3,0,0);Tick(b,0);Agent.Main.Position=new(3,2,0);a.NativeStep();Tick(b,1);
 Check(a.Frame.rotation.f.x>.99f&&HeadPlayer(a),"plain Stay uses head point without object-driven native body rotation");
 NpcPerception.Hear(new Vec3(0,4,0),6);Tick(b,2);a.NativeStep();Check(a.Frame.rotation.f.y>.99f&&NpcPerception.DescribeBodyRequest(a).Contains("noise"),"heard world sound replaces standing body direction and reports owner");
 Tick(b,9);a.NativeStep();Check(a.Frame.rotation.f.x>.99f&&NpcPerception.DescribeBodyRequest(a).Contains("return-duty"),"expired sound restores persistent duty direction");
 contracts=a.StandingWrites;b.CurrentAction.OnEnd(a);b.CurrentAction=new Moving();b.IsMovingOrTurning=true;Tick(b,10);
 Check(a.StandingWrites==contracts&&!a.StandingDirection.HasValue,"movement owns body after plain Stay releases it");
 b.IsMovingOrTurning=false;b.CurrentAction=new StayAction(null,false);b.CurrentAction.OnStart(a);Agent.Main.Position=new(3,0,0);Tick(b,11);
 Check(a.StandingWrites==contracts&&HeadPlayer(a),"new idle reacquires eye point without scripted direction");
 foreach(int mode in new[]{0,1,2,3,4,5}){(b,a)=Setup(new StayAction(null,false));b.IsInCombat=mode==0;b.IsPerforming=mode==1;b.IsBound=mode==2;b.IsStatusHeld=mode==3;b.IsStunned=mode==4;b.IsStoryIntercepting=mode==5;Agent.Main.Position=new(3,0,0);Tick(b,0);Check(a.StandingWrites==0,"busy owner cannot receive perception standing contract mode "+mode);}
 (b,a)=Setup(new StayAction(null,false,true));b.CurrentAction.OnStart(a);Check(a.StandingWrites==0,"knockout placeholder cannot install daily body contract");
 foreach(int mode in new[]{0,1,2,3,4,5,6,7}){(b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);var foreign=new Agent{Index=8};a.SetLookAgent(foreign);b.IsInCombat=mode==0;b.IsPerforming=mode==1;b.IsBound=mode==2;b.IsStatusHeld=mode==3;b.IsStunned=mode==4;b.IsStoryIntercepting=mode==5;a.Flags=mode==6?Agent.AIScriptedFrameFlags.InConversation:Agent.AIScriptedFrameFlags.None;b.HasDutyPoint=mode!=7;Tick(b,1);Check(!a.HeadPoint.HasValue&&a.GetLookAgent()==foreign,"head point yields without clearing new object owner mode "+mode);}
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);Settings.Instance.Disabled=true;Tick(b,1);Check(HeadNone(a),"interaction gate clears owned eye point");Settings.Instance.Disabled=false;
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);int headClears=a.HeadClears;a.Active=false;NpcPerception.Forget(a);Check(a.HeadClears==headClears,"deletion forgets managed ownership without native call");
 (b,a)=Setup();Agent.Main.Position=new(3,0,0);Tick(b,0);headClears=a.HeadClears;Mission.Current=new();NpcPerception.ReleaseGaze(a);Check(a.HeadClears==headClears,"mission handoff never clears old native Agent");
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
            ('distance handoff', source.replace('if (idle && !moving && actualTarget == player && actualTarget != null)', 'if (false)'), program, 'unregistered player gaze beyond radius'),
            ('eye point refresh', source.replace('AgentControlHelper.LookAtPointOfInterest(agent, player);', ''), program, 'idle must refresh eye point'),
            ('idle action handoff', source, program.replace(idle, idle.replace('AgentControlHelper.StopLooking(agent);', '')), 'real StreetIdle OnStart clears predecessor gaze'),
            ('object gaze recurrence', source.replace('AgentControlHelper.LookAtPointOfInterest(agent, player);', 'AgentControlHelper.LookAtAgent(agent, player);'), program, 'idle must refresh eye point'),
            ('owned eye point release', source.replace('AgentControlHelper.StopLookingAtPointOfInterest(agent);', ';'), program, 'reacquired eye point releases'),
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

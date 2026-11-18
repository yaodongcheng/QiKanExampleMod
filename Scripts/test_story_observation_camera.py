"""Observation framing: extracted production ComputeFrame with real TaleWorlds math.

Agent/eye-height boundaries are fixtures; scene occlusion and rendered popup sizing
remain in-game acceptance checks. Coordinates below are relative to the speaker's
LookFrame (right/forward/up); the wide pair uses the recorded second-stage positions.
"""
import csv
import json
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

from check_story_probe import _game_root
from test_story_feedback import ROOT, SRC, method


def run():
    rows = list(csv.DictReader((ROOT / 'ModuleData/DesignData/Camera.csv').open(encoding='utf-8-sig', newline='')))[2:]
    rows = [r for r in rows if r['ID'].startswith('story_observe_')]
    opening = json.loads((ROOT / 'ModuleData/DesignData/StoryOpeningTimeline.json').read_text(encoding='utf-8'))
    selected = {b['Actor']: b['Case'] for b in opening['Beats'] if b['Cmd'] == 'camera_cut'}
    # LookFrame in the pure-math fixture is Identity. Offset fixtures preserve
    # the scene-relative right/forward axes (the market actors face 180 degrees).
    groups = {'blocker': [(0, 0, 0)], 'pawnbroker': [(0, 0, 0), (2.1, 0, 0)],
              'widow': [(0, 0, 0), (-2.2, -7.5, .6)],
              'son': [(0, 0, 0)], 'butcher': [(0, 0, 0)]}
    math = (SRC / 'Camera/SpringArmMath.cs').read_text(encoding='utf-8-sig')
    view = (SRC / 'Camera/SpringArmCameraView.cs').read_text(encoding='utf-8-sig')
    a = view.index('public struct SpringArmCameraParam')
    b = view.index('\n    /// <summary>', a)
    params = view[a:b]
    rendered = []
    for actor, case in selected.items():
        row = next(r for r in rows if r['ID'] == case)
        assert row['AttachType'] == 'Speaker' and row['UseEngineEyeHeight'] == '0'
        fields = []
        # Only fields used by this exact production method; the camera parser's
        # boolean convention is numeric 0/1, not bool.TryParse.
        for key in ('PivotX', 'PivotY', 'PivotZ', 'ArmLength', 'ArmYaw', 'ArmPitch',
                    'SocketX', 'SocketY', 'SocketZ', 'SelfYaw', 'SelfPitch', 'SelfRoll', 'Fov'):
            fields.append(f'{key}={float(row[key])}f')
        points = ','.join('new Vec3(%sf,%sf,%sf)' % tuple(float(v) for v in p) for p in groups[actor])
        rendered.append('Test(%s,new SpringArmCameraParam{%s},new[]{%s});' %
                        (json.dumps(actor), ','.join(fields), points))
    code = r'''
using System;using TaleWorlds.Library;using MathF=TaleWorlds.Library.MathF;
class Agent {public MatrixFrame LookFrame=MatrixFrame.Identity;public Vec3 VisualPosition=>LookFrame.origin;}
__PARAMS__
static class SpringArmMath {
const float DegToRad=(float)Math.PI/180;
static float ResolveEyeHeightOffset(Agent a,bool engine)=>1.4626f;
static bool EngineAppliesCameraLift()=>false;
static float ComputeEngineLift(Agent a,float arm)=>throw new Exception("unexpected engine lift");
__METHOD__
}
class Program {
static bool Probe;
static float Dot(Vec3 a,Vec3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
static void Test(string actor,SpringArmCameraParam p,Vec3[] feet){
SpringArmMath.ComputeFrame(new Agent(),in p,out var frame,out float fov);
float minX=1,maxX=0,minY=1,maxY=0,bodyTop=1,bodyBottom=0;
foreach(float aspect in new[]{4f/3,16f/9,21f/9})foreach(var rawFoot in feet)
foreach(float yaw in new[]{-5f,0f,5f})
foreach(float z in new[]{0f,1.6f,2.05f,2.5f})foreach(float x in new[]{-.45f,.45f}){
float angle=yaw*(float)Math.PI/180;
var foot=new Vec3(rawFoot.x*(float)Math.Cos(angle)-rawFoot.y*(float)Math.Sin(angle),rawFoot.x*(float)Math.Sin(angle)+rawFoot.y*(float)Math.Cos(angle),rawFoot.z);
var delta=foot+new Vec3(x,0,z)-frame.origin;
float depth=Dot(delta,-frame.rotation.u);
if(depth<=0)throw new Exception(actor+" behind camera");
float tangent=(float)Math.Tan(fov*Math.PI/360);
float sx=.5f+Dot(delta,frame.rotation.s)/(2*depth*tangent*aspect);
float sy=.5f-Dot(delta,frame.rotation.f)/(2*depth*tangent);
minX=Math.Min(minX,sx);maxX=Math.Max(maxX,sx);minY=Math.Min(minY,sy);maxY=Math.Max(maxY,sy);
if(z<=2.05f){bodyTop=Math.Min(bodyTop,sy);bodyBottom=Math.Max(bodyBottom,sy);}
}
Console.WriteLine($"{actor}: bodyY={bodyTop:F3}..{bodyBottom:F3}, body+bubbleY={minY:F3}..{maxY:F3}, X={minX:F3}..{maxX:F3}");
if(!Probe&&(bodyTop<.70f||maxY>.98f||minY<.64f||minX<.08f||maxX>.92f))
throw new Exception(actor+" outside lower-screen framing margins");
}
static void Main(string[] args){Probe=args.Length>0;__TESTS__
if(!Probe){bool rejected=false;try{Test("old_closeup",new(){ArmLength=3.5f,ArmYaw=150,ArmPitch=-10,Fov=60},new[]{Vec3.Zero});}catch(Exception){rejected=true;}
if(!rejected)throw new Exception("old center-framed closeup was not rejected");Console.WriteLine("PASS: original center-framed closeup rejected");}}
}
'''.replace('__PARAMS__', params).replace('__METHOD__', method(math, 'ComputeFrame')).replace('__TESTS__', '\n'.join(rendered))
    out = ROOT / 'Debug/offline/observation_camera_tests'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'Program.cs').write_text(code, encoding='utf-8')
    library = Path(_game_root(str(ROOT))) / 'bin/Win64_Shipping_Client/TaleWorlds.Library.dll'
    (out / 'Harness.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>'
        '<Reference Include="TaleWorlds.Library"><HintPath>%s</HintPath></Reference>'
        '</ItemGroup></Project>' % escape(str(library)), encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(out / 'Harness.csproj'), '--verbosity', 'quiet']
                   + (['--', 'probe'] if '--probe' in sys.argv else []), check=True)
    print(('MEASURE' if '--probe' in sys.argv else 'PASS') + ': production camera math with real TaleWorlds Mat3/Vec3 (scene/UI rendering not certified)')


if __name__ == '__main__':
    run()

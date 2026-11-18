"""Real Gauntlet Measure/Layout regression for the production HUD prefab.

Input/render resources are absent; glyph bounds use varied-size leaf fixtures,
while containers, visibility, alignment and scaling use the real game DLLs.
Projection dispatch and coordinate conversion execute extracted production code;
native window projection is a fixture, not a live render-resolution measurement.
"""
from pathlib import Path
import re
import subprocess
import sys
from xml.sax.saxutils import escape

from check_story_probe import _game_root
from test_story_feedback import ROOT, SRC, method


def run(game=None):
    game = Path(game or _game_root(str(ROOT)))
    version = (game / 'bin/Win64_Shipping_Client/Version.xml').read_text(encoding='utf-8-sig')
    match = re.search(r'v1\.(\d+)\.', version)
    assert match, version
    # Production compatibility patch is compiled only for MB2_GE_140.
    swap = int(match.group(1)) >= 4
    source = (SRC / 'GUI/StackLayoutVerticalSwapPatch.cs').read_text(encoding='utf-8-sig')
    # Access modifier is needed only by the extractor; method bodies are unchanged.
    source = source.replace('static void Prefix(', 'private static void Prefix(').replace(
        'static void Postfix(', 'private static void Postfix(')
    compat = '\n'.join(method(source, n) for n in ('IsCustomUI', 'Prefix', 'Postfix'))
    view = (SRC / 'AgentHUD/AgentHudMissionView.cs').read_text(encoding='utf-8-sig')
    tick = method(view, 'OnMissionTick')
    scale_setup = re.search(r'float uiScale = .*?;.*?float invUiScale = .*?;', tick, re.S).group()
    position_calls = re.findall(r'hud.SetTargetPosition\((.*?)\);', tick, re.S)
    assert len(position_calls) == 2 and re.sub(r'\s+', '', position_calls[0]) == re.sub(r'\s+', '', position_calls[1])
    anchor_setup = re.search(r'Vec3 agentPos = .*?(?=var screenPos)', tick, re.S).group()
    pixel_setup = re.search(r'var screenPos = .*?float pixelY = .*?;', tick, re.S).group()
    code = r'''
using System;using System.Linq;using System.Collections.Generic;using System.Numerics;
using System.Reflection;using System.Runtime.CompilerServices;using System.Xml.Linq;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;
using TaleWorlds.GauntletUI;using TaleWorlds.GauntletUI.BaseTypes;using TaleWorlds.GauntletUI.Layout;
static class MBMath {public static float ClampFloat(float x,float lo,float hi)=>Math.Clamp(x,lo,hi);}
class GlyphBounds : IText {
public string Value{get;set;}public TextHorizontalAlignment HorizontalAlignment{get;set;}public TextVerticalAlignment VerticalAlignment{get;set;}
public Vector2 Bounds;
public Vector2 GetPreferredSize(bool fixedWidth,float width,bool fixedHeight,float height,SpriteData sprites,float scale)=>Bounds*scale;}
class Edge {__CLAMP__ __CLAMPX__ public float Y(float y,float h,float scale)=>ClampToEdgeY(y,h,scale,150);public float X(float x,float w,float scale)=>ClampToEdgeX(x,w,scale,300);}
class Agent {public Vec3 Position;public Vec3 Eye;public Vec3 GetEyeGlobalPosition()=>Eye;}
class Camera {public Vec2 WindowPixels;public float W=1;}
class SceneLayer {
public Camera Camera;public float Width,Height,RenderScale;
public Vec2 WorldPointToScreenPoint(Vec3 anchor)=>new(Camera.WindowPixels.x/Width*RenderScale,Camera.WindowPixels.y/Height*RenderScale);}
class MissionScreen {public Camera CombatCamera;public SceneLayer SceneLayer;}
static class MBWindowManager {
public static int Calls;public static Camera LastCamera;public static Vec3 LastAnchor;
public static float WorldToScreen(Camera camera,Vec3 anchor,ref float x,ref float y,ref float w){
Calls++;LastCamera=camera;LastAnchor=anchor;x=camera.WindowPixels.x;y=camera.WindowPixels.y;w=camera.W;return w;}}
class Hud {public float BubbleWidth=300,BubbleHeight=150;public Vector2 Position;public void SetTargetPosition(float x,float y)=>Position=new(x,y);}
class Layer {public UIContext UIContext;}
class Production {
__ANCHOR__
__PROJECT__
public Vec3 Anchor(Agent agent){__ANCHOR_SETUP__ return agentPos;}
public Vector2 Project(MissionScreen currentMissionScreen,Agent agent,float screenWidth,float screenHeight){__ANCHOR_SETUP__ __PIXEL_SETUP__ return new(pixelX,pixelY);}
public Vector2 Position(UIContext context,float pixelX,float pixelY){var _layer=new Layer{UIContext=context};var hud=new Hud();__SCALE_SETUP__ hud.SetTargetPosition(__POSITION__);return hud.Position;}}
static class Compat {__COMPAT__ public static LayoutMethod Apply(StackLayout s,Widget w){Prefix(s,w,out var old);return old;}public static void Restore(StackLayout s,LayoutMethod old)=>Postfix(s,old);}
class Program {
static int checks;static bool Swap=__SWAP__;
static void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
static UIContext Context(float scale,float modifier=1){
var c=(UIContext)RuntimeHelpers.GetUninitializedObject(typeof(UIContext));
typeof(UIContext).GetProperty("Scale").SetValue(c,scale);typeof(UIContext).GetProperty("CustomScale").SetValue(c,scale*modifier);
typeof(UIContext).GetProperty("InverseScale").SetValue(c,1/scale);typeof(UIContext).GetProperty("CustomInverseScale").SetValue(c,1/(scale*modifier));
// Only dirty flags are used; this tree is not registered for input/render updates.
typeof(UIContext).GetProperty("EventManager").SetValue(c,RuntimeHelpers.GetUninitializedObject(typeof(EventManager)));return c;}
static Dictionary<Widget,XElement> Nodes;static Dictionary<Widget,StackLayout> Stacks;
static Widget Build(XElement x,UIContext c,Widget parent,HashSet<string> flags,int speechLines,float textWidth){
var w=new Widget(c);Nodes[w]=x;
if(x.Attribute("Id")!=null)w.Id=(string)x.Attribute("Id");
string visible=(string)x.Attribute("IsVisible");if(visible!=null&&visible.StartsWith("@")&&visible!="@IsVisible")w.IsVisible=flags.Contains(visible.Substring(1));
foreach(string key in new[]{"WidthSizePolicy","HeightSizePolicy","HorizontalAlignment","VerticalAlignment"}){
var p=typeof(Widget).GetProperty(key);if(x.Attribute(key)!=null)p.SetValue(w,Enum.Parse(p.PropertyType,(string)x.Attribute(key)));}
foreach(string key in new[]{"SuggestedWidth","SuggestedHeight","MarginTop","MarginBottom"})
if(float.TryParse((string)x.Attribute(key),out float value))typeof(Widget).GetProperty(key).SetValue(w,value);
if(x.Name.LocalName=="RichTextWidget"){
string text=(string)x.Attribute("Text");float width=text=="@AgentName"?textWidth:text=="@NpcIntentDebugText"?textWidth*.7f:text=="@EventMarkText"?26:text=="@DamageText"?16:textWidth;
float height=float.Parse((string)x.Attribute("Brush.FontSize"))*(text=="@SpeechText"?speechLines:1);
typeof(Widget).GetProperty("LayoutImp").SetValue(w,new TextLayout(new GlyphBounds{Bounds=new(width,height)}));}
if(x.Name.LocalName=="ListPanel"){
var stack=new StackLayout();if(x.Attribute("StackLayout.LayoutMethod")!=null)stack.LayoutMethod=Enum.Parse<LayoutMethod>((string)x.Attribute("StackLayout.LayoutMethod"));
typeof(Widget).GetProperty("LayoutImp").SetValue(w,stack);Stacks[w]=stack;}
if(parent!=null)w.ParentWidget=parent;
foreach(var child in x.Element("Children")?.Elements()??Enumerable.Empty<XElement>())Build(child,c,w,flags,speechLines,textWidth);
return w;}
static bool Visible(Widget w){for(;w!=null;w=w.ParentWidget)if(!w.IsVisible)return false;return true;}
// The render-position cache is outside this fixture; sum real layout origins.
static float Y(Widget w){float y=0;for(;w!=null;w=w.ParentWidget)y+=w.Top;return y;}
static float X(Widget w){float x=0;for(;w!=null;w=w.ParentWidget)x+=w.Left;return x;}
static (float bottom,float speechBottom,float height,float center) Layout(XElement xml,HashSet<string> flags,int lines,float scale,float anchorY,float anchorX=800,float modifier=1,float textWidth=200,bool oldScale=false){
Nodes=new();Stacks=new();var context=Context(scale,modifier);var root=Build(xml,context,null,flags,lines,textWidth);
var box=Nodes.Keys.Single(w=>Nodes[w].Attribute("PositionYOffset")!=null);
var target=oldScale?new Vector2(anchorX/scale-150,anchorY/scale-150):new Production().Position(context,anchorX,anchorY);
box.PositionXOffset=target.X;box.PositionYOffset=target.Y;
var originals=new Dictionary<Widget,LayoutMethod>();if(Swap)foreach(var pair in Stacks)originals[pair.Key]=Compat.Apply(pair.Value,pair.Key);
root.Measure(Vector2.Zero);root.Layout(0,0,0,0);
foreach(var pair in originals)Compat.Restore(Stacks[pair.Key],pair.Value);
var painted=Nodes.Keys.Where(w=>Visible(w)&&(Nodes[w].Name.LocalName=="RichTextWidget"||Nodes[w].Attribute("Sprite")!=null)).ToArray();
float bottom=painted.Max(w=>Y(w)+w.Size.Y);
var speech=Nodes.Keys.Single(w=>(string)Nodes[w].Attribute("Text")=="@SpeechText");
var main=Nodes.Keys.Single(w=>w.Id=="LWN_AgentHud_Main");
return (bottom,Y(speech)+speech.Size.Y,main.Size.Y,X(box)+box.Size.X/2);}
static void Main(string[] args){
// Later game versions populate widget metadata during resource initialization.
// This offline tree uses only Widget; register its real reflection metadata.
var metadata=typeof(WidgetInfo).GetField("_widgetInfos",BindingFlags.Static|BindingFlags.NonPublic);
if(metadata?.FieldType==typeof(Dictionary<Type,WidgetInfo>))metadata.SetValue(null,new Dictionary<Type,WidgetInfo>{{typeof(Widget),new WidgetInfo(typeof(Widget))}});
var xml=XDocument.Load(args[0]).Descendants("ItemTemplate").Single().Elements().Single();
var mainXml=xml.Descendants("ListPanel").Single(x=>(string)x.Attribute("Id")=="LWN_AgentHud_Main");
float gap=float.Parse((string)mainXml.Attribute("MarginBottom")??"0");
var old=new XElement(xml);var oldMain=old.Descendants("ListPanel").Single(x=>(string)x.Attribute("Id")=="LWN_AgentHud_Main");
oldMain.SetAttributeValue("HeightSizePolicy","StretchToParent");oldMain.SetAttributeValue("VerticalAlignment","Center");
oldMain.SetAttributeValue("MarginBottom",null);
var speechFlags=new HashSet<string>{"ShowSpeech","ShowIntentDebug"};
var bad=Layout(old,speechFlags,1,1,600);Check(600-bad.speechBottom>80,"original fixed empty space must reproduce the detached speech");
Console.WriteLine($"OLD: speech bottom {600-bad.speechBottom:F1} UI units above head anchor");
var badX=Layout(xml,new(){"ShowName","ShowIntentDebug"},1,1,600,800,.75f,72,true);
Check(Math.Abs(badX.center-800)>100,"base-scale conversion must reproduce the horizontal drift");
Console.WriteLine($"OLD: baseScale=1 customScale=0.75 horizontal drift {badX.center-800:F1} pixels");
var production=new Production();var agent=new Agent{Position=new Vec3(10,20,30),Eye=new Vec3(10.4f,19.7f,31.6f)};
var head=production.Anchor(agent);
Check(Math.Abs(head.x-agent.Eye.x)<.0001f&&Math.Abs(head.y-agent.Eye.y)<.0001f&&Math.Abs(head.z-agent.Eye.z-.1f)<.0001f,"all anchor axes must follow the eye, retaining 0.1m lift");
// A head offset projects differently from the body even when UI scale is 1.
float headPixel=800+head.x*90+head.y*15;float bodyPixel=800+agent.Position.x*90+agent.Position.y*15;
Check(Math.Abs(headPixel-bodyPixel)>20,"mixed body XY must reproduce head drift independently of UI scaling");
// Change render-target scale independently of window size and UI scale. The
// boundary supplies known window pixels; the production path must neither use
// the render-layer result nor multiply window pixels by screen dimensions again.
foreach(var resolution in new[]{new Vector2(1280,720),new Vector2(1920,1080),new Vector2(2560,1440)})
foreach(float renderScale in new[]{.5f,.67f,.75f,1f,1.25f,2f})foreach(float uiModifier in new[]{.75f,1f,1.25f})
foreach(var location in new[]{new Vector2(.2f,.3f),new Vector2(.5f,.5f),new Vector2(.8f,.7f)}){
var camera=new Camera{WindowPixels=new Vec2(location.X*resolution.X,location.Y*resolution.Y)};
var screen=new MissionScreen{CombatCamera=camera,SceneLayer=new SceneLayer{Camera=camera,Width=resolution.X,Height=resolution.Y,RenderScale=renderScale}};
int calls=MBWindowManager.Calls;var pixels=production.Project(screen,agent,resolution.X,resolution.Y);
Check(MBWindowManager.Calls==calls+1&&ReferenceEquals(MBWindowManager.LastCamera,camera),"HUD must use the active combat camera with the native window projection");
Check(MBWindowManager.LastAnchor.Distance(head)<.0001f,"window projection must receive the actual eye anchor");
Check(Math.Abs(pixels.X-camera.WindowPixels.x)<.01f&&Math.Abs(pixels.Y-camera.WindowPixels.y)<.01f,"window pixels must not vary with render-target scale or be normalized twice");
var actual=Layout(xml,new(){"ShowName","ShowSpeech"},2,resolution.Y/1080,pixels.Y,pixels.X,uiModifier,72);
Check(Math.Abs(actual.center-camera.WindowPixels.x)<.01f,"render scaling and UI scaling must keep independent horizontal coordinates");
float effectiveUiScale=resolution.Y/1080*uiModifier;
Check(Math.Abs(actual.speechBottom-(camera.WindowPixels.y-(gap+2)*effectiveUiScale))<.01f,"render scaling must preserve the configured vertical head gap");
if(renderScale!=1){var legacyProjection=screen.SceneLayer.WorldPointToScreenPoint(head);
Check(Math.Abs(legacyProjection.x*resolution.X-pixels.X)>1&&Math.Abs(legacyProjection.y*resolution.Y-pixels.Y)>1,"a scaled render-layer result must expose the former conversion's drift");}
}
foreach(float baseScale in new[]{.75f,1f,1.4f})foreach(float modifier in new[]{.75f,1f,1.25f})
foreach(float anchorX in new[]{180f,800f,1650f})foreach(float width in new[]{26f,72f,288f,450f})foreach(int lines in new[]{1,3})
foreach(var flags in new[]{new HashSet<string>{"ShowName","ShowIntentDebug"},new HashSet<string>{"ShowName","ShowEventMark"},new HashSet<string>{"ShowName","ShowSpeech"},new HashSet<string>{"ShowAlert"}}){
var actual=Layout(xml,flags,lines,baseScale,600,anchorX,modifier,width);float scale=baseScale*modifier;
Check(Math.Abs(actual.center-anchorX)<.01f,"HUD center must track head pixels at every base/custom scale and screen X");
foreach(var leaf in Nodes.Keys.Where(w=>Visible(w)&&(Nodes[w].Name.LocalName=="RichTextWidget"&&Nodes[w].Attribute("HorizontalAlignment")?.Value=="Center"||Nodes[w].Attribute("Sprite")?.Value==@"MPGeneral\MPScoreboard\view_profile_icon"&&Nodes[w].Attribute("IsVisible")!=null)))
Check(Math.Abs(X(leaf)+leaf.Size.X/2-anchorX)<.01f,"each visible centered element must align independently of glyph width");
Check(actual.bottom<=600-gap*scale+.01f&&600-gap*scale-actual.bottom<=2.01f*scale,"custom scaling must retain head gap");}
foreach(float scale in new[]{.75f,1f,1.4f})foreach(int lines in new[]{1,2,6})
foreach(var flags in new[]{speechFlags,new HashSet<string>{"ShowName","ShowSpeech"},new HashSet<string>{"ShowName","ShowIntentDebug","ShowSpeech","ShowHealth","ShowDamage","ShowAlert","ShowEventMark"},new HashSet<string>{"ShowAlert"}}){
var actual=Layout(xml,flags,lines,scale,600);
float expected=600-gap*scale;
Check(actual.bottom<=expected+.01f&&expected-actual.bottom<=2.01f*scale,"visible bottom must keep the configured head gap as content or scale changes");
if(flags.Contains("ShowSpeech")&&!flags.Contains("ShowHealth"))Check(Math.Abs(actual.speechBottom-(expected-2*scale))<.01f,"speech adds only its own margin to the configured HUD gap");
}
foreach(float scale in new[]{.75f,1f,1.4f})foreach(float anchor in new[]{-100f,500f,2000f}){
float y=new Edge().Y(anchor,1080,scale);var actual=Layout(xml,new(){"ShowAlert"},1,scale,(y+150)*scale);
Check(actual.bottom<=1060.01f&&actual.bottom-20*scale>=19.99f,"off-screen alert remains inside screen margins");
}
foreach(float baseScale in new[]{.75f,1f,1.4f})foreach(float modifier in new[]{.75f,1f,1.25f})foreach(float anchor in new[]{-100f,960f,2500f}){
float scale=baseScale*modifier;float x=new Edge().X(anchor,1920,scale);float y=new Edge().Y(2000,1080,scale);
var actual=Layout(xml,new(){"ShowAlert"},1,baseScale,(y+150)*scale,(x+150)*scale,modifier);
Check(actual.center-150*scale>=19.99f&&actual.center+150*scale<=1900.01f,"off-screen HUD width remains inside horizontal margins");
Check(actual.bottom<=1060.01f&&actual.bottom-20*scale>=19.99f,"custom-scaled edge alert remains inside vertical margins");}
Console.WriteLine($"PASS: {checks} real Gauntlet HUD layout checks, production window-projection dispatch/coordinates/anchor, horizontal centering, vertical compatibility and edge clamp (native projection/glyph sizing/input/render resources are fixtures)");
}}
'''.replace('__COMPAT__', compat).replace('__CLAMP__', method(view, 'ClampToEdgeY')).replace(
        '__CLAMPX__', method(view, 'ClampToEdgeX')).replace('__ANCHOR__', method(view, 'GetHudAnchor')).replace(
        '__PROJECT__', method(view, 'ProjectHudAnchor')).replace('__PIXEL_SETUP__', pixel_setup).replace(
        '__ANCHOR_SETUP__', anchor_setup).replace('__SCALE_SETUP__', scale_setup.replace('if (uiScale <= 0f) return;', 'if (uiScale <= 0f) return Vector2.Zero;')).replace(
        '__POSITION__', position_calls[0]).replace('__SWAP__', 'true' if swap else 'false')
    out = ROOT / 'Debug/offline/hud_layout_tests'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'Program.cs').write_text(code, encoding='utf-8')
    refs = ''.join('<Reference Include="%s"><HintPath>%s</HintPath></Reference>' %
                   (name, escape(str(game / 'bin/Win64_Shipping_Client' / (name + '.dll'))))
                   for name in ('TaleWorlds.GauntletUI', 'TaleWorlds.Library', 'TaleWorlds.TwoDimension'))
    (out / 'Harness.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>'
        '<ItemGroup>' + refs + '</ItemGroup></Project>', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(out / 'Harness.csproj'), '--verbosity', 'quiet',
                    '--', str(ROOT / 'GUI/Prefabs/AgentHudNearby.xml')], check=True)


if __name__ == '__main__':
    run(sys.argv[1] if len(sys.argv) > 1 else None)

"""Real Gauntlet Measure/Layout regression for the production HUD prefab.

Input/render resources are absent; text glyph measurement uses fixed-height leaf
fixtures, while all containers, visibility, alignment and scaling use game DLLs.
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
    code = r'''
using System;using System.Linq;using System.Collections.Generic;using System.Numerics;
using System.Reflection;using System.Runtime.CompilerServices;using System.Xml.Linq;
using TaleWorlds.GauntletUI;using TaleWorlds.GauntletUI.BaseTypes;using TaleWorlds.GauntletUI.Layout;
static class MBMath {public static float ClampFloat(float x,float lo,float hi)=>Math.Clamp(x,lo,hi);}
class Edge {__CLAMP__ public float Y(float y,float h,float scale)=>ClampToEdgeY(y,h,scale,150);}
static class Compat {__COMPAT__ public static LayoutMethod Apply(StackLayout s,Widget w){Prefix(s,w,out var old);return old;}public static void Restore(StackLayout s,LayoutMethod old)=>Postfix(s,old);}
class Program {
static int checks;static bool Swap=__SWAP__;
static void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
static UIContext Context(float scale){
var c=(UIContext)RuntimeHelpers.GetUninitializedObject(typeof(UIContext));
foreach(string k in new[]{"Scale","CustomScale"})typeof(UIContext).GetProperty(k).SetValue(c,scale);
foreach(string k in new[]{"InverseScale","CustomInverseScale"})typeof(UIContext).GetProperty(k).SetValue(c,1/scale);
// Only dirty flags are used; this tree is not registered for input/render updates.
typeof(UIContext).GetProperty("EventManager").SetValue(c,RuntimeHelpers.GetUninitializedObject(typeof(EventManager)));return c;}
static Dictionary<Widget,XElement> Nodes;static Dictionary<Widget,StackLayout> Stacks;
static Widget Build(XElement x,UIContext c,Widget parent,HashSet<string> flags,int speechLines){
var w=new Widget(c);Nodes[w]=x;
if(x.Attribute("Id")!=null)w.Id=(string)x.Attribute("Id");
string visible=(string)x.Attribute("IsVisible");if(visible!=null&&visible.StartsWith("@")&&visible!="@IsVisible")w.IsVisible=flags.Contains(visible.Substring(1));
foreach(string key in new[]{"WidthSizePolicy","HeightSizePolicy","HorizontalAlignment","VerticalAlignment"}){
var p=typeof(Widget).GetProperty(key);if(x.Attribute(key)!=null)p.SetValue(w,Enum.Parse(p.PropertyType,(string)x.Attribute(key)));}
foreach(string key in new[]{"SuggestedWidth","SuggestedHeight","MarginTop","MarginBottom"})
if(float.TryParse((string)x.Attribute(key),out float value))typeof(Widget).GetProperty(key).SetValue(w,value);
if(x.Name.LocalName=="RichTextWidget"){
w.WidthSizePolicy=SizePolicy.Fixed;w.HeightSizePolicy=SizePolicy.Fixed;w.SuggestedWidth=200;
w.SuggestedHeight=float.Parse((string)x.Attribute("Brush.FontSize"))*((string)x.Attribute("Text")=="@SpeechText"?speechLines:1);}
if(x.Name.LocalName=="ListPanel"){
var stack=new StackLayout();if(x.Attribute("StackLayout.LayoutMethod")!=null)stack.LayoutMethod=Enum.Parse<LayoutMethod>((string)x.Attribute("StackLayout.LayoutMethod"));
typeof(Widget).GetProperty("LayoutImp").SetValue(w,stack);Stacks[w]=stack;}
if(parent!=null)w.ParentWidget=parent;
foreach(var child in x.Element("Children")?.Elements()??Enumerable.Empty<XElement>())Build(child,c,w,flags,speechLines);
return w;}
static bool Visible(Widget w){for(;w!=null;w=w.ParentWidget)if(!w.IsVisible)return false;return true;}
// The render-position cache is outside this fixture; sum real layout origins.
static float Y(Widget w){float y=0;for(;w!=null;w=w.ParentWidget)y+=w.Top;return y;}
static (float bottom,float speechBottom,float height) Layout(XElement xml,HashSet<string> flags,int lines,float scale,float anchorY){
Nodes=new();Stacks=new();var root=Build(xml,Context(scale),null,flags,lines);
var box=Nodes.Keys.Single(w=>Nodes[w].Attribute("PositionYOffset")!=null);
box.PositionYOffset=anchorY/scale-150;
var originals=new Dictionary<Widget,LayoutMethod>();if(Swap)foreach(var pair in Stacks)originals[pair.Key]=Compat.Apply(pair.Value,pair.Key);
root.Measure(Vector2.Zero);root.Layout(0,0,0,0);
foreach(var pair in originals)Compat.Restore(Stacks[pair.Key],pair.Value);
var painted=Nodes.Keys.Where(w=>Visible(w)&&(Nodes[w].Name.LocalName=="RichTextWidget"||Nodes[w].Attribute("Sprite")!=null)).ToArray();
float bottom=painted.Max(w=>Y(w)+w.Size.Y);
var speech=Nodes.Keys.Single(w=>(string)Nodes[w].Attribute("Text")=="@SpeechText");
var main=Nodes.Keys.Single(w=>w.Id=="LWN_AgentHud_Main");
return (bottom,Y(speech)+speech.Size.Y,main.Size.Y);}
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
Console.WriteLine($"PASS: {checks} real Gauntlet HUD layout checks, production vertical compatibility and edge clamp (glyph sizing/input/render resources are fixtures)");
}}
'''.replace('__COMPAT__', compat).replace('__CLAMP__', method(view, 'ClampToEdgeY')).replace(
        '__SWAP__', 'true' if swap else 'false')
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

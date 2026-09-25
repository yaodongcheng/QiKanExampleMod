#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成「状态机编辑器」页（2026-09-25 立，同日第二轮：容器 + 分层 tab + 整份导出）。

设计要点（对照用户 6 条意见）：
  ① **导出完整 XML**：拿原文件原文，只替换 <families> / <states> / <edges> 三段 —— 文件头注释原样保留。
  ② 「任意状态 *」= UE 的 Any State（从它出发的边任何状态都适用）；不是"外部状态"。
  ③ **容器（组）取代家族**：自己建组、把状态拖进去；可以重叠（如"趴姿本体"⊂"趴姿族"）。
     🔴 只在编辑器里嵌套 —— 导出仍是 `<family name="…">成员…</family>` 名单，**运行期一字不改**（方案甲）。
  ④ **每个状态有内部视图**：entry（入边列表）→ clip 卡片（可改 act / 循环性 / 时长 / next）→ output。
  ⑤ **分层 tab**：总览 / 各容器 / 各状态内部，各自一页。

🔴 本文件是生成器，产出的 `statemachine_editor.html` 是生成物 —— 改页面请改这里重跑。
   生成后**必须**跑 `python check_pages.py`（它会用 node --check 验页面里的 JS 语法 —— 踩过"语法错导致整页不跑"的坑）。

用法：python gen_statemachine_editor.py && python check_pages.py
产出：**本目录** `statemachine_editor.html`（🔴 这一份**进 git** —— 克隆下来双击就能看，
      不必先装 Python；自检截图在 `out/`，那个忽略）
"""
import io
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.abspath(os.path.join(HERE, "..", ".."))    # 上两级 = 模块根（tools/<工具链>/ 的固定深度）
SRC = os.path.join(MOD, "ExampleModVS", "ExampleMod", "ExampleMod")
TAIKOU = os.path.abspath(os.path.join(MOD, "..", "Taikou"))
XML = os.path.join(MOD, "ModuleData", "statemachine_flight.xml")
OUT = os.path.join(HERE, "statemachine_editor.html")


def strip_comments(t):
    t = re.sub(r"/\*.*?\*/", "", t, flags=re.S)
    return re.sub(r"//[^\n]*", "", t)


def carr(text, name):
    m = re.search(r'string\[\] %s\s*=\s*\{(.*?)\};' % name, text, re.S)
    return re.findall(r'"([^"]+)"', m.group(1))


def build_data():
    raw = io.open(XML, encoding="utf-8").read()
    root = ET.fromstring(raw)

    groups = [{"name": f.get("name"), "entry": f.get("entry") or "", "members": (f.text or "").split()}
              for f in root.findall("families/family")]

    states = []
    for st in root.findall("states/state"):
        states.append({"name": st.get("name"), "act": st.get("act"),
                       "dur": st.get("duration") or "", "next": st.get("next") or ""})

    edges = []
    for ed in root.findall("edges/edge"):
        e = {"from": ed.get("from"), "to": ed.get("to"),
             "after": (ed.get("after-finish") or "") == "true",
             "phase": (ed.get("phase") or "") == "true", "blend": ed.get("blend") or ""}
        if ed.get("key"):
            e["kind"], e["key"], e["mode"] = "key", ed.get("key"), ed.get("key-mode")
        elif ed.get("anim"):
            e["kind"], e["anim"], e["lt"] = "anim", ed.get("anim"), ed.get("anim-lt") or ""
        else:
            e["kind"], e["pred"] = "pred", ed.get("when")
        edges.append(e)

    conds = strip_comments(io.open(os.path.join(SRC, "Flight", "FlightAnimConditions.cs"), encoding="utf-8").read())
    preds = [{"name": k, "body": " ".join(v.split()).replace("C(c).", "")}
             for k, v in re.findall(r'AnimConditions\.Register\("([^"]+)",\s*c\s*=>\s*([^;]+)\);', conds)]

    prims = io.open(os.path.join(SRC, "Animation", "AnimPrimitives.cs"), encoding="utf-8").read()
    tuning = strip_comments(io.open(os.path.join(SRC, "Flight", "FlightTuning.cs"), encoding="utf-8").read())
    nums = dict(re.findall(r'public static (?:float|int|bool) (\w+)\s*=\s*([0-9.]+f?);', tuning))
    clips = dict(re.findall(r'type="(act_\w+)"\s*\n?\s*animation="([^"]+)"',
                            io.open(os.path.join(TAIKOU, "ModuleData", "action_sets.xml"), encoding="utf-8").read()))
    for s in states:
        s["clip"] = clips.get(s["act"], "?")
        if s["dur"] and not s["dur"].replace(".", "", 1).isdigit():
            f = s["dur"]
            s["durText"] = nums.get(f[0].upper() + f[1:], "?").rstrip("f") + "s"
        else:
            s["durText"] = (s["dur"] + "s") if s["dur"] else ""
        if not s["dur"]:
            s["durText"] = "循环"

    return {"machine": root.get("name"), "groups": groups, "states": states, "edges": edges,
            "preds": preds, "scalars": dict(re.findall(r'AnimConditions\.RegisterParam\("([^"]+)",\s*\(\)\s*=>\s*([A-Za-z0-9_.]+)\)', conds)),
            "keys": carr(prims, "Keys"), "modes": carr(prims, "Modes"), "anims": carr(prims, "Anims"),
            "rawXml": raw}


TEMPLATE = r"""<title>状态机编辑器</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap">
<style>
:root{
  --bg:#F6F7F9; --surface:#FFF; --surface-2:#EDEFF3; --ink:#14171C; --ink-2:#4A5361; --ink-3:#77808F;
  --line:#DCE0E7; --line-2:#C3C9D4;
  --upright:#2A5FA8; --upright-soft:#E7EEF8; --prone:#B4560C; --prone-soft:#FBEDE0;
  --door:#13785C; --door-soft:#E2F3ED; --bad:#A32020; --bad-soft:#FBE9E9;
  --any:#6B4FA8; --any-soft:#EEE8FA;
  --entry:#0E7C86; --entry-soft:#E0F2F3;
  --sans:'IBM Plex Sans',system-ui,-apple-system,'Segoe UI',sans-serif;
  --mono:'IBM Plex Mono',ui-monospace,Consolas,monospace;
  color-scheme:light;
}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){
  --bg:#0E1116; --surface:#171B22; --surface-2:#1F242D; --ink:#E7EAF0; --ink-2:#A8B1BF; --ink-3:#7C8595;
  --line:#262C36; --line-2:#38404C;
  --upright:#7FB0EF; --upright-soft:#16283D; --prone:#E8A063; --prone-soft:#34220F;
  --door:#54C79E; --door-soft:#12301F; --bad:#F08A8A; --bad-soft:#2E1616;
  --any:#B9A3F0; --any-soft:#241E3A;
  --entry:#5FC9D6; --entry-soft:#123036; color-scheme:dark;
}}
:root[data-theme="dark"]{
  --bg:#0E1116; --surface:#171B22; --surface-2:#1F242D; --ink:#E7EAF0; --ink-2:#A8B1BF; --ink-3:#7C8595;
  --line:#262C36; --line-2:#38404C;
  --upright:#7FB0EF; --upright-soft:#16283D; --prone:#E8A063; --prone-soft:#34220F;
  --door:#54C79E; --door-soft:#12301F; --bad:#F08A8A; --bad-soft:#2E1616;
  --any:#B9A3F0; --any-soft:#241E3A;
  --entry:#5FC9D6; --entry-soft:#123036; color-scheme:dark;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.6 var(--sans);padding-block:22px 40px;padding-inline:20px}
.mono{font-family:var(--mono);font-size:12px}
h1{margin:0;font-size:21px;font-weight:600;letter-spacing:-.015em}
.sub{color:var(--ink-2);font-size:13px;max-width:78ch;margin:6px 0 0}
header{display:flex;flex-wrap:wrap;gap:12px 20px;align-items:flex-end;padding-bottom:12px;border-bottom:1px solid var(--line)}
.btns{margin-inline-start:auto;display:flex;gap:8px;flex-wrap:wrap}
button{font:500 12.5px var(--sans);padding:7px 12px;border-radius:8px;border:1px solid var(--line-2);background:var(--surface);color:var(--ink);cursor:pointer}
button:hover{background:var(--surface-2)}
button.primary{border-color:var(--door);color:var(--door);background:var(--door-soft)}
button.danger{border-color:var(--bad);color:var(--bad);background:var(--bad-soft)}
.tabs{display:flex;gap:6px;flex-wrap:wrap;align-items:center;margin-top:12px}
.tab{font:500 12px var(--sans);padding:5px 10px;border-radius:8px 8px 0 0;border:1px solid var(--line);border-bottom-color:transparent;background:var(--surface-2);color:var(--ink-2);cursor:pointer;display:flex;gap:6px;align-items:center}
.tab.on{background:var(--surface);color:var(--ink);border-color:var(--line-2)}
.tab .x{color:var(--ink-3);font-size:11px}
.tab .x:hover{color:var(--bad)}
main{display:grid;grid-template-columns:minmax(0,1fr) 370px;gap:14px;margin-top:0}
@media (max-width:1000px){main{grid-template-columns:1fr}}
.canvas{background:var(--surface);border:1px solid var(--line-2);border-radius:0 12px 12px 12px;overflow:auto}
svg{display:block;color:var(--ink-2);touch-action:none}
.panel{background:var(--surface);border:1px solid var(--line);border-radius:12px;padding:14px}
.panel h2{margin:0 0 8px;font-size:13px;font-weight:600}
.panel h3{margin:14px 0 6px;font:500 11px/1 var(--mono);letter-spacing:.12em;text-transform:uppercase;color:var(--ink-3)}
label{display:block;font-size:12px;color:var(--ink-2);margin:9px 0 3px}
select,input,textarea{width:100%;font:12px var(--mono);padding:6px 8px;border-radius:7px;border:1px solid var(--line-2);background:var(--surface);color:var(--ink)}
textarea{min-height:170px;white-space:pre;overflow:auto}
.row{display:flex;gap:8px}
.row>div{flex:1;min-width:0}
.hint{font-size:11.5px;color:var(--ink-3);margin-top:6px}
.chips{display:flex;flex-wrap:wrap;gap:6px;margin-top:6px}
.chip{font:500 10.5px var(--mono);padding:2px 7px;border-radius:999px;border:1px solid var(--line-2);color:var(--ink-2);background:var(--surface-2)}
.chip.u{color:var(--upright);border-color:var(--upright);background:var(--upright-soft)}
.chip.p{color:var(--prone);border-color:var(--prone);background:var(--prone-soft)}
.chip.g{color:var(--door);border-color:var(--door);background:var(--door-soft)}
.chip.a{color:var(--any);border-color:var(--any);background:var(--any-soft)}
.members{display:flex;flex-wrap:wrap;gap:4px;margin-top:6px;max-height:120px;overflow:auto}
.mem{font:500 10.5px var(--mono);padding:2px 6px;border-radius:6px;border:1px solid var(--line-2);cursor:pointer}
.mem:hover{border-color:var(--bad);color:var(--bad)}
.elist{max-height:240px;overflow:auto;border:1px solid var(--line);border-radius:8px}
.erow{display:flex;gap:6px;align-items:center;padding:5px 7px;border-bottom:1px solid var(--line);font:11.5px var(--mono);cursor:pointer}
.erow:last-child{border-bottom:0}
.erow:hover{background:var(--surface-2)}
.erow.sel{background:var(--door-soft)}
.erow .pri{color:var(--ink-3);width:18px}
.erow .txt{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.erow .mv{color:var(--ink-3);padding:0 3px;user-select:none}
.erow .mv:hover{color:var(--ink)}
.problems{margin-top:8px;font-size:12px}
.problems ul{margin:4px 0 0;padding-inline-start:18px;color:var(--bad)}
.problems .ok{color:var(--door)}
.legend{display:flex;flex-wrap:wrap;gap:6px 14px;font-size:11.5px;color:var(--ink-2);margin-top:8px}
.legend i{display:inline-block;width:18px;height:0;border-top:2px solid currentColor;vertical-align:middle;margin-inline-end:6px}
.legend .d{border-top-style:dashed}
/* ── 本轮新增（P1：缩放平移 / Entry / 右键菜单 / 边高亮防撞字）────────── */
.canvas{position:relative;overflow:hidden}
#svg{width:100%;height:76vh;min-height:560px;display:block;touch-action:none;cursor:default;user-select:none;-webkit-user-select:none}
#svg.panning{cursor:grabbing}
.gridline{stroke:var(--line);stroke-width:1;opacity:.5}
.bggrid{cursor:grab}
.zoombar{position:absolute;right:10px;bottom:10px;display:flex;gap:4px;align-items:center;background:var(--surface);border:1px solid var(--line-2);border-radius:10px;padding:4px 6px;box-shadow:0 2px 10px rgba(0,0,0,.08);z-index:5}
.zoombar button{padding:3px 9px;font-size:12px;border-radius:6px}
.zoombar .pct{font:600 11px var(--mono);color:var(--ink-2);min-width:46px;text-align:center}
.edge{color:var(--ink-2)}
.edge.sel,.edge.hl{color:var(--door)}
.edge.dim{opacity:.14}
.edge path.main{fill:none;stroke:currentColor;stroke-width:1.4}
.edge.sel path.main,.edge.hl path.main{stroke-width:2.8}
.edge path.hit{fill:none;stroke:rgba(0,0,0,.001);stroke-width:14}
.lbl rect{fill:var(--surface);stroke:var(--line);stroke-width:.8}
.edge.sel .lbl rect,.edge.hl .lbl rect{fill:var(--door-soft);stroke:var(--door)}
.lbl text{font-family:var(--mono);font-size:10px;fill:currentColor}
#svg.labels-off .lbl{display:none}
.entry rect{fill:var(--entry-soft);stroke:var(--entry);stroke-width:1.6}
.entry .tri{fill:var(--entry)}
.entry .t1{fill:var(--entry);font-weight:600}
.ghost rect{fill:var(--surface-2)}
.ctx{position:fixed;z-index:100;max-height:72vh;overflow:auto;min-width:196px;background:var(--surface);border:1px solid var(--line-2);border-radius:10px;padding:5px;box-shadow:0 10px 30px rgba(0,0,0,.18);display:none}
.ctx.on{display:block}
.ctx .it{padding:6px 10px;border-radius:7px;font-size:12.5px;cursor:pointer;color:var(--ink);white-space:nowrap}
.ctx .it:hover{background:var(--surface-2)}
.ctx .it.bad:hover{background:var(--bad-soft);color:var(--bad)}
.ctx .sep{height:1px;background:var(--line);margin:4px 6px}
.ctx .hd{padding:5px 10px 3px;font:500 10px var(--mono);letter-spacing:.1em;text-transform:uppercase;color:var(--ink-3)}
.tip{position:fixed;z-index:99;pointer-events:none;background:var(--surface);border:1px solid var(--line-2);border-radius:9px;padding:7px 10px;box-shadow:0 8px 24px rgba(0,0,0,.16);font:11.5px/1.6 var(--mono);color:var(--ink);display:none;max-width:360px}
.tip.on{display:block}
.tip b{color:var(--door)}
/* 🔴 本地草稿提示条：草稿会**静默盖住 XML**（用户会以为"说了改了怎么还是旧的"）⇒ 必须显眼 + 一键用 XML 覆盖 */
.banner{display:none;margin-top:10px;padding:9px 12px;border-radius:10px;border:1px solid var(--prone);background:var(--prone-soft);color:var(--ink);font-size:12.5px;line-height:1.7;align-items:center;gap:10px;flex-wrap:wrap}
.banner.on{display:flex}
.banner b{color:var(--prone)}
.banner .mono{color:var(--ink-2)}
.bdg{fill:var(--door-soft);stroke:var(--door);stroke-width:.8}
/* 容器页的**真实入口边**（Entry → 入口状态）*/
.entry-edge path{fill:none;stroke:var(--entry);stroke-width:2}
.entry-edge text{fill:var(--entry);font-family:var(--mono);font-size:10px}
</style>

<header>
  <div>
    <h1>状态机编辑器</h1>
    <p class="sub">拖连线编转移、<b>自己建容器装状态</b>（容器 = 可当来源的一组状态，等价于 UE 的 State Alias；<b>一个状态只归属一个容器</b>，拖到别的容器 = 移动；
    <b>双击容器钻进去</b>，里面的 Entry = "从外部进入本容器"的那些边）。
    滚轮缩放 / 拖空白平移（<b>拖盒子 = 单独挪它</b>）/ 空白右键出菜单 / 悬停边高亮 / 条件标签自动避让防撞字。
    编完点 <b>导出完整 XML</b>，整份覆盖回 <span class="mono">ModuleData/statemachine_flight.xml</span>，重启游戏即生效（不用重编译）。</p>
  </div>
  <div class="btns">
    <button id="btn-newgroup">新建容器</button>
    <button id="btn-export-full" class="primary">导出完整 XML</button>
    <button id="btn-export">导出边（片段）</button>
    <button id="btn-import">导入（粘贴 XML）</button>
    <button id="btn-reset" class="danger">恢复成文件里的样子</button>
  </div>
</header>
<div class="tabs" id="tabs"></div>
<div class="banner" id="banner"></div>

<main>
  <div class="canvas">
    <svg id="svg"></svg>
    <div class="zoombar">
      <button id="z-out" title="缩小">−</button>
      <span class="pct" id="z-pct">100%</span>
      <button id="z-in" title="放大">+</button>
      <button id="z-fit" title="适应全部（快捷键 F）">适应</button>
      <button id="z-1" title="回到 1:1">1:1</button>
    </div>
    <div class="tip" id="tip"></div>
  </div>
  <div class="panel">
    <h2>选中：<span id="selname" class="mono">（没选）</span></h2>
    <div id="editor"></div>
    <h3>边列表（顺序 = 优先级 ↑↓）</h3>
    <div id="elist-bar"></div>
    <div class="elist" id="elist"></div>
    <h3>校验</h3>
    <div class="problems" id="problems"></div>
    <h3>导出 / 导入</h3>
    <textarea id="io" spellcheck="false"></textarea>
    <div class="row" style="margin-top:8px">
      <div><button id="btn-copy" style="width:100%">复制</button></div>
      <div><button id="btn-apply" style="width:100%">把粘贴的内容导入</button></div>
    </div>
    <p class="hint">导入接受：完整文件 / <span class="mono">&lt;edges&gt;</span> 段 / 若干 <span class="mono">&lt;edge /&gt;</span> 行。</p>
  </div>
</main>
<div class="ctx" id="ctx"></div>

<script>
const DATA = @@DATA@@;
const LSKEY = "lwn.statemachine.flight.draft2";

// ── 可编辑状态 ─────────────────────────────────────────────
let groups = JSON.parse(JSON.stringify(DATA.groups));    // [{name, members[]}]
let states = JSON.parse(JSON.stringify(DATA.states));    // [{name, act, dur, next, clip, durText}]
let edges = JSON.parse(JSON.stringify(DATA.edges));
let pos = {};
let sel = null;          // {type:'state'|'group'|'edge', id}
let pending = null;      // 正在拉线的源状态名
let tabs = [{kind: "root"}];
let active = 0;
let mouse = {x: 0, y: 0}, drag = null;
let lastTap = null;                     // 自己记"上一次点在哪/点的什么" —— 🔴 不用 PointerEvent.detail（见下）
let gpos = {};                          // 容器盒被拖走后的位置（空 = 用默认排布）
let pendingBoxes = null;                // 草稿里的三个特殊盒位置，等布局常量定义完再套用
const view = {k: 1, tx: 0, ty: 0};      // 画布视图：缩放 + 平移（对齐 UE 的图浏览手感）
let viewRestored = false;
let draftActive = false, draftAck = false;   // 载入了本地草稿 / 用户已点过"保留草稿"
let clip = null;                             // 内存剪贴板：复制状态 → 粘贴状态
let lastCtxXY = { clientX: 400, clientY: 300 };   // 右键菜单落点（二级菜单复用同一坐标）
let elistAll = false;                        // 边列表是否显示全部（false = 只看与选中相关）
let notice = "";                             // 一次性提示（例如改名失败）

try {
  const raw = localStorage.getItem(LSKEY);
  if (raw) { const o = JSON.parse(raw);
    if (o && o.edges) { edges = o.edges; groups = o.groups || groups; states = o.states || states; pos = o.pos || {};
      gpos = o.gpos || {}; pendingBoxes = o.boxes || null; draftActive = true;
      if (o.view) { view.k = +o.view.k || 1; view.tx = +o.view.tx || 0; view.ty = +o.view.ty || 0; viewRestored = true; } } }
} catch (e) { /* localStorage 坏了也不能影响用 */ }

// 🔴 必须是 let：`恢复成文件里的样子` / 导入 都会 `stateByName = {}` 重建索引。
//    原来是 const ⇒ 那句赋值抛 "Assignment to constant variable" ⇒ 整个恢复函数半路中断、什么都没恢复
//    （用户实测踩到：点了恢复还是旧数据）。老 bug，这次靠真输入自检才抓到。
let stateByName = {}; states.forEach(s => stateByName[s.name] = s);
let groupByName = {}; groups.forEach(g => groupByName[g.name] = g);

function save() { try { localStorage.setItem(LSKEY, JSON.stringify({edges, groups, states, pos, view, gpos,
  boxes: {outside: {x: OUTSIDE.x, y: OUTSIDE.y}}})); } catch (e) {} }
function esc(s) { return String(s === undefined || s === null ? "" : s)
  .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }

// ── 布局 ───────────────────────────────────────────────────
const NODE_W = 240, NODE_H = 50, ROW = 60, PORT_R = 6;
const OUTSIDE = {x: 24, y: 24, w: 196, h: 42};    // 机外：非飞行那个外部状态
// 🚫 「机内任意状态 `*`」已移除（用户 2026-09-25 裁定：来源一律写成显式的族；XML 里已无 from="*"）
// 🚫 机器级 Entry 已移除 —— 用户裁定：Entry 只属于「容器」（下钻到容器页才画一名）。
//    机器"从哪进"= 那条 from="outside" 的相位边本身（画在 机外 盒身上）。
// 🔴 两个特殊盒 + 容器盒**都能单独拖动**（用户反馈："只能整体拖，不能调节点位置"）
const BOX0 = {outside: {x: OUTSIDE.x, y: OUTSIDE.y}};
function boxOf(id) { return OUTSIDE; }
function resetBoxes() { OUTSIDE.x = BOX0.outside.x; OUTSIDE.y = BOX0.outside.y; }
if (pendingBoxes && pendingBoxes.outside) { OUTSIDE.x = +pendingBoxes.outside.x || 0; OUTSIDE.y = +pendingBoxes.outside.y || 0; }
function groupBox(i, g) {
  const p = gpos[g.name];
  return p ? {x: p.x, y: p.y, w: 200, h: 52, g} : {x: 24 + (i % 2) * 214, y: 246 + Math.floor(i / 2) * 62, w: 200, h: 52, g};
}
function defaultPos() {
  const col = {0: 470, 1: 760};
  const used = {0: 0, 1: 0};
  states.forEach(s => {
    const inProne = groups.some(g => /Prone|趴/.test(g.name) && g.members.indexOf(s.name) >= 0);
    const c = inProne ? 1 : 0;
    if (!pos[s.name]) pos[s.name] = {x: col[c], y: 60 + used[c] * ROW};
    used[c]++;
  });
}
defaultPos();

// 机器"从哪进"= 那条 from="outside" 的相位边（不再有 Entry 节点；它就是唯一答案）
function outsideTarget() { const e = edges.find(x => x.from === "outside"); return e ? e.to : null; }

function isProne(n) { return groups.some(g => /Prone|趴/.test(g.name) && g.members.indexOf(n) >= 0); }
function nodeRect(n) { const p = pos[n] || {x: 0, y: 0}; return {x: p.x, y: p.y, w: NODE_W, h: NODE_H}; }
function bezPt(t, p1, p2, dx) {
  const c1x = p1.x + dx, c1y = p1.y, c2x = p2.x - dx, c2y = p2.y, u = 1 - t;
  return {x: u*u*u*p1.x + 3*u*u*t*c1x + 3*u*t*t*c2x + t*t*t*p2.x,
          y: u*u*u*p1.y + 3*u*u*t*c1y + 3*u*t*t*c2y + t*t*t*p2.y};
}
function overlaps(a, b) { return !(a.x + a.w < b.x || b.x + b.w < a.x || a.y + a.h < b.y || b.y + b.h < a.y); }
// 按估算宽度截断（节点副标题超长会溢出框）
function clipW(s, maxPx) {
  let w = 0, o = "";
  for (const ch of s) { const cw = ch.charCodeAt(0) > 0x2E80 ? 10.2 : 6.1; if (w + cw > maxPx) return o + "…"; w += cw; o += ch; }
  return o;
}
function inflate(b, p) { return {x: b.x - p, y: b.y - p, w: b.w + p * 2, h: b.h + p * 2}; }
// 🔴 按字符类型估宽：中文/全角 ≈ 10.2px，ASCII ≈ 6.1px。
//    之前一律 text.length*5.9 ⇒ 中文标签估窄 ~40% ⇒ 避让"判定通过"但视觉上照样压字。
function labelWidth(s) { let w = 20; for (let i = 0; i < s.length; i++) w += s.charCodeAt(i) > 0x2E80 ? 13.6 : 6.1; return w; }
// 条件标签避让：沿曲线试若干落点 × 若干垂直偏移，选第一个不与节点 / 容器 / 其他标签相撞的
function placeLabel(text, p1, p2, dx, obstacles, placed, spin) {
  const w = labelWidth(text), h = 15;
  const t0 = [0.5, 0.42, 0.58, 0.34, 0.66, 0.27, 0.73, 0.2, 0.8, 0.12, 0.88];
  // 🔴 spin：按边序号旋转"优先 t 顺序"。容器页的边都从同一个 Entry 点扇出，
  //    同一个 t 处各条曲线几乎重合 ⇒ 不旋转的话标签只能排成一列看不出归属。
  const s = ((spin | 0) % t0.length + t0.length) % t0.length;
  const ts = t0.slice(s).concat(t0.slice(0, s));
  const dys = [0, -17, 17, -34, 34, -51, 51];
  const clash = b => { const bb = inflate(b, 4); return obstacles.some(o => overlaps(bb, o)) || placed.some(o => overlaps(bb, o)); };
  const put = b => { placed.push(b); return {b}; };
  // ① 先沿曲线找空位（最贴近边，语义最好）
  for (let k = 0; k < dys.length; k++) {
    for (let i = 0; i < ts.length; i++) {
      const p = bezPt(ts[i], p1, p2, dx);
      const b = {x: Math.max(4, p.x - w / 2), y: p.y - h / 2 + dys[k], w, h};
      if (!clash(b)) return put(b);
    }
  }
  // ② 曲线附近全挤满了（左侧那堆边都汇到同几个盒子）→ 以曲线中点为心环形搜索。
  //    宁可离曲线远一点，也不要被容器盒盖住导致"这条边的条件看不见"。
  const c = bezPt(0.5, p1, p2, dx);
  for (let r = 26; r <= 264; r += 22) {
    for (let a = 0; a < 16; a++) {
      const th = a * Math.PI / 8;
      const b = {x: Math.max(4, c.x - w / 2 + r * Math.cos(th)), y: c.y - h / 2 + r * Math.sin(th), w, h};
      if (!clash(b)) return put(b);
    }
  }
  const b = {x: Math.max(4, c.x - w / 2), y: c.y - h - 300, w, h};
  return put(b);
}
function condText(e) {
  if (e.phase) return "相位驱动";
  if (e.kind === "key") return e.key + " " + ({down: "按下", up: "松开", held: "按着", released: "没按"}[e.mode] || e.mode);
  if (e.kind === "anim") return e.anim === "finished" ? "动画演完" : "剩余 < " + e.lt + "s";
  return e.pred || "?";
}
function groupIndexOf(name) { return groups.findIndex(g => g.name === name); }
function edgesTouchingGroup(g) {
  const m = g.members;
  return edges.map((e, i) => ({e, i})).filter(o => o.e.from === g.name || m.indexOf(o.e.to) >= 0);
}

// ── 渲染 ───────────────────────────────────────────────────
const svg = document.getElementById("svg");
const tabsEl = document.getElementById("tabs");
const selname = document.getElementById("selname");
const editor = document.getElementById("editor");
const elist = document.getElementById("elist");
const problemsBox = document.getElementById("problems");
const io = document.getElementById("io");

function tabLabel(t) {
  if (t.kind === "root") return "总览";
  return t.kind === "group" ? "容器 " + t.id : "状态 " + t.id;
}
function renderTabs() {
  tabsEl.innerHTML = tabs.map((t, i) =>
    `<span class="tab${i === active ? " on" : ""}" data-tab="${i}">${esc(tabLabel(t))}` +
    (t.kind === "root" ? "" : `<span class="x" data-close="${i}">×</span>`) + `</span>`).join("");
}
function openTab(t) {
  const dup = tabs.findIndex(x => x.kind === t.kind && x.id === t.id);
  if (dup >= 0) { active = dup; } else { tabs.push(t); active = tabs.length - 1; }
  render();
}

function render() {
  const t = tabs[active];
  hoverEdge = null; tipEl.classList.remove("on");
  let out = [];
  out.push('<defs>'
    + '<marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto">'
    + '<polygon points="0,1 10,5 0,9" fill="currentColor"/></marker>'
    + '<marker id="ar-entry" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto">'
    + '<polygon points="0,1 10,5 0,9" fill="var(--entry)"/></marker>'
    + '<pattern id="grid" width="24" height="24" patternUnits="userSpaceOnUse">'
    + '<path class="gridline" d="M 24 0 L 0 0 0 24" fill="none"/></pattern>'
    + '</defs>');
  out.push('<g id="world">');
  out.push('<rect class="bggrid" x="-20000" y="-20000" width="40000" height="40000" fill="url(#grid)"/>');

  // 🔴 总览**只画顶层**：容器（成员进去看）+ 不属于任何容器的散状态 + 三个"机外/机内/入口"盒
  const showStates = states.filter(s => stateVisible(s.name, t));
  const drawnGroups = groups.map((g, i) => ({g, i})).filter(o => groupVisible(o.g.name, t));

  // 容器页里那个"本容器 Entry"
  const gEntry = t.kind === "group" ? groupEntryBox(t) : null;

  // 条件标签避让要用的"障碍物" = 本页所有画得到的盒子
  const obstacles = [];
  if (t.kind === "root") {
    obstacles.push(OUTSIDE);
    drawnGroups.forEach(o => obstacles.push(groupBox(o.i, o.g)));
  }
  if (gEntry) obstacles.push(gEntry);
  showStates.forEach(s => obstacles.push(nodeRect(s.name)));
  const placed = [];

  // 边：总览按锚点画；容器页只画"目标在本容器内"的边，来源若不在容器内 ⇒ 折进本容器 Entry
  if (t.kind !== "state") {
    const mem = t.kind === "group" ? membersOf(t.id) : null;
    const same = (p, q) => !!(p && q && p.x === q.x && p.y === q.y && p.w === q.w);
    // 先把每条边的两端锚点一次算好：后面要按"锚点是否重合"判断自转移 / 并行边
    const AB = edges.map(e => {
      if (mem) {
        if (mem.indexOf(e.to) < 0) return null;                  // 出本容器的边，本页不画
        return [mem.indexOf(e.from) >= 0 ? nodeRect(e.from) : gEntry, nodeRect(e.to)];
      }
      const a0 = anchorOf(e.from, t), b0 = anchorOf(e.to, t);
      if (!a0 || !b0) return null;
      // 🔴 容器「内部」的边不在总览画（UE 同理：子状态机内部的转移只在**子图**里看，父图不画）。
      //    判据用"归属容器"而不是"盒子是不是同一个"，这样**嵌套容器**也覆盖得到：
      //    PronePoses ⊂ ProneFamily ⇒ PronePoses→dodge* 与 ProneFamily→fastmoveLean* 都算 ProneFamily 内部，
      //    总览不再出现这两个盒子之间那 8 条纠缠的边（钻进容器仍能看全）。
      if (t.kind === "root") {
        const go = ownerGroup(e.from), gt = ownerGroup(e.to);
        if (go && gt && (groupContains(go, gt) || groupContains(gt, go))) return null;
      }
      return [a0, b0];
    });
    edges.forEach((e, i) => {
      const ab = AB[i];
      if (!ab) return;
      const a = ab[0], b = ab[1];
      // 同一对锚点上可能有多条边（容器视图把它们折叠到同一个盒子上）⇒ 给"第几条"一个 lane，曲线错开
      let lane = 0;
      for (let k = 0; k < i; k++) if (AB[k] && same(AB[k][0], a) && same(AB[k][1], b)) lane++;
      let p1, p2, d, L1, L2, ldx;
      if (same(a, b)) {
        // 🔴 自转移（来源与目标落在同一个盒子上，例如总览里 UprightFamily → idle 两端都被收进同一个容器盒）：
        //    画成盒子顶上的一段拱形回环，而不是从右边出发绕到左边、横穿自己（UE 的 self-transition 就长这样）
        const cx = a.x + 26 + (lane % 6) * 26, hh = 42 + (lane % 4) * 14;
        p1 = {x: cx - 8, y: a.y}; p2 = {x: cx + 8, y: a.y};
        d = `M ${p1.x} ${p1.y} C ${p1.x} ${p1.y - hh}, ${p2.x} ${p2.y - hh}, ${p2.x} ${p2.y}`;
        L1 = {x: p1.x, y: a.y - hh - 6}; L2 = {x: p2.x, y: a.y - hh - 6}; ldx = 10;
      } else {
        p1 = {x: a.x + a.w, y: a.y + a.h / 2}; p2 = {x: b.x - 10, y: b.y + b.h / 2};
        const dx = Math.max(28, Math.abs(p2.x - p1.x) * 0.4) + lane * 34;
        d = `M ${p1.x} ${p1.y} C ${p1.x+dx} ${p1.y}, ${p2.x-dx} ${p2.y}, ${p2.x} ${p2.y}`;
        L1 = p1; L2 = p2; ldx = dx;
      }
      const isSel = sel && sel.type === "edge" && sel.id === i;
      const dash = e.phase ? ' stroke-dasharray="1 4"' : (e.after ? ' stroke-dasharray="6 4"' : "");
      out.push(`<g class="edge${isSel ? " sel" : ""}" data-i="${i}">`);
      out.push(`<path class="hit" d="${d}"/>`);
      out.push(`<path class="main" d="${d}"${dash} marker-end="url(#ar)"/>`);
      if (view.k >= 0.6) {
        const text = (i + 1) + " " + condText(e);
        const lp = placeLabel(text, L1, L2, ldx, obstacles, placed, mem ? i : 0);
        out.push(`<g class="lbl"><rect x="${lp.b.x}" y="${lp.b.y}" width="${lp.b.w}" height="${lp.b.h}" rx="4"/>`
          + `<text x="${lp.b.x + lp.b.w/2}" y="${lp.b.y + 11}" text-anchor="middle">${esc(text)}</text></g>`);
      }
      out.push("</g>");
    });
  }

  // 🔴 机外盒（只属于总览）：
  if (t.kind === "root") {
    out.push(`<g data-box="outside" style="cursor:grab"><rect x="${OUTSIDE.x}" y="${OUTSIDE.y}" width="${OUTSIDE.w}" height="${OUTSIDE.h}" rx="9" fill="var(--door-soft)" stroke="var(--door)" stroke-width="1.6"/>`
           + `<text x="${OUTSIDE.x+12}" y="${OUTSIDE.y+18}" font-family="var(--mono)" font-size="12" font-weight="600" fill="var(--door)">非飞行（机外）</text>`
           + `<text x="${OUTSIDE.x+12}" y="${OUTSIDE.y+33}" font-family="var(--mono)" font-size="10" fill="var(--ink-3)">这台状态机之外 · 进出就靠它</text>`
           // 🔴 机外也要有**出边端口**（用户："你没给非飞行（机外）加端点"）：从这里拉线 ⇒ 来源 = 机外
           + `<circle class="port" data-p="outside" cx="${OUTSIDE.x + OUTSIDE.w}" cy="${OUTSIDE.y + OUTSIDE.h/2}" r="${PORT_R}" fill="var(--door)" opacity="0.85"/></g>`);
  }

  // 容器页：本容器的 Entry —— 等价于 UE 子图里的 Entry（外部进来的边都汇到它）
  if (gEntry) {
    const gcur = groupByName[t.id] || {members: []};
    const ext = edges.filter(e => membersOf(t.id).indexOf(e.to) >= 0 && membersOf(t.id).indexOf(e.from) < 0).length;
    out.push('<g class="entry" style="cursor:default">'
      + `<rect x="${gEntry.x}" y="${gEntry.y}" width="${gEntry.w}" height="${gEntry.h}" rx="9"/>`
      + `<path class="tri" d="M ${gEntry.x+11} ${gEntry.y+14} L ${gEntry.x+21} ${gEntry.y+21} L ${gEntry.x+11} ${gEntry.y+28} Z"/>`
      + `<text class="t1" x="${gEntry.x+28}" y="${gEntry.y+18}" font-family="var(--mono)" font-size="12">Entry</text>`
      + `<text x="${gEntry.x+28}" y="${gEntry.y+33}" font-family="var(--mono)" font-size="10" fill="var(--ink-3)">${gcur.entry ? "入口 → " + esc(gcur.entry) : "⚠ 未设入口"} · 外部进入边 ${ext} 条</text>`
      + '</g>');
    // 🔴 **真实入口**：Entry → 入口状态 画一条实边（装载期就是把 `to=本容器` 解析成它）
    if (gcur.entry && pos[gcur.entry]) {
      const r2 = nodeRect(gcur.entry);
      const q1 = { x: gEntry.x + gEntry.w, y: gEntry.y + gEntry.h / 2 }, q2 = { x: r2.x - 10, y: r2.y + r2.h / 2 };
      const qdx = Math.max(30, Math.abs(q2.x - q1.x) * 0.45);
      const et = "入口 → " + gcur.entry;
      const elp2 = placeLabel(et, q1, q2, qdx, obstacles, placed);
      out.push('<g class="entry-edge">'
        + `<path d="M ${q1.x} ${q1.y} C ${q1.x+qdx} ${q1.y}, ${q2.x-qdx} ${q2.y}, ${q2.x} ${q2.y}" marker-end="url(#ar-entry)"/>`
        + `<rect class="ebg" x="${elp2.b.x}" y="${elp2.b.y}" width="${elp2.b.w}" height="${elp2.b.h}" rx="4"/>`
        + `<text x="${elp2.b.x + elp2.b.w/2}" y="${elp2.b.y + 11}" text-anchor="middle">${esc(et)}</text></g>`);
    }
  }

  // 容器盒（只在总览画；钻进容器后不再画它本身）
  drawnGroups.forEach(o => {
    const g = o.g, b = groupBox(o.i, g);
    const isSel = sel && sel.type === "group" && sel.id === g.name;
    out.push(`<g class="grp" data-g="${esc(g.name)}" style="cursor:grab">`);
    out.push(`<rect x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}" rx="9" fill="var(--surface-2)" `
           + `stroke="${isSel ? "var(--door)" : "var(--line-2)"}" stroke-width="${isSel ? 2 : 1}" stroke-dasharray="6 4"/>`);
    out.push(`<text x="${b.x+11}" y="${b.y+20}" font-family="var(--mono)" font-size="11.5" font-weight="600" fill="var(--ink)">${esc(g.name)}</text>`);
    out.push(`<text x="${b.x+11}" y="${b.y+36}" font-family="var(--mono)" font-size="10" fill="var(--ink-3)">容器 · ${g.members.length} 个状态${g.entry ? " · 入口 → " + esc(g.entry) : " · ⚠ 未设入口"}</text>`);
    // 🔴 容器盒右侧给一个**出边端口**（和状态节点一样）：从这里拉线 ⇒ 边的来源是"整个容器"
    //    （原来这里是橙色小点，只是"可接收状态"的提示，想画 容器→状态 的边没法直接拖）
    out.push(`<circle class="port" data-p="${esc(g.name)}" cx="${b.x + b.w}" cy="${b.y + b.h/2}" r="${PORT_R}" fill="var(--door)" opacity="0.85"/>`);
    out.push("</g>");
  });

  // 状态节点
  showStates.forEach(s => {
    const r = nodeRect(s.name);
    const isSel = sel && sel.type === "state" && sel.id === s.name;
    const br = bridgeOf(s.name);                 // "in"/"out" = 进机/出机衔接状态（都是相位驱动边）
    const col = br ? "var(--door)" : (isProne(s.name) ? "var(--prone)" : "var(--upright)");
    out.push(`<g class="node" data-n="${esc(s.name)}" style="cursor:grab">`);
    out.push(`<rect x="${r.x}" y="${r.y}" width="${r.w}" height="${r.h}" rx="9" fill="${br ? "var(--door-soft)" : "var(--surface)"}" `
           + `stroke="${isSel || br ? "var(--door)" : "var(--line-2)"}" stroke-width="${br ? 1.6 : (isSel ? 2 : 1)}"/>`);
    out.push(`<rect x="${r.x+1}" y="${r.y+7}" width="4" height="${r.h-14}" rx="2" fill="${col}"/>`);
    out.push(`<text x="${r.x+15}" y="${r.y+20}" font-family="var(--mono)" font-size="12.5" font-weight="600" fill="var(--ink)">${esc(s.name)}</text>`);
    out.push(`<text x="${r.x+15}" y="${r.y+36}" font-family="var(--mono)" font-size="10" fill="var(--ink-3)">${esc(clipW(s.clip + " · " + s.durText + (s.next ? " · 演完→" + s.next : ""), br ? NODE_W - 62 : NODE_W - 24))}</text>`);
    if (br) {                                    // 右上角小标：进机 / 出机
      out.push(`<rect class="bdg" x="${r.x + r.w - 44}" y="${r.y + 7}" width="36" height="15" rx="7"/>`
             + `<text x="${r.x + r.w - 26}" y="${r.y + 18}" text-anchor="middle" font-family="var(--mono)" font-size="9.5" fill="var(--door)">${br === "in" ? "进机" : "出机"}</text>`);
    }
    out.push(`<circle class="port" data-p="${esc(s.name)}" cx="${r.x+r.w}" cy="${r.y+r.h/2}" r="${PORT_R}" fill="var(--door)" opacity="0.85"/>`);
    out.push("</g>");
  });

  // 状态内部视图：ghost 入边来源 → 本状态 → output（对齐 UE 的状态节点细节）
  if (t.kind === "state") {
    const s = stateByName[t.id];
    const r = nodeRect(s.name);
    const ins = edges.map((e, i) => ({e, i})).filter(o => o.e.to === s.name);
    const gw = 196, gh = 38, gx = Math.max(30, r.x - gw - 130);
    ins.forEach((o, k) => {
      const gy = r.y + r.h / 2 + (k - (ins.length - 1) / 2) * 50 - gh / 2;
      out.push('<g class="ghost">'
        + `<rect x="${gx}" y="${gy}" width="${gw}" height="${gh}" rx="8" fill="var(--surface-2)" stroke="var(--line-2)" stroke-dasharray="5 4"/>`
        + `<text x="${gx+12}" y="${gy+16}" font-family="var(--mono)" font-size="11" font-weight="600" fill="var(--ink-2)">${esc(o.e.from)}</text>`
        + `<text x="${gx+12}" y="${gy+29}" font-family="var(--mono)" font-size="9.5" fill="var(--ink-3)">#${o.i+1} ${esc(condText(o.e))}</text></g>`);
      const q1 = {x: gx + gw, y: gy + gh / 2}, q2 = {x: r.x - 10, y: r.y + r.h / 2};
      const qdx = Math.max(24, Math.abs(q2.x - q1.x) * 0.45);
      out.push(`<path d="M ${q1.x} ${q1.y} C ${q1.x+qdx} ${q1.y}, ${q2.x-qdx} ${q2.y}, ${q2.x} ${q2.y}" fill="none" stroke="var(--line-2)" stroke-width="1.3" marker-end="url(#ar)"/>`);
    });
    if (!ins.length) out.push(`<text x="${Math.max(30, r.x - 320)}" y="${r.y + 24}" font-family="var(--mono)" font-size="11" fill="var(--ink-3)">没有入边 —— 只能靠相位 Force 进</text>`);
    out.push(`<g><rect x="${r.x + NODE_W + 40}" y="${r.y}" width="190" height="52" rx="9" fill="var(--door-soft)" stroke="var(--door)"/>`
           + `<text x="${r.x+NODE_W+52}" y="${r.y+20}" font-family="var(--mono)" font-size="11.5" font-weight="600" fill="var(--door)">output</text>`
           + `<text x="${r.x+NODE_W+52}" y="${r.y+36}" font-family="var(--mono)" font-size="10" fill="var(--ink-2)">输出姿势 → 喂给父层</text></g>`);
  }

  if (pending) {
    const r = nodeRect(pending);
    // 🔴 预览线必须 pointer-events=none：它画在最上层，否则会挡住"落点"（拖到容器盒上会被它截住）
    out.push(`<path d="M ${r.x+r.w} ${r.y+r.h/2} L ${mouse.x} ${mouse.y}" pointer-events="none" stroke="var(--door)" stroke-width="2" stroke-dasharray="5 4" fill="none"/>`);
  }
  out.push("</g>");
  svg.innerHTML = out.join("");
  applyView();
  renderTabs();
  renderDraftBanner();
  renderPanel();
}

// 🔴 landing（superland）/ 起飞入姿（hoverstart）是**衔接状态**：它们不参与"姿态之间"的转移，
//    只负责和状态机外面接上（都是相位驱动边）。这里从 phase 边**推导**，不新造 XML 属性。
function bridgeOf(name) {
  let r = null;
  edges.forEach(e => {
    if (!e.phase) return;
    if (e.from === "outside" && e.to === name) r = "in";
    else if (e.to === "outside" && e.from === name && r !== "in") r = "out";
  });
  return r;
}
// 状态/容器"归属哪个容器"（容器名优先；散状态返回 null）
function ownerGroup(name) {
  if (groupByName[name]) return name;
  const g = groupOfState(name);
  return g ? g.name : null;
}
// outer 是否（等于或）包住 inner —— 容器允许重叠，PronePoses ⊂ ProneFamily 就是这种
function groupContains(outer, inner) {
  if (outer === inner) return true;
  const o = groupByName[outer], i = groupByName[inner];
  if (!o || !i) return false;
  return i.members.every(m => o.members.indexOf(m) >= 0);
}
// 草稿 vs XML 的差异摘要（用来把"你在看草稿"这件事说清楚）
function draftDiff() {
  const xs = DATA.states.map(s => s.name), ds = states.map(s => s.name);
  const xe = DATA.edges.map(e => e.from + "→" + e.to + "|" + condText(e));
  const de = edges.map(e => e.from + "→" + e.to + "|" + condText(e));
  const addS = ds.filter(n => xs.indexOf(n) < 0), delS = xs.filter(n => ds.indexOf(n) < 0);
  const addE = de.filter(k => xe.indexOf(k) < 0).length, delE = xe.filter(k => de.indexOf(k) < 0).length;
  return {addS, delS, addE, delE, n: addS.length + delS.length + addE + delE};
}
function renderDraftBanner() {
  const b = document.getElementById("banner");
  if (!b) return;
  if (!draftActive) { b.classList.remove("on"); b.innerHTML = ""; return; }
  const d = draftDiff();
  b.innerHTML = '<b>⚠ 现在画的是浏览器里的本地草稿，不是 XML</b>'
    + '<span class="mono">　与 XML 相比：'
    + (d.addS.length ? "多 " + d.addS.length + " 个状态" + (d.addS.length <= 4 ? "（" + esc(d.addS.join("/")) + "）" : "") + "　" : "")
    + (d.delS.length ? "少 " + d.delS.length + " 个状态　" : "")
    + (d.addE ? "多 " + d.addE + " 条边　" : "")
    + (d.delE ? "少 " + d.delE + " 条边　" : "")
    + "XML 现在是 " + DATA.states.length + " 状态 / " + DATA.edges.length + " 边</span>";
  const btn = document.createElement("button");
  btn.className = "primary"; btn.textContent = "用 XML 覆盖草稿";
  btn.onclick = () => { try { localStorage.removeItem(LSKEY); } catch (e) {}
    draftActive = false; draftAck = false; document.getElementById("btn-reset").onclick(); };
  const keep = document.createElement("button");
  keep.textContent = "保留草稿";
  keep.onclick = () => { draftAck = true; renderDraftBanner(); };
  b.appendChild(btn); b.appendChild(keep);
  b.classList.add("on");
}
function stateVisible(n, t) {
  if (t.kind === "root") return !groups.some(g => g.members.indexOf(n) >= 0);
  if (t.kind === "group") return (groupByName[t.id] || {members: []}).members.indexOf(n) >= 0;
  if (t.kind === "state") return n === t.id;
  return false;
}
// 🔴 容器页（钻进容器）**不再画容器自己的盒子**，也不再画机外 / 任意状态 / 机器级 Entry ——
//    只画"里面有什么"：本容器的 Entry（把"外部进入本容器"的边都收在它身上）+ 成员 + 成员之间的边。
//    （用户反馈："为什么进入容器了，还能看到容器自己的节点，还有外部状态节点"）
function groupVisible(gname, t) { return t.kind === "root"; }
function membersOf(gname) { return (groupByName[gname] || {members: []}).members; }
// 容器页那个"本容器 Entry"的位置：自动摆在成员的左外侧，跟着成员走
function groupEntryBox(t) {
  const mem = membersOf(t.id).filter(n => pos[n]);
  if (!mem.length) return {x: 60, y: 60, w: 214, h: 52};
  let minX = Infinity, minY = Infinity, maxY = -Infinity;
  mem.forEach(n => { const p = pos[n]; if (p.x < minX) minX = p.x; if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y; });
  return {x: minX - 350, y: Math.round((minY + maxY + NODE_H) / 2) - 26, w: 214, h: 52};
}
// 「适应视图」按当前页取景（原来固定按整机，容器页会缩得过小）
function tabBoxes(t) {
  if (t.kind === "group") {
    const bs = states.filter(s => stateVisible(s.name, t)).map(s => nodeRect(s.name));
    if (edges.some(e => membersOf(t.id).indexOf(e.to) >= 0 && membersOf(t.id).indexOf(e.from) < 0)) bs.push(groupEntryBox(t));
    return bs;
  }
  if (t.kind === "state") { const s = stateByName[t.id]; return s ? [nodeRect(s.name)] : []; }
  return allBoxes();
}
function groupOfState(n) { return groups.find(g => g.members.indexOf(n) >= 0); }
// 🔴 锚点必须是"本页真的画出来了的盒子"，否则箭头会指向空气（老毛病：总览里指向容器内状态的边）
function anchorOf(name, t) {
  if (t.kind === "state") return null;                  // 状态页改用 ghost 入边，见 render()
  if (name === "outside") return OUTSIDE;
  if (groupByName[name]) return groupVisible(name, t) ? groupBox(groupIndexOf(name), groupByName[name]) : null;
  if (pos[name] && stateVisible(name, t)) return nodeRect(name);
  const g = groupOfState(name);
  if (g && groupVisible(g.name, t)) return groupBox(groupIndexOf(g.name), g);
  return null;
}
function edgeInTab(e, t) {
  if (t.kind === "group") return e.from === t.id || (groupByName[t.id] || {members: []}).members.indexOf(e.to) >= 0;
  if (t.kind === "state") return e.to === t.id;
  return true;
}

// ── 面板 ───────────────────────────────────────────────────
function opt(v, list, cur) { return list.map(x => `<option value="${esc(x)}"${x === cur ? " selected" : ""}>${esc(x)}</option>`).join(""); }

function renderPanel() {
  if (!sel) {
    selname.textContent = "（没选）";
    editor.innerHTML = '<p class="hint"><b>非飞行（机外）</b> = 这台状态机之外（起飞从它进来、落地收摊回到它）。<br>点状态 / 容器 / 边来编辑。'
      + '<b>拖状态 / 容器 / 机外 盒</b> = 单独挪那个元素（方向不限、没有边界；拖空白 = 整体平移画布）；<br>'
      + '<b>拖绿点</b>到状态节点 = 新建边（容器盒右侧也有绿点，从它拉出 ⇒ 来源是"整个容器"）；<br>'
      + '拖线<b>落到容器盒</b>上 = 直接连到该容器（装载期解析成它的<b>入口 entry</b>，= UE 的子状态机）；<br>'
      + '<b>拖状态落到容器盒上</b> = 把那个状态移进容器；'
      + '<b>双击状态节点或容器</b>进去看内部（容器页只显示"里面有什么"：本容器的 Entry + 成员 + 成员之间的边）。</p>';
  } else if (sel.type === "state") {
    const s = stateByName[sel.id];
    selname.textContent = s.name + "（状态内部）";
    const inGroups = groups.filter(g => g.members.indexOf(s.name) >= 0).map(g => g.name);
    editor.innerHTML = (notice ? `<p class="hint" style="color:var(--bad)">${esc(notice)}</p>` : "")
      + `
      <div class="chips"><span class="chip">${esc(s.act)}</span><span class="chip g">${esc(s.clip)}</span>`
      + (s.next ? `<span class="chip p">演完兜底 → ${esc(s.next)}</span>` : "") + `</div>
      <label>状态名（改它会级联：容器成员 + 所有引用它的边）</label>
      <input id="s-name" value="${esc(s.name)}">
      <label>动作名 act（内容包 action_sets.xml 里的那一层）</label>
      <input id="s-act" value="${esc(s.act)}">
      <div class="row">
        <div><label>时长（秒，留空 = 循环）</label><input id="s-dur" value="${esc(s.dur)}" placeholder="留空 = 循环"></div>
        <div><label>演完兜底 next</label><input id="s-next" value="${esc(s.next)}" placeholder="留空 = 不兜底"></div>
      </div>
      <p class="hint">属于：${inGroups.length ? inGroups.map(esc).join(" / ") : "（不属于任何容器）"}</p>
      <p class="hint">改时长/兜底是<b>改结构</b>（导出进 XML 的 states 段）；改 act 要与内容包一致，否则装载期会报 act_none。</p>`;
    document.getElementById("s-name").onchange = ev => renameState(s.name, ev.target.value.trim());
    document.getElementById("s-act").onchange = ev => { s.act = ev.target.value.trim(); commit(); };
    document.getElementById("s-dur").onchange = ev => { s.dur = ev.target.value.trim(); commit(); };
    document.getElementById("s-next").onchange = ev => { s.next = ev.target.value.trim(); commit(); };
  } else if (sel.type === "group") {
    const g = groupByName[sel.id];
    selname.textContent = g.name + "（容器）";
    editor.innerHTML = `
      <label>容器名（导出成 &lt;family name="…"&gt;）</label>
      <input id="g-name" value="${esc(g.name)}">
      <label>入口状态 entry（从外面"进入本容器"时落到哪个状态；边指向容器就落到它）</label>
      <select id="g-entry">${['<option value=""' + (g.entry ? "" : " selected") + '>（不设 —— 此时边不能指向本容器）</option>']
        .concat(g.members.map(m => `<option value="${esc(m)}"${m === g.entry ? " selected" : ""}>${esc(m)}</option>`)).join("")}</select>
      <h3 style="margin-top:12px">成员（点一下移出）</h3>
      <div class="members">${g.members.map(m => `<span class="mem" data-m="${esc(m)}">${esc(m)}</span>`).join("") || '<span class="hint">空的 —— 把一个状态拖到容器盒上</span>'}</div>
      <div class="row" style="margin-top:12px"><div><button id="g-del" class="danger" style="width:100%">删除容器</button></div></div>
      <p class="hint">🔴 <b>一个状态只归属一个容器</b>（用户裁定 / UE 规范）：把状态拖到别的容器盒上 = <b>移动</b>，不是叠加。<br>边以容器为来源 = 容器里任何状态都算。</p>`;
    document.getElementById("g-entry").onchange = ev => { g.entry = ev.target.value; commit(); };
    document.getElementById("g-name").onchange = ev => {
      const nn = ev.target.value.trim();
      if (!nn || groupByName[nn]) { render(); return; }
      edges.forEach(e => { if (e.from === g.name) e.from = nn; });
      g.name = nn; sel.id = nn; commit();
    };
    editor.querySelectorAll(".mem").forEach(el => el.onclick = () => {
      const m = el.getAttribute("data-m");
      g.members = g.members.filter(x => x !== m); commit();
    });
    document.getElementById("g-del").onclick = () => {
      groups = groups.filter(x => x !== g); sel = null; commit();
    };
  } else {
    const e = edges[sel.id];
    if (!e) { sel = null; return renderPanel(); }
    selname.textContent = "边 #" + (sel.id + 1);
    const srcList = ["outside"].concat(groups.map(g => g.name), states.map(s => s.name));
    editor.innerHTML = `
      <label>来源（状态 / 容器 / 机外 outside）</label>
      <select id="f-from">${opt(e.from, srcList, e.from)}</select>
      <label>目标状态</label>
      <select id="f-to">${`<option value="outside"${e.to === "outside" ? " selected" : ""}>outside（机外 —— 只允许相位驱动）</option>`
        + states.map(s => `<option value="${esc(s.name)}"${s.name === e.to ? " selected" : ""}>${esc(s.name)}</option>`).join("")
        + groups.map(g => `<option value="${esc(g.name)}"${g.name === e.to ? " selected" : ""}>${esc(g.name)}（容器 → 入口 ${esc(g.entry || "未设！")}）</option>`).join("")}</select>
      <label>条件</label>
      <select id="f-kind">
        <option value="key"${e.kind === "key" ? " selected" : ""}>按键（按下 / 松开 / 按着 / 没按）</option>
        <option value="anim"${e.kind === "anim" ? " selected" : ""}>动画播放情况</option>
        <option value="pred"${e.kind === "pred" ? " selected" : ""}>命名谓词（复合条件）</option>
      </select>
      <div id="f-args"></div>
      <label>blend（秒，留空 = 默认）</label>
      <input id="f-blend" value="${esc(e.blend)}">
      <label style="margin-top:10px"><input type="checkbox" id="f-after"${e.after ? " checked" : ""}> 演完才进（不打断一次性动作）</label>
      <label><input type="checkbox" id="f-phase"${e.phase ? " checked" : ""}> 相位驱动（不由状态机求值，只作文档）</label>
      <div class="row" style="margin-top:10px"><div><button id="btn-del" class="danger" style="width:100%">删除这条边</button></div></div>`;
    document.getElementById("f-from").onchange = ev => {
      e.from = ev.target.value;
      if (e.from === "outside" && !e.phase) { e.phase = true; notice = "来源是机外 ⇒ 已自动改成相位驱动（机外只允许相位边）"; }
      commit();
    };
    document.getElementById("f-to").onchange = ev => {
      e.to = ev.target.value;
      if (e.to === "outside" && !e.phase) { e.phase = true; notice = "目标是机外 ⇒ 已自动改成相位驱动（机外只允许相位边）"; }
      commit();
    };
    document.getElementById("f-blend").onchange = ev => { e.blend = ev.target.value.trim(); commit(); };
    document.getElementById("f-after").onchange = ev => { e.after = ev.target.checked; commit(); };
    document.getElementById("f-phase").onchange = ev => {
      e.phase = ev.target.checked;
      if (e.phase) { e.kind = "pred"; e.pred = e.pred || DATA.preds[0].name; }
      commit();
    };
    document.getElementById("btn-del").onclick = () => { edges.splice(sel.id, 1); sel = null; commit(); };
    const kindSel = document.getElementById("f-kind");
    function drawArgs() {
      const box = document.getElementById("f-args");
      if (e.kind === "key") {
        box.innerHTML = `<div class="row"><div><label>键</label><select id="f-key">${opt(e.key, DATA.keys, e.key)}</select></div>`
          + `<div><label>模式</label><select id="f-mode">${opt(e.mode, DATA.modes, e.mode)}</select></div></div>`;
        document.getElementById("f-key").onchange = ev => { e.key = ev.target.value; commit(); };
        document.getElementById("f-mode").onchange = ev => { e.mode = ev.target.value; commit(); };
      } else if (e.kind === "anim") {
        box.innerHTML = `<div class="row"><div><label>动画</label><select id="f-anim">${opt(e.anim, DATA.anims, e.anim)}</select></div>`
          + `<div><label>剩余 &lt; 秒</label><input id="f-lt" value="${esc(e.lt)}"></div></div>`;
        document.getElementById("f-anim").onchange = ev => { e.anim = ev.target.value; commit(); };
        document.getElementById("f-lt").onchange = ev => { e.lt = ev.target.value.trim(); commit(); };
      } else {
        box.innerHTML = `<label>谓词</label><select id="f-pred">${opt(e.pred, DATA.preds.map(p => p.name), e.pred)}</select>`
          + `<p class="hint">真身：<span class="mono" id="f-body"></span></p>`;
        const show = () => { document.getElementById("f-body").textContent = (DATA.preds.find(p => p.name === e.pred) || {}).body || "?"; };
        document.getElementById("f-pred").onchange = ev => { e.pred = ev.target.value; show(); commit(); };
        show();
      }
    }
    kindSel.onchange = ev => {
      e.kind = ev.target.value;
      if (e.kind === "key") { e.key = e.key || "W"; e.mode = e.mode || "held"; }
      else if (e.kind === "anim") { e.anim = e.anim || "finished"; e.lt = e.lt || "0.2"; }
      else { e.pred = e.pred || DATA.preds[0].name; }
      commit();
    };
    drawArgs();
  }

  // 🔴 边列表按选中聚焦：选中状态/容器时只列与它相关的边
  //    （用户："我选中一个状态，但是右边很多和这个状态无关的边列表让我疑惑"）。
  //    顺序仍是全局优先级顺序（↑↓ 改的也是全局优先级），只是过滤显示。
  const allRows = edges.map((e, i) => ({e, i}));
  const relRows = allRows.filter(o => edgeRelevant(o.e));
  const rows = (elistAll || !sel || sel.type === "edge") ? allRows : relRows;
  elist.innerHTML = rows.length ? rows.map(({e, i}) => {
    const isSel = sel && sel.type === "edge" && sel.id === i;
    return `<div class="erow${isSel ? " sel" : ""}" data-i="${i}"><span class="pri">${i+1}</span>`
      + `<span class="txt">${esc(e.from)} → ${esc(toText(e.to))} · ${esc(condText(e))}</span>`
      + `<span class="mv" data-up="${i}">↑</span><span class="mv" data-dn="${i}">↓</span></div>`;
  }).join("") : '<div class="erow"><span class="txt">（选中项没有相关边）</span></div>';
  const elbar = document.getElementById("elist-bar");
  if (elbar) {
    if (sel && sel.type !== "edge") {
      elbar.innerHTML = `<span class="hint">只看与选中相关：${relRows.length} / ${edges.length} 条</span> `
        + `<button id="elist-toggle" style="padding:1px 8px;font-size:11px">${elistAll ? "只看相关" : "显示全部"}</button>`;
      const tb = document.getElementById("elist-toggle");
      if (tb) tb.onclick = () => { elistAll = !elistAll; render(); };
    } else {
      elbar.innerHTML = `<span class="hint">共 ${edges.length} 条（顺序 = 优先级）</span>`;
    }
  }

  const probs = validate();
  problemsBox.innerHTML = probs.length
    ? `<span style="color:var(--bad)">${probs.length} 处问题：</span><ul>${probs.map(p => "<li>" + esc(p) + "</li>").join("")}</ul>`
    : `<span class="ok">通过（${states.length} 状态 / ${groups.length} 容器 / ${edges.length} 边）—— 可以导出</span>`;
}

function validate() {
  const p = [], names = {}; states.forEach(s => {
    if (names[s.name]) p.push(`状态名重复："${s.name}"`);
    names[s.name] = 1;
  });
  const gn = {}, owner = {}; groups.forEach(g => {
    if (gn[g.name]) p.push(`容器名重复："${g.name}"`); gn[g.name] = 1;
    if (g.entry && g.members.indexOf(g.entry) < 0) p.push(`容器 "${g.name}" 的入口 entry="${g.entry}" 不是它的成员`);
    g.members.forEach(m => {
      if (!names[m]) p.push(`容器 "${g.name}" 里的 "${m}" 不是已声明的状态`);
      // 🔴 状态必须只出现一次的唯一定义（用户裁定 / UE 规范）：一个状态不能同时属于两个容器
      if (owner[m]) p.push(`"${m}" 同时属于容器 "${owner[m]}" 和 "${g.name}" —— 状态只能归属一个容器`);
      else owner[m] = g.name;
    });
  });
  edges.forEach((e, i) => {
    const n = i + 1;
    if (e.from !== "*" && e.from !== "outside" && !gn[e.from] && !names[e.from]) p.push(`#${n} 来源 "${e.from}" 不是状态也不是容器`);
    if ((e.from === "outside" || e.to === "outside") && !e.phase) p.push(`#${n} outside（机外）只允许用于相位驱动边`);
    if (e.to !== "outside") {
      if (gn[e.to]) { if (!groupByName[e.to].entry) p.push(`#${n} 目标 "${e.to}" 是容器，但它没设入口 entry`); }
      else if (!names[e.to]) p.push(`#${n} 目标 "${e.to}" 不是状态也不是容器`);
    }
    if (e.kind === "key") {
      if (DATA.keys.indexOf(e.key) < 0) p.push(`#${n} 键 "${e.key}" 不在可用键里`);
      if (DATA.modes.indexOf(e.mode) < 0) p.push(`#${n} 模式 "${e.mode}" 不合法`);
    } else if (e.kind === "anim") {
      if (DATA.anims.indexOf(e.anim) < 0) p.push(`#${n} 动画条件 "${e.anim}" 不合法`);
      if (e.anim === "remaining" && !/^[0-9.]+$/.test(e.lt)) p.push(`#${n} "剩余时间" 需要数字阈值`);
    } else if (e.kind === "pred") {
      if (!DATA.preds.some(x => x.name === e.pred)) p.push(`#${n} 谓词 "${e.pred}" 没登记`);
    } else p.push(`#${n} 没选条件`);
  });
  return p;
}
function commit() { save(); render(); }

function svgPt(ev) {
  const r = svg.getBoundingClientRect();
  return {x: (ev.clientX - r.left - view.tx) / view.k, y: (ev.clientY - r.top - view.ty) / view.k};
}

// 🔴 双击判定：自己记时间 + 位置。**绝不能用 `ev.detail >= 2`** ——
//    Chrome 对 pointerdown 不填点击次数（恒 0）⇒ 那个判断永远不成立，
//    表现就是"双击容器 / 双击状态节点都进不去"（用户实测）。原生 dblclick 另留一条路。
function isDouble(ev, kind, id) {
  const now = Date.now();
  const hit = lastTap && lastTap.kind === kind && lastTap.id === id && now - lastTap.t < 480
    && Math.abs(lastTap.sx - ev.clientX) < 6 && Math.abs(lastTap.sy - ev.clientY) < 6;
  lastTap = { t: now, sx: ev.clientX, sy: ev.clientY, kind, id };
  return hit;
}
function dragCur(d) {
  if (d.kind === "state") return pos[d.id] || {x: 0, y: 0};
  if (d.kind === "group") return groupBox(groupIndexOf(d.id), groupByName[d.id]);
  return boxOf(d.id);
}
function setDragPos(d, x, y) {
  if (d.kind === "state") pos[d.id] = {x, y};
  else if (d.kind === "group") gpos[d.id] = {x, y};
  else { const b = boxOf(d.id); b.x = x; b.y = y; }
}

svg.addEventListener("pointerdown", ev => {
  closeCtx();
  const port = ev.target.closest && ev.target.closest(".port");
  const node = ev.target.closest && ev.target.closest(".node");
  const grp = ev.target.closest && ev.target.closest(".grp");
  const edgeEl = ev.target.closest && ev.target.closest(".edge");
  const boxEl = ev.target.closest && ev.target.closest("[data-box]");
  mouse = svgPt(ev);
  if (port) { lastTap = null; pending = port.getAttribute("data-p"); svg.setPointerCapture(ev.pointerId); render(); return; }
  if (node) {                                   // 状态节点：可拖 + 双击进内部视图
    const n = node.getAttribute("data-n");
    if (isDouble(ev, "state", n)) { drag = null; openTab({kind: "state", id: n}); return; }
    drag = {kind: "state", id: n, dx: mouse.x - pos[n].x, dy: mouse.y - pos[n].y};
    sel = {type: "state", id: n};
    svg.setPointerCapture(ev.pointerId); render(); return;
  }
  if (grp) {                                    // 容器盒：可拖 + 双击进容器页
    const gname = grp.getAttribute("data-g");
    if (isDouble(ev, "group", gname)) { drag = null; openTab({kind: "group", id: gname}); return; }
    const b = groupBox(groupIndexOf(gname), groupByName[gname]);
    drag = {kind: "group", id: gname, dx: mouse.x - b.x, dy: mouse.y - b.y};
    sel = {type: "group", id: gname};
    svg.setPointerCapture(ev.pointerId); render(); return;
  }
  if (edgeEl) { lastTap = null; sel = {type: "edge", id: +edgeEl.getAttribute("data-i")}; render(); return; }
  if (boxEl) {                                  // 机外 / 任意状态 / Entry 三个盒也能单独拖
    const bid = boxEl.getAttribute("data-box");
    lastTap = null;
    const b = boxOf(bid);
    drag = {kind: "box", id: bid, dx: mouse.x - b.x, dy: mouse.y - b.y};
    svg.setPointerCapture(ev.pointerId); render(); return;
  }
  lastTap = null;
  // 空白：拖拽平移画布（UE 手感）
  pan = {x: ev.clientX, y: ev.clientY, tx: view.tx, ty: view.ty};
  svg.classList.add("panning");
  svg.setPointerCapture(ev.pointerId);
  if (sel) { sel = null; render(); }
});
svg.addEventListener("pointermove", ev => {
  if (pan) {
    view.tx = pan.tx + (ev.clientX - pan.x);
    view.ty = pan.ty + (ev.clientY - pan.y);
    applyView(); moveTip(ev); return;
  }
  mouse = svgPt(ev);
  if (drag) {
    // 3px 抖动阈值：单击 / 双击时别把盒子挪歪（UE 也是这个手感）
    // 🔴 不钳制坐标：世界是无限的，钳到 x≥0/y≥0 会让"画布左/上侧"变成够不到的死区
    //    （用户实测："红框那一侧我这么拖都拖不过去"）
    const nx = mouse.x - drag.dx, ny = mouse.y - drag.dy;
    const cur = dragCur(drag);
    if (!drag.active && Math.abs(nx - cur.x) < 3 && Math.abs(ny - cur.y) < 3) return;
    drag.active = true;
    setDragPos(drag, nx, ny);
    render(); return;
  }
  if (pending) { render(); moveTip(ev); return; }
  const edgeEl = ev.target.closest && ev.target.closest(".edge");
  setHoverEdge(edgeEl ? +edgeEl.getAttribute("data-i") : null);
  moveTip(ev);
});
// 🔴 落点**不能**用 ev.target：pointerdown 里调了 setPointerCapture，
//    捕获之后 pointerup/pointermove 的 target 会变成捕获元素（svg 自己）⇒ closest 永远拿不到节点。
//    （这就是"拖绿点建不出边、拖状态进不了容器"的真因 —— 老 bug，靠真输入自检才抓到。）
function dropTargetAt(ev) {
  const el = document.elementFromPoint(ev.clientX, ev.clientY);
  return {
    node: (el && el.closest) ? el.closest(".node") : null,
    grp: (el && el.closest) ? el.closest(".grp") : null,
    box: (el && el.closest) ? el.closest("[data-box]") : null
  };
}

svg.addEventListener("pointerup", ev => {
  if (pan) { pan = null; svg.classList.remove("panning"); save(); return; }
  const dt = dropTargetAt(ev);
  const node = dt.node, grpEl = dt.grp, drop = dt.grp, boxEl = dt.box;
  if (pending && node) {                         // 拉线 → 落到状态节点 = 新建边
    const from = pending, to = node.getAttribute("data-n");
    pending = null; drag = null;
    addEdge(from, to);
    return;
  }
  if (pending && grpEl) {
    // 🔴 落到**容器盒**上 = 建一条 `to=容器` 的边（= UE 的子状态机 Entry）：
    //    装载期会解析成该容器的 entry（进容器落到哪个状态）。
    const gname = grpEl.getAttribute("data-g"), from = pending;
    const g = groupByName[gname];
    pending = null; drag = null;
    if (!g || !g.entry) {
      notice = `容器「${gname}」还没设入口（entry）—— 先在它的面板里选一个入口状态，边才能指向容器`;
      return commit();
    }
    addEdge(from, gname, `已新建边 → 容器「${gname}」（装载时解析成它的入口 ${g.entry}）—— 条件在右侧改`);
    return;
  }
  if (pending && boxEl && boxEl.getAttribute("data-box") === "outside") {
    // 🔴 落到**机外盒**上 = `to=outside`（出机）。机外只允许相位驱动边，addEdge 会自动置 phase
    const from = pending;
    pending = null; drag = null;
    addEdge(from, "outside", "已新建边 → 机外（outside，自动设为相位驱动）—— 条件在右侧改");
    return;
  }
  // 拖状态 → 落进哪个容器：用**被拖节点的中心点**做几何判定。
  //  🔴 不能用 elementFromPoint：被拖的节点就在光标下面，会把自己判成落点 ⇒ 容器永远检测不到。
  if (drag && drag.kind === "state" && drag.active) {
    const c = { x: pos[drag.id].x + NODE_W / 2, y: pos[drag.id].y + NODE_H / 2 };
    let gname = null;
    groups.forEach((gg, i) => {
      const b = groupBox(i, gg);
      if (c.x >= b.x && c.x <= b.x + b.w && c.y >= b.y && c.y <= b.y + b.h) gname = gg.name;
    });
    const g = gname ? groupByName[gname] : null;
    if (g) {
      // 🔴 用户裁定：状态只能归属一个容器（UE 规范）。所以这里是**移动**：先从所有容器移出，再加进目标容器。
      groups.forEach(o => { o.members = o.members.filter(m => m !== drag.id); });
      g.members.push(drag.id);
    }
  }
  // 拉了半天线却落在空白处：给一句提示，别让人以为"没反应"
  if (pending) notice = "没落到有效目标上 —— 请拖到 状态节点 / 容器盒 / 机外盒 上（或右键用「从这里连一条边…」）";
  pending = null; drag = null;
  try { svg.releasePointerCapture(ev.pointerId); } catch (e) {}
  commit();
});
svg.addEventListener("pointerleave", () => setHoverEdge(null));
// 原生 dblclick 也留着当双保险（两条路都进 openTab，重复调用只是切到同一个 tab）
svg.addEventListener("dblclick", ev => {
  const node = ev.target.closest && ev.target.closest(".node");
  const grp = ev.target.closest && ev.target.closest(".grp");
  if (node) openTab({kind: "state", id: node.getAttribute("data-n")});
  else if (grp) openTab({kind: "group", id: grp.getAttribute("data-g")});
});

elist.addEventListener("click", ev => {
  const up = ev.target.getAttribute && ev.target.getAttribute("data-up");
  const dn = ev.target.getAttribute && ev.target.getAttribute("data-dn");
  if (up !== null && up !== undefined && up !== "") {
    const i = +up; if (i > 0) { const t = edges[i-1]; edges[i-1] = edges[i]; edges[i] = t; if (sel && sel.type === "edge") sel.id = i-1; commit(); }
    return;
  }
  if (dn !== null && dn !== undefined && dn !== "") {
    const i = +dn; if (i < edges.length - 1) { const t = edges[i+1]; edges[i+1] = edges[i]; edges[i] = t; if (sel && sel.type === "edge") sel.id = i+1; commit(); }
    return;
  }
  const row = ev.target.closest(".erow");
  if (row) { sel = {type: "edge", id: +row.getAttribute("data-i")}; render(); }
});
tabsEl.addEventListener("click", ev => {
  const c = ev.target.getAttribute && ev.target.getAttribute("data-close");
  if (c !== null && c !== undefined && c !== "") {
    const i = +c; tabs.splice(i, 1); if (active >= tabs.length) active = tabs.length - 1; render(); return;
  }
  const t = ev.target.closest(".tab");
  if (t) { active = +t.getAttribute("data-tab"); render(); }
});

// ── 缩放 / 平移（UE 手感：滚轮缩放、拖空白平移、适应视图）──────
const ctxEl = document.getElementById("ctx");
const tipEl = document.getElementById("tip");
const pctEl = document.getElementById("z-pct");
const KMIN = 0.35, KMAX = 2.6;
let pan = null, hoverEdge = null;

function applyView() {
  const w = document.getElementById("world");
  if (w) w.setAttribute("transform", `translate(${view.tx} ${view.ty}) scale(${view.k})`);
  if (pctEl) pctEl.textContent = Math.round(view.k * 100) + "%";
  // 缩得太小就不显示条件标签（UE 也这样）；用 class 切 = 零重绘成本
  svg.classList.toggle("labels-off", view.k < 0.6);
}
function zoomAt(cx, cy, f) {
  const k0 = view.k, k = Math.min(KMAX, Math.max(KMIN, k0 * f));
  if (k === k0) return;
  const r = svg.getBoundingClientRect(), x = cx - r.left, y = cy - r.top;
  view.tx = x - (x - view.tx) * (k / k0);
  view.ty = y - (y - view.ty) * (k / k0);
  view.k = k; applyView(); save();
}
function allBoxes() {
  const bs = [OUTSIDE];
  groups.forEach((g, i) => bs.push(groupBox(i, g)));
  states.forEach(s => { const p = pos[s.name]; if (p) bs.push({x: p.x, y: p.y, w: NODE_W, h: NODE_H}); });
  return bs;
}
function fit() {
  const bs = tabBoxes(tabs[active] || {kind: "root"});
  if (!bs.length) return;
  const pad = 36;
  const x0 = Math.min.apply(null, bs.map(b => b.x)), y0 = Math.min.apply(null, bs.map(b => b.y));
  const x1 = Math.max.apply(null, bs.map(b => b.x + b.w)), y1 = Math.max.apply(null, bs.map(b => b.y + b.h));
  const vw = svg.clientWidth || 1000, vh = svg.clientHeight || 700;
  const k = Math.min(1.15, Math.max(KMIN, Math.min((vw - pad * 2) / Math.max(1, x1 - x0), (vh - pad * 2) / Math.max(1, y1 - y0))));
  view.k = k;
  view.tx = pad + Math.max(0, (vw - pad * 2 - (x1 - x0) * k) / 2) - x0 * k;
  view.ty = pad + Math.max(0, (vh - pad * 2 - (y1 - y0) * k) / 2) - y0 * k;
  applyView(); save();
}
svg.addEventListener("wheel", ev => { ev.preventDefault(); zoomAt(ev.clientX, ev.clientY, ev.deltaY < 0 ? 1.12 : 1 / 1.12); }, {passive: false});
document.getElementById("z-in").onclick = () => { const r = svg.getBoundingClientRect(); zoomAt(r.left + r.width / 2, r.top + r.height / 2, 1.2); };
document.getElementById("z-out").onclick = () => { const r = svg.getBoundingClientRect(); zoomAt(r.left + r.width / 2, r.top + r.height / 2, 1 / 1.2); };
document.getElementById("z-fit").onclick = () => fit();
document.getElementById("z-1").onclick = () => { view.k = 1; applyView(); save(); };

// ── 边悬浮高亮 + 跟随提示（只切 class，不重绘 → 不闪）────────────
function edgeTip(i) {
  const e = edges[i]; if (!e) return "";
  const flags = [e.after ? "演完才进" : "", e.phase ? "相位驱动" : "", e.blend ? "blend " + e.blend + "s" : ""].filter(Boolean).join(" · ");
  const body = e.kind === "pred" ? ((DATA.preds.find(p => p.name === e.pred) || {}).body || "") : "";
  return `<b>#${i + 1} ${esc(e.from)} → ${esc(e.to)}</b><br>条件：${esc(condText(e))}`
       + (body ? `<br>真身：<span style="color:var(--ink-3)">${esc(body)}</span>` : "")
       + (flags ? `<br>${esc(flags)}` : "")
       + `<br><span style="color:var(--ink-3)">右键：改优先级 / 删除</span>`;
}
function setHoverEdge(i) {
  if (i === hoverEdge) return;
  hoverEdge = i;
  svg.querySelectorAll(".edge").forEach(g => {
    const gi = +g.getAttribute("data-i");
    g.classList.toggle("hl", gi === i);
    g.classList.toggle("dim", i !== null && gi !== i);
  });
  if (i === null) { tipEl.classList.remove("on"); return; }
  tipEl.innerHTML = edgeTip(i);
  tipEl.classList.add("on");
}
function moveTip(ev) {
  if (!tipEl.classList.contains("on")) return;
  let x = ev.clientX + 14, y = ev.clientY + 14;
  if (x + 360 > innerWidth) x = Math.max(8, ev.clientX - 370);
  if (y + 130 > innerHeight) y = Math.max(8, ev.clientY - 140);
  tipEl.style.left = x + "px"; tipEl.style.top = y + "px";
}

// ── 右键菜单（UE 全靠右键：建节点 / 删边 / 加注释都从这走）──────
function closeCtx() { ctxEl.classList.remove("on"); }
function showCtx(ev, items) {
  const acts = []; let html = "";
  items.forEach(it => {
    if (it === "-") { html += '<div class="sep"></div>'; return; }
    if (it.hd) { html += `<div class="hd">${esc(it.hd)}</div>`; return; }
    const i = acts.push(it.fn) - 1;
    html += `<div class="it${it.bad ? " bad" : ""}" data-a="${i}">${esc(it.t)}</div>`;
  });
  ctxEl.innerHTML = html;
  ctxEl.classList.add("on");
  lastCtxXY = { clientX: ev.clientX, clientY: ev.clientY };
  ctxEl.style.left = Math.max(6, Math.min(ev.clientX, innerWidth - 210)) + "px";
  ctxEl.style.top = Math.max(6, Math.min(ev.clientY, innerHeight - (items.length * 30 + 24))) + "px";
  ctxEl.querySelectorAll(".it").forEach(el => el.onclick = () => {
    const f = acts[+el.getAttribute("data-a")];
    closeCtx(); if (f) f();
  });
}
function uniqueName(base) { let i = 1, n; do { n = base + i++; } while (stateByName[n] || groupByName[n]); return n; }
// 🔴 「建立边」不该只能靠拖端口 —— 右键 → 从这里连一条边… → 选目标（状态 / 容器）
//    （用户："我现在应该怎么建立边？" —— 原来菜单里根本没有入口）
function pickEdgeTarget(from) {
  const items = [{ hd: `「${from}」→ 连到哪个？` }, "-"];
  states.forEach(s => {
    if (s.name !== from) items.push({ t: s.name, fn: () => addEdge(from, s.name) });
  });
  items.push("-");
  groups.forEach(g => {
    items.push({
      t: `${g.name}（容器 → 入口 ${g.entry || "未设！"}）`,
      fn: () => {
        if (!g.entry) { notice = `容器「${g.name}」还没设入口 entry —— 先在它面板里选一个，边才能指向容器`; return commit(); }
        addEdge(from, g.name, `已新建边 → 容器「${g.name}」（装载时解析成它的入口 ${g.entry}）`);
      }
    });
  });
  showCtx(lastCtxXY, items);
}
// 新建一条边（默认条件：按着 W —— 建完在右侧面板改）
function addEdge(from, to, note) {
  if (!from || !to || from === to) return;
  // 🔴 机外（outside）**只允许相位驱动边**（引擎装载期校验）。建边时自动置 phase，
  //    否则会造出一条"整台定义有问题 ⇒ 不注册"的边 —— 这个口子在审计时抓到的。
  const ph = (from === "outside" || to === "outside");
  edges.push({ from: from, to: to, kind: "key", key: "W", mode: "held", blend: "", after: false, phase: ph });
  sel = { type: "edge", id: edges.length - 1 };
  notice = note || (ph
    ? "已新建**相位驱动**边（机外只允许相位边）—— 在右侧把它改成你要的条件 / 来源 / 目标"
    : "已新建边（默认条件「按着 W」）—— 在右侧把它改成你要的条件 / 来源 / 目标");
  commit();
}
// 边的目标显示：容器要标出它会落到哪个入口
function toText(t) {
  return groupByName[t] ? t + "（容器 → 入口 " + (groupByName[t].entry || "未设！") + "）" : t;
}
// ── 复制 / 粘贴状态（内存剪贴板；Ctrl+C / Ctrl+V 也可）──────────────
function copyState(n) {
  const s = stateByName[n];
  if (!s) return;
  clip = { kind: "state", name: s.name, data: JSON.parse(JSON.stringify(s)) };
  notice = `已复制状态「${s.name}」—— 在画布空白处右键（或 Ctrl+V）粘贴`;
  render();
}
function pasteState(x, y) {
  if (!clip) { notice = "剪贴板是空的（先在状态节点上右键「复制状态」）"; return render(); }
  const base = clip.data.name;
  let nn = base + "_copy", i = 1;
  while (stateByName[nn] || groupByName[nn]) { i++; nn = base + "_copy" + i; }
  const s = Object.assign({}, clip.data, { name: nn });
  states.push(s); stateByName[nn] = s;
  pos[nn] = { x: x, y: y };
  sel = { type: "state", id: nn };
  notice = `已粘贴「${nn}」：act 沿用 ${s.act}（要换动作改下面的 act，否则两条状态播同一段动画）`;
  commit();
}
// ── 改状态名：必须**级联** —— 容器成员 + 所有引用它的边 + 位置 + 已打开的 tab
function renameState(oldName, nn) {
  notice = "";
  const s = stateByName[oldName];
  if (!s) return;
  if (!nn || nn === oldName) return render();
  if (stateByName[nn] || groupByName[nn]) { notice = `名字「${nn}」已被占用（状态 / 容器重名都不行）`; return render(); }
  s.name = nn;
  groups.forEach(g => { g.members = g.members.map(m => (m === oldName ? nn : m)); });
  edges.forEach(e => { if (e.from === oldName) e.from = nn; if (e.to === oldName) e.to = nn; });
  if (pos[oldName]) { pos[nn] = pos[oldName]; delete pos[oldName]; }
  delete stateByName[oldName]; stateByName[nn] = s;
  tabs.forEach(t => { if (t.kind === "state" && t.id === oldName) t.id = nn; });
  if (sel && sel.type === "state" && sel.id === oldName) sel.id = nn;
  commit();
}
// 视角中心的世界坐标（Ctrl+V 没在画布上移动过鼠标时用）
function viewportCenterWorld() {
  return { x: (svg.clientWidth / 2 - view.tx) / view.k, y: (svg.clientHeight / 2 - view.ty) / view.k };
}
// 边是否与当前选中相关（边列表聚焦用）
function edgeRelevant(e) {
  if (!sel) return true;
  if (sel.type === "state") return e.from === sel.id || e.to === sel.id;
  if (sel.type === "group") { const m = membersOf(sel.id); return e.from === sel.id || m.indexOf(e.to) >= 0; }
  return true;
}
function newState(x, y) {
  const name = uniqueName("新状态");
  states.push({name, act: "act_" + name, dur: "", next: "", clip: "?", durText: "循环"});
  stateByName[name] = states[states.length - 1];
  pos[name] = {x: x, y: y};
  sel = {type: "state", id: name};
  commit();
}
// 「机外进来落到哪个状态」= 改那条 from="outside" 相位边的目标（它就是机器的入口本身）
function setOutside(n) {
  const e = edges.find(x => x.from === "outside");
  if (e) { e.to = n; e.phase = true; e.after = false; e.kind = "pred"; e.pred = e.pred || "takeoff-trigger"; }
  else edges.unshift({from: "outside", to: n, kind: "pred", pred: "takeoff-trigger", phase: true, after: false, blend: ""});
  sel = null; commit();
}
function moveEdge(i, d) {
  const j = i + d;
  if (j < 0 || j >= edges.length) return;
  const t = edges[j]; edges[j] = edges[i]; edges[i] = t;
  if (sel && sel.type === "edge") sel.id = j;
  commit();
}
function removeState(n) {
  states = states.filter(s => s.name !== n);
  delete stateByName[n];
  groups.forEach(g => g.members = g.members.filter(m => m !== n));
  edges = edges.filter(e => e.from !== n && e.to !== n);
  if (sel && sel.type === "state" && sel.id === n) sel = null;
  tabs = tabs.filter(t => !(t.kind === "state" && t.id === n));
  if (active >= tabs.length) active = Math.max(0, tabs.length - 1);
  commit();
}
function removeGroup(n) {
  groups = groups.filter(g => g.name !== n);
  delete groupByName[n];
  edges = edges.filter(e => e.from !== n);     // 🔴 `*` 已废弃：容器没了，从它出发的边一并删掉
  if (sel && sel.type === "group" && sel.id === n) sel = null;
  tabs = tabs.filter(t => !(t.kind === "group" && t.id === n));
  if (active >= tabs.length) active = Math.max(0, tabs.length - 1);
  commit();
}
svg.addEventListener("contextmenu", ev => {
  ev.preventDefault();
  const node = ev.target.closest && ev.target.closest(".node");
  const grp = ev.target.closest && ev.target.closest(".grp");
  const edgeEl = ev.target.closest && ev.target.closest(".edge");
  const boxEl = ev.target.closest && ev.target.closest('[data-box="outside"]');
  const p = svgPt(ev);
  if (boxEl) {
    showCtx(ev, [{ hd: "机外（非飞行）" }, "-",
      { t: "从这里连一条边…（来源 = 机外）", fn: () => pickEdgeTarget("outside") }]);
    return;
  }
  if (node) {
    const n = node.getAttribute("data-n");
    showCtx(ev, [
      {hd: "状态 " + n}, "-",
      {t: outsideTarget() === n ? "（已是机外进入的目标）" : "设为机外进入的目标（outside 边）", fn: () => setOutside(n)},
      {t: "打开内部视图", fn: () => openTab({kind: "state", id: n})}, "-",
      {t: "从这里连一条边…", fn: () => pickEdgeTarget(n)}, "-",
      {t: "复制状态（进剪贴板）", fn: () => copyState(n)},
      {t: "删除该状态", bad: true, fn: () => removeState(n)}
    ]);
    return;
  }
  if (edgeEl) {
    const i = +edgeEl.getAttribute("data-i"), e = edges[i];
    if (!e) return;
    showCtx(ev, [
      {hd: "#" + (i + 1) + " " + e.from + " → " + e.to}, "-",
      {t: "↑ 提高优先级", fn: () => moveEdge(i, -1)},
      {t: "↓ 降低优先级", fn: () => moveEdge(i, 1)}, "-",
      {t: e.after ? "✓ 演完才进（点掉）" : "设为：演完才进", fn: () => { e.after = !e.after; commit(); }},
      {t: e.phase ? "✓ 相位驱动（点掉）" : "设为：相位驱动", fn: () => {
        e.phase = !e.phase;
        if (e.phase) { e.kind = "pred"; e.pred = e.pred || DATA.preds[0].name; }
        commit();
      }}, "-",
      {t: "删除这条边", bad: true, fn: () => { edges.splice(i, 1); sel = null; commit(); }}
    ]);
    return;
  }
  if (grp) {
    const gname = grp.getAttribute("data-g");
    showCtx(ev, [
      {hd: "容器 " + gname}, "-",
      {t: "打开容器", fn: () => openTab({kind: "group", id: gname})},
      {t: "从这里连一条边…（来源 = 整个容器）", fn: () => pickEdgeTarget(gname)},
      {t: "重命名…", fn: () => {
        const nn = prompt("容器名", gname);
        if (!nn || nn === gname || groupByName[nn]) return;
        edges.forEach(e => { if (e.from === gname) e.from = nn; });
        groupByName[gname].name = nn;
        if (sel && sel.type === "group" && sel.id === gname) sel.id = nn;
        commit();
      }},
      {t: "删除容器", bad: true, fn: () => removeGroup(gname)}
    ]);
    return;
  }
  showCtx(ev, [
    {hd: "画布"}, "-",
    {t: clip ? `粘贴状态「${clip.name}」` : "粘贴状态（剪贴板为空）",
     fn: () => pasteState(p.x - NODE_W / 2, p.y - NODE_H / 2)},
    {t: "新建状态", fn: () => newState(p.x - NODE_W / 2, p.y - NODE_H / 2)},
    {t: "新建容器", fn: () => document.getElementById("btn-newgroup").onclick()}, "-",
    {t: "适应视图（F）", fn: fit},
    {t: "恢复默认布局", fn: () => { pos = {}; gpos = {}; resetBoxes(); defaultPos(); commit(); fit(); }}, "-",
    {t: "恢复成文件里的样子", bad: true, fn: () => document.getElementById("btn-reset").onclick()}
  ]);
});
addEventListener("pointerdown", ev => { if (!ctxEl.contains(ev.target)) closeCtx(); }, true);
addEventListener("keydown", ev => {
  const tag = (ev.target && ev.target.tagName) || "";
  if (/^(INPUT|TEXTAREA|SELECT)$/.test(tag)) return;
  if (ev.key === "Escape") closeCtx();
  else if (ev.key === "f" || ev.key === "F") fit();
  else if ((ev.key === "Delete" || ev.key === "Backspace") && sel && sel.type === "edge") {
    edges.splice(sel.id, 1); sel = null; commit();
  }
  else if ((ev.ctrlKey || ev.metaKey) && (ev.key === "c" || ev.key === "C") && sel && sel.type === "state") {
    ev.preventDefault(); copyState(sel.id);
  }
  else if ((ev.ctrlKey || ev.metaKey) && (ev.key === "v" || ev.key === "V")) {
    ev.preventDefault();
    const c = viewportCenterWorld();
    pasteState(c.x - NODE_W / 2, c.y - NODE_H / 2);
  }
});

// ── 导出 / 导入 ────────────────────────────────────────────
function edgesXml() {
  return edges.map(e => {
    let c;
    if (e.phase) c = `when="${e.pred || DATA.preds[0].name}" phase="true"`;
    else if (e.kind === "key") c = `key="${e.key}" key-mode="${e.mode}"`;
    else if (e.kind === "anim") c = `anim="${e.anim}"${e.anim === "remaining" ? ` anim-lt="${e.lt}"` : ""}`;
    else c = `when="${e.pred}"`;
    return `\t\t<edge from="${e.from}" to="${e.to}" ${c}`
         + (e.blend ? ` blend="${e.blend}"` : "") + (e.after ? ` after-finish="true"` : "") + " />";
  }).join("\n");
}
function groupsXml() {
  // 容器 = 真实子状态机：导出要带 entry（入口状态）
  return groups.map(g => `\t\t<family name="${g.name}"${g.entry ? ` entry="${g.entry}"` : ""}>${g.members.join(" ")}</family>`).join("\n");
}
function statesXml() {
  return states.map(s => `\t\t<state name="${s.name}" act="${s.act}"`
    + (s.dur ? ` duration="${s.dur}"` : "") + (s.next ? ` next="${s.next}"` : "") + " />").join("\n");
}
// 🔴 整份导出：拿原文件原文，只替换三段 —— 文件头那一大段注释原样保留
function fullXml() {
  return DATA.rawXml
    .replace(/\n[ \t]*<families>[\s\S]*?\n[ \t]*<\/families>/, "\n\t<families>\n" + groupsXml() + "\n\t</families>")
    .replace(/\n[ \t]*<states>[\s\S]*?\n[ \t]*<\/states>/, "\n\t<states>\n" + statesXml() + "\n\t</states>")
    .replace(/\n[ \t]*<edges>[\s\S]*?\n[ \t]*<\/edges>/, "\n\t<edges>\n" + edgesXml() + "\n\t</edges>");
}
document.getElementById("btn-export-full").onclick = () => {
  const p = validate();
  io.value = (p.length ? "<!-- 还有 " + p.length + " 处问题，见右侧校验 -->\n" : "") + fullXml();
};
document.getElementById("btn-export").onclick = () => { io.value = "<edges>\n" + edgesXml() + "\n</edges>"; };
document.getElementById("btn-copy").onclick = async () => {
  io.select();
  try { await navigator.clipboard.writeText(io.value); } catch (e) { /* 已全选，手动 Ctrl+C */ }
};
document.getElementById("btn-import").onclick = () => { io.value = ""; io.focus(); };
document.getElementById("btn-apply").onclick = () => {
  const txt = io.value.replace(/<!--[\s\S]*?-->/g, "");
  const doc = new DOMParser().parseFromString("<root>" + txt + "</root>", "application/xml");
  const ens = [].slice.call(doc.querySelectorAll("edge"));
  const fams = [].slice.call(doc.querySelectorAll("family"));
  const sts = [].slice.call(doc.querySelectorAll("state"));
  if (!ens.length && !fams.length && !sts.length) { io.value = "（没解析出任何 edge / family / state）"; return; }
  if (fams.length) groups = fams.map(f => ({name: f.getAttribute("name"), entry: f.getAttribute("entry") || "",
    members: (f.textContent || "").split(/\s+/).filter(Boolean)}));
  if (sts.length) states = sts.map(s => ({name: s.getAttribute("name"), act: s.getAttribute("act"),
    dur: s.getAttribute("duration") || "", next: s.getAttribute("next") || "", clip: (stateByName[s.getAttribute("name")] || {}).clip || "?", durText: (stateByName[s.getAttribute("name")] || {}).durText || ""}));
  if (ens.length) edges = ens.map(nd => {
    const e = {from: nd.getAttribute("from"), to: nd.getAttribute("to"), blend: nd.getAttribute("blend") || "",
               after: nd.getAttribute("after-finish") === "true", phase: nd.getAttribute("phase") === "true"};
    if (nd.getAttribute("key")) { e.kind = "key"; e.key = nd.getAttribute("key"); e.mode = nd.getAttribute("key-mode"); }
    else if (nd.getAttribute("anim")) { e.kind = "anim"; e.anim = nd.getAttribute("anim"); e.lt = nd.getAttribute("anim-lt") || ""; }
    else { e.kind = "pred"; e.pred = nd.getAttribute("when"); }
    return e;
  });
  states.forEach(s => stateByName[s.name] = s);
  groups.forEach(g => groupByName[g.name] = g);
  sel = null; commit();
};
document.getElementById("btn-newgroup").onclick = () => {
  let i = 1, name;
  do { name = "新容器" + i++; } while (groupByName[name]);
  groups.push({name, members: []});
  sel = {type: "group", id: name};
  openTab({kind: "group", id: name});
};
document.getElementById("btn-reset").onclick = () => {
  groups = JSON.parse(JSON.stringify(DATA.groups));
  states = JSON.parse(JSON.stringify(DATA.states));
  edges = JSON.parse(JSON.stringify(DATA.edges));
  stateByName = {}; states.forEach(s => stateByName[s.name] = s);
  groupByName = {}; groups.forEach(g => groupByName[g.name] = g);
  pos = {}; gpos = {}; resetBoxes(); defaultPos(); sel = null; tabs = [{kind: "root"}]; active = 0; commit(); fit();
};
render();
if (!viewRestored) fit();
</script>
"""


def main():
    data = build_data()
    html = TEMPLATE.replace("@@DATA@@", json.dumps(data, ensure_ascii=False))
    io.open(OUT, "w", encoding="utf-8", newline="\n").write(html)
    print("OK -> " + OUT)
    print("   %d states / %d groups / %d edges" % (len(data["states"]), len(data["groups"]), len(data["edges"])))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""页面「视觉验收」：把生成物按几个场景无头截图，落到 _shots/ 供人（或视觉模型）核对。

为什么要有它（和 check_pages.py 是一对）：
  · check_pages.py  查**语法** —— 抓"JS 整段不跑、页面却看着能显示"这种隐形故障。
  · shoot_pages.py  出**画面** —— 抓"语法没错但渲染不对"：节点/连线错位、标签压字、
    Entry 不在、右键菜单没弹、缩放到不了 … 这类只有看出来才知道的问题。

做法：复制一份生成物 + 在末尾追加一段注入脚本（同页的两个 classic script 共享全局作用域，
      所以可以直接调 render()/fit()/setHoverEdge()/showCtx 触发的真实事件），
      再用 Chrome/Edge 的 --headless=new --screenshot 出图。**不改动生成物本身。**

用法（在 tools/anim-statemachine 下）：
    python shoot_pages.py                 # 截 statemachine_editor.html 的全部场景
    python shoot_pages.py xxx.html        # 只截指定页面（相对路径按本目录解析）
出图目录：out/_shots/（工具产物，不进 git，可随时删）
"""
import io
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PAGE_DIR = HERE                            # 页面在工具链根（那一份进 git）
OUT = os.path.join(HERE, "out", "_shots")  # 截图落点（忽略目录）


def rmtree_retry(path, tries=6, delay=0.4):
    """删目录 + 退避重试。Chrome 刚退出时 profile 里的文件可能还被占着句柄 ⇒ 一次删不掉。

    🔴 删的是 **Chrome 的 --user-data-dir**（几十 MB，里面大半是 Chrome 自带的 tflite 模型）——
       它只在本次运行内有意义（靠它让 file:// 的 localStorage 跨场景延续），跑完就是垃圾。
       删不掉也不报错：下次运行开头还会再清一遍，不会累积。
    """
    for i in range(tries):
        try:
            shutil.rmtree(path)
            return True
        except FileNotFoundError:
            return True
        except OSError:
            time.sleep(delay)
    return False


CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

CTX = ("var svgEl=document.getElementById('svg');"
       "%s.dispatchEvent(new MouseEvent('contextmenu',{clientX:%d,clientY:%d,bubbles:true,cancelable:true}));")

# 场景名 -> 注入的 JS（空 = 原样截图）
SCEN = [
    # 00 先把上一轮可能留下的草稿清掉（file:// 的 localStorage 在同一 profile 里是共享的，
    #    否则 13 号场景种下的旧草稿会污染后面所有截图）
    ("00_clear_draft", "try{localStorage.removeItem(LSKEY)}catch(e){}"),
    ("01_overview", ""),
    ("02_zoomout", "view.k=0.72;view.tx=40;view.ty=16;applyView();"),
    ("03_zoomfar", "view.k=0.44;view.tx=8;view.ty=8;applyView();"),          # 标签应被隐藏
    ("04_ctx_canvas", CTX % ("svgEl", 980, 620)),
    ("05_ctx_node", CTX % ("document.querySelector('.node')", 900, 300)),
    ("06_ctx_edge", CTX % ("document.querySelector('.edge')", 880, 520)),
    ("07_ctx_group", CTX % ("document.querySelector('.grp')", 300, 500)),
    ("08_hover_edge", "setHoverEdge(0);tipEl.style.left='760px';tipEl.style.top='330px';"),
    ("09_zoom150", "view.k=1.5;view.tx=-70;view.ty=-30;applyView();"),
    ("10_group_tab", "openTab({kind:'group', id:groups[0].name});"),
    ("11_state_tab", "openTab({kind:'state', id:'fastmoveStart'});"),
    ("12_group_prone", "openTab({kind:'group', id:'ProneFamily'});"),
    # 15/16 验三个交互补充：改名输入框 + 边列表聚焦 + 右键"粘贴状态"
    ("15_state_selected", "sel={type:'state', id:'hoverstart'}; render();"),
    ("17_edge_target_menu", "closeCtx(); pickEdgeTarget('hoverstart');"),
    ("16_ctx_canvas_paste", "clip={kind:'state',name:'hoverstart',data:{name:'hoverstart'}};"
                           "render();document.getElementById('svg').dispatchEvent(new MouseEvent('contextmenu',{clientX:620,clientY:620,bubbles:true,cancelable:true}));"),
    # 13 种一份"旧草稿"（含 magicIdle + from="*"），14 重新加载 —— 应当弹出显眼的草稿黄条
    ("13_seed_stale_draft", "try{const d={states:DATA.states.concat([{name:'magicIdle',act:'act_magic_idle',dur:'',next:'',clip:'magic_idle',durText:'循环'}]),edges:DATA.edges.concat([{from:'*',to:'magicIdle',kind:'pred',pred:'casting-fallback',blend:'0.15',after:false,phase:false}]),groups:DATA.groups,pos:{},view:{k:1.15,tx:30,ty:70},gpos:{}};localStorage.setItem(LSKEY,JSON.stringify(d));}catch(e){document.title='SEED-ERR '+e.message}"),
    ("14_draft_banner", ""),
]


def find_browser():
    for p in CHROME_CANDIDATES:
        if os.path.exists(p):
            return p
    return None


def build(page, name, js):
    html = io.open(page, encoding="utf-8").read()
    extra = ""
    if js:
        extra = ("\n<script>\n(function(){\ntry{\n" + js
                 + "\n}catch(e){document.title='INJECT-ERR: '+e.message;}\n})();\n</script>\n")
    path = os.path.join(OUT, name + ".html")
    io.open(path, "w", encoding="utf-8", newline="\n").write(html + extra)
    return path


def main():
    pages = sys.argv[1:] or ["statemachine_editor.html"]
    br = find_browser()
    if not br:
        print("没找到 Chrome/Edge，跳过视觉验收（装了再跑）")
        return 0
    if os.path.isdir(OUT):
        rmtree_retry(OUT)          # 退避重试：上一轮 Chrome 可能还没完全放掉 profile 的句柄
    os.makedirs(OUT, exist_ok=True)
    profile = os.path.join(OUT, ".profile")   # 全部场景共用一个 profile，localStorage 才能跨场景延续
    bad = 0
    for page in pages:
        p = page if os.path.isabs(page) else os.path.join(PAGE_DIR, page)
        if not os.path.exists(p):
            print("  %s（不存在，跳过 —— 先跑生成器）" % page)
            bad += 1
            continue
        stem = os.path.splitext(os.path.basename(page))[0]
        for name, js in SCEN:
            html = build(p, stem + "__" + name, js)
            png = html[:-5] + ".png"
            # 🔴 必须给一个**共用**的 --user-data-dir：否则每个场景都是独立 Chrome + 临时 profile，
            #    file:// 的 localStorage 不跨场景 ⇒ 种旧草稿 / 验草稿黄条这类场景全都验不了。
            subprocess.run([br, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                            "--no-first-run", "--no-default-browser-check",
                            "--user-data-dir=" + profile,
                            "--window-size=1680,1300", "--virtual-time-budget=2500",
                            "--screenshot=" + png, "file:///" + html.replace("\\", "/")],
                           capture_output=True)
            ok = os.path.exists(png)
            print("  %-52s %s" % (os.path.basename(png), "OK %d bytes" % os.path.getsize(png) if ok else "MISSING"))
            if not ok:
                bad += 1
    rmtree_retry(profile)      # 跑完把 Chrome 用户目录清掉（几十 MB，纯垃圾；留着它会越攒越大）
    print("出图目录：out/_shots/（只留截图；Chrome profile 已清）   结果：%d 个场景失败" % bad)
    print("看图重点：Entry 盒在不在 / 连线有没有指向空气 / 标签有没有压字或被盖住 /")
    print("          右键菜单弹没弹 / 悬停那条边有没有高亮并把其余压暗 / 缩到 44% 标签应消失")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

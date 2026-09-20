#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""build_preview_set.py —— 把一批 particle XML 出成「预览页套装」（一页一个 + 索引页）。

【为什么默认一页一个】
  页面把 XML 内嵌在 HTML 里，浏览器解析这几 MB 文本的耗时**只跟嵌进去的 XML 量有关**。
  探针实测「导航 → 脚本跑完」：1 个 = 0.70s · 9 个 = 0.82s · 99 个 = 4.24s。
  所以一页一个 = 打开最快，而且每页只跑一个 effect，帧率也最稳（用户 2026-09-20 裁定）。

【一页一个怎么不失去「自由切换」】
  每页底部注入一条导航条：`← 上一个 · 索引 · 下一个 →`（键盘 ← → 同效）。
  于是可以从第一个一路翻到最后一个，不用回索引页。

【用法】
    python tools/particle-pipeline/preview/build_preview_set.py
    ... --per-batch 9             # 改回一页 9 个（页内还有切换条）
    ... --xml-dir <目录> --out-dir <目录>

  🔴 产物落在**数据根**（D:/BrainMaker/骑砍2粒子特效复刻/output/preview），不在仓库里
     —— 预览页是数据不是代码（见 tools/particle-pipeline/README.md §1）。
"""
import argparse
import io
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
import paths                      # 两根定位 TOOL/DATA —— 见工具链根 paths.py

HERE = os.path.dirname(os.path.abspath(__file__))
MAKER = os.path.join(HERE, "make_preview.py")

NAME_RE = re.compile(r'<effect[^>]*\bname="([^"]*)"')


def safe_name(s):
    """effect 名 -> 安全文件名（只保留字母数字下划线点横线）。"""
    return re.sub(r"[^0-9A-Za-z_.-]", "_", s)


def load_meta():
    """从解析产物读「源类型 / 源资产」：asset 名 -> (kind, asset)。

    对位规则：XML 里的 effect 名带 `lwn_` 前缀（如 `lwn_ns_fireball`），
    对应 UE 资产 `NS_Fireball` —— 去掉前缀后不分大小写比对。对不上就 "?"（不猜）。
    """
    meta = {}
    pdir = paths.out("parsed")
    if not os.path.isdir(pdir):
        return meta
    for fn in os.listdir(pdir):
        if not fn.endswith(".json") or fn.startswith("_"):
            continue
        try:
            d = json.load(io.open(os.path.join(pdir, fn), encoding="utf-8"))
        except Exception:
            continue
        a = (d.get("asset") or "").strip()
        if a:
            meta[a.lower()] = (d.get("kind") or "?", a)
    return meta


def look_meta(meta, effect_name):
    key = effect_name[4:] if effect_name.startswith("lwn_") else effect_name
    return meta.get(key.lower(), ("?", "?"))


def read_effects(xml_path):
    """轻量读：只要 effect 名 + emitter 数，不整棵建树（99 个文件也就毫秒级）。"""
    try:
        root = ET.parse(xml_path).getroot()
    except Exception:
        return []
    out = []
    for eff in root.findall("effect"):
        out.append((eff.get("name") or os.path.basename(xml_path)[:-4],
                    len(eff.findall("emitters/emitter"))))
    return out


def nav_block(prev_f, prev_n, next_f, next_n):
    """每页底部的导航条：上一个 / 索引 / 下一个（键盘 ← → 同效）。

    🔴 跨页跳转只能靠**相对链接**：预览页是 file:// 直接双击打开的，
       file:// 下 fetch/XHR 被跨域策略拦死，所以只能靠 <a href> 换页。
    """
    css = ("position:fixed;left:14px;bottom:14px;z-index:99998;display:flex;gap:8px;"
           "align-items:center;background:rgba(10,9,18,.78);border:1px solid #241f3a;"
           "border-radius:10px;padding:7px 10px;backdrop-filter:blur(6px);"
           "font:12px/1.4 system-ui,sans-serif")
    lnk = "color:#5eead4;text-decoration:none;padding:3px 8px;border-radius:6px;" \
          "border:1px solid #241f3a;white-space:nowrap;max-width:280px;overflow:hidden;" \
          "text-overflow:ellipsis;display:inline-block"
    dim = "color:#5d5878;font:11px ui-monospace,monospace"
    it = []
    it.append('<div style="%s">' % css)
    if prev_f:
        it.append('<a style="%s" href="%s" title="上一个（键盘 ←）">◀ %s</a>'
                  % (lnk, prev_f, prev_n))
    it.append('<a style="%s" href="index.html" title="回索引">索引</a>' % lnk)
    if next_f:
        it.append('<a style="%s" href="%s" title="下一个（键盘 →）">%s ▶</a>'
                  % (lnk, next_f, next_n))
    it.append('<span style="%s">← → 翻页</span>' % dim)
    it.append("</div>")
    js = []
    js.append("<script>(function(){")
    if prev_f:
        js.append("var P='%s';" % prev_f)
    if next_f:
        js.append("var N='%s';" % next_f)
    js.append("document.addEventListener('keydown',function(e){")
    js.append("if(e.target&&/^(INPUT|SELECT|TEXTAREA)$/.test(e.target.tagName))return;")
    if prev_f:
        js.append("if(e.key==='ArrowLeft'&&typeof P!=='undefined'){location.href=P;}")
    if next_f:
        js.append("if(e.key==='ArrowRight'&&typeof N!=='undefined'){location.href=N;}")
    js.append("});})();</script>")
    return "\n".join(it) + "\n" + "".join(js)


def inject_nav(path, block):
    t = io.open(path, encoding="utf-8").read()
    t = t.replace("</html>", block + "\n</html>", 1) if "</html>" in t else (t + "\n" + block + "\n")
    io.open(path, "w", encoding="utf-8", newline="\n").write(t)


def build_index(out_dir, title, pages, meta, per_batch, n_total):
    """索引页：一张扁平表 + 搜索框（99 条时靠搜比靠翻快）。"""
    kk = {"niagara": "Niagara", "cascade": "Cascade"}
    h = ['<!doctype html><html lang="zh"><meta charset="utf-8">',
         "<title>%s 预览索引</title>" % title,
         "<style>body{background:#12161c;color:#dfe6ef;font:14px/1.6 system-ui,sans-serif;"
         "margin:0;padding:26px 32px}"
         "h1{font-size:20px;margin:0 0 4px}sub{color:#8b98a8;font-size:13px}"
         "table{border-collapse:collapse;margin:14px 0 30px;width:100%;max-width:1100px}"
         "th,td{border-bottom:1px solid #232a34;padding:5px 10px;text-align:left;font-size:13px}"
         "th{color:#8b98a8;font-weight:600;position:sticky;top:0;background:#12161c}"
         "code{color:#9ae66e}a{color:#5eead4;text-decoration:none}a:hover{text-decoration:underline}"
         ".k{color:#8b98a8;font-size:12px}"
         "#q{width:320px;background:#161327;color:#dfe6ef;border:1px solid #241f3a;"
         "border-radius:6px;padding:7px 10px;font:13px ui-monospace,monospace;margin:8px 0}</style>",
         "<h1>%s 预览索引</h1>" % title,
         "<sub>%d 个 effect / %d 页%s · 一页一个 · 键盘 ← → 直接翻页</sub>"
         % (n_total, len(pages), "（每页 %d 个）" % per_batch if per_batch > 1 else ""),
         '<input id="q" placeholder="搜 effect 名 / 源资产…（如 fire、drainlife）" autofocus>',
         "<table id=t><tr><th>#</th><th>effect 名</th><th>源类型</th><th>源资产</th>"
         "<th>emitter</th><th>页面</th></tr>"]
    for i, pg in enumerate(pages, 1):
        for r in pg["rows"]:
            h.append("<tr><td>%d</td><td><a href=\"%s\"><code>%s</code></a></td>"
                     "<td>%s</td><td class=\"k\">%s</td><td>%d</td><td class=\"k\">%s</td></tr>"
                     % (i, pg["file"], r["name"], kk.get(r["kind"], r["kind"]),
                        r["asset"], r["emitters"], pg["file"]))
    h.append("</table>")
    h.append("<script>(function(){var q=document.getElementById('q'),"
             "rows=document.querySelectorAll('#t tr');"
             "q.addEventListener('input',function(){var s=q.value.toLowerCase();"
             "for(var i=1;i<rows.length;i++){var t=rows[i].textContent.toLowerCase();"
             "rows[i].style.display=(!s||t.indexOf(s)>=0)?'':'none';}});})();</script>")
    open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8", newline="\n").write("\n".join(h) + "\n")


def main():
    ap = argparse.ArgumentParser(description="一批 particle XML -> 预览页套装（默认一页一个 + 索引页）")
    ap.add_argument("--xml-dir", default=paths.out("xml"), help="XML 目录（默认 数据根/output/xml）")
    ap.add_argument("--out-dir", default=paths.out("preview"), help="输出目录（默认 数据根/output/preview）")
    ap.add_argument("--per-batch", type=int, default=1, help="每页几个 effect（默认 1 = 一页一个，打开最快）")
    ap.add_argument("--cols", type=int, default=3, help="每页网格列数（只影响每页多个时）")
    ap.add_argument("--spacing", type=float, default=5.5, help="槽位间距，米（只影响每页多个时）")
    ap.add_argument("--title", default="LWN 迁移特效", help="页面标题前缀")
    ap.add_argument("--latin", default="LWN × FCS migrated", help="标题右侧拉丁副标题")
    ap.add_argument("--prefix", default="lwn_batch", help="一页多个时的文件名前缀")
    ap.add_argument("--no-nav", action="store_true", help="不注入上一个/下一个导航条")
    ap.add_argument("--no-tex", action="store_true", help="不配 UE 贴图（全部回落 smoke_d）")
    a = ap.parse_args()

    files = sorted(f for f in os.listdir(a.xml_dir) if f.lower().endswith(".xml"))
    if not files:
        raise SystemExit("XML 目录里没有 .xml：%s" % a.xml_dir)
    meta = load_meta()
    if not meta:
        print("  !! 读不到 %s —— 索引页的「源类型/源资产」两列会全是 ?" % paths.out("parsed"))

    os.makedirs(a.out_dir, exist_ok=True)
    # 🔴 每页几个 = 启动耗时的唯一变量（2026-09-20 探针实测「导航 → 脚本跑完」：
    #    1 个 = 0.70s · 9 个 = 0.82s · 99 个 = 4.24s）。默认 1。
    batches = ([files] if a.per_batch <= 0
               else [files[i:i + a.per_batch] for i in range(0, len(files), a.per_batch)])

    # 先算好每页的文件名（一页一个就用 effect 名做文件名，一眼知道是哪个）
    plan = []
    for bi, group in enumerate(batches, 1):
        rows_all = []
        for f in group:
            rows_all += [dict(name=n, emitters=e) for n, e in read_effects(os.path.join(a.xml_dir, f))]
        if len(rows_all) == 1 and len(group) == 1:
            page = safe_name(rows_all[0]["name"]) + ".html"
        elif len(group) == 1:
            page = safe_name(os.path.basename(group[0])[:-4]) + ".html"
        else:
            page = "%s_%02d.html" % (a.prefix, bi)
        plan.append(dict(files=group, page=page, bi=bi))

    print("XML %d 个 -> %d 页（%s，%d 列）"
          % (len(files), len(plan),
             "一页一个" if a.per_batch == 1 else ("全部一页" if a.per_batch <= 0 else "每页 %d 个" % a.per_batch),
             a.cols))
    print("输出目录: %s" % a.out_dir)

    pages = []
    for i, pg in enumerate(plan):
        out = os.path.join(a.out_dir, pg["page"])
        cmd = [sys.executable, MAKER,
               "--xml"] + [os.path.join(a.xml_dir, f) for f in pg["files"]] + [
               "-o", out,
               "--title", (a.title if len(plan) == 1 else "%s %s" % (a.title, pg["page"][:-5])),
               "--latin", a.latin,
               "--cols", str(a.cols),
               "--spacing", str(a.spacing)]
        if a.no_tex:
            cmd.append("--no-tex")
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if r.returncode != 0:
            print((r.stdout or "") + (r.stderr or ""))
            raise SystemExit("第 %d 页生成失败（%s）" % (i + 1, pg["page"]))
        rows = []
        for f in pg["files"]:
            root = ET.parse(os.path.join(a.xml_dir, f)).getroot()
            for eff in root.findall("effect"):
                nm = eff.get("name") or f[:-4]
                kind, asset = look_meta(meta, nm)
                rows.append({"name": nm, "kind": kind, "asset": asset,
                             "emitters": len(eff.findall("emitters/emitter"))})
        pages.append(dict(file=pg["page"], rows=rows, bi=pg["bi"]))
        print("  · %-34s %d 个  (%.0f KB)" % (pg["page"], len(rows), os.path.getsize(out) / 1024.0))

    # 导航条（上一个 / 索引 / 下一个）—— 一页一个时必须的，否则只能回索引点
    if not a.no_nav:
        for i, pg in enumerate(pages):
            prev_ = pages[i - 1] if i > 0 else None
            next_ = pages[i + 1] if i + 1 < len(pages) else None
            inject_nav(os.path.join(a.out_dir, pg["file"]),
                       nav_block(prev_ and prev_["file"],
                                 prev_ and (prev_["rows"][0]["name"] if prev_["rows"] else prev_["file"]),
                                 next_ and next_["file"],
                                 next_ and (next_["rows"][0]["name"] if next_["rows"] else next_["file"])))
        print("  · 每页注入导航条（键盘 ← → 翻页）")

    build_index(a.out_dir, a.title, pages, meta, a.per_batch, len(files))
    json.dump([{"file": p["file"], "effects": [r["name"] for r in p["rows"]]} for p in pages],
              open(os.path.join(a.out_dir, "_index.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("  · index.html + _index.json")

    # 上一轮留下、这轮不再生成的页面 —— 只提醒，不删（删除由人决定）
    cur = {p["file"] for p in pages} | {"index.html", "_index.json"}
    stale = [f for f in os.listdir(a.out_dir)
             if f.endswith(".html") and f not in cur and not f.startswith("_")]
    if stale:
        print("  !! 这些旧页面这轮没重新生成（要删自己删，共 %d 个）: %s%s"
              % (len(stale), ", ".join(sorted(stale)[:6]), " …" if len(stale) > 6 else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())

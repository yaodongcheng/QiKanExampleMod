#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""build_preview_set.py —— 一键把一整批 particle XML 出成「预览页套装」（批次页 + 索引页）。

【补的是哪一环】
  `lwn_preview/` 那 11 页当初是**临时命令行**跑出来的（目录里没有任何脚本能重跑它），
  于是一旦 XML 变了、想换每页张数、想换贴图，就得把 11 条命令重敲一遍。
  本脚本把它变成一条命令，并且顺手把索引页里原来填不上的两列（源类型 / 源资产）填对
  —— 那两个值就在解析产物 `output/parsed/*.json` 的 `kind` / `asset` 字段里。

【做什么】
  ① 扫 XML 目录（默认 数据根/output/xml）
  ② 每 N 个一批（默认 9 = 3×3），调 make_preview.py 出一页
  ③ 写 index.html 索引页（批次链接 + 每个 effect 的源类型/源资产/emitter 数）
  ④ 写 _index.json（批次 ↔ effect 映射，供别处引用）

【用法】
    python tools/particle-pipeline/preview/build_preview_set.py
    python tools/particle-pipeline/preview/build_preview_set.py --per-batch 6 --cols 3
    python tools/particle-pipeline/preview/build_preview_set.py --xml-dir <目录> --out-dir <目录>

  🔴 产物落在**数据根**（D:/BrainMaker/骑砍2粒子特效复刻/output/preview），不在仓库里
     —— 预览页是数据不是代码（见 tools/particle-pipeline/README.md §1）。
"""
import argparse
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
import paths                      # 两根定位 TOOL/DATA —— 见工具链根 paths.py

HERE = os.path.dirname(os.path.abspath(__file__))
MAKER = os.path.join(HERE, "make_preview.py")


def load_meta():
    """从解析产物读「源类型 / 源资产」：asset 名 -> (kind, asset)。

    对位规则：XML 里的 effect 名带 `lwn_` 前缀（如 `lwn_ns_fireball`），
    对应 UE 资产 `NS_Fireball` —— 去掉前缀后不分大小写比对。
    对不上就返回 "?"（不猜、不编）。
    """
    meta = {}
    pdir = paths.out("parsed")
    if not os.path.isdir(pdir):
        return meta
    for fn in os.listdir(pdir):
        if not fn.endswith(".json") or fn.startswith("_"):
            continue
        try:
            d = json.load(open(os.path.join(pdir, fn), encoding="utf-8"))
        except Exception:
            continue
        a = (d.get("asset") or "").strip()
        if a:
            meta[a.lower()] = (d.get("kind") or "?", a)
    return meta


def look_meta(meta, effect_name):
    key = effect_name[4:] if effect_name.startswith("lwn_") else effect_name
    return meta.get(key.lower(), ("?", "?"))


def count_emitters(xml_path):
    try:
        root = ET.parse(xml_path).getroot()
    except Exception:
        return 0
    return sum(len(e.findall("emitters/emitter")) for e in root.findall("effect"))


def main():
    ap = argparse.ArgumentParser(description="一批 particle XML -> 预览页套装（批次页 + 索引页）")
    ap.add_argument("--xml-dir", default=paths.out("xml"), help="XML 目录（默认 数据根/output/xml）")
    ap.add_argument("--out-dir", default=paths.out("preview"), help="输出目录（默认 数据根/output/preview）")
    ap.add_argument("--per-batch", type=int, default=9, help="每页几个 effect（默认 9 = 3×3）")
    ap.add_argument("--cols", type=int, default=3, help="每页网格列数（默认 3）")
    ap.add_argument("--spacing", type=float, default=5.5, help="槽位间距，米（默认 5.5）")
    ap.add_argument("--title", default="LWN 迁移特效", help="页面标题前缀")
    ap.add_argument("--latin", default="LWN × FCS migrated", help="标题右侧拉丁副标题")
    ap.add_argument("--prefix", default="lwn_batch", help="批次页文件名前缀")
    ap.add_argument("--no-tex", action="store_true", help="不配 UE 贴图（全部回落 smoke_d）")
    a = ap.parse_args()

    files = sorted(f for f in os.listdir(a.xml_dir) if f.lower().endswith(".xml"))
    if not files:
        raise SystemExit("XML 目录里没有 .xml：%s" % a.xml_dir)
    meta = load_meta()
    if not meta:
        print("  !! 读不到 %s —— 索引页的「源类型/源资产」两列会全是 ?" % paths.out("parsed"))

    os.makedirs(a.out_dir, exist_ok=True)
    batches = [files[i:i + a.per_batch] for i in range(0, len(files), a.per_batch)]
    print("XML %d 个 -> %d 页（每页 %d 个，%d 列）" % (len(files), len(batches), a.per_batch, a.cols))
    print("输出目录: %s" % a.out_dir)

    index = []
    for bi, group in enumerate(batches, 1):
        page = "%s_%02d.html" % (a.prefix, bi)
        out = os.path.join(a.out_dir, page)
        cmd = [sys.executable, MAKER,
               "--xml"] + [os.path.join(a.xml_dir, f) for f in group] + [
               "-o", out,
               "--title", "%s %02d" % (a.title, bi),
               "--latin", a.latin,
               "--cols", str(a.cols),
               "--spacing", str(a.spacing)]
        if a.no_tex:
            cmd.append("--no-tex")
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if r.returncode != 0:
            print((r.stdout or "") + (r.stderr or ""))
            raise SystemExit("第 %d 页生成失败（%s）" % (bi, page))
        rows = []
        for f in group:
            root = ET.parse(os.path.join(a.xml_dir, f)).getroot()
            for eff in root.findall("effect"):
                nm = eff.get("name") or f[:-4]
                kind, asset = look_meta(meta, nm)
                rows.append({"name": nm, "kind": kind, "asset": asset,
                             "emitters": len(eff.findall("emitters/emitter"))})
        index.append({"batch": bi, "file": page, "effects": [x["name"] for x in rows], "rows": rows})
        print("  · %s  %d 个  (%.0f KB)" % (page, len(rows), os.path.getsize(out) / 1024.0))

    # ---- 索引页 ----
    kk = {"niagara": "Niagara", "cascade": "Cascade"}
    h = ['<!doctype html><html lang="zh"><meta charset="utf-8">',
         "<title>%s 预览索引</title>" % a.title,
         "<style>body{background:#12161c;color:#dfe6ef;font:14px/1.6 system-ui,sans-serif;margin:0;padding:28px 34px}"
         "h1{font-size:20px;margin:0 0 6px}sub{color:#8b98a8}"
         "table{border-collapse:collapse;margin:16px 0 26px;width:100%;max-width:1180px}"
         "th,td{border-bottom:1px solid #232a34;padding:6px 10px;text-align:left;font-size:13px}"
         "th{color:#8b98a8;font-weight:600}code{color:#9ae66e}a{color:#5eead4;text-decoration:none}"
         "a:hover{text-decoration:underline}.k{color:#8b98a8;font-size:12px}</style>",
         "<h1>%s 预览索引</h1>" % a.title,
         "<sub>%d 个 effect / %d 页 · 每页 %d 个 %d 列排布 · 离线自包含（three.js 已内联）</sub>"
         % (len(files), len(batches), a.per_batch, a.cols)]
    for b in index:
        h.append('<h2 style="font-size:15px;margin:22px 0 4px"><a href="%s">批 %02d</a> '
                 '<span class="k">%d 个</span></h2>' % (b["file"], b["batch"], len(b["rows"])))
        h.append("<table><tr><th>#</th><th>effect 名</th><th>源类型</th><th>源资产</th><th>emitter</th></tr>")
        for i, r in enumerate(b["rows"], 1):
            h.append("<tr><td>%d</td><td><code>%s</code></td><td>%s</td>"
                     '<td class="k">%s</td><td>%d</td></tr>'
                     % (i, r["name"], kk.get(r["kind"], r["kind"]), r["asset"], r["emitters"]))
        h.append("</table>")
    open(os.path.join(a.out_dir, "index.html"), "w", encoding="utf-8", newline="\n").write("\n".join(h) + "\n")
    json.dump([{"batch": b["batch"], "file": b["file"], "effects": b["effects"]} for b in index],
              open(os.path.join(a.out_dir, "_index.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("  · index.html + _index.json")

    # 上一轮留下的、这轮不再生成的批次页 —— 只提醒，不删（删除由人决定）
    cur = {"%s_%02d.html" % (a.prefix, b["batch"]) for b in index} | {"index.html", "_index.json"}
    stale = [f for f in os.listdir(a.out_dir)
             if f.startswith(a.prefix) and f.endswith(".html") and f not in cur]
    if stale:
        print("  !! 这些旧批次页这轮没重新生成（要删自己删）: %s" % ", ".join(sorted(stale)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

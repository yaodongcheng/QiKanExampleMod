#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""查看器「谁属于哪一格」的统一入口 —— 一条命令把 GLB/TRF 落成某个数据集的一格，并（可选）登记进 dataset 下拉。

═══════════════════════════════════════════════════════════════════════════
查看器的「归属」由三层决定，**烘图脚本一概不知道**：

  ① 数据集（左上角下拉那个「分类」）
        → 只认 `viewer/datasets/index.json` 的 datasets[{id,name}]；id 必须 = datasets/<id>/ 目录名。
          没登记 = 下拉里没有，`?ds=<id>` 也会被回落掉。
  ② 一格（左面板「角色」列表里那一行）
        → 只认 `viewer/datasets/<id>/dataset.json` 的 `sides[<side名>]`：
          label 显示名 / color 色 / slot 左右位置(x) / rotY 朝向 / footBones 脚骨名
          files[] 一个或多个 GLB（相对 datasets/<id>/，可 ../别的数据集/assets/x.glb）
          可选 mode / pairAnchor / fixedClipReplace / clipStart / stripPositionTracks / freezeRootH
          🔴 JSON 里键的顺序 = 主次：第 1 个 = 主轴(src)，第 2 个 = dst，其余 = 额外展示侧。
  ③ 底部条目（clip）
        → **不写在 dataset.json 里** = 所有 side 的 GLB 里动画名的并集（跨 side 同名 = 同一条，一起播）。
          可选 `clipsFile` 指向 manifest 做筛选/排序。

本脚本把这些一次做完：烘 GLB（可选） → 写 dataset.json 的该 side → （可选）写 index.json。

用法
  # 只登记/更新一格，GLB 已经现成
  python pipeline/common/make_viewer_dataset.py --ds ue_fly_comp --side src \
      --label "① 源动画的 pose（UE 小白人）" --color "#7fa8d8" --slot -1.9 --rotY 0 \
      --footbones foot_l,foot_r,ball_l,ball_r \
      --url ../ue_flight/assets/ue_mannequin_flight.glb --front \
      --title "…" --subtitle "…" --register-index --index-name "…"

  # 从 TRF 现烘一格（走 glb_pack_retargeted.py --trfdir）
  python pipeline/common/make_viewer_dataset.py --ds ue_fly_comp --side comp \
      --label "③ 合成后的动画（骑砍2）" --color "#e0b070" --slot 1.9 --rotY 3.14159265 \
      --footbones l_toe0,r_toe0,l_foot,r_foot \
      --base input/target/bannerlord/body/body_male_a.fbx --trfdir output/trf \
      --map "fly_A_Flight_HoverMove_A_LeanL=A_Flight_HoverMove_A_L_Add,…"

  --dry   只打印会做什么，不落盘
  --front 把这一格放到 sides 的第一个（= 主轴）
═══════════════════════════════════════════════════════════════════════════
"""
import argparse
import io
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DSDIR = os.path.join(ROOT, "viewer/datasets")
BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"


def load_json(p, default):
    if os.path.isfile(p):
        return json.load(io.open(p, encoding="utf-8"))
    return default


def save_json(p, obj):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    io.open(p, "w", encoding="utf-8", newline="\n").write(
        json.dumps(obj, ensure_ascii=False, indent=2) + "\n")


def main():
    ap = argparse.ArgumentParser(description="查看器数据集/一格的统一落盘入口")
    ap.add_argument("--ds", required=True, help="数据集 id（= viewer/datasets/<id>/）")
    ap.add_argument("--side", help="这一格的键名（sides 里的 key），如 src / dst / comp")
    ap.add_argument("--label", help="左面板显示名")
    ap.add_argument("--color", default=None)
    ap.add_argument("--slot", type=float, default=0.0, help="左右位置（米）")
    ap.add_argument("--roty", "--rotY", dest="roty", type=float, default=0.0)
    ap.add_argument("--footbones", default="", help="脚骨名，逗号分隔（贴地/身高用）")
    ap.add_argument("--mode", default=None, help="ground / src（可选）")
    ap.add_argument("--url", help="直接给现成 GLB（相对 datasets/<ds>/）")
    ap.add_argument("--base", help="现烘：带网格的骨架 FBX")
    ap.add_argument("--trfdir", help="现烘：TRF 目录")
    ap.add_argument("--map", dest="mapping", help="现烘：TRF名=clip名，逗号分隔")
    ap.add_argument("--front", action="store_true", help="把这一格放到 sides 第一位（主轴）")
    ap.add_argument("--title", help="dataset.json 的 title（可选）")
    ap.add_argument("--subtitle", help="dataset.json 的 subtitle（可选）")
    ap.add_argument("--register-index", action="store_true", help="登记/更新 index.json")
    ap.add_argument("--index-name", help="index.json 里显示的名字")
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args()

    dsdir = os.path.join(DSDIR, a.ds)
    djson = os.path.join(dsdir, "dataset.json")
    out_glb_rel = "assets/%s.glb" % (a.side or "main")
    out_glb = os.path.join(dsdir, out_glb_rel)

    plan = []
    # ① 烘 GLB
    if a.base and a.trfdir and a.mapping:
        if not a.side:
            print("!! 现烘模式需要 --side（GLB 名用它）"); return 2
        cmd = [BLENDER, "-b", "--factory-startup", "--python",
               os.path.join(HERE, "glb_pack_retargeted.py"), "--",
               "--base", a.base if os.path.isabs(a.base) else os.path.join(ROOT, a.base),
               "--trfdir", a.trfdir if os.path.isabs(a.trfdir) else os.path.join(ROOT, a.trfdir),
               "--clips", a.mapping, "--out", out_glb, "--fps", "30"]
        plan.append("烘 GLB -> " + out_glb)
        if not a.dry:
            os.makedirs(os.path.dirname(out_glb), exist_ok=True)
            if subprocess.run(cmd).returncode != 0:
                print("!! 烘 GLB 失败"); return 3
        url = out_glb_rel
    elif a.url:
        url = a.url
    else:
        url = None

    # ② 写 dataset.json
    ds = load_json(djson, {"name": a.ds, "title": "", "subtitle": "", "sides": {}})
    ds.setdefault("sides", {})
    ds["name"] = ds.get("name") or a.ds
    if a.title: ds["title"] = a.title
    if a.subtitle: ds["subtitle"] = a.subtitle
    if a.side:
        if not url:
            print("!! 这一格没给 --url / 现烘参数，不知道指向哪个 GLB"); return 2
        side = dict(ds["sides"].get(a.side) or {})
        side["label"] = a.label or side.get("label") or a.side
        if a.color: side["color"] = a.color
        side["slot"] = a.slot
        side["rotY"] = a.roty
        if a.footbones:
            side["footBones"] = [x.strip() for x in a.footbones.split(",") if x.strip()]
        if a.mode: side["mode"] = a.mode
        side["files"] = [{"id": a.side, "url": url}]
        if a.front:
            rest = {k: v for k, v in ds["sides"].items() if k != a.side}
            ds["sides"] = dict([(a.side, side)] + list(rest.items()))
        else:
            ds["sides"][a.side] = side
        plan.append("写 %s 的 sides[%s]（label=%s, slot=%s, url=%s）"
                    % (djson, a.side, side["label"], a.slot, url))
    if not a.dry:
        save_json(djson, ds)

    # ③ 登记 index.json
    if a.register_index:
        ip = os.path.join(DSDIR, "index.json")
        idx = load_json(ip, {"default": a.ds, "datasets": []})
        ids = [d["id"] for d in idx["datasets"]]
        nm = a.index_name or a.title or a.ds
        if a.ds in ids:
            for d in idx["datasets"]:
                if d["id"] == a.ds: d["name"] = nm
            plan.append("更新 index.json 里 %s 的显示名" % a.ds)
        else:
            idx["datasets"].append({"id": a.ds, "name": nm})
            plan.append("在 index.json 新增数据集 %s（%s）" % (a.ds, nm))
        if not a.dry:
            save_json(ip, idx)

    print("计划 / 已做：")
    for p in plan: print("   ", p)
    if a.dry: print("（--dry：没落盘）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

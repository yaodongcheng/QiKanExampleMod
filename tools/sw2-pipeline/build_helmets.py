# -*- coding: utf-8 -*-
"""build_helmets.py —— 14 个戴盔角色的【独立头盔】（`head_armor` 物品，可摘换）。

依据：`Knowledge/骑砍2盔甲资产工程.md` §3.5（头盔跟着脸形参数缩放）+ 计划 TODO#5（用户选 C：
独立件 + `head_armor` 物品）。挑件表 `parts_table.py` 的 `helmet` 列**已人工标注**每个角色的兜件（idx），
本脚本把 idx 翻成子网格号，走同一条甲管线（选件 → Y 镜像 → 骨架重定向 → 6 级 LOD → 贴图）。

命名：`taikou_<slug>_helmet_a`（物品/网格/材质同名，同甲的制式）。

用法：
    python tools/sw2-pipeline/build_helmets.py --only L00_yukimura
    python tools/sw2-pipeline/build_helmets.py            # 14 人全做
"""
import argparse
import csv
import io
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))
from parts_table import TABLE  # noqa: E402
import build_armors as A  # noqa: E402  （复用骨架/图集查找与 run()）

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BUILD = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_armor.py")
TEX = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_textures.py")
OUT = os.path.join(REPO, "tools", "armor-pipeline", "out")


def helmet_subs(key):
    """挑件表的兜件 idx → 子网格号（读该角色的零件表）。"""
    idxs = TABLE[key].get("helmet") or []
    if not idxs:
        return []
    p = os.path.join(REPO, "Debug", "offline", "sw2_parts", "%s_parts.csv" % key)
    if not os.path.isfile(p):
        return []
    r = list(csv.reader(io.open(p, encoding="utf-8-sig")))
    h = r[0]
    out = []
    for d in (dict(zip(h, x)) for x in r[1:]):
        try:
            i = int(d.get("idx") or -1)
        except ValueError:
            continue
        if i in idxs:
            m = re.search(r'submesh_(\d+)', d.get("name") or "")
            if m:
                out.append(int(m.group(1)))
    return sorted(set(out))


def face_sub(key):
    """挑件表的 face idx → 子网格号（颏带的来源件就是脸壳件）。"""
    idxs = TABLE[key].get("face") or []
    p = os.path.join(REPO, "Debug", "offline", "sw2_parts", "%s_parts.csv" % key)
    if not idxs or not os.path.isfile(p):
        return None
    r = list(csv.reader(io.open(p, encoding="utf-8-sig")))
    for d in (dict(zip(r[0], x)) for x in r[1:]):
        try:
            i = int(d.get("idx") or -1)
        except ValueError:
            continue
        if i in idxs:
            m = re.search(r'submesh_(\d+)', d.get("name") or "")
            if m:
                return int(m.group(1))
    return None


def helm_strap_args(key):
    """🔴 颏带并入头盔：来源件 = 脸壳件，碎片 = parts_table 的 strap 种子点（2026-09-15）。"""
    st = list(TABLE[key].get("strap") or [])
    fs = face_sub(key)
    if not st or fs is None:
        return []
    # 🔴 只并【耳侧那几块碎片】。下巴那一横条是**下颌皮面本身**（头那边靠改 UV 让它不显示），
    #    并进盔会跟头重叠打架（2026-09-15 用户实机反馈后修正）。
    return ["--strap-from", str(fs),
            "--strap-seed", ";".join(",".join("%.2f" % v for v in sd) for sd in st)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()
    skel = A.find_skel()
    if not skel:
        print("[FATAL] 找不到 human_skeleton.fbx")
        return 2
    keys = [k for k in (args.only or sorted(TABLE)) if TABLE[k].get("helmet")]
    done, fail = [], []
    for key in keys:
        slug = A.TABLE[key]["asset"][len("head_"):-len("_a")]
        name = "taikou_%s_helmet_a" % slug
        out_fbx = os.path.join(OUT, name + ".fbx")
        if os.path.isfile(out_fbx) and not args.force:
            print("  [跳过] %-16s 已有" % key); continue
        subs = helmet_subs(key)
        if not subs:
            print("  ❌ %-16s 兜件 idx 翻不出子网格号（%s）" % (key, TABLE[key].get("helmet")))
            fail.append(key); continue
        src = os.path.join(A.SRC_DIR, key + ".fbx")
        print("  ▶ %-16s 兜件 sub%s" % (key, subs))
        rc, out = A.run([A.BLENDER, "-b", "--python", BUILD, "--",
                         "--src", src, "--skel", skel, "--out", OUT, "--name", name,
                         "--parts-idx", ",".join(map(str, subs)),
                         "--r", A.R_TORSO, "--r-arms", A.R_ARMS,
                         # 🔴 兜里混着飞出去的碎片（幸村那件有 2 片飞在 x=±44.6cm）→ 包围盒被撑到 103cm，
                         #    缩完就是 0.8 米的盖子。剔碎片后兜主体 ~25cm。判据同 build_head.prune_far。
                         "--prune-far", "4.0", "--keep-head-frags"] + helm_strap_args(key), "build_helmet")
        if rc != 0 or not os.path.isfile(out_fbx):
            print("     ❌ 失败（exit %d）" % rc)
            for l in [x for x in out.splitlines() if x.strip()][-5:]:
                print("        " + l[:150])
            fail.append(key); continue
        dif = A.find_diffuse(key)
        if dif:
            A.run([A.BLENDER, "-b", "--python", TEX, "--",
                   "--armor", out_fbx, "--src", src, "--diffuse", dif,
                   "--out", OUT, "--name", name, "--parts-idx", ",".join(map(str, subs)),
                   "--ao", "0.35"], "helm_tex")
        _st = [l.strip() for l in out.splitlines() if "颏带并入" in l]
        print("     ✅ %s（%.0f KB）%s" % (name, os.path.getsize(out_fbx) / 1024.0,
                                          ("  " + _st[0]) if _st else ""))
        done.append(key)
    print("\n完成 %d · 失败 %d%s" % (len(done), len(fail), ("（" + " ".join(fail) + "）") if fail else ""))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())

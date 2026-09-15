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
from troop_parts_table import TROOP_TABLE, row_of  # noqa: E402
import build_armors as A  # noqa: E402  （复用骨架/图集查找与 run()）

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BUILD = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_armor.py")
TEX = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_textures.py")
OUT = os.path.join(REPO, "tools", "armor-pipeline", "out")


def helmet_subs(key):
    """挑件表的兜件 idx → 【精确网格名】列表（读该角色的零件表）。

    🔴 返回名字而不是号（2026-09-15 深夜修）：`submesh_0` 与 `submesh_0.001` 解析出同一个号，
       按号选件会把兜的"兄弟件"一起选中，再被 --prune-far 当碎片剔掉。
       实测上杉谦信：兜是 `submesh_0..._0000.001`（242 顶点），按号选 → 输出只剩 **18 顶点**。
    """
    idxs = row_of(key, TABLE).get("helmet") or []
    if not idxs:
        return []
    p = os.path.join(REPO, "Debug", "offline", "sw2_parts", "%s_parts.csv" % key)
    if not os.path.isfile(p):
        return []
    r = list(csv.reader(io.open(p, encoding="utf-8-sig")))
    out = []
    for d in (dict(zip(r[0], x)) for x in r[1:]):
        try:
            i = int(d.get("idx") or -1)
        except ValueError:
            continue
        if i in idxs:
            nm = (d.get("name") or "").strip()
            if nm and nm not in out:
                out.append(nm)
    return out


def slug_of(key):
    """资产 slug：武将表从 `asset` 名推（head_yukimura_a → yukimura）；兵种表自带。"""
    r = row_of(key, TABLE)
    return r["slug"] if "slug" in r else r["asset"][len("head_"):-len("_a")]


def face_sub_name(key):
    """挑件表的 face idx → 【精确网格名】（颏带的来源件就是脸壳件）。"""
    idxs = row_of(key, TABLE).get("face") or []
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
            nm = (d.get("name") or "").strip()
            if nm:
                return nm
    return None


def face_sub(key):
    """挑件表的 face idx → 子网格号（颏带的来源件就是脸壳件）。"""
    idxs = row_of(key, TABLE).get("face") or []
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


def helm_whole_subs(key):
    """`helmet_whole` 列 → 子网格号集合（整块要的纯兜件，见 build_armor 里 `--helmet-whole`）。"""
    r = row_of(key, TABLE)
    idxs = r.get("helmet_whole") or []
    if not idxs:
        return []
    p = os.path.join(REPO, "Debug", "offline", "sw2_parts", "%s_parts.csv" % key)
    if not os.path.isfile(p):
        return []
    i2n, out = {}, []
    for row in csv.DictReader(io.open(p, encoding="utf-8-sig")):
        try:
            i2n[int(row["idx"])] = (row.get("name") or "").strip()
        except (TypeError, ValueError):
            pass
    for i in idxs:
        nm = i2n.get(i)
        m = re.search(r"submesh_(\d+)", nm or "")
        if m and int(m.group(1)) not in out:
            out.append(int(m.group(1)))
    return out


def helm_strap_args(key):
    """🔴 颏带并入头盔：来源件 = 脸壳件，碎片 = parts_table 的 `strap` 种子点。

    🔴 **只并【耳侧那几块碎片】，不并 `strap_bone`**（2026-09-15 深夜实测回退）：
       把 `strap_bone`（下巴那一横条）也并进来试过 —— 渲染出来兜上挂着一块**肉色**，
       因为那批顶点是**下颌皮肤本身**（`bone_59` 覆盖的是下巴，不是带子）。
       且头那边一剪就开黑腔。⇒ 这条带子是画在下颌皮面上的贴图，几何上就是下巴。
    """
    st = list(row_of(key, TABLE).get("strap") or [])
    fs = face_sub(key)
    if not st or fs is None:
        return []
    return ["--strap-from", str(fs),
            "--strap-seed", ";".join(",".join("%.2f" % v for v in sd) for sd in st)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--set", default="lords", choices=["lords", "troops"])
    args = ap.parse_args()
    skel = A.find_skel()
    if not skel:
        print("[FATAL] 找不到 human_skeleton.fbx")
        return 2
    src = TROOP_TABLE if args.set == "troops" else TABLE
    keys = [k for k in (args.only or sorted(src)) if row_of(k, TABLE).get("helmet")]
    done, fail = [], []
    for key in keys:
        slug = slug_of(key)
        name = "taikou_%s_helmet_a" % slug
        out_fbx = os.path.join(OUT, name + ".fbx")
        if os.path.isfile(out_fbx) and not args.force:
            print("  [跳过] %-16s 已有" % key); continue
        subs = helmet_subs(key)
        if not subs:
            print("  ❌ %-16s 兜件 idx 翻不出网格名（%s）" % (key, row_of(key, TABLE).get("helmet")))
            fail.append(key); continue
        src = os.path.join(A.SRC_DIR, key + ".fbx")
        print("  ▶ %-16s 兜件 %d 块" % (key, len(subs)))
        rc, out = A.run([A.BLENDER, "-b", "--python", BUILD, "--",
                         "--src", src, "--skel", skel, "--out", OUT, "--name", name,
                         "--parts-name", "|".join(subs),
                         "--r", A.R_TORSO, "--r-arms", A.R_ARMS,
                         # 🔴 兜里混着飞出去的碎片（幸村那件有 2 片飞在 x=±44.6cm）→ 包围盒被撑到 103cm，
                         #    缩完就是 0.8 米的盖子。剔碎片后兜主体 ~25cm。判据同 build_head.prune_far。
                         # 🔴 `--rigid`：兜是刚体，整顶（含剪来的颏带）必须吃同一个重定向矩阵，
                         #    否则绑不同源骨的部件会被按各自的矩阵拉开（实机：政宗颏带跑到后脑、
                         #    忠胜钵侧被甩到 ±40cm）。见 build_armor.py 里 `--rigid` 的长注释。
                         # 🔴 `--double-sided-all`：兜上的**前立/月牙/小饰件是单面板**，
                         #    引擎材质层没有双面开关 → 背面被剔除，从另一侧看"什么都没有"
                         #    （实测长政金前立：开剔除后背面整个消失，见 Debug/offline/_hB_cull.png）。
                         #    兜件面数小（150~400 面），全量复制+翻面代价可忽略。
                         "--prune-far", "4.0", "--keep-head-frags", "--double-sided-all"]
                         + (["--helmet-whole", ",".join(str(x) for x in helm_whole_subs(key))]
                            if helm_whole_subs(key) else [])
                         + helm_strap_args(key), "build_helmet")
        if rc != 0 or not os.path.isfile(out_fbx):
            print("     ❌ 失败（exit %d）" % rc)
            for l in [x for x in out.splitlines() if x.strip()][-5:]:
                print("        " + l[:150])
            fail.append(key); continue
        dif = A.find_diffuse(key)
        if dif:
            # 🔴 贴图的 UV 范围要把【脸壳件】也算进来（2026-09-15 深夜）：颏带的 UV 画在
            #    **脸壳的图集区**，只按兜件的范围裁图 → 带子采样落空 → 渲染成一块肤色。
            #    （这就是老计划里挂着的「兜的贴图告警」那条。）
            fs_nm = face_sub_name(key)
            tex_names = list(subs) + ([fs_nm] if fs_nm else [])
            A.run([A.BLENDER, "-b", "--python", TEX, "--",
                   "--armor", out_fbx, "--src", src, "--diffuse", dif,
                   "--out", OUT, "--name", name, "--parts-name", "|".join(tex_names),
                   "--ao", "0.35"], "helm_tex")
        _st = [l.strip() for l in out.splitlines() if "颏带并入" in l]
        print("     ✅ %s（%.0f KB）%s" % (name, os.path.getsize(out_fbx) / 1024.0,
                                          ("  " + _st[0]) if _st else ""))
        done.append(key)
    print("\n完成 %d · 失败 %d%s" % (len(done), len(fail), ("（" + " ".join(fail) + "）") if fail else ""))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())

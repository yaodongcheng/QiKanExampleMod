# -*- coding: utf-8 -*-
"""build_armors.py —— 28 人批量出甲（读骨普查 → 自动选件 → 走甲管线）。

依赖
----
  · `Debug/offline/sw2_census/<角色>_census.csv`（先跑 `run_census.py`）
  · `tools/armor-pipeline/scripts/build_armor.py` + `build_textures.py`
  · 骑砍骨架 `human_skeleton.fbx`（build_armor_chain.py 里那份）

选件规则（**全自动，判据来自普查**）
------------------------------------
  · 甲件   = 普查 verdict 以「甲件」或「内衬着物」开头的所有件
  · 着物   = verdict 含「内衬着物」的那件 → `--kimono-idx`
  · 剔头   = verdict 含「剔头碎片」的件 → `--drop-head-idx`（头发+披风那类混装件）
  · 一律 `--no-hands`（籠手止于手腕）+ `--kimono-torso-only`
  · 缩放沿用幸村实测值：躯干 `--r 0.0120`、手臂 `--r-arms 0.0160`（可用 OVERRIDE 覆盖）

命名：`taikou_<角色slug>_do_a`（与幸村的 `taikou_yukimura_do_a` 同制式）。

用法：
    python tools/sw2-pipeline/build_armors.py --only L02_nobunaga     # 单人
    python tools/sw2-pipeline/build_armors.py                         # 全部
"""
import argparse
import csv
import io
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
from parts_table import TABLE  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
SRC_ROOT = r"D:\BrainMaker\战国无双2资产解包分析"
SRC_DIR = os.path.join(SRC_ROOT, "export", "fbx")
CENSUS_DIR = os.path.join(REPO, "Debug", "offline", "sw2_census")
OUT_DIR = os.path.join(REPO, "tools", "armor-pipeline", "out")
BUILD = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_armor.py")
TEX = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_textures.py")

R_TORSO = "0.0120"
R_ARMS = "0.0160"
# 每人可覆盖（键=角色 key，值=dict(r=..., r_arms=...)）——只在实测需要时加
OVERRIDE = {}
# 🔴 甲里**不要**的件（2026-09-15）：有两人的兜件被普查误判成甲件，混进了甲网格，
#    做头盔时要从甲里剔掉（否则头顶会挂一块兜）。头盔另见 build_helmets.py。
EXCLUDE = {
    "L36_hideyoshi": [0],    # 日轮冠（真身是兜件，见 parts_table.helmet=[0,7]）
    "L46_kanetsugu": [3],    # 兜件（parts_table.helmet=[6] → sub3）
    # 🔴 2026-09-15：普查把**马尾件**（sub1，115 顶点，92% 绑胸骨 bone_9）判成了「甲件」
    #    ——它是头发，整块不要（它绑的不是头骨族，剔头开关管不到它，只能整件排除）
    "L03_mitsuhide": [5],
}

# 🔴 普查漏标的「混装件」→ 强制按【碎片】剔头（头发+衣服同块时，只剔绑头骨族的碎片）。
#    判据必须逐人看：普查的 verdict 是「甲件」，但它里面混着头发。
#    光秀：sub2 = 整套和服 + 头顶头发（bone_11 114 顶点）；sub9 = 裙子 + 前刘海框（bone_11 70 + 面部骨 38）
DROPHEAD_EXTRA = {
    "L03_mitsuhide": [2, 9],
}


def find_skel():
    """骑砍骨架：官方 modding_resources 那份（README 指定）。"""
    mb2 = os.environ.get("MB2_PATH") or r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
    cands = [
        os.path.join(mb2, "modding_resources", "skeletons", "human_skeleton.fbx"),
        os.path.join(mb2, "Modules", "Native", "Assets", "human_skeleton.fbx"),
    ]
    for c in cands:
        if os.path.isfile(c):
            return c
    return None


def find_diffuse(key):
    """源图集：`web/textures/<角色>.png`（幸村那件的 --diffuse 就是这个位置）。"""
    cands = [os.path.join(SRC_ROOT, "web", "textures", key + ".png"),
             os.path.join(SRC_DIR, key + ".png")]
    for c in cands:
        if os.path.isfile(c):
            return c
    return None


def read_census(key):
    p = os.path.join(CENSUS_DIR, key + "_census.csv")
    if not os.path.isfile(p):
        return None
    with io.open(p, encoding="utf-8") as fh:
        return [r for r in csv.DictReader(fh) if r.get("sub") not in ("", "-1")]


def plan_for(key):
    """→ (parts_idx, kimono_idx, drop_head_idx, 说明) 或 (None, 原因)。"""
    rows = read_census(key)
    if not rows:
        return None, "没有骨普查（先跑 run_tools/sw2-pipeline/run_census.py）"
    armor, kimono, drophead = [], [], []
    for r in rows:
        v = r["verdict"]
        sub = int(r["sub"])
        if v.startswith("甲件") or v.startswith("内衬着物"):
            armor.append(sub)
        if "内衬着物" in v:
            kimono.append(sub)
        if "剔头碎片" in v:
            drophead.append(sub)
    armor = [s for s in armor if s not in EXCLUDE.get(key, [])]
    drophead = sorted(set(drophead) | set(DROPHEAD_EXTRA.get(key, [])))
    if not armor:
        return None, "普查里一件甲都没认出来（人工看接触图）"
    return dict(parts=armor, kimono=kimono, drophead=drophead), ""


def run(cmd, tag):
    p = subprocess.run(cmd, capture_output=True)
    out = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    return p.returncode, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--force", action="store_true", help="已存在的也重做")
    args = ap.parse_args()

    skel = find_skel()
    if not skel:
        print("[FATAL] 找不到 human_skeleton.fbx —— 先看 build_armor_chain.py 用的是哪份")
        return 2
    keys = args.only or sorted(TABLE)
    done, fail, skip = [], [], []
    for key in keys:
        slug = TABLE[key]["asset"][len("head_"):-len("_a")]
        name = "taikou_%s_do_a" % slug
        out_fbx = os.path.join(OUT_DIR, name + ".fbx")
        if os.path.isfile(out_fbx) and not args.force:
            print("  [跳过] %-16s 已有 %s" % (key, os.path.basename(out_fbx))); skip.append(key); continue
        plan, why = plan_for(key)
        if not plan:
            print("  ❌ %-16s %s" % (key, why)); fail.append(key); continue
        src = os.path.join(SRC_DIR, key + ".fbx")
        own = OVERRIDE.get(key, {})
        cmd = [BLENDER, "-b", "--python", BUILD, "--",
               "--src", src, "--skel", skel, "--out", OUT_DIR, "--name", name,
               "--parts-idx", ",".join(str(x) for x in plan["parts"]),
               "--kimono-idx", ",".join(str(x) for x in plan["kimono"]) or "0",
               "--r", own.get("r", R_TORSO), "--r-arms", own.get("r_arms", R_ARMS),
               "--no-hands", "--kimono-torso-only"]
        if plan["drophead"]:
            cmd += ["--drop-head-idx", ",".join(str(x) for x in plan["drophead"])]
        print("  ▶ %-16s 甲件 %s%s%s" % (key, plan["parts"],
              " 着物%s" % plan["kimono"] if plan["kimono"] else "",
              " 剔头%s" % plan["drophead"] if plan["drophead"] else ""))
        rc, out = run(cmd, "build_armor")
        if rc != 0 or not os.path.isfile(out_fbx):
            print("     ❌ 建甲失败（exit %d）" % rc)
            for l in [x for x in out.splitlines() if x.strip()][-6:]:
                print("        " + l[:150])
            fail.append(key); continue
        # 贴图
        dif = find_diffuse(key)
        if dif:
            rc2, out2 = run([BLENDER, "-b", "--python", TEX, "--",
                             "--armor", out_fbx, "--src", src, "--diffuse", dif,
                             "--out", OUT_DIR, "--name", name,
                             "--parts-idx", ",".join(str(x) for x in plan["parts"]), "--ao", "0.35"], "build_textures")
            tex_ok = rc2 == 0
        else:
            tex_ok = False
            print("     ⚠️ 找不到源图集 → 只出网格不出贴图")
        size = os.path.getsize(out_fbx)
        print("     ✅ %s（%.0f KB）贴图%s" % (name, size / 1024.0, "✅" if tex_ok else "⚠️ 缺"))
        done.append(key)
    print("\n完成 %d · 跳过 %d · 失败 %d%s" % (len(done), len(skip), len(fail),
          ("（失败：" + " ".join(fail) + "）") if fail else ""))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())

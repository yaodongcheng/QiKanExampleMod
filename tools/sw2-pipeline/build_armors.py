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
from troop_parts_table import TROOP_TABLE, row_of  # noqa: E402

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

# 🔴 甲 = 所有已加载件 − 绑头骨族的碎片（2026-09-15 T3）。
#
#   原则（用户裁定）：源模型是**一整块**，脸 / 眼 / 头发 / 兜 / 武器 / 甲 之间
#   **不允许有共用的面**——每个碎片只能归一个部位，甲 = 前面几个部位拿走之后的**补集**。
#
#   所以判据不能是「逐件点名」（点谁是谁、漏一个就漏一份），只能是「碎片级切一刀」：
#   · 兜那一刀 = `--keep-head-frags`（只留绑头骨的碎片）
#   · 甲那一刀 = `--drop-head-idx`（剔掉绑头骨的碎片）—— **两边同一判据，天然互补**
#
#   ⚠️ 为什么必须对【每一件】都切，不能只切挑件表 helmet/hair 列点到的件：
#      实测政宗那块 1026 顶点的「身甲 + 大金月牙」里有 13 个顶点绑 `bip01_head_13`，
#      位置在头顶上方 1.81m —— 它所在的件既不在 helmet 列也不在 hair 列，
#      按件点名永远漏它（渲出来就是甲上方飘着一小片）。
#
#   ⚠️ 也不能改成「查普查 verdict」：普查恰恰把这些件标成「甲件」
#      （秀吉 idx0/idx7、政宗 idx7、半藏 idx12、长政 idx7），关键词一条都匹配不到；
#      全 28 人里更没有任何一件带「剔头碎片」verdict → 那条自动通道一直是空转的。
PARTITION_HEAD_FRAGS = True


# 🔴 人已确认是【甲】、但普查没认出来的件（verdict「待看（碎片分散）」等）——按子网格号补进来。
#    2026-09-15：谦信 idx15（sub9，84 顶点）= **两片大布片/袍**（z 69.5~180.5 ≈ 1.3 米长，
#    披在身上），不是头盔垂带（按件渲图 `_hL_kenshin_sub9.png` 确认）。
#    普查判「待看」→ 从来没人加载 → 归属检查报「漏」。按归属完备性裁定它归【甲】。
FORCE_ARMOR = {
    "L05_kenshin": [9],
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
    # 🔴 兵种：普查把「身体件」判成了「甲件」（武将那边判的是「内衬着物」），
    #    于是 `--kimono-torso-only` 从不生效 → 裸手臂/裸腿皮肤被一起收进甲。
    #    实测 L250 idx2（778 顶点）arm=18% leg=40%，就是躯干皮肤+脚絆。
    #    兵种的着物件由 troop_parts_table 直接点名（普查判据对兵种不适用）。
    # 🔴 兵种：**身体件必须排除**（2026-09-15 实测）。
    #    普查把兵种的身体件判成「甲件」→ 收进甲；它带着**裸手臂 / 裸腿 / 脚**的皮肤，
    #    进游戏会叠在骑砍自己的身体上（两套肢体）。实测 L250：`--kimono-torso-only`
    #    只删掉 6 个顶点（KEEP_SW 把大腿/肩也算作躯干），**过滤不掉**；
    #    直接从选件里去掉身体件才干净（渲染对比 Debug/offline/_pilot_render{,3}/）。
    #    代价：内衬着物（躯干那层布）也没了 —— 但胴丸本来就盖住躯干，看不出来。
    for _s in (row_of(key, TABLE).get("body") or []):
        while _s in armor:
            armor.remove(_s)
    for _s in (row_of(key, TABLE).get("kimono") or []):
        if _s not in kimono:
            kimono.append(_s)
    for _s in FORCE_ARMOR.get(key, []):
        if _s not in armor:
            armor.append(_s)
    if PARTITION_HEAD_FRAGS:
        # 每一件都切（不只 helmet/hair 列点到的件）——理由见文件头长注释
        drophead = sorted(set(armor))
    if not armor:
        return None, "普查里一件甲都没认出来（人工看接触图）"
    return dict(parts=armor, kimono=kimono, drophead=drophead), ""


def slug_of(key):
    """资产 slug：武将表从 `asset` 名推（head_yukimura_a → yukimura）；兵种表自带。"""
    r = row_of(key, TABLE)
    return r["slug"] if "slug" in r else r["asset"][len("head_"):-len("_a")]


def run(cmd, tag):
    p = subprocess.run(cmd, capture_output=True)
    out = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    return p.returncode, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--force", action="store_true", help="已存在的也重做")
    ap.add_argument("--set", default="lords", choices=["lords", "troops"])
    args = ap.parse_args()

    skel = find_skel()
    if not skel:
        print("[FATAL] 找不到 human_skeleton.fbx —— 先看 build_armor_chain.py 用的是哪份")
        return 2
    keys = args.only or sorted(TROOP_TABLE if args.set == "troops" else TABLE)
    done, fail, skip = [], [], []
    for key in keys:
        slug = slug_of(key)
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

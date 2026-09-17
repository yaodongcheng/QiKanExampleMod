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

定标（2026-09-16 第 2 步起）：**逐角色源变换 T**
----------------------------------------------
  · 每个角色从 `out/srcT.json` 取自己的 `s` / `z_sole`，传给 build_armor.py 的 `--t-s` / `--t-z-sole`
    → 甲与头/兜共用同一把尺，拼得回原角色（实测宁宁脖↔领口 12/12 档过闸）。
  · 🔴 **表里没有这个角色 = 直接报错退出**，禁止退回旧的 `--r` 系数
    （退回会拼出一件错的甲，而且从日志上完全看不出来）。
  · 旧的两条缩放参数（`--r 0.0120` / `--r-arms 0.0160`）在 T 模式下不再传递 —— 缩放由 T 的 s 承担。

去重复（2026-09-17）：甲 vs 头
------------------------------
  · 脖子皮肤绑**胸骨 bone_9**、不在甲剔的**头骨族**（bone_10/11 + 面部骨）里 → 那圈几何两边都有、
    共用同一个 T → 完全重合（实机 z-fighting）。甲侧由 `--drop-coincident <头 v1 FBX>` 删掉
    ≤1mm 的那些顶点，本文件只负责**判断该不该传**（`head_fbx`：有这个角色的头产物就传）。
  · 🔴 判据第二版起**一般化**：靶子 = 头 FBX 的**全部件**（旧版只认 `_neck` 件 → 脸壳自带颈部的
    头一个顶点都看不见，光秀 91 / 小太郎 20 全是这类漏网），靠**领口带**（1.25~1.70）圈住范围，
    并有安全闸（命中占比 > 15% → 报错退出、一个都不删）。细节见 build_armor.py 那段长注释。

命名：`taikou_<角色slug>_do_a`（与幸村的 `taikou_yukimura_do_a` 同制式）。

用法：
    python tools/sw2-pipeline/build_armors.py --only L02_nobunaga     # 单人
    python tools/sw2-pipeline/build_armors.py                         # 全部
    python tools/sw2-pipeline/build_armors.py --only L47_nene --dry-run   # 只看命令
    python tools/sw2-pipeline/build_armors.py --only L47_nene --out Debug/offline/t_armor_test --log
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
# 🔴 T 的定义/表读写只有一处实现 = tools/sw2-pipeline/src_transform.py（本文件在同一个目录）。
#    `load()` 默认读 out/srcT.json；`lookup()` 取不到会抛 KeyError（不许静默兜底）。
from src_transform import load as t_load, lookup as t_lookup  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
SRC_ROOT = r"D:\BrainMaker\战国无双2资产解包分析"
SRC_DIR = os.path.join(SRC_ROOT, "export", "fbx")
# 源工程 2026-09-16 贴图升级产物（<键>_d.png = 超分漫反射，2 倍于原图集）
TEX_BATCH = os.path.join(SRC_ROOT, "work", "tex_batch")
USE_TEX_UPGRADE = True          # 由 --no-tex-upgrade 关闭（main 里赋值）
CENSUS_DIR = os.path.join(REPO, "Debug", "offline", "sw2_census")
OUT_DIR = os.path.join(REPO, "tools", "armor-pipeline", "out")
BUILD = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_armor.py")
TEX = os.path.join(REPO, "tools", "armor-pipeline", "scripts", "build_textures.py")

# 🔴 退役（2026-09-16 第 2 步）：R_TORSO / R_ARMS 是**老模式**（逐骨重定向 + 全局径向系数 R）的缩放。
#    sw2 批量现在一律走 T 模式，这两个常量**不再传给 build_armor.py**（缩放由 T 的 s 承担）；
#    保留 = 记录当初的实测依据 + 真田工程走的是另一条链（build_armor_chain.py，不经过本文件）。
R_TORSO = "0.0120"
R_ARMS = "0.0160"
# 每人可覆盖（键=角色 key）——只在实测需要时加。
# ⚠️ `r` / `r_arms` 两项在 T 模式下**已失效**（保留 = 记录老模式的实测依据）；
#    `cloth_drop` / `cloth_hang` **仍然有效** —— 它们治的是"源是 T-pose、骑砍是 A-pose"这个
#    **姿态差**，T 不管这件事（T 只重排定标）。
OVERRIDE = {
    # 🔴 2026-09-16 浓姬：她手臂上没有籠手，`bone_16/17` 驱动的**只有两片大振袖**。
    #    `R_ARMS=0.0160` 是为「籠手要盖住骑砍更粗的胳膊」调的；套到袖子上 =
    #    把 53cm 的袖幅放到 53×0.0160 = **0.85 米**（甲总进深炸到 1.14 米，幸村甲只有 0.48）。
    #    实机表现就是两片巨大硬翅膀。袖子是**离身布**不是贴身甲，径向该用躯干那一档 R。
    #    另配 `cloth_drop`：源件是 T-pose，这两片布是斜挂在水平手臂上的，
    #    重定向到骑砍 A-pose 会变成"向后戳出去的板" → 绕肘→腕轴放平（见 build_armor 那段长注释）。
    "L12_nouhime": dict(r_arms=R_TORSO, cloth_drop="0,1"),
    # 🔴 2026-09-16 第二批（布料「垂挂」）：源件是 T-pose，这两人腰/背上的**长衣尾**被描成
    #    「迎风向后甩出去」的姿势（长政 idx1 从腰部向后伸 66cm），骨轴是竖直的脊椎
    #    → `--cloth-drop`（绕骨轴）对它无效，必须走 `--cloth-hang`（绕水平轴扫到正下方）。
    #    判据来源：驱动件↔渲染件按包围盒重合配对（8/8 命中），配对结果见
    #    Debug/offline/cloth_probe/audit_cloth.txt。
    "L42_nagamasa": dict(cloth_hang="0"),
    "L40_ieyasu":   dict(cloth_hang="0"),
}

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


# ---------------------------------------------------------------- 逐角色源变换 T（第 2 步）
# 🔴 表 = `out/srcT.json`，读写只有一处实现（src_transform.py）。甲/兜/头三件共用同一份 T
#    才拼得回原角色；表里没有这个角色 = **报错退出**，禁止退回旧的 R 系数。
T_TABLE = {}          # main() 里 load()


def load_t():
    """载入逐角色表（唯一来源 out/srcT.json，见 src_transform.py 文件头）。"""
    global T_TABLE
    T_TABLE = t_load()


def t_args(key):
    """→ ["--t-s", s, "--t-z-sole", z]。取不到就抛 KeyError（调用方负责报错），不许兜底。"""
    row = t_lookup(T_TABLE, key)
    s = row.get("s")
    if not s:
        raise KeyError("srcT 表里 %s 没有 s —— 该角色没取到头骨，先重刷 src_transform.py --write" % key)
    return ["--t-s", "%.9f" % s, "--t-z-sole", "%.6f" % (row.get("z_sole") or 0.0)]


def find_diffuse(key):
    """甲贴图来源。

    🔴 2026-09-16 起默认走**升级路线**：源工程超分 2 倍的图（`work/tex_batch/<键>_d.png`）。
    它比原图集锐利（实测清晰度 2456 vs 1878，而原图集纯放大只有 172.5），
    配合 `--tex-scale 2` 出 1024×1560 的甲贴图（原来 512×780）。
    回退：`--no-tex-upgrade` 走原图集（512×1024，输出 512×780）。

    甲**不共用武器贴图**：甲穿在身上、与角色共用图集（这条是对的）；
    武器相反，有独立贴图（见 build_weapons.py 的说明）。
    """
    if USE_TEX_UPGRADE:
        p = os.path.join(TEX_BATCH, key + "_d.png")
        if os.path.isfile(p):
            return p
        print("     ⚠️ 升级图缺 %s → 回退原图集" % p)
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


# ---------------------------------------------------------------- 🔴 去重复：甲 vs 头（2026-09-17）
# 问题：甲剔头骨族用的是**骨骼**判据（bone_10/11 + 面部骨），而脖子皮肤绑的是**胸骨 bone_9**
#   —— 脖子的那圈几何被甲留着；头的同一份顶点两边共用同一个 T → 完全重合（z-fighting）。
#   实测（修前）：28 人里 6 人有重合，@≤1.5mm 共 207 顶点
#   （光秀 91 / 归蝶 76 / 小太郎 20 / 胜家 13 / 小次郎 4 / 义弘 3）。
# 修法：`build_armor.py --drop-coincident <头 v1 FBX>` 把 ≤1mm 的那些甲顶点删掉
#   （**没传这个参数 = 甲一行没变**）。此处只负责**判断该不该传**。
#
# 🔴 判据一般化（第二版）：靶子 = **头 FBX 的全部件**（不再只认 `_neck` 件）。
#   旧版只认材质名 `_neck` 的那件 —— 而**没有独立脖子件的头**（脸壳自带颈部）旧版一个顶点都
#   看不见，光秀 91 / 小太郎 20 正是这类漏网（脸壳下沿 vs 甲领口）。靶子换成"全部件"之后靠
#   **领口带**（build_armor 的默认 `--drop-coincident-band 1.25,1.70`）把远处区域圈在外面。
#   判据细节与安全闸（命中占比 > 15% = 报错退出、不删）见 `build_armor.py` 那段长注释。


def head_fbx(key):
    """→ 头 FBX 路径（v1 几何版）或 None。读法与 check_materials.py 同一套。

    🔴 **只要这个角色有头产物就传**（第二版起）：旧版还要求"头里真有 `_neck` 件"，
      那是第一版判据（靶子只认 `_neck`）的配套 —— 一般化之后靶子是**全部件**，
      有没有独立脖子件都能量（脸壳下沿那类重合正是靠这一版才看得见）。
    🔴 用 **v1**（几何版）不是 v2：两版几何完全一样，v2 多带 59 条形变通道，读起来慢。
    兵种（表里带 slug）没有头产物 → 直接 None。
    """
    r = row_of(key, TABLE)
    if "slug" in r:                      # 兵种：build_heads.py 不给它们出头
        return None
    p = os.path.join(REPO, "Debug", "offline", "sw2_build", key, r["asset"] + "_v1.fbx")
    return p if os.path.isfile(p) else None


def run(cmd, tag):
    p = subprocess.run(cmd, capture_output=True)
    out = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    return p.returncode, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--force", action="store_true", help="已存在的也重做")
    ap.add_argument("--set", default="lords", choices=["lords", "troops"])
    ap.add_argument("--out", default=OUT_DIR, help="产物目录（默认 tools/armor-pipeline/out）")
    ap.add_argument("--dry-run", action="store_true", help="只打印要跑的命令，不执行")
    ap.add_argument("--log", action="store_true",
                    help="把网格步的完整输出打出来（看 bbox / 顶点数 / 未绑定顶点）")
    ap.add_argument("--no-tex-upgrade", action="store_true",
                    help="甲贴图用原图集（512×780）。默认走超分图，出 1024×1560")
    args = ap.parse_args()
    global USE_TEX_UPGRADE
    USE_TEX_UPGRADE = not args.no_tex_upgrade
    outdir = os.path.abspath(args.out)
    os.makedirs(outdir, exist_ok=True)
    # 升级图的像素尺寸是原图集的 2 倍 → 裁切与输出同倍放大（UV 重映射与尺寸无关，不会错位）
    tex_scale = "2" if USE_TEX_UPGRADE else "1"

    skel = find_skel()
    if not skel:
        print("[FATAL] 找不到 human_skeleton.fbx —— 先看 build_armor_chain.py 用的是哪份")
        return 2
    try:
        load_t()
    except Exception as e:
        print("[FATAL] 读不到逐角色源变换表（out/srcT.json）：%s" % e)
        print("        刷新：blender -b --python tools/sw2-pipeline/src_transform.py -- --write tools/sw2-pipeline/out/srcT.json")
        return 2
    keys = args.only or sorted(TROOP_TABLE if args.set == "troops" else TABLE)
    done, fail, skip = [], [], []
    for key in keys:
        slug = slug_of(key)
        name = "taikou_%s_do_a" % slug
        out_fbx = os.path.join(outdir, name + ".fbx")
        if os.path.isfile(out_fbx) and not args.force:
            print("  [跳过] %-16s 已有 %s" % (key, os.path.basename(out_fbx))); skip.append(key); continue
        plan, why = plan_for(key)
        if not plan:
            print("  ❌ %-16s %s" % (key, why)); fail.append(key); continue
        # 🔴 拿不到 T 就停 —— 索引不到的键必须显式失败（禁止退回旧 R 系数）
        try:
            tflag = t_args(key)
        except KeyError as e:
            print("  ❌ %-16s %s" % (key, e))
            fail.append(key); continue
        src = os.path.join(SRC_DIR, key + ".fbx")
        own = OVERRIDE.get(key, {})
        cmd = [BLENDER, "-b", "--python", BUILD, "--",
               "--src", src, "--skel", skel, "--out", outdir, "--name", name,
               "--parts-idx", ",".join(str(x) for x in plan["parts"]),
               "--kimono-idx", ",".join(str(x) for x in plan["kimono"]) or "0",
               # 定标 = 本角色的 T（第 2 步起）；旧的 --r / --r-arms 不再传（已退役）
               ] + tflag + ["--no-hands", "--kimono-torso-only"]
        if plan["drophead"]:
            cmd += ["--drop-head-idx", ",".join(str(x) for x in plan["drophead"])]
        if own.get("cloth_drop"):
            cmd += ["--cloth-drop", own["cloth_drop"]]
        if own.get("cloth_hang"):
            cmd += ["--cloth-hang", own["cloth_hang"]]
        # 🔴 甲 vs 头 去重复（2026-09-17）：只要**这个角色有头产物**就传（第二版起不要求 `_neck` 件，
        #    理由见 head_fbx）。靶子带 = build_armor 的默认 1.25~1.70（领口带）。
        _neckf = head_fbx(key)
        if _neckf:
            cmd += ["--drop-coincident", _neckf]
        # 🔴 区域剔除（2026-09-17 晚）：与头侧 `--skin-region` **同一份参数**（parts_table 那一行）
        #    —— 把「脖子/胸口皮肤区」从甲里剔掉，免得甲占着皮肤区（实机=领口里一块灰）。
        _skr = TABLE[key].get("skin_region")
        if _skr:
            # 🔴 优先「与头同一批源顶点」（头产物旁边有 <asset>_necksrc.json 就用它）：
            #    头拿多少、甲正好少多少；没有那份清单才退回"同区域各判一次"（旧做法，会剔偏）。
            _hjson = None
            if _neckf:
                _cand = os.path.join(os.path.dirname(_neckf), TABLE[key]["asset"] + "_necksrc.json")
                if os.path.isfile(_cand):
                    _hjson = _cand
            if _hjson:
                cmd += ["--drop-from-head", _hjson]
            else:
                cmd += ["--skin-drop-region", "%.3f,%.3f,%.3f" % (_skr["r"], _skr["z0"], _skr["z1"])]
            if _skr.get("bones"):
                cmd += ["--skin-drop-region-bones", ",".join(str(b) for b in _skr["bones"])]
        print("  ▶ %-16s 甲件 %s%s%s  T.s=%s%s" % (key, plan["parts"],
              " 着物%s" % plan["kimono"] if plan["kimono"] else "",
              " 剔头%s" % plan["drophead"] if plan["drophead"] else "",
              tflag[1], "  去重复←%s" % os.path.basename(_neckf) if _neckf else ""))
        if args.dry_run:
            print("     [dry-run] %s" % " ".join('"%s"' % c if " " in str(c) else str(c) for c in cmd))
            print("     [dry-run] 到此为止（未执行；贴图步复用同一份 cmd 的参数）")
            done.append(key); continue
        rc, out = run(cmd, "build_armor")
        if args.log:
            for l in out.splitlines():
                if l.strip():
                    print("        " + l[:200])
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
                             "--out", outdir, "--name", name,
                             "--tex-scale", tex_scale,
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

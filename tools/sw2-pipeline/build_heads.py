# -*- coding: utf-8 -*-
"""build_heads.py —— 战无2 28 人：源模型 → 可进编辑器的头部 FBX + 贴图（一条命令）。

🔴 **2026-09-16 改基准（三件共用源变换 T）**——本文件负责传 T 与脖子：
  · `--t-s / --t-z-sole` 取自 `tools/sw2-pipeline/out/srcT.json`（**唯一来源**，见 src_transform.py）。
    定向从「偏航 + 眼↔嘴标定 + 眼球送眼位」换成 **Y 轴镜像 + 等比缩放**（源原点=地面）。
    为什么：头按眼距、甲按骨锚，两套基准拼不回原角色（实测宁宁脖子比甲领口高 8.7cm）。
  · **脖子归头**（用户裁定）：`neck_src_idx(key)` 把普查判定为「甲件/内衬着物」的件拼进
    `--pick-idx` 的 `neck=...`，由 build_head.carve_neck_part 抠出**源模型自己的脖子**，
    作为头的第 4 个 part（材质 `<头名>_neck`，件位**必须排最后**）。贴图多 3 张
    `_neck_d/_n/_s`（= 脸那三张的副本，战无2 一件模型一张全身图集，不另裁）。
  · **退役**：`--neck-fill` / `--neck-tube-to`（人造下摆+脖子管）不再传，见下面的退役说明。

【模板 = 织田信长那版】+ **2026-09-16 补的两步（脖子）**：
  · `--neck-fill` 🔴 **已退役停用**（2026-09-16 用户裁定「脖子归头」）：原来它把脖子那个断口
    沿着 RIM_TABLE 的领口轮廓往下铺一层。现在脖子是从源模型身体件里抠的真几何。
  · 🔴 `--weights-from <原版同类头>`：**抄原版头的颈部权重**（这一步保留，是脖子两步走的第二步）。
    原来 28 张脸是**整体刚性绑 bone_13**，脖子不跟脊柱/锁骨动 → 待机呼吸时脖子从肩里冒出来。
    这正是萨菲罗斯那轮修过的同一个毛病（`Knowledge/蒂法换头工程.md` §21.7 问题 3 / §21.8）。
    男头抄男基准、女头抄女基准。
  · **不加** `--weld-seam`（合缝）：战无2 的脸壳是整块，没有萨菲罗斯那种前后两壳裂缝。
  · `--fit-rim` 仍不加：战无2 只沾 4 个顶点/1.7cm（萨菲罗斯是 118 个顶点/6.2cm），
    而脸件里混着兜帽/披风的角色会被它当成领口猛收（实测最大收进 1057mm）。
  信长那版的手工日志在 `work/out/build_head_L02.txt`。

🔴 **2026-09-17 加：两个「剔非头部」通道**（数据都在 parts_table 对应行，带实测数字）：
  · `junk`（源坐标种子点）→ `--strap-seed`：整块摘掉**完全落在脖子以下**的碎块
    （实测风魔小太郎 8 块衣带 / 明智光秀 6 个发尾尖）。
  · `cut_z`（目标空间米）→ `--cut-z face=<z>`：给**跨在脖子上下的**非头部几何切一刀
    （实测佐佐木小次郎的马尾：顶在 1.80 连着后脑，不能整块删）。

🔴 **渲染产物做视觉判断前必须先把形状键归零**：产物 FBX 里 `KeyTime_0..59` 的 value 都写着 1.0，
   Blender 直接渲 = 59 条形变全叠加的变形头（实测信长 x ±0.132 被压到 ±0.083）。
   `Debug/offline/_render_fit.py` 已内置归零。

每个人跑四步：
  ① build_head.py        挑件 → 切嘴 → 抠脖子 → 定向标定（T）→ 绑官方骨架 → 导出
  ② transfer_channels.py 从蒂法 v10 搬 59 条形变通道
  ③ make_sw2_textures.py 源图集 → **8 张**贴图（脸/眼/嘴/脖子 四张 diffuse 同一张图集 + `_n`/`_s`
                         + 脖子那三张的副本 = `_neck_d/_neck_n/_neck_s`）
  ④ fbx_probe.py         关卡 1（USF=100 / UpAxis=2 / 网格节点零变换）

用法（系统 python）:
  python tools/sw2-pipeline/build_heads.py                      # 全部 28 人
  python tools/sw2-pipeline/build_heads.py --only L00_yukimura L06_oichi
  python tools/sw2-pipeline/build_heads.py --dry-run            # 只打印要跑什么
  python tools/sw2-pipeline/build_heads.py --stage <AssetSources 目录>   # 顺手拷进编辑器工程

产出（Debug/offline/sw2_build/<角色>/）:
  <asset>_v1.fbx      几何版（无通道）
  <asset>_v2.fbx      ★ 成品（含 59 条形变通道）
  <asset>_*.png       8 张贴图
  <角色>.log          每步的完整输出（错了先看它）
"""
import argparse
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parts_table import (TABLE, build_head_args, strap_seeds, strap_bones,  # noqa: E402
                         neck_src_idx, neck_args, head_junk_seeds, head_cut_z, strap_cover)
from src_transform import load as load_srcT, lookup as srcT_lookup                       # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
FACE = os.path.join(REPO, "tools", "face-pipeline", "scripts")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

SRC_FBX = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
SRC_TEX = r"D:\BrainMaker\战国无双2资产解包分析\web\textures"
# 源工程 2026-09-16 贴图升级产物：<源角色键>_d.png（超分漫反射）/ _n.png（真法线）/ _mr.png（不用）
TEX_BATCH = r"D:\BrainMaker\战国无双2资产解包分析\work\tex_batch"
# 59 条脸形位移场的权威来源（build_head_chain.py 同款；通道与脸型无关，新头模一律从这里搬）
CHAN_SRC = r"D:\BrainMaker\blend_projects\tifa_export\backup_20260913\head_tifa_a_v10.fbx"
CHAN_OBJ = "head_tifa_a.0"
# 原版头的 FBX（抄颈部权重用）：男头抄 head_male_a、女头抄 head_female_a。
# 与 build_head_chain.py 给萨菲罗斯用的是同一份（extracted_sho 的 tpac dump）。
VANILLA_HEAD_DIR = r"D:\BrainMaker\extracted_sho\fbx\head"
OUT_ROOT = os.path.join(REPO, "Debug", "offline", "sw2_build")


def run(cmd, logf, keep=()):
    logf.write("\n$ %s\n" % " ".join('"%s"' % c if " " in str(c) else str(c) for c in cmd))
    p = subprocess.run(cmd, capture_output=True)
    # 🔴 按【字节】收再自己解码：Blender 在 Windows 控制台的输出是 GBK，
    #    直接 text=True + encoding="utf-8" 会把中文全变成乱码，日志就没法用了（踩过）。
    raw = (p.stdout or b"") + (p.stderr or b"")
    out = None
    for enc in ("utf-8", "gbk", "cp936"):
        try:
            out = raw.decode(enc)
            break
        except UnicodeDecodeError:
            continue
    if out is None:
        out = raw.decode("utf-8", "replace")
    logf.write(out)
    logf.flush()
    lines = [l.strip() for l in out.splitlines() if l.strip()]
    return p.returncode, lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None, help="只做这几个角色（parts_table 的键）")
    ap.add_argument("--out", default=OUT_ROOT)
    ap.add_argument("--stage", default=None, help="把这些产物拷到该目录（编辑器工程的 AssetSources）")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-tex-upgrade", action="store_true",
                    help="用原版图集 + 平法线（旧路线）。默认走源工程超分图 + 真法线")
    a = ap.parse_args()

    keys = a.only or list(TABLE.keys())
    # 🔴 源变换 T 的唯一来源：tools/sw2-pipeline/out/srcT.json（三件共用；表里缺角色会抛错）
    SRC_T = load_srcT()
    bad = []
    for n, key in enumerate(keys, 1):
        if key not in TABLE:
            print("[%2d/%d] %s 不在挑件表里，跳过" % (n, len(keys), key))
            continue
        r = TABLE[key]
        src = os.path.join(SRC_FBX, key + ".fbx")
        tex = os.path.join(SRC_TEX, key + ".png")
        d = os.path.join(a.out, key)
        v1 = os.path.join(d, r["asset"] + "_v1.fbx")
        v2 = os.path.join(d, r["asset"] + "_v2.fbx")
        parts, pick, seal = build_head_args(key)   # 🔴 挑件串只有这一个来源，别再抄一份
        # 🔴 2026-09-16「脖子归头」（用户裁定）：脖子从身体件里抠出来，作为头的**独立 part**
        #    （材质 `<头名>_neck`，UV 仍在源图集上 → 贴图是 `_neck_d/_n/_s` 三张副本）。
        #    件位**必须放最后**：引擎给脸部件分配贴图是按**子网格位置**算的，
        #    脸/嘴/眼 的位置不能动（见 parts_table.build_head_args 的性别顺序说明）。
        _neck_idx = neck_src_idx(key)
        if _neck_idx:
            pick += ",neck=%s" % "+".join(str(i) for i in _neck_idx)
            parts += ",neck"
            _trow = srcT_lookup(SRC_T, key)        # 表里没有会直接抛错——不许静默用错尺子
        else:
            # 两种「没有脖子件」：① 裁定**故意不做**（脸壳自己盖住领口，见 parts_table.neck_carve）
            #    ② 普查表里真没有可抠的件。两者的处置相同（头不带独立脖子 part），但要分清。
            print("%s  %s（头不带独立脖子 part）"
                  % (key, "脖子件已按裁定关闭（脸壳自带颈部）" if r.get("neck_carve") is False
                     else "⚠️ 普查表里没有可抠的件 → 抠不出脖子"))
            _trow = srcT_lookup(SRC_T, key)

        c1 = [BLENDER, "-b", "--python", os.path.join(FACE, "build_head.py"), "--",
              "--src", src, "--out", v1, "--name", r["asset"],
              "--gender", r["gender"], "--parts", parts,
              "--pick-idx", pick, "--cut-mouth"]
        # 🔴 源变换 T（三件共用一把尺）——s 与 z_sole 来自 tools/sw2-pipeline/out/srcT.json
        #    （唯一来源，见 src_transform.py 文件头；三件都用同一份，别在这里另算）
        c1 += ["--t-s", "%.8f" % _trow["s"], "--t-z-sole", "%.6f" % _trow["z_sole"]]
        # 抠脖子的**肤色判据**要读源图集（参照色取脸壳下巴一带）：升级路线读超分图，否则读原图集
        c1 += ["--neck-atlas", (os.path.join(TEX_BATCH, key + "_d.png")
                                if not a.no_tex_upgrade else tex)]
        # 🔴 抠脖子的判据参数 = parts_table.neck_args(key)（默认 + 逐人覆写）——**与闸门同一份**。
        #    闸门的「头侧下沿」就是这截脖子抠出来的东西，两边必须同参，否则一个过一个不过。
        _na = neck_args(key)
        c1 += ["--neck-r-max", "%.4f" % _na["r_max"], "--neck-y-max", "%.4f" % _na["y_max"],
               "--neck-z-top", "%.4f" % _na["z_top"], "--neck-sect", str(_na["n_sect"]),
               "--neck-z-cut", "%.4f" % _na["z_cut"], "--neck-skin-tol", "%.4f" % _na["skin_tol"]]
        # 🔴 ⑦ 主导骨排除（默认开；见 build_head.carve_neck_part）：别把「戴在头上的东西」当脖子。
        #    显示传 = 逐人覆写的通道也打开（`neck_args=dict(excl_head=False)`）。
        c1 += ["--neck-excl-head", "1" if _na["excl_head"] else "0"]
        # 🔴 `neck_keep_uv=True`（逐人，parts_table）：脖子件**保留原 UV**、不塌到肤色点。
        #    给「皮肤层」来源的人用（战无2 稻姬 sub6 = 头发+脖子胸口皮，UV 本来就在皮肤上）；
        #    从身体/甲件抠的那类（UV 跨到衣服区）**不要**加，会发暗。见 build_head.py 该分支的注释。
        if TABLE[key].get("neck_keep_uv"):
            c1 += ["--neck-keep-uv"]
        # 🔴 `skin_region=dict(r=…, z0=…, z1=…, skin=True/False)`（逐人，parts_table）：
        #    **区域裁剪**（逐顶点）取代整块连通域抠取 —— 给「皮肤/衣领长在大片段里」的角色用。
        #    甲侧由 build_armors 用**同一份参数** `--skin-drop-region` 剔掉同一区域（两头一致）。
        _skr = TABLE[key].get("skin_region")
        if _skr:
            _nd = os.path.join(d, r["asset"] + "_necksrc.json")
            c1 += ["--dump-neck-src", _nd]
            c1 += ["--skin-region", "%.3f,%.3f,%.3f" % (_skr["r"], _skr["z0"], _skr["z1"]),
                   "--skin-region-skin", "1" if _skr.get("skin", True) else "0"]
            c1 += ["--neck-keep-uv"]     # 区域裁剪本来就该保留原 UV（那片皮在皮肤贴图上）
            if _skr.get("ratio"):        # 色比口径（皮肤 R/G≈1.36 vs 灰布 ≈1.06）
                c1 += ["--skin-ratio", ",".join("%.3f" % c for c in _skr["ratio"])]
            if _skr.get("bones"):        # 主导骨白名单（不给 = 不管）
                c1 += ["--skin-region-bones", ",".join(str(b) for b in _skr["bones"])]
            if _skr.get("ref_color"):    # 逐人显式参照色（脸壳颧骨带被头发污染时用）
                c1 += ["--skin-ref-color", ",".join("%.3f" % c for c in _skr["ref_color"])]
        if seal:
            c1 += ["--seal-bottom", ",".join(str(i) for i in seal),
                   "--seal-atlas", tex]     # 补面的 UV 从这张图集里采最暗的发色点
        _st = strap_seeds(key)
        _jk = head_junk_seeds(key)     # 🔴 非头部碎块（衣服/带子/发尾混进头件）—— 与颏带同一条通道
        #    （build_head.py 只有 `--strap-seed` 这一个"按种子点整块摘碎片"的入口；同一条通道
        #     拼接即可，但**必须是两列数据**：颏带做兜时要加回去，非头部碎块只删不加）。
        if _jk:
            print("        剔非头部碎块 %d 块（种子点见 parts_table 的 junk 列）" % len(_jk))
        if _st or _jk:
            c1 += ["--strap-seed", ";".join(",".join("%.2f" % v for v in sd)
                                            for sd in list(_st) + list(_jk))]
        _sb = strap_bones(key)
        if _sb:
            c1 += ["--strap-bone", ",".join(_sb)]
        # 🔴 目标空间按 z 切一刀（数据在 parts_table 的 `cut_z`）—— 给"跨在脖子上下的非头部几何"用：
        #    碎片整块删会连头上的部分一起删（实测小次郎的马尾：顶在 1.80 连着后脑）。
        #    走 build_head.py 既有的 `--cut-z`，本轮不新增判据。
        _cz = head_cut_z(key)
        if _cz:
            print("        头件按 z 切一刀：face z < %.4f 删掉（parts_table 的 cut_z）" % _cz)
            c1 += ["--cut-z", "face=%.4f" % _cz]
        # 🔴 单人放宽「剔非头部」阈值（2026-09-15 深夜加，数据在 parts_table 的 `head_k`）
        #    用途：有些角色**脖子高度的头发/头绳**绑的是颈骨（bone_12/13），
        #    距面部骨簇 21，超过默认阈值 1.6×8.2=13 → 被当"太远"删掉，头和发尾就断开了。
        #    实测杂贺：脖子那 58 个顶点（z≈159~162）被删 → 后垂马尾和头之间一个缺口；
        #    而他的长外套（距 40~71）默认阈值下照样被删，放宽到 2.7 不会把外套放回来。
        _hk = r.get("head_k")
        if _hk:
            c1 += ["--head-k", str(_hk)]
        # 🔴 从身体件里抠脖子：老路线（`--neck-r/--neck-z0`，并进脸壳）已被 2026-09-16 的
        #    「脖子归头」裁定取代（拼进上面的 `neck=<序号>`，抠成独立 part），这两个参数**不再传**。
        #
        # 🔴 战无2 **不加** --fit-rim：信长那版实测只动 2~4 个顶点（等于没用），
        #    而脸件里混着兜帽/披风的角色（半藏、义弘、杂贺…）会被它当成领口猛收，
        #    实测最大收进 1057mm —— 有害无益。（RIM_TABLE 也只有 male 的轮廓。）
        #
        # 🔴 抄原版颈部权重（脖子两步走的第二步：**几何 + 权重**，只做几何 = 呼吸时脖子从肩里冒出来）。
        #    权重来源必须**同性别**：男头抄 head_male_a，女头抄 head_female_a；脖子件也吃这一套
        #    （build_head.py 5c 的 role in ("face","neck")）。
        #
        # 🔴🔴 退役（2026-09-16 用户裁定）：`--neck-fill` / `--neck-tube-to 1.25`（人造下摆 + 脖子管）
        #    **不再传**。它是为补偿「头按眼距定标 / 甲按骨锚定标」两套基准的错位而生的补丁；
        #    基准统一成 T、脖子又从源模型身体件抠出来之后，这圈人造几何只会多出一截藏在甲里的皮肤、
        #    把「治本了没有」这个判断盖住。按退役两步走：先停用（build_head.py 里函数保留）→
        #    实机验证 → 通过才删函数。
        c1 += ["--weights-from", os.path.join(VANILLA_HEAD_DIR,
                                              "head_%s_a.fbx" % r["gender"]),
               "--neck-z", "1.600", "--neck-band", "0.05"]
        c2 = [BLENDER, "-b", "--python", os.path.join(FACE, "transfer_channels.py"), "--",
              "--src", CHAN_SRC, "--src-object", CHAN_OBJ, "--dst", v1, "--out", v2]
        c3 = [sys.executable, os.path.join(HERE, "scripts", "make_sw2_textures.py"),
              "--atlas", tex, "--out", d, "--name", r["asset"]]
        # 🔴 颏带**改色**（不删几何，只换贴图）：给"颏带兼着堵下颌缝、摘了就露"的角色用
        #    （秀吉）。数据在 parts_table 的 `strap_cover`（两个 UV 矩形），理由见该行注释。
        _scv = strap_cover(key)
        if _scv:
            print("        颏带改色：UV 区 %s ← 皮肤块 %s" % (_scv["box"], _scv["src"]))
            c3 += ["--cover", _scv["box"], "--cover-src", _scv["src"]]
        if not a.no_tex_upgrade:
            # 升级路线：漫反射读超分图、法线读真法线（见 make_sw2_textures.py 文档头 B 路线）
            c3 += ["--src-dir", TEX_BATCH, "--key", key]
        c4 = [sys.executable, os.path.join(FACE, "fbx_probe.py"), v2]

        tag = "[%2d/%d] %-16s %s" % (n, len(keys), key, r["cn"])
        if a.dry_run:
            print(tag)
            for c in (c1, c2, c3, c4):
                print("      $ %s" % " ".join(c))
            continue

        for f, what in ((src, "源模型"), (tex, "源图集"), (CHAN_SRC, "通道源")):
            if not os.path.isfile(f):
                print("%s  ❌ 缺%s：%s" % (tag, what, f))
                bad.append((key, "缺" + what))
                break
        else:
            os.makedirs(d, exist_ok=True)
            # 🔴 跑之前先删掉上一轮的产物：脚本是靠「文件在不在」判断成功的，
            #    残留的旧文件会让失败的这一轮被误报成成功（已踩过一次）。
            for _f in (v1, v2):
                if os.path.exists(_f):
                    os.remove(_f)
            with open(os.path.join(d, key + ".log"), "w", encoding="utf-8") as logf:
                rc1, l1 = run(c1, logf)
                if rc1 != 0 or not os.path.isfile(v1):
                    print("%s  ❌ build_head 失败（看 %s.log）" % (tag, key))
                    print("        " + (l1[-1] if l1 else ""))
                    bad.append((key, "build_head"))
                    continue
                rc2, l2 = run(c2, logf)
                if rc2 != 0 or not os.path.isfile(v2):
                    print("%s  ❌ transfer_channels 失败（看 %s.log）" % (tag, key))
                    bad.append((key, "channels"))
                    continue
                rc3, l3 = run(c3, logf)
                if rc3 != 0:
                    print("%s  ❌ 贴图失败（看 %s.log）" % (tag, key))
                    bad.append((key, "textures"))
                    continue
                rc4, l4 = run(c4, logf)

            cut = next((l for l in l1 if "切嘴" in l), "")
            rim = next((l for l in l1 if "收领口" in l), "")
            vkey = next((l for l in l2 if "VertexKeyCount" in l or "通道" in l), "")
            gate = "✅" if rc4 == 0 else "❌关卡1"
            print("%s  %s   %s" % (tag, gate, rim.replace("  收领口：", "领口:")))
            if cut:
                print("        " + cut.strip())
            if rc4 != 0:
                bad.append((key, "关卡1"))

            if a.stage:
                dst = os.path.join(a.stage, "sw2", key)
                os.makedirs(dst, exist_ok=True)
                for f in os.listdir(d):
                    # 🔴 排除 `_pre*`：那是**早期调试留下的中间件**（搬形变通道之前的几何版），
                    #    进了编辑器导入根就会被一起导入 → 同一个头上多出一个不该有的资产。
                    #    （实测 1.2.12 客户端因此积了 `_pre_head_*_v2.fbx` 若干 + 一个手改副本，
                    #    2026-09-17 才发现。旧副本要用户自己删，见本轮报告。）
                    if f.startswith("_pre") or f.endswith("- 副本.fbx"):
                        continue
                    if f.endswith(".fbx") or f.endswith(".png"):
                        shutil.copy2(os.path.join(d, f), os.path.join(dst, f))
        if a.stage and not bad:
            pass

    print("\n" + "=" * 70)
    print("完成：成功 %d / 失败 %d" % (len(keys) - len(bad), len(bad)))
    for k, why in bad:
        print("   ❌ %-16s %s" % (k, why))
    if a.stage:
        print("已拷进：%s\\sw2\\<角色>\\" % a.stage)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

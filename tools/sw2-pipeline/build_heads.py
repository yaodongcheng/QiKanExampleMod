# -*- coding: utf-8 -*-
"""build_heads.py —— 战无2 28 人：源模型 → 可进编辑器的头部 FBX + 贴图（一条命令）。

【模板 = 织田信长那版】+ **2026-09-16 补的两步（脖子）**：
  · 🔴 `--neck-fill`：**补脖子下摆**。战无2 的脸壳件比原版头短约 5cm（信长 1.4653 vs 原版 1.4144），
    底下是个断口 —— 实机表现「颈部没有贴合肩部，往上抬了一点」（用户 2026-09-16 报，28 人全中）。
    本步把脖子那个断口沿着 RIM_TABLE 的领口轮廓往下铺一层，头底落到 1.4146 ≈ 原版 1.4144。
  · 🔴 `--weights-from <原版同类头>`：**抄原版头的颈部权重**。原来 28 张脸是**整体刚性绑 bone_13**，
    脖子不跟脊柱/锁骨动 → 待机呼吸时脖子从肩里冒出来。这正是萨菲罗斯那轮修过的同一个毛病
    （`Knowledge/蒂法换头工程.md` §21.7 问题 3 / §21.8），本脚本早先的判断「那是萨菲罗斯的毛病、
    战无2 的头没有」**是错的**，2026-09-16 更正。男头抄男基准、女头抄女基准。
  · **不加** `--weld-seam`（合缝）：战无2 的脸壳是整块，没有萨菲罗斯那种前后两壳裂缝。
  · `--fit-rim` 仍不加：战无2 只沾 4 个顶点/1.7cm（萨菲罗斯是 118 个顶点/6.2cm），
    而脸件里混着兜帽/披风的角色会被它当成领口猛收（实测最大收进 1057mm）。
  信长那版的手工日志在 `work/out/build_head_L02.txt`。

🔴 **渲染产物做视觉判断前必须先把形状键归零**：产物 FBX 里 `KeyTime_0..59` 的 value 都写着 1.0，
   Blender 直接渲 = 59 条形变全叠加的变形头（实测信长 x ±0.132 被压到 ±0.083）。
   `Debug/offline/_render_fit.py` 已内置归零。

每个人跑四步：
  ① build_head.py        挑件 → 切嘴 → 定向标定 → 收领口 → 绑官方骨架 → 导出
  ② transfer_channels.py 从蒂法 v10 搬 59 条形变通道
  ③ make_sw2_textures.py 源图集 → 5 张贴图（三张 diffuse 同一张图集 + 纯色 _n/_s）
  ④ fbx_probe.py         关卡 1（USF=100 / UpAxis=2 / 网格节点零变换）

用法（系统 python）:
  python tools/sw2-pipeline/build_heads.py                      # 全部 28 人
  python tools/sw2-pipeline/build_heads.py --only L00_yukimura L06_oichi
  python tools/sw2-pipeline/build_heads.py --dry-run            # 只打印要跑什么
  python tools/sw2-pipeline/build_heads.py --stage <AssetSources 目录>   # 顺手拷进编辑器工程

产出（Debug/offline/sw2_build/<角色>/）:
  <asset>_v1.fbx      几何版（无通道）
  <asset>_v2.fbx      ★ 成品（含 59 条形变通道）
  <asset>_*.png       5 张贴图
  <角色>.log          每步的完整输出（错了先看它）
"""
import argparse
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parts_table import TABLE, build_head_args, strap_seeds, strap_bones  # noqa: E402

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

        c1 = [BLENDER, "-b", "--python", os.path.join(FACE, "build_head.py"), "--",
              "--src", src, "--out", v1, "--name", r["asset"],
              "--gender", r["gender"], "--parts", parts,
              "--pick-idx", pick, "--cut-mouth"]
        if seal:
            c1 += ["--seal-bottom", ",".join(str(i) for i in seal),
                   "--seal-atlas", tex]     # 补面的 UV 从这张图集里采最暗的发色点
        _st = strap_seeds(key)
        if _st:
            c1 += ["--strap-seed", ";".join(",".join("%.2f" % v for v in sd) for sd in _st)]
        _sb = strap_bones(key)
        if _sb:
            c1 += ["--strap-bone", ",".join(_sb)]
        # 🔴 单人放宽「剔非头部」阈值（2026-09-15 深夜加，数据在 parts_table 的 `head_k`）
        #    用途：有些角色**脖子高度的头发/头绳**绑的是颈骨（bone_12/13），
        #    距面部骨簇 21，超过默认阈值 1.6×8.2=13 → 被当"太远"删掉，头和发尾就断开了。
        #    实测杂贺：脖子那 58 个顶点（z≈159~162）被删 → 后垂马尾和头之间一个缺口；
        #    而他的长外套（距 40~71）默认阈值下照样被删，放宽到 2.7 不会把外套放回来。
        _hk = r.get("head_k")
        if _hk:
            c1 += ["--head-k", str(_hk)]
        # 🔴 从身体件里抠脖子（见 parts_table 的 neck 注释；雑賀专用，2026-09-15 深夜）
        if r.get("neck"):
            c1 += ["--neck-r", str(r.get("neck_r", 12.0)),
                   "--neck-z0", str(r.get("neck_z0", 125.0))]
        # 🔴 战无2 **不加** --fit-rim：信长那版实测只动 2~4 个顶点（等于没用），
        #    而脸件里混着兜帽/披风的角色（半藏、义弘、杂贺…）会被它当成领口猛收，
        #    实测最大收进 1057mm —— 有害无益。（RIM_TABLE 也只有 male 的轮廓。）
        #
        # 🔴 2026-09-16 两步（脖子，见文件头）：补下摆 + 抄原版颈部权重。
        #    权重来源必须**同性别**：男头抄 head_male_a，女头抄 head_female_a。
        c1 += ["--neck-fill",
               "--weights-from", os.path.join(VANILLA_HEAD_DIR,
                                              "head_%s_a.fbx" % r["gender"]),
               "--neck-z", "1.600", "--neck-band", "0.05"]
        c2 = [BLENDER, "-b", "--python", os.path.join(FACE, "transfer_channels.py"), "--",
              "--src", CHAN_SRC, "--src-object", CHAN_OBJ, "--dst", v1, "--out", v2]
        c3 = [sys.executable, os.path.join(HERE, "scripts", "make_sw2_textures.py"),
              "--atlas", tex, "--out", d, "--name", r["asset"]]
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

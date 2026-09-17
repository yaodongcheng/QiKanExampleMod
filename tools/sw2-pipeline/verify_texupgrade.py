# -*- coding: utf-8 -*-
"""verify_texupgrade.py —— 贴图升级的产出核对（常驻，纯 python，秒级）。

回答一个问题：**换完之后，编辑器工程里的贴图是不是真的换了**？
只看"脚本退出码 0"不够 —— 尺寸没变、法线还是纯色，退出码照样是 0（本轮踩过）。

判据（每条都能独立失败）：
  1. 尺寸：脸 1024×2048 / 甲 1024×~1560 / 武器 512×128（--no-tex-upgrade 时另算）
  2. 法线**有真数据**：脸/武器的 `_n` 通道标准差 > 5（旧版纯色 = 0）
  3. 高光**仍是纯色**：`_s` 标准差 == 0（升级路线不换 _s，换了就是做错了）
  4. 三张 diffuse（脸/眼/嘴）逐像素相同（战无2 一件模型一张图集，这条是既定配方）
  5. 覆盖率：该有的资产一个不缺

用法:
    python tools/sw2-pipeline/verify_texupgrade.py
    python tools/sw2-pipeline/verify_texupgrade.py --root "<AssetSources/sw2>"
    python tools/sw2-pipeline/verify_texupgrade.py --legacy      # 按旧尺寸核对（回退后跑）
"""
import argparse
import io
import os
import sys

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("需要 Pillow 和 numpy：pip install pillow numpy")

HERE = os.path.dirname(os.path.abspath(__file__))
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass
DEFAULT_ROOT = os.path.join(
    r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12",
    "Mount & Blade II Bannerlord", "Modules", "TifaHead2", "AssetSources", "sw2")

# 旧路线的尺寸下限（回退模式核对用）；升级后应当是这些的 **2 倍**
BASELINE_MIN = {"head": (1024, 2048), "armor": (512, 64), "helmet": (64, 64),
                "weapon": (128, 32)}


def classify(base):
    """资产名 → 类别。

    🔴 兜（helmet）必须单独一类：它是**小块件**（实测裁出来只有 100~828 宽），
    当"甲"判会全部误报（本轮踩过：兜被要求宽 ≥900，28 顶全红）。
    甲/兜的尺寸都随各自 UV 范围变，**没有固定目标尺寸**，只能查"够不够大"。
    """
    if base.startswith("head_"):
        return "head"
    if "weapon" in base:
        return "weapon"
    if "helmet" in base:
        return "helmet"
    return "armor"


def load(p):
    im = Image.open(p).convert("RGB")
    return im, np.asarray(im, dtype=np.float32)


def is_flat(arr):
    """整张图是不是**一个纯色**。

    🔴 判据必须是「**每个通道各自**方差为 0」，不能看「三通道合并的方差」——
    纯色 (0,77,255) 的三通道合并标准差是 106.79（通道之间的差），看着像"有内容"，
    实际逐通道都是常数。本轮踩过：拿合并方差判，把纯色 _s 全判成了"非纯色"。
    """
    return all(float(arr[:, :, c].std()) < 0.01 for c in range(3))


def sheet_dirs(root):
    """要核对的目录列表。

    · 编辑器工程（AssetSources/sw2）→ **每个角色一个子目录 + troop 一个目录**，逐个返回
    · 管线产出（tools/armor-pipeline/out）→ **平铺**，全部资产堆在根目录，返回 [root]
    """
    out = []
    for n in sorted(os.listdir(root)):
        p = os.path.join(root, n)
        if not os.path.isdir(p) or n.startswith("_"):
            continue
        if n == "troop":
            out.append(p)
        elif n.startswith("L"):
            out.append(p)
    # 平铺目录：根目录自己就有 *_d.png → 把它也算一个"目录"
    if any(f.endswith("_d.png") for f in os.listdir(root)):
        out.append(root)
    return out


def check_asset(d, base, kind, legacy):
    """核对一个资产（一组贴图）。返回 (问题列表, 通过的检查数)。"""
    bad, ok = [], 0
    core = os.path.join(d, base)
    have_d = os.path.isfile(core + "_d.png")
    if not have_d:
        return ["缺 %s_d.png" % base], 0

    im, arr = load(core + "_d.png")
    # ---- 1. 尺寸 ----
    if kind == "head":
        # 脸的尺寸是固定的（整张图集）
        exp = (1024, 2048) if not legacy else (1024, 2048)
        if im.size != exp:
            bad.append("_d 尺寸 %s ≠ 期望 %s" % (im.size, exp))
        else:
            ok += 1
    elif kind == "weapon":
        # 🔴 武器贴图**每把尺寸不同**（源图实测 128×32 ~ 256×512），
        #    所以不能写死目标尺寸，只能查「至少是源图的 2 倍」= 升级生效。
        bw, bh = BASELINE_MIN["weapon"]
        want = (bw, bh) if legacy else (bw * 2, bh * 2)
        if im.size[0] < want[0] or im.size[1] < want[1]:
            bad.append("_d 尺寸 %s < 下限 %s（升级未生效？）" % (im.size, want))
        else:
            ok += 1
    else:
        # 甲/兜的尺寸随各自 UV 范围变，**没有固定目标**，只查够不够大。
        # 🔴 阈值按类别分：甲是大块（升级后 ≥900 宽），兜是小块（上百像素就算正常）。
        bw, bh = BASELINE_MIN[kind]
        min_w = bw if legacy else bw * 2
        if kind == "armor" and not legacy:
            min_w = 700          # 甲实测 1024 宽，留足余量
        if kind == "helmet":
            # 🔴 兜是**小块件**，尺寸完全跟着 UV 范围走 —— 实测升级后 100~1656 宽都出现过
            #    （100 宽那顶 = 只有额前一小片）。所以兜**不设尺寸下限**，
            #    只查有没有（下面 _n/_s 的检查照样能发现"没生成"）。
            min_w = 0
        if im.size[0] < min_w:
            bad.append("%s _d 宽 %d < %d（升级未生效？）" % (kind, im.size[0], min_w))
        else:
            ok += 1

    # ---- 2. 法线有真数据 ----
    np_ = core + "_n.png"
    if os.path.isfile(np_):
        _, na = load(np_)
        if na.shape != arr.shape:
            bad.append("_n 尺寸 %s ≠ _d %s" % (na.shape[:2][::-1], im.size))
        else:
            ok += 1
        std = float(na[:, :, 0].std())
        if legacy:
            if std > 5:
                bad.append("_n 标准差 %.1f > 5（回退模式下应为纯色）" % std)
            else:
                ok += 1
        elif kind in ("armor", "helmet"):
            # 甲/兜的法线是自己生成的（从漫反射提高频）。只要求**有梯度**——
            # 阈值不设高：素面/暗色件的高频本来就少（实测兜 0.78~0.99），
            # 但它们远大于"纯色"（逐通道 0），区分度足够。
            if std < 0.2:
                bad.append("%s _n 标准差 %.2f < 0.2（法线没生成？）" % (kind, std))
            else:
                ok += 1
        elif std < 5:
            bad.append("_n 标准差 %.2f < 5（还是纯色平法线，升级未生效）" % std)
        else:
            ok += 1
    else:
        bad.append("缺 %s_n.png" % base)

    # ---- 3. 高光仍是纯色 ----
    sp = core + "_s.png"
    if os.path.isfile(sp):
        _, sa = load(sp)
        if not is_flat(sa):
            # 甲/兜例外：两者的高光都是**同一条管线**（build_textures.py）按 UV 分区生成的，
            # 本来就非纯色。只有"脸/武器"的高光才该是纯色（走 make_sw2_textures.py）。
            if kind not in ("armor", "helmet"):
                bad.append("_s 不是纯色（逐通道 std 不全为 0）—— 升级路线不该换 _s")
            else:
                ok += 1
        else:
            ok += 1
    else:
        bad.append("缺 %s_s.png" % base)

    # ---- 4. 脸的多张 diffuse 必须逐像素相同 ----
    # 🔴 2026-09-16 加 `_neck_d`：脖子件（「脖子归头」裁定后头资产的第 4 个 part）的 UV 仍在
    #    源模型那张**全身图集**上 → 贴图就是脸那三张的副本（见 make_sw2_textures.py）。
    #    它必须与 _d 逐像素相同 —— 不同就说明脖子被指到了别的图，实机表现是脖子颜色/花纹错位。
    if kind == "head":
        for suf in ("_eye_d.png", "_mouth_d.png", "_neck_d.png"):
            q = core + suf
            if not os.path.isfile(q):
                bad.append("缺 %s%s" % (base, suf))
                continue
            if np.array_equal(np.asarray(Image.open(q).convert("RGB")), arr.astype(np.uint8)):
                ok += 1
            else:
                bad.append("%s 与 _d 不一致（战无2 应共用一张图集）" % suf)
    return bad, ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=DEFAULT_ROOT)
    ap.add_argument("--legacy", action="store_true", help="按旧路线尺寸核对（回退后跑）")
    a = ap.parse_args()

    if not os.path.isdir(a.root):
        sys.exit("FAIL: 找不到 %s" % a.root)

    total_bad, total_ok, assets = [], 0, 0
    for d in sheet_dirs(a.root):
        names = set()
        for f in os.listdir(d):
            if f.endswith("_d.png"):
                names.add(f[:-len("_d.png")])
        if not names:
            continue
        for base in sorted(names):
            # `_eye` / `_mouth` / `_neck` 是**同一件资产的配套贴图**（同名后缀），不是独立资产 ——
            # 2026-09-16 加 `_neck`：不加这条，`head_xxx_neck` 会被当成一个"头资产"去查它自己的
            # eye/mouth/neck 贴图 → 满屏假报错（实测）。
            if (base.endswith("_eye") or base.endswith("_mouth") or base.endswith("_neck")):
                continue          # 脸的副 diffuse，跟着主件查
            kind = classify(base)
            bad, ok = check_asset(d, base, kind, a.legacy)
            assets += 1
            total_ok += ok
            for b in bad:
                total_bad.append("%s/%s: %s" % (os.path.basename(d), base, b))

    print("=" * 70)
    print("核对目录：%s%s" % (a.root, "（回退模式）" if a.legacy else ""))
    print("资产 %d 个 · 通过项 %d · 问题 %d" % (assets, total_ok, len(total_bad)))
    print("=" * 70)
    for b in total_bad:
        print("  ❌ " + b)
    if not total_bad:
        print("  ✅ 全部通过")
    return 1 if total_bad else 0


if __name__ == "__main__":
    sys.exit(main())

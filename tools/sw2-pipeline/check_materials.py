# -*- coding: utf-8 -*-
"""check_materials.py —— 28 个头 FBX 的**导入前闸门**（系统 python，不用 Blender，秒级）。

检查什么
--------
1. `TifaHead2\\AssetSources\\sw2\\<角色>\\` 里 7 个文件齐（`head_*_a_v{1,2}.fbx` + 5 张贴图）
2. 头 FBX 里的**材质名**恰好是三个且互不相同：`<裸名>` / `<裸名>_eye` / `<裸名>_mouth`

为什么第二条是硬闸门（CLAUDE.md 铁律 27）
------------------------------------------
编辑器编译会把材质设置全部刷回 FBX 默认值，唯一还能认出"这件是谁"的线索就是材质名；
后处理 `install_pack.py` 的 `skinfix --fullmat` **按材质名判角色**（含不含 eye/mouth/lash）。
名字错或重名 → 每件都刷成同一个配方 → **眼睛和嘴糊上脸皮**，而且要到实机才看得出来
（2026-09-14 实机前抓到过一次：三件全成了 `_mouth`）。

怎么读的：FBX 是二进制，但对象名以明文串存在文件里，直接扫可打印串即可（不需要解 FBX）。

用法：
    python tools/sw2-pipeline/check_materials.py            # 28 人全查
    python tools/sw2-pipeline/check_materials.py --only L13_hanzo
    python tools/sw2-pipeline/check_materials.py --self-test   # 自证：造坏数据必须被拒
Exit: 0 全过 / 1 有不合格（逐条打印原因）/ 2 用法错。
"""
import argparse
import os
import re
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parts_table import TABLE  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")   # 不设的话报错行是乱码
except Exception:
    pass

SW2_DIRS = [
    r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
    r"\Modules\TifaHead2\AssetSources\sw2",
    r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
    r"\Modules\TifaHead2\AssetSources\sw2",
]
TEX_SUFFIX = ["_d.png", "_n.png", "_s.png", "_eye_d.png", "_mouth_d.png"]
PRINTABLE = re.compile(rb"[ -~]{4,60}")


def strings_of(path):
    with open(path, "rb") as fh:
        return {s.decode("ascii") for s in PRINTABLE.findall(fh.read())}


def materials_of(path, base):
    """文件里出现的、属于这个头的材质名（裸名 + 三个后缀）。"""
    want = re.compile(r"^%s(_eye|_mouth|_lash)?$" % re.escape(base))
    return sorted(s for s in strings_of(path) if want.match(s))


def check_one(root, key, entry):
    """→ 问题列表（空 = 合规）。"""
    base = entry["asset"]
    d = os.path.join(root, key)
    fbx = os.path.join(d, base + "_v2.fbx")
    why = []
    if not os.path.isfile(fbx):
        why.append("缺 %s_v2.fbx" % base)
    else:
        got = materials_of(fbx, base)
        want = sorted([base, base + "_eye", base + "_mouth"])
        if got != want:
            why.append("材质名 %s，应为 %s" % ("|".join(got) or "（一个都没扫到）", "|".join(want)))
    for sfx in TEX_SUFFIX:
        if not os.path.isfile(os.path.join(d, base + sfx)):
            why.append("缺贴图 %s%s" % (base, sfx))
    return why


def self_test():
    """自证：造一个"三件全成了 _mouth"的坏头 + 一个合规头，闸门必须一拒一收。"""
    tmp = tempfile.mkdtemp(prefix="lwn_matcheck_")
    try:
        good = os.path.join(tmp, "GOOD")
        os.makedirs(good)
        base = "head_good_a"
        with open(os.path.join(good, base + "_v2.fbx"), "wb") as fh:
            fh.write(b"Kaydara FBX Binary\x00" + b"\x00".join(
                s.encode() for s in (base, base + "_eye", base + "_mouth")))
        for sfx in TEX_SUFFIX:
            open(os.path.join(good, base + sfx), "wb").close()
        bad = os.path.join(tmp, "BAD")
        os.makedirs(bad)
        bbase = "head_bad_a"
        with open(os.path.join(bad, bbase + "_v2.fbx"), "wb") as fh:   # 2026-09-14 实机前抓到的错法
            fh.write(b"\x00".join(s.encode() for s in (bbase + "_mouth",) * 3))
        for sfx in TEX_SUFFIX:
            open(os.path.join(bad, bbase + sfx), "wb").close()
        g = check_one(tmp, "GOOD", dict(asset=base))
        b = check_one(tmp, "BAD", dict(asset=bbase))
        ok = (not g) and bool(b)
        print("自证：合规头 %s · 坏头 %s" % ("通过 ✅" if not g else "被误拒 ❌ %s" % g,
                                            "被拒 ✅（%s）" % b[0] if b else "被漏放 ❌"))
        return 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None, help="只查这几个人（表里的 key）")
    ap.add_argument("--root", default=None, help="资产目录（默认自动探测两个客户端）")
    ap.add_argument("--self-test", action="store_true", help="自证闸门能拒坏数据")
    args = ap.parse_args()

    if args.self_test:
        return self_test()

    root = args.root or next((d for d in SW2_DIRS if os.path.isdir(d)), None)
    if not root or not os.path.isdir(root):
        print("[FATAL] 找不到 sw2 资产目录（两个客户端都试过；可用 --root 指定）", file=sys.stderr)
        return 2

    keys = args.only or sorted(TABLE)
    unknown = [k for k in keys if k not in TABLE]
    if unknown:
        print("[FATAL] 挑件表里没有这些 key：%s\n（可用 key：%s）"
              % (" ".join(unknown), " ".join(sorted(TABLE))), file=sys.stderr)
        return 2

    bad = []
    for key in keys:
        why = check_one(root, key, TABLE[key])
        print("%-16s %-18s %s" % (key, TABLE[key]["cn"], "✅" if not why else "❌ " + "；".join(why)))
        if why:
            bad.append((key, why))

    print("\n%d/%d 合规" % (len(keys) - len(bad), len(keys)))
    if bad:
        print("不合格（**别导入编辑器**，先修生成物）：")
        for k, w in bad:
            print("   %s：%s" % (k, "；".join(w)))
        return 1
    print("可以导入：材质名三件互不相同，后处理按名判角色不会串")
    return 0


if __name__ == "__main__":
    sys.exit(main())

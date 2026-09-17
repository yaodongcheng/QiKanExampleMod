# -*- coding: utf-8 -*-
"""check_materials.py —— 28 个头 FBX 的**导入前闸门**（系统 python，不用 Blender，秒级）。

检查什么
--------
1. `TifaHead2\\AssetSources\\sw2\\<角色>\\` 里有 `head_*_a_v{1,2}.fbx` + 5 张必备贴图
2. 头 FBX 里的**材质名**：必备三件 `裸名` / `裸名_eye` / `裸名_mouth` 一个都不能缺，
   且**每个名字恰好出现一次**（>1 次 = 重名）；可选多一个 `裸名_neck`（脖子，第 4 个部件）。
   除这四类外不许有别的（`_lash` 不在本批头的允许集合里 —— 战无2 头只有 脸/眼/嘴 [+ 脖子]）。

🔴 材质名口径（2026-09-16 晚：从「恰好三个」放宽到「三件齐 + 至多一个 neck」）
------------------------------------------------------------------------------
为什么改成「**数出现次数**」而不是「集合恰好等于」：FBX 是二进制，材质名以明文串存在文件里，
**出现次数可以数**（实测 28 个成品头里每个材质名恰好 1 次），而"两个件都叫同一个名"这种
重名在集合口径下会被去重悄悄吃掉 —— 数次数才抓得住（2026-09-14 抓到的那次就是三件全成 `_mouth`，
它靠的是"少了两个名"暴露的；两个件撞名只会表现为"多了个名"时，集合口径看不出来）。

🔴 脖子贴图口径：`_neck_d/_neck_n/_neck_s` 三张**可选**，但**要么三张齐、要么一张都没有**；
   且 FBX 里已经有 `<裸名>_neck` 材质时三张必须齐（有材质没贴图 = 编辑器/实机里脖子是块裸面）。
   **"缺 neck"不算失败**：现状那批产物还没重跑（脖子贴图要等 `make_sw2_textures` 重跑才有），
   只在"三缺一"或"有材质无贴图"这两种**内部不自洽**的情况下报。

为什么第二条是硬闸门（CLAUDE.md 铁律 27）
------------------------------------------
编辑器编译会把材质设置全部刷回 FBX 默认值，唯一还能认出"这件是谁"的线索就是材质名；
后处理 `install_pack.py` 的 `skinfix --fullmat` **按材质名判角色**
（`MorphFix.cs` 的 `MatRole()`：含 mouth/lash/brow/shadow/eye 才是对应角色，**其余一律当 face**）。
名字错或重名 → 每件都刷成同一个配方 → **眼睛和嘴糊上脸皮**，而且要到实机才看得出来
（2026-09-14 实机前抓到过一次：三件全成了 `_mouth`）。
🔴 角色词集合 2026-09-16 从 eye/mouth/lash **扩到含 neck** —— `<名>_neck` 按 `MatRole()`
会**自动**拿到脸壳配方（脖子与脸同一套皮肤着色，正是我们要的），所以它出现在材质名里是合法且必需的，
`_lash` 本批仍不允许（战无2 头没有独立睫毛件）。

怎么读的：FBX 是二进制，但对象名以明文串存在文件里，直接扫可打印串即可（不需要解 FBX）。

用法：
    python tools/sw2-pipeline/check_materials.py            # 28 人全查
    python tools/sw2-pipeline/check_materials.py --only L13_hanzo
    python tools/sw2-pipeline/check_materials.py --self-test   # 自证：造坏数据必须被拒 + 好数据不许误伤
Exit: 0 全过 / 1 有不合格（逐条打印原因）/ 2 用法错。
"""
import argparse
import collections
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
TEX_REQUIRED = ["_d.png", "_n.png", "_s.png", "_eye_d.png", "_mouth_d.png"]
TEX_NECK = ["_neck_d.png", "_neck_n.png", "_neck_s.png"]     # 可选三张，但要么齐要么都没
MAT_REQUIRED = ("", "_eye", "_mouth")      # 必备材质：脸壳 / 眼 / 嘴
MAT_OPTIONAL = ("_neck",)                  # 可选材质：脖子（头的第 4 个部件）
MAT_ROLE_WORDS = "_eye|_mouth|_lash|_neck"  # 扫材质名时认的角色词（MorphFix.cs MatRole 那一族）
PRINTABLE = re.compile(rb"[ -~]{4,60}")


def strings_of(path):
    """文件里所有可打印串 —— **保留重复**：材质名的出现次数就是判"重名"的依据。"""
    with open(path, "rb") as fh:
        return [s.decode("ascii") for s in PRINTABLE.findall(fh.read())]


def materials_of(path, base):
    """本头家族的 材质名 → 出现次数（裸名 + 各角色词后缀的都在内）。"""
    want = re.compile(r"^%s(%s)?$" % (re.escape(base), MAT_ROLE_WORDS))
    return collections.Counter(s for s in strings_of(path) if want.match(s))


def check_one(root, key, entry):
    """→ 问题列表（空 = 合规）。"""
    base = entry["asset"]
    d = os.path.join(root, key)
    fbx = os.path.join(d, base + "_v2.fbx")
    why = []
    has_neck_mat = False
    if not os.path.isfile(fbx):
        why.append("缺 %s_v2.fbx" % base)
    else:
        cnt = materials_of(fbx, base)
        for sfx in MAT_REQUIRED:
            if cnt.get(base + sfx, 0) == 0:
                why.append("缺材质 %s%s" % (base, sfx))
        has_neck_mat = bool(cnt.get(base + "_neck", 0))
        allowed = {base + s for s in MAT_REQUIRED + MAT_OPTIONAL}
        for nm in sorted(cnt):
            if cnt[nm] > 1:
                why.append("材质 %s 重名（出现 %d 次）—— 后处理按名判角色会串" % (nm, cnt[nm]))
            elif nm not in allowed:
                why.append("多出材质 %s（本批头只许 裸名/_eye/_mouth，可选 _neck）" % nm)
    for sfx in TEX_REQUIRED:
        if not os.path.isfile(os.path.join(d, base + sfx)):
            why.append("缺贴图 %s%s" % (base, sfx))
    # 脖子贴图：三张要么齐、要么都没有；FBX 已经有 _neck 材质时三张必须齐
    have = [sfx for sfx in TEX_NECK if os.path.isfile(os.path.join(d, base + sfx))]
    if have and len(have) != len(TEX_NECK):
        why.append("脖子贴图三缺一（有 %s，缺 %s）"
                   % ("".join(have), "".join(s for s in TEX_NECK if s not in have)))
    elif not have and has_neck_mat:
        why.append("FBX 有 %s_neck 材质却缺脖子贴图 %s（脖子会是块裸面，先跑 make_sw2_textures）"
                   % (base, "/".join(TEX_NECK)))
    return why


def self_test():
    """自证：造坏数据必须被拒，造好数据不许误伤（**含"带脖子的四件头"**，防放宽后误报）。

    FBX 用假文件代替：把材质名按 NUL 分隔写进文件即可（真 FBX 里也正是这样以明文串存在）。
    """
    tmp = tempfile.mkdtemp(prefix="lwn_matcheck_")
    try:
        def make(sub, base, mats, tex_extra=()):
            """造一个假头：mats = 材质名序列（含重复即模拟重名）；tex_extra = 额外给的贴图后缀。"""
            p = os.path.join(tmp, sub)
            os.makedirs(p)
            with open(os.path.join(p, base + "_v2.fbx"), "wb") as fh:
                fh.write(b"Kaydara FBX Binary\x00" +
                         b"\x00".join(m.encode("ascii") for m in mats))
            for sfx in list(TEX_REQUIRED) + list(tex_extra):
                open(os.path.join(p, base + sfx), "wb").close()
            return sub, base

        cases = [
            # (给人看的名字, (子目录, 资产名), 期望被拒?)
            ("三件头（现状产物）", make("GOOD3", "head_good_a",
                                    ("head_good_a", "head_good_a_eye", "head_good_a_mouth")), False),
            ("四件头（材质+贴图都齐）", make("GOOD4", "head_four_a",
                                     ("head_four_a", "head_four_a_eye", "head_four_a_mouth",
                                      "head_four_a_neck"), TEX_NECK), False),
            # 2026-09-14 实机前抓到的错法：三件全成了同一个名
            ("三件全成 _mouth", make("BADMOUTH", "head_bad_a", ("head_bad_a_mouth",) * 3), True),
            # 重名的 _neck：两个件都叫 <裸名>_neck（集合口径看不出来，数次数才抓得住）
            ("重名的 _neck", make("BADNECK", "head_dup_a",
                                ("head_dup_a", "head_dup_a_eye", "head_dup_a_mouth",
                                 "head_dup_a_neck", "head_dup_a_neck"), TEX_NECK), True),
            # 有脖子材质却没写脖子贴图（编辑器里脖子是块裸面）
            ("有 _neck 材质无脖子贴图", make("BADTEX", "head_nt_a",
                                     ("head_nt_a", "head_nt_a_eye", "head_nt_a_mouth",
                                      "head_nt_a_neck")), True),
        ]
        ok = True
        for cn, (sub, base), must_reject in cases:
            why = check_one(tmp, sub, dict(asset=base))
            got_reject = bool(why)
            hit = (got_reject == must_reject)
            ok = ok and hit
            verdict = ("被拒 ✅（%s）" % why[0]) if got_reject else "通过 ✅"
            print("自证 %-24s %s%s" % (cn, verdict,
                                       "" if hit else "   ← ❌ 期望%s" % ("被拒" if must_reject else "通过")))
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
    print("可以导入：材质名 裸名/_eye/_mouth 齐且无重名（带脖子的那批 _neck 也齐），后处理按名判角色不会串")
    return 0


if __name__ == "__main__":
    sys.exit(main())

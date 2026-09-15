# -*- coding: utf-8 -*-
"""stage_for_import.py —— 把甲/头盔的网格+贴图**归拢到导入根**（和脸放一起，按名字区分）。

为什么（2026-09-15 用户裁定）
------------------------------
编辑器导入只认一个地方最省事：`TifaHead2\\AssetSources\\sw2\\<角色>\\`（TifaHead2 的定位就是
"编辑器导入 + tpac 导出"，见 plans/战国无双换装批量落地.md 核心原则①）。所以每个角色一个文件夹，
里面三样东西**靠文件名区分**：

    head_<名>_a_v2.fbx      + _d/_n/_s/_eye_d/_mouth_d     ← 脸（build_heads 产出，本来就在这）
    taikou_<名>_do_a.fbx    + _d/_n/_s                     ← 甲（本条脚本搬进来）
    taikou_<名>_helmet_a.fbx + _d/_n/_s                    ← 头盔（同上；只有 9 人戴盔）
    taikou_<名>_weapon_a.fbx + _d/_n/_s                    ← 武器（同上；每人一件）

兵种通用武器（6 件，`--set troops`）
------------------------------------
兵种不是英雄，没有「角色文件夹」可挂，所以单独归到 `sw2\\troop\\`：

    taikou_troop_<名>_weapon_a.fbx + _d/_n/_s              ← 长枪/打刀/弓/铁炮/忍刀/薙刀

真源仍在 `tools/armor-pipeline/out/`（工具产物）；这里是**为导入准备的副本**，
所以改完甲/头盔要重跑本脚本（幂等，只覆盖比源旧的副本）。

用法：
    python tools/sw2-pipeline/stage_for_import.py                 # 归拢 28 武将
    python tools/sw2-pipeline/stage_for_import.py --set troops    # 归拢 6 件兵种武器
    python tools/sw2-pipeline/stage_for_import.py --check         # 只报缺/过期，不写
"""
import argparse
import io
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
from parts_table import TABLE  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

SRC = os.path.join(REPO, "tools", "armor-pipeline", "out")
MB2 = os.environ.get("MB2_PATH") or r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
DST_ROOT = os.path.join(MB2, "Modules", "TifaHead2", "AssetSources", "sw2")
EXTS = (".fbx", "_d.png", "_n.png", "_s.png")


def slugs():
    """角色 key → 资产 slug（head_yukimura_a → yukimura）。"""
    return {k: TABLE[k]["asset"][len("head_"):-len("_a")] for k in TABLE}


def troop_names():
    """兵种资产名 = 6 件通用武器 + 23 套甲 + 15 顶笠（真源 = troop_parts_table.TROOP_TABLE）。"""
    from build_weapons import TROOP_WEAPONS
    from troop_parts_table import TROOP_TABLE
    out = ["taikou_%s_weapon_a" % slug for _src, slug, _cn, _en, _kind in TROOP_WEAPONS]
    for r in TROOP_TABLE.values():
        out.append("taikou_%s_do_a" % r["slug"])
        if r.get("helmet"):
            out.append("taikou_%s_helmet_a" % r["slug"])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--set", default="lords", choices=["lords", "troops"])
    args = ap.parse_args()
    if not os.path.isdir(DST_ROOT):
        print("[FATAL] 导入根不存在：%s" % DST_ROOT)
        return 2
    # (目标子目录名, [资产名...])。武将一个文件夹一个角色；兵种武器统一进 troop/
    if args.set == "troops":
        jobs = [("troop", troop_names())]
    else:
        jobs = [(key, ["taikou_%s_do_a" % slug, "taikou_%s_weapon_a" % slug] +
                 (["taikou_%s_helmet_a" % slug] if TABLE[key].get("helmet") else []))
                for key, slug in sorted(slugs().items())]
    n_new = n_upd = n_skip = 0
    miss = []
    stale = []
    for sub, names in jobs:
        for nm in names:
            src_fbx = os.path.join(SRC, nm + ".fbx")
            if not os.path.isfile(src_fbx):
                miss.append(nm)
                continue
            dst_dir = os.path.join(DST_ROOT, sub)
            os.makedirs(dst_dir, exist_ok=True)
            for ext in EXTS:
                s, d = src_fbx[:-4] + ext, os.path.join(dst_dir, nm + ext)
                if not os.path.isfile(s):
                    continue
                if os.path.isfile(d) and os.path.getmtime(d) >= os.path.getmtime(s):
                    n_skip += 1
                    continue
                if args.check:
                    # 🔴 2026-09-15 修：原来这里写的是 `(miss if not os.path.isfile(d) else []).append(...)`
                    #    —— 目标已存在时 append 进一个**临时空表**，报告里数不到 → 「过期」永远报 0。
                    stale.append(nm + ext)
                    continue
                # 🔴 存在性必须在 copy **之前**取：copy 完再判 isfile 永远为真
                #    → 「新拷」恒为 0、「更新」把新拷也算进去，报告读起来像什么都没做。
                existed = os.path.isfile(d)
                shutil.copy2(s, d)
                n_new += (not existed)
                n_upd += existed
    print("归拢到 %s（%s）" % (DST_ROOT, args.set))
    print("  新拷 %d · 更新 %d · 已最新 %d · 源里没有 %d · 过期 %d %s"
          % (n_new, n_upd, n_skip, len(set(miss)), len(set(stale)),
             sorted(set(stale))[:6] if stale else ""))
    if args.check and (miss or stale):
        print("  ⚠️ 有缺/过期 —— 跑一次不带 --check 的")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

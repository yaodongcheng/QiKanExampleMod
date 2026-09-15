# -*- coding: utf-8 -*-
"""build_weapons.py —— 28 人批量出武器（挑件 → 规范化 → 贴图 → 出 FBX）。

每人的武器件 = 源模型里材质名带 `mat_w_` 的件（实测 28/28 命中），
序号记在 `parts_table.py` 的 `weapons` 列（供核对，本脚本不依赖它挑件）。

规范化在 `scripts/build_weapon.py` 里做（握持点=手骨、长轴→+Z、握把跨原点），
本脚本只负责批量驱动 + 贴图。

命名：`taikou_<角色slug>_weapon_a`（与甲的 `_do_a`、盔的 `_helmet_a` 同制式）。
产物落 `tools/armor-pipeline/out/`（与甲/盔同目录，staging 脚本一并归拢）。

用法：
    python tools/sw2-pipeline/build_weapons.py --only L00_yukimura
    python tools/sw2-pipeline/build_weapons.py                 # 全部 28 人
    python tools/sw2-pipeline/build_weapons.py --force          # 已存在的也重做
    python tools/sw2-pipeline/build_weapons.py --set troops     # 6 件兵种通用武器
"""
import argparse
import io
import os
import re
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
OUT_DIR = os.path.join(REPO, "tools", "armor-pipeline", "out")
BUILD = os.path.join(HERE, "scripts", "build_weapon.py")
TEX = os.path.join(HERE, "scripts", "make_sw2_textures.py")
# 尺寸表：build_weapon.py 打印的包围盒汇总出来，给物品生成器定 weapon_length 用
DIMS = os.path.join(OUT_DIR, "weapon_dims.csv")

# 武器专用贴图映射：`web/weapons/manifest.json` 的 models[<角色>].tex（查不到的回落角色图集）
def _load_weapon_tex():
    import json
    p = os.path.join(SRC_ROOT, "web", "weapons", "manifest.json")
    if not os.path.isfile(p):
        print("   ⚠️ 没有 %s → 武器贴图回落到角色图集（多半会采错区域）" % p)
        return {}
    d = json.load(io.open(p, encoding="utf-8"))
    return {k: (v or {}).get("tex") for k, v in (d.get("models") or {}).items()}

WEAPON_TEX = _load_weapon_tex()

# ---------- 兵种通用武器（2026-09-15 用户裁定：只留能融进骑砍体系的 6 件）----------
# 源模型 = 该武器**嵌在哪个兵种模型里**（战无2 的兵种武器不是独立资产，是模型里材质名带
# `mat_w_` 的件）。同一把武器在多个模型里是**同一份网格**（顶点数逐一核对过：长枪 6 个模型
# 都是 168v / 打刀 8 个都是 149v / 忍刀 4 个都是 99v），所以每件只取一个源。
#
# 🔴 砍掉的 4 件及理由（想加回来先看这里）：
#   w_pcB0      与 w_longspear 是同一把枪（包围盒 x/y 完全相同，只长度差）→ 重复
#   w_rolling   弯粗棍 0.55m，形态不明、与打刀价值重叠
#   w_ironball  直径 32cm 的球，无柄无刃 → 骑砍没有对应物
#   w_bombA     带引信的陶壶 → 骑砍没有爆炸物物品类型
TROOP_WEAPONS = [
    # (源模型, mesh slug, 中文, 英文 fallback, 类型键)
    ("L250_SOLDIER1", "troop_yari",          "长枪", "Long Spear",   "polearm"),
    ("L253_SOLDIER4", "troop_uchigatana",    "打刀", "Uchigatana",   "sword1h"),
    ("L255_ARCHER",   "troop_yumi",          "弓",   "Bow",          "bow"),
    ("L256_GUNNER",   "troop_teppo",         "铁炮", "Matchlock",    "gun"),
    ("L257_NINJA1",   "troop_shinobigatana", "忍刀", "Shinobi Blade", "sword1h"),
    ("L200_boss1",    "troop_naginata",      "薙刀", "Naginata",     "polearm"),
]


def parse_dims(out, name):
    """从 Blender 输出里抠 [OUT ] 变换后包围盒行 → (总长, 柄侧, 尖侧, 最宽, 最高点) 米。"""
    m = re.search(r"变换后包围盒 x\[(-?[\d.]+),(-?[\d.]+)\] y\[(-?[\d.]+),(-?[\d.]+)\] "
                  r"z\[(-?[\d.]+),(-?[\d.]+)\]", out)
    if not m:
        return None
    v = [float(x) for x in m.groups()]
    return (v[5] - v[4], -v[4], v[5], v[1] - v[0], v[5])


def find_diffuse(key):
    """武器的**专用贴图**：`web/weapons/tex/<tex>.png`（`tex` 从查看器 manifest 读）。

    🔴 **武器不共用角色图集**（2026-09-15 实机抓到的错）：战无2 给每把武器配了独立贴图
    （28/28 实测，manifest 的 `models[<角色>].tex`），武器的 UV 是**相对那张贴图**画的。
    用角色图集 → UV 采到的是图集上完全不相干的区域。实测症状：庆次的枪杆渲染成金色、
    枪头丢掉金属银（正确贴图 `w_keiji0.png` 里杆是深色、枪头是银灰）。
    甲的贴图**仍旧用角色图集**（甲穿在身上、与角色共用图集，那条是对的）。
    """
    tex = WEAPON_TEX.get(key)
    if not tex:
        return None
    for c in (os.path.join(SRC_ROOT, "web", "weapons", "tex", tex + ".png"),):
        if os.path.isfile(c):
            return c
    return None


def run(cmd):
    p = subprocess.run(cmd, capture_output=True)
    return p.returncode, (p.stdout.decode("utf-8", errors="replace") +
                          p.stderr.decode("utf-8", errors="replace"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--set", default="lords", choices=["lords", "troops"],
                    help="lords=28 名武将 / troops=6 件兵种通用武器")
    ap.add_argument("--force", action="store_true", help="已存在的也重做")
    ap.add_argument("--tex-only", action="store_true",
                    help="只重出贴图（网格不动）——换了贴图源之后用这个，省 28 次 Blender 建模")
    args = ap.parse_args()

    # (源模型, mesh slug) 对。武将的 slug 从挑件表的 asset 名推；兵种的写在 TROOP_WEAPONS 里
    if args.set == "troops":
        items = [(k, s) for k, s, _, _, _ in TROOP_WEAPONS]
    else:
        items = [(k, TABLE[k]["asset"][len("head_"):-len("_a")]) for k in sorted(TABLE)]
    if args.only:
        want = set(args.only)
        items = [it for it in items if it[0] in want or it[1] in want]
        if not items:
            sys.exit("FAIL: --only 没匹配到任何条目：%s" % sorted(want))

    done, fail, skip = [], [], []
    dims = []
    for key, slug in items:
        name = "taikou_%s_weapon_a" % slug
        out_fbx = os.path.join(OUT_DIR, name + ".fbx")
        if args.tex_only and os.path.isfile(out_fbx):
            dif = find_diffuse(key)
            if not dif:
                print("  ❌ %-16s 找不到武器贴图" % key); fail.append(key); continue
            rc2, out2 = run([sys.executable, TEX, "--atlas", dif, "--out", OUT_DIR,
                             "--name", name, "--kind", "weapon", "--no-upscale"])
            print("  %s %-16s ← %s" % ("✅" if rc2 == 0 else "❌", key, os.path.basename(dif)))
            (done if rc2 == 0 else fail).append(key)
            continue
        if os.path.isfile(out_fbx) and not args.force:
            print("  [跳过] %-16s 已有 %s" % (key, os.path.basename(out_fbx)))
            skip.append(key)
            continue
        src = os.path.join(SRC_DIR, key + ".fbx")
        rc, out = run([BLENDER, "-b", "--python", BUILD, "--",
                       "--src", src, "--out", OUT_DIR, "--name", name])
        if rc != 0 or not os.path.isfile(out_fbx):
            print("  ❌ %-16s 建武器失败（exit %d）" % (key, rc))
            for l in [x for x in out.splitlines() if x.strip()][-6:]:
                print("        " + l[:150])
            fail.append(key)
            continue
        # 关键行摘出来（长度 / 握持手 / 包围盒）
        for l in out.splitlines():
            if l.startswith(("[AXIS]", "[HAND]", "[OUT ]")):
                print("     " + l)
        # 贴图（3 张：_d 图集 + _n/_s 纯色）
        dif = find_diffuse(key)
        if dif:
            rc2, out2 = run([sys.executable, TEX, "--atlas", dif, "--out", OUT_DIR,
                             "--name", name, "--kind", "weapon", "--no-upscale"])
            tex_ok = rc2 == 0
            if not tex_ok:
                print("     ⚠️ 贴图失败：" + out2.strip().splitlines()[-1][:120])
        else:
            tex_ok = False
            print("     ⚠️ 找不到源图集 → 只出网格不出贴图")
        print("     ✅ %s（%.0f KB）贴图%s" % (name, os.path.getsize(out_fbx) / 1024.0,
                                              "✅" if tex_ok else "⚠️ 缺"))
        d = parse_dims(out, name)
        if d:
            dims.append((name, key) + d)
        done.append(key)

    # 尺寸表（生成器读它定 weapon_length；也给人核对）
    if dims:
        old = {}
        if os.path.isfile(DIMS):
            for line in io.open(DIMS, encoding="utf-8").read().splitlines()[1:]:
                p = line.split(",")
                if len(p) == 7:
                    old[p[0]] = line
        for row in dims:
            old[row[0]] = "%s,%s,%.4f,%.4f,%.4f,%.4f,%.4f" % row
        with io.open(DIMS, "w", encoding="utf-8", newline="") as fh:
            fh.write("name,key,total_m,below_m,above_m,width_m,top_m\n")
            for k in sorted(old):
                fh.write(old[k] + "\n")
        print("尺寸表 -> %s（%d 行）" % (DIMS, len(old)))
    print("\n完成 %d · 跳过 %d · 失败 %d%s" % (len(done), len(skip), len(fail),
          ("（失败：" + " ".join(fail) + "）") if fail else ""))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())

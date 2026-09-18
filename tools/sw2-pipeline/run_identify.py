# -*- coding: utf-8 -*-
"""run_identify.py — 批量跑零件识别（战无2 全部角色）。

用法（系统 python，不需要 Blender）:
  python run_identify.py                 # 跑 28 个有名武将
  python run_identify.py --set troops    # 跑 17 兵种 + 6 护卫
  python run_identify.py --only L00_yukimura L01_keiji
  python run_identify.py --list

产出（Debug/offline/外观批量导入/sw2_parts/，离线产物不进 git）:
  <角色>_parts.csv    零件表
  <角色>_sheet.png    接触图（带编号，行优先；格子→CSV 行序）
  <角色>_sheet_B.png  接触图（背面）
"""
import argparse
import os
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
OUT = os.path.join(REPO, "Debug", "offline", "外观批量导入", "sw2_parts")
SCRIPT = os.path.join(HERE, "scripts", "identify_parts.py")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

# 28 个有名武将（对照表见 Knowledge/战国无双换装工程.md §7.15；谦信怨灵不做、稻姬要做）
LORDS = ["L00_yukimura", "L01_keiji", "L02_nobunaga", "L03_mitsuhide", "L05_kenshin",
         "L06_oichi", "L07_okuni", "L09_magoichi", "L10_shingen", "L11_masamune",
         "L12_nouhime", "L13_hanzo", "L14_rammaru", "L36_hideyoshi", "L38_tadakatsu",
         "L39_inahime", "L40_ieyasu", "L41_mitsunari", "L42_nagamasa", "L43_sakon",
         "L44_yoshihiro", "L45_ginchiyo", "L46_kanetsugu", "L47_nene", "L48_kotaro",
         "L49_musashi", "L100_kojiro", "L101_katsuie"]
TROOPS = ["L250_SOLDIER1", "L251_SOLDIER2", "L252_SOLDIER3", "L253_SOLDIER4", "L254_SOLDIER5",
          "L255_ARCHER", "L256_GUNNER", "L257_NINJA1", "L258_NINJA2", "L259_NOUMIN",
          "L260_KYUSHU1", "L261_KYUSHU2", "L262_TOTSUNIN", "L263_TOBININ", "L264_SENNIN",
          "L265_HAZENIN", "L266_NINJA3",
          "L300_guard1", "L301_guard2", "L302_guard3", "L303_guard4", "L304_guard5", "L305_guard6"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--set", default="lords", choices=["lords", "troops"])
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()

    names = LORDS if a.set == "lords" else TROOPS
    if a.only:
        names = a.only
    if a.list:
        print("\n".join(names))
        return 0

    os.makedirs(a.out, exist_ok=True)
    ok, bad = [], []
    for i, n in enumerate(names, 1):
        src = os.path.join(SRC, n + ".fbx")
        if not os.path.isfile(src):
            print("[%2d/%d] %-16s 源文件不存在，跳过" % (i, len(names), n))
            bad.append(n)
            continue
        p = subprocess.run([BLENDER, "-b", "--python", SCRIPT, "--",
                            "--src", src, "--out", a.out],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        out = (p.stdout or "") + (p.stderr or "")
        line = next((l for l in out.splitlines() if "块零件" in l), "")
        if p.returncode == 0 and line:
            print("[%2d/%d] %s" % (i, len(names), line.strip()))
            ok.append(n)
        else:
            print("[%2d/%d] %-16s 失败（退出码 %d）" % (i, len(names), n, p.returncode))
            for l in out.splitlines()[-6:]:
                print("        " + l)
            bad.append(n)
    print("\n完成：成功 %d / 失败 %d" % (len(ok), len(bad)))
    if bad:
        print("失败：", ", ".join(bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

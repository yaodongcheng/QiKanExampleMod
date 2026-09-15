# -*- coding: utf-8 -*-
"""run_census.py —— 批量跑 part_census.py（28 人一次跑完）。

用法：
    python tools/sw2-pipeline/run_census.py                 # 全部
    python tools/sw2-pipeline/run_census.py --only L02_nobunaga L10_shingen
产出：Debug/offline/sw2_census/<角色>_census.csv（离线产物，不进 git）
"""
import argparse
import os
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
SRC_DIR = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
OUT_DIR = os.path.join(REPO, "Debug", "offline", "sw2_census")
SCRIPT = os.path.join(HERE, "scripts", "part_census.py")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    args = ap.parse_args()
    keys = args.only or sorted(TABLE)
    os.makedirs(OUT_DIR, exist_ok=True)
    ok, bad = [], []
    for key in keys:
        src = os.path.join(SRC_DIR, key + ".fbx")
        if not os.path.isfile(src):
            print("  [跳过] 源文件不存在：%s" % src); bad.append(key); continue
        out = os.path.join(OUT_DIR, key + "_census.csv")
        p = subprocess.run([BLENDER, "-b", "--python", SCRIPT, "--", "--src", src, "--csv", out],
                           capture_output=True)
        txt = p.stdout.decode("utf-8", errors="replace")
        n = txt.count("\n") and len([l for l in txt.splitlines() if "[CENSUS]" in l])
        if os.path.isfile(out):
            rows = sum(1 for _ in open(out, encoding="utf-8")) - 1
            print("  ✅ %-16s → %s（%d 件）" % (key, os.path.basename(out), rows)); ok.append(key)
        else:
            print("  ❌ %-16s 失败（Blender 退出码 %d）" % (key, p.returncode))
            tail = [l for l in (txt or p.stderr.decode("utf-8", errors="replace")).splitlines() if l.strip()][-5:]
            for l in tail:
                print("        " + l[:150])
            bad.append(key)
    print("\n完成：%d 成功 / %d 失败 %s" % (len(ok), len(bad), ("失败：" + " ".join(bad)) if bad else ""))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

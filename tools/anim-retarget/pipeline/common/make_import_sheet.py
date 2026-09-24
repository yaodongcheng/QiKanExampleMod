#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 ModKit 导入填值清单 CSV —— 每个待导入的 TRF 的 duration / Source1 / Source2 / displacement。

【为什么要这张表】TRF 导进 ModKit 后，clip 的 `Source 1 / Source 2 / Duration` 是**默认 0**，
必须手工填成该 TRF 的实际帧范围与时长，否则动画不播；带位移的还要填 `displacement` 向量 + `endProgress`。
（口径与依据见 docs/TRF规范.md §4 与 Knowledge/动画带位移_RootMotion与代码推位移.md）

【列的含义】
  source1 / source2 : TRF 的首帧 / 末帧帧号（**不是固定的 0..N**，就是该 TRF 的时间列范围）
  duration(秒)      : (source2 − source1 + 1) ÷ 30   —— 口径同原版（帧数 ÷ fps）
  displacement X/Y  : 角色本地水平面、单位【米】；X=+右手侧 / Y=+正前方 / Z 恒 0
  endProgress       : 位移走完的进度点（占整条 clip 的比例，原版实测 0.4~1.0）
  ⚠️ 只有【带位移版（__Root）】才填 displacement；原地版与施法一律 0 或留空

用法（在 D:/BrainMaker/骑砍2动画重定向 下跑）：
    python pipeline/common/make_import_sheet.py [--assets <AssetSources 目录>] [--out <csv>]
"""
import argparse
import csv
import io
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))          # 骑砍2动画重定向/


def _find_travel_tool():
    """找量位移的工具。优先用工具链里那份（进 git、不会丢）；再往上找 Debug/offline/ 那份。"""
    v = os.environ.get("LWN_TRAVEL_TOOL")
    if v and os.path.isfile(v):
        return v
    local = os.path.join(HERE, "trf_root_travel.py")
    if os.path.isfile(local):
        return local
    p = HERE
    for _ in range(6):
        c = os.path.join(p, "Debug", "offline", "trf_root_travel.py")
        if os.path.isfile(c):
            return c
        p = os.path.dirname(p)
    return None


TRAVEL_TOOL = _find_travel_tool()

DEFAULT_ASSETS = ("H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/"
                  "Modules/TaikouAnim/AssetSources")
DEFAULT_OUT = os.path.join(ROOT, "output", "modkit_import_sheet.csv")
FPS = 30


def trf_frames(path):
    """读 TRF 的帧范围 → (首帧, 末帧)。第一根骨的第一/最后一个帧号即整条的跨度。"""
    with io.open(path, encoding="utf-8") as f:
        L = [ln.rstrip("\r\n") for ln in f]
    nb = int(L[3])
    i = 4
    first = last = None
    for b in range(nb):
        n = int(L[i]); i += 1
        if b == 0 and n:
            first = int(L[i].split()[0])
            last = int(L[i + n - 1].split()[0])
        i += n
    return first, last


def travel(path):
    """量位移 → (X, Y, endProgress)。复用已验证的 trf_root_travel.py，量不出就返回 None。"""
    if not os.path.isfile(TRAVEL_TOOL):
        return None
    r = subprocess.run([sys.executable, TRAVEL_TOOL, "--trf", path],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    x = re.search(r"X = ([-\d.]+)\s+Y = ([-\d.]+)", r.stdout)
    e = re.search(r"endProgress = ([\d.]+)", r.stdout)
    if not x:
        return None
    return x.group(1), x.group(2), (e.group(1) if e else "")


def role_of(name):
    # 🔴 顺序要紧：长的/更具体的先判 —— "Ambushed" 里含 "Ambush"、"Executed" 里不含 "Execution"，
    #    先判短词就会把受击方错认成攻击方（踩过一次）。
    if "Ambushed" in name:   return "暗杀·受击方"
    if "Ambush" in name:     return "暗杀·攻击方"
    if "Executed" in name:   return "处决·受击方"
    if "Execution" in name:  return "处决·攻击方"
    return "施法"


def kind_of(name):
    if name.endswith("__Root"):    return "带位移版"
    if name.endswith("__Inplace"): return "原地版"
    return "施法"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--assets", default=DEFAULT_ASSETS)
    ap.add_argument("--out", default=DEFAULT_OUT)
    a = ap.parse_args()

    rows = []
    for sub in ("Magic", "Execute"):
        d = os.path.join(a.assets, sub)
        if not os.path.isdir(d):
            print("!! 目录不存在:", d); continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".trf"):
                continue
            p = os.path.join(d, fn)
            f1, f2 = trf_frames(p)
            dur = "%.4f" % ((f2 - f1 + 1) / float(FPS))
            stem = fn[:-4] if fn.endswith(".trf") else fn      # 🔴 先剥后缀再判类型
            role, kind = role_of(stem), kind_of(stem)
            if kind == "带位移版":
                t = travel(p)
                if t:
                    x, y, prog = t
                else:
                    x = y = prog = ""
                note = "填 displacement"
            else:
                x = y = prog = ""
                note = "原地/施法 —— displacement 留空或填 0"
            rows.append({
                "目录": sub, "文件名": fn.replace(".trf", ""), "角色": role, "类型": kind,
                "source1": f1, "source2": f2, "duration(秒)": dur,
                "displacement_X": x, "displacement_Y": y, "endProgress": prog,
                "备注": note,
            })

    order = {"施法": 0, "处决·攻击方": 1, "处决·受击方": 2, "暗杀·攻击方": 3, "暗杀·受击方": 4}
    rows.sort(key=lambda r: (order.get(r["角色"], 9), r["类型"] == "带位移版", r["文件名"]))

    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    cols = ["目录", "文件名", "角色", "类型", "source1", "source2", "duration(秒)",
            "displacement_X", "displacement_Y", "endProgress", "备注"]
    with io.open(a.out, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols, lineterminator="\r\n")
        w.writeheader()
        w.writerows(rows)

    n_root = sum(1 for r in rows if r["类型"] == "带位移版")
    print("共 %d 条（带位移版 %d 条 / 原地·施法 %d 条）" % (len(rows), n_root, len(rows) - n_root))
    print("写出:", a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

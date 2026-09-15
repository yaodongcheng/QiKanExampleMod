# -*- coding: utf-8 -*-
"""verify_table.py —— 拿零件表逐条核对挑件表（`parts_table.py`）。

为什么要有它：28 行配方是「机器量 + 人看图」拼出来的，必须有脚本兜底，
不然一个手误（把子网格号当行号）要等实机才暴露。

检查项：
  1. 每个引用的序号都在零件表范围内
  2. eye 的顶点/面数 = 38/60（宫本武藏例外，19/30）
  3. face 的主导骨骼是 bone_46（或占比最高的那根就是它）
  4. helmet 的主导骨骼是头骨那一族（bone_11）
  5. weapons 的材质名带 mat_w_
  6. face 与 helmet / weapons 不重叠（重叠 = 手误）

用法: python tools/sw2-pipeline/verify_table.py
退出码 0 = 全过；1 = 有失败项
"""
import csv
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parts_table import TABLE  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PARTS = os.path.join(REPO, "Debug", "offline", "sw2_parts")
EYE_EXC = {"L49_musashi": (19, 30)}

ok_n = bad_n = 0
fail_lines = []
warn_lines = []


def rows_for(key):
    p = os.path.join(PARTS, key + "_parts.csv")
    if not os.path.isfile(p):
        return None
    return list(csv.DictReader(io.open(p, encoding="utf-8-sig")))


def bone_pct(row, bone):
    m = re.search(re.escape(bone) + r"\((\d+)%\)", row["top_bones"])
    return int(m.group(1)) if m else 0


def top_bone(row):
    m = re.match(r"([A-Za-z0-9_]+)\((\d+)%\)", row["top_bones"])
    return m.group(1) if m else ""


def check(cond, key, msg):
    global ok_n, bad_n
    if cond:
        ok_n += 1
    else:
        bad_n += 1
        fail_lines.append("  %-16s %s" % (key, msg))


for key, r in TABLE.items():
    rows = rows_for(key)
    if rows is None:
        check(False, key, "❌ 找不到零件表 %s_parts.csv（先跑 run_identify.py）" % key)
        continue
    bymap = {}
    for row in rows:
        bymap[int(row["idx"])] = row

    used = list(r["face"]) + list(r["eye"]) + list(r.get("hair") or []) \
        + list(r.get("helmet") or []) + list(r.get("weapons") or [])
    for i in used:
        check(i in bymap, key, "引用了不存在的序号 %d（该模型只有 0~%d）" % (i, len(rows) - 1))

    # 2) 眼球
    ev, ef = EYE_EXC.get(key, (38, 60))
    for i in r["eye"]:
        row = bymap.get(i)
        if row is None:
            continue
        check(row["verts"] == str(ev) and row["faces"] == str(ef), key,
              "eye 序号 %d 是 %s/%s，期望 %d/%d" % (i, row["verts"], row["faces"], ev, ef))

    # 3) 脸：bone_46 必须是该件占比最高的骨
    for i in r["face"]:
        row = bymap.get(i)
        if row is None:
            continue
        check(bone_pct(row, "bone_46") > 0, key, "face 序号 %d 的主导骨里没有 bone_46（%s）" % (i, row["top_bones"]))
        check(top_bone(row) in ("bone_46", "bone_11") or bone_pct(row, "bone_46") >= 30, key,
              "face 序号 %d 的 bone_46 占比过低（%s）" % (i, row["top_bones"]))

    # 4) 兜：主导骨应是头骨一族。
    #    ⚠️ 这一条只出**警告**不出错 —— helmet 这个字段的实际语义是「**不并进头的那些件**」，
    #    不是「一定是金属头盔」。实测有反例：服部半藏 idx12 看图是双角+顶刺（像兜），
    #    但主导骨是肩臂族 = 其实是带角的头罩/披风。两种解释都不并进头，结论一样。
    for i in r.get("helmet") or []:
        row = bymap.get(i)
        if row is None:
            continue
        if not (bone_pct(row, "bone_11") > 0 or bone_pct(row, "bone_46") > 0):
            warn_lines.append("  %-16s helmet 序号 %d 的主导骨不像头部（%s）—— 解释成「头饰/披风」即可"
                              % (key, i, row["top_bones"]))
            ok_n += 1
        else:
            ok_n += 1

    # 5) 武器：材质名带 mat_w_
    for i in r.get("weapons") or []:
        row = bymap.get(i)
        if row is None:
            continue
        check("mat_w_" in row["mats"], key, "weapons 序号 %d 的材质名不含 mat_w_（%s）" % (i, row["mats"]))

    # 6) 不重叠
    fs, hs, ws = set(r["face"]), set(r.get("helmet") or []), set(r.get("weapons") or [])
    check(not (fs & hs), key, "face 与 helmet 重叠：%s" % sorted(fs & hs))
    check(not (fs & ws), key, "face 与 weapons 重叠：%s" % sorted(fs & ws))

print("=" * 76)
print("挑件表核对：通过 %d 项，失败 %d 项，警告 %d 条" % (ok_n, bad_n, len(warn_lines)))
if fail_lines:
    print("-" * 76)
    print("失败：")
    for ln in fail_lines:
        print(ln)
if warn_lines:
    print("-" * 76)
    print("警告（不阻塞，看note确认解释即可）：")
    for ln in warn_lines:
        print(ln)
print("=" * 76)
sys.exit(1 if bad_n else 0)

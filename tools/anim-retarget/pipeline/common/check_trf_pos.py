#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TRF 批量验收：扫「位置轨首帧」—— 专抓「旋转轨对、位移轨错」的整批坏件。

【为什么需要它】单条 trf 坏能一眼看穿（见 wheels.d/assets.md §15.2：首帧 28 根骨整齐全是单位四元数），
但**一批里混进坏件是看不出来的**：文件大小、帧数、骨数、旋转轨全都正常，只有位置轨的语义错了。
2026-09-24 实测事故：FCS 施法 28 条（09-20 导出）旋转轨与正确版本**逐字相同**，位移轨却是绝对语义
（首帧 `(0, 0.0202, 0.7895)`）⇒ 预览里角色整体悬空约 1m。同窗口的 sw2 / 处决 / 伏击批次同病；
09-21 之后的飞行批次才是对的。**这批在发货前抓到**（TaikouAnim 里当时没有任何施法 TRF）。

【判据】引擎读位置轨时按「相对该骨架静止姿势的增量」读 ⇒ **首帧应 ≈ 0**。
        病征量级是 **0.79 ≈ 0.86（米）= 误用了绝对语义**，产物作废（见 docs/TRF规范.md CHECK_POS）。

【修法】拿同一份 retarget 产物 FBX 过一遍 `fbx_to_trf.py` 重出即可。
        **旁证：旋转轨会与坏件逐字相同，只有位移轨变** —— 若旋转轨也变了，说明动的不只是语义，先查原因。

用法
    python check_trf_pos.py <TRF目录或.trf文件...> [--threshold 0.30]

    退出码：0 = 全部通过；1 = 有文件超阈（可直接当流水线闸门）。

口径
    · 阈值默认 0.30：正常动作本身的骨盆位移可以到 ~0.15（例：引导起手/收招），
      病征是 0.79~0.86 那个量级 —— 0.30 落在两者之间，既不误报也不漏报。
    · `_backup*` / `_` 前缀目录与文件自动跳过（那是归档件，本来就留着旧语义做对照）。
"""
import os
import sys

THRESHOLD = 0.30
SKIP_PREFIX = "_"


def read_name_and_rotdone(path):
    """返回 (动画名, 骨数, 位置块第一行)。只顺序读到位置块头，不解析全部帧。"""
    with open(path, encoding="utf-8") as f:
        lines = [ln.rstrip("\r\n") for ln in f]
    if not lines or not lines[0].startswith("rfver"):
        raise ValueError("不是 TRF（首行=%r）" % (lines[0] if lines else ""))
    name = lines[2].split()[0]
    nb = int(lines[3])
    i = 4
    for _ in range(nb):
        i += 1 + int(lines[i])          # 跳过「帧数」行 + 该骨的帧行
    npos = int(lines[i])
    if npos == 0:
        return name, nb, None
    return name, nb, lines[i + 1]


def collect(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            for root, dirs, files in os.walk(p):
                dirs[:] = [d for d in dirs if not d.startswith(SKIP_PREFIX)]
                out += [os.path.join(root, f) for f in sorted(files) if f.endswith(".trf")]
        elif p.endswith(".trf"):
            out.append(p)
    return sorted(set(out))


def main():
    global THRESHOLD
    argv = [a for a in sys.argv[1:]]
    if "--threshold" in argv:
        i = argv.index("--threshold")
        THRESHOLD = float(argv[i + 1])
        del argv[i:i + 2]
    if not argv:
        print(__doc__)
        return 2

    files = collect(argv)
    if not files:
        print("!! 没找到 .trf")
        return 2

    bad = []
    print("%-46s %-26s %s" % ("文件", "位置轨首帧 (x,y,z)", "最大分量"))
    print("-" * 88)
    for p in files:
        try:
            name, nb, first = read_name_and_rotdone(p)
        except Exception as e:
            print("%-46s 解析失败: %s" % (os.path.basename(p), e))
            bad.append((p, None))
            continue
        if first is None:
            print("%-46s （无位置轨）" % os.path.basename(p))
            continue
        parts = first.split()
        v = [float(x) for x in parts[1:4]]
        m = max(abs(x) for x in v)
        flag = "❌ 绝对语义，作废" if m > THRESHOLD else "✅"
        if m > THRESHOLD:
            bad.append((p, m))
        print("%-46s (%8.4f,%8.4f,%8.4f) %8.4f  %s" % (os.path.basename(p), v[0], v[1], v[2], m, flag))

    print("-" * 88)
    print("共 %d 条，超阈(>%.2f) %d 条" % (len(files), THRESHOLD, len(bad)))
    if bad:
        print("!! 以下文件位移轨是绝对语义，须用 fbx_to_trf.py 重出：")
        for p, m in bad:
            print("   %s%s" % (p, "" if m is None else "  (%.4f)" % m))
        return 1
    print("CHECK_TRF_POS_OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())

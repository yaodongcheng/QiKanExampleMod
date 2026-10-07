# -*- coding: utf-8 -*-
"""make_still_trf.py —— 从一条 TRF 里抠出**末帧姿态**，做成"定格"clip（给"上弦"槽当隐形动作）。

为什么（用户 2026-10-07 提的方案，比借 hold/剪 idle 都干净）：
  · 1~2 帧 = **一次性**（引擎能等到收尾，不会像循环件那样卡流程）
  · 姿态 = 源 clip 的**末帧** ⇒ 播它时人物**本来就在那个姿势上** ⇒ 视觉上等于没动
  · 时长≈0.03~0.07 s ⇒ 装填窗口几乎为零，不会盖住 ch0 的状态机动画

【做法】取源 TRF 每根骨的**最后一帧**四元数，复制成 2 帧（同值）；
  **根位移轨全部写 0**（TRF 里位置是增量，置零 = 不位移 —— 否则人物会被推一下）。

用法：
  python tools/grapple-model/scripts/make_still_trf.py --src <源.trf> --out <目录> --name grapple_ground_still
"""
import argparse
import io
import os


def read_trf(path):
    L = [ln.rstrip("\r\n") for ln in io.open(path, encoding="utf-8", errors="replace")]
    header = L[0:3]                       # rfver / skeleton_anim / <name> <n>
    nb = int(L[3]); i = 4
    bones = []
    for _ in range(nb):
        n = int(L[i]); i += 1
        fr = []
        for _k in range(n):
            fr.append(L[i].split()); i += 1
        bones.append(fr)
    npos = int(L[i]); i += 1
    pos = []
    for _k in range(npos):
        pos.append(L[i].split()); i += 1
    return header, bones, pos


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default="grapple_ground_still")
    ap.add_argument("--frames", type=int, default=2, help="输出几帧（同值，默认 2）")
    a = ap.parse_args()

    header, bones, pos = read_trf(a.src)
    out = []
    out.append(header[0])
    out.append(header[1])
    out.append("%s 1" % a.name)
    out.append(" %d" % len(bones))
    # 🔴 **帧号从 2 起**（用户 2026-10-07 纠正）：他家所有 TRF 都是 `2 … N+1`
    #    （release：17 帧 = 帧号 2..18；clipinfo 的 `Source1/Source2 = 2 / 18` 就是这么来的）。
    #    写 0 起 ⇒ **ModKit 导入卡死**（实测卡在 "Reading TRF file"）。
    first = 2
    for fr in bones:
        # 🔴 **不能取"单帧复制 N 份"**（2026-10-07 实机）：全零运动的 TRF ⇒ 编辑器只生成 `_geo`、
        #    **不出 `_anm`**、导入卡死。旁证 = 用户自己的 `4_Hold` 特意标"无静止·全程微动·闭环 0.14°"。
        #    ⇒ 改成**取源片段的最后 K 帧**（相邻帧差 = 天然极微动，肉眼看不见，但编辑器认）。
        tail = fr[-a.frames:] if len(fr) >= a.frames else fr
        out.append(" %d" % len(tail))
        for k, e in enumerate(tail):
            out.append(" %d %s %s %s %s" % (first + k, e[1], e[2], e[3], e[4]))
    # 根位移轨：全部 0（不位移）
    k_count = min(a.frames, len(bones[0]) if bones else a.frames)
    out.append(" %d" % k_count)
    for k in range(k_count):
        out.append(" %d 0.000000 0.000000 0.000000" % (first + k))

    # 🔴🔴 **结尾必须有 `end`，且换行必须是 CRLF**（2026-10-07 用户纠正）：
    #    缺 `end` / 用 LF ⇒ 编辑器读完等不到结尾，**导入卡在 "Reading TRF file"**（我连踩两次）。
    out.append("end")
    os.makedirs(a.out, exist_ok=True)
    dst = os.path.join(a.out, a.name + ".trf")
    # ⚠️ `newline="\r\n"` 会把字符串里的 `\n` **再翻一次** ⇒ 用 "\r\n".join 会写出 `\r\r\n`（每行多个 \r，
    #    读取器读成空行 —— 2026-10-07 实机：打包器报 `invalid literal for int(): ''`）。
    #    正确写法 = 字符串里只放 `\n`，交给 newline 去翻。
    io.open(dst, "w", encoding="utf-8", newline="\r\n").write("\n".join(out) + "\n")
    print("[STILL] %s -> %s  (骨=%d 帧=%d，源=%s)" % (a.src, dst, len(bones), a.frames, os.path.basename(a.src)))


main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
trf_compose.py —— 把「增量动画」合成到「基础动画」上，产出一条新的 TRF。

═══════════════════════════════════════════════════════════════════════════
为什么需要
    UE 飞行工程里带 `_Add` / `Lean_*` / `Pose_*` 的动画是**增量**（相对某条参照动画的差），
    直接当普通动画导进来播会散架。正确用法（逐骨、局部空间）：

        结果(t) = 基础(t) ∘ ( 增量 ∘ 参照(参照帧)⁻¹ )

    即：先把增量相对参照"减"出来（四元数里减 = 乘逆），再"加"到基础上去（乘）。

🔴 为什么改 TRF 就够了（不用回 Blender、不用源 FBX）
    重定向对每根骨是**乘一个固定旋转 R**（`q_trf = R ⊗ q_src`）。代入：
        (R·增) ∘ (R·参照)⁻¹ = R · (增 ∘ 参照⁻¹) · R⁻¹          ← R 自己约掉
        R·基础 · [R·(增∘参照⁻¹)·R⁻¹] = R · (基础 ∘ 增 ∘ 参照⁻¹) · R⁻¹
    ⇒ **"先在源空间合成再重定向" 与 "先重定向再合成" 逐字等价**，
      所以直接在这批 TRF 上乘就行。

🔴 TRF 的两条轨道语义不一样（别搞混，见 docs/TRF规范.md §1）
    · 旋转 = **绝对局部变换**  → 合成就是上面的四元数乘法
    · 平移 = **相对静止姿势的纯增量**（且只有根骨有） → 合成用"差"相加：
          结果位移 = 基础位移 + ( 增量位移 − 参照位移 )
      （本批飞行增量全是 0 位移，这里仍按通式算，免得将来换个家族踩坑）

用法
    # 合成：把"巡航-左倾"增量加到巡航上
    python trf_compose.py --base fly_A_Flight_HoverMove_A.trf \
                          --add  fly_A_Flight_HoverMove_A_L_Add.trf \
                          --ref  fly_A_Flight_HoverMove_A.trf \
                          --out  fly_HoverMove_A_LeanL.trf

    # 自检：拿"中性"那条增量合成，结果应当 ≈ 基础本身（验参照帧假设 + 合成公式）
    python trf_compose.py --base fly_A_Flight_HoverMove_A.trf \
                          --add  fly_A_Flight_HoverMove_A_Add.trf --check
═══════════════════════════════════════════════════════════════════════════
"""
import argparse
import io
import math
import os
import sys

# Windows 控制台默认是 GBK，中文输出会乱码 —— 能改就改成 UTF-8（改不了不影响功能）
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ─────────────────────────── 四元数小工具（x, y, z, w）───────────────────────────


def qmul(a, b):
    """a ∘ b（先 a 后 b 的局部旋转复合）。"""
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qconj(q):
    """单位四元数的逆（= 共轭）。"""
    x, y, z, w = q
    return (-x, -y, -z, w)


def qnormalize(q):
    n = math.sqrt(sum(c * c for c in q))
    if n < 1e-12:
        return (0.0, 0.0, 0.0, 1.0)
    return tuple(c / n for c in q)


def qangle_deg(a, b):
    """两个朝向之间的夹角（度，0~180）。"""
    d = abs(sum(x * y for x, y in zip(qnormalize(a), qnormalize(b))))
    d = max(-1.0, min(1.0, d))
    return math.degrees(2.0 * math.acos(d))


def qslerp_identity(q, s):
    """从"不转"插值到 q 的 s 倍（s=0 → 单位四元数，s=1 → q）。用来缩放某根骨的增量转了多少。"""
    q = qnormalize(q)
    if q[3] < 0.0:                      # 走短弧
        q = tuple(-x for x in q)
    ang = math.acos(max(-1.0, min(1.0, q[3])))
    if ang < 1e-9:
        return (0.0, 0.0, 0.0, 1.0)
    k = math.sin(ang * s) / math.sin(ang)
    return (q[0] * k, q[1] * k, q[2] * k, math.cos(ang * s))


# ─────────────────────────────── TRF 读写 ───────────────────────────────


class Trf(object):
    """一个 TRF：每根骨的旋转帧 + 根骨的位移帧。

    bones[b]      = [(frame, (x, y, z, w)), ...]   ← 绝对局部旋转
    root_pos      = [(frame, (x, y, z)), ...]      ← 纯增量位移（只有根骨）
    """

    def __init__(self):
        self.name = ""
        self.bones = []
        self.root_pos = []

    @property
    def bone_count(self):
        return len(self.bones)

    def bone_first(self, b):
        return self.bones[b][0][1]

    def bone_at(self, b, frame_no):
        """按 Blender 帧号取（找不到就报错，不做静默兜底）。"""
        for f, q in self.bones[b]:
            if f == frame_no:
                return q
        raise KeyError("骨 %d 没有帧号 %d（该文件帧范围 %d~%d）"
                       % (b, frame_no, self.bones[b][0][0], self.bones[b][-1][0]))


def read_trf(path):
    if not os.path.isfile(path):
        raise IOError("文件不存在: " + path)
    txt = io.open(path, encoding="utf-8", errors="strict").read()
    lines = [ln.strip() for ln in txt.replace("\r", "").split("\n")]
    lines = [ln for ln in lines if ln != ""]

    if lines[0] != "rfver 4" or lines[1] != "skeleton_anim 1":
        raise ValueError("不是 skeleton_anim 型 TRF: " + path)

    t = Trf()
    t.name = lines[2].rsplit(" ", 1)[0]          # "<动画名> 1"
    bone_count = int(lines[3])

    i = 4
    for b in range(bone_count):
        n = int(lines[i]); i += 1
        frames = []
        for _ in range(n):
            p = lines[i].split(); i += 1
            frames.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]), float(p[4]))))
        t.bones.append(frames)

    if i < len(lines) and lines[i] != "end":
        n = int(lines[i]); i += 1
        for _ in range(n):
            p = lines[i].split(); i += 1
            t.root_pos.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]))))
    return t


def write_trf(t, path):
    out = []
    out.append("rfver 4")
    out.append("skeleton_anim 1")
    out.append("%s 1" % t.name)
    out.append(" %d" % t.bone_count)
    for frames in t.bones:
        out.append(" %d" % len(frames))
        for f, q in frames:
            out.append(" %d %.6f %.6f %.6f %.6f" % (f, q[0], q[1], q[2], q[3]))
    out.append(" %d" % len(t.root_pos))
    for f, p in t.root_pos:
        out.append(" %d %.6f %.6f %.6f" % (f, p[0], p[1], p[2]))
    out.append("end")

    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with io.open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\n".join(out) + "\n")


# ─────────────────────────────── 合成主逻辑 ───────────────────────────────


def compose(base, add, ref, ref_frame=None, root_scale=1.0, root_index=0):
    """结果(t) = 基础(t) ∘ (增量 ∘ 参照(参照帧)⁻¹)。ref_frame=None ⇒ 用参照文件的第一帧。

    root_scale：**根骨（默认 0 号 = pelvis）那道增量缩放多少**。1.0 = 原样（旧行为），
      0 = 完全不加（身体朝向保持基础动画的，倾斜只来自脊柱/四肢）。
      🔴 为什么要这个旋钮（2026-09-22 用户在查看器里盯出来的）：
         `FM_A_Lean_*` 这批"倾斜"增量在**根骨上有 85°**（其它骨最大 22°），照原样合成
         = 把人**整体转了 90°**；而 UE 那边这个倾斜的实际观感是"头带着肩膀倾"。
         根骨那道量是**趴姿家族根骨读数 ~90° 的已知疑点**（见方案 §3.7）在增量上的投影，
         不是"倾斜"本身 —— 所以合成时要能把它摘掉。
    """
    if base.bone_count != add.bone_count or base.bone_count != ref.bone_count:
        raise ValueError("骨数不一致: base=%d add=%d ref=%d"
                         % (base.bone_count, add.bone_count, ref.bone_count))

    # 增量是不是常量？（这批飞行增量都是 2 帧且两帧相同）
    for b in range(add.bone_count):
        frames = add.bones[b]
        for f, q in frames[1:]:
            if qangle_deg(q, frames[0][1]) > 0.01:
                print("[warn] 骨 %d 的增量不是常量（帧 %d 与首帧差 %.2f°）—— 本脚本按首帧当成常量处理"
                      % (b, f, qangle_deg(q, frames[0][1])))

    r = Trf()
    r.name = base.name + "_composed"
    # 🔴 名字在 main() 里按【输出文件名】覆盖 —— TRF 第 3 行是 ModKit 用来生成 clip 名的
    #     （见 fbx_to_trf.py 的 `--name` 注释）。原来写死 `_composed` 的后果（2026-09-22 用户抓到）：
    #     所有合成件内部名都叫 `<基准>_composed`，**既看不出是哪一条、LeanL 和 LeanR 还会撞名**。
    deltas = []

    for b in range(base.bone_count):
        q_ref = ref.bone_at(b, ref_frame) if ref_frame is not None else ref.bone_first(b)
        d = qmul(add.bone_first(b), qconj(q_ref))
        if b == root_index and root_scale < 0.999:
            _before = qangle_deg(d, (0, 0, 0, 1))
            d = qslerp_identity(d, max(0.0, root_scale))
            print("[info] 根骨（骨 %d）增量缩放 %.2f：%.2f° → %.2f°"
                  % (b, root_scale, _before, qangle_deg(d, (0, 0, 0, 1))))
        deltas.append(d)
        r.bones.append([(f, qnormalize(qmul(q, d))) for f, q in base.bones[b]])

    # 根骨位移：差相加（增量与参照都是纯增量语义）
    if base.root_pos:
        add_first = add.root_pos[0][1] if add.root_pos else (0.0, 0.0, 0.0)
        if ref.root_pos:
            rp = ref.root_pos[0][1]
            if ref_frame is not None:
                for f, p in ref.root_pos:
                    if f == ref_frame:
                        rp = p
                        break
        else:
            rp = (0.0, 0.0, 0.0)
        delta_p = (add_first[0] - rp[0], add_first[1] - rp[1], add_first[2] - rp[2])
        r.root_pos = [(f, (p[0] + delta_p[0], p[1] + delta_p[1], p[2] + delta_p[2]))
                      for f, p in base.root_pos]
    return r, deltas


def report_deltas(deltas):
    """列出被增量改动最大的骨头（看它动的是不是该动的地方）。"""
    order = sorted(range(len(deltas)), key=lambda b: -qangle_deg(deltas[b], (0, 0, 0, 1)))
    print("  增量幅度最大的 6 根骨（局部旋转相对'无变化'的角度）:")
    for b in order[:6]:
        print("    骨 #%-2d  %6.2f°" % (b, qangle_deg(deltas[b], (0, 0, 0, 1))))


def compare(out, base):
    """逐骨比较两条 TRF（用于自检：中性增量合成后应当 ≈ 基础）。"""
    worst = 0.0
    worst_bone = -1
    for b in range(base.bone_count):
        for (f1, q1), (f2, q2) in zip(out.bones[b], base.bones[b]):
            a = qangle_deg(q1, q2)
            if a > worst:
                worst, worst_bone = a, b
    n = sum(len(x) for x in base.bones)
    print("  与基础逐帧逐骨对比: 最大偏差 %.4f°（骨 #%d），共 %d 个旋转帧" % (worst, worst_bone, n))
    return worst


def main():
    ap = argparse.ArgumentParser(description="把增量动画合成到基础动画上（TRF → TRF）")
    ap.add_argument("--base", required=True, help="基础动画 TRF")
    ap.add_argument("--add", required=True, help="增量动画 TRF")
    ap.add_argument("--ref", help="参照动画 TRF（默认 = 基础）")
    ap.add_argument("--ref-frame", type=int, default=None,
                    help="参照取哪一帧（Blender 帧号；默认取参照文件的第一帧）")
    ap.add_argument("--out", help="输出 TRF（--check 时可省）")
    ap.add_argument("--name", help="TRF 里的动画名（缺省 = 输出文件名，ModKit 靠它生成 clip 名）")
    ap.add_argument("--root-scale", type=float, default=1.0,
                    help="根骨（0 号，pelvis）那道增量缩放多少：1=原样，0=完全不加（身体朝向保持基础动画）")
    ap.add_argument("--check", action="store_true",
                    help="自检模式：不写文件，只报告'合成结果 vs 基础'的偏差（中性增量应当 ≈ 0）")
    a = ap.parse_args()

    base = read_trf(a.base)
    add = read_trf(a.add)
    ref = read_trf(a.ref) if a.ref else base

    print("基础 = %s（%d 骨 / %d 帧）" % (base.name, base.bone_count, len(base.bones[0])))
    print("增量 = %s（%d 帧）" % (add.name, len(add.bones[0])))
    print("参照 = %s%s" % (ref.name, "" if a.ref else "（= 基础）"))

    out, deltas = compose(base, add, ref, a.ref_frame, a.root_scale)
    report_deltas(deltas)

    if a.check:
        compare(out, base)
        print("自检完毕（--check 不写文件）")
        return 0

    if not a.out:
        print("ERR: 没给 --out（或加 --check 只做自检）", file=sys.stderr)
        return 1
    # 🔴 TRF 第 3 行的动画名 = **输出文件名**（ModKit 靠它生成 clip 名）——
    #    不这么写就会全叫 `<基准>_composed`（既看不出是哪条、LeanL/LeanR 还会撞名）。
    out.name = a.name or os.path.splitext(os.path.basename(a.out))[0]
    write_trf(out, a.out)
    print("已写出: %s（动画名 = %s）" % (a.out, out.name))
    compare(out, base)
    return 0


if __name__ == "__main__":
    sys.exit(main())

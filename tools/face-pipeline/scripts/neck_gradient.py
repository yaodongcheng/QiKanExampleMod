# neck_gradient.py —— 从脖子往下做肤色渐变（把脸贴图的领口段逐渐拉向身体的色调）
#
# 症状（2026-09-14 用户实测，男女都有）：脖子/领口一段的肤色与身体接不上 ——
#   脖子偏暗、偏饱和，身体偏亮。
#   🔴 2026-09-14 重测（从 `AssetSources/color_compare/男-有光对比.png` 的取样框直接量）：
#     male   身体÷脖子 = (1.107, 1.110, 1.105)  ← **几乎是均匀提亮 11%，色相基本不变**
#     female 身体÷脖子 ≈ (1.11, 1.11, 1.10)     （女-有光对比 同法量）
#   ⚠️ 早先记在这里的 (1.09, 1.32, 1.42) **与截图实测对不上**（按它上色 → 领口发灰"漂白"，
#      渲染预览一眼假）。以本次实测值为准。
#
# 🔴 为什么不能"按 UV 矩形刷"：
#   蒂法的领口 UV 是连续的一条（z↔v 相关 +0.87），但**萨菲罗斯的不是**（相关 −0.55，
#   z 1.55~1.58 那一小段 UV 直接跳到图集另一端）→ 按矩形刷会连带刷到脸上。
#   所以改成【把网格的高度属性烘焙进 UV 空间】：逐三角形在 UV 上光栅化，
#   重心插值出每个像素对应的高度 z，再按 z 决定混合量（下颌 0% → 领口外沿 100%）。
#   两个头通用，且不依赖 UV 是否连续。
#
# 用法（Blender 跑，因为要读 FBX 的网格）：
#   blender --background --python neck_gradient.py -- \
#       --fbx <头部FBX> --tex <脸皮diffuse.png> --out <输出.png> \
#       [--z-top 1.62] [--ramp 0.10] [--ratio "1.11,1.11,1.10"] [--strength 1.0] [--suffix .0]
#
# 参数含义：
#   --z-top     这个高度以上完全不动（= 脸，保持原样）
#   --z-bottom  这个高度及以下施加满强度
#   --ratio     目标色相对原色的倍数（默认取上面量出来的系统偏移）
#   --strength  总强度闸门（1.0 = 用满 ratio；调小 = 过渡更保守）
#
# ⚠️ 只改【diffuse】。法线/高光贴图不动（改颜色不改形状）。
import bpy, sys, os
import numpy as np


def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d


def fail(m):
    print("FATAL: " + m); sys.stdout.flush(); sys.exit(1)


# 原版身体【领口内沿】轮廓：角度 → (半径, 高度)。与 build_head.py 的 RIM_TABLE["male"] 同源
# （当初就是用它把领口"收进身体"的）。👈 用它当渐变的基准高度。
#
# 🔴 为什么渐变必须【按角度取基准高度】，不能用固定 z：
#   --fit-rim 把领口收进了身体内侧 → 我们网格自己的边缘是看不见的，
#   玩家肉眼看到的接缝 = **我们的面从身体里钻出来的那条交线**，而这条线**随角度起伏**
#   （正面低、侧面高）。用固定 z 当起点，只有量过的那个角度对，其它角度全错位
#   —— 实测踩过：色带在某个视角量到 z≈1.53，按它做渐变，换角度看就完全对不上。
RIM_MALE = [(0.0, 0.0870, 1.5271), (15.0, 0.0974, 1.4986), (30.0, 0.1020, 1.4930),
            (45.0, 0.1068, 1.4883), (60.0, 0.1113, 1.4555), (75.0, 0.1180, 1.4350),
            (90.0, 0.1232, 1.4144), (105.0, 0.1113, 1.4555), (120.0, 0.1068, 1.4883),
            (135.0, 0.1020, 1.4930), (150.0, 0.0974, 1.4986), (165.0, 0.0870, 1.5271),
            (180.0, 0.0849, 1.5444), (195.0, 0.0896, 1.5410), (210.0, 0.0808, 1.5364),
            (225.0, 0.0750, 1.5340), (240.0, 0.0711, 1.5326), (255.0, 0.0710, 1.5325),
            (270.0, 0.0710, 1.5324), (285.0, 0.0711, 1.5326), (300.0, 0.0750, 1.5340),
            (315.0, 0.0808, 1.5364), (330.0, 0.0896, 1.5410), (345.0, 0.0849, 1.5444)]


def rim_z_at(adeg):
    """按角度插值出该处口沿的高度（度，0=+X 侧，90=+Y 正前）"""
    a = adeg % 360.0
    for k in range(len(RIM_MALE)):
        a0, _, z0 = RIM_MALE[k]
        a1, _, z1 = RIM_MALE[(k + 1) % len(RIM_MALE)]
        if a1 <= a0:
            a1 += 360.0
        if a0 <= a <= a1:
            t = (a - a0) / (a1 - a0)
            return z0 + (z1 - z0) * t
    return RIM_MALE[0][2]


def main():
    a = args_after_ddash()
    fbx = get(a, "--fbx"); tex = get(a, "--tex"); out = get(a, "--out")
    if not fbx or not tex or not out:
        fail("需要 --fbx / --tex / --out")
    z_top = float(get(a, "--z-top", "1.62"))     # 这个高度以上一律不碰（保护脸）
    ramp = float(get(a, "--ramp", "0.10"))       # 从"该角度的口沿高度"往上多少米渐弱到 0
    strength = float(get(a, "--strength", "1.0"))
    suffix = get(a, "--suffix", ".0")
    ratio = [float(x) for x in get(a, "--ratio", "1.11,1.11,1.10").split(",")]
    if ramp <= 0:
        fail("--ramp 必须 > 0")

    # ---------- 1) 读网格：领口段的三角形（UV + z） ----------
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    cand = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith(suffix)]
    if not cand:
        cand = [o for o in bpy.data.objects if o.type == 'MESH']
    ob = cand[0]
    me = ob.data
    uvl = me.uv_layers.active
    if uvl is None:
        fail("%s 没有 UV 层" % ob.name)
    mw = ob.matrix_world
    vs = [mw @ v.co for v in me.vertices]
    import math as _m

    def t_of(p):
        """权值 t：以【该角度处的口沿高度】为基准 —— 交线处 = 1，往上 ramp 米渐弱到 0。
        🔴 不能用固定 z：`--fit-rim` 把领口收进了身体内侧，**我们网格自己的边缘看不见**，
           玩家肉眼看到的接缝 = 【我们的面从身体里钻出来的那条交线】，而这条线**随角度起伏**
           （正面低、侧面高）。用固定 z 当基准，只有量过的那个角度对得上（实测踩过）。"""
        if p.z > z_top:
            return 0.0
        rz = rim_z_at(_m.degrees(_m.atan2(p.y, p.x)))
        return max(0.0, min(1.0, 1.0 - (p.z - rz) / ramp))

    tv = [t_of(p) for p in vs]
    tris = []
    for p in me.polygons:
        ids = list(p.vertices)
        ts = [tv[i] for i in ids]
        if max(ts) <= 1e-4:
            continue
        uvs = [tuple(uvl.data[li].uv) for li in p.loop_indices]
        for k in range(1, len(ids) - 1):
            tris.append(((uvs[0], uvs[k], uvs[k + 1]), (ts[0], ts[k], ts[k + 1])))
    print("上色三角面：%d（基准=各角度口沿高度，渐变带 %.0fmm）" % (len(tris), ramp * 1000))
    if not tris:
        fail("没有需要上色的面")

    # ---------- 2) 读贴图 ----------
    img = bpy.data.images.load(os.path.abspath(tex))
    W, H = img.size
    buf = np.empty(W * H * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    px = buf.reshape(H, W, 4)                    # Blender: 行 0 = 图底部

    # ---------- 3) 光栅化：把每个像素对应的 z 烘进一张权重图 ----------
    blend = np.zeros((H, W), dtype=np.float32)   # 0 = 不动，1 = 满强度
    for uvs, ts in tris:
        P = np.array([[u * W, v * H] for (u, v) in uvs], dtype=np.float64)
        T = np.array(ts, dtype=np.float64)
        x0 = max(int(np.floor(P[:, 0].min())), 0); x1 = min(int(np.ceil(P[:, 0].max())), W - 1)
        y0 = max(int(np.floor(P[:, 1].min())), 0); y1 = min(int(np.ceil(P[:, 1].max())), H - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs = np.arange(x0, x1 + 1) + 0.5
        ys = np.arange(y0, y1 + 1) + 0.5
        gx, gy = np.meshgrid(xs, ys)
        d = ((P[1, 0] - P[0, 0]) * (P[2, 1] - P[0, 1]) - (P[2, 0] - P[0, 0]) * (P[1, 1] - P[0, 1]))
        if abs(d) < 1e-12:
            continue
        w0 = ((P[1, 0] - gx) * (P[2, 1] - gy) - (P[2, 0] - gx) * (P[1, 1] - gy)) / d
        w1 = ((P[2, 0] - gx) * (P[0, 1] - gy) - (P[0, 0] - gx) * (P[2, 1] - gy)) / d
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        tt = (w0 * T[0] + w1 * T[1] + w2 * T[2]) * strength
        sub = blend[y0:y1 + 1, x0:x1 + 1]
        np.maximum(sub, np.where(inside, np.clip(tt, 0.0, 1.0), 0.0), out=sub)

    cov = int((blend > 1e-4).sum())
    print("被渐变覆盖的像素：%d / %d（%.1f%%）" % (cov, W * H, 100.0 * cov / (W * H)))
    if cov == 0:
        fail("权重图全 0 —— UV 或 z 范围不对")

    # ---------- 4) 上色 ----------
    rgb = px[:, :, :3]
    tgt = rgb * np.array(ratio, dtype=np.float32)
    w = blend[:, :, None]
    rgb[:] = rgb * (1.0 - w) + tgt * w
    px[:, :, :3] = np.clip(rgb, 0.0, 1.0)

    out_abs = os.path.abspath(out)
    out_img = bpy.data.images.new("neckout", width=W, height=H, alpha=True)
    out_img.pixels.foreach_set(px.reshape(-1))
    out_img.filepath_raw = out_abs
    out_img.file_format = 'PNG'
    out_img.save()
    print("EXPORTED -> %s" % out_abs)
    print("参数：z_top=%.3f ramp=%.0fmm ratio=%s strength=%.2f" % (z_top, ramp * 1000, ratio, strength))


main()

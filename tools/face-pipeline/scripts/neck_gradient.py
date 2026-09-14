# neck_gradient.py —— 从脖子往下做肤色渐变（把脸贴图的领口段逐渐拉向身体的色调）
#
# 症状（2026-09-14 用户实测，男女都有）：脖子/领口一段的肤色与身体接不上 ——
#   脖子偏红、偏暗、偏饱和，身体偏中性、偏亮。实测两者的比值：
#     male   身体÷脖子 = (1.09, 1.32, 1.42)
#     female 身体÷脖子 = (1.07, 1.32, 1.36)   ← 两个角色几乎一致 = 系统性偏移
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
#       [--z-top 1.58] [--z-bottom 1.42] [--ratio "1.08,1.32,1.39"] [--strength 1.0] [--suffix .0]
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


def main():
    a = args_after_ddash()
    fbx = get(a, "--fbx"); tex = get(a, "--tex"); out = get(a, "--out")
    if not fbx or not tex or not out:
        fail("需要 --fbx / --tex / --out")
    z_top = float(get(a, "--z-top", "1.58"))
    z_bot = float(get(a, "--z-bottom", "1.42"))
    ratio = [float(x) for x in get(a, "--ratio", "1.08,1.32,1.39").split(",")]
    strength = float(get(a, "--strength", "1.0"))
    suffix = get(a, "--suffix", ".0")
    if z_top <= z_bot:
        fail("--z-top 必须大于 --z-bottom")

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
    tris = []
    for p in me.polygons:
        ids = list(p.vertices)
        zs = [vs[i].z for i in ids]
        if max(zs) > z_top:                      # 只要有顶点在 z_top 之上就不碰（保护脸）
            continue
        uvs = [tuple(uvl.data[li].uv) for li in p.loop_indices]
        for k in range(1, len(ids) - 1):         # 扇形三角化
            tris.append(((uvs[0], uvs[k], uvs[k + 1]), (zs[0], zs[k], zs[k + 1])))
    print("领口段三角面：%d 个（z ≤ %.3f）" % (len(tris), z_top))
    if not tris:
        fail("没找到 z ≤ %.3f 的面 —— 检查 --suffix / --z-top" % z_top)

    # ---------- 2) 读贴图 ----------
    img = bpy.data.images.load(os.path.abspath(tex))
    W, H = img.size
    buf = np.empty(W * H * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    px = buf.reshape(H, W, 4)                    # Blender: 行 0 = 图底部

    # ---------- 3) 光栅化：把每个像素对应的 z 烘进一张权重图 ----------
    blend = np.zeros((H, W), dtype=np.float32)   # 0 = 不动，1 = 满强度
    for uvs, zs in tris:
        P = np.array([[u * W, v * H] for (u, v) in uvs], dtype=np.float64)
        Z = np.array(zs, dtype=np.float64)
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
        zmap = w0 * Z[0] + w1 * Z[1] + w2 * Z[2]
        t = (z_top - zmap) / (z_top - z_bot)     # z 越低 → t 越大
        t = np.clip(t, 0.0, 1.0) * strength
        sub = blend[y0:y1 + 1, x0:x1 + 1]
        np.maximum(sub, np.where(inside, t, 0.0), out=sub)

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
    print("参数：z_top=%.3f z_bot=%.3f ratio=%s strength=%.2f" % (z_top, z_bot, ratio, strength))


main()

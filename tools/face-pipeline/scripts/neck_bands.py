# neck_bands.py —— 给领口刷【高度色带】（诊断用，不调颜色）
#
# 目的：**量出"可见交界线"落在哪个高度**，别再靠猜。
#   做法：把领口那段（z 范围）按每 1cm 一条刷成不同色相（红→橙→黄→绿→青→蓝→紫），
#   打进包后进游戏截一张图，从图上直接读出交界线处是哪个颜色 → 换算成 z。
#
# 复用 neck_gradient.py 的同一套光栅化（把网格高度烘进 UV 空间），保证覆盖范围一致。
# 除领口段外，贴图其余部分保持原样（脸不受影响）。
#
# 用法：
#   blender --background --python neck_bands.py -- \
#       --fbx <头部FBX> --tex <脸皮diffuse.png> --out <输出.png> \
#       [--z-top 1.63] [--z-bottom 1.39] [--step 0.01] [--suffix .0]
import bpy, sys, os, colorsys
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
    z_top = float(get(a, "--z-top", "1.63"))
    z_bot = float(get(a, "--z-bottom", "1.39"))
    step = float(get(a, "--step", "0.01"))
    suffix = get(a, "--suffix", ".0")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    cand = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith(suffix)]
    if not cand:
        cand = [o for o in bpy.data.objects if o.type == 'MESH']
    ob = cand[0]; me = ob.data; uvl = me.uv_layers.active
    if uvl is None:
        fail("%s 没有 UV 层" % ob.name)
    mw = ob.matrix_world
    vs = [mw @ v.co for v in me.vertices]
    tris = []
    for p in me.polygons:
        ids = list(p.vertices)
        zs = [vs[i].z for i in ids]
        if max(zs) > z_top:
            continue
        uvs = [tuple(uvl.data[li].uv) for li in p.loop_indices]
        for k in range(1, len(ids) - 1):
            tris.append(((uvs[0], uvs[k], uvs[k + 1]), (zs[0], zs[k], zs[k + 1])))
    print("领口段三角面：%d（z ≤ %.3f）" % (len(tris), z_top))
    if not tris:
        fail("没有 z ≤ %.3f 的面" % z_top)

    img = bpy.data.images.load(os.path.abspath(tex))
    W, H = img.size
    buf = np.empty(W * H * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    px = buf.reshape(H, W, 4)

    nb = int(round((z_top - z_bot) / step)) + 1
    # 每个像素对应的 z（先算一张 z 图，用最近覆盖）
    zmap = np.full((H, W), np.nan, dtype=np.float32)
    for uvs, zs in tris:
        P = np.array([[u * W, v * H] for (u, v) in uvs], dtype=np.float64)
        Z = np.array(zs, dtype=np.float64)
        x0 = max(int(np.floor(P[:, 0].min())), 0); x1 = min(int(np.ceil(P[:, 0].max())), W - 1)
        y0 = max(int(np.floor(P[:, 1].min())), 0); y1 = min(int(np.ceil(P[:, 1].max())), H - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = ((P[1, 0] - P[0, 0]) * (P[2, 1] - P[0, 1]) - (P[2, 0] - P[0, 0]) * (P[1, 1] - P[0, 1]))
        if abs(d) < 1e-12:
            continue
        w0 = ((P[1, 0] - gx) * (P[2, 1] - gy) - (P[2, 0] - gx) * (P[1, 1] - gy)) / d
        w1 = ((P[2, 0] - gx) * (P[0, 1] - gy) - (P[0, 0] - gx) * (P[2, 1] - gy)) / d
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        zz = w0 * Z[0] + w1 * Z[1] + w2 * Z[2]
        sub = zmap[y0:y1 + 1, x0:x1 + 1]
        sub[inside] = zz[inside].astype(np.float32)

    cov = int(np.isfinite(zmap).sum())
    print("上色像素：%d（%.1f%%）" % (cov, 100.0 * cov / (W * H)))
    if cov == 0:
        fail("没有像素被覆盖")

    # 色带：色相在 z_bot(红 0°) → z_top(紫 300°) 之间均匀分 nb 档
    idx = np.floor((zmap - z_bot) / step)
    idx = np.clip(idx, 0, nb - 1)
    hue = np.where(np.isfinite(zmap), idx / max(nb - 1, 1) * 300.0, 0.0)
    col = np.zeros((H, W, 3), dtype=np.float32)
    for i in range(nb):
        h = (i / max(nb - 1, 1)) * 300.0
        r, g, b = colorsys.hsv_to_rgb(h / 360.0, 0.85, 0.95)
        m = (idx == i) & np.isfinite(zmap)
        col[m] = (r, g, b)
        z0 = z_bot + i * step
        print("   档 %2d  z %.3f~%.3f  hue %5.1f°  RGB(%.2f,%.2f,%.2f)" % (i, z0, z0 + step, h, r, g, b))

    m = np.isfinite(zmap)
    px[m, 0:3] = col[m]
    out_abs = os.path.abspath(out)
    oi = bpy.data.images.new("bands", width=W, height=H, alpha=True)
    oi.pixels.foreach_set(px.reshape(-1))
    oi.filepath_raw = out_abs; oi.file_format = 'PNG'; oi.save()
    print("EXPORTED -> %s" % out_abs)
    print("色带范围 z %.3f(红) → %.3f(紫)，每档 %.0fmm" % (z_bot, z_top, step * 1000))


main()

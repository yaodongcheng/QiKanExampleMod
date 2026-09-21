# gen_flight_sigil_assets.py —— 生成「飞行法阵」的源资产（2026-09-21）
#
# 产出两件（都进中转沙箱 TaikouAnim/AssetSources/，由用户在 ModKit 里导入）：
#   lwn_flight_sigil_d.png   法阵贴图
#   lwn_flight_sigil.fbx     承载法阵的平面网格（无骨架、无物理）
#
# 🔴 为什么贴图是【黑底 RGB】而不是透明 PNG：
#   ModKit 工程源的贴图管线（tools/face-pipeline/scripts/png_for_editor.py）
#   只接受 **8bit RGB、无 alpha** 的 PNG —— 带 alpha 的会被编辑器连源图带产物一起清掉。
#   而黑底 + **加法混合（additive）** 恰好是发光类特效的标准做法：
#   黑 = 加零 = 视觉上等于透明，且亮部会自己发亮。所以这不是妥协，是更对的选择。
#   ⇒ 材质要在 ModKit 里建成 **加法/自发光** 档，别建成不透明档（不透明 = 会看到一块黑方块）。
#
# 🔴 网格是双面（两套反向绕序的面片）：相机转到法阵下方时背面被剔除会看不见，
#   与其指望材质勾双面，不如网格自带两面 —— 便宜且不依赖材质设置。
#
# 用法：
#   python tools/gen_flight_sigil_assets.py --all
#   python tools/gen_flight_sigil_assets.py --png
#   python tools/gen_flight_sigil_assets.py --fbx
#   python tools/gen_flight_sigil_assets.py --all --size 3.0 --outdir <目录>

import argparse
import os
import subprocess
import sys
import tempfile

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
MODULE_ROOT = os.path.dirname(HERE)                       # .../Modules/LivingWorldNpcs
MODULES_DIR = os.path.dirname(MODULE_ROOT)                # .../Modules
DEFAULT_OUTDIR = os.path.join(MODULES_DIR, "TaikouAnim", "AssetSources", "sigil", "lwn_flight_sigil")

BASE_NAME = "lwn_flight_sigil"
DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

# 法阵配色：青白偏冷，中性（不带任何世界观暗示，铁律 3）
COLOR_CORE = (0.72, 0.94, 1.00)     # 主线
COLOR_HOT = (1.00, 1.00, 1.00)      # 高光/中心
COLOR_DIM = (0.28, 0.55, 0.72)      # 次级线


# ────────────────────────────── 贴图 ──────────────────────────────

def make_png(outdir, size_px=1024, supersample=2):
    import numpy as np
    from PIL import Image

    S = size_px * supersample
    c = (S - 1) / 2.0
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    dx = xx - c
    dy = yy - c
    r = np.sqrt(dx * dx + dy * dy)
    ang = np.arctan2(dy, dx)

    canvas = np.zeros((S, S, 3), dtype=np.float32)

    def ring(radius, width, color, intensity=1.0):
        """一圈实心圆环。width = 环宽（像素）。"""
        band = np.abs(r - radius * S / 2.0) <= (width * supersample / 2.0)
        for i in range(3):
            canvas[:, :, i] = np.maximum(canvas[:, :, i], band.astype(np.float32) * color[i] * intensity)

    R = S / 2.0        # 半径基准

    # 三层主环
    ring(0.940, 7, COLOR_CORE, 1.00)
    ring(0.880, 4, COLOR_CORE, 0.85)
    ring(0.600, 3, COLOR_DIM, 0.75)

    # 外环刻度（24 根短线，只在 0.88~0.94 之间）
    ticks = 24
    seg = (ang + np.pi) / (2 * np.pi)          # 0..1
    frac = seg * ticks
    near_tick = np.abs(frac - np.round(frac)) < 0.10
    tick_band = (r > 0.885 * R) & (r < 0.935 * R) & near_tick
    for i in range(3):
        canvas[:, :, i] = np.maximum(canvas[:, :, i], tick_band.astype(np.float32) * COLOR_CORE[i] * 0.9)

    # 符文字块（12 个，落在 0.60~0.88 之间）
    runes = 12
    rfrac = seg * runes
    near_rune = np.abs(rfrac - np.round(rfrac)) < 0.055
    rune_band = (r > 0.640 * R) & (r < 0.840 * R) & near_rune
    for i in range(3):
        canvas[:, :, i] = np.maximum(canvas[:, :, i], rune_band.astype(np.float32) * COLOR_DIM[i] * 1.1)

    # 六芒星（两个反向三角形，顶点在 0.60 环上）—— 线宽随半径做点微调
    def star_line(offset_rad):
        pts = []
        for k in range(3):
            a = offset_rad + k * (2 * np.pi / 3)
            pts.append((np.cos(a), np.sin(a)))
        # 三个顶点两两连线，取点到线段的距离
        dist = np.full((S, S), 1e9, dtype=np.float32)
        for i in range(3):
            x1, y1 = pts[i]
            x2, y2 = pts[(i + 1) % 3]
            vx, vy = x2 - x1, y2 - y1
            wx, wy = dx / R - x1, dy / R - y1
            t = np.clip((wx * vx + wy * vy) / (vx * vx + vy * vy), 0.0, 1.0)
            px, py = x1 + t * vx, y1 + t * vy
            d = np.sqrt((dx / R - px) ** 2 + (dy / R - py) ** 2)
            dist = np.minimum(dist, d)
        return dist < 0.012

    star = star_line(np.pi / 2) | star_line(np.pi / 2 + np.pi / 3)
    for i in range(3):
        canvas[:, :, i] = np.maximum(canvas[:, :, i], star.astype(np.float32) * COLOR_CORE[i] * 0.95)

    # 内环 + 中心光点
    ring(0.230, 3, COLOR_CORE, 0.9)
    core = np.clip(1.0 - r / (0.115 * R), 0.0, 1.0) ** 1.6
    for i in range(3):
        canvas[:, :, i] = np.maximum(canvas[:, :, i], core.astype(np.float32) * COLOR_HOT[i])

    # 柔光：整体做一次高斯模糊再按比例叠加（这就是"发光"的来源）
    from PIL import ImageFilter
    glow_src = Image.fromarray((np.clip(canvas, 0, 1) * 255).astype(np.uint8), "RGB")
    glow = np.asarray(glow_src.filter(ImageFilter.GaussianBlur(radius=10 * supersample)), dtype=np.float32) / 255.0
    canvas = np.clip(canvas + glow * 0.55, 0.0, 1.0)

    # 降到目标分辨率（超采样抗锯齿）
    img = Image.fromarray((canvas * 255).astype(np.uint8), "RGB").resize((size_px, size_px), Image.LANCZOS)

    os.makedirs(outdir, exist_ok=True)
    raw_path = os.path.join(outdir, BASE_NAME + "_d_raw.png")
    out_path = os.path.join(outdir, BASE_NAME + "_d.png")
    img.save(raw_path, format="PNG")

    # 🔴 必须过 png_for_editor（剥附加块 / 保证 8bit RGB）—— 直接丢编辑器会连源图带产物一起被清掉
    png_for_editor = os.path.join(HERE, "face-pipeline", "scripts", "png_for_editor.py")
    if os.path.isfile(png_for_editor):
        subprocess.run([sys.executable, png_for_editor, raw_path, out_path], check=True)
        os.remove(raw_path)
        print(f"[ok] 贴图: {out_path}  (已过 png_for_editor)")
    else:
        img.save(out_path, format="PNG")
        print(f"[!] 贴图: {out_path}  —— 没找到 png_for_editor.py，未做格式清洗，进编辑器前请手动跑一次")

    return out_path


# ────────────────────────────── 网格（Blender）──────────────────────────────

BLENDER_SCRIPT = r'''
import bpy, sys, os

argv = sys.argv[sys.argv.index("--") + 1:]
size = float(argv[0])
out_fbx = argv[1]
name = argv[2]

# 清空默认场景
bpy.ops.wm.read_factory_settings(use_empty=True)

h = size / 2.0
# XY 平面上的正方形，法线 +Z，原点在中心。
# 🔴 双面必须用【两套各自独立的 4 个顶点】：共用顶点 + 反向绕序会被 Blender 的 FBX
#    导出/导入当成同一个面去重掉（实测 verts=4 polys=1，双面失效）。
verts = [
    (-h, -h, 0.0), (h, -h, 0.0), (h, h, 0.0), (-h, h, 0.0),      # 正面（+Z 朝上）
    (-h, -h, 0.0), (h, -h, 0.0), (h, h, 0.0), (-h, h, 0.0),      # 背面（反向绕序，独立顶点）
]
faces = [(0, 1, 2, 3), (7, 6, 5, 4)]
uvs = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)] * 2

me = bpy.data.meshes.new(name)
me.from_pydata(verts, [], faces)
me.update()

uv = me.uv_layers.new(name="UVMap")
li = 0
for poly in me.polygons:
    for i in range(poly.loop_total):
        vi = me.loops[poly.loop_start + i].vertex_index
        uv.data[poly.loop_start + i].uv = uvs[vi]
    li += 1

mat = bpy.data.materials.new(name + "_mat")
mat.use_nodes = True
me.materials.append(mat)

ob = bpy.data.objects.new(name, me)
bpy.context.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)

bpy.ops.export_scene.fbx(
    filepath=out_fbx,
    use_selection=True,
    apply_unit_scale=True,
    global_scale=1.0,
    object_types={"MESH"},
    mesh_smooth_type="OFF",
    add_leaf_bones=False,
    use_mesh_modifiers=False,
    bake_anim=False,
    path_mode="COPY",
)
print("EXPORTED", out_fbx)
'''


def make_fbx(outdir, size_m, blender):
    if not os.path.isfile(blender):
        print(f"[!] 找不到 Blender: {blender} —— 跳过网格生成")
        return None

    os.makedirs(outdir, exist_ok=True)
    out_fbx = os.path.join(outdir, BASE_NAME + ".fbx")

    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False, encoding="utf-8") as f:
        f.write(BLENDER_SCRIPT)
        script = f.name

    try:
        r = subprocess.run(
            [blender, "-b", "--python", script, "--", str(size_m), out_fbx, BASE_NAME],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
        )
        tail = (r.stdout or "").strip().splitlines()[-6:]
        for line in tail:
            print("   ", line)
        if r.returncode != 0 or not os.path.isfile(out_fbx):
            print(f"[x] 网格生成失败 (rc={r.returncode})")
            err = (r.stderr or "").strip().splitlines()[-6:]
            for line in err:
                print("   ", line)
            return None
        print(f"[ok] 网格: {out_fbx}  ({size_m}x{size_m} 米，双面)")
        return out_fbx
    finally:
        try:
            os.remove(script)
        except OSError:
            pass


def main():
    ap = argparse.ArgumentParser(description="生成飞行法阵的源资产（贴图 + 平面网格）")
    ap.add_argument("--all", action="store_true", help="贴图和网格都生成（默认）")
    ap.add_argument("--png", action="store_true", help="只生成贴图")
    ap.add_argument("--fbx", action="store_true", help="只生成网格")
    ap.add_argument("--size", type=float, default=2.6, help="法阵边长（米），默认 2.6")
    ap.add_argument("--px", type=int, default=1024, help="贴图分辨率，默认 1024")
    ap.add_argument("--blender", default=DEFAULT_BLENDER, help="Blender 可执行文件路径")
    ap.add_argument("--outdir", default=DEFAULT_OUTDIR, help="输出目录（默认中转沙箱的 AssetSources）")
    a = ap.parse_args()

    do_png = a.png or a.all or (not a.png and not a.fbx)
    do_fbx = a.fbx or a.all or (not a.png and not a.fbx)

    print(f"输出目录: {a.outdir}")
    if do_png:
        make_png(a.outdir, size_px=a.px)
    if do_fbx:
        make_fbx(a.outdir, a.size, a.blender)

    print()
    print("下一步（ModKit，人工）：")
    print("  1. 打开 TaikouAnim 模块（先跑 to_editor_mode.bat）")
    print("  2. 资源浏览器导入 lwn_flight_sigil.fbx → 静态网格（不勾 Skinning、不要骨架）")
    print("  3. 导入 lwn_flight_sigil_d.png → 贴图")
    print("  4. 建材质：🔴 必须是【加法/自发光】档 —— 建成不透明档会看到一块黑方块")
    print("  5. 编译 + Publish（目标指向模块外），产物改名拷进内容包")


if __name__ == "__main__":
    main()

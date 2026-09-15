# -*- coding: utf-8 -*-
"""build_textures.py — 给甲生成骑砍要的三族贴图（_d / _n / _s），并按甲的 UV 范围裁图集。

背景：源件（战国无双2）只有一张漫反射图集，没有法线/高光。骑砍标准甲材质要三族
（实证自原版 body_male_a 材质：tex[0]=_d / tex[2]=_n / tex[4]=_s）。

做法：
  _d  = 源图集**裁到甲的 UV 范围**并重映射（原来整张带进去，含脸、约 28% 是空的）
  _s  = R 金属度 / G 255−粗糙度 / B 环境光遮蔽
        分区依据 = **UV 区域（哪件网格）+ 贴图颜色**，不是靠猜：
          · 内衬着物  -> 布：非金属、高粗糙
          · 甲片区    -> 按颜色再分：亮金 -> 金属低粗糙；饱和红（漆面札板）-> 半金属低粗糙；
                        白（系威绳）-> 布高粗糙；暗褐（皮）-> 非金属中粗糙；中灰（袴）-> 布高粗糙
        AO 用亮度做代理（画师已经把缝隙/阴影画进 diffuse 了）
  _n  = 平坦法线打底 + 从 diffuse 提的**高通**细节（只加表面质感，不复制已画好的光照）

用法（Blender 无头）:
  blender -b --python build_textures.py -- \\
      --armor   <重定向后的甲.fbx> \\
      --src     <源角色.fbx>          # 取各子网格 UV 做区域标签
      --diffuse <源漫反射.png> \\
      --out     <输出目录> \\
      --name    taikou_yukimura_do_a \\
      [--parts body_kimono] [--pad 0.012] [--nrm 0.30] [--ao 0.6] [--debug]
"""
import bpy
import sys
import os
import math
import numpy as np
from PIL import Image, ImageFilter

# ---------------------------------------------------------------- 参数

def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def get(args, key, default=None):
    return args[args.index(key) + 1] if key in args else default


A = args_after_ddash()
ARMOR = get(A, "--armor")
SRC = get(A, "--src")
DIFF = get(A, "--diffuse")
OUTDIR = get(A, "--out")
NAME = get(A, "--name", "armor")
PARTS = get(A, "--parts", "body_kimono")
PAD = float(get(A, "--pad", "0.012"))       # 裁切时四周留边（UV 单位）
NRM = float(get(A, "--nrm", "0.30"))        # 法线细节强度，0 = 纯平坦法线
AO_STRENGTH = float(get(A, "--ao", "0.6"))  # AO 强度 0~1
DEBUG = "--debug" in A
if not (ARMOR and SRC and DIFF and OUTDIR):
    print("!! 缺 --armor / --src / --diffuse / --out"); sys.exit(2)
os.makedirs(OUTDIR, exist_ok=True)

# 🔴 防呆：输出 _d 的路径绝不能和输入是同一个文件。
#    踩过：第一次跑失败后重跑，源图集已被上一轮裁切结果覆盖成 512x512，静默出错图。
_d_out = os.path.abspath(os.path.join(OUTDIR, (get(A, "--name", "armor")) + "_d.png"))
if os.path.abspath(DIFF) == _d_out:
    print("!! --diffuse 不能指向输出文件 %s" % _d_out)
    print("   源图集请指原始的漫反射（本例：D:/BrainMaker/.../web/textures/L00_yukimura.png）")
    sys.exit(2)

PART_SETS = {
    "body":        [2, 7, 8, 9],
    "body_kimono": [1, 2, 7, 8, 9],
}
# 🔴 通用化（2026-09-15）：预设号是**幸村的**，别人各不相同。按号直选（与 build_armor.py 同一套参数）：
#    `--parts-idx 1,2,3,4,7,8,9` + `--kimono-idx 1`（哪几件算"布"，金属度/粗糙度按布给）
_idx = (get(A, "--parts-idx", "") or "").strip()
WANT = [int(x) for x in _idx.split(",") if x.strip().lstrip("-").isdigit()] or \
       PART_SETS.get(PARTS, PART_SETS["body_kimono"])
# 🔴 `--parts-name`（2026-09-15 深夜）：按【精确对象名】选件，`|` 分隔 —— 与 build_armor.py 同一套。
#    为什么：`submesh_0` 与 `submesh_0.001` 按号解析是同一个号，选件会多带一块（谦信的兜就是这情况）。
WANT_NAMES = [x.strip() for x in (get(A, "--parts-name", "") or "").split("|") if x.strip()]
KIMONO = [int(x) for x in (get(A, "--kimono-idx", "1") or "1").split(",") if x.strip().isdigit()]
LBL_KIMONO, LBL_ARMOR = 1, 2


def parse_submesh(name):
    i = name.find("submesh_")
    if i < 0:
        return None
    j = i + len("submesh_"); k = j
    while k < len(name) and name[k].isdigit():
        k += 1
    return int(name[j:k]) if k > j else None


def want_piece(o):
    """这块源件是不是"我们要的件"。有 --parts-name 时按名字，否则按号。

    🔴 按名字时**不再叠 is_junk**（2026-09-15 深夜）：点名 = 人已确认，优先于启发式。
       实测上杉谦信的兜件材质名带 `mat_w_`，被 is_junk 排掉过。
    """
    if WANT_NAMES:
        return o.name in WANT_NAMES
    return parse_submesh(o.name) in WANT


def is_junk(o):
    if not o.data.uv_layers:
        return True
    return any(m and m.name.lower().startswith('mat_w_') for m in o.data.materials)


def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * (c ** (1 / 2.4)) - 0.055)


# ---------------------------------------------------------------- 1. 载入甲，算 UV 范围
print("== 1/6 读甲的 UV 范围 ==")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=ARMOR)
lod0 = next((o for o in bpy.data.objects if o.name == NAME), None)
if lod0 is None:
    cands = [o for o in bpy.data.objects if o.type == 'MESH' and '.lod' not in o.name]
    lod0 = max(cands, key=lambda o: len(o.data.vertices))
print("   LOD0 =", lod0.name, "v=", len(lod0.data.vertices))
uvl = lod0.data.uv_layers[0].data
us = np.array([d.uv[0] for d in uvl], dtype=np.float64)
vs = np.array([d.uv[1] for d in uvl], dtype=np.float64)
u0, u1, v0, v1 = us.min(), us.max(), vs.min(), vs.max()
print("   甲 UV 范围 u[%.4f,%.4f] v[%.4f,%.4f]" % (u0, u1, v0, v1))

# ---------------------------------------------------------------- 1b. 交叉校验：甲是不是已经被重映射过
# 🔴 本脚本不幂等（会把 UV 再映射一次）。判据：拿**源件**的 UV 当基准 —— 源件永远是干净的。
#    踩过：把上一轮的输出当输入重跑，UV 被映射两次，范围从 65% 涨到 96%，静默出错图。
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
su, sv = [], []
for o in bpy.data.objects:
    if o.type != 'MESH' or not want_piece(o) or (not WANT_NAMES and is_junk(o)):
        continue
    for d in o.data.uv_layers[0].data:
        su.append(d.uv[0]); sv.append(d.uv[1])
if not su:
    print("!! 源件里没找到任何选中的子网格，检查 --parts"); sys.exit(3)
su0, su1, sv0, sv1 = min(su), max(su), min(sv), max(sv)
print("   源件 UV 范围 u[%.4f,%.4f] v[%.4f,%.4f]（基准）" % (su0, su1, sv0, sv1))
# 判据 = **包含关系**，不是相等：甲会丢掉一部分源件顶点（如着物的头/手），范围本来就该更窄。
# 但若甲的 UV 跑到了源件范围之外，就说明它已经被重映射过。
TOL = 0.02
if u0 < su0 - TOL or u1 > su1 + TOL or v0 < sv0 - TOL or v1 > sv1 + TOL:
    print("!! 甲的 UV 超出了源件范围 —— 这份 FBX 很可能**已经被本脚本重映射过**。")
    print("   请先用 build_armor.py 重出一份干净 FBX，再跑本脚本。")
    print("   （甲 u[%.4f,%.4f] v[%.4f,%.4f]  vs  源 u[%.4f,%.4f] v[%.4f,%.4f]）" % (
        u0, u1, v0, v1, su0, su1, sv0, sv1))
    sys.exit(3)

src_img = Image.open(DIFF).convert("RGBA")
SW_, SH_ = src_img.size
print("   源图集 %dx%d" % (SW_, SH_))

# 留边 + 对齐像素
cu0 = max(0.0, u0 - PAD); cu1 = min(1.0, u1 + PAD)
cv0 = max(0.0, v0 - PAD); cv1 = min(1.0, v1 + PAD)
x0 = int(math.floor(cu0 * SW_)); x1 = int(math.ceil(cu1 * SW_))
# 图集行 0 在**上**（PNG），UV 的 v 从**下**起 —— 要翻
y0 = int(math.floor((1.0 - cv1) * SH_)); y1 = int(math.ceil((1.0 - cv0) * SH_))
crop_w, crop_h = x1 - x0, y1 - y0
print("   裁切矩形 px  x[%d,%d) y[%d,%d)  ->  %dx%d  (省 %.0f%%)" % (
    x0, x1, y0, y1, crop_w, crop_h,
    100 * (1 - crop_w * crop_h / (SW_ * SH_))))


def pot_le(n):
    p = 1
    while p * 2 <= n:
        p *= 2
    return p


if "--pot" in A:
    # 2 的幂：取不大于裁切尺寸的那个（不上采样）。代价是丢掉一部分分辨率，见下面告警。
    TW, TH = pot_le(crop_w), pot_le(crop_h)
else:
    # 默认：保持裁切原尺寸，只对齐到 4 的倍数（BC3 压缩要求）。
    # 🔴 别默认取 2 的幂：本例 512x696 取整到 512x512 会**白丢 26% 纵向分辨率**，
    #    而省下的显存和直接用 512x696 一样。
    TW, TH = (crop_w // 4) * 4, (crop_h // 4) * 4
    if TW < 4 or TH < 4:
        TW, TH = crop_w, crop_h
print("   输出尺寸 -> %dx%d%s" % (TW, TH, "（--pot）" if "--pot" in A else "（保持裁切尺寸，对齐 4）"))

# UV 重映射：源 UV -> 裁切后 UV
mu, mv = cu1 - cu0, cv1 - cv0
print("   UV 重映射  u'=(u-%.4f)/%.4f   v'=(v-%.4f)/%.4f" % (cu0, mu, cv0, mv))


# ---------------------------------------------------------------- 2. 裁 diffuse
print("== 2/6 裁 _d ==")
d_crop = src_img.crop((x0, y0, x1, y1))
if (TW, TH) != d_crop.size:
    d_crop = d_crop.resize((TW, TH), Image.LANCZOS)
d_path = os.path.join(OUTDIR, NAME + "_d.png")
d_crop.convert("RGB").save(d_path)
print("   ->", d_path)

# 后续算法在 sRGB 数值上做（PIL 给的就是 sRGB 数值）
d_rgb = np.asarray(d_crop.convert("RGB"), dtype=np.float32) / 255.0
H, W = d_rgb.shape[:2]


# ---------------------------------------------------------------- 3. 区域标签（UV 光栅化）
# 场景里已经是源件（1b 导入的），直接用，不重复导入
print("== 3/6 光栅化区域标签（哪件网格盖住哪个图素）==")
label = np.zeros((H, W), dtype=np.uint8)


def rasterize(objs, lbl):
    """把 objs 的三角形按 UV 填进 label（裁切后的 UV 空间）"""
    n_fill = 0
    for o in objs:
        me = o.data
        uv = me.uv_layers[0].data
        # 顶点 -> 裁切后 UV 的像素坐标（v 从下起；图像行 0 在上 -> 翻转）
        px = np.empty((len(me.vertices), 2), dtype=np.float64)
        # 逐 loop 取更准，这里按 loop 建三角形
        loop_uv = np.array([[uv[li].uv[0], uv[li].uv[1]] for li in range(len(uv))],
                           dtype=np.float64)
        for poly in me.polygons:
            li = list(poly.loop_indices)
            if len(li) < 3:
                continue
            for t in range(1, len(li) - 1):
                tri = [li[0], li[t], li[t + 1]]
                pt = []
                for k in tri:
                    u = (loop_uv[k][0] - cu0) / mu
                    v = (loop_uv[k][1] - cv0) / mv
                    pt.append((u * (W - 1), (1.0 - v) * (H - 1)))
                p = np.array(pt)
                mnx = max(0, int(math.floor(p[:, 0].min())))
                mxx = min(W - 1, int(math.ceil(p[:, 0].max())))
                mny = max(0, int(math.floor(p[:, 1].min())))
                mxy = min(H - 1, int(math.ceil(p[:, 1].max())))
                if mxx < mnx or mxy < mny:
                    continue
                gx, gy = np.meshgrid(np.arange(mnx, mxx + 1), np.arange(mny, mxy + 1))
                # 重心坐标
                x1_, y1_ = p[0]; x2_, y2_ = p[1]; x3_, y3_ = p[2]
                den = (y2_ - y3_) * (x1_ - x3_) + (x3_ - x2_) * (y1_ - y3_)
                if abs(den) < 1e-12:
                    continue
                a = ((y2_ - y3_) * (gx - x3_) + (x3_ - x2_) * (gy - y3_)) / den
                b = ((y3_ - y1_) * (gx - x3_) + (x1_ - x3_) * (gy - y3_)) / den
                c = 1.0 - a - b
                m = (a >= -0.002) & (b >= -0.002) & (c >= -0.002)
                label[gy[m], gx[m]] = lbl
                n_fill += m.sum()
    return n_fill


km = [o for o in bpy.data.objects
      if o.type == 'MESH' and not WANT_NAMES
      and parse_submesh(o.name) in KIMONO and not is_junk(o)]
ar = [o for o in bpy.data.objects
      if o.type == 'MESH' and want_piece(o)
      and (WANT_NAMES or parse_submesh(o.name) not in KIMONO)
      and (WANT_NAMES or not is_junk(o))]
n1 = rasterize(km, LBL_KIMONO)
n2 = rasterize(ar, LBL_ARMOR)
print("   着物 %d 像素 / 甲 %d 像素 / 空白 %d 像素" % (
    n1, n2, W * H - (label > 0).sum()))
if DEBUG:
    Image.fromarray((label * 80).astype(np.uint8)).save(os.path.join(OUTDIR, "_dbg_label.png"))


# ---------------------------------------------------------------- 4. 生成 _s
print("== 4/6 生成 _s（金属度/粗糙度/AO）==")
R_, G_, B_ = d_rgb[:, :, 0], d_rgb[:, :, 1], d_rgb[:, :, 2]
V_ = d_rgb.max(axis=2)
MN_ = d_rgb.min(axis=2)
S_ = np.where(V_ > 1e-6, (V_ - MN_) / np.maximum(V_, 1e-6), 0.0)

metal = np.zeros((H, W), dtype=np.float32)
rough = np.full((H, W), 0.85, dtype=np.float32)

# ① 亮金（金具）：高值 + 黄调（R>G>B 且 B 明显低）
gold = (V_ > 0.45) & (R_ > G_ * 1.05) & (G_ > B_ * 1.25) & (S_ > 0.28)
# ② 饱和红（漆面札板）：红主导、饱和度中高
lacquer = (R_ > G_ * 1.35) & (R_ > B_ * 1.35) & (S_ > 0.30) & ~gold
# ③ 白/浅（系威绳、白帯）
pale = (V_ > 0.62) & (S_ < 0.22)
# ④ 暗褐（皮、缘）
leather = (V_ < 0.42) & (S_ > 0.12)
# ⑤ 中灰/其它（袴、布）

metal[gold] = 0.90
rough[gold] = 0.32
metal[lacquer] = 0.35          # 漆面铁札：半金属、低粗糙（漆是光泽面）
rough[lacquer] = 0.38
metal[pale] = 0.00
rough[pale] = 0.88
metal[leather] = 0.00
rough[leather] = 0.68
other = ~(gold | lacquer | pale | leather)
metal[other] = 0.00
rough[other] = 0.88

# 内衬着物整片按布处理（区域标签优先于颜色）
kim = (label == LBL_KIMONO)
metal[kim] = 0.0
rough[kim] = 0.90

# 空白区（没有 UV 盖到）给中性值，免得采样到边界时冒出金属
empty = (label == 0)
metal[empty] = 0.0
rough[empty] = 0.85

# AO：用亮度做代理，但**按高分位归一化**（diffuse 本来就画了阴影，不能直接拿绝对值，
# 否则整张甲都被压暗）。分位取 90%，让大面积受光区 AO≈1。
lum = 0.299 * R_ + 0.587 * G_ + 0.114 * B_
ref = float(np.percentile(lum[label > 0], 90)) if (label > 0).any() else float(lum.max())
ref = max(ref, 1e-6)
lum_n = np.clip(lum / ref, 0.0, 1.0)
ao = 1.0 - AO_STRENGTH * (1.0 - lum_n) ** 0.8
ao = np.clip(ao, 0.55, 1.0)

# 平滑一下，避免色块边界出硬边
def smooth(a, r=2):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    im = im.filter(ImageFilter.GaussianBlur(r))
    return np.asarray(im, dtype=np.float32) / 255.0

metal = smooth(metal, 1.5)
rough = smooth(rough, 1.5)

s_rgb = np.stack([metal, 1.0 - rough, ao], axis=2)
s_img = Image.fromarray((np.clip(s_rgb, 0, 1) * 255 + 0.5).astype(np.uint8))
s_path = os.path.join(OUTDIR, NAME + "_s.png")
s_img.save(s_path)
print("   金属 %5.1f%% / 半金属（漆）%5.1f%% / 非金属 %5.1f%%" % (
    100 * (metal > 0.7).mean(), 100 * ((metal > 0.1) & (metal <= 0.7)).mean(),
    100 * (metal <= 0.1).mean()))
print("   粗糙度 均值 %.2f   AO 均值 %.2f" % (rough.mean(), ao.mean()))
print("   ->", s_path)
if DEBUG:
    Image.fromarray((np.stack([metal, 1 - rough, ao], 2) * 255).astype(np.uint8)).save(
        os.path.join(OUTDIR, "_dbg_s.png"))


# ---------------------------------------------------------------- 5. 生成 _n
print("== 5/6 生成 _n（平坦法线 + 高通细节 %.2f）==" % NRM)
if NRM <= 1e-6:
    n_rgb = np.zeros((H, W, 3), dtype=np.float32)
    n_rgb[:, :, 2] = 1.0
else:
    # 亮度高通 -> 高度场。只取细节，不复制已画好的光照
    lum_img = Image.fromarray((np.clip(lum, 0, 1) * 255).astype(np.uint8))
    blur = np.asarray(lum_img.filter(ImageFilter.GaussianBlur(3.0)), dtype=np.float32) / 255.0
    hgt = (lum - blur) * NRM
    # Sobel
    kx = np.array([[-1, 0, 1], [-2, 0, 2], [-1, 0, 1]], dtype=np.float32) / 8.0
    ky = kx.T
    def conv(a, k):
        p = np.pad(a, 1, mode='edge')
        out = np.zeros_like(a)
        for i in range(3):
            for j in range(3):
                out += k[i, j] * p[i:i + a.shape[0], j:j + a.shape[1]]
        return out
    dx, dy = conv(hgt, kx), conv(hgt, ky)
    nz = np.ones_like(dx)
    ln = np.sqrt(dx * dx + dy * dy + nz * nz)
    n_rgb = np.stack([-dx / ln, -dy / ln, nz / ln], axis=2)
n_img = Image.fromarray((np.clip(n_rgb * 0.5 + 0.5, 0, 1) * 255 + 0.5).astype(np.uint8))
n_path = os.path.join(OUTDIR, NAME + "_n.png")
n_img.save(n_path)
print("   ->", n_path)


# ---------------------------------------------------------------- 6. 重映射 UV 并导出
print("== 6/6 重映射 UV + 导出 FBX ==")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=ARMOR)
n_uv = 0
for o in [x for x in bpy.data.objects if x.type == 'MESH']:
    if not o.data.uv_layers:
        continue
    for d in o.data.uv_layers[0].data:
        d.uv[0] = (d.uv[0] - cu0) / mu
        d.uv[1] = (d.uv[1] - cv0) / mv
    n_uv += 1
print("   重映射了 %d 个网格的 UV" % n_uv)
# 边界抽样检查
chk = []
for o in [x for x in bpy.data.objects if x.type == 'MESH' and '.lod' not in x.name]:
    for d in o.data.uv_layers[0].data:
        chk.append((d.uv[0], d.uv[1]))
chk = np.array(chk)
print("   重映射后 UV 范围 u[%.4f,%.4f] v[%.4f,%.4f]（应落在 0~1 内）" % (
    chk[:, 0].min(), chk[:, 0].max(), chk[:, 1].min(), chk[:, 1].max()))
if chk.min() < -0.001 or chk.max() > 1.001:
    print("   !! UV 越界，检查裁切矩形")

bpy.context.scene.unit_settings.scale_length = 1.0
fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
kw = dict(filepath=fbx_path, use_selection=False, object_types={'ARMATURE', 'MESH'},
          global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
          bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
          primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='Y', axis_up='Z',
          bake_anim=False, path_mode='COPY', embed_textures=False, use_custom_props=False)
try:
    bpy.ops.export_scene.fbx(**kw)
except TypeError as e:
    print("   fbx kwarg 问题:", e); kw.pop('apply_scale_options', None)
    bpy.ops.export_scene.fbx(**kw)
print("   导出 FBX ->", fbx_path)
print("DONE")

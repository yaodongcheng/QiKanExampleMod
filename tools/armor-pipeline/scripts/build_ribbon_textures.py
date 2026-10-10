# -*- coding: utf-8 -*-
"""build_ribbon_textures.py —— 给「混天绫」生成装备三件套贴图（`_d` / `_n` / `_s`）。

【为什么必须有】装备件的贴图是**三件套**，缺一件观感就不对（这是全工程的核对表条目）。
   口径**逐条抄 `build_textures.py`**（那份是甲用的，已验证）：
     · `tex[0]` = `_d` 漫反射 · `tex[2]` = `_n` 法线 · `tex[4]` = `_s` 金属/粗糙/AO
     · 🔴 `_s` 通道约定 = **R 金属度 / G `255−粗糙度` / B 环境光遮蔽**
     · `_n` 编码 = `n*0.5+0.5`，green 取 **−dy**（= 工程一直用的那套朝向）

【贴图怎么铺】丝带的 UV：`u` = 横向 0~1（**不平铺**）、`v` = 沿长度**平铺 6 次**（每格 0.5 m）。
   ⇒ **u 方向的细节不会重复**（可以放边缘压暗、横向渐变），
      **v 方向会每 0.5 m 重复一次** ⇒ 那边**只放高频**（织纹/噪点），大尺度明暗必须压得很轻。

【产物】`<名>_d.png` / `<名>_n.png` / `<名>_s.png`，与 FBX 同目录。
   🔴 生成后**必须过 `png_for_editor.py`**：Blender/PIL 直出的 PNG 带 sRGB/gAMA 等附加块，
      编辑器读到会**把源图和编译产物一起删掉**（工程实测踩过）。本脚本自动调用它。

跑法（系统 python，不需要 Blender）：
  python tools/armor-pipeline/scripts/build_ribbon_textures.py [--out <目录>] [--size 512] [--red 198,30,30]
"""
import argparse
import math
import os
import subprocess
import sys

import numpy as np
from PIL import Image

# 控制台默认 GBK，中文/全角符号会 UnicodeEncodeError —— 与 png_for_editor.py 同款处理
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEFAULT_OUT = os.path.join(os.path.abspath(os.path.join(REPO, "..")),
                           "LwnAnim", "AssetSources", "ImortReady", "HuntianLing")
PNG_FOR_EDITOR = os.path.join(REPO, "tools", "face-pipeline", "scripts", "png_for_editor.py")

# 丝带实物尺寸（与 build_ribbon.py 的默认值一致）—— 用来把"每米多少像素"算准
WIDTH_M = 0.30          # 横向
TILE_M = 0.50           # v 方向一格覆盖多长（= 3.0 m / 平铺 6 次）


def parse_args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", default="lwn_huntian_ling")
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--size", type=int, default=512, help="贴图边长（正方形，会平铺）")
    ap.add_argument("--red", default="198,30,30", help="底色 sRGB，逗号分隔")
    ap.add_argument("--thread-mm", type=float, default=2.6, help="织纹的丝线粗细（毫米）")
    ap.add_argument("--weave", type=float, default=0.20, help="织纹明暗强度（0.32=麻布感，0.20=绸）")
    ap.add_argument("--rough", type=float, default=0.34, help="基础粗糙度（丝绸偏滑）")
    ap.add_argument("--ao", type=float, default=0.35, help="AO 强度")
    ap.add_argument("--nrm", type=float, default=0.9, help="法线细节强度")
    ap.add_argument("--no-pngfix", action="store_true", help="跳过后处理（默认会跑 png_for_editor）")
    return ap.parse_args()


A = parse_args()
S = A.size
NAME = A.name
OUT = A.out
BASE = np.array([float(x) for x in A.red.split(",")], dtype=np.float32) / 255.0

os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- 坐标栅格
# u：横向（不平铺）· v：沿长度（平铺）—— 都用像素中心，避免 0/1 端点退化
u = (np.arange(S, dtype=np.float32) + 0.5) / S          # 0..1 across the width
v = (np.arange(S, dtype=np.float32) + 0.5) / S          # 0..1 within one tile
U, V = np.meshgrid(u, v, indexing="xy")                 # 形状 (S, S)：行 = v，列 = u
PX_PER_M_U = S / WIDTH_M                                # 横向 像素/米
PX_PER_M_V = S / TILE_M                                 # 纵向 像素/米

# 让「丝线粗细」在两个方向都是真实尺寸：横向 N_u 根、纵向 N_v 根
N_U = max(4, int(round(WIDTH_M / (A.thread_mm / 1000.0))))
N_V = max(4, int(round(TILE_M / (A.thread_mm / 1000.0))))
print("== 混天绫贴图 %s ==" % NAME)
print("   尺寸 %dx%d   横向 %.0f px/m · 纵向 %.0f px/m   丝线 %.1f mm => %d x %d 根/格"
      % (S, S, PX_PER_M_U, PX_PER_M_V, A.thread_mm, N_U, N_V))


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / max(1e-6, (e1 - e0)), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def blur(a, radius):
    """可分离盒式模糊（重复 3 次近似高斯）—— 不引 scipy。

    🔴 **纵向（v）用环绕填充**：贴图要沿丝带长度平铺 6 次，纵向接缝必须无缝。
       第一版用 edge 填充 ⇒ 首末行采不到对方，`_s` 上留下 1.23/255 的台阶
       （肉眼看不见，但那是真接缝，顺手修干净）。横向（u）不平铺，edge 填充才对。
    """
    k = max(1, int(radius))
    out = a.astype(np.float32).copy()
    for _ in range(3):
        c = np.cumsum(np.pad(out, ((k, k), (0, 0)), mode="wrap"), axis=0)
        out = (c[2 * k:, :] - c[:-2 * k, :]) / (2 * k)
        c = np.cumsum(np.pad(out, ((0, 0), (k, k)), mode="edge"), axis=1)
        out = (c[:, 2 * k:] - c[:, :-2 * k]) / (2 * k)
    return out


def tile_noise(shape, cells, seed, octaves=4):
    """可平铺的多倍频值噪声（v 方向要平铺，u 方向不用，但两边都平铺也无妨）。"""
    rng = np.random.default_rng(seed)
    acc = np.zeros(shape, dtype=np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        g = rng.random((n, n)).astype(np.float32)
        g = np.pad(g, ((0, 1), (0, 1)), mode="wrap")          # 环绕 ⇒ 平铺无缝
        # 双线性放大到全尺寸
        yi = np.linspace(0, n, shape[0], endpoint=False, dtype=np.float32)
        xi = np.linspace(0, n, shape[1], endpoint=False, dtype=np.float32)
        y0 = np.floor(yi).astype(int); x0 = np.floor(xi).astype(int)
        fy = (yi - y0)[:, None]; fx = (xi - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x0 + 1)]
        c = g[np.ix_(y0 + 1, x0)]; d = g[np.ix_(y0 + 1, x0 + 1)]
        acc += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        tot += amp
        amp *= 0.5
    return acc / tot


# ---------------------------------------------------------------- 1) 织纹高度场（后面 _d 和 _n 共用）
# 丝织：经线（沿长度）压着纬线（沿横向）交替起伏
warp = np.sin(2.0 * math.pi * N_U * U)                 # 横向排列的经线
weft = np.sin(2.0 * math.pi * N_V * V)                 # 纵向排列的纬线
over = np.sin(2.0 * math.pi * (N_V * V + N_U * U) * 0.5)   # 平纹的上下交叠
weave_h = 0.55 * np.abs(np.cos(math.pi * (N_V * V + N_U * U) * 0.5))
weave_h = 0.5 * weave_h + 0.25 * (0.5 + 0.5 * warp) + 0.25 * (0.5 + 0.5 * weft)
fibre = tile_noise((S, S), 24, 11, octaves=5)          # 丝纤维的细噪
fibre2 = tile_noise((S, S), 96, 12, octaves=3)         # 更细的一层
# 🔴 绸 vs 麻：织纹压轻、纤维噪加重。第一版 0.32 的织纹明暗读起来像麻袋布。
height = weave_h * 0.45 + fibre * 0.42 + fibre2 * 0.13
height = (height - height.min()) / max(1e-6, float(np.ptp(height)))

# ---------------------------------------------------------------- 2) _d 漫反射
print("== 1/3 _d 漫反射 ==")
shade = (1.0 - A.weave) + 2.0 * A.weave * height         # 织纹的明暗（强度可调）
# 横向：两端收边压暗（u 不平铺 ⇒ 不会重复，这是唯一能放大尺度的地方）
edge = smoothstep(0.0, 0.055, U) * smoothstep(0.0, 0.055, 1.0 - U)
shade *= 0.62 + 0.38 * edge
# 横向还有一点点"中间亮"的鼓形，像布微微卷起
shade *= 0.96 + 0.06 * np.sin(math.pi * U)
# 纵向：只放极轻的大尺度起伏（会每 0.5 m 重复一次，压到 ±4% 以内看不见）
shade *= 1.0 + 0.04 * (tile_noise((S, S), 2, 21, octaves=2) - 0.5) * 2.0
# 丝绸的顺纹高光带（沿长度方向的窄亮条，属于"高光"但烘一点进底色更像绸）
sheen = 0.5 + 0.5 * np.cos(2.0 * math.pi * (3.0 * U + 0.12 * np.sin(2 * math.pi * V)))
shade *= 1.0 + 0.055 * sheen * (0.4 + 0.6 * edge)

shade = np.clip(shade, 0.35, 1.45)
d_rgb = np.clip(BASE[None, None, :] * shade[:, :, None], 0.0, 1.0)
d_img = Image.fromarray((d_rgb * 255 + 0.5).astype(np.uint8))
d_path = os.path.join(OUT, NAME + "_d.png")
d_img.save(d_path)
lum = d_rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

# ---------------------------------------------------------------- 3) _s 金属/粗糙/AO
print("== 2/3 _s（R 金属度 / G 255−粗糙度 / B 环境光遮蔽）==")
metal = np.zeros((S, S), dtype=np.float32)                            # 丝绸 = 绝缘体
rough = np.full((S, S), A.rough, dtype=np.float32)
rough += 0.10 * (1.0 - height)                                        # 织纹的谷更糙
rough += 0.05 * (1.0 - edge)                                          # 收边略糙
rough = np.clip(blur(rough, max(1, S // 220)), 0.06, 0.98)
lum_n = np.clip(lum / max(1e-6, float(np.percentile(lum, 97))), 0.0, 1.0)
ao = 1.0 - A.ao * (1.0 - (0.55 + 0.45 * lum_n)) ** 0.9                # 谷里略暗
ao *= 0.80 + 0.20 * edge                                              # 收边自遮挡
ao = np.clip(blur(ao, max(1, S // 160)), 0.45, 1.0)
s_rgb = np.stack([metal, 1.0 - rough, ao], axis=2)
s_path = os.path.join(OUT, NAME + "_s.png")
Image.fromarray((np.clip(s_rgb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(s_path)
print("   粗糙度 均值 %.2f（G 通道 %.0f）  AO 均值 %.2f"
      % (rough.mean(), (1 - rough).mean() * 255, ao.mean()))

# ---------------------------------------------------------------- 4) _n 法线
print("== 3/3 _n 法线 ==")
# 高度场：织纹为主，叠细噪。**只取高频**（不复制已画好的光照），与 build_textures.py 同口径
hgt = (height - 0.5) * A.nrm
hgt += (fibre2 - 0.5) * A.nrm * 0.35
kx = np.array([[-1, 0, 1], [-2, 0, 2], [-1, 0, 1]], dtype=np.float32) / 8.0
ky = kx.T


def conv(a, k):
    p = np.pad(a, 1, mode="wrap")                        # 环绕 ⇒ 平铺无缝
    out = np.zeros_like(a)
    for i in range(3):
        for j in range(3):
            out += k[i, j] * p[i:i + a.shape[0], j:j + a.shape[1]]
    return out


dx, dy = conv(hgt, kx), conv(hgt, ky)
nz = np.ones_like(dx)
ln = np.sqrt(dx * dx + dy * dy + nz * nz)
n_rgb = np.stack([-dx / ln, -dy / ln, nz / ln], axis=2)   # 🔴 green 取 −dy（工程定式）
n_path = os.path.join(OUT, NAME + "_n.png")
Image.fromarray((np.clip(n_rgb * 0.5 + 0.5, 0, 1) * 255 + 0.5).astype(np.uint8)).save(n_path)

# ---------------------------------------------------------------- 5) 过 png_for_editor
paths = [d_path, n_path, s_path]
print("\n== 后处理：png_for_editor（去掉附加块，否则编辑器会把源图和产物一起删）==")
if A.no_pngfix:
    print("   --no-pngfix：跳过")
else:
    for p in paths:
        r = subprocess.run([sys.executable, PNG_FOR_EDITOR, p], capture_output=True, text=True)
        tail = (r.stdout or "").strip().splitlines()
        print("   %-28s rc=%d  %s" % (os.path.basename(p), r.returncode, tail[-1] if tail else ""))
        if r.returncode != 0:
            print("   !! 后处理失败：%s" % (r.stderr or "")[:300])
            sys.exit(3)

# ---------------------------------------------------------------- 摘要
print("\n== 摘要 ==")
for p in paths:
    im = Image.open(p)
    d = open(p, "rb").read()
    chunks, i = [], 8
    while i < len(d) - 8:
        n = int.from_bytes(d[i:i + 4], "big")
        nm = d[i + 4:i + 8].decode("latin1")
        chunks.append(nm)
        i += 12 + n
        if nm == "IEND":
            break
    print("   %-30s %s %s  %d B  块=%s"
          % (os.path.basename(p), im.mode, im.size, len(d), ",".join(chunks)))

print("""
下一步（**人做**，在 ModKit 里）：
  1. 这三张 PNG 与 FBX 同目录，Import 网格时一并进工程
  2. Material Editor 挂三个槽：**tex[0] = _d · tex[2] = _n · tex[4] = _s**
     · `_n` 的纹理类型必须选 **Normal Map**（不选 = 法线被当颜色读）
     · 材质记得勾 **Bumpmap** 与 **Skinning**（漏 Skinning = 不跟骨架动）
  3. 若高光方向看着是反的 ⇒ 法线 green 通道要翻转（一行改动，告诉我）
""")

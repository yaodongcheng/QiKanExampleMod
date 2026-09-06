# -*- coding: utf-8 -*-
"""japanmap_hires.png -> 16bit 灰度高度图 + RGBA 材质图 + 分层 mask（战役地形管线，链路见 Knowledge/骑砍2战役地形制作管线.md）

🔴 v2（2026-09-06）：高度细节达到 NativeExample（卡拉迪亚原版高度图）水准——
   宏观（山块明暗经过 σ200 大平滑再分段，画法斑点≠海拔）+ 结构感知排水网（多倍频 fbm
   整域山脊变换 + 源图笔触方向场 + 半分辨率软烤）；校准实测：band(6-40px) std 0.0345
   vs Native 0.035，形态连通排水网、宽亮丘+窄深沟。原型迭代见 Output/_probe/（v6.x 系列）。

v1（2026-09-04/05 分层模型，历史语义保留在注释里）:
  | 元素        | 判据（源图色域采样）                    | 高度            |
  |-------------|-----------------------------------------|-----------------|
  | 海（深浅湾） | blueish & lum≤165                       | 0（统一）        |
  | 湖          | 陆上大块蓝                               | 0（=海）         |
  | 航路/浪花   | 海上低饱和亮色 + 开运算线状/点状         | 归海 → 0        |
  | 滩/岩墙/雪  | 粗白 & 距海 d 连续映射（灰白/淡蓝白/淡绿白）| 0.05+d/1200（贴海0.05→岩0.14→雪0.55+雪脊差分，上限0.95）|
  | 平原        | 绿区亮度分段低位（亮黄绿→低 0.16-0.22，Koei 画法亮度与高差反向）| ~0.2 |
  | 山地        | 绿区亮度分段高位 + 山影高斯差分(亮度门控增益) | 0.25~0.5+细节  |
  | 富士蓝白雪  | 蓝判+最大白点簇 → 雪帽；PEAKS 地标锥体豁免海清零 | 0.6-0.88 |
  | 道路/河线   | 黄色判/细蓝判 + 开运算线状              | 中值替换底色（不抬山不刻槽）|
  | 雪(材质图)  | 白系 & 离海>400px & 高度hm≥0.42         | 与高度图交叉（防海岸白带误判）|

  ⚠️ 关键裁定（用户亲报 2026-09-05）：富士山 == 源图坐标 (10500,7000)（山顶白点格线交点）。
  蓝白雪≠水：富士雪「蓝白+中心白点+绿地包围」曾三次被当湖泊判 0 → 黑斑/蓝湖 bug；
  修复=雪帽检测（最大白点簇）+ PEAKS 地标区豁免海清零（高度图、材质图两边必须同源，禁止分叉）。

用法（可复现）:
  python tools/make_heightmap.py    # 主档 4096x2560：hm_*_16bit.png + mat_*.png + mask_*_L? 分层 + 预览
  python tools/make_heightmap.py 2048 1280   # 任意档
"""
import os
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None

# 目录布局：脚本在 tools/ExportHeightMatMap/，素材默认 ./SourceMap/，产物默认 ./Output/
# 可覆盖：python make_heightmap.py [w] [h] [src_png] [out_dir]
BASE = os.path.dirname(os.path.abspath(__file__))
REFS = BASE
SRC = os.path.join(BASE, "SourceMap", "japanmap_hires.png")
OUTD = os.path.join(BASE, "Output")
os.makedirs(OUTD, exist_ok=True)
if not os.path.exists(SRC):
    print(f"[hint] 默认素材不在 {SRC}——若用别的素材图，按用法传参；找不到默认图将报错退出")
SRC_W = 0
SRC_H = 0                         # 运行时从图读（图会更新，尺寸可能变）
ST = "map"                        # 源图名（输出文件名前缀，main 中按素材覆盖）


def _set_src(im):
    global SRC_W, SRC_H
    SRC_W, SRC_H = im.size
TARGET_RATIO = 2048 / 1280.0          # 1.6:1 → 世界 2048x1280m
MANUAL_CROP = None                    # 例 (1366, 0, 15840, 9053)；None=自动

LUM_SEA_MAX = 165.0                   # 海面深蓝 lum≈84-150，浅湾可到 165（采样实测）
T_BEACH = 150.0                       # 滩/雪切分：距海欧氏距离阈值（源px，≈12km，按 MASTER 尺度）
OPEN_ITERS = 2                        # 5x5 开运算×2 ≈ 结构元 9x9：线厚≤8px 被识别为“线/点”
MASTER_W = 15840                      # 管线几何阈值的校准尺度（采样阈值基于 hires 推导）
SEED = 20260906                       # 细节噪声固定种子 → 同素材同图（可复现）
HEIGHT_GAMMA = 1.4                    # 🔴 压平曲线指数（2026-09-05 用户裁定「普通地表压平、富士独大」）：
                                      #    >1 = 普通地表压低、顶值保留：平 0.2→0.105 / 普通山 0.45→0.30 /
                                      #    雪帽 ~0.6→0.49 / 富士 1.0→1.0（1.0 = 不压，原样输出）
                                      #    场景 max_height=21 时：平原≈2m / 普通山≈6m / 富士≈21m（≈3.3×）


def classify(a):
    """a: (H,W,3) int16 → (sea, white, yellow, snow_dot)
    snow_dot = 高亮白点（雪山雪帽中心，与富士「蓝白雪+白顶」定义对应）"""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    blueish = (100 * b > 115 * r) & (100 * g > 102 * r) & (b >= 100)
    sea = blueish & (lum <= LUM_SEA_MAX)
    # 亮判：灰白/淡绿白（滩岩雪，低饱和）∪ 蓝白（富士雪）——排除海色；
    # 绿度排除 (g-b<=10)：Koei 平原/城郭亮色是 g 主导的浅绿，雪/滩是 g≈b——防「平原发白」误判
    lowsat = (np.abs(r - g) < 45) & (np.abs(g - b) < 45)
    bluewhite = (b > r * 1.05) & (lum > 160) & ~sea
    white = (((lowsat & (lum > 155)) | bluewhite) & ~sea & (g <= b + 10))
    yellow = (r - b > 50) & (g - b > 30) & (r > 150) & (g > b * 1.1) & ~sea
    # 高亮白点：雪帽中心（lum>168 低饱和）
    snow_dot = (lum > 168) & (np.abs(r - g) < 40) & (g <= b + 25)
    return sea, white, yellow, snow_dot


def autobbox(im):
    """40x 粗判陆地 bbox → 边距 → 补齐 1.6:1（严格绿判，防拼缝噪声拉爆）"""
    _set_src(im)
    small = im.resize((396, 264), Image.BOX)
    a_s = np.asarray(small).astype(np.int16)
    r, g, b = a_s[..., 0], a_s[..., 1], a_s[..., 2]
    land = (g > r) & (g > b * 0.95)
    land = ndimage.binary_opening(land, np.ones((3, 3)))
    cols = land.sum(0).astype(np.float64)
    rows = land.sum(1).astype(np.float64)
    cx = np.where(cols > 0.005 * cols.max())[0]
    cy = np.where(rows > 0.005 * rows.max())[0]
    sx, sy = SRC_W / 396.0, SRC_H / 264.0
    x0, x1 = (cx[0] - 6) * sx, (cx[-1] + 6) * sx
    y0, y1 = (cy[0] - 6) * sy, (cy[-1] + 6) * sy
    # 补比例：短边居中扩，越界改扩另一轴（两轮内必收敛）
    for _ in range(6):
        w, h = x1 - x0, y1 - y0
        if abs(w / h - TARGET_RATIO) < 0.002:
            break
        if w / h > TARGET_RATIO:
            need = w / TARGET_RATIO - h
            y0 -= need / 2
            y1 += need / 2
        else:
            need = h * TARGET_RATIO - w
            x0 -= need / 2
            x1 += need / 2
        x0 = max(0.0, x0); y0 = max(0.0, y0)
        x1 = min(SRC_W, x1); y1 = min(SRC_H, y1)
    return (x0, y0, x1, y1)


def fbm(rng, shape, base_cell, octaves=4, gain=0.5):
    """多倍频值噪声（每倍频独立随机格，map_coordinates 双线性采样）——平滑场"""
    H, W = shape
    acc = np.zeros(shape, np.float32)
    amp, tot, cell = 1.0, 0.0, base_cell
    for _ in range(octaves):
        gw_lat = max(3, int(W / cell) + 3)
        gh_lat = max(3, int(H / cell) + 3)
        lat = rng.random((gh_lat, gw_lat)).astype(np.float32)
        xs = np.broadcast_to((np.arange(W, dtype=np.float32) / cell)[None, :], (H, W))
        ys = np.broadcast_to((np.arange(H, dtype=np.float32) / cell)[:, None], (H, W))
        n = ndimage.map_coordinates(lat, [ys, xs], order=1, mode='wrap', prefilter=False)
        acc += amp * n
        tot += amp
        amp *= gain
        cell *= 0.5
    return acc / tot


_NQ_CACHE = None
def _load_nq():
    """NativeExample 陆地高度 CDF（native_quantiles.py 预计算表）——分布匹配的后验分布；
    表由 NativeExample/terrain_heightmap.png land 采样生成（quantile 0..100 步 1/128）。
    文件缺失（发布环境不带 NativeExample 时）→ 返回 None，管线自动跳过匹配。"""
    global _NQ_CACHE
    if _NQ_CACHE is not None:
        return _NQ_CACHE
    p = os.path.join(BASE, "native_quantiles.py")
    if not os.path.exists(p):
        print("[nq] native_quantiles.py 不存在 → 跳过分布匹配")
        _NQ_CACHE = None
        return None
    import importlib.util as _ilu
    spec = _ilu.spec_from_file_location("native_quantiles", p)
    mod = _ilu.module_from_spec(spec)
    spec.loader.exec_module(mod)
    _NQ_CACHE = np.asarray(mod.NATIVE_QUANTILES, np.float32)
    return _NQ_CACHE


def build(out_w, out_h):
    im = Image.open(SRC).convert("RGB")
    _set_src(im)
    # 🔴 尺度标定（2026-09-05 用户裁定）：几何阈值按 MASTER_W=15840 校准——
    # 小素材（如 TaikouMap2 704×448）同一管线直接跑会全军覆没（阈值 400px 在小图上=大半幅)。
    # 办法=小素材先 LANCZOS 放大到主线尺度，颜色判据分辨率无关，语义管线单标准执行。
    if SRC_W < MASTER_W * 0.5:
        ow0, oh0 = SRC_W, SRC_H
        up = MASTER_W / SRC_W
        im = im.resize((MASTER_W, max(1, int(SRC_H * up))), Image.LANCZOS)
        _set_src(im)
        print(f"[up] 小素材 {ow0}x{oh0} → 放大 {SRC_W}x{SRC_H}（管线单标准）")

    # ===== 1) 源图尺度语义掩膜 =====
    a = np.asarray(im).astype(np.int16)
    sea, white, yellow, snow_dot = classify(a)
    # 大海块：开运算把细蓝（河线）剔出；块级海=真海（含湖——湖是大块蓝，保留为海）
    sea_block = ndimage.binary_opening(sea, np.ones((5, 5)), iterations=OPEN_ITERS)
    river = sea & ~sea_block
    sea = sea_block
    # 🔴 雪山雪帽检测（用户裁定 2026-09-05）：蓝判块里含**足够大的**高亮白点(lum>168) = 高山雪。
    # 判据=最大白点簇（富士雪顶是全图最大白点；浪花/礁石/湖滨白点都是小簇，自动排除）。
    # 雪帽从水面救出 → 归雪顶高度（黑斑 bug 根因：富士蓝白雪色与海同色系，曾被当湖=0）。
    labd, ndd = ndimage.label(snow_dot)
    if ndd:
        sizes_d = ndimage.sum(snow_dot, labd, range(1, ndd + 1))
        maxd = float(sizes_d.max())
        big_dot = np.zeros_like(snow_dot)
        for i in range(ndd):
            if sizes_d[i] >= 0.3 * maxd:      # 最大簇 30% 以上 = 雪顶（预期 1-2 个）
                big_dot |= labd == i + 1
        dot_zone = ndimage.binary_dilation(big_dot, np.ones((9, 9)), iterations=6)
        snowcap = sea & dot_zone
        print(f"[cap] 最大白点簇={maxd:.0f}px 雪帽={snowcap.sum()}px")
    else:
        big_dot = snow_dot
        snowcap = np.zeros_like(sea)
    sea = sea & ~snowcap
    # 白色拆线/块：先闭运算（绿白交织的雪原合并成块，防碎白被误剔）再开运算剔线
    white = ndimage.binary_closing(white, np.ones((5, 5)), iterations=1)
    w_open = ndimage.binary_opening(white, np.ones((5, 5)), iterations=OPEN_ITERS)
    thin_white = white & ~w_open
    white_big = w_open
    # 黄路+河线 = 线状掩膜（底色替换用）
    line = (yellow | river)
    line_big = ndimage.binary_opening(line, np.ones((5, 5)), iterations=OPEN_ITERS)
    yline = line & ~line_big
    # 白色系连续距离映射（不再硬切雪/滩）：
    #   贴海 0.05 → 岩壁 0.14 → 内陆雪 0.55+（距离 600 源px 封顶）再叠雪脊内部差分
    # 距离阈值（仅 QA 着色画三段）：滩 ≤150 / 岩 150-400 / 雪 >400
    dist_sea = ndimage.distance_transform_edt(~sea).astype(np.float32)
    snow = white_big & (dist_sea > 400.0)
    beach = white_big & (dist_sea <= 150.0)
    # 海洋：细白线/点、以及海里粗白（礁石小点）的“邻接修正”——在海块内被淹没的区域清为海
    sea_final = (sea | (thin_white & ndimage.binary_dilation(
        sea, np.ones((3, 3)), iterations=3))) & ~snowcap
    # 雪帽质心列表（验证/给用户指认用）
    labc, nc = ndimage.label(snowcap)
    cap_list = []
    for i in range(1, nc + 1):
        yy, xx = np.where(labc == i)
        if len(yy) < 800:
            continue
        cap_list.append((xx.mean(), yy.mean(), len(yy)))
    print(f"[cap] 雪帽块数={len(cap_list)}")
    for cx, cy, sz in cap_list:
        print(f"  雪帽 ({cx:.0f},{cy:.0f}) 面积={sz}")
    print(f"[src] sea={100 * sea.mean():.2f}% river={100 * river.mean():.2f}% "
          f"thin={100 * thin_white.mean():.2f}% beach={100 * beach.mean():.2f}% "
          f"snow={100 * snow.mean():.2f}% yline={100 * yline.mean():.2f}%")
    del a, river, thin_white, w_open, line, line_big, sea_block

    # ===== 2) 目标尺度降采样 =====
    # 避免 BOX 均值吞掉信号：掩膜放大 255 后 BOX 均值 = 概率（float 0..1）
    def softmask(m):
        return np.asarray(Image.fromarray((m * 255).astype(np.uint8))
                          .resize((out_w, out_h), Image.BOX), dtype=np.float32) / 255.0

    M = {
        'sea':   softmask(sea_final),
        'white': softmask(white_big),
        'beach': softmask(beach),
        'snow':  softmask(snow),
        'yline': softmask(yline),
        'cap':   softmask(snowcap),
        'dot':   softmask(snow_dot),
        'dist':  np.asarray(Image.fromarray(
            np.clip(dist_sea / 8192.0, 0, 1).astype(np.float32)).resize((out_w, out_h), Image.BOX),
            np.float32) * 8192.0,
    }
    del dist_sea
    ap = np.asarray(im.resize((out_w, out_h), Image.LANCZOS)).astype(np.float32)
    r, g, b = ap[..., 0], ap[..., 1], ap[..., 2]
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    greenw = ((g > r * 1.02) & (g >= b * 0.95)).astype(np.float32)

    # ===== 3) 高度模型（v2 细节管代：2026-09-06 为 Native 细节水平改造） =====
    # 宏观（山块级）形态 + 结构感知山脊噪声：
    #   A 宏观：源图亮度先大平滑（σ=200源px，滤掉画法斑点/林冠/云影——画法明暗≠海拔，
    #           v1 直接分段=「暗绿大陆→黑团」根因）→ 分段 h_base + 雪帽映射 + PEAKS 地标 + gamma
    #   B 细节：多倍频 fbm 整域山脊变换（1-|2n-1|：|n-0.5| 过零曲线 = 连通排水网）+
    #           方向场=源图笔触描线方向（有机顺纹）→ 半分辨率生成再双线性放大 = 软润质感
    #   C 标定：band(6-40px) std=0.0345 vs Native 0.035 / p05 -0.063 vs -0.082 / 形态连通
    #           （2026-09-06 实测 NativeExample/terrain_heightmap.png 山窗基准）
    lum_big = np.asarray(Image.fromarray(np.clip(lum, 0, 255).astype(np.uint8))
                         .filter(ImageFilter.GaussianBlur(200.0 * out_w / SRC_W))).astype(np.float32)
    # v3A 宏观加陡：山档铺开到 0.74 顶（γ1.4 后 0.65）——Native 全图「山域发亮」的根
    # 是山块自身到 0.5-0.7 的亮度 + 山块间 ±0.15 差异（massif 层叠加），v2 只到 0.35 必秃。
    # 排布：亮平原 0.10-0.22 / 普通山地 0.38-0.50 / 暗山西 0.50-0.74（富士 1.0 仍独大）
    h_base = np.where(
        lum_big >= 150, np.clip(0.22 - (lum_big - 150) * 0.0018, 0.10, 0.22),
        np.where(lum_big >= 110, 0.38 + (150 - lum_big) * 0.0030,
                 0.50 + np.clip(110 - lum_big, 0, 60) * 0.0040))
    # 白色系（滩/岩壁/雪）连续距离映射：贴海 0.05 → 600源px 0.55；雪顶雪帽另算
    h_white = 0.05 + np.clip(M['dist'] / 600.0, 0, 1) * 0.50
    h_white = np.clip(h_white, 0.05, 0.95)
    w_land = np.clip(1.0 - M['sea'] * 1.5, 0, 1)          # 海概率把陆地权重压下去
    hm = h_white * M['white'] * w_land \
        + h_base * greenw * w_land \
        + h_base * np.clip(1 - greenw - M['white'], 0, 1) * w_land  # 非绿非白回落绿域
    hm = np.where((M['sea'] >= 0.5) & (M['cap'] < 0.5), 0.0, hm)

    # ===== 3.5) 雪帽救赎 + PEAKS 地标（与 v1 相同的判断与裁定，不变） =====
    h_cap = 0.60 + 0.30 * M['dot']
    hm = np.maximum(hm, h_cap * M['cap'])
    PEAKS = [
        (10500, 7000, 320, 1.00, 0.42, 800),   # 富士山（唯一峰值，雪顶/山体双层锥）
    ]
    if PEAKS:
        ys, xs = np.mgrid[0:out_h, 0:out_w]
        peak_zone = np.zeros((out_h, out_w), np.float32)
        peak_cone = np.zeros((out_h, out_w), np.float32)
        for cx, cy, R, H, B, RB in PEAKS:
            px = cx * out_w / SRC_W
            py = cy * out_h / SRC_H
            sc = R * out_w / SRC_W
            d = np.sqrt((xs - px) ** 2 + (ys - py) ** 2) / sc
            cone = H * np.clip(1.0 - d, 0, 1) ** 1.2          # 峰锥
            body = B * np.clip(1.0 - d * (R / RB), 0, 1) ** 0.8  # 山体基底（宽裙坡）
            hm = np.maximum(hm, np.maximum(cone, body))       # 只抬不压
            peak_zone = np.maximum(peak_zone, (np.maximum(cone, body) > 0.03).astype(np.float32))
            peak_cone = np.maximum(peak_cone, (cone > 0.35 * H).astype(np.float32))
    else:
        peak_zone = np.zeros((out_h, out_w), np.float32)
        peak_cone = np.zeros((out_h, out_w), np.float32)
    # 压平曲线（用户裁定：普通地表压平、富士独大；雪 mask 阈值 0.42 仍达标）
    hm_m = np.power(hm, HEIGHT_GAMMA)

    # ===== 3.6) 结构感知山脊细节（连通排水网 + 笔触方向场 + 半分辨软烤） =====
    # 结构场：源图山影带通（θ=75源px）→ 渐变方向 = 画中山脊描线走向
    sig_mid = 75.0 * out_w / SRC_W
    base_mid = np.asarray(Image.fromarray(np.clip(lum, 0, 255).astype(np.uint8))
                          .filter(ImageFilter.GaussianBlur(sig_mid))).astype(np.float32)
    S_mid = lum - base_mid
    sstd = float(S_mid.std())
    Ws = ndimage.gaussian_filter(np.clip(S_mid / (2.5 * max(sstd, 1e-6)), -1, 1), 6.0)
    gsy_, gsx_ = np.gradient(Ws)
    gg = np.sqrt(gsx_ ** 2 + gsy_ ** 2) + 1e-6
    Dx = ndimage.gaussian_filter(gsx_ / gg, 1.5)
    Dy = ndimage.gaussian_filter(gsy_ / gg, 1.5)
    rng = np.random.default_rng(SEED)
    warp_y = fbm(rng, (out_h, out_w), 26.0, 3, 0.5) - 0.5
    K = 5.0

    # 崎岖度包络：高度驱动（平原缓、山域满）+ 坡增比 + 近海平滑（滩岩海岸不皱）
    slope = ndimage.gaussian_filter(np.sqrt(
        (np.gradient(hm)[1]) ** 2 + (np.gradient(hm)[0]) ** 2), 3.0)
    env = np.clip((hm_m - 0.08) / 0.22, 0, 1) ** 1.3
    env = env * (0.75 + 0.25 * np.clip(slope / 0.006, 0, 1))
    env = env * np.clip(M['dist'] / 120.0, 0.1, 1.0)
    env = np.clip(env, 0, 1)

    # 半分辨率细节场参数（软润质感 与 消除 1-2px 脆毛）
    head, we = max(2, out_h // 2), max(2, out_w // 2)
    def _hs(arr):
        u8 = (np.clip(arr, -1, 1) * 127.5 + 127.5).astype(np.uint8)
        return np.asarray(Image.fromarray(u8).resize((we, head), Image.BILINEAR),
                          np.float32) / 127.5 - 1.0

    Gx_h = np.broadcast_to(np.arange(we, dtype=np.float32)[None, :], (head, we))
    Gy_h = np.broadcast_to(np.arange(head, dtype=np.float32)[:, None], (head, we))
    Dx_h, Dy_h = _hs(Dx), _hs(Dy)
    Uo = Gx_h * Dx_h + Gy_h * Dy_h
    Vo = Gx_h * (-Dy_h) + Gy_h * Dx_h
    Ws_h, warp_h = _hs(Ws), _hs(warp_y)

    def ridge_net(base_cell, octv, sb, sd, s=1.0):
        """整域山脊变换：多倍频 fbm 一次 (1-|2n-1|) 变换——|n-0.5| 过零曲线=连通排水网；
        sb 小→亮丘宽；sd 大→暗沟窄。采样坐标沿笔触方向拉伸 s 倍（顺纹）。"""
        acc = np.zeros((head, we), np.float32); amp, tot = 1.0, 0.0
        lat_cell = base_cell
        U = Uo / s + K * Ws_h
        V = Vo + K * warp_h
        for _ in range(octv):
            gw_lat = max(3, int(U.max() / lat_cell) + 3)
            gh_lat = max(3, int(V.max() / lat_cell) + 3)
            lat = rng.random((gh_lat, gw_lat)).astype(np.float32)
            n = ndimage.map_coordinates(lat, [V / lat_cell, U / lat_cell], order=1,
                                        mode='wrap', prefilter=False)
            acc += amp * n
            tot += amp
            amp *= 0.5
            lat_cell *= 0.5
        n = acc / tot
        r = 1.0 - 2.0 * np.abs(n - 0.5)
        return r ** sb - 1.15 * (1.0 - r) ** sd

    # 频段（半分辨格；全分辨率×2）：主 36(~72px) / 中 9 / 微 3.5 —— Native 实测频段能量
    # 90% 在 20px 以上（细带 std 只有中带 1/6），高频必须很少
    cfg = [("main", 42.0, 5, 0.22, 1.9, 2.0, 0.80),
           ("mid", 10.0, 3, 0.30, 2.4, 1.0, 0.16),
           ("micro", 4.0, 2, 0.40, 2.8, 1.0, 0.04)]
    totw = sum(c[6] for c in cfg)
    det = np.zeros((head, we), np.float32)
    for name, base_cell, octv, shp_b, shp_d, s, wt in cfg:
        det += wt * ridge_net(base_cell, octv, shp_b, shp_d, s) / totw
    # v3A 山块差异化：massif（山块层）振幅 << 加大，宏脊也加重——Native 全图山块间
    # 明暗差 ±0.15 是「丰富感」的主因之一（不是纹理，是块阶）
    meg = ridge_net(85.0, 4, 0.22, 2.0, 2.2)          # 宏脊（窗内坡度）
    massif = ridge_net(220.0, 3, 0.20, 2.0, 1.5)      # 山块（山块间差异，加大块径）
    det = det * 0.50 + 0.26 * meg + 0.24 * massif
    det = det - float(det.mean())
    det = np.clip(det, -0.45, 0.42)
    det = np.where(det > 0, det * 2.0, det * 1.30)    # 亮侧与谷沟同步刻深（Native：峰亮沟黑）
    det = (det + 0.45) / 0.87
    det = np.asarray(Image.fromarray((np.clip(det, 0, 1) * 255).astype(np.uint8))
                     .resize((out_w, out_h), Image.BILINEAR), np.float32) / 255.0 * 0.87 - 0.45
    massif = (massif - massif.min()) / max(1e-6, massif.max() - massif.min())
    massif = np.asarray(Image.fromarray((np.clip(massif, 0, 1) * 255).astype(np.uint8))
                        .resize((out_w, out_h), Image.BILINEAR), np.float32) / 255.0
    # v3A 宽谷带：大格(360px)山脊场的负部深刻——Native 全图的「黑色宽河谷带」贯穿感
    vally = ridge_net(360.0, 3, 0.35, 1.5, 1.5)
    vally = np.clip(vally - vally.mean(), -0.45, 0.10)
    vally = np.asarray(Image.fromarray((np.clip((vally + 0.45) / 0.55, 0, 1) * 255).astype(np.uint8))
                       .resize((out_w, out_h), Image.BILINEAR), np.float32) / 255.0 * 0.55 - 0.45
    d = env * (det * 0.95 + (massif - massif.mean()) * 0.85 + np.clip(vally, -0.45, 0.0) * 0.65)
    hm = np.clip(hm_m + d, 0, 1)

    # 线状元素底色替换：中值滤波只在 yline 高概率处写入（路/河不抬山不刻槽）
    hmf = ndimage.median_filter(hm, size=min(31, max(15, out_w // 96)))
    hm = np.where(M['yline'] > 0.25, hmf, hm)

    # ===== 3.9) 分布匹配（2026-09-06 用户在线判决：全图丰富度 = 分布层次 + 块尺度结构）=====
    # NativeExample 陆地高度 CDF（native_quantiles.py 预计算表）为「后验分布」：
    # 单调 rank 映射 → 亮块/黑谷/中灰 比例与 Native 同构。
    # 🔴 v3C 结构放大（关键修正）：rank 匹配是单调映射——空间块形状全由匹配前的结构决定。
    #    massif（山块层 ±0.2）必须 ×2.8 放大注入 core（σ90 平滑的大结构）再匹配，
    #    否则 core 是均质平台 → 匹配后仍是平台（v3B 碎钻 / v3C 平灰 两个极端教训）。
    # 豁免：PEAKS（富士 1.0 独大）与雪帽（0.6-0.9）不参与映射。
    NQ = _load_nq()
    if NQ is not None and len(NQ) >= 2:
        hmf = np.clip(hm, 0, 1)
        exempt = (peak_zone >= 0.5) | (M['cap'] >= 0.5)
        m_land = (M['sea'] < 0.5) & ~exempt
        if m_land.any():
            sig_core = 130.0 * out_w / 4096.0
            core = np.asarray(Image.fromarray((hmf * 255).astype(np.uint8))
                              .filter(ImageFilter.GaussianBlur(sig_core))).astype(np.float32) / 255.0
            fine = hmf - core
            core_s = core + 3.6 * (massif - massif.mean())     # 山块结构放大后进匹配
            vals = np.clip(core_s, 0, 1)[m_land]
            rank = np.argsort(np.argsort(vals)).astype(np.float32) / max(1, vals.size - 1)
            outf = np.interp(rank, np.linspace(0.0, 1.0, len(NQ)), NQ)
            hmf = np.array(hmf, copy=True)
            hmf[m_land] = outf + fine[m_land] * 0.35
        hm = np.where(m_land, hmf, hm)

    # ===== 4) 平滑 + 细节回补 + 严格分布重排 + 16bit + 强制海 0 =====
    # 🔴 v3G（用户硬指标：文件熵 B/px native 1.06 vs 我们 0.28——高频细节差一个量级）：
    #   ①全流程 float 域（旧链中段经 uint8 量化 → 16bit 图只有 8bit 有效信息）；
    #   ②重排（CDF 钉死 Native）后回补「平滑损失的多尺度细节」→ 空间信息密度从 0.28 → 0.8+；
    #   ③回补后值域 CDF 仅轻微扰动（±0.04 幅度），空间熵不受影响。
    hm_f = np.clip(hm, 0, 1).astype(np.float32)
    hm_sm = ndimage.median_filter(hm_f, size=5)
    hm_sm = ndimage.gaussian_filter(hm_sm, 2.2)
    fine2 = hm_f - hm_sm                                    # 平滑损失的 1-8px 细节
    # 微排水网（树状细支脉；半格生成→全格 float）
    env_h = _hs(np.clip(env, 0, 1))
    mnet_h = env_h * np.clip(ridge_net(7.0, 3, 0.30, 2.2, 1.5), -1, 1) * 0.030
    mnet = np.asarray(Image.fromarray((np.clip(mnet_h * 127.5 + 127.5, 0, 255)).astype(np.uint8))
                      .resize((out_w, out_h), Image.BILINEAR), np.float32) / 127.5 - 1.0
    hmv = hm_sm.copy()
    # 洋统一高度；雪帽/地标区豁免（保持原裁定值：富士 1.0 独大、雪顶 0.6-0.9）
    hmv[(M['sea'] >= 0.5) & (M['cap'] < 0.5) & (peak_zone < 0.5)] = 0.0
    # 🔴 严格重排（平滑后）：全部 land（非豁免）按 rank 严格映射 NQ → CDF == Native 精确；
    # rank 保序 → 结构/细节拓扑保留。豁免区（富士/雪帽）保留原裁定值。
    if NQ is not None and len(NQ) >= 2:
        exempt = (peak_zone >= 0.5) | (M['cap'] >= 0.5)
        landx = (M['sea'] < 0.5) & ~exempt
        valsx = hmv[landx]
        rankx = np.argsort(np.argsort(valsx)).astype(np.float32) / max(1, valsx.size - 1)
        outf = np.interp(rankx, np.linspace(0.0, 1.0, len(NQ)), NQ)
        hmv = np.array(hmv, copy=True)
        hmv[landx] = outf
    # 4.5) 云丘柔化（2026-09-06 用户五打回「碎钻/痘子」的根治）：rank 匹配产出「值域被打散」的
    #   黑白小粒；native 是「大云状连片山体」。做法=匹配后 σ10 大低通（成云丘连片），
    #   再叠 native 式连续纹理（中纹 σ2.5 残差 + 微纹 σ1.2 残差 + 细支脉）——纹理连片不散点。
    hmv = ndimage.gaussian_filter(hmv, 10.0)
    base = hmv
    mid = ndimage.gaussian_filter(hmv, 2.5)
    fin = ndimage.gaussian_filter(hmv, 1.2)
    d_fine = ((base - mid) + (mid - fin)) * 0.30 + env * np.clip(mnet, -1, 1) * 0.012
    hmv = np.clip(hmv + d_fine, 0, 1)
    nz = np.random.default_rng(SEED + 1)
    wn = nz.standard_normal((out_h, out_w)).astype(np.float32) * 0.0012
    hmv = np.clip(hmv + wn, 0, 1)
    hmv = (hmv * 65535.0).astype(np.uint16)

    out16 = os.path.join(OUTD, f"hm_{ST}_{out_w}x{out_h}_16bit.png")
    Image.fromarray(hmv).save(out16)
    print(f"[out] {out16}  land={100 * float((hmv > 0).mean()):.1f}% "
          f"min={hmv.min()} max={hmv.max()}")

    # ===== 5) QA：上半分类花色（海蓝/滩土黄/岩灰/雪白/路金/绿底灰）+ 下半灰度高度 =====
    gw = (np.clip(h_base / 0.55, 0, 1) * 200).astype(np.uint8)
    qa = np.dstack([gw, gw, gw])
    qa[np.where(M['sea'] >= 0.5)] = (30, 80, 150)
    rock = (M['white'] >= 0.5) & (M['dist'] > 150) & (M['dist'] <= 400)
    qa[np.where(M['beach'] >= 0.5)] = (180, 160, 100)
    qa[np.where(rock)] = (160, 150, 140)
    qa[np.where(M['snow'] >= 0.5)] = (245, 245, 245)
    qa[np.where(M['yline'] >= 0.4)] = (255, 190, 0)
    terr = np.stack([np.clip(hmv.astype(np.float32) * 0.9 / 65535 * 255, 0, 255)
                     .astype(np.uint8)] * 3, -1)
    Image.fromarray(np.concatenate([qa, terr], 0)) \
        .save(os.path.join(OUTD, f"qa_{ST}_{out_w}x{out_h}.png"))
    # ===== 6) Materialmap 材质图（RGBA 8bit）= R草 G林 B沙土 A雪；海/湖=0（引擎自动铺水面）=====
    # 草/林与高度模型同源（h_base 平原/山地档）：平原档(亮绿段 h_base<0.30)=草，山地档=林
    # 注：源图関東平原为暗绿画法（lum 128 落山地档），归林——如要草，调 h_base 平原阈值重生成
    # 🔴 v2：材质分档必须用「未大平滑」的源图亮度（画法覆盖是语义本身：亮绿=草野、暗绿=森山）；
    #    高度用 σ200 大平滑（防斑点当海拔）但分类用原始画法——两把尺子，禁止复用 h_base。
    lum8_cls = np.clip(lum, 0, 255).astype(np.uint8)
    lum_cls = np.asarray(Image.fromarray(lum8_cls)
                         .filter(ImageFilter.GaussianBlur(25.0 * out_w / SRC_W))).astype(np.float32)
    h_base_cls = np.where(
        lum_cls >= 150, np.clip(0.22 - (lum_cls - 150) * 0.0018, 0.10, 0.22),
        np.where(lum_cls >= 110, 0.38 + (150 - lum_cls) * 0.0020,
                 0.455 + np.clip(110 - lum_cls, 0, 60) * 0.0015))
    mat = np.zeros((out_h, out_w, 4), np.float32)
    gmask = (M['sea'] < 0.5) & (M['white'] < 0.5) & (M['yline'] < 0.6)
    mat[..., 0] = np.where(gmask & (h_base_cls < 0.30), 255.0, 0.0)   # 草（平原档）
    mat[..., 1] = np.where(gmask & (h_base_cls >= 0.30), 255.0, 0.0)  # 林（山地档）
    mat[..., 2] = np.where((M['white'] >= 0.5) & (M['dist'] <= 400), 255.0, 0.0)   # 沙/岩
    # 🔴 雪与高度交叉（用户裁定 2026-09-05）：白系虽判「远段=雪」，但必须 hm≥0.42 才配当雪；
    # 海岸线白带（岩壁/浪花）高度低 → 降级为沙/岩；高度太高说明地形本身就是高海拔。
    hnorm = hmv.astype(np.float32) / 65535.0
    snow_allowed = hnorm >= 0.42
    white_far = (M['white'] >= 0.5) & (M['dist'] > 400)
    mat[..., 3] = np.where((white_far | (M['cap'] >= 0.3) | (peak_cone >= 0.5)) & snow_allowed,
                           255.0, 0.0)
    mat[..., 2] = np.maximum(mat[..., 2], np.where(white_far & ~snow_allowed, 255.0, 0.0))
    # 🔴 地标区 → 雪：与高度图 peak_zone 豁免同源（富士等雪峰不是水）——
    # 材质图雪只认峰锥 peak_cone（山体基底 800px 不算雪，不然富士南边整片误白，2026-09-05 用户指正）
    mat[..., 3] = np.maximum(mat[..., 3], np.where(peak_cone >= 0.5, 255.0, 0.0))
    # 地标体区（峰锥外）→ 林：富士山体蓝雪圈若留空通道会被引擎读出为「水」——必须归类（2026-09-05）
    mat[..., 1] = np.maximum(mat[..., 1], np.where((peak_zone >= 0.5) & (peak_cone < 0.5), 255.0, 0.0))
    mat8 = mat.astype(np.uint8)
    outmat = os.path.join(OUTD, f"mat_{ST}_{out_w}x{out_h}.png")
    Image.fromarray(mat8, "RGBA").save(outmat)
    print(f"[out] {outmat}  R草={100 * (mat8[...,0] > 0).mean():.1f}% G林={100 * (mat8[...,1] > 0).mean():.1f}% "
          f"B沙={100 * (mat8[...,2] > 0).mean():.1f}% A雪={100 * (mat8[...,3] > 0).mean():.1f}%")

    # ===== 6.5) 分层 mask（官方流程：每图层一张 8bit 灰度，Add Layer→全选→逐层导入）=====
    # L1草 / L2林 / L3沙 / L4雪 与 RGBA 四通道同源同值（仅在引擎分层导入模式下使用，杜绝通道顺序猜谜）；
    # L5水 = 真水（海/湖，排除雪帽/地标区；河/路暂未分离，见知识文档三·八）
    wmask = (((M['sea'] >= 0.5) & (mat8[..., 3] < 128) & (mat8[..., 2] < 128)
              & (peak_zone < 0.5)).astype(np.uint8) * 255)
    for name, ch in (("L1_grass", mat8[..., 0]), ("L2_forest", mat8[..., 1]),
                     ("L3_sand", mat8[..., 2]), ("L4_snow", mat8[..., 3]),
                     ("L5_water", wmask)):
        p = os.path.join(OUTD, f"mask_{ST}_{name}_{out_w}x{out_h}.png")
        Image.fromarray(np.ascontiguousarray(ch).astype(np.uint8), "L").save(p)
        print(f"[out] {p}  {(np.asarray(ch) > 0).mean() * 100:.1f}%")
    # 材质预览（自然调色板：草浅绿/林深绿/沙土黄/雪白/海蓝）——mat_xxx.png 是 RGBA 四通道引擎版，
    # 普通查看器会把 A=雪当 alpha 显示成半透明/黑，肉眼看这张 preview
    w = mat8.astype(np.float32) / 255.0
    cols = np.array([(130, 190, 90), (40, 120, 40), (200, 180, 110), (240, 240, 240)], np.float32)
    prev = np.clip(w[..., 0, None] * cols[0] + w[..., 1, None] * cols[1]
                   + w[..., 2, None] * cols[2] + w[..., 3, None] * cols[3], 0, 255)
    # 海/湖涂蓝限制在「真水」（RGB 全 0 且不在地标区）——避免覆盖雪帽/地标（富士蓝雪区 sea 判定=1 但 A 已写雪）
    true_water = (M['sea'] >= 0.5) & (mat8[..., 3] < 128) & (mat8[..., 2] < 128) & (peak_zone < 0.5)
    prev[true_water] = (60, 110, 170)
    Image.fromarray(prev.astype(np.uint8)).save(os.path.join(OUTD, f"matpreview_{ST}_{out_w}x{out_h}.png"))
    return out16


if __name__ == "__main__":
    import os as _os
    # 可选覆盖：python make_heightmap.py [w] [h] [src_png] [out_dir]
    if len(sys.argv) >= 4:
        SRC = sys.argv[3]
    if len(sys.argv) >= 5:
        OUTD = sys.argv[4]
    if not _os.path.exists(SRC):
        print(f"[err] 素材图不存在: {SRC}")
        sys.exit(1)
    _os.makedirs(OUTD, exist_ok=True)
    import re as _re
    ST = _re.sub(r"\.(png|jpg|bmp|dds)$", "", _os.path.basename(SRC))
    # 先算 bbox（一次即可，打印给用户）
    _im = Image.open(SRC).convert("RGB")
    print("[bbox]", autobbox(_im))
    del _im
    sizes = [(4096, 2560)] if len(sys.argv) < 3 else [(int(sys.argv[1]), int(sys.argv[2]))]
    for w, h in sizes:
        print(f"===== {w}x{h}  src={ST}  out={OUTD} =====")
        build(w, h)

# -*- coding: utf-8 -*-
"""japanmap_hires.png -> 16bit 灰度高度图 + RGBA 材质图（战役地形管线，链路见 Knowledge/骑砍2战役地形制作管线.md）

2026-09-04/05 分层模型版（替换旧「亮度差分吃整图」;实测校验见知识文档）:
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
  python tools/make_heightmap.py    # 主档 4096x2560：hm_4096x2560_16bit.png + mat_4096x2560.png + 预览
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
SIG_BASE_SRC_PX = 150.0               # 🔴 山影差分基底 σ（源图px）：150=山域级(平顺, 交付版基线)；
                                      #    60=山脊级(细节细、噪感升)。待用户 ModKit 实测选定后定稿


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

    # ===== 3) 高度模型 =====
    # 绿基底：亮度连续映射（山脉暗 90→0.10，平原亮 180→0.35）+ 山影高斯差分
    # σ 按源图山域尺度换算（源纹 60px → 输出格数），禁止按输出分辨率等比放大（会把细节抹平）
    # 60px 比早期 150px 更「收窄」：捕获山脊级细节（2026-09-05 用户反馈 hires 山体太平滑）
    sig = SIG_BASE_SRC_PX * out_w / SRC_W
    base = np.asarray(Image.fromarray(lum.astype(np.uint8))
                      .filter(ImageFilter.GaussianBlur(sig))).astype(np.float32)
    detail = np.clip(lum - base, -55, 55)
    # 绿基底的亮度分段归位（Koei 画法：平原=亮黄绿、山地=暗绿 → 亮度与高度「反向」，
    # 不能直接用亮度抬山；山形主信号=山影差分 detail，亮度只做平原/山地的分档）：
    #   lum≥150 亮平原/城郭 → 0.14~0.20 低档（越亮越低，大平原发白 bug 修复）
    #   lum 110~150 普通山地 → 0.20~0.26 中档（暗绿=森林山）
    #   lum<110 阴影级 → 0.26~0.34 凹陷区微抬（防“山沟黑洞”视觉）
    h_base = np.where(
        lum >= 150, np.clip(0.22 - (lum - 150) * 0.0018, 0.10, 0.22),
        np.where(lum >= 110, 0.38 + (150 - lum) * 0.0020,
                 0.455 + np.clip(110 - lum, 0, 60) * 0.0015))
    # 山影增益按「低通亮度」调制（实测：Koei 平原纹理 detail P90=39 反而比山地 17 大——
    # detail 本身判别不了平原/山地；有效的判别=亮度：平原亮绿 160 / 山地暗绿 120）。
    # 亮处（softlum≥168）山影增益→0（路网/城郭细纹不再抬高平原），暗绿山地→全增益。
    softlum = np.asarray(Image.fromarray(lum.astype(np.uint8))
                         .filter(ImageFilter.GaussianBlur(25))).astype(np.float32)
    g = np.clip((168.0 - softlum) / 45.0, 0, 1)
    # 非对称增益：脊快（/95）谷缓（/165）——山脊骨骼锐利、谷地平缓（2026-09-05 增强）
    # 山影增益：均匀 /150（交付基线；非对称 脊/95 谷/165 曾实验性进入又覆写——以基线为准，参数见 SIG_BASE_SRC_PX）
    h_green = np.clip(h_base + detail * g / 150.0, 0.08, 0.55)
    # 白色系（滩/岩壁/雪）连续映射：离海 0 → 0.05，600px → 0.55；雪脊内部差分再抬
    h_white = 0.05 + np.clip(M['dist'] / 600.0, 0, 1) * 0.50 \
              + M['white'] * np.clip(detail, 0, 55) / 420.0
    h_white = np.clip(h_white, 0.05, 0.95)

    w_land = np.clip(1.0 - M['sea'] * 1.5, 0, 1)          # 海概率把陆地权重压下去
    hm = h_white * M['white'] * w_land \
        + h_green * greenw * w_land \
        + h_green * np.clip(1 - greenw - M['white'], 0, 1) * w_land  # 非绿非白回落绿域
    hm = np.where((M['sea'] >= 0.5) & (M['cap'] < 0.5), 0.0, hm)

    # ===== 3.5) 雪帽救赎：蓝白雪+中心白点 → 高山雪顶（0.62 圈 / 0.90 白点核）
    h_cap = 0.60 + 0.30 * M['dot']
    hm = np.maximum(hm, h_cap * M['cap'])

    # 地标修正：图面画法没画高但地理上必须高的峰 =====
    # 表项 = (源图 cx, cy, 锥半径px, 峰高, 山体基底高, 山体半径px)
    # 🔴 最终坐标用户亲报（2026-09-05 坐标格线图）：富士 = (10500, 7000)，山顶白点格线交点。
    # 源图特征=中心白点+蓝白雪圈+绿地包围（高山雪画法）；雪帽检测此前也在 (10453,6948) 摸到此块。
    PEAKS = [
        (10500, 7000, 320, 0.88, 0.42, 800),   # 富士山
    ]
    if PEAKS:
        ys, xs = np.mgrid[0:out_h, 0:out_w]
        peak_zone = np.zeros((out_h, out_w), np.float32)
        peak_cone = np.zeros((out_h, out_w), np.float32)   # 峰锥（雪）与山体基底分层：材质图雪只认锥
        for cx, cy, R, H, B, RB in PEAKS:
            px = cx * out_w / SRC_W
            py = cy * out_h / SRC_H
            sc = R * out_w / SRC_W
            d = np.sqrt((xs - px) ** 2 + (ys - py) ** 2) / sc
            cone = H * np.clip(1.0 - d, 0, 1) ** 1.2          # 峰锥
            body = B * np.clip(1.0 - d * (R / RB), 0, 1) ** 0.8  # 山体基底（宽裙坡）
            hm = np.maximum(hm, np.maximum(cone, body))       # 只抬不压：周边已有更高地形不削
            peak_zone = np.maximum(peak_zone, (np.maximum(cone, body) > 0.03).astype(np.float32))
            peak_cone = np.maximum(peak_cone, (cone > 0.35 * H).astype(np.float32))
    else:
        peak_zone = np.zeros((out_h, out_w), np.float32)
        peak_cone = np.zeros((out_h, out_w), np.float32)

    # 线状元素底色替换：中值滤波只在 yline 高概率处写入（路/河不抬山不刻槽）
    hmf = ndimage.median_filter(hm, size=min(31, max(15, out_w // 96)))
    hm = np.where(M['yline'] > 0.25, hmf, hm)

    # ===== 4) 平滑 + 16bit + 强制海 0 =====
    # 防针尖平滑固定 σ2.0（输出格单位）；禁止随分辨率放大
    hm8 = Image.fromarray((hm * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5))
    hm8 = hm8.filter(ImageFilter.GaussianBlur(2.0))
    hmv = np.asarray(hm8).astype(np.float32)
    hmv[(M['sea'] >= 0.5) & (M['cap'] < 0.5) & (peak_zone < 0.5)] = 0.0  # 洋统一高度；雪帽/地标区豁免
    hmv = (hmv / 255.0 * 65535.0).astype(np.uint16)

    out16 = os.path.join(OUTD, f"hm_{ST}_{out_w}x{out_h}_16bit.png")
    Image.fromarray(hmv).save(out16)
    print(f"[out] {out16}  land={100 * float((hmv > 0).mean()):.1f}% "
          f"min={hmv.min()} max={hmv.max()}")

    # ===== 5) QA：上半分类花色（海蓝/滩土黄/岩灰/雪白/路金/绿底灰）+ 下半灰度高度 =====
    gw = (np.clip(h_green / 0.55, 0, 1) * 200).astype(np.uint8)
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
    mat = np.zeros((out_h, out_w, 4), np.float32)
    gmask = (M['sea'] < 0.5) & (M['white'] < 0.5) & (M['yline'] < 0.6)
    mat[..., 0] = np.where(gmask & (h_base < 0.30), 255.0, 0.0)   # 草（平原档）
    mat[..., 1] = np.where(gmask & (h_base >= 0.30), 255.0, 0.0)  # 林（山地档）
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

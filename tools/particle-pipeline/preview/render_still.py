#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""粒子特效离屏渲染 —— 把 XML 渲成 PNG，用于和参考图并排比对。

【为什么需要它】
three.js 预览器只能靠人在浏览器里看；Claude 没有截图通道时就是瞎的。
这个脚本用 numpy 重实现同一套规则（同一份 XML、同一个相机、同样三种混合），
把指定时刻的静帧渲出来 —— **我自己的输出我自己能看**。

它**不是**游戏渲染的替代品：只画粒子 + 几个替身网格，没有 shader、没有光照。
用途只有一个：**快速自检「形状/密度/尺度/颜色」对不对**。

用法：
    python Debug/offline/particle_demo/render_still.py --t 1.40 --out charge.png
    python Debug/offline/particle_demo/render_still.py --t 1.85 --out burst.png
    python Debug/offline/particle_demo/render_still.py --t 3.20 --out trail.png
"""
import argparse, glob, math, os, random, re, sys
import xml.etree.ElementTree as ET
import numpy as np
from PIL import Image

random.seed(20260918)

HERE = os.path.dirname(os.path.abspath(__file__))
# 材质贴图目录（`_mat_tex_survey.py` 的产物；递归找，dump 会按类别分子目录）
MAT_DIR_DEF = os.path.join(HERE, "..", "out", "mattex_all")
W, H = 1100, 620
FOV = 46.0

# ===========================================================================
# 视图配置 —— XML 里**根本不存在**的那几个量：阶段划分 / 锚点 / 相机 / 替身网格
#
# 两种来源：
#   ① sidecar：与 XML 同名的 `<xml 去后缀>.view.json`（或 `--view` 指定）
#   ② 自动模式（没有 sidecar）：时间轴按 effect 数等分、锚点都在原点、
#      固定机位、不画阴魔斩那套替身网格（法印/球壳/月牙）
#
# 🔴 为什么必须分出来：阴魔斩那套（0~1.5s 举手蓄力 / 1.5~2.4s 胸前爆开 /
#    1.72~5.4s 沿 13 米直线飞出）是**那一个 demo 的演出参数**，不是 XML 的属性。
#    写死在代码里 = 换任何别的 XML 都对不上（原版甚至连 effect 少于 3 个都会
#    直接 IndexError，2026-09-20 修）。
# ===========================================================================
VIEW = None          # dict，由 load_view() 填
PHASES = []          # 摊平后的阶段表（configure() 填）
MESHES = False       # 是否画阴魔斩的替身网格
CAM_DEF = {"look": [0.0, -0.15, 0.0], "dist": 8.6}

# 🔴 每材质真贴图（2026-09-24 新增）—— 原版每个 prt_shd_* 材质的贴图**完全不一样**：
#    火焰 = torchflameloop（一格格的橙火苗）· 碎石 = stone_gravel_d · 雪 = prt_text_snow_dust_1
#    · 火星 = spark（黄色锥形条）· glow = 芝麻大的一个亮点 · 血 = blood19_sprite_horizontal …
#    预览必须**按材质取贴图**，否则火/冰/毒/血全渲成同一团灰烟 —— 那就"看不出对不对"，
#    也会误导判断（实测：曾经据此差点把 fire_1 的贴图问题当成冰系映射错）。
#    贴图来源：Debug/offline/_mat_tex_survey.py 从原版包导出 → out/mattex_all/（按类别分子目录）。
MAT_TEX = {}          # 材质名 -> PNG 路径
_SPRITE_SETS = {}     # (材质, 列, 行) -> (cells, 每格平均色)
TEX_DEFAULT = None    # 找不到材质贴图时的兜底（--tex，默认 smoke_d_256.png）
TEX_RGB = False       # 贴图自带颜色是否参与调色（默认关：阴魔斩那套基准是"只取形状"对齐出来的）
FOLLOW = {}          # 某阶段的专用机位：{fx: {"look":…, "dist":…}}
TRAIL_FRM, TRAIL_TO = 0.0, 0.0


def load_view(xml_path, view_path=None):
    """读 sidecar 视图配置；没有 sidecar → 自动模式。"""
    import json
    p = view_path or (os.path.splitext(xml_path)[0] + ".view.json")
    if os.path.exists(p):
        v = json.load(open(p, encoding="utf-8"))
        v["_src"] = os.path.basename(p)
        v.setdefault("auto", False)
        return v
    return {"auto": True, "_src": "自动模式（无 sidecar）"}


def est_total(fx):
    """自动模式的播放时长：按最长粒子寿命×2 + 余量估（与 three.js 预览器同一口径）。"""
    longest = 1.3
    for E in fx:
        for em in E["emitters"]:
            b, s = em["rnd"].get("particle_life", (1.3, 0.0))
            longest = max(longest, float(b) + abs(float(s)))
    return max(2.0, min(8.0, longest * 2.0 + 0.6))


def configure(fx, view):
    """把视图配置摊平成渲染要用的几张表。"""
    global VIEW, PHASES, MESHES, CAM_DEF, FOLLOW, TRAIL_FRM, TRAIL_TO
    VIEW = view
    n = max(1, len(fx))
    if view.get("auto"):
        total = est_total(fx)
        step = total / n
        PHASES = [dict(label="fx%d" % i, fx=i, frm=i * step, to=(i + 1) * step,
                       anchor=[0.0, 0.0, 0.0], anchor_to=None) for i in range(n)]
        MESHES, CAM_DEF, FOLLOW = False, {"look": [0.0, -0.15, 0.0], "dist": 8.6}, {}
    else:
        PHASES, FOLLOW = [], {}
        for q in view.get("phases", []):
            fx_i = int(q.get("fx", 0))
            ph = dict(label=q.get("label") or ("fx%d" % fx_i), fx=fx_i,
                      frm=float(q.get("from", 0.0)), to=float(q.get("to", 0.0)),
                      anchor=list(q.get("anchor") or [0.0, 0.0, 0.0]),
                      anchor_to=(list(q["anchor_to"]) if q.get("anchor_to") else None))
            PHASES.append(ph)
            if q.get("camera"):
                FOLLOW[fx_i] = q["camera"]
        if not PHASES:                       # sidecar 里没写 phases → 退回自动
            return configure(fx, {"auto": True, "_src": view.get("_src", "?") + "(无 phases)"})
        MESHES = bool(view.get("meshes", False))
        CAM_DEF = view.get("camera") or {"look": [0.0, -0.15, 0.0], "dist": 8.6}
        # 残迹段的飞行区间（替身网格画月牙时要用）
        trail = [p for p in PHASES if p["anchor_to"]] or PHASES[-1:]
        TRAIL_FRM, TRAIL_TO = trail[0]["frm"], trail[0]["to"]

# 材质 → 混合模式（2026-09-18 对 Native 41 个 prt_shd_* 材质实测）
MAT_BLEND = {
    "prt_shd_smoke_1": "combined",          # add_modulate_combined（叠加 + 调制）
    "prt_shd_dust_1": "modulate", "prt_shd_fire_haze_1": "modulate", "prt_shd_fire_nm": "modulate",
    "prt_shd_haze_1": "modulate", "prt_shd_rain_mud_1": "modulate", "prt_shd_rose_a": "modulate",
    "prt_shd_snow_dust_1": "modulate", "prt_shd_waterfall_dust": "modulate", "prt_shd_water_wave_1": "modulate",
    "prt_shd_fire_2": "alpha", "prt_shd_fire_3": "alpha", "prt_shd_steam_1": "alpha", "prt_shd_water_splash2": "alpha",
}


# ===========================================================================
# 解析
# ===========================================================================
def _keys(el):
    if el is None:
        return None
    ks = [(float(k.get("time")), k.get("value")) for k in el.findall("keys/key")]
    return ks or None


def _dec(s, v3):
    if not v3:
        return float(s)
    p = [float(x) for x in s.split(",")]
    return (p[0], p[1], p[2])


def sample(keys, t, v3=False):
    if not keys:
        return (1.0, 1.0, 1.0) if v3 else 1.0
    if t <= keys[0][0]:
        return _dec(keys[0][1], v3)
    if t >= keys[-1][0]:
        return _dec(keys[-1][1], v3)
    for i in range(len(keys) - 1):
        a, b = keys[i], keys[i + 1]
        if a[0] <= t <= b[0]:
            span = b[0] - a[0]
            u = 0.0 if span <= 0 else (t - a[0]) / span
            u = u * u * (3 - 2 * u)
            va, vb = _dec(a[1], v3), _dec(b[1], v3)
            if v3:
                return tuple(va[k] + (vb[k] - va[k]) * u for k in range(3))
            return va + (vb - va) * u
    return _dec(keys[-1][1], v3)


def rand(o):
    return o[0] + (random.random() * 2 - 1) * o[1]


def parse(path):
    root = ET.parse(path).getroot()
    out = []
    for ef in root.findall("effect"):
        E = dict(name=ef.get("name"), emitters=[])
        for i, em in enumerate(ef.findall("emitters/emitter")):
            o = dict(name=em.get("name"), flags={}, num={}, rnd={}, curves={}, color=None, alpha=None)
            for f in em.findall("flags/flag"):
                o["flags"][f.get("name")] = (f.get("value") == "true")
            for p in em.findall("parameters/parameter"):
                n = p.get("name")
                if n == "particle_color":
                    o["color"] = _keys(p.find("color"))
                    o["alpha"] = _keys(p.find("alpha"))
                    continue
                cu = p.find("curve")
                if p.get("base") is not None:
                    b, bi = float(p.get("base")), float(p.get("bias") or 0)
                    if cu is not None:
                        o["curves"][n] = dict(base=b, bias=bi,
                            mult=float(cu.get("curve_multiplier") or 1),
                            keys=_keys(cu.find("keys")))
                    else:
                        o["rnd"][n] = (b, bi)
                elif p.get("value") is not None:
                    o["num"][n] = p.get("value")
            o["gravity"] = tuple(float(x) for x in o["num"].get("gravity", "0,0,0").split(","))
            o["radius"] = float(o["num"].get("emit_sphere_radius", 0) or 0)
            o["maxAlive"] = int(float(o["num"].get("max_alive_particle_count", 0) or 0)) or 1500
            o["inherit"] = float(o["num"].get("inherit_emitter_velocity", 0) or 0)
            o["damping"] = o["rnd"].get("damping", (0, 0))[0]
            o["angDamp"] = o["rnd"].get("angular_damping", (0, 0))[0]
            o["blend"] = MAT_BLEND.get(o["num"].get("material", ""), "add")
            o["material"] = o["num"].get("material", "")
            _sc = (o["num"].get("texture_sprite_count") or "1, 1").split(",")
            try:
                o["grid"] = (max(1, int(float(_sc[0]))),
                             max(1, int(float(_sc[1]))) if len(_sc) > 1 else 1)
            except ValueError:
                o["grid"] = (1, 1)
            # 🔴 序列帧动画（2026-09-24 补）：原版粒子靠 `uses_sprite_animation` + frame_count/rate
            #    在贴图图集里逐帧播放（火苗图集就是 128 帧的火焰动画）。预览以前只画第 0 帧
            #    ⇒ 火焰动画的起手帧又小又暗，看着像"没做出来"（差点误判成材质错）。
            o["anim"] = bool(o["flags"].get("uses_sprite_animation"))
            try:
                o["frame_count"] = int(float(o["num"].get("texture_sprite_frame_count") or 1))
                o["frame_rate"] = float(o["num"].get("texture_sprite_frame_rate") or 0)
            except ValueError:
                o["frame_count"], o["frame_rate"] = 1, 0.0
            E["emitters"].append(o)
        out.append(E)
    return out


# ===========================================================================
# 锚点：粒子从哪儿发出来（由 VIEW 的 phases 决定；自动模式一律原点）
# ===========================================================================
def _phase_of(fx_i):
    for p in PHASES:
        if p["fx"] == fx_i:
            return p
    return PHASES[0] if PHASES else None


def anchor(fx_i, t):
    p = _phase_of(fx_i)
    if p is None:
        return (0.0, 0.0, 0.0)
    a = p["anchor"]
    if not p["anchor_to"]:
        return tuple(a)
    span = max(1e-6, p["to"] - p["frm"])
    u = max(0.0, min(1.0, (t - p["frm"]) / span))
    b = p["anchor_to"]
    return tuple(a[k] + (b[k] - a[k]) * u for k in range(3))


def emit_vel(fx_i, t):
    """发射点自身的速度 —— 只有「沿路径飞」的阶段才有；粒子靠继承它才拉得出尾迹。"""
    p = _phase_of(fx_i)
    if p is None or not p["anchor_to"]:
        return (0.0, 0.0, 0.0)
    a, b = anchor(fx_i, t - 0.02), anchor(fx_i, t + 0.02)
    return tuple((b[k] - a[k]) / 0.04 for k in range(3))


def trail_pos(t):
    """残迹段的飞行路径（只有替身网格画月牙时用得到）。"""
    for p in PHASES:
        if p["anchor_to"]:
            return anchor(p["fx"], t)
    return (0.0, 0.0, 0.0)


# ===========================================================================
# 相机
# ===========================================================================
class Cam:
    def __init__(self, theta, phi, dist, look):
        sp, cp = math.sin(phi), math.cos(phi)
        self.pos = (look[0] + dist * sp * math.cos(theta),
                    look[1] + dist * cp,
                    look[2] + dist * sp * math.sin(theta))
        f = [(look[k] - self.pos[k]) for k in range(3)]
        n = math.sqrt(sum(x * x for x in f)) or 1
        self.fwd = tuple(x / n for x in f)
        up = (0.0, 1.0, 0.0)
        r = (self.fwd[1] * up[2] - self.fwd[2] * up[1],
             self.fwd[2] * up[0] - self.fwd[0] * up[2],
             self.fwd[0] * up[1] - self.fwd[1] * up[0])
        n = math.sqrt(sum(x * x for x in r)) or 1
        self.right = tuple(x / n for x in r)
        self.up = (self.right[1] * self.fwd[2] - self.right[2] * self.fwd[1],
                   self.right[2] * self.fwd[0] - self.right[0] * self.fwd[2],
                   self.right[0] * self.fwd[1] - self.right[1] * self.fwd[0])
        self.f = (H * 0.5) / math.tan(math.radians(FOV) * 0.5)

    def project(self, p):
        d = (p[0] - self.pos[0], p[1] - self.pos[1], p[2] - self.pos[2])
        z = sum(d[k] * self.fwd[k] for k in range(3))
        if z <= 0.05:
            return None
        x = sum(d[k] * self.right[k] for k in range(3))
        y = sum(d[k] * self.up[k] for k in range(3))
        return (W * 0.5 + x * self.f / z, H * 0.5 - y * self.f / z, z)


# ===========================================================================
# 精灵 —— 🔴 smoke_d 是一张 2×2 图集（texture_sprite_count="2, 2"），
#        必须按格切；把整张当单颗粒子用 = 画出一坨多瓣的灰块（踩过）。
#        select_random_sprite=true → 每颗粒子随机挑一格。
# ===========================================================================
SIZES = (8, 12, 16, 24, 32, 48, 64, 96, 128)


def load_sprites(png, cols=2, rows=2):
    im = Image.open(png).convert("RGBA")
    cw, ch = im.width // cols, im.height // rows
    cells, means = [], []
    for r in range(rows):
        for c in range(cols):
            cell = im.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
            arr = np.asarray(cell, dtype=np.float32) / 255.0
            # 贴图**平均色**（只在 --tex-rgb 时参与调色）：火焰贴图是橙的、雪是白的、血是红的
            a = arr[:, :, 3:4]
            w = float(a.sum()) or 1.0
            mean = tuple(float((arr[:, :, i:i + 1] * a).sum() / w) for i in range(3))
            means.append(mean)
            cells.append({s: np.asarray(cell.resize((s, s), Image.BILINEAR).split()[3],
                                        dtype=np.float32) / 255.0 for s in SIZES})
    return cells, means


def sprite_set(mat, grid):
    """按材质取它的贴图图集（带缓存）。材质贴图缺失 → 退回默认烟贴图。

    网格（几列几行）用**发射器自己写的** texture_sprite_count —— 同一张贴图被不同
    发射器按不同格数切是合法的，所以缓存键 = (材质, 列, 行)。
    """
    key = (mat or "", grid)
    if key in _SPRITE_SETS:
        return _SPRITE_SETS[key]
    gx, gy = grid
    png = MAT_TEX.get(mat or "") or TEX_DEFAULT
    try:
        res = load_sprites(png, gx, gy)
    except Exception:
        res = load_sprites(TEX_DEFAULT, gx, gy)
    _SPRITE_SETS[key] = res
    return res


def build_mat_tex(matdir, matdefdir=None):
    """材质名 -> 贴图 PNG 路径。两步走（2026-09-24）：

      ① `preview/mats/<材质>.mat.txt` 给出「**材质 → 贴图名**」（原版 dump 的材质定义里有 tex[0]）；
      ② 贴图目录（`--matdir`）给出「**贴图名 → PNG**」（dump 会按类别丢进子目录 ⇒ 递归扫）。

    两张表缺一不可 —— 只扫目录会把「材质名」（prt_shd_fire_1）当成贴图名去找，永远命中不了。
    """
    if not matdir or not os.path.isdir(matdir):
        return {}
    png = {}
    for f in glob.glob(os.path.join(matdir, "**", "*.png"), recursive=True):
        png.setdefault(os.path.splitext(os.path.basename(f))[0], f)
    matdefdir = matdefdir or os.path.join(HERE, "mats")
    out = {}
    for f in glob.glob(os.path.join(matdefdir, "*.mat.txt")):
        mat = os.path.basename(f)[:-len(".mat.txt")]
        try:
            text = open(f, encoding="utf-8-sig").read()
        except OSError:
            continue
        m = re.findall(r"tex\[\d+\] = \S+ \(([^)]+)\)", text)
        if m and m[0] in png:
            out[mat] = png[m[0]]
    return out


def pick(want):
    for s in SIZES:
        if s >= want:
            return s
    return SIZES[-1]


# ===========================================================================
# 合成
# ===========================================================================
def blit(canvas, mask, color, alpha, x0, y0, mode):
    h, w = mask.shape
    x1, y1 = x0 + w, y0 + h
    cx0, cy0 = max(0, x0), max(0, y0)
    cx1, cy1 = min(W, x1), min(H, y1)
    if cx0 >= cx1 or cy0 >= cy1:
        return
    m = mask[cy0 - y0:cy1 - y0, cx0 - x0:cx1 - x0]
    a = (m * alpha)[:, :, None]
    c = np.asarray(color, dtype=np.float32)
    region = canvas[cy0:cy1, cx0:cx1]
    if mode == "add":
        # 纯加法：只能变亮 —— 🔴 在亮背景上几乎看不见
        np.add(region, c * a, out=region)
    elif mode == "modulate":
        # 乘法压暗：alpha=0 处乘 1（不变），alpha=1 处乘 color
        np.multiply(region, (1.0 - a) + c * a, out=region)
    elif mode == "combined":
        # add_modulate_combined（prt_shd_smoke_1 用的就是它）：
        # 近似 = 常规 alpha 混合 + 一份加法增益。
        # ⚠️ 引擎的确切公式未知；纯加法会让亮背景上的效果消失（踩过），
        #    所以这里必须带 alpha 那一半，否则预览失真。
        region *= (1.0 - a)
        region += c * a * 1.55
    else:  # alpha
        region *= (1.0 - a)
        region += c * a


def draw_figure(canvas, color):
    """人形剪影 —— 只提供尺度感。
       🔴 线宽必须**投影换算**（世界半径 → 像素），否则 1.8 米的人只剩几根 2px 的头发丝（踩过）。"""
    from PIL import ImageDraw
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)

    def seg(p0, p1, r):
        a, b = cam.project(p0), cam.project(p1)
        if a and b:
            z = (a[2] + b[2]) * 0.5
            wpx = max(2, int(r * 2 * cam.f / z))
            d.line([a[0], a[1], b[0], b[1]], fill=255, width=wpx)

    def ball(p, r):
        a = cam.project(p)
        if a:
            rr = max(2.0, r * cam.f / a[2])
            d.ellipse([a[0] - rr, a[1] - rr, a[0] + rr, a[1] + rr], fill=255)

    seg((-0.11, -2.2, 0), (-0.11, -1.2, 0), 0.10)
    seg((0.11, -2.2, 0), (0.11, -1.2, 0), 0.10)
    seg((0, -1.25, 0), (0, -0.50, 0), 0.19)
    ball((0, -0.33, 0), 0.135)
    seg((0.10, -0.45, 0), (0.30, 0.40, 0), 0.065)
    ball((0.36, 0.60, 0), 0.075)
    mm = np.asarray(m, dtype=np.float32)[:, :, None] / 255.0
    canvas[:] = canvas * (1 - mm * 0.92) + np.asarray(color, dtype=np.float32) * (mm * 0.92)


def draw_crescent(canvas, t):
    from PIL import Image, ImageDraw, ImageFilter
    if not (TRAIL_FRM <= t <= TRAIL_TO):
        return
    p = trail_pos(t)
    R, thick, arc, segs = 1.0, 0.030, math.pi * 0.86, 96
    SCALE = 1.95
    u2 = (t - TRAIL_FRM) / (TRAIL_TO - TRAIL_FRM)
    fade = min(1.0, u2 * 6) * max(0.0, 1 - u2 ** 2.2)
    if fade <= 0.01:
        return
    for (th, col, op) in ((thick * 7.0, (0.98, 0.10, 0.24), 0.60 * fade),
                          (thick, (1.0, 0.85, 0.82), 0.95 * fade)):
        m = Image.new("L", (W, H), 0)
        d = ImageDraw.Draw(m)
        poly = []
        for i in range(segs + 1):
            u = i / segs
            a = -arc / 2 + arc * u
            w = th * math.sin(math.pi * u)
            poly.append((p[0] + math.cos(a) * (R - w) * SCALE,
                         p[1] + math.sin(a) * (R - w) * SCALE, p[2]))
        for i in range(segs, -1, -1):
            u = i / segs
            a = -arc / 2 + arc * u
            w = th * math.sin(math.pi * u)
            poly.append((p[0] + math.cos(a) * (R + w) * SCALE,
                         p[1] + math.sin(a) * (R + w) * SCALE, p[2]))
        pts = [cam.project(q) for q in poly]
        if any(q is None for q in pts):
            return
        d.polygon([(q[0], q[1]) for q in pts], fill=255)
        m = m.filter(ImageFilter.GaussianBlur(3 if th < 0.1 else 11))
        blit(canvas, np.asarray(m, dtype=np.float32) / 255.0, col, op,
             0, 0, "add")


def draw_shell(canvas, t):
    from PIL import Image, ImageDraw, ImageFilter
    if not (1.50 <= t <= 2.40):
        return
    u = (t - 1.50) / 0.90
    r = 0.4 + 6.6 * (u ** 0.55)
    op = max(0.0, 1 - u ** 1.5) * 0.9
    if op <= 0.01:
        return
    c = cam.project(anchor(1, t))          # 2026-09-21 修：CENTER 未定义 → 用「爆开」阶段(fx=1)的锚点
    if not c:
        return
    rr = r * cam.f / c[2]
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([c[0] - rr, c[1] - rr, c[0] + rr, c[1] + rr], outline=255, width=max(2, int(rr * 0.09)))
    for k in range(28):                       # 经线
        a0 = k / 28 * math.pi * 2
        d.line([c[0] + math.cos(a0) * rr * 0.18, c[1] + math.sin(a0) * rr * 0.18,
                c[0] + math.cos(a0) * rr, c[1] + math.sin(a0) * rr], fill=90, width=2)
    m = m.filter(ImageFilter.GaussianBlur(5))
    blit(canvas, np.asarray(m, dtype=np.float32) / 255.0, (1.0, 0.35, 0.43), op, 0, 0, "add")


def draw_ring(canvas, t):
    from PIL import Image, ImageDraw
    if t > 1.85:
        return
    op = min(1.0, t / 0.7) * 0.85 if t < 1.5 else max(0.0, 1 - (t - 1.5) / 0.35) * 0.85
    if op <= 0.01:
        return
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    for R in (3.68, 2.23):
        pts = []
        for i in range(97):
            a = i / 96 * math.pi * 2
            q = cam.project((math.cos(a) * R, -2.16, math.sin(a) * R))
            if q is None:
                pts = []
                break
            pts.append((q[0], q[1]))
        if len(pts) > 2:
            d.line(pts + [pts[0]], fill=255, width=2)
    blit(canvas, np.asarray(m, dtype=np.float32) / 255.0, (0.16, 0.10, 0.13), op, 0, 0, "modulate")


# ===========================================================================
# 主
# ===========================================================================
cam = None

def render(xml_path, tex_path, t_end, out_path, view_path=None, matdir=None, tex_rgb=False):
    global cam, TEX_DEFAULT, TEX_RGB
    TEX_DEFAULT = tex_path
    TEX_RGB = tex_rgb
    MAT_TEX.clear()
    MAT_TEX.update(build_mat_tex(matdir or MAT_DIR_DEF))
    fx = parse(xml_path)
    view = load_view(xml_path, view_path)
    configure(fx, view)
    print("视图配置: %s" % view["_src"])
    ph = None
    for p in PHASES:
        if p["frm"] <= t_end < p["to"]:
            ph = p
    if ph is None:
        ph = PHASES[0] if t_end < PHASES[0]["to"] else PHASES[-1]

    # 相机：默认固定机位（眼平高度、微俯视）；某个阶段可以在 sidecar 里给自己配机位。
    # 残迹段之所以要专用机位：跟着飞行物跑会让它整段飞出画面
    #（投影 x=1481~2539，画布才 1100 宽，踩过）；固定取景 = 人和特效同框。
    _c = FOLLOW.get(ph["fx"]) or CAM_DEF
    look = list(_c.get("look", [0.0, -0.15, 0.0]))
    dist = float(_c.get("dist", 8.6))
    cam = Cam(0.85, 1.40, dist, look)

    # 背景：亮天空 + 地面 + 网格（地面与网格用两张独立掩码，否则网格线会被地面盖掉）
    sky = np.array([0.624, 0.706, 0.784], dtype=np.float32)
    gnd = np.array([0.745, 0.784, 0.816], dtype=np.float32)
    canvas = np.tile(sky, (H, W, 1))
    from PIL import Image as I2, ImageDraw as D2
    mg = I2.new("L", (W, H), 0)          # 地面
    ml = I2.new("L", (W, H), 0)          # 网格线
    dg, dl = D2.Draw(mg), D2.Draw(ml)
    poly = []
    for (X, Z) in ((-90, -90), (90, -90), (90, 90), (-90, 90)):
        q = cam.project((X, -2.2, Z))
        if q:
            poly.append((q[0], q[1]))
    if len(poly) >= 3:
        dg.polygon(poly, fill=255)
    for i in range(-10, 11):
        for pts in (((-10, -2.2, i), (10, -2.2, i)), ((i, -2.2, -10), (i, -2.2, 10))):
            a, b = cam.project(pts[0]), cam.project(pts[1])
            if a and b:
                dl.line([a[0], a[1], b[0], b[1]], fill=170, width=1)
    GM = np.asarray(mg, dtype=np.float32)[:, :, None] / 255.0
    GL = np.asarray(ml, dtype=np.float32)[:, :, None] / 255.0
    canvas[:] = canvas * (1 - GM) + gnd * GM
    canvas[:] = canvas * (1 - GL * 0.55) + np.array([0.44, 0.50, 0.56], dtype=np.float32) * (GL * 0.55)

    if MESHES:
        draw_ring(canvas, t_end)          # 阴魔斩那套替身网格：只有 sidecar 明确要时才画
    draw_figure(canvas, (0.87, 0.89, 0.91))   # 人形 = 1.8m 尺度基准，永远画

    # 粒子：从 phase 起点积到 t_end（贴图按材质取，见 sprite_set）
    dt = 1.0 / 60
    t0 = ph["frm"]
    ems = fx[ph["fx"]]["emitters"]
    state = [dict(acc=0.0, live=[]) for _ in ems]
    steps = int((t_end - t0) / dt)
    for s in range(steps + 1):
        t = t0 + s * dt
        org, ev = anchor(ph["fx"], t), emit_vel(ph["fx"], t)
        for k, em in enumerate(ems):
            st = state[k]
            rate = rand(em["rnd"].get("emission_rate", (0, 0)))
            st["acc"] += rate * dt
            g = 0
            while st["acc"] >= 1 and len(st["live"]) < em["maxAlive"] and g < 600:
                st["acc"] -= 1
                g += 1
                dx, dy, dz = (random.random() * 2 - 1 for _ in range(3))
                n = math.sqrt(dx * dx + dy * dy + dz * dz) or 1
                rad = em["radius"] * (random.random() ** (1 / 3))
                p = dict(
                    x=org[0] + dx / n * rad, y=org[1] + dy / n * rad, z=org[2] + dz / n * rad,
                    vx=rand(em["rnd"].get("emit_velocity_x", (0, 0))) + ev[0] * em["inherit"],
                    vy=rand(em["rnd"].get("emit_velocity_y", (0, 0))) + ev[1] * em["inherit"],
                    vz=rand(em["rnd"].get("emit_velocity_z", (0, 0))) + ev[2] * em["inherit"],
                    age=0.0, life=max(0.02, rand(em["rnd"].get("particle_life", (1, 0)))))
                psz = em["rnd"].get("particle_size") or em["curves"].get("particle_size")
                p["s0"] = rand((psz["base"], psz["bias"])) if psz else 0.2
                st["live"].append(p)
            if st["acc"] > 6:
                st["acc"] = 6
            for p in list(st["live"]):
                damp = math.exp(-em["damping"] * dt)
                p["vx"] *= damp; p["vy"] *= damp; p["vz"] *= damp
                p["vx"] += em["gravity"][0] * dt
                p["vy"] += em["gravity"][1] * dt
                p["vz"] += em["gravity"][2] * dt
                p["x"] += p["vx"] * dt; p["y"] += p["vy"] * dt; p["z"] += p["vz"] * dt
                p["age"] += dt
                if p["age"] >= p["life"]:
                    st["live"].remove(p)

    # 深度排序后合成（alpha/modulate 对顺序敏感）
    items = []
    for k, em in enumerate(ems):
        sset, smean = sprite_set(em.get("material", ""), em.get("grid", (1, 1)))  # 🔴 每材质自己的贴图
        for p in state[k]["live"]:
            q = cam.project((p["x"], p["y"], p["z"]))
            if q is None:
                continue
            u = p["age"] / p["life"]
            size = p["s0"]
            cv = em["curves"].get("particle_size")
            if cv:
                size += sample(cv["keys"], u, False) * cv["mult"]
            col = sample(em["color"], u, True) if em["color"] else (1, 1, 1)
            if em.get("anim") and em.get("frame_count", 1) > 1:
                # 序列帧：按**绝对年龄**推进（不是生命比例）—— 引擎就是这么放动画的
                ci = int(p["age"] * em.get("frame_rate", 0.0)) % len(sset)
            else:
                ci = random.randrange(len(sset)) if em["flags"].get("select_random_sprite") else 0
            if TEX_RGB:      # 贴图平均色参与调色（火焰→橙、雪→白、血→红）
                col = tuple(min(1.0, col[i] * smean[ci][i]) for i in range(3))
            a = sample(em["alpha"], u, False) if em["alpha"] else 1.0
            px = max(1, int(size * cam.f / q[2]))
            items.append((q[2], px, col, a, q[0], q[1], em["blend"], ci,
                          em.get("material", ""), em.get("grid", (1, 1))))
    items.sort(key=lambda z: -z[0])
    for (_, px, col, a, sx, sy, bl, ci, mat, grid) in items:
        s = pick(px)
        m = sprite_set(mat, grid)[0][ci][s]
        half = s * 0.5
        blit(canvas, m, col, a, int(sx - half), int(sy - half), bl)

    if MESHES:
        draw_shell(canvas, t_end)
        draw_crescent(canvas, t_end)

    img = Image.fromarray((np.clip(canvas, 0, 1) * 255).astype(np.uint8))
    img.save(out_path)
    print("%s  t=%.2fs  phase=%s  粒子=%d  → %s  (%dx%d)"
          % (os.path.basename(out_path), t_end, ph["label"],
             sum(len(s["live"]) for s in state), out_path, W, H))


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="离线静帧渲染（不需要浏览器）")
    ap.add_argument("--t", type=float, required=True, help="渲染时刻（秒）")
    ap.add_argument("--out", required=True, help="输出 PNG")
    ap.add_argument("--xml", required=True, help="粒子 XML（想看阴魔斩样张：先用 examples/yinmo_spec.py 生成一个）")
    ap.add_argument("--tex", default=os.path.join(HERE, "smoke_d_256.png"))
    ap.add_argument("--view", default=None,
                    help="视图配置 JSON（默认自动找同名 <xml>.view.json；都没有 = 自动模式）")
    ap.add_argument("--matdir", default=None,
                    help="材质贴图目录（默认 ../out/mattex_all；由 Debug/offline/_mat_tex_survey.py 生成）")
    ap.add_argument("--tex-rgb", action="store_true",
                    help="让贴图自带颜色参与调色（火焰→橙、雪→白、血→红）。默认关，保持阴魔斩基准不变")
    a = ap.parse_args()
    render(a.xml, a.tex, a.t, a.out, a.view, a.matdir, a.tex_rgb)
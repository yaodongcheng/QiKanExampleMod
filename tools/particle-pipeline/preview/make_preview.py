#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""make_preview.py —— 把骑砍粒子 XML 变成【离线自包含】的 three.js 预览页。

这是 preview.template.html 一直缺的「注入脚本」：模板里有两个占位符
    __FX_XML__          → 粒子 XML 正文（支持一个文件里多个 <effect>）
    __SMOKE_TEX_B64__   → smoke_d 贴图 base64
本脚本负责填它们，并把模板**通用化**成「任意 XML 都能看」的预览器。

为什么需要通用化（原模板只服务 yinmo 三段式那一个 demo）：
  1) `PL`（时间线）是硬编码的 3 段（蓄力/爆开/残迹），锚点在举手的 HAND、中心的 CENTER、
     以及一条 13 m 飞行直线上 —— 换一个 XML 就完全对不上；
  2) `stepSystems` 里 `active = ph.fx===S.fxIdx` 只让「当前阶段」发射 → 多 effect 只亮第一个；
  3) 原模板 three.js 走 CDN → **离线打不开**；
  4) 🔴 轴向：XML 是 **Z-up**（gravity 默认 `0,0,-1`），而 three.js 场景是 **Y-up**。
     原 demo 把所有 gravity 都写成 0，把这个不一致掩盖了；真实 XML 必须做重映射
     xml(x,y,z) → scene(x, z, -y)。

用法：
  python make_preview.py --xml a.xml b.xml -o out.html --title "火系"
  python make_preview.py --xml a.xml -o out.html --three none     # 保留 CDN（需联网）
"""
import os, sys, re, base64, argparse
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
TPL  = os.path.join(HERE, "preview.template.html")
SMOKE = os.path.join(HERE, "smoke_d_256.png")
# three.js 解析顺序：**自带 vendor** → `--three` 指定 → CDN（需联网）。
# 🔴 vendor 那份是唯一保证「换台机器照样离线可开」的来源：早先这里写死的是
#    另一个工程的 D 盘路径（D:/BrainMaker/shokuho_rebuild/...），换机器就静默
#    退化成 CDN —— 脚本不报错，页面却是"必须联网才打得开"，属于最难发现的一类。
THREE_CANDIDATES = [
    os.path.join(HERE, "vendor", "three.min.js"),
]

# 数据根（paths.py，工具链共用）：只为给 `--tex-dir` 兜一个默认值。
# 单独把这个文件拷走也能跑 —— 那种情况下没有 paths，走命令行显式传参即可。
try:
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
    import paths
except ImportError:
    paths = None

def log(*a):
    print(*a)

# ---------------------------------------------------------------- XML 合并
def merge_xml(paths):
    """把多个 XML 的 <effect> 合并到一个 <particle_effects> 根下。"""
    root = ET.Element("particle_effects")
    names = []
    for p in paths:
        try:
            r = ET.parse(p).getroot()
        except Exception as e:
            log("  !! 跳过 %s : %s" % (os.path.basename(p), e)); continue
        for eff in r.findall("effect"):
            root.append(eff)
            names.append((eff.get("name") or "?", p,
                          len(eff.findall("emitters/emitter"))))
    if len(root) == 0:
        raise SystemExit("没有任何 effect 被读入，检查 --xml 路径")
    return root, names

def indent(elem, level=0):
    pad = "\n" + "\t" * level
    if len(elem):
        if not (elem.text or "").strip(): elem.text = pad + "\t"
        for c in elem: indent(c, level + 1)
        if not (elem.tail or "").strip(): elem.tail = pad
        if not (c.tail or "").strip(): c.tail = pad
    else:
        if level and not (elem.tail or "").strip(): elem.tail = pad

def xml_text(root):
    indent(root)
    body = ET.tostring(root, encoding="unicode")
    return '<?xml version="1.0" encoding="utf-8"?>\n' + body + "\n"

# ---------------------------------------------------------------- 贴图
def _sheet_grid(im, size=256):
    """判断这张粒子贴图是不是「图集 / 序列帧」→ 返回 (cols, rows)；不是就 (1,1)。

    🔴 为什么必须判：着色器是 `texture2D(uTex, gl_PointCoord)` —— **整张贴图当一颗粒子画**。
       而原版 `smoke_d` 是 **2×2 图集**（四个不同的烟团），UE 的 `T_Fire_01` 更是 **8×8 序列帧**。
       整张喂进去 = 一个粒子画出 4 个 / 64 个（2026-09-20 用户实测截图：四个白烟团）。
       正确做法与引擎同口径：按 `texture_sprite_count` 切格，每颗取一格 —— 这里在内嵌前**先切出一格**。

    判据（两条都要过）：
      ① **分隔线上没墨** —— 真图集的分隔线走在帧与帧的空隙里，单帧图的分隔线会**穿过内容**
         （中心那块最亮）。🔴 第一版只用「各格墨量接近」，结果**居中对称的单帧图**（径向光晕、
         居中烟团）被四等分后墨量天然相等 → 全被误判成图集（连 T_Glow 都判成 2×2），已废。
      ② 各格墨量接近（每格一帧，长得像）。
    从细到粗试（8→4→3→2），取第一个通过的 —— 8×8 的序列帧不会被误判成 2×2。
    """
    import numpy as np
    a = np.asarray(im.convert("RGBA").resize((size, size))).astype(np.float32)
    al = a[..., 3]
    if al.mean() > 250:                    # 无有效 alpha（UE 贴图常态）→ 用亮度当形状
        al = a[..., :3].max(axis=2)
    total = float(al.mean())
    if total < 1.0:                        # 整张几乎是空的
        return 1, 1
    for k in (8, 4, 3, 2):
        step = size // k
        if step < 8:
            continue
        lines = []
        for c in range(1, k):
            x = c * step
            lines.append(al[:, max(0, x - 1):x + 2].mean())     # 竖分隔线
            lines.append(al[max(0, x - 1):x + 2, :].mean())     # 横分隔线
        li = float(sum(lines)) / len(lines)
        if li > 0.30 * total:              # 分隔线穿过内容 → 不是图集
            continue
        ink = [al[r * step:(r + 1) * step, c * step:(c + 1) * step].mean()
               for r in range(k) for c in range(k)]
        m = float(sum(ink)) / len(ink)
        if m < 1.0:
            continue
        var = (sum((x - m) ** 2 for x in ink) / len(ink)) ** 0.5
        if var / (m + 1e-6) < 0.5:         # 各格墨量接近 → 是图集
            return k, k
    return 1, 1


def _pick_cell(im, cols, rows):
    """从图集里挑「墨最多」的那一格（避开空帧 / 全黑首帧）。"""
    if cols <= 1 and rows <= 1:
        return im
    import numpy as np
    w, h = im.size
    cw, ch = w // cols, h // rows
    best, best_ink = None, -1.0
    for r in range(rows):
        for c in range(cols):
            cell = im.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
            a = np.asarray(cell.convert("RGBA")).astype(np.float32)
            ink = float(a[..., 3].mean())
            if a[..., 3].mean() > 250:     # 无 alpha → 用亮度
                ink = float(a[..., :3].max(axis=2).mean())
            if ink > best_ink:
                best_ink, best = ink, cell
    return best or im


def crop_sheet(im, tag=""):
    """图集 → 切出一格；单帧原样返回。返回 (图, 说明文字)。"""
    g = _sheet_grid(im)
    if g == (1, 1):
        return im, ""
    return _pick_cell(im, *g), "%s×%s 图集 → 取其中一格" % (g[0], g[1])


def _tex_b64_cropped(path):
    """默认贴图：先按图集切一格，再编成 base64（默认贴图已是 RGB白+alpha 约定，不做形状转换）。"""
    import io as _io
    from PIL import Image
    im = Image.open(path).convert("RGBA")
    im, note = crop_sheet(im)
    buf = _io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return base64.b64encode(buf.getvalue()).decode("ascii"), note


def b64_of(path):
    with open(path, "rb") as f:
        return base64.b64encode(f.read()).decode("ascii")

def three_js_inline(explicit):
    """把 three.js 内联进去 —— 保证离线可开。返回 (js文本, 来源说明)。"""
    if explicit and explicit.lower() == "none":
        return None, "CDN(需联网)"
    if explicit:
        if not os.path.exists(explicit):
            log("  !! --three 指定的文件不存在: %s" % explicit)
        else:
            return open(explicit, encoding="utf-8", errors="replace").read(), explicit
    for c in THREE_CANDIDATES:
        if os.path.exists(c):
            return open(c, encoding="utf-8", errors="replace").read(), c
    return None, "CDN(需联网: 未找到 %s)" % THREE_CANDIDATES[0]


# ---------------------------------------------------------------- 按材质配贴图
# 预览器默认所有 emitter 共用一张 smoke_d（运动模拟器口径）。但「像不像」很大程度取决于贴图，
# 手上又有 UE 源贴图（262 张），所以按材质名配一张代表性贴图，内嵌成 data URI。
MAT_TEX_HINT = [
    ("prt_shd_lightning",      ["T_Lightning.png", "Lightning"]),
    ("prt_shd_fire_haze_1",    ["FireNoiseTile", "FireBall_LerpMask", "Fire_01"]),
    ("prt_shd_fire_1",         ["T_Fire_8x8", "T_Fire_01", "T_FireFlipbook", "T_Flame"]),
    ("prt_shd_fire_2",         ["T_Fire_01", "T_Flame"]),
    ("prt_shd_fire_3",         ["T_Fire_01", "T_Flame"]),
    ("prt_shd_flame_1",        ["T_Flame", "T_Fire_01"]),
    ("prt_shd_sparks",         ["T_Spark", "T_Ember", "T_Flare_Spikeball"]),
    ("prt_shd_snow_dust_1",    ["T_Snow", "T_Frost", "T_Ice"]),
    ("prt_shd_snow_fall_1",    ["T_Snow", "T_Frost"]),
    ("prt_shd_water_splash2",  ["T_Splash", "T_Bubble", "T_WaterImpact"]),
    ("prt_shd_water_wave_1",   ["T_Water", "T_Ripple", "T_Splash"]),
    ("prt_shd_steam_2",        ["T_Poison", "T_Smoke_Wispy", "T_Plasma_Smoke"]),
    ("prt_shd_steam_1",        ["T_Smoke_Wispy", "T_Steam", "T_Mist", "T_Inky_smoke"]),
    ("prt_shd_haze_1",         ["T_Inky_Smoke", "T_Smoke", "T_MultiplyDust"]),
    ("prt_shd_dust_1",         ["T_MultiplyDust", "T_Dust", "T_Smoke"]),
    ("prt_shd_smoke_1",        ["T_SmokeFlipbook_01", "T_Smoke", "T_Inky_Smoke"]),
    ("prt_shd_smoke_2",        ["T_Smoke", "T_Inky_Smoke"]),
    ("prt_shd_stone_gravel",   ["T_Stone", "T_GroundCrack", "T_Rock"]),
    ("prt_shd_wood_splinter",  ["T_Wood", "T_Splinter"]),
    ("prt_shd_trail",          ["T_Trail", "T_Ribbon"]),
    ("prt_shd_glow",           ["T_Glow.png", "T_Glow", "T_Flare.png", "T_Flare", "T_Ring"]),
    ("prt_shd_blood_1",        ["T_Blood", "T_SlashLine"]),
]

# 🔴 必须排除的贴图：图集（整张当单帧用 → 满屏方形硬边）、法线图、遮罩图。
#    踩过：prt_shd_fire_1 配到 T_Fire_8x8 → 预览里是一格一格的方块。
BAD_TEX_SUBSTR = ["8x8", "4x4", "2x2", "2x3", "3x2", "flipbook", "_seq", "seq.",
                  "_normal", "_n.png", "_n_", "_d2", "mask", "_df", "_rough",
                  "chromatic", "_spec", "_ao", "depth", "_lut"]

def _to_alpha_shape(im):
    """把 UE 贴图转成骑砍 smoke_d 的约定：**RGB 恒白，alpha 承载形状**。

    🔴 为什么必须转：UE 这批 VFX 贴图绝大多数是 **RGB 无 alpha**（实测 12 张里 11 张
       alpha 全 255、RGB 均值只有 3~66 —— 形状是"暗底上的亮图案"）。
       预览器的片元着色器用 `texture.a` 当形状：直接拿来用 → alpha 恒 1 → 整张贴图
       变成一块【不透明正方形】，画面上就是"满屏方形硬边"（本轮踩过、被视觉模型抓到）。
    """
    import numpy as np
    a = np.asarray(im).astype(np.float32)
    alpha = a[..., 3]
    if alpha.mean() > 250:                      # 无有效 alpha → 用亮度当形状
        lum = a[..., :3].max(axis=2)            # 取通道最大值，对彩色光效更保形
        mx = float(lum.max()) or 1.0
        alpha = np.clip(lum / mx * 255.0, 0, 255)
    else:
        alpha = np.clip(alpha, 0, 255)
    white = np.full_like(alpha, 255.0)
    out = np.dstack([white, white, white, alpha]).astype(np.uint8)
    from PIL import Image as _I
    return _I.fromarray(out, "RGBA")


def build_mat_tex_map(tex_dir, size=256):
    """扫描贴图目录，按材质名挑一张代表性贴图，缩到 size 后 base64 内嵌。"""
    import io as _io
    from PIL import Image
    if not tex_dir or not os.path.isdir(tex_dir):
        return {}
    alln = sorted(os.listdir(tex_dir))
    names = [n for n in alln
             if n.lower().endswith(".png")
             and not any(b in n.lower() for b in BAD_TEX_SUBSTR)]
    out = {}
    for mat, hints in MAT_TEX_HINT:
        pick = None
        for h in hints:
            cands = [n for n in names if h.lower() in n.lower()]
            if cands:
                # 名字最短的最可能是「单帧干净图」，优先
                pick = sorted(cands, key=lambda s: (len(s), s))[0]
                break
        if not pick: continue
        try:
            im = Image.open(os.path.join(tex_dir, pick)).convert("RGBA")
            im, note = crop_sheet(im, pick)          # 图集先切一格，否则一个粒子画出 N 帧
            im = _to_alpha_shape(im)
            im.thumbnail((size, size), Image.LANCZOS)
            buf = _io.BytesIO(); im.save(buf, "PNG", optimize=True)
            out[mat] = (pick, "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii"))
            if note:
                log("     %-30s %s" % (pick, note))
        except Exception as e:
            log("  !! 贴图读取失败 %s: %s" % (pick, e))
    return out

def gen_mat_tex_js(mtm):
    """生成 MAT_TEX 构造块（必须在 makeSystem 之前定义）。"""
    if not mtm:
        return "var MAT_TEX = {};"
    lines = ["/* 按材质配的代表性贴图（--tex-dir）；没配到的仍回落到 smoke_d */",
             "var __MT_SRC__ = {"]
    for k, (nm, uri) in mtm.items():
        lines.append('  "%s": "%s",' % (k, uri))
    lines.append("};")
    lines.append("var MAT_TEX = {};")
    lines.append("Object.keys(__MT_SRC__).forEach(function(k){")
    lines.append("  var t = new THREE.TextureLoader().load(__MT_SRC__[k]);")
    lines.append("  t.minFilter = THREE.LinearFilter; t.magFilter = THREE.LinearFilter;")
    lines.append("  MAT_TEX[k] = t;")
    lines.append("});")
    return chr(10).join(lines)

# ---------------------------------------------------------------- 生成注入 JS
def gen_js(n_eff, cols, spacing, dur, auto, only, human=True, mtm=None, ground=True):
    """生成「通用化」注入块：时间线 / 锚点 / 激活 / 轴向重映射 / 替身网格 / 相机取景。"""
    rows = max(1, (n_eff + cols - 1) // cols)
    # 槽位排在 **XY 平面**（x 横 / y 竖 / z=0），相机正对看 —— 等价于一张"特效贴图墙"。
    #   🔴 早先排在 XZ 平面 + 斜视相机：行的投影在屏幕上重叠，9 个特效糊成中间一条
    #      （被视觉模型抓到"看不出 3x3 网格"）。正对相机 + 竖直排布才是逐格核对的正解。
    cx, cy, cz = 0.0, 0.0, 0.0
    fov = 46.0
    import math as _math
    k = 2.0 * _math.tan(_math.radians(fov / 2.0))          # 距离 d 处的可视高度 = k*d
    aspect = 1.6                                           # 默认 1280x800 / 1600x1000
    need_h = (rows * spacing + 2.0) / k
    need_w = (cols * spacing + 2.0) / (k * aspect)
    dist = max(9.0, need_h, need_w) * 1.10
    # 单特效取景（solo 模式）：按 1×1 算 —— 构图与「只发一个 XML」时一致
    solo_dist = max(6.5, (spacing + 2.0) / k, (spacing + 2.0) / (k * aspect)) * 1.10
    # 🔴 默认就是 solo（只跑一个 effect）。9 个同屏 = 上千粒子 + 685 张 emitter 卡片，
    #    浏览器顶不住（2026-09-20 用户实测反馈「太卡了」）→ 改成一次一个 + 随时切换。
    solo0 = 0
    only_js = ("null" if only is None else str(int(only)))
    # 🔴 用 raw 字符串：块里有 JS 正则转义（\b \s \S \/），非 raw 会被 Python 当成
    #    无效转义序列报警告并原样保留 —— 结果碰巧一样，但太脆。
    return gen_mat_tex_js(mtm) + r"""
/* ================================================================
   LWN 迁移预览 —— 自动注入（make_preview.py 生成，勿手改）
   ----------------------------------------------------------------
   1) 时间线：每个 <effect> 一个槽位；**默认只跑一个**（solo），可切换 / 可切回网格
   2) 锚点：solo 时摆在原点；网格模式按槽位摆放，避免多特效叠在一起
   3) 轴向：XML 是 Z-up，three.js 场景是 Y-up —— xml(x,y,z) -> scene(x, z, -y)
   ================================================================ */
var __N__ = %(n)d, __COLS__ = %(cols)d, __SP__ = %(sp).3f, __DUR__ = %(dur).3f;
var __ONLY__ = %(only)s;
var __GRID_DIST__ = %(dist).2f, __SOLO_DIST__ = %(solo_dist).2f;
var __SOLO__ = %(solo0)d;   /* 当前单独显示的 effect 下标；-1 = 网格模式（全部一起跑）*/

function __slotPos(i){
  var c = i %% __COLS__, r = Math.floor(i / __COLS__);
  /* XY 平面（y 向上）。重力重映射后是 -Y，粒子落在自己格子内，互不串场 */
  return new THREE.Vector3((c - (__COLS__-1)/2) * __SP__,
                           ((%(rows)d-1)/2 - r) * __SP__,
                           0);
}

/* ---- 轴向重映射（必须在 FXS 填好后跑；build 会被包装，重建也生效）---- */
function __remapAll(){
  FXS.forEach(function(E){
    E.emitters.forEach(function(em){
      if (em.gravity){ var g = em.gravity; em.gravity = [g[0], g[2], -g[1]]; }
      var vy = em.rnd.emit_velocity_y, vz = em.rnd.emit_velocity_z;
      if (vy || vz){
        var nvy = vz ? vz.base : 0,  nvyb = (vz && vz.bias) || 0;
        var nvz = vy ? -vy.base : 0, nvzb = (vy && vy.bias) || 0;
        em.rnd.emit_velocity_y = { base:nvy,  bias:nvyb  };
        em.rnd.emit_velocity_z = { base:nvz,  bias:nvzb  };
      }
      if (em.volume){ var v = em.volume; em.volume = [v[0], v[2], -v[1]]; }
    });
  });
}
/* ---- 懒解析：只把「当前这一个」effect 那段 XML 交给 DOMParser ----
   🔴 原版 build() 是 `FXS = parseXml(xmlText)` —— 一页装 99 个 effect 时，等于
      DOMParser 吃 5.4MB XML + 约 5 万次 querySelectorAll（685 emitter × 76 次查询），
      主线程直接堵死（用户实测「进入就卡死」）。切换时重解析只花几毫秒。
   做法：先把原文按 <effect> 切成段，再按需喂给原 parseXml。*/
var __CHUNKS__ = (function(){
  var out = [], re = /<effect\b[\s\S]*?<\/effect>/g, m;
  while ((m = re.exec(xmlText)) !== null) out.push(m[0]);
  return out;
})();
var __NAMES__ = __CHUNKS__.map(function(c){
  var m = c.match(/<effect[^>]*\bname="([^"]*)"/);
  return m ? m[1] : "?";
});
function __parseOne(fi){
  var c = __CHUNKS__[fi];
  if (!c) return [];
  return parseXml('<?xml version="1.0" encoding="utf-8"?>' + String.fromCharCode(10)
                  + '<particle_effects>' + String.fromCharCode(10) + c
                  + String.fromCharCode(10) + '</particle_effects>');
}
/* 只把当前 effect 放进 FXS（下标保持原序号，下游 FXS[pl.fx] 才不会错位）。
   Array.forEach 会跳过空槽 → __remapAll / __buildSystems 天然只处理当前这一个。*/
function __loadFx(){
  if (__SOLO__ < 0){ FXS = parseXml(xmlText); return; }   // 网格模式：全部解析（慢，用户自己点的）
  var r = __parseOne(__SOLO__);
  FXS = [];
  if (r[0]) FXS[__SOLO__] = r[0];
}
function __loadFxInit(){
  if (__SOLO__ < 0) return parseXml(xmlText);
  var r = __parseOne(__SOLO__), arr = [];
  if (r[0]) arr[__SOLO__] = r[0];
  return arr;
}

var __origBuild = build;
build = function(){
  __origBuild();          // 解析 XML -> FXS / systems（两行都被本脚本打了补丁：懒解析 + 懒建系统）
  __remapAll();           // XML 是 Z-up，场景是 Y-up
  __buildPL();            // PL 必须在 FXS 填好后建
  renderUI();             // 重建 emitter 卡片（renderUI 自带 innerHTML=""，可重复调）
  __renderPhases();       // 重建切换条（原版用的是旧 PL）
};

/* ---- 只给当前 effect 建粒子系统 ----
   🔴 原版是「把全部 effect 的 emitter 一次全建成 Points」：一页装 99 个 effect =
      685 个 BufferGeometry + 685 个 Points 常驻显存与绘制队列，浏览器直接卡死
      （用户实测反馈「太卡了」）。
   改成按需建 + 切换时销毁上一个：无论一页装多少个 effect，常驻的永远只有当前这一个。
   代价：切换时重建（几毫秒），换来加载与帧率与「一个 effect」等量级。*/
function __buildSystems(){
  systems.forEach(function(S){
    scene.remove(S.points);
    if (S.geo && S.geo.dispose) S.geo.dispose();
    if (S.mat && S.mat.dispose) S.mat.dispose();
  });
  systems.length = 0;
  FXS.forEach(function(E, ei){
    if (__SOLO__ >= 0 && ei !== __SOLO__) return;      // 网格模式才建全部
    E.emitters.forEach(function(em){ var s = makeSystem(em); s.fxIdx = ei; systems.push(s); });
  });
}

/* ---- 只给「当前这一个 effect」建卡片 ----
   同一个原因：99 个 effect 的卡片一次全建出来，DOM 就够卡一会儿。
   做法：临时把 PL 换成只含当前 effect 的子集喂给原 renderUI，建完换回来。*/
var __origRenderUI = renderUI;
renderUI = function(){
  var full = PL.slice();
  if (__SOLO__ >= 0 && PL[__SOLO__]) PL = [PL[__SOLO__]];
  __origRenderUI();
  PL = full;
  var fx = document.getElementById("fxid");
  if (fx){
    fx.textContent = (__SOLO__ >= 0 && FXS[__SOLO__])
      ? ((__SOLO__ + 1) + "/" + __N__ + " · " + FXS[__SOLO__].name + " · "
         + FXS[__SOLO__].emitters.length + " emitters")
      : (__N__ + " 个 effect · " + systems.length + " emitters");
  }
};

/* ---- 时间线 / 激活 ----
   🔴 必须在 build() 之后才能建 PL：FXS 是 build() 里才填的，
      在 build() 之前跑 FXS.forEach 会遍历空数组 → PL 为空 →
      右侧 emitter 卡片全没了（本轮踩过）。所以统一放进 build 包装里。*/
function __buildPL(){
  PL.length = 0;
  /* 名字从切好的 XML 段里取，不从 FXS 取 —— FXS 现在只装当前这一个 effect */
  for (var i = 0; i < __NAMES__.length; i++)
    PL.push({ key:"fx"+i, label:__NAMES__[i], from:0, to:__DUR__, fx:i });
  T.total = __DUR__;
}

/* ---- 切换条：一次只跑一个 effect，可自由切 ----
   ◀ / 下拉列表 / ▶  （键盘 ← → 同效）+「全部」切回网格模式。
   原版是「阶段胶囊」（每段一个）；多 effect 场景下改成这个切换器。*/
function __switch(i){
  if (__N__ <= 0) return;
  if (i < 0) i = __N__ - 1;
  if (i >= __N__) i = 0;
  __SOLO__ = i;
  T.t = 0;
  systems.forEach(function(s){ s.live.length = 0; s.acc = 0; });   // 清干净再播
  __loadFx();            // 换 effect = 解析新的一段（只这一段）
  __buildSystems();      // 换 effect = 换系统（旧的销毁，新的建起来）
  __renderPhases();
  renderUI();
}
function __renderPhases(){
  var h = document.getElementById("phases"); if (!h) return;
  h.innerHTML = "";
  if (__N__ <= 1) return;                       // 只有一个 effect 就不用切换器
  var bar = document.createElement("div");
  bar.style.cssText = "display:flex;align-items:center;gap:6px;flex-wrap:wrap;"
    + "background:rgba(10,9,18,.74);border:1px solid var(--line);border-radius:10px;"
    + "padding:6px 9px;backdrop-filter:blur(6px)";
  function mkbtn(txt, tip, fn){
    var b = document.createElement("button");
    b.textContent = txt; b.title = tip;
    b.style.cssText = "padding:4px 10px;cursor:pointer";
    b.addEventListener("click", fn);
    return b;
  }
  bar.appendChild(mkbtn("◀", "上一个（键盘 ←）",
    function(){ __switch((__SOLO__ < 0 ? 0 : __SOLO__) - 1); }));
  var sel = document.createElement("select");
  sel.style.cssText = "max-width:330px;background:var(--panel-2);color:var(--ink);"
    + "border:1px solid var(--line);border-radius:5px;padding:4px 6px;"
    + "font-family:var(--mono);font-size:12px";
  PL.forEach(function(p, i){
    var o = document.createElement("option");
    o.value = i; o.textContent = (i + 1) + "/" + __N__ + "  " + p.label;
    if (i === __SOLO__) o.selected = true;
    sel.appendChild(o);
  });
  sel.addEventListener("change", function(){ __switch(parseInt(this.value, 10)); });
  bar.appendChild(sel);
  bar.appendChild(mkbtn("▶", "下一个（键盘 →）",
    function(){ __switch((__SOLO__ < 0 ? -1 : __SOLO__) + 1); }));
  bar.appendChild(mkbtn(__SOLO__ < 0 ? "单个" : "全部",
    "单个 = 只跑一个 effect；全部 = 网格同屏（粒子多，会卡）",
    function(){
      __SOLO__ = (__SOLO__ < 0) ? 0 : -1;
      T.t = 0;
      systems.forEach(function(s){ s.live.length = 0; s.acc = 0; });
      __loadFx();                /* 网格模式要把全部 effect 解析进来（慢，用户自己点的） */
      __buildSystems();          /* 网格模式要把全部 effect 的系统建起来 */
      __renderPhases(); renderUI();
    }));
  h.appendChild(bar);
}
/* 页内切换的快捷键：只在「一页多个 effect」时注册 —— 一页一个时左右键留给
   build_preview_set.py 注入的【跨页翻页】导航条，否则两边会抢同一个键。*/
if (__N__ > 1) document.addEventListener("keydown", function(e){
  if (e.target && /^(INPUT|SELECT|TEXTAREA)$/.test(e.target.tagName)) return;
  if (e.key === "ArrowLeft"){  e.preventDefault(); __switch((__SOLO__ < 0 ? 0 : __SOLO__) - 1); }
  if (e.key === "ArrowRight"){ e.preventDefault(); __switch((__SOLO__ < 0 ? -1 : __SOLO__) + 1); }
});

phaseAt = function(t){ return (t < __DUR__) ? { key:"all", label:"全部", from:0, to:__DUR__, fx:null } : null; };
/* solo 模式把当前 effect 摆到原点（相机也按单特效取景）；网格模式摆到各自槽位 */
anchorOf = function(i, t){ return (__SOLO__ < 0) ? __slotPos(i) : new THREE.Vector3(0, 0, 0); };
emitterVelocity = function(i, t){ return new THREE.Vector3(); };

/* ---- 替身网格：原 demo 的法阵/球壳/暗晕/月牙都按 yinmo 的【绝对时间】硬编码，
        换 XML 后它们完全不讲道理 —— 一律停掉。人形只留作 1.8m 尺度基准。---- */
stepMeshes = function(){
  var solo = (__SOLO__ >= 0);        /* solo = 只跑一个，构图等同单特效 */
  if (typeof figure !== "undefined" && figure){
    figure.visible = (solo || %(human)s);    /* 人形 = 1.8m 尺度基准：单特效时给出来 */
    figure.position.y = 0;
  }
  /* 🔴 地面在 y=-2.2 且不透明；网格模式排到 y=-5.5 那一行会整行被它挡掉
     （踩过：视觉模型报「最下一行整行是空的，只有地面网格」）→ 网格模式关地面；
     solo 模式只有一个特效摆在原点，地面留着才看得出尺度 */
  if (!%(ground)s && !solo){
    if (typeof groundMesh !== "undefined" && groundMesh) groundMesh.visible = false;
    if (typeof grid !== "undefined" && grid) grid.visible = false;
  }
  if (typeof runeRing !== "undefined" && runeRing) runeRing.material.opacity = 0;
  if (typeof ring2    !== "undefined" && ring2)    ring2.material.opacity    = 0;
  if (typeof shell    !== "undefined" && shell)    shell.visible    = false;
  if (typeof aura     !== "undefined" && aura)     aura.visible     = false;
  if (typeof crescent !== "undefined" && crescent) crescent.visible = false;
};

/* ---- 相机：网格模式框住整片墙；solo 模式按单特效取景（固定机位，便于截图比对）---- */
orbit.auto = %(auto)s;
orbit.dist = %(dist).2f;
orbit.phi  = Math.PI/2;      /* 正对：相机在 +Z 轴上，直接看 XY 平面 */
orbit.theta = Math.PI/2;
if (orbit.target) orbit.target.set(%(cx).3f, %(cy).3f, %(cz).3f);
updateCamera = function(dt){
  if (orbit.auto) orbit.theta += dt * 0.06;
  orbit.dist = (__SOLO__ < 0) ? __GRID_DIST__ : __SOLO_DIST__;   /* 每帧按模式取景 */
  look.set(%(cx).3f, %(cy).3f, %(cz).3f);
  var sp = Math.sin(orbit.phi), cp = Math.cos(orbit.phi);
  camera.position.set(look.x + orbit.dist*sp*Math.cos(orbit.theta),
                      look.y + orbit.dist*cp,
                      look.z + orbit.dist*sp*Math.sin(orbit.theta));
  camera.lookAt(look);
};
""" % dict(n=n_eff, cols=cols, sp=spacing, dur=dur, rows=rows, dist=dist,
           solo_dist=solo_dist, solo0=solo0,
           cx=cx, cy=cy, cz=cz, auto=("true" if auto else "false"), only=only_js,
           human=("true" if human else "false"),
           ground=("true" if ground else "false"))

def est_duration(root):
    """按 XML 里最长的粒子寿命×2 + 余量估播放时长。"""
    longest = 1.3
    for eff in root.findall("effect"):
        for em in eff.findall("emitters/emitter"):
            for pa in em.findall("parameters/parameter"):
                if pa.get("name") != "particle_life": continue
                b = float(pa.get("base") or 0); s = float(pa.get("bias") or 0)
                longest = max(longest, b + abs(s))
    return max(2.0, min(8.0, longest * 2.0 + 0.6))

# ---------------------------------------------------------------- 组装 HTML
CDN_TAG = '<script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script>'
ACTIVE_OLD = 'active = ph && ph.fx===S.fxIdx;'
ACTIVE_NEW = ('active = ph && (ph.fx===null || ph.fx===S.fxIdx)'
              ' && (__SOLO__ < 0 || __SOLO__ === S.fxIdx);')
# 只用「两行连写」才唯一：单独的 buildEnv(); 首次出现是在「亮/暗背景」按钮的事件处理器里，
# 插到那儿 = 注入块永远不执行（本轮踩过：预览页 UI 还写着「阴魔斩 · 3-phase」、粒子只在 t<1.5 出）
ANCHOR_OLD = 'buildEnv();' + chr(10) + 'build();'
U_TEX_OLD = "uTex:{value:smokeTex}"
U_TEX_NEW = "uTex:{value:(MAT_TEX[em.num.material]||smokeTex)}"
LATIN_RE = r'<span class="latin">.*?</span>'
# 🔴 build() 里的这两行「全量」操作，是「一页装很多 effect」时卡死主线程的元凶：
#    前者 DOMParser 吃整份 XML + 约 5 万次 querySelectorAll，后者一次建几百个粒子系统。
#    都换成只处理「当前这一个 effect」的懒加载版（函数体在注入块里）。
FXS_OLD = 'FXS = parseXml(xmlText);'
FXS_NEW = 'FXS = __loadFxInit();   /* make_preview 注入：懒解析，只解析当前这一个 effect */'
SYS_OLD = ('FXS.forEach(function(E, ei){ E.emitters.forEach(function(em){ '
           'var s=makeSystem(em); s.fxIdx=ei; systems.push(s); }); });')
SYS_NEW = '__buildSystems();   /* make_preview 注入：懒建系统，只建当前这一个 effect 的 */'


def _must_replace(t, old, new, what):
    """模板打补丁的统一守卫 —— 找不到靶点就报错，不做静默兜底。

    🔴 本工具是**靠字符串替换**把模板改造成通用预览器的（模板本体仍是「阴魔斩」那一页），
       所以模板里每个靶点都是**易碎点**：靶点一改名/重排/换写法，替换静默失效，
       产出的页面看起来正常、其实是"没打上这一针"的坏页（本轮真踩过）。
       靶点全表见 tools/particle-pipeline/README.md「模板补丁靶点」一节。
    """
    if old not in t:
        raise SystemExit("模板打补丁失败：找不到靶点 [%s] —— 改过 preview.template.html？"
                         "靶点全表见 README「模板补丁靶点」" % what)
    return t.replace(old, new, 1)


def build_html(tpl, xml_s, smoke_b64, three_js, js_block, title, latin="LWN 粒子预览"):
    t = tpl
    if "__FX_XML__" not in t:
        raise SystemExit("模板里找不到占位符 __FX_XML__ —— 改过 preview.template.html？")
    t = t.replace("__FX_XML__", xml_s, 1)
    if "__SMOKE_TEX_B64__" not in t:
        raise SystemExit("模板里找不到占位符 __SMOKE_TEX_B64__ —— 改过 preview.template.html？")
    t = t.replace("__SMOKE_TEX_B64__", smoke_b64, 1)
    # three.js：内联（离线可开）或保留 CDN
    if three_js is not None:
        if "</script" in three_js:
            raise SystemExit("three.js 内含 </script，需转义后再内联")
        t = _must_replace(t, CDN_TAG, "<script>\n" + three_js + "\n</script>", "CDN_TAG")
    # 按材质用贴图（没配到的回落 smoke_d）
    t = _must_replace(t, U_TEX_OLD, U_TEX_NEW, "uTex 贴图选择")
    # 多 effect 同时发射（原逻辑只亮「当前阶段」）
    t = _must_replace(t, ACTIVE_OLD, ACTIVE_NEW, "active 判定行")
    # 懒解析 + 懒建系统（必须在注入 js_block 之前换：这两行在文档里比注入点靠前）
    t = _must_replace(t, FXS_OLD, FXS_NEW, "build(): FXS = parseXml(xmlText)")
    t = _must_replace(t, SYS_OLD, SYS_NEW, "build(): 粒子系统构建循环")
    # 注入通用化块（必须在 build() 之前）
    t = _must_replace(t, ANCHOR_OLD, js_block + "\n" + ANCHOR_OLD, "buildEnv() 注入点")
    # 标题：文档标题 / 大标题 / 拉丁副标题（模板里写死的是「阴魔斩 · 3-phase」）
    if not re.search(r"<title>.*?</title>", t):
        raise SystemExit("模板里找不到 <title> 靶点")
    t = re.sub(r"<title>.*?</title>", "<title>%s</title>" % title, t, count=1)
    t = re.sub(r"<h1>.*?</h1>", "<h1>%s</h1>" % title, t, count=1)
    t = re.sub(LATIN_RE, '<span class="latin">%s</span>' % latin, t, count=1)
    return t

def main():
    ap = argparse.ArgumentParser(description="骑砍粒子 XML -> 离线自包含 three.js 预览页")
    ap.add_argument("--xml", nargs="+", required=True, help="一个或多个 particle XML")
    ap.add_argument("-o", "--out", required=True, help="输出 html")
    ap.add_argument("--title", default="骑砍粒子预览")
    ap.add_argument("--latin", default="LWN 粒子预览", help="标题右侧的拉丁副标题")
    ap.add_argument("--three", default=None, help="本地 three.min.js 路径；none = 用 CDN")
    ap.add_argument("--cols", type=int, default=0, help="网格列数（0=自动）")
    ap.add_argument("--spacing", type=float, default=6.0, help="槽位间距(米)；太小会让多个特效糊成一坨")
    ap.add_argument("--auto", action="store_true", help="相机自动旋转（默认关，便于截图）")
    ap.add_argument("--no-human", action="store_true", help="不画 1.8m 人形尺度基准")
    ap.add_argument("--only", type=int, default=None, help="只预览第 i 个 effect")
    ap.add_argument("--texture", default=None, help="替换默认 smoke_d 贴图的 PNG")
    ap.add_argument("--tex-dir", default=None,
                    help="UE 源贴图目录：按材质名自动配一张代表性贴图（默认取 paths.TEX_DIR）")
    ap.add_argument("--no-tex", action="store_true",
                    help="不要自动配贴图 —— 全部 emitter 共用 smoke_d（纯运动模拟口径）")
    a = ap.parse_args()

    root, names = merge_xml(a.xml)
    if a.only is not None:
        keep = [e for i, e in enumerate(root.findall("effect")) if i == a.only]
        for e in list(root): root.remove(e)
        for e in keep: root.append(e)
        names = [n for i, n in enumerate(names) if i == a.only]
        log("  --only=%d → 只保留 %s" % (a.only, names[0][0] if names else "?"))

    n_eff = len(root.findall("effect"))
    cols = a.cols if a.cols > 0 else max(1, min(4, n_eff))
    dur = est_duration(root)
    log("effect 数 = %d，列数 = %d，播放时长 = %.2fs" % (n_eff, cols, dur))
    for nm, p, ne in names:
        log("   · %-34s %2d emitter   (%s)" % (nm, ne, os.path.basename(p)))

    smoke = a.texture or SMOKE
    if not os.path.exists(smoke):
        raise SystemExit("默认贴图不存在：%s\n  （它跟预览器放在一起：preview/smoke_d_256.png；"
                         "也可以用 --texture 指定别的 PNG）" % smoke)
    # 🔴 默认贴图同样要先「图集切一格」：原版 smoke_d 是 2×2 图集（四个不同的烟团），
    #    整张贴上去 = 每颗粒子画出 4 个（用户实测截图就是这个）。
    smoke_b64, _note = _tex_b64_cropped(smoke)
    if _note:
        log("默认贴图 %s: %s" % (os.path.basename(smoke), _note))
    three_js, src = three_js_inline(a.three)
    log("three.js: %s (%s)" % ("内联" if three_js else "CDN", src))

    if not os.path.exists(TPL):
        raise SystemExit("模板不存在：%s" % TPL)
    tpl = open(TPL, encoding="utf-8").read()
    tex_dir = None if a.no_tex else a.tex_dir
    if tex_dir is None and not a.no_tex and paths and os.path.isdir(paths.TEX_DIR):
        tex_dir = paths.TEX_DIR
        log("--tex-dir 未指定 → 用数据根里的 UE 贴图目录（%s）" % tex_dir)
    mtm = build_mat_tex_map(tex_dir) if tex_dir else {}
    if mtm:
        log("按材质配贴图 %d 条: %s" % (len(mtm), ", ".join(sorted(mtm))))
    # 人形只作 1.8m 尺度基准：单特效才留；多特效网格必须关掉，
    # 否则它站在正中（R2C2）白占一个格子（被视觉模型抓到）
    human = (not a.no_human) and (n_eff == 1)
    js = gen_js(n_eff, cols, a.spacing, dur, a.auto, a.only, human=human, mtm=mtm,
                ground=(n_eff == 1))    # 网格模式关地面，否则最下一行被挡
    html = build_html(tpl, xml_text(root), smoke_b64, three_js, js, a.title, a.latin)

    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    log("写出 %s  (%.0f KB)" % (a.out, len(html.encode("utf-8")) / 1024.0))
    return 0

if __name__ == "__main__":
    sys.exit(main())

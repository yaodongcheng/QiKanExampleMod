# -*- coding: utf-8 -*-
"""
ue2bannerlord.py -- 把 FCS(UE4.27) 解析出的粒子 JSON 转成骑砍2 emitter spec，
再交给 tools/particle-pipeline/gen_particle_effect.py 生成合规 XML。

核心映射（详见 Knowledge/骑砍2粒子系统.md §八）：
  * 单位：UE 是 cm，骑砍是 m  ->  长度/速度/半径 一律 /100
  * 材质：UE 的 MI_*/M_* 在骑砍不存在，必须按语义换名到原版 prt_shd_*
    （骑砍的 material 同时决定混合模式，换名 = 换发光/压暗/半透明）
  * 图集：Niagara SubImageSize / Cascade SubImages -> texture_sprite_count="c, r"
  * 朝向：Alignment -> billboard_type
  * 数值：Niagara 走 RapidIterationParameters 解码出的 Constants.<E>.<M>.<I>
          Cascade 走 Distribution*(Min/Max/Constant/Keys)

用法: python ue2bannerlord.py <parsed.json> [-o 输出spec.py] [--name lwn_xxx]
"""
import os, re, sys, json, argparse
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
import paths          # 两根定位 TOOL/DATA —— 见工具链根 paths.py（realpath 穿透 junction）

CM = 100.0          # UE cm -> m
# 🔴 控制台编码兜底（2026-09-24）：Windows 控制台是 GBK，笔记里的 `²` 等字符会让 print 抛
#    UnicodeEncodeError —— 而**打印发生在逐文件写盘的中途** ⇒ 一次只生成一半 XML，
#    症状就是"改了没生效"（极难查）。这里把 stdout 的错误策略改成 replace，永不再崩。
try:
    import sys as _sys
    _sys.stdout.reconfigure(errors="replace")
except Exception:
    pass

GRAV_SCALE = 1.0 / 980.0   # UE 重力常为 -980(cm/s²) -> 骑砍默认量级 -1

# --- UE 材质 -> 原版 prt_shd_* （顺序敏感：先具体后笼统；含 blend 语义） ---------
# 🔴 2026-09-24 修正：火焰以前映射到 `prt_shd_fire_1` —— **那是错的**。把原版 41 个材质
#    的贴图全导出来看（Debug/offline/_mat_tex_survey.py → out/sheet_materials.png）才发现：
#      · `prt_shd_fire_1` 的贴图是 `testparticle`（橙褐色**叶/片状**图集，alpha 覆盖极低）
#        ⇒ 火系效果渲染出来**几乎透明**（实渲验证：fireball/flamethrower/fireexplosion 全白淡一片）
#      · **真火焰是 `prt_shd_flame_1`**，贴图 `torchflameloop`（一格格的橙火苗），emissive+additive ✓
#      · 火星/余烬 = `prt_shd_sparks`（`spark` 是一条黄色锥形长条）
#    教训：**材质名不可望文生义**，按语义给名之前先看它的贴图长什么样。
MAT_RULES = [
    # 🔴 2026-09-25 更正：以前映射到 `prt_shd_lightning` —— **那是坏的**。它在包索引里存在
    #    （`tpaccli dump` 能解析、还带张 6 格闪电贴图），但**引擎的粒子材质表里没有它**
    #    ⇒ 编成资产后 ModKit 弹 `RGL CONTENT WARNING: Unable to find material{...}`（用户实机撞到）。
    #    判据：**"dump 得出来" ≠ "能用"** —— 只许用**原版粒子真正引用过的那 33 个**
    #    （`output/vanilla_prt_shd_materials.txt`，`validate_xml.py` 现在按它把守）。
    (("lightning", "electric", "thunder", "chain"),     "prt_shd_sparks"),
    (("fire_haze", "firehaze"),                        "prt_shd_haze_1"),
    # ⚠️ `prt_shd_fire_haze_1` 也不在那 33 个里（原版粒子没用过）⇒ 同样别用，见上一行的归并。
    (("flame", "fire", "burn", "torch", "meteor",
      "inferno", "lava", "fireball", "ignite"),         "prt_shd_flame_1"),
    (("ember", "spark"),                                "prt_shd_sparks"),
    (("snow", "frost", "ice", "blizzard", "icy", "crystal"), "prt_shd_snow_dust_1"),
    (("water", "bubble", "splash", "rain", "foam", "wave"), "prt_shd_water_splash2"),
    (("poison", "acid", "toxic", "sludge", "gross", "venom"), "prt_shd_steam_2"),
    (("steam", "mist", "fog", "wind", "noise", "distort", "refract"), "prt_shd_steam_1"),
    (("blood", "gore", "slash"),                        "prt_shd_blood_1"),
    (("gravel", "stone", "rock", "crack", "ground"),    "prt_shd_stone_gravel"),
    (("wood", "splinter"),                              "prt_shd_wood_splinter"),
    (("dust", "sand", "dirt"),                          "prt_shd_dust_1"),
    (("trail", "ribbon"),                               "prt_shd_trail"),
    (("smoke", "fume", "ash"),                          "prt_shd_smoke_1"),
    (("glow", "aura", "flash", "ring", "halo", "flare", "lens",
      "beam", "laser", "rune", "shard", "shck", "shock", "indicator",
      "icon", "marker", "cross", "shape", "circle", "place",
      "radial", "gradient", "thread", "magic", "plasma", "gpu",
      "whisp", "wisp", "light"),                       "prt_shd_glow"),
    (("dissolv", "fade", "mask", "default"),            "prt_shd_haze_1"),
]

def map_material(ue_path):
    """UE 材质全路径 -> (骑砍材质名, 命中规则)。"""
    if not ue_path: return "prt_shd_smoke_1", "default(空材质)"
    base = ue_path.rsplit("/", 1)[-1].split(".")[0].lower()
    for keys, tgt in MAT_RULES:
        for k in keys:
            if k in base:
                return tgt, "%s ~ %s" % (base, k)
    return "prt_shd_smoke_1", "fallback(%s)" % base

ALIGN2BB = {
    "VelocityAligned": "turn_to_velocity_side",
    "VelocityAlignedForward": "turn_to_velocity_side",
    "ScreenAligned": "2d",
    "ScreenAlignedUVRotated": "2d",
    "CustomAlign": "3d",
    "Automatic": "3d",
    "FacingCamera": "3d",
    "FacingCameraPlane": "3d",
    "FaceCameraPlane": "3d",
    "RotationAligned": "3d",
    "None": "none",
}

def sanitize(name):
    s = re.sub(r"[^0-9A-Za-z_]", "_", name or "emitter")
    if s[0].isdigit(): s = "e_" + s
    return s

def fnum(v, d=3):
    try: return float(v)
    except Exception: return None

# ---------------------------------------------------------------- Niagara
def strip_ci(lst, *words):
    """在候选名里找人话名（忽略大小写与空格差异）。"""
    for cand in lst:
        c = cand.lower().replace(" ", "")
        if all(w.lower().replace(" ", "") in c for w in words):
            return cand
    return None

def niagara_emitter_spec(em, asset):
    """一个 Niagara emitter -> gen_particle_effect 的 spec dict。"""
    consts = em["constants"]
    pfx = "Constants.%s." % em["name"]
    def C(module, inp):
        """按 Module.Input 取常量值。
        🔴 常量名的前缀是 emitter 的【基名】（如 Constants.Embers001.…），
        不是图里的节点名（Embers001_10）—— 所以这里必须前缀无关，只比后两段。"""
        target = (module + "." + inp).lower().replace(" ", "")
        for k, v in consts.items():
            parts = k.split(".")
            if len(parts) < 3 or parts[0] != "Constants": continue
            if ".".join(parts[2:]).lower().replace(" ", "") == target:
                return v.get("value")
        return None
    def Cany(*pairs):
        for m, i in pairs:
            v = C(m, i)
            if v is not None: return v
        return None

    mods = {m["name"] for m in em["modules"]}
    spec = {"name": sanitize(em["name"])}
    notes = []

    # --- 发射率
    sr = Cany(("SpawnRate", "SpawnRate"))
    spec["emission_rate"] = (sr, 0.0) if isinstance(sr, (int, float)) else (20.0, 0.0)

    # --- 寿命（Niagara InitializeParticle 可能给 Min/Max，也可能给单一 Lifetime）
    lmin, lmax = C("InitializeParticle", "Lifetime Min"), C("InitializeParticle", "Lifetime Max")
    lsingle = C("InitializeParticle", "Lifetime")
    if isinstance(lmin, (int, float)) and isinstance(lmax, (int, float)) and lmax > lmin:
        spec["particle_life"] = (lmin, lmax - lmin); notes.append("life=Min/Max")
    elif isinstance(lsingle, (int, float)) and lsingle > 0:
        spec["particle_life"] = (lsingle, 0.0); notes.append("life=单一Lifetime")
    # --- 发射器寿命
    ld = Cany(("EmitterState", "Loop Duration"), ("EmitterState", "Loop Delay"))
    if isinstance(ld, (int, float)) and ld > 0 and ld < 60:
        spec["emitter_life"] = ld
    # --- 发射体（球/盒）
    rad = Cany(("SphereLocation", "Sphere Radius"), ("SphereLocation", "Radius"))
    if isinstance(rad, (int, float)) and rad > 0:
        spec["emit_volume_type"] = "sphere"
        spec["emit_sphere_radius"] = rad / CM; notes.append("sphere r=%.2fm" % (rad / CM))
    # --- 初速
    vel = Cany(("AddVelocity", "Velocity"), ("AddVelocity", "Added Velocity"))
    if isinstance(vel, list) and len(vel) == 3 and any(abs(x) > 1e-6 for x in vel):
        spec["emit_velocity_x"] = (vel[0] / CM, abs(vel[0]) / CM * 0.2)
        spec["emit_velocity_y"] = (vel[1] / CM, abs(vel[1]) / CM * 0.2)
        spec["emit_velocity_z"] = (vel[2] / CM, abs(vel[2]) / CM * 0.2)
        notes.append("vel=%.1f,%.1f,%.1f cm/s" % tuple(vel))
    # --- 限速
    sl = Cany(("SolveForcesAndVelocity", "Speed Limit"))
    if isinstance(sl, (int, float)) and 0 < sl < 1e5:
        spec["emission_speed_limit"] = sl / CM
    # --- 湍流（骑砍没有 curl noise，用 turbulence 近似）
    ns = Cany(("CurlNoiseForce", "Noise Strength"))
    if isinstance(ns, (int, float)) and ns > 0:
        spec["turbulence_strength"] = (min(ns / CM, 8.0), min(ns / CM * 0.5, 4.0))
        notes.append("curlNoise strength=%.0f -> turbulence 近似" % ns)
    if "CurlNoiseForce" in mods:
        spec["emission_turbulence_interval"] = 0.10
        spec["emission_turbulence_strength"] = 0.0
    # --- 重力
    g = Cany(("GravityForce", "Gravity"), ("Gravity", "Gravity"), ("InitializeParticle", "Gravity"))
    if isinstance(g, list) and len(g) == 3 and any(abs(x) > 1e-6 for x in g):
        spec["gravity"] = "%.3f, %.3f, %.3f" % (g[0] * GRAV_SCALE, g[1] * GRAV_SCALE, g[2] * GRAV_SCALE)
        notes.append("gravity=%s cm/s² -> 缩放 %.4f" % (g, GRAV_SCALE))

    # --- 尺寸：① 先取常量表的真实尺寸（cm→m）② 曲线另算（两者叠加＝有效尺寸）
    sz = niagara_size_pair(em)
    if sz:
        spec["particle_size"] = sz
        notes.append("size %s cm -> %s m" % (niagara_const(em, "Uniform Sprite Size"),
                                             sz[0]))
    curve = niagara_size_curve(em)
    if curve:
        spec["size_curve"] = curve; notes.append("size 曲线来自 Vector2DFromCurve")
    elif isinstance(em.get("_cascade_size"), list):
        pass

    # --- 发射率 / 寿命：同样在常量表里（以前没读 ⇒ 全是兜底 20）
    sr = niagara_const(em, "SpawnRate")
    if isinstance(sr, (int, float)) and sr > 0:
        spec["emission_rate"] = (float(sr), 0.0); notes.append("rate=%s" % sr)
    lmin = niagara_const(em, "Lifetime Min")
    lmax = niagara_const(em, "Lifetime Max")
    if isinstance(lmin, (int, float)) and isinstance(lmax, (int, float)):
        spec["particle_life"] = (round((lmin + lmax) / 2.0, 3), round(abs(lmax - lmin) / 2.0, 3))
        notes.append("life=%s~%s" % (lmin, lmax))

    # --- 颜色
    col, alpha = niagara_color(em)
    if col: spec["color"] = col; notes.append("color 曲线来自 ColorFromCurve")
    if alpha: spec["alpha"] = alpha
    if col is None:
        cc = Cany(("Color", "Color"))
        # 🔴 纯黑 = Niagara 里「颜色由材质提供」的默认态，直接照搬会把特效刷成黑色。
        #    骑砍的 particle_color 是与材质相乘的，此时必须【不覆盖】，沿用生成器默认。
        if isinstance(cc, list) and len(cc) == 4 and (abs(cc[0]) + abs(cc[1]) + abs(cc[2])) > 0.02:
            # UE 常给 HDR 颜色（如闪电 (0, 47.8, 50)），骑砍的 particle_color 是 0~1 乘子，必须归一化
            mx = max(abs(cc[0]), abs(cc[1]), abs(cc[2]), 1.0)
            r3 = [max(0.0, min(1.0, c / mx)) for c in cc[:3]]
            spec["color"] = [("0.000", "%.3f, %.3f, %.3f" % tuple(r3))]
            notes.append("color 常量 %s%s" % (cc, "（已 HDR 归一）" if mx > 1.0 else ""))
        elif isinstance(cc, list) and len(cc) == 4:
            notes.append("color=纯黑 -> 判定为「颜色由材质给」，不覆盖")

    # --- 渲染器
    r = (em["renderers"] or [None])[0]
    if r:
        m, why = map_material(r.get("material"))
        spec["material"] = m; notes.append("material %s" % why)
        # 🔴 记住这个材质是「语义匹配到的」还是「兜底桶里的」（2026-09-25 第三轮）：
        #    元素覆盖只允许刷**兜底桶**（我们根本没认出它是什么），语义匹配到的一律保留
        #    —— 真实烟雾就该是烟、余烬就该是火星，不然爆炸只剩一团火焰（丢了黑烟对比）。
        spec["_mat_fb"] = why.startswith("fallback") or why.startswith("default")
        si = r.get("sub_image_size")
        if si:
            mm = re.findall(r"[XY]=([\d.]+)", si)
            if len(mm) == 2 and float(mm[0]) > 0 and float(mm[1]) > 0:
                # 🔴 顺序 = `texture_sprite_count="X, Y"`（先列后行），知识文档 §八「SubImageSize=(X,Y) → "X, Y"」
                #    实证：`TextureSpriteCountX/Y` 分别对应第 1/2 个数。2026-09-25 前这里是**写反的**
                #    （写成 Y, X）—— 方形图集看不出来（8×8/5×5），一旦 `X=2, Y=3` 这种就会切错格。
                spec["texture_sprite_count"] = "%d, %d" % (int(float(mm[0])), int(float(mm[1])))
                spec.setdefault("flags", {})["select_random_sprite"] = True; notes.append("atlas %s" % si)
        al = r.get("alignment")
        if al in ALIGN2BB:
            spec["billboard_type"] = ALIGN2BB[al]; notes.append("align %s" % al)
    else:
        spec["material"] = "prt_shd_smoke_1"; spec["_mat_fb"] = True; notes.append("material 缺省")
    spec["_notes"] = notes
    return spec

def const_find(em, *names):
    """在**扁平点号键**常量表里按尾段取原始值。

    🔴 2026-09-25 根因修正（T1）：解析产物里 `emitters[].constants` 是**扁平字典**——
    键就是完整点号路径（`Constants.<命名空间>.InitializeParticle.Uniform Sprite Size`），
    不是嵌套字典。以前三处读取代码（本函数的前身 / `niagara_size_pair` / `collect_ue_sizes`）
    都按"嵌套逐层 get"写 ⇒ **永远取不到** ⇒ 685 个发射器尺寸清一色吃生成器默认的 0.25±0.25。
    匹配规则：去掉 `Constants` 与命名空间两段后的尾段，与要查的名字比对；
    允许"尾段以 `.名字` 结尾"（调用方省掉模块前缀也能命中）。实测覆盖 280/685 个发射器。

    🔴 命名空间选值优先序（2026-09-25 第二轮）：一个发射器的常量表里可能**同时**挂着
    多个命名空间的同名参数（它自己的 + 上游继承来的），"取第一个"会取错 —— 实测
    `Empty002_4` 拿到了 `Empty` 的 0.30 而不是自己的 0.02、`Smoke_02_9` 拿到别家的 0.10。
    所以按「命名空间 = 发射器基名 > 前缀匹配（长者优先）> 第一个」挑。
    """
    consts = em.get("constants") or {}
    wants = [n.lower().replace(" ", "") for n in names]
    hits = []                                   # (命名空间, 距离, 取值)
    for k, v in consts.items():
        parts = k.split(".")
        if len(parts) < 3 or parts[0] != "Constants":
            continue
        tail = ".".join(parts[2:]).lower().replace(" ", "")
        if not any(tail == w or tail.endswith("." + w) for w in wants):
            continue
        hits.append((parts[1], ns_rank(em.get("name"), parts[1]), v.get("value")))
    if not hits:
        return None
    hits.sort(key=lambda h: h[1])
    return hits[0][2]


def ns_rank(em_name, ns):
    """命名空间与发射器的贴合度（越小越贴合）：同名 0 · 前缀匹配 1 · 其他 2。

    发射器名形如 `Embers001_3`（图里的节点名带序号），命名空间形如 `Embers001`
    （模块组名）—— 所以比较前先把名字尾部的 `_数字` 去掉。
    """
    base = re.sub(r"_\d+$", "", em_name or "").lower()
    low = (ns or "").lower()
    if base == low:
        return 0
    if low and (base.startswith(low) or low.startswith(base)):
        return 1
    return 2


def num_or_mean(v):
    """标量直接用；vec2/vecN 取分量均值（UE 的 `Sprite Size` 是「宽×高」两分量，
    骑砍的 `particle_size` 是单值方片）；其他一律 None。"""
    if isinstance(v, (int, float)):
        return float(v)
    if isinstance(v, (list, tuple)) and v and all(isinstance(x, (int, float)) for x in v):
        return float(sum(v)) / len(v)
    return None


def const_num(em, *names):
    """`const_find` 的数值版（见 `num_or_mean` 的取值口径）。"""
    return num_or_mean(const_find(em, *names))


def size_pair(mx, mn, one):
    """(Max, Min, 单值) 三个候选值（**cm**）→ 骑砍 `particle_size` 的 (base, bias)（**m**）。

    · Min/Max 齐 → base=(Max+Min)/200、bias=|Max−Min|/200（骑砍是 ± 半宽）
    · 只有单值  → base=值/100、bias=0
    """
    if mx is not None and mn is not None:
        return (round((mx + mn) / 200.0, 4), round(abs(mx - mn) / 200.0, 4))
    if one is not None:
        return (round(one / 100.0, 4), 0.0)
    if mx is not None:
        return (round(mx / 100.0, 4), 0.0)
    return None


def niagara_const(em, key_name):
    """从 Niagara 的 `constants.Constants.<Emitter>.<Module>.<Param>` 里取**第一个带 value 的**匹配项。

    🔴 2026-09-24 补：**这套常量表以前根本没被读** —— 于是绝大多数发射器的尺寸/发射率
    只能吃兜底值（`particle_size 0.25±0.25` / `emission_rate 20`），一堆角色完全不同的
    发射器参数一模一样（实查 lightningstrike 19 个发射器全同值 ⇒ 渲出来是一块大白板、
    锯齿状、没有任何"闪电"的形态）。UE 侧其实写得很清楚：
      · `InitializeParticle.Uniform Sprite Size`（**cm**）→ 粒子尺寸
      · `SpawnRate.SpawnRate` → 发射率
      · `InitializeParticle.Lifetime Min/Max` → 寿命
    注意同一前缀下还有 `....SpawnRate.size`（那是**字节数**），所以只认**带 value 的 dict**。
    """
    return const_find(em, key_name)


def niagara_size_pair(em):
    """UE 的粒子尺寸（cm）→ 骑砍 particle_size（m）：(base, bias)。

    🔴 2026-09-25 T1 修复：以前这里扫的是**按键名精确匹配的嵌套字典**，而真实键是
    扁平点号路径 ⇒ 一次都没命中过（实测 685 个发射器命中 0），尺寸全吃生成器默认的
    0.25±0.25 —— 这就是"闪电一片大白板、暴风雪永远一坨"的真因。改走 `const_num`
    （扁平键 + 尾段匹配，标量/vec2 都吃）后覆盖 320/487；剩下那些发射器自身没有该常量，由
    `collect_ue_sizes` 的文件级命名空间表兜底。
    """
    mx = const_num(em, "Uniform Sprite Size Max", "Sprite Size Max")
    mn = const_num(em, "Uniform Sprite Size Min", "Sprite Size Min")
    one = const_num(em, "Uniform Sprite Size", "Sprite Size")
    return size_pair(mx, mn, one)


def collect_ue_sizes(doc):
    """文件级尺寸表：**命名空间 → (base_m, bias_m)**，给"自己身上没有该常量"的发射器兜底。

    实测路径（2026-09-25 用扁平键重读）：`Constants.<命名空间>.InitializeParticle.Uniform Sprite Size[ Max|Min]`
    · `<命名空间>` = 该常量块的归属名（如 `Llightning` / `Sparks`），**不保证等于某个发射器名**；
    · 单位 **cm** ⇒ 走 `size_pair` 换算成米；
    · 实查样例：`Llightning` = Min50/Max100 → **0.75±0.25 m**；`Sparks` = 5 → **0.05 m**。
    """
    tbl = {}
    for em in (doc.get("emitters") or []):
        for k, v in (em.get("constants") or {}).items():
            parts = k.split(".")
            if len(parts) < 4 or parts[0] != "Constants":
                continue
            val = num_or_mean(v.get("value"))       # vec2 的 `Sprite Size` 也吃（取均值）
            if val is None:
                continue
            tail = ".".join(parts[2:]).lower().replace(" ", "")
            if tail not in ("initializeparticle.uniformspritesizemax",
                            "initializeparticle.uniformspritesizemin",
                            "initializeparticle.uniformspritesize",
                            "initializeparticle.spritesizemax",
                            "initializeparticle.spritesizemin",
                            "initializeparticle.spritesize"):
                continue
            tbl.setdefault(parts[1], {})[tail.rsplit(".", 1)[-1]] = float(val)
    out = {}
    for ns, d in tbl.items():
        v = size_pair(d.get("uniformspritesizemax") or d.get("spritesizemax"),
                      d.get("uniformspritesizemin") or d.get("spritesizemin"),
                      d.get("uniformspritesize") or d.get("spritesize"))
        if v:
            out[str(ns)] = v
    return out


def em_size_from_table(sizes, em_name):
    """按名字把尺寸表对到某个 emitter 上（对位规则与 `const_find` 同：同名 > 前缀 > 唯一值）。

    🔴 只认「名字对得上」与「全表只有一个取值」两种情形 —— **不做"取最常见值"**：
    同一文件里常常既有 20 m 的天幕也有 5 cm 的火花，硬塞一个值比吃默认值更糟
    （那会让小元素变成大白板，正是这轮要修的毛病）。
    """
    if not sizes:
        return None
    ranked = sorted(sizes.items(), key=lambda kv: ns_rank(em_name, kv[0]))
    if ranked and ns_rank(em_name, ranked[0][0]) == 0:              # 同名：直接用
        return ranked[0][1]
    prefixed = [kv for kv in ranked if ns_rank(em_name, kv[0]) == 1]
    if prefixed:
        return sizes[max(prefixed, key=lambda kv: len(kv[0]))[0]]   # 前缀匹配里取最长的那个
    if len(set(sizes.values())) == 1:
        return next(iter(sizes.values()))
    return None


def niagara_size_curve(em):
    """从 NiagaraDataInterfaceVector2DCurve 的 XCurve/YCurve 取尺寸曲线。"""
    for di in em.get("data_interfaces", []):
        p = di.get("props", {})
        xs, ys = p.get("XCurve", ""), p.get("YCurve", "")
        if not xs and not ys: continue
        kx = [(float(m.group(1)), float(m.group(2))) for m in
              re.finditer(r"Time=([\d.]+),Value=([\d.-]+)", xs)]
        ky = [(float(m.group(1)), float(m.group(2))) for m in
              re.finditer(r"Time=([\d.]+),Value=([\d.-]+)", ys)]
        base = kx[0][1] if kx else (ky[0][1] if ky else 0.5)
        end = kx[-1][1] if kx else (ky[-1][1] if ky else base)
        return (base, end, 1.0)
    return None

def niagara_color(em):
    """NiagaraDataInterfaceColorCurve 的 RGBA 四条曲线 -> (color_keys, alpha_keys)。"""
    for di in em.get("data_interfaces", []):
        p = di.get("props", {})
        if "RedCurve" not in p: continue
        def kv(s):
            out = []
            for m in re.finditer(r"Time=([\d.]+)(?:,Value=([\d.-]+))?", s):
                out.append((float(m.group(1)), float(m.group(2) or 0)))
            return out
        r, g, b = kv(p.get("RedCurve", "")), kv(p.get("GreenCurve", "")), kv(p.get("BlueCurve", ""))
        a = kv(p.get("AlphaCurve", ""))
        if not r: continue
        mx = max([v for _, v in r + g + b] or [1]) or 1
        scale = 100.0 if mx > 2.0 else 1.0   # UE 颜色曲线常见 0~255
        n = max(len(r), len(g), len(b))
        cols = []
        for i in range(n):
            t = r[i][0] if i < len(r) else r[-1][0]
            rr = (r[i][1] if i < len(r) else r[-1][1]) / scale
            gg = (g[i][1] if i < len(g) else g[-1][1]) / scale
            bb = (b[i][1] if i < len(b) else b[-1][1]) / scale
            cols.append(("%.3f" % t, "%.3f, %.3f, %.3f" % (rr, gg, bb)))
        al = [("%.3f" % t, "%.3f" % min(max(v, 0.0), 1.0)) for t, v in a]
        # 🔴 退化 alpha 必须丢掉（2026-09-25 实机：用户在 ModKit 的粒子面板里"什么都看不到"）：
        #    UE 侧很多发射器的 AlphaCurve 只有**一根键、值 0**（可见性其实交给材质或模块的
        #    Scale Alpha 决定），照搬过来 = 骑砍这边粒子**全透明**。全量统计：685 个发射器里 **104 个**
        #    是这种退化曲线（fireball 的主火焰 `Fire_8`＝125/s、1.09 m 就是这么"隐身"的）。
        #    判据：键数 < 2，或所有键值 ≤ 0.02 ⇒ 不写 alpha，让生成器的默认"淡入淡出"曲线顶上。
        vals = [v for _, v in a]
        if len(a) < 2 or (vals and max(vals) <= 0.02):
            al = None
        return cols, al
    return None, None

# ---------------------------------------------------------------- Cascade
PSA2BB = {"PSA_None": "3d", "PSA_Velocity": "turn_to_velocity_side",
          "PSA_Square": "2d", "PSA_FacingCameraPosition": "2d",
          "PSA_FacingCameraDistanceBlend": "3d"}

def cv_of(v):
    """把 resolve_distribution 的产物折算成 (min, max, constant, keys)。"""
    if not isinstance(v, dict): return None, None, None, None
    mn = v.get("MinValue"); mx = v.get("MaxValue")
    if mn is None and isinstance(v.get("min"), list): mn = sum(v["min"]) / 3.0
    if mx is None and isinstance(v.get("max"), list): mx = sum(v["max"]) / 3.0
    c = v.get("constant")
    if c is None and isinstance(v.get("constant_vec"), list):
        c = v["constant_vec"]
    return mn, mx, c, v.get("keys")

def cascade_get(em, cls_frag, key):
    for m in em["modules"]:
        if cls_frag in m["cls"]:
            if key in m["values"]: return m["values"][key]
    return None

def cascade_emitter_spec(em):
    spec = {"name": sanitize(em["name"])}
    notes = []
    # 发射率
    mn, mx, c, _ = cv_of(cascade_get(em, "ParticleModuleSpawn", "Rate"))
    if c is None and mn is None:
        mn, mx, c, _ = cv_of(cascade_get(em, "ParticleModuleRequired", "SpawnRate"))
    rate = c if c is not None else (max(mn or 0, mx or 0))
    spec["emission_rate"] = (float(rate or 20.0), 0.0) if (rate or 0) > 0 else (20.0, 0.0)
    # 寿命
    mn, mx, c, _ = cv_of(cascade_get(em, "ParticleModuleLifetime", "Lifetime"))
    if mn is not None and mx is not None:
        spec["particle_life"] = (mn, max(mx - mn, 0.0))
    elif c is not None:
        spec["particle_life"] = (c, 0.0)
    # 尺寸（UE cm -> m）
    sz = em.get("_StartSize")
    mn, mx, c, _ = cv_of(cascade_get(em, "ParticleModuleSize", "StartSize"))
    base = None
    if isinstance(c, list): base = max(c) / CM
    elif mn is not None and mx is not None: base = ((mn + mx) / 2.0) / CM
    if base:
        spec["size_curve"] = (base, base, 1.0); notes.append("size %.1f cm" % (base * CM))
    # 初速
    mn, mx, c, _ = cv_of(cascade_get(em, "ParticleModuleVelocity", "StartVelocity"))
    if isinstance(c, list) and any(abs(x) > 1e-6 for x in c):
        spec["emit_velocity_x"] = (c[0] / CM, abs(c[0]) / CM * 0.2)
        spec["emit_velocity_y"] = (c[1] / CM, abs(c[1]) / CM * 0.2)
        spec["emit_velocity_z"] = (c[2] / CM, abs(c[2]) / CM * 0.2)
        notes.append("vel=%s cm/s" % c)
    # 阻尼
    mn, mx, c, _ = cv_of(cascade_get(em, "AccelerationDrag", "DragCoefficient"))
    if isinstance(c, (int, float)) and c > 0:
        spec["damping"] = (min(c, 10.0), 0.0); notes.append("drag=%.2f" % c)
    # 重力/加速度
    mn, mx, c, _ = cv_of(cascade_get(em, "ParticleModuleAcceleration", "Acceleration"))
    if isinstance(c, list) and any(abs(x) > 1e-6 for x in c):
        spec["gravity"] = "%.3f, %.3f, %.3f" % tuple(x * GRAV_SCALE for x in c)
        notes.append("acc=%s cm/s²" % c)
    # 颜色
    mn, mx, c, keys = cv_of(cascade_get(em, "ParticleModuleColorOverLife", "ColorOverLife"))
    ak = cascade_get(em, "ParticleModuleColorOverLife", "AlphaOverLife")
    if keys:
        cols = [("%.3f" % (k.get("Time", 0)), "%.3f, %.3f, %.3f" %
                 (min(max(k.get("Value", 1), 0), 1),) * 3) for k in keys]
        spec["color"] = cols; notes.append("ColorOverLife %d keys" % len(cols))
    akd = None
    if isinstance(ak, dict) and ak.get("keys"):
        akd = [("%.3f" % k.get("Time", 0), "%.3f" % min(max(k.get("Value", 1), 0), 1)) for k in ak["keys"]]
        spec["alpha"] = akd; notes.append("AlphaOverLife %d keys" % len(akd))
    # 起始颜色（常量）——比 ColorOverLife 更常见，优先使用
    c2 = cascade_get(em, "ParticleModuleColor", "StartColor")
    a2 = cascade_get(em, "ParticleModuleColor", "StartAlpha")
    rgb = None
    if isinstance(c2, dict):
        rgb = c2.get("constant_vec") or c2.get("min") or c2.get("max")
    if isinstance(rgb, list) and len(rgb) >= 3:
        al = 1.0
        if isinstance(a2, dict):
            al = a2.get("constant", a2.get("MaxValue", 1.0)) or 1.0
        spec["color"] = [("0.000", "%.3f, %.3f, %.3f" % (rgb[0], rgb[1], rgb[2]))]
        spec["alpha"] = [("0.000", "%.3f" % al), ("1.000", "0.000")]
        notes.append("StartColor %s a=%.2f" % ([round(x, 3) for x in rgb], al))
    # 渲染器（材质/图集/朝向）——Cascade 的材质在 ParticleModuleRequired 上
    req = None
    for m in em["modules"]:
        if "ParticleModuleRequired" in m["cls"]: req = m["values"]; break
    mat = None
    if req and req.get("Material"):
        mm = re.search(r"'\"([^\"]+)\"'", str(req["Material"]))
        mat = mm.group(1) if mm else str(req["Material"])
    if not mat and em.get("type_data", {}).get("material"):
        mat = em["type_data"]["material"]
    mn, why = map_material(mat)
    spec["material"] = mn; notes.append("material %s" % why)
    spec["_mat_fb"] = why.startswith("fallback") or why.startswith("default")   # 同 Niagara：兜底桶标记
    if req:
        h = req.get("SubImages_Horizontal"); v = req.get("SubImages_Vertical")
        try:
            if int(float(h)) > 0 and int(float(v)) > 0 and (int(float(h)) > 1 or int(float(v)) > 1):
                spec["texture_sprite_count"] = "%d, %d" % (int(float(h)), int(float(v)))
                spec.setdefault("flags", {})["select_random_sprite"] = True; notes.append("atlas %sx%s" % (h, v))
        except Exception: pass
        sa = str(req.get("ScreenAlignment", ""))
        if sa in PSA2BB: spec["billboard_type"] = PSA2BB[sa]; notes.append("align %s" % sa)
        for k in ("EmitterDuration", "EmitterLoops", "EmitterDelay"):
            if req.get(k): notes.append("%s=%s" % (k, req[k]))
    spec["_notes"] = notes
    return spec

# ---------------------------------------------------------------- main
# 🔴 元素覆盖（2026-09-24）：按**效果名**定元素，纠正"UE 模板残留名"造成的错配。
#    起因（实渲验证抓到的）：冰弹 `NS_Frostbolt` 内部三个 emitter 叫 `Fire_8` / `Embers_6` / `Smoke_7`、
#    材质是引擎默认 `DefaultSpriteMaterial` ⇒ 按名字/材质映射会把**冰弹做成火焰**（预览里就是一团火）。
#    所以最后按"这个法术看起来该是什么"兜一道底。
#    ⚠️ 只覆盖"元素性质"的材质；刻意选的碎屑/血/木/草/水花/拖尾**不碰**；
#       暗色（modulate/压暗）材质也**保留** —— 它们是"阴"的对比来源，换成亮贴图就没黑色了。
ELEMENT_RULES = [
    (("frost", "ice", "snow", "blizzard", "icy", "crystal", "winter"), "prt_shd_snow_dust_1"),
    (("poison", "acid", "toxic", "venom"),                             "prt_shd_steam_2"),
    (("fire", "flame", "burn", "meteor", "inferno", "lava"),           "prt_shd_flame_1"),
    (("lightning", "electric", "thunder", "chain"),                    "prt_shd_sparks"),
    (("blood", "drain", "gore"),                                       "prt_shd_blood_1"),
    (("heal", "buff", "bless", "holy"),                                "prt_shd_glow"),
    (("madness", "shadow", "dark", "curse", "debuff"),                 "prt_shd_haze_1"),
]
# 🔴 `OVERRIDABLE`（可被元素覆盖的材质白名单）已于 2026-09-25 第三轮**废止** ——
#    它按"材质名在不在名单里"判，结果把语义明确的材质也刷掉了。改成按 `_mat_fb`
#    （这个材质是不是从兜底桶来的）判，见 `element_override`。别再把它加回来。
KEEP_DARK = {"prt_shd_haze_1", "prt_shd_fire_haze_1", "prt_shd_dust_1", "prt_shd_snow_dust_1",
             "prt_shd_blood_1", "prt_shd_stone_gravel", "prt_shd_wood_splinter", "prt_shd_trail"}


def element_override(effect_name, emitters):
    """按效果名统一元素材质 —— **只刷「兜底桶」里的发射器**；返回命中的元素关键字（没命中返回 None）。

    🔴 2026-09-25 第三轮收窄（原实现会刷掉一切"可覆盖"材质，实测 99 个效果里 56 个命中、共刷 278 次）：
    实测被误刷的大头是**语义明确的材质** —— `MI_Splash_01→sparks`、`MI_Fire_01_8X8→sparks`、
    `MI_SmokeFlipbook_01→sparks`、`MI_Lightning_01→sparks`（闪电系 8 个效果全是这样，
    再叠加 `SIZE_CAP[sparks]=0.45` 把尺寸压平 ⇒ **一整片白色贴片**）。
    现在的判据 = **我们根本没认出这个材质是什么**（`map_material` 走的 fallback/default 分支、
    或压根没有渲染器）才允许刷；认出语义的一律保留（真实烟雾就该是烟、余烬就该是火星，
    否则爆炸只剩一团火焰、丢了黑烟对比）。元素感主要由 `ELEMENT_COLOR` 的配色承担。
    """
    low = effect_name.lower()
    for keys, mat in ELEMENT_RULES:
        if any(k in low for k in keys):
            for em in emitters:
                m = em.get("material")
                if m in KEEP_DARK:
                    continue
                if (m is None or em.get("_mat_fb")) and m != mat:
                    em["material"] = mat
                    em.setdefault("_notes", []).append("element(%s)->%s" % (keys[0], mat))
            return keys[0]
    return None


# 🔴 材质 → 惯用图集切法（2026-09-24，**从原版自己的粒子 XML 统计**：8 个 particle_systems_*.xml、
#    57 个材质、取众数）。为什么必须跟着材质走：**图集切法是"贴图"的属性** —— 换材质就是换贴图，
#    切法必须一起换。以前我们只在 UE 给了 `sub_image_size` 时才填，于是把材料换成原版材质后
#    （如 fire_1 → flame_1）没跟着改 ⇒ **整张火苗图当一颗粒子画** → 缩成米粒、几乎透明
#    （2026-09-24 实渲验证抓到的：fireball/flamethrower/fireexplosion 全"看不见"）。
#    格式：材质名 -> (sprite_count, frame_count, frame_rate, 播序列帧?, 随机取格?)
MAT_SPRITE = {
    "prt_shd_smoke_1":           ("2, 2",   1,   1.0, False, False),
    "prt_shd_smoke_2":           ("5, 5",  25,  20.0, True,  False),
    "prt_shd_smoke_3":           ("2, 2",   1,   1.0, False, False),
    "prt_shd_smoke_4":           ("8, 8",  64,  32.0, True,  False),
    "prt_shd_haze_1":            ("2, 2",   1,   1.0, False, False),
    "prt_shd_fire_1":            ("5, 5",  25,  30.0, True,  False),
    "prt_shd_flame_1":           ("16, 8", 128, 48.0, True,  False),
    "prt_shd_glow":              ("1, 1",   1,   1.0, False, False),
    "prt_shd_sparks":            ("1, 1",   1,   1.0, False, False),
    # ⚠️ 2026-09-25：`prt_shd_lightning` 条目**已删** —— 那材质引擎粒子表里没有（见 MAT_RULES 顶部），
    #    它虽然是一张 6 格闪电分镜（`tex[0]=lightning`），但**不能用**。闪电改走 `prt_shd_sparks`。
    "prt_shd_dust":              ("2, 2",   1,   1.0, False, False),
    "prt_shd_dust_1":            ("1, 1",   1,   1.0, False, False),
    "prt_shd_dust_2":            ("2, 2",   1,   1.0, False, False),
    "prt_shd_default_dirt":      ("8, 8",  64,  96.0, True,  False),
    "prt_shd_steam_1":           ("1, 1",   1,   1.0, False, False),
    "prt_shd_steam_2":           ("8, 8",  64,  24.0, True,  False),
    "prt_shd_snow_dust_1":       ("1, 1",   1,   1.0, False, False),
    "prt_shd_snow_fall_1":       ("1, 1",   1,   1.0, False, False),
    "prt_shd_blood_1":           ("8, 8",  64,  64.0, True,  False),
    "prt_shd_blood_3":           ("8, 8",  64,  96.0, True,  False),
    "prt_shd_blood_4":           ("8, 4",  32,  32.0, True,  False),
    "prt_shd_blood_5":           ("4, 4",  16,  30.0, True,  False),
    "prt_shd_water_splash":      ("6, 6",  36,  48.0, True,  False),
    "prt_shd_water_splash2":     ("8, 8",  64, 128.0, True,  False),
    "prt_shd_water_dust":        ("8, 8",  64,  64.0, True,  False),
    "prt_shd_water_dust_2":      ("2, 2",   1,   1.0, False, False),
    "prt_shd_water_foam_circular": ("1, 1", 1,   1.0, False, False),
    "prt_shd_water_wave_1":      ("1, 1",   1,   1.0, False, False),
    "prt_shd_waterfall_dust":    ("1, 1",   1,   1.0, False, False),
    "prt_shd_stone_gravel":      ("4, 4",   1,   1.0, False, False),
    "prt_shd_wood_splinter":     ("4, 4",   1,   1.0, False, False),
    "prt_shd_wood_splinter_2":   ("4, 4",   1,   1.0, False, False),
    "prt_shd_trail":             ("1, 1",   1,   1.0, False, False),
    "prt_shd_grass":             ("4, 4",   1,   1.0, False, False),
    "prt_shd_straw_1":           ("3, 3",   1,   1.0, False, False),
    "prt_shd_rain_mud_1":        ("2, 2",   1,   1.0, False, False),
}
MAT_SPRITE_DEF = ("1, 1", 1, 1.0, False, False)


# 🔴 调参表（2026-09-24 晚 起）—— **自动翻译的起点不够看，这里按人眼复验的结果补差**。
#    为什么必须有人调：UE 的模块栈（CurlNoiseForce / PointAttraction / Vortex / 任意曲线力）
#    与骑砍那 55 个固定参数**不是一一对应** ⇒ 自动翻译能翻的翻、翻不了的丢，观感必然差一截。
#    每条都要写**依据**（哪次渲图看到什么），别凭感觉加数字。
#    格式：(效果名包含任一, 材质名, {参数: 系数})；**参数名用 spec/XML 的真名**
#          （emission_rate / particle_size / particle_life / alpha）
TUNE_RULES = [
    # 火焰系太淡（2026-09-24 渲图：fireball/fireexplosion/flamethrower 几乎看不见）——
    # 原因：UE 那边火焰靠 CurlNoise 把粒子吹散+叠加发光，骑砍没有等价力场 ⇒ 密度不够。
    (("fireball", "fireexplosion", "flamethrower", "firepit", "meteor", "rainoffire", "firetornado"),
     "prt_shd_flame_1", {"emission_rate": 2.5, "particle_size": 1.5, "particle_life": 1.4}),
    # 冰雪系：黑烟（haze_1，乘法压暗）占比过高 ⇒ blizzard 渲出来是一团死黑（2026-09-24 渲图）
    (("blizzard", "icywinds", "icytornado", "ice_circle", "snowstorm"),
     "prt_shd_haze_1", {"emission_rate": 0.45, "particle_size": 0.7, "alpha": 0.6}),
    # 冰雪系的亮部要顶上来（否则压暗一起作用 = 全黑）
    (("blizzard", "icywinds", "icytornado", "ice_circle", "snowstorm"),
     "prt_shd_snow_dust_1", {"emission_rate": 1.8, "particle_size": 1.3}),
    # 火花是"条"，尺寸一大就成一块板（2026-09-24：lightningbolt 渲出一个大锥形）
    (("lightning", "chainlightning", "lightningbolt", "lightningstrike"),
     "prt_shd_sparks", {"particle_size": 0.6, "emission_rate": 1.6}),
]


# 🔴 元素配色（2026-09-24 晚 第 2 轮）—— 上一轮只按元素换了**材质**、没换**颜色**，
#    于是：冰霜渲出来是灰的/红的（UE 曲线给的就是那个色）、毒是白的、暴风雪糊成一片死黑
#    （黑不是材质黑，是**粒子颜色**被 UE 曲线带成暗色）。
#    这里按元素给一条固定的颜色坡道，**只覆盖"元素主材质"那些发射器**，暗色（modulate）的不碰。
ELEMENT_COLOR = {
    "frost":     [("0.000", "0.78, 0.92, 1.000"), ("0.500", "0.45, 0.75, 1.000"), ("1.000", "0.20, 0.45, 0.850")],
    "fire":      [("0.000", "1.000, 0.85, 0.55"), ("0.400", "1.000, 0.55, 0.180"), ("1.000", "0.75, 0.20, 0.080")],
    "poison":    [("0.000", "0.70, 1.000, 0.45"), ("0.500", "0.40, 0.85, 0.250"), ("1.000", "0.20, 0.55, 0.120")],
    "lightning": [("0.000", "0.85, 0.95, 1.000"), ("0.500", "0.55, 0.80, 1.000"), ("1.000", "0.30, 0.55, 0.950")],
    "blood":     [("0.000", "0.85, 0.20, 0.200"), ("1.000", "0.35, 0.04, 0.060")],
    "heal":      [("0.000", "0.85, 1.000, 0.75"), ("1.000", "0.35, 0.80, 0.400")],
    "madness":   [("0.000", "0.70, 0.45, 0.900"), ("1.000", "0.25, 0.10, 0.400")],
}
# 🔴 尺寸上限（2026-09-24）：`sparks`（一条黄色锥形）和 `glow`（芝麻大一个点）**天生是小元素**，
#    UE 侧给的 size 一大就摊成"大白锥/大光板"（实查：chainlightning 一整块青板、
#    explosiongroundbig/frostexplosion 出扇面）。按材质钉上限。
SIZE_CAP = {"prt_shd_sparks": 0.45, "prt_shd_glow": 0.60, "prt_shd_flame_1": 1.20}


def apply_element_color(element, ems, main_mat):
    """按元素给"元素主材质"那些发射器刷固定配色（暗色/碎屑类不碰）。"""
    ramp = ELEMENT_COLOR.get(element)
    if not ramp:
        return 0
    n = 0
    for em in ems:
        if (em.get("material") or "") != main_mat:
            continue
        em["color"] = list(ramp)
        em.setdefault("_notes", []).append("color<=element(%s)" % element)
        n += 1
    return n


def cap_sizes(ems):
    """按材质钉尺寸上限（防"大白锥/大光板"）。

    ⚠️ 2026-09-24 实查：只钉 `particle_size`（基础值）**不够** —— 有效尺寸 = `基础 + 曲线贡献 × 倍率`
    （渲染器与引擎同算法），曲线一大照样摊成板子（chainlightning / explosiongroundbig 就是这样）。
    所以曲线也要一起压。
    """
    for em in ems:
        cap = SIZE_CAP.get(em.get("material") or "")
        if not cap:
            continue
        v = em.get("particle_size")
        if isinstance(v, (tuple, list)):
            try:
                s = [float(x) for x in v]
                if s and s[0] > cap:
                    em["particle_size"] = tuple([cap] + s[1:])
                    em.setdefault("_notes", []).append("size cap %s->%s" % (s[0], cap))
            except (TypeError, ValueError):
                pass
        c = em.get("size_curve")
        if isinstance(c, (tuple, list)) and len(c) == 3:
            try:
                cc = [float(x) for x in c]
                if max(cc) > 1.35:                      # 曲线峰值超 1.35 倍就等于没上限了
                    k = 1.35 / max(cc)
                    em["size_curve"] = tuple(round(x * k, 3) for x in cc)
                    em.setdefault("_notes", []).append("curve cap x%.2f" % k)
            except (TypeError, ValueError):
                pass


def apply_tune(effect_name, ems):
    low = effect_name.lower()
    hit = 0
    for names, mat, k in TUNE_RULES:
        if not any(n in low for n in names):
            continue
        for em in ems:
            if (em.get("material") or "") != mat:
                continue
            for key, mul in k.items():
                if key == "alpha":
                    # alpha 是 [(t, v)] 关键帧表；**值可能是字符串**（spec 里按 XML 原文存）⇒ 必须 float()
                    if em.get("alpha"):
                        try:
                            em["alpha"] = [(t, min(1.0, float(v) * mul)) for (t, v) in em["alpha"]]
                        except (TypeError, ValueError):
                            pass
                    continue
                v = em.get(key)
                if isinstance(v, (tuple, list)):
                    out = []
                    for x in v:
                        try:
                            out.append(float(x) * mul)
                        except (TypeError, ValueError):
                            out.append(x)      # 非数值项原样保留
                    em[key] = tuple(out)
                else:
                    try:
                        em[key] = float(v) * mul
                    except (TypeError, ValueError):
                        pass                   # str / None 一律不碰
            em.setdefault("_notes", []).append("tune %s%s" % (mat.replace("prt_shd_", ""), k))
            hit += 1
    return hit


def apply_sprite(em):
    """把材质的惯用图集切法写进 emitter（**覆盖** UE 侧推来的值 —— 贴图说了算）。

    🔴 `"1, 1"` 也必须**显式写**，不能靠"删键"让默认值兜底：生成器的模板默认是 `2, 2`
    （照抄骨架来的），删键 ⇒ 2,2 顶上来 ⇒ 一张单格贴图被切 4 份，画出来是"硬方块"
    （2026-09-24 实查：frostbolt / lightningbolt 的方块就是这么来的）。
    """
    key, fc, fr, anim, rnd = MAT_SPRITE.get(em.get("material") or "", MAT_SPRITE_DEF)
    em["texture_sprite_count"] = key
    if fc > 1:
        em["texture_sprite_frame_count"] = fc
        em["texture_sprite_frame_rate"] = fr
    fl = em.setdefault("flags", {})
    fl["uses_sprite_animation"] = anim
    if rnd:
        fl["select_random_sprite"] = True
    em.setdefault("_notes", []).append("sprite %s frame=%d" % (key, fc))


def build_spec(json_path, out_name=None):
    d = json.load(open(json_path, encoding="utf-8"))
    if d["kind"] == "niagara":
        ems = [niagara_emitter_spec(e, d["asset"]) for e in d["emitters"]]
    else:
        ems = [cascade_emitter_spec(e) for e in d["emitters"]]
    bare = os.path.basename(d["asset"] or "effect").lower()
    name = out_name or ("lwn_" + re.sub(r"[^a-z0-9_]", "_", bare))
    el = element_override(name, ems)
    if el:
        main = dict((k[0], m) for k, m in ELEMENT_RULES).get(el)
        apply_element_color(el, ems, main)      # 元素配色（第 2 轮：只换材质不够，颜色也得换）
    # 🔴 尺寸补差（2026-09-24 第 3 轮）：UE 的尺寸在**文件级常量表**里，按命名空间分组；
    #    emitter 自己身上没有 ⇒ 全文件扫一遍再按名字前缀对位。不补的话 685 个发射器
    #    清一色吃生成器默认的 0.25±0.25（这就是"闪电是一块白板""暴风雪永远一坨"的真因）。
    sizes = collect_ue_sizes(d)
    for em in ems:
        if em.get("particle_size") is None:
            v = em_size_from_table(sizes, em.get("name"))
            if v:
                em["particle_size"] = v
                em.setdefault("_notes", []).append("size<=const %s m" % (v[0],))
    for em in ems:                    # 材质定下来之后才能定图集切法
        apply_sprite(em)
    apply_tune(name, ems)             # 再按人眼复验补差（TUNE_RULES）
    cap_sizes(ems)                    # 最后钉尺寸上限（火花/光点天生小元素）
    for em in ems:
        # 🔴 `max_alive_particle_count = 0` 在引擎里是「**一颗都不给**」，不是"无限"。
        #    2026-09-24 实查：火焰系发射器全是 0（UE 侧没这个概念）⇒ 实机里火球只会是空的。
        n = em.get("max_alive_particle_count")
        if n is None or int(float(n)) <= 0:
            rate = (em.get("emission_rate") or (10, 0))[0]
            life = em.get("particle_life") or (1, 0)
            est = float(rate) * (float(life[0]) + float(life[1]))
            em["max_alive_particle_count"] = max(60, int(est * 1.5))
            em.setdefault("_notes", []).append("maxAlive 0->%d" % em["max_alive_particle_count"])
    return {"name": name, "guid": guid_of(name), "emitters": ems,
            "_src_kind": d["kind"], "_src_asset": d["asset"], "_element": el}

_GUID_CACHE = {}
def guid_of(name):
    import hashlib
    h = hashlib.md5(("lwn_particle_" + name).encode()).hexdigest().upper()
    return "{%s-%s-%s-%s-%s}" % (h[0:8], h[8:12], h[12:16], h[16:20], h[20:32])

GEN = paths.GEN                       # 生成器在仓库里（同工具链根）
SPEC_DIR = paths.out("spec")
XML_DIR = paths.out("xml")

def clean_emitter(e):
    """剥掉仅供人看的 _ 前缀字段 —— gen_particle_effect 对未知键会直接 KeyError。"""
    return {k: v for k, v in e.items() if not k.startswith("_")}

def write_spec(spec, path):
    lines = ["# -*- coding: utf-8 -*-",
             "# 自动生成：pipeline/ue2bannerlord.py  <-  FCS(UE4.27) 粒子解析结果",
             "# 改参数请改转换器或上游解析，不要手改（铁律 22）",
             "EFFECTS = ["]
    eff = {"name": spec["name"], "guid": spec["guid"],
           "emitters": [clean_emitter(e) for e in spec["emitters"]]}
    import pprint
    body = pprint.pformat(eff, width=110, sort_dicts=False)
    lines.append(body)
    lines.append("]")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "w", encoding="utf-8").write("\n".join(lines) + "\n")

def gen_xml(spec_path, out_xml):
    import subprocess
    os.makedirs(os.path.dirname(out_xml), exist_ok=True)
    r = subprocess.run([sys.executable, GEN, spec_path, "-o", out_xml],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("jsonpaths", nargs="+")
    ap.add_argument("--name", default=None)
    ap.add_argument("--no-gen", action="store_true")
    a = ap.parse_args()
    for jp in a.jsonpaths:
        spec = build_spec(jp, a.name)
        base = os.path.splitext(os.path.basename(jp))[0]
        sp = os.path.join(SPEC_DIR, base + ".spec.py")
        xp = os.path.join(XML_DIR, spec["name"] + ".xml")
        write_spec(spec, sp)
        print("=" * 78)
        print("源    : %s  [%s / %s]" % (os.path.basename(jp), spec["_src_kind"], spec["_src_asset"]))
        print("effect: %s  emitters=%d" % (spec["name"], len(spec["emitters"])))
        for e in spec["emitters"]:
            print("   - %-24s %s" % (e["name"], "; ".join(e.get("_notes", []))[:150]))
        if a.no_gen:
            print("spec  -> %s" % sp); continue
        rc, log = gen_xml(sp, xp)
        print("生成  : rc=%d  %s" % (rc, xp if os.path.exists(xp) else "(未产出)"))
        if rc != 0:
            print("   !! 生成器报错:\n" + "\n".join(log.splitlines()[:14]))

if __name__ == "__main__":
    main()

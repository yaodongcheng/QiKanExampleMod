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
GRAV_SCALE = 1.0 / 980.0   # UE 重力常为 -980(cm/s²) -> 骑砍默认量级 -1

# --- UE 材质 -> 原版 prt_shd_* （顺序敏感：先具体后笼统；含 blend 语义） ---------
MAT_RULES = [
    (("fire_haze", "firehaze"),                        "prt_shd_fire_haze_1"),
    (("lightning", "electric", "thunder", "chain"),     "prt_shd_lightning"),
    (("flame", "fire", "ember", "burn", "torch", "meteor",
      "inferno", "lava", "fireball", "ignite"),         "prt_shd_fire_1"),
    (("spark",),                                        "prt_shd_sparks"),
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

    # --- 尺寸：ScaleSpriteSize + Vector2DFromCurve 的 X/Y 曲线
    curve = niagara_size_curve(em)
    if curve:
        spec["size_curve"] = curve; notes.append("size 曲线来自 Vector2DFromCurve")
    elif isinstance(em.get("_cascade_size"), list):
        pass

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
        si = r.get("sub_image_size")
        if si:
            mm = re.findall(r"[XY]=([\d.]+)", si)
            if len(mm) == 2 and float(mm[0]) > 0 and float(mm[1]) > 0:
                spec["texture_sprite_count"] = "%d, %d" % (int(float(mm[1])), int(float(mm[0])))
                spec.setdefault("flags", {})["select_random_sprite"] = True; notes.append("atlas %s" % si)
        al = r.get("alignment")
        if al in ALIGN2BB:
            spec["billboard_type"] = ALIGN2BB[al]; notes.append("align %s" % al)
    else:
        spec["material"] = "prt_shd_smoke_1"; notes.append("material 缺省")
    spec["_notes"] = notes
    return spec

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
        al = [("%.3f" % t, "%.3f" % min(max(v, 0.0), 1.0)) for t, v in a] \
             or [("0.000", "1.000"), ("1.000", "0.000")]
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
def build_spec(json_path, out_name=None):
    d = json.load(open(json_path, encoding="utf-8"))
    if d["kind"] == "niagara":
        ems = [niagara_emitter_spec(e, d["asset"]) for e in d["emitters"]]
    else:
        ems = [cascade_emitter_spec(e) for e in d["emitters"]]
    bare = os.path.basename(d["asset"] or "effect").lower()
    name = out_name or ("lwn_" + re.sub(r"[^a-z0-9_]", "_", bare))
    return {"name": name, "guid": guid_of(name), "emitters": ems,
            "_src_kind": d["kind"], "_src_asset": d["asset"]}

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

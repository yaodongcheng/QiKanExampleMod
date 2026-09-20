# -*- coding: utf-8 -*-
"""
t3d_parse.py -- 把 UE 的 T3D 文本解析成结构化 JSON。

T3D 是 UE 原生文本序列化格式（ObjectExporterT3D 产出）。本解析器面向粒子资产：
  * Niagara : NiagaraSystem > NiagaraEmitter > NiagaraGraph > NiagaraNodeFunctionCall
              + NiagaraScript.RapidIterationParameters（数值常量，字节流）
              + NiagaraSpriteRendererProperties / Ribbon / Mesh（材质/对齐/图集）
              + NiagaraDataInterface*Curve（RedCurve/GreenCurve/... 关键帧）
  * Cascade : ParticleSystem > ParticleModule*（Distribution* 数值）
              + ParticleSpriteEmitter > ParticleLODLevel.Modules(N)= 归属
              + ParticleModuleTypeData*（材质/网格/对齐/图集）

用法: python t3d_parse.py [t3d目录] [输出目录]
      两个参数都不给时，按 paths.py 的「数据根」定位（默认 D 盘那个）
"""
import os, re, sys, json, glob, struct
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
import paths          # 两根定位 TOOL/DATA —— 见工具链根 paths.py（realpath 穿透 junction）

# ---------------------------------------------------------------- 基础工具
def f32(raw):
    return struct.unpack("<f", raw)[0]

TYPE_KIND = {53: "half", 54: "half", 55: "float", 56: "float", 57: "int",
             58: "bool", 59: "vec2", 60: "vec3", 61: "vec4", 62: "color",
             63: "quat", 64: "vec4"}
TYPE_SIZE = {53: 2, 54: 2, 55: 4, 56: 4, 57: 4, 58: 1, 59: 8, 60: 12,
             61: 16, 62: 16, 63: 16, 64: 16}

def decode_bytes(raw, kind, ti):
    """按类型解码一段字节。"""
    n = len(raw)
    try:
        if kind in ("float",) and n >= 4: return round(f32(raw[:4]), 6)
        if kind == "int" and n >= 4: return struct.unpack("<i", raw[:4])[0]
        if kind == "bool" and n >= 1: return bool(raw[0])
        if kind == "half" and n >= 2: return round(struct.unpack("<e", raw[:2])[0], 6)
        if kind == "vec2" and n >= 8: return [round(x, 6) for x in struct.unpack("<2f", raw[:8])]
        if kind in ("vec3",) and n >= 12: return [round(x, 6) for x in struct.unpack("<3f", raw[:12])]
        if kind in ("color", "vec4", "quat") and n >= 16:
            return [round(x, 6) for x in struct.unpack("<4f", raw[:16])]
    except Exception:
        pass
    return {"raw": list(raw[:16]), "type_index": ti, "kind": kind}

# ---------------------------------------------------------------- 对象遍历
OBJ_RE = re.compile(r'^\s*Begin Object (?:(Class=)(\S+) )?Name="([^"]+)"\s*$')
END_RE = re.compile(r'^\s*End Object\s*$')
PROP_RE = re.compile(r'^\s{3,}([A-Za-z_][A-Za-z0-9_]*)(?:\((\d+)\))?=(.*)$')

class Obj:
    __slots__ = ("name", "cls", "props", "pins", "children", "line", "parent")
    def __init__(self, name, cls, line, parent):
        self.name, self.cls, self.line, self.parent = name, cls, line, parent
        self.props, self.pins, self.children = {}, [], []

def walk_objects(lines):
    """把 T3D 文本走成一棵对象树（返回根列表）。同名对象在 T3D 里会被重复声明，
    这里【保留每一次出现】由上层负责合并（首现带 Class=，后续出现只有 Name=）。"""
    roots, stack = [], []
    classes = {}
    for i, raw in enumerate(lines):
        m = OBJ_RE.match(raw)
        if m:
            cls = m.group(2)
            name = m.group(3)
            if cls:
                classes[name] = cls.split(".")[-1]
            o = Obj(name, classes.get(name, "?"), i, stack[-1] if stack else None)
            (stack[-1].children if stack else roots).append(o)
            stack.append(o)
            continue
        if END_RE.match(raw):
            if stack: stack.pop()
            continue
        if not stack: continue
        o = stack[-1]
        s = raw.strip()
        if s.startswith("CustomProperties Pin ("):
            o.pins.append(s); continue
        pm = PROP_RE.match(raw)
        if pm:
            key = pm.group(1)
            idx = pm.group(2)
            val = pm.group(3).rstrip()
            # !! 形如 Modules(0)=/Modules(1)= 的数组型属性，必须把下标并进键名，
            #    否则后面的会覆盖前面的（本轮踩过：LOD 模块全丢）。
            if idx is not None:
                o.props["%s(%s)" % (key, idx)] = val
            o.props[key] = val
    return roots, classes

def prop_of(o, key, default=None):
    return o.props.get(key, default)

def mergename(objs):
    """把同名对象的 props 合并（后出现的补齐先出现的空值）。"""
    merged = {}
    for o in objs:
        m = merged.setdefault(o.name, {"name": o.name, "cls": o.cls, "props": {}, "pins": []})
        if o.cls and o.cls != "?": m["cls"] = o.cls
        m["props"].update(o.props)
        m["pins"].extend(o.pins)
    return merged

# ---------------------------------------------------------------- Niagara 常量解码
RI_RE = re.compile(r'SortedParameterOffsets=\((.*?)\),ParameterData=\((.*?)\)(?:,DebugName="([^"]*)")?')
OFF_RE = re.compile(r'Offset=(\d+),Name="([^"]*)",TypeDefHandle=\(RegisteredTypeIndex=(\d+)\)')

def parse_rapid_iteration(text):
    """解析 RapidIterationParameters=(SortedParameterOffsets=...,ParameterData=...,DebugName=...)
    -> (debugName, [(offset,name,type_index)], [bytes]) 失败返回 None。"""
    m = RI_RE.search(text)
    if not m: return None
    offs, data, dbg = m.group(1), m.group(2), m.group(3)
    params = [(int(a), b, int(c)) for a, b, c in OFF_RE.findall(offs)]
    if not params: return None
    try:
        blob = bytes(int(x) for x in data.split(",") if x.strip() != "")
    except Exception:
        return None
    return dbg or "", params, blob

def decode_rapid_iteration(text):
    """-> {完整常量名: 解码值}，并附带推算的字节尺寸。"""
    r = parse_rapid_iteration(text)
    if not r: return {}, None
    dbg, params, blob = r
    params_sorted = sorted(params, key=lambda p: p[0])
    out = {}
    for i, (off, name, ti) in enumerate(params_sorted):
        # 尺寸优先用「下一个偏移 - 当前偏移」推断，末尾用类型表兜底
        if i + 1 < len(params_sorted):
            size = params_sorted[i + 1][0] - off
        else:
            size = TYPE_SIZE.get(ti, 4)
        if size <= 0: size = TYPE_SIZE.get(ti, 4)
        raw = blob[off:off + size]
        kind = TYPE_KIND.get(ti, "float")
        out[name] = {"type_index": ti, "kind": kind, "size": size,
                     "value": decode_bytes(raw, kind, ti), "bytes": list(raw)}
    return out, dbg

# ---------------------------------------------------------------- Niagara 解析
MODULE_PATH_RE = re.compile(r'^NiagaraScript\'"(.*?)"\'$')
MAT_RE = re.compile(r"^(Material|MaterialInterface)=(\w+)'\"([^\"]+)\"'")

def cls_of(name, obj):
    return obj.get("cls", "?")

def build_niagara(path, roots):
    """把一个 NiagaraSystem 的 T3D 树解析成结构化 dict。"""
    res = {"kind": "niagara", "asset": None, "emitters": [], "_modules_global": []}
    # 合并同名对象
    flat = {}
    def collect(o):
        m = flat.setdefault(o.name, {"name": o.name, "cls": o.cls, "props": {}, "pins": []})
        if o.cls and o.cls != "?": m["cls"] = o.cls
        m["props"].update(o.props); m["pins"].extend(o.pins)
        for c in o.children: collect(c)
    for r in roots: collect(r)

    # 系统名
    for n, o in flat.items():
        if o["cls"] == "NiagaraSystem": res["asset"] = n; break

    # 逐个 emitter
    for n, o in flat.items():
        if o["cls"] != "NiagaraEmitter": continue
        em = {"name": n, "modules": [], "constants": {}, "renderers": [],
              "data_interfaces": [], "sim_target": o["props"].get("SimTarget"),
              "enabled": o["props"].get("bIsEnabled")}
        res["emitters"].append(em)
    byname = {e["name"]: e for e in res["emitters"]}

    # 逐对象归位（用树的真实嵌套判定归属）
    def visit(o, cur_em):
        em = cur_em
        if o.cls == "NiagaraEmitter": em = byname.get(o.name, cur_em)
        if em is not None:
            if o.cls == "NiagaraNodeFunctionCall":
                fs = MODULE_PATH_RE.match(o.props.get("FunctionScript", "").strip())
                dn = o.props.get("FunctionDisplayName", "").strip('"')
                pins = {}
                for p in o.pins:
                    pn = re.search(r'PinName="([^"]*)"', p)
                    dv = re.search(r'DefaultValue="([^"]*)"', p)
                    lk = re.search(r'LinkedTo=\((.*?)\)', p)
                    if pn and (dv or lk):
                        pins[pn.group(1)] = {"default": dv.group(1) if dv else None,
                                             "linked": bool(lk and lk.group(1).strip(" ,"))}
                # T3D 会把同一节点序列化两次（首现是空壳），必须「合并」而不是「取首现」
                modmap = em.setdefault("_modmap", {})
                rec = modmap.get(o.name)
                if rec is None:
                    rec = {"node": o.name, "name": None, "script": None, "pins": {}}
                    modmap[o.name] = rec
                    em["modules"].append(rec)
                rec["_dn"] = rec.get("_dn") or dn
                rec["script"] = rec["script"] or (fs.group(1) if fs else None)
                rec["pins"].update(pins)
            elif o.cls and o.cls.startswith("NiagaraDataInterface"):
                em["data_interfaces"].append({"name": o.name, "cls": o.cls, "props": o.props})
            elif o.cls and ("RendererProperties" in o.cls or o.cls in
                            ("NiagaraSpriteRendererProperties", "NiagaraMeshRendererProperties",
                             "NiagaraRibbonRendererProperties", "NiagaraLightRendererProperties",
                             "NiagaraComponentRendererProperties", "NiagaraDecalRendererProperties")):
                mat = o.props.get("Material", "")
                mm = re.search(r"'\"([^\"]+)\"'", mat)
                em["renderers"].append({
                    "cls": o.cls, "name": o.name,
                    "material": mm.group(1) if mm else (mat or None),
                    "alignment": o.props.get("Alignment"), "facing_mode": o.props.get("FacingMode"),
                    "sub_image_size": o.props.get("SubImageSize"),
                    "mesh": (re.search(r"'\"([^\"]+)\"'", o.props.get("Mesh", "")) or [None, None])[1]
                            if o.props.get("Mesh") else None,
                    "sort_mode": o.props.get("SortMode") or o.props.get("SortOrderHint"),
                    "orientation": o.props.get("OrientationBinding"),
                    "pivot_offset": o.props.get("PivotOffset"),
                    "props": {k: v for k, v in o.props.items() if len(v) < 200},
                })
            elif o.cls == "NiagaraScript":
                # 数值常量：一次 NiagaraScript 可能只带一段 RapidIterationParameters
                consts, dbg = decode_rapid_iteration(" ".join(
                    "%s=%s" % (k, v) for k, v in o.props.items()))
                for k, v in consts.items():
                    v = dict(v); v["script"] = o.props.get("DebugName") or o.name
                    em["constants"][k] = v
        for c in o.children: visit(c, em)
    for r in roots: visit(r, None)

    # NiagaraScript 的 RapidIterationParameters 在 props 里被截断过？再全局扫一遍兜底
    for n, o in flat.items():
        for k, v in o["props"].items():
            if "RapidIterationParameters=" in v:
                consts, dbg = decode_rapid_iteration(v)
                for cn, cv in consts.items():
                    # 归属：Constants.<Emitter>.<Module>.<Input>
                    parts = cn.split(".")
                    tgt = byname.get(parts[1]) if parts[0] == "Constants" and len(parts) > 2 else None
                    if tgt is None and res["emitters"]:
                        tgt = res["emitters"][0]
                    if tgt is not None and cn not in tgt["constants"]:
                        cv = dict(cv); cv["script"] = "global-scan"
                        tgt["constants"][cn] = cv
    # 模块名收尾：优先 FunctionDisplayName，其次脚本路径末段（T3D 首现是空壳，
    # 直接取首现会把名字写成 NiagaraNodeFunctionCall_N —— 本轮踩过）
    for e in res["emitters"]:
        for m in e["modules"]:
            nm = m.get("_dn") or (m["script"].rsplit(".", 1)[-1] if m.get("script") else None)
            m["name"] = nm or m.get("node")
            m.pop("_dn", None)
    return res

# ---------------------------------------------------------------- 分布/曲线通用解析
DIST_REF_RE = re.compile(r"Distribution=(\w+)'\"([^\"]+)\"'")
KEYS_RE = re.compile(r'Keys=\((.*)\)\s*$')

def parse_keys(s):
    """UE 的 Keys=((InterpMode=..,Time=..,Value=..,ArriveTangent=..,LeaveTangent=..),...) -> list[dict]"""
    m = KEYS_RE.search(s)
    if not m: return []
    body = m.group(1)
    out = []
    for km in re.finditer(r'\(([^()]*)\)', body):
        seg = km.group(1)
        if not seg.strip(): continue
        d = {}
        for kv in re.finditer(r'([A-Za-z_]+)=([-\d.eE+]+)', seg):
            k, v = kv.group(1), kv.group(2)
            try: d[k] = float(v)
            except Exception: d[k] = v
        if d: out.append(d)
    return out

def resolve_distribution(valstr, flat):
    """把 'Lifetime=(MinValue=..,Distribution=DistributionFloatConstant'"X"',..)' 解析成
    {min,max,dist_type,dist_obj,constant,keys}。"""
    out = {}
    if not isinstance(valstr, str): return out
    for k in ("MinValue", "MaxValue"):
        m = re.search(r'\b%s=([-\d.eE+]+)' % k, valstr)
        if m:
            try: out[k[0].lower() + k[1:]] = float(m.group(1))
            except Exception: pass
    for k in ("Min", "Max"):
        m = re.search(r'\b%s=\(X=([-\d.eE+]+),Y=([-\d.eE+]+),Z=([-\d.eE+]+)' % k, valstr)
        if m:
            out[k.lower()] = [float(m.group(1)), float(m.group(2)), float(m.group(3))]
    m = re.search(r'\bConstant=\(X=([-\d.eE+]+),Y=([-\d.eE+]+),Z=([-\d.eE+]+)', valstr)
    if m: out["constant_vec"] = [float(m.group(1)), float(m.group(2)), float(m.group(3))]
    m = re.search(r'\bConstant=([-\d.eE+]+)', valstr)
    if m: out["constant"] = float(m.group(1))
    d = DIST_REF_RE.search(valstr)
    if d:
        out["dist_type"] = d.group(1); out["dist_obj"] = d.group(2)
        tgt = flat.get(d.group(2))
        if tgt:
            if "constant" not in out and "Constant" in tgt["props"]:
                try: out["constant"] = float(tgt["props"]["Constant"])
                except Exception: pass
            for ck in ("ConstantCurve", "CurveTable"):
                if ck in tgt["props"]:
                    ks = parse_keys(tgt["props"][ck])
                    if ks: out["keys"] = ks
    if "keys" not in out:
        ks = parse_keys(valstr)
        if ks: out["keys"] = ks
    return out

# ---------------------------------------------------------------- Cascade 解析
CASCADE_MOD_RE = re.compile(r"Modules\((\d+)\)=(\w+)'\"([^:]+):([^\"']+)\"'")

def build_cascade(path, roots):
    res = {"kind": "cascade", "asset": None, "emitters": []}
    flat = {}
    def collect(o):
        m = flat.setdefault(o.name, {"name": o.name, "cls": o.cls, "props": {}, "pins": []})
        if o.cls and o.cls != "?": m["cls"] = o.cls
        m["props"].update(o.props)
        for c in o.children: collect(c)
    for r in roots: collect(r)
    for n, o in flat.items():
        if o["cls"] == "ParticleSystem": res["asset"] = n; break

    # 每个 ParticleSpriteEmitter/MeshEmitter 一个"发射器"
    for n, o in flat.items():
        if not o["cls"].startswith("Particle") or "Emitter" not in o["cls"]: continue
        if o["cls"] in ("ParticleSpriteEmitter", "ParticleMeshEmitter", "ParticleBeamEmitter"):
            en = o["props"].get("EmitterName", n)
            em = {"name": (en.strip('"') if isinstance(en, str) else en), "cls": o["cls"],
                  "modules": [], "lod_refs": [], "props": o["props"]}
            res["emitters"].append(em)
    bylod = {}
    for n, o in flat.items():
        if o["cls"] == "ParticleLODLevel":
            refs = []
            for k, v in o["props"].items():
                if k.startswith("Modules("):
                    mm = re.search(r"'\"([^:]+):([^\"]+)\"'", v)
                    if mm: refs.append(mm.group(2))
                elif k in ("RequiredModule", "TypeData"):
                    # RequiredModule 不在 Modules(N) 数组里，是单独一项（材质/对齐/图集都在它上面）
                    mm = re.search(r"'\"([^:]+):([^\"]+)\"'", v)
                    if mm and k == "RequiredModule": refs.insert(0, mm.group(2))
            bylod[n] = (refs, o["props"].get("TypeData", ""))
    # emitter -> 它的 lod refs（T3D 里 ParticleLODLevel 常挂在 emitter 下）
    def visit(o, cur):
        if o.cls in ("ParticleSpriteEmitter", "ParticleMeshEmitter", "ParticleBeamEmitter"):
            en2 = o.props.get("EmitterName", o.name)
            if isinstance(en2, str): en2 = en2.strip('"')
            cur = next((e for e in res["emitters"] if e["name"] == en2
                        or e["name"] == o.name), cur)
        if o.cls == "ParticleLODLevel" and cur is not None:
            refs, td = bylod.get(o.name, ([], ""))
            cur["lod_refs"] = list(dict.fromkeys(cur["lod_refs"] + refs))
            if td and not cur.get("type_data_ref"):
                mm = re.search(r"'\"([^:]+):([^\"]+)\"'", td)
                cur["type_data_ref"] = mm.group(2) if mm else td
        for c in o.children: visit(c, cur)
    for r in roots: visit(r, None)

    # 模块 props -> 解析后的值
    for em in res["emitters"]:
        for rn in em["lod_refs"]:
            mo = flat.get(rn)
            if mo is None: continue
            vals = {}
            for k, v in mo["props"].items():
                if k in ("ModuleEditorColor", "bBakedDataSuccesfully", "bIsDirty", "LODValidity",
                         "bEditable", "bEnabled", "bSpawnModule", "bUpdateModule", "bCurvesAsColor"):
                    continue
                rv = resolve_distribution(v, flat)
                vals[k] = rv if rv else v
            em["modules"].append({"cls": mo["cls"], "name": mo["name"], "values": vals})
        td = flat.get(em.get("type_data_ref", ""))
        if td:
            mat = td["props"].get("Material", "")
            mm = re.search(r"'\"([^\"]+)\"'", mat)
            em["type_data"] = {"cls": td["cls"], "material": mm.group(1) if mm else None,
                               "props": td["props"]}
    return res

# ---------------------------------------------------------------- main
def parse_one(path):
    txt = open(path, encoding="utf-8", errors="replace").read()
    lines = txt.splitlines()
    roots, classes = walk_objects(lines)
    if not roots: return None
    rootcls = roots[0].cls
    if rootcls == "NiagaraSystem":  return build_niagara(path, roots)
    if rootcls == "ParticleSystem": return build_cascade(path, roots)
    return {"kind": "other", "asset": roots[0].name, "cls": rootcls}

def summarize(res):
    """给出一行摘要，便于快速浏览。"""
    if not res: return "(unparsed)"
    if res["kind"] == "niagara":
        n = len(res["emitters"])
        mods = sum(len(e["modules"]) for e in res["emitters"])
        mats = sorted({r.get("material") for e in res["emitters"] for r in e["renderers"] if r.get("material")})
        return "Niagara emitters=%d modules=%d materials=%s" % (n, mods, mats[:4])
    if res["kind"] == "cascade":
        n = len(res["emitters"])
        mods = sum(len(e["modules"]) for e in res["emitters"])
        mats = sorted({e.get("type_data", {}).get("material") for e in res["emitters"] if e.get("type_data")})
        return "Cascade emitters=%d modules=%d materials=%s" % (n, mods, mats[:4])
    return res.get("cls", "?")

def main():
    src = sys.argv[1] if len(sys.argv) > 1 else paths.out("t3d")
    dst = sys.argv[2] if len(sys.argv) > 2 else paths.out("parsed")
    os.makedirs(dst, exist_ok=True)
    files = sorted(glob.glob(os.path.join(src, "*.t3d")))
    index, ni, ca, bad = [], 0, 0, 0
    for f in files:
        try:
            res = parse_one(f)
        except Exception as e:
            res = None
            print("PARSE-ERR %s: %s" % (os.path.basename(f), e))
        if res is None:
            bad += 1; continue
        base = os.path.basename(f).replace(".t3d", "")
        res["_source"] = f
        res["_id"] = base
        for e in res.get("emitters", []):
            e.pop("_seen", None); e.pop("_modmap", None)
        out = os.path.join(dst, base + ".json")
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(res, fh, ensure_ascii=False, indent=1)
        sm = summarize(res)
        if res["kind"] == "niagara": ni += 1
        elif res["kind"] == "cascade": ca += 1
        index.append({"id": base, "kind": res["kind"], "asset": res.get("asset"),
                      "emitters": len(res.get("emitters", [])), "summary": sm})
    with open(os.path.join(dst, "_index.json"), "w", encoding="utf-8") as fh:
        json.dump(index, fh, ensure_ascii=False, indent=1)
    print("== 解析完成: 共 %d, niagara=%d cascade=%d 失败=%d ==" % (len(files), ni, ca, bad))
    for it in index[:12]:
        print("  %-62s %s" % (it["id"][:62], it["summary"][:90]))

if __name__ == "__main__":
    main()

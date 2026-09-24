# -*- coding: utf-8 -*-
"""粒子特效全量拆解 v2：系统 → emitter（阶段/模块栈/参数值）→ 渲染器（材质/网格/图集）→ 材质 → 贴图。

关键解码：
  1) 阶段：emitter 的 {EmitterSpawn,EmitterUpdate,Spawn,Update}ScriptProps → 该 NiagaraScript 的 Usage；
     模块自身的路径（/Niagara/Modules/<阶段名>/...）作为交叉印证。
  2) 参数值：NiagaraScript.RapidIterationParameters =
       SortedParameterOffsets=((Offset,Name,TypeDefHandle=(RegisteredTypeIndex)),...) + ParameterData=(字节)
     按偏移排序，后用前者的差值当长度，再用类型索引解成 float/向量/颜色。
"""
import os, io, re, sys, json, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t3d_tools as T

from paths import DUMP_ROOT as OUT
from paths import T3D_DIR
VFX = os.path.join(T3D_DIR, "vfx")
MAT = os.path.join(OUT, "t3d", "mat")
MISC = os.path.join(OUT, "t3d", "misc")

RENDERER = ('NiagaraSpriteRendererProperties', 'NiagaraMeshRendererProperties',
            'NiagaraRibbonRendererProperties', 'NiagaraLightRendererProperties',
            'NiagaraDecalRendererProperties', 'NiagaraComponentRendererProperties')


def path_index(root):
    idx = {}

    def walk(n, path):
        p = path + (n['name'],) if n['name'] != '__root__' else ()
        if p:
            e = idx.setdefault(p, {'cls': None, 'props': []})
            if n['cls']:
                e['cls'] = n['cls']
            e['props'].extend(n['props'])
        for c in n['children']:
            walk(c, p)
    for c in root['children']:
        walk(c, ())
    return idx


def gp(e, key, default=None):
    for p in e['props']:
        if p.startswith(key + '='):
            return p[len(key) + 1:]
    return default


def unq(s):
    return s.strip('"') if s else s


def f32(b):
    return struct.unpack('<f', bytes(b))[0]


def decode_params(e):
    """解析一个 NiagaraScript 的 RapidIterationParameters → [(名字, 值)]"""
    raw = gp(e, 'RapidIterationParameters')
    if not raw or 'SortedParameterOffsets' not in raw:
        return []
    offs = []
    for m in re.finditer(r'\(Offset=(\d+),Name="([^"]+)",TypeDefHandle=\(RegisteredTypeIndex=(\d+)\)\)', raw):
        offs.append((int(m.group(1)), m.group(2), int(m.group(3))))
    md = re.search(r'ParameterData=\(([\d,]*)\)', raw)
    if not md or not offs:
        return []
    data = [int(x) for x in md.group(1).split(',') if x != '']
    offs.sort()
    out = []
    for i, (off, name, tid) in enumerate(offs):
        end = offs[i + 1][0] if i + 1 < len(offs) else len(data)
        seg = data[off:end]
        if not seg:
            continue
        if tid == 55 and len(seg) >= 4:
            v = f32(seg[:4])
        elif tid == 59 and len(seg) >= 8:
            v = [round(f32(seg[j:j + 4]), 4) for j in (0, 4)]
        elif tid == 60 and len(seg) >= 12:
            v = [round(f32(seg[j:j + 4]), 4) for j in (0, 4, 8)]
        elif tid == 62 and len(seg) >= 16:
            v = [round(f32(seg[j:j + 4]), 4) for j in (0, 4, 8, 12)]
        elif len(seg) == 4:
            iv = struct.unpack('<i', bytes(seg))[0]
            v = {'float': round(f32(seg), 4), 'int': iv}
        else:
            v = '<%d bytes>' % len(seg)
        out.append((name, v))
    return out


# ---------------------------------------------------------------- 材质
_mat_cache = {}


def mat_file(name):
    for d in (MAT, MISC, VFX):
        p = os.path.join(d, name + '.t3d')
        if os.path.exists(p):
            return p
    return None


def resolve_material(name, depth=0, seen=None):
    if seen is None:
        seen = set()
    if name in _mat_cache:
        return _mat_cache[name]
    if name in seen or depth > 5:
        return {'textures': [], 'parent': None, 'functions': []}
    seen.add(name)
    res = {'parent': None, 'textures': [], 'functions': []}
    p = mat_file(name)
    if p:
        txt = io.open(p, encoding='utf-8', errors='replace').read()
        m = re.search(r"Parent=(?:Material|MaterialInstance\w*)'\"([^\"]+)\"", txt)
        if m:
            res['parent'] = m.group(1).split('.')[-1]
        for mm in re.finditer(r"ParameterName=\"([^\"]+)\"[^)]*?Texture=Texture2D'\"([^\"]+)\"", txt):
            res['textures'].append([mm.group(1), mm.group(2)])
        for mm in re.finditer(r"TextureParameterValues\(\d+\)=\(ParameterInfo=\(Name=\"([^\"]+)\"\)[^)]*ParameterValue=Texture2D'\"([^\"]+)\"", txt):
            res['textures'].append([mm.group(1), mm.group(2)])
        for mm in re.finditer(r"MaterialFunction=MaterialFunction'\"([^\"]+)\"", txt):
            res['functions'].append(mm.group(1).split('.')[-1])
        if res['parent'] and res['parent'] != name:
            up = resolve_material(res['parent'], depth + 1, seen)
            res['textures'] += up.get('textures', [])
            res['functions'] += up.get('functions', [])
        # 材质函数里可能才有贴图
        for fn in list(dict.fromkeys(res['functions'])):
            fp = mat_file(fn)
            if not fp:
                continue
            ftxt = io.open(fp, encoding='utf-8', errors='replace').read()
            for mm in re.finditer(r"ParameterName=\"([^\"]+)\"[^)]*?Texture=Texture2D'\"([^\"]+)\"", ftxt):
                res['textures'].append([mm.group(1) + ' (' + fn + ')', mm.group(2)])
    seen.discard(name)
    seq, s = [], set()
    for x in res['textures']:
        k = tuple(x)
        if k not in s:
            s.add(k)
            seq.append(x)
    res['textures'] = seq
    res['functions'] = list(dict.fromkeys(res['functions']))
    _mat_cache[name] = res
    return res


STAGE_OF_SCRIPT = {
    'EmitterSpawnScript': 'EmitterSpawn',
    'EmitterUpdateScript': 'EmitterUpdate',
    'SpawnScript': 'ParticleSpawn',
    'UpdateScript': 'ParticleUpdate',
    'SystemSpawnScript': 'SystemSpawn',
    'SystemUpdateScript': 'SystemUpdate',
    'GPUComputeScript': 'GPUCompute',
}


def stage_from_module(path):
    if '/Niagara/Modules/Emitter/' in path:
        return 'Emitter级'
    if '/Niagara/Modules/Spawn/' in path:
        return 'ParticleSpawn'
    if '/Niagara/Modules/Update/' in path:
        return 'ParticleUpdate'
    if '/Niagara/Modules/Render/' in path:
        return 'Render'
    if '/Niagara/DynamicInputs/' in path or '/Niagara/Modules/Solvers/' in path:
        return '动态输入/求解器'
    if path.startswith('/Game/'):
        return '自定义模块'
    return '?'


def extract(system_t3d):
    root, scopes, graphs = T.parse_t3d(system_t3d)
    idx = path_index(root)
    sysname = os.path.basename(system_t3d)[:-4]
    out = {'system': sysname, 'emitters': []}
    for p, e in sorted(idx.items()):
        if len(p) != 2 or not (e['cls'] or '').endswith('NiagaraEmitter'):
            continue
        em = {'name': p[1], 'local_space': gp(e, 'bLocalSpace') == 'True',
              'interpolated': gp(e, 'bInterpolatedSpawning') == 'True',
              'renderers': [], 'params': [], 'stages': {}}
        # 阶段 → 脚本 → 参数
        for key, stage in STAGE_OF_SCRIPT.items():
            v = gp(e, key + 'Props')
            if not v:
                continue
            mm = re.search(r"Script=NiagaraScript'\"([^\"]+)\"", v)
            if not mm:
                continue
            sname = mm.group(1).split('.')[-1].split(':')[-1]
            se = idx.get((sysname, p[1], sname)) or idx.get((sysname, sname))
            if se is None:
                se = scopes.get(('root',), {}).get(sname)
            if se is not None:
                for nm, val in decode_params(se):
                    em['params'].append({'stage': stage, 'name': nm, 'value': val})
        # 渲染器
        for p2, e2 in sorted(idx.items()):
            if len(p2) == 3 and p2[0] == sysname and p2[1] == p[1] and (e2['cls'] or '').endswith(RENDERER):
                r = {'type': e2['cls'].split('.')[-1]}
                for k in ('Material', 'Mesh', 'Alignment', 'FacingMode', 'SubImageSize', 'SortMode'):
                    v = gp(e2, k)
                    if v:
                        r[k] = v
                mat = r.get('Material')
                if mat:
                    mn = re.search(r"'\"([^\"]+)\"", mat)
                    r['material'] = mn.group(1).split('.')[-1] if mn else mat
                    r['textures'] = resolve_material(r['material']).get('textures', [])
                em['renderers'].append(r)
        # 模块栈（按模块所属阶段分组）
        for p3, e3 in sorted(idx.items()):
            if len(p3) != 5 or p3[:2] != (sysname, p[1]) or not (e3['cls'] or '').endswith('NiagaraNodeFunctionCall'):
                continue
            fs = gp(e3, 'FunctionScript') or ''
            mm = re.search(r"'\"([^\"]+)\"", fs)
            mod = mm.group(1) if mm else fs
            st = stage_from_module(mod)
            em['stages'].setdefault(st, [])
            if mod.split('/')[-1] not in em['stages'][st]:
                em['stages'][st].append(mod.split('/')[-1])
        out['emitters'].append(em)
    return out


def main():
    filt = sys.argv[1] if len(sys.argv) > 1 else None
    files = sorted(f for f in os.listdir(VFX) if f.endswith('.t3d') and (f.startswith('NS_') or f.startswith('NE_')))
    allres = []
    for f in files:
        if filt and filt.lower() not in f.lower():
            continue
        try:
            allres.append(extract(os.path.join(VFX, f)))
        except Exception as ex:
            allres.append({'system': f[:-4], 'error': repr(ex)})
    json.dump(allres, io.open(os.path.join(OUT, 'digest', 'vfx_breakdown.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)
    dst = os.path.join(OUT, 'digest', 'VFX_BREAKDOWN.md')
    with io.open(dst, 'w', encoding='utf-8') as w:
        w.write("# 粒子特效逐系统全量拆解（自动生成）\n\n")
        w.write("结构：系统 → emitter → 渲染器（材质/网格/图集）+ 模块栈（按阶段）+ **模块参数值**（从 RapidIterationParameters 解出）\n")
        w.write("参数名形如 `Constants.<emitter>.<模块>.<参数>`；值按类型解成 float / 向量 / 颜色。\n\n")
        for r in allres:
            if r.get('error'):
                w.write("## %s ⚠️ %s\n\n" % (r['system'], r['error']))
                continue
            w.write("## %s\n\n" % r['system'])
            for em in r['emitters']:
                w.write("### emitter `%s`%s%s\n\n" % (em['name'],
                        "（局部空间）" if em['local_space'] else "",
                        "（插值生成）" if em['interpolated'] else ""))
                for rd in em['renderers']:
                    w.write("- 渲染器 `%s`" % rd['type'])
                    if rd.get('material'):
                        w.write(" 材质 `%s`" % rd['material'])
                    if rd.get('Mesh'):
                        w.write(" 网格 `%s`" % rd['Mesh'].split('/')[-1].rstrip("'"))
                    for k in ('Alignment', 'FacingMode', 'SubImageSize', 'SortMode'):
                        if rd.get(k):
                            w.write("  %s=%s" % (k, rd[k]))
                    w.write("\n")
                    if rd.get('textures'):
                        w.write("  - 贴图: %s\n" % ', '.join('`%s` ← `%s`' % (n, t.split('/')[-1]) for n, t in rd['textures'][:10]))
                for st, mods in em['stages'].items():
                    w.write("- 模块[%s]: %s\n" % (st, ' → '.join('`%s`' % m for m in mods)))
                if em['params']:
                    w.write("- 参数值（%d 条，节选）:\n" % len(em['params']))
                    for prm in em['params'][:40]:
                        w.write("    - `%s` = %s\n" % (prm['name'].replace('Constants.' + em['name'] + '.', ''), prm['value']))
                w.write("\n")
    print("VFX2_DONE", len(allres))


if __name__ == '__main__':
    main()

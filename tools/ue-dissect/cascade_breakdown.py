# -*- coding: utf-8 -*-
"""Cascade（UE 老粒子 UParticleSystem）全量拆解 + 模块普查。

结构（比 Niagara 直白，值全在具名属性里，不用解字节流）：
  ParticleSystem
    Emitters(n) → ParticleSpriteEmitter / ParticleMeshEmitter / …
        EmitterName="…"  LODLevels(n) → ParticleLODLevel
            RequiredModule → ParticleModuleRequired（材质/时长/延迟/图集/朝向）
            SpawnModule    → ParticleModuleSpawn（Rate / BurstList）
            Modules(n)     → ParticleModule*(Lifetime/Size/Color/Location/Velocity/…)
            TypeDataModule → ParticleModuleTypeData*(Sprite/Mesh/Ribbon/Beam/AnimTrail)
输出：out/digest/CASCADE_BREAKDOWN.md + cascade_census.json
"""
import os, io, re, sys, json, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t3d_tools as T

from paths import DUMP_ROOT as OUT
from paths import T3D_DIR
VFX = os.path.join(T3D_DIR, "vfx")
SKIP = ('NodePos', 'ChangeId', 'MergeId', 'Cached', 'ModuleEditorColor', 'bIsSoloing',
        'bBakedData', 'bIsDirty', 'LODValidity', 'bEditable', 'bEnabled', 'bIsInstance')


def ents(path):
    root, scopes, graphs = T.parse_t3d(path)
    return scopes.get(('root',), {})


def gp(e, key):
    for p in e['props']:
        if p.startswith(key + '='):
            return p[len(key) + 1:]
    return None


def all_gp(e, key):
    return [p for p in e['props'] if p.startswith(key)]


def name_of(ref):
    m = re.search(r"'\"([^\"]+)\"|Name=\"([^\"]+)\"", ref or '')
    if not m:
        return (ref or '?').strip('"')
    v = m.group(1) or m.group(2)
    return v.split(':')[-1] if ':' in v else v


def dist_value(e, propname):
    """把一个 Distribution 属性（Rate/Lifetime/StartSize/StartVelocity/…）读成 {min,max,mode,curve}"""
    raw = gp(e, propname)
    if raw is None:
        return None
    d = {'raw': raw[:200]}
    m = re.search(r'MinValue=(-?[\d.]+)', raw)
    if m:
        d['min'] = float(m.group(1))
    m = re.search(r'MaxValue=(-?[\d.]+)', raw)
    if m:
        d['max'] = float(m.group(1))
    for tag in ('MinValueVec', 'MaxValueVec'):
        m = re.search(tag + r'=\(X=(-?[\d.]+),Y=(-?[\d.]+),Z=(-?[\d.]+)\)', raw)
        if m:
            d[tag] = [float(x) for x in m.groups()]
    m = re.search(r"Distribution=(\w+)'\"([^\"]+)\"", raw)
    if m:
        d['dist'] = m.group(1)
        d['dist_name'] = name_of(m.group(2))
    return d


def extract(path):
    E = ents(path)
    name = os.path.basename(path)[:-4]
    best = {}
    for k, e in E.items():
        c = (e['cls'] or '').split('.')[-1]
        if c:
            best.setdefault(c, []).append((k, e))
    out = {'system': name, 'emitters': [], 'module_census': collections.Counter(),
           'dist_census': collections.Counter()}
    _add_census(E, out)
    ps = (best.get('ParticleSystem') or [(None, None)])[0][1]
    if ps is None:
        return out
    for line in all_gp(ps, 'Emitters('):
        em_ref = name_of(line.split('=', 1)[1])
        em = {'ref': em_ref, 'levels': []}
        # 找这个 emitter 对象
        for k, e in (best.get('ParticleSpriteEmitter', []) + best.get('ParticleMeshEmitter', []) +
                     best.get('ParticleBeamEmitter', []) + best.get('ParticleRibbonEmitter', []) +
                     best.get('ParticleAnimTrailEmitter', []) + best.get('ParticleGPUNodeEmitter', [])):
            if k == em_ref:
                em['type'] = (e['cls'] or '').split('.')[-1]
                em['name'] = (gp(e, 'EmitterName') or em_ref)
                for lv in all_gp(e, 'LODLevels('):
                    em['levels'].append(name_of(lv.split('=', 1)[1]))
                break
        # 每个 LOD 的模块
        for li, lref in enumerate(em['levels']):
            lv = None
            for k, e in best.get('ParticleLODLevel', []):
                if k == lref:
                    lv = e
            if lv is None:
                continue
            L = {'level': gp(lv, 'Level') or str(li), 'enabled': gp(lv, 'bEnabled') != 'False'}
            req = name_of((gp(lv, 'RequiredModule') or '').split('=', 1)[-1])
            for k, e in best.get('ParticleModuleRequired', []):
                if k == req:
                    L['required'] = {
                        'material': name_of(gp(e, 'Material') or ''),
                        'material_full': (gp(e, 'Material') or '').split("'\"")[-1].rstrip("'"),
                        'emitter_duration': gp(e, 'EmitterDuration'),
                        'emitter_delay': gp(e, 'EmitterDelay'),
                        'subimages': '%sx%s' % (gp(e, 'SubImages_Horizontal'), gp(e, 'SubImages_Vertical')),
                        'alignment': gp(e, 'ScreenAlignment'),
                        'interp': gp(e, 'InterpolationMethod'),
                    }
                    break
            sp = name_of((gp(lv, 'SpawnModule') or '').split('=', 1)[-1])
            for k, e in best.get('ParticleModuleSpawn', []):
                if k == sp:
                    L['spawn'] = {'rate': dist_value(e, 'Rate'), 'rate_scale': dist_value(e, 'RateScale'),
                                  'bursts': len(all_gp(e, 'BurstList('))}
                    break
            mods = []
            for line2 in all_gp(lv, 'Modules('):
                mref = name_of(line2.split('=', 1)[1])
                cls = re.sub(r'_\d+$', '', mref)
                for k, e in best.get(cls, []):
                    if k == mref:
                        md = {'module': cls.replace('ParticleModule', '')}
                        for p in e['props']:
                            key = p.split('=', 1)[0]
                            if key.startswith(SKIP) or key in ('LODValidity',):
                                continue
                            dv = dist_value(e, key)
                            md[key] = dv if dv else p.split('=', 1)[1][:90]
                        mods.append(md)
                        break
            td = name_of((gp(lv, 'TypeDataModule') or '').split('=', 1)[-1])
            L['typedata'] = td
            L['modules'] = mods
            em.setdefault('lod', []).append(L)
        out['emitters'].append(em)
    return out


def _add_census(E, out):
    for k, e in E.items():
        c = (e['cls'] or '').split('.')[-1]
        if not c:
            continue
        if c.startswith('ParticleModule'):
            out['module_census'][c] += 1
        elif c.startswith('Distribution'):
            out['dist_census'][c] += 1


def main():
    files = sorted(f for f in os.listdir(VFX) if f.endswith('.t3d'))
    cand = []
    for f in files:
        p = os.path.join(VFX, f)
        head = io.open(p, encoding='utf-8', errors='replace').readline()
        if 'ParticleSystem' in head:
            cand.append(f)
    res = []
    cen = collections.Counter()
    dcen = collections.Counter()
    for f in cand:
        try:
            r = extract(os.path.join(VFX, f))
        except Exception as e:
            r = {'system': f[:-4], 'error': repr(e)}
        cen.update(r.get('module_census', {}))
        dcen.update(r.get('dist_census', {}))
        res.append(r)
    json.dump({'systems': res, 'module_census': dict(cen), 'dist_census': dict(dcen)},
              io.open(os.path.join(OUT, 'digest', 'cascade_census.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)
    with io.open(os.path.join(OUT, 'digest', 'CASCADE_BREAKDOWN.md'), 'w', encoding='utf-8') as w:
        w.write("# Cascade（老粒子）逐系统拆解\n\n")
        for r in res:
            if r.get('error'):
                w.write("## %s ⚠️ %s\n\n" % (r['system'], r['error']))
                continue
            w.write("## %s\n\n" % r['system'])
            for em in r['emitters']:
                w.write("- emitter `%s` (%s) LOD 层数=%d\n" % (em.get('name'), em.get('type'), len(em.get('levels') or [])))
                for L in (em.get('lod') or []):
                    if not L.get('enabled'):
                        continue
                    req = L.get('required') or {}
                    w.write("  - LOD%s: 材质 `%s` 时长=%s 延迟=%s 图集=%s %s | SpawnRate=%s | TypeData=%s\n" % (
                        L.get('level'), req.get('material'), req.get('emitter_duration'),
                        req.get('emitter_delay'), req.get('subimages'), req.get('alignment') or '',
                        (L.get('spawn') or {}).get('rate', {}).get('max') if isinstance((L.get('spawn') or {}).get('rate'), dict) else '',
                        (L.get('typedata') or '').replace('ParticleModuleTypeData', '')))
                    for md in L.get('modules', []):
                        parts = []
                        for k, v in md.items():
                            if k == 'module':
                                continue
                            if isinstance(v, dict):
                                if 'min' in v and 'max' in v and v.get('min') != v.get('max'):
                                    parts.append('%s=%s~%s' % (k, v['min'], v['max']))
                                elif 'min' in v:
                                    parts.append('%s=%s' % (k, v['min']))
                                elif v.get('MaxValueVec'):
                                    parts.append('%s=%s~%s' % (k, v.get('MinValueVec'), v['MaxValueVec']))
                                elif v.get('dist'):
                                    parts.append('%s[%s]' % (k, v.get('dist_name')))
                                else:
                                    parts.append('%s=?' % k)
                            else:
                                parts.append('%s=%s' % (k, str(v)[:40]))
                        w.write("    · %-22s %s\n" % (md['module'], ' '.join(parts[:8])))
            w.write("\n")
        w.write("\n## 模块普查（整个工程 34 个 Cascade）\n\n")
        for k, v in cen.most_common():
            w.write("- `%s` × %d\n" % (k, v))
        w.write("\n## 分布类型普查\n\n")
        for k, v in dcen.most_common():
            w.write("- `%s` × %d\n" % (k, v))
    print("CASCADE_DONE", len(res), "modules:", len(cen), "dists:", len(dcen))


if __name__ == '__main__':
    main()

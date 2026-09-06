# -*- coding: utf-8 -*-
# build_blends.py — Blender 5.2 batch: 织丰场景重建(Main_map / sho_town_a) + prefab 陈列 + 道具
# data: scene.xscene(实体 transform) + Prefabs/*.xml(树) + extracted_sho/meshes/**/*.obj
# usage: blender -b --python build_blends.py -- map|town|prefabs|showcase|all
import bpy, re, os, sys
from mathutils import Matrix, Vector, Euler

BASE = r'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12'
SHO = BASE + r'\Mount & Blade II Bannerlord\Modules\Shokuho'
MESH_DIR = BASE + r'\extracted_sho\meshes'
OUT = BASE + r'\blend_projects'
os.makedirs(OUT, exist_ok=True)

ENT = re.compile(r'<game_entity([^>]*)>')
CLOSE = re.compile(r'</game_entity>')
ATTR = re.compile(r'(\w+)="([^"]*)"')
MESH = re.compile(r'<meta_mesh_component name="([^"]+)"')
TFORM = re.compile(r'<transform ([^>]*)/?>')


def get_span_map(xml):
    ev = [(m.start(), True, m) for m in ENT.finditer(xml)]
    ev += [(m.start(), False, m) for m in CLOSE.finditer(xml)]
    ev.sort(key=lambda e: e[0])
    spans, stack = {}, []
    for pos, is_open, m in ev:
        if is_open:
            stack.append(m)
        elif stack:
            o = stack.pop()
            spans[o.start()] = pos + m.end()
    return spans


def attrs_of(m):
    return dict(ATTR.findall(m.group(1)))


class Node:
    __slots__ = ('name', 'pos', 'rot', 'scl', 'meshes', 'children')

    def __init__(self):
        self.name = ''
        self.pos = (0.0, 0.0, 0.0)
        self.rot = (0.0, 0.0, 0.0)
        self.scl = (1.0, 1.0, 1.0)
        self.meshes = []
        self.children = []


DEFAULT_T = {'position': (0.0, 0.0, 0.0), 'rotation_euler': (0.0, 0.0, 0.0), 'scale': (1.0, 1.0, 1.0)}


def parse_xml_tree(xml, spans):
    """建 Node 树, 返回顶层 Node 列表"""
    ev = []
    for m in ENT.finditer(xml):
        ev.append(('O', m))
    for m in CLOSE.finditer(xml):
        ev.append(('C', m))
    ev.sort(key=lambda e: e[1].start())
    roots, stack = [], []
    for kind, m in ev:
        if kind == 'O':
            a = attrs_of(m)
            node = Node()
            node.name = a.get('prefab') or a.get('name') or ''
            end = spans.get(m.start())
            seg = xml[m.end():end]
            t = TFORM.search(seg)
            if t:
                for k, v in ATTR.findall(t.group(1)):
                    vals = tuple(float(x) for x in v.split(','))
                    if len(vals) == 3 and k in DEFAULT_T:
                        setattr(node, {'position': 'pos', 'rotation_euler': 'rot', 'scale': 'scl'}[k], vals)
            node.meshes = list(set(MESH.findall(seg)))
            if stack:
                stack[-1].children.append(node)
            else:
                roots.append(node)
            stack.append(node)
        else:
            if stack:
                stack.pop()
    return roots


def build_prefabs(prefabs_dir):
    nodes = {}
    for fn in sorted(os.listdir(prefabs_dir)):
        if not fn.endswith('.xml'):
            continue
        pxml = open(os.path.join(prefabs_dir, fn), encoding='utf-8').read()
        for node in parse_xml_tree(pxml, get_span_map(pxml)):
            if node.name and not node.name.startswith('_'):
                nodes.setdefault(node.name, node)
    return nodes


# ---------- mesh 模板缓存 ----------
_mono = {}
_miss = set()

FILES_INDEX = {}


def build_index():
    for root, dirs, files in os.walk(MESH_DIR):
        for f in files:
            if f.endswith('.obj'):
                FILES_INDEX.setdefault(f[:-4], os.path.join(root, f))


def load_obj_data(path, name):
    """纯 py obj 解析 -> mesh datablock (Blender 5.2 无 obj importer)"""
    vs = []
    vts = []
    faces = []
    fuv = []
    with open(path, encoding='utf-8', errors='ignore') as f:
        for line in f:
            if line.startswith('v '):
                parts = line.split()
                vs.append((float(parts[1]), float(parts[2]), float(parts[3])))
            elif line.startswith('vt '):
                p = line.split()
                vts.append((float(p[1]), float(p[2])))
            elif line.startswith('f '):
                idx = []
                for tok in line.split()[1:]:
                    sp = tok.split('/')
                    vi = int(sp[0]) - 1
                    ti = int(sp[1]) - 1 if len(sp) > 1 and sp[1] else -1
                    idx.append((vi, ti))
                faces.append([i for i, _ in idx])
                fuv.append(idx)
    me = bpy.data.meshes.new(meta_safe(name))
    me.from_pydata(vs, [], [tuple(fi) for fi in faces])
    me.update()
    if vts:
        uvl = me.uv_layers.new(name='UVMap')
        for poly in me.polygons:
            tui = fuv[poly.index]
            for k in range(poly.loop_total):
                li = poly.loop_start + k
                vt = tui[k % len(tui)][1]
                if vt is not None and vt >= 0:
                    uvl.data[li].uv = (vts[vt][0], 1.0 - vts[vt][1])
    return me


def import_obj_mesh(name):
    if name in _mono:
        return _mono[name]
    if name in _miss:
        return None
    fp = FILES_INDEX.get(name)
    if fp is None:
        _miss.add(name)
        return None
    try:
        data = load_obj_data(fp, name)
    except Exception:
        _miss.add(name)
        return None
    _mono[name] = data
    return data


def files_fast(files, name):
    return (name + '.obj') in files


def meta_safe(s):
    return re.sub(r'[^A-Za-z0-9_\-]', '_', s)[:60]


def make_obj(mn, wm, counter):
    md = import_obj_mesh(mn)
    if md is None:
        return
    ob = bpy.data.objects.new(meta_safe(mn) + '_%d' % (counter[0] % 100000), md)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = wm
    counter[0] += 1
    if counter[0] % 500 == 0:
        print('objects:', counter[0], flush=True)


def walk(n, pm, counter):
    lm = Matrix.LocRotScale(Vector(n.pos), Euler(n.rot, 'XYZ'), Vector(n.scl))
    wm = pm @ lm
    for mn in n.meshes:
        make_obj(mn, wm, counter)
    for c in n.children:
        walk(c, wm, counter)


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def finish(name):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name))
    print('SAVED', name)


def rebuild_scene(scene_dir, label, outfile):
    fresh()
    xml = open(os.path.join(scene_dir, 'scene.xscene'), encoding='utf-8').read()
    spans = get_span_map(xml)
    pnodes = build_prefabs(SHO + r'\Prefabs')
    build_index()
    print('index:', len(FILES_INDEX), 'meshes', flush=True)
    counter = [0, 0]
    print('prefabs:', len(pnodes), flush=True)
    placed = skipped = 0
    for m in ENT.finditer(xml):
        s = m.start()
        if s not in spans:
            continue
        a = attrs_of(m)
        pname = a.get('prefab') or a.get('name') or ''
        if not pname:
            continue
        seg = xml[m.end():m.end() + 1200]
        t = DEFAULT_T
        tm = TFORM.search(seg)
        if tm:
            for k, v in ATTR.findall(tm.group(1)):
                vals = tuple(float(x) for x in v.split(','))
                if len(vals) == 3 and k in DEFAULT_T:
                    t = dict(t)
                    t[k] = vals
        lm = Matrix.LocRotScale(Vector(t['position']), Euler(t['rotation_euler'], 'XYZ'), Vector(t['scale']))
        if placed % 250 == 0 and placed > 0:
            print('entities so far:', placed, 'objects:', counter[0], flush=True)
        node = pnodes.get(pname)
        if node is not None:
            walk(node, lm, counter)
            placed += 1
        else:
            blk = xml[s:spans[s]]
            for mn in set(MESH.findall(blk)):
                make_obj(mn, lm, counter)
            if MESH.findall(blk):
                placed += 1
            else:
                skipped += 1
    print(label, 'placed entities:', placed, 'skipped(no mesh):', skipped,
          'missing meshes:', len(_miss), 'objects:', counter[0], flush=True)
    finish(outfile)
    print('done', flush=True)


def showcase():
    fresh()
    counter = [0, 0]
    print('prefabs:', len(pnodes), flush=True)
    picks = ['sho_tate_shield_a', 'sho_banner_wall_a', 'bo_sho_woodenbarrel', 'sho_stone_block_a',
             'sho_sliding_door_wood', 'sho_structuralwood_beam_a', 'sho_house_flag_4',
             'sho_boat_row_1', 'sho_breastplate_a', 'sho_katana_a', 'bamboo_5', 'arrow_new_icon']
    x = 0.0
    for nm in picks:
        lm = Matrix.LocRotScale(Vector((x, 0, 0)), Euler((0, 0, 0)), Vector((1.5, 1.5, 1.5)))
        make_obj(nm, lm, counter)
        x += 10.0
    finish('05_showcase.blend')


if __name__ == '__main__':
    which = 'all'
    if '--' in sys.argv:
        which = sys.argv[sys.argv.index('--') + 1]
    if which in ('map', 'all'):
        rebuild_scene(SHO + r'\SceneObj\Main_map', 'MAIN_MAP', '03_map_scene_rebuild.blend')
    if which in ('town', 'all'):
        rebuild_scene(SHO + r'\SceneObj\sho_town_a', 'TOWN_A', '04_mission_scene_rebuild.blend')
    if which in ('showcase', 'all'):
        showcase()

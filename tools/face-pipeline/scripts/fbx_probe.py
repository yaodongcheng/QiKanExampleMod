# -*- coding: utf-8 -*-
"""
fbx_probe.py — 只读 FBX（7.x 二进制）关键字段，回答"这个 FBX 交给编辑器会落在哪"。

背景（2026-09-13）：蒂法换头工程卡在"头看不见"。根因 = FBX 的**单位声明**决定引擎
导入时的换算：UnitScaleFactor=100（1 单位=1 米）→ 数值原样进引擎；UnitScaleFactor=1
（1 单位=1 厘米）→ 引擎把数值 ×100。几何本身再被翻转一次就彻底飞出可视范围。

本脚本读出：
  1. GlobalSettings 的 UpAxis / FrontAxis / CoordAxis / UnitScaleFactor
  2. 每个 Geometry(Vertices) 数组的真实包围盒（FBX 存储坐标系）
  3. 每个 Model 节点的 Lcl Translation / Lcl Scaling（Blender 常见的 scale 残留）
  4. Deformer(SubDeformer) 的 Transform / TransformLink 矩阵（骨骼绑定位置）

用法：
  python fbx_probe.py <file.fbx> [--full]
"""
import struct
import sys
import zlib

ARRAY_TYPES = {b'f': ('f', 4), b'd': ('d', 8), b'l': ('q', 8), b'i': ('i', 4), b'b': ('b', 1)}


class Node:
    __slots__ = ('name', 'props', 'children')

    def __init__(self, name):
        self.name = name
        self.props = []
        self.children = []


def read_prop(b, i, end):
    t = b[i:i + 1]
    i += 1
    if t == b'Y':
        return struct.unpack('<h', b[i:i + 2])[0], i + 2
    if t == b'C':
        return bool(b[i]), i + 1
    if t == b'I':
        return struct.unpack('<i', b[i:i + 4])[0], i + 4
    if t == b'F':
        return struct.unpack('<f', b[i:i + 4])[0], i + 4
    if t == b'D':
        return struct.unpack('<d', b[i:i + 8])[0], i + 8
    if t == b'L':
        return struct.unpack('<q', b[i:i + 8])[0], i + 8
    if t in (b'S', b'R'):
        n = struct.unpack('<I', b[i:i + 4])[0]
        return b[i + 4:i + 4 + n].decode('utf-8', 'replace'), i + 4 + n
    if t in ARRAY_TYPES:
        n, enc, clen = struct.unpack('<III', b[i:i + 12])
        i += 12
        fmt, size = ARRAY_TYPES[t]
        if enc == 0:
            raw = b[i:i + n * size]
            i += n * size
        else:
            raw = zlib.decompress(b[i:i + clen])
            i += clen
        return list(struct.unpack('<%d%s' % (n, fmt), raw[:n * size])), i
    raise ValueError('unknown prop type %r at %d' % (t, i - 1))


def read_node(b, i, end):
    if i >= end:
        return None
    if b[i:i + 4] == b'\x00\x00\x00\x00':
        # null record 结尾
        if end - i <= 13:
            return None
    end_offset, n_props, _plen, name_len = struct.unpack('<IIIB', b[i:i + 13])
    if end_offset == 0:
        return None
    name = b[i + 13:i + 13 + name_len].decode('utf-8', 'replace')
    node = Node(name)
    i += 13 + name_len
    for _ in range(n_props):
        v, i = read_prop(b, i, end)
        node.props.append(v)
    while i < end_offset:
        child = read_node(b, i, end_offset)
        if child is None:
            break
        node.children.append(child)
        # 跳转到 child 的 end offset
        cend, _np, _pl, _nl = struct.unpack('<IIIB', b[i:i + 13])
        i = cend
    return node


def parse(path):
    b = open(path, 'rb').read()
    ver = struct.unpack('<I', b[23:27])[0]
    root = Node('ROOT')
    i = 27
    while i < len(b):
        end_offset, _np, _pl, name_len = struct.unpack('<IIIB', b[i:i + 13])
        if end_offset == 0:
            break
        node = read_node(b, i, end_offset)
        if node is None:
            break
        root.children.append(node)
        i = end_offset
    return ver, root


def find(root, name):
    out = []

    def walk(n):
        for c in n.children:
            if c.name == name:
                out.append(c)
            walk(c)
    walk(root)
    return out


def vec3(node):
    v = node.props
    return v


def main():
    path = sys.argv[1]
    full = '--full' in sys.argv
    ver, root = parse(path)
    print('== %s ==' % path)
    print('   FBX version = %d' % ver)

    gs = find(root, 'GlobalSettings')
    if gs:
        for p70 in find(gs[0], 'Properties70'):
            for p in p70.children:
                if p.name == 'P' and p.props and isinstance(p.props[0], str):
                    key = p.props[0]
                    if key in ('UpAxis', 'FrontAxis', 'CoordAxis', 'OriginalUpAxis',
                               'UpAxisSign', 'FrontAxisSign', 'CoordAxisSign',
                               'UnitScaleFactor', 'OriginalUnitScaleFactor'):
                        val = p.props[-1]
                        print('   %-24s = %s' % (key, val))
                        if key == 'UnitScaleFactor':
                            if val == 100:
                                print('      -> 1 单位 = 1 米：引擎数值原样进（期望值）')
                            elif val == 1:
                                print('      -> 1 单位 = 1 厘米：引擎会把数值 ×100（本工程踩过的坑）')
                            else:
                                print('      -> 非标准值：引擎换算倍率 = 100 / %s' % val)

    geos = find(root, 'Geometry')
    print('   Geometry 节点数 = %d' % len(geos))
    for g in geos:
        gname = g.props[1] if len(g.props) > 1 else '?'
        verts = [c for c in g.children if c.name == 'Vertices']
        if not verts:
            continue
        off = 0
        if isinstance(gname, str) and gname.startswith('Geometry::'):
            off = len('Geometry::')
        label = gname[off:] if isinstance(gname, str) else '?'
        v = verts[0].props[0]
        xs, ys, zs = v[0::3], v[1::3], v[2::3]
        print('   [%s] verts=%d  x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]  (%.4f x %.4f x %.4f)'
              % (label, len(xs), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
                 max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))

    # Model 节点上的非单位缩放 = Blender 侧 scale 残留
    shown = 0
    for m in find(root, 'Model'):
        scale = None
        trans = None
        rot = None
        for c in m.children:
            if c.name == 'Properties70':
                for p in c.children:
                    if p.name == 'P' and p.props and isinstance(p.props[0], str):
                        if p.props[0] == 'Lcl Scaling':
                            scale = p.props[-3:]
                        elif p.props[0] == 'Lcl Translation':
                            trans = p.props[-3:]
                        elif p.props[0] == 'Lcl Rotation':
                            rot = p.props[-3:]
        name = m.props[1] if len(m.props) > 1 else '?'
        if isinstance(name, str) and name.startswith('Model::'):
            name = name[len('Model::'):]
        bad_scale = scale and any(abs(s - 1.0) > 1e-6 for s in scale)
        bad_rot = rot and any(abs(r) > 1e-6 for r in rot)
        bad_trans = trans and any(abs(t) > 1e-6 for t in trans)
        # 位置有偏移的网格节点必须报出来 —— 纯平移不会触发 scale/rot 告警，但引擎照样会把
        # 它烘进顶点（2026-09-13 踩过：嘴/眼球节点带平移 → 渲染时飞离面部）
        if not (bad_scale or bad_rot) and not (bad_trans and name.startswith("head_tifa")):
            continue
        shown += 1
        if shown > 12:
            continue
        print('   Model %-26s scale=%-28s rot=%-26s trans=%s%s%s'
              % (name,
                 [round(s, 4) for s in scale] if scale else None,
                 [round(r, 3) for r in rot] if rot else None,
                 [round(t, 5) for t in trans] if trans else None,
                 '  <== 非单位缩放!' if bad_scale else '',
                 '  <== 非零旋转!' if bad_rot else ''))
    if shown > 12:
        print('   ... 另有 %d 个带非单位变换的 Model 节点' % (shown - 12))

    if full:
        subs = find(root, 'Deformer')
        print('   Deformer 节点数 = %d' % len(subs))
        for d in subs:
            dn = d.props[1] if len(d.props) > 1 else '?'
            tl = [c for c in d.children if c.name == 'TransformLink']
            if not tl:
                continue
            mat = tl[0].props[0]
            tx, ty, tz = mat[12], mat[13], mat[14]
            ss = [abs(mat[0]), abs(mat[5]), abs(mat[10])]
            print('   %-34s TransformLink 平移=(%.4f, %.4f, %.4f) 轴缩放=%s'
                  % (str(dn)[:34], tx, ty, tz, ['%.4f' % s for s in ss]))


if __name__ == '__main__':
    main()

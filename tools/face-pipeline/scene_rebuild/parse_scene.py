# -*- coding: utf-8 -*-
"""parse_scene.py — 场景展开规划: xscene 实体 -> Prefabs XML 树 -> metamesh 名单

产出 scene_plan.json: { "mesh_uses": {mesh名: 次数}, "entity_names": [..], "prefab_index": n }
用法: python parse_scene.py <SceneObj场景目录> <Shokuho/Prefabs目录> <out.json>
"""
import re, os, sys, json
from collections import Counter

ENT = re.compile(r'<game_entity([^>]*)>')
ATTR = re.compile(r'(\w+)="([^"]*)"')
MESH = re.compile(r'<meta_mesh_component name="([^"]+)"')


def block_of(xml, start):
    """取从 <game_entity> 到对应 </game_entity> 的完整块文本"""
    depth = 1
    pos = start
    while depth > 0:
        nxt = re.search(r'</?game_entity[^>]*>', xml[pos:])
        if not nxt:
            return None
        tok = nxt.group(0)
        pos += nxt.end()
        depth += -1 if tok.startswith('</') else 1
    return xml[start:pos] if depth == 0 else None


def scan_spans(xml):
    """一次性线性扫: 返回 {open_start: 块结束pos} for 所有成对 game_entity"""
    events = []
    for m in ENT.finditer(xml):
        events.append((m.start(), True, m))
    for m in re.finditer(r'</game_entity>', xml):
        events.append((m.start(), False, m))
    events.sort()
    spans = {}
    stack = []
    for pos, is_open, m in events:
        if is_open:
            stack.append(m)
        else:
            if stack:
                o = stack.pop()
                spans[o.start()] = pos + m.end()
    return spans


def main():
    scene_dir, prefabs_dir, out_json = sys.argv[1], sys.argv[2], sys.argv[3]
    xml = open(os.path.join(scene_dir, 'scene.xscene'), encoding='utf-8').read()

    # 1) prefab 注册表: name -> 块文本 (含 children / meta_mesh_component)
    prefabs = {}
    for fn in sorted(os.listdir(prefabs_dir)):
        if not fn.endswith('.xml'):
            continue
        pxml = open(os.path.join(prefabs_dir, fn), encoding='utf-8').read()
        pspans = scan_spans(pxml)
        for m in ENT.finditer(pxml):
            attrs = dict(ATTR.findall(m.group(1)))
            nm = attrs.get('name')
            if nm and m.start() in pspans:
                prefabs.setdefault(nm, pxml[m.start():pspans[m.start()]])

    # 2) 场景实体 -> 展开 (用线性 spans)
    spans = scan_spans(xml)
    mesh_uses = Counter()
    scene_entities = []
    for m in ENT.finditer(xml):
        start = m.start()
        if start not in spans:
            continue
        attrs = dict(ATTR.findall(m.group(1)))
        nm = attrs.get('name') or attrs.get('prefab') or ''
        if not nm:
            continue
        blk = xml[start:spans[start]]
        scene_entities.append(nm)
        for mm in MESH.finditer(blk):
            mesh_uses[mm.group(1)] += 1
        seen_sub = set()
        if nm in prefabs:
            for mm in MESH.finditer(prefabs[nm]):
                mesh_uses[mm.group(1)] += 1
            for sub in ENT.finditer(prefabs[nm]):
                sub_attrs = dict(ATTR.findall(sub.group(1)))
                subnm = sub_attrs.get('name') or sub_attrs.get('prefab') or ''
                if subnm and subnm in prefabs and subnm != nm and subnm not in seen_sub:
                    seen_sub.add(subnm)
                    for mm in MESH.finditer(prefabs[subnm]):
                        mesh_uses[mm.group(1)] += 1

    print('scene entities:', len(scene_entities))
    print('distinct metamesh names:', len(mesh_uses))
    for k, v in mesh_uses.most_common(18):
        print('  ', k, v)

    plan = {
        'scene': os.path.basename(scene_dir),
        'mesh_uses': dict(mesh_uses),
        'entity_names': scene_entities,
        'prefab_index': len(prefabs),
        'scene_entities': {},
    }
    json.dump(plan, open(out_json, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('wrote', out_json)


if __name__ == '__main__':
    main()

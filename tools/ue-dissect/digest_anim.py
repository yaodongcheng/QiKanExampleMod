# -*- coding: utf-8 -*-
"""动画摘要：通知对象 + 通知时间/时长 + 段落（蒙太奇）。"""
import os, io, re, sys, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t3d_tools as T

from paths import DUMP_ROOT as OUT
D = os.path.join(OUT, "digest")


def find_tree(root, name):
    res = []
    def f(n):
        if n['name'] == name:
            res.append(n)
        for c in n['children']:
            f(c)
    for c in root['children']:
        f(c)
    return res


def dig(name):
    p = os.path.join(OUT, "t3d", "anim", name + ".t3d")
    if not os.path.exists(p):
        return None
    root, scopes, graphs = T.parse_t3d(p)
    top = scopes.get(('root',), {}).get(name)
    if not top:
        return None
    out = ["### %s" % name]
    for pr in top['props']:
        if pr.startswith(('SequenceLength=', 'RateScale=', 'bEnableRootMotion=')):
            out.append("  " + pr)
    # 通知数组：Notifies(n)=(...)
    notifs = []
    for pr in top['props']:
        m = re.match(r'Notifies\((\d+)\)=\((.*)\)$', pr)
        if m:
            idx = int(m.group(1))
            kv = {}
            for part in T.split_top(m.group(2)):
                if '=' in part:
                    k, v = part.split('=', 1)
                    kv[k.strip()] = v.strip()
            notifs.append((idx, kv))
    notifs.sort()
    for idx, kv in notifs:
        out.append("  通知[%d]: %s  time=%s dur=%s track=%s" % (
            idx, kv.get('NotifyName', kv.get('NotifyClass', '?')),
            kv.get('TriggerTime', kv.get('LinkValue', '')), kv.get('Duration', ''),
            kv.get('TrackIndex', '')))
    # 蒙太奇段落
    for pr in top['props']:
        if pr.startswith('CompositeSections(') or pr.startswith('SlotAnimTracks('):
            out.append("  " + pr[:260])
    # 嵌套通知对象（拿到类名与参数）
    for tn in find_tree(root, name):
        def walk(n, d=0):
            for c in n['children']:
                if c['cls'] and ('AnimNotify' in c['cls']):
                    out.append("  通知对象: %s  %s" % (c['cls'].split('.')[-1], c['name']))
                    for pr in c['props'][:10]:
                        out.append("      " + pr[:200])
                walk(c, d + 1)
        walk(tn)
    return "\n".join(out)


def main():
    inv = json.load(io.open(os.path.join(OUT, "01_inventory.json"), encoding="utf-8"))
    parts = []
    n = 0
    for r in inv:
        if r.get("class") in ("AnimMontage", "AnimSequence", "AnimComposite"):
            s = dig(r["name"])
            if s:
                parts.append(s)
                n += 1
    io.open(os.path.join(D, "_anims.txt"), "w", encoding="utf-8").write("\n\n".join(parts))
    print("ANIM_DIGEST_DONE", n)


if __name__ == '__main__':
    main()

# -*- coding: utf-8 -*-
"""给 RDR2 场景补「新增地形层」的节点开关与权重槽。

场景：ModKit 里 Add Layer 后，`<layers>` 里多了层，但每个节点的
  · `layer_is_used_mask_{summer,fall,winter,spring}` 仍是 1（只认 layer0）
  · `<variable layer="N" value="0"/>`（没给新层分配 weightmap 槽）
⇒ 新层不渲染。本脚本把它们补上。

用法：
  python fix_rdr2_layer_node_masks.py --scene Main_map --layers 2 [--dry-run]
"""
import argparse
import io
import os
import re

MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
SCENES = {
    "Main_map": os.path.join(MODULES, "RDR2", "SceneObj", "Main_map", "scene.xscene"),
    "rdr2_sanit_denis": os.path.join(MODULES, "RDR2", "SceneObj", "rdr2_sanit_denis", "scene.xscene"),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", required=True, choices=sorted(SCENES))
    ap.add_argument("--layers", type=int, required=True)
    ap.add_argument("--weight-level", type=int, default=8)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    path = SCENES[args.scene]
    with open(path, "rb") as f:
        txt = f.read().decode("utf-8")
    bak = path + ".layermask_bak"
    if not os.path.isfile(bak):
        with open(bak, "wb") as f:
            f.write(txt.encode("utf-8"))
        print("backup ->", bak)

    mask = (1 << args.layers) - 1
    out, n_mask = re.subn(r'layer_is_used_mask_(summer|fall|winter|spring)="\d+"',
                          lambda m: 'layer_is_used_mask_%s="%d"' % (m.group(1), mask), txt)
    print("layer_is_used_mask -> %d (%d 处)" % (mask, n_mask))

    total_w = 0
    for li in range(1, args.layers):
        out, n = re.subn(r'(<variable layer="%d" value=")\d+(")' % li,
                         r'\g<1>%d\g<2>' % args.weight_level, out)
        total_w += n
        print("weightmap layer %d -> %d (%d 处)" % (li, args.weight_level, n))

    # 规整 water 层的空/错误贴图槽（原版 river 层：areamap/normal/displacement = none）
    m = re.search(r'<layer name="water">.*?</layer>', out, re.S)
    if m:
        block = m.group(0)
        fixed = block
        for typ in ("areamap", "normalmap", "displacementmap"):
            fixed = re.sub(r'(<texture\s+type="%s"\s+name=")[^"]*(")' % typ,
                           r'\g<1>none\g<2>', fixed)
        out = out.replace(block, fixed)
        print("water layer texture slots normalized")
    else:
        print("warn: no <layer name=\"water\">")

    if args.dry_run:
        print("dry-run: not written")
        return
    with open(path, "wb") as f:
        f.write(out.encode("utf-8"))
    print("wrote", path)


if __name__ == "__main__":
    main()

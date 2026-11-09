# -*- coding: utf-8 -*-
"""把 prefabs/sdn.json 转成 Bannerlord prefab XML（远景碎片）。

- 子节点位置 = pos_local（相对据点锚点，单位米）
- 旋转 = 四元数 (x,y,z,w) → 引擎 Mat3.ApplyEulerAngles 口径
  （x=asin(f.z), y=atan2(-s.z,u.z), z=atan2(-f.x,f.y)）
- 缩放 = (sxy, sxy, sz)
- 输出两份：
  · rdr2_sdn_lod       大地图缩略用（容器缩放 1:14.x）
  · rdr2_sdn_lod_1to1  1:1 场景用（不缩放）
"""
import io
import json
import math
import os
import re
import subprocess

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
TOOL = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
OUT = os.path.join(MOD, "Prefabs", "rdr2_lod_prefabs.xml")


def metamesh_name(model):
    """TRF 导入时引擎会去掉末尾的 _lod / _lodNNN 再命名 MetaMesh。"""
    return re.sub(r"_lod\d*$", "", model, flags=re.I)


def imported_assets():
    p = subprocess.run([TOOL, "list", "--packdir", os.path.join(MOD, "Assets", "models", "sdn_test")],
                       capture_output=True, text=True)
    out = (p.stdout or "")
    assets = set()
    for line in out.splitlines():
        if "\t" not in line:
            continue
        parts = line.split()
        if not parts:
            continue
        n = parts[0]
        if n.endswith(".trf"):
            continue
        if re.match(r"^[A-Za-z0-9_].*", n):
            assets.add(n)
    return assets

# 世界 → 地图单位（x 768/10768，y 576/8024，z 50/725.278）
SX = 768.0 / 10768.0
SY = 576.0 / 8024.0
SZ = 50.0 / (690.3469848632812 - (-34.93079376220703))


def quat_to_euler(qx, qy, qz, qw):
    # 标准四元数旋转矩阵（列向量 = local 基在世界中的像）
    xx, yy, zz = qx * qx, qy * qy, qz * qz
    xy, xz, yz = qx * qy, qx * qz, qy * qz
    wx, wy, wz = qw * qx, qw * qy, qw * qz
    s = (1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy))       # side (X)
    f = (2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx))       # forward (Y)
    u = (2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy))       # up (Z)
    ex = math.asin(max(-1.0, min(1.0, f[2])))
    ey = math.atan2(-s[2], u[2])
    ez = math.atan2(-f[0], f[1])
    return ex, ey, ez


def fmt(v):
    return "%.3f" % v


def frag_xml(f):
    p = f["pos_local"]
    r = f.get("rot", [0, 0, 0, 1])
    s = f.get("scale", [1, 1])
    ex, ey, ez = quat_to_euler(r[0], r[1], r[2], r[3])
    name = metamesh_name(f["name"])
    return (
        '\t\t\t<game_entity name="%s" mobility="1">\n'
        '\t\t\t\t<transform position="%s, %s, %s" rotation_euler="%s, %s, %s" scale="%s, %s, %s"/>\n'
        '\t\t\t\t<components>\n'
        '\t\t\t\t\t<meta_mesh_component name="%s"/>\n'
        '\t\t\t\t</components>\n'
        '\t\t\t</game_entity>\n'
        % (f["frag_id"], fmt(p[0]), fmt(p[1]), fmt(p[2]),
           fmt(ex), fmt(ey), fmt(ez), fmt(s[0]), fmt(s[0]), fmt(s[1]), name)
    )


def main():
    g = json.load(io.open(os.path.join(DELIV, "manifests", "prefabs", "sdn.json"), encoding="utf-8"))
    frags = g["fragments"]
    # 引擎导入 TRF 时会去掉 _lod/_lodNNN；按实际 MetaMesh 资产名匹配
    have = imported_assets()
    keep = [f for f in frags if metamesh_name(f["name"]) in have]
    skipped = [f for f in frags if metamesh_name(f["name"]) not in have]
    print("fragments=%d kept=%d skipped=%d" % (len(frags), len(keep), len(skipped)))

    # 只允许已知的“交付里本来就没网格”的件；其它缺失 = 导入/命名回归，直接失败
    KNOWN_MISSING = {"nopq_11_14_area_slod4__split_sdn"}
    unexpected = [f for f in skipped if f["name"] not in KNOWN_MISSING]
    if unexpected:
        print("UNEXPECTED missing MetaMesh (not in known missing list):")
        for f in unexpected:
            print("  ", f["frag_id"], f["name"], "->", metamesh_name(f["name"]))
        raise SystemExit("FAIL: unexpected missing meshes; import TRF first or fix name mapping")

    body = "".join(frag_xml(f) for f in keep)
    xml = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        "<!-- 圣丹尼斯 LOD 远景碎片 prefab（2026-11-09 自动生成） -->\n"
        "<prefabs>\n"
        '\t<game_entity name="rdr2_sdn_lod" mobility="1">\n'
        '\t\t<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>\n'
        "\t\t<children>\n"
        '\t\t\t<game_entity name="map_scale" mobility="1">\n'
        '\t\t\t\t<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000" '
        'scale="%s, %s, %s"/>\n'
        "\t\t\t\t<children>\n%s\t\t\t\t</children>\n"
        "\t\t\t</game_entity>\n"
        "\t\t</children>\n"
        "\t</game_entity>\n"
        '\t<game_entity name="rdr2_sdn_lod_1to1" mobility="1">\n'
        '\t\t<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>\n'
        "\t\t<children>\n%s\t\t</children>\n"
        "\t</game_entity>\n"
        "</prefabs>\n"
        % (fmt(SX), fmt(SY), fmt(SZ), body, body)
    )
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(xml)
    print("wrote", OUT, "%.1f KB" % (os.path.getsize(OUT) / 1024))
    if skipped:
        print("skipped:", [(f["frag_id"], f["name"]) for f in skipped])


if __name__ == "__main__":
    main()

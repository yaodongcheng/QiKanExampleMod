#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
旗帜与家纹体检（雷 130 / 131）
========================================================================
查四件事，全部离线可查：

  ① **结构**：`ModuleData/banner_icons.xml` 过得了引擎 `BannerManager.LoadFromXml` 的两条硬检查
     —— `doc.ChildNodes[1]` 必须是 `<base>`、`<base>.ChildNodes[0]` 必须是 `<BannerIconData>`。
     违反 = 开局 `TWXmlLoadException: Incorrect XML document format`（雷 130；雷 50 同款）。
     ⚠️ 判据口径：minidom **不把 XML 声明算作子节点**，所以 .NET 的 `ChildNodes[1]` == minidom 的 `childNodes[0]`。
        用 `documentElement` 取根会**跳过注释**、对这条完全免疫（第一版就这么写废的）。

  ② **段位**：图标 id 唯一、且不与别人撞段。现状 原版 1..535 / 织丰 536..839 / Taikou 840..1159。
     id 是先到先得（`BannerIconGroup.Deserialize` 跳过已被占用的 id）——撞段 = 后加载的那个整段静默失效。

  ③ **材质**：`banner_icons.xml` 里引用的每个 `material_name` 必须在自家 `AssetPackages/*.tpac` 里
     真实存在（二进制 grep）。缺 = 引擎 `Material.GetFromResource` 得 null → **整块跳过，旗只剩底色**
     （静默失效，最难查的一种）。

  ④ **几何**（雷 131）：所有带家纹的 `banner_key` 必须共用**同一套几何模板**。
     继承「借来的底旗」几何 → 一包 55 种不同位置/尺寸 → 实机「有的家纹巨大、有的小到看不见」
     （极端例：背景在 (4922,4922) 而图标在 (471,471)，名牌按旗形裁剪 → 图标被裁掉只剩一丝）。

Usage:
  python Scripts/check_taikou_banner_icons.py [--module <Taikou 模块目录>]
Exit: 0 绿 / 1 红
"""
import argparse
import os
import re
import sys
import glob
from xml.dom import minidom

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_MODULE = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou"

ICON_ID_MIN = 840          # Taikou 家纹段起点（原版 1..535 / 织丰 536..839）
CANON_BG_GEOM = ("1536", "1536", "764", "764", "1", "0", "0")
CANON_ICON_GEOM = ("483", "483", "764", "764", "0", "0", "0")
CANON_BG_MESH = "11"


def kids(node):
    return [c for c in node.childNodes
            if not (c.nodeType == c.TEXT_NODE and not c.data.strip())]


def check_structure(path):
    """① 引擎那两条硬检查。"""
    bad = []
    try:
        doc = minidom.parse(path)
    except Exception as ex:
        return ["XML 解析失败：%s" % ex]
    top = kids(doc)
    if not top or top[0].nodeName != "base":
        bad.append("XML 声明与根元素之间夹了 %s（引擎 ChildNodes[1] 必须是 base）"
                   % (top[0].nodeName if top else "(空文档)"))
        return bad
    inner = kids(top[0])
    if not inner or inner[0].nodeName != "BannerIconData":
        bad.append("<base> 第一个子节点是 %s，必须是 BannerIconData"
                   % (inner[0].nodeName if inner else "(空)"))
    return bad


def check_icons(path):
    """② 图标 id 段位与唯一性；③ 材质存在性。返回 (错误[], 警告[], id集合, 材质集合)。"""
    errs, warns = [], []
    doc = minidom.parse(path)
    icons = doc.getElementsByTagName("Icon")
    ids, mats = [], set()
    for it in icons:
        ids.append(int(it.getAttribute("id")))
        mats.add(it.getAttribute("material_name"))
    dup = {i for i in ids if ids.count(i) > 1}
    if dup:
        errs.append("图标 id 重复：%s（违反 BannerIcons.xsd 的 Icon/@id 唯一约束）"
                    % sorted(dup)[:8])
    below = [i for i in ids if i < ICON_ID_MIN]
    if below:
        errs.append("图标 id 落在 %d 之前（%s…）——与 原版 1..535 / 织丰 536..839 撞段"
                    % (ICON_ID_MIN, sorted(below)[:8]))
    return errs, warns, set(ids), mats


def check_materials(mats, module):
    """③ 材质必须在自家 tpac 里真实存在（二进制 grep，秒级）。"""
    errs = []
    packs = glob.glob(os.path.join(module, "AssetPackages", "*.tpac"))
    if not packs:
        return ["AssetPackages 下没有任何 tpac——家纹材质无处可寻"]
    blob = b""
    for p in packs:
        try:
            with open(p, "rb") as f:
                blob += f.read()
        except OSError as ex:
            errs.append("读不了 %s：%s" % (os.path.basename(p), ex))
    for m in sorted(mats):
        if m and m.encode("ascii", "ignore") not in blob:
            errs.append("材质 %s 在 AssetPackages 里找不到（引擎将静默跳过该图标，旗只剩底色）" % m)
    return errs


def check_geometry(module, icon_ids):
    """④ 带家纹的 banner_key 必须共用同一套几何模板。"""
    errs, warns = [], []
    files = sorted(glob.glob(os.path.join(module, "ModuleData", "spclans*.xml"))) + \
            sorted(glob.glob(os.path.join(module, "ModuleData", "spkingdoms*.xml")))
    geos = {}
    dangling = set()
    total = 0
    for f in files:
        txt = open(f, encoding="utf-8", errors="replace").read()
        for m in re.finditer(r'banner_key="([^"]+)"', txt):
            total += 1
            p = m.group(1).split(".")
            if len(p) < 20:
                continue                       # 纯色旗（10 段）= 未配家纹，合法
            try:
                iid = int(p[10])
            except ValueError:
                errs.append("%s：图标段不是数字 → %s" % (os.path.basename(f), m.group(1)[:60]))
                continue
            if iid not in icon_ids:
                dangling.add(iid)
                continue
            key = (p[0], tuple(p[3:10]), tuple(p[13:20]))
            geos.setdefault(key, []).append(os.path.basename(f))
    for iid in sorted(dangling):
        errs.append("banner_key 引用了未定义的图标 id %d（引擎查不到 → 静默不画）" % iid)
    if len(geos) > 1:
        detail = "；".join("背景%s/%s 图标%s（%d 处，如 %s）"
                          % (k[0], ",".join(k[1]), ",".join(k[2]), len(v), v[0])
                          for k, v in list(geos.items())[:3])
        errs.append("家纹旗出现 %d 套不同几何（应为 1 套）→ %s" % (len(geos), detail))
    else:
        for k in geos:
            if k[0] != CANON_BG_MESH or k[1] != CANON_BG_GEOM or k[2] != CANON_ICON_GEOM:
                warns.append("几何不是标准模板：背景 %s/%s 图标 %s" % (k[0], ",".join(k[1]), ",".join(k[2])))
    return errs, warns, total, sum(len(v) for v in geos.values())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=DEFAULT_MODULE)
    args = ap.parse_args()
    module = args.module

    icons_xml = os.path.join(module, "ModuleData", "banner_icons.xml")
    if not os.path.isfile(icons_xml):
        print("[skip] 本包没有 banner_icons.xml（未启用家纹）")
        print("\n结果：绿")
        return 0

    errs = []
    errs += ["（结构）" + e for e in check_structure(icons_xml)]

    e2, _w, icon_ids, mats = check_icons(icons_xml)
    errs += ["（段位）" + e for e in e2]
    errs += ["（材质）" + e for e in check_materials(mats, module)]

    warns = []
    e4, w4, total_keys, mon_keys = check_geometry(module, icon_ids)
    errs += ["（几何）" + e for e in e4]
    warns += w4

    print("banner_icons.xml：%d 个图标（id %d..%d）· %d 种材质"
          % (len(icon_ids), min(icon_ids), max(icon_ids), len(mats)))
    print("banner_key 共 %d 条，其中带家纹 %d 条" % (total_keys, mon_keys))
    for w in warns:
        print("[warn] " + w)
    if errs:
        print()
        for e in errs:
            print("[X] " + e)
        print("\n结果：红（%d 项）" % len(errs))
        return 1
    print("\n结果：绿")
    return 0


if __name__ == "__main__":
    sys.exit(main())

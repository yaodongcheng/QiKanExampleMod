#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Taikou 家纹图标注册表生成器（banner_icons.xml）
========================================================================
解决什么问题：让 Taikou 的城池在地图上挂出**日本家纹**，而不是原版欧洲图标。

链路（反编译实证，2026-09-16）：
  城池名牌 `SettlementBannerWidget` ← `Settlement.Banner`
    ← `OwnerClan.Banner`（若该家族是王国统治家族则取 `Kingdom.Banner`）
    ← `spclans.xml` / `spkingdoms.xml` 的 `banner_key` 字符串
    ← 图标 id → `BannerManager.AllIcons`（**所有模块 banner_icons.xml 合并**）
    ← `material_name` + `texture_index` → `Material.GetFromResource` → 运行时拼方块贴图

本脚本产出（**生成物，禁止手改** —— 铁律 22）：
  `Modules/Taikou/ModuleData/banner_icons.xml`  ← 20 张图集 × 16 格 = 320 个图标，
  id 从 840 起（段位理由见 `Scripts/taikou_mon_atlas.py` 模块 docstring）。

⚠️ 还要配合两件事，缺一不可：
  1. SubModule.xml 必须注册 `<XmlName id="BannerIcons" path="banner_icons"/>`
     —— 引擎只加载注册过的段（织丰就是漏了这步，304 个家纹从未生效）。
  2. 资产包 `AssetPackages/taikou_banners.tpac` 必须在位（20 个 `taikou_cl_mon_*` 材质）。

Usage:
  python Scripts/gen_taikou_banner_icons.py            # 重跑产出
  python Scripts/gen_taikou_banner_icons.py --check    # 只校验是否已最新（不一致 exit 1）
"""
import argparse
import io
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, str(Path(__file__).resolve().parent))
import taikou_mon_atlas as MON  # noqa: E402

MODULE = Path(r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
# BannerIcons.xsd 只在 1.5.x 客户端自带；1.2.12 的 XmlSchemas/ 里没有这份（实测 2026-09-16），
# 所以两台都找一遍——1.2.12 上做不了 XSD 校验（但它也不校验，见 verify_engine_shape 注释）。
XSD_CANDIDATES = [
    MODULE.parent.parent / "XmlSchemas" / "BannerIcons.xsd",
    Path(r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\XmlSchemas\BannerIcons.xsd"),
]
NATIVE_ICONS = MODULE.parent / "Native" / "ModuleData" / "banner_icons.xml"
SHOKUHO_ICONS = MODULE.parent / "Shokuho" / "ModuleData" / "banner_icons.xml"
PACK = MODULE / "AssetPackages" / "taikou_banners.tpac"
OUT = MODULE / "ModuleData" / "banner_icons.xml"

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"
	type="string">
	<BannerIconData>
		<!-- 🔴 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_banner_icons.py 从
		     Scripts/taikou_mon_atlas.py 的图集表生成。改内容 = 改图集表再重跑。
		     家纹图集资产 = AssetPackages/taikou_banners.tpac（taikou_cl_mon_*，20 张 4x4）。
		     图标 id 从 {base} 起（原版占 1..{native_max}，织丰声明 536..839）。

		     ⚠️ 雷 50（2026-09-16 实机踩中）：本注释**必须**留在这里，不许上移到
		     XML 声明与 <base> 之间，也不许移到 <base> 里 <BannerIconData> 之前——
		     BannerManager.LoadFromXml 硬取 doc.ChildNodes[1] 要它是 <base>、
		     再取 <base>.ChildNodes[0] 要它是 <BannerIconData>，中间夹注释 = 直接
		     throw TWXmlLoadException「Incorrect XML document format」→ 模块加载期崩。
		     放在 <BannerIconData> 内部则安全：那层循环只认 BannerIconGroup/BannerColors。 -->
		<BannerIconGroup
			id="{gid}"
			name="{{={key}}}{fallback}"
			is_pattern="false">
"""

FOOTER = """		</BannerIconGroup>
	</BannerIconData>
</base>
"""


def build():
    parts = [HEADER.format(base=MON.ICON_ID_BASE, native_max=535, gid=MON.GROUP_ID,
                           key=MON.GROUP_NAME_KEY, fallback=MON.GROUP_NAME_FALLBACK)]
    for iid, mat, cell in MON.all_icons():
        parts.append('\t\t\t<Icon\n\t\t\t\tid="%d"\n\t\t\t\tmaterial_name="%s"\n'
                     '\t\t\t\ttexture_index="%d" />\n' % (iid, mat, cell))
    parts.append(FOOTER)
    return "".join(parts)


def preflight():
    """返回 (硬错误列表, 警告列表)。"""
    errors, warns = [], []
    if not PACK.is_file():
        errors.append("资产包缺失: %s（先跑 tpaccli assetclone 建包）" % PACK)
    else:
        blob = PACK.read_bytes()
        for atlas in MON.ATLASES:
            name = MON.material_name(atlas).encode("ascii")
            if blob.count(name) == 0:
                errors.append("包里找不到材质 %s" % MON.material_name(atlas))
    # 段位体检：别的模块有没有占掉我们要用的 id（只警告，不改）
    for label, path in (("原版 Native", NATIVE_ICONS), ("织丰 Shokuho", SHOKUHO_ICONS)):
        if not path.is_file():
            continue
        txt = io.open(path, encoding="utf-8", errors="replace").read()
        ids = [int(x) for x in re.findall(r'<Icon\s+id="(\d+)"', txt)]
        if not ids:
            ids = [int(x) for x in re.findall(r'id="(\d+)"[\s\S]{0,80}?material_name=', txt)]
        if ids and max(ids) >= MON.ICON_ID_BASE:
            warns.append("%s 的图标 id 最大 %d，已侵入 Taikou 段位(>=%d)——需重划段位"
                         % (label, max(ids), MON.ICON_ID_BASE))
    return errors, warns


def verify_engine_shape(text):
    """按 1.2.12 `BannerManager.LoadFromXml` 的两条硬检查验产物结构。

    引擎读法（`LoadXmlFile` = 裸 LoadXml，PreserveWhitespace=false、**保留注释**）：
        doc.ChildNodes[1].Name         必须是 "base"            → 否则 TWXmlLoadException
        base.ChildNodes[0].Name        必须是 "BannerIconData"  → 否则同上
    ⇒ XML 声明与 `<base>` 之间、`<base>` 与 `<BannerIconData>` 之间**都不能夹注释**。
    返回 (ok, 说明)。"""
    from xml.dom import minidom
    try:
        d = minidom.parseString(text)
    except Exception as ex:
        return False, "XML 解析失败: %s" % ex

    def kids(n):
        return [c for c in n.childNodes
                if not (c.nodeType == c.TEXT_NODE and not c.data.strip())]

    # ⚠️ 索引换算：minidom **不把 XML 声明当子节点**，而 .NET 的 ChildNodes[0] 就是声明。
    #    所以 .NET 的 ChildNodes[1] == minidom 的 childNodes[0]。
    #    （坑：直接用 d.documentElement 会跳掉注释，这个闸就形同虚设——实测踩过。）
    top = kids(d)
    if not top or top[0].nodeName != "base":
        return False, "XML 声明与根元素之间夹了 %s（.NET ChildNodes[1] 必须是 base）" % (
            top[0].nodeName if top else "(空文档)")
    first = kids(top[0])
    if not first or first[0].nodeName != "BannerIconData":
        return False, "<base> 的第一个子节点是 %s，必须是 BannerIconData" % (
            first[0].nodeName if first else "(空)")
    grp = [c for c in kids(first[0]) if c.nodeName == "BannerIconGroup"]
    if len(grp) != 1:
        return False, "BannerIconData 下 BannerIconGroup 数 = %d（应为 1）" % len(grp)
    n_icon = len(d.getElementsByTagName("Icon"))
    if n_icon != len(MON.ATLASES) * MON.CELLS_PER_ATLAS:
        return False, "图标数 = %d（应为 %d）" % (n_icon, len(MON.ATLASES) * MON.CELLS_PER_ATLAS)

    # 权威检查：对游戏自带的 XmlSchemas/BannerIcons.xsd 校验。
    #   · 1.5.x 走合并路径 + skipValidation:false → 不合 XSD 会直接抛（1.2.12 不校验，故只在 1.5.x 爆）
    #   · 该 XSD 有 Icon/@id 唯一约束（织丰那份就是死在 817 重复上）
    # 找不到 XSD 或没有 lxml 都降级为提示，不阻断（结构检查已过）。
    xsd = next((p for p in XSD_CANDIDATES if p.is_file()), None)
    if xsd is None:
        print("[warn] 两台客户端都没找到 XmlSchemas/BannerIcons.xsd，跳过 XSD 校验")
    else:
        try:
            from lxml import etree
            schema = etree.XMLSchema(etree.parse(str(xsd)))
            if not schema.validate(etree.fromstring(text.encode("utf-8"))):
                return False, "XSD 校验不过(%s): %s" % (
                    xsd.parent.parent.name,
                    schema.error_log[0].message if len(schema.error_log) else "?")
        except ImportError:
            print("[warn] 无 lxml，跳过 BannerIcons.xsd 校验（结构检查已过）")
        except Exception as ex:
            print("[warn] XSD 校验异常，已跳过: %s" % ex)

    return True, "结构 OK：group id=%s，%d 个图标，首个 id=%s" % (
        grp[0].getAttribute("id"), n_icon, d.getElementsByTagName("Icon")[0].getAttribute("id"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只校验磁盘产物是否最新")
    args = ap.parse_args()

    errors, warns = preflight()
    for w in warns:
        print("[warn] " + w)
    for e in errors:
        print("[ERROR] " + e)
    if errors:
        return 2

    want = build()
    ok, why = verify_engine_shape(want)
    if not ok:
        print("[FATAL] 产物结构过不了引擎的 LoadFromXml 检查：%s" % why)
        print("        多半是注释位置——见本脚本 HEADER 里的雷 50 说明。")
        return 2

    cur = io.open(OUT, encoding="utf-8").read() if OUT.is_file() else None

    if args.check:
        if cur != want:
            print("[FAIL] %s 与生成器不一致（跑一次生成器重出）" % OUT)
            return 1
        print("[OK] %s 已是最新（%s）" % (OUT.name, why))
        return 0

    if cur == want:
        print("[skip] 已是最新，未改动（%s）" % why)
        return 0
    OUT.write_text(want, encoding="utf-8", newline="\n")
    print("[write] %s —— %s" % (OUT, why))
    return 0


if __name__ == "__main__":
    sys.exit(main())

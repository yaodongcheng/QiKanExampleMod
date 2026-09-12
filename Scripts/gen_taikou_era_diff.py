#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""🔴 **已退役（2026-09-12）**——三件套 `spclans_1582`/`spkingdoms_1582`/`taikou_heroes_1582`
已由 `Scripts/gen_taikou_era_world.py`（六代全量生成）接管，本脚本不再进一键体检。
保留在磁盘仅供回看差异 spike 的做法；**不要重跑**（重跑会把真实 1582 数据打回 spike 占位）。

Taikou 时代差异段生成器（时代剧本切换 spike，历史）
========================================================================
时代切换机制：**一模块多 GameType + 每时代一套数据段**——
引擎按 SubModule.xml 的 `<GameType value="…">` 过滤 XML 段，过滤键 = 战役类名
（`GameType.GameTypeStringId => GetType().Name`）。所以「同一个世界、不同时代」= 注册两套段。

本脚本产出**时代差异段**（只有 3 个段有时代差异；其余段跨时代共用，在 SubModule.xml 里
追加新 GameType 即可，不复制文件）：

    ModuleData/spclans_1582.xml        ← 家族（差异：织田家 → G 家）
    ModuleData/spkingdoms_1582.xml     ← 王国（差异：织田家 → G 家）
    ModuleData/taikou_heroes_1582.xml  ← 英雄（差异：织田 2 领主 → lord_g）

🔴 **据点不归本脚本**（2026-09-11 修冲突）：`settlements.xml` / `settlements_*.xml` 的**唯一产出方**
   是 `gen_taikou_settlements_xml.py`（从 Settlements.csv 生成，含各剧本名字差异与各剧本临时主人）。
   本脚本原先也写 `settlements_1582.xml`，两个生成器抢同一个文件 → `run_all_checks.py` 的两条
   一致性检查互相打架（跑完一个必让另一个报过期）。据点的时代差异由据点生成器按
   `TEMP_OWNER_BY_ERA` 处理，本脚本不再涉及。

🔴 生成物·禁止手改（铁律 22）——改差异请改下方 ERA_DIFF / 基线段，再重跑本脚本。

🔴 两个硬纪律：
  ① **据点 id 跨时代不变**（铁律 20：引用一律 StringId）——只换 owner，id/坐标/组件全同
     → 距离缓存（按据点 id 存据点对）天然可共用。
  ② **差异段的 GameType 必须互斥**：同名 XmlName 注册多段时引擎**合并加载**（官方 SandBox
     就注册了 7 份 NPCCharacters）→ 若两套段同时命中同一 GameType，同名 Faction/Hero 会重复定义。
     现状：基线段 = TaikouCampaign / TaikouCampaign1560，本脚本产出 = TaikouCampaign1582。

Usage:
  python Scripts/gen_taikou_era_diff.py            # 重跑产出
  python Scripts/gen_taikou_era_diff.py --check    # 只校验磁盘产物是否最新（不一致 exit 1）
  python Scripts/gen_taikou_era_diff.py --module PATH   # 换内容包（新世界复用）
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg


def registry_mb2_path():
    """读注册表 MB2_PATH（User 级优先，再 Machine）——铁律 19。"""
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                val, _ = winreg.QueryValueEx(k, "MB2_PATH")
                if val:
                    return val
        except OSError:
            continue
    return None


def _default_module():
    mb2 = registry_mb2_path()
    return Path(mb2) / "Modules" / "Taikou" if mb2 else Path("Taikou")


MODULE = _default_module()
DATA = MODULE / "ModuleData"

# ── 差异表：1582 时代相对基线的增量（spike 最小对比：只换归属，不换据点结构）──────────
ERA = "1582"

# 时代 A（基线）里有、时代 B 没有的家族 / 王国 / 英雄
DROP_CLANS = ["clan_oda", "clan_hattori_1", "clan_yagyuu_1", "clan_kuki_1", "clan_ruzon_1"]
DROP_KINGDOMS = ["kingdom_oda"]
DROP_HEROES = ["lord_tk5_195", "lord_tk5_379", "lord_tk5_517", "lord_tk5_587",
               "lord_tk5_740", "lord_tk5_279", "lord_tk5_549"]

# 时代 B 新增的家族（新增 id → 各字段；banner/颜色沿用基线织田款式，spike 不考究美术）
NEW_CLANS = [{
    "id": "clan_g",
    "owner": "Hero.lord_g",
    "super_faction": "Kingdom.kingdom_g",
    "name": "Clan G",
    "text": "The house of Clan G (spike placeholder).",
}]
NEW_KINGDOMS = [{
    "id": "kingdom_g",
    "owner": "Hero.lord_g",
    "name": "Kingdom G",
    "text": "The realm of Kingdom G (spike placeholder).",
}]
NEW_HEROES = [{
    "id": "lord_g",
    "faction": "Faction.clan_g",
    "text": "Lord G (spike placeholder)",
}]

# 颜色/旗标（沿用基线织田款式；spike 占位）
BANNER_KEY_CLAN = "11.80.84.864.864.763.762.1.0.0.671.18.103.483.483.764.729.0.0.0"
BANNER_KEY_PLAYER = "11.154.116.1536.1536.768.768.1.0.0.609.15.155.483.483.773.729.0.0.0"
COLORS = dict(label_color="FF5573BE", color="FF5573BE", color2="FFDE9953",
              alternative_color="FFCBC25D", alternative_color2="FF5D6347")

HEADER = ('<?xml version="1.0" encoding="utf-8"?>\n'
          '<!-- 🔴 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_era_diff.py 从 {base} 派生。\n'
          '     若需修改：改生成器的 ERA_DIFF / 基线段后重跑，不要直接编辑本文件。\n'
          '     ⚠️ 本段（{family}）属「时代差异段」：GameType 互斥注册——\n'
          '        {base} → TaikouCampaign / TaikouCampaign1560\n'
          '        {self} → TaikouCampaign{era}\n'
          '     两者**不得同时命中同一 GameType**（同名 XmlName 多段 = 引擎合并加载 → 同名对象重复定义）。\n'
          '     🔴 据点 id 跨时代不变（铁律 20）——只换归属，id/坐标/组件全同 → 距离缓存可共用。 -->\n')


def esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def attr_str(attrs):
    """(name, value) 列表 → `a="1" b="2"`（保持传入顺序）。"""
    return " ".join(f'{k}="{esc(v)}"' for k, v in attrs)


def build_clans(data):
    root = ET.parse(str(data / "spclans.xml")).getroot()
    out = []
    for c in root.findall("Faction"):
        if c.get("id") in DROP_CLANS:
            continue
        out.append(f'\t<Faction {attr_str(list(c.attrib.items()))}>')
        inf = c.find("Influence")
        if inf is not None:
            out.append("\t\t<Influence>")
            for b in inf:
                out.append(f'\t\t\t<{b.tag} {attr_str(list(b.attrib.items()))}/>')
            out.append("\t\t</Influence>")
        out.append("\t</Faction>")
    for n in NEW_CLANS:
        attrs = [("id", n["id"]), ("is_noble", "true"), ("owner", n["owner"]),
                 ("banner_key", BANNER_KEY_CLAN), ("is_minor_faction", "false"),
                 ("super_faction", n["super_faction"])] + list(COLORS.items()) + [
                 ("culture", "Culture.ikoku"), ("settlement_banner_mesh", "encounter_flag_a"),
                 ("name", f'{{=TAIKOU_{n["id"]}}}{n["name"]}'),
                 ("short_name", f'{{=TAIKOU_{n["id"]}_short}}{n["name"]}'),
                 ("title", f'{{=TAIKOU_{n["id"]}_short}}{n["name"]}'),
                 ("text", f'{{=TAIKOU_{n["id"]}_text}}{n["text"]}'), ("tier", "3")]
        out.append(f'\t<Faction {attr_str(attrs)}>')
        out.append("\t\t<Influence>")
        out.append('\t\t\t<base_influence value="60.0"/>')
        out.append("\t\t</Influence>")
        out.append("\t</Faction>")
    header = HEADER.format(base="spclans.xml", self=f"spclans_{ERA}.xml",
                           era=ERA, family="Factions/spclans*")
    return header + "<Factions>\n" + "\n".join(out) + "\n</Factions>\n"


def build_kingdoms(data):
    root = ET.parse(str(data / "spkingdoms.xml")).getroot()
    out = []
    tmpl = None
    for k in root.findall("Kingdom"):
        if k.get("id") in DROP_KINGDOMS:
            tmpl = dict(k.attrib)   # 留作新品模板（颜色/旗标款式沿用）
            continue
        out.append("\t<Kingdom")
        for key, val in k.attrib.items():
            out.append(f'\t\t{key}="{esc(val)}"')
        out.append("\t>")
        out.append("\t</Kingdom>")
    for n in NEW_KINGDOMS:
        base = dict(tmpl or {})
        attrs = [("id", n["id"]), ("owner", n["owner"]),
                 ("banner_key", base.get("banner_key", BANNER_KEY_CLAN)),
                 ("primary_banner_color", base.get("primary_banner_color", "0xff7a94d0")),
                 ("secondary_banner_color", base.get("secondary_banner_color", "0xff1d2c53"))]
        attrs += [(k, base.get(k, v)) for k, v in COLORS.items()]
        attrs += [("culture", "Culture.ikoku"),
                  ("settlement_banner_mesh", base.get("settlement_banner_mesh", "encounter_flag_a")),
                  ("flag_mesh", base.get("flag_mesh", "info_screen_flags_a")),
                  ("name", f'{{=TAIKOU_{n["id"]}}}{n["name"]}'),
                  ("short_name", f'{{=TAIKOU_{n["id"]}_short}}{n["name"]}'),
                  ("title", f'{{=TAIKOU_{n["id"]}_short}}{n["name"]}'),
                  ("ruler_title", "{=TAIKOU_daimyo}Daimyo"),
                  ("text", f'{{=TAIKOU_{n["id"]}_text}}{n["text"]}')]
        out.append("\t<Kingdom")
        for key, val in attrs:
            out.append(f'\t\t{key}="{esc(val)}"')
        out.append("\t>")
        out.append("\t</Kingdom>")
    header = HEADER.format(base="spkingdoms.xml", self=f"spkingdoms_{ERA}.xml",
                           era=ERA, family="Kingdoms/spkingdoms*")
    return header + "<Kingdoms>\n" + "\n".join(out) + "\n</Kingdoms>\n"


def build_heroes(data):
    root = ET.parse(str(data / "taikou_heroes.xml")).getroot()
    out = []
    for h in root.findall("Hero"):
        if h.get("id") in DROP_HEROES:
            continue
        out.append(f'\t<Hero {attr_str(list(h.attrib.items()))}/>')
    for n in NEW_HEROES:
        attrs = [("id", n["id"]), ("faction", n["faction"]),
                 ("text", f'{{=TAIKOU_hero_{n["id"].replace("lord_", "")}}}{n["text"]}')]
        out.append(f'\t<Hero {attr_str(attrs)}/>')
    header = HEADER.format(base="taikou_heroes.xml", self=f"taikou_heroes_{ERA}.xml",
                           era=ERA, family="Heroes/taikou_heroes*")
    return header + "<Heroes>\n" + "\n".join(out) + "\n</Heroes>\n"


TARGETS = [
    (f"spclans_{ERA}.xml", build_clans),
    (f"spkingdoms_{ERA}.xml", build_kingdoms),
    (f"taikou_heroes_{ERA}.xml", build_heroes),
]


def main():
    ap = argparse.ArgumentParser(description="Taikou era-diff data generator")
    ap.add_argument("--check", action="store_true", help="只校验产物是否最新")
    ap.add_argument("--module", default=None, help="内容包目录（缺省=注册表 MB2_PATH 下的 Taikou）")
    args = ap.parse_args()

    data = Path(args.module) / "ModuleData" if args.module else DATA

    if not data.is_dir():
        print(f"[FATAL] ModuleData not found: {data}")
        return 2

    stale = 0
    for fname, builder in TARGETS:
        path = data / fname
        try:
            text = builder(data)
        except Exception as e:
            print(f"[FATAL] {fname} 构建失败: {e}")
            return 2
        # 语法自检（生成即 parse，防产出非法 XML）
        try:
            ET.fromstring(text)
        except ET.ParseError as e:
            print(f"[FATAL] {fname} 产出非法 XML: {e}")
            return 2

        old = path.read_text(encoding="utf-8") if path.is_file() else None
        if args.check:
            if old == text:
                print(f"  ✅ {fname} 已最新")
            else:
                print(f"  ❌ {fname} 与生成器不一致（需重跑）")
                stale += 1
        else:
            path.write_text(text, encoding="utf-8", newline="\n")
            print(f"  {'写入' if old != text else '未变'} {fname}")

    if args.check and stale:
        print(f"\n结果：{stale} 份过期——重跑 `python Scripts/gen_taikou_era_diff.py`")
        return 1
    print("\n完成。" if not args.check else "\n结果：全部最新 ✓")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Taikou 选人目录生成器（**建世界之前**就要用的选人数据）
============================================================================
为什么要有这张表：选人界面原本读**活世界对象**（`Kingdom.All` / `Clan.Heroes`），
所以只能等世界建好（长 loading）之后才弹得出来。把选人提到 loading **之前**，
就必须给界面一份**静态的**「该时代有哪些可扮演的人、他们归谁、显示成什么」。

产出（内容包 ModuleData）
------------------------
  `ModuleData/AssetRegistry/HeroCatalog.xml`   ← 生成物·禁止手改

    <RealmType id="warrior" name="{=TAIKOU_forcetype_warrior}Warrior" order="1"/>   ← 左列筛档标签（全时代共用）
    <Era id="1560">
      <Realm id="kingdom_oda" name="{=TAIKOU_kingdom_oda}Oda Clan" type="warrior" order="1"/>
      <Realm id=""            name="{=TAIKOU_realm_masterless}Masterless" order="99"/>  ← 无所属档不带 type
      <House id="clan_oda" realm="kingdom_oda" name="{=TAIKOU_clan_oda}Oda Nobunaga" type="warrior" order="1"/>
      <Lord  id="lord_tk5_195" house="clan_oda" order="1"
             name="{=TAIKOU_hero_nobunaga}Oda Nobunaga"
             identity="{=TAIKOU_identity_daimyo}Daimyo"
             seat="{=TAIKOU_sett_town_tk080}Kiyosu Castle"
             bustup="lwnprof_bustup_195" mini="lwnprof_mini_195"/>
    </Era>

🔴 `type` = 势力类型（选人界面最左列的筛档）：王国取其势力的 `ForceType`；家族取其**当年**
   势力 id 的 `ForceType`（`Clan.csv` 的 `Kingdom_<年>`），立国家族跟王国同档；
    查不到 → `others`。取值见 `REALM_TYPES`。

🔴 **目录的 id 集合 = `taikou_heroes*.xml` 的 id 集合**（同源）——不是从 CSV 全量挑，
   所以「目录里有、世界里没有」这种漂移**不可能发生**。守卫见 Scripts/check_hero_profile_keys.py。

输入
----
  `ModuleData/taikou_heroes.xml` / `taikou_heroes_1582.xml`   ← 英雄集合 + 时代（**集合的唯一真源**）
  `ModuleData/spclans*.xml` + `spkingdoms*.xml`               ← 家族/王国归属与显示名
  `ModuleData/settlements*.xml`                               ← 据点显示名（按名反查，直接读引擎要读的那份）
  `csv/TaikouHero.csv`（只读镜像）                             ← 身份 / 据点名（按时代列）
  `csv/Settlements.csv`（只读镜像）                            ← 据点名 → id 反查

用法
----
  python Scripts/gen_taikou_hero_catalog.py            # 重跑产出
  python Scripts/gen_taikou_hero_catalog.py --check    # 只校验产物是否最新
"""
import argparse
import csv
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

OUT_NAME = "HeroCatalog.xml"
OUT_SUBDIR = "AssetRegistry"

# 时代 → 该时代的四个数据段（内容包自己的结构；LWN 侧不认识这些文件名——铁律 3）
ERAS = [
    {"id": "1560", "heroes": "taikou_heroes.xml", "clans": "spclans.xml",
     "kingdoms": "spkingdoms.xml", "settlements": "settlements.xml"},
    {"id": "1582", "heroes": "taikou_heroes_1582.xml", "clans": "spclans_1582.xml",
     "kingdoms": "spkingdoms_1582.xml", "settlements": "settlements_1582.xml"},
]

# 无王国那一档的显示名。
# 🔴 必须是**本内容包自己的键**（TAIKOU_*）——本表是内容包的数据文件，而语言检查器按模块归属：
#    内容包数据里出现 LWN_ 的键，检查器会在 Taikou 的语言文件里找不到它而永远报缺。
#    （跨模块引用文本键 = 坏味道，两个模块的键集应当各自闭合。）
NO_REALM_NAME = "{=TAIKOU_realm_masterless}Masterless"

# 建号占位主角：不是可选角色，与全工程的豁免口径一致（见 check_hero_templates.py）
EXCLUDE_HEROES = {"main_hero"}
# 玩家占位家族的主人：该家族无名字无内容，只在建号期存在 → 不进目录
PLAYER_PLACEHOLDER = "main_hero"

# 太阁5 身份枚举（CSV 的 Identity_<年> 列）→ (key 尾段, 英文)
# 🔴 全部 6 个年代并集共 29 个值（2026-09-11 从 CSV 实测枚举）；缺一个 = 该身份显示英文 key 名
IDENTITY_KEYS = {
    "大名": ("daimyo", "Daimyo"),
    "国主": ("kunishu", "Provincial Lord"),
    "城主": ("joushu", "Castle Lord"),
    "当家": ("touke", "Head of House"),
    "家老": ("karou", "Senior Retainer"),
    "部将": ("bushou", "Commander"),
    "侍大将": ("samurai_daishou", "Samurai Captain"),
    "足轻大将": ("ashigaru_daishou", "Ashigaru Captain"),
    "足轻组头": ("ashigaru_kumigashira", "Ashigaru Corporal"),
    "头领": ("touryou", "Chief"),
    "头目": ("toumoku", "Boss"),
    "师范": ("shihan", "Master"),
    "师范代": ("shihandai", "Deputy Master"),
    "上忍": ("jounin", "Jonin"),
    "中忍": ("chunin", "Chunin"),
    "下忍": ("genin", "Genin"),
    "浪人": ("rounin", "Ronin"),
    "见习": ("minarai", "Apprentice"),
    "伙计": ("kakei", "Shop Hand"),
    "掌柜": ("shoukai", "Shop Manager"),
    "僧侣": ("souryo", "Monk"),
    "医师": ("ishi", "Physician"),
    "茶人": ("chajin", "Tea Master"),
    "水夫": ("suifu", "Sailor"),
    "水夫头": ("suifutou", "Boatswain"),
    "船头": ("sendou", "Boat Master"),
    "船大将": ("funadaishou", "Fleet Captain"),
    "锻冶匠": ("kajishi", "Smith"),
}

# ─────────────────────── 势力类型（选人界面左列筛选）───────────────────────
# 选人界面最左列的筛选档：**全部（LWN 侧）** + 下面五档。档位 = 上游 `TaikouForce.csv`
# 的 `ForceType` 列（实测取值：Warrior / Trader / Ninja / Pirate / Neutral）+ 兜底档。
# 🔴 标签走**内容包自有键**（铁律 3：LWN 不认识「忍者/海贼」这类具体词汇）——
#    中文在 `Languages/CNs/std_Taikou_strings.xml` 里人工维护，英文 fallback 由
#    `gen_taikou_english_strings.py` 从本表自动抽取。
REALM_TYPES = [
    ("warrior", "Warrior", 1),      # 武将（大名家 / 国人 / 剑豪等武士势力）
    ("trader", "Trader", 2),        # 商人（商家——不立国，落在「无所属」档里）
    ("ninja", "Ninja", 3),          # 忍者（忍者众，各自立国）
    ("pirate", "Pirate", 4),        # 海贼（水军，各自立国）
    ("others", "Others", 5),        # 其他（浪人众等无势力归属的收容家族）
]
FORCE_TYPE_TO_KEY = {"Warrior": "warrior", "Trader": "trader",
                     "Ninja": "ninja", "Pirate": "pirate"}
REALM_TYPE_KEY = "TAIKOU_forcetype_%s"      # 标签键（中文在 CN 语言文件里人工维护）
DEFAULT_TYPE = "others"             # 查不到 / Neutral → 其他（不猜）


def force_type_table(csv_dir):
    """`TaikouForce.csv` → {势力 id: ForceType}。"""
    out = {}
    for r in read_csv(csv_dir / "TaikouForce.csv"):
        fid = (r.get("ID") or "").strip()
        if fid:
            out[fid] = (r.get("ForceType") or "").strip()
    return out


def clan_force_table(csv_dir, era_id):
    """`Clan.csv` → {家族 id: 该族**当年的势力 id**（`Kingdom_<年>`；'-' = 当年无势力）}。"""
    out = {}
    for r in read_csv(csv_dir / "Clan.csv"):
        cid = (r.get("ID") or "").strip()
        if cid:
            out[cid] = (r.get("Kingdom_" + era_id) or "").strip()
    return out


def type_key_of_force(fid, ftypes):
    """势力 id → 筛选档 key（查不到 = 'others'，不猜）。"""
    return FORCE_TYPE_TO_KEY.get(ftypes.get(fid, ""), DEFAULT_TYPE)


def force_id_of_realm(realm_id):
    """目录里的王国 id（`kingdom_oda`）→ 势力表里的势力 id（`oda`）。"""
    return realm_id[len("kingdom_"):] if realm_id.startswith("kingdom_") else realm_id


HEADER = ('<?xml version="1.0" encoding="utf-8"?>\n'
          '<!-- 🔴 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_hero_catalog.py 生成。\n'
          '     改内容 = 改生成器 / 上游 CSV / 数据段，然后重跑。\n'
          '     🔴 本表 id 集合 == taikou_heroes*.xml 的 id 集合（同源，不会漂移）。\n'
          '     建世界**之前**的选人界面读这张表（不依赖 Campaign.Current）。 -->\n')


def registry_mb2_path():
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


def esc(s):
    return (str(s).replace("&", "&amp;").replace("<", "&lt;")
            .replace(">", "&gt;").replace('"', "&quot;"))


def read_csv(path, head=2):
    """读 CSV → [dict]。`head`：2=双行表头取英文键（默认）/ 1=中文键 / 0=单行表头。"""
    from csv_dual import dict_rows
    return dict_rows(path, head=head)


def hero_rows(md, era_id):
    """该时代的英雄：id → {faction, name_raw}；集合真源 = taikou_heroes 段。"""
    fn = next(e["heroes"] for e in ERAS if e["id"] == era_id)
    root = ET.parse(str(md / fn)).getroot()
    out = {}
    for h in root.findall("Hero"):
        hid = h.get("id")
        if hid:
            out[hid] = {"faction": h.get("faction", ""), "name": h.get("text", "")}
    return out


def clan_table(md, era_id):
    """家族 id → {owner, super_faction, name, short_name}。"""
    fn = next(e["clans"] for e in ERAS if e["id"] == era_id)
    root = ET.parse(str(md / fn)).getroot()
    out = {}
    for c in root.findall("Faction"):
        out[c.get("id")] = {"owner": c.get("owner", ""),
                            "super_faction": c.get("super_faction", ""),
                            "name": c.get("name", ""),
                            "short_name": c.get("short_name", "")}
    return out


def kingdom_name_table(md, era_id):
    fn = next(e["kingdoms"] for e in ERAS if e["id"] == era_id)
    root = ET.parse(str(md / fn)).getroot()
    return {k.get("id"): k.get("name", "") for k in root.findall("Kingdom")}


def settlement_display(md, era_id):
    """据点显示名 → 该据点的 name 属性原样串（`{=KEY}fallback`）。
    直接读**引擎要读的那份** settlements XML——不猜命名约定。"""
    fn = next(e["settlements"] for e in ERAS if e["id"] == era_id)
    path = md / fn
    if not path.is_file():
        return {}
    txt = io.open(path, encoding="utf-8-sig").read()
    out = {}
    for m in re.finditer(r'<Settlement id="([^"]+)" name="([^"]+)"', txt):
        sid, raw = m.group(1), m.group(2)
        out[sid] = raw
        fb = raw.split("}", 1)[1] if "}" in raw else raw      # 剥掉 {=KEY} 取 fallback
        out.setdefault(fb, raw)
    return out


# 🔴 据点名一律**精确全名**（用户 2026-09-11 裁定：「城和町共同前缀，一定得写全」）
#    曾经这里有个「查不到就剥掉 之町/之砦/之里 再查」的兜底 —— 已拆除，别再写回来：
#    剥后缀 = 拿两个不同的地方当同一个（冈崎城 / 冈崎 町 同前缀）。
#    英雄表用的全名（京之町 / 鸟羽之砦 / 伊贺之里）由 `gen_settlements_csv.py` 的
#    `add_full_names` 在**构建期**写进 Settlements.csv 的 Name_All，本脚本按全名精确查。
def settlement_alias_index(csv_dir, seats):
    """Settlements.csv 的 Name_All 各段 → 该据点的显示串（取自引擎要读的那份 XML）

    `seats`（settlement_display）只有据点的一个规范名；英雄表还会用全名/改名/异写
    （京之町、上田城、堺之町…），这些都在 Name_All 里。按全名把它们指到同一个显示串。
    """
    path = csv_dir / "Settlements.csv"
    if not path.is_file():
        return {}
    out = {}
    from csv_dual import dict_rows
    for r in dict_rows(path):                    # Settlements.csv：双行表头取英文键
        sid = (r.get("id") or "").strip()
        if sid not in seats:
            continue
        for n in (r.get("Name_All") or "").split("|"):
            n = n.strip()
            if n and n not in out:
                out[n] = seats[sid]
    return out


def lookup_seat(seats, city):
    """点名人 → 据点显示串。**只精确匹配**（见上）。查不到留空 = 数据缺口，不是生成器 bug。"""
    return seats.get(city, "") if city else ""


def portrait_cards(md):
    """英雄 id → [(stage, bustupSprite, miniheadSprite), …]（读立绘表；缺文件 = 空表）。"""
    path = md / "AssetRegistry" / "ProfileStages.csv"
    if not path.is_file():
        return {}
    out = {}
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f):
            sid = (r.get("StringId") or "").strip()
            if sid:
                out.setdefault(sid, []).append(
                    ((r.get("stage") or "").strip(),
                     (r.get("bustupSprite") or "").strip(),
                     (r.get("miniheadSprite") or "").strip()))
    return out


def pick_card(cards, era_name):
    """挑该时代该用哪张立绘卡。
    🔴 规则 = **后缀匹配**：该时代的人名以某张卡的 stage 标签**结尾** → 用那张。
       依据：太阁5 的改名阶段改的是**名**（藤吉郎→秀吉），姓先改（木下→羽柴），
       所以「木下藤吉郎」后缀命中「藤吉郎」、「羽柴秀吉」后缀命中「秀吉」，且各自唯一。
       命中不了（多数角色：stage 是「上洛/鬼/海」这类主题词，与人名无关）→ 取第 0 张（默认卡）。
       ⚠️ 数据里**没有**「哪张卡对应哪一年」的字段，故不做任何猜测——宁可留默认卡，
       也不要编出一套没有依据的年代映射。"""
    if not cards:
        return None
    nm = (era_name or "").strip()
    if nm:
        for stage, bustup, mini in cards:
            if stage and nm.endswith(stage):
                return (bustup, mini)
    bustup, mini = cards[0][1], cards[0][2]
    return (bustup, mini) if bustup else None


def build(md, csv_dir):
    """产出 XML 文本 + (硬错误, 警告)。硬错误 = 生成器 bug；警告 = 数据缺口（可留空）。"""
    problems, warns = [], []
    taikou = {r["ID"]: r for r in read_csv(csv_dir / "TaikouHero.csv")}
    cards = portrait_cards(md)
    ftypes = force_type_table(csv_dir)          # 势力 id → ForceType（左列筛选用）
    _warned_forces = set()                      # 缺类型的势力只报一次（英雄循环里会重复命中）

    eras = []
    for e in ERAS:
        era = e["id"]
        heroes = hero_rows(md, era)
        clans = clan_table(md, era)
        kingdoms = kingdom_name_table(md, era)
        seats = settlement_display(md, era)
        seats.update(settlement_alias_index(csv_dir, seats))
        cforce = clan_force_table(csv_dir, era)      # 家族 id → 当年的势力 id

        realms, houses, lords = {}, {}, []
        for hid, h in heroes.items():
            if hid in EXCLUDE_HEROES:
                continue                      # 建号占位主角：不是可选角色
            fac = h["faction"].split(".", 1)[-1] if "." in h["faction"] else h["faction"]
            if fac not in clans:
                continue
            cl = clans[fac]
            if cl["owner"] == "Hero." + PLAYER_PLACEHOLDER:
                continue                      # 玩家占位家族（无名字、无内容）不进目录
            kd = cl["super_faction"].split(".", 1)[-1] if "." in cl["super_faction"] else ""
            realm_id = kd if kd in kingdoms else ""
            # 势力类型（左列筛选）：立国的按王国自己的势力类型（王国 id 去掉 `kingdom_`
            # 前缀才是势力表里的 id）；不立国的（商家/浪人众）按该族**当年**的势力 id 查
            # （如 clan_chaya_1 的 org_merchant_chaya = Trader）
            realm_type = type_key_of_force(force_id_of_realm(realm_id), ftypes) if realm_id else ""
            if realm_id and force_id_of_realm(realm_id) not in ftypes:
                key = (era, realm_id)
                if key not in _warned_forces:
                    _warned_forces.add(key)
                    warns.append(f"[{era}] 王国 {realm_id} 的势力 {force_id_of_realm(realm_id)!r} "
                                 f"不在 TaikouForce.csv 里 → 类型记 {DEFAULT_TYPE}")
            cid_force = cforce.get(fac, "")
            house_type = type_key_of_force(cid_force, ftypes) if cid_force else DEFAULT_TYPE
            if realm_id:
                house_type = realm_type            # 立国家族跟王国同档（免得两处口径打架）
            realms.setdefault(realm_id, {"name": kingdoms.get(kd, NO_REALM_NAME) if realm_id else NO_REALM_NAME,
                                         "type": realm_type})
            # 家族显示名优先用 short_name（列表里「Oda」比「Oda Nobunaga」合适）
            houses.setdefault(fac, {"realm": realm_id, "name": cl["short_name"] or cl["name"],
                                    "type": house_type})

            row = taikou.get(hid, {})
            identity_raw = (row.get("Identity_" + era) or "").strip()
            ident_out = ""
            if identity_raw and identity_raw != "无效":
                if identity_raw not in IDENTITY_KEYS:
                    problems.append(f"[{era}] {hid} 的身份 {identity_raw!r} 不在 IDENTITY_KEYS 表里")
                else:
                    k, en = IDENTITY_KEYS[identity_raw]
                    ident_out = "{=TAIKOU_identity_%s}%s" % (k, en)

            city = (row.get("City_" + era) or "").strip()
            seat_out = lookup_seat(seats, city)
            if city and not seat_out:
                # 查不到 = 该据点还没进世界（T4 数据缺口），**不是生成器 bug** → 警告 + 留空
                warns.append(f"[{era}] {hid} 的据点 {city!r} 在 settlements 里查不到（留空）")

            card = pick_card(cards.get(hid), row.get("Name_" + era))
            lords.append({"id": hid, "house": fac, "name": h["name"],
                          "identity": ident_out, "seat": seat_out,
                          "bustup": card[0] if card else "",
                          "mini": card[1] if card else "",
                          "leader": cl["owner"] == "Hero." + hid,
                          "birth": int(row.get("BirthYear") or 0)})

        # 排序：王国按出现序（无所属最后）；家族按出现序；英雄 = 族长优先，其余年长者在前
        realm_order = [r for r in realms if r] + ([""] if "" in realms else [])
        out_realms = []
        for i, r in enumerate(realm_order, start=1):
            info = realms[r]
            out_realms.append((r, info["name"], info["type"], i if r else 99))
        house_order = sorted(houses.items(), key=lambda kv: (kv[1]["realm"] == "", kv[0]))
        out_houses = [(hid_, h["realm"], h["name"], h["type"], i)
                      for i, (hid_, h) in enumerate(house_order, start=1)]
        lords.sort(key=lambda l: (l["house"], not l["leader"], l["birth"]))
        out_lords = []
        counter = {}
        for l in lords:
            counter[l["house"]] = counter.get(l["house"], 0) + 1
            out_lords.append((l, counter[l["house"]]))

        eras.append((era, out_realms, out_houses, out_lords))

    text = [HEADER, "<HeroCatalog>\n"]
    # 筛档标签（全时代共用一份；选人界面最左列按 order 排。界面自己再加「全部」档）
    for key, en, order in REALM_TYPES:
        text.append('  <RealmType id="%s" name="{=%s}%s" order="%d" />\n'
                    % (key, REALM_TYPE_KEY % key, en, order))
    for era, out_realms, out_houses, out_lords in eras:
        text.append('  <Era id="%s">\n' % era)
        for rid, name, rtype, order in out_realms:
            attrs = 'id="%s" name="%s"' % (esc(rid), esc(name))
            if rtype:
                attrs += ' type="%s"' % esc(rtype)         # 无所属档不带类型（由家族决定）
            text.append('    <Realm %s order="%d" />\n' % (attrs, order))
        for hid_, realm, name, htype, order in out_houses:
            attrs = 'id="%s" realm="%s" name="%s"' % (esc(hid_), esc(realm), esc(name))
            if htype:
                attrs += ' type="%s"' % esc(htype)
            text.append('    <House %s order="%d" />\n' % (attrs, order))
        for l, order in out_lords:
            attrs = ['id="%s"' % esc(l["id"]), 'house="%s"' % esc(l["house"]),
                     'order="%d"' % order, 'name="%s"' % esc(l["name"])]
            if l["identity"]:
                attrs.append('identity="%s"' % esc(l["identity"]))
            if l["seat"]:
                attrs.append('seat="%s"' % esc(l["seat"]))
            # 立绘卡（按时代后缀匹配挑出来的；挑不到 = 不写，界面回落到立绘表首张）
            if l["bustup"]:
                attrs.append('bustup="%s"' % esc(l["bustup"]))
            if l["mini"]:
                attrs.append('mini="%s"' % esc(l["mini"]))
            text.append("    <Lord %s />\n" % " ".join(attrs))
        text.append("  </Era>\n")
    text.append("</HeroCatalog>\n")
    return "".join(text), problems, warns


def main():
    ap = argparse.ArgumentParser(description="Taikou hero catalog generator")
    ap.add_argument("--check", action="store_true", help="只校验产物是否最新")
    ap.add_argument("--module", default=None, help="内容包目录（缺省 = 注册表 MB2_PATH 下的 Taikou）")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    proj = os.path.dirname(here)
    csv_dir = os.path.join(proj, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
    mb2 = registry_mb2_path()
    module = args.module or (os.path.join(mb2, "Modules", "Taikou") if mb2 else None)
    if not module or not os.path.isdir(os.path.join(module, "ModuleData")):
        print(f"[FATAL] 找不到内容包 ModuleData：{module}", file=sys.stderr)
        return 2
    md = __import__("pathlib").Path(module) / "ModuleData"

    text, problems, warns = build(md, __import__("pathlib").Path(csv_dir))
    try:
        ET.fromstring(text)
    except ET.ParseError as e:
        print(f"[FATAL] 产出非法 XML：{e}", file=sys.stderr)
        return 2
    for w in warns:
        print(f"  [WARN] {w}")
    if problems:
        print(f"[FATAL] 数据自检未通过（{len(problems)} 条）：")
        for p in problems[:20]:
            print("  - " + p)
        return 1

    path = md / OUT_SUBDIR / OUT_NAME
    old = io.open(path, encoding="utf-8").read() if path.is_file() else None
    n_lord = text.count("<Lord ")
    if args.check:
        if old == text:
            print(f"OK：{OUT_NAME} 已最新（条目 {n_lord}）")
            return 0
        print(f"产物与生成器不一致（需重跑）：{OUT_NAME}")
        return 1
    path.parent.mkdir(parents=True, exist_ok=True)
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    print(f"{'写入' if old != text else '未变'} {OUT_NAME}（条目 {n_lord}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

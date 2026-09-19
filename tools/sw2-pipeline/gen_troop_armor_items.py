# -*- coding: utf-8 -*-
"""gen_troop_armor_items.py —— 14 件「兵种甲」+ 10 件「兵种兜」的物品定义 + 中文名。

为什么单开一个生成器、单开一个 XML 文件
----------------------------------------
`gen_armor_items.prune_kind()` 会把**所有不在 28 武将名单里**的 `taikou_*_do_a` 条目删掉
（正则 `taikou_[a-z_]+_do_a` —— `taikou_troop_yari_ashigaru_do_a` 一样命中）。
兵种甲若写进同一个文件，下次跑武将甲生成器就被清掉。
所以走**独立文件 + 独立哨兵**，两边互不干扰（与 `gen_troop_weapon_items.py` 同一个理由）。

✅ **不需要改 SubModule** —— 引擎按**目录**加载物品表
   （`SubModule.xml` 里只有 `<XmlName id="Items" path="taikou_items"/>`），新文件放进该目录自动生效。

产出三处（都幂等：先删同 id 的旧块再写，重复跑 = 0 改动）
  ① `Taikou\\ModuleData\\taikou_items\\troop_armors.xml` —— 14 甲 + 10 兜
  ② `Taikou\\ModuleData\\Languages\\CNs\\std_Taikou_strings.xml` —— 24 条中文名（自己的哨兵）
  ③ `item.csv` 登记 24 行

谁上榜：`troop_parts_table.TROOP_TABLE` 里**写了 `troop=` 的**那些（= 游戏里真有这个兵种）。
没写 `troop=` 的（农民/九州兵×2/护卫×6）网格虽然做好了，但游戏里没人穿 → 不生成物品，
免得成为「没人引用的孤儿物品」（`prune_taikou_items.py` 会剪掉）。

命名口径
--------
**通用装备（无归属者）→ 直接给装备名，不加 `[xx之铠]` 归属结构**，每个兵种单独命名。
名字**不写死在本文件**：从 `Sw2OfficialNames.csv` 取（那张表由 `extract_official_names.py`
从上游 `web/armor.html` 提取 —— 铁律 28：源 → 生成器 → CSV）。改名字 = 改上游/改提取脚本，别改这里。

数值口径（🔴 2026-09-16 用户口径：**本轮只换外观，不重新平衡**）
--------------------------------------------------------------
按**兵种等级**给一套固定数值，取的就是本包现有兵种装备的实测值：
  Lv6  ← `short_padded_robe`(14/6/2)   · Lv11 ← `padded_leather_shirt`(16/8/4)
  Lv16 ← `leather_lamellar_armor`(24/12/6) · Lv21 ← `eastern_lamellar_armor`(39/10/7)
兜另给一档（笠/覆面轻、兜重），数值按等级单调递增。等级**从 `TROOPS` 读**（单一来源），
两边对不上会当场报错。

🔴 **4 个兵种没有兜**（弓足轻/下忍/中忍/飞忍）：战无2 原模型里它们的兜与**身体网格同件**，
`troop_parts_table` 的 `pending_helmet` 记着这件事，暂时切不出来。这 4 个**不给 Head 物品**
（视觉上头饰本来就在身体网格里，挂原版欧洲帽反而出戏），代价是**头防归零**。
将来补独立兜件 = 走「兜并入甲」路线，见 `troop_parts_table.py` 的说明。

用法：
    python tools/sw2-pipeline/gen_troop_armor_items.py            # 写盘
    python tools/sw2-pipeline/gen_troop_armor_items.py --check    # 只校验（exit 1 = 过期）
"""
import argparse
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))

from csv_dual import read_table, write_table          # noqa: E402
import gen_armor_items as G                           # noqa: E402  upsert / 哨兵 / 路径
from troop_parts_table import TROOP_TABLE             # noqa: E402  挑件表（slug / troop 的唯一定义源）
import taikou_equip_tables as EQ                      # noqa: E402  两张数据表（兵种/武将装备）

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

OUT_XML = os.path.join(G.MD, "taikou_items", "troop_armors.xml")
HEADER = '<?xml version="1.0" encoding="utf-8"?>\n<Items>\n'

# 🔴 **本工具自己的哨兵**（与武将甲/盔/武器的哨兵各一个）：都往同一个锚点插的话，
#    后跑的会把先跑的挤到上面 → 每次跑都重排一次，两边 `--check` 交替报过期。
MARK = ("<!-- ==== 兵种甲/兜名（tools/sw2-pipeline/gen_troop_armor_items.py 产出，"
        "禁止手改）==== -->")

# ───────── 数值：按兵种等级给（见文件头「数值口径」）─────────
LEVEL_STATS = {
    6:  dict(body=14, leg=6,  arm=2, weight=1.3, appearance=1.0,
             head=16, head_weight=1.5, head_appearance=1.0),
    11: dict(body=16, leg=8,  arm=4, weight=1.8, appearance=1.0,
             head=26, head_weight=2.2, head_appearance=1.5),
    16: dict(body=24, leg=12, arm=6, weight=14.0, appearance=1.2,
             head=34, head_weight=2.5, head_appearance=2.0),
    21: dict(body=39, leg=10, arm=7, weight=13.0, appearance=2.0,
             head=42, head_weight=2.9, head_appearance=2.8),
}

ARMOR_SUFFIX, HELMET_SUFFIX = "_do_a", "_helmet_a"
CULTURE = "Culture.ikoku"          # 与本包其余物品同口径（兵种本体的 culture 另在 TROOPS 里定）


def wanted_slugs():
    """该出物品定义的兵种甲 slug —— **从两张数据表反推**（2026-09-16 用户裁定：数据驱动）。

      ① `TaikouTroop.csv`：兵种身上穿的甲
      ② `HeroEquip.csv` ：武将候选池里的甲（武将也是穿戴者 —— 护卫 4 套 / 九州兵 1 套
                          本来没有对应**兵种**，全靠这里兜住）

    两张表都没提的（农民 / 九州精锐 / 护卫贰 / 护卫陆）**不生成** ——
    生成了就是没人引用的孤儿物品：`prune_taikou_items.py` 会按引用闭包剪掉，
    剪完 `--check` 又报过期，两边来回打架。
    """
    want = set()
    for iid in EQ.equip_item_ids():
        if iid.startswith("taikou_troop_") and iid.endswith(ARMOR_SUFFIX):
            want.add(iid[len("taikou_"):-len(ARMOR_SUFFIX)])
    # 🔴 前缀口径：CSV 里写的是**短 slug**（`guard4`），而 `TROOP_TABLE` 的 slug 是
    #    `troop_guard4`（3D 管线的命名）。这里补上前缀对齐，别在两边各改一套。
    want |= {"troop_" + s for s in EQ.hero_armor_slugs()}
    return want


# 只给武将穿、没有对应兵种的那几套，数值档位按角色定位给（见文件头「数值口径」）：
#   · 护卫壹~伍 = 亲卫具足（大名贴身护卫的重装）→ 最高档
#   · 九州兵    = 与枪足轻同系的地方足轻甲（源表原话「与枪足轻同系但更偏赤」）→ 最低档
HERO_ONLY_LEVEL = {
    "troop_guard1": 21, "troop_guard3": 21, "troop_guard4": 21, "troop_guard5": 21,
    "troop_kyushu": 6,
}


def troop_levels():
    """`TaikouTroop.csv` → {兵种 id: 等级}。等级**只在表里定义一处**，本文件只是消费它。"""
    out = {}
    for d in EQ.troops():
        try:
            out[d["ID"]] = int(d["Level"])
        except (TypeError, ValueError):
            sys.exit("FAIL: 兵种 %s 的等级不是整数：%r" % (d["ID"], d.get("Level")))
    return out


def slug_troop():
    """甲 slug → 穿它的**兵种 id**（多兵种共穿则取第一个）。武将专用套不在表里 → 查不到。"""
    out = {}
    for d in EQ.troops():
        for variant in d["variants"]:
            for _slot, iid in variant:
                if iid.startswith("taikou_troop_") and iid.endswith(ARMOR_SUFFIX):
                    out.setdefault(iid[len("taikou_"):-len(ARMOR_SUFFIX)], d["ID"])
    return out


def in_scope():
    """→ [(sw2_key, troop_id 或 None, slug, has_helmet)]，按 slug 排序。

    `troop_id` = None 表示这套甲**只有武将穿、没有对应兵种**（护卫 4 套 / 九州兵 1 套），
    数值档位改查 `HERO_ONLY_LEVEL`。
    """
    want = wanted_slugs()
    owner = slug_troop()
    out = []
    for key in sorted(TROOP_TABLE):
        r = TROOP_TABLE[key]
        if r["slug"] not in want:
            continue
        out.append((key, owner.get(r["slug"]), r["slug"], bool(r.get("helmet"))))
    return sorted(out, key=lambda t: t[2])


def official_names():
    """SW2 模型键 → dict(ArmorName/HelmetName)，取自 `Sw2OfficialNames.csv`（生成物，见文件头）。"""
    out = {}
    _, en, rows = read_table(G.NAMES_CSV, head=2)
    for r in rows:
        if not r:
            continue
        d = dict(zip(en, r))
        if d.get("Key"):
            out[d["Key"]] = d
    return out


NAMES = official_names()
LEVELS = troop_levels()


def key_of(iid):
    """物品 id → 本地化键。口径同武将甲/兵种武器：**键 = `TAIKOU_` + id 去掉 `taikou_` 前缀**。
    （照 id 直接拼会得到 `TAIKOU_taikou_troop_x_do_a` 两层前缀 —— 第一版踩过。）"""
    return "TAIKOU_" + iid[len("taikou_"):]


def item_block(iid, cn, en, kind, st):
    """一个 `<Item>` 块。`kind` ∈ {'armor','helmet'}；`st` = 该等级的数值行。"""
    if kind == "armor":
        subtype, itype = "body_armor", "BodyArmor"
        attr = ('<Armor body_armor="%d" leg_armor="%d" arm_armor="%d" '
                'has_gender_variations="false" covers_body="true" '
                'modifier_group="plate" material_type="Plate" />'
                % (st["body"], st["leg"], st["arm"]))
        weight, appearance = st["weight"], st["appearance"]
        what = "甲"
    else:
        subtype, itype = "head_armor", "HeadArmor"
        attr = ('<Armor head_armor="%d" has_gender_variations="false" '
                'hair_cover_type="all" modifier_group="plate" material_type="Plate" />'
                % st["head"])
        weight, appearance = st["head_weight"], st["head_appearance"]
        what = "兜"
    head = ('\t<!-- %s（战无2 解包件重定向；网格 tools/armor-pipeline/out/%s.fbx）\n'
            '\t     普通物品：挂进 EquipmentRoster 只决定初始装备，可被扒/被偷/作战利品。 -->\n'
            % (cn, iid))
    return (
        head +
        '\t<Item id="%s" name="{=%s}%s" subtype="%s" mesh="%s" '
        'culture="%s" weight="%s" difficulty="0" appearance="%s" Type="%s">\n'
        '\t\t<ItemComponent>\n'
        '\t\t\t%s\n'
        '\t\t</ItemComponent>\n'
        '\t\t<Flags UseTeamColor="true" Civilian="true" />\n'
        '\t</Item>\n'
        % (iid, key_of(iid), en, subtype, iid, CULTURE, weight, appearance, itype, attr))


def entries():
    """→ [(iid, cn, en, kind, stats)]，甲在前、兜在后（同兵种相邻）。"""
    out = []
    for key, troop, slug, has_helmet in in_scope():
        lvl = LEVELS.get(troop) if troop else HERO_ONLY_LEVEL.get(slug)
        if lvl is None:
            sys.exit("FAIL: %s 的数值档位读不到 —— 兵种的话先把它加进 TaikouTroop.csv；"
                     "只给武将穿的话在 HERO_ONLY_LEVEL 里加一行" % slug)
        if lvl not in LEVEL_STATS:
            sys.exit("FAIL: %s 的等级 %s 没有数值档（LEVEL_STATS 里加一档）" % (troop, lvl))
        st = LEVEL_STATS[lvl]
        nm = NAMES.get(key) or {}
        arm_cn = (nm.get("ArmorName") or "").strip()
        if not arm_cn:
            sys.exit("FAIL: %s（%s）在 Sw2OfficialNames.csv 里没有铠甲正式名" % (key, troop))
        iid = "taikou_%s%s" % (slug, ARMOR_SUFFIX)
        out.append((iid, arm_cn, EN_NAMES[iid], "armor", st))
        if has_helmet:
            hel_cn = (nm.get("HelmetName") or "").strip()
            if not hel_cn:
                sys.exit("FAIL: %s（%s）有兜但 Sw2OfficialNames.csv 没给头盔正式名" % (key, troop))
            out.append(("taikou_%s%s" % (slug, HELMET_SUFFIX), hel_cn,
                        EN_NAMES["taikou_%s%s" % (slug, HELMET_SUFFIX)], "helmet", st))
    return out


# ───────── 英文 fallback（单一事实源 = 这里的字符串 → 被抽进根级英文语言文件）─────────
# 口径同武器：**裸名**（无 `[xx之铠]` 归属结构）。用「颜色/材质 + 日式形制名」拼，
# 形制名保留日文罗马字（Do / Gusoku / Jingasa / Kabuto / Fukumen / Zukin），与
# 「Uchigatana」这类武器名一致 —— 不硬翻成 "Chest Plate" 那种出戏的词。
EN_NAMES = {
    "taikou_troop_yari_ashigaru_do_a": "Brown Leather Do",
    "taikou_troop_yari_ashigaru_helmet_a": "Brown Jingasa",
    "taikou_troop_katana_ashigaru_do_a": "Steel-blue Kusari Katabira",
    "taikou_troop_katana_ashigaru_helmet_a": "Steel-blue Jingasa",
    "taikou_troop_samurai_do_a": "Tea-blue Gusoku",
    "taikou_troop_samurai_helmet_a": "Blue Iron Kabuto",
    "taikou_troop_elite_ashigaru_do_a": "Vermilion Do",
    "taikou_troop_elite_ashigaru_helmet_a": "Vermilion Jingasa",
    "taikou_troop_taisho_do_a": "Sakura Gusoku",
    "taikou_troop_taisho_helmet_a": "Sakura Great Kabuto",
    "taikou_troop_yumi_ashigaru_do_a": "Light Leather Bow Do",
    "taikou_troop_teppo_ashigaru_do_a": "Red-brown Gunpowder Do",
    "taikou_troop_teppo_ashigaru_helmet_a": "Sugegasa",
    "taikou_troop_genin_do_a": "Navy Kunoichi Garb",
    "taikou_troop_chunin_do_a": "Brown Shinobi Garb",
    "taikou_troop_totsunin_do_a": "Red-brown Heavy Do",
    "taikou_troop_totsunin_helmet_a": "Red-brown Kabuto",
    "taikou_troop_tobinin_do_a": "Dark Red Shinobi Garb",
    "taikou_troop_sennin_do_a": "White Shinobi Garb",
    "taikou_troop_sennin_helmet_a": "White Zukin",
    "taikou_troop_hazenin_do_a": "Black Lacquered Gusoku",
    "taikou_troop_hazenin_helmet_a": "Black Lacquered Kabuto",
    "taikou_troop_jonin_do_a": "Black Shinobi Garb",
    "taikou_troop_jonin_helmet_a": "Black Fukumen",
    # 护卫壹~伍（只给武将穿，见 HERO_ONLY_LEVEL）—— 亲卫具足
    "taikou_troop_guard1_do_a": "Brown Leather Bodyguard Do",
    "taikou_troop_guard1_helmet_a": "Brown Bodyguard Kabuto",
    "taikou_troop_guard3_do_a": "Brown Riveted Bodyguard Do",
    "taikou_troop_guard3_helmet_a": "Brown Bodyguard Suji Kabuto",
    "taikou_troop_guard4_do_a": "Golden Lacquered Bodyguard Do",
    "taikou_troop_guard4_helmet_a": "Golden Crested Great Kabuto",
    "taikou_troop_guard5_do_a": "Red-brown Light Bodyguard Do",
    "taikou_troop_guard5_helmet_a": "Red-brown Bodyguard Small Kabuto",
    # 九州兵（只给武将穿）—— 地方足轻甲
    "taikou_troop_kyushu_do_a": "Red-brown Leather Do",
    "taikou_troop_kyushu_helmet_a": "Red-brown Jingasa",
}


def item_file_text(items):
    """生成整个 troop_armors.xml 的期望内容（幂等：以现有文件为基，逐件 upsert）。"""
    if os.path.isfile(OUT_XML):
        txt = io.open(OUT_XML, encoding="utf-8-sig").read()
    else:
        txt = HEADER + "</Items>\n"
    for iid, cn, en, kind, st in items:
        txt, _ = G.upsert(txt, iid, item_block(iid, cn, en, kind, st), tail="</Items>")
    return txt


def prune(txt, items):
    """把**不在名单里**的旧条清掉（名单缩了就跟着缩）。只动本文件、只动 troop 前缀。"""
    want = {iid for iid, _c, _e, _k, _s in items}
    n = 0
    pat = re.compile(r'<Item id="(taikou_troop_[a-z_]+(?:_do_a|_helmet_a))"')
    for iid in sorted(set(pat.findall(txt))):
        if iid in want:
            continue
        a = txt.find('<Item id="%s' % iid)
        b = txt.find("</Item>", a) + 7
        while b < len(txt) and txt[b] in ("\r", "\n"):
            b += 1
        c = txt.rfind("<!--", 0, a)
        if c >= 0:
            a = txt.rfind("\n", 0, c) + 1
        txt = txt[:a] + txt[b:]
        n += 1
    return txt, n


def register_item_csv(items):
    """item.csv 追加/更新（纯新增；列不变）。列 = ID, TK5Name, CNName, TK5Type, Kind, SourceCount, Remark。
    顺带清掉**本工具前缀**下不在名单里的旧行（挑件表撤掉 `troop=` 时，登记行要跟着走）。"""
    cn, en, rows = read_table(G.ITEM_CSV, head=2)
    want = {iid for iid, _c, _e, _k, _s in items}
    rows = [r for r in rows
            if not (r and r[0].startswith("taikou_troop_")
                    and (r[0].endswith(ARMOR_SUFFIX) or r[0].endswith(HELMET_SUFFIX))
                    and r[0] not in want)]
    by_id = {r[0]: i for i, r in enumerate(rows) if r}
    added = 0
    for iid, cn_name, _en, _kind, _st in items:
        row = [iid, "", cn_name, "", "item", "0",
               "战无2 兵种装备（tools/sw2-pipeline/gen_troop_armor_items.py，2026-09-16 登记）"]
        if iid in by_id:
            rows[by_id[iid]] = row
        else:
            rows.append(row)
            added += 1
    return cn, en, rows, added


def cn_line(iid, cn_name):
    return '  <string id="%s" text="%s" />\n' % (key_of(iid), cn_name)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    items = entries()
    old_items = (io.open(OUT_XML, encoding="utf-8-sig").read()
                 if os.path.isfile(OUT_XML) else HEADER + "</Items>\n")
    new_items, n_pruned = prune(item_file_text(items), items)

    cn_txt = io.open(G.CN, encoding="utf-8-sig").read()
    new_cn = G.ensure_cn_marker(cn_txt, MARK)
    for iid, cn_name, _en, _kind, _st in items:
        new_cn = G.upsert_str(new_cn, key_of(iid), cn_line(iid, cn_name), after_mark=MARK)

    if args.check:
        cn_o, en_o, rows_o = read_table(G.ITEM_CSV, head=2)
        cn_n, en_n, rows_n, _ = register_item_csv(items)
        ok = (new_items == old_items and new_cn == cn_txt and rows_n == rows_o)
        print("[兵种甲兜] %s 物品表%s · 中文名%s · item.csv%s" % (
            "✅" if ok else "❌ 过期",
            "一致" if new_items == old_items else "需重跑",
            "一致" if new_cn == cn_txt else "需重跑",
            "一致" if rows_n == rows_o else "需重跑"))
        return 0 if ok else 1

    wrote = 0
    if new_items != old_items:
        io.open(OUT_XML, "w", encoding="utf-8-sig", newline="").write(new_items)
        wrote += 1
    if new_cn != cn_txt:
        io.open(G.CN, "w", encoding="utf-8-sig", newline="").write(new_cn)
        wrote += 1
    cn_i, en_i, rows_i, added = register_item_csv(items)
    write_table(G.ITEM_CSV, cn_i, en_i, rows_i)

    # 自检：三处都要能查到自己的 id
    txt = io.open(OUT_XML, encoding="utf-8-sig").read()
    cnt = io.open(G.CN, encoding="utf-8-sig").read()
    ids = {r[0] for r in read_table(G.ITEM_CSV, head=2)[2] if r}
    miss = [iid for iid, _c, _e, _k, _s in items if 'id="%s"' % iid not in txt]
    miss_cn = [iid for iid, _c, _e, _k, _s in items if '"%s"' % key_of(iid) not in cnt]
    miss_reg = [iid for iid, _c, _e, _k, _s in items if iid not in ids]
    n_arm = sum(1 for i in items if i[3] == "armor")
    n_helm = len(items) - n_arm
    n_nohelm = sum(1 for _k, _t, _s, h in in_scope() if not h)
    print("[兵种甲兜] 写 %d 文件 · 甲 %d + 兜 %d = %d 件（%d 个兵种无兜件）· 清旧 %d · "
          "item.csv 新增 %d · 自检缺 %d/%d/%d"
          % (wrote, n_arm, n_helm, len(items), n_nohelm, n_pruned, added,
             len(miss), len(miss_cn), len(miss_reg)))
    for name, lst in (("物品", miss), ("中文", miss_cn), ("item.csv", miss_reg)):
        if lst:
            print("   ❌ %s 缺：%s" % (name, lst))
    return 1 if (miss or miss_cn or miss_reg) else 0


if __name__ == "__main__":
    sys.exit(main())

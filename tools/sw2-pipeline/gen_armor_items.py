# -*- coding: utf-8 -*-
"""gen_armor_items.py —— 28 件「武将甲」的物品定义 + 中文名（**只新增，不改任何现有内容**）。

产出两处（都幂等：先删掉同 id 的旧块再写，重复跑 = 0 改动）
----------------------------------------------------------
  ① `Taikou\\ModuleData\\taikou_items\\body_armors.xml` —— 28 个 `<Item>`（照幸村那件的字段制式）
  ② `Taikou\\ModuleData\\Languages\\CNs\\std_Taikou_strings.xml` —— 28 条 `TAIKOU_<slug>_do_a` 中文名

为什么是这 28 件
----------------
网格由 `tools/sw2-pipeline/build_armors.py` 产出（`taikou_<slug>_do_a.fbx`）。
挂到人身上**不在这里** —— 那一步在 `Scripts/gen_taikou_era_world.py` 的 `SPECIAL_ARMOR` 表
（它才是 taikou_lords*.xml 的唯一产出方，铁律 22）。

🔴 甲是**普通物品**（不是防丢的"绑定装"）：挂进 EquipmentRoster 只决定初始装备，
之后可以被扒/被偷/被战利品拿走 —— 用户要求（2026-09-15）。

用法：
    python tools/sw2-pipeline/gen_armor_items.py            # 写盘
    python tools/sw2-pipeline/gen_armor_items.py --check    # 只校验（exit 1 = 过期）
"""
import argparse
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))      # csv_dual（CSV 双行表头读写）
from parts_table import TABLE  # noqa: E402
from csv_dual import read_table, write_table  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

MB2 = os.environ.get("MB2_PATH") or r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
MD = os.path.join(MB2, "Modules", "Taikou", "ModuleData")
ITEMS = os.path.join(MD, "taikou_items", "body_armors.xml")      # 甲（子类型 body_armor）
HELMS = os.path.join(MD, "taikou_items", "head_armors.xml")      # 头盔（子类型 head_armor）
CN = os.path.join(MD, "Languages", "CNs", "std_Taikou_strings.xml")

# 甲数值：照幸村那件（42/12/12，plate）。要按角色分档就改这里。
STATS = dict(body_armor=42, leg_armor=12, arm_armor=12, weight=12, appearance=2)


def slug_of(key):
    return TABLE[key]["asset"][len("head_"):-len("_a")]


# ───────── 官方正式名（源 = 查看器名表；见 extract_official_names.py）─────────
NAMES_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Sw2OfficialNames.csv")


def load_names():
    """→ {角色 key: dict(WeaponName/ArmorName/HelmetName/...)}；表缺失不致命（回落拼接名）。"""
    if not os.path.isfile(NAMES_CSV):
        print("   ⚠️ 没有命名表 %s → 中文名回落到拼接名" % NAMES_CSV)
        return {}
    out = {}
    _, en, rows = read_table(NAMES_CSV, head=2)
    for r in rows:
        if not r:
            continue
        d = dict(zip(en, r))
        if d.get("Key"):
            out[d["Key"]] = d
    return out


NAMES = load_names()


def en_names():
    """英雄表里的人名英文（`lord_tk5_<n>` → EnglishName）——甲英文名用 `Oda Nobunaga's Do`。"""
    import csv
    p = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
    out = {}
    if not os.path.isfile(p):
        return out
    r = list(csv.reader(io.open(p, encoding="utf-8-sig")))
    h = r[1]
    for row in r[2:]:
        d = dict(zip(h, row))
        if d.get("ID") and d.get("EnglishName"):
            out[d["ID"]] = d["EnglishName"]
    return out


EN = en_names()


# 两种装备的差异集中在这张表（甲 / 头盔）
KINDS = {
    "armor": dict(
        suffix="_do_a", file_key="items", cn_word="当世具足", cn_note="的甲",
        attr='<Armor body_armor="%d" leg_armor="%d" arm_armor="%d" has_gender_variations="false" covers_body="true" modifier_group="plate" material_type="Plate" />',
        attr_args=(42, 12, 12), weight=12, appearance=2, subtype="body_armor", itype="BodyArmor",
        col_cn="甲", col_en="Armor", name_col="ArmorName",
    ),
    "helmet": dict(
        suffix="_helmet_a", file_key="helms", cn_word="兜", cn_note="的兜",
        attr='<Armor head_armor="%d" has_gender_variations="false" hair_cover_type="all" modifier_group="plate" material_type="Plate" />',
        attr_args=(39,), weight=2.5, appearance=3, subtype="head_armor", itype="HeadArmor",
        col_cn="头盔", col_en="Helmet", name_col="HelmetName",
    ),
}


# 占位头盔用的 mesh（Taikou 里已有的原版盔）——没有真模型时顶着，保证 item 合法可加载。
# 2026-09-15 用户裁定：没模型的先命名占坑、之后再补，并**明确标记**不存在真实模型。
PLACEHOLDER_MESH = "nasal_helmet_reinforced"


def item_block(slug, cn_name, en_name, K, official="", placeholder=False):
    iid = "taikou_%s%s" % (slug, K["suffix"])
    disp = (official or "").strip() or ("%s %s" % (cn_name, K["cn_word"]))
    mesh = PLACEHOLDER_MESH if placeholder else iid
    head = ('\t<!-- %s 的%s（战无2 解包件重定向；网格 tools/armor-pipeline/out/%s.fbx）\n'
            '\t     普通物品：挂进 EquipmentRoster 只决定初始装备，可被扒/被偷/作战利品。 -->\n'
            % (cn_name, K["cn_note"], iid))
    if placeholder:
        head = ('\t<!-- ⚠️⚠️ **占位·无真实模型** ——「%s」（%s 的%s）\n'
                '\t     战无2 原模型里这块与头部/头发连体，没有独立网格可导（2026-09-15 用户裁定：\n'
                '\t     先命名占坑、之后再补）。mesh 暂指原版「%s」顶着，且**未挂到任何人身上**。\n'
                '\t     🔴 将来补模型：build_helmets.py 出件 → 把 mesh 改成 %s → 填 TaikouHero「头盔」列。 -->\n'
                % (disp, cn_name, K["cn_word"], mesh, iid))
    return (
        head +
        '\t<Item id="%s" name="{=TAIKOU_%s%s}%s\'s %s" subtype="%s" '
        'mesh="%s" culture="Culture.ikoku" weight="%s" difficulty="0" appearance="%d" Type="%s">\n'
        '\t\t<ItemComponent>\n'
        '\t\t\t%s\n'
        '\t\t</ItemComponent>\n'
        '\t\t<Flags UseTeamColor="true" Civilian="true" />\n'
        '\t</Item>\n'
        % (iid, slug, K["suffix"], en_name,
           "Do" if K["suffix"] == "_do_a" else "Helmet",
           K["subtype"], mesh, K["weight"], K["appearance"], K["itype"],
           K["attr"] % K["attr_args"]))


def cn_line(slug, cn_name, K, official=""):
    """中文名 = **战无2 官方正式名**（「绯威赤备具足」而不是「真田幸村 当世具足」）。
    `official` 空则回落到旧的拼接名（命名表缺该角色时不至于写空串）。"""
    nm = (official or "").strip() or ("%s %s" % (cn_name, K["cn_word"]))
    return '  <string id="TAIKOU_%s%s" text="%s" />\n' % (slug, K["suffix"], nm)


# ───────────────── ② 登记表：item.csv + TaikouHero.csv 的装备列（2026-09-15 用户裁定）──
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
ITEM_CSV = os.path.join(CSV_DIR, "item.csv")
HERO_CSV = os.path.join(CSV_DIR, "TaikouHero.csv")


def register_item_csv(keys, K, placeholder=()):
    """item.csv 追加/更新（纯新增；列不变）。占位件在备注里**明写无真实模型**。"""
    ph = set(placeholder)
    cn, en, rows = read_table(ITEM_CSV, head=2)
    by_id = {r[0]: i for i, r in enumerate(rows) if r}
    added = 0
    for key in keys:
        slug, cn_name = slug_of(key), TABLE[key]["cn"]
        iid = "taikou_%s%s" % (slug, K["suffix"])
        official = (NAMES.get(key, {}) or {}).get(K["name_col"], "")
        disp = (official or "").strip() or ("%s %s" % (cn_name, K["cn_word"]))
        note = ("⚠️ 占位·无真实模型（战无2 原模型里与头部连体，待补；2026-09-15 登记）"
                if key in ph else
                "战无2 换装新品（%s，2026-09-15 登记）" % disp)
        row = [iid, "", disp, "", "item", "0", note]
        if iid in by_id:
            rows[by_id[iid]] = row
        else:
            rows.append(row); added += 1
    return cn, en, rows, added


def hero_armor_col(keys, K):
    """TaikouHero.csv 末列加「甲」/「头盔」列并填（其余格一字不动）。"""
    cn, en, rows = read_table(HERO_CSV, head=2)
    if K["col_en"] not in en:
        cn, en = list(cn) + [K["col_cn"]], list(en) + [K["col_en"]]
        rows = [list(r) + [""] for r in rows]
    j = en.index(K["col_en"])
    want = {TABLE[k]["taikou"]: "taikou_%s%s" % (slug_of(k), K["suffix"]) for k in keys}
    n = 0
    for r in rows:
        if r and r[0] in want and r[j] != want[r[0]]:
            r[j] = want[r[0]]; n += 1
    return cn, en, rows, n



def prune_kind(kind, keys, K):
    """把**不在名单里**的旧条目清掉（挑件表改了就跟着缩）。"""
    suffix = K["suffix"]
    want = set("taikou_%s%s" % (slug_of(k), suffix) for k in keys)
    path = ITEMS if kind == "armor" else HELMS
    txt = io.open(path, encoding="utf-8-sig").read()
    n_items = 0
    for iid in sorted(set(re.findall(chr(60) + "Item id=" + chr(34) + "(taikou_[a-z_]+%s)" % re.escape(suffix), txt))):
        if iid in want:
            continue
        a = txt.find(chr(60) + "Item id=" + chr(34) + iid)
        b = txt.find(chr(60) + chr(47) + "Item" + chr(62), a) + 7
        while b < len(txt) and txt[b] in (chr(13), chr(10)):
            b += 1
        c = txt.rfind(chr(60) + chr(33) + chr(45) + chr(45), 0, a)
        if c >= 0:
            a = txt.rfind(chr(10), 0, c) + 1
        txt = txt[:a] + txt[b:]
        n_items += 1
    if n_items:
        io.open(path, "w", encoding="utf-8-sig", newline="").write(txt)
    cn = io.open(CN, encoding="utf-8-sig").read()
    n_cn = 0
    for key in sorted(set(re.findall(chr(60) + "string id=" + chr(34) + "(TAIKOU_[a-z_]+%s)" % re.escape(suffix), cn))):
        if "taikou_" + key[len("TAIKOU_"):] in want:
            continue
        a = cn.find(chr(60) + "string id=" + chr(34) + key)
        b = cn.find(chr(62), a)
        while b < len(cn) and cn[b] not in (chr(13), chr(10)):
            b += 1
        while b < len(cn) and cn[b] in (chr(13), chr(10)):
            b += 1
        a = cn.rfind(chr(10), 0, a) + 1
        cn = cn[:a] + cn[b:]
        n_cn += 1
    if n_cn:
        io.open(CN, "w", encoding="utf-8-sig", newline="").write(cn)
    c, e, rows = read_table(ITEM_CSV, head=2)
    keep = [r for r in rows if not (r and r[0].startswith("taikou_") and r[0].endswith(suffix) and r[0] not in want)]
    n_csv = len(rows) - len(keep)
    if n_csv:
        write_table(ITEM_CSV, c, e, keep)
    c2, e2, rows2 = read_table(HERO_CSV, head=2)
    if K["col_en"] in e2:
        jj = e2.index(K["col_en"])
        for r in rows2:
            if len(r) > jj and r[jj].startswith("taikou_") and r[jj].endswith(suffix) and r[jj] not in want:
                r[jj] = ""
    write_table(HERO_CSV, c2, e2, rows2)
    if n_items or n_cn or n_csv:
        print("[%s] 清旧条目：物品 %d · 中文名 %d · item.csv %d" % (kind, n_items, n_cn, n_csv))


def upsert(text, key, block, tail="</Items>"):
    """删掉同 id 的旧块（可选上一行注释）再插到 tail 之前。→ (新文本, 是否变化)"""
    pat = re.compile(r'(?:\t<!-- [^\n]*\n(?:\t[^\n]*\n)*)?\t<Item id="%s"[\s\S]*?</Item>\n' % re.escape(key))
    new, n = pat.subn("", text)
    if n == 0 and key in text:
        raise SystemExit("FATAL: %s 已存在但没匹配到整块，不敢动（先人工看一眼）" % key)
    i = new.rfind(tail)
    if i < 0:
        raise SystemExit("FATAL: 找不到 %s 锚点" % tail)
    return new[:i] + block + new[i:], (n > 0 or block not in text)


CN_MARK = "<!-- ==== 战无2 装备名（tools/sw2-pipeline/gen_armor_items.py 产出，禁止手改）==== -->"
# 🔴 **甲与盔各自一个哨兵**（2026-09-15）：两批都插到同一个锚点的话，后跑的会把先跑的挤到上面
#    → **每次跑都重排一次**，两边 `--check` 交替报过期（武器那边同因，见 gen_weapon_items.W_MARK）。
CN_MARK_HELM = "<!-- ==== 战无2 头盔名（tools/sw2-pipeline/gen_armor_items.py 产出，禁止手改）==== -->"
# 🔴 插入位置铁律（2026-09-15 实机踩过）：**必须插在 era_world 生成块标记之前**。
#    `gen_taikou_era_world.py` 的 sync_cn 是「找到自己的标记行 → 把紧跟其后的连续 <string> 行全删掉再重写」，
#    插在 `</strings>` 前的行正好紧挨着那个块的尾巴 → **被当成块的一部分删掉**（实测 28 条装备名整批消失）。
#    放标记**之前**：删除循环从标记行才开始，前面的一律不碰。（且必须在 `<strings>` 之内，引擎只读它的子节点。）
EW_MARK = "<!-- ==== 生成块：英雄/家族/王国名字"


def upsert_str(text, key, line, after_mark=None):
    """语言文件：删掉同 id 的旧 `<string .../>` 行，再插到 `after_mark`（自己的哨兵）**之后**；
    没给哨兵则退回落「生成块标记之前」。"""
    pat = re.compile(r'[ \t]*<string id="%s"[^>]*/>\r?\n' % re.escape(key))
    new = pat.sub("", text)
    if after_mark and after_mark in new:
        i = new.find(after_mark)
        i = new.find("\n", i) + 1              # 哨兵行的下一行行首
        return new[:i] + line + new[i:]
    i = new.find(EW_MARK)
    if i < 0:
        i = new.rfind("</strings>")
        if i < 0:
            raise SystemExit("FATAL: 语言文件里既没有生成块标记也没有 </strings>")
    else:
        i = new.rfind("\n", 0, i) + 1          # 退到标记行的行首
    return new[:i] + line + new[i:]


def ensure_cn_marker(text, mark=None):
    """在生成块标记之前放本工具的哨兵注释（幂等）。`mark` 缺省 = 甲那个。"""
    mark = mark or CN_MARK
    if mark in text:
        return text
    i = text.find(EW_MARK)
    if i < 0:
        i = text.rfind("</strings>")
    i = text.rfind("\n", 0, i) + 1
    return text[:i] + mark + "\n" + text[i:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--kind", default="all", choices=["armor", "helmet", "all"])
    args = ap.parse_args()
    rc = 0
    for kind in (["armor", "helmet"] if args.kind == "all" else [args.kind]):
        rc |= run_kind(kind, args.check)
    return rc


def run_kind(kind, check=False):
    K = KINDS[kind]
    # 甲 = 全部 28 人；头盔也做全部 28 人 —— **没真实模型的走"占坑"**（2026-09-15 用户裁定：
    # 「这些不存在的就单独命名占坑，之后再补，标记下不存在真实模型」）：
    #   · 有独立盔件的 9 人（挑件表 helmet 非空）→ 真 mesh；
    #   · 其余 19 人头顶那块与头/头发连体、没有独立网格 → mesh 指向**Taikou 里已有的原版盔**顶着，
    #     注释 + item.csv 备注都标「占位·无真实模型」；
    #   · 占位件**不接线**（不填 TaikouHero 的「头盔」列）—— 否则他头上会盖个原版盔，把头发压没。
    keys = sorted(TABLE)
    real = [k for k in keys if TABLE[k].get("helmet")]      # 有真模型（可接线）
    path_items = ITEMS if kind == "armor" else HELMS
    items_txt = io.open(path_items, encoding="utf-8-sig").read()
    cn_txt = io.open(CN, encoding="utf-8-sig").read()
    my_mark = CN_MARK if kind == "armor" else CN_MARK_HELM
    new_items, new_cn = items_txt, ensure_cn_marker(cn_txt, my_mark)
    for key in keys:
        slug, cn_name = slug_of(key), TABLE[key]["cn"]
        en_name = EN.get(TABLE[key]["taikou"]) or slug.title()
        # 官方正式名（甲 → ArmorName 列 / 盔 → HelmetName 列；缺则回落拼接名）
        official = (NAMES.get(key, {}) or {}).get(K["name_col"], "")
        ph = (kind == "helmet" and not TABLE[key].get("helmet"))
        new_items, _ = upsert(new_items, "taikou_%s%s" % (slug, K["suffix"]),
                              item_block(slug, cn_name, en_name, K, official, placeholder=ph),
                              tail="</Items>")
        new_cn = upsert_str(new_cn, "TAIKOU_%s%s" % (slug, K["suffix"]),
                            cn_line(slug, cn_name, K, official), after_mark=my_mark)
    if check:
        ok = (new_items == items_txt and new_cn == cn_txt)
        print("[%s] %s 物品表%s · 中文名%s" % (kind, "✅" if ok else "❌ 过期",
              "一致" if new_items == items_txt else "需重跑", "一致" if new_cn == cn_txt else "需重跑"))
        return 0 if ok else 1
    wrote = 0
    for path, old, new in ((path_items, items_txt, new_items), (CN, cn_txt, new_cn)):
        if old != new:
            io.open(path, "w", encoding="utf-8-sig", newline="").write(new); wrote += 1
    prune_kind(kind, keys, K)
    wire = real if kind == "helmet" else keys          # 占位盔不接线
    cn_i, en_i, rows_i, added = register_item_csv(keys, K, placeholder=[k for k in keys if kind == "helmet" and k not in real])
    write_table(ITEM_CSV, cn_i, en_i, rows_i)
    cn_h, en_h, rows_h, n_filled = hero_armor_col(wire, K)
    write_table(HERO_CSV, cn_h, en_h, rows_h)
    txt = io.open(path_items, encoding="utf-8-sig").read()
    cnt = io.open(CN, encoding="utf-8-sig").read()
    miss = [k for k in keys if 'id="taikou_%s%s"' % (slug_of(k), K["suffix"]) not in txt]
    miss_cn = [k for k in keys if 'TAIKOU_%s%s"' % (slug_of(k), K["suffix"]) not in cnt]
    ids = {r[0] for r in read_table(ITEM_CSV, head=2)[2] if r}
    miss_reg = [k for k in keys if "taikou_%s%s" % (slug_of(k), K["suffix"]) not in ids]
    ph_n = len(keys) - len(real) if kind == "helmet" else 0
    print("[%s] 写 %d 文件 · %d 件%s · item.csv 新增 %d · TaikouHero「%s」填 %d 行 · 自检缺 %d/%d/%d"
          % (kind, wrote, len(keys), ("（占位 %d）" % ph_n) if ph_n else "", added, K["col_cn"],
             n_filled, len(miss), len(miss_cn), len(miss_reg)))
    return 1 if (miss or miss_cn or miss_reg) else 0


if __name__ == "__main__":
    sys.exit(main())

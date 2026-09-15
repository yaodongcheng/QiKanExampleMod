# -*- coding: utf-8 -*-
"""gen_troop_weapon_items.py —— 6 件「兵种通用武器」的物品定义 + 中文名。

为什么单开一个生成器、单开一个 XML 文件
----------------------------------------
`gen_weapon_items.py` 的 `prune()` 会**删掉所有不在 28 武将名单里**的 `taikou_*_weapon_a`
条目 —— 兵种武器若写进同一个文件，下次跑武将生成器就被清掉。
所以走**独立文件 + 独立哨兵**，两边互不干扰。

✅ **不需要改 SubModule** —— 引擎按**目录**加载物品表
   （`SubModule.xml` 里只有 `<XmlName id="Items" path="taikou_items"/>`），
   新文件放进该目录自动生效。

产出三处（都幂等：先删同 id 的旧块再写，重复跑 = 0 改动）
  ① `Taikou\\ModuleData\\taikou_items\\troop_weapons.xml` —— 6 个 `<Item>`
  ② `Taikou\\ModuleData\\Languages\\CNs\\std_Taikou_strings.xml` —— 6 条中文名（自己的哨兵）
  ③ `item.csv` 登记 6 行

🔴 **不动 `TaikouHero.csv`** —— 兵种不是英雄，没有「武器」列可填。

数值口径
--------
沿用 `gen_weapon_items.KINDS`（**同一个 mod 里同一类武器只能有一套数**）。
兵种强弱由 `spnpccharacters.xml` 的 level + skill_template 区分，**不靠武器数值分层** ——
骑砍的伤害 = 武器伤害 × 技能倍率，同一把枪在 Lv6 足轻和 Lv25 武将手里差得很远。

命名口径
--------
兵种是**通用装备（无归属者）**，直接给装备名，**不加 `[xx之武]` 归属结构**
（依据 `全角色武器甲胄兜名表.md` §三）。所以英文 fallback 也是裸名（"Long Spear" 而非 "X's Spear"）。

用法：
    python tools/sw2-pipeline/gen_troop_weapon_items.py            # 写盘
    python tools/sw2-pipeline/gen_troop_weapon_items.py --check    # 只校验（exit 1 = 过期）
"""
import argparse
import csv
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))

from csv_dual import read_table, write_table   # noqa: E402
import gen_armor_items as G                    # noqa: E402  upsert / 哨兵 / 通用工具
import gen_weapon_items as W                   # noqa: E402  KINDS / item_block / read_dims
from build_weapons import TROOP_WEAPONS        # noqa: E402  源模型 ↔ mesh slug 的唯一真源

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TROOP_XML = os.path.join(G.MD, "taikou_items", "troop_weapons.xml")
CN = G.CN
SUFFIX = W.SUFFIX
HEADER = '<?xml version="1.0" encoding="utf-8"?>\n<Items>\n'

# 🔴 **本工具自己的哨兵**（与甲/盔/武将武器各一个）：都往同一个锚点插的话，
#    后跑的会把先跑的挤到上面 → 每次跑都重排一次，两边 --check 交替报过期。
MARK = ("<!-- ==== 兵种通用武器名（tools/sw2-pipeline/gen_troop_weapon_items.py 产出，"
        "禁止手改）==== -->")

# ───────── 碰撞体（`body_name`）：武器的碰撞形状不是网格，是引擎内置 PhysicsShape ─────────
# 挑法照语义（见 wheels.d/assets.md「武器」条）：长柄 → bo_spear_b，刀 → bo_sword_one_handed。
# ⚠️ 打刀 90cm / 忍刀 78cm 都是**刀不是匕首**，所以不用 gen_weapon_items 给 sword1h 配的
#    `bo_knife_a`（那是给它自己那批短刀/军配/镰用的）。
BODY = {
    "polearm": "bo_spear_b",
    "sword1h": "bo_sword_one_handed",
    "bow": "bo_longbow_a",
    "gun": "bo_composite_crossbows",      # 与 gen_weapon_items 的 gun 取值一致
}


def entries():
    """→ [(slug, cn, en, K, iid)]，顺序同 TROOP_WEAPONS。"""
    out = []
    for _src, slug, cn, en, kind in TROOP_WEAPONS:
        out.append((slug, cn, en, W.KINDS[kind], "taikou_%s%s" % (slug, SUFFIX)))
    return out


def item_file_text(dims, items):
    """生成整个 troop_weapons.xml 的期望内容（幂等：以现有文件为基，逐件 upsert）。"""
    if os.path.isfile(TROOP_XML):
        txt = io.open(TROOP_XML, encoding="utf-8-sig").read()
    else:
        txt = HEADER + "</Items>\n"
    for slug, cn, en, K, iid in items:
        blk = W.item_block(slug, cn, en, K, dims[iid], BODY[_kind_of(slug)],
                           fb_name=en, label=cn)
        txt, _ = G.upsert(txt, iid, blk, tail="</Items>")
    return txt


def _kind_of(slug):
    for _src, s, _cn, _en, kind in TROOP_WEAPONS:
        if s == slug:
            return kind
    raise SystemExit("FATAL: %s 不在 TROOP_WEAPONS 里" % slug)


def prune(txt, items):
    """把**不在名单里**的旧条清掉（名单缩了就跟着缩）。只动本文件、只动本工具的前缀。"""
    want = {iid for _s, _c, _e, _K, iid in items}
    n = 0
    for iid in sorted(set(re.findall(r'<Item id="(taikou_troop_[a-z_]+%s)"' % re.escape(SUFFIX), txt))):
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
    """item.csv 追加/更新（纯新增；列不变）。列 = ID, TK5Name, CNName, TK5Type, Kind, SourceCount, Remark。"""
    cn, en, rows = read_table(G.ITEM_CSV, head=2)
    by_id = {r[0]: i for i, r in enumerate(rows) if r}
    added = 0
    for _slug, cn_name, _en, _K, iid in items:
        row = [iid, "", cn_name, "", "item", "0",
               "战无2 兵种通用武器（tools/sw2-pipeline/gen_troop_weapon_items.py，2026-09-15 登记）"]
        if iid in by_id:
            rows[by_id[iid]] = row
        else:
            rows.append(row)
            added += 1
    return cn, en, rows, added


# ───────── `--purge`：把本工具登记过的四处**全部撤掉** ─────────
# 为什么要它（2026-09-15 用户裁定）：网格**只 stage 到 TifaHead2、还没进 ModKit 导出**，
# 这时候物品定义指向的 mesh 在游戏里根本不存在 —— 那就是 6 个**隐形物品**
# （能在商店/战利品里刷出来、拿在手里什么都没有）。所以「资产就绪 ≠ 可以登记物品」，
# 登记必须等网格真的导出进 Taikou 之后再做。
#
# 撤回必须能一键重来，否则下次正式化时没法恢复；而按铁律 22 又不许手改生成物，
# 所以做成生成器自己的 purge（数据源一动，重跑即恢复）。
PURGE_REMARK = "tools/sw2-pipeline/gen_troop_weapon_items.py"


def purge():
    """删掉 ① troop_weapons.xml ② CN 语言键+哨兵 ③ item.csv 行。→ 各处删了几条"""
    n_xml = n_cn = n_csv = 0
    # ① 物品表：本工具是**唯一**写者，整个文件删掉
    if os.path.isfile(TROOP_XML):
        os.remove(TROOP_XML)
        n_xml = 1
    # ② 语言：删自己的哨兵行 + 6 个键行
    cn_txt = io.open(CN, encoding="utf-8-sig").read()
    new_cn = cn_txt
    if MARK in new_cn:
        new_cn = new_cn.replace(MARK + "\n", "")
    pat = re.compile(r'[ \t]*<string id="TAIKOU_troop_[a-z_]+%s"[^>]*/>\r?\n' % re.escape(SUFFIX))
    new_cn, n_cn = pat.subn("", new_cn)
    if new_cn != cn_txt:
        io.open(CN, "w", encoding="utf-8-sig", newline="").write(new_cn)
    # ③ item.csv：按备注列认自己的行（不按 id 前缀 —— 前缀将来可能被别的东西用）
    cn_i, en_i, rows_i = read_table(G.ITEM_CSV, head=2)
    keep = [r for r in rows_i if not (r and len(r) > 6 and PURGE_REMARK in (r[6] or ""))]
    n_csv = len(rows_i) - len(keep)
    if n_csv:
        write_table(G.ITEM_CSV, cn_i, en_i, keep)
    return n_xml, n_cn, n_csv


def cn_line(slug, cn_name):
    """兵种通用装备 = 裸名（无 [xx之武] 归属结构）。"""
    return '  <string id="TAIKOU_%s%s" text="%s" />\n' % (slug, SUFFIX, cn_name)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--purge", action="store_true",
                    help="撤回本工具登记的四处（网格没导出进 Taikou 之前不该登记物品）")
    args = ap.parse_args()

    if args.purge:
        a, b, c = purge()
        print("[兵种武器] 撤回：物品表 %d 个文件 · 语言键 %d 条 · item.csv %d 行" % (a, b, c))
        print("   ⚠️ 还要跑一次 `python Scripts/gen_taikou_english_strings.py` 重出英文层")
        return 0

    dims = W.read_dims()
    items = entries()
    miss_dim = [iid for _s, _c, _e, _K, iid in items if iid not in dims]
    if miss_dim:
        sys.exit("FAIL: 这些武器没有网格尺寸（先跑 build_weapons.py --set troops）：%s" % miss_dim)

    old_items = (io.open(TROOP_XML, encoding="utf-8-sig").read()
                 if os.path.isfile(TROOP_XML) else HEADER + "</Items>\n")
    new_items = item_file_text(dims, items)
    new_items, n_pruned = prune(new_items, items)

    cn_txt = io.open(CN, encoding="utf-8-sig").read()
    new_cn = G.ensure_cn_marker(cn_txt, MARK)
    for slug, cn_name, _en, _K, iid in items:
        new_cn = G.upsert_str(new_cn, "TAIKOU_%s%s" % (slug, SUFFIX),
                              cn_line(slug, cn_name), after_mark=MARK)

    if args.check:
        cn_o, en_o, rows_o = read_table(G.ITEM_CSV, head=2)
        cn_n, en_n, rows_n, _ = register_item_csv(items)
        ok = (new_items == old_items and new_cn == cn_txt and rows_n == rows_o)
        print("[兵种武器] %s 物品表%s · 中文名%s · item.csv%s" % (
            "✅" if ok else "❌ 过期",
            "一致" if new_items == old_items else "需重跑",
            "一致" if new_cn == cn_txt else "需重跑",
            "一致" if rows_n == rows_o else "需重跑"))
        return 0 if ok else 1

    wrote = 0
    if new_items != old_items:
        io.open(TROOP_XML, "w", encoding="utf-8-sig", newline="").write(new_items)
        wrote += 1
    if new_cn != cn_txt:
        io.open(CN, "w", encoding="utf-8-sig", newline="").write(new_cn)
        wrote += 1
    cn_i, en_i, rows_i, added = register_item_csv(items)
    write_table(G.ITEM_CSV, cn_i, en_i, rows_i)

    # 自检：三处都要能查到自己的 id
    txt = io.open(TROOP_XML, encoding="utf-8-sig").read()
    cnt = io.open(CN, encoding="utf-8-sig").read()
    ids = {r[0] for r in read_table(G.ITEM_CSV, head=2)[2] if r}
    miss = [iid for _s, _c, _e, _K, iid in items if 'id="%s"' % iid not in txt]
    # 🔴 语言键 = `TAIKOU_<slug><SUFFIX>`，**不是** `TAIKOU_<item id>`（item id 自带 `taikou_` 前缀，
    #    照 id 拼会多出一层 → 永远报缺。第一版踩过）。
    miss_cn = [iid for slug, _c, _e, _K, iid in items
               if 'TAIKOU_%s%s"' % (slug, SUFFIX) not in cnt]
    miss_reg = [iid for _s, _c, _e, _K, iid in items if iid not in ids]
    print("[兵种武器] 写 %d 文件 · %d 件 · 清旧 %d · item.csv 新增 %d · 自检缺 %d/%d/%d"
          % (wrote, len(items), n_pruned, added, len(miss), len(miss_cn), len(miss_reg)))
    for name, lst in (("物品", miss), ("中文", miss_cn), ("item.csv", miss_reg)):
        if lst:
            print("   ❌ %s 缺：%s" % (name, lst))
    return 1 if (miss or miss_cn or miss_reg) else 0


if __name__ == "__main__":
    sys.exit(main())

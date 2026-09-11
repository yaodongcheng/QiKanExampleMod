#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""收尾三件：① 李华梅 DX 号  ② 竹千代 stub 归并  ③ 年代名繁体并入 Alias
============================================================================
① 李华梅（原 `lord_tk5_1290`）→ `lord_tk5_1242`
   证据：BUSTUP 槽名 `玛丽亚(李华梅)`（括号=别名，是本目录通行写法）；DX[1242]=玛丽亚；
   同批《大航海时代》联动角色佐伯杏太郎=DX1161、蒂雅=DX1243 均已就位。

② 竹千代（原 `lord_tk5_1106`）→ 并入德川家康后删行
   该行**只有 5 个非空字段**（ID/外观ID/CNName/外观描述_光荣/立绘阶段）= 纯外观 stub，
   无生卒/家族/五维/年代名/列传；**DX 日志全表无「竹千」**（DX[1106] 是别人「佐女牛」）。
   而「竹千代」是德川家康幼名（家康 Alias 已有「松平竹千代」）→ 名字并入家康 Alias、删行。

③ 年代名的**繁体形式**并入 Alias（铁律 25：Alias 必须覆盖该实体所有年代的名字）
   现状：年代名（简体）已 0 缺口；繁体形式缺 570 处（opencc s2t 产出）。
   🔴 安全闸门：生成后做**别名冲突检查** —— 同一个别名键映射到两个不同 ID = 报错回滚
      （`_load_hero_aliases` 是「先到先得」，冲突会静默解析错人）。

用法
----
  python Scripts/finalize_alias_and_ids.py --dry-run
  python Scripts/finalize_alias_and_ids.py
"""
import argparse
import collections
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")

LEE_OLD, LEE_NEW = "lord_tk5_1290", "lord_tk5_1242"      # 李华梅
STUB_ID = "lord_tk5_1106"                                 # 竹千代（纯外观 stub）
STUB_OWNER_CN = "德川家康"                                 # 归并目标
STUB_NAME = "竹千代"
NON_WORDS = ("", "无", "無", "无效", "無效")

# 🔴 河野父子：儿子（lord_tk5_300）的 Alias 里**不该有「河野通直」** —— 那是**父亲**的名字。
#    上一轮误判为「同一人两行」时给子行建了这条别名，确认父子后即是错的；
#    留着会让「河野通直」这个键同时指向父子两人（`_load_hero_aliases` 先到先得 → 静默解析错人）。
#    儿子的正确别名只有「河野通直(牛福丸)」与「河野牛福丸」（牛福丸是他的幼名）。
SON_ID = "lord_tk5_300"
SON_ALIAS = ["河野通直(牛福丸)", "河野牛福丸"]


def main():
    ap = argparse.ArgumentParser(description="finalize aliases and remaining ids")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    try:
        import opencc
    except Exception as exc:                                 # noqa: BLE001
        raise SystemExit(f"[FATAL] 需要 opencc：{exc}")
    s2t = opencc.OpenCC("s2t")

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)
    namecols = [c for c in cols if c.startswith("Name_")]

    def alias_list(r):
        return [x.strip() for x in (r.get("Alias") or "").split("|") if x.strip()]

    def have_set(r):
        s = set(alias_list(r))
        cn = (r.get("CNName") or "").strip()
        if cn:
            s.add(cn)
        return s

    # ── ① 李华梅 ──
    lee = next((r for r in rows if r["ID"] == LEE_OLD), None)
    if lee:
        print(f"① 李华梅：{LEE_OLD} → {LEE_NEW}（外观ID={lee['外观ID']} Alias={lee['Alias']!r}）")
    else:
        print(f"① 李华梅：{LEE_OLD} 不存在（已改或无操作）")

    # ── ② 竹千代 stub 归并 ──
    stub = next((r for r in rows if r["ID"] == STUB_ID), None)
    owner = next((r for r in rows if (r.get("CNName") or "").strip() == STUB_OWNER_CN), None)
    if stub and owner:
        print(f"② 竹千代 stub：{STUB_ID} 归并入 {STUB_OWNER_CN}（{owner['ID']}）后删行")
        print(f"   家康现 Alias：{alias_list(owner)}")
        print(f"   将追加：{STUB_NAME}")
    else:
        print(f"② 竹千代 stub：{'已处理' if not stub else '找不到家康行'}")

    # ── ②b 河野儿子去掉父亲的别名（见 SON_ALIAS 注释）──
    son = next((r for r in rows if r["ID"] == SON_ID), None)
    if son:
        before = alias_list(son)
        if before != SON_ALIAS:
            print(f"②b 河野儿子（{SON_ID}）Alias 纠正：{before} → {SON_ALIAS}")
            son["Alias"] = "|".join(SON_ALIAS)
        else:
            print(f"②b 河野儿子 Alias 已是 {SON_ALIAS}")

    # ── ③ 年代名繁体并入 Alias（**带撞名过滤**）──
    #   🔴 2026-09-11 踩到：子行的 Name_1582/1584 就是「河野通直」（袭父名），
    #      无脑并入 → 该键同时属于父子两人 → 别名表静默解析错人。
    #      故先建「已被谁占」表（含各行 CNName 与已有别名），撞名的一律**不并入 Alias**
    #      （年代名本身留在 Name_<年> 列不动 —— 剧本管线按年取用，不受影响）。
    claimed = {}
    for r in rows:
        h = r["ID"]
        cn0 = (r.get("CNName") or "").strip()
        if cn0:
            claimed.setdefault(cn0, h)
        for a in alias_list(r):
            claimed.setdefault(a, h)

    added = skipped = 0
    skip_samples = []
    for r in rows:
        if r["ID"].startswith(("template_", "pronoun", "prounon")):
            continue
        hid = r["ID"]
        hv = have_set(r)
        add = []
        for c in namecols:
            v = (r.get(c) or "").strip()
            if not v or v in NON_WORDS:
                continue
            for cand in (v, s2t.convert(v)):
                if not cand or cand in hv or cand in add:
                    continue
                holder = claimed.get(cand)
                if holder is not None and holder != hid:
                    skipped += 1
                    if len(skip_samples) < 6:
                        skip_samples.append((hid, cand, holder))
                    continue
                add.append(cand)
        if add:
            r["Alias"] = "|".join(alias_list(r) + add)
            for a in add:
                claimed.setdefault(a, hid)
            added += 1
    print(f"③ 年代名繁体并入 Alias：{added} 行；因撞名跳过 {skipped} 个键")
    for hid, cand, holder in skip_samples:
        print(f"   ⚠️ {hid} 的年代名 {cand!r} 撞 {holder} 的主名/别名 → 不并入")

    # ── 冲突闸门（分两级，判据不同）──
    #   🔴 FATAL：**别名键**撞车 —— 别名表是年代无关的全局表，同一键指向两人 = 查询静默解析错人。
    #   ⚠️ WARN ：**年代名**跨行重名 —— 这是数据本身的歧义（父子同名的袭名），别名表管不了，
    #             但剧本管线也不需要它管：`gen_entity_maps` 组键是
    #             `[CNName] + 别名 + Name_<该年>`，年代名自带年代上下文 → 逐年代解析正确。
    #              实例：河野通直（父，全部年代）与河野通直（子，1582/1584 袭名）。
    alias_owner = collections.defaultdict(set)
    cn_owner = {}
    era_owner = collections.defaultdict(set)
    for r in rows:
        hid = r["ID"]
        cn = (r.get("CNName") or "").strip()
        if cn:
            cn_owner.setdefault(cn, hid)
        for a in alias_list(r):
            alias_owner[a].add(hid)
        for c in namecols:
            v = (r.get(c) or "").strip()
            if v and v not in NON_WORDS:
                era_owner[v].add(hid)

    fatal = {}
    for k, ids in alias_owner.items():
        if len(ids) > 1:
            fatal[k] = ("别名互撞", sorted(ids))
        elif k in cn_owner and cn_owner[k] not in ids:
            fatal[k] = ("别名撞别行主名", sorted(ids | {cn_owner[k]}))
    warn = {k: sorted(v) for k, v in era_owner.items() if len(v) > 1}

    print(f"\n🔴 别名键冲突：{len(fatal)} 个")
    for k, (why, ids) in list(fatal.items())[:12]:
        print(f"   {k!r} [{why}] ← {ids}")
    print(f"⚠️  年代名跨行重名（数据歧义，不阻断）：{len(warn)} 个")
    for k, ids in list(warn.items())[:8]:
        print(f"   {k!r} ← {ids}")
    if fatal:
        print("[FATAL] 别名键冲突——先解决再加（否则查询会静默解析错人）")
        return 1
    print("别名键无冲突 ✓")

    if args.dry_run:
        print("\n--dry-run：未写文件。")
        return 0

    # ── 落盘 ──
    if lee:
        lee["ID"] = LEE_NEW
    if stub and owner:
        ol = alias_list(owner)
        if STUB_NAME not in ol:
            ol.append(STUB_NAME)
        owner["Alias"] = "|".join(ol)
        rows = [r for r in rows if r["ID"] != STUB_ID]

    # 重排（李华梅换号后要归位）
    def sort_key(item):
        i, r = item
        mm = re.match(r"^lord_tk5_(\d+)(_alt)?$", r["ID"])
        return (0, int(mm.group(1)), i) if mm else (1, 0, i)
    rows = [r for _, r in sorted(enumerate(rows), key=sort_key)]

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print("[FATAL] 往返行数不符")
        return 1
    ids_a = sorted(r["ID"] for r in rows)
    ids_b = sorted(r["ID"] for r in back)
    if ids_a != ids_b:
        print("[FATAL] ID 集合变化")
        return 1
    print("往返校验通过")

    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print(f"已写回：{len(back)} 行")
    nums = [int(re.match(r"^lord_tk5_(\d+)", r["ID"]).group(1))
            for r in back if re.match(r"^lord_tk5_(\d+)", r["ID"])]
    bad = [(nums[k - 1], nums[k]) for k in range(1, len(nums)) if nums[k] < nums[k - 1]]
    print(f"DX 号单调递增: {'✓' if not bad else '✗' + str(bad[:3])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

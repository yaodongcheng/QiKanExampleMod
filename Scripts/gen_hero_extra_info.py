#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""英雄扩展信息表 `HeroExtraInfo.csv` —— LWN 给骑砍2英雄的**通用扩展属性**（内容包提供数据）
============================================================================
**定位**（2026-09-12 用户裁定）：这不是太阁专用表，而是**LWN 基座对英雄的扩展属性机制** ——
本代表「开局落点」（该年驻在城），以后加新属性 = 在本表**加列**即可（ID 之外的列全是扩展属性）。

**放哪**：`Modules/<内容包>/ModuleData/DesignData/HeroExtraInfo.csv`
  —— 与 LWN 既有 DesignData 机制（`Data/DesignDataLoad.cs`：内容包注入 Hero/Music/TagPoint/Emotion）
  同一目录；表头格式也照该机制：**三行表头**（第 1 行英文键 / 第 2 行类型 / 第 3 行中文标签），
  数据从第 4 行起。

**首列属性 Spawn_<年>（开局落点）为什么存在**：vanilla 刷领主部队走
`SettlementHelper.GetBestSettlementToSpawnAround(hero)`（按势力关系打分，**不读人物数据**）→
织田信长会被刷到那古野城而不是清洲城。数据下发后由 LWN 的 `HeroInitialSettlementPatch` 接管刷出点。

**数据源**：`TaikouHero.csv` 的 `City_<年>`（该年驻在城）→ 经 `Settlements.csv` 的
`Name_<年>` 翻成据点 id。
**口径**（用户裁定「优先走有指定数据的，没指定就算了」）：空 / 「无效」/「无」/「-」/ 名字对不上
→ **留空**（运行时该英雄走引擎默认落点）。

**与 TaikouHero.csv 的分工**（2026-09-12 用户裁定；必备清单雷 113）：
  · `TaikouHero.csv` = **数据源**：`City_<年>` 是**中文城名**（人读、开发侧、在 `Knowledge/` 目录）。
  · 本表 = **运行时下发物**：**据点 StringId**（游戏读得到、C# 直接查）——铁律 20「运行时引用
    一律 StringId」的就地落实（中文名是显示名，会随改名/语言变）。
  · 🔴 **源表不加 id 列**：`City_<年>` 已是信息源，再存一份 id = 真冗余（两份要互相同步）；
    id 化就发生在"下发"这一步。两份的一致性由**本生成器唯一产出 + `--check` 进体检**来守。

Usage:
  python Scripts/gen_hero_extra_info.py [--module PATH]   # 报告 + 写盘
  python Scripts/gen_hero_extra_info.py --check           # 只校验（进一键体检）
Exit: 0 正常 / 1 有硬问题 / 2 fatal。
"""
import argparse
import csv
import collections
import io
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from csv_dual import read_table  # noqa: E402

CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
SETT = os.path.join(CSV_DIR, "Settlements.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
NO_CITY = ("", "-", "无", "无效")

TABLE_NAME = "HeroExtraInfo.csv"
KEYS = ["ID", "Name"] + ["Spawn_" + e for e in ERAS]
TYPES = ["string"] * len(KEYS)
CN = ["英雄ID", "名称"] + ["落点_" + e for e in ERAS]


def default_module():
    """内容包路径 = <MB2_PATH>/Modules/Taikou（MB2_PATH 读**注册表**，铁律 19）。"""
    if sys.platform == "win32":
        import winreg
        for hive, key in ((winreg.HKEY_CURRENT_USER, "Environment"),
                          (winreg.HKEY_LOCAL_MACHINE,
                           r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
            try:
                with winreg.OpenKey(hive, key) as k:
                    v, _ = winreg.QueryValueEx(k, "MB2_PATH")
                    if v:
                        return os.path.join(v, "Modules", "Taikou")
            except OSError:
                continue
    return None


def name_to_id():
    """{(年, 城名): 据点 id} —— Settlements.csv 的 `Name_<年>` ↔ `id`。"""
    cn, en, rows = read_table(SETT, head=2)
    m = {}
    for r in rows:
        d = {k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(en)}
        for e in ERAS:
            nm = d.get("Name_" + e, "")
            if nm:
                m.setdefault((e, nm), d["id"])
    return m


def build():
    """→ (数据行, 统计)"""
    n2i = name_to_id()
    cn, en, rows = read_table(HERO, head=2)
    out, stat = [], collections.Counter()
    for r in rows:
        d = {k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(en)}
        if d.get("TemplateNPC", ""):
            stat["非人物行跳过"] += 1
            continue
        row = [d.get("ID", ""), d.get("CNName", "")]
        for e in ERAS:
            cty = d.get("City_" + e, "")
            sid = ""
            if cty and cty not in NO_CITY:
                sid = n2i.get((e, cty), "")
                stat["有落点" if sid else "城名对不上"] += 1
            else:
                stat["该代无指定(空/无效)" if cty in NO_CITY else "该代 City 空"] += 1
            row.append(sid)
        out.append(row)
    return out, stat


def read_product(path):
    """读产物（三行表头：键/类型/中文 → 数据从第 4 行起）→ (keys, 数据行)。"""
    t = io.open(path, encoding="utf-8-sig", errors="replace").read()
    rows = [r for r in csv.reader(io.StringIO(t, newline="")) if r and any((c or "").strip() for c in r)]
    if len(rows) < 3:
        return [], []
    return [c.strip() for c in rows[0]], rows[3:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=None, help="内容包路径（缺省读注册表 MB2_PATH 推 Taikou）")
    ap.add_argument("--check", action="store_true", help="只校验产物是否最新")
    args = ap.parse_args()

    md = args.module or default_module()
    if not md:
        print("[FATAL] 定位不到内容包路径；用 --module 指定")
        return 2
    out_dir = os.path.join(md, "ModuleData", "DesignData")
    out_csv = os.path.join(out_dir, TABLE_NAME)

    rows, stat = build()
    with_spawn = sum(1 for r in rows if any(r[2:]))
    print("英雄行 = %d；至少一代有落点 = %d（%.0f%%）" % (len(rows), with_spawn,
          100.0 * with_spawn / max(1, len(rows))))
    print("统计：%s" % dict(stat.most_common()))

    if args.check:
        if not os.path.isfile(out_csv):
            print("[CHECK] ❌ 缺产物 %s（跑一次不带 --check）" % out_csv)
            return 1
        keys, crows = read_product(out_csv)
        if keys != KEYS:
            print("[CHECK] ❌ 表头不一致：%s" % keys)
            return 1
        if len(crows) != len(rows):
            print("[CHECK] ❌ 行数不一致：产物 %d vs 生成 %d" % (len(crows), len(rows)))
            return 1
        for a, b in zip(rows, crows):
            if [x.strip() for x in b] != [x.strip() for x in a]:
                print("[CHECK] ❌ 行不一致：%s" % a[0])
                return 1
        print("[CHECK] ✅ %s 与生成器一致（%d 行 × %d 列）" % (TABLE_NAME, len(crows), len(KEYS)))
        return 0

    if not os.path.isdir(out_dir):
        print("[FATAL] 目录不存在：%s" % out_dir)
        return 2
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(KEYS)
    w.writerow(TYPES)
    w.writerow(CN)
    for r in rows:
        w.writerow(r)
    io.open(out_csv, "wb").write(buf.getvalue().encode("utf-8-sig"))
    # 往返校验（读**实际写出**的内容）
    keys, crows = read_product(out_csv)
    assert keys == KEYS, "表头往返不一致"
    assert len(crows) == len(rows), "行数往返不一致（%d ≠ %d）" % (len(crows), len(rows))
    for a, b in zip(rows, crows):
        assert [x.strip() for x in b] == [x.strip() for x in a], "行往返不一致：%s" % a[0]
    for r in rows:                                   # 铁律 24：值内禁半角逗号/竖线
        for v in r:
            assert "," not in v and "|" not in v, "值内出现禁用字符：%r" % v
    print("\n✅ 已写出 %s（%d 行 × %d 列）；往返读回一致" % (out_csv, len(rows), len(KEYS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

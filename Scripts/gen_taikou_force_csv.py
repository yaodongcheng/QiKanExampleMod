#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""TaikouForce.csv 生成器 —— Kingdom.csv × ForceTaikou.csv 合并（2026-09-11 用户裁定）
============================================================================
**为什么合并**：两张表本来是同一件事（太阁5 势力清单）的两半——
  · `Kingdom.csv`（135 条）= 织丰口径：文化/本地化键/是否织丰原生 + 单列 Owner
  · `ForceTaikou.csv`（184 条）= TK5 官方口径：太阁编号/势力名/别名/4 类型分类 + **六年代当主**
  各查一半、互相打架（如「建国 = 空国」警告的根因之一），合并成一张 **TaikouForce.csv** 作唯一势力表。

**合并规则（外连接 + 一条归并）**
  1. 行 = 两表 ID 并集（133 共有 + 2 只在 Kingdom + 51 只在 ForceTaikou = 186，再减 1 条归并 = **185**）
  2. 🔴 **`ikko_shu` 归并进 `honganji`**（用户裁定）：同一势力两条记录——
     `ikko_shu`（一向宗，Kingdom.csv，**织丰 XML 用的就是这个 id**）
     `honganji`（本愿寺，Kingdom.csv + ForceTaikou，别名=一向宗）
     归并方向 = 保留 `honganji`。
     🔴 **`Clan.csv` 同步改写已退役**（2026-09-11 家族重建）：Clan.csv 整表由
        `Scripts/gen_taikou_clan_csv.py` 从零重建，不再有单列 `Kingdom`，也不再引用 `ikko_shu`；
        本次归并只影响 TaikouForce.csv 自己（`honganji` 的别名已含「一向宗」）。
     ⚠️ 因此**丢掉织丰 XML 的王国 id `ikko_shu`**；备用本地化键 `{=bVqyvzea}Ikko-shu no Ryogoku`
     记录在此以备将来要兼容织丰时用。
  3. **列**：`势力类型` 放**第一列并作主排序键**（2026-09-11 用户裁定），次排序 = ID 字母序。
     列序 = `势力类型 | ID | 势力名 | 短名 | 别名 | Culture | Owner_<年>×6 | 太阁编号`。
     `太阁编号` 放**最后一列**（用户裁定：现行管线零消费，不占前排）——它的含义与用途：
       = 太阁5 `database.xml` 的「統一勢力番号」= 该家门众人物番号 + 120（多值 `|` = 一门全集，
         全库唯一）。段位编码类型：大名 120-919 / 商家 920-944 / 忍者 950-961 / 海贼 965-978。
       ⚠️ 它记的是**家门身份**，不是当年效力对象——所以「编号−120 落在英雄表里的人」与英雄表的
         `Kingdom_<年>` 大量不同名（实测 331 条里 173 条），**这是口径差不是错**。
       用途：对回 TK5 官方数据的唯一稳定键（Snr 快照 force_id 每时代重排会漂移，不能当身份）。
     留 ForceTaikou 的 `势力名/别名/势力类型/Owner_<年>`；
     留 Kingdom 的 `短名(ChineseName)/Culture`。
     🔴 **丢弃五列**：
       · Kingdom 单列 `Owner` —— 与 `Owner_<年>` 重复，且 40 条是织丰编号（那条红的根源）
       · `Is_1568` —— 语义待考（与 `Owner_1568` 有 38 条不一致；试过「当主换人」106/133、
         「1568 新出现」117/133 两个假设都不成立），**无人消费**，用户裁定去掉
       · `IsShokuho` —— 用户裁定去掉（其消费者 `GenerateXml.py`/`gen_entity_maps.py` 在**已冻结的
         剧本工程**里，且读的是 Clan.csv / Kingdom.csv；**Clan.csv 与 Culture.csv 各自保留自己的
         `IsShokuho` 列**，不受影响）
       · `LocozationName` + 一度派生的 `EnglishName` —— 见下方「英文名」节
  4. **只在 ForceTaikou 的 51 个 `org_*`**（商家/忍者/海贼）：`Culture` 统一填 `neutral_culture`（用户裁定）；
     `短名` = 势力名（它们本就不带「家」）。
  5. **只在 Kingdom 的 `noKingdom`**（无主家占位）：保留（用户裁定）；`Owner_<年>` 全 `-`；
     `势力类型` = `Neutral`（4 类型之外）。
  6. **排序**：`势力类型`（Warrior → Trader → Ninja → Pirate → Neutral，见 TYPE_ORDER）→ ID 字母序。

**🔴 Owner_<年> = 存在性 + 当主，口径 = 上级日志（2026-09-11 用户裁定）**
  规则只有一条：**某势力某年存在 ⟺ 上级日志里该年有人的「组织」= 该势力、且「立场」= 当主**。
  当主不在英雄表（泛用 NPC：透波众「六郎次」、轩猿众「道闲」…）→ 从同组织同年代的成员里挑**实名**替补
  （`lord_tk5_*`，部下多的优先）。`-` = 该年不存在。
  ⚠️ **名字必须先过别名表**：上杉谦信 1554/1560 的「组织」写 `长尾家`（= `uesugi` 的别名）；
    组织名还**跨类型撞车**（武家 `茶屋家` vs 商家 `茶屋`）→ 映射键必须是 **(名字, 势力类型)**。
  🔴 **两种旧口径都作废，别再回去**：①沿用 ForceTaikou 的 `-` 原标记（那是从势力槽快照抄的，
  而快照有一批槽位名字没解出来 → 把真势力误标成 `-`，如池田家 1598 有槽有城 35 人却标 `-`）；
  ②按英雄表 `Identity_<年>`=大名 推（会凭空造国，雷 72/74）。**`_analysis/decoded/era_v2` 快照不再参考。**
  🔴 连带（用户明确要求）：**忍者村/海盗众各是一个势力**（日志里它们都有当主）；首领是模板就用实名替补。

**🔴 英文名：用 ID，不设独立列（2026-09-11 用户裁定）**
  `LocozationName` 被删除——它的 134 个 `{=key}` **全部悬空**（95 个 `my_*` 键在 Taikou 语言包
  0 命中；39 个织丰随机键只存在于 `Shokuho/ModuleData/`，本模块不加载），本地化机制对它完全无效。
  一度派生的 `EnglishName` 列也一并删除：**势力英文名 = ID**（武家 ID 本就是门户名的罗马音，
  `oda`/`akamatsu` 只差大小写；显示时首字母大写即可）。**真正的本地化留到后续单独做。**
  ⚠️ **下面是删列前查实的派生知识，做本地化时会用到，别丢**：
    · 51 个 `org_*` 的 ID 是**机器键**（`org_<类型>_<slug>`），不是可显示的英文名。
      类型后缀惯例：商家 slug 自带「屋」（`abumiya`=镫屋）；忍者 slug 是地名、显示名要加 ` Shu`
      （`fuuma`→风魔众）；海贼 slug 多自带 `suigun`（`kumanosuigun`=熊野水军）。
    · 🔴 **退化 slug**：`org_pirate_kougun` 的 slug 是 pykakasi 读音退化（kougun ≠ 江戸水軍），
      势力名=江户水军、别名=`江戸水軍|江戸|江户` → 正确罗马音是 **Edo Suigun**。同类坑见
      `Knowledge/太阁5/太阁5_ForceTaikou_编号体系与校验经验_20260901.md`（钵屋/户隐/羽黑/透波）。
    · **同名不同实体**：`chaya`（茶屋**家**，武家，编号 599|600）vs `org_merchant_chaya`
      （茶屋，商家，编号 929，别名正是「茶屋家」）——靠 ID + 势力类型 区分，名称有意同形。

**纪律**：生成物·禁手改（铁律 22）——改内容改本脚本重跑。
  `--apply` 只写 TaikouForce.csv（带备份）。
  写入前做闭合校验：输出 id 唯一 + 两表 id 全覆盖。
  幂等两跑：第二次必须 0 改动。

Usage:
  python Scripts/gen_taikou_force_csv.py           # 报告 + 预览（不写）
  python Scripts/gen_taikou_force_csv.py --apply   # 写 TaikouForce.csv
  python Scripts/gen_taikou_force_csv.py --check   # 只校验磁盘产物与生成器一致（exit 1 = 过期）
Exit: 0 正常 / 1 --check 发现过期或有硬问题 / 2 fatal。
"""
import argparse
import csv
import io
import os
import re
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tk5_pua_names import restore  # noqa: E402  （太阁5 自绘字形槽还原：畠 等）

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")

KINGDOM = os.path.join(CSV_DIR, "Kingdom.csv")
FORCE = os.path.join(CSV_DIR, "ForceTaikou.csv")
CLAN = os.path.join(CSV_DIR, "Clan.csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
SUP_LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
OUT = os.path.join(CSV_DIR, "TaikouForce.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
SUP_LINE = re.compile(r"Log: SUP\|(.*)$")

# 势力类型的固定排序（第一列 = 主排序键）。Neutral = noKingdom 那种占位行。
TYPE_ORDER = ["Warrior", "Trader", "Ninja", "Pirate", "Neutral"]

# ── Owner_<年> = 该年代此势力的存在性与当主（🔴 2026-09-11 用户裁定：**完全按上级日志**）──
# 规则（只有这一条）：
#   **某势力在某年代存在 ⟺ 上级日志里该年有人的「组织」= 该势力、且「立场」= 当主**
#   当主 = 那个当主本人；**当主不在英雄表**（泛用 NPC，如透波众的「六郎次」/轩猿众的「道闲」）
#   → 从**同组织、同年代**的成员里挑**实名**替补（`lord_tk5_*`，部下多的优先）。
# `-` = 该年此势力不存在（日志里没人挂它 / 没有当主）。
#
# 🔴 为什么改口径（前一版作废的两种口径，都别再回去）：
#   · **旧口径 A（沿用 ForceTaikou 的 `-` 原标记）**：那是**织丰时代从势力槽快照抄来的**，
#     快照有一批槽位的**名字没解出来**（显示成「无」/空）→ 那些势力在表里被误标成不存在
#     （实测：池田家 1598 有槽有城有 35 人，却被标 `-`）。
#   · **旧口径 B（按英雄表 `Identity_<年>`=大名 推）**：会凭空造国（雷 72/74）。
#   · 用户裁定：**只看上级日志**（`Knowledge/太阁5/太阁日志/上级日志.md` = 游戏运行时导出的
#     立场/上司/组织/当主），**不再参考 `_analysis/decoded/era_v2` 快照**。
#   ⚠️ 连带后果（用户明确要求）：**忍者村/海盗众也各是一个势力**（日志里它们都有当主）；
#      首领若是模板则用实名替补——骑士团/水军同理。
#
# 🔴 英文名 = ID，本表不设 EnglishName / LocozationName 列（2026-09-11 用户裁定）。
#    删除前的派生知识（51 个 org_* 的退化 slug、chaya 同名不同实体）已记在文件头，做本地化时查那里。
# 🔴 英文名 = ID，本表不设 EnglishName / LocozationName 列（2026-09-11 用户裁定）。
#    删除前的派生知识（51 个 org_* 的退化 slug、chaya 同名不同实体）已记在文件头，做本地化时查那里。

# 列：中文表头（第 1 行）+ 英文表头（第 2 行）——沿用 ForceTaikou 的双行表头约定
# 🔴 势力类型放第一列并作主排序键（2026-09-11 用户裁定）
# 🔴 太阁编号（TK5_ID）放**最后一列**（2026-09-11 用户裁定：现行管线零消费，不占前排）
COLS_CN = (["势力类型", "ID", "势力名", "短名", "别名", "Culture"]
           + ["Owner_" + e for e in ERAS]
           + ["太阁编号"])
COLS_EN = (["ForceType", "ID", "ForceName", "ShortName", "Alias", "Culture"]
           + ["Owner_" + e for e in ERAS]
           + ["TK5_ID"])

# 归并：被并入者 → 保留者（Clan.csv 引用同步改写）
MERGE_INTO = {"ikko_shu": "honganji"}

ORG_CULTURE = "neutral_culture"     # 51 个 org_* 的文化（用户裁定）
NO_KINGDOM = "noKingdom"


def load_dict(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = [(c or "").strip() for c in (rd.fieldnames or [])]
        rows = [{(k or "").strip(): (v or "").strip() for k, v in r.items()}
                for r in rd]
        return cols, [r for r in rows if any(r.values())]


def parse_sup(path):
    """上级日志 → {年: [rec]}。rec: pid/名/立场/势力/组织/上司/部下（字段名是简体）。
    这是本脚本判断「势力该年存不存在、当主是谁」的**唯一权威来源**（2026-09-11 用户裁定）。"""
    blocks, cur = {}, None
    with io.open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            m = SUP_LINE.search(raw.rstrip("\n"))
            if not m:
                continue
            parts = m.group(1).split("|")
            if parts[0] == "HDR":
                d = dict(x.split(":", 1) for x in parts[1:] if ":" in x)
                cur = d.get("年")
                blocks[cur] = []
                continue
            if cur is None or len(parts) < 5:
                continue
            rec = dict(x.split(":", 1) for x in parts[4:] if ":" in x)
            blocks[cur].append({
                "pid": parts[2], "名": restore(parts[3].strip()),
                "立场": rec.get("立场", "?"), "势力": rec.get("势力", "?"),
                "组织": restore(rec.get("组织", "无")), "上司": restore(rec.get("上司", "无")),
                "部下": int(rec.get("部下") or 0),
            })
    return blocks


def load_force(path):
    """ForceTaikou 双行表头：第 1 行中文、第 2 行英文，数据从第 3 行起。"""
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    hdr, data = rows[0], rows[2:]
    return [dict(zip(hdr, r)) for r in data if r and r[0].strip()]


def build():
    _, kingdom = load_dict(KINGDOM)
    force = load_force(FORCE)
    kd = {r["ID"]: r for r in kingdom}
    ft = {r["ID"]: r for r in force}

    out, problems = {}, []
    for i in sorted(set(kd) | set(ft)):
        if i in MERGE_INTO:
            continue                        # 被归并者不单独出行
        k, f = kd.get(i), ft.get(i)
        row = {c: "" for c in COLS_CN}
        row["ID"] = i
        if f:
            row["太阁编号"] = f.get("太阁编号", "")
            row["势力名"] = f.get("势力名", "")
            row["别名"] = f.get("别名", "")
            row["势力类型"] = f.get("势力类型", "")
            for e in ERAS:
                row["Owner_" + e] = f.get("Owner_" + e, "-") or "-"
        if k:
            row["短名"] = k.get("ChineseName", "")
            row["Culture"] = k.get("Culture", "")
        # ── 补全规则 ──
        if not f:                           # noKingdom：只在 Kingdom 里
            row["势力名"] = row["短名"]
            row["势力类型"] = "Neutral"
            for e in ERAS:
                row["Owner_" + e] = "-"
        if not k:                           # org_*：只在 ForceTaikou 里
            row["短名"] = row["势力名"]
            row["Culture"] = ORG_CULTURE
        out[i] = row

    # ── 闭合校验 ──
    if len(out) != 186 - len(MERGE_INTO):
        problems.append("输出行数 %d，预期 %d" % (len(out), 186 - len(MERGE_INTO)))
    for i in set(kd) | set(ft):
        if i not in out and i not in MERGE_INTO:
            problems.append("两表有 %s，输出里没有" % i)
    for i, r in out.items():
        if not r["势力名"]:
            problems.append("%s 缺势力名" % i)

    # ── Owner_<年>：存在性 + 当主 = 完全按上级日志（见文件头）──
    # 组织名 → 势力 id：**带类型**（武家「茶屋家」vs 商家「茶屋」同名不同家）
    FType = {"大名家": "Warrior", "商家": "Trader", "忍者众": "Ninja", "海贼众": "Pirate"}
    o2f = {}
    for r in out.values():
        for n in [r["势力名"], r["短名"]] + [x.strip() for x in (r.get("别名") or "").split("|") if x.strip()]:
            if n:
                for key in ([n, n[:-1]] if n.endswith("家") else [n]):
                    o2f.setdefault((key, r["势力类型"]), r["ID"])

    _, heroes = load_dict(HERO)
    hero_ids = {h["ID"] for h in heroes if not h.get("模板NPC")}
    rows_by_era = parse_sup(SUP_LOG)
    n_exist = n_sub = n_norep = 0
    subs, norep = [], []
    for i, r in out.items():
        t = r["势力类型"]
        for e in ERAS:
            col = "Owner_" + e
            if t == "Neutral":
                r[col] = "-"
                continue
            mem = [x for x in rows_by_era.get(e, [])
                   if x["pid"] and o2f.get((x["组织"], FType.get(x["势力"], ""))) == i]
            heads = [x for x in mem if x["立场"] == "当主"]
            if not heads:
                r[col] = "-"
                continue
            h = heads[0]
            hid = "lord_tk5_" + h["pid"]
            if hid in hero_ids:
                r[col] = hid
            else:
                # 首领是泛用 NPC → 同组织里找实名替补（部下多的优先）
                cand = sorted([x for x in mem if ("lord_tk5_" + x["pid"]) in hero_ids and x["名"]],
                              key=lambda x: -x["部下"])
                if not cand:
                    # 该组织在英雄表里一个人都没有 → 世界里没这家人，不能建国（空国 = 引擎侧无 RulingClan）
                    # 这是数据事实不是错误 → 记进报告，不进硬问题
                    r[col] = "-"
                    n_norep += 1
                    norep.append("%s %s（当主「%s」，同僚全是泛用 NPC）" % (r["势力名"], e, h["名"]))
                    continue
                r[col] = "lord_tk5_" + cand[0]["pid"]
                n_sub += 1
                subs.append((r["势力名"], e, h["名"], cand[0]["名"]))
            n_exist += 1
    print("  Owner_<年>（日志口径）：存在 %d 格；其中首领用实名替补 %d 格" % (n_exist, n_sub))
    for x in subs[:20]:
        print("     %s %s：模板首领「%s」→ 用「%s」" % x)
    print("  该年在英雄表里无人、因而不出势力的格：%d" % n_norep)
    for x in norep:
        print("     %s" % x)
    return out, kd, problems


def sort_key(row):
    """主排序 = 势力类型（TYPE_ORDER 固定次序）；次排序 = ID 字母序。"""
    t = row.get("势力类型", "")
    return (TYPE_ORDER.index(t) if t in TYPE_ORDER else len(TYPE_ORDER), row["ID"])


def render(out):
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(COLS_CN)
    w.writerow(COLS_EN)
    for r in sorted(out.values(), key=sort_key):
        w.writerow([r.get(c, "") for c in COLS_CN])
    return buf.getvalue()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="写盘（默认只报告）")
    ap.add_argument("--check", action="store_true", help="只校验磁盘产物是否最新")
    args = ap.parse_args()

    for p in (KINGDOM, FORCE, CLAN):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2

    _, clan = load_dict(CLAN)
    out, kd, problems = build()

    n_w = sum(1 for r in out.values() if r["势力类型"] == "Warrior")
    n_t = sum(1 for r in out.values() if r["势力类型"] == "Trader")
    n_n = sum(1 for r in out.values() if r["势力类型"] == "Ninja")
    n_p = sum(1 for r in out.values() if r["势力类型"] == "Pirate")
    n_other = len(out) - n_w - n_t - n_n - n_p
    print("TaikouForce = %d 行（Warrior %d / Trader %d / Ninja %d / Pirate %d / 其他 %d）"
          % (len(out), n_w, n_t, n_n, n_p, n_other))
    print("  来源：Kingdom.csv %d + ForceTaikou.csv %d − 归并 %d = %d"
          % (len(kd), sum(1 for _ in out), len(MERGE_INTO), len(out)))
    for a, b in MERGE_INTO.items():
        print("  归并：%s（%s）→ %s（%s）"
              % (a, (out.get(a) or kd.get(a, {})).get("势力名") or (kd.get(a, {}).get("ChineseName") or "?"), b,
                 out[b]["势力名"] if b in out else "?"))

    # 🔴 Clan.csv 侧的引用已随家族重建退役（2026-09-11）：Clan.csv 现由
    #    gen_taikou_clan_csv.py 从零产出，没有单列 `Kingdom`，也不再引用 ikko_shu。
    #    它的 `Kingdom_<年>` → TaikouForce 闭合由 check_taikou_world_tables.py 常驻把守。
    for e in ERAS:
        bad = sorted({r.get("Kingdom_" + e, "") for r in clan
                      if r.get("Kingdom_" + e, "") not in ("", "-")
                      and r["Kingdom_" + e] not in out and r["Kingdom_" + e] not in MERGE_INTO})
        if bad:
            problems.append("Clan.csv.Kingdom_%s 有 %d 个值在 TaikouForce 里不存在：%s"
                            % (e, len(bad), " ".join(bad[:10])))

    if problems:
        print("\n❌ 硬问题 %d 条：" % len(problems))
        for p in problems:
            print("   %s" % p)
        return 1

    text = render(out)
    if args.check:
        if not os.path.isfile(OUT):
            print("\n[CHECK] 产物不存在：%s" % OUT)
            return 1
        cur = io.open(OUT, encoding="utf-8-sig", newline="").read()
        if cur.replace("\r\n", "\n") != text.replace("\r\n", "\n"):
            print("\n[CHECK] 产物与生成器不一致（改脚本后忘了重跑？）")
            return 1
        print("\n[CHECK] ✅ 产物与生成器一致")
        return 0

    if not args.apply:
        print("\n（未加 --apply，只报告不写）")
        return 0

    # ── 写 TaikouForce.csv ──
    stamp = time.strftime("%Y%m%d_%H%M%S")
    if os.path.isfile(OUT):
        shutil.copy2(OUT, OUT + ".bak_" + stamp)
    with io.open(OUT, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(text)
    print("\n✅ 已写出 %s（%d 行）" % (os.path.basename(OUT), len(out)))

    # ── 同步 Clan.csv：已退役（2026-09-11 家族重建；改 Clan.csv 一律走 gen_taikou_clan_csv.py）──
    return 0


if __name__ == "__main__":
    sys.exit(main())

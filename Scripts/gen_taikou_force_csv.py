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
     归并方向 = 保留 `honganji`；`Clan.csv` 里引用 `ikko_shu` 的 7 行**同步改写**（见 `--apply`）。
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

**🔴 Owner_<年> 重建（2026-09-11 用户裁定：「按 taikouhero 每个年代身份是大名的人来重建」）**
  原 Owner_<年> 的 386 格全是**织丰 StringId**（`lord_1_*`/`lord_2_*`/`dead_lord_*`），其中 103 个不同的
  id **连织丰 XML 里都不存在**（两边都悬空），且 0 格是本项目的 `lord_tk5_*`。重建规则：
    · **Warrior 行** → 英雄表里 `Identity_<年>`=大名 且 `Kingdom_<年>`=该势力 → `lord_tk5_*`；
      0 个候选 → `-`（该年此家无独立势力）；多个候选 → 走 `OWNER_OVERRIDE`，未登记的**生成期报错**
    · **org_* 行**（商家/忍者/海贼）→ 类型模板标记 `@商人`/`@忍者`/`@海贼`（用户裁定「保持 @模板」）；
      该年不存在 → `-`。原表那 105 格悬空的 `lord_*` 一并归一
    · **Neutral**（noKingdom）→ 全 `-`
  ⚠️ **名字必须先过别名表**（`alias2name`）：英雄表用的是**当时的名字**——上杉谦信 1554/1560 写
     `长尾家`、1568 才写 `上杉家`，而 `长尾家` 是 `uesugi` 的别名。严格相等会漏 77 格
     （实测：漏了会把 uesugi 1554/1560 误判成「不存在」）。
  🔴 **存在性不动**：`-`/非 `-` 一律沿用 ForceTaikou 原标记（已与 Snr 快照六年代验证一致），
     本脚本**只替换非 `-` 格的值**。曾经错误地"按英雄表的大名重算存在性"→ 多标 9 格
     （赤松家+4/有马家+2/秋田家+1/池田家+1/少贰家+1：英雄表把「无主家的城主」标成了大名）。
     漏标补正走 `EXISTENCE_FIX`（上杉家 1554/1560，附快照证据）。
     结果：485 → **487** 存在（多的 2 条正好是上杉），丢掉 0 条。

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
  `--apply` 会同时①写 TaikouForce.csv ②改 Clan.csv 的 `ikko_shu` → `honganji`（带备份）。
  写入前做闭合校验：输出 id 唯一 + 两表 id 全覆盖 + `Clan.csv.Kingdom` 全部解析得到。
  幂等两跑：第二次必须 0 改动。

Usage:
  python Scripts/gen_taikou_force_csv.py           # 报告 + 预览（不写）
  python Scripts/gen_taikou_force_csv.py --apply   # 写 TaikouForce.csv + 同步 Clan.csv
  python Scripts/gen_taikou_force_csv.py --check   # 只校验磁盘产物与生成器一致（exit 1 = 过期）
Exit: 0 正常 / 1 --check 发现过期或有硬问题 / 2 fatal。
"""
import argparse
import csv
import io
import os
import shutil
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")

KINGDOM = os.path.join(CSV_DIR, "Kingdom.csv")
FORCE = os.path.join(CSV_DIR, "ForceTaikou.csv")
CLAN = os.path.join(CSV_DIR, "Clan.csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
OUT = os.path.join(CSV_DIR, "TaikouForce.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]

# 势力类型的固定排序（第一列 = 主排序键）。Neutral = noKingdom 那种占位行。
TYPE_ORDER = ["Warrior", "Trader", "Ninja", "Pirate", "Neutral"]

# ── Owner_<年> 重建（2026-09-11 用户裁定：「按 taikouhero 每个年代身份是大名的人来重建」）──
# 🔴 为什么必须重建：原 Owner_<年> 的 386 格全是**织丰 StringId**（`lord_1_*`），
#    其中 103 个不同的 id **在织丰 XML 里也不存在**（两边都悬空）；且 0 格是 `lord_tk5_*`。
# 重建规则：
#   · Warrior 行 → 取英雄表里 `Identity_<年>`=大名 且 `Kingdom_<年>`=该势力名的人 → `lord_tk5_*`
#     0 个候选 → `-`（该年此家无独立势力）；多个候选 → 走下方 OWNER_OVERRIDE，未登记的报错
#   · org_* 行（Trader/Ninja/Pirate）→ 用类型模板标记（用户裁定「保持 @模板」），
#     该年不存在则 `-`。原表里那 105 格悬空的 `lord_*` 一并归一成模板标记
#   · Neutral（noKingdom）→ 全 `-`
# ⚠️ 存在性口径随之变为**英雄表口径**（实测 513 存在 / 597 `-`，与快照口径 485/625 接近）。
TPL = {"Trader": "@商人", "Ninja": "@忍者", "Pirate": "@海贼"}

# 🔴 存在性口径（2026-09-11 用户裁定）：
#   **`Owner_<年>` 是不是 `-` = 该年代此势力存不存在**——生成某年代的 XML 时只看这一格。
#   值本身**不要求是大名**（只要有个人能当代表即可）。所以：
#     · **存在性一律沿用 ForceTaikou 的 `-` 原标记**（已与 Snr 快照六年代交叉验证一致）
#     · 本脚本**只替换非 `-` 格的值**（把悬空的织丰 id 换成可解析的 `lord_tk5_*`）
#   ⚠️ 曾经错误地"按英雄表的大名重算存在性" → 多标 9 格（赤松家+4/有马家+2/秋田家+1/
#      池田家+1/少贰家+1：英雄表把「无主家的城主」标成了大名，快照证实那些年他们没有势力槽）。
# 例外：ForceTaikou 的漏标（快照证实存在却没标）—— 登记在此，附证据
EXISTENCE_FIX = {
    ("上杉家", "1554"): "快照 1554/forces.csv：`39,长尾家,119,上杉谦信,30`（长尾家=上杉家别名）",
    ("上杉家", "1560"): "快照 1560/forces.csv 同有长尾家",
}

# 武家「该年头目不唯一」的人工裁决（拆分同姓家 + 修 Identity 后只剩下面 6 条）
OWNER_OVERRIDE = {
    ("畠山家", "1554"): ("畠山高政", "河内畠山(clan_hatakeyama_1)是本家；能登畠山(clan_notohatakeyama_1)是分家"),
    ("畠山家", "1560"): ("畠山高政", "同上"),
    ("加藤家", "1598"): ("加藤清正", "拆分后 clan_katō_1=清正（肥後熊本）是本家，嘉明已拆去 _2"),
    ("小早川家", "1598"): ("小早川秀秋", "拆分后 clan_kobayakawa_1=秀秋（隆景養子、本家継承），秀包已拆去 _2"),
    ("京极家", "1598"): ("京极高次", "拆分后 clan_kyōgoku_1=高次（京極本家），高知已拆去 _2"),
    ("三好家", "1568"): ("三好义继", "同一家：义继是当主、长逸是家首重臣"),
}

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

    # ── Owner_<年> 重建（见文件头「Owner_<年> 重建」节）──
    # 🔴 名字必须先过别名表：英雄表用的是**当时的名字**（改名族用当代名）——
    #    上杉谦信 1554/1560 写 `长尾家`、1568 才写 `上杉家`，而 `长尾家` 是 `uesugi` 的别名。
    #    严格相等会漏掉 77 个格子（实测：改前 uesugi 1554/1560 被误判为「不存在」）。
    alias2name = {}
    for r in out.values():
        for n in [r["势力名"]] + (r.get("别名") or "").split("|"):
            n = n.strip()
            if n:
                alias2name.setdefault(n, r["势力名"])
                if n.endswith("家"):
                    alias2name.setdefault(n[:-1], r["势力名"])

    def resolve(v):
        return alias2name.get(v) or alias2name.get(v[:-1] if v.endswith("家") else v) or ""

    _, heroes = load_dict(HERO)
    name2id, lord = {}, {}
    for h in heroes:
        if h.get("模板NPC"):
            continue
        name2id.setdefault(h.get("CNName", ""), h["ID"])
        for e in ERAS:
            k = h.get("Kingdom_" + e, "")
            if k and k not in ("无", "无效") and h.get("Identity_" + e) == "大名":
                kdn = resolve(k)                     # ⚠️ 别用 kd（外层是 Kingdom 行字典）
                if kdn:
                    lord.setdefault((kdn, e), []).append((h["ID"], h.get("CNName", "")))
    n_rebuilt = n_tpl = n_dropped = 0
    for i, r in out.items():
        t = r.get("势力类型", "")
        for e in ERAS:
            col = "Owner_" + e
            raw = (r.get(col) or "").strip()
            # 🔴 存在性 = 原标记（ForceTaikou 的 `-`）+ 登记过的漏标补正
            exists = raw not in ("", "-") or (r["势力名"], e) in EXISTENCE_FIX
            if t == "Neutral":
                r[col] = "-"
                continue
            if not exists:
                if raw != "-":
                    n_dropped += 1
                    problems.append("%s %s：原标记 %r 解析为「不存在」，却又有值？" % (r["势力名"], e, raw))
                r[col] = "-"
                continue
            if t in TPL:                       # org_*：模板占位
                r[col] = TPL[t]
                n_tpl += 1
                continue
            ov = OWNER_OVERRIDE.get((r["势力名"], e))
            if ov:
                hid = name2id.get(ov[0])
                if not hid:
                    problems.append("OWNER_OVERRIDE 里 %s 的「%s」在英雄表查无" % (r["势力名"], ov[0]))
                    continue
                r[col] = hid
                n_rebuilt += 1
                continue
            cand = lord.get((r["势力名"], e), [])
            if len(cand) > 1:
                problems.append("%s %s：大名不唯一 %s —— 请登记进 OWNER_OVERRIDE"
                                % (r["势力名"], e, " / ".join(c[1] for c in cand)))
                continue
            if not cand:
                # 存在（原标记非 -）但英雄表找不到大名 → 放宽到「该年挂此势力的任意一人」
                # （用户口径：值不要求是大名）——再找不到才报缺口
                anyp = [h["ID"] for h in heroes
                        if not h.get("模板NPC") and resolve(h.get("Kingdom_" + e, "")) == r["势力名"]
                        and h.get("Kingdom_" + e, "") not in ("无", "无效")]
                if not anyp:
                    problems.append("%s %s：标为存在，但英雄表里没有任何人挂它" % (r["势力名"], e))
                    continue
                cand = [(anyp[0], "(非大名代表)")]
            if r[col] != cand[0][0]:
                n_rebuilt += 1
            r[col] = cand[0][0]
    print("  Owner 值重建：武家改 %d 格；org_* 归一到模板标记 %d 格；存在性沿用原标记"
          % (n_rebuilt, n_tpl))
    return out, kd, problems


def clan_refs(clan_rows):
    """Clan.csv 里引用「被归并 id」的行。"""
    return [(r["ID"], r["Kingdom"]) for r in clan_rows
            if r.get("Kingdom") in MERGE_INTO]


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

    refs = clan_refs(clan)
    print("\nClan.csv 需同步的引用（%d 行）：" % len(refs))
    for cid, k in refs:
        print("   %-22s Kingdom %s → %s" % (cid, k, MERGE_INTO[k]))

    # Clan.csv 同步后 Kingdom 链是否闭合
    unresolved = sorted({r["Kingdom"] for r in clan
                         if r.get("Kingdom") and r["Kingdom"] not in out
                         and r["Kingdom"] not in MERGE_INTO})
    if unresolved:
        problems.append("Clan.csv.Kingdom 有 %d 个值在 TaikouForce 里不存在：%s"
                        % (len(unresolved), " ".join(unresolved[:10])))

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

    # ── 同步 Clan.csv ──
    if refs:
        shutil.copy2(CLAN, CLAN + ".bak_forcemarge_" + stamp)
        with io.open(CLAN, encoding="utf-8-sig", newline="") as fh:
            rd = csv.DictReader(fh)
            cols = [(c or "").strip() for c in rd.fieldnames]
            crows = [{(k or "").strip(): (v or "") for k, v in r.items()} for r in rd]
        n = 0
        for r in crows:
            if r.get("Kingdom") in MERGE_INTO:
                r["Kingdom"] = MERGE_INTO[r["Kingdom"]]
                n += 1
        buf = io.StringIO()
        w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                           quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
        w.writeheader()
        for r in crows:
            w.writerow({c: (r.get(c) or "") for c in cols})
        with io.open(CLAN, "w", encoding="utf-8-sig", newline="") as fh:
            fh.write(buf.getvalue())
        print("✅ Clan.csv 已同步 %d 行（备份 %s.bak_forcemarge_%s）"
              % (n, os.path.basename(CLAN), stamp))
    return 0


if __name__ == "__main__":
    sys.exit(main())

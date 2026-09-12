#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""T4-b 世界四表体检（Culture / Kingdom / Clan / Settlements ↔ TaikouHero）
============================================================================
**体检对象**：`Knowledge/太阁5/骑砍2织丰角色ID对应/csv/` 下四张表
  · `Culture.csv`     —— 文化（`ID/Name/Alias` 三列；9 地域 + neutral_culture + 6 身份文化：忍者/海贼/商人/强盗/浪人/南蛮）
  · `TaikouForce.csv` —— 势力（**Kingdom.csv × ForceTaikou.csv 合并，185 条，2026-09-11 用户裁定**）
  · `Clan.csv`        —— 家族（278 条，**按侍奉关系重建**，id 规则 = 家头苗字罗马音 + 序号）
  · `Settlements.csv` —— 据点 274 条 × 六年代（名字/别名/当主/家族/兵员）
**权威参照**：`TaikouHero.csv`（1111 行英雄总表，2026-09-11 定稿）
           + `TaikouForce.csv`（势力名 + 别名权威 + 六年代存在性；原 ForceTaikou.csv 已并入）

🔴 **2026-09-11 合并纪要**：`Kingdom.csv`（织丰口径 135 条）与 `ForceTaikou.csv`（TK5 官方口径 184 条）
   合并为 `TaikouForce.csv`（生成器 `Scripts/gen_taikou_force_csv.py`），同时
   ①`ikko_shu`（一向宗）归并进 `honganji`（本愿寺）
   ②丢弃四列：Kingdom 单列 `Owner`（与 `Owner_<年>` 重复，且是织丰编号那条红的根源）、
     `Is_1568`（语义待考、无人消费）、`IsShokuho`（消费者在已冻结的剧本工程，且读 Clan/Kingdom 表）
   ③列序 = `势力类型 | ID | 势力名 | 别名 | Culture | Owner_<年>×6 | 太阁编号`（`短名` 2026-09-12 并入 `别名`），
     **势力类型是第一列并作主排序键**（Warrior → Trader → Ninja → Pirate → Neutral），次排序 = ID
     ⚠️ 2026-09-11 的纪要曾把 `LocozationName` 与 `太阁编号` 的位次写错（列早已删/已移到末位），
        2026-09-12 更正为现行列序。

🔴 **2026-09-12 源表收编纪要**：`Kingdom.csv` 的独有贡献实测只有 `Culture` 一列 + `noKingdom` 一行
   → 已由 `Scripts/migrate_taikou_force_source.py` 收编进 `ForceTaikou.csv`（后者成为**唯一势力源表**），
   **Kingdom.csv 归档**至 `csv/_archive/Kingdom_织丰口径_20260912.csv`；
   同时删掉 `ForceTaikou.csv` 的 `Owner_<年>` ×6（**死输入**：全被上级日志重算覆盖，原值是织丰编号）。
   `Clan.csv` 同步裁掉 4 个织丰遗留列 → 列 = `ID | Name | Alias | Culture | Owner_<年>×6 | Kingdom_<年>×6`。

查两件事（plan T4-b 原话）：
  ① **自身定义是否自洽** —— 必填字段、枚举取值、id 唯一
  ② **两两交叉引用是否闭合** —— 引用方指向的 id 在不在；被引用的有没有孤儿

🔴 **2026-09-11 家族重建纪要（用户裁定：家族 = 家臣团）**
  规则：**谁有人侍奉谁就是一家之主**；没部下的人并入其直接上司的家；妻子并入丈夫的家；
  既没上司也没部下的亲人并入该人的家；浪人/无所属**无家**。
  落地（生成器 `Scripts/gen_taikou_clan_csv.py`，六年代各一套）：
    · `Clan.csv` 整表重建 → 列 = `ID | Name | Alias | Culture | Owner_<年> ×6 | Kingdom_<年> ×6`
      （`-` = 该年代此家不存在）。建表时原为 19 列（含 `ScriptName`/`ChineseName`/`LocozationName`/
      `Surname`/`Is_Shokuho` 五个织丰遗留列），2026-09-12 裁掉其中 4 个（详见上方「源表收编纪要」）。
    · `TaikouHero.csv` 的 `ClanID` 单列 → `ClanID_<年>` ×6
    · `Settlements.csv` 的 `Clan_<年>` = 城主（`Owner_<年>`）的家族
  随之作废的旧检查：`Clan.csv.Kingdom`（单列）、`Clan.csv.Owner`（单列）、
    `TaikouHero.ClanID`（单列）、「家族归属分歧」（新规则下同族成员必然同一势力，不可能分歧）。

🔴 **核心闸门：id 体系不许分叉**
  TaikouHero 的主编号 = `lord_tk5_<DX号>`（2026-09-11 裁定），但四表的 Owner 列
  当年是从**织丰**（`lord_1_nanbu` / `dead_lord_1_*` / `spc_*`）结转的，两者不是一套编号
  —— 这类引用**谁都解析不出来**（既不在英雄表、也不在世界里），是硬错误不是警告。
  历史上 `renumber_taikou_hero_ids.py` 的 `sync_chain` 只同步了 ModuleData XML，
  **漏了这四张 CSV** → 本闸门就是防它再分叉（2026-09-11 查出）。

🔴 **据点名口径：只有一个名字列 `Name_All`（2026-09-11 用户裁定）**
  `Name_All` = 该据点的**全部称呼**（历年官方名 + 改名 + 异写误字 + 城下町叫法），`|` 分隔。
  ⚠️ 曾经加过第二个 `Alias` 列 —— **与 Name_All 是同一个概念拆两列**（消费者两边都得查），
    而且等于拿「多槽争用」当理由把矛盾藏进第二列。已合并，别再拆。
  🔴 三条不变量（本脚本查，生成器侧也断言）：同一个名字不得属于两个据点（名字 = 身份）、
    据点内不得重名、每个 `Name_<年>` 都必须在 `Name_All` 里。
  查名时二级回落 = 规范名 → 剥类型后缀（之町/之砦/之里）。

两级口径（纪律：硬错误 = 生成器/编号 bug；警告 = 数据缺口，别把缺口当 bug 报）：
  ❌ 硬错误：id 重复/空、枚举非法、引用悬空、id 体系分叉、当主与家族自相矛盾、据点名重名
  ⚠️ 警告：孤儿（定义但无人引用）、名字对不上（据点表没有这个地名）、无指定当主、
           英雄表里的名字含未还原的私用区码点

跳过（既有约定，见 check_englishname_clan_prefix）：模板行 `template_*` / 变量行 `pronoun_*`
—— 这两列在模板行里本就是错位用法（`SecondName/EnglishName/ClanID/CultureID` 依次右移一格，
详见 check_englishname_clan_prefix 文件头），不参与家族/文化闭合。

Usage:
  python Scripts/check_taikou_world_tables.py              # 独立跑
  python Scripts/check_taikou_world_tables.py --module X   # 兼容 run_all_checks 的接口（忽略）
  python Scripts/check_taikou_world_tables.py -v           # 打印警告明细
Exit: 0 全绿 / 1 有硬错误 / 2 fatal。
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
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
# 英雄表的年代列共 9 个：六交付剧本 + 3 个不交付（1549 / 1584 / dream1560 梦幻剧本）。
# 只有交付剧本进缺口统计——拿 dream1560（太阁5 的 what-if 剧本）的据点去要求据点表补齐，
# 是在查一个永远不会进游戏的东西（实测那三列独有的 16 个地名全是 dream1560）。
HERO_ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
EXTRA_ERAS = ["1549", "1584", "dream1560"]

TK5_TYPE = ("城", "町", "里", "砦")

# 势力类型的合法取值与固定次序（2026-09-11 用户裁定：第一列并作主排序键）——
# 与生成器 `gen_taikou_force_csv.py` 的 TYPE_ORDER 必须一致（那边是排序，这边是闸门）
TYPE_ORDER = ["Warrior", "Trader", "Ninja", "Pirate", "Neutral"]

# 🔴 名字匹配一律**精确全名**（用户 2026-09-11 裁定：城和町会共用前缀，名字必须写全）。
#    禁止剥「城/町/之町」后缀再比 —— 冈崎城 与 冈崎 町 是两个据点。
#    ⚠️ 本项目曾有三处「剥后缀兜底」（本脚本 / gen_settlements_csv.norm_place /
#       gen_taikou_hero_catalog.lookup_seat），2026-09-11 全部拆除，别再写回来。
#    英雄表用的全名（京之町 / 鸟羽之砦 / 伊贺之里）由生成器构建期写进 Name_All。

# 🔴 id 体系闸门：这些前缀 = 织丰（Shokuho）编号，本项目一律不用
SHOKUHO_ID_PREFIXES = ("lord_1_", "lord_2_", "lord_3_", "dead_lord_", "spc_", "sho_")

# 🔴 身份 → 据点类型（用户 2026-09-11 裁定：「浪人在町，武将在城」）
#    英雄表用「城名」还是「町名/里名/砦名」，由他**当年的身份**决定。
#    实测（六交付剧本 × 精确全名命中）对角线近乎全中；首次跑出的 11 组例外**全部**出自
#    人工别名误挂（姬路城挂町槽 49 人次 / 大坂之町、安土之町挂城槽各 8 人次）→ 已修，
#    现全库 0 违反。所以这条按硬错误查：违反 = 别名挂错槽 或 槽的类型写错，都是真问题。
IDENTITY_TYPE = {
    "城": ("大名", "城主", "侍大将", "家老", "部将", "足轻大将", "足轻组头", "国主"),
    "町": ("浪人", "师范", "师范代", "当家", "掌柜", "医师", "锻冶匠", "见习",
           "僧侣", "茶人", "伙计"),
    "里": ("上忍", "中忍", "下忍", "头目"),
    "砦": ("头领", "船头", "水夫头", "船大将", "水夫"),
}

# 据点表排除项（4 个外国港：日本图上没有对应陆地，见 gen_taikou_settlements_xml.EXCLUDE_IDS）
EXCLUDED_SETTLEMENTS = {"village_tk242", "village_tk243", "village_tk244", "village_tk245"}

# 年代列里的哨兵值（不是缺失，是「不适用」）：
#   `无效` = 该年此人已死亡 → Identity/City/CareerStance 三列同时写「无效」（41 人，与 Appear=已死亡 同集）
#   `无`   = 该年此人无主家 → 对应 TaikouForce.csv 的 `noKingdom`
HERO_SENTINELS = {"无效", ""}

WORLD_W, WORLD_H = 2048.0, 1280.0          # 战役地形规格（16×10 节点 × 128m）

hard = []          # (标题, 明细行列表)
warn = []


def load(name, head=2):
    """读表 → (列名, 行 dict)。`head`：2=双行表头取英文键（默认）/ 1=取中文键 / 0=单行表头。"""
    from csv_dual import read_table
    cn, en, raw = read_table(os.path.join(CSV_DIR, name), head=head)
    cols = en if head != 1 else cn
    rows = []
    for r in raw:
        if any((x or "").strip() for x in r):
            rows.append({k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(cols)})
    return cols, rows


def col(rows, name):
    return [r.get(name, "") for r in rows]


def hard_err(title, items):
    if items:
        hard.append((title, items))


def warn_err(title, items):
    if items:
        warn.append((title, items))


def is_shokuho_id(v):
    return any(v.startswith(p) for p in SHOKUHO_ID_PREFIXES)


def is_person(r, cols):
    return r.get("模板NPC", "") == ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（本检查不用）")
    ap.add_argument("-v", "--verbose", action="store_true", help="打印警告明细")
    args = ap.parse_args()

    for fn in ("Culture.csv", "TaikouForce.csv", "Clan.csv", "Settlements.csv",
               "TaikouHero.csv"):
        if not os.path.isfile(os.path.join(CSV_DIR, fn)):
            print("[FATAL] 缺文件：%s" % os.path.join(CSV_DIR, fn), file=sys.stderr)
            return 2

    _, culture = load("Culture.csv")
    _, clan = load("Clan.csv")
    _, sett = load("Settlements.csv")

    # TaikouForce：双行表头，读者暂用第 1 行（中文键）——待迁移到第 2 行（英文键）
    kingdom = load("TaikouForce.csv", head=1)[1]
    # TaikouHero 尚未转双行表头（见 CLAUDE.md CSV 表头规范）
    hero = load("TaikouHero.csv", head=0)[1]

    cult_ids = [r["ID"] for r in culture]
    kd_ids = [r["ID"] for r in kingdom]
    clan_ids = [r["ID"] for r in clan]
    sett_ids = [r["id"] for r in sett]
    hero_ids = set(r["ID"] for r in hero)

    cult_set, kd_set, clan_set = set(cult_ids), set(kd_ids), set(clan_ids)
    people = [r for r in hero if is_person(r, None)]

    # ───────────────── ① 自身自洽 ─────────────────
    def dup_empty(label, ids):
        d = [k for k, v in collections.Counter(ids).items() if v > 1]
        e = sum(1 for k in ids if not k)
        hard_err("%s：id 唯一且非空" % label,
                 (["重复 id：%s" % d] if d else []) + (["空 id ×%d" % e] if e else []))

    dup_empty("Culture.csv", cult_ids)
    dup_empty("TaikouForce.csv", kd_ids)
    dup_empty("Clan.csv", clan_ids)
    dup_empty("Settlements.csv", sett_ids)

    # 2026-09-12 用户裁定：文化表列收敛为 `ID / Name / Alias`——
    #   三名（ScriptName/ChineseName/OtherName）合并后再拆：**主名进 `Name`，其余进 `Alias`**（`|` 分隔）；
    #   `LocozationName` 删（本地化键在 spcultures.xml 的 name="{=…}" 里，表里不重复记 → 零信息损失）、
    #   `IsShokuho` 删（所有文化已自建，无来源之分）、`IsMainCulture` 删（该信息在生成器 `CULTURES` 里）。
    hard_err("Culture.csv：必填 Name",
             ["%s 缺 Name" % r["ID"] for r in culture if not r.get("Name")])

    # 🔴 势力「别名」里不得出现英雄表的人名（2026-09-12 用户抓出）——
    #   别名 = **门户名的异写**（`田山家` = `畠山家`），**不是门众名单**。
    #   实测 `hatakeyama` 的别名曾是 `田山家|田山高政|田山義續|田山義綱`：后三个 = 该家一门众
    #   （与 `太阁编号` 698|699|700 = lord_tk5_578/579/580 同一批人）→ 上游把两样东西塞进了一格。
    #   查成硬错误（不是警告）：人名当作势力别名**定义上就是错的**，且这是跨表一致性（同 id 体系闸门一族）。
    hero_names = set()
    for r in hero:
        for n in [r.get("CNName", "")] + (r.get("Alias") or "").split("|"):
            n = n.strip()
            if len(n) >= 3:            # 少于 3 字容易与门户名撞（如「织田」）
                hero_names.add(n)
    hard_err("TaikouForce.别名 里出现英雄表的人名（别名只放门户异写，不放门众名单）",
             ["%s（%s）：%s" % (r["ID"], r["势力名"], "|".join(x for x in (r.get("别名") or "").split("|")
                                                              if x.strip() in hero_names))
              for r in kingdom
              if any(x.strip() in hero_names for x in (r.get("别名") or "").split("|"))])

    hard_err("Settlements.csv：TK5Type 必须是 %s 之一" % "/".join(TK5_TYPE),
             ["%s = %r" % (r["id"], r.get("TK5Type")) for r in sett
              if r.get("TK5Type") not in TK5_TYPE])

    bad_xy = []
    for r in sett:
        try:
            x, y = float(r["MOD_X"]), float(r["MOD_Y"])
        except (KeyError, ValueError):
            bad_xy.append("%s MOD 坐标非数：%r,%r" % (r["id"], r.get("MOD_X"), r.get("MOD_Y")))
            continue
        if not (0 <= x <= WORLD_W and 0 <= y <= WORLD_H):
            bad_xy.append("%s 坐标越界：(%s,%s)" % (r["id"], x, y))
    hard_err("Settlements.csv：MOD 坐标必须是 0..2048 / 0..1280 内的数", bad_xy)

    # ───────────────── ② 交叉引用闭合 ─────────────────
    hard_err("TaikouForce.Culture → Culture.csv",
             ["%s → %s（Culture.csv 无此文化）" % (r["ID"], r["Culture"]) for r in kingdom
              if r.get("Culture") and r["Culture"] not in cult_set])

    # 🔴 势力类型枚举（2026-09-12 补）：写错一个值**不会报任何错**——
    #    生成器的排序会把它静默丢到末尾（`sort_key` 的 len(TYPE_ORDER) 兜底），
    #    下面那条「空国」警告也会跳过它（按 `== "Warrior"` 筛）。所以必须显式闸门。
    hard_err("TaikouForce.势力类型 必须是 %s 之一" % " / ".join(TYPE_ORDER),
             ["%s = %r" % (r["ID"], r.get("势力类型")) for r in kingdom
              if r.get("势力类型") not in TYPE_ORDER])

    # 🔴 太阁编号（2026-09-12 补）：格式 = `N` 或 `N|N|N`（多值 = 一门全集）；
    #    **全库唯一**（跨行不得重复，这是「对回 TK5 官方数据的稳定键」的前提）。
    #    ⚠️ 允许为空：6 个织丰补造的家（本庄/一宫/三木/二阶堂/少贰/noKingdom）本就不在 TK5 势力表里。
    bad = []
    tkg_seen = {}
    for r in kingdom:
        v = (r.get("太阁编号") or "").strip()
        if not v:
            continue
        if not re.fullmatch(r"\d+(\|\d+)*", v):
            bad.append("%s 太阁编号 = %r（格式应为 N 或 N|N|N）" % (r["ID"], v))
            continue
        for n in v.split("|"):
            if n in tkg_seen:
                bad.append("编号 %s 被两家占用：%s 与 %s" % (n, tkg_seen[n], r["ID"]))
            else:
                tkg_seen[n] = r["ID"]
    hard_err("TaikouForce.太阁编号 格式合法且跨行唯一", bad)

    bad = []
    for r in clan:
        for e in ERAS:
            v = r.get("Kingdom_" + e, "")
            if v and v != "-" and v not in kd_set:
                bad.append("%s Kingdom_%s → %s" % (r["ID"], e, v))
    hard_err("Clan.csv.Kingdom_<年> → TaikouForce.csv（`-` = 该年此家不存在）", bad)

    hard_err("Clan.csv.Culture → Culture.csv",
             ["%s → %s" % (r["ID"], r["Culture"]) for r in clan
              if r.get("Culture") and r["Culture"] not in cult_set])

    bad = []
    for r in people:
        for e in HERO_ERAS:
            v = r.get("ClanID_" + e, "")
            if v and v not in clan_set:
                bad.append("%s %s ClanID_%s → %s" % (r["ID"], r.get("CNName", ""), e, v))
    hard_err("TaikouHero.ClanID_<年> → Clan.csv（人物行；空 = 该年无家＝浪人）", bad)

    hard_err("TaikouHero.CultureID → Culture.csv（人物行）",
             ["%s %s → %s" % (r["ID"], r.get("CNName", ""), r["CultureID"]) for r in people
              if r.get("CultureID") and r["CultureID"] not in cult_set])

    # 据点：家族列 → Clan.csv
    bad = []
    for r in sett:
        for e in ERAS:
            v = r.get("Clan_" + e, "")
            if v and v not in clan_set:
                bad.append("%s Clan_%s → %s" % (r["id"], e, v))
    hard_err("Settlements.Clan_<年> → Clan.csv", bad)

    # 🔴 据点：当主列 id 体系闸门 + 必须在英雄表里
    bad = []
    for r in sett:
        for e in ERAS:
            v = r.get("Owner_" + e, "")
            if not v:
                continue
            if is_shokuho_id(v):
                bad.append("%s Owner_%s = %s（织丰编号，本项目不认）" % (r["id"], e, v))
            elif v not in hero_ids:
                bad.append("%s Owner_%s = %s（英雄表无此人）" % (r["id"], e, v))
    hard_err("Settlements.Owner_<年> 必须是 lord_tk5_* 且存在于英雄表", bad)

    # 🔴 O 列闸门：TaikouForce.Owner_<年> 只允许三态 —— `-` / `@商人|@忍者|@海贼` / `lord_tk5_*`（且在英雄表）
    #    （2026-09-11 重建后新增；重建前这里 386 格全是织丰 `lord_1_*`，其中 103 个 id 连织丰 XML 都没有）
    _tpl_ok = {"@商人", "@忍者", "@海贼"}
    bad = []
    for r in kingdom:
        for e in ERAS:
            v = r.get("Owner_" + e, "")
            if not v or v == "-" or v in _tpl_ok:
                continue
            if v.startswith(("lord_1_", "lord_2_", "dead_lord_", "spc_")):
                bad.append("%s Owner_%s = %s（织丰编号，本项目不认）" % (r["ID"], e, v))
            elif v not in hero_ids:
                bad.append("%s Owner_%s = %s（英雄表无此人）" % (r["ID"], e, v))
    hard_err("TaikouForce.Owner_<年> 只能是 - / @模板标记 / lord_tk5_*（且存在于英雄表）", bad)

    # 🔴 家族表自己的 Owner 列同一闸门
    #    （势力表那条已随 2026-09-11 合并删除——TaikouForce 没有单列 Owner，
    #      织丰编号只剩 Owner_<年> 六列，其闸门见上「Settlements.Owner_<年>」同款 + 下方势力名对账）
    bad = []
    for r in clan:
        for e in ERAS:
            v = r.get("Owner_" + e, "")
            if not v or v == "-":
                continue
            if is_shokuho_id(v):
                bad.append("%s Owner_%s = %s（织丰编号，本项目不认）" % (r["ID"], e, v))
            elif v not in hero_ids:
                bad.append("%s Owner_%s = %s（英雄表无此人）" % (r["ID"], e, v))
    hard_err("Clan.csv.Owner_<年> 只能是 - / lord_tk5_*（且存在于英雄表）", bad)

    # 🔴 互证：Owner_<年> 那位英雄，自己那一年的 ClanID 必须就是本行
    bad = []
    hrow = {r["ID"]: r for r in hero}
    for r in clan:
        for e in ERAS:
            v = r.get("Owner_" + e, "")
            if not v or v == "-":
                continue
            own = (hrow.get(v) or {}).get("ClanID_" + e, "")
            if own != r["ID"]:
                bad.append("%s %s：当主 %s 自己的 ClanID_%s = %r" % (r["ID"], e, v, e, own))
    hard_err("Clan.csv.Owner_<年> ↔ TaikouHero.ClanID_<年> 必须互证", bad)

    # 据点：当主的家族必须与 Clan_<年> 一致（自相矛盾 = 硬错误）
    hc = {r["ID"]: r for r in hero}
    bad, soft = [], []
    for r in sett:
        for e in ERAS:
            o, c = r.get("Owner_" + e, ""), r.get("Clan_" + e, "")
            h = hc.get(o)
            if not (o and c and h):
                continue
            own = h.get("ClanID_" + e, "")
            if own == c:
                continue
            # 城主当年无家（浪人当着城主）→ 生成器沿用他最近有家的那家；这里只警告
            ever = {h.get("ClanID_" + x, "") for x in HERO_ERAS} - {""}
            if not own and c in ever:
                soft.append("%s %s：当主 %s 当年无家，Clan_%s = %s（沿用其有家的年代）"
                            % (r["id"], e, h.get("CNName", o), e, c))
            else:
                bad.append("%s %s：当主 %s 属 %r，但 Clan_%s = %s" % (r["id"], e, o, own, e, c))
    hard_err("Settlements.Clan_<年> 必须等于城主当年的 ClanID_<年>", bad)
    warn_err("Settlements：城主当年无家但据点仍有家族（沿用最近年代）", soft)

    # ───────────────── ③ 名字对账（警告：数据缺口）─────────────────
    # 查名三级回落（用户 2026-09-11 裁定「按 alias 宽匹配」）：
    #   规范名（Name_All 各段）→ 剥类型后缀（之町/之砦/之里）→ 据点自己的 Alias 列
    # 据点名：**只有一个名字列 Name_All**（= 该据点全部称呼，口径见 gen_settlements_csv 文件头）
    # 查名二级回落：规范名 → 剥类型后缀（之町/之砦/之里）
    name_pool = set()
    name_owner = {}                                # 名字 → 据点 id（查重名用）
    dup_names, inner_dup, era_missing = [], [], []
    for r in sett:
        parts = [x.strip() for x in r.get("Name_All", "").split("|") if x.strip()]
        d = [n for n, k in collections.Counter(parts).items() if k > 1]
        if d:
            inner_dup.append("%s 内部重名：%s" % (r["id"], d))
        for n in parts:
            if n in name_owner and name_owner[n] != r["id"]:
                dup_names.append("「%s」同时属于 %s 和 %s" % (n, name_owner[n], r["id"]))
            name_owner[n] = r["id"]
            name_pool.add(n)
        for e in ERAS:
            v = r.get("Name_" + e, "")
            if v and v not in parts:
                era_missing.append("%s 的 Name_%s「%s」不在 Name_All 里" % (r["id"], e, v))
    hard_err("Settlements：同一个名字不得属于两个据点（名字 = 身份，重名即匹配歧义）",
             dup_names)
    hard_err("Settlements：Name_All 内部不得重名", inner_dup)
    hard_err("Settlements：每个 Name_<年> 都必须在 Name_All 里（年代名是 Name_All 的子集）",
             era_missing)

    def city_ok(city):
        # 🔴 只认精确全名（用户 2026-09-11 裁定：「城和町有共同前缀，一定得写全」）
        #    不允许剥 城/町/之町 后再比 —— 冈崎城 / 冈崎 町 是两个据点，剥了就指错地方。
        #    英雄表用的全名（京之町 / 鸟羽之砦）由生成器构建期写进 Name_All（add_full_names）。
        return city in name_pool

    # 🔴 身份 → 类型：命中的据点类型必须与当年身份对应的类型一致（「浪人在町，武将在城」）
    type_owner = {}                                # 名字 → (据点 id, 类型)
    for r in sett:
        for n in [x.strip() for x in r.get("Name_All", "").split("|") if x.strip()]:
            type_owner[n] = (r["id"], r.get("TK5Type", ""))
    id2type = {v: t for v, t in IDENTITY_TYPE.items()}
    id2type = {i: t for t, ids in IDENTITY_TYPE.items() for i in ids}
    bad_type = []
    for e in HERO_ERAS:
        for r in people:
            v, ident = r.get("City_" + e, ""), r.get("Identity_" + e, "")
            if not v or v in HERO_SENTINELS or ident in HERO_SENTINELS or not ident:
                continue
            hit = type_owner.get(v)
            if hit is None:
                continue                            # 名字本身查无 → 归「地名查无」那条报
            want = id2type.get(ident)
            if want and hit[1] != want:
                bad_type.append("%s %s：身份 %s（该在%s）却落在 %s「%s」（%s）"
                                % (r["ID"], e, ident, want, hit[0], v, hit[1]))
    hard_err("TaikouHero：身份对应的据点类型必须与命中的据点一致（浪人在町 / 武将在城 / "
             "忍者在里 / 海贼水军系在砦），不一致 = 别名挂错槽或槽的类型写错",
             sorted(set(bad_type)))

    miss = collections.Counter()
    pua = collections.Counter()
    extra_only = collections.Counter()
    for e in HERO_ERAS + EXTRA_ERAS:
        for r in people:
            v = r.get("City_" + e, "")
            if not v or v in HERO_SENTINELS:
                continue
            if any(ord(ch) > 0xE000 for ch in v):      # 私用区码点：还原表没覆盖到
                if e in HERO_ERAS:
                    pua[v] += 1
            elif not city_ok(v):
                (miss if e in HERO_ERAS else extra_only)[v] += 1
    warn_err("TaikouHero.City_<年> 在地名总表里查无（%d 个不同地名）" % len(miss),
             ["%s（%d 个年代）" % (k, n) for k, n in sorted(miss.items(), key=lambda kv: -kv[1])])
    warn_err("TaikouHero.City_<年> 含未还原的私用区字符（%d 个值）——先补 "
             "Scripts/tk5_pua_names.PUA_TO_CHAR 再谈匹配" % len(pua),
             ["%s（%d 个年代，码点 %s）" % (k, n, " ".join("U+%04X" % ord(c) for c in k if ord(c) > 0xE000))
              for k, n in sorted(pua.items(), key=lambda kv: -kv[1])])
    warn_err("TaikouHero.City_<年>：只在 %s 出现、据点表没有（**不交付的剧本，不计缺口**）"
             % "/".join(EXTRA_ERAS),
             ["%s（%d 个年代）" % (k, n) for k, n in sorted(extra_only.items(), key=lambda kv: -kv[1])])

    # 势力名对账：权威 = TaikouForce.csv（185 条合并表，势力名 + 别名）
    #   （合并前的对照：旧口径 Kingdom.csv 查无 68 个；ForceTaikou 查无仅 17 个
    #     —— 差的 51 个全是 org_* 组织（水军/忍者众/商屋），织丰表里没有）
    f_name = {}
    for f in kingdom:
        for n in [f.get("势力名", "")] + (f.get("别名", "") or "").split("|"):
            n = n.strip()
            if n:
                f_name.setdefault(n, f["ID"])
    miss = collections.Counter()
    for e in HERO_ERAS:
        for r in people:
            v = r.get("Kingdom_" + e, "")
            if v in HERO_SENTINELS or v == "无" or not v:
                continue          # 「无」= 该年此人无主家 → 对应 Kingdom.csv 的 noKingdom，不是缺口
            if v not in f_name:
                miss[v] += 1
    warn_err("TaikouHero.Kingdom_<年> 在 TaikouForce.csv 查无（%d 个不同势力名）" % len(miss),
             ["%s（%d 个年代）" % (k, n) for k, n in sorted(miss.items(), key=lambda kv: -kv[1])])

    # ───────────────── ④ 孤儿（警告：数据缺口）─────────────────
    used_clan = {r.get("ClanID_" + e, "") for r in people for e in HERO_ERAS}
    for r in sett:
        for e in ERAS:
            used_clan.add(r.get("Clan_" + e, ""))
    warn_err("Clan.csv 定义但无任何英雄/据点引用",
             sorted(c for c in clan_set if c and c not in used_clan))

    # 势力表自己有没有被用到（合并后 Kingdom.csv 那套「旧口径」对照已并入同一张表）
    kd_cn = collections.defaultdict(set)
    for r in kingdom:
        for n in (r.get("势力名", ""), r.get("别名", "")):
            if n:
                kd_cn[n].add(r["ID"])
                if n.endswith("家"):
                    kd_cn[n[:-1]].add(r["ID"])
    used_kd = {r.get("Kingdom_" + e, "") for r in clan for e in ERAS} - {"-"}
    for e in HERO_ERAS:
        for r in people:
            v = r.get("Kingdom_" + e, "")
            if v not in HERO_SENTINELS and v:
                used_kd |= kd_cn.get(v[:-1] if v.endswith("家") else v, set())
    warn_err("TaikouForce.csv 定义但无任何家族/英雄引用",
             sorted(k for k in kd_set if k and k not in used_kd))

    # 无家族的势力：建国时会是空国（宗族一个没有）—— T4-c 铺数据前必须补家族或降级为 minor faction
    # ⚠️ 只查 Warrior：商家/忍者/海贼（org_*）本就不该有武家家族，把它们算进来是噪声
    # 🔴 只在「该年代此势力确实存在」的格上查：`-` = 该年不建国 → 本来就不需要家族
    #    （TaikouForce 里 33 个武家六个年代全 `-`（赤松/松平/池田…＝国主家，不是独立大名），
    #      它们永远不建国，旧口径把它们一律算成「空国」是噪声）
    withclan = collections.defaultdict(set)
    for r in clan:
        for e in ERAS:
            v = r.get("Kingdom_" + e, "")
            if v and v != "-":
                withclan[v].add(e)
    warn_err("武家在某年代存在、但该年代没有任何家族挂它（建国 = 空国）",
             ["%s（%s）%s" % (r.get("势力名"), r["ID"], e) for r in kingdom
              if r.get("势力类型") == "Warrior"
              for e in ERAS
              if (r.get("Owner_" + e) or "-").strip() not in ("", "-")
              and e not in withclan.get(r["ID"], set())])

    used_cult = set(r.get("CultureID", "") for r in people) | set(r.get("Culture", "") for r in clan) \
        | set(r.get("Culture", "") for r in kingdom)
    warn_err("Culture.csv 定义但无任何引用",
             sorted(c for c in cult_set if c and c not in used_cult))

    # 🔴 「家族归属分歧」检查已作废（2026-09-11 家族重建）：
    #    新规则下家族 = 家头 + 他的部下，全员必然同属家头的势力，
    #    「同族成员同年效力不同势力」在结构上不可能出现——旧检查的 40 格裁决一并作废。
    #    取而代之：无家者画像（浪人/无所属 → 骑砍侧当游侠）与「一家多主」互证（见上）。
    nocl = collections.Counter()
    for r in people:
        for e in HERO_ERAS:
            if not r.get("ClanID_" + e, "") and r.get("Appear_" + e, "") not in HERO_SENTINELS:
                nocl[e] += 1
    warn_err("无家的英雄（浪人/无所属，骑砍侧当游荡者）",
             ["%s：%d 人" % (e, nocl[e]) for e in HERO_ERAS])

    # 据点六年代全无指定当主（数据缺口，T4-c 接真实归属时才补）
    #   🔴 2026-09-12 改口径：原先只查 1560（年份写死的遗留），实测 1554 无当主 77 个、1560 74 个
    #      ——只报 1560 会让人以为那是 1560 独有的问题。改查「六年代全无当主」才是真缺口
    #      （74 个里 68 个六代全无主，且多为町：太阁5 的町本就不设城主）。
    no_owner = [r["id"] for r in sett
                if r["id"] not in EXCLUDED_SETTLEMENTS
                and not any((r.get("Owner_" + e) or "").strip() for e in ERAS)]
    warn_err("Settlements 六年代全无指定当主（%d 个）" % len(no_owner), no_owner)

    # ───────────────── 报告 ─────────────────
    print("世界四表体检（Culture %d / Kingdom %d / Clan %d / Settlements %d；英雄 %d）"
          % (len(culture), len(kingdom), len(clan), len(sett), len(hero)))
    if hard:
        print("\n❌ 硬错误 %d 类：" % len(hard))
        for title, items in hard:
            print("  ▸ %s" % title)
            for it in items[:12]:
                print("      %s" % it)
            if len(items) > 12:
                print("      … 另有 %d 条" % (len(items) - 12))
    if warn:
        print("\n⚠️  警告 %d 类（数据缺口，不阻断）：" % len(warn))
        for title, items in warn:
            print("  ▸ %s" % title)
            if args.verbose or len(items) <= 6:
                for it in items[:40]:
                    print("      %s" % it)
                if len(items) > 40:
                    print("      … 另有 %d 条" % (len(items) - 40))
            else:
                print("      %s …（共 %d 条，加 -v 看全）" % (", ".join(items[:3]), len(items)))
    if not hard:
        print("\n✅ 硬错误 0：四表自身自洽、交叉引用闭合、id 体系无分叉")
    return 1 if hard else 0


if __name__ == "__main__":
    sys.exit(main())

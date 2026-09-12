#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""Clan.csv 生成器 —— 按「侍奉关系」重建家族表（2026-09-11 用户裁定）
============================================================================
**旧定义作废**：家族不再按苗字（同姓即同族）+ 亲情分。旧 Clan.csv（663 行，织丰口径）
整表存档到 `csv/_archive/`，本脚本从零产出新表。

**唯一规则 = 谁侍奉谁**（用户原话落地）：

  | # | 情况 | 归谁 | 例（1560 实测） |
  |---|---|---|---|
  | R1 | **有部下**（有人直接侍奉他） | **自己开一家**（他就是家头） | 北条氏政只是氏康的直臣，但氏直侍奉他 → 氏政自立一家 |
  | R2 | 没部下、有上司 | 并入**直接上司**的家 | 北条氏直 → 北条氏政家 |
  | R3 | 没部下、没上司 | **无家**（浪人 / 无所属）→ 骑砍侧当游荡者 | 石田三成（1560 浪人） |
  | R4 | 妻子 | 并入**丈夫**的家 | 归蝶 → 织田家 |
  | R5 | 其他亲人（父/母/祖父/亲戚）**既没上司也没部下** | 并入该人的家 | 武田信虎（隐居无主）→ 武田家 |

  一句话记法：**谁有人侍奉，谁就是一家之主**；「当主的家 = 自己 + 部下里不带人的那些」，
  所以带了人的部下自然另立一家。这正是太阁5「城主/国主有自己的家臣团」的形态。

**六个年代各算一套**（人物换主频繁：1304 人里 930 人跨年代换过所属组织、808 人换过立场）：
  · `Clan.csv` 用 `Owner_<年>` / `Kingdom_<年>` 六对列（`-` = 该年代此家不存在）
  · `TaikouHero.csv` 的 `ClanID` 单列 → 拆成 `ClanID_<年>` ×6（全员六格）
  · `Settlements.csv` 的 `Clan_<年>` 重算 = 城主（`Owner_<年>`）的家族

**命名与 id**（用户裁定：名字相同、id 不同）：
  · id = `clan_<苗字罗马音>_<序号>`；序号按「首次出现年代 → 规模降序 → 家头编号」排
  · 名字 = 苗字（5 个「北条家」= `clan_hojo_1..5`，靠骑砍 UI 的当主名区分）

**列**（2026-09-12 用户裁定裁掉 4 个织丰遗留列）：

    ID | Name | Alias | Culture | Owner_1554 | Kingdom_1554 | … | Owner_1598 | Kingdom_1598

  · `Name` = 苗字（原 `ChineseName`；原 `ScriptName` 与它**逐字相同**，已删）
  · `Alias` = **历代异名**，`|` 分隔（铁律 24），**不重复主名**。口径同 `TaikouForce.别名`。
    来源①各年代家头的苗字（按年代先后，改名家如 长尾→上杉 这里就是「长尾」）；
    来源②旧织丰表存档的 `OtherName`（如 木下 → `羽柴|丰臣`，这类靠首列名字推不出来）
  · `Culture` = 该家成员多数文化
  · 删掉的 4 列及实测依据：`ScriptName`（与 Name 逐字相同 283/283）、
    `Surname`（= id 的罗马音块 283/283，机械可推）、
    `LocozationName`（机械拼 `{=TAIKOU_<id>}<罗马音>`，且 **282/283 的键在 Taikou 语言包不存在**）、
    `Is_Shokuho`（283/283 恒为 "0"）
    ⚠️ 势力英文名/本地化键将来做本地化时再起，别把这 4 列当"删错了"加回来

**跨年代同一个家的认定**（否则「织田信长 1554-1582 / 织田秀信 1598」会被拆成两个 id）：
  同一个家 = 家头同一个人，或 两个家头是**直系血缘**（父/母链）且**从未在同一年代同时当家头**。
  ⚠️ 北条氏康与北条氏政是父子、但 1554~1582 同时当家头 → **不并**（数据上他们各有家臣团）。

**输入（只读）**
  · `Knowledge/太阁5/太阁日志/上级日志.md`  —— tkhack 导出的六年代上级快照（简体字段）
  · `csv/TaikouHero.csv`                      —— 人物总表（亲属/英文名/文化；join 键 = ID 里的 DX 号）
  · `csv/TaikouForce.csv`                     —— 势力表（组织名 → 势力 id）
  · `csv/Settlements.csv`                     —— 据点表（只读 `Owner_<年>`，回写 `Clan_<年>`）

**输出**
  · `csv/Clan.csv`（整表重建）· `csv/TaikouHero.csv`（ClanID_<年>）· `csv/Settlements.csv`（Clan_<年>）
  · `Knowledge/太阁5/骑砍2织丰角色ID对应/家族重建报告_<日期>.md`（逐年代统计 + 异常清单）

**纪律**（铁律 22 + 本轮踩坑）：生成物禁手改；`--apply` 先备份、写完往返校验（写盘 == 读回）、
再跑一次必须 0 改动（幂等）。脚本别接 `head`（SIGPIPE 会打断写盘）。

Usage:
  python Scripts/gen_taikou_clan_csv.py            # 报告 + 预览（不写盘）
  python Scripts/gen_taikou_clan_csv.py --apply    # 落盘（带备份 + 往返校验）
  python Scripts/gen_taikou_clan_csv.py --check    # 只校验磁盘产物与生成器一致（exit 1 = 过期）
Exit: 0 正常 / 1 --check 过期或硬问题 / 2 fatal。
"""
import argparse
import collections
import csv
import io
import os
import re
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tk5_pua_names import restore  # noqa: E402  （太阁5 自绘字形槽：U+E00D 之类 → 真字）

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
SUP_LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
ARCHIVE = os.path.join(CSV_DIR, "_archive")

HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
FORCE = os.path.join(CSV_DIR, "TaikouForce.csv")
SETT = os.path.join(CSV_DIR, "Settlements.csv")
CLAN = os.path.join(CSV_DIR, "Clan.csv")
# 泛用 hero 槽位对齐表（gen_generic_hero_ids.py 产出）：合成号 ↔ 真 ID ↔ 各年代槽号
GEN_ALIGN = os.path.join(os.path.dirname(CSV_DIR), "泛用hero槽位对齐_20260912.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
DXRE = re.compile(r"lord_tk5_(\d+)$")
SUPLINE = re.compile(r"Log: SUP\|(.*)$")
ROMAN_CLEAN = re.compile(r"[^A-Za-z]")

# 无家的势力类型（用户裁定：浪人无家；无所属同理——他们连势力都没有）
CLANLESS_FACTION = {"浪人", "?", "", "无"}
# Clan.csv 列序（Owner/Kingdom 成对相邻，便于人读）
# 🔴 2026-09-12 用户裁定裁掉 4 个织丰遗留列（实测全是冗余/常量，见文件头「列」节）：
#    ScriptName（与 Name 逐字相同）/ Surname（= id 的罗马音块）/ LocozationName（机械拼 + 282/283 键悬空）/ Is_Shokuho（恒 0）
CLAN_COLS = (["ID", "Name", "Alias", "Culture"]
             + [c for e in ERAS for c in ("Owner_" + e, "Kingdom_" + e)])
HERO_CLAN_COLS = ["ClanID_" + e for e in ERAS]


def load_dict(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = [(c or "").strip() for c in (rd.fieldnames or [])]
        return cols, [{(k or "").strip(): (v or "").strip() for k, v in r.items()} for r in rd]


def load_force(path):
    """TaikouForce.csv 是双行表头（中文 + 英文），数据从第 3 行起。"""
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    hdr, data = rows[0], rows[2:]
    return [dict(zip(hdr, r)) for r in data if r and r[0].strip()]


def parse_sup(path):
    """上级日志 → {年: {pid: rec}}（字段名是简体：立场/势力/组织/上司/当主/据点/部下/陪臣）"""
    blocks, cur = {}, None
    with io.open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            m = SUPLINE.search(raw.rstrip("\n"))
            if not m:
                continue
            parts = m.group(1).split("|")
            if parts[0] == "HDR":
                d = dict(x.split(":", 1) for x in parts[1:] if ":" in x)
                cur = d.get("年")
                blocks[cur] = {}
                continue
            if cur is None or len(parts) < 5:
                continue
            rec = dict(x.split(":", 1) for x in parts[4:] if ":" in x)
            blocks[cur][parts[2]] = {
                "pid": parts[2], "名": restore(parts[3].strip()),
                "立场": rec.get("立场", "?"), "势力": rec.get("势力", "?"),
                "组织": restore(rec.get("组织", "无")), "上司": restore(rec.get("上司", "无")),
                "当主": restore(rec.get("当主", "无")), "据点": restore(rec.get("据点", "无")),
                "部下": int(rec.get("部下") or 0), "陪臣": int(rec.get("陪臣") or 0),
            }
    return blocks


def cells(v):
    """多值单元格（`|` 分隔）→ 去空列表。"""
    return [x.strip() for x in (v or "").split("|") if x.strip()]


# ─────────────────────────── 主流程 ───────────────────────────

def build():
    sup = parse_sup(SUP_LOG)
    missing = [e for e in ERAS if e not in sup]
    if missing:
        return None, ["上级日志缺年代：%s（先跑 tkhack 导出脚本）" % ",".join(missing)]

    _, hero_rows = load_dict(HERO)
    _, sett_rows = load_dict(SETT)
    force = load_force(FORCE)
    people = [r for r in hero_rows if not r.get("模板NPC")]
    by_dx = {}
    for r in people:
        m = DXRE.match(r["ID"] or "")
        if m:
            by_dx[m.group(1)] = r
    keep = set(by_dx)

    # 🔴 泛用 hero（2026-09-12 用户裁定）：ID 是名字式的（`lord_tk5_ninja_rokurooji`），
    #    **每年占的槽号还不同**（仙左卫门 = 1108/1120）——所以不能靠 ID 里的 DX 号认人。
    #    对齐表给每人一个**合成号**当内部键（9000 起，与真实号段不撞），本函数把
    #    日志行的键从「槽号」换成「合成号」，下游（resolve/keep/clan）全部不用改。
    #    ⚠️ 输出边界必须换回真 ID：`OUT_ID[键]`，别写 `"lord_tk5_" + 键`（那条只对真 DX 号成立）。
    OUT_ID = {}
    GEN_ERAS = {}          # 合成号 → 他该出现的年代集合（其余年代不在日志里是**正常**的）
    if os.path.isfile(GEN_ALIGN):
        with io.open(GEN_ALIGN, encoding="utf-8-sig", newline="") as fh:
            for gr in csv.DictReader(fh):
                syn, hid = (gr.get("合成号") or "").strip(), (gr.get("hero_id") or "").strip()
                if not syn or not hid:
                    continue
                OUT_ID[syn] = hid
                row = by_dx.get(syn)
                if row is None:
                    row = next((p for p in people if (p.get("ID") or "") == hid), None)
                    if row is None:
                        problems.append("对齐表 %s 在英雄表里查无（先跑 promote_generic_npcs.py）" % hid)
                        continue
                    by_dx[syn] = row
                keep.add(syn)
                GEN_ERAS.setdefault(syn, set())
                for e in ERAS:
                    slot = (gr.get("槽号_" + e) or "").strip()
                    if not slot:
                        continue
                    GEN_ERAS[syn].add(e)
                    if slot in sup.get(e, {}):
                        sup[e][syn] = sup[e].pop(slot)
    for dx in by_dx:
        OUT_ID.setdefault(dx, "lord_tk5_" + dx)

    name2dx = {r.get("CNName", ""): dx for dx, r in by_dx.items() if r.get("CNName")}

    # 组织名 → 势力 id（含别名 / 去「家」）
    # 🔴 **势力存在性 = 上级日志口径**（2026-09-11 用户裁定）：某家该年有当主 = 该年存在 = 一个王国。
    #    所以「族头组织」只要该年存在就当王国；`TaikouForce.Owner_<年>` 就是这一格的唯一判据。
    #    配合 R6（势力代表兜底），**每个家族都能挂进一个王国**——实测六年代「无王国」= 0 家。
    org2force = {}          # 组织名 → 势力 id（不分类型，兜底）
    org2force_typed = {}    # (组织名, 势力类型) → 势力 id（消歧：茶屋家(武家) vs 茶屋(商家)）
    FType = {"大名家": "Warrior", "商家": "Trader", "忍者众": "Ninja", "海贼众": "Pirate"}
    force_exists = set()
    for f in force:
        if f.get("ID") == "ID":
            continue
        for e in ERAS:
            if (f.get("Owner_" + e) or "-").strip() not in ("", "-"):
                force_exists.add((f["ID"], e))
        for n in [f.get("势力名", ""), f.get("短名", "")] + cells(f.get("别名", "")):
            if n:
                for key in ([n, n[:-1]] if n.endswith("家") else [n]):
                    org2force.setdefault(key, f["ID"])
                    org2force_typed.setdefault((key, f.get("势力类型", "")), f["ID"])

    # (势力id, 年) → 代表 lord_tk5_*（来自势力表；`-` = 该年不存在，不进表）
    force_owner = {}
    for f in force:
        if f.get("ID") == "ID":
            continue
        for e in ERAS:
            v = (f.get("Owner_" + e) or "-").strip()
            if v and v != "-":
                force_owner[(f["ID"], e)] = v

    def force_of(org, shokugyo):
        """组织名 + 势力类型 → 势力 id（先按类型消歧，再退不分类型的兜底）"""
        if not org or org == "无":
            return None
        return org2force_typed.get((org, FType.get(shokugyo, ""))) or org2force.get(org)

    # 苗字 → 罗马音兜底表：旧 Clan.csv（织丰口径存档，2026-09-11 落档）的 ScriptName→Surname。
    # 用途：家头 EnglishName 为空时（如 lord_tk5_176 冈部贞纲，织丰侧就没有罗马音）也能出 id。
    zok2roman = {}
    # 任一名字 → 该家的**全部名字**（本名在前 + OtherName 异名按原序）——给 Alias 列找历代异名。
    # 🔴 必须按「全部名字」反查，不能只按本名：如丰臣家现名「丰臣」，而旧表那行记的是
    #    本名「木下」+ 异名「羽柴|丰臣」，只按本名查会漏掉这个最典型的改名家。
    zok2names = {}
    arch = os.path.join(ARCHIVE, "Clan_织丰口径_20260911.csv")
    if os.path.isfile(arch):
        _, arows = load_dict(arch)
        for r in arows:
            for k in ("ScriptName", "ChineseName"):
                if r.get(k) and r.get("Surname"):
                    zok2roman.setdefault(r[k], r["Surname"].lower())
                allnm = []
                for n in [r.get(k, "")] + [x.strip() for x in (r.get("OtherName") or "").split("|")]:
                    if n and n not in allnm:
                        allnm.append(n)
                if len(allnm) > 1:
                    for n in allnm:
                        zok2names.setdefault(n, allnm)

    def rel_pids(r, col):
        """亲属列 → pid 列表（值可能是 lord_tk5_N，也可能是人名——如 北条氏政.Grand=北条氏纲）"""
        out = []
        for v in cells(r.get(col)):
            m = DXRE.match(v)
            if m:
                out.append(m.group(1))
            elif v in name2dx:
                out.append(name2dx[v])
        return out

    kin_cols = ["FatherId", "MotherId", "GrandFatherId", "KinsId"]
    parents = {}          # pid → [父, 母]（只放父母，用于「直系血缘」判定）
    relatives = {}        # pid → [(列, 亲属 pid)]
    for dx, r in by_dx.items():
        parents[dx] = rel_pids(r, "FatherId") + rel_pids(r, "MotherId")
        relatives[dx] = [(c, p) for c in kin_cols for p in rel_pids(r, c)]

    # ── 逐年代定家 ──
    problems = []
    warns = []        # 数据缺口类警告（不是生成器 bug，见 wheels「生成器自检两级口径」）
    renames = []      # 改名家（家头苗字历代不一）：只报告，不阻断
    rom_gap = []      # 家头没英文名且旧表也查不到 → id 暂用 unknownN，进报告
    era_clan = {}         # 年 → {pid: head_pid or None}
    era_head = {}         # 年 → set(head_pid)
    stat = {}
    for e in ERAS:
        rows = sup[e]
        byname = {}
        for pid, r in rows.items():
            if r["名"]:
                byname.setdefault(r["名"], pid)

        def is_head(pid, r):
            if r["部下"] > 0 and r["势力"] not in CLANLESS_FACTION:
                return True
            return r["上司"] in ("无", "") and r["立场"] == "当主"

        def resolve(pid):
            """走上司链，找第一个「在英雄表里、且是家头」的人（跨过非英雄城主）"""
            seen, cur = set(), pid
            while cur and cur not in seen:
                seen.add(cur)
                r = rows.get(cur)
                if not r or not r["名"]:
                    return None
                if cur in keep and is_head(cur, r):
                    return cur
                cur = byname.get(r["上司"]) if r["上司"] not in ("无", "") else None
            return None

        clan = {pid: resolve(pid) for pid in sorted(keep, key=int) if pid in rows}
        for pid in sorted(keep, key=int):
            if pid not in rows and (pid not in GEN_ERAS or e in GEN_ERAS[pid]):
                problems.append("[%s] %s（%s）在上级日志里没有行" % (e, by_dx[pid]["CNName"], pid))

        # R4 妻子 → 丈夫
        r4 = 0
        for dx, r in by_dx.items():
            if dx not in rows:
                continue
            for sp in rel_pids(r, "SpouseId"):
                if sp not in rows:
                    continue
                g1, g2 = r.get("Gender", ""), by_dx.get(sp, {}).get("Gender", "")
                wife, hus = (dx, sp) if g1 == "0" else ((sp, dx) if g2 == "0" else (None, None))
                if wife is None:                        # 性别缺失 → 按「无家者并入有家者」
                    if clan.get(dx) is None and clan.get(sp) is not None:
                        clan[dx] = clan[sp]
                        r4 += 1
                    elif clan.get(sp) is None and clan.get(dx) is not None:
                        clan[sp] = clan[dx]
                        r4 += 1
                    continue
                if clan.get(wife) is None and clan.get(hus) is not None:
                    clan[wife] = clan[hus]
                    r4 += 1
                elif clan.get(wife) is not None and clan.get(hus) is not None \
                        and clan[wife] != clan[hus]:
                    problems.append("[%s] 夫妻各有所属：%s(%s) vs %s(%s)"
                                    % (e, by_dx[wife]["CNName"], clan[wife],
                                       by_dx[hus]["CNName"], clan[hus]))

        # R5 亲人（无上司、无部下者）→ 并入
        r5 = 0
        prop = collections.defaultdict(list)        # 亲属 pid → [(关系序, 提议者)]
        for dx, r in by_dx.items():
            if clan.get(dx) is None:
                continue
            for ci, (col, p) in enumerate(relatives.get(dx, [])):
                if p not in keep or clan.get(p) is not None:
                    continue
                pr = rows.get(p)
                if pr is None or pr["上司"] not in ("无", "") or pr["部下"] > 0:
                    continue                        # 有上级或带人 → 不是「无主的亲人」
                prop[p].append((ci, dx))
        for p, cands in prop.items():
            size = {h: sum(1 for v in clan.values() if v == h) for _, h in cands}
            _, h = min(cands, key=lambda t: (t[0], -size.get(t[1], 0), t[1]))
            clan[p] = clan[h]
            r5 += 1

        # R6 势力代表兜底 —— 🔴 **已删除（2026-09-12 用户裁定）**
        #    原规则：「某势力该年存在、却没有任何家族挂它 → 取势力表里的代表（`Owner_<年>`）立为家头」。
        #    ⚠️ **它与 R1「谁有人侍奉谁就是一家之主」直接冲突**：这些势力的当主在日志里是**泛用 NPC**
        #    （「仙左卫门」「六郎次」「道闲」「甚五兵卫」「白云斋」，都不在英雄表），而势力表换的
        #    **实名替补**（海雷丁/高坂甚内/加藤段藏/猿飞佐助/金光文右卫门）在日志里全是
        #    **`立场=直臣` 且 `部下=0`** —— 是被别人侍奉的对象的家臣，**不是家头**。
        #    原规则把家臣提升成家头，造出 5 个假家族：`clan_hairedin_1` / `clan_kanemitsu_1` /
        #    `clan_katoo_1` / `clan_koosaka_1` / `clan_sarutobi_1`（实测 15 格，六年代分布）。
        #    **用户裁定：按日志为准，这 5 人不该有家。**
        #    代价（已知并接受）：安东水军 / 土佐水军 / 轩猿众 / 透波众 / 甲贺众 在这些年代
        #    **没有家族挂它**（骑砍侧无 RulingClan）——由 `check_taikou_world_tables.py` 的
        #    「武家在某年代存在、但该年没有任何家族挂它」警告常驻盯着，不静默。
        #    ⚠️ 别把这条兜底写回来：它解决的是引擎需求，代价是违反家族定义。
        r6 = 0
        era_clan[e] = clan
        era_head[e] = {pid for pid, h in clan.items() if h == pid}
        n_indep = 0
        for h in era_head[e]:
            r0 = rows.get(h) or {}
            fid = force_of(r0.get("组织", "无"), r0.get("势力", ""))
            if not fid or (fid, e) not in force_exists:
                n_indep += 1
        stat[e] = {
            "独立": n_indep,
            "家族": len(era_head[e]),
            "单人": sum(1 for h in era_head[e]
                        if sum(1 for v in clan.values() if v == h) == 1),
            "无家": sum(1 for pid in keep if clan.get(pid) is None),
            "最大": max((sum(1 for v in clan.values() if v == h) for h in era_head[e]), default=0),
            "R4": r4, "R5": r5, "R6": r6,
        }

    def ancestors(pid):
        """{祖先 pid: 代数}（沿父/母链，限深 12）"""
        out, stack = {}, [(pid, 0)]
        while stack:
            x, d = stack.pop()
            if d >= 12:
                continue
            for p in parents.get(x, []):
                if p not in out or out[p] > d + 1:
                    out[p] = d + 1
                    stack.append((p, d + 1))
        return out

    def blood_dist(a, b):
        """直系血缘代数（父=1、祖=2…）；无血缘 → None。用于「并入最近的那一家」"""
        if a == b:
            return 0
        aa, ab = ancestors(a), ancestors(b)
        best = aa.get(b)
        if b in aa:
            best = aa[b]
        if a in ab:
            best = ab[a] if best is None else min(best, ab[a])
        for x, da in aa.items():
            if x in ab:
                d = da + ab[x]
                if best is None or d < best:
                    best = d
        return best

    # ── 跨年代并家：同一家头 或 直系血缘；按血缘距离优先（近的先生效）──
    groups = []                 # [{"heads":set, "eras":set}]
    head2group = {}
    for e in ERAS:
        for h in sorted(era_head[e], key=int):
            if h in head2group:
                g = head2group[h]
                g["eras"].add(e)
            else:
                g = {"heads": {h}, "eras": {e}}
                groups.append(g)
                head2group[h] = g

    # 家头同姓（罗马音）才谈合并
    def rom_of(pid):
        return roman(by_dx[pid])

    while True:
        cands = []
        for i in range(len(groups)):
            gi = groups[i]
            for j in range(i + 1, len(groups)):
                gj = groups[j]
                # 🔴 同一年代两边都有家头 = 两家真实并存 → 永不合并
                #    （否则「伊达晴宗+伊达辉宗+伊达政宗」会被血缘链传递并成一家，
                #      两家共用一个 id → 一家两主的非法状态）
                shared = gi["eras"] & gj["eras"]
                if shared and any(a in era_head[e] and b in era_head[e]
                                  for e in shared for a in gi["heads"] for b in gj["heads"]):
                    continue
                best = None
                for a in gi["heads"]:
                    for b in gj["heads"]:
                        if rom_of(a) != rom_of(b):
                            continue
                        d = blood_dist(a, b)
                        if d is not None and (best is None or d < best):
                            best = d
                if best is not None:
                    cands.append((best, i, j))
        if not cands:
            break
        # 一轮内：按距离从近到远合并（远的留到下一轮重算，避免先到先得）
        cands.sort()
        dead = set()
        for _d, i, j in cands:
            if i in dead or j in dead:
                continue
            gi, gj = groups[i], groups[j]
            # 🔴 必须**重验**并存守卫：本轮的 i 可能刚吃掉别的组，
            #    新并入的家头会让「同代两家头」重新成立（cands 是吃掉之前算的）
            shared = gi["eras"] & gj["eras"]
            if shared and any(a in era_head[e] and b in era_head[e]
                              for e in shared for a in gi["heads"] for b in gj["heads"]):
                continue
            gi["heads"] |= gj["heads"]
            gi["eras"] |= gj["eras"]
            groups[j] = None
            dead.add(j)
        groups = [g for g in groups if g]

    # ── 苗字 + id 分配（苗字必须先定：id 由它派生）──
    def size_in(g, e):
        return sum(1 for h in g["heads"] if h in era_head[e]
                   for v in era_clan[e].values() if v == h)

    def head_at(g, e):
        return next((h for h in sorted(g["heads"], key=lambda x: int(x)) if h in era_head[e]), None)

    for g in groups:
        g["first_era"] = min(g["eras"])
        g["size0"] = size_in(g, g["first_era"])
        g["size"] = max(size_in(g, e) for e in g["eras"])
        # 苗字取「规模最大的那年」的家头（改名家以最终形态命名：长尾政景→上杉景胜 = 上杉家）
        peak = max(sorted(g["eras"]), key=lambda e: (size_in(g, e), -int(e)))
        g["head0"] = head_at(g, peak) or head_at(g, sorted(g["eras"])[0])
        g["zok"] = (by_dx[g["head0"]].get("FirstName") or by_dx[g["head0"]].get("CNName") or "").strip()
        # 罗马音 = 家头 EnglishName 首块；家头没英文名 → 查旧家族表存档的苗字罗马音（已标出处）
        g["rom"] = roman(by_dx[g["head0"]]) or zok2roman.get(g["zok"], "")
        if not g["rom"]:
            g["rom"] = "unknown%d" % int(g["head0"])
            rom_gap.append("%s（家头 %s）" % (g["zok"] or "?", by_dx[g["head0"]]["CNName"]))
        names = {((by_dx[h].get("FirstName") or by_dx[h].get("CNName") or "").strip())
                 for h in g["heads"]}
        if len(names) > 1:
            renames.append("%s：家头苗字历代不一（%s）→ 取名用最大那年（%s）"
                           % (g["zok"], "/".join(sorted(names)), g["zok"]))
        # ── 历代异名 → Alias 列（2026-09-12 用户裁定；口径同 TaikouForce.别名：`|` 分隔、不重复主名）──
        #    来源①：各年代家头的苗字（按年代先后；改名家如 长尾→上杉，主名取最大那年，异名就是长尾）
        #    来源②：旧织丰表的 OtherName（如 木下 → 羽柴|丰臣，这类靠首列名字推不出来，必须从旧表继承）
        alts, seen = [], {g["zok"]}
        for e in sorted(g["eras"]):
            h = head_at(g, e)
            if not h:
                continue
            nm = (by_dx[h].get("FirstName") or by_dx[h].get("CNName") or "").strip()
            if nm and nm not in seen:
                seen.add(nm)
                alts.append(nm)
        for nm in (zok2names.get(g["zok"]) or []):
            if nm and nm not in seen:
                seen.add(nm)
                alts.append(nm)
        g["alt_names"] = alts

    by_rom = collections.defaultdict(list)
    for g in groups:
        by_rom[g["rom"]].append(g)
    for rom, gs in by_rom.items():
        # 序号 = 先看首次出现年代 → 该年规模（老资格/大宗在前）→ 历代最大 → 家头编号
        gs.sort(key=lambda g: (g["first_era"], -g["size0"], -g["size"], min(int(h) for h in g["heads"])))
        for n, g in enumerate(gs, 1):
            g["id"] = "clan_%s_%d" % (rom, n)

    warns.extend(renames)
    # 家头 → 家 id
    head2id = {}
    for g in groups:
        for h in g["heads"]:
            head2id[h] = g["id"]

    # ── 文化（成员多数）──
    for g in groups:
        cult = collections.Counter()
        for e in sorted(g["eras"]):
            for pid, h in era_clan[e].items():
                if h in g["heads"]:
                    cult[by_dx[pid].get("CultureID", "")] += 1
        g["culture"] = sorted(cult.items(), key=lambda kv: (-kv[1], kv[0]))[0][0] if cult else ""

    # ── TaikouHero 六年代 clan 格 ──
    hero_clan = {dx: [(head2id.get(era_clan[e].get(dx)) or "") for e in ERAS] for dx in by_dx}

    # ── 据点 Clan_<年> ──
    sett_clan, sett_fix = {}, []
    for r in sett_rows:
        for e in ERAS:
            ow = (r.get("Owner_" + e) or "").strip()
            m = DXRE.match(ow) if ow else None
            cid = ""
            if m:
                cid = head2id.get(era_clan[e].get(m.group(1))) or ""
                if not cid and m.group(1) in keep:
                    # 城主该年无家（浪人当着城主，实机只 2 格：河野通直 1582）
                    # → 用他最近年代有家的那个家（保住「他的城还是他的」）
                    for e2 in sorted(ERAS, key=lambda x: abs(int(x) - int(e))):
                        c2 = head2id.get(era_clan[e2].get(m.group(1))) or ""
                        if c2:
                            cid = c2
                            sett_fix.append("%s %s：城主 %s 当年无家 → 沿用 %s 年的 %s"
                                            % (r.get("id"), e, by_dx[m.group(1)]["CNName"], e2, c2))
                            break
            sett_clan[(r.get("id"), e)] = cid
            if ow and not cid and not m:
                problems.append("据点 %s %s：Owner 不是 lord_tk5 编号（%s）" % (r.get("id"), e, ow))

    # ── 闭合校验 ──
    ids = [g["id"] for g in groups]
    if len(set(ids)) != len(ids):
        problems.append("家族 id 重复：%s" % [i for i, c in collections.Counter(ids).items() if c > 1])
    for g in groups:
        if not g["zok"]:
            problems.append("%s 没有苗字（家头无 FirstName/CNName）" % g["id"])
        for e in g["eras"]:
            h = next(x for x in g["heads"] if x in era_head[e])
            org = sup[e][h]["组织"]
            if org in ("无", "") and sup[e][h]["立场"] == "当主":
                problems.append("%s %s：家头 %s 没有组织（立场=当主）" % (g["id"], e, by_dx[h]["CNName"]))
            elif org not in ("无", "") and not force_of(org, sup[e][h]["势力"]):
                problems.append("%s %s：组织「%s」（%s）→ TaikouForce 无对应"
                                % (g["id"], e, org, sup[e][h]["势力"]))
    # 🔴 自证：一家在任一年代只能有一个家头（合并的硬不变量）
    for g in groups:
        for e in g["eras"]:
            who = [h for h in g["heads"] if h in era_head[e]]
            if len(who) != 1:
                problems.append("%s %s：这家有 %d 个家头（%s）—— 合并越权"
                                % (g["id"], e, len(who),
                                   ",".join(by_dx[h]["CNName"] for h in who)))

    # 势力 → 家 覆盖（数据缺口 → 警告，不是生成器 bug：见 wheels「生成器自检两级口径」）
    n_orphan = 0
    for f in force:
        if f.get("ID") == "ID" or f.get("势力类型") != "Warrior":
            continue
        for e in ERAS:
            if (f.get("Owner_" + e) or "-").strip() in ("", "-"):
                continue
            hit = any(e in g["eras"] and
                      force_of(sup[e][head_at(g, e)]["组织"], sup[e][head_at(g, e)]["势力"]) == f["ID"]
                      for g in groups)
            if not hit:
                n_orphan += 1
                warns.append("势力 %s（%s）%s 年标为存在，但英雄表里没人挂它 → 该年这个国没有家族"
                             % (f["势力名"], f["ID"], e))
    # 势力 Owner_<年> → 该家头当年的王国是否就是该势力（错位 = 数据缺口，警告）
    n_shift = 0
    for f in force:
        if f.get("ID") == "ID" or f.get("势力类型") != "Warrior":
            continue
        for e in ERAS:
            ow = (f.get("Owner_" + e) or "-").strip()
            m = DXRE.match(ow)
            if not m or m.group(1) not in keep:
                continue
            hid = head2id.get(era_clan[e].get(m.group(1)))
            if not hid:
                n_shift += 1
                warns.append("势力 %s %s 年当主 %s 当年无家（浪人）→ 王国没有可挂的家族"
                             % (f["势力名"], e, by_dx[m.group(1)]["CNName"]))
                continue
            g = next(x for x in groups if x["id"] == hid)
            if e in g["eras"]:
                h = head_at(g, e)
                if force_of(sup[e][h]["组织"], sup[e][h]["势力"]) != f["ID"]:
                    n_shift += 1
                    warns.append("势力 %s %s 年当主 %s 的家族挂的是 %s（不是本势力）"
                                 % (f["势力名"], e, by_dx[m.group(1)]["CNName"],
                                    force_of(sup[e][h]["组织"], sup[e][h]["势力"]) or "?"))
    return {
        "groups": groups, "stat": stat, "hero_clan": hero_clan, "sett_clan": sett_clan,
        "hero_cols": None, "sett_fix": sett_fix, "org2force": org2force, "warns": warns,
        "n_orphan": n_orphan, "n_shift": n_shift, "rom_gap": rom_gap,
        "force_exists": force_exists, "force_of": force_of, "out_id": OUT_ID,
        "gen_eras": GEN_ERAS,
        "id2key": {v: k for k, v in OUT_ID.items()},
        "by_dx": by_dx, "sup": sup, "era_clan": era_clan, "head2id": head2id,
    }, problems


def roman(r):
    en = (r.get("EnglishName") or "").strip()
    tok = en.split(" ")[0] if en else ""
    return ROMAN_CLEAN.sub("", tok).lower()


def render_clan(data):
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(CLAN_COLS)
    sup, org2force = data["sup"], data["org2force"]
    OUT_ID = data.get("out_id") or {}
    GEN_ERAS = data.get("gen_eras") or {}
    force_exists = data["force_exists"]
    force_of = data["force_of"]
    for g in sorted(data["groups"], key=lambda g: g["id"]):
        row = {"ID": g["id"], "Name": g["zok"], "Alias": "|".join(g.get("alt_names") or []),
               "Culture": g["culture"]}
        for e in ERAS:
            if e in g["eras"]:
                h = next(x for x in g["heads"] if x in data["era_clan"][e] and
                         data["era_clan"][e][x] == x)
                row["Owner_" + e] = OUT_ID.get(h, "lord_tk5_" + h)
                fid = force_of(sup[e][h]["组织"], sup[e][h]["势力"])
                row["Kingdom_" + e] = fid if (fid, e) in force_exists else "-"
            else:
                row["Owner_" + e] = row["Kingdom_" + e] = "-"
        w.writerow([row.get(c, "") for c in CLAN_COLS])
    return buf.getvalue()


def apply_hero(data, hero_cols):
    """TaikouHero.csv：ClanID 单列 → ClanID_<年> ×6（原位替换）"""
    with io.open(HERO, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    hdr = rows[0]
    if "ClanID" not in hdr:
        if all(c in hdr for c in HERO_CLAN_COLS):
            pos = hdr.index(HERO_CLAN_COLS[0])
            new_hdr = hdr
        else:
            raise SystemExit("[FATAL] TaikouHero.csv 既没有 ClanID 也没有 ClanID_<年>")
    else:
        pos = hdr.index("ClanID")
        new_hdr = hdr[:pos] + HERO_CLAN_COLS + hdr[pos + 1:]
    out = [new_hdr]
    for r in rows[1:]:
        if not r:
            continue
        r = list(r) + [""] * (len(hdr) - len(r))
        hid = (r[0] or "").strip()
        m = DXRE.match(hid)
        # 🔴 泛用 hero 的 ID 是名字式的（DXRE 匹配不到）→ 走 真ID→内部键 反查表
        key = m.group(1) if m else (data.get("id2key") or {}).get(hid)
        vals = data["hero_clan"].get(key) if key else None
        if vals is None:
            vals = [""] * len(ERAS)
        if "ClanID" in hdr:
            out.append(r[:pos] + vals + r[pos + 1:])
        else:
            out.append(r[:pos] + vals + r[pos + len(ERAS):])
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerows(out)
    return buf.getvalue(), hdr, new_hdr


def render_sett(data):
    with io.open(SETT, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    hdr = rows[0]
    idx = {c: hdr.index(c) for c in hdr if c.startswith("Clan_")}
    out = [hdr]
    n = 0
    for r in rows[1:]:
        if not r:
            continue
        r = list(r) + [""] * (len(hdr) - len(r))
        for e in ERAS:
            col = "Clan_" + e
            if col in idx:
                v = data["sett_clan"].get((r[0], e), "")
                if r[idx[col]] != v:
                    n += 1
                r[idx[col]] = v
        out.append(r)
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerows(out)
    return buf.getvalue(), n


def report(data, problems, extra):
    L = []
    A = L.append
    A("# 家族重建报告（按侍奉关系，2026-09-11 用户裁定）\n")
    A("> 生成物：`Scripts/gen_taikou_clan_csv.py`。规则见脚本头注释与 plan「零、0.2」。\n")
    A("## 逐年代规模\n")
    A("| 年代 | " + " | ".join(ERAS) + " |")
    A("|---|" + "---|" * len(ERAS))
    for k in ("家族", "独立", "单人", "无家", "最大", "R4", "R5", "R6"):
        lbl = {"家族": "家族数", "独立": "其中无王国的独立家族", "单人": "其中单人",
               "无家": "无家（浪人/无所属，骑砍侧当游荡者）",
               "最大": "最大族", "R4": "妻子并入", "R5": "亲人并入",
               "R6": "并入势力代表家"}[k]
        A("| %s | " % lbl + " | ".join(str(data["stat"][e][k]) for e in ERAS) + " |")
    A("")
    A("## 家族名单（%d 个，按 id）\n" % len(data["groups"]))
    A("| id | 苗字 | 文化 | " + " | ".join(ERAS) + " |")
    A("|---|---|---|" + "---|" * len(ERAS))
    for g in sorted(data["groups"], key=lambda g: g["id"]):
        cells_ = []
        for e in ERAS:
            if e in g["eras"]:
                h = next(x for x in g["heads"] if x in data["era_clan"][e] and
                         data["era_clan"][e][x] == x)
                cells_.append(data["by_dx"][h]["CNName"])
            else:
                cells_.append("-")
        A("| `%s` | %s | %s | " % (g["id"], g["zok"], g["culture"]) + " | ".join(cells_) + " |")
    A("")
    A("## 异常 / 例外\n")
    if extra:
        for x in extra:
            A("- %s" % x)
    A("")
    A("## 数据缺口警告（不是生成器 bug，见 wheels「生成器自检两级口径」）\n")
    A("- 势力标为存在、但英雄表里没人挂它（该年这个国没有家族）：**%d** 格"
      % data.get("n_orphan", 0))
    A("- 势力当主的家族挂不到本势力：**%d** 格" % data.get("n_shift", 0))
    if data.get("rom_gap"):
        A("- 家头没有英文名（苗字罗马音走旧表兜底）：%s" % "、".join(data["rom_gap"]))
    for x in data.get("warns", []):
        A("  - %s" % x)
    if problems:
        A("\n### 🔴 硬问题 %d 条\n" % len(problems))
        for p in problems:
            A("- %s" % p)
    else:
        A("\n无硬问题。")
    return "\n".join(L) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="写盘（默认只报告）")
    ap.add_argument("--check", action="store_true", help="只校验磁盘产物是否最新")
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（本检查不读模块）")
    args = ap.parse_args()

    for p in (SUP_LOG, HERO, FORCE, SETT):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2

    data, problems = build()
    if data is None:
        print("[FATAL] %s" % problems[0], file=sys.stderr)
        return 2

    print("家族重建：跨年代合计 %d 个家" % len(data["groups"]))
    print("  " + "  ".join("%s:%d家/无家%d" % (e, data["stat"][e]["家族"], data["stat"][e]["无家"])
                           for e in ERAS))
    print("  妻子并入 %s 人；亲人并入 %s 人"
          % (sum(data["stat"][e]["R4"] for e in ERAS), sum(data["stat"][e]["R5"] for e in ERAS)))
    print("  无王国的独立家族：%s"
          % "  ".join("%s:%d" % (e, data["stat"][e]["独立"]) for e in ERAS))

    clan_text = render_clan(data)
    hero_text, hero_hdr, hero_new = apply_hero(data, None)
    sett_text, sett_changed = render_sett(data)
    print("  据点 Clan_<年> 将改 %d 格" % sett_changed)
    for x in data["sett_fix"]:
        print("   · %s" % x)

    if problems:
        print("\n❌ 硬问题 %d 条：" % len(problems))
        for p in problems[:40]:
            print("   %s" % p)
        return 1

    if args.check:
        bad = []
        for path, text in ((CLAN, clan_text), (HERO, hero_text), (SETT, sett_text)):
            if not os.path.isfile(path):
                bad.append(os.path.basename(path) + " 不存在")
                continue
            cur = io.open(path, encoding="utf-8-sig", newline="").read()
            if cur.replace("\r\n", "\n") != text.replace("\r\n", "\n"):
                bad.append(os.path.basename(path) + " 与生成器不一致")
        if bad:
            print("\n[CHECK] ❌ " + "；".join(bad))
            return 1
        print("\n[CHECK] ✅ 三张表与生成器一致")
        return 0

    if not args.apply:
        print("\n（未加 --apply，只报告不写盘）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    if not os.path.isdir(ARCHIVE):
        os.makedirs(ARCHIVE)
    # 旧 Clan.csv（织丰口径）首次整表存档
    arch = os.path.join(ARCHIVE, "Clan_织丰口径_20260911.csv")
    if not os.path.isfile(arch) and os.path.isfile(CLAN):
        shutil.copy2(CLAN, arch)
        print("  旧 Clan.csv 已存档 → %s" % os.path.relpath(arch, REPO))
    # 🔴 现版 Clan.csv（19 列，织丰遗留列尚未裁）存档 —— 2026-09-12 列裁剪，留档供人回查
    arch19 = os.path.join(ARCHIVE, "Clan_19列_20260912.csv")
    if not os.path.isfile(arch19) and os.path.isfile(CLAN):
        shutil.copy2(CLAN, arch19)
        print("  现版 Clan.csv（19 列）已存档 → %s" % os.path.relpath(arch19, REPO))
    for p in (CLAN, HERO, SETT):
        if os.path.isfile(p):
            shutil.copy2(p, p + ".bak_clan_" + stamp)

    with io.open(CLAN, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(clan_text)
    with io.open(HERO, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(hero_text)
    with io.open(SETT, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(sett_text)
    print("✅ 已写出 Clan.csv（%d 行）/ TaikouHero.csv（ClanID_<年>×6）/ Settlements.csv"
          % len(data["groups"]))

    # ── 往返校验（写盘 == 读回；本次因缺此校验写坏过 960 格）──
    _, clan_back = load_dict(CLAN)
    _, hero_back = load_dict(HERO)
    errs = []
    if len(clan_back) != len(data["groups"]):
        errs.append("Clan.csv 行数 %d ≠ %d" % (len(clan_back), len(data["groups"])))
    for r in clan_back:
        if not all(r.get(c) for c in ("ID", "Name")):
            errs.append("Clan.csv %s 有空格" % r.get("ID"))
    if "ClanID" in hero_back[0]:
        errs.append("TaikouHero.csv 旧 ClanID 列没删干净")
    n_filled = sum(1 for r in hero_back if any(r.get(c) for c in HERO_CLAN_COLS))
    print("  往返校验：Clan.csv %d 行 / TaikouHero 有家族的 %d 行" % (len(clan_back), n_filled))
    if errs:
        print("❌ 往返校验失败：\n   " + "\n   ".join(errs))
        return 1

    rpt = os.path.join(os.path.dirname(CSV_DIR), "家族重建报告_20260911.md")
    with io.open(rpt, "w", encoding="utf-8") as fh:
        fh.write(report(data, problems, data["sett_fix"]))
    print("✅ 报告 → %s" % os.path.relpath(rpt, REPO))
    return 0


if __name__ == "__main__":
    sys.exit(main())

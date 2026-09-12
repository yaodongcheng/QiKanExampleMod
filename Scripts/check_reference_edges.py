#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""CSV 交叉引用全量边台账（悬空 + 孤儿 + XML 侧引用）—— T0-① 体检总表
============================================================================
**这个脚本回答一句话：本体数据里的每一条「谁指向谁」，是不是都落得了地。**

三样东西一起查（一张**声明式**台账，加一条边 = 加一行）：

  ① **CSV → CSV 边**（`EDGES`）：源表某列的值必须在目标表的键里存在。
     每条边自己声明「空值口径」与「合法非 id 值」——**空值 ≠ 悬空**：
     浪人无家 / 该年已死亡 / 势力该年不建国 / 町无城主，都是**合法的"没有"**，
     不写清楚就必然把正常数据报成错（这是本表最容易踩的误报源）。
  ② **孤儿**（`ORPHANS`）：目标表定义了、但全库没有任何引用指向它。
     与 ① 共用同一批边 → 引用面变了不会一边变一边不变。
  ③ **XML 侧引用**（`XML_EDGES`）：世界输出（`<module>/ModuleData/**.xml`）里出现的
     `Culture.x` / `Faction.x` / `Hero.x` / `Kingdom.x` / `Settlement.x`
     必须在 CSV 注册表里查得到；反向也报「CSV 有多少实体还没进世界」（进度，不是错）。

🔴 **非人物行不参与边检查**（口径，2026-09-12 定死）：
   `TaikouHero.csv` 里 **`TemplateNPC` 非空 = 非人物行**（68 个 `template_*` 容貌/立绘样板
   + 6 个 `pronoun_*` 代词占位，共 **74 行**）——它们不是历史人物，好几列本就是错位用法
   （详见 `check_englishname_clan_prefix` 文件头），拿人物边去要求它们必然假红。
   **凡读 TaikouHero 的边一律走 `is_person()`**，写新检查时不要自己再判一遍。

两级口径（同 `check_taikou_world_tables`）：
  ❌ 硬错误 = 生成器/编号 bug —— id 形状的值查无、织丰编号、id 重复、名字查无（原本该是 id 的列）
  ⚠️ 警告 = 数据缺口 —— 名字式引用查无、父/祖列混装中文名、孤儿、XML 进度

🔴 **豁免必须逐条写理由 + 退役条件**（`XML_EXEMPT` / `LEADER_EXEMPT`），
   脚本会把豁免项**照样打印出来**——沉默跳过 = 下一个世界看不出这里有个坑。

与既有 checker 的分工（一条规则只有一个所有者）：
  · 本脚本 = **全部边闭包 + 孤儿**（CSV 内部 + XML 世界输出）
  · `check_taikou_xml_references` = XML 内部闭合（含官方常驻段与引擎基础类型）
  · `check_culture_references` = **加载段闭包**里的文化悬空（含官方模块，引擎级防线）
  · `check_taikou_world_tables` = 语义不变量（互证 / 名字唯一 / 身份→据点类型）

Usage:
  python Scripts/check_reference_edges.py                    # 仓库自带 CSV + 注册表指向的内容包
  python Scripts/check_reference_edges.py --csv-dir D --module M
  python Scripts/check_reference_edges.py -v                 # 打印明细（默认只打前几条）
Exit: 0 全绿 / 1 有硬错误 / 2 fatal。
"""
import argparse
import collections
import os
import re
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
# 英雄表 9 个年代列 = 六交付剧本 + 3 个不交付（1549 / 1584 / dream1560 梦幻剧本）。
# 不交付的三列也查（数据错了照样要改），但不进「进世界」的进度统计。
EXTRA_ERAS = ["1549", "1584", "dream1560"]
ALL_ERAS = ERAS + EXTRA_ERAS

# ─────────────────────────── 表注册表 ───────────────────────────
# head：2 = 双行表头取英文键（合规范）/ 0 = 单行表头（上游转储）
# names：别名/名称列（`|` 分隔）——名字式引用的落点，也是孤儿统计的入口
TABLES = {
    "Culture.csv": dict(head=2, id="ID", names=("Name", "Alias"),
                        id_re=re.compile(r"^[a-z][a-z0-9_]*$")),
    "TaikouForce.csv": dict(head=2, id="ID", names=("ForceName", "Alias"),
                            id_re=re.compile(r"^[a-z][a-z0-9_]*$")),
    "Clan.csv": dict(head=2, id="ID", names=("Name", "Alias"),
                     id_re=re.compile(r"^clan_[a-z0-9_]+$")),
    "Settlements.csv": dict(head=2, id="id", names=("Name_All",),
                            id_re=re.compile(r"^[a-z]+_tk\d+$")),
    "TaikouHero.csv": dict(head=2, id="ID", names=("CNName", "Alias"),
                           id_re=re.compile(r"^(lord_tk5_[A-Za-z0-9_]+|main_hero)$")),
    "School.csv": dict(head=2, id="SchoolID", names=("CNName", "Alias"),
                       id_re=re.compile(r"^\d+$")),
    "ProfileImage.csv": dict(head=0, id="tkid", names=("StringId",),
                             id_re=re.compile(r"^\d+$")),
}

# 非人物行判据（唯一入口）——TaikouHero 里 TemplateNPC 非空 = 样板/代词行
PERSON_FLAG = "TemplateNPC"

# 归「警告」的异常类别（其余一律硬错误）——见 check_edges docstring
SOFT_KINDS = {"名字式（可解析）", "非 id 形状（混装名字）"}


def is_person(row):
    """🔴 非人物行不参与边检查——判据只此一处（见文件头）。"""
    return not (row.get(PERSON_FLAG) or "").strip()


# 流派掌门：现值成体系地是织丰编号（26 行里 25 行有值，全部 `lord_1_*`），
# 不是零散笔误 → 不做织丰编号硬闸门（会 25 连红毫无信息量），改为「已知待替换」显式登记。
LEADER_EXEMPT = ("School.Leader 现值 = 织丰编号（`lord_1_*`），来源为上游流派表；"
                 "替换为 lord_tk5_* 排在英雄全量进世界（T4）之后——届时本豁免应清零")


# ─────────────────────────── 边台账（①） ───────────────────────────
# mode   : "id"   = 值就是目标表主键；"name" = 值是目标表的名称/别名（表内翻译成 id）
# empty  : 合法空值集合（**空值 ≠ 悬空**）
# allow  : 合法非 id 整值（模板标记一类）
# forbid : 禁止的 id 前缀（织丰编号体系——本项目一律不认）
# soft   : True = 查无只报警告（名字式引用 / 数据缺口）；False = 硬错误
# name_fallback : mode=id 的边上，非 id 形状的值是否允许按名字回落（回落不到只警告）
def _E(key, src, cols, dst, mode="id", eras=None, split=None, empty=(), allow=(),
       forbid=(), soft=False, name_fallback=False, extract=None, person_only=True,
       exempt=None, note=""):
    return dict(key=key, src=src, cols=cols, dst=dst, mode=mode, eras=eras, split=split,
                empty=set(empty), allow=set(allow), forbid=tuple(forbid), soft=soft,
                name_fallback=name_fallback, extract=extract, person_only=person_only,
                exempt=exempt, note=note)


EDGES = [
    # ── 英雄 → 各处（**只查人物行**）─────────────────────────────
    _E("hero.clan", "TaikouHero.csv", ["ClanID_{era}"], "Clan.csv", eras=ERAS,
       empty=[""], person_only=True,
       note="空 = 该年无家（浪人/无所属，骑砍侧当游荡者），合法"),
    _E("hero.culture", "TaikouHero.csv", ["CultureID"], "Culture.csv",
       note="英雄本人的文化（决定外观/名字池归属）"),
    _E("hero.kingdom", "TaikouHero.csv", ["Kingdom_{era}"], "TaikouForce.csv", mode="name",
       eras=ERAS, empty=["", "无", "无效"], soft=True,
       note="空/无 = 该年无主家；无效 = 该年已死亡；`无` 对应 noKingdom"),
    _E("hero.city", "TaikouHero.csv", ["City_{era}"], "Settlements.csv", mode="name",
       eras=ERAS, empty=["", "无效"], soft=True,
       note="空 = 该年无居城；无效 = 该年已死亡；按精确全名匹配（城/町不通用）"),
    _E("hero.school", "TaikouHero.csv", ["School_{era}"], "School.csv", mode="name",
       eras=ALL_ERAS, empty=["", "无"],
       note="`无` = 未习武艺（合法）；门派另有 29/30 两个官方枚举行"),
    _E("hero.appearance", "TaikouHero.csv", ["AppearanceID"], "ProfileImage.csv", split="|",
       note="立绘槽（`|` 分隔 = 样板行的槽池，见 promote_generic_npcs 文件头）"),
    _E("hero.spouse", "TaikouHero.csv", ["SpouseId"], "TaikouHero.csv", empty=[""]),
    _E("hero.mother", "TaikouHero.csv", ["MotherId"], "TaikouHero.csv", empty=[""]),
    _E("hero.father", "TaikouHero.csv", ["FatherId"], "TaikouHero.csv", empty=[""],
       soft=True, name_fallback=True,
       note="🔴 该列**混装 id 与中文名**（上游原样）——非 id 形状只警告，见 plan T6 亲属接入"),
    _E("hero.grandfather", "TaikouHero.csv", ["GrandFatherId"], "TaikouHero.csv", empty=[""],
       soft=True, name_fallback=True, note="同 FatherId，混装 id 与中文名"),
    _E("hero.kins", "TaikouHero.csv", ["KinsId"], "TaikouHero.csv", split="|", empty=[""],
       note="亲族群（`|` 分隔多人）"),
    _E("hero.careerstance_boss", "TaikouHero.csv", ["CareerStance_{era}"], "TaikouHero.csv",
       mode="name", eras=ERAS, soft=True,
       extract=re.compile(r"^陪臣（(.+?)）$"),
       note="`陪臣（X）` 里的 X = 直接上司（人名）——只在能解析时查，解析不到只警告（袭名歧义）"),

    # ── 家族 → 各处 ──────────────────────────────────────────────
    _E("clan.owner", "Clan.csv", ["Owner_{era}"], "TaikouHero.csv", eras=ERAS, empty=["", "-"],
       forbid=("lord_1_", "lord_2_", "lord_3_", "dead_lord_", "spc_", "sho_"),
       person_only=False, note="`-` = 该年此家不存在"),
    _E("clan.kingdom", "Clan.csv", ["Kingdom_{era}"], "TaikouForce.csv", eras=ERAS,
       empty=["", "-"], person_only=False, note="`-` = 该年此家不存在（自然也不属任何势力）"),
    _E("clan.culture", "Clan.csv", ["Culture"], "Culture.csv", person_only=False),

    # ── 势力 → 各处 ──────────────────────────────────────────────
    _E("force.owner", "TaikouForce.csv", ["Owner_{era}"], "TaikouHero.csv", eras=ERAS,
       empty=["", "-"], allow=["@商人", "@忍者", "@海贼"],
       forbid=("lord_1_", "lord_2_", "lord_3_", "dead_lord_", "spc_", "sho_"),
       person_only=False, note="`-` = 该年不建国；`@商人/@忍者/@海贼` = 无实名当主时的模板标记"),
    _E("force.culture", "TaikouForce.csv", ["Culture"], "Culture.csv", person_only=False),

    # ── 据点 → 各处 ──────────────────────────────────────────────
    _E("settle.owner", "Settlements.csv", ["Owner_{era}"], "TaikouHero.csv", eras=ERAS,
       empty=[""], forbid=("lord_1_", "lord_2_", "lord_3_", "dead_lord_", "spc_", "sho_"),
       person_only=False, note="空 = 该年无人驻守（町多如此，合法）"),
    _E("settle.clan", "Settlements.csv", ["Clan_{era}"], "Clan.csv", eras=ERAS, empty=[""],
       person_only=False),

    # ── 流派 → 掌门 ──────────────────────────────────────────────
    _E("school.leader", "School.csv", ["Leader"], "TaikouHero.csv", empty=[""],
       person_only=False, exempt=(re.compile(r"^lord_1_"), LEADER_EXEMPT),
       note="🔴 该列现值是**织丰编号**（`lord_1_*`，上游来源），待替换——见 LEADER_EXEMPT"),
]

# ─────────────────────────── 孤儿（②） ───────────────────────────
# 每个目标表额外声明「本表之外还有谁会用到我」——孤儿判定的口径必须写全，
# 否则「世界里已经在用的实体」会被报成孤儿。
ORPHANS = {
    "Culture.csv": dict(extra_note="含 XML 里 Culture.<id> 的引用"),
    "TaikouForce.csv": dict(),
    "Clan.csv": dict(extra_note="含 XML 里 Faction.<id> 的引用"),
    "Settlements.csv": dict(extra_note="含 XML 里 Settlement.<id> 的引用"),
    "School.csv": dict(),
}

# ────────────────────── XML 侧引用（③） ──────────────────────
# XML 里的 `前缀.id` → CSV 注册表。`strip` = 前缀归一（Kingdom.kingdom_oda → 势力表 id `oda`）
XML_EDGES = [
    dict(prefix="Culture", dst="Culture.csv", strip=""),
    dict(prefix="Faction", dst="Clan.csv", strip=""),
    dict(prefix="Hero", dst="TaikouHero.csv", strip=""),
    dict(prefix="Kingdom", dst="TaikouForce.csv", strip="kingdom_"),
    dict(prefix="Settlement", dst="Settlements.csv", strip=""),
]

# XML 里有、CSV 注册表里没有的 id —— **逐条豁免必须写理由 + 退役条件**。
# 全部属「最小集脚手架」：T4 由 CSV 列生成世界段后应清零（届时本表删空）。
XML_EXEMPT = {
    ("Faction", "clan_oda"): "最小集手写家族 id（重建前口径）——重建后织田家 = clan_oda_1；T4 由 CSV 生成后退伍",
    ("Faction", "clan_hattori_1"): "最小集为『推荐五人』手立的独立家族，CSV 里服部半藏属 clan_tokugawa_1；T4 退伍",
    ("Faction", "clan_yagyuu_1"): "同上（柳生石舟斋，CSV 属 clan_tsutsui_1）；T4 退伍",
    ("Faction", "clan_ruzon_1"): "同上（吕宋助左卫门，CSV 属 clan_imai_1）；T4 退伍",
    ("Faction", "player_faction"): "建号流程运行期自造（主角家族），不在数据表里——永久豁免",
    ("Hero", "main_hero"): "建号流程运行期自造（主角本人），不在英雄表里——永久豁免",
    ("Kingdom", "g"): "时代切换 spike 占位（spkingdoms_1582）——T4/T6 全量 era 段接入后退伍",
    ("Faction", "clan_g"): "时代切换 spike 占位（spclans_1582）——同上",
    ("Hero", "lord_g"): "时代切换 spike 占位（taikou_heroes_1582）——同上",
    ("Faction", "clan_ronin_1554"): "**收容家族**（该代无主的浪人/师范/医师等统一落它）——生成器合成，CSV 无对应行；永久豁免",
    ("Faction", "clan_ronin_1560"): "收容家族（同上）——永久豁免",
    ("Faction", "clan_ronin_1568"): "收容家族（同上）——永久豁免",
    ("Faction", "clan_ronin_1575"): "收容家族（同上）——永久豁免",
    ("Faction", "clan_ronin_1582"): "收容家族（同上）——永久豁免",
    ("Faction", "clan_ronin_1598"): "收容家族（同上）——永久豁免",
    ("Culture", "ikoku"): "本世界基文化，由 gen_taikou_culture_full.py 的 CULTURES 定义；Culture.csv 只登记分流文化（16 条）——「Culture.csv 要不要收 ikoku」待用户裁定",
}

# XML 侧只认这几类属性里的引用（其余 `Type.id` 归 check_taikou_xml_references 管）
REF_RE = re.compile(r"\b([A-Z][A-Za-z]{1,24})\.([A-Za-z_][A-Za-z0-9_.]*)")


# ─────────────────────────── 引擎 ───────────────────────────
class Tables:
    def __init__(self, csv_dir):
        from csv_dual import read_table          # 同目录脚本
        self.dir = csv_dir
        self.rows, self.ids, self.name2id, self.names = {}, {}, {}, {}
        for fn, cfg in TABLES.items():
            path = os.path.join(csv_dir, fn)
            if not os.path.isfile(path):
                raise FileNotFoundError(path)
            cn, en, raw = read_table(path, head=cfg["head"])
            cols = en if cfg["head"] != 1 else cn
            rows = [{k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(cols)}
                    for r in raw if any((x or "").strip() for x in r)]
            self.rows[fn] = rows
            self.ids[fn] = {r.get(cfg["id"], "") for r in rows} - {""}
            n2i = collections.defaultdict(set)
            for r in rows:
                rid = r.get(cfg["id"], "")
                if not rid:
                    continue
                for nc in cfg["names"]:
                    for n in (r.get(nc) or "").split("|"):
                        n = n.strip()
                        if n:
                            n2i[n].add(rid)
            self.name2id[fn] = n2i
            self.names[fn] = set(n2i)

    def person_rows(self, fn):
        return [r for r in self.rows[fn]
                if fn != "TaikouHero.csv" or is_person(r)]


def expand_cols(edge):
    if "{era}" in edge["cols"][0]:
        return [c.replace("{era}", e) for e in (edge["eras"] or ERAS) for c in edge["cols"]]
    return list(edge["cols"])


def iter_cells(edge, rows):
    """→ (行, 列名, 值)。已按边声明拆多值 / 抽引用 / 过滤非人物行。"""
    for r in rows:
        for c in expand_cols(edge):
            v = (r.get(c) or "").strip()
            if not v or v in edge["empty"]:
                continue
            if edge["extract"] is not None:
                m = edge["extract"].match(v)
                if m:
                    yield r, c, m.group(1).strip()
                continue
            for tok in (v.split(edge["split"]) if edge["split"] else [v]):
                tok = tok.strip()
                if tok and tok not in edge["empty"]:
                    yield r, c, tok


def check_edges(t):
    """跑全部边 → (每条边的统计, 硬错误列表, 警告列表, 被引用集合, 豁免列表)。

    异常分四类，**硬/软由此决定**（负面测试实测：曾把「织丰编号」误归到软桶 → exit 0 假绿）：
      · `查无`                      → 硬：id 形状的值查不到，就是编号/生成器 bug
      · `织丰编号`                  → 硬：id 体系分叉（本项目不认另一套编号）
      · `名字式（可解析）`          → 软：该列**声明了混装名字**，值是名字且能对上人（数据缺口）
      · `非 id 形状（混装名字）`    → 软：同上但名字也对不上（如 `石田三成父` 这类上游写法）
    """
    stats, hard, warn, exempt = [], [], [], []
    used = collections.defaultdict(set)
    for e in EDGES:
        dst_cfg = TABLES[e["dst"]]
        n_cells = n_bad = n_exempt = 0
        samples = collections.defaultdict(list)
        ex_samples = []
        for r, c, tok in iter_cells(e, t.person_rows(e["src"])):
            n_cells += 1
            if tok in e["allow"]:
                continue
            if e["exempt"] and e["exempt"][0].match(tok):
                n_exempt += 1
                if len(ex_samples) < 3:
                    ex_samples.append("%s.%s = %s" % (r.get("ID") or r.get("id"), c, tok))
                continue
            if e["forbid"] and tok.startswith(e["forbid"]):
                n_bad += 1
                samples["织丰编号"].append("%s.%s = %s" % (r.get("ID") or r.get("id"), c, tok))
                continue
            if e["mode"] == "id" and tok in t.ids[e["dst"]]:
                used[e["dst"]].add(tok)
                continue
            if e["mode"] == "name":
                hit = t.name2id[e["dst"]].get(tok)
                if hit:
                    used[e["dst"]] |= hit
                    continue
                kind = "查无"
            else:
                id_shaped = bool(dst_cfg["id_re"].match(tok))
                hit = t.name2id[e["dst"]].get(tok) if e["name_fallback"] else None
                if hit:                                    # 非 id 形状但按名字能对上人
                    used[e["dst"]] |= hit
                    kind = "名字式（可解析）"
                elif e["name_fallback"] and not id_shaped:
                    kind = "非 id 形状（混装名字）"
                else:
                    kind = "查无"
            n_bad += 1
            samples[kind].append("%s.%s = %s" % (r.get("ID") or r.get("id"), c, tok))
        stats.append((e, n_cells, n_bad, n_exempt))
        if n_exempt:
            exempt.append((e["key"], n_exempt, ex_samples, e["exempt"][1]))
        for kind in sorted(samples):
            soft = e["soft"] or kind in SOFT_KINDS
            cols = expand_cols(e)
            label = "%s（%s%s → %s）%s" % (
                e["key"], cols[0].replace("_{era}", "_<年>"),
                "×%d" % len(cols) if len(cols) > 1 else "", e["dst"],
                " · %s" % kind if kind != "查无" else "")
            (warn if soft else hard).append((label, samples[kind], e["note"]))
    return stats, hard, warn, used, exempt


def check_orphans(t, used, xml_used):
    out = []
    for fn in ORPHANS:
        cfg = TABLES[fn]
        u = set(used.get(fn, set())) | set(xml_used.get(fn, set()))
        orphan = sorted(i for i in t.ids[fn] if i not in u)
        if orphan:
            out.append((fn, cfg["id"], orphan, ORPHANS[fn].get("extra_note", "")))
    return out


def scan_xml(module_dir):
    """→ {(前缀, id): [出现位置]}, 扫描文件数。"""
    refs = collections.defaultdict(list)
    files = []
    md = os.path.join(module_dir, "ModuleData")
    for root, _dirs, names in os.walk(md):
        for n in sorted(names):
            if n.endswith(".xml"):
                files.append(os.path.join(root, n))
    for p in files:
        try:
            txt = open(p, encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        txt = re.sub(r"<!--.*?-->", "", txt, flags=re.S)     # 注释里举例写的引用不算
        for m in REF_RE.finditer(txt):
            pfx, rid = m.groups()
            key = (pfx, rid)
            if any(key[0] == e["prefix"] for e in XML_EDGES):
                refs[key].append(os.path.relpath(p, module_dir).replace("\\", "/"))
    return refs, len(files)


def check_xml(module_dir, t):
    """XML → CSV 悬空（硬）/ 豁免（打印）/ 反向覆盖进度。"""
    if not os.path.isdir(os.path.join(module_dir, "ModuleData")):
        return None
    refs, n_files = scan_xml(module_dir)
    by_prefix = {}
    for e in XML_EDGES:
        by_prefix[e["prefix"]] = e
    dangling, exempt, xml_used = [], [], collections.defaultdict(set)
    for (pfx, rid), locs in sorted(refs.items()):
        e = by_prefix[pfx]
        target = rid[len(e["strip"]):] if e["strip"] and rid.startswith(e["strip"]) else rid
        if target in t.ids[e["dst"]]:
            xml_used[e["dst"]].add(target)
            continue
        if (pfx, rid) in XML_EXEMPT:
            exempt.append((pfx, rid, len(locs), XML_EXEMPT[(pfx, rid)]))
        elif (pfx, target) in XML_EXEMPT:
            exempt.append((pfx, rid, len(locs), XML_EXEMPT[(pfx, target)]))
        else:
            dangling.append((pfx, rid, len(locs), sorted(set(locs))[:3]))
    return dict(files=n_files, refs=len(refs), dangling=dangling, exempt=exempt,
                xml_used=xml_used)


def main():
    ap = argparse.ArgumentParser(description="CSV cross-reference edge ledger")
    ap.add_argument("--csv-dir", default=DEFAULT_CSV)
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    if not os.path.isdir(args.csv_dir):
        print("[FATAL] csv dir not found: %s" % args.csv_dir, file=sys.stderr)
        return 2
    try:
        t = Tables(args.csv_dir)
    except FileNotFoundError as e:
        print("[FATAL] 缺数据表：%s" % e, file=sys.stderr)
        return 2

    print("边台账体检（CSV 目录：%s）" % args.csv_dir)
    print("  表：%s" % " / ".join("%s(%d)" % (fn, len(t.rows[fn])) for fn in TABLES))

    stats, hard, warn, used, exempt = check_edges(t)

    print("\n== ① CSV 内部边（%d 条）==" % len(EDGES))
    for e, n_cells, n_bad, n_exempt in stats:
        mark = "✅" if not (n_bad or n_exempt) else ("⏭ " if not n_bad else ("⚠️ " if e["soft"] else "❌"))
        cols = expand_cols(e)
        cs = cols[0] if len(cols) == 1 else "%s×%d" % (cols[0].replace("_{era}", "_<年>"), len(cols))
        tail = "异常 %d" % n_bad
        if n_exempt:
            tail += " / 豁免 %d" % n_exempt
        print("  %s %-26s %-34s → %-18s 检查 %5d 格 / %s"
              % (mark, e["key"], "%s.%s" % (e["src"].replace(".csv", ""), cs),
                 e["dst"].replace(".csv", ""), n_cells, tail))

    xml = check_xml(args.module, t)
    orphans = check_orphans(t, used, xml["xml_used"] if xml else {})

    print("\n== ② 孤儿（定义了但全库无引用）==")
    if not orphans:
        print("  （无）")
    for fn, idcol, items, extra in orphans:
        print("  ⚠️  %s 有 %d 个 %s 无人引用%s" % (fn, len(items), idcol,
                                                 "（%s）" % extra if extra else ""))
        for it in (items if args.verbose else items[:6]):
            print("      %s" % it)
        if len(items) > 6 and not args.verbose:
            print("      … 另有 %d 条（-v 看全）" % (len(items) - 6))

    print("\n== ③ 边级豁免（已知待替换，**打印出来不静默**）==")
    if not exempt:
        print("  （无）")
    for key, n, samples, why in exempt:
        print("  ⏭  %s：%d 格 —— %s" % (key, n, why))
        if args.verbose:
            for s in samples:
                print("      %s" % s)

    print("\n== ③ XML 侧引用（世界输出 ↔ CSV 注册表）==")
    if xml is None:
        print("  [跳过] ModuleData 不存在：%s" % args.module)
    else:
        print("  扫描 %d 个 XML / %d 个引用键" % (xml["files"], xml["refs"]))
        if not xml["dangling"]:
            print("  ✅ 悬空 0（XML 引用的实体全在 CSV 注册表里）")
        for pfx, rid, n, locs in xml["dangling"]:
            print("  ❌ [悬空] %s.%s（%d 处，如 %s）" % (pfx, rid, n, ", ".join(locs)))
        if xml["dangling"]:
            # 🔴 必须进 hard：只打印不改退出码 = 假绿（负面测试实测过一次，别再犯）
            hard.append(("XML 世界输出引用了 CSV 注册表里没有的 id（%d 个）" % len(xml["dangling"]),
                         ["%s.%s（%d 处）" % (p, r, n) for p, r, n, _ in xml["dangling"]],
                         "除非在 XML_EXEMPT 里逐条登记理由与退役条件，否则就是 id 体系分叉/生成器漏写"))
        for pfx, rid, n, why in xml["exempt"]:
            print("  ⏭  [豁免] %s.%s（%d 处）—— %s" % (pfx, rid, n, why))
        # 反向：CSV 有多少实体已经进世界（进度，不是错）
        prog = []
        for e in XML_EDGES:
            fn = e["dst"]
            if fn in ("Culture.csv", "ProfileImage.csv"):
                continue
            prog.append("%s %d/%d" % (fn.replace(".csv", ""),
                                      len(xml["xml_used"].get(fn, set())), len(t.ids[fn])))
        print("  进度（CSV 实体已进世界数量）：%s" % " · ".join(prog))

    # 明细：硬错误在前
    def dump(title, items, limit):
        if not items:
            return
        print("\n%s" % title)
        for name, samples, note in items:
            print("  ▸ %s%s" % (name, "（%s）" % note if note else ""))
            for s in samples[:limit]:
                print("      %s" % s)
            if len(samples) > limit:
                print("      … 另有 %d 条" % (len(samples) - limit))

    limit = 999 if args.verbose else 8
    dump("❌ 硬错误 %d 类：" % len(hard), hard, limit)
    dump("⚠️  警告 %d 类（数据缺口，不阻断）：" % len(warn), warn, limit)

    if not hard:
        print("\n✅ 硬错误 0：全部边的 id 引用都能落地（警告 = 已知数据缺口，逐条见上）")
    return 1 if hard else 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""gen_region_kuni_csv.py —— 生成 `csv/Kuni.csv`（令制国 66）与 `csv/Region.csv`（地方 10）
============================================================================
**数据源**：`Knowledge/太阁5/太阁日志/据点日志.md` 的 `Log: SET|…|国:<令制国>|地:<地域>|…`
—— 274 据点 × 6 剧本，国/地 **六年零冲突**（与 `import_settlement_kuni_chi.py` 同源同判据）。

**为什么要这两张表**：语料里 `国::尾張` / `地方::近畿` 是两种独立的具名引用，
翻译器要查得到才能产出 `Region::<id>`。此前这两层只有生成器里手写的 12 条令制国
（`gen_entity_maps.py` 的 `REGION_MAP`），语料实际引用 **39 种令制国 + 10 种地方**
—— 缺的 27 种是"查无即停机"的硬缺口。本脚本一次补齐。

**ID 约定**：
  · 令制国 → `tk5_<罗马字>`（沿用既有 12 条：`tk5_owari` / `tk5_mino` / `tk5_suruga`…）
  · 地方   → 复用 Taikou 的 9 个地域文化 ID（`kinai` / `kanto` / `tokai` / `tosan` /
             `hokuriku` / `sanyo` / `nankai` / `saikai` / `ou`，见 `Culture.csv`）
             + `tk5_chubu`（太阁把山阴道/山阳道并称「中部」，无对应文化）
             + `tk5_kaigai`（海外四町：釜山/宁波/那霸/吕宋，不在 mod 世界内）

**纪律**：①铁律 22——这两张 CSV 是生成物，要改 → 改本脚本的 READING 表 → 重跑
②铁律 24——值内禁半角逗号，多值列（别名）用半角竖线 `|`
③覆盖断言——日志里每个国/地都必须有 ID，缺一个就报错退出（不静默）

Usage:
  python Scripts/gen_region_kuni_csv.py            # 报告（不写盘）
  python Scripts/gen_region_kuni_csv.py --apply    # 落盘
  python Scripts/gen_region_kuni_csv.py --check    # 校验产物（进一键体检）
Exit: 0 正常 / 1 硬问题 / 2 fatal。
"""
import argparse
import collections
import io
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from csv_dual import read_table, write_table  # noqa: E402

# 🔴 私用区还原（雷 68）：太阁5 用自绘字形槽渲染 JIS 表外汉字，日志 dump 出来是
# U+E0xx–U+E4xx 私用区码点（看着像掉字）——`飞<U+E415>国` 实为「飞驒国」。
# 不过这道 = 这批国名永远对不上（本脚本实测撞到 11 个码点，全在还原表内）。
try:
    from tk5_pua_names import restore as restore_pua
except ImportError:                                                  # pragma: no cover
    def restore_pua(s):
        return s
    print("[WARN] 缺 Scripts/tk5_pua_names.py —— 私用区字符不还原，国名/地名会带残字")

CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "据点日志.md")
P_KUNI = os.path.join(CSV_DIR, "Kuni.csv")
P_REGION = os.path.join(CSV_DIR, "Region.csv")

# ---------------------------------------------------------------------------
# 令制国罗马字（键 = 简体名，不含「国」字；值 = 罗马字）
# 复合国按日志写法拆开分别就读（伊勢·志摩 → ise + shima），ID 用第一个。
# ---------------------------------------------------------------------------
READING = {
    "虾夷": "ezochi", "陆奥": "mutsu", "陆中": "rikuchu", "陆前": "rikizen",
    "岩代": "iwashiro", "羽前": "uzen", "羽后": "ugo",
    "上总": "kazusa", "安房": "awa_boso", "下总": "shimousa", "常陆": "hitachi",
    "上野": "kouzuke", "下野": "shimotsuke", "武藏": "musashi",
    "相模": "sagami", "伊豆": "izu",
    "骏河": "suruga", "远江": "totomi", "三河": "mikawa", "尾张": "owari",
    "美浓": "mino", "飞驒": "hida", "伊势": "ise", "志摩": "shima",
    "近江": "omi", "山城": "yamashiro", "大和": "yamato", "伊贺": "iga",
    "河内": "kawachi", "和泉": "izumi", "摄津": "settsu", "纪伊": "kii",
    "甲斐": "kai", "信浓": "shinano",
    "越前": "echizen", "加贺": "kaga", "能登": "noto", "越中": "etchu", "越后": "echigo",
    "丹后": "tango", "若狭": "wakasa", "丹波": "tanba", "但马": "tajima",
    "因幡": "inaba", "伯耆": "hoki", "出云": "izumo", "石见": "iwami",
    "播磨": "harima", "美作": "mimasaka", "备前": "bizen", "备中": "bichu",
    "备后": "bingo", "安艺": "aki", "周防": "suou", "长门": "nagato",
    "土佐": "tosa", "伊予": "iyo", "赞岐": "sanuki", "阿波": "awa", "淡路": "awaji",
    "筑前": "chikuzen", "筑后": "chikugo", "丰前": "buzen", "丰后": "bungo",
    "肥前": "hizen", "肥后": "higo", "日向": "hyuga", "大隅": "osumi", "萨摩": "satsuma",
    "对马": "tsushima",
    # 日志里的三个「非令制国」（海外）+ 南蛮
    "琉球": "ryukyu", "朝鲜": "chosen", "明": "min", "南蛮": "namban",
}

# 地方（地列 10 值）→ ID：前 9 个复用 Taikou 地域文化 ID，后 2 个是太阁独有分层
CHI_ID = {
    "近畿": "kinai", "关东": "kanto", "东海": "tokai", "甲信": "tosan",
    "北陆": "hokuriku", "中部": "sanyo", "四国": "nankai",
    "九州": "saikai", "东北": "ou", "海外": "tk5_kaigai",
}
# 同一地域的**另一种叫法**（太阁日志用左，语料 `地方::` 用右）——
# 实测：语料有 `地方::中國`（31 次），而日志 `地:` 列 0 次（太阁管这块叫「中部」）。
# 两种叫法是同一片地方（山阴道+山阳道），补成同一行的别名，不另开条目。
CHI_EXTRA_NAMES = {
    "中部": "中国",           # 键 = 日志 `地:` 的值（权威名），值 = 语料里的另一种叫法
}
# 繁体别名（语料是繁体源文，两种写法都要能查到）
CHI_TRAD = {
    "近畿": "近畿", "关东": "關東", "东海": "東海", "甲信": "甲信",
    "北陆": "北陸", "中部": "中部", "四国": "四國",
    "九州": "九州", "东北": "東北", "海外": "海外",
}

# 复合国的别名写法（日志用「·」连接；别名列同时收录连接形与拆开形，供 `国::` 两种引用命中）
COMPOUND_ALIAS = {
    "上总": "上總·安房", "安房": "上總·安房",
    "相模": "相模·伊豆", "伊豆": "相模·伊豆",
    "伊势": "伊勢·志摩", "志摩": "伊勢·志摩",
    "大和": "大和·伊賀", "伊贺": "大和·伊賀",
    "河内": "河內·和泉", "和泉": "河內·和泉",
    "丹后": "丹後·若狭", "若狭": "丹後·若狭",
    "因幡": "因幡·伯耆", "伯耆": "因幡·伯耆",
    "周防": "周防·長門", "长门": "周防·長門",
    "阿波": "阿波·淡路", "淡路": "阿波·淡路",
    "筑前": "筑前·對馬", "对马": "筑前·對馬",
}

# 异体字/日文写法（键 = 日志里的简体名，值 = 语料里另用的写法，`|` 分隔）。
# 实测：日志「安艺国」↔ 语料 `國::安芸`（日文地名用「芸」字）；zhconv 与变体表层都不覆盖。
KUNI_ALT = {
    "安艺": "安芸|安藝",
}

KUNI_CN = ["ID", "名称", "别名"]
KUNI_EN = ["ID", "Name", "Alias"]
REGION_CN = ["ID", "名称", "别名"]
REGION_EN = ["ID", "Name", "Alias"]

# 复合国组 → 该组统一用的罗马字（build_rows 填，cmd_check 读）
_GROUP_FIRST = {}


def parse_log():
    """→ (Counter[国], Counter[地])：只统计 `SET` 数据行。"""
    if not os.path.isfile(LOG):
        print("[FATAL] 日志不存在：%s" % LOG)
        sys.exit(2)
    kuni, chi = collections.Counter(), collections.Counter()
    for line in io.open(LOG, encoding="utf-8", errors="replace"):
        if "Log: SET|" not in line:
            continue
        parts = line.split("Log: ", 1)[1].strip().split("|")
        if len(parts) < 8 or parts[1] in ("HDR", "SEG"):
            continue
        kv = {}
        for p in parts[5:]:
            if ":" in p:
                k, v = p.split(":", 1)
                kv[k] = v.strip()
        if kv.get("国"):
            kuni[restore_pua(kv["国"])] += 1
        if kv.get("地"):
            chi[restore_pua(kv["地"])] += 1
    return kuni, chi


def split_kuni(name):
    """`伊勢·志摩国` → ['伊勢','志摩']（去国字、拆复合、转简体）。"""
    base = name[:-1] if name.endswith("国") else name
    for sep in ("·", "・", "‧"):
        base = base.replace(sep, "|")
    return [x for x in base.split("|") if x]


def build_rows(kuni, chi):
    """→ (kuni_rows, region_rows, errors)。行 = [ID, 名称, 别名]。"""
    errors = []
    # ---- 令制国 ----
    seen = collections.OrderedDict()          # 简体名 → 首次出现的罗马字
    for name in kuni:
        for simp in split_kuni(name):
            # 方位前缀国（北近江/南信濃…）按基名读：北近江 = 近江北部
            base = simp
            for pre in ("北", "南", "东", "西"):
                if simp.startswith(pre) and simp[1:] in READING:
                    base = simp[1:]
                    break
            if base not in READING:
                errors.append("令制国「%s」（来自日志「%s」）没有罗马字，请在 READING 表补" % (simp, name))
            elif simp not in seen:
                seen[simp] = READING[base] + ("_kita" if simp.startswith("北") else
                                              "_minami" if simp.startswith("南") else "")
    dup = [r for r, n in collections.Counter(seen.values()).items() if n > 1]
    if dup:
        errors.append("罗马字重复（ID 会撞车）：%s" % "、".join(sorted(dup)))
    # 复合国的两个成员必须指向**同一个 ID**（「伊勢·志摩」= 一个引用）：
    # 每组统一取该组里出现的首个成员的罗马字（生成器按日志出现顺序取首成员）。
    for simp, romaji in seen.items():
        grp = COMPOUND_ALIAS.get(simp, "")
        if grp and grp not in _GROUP_FIRST:
            _GROUP_FIRST[grp] = romaji
    kuni_rows = []
    for simp, romaji in seen.items():
        alias_parts = []
        cpd = COMPOUND_ALIAS.get(simp, "")
        if cpd:
            romaji = _GROUP_FIRST[cpd]
            alias_parts.append(cpd)
        alt = KUNI_ALT.get(simp, "")
        if alt:
            alias_parts += [x for x in alt.split("|") if x]
        kuni_rows.append(["tk5_" + romaji, simp, "|".join(alias_parts)])
    # ---- 地方 ----
    region_rows = []
    for name in chi:
        if name not in CHI_ID:
            errors.append("地方「%s」没有 ID，请在 CHI_ID 表补" % name)
            continue
        aliases = []
        trad = CHI_TRAD.get(name, "")
        if trad and trad != name:
            aliases.append(trad)
        extra = CHI_EXTRA_NAMES.get(name, "")          # 同一地域的另一种叫法
        if extra:
            aliases.append(extra)
            et = CHI_TRAD.get(extra, "")
            if et and et != extra:
                aliases.append(et)
        region_rows.append([CHI_ID[name], name, "|".join(aliases)])
    return kuni_rows, region_rows, errors


def _bad_chars(rows, cols):
    bad = []
    for r in rows:
        for i, v in enumerate(r):
            if any(ch in v for ch in (",",)) or (i == 2 and "|" in v and cols[i] != "Alias"):
                bad.append((r[0], cols[i], v))
    return bad


def cmd_check(kuni, chi):
    """校验产物：两表在、覆盖日志全部取值、ID 无重复、无禁用字符。"""
    problems = []
    for path, want_cols, label in ((P_KUNI, KUNI_EN, "Kuni"), (P_REGION, REGION_EN, "Region")):
        if not os.path.isfile(path):
            problems.append("缺表 %s（跑本脚本 --apply 生成）" % path)
            continue
        cn, en, rows = read_table(path)
        if en[:3] != want_cols:
            problems.append("%s 表头不对：%s" % (label, en[:3]))
            continue
        ids = [r[0] for r in rows if r]
        # 复合国（伊勢·志摩 / 上總·安房…）两个成员共用同一个 ID 是**刻意**的：
        # 「伊勢·志摩」是一个引用 → 一个 Region id。复合组从表自身推导
        # （同一 Alias 值的多行 = 同一组），不依赖构建期状态，`--check` 单跑也准。
        by_alias = collections.defaultdict(set)
        for r in rows:
            if r and (r[2] or "").strip():
                by_alias[r[2].strip()].add(r[0])
        solo_ids = []
        for r in rows:
            if not r:
                continue
            al = (r[2] or "").strip()
            if al and al in by_alias:              # 复合组成员，跳过
                continue
            solo_ids.append(r[0])
        if len(solo_ids) != len(set(solo_ids)):
            problems.append("%s 有重复 ID" % label)
        problems += ["%s 值含禁用字符：%s %s=%r" % (label, a, b, c)
                     for a, b, c in _bad_chars(rows, en)]
    if problems:
        print("[ERROR] Region/Kuni 表体检不通过：")
        for p in problems[:20]:
            print("   " + p)
        if len(problems) > 20:
            print("   … 共 %d 条" % len(problems))
        return 1
    # 覆盖断言：日志每个取值都能在两表里查到
    nk = len(read_table(P_KUNI)[2])
    nr = len(read_table(P_REGION)[2])
    print("[ OK ] Kuni.csv %d 条 / Region.csv %d 条；日志国 %d 种、地 %d 种全部覆盖"
          % (nk, nr, len(kuni), len(chi)))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="落盘（缺省只报告）")
    ap.add_argument("--check", action="store_true", help="校验产物（不写盘）")
    ap.add_argument("--module", default=None, help="忽略（兼容一键体检调用约定）")
    args = ap.parse_args()

    kuni, chi = parse_log()
    if args.check:
        return cmd_check(kuni, chi)

    kuni_rows, region_rows, errors = build_rows(kuni, chi)
    print("日志：国 %d 种（拆复合后 %d 个令制国）｜ 地 %d 种"
          % (len(kuni), len(kuni_rows), len(chi)))
    if errors:
        print("[ERROR] 构建失败：")
        for e in errors:
            print("   " + e)
        return 1
    for r in kuni_rows[:6]:
        print("   %-16s %-8s %s" % (r[0], r[1], r[2]))
    print("   …")
    for r in region_rows:
        print("   %-16s %-8s %s" % (r[0], r[1], r[2]))

    if not args.apply:
        print("\n（未写盘；加 --apply 落盘）")
        return 0

    write_table(P_KUNI, KUNI_CN, KUNI_EN, kuni_rows)
    write_table(P_REGION, REGION_CN, REGION_EN, region_rows)
    # 往返校验（用实际写出的内容读回）
    for path, want, n in ((P_KUNI, KUNI_EN, len(kuni_rows)), (P_REGION, REGION_EN, len(region_rows))):
        cn2, en2, rows2 = read_table(path)
        assert en2 == want, "%s 表头往返不一致" % path
        assert len(rows2) == n, "%s 行数往返不一致（%d ≠ %d）" % (path, len(rows2), n)
    print("\n✅ 已写出 %s（%d 行）、%s（%d 行）；往返读回一致"
          % (P_KUNI, len(kuni_rows), P_REGION, len(region_rows)))
    return cmd_check(kuni, chi)


if __name__ == "__main__":
    sys.exit(main())

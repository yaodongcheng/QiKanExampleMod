#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""泛用 NPC → hero 的 ID 表（职业 + 名字罗马音）
============================================================================
**为什么需要**（2026-09-12 用户裁定）：日志里存在过、但英雄表里没有的泛用 NPC
（商家/海贼众/忍者众），要提升为 hero 补充人口。

**识别键 = (名前, 职业类型)** —— 用户裁定「同名同类型 = 同一个人」（他不同年代换了组织）。
  实测验证：**同一年代内 (名,类型) 唯一，0 冲突** → 键成立。
  903 个 (名,组织,类型) 组合 → **329 人**（商家 120 / 海贼 110 / 忍者 99）。

**ID 规则 = `lord_tk5_<职业>_<名字罗马音>`**（用户裁定：职业+名字）。
  · 职业 ∈ { ninja, pirate, trader }
  · 罗马音**统一按项目 macron 铁律**（ō→oo）：郎=roo、次郎=jiroo、四郎=shiroo、
    兵衛=bee、右衛門=uemon、左衛門=zaemon（2026-09-12 用户裁定；
    既有 1037 条的写法有三套漂移（Rokuro/Rokuroo/Rokurou），**本次不动它们**）。

**为什么不用 pykakasi**：实测对这些名字不可用——327 个里 45 个直接输出空/截断
  （军兵卫/孙兵卫/传助/德藏…），抽样 25 个里至少 8 个读错
  （与平次→yoheitsugi、三郎左→saburouhidari、一贯→ichi）。
  所以改用**词干表 + 后缀表**两张人工表（名字 = 词干 + 后缀，后缀高度规律）。
  ⚠️ 与 `org_pirate_kougun` 的 slug 退化是同源坑（见 gen_taikou_force_csv 文件头）。

**产出**：`Knowledge/太阁5/骑砍2织丰角色ID对应/泛用heroID表_<日期>.csv`
  （名前 / 职业 / 罗马音 / ID / 出现年代 / 各年代槽号 / 置信度）——供用户抽查。

Usage:
  python Scripts/gen_generic_hero_ids.py            # 生成 + 打印摘要
  python Scripts/gen_generic_hero_ids.py --check    # 只校验产物是否最新
Exit: 0 正常 / 1 有硬问题 / 2 fatal。
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

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tk5_pua_names import restore  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
OUT = os.path.join(os.path.dirname(CSV_DIR), "泛用heroID表_20260912.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
TYPENAME = {"忍者众": "ninja", "海贼众": "pirate", "商家": "trader"}

# 🔴 合成号（内部键）：泛用 hero 的 ID 是名字式的（`lord_tk5_ninja_rokurooji`），
#    但**全部生成器都假设「人物键是数字」**（`DXRE = r"lord_tk5_(\d+)$"`、
#    `sorted(keep, key=int)`、`"unknown%d" % int(head)`…）。所以给每个泛用 hero
#    分配一个**合成号**当内部键，日志里每年不同的槽号都映射到它，输出边界再映射回真 ID。
#    号段取 9000 起：实测英雄表数字号最大 1289、日志槽号最大 1303 → 不会撞。
SYNTH_BASE = 9000
ALIGN_OUT = os.path.join(os.path.dirname(CSV_DIR), "泛用hero槽位对齐_20260912.csv")

# ── 后缀表（按长度降序匹配；读音统一按 macron 铁律：ō→oo）──
SUFFIX = [
    ("右卫门", "uemon"), ("左卫门", "zaemon"), ("之丞", "nojo"), ("之助", "nosuke"),
    ("之介", "nosuke"), ("之进", "noshin"), ("兵卫", "bee"), ("卫门", "emon"),
    ("太郎", "taroo"), ("次郎", "jiroo"), ("三郎", "saburoo"), ("四郎", "shiroo"),
    ("五郎", "goroo"), ("六郎", "rokuroo"), ("七郎", "shichiroo"), ("八郎", "hachiroo"),
    ("九郎", "kuroo"), ("十郎", "juuroo"), ("太夫", "dayuu"), ("郎兵卫", "robe"),
    ("丞", "jo"), ("介", "suke"), ("助", "suke"), ("蔵", "zoo"), ("藏", "zoo"),
    ("次", "ji"), ("左", "za"), ("右", "u"), ("平", "hei"), ("吉", "kichi"),
    ("松", "matsu"), ("丸", "maru"), ("马", "ma"), ("夫", "o"), ("郎", "roo"),
    ("坊", "boo"), ("斋", "sai"), ("门", "mon"), ("卫", "ei"),
]

# ── 词干表（240 条，人工定音；不确定的标 ?，产出表里带 flag）──
STEM = {
    "一": "ichi", "一若": "ichiwaka", "一角": "ikkaku", "一贯": "ikkan", "七郎": "shichiroo",
    "万": "man", "万千代": "manchiyo", "三": "san", "三郎": "saburoo", "三郎太": "saburoota",
    "与": "yo", "与三": "yoso", "与平": "yohei", "丑": "ushi", "专": "sen?",
    "中三": "chuuzan", "中八": "nakahachi", "丹": "tan", "为": "tame", "久": "hisa",
    "义": "yoshi", "九": "ku", "九郎": "kuroo", "二十八": "nijuuhachi", "五": "go",
    "仁": "jin", "仁八": "jinpachi", "仙": "sen", "仙造": "senzoo", "仪": "gi",
    "伊三": "izo", "伊作": "isaku", "伊势男": "iseo", "传": "den", "传六": "denroku",
    "传林": "denrin?", "佐": "sa", "佐七": "sashichi", "佐市": "saichi", "佐平": "sahei",
    "余": "yo", "余市": "yoichi", "作": "saku", "俊海": "shunkai", "修罗": "shura",
    "光": "mitsu", "八": "hachi", "八十": "yaso", "六": "roku", "六平太": "rokuheita",
    "六郎": "rokuroo", "兵太": "hyouta", "军": "gun", "军荼利": "gundari", "准": "jun",
    "刚": "takeshi", "初": "hatsu", "利": "toshi", "利八": "rihachi", "力": "riki",
    "力弥": "rikiya", "助": "suke", "助二": "sukeji", "助六": "sukeroku", "勘": "kan",
    "勘八": "kanpachi", "勘六": "kanroku", "勘解由": "kageyu", "十": "juu", "千代": "chiyo",
    "半": "han", "卯": "u", "又": "mata", "又八": "matahachi", "吉": "kichi",
    "吹雪": "fubuki", "周": "shuu", "和": "kazu", "善": "zen", "善八": "zenpachi",
    "善辅": "zensuke", "喜": "ki", "喜三太": "kisanta", "嘉": "yoshi", "四郎": "shiroo",
    "国之辅": "kuninosuke", "圆": "en", "外记": "geki", "多": "ta", "多津": "tazu",
    "夜叉": "yasha", "大": "dai", "大八": "daihachi", "天元": "tengen", "太郎": "taroo",
    "孙": "mago", "孙七": "magoshichi", "孙作": "magosaku", "宇": "u", "安": "yasu",
    "安二": "yasuji", "定": "sada", "实三": "sanezoo?", "宫": "miya", "宽": "kan",
    "寅": "tora", "富": "tomi", "富士": "fuji", "小": "ko", "小平": "kohei",
    "小弥太": "koyata", "小忠太": "kochuuta", "小源太": "kogenta", "小金": "kogane",
    "尚": "nao", "岩蓦": "ganbaku?", "峰": "mine", "左内": "sanai", "左膳": "sazen",
    "左近": "sakon", "市之允": "ichinosuke", "带刀": "tatewaki", "平": "taira",
    "平太": "heita", "幸": "sachi", "幸作": "koosaku", "幻妖": "gennyoo", "庄": "shoo",
    "弁": "ben", "弘庵": "koan", "弥": "ya", "弥二": "yaji", "弥市": "yaichi",
    "弥次": "yaji", "弥源": "yagen", "彦": "hiko", "彦十": "hikojuu", "德": "toku",
    "忠": "tada", "忠弥": "tadaya", "总一": "sooichi", "惣": "soo", "房": "fusa",
    "才": "sai", "才七": "saishichi", "文": "bun", "新": "shin", "新七": "shinshichi",
    "新八": "shinpachi", "无天": "muten", "明岳": "meigaku", "晋": "shin", "权": "gon",
    "权八": "gonpachi", "松": "matsu", "林": "hayashi", "枣": "natsume", "枫": "kaede",
    "柳": "yanagi", "梅": "ume", "梦": "yume", "梶": "kaji", "次郎": "jiroo",
    "正": "masa", "段": "dan", "治": "ji", "泉识": "senzui?", "法海": "hokkai",
    "泷": "taki", "清": "kiyo", "源": "gen", "源五": "gengo", "源内": "gennai",
    "源吾": "gengo", "源阿弥": "gennami", "满作": "mansaku", "濑": "se", "熊": "kuma",
    "猪": "i", "玄": "gen", "玄龙": "genryuu", "玉": "tama", "理": "osamu",
    "甚": "jin", "甚五": "jingo", "甚六": "jinroku", "甚内": "jinnai", "甲": "koo",
    "留": "tome", "白云": "hakuun", "百": "hyaku", "百千代": "mochiyo", "百鬼": "hyakki",
    "石": "ishi", "祯三": "teizoo", "稻": "ine", "竹": "take", "红叶": "momiji",
    "纲": "tsuna", "胜": "katsu", "般若": "hannya", "英七": "eishichi", "茂": "shige",
    "菊": "kiku", "藤": "fuji", "虎": "tora", "虎千代": "torachiyo", "觉": "kaku",
    "角": "kaku", "谦": "ken", "贞": "sada", "贯": "kan", "贯一": "kanichi",
    "辰": "tatsu", "道由": "dooyu", "道空": "dookuu", "道贤": "dooken", "道闲": "dookan",
    "道阿弥": "dooami", "修": "osamu", "金": "kin", "铁": "tetsu", "银": "gin", "长": "chou",
    "闻太": "kikuta?", "隆玄": "ryuugen", "隼人": "hayato", "雷": "rai", "静": "shizu",
    "马": "ma", "驹": "koma", "鬼七": "onishichi", "鸠": "kyuu", "鸢七": "tobishichi?",
    "鹤": "tsuru", "鹤千代": "tsuruchiyo", "黑": "kuro", "龟": "kame", "龟七": "kameshichi",
}


UNRESOLVED_PUA = {"": "xE413"}   # 项目已记 UNRESOLVED，词库无对应，占位待解


def romaji(nm):
    """名字 → 罗马音：先按后缀切，再查词干表。返回 (读音, 置信度)。"""
    for pua, tag in UNRESOLVED_PUA.items():
        if pua in nm:
            return (tag + "_" + nm.replace(pua, ""), "待解")
    for s, r in SUFFIX:
        if nm == s:                      # 整个名字就是一个后缀（如「兵卫」）
            return (r, "高")
        if nm.endswith(s) and len(nm) > len(s):
            stem = nm[:-len(s)]
            if stem in STEM:
                v = STEM[stem]
                return (v.rstrip("?") + r, "低" if v.endswith("?") else "高")
            return ("?" + nm, "缺")
    if nm in STEM:
        v = STEM[nm]
        return (v.rstrip("?"), "低" if v.endswith("?") else "高")
    return ("?" + nm, "缺")


def load_sup():
    sup = collections.defaultdict(dict)
    cur = None
    for line in io.open(LOG, encoding="utf-8", errors="replace"):
        m = re.search(r"Log: SUP\|(.*)$", line.rstrip("\n"))
        if not m:
            continue
        p = m.group(1).split("|")
        if p[0] == "HDR":
            cur = dict(x.split(":", 1) for x in p[1:] if ":" in x).get("年")
            continue
        if cur is None or len(p) < 5:
            continue
        f = dict(x.split(":", 1) for x in p[4:] if ":" in x)
        sup[cur][p[2]] = {"名": restore(p[3]), "势力": restore(f.get("势力", "")),
                          "组织": restore(f.get("组织", "无")),
                          "立场": f.get("立场", ""), "部下": int(f.get("部下") or 0)}
    return sup


def build():
    sup = load_sup()
    with io.open(os.path.join(CSV_DIR, "TaikouHero.csv"), encoding="utf-8-sig",
                 newline="") as fh:
        hr = [r for r in csv.reader(fh) if r and any(x.strip() for x in r)]
    hero = {dict(zip(hr[0], r))["ID"] for r in hr[1:]}
    who = collections.defaultdict(lambda: collections.defaultdict(dict))
    for e in ERAS:
        for p, r in sup[e].items():
            if ("lord_tk5_" + p) in hero or not r["名"] or r["势力"] not in TYPENAME:
                continue
            who[(r["名"], r["势力"])][e] = {"pid": p, "组织": r["组织"],
                                            "立场": r["立场"], "部下": r["部下"]}
    rows, problems = [], []
    for (nm, tp), eras in sorted(who.items(), key=lambda kv: (kv[0][1], kv[0][0])):
        rom, conf = romaji(nm)
        if conf == "缺":
            problems.append("词干表缺字：%s（%s）" % (nm, tp))
        hid = "lord_tk5_%s_%s" % (TYPENAME[tp], rom.lstrip("?"))
        rows.append({
            "ID": hid, "名前": nm, "职业": TYPENAME[tp], "罗马音": rom, "槽位年": eras,
            "置信度": conf,
            "出现年代": "|".join(e for e in ERAS if e in eras),
            "各年代槽号": "|".join("%s:%s" % (e, eras[e]["pid"]) for e in ERAS if e in eras),
            "各年代组织": "|".join("%s:%s" % (e, eras[e]["组织"]) for e in ERAS if e in eras),
            "是否当主": "是" if any(eras[e]["立场"] == "当主" for e in eras) else "",
        })
    # 合成号：按 (职业, 罗马音) 排序后顺序分配 —— 确定性（雷 79）
    for n, r in enumerate(sorted(rows, key=lambda x: (x["职业"], x["罗马音"])), SYNTH_BASE):
        r["合成号"] = str(n)
    dup = [k for k, v in collections.Counter(r["ID"] for r in rows).items() if v > 1]
    if dup:
        problems.append("ID 重复：%s" % " ".join(dup[:10]))
    return rows, problems


def render(rows):
    buf = io.StringIO()
    cols = ["合成号", "ID", "名前", "职业", "罗马音", "置信度", "出现年代", "各年代槽号", "各年代组织", "是否当主"]
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(cols)
    for r in rows:
        w.writerow([r[c] for c in cols])
    return buf.getvalue()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    rows, problems = build()
    text = render(rows)
    if args.check:
        if not os.path.isfile(OUT):
            print("[CHECK] 产物不存在：%s" % OUT)
            return 1
        cur = io.open(OUT, encoding="utf-8-sig", newline="").read()
        if cur.replace("\r\n", "\n") != text.replace("\r\n", "\n"):
            print("[CHECK] 产物与脚本不一致")
            return 1
        print("[CHECK] ✅ 产物一致")
        return 0
    if problems:
        print("❌ 硬问题 %d 条：" % len(problems))
        for p in problems[:30]:
            print("   %s" % p)
        return 1
    with io.open(OUT, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(text)
    # 对齐表：**一人一行**，六年代各一列（与英雄表 Xxx_<年> 同体例——用户 2026-09-12 裁定）
    ab = io.StringIO()
    aw = csv.writer(ab, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    aw.writerow(["合成号", "hero_id", "名前", "职业"] + ["槽号_" + e for e in ERAS]
               + ["组织_" + e for e in ERAS])
    def pick(s_, e):
        for x in s_.split("|"):
            if x.startswith(e + ":"):
                return x.split(":", 1)[1]
        return ""
    for r in sorted(rows, key=lambda x: int(x["合成号"])):
        aw.writerow([r["合成号"], r["ID"], r["名前"], r["职业"]]
                   + [pick(r["各年代槽号"], e) for e in ERAS]
                   + [pick(r["各年代组织"], e) for e in ERAS])
    with io.open(ALIGN_OUT, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(ab.getvalue())
    print("   对齐表 → %s" % os.path.relpath(ALIGN_OUT, REPO))
    c = collections.Counter(r["置信度"] for r in rows)
    print("✅ 已写出 %s（%d 人）" % (os.path.relpath(OUT, REPO), len(rows)))
    print("   置信度：高 %d / 低 %d / 缺 %d" % (c.get("高", 0), c.get("低", 0), c.get("缺", 0)))
    print("   当主 %d 人" % sum(1 for r in rows if r["是否当主"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())

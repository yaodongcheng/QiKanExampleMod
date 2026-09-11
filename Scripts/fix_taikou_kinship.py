#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TaikouHero.csv 亲属列检修 + 身份纠错 + 织丰补全（2026-09-11 用户裁定）
============================================================================
一次做五件事：

1. **列改名 Name → Id**，新增 `MotherId`（母）、`SpouseId`（配偶）。
   列序：FatherId, MotherId, SpouseId, GrandFatherId, KinsId。

2. **残留人名保留** —— 48 个名字是「太阁表里没有的人」（织田信定、北条氏纲……），
   用户裁定：保持中文原名不动，不做 ID 转换、不清空。

3. **性别错位挪正** —— 父列里填了女性 ID 的（丰臣秀吉/秀长的「父」其实是母亲阿中），
   挪进 MotherId。

4. 🔴 **身份纠错** —— 1180 段女性行的 EnglishName/ClanID/CultureID 是当年「按名字
   匹配织丰」填的（BaseInfo 的 `匹配类型=精确匹配`），重名即错。TK5 官方事件脚本写死
   了真实身份（`我是前田利家的妻子・阿松`），据此纠正。证据见 IDENTITY_FIX 注释。

5. **亲属补全** —— 两级来源，高优先者先写、低优先者只补空：
   ① **TK5 脚本铁证**（TK5_SPOUSE / TK5_FATHER，人填表，每条带原文出处）
   ② **织丰（Shokuho）**：只按名字匹配（🔴 禁 ID 对 ID——两侧 StringId 体系各自重编过），
      去空格比对；女性重名用太阁 EnglishName 里的家名消歧。
   与太阁已有值冲突的一律**太阁优先，只报告不覆盖**。

用法：
  python Scripts/fix_taikou_kinship.py --dry-run   # 只出报告
  python Scripts/fix_taikou_kinship.py             # 重写 CSV + 报告
"""
import argparse
import csv
import glob
import io
import os
import re
import sys
from collections import Counter, defaultdict

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHO_DIR = r"h:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/Shokuho"
SHOCN_DIR = r"h:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/Shokuho_CNs"

CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
REPORT_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应",
                           "亲属补全报告_20260911.md")

RENAME = {"FatherName": "FatherId", "GrandFatherName": "GrandFatherId", "KinsName": "KinsId"}
INSERT_AFTER_FATHER = ["MotherId", "SpouseId"]
ID_RE = re.compile(r"^lord_tk5_\d+$")

# ══════════════════════════════════════════════════════════════════════════
#  人填映射表 —— 每条都附 TK5 官方事件脚本原文出处
# ══════════════════════════════════════════════════════════════════════════

# ① 身份纠错：本行 ID → (依附人 ID, 新英文名, 依据)
#    家族/文化跟依附人走（沿用既有约定：阿市 挂 clan_azai_1 = 丈夫浅井家）
IDENTITY_FIX = {
    "lord_tk5_1182": ("lord_tk5_659", "Maeda Matsu",
                      "EP120500「我是(前田利家.名)的妻子．(阿松)」"),
    "lord_tk5_1188": ("lord_tk5_629", "Hojo Ume",
                      "EFF06E00 武田信玄「我女兒(阿梅)則出嫁…嫡子(北條氏政.名)大人」"),
    "lord_tk5_1193": ("lord_tk5_636", "Hosokawa Tama",
                      "EFF20600「{細川忠興.名前}之妻(阿玉)」"),
    "lord_tk5_1185": ("lord_tk5_447", "Takeda Sagami",
                      "EFF0C300「從北條家嫁至(武田勝賴)的(相模)夫人」"),
    "lord_tk5_1198": ("lord_tk5_357", "Sanada Komatsu",
                      "EFF16700「(真田信幸.名)的妻子，小松夫人」"),
    "lord_tk5_1197": ("lord_tk5_760", "Yamauchi Chiyo",
                      "ECF00000「調查:(人物::山內一豐.妻)==(人物::千代)」"),
    # 2026-09-11 补：按 Alias 全段重扫后找到的铁证（此前只搜简体主名，漏了日文/繁体写法）
    "lord_tk5_1194": ("lord_tk5_195", "Oda Kicho",
                      "EFF0C300「調查:(人物::織田信長.妻)==(人物::歸蝶)」"),
    "lord_tk5_1195": ("lord_tk5_506", "Tokugawa Sena Fuji",
                      "ECF00000「調查:(人物::德川家康.妻)==(人物::瀬名)」"),
    "lord_tk5_1196": ("lord_tk5_507", "Tokugawa Toku Hime",
                      "ECF00000「調查:(人物::德川信康.妻)==(人物::德公主)」"),
    "lord_tk5_1183": ("lord_tk5_116", "Uesugi Kiku Hime",
                      "EFF07400「以(武田勝賴.名)的妹妹(菊公主)與(上杉景勝.名)結婚為條件」"),
    "lord_tk5_1236": ("lord_tk5_110", "Imagawa Minami-hime",
                      "EFF06E00 今川義元「吾妻(南姫)」"),
    "lord_tk5_1184": ("lord_tk5_109", "Imagawa Fujin",
                      "ECF00000「更新:(人物::今川氏真.妻)(人物::早川夫人)」（已有昵称，此处只收紧家族/文化）"),
}

# ② TK5 脚本铁证的配偶（双向写）
TK5_SPOUSE = [
    ("lord_tk5_1182", "lord_tk5_659", "阿松=前田利家之妻（EP120500「我是(前田利家.名)的妻子．(阿松)」）"),
    ("lord_tk5_1188", "lord_tk5_629", "阿梅=北条氏政之妻（EFF06E00 原文）"),
    ("lord_tk5_1193", "lord_tk5_636", "阿玉=细川忠兴之妻（EFF01A00「調查:(人物::細川忠興.妻)==(人物::阿玉)」）"),
    ("lord_tk5_1185", "lord_tk5_447", "相模=武田胜赖之妻（EFF0C300「調查:(人物::武田勝賴.妻)==(人物::相模)」+ 旁白「從北條家嫁至」）"),
    ("lord_tk5_1198", "lord_tk5_357", "小松=真田信幸之妻（EFF16700 原文）"),
    ("lord_tk5_1197", "lord_tk5_760", "千代=山内一丰之妻（ECF00000 原文）"),
    ("lord_tk5_1183", "lord_tk5_116", "菊公主=上杉景胜之妻（「以武田勝賴之妹菊公主與上杉景勝結婚為條件」）"),
    ("lord_tk5_1184", "lord_tk5_109", "早川夫人=今川氏真之妻（「更新:(人物::今川氏真.妻)(人物::早川夫人)」）"),
    ("lord_tk5_1196", "lord_tk5_507", "德公主=德川信康之妻（ECF00000「調查:(人物::德川信康.妻)==(人物::德公主)」）"),
    ("lord_tk5_1236", "lord_tk5_110", "南姫=今川义元之妻（今川义元「吾妻(南姫)」）"),
    ("lord_tk5_1181", "lord_tk5_16", "阿市=浅井长政之妻（「織田信長的妹妹阿市嫁到淺井長政」；后改嫁柴田勝家）"),
    ("lord_tk5_1194", "lord_tk5_195", "归蝶=织田信长之妻（EFF0C300「迎娶了他的女兒(歸蝶)，而成為蝮蛇之女婿的(織田信長)」）"),
    ("lord_tk5_1195", "lord_tk5_506", "濑名=德川家康正室（ECF00000「就是築山殿的{德川家康.名前}正室，本名(瀬名)小姐」）"),
    ("lord_tk5_1179", "lord_tk5_517", "宁宁=丰臣秀吉之妻（EFF0A500「高台院（(豐臣秀吉.名)之妻寧寧）大人」+ EP120500「(豐臣秀吉)順利跟淺野長勝的養女．(寧寧)結婚了」）"),
    ("lord_tk5_1189", "lord_tk5_458", "訚千代=立花宗茂之妻（EPF1CA00 立花道雪「希望你能成我家的女婿」+ 大友宗麟「真是對匹配的夫妻啊！」）"),
]

# ③ TK5 脚本铁证的父亲
TK5_FATHER = [
    ("lord_tk5_1188", "lord_tk5_449", "阿梅=武田信玄之女（EFF06E00 武田信玄「我女兒(阿梅)」）"),
    ("lord_tk5_1198", "lord_tk5_652", "小松=本多忠胜之女（EFF16700「是德川家家(本多忠勝)的女兒」）"),
    ("lord_tk5_1183", "lord_tk5_449", "菊公主=武田信玄之女（武田勝賴之妹 ⇒ 信玄之女；织丰同证）"),
    ("lord_tk5_1196", "lord_tk5_195", "德公主=织田信长之女（「織田信長的女兒(德公主)」）"),
    ("lord_tk5_1236", "lord_tk5_801", "南姫=武田信虎之女（今川义元「吾妻(南姫)為(武田信虎)大人之女」）"),
    ("lord_tk5_1189", "lord_tk5_456", "訚千代=立花道雪之女（立花道雪「我的女兒‧(誾千代)」）"),
    ("lord_tk5_1194", "lord_tk5_324", "归蝶=斋藤道三之女（EFF0C300「他的女兒(歸蝶)」「蝮蛇之女婿」）"),
    # 三法师（lord_tk5_1177）已于 2026-09-11 并入 织田秀信（lord_tk5_196）——同一人只留一行；
    # 196 行自带 FatherId=194 / GrandFatherId=195，故此处不再单列。见 merge_taikou_duplicate_person_rows.py
]

# ③a TK5 脚本铁证的祖父（写孙行的 GrandFatherId）
TK5_GRAND = [
    # 三法师→织田秀信 已并（2026-09-11）：196 行自带 GrandFatherId=195，无需另列
]

# ③b TK5 脚本铁证的母亲（写子行的 MotherId）
TK5_MOTHER = [
    ("lord_tk5_507", "lord_tk5_1195", "德川信康之母=濑名（ECF00000「(德川信康.名)大人的母親，就是築山殿的…正室，本名(瀬名)」）"),
    ("lord_tk5_482", "lord_tk5_1232", "长宗我部信亲之母=白枧（EFF1E300「容貌也如母親(白樫)一樣端莊」；光荣立绘文件名作「白㭴」）"),
    ("lord_tk5_518", "lord_tk5_1178", "丰臣秀赖之母=淀夫人（EFF1FA00「(豐臣秀賴)的母親‧(淀夫人)」+ EFF20600 母子对话 16 处）"),
]

# ③c 🔴 **史实来源**（用户 2026-09-11 批准 A/B 组）——**不是游戏数据**：
#     太阁 5 对这些人彻底沉默（CNName+Alias 全段搜事件脚本 0 命中、人物表女性亲属列全空），
#     只能靠史实。提案与审批记录 = `csv/../未识别女性_史实提案_20260911.md`。
#     身份列只跟丈夫收紧 ClanID/CultureID——这些行英文名原本为空，**不编造罗马字**。
HIST_ANCHOR = {
    "lord_tk5_1190": ("lord_tk5_466", "爱姬=伊达政宗正室（阳德院）"),
    "lord_tk5_1192": ("lord_tk5_125", "豪姬=宇喜多秀家正室"),
    "lord_tk5_1201": ("lord_tk5_271", "初姬=京极高次正室（常高院）"),
    "lord_tk5_1202": ("lord_tk5_508", "小督=德川秀忠正室（崇源院）"),
    "lord_tk5_1200": ("lord_tk5_449", "三条=武田信玄正室（三条夫人）"),
    "lord_tk5_1234": ("lord_tk5_359", "山手=真田昌幸正室（山手殿）"),
    "lord_tk5_1203": ("lord_tk5_548", "彦鹤=锅岛直茂正室"),
    "lord_tk5_1191": ("lord_tk5_464", "义姬=伊达辉宗正室（保春院）"),
}
HIST_SPOUSE = [
    ("lord_tk5_1190", "lord_tk5_466", "爱姬=伊达政宗正室"),
    ("lord_tk5_1192", "lord_tk5_125", "豪姬=宇喜多秀家正室"),
    ("lord_tk5_1201", "lord_tk5_271", "初姬=京极高次正室"),
    ("lord_tk5_1202", "lord_tk5_508", "小督=德川秀忠正室"),
    ("lord_tk5_1200", "lord_tk5_449", "三条=武田信玄正室"),
    ("lord_tk5_1234", "lord_tk5_359", "山手=真田昌幸正室"),
    ("lord_tk5_1191", "lord_tk5_464", "义姬=伊达辉宗正室"),
    ("lord_tk5_1203", "lord_tk5_548", "彦鹤=锅岛直茂正室"),
]
HIST_FATHER = [
    ("lord_tk5_1190", "lord_tk5_723", "爱姬=最上义光之女"),
    ("lord_tk5_1191", "lord_tk5_723", "义姬=最上义光之女"),
    ("lord_tk5_1192", "lord_tk5_659", "豪姬=前田利家之女"),
    ("lord_tk5_1201", "lord_tk5_16", "初姬=浅井长政之女"),
    ("lord_tk5_1202", "lord_tk5_16", "小督=浅井长政之女"),
    ("lord_tk5_1203", "lord_tk5_785", "彦鹤=龙造寺隆信之女（存疑：一说为妹）"),
]

# ④ 证据指向「非武家（通用町民女）」→ 家族回落通用、文化清空、不写亲属
DEFER_GENERIC = {
    "lord_tk5_1214": "立绘槽文件名「1115_町民女、小夜」；事件里只作 `代入人物Ａ` 与「主人公.妻」",
    "lord_tk5_1210": "事件里只作 `代入人物Ａ`（据点通用群众角色），无身份句",
    "lord_tk5_1213": "事件里只作 `代入人物Ａ`（据点群众角色），无身份句",
}

# ⑤ 身份存疑、用户裁定「保留现状」→ 身份列不动，也不从织丰补任何亲属
DEFER_KEEP = {
    "lord_tk5_1211": "阿铃：立绘是独立女性像（姬武将段 1065~1083），但事件脚本 0 次出现，身份判不了",
}

# 🔴 以上两类都不参与织丰补全（既不作补的来源，也不作补的目标）——
#    身份没坐实就写亲属 = 把错的写进表里。
NO_FILL = set(DEFER_GENERIC) | set(DEFER_KEEP)

report = []


def say(line=""):
    print(line)
    report.append(line)


def ck(s):
    s = re.sub(r"^\{=[^}]*\}", "", (s or "").strip())
    return re.sub(r"[\s\u00b7\u30fb_·]+", "", s)


def nk(s):
    return re.sub(r"[\s\u00b7\u30fb_·\-\u2019']+", "", (s or "").strip()).lower()


def clan_of_sho(hid):
    p = [x for x in re.sub(r"^(dead_)?lord(_\d+)?_", "", hid).split("_") if not x.isdigit()]
    return p[0] if p else ""


def clan_of_tk(cid):
    c = re.sub(r"_\d+$", "", re.sub(r"^clan_", "", (cid or "").strip()))
    return "" if c in ("japanese", "") else c


def load_shokuho():
    cn = {}
    for p in glob.glob(os.path.join(SHOCN_DIR, "**", "*.xml"), recursive=True):
        for m in re.finditer(r'<string id="([^"]+)"[^>]*text="([^"]*)"',
                             io.open(p, encoding="utf-8", errors="replace").read()):
            cn.setdefault(m.group(1), m.group(2))
    sho, dup = {}, []
    for p in sorted(glob.glob(os.path.join(SHO_DIR, "ModuleData", "heroes", "*.xml"))):
        for m in re.finditer(r"<Hero\b[^>]*>", io.open(p, encoding="utf-8").read()):
            s = m.group(0)

            def g(k, s=s):
                mm = re.search(k + r'="([^"]*)"', s)
                return (mm.group(1) if mm else "").strip()

            hid = g("id")
            if hid in sho:
                dup.append(hid)
                continue
            sho[hid] = dict(father=g("father"), mother=g("mother"), spouse=g("spouse"),
                            text=g("text"), dead=hid.startswith("dead_"))
    return sho, cn, dup


def main():
    ap = argparse.ArgumentParser(description="TaikouHero kinship repair + identity fix + Shokuho fill")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)
    by = {r["ID"]: r for r in rows}

    # 🔴 列名按当前文件实际结构取（老结构 FatherName / 新结构 FatherId），
    #    这样二次运行也走同一套键，不会出现「读旧键读到空、把新列覆盖掉」。
    FATHER = "FatherId" if "FatherId" in cols else "FatherName"
    GRAND = "GrandFatherId" if FATHER == "FatherId" else "GrandFatherName"
    KINS = "KinsId" if FATHER == "FatherId" else "KinsName"
    OLD_KINS = [FATHER, GRAND, KINS]
    REV = {} if FATHER == "FatherId" else {v: k for k, v in RENAME.items()}

    say("# TaikouHero.csv 亲属列检修 + 身份纠错 + 织丰补全报告（2026-09-11）\n")
    say(f"输入：`csv/TaikouHero.csv` {len(rows)} 行 / {len(cols)} 列\n")

    # ── 1. 身份纠错（先做，后面的匹配要用新英文名）──
    say("## 一、身份纠错（TK5 官方事件脚本铁证）\n")
    say("这 1180 段女性行的英文名/家族/文化是当年「按名字匹配织丰」填的，重名即错。\n")
    for tid, (anchor, new_en, why) in IDENTITY_FIX.items():
        r, a = by[tid], by[anchor]
        say(f"- **{r['CNName']}**（{tid}）：`{r.get('EnglishName')}` / `{r.get('ClanID')}` "
            f"→ `{new_en}` / `{a.get('ClanID')}` / `{a.get('CultureID')}`（跟 {a['CNName']}）")
        say(f"    - 依据：{why}")
        r["EnglishName"] = new_en
        r["ClanID"] = a.get("ClanID", "")
        r["CultureID"] = a.get("CultureID", "")

    say(f"\n### 1.1 证据指向「非武家」→ 家族回落通用、文化清空（不写亲属）{len(DEFER_GENERIC)} 条")
    for tid, why in DEFER_GENERIC.items():
        r = by[tid]
        say(f"- **{r['CNName']}**（{tid}）：`{r.get('ClanID')}` / `{r.get('CultureID')}` "
            f"→ `clan_japanese_1` / 空（原为织丰借名）")
        say(f"    - 依据：{why}")
        r["ClanID"] = "clan_japanese_1"
        r["CultureID"] = ""

    say(f"\n### 1.2 身份存疑、保留现状（不动身份列，也不补亲属）{len(DEFER_KEEP)} 条")
    for tid, why in DEFER_KEEP.items():
        r = by[tid]
        say(f"- **{r['CNName']}**（{tid}）：保留 `{r.get('EnglishName')}` / `{r.get('ClanID')}` 不写亲属")
        say(f"    - 依据：{why}")

    # ── 1b. 史实组身份收紧（只动 ClanID/CultureID，英文名原本为空故不编）──
    say(f"\n### 1.3 史实组身份收紧（A/B 组，跟丈夫走）{len(HIST_ANCHOR)} 条")
    for tid, (anchor, why) in HIST_ANCHOR.items():
        r, a = by[tid], by[anchor]
        say(f"- **{r['CNName']}**（{tid}）：`{r.get('ClanID') or '空'}` → `{a.get('ClanID')}` / `{a.get('CultureID')}`（跟 {a['CNName']}）")
        r["ClanID"] = a.get("ClanID", "")
        r["CultureID"] = a.get("CultureID", "")

    # ── 2. 内部检查 ──
    say("\n## 二、内部检查\n")
    resid, dangling, self_ref, dup_tok, female_in = [], [], [], [], []
    for r in rows:
        for c in OLD_KINS:
            toks = [t.strip() for t in (r.get(c) or "").split("|") if t.strip()]
            if len(toks) != len(set(toks)):
                dup_tok.append((r["ID"], r["CNName"], c))
            for t in toks:
                if not ID_RE.match(t):
                    resid.append((r["CNName"], c, t))
                elif t not in by:
                    dangling.append((r["CNName"], c, t))
                elif t == r["ID"]:
                    self_ref.append((r["ID"], r["CNName"], c))
                elif by[t].get("Gender", "").strip() == "0":
                    female_in.append((r["ID"], r["CNName"], c, t, by[t]["CNName"]))
    say(f"- 悬空 ID（指向不存在的行）：**{len(dangling)}**" + (f" → {dangling}" if dangling else ""))
    say(f"- 自引用 {len(self_ref)}；列内重复 ID {len(dup_tok)}")
    uniq = sorted(set(x[2] for x in resid))
    say(f"- 残留人名（用户裁定保留原名）：**{len(uniq)} 个名字 / {len(resid)} 处**"
        f"（太阁表里没有这些人：祖辈如织田信定/北条氏纲/松平清康，及「石田三成父」这类无名占位）")
    say(f"    - 全部：{'、'.join(uniq)}")
    say(f"- 父/祖列指向女性行：**{len(female_in)}**")
    for x in female_in:
        say(f"    - {x[1]}（{x[0]}）的 {x[2]} = {x[4]}（{x[3]}）")

    # ── 3. 性别错位挪正 ──
    moved = []
    for tid, nm, c, tgt, tnm in female_in:
        if c != FATHER:
            continue
        r = by[tid]
        r[FATHER] = ""
        r["MotherId"] = tgt
        moved.append((tid, nm, tgt, tnm))
    say(f"\n## 三、性别错位挪正（父列 → 母列）{len(moved)} 条\n")
    for tid, nm, tgt, tnm in moved:
        say(f"- {nm}（{tid}）：父位 {tnm}（{tgt}）→ 移入 MotherId")

    # ── 4. 亲属补全：TK5 铁证优先，织丰补空 ──
    tk5_spouse, tk5_father, tk5_mother, tk5_grand, blk = [], [], [], [], []
    for a, b, why in TK5_SPOUSE:
        for me, other in ((a, b), (b, a)):
            if (by[me].get("SpouseId") or "").strip():
                blk.append((by[me]["CNName"], "配偶", by[other]["CNName"], "格子已有值"))
                continue
            by[me]["SpouseId"] = other
            if me == a:
                tk5_spouse.append((by[a]["CNName"], by[b]["CNName"], why))
    for me, fa, why in TK5_FATHER:
        cur = (by[me].get(FATHER) or "").strip()
        if cur:
            blk.append((by[me]["CNName"], "父", by[fa]["CNName"], f"已有 {by.get(cur, {}).get('CNName', cur)}"))
            continue
        by[me][FATHER] = fa
        tk5_father.append((by[me]["CNName"], by[fa]["CNName"], why))
    for kid, mo, why in TK5_MOTHER:
        cur = (by[kid].get("MotherId") or "").strip()
        if cur:
            blk.append((by[kid]["CNName"], "母", by[mo]["CNName"], f"已有 {by.get(cur, {}).get('CNName', cur)}"))
            continue
        by[kid]["MotherId"] = mo
        tk5_mother.append((by[kid]["CNName"], by[mo]["CNName"], why))
    for me, gf, why in TK5_GRAND:
        cur = (by[me].get(GRAND) or "").strip()
        if cur:
            blk.append((by[me]["CNName"], "祖父", by[gf]["CNName"], f"已有 {by.get(cur, {}).get('CNName', cur)}"))
            continue
        by[me][GRAND] = gf
        tk5_grand.append((by[me]["CNName"], by[gf]["CNName"], why))
    # ── 4b. 史实组亲属（来源＝史实，非游戏数据；与 TK5 铁证分开登记）──
    hist_sp, hist_fa = [], []
    for a, b, why in HIST_SPOUSE:
        for me, other in ((a, b), (b, a)):
            if (by[me].get("SpouseId") or "").strip():
                blk.append((by[me]["CNName"], "配偶(史实)", by[other]["CNName"], "格子已有值"))
                continue
            by[me]["SpouseId"] = other
            if me == a:
                hist_sp.append((by[a]["CNName"], by[b]["CNName"], why))
    for me, fa, why in HIST_FATHER:
        cur = (by[me].get(FATHER) or "").strip()
        if cur:
            blk.append((by[me]["CNName"], "父(史实)", by[fa]["CNName"], f"已有 {by.get(cur, {}).get('CNName', cur)}"))
            continue
        by[me][FATHER] = fa
        hist_fa.append((by[me]["CNName"], by[fa]["CNName"], why))

    say(f"\n## 四、TK5 脚本铁证补入\n")
    say(f"### 4.1 配偶 {len(tk5_spouse)} 对（双向）")
    for a, b, why in tk5_spouse:
        say(f"- {a} ↔ {b}　—— {why}")
    say(f"\n### 4.2 父亲 {len(tk5_father)} 条")
    for a, b, why in tk5_father:
        say(f"- {a} ← {b}　—— {why}")
    say(f"\n### 4.3 母亲 {len(tk5_mother)} 条（写在子行的 MotherId）")
    for a, b, why in tk5_mother:
        say(f"- {a} 之母 ← {b}　—— {why}")

    say(f"\n### 4.4 🔴 史实组（**来源＝史实知识，非游戏数据**；用户 2026-09-11 批准 A/B 组）")
    say(f"### 4.3b 祖父 {len(tk5_grand)} 条")
    for a, b, why in tk5_grand:
        say(f"- {a} 之祖 ← {b}　—— {why}")
    say(f"- 配偶 {len(hist_sp)} 对：")
    for a, b, why in hist_sp:
        say(f"    - {a} ↔ {b}　—— {why}")
    say(f"- 父亲 {len(hist_fa)} 条：")
    for a, b, why in hist_fa:
        say(f"    - {a} ← {b}　—— {why}")

    # ── 5. 织丰匹配 + 补空 ──
    sho, cn, dup = load_shokuho()
    def sho_cn(h):
        k = re.sub(r"^\{=([^}]*)\}.*$", r"\1", sho[h]["text"])
        return ck(cn.get(k, ""))

    def sho_en(h):
        return nk(re.sub(r"^\{=[^}]*\}", "", sho[h]["text"]))

    tk_en = {}
    for r in rows:
        if nk(r.get("EnglishName")):
            tk_en.setdefault(nk(r["EnglishName"]), r["ID"])
    tk_cn = defaultdict(list)
    for r in rows:
        for k in [ck(r.get("CNName"))] + [ck(x) for x in (r.get("Alias") or "").split("|")]:
            if k:
                tk_cn[k].append(r["ID"])

    cand = defaultdict(list)
    for hid in sho:
        t = tk_en.get(sho_en(hid))
        if t:
            if t not in NO_FILL:
                cand[t].append((hid, "en"))
            continue
        nm = sho_cn(hid)
        if not nm:
            continue
        sc = clan_of_sho(hid)
        for c in tk_cn.get(nm, []):
            if c in NO_FILL:
                continue
            cand[c].append((hid, "clan" if (sc and clan_of_tk(by[c].get("ClanID")) == sc) else "cn"))

    resolved, dropped = {}, []
    for tid, claims in cand.items():
        for lvl in ("en", "clan", "cn"):
            same = [h for h, e in claims if e == lvl]
            if not same:
                continue
            if len(same) > 1:
                alive = [h for h in same if not sho[h]["dead"]]
                same = alive if len(alive) == 1 else [h for h in same if not re.search(r"_\d+$", h)]
            if len(same) == 1:
                resolved[same[0]] = tid
            else:
                dropped.append((by[tid]["CNName"], lvl, len(same)))
            break
    say(f"\n## 五、织丰匹配（只按名字，禁 ID 对 ID）\n")
    say(f"- 织丰英雄 {len(sho)} 个（跨文件重复 id {len(dup)} 个，取首个）")
    say(f"- 匹配上 {len(resolved)} 对，覆盖太阁 {len(set(resolved.values()))} 行")
    say(f"- 撞车丢弃 {len(dropped)} 组：" + "；".join(f"{n}({l})" for n, l, _ in dropped))

    fill_f, fill_m, fill_s, conflict, gate = [], [], [], [], []
    for hid, tid in resolved.items():
        me = by[tid]
        for field, col, want in (("father", FATHER, "1"),
                                 ("mother", "MotherId", "0"),
                                 ("spouse", "SpouseId", None)):
            ref = re.sub(r"^Hero\.", "", sho[hid][field])
            if not ref or ref == hid or ref not in resolved:
                continue
            tgt = resolved[ref]
            if tgt == tid:
                continue
            g_me, g_tg = me.get("Gender", "").strip(), by[tgt].get("Gender", "").strip()
            if want and g_tg and g_tg != want:
                gate.append((me["CNName"], field, by[tgt]["CNName"], "目标性别不符"))
                continue
            if field == "spouse" and g_me and g_tg and g_me == g_tg:
                gate.append((me["CNName"], field, by[tgt]["CNName"], "同性"))
                continue
            have = (me.get(col or "SpouseId") or "").strip()
            if field == "father" and have and have != tgt:
                conflict.append((me["CNName"], by[tgt]["CNName"],
                                 by.get(have, {}).get("CNName", have)))
                continue
            if have:
                continue
            row = (me["CNName"], by[tgt]["CNName"])
            {"father": fill_f, "mother": fill_m, "spouse": fill_s}[field].append(row)
            me[col or "SpouseId"] = tgt

    say(f"\n### 5.1 织丰补空：父 {len(fill_f)} / 母 {len(fill_m)} / 配偶 {len(fill_s)} 格")
    for lbl, lst in (("父", fill_f), ("母", fill_m), ("配偶", fill_s)):
        for a, b in lst:
            say(f"- [{lbl}] {a} ← {b}")
    say(f"\n### 5.2 太阁 vs 织丰 父冲突（太阁优先，不覆盖）{len(conflict)} 条")
    for nm, sf, tf in conflict:
        say(f"- {nm}：太阁记 {tf}，织丰记 {sf}")
    say(f"\n### 5.3 性别闸门拦下 {len(gate)} 条")
    for x in gate:
        say(f"- {x[0]}（{x[2]}）：{x[3]}")

    # ── 6. 落盘 ──
    say("\n## 六、已裁定 / 待裁定\n")
    say("- ✅ **父亲侧 KinsId 不回填**（用户裁定 2026-09-11）：新加的 10 条「子认父」只保证"
        "子→父方向；父亲那侧 KinsId 不登记这些孩子（涉及 武田信玄/织田信长/本多忠胜/立花道雪/"
        "武田信虎/伊集院忠朗/佐竹义昭/土居清良/河野通宣）。**后续排查亲子对账时会看到这 10 条"
        "单向，属已知状态，不是缺陷**")
    say("- ✅ **家族收紧**（用户裁定 2026-09-11）：已识别身份的 13 行，ClanID/CultureID 一律跟丈夫走"
        "（阿市、阿松、阿梅、阿玉、相模、小松、千代、归蝶、濑名、德公主、菊公主、南姫、早川夫人）；"
        "史实组 7 行（义姬/豪姬/初姬/小督/三条/山手/彦鹤）同规则收紧——这些行英文名原为空，**未编造罗马字**")
    say("- ⏳ **史实组只落了 A/B 组**（用户 2026-09-11 裁定「只处理高置信以及中置信」）："
        "C 组 17 位（安岐/永姬/熙子/幸圆/志津/阿通/阿史/美代/岭/千岁/蝴蝶/新庄/照姬/阿铃/阿妙/阿春…）"
        "身份未定，保持空白；D/E 组未处理——阿夏·阿绫（光荣记名「农民女、阿夏」「奇怪的姑娘、阿绫」"
        "= 通用町民女立绘）、里璐·李华梅·蒂雅（大航海时代联动）、九郎判官义经·相马小次郎将门（传说人物）。"
        "提案与审批记录 = `csv/../未识别女性_史实提案_20260911.md`")
    say("- ✅ **同人两行已并**（用户裁定「同一人只能一行」）：三法师（原 lord_tk5_1177）并入 织田秀信（lord_tk5_196）"
        "——TK5 列传原文「信忠之嫡子。信長之孫。幼名三法師。」；其独立立绘（外观 1045）以 stage「三法师」保留在 196 行的立绘阶段里。\n"
        "范本脚本 `merge_taikou_duplicate_person_rows.py`")
    say("- ⚠️ **冬姬**（lord_tk5_1187）：B 组里唯一没落的一条——「织田信长之女」说法我把握不足，未填，等你裁定")
    say("- ⚠️ **阿妙 / 阿春 归类待复核**：原先按「事件里只作 `代入人物Ａ`」判为通用町民女，"
        "但两者的光荣立绘在**姬武将段**（1078/1081）、不在通用段——判定可能不对")
    say("- ⏳ **丰臣秀次 / 秀赖 的祖父位**：填的是 阿中（实为祖母），按裁定留在原列未动")
    say("- ⏳ **阿铃**（lord_tk5_1211）：身份判不了，保留现状（织丰借来的 `Chosokabe Suzu`），也未补任何亲属")
    say("- ✅ **48 个表外亲属名**：保留中文原名（太阁 5 里本来就是纯文字显示）")
    say("- ✅ **9 条父冲突**（太阁记生父、织丰记养父）：太阁优先，未覆盖——见 §5.2")

    # ── 7. 落盘 ──
    if "FatherId" in cols:                       # 已是新版列结构（二次运行）→ 不再改名/插列
        new_cols = list(cols)
        rev = {}                                 # 新列名就是真列名，无需回映
        say("\n## 七、落盘\n")
        say(f"- 列结构已是新版（{len(cols)} 列，含 {INSERT_AFTER_FATHER}），未改列")
    else:
        new_cols = []
        for c in cols:
            if c in RENAME:
                new_cols.append(RENAME[c])
                if c == "FatherName":
                    new_cols += INSERT_AFTER_FATHER
            else:
                new_cols.append(c)
        rev = {v: k for k, v in RENAME.items()}   # 新列名 → 旧列名（改名列取旧值）

    if args.dry_run:
        say("\n--dry-run：未写文件。")
        io.open(REPORT_PATH, "w", encoding="utf-8").write("\n".join(report) + "\n")
        print(f"\n报告：{REPORT_PATH}")
        return 0

    # 实际要写出的内容（往返校验拿它对账——不能用 rows.get(new_col) 查，改名列查不到）
    payload = [{c: (r.get(rev.get(c, c)) or "") for c in new_cols} for r in rows]

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=new_cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for d in payload:
        w.writerow(d)
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    ok = len(back) == len(rows)
    if ok:
        for i, b in enumerate(back):
            for c in new_cols:
                if payload[i][c] != (b.get(c) or ""):
                    say(f"[FATAL] 往返不一致 行{i} 列{c!r}：写出 {payload[i][c]!r} 读回 {b.get(c)!r}")
                    ok = False
                    break
            if not ok:
                break
    if not ok:
        say("[FATAL] 往返校验失败，未写文件")
        io.open(REPORT_PATH, "w", encoding="utf-8").write("\n".join(report) + "\n")
        return 1

    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    if "FatherId" not in cols:
        say(f"- 列：{len(cols)} → {len(new_cols)}（改名 {[RENAME[c] for c in OLD_KINS]}，新增 {INSERT_AFTER_FATHER}）")
    say(f"- 往返校验通过，已写回 `{os.path.relpath(CSV_PATH, REPO).replace(chr(92), '/')}`")
    io.open(REPORT_PATH, "w", encoding="utf-8").write("\n".join(report) + "\n")
    print(f"\n报告：{REPORT_PATH}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

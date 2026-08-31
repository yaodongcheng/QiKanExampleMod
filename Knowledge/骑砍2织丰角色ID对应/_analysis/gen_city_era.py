# -*- coding: utf-8 -*-
"""城配对直灌器 v2（TODO-A 定稿）：tk5_era_init_v1.json 城记录 → CityTaikou.csv Owner_*/Soldiers_*。

规则（06 plan「城配对（TODO-A）」忠实落码，2026-08-31 数据实证修订）：
  R0 装载   ：hero_by_tk（TK5编号→TaikouHero 行，多值先到先得）；
              name2rows = ChineseName + TK5_Name(|拆) + Alias(|拆) —— 三索引（实证：滨松城
              行名存在但 TK5_Name=曳马城，缺 ChineseName 索引会 NOMATCH）。
  R1 城主键 ：城记录 lord → hero；无映射 → 清单（tk5_#<编号>占位；当前 6 时代 0 例）。
  R2 主链   ：城主 City_<era>（本时代据点名）→ name2rows 唯一行 → 写 Owner=hero.ID /
              Soldiers=该城主城记录组**最大档**（城主多城时主城=最大档，§3.6 信长 7600 清洲）。
              主链未命中（NOMATCH/多命中）→ 该城主整组进 R3fallback。
  R3 剩余   ：多城城主剩余记录（最大档之后的 1..n 条）→ 候选 = 城主**正史** City_* 全部列
              （1549..1598，**排除 City_dream1560 —— 梦剧本列非正史快照，实证：三好长庆
              dream1560=骏府城 幽灵名**；排除「无效」）→ 行集（排除已铺行）：
              剩余 n==1 且候选 m==1 → 写；否则 → 清单（不瞎填，规则 3/4）。
  R3fallback：主链失败的城主（信长 1568 岐阜城/安土城 NOMATCH 等）整组记录 ==1 且
              hero 正史 City_* 行集（未铺）唯一 → 补锚；否则整组进清单。
  R4 N:1 吸收：城主主城行 TK5_Name 多值（|）→ 其余档位可能已并入主城行 → 清单备注，
              不写他行（多对一 Owner/Soldiers 取最大档，其余档位注释保留，规则 4）。
  R5 地理   ：写入行 Culture != 城主 CultureID → warning（名字命中优先，不拦）。
  R6 无主   ：lord=1101（無标记）等无 hero → 清单（该时代无主城记录）。

只写 6 时代 12 列（Owner_YYYY/Soldiers_YYYY），其余列一字不动（铁律 22：改规则→重跑）。
待核清单输出 stdout + 落 city_pair_pending_<date>.md（文档承担审核清单职责）。
"""
import csv
import io
import json
import sys
import collections
import datetime
import os

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

KN = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\骑砍2织丰角色ID对应\csv"
FN = KN + r"\CityTaikou.csv"
TK5 = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\_analysis\decoded\tk5_era_init_v1.json"
HERE = os.path.dirname(os.path.abspath(__file__))
ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
CITY_COLS = ["City_1549", "City_1554", "City_1560", "City_1568",
             "City_1575", "City_1582", "City_1584", "City_1598"]   # 正史列（排除 dream1560）
NO_HERO_LORD = 1101                                                 # TK5「無」标记


def rdd(fn):
    return list(csv.DictReader(io.open(fn, encoding="utf-8-sig")))


hero = rdd(KN + r"\TaikouHero.csv")
hero_by_tk = {}
for h in hero:
    for part in (h.get("TK5编号") or "").split("|"):
        part = part.strip()
        if part.isdigit():
            if int(part) in hero_by_tk and hero_by_tk[int(part)]["ID"] != h["ID"]:
                print(f"⚠️ TK5编号 {part} 冲突: {hero_by_tk[int(part)]['CNName']} vs {h['CNName']}（保留先到）")
            hero_by_tk.setdefault(int(part), h)

with io.open(FN, encoding="utf-8-sig", newline="") as f:
    rows = list(csv.reader(f))
hdr = rows[0]
# 表头卫生（生成链历史缺陷）：数据区若混入表头副本行（rows[1]==hdr），剥除——
# 双表头会让"表头副本"成为一个假数据行，且 765+1 行口径失真
rows = [hdr] + [r for r in rows[1:] if not (r and r == hdr)]
ci = {y: (hdr.index("Owner_%s" % y), hdr.index("Soldiers_%s" % y)) for y in ERAS}
idx_cn, idx_tkn, idx_alias, idx_cu = hdr.index("ChineseName"), hdr.index("TK5_Name"), hdr.index("Alias"), hdr.index("Culture")
idx_id = hdr.index("ID")

# R0：name2rows（三索引 → 织丰行列表；含「城」尾归一：行名安土 vs TK5名安土城 双向）
#     等级 level：'main' = 行名/别名/TK5_Name首段（主城锚）；'sub' = TK5_Name 余段（N:1 并入城）
#     —— sub 命中不进自动路径（长篠城并入冈崎城行：长照/信昌的 City=长篠城 → 清单，
#     让织丰行 Owner 归真正主城城主，不因并入城占用主城列）
name2rows = collections.defaultdict(list)


def _add_nm(nm, cn, level):
    if not nm:
        return
    if all(c != cn for c, _l in name2rows[nm]):
        name2rows[nm].append((cn, level))
    t = nm[:-1] if nm.endswith("城") else nm + "城"
    if t != nm:
        if all(c != cn for c, _l in name2rows[t]):
            name2rows[t].append((cn, level))


for r in rows[1:]:
    cn = r[idx_cn]
    _add_nm((cn or "").strip(), cn, "main")
    for nm in (r[idx_alias] or "").split("|"):
        _add_nm(nm.strip(), cn, "main")
    tkn_parts = (r[idx_tkn] or "").split("|")
    for nm in tkn_parts:
        # TK5_Name 段等级：单段 = 唯一桥名（织丰名≠TK5名：滨松=曳马/岐阜=稻叶山/清须=清洲——main）；
        # 多段 = 并入城：与行名相等段 main，其余=sub（兴国寺城|骏府城：骏府 main、兴国寺 sub——
        # 垪和氏续 City=兴国寺城 只算并入城城主，不得占用骏府城行 Owner；那古野城经「城」归一
        # 与行名那古野同键，先入 main 先到者胜）
        _add_nm(nm.strip(), cn, "main" if len(tkn_parts) == 1 or (nm.strip() == (cn or "").strip()) else "sub")

era_data = json.load(open(TK5, encoding="utf-8"))
pending = []            # 待核清单条目
pinfo = []              # (era, 城行, Owner, Soldiers, 规则)

# ---- R2/R3 函数定义（先定义后启用，规范顺序） ----
def _R3(era, h, lid, rest, placed, pending, name2rows, pinfo, main_row):
    """剩余记录（非最大档）落位：n==1 且候选行唯一 → 写；否则清单。"""
    cand = []
    for col in CITY_COLS:
        v = (h.get(col) or "").strip()
        if not v or v == "无效":
            continue
        for cn, lv in name2rows.get(v, []):
            if lv == "main" and cn not in placed and cn not in cand and cn != main_row:
                cand.append(cn)
    n = len(rest)
    sol = [c["soldiers"] for c in rest]
    if n == 1 and len(cand) == 1:
        row_cn = cand[0]
        placed.add(row_cn)
        pinfo.append((era, row_cn, h["ID"], str(rest[0]["soldiers"]), "R3剩余"))
        print(f"[{era}] R3写: {row_cn} <- {h['CNName']} {rest[0]['soldiers']}")
    else:
        pending.append((era, h["CNName"], lid, "城主多城-剩余落位", sol,
                        "主城行 %s；候选行 %s" % (main_row, str(cand) if cand else "无（N:1 吸收/R4）")))


def _R3fallback(era, h, lid, cl, placed, pending, pinfo):
    """主链 NOMATCH/多命中：城主整组落位。"""
    cand = []
    for col in CITY_COLS:
        v = (h.get(col) or "").strip()
        if not v or v == "无效":
            continue
        for cn, lv in name2rows.get(v, []):
            if lv == "main" and cn not in placed and cn not in cand:
                cand.append(cn)
    sol = [c["soldiers"] for c in cl]
    if len(cl) == 1 and len(cand) == 1:
        row_cn = cand[0]
        placed.add(row_cn)
        pinfo.append((era, row_cn, h["ID"], str(cl[0]["soldiers"]), "R3f补锚"))
        print(f"[{era}] R3f写: {row_cn} <- {h['CNName']} {cl[0]['soldiers']}")
    else:
        pending.append((era, h["CNName"], lid, "主链NOMATCH整组", sol,
                        "City_%s=%s；正史行集=%s" % (era, h.get("City_" + era), str(cand) if cand else "无")))


for era in ERAS:
    by_lord = collections.defaultdict(list)
    for c in era_data["cities"][era]:
        by_lord[c["lord"]].append(c)
    placed = set()
    for lid, cl in sorted(by_lord.items()):
        sol = [c["soldiers"] for c in cl]
        h = hero_by_tk.get(lid)
        if h is None:
            if lid == NO_HERO_LORD:
                pending.append((era, "（無）", lid, "无主城记录", sol, "建议 Owner 留空，不放占位"))
            else:
                pending.append((era, "?", lid, "城主无织丰映射", sol, "占位 tk5_#%s" % lid))
            continue
        cy = (h.get("City_" + era) or "").strip()
        hits = name2rows.get(cy, []) if (cy and cy != "无效") else []
        mains = [c for c, lv in hits if lv == "main"]
        if len(mains) > 1:
            # 织丰成对城/町行（久留里城/久留里、新发田城/新发田…）：TK5 城名 = 城堡 → 城尾行优先
            jx = [c for c in mains if c.endswith("城")]
            if len(jx) == 1:
                mains = jx
            else:
                pending.append((era, h["CNName"], lid, "主链多命中", sol,
                                "City_%s=%s 命中多行 %s——人工裁决" % (era, cy, mains)))
                continue
        if len(mains) == 0:
            if hits:
                # 仅 sub 命中 = City_<era> 是被 N:1 并入的城（长篠城→冈崎城行）——清单
                pending.append((era, h["CNName"], lid, "主链-N1并入城", sol,
                                "City_%s=%s 命中并入行 %s（N:1 次级）——织丰无此城；城主为并入城城主，"
                                "织丰行 Owner 应归主城城主，人工裁决" % (era, cy, [c for c, _ in hits])))
            else:
                _R3fallback(era, h, lid, cl, placed, pending, pinfo)
            continue
        row_cn = mains[0]
        if row_cn in placed:
            # 主链冲突（该行已被其他城主铺）→主城档进清单，剩余记录仍试 R3
            cl_sorted = sorted(cl, key=lambda x: -x["soldiers"])
            pending.append((era, h["CNName"], lid, "主链冲突-行已铺", [cl_sorted[0]["soldiers"]],
                            "行 %s 已被其他城主占——两城主同据一城，人工裁决" % row_cn))
            rest = cl_sorted[1:]
            if rest:
                _R3(era, h, lid, rest, placed, pending, name2rows, pinfo, row_cn)
            continue
        cl_sorted = sorted(cl, key=lambda x: -x["soldiers"])
        best = cl_sorted[0]
        placed.add(row_cn)
        pinfo.append((era, row_cn, h["ID"], str(best["soldiers"]), "R2主链"))
        print(f"[{era}] R2写: {row_cn} <- {h['CNName']}({h['ID']}) {best['soldiers']}")
        rest = cl_sorted[1:]
        if rest:
            _R3(era, h, lid, rest, placed, pending, name2rows, pinfo, row_cn)
    print(f"[{era}] 完成：写入 {len(placed)} 行，清单累积 {len(pending)} 条")

# ---- 写回：只写 12 列，其余列一字不动 ----
done = set()
for era, row_cn, owner, soldiers, rule in pinfo:
    if (era, row_cn) in done:
        continue
    for r in rows[1:]:
        if r[idx_cn] == row_cn and r[ci[era][0]] == "":
            r[ci[era][0]] = owner
            r[ci[era][1]] = soldiers
            done.add((era, row_cn))
            break

# ---- R7 村庄继承（用户裁定 2026-08-31）：village 行所属 = 父级 castle/town 的同年所属；Soldiers 留空 ----
import re as _re
import math as _math
SETTLE_XML = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\_analysis\settlements_extra.work.xml"
# XML 副本（禁止改源文件 Modules/Shokuho/...）：Settlement id → (posX, posY, culture)
xy_cul = {}
for m in _re.finditer(r'<Settlement id="([^"]+)"([^>]*)>', open(SETTLE_XML, encoding="utf-8").read()):
    attrs = {}
    for av in _re.finditer(r'(posX|posY|culture)="([^"]*)"', m.group(2)):
        attrs[av.group(1)] = av.group(2)
    if attrs.get("posX") and attrs.get("posY"):
        xy_cul[m.group(1)] = (float(attrs["posX"]), float(attrs["posY"]), (attrs.get("culture") or "").split(".")[-1])

# ---- R8 反向继承（用户裁定 2026-08-31）：纯织丰城（无 TK5_ID，TK5 无此城）→ 同文化最近的
#     已配城所属（posX/posY 欧氏；Soldiers 留空）——先 R8 填城主城，再 R7 村庄随父城补齐 ----
idx_tk5id = hdr.index("TK5_ID")
rev_inh = 0
rev_far = []           # 长距继承（>500）备注
COLS_ALL = [c for c in hdr if c.startswith(("Owner_", "Soldiers_"))]
for era in ERAS:
    rows_by_id = {r[idx_id]: r for r in rows[1:]}
    # 候选 = 城级行（非农村 village_*）、Owner 空、无 TK5_ID（纯织丰）
    cands = [r for r in rows[1:] if not r[idx_id].startswith("village_")
             and not (r[ci[era][0]] or "").strip() and not (r[idx_tk5id] or "").strip()]
    # 锚 = 城级行、Owner 已填、有坐标
    anchors = [r for r in rows[1:] if not r[idx_id].startswith("village_")
               and (r[ci[era][0]] or "").strip() and r[idx_id] in xy_cul]
    for c in cands:
        cd = xy_cul.get(c[idx_id])
        if not cd:
            pending.append((era, c[idx_cn], c[idx_id], "反向继承-无坐标", [], "XML 副本无此坐标"))
            continue
        best = None
        for a in anchors:
            ad = xy_cul[a[idx_id]]
            if ad[2] != cd[2]:
                continue                      # 同文化
            d = _math.hypot(ad[0] - cd[0], ad[1] - cd[1])
            if best is None or d < best[0]:
                best = (d, a)
        if best:
            d, a = best
            c[ci[era][0]] = a[ci[era][0]]     # 继承最近同文化城所属；Soldiers 留空
            rev_inh += 1
            if d > 500:
                rev_far.append((era, c[idx_cn], a[idx_cn], d))
        else:
            pending.append((era, c[idx_cn], c[idx_id], "反向继承-无锚", [], "本时代无同文化已配城"))
v2p = {}
for m in _re.finditer(r'<Settlement id="(village_[^"]+)"(?:(?!</Settlement>).)*?bound="Settlement\.([^"]+)"(?:(?!</Settlement>).)*?</Settlement>',
                      open(SETTLE_XML, encoding="utf-8").read(), _re.S):
    v2p[m.group(1)] = m.group(2)
v_inh = 0
_cv = _re.compile(r"^castle_village_(.+)_\d+$")
for era in ERAS:
    for r in rows[1:]:
        rid = r[idx_id]
        if rid.startswith("castle_village_"):
            mm = _cv.match(rid)
            pid = ("castle_" + mm.group(1)) if mm else None      # 城堡附属村：前缀规则
        else:
            pid = v2p.get(rid)                                    # 独立农村：XML bound
        if not pid:
            continue
        if (r[ci[era][0]] or "").strip():
            continue
        for pr in rows[1:]:
            if pr[idx_id] == pid and (pr[ci[era][0]] or "").strip():
                r[ci[era][0]] = pr[ci[era][0]]      # 继承父城所属；Soldiers 留空（村庄无城兵）
                v_inh += 1
                break

with io.open(FN, "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f, lineterminator="\r\n")
    w.writerows(rows)

# ---- 待核清单落盘 ----
today = datetime.date.today().isoformat()
out_pending = os.path.join(HERE, "city_pair_pending_%s.md" % today)
with io.open(out_pending, "w", encoding="utf-8") as f:
    f.write("# 城配对待核清单（gen_city_era.py v2 产出，%s）\n\n" % today)
    f.write("> 清单条目 = 未自动落位的城主记录。批准方式：逐条确认行名后改 CITY 表/别名后重跑，\n")
    f.write("> 或由用户裁定后反馈（人工补写需走生成链，禁止手改 CSV——铁律 22）。\n\n")
    cnt = collections.Counter(p[3] for p in pending)
    f.write("## 分类统计\n")
    for k, v in cnt.most_common():
        f.write("- **%s**: %d 条\n" % (k, v))
    f.write("\n## 明细\n")
    f.write("| 时代 | 城主 | TK5编号 | 类型 | 士兵档 | 说明 |\n|---|---|---|---|---|---|\n")
    for era, name, lid, typ, sol, note in pending:
        f.write("| %s | %s | %s | %s | %s | %s |\n" % (era, name, lid, typ, "|".join(map(str, sol)), note))

print("\n=== 汇总 ===")
print("城主城自动:", len(done), "| 反向继承(城-时代):", rev_inh, "| 村庄继承(村-时代):", v_inh,
      "| 待核清单:", len(pending), "条 ->", os.path.basename(out_pending))
if rev_far:
    print("⚠️ 长距反向继承(>500) 样例:", rev_far[:8], "共", len(rev_far), "条")
# 验收抽查
rows2 = list(csv.reader(io.open(FN, encoding="utf-8-sig")))
for r in rows2[1:]:
    if r[idx_cn] in ("骏府城", "清须城", "小谷城", "冈崎城", "滨松城"):
        print("验收 %-5s O1554=%s/%s O1560=%s/%s O1568=%s/%s O1575=%s/%s O1582=%s/%s" % (
            r[idx_cn], r[ci["1554"][0]], r[ci["1554"][1]], r[ci["1560"][0]], r[ci["1560"][1]],
            r[ci["1568"][0]], r[ci["1568"][1]], r[ci["1575"][0]], r[ci["1575"][1]],
            r[ci["1582"][0]], r[ci["1582"][1]]))

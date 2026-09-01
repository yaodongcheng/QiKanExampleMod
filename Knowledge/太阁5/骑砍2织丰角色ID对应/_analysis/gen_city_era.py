# -*- coding: utf-8 -*-
"""城配对直灌器 v3（2026-09-01）：era_v2 为唯一信源直接重填 Owner_YYYY/Soldiers_YYYY。

信源（全部确定性，禁止猜测/投票/匹配打分——2026-09-01 用户裁定）：
  era_v2/{1554..1598}/cities.csv（城表 180 条等距直读 + 町 66 + 里 12 + 砦 16；
  name_official=当代名，name_history=全量历史名集含当前名；lord_name/force_name/soldiers 权威）
  织丰侧：CityTaikou 行（name2rows = ChineseName + Alias + TK5_Name）｜hero：TaikouHero CNName→ID

规则（v3）：
  R2v 城直写 ：v2 城（type=城，180）→ name_history 命中唯一织丰行 → 直写
               Owner=城主名→StringId、Soldiers=v2 数值（每时代独立）；
               N:1 行（一织丰行收多座 v2 城，23 行）：主城 = name_official 归一(去城尾)
               ==行名归一的唯一者（那古野行←鸣海/小牧山/那古野城）；其余=并入城 →
               城主记录进清单（不占行，行数据=主城，对应旧桥 IsMerge 语义）。
  R町砦直写 ：type=町/砦且命中唯一行 → 直写（城主+士兵）；多命中→清单；无织丰行→跳过汇报。
  R8v 收窄  ：仍空的城行（城级、无 TK5_ID 旧桥=织丰独有）→ 同文化最近已配城（posX/posY）继承
              所属；Soldiers 留空。（v2 命不中的才允许借邻居。）
  R7 村继承 ：village_*（XML bound）/ castle_village_*（前缀 X_N→X）→ 父城同年所属；Soldiers 留空。
  表头卫生  ：数据区表头副本行剥除（v2 起固定）。

只写 6 时代 12 列，其余列一字不动（铁律 22）。脚本=临时产物不入库。
审计：跑完自动与 v2 全量回验（应 对名+兵=1080、错名=0、兵错=0）。
"""
import csv
import io
import os
import re
import sys
import json
import math
import collections
import datetime

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BASE = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\太阁5\骑砍2织丰角色ID对应"
KN = BASE + r"\csv"
FN = KN + r"\CityTaikou.csv"
V2 = BASE + r"\_analysis\decoded\era_v2"
SETTLE_XML = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\太阁5\骑砍2织丰角色ID对应\_analysis\settlements_extra.work.xml"
HERE = os.path.dirname(os.path.abspath(__file__))
ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]


def rdd(fn):
    return list(csv.DictReader(io.open(fn, encoding="utf-8-sig")))


# ---- R0 装载 ----
heroes = rdd(KN + r"\TaikouHero.csv")
hero_by_cn = collections.defaultdict(list)
for h in heroes:
    nm = (h.get("CNName") or "").strip()
    if nm:
        hero_by_cn[nm].append(h["ID"])

with io.open(FN, encoding="utf-8-sig", newline="") as f:
    rows = list(csv.reader(f))
hdr = rows[0]
# 表头卫生：数据区表头副本行剥除
rows = [hdr] + [r for r in rows[1:] if not (r and r == hdr)]
ci = {y: (hdr.index("Owner_%s" % y), hdr.index("Soldiers_%s" % y)) for y in ERAS}
idx_id = hdr.index("ID")
idx_cn = hdr.index("ChineseName")
idx_tkn = hdr.index("TK5_Name")
idx_alias = hdr.index("Alias")
idx_tk5id = hdr.index("TK5_ID")

name2rows = collections.defaultdict(list)
for r in rows[1:]:
    for nm in [r[idx_cn]] + (r[idx_alias] or "").split("|") + (r[idx_tkn] or "").split("|"):
        nm = nm.strip()
        if nm and r[idx_cn] not in name2rows[nm]:
            name2rows[nm].append(r[idx_cn])
row_by_cn = {r[idx_cn]: r for r in rows[1:]}

# XML 副本：Settlement id → (posX, posY, culture)
xy_cul = {}
for m in re.finditer(r'<Settlement id="([^"]+)"([^>]*)>', open(SETTLE_XML, encoding="utf-8").read()):
    attrs = {}
    for av in re.finditer(r'(posX|posY|culture)="([^"]*)"', m.group(2)):
        attrs[av.group(1)] = av.group(2)
    if attrs.get("posX"):
        xy_cul[m.group(1)] = (float(attrs["posX"]), float(attrs["posY"]), (attrs.get("culture") or "").split(".")[-1])

v2_data = {}
for era in ERAS:
    v2_data[era] = rdd(V2 + "\\" + era + r"\cities.csv")

pending = []        # 待核清单条目 (era, 城主名, city_idx, 类型, 士兵, 说明)
pinfo = []          # (era, 行名, OwnerID, Soldiers, 规则)
cnt = collections.Counter()

def _norm(nm):
    return (nm or "").replace("城", "").strip()

# ---- 主城判定表（N:1 行：一织丰行收多座 v2 城 → 哪座是主城）----
# city_idx(全局) → v2 城基本信息（name_official 以任意时代为准；改名只在一个 label）
v2_index = {}
for era, rows2 in v2_data.items():
    for v in rows2:
        k = (v["type"], v["city_idx"])
        # 只记一次（改名期不同 name_official：保留 1554（初名）作为主判定基名 + 全量 history 并集）
        if k not in v2_index:
            v2_index[k] = {"types": [], "names": set(), "kana": v.get("kana4", "")}
        v2_index[k]["types"].append(era)
        v2_index[k]["names"].add(v["name_official"] or "")
        if v.get("name_history"):
            v2_index[k]["names"].add(v["name_history"])

# 行 ← 候选 v2 城（跨时代并集；每行候选 = 命中的 city_idx 集）
row_cand = collections.defaultdict(set)
for k, v in v2_index.items():
    hits = []
    for nm in v["names"]:
        nm = nm.strip()
        if not nm:
            continue
        for cn in name2rows.get(nm, []):
            if cn not in hits:
                hits.append(cn)
    v2_index[k]["rows"] = hits
    for cn in hits:
        row_cand[cn].add(k)

def principal_of(cn, cand_idxs):
    """某织丰行收到的多座 v2 城中，主城 = 任一官方名归一（去城尾）== 行名归一的唯一者。"""
    m = [c for c in cand_idxs if any(_norm(x) == _norm(cn) for x in v2_index[c]["names"])]
    return m[0] if len(m) == 1 else None

# ---- R2v / R町砦：直写 ----
written_era = collections.defaultdict(set)     # era -> set(行名)
for era in ERAS:
    for v in v2_data[era]:
        vtype, cidx = v["type"], v["city_idx"]
        key = (vtype, cidx)
        info = v2_index[key]
        hits = info["rows"]
        if vtype == "城":
            tag = "R2v主链"
        elif vtype in ("町", "砦"):
            tag = "R町砦直接"
        else:
            continue
        if len(hits) == 0:
            cnt["无织丰行-" + vtype] += 1
            continue
        if len(hits) > 1:
            cnt["多行命中-" + vtype] += 1
            pending.append((era, v["lord_name"], cidx, "v2多行命中", v["soldiers"], "name_history命中 %s" % hits))
            continue
        cn = hits[0]
        row = row_by_cn[cn]
        # N:1 — 本行还可能有其他 v2 城：只有主城写主行列（判定只看城型候选；町/砦同名不干扰）
        opp = row_cand.get(cn, set())
        opp_city = {k for k in opp if k[0] == "城"}
        # 町/砦与城同名命中同一行：城为主（町=城下町，非据点城），町/砦不占城行
        if vtype != "城" and opp_city:
            cnt["町砦让位城行"] += 1
            continue
        if len(opp_city) > 1:
            if key == principal_of(cn, opp_city):
                tag = "R2v主链(N1主)"
            else:
                cnt["N1并入城记录"] += 1
                pending.append((era, v["lord_name"], key, "N1并入城主记录", v["soldiers"],
                                "织丰行 %s 收多城；并入城记录备查（行=主城 %s）" % (cn, principal_of(cn, opp_city))))
                continue
        # 城主名 → StringId（町/砦 lord 空 = 无主商栈/砦，正常，不报错）
        lord_nm = (v["lord_name"] or "").strip()
        if not lord_nm:
            cnt["无主町砦"] += 1
            continue
        lids = hero_by_cn.get(lord_nm, [])
        if not lids:
            cnt["城主名无hero"] += 1
            pending.append((era, v["lord_name"], cidx, "城主名无织丰hero", v["soldiers"], cn))
            continue
        if len(lids) > 1:
            cnt["城主名多hero"] += 1
            pending.append((era, v["lord_name"], cidx, "城主名多hero", v["soldiers"], "%s -> %s" % (cn, lids)))
            continue
        if row[ci[era][0]]:      # 已填（幂等保护）——第一轮不会发生
            cnt["已填跳过"] += 1
            continue
        if cn in ("浦户", "佐仓", "京"):
            print("[DBG] %s 写入 %s <- v2城%s(%s) lord=%s lids=%s" % (era, cn, key, v["name_official"], lord_nm, lids))
        row[ci[era][0]] = lids[0]
        row[ci[era][1]] = str(v["soldiers"])
        written_era[era].add(cn)
        cnt[tag] += 1
    print(f"[{era}] R2v/R町砦 直写完成 累计写 {len(written_era[era])} 行")

# v2 城命中行集（有 v2 记录 = 有正经城主数据——不许借邻居；无hero的留空等补造）
v2_hit_rows = set()
for k, v in v2_index.items():
    for cn in v["rows"]:
        v2_hit_rows.add(cn)

# ---- R8v 反向继承（收窄：仅仍空的城级行；借邻居=同文化最近已配城）----
rev_cnt = 0
for era in ERAS:
    anchors = [r for r in rows[1:]
               if not r[idx_id].startswith("village_")
               and (r[ci[era][0]] or "").strip() and r[idx_id] in xy_cul]
    cands = [r for r in rows[1:]
             if not r[idx_id].startswith(("village_", "castle_village_"))
             and not (r[ci[era][0]] or "").strip()
             and not (r[idx_tk5id] or "").strip()
             and r[idx_cn] not in v2_hit_rows]     # 有 v2 记录的城不借邻居（无hero也留空等补造）
    for c in cands:
        cd = xy_cul.get(c[idx_id])
        if not cd:
            continue
        best = None
        for a in anchors:
            ad = xy_cul[a[idx_id]]
            if ad[2] != cd[2]:
                continue
            d = math.hypot(ad[0] - cd[0], ad[1] - cd[1])
            if best is None or d < best[0]:
                best = (d, a)
        if best:
            c[ci[era][0]] = best[1][ci[era][0]]
            rev_cnt += 1

# ---- R7 村庄继承（2026-09-01 用户裁定：父城 = XML 字段 bound（527/527 Village 组件全带）；
#       village_*（164，bound→town 镇村）与 castle_village_*（363，bound→castle 堡村）同用该字段；
#       ID 前缀仅命名约定，经交叉验证与 bound 100% 一致，代码只认字段）----
v2p = {}
for m in re.finditer(r'<Settlement id="(village_[^"]+)"[^>]*>.*?bound="Settlement\.([^"]+)"[^>]*/>',
                     open(SETTLE_XML, encoding="utf-8").read(), re.S):
    v2p[m.group(1)] = m.group(2)
for m in re.finditer(r'<Settlement id="(castle_village_[^"]+)"[^>]*>.*?bound="Settlement\.([^"]+)"[^>]*/>',
                     open(SETTLE_XML, encoding="utf-8").read(), re.S):
    v2p[m.group(1)] = m.group(2)
v_inh = 0
for era in ERAS:
    for r in rows[1:]:
        pid = v2p.get(r[idx_id])          # 只认字段（village_* / castle_village_* 全部覆盖）
        if not pid or (r[ci[era][0]] or "").strip():
            continue
        for pr in rows[1:]:
            if pr[idx_id] == pid and (pr[ci[era][0]] or "").strip():
                r[ci[era][0]] = pr[ci[era][0]]
                v_inh += 1
                break

# ---- 写回 ----
with io.open(FN, "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f, lineterminator="\r\n")
    w.writerows(rows)

today = datetime.date.today().isoformat()
out_pending = os.path.join(HERE, "city_pair_pending_%s.md" % today)
with io.open(out_pending, "w", encoding="utf-8") as f:
    f.write("# 城配对待核清单（gen_city_era.py v3 / era_v2 信源，%s）\n\n" % today)
    f.write("| 时代 | 城主 | city_idx | 类型 | 士兵 | 说明 |\n|---|---|---|---|---|---|\n")
    for era, name, cidx, typ, sol, note in pending:
        f.write("| %s | %s | %s | %s | %s | %s |\n" % (era, name, cidx, typ, sol, note))

# ---- 审计：v2 全量回验 ----
ok = err_n = err_s = miss = 0
for era in ERAS:
    for v in v2_data[era]:
        if v["type"] != "城":     # 审计只审城（町=城下町非据点，无主公数据在城行；砦半数据）
            continue
        key = (v["type"], v["city_idx"])
        hits = v2_index[key]["rows"]
        if len(hits) != 1:
            continue
        cn = hits[0]
        row = row_by_cn.get(cn)
        if row is None:
            continue
        # 审计对比仅限"行主城"（N1 并入城城主本就不写入——不相干）
        opp = row_cand.get(cn, set())
        opp_city2 = {k for k in opp if k[0] == "城"}
        if len(opp_city2) > 1 and key != principal_of(cn, opp_city2):
            continue
        mine_o = (row[ci[era][0]] or "").strip()
        mine_s = (row[ci[era][1]] or "").strip()
        lids = hero_by_cn.get((v["lord_name"] or "").strip(), [])
        if not mine_o:
            miss += 1
        elif not lids or mine_o not in lids:
            err_n += 1
        elif mine_s != str(v["soldiers"]):
            err_s += 1
        else:
            ok += 1
print("\n=== 汇总 ===")
print("R2v主链:", cnt["R2v主链"], "+N1主:", cnt["R2v主链(N1主)"], "| 町砦:", cnt["R町砦直接"],
      "| N1并入记录:", cnt["N1并入城记录"], "| 反向继承:", rev_cnt, "| 村继承:", v_inh,
      "| 待核:", len(pending), "条")
print("审计（v2 全量回验）: 对(名+兵)=%d | 错名=%d | 兵错=%d | 漏=%d" % (ok, err_n, err_s, miss))
print("分类: ", dict(cnt))

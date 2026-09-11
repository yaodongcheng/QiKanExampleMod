#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""太阁英雄 ID 重编器 v2 —— 以 **DX 人物番号** 为主编号
============================================================================
用户裁定（2026-09-11）
--------------------
  · **主编号 = DX 人物番号** → Hero StringId = `lord_tk5_<DX号>`
  · 新增一列 `原版编号`（原版人物番号）
  · 保留 `外观ID` 列（= BUSTUP 立绘槽号 = 世界表 pid，用于 join 六年代剧本数据）
  · 模板 id / 变量 id **不能按人物编号编**，特殊处理（保留原 ID，编号列留空）

三套编号是什么（详见 memory: taikou5-three-id-systems）
-------------------------------------------------------
  DX人物番号    = DX 游戏 `人物Ａ.人物番號`；范围 0-1303
  原版人物番号  = 原版游戏同上；范围 0-1099（本机）
  外观ID        = BUSTUP 目录「编号_姓名」槽号，1292 槽；**0-859 与人物番号重合，860 起分道**
  实例：长谷川宗仁 = 原版1000 / DX1162 / 外观ID1030

编号怎么定的（每行都有依据，不是推断）
--------------------------------------
  A. 行 0-961  → **DX号 = 行号**。918 行直接对上 DX 日志；剩下 59 行经逐条核对**全是同人异名**
     （秋田實季/安東實季、島左近/島清興、豐臣秀吉/木下藤吉郎、北田具教/北畠具教…），槽位没错。
  B. 行 962-1042 → DX号 从 DX 日志按名反查（这批是追加段：南蛮人/文化人/女性，原版号 1000-1056、
     DX号 1162-1243、外观ID 1030-1103 三条线各走各的）。
  C. 行 1043+  → `template_*`（68，通用 NPC 模板）/ `pronoun_*`（6，变量：主人公/發生人物…），
     **不是人物对象，不参与编号**，保留原 ID。

🔴 别名重复行（同一人两行，碰撞即报）
   蒲生賴鄉/蒲生鄉舍、長阪釣閑/長阪長閑、三好為三/三好政勝、三好宗渭/三好政康
   —— 两行指向同一个 DX 槽。**不自动合并**（合不合是数据决策），后一行 ID 加 `_alt` 后缀并
   在报告里点名，等用户裁定。

按名字反查前必须过 `tk5_pua_names.restore()` —— DX 日志有 37 条名字写着 Unicode 私用区码点
（誾千代 写作 <E41D>千代），不过这道必然对不上。

用法
----
  python Scripts/renumber_taikou_hero_ids.py --dry-run     # 出映射报告 + 碰撞清单
  python Scripts/renumber_taikou_hero_ids.py --apply       # 写 CSV + 同步全链引用
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
from tk5_pua_names import restore          # noqa: E402

try:
    import opencc
    _CC = opencc.OpenCC("t2s")
except Exception:                                            # noqa: BLE001
    _CC = None

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应")
CSV_PATH = os.path.join(BASE, "csv", "TaikouHero.csv")
LOG_DIR = r"E:\TKHACK\log"
LINE_RE = re.compile(r"Log: (\d+)\|(.*)$")

# 🔴 别名重复行：同一太阁人物在 CSV 里占了两行（两个名字各一行）。
#    2026-09-11 用两版实机日志逐条核实（DX 日志在该槽显示的就是本行的人，只是写法不同）：
#      行960 三好政勝 → DX705（行705 三好為三；DX[705] 显示「三好为三」）
#      行961 三好政康 → DX706（行706 三好宗渭；DX[706] 显示「三好宗渭」）
#      行970 蒲生鄉舍 → DX243（行243 蒲生賴鄉；DX[243] 显示「蒲生赖乡」）
#      行973 長阪長閑 → DX530（行530 長阪釣閑；DX[530] 显示「长坂钓闲」）
#      行906 河野通直   → DX300（行300 河野通直牛福丸）🔴 2026-09-11 用户抓漏补入：
#             两行 **原版编号都是 300、外观ID 单元都是 `300|1234`**，DX 里也确实有两槽
#             （DX[300]=河野牛福丸 / DX[906]=河野通直）。当初漏检是因为两行都在 0-959 段、
#             按「DX号 = 行号」各拿了 300 与 906，**撞不上** →
#             故新增 `check_dup_pairs()` 以「原版编号相同」为第二道闸门兜住这类。
#   🔴 2026-09-11 后续：其余 4 组已**合并**（重复行数据全空，名字并入主行 Alias 后删行，
#      见 `merge_taikou_hero_alias_rows.py`）。
#   🔴 行 906 河野通直 / 行 300 河野牛福丸 **也不是重复行** —— 用户裁定为**父子**
#      （河野家父子同名「通直」，牛福丸是儿子幼名）。实机两版日志铁证：
#      原版[300]=河野通直(父) / DX[300]=河野牛福丸(子) / DX[906]=河野通直(父)，
#      且「牛福丸」在原版**完全不存在** → **两版的 300 号不是同一人**（父子互换）。
#      已由 `fix_kouno_father_son.py` 分号：子=lord_tk5_300（外观1234）、父=lord_tk5_906（外观300）。
#   ⇒ 至此**别名重复表为空** —— 全表再无「同一人占两行」。
ALIAS_DUP_ROWS = {}

# 🔴 原版号**刻意留空**的行（不参与「行 0-799 取行号」的规则）。
#    行 300 = 河野牛福丸（子）：原版人物表里**没有这个人**（原版[300] 是父 河野通直），
#    故不能占 300 这个号 —— 留空才对。
ORIG_NO_BLANK_ROWS = {300}

# 行号即 DX 号的段上界（含）。依据见文件头 A。
# 🔴 2026-09-11 实测修正：上界是 **959**，不是 961 —— DX 槽 960/961 放的是**玩家自建新武将**
#    （日志里是「姚东成」「徐炜铃」这种中文名），行 960/961（三好政勝/三好政康）其实与
#    行 705/706（三好為三/三好宗渭）是同一人 → 走别名重复分支。
ROW_EQ_DX_MAX = 959
# 人物行的上界（含）：960-1042 是追加段，1043 起是模板/变量
PERSON_MAX = 1042
# 原版号 == 行号 的段上界（含）：0-799 两版三套完全一致（原版日志在该段显示的是当年龄名，
# 如 506 显示「松平元康」而非「德川家康」，按名字反查会漏 —— 故这一段直接取行号）
ROW_EQ_ORIG_MAX = 799
# 非人物前缀
NON_PERSON = ("template_", "pronoun_", "prounon")

COL_ID, COL_ORIG, COL_APPEAR = "ID", "原版编号", "外观ID"


def norm(s):
    s = restore((s or "").strip())
    s = re.sub(r"[（(].*?[)）]", "", s)                      # 去括号别名
    s = re.sub(r"[\s　・･、,，]+", "", s)
    return _CC.convert(s) if _CC else s


def load_log(fn):
    p = os.path.join(LOG_DIR, fn)
    d = collections.defaultdict(list)
    if not os.path.isfile(p):
        return d
    for raw in io.open(p, encoding="utf-8", errors="replace"):
        m = LINE_RE.search(raw.rstrip("\r\n"))
        if m and m.group(2).strip():
            d[norm(m.group(2))].append(m.group(1) and int(m.group(1)))
    return d


def check_dup_pairs(rows):
    """第二道闸门：扫「同一人占两行」的漏网（不依赖 ID 撞号）。

    🔴 2026-09-11 用户抓漏后补（行 300/906 河野通直）：原先只在两行算出**同一个 DX 号**时
       才报重复 —— 而两行都在 0-959 段时按「DX号 = 行号」各拿各的号，**永不撞号**，这类必漏。
       故改用**数据本身的强信号**，两个扫描各自独立跑、按行集合去重：

         ① 两行 `原版编号` 相同   ← 最干净（一个人在原版只有一个号）
         ② 两行（都是人物行）`外观ID` 单元格完全相同 ← 立绘槽都同，必是同一人

       模板行（template_*）**排除在 ②外** —— 一堆模板共用同一立绘槽是正常的
       （13 个身份模板都指 1121、4 个水军模板都指 1012）。
    """
    persons = [(i, r) for i, r in enumerate(rows) if not r["ID"].startswith(NON_PERSON)]

    def grouped(col):
        d = collections.defaultdict(list)
        for i, r in persons:
            v = (r.get(col) or "").strip()
            if v:
                d[v].append(i)
        return [(v, idxs) for v, idxs in d.items() if len(idxs) > 1]

    out, seen = [], set()
    for label, col in (("原版编号相同", "原版编号"), ("外观ID相同", "外观ID")):
        for v, idxs in grouped(col):
            key = tuple(sorted(idxs))
            if key in seen:
                continue
            seen.add(key)
            out.append((f"{label}={v}", idxs))
    return out


def row_keys(r):
    """该行的全部可用名（归一化）—— CNName + Alias 各段。

    🔴 2026-09-11：`ScriptName` 列已删（繁体名并入 Alias），故取键一律走这两列。
    """
    out = []
    for src in [r.get("CNName", "")] + (r.get("Alias") or "").split("|"):
        k = norm(src)
        if k:
            out.append(k)
    return out


def build_map(rows):
    """→ (map[row]={dx,orig,appear}, problems, alias_dups, unresolved)"""
    dx = load_log("DX角色ID.log")
    og = load_log("原版角色ID.log")
    res, alias_dups, unresolved = {}, [], []
    taken = {}

    for i, r in enumerate(rows):
        if r["ID"].startswith(NON_PERSON):
            continue
        disp = (r.get("CNName") or "").strip()            # 显示用名（ScriptName 已删）
        keys = row_keys(r)

        # 别名重复行：DX 槽 = 主行的槽（表见 ALIAS_DUP_ROWS）
        if i in ALIAS_DUP_ROWS:
            owner = ALIAS_DUP_ROWS[i]
            if owner not in res:
                unresolved.append((i, r["ID"], disp, f"别名重复的主行 {owner} 未解出"))
                continue
            res[i] = {"dx": res[owner]["dx"], "orig": res[owner]["orig"],
                      "appear": None, "alt": True}
            alias_dups.append((i, r["ID"], disp, res[owner]["dx"],
                               owner, (rows[owner].get("CNName") or "").strip()))
            continue

        dxs = next((dx[k] for k in keys if k in dx), [])
        ogs = next((og[k] for k in keys if k in og), [])

        if i <= ROW_EQ_DX_MAX:
            dxno = i                                      # 规则 A
        elif i <= PERSON_MAX:
            dxno = next((int(x) for x in sorted(set(dxs))), None)   # 规则 B
            if dxno is None:
                unresolved.append((i, r["ID"], disp, "DX 日志查不到（追加段）"))
                continue
        else:
            continue

        if i in ORIG_NO_BLANK_ROWS:
            orig = None                                   # 刻意留空（见该常量注释）
        elif i <= ROW_EQ_ORIG_MAX:
            orig = i                                      # 0-799 两版同号，直接取行号
        else:
            orig = next((int(x) for x in sorted(set(ogs))), None)

        appear_raw = (r.get(COL_APPEAR) or "").strip()
        appear = next((int(x) for x in appear_raw.split("|") if x.strip().isdigit()), None)

        if dxno in taken:
            alias_dups.append((i, r["ID"], disp, dxno,
                               taken[dxno][0], taken[dxno][1]))
            res[i] = {"dx": dxno, "orig": orig, "appear": appear, "alt": True}
        else:
            taken[dxno] = (i, disp)
            res[i] = {"dx": dxno, "orig": orig, "appear": appear, "alt": False}
    return res, alias_dups, unresolved


def new_id(info):
    return "lord_tk5_%d%s" % (info["dx"], "_alt" if info["alt"] else "")


def sync_chain(old2new, module):
    """把 旧id → 新id 同步到「运行期真正会读」的文件。

    只改这组（同上一轮口径）：TaikouHero.csv 已单独处理，这里管
      · ModuleData/AssetRegistry/ProfileStages.csv（立绘表 StringId 列）
      · ModuleData 下 6 个 XML：taikou_heroes*.xml / spnpccharacters.xml /
        spclans.xml / spkingdoms.xml / spcultures.xml
    替换**长串优先**：`lord_tk5_195` 是 `lord_tk5_1950` 的前缀，短串先替会串号。
    """
    order = sorted(old2new, key=len, reverse=True)
    targets = [os.path.join(module, "ModuleData", "AssetRegistry", "ProfileStages.csv")]
    md = os.path.join(module, "ModuleData")
    for fn in sorted(os.listdir(md)):
        if fn.endswith(".xml") and fn[:-4] in (
                "taikou_heroes", "taikou_heroes_1582", "spnpccharacters",
                "spclans", "spkingdoms", "spcultures"):
            targets.append(os.path.join(md, fn))
    report = []
    for p in targets:
        if not os.path.isfile(p):
            continue
        raw = io.open(p, encoding="utf-8-sig", newline="").read()
        out, n = raw, 0
        for old in order:
            if old in out:
                n += out.count(old)
                out = out.replace(old, old2new[old])
        if out != raw:
            io.open(p, "w", encoding="utf-8-sig", newline="").write(out)
        report.append((os.path.basename(p), n))
    return report


def scan_stale_in_scripts(old2new):
    """扫 Scripts/*.py 里是否还留着旧 id（硬编码表最容易漏）。

    🔴 2026-09-11 实锤：上一轮就漏了 gen_taikou_era_diff.DROP_HEROES —— 7 个旧 id 留在
       生成器里，重跑后 1582 差异段从「只留 lord_g」变成「全量 7 人」，
       靠 check_hero_profile_keys 报「世界有但目录没有」才抓到。检查必须脚本化。
    """
    here = os.path.dirname(os.path.abspath(__file__))
    me = os.path.basename(__file__)
    hits = []
    for fn in sorted(os.listdir(here)):
        if not fn.endswith(".py") or fn == me:
            continue
        raw = io.open(os.path.join(here, fn), encoding="utf-8", errors="replace").read()
        for ln, line in enumerate(raw.splitlines(), 1):
            for old in old2new:
                if old in line:
                    hits.append((fn, ln, old, line.strip()[:88]))
                    break
    return hits


def resolve_module(args):
    if sys.platform == "win32":
        import winreg
        mb2 = None
        for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                          (winreg.HKEY_LOCAL_MACHINE,
                           r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
            try:
                with winreg.OpenKey(hive, sub) as k:
                    mb2 = winreg.QueryValueEx(k, "MB2_PATH")[0] or mb2
            except OSError:
                continue
        return args.module or (os.path.join(mb2, "Modules", "Taikou") if mb2 else None)
    return args.module


def sync_chain_and_report(old2new, args, rows):
    module = resolve_module(args)
    if not module or not os.path.isdir(os.path.join(module, "ModuleData")):
        print(f"[FATAL] 找不到内容包 ModuleData：{module}", file=sys.stderr)
        return 2

    print()
    print(f"=== 同步全链引用（{len(old2new)} 条映射）===")
    for name, n in sync_chain(old2new, module):
        print(f"  {name:<28} 替换 {n} 处")

    stale = scan_stale_in_scripts(old2new)
    if stale:
        print()
        print(f"🔴 Scripts/ 里仍残留 {len(stale)} 处旧 id（硬编码表，必须手工改）：")
        for fn, ln, old, txt in stale:
            print(f"   {fn}:{ln}  [{old}]  {txt}")
    else:
        print("Scripts/ 无旧 id 残留 ✓")
    print()
    print("下一步：重跑生成器（gen_taikou_hero_profiles / gen_taikou_hero_catalog /")
    print("        gen_taikou_era_diff / gen_taikou_english_strings / sync_taikou_bio_cns）")
    print("        → 再跑 Scripts/run_all_checks.py")
    return 0


def main():
    ap = argparse.ArgumentParser(description="Taikou hero id renumbering v2 (DX 主编号)")
    ap.add_argument("--apply", action="store_true", help="写回（默认只报告）")
    ap.add_argument("--dry-run", action="store_true", help="只报告不写（默认行为，显式写出来便于阅读）")
    ap.add_argument("--sync-chain", action="store_true",
                    help="只做全链引用同步（CSV 已改名、但模块引用没跟上时用；"
                         "按上一轮编号规则重算旧 id → 新 id）")
    ap.add_argument("--module", default=None, help="内容包目录")
    args = ap.parse_args()

    if not _CC:
        print("[FATAL] 需要 opencc 做繁→简归一，无法比对名字：pip install opencc")
        return 2

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    res, alias_dups, unresolved = build_map(rows)
    n_person = len(res)

    # ── 第二道闸门：同一人占两行的漏网普查（见 check_dup_pairs 文档）──
    covered = {frozenset((k, v)) for k, v in ALIAS_DUP_ROWS.items()}
    uncovered = []
    for label, idxs in check_dup_pairs(rows):
        pair = frozenset(idxs)
        if pair in covered:
            continue
        uncovered.append((label, idxs))

    # ── --sync-chain：CSV 已改名、模块引用没跟上时用 ──
    #    旧 id 按**上一轮编号规则**重算：行 0-859 → `lord_tk5_<行号>`；
    #    行 860-1042 → `lord_tk5_<外观ID 首段>`（上一轮误用了外观ID 系统）。
    if args.sync_chain:
        old2new = {}
        for i, r in enumerate(rows):
            if r["ID"].startswith(NON_PERSON):
                continue
            if i <= 859:
                old = "lord_tk5_%d" % i
            else:
                a = (r.get(COL_APPEAR) or "").split("|")[0].strip()
                if not a.isdigit():
                    continue
                old = "lord_tk5_%s" % a
            if old != r["ID"]:
                old2new[old] = r["ID"]
        print(f"--sync-chain：重算上一轮旧 id {len(old2new)} 条 → 同步模块引用")
        return sync_chain_and_report(old2new, args, rows)

    print(f"TaikouHero {len(rows)} 行：人物行 {n_person}，非人物（模板/变量）{len(rows) - n_person}")

    seg = collections.Counter()
    for i in res:
        seg["A 0-961" if i <= ROW_EQ_DX_MAX else "B 962-1042"] += 1
    print("  " + "  ".join(f"{k}={v}" for k, v in sorted(seg.items())))

    # 抽样核对
    print()
    print("=== 抽样（行 → 新 ID ← 现 ID | 原版号/外观ID | 名字）===")
    for i in (0, 14, 195, 300, 506, 517, 800, 860, 961, 962, 1000, 1008, 1030, 1042):
        if i in res:
            x = res[i]
            print(f"  行{i:>4} {new_id(x):<18} ← {rows[i]['ID']:<28} "
                  f"原版={str(x['orig']):<6} 外观={str(x['appear']):<6} {rows[i].get('CNName','')}")

    if unresolved:
        print()
        print(f"=== 未解出 {len(unresolved)} 行（保持原 ID）===")
        for i, cid, nm, why in unresolved:
            print(f"  行{i:>4} {cid:<28} {nm:<14} —— {why}")

    if alias_dups:
        print()
        print(f"=== 🔴 别名重复 {len(alias_dups)} 行（同一 DX 槽两行，需你裁定合不合并）===")
        for i, cid, nm, dxno, owner_i, owner_nm in alias_dups:
            print(f"  行{i:>4} {cid:<28} {nm:<14} 的 DX{dxno} 已被行{owner_i}（{owner_nm}）占用 "
                  f"→ 本行 ID 加 _alt 后缀")

    changed = sum(1 for i, x in res.items() if rows[i]["ID"] != new_id(x))
    print()
    print(f"需要改 ID 的行：{changed} / {n_person}")

    if uncovered:
        print()
        print(f"🔴 [未覆盖] {len(uncovered)} 组「同一人占两行」没被 ALIAS_DUP_ROWS 登记：")
        for label, idxs in uncovered:
            who = [f"行{i} {rows[i]['ID']} {rows[i]['CNName']}" for i in idxs]
            print(f"   [{label}] " + " | ".join(who))
        print("   → 请核实后在 ALIAS_DUP_ROWS 登记（后一行会拿 _alt 后缀），否则两行会各拿一个 ID")
        return 1

    if not args.apply:
        print("\n未写文件（加 --apply 才写回）。")
        return 0

    # 🔴 old2new 必须在**改写之前**算 —— 2026-09-11 踩过：先改 rows[i][ID] 再算，
    #    两边都成新值 → 映射恒为空 → 全链同步静默不发生（"无需同步引用"是假象）。
    old2new = {rows[i]["ID"]: new_id(x) for i, x in res.items() if rows[i]["ID"] != new_id(x)}
    print(f"需同步的引用映射：{len(old2new)} 条")

    # ── 写 CSV ──
    if COL_ORIG in cols:
        cols.remove(COL_ORIG)
    for r in rows:
        r.pop(COL_ORIG, None)

    for i, x in res.items():
        rows[i][COL_ID] = new_id(x)
        rows[i][COL_ORIG] = "" if x["orig"] is None else str(x["orig"])
    for i, r in enumerate(rows):
        if i not in res:
            r[COL_ORIG] = ""                       # 模板/变量行留空

    # 原版编号 紧跟 ID 之后（可读性）
    out_cols = [COL_ID, COL_ORIG] + [c for c in cols if c != COL_ID]
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=out_cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in out_cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print(f"[FATAL] 往返行数不符 {len(back)} != {len(rows)}")
        return 1
    for i, (a, b) in enumerate(zip(rows, back)):
        for c in cols:
            if c == COL_ID:
                continue
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致 行{i} 列{c!r}: {a.get(c)!r} vs {b.get(c)!r}")
                return 1
    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print(f"已写回 CSV（{len(out_cols)} 列，新增「{COL_ORIG}」）")

    return sync_chain_and_report(old2new, args, rows)



if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""Settlements.csv 补「国 / 地」两列（`Kuni` / `Chi`）—— 太阁5 据点日志 → 据点表
============================================================================
**数据源**：`Knowledge/太阁5/太阁日志/据点日志.md` 的
`Log: SET|<年>|<序号>|<类型>|<名>|当主:…|势力:…|国:<令制国>|地:<地域>|…`
—— 274 据点 × 6 剧本，**地理属性六年零冲突**（城不改属国，实测 0 例）。

**为什么加这两列**：据点文化（`settlements.xml` 的 `culture`）要按「地」定 ——
太阁5 的 10 个地域 ↔ Taikou 9 个地域文化（映射表在消费方，本脚本只落数据）：
    九州→saikai · 四国→nankai · 中部（山阴山阳）→sanyo · 近畿→kinai · 东北→ou
    关东→kanto · 东海→tokai · 北陆→hokuriku · 甲信→tosan
    海外（釜山/宁波/那霸/吕宋 4 町）不在 mod 世界内 → 无据点、不参与。
「国」（66 个令制国）留作更细粒度备用（将来按国分兵种/名字池）。

**匹配**：**日志 `序号` − 1 = 我们的 tk 编号**（与 `gen_taikou_settlement_owner.py` 同款对位，274/274）；
坐标（日志 `横`/`纵` ↔ 表 `TK5_X`/`TK5_Y`）作**交叉验证**——不一致时打印但不阻断：
已知例外 = 海外 4 町（釜山/宁波/那霸/吕宋，CSV 坐标与日志差几格，序号仍准）。**不用名字**（名字随年代改）。

**纪律**：①幂等（重跑结果一致）②往返校验 ③值内禁半角逗号与竖线（铁律 24）
④`Settlements.csv` 是生成物（铁律 22）→ **本脚本即这两列的生成器**；
  ⚠️ `_analysis/gen_settlements_csv.py` 的 HEADER 里没有这两列，**重跑它会丢列**，
     须随后重跑本脚本回填（已在该脚本头部登记）。

Usage:
  python Scripts/import_settlement_kuni_chi.py            # 报告（不写盘）
  python Scripts/import_settlement_kuni_chi.py --apply    # 落盘
  python Scripts/import_settlement_kuni_chi.py --check    # 校验产物（进一键体检）
Exit: 0 正常 / 1 有硬问题 / 2 fatal。
"""
import argparse
import collections
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from csv_dual import read_table, write_table  # noqa: E402

CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
SETT = os.path.join(CSV_DIR, "Settlements.csv")
LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "据点日志.md")

KUNI_CN, KUNI_EN = "国", "Kuni"
CHI_CN, CHI_EN = "地", "Chi"
ANCHOR = "TK5_Y"                     # 两列插在它之后（坐标 → 地理属性，语义相邻）
BAD_CHARS = (",", "|")               # 铁律 24：值内禁半角逗号；单值列不许带竖线


def parse_log():
    """→ ({tk号: (国, 地)}, {(横, 纵): (国, 地)})。

    对位 = 日志 `序号` − 1（city_idx 口径）；坐标字典供交叉验证。
    同号/同坐标跨年冲突 → 硬错误（地理属性不该随剧本变）。
    """
    if not os.path.isfile(LOG):
        print("[FATAL] 日志不存在：%s" % LOG)
        sys.exit(2)
    by_no = collections.defaultdict(lambda: {"kuni": set(), "chi": set()})
    by_xy = collections.defaultdict(lambda: {"kuni": set(), "chi": set()})
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
        try:
            tk_no = int(parts[2]) - 1
        except ValueError:
            continue
        kuni, chi = kv.get("国", ""), kv.get("地", "")
        by_no[tk_no]["kuni"].add(kuni)
        by_no[tk_no]["chi"].add(chi)
        x, y = kv.get("横", ""), kv.get("纵", "")
        if x and y:
            by_xy[(x, y)]["kuni"].add(kuni)
            by_xy[(x, y)]["chi"].add(chi)

    conflict = {k: v for k, v in list(by_no.items()) + list(by_xy.items())
                if len(v["kuni"]) > 1 or len(v["chi"]) > 1}
    if conflict:
        print("[ERROR] %d 个键的国/地跨年代不一致（应零冲突）：" % len(conflict))
        for c, v in list(conflict.items())[:10]:
            print("   %s → 国%s 地%s" % (c, sorted(v["kuni"]), sorted(v["chi"])))
        sys.exit(1)

    def flat(d):
        out = {}
        for k, v in d.items():
            kuni = sorted(v["kuni"])[0] if v["kuni"] else ""
            chi = sorted(v["chi"])[0] if v["chi"] else ""
            if not kuni or not chi:
                print("[ERROR] %s 缺国或地（日志字段缺失）" % (k,))
                sys.exit(1)
            out[k] = (kuni, chi)
        return out

    return flat(by_no), flat(by_xy)


def tk_no_of(sid):
    """`town_tk049` → 49；不合形状 → None。"""
    m = re.match(r"^(?:town|village|castle)_tk(\d+)$", (sid or "").strip())
    return int(m.group(1)) if m else None


def load_table():
    cn, en, rows = read_table(SETT, head=2)
    if not en:
        print("[FATAL] 读不到表头：%s" % SETT)
        sys.exit(2)
    return cn, en, rows


def ensure_cols(cn, en, rows):
    """两列不存在 → 插到 TK5_Y 之后（中文标签行同步插）。→ (cn, en, rows, 是否新加)"""
    if KUNI_EN in en and CHI_EN in en:
        return cn, en, rows, False
    if ANCHOR not in en:
        print("[FATAL] 表里找不到锚点列 %s" % ANCHOR)
        sys.exit(2)
    i = en.index(ANCHOR) + 1
    cn = cn[:i] + [KUNI_CN, CHI_CN] + cn[i:]
    en = en[:i] + [KUNI_EN, CHI_EN] + en[i:]
    rows = [r[:i] + ["", ""] + r[i:] for r in rows]
    return cn, en, rows, True


def fill(rows, en, per_no, per_xy):
    """按 tk 编号填两列 → (rows, miss 列表, 改动格数, 坐标不符列表)"""
    ii, xi, yi = en.index("id"), en.index("TK5_X"), en.index("TK5_Y")
    ki, ci = en.index(KUNI_EN), en.index(CHI_EN)
    miss, changed, xy_bad = [], 0, []
    for r in rows:
        while len(r) < len(en):
            r.append("")
        sid = (r[ii] or "").strip()
        no = tk_no_of(sid)
        v = per_no.get(no) if no is not None else None
        if not v:
            miss.append((sid, no))
            continue
        xy = ((r[xi] or "").strip(), (r[yi] or "").strip())
        if per_xy.get(xy) != v:
            xy_bad.append((sid, xy, per_xy.get(xy), v))
        if r[ki] != v[0] or r[ci] != v[1]:
            changed += 1
        r[ki], r[ci] = v[0], v[1]
    return rows, miss, changed, xy_bad


def check_bad_chars(rows, en):
    ki, ci = en.index(KUNI_EN), en.index(CHI_EN)
    bad = []
    for r in rows:
        for i in (ki, ci):
            v = r[i] if i < len(r) else ""
            if any(ch in v for ch in BAD_CHARS):
                bad.append((r[0], en[i], v))
    return bad


def cmd_check():
    """校验产物（进一键体检）：两列在、274 行非空、与日志一致。"""
    per_no, per_xy = parse_log()
    cn, en, rows = load_table()
    problems = []
    if KUNI_EN not in en or CHI_EN not in en:
        problems.append("缺列 %s/%s（跑本脚本 --apply 回填）" % (KUNI_EN, CHI_EN))
    else:
        _, _, rows, _ = ensure_cols(cn, en, rows)
        rows, miss, _, xy_bad = fill(rows, en, per_no, per_xy)
        for sid, no in miss:
            problems.append("据点 %s（tk号 %s）在日志里查不到" % (sid, no))
        for sid, xy, logv, byno in xy_bad:
            # 序号对位是权威（与 gen_taikou_settlement_owner.py 同口径）；坐标只是交叉验证，
            # 海外 4 町（釜山/宁波/那霸/吕宋）两侧坐标本就不同 → 提示不阻断。
            print("[WARN] 据点 %s 坐标 %s 与日志不符（序号给 %s）—— 海外町属已知" % (sid, xy, byno))
        for sid, col, v in check_bad_chars(rows, en):
            problems.append("据点 %s 的 %s 值 %r 含禁用字符（铁律 24）" % (sid, col, v))
    if problems:
        print("[ERROR] Kuni/Chi 列体检不通过：")
        for p in problems[:20]:
            print("   " + p)
        if len(problems) > 20:
            print("   … 共 %d 条" % len(problems))
        return 1
    dist = collections.Counter((r[en.index(CHI_EN)] if len(r) > en.index(CHI_EN) else "")
                               for r in rows)
    print("[ OK ] Kuni/Chi 已回填：%d 行；地分布 %s" % (len(rows), dict(dist.most_common())))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="落盘（缺省只报告）")
    ap.add_argument("--check", action="store_true", help="校验产物（不写盘）")
    # 一键体检统一传 `--module <路径>`（雷 92：新脚本必须接，不接 = argparse exit 2 假红）。
    # 本脚本只读 CSV/日志、不碰模块目录 → 收下即忽略。
    ap.add_argument("--module", default=None, help="忽略（兼容一键体检调用约定）")
    args = ap.parse_args()

    if args.check:
        return cmd_check()

    per_no, per_xy = parse_log()
    cn, en, rows = load_table()
    cn, en, rows, added = ensure_cols(cn, en, rows)
    rows, miss, changed, xy_bad = fill(rows, en, per_no, per_xy)

    print("日志据点 = %d；表行数 = %d" % (len(per_no), len(rows)))
    print("列：%s（%s / %s）%s" % ("新增" if added else "已存在", KUNI_EN, CHI_EN,
                                  "← 插在 %s 之后" % ANCHOR if added else ""))
    print("填值：改动 %d 行；未命中 %d 行" % (changed, len(miss)))
    if miss:
        print("[ERROR] 以下据点在日志里查不到（不静默跳过）：")
        for sid, no in miss[:20]:
            print("   %s (tk号 %s)" % (sid, no))
        return 1
    if xy_bad:
        print("[WARN] %d 个据点坐标与日志不符（序号对位仍准；海外 4 町属已知此类）：" % len(xy_bad))
        for sid, xy, logv, byno in xy_bad[:6]:
            print("   %s 表坐标%s → 坐标查得%s；序号查得%s" % (sid, xy, logv, byno))

    bad = check_bad_chars(rows, en)
    if bad:
        print("[ERROR] 值内出现禁用字符（铁律 24）：")
        for sid, col, v in bad[:10]:
            print("   %s %s=%r" % (sid, col, v))
        return 1

    dist = collections.Counter(r[en.index(CHI_EN)] for r in rows)
    kuni_n = len({r[en.index(KUNI_EN)] for r in rows})
    print("地分布：%s" % dict(dist.most_common()))
    print("令制国数：%d" % kuni_n)

    if not args.apply:
        print("\n（未写盘；加 --apply 落盘）")
        return 0

    write_table(SETT, cn, en, rows)

    # 往返校验（用实际写出的内容读回）
    cn2, en2, rows2 = read_table(SETT, head=2)
    assert en2 == en, "表头往返不一致"
    assert len(rows2) == len(rows), "行数往返不一致（%d ≠ %d）" % (len(rows2), len(rows))
    ki, ci = en2.index(KUNI_EN), en2.index(CHI_EN)
    for a, b in zip(rows, rows2):
        assert a[ki] == b[ki] and a[ci] == b[ci], "值往返不一致：%s" % a[0]
    print("\n✅ 已写出 %s（%d 行 / %d 列）；往返读回 %d 行一致" % (SETT, len(rows), len(en), len(rows2)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

# -*- coding: utf-8 -*-
"""把 13_dtcells.json 规范化成可读表 + CSV：
 - 列名剥掉 UDS 的 `_序号_GUID` 尾巴
 - 输出 out/tables/<表名>.csv 与 out/tables/_summary.txt（宽表转置，一行一个法术）
用法: python dt_catalog.py [表名过滤]
"""
import json, io, os, sys, re, csv

from paths import DUMP_ROOT as OUT
TAIL = re.compile(r'_(\d{1,3})_([0-9A-Fa-f]{32})$')


def short(full):
    return TAIL.sub('', full)


def clean(v):
    v = v.strip()
    if v.startswith("(") and v.endswith(")"):
        return v
    return v


def main():
    filt = sys.argv[1] if len(sys.argv) > 1 else None
    d = json.load(io.open(os.path.join(OUT, "13_dtcells.json"), encoding="utf-8"))
    os.makedirs(os.path.join(OUT, "tables"), exist_ok=True)
    for tname, t in sorted(d.items()):
        if filt and filt.lower() not in tname.lower():
            continue
        rows = t["rows"]
        cols = {short(k): v for k, v in t["cells"].items()}
        # CSV：行=法术，列=字段
        with io.open(os.path.join(OUT, "tables", tname + ".csv"), "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f)
            cnames = sorted(cols)
            w.writerow(["Row"] + cnames)
            for i, r in enumerate(rows):
                w.writerow([r] + [(cols[c][i] if i < len(cols[c]) else "") for c in cnames])
        print("%-34s rows=%-4d cols=%-3d %s" % (tname, len(rows), len(cols), ",".join(sorted(cols)[:12])))


main()

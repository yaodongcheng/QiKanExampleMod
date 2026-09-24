# -*- coding: utf-8 -*-
"""从 DataTable/Struct 的 .uasset+.uexp 二进制里挖出「用户定义结构体字段的全名」。
UDS 字段的 FName 形态 = <字段名>_<序号>_<32位GUID>，正是 FindField 需要的键。
产出 columns_by_package.json : { "DT_SpellsInfo": {"full":[...], "bare":[...]}, ... }
"""
import io, os, re, json, sys, collections

from paths import UE_PROJECT as PROJECT
from paths import INVENTORY as INV
from paths import DUMP_ROOT as OUT

FULL = re.compile(rb"([A-Za-z_][A-Za-z0-9_]{0,63})_(\d{1,3})_([0-9A-Fa-f]{32})")


def scan(path):
    try:
        b = open(path, "rb").read()
    except Exception:
        return set()
    return set(m.group(0).decode("ascii") for m in FULL.finditer(b))


def main():
    inv = json.load(io.open(INV, encoding="utf-8"))
    dt = [r for r in inv if r.get("class") == "DataTable"]
    uds = [r for r in inv if r.get("class") == "UserDefinedStruct"]
    out = {"tables": {}, "structs": {}}
    for r in dt:
        pkg = r["path"].replace("/Game/", "")
        p = os.path.join(ROOT, pkg + ".uasset")
        names = scan(p)
        names |= scan(p[:-7] + ".uexp")
        bare = set()
        for n in names:
            m = FULL.match(n.encode("ascii"))
            if m:
                bare.add(m.group(1).decode("ascii"))
        out["tables"][r["name"]] = {"pkg": r["path"], "full": sorted(names), "bare": sorted(bare)}
    for r in uds:
        pkg = r["path"].replace("/Game/", "")
        p = os.path.join(ROOT, pkg + ".uasset")
        names = scan(p)
        names |= scan(p[:-7] + ".uexp")
        out["structs"][r["name"]] = {"pkg": r["path"], "full": sorted(names)}
    json.dump(out, io.open(os.path.join(OUT, "12_dt_columns.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("tables", len(out["tables"]), "structs", len(out["structs"]))
    for k in list(out["tables"])[:5]:
        print(k, len(out["tables"][k]["full"]), out["tables"][k]["full"][:3])
    for k in list(out["structs"])[:5]:
        print("S:", k, len(out["structs"][k]["full"]), out["structs"][k]["full"][:3])


main()

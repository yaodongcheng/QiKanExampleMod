# -*- coding: utf-8 -*-
"""extract_official_names.py —— 从查看器的正式名表（armor.html）提取 CSW2 官方命名 → CSV。

源（用户提供，**只读**）
------------------------
    `D:\\BrainMaker\\战国无双2资产解包分析\\web\\armor.html`
    标题 =「SW2 全角色 武器 · 铠甲 · 头盔 正式名表」，62 行（29 武将 + BOSS + 兵种 + 护卫 + 怨灵）。

🔴 **源 → 生成器 → CSV**（铁律 28）：本脚本是那个生成器，`armor.html` 是源，
   产物 `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/Sw2OfficialNames.csv` 是离场层，
   **禁止手改产物** —— 改名字 = 改 html（上游）或改本脚本的解析，然后重跑。

为什么要这一步
--------------
item 的中文名原来是拼接的（「真田幸村的枪」），而战无2 **有官方正式名**
（「十文字枪」/「绯威赤备具足」/「六文钱前立兜」）。名字写进数据表，
两个生成器（`gen_weapon_items.py` / `gen_armor_items.py`）读表取名。

用法：
    python tools/sw2-pipeline/extract_official_names.py            # 生成 CSV
    python tools/sw2-pipeline/extract_official_names.py --check    # 只校验（exit 1 = 过期）
"""
import argparse
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))
from parts_table import TABLE                       # noqa: E402
from troop_parts_table import TROOP_TABLE           # noqa: E402
from csv_dual import write_table                    # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

SRC_HTML = r"D:\BrainMaker\战国无双2资产解包分析\web\armor.html"
OUT = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Sw2OfficialNames.csv")

CN = ["角色", "模型号", "武器正式名", "铠甲正式名", "头盔正式名",
      "武器Lv1", "武器Lv2", "武器Lv3", "武器Lv4", "武器Lv5", "日文原表记", "依据"]
EN = ["Key", "Model", "WeaponName", "ArmorName", "HelmetName",
      "W1", "W2", "W3", "W4", "W5", "JpName", "Note"]


def strip_tags(s):
    return re.sub(r"<[^>]+>", "", s or "").strip()


def parse_html(path):
    """→ {模型号: dict(...)}。模型号 = html 里 `<td class="model">` 的值（L00 / L05g / L200…）。"""
    t = io.open(path, encoding="utf-8", errors="replace").read()
    out = {}
    for row in re.findall(r'<tr class="r[^"]*"[\s\S]*?</tr>', t):
        def cell(cls):
            m = re.search(r'<td class="%s">([\s\S]*?)</td>' % cls, row)
            return strip_tags(m.group(1)) if m else ""
        model = cell("model")
        if not model:
            continue
        lv = [x.strip() for x in re.split(r"[→>]", cell("lv")) if x.strip()]
        lv += [""] * (5 - len(lv))
        out[model] = dict(
            model=model, cn=cell("name"),
            weapon=cell("f fw").split("[")[0].strip(),
            armor=cell("f fa").split("[")[0].strip(),
            helmet=cell("f fh").split("[")[0].strip(),
            lv=lv[:5], jp=cell("jp"), note=cell("note"))
    return out


def model_of(key):
    """`L00_yukimura` → `L00`；`L05_kenshin` → `L05`（怨灵是 `L05g`，不在我们的表里）。"""
    return key.split("_")[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    if not os.path.isfile(SRC_HTML):
        sys.exit("FAIL: 找不到源 %s" % SRC_HTML)
    src = parse_html(SRC_HTML)
    print("[名字] 源表解析：%d 行" % len(src))

    rows, miss_name = [], []
    for key in sorted(TABLE):
        m = model_of(key)
        o = src.get(m)
        if not o:
            miss_name.append(key)
            continue
        rows.append([key, m, o["weapon"], o["armor"], o["helmet"],
                     o["lv"][0], o["lv"][1], o["lv"][2], o["lv"][3], o["lv"][4],
                     o["jp"], o["note"]])
    if miss_name:
        sys.exit("FAIL: 这些角色在源表里找不到（模型号对不上）：%s" % miss_name)

    # ── 兵种 / 护卫（2026-09-16 补）────────────────────────────────────────────
    # 源表里**也有**它们的正式名（「茶革赤缀胴丸」「茶革阵笠」这一批），原来只提取武将那 28 行。
    # 兵种是**通用装备（无归属者）**：源表只给铠甲/头盔两列，没有武器列、没有 Lv1~5、没有日文原表记。
    # 🔴 名字口径见 `全角色武器甲胄兜名表.md` §三「通用装备逐个区分命名，全表无重名」——
    #    所以每套甲/每顶笠都有独立名，不拼「XX 的甲」。
    miss_troop = []
    for key in sorted(TROOP_TABLE):
        m = model_of(key)
        o = src.get(m)
        if not o:
            miss_troop.append(key)
            continue
        rows.append([key, m, "", o["armor"], o["helmet"],
                     "", "", "", "", "", "", o["note"]])
    if miss_troop:
        sys.exit("FAIL: 这些兵种/护卫在源表里找不到（模型号对不上）：%s" % miss_troop)

    old = io.open(OUT, encoding="utf-8-sig").read() if os.path.isfile(OUT) else ""
    tmp = OUT + ".tmp"
    write_table(tmp, CN, EN, rows)          # 双行表头（CLAUDE.md CSV 规范）
    new = io.open(tmp, encoding="utf-8-sig").read()

    if args.check:
        os.remove(tmp)
        ok = (new == old)
        print("[名字] %s %s" % ("✅ 一致" if ok else "❌ 过期", OUT))
        return 0 if ok else 1
    if new != old:
        os.replace(tmp, OUT)
        print("[名字] 写出 %s（%d 行）" % (OUT, len(rows)))
    else:
        os.remove(tmp)
        print("[名字] 已最新，未动盘")
    # 空名自检（解析错位检测）：甲/盔两列**所有行**都必须有；武器列只有武将那 28 行要求
    # （兵种/护卫是通用装备，源表本来就没有武器列）。
    bad = [r[0] for r in rows if not (r[3] and r[4]) or (r[0] in TABLE and not r[2])]
    if bad:
        print("   ❌ 有空的（甲/盔必有，武器仅武将要求）：%s" % bad)
        return 1
    for r in rows[:3]:
        print("   %-16s 武=%-10s 甲=%-12s 盔=%-12s Lv5=%s" % (r[0], r[2], r[3], r[4], r[9]))
    return 0


if __name__ == "__main__":
    sys.exit(main())

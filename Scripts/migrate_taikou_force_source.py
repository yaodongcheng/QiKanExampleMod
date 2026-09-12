#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""ForceTaikou.csv 源表收编 Kingdom.csv（2026-09-12 用户裁定）
============================================================================
**为什么**：`Kingdom.csv` 与 `ForceTaikou.csv` 是太阁5 势力清单的两半（织丰口径 / TK5 官方口径），
2026-09-11 已合并成产物 `TaikouForce.csv`。Kingdom.csv 从此不再是消费表，却仍挂在生成器输入位、
还带 5 个报废列（`Owner` / `Is_1568` / `IsShokuho` / `LocozationName` 合并时已丢，`ScriptName` 与
`ChineseName` 逐字相同）→ **看着像重复表，实际是身份错位**。

实测它对新表的**独有贡献只有两样**：
  1. `Culture` 列（`TaikouForce.csv` 没这列，133 行的 Culture 全靠它）
  2. `noKingdom` 一行（无主家占位）
`短名` 不算独有贡献——机械可推（实测 0/185 例外 = 势力名去掉尾部「家」）。

**本脚本把这两样收编进 `ForceTaikou.csv`，使它成为唯一势力源表**；Kingdom.csv 随后归档。

**🔴 顺带动到的 `Owner_<年>` ×6 —— 已按用户裁定恢复，别再删（2026-09-12）**
  本脚本当时以「死输入」为由删掉了这六列：实测 **396 格是织丰编号**（`lord_1_*`）、89 格是
  `@商人/@忍者/@海贼` 模板标记，而生成器**每一格都从上级日志重算覆盖**，
  产物里 0 织丰编号、0 模板标记、525 个 `lord_tk5_*` → 对输出**零贡献**。

  🔴 **当日用户裁定：需要保留，并且要时刻保持交叉验证。**
  理由：它虽对产物零贡献，却是**势力槽快照口径的唯一逐格记录**——
  `_analysis/` 下的派生表只是**中间状态文件**（不作留档依据），且实测有 42 个取值
  （忍者/商人/海贼头目）在别处**无留档**。两套口径留着**互相对照**才有价值。

  落地：
    · 恢复脚本 `Scripts/restore_force_owner_columns.py`（从本脚本的 `.bak_forcesrc_*` 回填）
    · 🔴 **列名保持 `Owner_<年>` 不动**——产物 `TaikouForce.csv` 也叫 `Owner_<年>`，**同名不是问题**，
      两张表本来就记同一概念的两种口径，区分靠**文件名**。
      （本脚本一度把那六列改名为 `快照当主_<年>`/`SnapshotOwner_<年>`，**属未获授权的自作主张**，
       2026-09-12 用户裁定撤回到 `Owner_<年>`。）
    · 常驻交叉验证 `Scripts/check_force_owner_crossval.py`（已挂一键体检）：桥好两套编号后逐格比对，
      四类计数对基线，**改任一源表/重导日志/改生成器都会让它变红**

**新表结构**（双行表头保留）：

    中文：ID | 太阁编号 | 势力名 | 别名 | 势力类型 | Culture | Owner_1554 … Owner_1598
    英文：ID | TK5_ID  | ForceName | Alias | ForceType | Culture | Owner_1554 … Owner_1598

**写入前/后的不变量**（本脚本自检）：
  · `TaikouForce.csv` 产物必须**逐字节不变**（收编的是同一批数据，只是换了来源）
  · 收编后 `Culture` 非空的行数与收编前一致
  · `noKingdom` 恰好一行，`势力类型=Neutral`

**纪律**：备份 + 往返校验（写盘 == 读回）+ 幂等两跑（第二次必须 0 改动）。

Usage:
  python Scripts/migrate_taikou_force_source.py            # 报告 + 预览（不写盘）
  python Scripts/migrate_taikou_force_source.py --apply    # 落盘（带备份 + 往返校验）
Exit: 0 正常 / 1 有硬问题 / 2 fatal。
"""
import argparse
import csv
import io
import os
import shutil
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")

KINGDOM = os.path.join(CSV_DIR, "Kingdom.csv")
FORCE = os.path.join(CSV_DIR, "ForceTaikou.csv")

# 🔴 本条归并的源行随 Kingdom.csv 一起消失 → 归档后 `ikko_shu` 自然不存在。
#    归并方向与理由见 gen_taikou_force_csv.py 文件头（一向宗 → 本愿寺，保留 honganji）。
ORPHAN_DROP = "ikko_shu"
NO_KINGDOM = "noKingdom"
ORG_CULTURE = "neutral_culture"

HDR_CN = ["ID", "太阁编号", "势力名", "别名", "势力类型", "Culture"]
HDR_EN = ["ID", "TK5_ID", "ForceName", "Alias", "ForceType", "Culture"]
OLD_OWNER_COLS = ["Owner_" + e for e in ("1554", "1560", "1568", "1575", "1582", "1598")]


def load_dict(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        return [{(k or "").strip(): (v or "").strip() for k, v in r.items()} for r in rd
                if any((v or "").strip() for v in r.values())]


def load_force(path):
    """ForceTaikou 双行表头：第 1 行中文、第 2 行英文，数据从第 3 行起。"""
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    return rows[0], rows[1], [r for r in rows[2:] if r and r[0].strip()]


def render(data):
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(HDR_CN)
    w.writerow(HDR_EN)
    for r in data:
        w.writerow(r)
    return buf.getvalue()


def main():
    ap = argparse.ArgumentParser(description="Kingdom.csv 收编进 ForceTaikou.csv")
    ap.add_argument("--apply", action="store_true", help="写盘（默认只报告）")
    args = ap.parse_args()

    # 🔴 本脚本已执行完毕（2026-09-12）。它对当前表结构不再适用——再跑会把快照列洗掉。
    if not os.path.isfile(KINGDOM):
        print("本脚本已完成使命（2026-09-12），不再执行。\n"
              "  · Kingdom.csv 已归档 → csv/_archive/Kingdom_织丰口径_20260912.csv\n"
              "  · 它的 Culture / noKingdom 已收编进 ForceTaikou.csv（唯一势力源表）\n"
              "  要改源表请直接编辑 ForceTaikou.csv；要恢复 Owner_<年> 六列用 restore_force_owner_columns.py。")
        return 0

    for p in (KINGDOM, FORCE):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2

    kingdom = load_dict(KINGDOM)
    kcult = {r["ID"]: r.get("Culture", "") for r in kingdom}
    cn_hdr, en_hdr, rows = load_force(FORCE)

    if "Culture" in cn_hdr:
        print("ForceTaikou.csv 已是收编后的形态（含 Culture 列）—— 本脚本不再适用。\n"
              "  要改源表请直接编辑 ForceTaikou.csv；要恢复 Owner_<年> 六列用 restore_force_owner_columns.py。")
        return 0

    problems = []
    if cn_hdr != ["ID", "太阁编号", "势力名", "别名", "势力类型"] + OLD_OWNER_COLS:
        problems.append("ForceTaikou 表头与预期不符：%s" % cn_hdr)
    if cn_hdr[:1] != HDR_CN[:1]:
        problems.append("ForceTaikou 首列不是 ID")

    # ── 逐行收编 ──
    data, stats = [], {"kept": 0, "got_culture": 0, "org": 0, "dropped": 0}
    have = set()
    for r in rows:
        rid = r[0].strip()
        if rid == ORPHAN_DROP:
            stats["dropped"] += 1
            continue
        if rid in have:
            problems.append("ForceTaikou 里 %s 重复" % rid)
        have.add(rid)
        rec = dict(zip(cn_hdr, r))
        if rid in kcult:
            cult = kcult[rid]
            stats["got_culture"] += 1
        else:                                   # 51 个 org_*（商家/忍者/海贼）
            cult = ORG_CULTURE
            stats["org"] += 1
        if not cult:
            problems.append("%s 的 Culture 取不到" % rid)
        data.append([rec["ID"], rec["太阁编号"], rec["势力名"], rec["别名"],
                     rec["势力类型"], cult])
        stats["kept"] += 1

    # ── noKingdom：Kingdom.csv 独有的一行，收编进来 ──
    if NO_KINGDOM in have:
        problems.append("%s 已在 ForceTaikou 里（不该重复收编）" % NO_KINGDOM)
    else:
        nk = next((r for r in kingdom if r["ID"] == NO_KINGDOM), None)
        if nk is None:
            problems.append("Kingdom.csv 里没有 %s 行" % NO_KINGDOM)
        else:
            data.append([NO_KINGDOM, "", nk.get("ChineseName", "无"), "",
                         "Neutral", nk.get("Culture", "") or ORG_CULTURE])
            stats["kept"] += 1
            print("  收编 %s 行：势力名=%s 势力类型=Neutral Culture=%s"
                  % (NO_KINGDOM, nk.get("ChineseName", ""), data[-1][5]))

    # ── 闭合校验 ──
    ids = [r[0] for r in data]
    if len(set(ids)) != len(ids):
        problems.append("输出 id 有重复")
    if stats["kept"] != 185:
        problems.append("收编后行数 %d ≠ 预期 185（184 −1 归并 +1 noKingdom）" % stats["kept"])

    # ── 自检：产物必须逐字节不变 ──
    print("ForceTaikou 收编：保留 %d 行（取自 Kingdom 的 Culture %d 行 / org_* 兜底 %d 行）"
          % (stats["kept"], stats["got_culture"], stats["org"]))
    print("  丢弃归并源行 %d（%s —— 随 Kingdom.csv 归档自然消失）" % (stats["dropped"], ORPHAN_DROP))
    print("  删除死列：%s" % " ".join(OLD_OWNER_COLS))

    text = render(data)

    if problems:
        print("\n❌ 硬问题 %d 条：" % len(problems))
        for p in problems:
            print("   %s" % p)
        return 1

    if not args.apply:
        print("\n（未加 --apply，只报告不写盘）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(FORCE, FORCE + ".bak_forcesrc_" + stamp)
    with io.open(FORCE, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(text)
    print("\n✅ 已写出 ForceTaikou.csv（%d 行数据）" % len(data))

    # ── 往返校验：写盘 == 读回 ──
    cn2, en2, rows2 = load_force(FORCE)
    if cn2 != HDR_CN or en2 != HDR_EN:
        print("❌ 往返校验失败：表头不一致")
        return 1
    if len(rows2) != len(data):
        print("❌ 往返校验失败：行数 %d ≠ %d" % (len(rows2), len(data)))
        return 1
    print("  往返校验 ✅（%d 行 / 表头一致）" % len(rows2))
    print("  ⏭ 下一步：python Scripts/gen_taikou_force_csv.py --apply")
    print("            （产物 TaikouForce.csv 应逐字节不变）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

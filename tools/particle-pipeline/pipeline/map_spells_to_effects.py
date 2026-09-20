#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""map_spells_to_effects.py —— 把「技能 ↔ 特效」对起来（回答：每个特效是哪个技能在用）。

输入（都是数据根里的产物）：
  output/spells_export/spells.json   ← pipeline/export_spell_table.py（UE 侧导出）
  output/xml/*.xml                   ← 我们生成的 99 个骑砍粒子 XML
输出：
  output/spells_export/spell_effect_map.json  技能 → 特效（带槽位：蓄力/命中/持续/放置）
  output/spells_export/spell_effect_map.md    同一份，人读的表格

【对位规则】
  FCS 的技能行里，VFX 字段是 `NiagaraSystem'"/Game/.../<资产名>.<资产名>"'`，
  槽位靠**字段名**区分（`SpellChannelPS`=持续引导 / `SpellHitPS`=命中 / `SpellPS`=放置持续 /
  `SpellPlacement`=放置法阵 / …）。
  资产名 → 我们的 effect 名 = 全小写加 `lwn_` 前缀（`NS_Fireball` → `lwn_ns_fireball`），
  这个前缀是当初转换时定的（见 pipeline/ue2bannerlord.py），所以能直接对上。

用法：
    python pipeline/map_spells_to_effects.py
"""
import io
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
import paths

SPELLS = None  # 延迟到 main 里定，避免 import 时就要求文件存在

ASSET_RE = re.compile(r"(\w+)\s*=\s*NiagaraSystem'\"([^\"]+)\"'")


def asset_to_effect(asset_path):
    """`/Game/.../NS_Fireball.NS_Fireball` -> `lwn_ns_fireball`（对我们没有的资产返回 None）。"""
    name = asset_path.split("/")[-1].split(".")[0]
    return "lwn_" + name.lower()


def main():
    data_dir = paths.out("spells_export")
    spells_json = os.path.join(data_dir, "spells.json")
    if not os.path.exists(spells_json):
        raise SystemExit("找不到 %s —— 先跑 pipeline/export_spell_table.py（UE 侧）" % spells_json)

    d = json.load(io.open(spells_json, encoding="utf-8"))
    cols, rows = d.get("columns", []), d.get("rows", {})

    # 我们手上真有的 effect（99 个 XML 的文件名）
    have = {f[:-4] for f in os.listdir(paths.out("xml")) if f.endswith(".xml")}

    out_rows, used_effects = [], {}
    for key, row in rows.items():
        name = row.get("Name", "")
        m = re.search(r'"([^"]*)"\s*\)\s*$', name)          # NSLOCTEXT(...,"Fireball") -> Fireball
        disp = m.group(1) if m else name
        slots = []
        for col in cols:
            for field, path in ASSET_RE.findall(row.get(col, "") or ""):
                eff = asset_to_effect(path)
                if eff not in have:
                    slots.append({"slot": field, "asset": path.split("/")[-1], "effect": None,
                                  "note": "我们没转这个资产"})
                    continue
                slots.append({"slot": field, "asset": path.split("/")[-1], "effect": eff})
                used_effects.setdefault(eff, []).append("%s.%s" % (key, field))
        if slots:
            out_rows.append({"key": key, "name": disp, "category": row.get("SpellCategory", ""),
                             "damage_type": row.get("DamageType", ""), "slots": slots})

    # 按「法术显示名」合并（表里同一法术有 Rank1/2/3 多行，VFX 通常一样）
    merged = {}
    for r in out_rows:
        m = merged.setdefault(r["name"], {"name": r["name"], "category": r["category"],
                                          "rows": 0, "slots": {}})
        m["rows"] += 1
        for s in r["slots"]:
            m["slots"][(s["slot"], s["asset"], s["effect"])] = True

    # ⚠️ 跨分类共用的 (槽位, 资产)：同一个组合出现在 ≥4 个**不同分类**的法术上。
    #    两种可能，**要人工分辨**：
    #      · 合理共用 —— 例如 `SpellPlacement = NS_PlacementCircle`：所有放置类法术共用同一个法阵 ✓
    #      · 疑似默认值 —— 例如 `SpellHitPS = NS_MadnessExplosion` 出现在火焰喷射/冰风/毒喷/连锁闪电上，
    #        语义完全不相干，多半是这行没填、吃了结构体默认值 ✗
    #    脚本只负责标出来，不替人下结论。
    slot_cats = {}
    for m in merged.values():
        for (slot, asset, eff) in m["slots"]:
            slot_cats.setdefault((slot, asset), set()).add(m["category"])
    suspect = {k for k, v in slot_cats.items() if len(v) >= 4}

    never = sorted(have - set(used_effects))
    result = {
        "spell_rows": len(rows),
        "spells": len(merged),
        "effects_used": len(used_effects),
        "effects_total": len(have),
        "effects_never_used": never,
        "suspect_defaults": ["%s = %s" % (s, a) for (s, a) in sorted(suspect)],
        "map": [{"name": m["name"], "category": m["category"], "rows": m["rows"],
                 "slots": [{"slot": s, "asset": a, "effect": e,
                            "suspect_default": (s, a) in suspect}
                           for (s, a, e) in sorted(m["slots"])]}
                for m in sorted(merged.values(), key=lambda x: x["name"])],
    }
    os.makedirs(data_dir, exist_ok=True)
    with io.open(os.path.join(data_dir, "spell_effect_map.json"), "w",
                 encoding="utf-8", newline="\n") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)

    # 人读版
    L = ["# 技能 ↔ 特效 对照（由 FCS 技能表 DT_SpellsInfo 导出）", "",
         "共 **%d** 个法术行（含 Rank 变体）→ 合并成 **%d** 个法术；覆盖我们 **%d/%d** 个特效。"
         % (len(rows), len(merged), len(used_effects), len(have)), "",
         "| 法术 | 分类 | 槽位 | VFX 资产 | 我们的 effect |", "|---|---|---|---|---|"]
    for m in result["map"]:
        for s in m["slots"]:
            L.append("| %s | %s | %s | %s | %s%s |"
                     % (m["name"], m["category"], s["slot"], s["asset"],
                        s["effect"] or "—",
                        " ⚠️跨分类共用（需人工确认）" if s["suspect_default"] else ""))
    if suspect:
        L += ["", "## ⚠️ 跨分类共用的槽位（要人工分辨：合理共用，还是吃了默认值）", "",
              "同一个「槽位 = 资产」出现在 ≥4 个**不相关分类**的法术上。两种可能：", "",
              "- **合理共用**：如 `SpellPlacement = NS_PlacementCircle` —— 所有放置类法术共用一个法阵",
              "- **疑似默认值**：如 `SpellHitPS = NS_MadnessExplosion` 出现在火焰喷射/冰风/毒喷/连锁闪电上，"
              "语义不相干，多半是这行没填、吃了结构体默认值（用到时要回 UE 核对）", ""]
        L += ["- `%s`" % x for x in result["suspect_defaults"]]
    if never:
        L += ["", "## 技能表没引用到的特效（%d 个）" % len(never), "",
              "这些不挂在法术行上，来源是别处（伤害/状态组件、或作为别的特效的内部子发射器）：", ""]
        L += ["- `%s`" % x for x in never]
    with io.open(os.path.join(data_dir, "spell_effect_map.md"), "w",
                 encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L) + "\n")

    print("技能行 %d / 有 VFX 的 %d" % (len(rows), len(out_rows)))
    print("覆盖特效 %d/%d" % (len(used_effects), len(have)))
    print("写出 spell_effect_map.json / .md（在 %s）" % data_dir)
    print("未被任何技能引用的特效 %d 个: %s" % (len(never), ", ".join(never[:8]) + (" …" if len(never) > 8 else "")))
    return 0


if __name__ == "__main__":
    sys.exit(main())

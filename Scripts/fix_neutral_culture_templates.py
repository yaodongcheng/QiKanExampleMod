#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_neutral_culture_templates.py — neutral_culture 的部队模板/兵种属性对齐（手写块 · 幂等复跑）
=====================================================================================
为什么单独一个脚本：`neutral_culture` 块是**手写块**（不在 `gen_taikou_culture_full.py` 的
16 个生成文化里，生成器原样保留它，见必备清单雷 110）——但它被 495 处引用（26 王国 + 51 个
org_* 无地势力 + 若干家族），属性组必须与生成出来的 16 个文化**逐条对齐**，否则就是雷 110
（缺 `default_party_template` = 建世界刷领主部队 `FillPartyStacks` NRE）的翻版。

本次对齐的值（2026-09-12，C 档最小日本兵种集）：
  · 8 个部队模板属性 → `taikou_*_party_template`（旧值 = 占位模板 `main_hero_party_template`
    「主角」/ `militia_template`；占位模板会让全世界部队被填成「主角」副本，雷 115）
  · 6 个兵种属性 → 真兵种（`yari_ashigaru` 枪足轻 / `veteran_ashigaru` 精锐足轻 / 弓足轻 …
    `elite_basic_troop` = 精锐足轻：对齐官方语义——精英**征募兵**是「更好的新兵」，
    不是最高档兵（官方 = `imperial_vigla_recruit`，不是骑士））

做法（幂等）：把 `name="{=TAIKOU_culture_neutral}Neutral"` 与 `encounter_background_mesh=`
之间的属性组整段替换成规范组——**任意旧值都能对齐**，重复跑 = 无操作。
纪律：只动这一段的属性行（其它内容一字不碰）；改完 parse 验证再写盘。
用法：python Scripts/fix_neutral_culture_templates.py [--module PATH] [--dry-run]
"""
import argparse
import re
import sys
import xml.dom.minidom as minidom
from pathlib import Path

# 规范属性组（与 gen_taikou_culture_full.py 的 culture 模板逐字一致）
NEW_ATTRS = """			 basic_troop="NPCCharacter.yari_ashigaru"
			 elite_basic_troop="NPCCharacter.veteran_ashigaru"
			 melee_militia_troop="NPCCharacter.peasant_farmer"
			 ranged_militia_troop="NPCCharacter.yumi_ashigaru"
			 melee_elite_militia_troop="NPCCharacter.yari_ashigaru"
			 ranged_elite_militia_troop="NPCCharacter.yumi_ashigaru"
			 default_party_template="PartyTemplate.taikou_lord_party_template"
			 militia_party_template="PartyTemplate.taikou_militia_party_template"
			 villager_party_template="PartyTemplate.taikou_villager_party_template"
			 caravan_party_template="PartyTemplate.taikou_caravan_party_template"
			 elite_caravan_party_template="PartyTemplate.taikou_elite_caravan_party_template"
			 rebels_party_template="PartyTemplate.taikou_rebels_party_template"
			 bandit_boss_party_template="PartyTemplate.taikou_bandit_party_template"
			 vassal_reward_party_template="PartyTemplate.taikou_vassal_reward_party_template\""""

# 规范组所在的「槽」：neutral 文化块的 name 行之后、encounter_background_mesh 行之前
SLOT = re.compile(
    r'(name="\{=TAIKOU_culture_neutral\}Neutral"\n)(.*?)(\t\t\t encounter_background_mesh=)',
    re.S)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    path = Path(args.module) / "ModuleData" / "spcultures.xml"
    if not path.is_file():
        print(f"[FATAL] 找不到 {path}")
        return 2
    raw = path.read_bytes()
    eol = "\r\n" if b"\r\n" in raw else "\n"
    txt = raw.decode("utf-8-sig", errors="replace")

    m = SLOT.search(txt)
    if m is None:
        print("[FATAL] 找不到 neutral_culture 的属性槽（name=…Neutral / encounter_background_mesh）")
        return 2

    want = NEW_ATTRS.replace("\n", eol) + eol
    if m.group(2) == want:
        print("[KEEP] neutral_culture 属性组已是规范值 → 无操作")
        return 0

    out = txt[:m.start(2)] + want + txt[m.end(2):]
    try:
        minidom.parseString(out)
    except Exception as e:
        print(f"[FATAL] 替换后 XML parse 失败，未写盘：{e}")
        return 2
    old_lines = len([l for l in m.group(2).splitlines() if l.strip()])
    new_lines = len([l for l in NEW_ATTRS.splitlines() if l.strip()])
    if args.dry_run:
        print(f"[DRY ] 将把 neutral_culture 属性组 {old_lines} 行 → {new_lines} 行规范值")
        return 0
    path.write_bytes(out.encode("utf-8-sig"))
    print(f"[DONE] neutral_culture 属性组已对齐（{old_lines} 行 → {new_lines} 行），{path.name} 已写盘")
    return 0


if __name__ == "__main__":
    sys.exit(main())

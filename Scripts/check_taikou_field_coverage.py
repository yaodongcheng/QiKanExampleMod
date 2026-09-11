#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_taikou_field_coverage.py — 内容包数据「字段交集覆盖检查」（七类对象）
=====================================================================
用途：对 Culture/Settlement/Item/Hero/NPCCharacter/Clan/Kingdom 七类对象，
  计算 织丰(主文化) ∩ 原版 的字段集合（最小必要字段），对照我们 Taikou 的实际定义，
  输出【缺失字段清单】——一次看清 "我们差什么"（2026-09-08 用户裁定：不要挤牙膏，一次看全）。

用法：python Scripts/check_taikou_field_coverage.py [--module PATH]
输出：每类：原版字段 / 织丰字段 / 交集 / 我们都覆盖了哪些 / 缺失项。
"""
import re
import sys
from pathlib import Path

MB = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules"
OFFICIAL = {
    "Culture": (MB + r"\SandBoxCore\ModuleData\spcultures.xml", "Culture"),
    "Settlement": (MB + r"\SandBox\ModuleData\settlements.xml", "Settlement"),
    "Hero": (MB + r"\SandBox\ModuleData\heroes.xml", "Hero"),
    "NPCCharacter": (MB + r"\SandBoxCore\ModuleData\spnpccharacters.xml", "NPCCharacter"),
    "Clan": (MB + r"\SandBox\ModuleData\spclans.xml", "Faction"),
    "Kingdom": (MB + r"\SandBox\ModuleData\spkingdoms.xml", "Kingdom"),
    "Item": (MB + r"\SandBoxCore\ModuleData\items\weapons.xml", "Item"),
}
SHOKUHO_CULTURE = MB + r"\Shokuho\ModuleData\spcultures\shokuho_main_cultures.xml"


def attrs_and_subs(path, tag):
    txt = open(path, encoding="utf-8-sig", errors="replace").read().split("?>", 1)[1]
    attrs = set(re.findall(r"(\w+)=", txt))
    subs = set(re.findall(r"^\s*<([A-Za-z_]+)[\s>]", txt, re.M)) - {tag}
    return attrs, subs


def main():
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    args = ap.parse_args()
    md = Path(args.module)
    # 🔴 我们自己的文件缺失 = 硬报错，**不能只印一行然后 continue**（2026-09-11 用户裁定）。
    #   理由同 check_scene_entities：文件名是约定（如基线时代走无后缀名 settlements.xml），
    #   约定漂移时"印一行就跳过"会让整类对象静默不检查，而脚本仍然 exit 0（假绿）。
    errors = []
    for cls, (opath, tag) in OFFICIAL.items():
        try:
            oa, os_ = attrs_and_subs(opath, tag)
        except Exception as e:
            print(f"[{cls}] official read fail: {e}")
            continue
        if cls == "Culture":
            wa, ws = attrs_and_subs(SHOKUHO_CULTURE, tag)
        else:
            wa, ws = set(), set()  # 织丰非文化类无独立文件参考（沿用官方）
        inter = wa & oa if cls == "Culture" else oa  # 交集：Culture 用织丰∩原版，其它类官方为主
        # 我们自己的（spcultures/settlements/heroes/spnpccharacters/spclans/spkingdoms/items）
        ours_files = {
            "Culture": md / "ModuleData" / "spcultures.xml",
            "Settlement": md / "ModuleData" / "settlements.xml",
            "Hero": md / "ModuleData" / "taikou_heroes.xml",
            "NPCCharacter": md / "ModuleData" / "spnpccharacters.xml",
            "Clan": md / "ModuleData" / "spclans.xml",
            "Kingdom": md / "ModuleData" / "spkingdoms.xml",
            "Item": md / "ModuleData" / "taikou_items" / "weapons.xml",
        }
        p = ours_files[cls]
        if not p.exists():
            errors.append(f"{cls}: 我们的文件不存在 {p}")
            print(f"  [ERROR] [{cls}] 我们文件缺失: {p}")
            continue
        aa, ss = attrs_and_subs(str(p), tag)
        missing = inter - aa
        print(f"\n== {cls} (交集 {len(inter)} 字段; 我们已有 {len(aa & inter)})")
        if missing:
            print("   缺失:", sorted(missing))
        else:
            print("   缺失: 无 ✓")
    print("\n提示: 子节(interior elements)单独看法——`name/feat/policy/template` 等多为内容节，值空可接受；"
          "属性(attributes)缺失 = 运行风险，优先看上面清单。")
    if errors:
        print(f"\nSummary: errors={len(errors)}（文件缺失 = 该类对象整块没查，属硬错误）")
        return 1
    print("\nSummary: errors=0")
    return 0


if __name__ == "__main__":
    sys.exit(main())

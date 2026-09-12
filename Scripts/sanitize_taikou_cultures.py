#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sanitize_taikou_cultures.py — 把 Taikou 数据包内所有指向外部文化的引用统一替换为 Culture.ikoku
====================================================================================
背景（2026-09-07 交接文档）：官方 SPCultures 段被 GameType 白名单过滤，Taikou 逻辑内只存在
Culture.ikoku；但拷贝来的官方物品/工艺件/音乐/装备模板带 2800+ 处 Culture.<原版八文化> 引用，
运行时 GetPresumedObject 以引用创建裸文化桩（模板列表为 null）→ Companion NRE。

被替换的文化（全部为非自给）：empire aserai battania khuzait sturgia vlandia looters
# 🔴 2026-09-12 剔除 neutral_culture：本包自己定义了 Culture.neutral_culture（spcultures.xml），
#    再替换会把生成物里合法的 neutral_culture 引用改掉（实测：taikou_lords.xml 51 处 → 产物≠生成器，铁律 22 违规）。
# 🔴 用法纪律：**只对「刚拷入的官方文件」跑**（物品/装备模板/工艺件那批）；对生成物跑 = 改生成物，
#    正确的顺序是 拷贝官方 → 本脚本 → prune/生成器；若已误跑，重跑对应生成器即可还原。
替换目标：Culture.ikoku（唯一自给文化；替换后引用恒有效，任何文化语义后续由生成器统一裁决）

纪律：脚本改 XML 必 parse 验证（替换前后 minidom.parse）；输出被改文件清单与替换数。
用法：python Scripts/sanitize_taikou_cultures.py [--module PATH] [--dry-run]
"""
import argparse
import re
import sys
import xml.dom.minidom as minidom
from pathlib import Path

BAD_CULTURES = re.compile(
    # 🔴 2026-09-12 复核（雷 109 纪律：清洗表随数据演进复核）：补 `nord`——重拷官方
    #    `items/shields.xml` 后，一件引擎必留盾牌带 `culture="Culture.nord"`，而 nord 是官方
    #    孤岛文化（本包不定义）→ 运行期裸桩，且 `check_taikou_xml_references.py` 报 DANGLING。
    #    `vakken`/`darshi` 是同类官方孤岛文化，一并列入（当前 0 命中，纯预防）。
    r"Culture\.(empire|aserai|battania|khuzait|sturgia|vlandia|looters|nord|vakken|darshi)"
)


def transform(text: str) -> str:
    return BAD_CULTURES.sub("Culture.ikoku", text)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    module_data = Path(args.module) / "ModuleData"
    if not module_data.is_dir():
        print(f"[FATAL] ModuleData not found: {module_data}")
        sys.exit(2)

    total = 0
    changed_files = []
    for path in sorted(module_data.rglob("*.xml")):
        if path.name in ("spcultures.xml", "_skip_"):  # spcultures 由人工写文件，不在批量替换范围
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        hits = len(BAD_CULTURES.findall(text))
        if not hits:
            continue
        new_text = transform(text)
        try:
            minidom.parseString(new_text.encode("utf-8"))  # 铁律：脚本改 XML 必 parse
        except Exception as e:
            print(f"[SKIP-PARSE-FAIL] {path}: {e}")
            continue
        total += hits
        changed_files.append((path, hits))
        if not args.dry_run:
            path.write_text(new_text, encoding="utf-8-sig")
        print(f"[{'DRY  ' if args.dry_run else 'FIXED'}] {path.name}: {hits} refs")

    print(f"\nsummary: {len(changed_files)} files, {total} culture refs -> Culture.ikoku")
    sys.exit(0)


if __name__ == "__main__":
    main()

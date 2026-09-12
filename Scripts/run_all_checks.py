#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
一键跑「数据改动必跑」全套检查（自定义世界内容包体检总入口）
========================================================================
把散落的 checker 串成一条命令，逐个体检并汇总。**改完数据/内容包跑这一条就够。**
每个 checker 的判定规则见各自文件头；清单侧对照表见
`Knowledge/自定义世界内容包从零起步必备清单.md`「清单条目 ↔ 脚本对照表」。

Usage:
  python Scripts/run_all_checks.py                 # 全部（默认内容包 = 环境/注册表指向的 Taikou）
  python Scripts/run_all_checks.py --module PATH   # 换内容包（新世界复用）
  python Scripts/run_all_checks.py --quick         # 跳过慢的（官方拷贝全文件比对、距离缓存）
Exit: 0 全绿 / 1 有红 / 2 fatal。
"""
import argparse
import subprocess
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent

# (脚本, 说明, 是否算「慢」, 自定义参数 or None=默认 --module)
CHECKS = [
    ("check_taikou_xml_references.py", "交叉引用完整性 + 列表污染（雷 11/52）", False, None),
    ("check_taikou_field_coverage.py", "最小字段交集覆盖", False, None),
    ("check_required_ids.py", "引擎硬编码点名 id 必须存在（雷 6/7/15/16/19/27/31/40/41）", False, None),
    ("check_data_fields.py", "据点必填字段 / 城防 level≤3 / occupation 枚举 / 文化必备（雷 19/22/29/16）", False, None),
    ("check_culture_references.py", "文化引用悬空 + 角色模板文化属性（雷 11/30）", False, None),
    ("check_culture_text_variants.py", "文化 variation 文本族（雷 45）", False, None),
    ("check_language_registration.py", "语言文件登记 / 自有键中文 / emoji（雷 47/48/50/51 + 铁律 14）", False, None),
    ("check_scene_consumables.py", "官方场景消费物品（雷 40/41）", False, None),
    ("check_scene_entities.py", "地图场景必备实体（雷 28/34/37/42/53）", False, None),
    ("check_module_registration.py", "段注册 / 孤儿数据文件 / csproj 漏登记（雷 3/4/5/35/40）", False, None),
    ("check_era_segments.py", "时代段注册互斥 + 据点 id 跨时代稳定（时代切换 spike）", False, None),
    ("check_hero_templates.py", "英雄必须配同名 CharacterObject 模板（缺 = 静默被吞 → 新战役崩）", False, None),
    ("check_englishname_clan_prefix.py", "家族 id ↔ 家头罗马音一致（id 由家头派生，错了会造出假家族）", False, None),
    ("check_taikou_world_tables.py", "世界四表体检：自洽 + 交叉闭合 + id 体系闸门 + 据点名唯一（T4-b）", False, None),
    ("check_force_owner_crossval.py", "势力当主双口径交叉验证（快照 快照当主_<年> × 日志 Owner_<年>）", False, None),
    ("check_hero_profile_keys.py", "英雄 id 三处同键：模板 ↔ 画像表 ↔ 立绘表（选人详情页取数）", False, None),
    ("gen_taikou_era_diff.py", "时代差异段产物与生成器一致（铁律 22：生成物禁手改）", False, ["--check"]),
    ("gen_taikou_hero_profiles.py", "英雄画像表产物与生成器一致（铁律 22：生成物禁手改）", False, ["--check"]),
    ("gen_taikou_hero_catalog.py", "选人目录产物与生成器一致（建世界之前选人的唯一取数源）", False, ["--check"]),
    ("gen_taikou_clan_csv.py", "家族三表产物与生成器一致（Clan.csv / ClanID_<年> / 据点 Clan_<年>，铁律 22）", False, ["--check"]),
    ("gen_taikou_force_csv.py", "势力表产物与生成器一致（TaikouForce.csv ← 源表 ForceTaikou.csv，铁律 22）", False, ["--check"]),
    ("check_settlement_distance_cache.py", "距离缓存与据点一致（雷 53）", True, None),
    ("check_official_copies.py", "官方拷贝保持原样（雷 49）", True, None),
]


def main():
    ap = argparse.ArgumentParser(description="Run all content-pack checks")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--quick", action="store_true", help="跳过慢检查")
    args = ap.parse_args()

    results = []
    for script, desc, slow, extra in CHECKS:
        path = HERE / script
        if not path.is_file():
            results.append((script, desc, None, "脚本缺失"))
            continue
        if slow and args.quick:
            results.append((script, desc, None, "已跳过(--quick)"))
            continue
        cmd = [sys.executable, str(path)] + (extra if extra else ["--module", args.module])
        if extra and "--module" not in extra:
            cmd += ["--module", args.module]      # 生成器既有 --check 也接受 --module
        t0 = time.time()
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        dt = time.time() - t0
        results.append((script, desc, r.returncode, f"{dt:.1f}s"))
        # 红的把输出尾巴打出来，便于当场定位
        if r.returncode != 0:
            print(f"\n{'=' * 78}\n▼ {script}（exit {r.returncode}）—— {desc}\n{'=' * 78}")
            tail = [l for l in r.stdout.splitlines() if l.strip()][-14:]
            print("\n".join(tail))
            if r.stderr.strip():
                print("[stderr] " + r.stderr.strip()[:400])

    print(f"\n{'=' * 78}\n体检汇总（模块：{args.module}）\n{'=' * 78}")
    red = 0
    for script, desc, code, note in results:
        if code is None:
            mark = "⏭ "
        elif code == 0:
            mark = "✅"
        else:
            mark = "❌"
            red += 1
        print(f"  {mark} {script:38} {note:14} {desc}")
    print(f"\n结果：{len(results) - red} 绿 / {red} 红"
          + ("（红 = 有真问题，逐条看上面的输出）" if red else "（全绿 ✓）"))
    return 1 if red else 0


if __name__ == "__main__":
    sys.exit(main())

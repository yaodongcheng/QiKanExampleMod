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
REPO = HERE.parent
# Blender 侧检查（拼装闸门要 Blender 导 FBX/量几何）——路径写死，找不到就报"脚本缺失"
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

# (脚本, 说明, 是否算「慢」, 自定义参数 or None=默认 --module, 解释器 or None=python)
# 脚本名可以带相对仓库根的路径（例：tools/sw2-pipeline/check_assembly.py）；解释器="blender" 时
# 用 Blender 跑、且**不**追加 --module（Blender 脚本不认那个参数）。
CHECKS = [
    ("check_xml_parse.py", "XML 全量 parse 门（雷 8：任一文件坏 = exit 1）", False, None),
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
    ("check_taikou_world_tables.py", "世界四表体检：自洽 + 语义不变量 + 据点名唯一（T4-b）", False, None),
    ("check_reference_edges.py", "CSV 交叉引用全量边台账（悬空 + 全表孤儿 + XML 世界输出）", False, None),
    ("check_taikou_equip_tables.py", "两张装备数据表自洽（兵种表 TaikouTroop.csv / 武将装备档 HeroEquip.csv：slug·技能·文化·升级链·兜件配对）", False, None),
    ("check_equip_item_defs.py", "三张装备来源表（兵种表/武将表/武将装备档表）引用的物品**必须全部有定义**（本包 ∪ 引擎模块 ∪ 引擎硬编码）", False, None),
    ("check_items_civilian.py", "物品平民装可用性（任何装备都必须 <Flags Civilian=\"true\"/>，2026-09-21 铁律）", False, None),
    ("check_items_mesh_fields.py", "物品网格字段不许留空（mesh 空/缺 = 装备 AV 雷 166 · 箭类 holster_mesh 空 = 背包图标 NRE 雷 168）", False, None),
    ("check_action_wiring.py", "动作名接线：被引用的 act_* 必须在 action_types.xml 声明（漏 = 无效索引 = 拔装备 AV，雷 169）", False, None),
    # 🔴 `check_spell_modifiers.py` 两条已移至「第二遍：LWN 自己」——
    #    法术表（Spells.xml / Modifiers.xml）2026-10-09 随通用玩法搬进了 LivingWorldNpcs。
    ("test_negative_edges.py", "边台账负面测试（故意造坏数据必须抓到；含非人物行豁免反向验证）", False, None),
    ("test_negative_equip_tables.py", "两张装备表的负面测试（故意造坏必须抓到；含正向对照）", False, None),
    ("test_negative_equip_item_defs.py", "物品定义校验的负面测试（含「可锻造武器/引擎硬编码不许误报」反向验证）", False, None),
    ("test_negative_checks.py", "模块级检查负面测试（XML parse/场景/必填字段/PUA 造坏必须抓到）", False, None),
    ("test_negative_banner_icons.py", "家纹体检负面测试（注释错位/段位/材质缺失/几何分裂造坏必须抓到）", False, None),
    ("check_source_invariants.py", "C# 源码不变量（相机复位/出生点时点/停用类不得被引用）", False, None),
    ("check_hero_profile_keys.py", "英雄 id 三处同键：模板 ↔ 画像表 ↔ 立绘表（选人详情页取数）", False, None),
    ("check_taikou_banner_icons.py", "旗帜与家纹体检：结构(雷 130)/图标段位/材质存在/几何统一(雷 131)", False, None),
    # ("gen_taikou_era_diff.py", …) 🔴 2026-09-12 退役：三件套 _1582 已由 gen_taikou_era_world.py 接管
    ("gen_taikou_hero_profiles.py", "英雄画像表产物与生成器一致（铁律 22：生成物禁手改）", False, ["--check"]),
    ("gen_taikou_hero_catalog.py", "选人目录产物与生成器一致（建世界之前选人的唯一取数源）", False, ["--check"]),
    ("gen_taikou_clan_csv.py", "家族三表产物与生成器一致（Clan.csv / ClanID_<年> / 据点 Clan_<年>，铁律 22）", False, ["--check"]),
    ("gen_taikou_wanderer_culture.py", "英雄身份文化一致（游荡者=ronin / 大航海联动=pirate；依赖 ClanID，须在家族生成器之后）", False, ["--check"]),
    ("gen_taikou_force_csv.py", "势力表产物与生成器一致（TaikouForce.csv 读自己原地刷新，铁律 22）", False, ["--check"]),
    ("gen_taikou_settlement_owner.py", "据点归属与据点日志一致（Owner_<年> = 该城当年当主，铁律 22）", False, ["--check"]),
    ("import_settlement_kuni_chi.py", "据点「国/地」列与太阁日志一致（Kuni/Chi 回填，铁律 22）", False, ["--check"]),
    ("gen_taikou_english_strings.py", "英文语言层与数据 XML 内联 fallback 一致（铁律 22）", False, ["--check"]),
    ("gen_taikou_era_world.py", "六代世界段（英雄/领主模板/家族/王国）与生成器一致（铁律 22）", False, ["--check"]),
    ("gen_taikou_sw2_heads.py", "SW2 28 个专属 race：产物与生成器一致（铁律 22）", False, ["--check"]),
    ("gen_taikou_sw2_heads.py", "SW2 28 个专属 race：语义自检（Monster 整族 / 年龄段覆盖 / 脸池条数 / 三处 id 一致）", False, ["--selfcheck"]),
    ("gen_hero_extra_info.py", "英雄扩展属性表（DesignData/HeroExtraInfo.csv：落点等）与生成器一致（铁律 22）", False, ["--check"]),
    ("check_settlement_distance_cache.py", "距离缓存与据点一致（雷 53）", True, None),
    ("check_village_types_and_items.py", "村型 id 合法 + 村型产出物在世界物品集（雷 108）", False, None),
    ("check_official_copies.py", "官方拷贝保持原样（雷 49）", True, None),
    # Blender 侧：拼装闸门（头/甲/兜 按共用 T 拼回原角色）——28 人挨个跑，任一不过 exit 1
    #   三条硬判 = ① 最长连续露缝弧 ≤60° ①' 单档 gap ≤30mm ③ 不穿模 ④ 无孤立浮片（>50mm）
    ("tools/sw2-pipeline/check_assembly.py",
     "拼装闸门：三件共用 T 的 露缝弧/单档gap/不穿模/浮片（28 人）",
     False, ["--all"], "blender"),
]

# 🔴 2026-10-09 新增：**再跑一遍 LWN 自己**。
#    通用玩法（钩索/飞行/处决/法术/贴花）的数据与资产从 Taikou 内容包搬进了 LivingWorldNpcs，
#    于是「动作接线 / 物品 / 法术表」这几类检查对 LWN 也成立了 ——
#    只查内容包 = 通用玩法那半边没有体检。
#
#    ⚠️ 这里**只列对 LWN 有意义、且当前应当是绿的**检查。三条被**故意排除**的，理由记在这儿：
#      · `check_module_registration.py` —— 它查的是「内容包必须自备的整个世界段清单」
#        （SPCultures / Kingdoms / GameText×9 …）。LWN 是**基座不是内容包**，本来就不该有那些段，
#        跑它必然 27 条红，纯噪声。
#      · `check_xml_parse.py` —— 它扫的是**整模块**（含 tools/ 下的离线草稿产物），
#        LWN 里那些草稿不属于交付面。要查新搬进来的 XML 用 `check_action_wiring` 那条链覆盖。
#      · `check_language_registration.py` —— 它的「自有键中文覆盖」部分本次已人工核过（缺 0）；
#        剩下的告警打在 `CNs/std_scn_okehazama.xml` 上 —— 那是**剧本工程的在制品**
#        （繁体、作者尚未登记进 language_data.xml），不归本次工作调整管，别让 WIP 把这一遍染红。
LWN_MODULE = str(REPO)
# (脚本, 自定义参数 or None)
SECOND_MODULE_CHECKS = [
    ("check_items_civilian.py", None),      # 钩索/法术物品必须平民装可用（铁律 33）
    ("check_items_mesh_fields.py", None),   # 网格字段不许留空（雷 166/168）
    ("check_action_wiring.py", None),       # 44 条通用动作名的三处接线（漏声明 = 拔装备 AV，雷 169）
    ("check_spell_modifiers.py", None),     # 宝石表（已搬到 LWN/ModuleData/AssetRegistry/Modifiers.xml）
    ("check_spell_modifiers.py", ["--selftest"]),  # 同上，负面测试（夹具里的手环物品名已同步改成 lwn_spell_seal）
]


def main():
    ap = argparse.ArgumentParser(description="Run all content-pack checks")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--quick", action="store_true", help="跳过慢检查")
    args = ap.parse_args()

    results = []
    for entry in CHECKS:
        script, desc, slow, extra = entry[:4]
        runner = entry[4] if len(entry) > 4 else None
        # 带路径的条目按仓库根解析（例：tools/sw2-pipeline/check_assembly.py），否则按 Scripts/
        path = (REPO / script) if "/" in script else (HERE / script)
        if not path.is_file():
            results.append((script, desc, None, "脚本缺失"))
            continue
        if slow and args.quick:
            results.append((script, desc, None, "已跳过(--quick)"))
            continue
        if runner == "blender":
            if not Path(BLENDER).is_file():
                results.append((script, desc, None, "缺 Blender"))
                continue
            cmd = [BLENDER, "-b", "--python", str(path), "--"] + (extra or [])
        else:
            cmd = [sys.executable, str(path)] + (extra if extra else ["--module", args.module])
            if extra and "--module" not in extra:
                cmd += ["--module", args.module]      # 生成器既有 --check 也接受 --module
        t0 = time.time()
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        dt = time.time() - t0
        code = r.returncode
        # 🔴 Blender 侧脚本出错（语法错/未捕获异常）时 **blender 自己仍然 exit 0**（实测 2026-09-16：
        #    闸门里一个 SyntaxError，体检表照样打 ✅）。所以这里额外看 stderr 有没有 Traceback，
        #    有就按失败算 —— 否则"脚本坏了"会被当成"数据全过"。
        if runner == "blender" and code == 0 and "Traceback (most recent call last)" in (r.stderr or ""):
            code = 1
        results.append((script, desc, code, f"{dt:.1f}s"))
        # 红的把输出尾巴打出来，便于当场定位
        if code != 0:
            print(f"\n{'=' * 78}\n▼ {script}（exit {code}）—— {desc}\n{'=' * 78}")
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

    # ── 第二遍：LWN 自己（通用玩法的正本所在；见 SECOND_MODULE_CHECKS 的说明）──
    lwn_results = []
    if args.module != LWN_MODULE:
        print(f"\n{'=' * 78}\n第二遍：LWN 自己（{LWN_MODULE}）\n{'=' * 78}")
        for script, extra in SECOND_MODULE_CHECKS:
            path = HERE / script
            if not path.is_file():
                lwn_results.append((script, None, "脚本缺失"))
                continue
            cmd = [sys.executable, str(path)] + (extra or []) + ["--module", LWN_MODULE]
            r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
            lwn_results.append((script + (" " + " ".join(extra) if extra else ""), r.returncode, ""))
            if r.returncode != 0:
                print(f"\n{'=' * 78}\n▼ [LWN] {script}（exit {r.returncode}）\n{'=' * 78}")
                print("\n".join([l for l in r.stdout.splitlines() if l.strip()][-14:]))
                if r.stderr.strip():
                    print("[stderr] " + r.stderr.strip()[:400])
        red2 = 0
        for script, code, note in lwn_results:
            if code is None:
                mark = "⏭ "
            elif code == 0:
                mark = "✅"
            else:
                mark = "❌"
                red2 += 1
            print(f"  {mark} [LWN] {script:38} {note}")
        print(f"\nLWN 结果：{len(lwn_results) - red2} 绿 / {red2} 红"
              + ("（红 = 有真问题，逐条看上面的输出）" if red2 else "（全绿 ✓）"))
        red += red2
    return 1 if red else 0


if __name__ == "__main__":
    sys.exit(main())

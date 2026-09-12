#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Hero three-way key checker (英雄 id 三处同键：模板 ↔ 画像表 ↔ 立绘表)
========================================================================
🔴 规则（2026-09-11 立）——选人详情页取数/取立绘全靠「一个 id 走通全链」：

    spnpccharacters.xml 的 <NPCCharacter id="X">        ← 引擎建英雄的模板
    AssetRegistry/HeroProfiles.xml 的 <HeroProfile id="X">  ← 详情页的五维/16 技能/生卒
    AssetRegistry/ProfileStages.csv 的 StringId 列          ← 立绘/小头像 sprite

  三处**必须同键**（CSV 制式，如 lord_tk5_195）。任一处不同 = 详情页那一块静默空白：
    · 模板 id 改了、另两处没改 → 英雄还在，但**画像与立绘都查不到**（界面画「暂无史料」占位）
    · 这正是「步 0」要修的历史问题（旧 id `lord_oda_nobunaga` ↔ 表制式 `lord_1_oda`）

为什么值得单独守：**三处都语法合法、交叉引用也都解析得了**——
  spnpccharacters 的 id 是自洽的，HeroProfiles 的 id 也是自洽的，
  现有的 check_taikou_xml_references 只查「引用能不能解析」，**查不出两套 id 各说各话**。
  这是"同一实体两套命名"的静默错位，不是悬空引用。

检查项
------
  ① ERROR：世界里每条 <Hero id="X">（按 GameType 逐套查）在 spnpccharacters.xml 里有无同名模板
     —— 与 check_hero_templates.py 同口径，此处只做交叉引用，不重复那份的详细规则
  ② ERROR：**「推荐」名单**里的每个 id 必须在 1560 那套英雄里找得到
     —— 找不到 = 推荐列表整行灰掉（「未登场」），功能等于没做
  ③ ERROR：推荐人必须同时有 <HeroProfile> 与 ProfileStages 立绘条目（推荐卡没头像是硬伤）
  ④ WARN ：普通世界英雄缺 <HeroProfile> / 缺立绘 —— 占位期正常，全量数据到位后应清零

Usage:
  python Scripts/check_hero_profile_keys.py [--module PATH]
Exit: 0 clean / 1 problem found / 2 fatal.
"""
import argparse
import csv
import io
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

# 引擎自造、不需要模板/画像的英雄（建号流程创建，同 check_hero_templates 口径）
EXEMPT_HERO_IDS = {"main_hero"}

# 推荐名单固定服务的时代段（太阁5 推荐人名单绑 1560）
RECOMMEND_ERA_PATH = "taikou_heroes"


def registry_mb2_path():
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                val, _ = winreg.QueryValueEx(k, "MB2_PATH")
                if val:
                    return val
        except OSError:
            continue
    return None


def ids_from(path, tag):
    """取 XML 里某标签的 id 集合（文件不存在 = 空集，不致命）。"""
    if not path.is_file():
        return set()
    root = ET.parse(str(path)).getroot()
    return {el.get("id") for el in root.iter(tag) if el.get("id")}


def main():
    ap = argparse.ArgumentParser(description="Hero three-way key checker")
    ap.add_argument("--module", default=None, help="内容包目录（缺省 = 注册表 MB2_PATH 下的 Taikou）")
    args = ap.parse_args()

    module = Path(args.module) if args.module else (
        Path(registry_mb2_path()) / "Modules" / "Taikou" if registry_mb2_path() else None)
    if not module or not (module / "ModuleData").is_dir():
        print(f"[FATAL] 找不到内容包 ModuleData：{module}", file=sys.stderr)
        return 2
    data = module / "ModuleData"
    reg = data / "AssetRegistry"
    print(f"Module   : {module}")

    errors, warns = [], []

    # ── 输入表 ──
    # 🔴 角色模板**不止 spnpccharacters.xml 一个文件**（2026-09-12 修）：
    #    领主模板按年代切到了 `taikou_lords_<年>.xml`（模板要按代算年龄/装备），
    #    只扫 spnpccharacters 会把 4000+ 条英雄报成「无模板」（假红）。
    #    这里取**所有 NPCCharacters 段文件**的并集；GameType 级别的配对由 `check_hero_templates` 负责。
    template_ids = set()
    for tpl_file in sorted(data.glob("spnpccharacters*.xml")) + sorted(data.glob("taikou_lords*.xml")) \
            + sorted(data.glob("taikou_gangsters*.xml")):
        template_ids |= ids_from(tpl_file, "NPCCharacter")
    profile_ids = ids_from(reg / "HeroProfiles.xml", "HeroProfile")
    recommended = []
    hp = reg / "HeroProfiles.xml"
    if hp.is_file():
        for el in ET.parse(str(hp)).getroot().iter("Recommended"):
            if el.get("id"):
                recommended.append((int(el.get("order") or 0), el.get("id")))

    stage_ids = set()
    stages_csv = reg / "ProfileStages.csv"
    if stages_csv.is_file():
        with io.open(stages_csv, encoding="utf-8-sig", newline="") as f:
            for row in csv.DictReader(f):
                sid = (row.get("StringId") or "").strip()
                if sid:
                    stage_ids.add(sid)

    # ── 世界里的英雄（按时代段逐个文件）──
    hero_files = sorted(data.glob("taikou_heroes*.xml"))
    if not hero_files:
        print("[FATAL] 没找到 taikou_heroes*.xml", file=sys.stderr)
        return 2

    era_heroes = {}
    for f in hero_files:
        era_heroes[f.stem] = ids_from(f, "Hero")

    print(f"模板 {len(template_ids)} / 画像 {len(profile_ids)} / 立绘 {len(stage_ids)} 条"
          f" / 推荐 {len(recommended)} 个 / 时代段 {len(hero_files)} 个")

    # ── ① 每个时代的英雄都要有同名模板 ──
    for stem, heroes in era_heroes.items():
        for hid in sorted(heroes - EXEMPT_HERO_IDS):
            if hid not in template_ids:
                errors.append(f"[{stem}] 英雄 {hid} 无同名 NPCCharacter 模板（引擎会静默吞掉这条）")

    # ── ② 推荐人必须在 1560 那套英雄里 ──
    base_heroes = era_heroes.get(RECOMMEND_ERA_PATH, set())
    for order, hid in recommended:
        if hid not in base_heroes:
            errors.append(f"推荐人 #{order} {hid} 不在 {RECOMMEND_ERA_PATH}.xml 的英雄里"
                          f"（推荐列表会整行灰掉）")
        if hid not in template_ids:
            errors.append(f"推荐人 #{order} {hid} 无同名 NPCCharacter 模板")
        # ③ 推荐人必须有画像 + 立绘（推荐卡没画像是硬伤）
        if hid not in profile_ids:
            errors.append(f"推荐人 #{order} {hid} 在 HeroProfiles.xml 里没有画像条目")
        if hid not in stage_ids:
            errors.append(f"推荐人 #{order} {hid} 在 ProfileStages.csv 里没有立绘条目")

    # ── ④ 普通英雄缺画像/立绘：占位期正常，只 WARN ──
    for stem, heroes in era_heroes.items():
        for hid in sorted(heroes - EXEMPT_HERO_IDS):
            miss = []
            if hid not in profile_ids:
                miss.append("画像")
            if hid not in stage_ids:
                miss.append("立绘")
            if miss:
                warns.append(f"[{stem}] {hid} 缺 {'/'.join(miss)}（占位期正常；详情页该块显示占位）")

    # ── ⑤ 选人目录 ↔ 世界英雄集合（2026-09-11 加）──
    #   HeroCatalog.xml 是**建世界之前**选人界面唯一的取数来源（不读 Campaign.Current）。
    #   它的 id 集合必须与该时代真会建出来的英雄集合**逐个相等**——
    #   少了 = 该人不可选（静默消失）；多了 = 选了之后世界里找不到人（落地失败，回退建号）。
    #   口径：时代 1560 → taikou_heroes.xml（基线无后缀）；时代 <N> → taikou_heroes_<N>.xml
    #   （与据点文件同一条命名约定；改约定要同时改生成器 ERAS 表与本处）。
    catalog = reg / "HeroCatalog.xml"
    if not catalog.is_file():
        warns.append("HeroCatalog.xml 不存在——选人目录检查跳过（建世界之前的选人界面会没数据）")
    else:
        for era_node in ET.parse(str(catalog)).getroot().iter("Era"):
            era_id = era_node.get("id") or ""
            lords = {el.get("id") for el in era_node.iter("Lord") if el.get("id")}
            heroes_fn = data / ("taikou_heroes.xml" if era_id == "1560"
                                else "taikou_heroes_%s.xml" % era_id)
            if not heroes_fn.is_file():
                errors.append(f"选人目录有 Era {era_id}，但找不到对应英雄段 {heroes_fn.name}")
                continue
            world = ids_from(heroes_fn, "Hero") - EXEMPT_HERO_IDS
            # 目录里不含"建号占位家族"的人（内容包有权剔除），故只查「世界有而目录无」
            missing = sorted(world - lords)
            extra = sorted(lords - world)
            if missing:
                errors.append(f"[Era {era_id}] 世界有这些英雄但选人目录里没有（会不可选）：{missing}")
            if extra:
                errors.append(f"[Era {era_id}] 选人目录有这些但世界英雄段里没有（选了会落地失败）：{extra}")
            if not missing and not extra:
                print(f"  [OK] Era {era_id}：目录 {len(lords)} 人 == 世界英雄 {len(world)} 人")

        # 推荐人必须在 1560 那份目录里（推荐入口固定进 1560）
        era1560 = next((e for e in ET.parse(str(catalog)).getroot().iter("Era")
                        if e.get("id") == "1560"), None)
        lords1560 = {el.get("id") for el in era1560.iter("Lord")} if era1560 is not None else set()
        for order, hid in recommended:
            if hid not in lords1560:
                errors.append(f"推荐人 #{order} {hid} 不在 Era 1560 的选人目录里"
                              f"（推荐列表会显示「未登场」）")

    # ── 反向：立绘表覆盖了多少世界英雄（信息项）──
    all_world = set().union(*era_heroes.values()) if era_heroes else set()
    covered = len(all_world & stage_ids)
    print(f"世界英雄 {len(all_world)} 名，其中 {covered} 名查得到立绘")

    print("\n== 问题 ==")
    for e in errors:
        print(f"  [ERROR] {e}")
    for w in warns[:10]:
        print(f"  [WARN]  {w}")
    if len(warns) > 10:
        print(f"  [WARN]  … 另有 {len(warns) - 10} 条同类")
    if not errors and not warns:
        print("  （无 ✓）")
    print(f"\nSummary: errors={len(errors)} warnings={len(warns)}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())

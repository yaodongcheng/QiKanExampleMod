#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Culture-reference checker (自定义世界「文化引用悬空」离线体检 — 雷 11 族防线)
========================================================================
雷 11 根因（2026-09-07 T1 结案）：
  自定义 GameType 下官方 SPCultures 段被 IncludedGameTypes 白名单过滤，但内容包拷贝的官方文件
  （物品/装备/工艺件…）里带着 `Culture.empire` 这类**原版文化引用**——XML 引用解析走
  MBObjectManager.GetPresumedObject：对象不存在且该类型 AutoCreate=true 时**凭空造一个「裸文化桩」**
  （只记 id、从未 Deserialize → NotableAndWandererTemplates/LordTemplates 等模板列表为 null）
  → 引擎 CompanionsCampaignBehavior.InitializeCompanionTemplateList 无 null 保护 → NRE，世界建不出来。

本脚本把这道检查提前到离线：
  1. 段清单 = 本包 DependedModules 闭包内，所有「在目标 GameType 下会被加载」的 XmlNode
     （无 IncludedGameTypes = 全 GameType 加载；有 = 白名单含目标 GameType）
  2. 文化定义 = 所有被加载段里 <Culture id="...">（SPCultures 段）
  3. 文化引用 = 所有被加载段文件正文里的 `Culture.<id>` token
  4. 判定：引用 ⊄ 定义 → 悬空（运行时 = 造裸文化桩 → 模板列表 null → NRE）→ exit 1

Usage:
  python Scripts/check_culture_references.py [--module PATH] [--official-root PATH] [--game-type NAME]
  --official-root 缺省 = 读注册表 MB2_PATH（铁律 19：环境变量以注册表为准）
  --game-type 缺省 = 由 --module 的目录名推断（Taikou → TaikouCampaign）
Exit: 0 no dangling / 1 dangling found / 2 fatal.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# GBK 控制台下 ✓ 等符号会崩——统一 UTF-8 输出（py3.7+）
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

REF_RE = re.compile(r"\bCulture\.([A-Za-z_][A-Za-z0-9_]*)")


def registry_mb2_path():
    """读注册表 MB2_PATH（User 级优先，再 Machine）——铁律 19。"""
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


def module_closure(mod_root, start):
    """DependedModules 递归闭包（含自身）——只有被依赖的模块才会一起加载。"""
    seen, order = set(), []

    def visit(mod_id):
        if mod_id in seen:
            return
        seen.add(mod_id)
        sm = mod_root / mod_id / "SubModule.xml"
        if sm.is_file():
            try:
                root = ET.parse(str(sm)).getroot()
            except Exception:
                order.append(mod_id)
                return
            dep = root.find("DependedModules")
            if dep is not None:
                for d in dep.iter("DependedModule"):
                    if d.get("Id"):
                        visit(d.get("Id"))
        order.append(mod_id)

    visit(start)
    return order


def loaded_sections(mod_root, mod_id, game_type):
    """该模块在 game_type 下会被加载的 (XmlName.id, path) 列表。"""
    out = []
    sm = mod_root / mod_id / "SubModule.xml"
    if not sm.is_file():
        return out
    try:
        root = ET.parse(str(sm)).getroot()
    except Exception as e:
        print(f"  [WARN] {mod_id}/SubModule.xml 解析失败: {e}")
        return out
    for node in root.iter("XmlNode"):
        name = node.find("XmlName")
        if name is None or not name.get("path"):
            continue
        gt = node.find("IncludedGameTypes")
        if gt is None:
            out.append((name.get("id"), name.get("path")))
        elif any(g.get("value") == game_type for g in gt.iter("GameType")):
            out.append((name.get("id"), name.get("path")))
    return out


def section_files(mod_root, mod_id, path):
    """段 path → 实际文件（单文件 `path.xml` 或目录型段 `path/`——两者都试）。"""
    data = mod_root / mod_id / "ModuleData"
    for cand in (data / (path + ".xml"), data / path):
        if cand.is_file():
            return [cand]
        if cand.is_dir():
            return sorted(cand.rglob("*.xml"))
    return []



def inferred_game_types(mod_path, explicit=None):
    """本模块要体检的 GameType 列表。
    显式 --game-type → 只跑那一个（单跑语义不变）。
    缺省 = SubModule.xml 里所有「<模块名> + 数字年份」的 GameType（时代切换后一模块多 GameType：
    Taikou → TaikouCampaign1560 / TaikouCampaign1582 …），**每个都要跑**——
    只跑一个 = 另一个时代的段查不到 = 静默假绿（时代切换 spike 的核心风险点）。
    """
    if explicit:
        return [explicit]
    out = []
    sm = mod_path / "SubModule.xml"
    if sm.is_file():
        try:
            root = ET.parse(str(sm)).getroot()
        except Exception:
            root = None
        if root is not None:
            pat = re.compile(re.escape(mod_path.name) + r"Campaign\d{4}$")
            for g in root.iter("GameType"):
                v = g.get("value")
                if v and pat.fullmatch(v) and v not in out:
                    out.append(v)
    return out or [mod_path.name + "Campaign"]


def main():
    ap = argparse.ArgumentParser(description="Content-pack culture reference checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None,
                    help="游戏根（缺省=注册表 MB2_PATH）；内容包所在 <root>/Modules 下")
    ap.add_argument("--game-type", default=None, help="缺省=SubModule.xml 里本模块全部时代 GameType，各跑一遍")
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    mod_root = mod_path.parent

    root = Path(args.official_root) if args.official_root else None
    if root is None:
        mb2 = registry_mb2_path()
        root = Path(mb2) if mb2 else None
    if root is not None and (root / "Modules").is_dir() and root != mod_root:
        mod_root = root / "Modules"   # 以官方根为准（内容包在 <root>/Modules/<id>）
    if not mod_root.is_dir():
        print(f"[FATAL] Modules root not found: {mod_root}")
        return 2

    exit_codes = []
    for game_type in inferred_game_types(mod_path, args.game_type):
        print(f"Module   : {mod_path}")
        print(f"Modules根: {mod_root}")
        print(f"GameType : {game_type}")

        closure = module_closure(mod_root, mod_path.name)
        print(f"加载模块闭包: {' → '.join(closure)}\n")

        defined = {}      # 文化 id -> 定义处
        refs = {}         # 文化 id -> 引用处集合
        missing_culture = []   # (来源, 模板 id, 原因) —— 文化属性体检
        scanned = 0
        section_count = 0

        for mod_id in closure:
            for sec_id, path in loaded_sections(mod_root, mod_id, game_type):
                section_count += 1
                for f in section_files(mod_root, mod_id, path):
                    scanned += 1
                    try:
                        txt = f.read_text(encoding="utf-8", errors="replace")
                    except Exception:
                        continue
                    # 🔴 先剥 XML 注释再扫：注释里举例写的 `Culture.xxx`（文档/反编译摘录）也会被正则命中 = 假悬空
                    #   （2026-09-10 现场：taikou_module_strings.xml 的说明注释写了 "*.Culture.StringId" → 报假缺）
                    txt = re.sub(r"<!--.*?-->", "", txt, flags=re.S)
                    if sec_id == "SPCultures":
                        try:
                            for c in ET.fromstring(txt).iter("Culture"):
                                if c.get("id"):
                                    defined[c.get("id")] = f"{mod_id}/{f.name}"
                        except Exception:
                            pass
                    # 🔴 文化属性体检（2026-09-10 新增，守 CharacterCultureBackfill 退役后的不变量）：
                    #   每个 NPCCharacter 必须带 culture 属性、且指向已定义的文化——缺任一条 =
                    #   CharacterObject.Culture 为 null → 伤害模型 `.Culture.IsBandit` 裸解引用 NRE
                    #   （运行时旧兜底会"按生成地点猜一个文化"补进去 = 掩盖数据错，已退役；本规则是它的替代防线）
                    if sec_id == "NPCCharacters":
                        try:
                            for el in ET.fromstring(txt).iter("NPCCharacter"):
                                cid = el.get("id") or "<无 id>"
                                cult = el.get("culture")
                                if not cult:
                                    missing_culture.append((f"{mod_id}/{f.name}", cid, "缺少 culture 属性"))
                                else:
                                    m = re.match(r"\s*(?:Culture\.)?([A-Za-z_][A-Za-z0-9_]*)\s*$", cult)
                                    if not m:
                                        missing_culture.append((f"{mod_id}/{f.name}", cid, f"culture 值形状异常: {cult}"))
                                    elif m.group(1) not in defined:
                                        missing_culture.append((f"{mod_id}/{f.name}", cid, f"culture 指向未定义文化: {cult}"))
                        except Exception as e:
                            print(f"  [WARN] {mod_id}/{f.name} NPCCharacters 解析失败: {e}")
                    for m in REF_RE.finditer(txt):
                        refs.setdefault(m.group(1), set()).add(f"{mod_id}/{f.name}")

        print(f"扫描：{len(closure)} 模块 / {section_count} 段 / {scanned} 文件")
        print(f"文化定义（{len(defined)}）: " + ", ".join(f"{k}({v})" for k, v in sorted(defined.items())) or "（无）")

        dangling = {c: v for c, v in refs.items() if c not in defined}
        print(f"\n== 悬空文化引用（引用 ⊄ 定义 → 运行时会造裸文化桩 → NRE） ==")
        if not dangling:
            print("  （无）")
        else:
            for c in sorted(dangling):
                print(f"  [悬空] Culture.{c}  ← {len(dangling[c])} 文件: {sorted(dangling[c])[:6]}")

        # 文化属性体检：只看"我们自己"的模块（官方模块的数据我们改不了，单独作为提示列出）
        ours = [x for x in missing_culture if x[0].startswith(mod_path.name + "/")]
        others = [x for x in missing_culture if not x[0].startswith(mod_path.name + "/")]
        print(f"\n== 角色模板文化属性体检（缺 = 运行时 Culture null → 伤害模型 NRE） ==")
        if not missing_culture:
            print("  （无）")
        else:
            for src, cid, why in ours:
                print(f"  [缺失] {src} :: {cid} —— {why}")
            for src, cid, why in others[:20]:
                print(f"  [提示·官方模块] {src} :: {cid} —— {why}")
            if len(others) > 20:
                print(f"  [提示·官方模块] …另有 {len(others) - 20} 条")

        print(f"\nSummary: defined={len(defined)} referenced={len(refs)} dangling={len(dangling)} "
              f"missing_culture(ours)={len(ours)} missing_culture(official)={len(others)}")
        exit_codes.append(1) if (dangling or ours) else 0    # ← 必须在循环**内**：循环外只剩最后一趟的 errors（假绿，2026-09-12 修）



    return max(exit_codes) if exit_codes else 0

if __name__ == "__main__":
    sys.exit(main())

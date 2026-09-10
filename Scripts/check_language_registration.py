#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Language-file checker (语言文件登记 + 铁律 14 体检)
========================================================================
背景（2026-09-10 实机定位的雷 47）：
  引擎的语言文件是「**清单式**」加载——
  ① `LocalizedTextManager.LoadLocalizationXmls` 只扫各模块 `ModuleData/Languages/` 下的 `language_data.xml`；
  ② `LanguageData.Deserialize` 只收该文件里的 `<LanguageFile xml_path="..."/>` 条目。
  → **没登记在清单里的字符串文件 = 静默不加载**（玩家可见文本全落回英文 fallback，控制台零报错）。
  实机症状：中文对白里夹英文（「我是rightful daimyo of Japan …」）——Taikou 的 CN 文件自 2026-09-08 起就没生效。

本脚本体检四件事：
  1. 每个语言目录（`Languages/<lang>/`）必须有 `language_data.xml`（缺失 = 该目录全部字符串文件死掉）
  2. 目录下每个字符串 `*.xml` 都必须在清单里登记（未登记 = 静默不加载；清单里指向不存在的文件也报）
  3. 铁律 14：`Languages/` 下任何 XML 不得含 U+FFFF 以上字符（emoji = 引擎 UTF-16 XML 解析器遇代理对直接崩）
  4. **自有键的中文覆盖**（雷 48）：数据 XML 里 `{=自家前缀_…}` 的键必须在自家语言文件里有中文条目
     —— 缺了不报错，只是玩家看到英文（实机症状：中文游戏里冒出 "Oda Nobunaga"）。
     ⚠️ 只查「自家前缀」（默认 Taikou→TAIKOU_ / LivingWorldNpcs→LWN_）——官方键（物品名等）由原版 CN 覆盖，不该报。

Usage:
  python Scripts/check_language_registration.py [--modules-root PATH] [--module NAME ...] [--key-prefix MOD=PREFIX ...]
  --modules-root 缺省 = 注册表 MB2_PATH 的 Modules（铁律 19）
Exit: 0 clean / 1 problem found / 2 fatal.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg


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


def main():
    ap = argparse.ArgumentParser(description="Language file registration checker")
    ap.add_argument("--modules-root", default=None, help="缺省 = 注册表 MB2_PATH 的 Modules（铁律 19）")
    ap.add_argument("--module", action="append", default=None,
                    help="只查指定模块（可重复）；缺省 = Taikou + LivingWorldNpcs（本项目两块）")
    ap.add_argument("--key-prefix", action="append", default=None,
                    help="模块=键前缀（可重复），如 Taikou=TAIKOU_；缺省按内置表（Taikou→TAIKOU_ / LivingWorldNpcs→LWN_）")
    args = ap.parse_args()

    prefixes = {"Taikou": "TAIKOU_", "LivingWorldNpcs": "LWN_"}
    for kv in (args.key_prefix or []):
        if "=" in kv:
            m, p = kv.split("=", 1)
            prefixes[m.strip()] = p.strip()

    root = Path(args.modules_root) if args.modules_root else None
    if root is None:
        mb2 = registry_mb2_path()
        if not mb2:
            print("[FATAL] 读不到 MB2_PATH，且未给 --modules-root")
            return 2
        root = Path(mb2) / "Modules"
    if not (root / "Taikou").is_dir() and args.module is None:
        print(f"[FATAL] 模块根看起来不对：{root}")
        return 2

    modules = args.module or ["Taikou", "LivingWorldNpcs"]
    problems = []
    checked_dirs = 0

    for mod in modules:
        lang_root = root / mod / "ModuleData" / "Languages"
        if not lang_root.is_dir():
            print(f"[SKIP] {mod}: 无 Languages/ 目录")
            continue
        # ① 每个语言目录都要有 language_data.xml
        for lang_dir in sorted(d for d in lang_root.iterdir() if d.is_dir()):
            checked_dirs += 1
            manifest = lang_dir / "language_data.xml"
            files = sorted(p for p in lang_dir.glob("*.xml") if p.name != "language_data.xml")
            if not manifest.is_file():
                problems.append(f"{mod}/{lang_dir.name}: **缺 language_data.xml** → 该目录 {len(files)} 个字符串文件全部不会加载"
                                f"（{', '.join(f.name for f in files[:4])}{' …' if len(files) > 4 else ''}）")
                continue
            # ② 清单完整性（双向）
            try:
                listed = {e.get("xml_path") for e in ET.parse(str(manifest)).getroot().iter("LanguageFile")}
            except Exception as e:
                problems.append(f"{mod}/{lang_dir.name}/language_data.xml 解析失败: {e}")
                continue
            listed = {p.replace("\\", "/") for p in listed if p}
            for f in files:
                rel = f"{lang_dir.name}/{f.name}"
                if rel not in listed:
                    problems.append(f"{mod}: `{rel}` **未登记**（清单 {lang_dir.name}/language_data.xml 里没有）→ 静默不加载")
            for entry in sorted(listed):
                if not (lang_root / entry).is_file():
                    problems.append(f"{mod}: 清单里 `{entry}` **指向不存在的文件**")

    # ③ 铁律 14：emoji / 超 BMP 字符
    for mod in modules:
        lang_root = root / mod / "ModuleData" / "Languages"
        if not lang_root.is_dir():
            continue
        for p in lang_root.rglob("*.xml"):
            txt = p.read_text(encoding="utf-8", errors="replace")
            hits = sorted({ch for ch in txt if ord(ch) > 0xFFFF})
            if hits:
                problems.append(f"{mod}: `{p.relative_to(lang_root)}` 含超 BMP 字符 "
                                f"{[hex(ord(c)) for c in hits]} → 铁律 14（引擎 UTF-16 XML 解析器遇代理对直接崩）")

    # ④ 自有键的中文覆盖（雷 48：不报错，但中文游戏里显示英文）
    for mod in modules:
        pfx = prefixes.get(mod)
        data_root = root / mod / "ModuleData"
        if not pfx or not data_root.is_dir():
            continue
        used = {}
        for p in data_root.rglob("*.xml"):
            if "Languages" in p.parts:
                continue
            try:
                txt = p.read_text(encoding="utf-8", errors="replace")
            except Exception:
                continue
            for m in re.finditer(r"\{=(" + re.escape(pfx) + r"[A-Za-z0-9_]*)\}", txt):
                used.setdefault(m.group(1), p.name)
        have = set()
        for p in (data_root / "Languages").rglob("*.xml"):
            have |= set(re.findall(r'id="(' + re.escape(pfx) + r'[A-Za-z0-9_]*)"',
                                   p.read_text(encoding="utf-8", errors="replace")))
        if not used:
            continue
        absent = sorted(set(used) - have)
        print(f"[{mod}] 自有键中文覆盖：数据用 {len(used)} / 已有中文 {len(have)} / 缺 {len(absent)}")
        for k in absent[:20]:
            problems.append(f"{mod}: `{k}`（{used[k]}）**无中文条目** → 中文游戏里显示英文")
        if len(absent) > 20:
            problems.append(f"{mod}: 另有 {len(absent) - 20} 个自有键无中文条目（省略显示）")

    print(f"检查模块: {', '.join(modules)} | 语言目录 {checked_dirs} 个\n")
    if problems:
        print("== 问题 ==")
        for p in problems:
            print(f"  [!] {p}")
        print(f"\nSummary: problems={len(problems)}")
        return 1
    print("登记完整 + 无超 BMP 字符 ✓")
    print("Summary: problems=0")
    return 0


if __name__ == "__main__":
    sys.exit(main())

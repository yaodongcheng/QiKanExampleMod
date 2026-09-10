#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Official-copy checker (自定义世界「官方拷贝保持原样」离线体检)
========================================================================
覆盖「必备清单」§1.6 条目（雷 49）：
  「官方拷贝保持原样，自家串放自己的段」——9 个 GameText 文本段（+ 同类拷贝件）照官方**原样拷贝**，
  这样游戏更新时能整文件重拷/比对；**自家内容混进官方拷贝 = 更新时要么丢自家内容、要么手工合并**，
  且会出现「同一份官方文本两份拷贝、条目重复定义」。
  清单里给的手工手法 = `diff <官方文件> <本包同名文件>`（差异应为 0）——本脚本把它自动化。

规则：
  对每个「声明为官方原样拷贝」的文件名（默认 = 9 个 GameText 段）：
    1. 本包没有该文件 → 跳过（没拷贝就没这回事；「必须拷贝」由 check_module_registration 管）
    2. 在**加载闭包**里找官方同名文件（Native/SandBox/SandBoxCore/…）
       —— 找不到 → WARN（来源可能是别的模块或工具产物）
    3. 规范化（去 BOM、统一换行）后逐行比对：有差异 → ERROR 并打印**前几条差异行号**

Usage:
  python Scripts/check_official_copies.py [--module PATH] [--official-root PATH] [--game-type NAME]
Exit: 0 无差异 / 1 有差异 / 2 fatal。
"""
import argparse
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

# 声明为「官方原样拷贝」的文件 —— (文件名, 源头模块)。
# 🔴 源头模块必须写明：多个官方模块有**同名但不同内容**的文件（Native/module_strings.xml 6887 行
#    vs SandBox/module_strings.xml 663 行），猜源头 = 必然误报。
VERBATIM_FILES = [
    ("module_strings", "SandBox"), ("world_lore_strings", "SandBox"),
    ("companion_strings", "SandBox"), ("wanderer_strings", "SandBox"),
    ("comment_strings", "SandBox"), ("comment_on_action_strings", "SandBox"),
    ("trait_strings", "SandBox"), ("voice_strings", "SandBox"), ("action_strings", "SandBox"),
]
MAX_DIFF_LINES = 6


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


def norm_lines(p):
    txt = p.read_text(encoding="utf-8-sig", errors="replace").replace("\r\n", "\n").replace("\r", "\n")
    return txt.split("\n")


def main():
    ap = argparse.ArgumentParser(description="Official-copy checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None)
    ap.add_argument("--game-type", default=None)
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
        mod_root = root / "Modules"
    print(f"Module : {mod_path}")
    print(f"Modules根: {mod_root}\n")

    if not mod_root.is_dir():
        print(f"[FATAL] Modules root not found: {mod_root}")
        return 2
    # 官方候选模块（按优先级；只在其中找同名文件）
    candidates = [m for m in ("Native", "SandBox", "SandBoxCore") if (mod_root / m).is_dir()]
    print(f"官方候选模块: {candidates}\n")

    errors, warns = [], []
    print("== 官方拷贝原样性（规范化后逐行比对；差异应为 0，雷 49） ==")
    for name, src_mod in VERBATIM_FILES:
        ours = mod_path / "ModuleData" / (name + ".xml")
        if not ours.is_file():
            print(f"  [SKIP] {name}.xml —— 本包没有（未拷贝；是否「必须拷贝」由 module_registration 检查）")
            continue
        # 源头模块优先（声明的），找不到再按候选顺序兜底（并提示）
        order = ([src_mod] if src_mod in candidates else []) + [m for m in candidates if m != src_mod]
        src = None
        for m in order:
            cand = mod_root / m / "ModuleData" / (name + ".xml")
            if cand.is_file():
                src = cand
                if m != src_mod:
                    warns.append(f"{name}: 声明的源头 {src_mod} 里没有，退用 {m}")
                    print(f"  [WARN] {name}.xml —— 声明的源头 {src_mod} 里没有该文件，退用 {m} 比对")
                break
        if src is None:
            warns.append(f"{name}: 官方候选里找不到同名文件")
            print(f"  [WARN] {name}.xml —— 官方候选模块里找不到同名文件，无法比对")
            continue
        a, b = norm_lines(ours), norm_lines(src)
        if a == b:
            print(f"  [ OK ] {name}.xml（{len(a)} 行，与 {src.parent.parent.name} 逐字一致）")
            continue
        diffs = [(i + 1, x, y) for i, (x, y) in enumerate(zip(a, b)) if x != y]
        errors.append(f"{name}.xml 与官方有 {len(diffs)} 行差异")
        print(f"  [ERROR] {name}.xml 与 {src.parent.parent.name}/{name}.xml **有差异**"
              f"（本包 {len(a)} 行 / 官方 {len(b)} 行）—— 自家内容混进官方拷贝了（雷 49）")
        for ln, x, y in diffs[:MAX_DIFF_LINES]:
            print(f"           L{ln}: 本包 {x.strip()[:70]!r}")
            print(f"           L{ln}: 官方 {y.strip()[:70]!r}")
        if len(diffs) > MAX_DIFF_LINES:
            print(f"           …另有 {len(diffs) - MAX_DIFF_LINES} 行差异")

    print(f"\nSummary: checked={len(VERBATIM_FILES)} errors={len(errors)} warnings={len(warns)}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())

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

本脚本体检五件事：
  1. 每个语言目录（`Languages/<lang>/`）必须有 `language_data.xml`（缺失 = 该目录全部字符串文件死掉）
  2. 目录下每个字符串 `*.xml` 都必须在清单里登记（未登记 = 静默不加载；清单里指向不存在的文件也报）
  3. 铁律 14：`Languages/` 下任何 XML 不得含 U+FFFF 以上字符（emoji = 引擎 UTF-16 XML 解析器遇代理对直接崩）
  4. **自有键的中文覆盖**（雷 48）：数据 XML 里 `{=自家前缀_…}` 的键必须在自家语言文件里有中文条目
     —— 缺了不报错，只是玩家看到英文（实机症状：中文游戏里冒出 "Oda Nobunaga"）。
     ⚠️ 只查「自家前缀」（默认 Taikou→TAIKOU_ / LivingWorldNpcs→LWN_）——官方键（物品名等）由原版 CN 覆盖，不该报。
  5. **每条 `<string>` 必须在 `<strings>` 之内**（2026-09-12 实机事故）：引擎只读 `<strings>` 的
     直接子节点，写在 `</strings>` 之后的条目**一条都不加载**、零报错（玩家看到英文 fallback）。

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


def first_construct_after_decl(path):
    """返回 XML 声明之后紧跟的构造类型：'element' / 'comment' / '?'（读不出）。
    🔴 雷 50：引擎语言加载器按 `xmlDocument.ChildNodes[1].FirstChild` 取 <strings>——
       ChildNodes[0] = XML 声明、[1] 必须是根元素；声明与根元素之间夹注释 → [1] 变注释
       → FirstChild 为 null → **整份字符串文件静默不加载**（玩家看到全英文，零报错）。"""
    b = path.read_bytes()
    if b[:2] in (b"\xff\xfe", b"\xfe\xff"):
        t = b.decode("utf-16", errors="replace")
    else:
        t = b.decode("utf-8-sig", errors="replace")
    m = re.match(r"\s*<\?xml[^>]*\?>", t)
    rest = (t[m.end():] if m else t).lstrip()
    if rest.startswith("<!--"):
        return "comment"
    if rest.startswith("<"):
        return "element"
    return "?"


def read_lang_text(path):
    """按 BOM 解码语言 XML（官方文件是 UTF-16，按 UTF-8 读会得到乱码 → 键集抓空 = 满屏假报）。"""
    b = path.read_bytes()
    if b[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return b.decode("utf-16", errors="replace")
    return b.decode("utf-8-sig", errors="replace")


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

    # 🔴 `--module` 既收**模块名**（Taikou）也收**模块路径**（`run_all_checks.py` 传的就是路径）——
    #    只认名字的话，套件里传路径 = 每个模块都解析成 `<根>/<长路径>` 找不到 → [SKIP] 静默跳过
    #    → 语言检查在套件里**永远是绿的**（假绿；2026-09-12 发现，LWN 的未登记文件因此长期没被套件抓到）。
    targets = []                                  # (显示名, 模块根目录, 键前缀)
    for m in (args.module or ["Taikou", "LivingWorldNpcs"]):
        pm = Path(m)
        if pm.is_dir():
            targets.append((pm.name, pm, prefixes.get(pm.name)))
        else:
            targets.append((m, root / m, prefixes.get(m)))
    problems = []
    checked_dirs = 0

    for mod, mod_root, _pfx in targets:
        lang_root = mod_root / "ModuleData" / "Languages"
        if not lang_root.is_dir():
            print(f"[SKIP] {mod}: 无 Languages/ 目录")
            continue
        # ① 每个语言目录都要有 language_data.xml（根级 Languages/ 也算一层：默认语言层）
        scopes = [(lang_dir, lang_dir.name) for lang_dir in sorted(d for d in lang_root.iterdir() if d.is_dir())]
        scopes.append((lang_root, ""))            # 根级：清单 = Languages/language_data.xml，文件名不带前缀
        for scope_dir, scope_name in scopes:
            checked_dirs += 1
            label = f"{mod}/{scope_name}" if scope_name else f"{mod}（根级/默认语言）"
            manifest = scope_dir / "language_data.xml"
            files = sorted(p for p in scope_dir.glob("*.xml") if p.name != "language_data.xml")
            if not manifest.is_file():
                problems.append(f"{label}: **缺 language_data.xml** → 该目录 {len(files)} 个字符串文件全部不会加载"
                                f"（{', '.join(f.name for f in files[:4])}{' …' if len(files) > 4 else ''}）")
                continue
            # ② 清单完整性（双向）
            try:
                listed = {e.get("xml_path") for e in ET.parse(str(manifest)).getroot().iter("LanguageFile")}
            except Exception as e:
                problems.append(f"{label}/language_data.xml 解析失败: {e}")
                continue
            listed = {p.replace("\\", "/") for p in listed if p}
            for f in files:
                rel = f"{scope_name}/{f.name}" if scope_name else f.name
                if rel not in listed:
                    problems.append(f"{mod}: `{rel}` **未登记**（清单 {rel.split('/')[0] + '/' if scope_name else ''}language_data.xml 里没有）→ 静默不加载")
            for entry in sorted(listed):
                if not (lang_root / entry).is_file():
                    problems.append(f"{mod}: 清单里 `{entry}` **指向不存在的文件**")
            # ⑤ 根元素前禁止夹注释（雷 50）
            for f in files:
                if first_construct_after_decl(f) == "comment":
                    rel = f"{scope_name}/{f.name}" if scope_name else f.name
                    problems.append(f"{mod}: `{rel}` **XML 声明与根元素之间夹了注释** "
                                    f"→ 引擎按 ChildNodes[1] 取根会拿到注释 → 整文件静默不加载（雷 50）")
            # ⑥ 每条 <string> 必须在 <strings> **之内**（雷 53；2026-09-12 实机事故）
            #    引擎 `LocalizedTextManager.LoadLanguage` 只遍历 `<strings>` 的**直接子节点**里
            #    Name=="string" 的那些；丢在 `<strings>` 之外（哪怕还在根元素里）的条目
            #    **一条都不会加载**，且零报错、零崩溃——玩家只是看到数据 XML 里的英文 fallback。
            #    实机症状：选人界面的 势力/家族/英雄 三列全英文（4985 条中文在 `</strings>` 之后）。
            for f in files:
                rel = f"{scope_name}/{f.name}" if scope_name else f.name
                try:
                    r = ET.fromstring(read_lang_text(f))
                except Exception as e:
                    problems.append(f"{mod}: `{rel}` XML 解析失败: {e}")
                    continue
                every = list(r.iter("string"))
                if not every:
                    continue                        # 非字符串文件（如只有 <functions>）
                if r.find("strings") is None:
                    shape = ("根元素就是 `<strings>`（引擎要的是 `<base type=\"string\">` 包一层）"
                             if r.tag == "strings" else
                             f"根元素 `<{r.tag}>` 下没有 `<strings>`")
                    problems.append(
                        f"{mod}: `{rel}` **{shape}**（共 {len(every)} 条 string）"
                        f" → 引擎在根的直接子节点里只认 `<strings>`，本文件一条都不加载")
                    continue
                inner_ids = {id(s) for s in r.findall("strings/string")}
                outside = [s for s in every if id(s) not in inner_ids]
                if outside:
                    sample = [s.get("id") for s in outside[:5]]
                    problems.append(
                        f"{mod}: `{rel}` **{len(outside)} 条 <string> 在 <strings> 之外**（例：{sample}）"
                        f" → 引擎只读 strings 的直接子节点，块外条目一条都不加载"
                        f"（玩家看到英文 fallback，零报错）")

    # ③ 铁律 14：emoji / 超 BMP 字符
    for mod, mod_root, _pfx in targets:
        lang_root = mod_root / "ModuleData" / "Languages"
        if not lang_root.is_dir():
            continue
        for p in lang_root.rglob("*.xml"):
            txt = p.read_text(encoding="utf-8", errors="replace")
            hits = sorted({ch for ch in txt if ord(ch) > 0xFFFF})
            if hits:
                problems.append(f"{mod}: `{p.relative_to(lang_root)}` 含超 BMP 字符 "
                                f"{[hex(ord(c)) for c in hits]} → 铁律 14（引擎 UTF-16 XML 解析器遇代理对直接崩）")

    # ④ 自有键的中文覆盖（雷 48 / 雷 51：不报错，但中文游戏里显示英文）
    #    「自有键」判据 = 本模块数据/C# 用到、而**官方语言文件里没有**的键
    #    （官方键如物品名由原版 CN 覆盖，不该报；名字池那种 8 位随机 key 也照样能抓——2026-09-10 教训）
    vanilla_keys = set()
    for off in ("Native", "SandBox", "SandBoxCore", "StoryMode", "CustomBattle"):
        off_lang = root / off / "ModuleData" / "Languages"
        if not off_lang.is_dir():
            continue
        for p in off_lang.rglob("*.xml"):
            if p.name == "language_data.xml":
                continue
            vanilla_keys |= set(re.findall(r'id="([A-Za-z0-9_]+)"', read_lang_text(p)))
    # 「官方自己也在用」的键也算官方（官方数据 XML 里出现过的 {=KEY}）——否则会把官方文本段拷贝里
    # 那些"官方自己都没放进语言文件"的键误判成我们的（2026-09-10 假报实录：comment_strings 的一堆 8 位键）
    for off in ("Native", "SandBox", "SandBoxCore", "StoryMode", "CustomBattle"):
        off_data = root / off / "ModuleData"
        if not off_data.is_dir():
            continue
        for p in off_data.rglob("*.xml"):
            if "Languages" in p.parts:
                continue
            try:
                vanilla_keys |= set(re.findall(r"\{=([A-Za-z0-9_]+)\}",
                                               p.read_text(encoding="utf-8", errors="replace")))
            except Exception:
                continue
    for mod, mod_root, pfx in targets:
        data_root = mod_root / "ModuleData"
        if not data_root.is_dir():
            continue
        used = {}
        # ④a 数据 XML（含 GameText 里的 {=内层键} 与 name 属性的 {=键}）
        for p in data_root.rglob("*.xml"):
            if "Languages" in p.parts:
                continue
            try:
                txt = p.read_text(encoding="utf-8", errors="replace")
            except Exception:
                continue
            for m in re.finditer(r"\{=([A-Za-z0-9_]+)\}", txt):
                used.setdefault(m.group(1), p.name)
        # ④b C# 源码（LWN 的玩家可见文本写在代码里；**先剥注释**——文档里举例的 `{=LWN_KEY}` 不是真键）
        #     ⚠️ 跳过反编译参考副本（Modules/decompile/**）——那是别的版本的原版代码，键不是我们的
        #     （2026-09-10 假报实录：35 个"缺中文"的键全来自 decompile 副本）
        for p in mod_root.rglob("*.cs"):
            if {"obj", "bin", "decompile"} & set(p.parts):
                continue
            try:
                txt = p.read_text(encoding="utf-8", errors="replace")
            except Exception:
                continue
            txt = re.sub(r"/\*.*?\*/", "", txt, flags=re.S)
            txt = re.sub(r"(?m)//.*$", "", txt)
            for m in re.finditer(r"\{=([A-Za-z0-9_]+)\}", txt):
                used.setdefault(m.group(1), p.name)
        # 只留「自有键」（官方语言文件里没有的）
        used = {k: v for k, v in used.items() if k not in vanilla_keys}
        have = set()
        for p in (data_root / "Languages").rglob("*.xml"):
            have |= set(re.findall(r'id="([A-Za-z0-9_]+)"', read_lang_text(p)))
        if not used:
            continue
        absent = sorted(set(used) - have)
        print(f"[{mod}] 自有键中文覆盖：用 {len(used)} / 已有中文 {len(have)} / 缺 {len(absent)}")
        for k in absent[:20]:
            problems.append(f"{mod}: `{k}`（{used[k]}）**无中文条目** → 中文游戏里显示英文")
        if len(absent) > 20:
            problems.append(f"{mod}: 另有 {len(absent) - 20} 个自有键无中文条目（省略显示）")

    print(f"检查模块: {', '.join(t[0] for t in targets)} | 语言目录 {checked_dirs} 个\n")
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

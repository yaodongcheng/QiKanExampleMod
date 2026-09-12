#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Taikou 英文语言文件生成器（Languages/ 根级 = 默认语言层）
========================================================================
为什么要有根级英文文件（2026-09-10 用户裁定）：
  官方模块与 LWN 的 Languages/ 结构都是两层——
    Languages/language_data.xml + Languages/std_<模块>_strings.xml   ← 默认语言（English）
    Languages/<语言>/language_data.xml + std_<模块>_strings.xml      ← 该语言翻译
  Taikou 原先只有中文层，根级英文层缺失 = 结构不完整（英文虽靠内联 fallback 能显示，
  但没有可翻译/可审校的英文单一入口）。

本脚本的产出（**生成物，禁止手改**——铁律 22）：
  1. `Modules/Taikou/ModuleData/Languages/std_Taikou_strings.xml`  ← 英文，从数据 XML 的
     `{=TAIKOU_KEY}English fallback` 内联 fallback 全量抽取（单一事实源 = 数据 XML）
  2. `Modules/Taikou/ModuleData/Languages/language_data.xml`       ← English 清单（列上面那份）

Usage:
  python Scripts/gen_taikou_english_strings.py            # 重跑产出
  python Scripts/gen_taikou_english_strings.py --check    # 只校验是否已最新（不一致 exit 1）
"""
import argparse
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MODULE = Path(r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
PREFIX = "TAIKOU_"
STRINGS_NAME = "std_Taikou_strings.xml"

# {=TAIKOU_key}fallback —— fallback 到下一个 { 或引号为止
REF = re.compile(r"\{=(" + PREFIX + r"[A-Za-z0-9_]+)\}([^{}\"]*)")

XML_HEADER = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
# 🔴 注释必须写在根元素**里面**（雷 50）：引擎语言加载器按 `xmlDocument.ChildNodes[1].FirstChild` 取 strings——
#    ChildNodes[0] 是 XML 声明、[1] 必须是根元素；声明与根元素之间夹注释 → [1] 变注释 → FirstChild 为 null
#    → 整个文件静默不加载（玩家看到全英文，控制台零报错）。2026-09-10 实机踩过。
GENERATED_NOTE = ("  <!-- 【生成物·禁止手改】由 Scripts/gen_taikou_english_strings.py 从数据 XML 的\n"
                  "       {=TAIKOU_KEY}English fallback 全量抽取生成——改英文请改数据 XML 的 fallback 再重跑（铁律 22）。 -->\n")


def collect():
    """扫数据 XML（不含 Languages/），收集 key → 英文 fallback。"""
    pairs = {}
    dup = []
    for p in sorted((MODULE / "ModuleData").rglob("*.xml")):
        if "Languages" in p.parts:
            continue
        txt = p.read_text(encoding="utf-8", errors="replace")
        for m in REF.finditer(txt):
            key, fallback = m.group(1), m.group(2)
            if key in pairs and pairs[key] != fallback:
                dup.append((key, pairs[key], fallback, p.name))
            pairs.setdefault(key, fallback)
    return pairs, dup


def build_strings_xml(pairs):
    out = [XML_HEADER, "<base type=\"string\">\n", GENERATED_NOTE,
           "  <tags>\n    <tag language=\"English\" />\n  </tags>\n",
           "  <strings>\n"]
    for key in sorted(pairs):
        text = pairs[key].replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        out.append(f"    <string id=\"{key}\" text=\"{text}\" />\n")
    out.append("  </strings>\n</base>\n")
    return "".join(out)


def build_manifest():
    return (XML_HEADER +
            "<LanguageData id=\"English\" name=\"English\" subtitle_extension=\"en-GB\" "
            "supported_iso=\"en-GB,en-US,en,eng,en-us,en-gb,en-au,en-bz,en-ca,en-ie,en-jm,en-nz,en-za,en-tt\" "
            "text_processor=\"TaleWorlds.Localization.TextProcessor.LanguageProcessors.EnglishTextProcessor\" "
            "under_development=\"false\">\n"
            + GENERATED_NOTE.replace("  <!--", "  <!--") +
            f"  <LanguageFile xml_path=\"{STRINGS_NAME}\" />\n"
            "</LanguageData>\n")


def main():
    ap = argparse.ArgumentParser(description="Taikou English language files generator")
    ap.add_argument("--check", action="store_true", help="只校验是否已最新")
    # 兼容 run_all_checks 的接口（它给每个脚本追加 --module <内容包路径>）——
    # 🔴 不接这个参数 = argparse exit 2，体检里只看到红（雷 92 实录），一律加上。
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（本生成器不用）")
    args = ap.parse_args()

    pairs, dup = collect()
    if not pairs:
        print("[FATAL] 没抽到任何 {=TAIKOU_*} 键——路径对不对？")
        return 2
    for key, a, b, where in dup:
        print(f"  [WARN] 同键不同文（以先到者为准）: {key} | {a!r} vs {b!r} ({where})")

    lang = MODULE / "ModuleData" / "Languages"
    lang.mkdir(parents=True, exist_ok=True)
    targets = {lang / STRINGS_NAME: build_strings_xml(pairs),
               lang / "language_data.xml": build_manifest()}

    stale = []
    for path, content in targets.items():
        old = path.read_text(encoding="utf-8", errors="replace") if path.is_file() else None
        if old != content:
            stale.append(path)
            if not args.check:
                path.write_text(content, encoding="utf-8")

    print(f"抽取 {len(pairs)} 个 {PREFIX}* 键")
    if args.check:
        if stale:
            print("[STALE] 以下生成物与数据不一致，重跑生成器：")
            for p in stale:
                print(f"  {p.relative_to(lang)}")
            return 1
        print("生成物已最新 ✓")
        return 0
    for p in targets:
        print(f"  写出 {p.relative_to(lang)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

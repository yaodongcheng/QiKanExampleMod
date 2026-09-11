#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""太阁列传 → 中文语言层（CNs）同步器
============================================================================
为什么单独一支脚本
------------------
`Languages/CNs/std_Taikou_strings.xml` 是**人工维护**的翻译文件（键集必须与根级英文层
逐键相等）。但列传那 800 条**不是翻译**——是太阁5 游戏自带的原文数据，人不可能手抄，
性质上属「数据」而非「译文」。故本脚本只负责把这一块搬进去，且：

  · 只动 `TAIKOU_bio_*` 这一个块（带标记注释，可一眼识别、可整体替换）
  · 其余键一个字节都不碰（改前改后逐键比对）
  · 幂等：重跑 = 整块替换，不追加

数据源 = `csv/TaikouHero.csv` 的 `列传`（StringId）+ `列传简体`（正文），
由 `Scripts/import_taikou_hero_bios.py` 从 tkhack 实机日志落库。

用法
----
  python Scripts/sync_taikou_bio_cns.py --check   # 只校验是否已同步（不一致 exit 1）
  python Scripts/sync_taikou_bio_cns.py           # 写入
"""
import argparse
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")

if sys.platform == "win32":
    import winreg

MARK_BEGIN = "    <!-- ==== TAIKOU_bio_* 起（生成块·勿手改）===="
MARK_END = "    <!-- ==== TAIKOU_bio_* 止 ==== -->"
BLOCK_NOTE = (
    MARK_BEGIN + "\n"
    "         太阁5 列传数据（**不是翻译**，是游戏自带原文）——由\n"
    "         Scripts/sync_taikou_bio_cns.py 从 csv/TaikouHero.csv 的\n"
    "         列传 / 列传简体 两列生成。改列传 = 改 CSV 后重跑（铁律 22）。\n"
    "         键集与根级 std_Taikou_strings.xml 的 TAIKOU_bio_* 一一对应。 -->\n"
)


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


def esc(s):
    return (str(s).replace("&", "&amp;").replace("<", "&lt;")
            .replace(">", "&gt;").replace('"', "&quot;"))


def bio_entries():
    """→ [(key, 简体正文), …]（按番号序）

    🔴 2026-09-11：CSV 的 `列传简体` 列已删（用户裁定：列传只留繁体原文，
       简体/英文在**生成本地化产物时**再走正式流程）。故本脚本自己用 opencc 转简体——
       转换点从「CSV 落库时」挪到「生成语言层时」，语义不变、少一列。
    """
    try:
        import opencc
        cc = opencc.OpenCC("t2s")
    except Exception as exc:                                 # noqa: BLE001
        raise SystemExit(f"[FATAL] 需要 opencc 转简体：{exc}")
    out = []
    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        for r in csv.DictReader(fh):
            key = (r.get("列传") or "").strip()
            text = (r.get("列传原文") or "").strip()
            if key and text:
                out.append((key, cc.convert(text)))
    return out


def strip_block(text):
    """移除既有生成块（含标记），返回 (无块文本, 原块行数)。"""
    if MARK_BEGIN not in text:
        return text, 0
    i = text.index(MARK_BEGIN)
    j = text.index(MARK_END, i) + len(MARK_END)
    # 连同结束行后的换行一起吃掉
    while j < len(text) and text[j] in "\r\n":
        j += 1
    return text[:i] + text[j:], text[i:j].count("\n")


def main():
    ap = argparse.ArgumentParser(description="Taikou bio -> CNs strings sync")
    ap.add_argument("--check", action="store_true", help="只校验，不写")
    ap.add_argument("--module", default=None, help="内容包目录（缺省 = 注册表 MB2_PATH 下的 Taikou）")
    args = ap.parse_args()

    mb2 = registry_mb2_path()
    module = args.module or (os.path.join(mb2, "Modules", "Taikou") if mb2 else None)
    cns = os.path.join(module, "ModuleData", "Languages", "CNs", "std_Taikou_strings.xml")
    if not os.path.isfile(cns):
        print(f"[FATAL] 找不到 CNs 语言文件：{cns}", file=sys.stderr)
        return 2

    raw = io.open(cns, encoding="utf-8", newline="").read()
    entries = bio_entries()
    if not entries:
        print("[FATAL] CSV 里没有 列传/列传简体 数据——先跑 import_taikou_hero_bios.py", file=sys.stderr)
        return 2

    base, removed = strip_block(raw)

    # 已有键：防止与块外的同键条目撞车
    outside = set(re.findall(r'<string id="(TAIKOU_bio_\d+)"', base))
    if outside:
        print(f"[FATAL] 块外已存在 {len(outside)} 个 TAIKOU_bio_* 键（{sorted(outside)[:5]}…）——"
              f"先人工清理，避免同键两条", file=sys.stderr)
        return 2

    anchor_m = re.search(r"[ \t]*<strings>[ \t]*\r?\n", base)
    if not anchor_m:
        print("[FATAL] 找不到 `<strings>` 锚点——CNs 结构变了？", file=sys.stderr)
        return 2
    eol = "\r\n" if anchor_m.group(0).endswith("\r\n") else "\n"
    block = (BLOCK_NOTE
             + "".join(f'    <string id="{k}" text="{esc(t)}" />\n' for k, t in entries)
             + MARK_END + "\n").replace("\n", eol)
    new = base[:anchor_m.end()] + block + base[anchor_m.end():]

    # ── 自检：除生成块外，逐键一致 ──
    new_base, _ = strip_block(new)
    old_pairs = re.findall(r'<string id="([^"]+)" text="([^"]*)"\s*/>', base)
    new_pairs = re.findall(r'<string id="([^"]+)" text="([^"]*)"\s*/>', new_base)
    if old_pairs != new_pairs:
        print(f"[FATAL] 除生成块外键集发生变化：{len(old_pairs)} → {len(new_pairs)}", file=sys.stderr)
        return 2
    print(f"生成块：{len(entries)} 条列传（替换掉旧块 {removed} 行）"
          f"；块外 {len(old_pairs)} 键逐键未动")

    if args.check:
        if raw == new:
            print("OK：CNs 已同步")
            return 0
        print("产物与数据不一致（需重跑）")
        return 1

    if raw != new:
        io.open(cns, "w", encoding="utf-8", newline="").write(new)
        print(f"已写入 {cns}")
    else:
        print("未变（已是最新）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

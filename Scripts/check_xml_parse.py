#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""XML 全量 parse 门（内容包每一个 XML 都必须解析得过）
============================================================================
**为什么单独一条**（雷 8 的离线防线，2026-09-12 补齐）：
  · **改 XML 必 parse** 是清单里的硬纪律（雷 8 教训：标签未闭合 = 首发铁定 NRE，
    且症状错位迷惑，当年排查烧掉 15 轮）——但此前**没有任何脚本真正全局把守**：
    各 checker 各写各的容错，多数是 `except: continue` **静默跳过**坏文件
    （`check_data_fields` 只打印 `[FILE-ERROR]` 不进 errors；`check_required_ids` /
    `check_culture_text_variants` / `check_era_segments` / `check_hero_templates` 直接 continue）
    → **文件坏掉时整条体检仍可能全绿**（假绿，同雷 99 一族）。
  · 清单第 187 行曾写「一键里含 ⑱改过的 XML 全 parse」——**实际不存在这一条**，
    本条就是把那句话兑现。

覆盖范围：内容包目录下**全部** `*.xml`（含 `ModuleData/**`、`GUI/`、`SubModule.xml`）
  —— 不按段清单筛，因为「没被段引用的 XML 解析坏了」同样是数据坏掉的信号。

🔴 判定：任一文件 parse 失败 → **exit 1 并点名文件 + 行列号**（不是警告，无豁免）。
   唯一豁免 = `_archive/`（归档目录，不参与加载；写死并打印理由）。

Usage:
  python Scripts/check_xml_parse.py [--module PATH]
Exit: 0 全部解析通过 / 1 有文件坏 / 2 fatal。
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

# 归档目录不参与加载（不是数据，是留档）——豁免要写理由，别默默跳过
EXEMPT_DIRS = ("_archive",)


def main():
    ap = argparse.ArgumentParser(description="Content-pack XML parse gate")
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    root = Path(args.module)
    if not root.is_dir():
        print("[FATAL] module not found: %s" % root, file=sys.stderr)
        return 2

    files = sorted(p for p in root.rglob("*.xml")
                   if not any(d in p.parts for d in EXEMPT_DIRS))
    if not files:
        print("[FATAL] 目录下没有任何 XML：%s" % root, file=sys.stderr)
        return 2

    bad, exempt = [], []
    for p in root.rglob("*.xml"):
        if any(d in p.parts for d in EXEMPT_DIRS):
            exempt.append(str(p.relative_to(root)))
    for p in files:
        try:
            ET.parse(str(p))
        except ET.ParseError as e:
            bad.append((str(p.relative_to(root)), "XML 语法错误：%s（行 %s 列 %s）"
                        % (e.msg, getattr(e, "position", ("?", "?"))[0],
                           getattr(e, "position", ("?", "?"))[1])))
        except OSError as e:
            bad.append((str(p.relative_to(root)), "读文件失败：%s" % e))

    print("XML parse 门（%s）" % root)
    print("  扫描 %d 个 XML" % len(files)
          + ("，豁免 %d 个（%s）" % (len(exempt), "/".join(EXEMPT_DIRS)) if exempt else ""))
    if args.verbose:
        for p in files:
            print("    ✓ %s" % p.relative_to(root))
    if bad:
        print("\n❌ 解析失败 %d 个：" % len(bad))
        for name, why in bad:
            print("  ▸ %s\n      %s" % (name, why))
        print("\n修法：改完 XML 立刻重跑本脚本；**别靠游戏启动来发现**（雷 8：症状错位、排查 15 轮）。")
        return 1
    print("\n✅ 全部 %d 个 XML 解析通过" % len(files))
    return 0


if __name__ == "__main__":
    sys.exit(main())

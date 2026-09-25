#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成后自检：把页面里的 <script> 抠出来喂给 `node --check`。

🔴 **为什么必须有这道检查**（2026-09-25 实锤）：
   生成器模板里一个转义写错（`\\n` 被 Python 吃掉 → JS 字符串跨行）⇒ **整个 <script> 语法错**
   ⇒ 页面"能显示但一行 JS 都不跑"：画布全空、面板空白，**肉眼完全看不出是语法错**（当时排查了好几轮）。
   拿 node 一跑就定位到了 —— 所以固化成本脚本，改完生成器**先跑它**。

用法（在 tools/anim-statemachine 下）：
    python check_pages.py                # 检查本目录所有 .html
    python check_pages.py xxx.html       # 只检查指定页面（相对路径按本目录解析）
没装 node 就跳过（不阻塞）。
"""
import io
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TMP_DIR = os.path.join(HERE, "out")     # 临时 js 丢这儿（out/ 是忽略目录，别在源码目录留垃圾）


def check(path):
    html = io.open(path, encoding="utf-8").read()
    blocks = re.findall(r"<script>([\s\S]*?)</script>", html)
    if not blocks:
        print("  %-34s （没有内联脚本，跳过）" % os.path.basename(path))
        return True
    tmp = os.path.join(TMP_DIR, "_syntax_check.js")
    io.open(tmp, "w", encoding="utf-8", newline="\n").write(blocks[-1])
    try:
        r = subprocess.run(["node", "--check", tmp], capture_output=True, text=True,
                           encoding="utf-8", errors="replace")
    finally:
        os.remove(tmp)
    if r.returncode == 0:
        print("  %-34s JS syntax OK (%d bytes)" % (os.path.basename(path), len(blocks[-1])))
        return True
    print("  %-34s JS SYNTAX ERROR" % os.path.basename(path))
    print("    " + (r.stderr or "").strip().replace("\n", "\n    "))
    return False


def main():
    if not shutil.which("node"):
        print("没装 node，跳过 JS 语法自检")
        return 0
    os.makedirs(TMP_DIR, exist_ok=True)
    targets = sys.argv[1:] or sorted(f for f in os.listdir(HERE) if f.endswith(".html"))
    bad = 0
    for t in targets:
        p = t if os.path.isabs(t) else os.path.join(HERE, t)
        if not check(p):
            bad += 1
    print("结果：%d 个页面，%d 个有问题" % (len(targets), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

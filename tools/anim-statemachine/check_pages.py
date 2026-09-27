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

# 控制台可能是 GBK（Windows 默认）—— 本文件会打中文与 ✗，不重设编码会**当场崩**（实测过）
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
TMP_DIR = os.path.join(HERE, "out")     # 临时 js 丢这儿（out/ 是忽略目录，别在源码目录留垃圾）


def check_script_markers(path, blocks):
    """🔴 脚本块里不许出现字面 `<!--` / `-->`（CLAUDE.md 铁律 37，2026-09-27）。

    为什么：HTML 规范里 `<script>` 内出现 `<!--` 会切进"脚本转义"状态 ——
    浏览器与 node --check 都照规范处理（**页面照跑**），但 **VSCode 的 HTML 语言服务会解析错位**，
    在脚本尾部报一片 `Argument expression expected` 假错误（用户截图来问过一轮）。
    嵌入的 JSON 里 `<` `>` 要写成 `\\u003C` / `\\u003E`；源码里的字符串/正则拼出来；注释里也别写。
    """
    hits = []
    for i, b in enumerate(blocks):
        if "<!--" in b or "-->" in b:
            for n, line in enumerate(b.split("\n"), 1):
                if "<!--" in line or "-->" in line:
                    hits.append("块%d 第%d行: %s" % (i + 1, n, line.strip()[:70]))
    if hits:
        print("  %-34s ✗ 脚本块里有字面 <!-- / -->（会让 VSCode 解析错位）" % os.path.basename(path))
        for h in hits[:6]:
            print("      " + h)
        return False
    print("  %-34s 脚本块无 HTML 注释标记 OK" % os.path.basename(path))
    return True


def check(path):
    html = io.open(path, encoding="utf-8").read()
    blocks = re.findall(r"<script>([\s\S]*?)</script>", html)
    if not blocks:
        print("  %-34s （没有内联脚本，跳过）" % os.path.basename(path))
        return True
    ok = check_script_markers(path, blocks)
    tmp = os.path.join(TMP_DIR, "_syntax_check.js")
    io.open(tmp, "w", encoding="utf-8", newline="\n").write(blocks[-1])
    try:
        r = subprocess.run(["node", "--check", tmp], capture_output=True, text=True,
                           encoding="utf-8", errors="replace")
    finally:
        os.remove(tmp)
    if r.returncode == 0:
        print("  %-34s JS syntax OK (%d bytes)" % (os.path.basename(path), len(blocks[-1])))
        return ok          # 🔴 语法 OK ≠ 页面合格：脚下还有"脚本块不许有 HTML 注释标记"那条
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

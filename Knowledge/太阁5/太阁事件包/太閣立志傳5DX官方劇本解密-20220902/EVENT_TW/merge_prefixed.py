# -*- coding: utf-8 -*-
"""
TK5 EVENT_TW 源剧本合并（带文件名前缀版）
=========================================
将 EVENT_TW 下全部 E*.evm.decompiled.txt 合并成单一文件，事件号全局唯一。

为什么要带前缀：
    源数据按剧本分文件，每个文件内事件号独立（EFF0C300_159 和 EFF06E00_159 是两个不同事件）。
    旧合并（TK5AllEvents.txt JSON）按事件号覆盖合并，同号撞车导致后写覆盖先写——今川线整套事件
    曾因此静默丢失（2026-08-25 桶狭间教训，见 plans/scenario-campaign-mode/08-转化管线.md）。

前缀规则（2026-08-25 用户裁定）：
    编号 = 文件名_事件ID，如 EFF0C300_159 = EFF0C300 剧本文件内事件 159。
    块标记：事件:事件EFF0C300_159{ ... }//事件EFF0C300_159
    文件内事件完成标志引用（条件/更新里的 事件::N）同步加本文件前缀：
        调查:(事件::159)==(1)  →  调查:(事件::EFF0C300_159)==(1)
        （原语义 = "本剧本内事件 159 已完成"；跨文件合并后必须带前缀才不歧义）
    人物/据点/大名等对象引用（人物::/據點::/大名家::...）是全局对象，不加前缀。

用法：
    python merge_prefixed.py <EVENT_TW目录> <输出文件>
"""
import os
import sys
import glob
import re

# 事件块开始标记：事件:事件699{
RE_BLOCK_START = re.compile(r"事件:事件(\d+)\{")
# 事件块结束标记：}//事件699
RE_BLOCK_END = re.compile(r"\}//事件(\d+)")
# 文件内事件完成标志引用：调查:(事件::159)==(1) / 更新:(事件::158,已發生)
RE_EVENT_FLAG = re.compile(r"事件::(\d+)")


def merge_prefixed(directory, output):
    files = sorted(glob.glob(os.path.join(directory, "E*.evm.decompiled.txt")))
    if not files:
        print("错误：目录内没有找到 E*.evm.decompiled.txt 文件")
        return

    total = 0
    with open(output, "w", encoding="utf-8") as out:
        out.write("# TK5 EVENT_TW 全量合并（带文件名前缀，事件号全局唯一）\n")
        out.write("# 源目录: " + os.path.abspath(directory) + "\n")
        out.write("# 编号规则: 文件名_事件ID（如 EFF0C300_159 = EFF0C300 剧本文件内事件 159）\n")
        out.write("# 文件内事件标志引用（事件::N）已按本文件前缀归一（事件::EFF0C300_159）\n")
        for path in files:
            fname = os.path.splitext(os.path.basename(path))[0]  # EFF0C300（去掉 .evm.decompiled.txt）
            fname = fname.replace(".evm.decompiled", "")
            with open(path, encoding="utf-8") as f:
                content = f.read()

            n_blocks = len(RE_BLOCK_START.findall(content))
            content = RE_BLOCK_START.sub(r"事件:事件%s_\1{" % fname, content)
            content = RE_BLOCK_END.sub(r"}//事件%s_\1" % fname, content)
            n_flags = len(RE_EVENT_FLAG.findall(content))
            content = RE_EVENT_FLAG.sub(r"事件::%s_\1" % fname, content)

            total += n_blocks
            out.write("\n")
            out.write("# ===== 源文件: %s =====\n" % os.path.basename(path))
            out.write(content)
            out.write("\n")
            print("已合并: %-20s 事件 %3d 个, 事件标志引用 %3d 处" % (fname, n_blocks, n_flags))

    print("合并完成！共 %d 个文件, %d 个事件 → %s" % (len(files), total, output))


if __name__ == "__main__":
    directory = sys.argv[1] if len(sys.argv) > 1 else "."
    output = sys.argv[2] if len(sys.argv) > 2 else "TK5AllEvents_merged.txt"
    merge_prefixed(directory, output)

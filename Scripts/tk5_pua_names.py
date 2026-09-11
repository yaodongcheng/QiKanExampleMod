#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""太阁5 DX 日志「私用区字符」还原表（人名可读化）
============================================================================
问题（2026-09-11 用户实测发现）
------------------------------
tkhack 导出的 `E:\\TKHACK\\log\\DX角色ID.log` 里，**37 条人名的某个字写成 Unicode
私用区码点**（U+E40B–U+E43A 共 19 个）。终端/编辑器画不出私用区字形 → 看着像掉字，
例如 `Log: 1189|<E41D>千代` 实际是「誾千代」。

成因：这些字全是**日本 JIS 标准码表外的汉字**（惣/畠/宍/梶/榊/塚/辻/籾/垪/楯/樫…），
太阁5 靠自绘字形槽渲染；**DX 中文版**把它们映射进了 Unicode 私用区。原版日志与
BUSTUP 目录都是正常字，0 条私用区——所以本表只对 DX 日志有意义。

映射怎么来的（不是猜的）
------------------------
对每个私用区码点，在「BUSTUP 目录名 + persons.csv 名字 + TaikouHero 名字」三处合并的
正常字名词库里，找**同长度、除该位外逐字全同**的候选 → 唯一命中的即正字。
17 个码点唯一命中；剩 2 个（U+E413、U+E427）词库无对应，标 UNRESOLVED，遇到时原样保留。

🔴 用法：解析 DX 日志（或任何太阁5 数据）取人名前，先过 `restore()`。
   按名字做对齐/比对时不过这道 = 这 37 条必然对不上（我 2026-09-11 第一轮对齐就栽在这）。
"""
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# 私用区码点 → 正字（17 个已确认 + 2 个待补）
PUA_TO_CHAR = {
    0xE40B: "惣",   # 池田惣左卫门 / 伊藤惣十郎
    0xE40C: "梶",   # 梶原景宗 / 梶原政景
    0xE40D: "畠",   # 北畠具教 / 畠山高政
    0xE40E: "笹",   # 笹部勘二郎
    0xE40F: "宍",   # 宍戸隆家 / 宍戸梅轩
    0xE410: "辻",   # 芝辻清右卫门
    0xE411: "籾",   # 籾井教业
    0xE412: "垪",   # 垪和氏续
    0xE417: "樫",   # 白樫
    0xE41A: "祐",   # 赤松义祐 / 伊东祐兵
    0xE41B: "稙",   # 伊达稙宗
    0xE41D: "誾",   # 誾千代
    0xE420: "塙",   # 塙直政 / 塙团右卫门
    0xE42B: "篠",   # 篠原长房
    0xE431: "楯",   # 楯冈满茂
    0xE438: "榊",   # 榊原康政
    0xE43A: "塚",   # 塚原卜传 / 平塚为广
    # —— 地名场景补录（2026-09-11，T4-b 四表体检时在 TaikouHero 的 City_<年> 列发现）——
    # 原表只从**人名**词库推定，这几个码点只在**据点名**里出现，故当时漏了。
    # 证据链两份互相印证：①TaikouHero 的人名/据点名（E426=高槻城，用它的正是 和田惟政/
    # 高山友照/高山重友）②各剧本据点名日志（岩<E426>城 对应我们表里已写对的「岩槻城」、
    # 长<E42B>城 对应「长篠城」）。
    0xE40A: "堺",   # <U+E40A>之町 —— 堺（和泉，交易町；据点表曾误写「界」）
    0xE415: "驒",   # 飞<U+E415>高山城 / 飞<U+E415>高山之町 —— 飞驒高山（飞驒国）
    0xE416: "橡",   # <U+E416>尾城 —— 橡尾城（岩代）
    0xE426: "槻",   # 岩<U+E426>城 / 高<U+E426>城 —— 岩槻城（武藏）/ 高槻城（摄津）
    # —— 列传场景补录（2026-09-11，修 TaikouHero 全表私用区时发现）——
    # 依据：同一批列传里「之嫡子」26 次、「之子」183 次、「之次子」21 次，**「の」0 次**；
    #       三好长庆原文「細川家臣。元長<U+F724>嫡子。」→ 与 26 条同构 → 之。
    0xF724: "之",   # 元長<U+F724>嫡子 —— 「元長之嫡子」（三好长庆）
}

# 词库不足、暂未推定的两个（遇到时原样保留，并在报告里点名）
UNRESOLVED = {
    0xE413: "DX日志 id=1033 「?兵卫」（候选：権兵衛/弥兵衛 等，词库无一命中）",
    0xE427: "DX日志 id=1075 「?犬」",
}

LOG_DIR = r"E:\TKHACK\log"
OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "_analysis", "decoded")
LINE_RE = re.compile(r"Log: (\d+)\|(.*)$")


def is_pua(ch):
    return 0xE000 <= ord(ch) <= 0xF8FF


def restore(name):
    """把名字里的私用区码点换成正字；未定码点原样保留。"""
    out = []
    for ch in name or "":
        if is_pua(ch):
            out.append(PUA_TO_CHAR.get(ord(ch), ch))
        else:
            out.append(ch)
    return "".join(out)


def restore_marks(name):
    """还原 + 标出未定码点（给人看的诊断串）。"""
    return "".join(f"<?{ord(c):04X}>" if is_pua(c) and ord(c) not in PUA_TO_CHAR
                   else (PUA_TO_CHAR.get(ord(c), c) if is_pua(c) else c)
                   for c in (name or ""))


def load_log(path):
    d = {}
    for raw in io.open(path, encoding="utf-8", errors="replace"):
        m = LINE_RE.search(raw.rstrip("\r\n"))
        if m:
            d[int(m.group(1))] = m.group(2).rstrip()
    return d


def main():
    print(f"私用区还原表：已确认 {len(PUA_TO_CHAR)} 个，待补 {len(UNRESOLVED)} 个")
    for cp, ex in sorted(UNRESOLVED.items()):
        print(f"  U+{cp:04X} 待补：{ex}")

    # 自检：把表里的字塞进私用区再还原，必须一一还原
    bad = [cp for cp, ch in PUA_TO_CHAR.items() if restore(chr(cp)) != ch]
    if bad:
        print(f"[FATAL] 自检失败：{bad}")
        return 1
    print("往返自检 ✓")

    if not os.path.isdir(LOG_DIR):
        print(f"[WARN] 找不到日志目录 {LOG_DIR}，跳过还原导出")
        return 0

    os.makedirs(OUT_DIR, exist_ok=True)
    total = 0
    for tag, fn in (("dx", "DX角色ID.log"), ("orig", "原版角色ID.log")):
        p = os.path.join(LOG_DIR, fn)
        if not os.path.isfile(p):
            print(f"[WARN] 缺 {p}")
            continue
        tbl = load_log(p)
        n_pua = sum(1 for v in tbl.values() if any(is_pua(c) for c in v))
        total += n_pua
        dest = os.path.join(OUT_DIR, f"person_ids_{tag}.csv")
        with io.open(dest, "w", encoding="utf-8-sig", newline="") as fh:
            fh.write("id,name_raw,name_restored,had_pua\n")
            for i in sorted(tbl):
                raw = tbl[i]
                fh.write(f'{i},"{raw}","{restore_marks(raw)}",'
                         f'{"1" if any(is_pua(c) for c in raw) else "0"}\n')
        print(f"  {fn}: {len(tbl)} 槽，含私用区 {n_pua} 条 → {os.path.relpath(dest)}")
    print(f"合计还原 {total} 条")
    return 0


if __name__ == "__main__":
    sys.exit(main())

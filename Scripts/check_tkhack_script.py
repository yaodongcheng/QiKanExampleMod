#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""tkhack 脚本静态自检（太阁立志传5 事件源文件 .txt）
============================================================================
**为什么有这条检查**（2026-09-12）：
tkhack 脚本里属性名 / 容器名 / 变量名拼错时**不会加载报错**，而是静默取到
「无效对象」——插值后日志里打出固定假名（人物槽 → 「门卫」，家族槽 → 「一宫家」，
2026-09-11 实测）。人眼校对几百行脚本不现实，所以把权威源拉进来做机器核对。

**五道检查**（全部对着 E:\TKHACK\database\database_dx.xml，逐字比对，非猜测）：
1. 花括号配对（剥掉 // 注释后计数；注释里的 {…} 示例不参与）
2. `X::变量.属性` 的类型前缀与属性名必须同时存在于对应属性表
   （例：城::城Ａ.兵士數 → database 的「城屬性」表里必须有「兵士數」）
3. `容器設定:(X,...)` 的容器名必须是 database 里的对象类型
4. 对象变量名（城Ａ/人物Ａ/文字列Ａ/势力Ａ…）必须在 database 的「變量」/「文字列」表里
5. `[[ ]]` 插值里的类型前缀不能跟对象变量（只能跟数值变量/番号）

🔴 **首次运行即抓出真 bug（2026-09-12）**：砦屬性 表**没有**「兵士數」——
砦的兵是船，字段是 所有船舶數13 / 大型船舶數14 / 鐵甲船數15；而里屬性 表**有**兵士數。
四张据点属性表（城/町/里/砦）字段各不相同，写脚本时最易照抄串表，本检查专防这个。

🔴 **逐字比对，不做异体归一（2026-09-12 修正）**：`衆`(U+8846) 与 `眾`(U+773E) 是两个字，
引擎直查 database 不归一——写成「忍者衆」实机报 `Unknown container class '忍者衆'`。
本脚本曾读 keywords.xml 做「衆→眾」归一，那是**过度归一**：会把实机必挂的写法放行。
已移除，别再加回来。

用法:
    python Scripts/check_tkhack_script.py                    # 扫 Knowledge/太阁5/太阁5脚本/*.txt
    python Scripts/check_tkhack_script.py <文件.txt> [...]    # 只检查指定文件
退出码: 0 = 全通过；1 = 有硬错误
"""

import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE = Path(__file__).resolve().parent.parent
DEFAULT_DIR = BASE / "Knowledge" / "太阁5" / "太阁5脚本"

DB_DIR = Path(r"E:\TKHACK\database")
DB_XML = DB_DIR / "database_dx.xml"

# 全角字母 + 汉字，用于变量名/属性名匹配
HAN = r"\u4e00-\u9fff"
FW = r"\u4e00-\u9fffＡ-Ｚａ-ｚ"


def strip_comments(text: str) -> str:
    """剥掉行内 // 注释，但 [[ ]] 内的 // 不剥（可能是台词里的斜杠）。"""
    out = []
    for line in text.splitlines():
        depth = 0
        cut = len(line)
        i = 0
        while i < len(line) - 1:
            two = line[i:i + 2]
            if two == "[[":
                depth += 1
                i += 2
                continue
            if two == "]]":
                depth = max(0, depth - 1)
                i += 2
                continue
            if two == "//" and depth == 0:
                cut = i
                break
            i += 1
        out.append(line[:cut])
    return "\n".join(out)


def load_db():
    """返回 (属性表dict, 全部对象类型名set, 变量名set)"""
    if not DB_XML.exists():
        print(f"[错误] 找不到 {DB_XML}（tkhack 安装路径变了？改本脚本 DB_DIR）")
        sys.exit(2)
    db = DB_XML.read_text(encoding="utf-8", errors="replace")

    tables = {}
    for m in re.finditer(r'<Data type="([^"]+)"[^>]*>(.*?)</Data>', db, re.S):
        tables.setdefault(m.group(1), set()).update(
            re.findall(r'<Value name="([^"]+)"', m.group(2)))

    # 对象类型名 = 所有 <Data type="X"> 的表名（城/町/里/砦/人物/大名家…）
    types = set(tables.keys())

    # 变量名 = 變量 各 subType 表 + 文字列 表
    variables = set()
    for m in re.finditer(r'<Data type="變量"[^>]*>(.*?)</Data>', db, re.S):
        variables |= set(re.findall(r'<Value name="([^"]+)"', m.group(1)))
    if "文字列" in tables:
        variables |= tables["文字列"]

    return tables, types, variables


def read_script(path: Path) -> str:
    """按编码读脚本。tkhack 生态里三种编码都有，猜错会读成乱码 → 正则全不命中 → 假绿：
      · UTF-8 无 BOM —— 本项目自有的导出脚本（`Knowledge/太阁5/太阁5脚本/`）
      · UTF-16LE（FF FE）—— `resources/腳本/測試用例/` 大部分文件
      · GBK 无 BOM —— 部分官方/社区剧本（如 `劇本/測試事件/編譯測試/容器設定.txt`，实测 2026-09-12）
    """
    d = path.read_bytes()
    if d[:2] == b"\xff\xfe":
        return d.decode("utf-16", errors="replace")
    if d[:3] == b"\xef\xbb\xbf":
        return d.decode("utf-8-sig", errors="replace")
    try:
        return d.decode("utf-8")
    except UnicodeDecodeError:
        return d.decode("gbk", errors="replace")


def check_file(path: Path, tables, types, variables):
    raw = read_script(path)
    body = strip_comments(raw)
    errs, warns = [], []

    # 1) 花括号配对
    lb, rb = body.count("{"), body.count("}")
    if lb != rb:
        errs.append(f"花括号不配对：{{ ×{lb} vs }} ×{rb}（剥注释后）")

    # 2) 类型前缀 + 变量 + 属性
    refs = set(re.findall(rf'([{HAN}]+)::[{FW}]+\.([{HAN}]+)', body))
    for tname, attr in sorted(refs):
        tbl = tname + "屬性"
        if tname not in types and tbl not in tables:
            errs.append(f"未知类型前缀「{tname}::」（database 里没有 {tname} / {tbl} 表）")
        elif tbl in tables:
            if attr not in tables[tbl]:
                cand = [x for x in ("城屬性", "町屬性", "里屬性", "砦屬性", "據點屬性")
                        if attr in tables.get(x, ())]
                hint = ("该属性属于 " + "/".join(cand)) if cand else "database 里查无此属性名"
                errs.append(f"{tname}::{attr} —— {tbl} 表里没有这个属性（{hint}）")

    # 3) 容器名（容器設定 / 容器篩選 / 容器排序 的第一个参数；排除 無效 等占位）
    #    ⚠️ 必须逐字比对：`衆`(U+8846) 与 `眾`(U+773E) 是两个不同的字，引擎直查 database 不归一，
    #    写成「忍者衆」实机报 'Unknown container class'（2026-09-12 实测）。
    #    早先版本读 keywords.xml 做过「衆→眾」归一，那是**过度归一**——会把这类必挂的写法放行，
    #    已移除。别再加回来。
    for verb in ("容器設定", "容器篩選", "容器排序"):
        for cname in set(re.findall(rf'{verb}:\(([{HAN}]+)', body)):
            if cname in ("無效", "有效"):
                continue
            if cname not in types:
                errs.append(f"{verb} 的容器名「{cname}」不在 database 对象类型表里"
                            f"（注意 衆/眾 是两个字，必须逐字照 database 写）")

    # 4) 变量名（「代入X:(…)」取 代入 与 : 之间的 X；「容器選擇:(X,…)」取第一个参数；
    #    容器清理 的第一参数是清理种类（消去/保留），容器添加/排除 的第一个是容器名，都不是变量）
    used = set(re.findall(rf'代入([{FW}]+):\(', body))
    used |= set(re.findall(rf'容器選擇:\(([{FW}]+),', body))
    for v in sorted(used):
        if v in variables:
            continue
        warns.append(f"变量「{v}」不在 database 變量/文字列 表里（可能是扩展变量或笔误）")

    # 5) 🔴 插值里的类型前缀只能跟「番号」（数值变量 ａ~ｚ / 字面番号），不能跟对象变量
    #    实测（2026-09-12 实机）：日誌:[[…<城::城Ａ.規模>…]] 报
    #    「Text variable token '城::城Ａ.規模' invalid, '城Ａ.規模' is not '城'」
    #    —— 它在找"名叫 城Ａ.規模 的城"。正解：先 代入ａ:(城::城Ａ.規模)，再插 <ａ>。
    for m in re.finditer(r'\[\[(.*?)\]\]', body, re.S):
        for tname, var, attr in re.findall(rf'([{HAN}]+)::([{FW}]+)\.([{HAN}]+)', m.group(1)):
            if re.search(r'[Ａ-Ｚ]', var):   # 含全角大写字母 = 对象变量（城Ａ/人物Ａ/文字列Ａ…）
                errs.append(
                    f"插值里「{tname}::{var}.{attr}」不合法：类型前缀后只能跟数值变量（番号），"
                    f"不能跟对象变量 → 改成先「代入ａ:({tname}::{var}.{attr})」再插 <ａ>")

    return errs, warns, len(refs)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    files = [Path(a) for a in args] if args else sorted(DEFAULT_DIR.glob("*.txt"))

    tables, types, variables = load_db()
    print(f"权威源: {DB_XML}（{len(tables)} 张表 / {len(variables)} 个变量名）")
    print(f"待检文件: {len(files)} 个\n")

    total_err = 0
    for f in files:
        if not f.exists():
            print(f"✗ {f} —— 文件不存在")
            total_err += 1
            continue
        errs, warns, nrefs = check_file(f, tables, types, variables)
        status = "✓" if not errs else "✗"
        print(f"{status} {f.name}（属性引用 {nrefs} 条）")
        for e in errs:
            print(f"    硬错误: {e}")
        for w in warns:
            print(f"    提示:   {w}")
        total_err += len(errs)

    print()
    if total_err:
        print(f"结论: 发现 {total_err} 处硬错误 —— 属性/容器名对不上会静默打出垃圾值，必须改。")
        return 1
    print("结论: 全部通过。")
    return 0


if __name__ == "__main__":
    sys.exit(main())

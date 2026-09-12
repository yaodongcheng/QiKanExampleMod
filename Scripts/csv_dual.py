#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""CSV 双行表头读写（项目统一约定，2026-09-12 用户裁定）
============================================================================
**约定**：`csv/` 下的**数据表**一律两行表头——

    第 1 行 = 中文标签（给人看的）
    第 2 行 = 英文键（**给机器看的：脚本一律按第 2 行取键**）
    第 3 行起 = 数据

为什么这样切：中文标签随内容改（`追剥`→`强盗`、`短名`→并入`别名`），**代码的键不能跟着动**——
所以把「人读的那行」和「机器读的那行」分开，改标签不碰代码。

**用法**（读）：

    from csv_dual import read_table, dict_rows
    cn, en, rows = read_table(path)      # cn/en = 两行表头；rows = 数据行（list[list[str]]）
    for r in dict_rows(path):            # → dict（键 = 第 2 行英文键）
        print(r["Owner_1554"])

**用法**（写）：

    from csv_dual import write_table
    write_table(path, CN_COLS, EN_COLS, rows)   # 保留原文件换行风格

🔴 **不做自动判据**（2026-09-12 踩坑）：曾按「第 2 行全非空 + 两行无相同单元格」自动识别双行表头——
实测**在「首行数据恰好全非空」的表上误判**（`Culture.csv` 首行 `saikai,西海,九州`、`Settlements.csv` 首行
`town_tk000,城,…` 都被当成英文表头 ✗）。所以改成**显式**：`dual=True`（默认，合规范）/
`dual=False`（单行表头的旧文件或上游转储——**读之前必须先声明**，别猜）。
"""
import csv
import io

__all__ = ["read_table", "dict_rows", "write_table", "read_header"]


def _raw(path):
    return io.open(path, "rb").read()


def eol_of(raw):
    """原文件的换行风格（本仓文件有 LF 也有 CRLF，写回时保持一致）。"""
    return "\r\n" if b"\r\n" in raw else "\n"


def read_table(path, head=2):
    """→ (中文标签列表, 英文键列表, 数据行列表)。

    🔴 `head` = **用哪一行当键**，不做自动判据（见文件头）：
      · `head=2`（默认）= 两行表头，键取**第 2 行（英文）**——合规范的读法
      · `head=1` = 两行表头，键取**第 1 行（中文）**——`TaikouForce` 的旧读者还在用（待迁移）
      · `head=0` = **单行表头**（上游转储 / 还没转双行的表，如 `TaikouHero.csv`）
    """
    raw = _raw(path)
    rows = list(csv.reader(io.StringIO(raw.decode("utf-8-sig", errors="replace"), newline="")))
    rows = [r for r in rows if r and any((c or "").strip() for c in r)]
    if not rows:
        return [], [], []
    hdr1 = [(c or "").strip() for c in rows[0]]
    if head == 0:
        return hdr1, hdr1, rows[1:]
    hdr2 = [(c or "").strip() for c in rows[1]] if len(rows) > 1 else hdr1
    if head == 1:                      # 双行表头但键取中文行：数据仍从第 3 行起
        return hdr1, hdr1, rows[2:]
    return hdr1, hdr2, rows[2:]


def read_header(path, head=2):
    """只要表头 → (中文标签, 英文键)。"""
    cn, en, _ = read_table(path, head=head)
    return cn, en


def dict_rows(path, head=2):
    """→ [dict]（键 = 按 `head` 选的那行；缺格的补空串）。"""
    _, en, rows = read_table(path, head=head)
    out = []
    for r in rows:
        d = {k: (r[i] if i < len(r) else "") for i, k in enumerate(en)}
        out.append(d)
    return out


def write_table(path, cn_cols, en_cols, rows):
    """写双行表头 + 数据（保留原文件换行风格与 BOM）。"""
    raw = b""
    try:
        raw = _raw(path)
    except OSError:
        pass
    eol = eol_of(raw) if raw else "\n"
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(cn_cols)
    w.writerow(en_cols)
    for r in rows:
        w.writerow(r)
    io.open(path, "wb").write(buf.getvalue().replace("\r\n", eol).encode("utf-8-sig"))

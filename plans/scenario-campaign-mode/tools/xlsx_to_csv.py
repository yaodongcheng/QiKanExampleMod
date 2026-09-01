# -*- coding: utf-8 -*-
"""
🔴 已归档（2026-09-01 用户拍板）——**本工具不再使用，源 xlsx 为历史数据，非必要不去查**。
   目录已迁至 Knowledge/太阁5/骑砍2织丰角色ID对应/；CSV 工作层独立生效。
   保留本文仅为存档参照，勿为新任务调用。
"""
"""
xlsx_to_csv.py —— 《骑砍2太阁Mod表.xlsx》→ 一张 Sheet 一个 CSV（管线改版：数据表走 git 管理）

为什么
------
2026-08-28 用户裁定：织丰表数据要上传 git 管理后，xlsx（3.2MB 二进制）不可逐行 diff、
不可合冲突、改数据只能开 Excel。拆成「一张 Sheet 一个 CSV」：
逐行文本 diff、可审可合、Excel 照样能打开编辑（UTF-8 带 BOM，中文不乱码）。

纪律（三条，写计划评审时按此为准）
----------------------------------
1. **xlsx 是上游导入源，不再作为编辑面**；CSV = 我们这边的工作数据层。
   上游织丰表更新（新版本 xlsx）→ 重跑本脚本刷新镜像。
2. 🔴 **CSV 是上游镜像（生成物）：人填的数据禁止直接写进镜像 CSV**——重跑即丢。
   人填数据（StringId 补列/地方名对照/别名修正等）写 `gen_entity_maps.py` 的映射表
   （NAME_ALIAS / TK5_ONLY_HERO / SAME_AS_NEAR 范式）或独立小文件。
3. xlsx 的 zip 手扒解析（openpyxl 解析不了这张表的样式段）**只留在本脚本**；
   `gen_entity_maps.py` 此后读 CSV，不再依赖脆弱解析。

用法
----
    python tools/xlsx_to_csv.py      # 转换 + 自检（逐行重读对比）+ 打印清单
"""
from __future__ import unicode_literals
import io
import os
import re
import sys
import csv
import zipfile
import xml.etree.ElementTree as ET

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))          # LivingWorldNpcs
XLSX = os.path.join(ROOT, 'Knowledge', '骑砍2织丰角色ID对应', '骑砍2太阁Mod表.xlsx')
OUT_DIR = os.path.join(ROOT, 'Knowledge', '骑砍2织丰角色ID对应', 'csv')


# ---------------------------------------------------------------------------
# xlsx 手扒解析（与旧版 gen_entity_maps.read_sheets 同源；openpyxl 3.1 解析不了
# 这张表的样式段 `Fill() takes no arguments`，所以自拆 sharedStrings + sheet XML）
# ---------------------------------------------------------------------------
def parse_sheet_xml(path, z, shared):
    """一个 sheet 的 XML → 原始行列表（未过滤，含空行/类型行；单元格已按列对齐补位）。"""
    NS = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'

    def col_of(ref):
        i = 0
        for ch in ref:
            if not ch.isalpha():
                break
            i = i * 26 + (ord(ch.upper()) - 64)
        return i - 1

    rows = []
    for row in ET.fromstring(z.read(path)).iter(NS + 'row'):
        cells = []
        for c in row.iter(NS + 'c'):
            idx = col_of(c.get('r') or '')
            if c.get('t') == 'inlineStr':
                v = ''.join(t.text or '' for t in c.iter(NS + 't'))
            else:
                vn = c.find(NS + 'v')
                v = '' if vn is None or vn.text is None else vn.text
                if c.get('t') == 's' and v.isdigit():
                    v = shared[int(v)] if int(v) < len(shared) else ''
            while len(cells) < idx:
                cells.append('')
            cells.append(v.strip())
        rows.append(cells)
    return rows


def read_sheets():
    """{sheet名: (表头, 数据行)}——跳过空行与类型/注释行（织丰表格式，不是数据）。"""
    z = zipfile.ZipFile(XLSX)
    shared = []
    if 'xl/sharedStrings.xml' in z.namelist():
        for si in ET.fromstring(z.read('xl/sharedStrings.xml')):
            shared.append(''.join(t.text or '' for t in si.iter(
                '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}t')))

    rels = {}
    for r in ET.fromstring(z.read('xl/_rels/workbook.xml.rels')):
        rels[r.get('Id')] = r.get('Target')
    order = []
    for s in ET.fromstring(z.read('xl/workbook.xml')).iter(
            '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}sheet'):
        tgt = (rels.get(s.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id'))
               or rels.get(s.get('{http://schemas.openxmlformats.org/package/2006/relationships}id'))
               or '')
        order.append((s.get('name'), 'xl/' + tgt.lstrip('/').replace('xl/', '', 1)))

    out = {}
    for name, path in order:
        if path not in z.namelist():
            continue
        rows = parse_sheet_xml(path, z, shared)
        if not rows:
            continue
        head = rows[0]
        body = [r for r in rows[1:] if any(r)]
        # 第 2~3 行 = 类型行 / 中文注释行（织丰表格式），不是数据
        while body and body[0] and body[0][0] in ('string', 'int', '编号', '骑砍ID', '内置番号'):
            body.pop(0)
        out[name] = (head, body)
    z.close()
    return out


# ---------------------------------------------------------------------------
# 转换 + 自检
# ---------------------------------------------------------------------------
def safe_name(name):
    """sheet 名 → 文件名（防御性替换路径非法字符；正常都是英文名）。"""
    return re.sub(r'[\\/:*?"<>|]', '_', name)


def main():
    if not os.path.exists(XLSX):
        print('找不到织丰表：%s' % XLSX)
        return 1
    sheets = read_sheets()
    print('xlsx 解析完成：%d 张 sheet' % len(sheets))

    os.makedirs(OUT_DIR, exist_ok=True)
    written = []
    for name, (head, body) in sheets.items():
        w = max(len(head), max((len(r) for r in body), default=0))
        rows = [head + [''] * (w - len(head))] + \
               [r + [''] * (w - len(r)) for r in body]
        path = os.path.join(OUT_DIR, safe_name(name) + '.csv')
        with io.open(path, 'w', encoding='utf-8-sig', newline='') as f:
            wr = csv.writer(f, lineterminator='\r\n')
            wr.writerows(rows)
        written.append((name, path, len(body), len(head)))

    # ---- 自检：逐行重读 CSV，与内存解析结果（同宽度补位后）逐行等价 ----
    def readback_rows(name):
        path = os.path.join(OUT_DIR, safe_name(name) + '.csv')
        with io.open(path, encoding='utf-8-sig', newline='') as f:
            rs = list(csv.reader(f))
        return rs[0], [r for r in rs[1:] if any(r)]

    bad = 0
    for name, (head, body) in sheets.items():
        w = max(len(head), max((len(r) for r in body), default=0))
        expect = [head + [''] * (w - len(head))] + \
                 [r + [''] * (w - len(r)) for r in body]
        got = readback_rows(name)
        if list(got[0]) != expect[0] or got[1] != expect[1:]:
            bad += 1
            print('  ⚠️ %s 重读对比不一致' % name)
    if bad:
        print('自检失败：%d 张 sheet 不一致' % bad)
        return 1
    print('自检通过：15/15（逐行重读对比一致）' if len(written) == 15 else
          '自检通过：%d/%d' % (len(sheets), len(written)))

    print('')
    print('已写入 %s' % os.path.relpath(OUT_DIR, ROOT))
    for name, path, n_rows, n_cols in written:
        print('  %-12s %5d 行 × %3d 列' % (name, n_rows, n_cols))
    print('')
    print('🔴 纪律：CSV 是上游镜像（生成物）。人填数据写 gen_entity_maps.py 的映射表，')
    print('   禁止改镜像 CSV（重跑本脚本即覆盖）。上游 xlsx 更新 → 重跑本脚本。')
    return 0


if __name__ == '__main__':
    sys.exit(main())

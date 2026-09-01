# -*- coding: utf-8 -*-
"""一次性迁移：gen_entity_maps.py 的 NAME_ALIAS（29 条）→ TaikouHero.csv 新增 Alias 列。

规则（用户裁定，铁律 24/25）：
- 方向 = 太阁写法(左) → 织丰 CNName(右)；写入"右"行的 Alias 列（追加左值，| 分隔，去重）。
- 值内禁止半角逗号与 |（脚本校验失败即报错停止）。
- 先备份 TaikouHero.csv（.bak_20260831）。
"""
import csv, sys, os, ast, shutil

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
CSV = r'h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\太阁5\骑砍2织丰角色ID对应\csv\TaikouHero.csv'
GEN = r'h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\plans\scenario-campaign-mode\tools\gen_entity_maps.py'

# 1. 从 gen_entity_maps.py 提取 NAME_ALIAS
src = open(GEN, encoding='utf-8').read()
i = src.find('NAME_ALIAS = {')
j = src.find('\n}\n', i)
block = src[i:i + j - i + 2]
ns = {}
exec(block, {'__builtins__': {}}, ns)
ALIAS = ns['NAME_ALIAS']
print('NAME_ALIAS 条数:', len(ALIAS))

# 2. 读 TaikouHero.csv
rows = list(csv.reader(open(CSV, encoding='utf-8-sig')))
hdr = rows[0]
assert 'CNName' in hdr and 'ID' in hdr, '表头不符: %s' % hdr[:6]
ci_cn = hdr.index('CNName')
ci_id = hdr.index('ID')
assert 'Alias' not in hdr, 'Alias 列已存在，脚本停止（避免重复迁移）'
new_hdr = hdr + ['Alias']
new_rows = [new_hdr]

# 3. 行索引：CNName → 行
by_cn = {}
for r in rows[1:]:
    if r and r[ci_cn]:
        by_cn.setdefault(r[ci_cn], r)
missing = []
consume = {}
for left, right in ALIAS.items():
    row = by_cn.get(right)
    if row is None:
        missing.append((left, right))
        continue
    consume.setdefault(id(row), []).append(left)

for r in rows[1:]:
    alias_vals = consume.get(id(r), [])
    if alias_vals:
        # 去重（按已有? Alias 新列，直接写）
        uniq = []
        for v in alias_vals:
            if v not in uniq:
                uniq.append(v)
        # 铁律 24 校验
        for v in uniq:
            if ',' in v or '|' in v:
                raise SystemExit('非法值含 , 或 |: %r' % v)
        new_rows.append(r + ['|'.join(uniq)])
    else:
        new_rows.append(r + [''])

# 4. 备份 + 写回
shutil.copyfile(CSV, CSV + '.bak_20260831')
with open(CSV, 'w', newline='', encoding='utf-8-sig') as f:
    csv.writer(f).writerows(new_rows)
print('写入完成。总行: %d (原 %d)。' % (len(new_rows), len(rows)))
print('未找到织丰行的别名条目 (%d):' % len(missing))
for left, right in missing:
    print('  %r -> %r' % (left, right))

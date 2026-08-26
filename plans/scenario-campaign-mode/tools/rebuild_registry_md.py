# -*- coding: utf-8 -*-
"""重构 16：第一部分 = CSV 大表渲染（唯一事实源 16-DSL翻译总表.csv）+ 保留机制章节"""
import csv
import re

CSV = 'plans/scenario-campaign-mode/16-DSL翻译总表.csv'
MD = 'plans/scenario-campaign-mode/16-DSL注册表全表.md'

with open(CSV, encoding='utf-8-sig') as f:
    rows = list(csv.DictReader(f))

def render_table(items):
    lines = ['| 太阁原词 | 频率 | 我们侧名 | 类型 | 语义 | 参数 | 实现用法 | 状态 |',
             '|---|---|---|---|---|---|---|---|']
    for r in items:
        lines.append('| %s | %s | %s | %s | %s | %s | %s | %s |' % (
            r['太阁原词'], r['频率'], r['我们侧名'], r['类型'],
            r['语义'].replace('|', '\\|'), r['参数'], r['实现用法'], r['状态']))
    return '\n'.join(lines)

sec = {}
for r in rows:
    sec.setdefault(r['类别'], []).append(r)

part1 = []
part1.append('# 第一部分：太阁5 ↔ 骑砍2 翻译总表（唯一事实源 = `16-DSL翻译总表.csv`，2026-08-26 重构）\n')
part1.append('> 🔴 **本部分 = 唯一大翻译表**（策划配置式）：第一列太阁原词 → 我们侧名 → 类型/语义/参数/实现用法/状态，一行一个词条。'
             '**CSV 是事实源**（`16-DSL翻译总表.csv`，424 行），下表为 markdown 渲染（`tools/build_registry_csv.py` 生成）；'
             '01 validator 的注册表检查（域/属性/谓词/动作）读同一份 CSV——改表 = 改 CSV + 重跑生成，三处不再漂移。'
             '统计口径：程序化复跑 `TK5AllEvents_merged.txt`（250576 行），复跑命令见文末。\n')
part1.append('## 1.0 操作符统计（唯一数据源 = 原附录，2026-08-26 随合并迁入）\n')
part1.append('''| 操作符 | 次数 | 说明 |
|---|---|---|
| `==` | 22657 | 相等（最常用：身份/归属/状态判断） |
| `!=` | 4233 | 不等 |
| `>=` | 2633 | 数值门槛（年份/日数/数值属性） |
| `<` / `>` / `<=` | 686 / 431 / 410 | 数值比较 |
| 存在性（无操作符） | 9525 | `exists(大名家::X)` 式：势力/人物/组织是否存在 |\n''')
part1.append('## 1.1 域（42 个）\n')
part1.append(render_table(sec['域']))
part1.append('\n## 1.2 属性（199 个）\n')
part1.append(render_table(sec['属性']))
part1.append('\n## 1.3 命令（174 种）\n')
part1.append(render_table(sec['命令']))
part1.append('\n## 1.4 谓词（9 个）\n')
part1.append(render_table(sec['谓词']))
part1.append('\n## 1.5 特殊形态速查\n')
part1.append('''- **代入槽机制**：代入* 命令合计 ~15000 次（人物Ａ/B/C/D/E、大名家Ａ-D、城Ａ-E、據點Ａ-C、海賊衆/忍者衆、ａ-ｅ/ｔ 数字槽）→ Ctx（事件内局部）/ Variable / GlobalSlot（跨事件需存档）三档，详见第二部分 2.1
- **容器机制**：容器篩選/選擇/設定/排除/清理/排序（~15000 次合计）→ 首版静态引用 + 映射表，`pick` 谓词后续扩展
- **變名對話**：1824 次（含台词文本）→ 演绎剧本变名节点（05 演出系统，动作表现 + 台词）\n''')

new_part1 = '\n'.join(part1)

t = open(MD, encoding='utf-8').read()

# 1) 替换第一部分（从 "# 第一部分" 到 "# 第二部分" 之前，含 1.0-1.4）
t, n = re.subn(r'# 第一部分：太阁5 侧全量清单.*?(?=# 第二部分)', new_part1 + '\n\n', t, flags=re.DOTALL)
assert n == 1, '第一部分替换失败 n=%d' % n

# 2) 删除 一域表 + 二属性白名单（## 一、 到 ## 三、 前）
t, n = re.subn(r'## 一、域表（完整）.*?(?=## 三、🔴 代入槽机制)', '## 二、🔴 代入槽机制（Ctx 上下文变量，太阁"人物Ａ/主人公/發生據點"的映射）\n', t, flags=re.DOTALL)
assert n == 1, '一/二节删除失败 n=%d' % n

# 3) 删除 四谓词表 + 五动作表（## 四、 到 ## 六、 前）
t, n = re.subn(r'## 四、谓词表（完整，关系判断——与属性区分：属性是单值、谓词是关系）.*?(?=## 六、触发时机注册表)', '', t, flags=re.DOTALL)
assert n == 1, '四/五节删除失败 n=%d' % n

# 4) 编号重整：六→三、七→四（机制章节）；"## 六、触发时机注册表" 保留标题文字但编号改
t = t.replace('## 六、触发时机注册表（trigger / once / priority，2026-08-26 新增）',
              '## 三、触发时机注册表（trigger / once / priority，2026-08-26 新增）')
t = t.replace('## 七、覆盖结论', '## 四、覆盖结论')
# 机制章节定位说明（第二部分标题下）
t = t.replace('# 第二部分：逐项骑砍2 对应（映射状态全表）',
              '# 第二部分：机制定义（总表之外的机制权威）\n'
              '> 🔴 2026-08-26 重构：原「一域表/二属性白名单/四谓词表/五动作表/七映射总表」已并入**第一部分总表**（类型/语义/参数/实现/状态列在 CSV 大表）；'
              '本部分只保留**机制性定义**（总表容纳不了的：Ctx 三档生命周期、trigger/facility 注册表、覆盖结论）。'
              '谓词参数见第一部分 1.4，动作参数见 1.3 命令行的参数/实现用法列。')

open(MD, 'w', encoding='utf-8').write(t)
print('16 重构完成')

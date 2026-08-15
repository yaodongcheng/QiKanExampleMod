# -*- coding: utf-8 -*-
import io

p = 'GUI/Prefabs/ImChatCompact.xml'
t = io.open(p, encoding='utf-8').read()

# ============ 1) 标题行 → 频道切换三件套 + 放大/关闭（无 Title 文字），56 高 ============
old_title_start = t.index('                <!-- ═══ 标题行（2026-08-15 用户裁定恢复）')
old_title_end = t.index('                <!-- ═══ 行 A：未决锚点卡')

new_title = open('Scripts/_tmp_title.xml', encoding='utf-8').read()
t = t[:old_title_start] + new_title + t[old_title_end:]
io.open(p, 'w', encoding='utf-8').write(t)
print('title row replaced')

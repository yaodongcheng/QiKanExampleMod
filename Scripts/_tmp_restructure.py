# -*- coding: utf-8 -*-
import io

p = 'GUI/Prefabs/ImChatCompact.xml'
t = io.open(p, encoding='utf-8').read()

start = t.index('                <!-- ═══ 顶部行：原版 Options 频道下拉')
end = t.index('\n\n            <!-- \U0001f534 2026-08-15（用户裁定）：拖动已移除')

new_block = io.open('Scripts/_tmp_input_row.xml', encoding='utf-8').read()

t = t[:start] + new_block + t[end:]
io.open(p, 'w', encoding='utf-8').write(t)
print('restructured OK, new length', len(t))

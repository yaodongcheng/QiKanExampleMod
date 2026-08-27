# -*- coding: utf-8 -*-
"""patch7：官位/官職 从「对象实体」改判「枚举」，堵万能接收器（16.md §四 裁定对齐）。

问题：gen_registry_tables.ENTITY_DOMAINS 把 官位/官職 当具名实体域 —— 实体值按纪律
不进 CSV（查名字表 + fallback_id 兜底），于是：
  ① 语料 10 个官位 / 18 个官職（306 次）一条都没进 16a 翻译总表；
  ② val_side('官位', 任意瞎编词) 永远返回哈希侧名 = **万能接收器**，生成期自检对这两域失效。
而 16.md §四「值类型体系」明确裁定：**官職/官位不是对象——属性 title/court_rank 的值 = 枚举 token**。

修：① ENTITY_DOMAINS 删 官位/官職 ② DOMAIN_VAL_MAP 逐值登记（罗马字 token，与 身份 daimyo/ronin 同风格）
    ③ 属性 官位 与 官職 撞同一侧名 Hero.title → 官位 改 Hero.court_rank
序（品级链）不进 token —— 16.md 裁定：带序枚举的等级表由 17 系统产出，禁止按 token/行序推断。
"""
import ast
import io
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'

BLOCK = """    # 官位域值（🔴 2026-08-27：16.md §四 裁定「官位不是对象，值 = 枚举 token」——
    #   原走 ENTITY_DOMAINS 哈希兜底 = 万能接收器，自检失效；改逐值登记。
    #   品级序（正一位 > 從一位 > 正二位 …）= 17 产出的等级表，不编进 token）
    ('官位', '正一位'): 'shoichii', ('官位', '從一位'): 'juichii',
    ('官位', '正二位'): 'shonii', ('官位', '從二位'): 'junii',
    ('官位', '正三位'): 'shosanmi', ('官位', '從三位'): 'jusanmi',
    ('官位', '正四位上'): 'shoshii_jo', ('官位', '正四位下'): 'shoshii_ge',
    ('官位', '從四位上'): 'jushii_jo', ('官位', '從四位下'): 'jushii_ge',
    ('官位', '正五位上'): 'shogoi_jo', ('官位', '正五位下'): 'shogoi_ge',
    ('官位', '從五位上'): 'jugoi_jo', ('官位', '從五位下'): 'jugoi_ge',
    ('官位', '正六位上'): 'shorokui_jo', ('官位', '正六位下'): 'shorokui_ge',
    # 官職域值（同上裁定；称号无序，17 官职表权威）
    ('官職', '征夷大將軍'): 'seii_taishogun', ('官職', '左大臣'): 'sadaijin',
    ('官職', '右大臣'): 'udaijin', ('官職', '內大臣'): 'naidaijin',
    ('官職', '大納言'): 'dainagon', ('官職', '中納言'): 'chunagon',
    ('官職', '權中納言'): 'gon_chunagon', ('官職', '左近衛大將'): 'sakonoe_taisho',
    ('官職', '左近衛中將'): 'sakonoe_chujo', ('官職', '右近衛中將'): 'ukonoe_chujo',
    ('官職', '左衛門佐'): 'saemon_no_suke', ('官職', '刑部少輔'): 'gyobu_shoyu',
    ('官職', '治部少輔'): 'jibu_shoyu', ('官職', '修理亮'): 'shuri_no_suke',
    ('官職', '右京大夫'): 'ukyo_daibu', ('官職', '筑前守'): 'chikuzen_no_kami',
    ('官職', '日向守'): 'hyuga_no_kami',
"""


def main():
    src = io.open(P, encoding='utf-8').read()
    if "('官位', '正一位')" in src:
        print('已打过补丁，跳过')
        return

    old_ent = "    '官位': 'court_rank', '官職': 'title', '工作': 'QuestDef', '事件主命': 'QuestDef',"
    new_ent = "    '工作': 'QuestDef', '事件主命': 'QuestDef',   # 🔴 官位/官職 已移出（16.md §四：不是对象，是枚举）"
    assert old_ent in src, '锚点缺失：ENTITY_DOMAINS'
    src = src.replace(old_ent, new_ent, 1)

    anchor = "    # 真偽域值（布尔字面量）\n"
    assert anchor in src, '锚点缺失：DOMAIN_VAL_MAP 真偽段'
    src = src.replace(anchor, BLOCK + anchor, 1)

    old_attr = "'体力': 'Hero.health', '主命目標': 'Hero.quest_goal', '官位': 'Hero.title',"
    new_attr = "'体力': 'Hero.health', '主命目標': 'Hero.quest_goal', '官位': 'Hero.court_rank',"
    assert old_attr in src, '锚点缺失：属性 官位'
    src = src.replace(old_attr, new_attr, 1)

    old_doc = "'官位': 'Hero.title（17）',"
    assert old_doc in src, '锚点缺失：域说明 官位'
    src = src.replace(old_doc, "'官位': 'Hero.court_rank（17 官位品级链）',", 1)

    ast.parse(src)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ patch7 已应用（官位 16 值 + 官職 17 值 登记；属性 官位 → Hero.court_rank）')


if __name__ == '__main__':
    main()

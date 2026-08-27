# -*- coding: utf-8 -*-
"""patch10：补注册（觸發 / 場所 / 模板NPC / 軍團槽）+ 孪生词表统一 + 跨表一致性自检。

三件事：
① 补注册 —— 觸發 22、場所 53（設施38∪場面14∪背景19∪決鬥場地9 的并集）、模板NPC 13、
   軍團槽 8 全部给出可读 token，hash 侧名（Trigger::tk5_uc82b44）清零。
   🔴 16-DSL注册表全表.md §二 已声明的 14 trigger + 13 facility token **原样采用**（那是权威）。
② 孪生词表统一 —— 同一套词被写在多张表里、侧名各不相同（身份 那个病的同族）：
     場面(域) / 設施(资源集) / 背景(资源集) / 決鬥場地(枚举)  → 共用 PLACE_TOKENS
     軍團(域) / 軍團槽(枚举)                                 → 共用 ARMY_SLOTS
   各表只保留「哪些词属于我」的成员清单，侧名一律从单一词表取（前缀各自加）。
③ 跨表一致性自检 —— 见 patch11（verify_coverage 新增孪生分叉断言）。
"""
import ast
import io
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'

BLOCK = '''
# ═══════════════════════════════════════════════════════════════════════════
# 🔴 场所词汇单一来源（2026-08-27 补注册）：設施 / 背景 / 場面 / 決鬥場地 四张表
#   说的是同一套「地方」词——之前四张表各写各的（場面::城主間=lord_room、
#   設施::城主間=hash、決鬥場地::城主間=lord_room），跟 身份 双表分叉同一个病。
#   现在只此一张表，四处按各自前缀取用（Facility:: / Background:: / 裸 slug）。
#   ✅16 = 16-DSL注册表全表.md §二 facility 注册表 v1 已声明，原样采用。
# ═══════════════════════════════════════════════════════════════════════════
PLACE_TOKENS = {
    # ── 16.md §二 已声明（权威，勿改）──
    '自宅': 'house',                   # ✅16
    '酒場': 'tavern',                  # ✅16
    '城主間': 'castle_hall',           # ✅16（原 場面/決鬥場地 写的 lord_room，以 16.md 为准）
    '評定間': 'council_room',          # ✅16（原 場面 写的 council）
    '座': 'za',                        # ✅16
    '主人公診療所': 'clinic',           # ✅16
    '主人公道場': 'dojo',               # ✅16
    '民家': 'house_min',               # ✅16
    '商家': 'shop',                    # ✅16
    '南蠻商館': 'nanban_trade',         # ✅16
    '主人公鍛冶屋': 'smithy',           # ✅16
    '主人公茶室': 'tea_room',           # ✅16
    '寺': 'temple',                    # ✅16
    # ── 16.md「其余（≤4 次，无场景一律降级 menu_dialogue）」——本次补注册 token ──
    '主人公評定': 'council_own',        # 主人公自己主持的评定（区别于 評定間 场所）
    '公家宅': 'kuge_house',             # 公家 = 朝廷贵族
    '南蠻寺': 'nanban_temple',          # 南蛮寺 = 天主教堂
    '城練兵場': 'castle_drill',
    '宿屋': 'inn',
    '御所': 'imperial_palace',          # 御所 = 天皇居所
    '忍屋敷': 'ninja_manor',
    '忍者宅': 'ninja_house',
    '據點內畫面': 'settlement_screen',  # 不是设施，是「据点主界面」占位
    '武家宅': 'samurai_house',
    '海外交易所': 'overseas_trade',
    '海賊宅': 'pirate_den',
    '海賊屋敷': 'pirate_manor',
    '砦修業場': 'fort_training',        # 砦 = 要塞
    '砦練兵場': 'fort_drill',
    '米屋': 'rice_shop',
    '職人宅': 'artisan_house',
    '茶人宅': 'tea_master_house',
    '造船所': 'shipyard',
    '道場': 'dojo_town',               # 城里的公用道场（主人公道場 = dojo，两者不同）
    '醫師宅': 'doctor_house',
    '里修業場': 'village_training',
    '里練兵場': 'village_drill',
    '鍛冶屋': 'smithy_town',           # 城里的公用铁匠铺（主人公鍛冶屋 = smithy）
    '馬屋': 'stable',
    # ── 場面 域独有：占位符，指「本事件的发生设施」──
    '發生設施': 'event_facility',
    # ── 背景独有（场地背景图，不是可进入设施）──
    '初期設定': 'initial_setup',
    '合戰畫面': 'battle_screen',
    '海道': 'sea_route',
    '賭博所': 'gambling_den',
    '路口': 'crossroad',
    '陸道': 'land_route',
    '黑暗': 'darkness',
    # ── 決鬥場地独有（庭院/野外）──
    '原野': 'field',
    '忍者宅庭院': 'ninja_yard',
    '武家宅庭院': 'samurai_yard',
    '民家庭院': 'house_yard',
    '沙灘': 'beach',
    '船的甲板': 'ship_deck',
}

# 🔴 触发时机 token（16-DSL注册表全表.md §二 trigger 注册表 v1 = 权威，✅16 标记原样采用）
TRIGGER_TOKENS = {
    '每日處理的開頭': 'daily',              # ✅16
    '每月處理的最後': 'monthly',            # ✅16
    '遊戲開始時': 'game_start',             # ✅16
    '據點畫面表示後': 'settlement_enter',   # ✅16
    '室內畫面表示後': 'house_enter',        # ✅16（第二参 = facility，见 PLACE_TOKENS）
    '評定開始時': 'council_start',          # ✅16
    '移動畫面表示後': 'travel_screen',      # ✅16
    '野戰開始時': 'field_battle_start',     # ✅16
    '野戰結束時': 'field_battle_end',       # ✅16
    '攻城戰開始時': 'siege_battle_start',   # ✅16
    '攻城戰結束時': 'siege_battle_end',     # ✅16
    '軍團移動結束時': 'army_move_end',      # ✅16
    '章節凍結時': 'chapter_freeze',         # ✅16
    '遊戲通關時': 'game_clear',             # ✅16
    # ── 语料里有、16.md v1 未声明——本次补注册（16.md §二 表需同步补行）──
    '人物對話時': 'npc_talk',               # 77 次，最大的未声明契機
    '合戰決定時': 'battle_decided',         # 合战（会战）判定打不打之后
    '大名家滅亡時': 'clan_destroyed',
    '每月處理的最後絕對': 'monthly_forced',  # 「絕對」= 不参与互斥选路，必执行
    '選擇移動畫面時': 'travel_screen_select',   # 点开移动画面的瞬间（travel_screen 是画面显示完）
    '選擇設施時': 'facility_select',        # 点设施图标的瞬间（house_enter 是进去之后）
    '軍團移動開始時': 'army_move_start',
    '遊戲結束時': 'game_over',              # 败亡结束（game_clear 是通关）
}

# 🔴 模板 NPC token（05 演出的无名角色模板；07 素材表落 CharacterObject）
NPC_TOKENS = {
    '凄腕用心棒': 'elite_bodyguard', '喝醉的女人': 'drunk_woman', '喝醉的男人': 'drunk_man',
    '奇怪的姑娘': 'strange_girl', '女の子': 'young_girl', '婆さん': 'old_woman',
    '小孩': 'child', '明國商人': 'ming_merchant', '槍術師範代': 'spear_instructor',
    '琉球商人': 'ryukyu_merchant', '米屋的老闆': 'rice_shop_owner', '賊': 'bandit',
    '頭目': 'boss',
}

# 🔴 军团槽单一来源：軍團(域值) 与 軍團槽(枚举) 是同一套槽位，之前两张表各写各的
#   （主人公軍團 = main_army / player 两个侧名，事件用１軍團 = event_army_1 / event_1）
ARMY_SLOTS = {
    '主人公軍團': 'Army::player',
    '軍團１': 'Army::army_1', '軍團２': 'Army::army_2',
    '事件用１軍團': 'Army::event_1', '事件用２軍團': 'Army::event_2',
    '事件用３軍團': 'Army::event_3', '事件用４軍團': 'Army::event_4',
    '事件用５軍團': 'Army::event_5',
}

'''

OLD_SCENE = """    ('場面', '自宅'): 'Facility::home', ('場面', '發生設施'): 'Facility::event_facility', ('場面', '海賊宅'): 'Facility::pirate_den',
    ('場面', '評定間'): 'Facility::council', ('場面', '城主間'): 'Facility::lord_room',
"""
NEW_SCENE = "    # 🔴 場面 域值不在此手写——见 PLACE_TOKENS（场所词汇单一来源），domain_val_rule('場面') 取用\n"

OLD_ARMY = """    ('軍團', '軍團１'): 'Army::army_1', ('軍團', '軍團２'): 'Army::army_2',
    ('軍團', '主人公軍團'): 'Army::main_army', ('軍團', '事件用１軍團'): 'Army::event_army_1',
"""
NEW_ARMY = "    # 🔴 軍團 域值不在此手写——见 ARMY_SLOTS（军团槽单一来源），domain_val_rule('軍團') 取用\n"

OLD_COND = "    ('狀況', '戰爭禁止日數'): 'Variable::war_ban_days', ('狀況', '空閒大名家數'): 'Variable::idle_clans', ('狀況', '場面'): 'Variable::scene',"
NEW_COND = (
    "    ('狀況', '戰爭禁止日數'): 'Variable::war_ban_days', ('狀況', '空閒大名家數'): 'Variable::idle_clans', ('狀況', '場面'): 'Variable::scene',\n"
    "    ('狀況', '天氣'): 'Variable::weather', ('狀況', '遊戲經過日數'): 'Variable::days_elapsed',\n"
    "    ('狀況', '評定期限結束標誌'): 'Variable::assessment_deadline_flag',\n"
    "    ('變量', '容器記錄數'): 'Variable::container_count', ('變量', '參考值'): 'Variable::ref_value',\n"
    "    ('軍團方針', '歸還'): 'intent_return_home',   # 与 軍團指令::歸還=return_home 同义，加 intent_ 前缀区分层")

OLD_R1 = """    if dom == '場面':
        return f'Facility::{fallback_id(val)}'         # 05 演出设施（专表外）
    if dom == '軍團':
        return f'Army::{fallback_id(val)}'             # 命名军团实例（专表外）"""
NEW_R1 = """    if dom == '場面':
        # 🔴 场所词汇单一来源：設施/背景/場面/決鬥場地 同一张 PLACE_TOKENS
        return f'Facility::{PLACE_TOKENS[val]}' if val in PLACE_TOKENS else f'Facility::{fallback_id(val)}'
    if dom == '軍團':
        return ARMY_SLOTS.get(val) or f'Army::{fallback_id(val)}'   # 军团槽单一来源"""

OLD_RES = '''def res_side(setname, tok):
    """资源型枚举侧名：Bgm::tk5_uXXXXXX（08b 踩坑 5：确定性 ID + report 登记中文名）。"""
    p = RES_PREFIX[setname]
    return '%s::%s' % (p, ascii_translit(tok) or fallback_id(tok))'''
NEW_RES = '''RES_TOKEN_TABLE = {           # 资源集 → 单一词表（表内有 = 用可读 token；BGM/SE/CG 无表，走转写/hash）
    '設施': PLACE_TOKENS, '背景': PLACE_TOKENS, '觸發': TRIGGER_TOKENS, '模板NPC': NPC_TOKENS,
}


def res_side(setname, tok):
    """资源型枚举侧名。有单一词表的查表（設施/背景/觸發/模板NPC），
    其余（ＢＧＭ/ＳＥ/事件ＣＧ = 纯媒体资产名，07 素材表落文件）走全角转写 + hash 兜底。"""
    p = RES_PREFIX[setname]
    tbl = RES_TOKEN_TABLE.get(setname)
    if tbl is not None and tok in tbl:
        return '%s::%s' % (p, tbl[tok])
    return '%s::%s' % (p, ascii_translit(tok) or fallback_id(tok))'''

OLD_DUEL = """    '決鬥場地': {'原野': 'field', '城主間': 'lord_room', '民家庭院': 'house_yard',
                '武家宅庭院': 'samurai_yard', '道場': 'dojo', '酒場': 'tavern',
                '忍者宅庭院': 'ninja_yard', '沙灘': 'beach', '船的甲板': 'ship_deck'},"""
NEW_DUEL = """    # 🔴 決鬥場地 不在此手写——下方从 PLACE_TOKENS 派生（场所词汇单一来源）
    '決鬥場地': {},"""

OLD_SLOT = """    '軍團槽': {'主人公軍團': 'Army::player', '軍團１': 'Army::army_1', '軍團２': 'Army::army_2',
              '事件用１軍團': 'Army::event_1', '事件用２軍團': 'Army::event_2', '事件用３軍團': 'Army::event_3',
              '事件用４軍團': 'Army::event_4', '事件用５軍團': 'Army::event_5'},"""
NEW_SLOT = """    # 🔴 軍團槽 不在此手写——下方从 ARMY_SLOTS 派生（军团槽单一来源）
    '軍團槽': {},"""

OLD_DERIVE = """    assert ENUM_SETS[_n], '派生枚举集为空：%s（DOMAIN_VAL_MAP 里没有该域的值）' % _n"""
NEW_DERIVE = """    assert ENUM_SETS[_n], '派生枚举集为空：%s（DOMAIN_VAL_MAP 里没有该域的值）' % _n

# 決鬥場地 = PLACE_TOKENS 的子集（决斗能打的那几个场所）；軍團槽 = ARMY_SLOTS 全量
DUEL_PLACES = ('原野', '城主間', '民家庭院', '武家宅庭院', '道場', '酒場',
               '忍者宅庭院', '沙灘', '船的甲板')
ENUM_SETS['決鬥場地'] = {_p: PLACE_TOKENS[_p] for _p in DUEL_PLACES}
ENUM_SETS['軍團槽'] = dict(ARMY_SLOTS)"""


def main():
    src = io.open(P, encoding='utf-8').read()
    if 'PLACE_TOKENS' in src:
        print('已打过补丁，跳过')
        return

    anchor = 'DOMAIN_VAL_MAP = {'
    assert anchor in src, '锚点缺失：DOMAIN_VAL_MAP'
    src = src.replace(anchor, BLOCK.lstrip('\n') + anchor, 1)

    for name, old, new in (('場面域值', OLD_SCENE, NEW_SCENE), ('軍團域值', OLD_ARMY, NEW_ARMY),
                           ('狀況域值', OLD_COND, NEW_COND), ('domain_val_rule', OLD_R1, NEW_R1),
                           ('res_side', OLD_RES, NEW_RES), ('決鬥場地', OLD_DUEL, NEW_DUEL),
                           ('軍團槽', OLD_SLOT, NEW_SLOT), ('派生块', OLD_DERIVE, NEW_DERIVE)):
        assert old in src, '锚点缺失：%s' % name
        src = src.replace(old, new, 1)

    # RES_SETS['模板NPC'] 剔除 '其他'（它是 主人公分歧 的 else 分支，不是 NPC 模板）
    assert "'其他', '凄腕用心棒'" in src, '锚点缺失：模板NPC 其他'
    src = src.replace("'其他', '凄腕用心棒'", "'凄腕用心棒'", 1)

    ast.parse(src)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ patch10 已应用（PLACE_TOKENS 53 + TRIGGER_TOKENS 22 + NPC_TOKENS 13 + ARMY_SLOTS 8，四表统一）')


if __name__ == '__main__':
    main()

# -*- coding: utf-8 -*-
"""把「命令参数位登记」段插入 gen_registry_tables.py（铁律 22：改生成器不改产物）。"""
import io
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'
ANCHOR = '# ═══ 生成期自检：全语料覆盖断言（表外 = 生成失败）═══'

BLOCK = '''# ═══════════════════════════════════════════════════════════════════════════
# 🔴 v3（2026-08-27）：命令参数位登记 —— 补上「域::X 形态」之外的最大盲区
#   旧自检只查 域/属性/域值/函数/命令头 五种形态，命令**裸参数位**（枚举/资源名/
#   容器字段/军团槽/触发名）完全没有模型 → 5.4 万处词条游离在总表之外。
#   现在：每个命令的每个参数位声明「收什么」，枚举集逐 token 登记，表外即生成失败。
# ═══════════════════════════════════════════════════════════════════════════

# ── 枚举集：token → 侧名（语义型手写；资源型走 res_side 规则生成）──
ENUM_SETS = {
    # 容器操作
    '容器位置': {'先頭': 'first', '指針': 'cursor', '末尾': 'last'},
    '容器清理': {'消去': 'clear', '保留': 'keep'},
    '容器存取': {'保存': 'save', '恢復': 'restore'},
    '排序方向': {'降順': 'desc', '升順': 'asc'},
    '排序特殊鍵': {'亂序': 'random'},
    '容器統計': {'容器記錄數': 'container.count'},
    # 军团（02 PartyBrain）
    '軍團槽': {'主人公軍團': 'Army::player', '軍團１': 'Army::army_1', '軍團２': 'Army::army_2',
              '事件用１軍團': 'Army::event_1', '事件用２軍團': 'Army::event_2', '事件用３軍團': 'Army::event_3',
              '事件用４軍團': 'Army::event_4', '事件用５軍團': 'Army::event_5'},
    '軍團指令': {'據點移動': 'move_to', '軍團攻擊': 'attack_party', '據點攻擊': 'siege',
                '歸還': 'return_home', '終結': 'disband', '平局': 'draw'},
    '零值': {'Ｚｅｒｏ': '0'},
    # 演出（05）
    '轉場': {'褪去': 'fade', '無效果': 'none', '圓形擦出': 'circle_wipe', '回旋擦出': 'spiral_wipe'},
    '畫面效果': {'暗出': 'fade_out', '暗入': 'fade_in'},
    '圖片類型': {'事件ＣＧ': 'cg', '物品': 'item', '人物': 'portrait'},
    '背景類型': {'場面': 'scene', '場地背景': 'field', '據點': 'settlement'},
    # 身份 / 立场 / 状态
    '從屬類型': {'直臣': 'direct_vassal', '陪臣': 'sub_vassal'},
    '獨立方式': {'只有陪臣': 'sub_only', '通常': 'normal'},
    '出現狀態': {'已出現': 'appeared', '未出現': 'hidden'},
    '真偽': {'真': 'true', '偽': 'false'},
    '其他分支': {'其他': 'else'},
    '身份': {'大名': 'daimyo', '城主': 'castle_lord', '國主': 'province_lord', '家老': 'elder',
            '侍大將': 'samurai_general', '足輕大將': 'ashigaru_general', '足輕組頭': 'ashigaru_captain',
            '頭領': 'chief', '頭': 'head', '上忍': 'jonin', '元締': 'boss', '支配人': 'manager',
            '大老闆': 'big_merchant', '船大將': 'fleet_captain', '町人': 'townsman', '浪人': 'ronin',
            '師範代': 'instructor', '最高位': 'top_rank', '事件人物': 'event_hero'},
    '人物類別': {'泛用對手': 'generic_rival'},
    '物品種類': {'武器': 'weapon', '書物': 'book', '兵法書': 'strategy_book', '茶器': 'tea_ware',
                '藝術品': 'art', '南蠻物': 'exotic', '酒': 'sake', '財寶': 'treasure', '海外': 'overseas',
                '小粒金': 'gold_nugget'},
    '武器種類': {'刀劍': 'sword', '鎖鎌': 'kusarigama', '弓': 'bow', '槍': 'spear',
                '苦無': 'kunai', '火繩槍': 'matchlock'},
    '性別': {'男': 'male', '女': 'female'},
    '生存狀態': {'生存': 'alive', '被殺': 'killed'},
    # 个人战斗 / 迷你游戏
    '逃跑許可': {'不可逃跑': 'no_flee', '可逃跑': 'can_flee'},
    '護衛': {'無護衛': 'no_guard', '有護衛': 'guarded'},
    '決鬥場地': {'原野': 'field', '城主間': 'lord_room', '民家庭院': 'house_yard',
                '武家宅庭院': 'samurai_yard', '道場': 'dojo', '酒場': 'tavern',
                '忍者宅庭院': 'ninja_yard', '沙灘': 'beach', '船的甲板': 'ship_deck'},
    '迷你遊戲': {'調製藥物': 'medicine', '鐵砲打靶': 'gun_range', '建設灌溉水路': 'irrigation',
                '閃躲飛矢': 'dodge_arrows', '組合木材': 'carpentry', '二十一計': 'twentyone',
                '排列茶器': 'tea_arrange', '組合九張畫': 'picture_puzzle', '破壞方陣': 'break_formation',
                '算術填空': 'arithmetic', '弓箭射鵠': 'archery', '尋找黃金礦脈': 'gold_vein',
                '搜索人物': 'search_person', '破壞工作': 'sabotage'},
    '難度': {'初學': 'novice', '進階': 'adept', '高手': 'master'},
    # 事件文件头
    '事件屬性': {'一次': 'once', '多次': 'repeat', '弱': 'weak'},
    '主命字段': {'事件主命成果': 'quest.result', '事件主命對象': 'quest.target', '事件主命目標': 'quest.goal'},
    '主命目標類': {'主命目標銷路': 'quest.market', '主命目標商業圈': 'quest.trade_zone', '主命目標海域': 'quest.sea'},
    # 状态写入右值（更新:(左)(右) 的右值）
    '狀態值': {
        '已發生': 'done', '未發生': 'not_done', '成立': 'established', '不成立': 'not_established',
        '已認識': 'known', '不認識': 'unknown', '已出現': 'appeared', '未出現': 'hidden',
        '死亡': 'dead', '死刑': 'executed', '健康': 'healthy', '生病': 'sick',
        '在家': 'at_home', '離家': 'away', '出撃中': 'sortied', '真': 'true', '偽': 'false',
        '可能': 'available', '沒持有': 'not_owned', '持有中': 'owned', '已鑑定': 'appraised',
        '量産可能': 'mass_producible',
        # 外交 / 感情
        '盟友': 'ally', '同盟': 'alliance', '無同盟': 'no_alliance', '絶交': 'broken_off',
        '從屬': 'subordinate', '支配': 'dominated', '友好': 'friendly', '普通': 'neutral',
        '良好': 'good', '圓滿': 'excellent', '險惡': 'strained', '敵視': 'hostile',
        '沒那個意思': 'no_interest',
        # 战略目标
        '統一天下': 'unify_realm', '國內統一': 'unify_province', '分國統一': 'unify_domain',
        '地方統一': 'unify_region', '大名攻略': 'conquer_clan', '敵城攻略': 'take_castle',
        '領土守備': 'defend_territory', '國內守備': 'defend_province', '里守備': 'defend_village',
        '領土發展': 'develop_territory', '大名支援': 'support_clan', '砦增強': 'fortify',
        '上洛': 'march_kyoto',
        # 关系经纬
        '原上司': 'ex_superior', '原同事': 'ex_peer', '原屬下': 'ex_subordinate',
        '背叛了': 'betrayed', '主人公背叛': 'betrayed_by_player', '此武將背叛': 'betrayed_by_general',
        # 性格 / 其他
        '重視情義': 'values_loyalty', '活潑好動': 'lively', '小家碧玉': 'demure', '平常': 'normal',
        '全職種': 'all_classes', '只限武將': 'generals_only', '其他': 'else',
        '沒有主命': 'no_quest', '藤原氏': 'fujiwara',
    },
}

# ── 资源型枚举集：token 是数据包资源名（BGM/SE/CG/背景/设施/模板 NPC），
#    数量大且无语义映射价值 → 侧名走确定性规则（前缀 + ascii 转写/hash），逐 token 进 CSV 登记 ──
RES_PREFIX = {
    'ＢＧＭ': 'Bgm', 'ＳＥ': 'Se', '事件ＣＧ': 'Cg',
    '設施': 'Facility', '背景': 'Background', '模板NPC': 'Npc', '觸發': 'Trigger',
}


def res_side(setname, tok):
    """资源型枚举侧名：Bgm::tk5_uXXXXXX（08b 踩坑 5：确定性 ID + report 登记中文名）。"""
    p = RES_PREFIX[setname]
    return '%s::%s' % (p, ascii_translit(tok) or fallback_id(tok))


def enum_side(setname, tok):
    """枚举 token → 侧名。语义集查表（表外 = None → 生成期报错）；资源集走规则。"""
    if setname in RES_PREFIX:
        return res_side(setname, tok)
    return ENUM_SETS.get(setname, {}).get(tok)


'''


def main():
    src = io.open(P, encoding='utf-8').read()
    if 'ENUM_SETS = {' in src:
        print('已打过补丁，跳过')
        return
    assert ANCHOR in src, '锚点缺失'
    io.open(P, 'w', encoding='utf-8').write(src.replace(ANCHOR, BLOCK + ANCHOR, 1))
    print('✅ 插入枚举集段 %d 行' % len(BLOCK.splitlines()))


if __name__ == '__main__':
    main()

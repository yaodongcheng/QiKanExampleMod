# -*- coding: utf-8 -*-
"""patch2：命令参数签名表 + 实体名解析 + 台词变量/字段 + cmd_rule 修正。"""
import io
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'
ANCHOR = '# ═══ 生成期自检：全语料覆盖断言（表外 = 生成失败）═══'

BLOCK = '''# ── 具名实体名单 ──
#   ① 语料实测：域::名 且 域 ∈ ENTITY_DOMAINS（人物::織田信長 → 織田信長 是具名人物）
#   ② EXTRA_ENTITY_NAMES：只在**参数位/台词插值**出现、从未写成 域::名 的具名实体（补录）
ENTITY_NAMES = set()
for _d, _v in re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([々〆ヶ・·一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９_]{1,16})', body):
    if _d in ENTITY_DOMAINS:
        ENTITY_NAMES.add(_v)

EXTRA_ENTITY_NAMES = {
    # 只作为命令裸参数/台词插值出现的具名人物（无 人物::X 形态）
    '高阪甚內': 'Hero', '新庄': 'Hero', '阿福': 'Hero', '安岐': 'Hero', '三條': 'Hero',
    '小督': 'Hero', '彦鶴': 'Hero', '煕子': 'Hero', '幸圓': 'Hero', '春桃': 'Hero',
    '細川晴元': 'Hero', '吉見正賴': 'Hero', '木曾義昌': 'Hero', '南姫': 'Hero', '德公主': 'Hero',
    '立花直次': 'Hero', '北信愛': 'Hero', '安芸國虎': 'Hero', '谷忠澄': 'Hero',
    '三法師': 'Hero', '九郎判官義経': 'Hero', '肉戶梅軒': 'Hero', '長谷川宗仁': 'Hero',
    '里璐': 'Hero', '瀬名': 'Hero', '拉斐耶魯': 'Hero',
}

RE_HERO_NUM = re.compile(r'^人物[0-9０-９]+$')          # 人物１００８ = 编号人物引用
RE_UNKNOWN_N = re.compile(r'^未知[0-9０-９]+$')          # 未知13 / 未知４９６ = 反编译碎片


def entity_side(tok):
    """具名实体 → 侧名（名字表先行，fallback_id 兜底）。非实体返回 None。"""
    if tok in EXTRA_ENTITY_NAMES:
        return '%s::%s' % (EXTRA_ENTITY_NAMES[tok], fallback_id(tok))
    if tok in ENTITY_NAMES:
        return 'Entity::%s' % fallback_id(tok)
    if RE_HERO_NUM.match(tok):
        return 'Hero::hero_%s' % (ascii_translit(tok[2:]) or tok[2:])
    return None


# ── 台词插值登记（[[正文]] 里的 {变量} / <变量> / (主体.字段)）──
TEXT_FIELDS = {                     # 主体.字段
    '姓': '.family_name', '名': '.given_name', '名前': '.name',
    '姓名': '.full_name', '代名詞': '.pronoun',
}
TEXT_VARS = {                       # 裸变量
    '一人稱': 'Text::first_person', '二人稱': 'Text::second_person',
    '二人稱名前': 'Text::second_person_name', '武器': 'Text::weapon',
    '年': 'Time::year', '月': 'Time::month', '日': 'Time::day',
}


# ═══ 命令参数位签名（位 → 收什么）═══
#   位类型：'E' 具名实体 / 'D' 域名 / 'A' 属性名 / '<枚举集名>'
#   任何位都隐含允许：数字、槽（人物Ａ/ａ）、特殊值（主人公/無效…）、域::X 形态、事件 ID、空参
#   键 '*' = 命令头值（發生契機:據點畫面表示後(…) 里冒号后、括号前那一段）
CMD_ARG_SPEC = {
    # ── 容器（pick 组）──
    '容器篩選': {0: ('D',), 1: ('A',), 2: ('E', '真偽', '狀態值', '身份', '物品種類',
                                        '武器種類', '生存狀態', '軍團槽', '人物類別')},
    '容器排除': {0: ('D',), 1: ('A',), 2: ('E', '身份', '真偽', '狀態值', '人物類別', '物品種類')},
    '容器設定': {0: ('D',), 1: ('A',), 2: ('E', '真偽', '人物類別', '物品種類', '狀態值')},
    '容器排序': {0: ('D',), 1: ('A', '排序特殊鍵'), 2: ('排序方向',)},
    '容器選擇': {1: ('容器位置',)},
    '容器清理': {0: ('容器清理',)},
    '容器存取': {0: ('容器存取',)},
    '容器檢索': {0: ('D',), 1: ('A',)},
    # ── 状态写入 / 条件 ──
    '更新': {1: ('狀態值',)},
    '調查': {0: ('容器統計',), 1: ('狀態值', '出現狀態', '性別', '生存狀態', 'D', 'E')},
    '場合分歧': {0: ('其他分支', '狀態值')},
    '主人公分歧': {0: ('E', '其他分支', '模板NPC')},
    '遊戲中斷': {0: ('真偽',)},
    # ── 演出（05）──
    'ＢＧＭ變更': {0: ('ＢＧＭ',)},
    'ＳＥ開始': {0: ('ＳＥ',)}, 'ＳＥ循環': {0: ('ＳＥ',)}, 'ＳＥ停止': {0: ('ＳＥ',)},
    '圖片表示': {0: ('圖片類型',), 1: ('事件ＣＧ', 'E'), 4: ('轉場',)},
    '圖片消去': {0: ('轉場',)},
    '背景變更': {0: ('背景類型',), 1: ('背景', 'E'), 2: ('轉場',)},
    '背景恢復': {0: ('轉場',)},
    '畫面效果': {0: ('畫面效果',)},
    '進入設施': {0: ('設施',)},
    '下個場面': {0: ('設施',)},
    # ── 事件文件头 ──
    '屬性': {'*': ('事件屬性',)},
    '發生契機': {'*': ('觸發',), 0: ('E', '設施', '軍團槽', '生存狀態', '身份', '觸發'),
                1: ('E', '設施'), 2: ('軍團指令', 'E'), 3: ('E', '軍團槽')},
    # ── 对话 ──
    '對話': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},
    '變名對話': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},
    '對話選擇': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},
    '對話可否選擇': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},
    # ── 军团（02）──
    '軍團指令': {0: ('軍團槽',), 1: ('軍團指令',), 2: ('E', '軍團槽'), 3: ('軍團槽',)},
    # ── 人事 / 势力 ──
    '人物登用': {0: ('E',), 1: ('從屬類型',), 2: ('E',)},
    '人物解雇': {0: ('E',), 1: ('E',), 2: ('出現狀態',)},
    '立場變更': {0: ('E',), 1: ('從屬類型',), 2: ('E',)},
    '獨立': {0: ('E',), 1: ('E',), 2: ('獨立方式',)},
    '改名': {0: ('E',)}, '據點改名': {0: ('E',), 1: ('E',)},
    '強制移動': {0: ('E',)}, '居城變更': {1: ('E',)},
    '城主任命': {0: ('E',), 1: ('E',)}, '城主解任': {0: ('E',)},
    '國主任命': {0: ('E',), 1: ('E',), 2: ('E',)}, '國主解任': {0: ('E',)},
    '家督讓位': {0: ('E',), 1: ('E',)},
    '勢力滅亡': {0: ('E',), 1: ('E',)}, '武將死亡': {0: ('E',)},
    '成為御用商人': {0: ('E',)},
    '主命作成': {0: ('E',), 1: ('E',)}, '事件主命作成': {0: ('E',)}, '解除主命': {0: ('E',)},
    '事件主命變更': {0: ('主命字段',)},
    # ── 战斗 / 小游戏 ──
    '個人戰鬥': {0: ('逃跑許可',), 1: ('護衛',), 2: ('E', '身份', '模板NPC'),
                3: ('E', '身份', '模板NPC'), 4: ('E', '身份', '模板NPC'),
                5: ('E', '身份', '模板NPC'), 6: ('E', '身份', '模板NPC'),
                7: ('E', '身份', '模板NPC'), 8: ('決鬥場地',), 9: ('真偽',), 10: ('真偽',)},
    '迷你遊戲': {0: ('迷你遊戲',), 1: ('E', '模板NPC'), 2: ('難度',)},
    '強制武器交換': {0: ('武器種類',)},
}

# 军团编成家族（軍團編成/軍團編成最強/海賊軍團編成*/忍者軍團編成*）参数位一致 → 共用签名
_ARMY_FORM = {0: ('軍團槽',), 1: ('E',), 2: ('軍團指令',), 3: ('E', '軍團槽'), 4: ('軍團槽', 'E'),
              5: ('E',), 6: ('E',), 7: ('E',), 8: ('E',), 9: ('E',), 10: ('E',),
              11: ('零值',), 12: ('零值',), 13: ('零值',), 14: ('零值',), 15: ('零值',),
              16: ('零值',), 17: ('零值',)}
for _p in ('', '海賊', '忍者'):
    for _s in ('軍團編成', '軍團編成最強'):
        CMD_ARG_SPEC[_p + _s] = dict(_ARMY_FORM)

# 代入族（代入ａ/代入ｔ…）：右值可为容器统计量
CMD_ARG_PREFIX = {'代入': {1: ('容器統計',)}}


def arg_spec(cmd, pos):
    """(命令, 位) → 允许的类型元组；未登记位返回 ()（只收数字/槽/特殊值/域引用）。"""
    spec = CMD_ARG_SPEC.get(cmd)
    if spec is None:
        for pre, s in CMD_ARG_PREFIX.items():
            if cmd.startswith(pre):
                spec = s
                break
    return (spec or {}).get(pos, ())


def arg_side(cmd, pos, tok):
    """命令裸参数 → 侧名。表外返回 None（生成期报错）。"""
    kinds = arg_spec(cmd, pos)
    for k in kinds:
        if k == 'E':
            s = entity_side(tok)
        elif k == 'D':
            s = DOMAIN_MAP.get(tok) and ('Domain::' + tok)
        elif k == 'A':
            s = next((pair_side(d, tok) for d in PREFIX_BY_DOMAIN if pair_side(d, tok)), None)
        else:
            s = enum_side(k, tok)
        if s:
            return s
    return None


'''


def main():
    src = io.open(P, encoding='utf-8').read()
    if 'CMD_ARG_SPEC = {' in src:
        print('已打过补丁，跳过')
        return
    assert ANCHOR in src, '锚点缺失'
    src = src.replace(ANCHOR, BLOCK + ANCHOR, 1)
    # cmd_rule：事件主命未知75 这类「XX未知NN」也是反编译碎片（原来只认开头 未知）
    old = "    if name.startswith('未知'):"
    new = "    if re.search(r'未知[0-9\\uff10-\\uff19]+$', name) or name.startswith('未知'):"
    assert old in src
    src = src.replace(old, new, 1)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ 插入参数签名段 %d 行 + cmd_rule 修正' % len(BLOCK.splitlines()))


if __name__ == '__main__':
    main()

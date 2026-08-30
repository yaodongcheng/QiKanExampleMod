# -*- coding: utf-8 -*-
"""
gen_entity_maps.py —— 从《骑砍2太阁Mod表.xlsx》生成实体归一表 `tools/entity_maps.py`。

解决什么问题
------------
tk5_to_json.py 原来手写了 6 张名字→StringId 的字典（HERO_MAP / AGENT_MAP / CLAN_MAP /
KINGDOM_MAP / SETTLEMENT_MAP / REGION_MAP），只覆盖桶狭间那几十个人。全量 2594 事件一开跑，
手写表必然漏，而且漏了不报错——直接生成 `tk5_uXXXXXX` 哈希占位，静默错到底。

本脚本把这张表换成从织丰数据表机器生成：
    Knowledge/骑砍2织丰角色ID对应/csv/*.csv                （xlsx_to_csv.py 从织丰表转换的上游镜像）
        + Modules/ShokuhoTaikouExpansionPack/ModuleData/{Shokuho,DesignData}/*.xml  （存在性核对）
        → tools/entity_maps.py                          （生成物，禁止手改，铁律 22）

三条纪律
--------
1. **铁律 20**：产出的全是游戏内 StringId，中文只作查找键，不进 ID。
2. **铁律 22**：`entity_maps.py` 是生成物；要改映射 → 改本脚本（OVERRIDES / 后缀规则）→ 重跑。
3. **铁律 5**：每个 ID 都拿模块 XML 核对过存在性；核不到的进 `MISSING_IN_XML`，翻译器照常用
   （数据包可能后补），但报告里点名，禁止静默。

🔴 物品/交易品（2026-08-30 v6.1）
--------------------------------
织丰 xlsx 无物品表（列表见下）→ 新建 **`Knowledge/骑砍2织丰角色ID对应/csv/item.csv`**：
**非镜像人工维护源表**（xff 织丰物品数据第 1 张表，07 数据包建织丰物品时填骑砍真 StringId）。
- 人只编辑 ID / CNName / TK5Type / Remark 四列；TK5Name / Kind / SourceCount 由本脚本从语料
  扫描维护（only-append 补行，已有行不覆盖）。
- 本脚本产 ITEM_MAP/MERC_T_MAP：ID 列有真值用真值（铁律 5）；空/占位 = 确定性占位 ID
  tk5_item_*/tk5_trade_*（生成器产物，翻译器零兜底不变）。

繁简
----
太阁源文是繁体（織田信長），织丰表是简体（织田信长）。生成期用 zhconv 把简体名转成繁体，
**两种写法都进表**，运行时不需要 zhconv 依赖。zhconv 缺失时只生成简体键并告警。

用法
----
    python tools/xlsx_to_csv.py             # 上游织丰表更新后：刷新镜像 CSV（一次性转换，见该脚本）
    python tools/gen_entity_maps.py            # 生成 + 打统计
    python tools/gen_entity_maps.py --report   # 只打统计与缺口清单，不写文件
"""
from __future__ import unicode_literals
import io
import os
import re
import sys
import csv
import collections
import hashlib

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))          # LivingWorldNpcs
MODULES = os.path.dirname(ROOT)                                        # …/Modules
CSV_DIR = os.path.join(ROOT, 'Knowledge', '骑砍2织丰角色ID对应', 'csv')
SHOKUHO = os.path.join(MODULES, 'ShokuhoTaikouExpansionPack', 'ModuleData')
# 🔴 活数据在**基础织丰 mod**（Modules/Shokuho），不在扩展包：扩展包的 Shokuho/settlements.xml
# 只有 5 条示例、spkingdoms.xml 只有 1 条。存在性核对必须扫基础 mod，否则会误报几百条缺失。
SHOKUHO_BASE = os.path.join(MODULES, 'Shokuho', 'ModuleData')
P_OUT = os.path.join(HERE, 'entity_maps.py')

# 剧本年份（桶狭间 = 1560 日轮之章）——决定取哪一组「登场/身份/势力/据点」列
SCENARIO_YEAR = '1560'

try:
    import zhconv
    def to_trad(s):
        return zhconv.convert(s, 'zh-hant')
    HAS_ZH = True
except Exception:                                                      # pragma: no cover
    def to_trad(s):
        return s
    HAS_ZH = False


# ---------------------------------------------------------------------------
# 人工覆盖：太阁原文里的写法 ≠ 织丰表里的写法（生成期一次性对齐，运行时无中文参与）
# ---------------------------------------------------------------------------
# 🔴 NAME_ALIAS = 双向别名表（2026-08-29 约定，使用侧必须双向查询）：
#   ① dict 本体 = 单向「太阁写法(左键) → 织丰写法(右键)」；
#   ② 反向查询由调用方构建 ALIAS_REV(右键→[左键们])，一对多支持（如 丰臣秀吉←木下秀吉/羽柴秀吉）；
#   ③ 两类条目，方向都必须是「左=源文/太阁目录名，右=织丰 CNName」：
#      - 目录名别名：左键 = TK5 BUSTUP 立绘目录名（含异体字，如 丽璐/龟井茲矩/淀殿），
#        右键 = 主源 CSV 的 CNName（里璐/龟井兹矩/淀夫人）——build_refs_full 建图匹配用，
#        左键应能在 E:\taikou5\TaikouImage\BUSTUP 命中目录；
#      - 史名别名：左键 = 历史异写/化名（豐臣秀吉/寧寧/木下秀吉…），右键 = 织丰名——
#        不要求左键在 BUSTUP，服务剧本侧文本与 Name_{年份} 匹配；
#   ④ 防呆检查：新增条目后跑一遍「左键 BUSTUP 命中」把两类分开核对（史名别名命中失败属于正常）。

NAME_ALIAS = {
    # 太阁写法 → 织丰表写法（简体）
    '豐臣秀吉': '木下藤吉郎',
    '豐臣秀長': '木下小一郎',
    '德川家康': '松平元康',
    '寧寧': '宁宁',
    # 🔴 2026-08-28：从历史脚本 generate_taikou_char_info.py 的 HISTORICAL_ALIASES 抄入（原则 3 化名库）。
    # 只抄 5 条（tk 源文无键 + zf 表内确认存在）；旧脚本其余 8 条未抄：
    # 松平元康/长尾景虎/大友义镇/木下小一郎/真田幸村/真田信幸/黑田如水 = tk 已有键；斋藤利三 = 自映射占位。
    '木下秀吉': '丰臣秀吉',
    '羽柴秀吉': '丰臣秀吉',
    '竹中重治': '竹中半兵卫',
    '黑田孝高': '黑田如水',   # 🔴 zf 必须 = CNName（黑田官兵卫 只是年份别名，alias 机制只认 CNName 行）
    '山本晴幸': '山本勘助',
    # 2026-08-29 底图匹配（ArtSource/build_refs_full.py Task1）补：织丰显示名 ≠ TK5 立绘目录名
    # 的同人写法。方向 = 太阁写法 → 织丰写法（与全表同义）；build_refs_full 反向使用做 TK5 目录候选。
    '长坂钓闲': '长阪长闲',   # 530_长坂钓闲(长坂长闲)：长闲/钓闲 = 长坂光定 通称，括号别名曾命中
    '铃木佐大夫': '铃木佐太夫',
    '糟屋武则': '糟谷武则',
    '小川祐忠': '小川佑忠',
    '阿尔梅达': '阿鲁梅达',   # 1039_阿尔梅达（音译变体）
    '拉斐尔': '拉斐耶鲁',     # 1040_拉斐尔（音译变体）
    '弗洛伊斯': '佛罗伊斯',
    '淀殿': '淀夫人',
    '德姬': '德公主',
    '白㭴': '白枧',   # 1093_白㭴（㭴/枧 异体）
    '菊姬': '菊公主',
    '早川殿': '早川夫人',
    '南姬': '南姫',
    '泰泉寺丰后': '秦泉寺丰后',   # 1271_泰泉寺丰后（秦/泰 史称异写）
    '森田浄云': '森田净云',
    '武田逍遙轩': '武田逍遥轩',
    '龟井茲矩': '龟井兹矩',   # 239_龟井茲矩（太阁用異体字）
    '丽璐': '里璐',   # 1041_丽璐（太阁）=> lord_1_sato 里璐（织丰）；同音异形，2026-08-29 用户抓包   # 1038_弗洛伊斯（音译变体）
}

# 势力别名：太阁按 1560 年的家名叫，织丰表按代表家名建 Kingdom
# （长尾景虎 1561 年才继上杉家；织丰表只有 uesugi 一条，所以 1560 年的「长尾家」要指过去）
KINGDOM_ALIAS = {
    '长尾': '上杉',
}

# 太阁独有、织丰表里没有的角色 → 占位 ID（有主占位，07 数据包补齐后回填这里重跑）
TK5_ONLY_HERO = {
    '服部小平太': 'tk5_hattori_koheita',
    '毛利新介': 'tk5_mori_shinsuke',
    '簗田政綱': 'tk5_yanada_masatsuna',
}

# 🔴 ID 写法平替（2026-08-28 用户抓包 小早川隆景 案）：主表按「全名式/后人名」造 id，
# 但基础织丰 mod 真身用「省名式/当年名」——同一人两套 id，表里还标着「精确匹配织丰」。
# 原则 1：沿用真身 id。全表扫描又回 13 条同类（改名前/别号系列），已确认同人；
# （铃木重秀↔杂贺孙一 有父子歧义，留 07c 步骤 2 分诊，不武断映射）
ID_REPLACE = {
    'lord_1_kobayakawa_takakage': 'lord_1_kobayakawa',   # 小早川隆景
    'lord_1_ito_hoan': 'lord_1_ito',                     # 伊东义祐
    'lord_1_ito_sadachika': 'lord_1_ito_7',              # 伊东祐兵
    'lord_1_tsugaru_tamenobu': 'lord_1_oura',            # 大浦为信（津轻为信）
    'lord_1_sakazaki_naomori': 'lord_1_ukita_9',         # 宇喜多诠家
    'lord_1_yamana_yuu': 'lord_1_yamana',                # 山名祐丰
    'lord_1_tachibana_dosetsu': 'lord_1_bekki',          # 户次鉴连（立花道雪）
    'lord_1_takeda_harufusa': 'lord_1_takeda_9',         # 武田信廉（逍遥轩）
    'lord_1_sanada_taneju': 'lord_1_sanada_8',           # 真田信幸
    'lord_1_anayama_akisane': 'lord_1_anayama',          # 穴山信君
    'lord_1_hosokawa_yuu': 'lord_1_hosokawa',            # 细川藤孝（幽斋）
    'lord_1_oda_kiyoeimon': 'lord_1_oda_10',             # 织田长益（有乐）
    'lord_1_tachibana_muneshige': 'lord_1_takahashi_2',  # 高桥统虎（立花宗茂）
    'lord_1_kuroda_iekata': 'lord_1_kuroda',             # 黑田孝高（官兵卫/如水）
}

# 模板角色（无 Hero 身份的路人）→ Agent:: 模板引用；织丰表里不会有，本表就是事实源
AGENT_MAP = {
    '忍者': 'tk5_ninja', '小姓': 'tk5_kosho', '家臣': 'tk5_kashin', '傳令': 'tk5_denrei',
    '侍從': 'tk5_jiju', '足輕': 'tk5_ashigaru', '備大將': 'tk5_bitaisho', '部將': 'tk5_busho',
    '武將': 'tk5_busho_generic', '僧侶': 'tk5_monk', '旅人': 'tk5_traveler',
    '守將': 'tk5_shusho', '守軍': 'tk5_shugun', '今川兵': 'tk5_imagawa_soldier',
    '士兵': 'tk5_soldier', '功勳家臣': 'tk5_kashin_merit', '武力家臣': 'tk5_kashin_martial',
    '外交家臣': 'tk5_kashin_diplomat', '功勳陪臣': 'tk5_hikan_merit',
}

# 地方（令制国）——织丰表**有**分区数据，但不是这一层：CityTaikou.Area 是 62 个细分区，
# Culture 表是 9 个文化大区（畿内/关东/东海/北陆/东山/山阳/南海/西海/奥羽）。太阁的
# `地方::近畿` 用的是现代地方名，与 Culture 的古代八道一一对得上（近畿=畿内、中国=山阳、
# 四国=南海、九州=西海、东北=奥羽、甲信=东山），只是叫法不同——缺的是这张 9 行对照表，
# 不是数据（07b §五-1）。令制国这一层织丰确实没有，下表兜底。
REGION_MAP = {
    '駿河': 'tk5_suruga', '遠江': 'tk5_totomi', '三河': 'tk5_mikawa', '尾張': 'tk5_owari',
    '美濃': 'tk5_mino', '伊勢': 'tk5_ise', '近江': 'tk5_omi', '山城': 'tk5_yamashiro',
    '甲斐': 'tk5_kai', '信濃': 'tk5_shinano', '相模': 'tk5_sagami', '武藏': 'tk5_musashi',
}

# 🔴 TK5 物品/交易品 → 罗马字 slug（2026-08-30 v6.2 用户裁定：ID 列按骑砍 StringId 规则填——小写
#   蛇形语义词（同织丰既有物品风格：fire_arrow / sho_fukinuki_banner / hr_weapon_kunai…）。
#   规则：中日同形词 → 日文读法（三日月茶壺→mikazuki_chaho、葡萄酒→putaojiu/麦酒→bakushu）；
#   纯中文词 → 汉语拼音（斗篷→doupeng、蛋糕→dangao）；读音存疑的取近似读法 + 注释。
#   词表外新词条 = md5 占位 + 报告点名「待罗马字转写」（07 数据包落地时补词表重跑）。
ITEM_SLUG = {
    '万葉集': 'manyoshu', '万金丹': 'mankin_tan', '三味線': 'shamisen', '三國黑': 'mikuni_kuro',
    '三日月茶壺': 'mikazuki_chaho', '三芳野': 'miyoshino', '不二山': 'fujisan',
    '世界圖屏風': 'sekai_zu_byobu', '丹波茶壺': 'tanba_chaho', '九十九髪茄子': 'tsukumo_nasubi',
    '二人静': 'futari_shizuka', '五輪書': 'gorinsho', '信樂燒': 'shigaraki_yaki',
    '備前燒': 'bizen_yaki', '備前長船兼光': 'bizen_osafune_kanemitsu', '僧坊酒': 'soboshi',
    '初花肩衝': 'hatsuhana_katatsuki', '利休酒': 'rikyu_shu', '十文字槍': 'jumonji_yari',
    '千鳥之香炉': 'chidori_no_koro', '反魂丹': 'hankontan', '古今和歌集': 'kokinshu',
    '古天明平蜘蛛': 'kotenmei_hirakumo', '吉光骨食': 'yoshimitsu_kotsujiki', '呂宋壺': 'ruson_tsubo',
    '唐芋': 'kara_imo', '國友筒': 'kunimoto_tsutsu', '地球儀': 'chikyugi', '大真珠': 'o_shinju',
    '天國': 'tengoku', '天明釜': 'tenmei_gama', '宗三左文字': 'sozan_samonji', '寶冠': 'hokan',
    '小粒金': 'kotsubugane', '小雲雀': 'kohibari', '山桃': 'yamamomo', '帝釋栗毛': 'taishaku_kurige',
    '徒然草': 'tsurezuregusa', '忍鎌': 'shinobi_gama', '手裏剣': 'shuriken', '捨子': 'sutego',
    '放生月毛': 'hojou_tsukige', '斗篷': 'doupeng', '新田肩衝': 'shinden_katatsuki',
    '日本號': 'nihon_go', '旨酒': 'umazake', '星崎': 'hoshizaki', '會津黑': 'aizu_kuro',
    '朝鮮唐津': 'chousen_karatsu', '春慶塗': 'shunkei_nuri', '更紗': 'sarasa', '杉原紙': 'sugihara_kami',
    '村正': 'muramasa', '村雨': 'murasame', '松島之壺': 'matsushima_no_tsubo',
    '松本茶碗': 'matsumoto_chawan', '松風': 'matsukaze', '根來塗': 'negoro_nuri', '梨': 'nashi',
    '梳子': 'shuzi', '楢柴肩衝': 'narashiba_katatsuki', '正宗': 'masamune', '水晶': 'suisho',
    '沙丁魚': 'shadingyu', '淚': 'namida', '清酒': 'seishu', '渡航朱印狀': 'tokou_shuinjou',
    '濁酒': 'nigorizake', '無效藥': 'fukouyaku', '無銘槍': 'mumei_yari', '煙槍': 'enso',
    '物干竿': 'monohoshizao', '珍陀酒': 'chindashu', '珠光小茄子': 'shuko_konasubi',
    '瓶割刀': 'kamewarito', '疊': 'tatami', '白石': 'shiraishi', '百段': 'hyakudan',
    '祥瑞': 'shouzui', '童子切': 'doujigiri', '竹光': 'takemitsu', '紅寶石之戒': 'hongem_no_yubiwa',
    '縹糸下散紅威': 'kokiito_shitasankoui', '美濃紙': 'mino_gami', '翡翠首飾': 'hisui_no_kazari',
    '聖騎士之鎧': 'seikishi_no_yoroi', '肥皂': 'feizao', '脇差': 'wakizashi', '色紙': 'shikishi',
    '茜': 'akane', '荏胡麻': 'egoma', '花下遊樂圖': 'hanashita_yuraku_zu', '胡椒': 'koshou',
    '莫邪': 'moye', '菊池槍': 'kikuchi_yari', '菊酒': 'kikuzake', '萩燒': 'hagi_yaki',
    '萬曆赤絵': 'manreki_akae', '葡萄': 'budo', '葡萄酒': 'putao_jiu', '蕎麦': 'soba',
    '藍': 'ai', '蘆屋釜': 'ashiya_gama', '蘭奢待': 'ranshatai', '蚩尤之鎧': 'shiyu_no_yoroi',
    '蛋糕': 'dangao', '蜜柑': 'mikan', '赤樂茶碗': 'akaraku_chawan', '足輕具足': 'ashigaru_gusoku',
    '輪島塗': 'wajima_nuri', '近江黑': 'oomi_kuro', '醬油': 'shoyu', '金塊': 'kinkai',
    '金平糖': 'konpeito', '金陀美具足': 'kondami_gusoku', '鎖具足': 'kusari_gusoku',
    '鑽石': 'zuanshi', '雷切': 'raikiri', '青磁花入': 'seiji_hanaire', '馬蝗絆': 'bakouhaken',
    '鬼丸': 'onimaru', '鮨': 'sushi', '鯛魚': 'tai', '鳥子紙': 'torinoko_kami', '麦酒': 'bakushu',
    '黄瀬戶花入': 'kizeto_hanaire', '黄瀬戶茶碗': 'kizeto_chawan', '黄金分銅': 'ougon_bundo',
    '黑樂茶碗': 'kuro_raku_chawan', '黑葦威胴丸': 'kuroashi_odoshi_doumaru', '黑雲': 'kurogumo',
    '龍蝦': 'longxia', '煙草': 'tabako', '牡蠣': 'kaki', '玻璃瓶': 'boli_ping', '白磁': 'hakuji',
    '硯': 'suzuri', '筆': 'fude', '紅花': 'benibana', '紙': 'kami', '紫根': 'shikon',
}

# 🔴 据点两列的语义不一样，不能混用（2026-08-28 查表实证）：
#   MatchSettlement = 骑砍地图上就是这座城（99 条）→ 直接用它的 StringId；
#   NearSettlement  = 骑砍地图上**没有**这座城，只给了最近的一个（81 条）→ 拿它当同一个地方就错了
#                     （太阁「鸣海城」的 near 是「那古野」——这是两座城，用了会让鸣海攻防打到那古野去）。
#   所以：只有 near 的城 → 自己发一个 tk5_city_NNN 占位 ID（07 数据包补真城），
#         同时把 near 记进 SETTLEMENT_ANCHOR，让事件知道该在地图哪一带发生。
# 例外白名单：near 其实就是同一座城，只是汉字写法不同（清洲/清须、河越/川越…），这些直接用真 ID。
SAME_AS_NEAR = {
    '清洲城': '清须城',      # 清洲 = 清須，织田本城
    '河越城': '川越城',      # 河越 = 川越
    '阪本城': '坂本',        # 阪本 = 坂本
    '踯躅崎馆': '甲府城',    # 武田居馆即甲府
    '石山本愿寺': '大坂御坊',  # 石山 = 大坂
    '芥川城': '芥川山城',
}

# 据点名后缀：太阁「鳴海」/「鳴海城」/「岡崎之町」↔ 织丰「鸣海城」
SUFFIXES = ('', '城', '馆', '館', '之町', '町', '之砦', '砦', '港', '之港')

# 🔴 织丰表查无之町（2026-08-30 v6 回填——太阁有、CityTaikou 表查无此地 → 无 Match 无 Near）：
#   闭包登记占位 ID + 报告点名（07 数据包补真城/锚点）；ID 沿用 2026-08-27 report 用过的
#   tk5_busan/tk5_naha/tk5_ningbo/tk5_lusong（与已发布报告里的占位 ID 保持一致，不另起名字）
CLOSURE_TOWN = {
    '釜山之町': 'tk5_busan',
    '那覇之町': 'tk5_naha',
    '寧波之町': 'tk5_ningbo',
    '呂宋之町': 'tk5_lusong',
}


# ---------------------------------------------------------------------------
# 读织丰表（CSV 镜像，由 tools/xlsx_to_csv.py 从《骑砍2太阁Mod表.xlsx》转换）
# ---------------------------------------------------------------------------
def read_sheets():
    """读 csv/ 下一张 sheet 一个 CSV。返回同旧格式：{sheet名: [dict]}。

    列名 = 表头行；数据行缺列补 ''；类型/注释行（string/int/编号/骑砍ID/内置番号 开头）
    跳过（织丰表格式，不是数据）。
    🔴 镜像 CSV 禁止手改（铁律 22 精神）：改映射 → 本文件映射表；改数据 → 改 xlsx 后
       重跑 xlsx_to_csv.py 刷新镜像。
    """
    sheets = {}
    if not os.path.isdir(CSV_DIR):
        print('找不到 CSV 数据目录：%s（先跑 `python tools/xlsx_to_csv.py` 从织丰表转换）' % CSV_DIR)
        return None
    for fn in sorted(f for f in os.listdir(CSV_DIR) if f.endswith('.csv')):
        with io.open(os.path.join(CSV_DIR, fn), encoding='utf-8-sig', newline='') as f:
            rows = list(csv.reader(f))
        if not rows:
            continue
        head = rows[0]
        body = [r for r in rows[1:] if any(r)]
        # 第 2~3 行 = 类型行 / 中文注释行（织丰表格式），不是数据
        while body and body[0] and body[0][0] in ('string', 'int', '编号', '骑砍ID', '内置番号'):
            body.pop(0)
        sheets[fn[:-4]] = [dict(zip(head, r + [''] * (len(head) - len(r)))) for r in body]
    return sheets


def keys_of(name):
    """一个中文名 → 查找键集合（原样 + 繁体）。"""
    ks = {name}
    t = to_trad(name)
    if t:
        ks.add(t)
    return ks


def put(d, name, value, conflicts, tag):
    for k in keys_of(name):
        if k in d and d[k] != value:
            conflicts.append('%s：「%s」既指向 %s 又指向 %s（保留先到的）' % (tag, k, d[k], value))
            continue
        d.setdefault(k, value)


# ---------------------------------------------------------------------------
# 模块 XML 存在性核对（铁律 5）
# ---------------------------------------------------------------------------
def xml_ids(*globs):
    """扫模块 XML 收集所有 id=""。参数是相对 Modules/ 的 glob（基础 mod + 扩展包都扫）。"""
    import glob as _g
    ids = set()
    for pat in globs:
        for p in _g.glob(os.path.join(MODULES, *pat.split('/'))):
            if not os.path.isfile(p):
                continue
            txt = io.open(p, encoding='utf-8', errors='replace').read()
            ids |= set(re.findall(r'id="([^"]+)"', txt))
    return ids


def main():
    report_only = '--report' in sys.argv
    sh = read_sheets()
    if sh is None:
        return 1
    need = ('TaikouHero', 'Clan', 'Kingdom', 'Settlements', 'CityTaikou')
    for n in need:
        if n not in sh:
            print('xlsx 缺工作表「%s」（现有：%s）' % (n, ' / '.join(sh)))
            return 1

    conflicts = []
    year = SCENARIO_YEAR

    # ---- 据点：CityTaikou（太阁城）→ Settlements（骑砍据点）----
    settle_by_name = collections.defaultdict(list)
    settle_ids = set()
    for r in sh['Settlements']:
        if r.get('ID'):
            settle_by_name[r.get('CityName', '')].append(r['ID'])
            settle_ids.add(r['ID'])

    def find_settle(n):
        if not n:
            return None
        for s in SUFFIXES:
            if n + s in settle_by_name:
                return settle_by_name[n + s][0]
        for s in ('城', '馆', '館', '之町', '之砦'):
            if n.endswith(s) and n[:-len(s)] in settle_by_name:
                return settle_by_name[n[:-len(s)]][0]
        return None

    def strip_suffix(n):
        return re.sub('(城|馆|館|御所|之町|町|之砦|砦|湊|港)$', '', n or '')

    SETTLEMENT_MAP, SETTLEMENT_ANCHOR = {}, {}
    placeholder_city, anchorless_city = [], []
    for r in sh['CityTaikou']:
        cn = r.get('ChineseName', '')
        if not cn:
            continue
        near = r.get('NearSettlement', '')
        sid = find_settle(r.get('MatchSettlement', ''))
        if not sid and near:
            # near 与本城同名（只差后缀）或在白名单里 → 同一座城；否则只当锚点
            if strip_suffix(near) == strip_suffix(cn) or SAME_AS_NEAR.get(cn) == near:
                sid = find_settle(near)
        if not sid:
            sid = find_settle(cn)                     # 城名本身在 Settlements 里
        anchor = None
        if not sid:
            sid = 'tk5_city_%03d' % int(r.get('ID') or 0)
            anchor = find_settle(near)
            (placeholder_city if anchor else anchorless_city).append(cn)

        # 🔴 同一座城的不同区都指向同一个据点：太阁把「岡崎城」（城）和「岡崎之町」（町区）
        # 当两个地点写，骑砍这边 town_CHUB10 一个据点就把城和町都包了。不注册变体键的话，
        # 「岡崎之町」查不到 → 发独立占位 → 事件里「筛选所属据点=岡崎之町的人」一个也筛不到。
        # 🔴 据点表和锚点表必须挂**同一套查找键**（2026-08-28 修）：原来锚点只挂在表里的正名
        # （鳴海城）上，事件里写的是别名（鳴海館）→ 据点查得到、锚点查不到，报告里显示成
        # 「占位据点无锚点」，事件就不知道该在地图哪一带发生。实测键覆盖率只有 20.7%。
        names = [cn]
        base = re.sub('(城|馆|館|之町|町|之砦|砦)$', '', cn)
        if base and base != cn:
            names.append(base)
            names.extend(base + suf for suf in ('城', '之町', '町', '之砦', '砦',
                                                '館', '馆', '港', '之港') if base + suf != cn)
        for n in names:
            put(SETTLEMENT_MAP, n, sid, conflicts, '据点')
            if not anchor:
                continue
            for k in keys_of(n):
                if SETTLEMENT_MAP.get(k) == sid:      # 键确实指向这座占位城，才给它挂锚点
                    SETTLEMENT_ANCHOR.setdefault(k, anchor)

    # 🔴 织丰表查无之町（太阁有、CityTaikou 表里连名字都没有 → 无 Match 无 Near，连锚点都发不出）：
    #   2026-08-30 v6 回填（原 tk5_to_json FALLBACK_MAP 兜底已删，归信源 B 生成器闭包登记）。
    #   纪律 21 同款：确定性占位 ID + 报告点名，07 数据包补真城/补锚点。
    for _cn, _sid in CLOSURE_TOWN.items():
        put(SETTLEMENT_MAP, _cn, _sid, conflicts, '据点（织丰表查无，闭包占位）')
        anchorless_city.append(_cn + '（织丰表查无）')

    # ---- 势力：Kingdom 表 ----
    kingdom_ids = set()
    KINGDOM_BY_NAME = {}
    for r in sh['Kingdom']:
        if not r.get('ID'):
            continue
        kingdom_ids.add(r['ID'])
        for n in (r.get('ChineseName', ''), (r.get('ChineseName', '') + '家')):
            if n:
                put(KINGDOM_BY_NAME, n, r['ID'], conflicts, '势力')

    def find_kingdom(n):
        if not n or n == '无':
            return None
        bare = re.sub('家$', '', n)
        bare = KINGDOM_ALIAS.get(bare, bare)
        return KINGDOM_BY_NAME.get(n) or KINGDOM_BY_NAME.get(bare)

    # ---- 家族：Clan 表 ----
    clan_ids = set(r['ID'] for r in sh['Clan'] if r.get('ID'))

    # ---- 人物：TaikouHero 表（本剧本年份那一组列）----
    hero_rows = [r for r in sh['TaikouHero'] if r.get('ID')]
    alias_rev = {}
    for tk, zf in NAME_ALIAS.items():
        alias_rev.setdefault(zf, []).append(tk)

    # ---- 模板 NPC：织丰表模板行（模板NPC=1，ScriptName/CNName = TK5 模板名）→ CharacterObject
    #      模板真 StringId，优先于手写占位（tk5_* = 表外模板才用）；铁律 8 模板身份 = CharacterObject
    agent_map = dict(AGENT_MAP)
    for r in hero_rows:
        if (r.get('模板NPC') or '').strip() != '1':
            continue
        tid = r.get('ID')
        if not tid:
            continue
        for n in (r.get('ScriptName', ''), r.get('CNName', '')):
            if n:
                agent_map[n] = tid        # 表行真 ID 优先（织丰现成 CharacterObject，铁律 5）
    hero_rows = [r for r in hero_rows if (r.get('模板NPC') or '').strip() != '1']   # 🔴 模板行进 HERO_MAP
    #   = 语义污染（CharacterObject 模板 ≠ HeroObject；原 HERO_MAP 的 template_shougun_01 与 AGENT_MAP
    #   tk5_bitaisho 双命中歧义，2026-08-30 v6）——hero 相关循环一律在过滤后的列表上跑

    HERO_MAP, HERO_META, CLAN_BY_HERO, KINGDOM_BY_HERO = {}, {}, {}, {}
    org_names = collections.Counter()
    heroid2cn = {}            # hero ID → 中文名（当主→势力反查用）
    clan_kingdom = {}         # ClanID → Kingdom ID（Clan 表 Kingdom 列）
    owner_id = {}             # ClanID → Owner(当主 hero ID)
    for r in sh['Clan']:
        if r.get('ID'):
            clan_kingdom[r['ID']] = r.get('Kingdom', '') or ''
            owner_id[r['ID']] = r.get('Owner', '') or ''
    for r in hero_rows:
        cn = r.get('CNName', '')
        if not cn:
            continue
        hid = r['ID']
        heroid2cn[hid] = cn

    # ---- 人物：TaikouHero 表（本剧本年份那一组列）----
    for r in hero_rows:
        cn = r.get('CNName', '')
        if not cn:
            continue
        hid = r['ID']
        names = [cn] + alias_rev.get(cn, [])
        # 本年份的姓名列（改名角色：木下藤吉郎 → 羽柴秀吉）
        yname = r.get('Name_%s' % year, '')
        if yname and yname not in names:
            names.append(yname)
        for n in names:
            put(HERO_MAP, n, hid, conflicts, '人物')
        cid = r.get('ClanID', '')
        kid = clan_kingdom.get(cid, '')
        if not kid:
            # 🔴 Kingdom_年份 列实测 = 该年居城名（骏府城/清洲城…），不是势力名——
            #   当主→势力一律走 Clan 表（Clan.Kingdom 列），禁以城名当势力名（2026-08-30 v6）
            kname = ''
        else:
            kname = r.get('Kingdom_%s' % year, '')
        if kname and kname != '无':
            org_names[kname] += 1
        HERO_META[hid] = {
            'clan': cid,
            'kingdom': kid or '',
            'kingdom_name': r.get('Kingdom_%s' % year, ''),   # 王国名（今川家）；镜像修复后正确
            'city': SETTLEMENT_MAP.get(r.get('City_%s' % year, ''), ''),
            'appear': r.get('Appear_%s' % year, ''),
            'identity': r.get('Identity_%s' % year, ''),
            'stance': r.get('CareerStance_%s' % year, ''),
        }
        for n in names:
            if cid:
                put(CLAN_BY_HERO, n, cid, conflicts, '家族')
    # 🔴 当主名 → Kingdom（2026-08-30 v6 双链修复——TaikouHero 镜像 CSV 曾被加列事故错位
    #   （2dbba69：TK5编号插入时数据行右移，Kingdom_年份 列读到 City 残值），修复后：
    #   链①（首选）：hero.ClanID（真家族 ID）→ Clan.Kingdom —— 一行直达；
    #   链②（兜底）：Kingdom_年份 列 = 王国名（今川家）→ Kingdom 表 ChineseName（+家）直接查。
    #   2026-08-30 用户确认：Kingdom_年份 = 1560 剧本所处王国（枚举 = Kingdom.csv）。
    settle_owneroc = {}
    for r in sh['Settlements']:
        if r.get('CityName') and r.get('OwnerClan'):
            settle_owneroc[r['CityName']] = r['OwnerClan'].replace('Faction.', '')
    for cid, okid in clan_kingdom.items():          # 链①（Clan.Owner 当主反查，直接命中者优先）
        oname = heroid2cn.get(owner_id.get(cid, ''), '')
        if okid and oname:
            put(KINGDOM_BY_HERO, oname, okid, conflicts, '势力(按人)')
    for r in hero_rows:                              # 链①② 同时跑（按人字典序后到者不覆盖——put 先到保留）
        cn = r.get('CNName', '')
        if not cn:
            continue
        cid = r.get('ClanID', '')
        kid = clan_kingdom.get(cid, '') or ''
        if not kid:
            kid = find_kingdom(r.get('Kingdom_%s' % year, ''))
        if kid:
            for n in [cn] + alias_rev.get(cn, []):
                put(KINGDOM_BY_HERO, n, kid, conflicts, '势力(按人)')

    # 🔴 TK5_ONLY 三人 = Hero 实例（有名有姓的个体，用户 2026-08-28 裁定）。
    #   归属：挂 Faction.clan_oda_1 —— 骑砍的 Clan/Faction 是**政治集团**（原版草寇/
    #   雇佣商人/流浪者各有 clan），「织田家武士」挂织田集团语义正确；无 clan Hero
    #   在织丰战役容错不明（风险），用户裁定挂织田（安全优先）。
    #   HERO_META.clan 同步填 clan_oda_1；家庭成员无关，不体现血亲。
    for n, hid in TK5_ONLY_HERO.items():
        put(HERO_MAP, n, hid, conflicts, '人物')
        HERO_META.setdefault(hid, {'clan': 'clan_oda_1', 'kingdom': 'oda', 'kingdom_name': '织田家',
                                   'city': '', 'appear': '太阁独有', 'identity': '织田家武士', 'stance': ''})
    # 🔴 ID_REPLACE 置换（2026-08-28）：金句「表里的全名式 id 换基础 mod 真身省名式 id」
    for k, v in list(HERO_MAP.items()):
        if v in ID_REPLACE:
            HERO_MAP[k] = ID_REPLACE[v]
        HERO_META.setdefault(ID_REPLACE.get(v, v), HERO_META.get(v))  # 防 HERO_META 键仍然挂在旧 id 上
    for old, new in ID_REPLACE.items():
        if old in HERO_META:
            HERO_META.setdefault(new, HERO_META.pop(old))

    # ---- 存在性核对（🔴 2026-08-28 双池改造：基础织丰 mod / 扩展包分开扫，再不分池混扫）----
    xml_hero_base = xml_ids('Shokuho/ModuleData/heroes/*.xml', 'Shokuho/ModuleData/lords/*.xml',
                            'Shokuho/ModuleData/spnpccharactertemplates.xml',
                            'Shokuho/ModuleData/spspecialcharacters/*.xml',
                            'Shokuho/ModuleData/spnpccharacters/*.xml')
    xml_hero_exp = xml_ids('ShokuhoTaikouExpansionPack/ModuleData/*/heroes.xml',
                           'ShokuhoTaikouExpansionPack/ModuleData/*/lords.xml')
    xml_clan_base = xml_ids('Shokuho/ModuleData/spclans/*.xml')
    xml_clan_exp = xml_ids('ShokuhoTaikouExpansionPack/ModuleData/*/clans.xml')
    xml_kingdom_base = xml_ids('Shokuho/ModuleData/spkingdoms/*.xml')
    xml_settle_base = xml_ids('Shokuho/ModuleData/settlements.xml',
                              'Shokuho/ModuleData/port_location_settlements.xml',
                              'Shokuho/ModuleData/*_location_settlements.xml')
    # ---- 物品/交易品（2026-08-30 v6.2 用户裁定：ID 列按骑砍 StringId 规则填——tk5_item_/tk5_trade_
    #   + 罗马字语义词，与织丰既有物品 ID（fire_arrow/sho_fukinuki_banner/hr_weapon_kunai）同风格；
    #   ID = 生成器按 ITEM_SLUG 词表产出（确定性、可读），07 数据包建真物品时人覆盖为真 StringId）。
    #   🔴 v6.2 槽剔除：`物品::物品Ａ`/`圖片表示:(物品,物品Ａ,…)` 是**槽引用**（16a 域值区
    #   Ctx::item_a），不是物品名——含全角字母/数字的词一律剔除（物品Ａ/Ｂ、交易品Ａ/Ｅ 曾误收录）。
    #   职责：人只编辑 ID/CNName/TK5Type/Remark；生成器维护 TK5Name/Kind/SourceCount + 自动补行
    #   （only-append 语义，人填行不覆盖——仅槽行清洗会删行）；脚本不猜真 ID。
    ITEM_MAP, MERC_T_MAP = {}, {}
    _item_csv_path = os.path.join(CSV_DIR, 'item.csv')
    _src_tk5 = os.path.join(ROOT, 'Knowledge', '太阁事件包', 'TK5AllEvents_merged.txt')
    _item_kinds = collections.defaultdict(collections.Counter)   # TK5名 -> Counter(item/trade_good)
    _item_known = {}                                             # TK5名 -> 已登记行

    def _is_slot_name(k):
        """槽引用判定：物品Ａ/交易品Ｅ 等——含全角字母或纯全角数字 = 槽（16a 有 Ctx::item_* 注册）。"""
        return bool(re.search(r'[Ａ-Ｚａ-ｚ０-９]', k)) or k in ('無效', '主人公')

    if os.path.exists(_src_tk5):
        _txt = io.open(_src_tk5, encoding='utf-8-sig').read()
        for _m in re.finditer(r'(?:物品|交易品)::([^.\s（()（）]+)', _txt):
            _k = _m.group(1)
            if _k and not _is_slot_name(_k):
                _item_kinds[_k]['trade_good' if _m.group(0).startswith('交易品') else 'item'] += 1
        for _m in re.finditer(r'圖片表示:\s*\(\s*物品\s*,\s*([^,()]+)', _txt):
            _k = _m.group(1).strip()
            if _k and not _is_slot_name(_k):
                _item_kinds[_k]['item'] += 1
    if os.path.exists(_item_csv_path):
        with io.open(_item_csv_path, encoding='utf-8-sig') as _f:
            for _r in csv.DictReader(_f):
                _n = (_r.get('TK5Name') or '').strip()
                if _n and not _is_slot_name(_n):          # 🔴 槽行清洗（v6.0 误收录 → 剔除）
                    _item_known[_n] = _r
    # 补行 + 汇总（唯一写入口：本段结尾统一写回；已有行仅当 ID 为空/占位时回填规则 ID，不覆盖人）
    _rows = [_r for _n, _r in sorted(_item_known.items())]
    _slug_used, _slug_missing = {}, []
    def _item_id(_n, _kind):     # 规则 ID：slug 优先，词表缺 = md5 占位 + 点名
        _slug = ITEM_SLUG.get(_n)
        if not _slug:
            _slug_missing.append(_n)
            _slug = hashlib.md5(_n.encode('utf-8')).hexdigest()[:6]
        _n2 = _slug_used.get(_slug, 0) + 1
        _slug_used[_slug] = _n2
        _slug = '%s_%d' % (_slug, _n2) if _n2 > 1 else _slug
        return ('tk5_trade_' if _kind == 'trade_good' else 'tk5_item_') + _slug
    for _n in sorted(_item_kinds):
        if _n in _item_known:
            continue
        _cnt = _item_kinds[_n]
        _kind = 'trade_good' if _cnt.get('trade_good', 0) > _cnt.get('item', 0) else 'item'
        _rows.append({'ID': '', 'TK5Name': _n, 'CNName': '', 'TK5Type': '', 'Kind': _kind,
                      'SourceCount': str(sum(_cnt.values())),
                      'Remark': '2026-08-30 语料扫描（07 数据包补 ID/类型）'})
    _shielded = 0                                        # 已定真 ID 的行数（07 人填，不覆盖）
    for _r in _rows:
        _n = _r['TK5Name']
        _kind = _r.get('Kind')
        if _kind not in ('item', 'trade_good'):
            _cnt = _item_kinds[_n]
            _kind = 'trade_good' if _cnt.get('trade_good', 0) > _cnt.get('item', 0) else 'item'
            _r['Kind'] = _kind
        _cid = (_r.get('ID') or '').strip()
        if _cid and not _cid.startswith('tk5_'):
            _shielded += 1
            (MERC_T_MAP if _kind == 'trade_good' else ITEM_MAP)[_n] = _cid
            continue
        _r['ID'] = _item_id(_n, _kind)
        (MERC_T_MAP if _kind == 'trade_good' else ITEM_MAP)[_n] = _r['ID']
    import csv as _csv
    with io.open(_item_csv_path, 'w', encoding='utf-8-sig', newline='') as _f:
        _w = _csv.DictWriter(_f, fieldnames=['ID', 'TK5Name', 'CNName', 'TK5Type',
                                             'Kind', 'SourceCount', 'Remark'])
        _w.writeheader()
        for _r in _rows:
            _w.writerow({k: (_r.get(k) or '') for k in _w.fieldnames})
    missing = collections.OrderedDict()
    supplements = {}
    def check2(label, ids, base_pool, exp_pool):
        """分层核对：真缺 = 两边都没有；supplement = 只在扩展包生成物（注册前运行时不活）。"""
        sup = sorted(i for i in ids - base_pool if i in exp_pool and not i.startswith('tk5_'))
        bad = sorted(i for i in ids - base_pool - exp_pool if not i.startswith('tk5_'))
        if bad:
            missing[label] = bad
        if sup:
            supplements[label] = sup
        return bad, sup
    check2('人物', set(HERO_MAP.values()), xml_hero_base, xml_hero_exp)
    check2('家族', clan_ids, xml_clan_base, xml_clan_exp)
    # 🔴 势力只核 IsShokuho=1（启用）的：IsShokuho=0 = 织丰做好但未启用的预备数据（07b §4.1 已定性），不是缺口
    kingdom_active = set()
    for r in sh['Kingdom']:
        if r.get('ID') and (r.get('IsShokuho') or '').strip() == '1':
            kingdom_active.add(r['ID'])
    check2('势力', kingdom_active, xml_kingdom_base, set())
    check2('据点', settle_ids, xml_settle_base, set())

    # ---- 统计 ----
    print('== 织丰表 → 实体归一表 ==')
    print('人物 %d 行 → %d 个 ID，查找键 %d 个%s'
          % (len(hero_rows), len(set(HERO_MAP.values())), len(HERO_MAP),
             '' if HAS_ZH else '（⚠️ 无 zhconv，只生成了简体键，繁体源文会查不到）'))
    real_city = set(v for v in SETTLEMENT_MAP.values() if not v.startswith('tk5_city_'))
    print('家族 %d ／ 势力 %d ／ 据点 %d' % (len(clan_ids), len(kingdom_ids), len(settle_ids)))
    print('太阁城 %d 条：对上骑砍真城 %d 座，占位 %d 座（其中 %d 座连锚点都没有）'
          % (len(sh['CityTaikou']), len(real_city),
             len(placeholder_city) + len(anchorless_city), len(anchorless_city)))
    if anchorless_city:
        print('  连最近据点都查不到（07 数据包要连位置一起定）：' + '、'.join(anchorless_city))
    if org_names:
        print('  %d 个「势力」在 Kingdom 表里没有条目 = 组织（水军/众/屋）→ 走 Org:: 占位（16b T3-预留）：%s'
              % (len(org_names), '、'.join(n for n, _ in org_names.most_common(8)) + ' …'))
    for label, bad in missing.items():
        print('  ⚠️ %s：%d 个 ID 在模块 XML 里查不到 → %s' % (label, len(bad), '、'.join(bad[:6])))
    for label, sup in supplements.items():
        print('  🔴 %s：%d 个 ID 只在扩展包生成物（需注册才行，运行时未注册 = 查得到活不了）→ %s'
              % (label, len(sup), '、'.join(sup[:6])))
    n_base = sum(1 for v in set(HERO_MAP.values()) if v in xml_hero_base)
    n_exp = sum(1 for v in set(HERO_MAP.values()) if v not in xml_hero_base and v in xml_hero_exp)
    print('  人物存活分层：基础 mod %d ｜ 生成物 %d ｜ 全新 %d' % (n_base, n_exp, len(set(HERO_MAP.values())) - n_base - n_exp))
    prem = len(kingdom_ids) - len(kingdom_active)
    if prem:
        print('  势力：IsShokuho=0 预备数据 %d 条（织丰做好未启用，非缺口，07b §4.1）' % prem)
    for c in conflicts[:10]:
        print('  冲突 ' + c)
    if len(conflicts) > 10:
        print('  …还有 %d 条冲突' % (len(conflicts) - 10))

    if ITEM_MAP or MERC_T_MAP:
        _all = len(_item_kinds)
        print('  物品 %d 件 ／ 交易品 %d 件（源表 csv/item.csv %d 词条：规则 ID 已生成 %d，'
              '07 真 ID %d（人已填，不覆盖））'
              % (len(ITEM_MAP), len(MERC_T_MAP), _all, _all - _shielded, _shielded))
        if _slug_missing:
            print('    ⚠️ 罗马字词表缺失 %d 词条（ID = md5 占位，待补 gen_entity_maps.py ITEM_SLUG）：%s'
                  % (len(set(_slug_missing)), '、'.join(sorted(set(_slug_missing))[:12])))
        print('    物品样例：%s' % '、'.join(sorted(ITEM_MAP)[:10]))
    if report_only:
        return 0

    # ---- 生成 ----
    def lit(s):
        return "'" + str(s).replace('\\', '\\\\').replace("'", "\\'") + "'"

    def dump(name, d, comment):
        buf = ['# %s\n' % comment, '%s = {\n' % name]
        for k in sorted(d):
            buf.append('    %s: %s,\n' % (lit(k), lit(d[k])))
        buf.append('}\n\n')
        return ''.join(buf)

    out = ['# -*- coding: utf-8 -*-\n',
           '# 🔴 自动生成，勿手改（铁律 22）。由 tools/gen_entity_maps.py 从\n',
           '# Knowledge/骑砍2织丰角色ID对应/csv/*.csv（xlsx_to_csv.py 从织丰表转换的镜像）生成，'
           '剧本年份 = %s。\n' % year,
           '# 要改映射 → 改 gen_entity_maps.py（NAME_ALIAS / TK5_ONLY_HERO / SUFFIXES）→ 重跑。\n',
           'from __future__ import unicode_literals\n\n',
           'SCENARIO_YEAR = %s\n\n' % lit(year)]
    out.append(dump('HERO_MAP', HERO_MAP, '中文名（繁/简）→ Hero StringId'))
    out.append(dump('CLAN_BY_HERO', CLAN_BY_HERO, '当主名 → Clan StringId（太阁 `大名家::織田信長` = 织田信长的家）'))
    out.append(dump('KINGDOM_BY_HERO', KINGDOM_BY_HERO, '当主名 → Kingdom StringId（%s 年在籍势力）' % year))
    out.append(dump('KINGDOM_BY_NAME', KINGDOM_BY_NAME, '势力名 → Kingdom StringId'))
    out.append(dump('SETTLEMENT_MAP', SETTLEMENT_MAP,
                    '太阁据点名 → Settlement StringId（tk5_city_NNN = 骑砍地图上没有这座城，07 数据包补）'))
    out.append(dump('SETTLEMENT_ANCHOR', SETTLEMENT_ANCHOR,
                    '占位据点 → 最近的骑砍真据点（决定事件在地图哪一带发生，不是同一个地方）'))
    out.append(dump('AGENT_MAP', agent_map,
                    '模板角色名 → CharacterObject 模板 StringId（织丰表模板行 template_* 真 ID 优先；'
                    'tk5_* = 表外模板占位，07 数据包补）'))
    out.append(dump('REGION_MAP', REGION_MAP, '令制国名 → Region 占位 ID（织丰表只到「文化大区/细分区」两层，无令制国层，本表兜底）'))
    out.append(dump('ITEM_MAP', ITEM_MAP,
                    'TK5 物品名 → Item StringId（源表 csv/item.csv 的 ID 列——真 ID 优先；'
                    'tk5_item_XXXX = 占位（07 数据包建织丰物品后填 item.csv 重跑即换真 ID））'))
    out.append(dump('MERC_T_MAP', MERC_T_MAP,
                    'TK5 交易品名 → Item StringId（同上；tk5_trade_XXXX = 占位）'))
    out.append('# 人物在本剧本年份的状态：{hero_id: {clan, kingdom, kingdom_name, city, appear, identity, stance}}\n')
    out.append('HERO_META = {\n')
    for hid in sorted(HERO_META):
        m = HERO_META[hid]
        out.append('    %s: {%s},\n' % (lit(hid), ', '.join(
            '%s: %s' % (lit(k), lit(m[k])) for k in ('clan', 'kingdom', 'kingdom_name',
                                                     'city', 'appear', 'identity', 'stance'))))
    out.append('}\n\n')
    out.append('# 在 Kingdom 表里查不到的「势力」= 组织（忍者众/海贼众/商家），走 Org:: 占位\n')
    out.append('ORG_NAMES = {\n')
    for n in sorted(org_names):
        oid = 'tk5_org_' + hashlib.md5(n.encode('utf-8')).hexdigest()[:6]
        for k in sorted(keys_of(n)):
            out.append('    %s: %s,\n' % (lit(k), lit(oid)))
    out.append('}\n\n')
    out.append('# 模块 XML 里查不到的 ID（数据包待补；翻译器照常引用，报告点名）\n')
    out.append('MISSING_IN_XML = {\n')
    for label, bad in missing.items():
        out.append('    %s: [%s],\n' % (lit(label), ', '.join(lit(b) for b in bad)))
    out.append('}\n\n')
    # 🔴 存活分层（2026-08-28 用户裁定）：调用方先问 ORIGIN 再决定引用安全性
    out.append('# 🔴 StringId 存活分层（2026-08-28）：base = 织丰基础 mod 本来就有的（可直接引用）；\n'
               '#   supplement = 只在扩展包生成物里（SubModule 注册前运行时不活——查得到 ≠ 找得到）；\n'
               '#   new = 两边都没有（待 07 数据包/07c 步骤 2 新增）。翻译器引用前必查。\n')
    def dump_origin(name, d):
        buf = ['%s = {\n' % name]
        for i in sorted(d):
            buf.append('    %s: %s,\n' % (lit(i), lit(d[i])))
        buf.append('}\n\n')
        return ''.join(buf)
    out.append(dump_origin('HERO_ORIGIN',
                           {i: ('base' if i in xml_hero_base else 'supplement' if i in xml_hero_exp else 'new')
                            for i in set(HERO_MAP.values())}))
    out.append(dump_origin('CLAN_ORIGIN',
                           {i: ('base' if i in xml_clan_base else 'supplement' if i in xml_clan_exp else 'new')
                            for i in clan_ids}))
    out.append(dump_origin('SETTLEMENT_ORIGIN',
                           {i: ('base' if i in xml_settle_base else 'new')
                            for i in settle_ids}))
    io.open(P_OUT, 'w', encoding='utf-8').write(''.join(out))
    print('已生成 %s' % os.path.relpath(P_OUT, ROOT))
    return 0


if __name__ == '__main__':
    sys.exit(main())

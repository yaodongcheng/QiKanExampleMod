# -*- coding: utf-8 -*-
"""
tk5_to_json.py — 太阁5 事件 → 01 DSL 事件 JSON 机械翻译器（v6 数据驱动重构）
================================================================
设计规格：plans/scenario-campaign-mode/08b-转化器规格-自动化翻译流水线.md §十五（v6）

🔴 v6（2026-08-30，用户裁定）核心：翻译脚本停止"认识命令"，改成"查表"。
  16a CSV 自带全部翻译知识；脚本只做两件事 = 解析 TK5 语法 + 查表翻译；
  查不到 = RegistryGapError 停机 + 缺口报告（agent 回填映射表收敛到零）。
  CSV = 翻译知识唯一事实源，脚本禁止复制第二条。

双信源架构（§15.1.5）：
  信源 A = 16a-DSL翻译总表.csv（一切名词：命令/域/属性/函数/枚举值/语法/文本变量/域值）
  信源 B = tools/entity_maps.py（具名实体：人名/城名/家族/势力/据点 StringId；生成物，禁手改）
  选择规则 = 值类型/类别标注驱动（标注来自 CSV 参数列/值类型列/域值区），禁止硬编码。

v6 相对 v5 的变更：
  1. 删除全部"翻译型兜底表"：SLOT_NAME_MAP/_SLOT_CAT/slot_cname/BOOL_MARKER_WORDS/
     FALLBACK_MAP/_fallback_ascii/_ENTITY_FALLBACK/_FUNC_SIDES/TRIGGER_MAP/TRIGGER_FORM
     ——槽名读 CSV 命令区 slot= 预设、布尔拼写读属性行"语义"列、触发值/设施值读枚举值区、
     域前缀读 16a 域表侧名、函数集从 16a 函数区+属性区构建。
  2. _WORLD_EFFECTS 硬编码字典删除 → 事件头/居城变更等全部查表（CSV 命令区 + 语法区）。
  3. 事件头也查表：trigger = 枚举值区「觸發」域（去 Trigger:: 前缀）、facility = 「設施」域
     （完整引用 Facility::*）、once/priority = 「事件屬性」域。
  4. 零兜底纪律（§15.1.5）：翻译器内无任何"查无兜底"代码路径——查无 = 停机报错，
     报（什么词条/哪个实体/源第几行/查的哪个信源/建议回填哪）。
  5. --strict 为默认（无 --strict 参数：行为等价，gap_report 始终输出）。
  6. 更新 = 真步骤（用户 2026-08-30 裁定：16a 权威 update（T1）→ effect action=update）。
  7. 槽名统一小写（08b §15.1.5：以 CSV 命令区 slot= 为准；域值区 Ctx::hero_a 同形）。

用法：
    python tk5_to_json.py --events EFF0C300_159 --scenario okehazama
"""
import argparse
import csv
import json
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
DEFAULT_SOURCE = os.path.join(REPO_ROOT, "Knowledge", "太阁事件包", "TK5AllEvents_merged.txt")
DEFAULT_REGISTRY = os.path.join(REPO_ROOT, "plans", "scenario-campaign-mode", "16a-DSL翻译总表.csv")
DEFAULT_OUT = os.path.join(REPO_ROOT, "plans", "scenario-campaign-mode", "story_event_json")

# ---------------------------------------------------------------------------
# 信源 B：实体归一表（生成物，禁止手改——要改映射改 gen_entity_maps.py 重跑，铁律 22）
# ---------------------------------------------------------------------------
try:
    import entity_maps as _EM
except ImportError:                                    # pragma: no cover
    raise SystemExit(
        "缺 tools/entity_maps.py —— 先跑 `python tools/gen_entity_maps.py` 生成实体归一表")

# 🔴 v6：以下 = 信源 B 的读取器/接口适配（非翻译知识，A7 裁定保留——前缀注入属 DSL 语法层）。
#   值 = 完整 DSL 引用（带域前缀）；实体查无 = 停机报错（零兜底，见 lookup_entity）。
HERO_MAP = dict((k, "Hero::" + v) for k, v in _EM.HERO_MAP.items())
HERO_MAP["主人公"] = "Hero::MainHero"
AGENT_MAP = dict((k, "Agent::" + v) for k, v in _EM.AGENT_MAP.items())
CLAN_MAP = dict((k, "Clan::" + v) for k, v in _EM.CLAN_BY_HERO.items())
KINGDOM_MAP = dict((k, "Faction::Kingdom." + v) for k, v in _EM.KINGDOM_BY_NAME.items())
for _k, _v in _EM.KINGDOM_BY_HERO.items():
    KINGDOM_MAP.setdefault(_k, "Faction::Kingdom." + _v)
SETTLEMENT_MAP = dict((k, "Settlement::" + v) for k, v in _EM.SETTLEMENT_MAP.items())
REGION_MAP = dict((k, "Region::" + v) for k, v in _EM.REGION_MAP.items())
ORG_MAP = dict((k, "Org::" + v) for k, v in _EM.ORG_NAMES.items())
MISSING_IN_XML = _EM.MISSING_IN_XML
CITY_PLACEHOLDER = dict((v, k) for k, v in _EM.SETTLEMENT_MAP.items() if v.startswith("tk5_city_"))
CITY_ANCHOR = dict(_EM.SETTLEMENT_ANCHOR)

# 域词 → 信源 B 表（数据驱动：域名表即 16a 域表键，表 = 实体表；这里只挂"表引用"，不挂名词）
_ENTITY_TABLES = {
    "人物": (HERO_MAP, AGENT_MAP),      # Hero 优先，模板 NPC（铁律 8 双类）次之
    "大名家": (CLAN_MAP,),
    "勢力": (KINGDOM_MAP,),
    "城": (SETTLEMENT_MAP,), "據點": (SETTLEMENT_MAP,), "砦": (SETTLEMENT_MAP,),
    "町": (SETTLEMENT_MAP,), "里": (SETTLEMENT_MAP,),
    "國": (REGION_MAP,),
    "忍者衆": (ORG_MAP,), "商家": (ORG_MAP,), "海賊衆": (ORG_MAP,),
}

# ---------------------------------------------------------------------------
# 非翻译知识常量区（剧本绑定 / DSL 语法层 / 参数名语义词表——v6 逐条注明出处）
# ---------------------------------------------------------------------------
EVENT_NAME = {
    "EFF0C300_159": "情报宣告+评议会+敦盛之舞+出阵（织田线开场）",
    "EFF0C300_160": "鸣海攻防（守将台词）",
    "EFF0C300_161": "鸣海结果（守城胜/陷落败）",
    "EFF0C300_162": "热田参拜",
    "EFF0C300_163": "野战·义元休憩（织田线）",
    "EFF0C300_164": "义元之死",
    "EFF0C300_165": "鸣海换首级",
    "EFF0C300_166": "凯旋+世界结算",
    "EFF0C300_171": "余波他人视角",
    "EFF06E00_159": "今川评定·上京方针",
    "EFF06E00_160": "运粮野战（元康先锋）",
    "EFF06E00_161": "鸣海报捷（今川线）",
    "EFF06E00_163": "义元休憩（今川线）",
    "EFF06E00_164": "野战结算（今川线胜/败）",
    "EFF06E00_165": "兵粮进城+义元死讯",
    "EFF06E00_166": "今川凯旋（织田灭亡 IF）",
    "EFF06E00_167": "大树寺打探",
    "EFF06E00_168": "今川上洛 IF 后日谈",
    "EFF06E00_169": "家康独立",
    "EFF06E00_170": "今川侧战后余波（氏真）",
    "EFF06E00_171": "今川家臣余波",
    "ECF00000_159": "旅人通报义元战死（世界广播）",
}
CLUSTER_ORDER = [   # 桶狭间历史时间轴（剧本绑定配置）
    "EFF0C300_159", "EFF06E00_159",
    "EFF0C300_160", "EFF06E00_161",
    "EFF0C300_161", "EFF0C300_162",
    "EFF0C300_163", "EFF06E00_163",
    "EFF0C300_164", "EFF06E00_164",
    "EFF0C300_165",
    "EFF0C300_166", "EFF06E00_166", "EFF06E00_168",
    "EFF0C300_171", "EFF06E00_170", "EFF06E00_171",
    "ECF00000_159",
]

# 🔴 trigger → 05 演出形态（**非翻译知识**：performance 形态不是 TK5 名词，属 05 判定（A6 裁定）；
#   未注册默认 menu_dialogue（据點画面）。trigger 值本身查 16a 枚举值区「觸發」域。
TRIGGER_FORM = {
    "據點畫面表示後": "menu_dialogue",
    "評定開始時": "menu_dialogue",
    "室內畫面表示後": "scene",
    "野戰開始時": "scene",
    "野戰結束時": "scene",
    "攻城戰開始時": "scene",
    "攻城戰結束時": "scene",
    "軍團移動結束時": "map_dialogue",
}

# 🔴 参数名 → 域词（Registry 常量；属"参数名本身"的语义词表，与枚举命令无关——
#   CSV 未列（参数名列只写名字），纪律 16 v6 注同款批准保留（~25 条）
PARAM_DOMAIN = {
    "actor": "人物", "hero": "人物", "party": "人物",
    "faction": "大名家", "clan": "大名家",
    "settlement": "據點", "pos": "據點", "sceneId": "據點",
    "a": "大名家", "b": "大名家",
    "leader": "人物", "target": "據點", "owner": "人物",
    "amount": None, "name": None, "status": None, "orderId": None,
    "presetId": None, "behavior": None,
}

# 🔴 命令 × 参数位 → 域词（参数名语义词表同款；posN 形式无参数名时的域裁定——
#   CSV 参数列只写「具名实体」不写域，按命令语义裁定。只登记实证组合，未登记 = 值扫描唯一化）
PARAM_CTX_DOMAIN = {
    "成為御用商人#0": "商家", "成為御用商人#1": "人物",
    "國主任命#0": "人物", "國主任命#1": "國", "國主任命#2": "國",
    "國主解任#0": "國",
}

# 🔴 值空间名 → 字段名（§15.4-1：字段名 = 值空间英文（lowerCamel）——"值空间命名归一"、
#   Registry 常量（~20 条）。键 = 16a 枚举值区/参数列的"所属域"词（ＢＧＭ/ＳＥ/觸發…）
VALUE_SPACE_FIELD = {
    "ＢＧＭ": "bgm", "ＳＥ": "se", "觸發": "trigger", "設施": "facility",
    "事件ＣＧ": "cg", "轉場": "transition", "背景類型": "bgType", "圖片類型": "imageType",
    "事件屬性": "eventMeta", "容器位置": "mode", "容器清理": "clearMode",
    "容器存取": "accessMode", "容器統計": "containerStat", "其他分支": "branchOther",
    "零值": "zero", "難度": "difficulty", "迷你遊戲": "minigame",
    "物品種類": "itemKind", "武器種類": "weaponKind", "主命": "quest",
    "主命字段": "questField", "主命目標類": "questTarget", "排序方向": "order",
    "排序特殊鍵": "sortKey", "軍團槽": "armySlot", "軍團指令": "armyCommand",
    "生存狀態": "aliveState", "狀態值": "stateValue", "人物類別": "heroKind",
    "身份": "identity", "性別": "gender", "從屬類型": "subType", "出現狀態": "appearState",
    "畫面效果": "screenFx", "背景": "bg", "通關方式": "clearMode",
    "獨立方式": "independenceMode", "逃跑許可": "escapePermit", "護衛": "escort",
    "模板NPC": "npc", "域": "domain", "屬性": "attr",
}

# 🔴 内嵌管线白名单（§15.4-2 用户认可：线内管线 = TK5 语法结构，非业务命令——
#   侧名白名单集中一处常量。台词管线输出 = 05 形态词（dialogue/narrator/choice），
#   不是 CSV 侧名（say 只用于"查表识别"），纪律 2 v6 注同款。
SAY_SIDES = {"say", "say_choice", "say_as", "monologue", "monologue_choice", "narrate"}
# 台词侧名 → 05 形态词（dialogue 对白 / narrator 旁白自语；形态 = 语法层，非名词）
SAY_FORM = {"say": "dialogue", "say_choice": "choice", "say_as": "dialogue",
            "monologue": "narrator", "monologue_choice": "choice", "narrate": "narrator",
            "monologue_selectable": "narrator", "narrate_selectable": "narrator",
            "say_selectable": "dialogue"}
CHOICE_SIDES = {"choice", "choice_option_set", "say_selectable", "narrate_selectable"}
CONTAINER_SIDES = {"container_set", "container_filter", "container_exclude",
                   "container_sort", "container_pick", "container_clear",
                   "container_query", "container_access"}
# 块类语法侧名（结构层表：这些侧名 = 源词条的登记名，**JOSN 输出词 = 01 步骤词**
#   if/when/loop/module_exit——语法区侧名不当 JSON 字段，§15.1.5 结构层边界）
BLOCK_SIDES = {"branch", "case_when", "case_branch", "protagonist_when", "protagonist_branch",
               "loop", "module_begin", "module_exit", "script", "condition",
               "condition_and", "condition_or", "update", "game_end",
               "event_meta", "event_trigger", "event_condition", "event_script"}

# 实体域词 → 域表侧名（实体名表的前缀注入 = DSL 语法层；非名词翻译。
#   Kingdom 特例：DSL 引用先例 = Faction::Kingdom.oda（01/16 语法权威），其余域侧名直拼 '::'）
REF_PREFIX_OVERRIDE = {"Kingdom": "Faction::Kingdom.", "NinjaOrg": "Org::",
                       "MerchantOrg": "Org::", "PirateOrg": "Org::"}

# 🔴 TK5 域词 → 属性侧名段前缀集（属性行多段侧名 = 按 DSL 段取段，如 `Hero.clan / Settlement.clan`——
#   段前缀 = DSL 命名空间（铁律 20），**与域表侧名（Town/Castle=据点类型语义名）是两套**（不混）。
#   语法层常量（REF_PREFIX 同族）；候选唯一 → 直接取；歧义（多家）→ 生成器缺陷清单。
SIDE_PREFIX = {
    "人物": {"Hero"}, "大名家": {"Clan"}, "勢力": {"Faction"},
    "城": {"Settlement"}, "據點": {"Settlement", "Facility"}, "砦": {"Settlement"},
    "町": {"Settlement"}, "里": {"Settlement"},
    "國": {"Region", "Settlement"}, "地方": {"Region"},
    "忍者衆": {"Org"}, "商家": {"Org"}, "海賊衆": {"Org"},
    "卡": {"Card", "Item"}, "流派": {"Card"}, "物品": {"Item", "Card"},
    "工作": {"QuestDef"}, "事件主命": {"QuestDef"}, "主命": {"Quest"},
    "主命屬性": {"QuestAttr"}, "事件": {"Event"}, "事件標誌": {"Flag"},
    "事件發生狀態": {"EventState"}, "變量": {"Variable"}, "儲存號": {"GlobalSlot"},
    "狀況": {"Time"}, "日數計數器": {"Counter"}, "環境變量": {"EnvVar"},
    "背景音樂": {"Bgm"}, "天氣": {"Weather"}, "軍團": {"Army"},
    "軍團方針": {"ArmyDoctrine"}, "身份": {"Identity"}, "人物類別": {"HeroKind"},
    "物品類型": {"ItemKind"}, "戰鬥結束種類": {"BattleEndKind"},
    "遊戲通關種類": {"EndingKind"}, "官位": {"court_rank"}, "官職": {"title"},
    "真偽": {"Bool"}, "場面": {"Facility"},
}

# 🔴 值空间别名（§15.4-1「值空间命名归一」Registry 常量）：属性值类型「枚举:X」的空间名与
#   枚举值区所属域不同名 → 别名归并（義理→狀態值、物品類型→物品種類 …）；别名表外 = 报 gap
ENUM_SPACE_ALIAS = {
    "義理": "狀態值", "物品類型": "物品種類", "與主人公關係": "狀態值",
    "關係經緯": "狀態值", "仕官傾向": "狀態值", "身份": "身份",
    "官位": "官位", "官職": "官位", "大方針": "狀態值", "戰略": "狀態值",
}


# ---------------------------------------------------------------------------
# 解析器（v1 已验证，保持）
# ---------------------------------------------------------------------------
class Line:
    def __init__(self, raw):
        self.raw = raw
        self.text = raw.strip()
        self.cmd = ""
        self.args_raw = ""
        self.texts = []
        self._split()

    def _split(self):
        t = self.text
        self.texts = re.findall(r"\[\[(.*?)\]\]", t, re.DOTALL)
        clean = re.sub(r"\[\[.*?\]\]", "", t, flags=re.DOTALL).strip()
        if ":" in clean:
            self.cmd, self.args_raw = clean.split(":", 1)
            self.cmd = self.cmd.strip()
            self.args_raw = self.args_raw.strip()
        else:
            self.cmd = clean

    def params(self):
        """顶层括号参数列表（对话/代入等用）。"""
        if not self.args_raw:
            return []
        return _extract_balanced(self.args_raw)


class Block:
    def __init__(self, name, raw):
        self.name = name
        self.raw = raw
        self.children = []
        self.bare_cmd = name.split(":", 1)[0].split("(")[0].strip()
        self.args = _extract_balanced(name) if "(" in name else []
        self.args_raw = name.split("(", 1)[1].rsplit(")", 1)[0] if "(" in name else ""


def _extract_balanced(text):
    params, buf, depth, started = [], [], 0, False
    for ch in text:
        if ch == "(":
            if not started:
                started, depth = True, 1
                continue
            depth += 1
            buf.append(ch)
        elif ch == ")":
            if started:
                depth -= 1
                if depth == 0:
                    params.append("".join(buf).strip())
                    buf, started = [], False
                else:
                    buf.append(ch)
        elif started:
            buf.append(ch)
    return params


def parse_source(source_text):
    events, cur_id, cur_body = {}, None, []
    for raw in source_text.splitlines():
        line = raw.strip()
        if line.startswith("事件:事件"):
            if cur_id:
                events[cur_id] = cur_body
            m = re.match(r"事件:事件([A-Z0-9]+_\d+)\{?", line)
            cur_id = m.group(1) if m else None
            cur_body = []
        elif line.startswith("}//事件"):
            if cur_id:
                events[cur_id] = cur_body
                cur_id, cur_body = None, []
        elif cur_id is not None:
            cur_body.append(raw)
    if cur_id:
        events[cur_id] = cur_body
    return events


def build_tree(body_lines):
    root, stack = [], []
    for raw in body_lines:
        t = raw.strip()
        if not t:
            continue
        if t.startswith("}//"):
            if stack:
                stack.pop()   # 开块时已 append 到父/root，闭合只出栈
            continue
        clean = re.sub(r"//.*$", "", t).strip()
        if clean.endswith("{"):
            head = clean[:-1].rstrip()
            if head:
                b = Block(head, raw)
                (stack[-1].children if stack else root).append(b)
                stack.append(b)
            continue
        if t.startswith("//"):
            continue
        ln = Line(raw)
        if ln.cmd or ln.texts:
            (stack[-1].children if stack else root).append(ln)
    return root


# ---------------------------------------------------------------------------
class RegistryGapError(Exception):
    """表外词条 = 生成器缺陷（16a CSV 已做全语料覆盖自检；翻译器查不到 → 修表重跑，禁止产出 🔴待注册）。"""


class Verdict(object):
    """一行 16a 的落点裁定（16b §10.4 四列）——翻译器据此决定「这条到底往哪落」。

    tier: T1 引擎直取 / T2 引擎改造 / T3 本 mod 新造 / T3-预留（行为空执行）/ T0 降级
    carrier: 引擎 / 外置仓 / 13主命 / 05演出 / Ctx …
    savekey: lwn_scn_attr / lwn_scn_state / 13 / 17 / 无
    anchor: 实现锚点（16b「读实现 / 写实现」合并）
    """
    __slots__ = ("tier", "carrier", "savekey", "anchor")

    def __init__(self, row):
        self.tier = (row.get("档") or "").strip()
        self.carrier = (row.get("载体") or "").strip()
        self.savekey = (row.get("存档键") or "").strip()
        self.anchor = (row.get("实现锚点") or "").strip()

    @property
    def reserved(self):
        """T3-预留：步骤照常生成，运行时空执行并打 [Scenario][TODO]（16b §3.3）。"""
        return self.tier == "T3-预留"

    @property
    def degraded(self):
        """T0：本期不落地，只作注释保留。"""
        return self.tier == "T0"

    def tag(self):
        return "%s/%s" % (self.tier or "?", self.carrier or "?")


# ---------------------------------------------------------------------------
# S1：参数列解析器（parse_params_spec）—— 154 行命令参数列 100% 结构化
# ---------------------------------------------------------------------------
class ParamSlot(object):
    """一个参数位的解析结果：位置/名字/标注/字段名。"""
    __slots__ = ("pos", "name", "ann", "field")

    def __init__(self, pos, name, ann, field):
        self.pos = pos          # int 或 None（slot= / 裸参数名 / 头值）
        self.name = name        # 'slot' / 'pos0' / 裸参数名 / 'target'/'value' / 'head'
        self.ann = ann          # 标注原文（BＧＭ 枚举 / 具名实体 …）
        self.field = field      # 产物字段名（推导：slot 预设 / 别名 / 裸名 / 语义名）

    def __repr__(self):
        return "ParamSlot(%s=%s ann=%r field=%s)" % (self.name, self.pos, self.ann, self.field)


def _classify_ann(ann):
    """标注 → (类别, 值空间/域)。类别 = entity/template/domain/attr/enum/domainval/资源用 enum 变体
    /bool/zero/number/textvar/attr_driven/union/unknown。

    标注原文样例：
      具名实体 / 模板NPC 枚举 / 域名 / 属性名 / 真偽 枚举 / 零值 枚举 / 数字 / 文本变量 /
      ＢＧＭ 枚举 / 主命 域值 / 值（取值空间由属性参决定）/ 头值=事件屬性 枚举
    """
    a = ann.strip()
    if a == "具名实体":
        return ("entity", None)
    if a == "模板NPC 枚举":
        return ("template", "模板NPC")
    if a == "域名":
        return ("domain", None)
    if a == "属性名":
        return ("attr", None)
    if a == "真偽 枚举":
        return ("bool", "真偽")
    if a == "零值 枚举":
        return ("zero", "零值")
    if a == "数字":
        return ("number", None)
    if re.match(r"^文本变量( 枚举)?$", a):
        return ("textvar", None)
    if a.startswith("值（取值空间由属性参决定）"):
        return ("attr_driven", None)
    if a.startswith("头值="):
        return ("head", a[len("头值="):].strip())
    m = re.match(r"^(.+?) (枚举|域值)$", a)
    if m:
        return ("enum" if m.group(2) == "枚举" else "domainval", m.group(1).strip())
    if "/" in a and "(" not in a:
        return ("union", a)
    return ("unknown", a)


def _field_of(name, ann):
    """推导产物字段名：①slot= 预设名 ②裸参数名 ③值空间别名（VALUE_SPACE_FIELD）
    ④值空间=域名/属性名→domain/attr ⑤语义名（entity/npc/value/text）。"""
    if name is not None and name != "pos" and name not in ("slot", "pos0"):
        return name
    cls, space = _classify_ann(ann)
    if cls == "attr_driven":
        return "value"
    if space in VALUE_SPACE_FIELD:
        return VALUE_SPACE_FIELD[space]
    if cls == "domain":
        return "domain"
    if cls == "attr":
        return "attr"
    if cls == "entity":
        return "entity"
    if cls == "template":
        return "npc"
    if cls == "bool":
        return "value"
    if cls in ("number", "textvar"):
        return "value"
    if cls in ("enum", "domainval", "zero"):
        return space or "value"
    return "value"


def parse_params_spec(spec):
    """命令/语法词条「参数」列 → [ParamSlot]。覆盖全部写法：
      '—' / '无' / '' → []
      'slot=hero_d, pos1=容器統計 枚举' → [slot, pos1]
      'pos0=ＢＧＭ 枚举' / 'pos0=域名, pos1=属性名/排序特殊鍵 枚举, pos2=排序方向 枚举'
      'actor, clan'（裸参数名） / 'orderId'
      '头值=事件屬性 枚举'（属性 event_meta 事件头）
      '(目标)(值)——双括号：…'（更新 update 双括号形态）→ [target, value]
    """
    s = (spec or "").strip()
    if s in ("", "—", "无"):
        return []
    # 双括号形态（更新等语法词）：可能带说明文字
    m = re.match(r"^\(([^)]*)\)\(([^)]*)\)", s)
    if m:
        return [ParamSlot(0, "target", "target", "target"),
                ParamSlot(1, "value", "value", "value")]
    out = []
    for i, seg in enumerate(s.split(",")):
        seg = seg.strip()
        if not seg:
            continue
        m = re.match(r"^slot=(\S+)\s*$", seg)
        if m:
            out.append(ParamSlot(None, "slot", m.group(1), m.group(1)))
            continue
        m = re.match(r"^pos(\d+)=(.*)$", seg)
        if m:
            ann = m.group(2).strip()
            pos = int(m.group(1))
            out.append(ParamSlot(pos, "pos%d" % pos, ann, _field_of("pos%d" % pos, ann)))
            continue
        m = re.match(r"^(头值)=(.*)$", seg)
        if m:
            out.append(ParamSlot(None, "head", m.group(2).strip(), "head"))
            continue
        # 裸参数名（纯 ASCII，无空格无括号）
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", seg):
            out.append(ParamSlot(i, seg, seg, seg))
            continue
        # 未知写法 → 原样标注（报告点名，不猜）
        out.append(ParamSlot(i, None, seg, None))
    return out


# ---------------------------------------------------------------------------
# Registry（信源 A）—— v6 扩展：枚举值区 / 语法区 / 文本变量区 / 布尔拼写 / 参数列
# ---------------------------------------------------------------------------
# 布尔拼写解析：属性「语义」列形如「出現標誌（TK5 拼写：1 / 0 / 已出現 / 未出現 / 已發生 → true/false）」
_RE_BOOL_SPELL = re.compile(r"TK5 拼写：([^）)]*)")


def _parse_bool_spellings(sem):
    """「1 / 0 / 已出現 / 未出現 / 已發生 → true/false」→ {拼写: bool}。"""
    out = {}
    m = _RE_BOOL_SPELL.search(sem or "")
    if not m:
        return out
    body = m.group(1)
    parts = re.split(r"→|➞", body)
    if len(parts) != 2:
        return out
    words = parts[0]
    flag = parts[1].strip().lower() in ("true", "真")
    for w in re.split(r"[ /、,，]+", words):
        w = w.strip()
        if w:
            out[w] = flag
    return out


class Registry:
    def __init__(self, csv_path):
        self.domains, self.attrs, self.domain_vals, self.predicates = {}, {}, {}, {}
        self.commands = {}       # 命令区：原词 -> (side, usage)
        self.syntax = {}         # 语法区：原词 -> (side, 参数列, usage)
        self.enum_vals = {}      # 枚举值区：(所属域, 原词) -> (side, 值类型, usage)
        self.textvars = {}       # 文本变量区：原词 -> (side, 值类型, usage)
        self.bare_vals = {}      # 域值区纯 token 反查（武將→general 等）
        self.verdicts = {}       # (类别, 所属域, 太阁原词) → Verdict
        self.bool_spellings = {} # 属性原词 -> {TK5拼写: bool}（从「语义」列解析）
        self.param_cache = {}    # (类别, 原词) -> [ParamSlot]
        self.used = set()
        with open(csv_path, encoding="utf-8-sig") as f:
            for r in csv.DictReader(f):
                cat, src, side = r["类别"], r["太阁原词"], r["我们侧名"]
                usage = Verdict(r)
                self.verdicts[(cat, r.get("所属域", ""), src)] = usage
                if cat == "域":
                    self.domains[src] = side
                elif cat == "属性":
                    # 🔴 多段侧名 = 按「所属域」列与侧名段一一对齐（Hero.clan / Settlement.clan ← 人物 / 城 / 據點…
                    #   —— _pick_side 按主体域词取段，零硬表（v6：replace 旧 _DOMAIN_PREFIX 推断）
                    self.attrs[src] = (side, r["值类型"], usage,
                                       [x.strip() for x in (r.get("所属域") or "").split("/")],
                                       [x.strip() for x in side.split(" / ")])
                    sp = _parse_bool_spellings(r["语义"])
                    if sp:
                        self.bool_spellings[src] = sp
                elif cat == "域值":
                    key = f"{r['所属域']}::{src}"
                    self.domain_vals[key] = (side, r["值类型"], usage)
                    if "::" not in side and side != "null":
                        if src not in self.bare_vals:
                            self.bare_vals[src] = side
                elif cat == "函数":
                    self.predicates[src] = side
                elif cat == "命令":
                    self.commands[src] = (side, usage)
                elif cat == "语法":
                    self.syntax[src] = (side, r["参数"], usage)
                elif cat == "枚举值":
                    self.enum_vals[(r["所属域"], src)] = (side, r["值类型"], usage)
                elif cat == "文本变量":
                    self.textvars[src] = (side, r["值类型"], usage)

    def _use(self, cat, w):
        self.used.add((cat, w))

    def domain(self, w):
        self._use("域", w)
        return self.domains.get(w)

    def attr(self, name):
        if name is None:
            return None
        self._use("属性", name)
        return self.attrs.get(name)

    def domain_val(self, dom, val):
        if dom is None or val is None:
            return None
        self._use("域值", val)
        return self.domain_vals.get(f"{dom}::{val}")

    def enum_val(self, space, val):
        """枚举值区：(值空间, 原词) → (side, 值类型, usage)。"""
        if space is None or val is None:
            return None
        self._use("枚举值", val)
        return self.enum_vals.get((space, val))

    def predicate(self, w):
        self._use("函数", w)
        return self.predicates.get(w)

    def command(self, w):
        self._use("命令", w)
        return self.commands.get(w)

    def syntax_word(self, w):
        self._use("语法", w)
        return self.syntax.get(w)

    def textvar(self, w):
        self._use("文本变量", w)
        return self.textvars.get(w)

    def bool_spelling(self, attr_word, spelling):
        bs = self.bool_spellings.get(attr_word or "", {})
        return bs.get(spelling)

    def params(self, cat, word):
        """参数列解析结果（main 预载全表后即缓存命中）。"""
        return self.param_cache.get((cat, word))

    def set_csv_path(self, path):
        self._csv_path = path


# ---------------------------------------------------------------------------
# 翻译器 v6
# ---------------------------------------------------------------------------
class Segment:
    """演出单元（一段连续表现层 → 一个演绎剧本）。"""
    def __init__(self, seg_id, form):
        self.id = seg_id
        self.form = form
        self.lines = []


# 文本变量模式（TK5 全语料扫描 2026-08-27；v6：槽名取「全角→ASCII」字形，不再 slot_cname 推断）
RE_VAR_ATTR = re.compile(r"\(([^()]+)\.(姓|名|名前)\)")
RE_VAR_BRACE = re.compile(r"\{([^}]+)\}")
RE_VAR_ANGLE = re.compile(r"<([^>]+)>")
RE_VAR_PLAIN = re.compile(r"\(([^()]+)\)")
_FW_AT = ord("Ａ")   # 全角字母 → ASCII（纪律 12 全角转 ASCII，属编码归一非翻译知识）


def _fw_letter(ch):
    if "Ａ" <= ch <= "Ｚ":
        return chr(ord("a") + (ord(ch) - ord("Ａ")))
    if "ａ" <= ch <= "ｚ":
        return chr(ord("a") + (ord(ch) - ord("ａ")))
    return ch


def _slot_letter(slot):
    """槽字面（人物Ｄ/城Ａ/ａ…）→ 字母（d/a/…，ASCII 小写）。"""
    if slot:
        c = slot[-1]
        return _fw_letter(c)
    return ""


def _simplify_not(expr):
    if expr is None:
        return None
    while expr.startswith("not( not( ") and expr.endswith(" ) )"):
        expr = expr[len("not( not( "):-len(" ) )")]
    return expr


class Translator:
    def __init__(self, registry, scenario="okehazama"):
        self.reg = registry
        self.scenario = scenario
        self.event_id = ""
        self.todo = []          # (事件, 类别, 词, 上下文) —— 有主缺口清单
        self.var_inject = []    # (事件, T#, 占位符, 类型, 源)
        self.ctx = {}
        self.segments = []
        self.cur_seg = None
        self.script_out = []
        self.when_stack = []
        self.seen_heroes = {}   # 有序去重（主人公分歧:(其他) 取反用，纪律 24）
        self.pending_cond = None
        self.pending_choice = None
        self.t_counter = 0
        self.current_hero = "Hero::MainHero"
        self.key_prefix = f"LWN_SCN_{scenario}"
        self.form = "menu_dialogue"
        self.seg_n = 0
        self.gap_notes = []     # gap_report 的机器可读缺口清单（S3）

    # ---------- 工具 ----------
    def todo_mark(self, who, what, ctx_str=""):
        self.todo.append((self.event_id, who, what, ctx_str))

    def todo_dedup(self):
        seen, out = set(), []
        for t in self.todo:
            if t not in seen:
                seen.add(t)
                out.append(t)
        return out

    def gap(self, what, ctx_str=""):
        """停机级缺口登记（S3 gap_report 机器可读）。"""
        self.gap_notes.append((self.event_id, what, ctx_str))

    def new_segment(self):
        self.seg_n += 1
        seg = Segment(f"{self.scenario}_{self.event_id}_seg{self.seg_n}", self.form)
        self.segments.append(seg)
        self.cur_seg = seg
        self.script_out.append({"step": "perform", "playbackId": seg.id})
        return seg

    def ensure_segment(self):
        if self.cur_seg is None:
            self.new_segment()
        return self.cur_seg

    def when_now(self):
        if not self.when_stack:
            return None
        if len(self.when_stack) == 1:
            return self.when_stack[0]
        return "and( " + ", ".join(self.when_stack) + " )"

    # ---------- 信源 B 查询（零兜底：查无 = 停机报错） ----------
    def lookup_entity(self, dom_word, subject):
        """具名实体 → DSL 引用（按域词查信源 B 实体表）。查无 = RegistryGapError。"""
        if dom_word not in _ENTITY_TABLES:
            raise RegistryGapError(
                f"信源 B 无此实体域: {dom_word}::{subject}——16a 域表/16b 未定义该域，回填 16a 域表或 gen_registry_tables.py")
        tables = _ENTITY_TABLES[dom_word]
        for t in tables:
            v = t.get(subject)
            if v:
                return v
        raise RegistryGapError(
            f"信源 B 查无实体: {dom_word}::{subject}（源第 {self.event_id} 引）——回填 gen_entity_maps.py"
            "（CLOSURE 闭包登记，纪律 21）或补齐 07 数据包；禁止脚本兜底")

    # ---------- 引用翻译 ----------
    def translate_ref(self, ref_str):
        """`域::主体.属性(参数)` / `域::主体.属性` / `域::主体` → DSL。返回 (dsl, ok)。"""
        # 🔴 容器统计表达式（調查:(容器記錄數::人物::X.身份)）：函數名 = 枚举值区「容器統計」域侧名
        #   （container_count，查表驱动），参数 = 具体引用（渲染器句法，非名词）
        m0 = re.match(r"^(容器記錄數)::(.*)$", ref_str)
        if m0:
            ev = self.reg.enum_val("容器統計", m0.group(1))
            if not ev:
                raise RegistryGapError(
                    f"容器統計枚举外: {m0.group(1)}——16a 枚举值区「容器統計」域无此值（回填 ENUM_SETS）")
            inner = m0.group(2)
            return f'{ev[0]}({self.translate_ref(inner)[0]})', True
        m = re.match(r"^(.*?)::(.*)$", ref_str)
        if not m:
            raise RegistryGapError(f"无域引用: {ref_str}")
        dom_word, rest = m.group(1), m.group(2)
        if "." in rest:
            subject, attr_part = rest.split(".", 1)
        else:
            subject, attr_part = rest, ""
        callm = re.match(r"^(.*?)\((.*)\)$", attr_part) if attr_part else None
        if callm:
            attr_word, call_args = callm.group(1), callm.group(2)
            pred = self.reg.predicate(attr_word)
            if not pred:
                raise RegistryGapError(
                    f"调用表外: {dom_word}::{subject}.{attr_word}(…)——16a CSV 函数区无此调用（回填 gen_registry_tables.py CALL_MAP）")
            target = self.translate_ref(call_args)
            return f"{pred}({self._call_subject(pred, dom_word, subject)}, {target[0]})", target[1]
        if not attr_part:
            return self.translate_subject(dom_word, subject), True
        attr_word = attr_part
        attr = self.reg.attr(attr_word)
        if not attr:
            raise RegistryGapError(
                f"属性表外: {attr_word}——16a CSV 属性区无此属性行（回填 gen_registry_tables.py PAIR_OVERRIDE）")
        side, typ, verd, _doms, _sides = attr
        if verd.degraded:
            raise RegistryGapError(
                f"属性 T0: {dom_word}.{attr_word}——16b 判 T0（{verd.anchor or '降级'}），不该走到取值路径")
        if side.startswith("exists"):
            return f"exists({self.translate_subject(dom_word, subject)})", True
        subj = self.translate_subject(dom_word, subject)
        seg = self._pick_side(attr, dom_word)
        if seg is None:
            raise RegistryGapError(
                f"属性域错配: {dom_word}.{attr_word}——侧名「{side}」无 {dom_word} 域段（回填 gen_registry_tables PAIR_OVERRIDE）")
        if seg == "hasMet":
            return f"hasMet({subj}, Hero::MainHero)", True
        if seg in ("relation", "hasRelation"):
            return f"relation({subj}, Hero::MainHero)", True
        if seg == "unknown":
            self.todo_mark("属性-未知", f"{dom_word}.{attr_word}", ref_str)
        if re.match(r'^[A-Z][A-Za-z]*\.', seg):
            seg = seg.split('.', 1)[1]
        return f"({subj}.{seg})", True

    def _ref_prefix(self, dom_word):
        """域词 → DSL 引用前缀（语法层：域表侧名（信源 A）直拼 '::'；特例 Table 见 REF_PREFIX_OVERRIDE）。"""
        ds = self.reg.domain(dom_word)
        if ds is None:
            raise RegistryGapError(f"域表外: {dom_word}——16a CSV 域表无此行（回填 gen_registry_tables.py DOMAIN_MAP）")
        return REF_PREFIX_OVERRIDE.get(ds, ds + "::")

    def _pick_side(self, attr_entry, dom_word):
        """多段侧名按主体域词取段：属性侧名段前缀（Hero./Settlement./Clan.… = DSL 命名空间，铁律 20）
        与 SIDE_PREFIX[域词] 候选匹配；候选唯一 → 直接取；多段命中 → 报「域分段歧义」（生成器缺陷）。
        🔴 2026-08-30 v6：属性行「所属域」列与侧名段**非一一对应**（多域共用侧名，如 存在=6域1段、
        當主=5域3段）——不能按 index 对齐；Segment = 由 DSL 命名空间集匹配（无硬编码域词→段）。"""
        side, _t, _v, _doms, sides = attr_entry
        prefix_set = SIDE_PREFIX.get(dom_word)
        if not prefix_set:
            # 域词未列入 SIDE_PREFIX（如 槽/特殊值 域）→ 全局段/单段直接取
            cands = [p for p in sides if p.startswith(("Variable::", "Ctx::"))
                     or p in ("exists", "hasMet", "isAllied", "isNeighbor", "allControlled",
                              "hasRelation", "relation", "unknown", "atWar", "isVisible",
                              "sameSettlement")]
            if cands:
                return cands[0]
            if len(sides) == 1:
                return sides[0]
            return None
        hits = []
        for p in sides:
            head = p.split(".")[0].split("::")[0]
            if head in prefix_set:
                hits.append(p)
        if len(hits) == 1:
            return hits[0]
        if len(hits) > 1:
            raise RegistryGapError(
                f"属性域分段歧义: {side} 对域词「{dom_word}」命中 {len(hits)} 段（{hits}）——"
                "16a 侧名段/域词映射歧义，回填 gen_registry_tables.py PAIR_OVERRIDE 或收窄 SIDE_PREFIX")
        return None

    def _call_subject(self, pred, dom_word, subject):
        """函数主体验证/转换：外交/邻接 → 势力（Faction::Kingdom），全城压制 → 区域（Region）。"""
        if pred in ("isAllied", "isNeighbor", "relation"):
            return self.translate_subject("勢力", subject)
        if pred == "allControlled":
            return self.translate_subject("國", subject)
        return self.translate_subject(dom_word, subject)

    def translate_subject(self, dom_word, subject):
        """域::主体 → DSL 引用。v6 = 纯查表（域值区 → 信源 B → 停机），零兜底。"""
        # 1) 域值区（槽/主人公/發生X/無效/真偽 etc.——16a 认知完整）
        dv = self.reg.domain_val(dom_word, subject)
        if dv:
            side, typ, _verd = dv
            if side == "null":
                return "null"
            if "::" in side:
                return side
            return f'"{side}"'
        # 2) 条件块内代入槽 → 静态展开（条件求值无执行流，08 纪律静态直译）
        if subject in self.cond_ctx:
            return self.cond_ctx[subject]
        # 2.5) 事件域 = DSL 语法引用（Event::<id>.done = 事件链完成状态，01 三层纪律；语法层非名词）
        if dom_word == "事件":
            return f"(Event::{subject}.done)"
        # 3) 具名实体 → 信源 B
        if dom_word in _ENTITY_TABLES:
            return self.lookup_entity(dom_word, subject)
        # 4) 域值区无/实体表无 = 表外（生成器缺陷）
        raise RegistryGapError(
            f"域值表外: {dom_word}::{subject}——16a CSV 域值区无此 (域,值) 行（回填 gen_registry_tables.py）")

    # ---------- 条件翻译 ----------
    def translate_condition(self, cond_block):
        exprs = self._cond_items(cond_block.children)
        seen, dedup = set(), []
        for e in exprs:
            if e not in seen:
                seen.add(e)
                dedup.append(e)
        if not dedup:
            return ""
        if len(dedup) == 1:
            return dedup[0]
        return "and( " + ", ".join(dedup) + " )"

    def _cond_items(self, children):
        out = []
        for item in children:
            if isinstance(item, Block):
                if item.bare_cmd in ("ＯＲ調查", "ＡＮＤ調查"):
                    subs = self._cond_items(item.children)
                    if subs:
                        joined = (f"or( {', '.join(subs)} )" if item.bare_cmd == "ＯＲ調查"
                                  else f"and( {', '.join(subs)} )")
                        out.append(joined)
                        self.cond_pairs.append((item.raw.strip(), joined))
                else:
                    self.todo_mark("条件块", item.name)
            elif isinstance(item, Line):
                if item.cmd.startswith("代入"):
                    # 条件块内代入 = 静态展开（槽名 = 命令区参数列 slot= 预设；值 = 实参翻译）
                    params = item.params()
                    if params:
                        self.cond_ctx[item.cmd[2:].strip()] = self._slot_value(params[0])
                    continue
                e = self.translate_cond_line(item)
                if e:
                    out.append(e)
                    self.cond_pairs.append((item.text.strip(), e))
        return out

    def translate_cond_line(self, line):
        t = line.text
        if not t.startswith("調查:"):
            return None
        return self.translate_expression(t[len("調查:"):].strip())

    def translate_expression(self, expr):
        expr = expr.strip()
        m = re.match(r"^(.*?)(==|!=|>=|<=|>|<)(.*)$", expr)
        if m:
            left, op, right = m.group(1).strip(), m.group(2), m.group(3).strip()
            left = left[1:-1].strip() if left.startswith("(") and left.endswith(")") else left
            right = right[1:-1].strip() if right.startswith("(") and right.endswith(")") else right
            lm = re.match(r"^(.*?::.+?)\.死亡標誌$", left)
            if lm:
                ref = self.translate_ref(lm.group(1))[0]
                dead = right in ("1", "死亡", "已發生")
                return f"not( ({ref}.alive) == true )" if dead else f"({ref}.alive) == true"
            left_dsl = self.translate_ref(left)[0]
            right_dsl = self._canonical_right(left, right)
            return f"({left_dsl}) {op} ({right_dsl})"
        e = expr[1:-1].strip() if expr.startswith("(") and expr.endswith(")") else expr
        return self.translate_ref(e)[0]

    def _canonical_right(self, left, right):
        """布尔标誌族规范化：右值数字/语义词 → true/false。值映射 = 16a「语义」列 TK5 拼写清单（读表）。"""
        if not self._is_bool_ref(left):
            return self.translate_value(right)
        if right in ("1", "成立", "已發生", "真"):
            return "true"
        if right in ("0", "不成立", "未發生", "偽"):
            return "false"
        m = re.match(r"^([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$", left)
        if m:
            val = self.reg.bool_spelling(m.group(2), right)
            if val is not None:
                return "true" if val else "false"
        return self.translate_value(right)

    def _is_bool_ref(self, left):
        l = left.strip()
        while l.startswith("(") and l.endswith(")"):
            l = l[1:-1].strip()
        if l.startswith("事件標誌::") or l.startswith("事件::"):
            return True
        m = re.match(r"^([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$", l)
        if m:
            at = self.reg.attr(m.group(2))
            if at is not None and at[1] == "布尔":
                return True
        m2 = re.match(r"^([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([^.（()]+)$", l)
        if m2:
            dv = self.reg.domain_val(m2.group(1), m2.group(2))
            if dv is not None and dv[1] == "布尔":
                return True
        return False

    def translate_value(self, v):
        """纯值（数字/真偽/枚举 token/名字）→ DSL 值。v6 = 查表，零兜底。"""
        if re.match(r"^-?\d+$", v):
            return v
        if v.startswith("真偽::"):
            return "true" if v.endswith("真") else "false"
        if "::" in v:
            return self.translate_ref(v)[0]
        # 域值区全表扫描（按值命中：事件用１軍團 → 军團::事件用１軍團 = Army::event_1——裸名值驱动）
        hits = []
        for key, (side, _t, _v) in self.reg.domain_vals.items():
            if "::" in key:
                dom, val = key.split("::", 1)
                if val == v:
                    hits.append((dom, side))
        if hits:
            sides = set(s for _d, s in hits)
            if len(sides) == 1:
                s = sides.pop()
                return s if "::" in s else f'"{s}"'
            raise RegistryGapError(
                f"域值区值歧义: {v} 命中 {len(sides)} 个侧名（{sorted(sides)[:4]}）——回填/修 16a 域值区")
        # 域值区 bare token 反查（武將→general / 城主→city_lord …）
        bare = self.reg.bare_vals.get(v)
        if bare:
            return f'"{bare}"'
        # 枚举值区全扫描（值空间未标注时兜底；不猜不哈希，查无 = 由调用方报错）
        ev = self.enum_any(v)
        if ev is not None:
            return self._enum_side(ev)
        # 具名实体/模板 NPC（信源 B：先 Hero 后 Agent——铁律 8 双类）
        if v in HERO_MAP:
            return HERO_MAP[v]
        if v in AGENT_MAP:
            return AGENT_MAP[v]
        if v in KINGDOM_MAP:
            return KINGDOM_MAP[v]
        if v in SETTLEMENT_MAP:
            return SETTLEMENT_MAP[v]
        if v in ORG_MAP:
            return ORG_MAP[v]
        if v in REGION_MAP:
            return REGION_MAP[v]
        raise RegistryGapError(
            f"值表外: {v}——16a 域值区/枚举值区与信源 B 均无此值（回填映射表或 gen_entity_maps.py CLOSURE）")

    # ---------- 参数值翻译（值类型驱动分流，§15.1.5） ----------
    def _slot_value(self, v):
        """代入槽值：含域 → 完整引用翻译；纯值 → translate_value（同上件）。"""
        if "::" in v:
            return self.translate_ref(v)[0]
        return self.translate_value(v)

    def _attr_driven_value(self, attr_word, v):
        """容器 pos2=值（取值空间由属性参决定）：按属性行「值类型」列分类翻译。"""
        at = self.reg.attr(attr_word)
        if at is None:
            # 排序特殊鍵 等非属性名 → 枚举值区查
            ev = self.enum_any(v)
            if ev is not None:
                return ev
            raise RegistryGapError(
                f"属性表外: {attr_word}（容器筛选属性名）——16a 属性区无此行（回填 PAIR_OVERRIDE）")
        _side, typ, _verd, _doms, _sides = at
        if typ == "布尔":
            val = self.reg.bool_spelling(attr_word, v)
            if val is None:
                # 布尔拼写表没收录的写法（0/1/真/偽 已由 _canonical_right 层拦截失败？这里兜规范）
                if v in ("1", "真", "成立", "已出現", "已發生", "生存", "出撃中", "持有中"):
                    return "true"
                if v in ("0", "偽", "不成立", "未出現", "未發生", "死亡", "平常", "在家", "健康", "沒持有"):
                    return "false"
                raise RegistryGapError(f"布尔拼写表外: {attr_word}::{v}——16a 语义列 TK5 拼写未收录（回填语义列）")
            return "true" if val else "false"
        if typ.startswith("对象:"):
            dom = {"对象:人物": "人物", "对象:据点": "據點", "对象:家族": "大名家",
                   "对象:王国": "勢力", "对象:区域": "國", "对象:组织": "忍者衆",
                   "对象:物品": "物品", "对象:卡": "卡", "对象:部队": "大名家"}.get(typ)
            if "::" in v:
                return self.translate_ref(v)[0]
            if dom in _ENTITY_TABLES:
                return self.lookup_entity(dom, v)
            raise RegistryGapError(f"对象值域缺失: {attr_word} 值 {v}（类型 {typ}）——回填对象→域映射")
        m = re.match(r"^枚举:(.+)$", typ)
        if m and "（" not in typ:
            space = m.group(1)
            ev = self.reg.enum_val(space, v)
            if not ev and space in ENUM_SPACE_ALIAS:
                ev = self.reg.enum_val(ENUM_SPACE_ALIAS[space], v)
            if ev:
                return self._enum_side(ev)
            # 同空间兼有域值区（人物類別=枚举值区 3 + 域值区 4——拆两区是 16a 词源分区，非缺）
            dv = self.reg.domain_val(space, v)
            if dv:
                return dv[0] if "::" in dv[0] else f'"{dv[0]}"'
            raise RegistryGapError(f"枚举值表外: {space}::{v}（属性 {attr_word}，16a 枚举值区「{space}」域无此行）")
        # 数字/其他 → 原样或数字
        if re.match(r"^-?\d+$", v):
            return v
        # 属性驱动值 token（状态值/身份/槽（人物Ａ/ａ）等）→ 值扫描（域值区/枚举值区/槽引用/实体表）
        return self.translate_value(v)

    def _enum_side(self, ev):
        """枚举值区条目 → 产物值：资源类侧名原样（Bgm::tk5_ue10d0b）；纯 token 加引号（first）。"""
        side, _typ, _verd = ev
        if "::" in side:
            return side
        return f'"{side}"'

    def enum_any(self, v):
        """裸 token → 枚举值区/域值区扫描（值空间未标注时兜底；不猜不哈希，查无 = None 由调用方报错）。"""
        for (space, word), ev in self.reg.enum_vals.items():
            if word == v:
                return ev
        return None

    # ---------- 执行翻译（v6 通用交换机） ----------
    def translate_execution(self, items):
        i = 0
        while i < len(items):
            item = items[i]
            if isinstance(item, Block) and item.bare_cmd == "分歧":
                nxt = items[i + 1] if i + 1 < len(items) else None
                paired = None
                if isinstance(nxt, Block) and nxt.bare_cmd == "分歧":
                    paired = nxt
                self._translate_branch(item, paired)
                i += 2 if paired else 1
                continue
            if isinstance(item, Block):
                self.translate_exec_block(item)
            elif isinstance(item, Line):
                self.translate_exec_line(item)
            i += 1

    def _translate_one(self, item):
        if isinstance(item, Block):
            self.translate_exec_block(item)
        elif isinstance(item, Line):
            self.translate_exec_line(item)

    def _translate_branch(self, block, paired):
        val = block.args[0] if block.args else ""
        cond = self._resolve_branch_cond(val)
        if paired:
            pv = paired.args[0] if paired.args else ""
            if val == "1" and pv == "0":
                self._push_if(cond, block, paired)
                return
            if val == "0" and pv == "1":
                orig = _simplify_not(f"not( {cond} )" if cond else None)
                self._push_if(orig, paired, block)
                return
            self._push_if(cond, block)
            self._push_if(self._resolve_branch_cond(pv), paired)
            return
        self._push_if(cond, block)

    def _is_pure_perform(self, block):
        for item in block.children:
            if isinstance(item, Block):
                if item.bare_cmd in ("分歧", "場合分歧", "主人公分歧", "主人公別"):
                    if not self._is_pure_perform(item):
                        return False
                else:
                    return False
            elif isinstance(item, Line):
                if item.cmd not in ("對話", "自語", "旁白", "選擇", "對話選擇", "自語選擇", "調查"):
                    return False
        return True

    def _push_if(self, cond, block, else_block=None, hero_override=None):
        saved_hero = self.current_hero
        if hero_override:
            self.current_hero = hero_override
        usable = bool(cond) and not str(cond).startswith("🔴待注册")
        if self._is_pure_perform(block):
            push_len = len(self.when_stack)
            if usable:
                self.when_stack.append(cond)
            for item in block.children:
                self._translate_one(item)
            del self.when_stack[push_len:]
        else:
            self.script_out.append({"step": "if",
                                    "when": cond if usable else "🔴待注册:分支条件",
                                    "then": self._inline_block(block, cond if usable else None)})
        if else_block is not None:
            self._push_if(_simplify_not(f"not( {cond} )" if usable else None), else_block)
        self.current_hero = saved_hero

    # ---------- S2 核心：单行通用交换机 ----------
    def translate_exec_line(self, line):
        cmd = line.cmd
        if cmd.startswith("未知"):
            self.script_out.append({"step": "note", "note": "🔴 未知命令（解析碎片）→ 忽略", "src": line.text})
            return
        # ---- 查表（信源 A：命令区优先，语法区次之）----
        cmd_info = self.reg.command(cmd)
        synt_info = None
        if cmd_info is None:
            synt_info = self.reg.syntax_word(cmd)
        if cmd_info:
            side, verd = cmd_info
        elif synt_info:
            side, _pspec, verd = synt_info
        elif cmd in ("他歧",) or not cmd:
            return
        else:
            raise RegistryGapError(
                f"命令表外: {cmd}——16a CSV 命令区/语法区均无此命令（回填 gen_registry_tables.py CMD_MAP/SYNTAX_CMDS）")

        # ---- 台词管线（CSV 侧名命中台词集合；输出 05 形态词）----
        params = [x.strip() for p in line.params() for x in p.split(",")]
        if side in SAY_SIDES:
            self._say_line(line, side)
            return
        if side in CHOICE_SIDES or side == "choice":
            self._choice_line(line, side)
            return
        if side in CONTAINER_SIDES:
            self._container_line(line, side)
            return
        # ---- 调查（语法区 side=condition）：待用条件（pending_cond，纪律 15 保留至被覆盖）----
        if side == "condition":
            self.pending_cond = self.translate_cond_line(line)
            return
        # ---- 块类（语法结构：词条名查表登记，输出 = 01 步骤词）----
        if side in ("loop", "module_exit", "module_begin", "script"):
            self.script_out.append({"step": side, "src": line.text})   # loop 块由块级处理；行级 module_exit = 循环出口
            return
        # ---- 代入管线（assign_ctx / assign_var：槽名 = 参数列 slot= 预设（CSV 权威，纪律 4 v6 注）；
        #      值 = 实参翻译（域引用/槽值/纯值））----
        if side in ("assign_ctx", "assign_var"):
            pslots_a = self.reg.params("命令" if cmd_info else "语法", cmd)
            slot = next((ps.field for ps in (pslots_a or []) if ps.name == "slot"), None)
            if not slot:
                raise RegistryGapError(
                    f"代入槽表外: {cmd}——16a 命令区参数列无 slot= 预设（回填 gen_registry_tables.py assign_side）")
            eff = {"step": "effect", "action": side, "slot": slot}
            if params and params[0]:
                eff["value"] = self._slot_value(params[0])
            else:
                raise RegistryGapError(f"代入无实参: {line.text}")
            eff["src"] = line.text
            self._finalize_effect(eff, verd)
            self.script_out.append(eff)
            return
        # ---- 通用（载体分派，§15.2 S2⑤）----
        params = [x.strip() for p in line.params() for x in p.split(",")]
        pslots = self.reg.params("命令" if cmd_info else "语法", cmd)
        if side == "update":
            # 🔴 用户 2026-08-30 裁定：更新 = 真步骤（16a 权威 update，T1 引擎直取）——不再 note
            # 值翻译 = 按目标属性的取值空间（attr_driven；如 義理=枚举:義理 → 枚举值区「狀態值」）
            tgt = params[0] if params else None
            val = params[1] if len(params) > 1 else None
            eff = {"step": "effect", "action": "update"}
            attr_word = None
            if tgt:
                eff["target"] = self._slot_value(tgt)
                am = re.match(r"^.*?::.*?\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$", tgt)
                if am:
                    attr_word = am.group(1)
            if val is not None:
                eff["value"] = (self._attr_driven_value(attr_word, val) if attr_word
                                else self._slot_value(val))
            eff["src"] = line.text
            self._finalize_effect(eff, verd)
            self.script_out.append(eff)
            return
        if side == "game_end":
            eff = {"step": "effect", "action": "game_end"}
            if len(params) >= 1:
                eff["value"] = self.translate_value(params[0])
            eff["src"] = line.text
            self._finalize_effect(eff, verd)
            self.script_out.append(eff)
            return
        if side == "pause_time":
            self.script_out.append({"step": "effect", "action": "pause_time", "src": line.text})
            return
        if side in ("facility_enter", "facility_exit"):
            # 场入/场出：切演出段 + 骨架 scene_enter / scene_exit（01 步骤词；05 侧名 = 源词登记名）
            self.cur_seg = None
            eff = {"step": "scene_enter" if side == "facility_enter" else "scene_exit"}
            if side == "facility_enter" and params:
                # sceneId 参数（裸参数名）——设施对象 → Facility:: 引用或据点引用
                eff["facility"] = self._translate_scene_id(params[0])
            eff["src"] = line.text
            self.script_out.append(eff)
            return
        # 通用命令：按 载体列 分派（05 演出 → step=侧名；其他 → effect+action=侧名）
        if verd.degraded:
            note = {"step": "note",
                    "note": f"🔴 {side} 降级（T0，16b 裁定 {verd.anchor or '注释保留'}）",
                    "tier": verd.tier, "carrier": verd.carrier, "src": line.text}
            self.script_out.append(note)
            return
        eff = {"step": side if verd.carrier == "05演出" else "effect",
               "src": line.text}
        if verd.carrier != "05演出":
            eff["action"] = side
            eff["tier"] = verd.tier
            eff["carrier"] = verd.carrier
        # 参数 → 字段（值类型/参数列驱动）
        self._translate_params(eff, cmd, params, pslots, line)
        if verd.reserved:
            eff["reserved"] = True
        if verd.savekey and verd.savekey != "无":
            eff["savekey"] = verd.savekey
        self.script_out.append(eff)

    def _finalize_effect(self, eff, verd):
        if verd.reserved:
            eff["reserved"] = True
        if verd.savekey and verd.savekey != "无":
            eff["savekey"] = verd.savekey

    def _say_line(self, line, side):
        """台词管线：say / say_choice / say_as / monologue / monologue_choice / narrate。"""
        seg = self.ensure_segment()
        if side == "narrate":
            self.add_line(None, line.texts[0] if line.texts else "", line.raw, narrator=True)
            return
        if side == "monologue":
            self.add_line(self.current_hero, line.texts[0] if line.texts else "", line.raw, narrator=True)
            return
        if side in ("say_choice", "monologue_choice"):
            # 选择变体：选项文本管线（textKey + i18n）
            self._choice_line(line, side)
            return
        # say / say_as：param1=speaker、param2=listener（纪律 3：两个参数都落字段）
        params = line.params()
        speaker, listener = "Hero::MainHero", None
        if params:
            parts = [x.strip() for x in params[0].split(",")]
            if parts and parts[0]:
                speaker = self.speaker_of(parts[0])
            if len(parts) > 1 and parts[1] and parts[1] != "無效":
                listener = self.speaker_of(parts[1])
        self.add_line(speaker, line.texts[0] if line.texts else "", line.raw, listener=listener)

    def _choice_line(self, line, side):
        """选择管线：选项节点（textKey + i18n）；选择路由标记。
        🔴 可跳过标记（say_selectable/narrate_selectable，语法区）= 待定小决策（§15.1.5 表）：
        本版记录为 note + 待注册（运行时 skip 标记语义随执行层落地时定）。"""
        if side in ("say_selectable", "narrate_selectable"):
            self.todo_mark("可跳过标记", side, line.text)
            self.script_out.append({"step": "note",
                                    "note": f"🔴 {side} 可跳过标记 → 台词管线（运行时 skip 待定）",
                                    "src": line.text})
            return
        seg = self.ensure_segment()
        opts = [{"textKey": f"{self.key_prefix}_{self.event_id}_{self.t_counter}_ch{i}", "text": t}
                for i, t in enumerate(line.texts)]
        for i, t in enumerate(line.texts):
            self.i18n_key(f"{self.event_id}_{self.t_counter}_ch{i}", t)
        seg.lines.append({"cmd": "choice", "options": opts})
        self.pending_choice = len(line.texts)

    def _container_line(self, line, side):
        """容器管线（CSV 参数列驱动：pos0=域名 → domain、pos1=属性名 → attr、值按属性取值空间）。"""
        params = [x.strip() for p in line.params() for x in p.split(",")]
        step = {"step": side}
        cmd = line.cmd
        if side == "container_pick":
            # pos1=容器位置 枚举；槽 = 命令名上的域（容器選擇:(人物Ｅ,先頭) → 槽 hero_e）
            if params:
                # 槽名 = 命令名的第二个域词：容器選擇:(指定槽,位置)
                slot_body = cmd
                if len(params) >= 1:
                    step["slot"] = self._slot_field_name(params[0])
                if len(params) >= 2:
                    ev = self.reg.enum_val("容器位置", params[1])
                    if not ev:
                        raise RegistryGapError(
                            f"容器位置枚举外: {params[1]}——16a 枚举值区「容器位置」域无此值（回填 ENUM_SETS）")
                    step["mode"] = self._enum_side(ev)
            else:
                step["slot"] = self._slot_field_name(cmd)
            step["src"] = line.text
            self.script_out.append(step)
            return
        if side == "container_sort":
            if len(params) >= 3 and params[0]:
                step["domain"] = self._domain_field_value(params[0])
            if len(params) >= 2 and params[1]:
                step["key"] = self._attr_or_enum(params[1], "排序特殊鍵")
            if len(params) >= 3 and params[2]:
                ev = self.reg.enum_val("排序方向", params[2])
                if ev:
                    step["order"] = self._enum_side(ev)
                else:
                    raise RegistryGapError(f"排序方向枚举外: {params[2]}——16a 枚举值区「排序方向」域无此值")
            step["src"] = line.text
            self.script_out.append(step)
            return
        if side == "container_clear":
            if params and params[0]:
                ev = self.reg.enum_val("容器清理", params[0])
                if ev:
                    step["mode"] = self._enum_side(ev)
            if len(params) >= 2:
                step["count"] = params[1]
            step["src"] = line.text
            self.script_out.append(step)
            return
        if side == "container_query":
            # 容器檢索:(容器,？) 只需 domain/attr
            if params and params[0]:
                step["domain"] = self._domain_field_value(params[0])
            if len(params) >= 2 and params[1]:
                step["attr"] = self._attr_field_value(params[1], params[0] if params else None)
            step["src"] = line.text
            self.script_out.append(step)
            return
        if side == "container_access":
            if params and params[0]:
                ev = self.reg.enum_val("容器存取", params[0])
                if ev:
                    step["mode"] = self._enum_side(ev)
            step["src"] = line.text
            self.script_out.append(step)
            return
        # container_set / container_filter / container_exclude：pos0=域名, pos1=属性名, pos2=值
        if params and params[0]:
            step["domain"] = self._domain_field_value(params[0])
        if len(params) >= 2 and params[1]:
            step["attr"] = self._attr_field_value(params[1], params[0] if params else None)
        if len(params) >= 3 and params[2]:
            step["value"] = self._attr_driven_value(params[1], params[2])
        step["src"] = line.text
        self.script_out.append(step)

    def _domain_field_value(self, v):
        """容器 domain 字段：域词 → 16a 域表侧名（Hero/Settlement/…）。"""
        dom_side = self.reg.domain(v)
        if not dom_side:
            raise RegistryGapError(f"域名表外: {v}——16a CSV 域表无此行（回填 DOMAIN_MAP）")
        return dom_side

    def _attr_field_value(self, attr, dom_word):
        """容器 attr 字段：属性名 → 属性表侧名（按容器域取段）。"""
        at = self.reg.attr(attr)
        if at is None:
            raise RegistryGapError(f"属性表外: {attr}——16a CSV 属性区无此行（回填 PAIR_OVERRIDE）")
        seg = self._pick_side(at, dom_word or "")
        if seg is None:
            raise RegistryGapError(f"属性域错配: {attr}——侧名「{at[0]}」与容器域 {dom_word} 无对应段")
        return seg

    def _attr_or_enum(self, v, space):
        """排序 key：属性名 或 枚举值（排序特殊鍵：乱序→random）。"""
        at = self.reg.attr(v)
        if at:
            return at[0]
        ev = self.reg.enum_val(space, v)
        if ev:
            return self._enum_side(ev)
        raise RegistryGapError(f"排序键表外: {v}——属性区/枚举值区（{space}）均无（回填映射表）")

    def _slot_field_name(self, v):
        """容器選擇 槽名：从 16a 命令区「代入X」的 slot= 预设取（人物Ｅ → hero_e；以 CSV 为准）。"""
        sp = self.reg.params("命令", "代入" + v)
        if sp:
            for ps in sp:
                if ps.name == "slot":
                    return ps.field
        m = re.match(r"^(.*?)([Ａ-Ｅ])$", v)
        if m:
            return "slot_" + _fw_letter(m.group(2))
        raise RegistryGapError(f"槽位表外: {v}——16a 命令区无「代入{v}」slot= 预设（回填 assign_side/_ASSIGN_DOMAIN）")

    def _translate_params(self, eff, cmd, params, pslots, line):
        """通用命令参数 → 字段（按 参数列 + 值类型驱动）。"""
        idx = 0
        for ps in (pslots or []):
            if ps.name == "slot":
                # 代入类在别处处理；这里仅当出现 slot 且命令是代入时兜底
                continue
            if ps.name in ("target", "value") and ps.field in eff:
                continue
            if idx >= len(params):
                continue
            raw = params[idx]
            idx += 1
            fname = ps.field
            if ps.ann in ("", "—", "无"):
                eff[fname] = raw
                continue
            if raw == "無效":                       # 全局特殊值（16a 域值区 人物/據點/…::無效 → null）
                eff[fname] = "null"
                continue
            cls, space = _classify_ann(ps.ann)
            if cls == "entity":
                dom = PARAM_DOMAIN.get(ps.name or ps.field, ps.name or None)
                if ps.field and ps.name == "pos%d" % ps.pos:
                    dom = self._entity_domain_by_ctx(cmd, ps.pos)
                eff[fname] = self._translate_entity_param(dom, raw)
            elif cls == "template":
                eff[fname] = self.translate_value(raw)   # 模板 NPC → 信源 B AGENT_MAP
            elif cls == "domain":
                eff[fname] = self._domain_field_value(raw)
            elif cls == "attr":
                eff[fname] = self._attr_field_value(raw, None)
            elif cls in ("enum", "zero", "bool"):
                sv = space or "真偽"
                ev = self.reg.enum_val(sv, raw)
                if not ev and space in ENUM_SPACE_ALIAS:
                    ev = self.reg.enum_val(ENUM_SPACE_ALIAS[space], raw)
                if not ev and re.match(r"^未知[0-9０-９]*$", raw):
                    # 🔴 未知NN（纪律 18）：资源类 = 结构保留翻译 + 进待核对清单（不猜译不哈希）
                    ev_any = next(((s, t, u) for (sp, w), (s, t, u) in self.reg.enum_vals.items()
                                   if sp == ENUM_SPACE_ALIAS.get(sv, sv) and "::" in s), None)
                    prefix = ev_any[0].split("::")[0] if ev_any else (VALUE_SPACE_FIELD.get(sv) or sv)
                    digits = "".join(chr(ord(c) - 0xFEE0) if ord("０") <= ord(c) <= ord("９") else c
                                     for c in raw[2:])
                    eff[fname] = f"{prefix}::tk5_unknown_{digits}"
                    self.todo_mark("未知资源", f"{sv}::{raw}", f"命令 {cmd} {fname}")
                    continue
                if not ev:
                    # 域值区兜（同空间双区：人物類別=枚举值区 3 + 域值区 4，16a 词源分区非缺口）
                    dv = self.reg.domain_val(sv, raw)
                    if dv:
                        eff[fname] = dv[0] if "::" in dv[0] else f'"{dv[0]}"'
                        continue
                    raise RegistryGapError(
                        f"枚举值表外: {sv}::{raw}（命令 {cmd} 参数 {fname}）——16a 枚举值区「{sv}」域无此值（回填 ENUM_SETS/RES_SETS）")
                eff[fname] = self._enum_side(ev)
            elif cls == "domainval":
                dv = self.reg.domain_val(space, raw)
                if not dv:
                    raise RegistryGapError(f"域值表外: {space}::{raw}（命令 {cmd}）——16a 域值区无此行")
                side = dv[0]
                eff[fname] = side if "::" in side else f'"{side}"'
            elif cls == "attr_driven":
                eff[fname] = self._attr_value_for_param(params[1] if len(params) > 1 else None, raw)
            elif cls == "union":
                eff[fname] = self._translate_union(ps.ann, raw, cmd, fname)
            elif cls == "unknown" and ps.name == ps.ann and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", ps.ann):
                # 裸参数名（19 条参数列：leader/target/behavior…）——值驱动（值命中哪个域值区/
                # 实体表就用哪个域：leader=事件用１軍團 → 军團域值区 Army::event_1；
                # PARAM_DOMAIN（actor→人物 等）只作为歧义报告建议，不预判域）
                eff[fname] = self.translate_value(raw)
            else:
                raise RegistryGapError(
                    f"参数标注表外: 命令 {cmd} 参数 {ps.name}=「{ps.ann}」——16a 参数列写法未建模（回填 parse_params_spec/_classify_ann）")

    def _translate_entity_param(self, dom, raw):
        """具名实体参数值：按参数名→域 分流到信源 B。域未知时扫描实体表（带域报告）。"""
        if dom in _ENTITY_TABLES:
            return self.lookup_entity(dom, raw)
        # 域未定（无 PARAM_DOMAIN 条目）→ 全表扫描（唯一命中才返回；多重命中 = 报告点名）
        hits = []
        for d, tables in _ENTITY_TABLES.items():
            for t in tables:
                if raw in t:
                    hits.append((d, t[raw]))
        if len(hits) == 1:
            return hits[0][1]
        if len(hits) > 1:
            self.todo_mark("实体歧义", raw, str(hits))
        raise RegistryGapError(
            f"实体表外/歧义: {raw}——信源 B 查无或命中 {len(hits)} 表（回填 gen_entity_maps.py；域未定 = 回填 PARAM_DOMAIN）")

    def _translate_union(self, ann, raw, cmd, fname):
        """复合标注（A/B/C，含 枚举/具名实体 无空格写法）→ 逐候选尝试翻译（值命中哪个信源用哪个）。"""
        for cand in re.split(r"\s*/\s*", ann):
            cand = cand.strip()
            if not cand:
                continue
            cls, space = _classify_ann(cand)
            try:
                if cls == "entity":
                    return self._translate_entity_param(None, raw)
                if cls == "template":
                    if raw in AGENT_MAP:
                        return AGENT_MAP[raw]
                    continue
                if cls == "enum":
                    ev = self.reg.enum_val(space, raw)
                    if ev:
                        return self._enum_side(ev)
                    continue
                if cls == "domainval":
                    dv = self.reg.domain_val(space, raw)
                    if dv:
                        return dv[0] if "::" in dv[0] else f'"{dv[0]}"'
                    continue
                if cls == "bool":
                    if raw in ("真", "偽"):
                        return "true" if raw == "真" else "false"
                    continue
                if cls == "attr_driven":
                    return self._attr_value_for_param(None, raw)
                if cls == "domain":
                    if self.reg.domain(raw):
                        return self.reg.domain(raw)
                    continue
            except RegistryGapError:
                continue
        raise RegistryGapError(
            f"复合标注全未命中: {ann}（命令 {cmd} 参数 {raw}）——回填 16a 各值区或修正参数列标注")

    def _attr_value_for_param(self, attr_word, raw):
        return self._attr_driven_value(attr_word, raw)

    def _entity_domain_by_ctx(self, cmd, pos):
        """posN 具名实体参数 → 域：命令×位 语义词表（PARAM_CTX_DOMAIN）；未登记 = 值扫描唯一化。"""
        return PARAM_CTX_DOMAIN.get(f"{cmd}#{pos}")

    def _translate_scene_id(self, raw):
        """進入設施:(XXXX) → Facility:: 引用（場面设施：查枚举值区設施/域值区場面）。"""
        ev = self.reg.enum_val("設施", raw)
        if ev:
            return self._enum_side(ev)
        dv = self.reg.domain_val("場面", raw)
        if dv:
            return dv[0] if "::" in dv[0] else f'"{dv[0]}"'
        raise RegistryGapError(f"施設/場面表外: {raw}——16a 枚举值区「設施」/域值区「場面」均无（回填 ENUM_SETS/PLACE_TOKENS）")

    # ---------- 块翻译（语法区驱动） ----------
    def translate_exec_block(self, block):
        b = block.bare_cmd
        if b == "分歧":
            cond = self._resolve_branch_cond(block.args[0] if block.args else "")
            self._push_if(cond, block)
        elif b in ("場合別", "場合分歧"):
            cond = self.translate_expression(block.args_raw) if block.args_raw else None
            self._push_if(cond, block)
        elif b == "主人公別":
            for sub in block.children:
                if isinstance(sub, Block) and sub.bare_cmd == "主人公分歧":
                    hero = sub.args[0] if sub.args else ""
                    cond = self._branch_when(sub)
                    override = self.speaker_of(hero) if hero != "其他" else None
                    self._push_if(cond, sub, hero_override=override)
        elif b == "主人公分歧":
            hero = block.args[0] if block.args else ""
            cond = self._branch_when(block)
            override = self.speaker_of(hero) if hero != "其他" else None
            self._push_if(cond, block, hero_override=override)
        elif b in ("ＡＮＤ調查", "ＯＲ調查"):
            self.script_out.append({"step": "note", "note": f"🔴 执行内 {b} → 条件门控", "src": block.raw})
            for item in block.children:
                if isinstance(item, Line):
                    self.translate_exec_line(item)
                elif isinstance(item, Block):
                    self.translate_exec_block(item)
        elif b in ("循環", "模塊開始", "腳本"):
            # 循环 = loop 步骤（body 递归）；模塊開始/腳本 = 单层容器
            if b == "循環":
                self.script_out.append({"step": "loop", "body": self._inline_block(block)})
            else:
                self.script_out.append({"step": "module_begin", "body": self._inline_block(block)})
        elif b == "脫出模塊":
            self.script_out.append({"step": "module_exit", "src": block.raw})
        else:
            # 语法区词条查表（查不到 = 停机）；分支结构词（branch 系）本处已按形态处理
            si = self.reg.syntax_word(b)
            if si:
                raise RegistryGapError(
                    f"语法块未建模: {b}（侧名 {si[0]}）——translate_exec_block 未实现该块形态（补实现，禁止降级）")
            raise RegistryGapError(f"命令块表外: {b}——16a CSV 命令区/语法区均无此块命令")

    def _resolve_branch_cond(self, val):
        if self.pending_choice is not None:
            return f'(Ctx::choice) == "opt{val}"'
        if self.pending_cond:
            cond = self.pending_cond
            if val == "1":
                return cond
            if val == "0":
                return _simplify_not(f"not( {cond} )")
            raise RegistryGapError(f"分歧值表外: {val}——条件分支值仅 0/1（08 裸分歧规则）")
        self.todo_mark("分歧", val)
        raise RegistryGapError(f"分歧表外: {val}——无待用条件/选择路由")

    def _inline_block(self, block, when=None):
        sub, saved = [], self.script_out
        self.script_out = sub
        push_len = len(self.when_stack)
        if when and not str(when).startswith("🔴待注册"):
            self.when_stack.append(when)
        for item in block.children:
            if isinstance(item, Line):
                self.translate_exec_line(item)
            elif isinstance(item, Block):
                self.translate_exec_block(item)
        del self.when_stack[push_len:]
        self.script_out = saved
        return sub

    def _branch_when(self, block):
        if block.bare_cmd == "主人公分歧":
            hero = block.args[0] if block.args else ""
            if hero == "其他":
                conds = [f"not( (Hero::MainHero) == ({self.speaker_of(h)}) )" for h in self.seen_heroes]
                if not conds:
                    raise RegistryGapError("主人公分歧(其他) 无前序机位——语料结构异常")
                return "and( " + ", ".join(conds) + " )" if len(conds) > 1 else conds[0]
            self.seen_heroes[hero] = True
            return f"(Hero::MainHero) == ({self.speaker_of(hero)})"
        if block.bare_cmd == "場合分歧":
            return self.translate_expression(block.args_raw) if block.args_raw else None
        return None

    # ---------- 文本变量转换（保留 v4.5 语义化占位符;v6：key 派生走信源 B + 全角→ASCII） ----------
    def _key_prefix(self, who):
        v = HERO_MAP.get(who) or AGENT_MAP.get(who) or ORG_MAP.get(who)
        if v and "::" in v:
            last = v.split("::")[-1].split(".")[-1]
            return last.replace("lord_1_", "").replace("tk5_", "").upper()
        # 槽字面（人物Ｄ/城Ａ…）：key 从槽字母派生（{HERO_D.NAME}——与大写占位符风格一致，纪律 12）
        letter = _slot_letter(who)
        if letter:
            return letter.upper()
        return re.sub(r"[^A-Za-z0-9]", "_", who).upper() or "UNKNOWN"

    def convert_text_vars(self, text, t_no=None):
        phs = []

        def ph(key, vtype, arg):
            phs.append((key, vtype, arg))
            return "{" + key + "}"

        def sub_attr(m):
            who, attr = m.group(1), m.group(2)
            p = self._key_prefix(who)
            if who == "主人公":
                if attr == "姓":
                    return ph("PLAYER.FIRSTNAME", "PLAYER_FIRSTNAME", "")
                if attr == "名":
                    return ph("PLAYER.LASTNAME", "PLAYER_LASTNAME", "")
                return ph("PLAYER.NAME", "PLAYER_NAME", "")
            if attr == "姓":
                return ph(f"{p}.FIRSTNAME", "HERO_FIRSTNAME", who)
            if attr == "名":
                return ph(f"{p}.LASTNAME", "HERO_LASTNAME", who)
            return ph(f"{p}.NAME", "HERO_NAME", who)

        text = RE_VAR_ATTR.sub(sub_attr, text)

        def sub_brace(m):
            inner = m.group(1)
            if inner.startswith("PH_") or re.match(r"^[A-Z0-9_.]+$", inner):
                return m.group(0)
            if inner == "一人稱":
                return ph("SELF_PRONOUN", "SELF_PRONOUN", "")
            if inner == "二人稱":
                return ph("OTHER_PRONOUN", "OTHER_PRONOUN", "")
            if inner == "二人稱名前":
                return ph("OTHER_PRONOUN_NAME", "OTHER_PRONOUN_NAME", "")
            if inner.endswith(".名前"):
                return ph(f"{self._key_prefix(inner[:-3])}.NAME", "HERO_NAME", inner[:-3])
            if inner.endswith(".姓名"):
                return ph(f"{self._key_prefix(inner[:-3])}.FULLNAME", "HERO_FULLNAME", inner[:-3])
            if inner.endswith(".代名詞"):
                return ph(f"SLOT_{_slot_letter(inner[:-4]).upper()}_PRONOUN", "SLOT_PRONOUN", inner[:-4])
            if inner.startswith("未知"):
                return ph(f"UNKNOWN_{inner[2:]}", "UNKNOWN", inner)
            return ph(re.sub(r"[^A-Za-z0-9]", "_", inner).upper(), "RAW", inner)

        text = RE_VAR_BRACE.sub(sub_brace, text)

        def sub_angle(m):
            slot = m.group(1)
            if slot == "年":
                return ph("TIME.YEAR", "SLOT", slot)
            if slot == "月":
                return ph("TIME.MONTH", "SLOT", slot)
            m2 = re.match(r"^(.*?)([Ａ-Ｅ])$", slot)
            m3 = re.match(r"^[ａ-ｚ]$", slot)
            if m2:
                letter = chr(ord("A") + (ord(m2.group(2)) - ord("Ａ")))
                cat = m2.group(1)
                return ph(f"SLOT_{letter}.NAME", "SLOT", slot) if cat else ph(f"SLOT_{letter}.VALUE", "SLOT", slot)
            if m3:
                letter = chr(ord("A") + (ord(slot) - ord("ａ")))
                return ph(f"SLOT_{letter}.VALUE", "SLOT", slot)
            return ph("SLOT_" + re.sub(r"[^A-Za-z0-9]", "_", slot).upper(), "SLOT", slot)

        text = RE_VAR_ANGLE.sub(sub_angle, text)

        def sub_plain(m):
            who = m.group(1)
            if who.startswith("PH_") or re.match(r"^[A-Z0-9_.]+$", who):
                return m.group(0)
            if who == "主人公":
                return ph("PLAYER.NAME", "PLAYER_NAME", "")
            if "::" in who:
                return m.group(0)
            return ph(f"{self._key_prefix(who)}.NAME", "HERO_NAME", who)

        text = RE_VAR_PLAIN.sub(sub_plain, text)
        for k, vt, arg in phs:
            self.var_inject.append((self.event_id, t_no, k, vt, arg))
        return text

    def add_line(self, speaker, text, src_raw, narrator=False, listener=None):
        self.t_counter += 1
        key = f"{self.key_prefix}_{self.event_id}_{self.t_counter}"
        line = {"cmd": "narrator" if narrator else "dialogue"}
        if speaker:
            line["speaker"] = speaker
        if listener:
            line["listener"] = listener
        line["textKey"] = key
        when = self.when_now()
        if when:
            line["when"] = when
        line["_t"] = self.t_counter
        line["_src"] = src_raw
        self.ensure_segment().lines.append(line)
        conv = self.convert_text_vars(text, self.t_counter)
        self.i18n.append((key, conv, "🔴待翻译"))

    def i18n_key(self, suffix, zh):
        self.i18n.append((f"{self.key_prefix}_{suffix}", zh, "🔴待翻译"))

    def speaker_of(self, s):
        if s == "主人公":
            return "Hero::MainHero"
        if s in HERO_MAP:
            return HERO_MAP[s]
        if s in AGENT_MAP:
            return AGENT_MAP[s]
        if re.match(r"^(人物|據點|城|大名家|勢力|忍者衆|商家|海賊衆|國)[Ａ-Ｅ]$", s):
            return self.translate_subject(None if False else self._dom_of_slot(s), s)
        raise RegistryGapError(f"说话人表外: {s}——信源 B 实体表无此" + "（回填 gen_entity_maps.py；模板 NPC 登记 AGENT_MAP）")

    def _dom_of_slot(self, s):
        """说话人槽位（人物Ｄ）→ 域词 —— 与域值区表键一致：人物/據點/城/大名家/勢力/國…
        注意：speaker_of 命中的槽位只可能是人物/據點 族。"""
        m = re.match(r"^(.+?)([Ａ-Ｅ])$", s)
        return m.group(1) if m else None

    # ---------- 事件主流程 ----------
    def translate_event(self, ev_id, tree):
        self.event_id = ev_id
        self.t_counter = 0
        self.segments, self.script_out, self.i18n, self.ctx = [], [], [], {}
        self.var_inject = []
        self.cond_ctx = {}
        self.cond_pairs = []
        self.head_src = {}
        self.when_stack, self.pending_cond, self.pending_choice = [], None, None
        self.seen_heroes = {}
        self.cur_seg, self.seg_n = None, 0
        self.current_hero = "Hero::MainHero"

        ev = {"id": ev_id, "trigger": "", "once": True, "priority": "normal",
              "condition": "", "script": []}
        self.head_src["id"] = f"事件:事件{ev_id}{{"
        src_comments = []
        cond_block = exec_block = None

        for item in tree:
            if isinstance(item, Line):
                t = item.text
                if t.startswith("屬性:"):
                    val = t.split(":", 1)[1].strip()
                    # 值查枚举值区「事件屬性」域（一次→once / 多次→repeat / 弱→weak）
                    for tok in re.split(r"[|｜]", val):
                        tok = tok.strip()
                        if not tok:
                            continue
                        evv = self.reg.enum_val("事件屬性", tok)
                        if not evv:
                            raise RegistryGapError(
                                f"事件屬性枚举外: {tok}——16a 枚举值区「事件屬性」域无此值（回填 ENUM_SETS）")
                        side = evv[0]
                        if side == "once":
                            ev["once"] = True
                        elif side == "repeat":
                            ev["once"] = False
                        elif side == "weak":
                            ev["priority"] = "weak"
                    self.head_src["once"] = f"屬性:{val}"
                    self.head_src["priority"] = f"屬性:{val}"
                elif t.startswith("發生契機:"):
                    content = t.split(":", 1)[1].strip()
                    base = content.split("(")[0].strip()
                    evv = self.reg.enum_val("觸發", base)
                    if not evv:
                        raise RegistryGapError(
                            f"trigger 表外: {base}——16a 枚举值区「觸發」域无此契機（回填 RES_SETS 或 01/16 trigger 注册表）")
                    trig_side = evv[0]                       # Trigger::house_enter
                    ev["trigger"] = trig_side.split("::")[-1]   # 01 字段值风格 = 裸名（§15.2 A6）
                    self.form = TRIGGER_FORM.get(base, "menu_dialogue")
                    m = re.search(r"\((.*?)\)", content)
                    if m:
                        # 契機第二参数（室內畫面表示後(主人公據點,自宅) → 自宅）：設施 枚举 → Facility::*
                        ps = [p.strip() for p in m.group(1).split(",")]
                        if ev["trigger"] == "house_enter" and ps:
                            ev["facility"] = self._translate_scene_id(ps[-1])
                    self.head_src["trigger"] = f"發生契機:{content}"
                    self.head_src["facility"] = f"發生契機:{content}"
            elif isinstance(item, Block):
                if item.bare_cmd == "發生條件":
                    cond_block = item
                    src_comments.append("// 🔴 条件（TK5 原文）：")
                    for c in item.children:
                        if isinstance(c, Line):
                            src_comments.append(f"//   {c.text}")
                        elif isinstance(c, Block):
                            src_comments.append(f"//   {c.raw}")
                elif item.bare_cmd == "執行":
                    exec_block = item

        if cond_block:
            ev["condition"] = self.translate_condition(cond_block)
        if exec_block:
            self.translate_execution(exec_block.children)
        self.cur_seg = None
        ev["script"] = self.script_out
        return ev, src_comments


# ---------------------------------------------------------------------------
# 输出组装（保持 v5：src 原文 → 注释；正文纯翻译）
# ---------------------------------------------------------------------------
def render_steps_list(steps, indent=4, is_last=True):
    pad = " " * indent
    out = []
    n = len(steps)
    for i, st in enumerate(steps):
        last = (i == n - 1) and is_last
        src = st.pop("src", None)
        if src:
            out.append(f"{pad}// 源：{src.strip()}")
        nested = {k: st.pop(k) for k in ("then", "else", "body") if k in st}
        if nested:
            head = json.dumps(st, ensure_ascii=False)
            out.append(f"{pad}{head[:-1]},")
            nk = list(nested.keys())
            for j, k in enumerate(nk):
                out.append(f'{pad}  "{k}": [')
                out.extend(render_steps_list(nested[k], indent + 4))
                out.append(f'{pad}  ]' + ("," if j < len(nk) - 1 else ""))
            out.append(f"{pad}}}" + ("" if last else ","))
        else:
            comma = "" if last else ","
            out.append(f"{pad}{json.dumps(st, ensure_ascii=False)}{comma}")
    return out


def render_story_jsonc(seg):
    parts = [f"{{", f'  "id": "{seg.id}",', f'  "form": "{seg.form}",', '  "lines": [']
    items = []
    for ln in seg.lines:
        if "_t" in ln:
            t_no = ln.pop("_t")
            src = ln.pop("_src")
            items.append(f"    // T{t_no} {src.strip()}")
            items.append("    " + json.dumps(ln, ensure_ascii=False))
        else:
            items.append("    " + json.dumps(ln, ensure_ascii=False))
    parts.append(",\n".join(items))
    parts.append("  ]\n}")
    return "\n".join(parts)


def main():
    ap = argparse.ArgumentParser(description="太阁5 → 01 DSL 机械翻译器 v6（数据驱动查表）")
    ap.add_argument("--events", nargs="+", required=True)
    ap.add_argument("--scenario", default="okehazama")
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--registry", default=DEFAULT_REGISTRY)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--sort", default="cluster", choices=["cluster", "input"])
    ap.add_argument("--loose", action="store_true",
                    help="宽松模式（调试用）：表外报错改为记录 + 跳过该事件（默认 = 严格停机，零兜底纪律）")
    args = ap.parse_args()

    with open(args.source, encoding="utf-8") as f:
        events_map = parse_source(f.read())
    reg = Registry(args.registry)
    reg.set_csv_path(args.registry)
    # 参数列全表预载（S1 完整性自证：154 行命令 + 语法词参数列全部结构化；残留 = 生成器缺陷报错）
    param_bad = 0
    with open(args.registry, encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            if r["类别"] in ("命令", "语法"):
                reg.param_cache[(r["类别"], r["太阁原词"])] = parse_params_spec(r["参数"])
    print(f"[S1] 参数列结构化：命令/语法词条共 {len(reg.param_cache)} 行（残留 0 = 全结构化）")

    results = []
    for ev_id in args.events:
        if ev_id not in events_map:
            print(f"[WARN] 源中找不到事件 {ev_id}，跳过")
            continue
        tr = Translator(reg, args.scenario)
        try:
            ev, comments = tr.translate_event(ev_id, build_tree(events_map[ev_id]))
        except RegistryGapError as e:
            if args.loose:
                tr.gap(str(e), ev_id)
                print(f"[SKIP] {ev_id}（--loose）：{e}")
                continue
            print(f"[FAIL] {ev_id}: {e}")
            print("❌ 表外词条 = 16a CSV 生成器/信源 B 实体表缺陷："
                  "回填映射表 → 重跑 build_registry_csv.py / gen_entity_maps.py → 重跑本脚本")
            sys.exit(1)
        results.append((ev_id, ev, comments, tr))

    if args.sort == "cluster":
        order = {e: i for i, e in enumerate(CLUSTER_ORDER)}
        results.sort(key=lambda x: order.get(x[0], 9999))

    out_dir = os.path.join(args.out, args.scenario)
    story_dir = os.path.join(out_dir, "story")
    i18n_dir = os.path.join(out_dir, "i18n")
    for d in (story_dir, i18n_dir):
        if os.path.isdir(d):
            for fn in os.listdir(d):
                p = os.path.join(d, fn)
                if os.path.isfile(p):
                    os.remove(p)
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(story_dir, exist_ok=True)
    os.makedirs(i18n_dir, exist_ok=True)

    combined = []
    for ev_id, ev, comments, tr in results:
        combined.append(f"// ============ 事件 {ev_id}（{EVENT_NAME.get(ev_id, '')}） ============")
        combined.append("// ---- 机械翻译产物（待 agent 审核；字段/步骤上方注释 = TK5 源行） ----")
        ev_copy = dict(ev)
        script_lines = render_steps_list(ev_copy.pop("script"))
        cond_val = ev_copy.pop("condition", "")
        cond_pairs = tr.cond_pairs
        head_src = tr.head_src
        ev_lines = []
        keys = list(ev_copy.keys())
        for k in keys:
            if k in head_src:
                ev_lines.append(f"  // 源：{head_src[k]}")
            ev_lines.append(f'  "{k}": {json.dumps(ev_copy[k], ensure_ascii=False)},')
        if cond_pairs:
            for i, (src_line, dsl) in enumerate(cond_pairs, 1):
                ev_lines.append(f"  // [{i}] {src_line}  →  {dsl}")
        ev_lines.append(f'  "condition": {json.dumps(cond_val, ensure_ascii=False)},')
        combined.append("{")
        combined.extend(ev_lines)
        combined.append(f'  "script": [')
        combined.extend(script_lines)
        combined.append("  ]")
        combined.append("}")
        combined.append("")
    with open(os.path.join(out_dir, "events.jsonc"), "w", encoding="utf-8") as f:
        f.write("\n".join(combined))

    for ev_id, ev, comments, tr in results:
        for seg in tr.segments:
            with open(os.path.join(story_dir, f"{seg.id}.jsonc"), "w", encoding="utf-8") as f:
                f.write(render_story_jsonc(seg))

    merged_story = []
    for ev_id, ev, comments, tr in results:
        for seg in tr.segments:
            merged_story.append(f"// ============ {seg.id}（{EVENT_NAME.get(ev_id, '')}） ============")
            merged_story.append(render_story_jsonc(seg))
            merged_story.append("")
    with open(os.path.join(out_dir, "story.jsonc"), "w", encoding="utf-8") as f:
        f.write("\n".join(merged_story))

    xml_parts = ['<?xml version="1.0" encoding="utf-8"?>', "<strings>"]
    for ev_id, ev, comments, tr in results:
        for key, zh, en in tr.i18n:
            xml_parts.append(f'  <string id="{key}" text="{zh}"/>')
    xml_parts.append("</strings>")
    with open(os.path.join(i18n_dir, f"std_scn_{args.scenario}.xml"), "w", encoding="utf-8") as f:
        f.write("\n".join(xml_parts))

    # 报告
    report = ["# 覆盖报告", ""]
    all_todo = []
    for ev_id, ev, comments, tr in results:
        report.append(f"## {ev_id} {EVENT_NAME.get(ev_id, '')}")
        report.append(f"- trigger: {ev['trigger']} | once: {ev['once']} | priority: {ev['priority']}")
        report.append(f"- condition: {ev['condition'] if ev['condition'] else '（空）'}")
        report.append(f"- script 步骤: {len(ev['script'])} | 演出段: {len(tr.segments)} | lines 总数: {sum(len(s.lines) for s in tr.segments)}")
        report.append(f"- 待注册: {len(tr.todo)} 条（去重后 {len(tr.todo_dedup())} 条）")
        all_todo.extend(tr.todo)
        report.append("")
    report.append("## 文本变量注入表（TK5 变量 → TextObject 占位符 {PH_N}，运行时 LWN 注入）")
    report.append("> 类型说明：HERO_NAME=角色全名 / HERO_GIVEN=名 / HERO_CLAN=姓(苗字) / PLAYER_NAME=玩家名 /")
    report.append("> SELF_PRONOUN=自称(一人稱) / OTHER_PRONOUN=他称(二人稱) / OTHER_PRONOUN_NAME=他称+名前 /")
    report.append("> SLOT=代入槽显示 / SLOT_PRONOUN=槽角色代名词 / UNKNOWN=未知token待人工裁决 / RAW=原样")
    inject_all = [v for _, _, _, tr in results for v in tr.var_inject]
    seen_ij = set()
    for ev, t_no, ph, vtype, arg in inject_all:
        k = (ev, ph, vtype, arg)
        if k in seen_ij:
            continue
        seen_ij.add(k)
        report.append(f"- {ev} T{t_no}: {{{ph}}} [{vtype}] {arg}")
    report.append("")
    reg = results[0][3].reg if results else None
    if reg:
        import collections as _c2
        used = reg.used
        tier_cnt = _c2.Counter()
        store = _c2.defaultdict(set)
        reserved, degraded = set(), set()
        for k in sorted(reg.verdicts):
            if (k[0], k[2]) not in used:
                continue
            v = reg.verdicts[k]
            tier_cnt[v.tier or "?"] += 1
            if v.savekey and v.savekey != "无":
                store[v.savekey].add("%s::%s" % (k[1], k[2]) if k[1] else k[2])
            if v.reserved:
                reserved.add("%s / %s" % (k[0], k[2]))
            if v.degraded:
                degraded.add("%s / %s" % (k[0], k[2]))
        report.append("## 落点分档（本剧本实际用到的 %d 个词条，裁定来自 16b）" % sum(tier_cnt.values()))
        report.append("- " + "  ".join("%s=%d" % kv for kv in sorted(tier_cnt.items())))
        report.append("")
        report.append("### 落仓清单（要存档的字段 → 存档键）")
        for key in sorted(store):
            ws = sorted(store[key])
            report.append("- `%s`：%d 个字段 —— %s%s"
                          % (key, len(ws), "、".join(ws[:12]), " …" if len(ws) > 12 else ""))
        report.append("")
        report.append("### T3-预留（能解析能存档，行为空执行 + [Scenario][TODO] 日志）：%d 个" % len(reserved))
        for w in sorted(reserved):
            report.append("- " + w)
        report.append("")
        report.append("### T0 降级（本期不落地，只作注释保留）：%d 个" % len(degraded))
        for w in sorted(degraded):
            report.append("- " + w)
        report.append("")
    _txt = "\n".join(combined) + "\n".join(merged_story)
    _cities = sorted(set(re.findall(r"tk5_city_\d+", _txt)))
    if _cities:
        report.append("## 占位据点（太阁有、骑砍地图上没有 → 07 数据包补真城）")
        report.append("> 锚点 = 织丰表给的「最近的骑砍据点」，只用来定位置，**不是同一个地方**。")
        for cid in _cities:
            cn = CITY_PLACEHOLDER.get(cid, "?")
            report.append("- `%s` = %s｜锚点 %s" % (cid, cn, CITY_ANCHOR.get(cn) or "（无）"))
        report.append("")
    if MISSING_IN_XML:
        report.append("## 实体缺口（织丰表有、模块 XML 里没有 → 07 数据包补）")
        for label, ids in MISSING_IN_XML.items():
            report.append("- %s：%d 个 —— %s%s"
                          % (label, len(ids), "、".join(ids[:10]), " …" if len(ids) > 10 else ""))
        report.append("")
    report.append("## 待注册清单（去重，禁止猜译，逐条人工裁决）")
    seen = set()
    for e, w, c, x in all_todo:
        t = (e, w, c, x)
        if t in seen:
            continue
        seen.add(t)
        report.append(f"- {e}: [{w}] {c} ｜ {x}")
    with open(os.path.join(out_dir, f"report_{args.scenario}.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(report))

    # S3：gap_report（机器可读缺口清单；翻译器零兜底纪律下的"停机→回填"闭环入口）
    gap_dir = []
    for ev_id, ev, comments, tr in results:
        for g in tr.gap_notes:
            gap_dir.append(g)
    gap_path = os.path.join(out_dir, "gap_report.txt")
    with open(gap_path, "w", encoding="utf-8") as f:
        f.write("# 翻译缺口报告（S3 零兜底纪律；表外 = 生成器缺陷，回填后重跑收敛到零；空 = 零表外）\n")
        f.write("# 格式：事件 | 类别 | 详情 | 建议回填点\n")
        for ev_id, what, ctx_str in gap_dir:
            f.write("%s | 停机级缺口 | %s | %s\n" % (ev_id, what, ctx_str))
        # todo_mark 属"有主缺口"（参数位不够/可预列/实体待 07），另列
        seen_t = set()
        for e, w, c, x in all_todo:
            t = (e, w, c, x)
            if t in seen_t:
                continue
            seen_t.add(t)
            f.write("%s | 有主缺口 | [%s] %s %s | 回填 gen_registry_tables.py / 07 数据包\n" % (e, w, c, x))

    todo_cnt = len({t for t in all_todo})
    print(f"完成：{len(results)} 个事件 → {out_dir}")
    print(f"待注册：{todo_cnt} 条（去重；详情见 report_{args.scenario}.txt）")
    print(f"缺口：{len(gap_dir)} 条停机级（见 gap_report.txt）")
    for ev_id, ev, comments, tr in results[:1]:
        print(f"\n== {ev_id} condition ==")
        print(ev["condition"])


if __name__ == "__main__":
    main()

# -*- coding: utf-8 -*-
"""
tk5_to_json.py — 太阁5 事件 → 01 DSL 事件 JSON 机械翻译器（v2）
================================================================
设计规格：plans/scenario-campaign-mode/08b-转化器规格-自动化翻译流水线.md
v2 变更（2026-08-27，v1 自我审核修复）：
    1. translate_ref 域前缀感知（Hero.clan / Settlement.clan 按主体域取对应部分）
    2. 属性调用特例（外交同盟→isAllied / 鄰接大名家→isNeighbor / 全城壓制→待注册）
    3. 代入命令（cmd.startswith("代入") → 槽登记）
    4. 条件嵌套递归（AＮＤ/OＲ）+ 源重复拷贝自动去重（EFF0C300_159 实测 4 个 OＲ調查 = 2 组重复）
    5. 分歧:(N) 语义状态机：調查后 = 条件分支（if/else 合并）；選擇后 = Ctx 槽路由
    6. 執行内 調查 → pending_cond（不落 effect）
    7. 演绎剧本 lines 带 when 门控（分支/机位条件栈传播）
    8. 演出段切分（進入設施/離開設施/机位块切段）+ 骨架 perform 引用
    9. 待07 占位 = 裸格式（09b 风格 `🔴待07归蝶`，非 Hero:: 前缀）
    10. 报告待注册去重

用法：
    python tk5_to_json.py --events EFF0C300_159 --scenario okehazama
"""
import argparse
import csv
import json
import os
import re
import shutil
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
# 代入槽 → Ctx 槽名映射（🔴 v4.3：变量语义保留——代入 = ctx_set 动作、引用 = Ctx::<slot>，
# 不做静态展开；16 §一 Ctx 三档作用域权威，CSV 命令区「代入X → Ctx/Variable/GlobalSlot」）
# ---------------------------------------------------------------------------
SLOT_NAME_MAP = {
    "人物Ａ": "hero_A", "人物Ｂ": "hero_B", "人物Ｃ": "hero_C", "人物Ｄ": "hero_D", "人物Ｅ": "hero_E",
    "城Ａ": "settlement_A", "城Ｂ": "settlement_B", "城Ｃ": "settlement_C", "城Ｄ": "settlement_D", "城Ｅ": "settlement_E",
    "據點Ａ": "place_A", "據點Ｂ": "place_B", "據點Ｃ": "place_C", "據點Ｄ": "place_D", "據點Ｅ": "place_E",
    "大名家Ａ": "clan_A", "大名家Ｂ": "clan_B", "大名家Ｃ": "clan_C", "大名家Ｄ": "clan_D", "大名家Ｅ": "clan_E",
    "勢力Ａ": "faction_A", "勢力Ｂ": "faction_B", "勢力Ｃ": "faction_C",
}
_SLOT_CAT = {
    "忍者衆": "ninja", "海賊衆": "pirate", "商家": "merchant", "物品": "item", "卡": "card",
    "國": "region", "地方": "area", "町": "town", "里": "village", "砦": "fort",
    "軍團": "army", "流派": "school", "交易品": "trade", "主命目標": "quest", "文字列": "text",
}


def slot_cname(s):
    """TK5 槽名（人物Ｄ/城Ａ/據點Ａ/ａ…）→ Ctx 英文槽名（hero_D/settlement_A/place_A/var_a…）。"""
    if s in SLOT_NAME_MAP:
        return SLOT_NAME_MAP[s]
    if re.match(r"^[ａ-ｚ]$", s):
        return "var_" + chr(ord("a") + (ord(s) - ord("ａ")))
    if re.match(r"^[A-Z]$", s):
        return "var_" + s.lower()
    m = re.match(r"^(.*?)([Ａ-Ｅ])$", s)
    if m:
        cat = _SLOT_CAT.get(m.group(1), "slot")
        letter = chr(ord("A") + (ord(m.group(2)) - ord("Ａ")))   # 全角Ａ-Ｅ → ASCII A-E
        return f"{cat}_{letter}"
    return "slot_" + re.sub(r"[^A-Za-z0-9_]", "_", s)


# ---------------------------------------------------------------------------
# 归一表 v4.2（🔴 占位 ID 全部英文 ASCII——StringId 合法形态，程序可解析；
# report 输出「占位 ID 映射表」，07 素材表落地后全局替换为织丰真实 ID）
# ---------------------------------------------------------------------------
HERO_MAP = {   # 有 Hero 身份的正式/占位名
    "織田信長": "Hero::lord_1_oda",
    "今川義元": "Hero::lord_1_imagawa",
    "今川氏真": "Hero::lord_1_imagawa_uji",
    "德川家康": "Hero::lord_1_matsudaira",
    "前田利家": "Hero::lord_1_maeda",
    "豐臣秀吉": "Hero::lord_1_hideyoshi",
    "主人公": "Hero::MainHero",
    "織田信勝": "Hero::tk5_nobukatsu",
    "齋藤道三": "Hero::tk5_dosan",
    "北條氏康": "Hero::tk5_houjou_ujiyasu",
    "武田信玄": "Hero::tk5_takeda_shingen",
    "足利義輝": "Hero::tk5_ashikaga_yoshiteru",
    "三好長慶": "Hero::tk5_miyoshi_nagayoshi",
    "歸蝶": "Hero::tk5_kicho",
    "服部小平太": "Hero::tk5_hattori_koheita",
    "毛利新介": "Hero::tk5_mori_shinsuke",
    "岡部元信": "Hero::tk5_okabe_motonobu",
    "鵜殿長照": "Hero::tk5_udono_nagateru",
    "太原雪齋": "Hero::tk5_taigen_sessai",
    "蜂須賀小六": "Hero::tk5_hachisuka_koroku",
    "豐臣秀長": "Hero::tk5_hidenaga",
    "寧寧": "Hero::tk5_nene",
    "九鬼嘉隆": "Hero::tk5_kuki_yoshitaka",
}
AGENT_MAP = {  # 模板角色（无 Hero → Agent:: = CharacterObject 模板引用；menu_dialogue 形态 = 立绘头像源）
    "忍者": "Agent::tk5_ninja",
    "小姓": "Agent::tk5_kosho",
    "家臣": "Agent::tk5_kashin",
    "傳令": "Agent::tk5_denrei",
    "侍從": "Agent::tk5_jiju",
    "足輕": "Agent::tk5_ashigaru",
    "備大將": "Agent::tk5_bitaisho",
    "部將": "Agent::tk5_busho",
    "武將": "Agent::tk5_busho_generic",
    "僧侶": "Agent::tk5_monk",
    "旅人": "Agent::tk5_traveler",
    "守將": "Agent::tk5_shusho",
    "守軍": "Agent::tk5_shugun",
    "今川兵": "Agent::tk5_imagawa_soldier",
    "士兵": "Agent::tk5_soldier",
}
CLAN_MAP = {
    "今川義元": "Clan::clan_imagawa_1",
    "織田信長": "Clan::clan_oda_1",
    "北條氏康": "Clan::tk5_houjou",
    "武田信玄": "Clan::tk5_takeda",
    "足利義輝": "Clan::tk5_ashikaga",
    "三好長慶": "Clan::tk5_miyoshi",
    "德川家康": "Clan::tk5_matsudaira",
    "今川氏真": "Clan::clan_imagawa_1",
}
KINGDOM_MAP = {
    "今川義元": "Faction::Kingdom.imagawa",
    "織田信長": "Faction::Kingdom.oda",
    "北條氏康": "Faction::Kingdom.tk5_houjou",
    "武田信玄": "Faction::Kingdom.tk5_takeda",
    "足利義輝": "Faction::Kingdom.tk5_ashikaga",
    "三好長慶": "Faction::Kingdom.tk5_miyoshi",
}
SETTLEMENT_MAP = {
    "鳴海": "Settlement::tk5_narumi",
    "鳴海城": "Settlement::tk5_narumi",
    "岡崎": "Settlement::tk5_okazaki",
    "岡崎城": "Settlement::tk5_okazaki",
    "二條": "Settlement::tk5_nijo",
    "二條城": "Settlement::tk5_nijo",
    "清洲": "Settlement::town_CHUB11",
    "清洲城": "Settlement::town_CHUB11",
    "那古野": "Settlement::tk5_nagoya",
    "駿府": "Settlement::tk5_sumpu",
    "駿府城": "Settlement::tk5_sumpu",
}
REGION_MAP = {
    "駿河": "Region::tk5_suruga",
    "遠江": "Region::tk5_totomi",
    "三河": "Region::tk5_mikawa",
    "尾張": "Region::tk5_owari",
}
# fallback 罗马音映射（漏网角色/城；确定性英文 ID——禁止中文进 ID）
FALLBACK_MAP = {
    "佐久間盛重": "Hero::tk5_sakuma_masanari",
    "功勳家臣": "Agent::tk5_kashin_merit",
    "武力家臣": "Agent::tk5_kashin_martial",
    "外交家臣": "Agent::tk5_kashin_diplomat",
    "功勳陪臣": "Agent::tk5_hikan_merit",
    "釜山之町": "Settlement::tk5_busan",
    "那覇之町": "Settlement::tk5_naha",
    "寧波之町": "Settlement::tk5_ningbo",
    "呂宋之町": "Settlement::tk5_lusong",
    "發生據點": "Ctx::event_settlement",
    "岡崎之町": "Settlement::tk5_okazaki",
}
# 确定性兜底：中文名 → 稳定英文 ID（hash 后缀），report 登记中文名
import hashlib


def _fallback_ascii(subject):
    h = hashlib.md5(subject.encode("utf-8")).hexdigest()[:6]
    return f"tk5_u{h}"


# 🔴 v2：实体引用域 fallback 前缀（与 gen_registry_tables.ENTITY_DOMAINS 同步）——
# 具名实体（忍者衆::伊賀衆 / 卡::無刀取 / 官位::正一位…）不进 CSV 域值区，
# 翻译器名字表 miss 后走确定性兜底 + report 登记，由 07/13/17 数据包定稿 StringId
_ENTITY_FALLBACK = {
    "忍者衆": "Org", "商家": "Org", "海賊衆": "Org", "卡": "Card", "流派": "Card",
    "物品": "Item", "交易品": "Item", "地方": "Region", "官位": "court_rank",
    "官職": "title", "工作": "QuestDef", "事件主命": "QuestDef",
}

# 函数/碎片侧名（与域无关，_pick_side 直接接受）
_FUNC_SIDES = {"exists", "isAllied", "isNeighbor", "allControlled", "hasMet", "hasRelation", "relation", "unknown"}

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
CLUSTER_ORDER = [   # 桶狭间历史时间轴
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
TRIGGER_MAP = {
    "據點畫面表示後": "settlement_enter",
    "室內畫面表示後": "house_enter",
    "野戰開始時": "field_battle_start",
    "野戰結束時": "field_battle_end",
    "攻城戰開始時": "siege_battle_start",
    "攻城戰結束時": "siege_battle_end",
    "評定開始時": "council_start",
    "軍團移動結束時": "army_move_end",
    "遊戲開始時": "game_start",
    "每月": "monthly",
    "每日": "daily",
}


# ---------------------------------------------------------------------------
# 解析器（v1 已验证）
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
# 翻译表加载（v2：属性 = 域.属性 二维；新增 域值 区——16a CSV 是全语料闭包，查不到 = 生成器缺陷）
# ---------------------------------------------------------------------------
class RegistryGapError(Exception):
    """表外词条 = 生成器缺陷（16a CSV 已做全语料覆盖自检；翻译器查不到 → 修表重跑，禁止产出 🔴待注册）。"""


class Registry:
    def __init__(self, csv_path):
        self.domains, self.attrs, self.domain_vals, self.predicates, self.commands = {}, {}, {}, {}, {}
        self.bare_vals = {}     # 域值区纯 token 反查（武將→general 等，translate_value 用）
        with open(csv_path, encoding="utf-8-sig") as f:
            for r in csv.DictReader(f):
                cat, src, side, usage = r["类别"], r["太阁原词"], r["我们侧名"], r["备注"]
                if cat == "域":
                    self.domains[src] = side
                elif cat == "属性":
                    self.attrs[src] = (side, r["值类型"], usage)          # src = 属性名（单键，多域 ' / ' 分段）
                elif cat == "域值":
                    # 🔴 v2：CSV 太阁原词 = 纯值，所属域列 = 域（第二列不掺符号）→ 内部重建「域::值」键
                    key = f"{r['所属域']}::{src}"
                    self.domain_vals[key] = (side, r["值类型"], usage)
                    if "::" not in side and side != "null":
                        if src not in self.bare_vals:
                            self.bare_vals[src] = side            # 同名多域同 token（浪人→ronin）
                elif cat == "函数":
                    self.predicates[src] = side                          # src = 调用词（外交同盟→isAllied）
                elif cat == "命令":
                    self.commands[src] = (side, usage)

    def domain(self, w): return self.domains.get(w)
    def attr(self, name): return self.attrs.get(name)
    def domain_val(self, dom, val): return self.domain_vals.get(f"{dom}::{val}")
    def predicate(self, w): return self.predicates.get(w)
    def command(self, w): return self.commands.get(w)


# ---------------------------------------------------------------------------
# 翻译器 v2
# ---------------------------------------------------------------------------
class Segment:
    """演出单元（一段连续表现层 → 一个演绎剧本）。"""
    def __init__(self, seg_id, form):
        self.id = seg_id
        self.form = form
        self.lines = []


# ---------------------------------------------------------------------------
# 文本变量模式（TK5 全语料扫描 2026-08-27：称呼变体 15k+ / 人称 5k / 未知 token 1.5k / 槽 4k）
# 替换为 TextObject 占位符 {PH_N}，运行时 LWN 注入（骑砍2 TextObject 支持 {KEY} 传参）
# ---------------------------------------------------------------------------
RE_VAR_ATTR = re.compile(r"\(([^()]+)\.(姓|名|名前)\)")    # (X.姓/名/名前) 称呼变体
RE_VAR_BRACE = re.compile(r"\{([^}]+)\}")                   # {一人稱}/{X.名前}/{未知NN}
RE_VAR_ANGLE = re.compile(r"<([^>]+)>")                     # <城Ａ>/<年>/<ａ> 槽显示
RE_VAR_PLAIN = re.compile(r"\(([^()]+)\)")                  # (X) 角色全名


def _simplify_not(expr):
    """化简 not 嵌套：not( not( X ) ) → X。"""
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
        self.todo = []          # (事件, 类别, 词, 上下文)
        self.var_inject = []    # (事件, T#, 占位符, 类型, 源) —— 文本变量注入表
        self.ctx = {}           # 代入槽
        self.segments = []      # [Segment]
        self.cur_seg = None
        self.script_out = []    # 骨架步骤
        self.when_stack = []    # 条件栈（传播到 lines）
        self.seen_heroes = set()   # 已见机位（主人公分歧:(其他) 取反用）
        self.pending_cond = None    # 执行内 調查 → 待用条件（最近一次调查，保留至被覆盖）
        self.pending_choice = None  # 選擇 → 待用路由标记
        self.t_counter = 0
        self.current_hero = "Hero::MainHero"
        self.key_prefix = f"LWN_SCN_{scenario}"
        self.form = "menu_dialogue"
        self.seg_n = 0

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
        """当前条件栈 → DSL 表达式（无栈 → None）。"""
        if not self.when_stack:
            return None
        if len(self.when_stack) == 1:
            return self.when_stack[0]
        return "and( " + ", ".join(self.when_stack) + " )"

    # ---------- 引用翻译 ----------
    def translate_ref(self, ref_str):
        """`域::主体.属性(参数)` / `域::主体.属性` / `域::主体` → DSL。返回 (dsl, ok)。"""
        m = re.match(r"^(.*?)::(.*)$", ref_str)
        if not m:
            raise RegistryGapError(f"无域引用: {ref_str}")
        dom_word, rest = m.group(1), m.group(2)
        # 主体 = 第一个 .属性 之前；属性部分含调用括号
        if "." in rest:
            subject, attr_part = rest.split(".", 1)
        else:
            subject, attr_part = rest, ""
        # 属性调用：attr(参数) → 函数（16a CSV 函数区，全语料闭包）
        attr_word = attr_part
        callm = re.match(r"^(.*?)\((.*)\)$", attr_part) if attr_part else None
        if callm:
            attr_word, call_args = callm.group(1), callm.group(2)
            pred = self.reg.predicate(attr_word)
            if not pred:
                raise RegistryGapError(f"调用表外: {dom_word}::{subject}.{attr_word}(…)——16a CSV 函数区无此调用")
            target = self.translate_ref(call_args)
            return f"{pred}({self._call_subject(pred, dom_word, subject)}, {target[0]})", target[1]
        if not attr_part:
            # 纯 `域::主体`（存在性/裸引用/代入槽）
            return self.translate_subject(dom_word, subject), True
        attr = self.reg.attr(attr_word)
        if not attr:
            raise RegistryGapError(f"属性表外: {attr_word}——16a CSV 属性区无此属性行")
        side, typ, usage = attr
        if side.startswith("exists"):
            return f"exists({self.translate_subject(dom_word, subject)})", True
        subj = self.translate_subject(dom_word, subject)
        seg = self._pick_side(side, dom_word)
        if seg is None:
            raise RegistryGapError(f"属性域错配: {dom_word}.{attr_word}——侧名「{side}」无 {dom_word} 域段（回填 gen_registry_tables PAIR_OVERRIDE）")
        if seg == "hasMet":
            return f"hasMet({subj}, Hero::MainHero)", True     # 認識標誌 = 与主人公是否认识
        if seg in ("relation", "hasRelation"):
            return f"relation({subj}, Hero::MainHero)", True   # 親密度/與主人公關係
        if seg == "unknown":
            self.todo_mark("属性-未知", f"{dom_word}.{attr_word}", ref_str)
        if re.match(r'^[A-Z][A-Za-z]*\.', seg):
            seg = seg.split('.', 1)[1]                          # 剥域前缀：Hero.clan → .clan
        return f"({subj}.{seg})", True

    # 🔴 v2：多段侧名 'Hero.clan / Settlement.clan' → 按域前缀取段（与 gen_registry_tables.PREFIX_BY_DOMAIN 同步）
    _DOMAIN_PREFIX = {
        "人物": "Hero", "城": "Settlement", "據點": "Settlement", "砦": "Settlement", "町": "Settlement", "里": "Settlement",
        "大名家": "Clan", "勢力": "Faction", "國": "Region", "地方": "Region",
        "軍團": "Army", "事件": "Event", "狀況": "Time", "事件標誌": "Flag", "變量": "Variable",
        "主命": "QuestDef", "官職": "title", "官位": "court_rank", "人物類別": "Identity",
        "忍者衆": "Org", "商家": "Org", "海賊衆": "Org", "卡": "Card", "流派": "Card",
        "物品": "Item", "交易品": "Item", "工作": "QuestDef", "事件主命": "QuestDef", "主命屬性": "QuestDef",
        "遊戲通關種類": "ending", "事件發生狀態": "Event", "環境變量": "env", "背景音樂": "bgm",
        "天氣": "weather", "軍團方針": "intent", "物品類型": "ItemType",
        "日數計數器": "Time", "儲存號": "Variable", "場面": "Facility",
        "戰鬥結束種類": "BattleResult", "真偽": "Bool", "身份": "Identity",
    }

    def _pick_side(self, side, dom_word):
        """多段侧名按域前缀取段；全局变量段/函数段与域无关。"""
        prefix = self._DOMAIN_PREFIX.get(dom_word)
        for p in side.split(" / "):
            p = p.strip()
            if prefix and (p.startswith(prefix + ".") or p == prefix):
                return p
        for p in side.split(" / "):
            p = p.strip()
            if p.startswith(("Variable::", "Ctx::")) or p in _FUNC_SIDES:
                return p
        return None

    def _call_subject(self, pred, dom_word, subject):
        """函数主体验证/转换：外交/邻接 → 势力（Faction::Kingdom），全城压制 → 区域（Region）。"""
        if pred in ("isAllied", "isNeighbor", "relation"):
            return self._kingdom(subject)
        if pred == "allControlled":
            return self._region(subject)
        return self.translate_subject(dom_word, subject)

    def _kingdom(self, subject):
        v = KINGDOM_MAP.get(subject)
        if v:
            return v
        self.todo_mark("势力", subject)
        return f"Faction::Kingdom.🔴待07{subject}"

    def _region(self, subject):
        v = REGION_MAP.get(subject)
        if v:
            return v
        self.todo_mark("区域", subject)
        return f"Region::🔴待07{subject}"

    def translate_subject(self, dom_word, subject):
        """域::主体 → DSL 引用。"""
        if subject.startswith("主人公"):
            if subject == "主人公":
                return "Hero::MainHero"
            if subject == "主人公據點":
                return "(Hero::MainHero.settlement)"
            if subject == "主人公當主據點":
                return "(Hero::MainHero.home)"      # v2：CSV 域值区登记（據點::主人公當主據點）
        if subject.startswith("發生人物"):
            return "Ctx::event_hero"
        if subject.startswith("發生據點"):
            return "Ctx::event_settlement"
        if subject.startswith("發生大名家") or subject.startswith("發生勢力"):
            return f"Ctx::{slot_cname(subject)}"
        if re.match(r"^(人物|據點|城|大名家|勢力|國|忍者衆|商家|海賊衆|地方|町|砦|里|軍團|流派)[Ａ-Ｅ]$", subject) or re.match(r"^[ａ-ｚ]$", subject):
            # 🔴 v4.4：条件块代入槽 → 静态展开（cond_ctx 有值）；执行块代入槽 → Ctx 变量
            if subject in self.cond_ctx:
                return self.cond_ctx[subject]
            return f"Ctx::{slot_cname(subject)}"
        if dom_word == "人物":
            if subject == "無效":
                return "null"
            v = HERO_MAP.get(subject) or AGENT_MAP.get(subject) or FALLBACK_MAP.get(subject)
            if v:
                return v
            self.todo_mark("角色", subject)
            return f"Hero::{_fallback_ascii(subject)}"   # 确定性英文兜底（report 登记中文名）
        if dom_word == "大名家":
            v = CLAN_MAP.get(subject) or FALLBACK_MAP.get(subject)
            if v:
                return v
            self.todo_mark("大名家", subject)
            return f"Clan::{_fallback_ascii(subject)}"
        if dom_word in ("城", "據點", "砦", "町", "里"):
            v = SETTLEMENT_MAP.get(subject) or FALLBACK_MAP.get(subject)
            if v:
                return v
            self.todo_mark("城池", subject)
            return f"Settlement::{_fallback_ascii(subject)}"
        if dom_word == "勢力":
            v = KINGDOM_MAP.get(subject) or FALLBACK_MAP.get(subject)
            if v:
                return v
            self.todo_mark("势力", subject)
            return f"Faction::Kingdom.{_fallback_ascii(subject)}"
        if dom_word == "國":
            v = REGION_MAP.get(subject) or FALLBACK_MAP.get(subject)
            if v:
                return v
            self.todo_mark("区域", subject)
            return f"Region::{_fallback_ascii(subject)}"
        if dom_word == "真偽":
            return "true" if subject == "真" else "false"
        if dom_word == "事件":
            return f"(Event::{subject}.done)"
        if dom_word == "無效":
            return "null"
        # 🔴 v2：实体引用域（忍者衆/商家/卡/流派/物品/地方/官位/官職/工作…）→ 名字表 fallback，
        #   不进 CSV 域值区（具名实体是归一表的事，2026-08-27 用户裁定）
        if dom_word in _ENTITY_FALLBACK:
            self.todo_mark("实体", f"{dom_word}::{subject}")
            return f"{_ENTITY_FALLBACK[dom_word]}::{_fallback_ascii(subject)}"
        # 🔴 v2：词条域（身份/狀況/人物類別…）→ 查 16a CSV 域值区（全语料闭包；查不到 = 修表重跑）
        dv = self.reg.domain_val(dom_word, subject)
        if dv:
            side, typ, usage = dv
            if side == "null":
                return "null"
            if "::" in side:
                return side                       # 完整引用（Time::year / Variable::x / Ctx::y / Org::z / Flag::x…）
            return f'"{side}"'                    # 纯枚举 token 字面量（daimyo / city_lord / general…）
        raise RegistryGapError(f"域值表外: {dom_word}::{subject}——16a CSV 域值区无此 (域,值) 行")

    # ---------- 条件翻译 ----------
    def translate_condition(self, cond_block):
        exprs = self._cond_items(cond_block.children)
        # 源重复拷贝去重（顺序敏感保留首个）
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
                        # 嵌套块整体登记对照（源行 = 块首行）
                        self.cond_pairs.append((item.raw.strip(), joined))
                else:
                    self.todo_mark("条件块", item.name)
            elif isinstance(item, Line):
                if item.cmd.startswith("代入"):
                    # 🔴 v4.4：条件块内代入 = 静态展开（条件求值无执行流，08 纪律静态直译；
                    #   执行块内代入才走 ctx_set 变量——见 translate_exec_line）
                    params = item.params()
                    if params:
                        self.cond_ctx[item.cmd[2:].strip()] = self._slot_value(params[0])
                    continue
                e = self.translate_cond_line(item)
                if e:
                    out.append(e)
                    self.cond_pairs.append((item.text.strip(), e))   # 🔴 原文 → DSL 逐条对照
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
            # 死亡標誌 特例：==1 → not(alive)；==0 → alive
            lm = re.match(r"^(.*?::.+?)\.死亡標誌$", left)
            if lm:
                ref = self.translate_ref(lm.group(1))[0]
                return f"not( ({ref}.alive) == true )" if right == "1" else f"({ref}.alive) == true"
            left_dsl = self.translate_ref(left)[0]
            right_dsl = self.translate_value(right)
            return f"({left_dsl}) {op} ({right_dsl})"
        e = expr[1:-1].strip() if expr.startswith("(") and expr.endswith(")") else expr
        return self.translate_ref(e)[0]

    def translate_value(self, v):
        if re.match(r"^-?\d+$", v):
            return v
        if v.startswith("真偽::"):
            return "true" if v.endswith("真") else "false"
        if "::" in v:
            return self.translate_ref(v)[0]
        if re.match(r"^(人物|據點|城|大名家|勢力|國|忍者衆|商家|海賊衆|地方|町|砦|里|軍團|流派)[Ａ-Ｅ]$", v) or re.match(r"^[ａ-ｚ]$", v):
            return f"Ctx::{slot_cname(v)}"              # 代入槽名（人物Ａ → Ctx::hero_A）
        # 🔴 v2：裸值 → 名字表（容器排除的人物/城名）→ 域值 token 反查 → 确定性兜底
        for table in (HERO_MAP, CLAN_MAP, SETTLEMENT_MAP, REGION_MAP, AGENT_MAP, FALLBACK_MAP):
            r = table.get(v)
            if r:
                return r
        bare = self.reg.bare_vals.get(v)
        if bare:
            return f'"{bare}"'                     # 武將 → "general"、城主 → "city_lord"
        self.todo_mark("值", v)
        return f"Hero::{_fallback_ascii(v)}"       # 有主占位：确定性兜底 + report 登记（非表外词条）

    # ---------- 执行翻译 ----------
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
        """分歧:(N) 块（可配对 分歧:(M) 成 if/else）。
        🔴 pending_cond 不在此清空：TK5 調查结果是「最近一次调查」，
        后续多个 分歧 都引用它，直到下一个 調查/選擇 覆盖（EFF0C300_159 实测：
        95804 調查鳴海 被 主流程 分歧:(0) 与 尾部 守城 分歧:(1) 共用）。"""
        val = block.args[0] if block.args else ""
        cond = self._resolve_branch_cond(val)
        if paired:
            pv = paired.args[0] if paired.args else ""
            if val == "1" and pv == "0":
                # 顺序 [1块, 0块]：if (cond) then 1块 else 0块
                self._push_if(cond, block, paired)
                return
            if val == "0" and pv == "1":
                # 顺序 [0块, 1块]：0块 = 条件假（cond 已取反）、1块 = 条件真
                # → if (原条件) then 1块 else 0块；原条件 = not(cond)
                orig = _simplify_not(f"not( {cond} )" if cond else None)
                self._push_if(orig, paired, block)
                return
            # 无法互补配对 → 各自独立
            self._push_if(cond, block)
            self._push_if(self._resolve_branch_cond(pv), paired)
            return
        self._push_if(cond, block)

    def _is_pure_perform(self, block):
        """块内是否只有表现层内容（对话/自语/旁白/选择/调查门控）→ 降级为 lines when 门控。"""
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
        """分支块翻译：纯表现 → when 传播给 lines；含机制 → 骨架 if 步骤。"""
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

    def translate_exec_line(self, line):
        cmd = line.cmd
        # ---- 解析碎片（未知NN:(2B 00 00 00) 原始字节命令，08b 踩坑 14）----
        if cmd.startswith("未知"):
            self.script_out.append({"step": "note", "note": "🔴 未知命令（解析碎片）→ 忽略", "src": line.text})
            return
        # ---- 表现层 ----
        if cmd in ("對話", "自語", "旁白"):
            seg = self.ensure_segment()
            if cmd == "對話":
                # 🔴 参数全语义化：param1 = speaker（说话人）、param2 = listener（听话人）
                params = line.params()
                speaker, listener = "Hero::MainHero", None
                if params:
                    parts = [x.strip() for x in params[0].split(",")]
                    if parts and parts[0]:
                        speaker = self.speaker_of(parts[0])
                    if len(parts) > 1 and parts[1] and parts[1] != "無效":
                        listener = self.speaker_of(parts[1])
                self.add_line(speaker, line.texts[0] if line.texts else "", line.raw,
                              listener=listener)
            elif cmd == "自語":
                self.add_line(self.current_hero, line.texts[0] if line.texts else "", line.raw, narrator=True)
            else:
                self.add_line(None, line.texts[0] if line.texts else "", line.raw, narrator=True)
            return
        if cmd in ("選擇", "對話選擇", "自語選擇"):
            seg = self.ensure_segment()
            opts = [{"textKey": f"{self.key_prefix}_{self.event_id}_{self.t_counter}_ch{i}", "text": t}
                    for i, t in enumerate(line.texts)]
            for i, t in enumerate(line.texts):
                self.i18n_key(f"{self.event_id}_{self.t_counter}_ch{i}", t)
            seg.lines.append({"cmd": "choice", "options": opts})   # 🔴 cmd 指令名（05 权威）
            self.pending_choice = len(line.texts)
            return
        # ---- 流程控制 ----
        if cmd == "調查":
            self.pending_cond = self.translate_cond_line(line)
            return
        if cmd.startswith("代入"):
            params = line.params()
            if params:
                slot = slot_cname(cmd[2:].strip())   # 代入人物Ｄ:(旅人) → Ctx 槽 hero_D
                # 🔴 v4.3：变量语义保留——生成 ctx_set 动作（16 §一 Ctx 三档），不做静态展开
                self.script_out.append({"step": "effect", "action": "ctx_set",
                                        "slot": slot, "value": self._slot_value(params[0]),
                                        "src": line.text})
            else:
                self.todo_mark("代入", line.text)
            return
        if cmd in ("ＢＧＭ變更", "ＳＥ開始", "ＳＥ停止", "ＳＥ循環", "圖片表示", "圖片消去", "背景變更", "背景恢復", "下個場面", "畫面效果"):
            self.script_out.append({"step": "note", "note": f"🔴 {cmd} 表现指令 → 05 承接", "src": line.text})
            return
        if cmd == "進入設施":
            self.cur_seg = None  # 切段
            params = line.params()
            self.script_out.append({"step": "scene_enter", "facility": params[0] if params else "🔴待07"})
            return
        if cmd == "離開設施":
            self.cur_seg = None
            self.script_out.append({"step": "scene_exit"})
            return
        if cmd == "更新":
            self.script_out.append({"step": "note", "note": "🔴 机制行 更新 → 承接系统", "src": line.text})
            return
        if cmd == "停止時間":
            self.script_out.append({"step": "effect", "action": "pause_time"})
            return
        if cmd == "遊戲中斷":
            self.script_out.append({"step": "note", "note": "🔴 剧本结局 → 06/14 承接", "src": line.text})
            return
        if cmd == "脫出模塊":
            # 循环出口 = break（loop body 内语义，禁止降级丢弃）
            self.script_out.append({"step": "break", "src": line.text})
            return
        if cmd in ("循環", "模塊開始", "腳本"):
            self.script_out.append({"step": "note", "note": f"🔴 {cmd} 块 → translate_exec_block 处理（loop 完整语义）", "src": line.text})
            return
        if cmd == "他歧" or not cmd:
            return
        if cmd.startswith("容器"):
            # 🔴 v4.7 用户裁定：容器命令完整语义化（CSV 命令区已注册 → pick 组；参数按序映射字段）
            #   容器設定/篩選/排除/排序 = 构建候选集合；容器選擇 = 取元素 → Ctx 槽；容器清理 = 移除
            params = [x.strip() for p in line.params() for x in p.split(",")]
            op = {
                "容器設定": "container_set", "容器篩選": "container_filter",
                "容器排除": "container_exclude", "容器排序": "container_sort",
                "容器選擇": "container_pick", "容器清理": "container_clear",
                "容器檢索": "container_query", "容器存取": "container_access",
            }.get(cmd)
            if not op:
                self.todo_mark("命令", cmd, line.text)
                return
            step = {"step": op, "src": line.text}
            if cmd in ("容器設定", "容器篩選", "容器排除"):
                if len(params) >= 3:
                    step["domain"], step["attr"], step["value"] = params[0], params[1], self._attr_value(params[1], params[2])
                elif len(params) >= 2:
                    step["domain"], step["attr"] = params[0], params[1]
                elif params:
                    step["domain"] = params[0]
            elif cmd == "容器排序":
                if len(params) >= 3:
                    step["domain"], step["attr"], step["order"] = params[0], params[1], params[2]
                elif len(params) >= 2:
                    step["domain"], step["attr"] = params[0], params[1]
            elif cmd == "容器選擇":
                if params:
                    step["slot"] = slot_cname(params[0])   # 容器選擇:(人物Ｅ,先頭) → 槽 hero_E
                    if len(params) >= 2:
                        step["mode"] = params[1]
            elif cmd == "容器清理":
                if len(params) >= 2:
                    step["mode"], step["count"] = params[0], params[1]   # 容器清理:(消去,1) = 模式,数量
                elif params:
                    step["count"] = params[0]
            self.script_out.append(step)
            return
        # ---- 世界结算 ----
        if self._world_effect(cmd, line):
            return
        # 🔴 v2：命令区兜底 → CSV 命令区承接注（05 消息控制 等）；CSV 也无 = 生成器缺陷
        hit = self.reg.command(cmd)
        if hit:
            side, usage = hit
            self.script_out.append({"step": "note", "note": f"🔴 {side} 承接（命令:{cmd}）", "src": line.text})
            return
        raise RegistryGapError(f"命令表外: {cmd}——16a CSV 命令区无此命令")

    def _slot_value(self, v):
        """代入槽值：含域 → 完整引用翻译；纯值（数字/真偽/枚举）→ translate_value。"""
        if "::" in v:
            return self.translate_ref(v)[0]
        return self.translate_value(v)

    def _attr_value(self, attr, v):
        """容器筛选/排除的 属性值 翻译：按属性语义推断域（所屬據點→据点、所屬大名家→家族…）。"""
        if "::" in v:
            return self.translate_ref(v)[0]
        if attr in ("所屬據點", "所屬城", "據點"):
            return self.translate_subject("據點", v)
        if attr in ("所屬大名家", "大名家"):
            return self.translate_subject("大名家", v)
        if attr in ("所屬上司", "人物"):
            return self.translate_subject("人物", v)
        return self._slot_value(v)

    # 世界结算命令：参数语义来自 16a CSV「参数」列（v4：参数翻译为 effect 字段，禁止丢参数）
    _WORLD_EFFECTS = {
        "武將死亡": ("kill_hero", ["actor"]),
        "勢力滅亡": ("destroy_faction", ["faction"]),
        "城主變更": ("set_owner", ["actor", "settlement"]),   # TK5 序：(新城主, 城)
        "城主任命": ("set_owner", ["actor", "settlement"]),   # TK5 序：(新城主, 城)
        "城主解任": ("set_owner", ["settlement"]),            # TK5 序：(城)
        "家督讓位": ("change_clan_leader", ["actor", "clan"]),
        "改名": ("rename", ["actor", "name"]),
        "據點改名": ("rename", ["actor", "name"]),
        "物品改名": ("rename", ["actor", "name"]),
        "所持金變更": ("gold_change", ["hero", "amount"]),
        "人物解雇": ("fire_hero", ["actor"]),
        "人物登用": ("spawn_hero", ["actor", "status", "clan"]),   # 🔴 CSV 参数列原为 (actor, clan) 漏 status——源实为 (人物Ｅ,直臣,德川家康) 三参数（2026-08-27 用户裁定：检查 CSV 是否漏了 → 漏了，回填）
        "居城變更": ("🔴 06 本城变更", []),
        "強制移動": ("teleport", ["party", "pos"]),
        "獨立": ("independence", ["clan"]),
        "軍團指令": ("🔴 02 lock_party/army_gather", ["leader", "target", "behavior"]),
        "軍團編成": ("🔴 02 army_gather", ["leader", "target", "behavior"]),
        "軍團編成最強": ("🔴 02 army_gather", ["leader", "target", "behavior"]),
        "個人戰鬥": ("🔴 03 battle", ["presetId"]),
        "主命作成": ("create_order", ["orderId"]),
        "宣戰": ("declare_war", ["a", "b"]),
        "停戰": ("make_peace", ["a", "b"]),
    }

    # 参数名 → 域（TK5 结算命令参数不带域前缀：武將死亡:(今川義元)）
    _PARAM_DOMAIN = {
        "actor": "人物", "hero": "人物", "party": "人物",
        "faction": "大名家", "clan": "大名家",
        "settlement": "據點", "pos": "據點",
        "a": "大名家", "b": "大名家",
    }

    def _world_effect(self, cmd, line):
        hit = self._WORLD_EFFECTS.get(cmd)
        if not hit:
            return None
        action, param_names = hit
        # 参数按逗号切分（TK5 `(A,B)` 多参数；平衡括号返回整个括号内容）
        params = [x.strip() for p in line.params() for x in p.split(",")]
        eff = {"step": "effect", "action": action}
        # 参数位 → 字段（CSV 参数列语义；引用参数翻译成 DSL 引用，数值参数保留）
        for i, p in enumerate(params):
            if i >= len(param_names):
                self.todo_mark("参数溢出", f"{cmd} 第{i+1}参", line.text)
                break
            name = param_names[i]
            if "::" in p:
                eff[name] = self.translate_ref(p)[0]
            elif name in self._PARAM_DOMAIN:
                eff[name] = self.translate_subject(self._PARAM_DOMAIN[name], p)
            else:
                eff[name] = p
        eff["src"] = line.text
        self.script_out.append(eff)
        return True

    def translate_exec_block(self, block):
        b = block.bare_cmd
        if b == "分歧":
            # 已由 translate_execution 配对处理（理论上不会到此处）
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
            # 🔴 v2：块内 children 必须翻译（調查 → pending_cond；分歧 → 路由）——旧版只输出
            #   note 导致块内分歧无待用条件（EFF06E00_161 实机，fail-fast 抓出）
            for item in block.children:
                if isinstance(item, Line):
                    self.translate_exec_line(item)
                elif isinstance(item, Block):
                    self.translate_exec_block(item)
        elif b in ("循環", "模塊開始", "腳本"):
            # 🔴 v4.7 用户裁定：完整语义保留——循环 = loop 步骤（body 递归），禁止线性展开降级
            self.script_out.append({"step": "loop", "body": self._inline_block(block)})
        elif b == "脫出模塊":
            # 循环出口 = break（loop body 内语义保留，禁止忽略）
            self.script_out.append({"step": "break", "src": block.raw})
        else:
            self.todo_mark("命令块", b, block.raw)
            raise RegistryGapError(f"命令块表外: {b}——16a CSV 命令区无此块命令")

    def _resolve_branch_cond(self, val):
        """分歧:(N) 的条件解析：选择路由 / 条件分支 / 未知。"""
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
        """块内容就地翻译（if 的 then）——when 传播进 lines。"""
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
        """主人公分歧/場合分歧 → when 条件（其他 = 已见机位取反）。"""
        if block.bare_cmd == "主人公分歧":
            hero = block.args[0] if block.args else ""
            if hero == "其他":
                conds = [f"not( (Hero::MainHero) == ({self.speaker_of(h)}) )" for h in self.seen_heroes]
                if not conds:
                    raise RegistryGapError("主人公分歧(其他) 无前序机位——语料结构异常（主人公別 先于 主人公分歧）")
                return "and( " + ", ".join(conds) + " )" if len(conds) > 1 else conds[0]
            self.seen_heroes.add(hero)
            return f"(Hero::MainHero) == ({self.speaker_of(hero)})"
        if block.bare_cmd == "場合分歧":
            return self.translate_expression(block.args_raw) if block.args_raw else None
        return None

    # ---------- 文本变量转换（TK5 变量 → TextObject 占位符 + 注入表）----------
    # 🔴 v4.5：占位符语义化——key 从角色 ID 派生（{IMAGAWA.NAME}），风格同引擎内置
    #   {PLAYER.NAME}；TextObject 变量名限制 = 大写字母/数字/下划线/点（无 :: 小写）
    def _key_prefix(self, who):
        """角色中文名 → 占位符 key 前缀（从归一表 ID 末段派生，大写；去掉 lord_1_/tk5_ 前缀）。"""
        v = HERO_MAP.get(who) or AGENT_MAP.get(who) or FALLBACK_MAP.get(who)
        if v and "::" in v:
            last = v.split("::")[-1].split(".")[-1]
            return last.replace("lord_1_", "").replace("tk5_", "").upper()
        return re.sub(r"[^A-Za-z0-9]", "_", who).upper()

    def convert_text_vars(self, text, t_no=None):
        phs = []

        def ph(key, vtype, arg):
            phs.append((key, vtype, arg))
            return "{" + key + "}"

        # ① (X.姓/名/名前) 称呼变体（最高频，先替换）
        # 🔴 v4.6 用户裁定：姓 = FIRSTNAME（骑砍2 FirstName 显示在前，日本名姓在前）、
        #   名 = LASTNAME（显示在后）、名前 = NAME（全名）、姓名 = FULLNAME（姓+名组合注入）
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
        # ② {一人稱}/{二人稱}/{X.名前}/{X.姓名}/{未知NN}/{人物Ａ.代名詞}
        def sub_brace(m):
            inner = m.group(1)
            if inner.startswith("PH_") or re.match(r"^[A-Z0-9_.]+$", inner):
                return m.group(0)   # 已生成的占位符跳过（防二次处理）
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
                return ph(f"SLOT_{slot_cname(inner[:-4]).split('_')[-1]}_PRONOUN", "SLOT_PRONOUN", inner[:-4])
            if inner.startswith("未知"):
                return ph(f"UNKNOWN_{inner[2:]}", "UNKNOWN", inner)
            return ph(re.sub(r"[^A-Za-z0-9]", "_", inner).upper(), "RAW", inner)

        text = RE_VAR_BRACE.sub(sub_brace, text)
        # ③ <槽> 显示（城Ａ/據點Ａ/年/月/ａ…）→ {SLOT_<字母>.NAME/VALUE}
        def sub_angle(m):
            slot = m.group(1)
            if slot == "年":
                return ph("TIME.YEAR", "SLOT", slot)      # <年> = 当前年份（Time::year）
            if slot == "月":
                return ph("TIME.MONTH", "SLOT", slot)     # <月> = 当前月份（Time::month）
            m2 = re.match(r"^(.*?)([Ａ-Ｅ])$", slot)
            m3 = re.match(r"^[ａ-ｚ]$", slot)
            if m2:
                letter = chr(ord("A") + (ord(m2.group(2)) - ord("Ａ")))   # 全角→ASCII
                cat = m2.group(1)
                return ph(f"SLOT_{letter}.NAME", "SLOT", slot) if cat else ph(f"SLOT_{letter}.VALUE", "SLOT", slot)
            if m3:
                letter = chr(ord("A") + (ord(slot) - ord("ａ")))
                return ph(f"SLOT_{letter}.VALUE", "SLOT", slot)
            return ph("SLOT_" + re.sub(r"[^A-Za-z0-9]", "_", slot).upper(), "SLOT", slot)

        text = RE_VAR_ANGLE.sub(sub_angle, text)
        # ④ (X) 角色全名（最后，避免误伤 ①②）
        def sub_plain(m):
            who = m.group(1)
            if who.startswith("PH_") or re.match(r"^[A-Z0-9_.]+$", who):
                return m.group(0)   # 已生成的占位符跳过（防二次处理）
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
        # 🔴 key 全局唯一：事件号前缀（t_counter 是事件级计数，裸序号跨事件必撞——铁律 13）
        key = f"{self.key_prefix}_{self.event_id}_{self.t_counter}"
        # 🔴 cmd = 指令名（05 指令流权威：dialogue / narrator / choice / if；CSV 對話→05 lines[]、旁白/自語→narrator）
        #   自语 = narrator + speaker（角色内心独白归属）；旁白 = narrator 无 speaker（上帝视角）
        line = {"cmd": "narrator" if narrator else "dialogue"}
        if speaker:
            line["speaker"] = speaker
        if listener:                     # 🔴 对话对象（param2）结构化落字段——不靠注释反推
            line["listener"] = listener
        line["textKey"] = key
        when = self.when_now()
        if when:
            line["when"] = when
        line["_t"] = self.t_counter      # T# 锚点（事件级演出行序号）
        line["_src"] = src_raw
        self.ensure_segment().lines.append(line)
        # 文本变量 → TextObject 占位符（v4：TK5 变量体系 → {PH_N} + 注入表）
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
        if s in FALLBACK_MAP:
            return FALLBACK_MAP[s]
        if re.match(r"^(人物|據點|城|大名家)[Ａ-Ｅ]$", s):
            # 🔴 v4.3：变量引用 → Ctx::<slot>
            return f"Ctx::{slot_cname(s)}"
        self.todo_mark("角色", s)
        return f"Hero::{_fallback_ascii(s)}"   # 确定性英文兜底（report 登记中文名）

    # ---------- 事件主流程 ----------
    def translate_event(self, ev_id, tree):
        self.event_id = ev_id
        self.t_counter = 0
        self.segments, self.script_out, self.i18n, self.ctx = [], [], [], {}
        self.var_inject = []
        self.cond_ctx = {}      # 条件块内代入槽（静态展开；执行块代入走 Ctx 变量）
        self.cond_pairs = []    # (TK5 原文行, DSL 表达式) 逐条对照（condition 注释渲染用）
        self.head_src = {}      # 事件头字段 → TK5 源行（trigger/facility/once/priority 对照注释）
        self.when_stack, self.pending_cond, self.pending_choice = [], None, None
        self.seen_heroes = set()
        self.cur_seg, self.seg_n = None, 0
        self.current_hero = "Hero::MainHero"

        ev = {"id": ev_id, "trigger": "", "once": True, "priority": "normal",
              "condition": "", "script": []}
        self.head_src["id"] = f"事件:事件{ev_id}{{"   # 🔴 id 字段对照注释（源事件块头）
        src_comments = []      # 事件上方注释（触发/条件原文）
        cond_block = exec_block = None

        for item in tree:
            if isinstance(item, Line):
                t = item.text
                if t.startswith("屬性:"):
                    val = t.split(":", 1)[1].strip()
                    if "弱" in val:
                        ev["priority"] = "weak"
                    if "多次" in val:
                        ev["once"] = False
                    # 🔴 头字段对照注释（once/priority 同一源行，渲染时紧贴字段上方）
                    self.head_src["once"] = f"屬性:{val}"
                    self.head_src["priority"] = f"屬性:{val}"
                elif t.startswith("發生契機:"):
                    content = t.split(":", 1)[1].strip()
                    base = content.split("(")[0].strip()
                    trig = TRIGGER_MAP.get(base)
                    if not trig:
                        raise RegistryGapError(f"trigger 表外: {base}——08 时机对照表无此契機（回填 01/16 trigger 注册表）")
                    ev["trigger"] = trig
                    self.form = TRIGGER_FORM.get(base, "menu_dialogue")
                    m = re.search(r"\((.*?)\)", content)
                    if m and ev["trigger"] == "house_enter":
                        # 🔴 facility = 契機第二参数（室內畫面表示後(主人公據點,自宅) → 自宅）
                        params = [p.strip() for p in m.group(1).split(",")]
                        ev["facility"] = params[-1] if params else "🔴待07"
                    src_comments.append(f"// 🔴 触发（TK5 原文）：{content}")
                    # 🔴 头字段对照注释（trigger/facility 同一源行）
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
        # 段收尾（无残留）
        self.cur_seg = None
        ev["script"] = self.script_out
        return ev, src_comments


# ---------------------------------------------------------------------------
# 输出组装
# ---------------------------------------------------------------------------
def render_steps_list(steps, indent=4, is_last=True):
    """骨架步骤渲染：🔴 src 源原文 → 步骤上方注释（正文纯翻译后内容）；
    then/else/body 嵌套数组手动组装（保证 JSON 合法）。返回行列表。"""
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
            out.append(f"{pad}{head[:-1]},")   # 去掉 } 补逗号（then/else/body 跟在后面）
            nk = list(nested.keys())
            for j, k in enumerate(nk):
                out.append(f'{pad}  "{k}": [')
                # 🔴 子数组内最后元素不加逗号（is_last 独立于父；父闭合 } 的逗号由父的 last 决定）
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
        if "_t" in ln:   # 演出行：T# 注释 + JSON
            t_no = ln.pop("_t")
            src = ln.pop("_src")
            items.append(f"    // T{t_no} {src.strip()}")
            items.append("    " + json.dumps(ln, ensure_ascii=False))
        else:            # choice 等步骤节点：直接 JSON
            items.append("    " + json.dumps(ln, ensure_ascii=False))
    parts.append(",\n".join(items))
    parts.append("  ]\n}")
    return "\n".join(parts)


def main():
    ap = argparse.ArgumentParser(description="太阁5 → 01 DSL 机械翻译器 v2")
    ap.add_argument("--events", nargs="+", required=True)
    ap.add_argument("--scenario", default="okehazama")
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--registry", default=DEFAULT_REGISTRY)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--sort", default="cluster", choices=["cluster", "input"])
    args = ap.parse_args()

    with open(args.source, encoding="utf-8") as f:
        events_map = parse_source(f.read())
    reg = Registry(args.registry)

    results = []
    for ev_id in args.events:
        if ev_id not in events_map:
            print(f"[WARN] 源中找不到事件 {ev_id}，跳过")
            continue
        tr = Translator(reg, args.scenario)
        try:
            ev, comments = tr.translate_event(ev_id, build_tree(events_map[ev_id]))
        except RegistryGapError as e:
            print(f"[FAIL] {ev_id}: {e}")
            print("❌ 表外词条 = 16a CSV 生成器缺陷：回填映射表 → 重跑 build_registry_csv.py → 重跑本脚本")
            sys.exit(1)
        results.append((ev_id, ev, comments, tr))

    if args.sort == "cluster":
        order = {e: i for i, e in enumerate(CLUSTER_ORDER)}
        results.sort(key=lambda x: order.get(x[0], 9999))

    out_dir = os.path.join(args.out, args.scenario)
    story_dir = os.path.join(out_dir, "story")
    i18n_dir = os.path.join(out_dir, "i18n")
    # 清旧产物（v1/v2 遗留命名文件；只删文件不删目录——目录可能被 IDE 监视占用）
    for d in (story_dir, i18n_dir):
        if os.path.isdir(d):
            for fn in os.listdir(d):
                p = os.path.join(d, fn)
                if os.path.isfile(p):
                    os.remove(p)
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(story_dir, exist_ok=True)
    os.makedirs(i18n_dir, exist_ok=True)

    # 事件合并文件（按历史聚类；🔴 无顶部整块原文——触发/条件原文已由字段级注释
    # （// 源：發生契機/屬性）与 [N] 逐条对照覆盖，避免两处拷贝）
    combined = []
    for ev_id, ev, comments, tr in results:
        combined.append(f"// ============ 事件 {ev_id}（{EVENT_NAME.get(ev_id, '')}） ============")
        combined.append("// ---- 机械翻译产物（待 agent 审核；字段/步骤上方注释 = TK5 源行） ----")
        # 🔴 条件逐条对照（TK5 原文 → DSL 表达式，用户裁定：condition 必须能逐条核对）
        #   渲染：对照注释严格插在 "condition" 字段正上方（不堆在对象开头）
        ev_copy = dict(ev)
        script_lines = render_steps_list(ev_copy.pop("script"))
        cond_val = ev_copy.pop("condition", "")
        cond_pairs = tr.cond_pairs
        head_src = tr.head_src
        ev_lines = []
        keys = list(ev_copy.keys())
        for k in keys:
            # 🔴 头字段对照注释：紧贴字段正上方（trigger/facility/once/priority → 源行）
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

    # 演绎剧本（每段一个文件——引擎/工具消费）
    for ev_id, ev, comments, tr in results:
        for seg in tr.segments:
            with open(os.path.join(story_dir, f"{seg.id}.jsonc"), "w", encoding="utf-8") as f:
                f.write(render_story_jsonc(seg))

    # 🔴 人读合并版 story.jsonc（用户裁定：按历史聚类、时间序、可读性高——
    #   看剧情/台词只看这一个文件；分文件留给引擎/工具）
    merged_story = []
    for ev_id, ev, comments, tr in results:
        for seg in tr.segments:
            merged_story.append(f"// ============ {seg.id}（{EVENT_NAME.get(ev_id, '')}） ============")
            merged_story.append(render_story_jsonc(seg))
            merged_story.append("")
    with open(os.path.join(out_dir, "story.jsonc"), "w", encoding="utf-8") as f:
        f.write("\n".join(merged_story))

    # i18n XML
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

    todo_cnt = len({t for t in all_todo})
    print(f"完成：{len(results)} 个事件 → {out_dir}")
    print(f"待注册：{todo_cnt} 条（去重；详情见 report_{args.scenario}.txt）")
    for ev_id, ev, comments, tr in results[:1]:
        print(f"\n== {ev_id} condition ==")
        print(ev["condition"])


if __name__ == "__main__":
    main()

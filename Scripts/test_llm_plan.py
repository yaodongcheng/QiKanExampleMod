#!/usr/bin/env python3
"""test_llm_plan.py -- LLM 密谋链路回归测试（计划生成 + 稳定性 + 意图正确性）

游戏外验证 LLM 链路：读 MCM 配置 → 构建与 C# PromptBuilder.BuildPlanPrompt 同构的
prompt → 发真实请求（reasoning_effort:none 关思考）→ 模拟 PlanValidator 校验 →
汇总分类正确率 / schema 合规率 / 耗时。

用法：
  python Scripts/test_llm_plan.py                       # 预设 8 命令（默认小场景）
  python Scripts/test_llm_plan.py --scene 30            # 场景规模（agents 数）
  python Scripts/test_llm_plan.py --cmd "干掉他"        # 单命令测试
  python Scripts/test_llm_plan.py --list                # 预设命令清单
  python Scripts/test_llm_plan.py --json "Debug/PlanExamples/A_DISTRACT.json"  # 校验示例计划 JSON（不发请求）

退出码：0 = 全部通过；1 = 有失败。
"""

import json
import os
import sys
import time
import urllib.request
# Windows 下强制 UTF-8 输出：默认 GBK 代码页会让重定向日志（regress_*.log）乱码（2026-08-08）
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

# ═══════════════════════════════════════════════════════════════
# 配置
# ═══════════════════════════════════════════════════════════════

MCM_PATH = os.path.expandvars(
    r"%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\LivingWorldNpcs\LivingWorldNpcsSettings_v1.json")

# 与 C# PlanVocab 一致的封闭词表 + 别名容错（与 C# ActionAliases 同步）
ALLOWED_ACTIONS = {
    "move_to", "follow", "stop_following", "order_attack", "knockout", "lead",
    "face", "look_at", "say_to", "wait", "emote", "make_noise", "signal_player",
    "steal_attempt", "give_item", "give_gold", "deliver_item", "shadow",
    "negotiate", "duel", "end_plan",
}
ACTION_ALIASES = {
    "attack": "order_attack", "move": "move_to", "stop": "stop_following",
    "speak": "say_to", "give": "give_item", "steal": "steal_attempt",
}

# reactions 封闭词表（与 C# ReactiveAgent.IsTriggerEvent / ExecuteReaction 同步）
REACTIVE_EVENTS = {
    "approach_by", "spoken_to", "asked_to_follow", "asked_to_stay",
    "player_suspicious_near", "see_crime", "combat_nearby", "left_post_seconds",
    "alone_with", "seen_speaking", "see_ally_killed",
}
REACTIVE_ACTIONS = {
    "listen", "consider", "respond", "refuse", "follow_for_a_bit", "investigate",
    "return_post", "stare", "alert_raise", "attack", "call_guards",
    "ignore", "relay_message", "pay", "hand_over_item", "flee",
}

# 封闭谓词词表（与 C# PlanVocab.Predicates 同步，§5.2）。
# 事件词（approach_by/see_crime 等 REACTIVE_EVENTS）只能出现在 reactions.event，禁止写进条件。
PREDICATES = {
    "distance", "seeing", "alert_phase", "following", "facing", "moving", "in_zone",
    "combat", "player_action", "time_since", "dead", "knocked_out", "count",
    "and", "or", "not",
}

# 封闭查询词表（与 C# PlanVocab.Queries 同步，§5.0 动态目标引用）
QUERIES = {
    "nearest_enemy", "all_in", "hidden_spot", "lure_spot", "stand_spot", "zone", "point",
}

# 场景锚点（真实游戏场景 = 语义 tag 探测，原生场景通常为空 → 锚点集为空；
# zone(名称) 只能引用锚点段出现的名称，未列出的区域 → 用 hidden_spot/lure_spot 动态找点或按 fail）
SCENE_ANCHORS = set()

# ═══════════════════════════════════════════════════════════════
# 场景快照生成（模拟 C# SceneSnapshot.ToPromptText）
# 🔴 2026-08-08 同步：实机已移除【场景可互动物件】段（121 个匿名 object 纯噪声，待按有意义 tag 重做）——
#     py 场景必须同步移除，回归 prompt 才与实机一致；STEAL 类命令在无物件段下应走澄清/CUSTOM。
# ═══════════════════════════════════════════════════════════════

def build_scene(agent_count, obj_count=4):
    lines = [f"【场景当前人员】（{agent_count} 人）"]
    roles = ["guard", "villager", "merchant", "tavernkeeper", "chief",
             "drunkard", "hero", None]
    dirs = ["东", "南", "西", "北"]
    faces = ["面朝玩家", "背对玩家", "侧身对着玩家"]
    states = ["站着", "蹲着", "巡逻中"]
    for i in range(agent_count):
        role = roles[i % len(roles)]
        r = f"[{role}] " if role else ""
        occ = role or "路人"
        lines.append(f"- {r}角色{i}（{occ}）：你{dirs[i % 4]}侧{3 + i * 2}米，"
                     f"{faces[i % 3]}，{states[i % 3]}"
                     f"{'（尽职尽责，坚守岗位）' if role == 'guard' else ''}")
    # 物件段已按实机移除（obj_count 参数保留签名，不再输出）
    # 无锚点段：模拟真实游戏（语义 tag 探测，原生场景通常为空）
    return "\n".join(lines)

# 固定场景（含全部目标角色，供预设命令引用）
# 注意：无【场景区域锚点】段——模拟真实游戏（SceneSnapshot Zones 靠语义 tag 探测，原生场景通常为空）
# 注意：无【场景可互动物件】段——实机已移除（2026-08-08 同步）
FIXED_SCENE = """【场景当前人员】（9 人）
- [player] 玩家：你身旁1米，面朝玩家，站着
- [guard] 帝国守卫（守卫）：你东侧8米，背对玩家，站着（尽职尽责，坚守岗位）
- [chief] 村长（村长/乡绅）：你南侧15米，面朝玩家，站着
- [tavernkeeper] 酒馆老板（酒馆老板）：你西侧12米，侧身对着玩家，站着（八面玲珑）
- [merchant] 商人（商人）：你北侧10米，面朝玩家，站着（精于算计）
- [drunkard] 醉汉（醉醺醺）：你东侧5米，坐着（醉醺醺）
- [foe] 可疑黑衣人（路人）：你北侧18米，背对玩家，巡逻中
- [contact] 瘦高个（路人）：你南侧20米，站着
- [villager] 村民（村民）：你西南侧9米，站着"""

# ═══════════════════════════════════════════════════════════════
# prompt 构建（与 C# BuildPlanPrompt 逐段同构）
# ═══════════════════════════════════════════════════════════════

# ═══════════════════════════════════════════════════════════════
# prompt 文本单一事实源（与 C# 同源）：静态块读 std_LivingWorldNpcs_prompts.xml 的 LWN_plan_* key。
# 词表动态拼接段（动作/谓词/reactions）保留在下方代码（check_vocab_sync.py 校验同步）。
# 改 prompt 文本 → 只改 XML（ModuleData/Languages/CNs/std_LivingWorldNpcs_prompts.xml），
# 改完跑 validate_localization.py + LLM 回归；XML 加载失败 → 缺段降级不崩（铁律 1 精神）。
# ═══════════════════════════════════════════════════════════════
import xml.etree.ElementTree as _ET


def _load_plan_prompts():
    xml_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                            "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_prompts.xml")
    if not os.path.exists(xml_path):
        print(f"警告: prompt XML 不存在: {xml_path}（prompt 静态块将缺失）")
        return {}
    try:
        root = _ET.parse(xml_path).getroot()
        out = {}
        for node in root.iter("string"):
            kid = node.get("id") or ""
            if kid.startswith("LWN_plan_"):
                out[kid] = (node.get("text") or "").replace("\\n", "\n")
        return out
    except Exception as e:
        print(f"警告: prompt XML 解析失败: {e}")
        return {}


_PLAN_PROMPTS = _load_plan_prompts()

# 意图列表（词表动态拼接段，不移 XML；与 C# 枚举拼接同源，check_vocab_sync.py 校验）
INTENT_TABLE = ("""【意图分类】你只能从以下意图中选择 intent_type（严禁创造未定义类型）：
FOLLOW 跟我走 / WAIT 在这等我 / STOP 住手 / ATTACK 干掉他 / GUARD 护住他/条件参战 / BRING 请人到面前 / DISTRACT 引开某人 / LOOKOUT 望风 / DELIVER 传话/送物 / ENGAGE 缠住/拖住 / DRIVE_AWAY 赶走 / STEAL 偷物/扒窃 / FORMATION 站位 / SPAR 切磋 / FETCH 取物 / PURCHASE 购买 / KNOCKOUT 打晕 / GUIDE 带路 / SCOUT 侦察 / TALK_TO 交涉 / FIND 找人 / SHADOW 跟踪 / COLLECT 讨债 / DUEL 比武 / ANNIHILATE 清剿（把某个区域的所有人杀掉/打晕，批量战斗） / COMMOTION 闹出动静 / CUSTOM 词表外（现实做不到的动作：翻译/施法/修装备等 → 诚实拒绝）"""
    + "\n" + _PLAN_PROMPTS.get("LWN_plan_intent_fewshot", ""))

# 词表动态拼接段（不移 XML；与 C# BuildGrammar 同源，check_vocab_sync.py 校验）
_GRAMMAR_VOCAB = """【动作词表】move_to/follow/stop_following/order_attack/knockout/lead/face/look_at/say_to/wait/emote/make_noise/signal_player/steal_attempt/give_item/give_gold/deliver_item/shadow/negotiate/duel/end_plan
【谓词词表】distance/seeing/alert_phase/following/facing/moving/in_zone/combat/player_action/time_since/dead/knocked_out/count/and/or/not（修饰：sustained_s、was）

【reactions 封闭词表（事件/动作严禁自创）】
事件 event 只能写：approach_by / spoken_to / asked_to_follow / asked_to_stay / player_suspicious_near / see_crime / combat_nearby / left_post_seconds / alone_with / seen_speaking / see_ally_killed（注意是 approach_by，不是 approached_by）
动作 action 只能写：listen / consider / respond / refuse / follow_for_a_bit / investigate / return_post / stare / alert_raise / attack / call_guards / ignore / relay_message / pay / hand_over_item / flee（flee = 看到同伴被杀等恐慌情境下跑离现场；respond = 被搭话时开口回应，台词实时生成）"""

# 静态块（纪律/模板/示范/质量/执行）全部读 XML（单一事实源）
GRAMMAR = (
    _PLAN_PROMPTS.get("LWN_plan_rules", "")
    + "\n\n" + _GRAMMAR_VOCAB
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_template_bring", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_template_loop", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_example_fail", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_example_respond", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_example_keep", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_example_result", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_quality", "")
    + "\n\n" + _PLAN_PROMPTS.get("LWN_plan_exec", "")
)

# 预设命令：命令 → 期望意图（CLARIFY = 歧义应澄清；CUSTOM = 词表外应拒绝）
PRESET_COMMANDS = [
    ("我想偷那箱子，有人盯着怎么办？", "CLARIFY", "偷箱子（场景无物件段 → 按纪律 14 无目标应澄清，2026-08-08 随场景同步调整）"),
    ("请村长到我面前来", "BRING", "请村长"),
    ("去把那个守卫的钱袋摸来", "STEAL", "扒守卫（目标 [guard] 在场景中，无物件段不影响）"),
    ("帮我望风，来人了叫我", "LOOKOUT", "望风"),
    ("干掉他", "ATTACK", "攻击（歧义）"),
    ("带我去河边", None, "带路（场景无河边锚点 → 澄清/诚实报告/运行时失败兜底，看行为）"),
    ("把那醉鬼从我身边赶走", "DRIVE_AWAY", "赶走"),
    ("告诉他，我在老地方等他", "DELIVER", "传话"),
    ("缠住掌柜，我去翻那保管箱", "ENGAGE", "缠住"),
    ("去找酒馆老板聊聊，探探口风", "TALK_TO", "找人提前失败 badcase（纪律 19）：目标在场应 move_to 去找；seeing-false contingency 无 sustained_s = 违规"),
    ("帮我把这封信翻译成拉丁文", "CUSTOM", "词表外拒绝"),
    ("把全村人都杀了", "ANNIHILATE", "清剿（模型可能自主拒绝）"),
]


# 世界观段（模拟 C# Settings.WorldDescription 实机默认文本；内容包注入的 flavor 不模拟）
WORLD_DESC = "骑马与砍杀2 卡拉迪亚中世纪世界"

# 身份段（模拟 C# BuildPersona 实机格式：你是 随从名，玩家名 的随从——2026-08-08 起主人=玩家名）
PERSONA = ('你是 "铁匠"沃泰尔，奥斯帕克 的随从。说话简短、恭敬、务实，像游戏里的随从。')


def build_prompt(scene, command):
    # 与 C# BuildPlanPrompt 逐段同构（2026-08-08 同步）：
    # 世界观 → 场景 → 身份 → 命令 →（历史 py 不模拟）→ 意图表 → 语法
    return ("【世界观】" + WORLD_DESC + "\n\n【当前场景】\n" + scene
            + "\n\n【你的身份】\n" + PERSONA
            + "\n\n【玩家命令】\n" + command + "\n"
            + INTENT_TABLE + "\n" + GRAMMAR)


# ═══════════════════════════════════════════════════════════════
# PlanValidator 模拟（与 C# PlanValidator 同规：S1 跳转存在 / S4 id 唯一 /
# fallbacks 双层 / 动作词表 / say_to 字段名）
# ═══════════════════════════════════════════════════════════════

def validate_plan(parsed):
    issues = []
    it = parsed.get("intent") or {}
    # 兼容两种输入：PlanResponse 壳（intent/plan 嵌套）与裸 plan 对象（顶层即 plan）
    pl = parsed.get("plan") or (parsed if "steps" in parsed or "loop" in parsed else None)
    if not pl:
        return issues, it, None
    steps = pl.get("steps") or []
    fbs = pl.get("fallbacks") or []
    loop_steps = ((pl.get("loop") or {}).get("steps") or []) if isinstance(pl.get("loop"), dict) else []
    # 统一遍历对象：主链 + 预案 + 循环段（与 C# IterSteps 同步——loop 内部同样受全部校验）
    all_steps = steps + [x for fb in fbs for x in fb] + loop_steps
    if fbs and isinstance(fbs[0], dict):
        issues.append("fallbacks 单层（应为数组的数组）")
        fbs = [fbs]
    ids = []
    for s in all_steps:
        if isinstance(s, dict) and s.get("id"):
            ids.append(s["id"])
    if len(ids) != len(set(ids)):
        issues.append("重复 id")
    for s in all_steps:
        if not isinstance(s, dict):
            continue
        action = s.get("action")
        if action in ACTION_ALIASES:
            action = ACTION_ALIASES[action]
        if action not in ALLOWED_ACTIONS:
            issues.append(f"未知动作 {action}")
        if s.get("action") == "say_to" and "text" not in s and "outline" not in s:
            issues.append("say_to 缺 text 或 outline 字段")
        if s.get("action") == "say_to" and "outline" in s:
            ol = s["outline"]
            if not isinstance(ol, list) or not all(isinstance(x, str) and x.strip() for x in ol):
                issues.append("outline 必须是字符串数组（对话模式走向段）")
            elif not (2 <= len(ol) <= 5):
                issues.append(f"outline 段数必须 2-5，实际 {len(ol)}")
        if s.get("action") == "say_to" and s.get("ask") and s["ask"] != "follow":
            issues.append(f"ask 只允许 follow，实际 {s['ask']}")
        until = s.get("until")
        if until is not None and not isinstance(until, dict):
            issues.append(f"until 必须是对象，实际是字符串 {until}")
        for f in ("on_timeout", "on_success"):
            t = s.get(f)
            if t is not None and not isinstance(t, str):
                issues.append(f"{f} 必须是字符串，实际是 {type(t).__name__}")
            elif t and not t.startswith("@") and t not in ids:
                issues.append(f"悬空跳转 {f}={t}")
        for e in (s.get("on_event") or []):
            if isinstance(e, dict) and e.get("then") is not None:
                if not isinstance(e["then"], str):
                    issues.append(f"on_event.then 必须是字符串，实际是 {type(e['then']).__name__}")
                elif not e["then"].startswith("@") and e["then"] not in ids:
                    issues.append(f"悬空 on_event {e['then']}")
        until = s.get("until")
        if isinstance(until, dict) and until.get("type") == "time_since":
            sid = until.get("step_id")
            if sid and sid not in ids:
                issues.append(f"悬空 time_since {sid}")
    for c in (pl.get("contingencies") or []):
        if isinstance(c, dict) and c.get("then") is not None:
            if not isinstance(c["then"], str):
                issues.append(f"contingency.then 必须是字符串（跳转目标 id 或 @指令），实际是 {type(c['then']).__name__}")
            elif not c["then"].startswith("@") and c["then"] not in ids:
                issues.append(f"悬空 contingency {c['then']}")
    # 纪律 19（2026-08-08 实机 badcase："找人" 79ms 跳失败收尾）：
    # seeing-false 类 contingency（掉线/丢目标检测）必须带 sustained_s 防抖——
    # 无防抖 → 目标转头/视线短暂丢失即瞬间触发 → 整个计划被误杀。
    for c in (pl.get("contingencies") or []):
        if isinstance(c, dict) and isinstance(c.get("when"), dict):
            w = c["when"]
            if w.get("type") == "seeing" \
                    and str(w.get("op") or "true").lower() == "false" \
                    and not (w.get("sustained_s") or 0):
                issues.append(f"contingency seeing-false 无 sustained_s 防抖（纪律 19：视线瞬时丢失误杀计划）")
    # 纪律 20（2026-08-08 实机 badcase 二连：following(player,self,false) 79ms 开局触发）：
    # following(A,B)=A 跟着 B；计划启动即停止跟随 → following-false 恒成立必触发 → 违规。
    for c in (pl.get("contingencies") or []):
        if isinstance(c, dict) and isinstance(c.get("when"), dict):
            w = c["when"]
            if w.get("type") == "following" \
                    and str(w.get("op") or "true").lower() == "false":
                issues.append(f"contingency following-false 恒成立必触发（纪律 20：计划启动即停止跟随）")
    # 失败跳转指向 success 收尾 = 谎报成功（只查"条件等待"步骤：带 until 的 wait/move_to 超时 =
    # 条件没达成 = 失败；纯时长等待（wait seconds，无 until）超时 = 等够了 = 完成，不算谎报）
    id2step = {}
    for st in all_steps:
        if isinstance(st, dict) and st.get("id"):
            id2step[st["id"]] = st
    def is_condition_wait(st):
        return bool(st.get("until")) or (st.get("action") == "move_to" and st.get("until"))
    for st in all_steps:
        if not isinstance(st, dict):
            continue
        if not is_condition_wait(st):
            continue
        for f, t in (("on_timeout", st.get("on_timeout")), ("on_event", None)):
            if isinstance(t, str) and not t.startswith("@"):
                target = id2step.get(t)
                if target and target.get("action") == "end_plan" and target.get("result") == "success":
                    issues.append(f"{f} 指向 success 收尾 {t}（条件等待失败路径谎报成功）")
        for e in (st.get("on_event") or []):
            if isinstance(e, dict) and isinstance(e.get("then"), str) and not e["then"].startswith("@"):
                target = id2step.get(e["then"])
                if target and target.get("action") == "end_plan" and target.get("result") == "success":
                    issues.append(f"on_event 指向 success 收尾 {e['then']}（条件等待失败路径谎报成功）")
    # zone/point 引用纪律：引用的名称必须在场景锚点段出现（场景无锚点 → 引用 zone = 不合规）
    for st in all_steps:
        if not isinstance(st, dict):
            continue
        t = st.get("target")
        q = None
        if isinstance(t, dict) and t.get("query"):
            q = t["query"]
        elif isinstance(t, str) and t.startswith("zone("):
            q = t
        if q and q.startswith("zone("):
            name = q[q.index("(") + 1:q.rindex(")")].strip('"\'')
            if name not in SCENE_ANCHORS:
                issues.append(f"zone/point 引用场景锚点外的名称 {name}（锚点: {sorted(SCENE_ANCHORS)}）")
    # 谓词词表校验（与 C# ValidateCondition 同步）：until/when/goal/triggers/contingencies/loop.until
    # 的 type 必须在 PREDICATES；事件词（approach_by 等）写进条件 = 模型把事件当谓词用 → 不合规
    def check_condition(c, where):
        if not isinstance(c, dict):
            return
        t = c.get("type")
        if not t or t not in PREDICATES:
            issues.append(f"{where} 未知谓词 {t}")
            return
        subs = c.get("conditions")
        if t in ("and", "or") and not isinstance(subs, list):
            issues.append(f"{where} {t} 缺 conditions 数组")
        if isinstance(subs, list):
            for sub in subs:
                check_condition(sub, where)
        if t == "not" and isinstance(subs, list) and len(subs) != 1:
            issues.append(f"{where} not 应只有 1 个子条件")
    for st in all_steps:
        if not isinstance(st, dict):
            continue
        if st.get("until") is not None:
            check_condition(st["until"], f"step {st.get('id')} until")
        if st.get("when") is not None:
            check_condition(st["when"], f"step {st.get('id')} when")
    for c in (pl.get("contingencies") or []):
        if isinstance(c, dict) and c.get("when") is not None:
            check_condition(c["when"], "contingency when")
    if pl.get("goal") is not None:
        check_condition(pl["goal"], "goal")
    for tr in (pl.get("triggers") or []):
        if isinstance(tr, dict) and tr.get("when") is not None:
            check_condition(tr["when"], "trigger when")
    lp = pl.get("loop")
    if isinstance(lp, dict) and lp.get("until") is not None:
        check_condition(lp["until"], "loop until")
    # reactions 词表校验（自创事件/动作 → 该 NPC 会失聪/无操作，按不合规记）
    for rp in (parsed.get("reactions") or []):
        if not isinstance(rp, dict):
            continue
        for rs in (rp.get("responses") or []):
            if not isinstance(rs, dict):
                continue
            ev = rs.get("event")
            if ev and ev not in REACTIVE_EVENTS:
                issues.append(f"reactions 自创事件 {ev}（不在触发词表）")
            for rr in (rs.get("reactions") or []):
                if isinstance(rr, dict) and rr.get("action") and rr["action"] not in REACTIVE_ACTIONS:
                    issues.append(f"reactions 自创动作 {rr['action']}")
    return issues, it, pl


# ═══════════════════════════════════════════════════════════════
# 请求
# ═══════════════════════════════════════════════════════════════

def load_config():
    if not os.path.exists(MCM_PATH):
        print(f"错误: MCM 配置文件不存在: {MCM_PATH}")
        sys.exit(2)
    cfg = json.load(open(MCM_PATH, encoding="utf-8-sig"))
    base = (cfg.get("LLMBaseUrl") or "").rstrip("/")
    key = cfg.get("LLMApiKey") or ""
    model = cfg.get("LLMModel") or ""
    if not (base and key and model):
        print("错误: MCM 未配置 LLM 三字段（LLMBaseUrl/LLMApiKey/LLMModel）")
        sys.exit(2)
    return base, key, model


def call_llm(base, key, model, prompt, timeout=120, temperature=0.4, reasoning="none"):
    body = {
        "model": model,
        "messages": [{"role": "system", "content": prompt}],
        "temperature": temperature,
        "max_tokens": 4000,
        "response_format": {"type": "json_object"},
        "reasoning_effort": reasoning,  # none=关思考（默认，实测 25s→3.5s）；low/medium=开思考（质量/延迟权衡）
    }
    req = urllib.request.Request(
        base + "/chat/completions",
        data=json.dumps(body).encode("utf-8"),
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            content = data["choices"][0]["message"]["content"]
            elapsed = time.time() - t0
            parsed = json.loads(content)
            return parsed, elapsed, None
    except urllib.error.HTTPError as e:
        return None, 0, f"HTTP {e.code}: {e.read().decode('utf-8')[:200]}"
    except Exception as e:
        return None, 0, f"{type(e).__name__}: {e}"


def run_case(base, key, model, command, expected, label, scene=None, verbose=True, temperature=0.4, reasoning="none"):
    scene = scene or FIXED_SCENE
    prompt = build_prompt(scene, command)
    parsed, elapsed, err = call_llm(base, key, model, prompt, temperature=temperature, reasoning=reasoning)
    if err:
        print(f"[FAIL] {label}: {err}")
        return False
    issues, it, pl = validate_plan(parsed)
    got = it.get("intent_type")
    questions = parsed.get("questions") or []
    steps_n = len((pl or {}).get("steps") or []) if pl else 0

    if expected == "CUSTOM":
        ok = got == "CUSTOM" and pl is None
    elif expected is None:
        # 单命令模式/看行为：分类成功且（计划有效 或 诚实拒绝 或 走了澄清）且 schema 合规
        ok = bool(got) and (pl is not None or got == "CUSTOM" or bool(questions)) and not issues
    else:
        # 分类正确即过（走了澄清轮 = 设计内行为，不判失败）
        ok = got == expected and (pl is not None or bool(questions)) and not issues

    if verbose:
        mark = "OK" if ok else "ISSUE"
        print(f"[{mark}] {label}  intent={got}(期望{expected})  "
              f"steps={steps_n}  澄清={len(questions)}  {elapsed:.1f}s")
        if issues:
            print(f"       问题: {'; '.join(issues)}")
        if pl and pl.get("summary"):
            print(f"       摘要: {pl.get('summary')}")
        if not pl and parsed.get("reply"):
            print(f"       回复: {parsed.get('reply', '')[:80]}")
    return ok


def validate_example_json(path):
    """校验示例计划 JSON（不发请求）——与 validate_plan_json.py 互补。"""
    obj = json.load(open(path, encoding="utf-8"))
    issues, it, pl = validate_plan(obj)
    if issues:
        print(f"[FAIL] {path}: {'; '.join(issues)}")
        return False
    print(f"[OK] {path}: intent={it.get('intent_type')}  "
          f"steps={len((pl or {}).get('steps') or [])}")
    return True


# ═══════════════════════════════════════════════════════════════
# main
# ═══════════════════════════════════════════════════════════════

def main():
    args = sys.argv[1:]
    if "--list" in args:
        for cmd, expected, label in PRESET_COMMANDS:
            print(f"  {cmd}  →  期望 {expected}（{label}）")
        return 0

    if "--json" in args:
        idx = args.index("--json")
        path = args[idx + 1] if idx + 1 < len(args) else None
        if not path:
            print("用法: --json <示例 JSON 路径>")
            return 2
        return 0 if validate_example_json(path) else 1

    base, key, model = load_config()
    temperature = 0.4
    if "--temp" in args:
        temperature = float(args[args.index("--temp") + 1])
    reasoning = "none"
    if "--reasoning" in args:
        reasoning = args[args.index("--reasoning") + 1]
    print(f"模型: {model}  端点: {base}  key: ***{key[-4:]}  温度: {temperature}  思考: {reasoning}")
    print(f"（reasoning_effort: none — 关思考模式）")

    scene_arg = None
    if "--scene" in args:
        idx = args.index("--scene")
        if idx + 1 < len(args):
            try:
                scene_arg = build_scene(int(args[idx + 1]))
            except ValueError:
                print("错误: --scene 需要整数")
                return 2

    if "--cmd" in args:
        idx = args.index("--cmd")
        if idx + 1 < len(args):
            print("\n=== 单命令测试 ===")
            ok = run_case(base, key, model, args[idx + 1], None, "单命令",
                          scene=scene_arg, temperature=temperature)
            return 0 if ok else 1
        print("用法: --cmd <命令文本>")
        return 2

    print(f"\n=== 预设命令回归（{len(PRESET_COMMANDS)} 个）===")
    results = [run_case(base, key, model, cmd, exp, f"{label}", scene=scene_arg, temperature=temperature)
               for cmd, exp, label in PRESET_COMMANDS]
    passed = sum(1 for r in results if r)
    print(f"\n--- 汇总: 通过 {passed}/{len(results)} "
          f"（分类正确率 {100 * passed // len(results)}%）---")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())

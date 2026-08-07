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
    "listen", "consider", "refuse", "follow_for_a_bit", "investigate",
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

# 场景锚点（真实游戏场景 = 语义 tag 探测，原生场景通常为空 → 锚点集为空；
# zone(名称) 只能引用锚点段出现的名称，未列出的区域 → 用 hidden_spot/lure_spot 动态找点或按 fail）
SCENE_ANCHORS = set()

# ═══════════════════════════════════════════════════════════════
# 场景快照生成（模拟 C# SceneSnapshot.ToPromptText）
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
    lines.append(f"【场景可互动物件】（{obj_count} 个）")
    for i in range(obj_count):
        lines.append(f"- 箱子{i}（chest）：你东侧{5 + i}米")
    # 无锚点段：模拟真实游戏（语义 tag 探测，原生场景通常为空）
    return "\n".join(lines)

# 固定场景（含全部目标角色，供预设命令引用）
# 注意：无【场景区域锚点】段——模拟真实游戏（SceneSnapshot Zones 靠语义 tag 探测，原生场景通常为空）
FIXED_SCENE = """【场景当前人员】（9 人）
- [player] 玩家：你身旁1米，面朝玩家，站着
- [guard] 帝国守卫（守卫）：你东侧8米，背对玩家，站着（尽职尽责，坚守岗位）
- [chief] 村长（村长/乡绅）：你南侧15米，面朝玩家，站着
- [tavernkeeper] 酒馆老板（酒馆老板）：你西侧12米，侧身对着玩家，站着（八面玲珑）
- [merchant] 商人（商人）：你北侧10米，面朝玩家，站着（精于算计）
- [drunkard] 醉汉（醉醺醺）：你东侧5米，坐着（醉醺醺）
- [foe] 可疑黑衣人（路人）：你北侧18米，背对玩家，巡逻中
- [contact] 瘦高个（路人）：你南侧20米，站着
- [villager] 村民（村民）：你西南侧9米，站着
【场景可互动物件】（4 个）
- 箱子（chest）：你东侧9米
- 酒桶（barrel）：你西侧8米
- 保管箱（chest）：你东侧14米
- 大门（door）：你东侧25米"""

# ═══════════════════════════════════════════════════════════════
# prompt 构建（与 C# BuildPlanPrompt 逐段同构）
# ═══════════════════════════════════════════════════════════════

INTENT_TABLE = """【意图分类】你只能从以下意图中选择 intent_type（严禁创造未定义类型）：
FOLLOW 跟我走 / WAIT 在这等我 / STOP 住手 / ATTACK 干掉他 / GUARD 护住他/条件参战 / BRING 请人到面前 / DISTRACT 引开某人 / LOOKOUT 望风 / DELIVER 传话/送物 / ENGAGE 缠住/拖住 / DRIVE_AWAY 赶走 / STEAL 偷物/扒窃 / FORMATION 站位 / SPAR 切磋 / FETCH 取物 / PURCHASE 购买 / KNOCKOUT 打晕 / GUIDE 带路 / SCOUT 侦察 / TALK_TO 交涉 / FIND 找人 / SHADOW 跟踪 / COLLECT 讨债 / DUEL 比武 / ANNIHILATE 清剿（把某个区域的所有人杀掉/打晕，批量战斗） / COMMOTION 闹出动静 / CUSTOM 词表外（现实做不到的动作：翻译/施法/修装备等 → 诚实拒绝）
【意图判定基准（few-shot）】
"干掉他/杀了他/解决他/做了他" → ATTACK（要动手见血）
"引开/骗走/调虎离山/把某人支开" → DISTRACT（不交手，只转移注意力）
"缠住/拖住/别让他走/稳住他" → ENGAGE（对话/周旋，不让对方脱身）
"偷/摸/拿那东西" → STEAL；"请/叫某人过来" → BRING；"望风/盯梢/来人了叫我" → LOOKOUT
"带我去/领我去" → GUIDE；"赶走/轰走/撵走" → DRIVE_AWAY；"传话/告诉他" → DELIVER
"去和X切磋/比试，试他深浅" → DUEL（随从与第三方比武，非致死，回报评估）；"和我切磋/和我比划" → SPAR（玩家是互动对象）
"订房/安排事务/订酒菜" → TALK_TO（交涉安排）；"买/购买某物" → PURCHASE（随从花钱买货带回来）；"讨债/要钱/收账" → COLLECT（把钱要回来）
【复合命令判定（重要：按最终目的分类，不是第一个动作）】
"引开/骗走 X 打晕/干掉/放倒" → KNOCKOUT/ATTACK（引开只是手段，最终目的是击晕/击杀）
"我引开/缠住/望风，你去偷/翻/动手" → STEAL 等（"我…你…" = 角色分工，随从执行的是后半句的主动作）
"X 敢还手/动手/攻击，你就上/参战" → GUARD（条件参战：平时压阵，对方动手才打）
"先…然后…/顺便…/同时…" → 按最终目的分类
"在这等我，去那边看看/打听…" → SCOUT（后半句的任务才是命令主体）
【指代纪律】命令里的"他/她/它/那东西/那个人"若场景存在多个候选或指代不明 → 必须 questions 澄清（列候选位置让玩家选），禁止自行挑一个；"跟他走"无明确指代也须澄清（除非场景只有唯一可跟随者）。"""

GRAMMAR = """【计划语法纪律】
1. 只允许已定义 action/谓词。2. 每步有唯一 id。3. 每个跳转必须指向真实存在的步骤 id 或 @abort_gracefully。4. fallbacks 必须是数组的数组 [[预案1],[预案2]]，每个预案第一条 id 必须被至少一个跳转引用，只能跳预案第一条。5. 顺利路径走 steps，失败/意外走 fallbacks/contingencies。6. 每个失败出口必须落到跳转/超时路径。7. 目标引用只能用场景角色/物件或 query（nearest_enemy(self)/all_in(zone)/lure_spot(watch_point,12)/hidden_spot(self,15)/stand_spot(target,anchor)/zone(名称)/point(描述)）。8. say_to 前必须有 move_to。9. 安全窗口加 sustained_s（窗口3s/离岗5s，上限30s）。10. 只基于场景可见事实。11. say_to 台词字段是 text（不是 content）；wait 退出条件写 until（必须是对象 {"type":...}，禁止写成字符串）；ask 只允许写 "follow"。12. 成功收尾与失败收尾是两个不同节点：主链末尾放 end_plan result="success"（成功收尾，report 成功台词）；on_timeout / on_event 是失败路径，只能指向 result="fail" 的 end_plan 或重试预案，禁止指向 success 收尾。13. wait 步骤的 on_timeout 语义 = "条件没等到"（守卫没跟来/目标没到位），是失败路径，必须指向 fail 收尾；只有计划真正完成才走 success 收尾。14. 地点诚实纪律：命令提到的地点（河边/城堡/张员外家等）若场景快照里没有该角色/物件/锚点 → 不编造带路，用 questions 澄清或 CUSTOM 诚实说"不知道在哪"；只有场景里存在的实体（角色/物件/锚点）才能作为 target。15. 保持型命令（望风/压阵/盯梢/缠住/跟随/闹事引众/"别让他走"这类持续到玩家叫停的任务）用无限 wait（不写 seconds/until/timeout）或无限 follow 表达保持，用 triggers 表达事件报告，不设 goal，结束由玩家按停止键；禁止把"等 N 秒没人来/没动静"写成 success 收尾（望风不是看一会就收工，是持续待命）。**任务型 vs 保持型的区别**：有成功时刻（请到人/偷到物/杀死目标）→ goal + 主链 end_plan success 收尾；无成功时刻（望风/缠住/压阵/跟随/闹事）→ 保持 + 玩家叫停——缠住/拖住/闹事是保持型：达成后进入保持期（GOAL→MAINTAIN），**不因"缠住了/引来了"就 success 收尾**；"跟我来/跟着我" = 无限 follow（{"action":"follow","target":"player"}），不是走到玩家身边就收尾。16. until/when/goal/triggers/contingencies 的条件里只能写谓词词表（distance/seeing/following/combat/in_zone/count/time_since 等）；approach_by/spoken_to/see_crime/left_post_seconds/player_suspicious_near/**combat_nearby** 等事件词只能出现在 reactions 的 event 字段，禁止写进条件（想表达"有人靠近/进入区域"→ 用谓词 in_zone(any, 区域)；**想表达"附近有战斗"→ 用谓词 combat(any, any)，不是 combat_nearby**；**想表达"等对方回应/说完"→ 用谓词 time_since(对应 say_to 步骤的 step_id, 秒)，不是 spoken_to**；想表达"物品到手"→ 没有专用谓词，省略该条件，用步骤自身 result 路由或 time_since）。**goal/until 只能写谓词词表**：无法用谓词表达的成功（如"物品到手"）→ 省略 goal，用主链 end_plan result="success" 表达成功。17. ask:"follow" 只用于"请对方跟走/过来"的邀请（请人来/引开人）；缠住/传话/望风等任务不写 ask。18. contingencies[].then 必须是字符串（步骤 id 或 @abort_gracefully）——写 {"action":...} 对象是非法结构（那是 triggers[].then 的形态，triggers 与 contingencies 结构不同，禁止混写）。

【动作词表】move_to/follow/stop_following/order_attack/knockout/lead/face/look_at/say_to/wait/emote/make_noise/signal_player/steal_attempt/give_item/give_gold/deliver_item/shadow/negotiate/duel/end_plan
【谓词词表】distance/seeing/alert_phase/following/facing/moving/in_zone/combat/player_action/time_since/dead/knocked_out/count/and/or/not（修饰：sustained_s、was）

【reactions 封闭词表（事件/动作严禁自创）】
事件 event 只能写：approach_by / spoken_to / asked_to_follow / asked_to_stay / player_suspicious_near / see_crime / combat_nearby / left_post_seconds / alone_with / seen_speaking / see_ally_killed（注意是 approach_by，不是 approached_by）
动作 action 只能写：listen / consider / refuse / follow_for_a_bit / investigate / return_post / stare / alert_raise / attack / call_guards / ignore / relay_message / pay / hand_over_item / flee（flee = 看到同伴被杀等恐慌情境下跑离现场）

【输出格式】只输出一个 JSON 对象，不要 Markdown。完整模板（BRING 示范：显式 success 收尾 + fail 收尾双出口；照此粒度输出，禁止缩水）：
{"reply":"我去请村长过来见你。","emotion":"normal","intent":{"intent_type":"BRING","subjects":["self"],"target":"chief","who_does":"companion"},"questions":[],"needs_clarification":false,"plan":{"summary":"我去请村长过来见你。","goal":{"type":"and","conditions":[{"type":"distance","a":"chief","b":"player","op":"<","value":3},{"type":"moving","a":"chief","op":"false"}]},"steps":[{"id":"b1","action":"move_to","target":"chief","within":2.0,"timeout_s":30},{"id":"b2","action":"say_to","target":"chief","ask":"follow","text":"村长，我家主人请您过去一趟，有事相商","timeout_s":8},{"id":"b3","action":"wait","until":{"type":"following","a":"chief","b":"self","op":"true"},"timeout_s":10,"on_timeout":"b7"},{"id":"b4","action":"move_to","target":"player","within":3.0,"until":{"type":"distance","a":"chief","b":"player","op":"<","value":3},"timeout_s":40,"on_timeout":"b7"},{"id":"b5","action":"wait","until":{"type":"distance","a":"chief","b":"player","op":"<","value":3,"sustained_s":5},"timeout_s":20,"on_timeout":"b7"},{"id":"b6","action":"end_plan","result":"success","report":"村长请来了","timeout_s":3}],"fallbacks":[[{"id":"b7","action":"end_plan","result":"fail","report":"村长说忙，不肯来","timeout_s":3}]],"contingencies":[{"when":{"type":"combat","entity":"self"},"then":"@abort_gracefully","one_shot":true}]},"reactions":[{"role":"chief","personality":{"gullibility":0.4,"duty":0.6,"temper":0.4,"social":0.7,"greed":0.4},"responses":[{"event":"asked_to_follow","reactions":[{"action":"follow_for_a_bit","weight":0.7},{"action":"refuse","weight":0.3}]}]}]}

批量目标（杀/打晕一群人）用 loop 段（示例）：
"loop":{"steps":[{"id":"p1","action":"move_to","target":{"query":"nearest_enemy(self)"},"within":1.5,"timeout_s":15},{"id":"p2","action":"knockout","target":{"query":"nearest_enemy(self)"},"timeout_s":10}],"until":{"type":"count","of":{"query":"all_in(zone)"},"op":"=","value":0}}，loop 之后接主链报告步骤

【失败路径示范（照抄此结构）】条件等待超时/拒绝事件 = 失败，on_timeout/on_event 只能指向 fail 收尾或重试预案：
{"id":"w3","action":"wait","until":{"type":"following","a":"guard","b":"self","op":"true"},"timeout_s":10,"on_timeout":"w4"},
{"id":"w4","action":"end_plan","result":"fail","report":"他没跟来，不肯走","timeout_s":3}
（禁止 on_timeout 指向 result="success" 的 end_plan——条件没等到却说成功 = 谎报）

【等对方回应示范（照抄此结构）】"等目标回应/说完"用 time_since 引用 say_to 步骤（禁止用 spoken_to——那是事件词）：
{"id":"i2","action":"say_to","target":"contact","text":"我家主人说，他在老地方等你","timeout_s":8},
{"id":"i3","action":"wait","until":{"type":"time_since","step_id":"i2","op":">","value":2},"on_event":[{"type":"refused","then":"i5"}],"timeout_s":6,"on_timeout":"i6"},
{"id":"i4","action":"end_plan","result":"success","report":"说好了","timeout_s":3}，i5/i6 为 fail 收尾

【保持型示范（望风/压阵，照抄此结构）】持续待命 = 无限 wait（省略 seconds/until/timeout）+ triggers 事件报告，不设 goal：
"steps":[{"id":"h1","action":"move_to","target":"player","within":2.0,"timeout_s":30},{"id":"h2","action":"wait"}],
"triggers":[{"when":{"type":"in_zone","a":"any","b":"player","op":"true"},"then":{"action":"signal_player","text":"有人来了！"}}]
（h2 无限等待，结束 = 玩家按停止键；禁止把"等 N 秒没人来"写成 success 收尾）

【判定型步骤示范（照抄此结构）】偷窃/拿取类"物品到手"用 result 路由表达结果，不写 has_item 类条件：
{"id":"c1","action":"steal_attempt","target":"chest","variant":"item","when":{"type":"seeing","a":"any","b":"self","op":"false","sustained_s":3},"result":{"success":"c2","empty":"c6","interrupted":"c8"},"timeout_s":40,"on_timeout":"c7"},
{"id":"c2","action":"signal_player","text":"得手了，撤！","timeout_s":3}，c6/c7/c8 为 fail 收尾（result 的每个键都必须指向存在的步骤）

【输出质量要求（不满足视为不合格，必须重写）】
1. 主链 steps ≥ 5 步（简单任务至少 4 步），粒度到"走→说→等→验证→报告"，禁止 2-3 步糊弄。
2. fallbacks ≥ 2 个预案（每个预案 = 一种失败情形：目标拒绝/超时/意外中断，预案内 ≥ 2 步，含 end_plan + report）。
3. contingencies ≥ 2 条：combat → @abort_gracefully 必写 + 至少 1 条任务相关意外（折返 following was 检测/警戒 alert_phase/掉线 seeing 翻转）。
4. 非保持型计划必须带 goal（计划成功条件：distance/seeing/count 谓词组合）；保持型计划（望风/压阵/盯梢）不设 goal，用无限 wait + triggers 表达，结束由玩家叫停。
5. reactions：事件/动作必须在封闭词表内，禁止自创。
6. 简单命令也要完整收尾：最后一步用 end_plan（可带 report 一句收尾台词，如"办好了/我就在这等你"），禁止裸 wait 结尾。
7. 动态目标优先用 query 找点（lure_spot/hidden_spot/stand_spot/nearest_enemy/all_in/zone/point），禁止硬编码场景里不存在的区域名。
8. 引开/望风/拖延/得手类计划必须用 signal_player 给玩家行动窗口提示；成功/失败收尾尽量带 report 当面报告。
9. zone(名称)/point(描述) 只能引用【场景区域锚点】段列出的名称；场景未列出的区域 → 改用 hidden_spot/lure_spot 动态找点，找不到就按失败收尾，禁止编造锚点。
10. 失败跳转纪律：on_timeout / on_event（refused 等拒绝类事件）是失败路径，必须指向 result="fail" 的 end_plan 或重试预案，禁止指向 success 收尾（谎报成功）。

【执行要求】
1. 歧义→填 questions 置 needs_clarification=true 不生成 plan。2. 词表外→CUSTOM 且 plan 为 null。3. reactions 给相关 NPC 写反应计划。4. 台词像随从说话，简短自然。"""

# 预设命令：命令 → 期望意图（CLARIFY = 歧义应澄清；CUSTOM = 词表外应拒绝）
PRESET_COMMANDS = [
    ("我想偷那箱子，有人盯着怎么办？", "STEAL", "偷箱子+引开策略（复合命令按最终目的=STEAL；模型可能 STEAL+澄清，设计内）"),
    ("请村长到我面前来", "BRING", "请村长"),
    ("去把那个守卫的钱袋摸来", "STEAL", "扒守卫"),
    ("帮我望风，来人了叫我", "LOOKOUT", "望风"),
    ("干掉他", "ATTACK", "攻击（歧义）"),
    ("带我去河边", None, "带路（场景无河边锚点 → 澄清/诚实报告/运行时失败兜底，看行为）"),
    ("把那醉鬼从我身边赶走", "DRIVE_AWAY", "赶走"),
    ("告诉他，我在老地方等他", "DELIVER", "传话"),
    ("缠住掌柜，我去翻那保管箱", "ENGAGE", "缠住"),
    ("帮我把这封信翻译成拉丁文", "CUSTOM", "词表外拒绝"),
    ("把全村人都杀了", "ANNIHILATE", "清剿（模型可能自主拒绝）"),
]


def build_prompt(scene, command):
    return ("【当前场景】\n" + scene + "\n\n【玩家命令】\n" + command + "\n"
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
        if s.get("action") == "say_to" and "text" not in s:
            issues.append("say_to 缺 text 字段")
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

# -*- coding: utf-8 -*-
"""把序章的真实台词抽出来，生成一段 HTML（供逆水寒511-玩法演示.html嵌入）。

读取语言XML和唯一维护源 *.dialog.json；不读取旧格式，不修改对白真源。
用法：python plans/逆水寒511/tools/gen_dialogue_html.py > 片段.html
直接更新既有页面：python plans/逆水寒511/tools/gen_dialogue_html.py --write
# 片段插进 plans/逆水寒511/逆水寒511-玩法演示.html 的「他们到底说些什么」一节（替换 h3/details 块）
"""
import io
import re
import json
import os
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CN = os.path.join(ROOT, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def load():
    root = ET.parse(CN).getroot()
    return {e.get("id"): e.get("text") for e in root.iter("string")}


def definitions():
    """正式格式唯一源；坏文件不能静默跳过让预览少一段内容。"""
    folder = os.path.join(ROOT, "ModuleData", "DesignData", "Dialogues")
    for fn in sorted(os.listdir(folder)):
        if fn.endswith(".dialog.json"):
            with io.open(os.path.join(folder, fn), encoding="utf-8-sig") as source:
                yield fn, json.load(source)


def node_of(key):
    """按 TextKey 反查对话节点（用来把「玩家的选项」也列出来，让这段读起来是对话而不是独白）。"""
    for _, data in definitions():
        for n in data.get("Nodes", []):
            if n.get("TextKey") == key:
                # A reviewed legacy TextKey belongs to the tail; display from the first native NPC sentence.
                head, visited = n, set()
                while head.get("Id") not in visited:
                    visited.add(head.get("Id"))
                    before = [x for x in data["Nodes"] if x.get("NextNode") == head.get("Id")]
                    if len(before) != 1: break
                    head = before[0]
                return data, head
    return None


def node_by_id(data, nid):
    for n in data.get("Nodes", []):
        if n.get("Id") == nid:
            return n
    return None


def assign_owner(data, entry_id):
    """树的归属：每个节点归给**离入口最近**的那条边（层序认领）。

    🔴 不能按"深度优先第一次到达"算归属 —— 那会让先展开的分支把公共节点抢走：
       开场层的并列选项里，第一个选项一路走到菜单，菜单下的节点就全在它名下，
       其余并列选项只能显示 goto、点不开（用户 2026-11-13 实测到的就是这个）。
       层序认领保证同一层的兄弟选项都展开、都能点。
    """
    by_id = {n.get("Id"): n for n in data.get("Nodes", [])}
    owner = {entry_id: None}
    queue = [entry_id]
    while queue:
        nid = queue.pop(0)
        node = by_id.get(nid)
        if not node:
            continue
        t = node.get("NextNode")
        if t and t not in owner:
            owner[t] = nid
            queue.append(t)
        for tr in node.get("Options", []):
            for t in ((tr.get("Check") or {}).get("Success") or tr.get("NextNode"), (tr.get("Check") or {}).get("Fail")):
                if not t or t in owner:
                    continue
                owner[t] = nid
                queue.append(t)
    return owner


def render_flow(lang, data, node, who, rendered, depth=0, emit_lines=True, owner=None, prefix="", placed=None):
    """对话树 = **有向图**，但呈现按树来：树是主干，goto 是跳转边。

    树边 = 层序认领的那条边（见 assign_owner）；只有不是树边的指向才画成 goto。
    同一个节点只展开一次 —— 它的树位置由认领它的那条边决定。
    """
    out = []
    if node is None or depth > 30:
        return out
    nid = node.get("Id")
    if nid in rendered:
        return out          # 已由调用方渲染成返回链接
    rendered.add(nid)

    if emit_lines:
        out.append('<div class="jnote">说话者：%s｜听话者：%s｜节点：%s</div>' %
                   (esc(node.get("Speaker", "")), esc(node.get("Listener", "")), esc(nid)))
        text = lang.get(node.get("TextKey", ""), "")
        for line in text.replace("&#10;", chr(10)).split(chr(10)):
            line = line.strip()
            if line:
                out.append('<div class="flow-l">%s</div>' % esc(line))

    if node.get("NextNode"):
        target = node["NextNode"]
        child = node_by_id(data, target)
        out.append('<div class="jnote">↓ 点击原版继续，下一句 NPC 台词（无玩家选项）</div>')
        if target in rendered:
            out.append('<a href="#%s%s">前往 %s</a>' % (prefix, esc(target), esc(target)))
        else:
            kids = render_flow(lang, data, child, who, rendered, depth, owner=owner, prefix=prefix, placed=placed)
            out.append('<div id="%s%s">%s</div>' % (prefix, esc(target), "".join(kids)))
        return out

    # 先画「一进场就能选」的；挂了显示条件的收进一个折叠块（规则十一：一屏别铺一堆）
    def emit(tr, out):
        pk = tr.get("PlayerKey")
        nxt = (tr.get("Check") or {}).get("Success") or tr.get("NextNode")
        fail_nxt = (tr.get("Check") or {}).get("Fail")
        if not pk:
            return
        label = re.sub(r"^\[[^\]]*\]\s*", "", lang.get(pk, "")).strip()
        if not label:
            return

        def emit_branch(target_id, branch_badge=""):
            child = node_by_id(data, target_id) if target_id else None
            badges = branch_badge
            if tr.get("Requires"):
                badges += '<span class="jcond">需 %s</span>' % esc(", ".join(tr["Requires"]))
            if any(r.startswith("!asked:") for r in tr.get("Requires", [])):
                badges += '<span class="jcond">只问一次</span>'
            if tr.get("Check"):
                check = tr["Check"]
                badges += '<span class="jcond">检定 %s｜%s</span>' % (esc(check.get("Target", "")), esc(check.get("Skill", "")))
            head = '▸ （玩家，%s）：%s%s<span class="jid">→ %s</span>' % (esc(tr.get("Listener", who)), esc(label), badges, esc(target_id or "（离开）"))

            if child is None:
                out.append('<div class="flow-o">%s</div>' % head)
                return

            def goto_row():
                flabel = esc(first_line(lang, child))
                known = placed if placed is not None else owner
                if known is not None and target_id in known:
                    return '<a class="flow-o jgotorow" href="#%s%s">%s<span class="jback">↩ goto「%s」</span></a>' \
                           % (prefix, esc(target_id), head, flabel)
                return '<div class="flow-o">%s<span class="jback">↩ goto「%s」（不在本块展开）</span></div>' % (head, flabel)

            if owner is not None and owner.get(target_id) != nid:
                out.append(goto_row())
                return

            if target_id in rendered:
                out.append(goto_row())
                return

            kids = render_flow(lang, data, child, who, rendered, depth + 1, owner=owner, prefix=prefix, placed=placed)
            out.append('<details class="fnode" id="%s%s"><summary class="flow-o">%s</summary><div class="fkids">%s</div></details>'
                       % (prefix, esc(target_id), head, "".join(kids)))

        if tr.get("Check"):
            emit_branch(nxt, '<span class="jcond">检定成</span>')
            emit_branch(fail_nxt, '<span class="jcond">检定败</span>')
        else:
            emit_branch(nxt, "")

    plain, gated, leave = [], [], []
    for tr in node.get("Options", []):
        if tr.get("PlayerKey") == "LWN_story_opt_leave":
            leave.append(tr)
            continue
        (gated if tr.get("Requires") else plain).append(tr)
    for tr in plain:
        emit(tr, out)
    if gated:
        inner = []
        for tr in gated:
            emit(tr, inner)
        out.append('<div class="gated-list" data-gated="%d">%s</div>' % (len(gated), "".join(inner)))
    if leave:
        # 离开是真实选项，页面上必须看得到 —— 否则"这句该不该收起对话"根本没法在预览里核对
        out.append('<div class="flow-o jleave">（玩家，%s）：[离开] 结束对话<span class="jid">→ （关闭）</span></div>'
                   % esc(who))
    return out


def first_line(lang, node):
    text = lang.get(node.get("TextKey", ""), "")
    for line in text.replace("&#10;", chr(10)).split(chr(10)):
        line = line.strip()
        if line:
            return line.split("：")[-1][:14]
    return node.get("Id", "")


def player_lines(d, key):
    """这句 NPC 话之后，玩家能接的话（每条都带 [标签]，标签本身不念出来）。"""
    hit = node_of(key)
    if not hit:
        return []
    data, node = hit
    out = []
    for tr in node.get("Options", []):
        pk = tr.get("PlayerKey")
        if not pk:
            continue
        text = d.get(pk, "")
        if not text:
            continue
        # 去掉 [说两句] / [打听] 这种 UI 标签
        text = re.sub(r"^\[[^\]]*\]\s*", "", text).strip()
        if not text or text in out:
            continue
        out.append(text)
    return out


def block(d, title, sub, keys, prefix="", turns=None):
    lines = []
    for k in keys:
        t = d.get(k)
        if not t:
            continue
        t = t.replace("&#10;", chr(10)).replace(chr(92) + "n", chr(10))
        for ln in t.split(chr(10)):
            ln = ln.strip()
            if ln:
                lines.append((k, ln))
    if not lines:
        return ""
    parts = []
    for key, x in lines:
        turn = (turns or {}).get(key)
        if turn:
            parts.append('<div class="jnote">说话者：%s｜听话者：%s（审阅字段，不属于台词正文）</div>' % (esc(turn[0]), esc(turn[1])))
        parts.append("<p>%s</p>" % esc(x))

    # 🔴 原版对话流：NPC 说完 → 你挑一句 → 他接着说什么（整条链展开）
    if keys:
        hit = node_of(keys[0])
        if hit:
            data, node = hit
            who = node.get("Speaker", "对方")
            # All reviewed dialogue comes from the actual graph; no extra manually listed lines.
            parts = []
            owner = assign_owner(data, node.get("Id"))
            # 块入口节点不在 <details> 里（它就是本块正文），单独补一个锚点给它，
            # 否则别的块 goto 过来会指向一个不存在的 id
            parts.append('<span id="%s%s"></span>' % (prefix, esc(node.get("Id"))))
            rendered = set()
            # placed = 本块真的会渲染成锚点的节点。非开场块不追加 *_hub，
            # 所以那些块里的 hub 不能出链接（否则就是悬空 goto）。
            placed = set(owner)
            is_entry_block = any(e.get("Node") == node.get("Id") and e.get("Trigger") == "player_talk" and not e.get("Conditions") for e in data.get("Entries", []))
            if not is_entry_block:
                placed = {x for x in placed if not str(x).endswith("_hub")}
            flow = render_flow(d, data, node, who, rendered, emit_lines=True,
                               owner=owner, prefix=prefix, placed=placed)

            # 🔴 菜单层放在**开场台词正下方、话题列表之前** —— 它是"上一级"。
            #    放块末尾的话，点"别的先不问了"是往下跳到一块新内容（用户实测：像"开了一个新的"）；
            #    放上面，收起当前分支后视线回到上面，才是"回到上一级"。
            #    🔴 只放台词、**不再抄一遍选项**：菜单的选项就是下面那三个话题，
            #       重复列出来会让人以为有两套（用户 2026-11-13 实测："两个重复的内容"）。
            # 第一段（就是上面已渲染的 NPC 台词）去掉，从"你的选项"开始接
            parts.append('<div class="opts">%s</div>' % "".join(flow))
    body = "".join(parts)
    return (
        '    <details class="dlg">\n'
        '      <summary><b>%s</b><span>%s</span></summary>\n'
        '      <div class="dlgbody">%s</div>\n'
        '    </details>\n' % (esc(title), esc(sub), body)
    )


GROUPS = [
    ("四家 · 第一次见面", [
        ("老周 · 当铺老板", "",
         ["LWN_story_pawnbroker_intro"]),
        ("寡妇 · 剧团领班", "",
         ["LWN_story_widow_intro"]),
        ("儿子 · 小贼", "",
         ["LWN_story_son_intro"]),
        ("屠户 · 叛逃者", "",
         ["LWN_story_butcher_intro"]),
    ]),
    ("四家 · 他们各自知道的那一截", [
        ("老周：一只没赎回的镯子", "",
         ["LWN_story_pawnbroker_ask_sister", "LWN_story_pawnbroker_sister_where"]),
        ("寡妇：贵族身份的门路", "当前资格不是独立文书物品",
         ["LWN_story_widow_ask_sister"]),
        ("儿子：后巷那一夜", "",
         ["LWN_story_son_ask_sister"]),
        ("屠户：他们往码头去了", "",
         ["LWN_story_butcher_ask_sister"]),
    ]),
    ("四家 · 他们派你做的事", [
        ("老周：把账收回来", "",
         ["LWN_story_pawnbroker_deal"]),
        ("寡妇：争取戏台场地", "可魅力说服或真实战斗",
         ["LWN_story_widow_deal"]),
        ("儿子：打发墙根的野贼", "",
         ["LWN_story_son_deal"]),
        ("屠户：教训砸摊的打手", "",
         ["LWN_story_butcher_deal"]),
    ]),
    ("四家 · 办完事回去交差", [
        ("老周 · 交差（拿酬劳与旧账本）", "",
         ["LWN_story_pawnbroker_turnin"]),
        ("寡妇 · 交差（拿贵族行头与身份资格）", "",
         ["LWN_story_widow_turnin"]),
        ("儿子 · 交差（拿飞钩与撬锁包）", "",
         ["LWN_story_son_turnin"]),
        ("屠户 · 交差（拿开刃砍刀与麻绳）", "",
         ["LWN_story_butcher_turnin"]),
    ]),
    ("四个仇家 · 登门对质与讨债", [
        ("收数人（欠老周的钱 · 可说服还款或动手）", "",
         ["LWN_story_debtor_intro"]),
        ("对台领班（寡妇的对头 · 护招牌）", "",
         ["LWN_story_rival_intro"]),
        ("墙根野贼（抢走小贼飞钩的人）", "",
         ["LWN_story_outlaw_intro"]),
        ("码头帮打手（砸屠户肉案的人）", "",
         ["LWN_story_thug_intro"]),
    ]),
    ("守门人 · 双岗共用对话定义", [
        ("第一次进城 · 质问台词", "首遇双岗已有实机出现；实际质问者按岗位AI请求绑定，共用gate_interrogation。下面逐句展示正式源，questioner绑定实际质问守卫；真实说话者/听话者已接原版机制，换人接手与站位仍待实机；首遇强化与保命设定待裁定",
         ["LWN_story_blocker_first"]),
        ("第二次进城 · 城门过招", "四条对话路径＋六种现场手段；现场手段不靠点选通关",
         ["LWN_story_blocker_open"]),
        ("塞钱：他当面数两遍", "",
         ["LWN_story_blocker_bribe_ok"]),
        ("威吓成功", "",
         ["LWN_story_blocker_threat_ok"]),
        ("威吓失败", "",
         ["LWN_story_blocker_threat_fail"]),
        ("冒充成功", "",
         ["LWN_story_blocker_disguise_ok"]),
        ("冒充被识破", "",
         ["LWN_story_blocker_disguise_fail"]),
        ("潜行成功 · 结果文本参考", "须真实蹲行穿门、视线及技巧判定；此文本不是点选通关入口",
         ["LWN_story_blocker_sneak_ok"]),
        ("钩索翻墙成功 · 结果文本参考", "须真实钩索越墙并稳定落地；此文本不是点选通关入口",
         ["LWN_story_blocker_climb_ok"]),
        ("钩子滑了", "",
         ["LWN_story_climb_missed"]),
        ("被认出来（通缉 + 没遮面）", "",
         ["LWN_story_recognized"]),
        ("扒空了他的刀", "",
         ["LWN_story_blocker_disarmed_ok"]),
    ]),
    ("街头 · 事件里他们在吵什么", [
        ("事件 1 · 收保护费", "",
         ["LWN_story_event1_join"]),
        ("事件 1 · 你出钱之后", "",
         ["LWN_story_event1_paid"]),
        ("事件 2 · 砸摊", "",
         ["LWN_story_event2_join"]),
        ("事件 3 · 骂街", "",
         ["LWN_story_event3_join"]),
        ("事件 4 · 追问偷窃", "抓领子动作未实现",
         ["LWN_story_event4_join"]),
        ("事件 5 · 一个人发愁", "",
         ["LWN_story_event5_join"]),
        ("事件 6 · 关照伤势", "搬案板演出未实现",
         ["LWN_story_event6_join"]),
    ]),
    ("进城引导与牢内换装", [
        ("进城引导 · 同伴或旁白", "下列为当前旁白原文；已改为只借用已有可用同伴，无人走旁白，身份/查重与全生命周期仍待联验",
         ["LWN_story_intro_line_1", "LWN_story_intro_line_2", "LWN_story_intro_line_3"]),
        ("牢内换装确认 · 当前弹窗文本", "原文提到老囚交衣，仅为文字叙述；老囚实体交接、黑屏演出未实现",
         ["LWN_story_jail_body_1", "LWN_story_jail_body_2", "LWN_story_jail_body_3"]),
    ]),
]


def render_definitions():
    """让审阅者看到与运行相同的稳定ID、触发和条件，表内容直接来自正式源。"""
    out = ['<h3 class="dlghead">正式对话定义 · 唯一维护源</h3>',
           '<p>对白树、入口表与游戏定义直接读取同一批 .dialog.json。每次互动由NPC先开口；一句一节点；人物信息单独显示供审阅，不属于游戏台词正文。连续台词点击原版继续，玩家选项只在末句之后出现。中文键旁注保留，实机状态见总纲。</p>']
    for filename, data in definitions():
        parts = ['<p>源：<code>%s</code>｜版本 %s｜演员：%s</p>' %
                 (esc(filename), esc(str(data.get("ContentVersion", ""))),
                  esc(", ".join(data.get("Actors", []))))]
        parts.append('<p>入口演员：%s｜NPC先开场：%s</p>' % (esc(", ".join(data.get("EntryActors", []))), "是" if data.get("SkipVanillaOpening") else "否"))
        parts.append('<table><thead><tr><th>触发ID</th><th>来源／目的</th><th>启动条件</th></tr></thead><tbody>')
        for trig in data.get("Triggers", []):
            parts.append('<tr><td>%s</td><td>%s／%s</td><td>%s</td></tr>' %
                         (esc(trig["Id"]), esc(trig["Source"]), esc(trig["Purpose"]),
                          esc(", ".join(trig.get("Conditions", [])) or "无额外条件")))
        parts.append('</tbody></table><table><thead><tr><th>入口ID</th><th>触发ID</th><th>条件</th><th>优先级</th><th>节点</th></tr></thead><tbody>')
        for entry in data.get("Entries", []):
            parts.append('<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>' %
                         (esc(entry["Id"]), esc(entry["Trigger"]),
                          esc(", ".join(entry.get("Conditions", [])) or "无额外条件"),
                          esc(str(entry["Priority"])), esc(entry["Node"])))
        parts.append('</tbody></table><table><thead><tr><th>节点</th><th>说话者</th><th>听话者</th><th>台词正文</th><th>下一句／玩家选项</th></tr></thead><tbody>')
        for node in data.get("Nodes", []):
            parts.append('<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>' %
                         (esc(node["Id"]), esc(node["Speaker"]), esc(node["Listener"]), esc(node.get("TextKeyZh", "")),
                          esc(node.get("NextNode") or ("玩家选项：%s" % len(node.get("Options", []))))))
        parts.append('</tbody></table>')
        out.append('<details class="dlg"><summary><b>%s</b><span>%s个节点</span></summary><div class="dlgbody">%s</div></details>' %
                   (esc(data["DialogueId"]), len(data.get("Nodes", [])), "".join(parts)))
    return "".join(out)


def render():
    d = load()
    cast_path = os.path.join(ROOT, "ModuleData", "DesignData", "StoryProbeCast.json")
    with io.open(cast_path, encoding="utf-8-sig") as source:
        cast = json.load(source)
    names = {n["Id"]: d.get(n.get("NameKey"), n["Id"]) for n in cast.get("Npcs", [])}
    names["player"] = "玩家"
    turns = {}
    for s in cast.get("StreetLife", []):
        for key in s.get("LineKeys", []):
            turns[key] = (names.get(s["NpcId"], s["NpcId"]), names.get(s.get("Listener"), s.get("Listener", "")))
    for event in cast.get("Events", []):
        for key, speaker, listener in zip(event.get("LineKeys", []), event.get("Speakers", []), event.get("Listeners", [])):
            turns[key] = (names.get(speaker, speaker), names.get(listener, listener))
    out = [render_definitions()]
    bid = 0
    for title, items in GROUPS:
        out.append('  <h3 class="dlghead">%s</h3>\n' % esc(title))
        for t, s, keys in items:
            bid += 1
            out.append(block(d, t, s, keys, prefix="dlg%d-" % bid, turns=turns))
    return "".join(out)


if __name__ == "__main__":
    result = render()
    if "--write" in sys.argv[1:]:
        page_path = os.path.join(ROOT, "plans", "逆水寒511", "逆水寒511-玩法演示.html")
        with io.open(page_path, encoding="utf-8") as source:
            page = source.read()
        section = page.index('<h2>他们到底说些什么</h2>')
        start = page.index('<h3 class="dlghead">', section)
        end = page.index('</section>', start)
        with io.open(page_path, "w", encoding="utf-8", newline="") as target:
            target.write(page[:start] + result + "\n  " + page[end:])
        print("dialogue html updated from formal definitions and speech bindings")
    else:
        print(result)

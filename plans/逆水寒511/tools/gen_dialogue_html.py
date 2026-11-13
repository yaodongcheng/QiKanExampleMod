# -*- coding: utf-8 -*-
"""把序章的真实台词抽出来，生成一段 HTML（供 city-gate-paths.html 嵌入）。

只读语言 XML；不改任何东西。
用法：python plans/逆水寒511/tools/gen_dialogue_html.py > 片段.html
# 片段插进 plans/逆水寒511-玩法演示.html 的「他们到底说些什么」一节（替换 h3/details 块）
"""
import io
import re
import json
import os
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CN = os.path.join(ROOT, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def load():
    root = ET.parse(CN).getroot()
    return {e.get("id"): e.get("text") for e in root.iter("string")}


def node_of(key):
    """按 NpcKey 反查对话节点（用来把「玩家的选项」也列出来，让这段读起来是对话而不是独白）。"""
    folder = os.path.join(ROOT, "ModuleData", "DesignData", "Dialogues")
    if not os.path.isdir(folder):
        return None
    for fn in sorted(os.listdir(folder)):
        if not fn.endswith(".json"):
            continue
        try:
            data = json.load(io.open(os.path.join(folder, fn), encoding="utf-8"))
        except Exception:
            continue
        for n in data.get("Nodes", []):
            if n.get("NpcKey") == key:
                return data, n
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
        for tr in node.get("Transitions", []):
            t = tr.get("NextNodeOnSuccess")
            if not t or t in owner:
                continue
            owner[t] = nid
            queue.append(t)
    # 🔴 菜单节点（*_hub）固定挂在块入口下 —— 它是所有支线的公共出口。
    #    按最短路径算会把它随机埋进某条支线（实测埋进"门口那个打手"里 3 层深），
    #    读者点"别的先不问了"就跳进一个无关区块。放顶层才是"回到上一级"。
    for nid in list(owner.keys()):
        if nid.endswith("_hub"):
            owner[nid] = entry_id
    return owner


def render_flow(lang, data, node, who, rendered, depth=0, emit_lines=True, owner=None, prefix="", placed=None):
    """对话树 = **有向图**，但呈现按树来：树是主干，goto 是跳转边。

    树边 = 层序认领的那条边（见 assign_owner）；只有不是树边的指向才画成 goto。
    同一个节点只展开一次 —— 它的树位置由认领它的那条边决定。
    """
    out = []
    if node is None or depth > 10:
        return out
    nid = node.get("Id")
    if nid in rendered:
        return out          # 已由调用方渲染成返回链接
    rendered.add(nid)

    if emit_lines:
        text = lang.get(node.get("NpcKey", ""), "")
        for line in text.replace("&#10;", chr(10)).split(chr(10)):
            line = line.strip()
            if line:
                out.append('<div class="flow-l">%s</div>' % esc(line))

    # 先画「一进场就能选」的；挂了显示条件的收进一个折叠块（规则十一：一屏别铺一堆）
    def emit(tr, out):
        pk = tr.get("PlayerKey")
        nxt = tr.get("NextNodeOnSuccess")
        if not pk:
            return
        label = re.sub(r"^\[[^\]]*\]\s*", "", lang.get(pk, "")).strip()
        if not label:
            return
        child = node_by_id(data, nxt) if nxt else None
        badges = ""
        if tr.get("Requires"):
            badges += '<span class="jcond">需 %s</span>' % esc(tr["Requires"])
        if tr.get("Once"):
            badges += '<span class="jcond">只问一次</span>'
        head = '▸ （玩家，%s）：%s%s<span class="jid">→ %s</span>' % (esc(who), esc(label), badges, esc(nxt or "（离开）"))

        if child is None:
            out.append('<div class="flow-o">%s</div>' % head)
            return

        # goto 边：目标是别处已展开的节点 —— 整行可点（不是只有尾巴那截标签）
        def goto_row():
            label = esc(first_line(lang, child))
            known = placed if placed is not None else owner
            if known is not None and nxt in known:
                return '<a class="flow-o jgotorow" href="#%s%s">%s<span class="jback">↩ goto「%s」</span></a>' \
                       % (prefix, esc(nxt), head, label)
            return '<div class="flow-o">%s<span class="jback">↩ goto「%s」（不在本块展开）</span></div>' % (head, label)

        if owner is not None and owner.get(nxt) != nid:
            # 这条边不是本节点的树边（目标已被离入口更近的节点认领）→ 真·goto
            out.append(goto_row())
            return

        if nxt in rendered:
            out.append(goto_row())
            return

        kids = render_flow(lang, data, child, who, rendered, depth + 1, owner=owner, prefix=prefix, placed=placed)
        out.append('<details class="fnode" id="%s%s"><summary class="flow-o">%s</summary><div class="fkids">%s</div></details>'
                   % (prefix, esc(nxt), head, "".join(kids)))

    plain, gated, leave = [], [], []
    for tr in node.get("Transitions", []):
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
    text = lang.get(node.get("NpcKey", ""), "")
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
    for tr in node.get("Transitions", []):
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


def block(d, title, sub, keys, prefix=""):
    lines = []
    for k in keys:
        t = d.get(k)
        if not t:
            continue
        t = t.replace("&#10;", chr(10)).replace(chr(92) + "n", chr(10))
        for ln in t.split(chr(10)):
            ln = ln.strip()
            if ln:
                lines.append(ln)
    if not lines:
        return ""
    parts = []
    for x in lines:
        parts.append("<p>%s</p>" % esc(x))

    # 🔴 原版对话流：NPC 说完 → 你挑一句 → 他接着说什么（整条链展开）
    if keys:
        hit = node_of(keys[0])
        if hit:
            data, node = hit
            mm = re.match(r"^（([^，]+)，", lines[-1]) if lines else None
            who = mm.group(1) if mm else "对方"
            owner = assign_owner(data, node.get("Id"))
            # 块入口节点不在 <details> 里（它就是本块正文），单独补一个锚点给它，
            # 否则别的块 goto 过来会指向一个不存在的 id
            parts.append('<span id="%s%s"></span>' % (prefix, esc(node.get("Id"))))
            rendered = set()
            # placed = 本块真的会渲染成锚点的节点。非开场块不追加 *_hub，
            # 所以那些块里的 hub 不能出链接（否则就是悬空 goto）。
            placed = set(owner)
            is_entry_block = node.get("Id") == data.get("EntryNode")
            if not is_entry_block:
                placed = {x for x in placed if not str(x).endswith("_hub")}
            flow = render_flow(d, data, node, who, rendered, emit_lines=False,
                               owner=owner, prefix=prefix, placed=placed)

            # 🔴 菜单层放在**开场台词正下方、话题列表之前** —— 它是"上一级"。
            #    放块末尾的话，点"别的先不问了"是往下跳到一块新内容（用户实测：像"开了一个新的"）；
            #    放上面，收起当前分支后视线回到上面，才是"回到上一级"。
            #    🔴 只放台词、**不再抄一遍选项**：菜单的选项就是下面那三个话题，
            #       重复列出来会让人以为有两套（用户 2026-11-13 实测："两个重复的内容"）。
            if is_entry_block:
                hub = next((n for n in data.get("Nodes", [])
                            if str(n.get("Id", "")).endswith("_hub")), None)
                if hub:
                    hub_text = d.get(hub.get("NpcKey", ""), "").replace("&#10;", chr(10))
                    hub_lines = "".join('<div class="flow-l">%s</div>' % esc(l.strip())
                                        for l in hub_text.split(chr(10)) if l.strip())
                    parts.append('<div class="dlg-hub" id="%s%s">%s'
                                 '<div class="jnote">↓ 回到下面这组选项（离开也在下面）</div></div>'
                                 % (prefix, esc(hub.get("Id")), hub_lines))

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
        ("寡妇：一张通行文书", "",
         ["LWN_story_widow_ask_sister"]),
        ("儿子：后巷那一夜", "",
         ["LWN_story_son_ask_sister"]),
        ("屠户：他们往码头去了", "",
         ["LWN_story_butcher_ask_sister"]),
    ]),
    ("四家 · 他们派你做的事", [
        ("老周：把账收回来", "",
         ["LWN_story_pawnbroker_deal", "LWN_story_pawnbroker_quest_accept"]),
        ("寡妇：砸了对台的招牌", "",
         ["LWN_story_widow_deal", "LWN_story_widow_quest_accept"]),
        ("儿子：打发墙根的野贼", "",
         ["LWN_story_son_deal", "LWN_story_son_quest_accept"]),
        ("屠户：教训砸摊的 thug", "",
         ["LWN_story_butcher_deal", "LWN_story_butcher_quest_accept"]),
    ]),
    ("守门人 · 铁头（门就在他手里）", [
        ("他拦下你", "",
         ["LWN_story_blocker_first", "LWN_story_blocker_open"]),
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
        ("潜行成功", "",
         ["LWN_story_blocker_sneak_ok"]),
        ("钩索翻墙成功", "",
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
        ("事件 4 · 抓小偷", "",
         ["LWN_story_event4_join"]),
        ("事件 5 · 一个人发愁", "",
         ["LWN_story_event5_join"]),
        ("事件 6 · 一个人收拾", "",
         ["LWN_story_event6_join"]),
    ]),
    ("随从与牢里", [
        ("随从 · 他只说三句", "",
         ["LWN_story_intro_line_1", "LWN_story_intro_line_2", "LWN_story_intro_line_3"]),
        ("牢里 · 老囚", "",
         ["LWN_story_jail_body_1", "LWN_story_jail_body_2", "LWN_story_jail_body_3"]),
    ]),
]


def render():
    d = load()
    out = []
    bid = 0
    for title, items in GROUPS:
        out.append('  <h3 class="dlghead">%s</h3>\n' % esc(title))
        for t, s, keys in items:
            bid += 1
            out.append(block(d, t, s, keys, prefix="dlg%d-" % bid))
    return "".join(out)


if __name__ == "__main__":
    print(render())

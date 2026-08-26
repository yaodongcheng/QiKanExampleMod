# -*- coding: utf-8 -*-
"""
verify_tk5_coverage.py — 太阁5 事件 ↔ mod 剧本 JSON 覆盖校验器
================================================================
检查 mod 剧本（09b/09c 等 .md）对 TK5 源事件是否"全覆盖"：

    1. 演出行（對話/旁白/自語）→ 剧本落点池中必须有原文对应
       （铁律 7：TK5 原文行完整性——每一行必须在 JSON 存在对应且作为行注释；
       落点池 = jsonc 代码块行注释 + 09c 风格"原文表"（| B1 | 【家康】"…" |））
    2. 触发（發生契機）→ condition 上方注释块必须提及（弱检查）
    3. 条件（發生條件 的 調查 列表）→ condition 上方注释块必须覆盖主要对象（弱检查，报告覆盖率）

🔴 2026-08-26 新增检查：
    4. 数据驱动字段——事件 JSON（含 condition 的块）必须带 trigger（∈ TRIGGER_REGISTRY），
       once ∈ {true,false}、priority ∈ {normal,weak} 枚举校验；注释有触发原文但无 trigger 字段 = [WARN] 欠账
    5. 标题行数声明——"N 行全有落点" 与源实际行数不符 = [WARN]（防手数失准）
    6. 白名单对称——多文件运行时，豁免行（09c 迁移/结算唯一）必须在其它文件也有原文落点，
       否则 [WARN]（迁移可能未完成）

只查"源 → mod"方向（mod 必须包含源事件的全部台词行）。
不检查 mod 多出来的内容：舞台管理（角色移动/进场/退场/镜头）与游戏机制是 3D 化必要补充，
允许 mod 额外发挥（01/05 原则）；台词禁止凭空发挥。

白名单：两处合法豁免——①结算唯一引用（三连旁白等，原文在另一事件全量出现，本处仅"引用"注释）；
②09c 配角线迁移（整版替换）。白名单按"事件 + 源演出行序号"登记（见 ALLOWLIST），
未登记的行报 [FAIL] 缺失。

用法：
    python verify_tk5_coverage.py <剧本.md>... [--source TK5AllEvents_merged.txt]
示例：
    python verify_tk5_coverage.py plans/scenario-campaign-mode/09b-桶狭间剧本-可执行定义.md \
        --source Knowledge/太阁事件包/TK5AllEvents_merged.txt
    python verify_tk5_coverage.py 09b.md 09c.md --strict
参数：
    --source    TK5 合并源文件（默认 Knowledge/太阁事件包/TK5AllEvents_merged.txt）
    --strict    禁用白名单豁免（全部按 [FAIL] 报）
退出码：0 = 无缺失；1 = 有 [FAIL] 缺失；2 = 事件整体缺失（md 未声明该源事件）
"""
import os
import re
import sys

# Windows 控制台默认 GBK，强制 UTF-8 以支持 [OK]/[WARN]/[FAIL] 输出
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ---------------------------------------------------------------------------
# 白名单：{事件ID: {原因: [源演出行序号...]}}  行序号 = 演出行解析顺序（1-based，
# 性别变体各自成行）。先用本脚本跑一遍生成 [FAIL] 报告，把合法的豁免登记进来。
# ---------------------------------------------------------------------------
ALLOWLIST = {
    # 09c 配角线迁移（整版替换，原文在 09c；行号 = 源演出行解析顺序，先用脚本跑一遍确认）
    "EFF0C300_159": {"09c 秀吉线宁宁婚约分支（主人公=秀吉）": list(range(13, 23))},
    "EFF0C300_163": {"09c 秀吉线（随军独白）": [26]},
    "EFF0C300_164": {"09c 秀吉/其他独白（软化处理）": [26, 28]},
    "EFF0C300_166": {"09c 秀吉线（宁宁婚约分支 + 婚约旁白）": list(range(6, 22)) + [32, 36]},
    "EPF29300_707": {"09c 利家线整版替换（带罪线野战 X1-X21）": list(range(1, 22))},
    "EPF29300_708": {"09c 利家线整版替换（带罪线结算 Y1-Y9/Z1-Z11，含自尽软化）": list(range(1, 30))},
    "EFF06E00_159": {"09c 元康线（主人公=家康机位分支，09b 只实现义元分支）": [
        13, 14, 15, 16, 26, 31, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 44, 46, 47, 48, 49,
        50, 51, 52, 60, 62, 71, 73, 75, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 88, 90, 91,
        92, 93, 94, 95, 96]},
}

# 源文件默认路径（相对仓库根）
DEFAULT_SOURCE = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "..", "Knowledge", "太阁事件包", "TK5AllEvents_merged.txt",
)

# ---------------------------------------------------------------------------
# 解析 TK5 合并源文件
# ---------------------------------------------------------------------------
RE_BLOCK_START = re.compile(r"事件:事件([A-Z0-9]+_\d+)\{")
RE_BLOCK_END = re.compile(r"\}//事件[A-Z0-9]+_\d+")
RE_TRIGGER = re.compile(r"發生契機:(.+)")
RE_CONDITIONS = re.compile(r"發生條件:\{(.*?)\}//發生條件", re.DOTALL)
RE_COND_ROW = re.compile(r"調查:\(([^)]+)\)")
RE_DIALOG = re.compile(r"對話:\(([^,]+),([^)]+)\)\[\[(.*?)\]\]", re.DOTALL)
RE_NARRATE = re.compile(r"旁白:\[\[(.*?)\]\]", re.DOTALL)
RE_SOLILOQUY = re.compile(r"自語:\[\[(.*?)\]\]", re.DOTALL)


def parse_merged(path):
    """返回 {事件ID: {"trigger": str, "conditions": [str], "lines": [(说话人, 文本), ...]}}"""
    with open(path, encoding="utf-8") as f:
        content = f.read()
    events = {}
    pos = 0
    while True:
        m = RE_BLOCK_START.search(content, pos)
        if not m:
            break
        event_id = m.group(1)
        end = RE_BLOCK_END.search(content, m.end())
        body = content[m.end(): end.start() if end else len(content)]
        pos = end.end() if end else len(content)

        ev = {"trigger": "", "conditions": [], "lines": []}

        tm = RE_TRIGGER.search(body)
        if tm:
            ev["trigger"] = tm.group(1).strip()

        cm = RE_CONDITIONS.search(body)
        if cm:
            ev["conditions"] = [r for r in RE_COND_ROW.findall(cm.group(1))]

        # 演出行：對話/旁白/自語（跳过 ＢＧＭ/ＳＥ/代入/更新/分歧/設施 等机制行——机制允许 mod 发挥）
        for spk, _, text in RE_DIALOG.findall(body):
            ev["lines"].append((spk.strip(), text))
        for text in RE_NARRATE.findall(body):
            ev["lines"].append(("旁白", text))
        for text in RE_SOLILOQUY.findall(body):
            ev["lines"].append(("自語", text))

        events[event_id] = ev
    return events


# ---------------------------------------------------------------------------
# 归一化：删标签/标点/空白，只留纯字符序列（源与注释两侧同样处理）
# 注意：括号只删字符、保留内容——源行带占位符（今川義元），09b 旧表转述版可能不带括号，
#       删括号后内容对齐才能子串匹配（源"那個(今川義元)"→"那個今川義元"= 注释"那個今川義元"）
# ---------------------------------------------------------------------------
def norm(s):
    s = s.replace("\\n", "")                      # 源文件里的 \n 转义
    s = re.sub(r"[（）()]", "", s)                 # 只删括号字符，保留内容
    s = re.sub(r"<[^>]*>", "", s)                 # <城Ｃ><ｃ> 等
    s = re.sub(r"\{[^}]*\}", "", s)               # {未知47} 等
    s = re.sub(r'[“”"\'，。、！？～……「」『』：；／·．\-—,\s　]', "", s)
    return s


def grams(s, n=2):
    return set(s[i:i + n] for i in range(len(s) - n + 1))


def overlap_ratio(a, b):
    ga, gb = grams(a), grams(b)
    if not ga:
        return 0.0
    return len(ga & gb) / len(ga)


# 引用落点关键词：注释含这些词 = 该行是"引用/迁移"而非逐句落点
REF_KEYWORDS = ("结算唯一", "不重复", "引用", "同源", "整版替换", "09c", "见09c", "渠道")

# 🔴 trigger 注册表 v1（01/16 同源；表外 = [FAIL]）
TRIGGER_REGISTRY = {
    "daily", "monthly", "game_start", "settlement_enter", "house_enter",
    "council_start", "travel_screen", "field_battle_start", "field_battle_end",
    "siege_battle_start", "siege_battle_end", "army_move_end",
    "chapter_freeze", "game_clear",
}

# ---------------------------------------------------------------------------
# 解析剧本 .md：提取 jsonc 代码块注释 + 声明源事件 ID 的标题
# ---------------------------------------------------------------------------
RE_CODEBLOCK = re.compile(r"```jsonc\s*\n(.*?)\n```", re.DOTALL)
RE_HEADING = re.compile(r"^#{2,4}\s+(.+)$", re.MULTILINE)
RE_EVENT_ID = re.compile(r"(EFF0C300|EFF06E00|ECF00000|EPF29300|EFF0D000)_\d+")


def parse_md(path):
    """返回 (事件ID→首个声明标题后的 when 注释块文本, 全文件注释列表)"""
    with open(path, encoding="utf-8") as f:
        content = f.read()

    headings = [(m.start(), m.group(1)) for m in RE_HEADING.finditer(content)]
    blocks = [(m.start(), m.group(1)) for m in RE_CODEBLOCK.finditer(content)]

    # 注释行提取（代码块内 // 之后）
    all_comments = []
    when_comments_by_event = {}   # 事件ID → 该事件 when 注释块文本（首个声明标题后的第一个代码块）
    first_claim = {}              # 事件ID → 首个声明标题位置

    for hidx, (hpos, htext) in enumerate(headings):
        ids = [m.group(0) for m in RE_EVENT_ID.finditer(htext)]
        h_end = headings[hidx + 1][0] if hidx + 1 < len(headings) else len(content)
        for eid in ids:
            if eid not in first_claim:
                first_claim[eid] = hpos

    for bpos, btext in blocks:
        comments = []
        for line in btext.splitlines():
            idx = line.find("//")
            if idx >= 0:
                comments.append(line[idx + 2:].strip())
        all_comments.extend(comments)
        # condition 注释块：代码块包含事件级 "condition" 字段 → 其内注释归属到"块前最近含事件 ID 的标题的所有 ID"
        # （无 ID 标题不重置——如"## 事件 1"夹在声明标题之间；步骤/行级 "when" 不参与归属）
        if '"condition"' in btext:
            claimer_ids = []
            for hpos, htext in sorted(headings):
                if hpos >= bpos:
                    break
                ids = [m.group(0) for m in RE_EVENT_ID.finditer(htext)]
                if ids:
                    claimer_ids = ids
            if claimer_ids:
                # when 块的注释（触发/条件/落点说明）全部归属标题声明的每个事件
                for eid in claimer_ids:
                    when_comments_by_event.setdefault(eid, []).extend(comments)

    # 触发/条件检查时每个事件用"首个声明标题"后的 condition 注释；fallback 到任意 condition 注释
    # 🔴 原文表提取（09c 风格，2026-08-26）：markdown 表格行 | B1 | 【家康】"…" ／ 【旁白】"…" |
    #   —— 表格内的【说话人】"文本"并入落点池（09c 用"原文表"而非 jsonc 行注释）
    for line in content.splitlines():
        if line.startswith("|"):
            all_comments.extend(re.findall(r"【[^】]+】\"[^\"]*\"", line))
    return {"all_comments": all_comments, "when": when_comments_by_event}


# 繁简映射（覆盖剧本对象名常见字，够用即可）
TRAD2SIMPLE = {
    "長": "长", "岡": "冈", "鳴": "鸣", "遠": "远", "駿": "骏", "條": "条",
    "齋": "斋", "織": "织", "義": "义", "德": "德", "氏": "氏", "張": "张",
    "豊": "丰", "鵜": "鹈", "殿": "殿", "照": "照", "雪": "雪", "輝": "辉",
    "慶": "庆", "諫": "谏", "參": "参", "與": "与", "發": "发", "軍": "军",
    "將": "将", "當": "当", "據": "据", "點": "点", "標": "标", "誌": "志",
    "偽": "伪", "國": "国", "評": "评", "間": "间", "來": "来", "無": "无",
    "動": "动", "戰": "战", "擊": "击", "勝": "胜", "敗": "败", "聲": "声",
    "亂": "乱", "險": "险", "覺": "觉", "運": "运", "陣": "阵", "斬": "斩",
    "殺": "杀", "傷": "伤", "認": "认", "為": "为", "說": "说", "話": "话",
    "傳": "传", "達": "达", "襲": "袭", "響": "响", "歸": "归", "還": "还",
    "願": "愿", "讓": "让", "興": "兴", "觀": "观", "隱": "隐", "憂": "忧",
    "慮": "虑", "遺": "遗", "辭": "辞", "職": "职", "務": "务", "異": "异",
}


def trad2simple(s):
    return "".join(TRAD2SIMPLE.get(ch, ch) for ch in s)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    source = DEFAULT_SOURCE
    strict = False
    if "--source" in sys.argv:
        source = sys.argv[sys.argv.index("--source") + 1]
    if "--strict" in sys.argv:
        strict = True
    if not args:
        print(__doc__)
        sys.exit(2)

    if not os.path.exists(source):
        print("错误：源文件不存在：" + source)
        sys.exit(2)

    sources = parse_merged(source)
    print("源事件解析完成：共 %d 个事件（%s）" % (len(sources), os.path.basename(source)))

    # 🔴 白名单对称池（2026-08-26）：多文件运行时，豁免行必须在其它文件也有落点
    pools = {}
    for md_path in args:
        if os.path.exists(md_path):
            pools[md_path] = [norm(c) for c in parse_md(md_path)["all_comments"]]

    exit_code = 0
    for md_path in args:
        if not os.path.exists(md_path):
            print("错误：剧本文件不存在：" + md_path)
            sys.exit(2)
        md = parse_md(md_path)
        all_norm = [norm(c) for c in md["all_comments"]]

        # 事件 ID 声明只认标题行（##/###）——正文表格/指针/修订记录里的 ID 不算声明
        with open(md_path, encoding="utf-8") as f:
            md_text = f.read()
        claimed = sorted({
            m.group(0)
            for htext in RE_HEADING.findall(md_text)
            for m in RE_EVENT_ID.finditer(htext)
        })
        print("\n========== %s ==========" % os.path.basename(md_path))
        print("声明源事件：%s" % "、".join(claimed))

        for event_id in claimed:
            if event_id not in sources:
                print("  [[FAIL]] %s：md 声明了但 TK5 源文件中不存在（ID 拼写错误？）" % event_id)
                exit_code = max(exit_code, 2)
                continue
            ev = sources[event_id]
            n_cond_objs = 0
            print("\n── %s（触发：%s）" % (event_id, ev["trigger"] or "（无触发头）"))
            print("    源演出行 %d 行，条件 %d 条" % (len(ev["lines"]), len(ev["conditions"])))

            # ── 触发检查（弱）：when 注释块是否提及触发特征 ──
            trig_ok = "?"
            t_norm = norm(ev["trigger"])
            t_key = t_norm[:8] if len(t_norm) >= 4 else t_norm
            if not t_key:
                trig_ok = "[OK]（源无触发头）"
            else:
                when_comments = md["when"].get(event_id, [])
                if any(t_key and t_key in norm(c) for c in when_comments):
                    trig_ok = "[OK]"
                elif when_comments:
                    trig_ok = "[WARN] 未在 condition 注释块找到触发特征「%s」——人工核对" % t_key
                else:
                    trig_ok = "[FAIL] 未找到 condition 注释块（触发/条件注释缺失）"
            print("    触发检查：%s" % trig_ok)

            # ── 条件检查（弱）：对象名覆盖率 ──
            when_comments = md["when"].get(event_id, [])
            # 🔴 双侧繁简归一（2026-08-26）：条件对象已 trad2simple，注释侧同样转简——防"織田家存在" vs "织田" 漏配
            when_norm = trad2simple(" ".join(norm(c) for c in when_comments))
            cond_hit, cond_total = 0, 0
            for row in ev["conditions"]:
                m = re.search(r"(?:人物|大名家|城|國|據點|狀況)::([^.()]+)", row)
                if not m:
                    continue
                obj = trad2simple(m.group(1).strip())
                # 摘要式条件注释常只写"姓"（今川）或"名"（道三），全名/前2字/后2字任一命中即算覆盖
                candidates = {obj, obj[:2], obj[-2:]} if len(obj) >= 2 else {obj}
                cond_total += 1
                if any(c and c in when_norm for c in candidates):
                    cond_hit += 1
            if cond_total == 0:
                print("    条件检查：[OK]（源无条件行）")
            else:
                ratio = cond_hit / cond_total
                # 弱检查：摘要式条件注释（铁律 7 允许"原文 + 落点说明"摘要）不产生 FAIL——
                # 覆盖率不足只标 [WARN] 人工核对；演出行才是强检查
                mark = "[OK]" if ratio >= 0.9 else "[WARN]"
                print("    条件检查：%s 对象覆盖率 %d/%d（%.0f%%）——弱检查，摘要式条件以人工核对为准"
                      % (mark, cond_hit, cond_total, ratio * 100))

            # ── 标题行数声明核对（2026-08-26）："N 行/句全有落点" 与源实际行数不符 = WARN ──
            for htext in RE_HEADING.findall(md_text):
                if event_id in htext:
                    m = re.search(r"(\d+)\s*(?:行|句)全有落点", htext)
                    if m and int(m.group(1)) != len(ev["lines"]):
                        print("    [WARN] 标题声称「%s 行全有落点」，源实际 %d 行——行数以工具实测为准" % (m.group(1), len(ev["lines"])))

            # ── 数据驱动字段检查（2026-08-26）：trigger ∈ 注册表；once/priority 枚举；注释欠账提示 ──
            blocks_txt = RE_CODEBLOCK.findall(md_text)
            evt_block = next((b for b in blocks_txt if '"condition"' in b and '"id": "%s"' % event_id in b), None)
            if evt_block is not None:
                tm = re.search(r'"trigger"\s*:\s*"([^"]+)"', evt_block)
                if not tm:
                    print("    [FAIL] 缺 trigger 字段（数据驱动：触发时机必须落 JSON，禁止只写注释）")
                    exit_code = max(exit_code, 1)
                elif tm.group(1) not in TRIGGER_REGISTRY:
                    print("    [FAIL] trigger=%s ∈ 注册表外（01/16 trigger 注册表 v1）" % tm.group(1))
                    exit_code = max(exit_code, 1)
                if re.search(r'"once"\s*:', evt_block) and not re.search(r'"once"\s*:\s*(true|false)', evt_block):
                    print("    [WARN] once 值非法（true/false）")
                pm = re.search(r'"priority"\s*:\s*"([^"]+)"', evt_block)
                if pm and pm.group(1) not in ("normal", "weak"):
                    print("    [FAIL] priority=%s ∈ 枚举外（normal/weak）" % pm.group(1))
                    exit_code = max(exit_code, 1)
                if "触发（TK5 原文）" in evt_block and '"trigger"' not in evt_block:
                    print("    [WARN] 注释含触发原文但无 trigger 字段——数据驱动欠账（触发时机必须落 JSON）")

            # ── 演出行检查（强）：每一行必须有原文落点 ──
            missing = []
            ref_hits = []
            cross_miss = []   # 白名单对称缺失（多文件模式）
            for idx, (spk, text) in enumerate(ev["lines"], 1):
                n = norm(text)
                if not n:
                    continue
                if any(n in c for c in all_norm):
                    continue
                # 未命中 → 2-gram 重合检测（引用/迁移注释可能不带全文）
                best = max((overlap_ratio(n, c) for c in all_norm), default=0.0)
                if best >= 0.6:
                    ref_hits.append((idx, spk, text, best))
                    continue
                # 白名单豁免
                allow = not strict and any(idx in rows for rows in ALLOWLIST.get(event_id, {}).values())
                if allow:
                    reasons = [r for r, rows in ALLOWLIST.get(event_id, {}).items() if idx in rows]
                    ref_hits.append((idx, spk, text, 0.0))
                    # 🔴 白名单对称（2026-08-26）：多文件运行时，豁免行必须在其它文件也有原文落点
                    if len(args) > 1:
                        n2 = norm(text)
                        if not any(n2 in c for op, c in pools.items() if op != md_path):
                            cross_miss.append((idx, reasons[0]))
                    continue
                missing.append((idx, spk, text))

            n_ok = len(ev["lines"]) - len(missing)
            print("    演出行：%d/%d 有原文落点" % (n_ok, len(ev["lines"])))
            if ref_hits:
                print("    [WARN] 引用/豁免落点 %d 行（结算唯一/09c 迁移，人工确认）："
                      % len(ref_hits))
                for idx, spk, text, ov in ref_hits:
                    print("        行 %d 【%s】%s（2-gram 重合 %.0f%%）" % (idx, spk, text[:40], ov * 100))
            if missing:
                exit_code = max(exit_code, 1)
                print("    [FAIL] 缺失 %d 行（TK5 原文没有对应注释）——违反铁律 7：" % len(missing))
                for idx, spk, text in missing:
                    print("        行 %d 【%s】%s" % (idx, spk, text[:60]))

            # 🔴 白名单对称缺失合并输出（2026-08-26）
            if cross_miss:
                idxs = [i for i, _ in cross_miss]
                rng = []
                s = e = idxs[0]
                for i in idxs[1:]:
                    if i == e + 1:
                        e = i
                    else:
                        rng.append(str(s) if s == e else "%d-%d" % (s, e)); s = e = i
                rng.append(str(s) if s == e else "%d-%d" % (s, e))
                reason = cross_miss[0][1]
                print("    [WARN] 白名单豁免 %d 行（%s）在其它剧本文件未找到原文落点——迁移/引用待产出，人工核对（09c 附节已登记）" % (len(cross_miss), ",".join(rng)))
                print("          原因：%s" % reason)

    print("\n结果：%s" % ("通过（无缺失）" if exit_code == 0 else "存在缺失/错误，详见上方 [FAIL]"))
    sys.exit(exit_code)


if __name__ == "__main__":
    main()

# -*- coding: utf-8 -*-
"""生成「骑砍2 原生表情」全量清单（机器基线 + 文档表格）。

数据来自游戏本体，不手抄：
  · 表情片段 = `Modules/Native/AssetPackages/morph_anims.tpac` 里的资产名（**能播的就是这些名字**）
  · 登记表   = `Modules/Native/ModuleData/voices.xml` 的 `face_animation_record`（带 do_blink / look_at_player 标志）
  · 版本     = `<游戏根>/bin/Win64_Shipping_Client/Version.xml`

用法：
    python Scripts/gen_face_animations.py                 # 预览
    python Scripts/gen_face_animations.py --write         # 写 Scripts/face_animations.csv
    python Scripts/gen_face_animations.py --markdown      # 打印文档用表格
    python Scripts/gen_face_animations.py --game <游戏根>  # 另加一个客户端（可重复，用于版本对照）

🔴 生成物，禁止手改：要改分组/中文说明改本文件后重跑。
"""
import io
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_CSV = os.path.join(ROOT, "Scripts", "face_animations.csv")
WORK = os.path.join(ROOT, "Debug", "offline", "_face_catalog")
TPACLI = os.path.join(ROOT, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
TPACLI_DEBUG = os.path.join(ROOT, "tools", "tpactool", "TpacToolCLI", "bin", "Debug", "net9.0", "tpaccli.exe")

# ── 分组规则：先匹配先生效 ──
GROUPS = [
    ("convo", lambda n: n.startswith("convo_")),
    ("talking", lambda n: n.startswith("talking_")),
    ("idle", lambda n: n.startswith("idle_")),
    ("look", lambda n: n.startswith("look")),
    ("pose", lambda n: n.startswith("pose_")),
    ("cutscene", lambda n: n.startswith("declare_dragon_banner_face") or n.startswith("cutscene_expression")),
    ("combat", lambda n: n.startswith("arrow_death_") or n.startswith("hitface") or n in ("death", "death2")),
    ("convo_variant", lambda n: n.endswith(("_ag", "_no", "_hi", "_we", "_co1", "_co2", "_ne"))),
    ("base", lambda n: n in ("male_custom", "male_facegen", "female_facegen", "female_custom",
                             "facegen_teeth", "portraits")),
    ("test", lambda n: n.startswith("face_")),
]

GROUP_ZH = {
    "emotion": "基础情绪（哭/笑/怒/怕…）",
    "convo": "对话待机与反应（原版会话用）",
    "convo_variant": "对话反应变体（按态度/身份分组）",
    "talking": "说话语气（挂在持续讲话上）",
    "idle": "常驻神态（醉/困/警戒/谄媚…）",
    "look": "视线与低头抬头",
    "pose": "过场摆姿",
    "cutscene": "剧情过场专用",
    "combat": "受击与死亡",
    "base": "脸基座（眨眼/看向玩家）",
    "test": "测试用",
}

# ── 中文说明：只给确定含义的名字；没写的不编 ──
ZH = {
    "cry": "哭", "happy": "开心", "angry": "生气", "rage": "暴怒", "shocked": "震惊",
    "scared": "害怕", "terrified": "惊恐", "sneer": "冷笑", "smirk": "得意地笑",
    "confused": "困惑", "suspicious": "起疑", "impressed": "佩服", "power": "强势",
    "hard_laugh": "大笑", "complimented": "被夸得受用", "scream": "尖叫", "pain": "疼痛",
    "extremepain": "剧痛", "yell": "喊叫", "grunt": "闷哼", "hit": "挨打",
    "wow": "惊叹", "yeah": "应声", "death": "死亡", "death2": "死亡（另一种）",
    "victory": "胜利", "shout": "高喊", "parry": "格挡", "aim": "瞄准", "aim_shake": "瞄准发抖",
    "release": "松手放箭", "lift": "举起", "carry_on": "继续干", "pleasure_to_meet_you": "幸会",
    "your_money": "你的钱（递钱）", "test": "测试", "test1": "测试1", "test2": "测试2",
    "talking_normal": "说话-平静", "talking_happy": "说话-高兴", "talking_angry": "说话-生气",
    "talking_sad": "说话-悲伤", "talking_mean": "说话-刻薄", "talking_engaged": "说话-投入",
    "convo_normal": "对话-平静（默认待机）", "convo_angry": "对话-生气", "convo_furious": "对话-暴怒",
    "convo_aggressive": "对话-挑衅", "convo_predatory": "对话-猎食者般的压迫",
    "convo_bared_teeth": "对话-龇牙", "convo_irritable": "对话-烦躁", "convo_insulted": "对话-被冒犯",
    "convo_annoyed": "对话-不耐烦", "convo_confused_annoyed": "对话-困惑又恼火",
    "convo_confused_normal": "对话-困惑", "convo_pondering": "对话-沉吟", "convo_thinking": "对话-思考",
    "convo_disbelief": "对话-难以置信", "convo_shocked": "对话-震惊", "convo_astonished": "对话-惊愕",
    "convo_dismayed": "对话-沮丧", "convo_grave": "对话-凝重", "convo_worried": "对话-担忧",
    "convo_beaten": "对话-被打垮", "convo_nervous": "对话-紧张", "convo_nervous2": "对话-紧张2",
    "convo_bored": "对话-无聊", "convo_bored2": "对话-无聊2", "convo_stern": "对话-严厉",
    "convo_very_stern": "对话-极严厉", "convo_happy": "对话-愉快", "convo_delighted": "对话-欣喜",
    "convo_merry": "对话-欢快", "convo_relaxed_happy": "对话-放松愉快", "convo_focused_happy": "对话-专注而愉快",
    "convo_huge_smile": "对话-大笑脸", "convo_innocent_smile": "对话-无辜地笑",
    "convo_calm_friendly": "对话-平和友善", "convo_approving": "对话-认可", "convo_grateful": "对话-感激",
    "convo_excited": "对话-兴奋", "convo_bemused": "对话-觉得好笑", "convo_nonchalant": "对话-漫不经心",
    "convo_contemptuous": "对话-轻蔑", "convo_disgusted": "对话-嫌恶", "convo_evil_smile": "对话-坏笑",
    "convo_snide_voice": "对话-阴阳怪气", "convo_empathic_voice": "对话-共情",
    "convo_uncomfortable": "对话-不自在", "convo_uncomfortable_voice": "对话-不自在（带声）",
    "convo_confused_voice": "对话-困惑（带声）", "convo_angry_voice": "对话-生气（带声）",
    "convo_focused_voice": "对话-专注（带声）",
    "convo_mocking_teasing": "对话-戏谑", "convo_mocking_revenge": "对话-嘲弄（复仇）",
    "convo_mocking_aristocratic": "对话-贵族式嘲弄", "convo_undecided_open": "对话-迟疑（开口）",
    "convo_undecided_closed": "对话-迟疑（闭嘴）",
    "idle_normal": "常驻-平常", "idle_angry": "常驻-生气", "idle_furious": "常驻-暴怒",
    "idle_sneer": "常驻-冷笑", "idle_insulted": "常驻-被冒犯", "idle_nervous": "常驻-紧张",
    "idle_scared": "常驻-害怕", "idle_terrified": "常驻-惊恐", "idle_bandit": "常驻-匪气",
    "idle_displeased": "常驻-不悦", "idle_tired": "常驻-疲惫", "idle_tired2": "常驻-疲惫2",
    "idle_flirty": "常驻-调情", "idle_concentration": "常驻-专注", "idle_pleased": "常驻-满意",
    "idle_drunk": "常驻-醉", "idle_noble": "常驻-贵族气", "idle_guard": "常驻-守卫",
    "idle_sick": "常驻-病态", "idle_despise": "常驻-鄙夷", "idle_repulsed": "常驻-厌恶",
    "idle_condescend": "常驻-居高临下", "idle_cheering1": "常驻-欢呼1", "idle_cheering2": "常驻-欢呼2",
    "idle_cheering3": "常驻-欢呼3", "idle_survive": "常驻-劫后余生", "idle_sleep": "常驻-睡着",
    "idle_lookaround": "常驻-四处张望", "idle_epicwin": "常驻-大获全胜", "idle_old": "常驻-老态",
    "idle_hero_low_hp": "常驻-濒死", "idle_sneer2": "常驻-冷笑2",
    "drink": "喝酒", "drink2": "喝酒2", "eat_worried": "吃东西-担心", "eat_pleased": "吃东西-满意",
    "eat_weird": "吃东西-古怪", "eat_happy": "吃东西-开心", "eat_all": "吃东西-狼吞虎咽",
    "lookdown": "低头", "lookup": "抬头", "lookleft": "看左", "lookright": "看右",
    "lookstupid": "发呆", "sad_face_cutscene": "悲伤（过场，1.5.7+）",
    # 脸基座
    "facegen_teeth": "牙齿（脸基座）", "female_custom": "自定义女脸", "female_facegen": "捏脸女脸",
    "male_custom": "自定义男脸", "male_facegen": "捏脸男脸", "portraits": "肖像默认脸",
    # 受击
    "hitfacefront": "正面受击", "hitfaceleft": "左侧受击", "hitfaceright": "右侧受击", "hitfaceup": "上方受击",
    # 战场口令的口型
    "archers": "口令-弓手", "attack": "口令-进攻", "cavalry": "口令-骑兵", "everyone": "口令-全员",
    "followme": "口令-跟我来", "infantry": "口令-步兵", "move": "口令-前进", "retreat": "口令-撤退",
    "smirkcon": "得意地笑（变体）", "face_smile1": "微笑（测试）", "face_smiling_hard": "大笑（测试）",
}


def die(msg):
    raise SystemExit(msg)


def tpaccli_path():
    for p in (TPACLI, TPACLI_DEBUG):
        if os.path.exists(p):
            return p
    die("tpaccli.exe not found; run: dotnet build -c Release in tools/tpactool")


def game_version(game):
    p = os.path.join(game, "bin", "Win64_Shipping_Client", "Version.xml")
    if not os.path.exists(p):
        return "unknown"
    s = io.open(p, encoding="utf-8-sig", errors="ignore").read()
    m = re.search(r'Singleplayer\s+Value="([^"]+)"', s)
    return m.group(1) if m else "unknown"


def morph_assets(game, ver):
    """tpac 里能查到的表情片段名。拷一份单包目录再 list（不硬链接、不动原目录）。"""
    src = os.path.join(game, "Modules", "Native", "AssetPackages", "morph_anims.tpac")
    if not os.path.exists(src):
        die("morph_anims.tpac not found under " + game)
    pack = os.path.join(WORK, "pack_" + ver.replace("/", "_"))
    if os.path.isdir(pack):
        shutil.rmtree(pack)
    os.makedirs(pack)
    shutil.copy2(src, os.path.join(pack, "morph_anims.tpac"))
    out = subprocess.run([tpaccli_path(), "list", "--packdir", pack], capture_output=True, text=True)
    names = []
    for line in (out.stdout or "").splitlines():
        line = line.strip()
        if not line or line.startswith("Loaded "):
            continue
        names.append(line.split("\t")[0].strip())
    if not names:
        die("tpaccli list returned nothing (stderr: %s)" % (out.stderr or "")[:200])
    return set(names)


def voice_records(game):
    """voices.xml 的登记名 + 标志（do_blink / look_at_player）。"""
    p = os.path.join(game, "Modules", "Native", "ModuleData", "voices.xml")
    if not os.path.exists(p):
        return {}
    root = ET.parse(p).getroot()
    recs = {}
    for r in root.iter("face_animation_record"):
        rid = r.get("id")
        if not rid:
            continue
        flags = sorted({f.get("name") for f in r.iter("flag") if f.get("name")})
        recs[rid] = flags
    return recs


def group_of(name):
    for g, fn in GROUPS:
        if fn(name):
            return g
    return "emotion"


# ── 有规律的名字按规则生成中文（编号类、对话反应变体类）──
REACTION_ZH = {"negative": "否定", "positive": "认同", "very_negative": "强烈否定",
               "very_positive": "强烈认同", "unsure": "迟疑", "trivial": "敷衍",
               "stand": "站姿", "start": "起手"}
STYLE_ZH = {"ag": "强势", "no": "平常", "hi": "带派", "we": "疲惫", "ne": "紧张",
            "co1": "收敛", "co2": "收敛2"}


def gloss(name):
    if name in ZH:
        return ZH[name]
    m = re.match(r"arrow_death_(\d+)$", name)
    if m:
        return "中箭倒地（变体 %s）" % m.group(1)
    m = re.match(r"lookdirection_(\d+)$", name)
    if m:
        return "看向方位 %s（12 向）" % m.group(1)
    m = re.match(r"pose_(\d+)$", name)
    if m:
        return "过场摆姿 %s" % m.group(1)
    m = re.match(r"declare_dragon_banner_face_(\d+)$", name)
    if m:
        return "举旗宣言过场表情 %s" % m.group(1)
    m = re.match(r"(very_negative|very_positive|negative|positive|unsure|trivial|stand|start)_"
                 r"(ag|no|hi|we|ne|co1|co2)$", name)
    if m:
        return "对话反应-%s·%s" % (REACTION_ZH[m.group(1)], STYLE_ZH[m.group(2)])
    return ""


def collect(games):
    asset_sets, rec_sets, per_version = {}, {}, {}
    for game in games:
        ver = game_version(game)
        asset_sets[ver] = morph_assets(game, ver)
        rec_sets[ver] = voice_records(game)
        per_version[ver] = len(asset_sets[ver])

    rows = {}
    for ver in asset_sets:
        for name in asset_sets[ver]:
            r = rows.setdefault(name, {"versions": set(), "assets": set(), "record": False, "flags": set()})
            r["versions"].add(ver)
            r["assets"].add(ver)
        for name, flags in rec_sets[ver].items():
            r = rows.setdefault(name, {"versions": set(), "assets": set(), "record": False, "flags": set()})
            r["versions"].add(ver)
            r["record"] = True
            r["flags"].update(flags)

    # 登记表里有、任何版本都没有资产 = 播不出来
    for r in rows.values():
        r["missing"] = not r["assets"]
    return rows, per_version


def build(games):
    rows, per_version = collect(games)
    out = []
    for name in sorted(rows, key=lambda n: (group_of(n), n)):
        r = rows[name]
        note = []
        if r.get("missing"):
            note.append("登记有、资产缺失（播不出来）")
        elif not r["record"]:
            note.append("资产有、登记表没有（可直接调）")
        if r.get("flags"):
            note.append("标志=" + "/".join(sorted(r["flags"])))
        out.append({
            "Name": name,
            "Group": group_of(name),
            "Zh": gloss(name),
            "Versions": "|".join(sorted(r["versions"])),
            "Note": "；".join(note),
        })
    return out, per_version


def write_csv(out):
    lines = ["Name,Group,Zh,Versions,Note"]
    for r in out:
        vals = [r["Name"], r["Group"], r["Zh"], r["Versions"], r["Note"]]
        lines.append(",".join('"%s"' % v.replace('"', '""') if ("," in v or '"' in v) else v for v in vals))
    io.open(OUT_CSV, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")


def print_markdown(out):
    print("| 名字 | 含义 | 版本 |")
    print("|---|---|---|")
    by_group = {}
    for r in out:
        if r["Note"].startswith("登记有、资产缺失"):
            continue
        by_group.setdefault(r["Group"], []).append(r)
    for g, rows in by_group.items():
        print("")
        print("**%s**（%s，%d 条）" % (g, GROUP_ZH.get(g, g), len(rows)))
        print("")
        for r in rows:
            ver = r["Versions"].replace("v1.2.12|v1.5.7", "1.2.12/1.5.x").replace("|", " / ")
            print("| `%s` | %s | %s |" % (r["Name"], r["Zh"] or "—", ver))


def update_doc(path, out):
    """把生成表格填进文档的两个标记之间（文档其余部分手写，不动）。"""
    BEGIN = "<!-- BEGIN GENERATED TABLE -->"
    END = "<!-- END GENERATED TABLE -->"
    if not os.path.exists(path):
        die("doc not found: " + path)
    s = io.open(path, encoding="utf-8").read()
    if BEGIN not in s or END not in s:
        die("markers missing in " + path)
    import contextlib
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        print_markdown(out)
    head, rest = s.split(BEGIN, 1)
    _, tail = rest.split(END, 1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(
        head + BEGIN + "\n\n" + buf.getvalue().strip() + "\n\n" + END + tail)
    print("updated doc", path)


def main():
    games = []
    argv = sys.argv[1:]
    doc_path = None
    i = 0
    while i < len(argv):
        if argv[i] == "--game" and i + 1 < len(argv):
            games.append(os.path.abspath(argv[i + 1]))
            i += 2
            continue
        if argv[i] == "--doc" and i + 1 < len(argv):
            doc_path = os.path.abspath(argv[i + 1])
            i += 2
            continue
        i += 1
    if not games:
        games = [os.path.abspath(os.path.join(os.path.dirname(ROOT), ".."))]  # Modules/<mod> 的上两级

    out, per_version = build(games)
    print("versions:", per_version)
    print("names:", len(out), " playable:", sum(1 for r in out if not r["Note"].startswith("登记有")))
    if "--markdown" in argv:
        print_markdown(out)
    if "--write" in argv:
        write_csv(out)
        print("wrote", OUT_CSV)
    if doc_path:
        update_doc(doc_path, out)


if __name__ == "__main__":
    main()
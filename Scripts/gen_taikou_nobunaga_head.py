# -*- coding: utf-8 -*-
"""生成 Taikou 模块里「织田信长专用头」的两个接线文件（铁律 22：生成物禁手改）。

产物：
    Taikou/ModuleData/skins.xml      ← <skins><race id="lwn_nobunaga"> 一份完整男性成年皮肤 </race></skins>
    Taikou/ModuleData/monsters.xml   ← <Monsters> 里**整族** Monster：
                                       lwn_nobunaga · _child · _settlement · _settlement_fast · _settlement_slow
                                       （= Native human 那一族的逐块副本，只改 id）

做法：从 Native 的同名节点**整块复制**，只改要改的字段——
  · skin：face_meta_mesh / face_textures 指向自定义头（默认=原版名 → 视觉零变化，用来先验机制）
  · monster：把 Native `human*` 那一族的每个 id 加 `lwn_nobunaga` 前缀（物理/骨骼/胶囊全部继承）
    🔴 **必须整族**——引擎按 race+后缀取变体，缺一个 = 该处取到 null → Mission.SpawnAgent NRE
    （2026-09-14 实机踩过，详见 build_monsters 注释）

为什么这么做：骑砍2 的头网格由 skin 决定，skin 按 (race × 性别 × 年龄段) 选。
要让「只有信长」换头，就必须给他一个**独有 race**，再只在 taikou_lords*.xml 里给
lord_tk5_195 加 race="lwn_nobunaga"——其他人仍走 Native 的 race="human"，零影响。

用法：
    python Scripts/gen_taikou_nobunaga_head.py                    # 默认：指向原版网格（跑机制验证）
    python Scripts/gen_taikou_nobunaga_head.py --face-mesh head_nobunaga_a --face-tex head_nobunaga_a
    python Scripts/gen_taikou_nobunaga_head.py --check            # 只比对现状，不写盘
"""
import io
import os
import re
import sys

def find_mb2():
    """游戏根目录：先环境变量，再注册表（铁律 19：进程快照不可信）。"""
    p = os.environ.get("MB2_PATH")
    if p and os.path.isdir(p):
        return p
    try:
        import winreg
        for hive, key in ((winreg.HKEY_CURRENT_USER, "Environment"),):
            with winreg.OpenKey(hive, key) as k:
                p = winreg.QueryValueEx(k, "MB2_PATH")[0]
                if p and os.path.isdir(p):
                    return p
    except Exception:
        pass
    raise SystemExit("FATAL: 找不到游戏目录，请设置 MB2_PATH")


MB2 = find_mb2()
NATIVE = os.path.join(MB2, "Modules", "Native", "ModuleData")
TAIKOU = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Taikou", "ModuleData"))

RACE_ID = "lwn_nobunaga"
MONSTER_ID = "lwn_nobunaga"

HEADER = (
    "<!-- \U0001f534 生成物\u00b7\u7981\u6b62\u624b\u6539\uff08\u94c1\u5f8b 22\uff09\u2014\u2014"
    "\u7531 Scripts/gen_taikou_nobunaga_head.py \u4ece Native \u540c\u540d\u8282\u70b9\u6574\u5757\u590d\u5236\u751f\u6210\u3002\n"
    "     \u6539\u5185\u5bb9 = \u6539\u811a\u672c\u91cd\u8dd1\u3002\n"
    "     \u7528\u9014\uff1a\u7ec7\u7530\u4fe1\u957f\uff08lord_tk5_195\uff09\u4e13\u7528 race \u2014\u2014"
    "\u53ea\u6709\u4ed6\u5728 taikou_lords*.xml \u91cc\u5e26 race=\"%s\"\uff0c\u5176\u4ed6\u4eba\u4ecd\u8d70 race=\"human\"\u3002 -->\n"
) % RACE_ID


def read(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return f.read()


def write(path, text):
    with io.open(path, "w", encoding="utf-8-sig", newline="\n") as f:
        f.write(text)


def sub_outside_comments(text, pattern, repl, flags=0):
    """只在 XML 注释之外做替换（Native 的 skin 块里有被注释掉的 face_texture 样例）。"""
    out, pos, hits = [], 0, 0
    for m in re.finditer(r"<!--.*?-->", text, re.S):
        seg, sub = re.subn(pattern, repl, text[pos:m.start()], flags=flags)
        out.append(seg)
        out.append(m.group(0))
        hits += sub
        pos = m.end()
    seg, sub = re.subn(pattern, repl, text[pos:], flags=flags)
    out.append(seg)
    return "".join(out), hits + sub


def extract_block(src, tag, id_attr, id_val):
    """从 src 里抠出 <tag ... id="id_val" ...> ... </tag> 整块（含缩进）。"""
    pat = re.compile(r'<%s\b[^>]*\b%s="%s"' % (tag, id_attr, re.escape(id_val)))
    m = pat.search(src)
    if not m:
        raise SystemExit("FATAL: 在源文件里找不到 <%s %s=\"%s\">" % (tag, id_attr, id_val))
    start = src.rfind("\n", 0, m.start()) + 1
    end = src.find("</%s>" % tag, m.end())
    if end < 0:
        raise SystemExit("FATAL: <%s id=%s> 没有闭合" % (tag, id_val))
    end = src.find("\n", end) + 1
    return src[start:end]


def build_skins(face_mesh, face_tex, min_scale=None):
    native = read(os.path.join(NATIVE, "skins.xml"))
    human_block = extract_block(native, "race", "id", "human")
    # 取 human race 里的「男性成年」skin（整块）
    blocks = [b for b in re.findall(r'<skin\b.*?</skin>\n', human_block, re.S)
              if 'gender="0"' in b and 'mesh_maturity_type="adult"' in b]
    if len(blocks) != 1:
        raise SystemExit("FATAL: human race 里男性成年 skin 找到 %d 份（应为 1）" % len(blocks))
    skin_block = blocks[0]

    # ① face_meta_mesh
    new_skin, n = re.subn(r'face_meta_mesh="[^"]*"', 'face_meta_mesh="%s"' % face_mesh, skin_block, count=1)
    if n != 1:
        raise SystemExit("FATAL: skin 里没找到 face_meta_mesh 属性")
    # ② face_textures：条数保持 4（引擎按索引取，条数变了会越界 —— 蒂法工程 §11.2 根因 C）
    def swap_face_tex(m):
        return re.sub(r'name="[^"]*"', 'name="%s"' % face_tex, m.group(0), count=1)
    new_skin, n = sub_outside_comments(new_skin, r'<face_texture\b.*?</face_texture>', swap_face_tex, re.S)
    if n != 4:
        raise SystemExit("FATAL: face_texture 条数 = %d (原版为 4, 条数必须一致)" % n)
    # ③ 可选：改 min_scale —— 只用来做「race 到底生效没有」的肉眼标记（原版男=1.07）
    if min_scale is not None:
        new_skin, n = re.subn(r'min_scale="[^"]*"', 'min_scale="%s"' % min_scale, new_skin, count=1)
        if n != 1:
            raise SystemExit("FATAL: skin 里没找到 min_scale 属性")

    # 缩进：把整块右移一层（race 之下）
    body = "".join(("\t" + ln if ln.strip() else ln) for ln in new_skin.splitlines(True))
    return ('<?xml version="1.0" encoding="utf-8"?>\n<skins>\n'
            + HEADER
            + '\t<race\n\t\tid="%s">\n' % RACE_ID
            + body
            + "\t</race>\n</skins>\n")


def build_monsters():
    native = read(os.path.join(NATIVE, "monsters.xml"))
    # 🔴 引擎按「race + 后缀」取 Monster **变体**（`FaceGen.GetMonsterWithSuffix(race, "_settlement")`）。
    #    Native 给 human 定义的是**一族** id：
    #        human · human_child · human_settlement · human_settlement_fast · human_settlement_slow
    #    自建 race 必须**整族复刻**——少一个 = 引擎在该处取到 null 且**无兜底**
    #    → `AgentData.AgentMonster = FaceGen.GetBaseMonsterFromRace(...)` 或 `.Monster(suffix版)` = null
    #    → `Mission.SpawnAgent` 里 `agentBuildData.AgentMonster.Weight` 抛 NullReferenceException。
    #    2026-09-14 实机踩中：信长是 Lord → 进领主大厅走 `AddHeroToDecidedLocation` 的 LordsHall 分支
    #    → 要 `lwn_nobunaga_settlement` → 我们只造了 `lwn_nobunaga` → 一进大厅就崩。
    #    后缀用量实测（1.2.12 全 DLL 反编译计数）：_settlement 46 处 · _child 12 · _settlement_slow 5
    #    · _settlement_fast 1 —— 都不能缺。
    native_ids = re.findall(r'<Monster\b[^>]*?\bid="([^"]+)"', native, re.S)
    family = [i for i in native_ids if i == "human" or i.startswith("human_")]
    if "human" not in family:
        raise SystemExit('FATAL: Native monsters.xml 里没有 id="human"')

    body = ""
    for nid in family:
        new_id = MONSTER_ID + nid[len("human"):]      # human_child → lwn_nobunaga_child
        block = extract_block(native, "Monster", "id", nid)
        # 只改**开标签**里的那个 id（块内可能还有同名字串，别误伤）
        new_block, n = re.subn(
            r'(<Monster\b[^>]*?\bid=")%s(")' % re.escape(nid),
            lambda m: m.group(1) + new_id + m.group(2),
            block, count=1, flags=re.S)
        if n != 1:
            raise SystemExit('FATAL: monster id="%s" 的开标签 id 没改写成功' % nid)
        body += "".join(("\t" + ln if ln.strip() else ln) for ln in new_block.splitlines(True))

    return ('<?xml version="1.0" encoding="utf-8"?>\n<Monsters>\n'
            + HEADER
            + body
            + "</Monsters>\n")


def main():
    a = sys.argv[1:]
    check = "--check" in a

    def opt(key, default):
        return a[a.index(key) + 1] if key in a else default

    face_mesh = opt("--face-mesh", "head_male_a")
    face_tex = opt("--face-tex", face_mesh)
    min_scale = opt("--min-scale", None)

    skins = build_skins(face_mesh, face_tex, min_scale)
    monsters = build_monsters()

    out_skins = os.path.join(TAIKOU, "skins.xml")
    out_monsters = os.path.join(TAIKOU, "monsters.xml")

    print("race      = %s" % RACE_ID)
    print("monster   = %s (+ _child/_settlement/_settlement_fast/_settlement_slow)" % MONSTER_ID)
    print("face_mesh = %s" % face_mesh)
    print("face_tex  = %s" % face_tex)
    print("min_scale = %s" % (min_scale if min_scale else "(vanilla, unchanged)"))
    print("skins.xml    -> %s  (%d bytes)" % (out_skins, len(skins.encode("utf-8"))))
    print("monsters.xml -> %s  (%d bytes)" % (out_monsters, len(monsters.encode("utf-8"))))

    if check:
        for p, want in ((out_skins, skins), (out_monsters, monsters)):
            cur = read(p) if os.path.exists(p) else ""
            print("%-14s %s" % (os.path.basename(p), "IN SYNC" if cur == want else "**OUT OF SYNC**"))
        return

    write(out_skins, skins)
    write(out_monsters, monsters)
    print("OK written")


if __name__ == "__main__":
    main()

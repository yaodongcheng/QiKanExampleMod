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
    python Scripts/gen_taikou_nobunaga_head.py                    # 还原成原版外观（网格+4 条脸池+身高全回原版）
    python Scripts/gen_taikou_nobunaga_head.py --face-mesh head_nobunaga_a --face-tex head_nobunaga_a
    python Scripts/gen_taikou_nobunaga_head.py --min-scale 1.35   # 临时放大，用来肉眼验证 race 生效
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

PARAM_MARK = "gen-params:"


def header(extra=None):
    """生成物文件头。extra = 本次生成参数，写进文件；--check 靠它自描述读回（见 read_params）。"""
    s = ("<!-- 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_nobunaga_head.py "
         "从 Native 同名节点整块复制生成。\n"
         "     改内容 = 改脚本重跑。\n"
         "     用途：织田信长（lord_tk5_195）专用 race，只有他在 taikou_lords*.xml 里带 "
         "race=\"%s\"，其他人仍走 race=\"human\"。\n" % RACE_ID)
    if extra:
        s += "     %s %s\n" % (PARAM_MARK, extra)
    return s + " -->\n"


def read_params(path):
    """从已生成的文件里读回上次用的参数。

    为什么要它：`--check` 若拿**当前命令行参数**去比对，忘了照抄参数就会误报 OUT OF SYNC
    （pitfalls.md 已登记这条）。改成自描述——`--check` 优先按文件里记的参数重建再比。
    """
    if not os.path.exists(path):
        return None
    m = re.search(re.escape(PARAM_MARK) + r"\s*(.*?)\s*-->", read(path), re.S)
    if not m:
        return None
    d = {}
    for kv in m.group(1).split():
        if "=" in kv:
            k, v = kv.split("=", 1)
            d[k] = None if v == "(none)" else v
    return d


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


def build_skins(face_mesh, face_tex, min_scale=None, mouth_tex=None, freeze_face=False, no_hair=False):
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
    #    --face-tex 给 1 个名字 = 4 条全指它（自定义头，照抄蒂法/萨菲罗斯的做法）；
    #               给 4 个（逗号分隔）= 逐条替换（🔴 还原原版必须走这条：原版是 a/b/c/d 四种脸池，
    #               全指同一个不等于原版，是"永远用同一个池"）
    names = [x.strip() for x in face_tex.split(",") if x.strip()]
    if len(names) == 1:
        names = names * 4
    if len(names) != 4:
        raise SystemExit("FATAL: --face-tex 需要 1 个或 4 个名字（逗号分隔），收到 %d 个" % len(names))

    seq = iter(names)

    def swap_face_tex(m):
        blk = re.sub(r'name="[^"]*"', 'name="%s"' % next(seq), m.group(0), count=1)
        # 🔴 lod_material 也要一起换。原版写的是 `head_male_a.lod`（**原版材质**）——
        #    不换 = 引擎在 LOD 路径上拿原版脸贴图渲染（实机症状：头顶露出一块原版头皮/肤色）。
        #    两个实机通过的自定义头（蒂法/萨菲罗斯）这里写的都是**自己的名字**（与 name 同值）；
        #    还原原版时保持原样不动。
        if face_mesh != "head_male_a":
            blk = re.sub(r'lod_material="[^"]*"', 'lod_material="%s"' % face_mesh, blk, count=1)
        return blk
    new_skin, n = sub_outside_comments(new_skin, r'<face_texture\b.*?</face_texture>', swap_face_tex, re.S)
    if n != 4:
        raise SystemExit("FATAL: face_texture 条数 = %d (原版为 4, 条数必须一致)" % n)
    # ③ 可选：mouth_textures 覆盖 —— 🔴 与 face_textures 同一个机制（引擎按索引把皮肤里的
    #    嘴材质盖到嘴子网格上）。不覆盖 = 用**原版粉唇贴图**配我们的 UV → 实机 2026-09-14
    #    看到"嘴开花"（一块粉色糊斑）。条数不动、只换名字（块是整块复制的，条数天然一致）。
    if mouth_tex:
        def swap_mouth_tex(m):
            return re.sub(r'name="[^"]*"', 'name="%s"' % mouth_tex, m.group(0), count=1)
        new_skin, n_mouth = sub_outside_comments(
            new_skin, r'<mouth_texture\b.*?</mouth_texture>', swap_mouth_tex, re.S)
        if n_mouth == 0:
            raise SystemExit("FATAL: skin 里没找到 <mouth_texture>（原版应有若干条）")

    # ④ 可选：改 min_scale —— 只用来做「race 到底生效没有」的肉眼标记（原版男=1.07）
    if min_scale is not None:
        new_skin, n = re.subn(r'min_scale="[^"]*"', 'min_scale="%s"' % min_scale, new_skin, count=1)
        if n != 1:
            raise SystemExit("FATAL: skin 里没找到 min_scale 属性")

    # ⑤ 可选：冻结脸形通道 —— 把 1..59 号（脸形键）的 key_min/key_max 全设 0。
    #    原理：morph 值 = key_min + w×(key_max−key_min)；min=max=0 → 恒为 0 → 网格永远停在 Basis
    #    （Basis = 战无2 原脸）。**不冻的后果**（2026-09-14 实机）：角色自己的 facekey 权重是按
    #    **原版头**调的，套到我们搬来的 59 条位移场上会被放大 → "整张脸被拉宽"；
    #    而 ModKit 显示的是 Basis（不套权重），所以编辑器里看着正常、进游戏才变形。
    #    🔴 **只冻 1..59**：0(skinkey_post_edit) 与 60..63(weight/build/height/age) 是身体键，
    #       冻了会坏体格/身高。
    if freeze_face:
        def _freeze(m):
            blk = m.group(0)
            t = re.search(r'key_time_point="(\d+)"', blk)
            if not t or not (1 <= int(t.group(1)) <= 59):
                return blk
            blk = re.sub(r'key_min="[^"]*"', 'key_min="0"', blk)
            blk = re.sub(r'key_max="[^"]*"', 'key_max="0"', blk)
            return blk
        new_skin, n_frozen = sub_outside_comments(new_skin, r'<deform_key\b.*?/>', _freeze, re.S)
        if n_frozen < 59:
            raise SystemExit("FATAL: 冻脸只命中 %d 条（应 ≥59）——deform_key 结构变了？" % n_frozen)
        print("  [freeze-face] 冻结 %d 条脸形通道（key_min=key_max=0）" % n_frozen)

    # ⑥ 可选：只留"光头"一条发型 —— 我们的头**自带头发**（头发和脸壳在同一个 .0 子网格里），
    #    但角色的 body properties 是按 `face_key_template`（原版模板，如 BodyProperty.fighter_empire）
    #    随机生成的，**里面带一个原版发型索引** → 引擎会按它从 hair_meshes 里取一个**原版发型网格**
    #    挂到头上渲染。原版发型是照原版头（z→1.81）做的、我们更高（z→1.95），两者只会互相打架
    #    （实机症状：头顶露出一块肤色）——我们的头不需要引擎再给发型。
    #    做法：把 <hair_meshes> 里的条目砍到只剩第一条（无 name 属性 = Bald = 不渲染任何网格）。
    if no_hair:
        m = re.search(r'(<hair_meshes\b[^>]*>)(.*?)(</hair_meshes>)', new_skin, re.S)
        if not m:
            raise SystemExit("FATAL: skin 里没找到 <hair_meshes>")
        # 🔴 做法：**保留条目、只摘掉网格**（删 name / cover_type 属性），**不是删条目**。
        #    为什么：引擎按 body properties 里的发型索引去取列表项，删条目 = 存档里的旧索引越界
        #    （这引擎缺定义从不兜底，怪物后缀族就是活例子）。摘掉属性后条目还在、位置不变，
        #    但没有任何网格可渲染 = 等于光头。原版那条 Bald 本来就是"无 name + 只有 style_tags"，
        #    所以这种写法本身合法、有先例。
        #    ⚠️ 与老版本（删条目只留 Bald）的区别：那个会让列表长度从 29 变 1，老档索引必越界。
        def strip_mesh(mm):
            blk = re.sub(r'\s+name="[^"]*"', '', mm.group(0), count=1)
            blk = re.sub(r'\s+cover_type\d="[^"]*"', '', blk)
            return blk
        new_body, n_strip = sub_outside_comments(m.group(2), r'<hair_mesh\b[^>]*>', strip_mesh)
        left = re.findall(r'<hair_mesh\b[^>]*\sname=', re.sub(r'<!--.*?-->', '', new_body, flags=re.S))
        if left:
            raise SystemExit("FATAL: 还有 %d 条发型带 name（摘网格没摘干净）" % len(left))
        n_eff = len(re.findall(r'<hair_mesh\b', re.sub(r'<!--.*?-->', '', new_body, flags=re.S)))
        if n_eff < 29:
            raise SystemExit("FATAL: 条目数 %d < 29 —— 摘网格不该减少条目（越界风险就在这）" % n_eff)
        new_skin = new_skin[:m.start(2)] + new_body + new_skin[m.end(2):]
        print("  [no-hair] %d 条发型全部摘掉网格（条目保留 %d 条，索引不越界；引擎不再挂原版发型）"
              % (n_strip, n_eff))

    # 缩进：把整块右移一层（race 之下）
    body = "".join(("\t" + ln if ln.strip() else ln) for ln in new_skin.splitlines(True))
    return ('<?xml version="1.0" encoding="utf-8"?>\n<skins>\n'
            + header("face_mesh=%s face_tex=%s min_scale=%s no_hair=%s"
                     % (face_mesh, face_tex, min_scale or "(none)", "1" if no_hair else "0"))
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
            + header()
            + body
            + "</Monsters>\n")


def main():
    a = sys.argv[1:]
    check = "--check" in a

    def opt(key, default):
        return a[a.index(key) + 1] if key in a else default

    face_mesh = opt("--face-mesh", "head_male_a")
    # 默认 = 原版那 4 条脸池（a/b/c/d）逐条还原；换自定义头时**必须**显式给 --face-tex
    face_tex = opt("--face-tex", "head_male_a,head_male_b,head_male_c,head_male_d")
    if face_mesh != "head_male_a" and "--face-tex" not in a:
        raise SystemExit("FATAL: 换了 --face-mesh 却没给 --face-tex —— 会把脸池留在原版，脸和网格对不上")
    out_skins = os.path.join(TAIKOU, "skins.xml")
    out_monsters = os.path.join(TAIKOU, "monsters.xml")

    min_scale = opt("--min-scale", None)
    mouth_tex = opt("--mouth-tex", None)
    no_hair = "--no-hair" in a

    def derive_freeze():
        return ("--no-freeze-face" not in a) and ("--freeze-face" in a or face_mesh != "head_male_a")

    # --check 且没显式给参数 → 先按【文件里记着的参数】回填。
    #   🔴 顺序要紧：回填必须在 mouth_tex / freeze_face 【派生之前】——
    #      否则重建时那两项还是按默认 face_mesh(head_male_a) 算的（嘴不覆盖、脸不冻结）
    #      → 产物与落盘不符，`--check` 误报 OUT OF SYNC（2026-09-14 实测踩到）。
    if check and not any(k in a for k in ("--face-mesh", "--face-tex", "--min-scale", "--no-hair")):
        rec = read_params(out_skins)
        if rec:
            face_mesh = rec.get("face_mesh") or face_mesh
            face_tex = rec.get("face_tex") or face_tex
            min_scale = rec.get("min_scale")
            no_hair = rec.get("no_hair") == "1"
            print("(check 按文件里记的参数重建)")

    # 嘴材质默认跟着 face_mesh 走（`<face_mesh>_mouth`）；换回原版头时不覆盖
    if mouth_tex is None and face_mesh != "head_male_a":
        mouth_tex = face_mesh + "_mouth"
    freeze_face = derive_freeze()
    skins = build_skins(face_mesh, face_tex, min_scale, mouth_tex, freeze_face, no_hair)
    monsters = build_monsters()

    print("race      = %s" % RACE_ID)
    print("monster   = %s (+ _child/_settlement/_settlement_fast/_settlement_slow)" % MONSTER_ID)
    print("face_mesh = %s" % face_mesh)
    print("face_tex  = %s" % face_tex)
    print("min_scale = %s" % (min_scale if min_scale else "(vanilla, unchanged)"))
    print("no_hair   = %s" % ("yes (只留 Bald，引擎不挂原版发型)" if no_hair else "no (保留原版发型表)"))
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

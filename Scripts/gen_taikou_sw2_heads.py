# -*- coding: utf-8 -*-
"""gen_taikou_sw2_heads.py —— 给战无2 的 28 个有名武将各生成一个【专属 race】。

为什么需要：骑砍2 的头网格由**皮肤**决定，而皮肤是按 (race × 性别 × 年龄段) 选的 ——
所以"给某一个人换头"绕不开给他开新 race。这是本工程最贵的发现，见
Knowledge/战国无双换装工程.md §2。

本脚本 = `gen_taikou_nobunaga_head.py`（只做信长一个人）的**多角色版**：
    · 28 个 race（男 22 / 女 6）一次生成到 Taikou\\ModuleData\\skins.xml
    · 同时产出 Taikou\\ModuleData\\AssetRegistry\\RaceGenders.xml（race → 允许性别）——
      LWN 捏脸「种族」下拉按性别置灰的唯一依据；**不能**改成运行时从角色反推（见 OUT_RACEGENDERS 注释）
    · 每个 race 的 Monster **整族 5 个**（human/human_child/human_settlement/... 的复刻）
      —— 少一个 = 进据点领主大厅 `Mission.SpawnAgent` 直接 NRE（信长那次实机踩过）
    · 每个 race 的皮肤照抄 Native 对应性别的**全部 5 档年龄段**，每块只换这几处：
        face_meta_mesh / face_textures（条数不动，只换名字）/ mouth_textures / lod_material
        + 冻结 1..59 脸形键（战无2 的位移场是从蒂法移植的，原版的 320 键权重会把脸拉坏）
        + 摘掉发型表的网格名（我们的头自带头发，原版发型会打架）

🔴 **为什么是 5 档而不是 1 档成年**（2026-09-15）：引擎按 (race × 性别 × 年龄段) 取 skin，
只做成年块 → 落在 teenager/tween/child/toddler 档的角色**在本 race 里没有 skin**，
取不到的行为没有保证。实测太阁六代共 **13 个带 race 的领主年龄 ≤17**（家康 16 岁登场、
幸村 16 岁登场、武藏 16 岁…），而且**子代会继承同性别家长的 race**
（`HeroCreator.DeliverOffSpring` → `CreateNewHero(同性别家长的 CharacterObject)`），
子代从小到大要跨过全部 5 档。范本：Shokuho / 宜古三国都对 human 给了整族 10 块。

用法：
    python Scripts/gen_taikou_sw2_heads.py              # 生成
    python Scripts/gen_taikou_sw2_heads.py --check      # 只校验是否最新
    python Scripts/gen_taikou_sw2_heads.py --selfcheck  # 语义自检（C2 六条，见 selfcheck()）
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools", "sw2-pipeline"))

# 复用信长那个生成器的 helper（find_mb2 / read / write / extract_block / sub_outside_comments / header）
import gen_taikou_nobunaga_head as G   # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

from parts_table import TABLE          # noqa: E402  （28 人的挑件表，含 Taikou StringId 与性别）

# race id 一律 lwn_<战无2 文件名的角色段>；与 parts_table 的 asset 名对齐
def race_id(key):
    return "lwn_" + key.split("_", 1)[1].lower()


# 🔴 颅骨版优先（2026-09-18 用户裁定）
#    规则：`TifaHead2\AssetSources\head\<名>\fullhead\` **存在** → 用颅骨版（网格/贴图名加 `_skull` 后缀）；
#          不存在 → 沿用原 mesh。
#    ⚠️ **race id 不受影响**（仍由原 asset 派生）—— `taikou_lords*.xml` 里的 `race="lwn_xx"` 引用照旧，
#       所以这条规则只换"头长什么样"，不动任何角色接线。
#    为什么放编辑器工程路径上判：`fullhead\` 就是"这个人的颅骨版做完了"的唯一权威标记
#    （交付目录 = `AssetSources\head\<名>\fullhead\`，见 总纲 §4.15.0 第 5 步）。
EDITOR_HEADS = os.path.join(G.MB2, "Modules", "TifaHead2", "AssetSources", "head")


def head_asset(r):
    """返回 (实际要写进 skins.xml 的头资产名, 是不是颅骨版)。"""
    slug = r["asset"][len("head_"):-len("_a")]
    if os.path.isdir(os.path.join(EDITOR_HEADS, slug, "fullhead")):
        return r["asset"] + "_skull", True
    return r["asset"], False


OUT_SKINS = os.path.join(G.MB2, "Modules", "Taikou", "ModuleData", "skins.xml")
OUT_MONSTERS = os.path.join(G.MB2, "Modules", "Taikou", "ModuleData", "monsters.xml")
# 🔴 种族 → 允许性别（LWN 捏脸「种族」下拉按性别置灰用）。**必须是这张离线表**：
#    运行时从角色反推不行 —— 六代领主是**按时代互斥加载**的，某一代没有的 race 就查不到使用者，
#    表里缺项 = 过滤失效 = 玩家能点到性别不符的 race = native AV（2026-09-16 实机 1598 代踩过：
#    该代只有 18 个带 lwn_ race 的领主，另外 10 个男 race 漏网）。
RACEGENDERS_REL = os.path.join("AssetRegistry", "RaceGenders.xml")
OUT_RACEGENDERS = os.path.join(G.MB2, "Modules", "Taikou", "ModuleData", RACEGENDERS_REL)


def skin_blocks(native, gender):
    """从 Native human race 里取该性别的**全部年龄段** skin → [(maturity, 块文本)]。

    Native 男/女各 5 档（adult / teenager / tween / child / toddler），顺序按 Native 原文件。
    🔴 不能只取成年：见文件头。缺哪一档，落在那一档的角色就没 skin 可用。
    """
    human = G.extract_block(native, "race", "id", "human")
    gflag = 'gender="0"' if gender == "male" else 'gender="1"'
    out = []
    for b in re.findall(r'<skin\b.*?</skin>\n', human, re.S):
        if gflag not in b:
            continue
        m = re.search(r'mesh_maturity_type="([^"]+)"', b)
        if not m:
            raise SystemExit("FATAL: 有一个 %s skin 没写 mesh_maturity_type" % gender)
        out.append((m.group(1), b))
    if len(out) != 5:
        raise SystemExit("FATAL: human race 的 %s skin 找到 %d 份（应为 5 档年龄段：%s）"
                         % (gender, len(out), " ".join(m for m, _ in out)))
    return out


def build_skin(native, asset, gender):
    """→ [(maturity, 改好的 skin 文本, 摘掉的发型条目数)]；每档一套，规则同一份。"""
    out = []
    for maturity, block in skin_blocks(native, gender):
        skin = block

        # ① 头网格（Native 各档用的都是同一个 head_male_a，引擎按年龄缩放；我们照此对每档都换）
        skin, n = re.subn(r'face_meta_mesh="[^"]*"', 'face_meta_mesh="%s"' % asset, skin, count=1)
        if n != 1:
            raise SystemExit("FATAL: %s/%s 的 skin 里没找到 face_meta_mesh" % (asset, maturity))

        # ② 脸池：**条数一条不动**，只把名字全换成我们的（引擎按索引取，条数变了会越界）。
        #    🔴 条数一律**排除注释后**数 —— Native 的 skin 里躺着被注释掉的样例 face_texture，
        #    按裸文本数会多数（实测成年男：裸 7 条、真 4 条）。
        _, want_ft = G.sub_outside_comments(block, r'<face_texture\b.*?</face_texture>',
                                           lambda m: m.group(0), re.S)
        cnt = [0]
        def swap_face(m):
            cnt[0] += 1
            b = re.sub(r'name="[^"]*"', 'name="%s"' % asset, m.group(0), count=1)
            b = re.sub(r'lod_material="[^"]*"', 'lod_material="%s"' % asset, b, count=1)
            return b
        skin, n = G.sub_outside_comments(skin, r'<face_texture\b.*?</face_texture>', swap_face, re.S)
        if n != want_ft or cnt[0] != want_ft:
            raise SystemExit("FATAL: %s/%s 脸池条数变了（%d → %d）" % (asset, maturity, want_ft, cnt[0]))

        # ③ 嘴：同一机制（不换 = 引擎拿原版粉唇材质盖到嘴子网格上 → 实机"嘴开花"）
        def swap_mouth(m):
            return re.sub(r'name="[^"]*"', 'name="%s_mouth"' % asset, m.group(0), count=1)
        skin, n_mouth = G.sub_outside_comments(skin, r'<mouth_texture\b.*?</mouth_texture>', swap_mouth, re.S)

        # ④ 冻结 1..59 脸形键：战无2 的 59 条位移场是从蒂法移植来的，
        #    原版 320 键的权重是按原版头调的，套上去会把脸拉变形（信长当年实测过）。
        #    🔴 只冻 1..59：0/60/61/62/63 是身体键（体格/身高/年龄），冻了会坏体格。
        def freeze(m):
            t = re.search(r'key_time_point="(\d+)"', m.group(0))
            if not t:
                return m.group(0)
            tp = int(t.group(1))
            if 1 <= tp <= 59:
                b = re.sub(r'key_min="[^"]*"', 'key_min="0.0"', m.group(0), count=1)
                b = re.sub(r'key_max="[^"]*"', 'key_max="0.0"', b, count=1)
                return b
            return m.group(0)
        skin, _ = G.sub_outside_comments(skin, r'<deform_key\b[^>]*/>', freeze, re.S)

        # ⑤ 摘掉发型表的网格名（条目数与 style_tags 保留 —— 存档里的发型索引不能越界）
        def strip_hair(m):
            return re.sub(r'\s+name="[^"]*"|\s+cover_type\d="[^"]*"', '', m.group(0))
        skin, n_hair = G.sub_outside_comments(skin, r'<hair_mesh\b[^>]*>', strip_hair, re.S)

        out.append((maturity, skin, n_hair))
    return out


def build_race_block(asset, gender, native, mesh=None):
    """asset = **原资产名**（race id 由它派生，永远不带后缀）；
    mesh  = 实际写进网格/贴图字段的名字（默认 = asset；颅骨版 = asset + "_skull"）。
    🔴 race id 必须由**原资产名**派生 —— 它挂在 `taikou_lords*.xml` 的 `race="lwn_xx"` 上，
       带上 `_skull` 会让全部引用失配（2026-09-18 差点写进去）。"""
    mesh = mesh or asset
    rid = "lwn_" + asset[len("head_"):-len("_a")]
    body = '\t<race\n\t\t\tid="%s">\n' % rid
    n_hair = 0
    for _maturity, skin, nh in build_skin(native, mesh, gender):
        n_hair = nh                                    # 各档摘掉的条数一致，取一份打印即可
        body += "".join(("\t\t" + ln if ln.strip() else ln) for ln in skin.splitlines(True))
    body += "\t</race>\n"
    return rid, body, n_hair


def tag_close(src, pos):
    """从 pos 起找**开标签**的收尾 `>`（跳过属性值引号里的 `>`）→ (位置, 是否自闭合)。"""
    in_q, i = False, pos
    while i < len(src):
        c = src[i]
        if in_q:
            if c == '"':
                in_q = False
        elif c == '"':
            in_q = True
        elif c == ">":
            return i, src[i - 1] == "/"
        i += 1
    raise SystemExit("FATAL: 开标签没有收尾 `>`")


def extract_monster(src, nid):
    """抠出 `<Monster id="nid" …>` **这一个**元素（含缩进与行尾）。

    🔴 为什么不能复用 `G.extract_block`：它按「下一个 `</Monster>`」结尾，而 Native 的
    Monster 元素**两种写法混用** —— `human` 是长元素（自带 `</Monster>`），
    `human_child` / `human_settlement*` 是自闭合的 `… />`，**根本没有 `</Monster>`**。
    于是抠 `human` 会把后面几个邻居一起带出来，而改名只改第一个 → 生成物里
    `human_settlement` 重复 28 次、`horse` 重复 56 次（2026-09-14 实测 252 条 = 140 真 + 112 垃圾）。
    重复 Monster id 在引擎里的行为未定义，属于不该写进包的东西。
    """
    m = re.search(r'<Monster\b[^>]*?\bid="%s"' % re.escape(nid), src, re.S)
    if not m:
        raise SystemExit('FATAL: 在 Native monsters.xml 里找不到 <Monster id="%s">' % nid)
    gt, self_closing = tag_close(src, m.end())
    if self_closing:
        end = src.find("\n", gt) + 1
    else:
        close = src.find("</Monster>", gt)
        if close < 0:
            raise SystemExit('FATAL: <Monster id="%s"> 没有闭合' % nid)
        end = src.find("\n", close) + 1
    return src[src.rfind("\n", 0, m.start()) + 1:end]


def _skins_of(text, race=None):
    """文本 → {(race, gender, maturity): face_texture 条数}，**条数一律排除注释**。

    🔴 不能按裸文本数：Native 的 skin 里躺着被注释掉的样例 `face_texture`（成年男裸数 7、真数 4），
    按裸文本数会让"我少了一条"和"基准本来就多"互相抵消 —— 这条自检就永远看不出问题（实测踩过）。
    """
    out = {}
    pat = r'<race\s+id="([^"]+)">(.*?)</race>' if race is None else \
          r'<race\s+id="%s">(.*?)</race>' % re.escape(race)
    for m in re.finditer(pat, text, re.S):
        name, body = (m.group(1), m.group(2)) if race is None else (race, m.group(1))
        for b in re.findall(r'<skin\b.*?</skin>\n', body, re.S):
            g = re.search(r'gender="(\d)"', b)
            mt = re.search(r'mesh_maturity_type="([^"]+)"', b)
            if not (g and mt):
                continue
            _, n = G.sub_outside_comments(b, r'<face_texture\b.*?</face_texture>',
                                          lambda mm: mm.group(0), re.S)
            out[(name, g.group(1), mt.group(1))] = n
    return out


def selfcheck(skins_text, monsters_text, module_data_dir, native_skins):
    """C2 六条语义自检（**建 50 个 race 靠人记这几条必出错**，所以做成脚本）：

    ① Monster 整族齐不齐 ② skin 的性别×年龄段覆盖 ③ face_textures 条数 ④ race/monster/NPC 三处 id 一致
    ⑤ 领主性别 ⊆ 该 race 的 skin 性别（对不上 = 运行期取不到皮肤，落兜底皮肤）
    ⑥ 捏脸过滤表 AssetRegistry/RaceGenders.xml 与 skin 性别逐条一致（错一条 = 玩家点得到 = native AV）
    → (是否全过, 报告行列表)
    """
    ok = True
    out = []
    base = _skins_of(native_skins, race="human")
    got = _skins_of(skins_text)
    races = {k[0] for k in got}

    # 解析生成的 monsters：race → 整族 id 集合（Monster id = race id + Native 族名的后缀）
    want_family = set()
    for mid in re.findall(r'<Monster\b[^>]*?\bid="([^"]+)"',
                          G.read(os.path.join(G.NATIVE, "monsters.xml")), re.S):
        if mid == "human" or mid.startswith("human_"):
            want_family.add(mid)
    mon_family = {}
    for mid in re.findall(r'<Monster\b[^>]*?\bid="([^"]+)"', monsters_text, re.S):
        for rid in races:
            if mid == rid or mid.startswith(rid + "_"):
                mon_family.setdefault(rid, set()).add(mid)

    # ④ 领主文件里真正用到的 race= 属性（**按人记账**：记下每个 race 被哪个性别的领主用了，给 ⑤ 用）
    users = {}          # race id -> 使用者性别的集合（"m"/"f"）
    md = module_data_dir
    if os.path.isdir(md):
        for fn in sorted(os.listdir(md)):
            if re.match(r'taikou_lords.*\.xml$', fn):
                for tag in re.findall(r'<NPCCharacter\b[^>]*>',
                                      G.read(os.path.join(md, fn)), re.S):
                    m_race = re.search(r'race="([^"]+)"', tag)
                    if not m_race:
                        continue
                    users.setdefault(m_race.group(1), set()).add(
                        "f" if 'is_female="true"' in tag else "m")
    used = set(users)
    if not used:
        ok = False
        out.append("❌ 六代领主文件里一个 race= 都没读到（路径不对？%s）" % md)

    for rid in sorted(races):
        per = {(g, mt): n for (r_, g, mt), n in got.items() if r_ == rid}
        genders = {g for g, _ in per}
        mats = {mt for _, mt in per}
        # ② 年龄段覆盖：该性别的 5 档必须齐（只做成年 = 16~19 岁角色无 skin，见文件头）
        want_mats = {mt for (_r, g, mt) in base if g in genders}
        if not genders or mats != want_mats:
            ok = False
            out.append("❌ %s 年龄段覆盖 %s，应为 %s" % (rid, sorted(mats), sorted(want_mats)))
        # ③ face_textures 条数必须与 Native 同格相同（引擎按索引取，条数变了会越界）
        for (g, mt), n in sorted(per.items()):
            want = base.get(("human", g, mt))
            if want is not None and n != want:
                ok = False
                out.append("❌ %s 的 gender=%s/%s 档 face_textures %d 条，Native 是 %d 条"
                           % (rid, g, mt, n, want))
        # ① Monster 整族：race id 加上 Native 族名的后缀，一个都不能少
        want_ids = {rid + w[len("human"):] for w in want_family}
        if mon_family.get(rid, set()) != want_ids:
            ok = False
            out.append("❌ %s 的 Monster 族不齐：缺 %s"
                       % (rid, sorted(want_ids - mon_family.get(rid, set()))))
        # ⑤ 用这个 race 的领主，性别必须落在该 race 的 skin 性别里
        #    （skin 按 race × 性别 选：性别对不上 = 引擎取不到皮肤 → 落兜底皮肤，身体/头对不上。
        #     运行期那道防线 = 捏脸「种族」下拉按性别置灰 FaceGenRaceGenderFilterPatch，本条是数据侧同一条不变量）
        #    ⚠️ 两个域要归一：skin 的 gender 是 "0"/"1"，使用者记的是 "m"/"f"
        skin_genders = {"f" if g == "1" else "m" for g in genders}
        bad = sorted(g for g in users.get(rid, ()) if g not in skin_genders)
        if bad:
            ok = False
            out.append("❌ %s 只做了 gender=%s 的 skin，却有%s领主在用（性别不匹配 → 兜底皮肤）"
                       % (rid, "/".join(sorted(genders)),
                          "、".join("女" if g == "f" else "男" for g in bad)))

    # ④ 三处 id 一致：领主文件用到的都在 skins 里有定义；定义了但没人用也报出来（多半是漏接）
    missing = sorted(used - races)
    unused = sorted(races - used)
    if missing:
        ok = False
        out.append("❌ 领主文件用了但 skins.xml 没定义的 race：%s" % " ".join(missing))
    if unused:
        out.append("⚠️ 定义了但六代领主文件都没用（多半是没接上 SPECIAL_RACE）：%s" % " ".join(unused))

    # ⑥ 捏脸过滤表（AssetRegistry/RaceGenders.xml）必须与 skins 的 skin 性别逐条一致 ——
    #    这张表是 LWN「种族」下拉按性别置灰的**唯一**依据，错一条 = 玩家能点到性别不符的 race = native AV
    rg_path = os.path.join(module_data_dir, "AssetRegistry", "RaceGenders.xml")
    if not os.path.exists(rg_path):
        ok = False
        out.append("❌ 缺 AssetRegistry/RaceGenders.xml（捏脸「种族」下拉按性别过滤要用它）")
    else:
        declared = dict(re.findall(r'<Race\s+id="([^"]+)"\s+gender="([^"]+)"', G.read(rg_path)))
        for rid in sorted(races):
            mine = {g for (r_, g, mt) in got if r_ == rid}
            want = ({"0"} if declared.get(rid) == "male" else
                    {"1"} if declared.get(rid) == "female" else None)
            if want is None or mine != want:
                ok = False
                out.append("❌ %s 的性别声明与 skin 不符：表说 %s，skin 是 gender=%s"
                           % (rid, declared.get(rid, "（缺这行）"), "/".join(sorted(mine))))
        extra = sorted(set(declared) - races)
        if extra:
            ok = False
            out.append("❌ RaceGenders.xml 里有 skins.xml 没定义的 race：%s" % " ".join(extra))

    out.append("统计：race %d 个 · skin %d 块 · Monster %d 个 · 领主文件用到 %d 个 race"
               % (len(races), len(got), sum(len(v) for v in mon_family.values()), len(used)))
    return ok, out


def main():
    check = "--check" in sys.argv
    self_check = "--selfcheck" in sys.argv
    native_skins = G.read(os.path.join(G.NATIVE, "skins.xml"))
    native_mon = G.read(os.path.join(G.NATIVE, "monsters.xml"))

    # ---- skins.xml ----
    races = []
    race_rows = []                      # (race id, 性别) —— 给 AssetRegistry/RaceGenders.xml
    for key, r in TABLE.items():
        hm, is_skull = head_asset(r)
        rid, body, n_hair = build_race_block(r["asset"], r["gender"], native_skins, mesh=hm)
        races.append(body)
        race_rows.append((rid, r["gender"]))
        print("  %-16s %-8s race=%-18s 头=%-26s 发型条目摘名 %d 条"
              % (key, r["gender"], rid, hm + ("（颅骨版）" if is_skull else ""), n_hair))
    skins = ('<?xml version="1.0" encoding="utf-8"?>\n<skins>\n'
             + G.header("SW2 28 人专属 race（男 22 / 女 6）")
             + "".join(races) + "</skins>\n")

    # ---- AssetRegistry/RaceGenders.xml：race → 允许性别（与 skins.xml 同源，同一个循环产出） ----
    racegenders = ('<?xml version="1.0" encoding="utf-8"?>\n'
                   '<!-- 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_sw2_heads.py '
                   '从挑件表（tools/sw2-pipeline/parts_table.py）生成。\n'
                   '     改内容 = 改挑件表后重跑本脚本。\n'
                   '     用途：LWN 捏脸/建号的「种族」下拉**按性别过滤**——每个换头角色一个 race，\n'
                   '           而一个 race 只做单一性别的 skin（性别不符 = 引擎取不到皮肤 = native AV）。\n'
                   '     消费方：LWN CampaignMode/RaceGenderRegistry.cs（读盘，不走 MBObjectManager）。\n'
                   '     键 = race StringId（与 skins.xml 的 <race id> 同键）；gender = male / female。\n'
                   '     🔴 这张表**不能**改成运行时从角色反推：六代领主按时代互斥加载，\n'
                   '        某一代没有的 race 查不到使用者 = 过滤失效（2026-09-16 实机崩过一次）。\n'
                   ' -->\n'
                   '<RaceGenders>\n'
                   + "".join('\t<Race id="%s" gender="%s" />\n' % (rid, g) for rid, g in race_rows)
                   + '</RaceGenders>\n')

    # ---- monsters.xml：每个 race 整族 5 个 ----
    native_ids = re.findall(r'<Monster\b[^>]*?\bid="([^"]+)"', native_mon, re.S)
    family = [i for i in native_ids if i == "human" or i.startswith("human_")]
    if "human" not in family:
        raise SystemExit('FATAL: Native monsters.xml 里没有 id="human"')
    monsters = []
    for key, r in TABLE.items():
        rid = "lwn_" + r["asset"][len("head_"):-len("_a")]
        for nid in family:
            new_id = rid + nid[len("human"):]
            block = extract_monster(native_mon, nid)
            new_block, n = re.subn(r'(<Monster\b[^>]*?\bid=")%s(")' % re.escape(nid),
                                   lambda m: m.group(1) + new_id + m.group(2),
                                   block, count=1, flags=re.S)
            if n != 1:
                raise SystemExit('FATAL: monster id="%s" 改写失败' % nid)
            monsters.append("".join(("\t" + ln if ln.strip() else ln)
                                    for ln in new_block.splitlines(True)))
    # 闭合自检：每个 race 恰好 5 个、全表 id 唯一（重复 id = 引擎行为未定义，禁止写进包）
    got = re.findall(r'<Monster\b[^>]*?\bid="([^"]+)"', "".join(monsters), re.S)
    dup = sorted({i for i in got if got.count(i) > 1})
    if len(got) != 5 * len(TABLE) or dup:
        raise SystemExit("FATAL: monster 表不干净（%d 条，应为 %d；重复 id：%s）"
                         % (len(got), 5 * len(TABLE), dup or "无"))
    mon = ('<?xml version="1.0" encoding="utf-8"?>\n<Monsters>\n'
           + G.header("SW2 28 人 race 的 Monster 整族（每个 5 个变体）")
           + "".join(monsters) + "</Monsters>\n")

    if check:
        ok = True
        for path, want in ((OUT_SKINS, skins), (OUT_MONSTERS, mon), (OUT_RACEGENDERS, racegenders)):
            cur = G.read(path) if os.path.exists(path) else ""
            same = cur == want
            ok = ok and same
            print("%s %s" % ("✅" if same else "❌ 不同步", path))
        print("结果：%s" % ("最新" if ok else "需要重跑（不加 --check）"))
        return 0 if ok else 1

    if self_check:
        # 语义自检跑在**磁盘上的产物**上（不只跑在内存里的新文本上）——
        # 这样它同时能查出"产物是旧的"以外的结构问题。不一致时先提示重跑。
        want = {OUT_SKINS: skins, OUT_MONSTERS: mon, OUT_RACEGENDERS: racegenders}
        disk = {p: (G.read(p) if os.path.exists(p) else "") for p in want}
        if disk != want:
            print("❌ 磁盘产物与生成器不一致 —— 先不加 --selfcheck 重跑一次")
            return 1
        ok, report = selfcheck(skins, mon, os.path.dirname(OUT_SKINS), native_skins)
        print("\n== 语义自检（C2 六条）==")
        for ln in report:
            print("   " + ln)
        print("结果：%s" % ("✅ 全过" if ok else "❌ 有问题（上面逐条）"))
        return 0 if ok else 1

    G.write(OUT_SKINS, skins)
    G.write(OUT_MONSTERS, mon)
    G.write(OUT_RACEGENDERS, racegenders)
    print("\n写出：\n  %s\n  %s\n  %s" % (OUT_SKINS, OUT_MONSTERS, OUT_RACEGENDERS))
    print("\n下一步：把 race= 接到 NPCCharacter 上（Scripts/gen_taikou_era_world.py 的 SPECIAL_RACE 表）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

# -*- coding: utf-8 -*-
"""gen_weapon_items.py —— 28 件「武将武器」的物品定义 + 中文名（**只新增，不改任何现有内容**）。

产出四处（都幂等：先删同 id 的旧块再写，重复跑 = 0 改动）
----------------------------------------------------------
  ① `Taikou\\ModuleData\\taikou_items\\weapons.xml` —— 28 个 `<Item>`（追加在官方拷贝件之后）
  ② `Taikou\\ModuleData\\Languages\\CNs\\std_Taikou_strings.xml` —— 28 条中文名
  ③ `item.csv` 登记 28 行
  ④ `TaikouHero.csv`「武器」列填 28 行

网格由 `tools/sw2-pipeline/build_weapons.py` 产出（`taikou_<slug>_weapon_a.fbx`），
尺寸从 `tools/armor-pipeline/out/weapon_dims.csv` 读（build_weapons.py 顺带写的）。

武器类型（5 类）
----------------
类型表 `WEAPON_OF` 按**武器形态**分（不是按长度自动分——军配/弓/铁炮这些长度和用法不成比例）：

    polearm  长柄（枪/薙刀）  TwoHandedPolearm  polearm_block_thrust
    sword2h  双手刀          TwoHandedSword    twohanded_block_swing_thrust
    sword1h  单手刀/短兵      OneHandedSword    onehanded_block_swing_thrust
    bow      弓              Bow               bow
    gun      铁炮            Crossbow          crossbow

🔴 `weapon_class` / `item_usage` 的取值**必须**在引擎枚举与原版 `item_usage_sets.xml` 里
（`TwoHandedPolearm` 等已核；织丰的 `musket` 是它自定义的 usage，**不能用**——铁则 6 零依赖，
所以铁炮走原版 `crossbow`）。

用法：
    python tools/sw2-pipeline/gen_weapon_items.py            # 写盘
    python tools/sw2-pipeline/gen_weapon_items.py --check    # 只校验（exit 1 = 过期）
"""
import argparse
import csv
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))
from parts_table import TABLE            # noqa: E402
import gen_armor_items as G              # noqa: E402  复用 upsert / 登记 / 中文名工具
from csv_dual import read_table, write_table  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

MB2 = os.environ.get("MB2_PATH") or r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
MD = os.path.join(MB2, "Modules", "Taikou", "ModuleData")
WEAPONS = os.path.join(MD, "taikou_items", "weapons.xml")
DIMS = os.path.join(REPO, "tools", "armor-pipeline", "out", "weapon_dims.csv")

# ---------- 每人用什么武器类型（形态决定）----------
WEAPON_OF = {
    # 长柄（枪 / 薙刀 / 伞柄）
    "L00_yukimura": "polearm",   # 十文字枪
    "L01_keiji": "polearm",      # 朱枪
    "L05_kenshin": "polearm",    # 枪（带旗）
    "L07_okuni": "polearm",      # 伞
    "L101_katsuie": "polearm",   # 长枪
    "L36_hideyoshi": "polearm",  # 长枪
    "L38_tadakatsu": "polearm",  # 蜻蛉切
    "L40_ieyasu": "polearm",     # 枪
    "L42_nagamasa": "polearm",   # 枪
    "L44_yoshihiro": "polearm",  # 大枪
    "L45_ginchiyo": "polearm",   # 薙刀
    "L46_kanetsugu": "polearm",  # 枪
    # 双手刀
    "L02_nobunaga": "sword2h", "L03_mitsuhide": "sword2h", "L11_masamune": "sword2h",
    "L12_nouhime": "sword2h", "L14_rammaru": "sword2h", "L100_kojiro": "sword2h",
    "L43_sakon": "sword2h", "L49_musashi": "sword2h",
    # 单手刀 / 短兵 / 军配阵扇
    "L06_oichi": "sword1h", "L10_shingen": "sword1h", "L13_hanzo": "sword1h",
    "L41_mitsunari": "sword1h", "L47_nene": "sword1h", "L48_kotaro": "sword1h",
    # 远程
    "L39_inahime": "bow",        # 弓
    "L09_magoichi": "gun",       # 铁炮
}

# ---------- 五类武器的数值（2026-09-15 用户裁定：全部顶级）----------
KINDS = {
    "polearm": dict(
        itype="TwoHandedWeapon", wclass="TwoHandedPolearm", usage="polearm_block_thrust",
        holster="polearm_back", phys="wood_weapon", body_name="bo_mace_a",
        swing=70, thrust=130, speed=85, weight=3.0, value=30000,
        cn_word="枪", en_word="Spear", flags='MeleeWeapon="true"',
        extra_head="", extra_tail=' center_of_mass="0,0,0.4"'),
    "sword2h": dict(
        itype="TwoHandedWeapon", wclass="TwoHandedSword", usage="twohanded_block_swing_thrust",
        holster="sword_left_hip", phys="metal_weapon", body_name="bo_mace_a",
        swing=140, thrust=100, speed=92, weight=2.0, value=35000,
        cn_word="太刀", en_word="Katana", flags='MeleeWeapon="true" TwoHandIdleOnMount="true"',
        extra_head="", extra_tail=' center_of_mass="0,0,0.4"'),
    "sword1h": dict(
        itype="OneHandedWeapon", wclass="OneHandedSword", usage="onehanded_block_swing_thrust",
        holster="sword_left_hip", phys="metal_weapon", body_name="bo_mace_a",
        swing=110, thrust=85, speed=100, weight=1.2, value=25000,
        cn_word="刀", en_word="Blade", flags='MeleeWeapon="true"',
        extra_head="", extra_tail=''),
    "bow": dict(
        itype="Bow", wclass="Bow", usage="bow", holster="bow_back", phys="wood_weapon",
        body_name="bo_composite_longbow_g",
        swing=0, thrust=110, speed=92, weight=0.8, value=30000,
        cn_word="弓", en_word="Bow",
        flags='RangedWeapon="true" HasString="true" StringHeldByHand="true" '
              'NotUsableWithOneHand="true" TwoHandIdleOnMount="true" AutoReload="true" '
              'UnloadWhenSheathed="true"',
        extra_head=' ammo_class="Arrow" ammo_limit="1" missile_speed="78" accuracy="94"',
        extra_tail=' center_of_mass="0.15,0,0"'),
    "gun": dict(
        itype="Crossbow", wclass="Crossbow", usage="tk_firearm", holster="crossbow_back",   # 🔴 tk_firearm = Taikou 自定义 usage（换装填动作），定义见 Taikou/ModuleData/item_usage_sets.xml
        phys="wood_weapon", body_name="bo_composite_crossbows",
        swing=0, thrust=200, speed=88, weight=4.5, value=50000,
        cn_word="铁炮", en_word="Matchlock",
        flags='RangedWeapon="true" HasString="true" UseHandAsThrowBase="true" '
              'NotUsableWithOneHand="true" TwoHandIdleOnMount="true" BonusAgainstShield="true"',
        extra_head=' ammo_class="Cartridge" ammo_limit="1" reload_phase_count="2" missile_speed="600" accuracy="40"',   # 🔴 Cartridge 不是 Bolt（火器身份，2026-09-16 实机定）；reload_phase_count=2 照原版弩（9 把全有）
        extra_tail=' center_of_mass="0,0,0.4"'),
}
SUFFIX = "_weapon_a"

# ───────── 物理体（`body_name`）：武器的**碰撞形状不是网格** ─────────
# 实测（2026-09-15）：`body_name` 指向的是引擎内置的 **PhysicsShape** 资产
# （定义在 `Native/ModuleData/CoreGameReferences/physics_shape.txt`，共 542 条、近战可用的只有十来个），
# 不是我们做的网格。`WeaponComponentData` 里**没有任何形状字段** —— 只有
# WeaponLength / WeaponBalance / TotalInertia / CenterOfMass / SweetSpotReach。
# ⇒ **网格形状只影响观感；碰撞 = PhysicsShape + weapon_length**。
# 原版拼件也是按语义挑这几个体（blade 件统计：bo_sword_one_handed×118 / bo_spear_b×50 /
# bo_mace_a×35 / bo_knife_a×22），所以照抄即可。
BODY_OF = {
    "polearm": "bo_spear_b",
    "sword2h": "bo_sword_one_handed",
    "sword1h": "bo_knife_a",
    "bow": "bo_longbow_a",
    "gun": "bo_composite_crossbows",
}
# 按角色覆盖（形态不是刀剑枪的那几件：槌/斧/扇/球/笼手）
BODY_OVERRIDE = {
    "L44_yoshihiro": "bo_mace_a",        # 丸太槌（大槌）
    "L101_katsuie": "bo_axe_longer_a",   # 二丁斧（长柄斧）
    "L10_shingen": "bo_mace_a",          # 将配（军配扇，挥击）
    "L41_mitsunari": "bo_mace_a",        # 义扇
    "L06_oichi": "bo_mace_a",            # 剑玉（球）
    "L48_kotaro": "bo_mace_a",           # 铁笼手（拳套）
}


def body_of(key):
    return BODY_OVERRIDE.get(key) or BODY_OF[WEAPON_OF[key]]
# 远程武器的弹药（写进 TaikouHero.csv 的「弹药」列，键 Ammo）。
# 🔴 **必须自给**：`Item.` 引用只能指向 Taikou 包内定义的东西 —— Taikou 依赖 SandBoxCore，
#    但按「内容包数据自给」纪律（check_taikou_xml_references.py 的门槛），原版物品**不算**可用，
#    所以下面这两个弹药要从 SandBoxCore 拷定义进 Taikou（与 leather_shoes 等拷贝件同口径）。
AMMO_OF = {"bow": "piercing_arrows", "gun": "bolt_e"}
AMMO_SRC = os.path.join(MB2, "Modules", "SandBoxCore", "ModuleData", "items", "weapons.xml")


def read_dims():
    """weapon_dims.csv → {资源名: dict(total/below/above/width)}。"""
    out = {}
    if not os.path.isfile(DIMS):
        sys.exit("FAIL: 找不到尺寸表 %s —— 先跑 build_weapons.py" % DIMS)
    for r in csv.DictReader(io.open(DIMS, encoding="utf-8")):
        out[r["name"]] = {k: float(r[k]) for k in ("total_m", "below_m", "above_m", "width_m")}
    return out


def weapon_length(K, d):
    """骑砍2 的 weapon_length（cm）：远程用全长，近战用「握持点→刀尖」；最少 40（免得太短没判定）。"""
    v = d["total_m"] if K["itype"] in ("Bow", "Crossbow") else d["above_m"]
    return int(round(max(v * 100.0, 40.0)))


def item_block(slug, cn_name, en_name, K, d, body, fb_name=None, label=None):
    """照织丰整体网格武器的写法（`sho_bokken_katana` / `sho_new_yumi_1` / `sho_tanegashima_musket`）。

    `fb_name` = 英文 fallback 全名。缺省是「<角色>的<武器>」（武将件）；
    兵种通用武器传「打刀」这类**无归属**的裸名（名表规则：兵种/护卫是通用装备，不加 [xx之武]）。
    `label` = 生成注释开头的那句（缺省「<角色> 的<类>」，兵种件传裸名免得写成「长枪 的枪」）。"""
    iid = "taikou_%s%s" % (slug, SUFFIX)
    ln = weapon_length(K, d)
    swing = (' swing_damage="%d" swing_damage_type="Cut"' % K["swing"]) if K["swing"] else ""
    if fb_name is None:
        fb_name = "%s's %s" % (en_name, K["en_word"])
    if label is None:
        label = "%s 的%s" % (cn_name, K["cn_word"])
    return (
        '\t<!-- %s（战无2 解包件重定向；网格 tools/armor-pipeline/out/%s.fbx）\n'
        '\t     普通物品：挂进 EquipmentRoster 只决定初始装备，可被扒/被偷/作战利品。 -->\n'
        '\t<Item id="%s" name="{=TAIKOU_%s%s}%s" body_name="%s" mesh="%s" '
        'culture="Culture.ikoku" weight="%s" value="%d" difficulty="0" appearance="1.0" '
        'Type="%s" item_holsters="%s">\n'
        '\t\t<ItemComponent>\n'
        '\t\t\t<Weapon weapon_class="%s"%s thrust_speed="%d" speed_rating="%d" '
        'weapon_length="%d"%s thrust_damage="%d" thrust_damage_type="Pierce" '
        'item_usage="%s" physics_material="%s"%s>\n'
        '\t\t\t\t<WeaponFlags %s />\n'
        '\t\t\t</Weapon>\n'
        '\t\t</ItemComponent>\n'
        '\t\t<Flags%s />\n'
        '\t</Item>\n'
        % (label, iid,
           iid, slug, SUFFIX, fb_name,
           body, iid, K["weight"], K["value"], K["itype"], K["holster"],
           K["wclass"], K["extra_head"], K["speed"] + 5, K["speed"],
           ln, swing, K["thrust"], K["usage"], K["phys"], K["extra_tail"],
           K["flags"],
           (' ForceAttachOffHandPrimaryItemBone="true"' if K["itype"] == "Bow" else "") + ' Civilian="true"'))


def slug_of(key):
    return TABLE[key]["asset"][len("head_"):-len("_a")]


# ───────── 官方正式名（源 = 查看器名表，见 extract_official_names.py）─────────
NAMES_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Sw2OfficialNames.csv")


def load_names():
    """→ {角色 key: dict(WeaponName/ArmorName/HelmetName/W1..W5/...)}"""
    if not os.path.isfile(NAMES_CSV):
        sys.exit("FAIL: 找不到命名表 %s —— 先跑 extract_official_names.py" % NAMES_CSV)
    cn, en, rows = read_table(NAMES_CSV, head=2)
    out = {}
    for r in rows:
        if not r:
            continue
        d = dict(zip(en, r))
        if d.get("Key"):
            out[d["Key"]] = d
    return out


NAMES = load_names()

# 🔴 **本工具自己的哨兵**：不能和 `gen_armor_items.py` 共用插入锚点 ——
#    两边都往「生成块标记之前」插的话，后跑的会把先跑的挤到上面，**每次跑都重排一次**
#    （实测：weapon 的 --check 与 armor 的 --check 交替报过期）。
#    各自插到自己的哨兵之后 = 互不干扰、位置稳定。
W_MARK = "<!-- ==== 战无2 武器名（tools/sw2-pipeline/gen_weapon_items.py 产出，禁止手改）==== -->"


def ensure_w_marker(text):
    """在生成块标记之前放本工具的哨兵（幂等）。"""
    if W_MARK in text:
        return text
    i = text.find(G.EW_MARK)
    if i < 0:
        i = text.rfind("</strings>")
    i = text.rfind("\n", 0, i) + 1
    return text[:i] + W_MARK + "\n" + text[i:]


def upsert_wstr(text, key, line):
    """删掉同 id 的旧行，再插到**自己的哨兵之后**（挤不到甲/盔那一块去）。"""
    pat = re.compile(r'[ \t]*<string id="%s"[^>]*/>\r?\n' % re.escape(key))
    new = pat.sub("", text)
    i = new.find(W_MARK)
    if i < 0:
        raise SystemExit("FATAL: 语言文件里没有武器哨兵")
    i = new.find("\n", i) + 1              # 哨兵行的下一行行首
    return new[:i] + line + new[i:]


def cn_line(key, slug, K):
    """中文名 = **战无2 官方正式名**（「十文字枪」而不是「真田幸村的枪」）。"""
    nm = (NAMES.get(key, {}).get("WeaponName") or "").strip()
    if not nm:
        sys.exit("FAIL: 命名表里 %s 没有武器名" % key)
    return '  <string id="TAIKOU_%s%s" text="%s" />\n' % (slug, SUFFIX, nm)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    dims = read_dims()
    keys = sorted(TABLE)
    miss_kind = [k for k in keys if k not in WEAPON_OF]
    if miss_kind:
        sys.exit("FAIL: 这些角色没有指定武器类型（补 WEAPON_OF）：%s" % miss_kind)
    miss_dim = [k for k in keys if "taikou_%s%s" % (slug_of(k), SUFFIX) not in dims]
    if miss_dim:
        sys.exit("FAIL: 这些角色没有网格尺寸（先跑 build_weapons.py）：%s" % miss_dim)

    K_ALL = dict(suffix=SUFFIX, col_cn="武器", col_en="Weapon",
                 cn_word="武器", cn_note="的武器", file_key="weapons",
                 name_col="WeaponName")     # 官方正式名取命表的哪一列
    items_txt = io.open(WEAPONS, encoding="utf-8-sig").read()
    cn_txt = io.open(G.CN, encoding="utf-8-sig").read()
    new_items, new_cn = items_txt, ensure_w_marker(G.ensure_cn_marker(cn_txt))

    for key in keys:
        slug, cn_name = slug_of(key), TABLE[key]["cn"]
        K = KINDS[WEAPON_OF[key]]
        en_name = G.EN.get(TABLE[key]["taikou"]) or slug.title()
        iid = "taikou_%s%s" % (slug, SUFFIX)
        new_items, _ = G.upsert(new_items, iid,
                                item_block(slug, cn_name, en_name, K, dims[iid], body_of(key)), tail="</Items>")
        new_cn = upsert_wstr(new_cn, "TAIKOU_%s%s" % (slug, SUFFIX), cn_line(key, slug, K))

    if args.check:
        # 期望值 = 28 件武器 + 自给弹药（弹药块由 apply_ammo 补，check 模式不写盘）
        want_items, _ = apply_ammo(new_items)
        ok = (want_items == items_txt and new_cn == cn_txt)
        print("[武器] %s 物品表%s · 中文名%s" % ("✅" if ok else "❌ 过期",
              "一致" if want_items == items_txt else "需重跑",
              "一致" if new_cn == cn_txt else "需重跑"))
        return 0 if ok else 1

    wrote = 0
    for path, old, new in ((WEAPONS, items_txt, new_items), (G.CN, cn_txt, new_cn)):
        if old != new:
            io.open(path, "w", encoding="utf-8-sig", newline="").write(new)
            wrote += 1
    ensure_ammo()                                 # 远程弹药的原始定义（自给）
    # 登记（复用甲/盔那套）
    G.prune_kind = lambda *a, **k: None          # 武器不共用甲的 prune（文件不同），自己清
    prune(keys)
    cn_i, en_i, rows_i, added = G.register_item_csv(keys, K_ALL)
    write_table(G.ITEM_CSV, cn_i, en_i, rows_i)
    cn_h, en_h, rows_h, n_filled = G.hero_armor_col(keys, K_ALL)
    write_table(G.HERO_CSV, cn_h, en_h, rows_h)
    # 「弹药」列：只有弓/铁炮有值（近战留空）
    n_ammo = fill_col(keys, "弹药", "Ammo",
                      lambda k: AMMO_OF.get(WEAPON_OF[k], ""))

    txt = io.open(WEAPONS, encoding="utf-8-sig").read()
    cnt = io.open(G.CN, encoding="utf-8-sig").read()
    miss = [k for k in keys if 'id="taikou_%s%s"' % (slug_of(k), SUFFIX) not in txt]
    miss_cn = [k for k in keys if 'TAIKOU_%s%s"' % (slug_of(k), SUFFIX) not in cnt]
    ids = {r[0] for r in read_table(G.ITEM_CSV, head=2)[2] if r}
    miss_reg = [k for k in keys if "taikou_%s%s" % (slug_of(k), SUFFIX) not in ids]
    print("[武器] 写 %d 文件 · %d 件 · item.csv 新增 %d · TaikouHero「武器」填 %d 行 · 「弹药」填 %d 行 · 自检缺 %d/%d/%d"
          % (wrote, len(keys), added, n_filled, n_ammo, len(miss), len(miss_cn), len(miss_reg)))
    for name, lst in (("物品", miss), ("中文", miss_cn), ("item.csv", miss_reg)):
        if lst:
            print("   ❌ %s 缺：%s" % (name, lst))
    return 1 if (miss or miss_cn or miss_reg) else 0


def fill_col(keys, col_cn, col_en, value_of):
    """给 `TaikouHero.csv` 加一列并填值（列不存在就补列；其余格一字不动）。→ 填了几行"""
    cn, en, rows = read_table(G.HERO_CSV, head=2)
    if col_en not in en:
        cn, en = list(cn) + [col_cn], list(en) + [col_en]
        rows = [list(r) + [""] for r in rows]
    j = en.index(col_en)
    want = {TABLE[k]["taikou"]: value_of(k) for k in keys}
    n = 0
    for r in rows:
        if r and r[0] in want and r[j] != want[r[0]]:
            r[j] = want[r[0]]
            n += 1
    write_table(G.HERO_CSV, cn, en, rows)
    return n


def apply_ammo(txt):
    """把远程武器要的弹药定义补进 weapons.xml 文本（纯函数，check 模式也用它算期望值）。
    culture 一律改 `Culture.ikoku`（照 steppe_arrows 惯例）。→ (新文本, 加了几个)"""
    need = sorted(set(AMMO_OF.values()))
    todo = [a for a in need if 'id="%s"' % a not in txt]
    if not todo:
        return txt, 0
    if not os.path.isfile(AMMO_SRC):
        sys.exit("FAIL: 找不到原版武器表 %s（拷弹药要用）" % AMMO_SRC)
    src = io.open(AMMO_SRC, encoding="utf-8-sig", errors="replace").read()
    for aid in todo:
        m = re.search(r'<Item\s+id="%s"[\s\S]{0,1800}?</Item>' % re.escape(aid), src)
        if not m:
            sys.exit("FAIL: 原版里没有弹药 %s（换个 id 或改 AMMO_OF）" % aid)
        blk = m.group(0)
        blk = re.sub(r'culture="[^"]*"', 'culture="Culture.ikoku"', blk)
        if 'culture=' not in blk:
            blk = blk.replace('<Item\n\t\tid="%s"' % aid,
                              '<Item\n\t\tid="%s"\n\t\tculture="Culture.ikoku"' % aid, 1)
        blk = ("\t<!-- %s：原版弹药定义拷入（远程武器要自给；本块由 gen_weapon_items.py 产出） -->\n"
               % aid) + blk.lstrip()
        i = txt.rfind("</Items>")
        txt = txt[:i] + blk.rstrip() + "\n" + txt[i:]
    return txt, len(todo)


def ensure_ammo():
    txt = io.open(WEAPONS, encoding="utf-8-sig").read()
    new, n = apply_ammo(txt)
    if n:
        io.open(WEAPONS, "w", encoding="utf-8-sig", newline="").write(new)
        print("[武器] 拷入弹药定义 %d 个：%s" % (n, sorted(set(AMMO_OF.values()))))
    return n


def prune(keys):
    """把**不在名单里**的旧武器条目清掉（类型表改了就跟着缩）。"""
    want = set("taikou_%s%s" % (slug_of(k), SUFFIX) for k in keys)
    txt = io.open(WEAPONS, encoding="utf-8-sig").read()
    n = 0
    for iid in sorted(set(re.findall(r'<Item id="(taikou_[a-z_]+%s)"' % re.escape(SUFFIX), txt))):
        if iid in want:
            continue
        a = txt.find('<Item id="%s' % iid)
        b = txt.find("</Item>", a) + 7
        while b < len(txt) and txt[b] in ("\r", "\n"):
            b += 1
        c = txt.rfind("<!--", 0, a)
        if c >= 0:
            a = txt.rfind("\n", 0, c) + 1
        txt = txt[:a] + txt[b:]
        n += 1
    if n:
        io.open(WEAPONS, "w", encoding="utf-8-sig", newline="").write(txt)
        print("[武器] 清旧条目：物品 %d" % n)


if __name__ == "__main__":
    sys.exit(main())

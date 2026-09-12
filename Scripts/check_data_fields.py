#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Data-field checker (自定义世界「必填字段 / 取值合法」离线体检)
========================================================================
覆盖「必备清单」里这些**只有文字、没有脚本**的条目：
  §1.3 据点：每个 Settlement 必填字段 / 城防 level ≤ 3 / CommonAreas（雷 19）
  §1.4 occupation 必须过引擎枚举全集（错 = NPCCharacters 段**静默截断**，雷 16）
  §1.1 文化（被使用的文化）：名字池三池非空 / N&W 池产得出 merchant·artisan / 各 party_template
  §1.4 商队护卫引擎硬查询：`Occupation=CaravanGuard ∧ 步兵 ∧ Level==26 ∧ 文化=该文化`（First 找不到即抛）
  §1.2 王国/家族：Kingdom.owner 链可解析

🔴 规则边界全部**拿官方数据验过**（SandBox 493 据点 / 16 文化），不是拍脑袋：
  · owner 只有「城镇」有（村庄/城堡/巢穴/服务性据点都没有）→ 只对 Town 强制
  · CommonAreas 只有「城镇」有（城堡没有）→ 只对 Town 强制
  · Locations 只有城镇/城堡/村庄有（巢穴没有）
  · 城镇 level 官方实测分布 {1,2,3}，上界 = 3（雷 29）
  · occupation 官方有 52 个模板**不写**该属性 → 只在「写了」的时候校验合法性
  · 文化规则只作用于「**被使用的非流寇文化**」（有据点的；官方 nord/vakken/darshi 无据点也无名字池
    → 不该报；流寇文化占巢穴 → 排除）

数据来源 = 目标 GameType 下**实际会被加载的段**（DependedModules 闭包 + GameType 白名单），
与引擎所见一致（定义在未加载段里 = 不存在）。

Usage:
  python Scripts/check_data_fields.py [--module PATH] [--official-root PATH] [--game-type NAME]
Exit: 0 无问题 / 1 有问题 / 2 fatal。
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

# 引擎枚举 TaleWorlds.CampaignSystem.Occupation（1.2.12 反编译全文；末位 NumberOfOccupations 是哨兵，不算合法值）
OCCUPATIONS = {
    "NotAssigned", "Tavernkeeper", "Mercenary", "Lord", "GoodsTrader", "ArenaMaster", "Villager",
    "Soldier", "Townsfolk", "RansomBroker", "Weaponsmith", "Armorer", "HorseTrader", "TavernWench",
    "TavernGameHost", "Bandit", "Wanderer", "Artisan", "Merchant", "Preacher", "Headman",
    "GangLeader", "RuralNotable", "PrisonGuard", "Guard", "ShopWorker", "Musician", "Gangster",
    "Blacksmith", "BannerBearer", "CaravanGuard", "Special",
}

# 据点必填字段（按组件类型分档——官方实测边界，见文件头）
REQ_FIELDS = {
    "Town": ["name", "culture", "posX", "posY", "owner"],
    "Castle": ["name", "culture", "posX", "posY"],
    "Village": ["name", "culture", "posX", "posY"],
    "Hideout": ["name", "culture", "posX", "posY"],
    "Service": ["name", "culture", "posX", "posY"],
}
NEEDS_LOCATIONS = {"Town", "Castle", "Village"}
NEEDS_COMMON_AREAS = {"Town"}
MAX_TOWN_LEVEL = 3

# 被使用的非流寇文化：必备的模板类属性（非空）
REQ_CULTURE_TEMPLATE_ATTRS = [
    ("default_party_template", "CalculateAverageWage NRE", "雷 6"),
    ("militia_party_template", "SpawnMilitiaParty NRE", "雷 14"),
    ("basic_troop", "基础兵种", "1.1"),
    ("elite_basic_troop", "精英基础兵种", "1.1"),
]
# N&W 池必须能产出的职业（工坊名流，ChooseWeighted(空) NRE）
REQ_NW_OCCUPATIONS = [("Merchant", "工坊名流 CreateHeroAtOccupation", "雷 15"),
                      ("Artisan", "同上", "雷 15")]


def registry_mb2_path():
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                val, _ = winreg.QueryValueEx(k, "MB2_PATH")
                if val:
                    return val
        except OSError:
            continue
    return None


def module_closure(mod_root, start):
    seen, order = set(), []

    def visit(mod_id):
        if mod_id in seen:
            return
        seen.add(mod_id)
        sm = mod_root / mod_id / "SubModule.xml"
        if sm.is_file():
            try:
                root = ET.parse(str(sm)).getroot()
            except Exception:
                order.append(mod_id)
                return
            dep = root.find("DependedModules")
            if dep is not None:
                for d in dep.iter("DependedModule"):
                    if d.get("Id"):
                        visit(d.get("Id"))
        order.append(mod_id)

    visit(start)
    return order


def loaded_sections(mod_root, mod_id, game_type):
    out = []
    sm = mod_root / mod_id / "SubModule.xml"
    if not sm.is_file():
        return out
    try:
        root = ET.parse(str(sm)).getroot()
    except Exception:
        return out
    for node in root.iter("XmlNode"):
        name = node.find("XmlName")
        if name is None or not name.get("path"):
            continue
        gt = node.find("IncludedGameTypes")
        if gt is None:
            out.append((name.get("id"), name.get("path")))
        elif any(g.get("value") == game_type for g in gt.iter("GameType")):
            out.append((name.get("id"), name.get("path")))
    return out


def section_files(mod_root, mod_id, path):
    data = mod_root / mod_id / "ModuleData"
    for cand in (data / (path + ".xml"), data / path):
        if cand.is_file():
            return [cand]
        if cand.is_dir():
            return sorted(cand.rglob("*.xml"))
    return []


def is_float(x):
    try:
        float(x)
        return True
    except (TypeError, ValueError):
        return False


def settlement_kind(s):
    comps = s.find("Components")
    kinds = [c.tag for c in comps] if comps is not None else []
    town = comps.find("Town") if comps is not None else None
    if town is not None:
        return ("Castle" if town.get("is_castle") == "true" else "Town"), town
    for k in ("Village", "Hideout"):
        if k in kinds:
            return k, None
    return "Service", None



def inferred_game_types(mod_path, explicit=None):
    """本模块要体检的 GameType 列表。
    显式 --game-type → 只跑那一个（单跑语义不变）。
    缺省 = SubModule.xml 里所有「<模块名> + 数字年份」的 GameType（时代切换后一模块多 GameType：
    Taikou → TaikouCampaign1560 / TaikouCampaign1582 …），**每个都要跑**——
    只跑一个 = 另一个时代的段查不到 = 静默假绿（时代切换 spike 的核心风险点）。
    """
    if explicit:
        return [explicit]
    out = []
    sm = mod_path / "SubModule.xml"
    if sm.is_file():
        try:
            root = ET.parse(str(sm)).getroot()
        except Exception:
            root = None
        if root is not None:
            pat = re.compile(re.escape(mod_path.name) + r"Campaign\d{4}$")
            for g in root.iter("GameType"):
                v = g.get("value")
                if v and pat.fullmatch(v) and v not in out:
                    out.append(v)
    return out or [mod_path.name + "Campaign"]


def main():
    ap = argparse.ArgumentParser(description="Content-pack data-field checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None)
    ap.add_argument("--game-type", default=None)
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    mod_root = mod_path.parent
    root = Path(args.official_root) if args.official_root else None
    if root is None:
        mb2 = registry_mb2_path()
        root = Path(mb2) if mb2 else None
    if root is not None and (root / "Modules").is_dir() and root != mod_root:
        mod_root = root / "Modules"
    if not mod_root.is_dir():
        print(f"[FATAL] Modules root not found: {mod_root}")
        return 2
    exit_codes = []
    for game_type in inferred_game_types(mod_path, args.game_type):
        print(f"Module   : {mod_path}")
        print(f"GameType : {game_type}")

        closure = module_closure(mod_root, mod_path.name)
        files, sec_count = [], 0
        for mod_id in closure:
            for _sid, path in loaded_sections(mod_root, mod_id, game_type):
                sec_count += 1
                files.extend(section_files(mod_root, mod_id, path))
        print(f"扫描     : {len(closure)} 模块 / {sec_count} 段 / {len(files)} 文件\n")

        settlements, cultures, chars = [], {}, {}
        kingdoms, clans, heroes = {}, {}, {}
        for f in files:
            try:
                r = ET.parse(str(f)).getroot()
            except Exception as e:
                print(f"  [FILE-ERROR] {f.name}: {e}")
                continue
            src = f"{f.parent.name}/{f.name}"
            for el in r.iter("Settlement"):
                settlements.append((el, src))
            for el in r.iter("Culture"):
                if el.get("id"):
                    cultures[el.get("id")] = (el, src)
            for el in r.iter("NPCCharacter"):
                if el.get("id"):
                    chars[el.get("id")] = (el, src)
            for tag, bucket in (("Kingdom", kingdoms), ("MBKingdom", kingdoms),
                                ("Faction", clans), ("MBFaction", clans),
                                ("Hero", heroes), ("MBHero", heroes)):
                for el in r.iter(tag):
                    if el.get("id"):
                        bucket.setdefault(el.get("id"), (el, src))

        errors, warns = [], []

        # ── 0. 特殊文化存在性（引擎 fallback 消费点；官方 SandBoxCore 里同样存在） ──
        print("== 特殊文化存在性 ==")
        if "neutral_culture" not in cultures:
            errors.append("缺 neutral_culture")
            print("  [ERROR] 缺 neutral_culture —— 引擎 4 处 fallback 消费它（1.1）")
        else:
            print("  [ OK ] neutral_culture")

        # ── 0b. 文化部队模板属性完整性（🔴 雷 110：2026-09-12 实机建世界崩溃根因） ──
        #   引擎 CultureObject 反编译实证（1.2.12）：8 个 *_party_template 属性全部**无 null 兜底**；
        #   而 Clan.DefaultPartyTemplate 取值 = `_defaultPartyTemplate ?? Culture.DefaultPartyTemplate`
        #   → 文化缺 default_party_template = 该文化的领主部队刷兵时 pt=null = FillPartyStacks NRE。
        #   实测：neutral_culture 独缺 default_party_template，宇佐美家（clan_usami_1 文化 = 它）建世界即崩。
        print("== 文化部队模板属性（引擎无 null 兜底，缺一即崩） ==")
        PT_REQUIRED = ("default_party_template", "militia_party_template", "villager_party_template",
                       "caravan_party_template", "elite_caravan_party_template",
                       "rebels_party_template", "vassal_reward_party_template")
        bad_pt = 0
        for cid, (cel, csrc) in sorted(cultures.items()):
            miss = [a for a in PT_REQUIRED if not cel.get(a)]
            if miss:
                bad_pt += 1
                errors.append(f"文化 {cid} 缺部队模板属性 {','.join(miss)}")
                print(f"  [ERROR] 文化 {cid} 缺 {','.join(miss)} —— 刷兵即崩（雷 110） ← {csrc}")
        if not bad_pt:
            print(f"  [ OK ] {len(cultures)} 个文化 × {len(PT_REQUIRED)} 个模板属性齐备")
        pt_pending = [cid for cid, (cel, _s) in cultures.items() if not cel.get("bandit_boss_party_template")]
        if pt_pending:
            warns.append(f"{len(pt_pending)} 个文化缺 bandit_boss_party_template")
            print(f"  [WARN] {len(pt_pending)}/{len(cultures)} 个文化缺 bandit_boss_party_template"
                  f"（匪首部队模板，同样无 null 兜底；需先有匪兵兵种模板 → 属内容决策，待补）")

        # ── 1. 据点字段 ──
        print("== 据点必填字段 / 取值（官方 493 据点边界验证） ==")
        by_kind = {}
        for s, src in settlements:
            sid = s.get("id") or "<无 id>"
            kind, town = settlement_kind(s)
            by_kind[kind] = by_kind.get(kind, 0) + 1
            for fld in REQ_FIELDS[kind]:
                if not s.get(fld):
                    errors.append(f"据点 {sid}（{kind}）缺 {fld}")
                    print(f"  [ERROR] {sid}（{kind}）缺字段 {fld}  ← {src}")
            for fld in ("posX", "posY"):
                v = s.get(fld)
                if v and not is_float(v):
                    errors.append(f"据点 {sid} {fld} 非数值")
                    print(f"  [ERROR] {sid} {fld}={v!r} 不是数值")
            if kind in NEEDS_LOCATIONS and s.find("Locations") is None:
                errors.append(f"据点 {sid}（{kind}）缺 Locations")
                print(f"  [ERROR] {sid}（{kind}）缺 Locations")
            if kind in NEEDS_COMMON_AREAS:
                ca = s.find("CommonAreas")
                if ca is None or len(ca) == 0:
                    errors.append(f"据点 {sid}（城镇）CommonAreas 空")
                    print(f"  [ERROR] {sid}（城镇）CommonAreas 空 —— AlleyCampaignBehavior `i % Count` 除零（雷 19）")
            if town is not None:
                lv = town.get("level")
                if lv is None or not lv.isdigit() or int(lv) > MAX_TOWN_LEVEL:
                    errors.append(f"据点 {sid} 城防 level={lv} 超上界 {MAX_TOWN_LEVEL}")
                    print(f"  [ERROR] {sid} 城防 level={lv} —— 官方上界 {MAX_TOWN_LEVEL}"
                          f"（超 = PartyVisual.RefreshPartyIcon KeyNotFound，雷 29）")
        print(f"  （据点分类统计：{by_kind}）")

        # ── 2. occupation 枚举合法 ──
        print("\n== occupation 取值合法性（错 = NPCCharacters 段静默截断，雷 16） ==")
        bad_occ = 0
        for cid, (el, src) in chars.items():
            occ = el.get("occupation")
            if occ and occ not in OCCUPATIONS:
                bad_occ += 1
                errors.append(f"{cid} occupation={occ} 非法")
                print(f"  [ERROR] {cid} occupation={occ!r} 不在引擎枚举内 ← {src}")
        if not bad_occ:
            print(f"  （无 ✓ 共检查 {len(chars)} 个角色模板）")

        # ── 3. 被使用的非流寇文化 ──
        # 🔴 口径 = 「**拥有据点**的文化」（官方数据验证：nord/vakken/darshi 是雇佣兵团文化，
        #   被 Faction 引用但**不拥有据点**，照样没有名字池/队伍模板 —— 拿它们当"被使用"会误报 12 条）。
        #   流寇文化拥有巢穴但同样不参与城镇/名流/商队链 → 按 is_bandit 排除。
        used = set()
        for s, _src in settlements:
            if s.get("culture"):
                used.add(s.get("culture").split(".")[-1])

        bandit = {cid for cid, (el, _s) in cultures.items() if (el.get("is_bandit") or "").lower() == "true"}
        check_cultures = sorted(used - bandit)

        print(f"\n== 拥有据点的非流寇文化（{len(check_cultures)} 个：{', '.join(check_cultures) or '无'}） ==")
        if not used:
            warns.append("没有任何据点引用文化——数据可能不完整")
            print("  [WARN] 没有任何据点引用文化")
        for cid in check_cultures:
            cel, csrc = cultures.get(cid, (None, "?"))
            if cel is None:
                errors.append(f"文化 {cid} 被引用但未定义")
                print(f"  [ERROR] 文化 {cid} 被据点/势力引用，但没有定义")
                continue
            problems = []
            # 名字池三池
            for pool in ("clan_names", "male_names", "female_names"):
                el = cel.find(pool)
                if el is None or len(el) == 0:
                    problems.append((f"{pool} 空", "GenerateClanName / 命名 NRE", "雷 22"))
            # 模板类属性
            for attr, why, lei in REQ_CULTURE_TEMPLATE_ATTRS:
                if not cel.get(attr):
                    problems.append((f"{attr} 空", why, lei))
            # N&W 必须产得出 merchant/artisan
            nw = cel.find("notable_and_wanderer_templates")
            nw_ids = [t.get("name") or t.get("id") for t in nw] if nw is not None else []
            if not nw_ids:
                problems.append(("notable_and_wanderer_templates 空", "名流生不出", "雷 10/15"))
            else:
                nw_occs = set()
                for tid in nw_ids:
                    if tid:
                        key = tid.split(".")[-1]
                        if key in chars:
                            nw_occs.add(chars[key][0].get("occupation"))
                for occ, why, lei in REQ_NW_OCCUPATIONS:
                    if occ not in nw_occs:
                        problems.append((f"N&W 池缺 {occ} 职业模板", why, lei))
            # 基础雇佣兵（FindTotalMercenaryProbability 消费；官方 6 个有据点的文化均非空）
            bm = cel.find("basic_mercenary_troops")
            if bm is None or len(bm) == 0:
                problems.append(("basic_mercenary_troops 空", "FindTotalMercenaryProbability NRE", "雷 21"))
            # 商队护卫引擎硬查询（Level==26 ∧ 步兵 ∧ CaravanGuard ∧ 本文化）
            hit = [c for c, (el, _s) in chars.items()
                   if el.get("occupation") == "CaravanGuard"
                   and (el.get("default_group") or "") == "Infantry"
                   and (el.get("level") or "") == "26"
                   and (el.get("culture") or "").split(".")[-1] == cid]
            if not hit:
                problems.append(("无 (CaravanGuard ∧ 步兵 ∧ level=26 ∧ 本文化) 的模板",
                                 "InitializeCaravanOnCreation 的 First 抛异常", "雷 16"))
            if problems:
                for what, why, lei in problems:
                    errors.append(f"文化 {cid}: {what}")
                    print(f"  [ERROR] {cid} :: {what} —— {why}（{lei}）")
            else:
                print(f"  [ OK ] {cid}")

        # ── 4. 势力：王国必填 + 家族（拥有据点的）必填 + owner 链可解析 ──
        # 🔴 边界经官方验证：Kingdom 8/8 全字段齐 → 无条件要求；
        #   Faction 94 个里 21 个缺 is_noble/super_faction、20 个缺 owner（多为流寇/雇佣兵家族）
        #   → **只对「拥有据点的家族」强制**（settlements.xml 的 owner 出现过的）
        print("\n== 势力必填 + owner 链（Kingdom.OnNewGameCreated / RulingClan 链，雷 13） ==")
        owner_clans = set()
        for s, _src in settlements:
            o = s.get("owner")
            if o:
                owner_clans.add(o.split(".")[-1])

        for kid, (kel, ksrc) in sorted(kingdoms.items()):
            # banner_key 2026-09-12 补：Kingdom 节点缺它 = 旗帜渲染链取不到 key（官方 8/8 全带 →
            # 无条件要求）。同理 owner/culture/name 是引擎建王国时就读的字段。
            for fld in ("owner", "culture", "name", "banner_key"):
                if not kel.get(fld):
                    errors.append(f"Kingdom {kid} 缺 {fld}")
                    print(f"  [ERROR] Kingdom {kid} 缺 {fld}  ← {ksrc}")
            own = (kel.get("owner") or "").split(".")[-1]
            if own and own not in heroes:
                errors.append(f"Kingdom {kid} 的 owner 英雄 {own} 不存在")
                print(f"  [ERROR] Kingdom {kid} owner=Hero.{own} —— 该 Hero 未定义（雷 13 owner→Hero→Clan 链断）")
            elif own:
                hclan = (heroes[own][0].get("faction") or "").split(".")[-1]
                if hclan and hclan not in clans:
                    errors.append(f"Kingdom {kid} owner 英雄的 Clan {hclan} 不存在")
                    print(f"  [ERROR] Kingdom {kid} owner 英雄 {own} 的 faction=Clan.{hclan} 未定义（RulingClan 链断）")

        for cid in sorted(owner_clans):
            if cid not in clans:
                errors.append(f"据点 owner 家族 {cid} 未定义")
                print(f"  [ERROR] 据点引用的 owner 家族 {cid} 未定义")
                continue
            cel, csrc = clans[cid]
            for fld in ("is_noble", "owner", "super_faction"):
                if not cel.get(fld):
                    errors.append(f"Clan {cid} 缺 {fld}")
                    print(f"  [ERROR] Clan {cid}（拥有据点）缺 {fld}  ← {csrc}")
            own = (cel.get("owner") or "").split(".")[-1]
            if own and own not in heroes:
                errors.append(f"Clan {cid} 的 owner 英雄 {own} 不存在")
                print(f"  [ERROR] Clan {cid} owner=Hero.{own} —— 该 Hero 未定义")
            sf = (cel.get("super_faction") or "").split(".")[-1]
            if sf and sf not in kingdoms:
                errors.append(f"Clan {cid} 的 super_faction {sf} 未定义")
                print(f"  [ERROR] Clan {cid} super_faction=Kingdom.{sf} 未定义")
        if kingdoms or owner_clans:
            print(f"  （检查 {len(kingdoms)} 个王国 / {len(owner_clans)} 个拥有据点的家族）")

        # ── 5. Hero 段必填（2026-09-12 补）──
        # Hero 节点本身**不带 culture/occupation**——那两样由同名 NPCCharacter 模板提供
        # （实测 lord_tk5_195：Hero 段无、spnpccharacters 段有 culture=Culture.ikoku/occupation=Lord）。
        # 所以这里只查**引擎建英雄时就要读的 faction**（缺 = 英雄无家族 → RulingClan/领主链断）。
        print("\n== Hero 段必填（faction = 建英雄时就读的字段） ==")
        bad_faction = [hid for hid, (hel, _s) in heroes.items() if not hel.get("faction")]
        for hid in sorted(bad_faction):
            errors.append(f"Hero {hid} 缺 faction")
            print(f"  [ERROR] Hero {hid} 缺 faction（faction 缺失 = 英雄无家族，RulingClan/领主链断）")
        if not bad_faction:
            print(f"  [ OK ] {len(heroes)} 个 Hero 全带 faction")

        # ── 6. 玩家族形态对照项（雷 36；**只提示不阻断**，属设计待定）──
        # 清单 1.2：「玩家族参考织丰 is_minor_faction="true"（独立势力形态）」；
        # 本包现值 false（与官方 SandBox 玩家族同款）。两者都能跑，差别在「玩家族算不算独立势力」
        # → 属设计选择，不是数据缺陷，所以只报出当前值让人核，别做成硬红。
        player_clans = [cid for cid in clans if cid.startswith("player")]
        for cid in sorted(player_clans):
            v = (clans[cid][0].get("is_minor_faction") or "").strip().lower()
            if v != "true":
                warns.append(f"玩家族 {cid} is_minor_faction={v or '未写'}（清单 1.2 对照 织丰 = true）")
                print(f"  [WARN] 玩家族 {cid} is_minor_faction={v or '未写'} —— "
                      f"清单 1.2 的对照项是 true（独立势力形态）；属设计待定，见雷 36")

        print(f"\nSummary: settlements={len(settlements)} cultures_checked={len(check_cultures)} "
              f"characters={len(chars)} errors={len(errors)} warnings={len(warns)}")
        exit_codes.append(1) if errors else 0    # <- 必须在循环内：循环外只剩最后一趟的 errors（假绿，2026-09-12 修）



    return max(exit_codes) if exit_codes else 0

if __name__ == "__main__":
    sys.exit(main())

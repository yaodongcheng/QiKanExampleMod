#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""太阁六代世界段生成器（英雄 / 领主模板 / 家族 / 王国）
============================================================================
**一个产物一个产出方**（雷 60）：本脚本是下列 4 个段族 × 6 代的**唯一产出方**：
    taikou_heroes_<年>.xml   ← `<Hero id faction text>`（该代已登场的英雄）
    taikou_lords_<年>.xml    ← 同名 `<NPCCharacter>` 领主模板（年龄=该年−生年；装备按身份分档）
    spclans_<年>.xml         ← 该代存在的家族（`super_faction` = 所属王国，或独立家族）
    spkingdoms_<年>.xml      ← 该代立国的势力（武家 + 忍者 + 海贼）
  1560 那份沿用**无后缀**文件名（`taikou_heroes.xml` / `spclans.xml` / `spkingdoms.xml`）——
  现有 SubModule 注册与旧存档都认它。
⚠️ **据点段不归本脚本**（settlements*.xml 的唯一产出方 = `Scripts/gen_taikou_settlements_xml.py`）。
⚠️ `gen_taikou_era_diff.py`（旧的"时代差异 spike"）产的三件套 `_1582` 已被本脚本接管 → 该脚本退役。

口径（每条都有出处，改口径改这里）
------------------------------------
  · **谁进该代**：`Appear_<年> == 已登场`（`未登场`/`已死亡`/空 都排除——
    `已死亡` ⟺ `Identity='无效'`，实测六代零例外）。
  · **非人物行不参与**：`TemplateNPC` 非空（74 行样板/代词行）一律跳过（口径同边台账）。
  · **立国**（2026-09-12 用户裁定）：**武家 + 忍者 + 海贼立国；商家不立国**
    （商家做独立家族 `is_minor_faction="true"`、无 `super_faction`——范本 = 现有最小集「纳屋」）。
    某家某年 `Owner_<年> == "-"` = 该年不建国 → **该代的段里根本不出现它**。
  · **年龄** = `该代年份 − BirthYear`（钳 16~70；`HeroProfileRegistry` 同款口径）。
    模板按代切段就是为了这个：一个英雄在 1560 和 1598 该差 38 岁。
  · **occupation 一律 `Lord`**：非 Lord 会不会被踢出 `Clan.Lords` 无法离线证实（崩游戏风险），
    织丰 753 个领主也全写 `Lord`；**身份只影响装备分档**。
  · **不写 skills/五维**：官方/织丰/现有领主的英雄条目全都不写（`skill_template` 只给非英雄兵种用）；
    本包 113 个 SkillSet 全是官方原样拷贝，表达不了太阁 16 技能 → 留 T7 自建 SkillSet。
  · **无家的英雄不写 faction**（143 人：浪人/师范/医师/锻冶匠/僧侣/茶人）——
    既有口径「浪人/无所属无家，骑砍侧当游荡者」（见 `gen_taikou_wanderer_culture.py`）。
  · **装备只能用本包 43 件物品**（`taikou_items/`）——官方物品在本 GameType 下被过滤，引了 = null。
  · **旗号从官方 SandBox 家族池借**（94 个可用键，按序轮转）——不瞎编 banner_key（引擎解析格式，编错风险高）。

Usage:
  python Scripts/gen_taikou_era_world.py --dry-run          # 只算不写：逐代条目数 + 链完整性 + 异常
  python Scripts/gen_taikou_era_world.py                    # 写盘（含 CN 名字块同步）
  python Scripts/gen_taikou_era_world.py --check            # 只校验磁盘产物是否最新（不一致 exit 1）
  python Scripts/gen_taikou_era_world.py --era 1560         # 只做一代（调试用）
Exit: 0 成功 / 1 --check 不一致或有硬错误 / 2 fatal。
"""
import argparse
import collections
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
BASELINE_ERA = "1560"                      # 无后缀文件 = 这一代
KINGDOM_TYPES = ("Warrior", "Ninja", "Pirate")   # 立国的势力类型（用户裁定）
CULTURE_FALLBACK = "ikoku"                 # CSV 没给文化时的兜底（现有最小集同款）

# ── 装备分档（只能用 taikou_items/ 里这 43 件；槽位名 = 引擎枚举）──
# 🔴 不配马：马要配 default_group=Cavalry，而 Cavalry 无马会不会炸没验证过 → 一律 Infantry（零风险）。
ARMOR_HEAVY = dict(Head="nasal_helmet_with_mail", Body="desert_lamellar",
                   Gloves="reinforced_mail_mitten", Leg="leather_cavalier_boots")
ARMOR_LIGHT = dict(Body="battania_civil_b", Leg="leather_shoes")
ARMOR_ROBE = dict(Body="short_padded_robe", Leg="leather_shoes")

EQUIP_BY_IDENTITY = {
    # 身份 → (武器列表, 甲, 民用甲)
    "大名": (["ridged_sabre_sword_t4", "leather_round_shield"], ARMOR_HEAVY, ARMOR_LIGHT),
    "国主": (["ridged_sabre_sword_t4", "leather_round_shield"], ARMOR_HEAVY, ARMOR_LIGHT),
    "城主": (["ridged_sabre_sword_t4", "leather_round_shield"], ARMOR_HEAVY, ARMOR_LIGHT),
    "当家": (["ridged_sabre_sword_t4"], ARMOR_LIGHT, ARMOR_ROBE),
    "家老": (["short_sword_t3", "leather_round_shield"], ARMOR_HEAVY, ARMOR_LIGHT),
    "部将": (["short_sword_t3", "leather_round_shield"], ARMOR_HEAVY, ARMOR_LIGHT),
    "侍大将": (["short_sword_t3"], ARMOR_HEAVY, ARMOR_LIGHT),
    "足轻大将": (["short_sword_t3"], ARMOR_LIGHT, ARMOR_ROBE),
    "足轻组头": (["cleaver_sword_t3"], ARMOR_LIGHT, ARMOR_ROBE),
    "上忍": (["pugio"], ARMOR_ROBE, ARMOR_ROBE),
    "中忍": (["pugio"], ARMOR_ROBE, ARMOR_ROBE),
    "下忍": (["leafblade_throwing_knife"], ARMOR_ROBE, ARMOR_ROBE),
    "头目": (["pugio"], ARMOR_ROBE, ARMOR_ROBE),
    "头领": (["cleaver_sword_t3"], ARMOR_LIGHT, ARMOR_ROBE),
    "船大将": (["cleaver_sword_t3"], ARMOR_LIGHT, ARMOR_ROBE),
    "船头": (["cleaver_sword_t3"], ARMOR_LIGHT, ARMOR_ROBE),
    "水夫头": (["falchion_sword_t2"], ARMOR_LIGHT, ARMOR_ROBE),
    "水夫": (["falchion_sword_t2"], ARMOR_ROBE, ARMOR_ROBE),
    "掌柜": (["pugio"], ARMOR_ROBE, ARMOR_ROBE),
    "伙计": (["pugio"], ARMOR_ROBE, ARMOR_ROBE),
    "浪人": (["battania_mace_1_t2"], ARMOR_LIGHT, ARMOR_ROBE),
    "师范": (["wooden_sword_t1"], ARMOR_ROBE, ARMOR_ROBE),
    "师范代": (["wooden_sword_t1"], ARMOR_ROBE, ARMOR_ROBE),
    "见习": (["wooden_sword_t1"], ARMOR_ROBE, ARMOR_ROBE),
    "医师": (["pugio"], ARMOR_ROBE, ARMOR_ROBE),
    "锻冶匠": (["battania_mace_1_t2"], ARMOR_ROBE, ARMOR_ROBE),
    "僧侣": ([], ARMOR_ROBE, ARMOR_ROBE),
    "茶人": ([], ARMOR_ROBE, ARMOR_ROBE),
}
EQUIP_DEFAULT = (["short_sword_t3"], ARMOR_LIGHT, ARMOR_ROBE)

VOICE_BY_IDENTITY_FEMALE = "calm"
VOICE_DEFAULT = "curt"


def load(csv_dir, name, head=2):
    sys.path.insert(0, os.path.join(REPO, "Scripts"))
    from csv_dual import read_table
    cn, en, rows = read_table(os.path.join(csv_dir, name), head=head)
    return [{k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(en)}
            for r in rows if any((x or "").strip() for x in r)]


def era_suffix(era):
    return "" if era == BASELINE_ERA else "_" + era


def name_key(hero_id, era):
    """英雄名字键 = `TAIKOU_hero_<去前缀id>_<年代>`。

    🔴 **必须按年代分开**：名字随元服改名变（实测 49 人六代里改过名——
    木下藤吉郎 → 羽柴秀吉 → 丰臣秀吉），一个键装不下六个名字。
    `main_hero` 例外：玩家名跨代共用，沿用语言文件里已有的手维护键 `TAIKOU_main_hero`。
    """
    if hero_id == "main_hero":
        return "TAIKOU_main_hero"
    return "TAIKOU_hero_%s_%s" % (re.sub(r"[^A-Za-z0-9_]", "_",
                                        hero_id.replace("lord_tk5_", "")), era)


def cn_name_of(r, era):
    """该代的**中文名**：`Name_<年>`（当年代名，如 羽柴秀吉）→ 回落 `CNName`（通用名）。"""
    return (r.get("Name_" + era) or "").strip() or (r.get("CNName") or "").strip()


def clan_key(clan_id):
    return "TAIKOU_clan_" + re.sub(r"[^A-Za-z0-9_]", "_", clan_id.replace("clan_", ""))


def kingdom_key(kingdom_id):
    return "TAIKOU_kingdom_" + re.sub(r"[^A-Za-z0-9_]", "_", kingdom_id)


def en_name_of(r):
    """英文名：EnglishName → id 罗马块 → CNName（逐级回落，缺的登记出来）。"""
    en = (r.get("EnglishName") or "").strip()
    if en:
        return en
    if r["ID"].startswith("lord_tk5_"):
        slug = r["ID"][len("lord_tk5_"):]
        if not slug.isdigit():
            return slug.replace("_", " ").title()
    return (r.get("CNName") or r["ID"])


def slug_title(clan_id):
    return clan_id.replace("clan_", "").rsplit("_", 1)[0].replace("_", " ").title()


def banner_pool(official_root):
    """从官方 SandBox 家族借 banner_key 池（按序轮转；不瞎编格式）。"""
    p = os.path.join(official_root, "Modules", "SandBox", "ModuleData", "spclans.xml")
    if not os.path.isfile(p):
        return []
    txt = io.open(p, encoding="utf-8", errors="replace").read()
    return sorted(set(re.findall(r'banner_key="([^"]+)"', txt)))


class World:
    """一代的世界模型（英雄 / 家族 / 王国），带自有不变量断言。"""

    def __init__(self, era, hero_rows, clan_rows, force_rows, cultures):
        self.era = era
        self.errors = []
        self.推定 = []                       # 推定值清单（打印出来让人核）
        self.heroes = [r for r in hero_rows
                       if not r.get("TemplateNPC", "")
                       and self._present(r, era)]
        self.hero_ids = {r["ID"] for r in self.heroes}
        # 王国：该年有当主 + 类型属于立国三类（用户 2026-09-12 裁定：武家/忍者/海贼立国，商家不立国）
        self.kingdoms = [r for r in force_rows
                         if (r.get("Owner_" + era, "") or "").strip() not in ("", "-")
                         and (r.get("ForceType") or "") in KINGDOM_TYPES]
        self.kingdom_ids = {r["ID"] for r in self.kingdoms}
        self.force_by_id = {r["ID"]: r for r in force_rows}
        # 家族：**该年家头在场才初始化**（用户裁定同款口径：该年没有当主就不必初始化这个国/家）。
        # 家头不在场（还是孩子/已死）→ 该代不出现这个家族；其成员在该代也就没有 faction（当游荡者）。
        self.clans, dropped, independent = [], [], []
        for c in clan_rows:
            own = (c.get("Owner_" + era, "") or "").strip()
            kd = (c.get("Kingdom_" + era, "") or "").strip()
            if not own or own == "-":
                if kd and kd != "-":
                    dropped.append((c, "该年无家头"))
                continue
            if own not in self.hero_ids:
                dropped.append((c, "家头该年不在场"))
                continue
            if kd and kd != "-" and kd not in self.kingdom_ids:
                independent.append((c, kd))          # 商家等不立国 → 落独立家族（super_faction 留空）
            self.clans.append(c)
        self.dropped = dropped
        self.independent = independent
        self.clan_ids = {r["ID"] for r in self.clans}
        self.clan_by_id = {r["ID"]: r for r in self.clans}
        self.cultures = cultures
        self.hero_clan = {r["ID"]: (r.get("ClanID_" + era, "") or "").strip() for r in self.heroes}
        # 被牵连的英雄：该年家族没初始化 → 他们该年无 faction（当游荡者）
        self.orphan_heroes = [h for h in self.heroes
                              if self.hero_clan.get(h["ID"])
                              and self.hero_clan[h["ID"]] not in self.clan_ids]
        self._validate()

    # ── 在场判据（唯一入口）──
    @staticmethod
    def _present(r, era):
        """🔴 在场 = `Appear_<年> == 已登场`，**或**（`Appear_<年>` 空 且 `ClanID_<年>` 非空 = 推定在场）。

        为什么要有后半条（2026-09-12 用户抓出）：33 位女性（宁宁/阿市/淀夫人/归蝶/濑名…）
        有**逐代家族归属**却没有 `Appear_<年>` 列 → 只看 Appear 会把她们全漏掉。
        推定成立的理由：`ClanID_<年>` 是从**该年的运行时日志**派生的（谁那年侍奉谁）——
        有家族 = 那年在场；缺的只是"登场"标记这一列。
        ⚠️ **只对 Appear 为空时回落**：`已死亡`/`未登场` 即使挂着家族也不进
        （实测"已死亡却仍挂家族"有 12~149 格脏数据，拿它当在场判据会让死人复活）。
        """
        ap = (r.get("Appear_" + era, "") or "").strip()
        if ap == "已登场":
            return True
        if ap:                                   # 未登场 / 已死亡
            return False
        return bool((r.get("ClanID_" + era, "") or "").strip())

    def is_female(self, r):
        """女性判据：`Gender == "0"` = 女；`Gender` 空时按名字推定（姬/姫/公主/夫人）。

        ⚠️ 推定项逐条进 `self.推定`，打印出来让人核（引擎支持女性英雄，拉盖娅同款字段）。
        """
        g = (r.get("Gender", "") or "").strip()
        if g == "0":
            return True
        if g == "1":
            return False
        name = r.get("CNName", "") or ""
        if any(t in name for t in ("姬", "姫", "公主", "夫人")):
            self.推定.append("女性推定（Gender 列为空，按名字判）：%s %s" % (r["ID"], name))
            return True
        return False

    def age_of(self, r):
        """年龄 = 该代年份 − 生年（钳 16~70）；**无生年 → 占位 25 并登记推定**。"""
        by = (r.get("BirthYear", "") or "").strip()
        if by.isdigit():
            return max(16, min(70, int(self.era) - int(by)))
        return 25

    # ── 不变量（每个王国必须有家族 / 每个家族有 owner / 归属链通）──
    def _validate(self):
        e = self.errors
        for c in self.clans:
            own = (c.get("Owner_" + self.era) or "").strip()
            if own and own not in self.hero_ids:
                e.append("家族 %s 的当年家头 %s 不在该代英雄里" % (c["ID"], own))
            if c.get("Culture") and c["Culture"] not in self.cultures:
                e.append("家族 %s 文化 %s 未定义" % (c["ID"], c["Culture"]))
        for k in self.kingdoms:
            own = (k.get("Owner_" + self.era) or "").strip()
            if own and own not in self.hero_ids:
                e.append("王国 %s 的当主 %s 不在该代英雄里" % (k["ID"], own))
            if not own:
                e.append("王国 %s 该年没有当主" % k["ID"])
            if not k.get("Culture"):
                e.append("王国 %s 缺 Culture" % k["ID"])
        # 每个王国至少一家（RulingClan 链；空国 = 引擎建王国时 Leader null）
        with_clan = collections.Counter((c.get("Kingdom_" + self.era) or "").strip()
                                       for c in self.clans)
        for k in self.kingdoms:
            if not with_clan.get(k["ID"]):
                e.append("王国 %s（%s）该年一个家族都没有" % (k["ID"], k.get("ForceName", "")))

    def counts(self):
        nohome = sum(1 for r in self.heroes if not r.get("ClanID_" + self.era))
        female = sum(1 for r in self.heroes if self.is_female(r))
        return dict(heroes=len(self.heroes), clans=len(self.clans),
                    kingdoms=len(self.kingdoms), nohome=nohome, female=female,
                    dropped=len(self.dropped), independent=len(self.independent),
                    orphan=len(self.orphan_heroes))


def build_worlds(csv_dir):
    hero = load(csv_dir, "TaikouHero.csv")
    clan = load(csv_dir, "Clan.csv")
    force = load(csv_dir, "TaikouForce.csv")
    cultures = {r["ID"] for r in load(csv_dir, "Culture.csv")}
    return {e: World(e, hero, clan, force, cultures) for e in ERAS}


# ─────────────────────────── XML 输出 ───────────────────────────
HEADER = ('<?xml version="1.0" encoding="utf-8"?>\n'
          '<!-- 🔴 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_era_world.py 从 csv/ 生成。\n'
          '     数据来源：TaikouHero.csv / Clan.csv / TaikouForce.csv（%s 年）。\n'
          '     改内容 = 改 CSV 或改生成器，然后重跑；check 模式守一致性。\n'
          '     ⚠️ 本段与其它年代的同名段 GameType 互斥（SubModule.xml），不得同时命中同一 GameType。\n'
          '     ⚠️ 注释里禁止出现连续两个减号（XML 规范不允许），改本字符串时注意。 -->\n')


def esc(s):
    return (s or "").replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def write_heroes(w, path):
    L = [HEADER % w.era, "<Heroes>\n"]
    L.append('\t<Hero id="main_hero" faction="Faction.player_faction" text="{=TAIKOU_main_hero}Eren"/>\n')
    for r in sorted(w.heroes, key=lambda x: x["ID"]):
        cid = w.hero_clan.get(r["ID"], "")
        if not cid or cid not in w.clan_ids:
            cid = ronin_clan_id(w.era)      # 无家 → 收容家族（不留空，见 ronin_clan_id 注释）
        fac = ' faction="Faction.%s"' % cid
        L.append('\t<Hero id="%s"%s text="{=%s}%s"/>\n'
                 % (r["ID"], fac, name_key(r["ID"], w.era), esc(en_name_of(r))))
    L.append("</Heroes>\n")
    return "".join(L)


def write_lords(w, path):
    L = [HEADER % w.era, "<NPCCharacters>\n"]
    for r in sorted(w.heroes, key=lambda x: x["ID"]):
        ident = (r.get("Identity_" + w.era) or "").strip()
        if ident in ("", "无效"):
            ident = ""                                  # 无身份（女性/推定在场那批）→ 用默认档
        weapons, armor, civil = EQUIP_BY_IDENTITY.get(ident, EQUIP_DEFAULT)
        fem = w.is_female(r)
        cul = (r.get("CultureID") or "").strip() or CULTURE_FALLBACK
        L.append('\t<NPCCharacter id="%s" default_group="Infantry" age="%d" voice="%s" '
                 'is_hero="true" is_female="%s" culture="Culture.%s" name="{=%s}%s" occupation="Lord" '
                 'banner_symbol_mesh_name="test_symbol_a" banner_symbol_color="FF000000">\n'
                 % (r["ID"], w.age_of(r), VOICE_BY_IDENTITY_FEMALE if fem else VOICE_DEFAULT,
                    "true" if fem else "false", cul, name_key(r["ID"], w.era), esc(en_name_of(r))))
        L.append('\t\t<face>\n\t\t\t<face_key_template value="BodyProperty.fighter_empire"/>\n\t\t</face>\n')
        L.append('\t\t<Equipments>\n\t\t\t<EquipmentRoster>\n')
        for i, it in enumerate(weapons):
            L.append('\t\t\t\t<equipment slot="Item%d" id="Item.%s" />\n' % (i, it))
        for slot, it in sorted(armor.items()):
            L.append('\t\t\t\t<equipment slot="%s" id="Item.%s" />\n' % (slot, it))
        L.append("\t\t\t</EquipmentRoster>\n\t\t\t<EquipmentRoster civilian=\"true\">\n")
        for slot, it in sorted(civil.items()):
            L.append('\t\t\t\t<equipment slot="%s" id="Item.%s" />\n' % (slot, it))
        L.append("\t\t\t</EquipmentRoster>\n\t\t</Equipments>\n\t</NPCCharacter>\n")
    L.append("</NPCCharacters>\n")
    return "".join(L)


def ronin_clan_id(era):
    """该代的**收容家族** id（无名无主的浪人/师范/医师/锻冶匠/僧侣/茶人 统一落它）。

    🔴 为什么必须给这些英雄一个家族（2026-09-12）：骑砍里**每个英雄都属某个家族**（原版零例外），
    「Hero 无 faction」没有先例 → `Hero.Clan` 为 null 会在多少条链路上裸解引用未知
    （铁律 1：不许拿新档去试）。所以给一个无地的收容家
    （`is_minor_faction="true"`、无 super_faction——与「柳生石舟斋独立家族」同款形态），而不是留空。
    """
    return "clan_ronin_%s" % era


# 玩家族（建号结束主角加入）——**每代都要写**：原手写 spclans.xml 里有它，
# 段一旦交给生成器接管就得继续提供（否则 main_hero 的 faction 悬空）。
PLAYER_FACTION = ('\t<Faction id="player_faction" is_noble="true" owner="Hero.main_hero" '
                  'banner_key="11.154.116.1536.1536.768.768.1.0.0.609.15.155.483.483.773.729.0.0.0" '
                  'is_minor_faction="false" label_color="FFD2C0AA" color="FF8D5C44" color2="FFE9A74D" '
                  'alternative_color="FF6C5749" alternative_color2="FFB3A491" culture="Culture.ikoku" '
                  'settlement_banner_mesh="encounter_flag_a" name="{=TAIKOU_player_faction}Player" tier="0">\n'
                  '\t\t<Influence>\n\t\t\t<base_influence value="30.0"/>\n\t\t</Influence>\n'
                  '\t</Faction>\n')


def write_clans(w, path):
    L = [HEADER % w.era, "<Factions>\n", PLAYER_FACTION]
    for i, c in enumerate(sorted(w.clans, key=lambda x: x["ID"])):
        kd = (c.get("Kingdom_" + w.era) or "").strip()
        # 🔴 XML 里 Kingdom 的 id 写作 `kingdom_<势力id>`（沿用现有 spkingdoms.xml 制式），
        #    所以 super_faction 也必须写全 `Kingdom.kingdom_<id>`——少个前缀就是悬空引用。
        super_fac = (' super_faction="Kingdom.kingdom_%s"' % kd
                     if kd in w.kingdom_ids else "")
        minor = "false" if super_fac else "true"     # 独立家族 = minor faction（纳屋先例）
        bkey = w.banners[i % len(w.banners)]
        key = clan_key(c["ID"])
        nm = esc(slug_title(c["ID"]))
        L.append('\t<Faction id="%s" is_noble="true" owner="Hero.%s" banner_key="%s" '
                 'is_minor_faction="%s"%s culture="Culture.%s" settlement_banner_mesh="encounter_flag_a" '
                 'name="{=%s}%s" short_name="{=%s}%s" title="{=%s}%s" tier="3">\n'
                 % (c["ID"], c.get("Owner_" + w.era, ""), bkey, minor, super_fac,
                    (c.get("Culture") or CULTURE_FALLBACK), key, nm, key, nm, key, nm))
        L.append('\t\t<Influence>\n\t\t\t<base_influence value="60.0"/>\n\t\t</Influence>\n')
        L.append("\t</Faction>\n")
    # 收容家族（该代有浪人时才写；owner = 排序后第一个无家英雄，保证确定性）
    ronin = sorted(r["ID"] for r in w.heroes
                   if not w.hero_clan.get(r["ID"]) or w.hero_clan[r["ID"]] not in w.clan_ids)
    if ronin:
        key = clan_key(ronin_clan_id(w.era))
        L.append('\t<Faction id="%s" is_noble="false" owner="Hero.%s" banner_key="%s" '
                 'is_minor_faction="true" culture="Culture.ronin" settlement_banner_mesh="encounter_flag_a" '
                 'name="{=%s}Ronin" short_name="{=%s}Ronin" title="{=%s}Ronin" tier="1">\n'
                 '\t\t<Influence>\n\t\t\t<base_influence value="10.0"/>\n\t\t</Influence>\n'
                 '\t</Faction>\n' % (ronin_clan_id(w.era), ronin[0], w.banners[5 % len(w.banners)],
                                     key, key, key))
    L.append("</Factions>\n")
    return "".join(L)


def write_kingdoms(w, path):
    L = [HEADER % w.era, "<Kingdoms>\n"]
    for i, k in enumerate(sorted(w.kingdoms, key=lambda x: x["ID"])):
        bkey = w.banners[(i * 7 + 3) % len(w.banners)]
        key = kingdom_key(k["ID"])
        nm = esc(slug_title_from_slug(k["ID"]))
        L.append('\t<Kingdom\n\t\tid="kingdom_%s"\n\t\towner="Hero.%s"\n\t\tbanner_key="%s"\n'
                 '\t\tprimary_banner_color="0xff7a94d0"\n\t\tsecondary_banner_color="0xff1d2c53"\n'
                 '\t\tlabel_color="FF5573BE"\n\t\tcolor="FF5573BE"\n\t\tcolor2="FFDE9953"\n'
                 '\t\talternative_color="FFCBC25D"\n\t\talternative_color2="FF5D6347"\n'
                 '\t\tculture="Culture.%s"\n\t\tsettlement_banner_mesh="encounter_flag_a"\n'
                 '\t\tflag_mesh="info_screen_flags_a"\n'
                 '\t\tname="{=%s}%s"\n\t\tshort_name="{=%s}%s"\n\t\ttitle="{=%s}%s"\n'
                 '\t\truler_title="{=TAIKOU_daimyo}Daimyo"\n\t\ttext="{=%s_text}%s">\n\t</Kingdom>\n'
                 % (k["ID"], k.get("Owner_" + w.era, ""), bkey, (k.get("Culture") or CULTURE_FALLBACK),
                    key, nm, key, nm, key, nm, key, nm))
    L.append("</Kingdoms>\n")
    return "".join(L)


def slug_title_from_slug(fid):
    return fid.replace("org_", "").rsplit("_", 1)[0].replace("_", " ").title()


# ─────────────────────── 语言层（名字键族，中英键集必须相等）───────────────────────
# 🔴 本生成器**接管三个键族**：`TAIKOU_hero_*` / `TAIKOU_clan_*` / `TAIKOU_kingdom_*`。
#    理由：英雄名字键按「英雄×年代」生成（4498 条），且 id 体系换了（clan_oda → clan_oda_1）——
#    旧键（TAIKOU_clan_oda / TAIKOU_hero_nobunaga）已无人引用，留着会让「英文键集 == 中文键集」
#    这条不变量破掉（英文层由 gen_taikou_english_strings 从数据 XML 重生成，旧键自动消失）。
#    做法与 `sync_taikou_bio_cns.py` 同款：**只动这三个键族 + 自己的标记块，其余一字节不碰**，幂等整块替换。
CN_BLOCK_MARK = "<!-- ==== 生成块：英雄/家族/王国名字（Scripts/gen_taikou_era_world.py 产出，禁止手改）==== -->"
CN_KEY_FAMILIES = ("TAIKOU_hero_", "TAIKOU_clan_", "TAIKOU_kingdom_")


def build_cn_block(worlds, eras, clan_rows, force_rows):
    """→ (块文本, 条目数)。中文名一律取 CSV 的中文列（英雄取当年代名，家族/王国取本名）。"""
    ent = []
    for e in eras:
        w = worlds[e]
        for r in sorted(w.heroes, key=lambda x: x["ID"]):
            ent.append((name_key(r["ID"], e), cn_name_of(r, e)))
    seen = set()
    for e in eras:                                       # 收容家族（浪人众）也要名字
        w = worlds[e]
        if any(not w.hero_clan.get(r["ID"]) or w.hero_clan[r["ID"]] not in w.clan_ids
               for r in w.heroes):
            ent.append((clan_key(ronin_clan_id(e)), "浪人众"))
    for c in clan_rows:                                  # 家族名跨代不变 → 每族一条
        nm = (c.get("Name") or "").strip()
        k = clan_key(c["ID"])
        if nm and k not in seen:
            seen.add(k)
            ent.append((k, nm))
    for f in force_rows:
        nm = (f.get("ForceName") or "").strip()
        k = kingdom_key(f["ID"])
        if nm and k not in seen:
            seen.add(k)
            ent.append((k, nm))
    lines = [CN_BLOCK_MARK + "\n"]
    for k, v in sorted(ent):
        lines.append('  <string id="%s" text="%s" />\n' % (k, esc(v)))
    return "".join(lines), len(ent)


def sync_cn(md, block):
    """把生成块写进 CN 语言文件（并清掉三个键族的旧条目）。→ (是否变化, 删了几条旧条目)"""
    path = os.path.join(md, "Languages", "CNs", "std_Taikou_strings.xml")
    if not os.path.isfile(path):
        return None, 0
    raw = io.open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    eol = "\r\n" if "\r\n" in text else "\n"
    # 1) 删掉旧块（标记行 + 紧随其后的 <string> 行）。
    #    🔴 必须连行删干净：块的**位置**是本文件的头号坑（见第 3 步），旧产物散在两种位置，
    #    按「标记行 + 后面连续的 string 行」扫，两种都能吃。
    lines, removed = text.split("\n"), 0
    out, i = [], 0
    while i < len(lines):
        if CN_BLOCK_MARK in lines[i]:
            i += 1
            while i < len(lines) and "<string " in lines[i]:
                removed += 1
                i += 1
            continue
        out.append(lines[i])
        i += 1
    # 2) 删掉三个键族的散条目（不管在文件哪一处）
    kept = []
    for line in out:
        m = re.search(r'<string id="([^"]+)"', line)
        if m and any(m.group(1).startswith(f) for f in CN_KEY_FAMILIES):
            removed += 1
            continue
        kept.append(line)
    text = "\n".join(kept)
    # 3) 插到 </strings> **之内**
    #    🔴🔴 位置铁律（2026-09-12 实机事故）：引擎 LocalizedTextManager.LoadLanguage 只遍历
    #    `<strings>` 的子节点，写在 `</strings>` 之后的 string 一律**静默不加载**（不报错、不崩）。
    #    旧版这里插在 `</base>` 前 = 全在 `</strings>` 之后 → 势力/家族/英雄 4985 条中文全部失效，
    #    玩家的选人界面看到的是数据 XML 里的英文 fallback。改动此处前先读
    #    `plans/rules/wheels.d/campaign-mode.md` 文本线体检三件套。
    idx = text.rfind("</strings>")
    if idx < 0:
        print("  [FATAL] CN 文件里找不到 </strings> 锚点——结构变了？本块不写（玩家会看到英文名）",
              file=sys.stderr)
        return None, removed
    new = text[:idx] + block + text[idx:]
    data = new.replace("\r\n", "\n").replace("\n", eol).encode("utf-8-sig")
    if data == raw:
        return False, removed
    io.open(path, "wb").write(data)
    return True, removed


def main():
    ap = argparse.ArgumentParser(description="Taikou six-era world generator")
    ap.add_argument("--csv-dir", default=DEFAULT_CSV)
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("--official-root", default=None, help="游戏根（缺省=读注册表 MB2_PATH）")
    ap.add_argument("--era", default=None, help="只做一代（调试）")
    ap.add_argument("--dry-run", action="store_true", help="只算不写")
    ap.add_argument("--check", action="store_true", help="只校验产物是否最新")
    ap.add_argument("-v", "--verbose", action="store_true", help="打印推定项明细")
    args = ap.parse_args()

    if not os.path.isdir(args.csv_dir):
        print("[FATAL] csv dir not found: %s" % args.csv_dir, file=sys.stderr)
        return 2

    official = args.official_root
    if not official:
        if sys.platform == "win32":
            import winreg
            for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                              (winreg.HKEY_LOCAL_MACHINE,
                               r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
                try:
                    with winreg.OpenKey(hive, sub) as k:
                        official, _ = winreg.QueryValueEx(k, "MB2_PATH")
                        if official:
                            break
                except OSError:
                    continue
    banners = banner_pool(official) if official else []
    if not banners:
        print("[FATAL] 借不到官方旗号池（--official-root / 注册表 MB2_PATH）", file=sys.stderr)
        return 2

    eras = [args.era] if args.era else ERAS
    worlds = build_worlds(args.csv_dir)
    bad = [(e, w.errors) for e, w in worlds.items() if w.errors]

    print("太阁六代世界段生成器（%s）" % ("干跑" if args.dry_run else
                                    "只校验" if args.check else "写盘"))
    print("  旗号池：%d 个官方家族 banner" % len(banners))
    for e in eras:
        w = worlds[e]
        c = w.counts()
        print("\n== %s ==" % e)
        print("  英雄 %4d（女性 %2d · 无家 %d）· 家族 %3d · 王国 %3d"
              % (c["heroes"], c["female"], c["nohome"], c["clans"], c["kingdoms"]))
        print("      家族：独立家族（商家等不立国）%d · **该代不初始化** %d · 因此该代无 faction 的英雄 %d"
              % (c["independent"], c["dropped"], c["orphan"]))
        if w.dropped:
            why = collections.Counter(y for _c, y in w.dropped)
            print("      不初始化原因：%s" % " · ".join("%s×%d" % (k, v) for k, v in why.most_common()))
        if w.errors:
            print("  ❌ 不变量 %d 条：" % len(w.errors))
            for x in w.errors[:8]:
                print("      %s" % x)
            if len(w.errors) > 8:
                print("      … 另有 %d 条" % (len(w.errors) - 8))
        if args.verbose and w.推定:
            print("  ⚠️ 推定项 %d 条：" % len(w.推定))
            for x in w.推定[:20]:
                print("      %s" % x)

    if bad:
        print("\n❌ 有 %d 代不变量不过（上面逐条）——先改数据再生成。" % len(bad))
        return 1
    print("\n✅ 六代不变量全过（每个王国都有家族、每个家族都有当年家头且家头在该代英雄里）")
    if args.dry_run:
        return 0

    # ── 写盘（先全算成字符串 → 比对/写入；一个产物一个产出方）──
    md = os.path.join(args.module, "ModuleData")
    if not os.path.isdir(md):
        print("[FATAL] ModuleData 不存在：%s" % md, file=sys.stderr)
        return 2
    for w in worlds.values():
        w.banners = banners
    plan = []
    for e in eras:
        w = worlds[e]
        sfx = era_suffix(e)
        plan += [(os.path.join(md, "taikou_heroes%s.xml" % sfx), write_heroes(w, None)),
                 (os.path.join(md, "taikou_lords%s.xml" % sfx), write_lords(w, None)),
                 (os.path.join(md, "spclans%s.xml" % sfx), write_clans(w, None)),
                 (os.path.join(md, "spkingdoms%s.xml" % sfx), write_kingdoms(w, None))]

    # 往返校验：写完必须能逐字读回（写的内容 = 校验的内容，同一个字符串）
    for path, text in plan:
        try:
            ET.fromstring(text.encode("utf-8"))
        except ET.ParseError as ex:
            print("[FATAL] 生成的 XML 解析不过：%s —— %s" % (os.path.basename(path), ex), file=sys.stderr)
            return 2

    if args.check:
        stale = [(p, t) for p, t in plan
                 if not os.path.isfile(p)
                 or io.open(p, "rb").read() != ("﻿" + t).encode("utf-8")]
        for p, _t in stale:
            print("  [过期] %s —— 请重跑生成器" % os.path.basename(p))
        cn_path = os.path.join(md, "Languages", "CNs", "std_Taikou_strings.xml")
        cn_txt = io.open(cn_path, encoding="utf-8-sig").read() if os.path.isfile(cn_path) else ""
        cn_ok = CN_BLOCK_MARK in cn_txt
        print("\n%s：%d 个产物%s · CN 名字块%s"
              % ("❌ 有过期" if (stale or not cn_ok) else "✅ 已最新", len(plan),
                 "" if not stale else "（%d 个不一致）" % len(stale),
                 "在" if cn_ok else "**缺失**"))
        return 1 if (stale or not cn_ok) else 0

    written = 0
    for path, text in plan:
        data = ("﻿" + text).encode("utf-8")     # LF + BOM（与 spcultures.xml 同款）
        if os.path.isfile(path) and io.open(path, "rb").read() == data:
            continue                                  # 幂等：内容一样就不动盘（保住 mtime）
        io.open(path, "wb").write(data)
        written += 1
    print("\n✅ 写入 %d 个产物（另 %d 个内容未变、未动盘）" % (written, len(plan) - written))

    block, n = build_cn_block(worlds, eras, load(args.csv_dir, "Clan.csv"),
                              load(args.csv_dir, "TaikouForce.csv"))
    changed, removed = sync_cn(md, block)
    if changed is None:
        print("  ⚠️ CN 语言文件不存在，跳过名字块（%d 条名字键未落中文）" % n)
    else:
        print("  ✅ CN 名字块：%d 条（%s；清掉旧键 %d 条）"
              % (n, "已更新" if changed else "无需变更", removed))
    print("   幂等自检：再跑一次 check 模式应为 ✅")
    return 0


if __name__ == "__main__":
    sys.exit(main())

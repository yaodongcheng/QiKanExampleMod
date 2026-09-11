#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Taikou 英雄画像表生成器（选人详情页的数据源）
============================================================================
为什么要有这张表：详情页要展示「太阁5 的五维 / 16 技能 / 生卒」，这些数全在
`TaikouHero.csv` 里——但 **C# 不解析 CSV**（CSV 是人维护的源，运行期解析 = 每次启动
开销 + 格式漂移风险）。故按本项目惯例走生成器（铁律 22）：CSV → XML → 运行期读 XML。

输入
----
  `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/TaikouHero.csv`
    （**只读镜像**，禁止手改——见 wheels.d/data-registry.md「织丰表数据源纪律」）
    取用列：ID / BirthYear / DieYear / ClanID / CultureID
            五维 CommandValue ForceValue GovernValue WisdomValue CharmValue
            16 技能 SoldierSkill MountSkill GunSkill NavySkill ArcherSkill CombatSkill
                   MilitarySkill NinjaSkill BuildSkill FarmSkill MineSkill ArthmeticSkill
                   EtiquetteSkill DebateSkill TeaSkill MedicalSkill

产出（内容包 ModuleData）
------------------------
  `ModuleData/AssetRegistry/HeroProfiles.xml`   ← 生成物·禁止手改
     <HeroProfile id="lord_tk5_195" birth="1534" die="1582" clan="clan_oda_1" culture="kinai"
                  command="96" force="87" … soldier="90" …
                  bio="{=TAIKOU_bio_195}尾张国出生。…" />
     <Recommended order="1" id="lord_tk5_517"
                  storyType="{=TAIKOU_story_type_bushi}Samurai Story"
                  storyGoal="{=TAIKOU_story_goal_kinoshita}…" />

🔴 键 = 英雄 StringId，且与另两张表**同键**（铁律 20 + 「一个 id 走通全链」）：
   `spnpccharacters.xml` 的 NPCCharacter id ／ `ProfileStages.csv` 的 StringId。
   三处不同键 = 详情页取不到立绘/数据（步 0 就是把它们对齐成 CSV 制式）。

🔴 人工数据进本脚本的映射表，不进 CSV（镜像 CSV 禁手改）：
   「推荐人」名单/顺序/型别/目标描述 = 太阁5 实机截图抄录，见下方 RECOMMENDED。

Usage:
  python Scripts/gen_taikou_hero_profiles.py            # 重跑产出
  python Scripts/gen_taikou_hero_profiles.py --check    # 只校验产物是否最新（不一致 exit 1）
  python Scripts/gen_taikou_hero_profiles.py --module PATH
"""
import argparse
import csv
import io
import os
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

# 🔴 通用契约路径（不叫 taikou_*——LWN 侧读取器必须与内容包无关，铁律 3）：
#    任何内容包把画像表放在 ModuleData/AssetRegistry/HeroProfiles.xml，LWN 即自动接上
#    （与 ProfileStages.csv / ProfileEmotion.csv 同域同约定）。
OUT_NAME = "HeroProfiles.xml"
OUT_SUBDIR = "AssetRegistry"

# 五维：CSV 列名 → XML 属性名
FIVE = [("CommandValue", "command"), ("ForceValue", "force"), ("GovernValue", "govern"),
        ("WisdomValue", "wisdom"), ("CharmValue", "charm")]

# 16 技能：CSV 列名 → XML 属性名（顺序 = 详情页显示顺序 = 太阁5 原版顺序）
SKILLS = [("SoldierSkill", "soldier"), ("MountSkill", "mount"), ("GunSkill", "gun"),
          ("NavySkill", "navy"), ("ArcherSkill", "archer"), ("CombatSkill", "combat"),
          ("MilitarySkill", "military"), ("NinjaSkill", "ninja"), ("BuildSkill", "build"),
          ("FarmSkill", "farm"), ("MineSkill", "mine"), ("ArthmeticSkill", "arthmetic"),
          ("EtiquetteSkill", "etiquette"), ("DebateSkill", "debate"), ("TeaSkill", "tea"),
          ("MedicalSkill", "medical")]

# ── 「推荐」五人（太阁5 五种故事型）──────────────────────────────────────────
# 来源：用户提供的太阁5 实机截图抄录（2026-09-10 裁定：点「推荐」固定进 1560，显示这 5 人）。
# 顺序即列表显示顺序（太阁5 原版顺序）。型别/目标描述 = 剧本无关（同一人物任何年代同一型）。
# 🔴 型别与目标描述是玩家可见文本 → 走 {=TAIKOU_KEY}fallback（铁律 13），英文层由
#    gen_taikou_english_strings.py 自动抽取，中文层人工补在 Languages/CNs/。
RECOMMENDED = [
    ("lord_tk5_517", "TAIKOU_story_type_bushi", "Samurai Story",
     "TAIKOU_story_goal_kinoshita",
     "Rise from the ranks and unite the realm. Governance, arms and diplomacy must all be mastered."),
    ("lord_tk5_587", "TAIKOU_story_type_ninja", "Ninja Story",
     "TAIKOU_story_goal_hanzo",
     "Unite the realm under the lord you serve. Your work is espionage, seizure and sabotage."),
    ("lord_tk5_740", "TAIKOU_story_type_kenkou", "Swordmaster Story",
     "TAIKOU_story_goal_yagyuu",
     "Perfect your sword and become the finest blade in the land. To that end, revive your school and gather disciples."),
    ("lord_tk5_279", "TAIKOU_story_type_suigun", "Sea Lord Story",
     "TAIKOU_story_goal_kuki",
     "Unite the realm under the lord you serve. Subdue the rival sea lords and pacify the waters."),
    ("lord_tk5_549", "TAIKOU_story_type_shounin", "Merchant Story",
     "TAIKOU_story_goal_ruzon",
     "Dominate the markets of the realm. Raise your fortune by trade and by every means at hand."),
]

# ── 标签（玩家可见文本 → 归**内容包**，不进 LWN：铁律 3）────────────────────────
# 五维名称（顺序 = FIVE）
DIM_LABELS = [("command", "TAIKOU_dim_command", "Command"),
              ("force", "TAIKOU_dim_force", "Valor"),
              ("govern", "TAIKOU_dim_govern", "Governance"),
              ("wisdom", "TAIKOU_dim_wisdom", "Wisdom"),
              ("charm", "TAIKOU_dim_charm", "Charisma")]

# 16 技能名称（顺序 = SKILLS，且必须与 LWN 侧 HeroProfileRegistry.SkillIds 同序）
SKILL_LABELS = [("soldier", "TAIKOU_skill_soldier", "Spearmen"),
                ("mount", "TAIKOU_skill_mount", "Horsemanship"),
                ("gun", "TAIKOU_skill_gun", "Gunnery"),
                ("navy", "TAIKOU_skill_navy", "Navy"),
                ("archer", "TAIKOU_skill_archer", "Archery"),
                ("combat", "TAIKOU_skill_combat", "Swordsmanship"),
                ("military", "TAIKOU_skill_military", "Strategy"),
                ("ninja", "TAIKOU_skill_ninja", "Ninjutsu"),
                ("build", "TAIKOU_skill_build", "Construction"),
                ("farm", "TAIKOU_skill_farm", "Farming"),
                ("mine", "TAIKOU_skill_mine", "Mining"),
                ("arthmetic", "TAIKOU_skill_arthmetic", "Arithmetic"),
                ("etiquette", "TAIKOU_skill_etiquette", "Etiquette"),
                ("debate", "TAIKOU_skill_debate", "Rhetoric"),
                ("tea", "TAIKOU_skill_tea", "Tea Ceremony"),
                ("medical", "TAIKOU_skill_medical", "Medicine")]

HEADER = ('<?xml version="1.0" encoding="utf-8"?>\n'
          '<!-- 🔴 生成物·禁止手改（铁律 22）——由 Scripts/gen_taikou_hero_profiles.py 从\n'
          '     Knowledge/太阁5/骑砍2织丰角色ID对应/csv/TaikouHero.csv 生成。\n'
          '     改数据 = 改上游 CSV（或生成器的 RECOMMENDED 表）后重跑，不要直接编辑本文件。\n'
          '     键 = 英雄 StringId，与 spnpccharacters.xml 的 NPCCharacter id、\n'
          '     ModuleData/AssetRegistry/ProfileStages.csv 的 StringId **三处同键**（否则详情页取不到数据/立绘）。 -->\n')


def _to_simplified(s):
    """繁体 → 简体（列传原文落 XML 用）。

    🔴 2026-09-11：CSV 的 `列传简体` 列已删，转换点挪到生成期。opencc 缺失时 FATAL，
       不静默把繁体当简体写进产物（那样英文层与中文层都会是繁体且没人发现）。
    """
    if not s:
        return ""
    try:
        import opencc
    except Exception as exc:                                 # noqa: BLE001
        raise SystemExit(f"[FATAL] 需要 opencc 转简体（列传）：{exc}")
    return opencc.OpenCC("t2s").convert(s)


def registry_mb2_path():
    """读注册表 MB2_PATH（铁律 19：环境变量以注册表为准，进程快照不可信）。"""
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


def esc(s):
    return (str(s).replace("&", "&amp;").replace("<", "&lt;")
            .replace(">", "&gt;").replace('"', "&quot;"))


def int_or_zero(raw):
    """CSV 数值列 → 非负整数（空/脏值按 0；越界由 check() 报错，不在此处悄悄夹紧）。"""
    try:
        return int(str(raw).strip())
    except (TypeError, ValueError):
        return 0


def read_csv(path):
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def build(rows):
    """行 → XML 文本。顺带做完整性自检，返回 (text, problems)。"""
    problems = []
    seen = set()
    lines = []
    for i, r in enumerate(rows, start=2):          # 2 = 首行是表头
        hid = (r.get("ID") or "").strip()
        if not hid:
            problems.append(f"第 {i} 行：ID 为空")
            continue
        if hid in seen:
            problems.append(f"第 {i} 行：ID 重复 —— {hid}")
            continue
        seen.add(hid)

        attrs = [("id", hid),
                 ("birth", int_or_zero(r.get("BirthYear"))),
                 ("die", int_or_zero(r.get("DieYear"))),
                 ("clan", (r.get("ClanID") or "").strip()),
                 ("culture", (r.get("CultureID") or "").strip())]
        for col, name in FIVE:
            v = int_or_zero(r.get(col))
            if not 0 <= v <= 100:
                problems.append(f"第 {i} 行（{hid}）：{col}={v} 越界（应为 0–100）")
            attrs.append((name, v))
        for col, name in SKILLS:
            v = int_or_zero(r.get(col))
            if not 0 <= v <= 100:
                problems.append(f"第 {i} 行（{hid}）：{col}={v} 越界（应为 0–100）")
            attrs.append((name, v))

        # 一行一个英雄（字段多，用换行缩进排布，便于人工 diff）
        head = " ".join(f'{k}="{esc(v)}"' for k, v in attrs[:5])
        body = " ".join(f'{k}="{esc(v)}"' for k, v in attrs[5:13])
        tail = " ".join(f'{k}="{esc(v)}"' for k, v in attrs[13:])

        # 🔴 列传（= 原版 `<Hero text=…>` 的百科传记字段，见 Hero.Deserialize：
        #    `EncyclopediaText = node.Attributes["text"]`）。
        #    源 = 太阁5 实机导出的列传**繁体原文**（CSV 的 列传/列传原文 两列，由
        #    Scripts/import_taikou_hero_bios.py 落库）。
        #    🔴 2026-09-11：CSV 的 `列传简体` 列已删（用户裁定：列传只留繁体原文，
        #       简体/英文在生成本地化产物时再走正式流程）→ 这里自己转简体。
        bio_key = (r.get("列传") or "").strip()
        bio_raw = (r.get("列传原文") or "").strip()
        bio_simp = _to_simplified(bio_raw) if bio_raw else ""
        if bio_key and bio_simp:
            lines.append(f'  <HeroProfile {head}\n               {body}\n               {tail}\n'
                         f'               bio="{{={bio_key}}}{esc(bio_simp)}" />')
        else:
            lines.append(f'  <HeroProfile {head}\n               {body}\n               {tail} />')

    rec = []
    for order, (hid, type_key, type_en, goal_key, goal_en) in enumerate(RECOMMENDED, start=1):
        if hid not in seen:
            problems.append(f"推荐人 {hid} 不在 CSV 中（名单与数据源脱节）")
        rec.append(f'  <Recommended order="{order}" id="{esc(hid)}"\n'
                   f'               storyType="{{={type_key}}}{esc(type_en)}"\n'
                   f'               storyGoal="{{={goal_key}}}{esc(goal_en)}" />')

    text = (HEADER + "<HeroProfiles>\n"
            + "\n".join(lines) + "\n\n"
            + "  <!-- 标签：玩家可见文本归内容包（铁律 3——LWN 侧不出现具体世界观词汇）。\n"
            + "       顺序 = 数据字段顺序，读取端按序取用；缺标签时读取端退回字段名。 -->\n"
            + "  <DimensionLabels>\n"
            + "".join(f'    <Label field="{f}" text="{{={k}}}{esc(en)}" />\n'
                     for f, k, en in DIM_LABELS)
            + "  </DimensionLabels>\n"
            + "  <SkillLabels>\n"
            + "".join(f'    <Label field="{f}" text="{{={k}}}{esc(en)}" />\n'
                     for f, k, en in SKILL_LABELS)
            + "  </SkillLabels>\n\n"
            + "  <!-- 「推荐」五人（太阁5 五种故事型）——点「推荐」时列表显示这 5 人；\n"
            + "       型别/目标描述是玩家可见文本，走 {=TAIKOU_KEY}fallback 本地化。 -->\n"
            + "\n".join(rec) + "\n</HeroProfiles>\n")
    return text, problems


def main():
    ap = argparse.ArgumentParser(description="Taikou hero profile table generator")
    ap.add_argument("--check", action="store_true", help="只校验产物是否最新")
    ap.add_argument("--module", default=None, help="内容包目录（缺省=注册表 MB2_PATH 下的 Taikou）")
    ap.add_argument("--csv", default=None, help="TaikouHero.csv 路径")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    proj = os.path.dirname(here)
    csv_path = args.csv or os.path.join(proj, "Knowledge", "太阁5",
                                        "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
    mb2 = registry_mb2_path()
    module = args.module or (os.path.join(mb2, "Modules", "Taikou") if mb2 else None)
    if not module or not os.path.isdir(os.path.join(module, "ModuleData")):
        print(f"[FATAL] 找不到内容包 ModuleData：{module}", file=sys.stderr)
        return 2
    if not os.path.isfile(csv_path):
        print(f"[FATAL] 找不到源 CSV：{csv_path}", file=sys.stderr)
        return 2

    rows = read_csv(csv_path)
    text, problems = build(rows)

    # 生成即 parse（防产出非法 XML）
    try:
        ET.fromstring(text)
    except ET.ParseError as e:
        print(f"[FATAL] 产出非法 XML：{e}", file=sys.stderr)
        return 2

    if problems:
        print(f"[FATAL] 数据自检未通过（{len(problems)} 条）：")
        for p in problems[:20]:
            print("  - " + p)
        if len(problems) > 20:
            print(f"  … 另有 {len(problems) - 20} 条")
        return 1

    path = os.path.join(module, "ModuleData", OUT_SUBDIR, OUT_NAME)
    old = io.open(path, encoding="utf-8").read() if os.path.isfile(path) else None
    if args.check:
        if old == text:
            print(f"OK：{OUT_NAME} 已最新（英雄 {len(rows)} 名 / 推荐 {len(RECOMMENDED)} 名）")
            return 0
        print(f"产物与生成器不一致（需重跑）：{OUT_NAME}")
        return 1

    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    print(f"{'写入' if old != text else '未变'} {OUT_NAME}"
          f"（英雄 {len(rows)} 名 / 推荐 {len(RECOMMENDED)} 名）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

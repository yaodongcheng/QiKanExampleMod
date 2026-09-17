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
  · **旗号 = 官方 SandBox 家族池借来的「底旗」+ 自家家纹图标**（2026-09-16 改）——
    底色/配色/几何仍借官方键（官方调好的对比度，不瞎编）；只在第 11 段换成
    `Clan.csv`/`TaikouForce.csv` 的 `Mon` 列所指定家纹（见 `taikou_mon_atlas.py`）。
    `Mon` 为空 → 输出**纯色旗**（剥掉图标），免得混进欧洲纹章。
  · **逐角色身高**（2026-09-17，第 4 步）：领主 `<face>` 里**内联** `<BodyProperties>`，
    身高 = 脸键 `KeyPart8` 的 6 bit（位 19..24）。有名武将取 `tools/sw2-pipeline/out/srcT.json`
    的 `height_ratio`（网格管线量出来的「相对原版等高」倍数）；其余 1300+ 人按 StringId
    确定性落在自然带内（同一个人六代同高、重跑同高，但彼此有高低差）。
    **改身高 = 改 `HEIGHT_BAND` / 网格管线的 ratio 再重跑**；证据与标定实验见
    `Debug/offline/_step4_prereq.md`。
  · **逐角色体重 / 体型**（2026-09-17）：同一条 `<BodyProperties>` 的 `weight=` / `build=` 属性。
    **不在脸键里**（脸键只有身高那 6 bit），且实现是**骨架骨缩放**（`skins.xml` 的 `<bone_scales>`）
    → 我们的甲/头会跟着被拉伸，且甲本来就是按本人身形做的（双重计入）。
    ⇒ **已打开**（`APPLY_BODY_SHAPE = True`，2026-09-17 用户裁定「都要」）：只有 28 个有名武将
    有逐人体型，其余 1300+ 人仍写模板值；逐人真实体型与量法见
    `Debug/offline/_bodyprops_table.md`（含量法、证据链、逐人一行）。
    **一键关回去 = 改 `APPLY_BODY_SHAPE = False` 后重跑本脚本**（六代一起回到模板值）。

Usage:
  python Scripts/gen_taikou_era_world.py --dry-run          # 只算不写：逐代条目数 + 链完整性 + 异常
  python Scripts/gen_taikou_era_world.py                    # 写盘（含 CN 名字块同步）
  python Scripts/gen_taikou_era_world.py --check            # 只校验磁盘产物是否最新（不一致 exit 1）
  python Scripts/gen_taikou_era_world.py --era 1560         # 只做一代（调试用）
Exit: 0 成功 / 1 --check 不一致或有硬错误 / 2 fatal。
"""
import argparse
import collections
import hashlib
import io
import json
import os
import re
import sys
import xml.etree.ElementTree as ET


if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

sys.path.insert(0, os.path.join(REPO, "Scripts"))
import taikou_equip_tables as EQ      # noqa: E402  兵种表 / 武将装备档表
import taikou_mon_atlas as MON        # noqa: E402  家纹图集表（Mon 列 → 引擎图标 id）
DEFAULT_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
BASELINE_ERA = "1560"                      # 无后缀文件 = 这一代
KINGDOM_TYPES = ("Warrior", "Ninja", "Pirate")   # 立国的势力类型（用户裁定）
CULTURE_FALLBACK = "ikoku"                 # CSV 没给文化时的兜底（现有最小集同款）

# ── 身高（第 4 步：逐角色身高交给游戏自身的缩放，不做进网格）──────────────────
# 为什么在这里：骑砍2 的角色身高 = `BodyProperties` 脸键里 KeyPart8 的 6 bit（位 19..24），
#   由 native 换算成一个**均匀缩放标量**乘在整个人（含装备）上。**英雄取的是模板的 min 那份**
#   （`CharacterObject.GetBodyPropertiesMin()`）——所以「某个人多高」= 他 min 键里的那 6 bit。
# 数据源：`tools/sw2-pipeline/out/srcT.json`（网格管线产出）的 `height_ratio`
#   = 该角色净身高 ÷ 原版等高基准（1.0 = 与网格侧统一后的原版身高）。
# 落点：领主 `<face>` 里的**内联** `<BodyProperties>`。
#   🔴 不能与 `<face_key_template>` 并存：C# 侧只要读到 face_key_template 就用模板，
#      内联那条会被整个丢掉（`TaleWorlds.Core.BasicCharacterObject.Deserialize`）。
# 改法：键串 = 8 段各 16 个十六进制字符；**只改最后一段（KeyPart8）的 bit 19..24**，
#   其余 7 段与全部其它位一字不动 —— 脸形/体格特征完全沿用基准键，与网格侧互不干扰。
SW2_SRC_T = os.path.join(REPO, "tools", "sw2-pipeline", "out", "srcT.json")
BASE_BODY_PROPERTY = "fighter_empire"      # 基准键（除身高外的一切特征取自它）
# 身高带：bit 0..63 ↔ 身高倍数 1±HEIGHT_BAND（对称带，中心 = 原版等高基准）。
# 🔴 **已定标（2026-09-17，反汇编 native 实证，别再猜）**：`TaleWorlds.Native.dll`
#    · `get_scale`（0x18060cad0）：`rax = [BodyProperties+0x48]`（= KeyPart8）；`shr rax,0x13`；
#      `and eax,0x3f`；`× 0.015873`（= 1/63）→ 得到 h = bits/63 ∈ [0,1]。
#    · 核心（0x1804bbc19）：`h × 0.2 + 0.9`，再乘上「(race, 性别, 年龄段) 的基础缩放」。
#    ⇒ **AgentScale = (0.9 + 0.2 × bits/63) × min_scale(该 skin)**；
#      成年男 min_scale = 1.07 → bits 0/32/63 ↔ AgentScale 0.963 / 1.070 / 1.177。
#      **bits 31.5 = 1.0×基准**，全量程只有 ±10% —— 倍数超出 [0.9, 1.1] 的会被钳到端点。
#    证据与复现命令：`Debug/offline/_step4_prereq.md`「落地实施」一节。
HEIGHT_BAND = 0.10
# 无名武将（1300+ 人，没有自建网格）：按 StringId 确定性落在自然带里 —— 同一个人怎么看都是
#   同一个身高（六代一致、重跑一致），但人与人之间有自然的高低差，不是一刀切一个值。
NPC_HEIGHT_BAND = (0.92, 1.08)

# ── 体重 / 体型（逐人默认值；2026-09-17）──────────────────────────────────────
# 🔴 **前提复核结论（与最初的假设不同，别再按旧假设办）**：
#   · `weight` / `build` **不在 128 位脸键里** —— 它们是 `BodyProperties` 上的两个**动态浮点**，
#     走 XML 的 `weight=` / `build=` 属性（`TaleWorlds.Core.BodyProperties`：3 个 float 动态段
#     (Age/Weight/Build) + 8 个 ulong 静态键；键里没有这两项的位）。
#   · 它们的实现 = **骨架骨缩放**（`skins.xml` 里那两个 deform_key 挂的是 `<bone_scales>`
#     → `biped_abdomen` / `biped_thorax` / 大腿 / 肩臂，逐轴给区间）。
#     **不是网格形变**：原版身体网格 `body_male_a` 的 `VertexKeyCount=0`（`tpaccli morphinfo` 实测），
#     根本没有 morph 帧可用。
#   · ⇒ **我们的甲与头会跟着变**（都蒙在同一副骨架上）→ 不是「穿模/空隙」，而是
#     「硬质甲片被拉宽拉厚」+「双重计入」（甲本来就是按本人身形做的）。
#     量级：weight 全程只动腹部厚度 ±12%、build 动胸腔宽度 ±22%；逐人偏置最多 ±0.10 → ±2.3%。
# 完整证据链 / 逐人核对表 / 量法：`Debug/offline/_bodyprops_table.md`（生成器 `_gen_bodyprops_table.py`）。
#
# 🔴 **已打开**（2026-09-17 用户裁定「都要，我只看最终最好的效果」）：28 个有名武将写逐人体型，
#   其余人（无名武将/兵种）写模板值。**风险已知并接受 A/B 复核**：甲/头会跟着骨缩放一起被拉伸，
#   而甲本来就是按本人身形做的 → **双重计入**（详见 `plans/逐角色共用变换与拼装闸门.md` §10.7）。
#   **一键关回去 = 改 False 后重跑本脚本**（六代一起回到模板值；产物字节随之复原）。
APPLY_BODY_SHAPE = True
BODY_SHAPE_GAIN = 0.5                 # 比值偏离中位数 10% → 滑条走 0.05
BODY_SHAPE_CLAMP = (0.80, 1.30)       # 量测有噪声；带外的换算成滑条没有意义（会饱和）
# 逐人体型量测（Blender 产出，见 `Debug/offline/_measure_src_body.py`）；**只在开关打开时才读**，
#   所以关着的时候本脚本对 `Debug/offline/` 零依赖（离线产物不进 git，不该卡住生成器）。
BODY_SHAPE_SRC = os.path.join(REPO, "Debug", "offline", "_src_body.json")

# ── 武将装备：**数据在 CSV，不在这里**（2026-09-16 用户裁定）──
# 🔴 读 `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/HeroEquip.csv`（源表，手维护）：
#    一行一个**身份**，**铠甲 / 头盔 / 武器各占一列**（另有女将专用的甲/盔两列）。
#    候选写竖线分隔的**兵种 slug**；甲与盔**各自独立挑**。
#    🔴 **女将专用列有值 = 覆盖**（「女将统一穿女甲」）：樱色女具足是照女性身形做的，
#       而那些具足是按男性身形做的 —— 女将穿会偏大。头盔留空 = 走通用池（笠/头巾不分男女）。
#    **改武将穿什么 = 改那张表再重跑本脚本。**
#    读取器：`Scripts/taikou_equip_tables.py`（列口径/多值分隔/自检都在那里）。
#
# 三条口径（表本身表达不了的、属于渲染规则，所以留在这里）：
#  ① **同一个武将固定穿池子里的一对**（按 id 挑，六代一致）→ 同身份档内长相各异，1300 多人不撞衫。
#  ② **不挂 Leg / Gloves**（2026-09-16 用户裁定）：「腿甲基本上被铠甲覆盖了」——
#     甲件本来就盖到脚踝、自带籠手，再挂原版靴子/铁手套反而穿帮。
#     腿 / 臂防护由甲自己的 `leg_armor` / `arm_armor` 提供，不吃亏。
#  ③ **不配马**：马要配 default_group=Cavalry，而 Cavalry 无马会不会炸没验证过 → 一律 Infantry（零风险）。
HERO_EQUIP, HERO_EQUIP_DEFAULT = EQ.hero_equip()


def pick_armor(row, hero_id, female):
    """身份行 + 武将 id + 性别 → (甲 id, 兜 id)。**按 id 定，不看年代**（同一个人六代穿同一套）。

    甲与盔**各自独立挑**（表里是两列独立候选池，见 `HeroEquip.csv`）——
    所以「侍的具足配阵笠」这种组合也会出现，比原来「甲兜成对」的组合数多得多。
    女将走「女将专用甲」池（覆盖语义），头盔仍走通用池。
    """
    return EQ.pick_hero_equip(row, hero_id, female)


VOICE_BY_IDENTITY_FEMALE = "calm"
VOICE_DEFAULT = "curt"

# 🔴 测试开关（2026-09-15 用户要求）：把专属甲/盔/武器也写进**平民装**那一档，
#    好在城里直接看到这些自制外观（否则只有战斗装穿、进城换便服就看不见）。
#    恢复正常表现 = 改 False 后重跑本脚本（产物唯一真源仍是 TaikouHero.csv）。
MIRROR_DEDICATED_TO_CIVILIAN = True

# 🔴 专用 race 的**代码表已删除**（2026-09-15）：映射搬进了 `TaikouHero.csv` 的「头」列（键 `Race`）。
#    搬移工具 = tools/sw2-pipeline/gen_hero_wiring_cols.py；生成器只读表，不再有第二处真源。


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


# ── 旗号几何：**固定模板，不再从借来的底旗继承**（2026-09-16 实机修）────────────────
# 背景 = banner_background_test_11（原版 is_base_background 那条矩形旗），图标居中铺满。
# 🔴 为什么必须固定：原来照抄借来的底旗几何 → 全局出现 **55 种不同的图标位置/尺寸**
#    （原版每个键都是为自己那个图标调的），实机症状 = 「有的城池家纹巨大、织田的却小到看不见」。
#    极端例：某键背景画在 (4922,4922) 而图标在 (471,471)，两者相距极远，
#    名牌又是 MaskedTextureWidget（按旗形裁剪）→ 图标落到旗形外被裁掉。
#    这组数字是本包**原来自证可用**的那套（实机里长篠城的风车就是这么渲染出来的）。
CANON_BG_GEOM = ["1536", "1536", "764", "764", "1", "0", "0"]
CANON_ICON_GEOM = ["483", "483", "764", "764", "0", "0", "0"]
BG_MESH = "11"

# 图标配色：按底色亮度二选一（都取自原版调色板，见 banner_icons.xml 的 BannerColors）
ICON_COLOR_ON_DARK = "171"     # ffFFB53E 金——深底上醒目
ICON_COLOR_ON_LIGHT = "116"    # ff0B0C11 近黑——浅底上醒目
_PALETTE = {}                  # {id: "0xRRGGBB"}，main() 里从官方 banner_icons.xml 读


def load_palette(official_root):
    """读官方 banner_icons.xml 的 <BannerColors> → {id: hex}（选图标色要算亮度）。"""
    p = os.path.join(official_root, "Modules", "Native", "ModuleData", "banner_icons.xml")
    if not os.path.isfile(p):
        return {}
    txt = io.open(p, encoding="utf-8", errors="replace").read()
    out = {}
    for m in re.finditer(r'<Color\s+id="(\d+)"\s+hex="0x([0-9a-fA-F]+)"', txt):
        out[int(m.group(1))] = m.group(2)
    return out


def _bg_luminance(color_id):
    """底色亮度。⚠️ 调色板 hex 是 `0xAARRGGBB` —— 必须跳过前两位 alpha，
    否则把 alpha(ff) 当成 R 读，亮度恒 ≈255，全被误判成「浅底」（实测踩过）。"""
    h = _PALETTE.get(color_id)
    if not h or len(h) < 8:
        return 128.0                       # 查不到就当中等亮度 → 走金色
    return 0.299 * int(h[2:4], 16) + 0.587 * int(h[4:6], 16) + 0.114 * int(h[6:8], 16)


def apply_mon(base_key, mon_key):
    """底旗（只取它的**配色**）+ 家纹键 → 最终 banner_key。

    banner_key 定长串：`背景10段.图标id.图标9段`。
      · mon_key 有效 → 换成本包家纹图标 id；几何一律用 CANON_*（不再继承底旗的）
      · mon_key 为空 → 只留背景 = **纯色旗**（不配家纹的家族，也别留官方图标）
    底旗只贡献**底色两个色号**（保证各家族颜色有变化），几何全部固定。
    """
    p = base_key.split(".")
    c1 = p[1] if len(p) > 2 else "163"
    c2 = p[2] if len(p) > 2 else "166"
    head = [BG_MESH, c1, c2] + CANON_BG_GEOM
    icon_id = MON.mon_key_to_icon_id(mon_key)
    if icon_id is None:
        return ".".join(head)
    ic = ICON_COLOR_ON_DARK if _bg_luminance(int(c1)) < 140 else ICON_COLOR_ON_LIGHT
    return ".".join(head + [str(icon_id), ic, ic] + CANON_ICON_GEOM)


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


# ── 身高：脸键拼装（第 4 步，见文件顶部的说明块）────────────────────────────
_HEIGHT_CTX = None          # 懒加载：{"base": (age, weight, build, key), "ratio": {StringId: ratio}}
_HEIGHT_SEEN = []           # [(StringId, "sw2"/"npc", ratio, bits)]，六代累加，生成后打印分布
_BODY_SHAPE_CTX = None      # 懒加载：{StringId: 逐人体型量测行}（只在 APPLY_BODY_SHAPE 打开时读）
_BODY_SHAPE_BASE = None     # (模板 weight, 模板 build)，由 _height_ctx 设好


def _height_ctx(md):
    """读基准键 + 28 个有名武将的 height_ratio（都只读一次）。读不到 = 硬错，不静默跳过。"""
    global _HEIGHT_CTX, _BODY_SHAPE_BASE
    if _HEIGHT_CTX is not None:
        return _HEIGHT_CTX
    bp_path = os.path.join(md, "taikou_bodyproperties.xml")
    if not os.path.isfile(bp_path):
        print("[FATAL] 找不到基准身体属性文件：%s" % bp_path, file=sys.stderr)
        raise SystemExit(2)
    root = ET.parse(bp_path).getroot()
    base = None
    for bp in root.findall("BodyProperty"):
        if bp.get("id") != BASE_BODY_PROPERTY:
            continue
        mn = bp.find("BodyPropertiesMin")
        base = (mn.get("age"), mn.get("weight"), mn.get("build"), mn.get("key"))
    if not base or not base[3] or len(base[3]) != 128:
        print("[FATAL] %s 里读不到 %s 的 min 键（128 位十六进制）"
              % (bp_path, BASE_BODY_PROPERTY), file=sys.stderr)
        raise SystemExit(2)
    # 28 个有名武将：SW2 挑件表（StringId 口径的唯一真源）→ srcT.json 的 height_ratio
    sys.path.insert(0, os.path.join(REPO, "tools", "sw2-pipeline"))
    from parts_table import TABLE as SW2_PARTS        # noqa: E402
    if not os.path.isfile(SW2_SRC_T):
        print("[FATAL] 找不到网格管线产出：%s（先跑 tools/sw2-pipeline 的变换导出）" % SW2_SRC_T,
              file=sys.stderr)
        raise SystemExit(2)
    src = json.load(io.open(SW2_SRC_T, encoding="utf-8"))["chars"]
    # srcT.json 里还有兵种条目（`L250_SOLDIER1` 之类，挑件表里没有）——本脚本只用武将，
    # 挑件表里没有的条目直接忽略；**挑件表里有的（= 我们要用的 28 人）必须有 ratio**，
    # 缺了就是网格管线还没算完 → 硬错，不许静默回落到随机值。
    ratio, pending = {}, []
    for key, row in src.items():
        if key not in SW2_PARTS:
            continue
        sid = (SW2_PARTS[key] or {}).get("taikou")
        hr = row.get("height_ratio")
        if not sid or hr is None:
            pending.append(key)
            continue
        ratio[sid] = float(hr)
    if pending:
        print("[FATAL] 网格管线还没给这些武将 height_ratio：%s（等 srcT.json 算完再重跑）"
              % ", ".join(sorted(pending)), file=sys.stderr)
        raise SystemExit(2)
    _HEIGHT_CTX = {"base": base, "ratio": ratio}
    _BODY_SHAPE_BASE = (float(base[1]), float(base[2]))   # 模板的 weight / build（逐人换算的基准）
    return _HEIGHT_CTX


def height_bits(ratio):
    """身高倍数 → 6 bit（0..63）。bits 31/32 = 原版等高基准，±HEIGHT_BAND 打满 0 / 63。"""
    bits = int(round(31.5 + (ratio - 1.0) * (31.5 / HEIGHT_BAND)))
    return max(0, min(63, bits))


def set_height_bits(key, bits):
    """键串（128 个十六进制字符）→ 只换 KeyPart8 的 bit 19..24，其余一字不动。"""
    kp8 = int(key[112:128], 16)
    kp8 = (kp8 & ~(0x3F << 19)) | (bits << 19)
    return key[:112] + ("%016X" % kp8)


def npc_height_ratio(sid):
    """无名武将：StringId → 确定性身高倍数（两次抽样取均值 = 中间多两端少）。"""
    lo, hi = NPC_HEIGHT_BAND
    h = hashlib.sha256(sid.encode("utf-8")).digest()
    u1 = int.from_bytes(h[0:4], "big") / 0xFFFFFFFF
    u2 = int.from_bytes(h[4:8], "big") / 0xFFFFFFFF
    return lo + (hi - lo) * (u1 + u2) / 2.0


def height_summary():
    """打印身高地图（验收用）：唯一人数 / 有名 / 无名 / bits 分布；并守六代一致。"""
    uniq = {}
    for sid, src, ratio, bits in _HEIGHT_SEEN:
        prev = uniq.get(sid)
        if prev and prev != (src, ratio, bits):
            print("[FATAL] %s 六代身高不一致：%s vs %s" % (sid, prev, (src, ratio, bits)),
                  file=sys.stderr)
            raise SystemExit(2)
        uniq[sid] = (src, ratio, bits)
    sw2 = sorted([(s, v) for s, v in uniq.items() if v[0] == "sw2"], key=lambda x: -x[1][1])
    npc = [v for v in uniq.values() if v[0] == "npc"]
    hist = collections.Counter(v[2] for v in uniq.values())
    print("── 身高（脸键 KeyPart8 位 19..24；bits 31/32 = 原版等高基准，带 1±%.2f）──" % HEIGHT_BAND)
    for sid, (_s, ratio, bits) in sw2:
        print("   有名 %-16s ×%.4f → bits %2d" % (sid, ratio, bits))
    if npc:
        rs = sorted(v[1] for v in npc)
        print("   无名 %d 人：×%.3f~%.3f（中位 ×%.3f）→ bits %d~%d"
              % (len(npc), rs[0], rs[-1], rs[len(rs) // 2],
                 min(v[2] for v in npc), max(v[2] for v in npc)))
    print("   合计 %d 人 · bits 分布 %s" % (len(uniq), dict(sorted(hist.items()))))


def body_shape(sid, md):
    """逐人 (weight, build)。**开关关着 = 返回 None**（调用方回落模板值 → 产物字节不变）。

    数据源 = `Debug/offline/_src_body.json`（Blender 量的逐人「腰腹厚比 / 胸背宽比」，口径与
    量法见 `Debug/offline/_measure_src_body.py` 与 `_bodyprops_table.md` §七）。
    映射：`模板值 + GAIN × (夹到 CLAMP 的比值 − 1)`；有名 28 人以外（无名武将/兵种）一律 None。
    """
    if not APPLY_BODY_SHAPE:
        return None
    _height_ctx(md)                # 顺带把模板的 weight/build 基准设好（幂等，只读一次）
    global _BODY_SHAPE_CTX
    if _BODY_SHAPE_CTX is None:
        if not os.path.isfile(BODY_SHAPE_SRC):
            print("[FATAL] APPLY_BODY_SHAPE 已打开但读不到逐人体型量测：%s\n"
                  "        （先跑 Debug/offline/_measure_src_body.py 产出它）" % BODY_SHAPE_SRC,
                  file=sys.stderr)
            raise SystemExit(2)
        sys.path.insert(0, os.path.join(REPO, "tools", "sw2-pipeline"))
        from parts_table import TABLE as SW2_PARTS        # noqa: E402
        raw = json.load(io.open(BODY_SHAPE_SRC, encoding="utf-8"))["chars"]
        by_key = {}
        for k, v in SW2_PARTS.items():
            tid = (v or {}).get("taikou")
            if tid and k in raw:
                by_key[tid] = raw[k]
        _BODY_SHAPE_CTX = by_key
    row = _BODY_SHAPE_CTX.get(sid)
    if not row:
        return None

    def to_slider(ratio, base):
        lo, hi = BODY_SHAPE_CLAMP
        r = max(lo, min(hi, float(ratio)))
        return "%.4f" % max(0.0, min(1.0, float(base) + BODY_SHAPE_GAIN * (r - 1.0)))

    # 基准值由 `_height_ctx` 一并设好（上面已调过）
    base_w, base_b = _BODY_SHAPE_BASE
    wr = (row.get("waist") or {}).get("torso_d_robust_ratio")
    br = (row.get("chest") or {}).get("torso_w_robust_ratio")
    return (to_slider(wr, base_w) if wr else None,
            to_slider(br, base_b) if br else None)


def body_props_block(w, r, md, seen):
    """领主的 <face> 块：内联 <BodyProperties>（英雄取 min → 这条就是他的身高 + 体重/体型）。"""
    ctx = _height_ctx(md)
    age_b, weight, build, base_key = ctx["base"]
    sid = r["ID"]
    ratio = ctx["ratio"].get(sid)
    if ratio is None:
        if (r.get("Race") or "").strip():
            print("[FATAL] %s 有专属头模（Race）却没有 height_ratio —— 网格管线漏了他？" % sid,
                  file=sys.stderr)
            raise SystemExit(2)
        ratio = npc_height_ratio(sid)
        src = "npc"
    else:
        src = "sw2"
    seen.append((sid, src, ratio, height_bits(ratio)))
    key = set_height_bits(base_key, height_bits(ratio))
    shape = body_shape(sid, md)                  # 默认 None = 保持模板值（现状）
    if shape and shape[0] and shape[1]:
        weight, build = shape
    return ('\t\t<face>\n'
            '\t\t\t<BodyProperties version="4" age="%s" weight="%s" build="%s" key="%s" />\n'
            '\t\t</face>\n' % (age_b, weight, build, key))


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


def write_lords(w, md):
    L = [HEADER % w.era, "<NPCCharacters>\n"]
    for r in sorted(w.heroes, key=lambda x: x["ID"]):
        ident = (r.get("Identity_" + w.era) or "").strip()
        if ident in ("", "无效"):
            ident = ""                                  # 无身份（女性/推定在场那批）→ 用默认档
        row = HERO_EQUIP.get(ident) or HERO_EQUIP_DEFAULT     # 身份没登记 → 表的「none」行
        weapons = list(row["Weapons"])
        fem = w.is_female(r)
        # 甲 / 兜：按身份行 + 武将 id 从候选池挑一对（六代一致）→ 同档内长相各异。
        body_id, head_id = pick_armor(row, r["ID"], fem)
        armor = dict(Body=body_id, Head=head_id)
        # 🔴 **「专属武将」的识别 = 「头」列有值**（键 `Race` —— 战无2 那 28 个专属头模；
        #    2026-09-17 用户裁定：这一批人一律看 `Race` 认，不再一处一个判法）。
        #    ⚠️ 与「武器」列今天正好是同一批 28 人（实测 Race 28 行 = Weapon 28 行、无单边），
        #    但**判据以 Race 为准** ——「有没有专属头模」才是这批人的身份，「有没有专属武器」不是。
        #    这一个判据统管两件事：① 不戴档位兜（本段下面）② 身上只带自己那套（武器段）。
        race_id = (r.get("Race") or "").strip()
        # 专属甲（2026-09-15 用户裁定）：来源 = `TaikouHero.csv` 的「甲」列（键 `Armor`）——
        #    哪位武将穿哪件甲写**数据**里，不写代码表（改人只改表）。
        #    ① 只覆盖**战斗装**的 Body 槽，民用装（进城便服）不动；
        #    ② 甲是**普通物品**（`taikou_items/body_armors.xml` 里定义的），这里只决定他出场穿什么，
        #       之后可以被扒 / 被偷 / 当战利品拿走 —— 这是用户明确要的效果。
        body_armor = (r.get("Armor") or "").strip()
        if body_armor:
            armor = dict(armor, Body=body_armor)
        # 专属头盔（「头盔」列）→ Head 槽。同甲：普通物品，只是出场戴着，可摘可偷。
        helmet = (r.get("Helmet") or "").strip()
        # 🔴 **有专属头模的人不戴档位兜**（2026-09-16 踩到）：战无2 那 28 个专属 race 的头模里
        #    **已经长着头饰了** —— 其中 19 人的兜与头部连体、根本切不出独立网格（所以「头盔」列是空的，
        #    见 `parts_table` 与 item 的「占位·无真实模型」标记）。给这 19 人再戴一顶档位兜
        #    = **头上套两层**（实机没验也知道穿帮）。有专属头模 + 有专属兜的那 9 人才该戴。
        if race_id and not helmet:
            armor = {k: v for k, v in armor.items() if k != "Head"}
        elif helmet:
            armor = dict(armor, Head=helmet)
        # 🔴 专属武器（2026-09-15；2026-09-17 收紧）：来源 = `TaikouHero.csv` 的「武器」列。
        #    ① **专属武将身上只带这一套** —— 主武器 +（远程则带）弹药，**不从身份档表补任何东西**
        #       （2026-09-17 用户裁定：「不要擅自派发盾牌；如果有装备那么身上就只带那装备」。
        #        原来这里把身份档的其余武器顺进 Item1 —— 大名/国主/城主/家老/部将 5 档写着
        #        `刀|leather_round_shield`，于是这 28 人平白多出一面盾，且是欧洲圆盾）；
        #    ② 远程武器（弓/铁炮）**必须带弹药**，否则拿着射不出去 —— 弹药 id 在「弹药」列，
        #       插在 Item1/Item2（原版弓手布局就是 Item0=弓 Item1=箭）。弹药算他这套的一部分，
        #       不是"补的东西"；顺带也就不会再出现「带盾拿枪射不出去」（`requires_no_shield`）；
        #    ③ 同甲：普通物品，出场只决定初始装备，可被扒/被偷/作战利品。
        weapon = (r.get("Weapon") or "").strip()
        ammo = (r.get("Ammo") or "").strip()
        dedicated = bool(race_id and weapon)
        if dedicated:
            weapons = [weapon] + ([ammo, ammo] if ammo else [])
        elif race_id:
            print("[WARN] %s 有专属头模却没有专属武器（「武器」列空）→ 仍按身份档表拿武器"
                  % r["ID"], file=sys.stderr)
        elif weapon:
            print("[WARN] %s 填了专属武器却没有专属头模（「头」列空）→ 「武器」列被忽略，"
                  "按身份档表拿武器" % r["ID"], file=sys.stderr)
        cul = (r.get("CultureID") or "").strip() or CULTURE_FALLBACK
        # 🔴 专属 race 的来源 = `TaikouHero.csv` 的「头」列（键 `Race`）—— 2026-09-15 用户裁定：
        #    "谁长什么脸"是**数据**，不写死在生成器里（与「甲」列同口径）。
        #    （`race_id` 上面已经读过：它既是「戴不戴档位兜」的判据，也是「只带自己那套」的判据。）
        race_attr = ' race="%s"' % race_id if race_id else ""
        L.append('\t<NPCCharacter id="%s" default_group="Infantry" age="%d" voice="%s" '
                 'is_hero="true" is_female="%s" culture="Culture.%s"%s name="{=%s}%s" occupation="Lord" '
                 'banner_symbol_mesh_name="test_symbol_a" banner_symbol_color="FF000000">\n'
                 % (r["ID"], w.age_of(r), VOICE_BY_IDENTITY_FEMALE if fem else VOICE_DEFAULT,
                    "true" if fem else "false", cul, race_attr, name_key(r["ID"], w.era), esc(en_name_of(r))))
        L.append(body_props_block(w, r, md, _HEIGHT_SEEN))
        L.append('\t\t<Equipments>\n\t\t\t<EquipmentRoster>\n')
        for i, it in enumerate(weapons):
            L.append('\t\t\t\t<equipment slot="Item%d" id="Item.%s" />\n' % (i, it))
        for slot, it in sorted(armor.items()):
            L.append('\t\t\t\t<equipment slot="%s" id="Item.%s" />\n' % (slot, it))
        L.append("\t\t\t</EquipmentRoster>\n\t\t\t<EquipmentRoster civilian=\"true\">\n")
        # 🔴 民用装（2026-09-16 起）= **最终确定的 `armor`**（不是中途那份）——
        #    原来"城里穿便服"那套是**原版衣服**，与用户裁定「武将不穿原版甲」冲突。
        #    ⚠️ 必须用 `armor` 而非中途快照：专属甲/兜覆盖、以及「有专属头模就不戴兜」
        #       这两步都在后面做，用旧快照会让平民装**穿回**战斗装已经去掉的头盔。
        #    下面这个开关现在**只管武器**：把专属武器也写进平民装，好在城里直接看到自制外观。
        #    ⚠️ 恢复"城里只带随身武器"= 改 False 后重跑本脚本（产物唯一真源仍是 TaikouHero.csv）。
        civil_final = dict(armor)
        civil_weapons = []
        if MIRROR_DEDICATED_TO_CIVILIAN:
            if body_armor:
                civil_final["Body"] = body_armor
            if helmet:
                civil_final["Head"] = helmet
            if dedicated:
                civil_weapons = [weapon] + ([ammo, ammo] if ammo else [])
        for i, it in enumerate(civil_weapons):
            L.append('\t\t\t\t<equipment slot="Item%d" id="Item.%s" />\n' % (i, it))
        for slot, it in sorted(civil_final.items()):
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
# 🔴 家纹留空 = **纯色旗**（2026-09-16）：原值是官方图标 609（欧洲纹章），与本包家纹体系不搭；
#    主角反正要在建号捏旗界面自己选，而那里现在能选到本包 320 个家纹。
PLAYER_FACTION = ('\t<Faction id="player_faction" is_noble="true" owner="Hero.main_hero" '
                  'banner_key="11.154.116.1536.1536.768.768.1.0.0" '
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
        bkey = apply_mon(w.banners[i % len(w.banners)], c.get("Mon"))
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
                 '\t</Faction>\n' % (ronin_clan_id(w.era), ronin[0],
                                     # 浪人众不是家系 → 纯色旗（不配家纹，也别留官方图标）
                                     apply_mon(w.banners[5 % len(w.banners)], None),
                                     key, key, key))
    L.append("</Factions>\n")
    return "".join(L)


def write_kingdoms(w, path):
    L = [HEADER % w.era, "<Kingdoms>\n"]
    for i, k in enumerate(sorted(w.kingdoms, key=lambda x: x["ID"])):
        # 王国旗 = 该势力的家纹（大名的居城挂的是王国旗，见 Clan.Banner 的统治家族特例）
        bkey = apply_mon(w.banners[(i * 7 + 3) % len(w.banners)], k.get("Mon"))
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
    # 调色板：选图标配色要算底色亮度（apply_mon 用）
    _PALETTE.update(load_palette(official) if official else {})
    if not _PALETTE:
        print("[warn] 读不到官方调色板，图标配色一律走金色", file=sys.stderr)

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
                 (os.path.join(md, "taikou_lords%s.xml" % sfx), write_lords(w, md)),
                 (os.path.join(md, "spclans%s.xml" % sfx), write_clans(w, None)),
                 (os.path.join(md, "spkingdoms%s.xml" % sfx), write_kingdoms(w, None))]

    # 往返校验：写完必须能逐字读回（写的内容 = 校验的内容，同一个字符串）
    for path, text in plan:
        try:
            ET.fromstring(text.encode("utf-8"))
        except ET.ParseError as ex:
            print("[FATAL] 生成的 XML 解析不过：%s —— %s" % (os.path.basename(path), ex), file=sys.stderr)
            return 2

    # ── 身高地图（六代一并打印；顺带校验同一个人六代一致）──
    height_summary()

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

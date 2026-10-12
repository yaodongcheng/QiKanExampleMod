#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Module-registration checker (自定义世界「段注册 / 源文件登记」离线体检)
========================================================================
覆盖「必备清单」里这两组**只有文字、没有脚本**的条目：
  阶段 0「SubModule.xml 完整段清单」——缺段 = 每少一段崩一次（雷 3/4/5）
  1.6「SandBox 9 个 GameText 文本段全量拷贝」——被 GameType 白名单过滤 = 整文件不加载（雷 35）
  阶段 2「新增 .cs 必须登记 csproj」——漏登记 = 静默不编译，build 报 0 错是假象（雷 40）

五条规则：
  1. **必需段在位**：id 清单（Items/SPCultures/…/GameText）+ 9 个官方 GameText path，
     且必须在本包 GameType 下生效（无 IncludedGameTypes 或白名单含它）
  2. **段 path 可解析**：每个注册的 path 必须能落到实际文件/目录（写错路径 = 引擎加载失败或静默空）
  3. **孤儿数据文件**：ModuleData 下的 XML 既不被任何段覆盖、又不挂在 project.mbproj 上、
     又不在引擎惯例名单里、**且定义了对象** = 数据写了但运行时不存在（0 定义的空模板只提示）
  3b. **soln 体系文件必须挂 project.mbproj**：`item_usage_sets` / `item_holsters` / `module_sounds` /
     `action_sets` / `action_types` / `skins` / **`particle_systems*`（2026-09-23 加，官方数据核过）**
     只有 mbproj 一条加载路径，文件在而没挂 = 完全不加载
     （雷 122 的 AV 崩 / 雷 136 的枪管朝天 / 音效静音 / 粒子查不到）
  4. **csproj 漏登记**：`ExampleModVS/**/*.cs` 与 `<Compile Include>` 清单比对，未登记 = ERROR

Usage:
  python Scripts/check_module_registration.py [--module PATH] [--repo PATH] [--game-type NAME]
Exit: 0 无问题 / 1 有 ERROR / 2 fatal。
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

# ① 必需段 id（缺 = 每次崩一个，雷 3/4/5）——新内容包照抄 Taikou 的 SubModule.xml 即可全中
REQUIRED_SECTION_IDS = [
    "Items", "SPCultures", "NPCCharacters", "partyTemplates", "Kingdoms", "Factions",
    "WorkshopTypes", "Settlements", "BodyProperties", "SkillSets", "EquipmentRosters",
    "Concepts", "CraftingPieces", "Heroes", "LocationComplexTemplates",
    "MusicInstruments", "MusicTracks", "GameText",
]
# ② 必需 GameText path（SandBox 9 段全被 GameType 白名单过滤 → 自定义 GameType 下必须自备，雷 35）
REQUIRED_GAMETEXT_PATHS = [
    "module_strings", "world_lore_strings", "companion_strings", "wanderer_strings",
    "comment_strings", "comment_on_action_strings", "trait_strings", "voice_strings", "action_strings",
]
# ③ 引擎按**惯例文件名**加载、不经 XmlNode 段注册的文件（官方模块同款；实测 Native/SandBox 里存在）
CONVENTIONAL_FILES = {
    "action_sets", "action_types", "collision_infos", "combat_parameters", "face_animations",
    "module_sounds", "native_parameters", "physics_materials",
    "skins",
}
# 🔴 2026-10-12 更正（反编译实证，别再被旧注释误导）：**引擎没有"惯例文件名加载"这条路**。
#    全引擎只有**两个**加载列表，写入方各一个、读取方各一个：
#      · `XmlResource.GetXmlListAndApply`（读各模块 `SubModule.xml` 的 `<Xmls><XmlNode>`）→ `XmlInformationList`
#        → 供 `GetMergedXmlForManaged`（托管类型：Items / NPCCharacters / …）
#      · `XmlResource.GetMbprojxmls`（读 `ModuleData/project.mbproj` 的 `<file>` 行）→ `MbprojXmls`
#        → 供 `GetMergedXmlForNative`（native 类型：action_sets / skins / item_usage_sets / …）
#    **没有第三处**：全 bin 反编译搜 `XmlInformationList` / `MbprojXmls`，除上面两写两读之外零命中。
#    ⇒ 上面这张表里剩下的名字（`skins` 等）**并不是"惯例加载"**，而是"官方模块里存在、且通常已由
#      mbproj 挂上"（如 Native 的 `soln_skins`）。它们留在这张表里只为**不误报官方的空模板文件**；
#      ⚠️ 反过来说：**任何模块里一份没被声明、又有定义的 XML 都真的不会被加载** —— 别指望惯例。
#    本次据此删掉了旧的 `"items"` 项：`ModuleData/items.xml` 是模板自带的**空占位**（`<Items/>`，
#    官方注释写着 "You can add nodes to this xml"），不是被引擎惯例吃掉的；照旧留着它会让
#    "items 目录/文件没声明也不报" 变成一条**假的安全感** —— 而法术总闸正是靠"没声明 = 不加载"成立的。
# ④ 🔴 **soln 体系文件**：不经 SubModule.xml 的 XmlNode，而是挂在 `ModuleData/project.mbproj` 的
#    `<file id="soln_xxx" name="…" type="…"/>` 行上，引擎 `GetMergedXmlForNative` 合并后才交给 native。
#    **文件在磁盘上 ≠ 被加载** —— 不挂 mbproj 行 = 完全不加载（静默），后果看文件而定：
#      · `item_usage_sets` 不挂 → 物品的 item_usage 指向不存在的 usage → native 取无效索引 → AV 崩（雷 122）
#      · `item_holsters` 不挂 → 换皮武器继续用旧槽 → 枪管朝天（雷 136）
#      · `module_sounds` 不挂 → `SoundEvent.GetEventIdFromString` 全查不到 → 该档静音
#    （`action_sets` / `action_types` / `skins` 同理；Taikou 的 project.mbproj 里逐条写着来龙去脉。）
#    ⇒ 本检查器第 3b 段专门守这个：**文件在、mbproj 没挂 = ERROR**，见 SOLN_ONLY_FILES。
# 🔴 2026-09-21 更正旧记录：此前这里写着「`item_usage_sets` / `monster_usage_sets` 只由 Native 加载，
#    内容包注册不了，写了就是死文件」—— **那条是错的**（当时没发现 mbproj 这条路），
#    它把**正常运行的文件**永久报成孤儿，也把"我没找到路"写成了"没有路"。
#    实机反证：Taikou 挂了 `soln_item_usage_sets` 之后，铁炮的自定义 usage `tk_firearm`
#    与自定义动作 `act_gun_reload` 都正常工作（2026-09-19 起）。
#    ⚠️ 但**同名 id 会跨模块合并**：内容包写 item_usage_sets 时只能加**新 id**，
#       照抄一份 Native 的 id = 合并出重复定义 = KeyNotFoundException（2026-09-08 实机崩过）。
# Languages/ 由语言系统按清单加载，不走段注册
# AssetRegistry/ = **运行期自读目录**（不经 MBObjectManager）：立绘表 ProfileStages.csv、
#   ProfileEmotion.csv，以及选人详情页的画像表 HeroProfiles.xml 都放这里——
#   这些文件由 C# 直接读盘（PortraitRegistry / HeroProfileRegistry），
#   **故意不注册**（注册了反而要求配套一个 MBObjectManager 类，徒增负担）。
#   见 plans/选人流程复刻太阁5-设计.md §五·补。
CONVENTIONAL_DIRS = {"Languages", "AssetRegistry"}

# 见 ④：这些文件**只有** mbproj 一条加载路径（没有任何"惯例文件名"回退），
# 所以"文件在、mbproj 没挂"是确定的 ERROR，不是猜测。
SOLN_ONLY_FILES = {"item_usage_sets", "item_holsters", "module_sounds", "action_sets", "action_types", "skins"}

# 🔴 2026-09-23 新增：**粒子系统同属 soln 体系**（拿官方数据核过，不是推断）——
#   Native 的 project.mbproj 挂了 7 行 `particle_systems_*.xml` + `gpu_particle_systems.xml`，
#   同目录下只有 `particle_systems2.xml` / `particle_systems_old.xml` 没挂，而那两个确实是**死档**
#   （Knowledge/骑砍2粒子系统.md §1.6 实测：141 / 144 个 effect 从不加载）。
#   ⇒「文件在、mbproj 没挂 = 完全不加载」对粒子同样成立。
#   ⚠️ 粒子文件是**带前缀的一族名字**（`particle_systems_<名>.xml`），所以按**前缀**匹配、不按整名 ——
#      按整名只认一个字面文件名，`particle_systems_yinmo.xml` 这类就漏网了（点名 = 漏网的老教训）。
SOLN_ONLY_PREFIXES = ("particle_systems", "gpu_particle_systems")

# ⑤ 🔴 **故意不被引擎加载**的数据文件（2026-10-12 立）—— 它们的加载权**已经交给 C#**，
#    所以"没有任何段声明它"是**对的**，不是孤儿。
#    ⚠️ 进这张表的**前提**：真有一处 C# 会在运行时按需加载它（表里的说明必须点名那处代码）。
#       这不是"眼不见为净"的收容所 —— 只是想消掉一条告警，就该回去把文件接回段里。
#    ⚠️ 配套**反向断言**（本文件第 3 段末尾）：表里哪个文件**又被某个段声明了** = 报错，
#       因为那说明它会被引擎**无条件**加载 ⇒ 开关失效（这份表的存在意义就是"关得掉"）。
INTENTIONALLY_UNLOADED = {
    "items/spells":
        "法术体系未发布（config.json SpellSystemEnabled 默认 false）：SubModule.xml 故意不声明它，"
        "改由 ExampleModVS/ExampleMod/ExampleMod/Combat/SpellSystemGate.cs 在开关打开时按需注册"
        "（引擎的 GetFiles(\"*.xml\") 会把整目录无条件吃掉，所以只能逐文件声明）",
}

# ⑤b 🔴 **正向断言要盯的目录**（2026-10-12 立）：这些目录**原本是整目录声明**，后来被改成
#     「逐文件声明」—— 改动带来的**唯一代价**就是：往目录里加新文件**不再自动加载**（静默）。
#     引擎为什么非改不可见 `INTENTIONALLY_UNLOADED` 的说明；代价必须由脚本兜住，不能靠人记。
#     判据 = 目录下每个 `*.xml` 要么被某个段覆盖、要么在 `INTENTIONALLY_UNLOADED` 里，否则 ERROR。
PER_FILE_DECLARED_DIRS = {
    "items": "SubModule.xml 把 items/ 改成了逐文件声明（为了让 items/spells 能被开关关掉）——"
             "新加文件必须回 SubModule.xml 补一行，或进 INTENTIONALLY_UNLOADED",
}


def report_undeclared_in_per_file_dirs(data_dir, covered, errors):
    """⑤b 正向断言：逐文件声明的目录里，不许有"既没被声明、也不在白名单"的文件。

    返回漏声明条数（0 = 干净）。命中即 ERROR —— 那种文件的症状是"文件在磁盘上、游戏里没有"。"""
    missing = 0
    for sub, why in sorted(PER_FILE_DECLARED_DIRS.items()):
        d = data_dir / sub
        if not d.is_dir():
            continue
        for f in sorted(d.rglob("*.xml")):
            stem = f.relative_to(data_dir).as_posix()[:-4]
            if stem in covered or stem in INTENTIONALLY_UNLOADED:
                continue
            missing += 1
            errors.append(f"{stem} 落在逐文件声明的目录里却没被声明")
            print(f"  [ERROR] {stem} —— **既没被任何段声明、也不在故意不加载的白名单里** ⇒ "
                  f"引擎不会加载它（文件在磁盘上、游戏里没有）")
            print(f"          来历：{why}")
    return missing


def is_soln_only(stem):
    """这个文件名是否属于「只有 project.mbproj 一条加载路径」的 soln 体系。"""
    return stem in SOLN_ONLY_FILES or stem.startswith(SOLN_ONLY_PREFIXES)


def section_covered(sections, data_dir):
    """段声明**覆盖到**的文件集合（目录声明会展开成目录下每个 xml）。

    键 = 相对 `ModuleData` 的 posix 路径（**去掉 `.xml` 后缀**，与段 path 的写法对齐）。
    第 3 段（孤儿）与第 3c 段（反向断言）共用这一份，避免两处各算一遍算出分歧。"""
    covered = set()
    for _sid, path, _g, _act in sections:
        covered.add(path)
        d = data_dir / path
        if d.is_dir():
            for f in d.rglob("*.xml"):
                covered.add(f.relative_to(data_dir).as_posix()[:-4])
    return covered


def report_unloaded_not_declared(covered, errors):
    """⑤ 反向断言：`INTENTIONALLY_UNLOADED` 里的文件**不许**被任何段声明。

    返回命中条数（0 = 全还关着）。命中即 ERROR —— 见那张表上方的说明。"""
    regained = 0
    for stem, why in sorted(INTENTIONALLY_UNLOADED.items()):
        if stem in covered or stem.split("/")[0] in covered:
            regained += 1
            errors.append(f"{stem} 又被段声明了（总闸失效）")
            print(f"  [ERROR] {stem} —— **它被某个段声明了** ⇒ 引擎会无条件加载它，"
                  f"开关再也关不掉")
            print(f"          来历：{why}")
    return regained


def read_mbproj(data_dir):
    """读 `ModuleData/project.mbproj`，返回**未被注释掉**的 `<file name="…"/>` 路径集合。

    返回相对 **ModuleData** 的 posix 路径（mbproj 的 name 相对**模块根**，多数以 `ModuleData/`
    开头，这里统一剥掉）。
    🔴 **注释必须先剥**——Taikou 的 mbproj 里大段注释举着 `<file …/>` 的例子（说明"Native 自己
    就是这么挂的"），不剥会把注释里的示例当成真挂载。"""
    out = set()
    p = data_dir / "project.mbproj"
    if not p.is_file():
        return out
    txt = p.read_text(encoding="utf-8-sig", errors="replace")
    txt = re.sub(r"<!--.*?-->", "", txt, flags=re.S)
    for m in re.finditer(r'<file\b[^>]*\bname="([^"]+)"', txt):
        name = m.group(1).replace("\\", "/").lstrip("/")
        if name.startswith("ModuleData/"):
            name = name[len("ModuleData/"):]
        out.add(name)
    return out


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
    ap = argparse.ArgumentParser(description="Module registration checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None)
    ap.add_argument("--game-type", default=None)
    ap.add_argument("--repo", default=None, help="仓库根（缺省=脚本上一级）——用于 csproj 检查")
    ap.add_argument("--unloaded-only", action="store_true",
                    help="🔴 只跑「⑤ 故意不加载的文件不许被段声明」那一条（2026-10-12）——"
                         "给**基座模块**（LivingWorldNpcs）用：本脚本其余规则查的是"
                         "「内容包必须自备的整个世界段清单」，基座本来就没有那些段，跑全量必然一片红；"
                         "但「开关关掉的数据不许被引擎无条件加载」这条对基座**同样成立**，"
                         "所以单开一个模式让一键体检能把这条也挂在基座上。")
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    exit_codes = []
    for game_type in inferred_game_types(mod_path, args.game_type):
        print(f"Module   : {mod_path}")
        print(f"GameType : {game_type}\n")

        sm = mod_path / "SubModule.xml"
        if not sm.is_file():
            print(f"[FATAL] SubModule.xml not found: {sm}")
            return 2
        try:
            root = ET.parse(str(sm)).getroot()
        except Exception as e:
            print(f"[FATAL] SubModule.xml 解析失败: {e}")
            return 2

        sections = []   # (id, path, [gametypes] or None, 是否对本 GameType 生效)
        for node in root.iter("XmlNode"):
            name = node.find("XmlName")
            if name is None or not name.get("path"):
                continue
            gt = node.find("IncludedGameTypes")
            gts = [g.get("value") for g in gt.iter("GameType")] if gt is not None else None
            active = gts is None or game_type in gts
            sections.append((name.get("id"), name.get("path"), gts, active))

        errors, warns = [], []
        data = mod_path / "ModuleData"

        # 🔴 `--unloaded-only`（2026-10-12）：**基座模块专用** —— 只查 ⑤ 那一条，其余全跳过。
        #    为什么单开一个模式：本脚本其余规则查的是「内容包必须自备的整个世界段清单」
        #    （SPCultures / Kingdoms / GameText×9 …），而基座（LivingWorldNpcs）本来就不该有那些段，
        #    跑全量必然一片红 —— run_all_checks.py 正是因为这个才把它排除在第二遍之外。
        #    但「开关关掉的数据不许被引擎无条件加载」这条对基座**同样成立**（法术物品就在基座里），
        #    ⇒ 留一个能挂进一键体检的窄入口（用法见 run_all_checks.py 的 SECOND_MODULE_CHECKS）。
        if args.unloaded_only:
            print("== ⑤ 故意不加载的文件不许被段声明（被声明 = 总闸失效） ==")
            _covered = section_covered(sections, data)
            regained = report_unloaded_not_declared(_covered, errors)
            if not regained:
                print(f"  （{len(INTENTIONALLY_UNLOADED)} 个文件都还关着 ✓）")
            else:
                print(f"\n  修：把那几行从 SubModule.xml 的 <Xmls> 里删掉 —— "
                      f"它们该由 C# 在开关打开时按需注册，不能被引擎无条件加载")
            print("\n== ⑤b 逐文件声明的目录里不许有漏网文件（漏了 = 文件在、游戏里没有） ==")
            undeclared = report_undeclared_in_per_file_dirs(data, _covered, errors)
            if not undeclared:
                print(f"  （{len(PER_FILE_DECLARED_DIRS)} 个目录下的文件都已交代 ✓）")
            else:
                print("\n  修：在 SubModule.xml 的 <Xmls> 里加一行 "
                      "<XmlNode><XmlName id=\"…\" path=\"…\" /></XmlNode>")
            print(f"\nSummary: sections={len(sections)} errors={len(errors)} warnings={len(warns)}")
            return 1 if errors else 0

        # ── 1. 必需段在位且对本 GameType 生效 ──
        print("== 必需段注册（缺 = 每次崩一个，雷 3/4/5；9 个文本段 = 雷 35） ==")
        have = {sid for sid, _p, _g, act in sections if act}
        for sid in REQUIRED_SECTION_IDS:
            if sid not in have:
                errors.append(f"缺段 {sid}")
                print(f"  [ERROR] 缺段 {sid}（或该段未包含 {game_type}）")
        for p in REQUIRED_GAMETEXT_PATHS:
            if not any(sid == "GameText" and path == p and act for sid, path, _g, act in sections):
                errors.append(f"缺 GameText 段 {p}")
                print(f"  [ERROR] 缺 GameText 段 {p}（SandBox 该段被 GameType 过滤 → 必须自备，雷 35）")
        if not errors:
            print(f"  （{len(REQUIRED_SECTION_IDS)} 个必需段 id 全在位；9 个文本段全在位 ✓）")

        # ── 2. 段 path 可解析 ──
        print("\n== 段 path 可解析（写错路径 = 引擎加载失败/静默空） ==")
        bad_path = 0
        for sid, path, _g, act in sections:
            if not act:
                continue
            if not ((data / (path + ".xml")).is_file() or (data / path).is_dir()):
                bad_path += 1
                errors.append(f"段 {sid} 的 path 找不到: {path}")
                print(f"  [ERROR] 段 {sid} path={path!r} 在 ModuleData 下既不是 .xml 也不是目录")
        if not bad_path:
            print(f"  （{len(sections)} 段全部可解析 ✓）")

        # ── 3. 孤儿数据文件 ──
        print("\n== 孤儿数据文件（写了但没有任何段加载它） ==")
        covered = section_covered(sections, data)
        mbproj = read_mbproj(data)          # ④ soln 体系挂载的（相对 ModuleData 的 posix 路径）
        orphans = 0
        for f in sorted(data.rglob("*.xml")):
            rel = f.relative_to(data).as_posix()
            stem = rel[:-4]
            if stem in covered or stem.split("/")[0] in covered:
                continue
            if stem in INTENTIONALLY_UNLOADED:
                # ⑤ 加载权在 C# 手里（如法术物品）—— 没段声明是对的
                print(f"  [INFO]  {rel} —— 故意不被引擎加载：{INTENTIONALLY_UNLOADED[stem]}")
                continue
            if rel in mbproj:
                continue                    # 挂在 project.mbproj 的 soln 行上 → 加载路径成立
            if stem in CONVENTIONAL_FILES or stem.split("/")[0] in CONVENTIONAL_DIRS:
                continue
            if is_soln_only(Path(rel).stem):
                continue                    # soln 体系由第 3b 段专管（报"没挂 mbproj"，不报"孤儿"）
            try:
                n = sum(1 for el in ET.parse(str(f)).getroot().iter() if el.get("id"))
            except Exception:
                n = -1
            if n > 0:
                orphans += 1
                errors.append(f"孤儿数据文件 {rel}（{n} 个定义）")
                print(f"  [ERROR] {rel} —— 有 {n} 个定义，但没有任何段加载它（运行时不存在）")
            else:
                warns.append(f"空模板 {rel}")
                print(f"  [INFO]  {rel} —— 0 个定义（模板/占位，忽略）")
        if not orphans:
            print("  （无 ✓）")

        # ── 3c. 🔴 反向断言：⑤ 表里的文件**不许**再被任何段声明（2026-10-12 立） ──
        #    理由：那些文件的加载权已经交给 C#（开关控制）。一旦某个段声明了它，
        #    引擎就会**无条件**加载 ⇒ 总闸失效（"发布时关掉"这个目的当场落空，而且零报错）。
        #    ⚠️ 这条是**结构性**的：`path="items"`（整目录）会把目录下每个 xml 都吃掉，
        #       所以目录形态同样要被抓到（下面 `stem.split("/")[0] in covered` 就管这个）。
        print("\n== ⑤ 故意不加载的文件不许被段声明（被声明 = 总闸失效） ==")
        regained = report_unloaded_not_declared(covered, errors)
        if not regained:
            print(f"  （{len(INTENTIONALLY_UNLOADED)} 个文件都还关着 ✓）")

        # ── 3b. soln 体系文件必须挂 project.mbproj（雷 122/136） ──
        #    判据不靠猜：这些文件**没有**任何"惯例文件名"回退路径，只有 mbproj 一条。
        #    文件在磁盘上而 mbproj 没挂 = 完全不加载（引擎零报错），后果按文件不同：
        #    AV 崩 / 武器姿势错 / 音效静音。
        print("\n== soln 体系文件挂载（不挂 = 完全不加载，雷 122/136） ==")
        unmounted = 0
        for f in sorted(data.rglob("*.xml")):
            rel = f.relative_to(data).as_posix()
            if not is_soln_only(Path(rel).stem):
                continue
            if rel in mbproj:
                continue
            unmounted += 1
            errors.append(f"{rel} 未挂 project.mbproj")
            print(f"  [ERROR] {rel} —— **未挂 project.mbproj**：文件在，但 project.mbproj 里没有"
                  f"对应的 <file name=…/> 行 → **完全不加载**（引擎零报错）")
            print(f"          修：在 ModuleData/project.mbproj 加一行 "
                  f"<file id=\"soln_{Path(rel).stem}\" name=\"ModuleData/{rel}\" type=\"…\"/>")
        if not unmounted:
            print("  （soln 体系文件全部已挂 ✓）")

        # ── 4. csproj 漏登记 .cs ──
        repo = Path(args.repo) if args.repo else Path(__file__).resolve().parent.parent
        proj = None
        for cand in repo.glob("ExampleModVS/**/*.csproj"):
            proj = cand
            break
        print(f"\n== csproj 编译清单（漏登记 = 静默不编译，build 0 错是假象，雷 40） ==")
        if proj is None:
            warns.append("未找到 .csproj（跳过）")
            print("  [WARN] 未找到 .csproj，跳过")
        else:
            txt = proj.read_text(encoding="utf-8-sig", errors="replace")
            # 只取**未被注释掉**的 Compile 行
            active_includes = set()
            for line in txt.splitlines():
                if line.lstrip().startswith("<!--"):
                    continue
                m = re.search(r'<Compile\s+Include="([^"]+)"', line)
                if m:
                    active_includes.add(m.group(1).replace("/", "\\"))
            proj_dir = proj.parent
            on_disk = []
            for f in proj_dir.rglob("*.cs"):
                rel = f.relative_to(proj_dir).as_posix()
                if rel.startswith("obj/") or rel.startswith("bin/"):
                    continue
                on_disk.append(rel)
            missing = [r for r in on_disk if r.replace("/", "\\") not in active_includes]
            print(f"  项目: {proj}")
            print(f"  磁盘 .cs: {len(on_disk)} | csproj 已登记: {len(active_includes)}")
            if missing:
                for m in missing:
                    # 有意未登记 vs 真漏：csproj 里（含注释）提到过这个名字 = 作者有交代 → INFO
                    # （注释里通常写类名不带 .cs —— 两种写法都要认）
                    if Path(m).name in txt or Path(m).stem in txt:
                        warns.append(f"有意未登记 {m}")
                        print(f"  [INFO]  {m} —— csproj 注释里有说明（有意未登记/已废弃）")
                    else:
                        errors.append(f"未登记 {m}")
                        print(f"  [ERROR] 未登记: {m} —— 改 csproj 加 <Compile Include=\"{m.replace('/', chr(92))}\" />")
            else:
                print("  （无漏登记 ✓）")

        print(f"\nSummary: sections={len(sections)} errors={len(errors)} warnings={len(warns)}")
        exit_codes.append(1) if errors else 0    # <- 必须在循环内：循环外只剩最后一趟的 errors（假绿，2026-09-12 修）



    return max(exit_codes) if exit_codes else 0

if __name__ == "__main__":
    sys.exit(main())

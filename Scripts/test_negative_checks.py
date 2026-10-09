#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""模块级检查负面测试（故意造坏数据/坏场景/坏源码 → 必须 exit 1）
============================================================================
**为什么要有第二套**：`test_negative_edges.py` 只覆盖边台账（CSV 侧）。本脚本覆盖**另一半**——
  XML 全量 parse 门 / 场景实体 / 据点与势力必填 / 私用区三档 / C# 源码不变量。
  依据同上：清单「写完必须做负面测试」——**只跑真数据全绿不算数**，真绿可能只是"检查根本没跑起来"。

做法：内容包与 CSV 各拷一份到临时目录 → 每个用例**只动一处** → 跑对应 checker
   → 核对 **退出码 + 输出里有没有点名那一处** → 还原 → 跑完删临时目录。
  **绝不碰真数据/真内容包/真源码**（源码用例用**合成仓库**，见 CASE 表末三条）。

用例三类：
  ① 该报的必须报（exit 1 且点名）：坏 XML / 缺场景脚本实体 / 缺地图交付物 / 缺 Kingdom banner_key /
     缺 Hero faction / 未知私用区码点 / 源码里相机复位被删 / 停用类被活跃代码引用
  ② 该静默的必须静默（exit 0）：**已知待补**的私用区码点（UNRESOLVED，字体码表未破解）
  ③ 该软报的软报（exit 0 但有警告）：**可直接还原**的私用区码点（表里有正字）

Usage:
  python Scripts/test_negative_checks.py            # 全部（约 30~60 秒）
  python Scripts/test_negative_checks.py -v         # 打印每个用例的输出尾巴
Exit: 0 全部符合预期 / 1 有用例不符合 / 2 fatal。
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
CSV_SRC = REPO / "Knowledge" / "太阁5" / "骑砍2织丰角色ID对应" / "csv"
MODULE_SRC = Path(r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")
# 🔴 2026-10-09 新增第二个沙箱：通用玩法（钩索/飞行/处决/法术/贴花）的数据搬进了 LivingWorldNpcs，
#    于是「物品网格字段」「动作接线」那几条用例的作案对象也搬过去了 —— 用例里用 target="lwn" 指它。
LWN_SRC = REPO
# 沙箱只拷这几样（ModuleData + 地图场景 + 段注册）；资产/音乐/视频与检查无关
SANDBOX_PARTS = ("ModuleData", "SceneObj", "GUI", "SubModule.xml", "Languages")
# LWN 没有 SceneObj/GUI；要的是物品段 + 动作接线 + project.mbproj（都在 ModuleData 里）
LWN_SANDBOX_PARTS = ("ModuleData", "SubModule.xml")

# 接受 `--official-root` 的 checker（决定沙箱根怎么传）；
# ⚠️ 给不认这个参数的脚本传 = argparse exit 2（不是检查失败，是参数没接——雷 92 同族）
TAKES_OFFICIAL_ROOT = {"check_data_fields.py", "check_required_ids.py",
                       "check_culture_references.py", "check_module_registration.py",
                       "check_era_segments.py", "check_scene_entities.py",
                       "check_scene_consumables.py"}
# 认 `--modules-root <根>/Modules --module <名>`（不是路径）的 checker：语言登记类
TAKES_MODULES_ROOT = {"check_language_registration.py"}


def run(script, *args):
    cmd = [sys.executable, str(HERE / script)] + [str(a) for a in args]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def patch_text(path, old, new, count=1):
    t = path.read_text(encoding="utf-8", errors="replace")
    n = t.count(old)
    if n < 1:
        raise SystemExit("[FATAL] %s 里找不到锚点：%r" % (path.name, old[:60]))
    path.write_text(t.replace(old, new, count), encoding="utf-8")
    return n


def patch_cell(path, key_col, key_val, col, new_val, head=2):
    sys.path.insert(0, str(HERE))
    from csv_dual import read_table, write_table
    cn, en, raw = read_table(str(path), head=head)
    cols = en if head == 2 else cn
    ki, ci = cols.index(key_col), cols.index(col)
    hit = 0
    for r in raw:
        if len(r) > ki and (r[ki] or "").strip() == key_val:
            while len(r) <= ci:
                r.append("")
            r[ci] = new_val
            hit += 1
    if hit != 1:
        raise SystemExit("[FATAL] %s 里 %s=%s 命中 %d 行" % (path.name, key_col, key_val, hit))
    write_table(str(path), cn, en, raw)


# ── 模块级用例：(说明, checker, 造坏动作, 期望退出码, 必须出现 / 禁止出现) ──
# 造坏动作用 lambda(sb_module, sb_csv) 表达；返回后自动还原
CASES = []


def case(desc, script, action, want, needle, extra_args=(), target="pack"):
    """target: "pack" = 内容包沙箱（默认）/ "lwn" = LivingWorldNpcs 沙箱。"""
    CASES.append((desc, script, action, want, needle, list(extra_args), target))


def _break_xml(m, c):
    patch_text(m / "ModuleData" / "settlements.xml", "</Components>", "<Components>", 1)


case("XML parse：未闭合标签必须抓到", "check_xml_parse.py", _break_xml, 1, "settlements.xml")

case("场景：缺引擎硬查询脚本实体必须抓到", "check_scene_entities.py",
     lambda m, c: patch_text(m / "SceneObj" / "Main_map" / "scene.xscene",
                             'name="CampaignMapSiegePrefabEntityCache"',
                             'name="CampaignMapSiegePrefabEntityCacheXX"', 1),
     1, "CampaignMapSiegePrefabEntityCache")

case("场景：缺地图交付物 terrain.bin 必须抓到", "check_scene_entities.py",
     lambda m, c: (m / "SceneObj" / "Main_map" / "terrain.bin").unlink(), 1, "terrain.bin")

case("必填：Kingdom 缺 banner_key 必须抓到", "check_data_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "spkingdoms.xml", "banner_key=", "banner_key_removed=", 1),
     1, "banner_key")

case("必填：Hero 缺 faction 必须抓到", "check_data_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "taikou_heroes.xml",
                             '<Hero id="lord_tk5_195" faction="Faction.clan_oda_1"',
                             '<Hero id="lord_tk5_195"', 1),
     1, "faction")

# 雷 110：文化缺部队模板属性 = 该文化的领主部队刷兵 NRE（2026-09-12 实机崩溃根因）
case("必填：文化缺 default_party_template 必须抓到（雷 110）", "check_data_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "spcultures.xml",
                             'default_party_template="PartyTemplate.taikou_lord_party_template"\n', "", 1),
     1, "default_party_template")

# 雷 115：文化部队模板指向**占位模板**（内容 = 1× main_hero「主角」，is_hero=true）
#   = 全世界部队被填成「主角」副本（实机症状：地图 100+ 人、队伍界面无一兵）
case("部队模板：指向英雄占位模板必须抓到（雷 115）", "check_data_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "spcultures.xml",
                             'default_party_template="PartyTemplate.taikou_lord_party_template"',
                             'default_party_template="PartyTemplate.main_hero_party_template"', 1),
     1, "is_hero=true")

# 雷 115 同族：部队模板引用不存在的兵种（改 id 忘改模板）= 刷兵悬空
case("部队模板：引用未定义兵种必须抓到（雷 115）", "check_data_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "partyTemplates.xml",
                             'troop="NPCCharacter.yari_ashigaru"',
                             'troop="NPCCharacter.yari_ashigaru_XX"', 1),
     1, "引用未定义兵种")

case("私用区：还原表里没有的码点 = 硬错误", "check_taikou_world_tables.py",
     lambda m, c: patch_cell(c / "TaikouHero.csv", "ID", "lord_tk5_195", "CNName", "织田\uE7FF信长"),
     1, "U+E7FF")

case("私用区：可直接还原的码点 = 警告（exit 0）", "check_taikou_world_tables.py",
     lambda m, c: patch_cell(c / "TaikouHero.csv", "ID", "lord_tk5_195", "CNName", "\uE40B左卫门"),
     0, "U+E40B")

case("私用区：已登记待补的码点必须静默（exit 0）", "check_taikou_world_tables.py",
     lambda m, c: patch_cell(c / "TaikouHero.csv", "ID", "lord_tk5_195", "CNName", "\uE413兵卫"),
     0, "已知待补·不告警")           # 必须落进「已知待补」段（= 不告警），而不是警告段

# 语言：<strings> 块外的条目 = 引擎一条都不加载（2026-09-12 实机：势力/家族/英雄全英文）
case("语言：<strings> 块外的 <string> 必须抓到", "check_language_registration.py",
     lambda m, c: patch_text(m / "ModuleData" / "Languages" / "CNs" / "std_Taikou_strings.xml",
                             "</strings>",
                             "</strings>\n  <string id=\"TAIKOU_out_of_block\" text=\"块外条目\" />", 1),
     1, "在 <strings> 之外")

# 物品：缺 Civilian 标记 = 玩家在城镇换日常装时选不到它（2026-09-21 用户裁定：任何装备都要平民可用）
case("物品：缺 <Flags Civilian=\"true\"/> 必须抓到", "check_items_civilian.py",
     lambda m, c: patch_text(m / "ModuleData" / "taikou_items" / "firearms.xml",
                             '<Flags Civilian="true" />', '<Flags Stealth="true" />', 1),
     1, "taikou_teppo")

# 物品：空网格字段 = 引擎取网格的每条路**都没做 null 兜底**，崩法按字段而异（雷 166/167 同一族，2026-10-08 立）
#   ① 箭类物品 `holster_mesh=""` —— 背包图标取的就是它（`GetItemMeshForInventory` 对 Arrows/Bolts
#      直接返回 holster 网格）⇒ 进装备/背包界面**搜到这件就 NullReferenceException**（雷 168）
#   🔴 2026-10-09：钩索物品搬进了 LWN（`ModuleData/items/grapple.xml`）⇒ 这三条改打 LWN 沙箱。
case("物品：箭类 holster_mesh 留空必须抓到（雷 168）", "check_items_mesh_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "items" / "grapple.xml",
                             'holster_mesh="lwn_grapple_hook"', 'holster_mesh=""', 1),
     1, "lwn_grapple_hook", target="lwn")

#   ② `mesh=""` —— 装备那一刻 AccessViolation（雷 166）；pattern 取「任意类型都命中」的第一处
#      （`\t\tmesh=` 不会误伤 `\t\tholster_mesh=`：前缀必须紧贴 `mesh=`）
case("物品：mesh 留空必须抓到（雷 166）", "check_items_mesh_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "items" / "grapple.xml",
                             '\t\tmesh="lwn_proxy_invisible"\n', '\t\tmesh=""\n', 1),
     1, "lwn_grapple_hook", target="lwn")

#   ③ 整个 `mesh` 属性缺失 —— 与空串同源（`MeshName == null`）
case("物品：整个 mesh 属性缺失必须抓到", "check_items_mesh_fields.py",
     lambda m, c: patch_text(m / "ModuleData" / "items" / "grapple.xml",
                             '\t\tmesh="lwn_proxy_invisible"\n', '', 1),
     1, "属性缺失", target="lwn")

# 动作接线：被 movement_sets 引用的动作名没在 action_types.xml 声明 = 引擎拿无效动作索引 = 拔装备 AV
#   （雷 169，2026-10-08 实机：崩在 MissionState.TickMission 的托管→本机转换，日志零线索）
#   🔴 2026-10-09：这 44 条通用动作名的声明搬进了 LWN ⇒ 改打 LWN 沙箱。
case("动作接线：漏声明必须抓到（雷 169）", "check_action_wiring.py",
     lambda m, c: patch_text(m / "ModuleData" / "action_types.xml",
                             '\t<action name="act_grapple_walk_forward" />\n', '', 1),
     1, "act_grapple_walk_forward", target="lwn")

# soln 体系：文件在磁盘上但 project.mbproj 没挂 = **完全不加载**（引擎零报错）
#   造坏 = 把挂 item_usage_sets 的那行**注释掉** —— 注意注释里还留着同一串文本，
#   所以这条同时验了两件事：①"没挂"能抓到 ②read_mbproj 剥注释（否则会把注释里的示例当成真挂载）
case("soln 体系：project.mbproj 没挂 item_usage_sets 必须抓到（雷 122）",
     "check_module_registration.py",
     lambda m, c: patch_text(
         m / "ModuleData" / "project.mbproj",
         '<file id="soln_item_usage_sets" name="ModuleData/item_usage_sets.xml" type="item_usage_set"/>',
         '<!-- <file id="soln_item_usage_sets" name="ModuleData/item_usage_sets.xml" '
         'type="item_usage_set"/> -->', 1),
     1, "未挂 project.mbproj")

# soln 体系（粒子）：粒子文件名是**带前缀的一族**（particle_systems_<名>.xml），
#   按整名匹配会漏掉它 —— 这条专门验「前缀匹配」生效（2026-09-23 加，官方数据核过：
#   Native 挂了 7 行 particle_systems*，粒子同属 soln 体系）。
#   🔴 2026-10-09：粒子 XML 随通用玩法搬进 LWN ⇒ 改打 LWN 沙箱（锚点也换成那边的行文格式）。
case("soln 体系：没挂 particle_systems_yinmo 必须抓到（前缀匹配，不是整名）",
     "check_module_registration.py",
     lambda m, c: patch_text(
         m / "ModuleData" / "project.mbproj",
         '<file id="soln_particle_systems"   name="ModuleData/particle_systems_yinmo.xml"   '
         'type="particle_system" />',
         '<!-- <file id="soln_particle_systems"   name="ModuleData/particle_systems_yinmo.xml"   '
         'type="particle_system" /> -->', 1),
     1, "particle_systems_yinmo.xml", target="lwn")

# 选人目录：Realm/House 的类型引用了不存在的筛档（左列点它会筛出空）
case("选人目录：悬空的势力类型必须抓到", "check_hero_profile_keys.py",
     lambda m, c: patch_text(m / "ModuleData" / "AssetRegistry" / "HeroCatalog.xml",
                             'type="warrior"', 'type="warriorXX"', 1),
     1, "不在 <RealmType> 档位表里")

# 选人目录：某人的名字键串成了别人的（实测事故：全目录英雄名变成家族名）
case("选人目录：Lord 名字键张冠李戴必须抓到", "check_hero_profile_keys.py",
     lambda m, c: patch_text(m / "ModuleData" / "AssetRegistry" / "HeroCatalog.xml",
                             'name="{=TAIKOU_hero_195_1560}Oda Nobunaga"',
                             'name="{=TAIKOU_clan_oda_1}Oda"', 1),
     1, "的名字键是")

# ── C# 源码用例：用合成仓库（不碰真源码）──
SRC_OK = """using System;
namespace X {
  public class Cc {
    void Finish() {
      mapState.Handler.ResetCamera(true, true);
      mapState.Handler.TeleportCameraToMainParty();
    }
    public virtual Vec2? StartingPosition => null;
    void Hook() { OnNewGameCreatedPartialFollowUp(0); }
  }
}
"""
SRC_NO_CAMERA = SRC_OK.replace("ResetCamera(true, true);", "/* 已退役 */")
SRC_REFERS_DISABLED = """using System;
namespace X {
  public class User {
    void F() { GameMenuLogger.Tick(); }   // 引用了 csproj 已注释停用的类
  }
}
"""
CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <Compile Include="Cc.cs" />
    <!-- <Compile Include="GameMenuLogger.cs" /> -->
  </ItemGroup>
</Project>
"""


def make_repo(root, cc_src, logger_present=False):
    (root / "ExampleModVS" / "M").mkdir(parents=True, exist_ok=True)
    (root / "ExampleModVS" / "M" / "M.csproj").write_text(CSPROJ, encoding="utf-8")
    (root / "ExampleModVS" / "M" / "Cc.cs").write_text(cc_src, encoding="utf-8")
    if logger_present:
        (root / "ExampleModVS" / "M" / "GameMenuLogger.cs").write_text(
            "namespace X { public static class GameMenuLogger { public static void Tick() {} } }\n",
            encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（本检查自带沙箱）")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    if not CSV_SRC.is_dir() or not MODULE_SRC.is_dir():
        print("[FATAL] 找不到源数据：%s / %s" % (CSV_SRC, MODULE_SRC), file=sys.stderr)
        return 2

    tmp = Path(tempfile.mkdtemp(prefix="lwn_mod_neg_"))
    # 🔴 沙箱布局两条硬要求（2026-09-12 实测踩出来的，缺任一条 = 用例在"另一个世界"上跑）：
    #    ① 目录名必须是真内容包名（Taikou）——GameType 推断按「<模块名>Campaign<年份>」正则匹配
    #       SubModule.xml 里的 GameType 值；换个名字 = 推断不出 = 一个段都不加载。
    #    ② 必须是 `<沙箱根>/Modules/Taikou` 这个形状，且把 `<沙箱根>` 当 `--official-root` 传——
    #       因为多数 checker 会用 `--official-root`（缺省=注册表 MB2_PATH）**覆盖**模块根，
    #       不这么摆 = 检查读的是**真内容包**，造坏的那份根本没人看（用例假绿/假红）。
    sandbox_root = tmp / "sandbox"
    pristine_mod = tmp / "_pristine" / "Taikou"
    sandbox_mod = sandbox_root / "Modules" / "Taikou"
    pristine_lwn = tmp / "_pristine" / "LivingWorldNpcs"
    sandbox_lwn = sandbox_root / "Modules" / "LivingWorldNpcs"
    pristine_csv, sandbox_csv = tmp / "csv_pristine", tmp / "csv"
    results = []
    try:
        # 沙箱：内容包只拷需要的部分；CSV 全拷
        for d in (pristine_mod, sandbox_mod):
            d.mkdir(parents=True)
            for part in SANDBOX_PARTS:
                src = MODULE_SRC / part
                if src.is_dir():
                    shutil.copytree(str(src), str(d / part))
                elif src.is_file():
                    shutil.copy2(str(src), str(d / part))
        # LWN 沙箱（第二批用例：钩索物品与通用动作名）
        for d in (pristine_lwn, sandbox_lwn):
            d.mkdir(parents=True)
            for part in LWN_SANDBOX_PARTS:
                src = LWN_SRC / part
                if src.is_dir():
                    shutil.copytree(str(src), str(d / part))
                elif src.is_file():
                    shutil.copy2(str(src), str(d / part))
        shutil.copytree(str(CSV_SRC), str(pristine_csv))
        shutil.copytree(str(CSV_SRC), str(sandbox_csv))

        for desc, script, action, want, needle, _extra, target in CASES:
            # 每个用例前把沙箱恢复成干净副本
            for name in SANDBOX_PARTS:
                dst, src = sandbox_mod / name, pristine_mod / name
                if dst.is_dir():
                    shutil.rmtree(str(dst))
                    shutil.copytree(str(src), str(dst))
                elif src.is_file():
                    shutil.copy2(str(src), str(dst))
            if not (sandbox_mod / "SceneObj" / "Main_map" / "terrain.bin").exists():
                shutil.copy2(str(pristine_mod / "SceneObj" / "Main_map" / "terrain.bin"),
                             str(sandbox_mod / "SceneObj" / "Main_map" / "terrain.bin"))
            for name in LWN_SANDBOX_PARTS:
                dst, src = sandbox_lwn / name, pristine_lwn / name
                if dst.is_dir():
                    shutil.rmtree(str(dst))
                    shutil.copytree(str(src), str(dst))
                elif src.is_file():
                    shutil.copy2(str(src), str(dst))
            shutil.rmtree(str(sandbox_csv))
            shutil.copytree(str(pristine_csv), str(sandbox_csv))

            # target="lwn" 的用例：作案对象与 --module 都换成 LWN 沙箱
            m = sandbox_lwn if target == "lwn" else sandbox_mod
            action(m, sandbox_csv)
            if script in TAKES_MODULES_ROOT:
                # 语言登记类：吃「模块根 + 模块名」，不是模块路径
                extra = ["--modules-root", sandbox_root / "Modules", "--module", "Taikou"]
            else:
                extra = ["--module", m]
            if script in TAKES_OFFICIAL_ROOT:
                extra += ["--official-root", sandbox_root]
            if script == "check_taikou_world_tables.py":
                extra += ["--csv-dir", sandbox_csv]
            code, out = run(script, *extra)
            ok = (code == want) and (needle in out)
            results.append((desc, ok, code, want, out))
            if args.verbose or not ok:
                print("\n%s %s（exit %d，期望 %d）" % ("✅" if ok else "❌", desc, code, want))
                print("   " + "\n   ".join(out.strip().splitlines()[-10:]))

        # ── C# 源码用例（合成仓库）──
        for desc, cc, logger, want, needle in (
            ("源码：相机复位两行被删必须抓到", SRC_NO_CAMERA, False, 1, "ResetCamera"),
            ("源码：停用类被活跃代码引用必须抓到", SRC_REFERS_DISABLED, True, 1, "GameMenuLogger"),
            ("源码：齐全时必须全绿", SRC_OK, False, 0, "硬错误 0"),
        ):
            r = tmp / ("repo_" + re.sub(r"\W+", "_", desc))
            if r.exists():
                shutil.rmtree(str(r))
            make_repo(r, cc, logger)
            code, out = run("check_source_invariants.py", "--repo", r)
            ok = (code == want) and (needle in out)
            results.append((desc, ok, code, want, out))
            if args.verbose or not ok:
                print("\n%s %s（exit %d，期望 %d）" % ("✅" if ok else "❌", desc, code, want))
                print("   " + "\n   ".join(out.strip().splitlines()[-8:]))

        bad = [r for r in results if not r[1]]
        print("\n%s\n模块级负面测试：%d 个用例，%d 通过 / %d 失败\n%s"
              % ("=" * 74, len(results), len(results) - len(bad), len(bad), "=" * 74))
        for desc, ok, code, want, _ in results:
            print("  %s %-44s exit %d（期望 %d）" % ("✅" if ok else "❌", desc, code, want))
        return 1 if bad else 0
    finally:
        shutil.rmtree(str(tmp), ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())

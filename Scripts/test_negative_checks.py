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
# 沙箱只拷这几样（ModuleData + 地图场景 + 段注册）；资产/音乐/视频与检查无关
SANDBOX_PARTS = ("ModuleData", "SceneObj", "GUI", "SubModule.xml", "Languages")

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


def case(desc, script, action, want, needle, extra_args=()):
    CASES.append((desc, script, action, want, needle, list(extra_args)))


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

# 选人目录：Realm/House 的类型引用了不存在的筛档（左列点它会筛出空）
case("选人目录：悬空的势力类型必须抓到", "check_hero_profile_keys.py",
     lambda m, c: patch_text(m / "ModuleData" / "AssetRegistry" / "HeroCatalog.xml",
                             'type="warrior"', 'type="warriorXX"', 1),
     1, "不在 <RealmType> 档位表里")

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
        shutil.copytree(str(CSV_SRC), str(pristine_csv))
        shutil.copytree(str(CSV_SRC), str(sandbox_csv))

        for desc, script, action, want, needle, _extra in CASES:
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
            shutil.rmtree(str(sandbox_csv))
            shutil.copytree(str(pristine_csv), str(sandbox_csv))

            action(sandbox_mod, sandbox_csv)
            if script in TAKES_MODULES_ROOT:
                # 语言登记类：吃「模块根 + 模块名」，不是模块路径
                extra = ["--modules-root", sandbox_root / "Modules", "--module", "Taikou"]
            else:
                extra = ["--module", sandbox_mod]
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

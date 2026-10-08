#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Action wiring checker (动作名接线体检 —— 雷 169)
============================================================================
背景（2026-10-08 实机，钩索走/跑那两条）：
  给内容包接一个**新动作名**，必须三处都写：
    ① **`action_types.xml` 声明动作名**（引擎的"动作名注册表"）
    ② `action_sets.xml` 把动作名映射到 clip
    ③ **谁引用它**（`movement_sets.xml` 的 `idle`/`forward`…、`item_usage_sets.xml` 的
       `ready_action`/`release_action`/`reload_action`…、`item_holsters.xml`）
  **只做②③ = 引擎解析持械武器的动作表时拿到无效动作索引 ⇒ `AccessViolation`**，
  崩在 `MissionState.TickMission` 的"托管→本机"转换，**栈里没有托管帧、日志一条线索都没有**。
  规则早见于 [wheels.d/assets.md](../plans/rules/wheels.d/assets.md) 卷十五 §3「接线三件套」——
  本脚本就是"把规则变成动作"的那一步（2026-10-08 用户裁定：规则要能自动查）。

判据（一句话）：
  **内容包里出现的每个 `act_*` 名字，必须能在这两处之一找到声明** ——
    · 本包自己的 `action_types.xml`，或
    · **原版动作名基准表** `vanilla_action_names.txt`（脚本同目录，从 Native/SandBoxCore/SandBox 生成）。
  找不到 = ERROR（引擎那边就是无效索引）。反向（声明了却没人引用）= WARN（多半是拼错 / 写完忘接线）。

扫什么：
  · **引用源** = 内容包 `ModuleData/**/*.xml`（**剥掉 XML 注释**：注释里写动作名不算引用）
    + 本仓库 `ExampleModVS/**/*.cs`（控制台命令里手播的动作名，如 `act_fly_idle`，只在代码里出现）。
  · **声明源** = `--module` 的 `action_types.xml` + 基准表。
  · 名字必须**带左边界**（`(?<![A-Za-z0-9_])`）—— 否则 `impact_particle` / `..._contract_end`
    里的子串会被误认（2026-10-08 实测的两个误报）。

基准表怎么来（**生成物，禁止手改**，铁律 22）：
  `python Scripts/check_action_wiring.py --regen-basis`   # 默认路径读注册表 MB2_PATH（铁律 19）
  `python Scripts/check_action_wiring.py --regen-basis --game "<游戏根>"`
  ⇒ 重写 `Scripts/vanilla_action_names.txt`。

Usage:
  python Scripts/check_action_wiring.py [--module PATH] [--basis PATH]
Exit: 0 全绿（可能有 WARN）/ 1 有未声明的动作名 / 2 fatal。
负面测试：`Scripts/test_negative_checks.py`「动作接线：漏声明必须抓到（雷 169）」。
"""
import argparse
import io
import os
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
BASIS = HERE / "vanilla_action_names.txt"

# 名字 = act_ 开头 + 词字符；**左边界**排除 `impact_particle` / `..._contract_end` 这类子串
NAME_RE = re.compile(r"(?<![A-Za-z0-9_])(act_[A-Za-z0-9_]+)")
# action_types.xml 里的声明写法：<action name="act_x" ... />
DECL_RE = re.compile(r'name="(act_[A-Za-z0-9_]+)"')
COMMENT_RE = re.compile(r"<!--.*?-->", re.S)


def read(path):
    return io.open(path, encoding="utf-8-sig", errors="replace").read()


def collect_basis(official_root):
    """从原版模块的 action_types.xml 收集动作名（基准表内容）。"""
    names = set()
    files = []
    for mod in ("Native", "SandBoxCore", "SandBox"):
        p = Path(official_root) / "Modules" / mod / "ModuleData" / "action_types.xml"
        if p.is_file():
            files.append(p)
            names |= set(NAME_RE.findall(read(p)))
    return sorted(names), files


def regen_basis(game_root):
    """读注册表/给定路径 → 重写基准表（生成物）。"""
    if game_root is None:
        try:
            import winreg
            for hive in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
                try:
                    with winreg.OpenKey(hive, "Environment") as k:
                        game_root = winreg.QueryValueEx(k, "MB2_PATH")[0]
                        break
                except OSError:
                    continue
        except ImportError:
            pass
    if not game_root:
        print("[FATAL] 没拿到游戏根目录：--game <路径> 或注册表 MB2_PATH（铁律 19：以注册表为准）")
        return 2
    names, files = collect_basis(game_root)
    if not names:
        print(f"[FATAL] 在 {game_root}\\Modules\\<Native|SandBoxCore|SandBox>\\ModuleData\\action_types.xml 里没抽到动作名")
        return 2
    out = [
        "# 原版动作名基准表（生成物，禁止手改 —— 铁律 22）",
        "# 由 Scripts/check_action_wiring.py --regen-basis 生成",
        f"# 来源（{len(files)} 份 action_types.xml）: " + " · ".join(f.parents[1].name for f in files),
        f"# 游戏根: {game_root}",
        f"# 动作名数: {len(names)}",
        "",
    ] + names
    BASIS.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"写出 {BASIS}（{len(names)} 个动作名）")
    return 0


def main():
    ap = argparse.ArgumentParser(description="Action wiring checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--basis", default=str(BASIS))
    ap.add_argument("--regen-basis", action="store_true", help="重生成原版动作名基准表")
    ap.add_argument("--game", default=None, help="--regen-basis 用：游戏根目录（默认读注册表 MB2_PATH）")
    args = ap.parse_args()

    if args.regen_basis:
        return regen_basis(args.game)

    mod = Path(args.module)
    data = mod / "ModuleData"
    if not data.is_dir():
        print(f"[FATAL] ModuleData not found: {data}")
        return 2
    basis_path = Path(args.basis)
    if not basis_path.is_file():
        print(f"[FATAL] 基准表缺失：{basis_path}（先跑 --regen-basis）")
        return 2

    vanilla = {l.strip() for l in read(basis_path).splitlines()
               if l.strip() and not l.startswith("#")}

    # ① 本包声明的动作名
    own_decl = {}
    decl_file = data / "action_types.xml"
    if decl_file.is_file():
        # 🔴 先剥注释再收声明：**注释掉的声明引擎不算**（XML 注释被忽略）——不剥就会漏报
        for m in DECL_RE.finditer(COMMENT_RE.sub("", read(decl_file))):
            own_decl.setdefault(m.group(1), decl_file.name)
    declared = set(own_decl) | vanilla

    # ② 引用源分两类（口径见文件头"两级"）：
    #    · **XML 引用 = ERROR 级** —— 内容包 ModuleData 里的 movement_sets / item_usage_sets / action_sets /
    #      item_holsters 引用的动作名，引擎解析不到就是**无效索引 ⇒ AV**，正是雷 169 那一类。
    #    · **C# 里的名字 = 只参与"有没有人用"的判断** —— 代码里常见的不是真动作名：
    #      前缀片段（`"act_fly_" + suffix`）、哨兵值（`act_none`）、原版动作名（`act_bow_1` 这种
    #      **本来就不在 action_types.xml 里**的表情/姿态名）⇒ 拿它报 ERROR 就是误报。
    refs, cs_refs = {}, set()
    for f in sorted(data.rglob("*.xml")):
        if "Languages" in f.parts:
            continue
        txt = COMMENT_RE.sub("", read(f))
        for m in NAME_RE.finditer(txt):
            refs.setdefault(m.group(1), set()).add(f.relative_to(data).as_posix())
    cs_root = REPO / "ExampleModVS"
    if cs_root.is_dir():
        for f in sorted(cs_root.rglob("*.cs")):
            if "obj" in f.parts or "bin" in f.parts:
                continue
            for m in NAME_RE.finditer(read(f)):
                cs_refs.add(m.group(1))

    missing = sorted(n for n in refs if n not in declared)
    unused = sorted(n for n in own_decl if n not in refs and n not in cs_refs)

    print(f"== 动作接线体检（{mod.name}） ==")
    print(f"  本包声明 {len(own_decl)} 个 · 原版基准表 {len(vanilla)} 个 · 被引用的名字 {len(refs)} 个")
    if missing:
        print(f"  [ERROR] {len(missing)} 个动作名**被引用但没声明** —— 引擎会当无效索引（拔装备即 AV，雷 169）：")
        for n in missing:
            where = ", ".join(sorted(refs[n])[:3])
            print(f"      {n}   ← {where}")
        print("\n  修：在 ModuleData/action_types.xml 里补一行裸声明 <action name=\"act_x\" />")
        print("      （移动/待机这一族原版就是零属性；带 type/action_stage 的只有「要挂进状态机」的动作）")
    else:
        print("  （所有被引用的动作名都已声明 ✓）")
    if unused:
        print(f"  [WARN] {len(unused)} 个动作名声明了但没人引用（多半是拼错 / 写完忘接线，不拦）：")
        for n in unused:
            print(f"      {n}   （{own_decl[n]}）")
    print(f"\nSummary: declared={len(own_decl)} vanilla={len(vanilla)} referenced={len(refs)} "
          f"missing={len(missing)} unused={len(unused)}")
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())

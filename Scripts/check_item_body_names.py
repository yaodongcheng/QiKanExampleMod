#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Item body-name checker (物品的碰撞体名必须真的存在 —— 雷 172)
============================================================================
背景（2026-10-24 实机 + 内存转储实证）：
  进战场时引擎会**预装载参战角色身上每件装备**的网格与碰撞体，然后原地死等：

      PreloadHelper.WaitForMeshesToBeLoaded()   // 1.2.12 反编译原文，没有超时
        do {
            num = 0;
            foreach (m in _uniqueMetaMeshes)          num += m.CheckResources();
            foreach (s in _uniqueDynamicPhysicsShapeName)
                num += PhysicsShape.GetFromResource(s, true) == null ? 1 : 0;
            Thread.Sleep(1);
        } while (num != 0);

  **只要有一件装备的碰撞体名游戏里不存在，这个循环就永远不退出** ——
  症状 = 进战场后 loading 界面永不撤、画面冻死、**还能听到战吼**、无异常无崩溃。
  实测：太阁弓 `body_name="bo_longbow_a"`（游戏里没有；正确 = `bo_composite_longbow_a`），
  弓足轻人人有 ⇒ **场场必卡**；同批还有盾的 `shield_body_name="bo_sturgia_shield_b"`（正确 `_b`→`_a`）。

判据：
  物品的这四个属性必须**逐字出现在**游戏的碰撞体权威表
  `<game>/Modules/Native/ModuleData/CoreGameReferences/physics_shape.txt` 里：
      body_name · holster_body_name · collision_body_name · shield_body_name

扫描口径：`<module>/ModuleData/**/*.xml` 里**根元素是 `<Items>`** 的每一份，其**直接子元素** `<Item>`
  （与 check_items_mesh_fields.py 同口径：`<Cosmetic>` 里的 `<Item>` 是引用不是定义）。

容错（铁律 1）：单文件解析失败 → 记一条、继续；权威表找不到 → 打 WARN 并 exit 0
  （没装游戏/没设 MB2_PATH 的机器上不该把体检染红，但会明确告诉你"本项没查到"）。

Usage:
  python Scripts/check_item_body_names.py [--module PATH] [--game PATH]
Exit: 0 全合规 / 1 有错误 / 2 fatal。
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BODY_ATTRS = ("body_name", "holster_body_name", "collision_body_name", "shield_body_name")

DEFAULT_GAME = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"

# 🔴 **已知且接受的坏名**（2026-10-24 用户裁定）：报 WARN 不报 ERROR，一键体检保持全绿。
#    · 用户要求**故意保留脏数据**当保险丝的实机回归用例 —— 每场战斗都能验证
#      `Core/PreloadWaitGuard.cs`（5 秒无进展 → 记日志放行）真的顶得住。
#    · 代价（已告知并接受）：该局弓兵的弓**没有碰撞体**；进战场前多等 ~5 秒。
#    · **新增的任何坏名一律 ERROR** —— 这张表只认这里逐条写死的。
KNOWN_ACCEPTED = {
    ("taikou_troop_yumi_weapon_a", "body_name", "bo_longbow_a"):
        "保留为保险丝回归用例（正确名 bo_composite_longbow_a）；由 PreloadWaitGuard 兜底放行",
}


def registry_mb2_path():
    """读注册表 MB2_PATH（User 级优先，再 Machine）—— 铁律 19：环境变量以注册表为准。"""
    if sys.platform != "win32":
        return None
    import winreg
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


def load_shapes(game_root):
    """碰撞体权威表 → set(名字)。找不到返回 None。"""
    if not game_root:
        return None
    path = Path(game_root) / "Modules" / "Native" / "ModuleData" / "CoreGameReferences" / "physics_shape.txt"
    if not path.is_file():
        return None
    names = set()
    for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        s = line.strip()
        if s:
            names.add(s)
    return names or None


def scan(module_root, shapes, errors, stats):
    for xml_path in sorted(Path(module_root, "ModuleData").rglob("*.xml")):
        try:
            root = ET.parse(str(xml_path)).getroot()
        except Exception as ex:                      # 铁律 1：单文件坏了不拖垮体检
            print(f"[WARN] 解析失败，跳过：{xml_path} ({ex})")
            continue
        if root.tag != "Items":
            continue
        for item in root.findall("Item"):
            item_id = item.get("id") or "(无 id)"
            for attr in BODY_ATTRS:
                value = item.get(attr)
                if value is None or value == "":
                    continue
                stats["refs"] += 1
                if value not in shapes:
                    key = (item_id, attr, value)
                    if key in KNOWN_ACCEPTED:
                        stats["accepted"] += 1
                        print(f"[WARN] {xml_path.name} · 物品 '{item_id}' 的 {attr}='{value}' 不存在"
                              f" —— 已登记为已知接受：{KNOWN_ACCEPTED[key]}")
                        continue
                    stats["bad"] += 1
                    errors.append(
                        f"[ERROR] {xml_path.name} · 物品 '{item_id}' 的 {attr}='{value}' "
                        f"不在 physics_shape.txt 里 ⇒ 装备后进战场会卡死在 loading")


def main():
    ap = argparse.ArgumentParser(description="物品碰撞体名必须在游戏的 physics_shape.txt 里（雷 172）")
    here = Path(__file__).resolve().parent
    ap.add_argument("--module", default=str(here.parent), help="要扫的模块根目录（默认本仓库）")
    ap.add_argument("--game", default=None, help="游戏根目录（默认读注册表 MB2_PATH）")
    args = ap.parse_args()

    game_root = args.game or registry_mb2_path() or DEFAULT_GAME
    shapes = load_shapes(game_root)
    if shapes is None:
        print(f"[WARN] 找不到碰撞体权威表（game_root={game_root!r}）"
              f" → 本项未检查（装游戏或给 --game 后重跑）")
        return 0

    errors, stats = [], {"refs": 0, "bad": 0, "accepted": 0}
    scan(args.module, shapes, errors, stats)

    for line in errors:
        print(line)
    if errors:
        print(f"\n[FAIL] 碰撞体引用 {stats['refs']} 处，其中 {stats['bad']} 处名字不存在（雷 172）")
        return 1

    print(f"[OK] 碰撞体引用 {stats['refs']} 处全部存在"
          f"（权威表 {len(shapes)} 条；其中 {stats['accepted']} 处为已登记接受）")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as ex:
        print(f"[FATAL] {ex}")
        sys.exit(2)

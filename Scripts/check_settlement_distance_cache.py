#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Settlement-distance-cache checker (自定义世界「距离缓存与据点数据一致」离线体检)
========================================================================
覆盖「必备清单」§1.3 的生成物纪律条目（雷 53）：
  `ModuleData/settlements_distance_cache.bin` 是**生成物**，由 `SettlementPositionScript` 按
  「settlements.xml 的每个 id → 找**同名场景实体**」生成（找不到实体即 `continue` 跳过）。
  改据点后不重生成 = 缓存过期 → 据点对变少 → 全局最大据点距 `MaximumDistanceBetweenTwoSettlements`
  恒 0 → 家宅打分末步 `1 − 距离/0` = **NaN** → `NaN > 0` 恒假 → 家宅/王国家园永远选不中
  （表面无症状，被运行期反射置位兜着）。

规则：
  1. 缓存文件存在（缺 = 引擎当场重建但**当次启动内存里是空的** → 那一局的家宅仍是 null）
  2. 缓存里的据点 id 集合 == 期望集合（settlements.xml ∩ 有同名场景实体，服务性据点除外）
     —— 少了 = 缓存过期（改完据点没重生成）；多了 = 缓存里有世界里已删的据点
  3. 据点对 ≥ 1（= 至少有 2 个据点进缓存，否则最大据点距必为 0 → 家宅 NaN）

缓存文件格式（反编译 DefaultMapDistanceModel.LoadCacheFromFile 实锤）：
  int32 count
  for l in 0..count-1:  string id_l
                        for m in l+1..count-1:  string id_m + float32 距离
  then:  int32 faceIndex + string 据点id …… 直到 faceIndex < 0（导航面→最近据点缓存，不校验）
  字符串 = .NET BinaryWriter.Write(string) = 7-bit 变长长度前缀 + UTF-8

Usage:
  python Scripts/check_settlement_distance_cache.py [--module PATH]
Exit: 0 无问题 / 1 有问题 / 2 fatal。
"""
import argparse
import io
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MAP_COMPONENT_TAGS = ("Town", "Village", "Castle", "Hideout")


def read_dotnet_string(f):
    n, shift = 0, 0
    while True:
        b = f.read(1)
        if not b:
            raise EOFError("字符串长度前缀读到文件尾")
        v = b[0]
        n |= (v & 0x7F) << shift
        if not (v & 0x80):
            break
        shift += 7
    return f.read(n).decode("utf-8")


def parse_cache(path):
    """→ (据点 id 列表, 据点对数)。导航面缓存段不解析（不校验）。"""
    data = path.read_bytes()
    f = io.BytesIO(data)
    count = int.from_bytes(f.read(4), "little", signed=True)
    ids, pairs = [], 0
    for l in range(count):
        ids.append(read_dotnet_string(f))
        for _m in range(l + 1, count):
            read_dotnet_string(f)          # id_m
            f.read(4)                      # float32 距离
            pairs += 1
    return ids, pairs


def scene_entity_names(scene_file):
    try:
        root = ET.parse(str(scene_file)).getroot()
    except Exception:
        return None
    names = set()

    def walk(node):
        for e in node.findall("game_entity"):
            if e.get("name"):
                names.add(e.get("name"))
            ch = e.find("children")
            if ch is not None:
                walk(ch)

    ents = root.find("entities")
    if ents is not None:
        walk(ents)
    return names


def main():
    ap = argparse.ArgumentParser(description="Settlement distance cache checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--scene", default="Main_map")
    args = ap.parse_args()

    mod_path = Path(args.module)
    data = mod_path / "ModuleData"
    cache = data / "settlements_distance_cache.bin"
    sett = data / "settlements.xml"
    scene = mod_path / "SceneObj" / args.scene / "scene.xscene"
    print(f"Module : {mod_path}")
    print(f"Cache  : {cache}\n")

    errors, warns = [], []

    if not sett.is_file():
        print(f"[FATAL] settlements.xml 不存在: {sett}")
        return 2

    # 期望集合 = 有地图组件的据点 ∩ 有同名场景实体（引擎生成缓存时的实际口径）
    names = scene_entity_names(scene)
    if names is None:
        warns.append(f"场景不可读（{scene}）—— 期望集合退化为「全部有地图组件的据点」")
        names = set()
    expected = set()
    for s in ET.parse(str(sett)).getroot().findall("Settlement"):
        comps = s.find("Components")
        kinds = [c.tag for c in comps] if comps is not None else []
        if not any(k in kinds for k in MAP_COMPONENT_TAGS):
            continue                                    # 服务性据点不进缓存（官方亦如此）
        if names and s.get("id") not in names:
            continue                                    # 无同名场景实体 → 引擎跳过
        expected.add(s.get("id"))

    if not cache.is_file():
        errors.append("距离缓存文件不存在")
        print("== 缓存存在性 ==")
        print("  [ERROR] settlements_distance_cache.bin 不存在 —— 引擎会当场重建（耗时数分钟），"
              "但**当次启动内存里的距离模型是空的** → 那一局家宅/王国家园仍为 null（雷 53）")
        print(f"\nSummary: expected={len(expected)} errors=1")
        return 1

    ids, pairs = parse_cache(cache)
    print(f"== 缓存内容 ==")
    print(f"  缓存据点 {len(ids)} 个 / 据点对 {pairs} 对：{ids}")

    missing = sorted(expected - set(ids))
    extra = sorted(set(ids) - expected)
    if missing:
        errors.append(f"缓存缺据点 {missing}")
        print(f"  [ERROR] 缓存里缺 {missing} —— 缓存过期（改完据点没重生成）")
        print( "          → 重生成：删掉该 bin 让引擎下次启动重建（再下一局生效），"
               "或 ModKit 编辑器变量 ComputeAndSaveSettlementDistanceCache")
    if extra:
        warns.append(f"缓存多出据点 {extra}")
        print(f"  [WARN]  缓存多出 {extra} —— 世界里已无此据点（或它没有场景实体），缓存偏旧")

    print("\n== 据点对（决定全局最大据点距） ==")
    if pairs == 0:
        errors.append("据点对为 0")
        print("  [ERROR] 据点对 = 0 → 全局最大据点距恒 0 → 家宅打分 `1 − 0/0` = NaN"
              " → 家宅/王国家园永远选不中（雷 53：需要 ≥2 个「有场景实体」的据点）")
    else:
        print(f"  [ OK ] {pairs} 对")

    print(f"\nSummary: expected={len(expected)} cached={len(ids)} pairs={pairs} "
          f"errors={len(errors)} warnings={len(warns)}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Taikou 家纹图集表（共享单一事实源）
========================================================================
这张表是「家纹图集」的唯一定义：20 张图集，每张 4×4 = 16 格，
格位 0..15 的行优先序（左上→右下）由引擎写死（`BannerVisualExtensions`：
u = (i%4)*0.25，v = 1-(i/4)*0.25），**不可自行改序**。

谁在用：
  · `Scripts/gen_taikou_banner_icons.py`  → 生成 ModuleData/banner_icons.xml
  · `Scripts/gen_taikou_era_world.py`     → 把 Clan.csv 的 Mon 列翻成 banner_key

🔴 图集来源与授权（2026-09-16）：
  图案 = 从织丰（Shokuho）模组的 AssetPackages 用 tpaccli assetclone 克隆出来的
  2048×2048 家纹图集，改名 taikou_cl_mon_* 落在 Taikou 自己的资产包
  `Modules/Taikou/AssetPackages/taikou_banners.tpac`（自包含，运行期不依赖织丰）。
  **家纹图案本身是公有领域的历史纹章，但这批 PNG 是织丰作者的美术资产 —— 发布前需取得授权**
  （同名字池借用口径：先标出处，发布前谈授权）。

🔴 图标 id 段位（为什么从 840 起）：
  `BannerManager` 合并所有模块的 banner_icons.xml，**图标 id 先到先得**
  （`BannerIconGroup.Deserialize`：已被前面组占用的 id 直接跳过）。
  原版 Native 占 1..535；织丰声明了 536..839（但见下方注）。所以 Taikou 必须落在 840 之后。
  ⚠️ 实测发现（2026-09-16）：织丰**没有**在自己的 SubModule.xml 注册 banner_icons，
  引擎只认注册过的 `<XmlName id="BannerIcons">`（`MBObjectManager.GetMergedXmlForManaged`
  遍历 `XmlResource.XmlInformationList`，该表只由 SubModule.xml 的 XmlNode 填充）
  —— 织丰那 304 个家纹**从未真正加载过**，其家族旗实际只画底色。
  Taikou 这边必须注册，否则同样白干。
"""
import re

# ── 图集清单（顺序 = 图标 id 的分配顺序，**改动会使已配好的 Mon 列整体错位**，非必要勿动）──
ATLASES = [
    "chubu_1", "chubu_2", "chubu_3",
    "chugoku_1", "chugoku_2",
    "custom_1", "custom_2",
    "kanto_1", "kanto_2",
    "kinki_1", "kinki_2",
    "kyushu_1", "kyushu_2",
    "minor_1", "minor_2",
    "religion_1",
    "shikoku_1",
    "tohoku_1", "tohoku_2", "tohoku_3",
]

MATERIAL_PREFIX = "taikou_cl_mon_"
CELLS_PER_ATLAS = 16          # 4×4，引擎写死
ICON_ID_BASE = 840            # 见模块 docstring：必须 > 839
GROUP_ID = 9                  # 原版 1..6，织丰声明 7..8（未生效），Taikou 取 9
GROUP_NAME_KEY = "TAIKOU_banner_group_mon"

# 本地化键的英文 fallback（玩家可见 → 走铁律 13 的 {=KEY}fallback 机制）
GROUP_NAME_FALLBACK = "Taikou Family Crests"

_MON_RE = re.compile(r"^([a-z]+_[0-9]+)_([0-9]|1[0-5])$")


def material_name(atlas):
    """图集短名 → 资产材质名。"""
    if atlas not in ATLASES:
        raise KeyError("未知家纹图集: %s" % atlas)
    return MATERIAL_PREFIX + atlas


def icon_id(atlas, cell):
    """(图集, 格位) → 引擎图标 id。"""
    if atlas not in ATLASES:
        raise KeyError("未知家纹图集: %s" % atlas)
    if not (0 <= cell < CELLS_PER_ATLAS):
        raise ValueError("格位越界: %s" % cell)
    return ICON_ID_BASE + ATLASES.index(atlas) * CELLS_PER_ATLAS + cell


def parse_mon_key(key):
    """Mon 列取值 `kinki_1_5` → ("kinki_1", 5)。空值/非法 → None。"""
    if not key:
        return None
    m = _MON_RE.match(key.strip())
    if not m:
        return None
    atlas, cell = m.group(1), int(m.group(2))
    if atlas not in ATLASES:
        return None
    return atlas, cell


def mon_key_to_icon_id(key):
    """Mon 列取值 → 图标 id；非法返回 None。"""
    parsed = parse_mon_key(key)
    return icon_id(*parsed) if parsed else None


def all_icons():
    """按 (图标id, 材质名, 格位) 顺序产出全部 320 个格位。"""
    for atlas in ATLASES:
        for cell in range(CELLS_PER_ATLAS):
            yield icon_id(atlas, cell), material_name(atlas), cell


def max_icon_id():
    return ICON_ID_BASE + len(ATLASES) * CELLS_PER_ATLAS - 1

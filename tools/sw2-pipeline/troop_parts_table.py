# -*- coding: utf-8 -*-
"""troop_parts_table.py —— 23 个兵种/护卫模型的挑件表（对照 `parts_table.py` 的 28 武将表）。

为什么单开一张表
----------------
`parts_table.TABLE` 的键是 28 个武将、值是甲/脸/兜/发等**人工标注的 idx**。
兵种不是武将（没有脸件、没有换头需求），但甲/兜两条管线**共用同一套选件机制**，
所以给它们一张同构的小表，两个 `build_*.py` 只要换数据源就能直接跑。

🔴 **兜件 idx 是怎么定的**（2026-09-15，`Debug/offline/_troop_part_probe.py` 逐对象头部取景实测）
------------------------------------------------------------------------------------------------
基础兵种的兜**和身体在同一个对象里**（不像护卫那样是干净的独立件），所以判据不是"哪件是兜"，
而是**「哪个对象里装着兜」** —— 甲管线的 `--keep-head-frags` 会按碎片主导骨把兜切出来。

    `helmet` 列 = **装着兜的那个对象的 idx**（不是"兜这个件"）
    `[]` = 该模型没有独立的兜/笠（裸头）

⚠️ **`face_shared=True` = 那个对象里同时装着【脸】**（实测 6 个：弓/下忍/中忍/九州精锐/飞忍/护卫陆）。
   这类走 `--keep-head-frags` 会**把脸一起切进头盔**（脸也绑头骨族）。
   当前处置：**照跑，产物出来人工看**——若确实是"兜 + 一张脸"，退路是走「兜并入甲」，
   即不给它出头盔物品、甲件里保留兜（`--keep-head-frags` 不生效时兜留在甲上，视觉仍然正确）。
   详见 `plans/战国无双换装批量落地.md` 的兵种段。

🔴 **哪个模型是哪个游戏兵种、穿什么 —— 不在本表**（2026-09-16 用户裁定：数据驱动）
------------------------------------------------------------------------------------
兵种的定义与装备一律在 `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/TaikouTroop.csv`
（等级/兵种组/技能组/文化/各槽装备/升级链），武将在 `HeroEquip.csv`。
本表只管 **3D 管线的事**：`slug`（资产命名）+ `helmet`（兜件切出来了没）。
两个物品生成器按「CSV 里有人穿这个 slug」决定该出哪些物品定义 —— 见
`Scripts/taikou_equip_tables.py`。

⚠️ 名字（铠甲/头盔正式名）也不在本表，从 `Sw2OfficialNames.csv` 取 —— 那张表由
   `extract_official_names.py` 从上游 `web/armor.html` 提取（铁律 28：源 → 生成器 → CSV）。

数据来源（都在 `Debug/offline/`，离线产物不进 git）
  · `sw2_parts/<模型>_parts.csv`   —— 零件表（`build_helmets.helmet_subs` 靠它把 idx 翻成网格名）
  · `sw2_census/<模型>_census.csv` —— 骨普查（甲管线 `plan_for` 靠它挑甲件）
"""
TROOP_TABLE = {
    # ── 足轻 / 侍 系 ────────────────────────────────────────────────
    "L250_SOLDIER1": dict(slug="troop_yari_ashigaru", body=[],   helmet=[3], kimono=[1], face_shared=False),  # 枪足轻：阵笠（idx3 同件还有裙，在头区外）
    "L251_SOLDIER2": dict(slug="troop_katana_ashigaru", body=[], helmet=[2], face_shared=False),  # 刀足轻：大斗笠 + 胸甲同件
    "L252_SOLDIER3": dict(slug="troop_samurai", body=[],         helmet=[2], face_shared=False),  # 侍：头巾 + 胸甲同件
    "L253_SOLDIER4": dict(slug="troop_elite_ashigaru", body=[],  helmet=[2], face_shared=False),  # 精锐足轻：铁兜 + 胴 + 袖同件
    "L254_SOLDIER5": dict(slug="troop_taisho", body=[],          helmet=[2], face_shared=False),  # 侍大将：粉兜（idx4 是垂纱，不是兜）
    "L255_ARCHER":   dict(pending_helmet=[0], slug="troop_yumi_ashigaru", body=[],   helmet=[], face_shared=True),   # 弓足轻：尖顶兜 + 身体同件
    "L256_GUNNER":   dict(slug="troop_teppo_ashigaru", body=[],  helmet=[2], face_shared=False),  # 铁炮足轻：菅笠 + 手甲同件
    # ── 忍者 系 ────────────────────────────────────────────────────
    "L257_NINJA1":   dict(pending_helmet=[0], slug="troop_genin", body=[],           helmet=[], face_shared=True),   # 下忍：头罩 + 覆面 + 身体同件
    "L258_NINJA2":   dict(pending_helmet=[0], slug="troop_chunin", body=[],          helmet=[], face_shared=True),   # 中忍：覆面 + 身体同件
    "L262_TOTSUNIN": dict(slug="troop_totsunin", body=[],        helmet=[2], face_shared=False),  # 突忍：赤面具
    "L263_TOBININ":  dict(pending_helmet=[0], slug="troop_tobinin", body=[],         helmet=[], face_shared=True),   # 飞忍：覆面 + 身体同件
    "L264_SENNIN":   dict(slug="troop_sennin", body=[],          helmet=[2], face_shared=False),  # 旋忍：银白大盔 + 身甲同件
    "L265_HAZENIN":  dict(slug="troop_hazenin", body=[],         helmet=[5], face_shared=False),  # 破忍：黑漆筋兜 + 胸甲同件
    "L266_NINJA3":   dict(slug="troop_jonin", body=[],           helmet=[2], face_shared=False),  # 上忍：额当 + 前立（头罩在 idx0，与脸同件）
    # ── 其它兵种（**没有对应兵种**；九州兵那套已给武将穿，其余暂无人穿）─────────
    "L259_NOUMIN":   dict(slug="troop_noumin", body=[],          helmet=[],  face_shared=False),  # 农民：裸头（素肌 + 作务衣）
    "L260_KYUSHU1":  dict(slug="troop_kyushu", body=[],          helmet=[2], face_shared=False),  # 九州兵：阵笠 + 大袖同件
    "L261_KYUSHU2":  dict(pending_helmet=[0], slug="troop_kyushu_elite", body=[],    helmet=[], face_shared=True),   # 九州兵精锐：尖顶兜 + 身体同件
    # ── 护卫（兜大多是干净的独立件）──────────────────────────────
    "L300_guard1":   dict(slug="troop_guard1", body=[],          helmet=[6], face_shared=False),  # 护卫壹：金双叉前立兜
    "L301_guard2":   dict(slug="troop_guard2", body=[],          helmet=[],  face_shared=False),  # 护卫贰：裸头 + 顶髻（无兜）
    "L302_guard3":   dict(slug="troop_guard3",          helmet=[6], face_shared=False),  # 护卫叁：金额当 + 肩
    "L303_guard4":   dict(slug="troop_guard4", body=[],          helmet=[6], face_shared=False),  # 护卫肆：金漆大兜
    "L304_guard5":   dict(slug="troop_guard5", body=[],          helmet=[7], face_shared=False),  # 护卫伍：兜 + 垂纱
    "L305_guard6":   dict(pending_helmet=[4], slug="troop_guard6", body=[],          helmet=[], face_shared=True),   # 护卫陆：黑覆面（与脸同件）
}


def row_of(key, table):
    """两张表统一取行：武将在 `parts_table.TABLE`，兵种在本表。"""
    if key in table:
        return table[key]
    return TROOP_TABLE[key]

# -*- coding: utf-8 -*-
"""export_spell_table.py -- 把 FCS 的技能表导出来（回答：**每个特效是哪个技能在用**）。

【为什么要它】
  99 个粒子特效必须挂到「哪个技能、什么时机」上才有意义（冲锋 / 蓄力 / 命中 / 持续 / 增益）。
  这个信息全在 FCS 的技能表 `DT_SpellsInfo`（行 = 法术，字段 = 法术蓝图 / VFX / 音效 / 图标 / 动画）。

【跑法】（与 export_t3d_all.py 同款，UE 无头；约 30~60 秒）
  MSYS_NO_PATHCONV=1 "D:/UNREAL/UE_4.27/Engine/Binaries/Win64/UE4Editor.exe" \
    "D:/UEProjects/【UE5】FlexibleCombatSystem/FlexibleCombatSystem.uproject" \
    -run=pythonscript -script="<本文件>" -unattended -nosplash -nullrhi -stdout
  不看退出码（引擎把 warning 记成 error）；看 OUT_DIR 里的 csv 在不在。

【产物】OUT_DIR（默认 数据根/output/spells_export）= 每张表的 CSV。
"""
import unreal
import os
import traceback

# 🔴 本文件跑在 UE 自带 python 里，sys.path 不可控 → 与 export_t3d_all.py 一样，
#    路径写死在文件头（改数据根 = 改这一行或设环境变量）。
OUT_DIR = os.environ.get("BM_SPELL_OUT", r"D:/BrainMaker/骑砍2粒子特效复刻/output/spells_export")
TABLES = [
    "/Game/FlexibleCombatSystem/DataTables/MagicAsset/DT_SpellsInfo",
    "/Game/FlexibleCombatSystem/DataTables/MagicAsset/DT_Trainer-AllSpells",
]


def L(m):
    s = str(m)
    print(s, flush=True)
    try:
        unreal.log(s)
    except Exception:
        pass


def main():
    L("=== 导出技能表 ===")
    os.makedirs(OUT_DIR, exist_ok=True)

    for path in TABLES:
        L("---- %s" % path)
        try:
            dt = unreal.load_asset(path)
        except Exception as e:
            L("  load 失败: %s" % e)
            continue
        if dt is None:
            L("  !! 载入为空（路径不对 / 资产不在）")
            continue
        L("  资产 = %s" % dt)

        # ① 行名（顺带证明表能读到）
        try:
            rows = unreal.DataTableFunctionLibrary.get_data_table_row_names(dt)
            L("  行数 = %d" % len(rows))
            L("  行名 = %s" % ", ".join(str(r) for r in rows))
        except Exception as e:
            L("  取行名失败: %s" % e)

        # ② 导出成 CSV（AssetTools 会挑该资产的默认导出器：DataTable -> CSV）
        try:
            at = unreal.AssetToolsHelpers.get_asset_tools()
            at.export_assets([path], OUT_DIR)
            L("  export_assets 已调用 -> %s" % OUT_DIR)
        except Exception as e:
            L("  export_assets 失败: %s" % e)

    # ③ 看落盘结果
    try:
        got = sorted(os.listdir(OUT_DIR))
        L("OUT_DIR 内容: %s" % ", ".join(got) if got else "OUT_DIR 空")
    except Exception as e:
        L("列目录失败: %s" % e)
    L("=== 完成 ===")


try:
    main()
except Exception:
    L("FATAL\n" + traceback.format_exc())

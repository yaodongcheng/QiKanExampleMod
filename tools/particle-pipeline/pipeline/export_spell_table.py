# -*- coding: utf-8 -*-
"""export_spell_table.py -- 把 FCS 的技能表导出来（回答：**每个特效是哪个技能在用**）。

【为什么要它】
  99 个粒子特效必须挂到「哪个技能、什么时机」上才有意义（蓄力 / 命中 / 持续 / 增益 / 屏障）。
  这些信息全在 FCS 的技能表 `DT_SpellsInfo`：行 = 法术，字段 = 法术蓝图 / VFX / 音效 / 图标 / 动画。

【跑法】（UE 无头；约 15~30 秒）
  MSYS_NO_PATHCONV=1 "D:/UNREAL/UE_4.27/Engine/Binaries/Win64/UE4Editor.exe" \
    "D:/UEProjects/【UE5】FlexibleCombatSystem/FlexibleCombatSystem.uproject" \
    -run=pythonscript -script="<本文件>" -unattended -nosplash -nullrhi -stdout
  ⚠️ 路径必须**无空格**（`Mount & Blade...` 会让 UE 解析不到文件，退化成"把路径当代码执行"）→
     所以从仓库跑的话用 launcher 先拷到无空格路径，或直接用 `pipeline/run_ue_script.py`。

【🔴 为什么结果写文件而不是打印】
  实测：这种无头调用下脚本的 `print()` 与 `unreal.log()` **都不会进日志**
  （脚本本身跑得好好的 —— 用写文件验证过），日志里只有引擎自己的行。
  所以本脚本把**所有**诊断与结果写进 OUT_DIR/_report.json，看那个文件。

【产物】OUT_DIR（默认 数据根/output/spells_export/）：
  _report.json   诊断（哪些 API 可用、行数、每行字段）
  spells.json    技能表（行名 -> 字段字典）
"""
import unreal
import json
import os
import traceback

OUT_DIR = os.environ.get("BM_SPELL_OUT", r"D:/BrainMaker/骑砍2粒子特效复刻/output/spells_export")
DT_MAIN = "/Game/FlexibleCombatSystem/DataTables/MagicAsset/DT_SpellsInfo"
DT_EXTRA = ["/Game/FlexibleCombatSystem/DataTables/MagicAsset/DT_Trainer-AllSpells"]

report = {"steps": [], "api": {}, "tables": {}}

# S_SpellInfo 的字段名（从 `Content/FlexibleCombatSystem/Structs/Magic/S_SpellInfo.uasset`
# 的字符串表里抽出来的候选）。Python 侧**没有** DataTable 的 CSV/JSON 导出器
# （unreal 只暴露了 FBX/T3D/HDR 那批），所以只能按列名取值 —— 名字不对就跳过，不编。
CAND_COLS = [
    # 身份 / 展示
    "Name", "DisplayName", "FriendlyName", "Description", "SpellDescription", "Tooltip",
    "AbilityImage", "SubCategory", "Rank",
    # 数值
    "SpellCategory", "DamageType", "BaseDamage", "SpellDamage", "SpellAmount", "GoldCost",
    "ManaCost", "Cooldown", "Range", "Radius", "SpellRadius", "LevelRequirement",
    # 演出（这几列里嵌着 VFX）
    "Animation", "AnimMontage", "PolymorphAnimation", "SocketAttachName",
    "SpellChannel", "SpellChannelPS", "SpellProjectile", "SpellPlacement",
    "SpellCharge", "SpellChargePS", "SpellHit", "SpellHitPS", "SpellPS", "SpellLoop",
    "SpellFire", "SpellDurations", "DurationSpells", "SpellDuration", "SpellDurationType",
    "SpellSoundFX", "SpellSounds", "SpellSpawnWeapon", "SpellRequiresChannel",
    "SpellParentClass", "WeaponVFX", "WeaponVFXInfo",
    # 伤害/增益明细
    "SpecialDamageInfo", "SpellTrainerInfo", "SpellInfo",
    "BuffType", "BuffPower", "BuffDuration", "BarrierReflectDamage", "BarrierHitDamage",
    "BarrierDamageType", "BarrierDamageRank", "SpawnWeaponType", "SpawnedWeapon",
    "RuneActorName", "SlowDuration", "SlowPercentage", "FreezeDuration",
    "PoisonDamage", "PoisonDuration", "Critical", "PolymorphMesh", "PolymorphDuration",
    "TickFrequency", "TickDuration", "TickInArea",
]


def step(msg):
    report["steps"].append(str(msg))


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    # ⓪ 这版引擎有哪些 DataTable API（不猜，问它）
    report["api"]["DataTableFunctionLibrary"] = [
        x for x in dir(unreal.DataTableFunctionLibrary) if not x.startswith("_")]
    report["api"]["unreal_DataTableNames"] = [x for x in dir(unreal) if "DataTable" in x or "Exporter" in x]
    step("introspect 完成")

    for path in [DT_MAIN] + DT_EXTRA:
        tinfo = {"path": path, "rows": [], "cols_ok": [], "cols_bad": [], "data": {}}
        report["tables"][path] = tinfo
        try:
            dt = unreal.load_asset(path)
        except Exception as e:
            tinfo["error"] = "load 失败: %s" % e
            continue
        if dt is None:
            tinfo["error"] = "载入为空"
            continue
        # ① 行名
        try:
            rows = [str(r) for r in unreal.DataTableFunctionLibrary.get_data_table_row_names(dt)]
            tinfo["rows"] = rows
            step("%s 行数=%d" % (path, len(rows)))
        except Exception as e:
            tinfo["error"] = "取行名失败: %s" % e
            continue
        # ② 按列取值（列名对了才收）
        col_vals = {}
        for col in CAND_COLS:
            try:
                vals = unreal.DataTableFunctionLibrary.get_data_table_column_as_string(dt, col)
            except Exception as e:
                tinfo["cols_bad"].append("%s (%s)" % (col, e))
                continue
            vals = [str(v) for v in vals]
            if len(vals) != len(rows) or all(v.strip() in ("", "None") for v in vals):
                tinfo["cols_bad"].append(col)
                continue
            col_vals[col] = vals
            tinfo["cols_ok"].append(col)
        step("%s 取到 %d 列: %s" % (path, len(col_vals), ", ".join(col_vals)))
        # ③ 拼成 行 -> {列: 值}
        for i, r in enumerate(rows):
            tinfo["data"][r] = {c: v[i] for c, v in col_vals.items()}

    with open(os.path.join(OUT_DIR, "_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    main_data = report["tables"].get(DT_MAIN, {}).get("data", {})
    with open(os.path.join(OUT_DIR, "spells.json"), "w", encoding="utf-8") as f:
        json.dump({"columns": report["tables"].get(DT_MAIN, {}).get("cols_ok", []),
                   "rows": main_data}, f, ensure_ascii=False, indent=1)


try:
    main()
except Exception:
    report["fatal"] = traceback.format_exc()
    try:
        os.makedirs(OUT_DIR, exist_ok=True)
        with open(os.path.join(OUT_DIR, "_report.json"), "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
    except Exception:
        pass

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_mesh_names.py — 回读 FBX，验「对象名 / 网格名 / 材质名」三件套（验收用）
============================================================================
    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python check_mesh_names.py -- <fbx> [<fbx> ...]

【为什么要有它】
编辑器 Import FBX 时是**按 FBX 里的材质名建材质**的（`build_sphere_shell.py` 头部写明：
"Import 上面的 FBX ⇒ 自动建网格 + 同名材质"）。所以材质名错了 = 导入后编辑器里那个名字也错，
再往下（铁律 32：网格引用的材质名必须是已定义的那个）就是白板 / 找不到材质。

本脚本把 FBX 重新导入 Blender，逐对象打出三件套并**判定**：
    · 对象名 == 网格名 == 材质名（与文件名一致）
    · 材质只有一个（多材质 = 导入后多建几个资源，容易乱）
    · 名字不以 `lwn_` 开头 → 警告（可能撞原版资源名）
    · 🔴 带 `.001`/`.002` 之类 **Blender 去重后缀 → 直接判不合格**
      （铁律 32 坑①：各自 new 一个同名材质会被 Blender 去重成 `<名>.001`，
       导出写的就是带后缀的名字，编辑器照样找不到 = 白板没修掉）

退出码：0 = 全合格，1 = 有不合格项（可挂进自检链）。
"""
import os
import sys

import bpy

DEDUP_SUFFIXES = (".001", ".002", ".003", ".004")


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("[STOP] 用法：blender -b --python check_mesh_names.py -- <fbx> [<fbx> ...]")
        sys.exit(2)

    bad = 0
    for path in args:
        if not os.path.isfile(path):
            print(f"[STOP] 找不到 {path}")
            bad += 1
            continue
        want = os.path.splitext(os.path.basename(path))[0]      # 期望名 = 文件名
        bpy.ops.wm.read_factory_settings(use_empty=True)
        try:
            bpy.ops.import_scene.fbx(filepath=path)
        except Exception as ex:
            print(f"[STOP] {want} 导入失败：{ex}")
            bad += 1
            continue

        for ob in bpy.data.objects:
            mats = [m.name for m in ob.data.materials]
            issues = []
            if ob.name != want:
                issues.append(f"对象名≠文件名({want})")
            if ob.data.name != want:
                issues.append(f"网格名≠文件名({want})")
            if len(mats) != 1:
                issues.append(f"材质数={len(mats)}（应为 1）")
            for mn in mats:
                if mn != want:
                    issues.append(f"材质名≠文件名({want})")
                if not mn.startswith("lwn_"):
                    issues.append("材质名不以 lwn_ 开头")
                if any(s in mn for s in DEDUP_SUFFIXES):
                    issues.append("🔴 带 Blender 去重后缀")

            # 🔴 平滑/硬边：**不能看 `polygon.use_smooth`** —— 实测（2026-09-27）FBX 往返后
            #    它**恒为 True**（Blender 是用"拆分法线"表达硬边的，跟这个标志无关）。
            #    可靠判据 = 逐面比较「面法线」与「该面各面角的法线」：全一致 = 硬边。
            #    ⚠️ 这一项**只报数值、不判合格** —— 球该平滑、薄片该硬边，脚本无从知道你的意图。
            me = ob.data
            total = len(me.polygons)
            smooth_n = None
            if total:
                try:
                    cn = me.corner_normals
                    n_soft = 0
                    for p in me.polygons:
                        pn = p.normal
                        for li in p.loop_indices:
                            if (cn[li].vector - pn).length > 0.02:
                                n_soft += 1
                                break
                    smooth_n = n_soft
                except Exception:
                    smooth_n = None
            shape_txt = (f"平滑 {smooth_n}/{total} 面" if smooth_n is not None
                         else f"面 {total}（着色判定不可用）")

            flag = ("  ✗ " + " · ".join(issues)) if issues else "  ✓ 合格"
            print(f"  [{want}] 对象={ob.name}  网格={ob.data.name}  材质={mats}  {shape_txt}{flag}")
            if issues:
                bad += 1

    print(f"  === 不合格 {bad} 项 ===")
    sys.exit(1 if bad else 0)


main()

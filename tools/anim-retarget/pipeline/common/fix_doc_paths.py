#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""文档路径修复器：把重组前的旧路径（ue5_失败原因复查/*, web_slim/*, out/*, web/*, OpenTrf/* …）
   批量改写成重组后的新结构（input/ pipeline/ output/ viewer/ docs/ _legacy/）。
   做法：只替换「被正则识别为路径 token 且命中映射表」的整段，绝不做子串替换，
        因此不会误伤 output/ 这类与 out/ 相似的串。
   用法： python pipeline/common/fix_doc_paths.py [--dry]
"""
import os, re, sys, shutil, datetime

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BS = chr(92)

# 旧路径(整段) -> 新路径；含"文件被改名"的语义映射
MAP = {
    # ── 文档 ──
    "ue5_失败原因复查/UE5重定向失败原因复查.md": "docs/UE5重定向失败原因复查.md",
    "ue5_失败原因复查/web/说明_查看器.md":       "_legacy/old_viewers/ue_basic/说明_查看器.md",
    "web_flight/说明_飞行动画查看器.md":          "_legacy/old_viewers/ue_flight/说明_飞行动画查看器.md",
    "战国无双2铁炮兵_p006_重定向/README_可行性结论.md": "docs/SW2_可行性结论.md",
    "骑砍2动画重定向/README_复盘.md":             "docs/README_复盘.md",
    "docs/骨骼经验.md":                            "docs/README_骨骼经验.md",
    "教程存档/README.md":                          "docs/教程存档/README.md",
    "教程存档/trf_skeleton_animation_exporter.py": "docs/教程存档/trf_skeleton_animation_exporter.py",
    "_deprecated/README.md":                       "_legacy/trf_deprecated/README.md",
    "OpenTrf/README.md":                           "docs/TRF规范.md",
    # ── 脚本（含改名） ──
    "UEAnims/exporter/ue_export_fbx.py":           "pipeline/common/export_ue_fbx.py",
    "UEAnims/exporter/ue_export_slim.py":          "pipeline/common/export_ue_fbx_slim.py",
    "ue5_失败原因复查/ue_batch_glb.py":            "pipeline/common/fbx_to_glb.py",
    "ue5_失败原因复查/glb_check.py":               "pipeline/common/check_quality.py",
    "ue5_失败原因复查/ue_align.py":                "pipeline/rigs/ue_mannequin/retarget.py",
    "web_slim/make_manifest.py":                   "pipeline/common/make_manifest.py",
    "web_slim/verify_shards.mjs":                  "pipeline/common/verify_shards.mjs",
    "work_slim/shot.py":                           "pipeline/common/shot_headless.py",
    "战国无双2铁炮兵_p006_重定向/retarget_sw2_to_bannerlord.py": "pipeline/rigs/sw2_gunner/retarget.py",
    "战国无双2铁炮兵_p006_重定向/reexport_for_modkit.py":       "pipeline/rigs/sw2_gunner/reexport_for_modkit.py",
    "_deprecated/fbx_to_trf.py":                   "pipeline/common/fbx_to_trf.py",
    "_deprecated/retarget_to_official.py":         "_legacy/trf_deprecated/retarget_to_official.py",
    "OpenTrf/fbx_to_trf_fixed.py":                 "pipeline/common/fbx_to_trf.py",
    "../fbx_to_trf_fixed.py":                      "../../pipeline/common/fbx_to_trf.py",
    "../README.md":                                "../../README.md",
    # ── 资产 / 数据 ──
    "UEAnims/anim_inventory":                      "input/inventory",
    "anim_inventory":                              "input/inventory",
    "UEAnims/exported_slim":                       "input/source/ue_mannequin/clips_slim",
    "UEAnims/exported_slim/export_trace_slim.log": "input/source/ue_mannequin/clips_slim/export_trace_slim.log",
    "UEAnims/exported_fbx":                        "input/source/ue_mannequin/clips_basic",
    "UEAnims/exported_flight":                     "input/source/ue_mannequin/clips_flight",
    "UEAnims/mannequin_src/Mannequin_src.fbx":     "input/source/ue_mannequin/rig/Mannequin_src.fbx",
    "human/human_lod_4.fbx":                       "input/target/bannerlord/human_lod_4.fbx",
    "human/human_lod_4.fbx".replace("human/",""):  "input/target/bannerlord/human_lod_4.fbx",
    "human_lod_4.fbx":                             "input/target/bannerlord/human_lod_4.fbx",
    "body/body_female_a.fbx":                      "input/target/bannerlord/body/body_female_a.fbx",
    "web/anim/L256_GUNNER_anim.gltf":              "input/source/sw2_gunner/L256_GUNNER_anim.gltf",
    "datasets/index.json":                         "viewer/datasets/index.json",
    # ── viewer / glb ──
    "web_flight/bannerlord_flight.glb":            "output/glb/ue_flight/bannerlord_flight.glb",
    "web_flight/serve.py":                         "viewer/serve.py",
    "web_flight/viewer.html":                      "viewer/viewer.html",
    "sw2_basic/viewer.html":                       "_legacy/old_viewers/sw2_basic/viewer.html",
    "web/assets/bannerlord_slim.glb":              "viewer/datasets/ue_slim/assets/bannerlord_ground.glb",
    "web/assets/ue_mannequin_slim.glb":            "viewer/datasets/ue_slim/assets/ue_mannequin_ground.glb",
    "web/assets/xxx.glb":                          "viewer/datasets/ue_slim/assets/xxx.glb",
    "work_slim/verify":                            "output/verify/slim_verify",
    "work_slim/verify/对照表_修复后8段.png":             "output/verify/slim_verify/对照表_修复后8段.png",
    # ── out/ 旧产物 ──
    "out/dump_align.json":                         "output/verify/dump_align.json",
    "out/sheet_front.png":                         "output/verify/sheet_front.png",
    "out/sheet_side.png":                          "output/verify/sheet_side.png",
    "out/sw2_gunner_p006_alig.fbx":                "output/fbx/sw2_gunner_p006_alig.fbx",
    "out/sw2_gunner_p006_alig.trf":                "output/trf/sw2_gunner_p006_alig.trf",
    "out/sw2_gunner_p006_abso.fbx":                "_legacy/algorithm_archive/sw2_gunner_p006_abso.fbx",
    "out/sw2_gunner_p006_delt.fbx":                "_legacy/algorithm_archive/sw2_gunner_p006_delt.fbx",
    "out/bac_ortho.fbx":                           "_legacy/algorithm_archive/bac_ortho.fbx",
    "out/bac_sw2_p006.fbx":                        "_legacy/algorithm_archive/bac_sw2_p006.fbx",
    "out/biosculpt_sw2_p006.fbx":                  "_legacy/algorithm_archive/biosculpt_sw2_p006.fbx",
    "output/example/sw2_gunner_p006_alig.fbx":     "output/fbx/sw2_gunner_p006_alig.fbx",
    "output/example/sw2_gunner_p006_alig_abs.trf": "output/trf/sw2_gunner_p006_alig_abs.trf",
    "../output/example/sw2_gunner_p006_alig_abs.trf": "../../output/trf/sw2_gunner_p006_alig_abs.trf",
    "_deprecated/sw2_gunner_p006_alig.trf":        "output/trf/sw2_gunner_p006_alig.trf",
    "_deprecated/sw2_gunner_p006_retarget.trf":    "_legacy/trf_deprecated/sw2_gunner_p006_retarget.trf",
    "preview/bannerlord_anim_preview_high.blend":  "_legacy/output_2026-09-09/preview/bannerlord_anim_preview_high.blend",
    "preview/并排对比_源vs骑砍_v2.blend":             "_legacy/output_2026-09-09/preview/并排对比_源vs骑砍_v2.blend",
    "关节网络v2_小白人vs骑砍lod4_front/q34.png":      "_legacy/output_2026-09-09/preview/关节网络v2_小白人vs骑砍lod4_q34.png",
}
DOCS = ["README.md", "docs/历史_交接与TODO.md", "docs/README_骨骼经验.md", "docs/TRF规范.md",
        "docs/README_素材库盘点与状态机设计.md", "docs/UE5重定向失败原因复查.md",
        "docs/SW2_可行性结论.md", "docs/README_复盘.md", "docs/教程存档/README.md",
        "_legacy/old_viewers/README.md"]
PAT = re.compile(r'(?<![\w/])((?:\.\./|\./)?(?:[A-Za-z0-9_\u4e00-\u9fff\.\-]+/)+[A-Za-z0-9_\u4e00-\u9fff\.\-]+\.[A-Za-z0-9]{1,6})')

def fix_text(t):
    hits = {}
    def sub(m):
        tok = m.group(1)
        new = MAP.get(tok)
        if new and not os.path.exists(tok):
            hits[tok] = new
            return new
        return tok
    return PAT.sub(sub, t), hits

def main():
    dry = "--dry" in sys.argv
    bakdir = os.path.join(ROOT, "_legacy", "path_fix_backup_" + datetime.datetime.now().strftime("%Y%m%d_%H%M"))
    total = 0
    for d in DOCS:
        p = os.path.join(ROOT, d)
        if not os.path.exists(p): print("  ! 跳过（不存在）", d); continue
        raw = open(p, "rb").read()
        t = raw.decode("utf-8")
        nt, hits = fix_text(t)
        if not hits: print("  = %-44s 无需修改" % d); continue
        total += len(hits)
        print("  ✓ %-44s 修 %2d 条" % (d, len(hits)))
        for k, v in sorted(hits.items()): print("        %-46s -> %s" % (k, v))
        if not dry:
            os.makedirs(os.path.join(bakdir, os.path.dirname(d)), exist_ok=True)
            shutil.copy2(p, os.path.join(bakdir, d))
            # 保持原行尾风格：把本文档原本的换行原样拼回去，避免 LF/CRLF 被规范化
            nl = chr(13)+chr(10) if (chr(13)+chr(10)) in t else chr(10)
            enc = nt.replace(chr(10), nl).replace(nl+nl, nl).encode("utf-8")
            if enc != raw: open(p, "wb").write(enc)
    print("\n合计修复 %d 处引用%s" % (total, "（dry-run，未写入）" if dry else "；备份在 " + os.path.relpath(bakdir, ROOT)))

main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""骑砍2 动画重定向 · 统一入口（调度层，不依赖 bpy）

一次产出：
    output/fbx/<name>.fbx    ModKit 规格（28 骨 / 只骨架 / Z-up / cm / 根名 human_skeleton_notused）
    output/trf/<name>.trf    ★ 最终交付物
                             （语义：旋转 = 绝对局部变换；平移 = 相对静止姿势的增量。
                               两条不一样，写反的后果见 common/fbx_to_trf.py 文件头。）

用法（两边都能跑；数据面不在仓库里，脚本会自动改用数据根）：
    python pipeline/run_retarget.py --rig sw2_gunner    --clip p006   --name sw2_gunner_p006_alig
    python pipeline/run_retarget.py --rig ue_mannequin  --clip 010_01 --name ue_010_01

    🔴 本仓库里的这份是【代码真身】，数据面（input/ output/）在
       D:\\BrainMaker\\骑砍2动画重定向\\ —— 那边是同名 junction，指向本文件。
       从哪边跑都是同一份代码、同一份数据。

分层约定：
    pipeline/common/            通用层（骨架无关：TRF 导出 / ModKit 体检 / GLB 烘焙 / 质量校验）
    pipeline/rigs/<rig>/        骨架专有层（映射表 + 重定向脚本，只放差异）
    pipeline/run_retarget.py    调度层（本文件）：选 rig → 组命令 → 跑 blender → 校验产物
"""
import argparse
import os
import subprocess
import sys

# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

# --- 数据根：数据面（input/ output/）不在仓库里，在 D:\BrainMaker\骑砍2动画重定向\ ---
#     那边是同名 junction，指向本文件同一份代码。
#     _project_root() 找不到目标时会【静默】回落到一个错的根（实测落到游戏根目录），
#     所以这里显式校验一次；本仓库路径下解析不出数据根就自动改用 DATA_ROOT。
DATA_ROOT = r"D:\BrainMaker\骑砍2动画重定向"
def _has_data(p):
    return _os.path.isdir(_os.path.join(p, "input")) and _os.path.isdir(_os.path.join(p, "output"))
if not _has_data(PROJECT_ROOT):
    if _has_data(DATA_ROOT):
        print("CONVERT_INFO: 数据面改用数据根 %s（本仓库不含数据，那边是 junction）" % DATA_ROOT)
        PROJECT_ROOT = DATA_ROOT
    else:
        sys.stderr.write(
            "PROJECT_ROOT 解析失败：%s\n"
            "  该目录下没有 input/ 与 output/，备用的数据根也没有：\n"
            "      %s\n"
            "  数据面（源动画 / 产物）不在仓库里。请确认该目录存在，\n"
            "  或把 DATA_ROOT 改成实际位置后重试。\n"
            % (PROJECT_ROOT, DATA_ROOT))
        sys.exit(2)

DEFAULT_BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"

# 骨架专有层注册表：新增一个源骨架，只加一条
RIGS = {
    "sw2_gunner": {
        "script": "pipeline/rigs/sw2_gunner/retarget.py",
        "map": "pipeline/rigs/sw2_gunner/map.json",
        "source_kind": "gltf",
        "clip_arg": "--action",
        "desc": "战国无双2 铁炮兵 -> 骑砍2（源为 glTF）",
    },
    "ue_mannequin": {
        "script": "pipeline/rigs/ue_mannequin/retarget.py",
        "map": "pipeline/rigs/ue_mannequin/map.json",
        "source_kind": "fbx",
        "clip_arg": "--clip",
        "desc": "UE5 小白人 -> 骑砍2（源为 FBX）",
    },
}


def main():
    ap = argparse.ArgumentParser(description="骑砍2 动画重定向统一入口（FBX + TRF 一次产出）")
    ap.add_argument("--rig", required=True, choices=sorted(RIGS.keys()), help="骨架专有配置名")
    ap.add_argument("--clip", required=True, help="动画/动作名（如 p006 / 010_01）")
    ap.add_argument("--name", help="输出名，缺省 = <rig>_<clip>")
    ap.add_argument("--out-dir", default="output", help="输出根目录（默认 output/）")
    ap.add_argument("--pose", default="align", choices=["align", "delta", "absolute"],
                    help="重定向算法（默认 align，唯一交付模式）")
    ap.add_argument("--pelvis", default=None, help="骨盆策略 ground|src|delta|none")
    ap.add_argument("--animdir", default=None, help="源动画目录（缺省用 rig 脚本内置默认）")
    ap.add_argument("--no-trf", action="store_true", help="只要 FBX，不导 TRF（默认两者都产）")
    ap.add_argument("--blender", default=os.environ.get("BLENDER", DEFAULT_BLENDER))
    ap.add_argument("--dry-run", action="store_true", help="只打印命令")
    a = ap.parse_args()

    rig = RIGS[a.rig]
    name = a.name or ("%s_%s" % (a.rig, a.clip))
    outdir = a.out_dir if os.path.isabs(a.out_dir) else os.path.join(PROJECT_ROOT, a.out_dir)
    script = os.path.join(PROJECT_ROOT, rig["script"])

    cmd = [a.blender, "-b", "--python", script, "--",
           rig["clip_arg"], a.clip, "--name", name, "--outdir", outdir, "--pose", a.pose]
    if a.pelvis:
        cmd += ["--pelvis", a.pelvis]
    if a.animdir:
        cmd += ["--animdir", a.animdir]
    if a.no_trf:
        cmd += ["--no_trf", "true"]

    print("=" * 68)
    print("  rig      : %s  (%s)" % (a.rig, rig["desc"]))
    print("  clip     : %s" % a.clip)
    print("  name     : %s" % name)
    print("  pose     : %s%s" % (a.pose, " (唯一交付模式)" if a.pose == "align" else " [非交付模式]"))
    print("  output   : %s" % outdir)
    print("  TRF      : %s" % ("跳过" if a.no_trf else "产出（最终交付物）"))
    print("=" * 68)
    if a.dry_run:
        print(" ".join('"%s"' % c if " " in c else c for c in cmd))
        return 0

    if not os.path.isfile(a.blender):
        print("[X] 找不到 Blender：%s（用 --blender 指定）" % a.blender)
        return 2
    import time as _t
    _start = _t.time()
    rc = subprocess.call(cmd)
    if rc != 0:
        print("[X] 重定向失败，返回码 %d" % rc)
        return rc

    # 产物归位：FBX -> output/fbx/  TRF -> output/trf/（TRF 是最终交付物，单独分层）
    import shutil
    dst_fbx = os.path.join(PROJECT_ROOT, "output", "fbx")
    dst_trf = os.path.join(PROJECT_ROOT, "output", "trf")
    os.makedirs(dst_fbx, exist_ok=True)
    os.makedirs(dst_trf, exist_ok=True)
    fbx = os.path.join(dst_fbx, name + ".fbx")
    trf = os.path.join(dst_trf, name + ".trf")
    _s = os.path.join(outdir, name + ".fbx")
    if os.path.isfile(_s):
        shutil.move(_s, fbx)
    _s = os.path.join(outdir, name + ".trf")
    if os.path.isfile(_s):
        shutil.move(_s, trf)
    def _fresh(fp):
        return os.path.isfile(fp) and os.path.getmtime(fp) >= _start - 5
    产物 = [(fbx, _fresh(fbx))]
    if not a.no_trf:
        产物.append((trf, _fresh(trf)))
    print("-" * 68)
    ok = True
    for p, e in 产物:
        print("  %s %s" % ("[OK]" if e else "[X] ", p))
        ok = ok and e
    if not a.no_trf and os.path.isfile(trf):
        print("  TRF 自检请确认脚本输出含 CHECK_OK（<0.5°）与 CHECK_POS（首帧 ≈ 0，纯增量）")
    print("-" * 68)
    print("  下一步：ModKit 导入 → 填 Source 1 / Source 2 = 该 TRF 的帧范围")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

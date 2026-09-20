# -*- coding: utf-8 -*-
"""复查 UE5 -> 骑砍2 重定向：时长/帧率 + 姿态(肢段方向)。
   姿态用"穷举所有朝向/镜像变换取最优"来比，保证对任何结果都公平。
   用法: blender -b --python ue_check.py -- --clip 010_01 --cands "标签:子目录,标签:子目录"
"""
import bpy, sys, os, json, math, argparse
from mathutils import Matrix

# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

ROOT = PROJECT_ROOT
MAP  = json.load(open(os.path.join(ROOT,"pipeline","rigs","ue_mannequin","map.json"), encoding="utf-8"))
PAIRS = MAP["bone_map"]

def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(); ap.add_argument("--clip", default="010_01")
    ap.add_argument("--cands", default="_legacy/output_2026-09-09/output_world")
    ap.add_argument("--sub", default="")
    return ap.parse_args(a)
args = parse()
SUB = ("/" + args.sub) if args.sub else ""

def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next((o for o in bpy.data.objects if o.type=='ARMATURE'), None)
    fps = bpy.context.scene.render.fps
    act = arm.animation_data.action if (arm and arm.animation_data) else None
    return arm, fps, (act.frame_range if act else None)

def mapped_parent(arm, name, is_src):
    b = arm.data.bones.get(name)
    while b and b.parent:
        b = b.parent
        if (b.name in PAIRS) if is_src else (b.name in PAIRS.values()): return b.name
    return None

def dirs(arm, is_src):
    out = {}
    for s,t in PAIRS.items():
        nm = s if is_src else t
        p  = mapped_parent(arm, nm, is_src)
        if not p: continue
        a = arm.matrix_world @ arm.pose.bones[nm].head
        b = arm.matrix_world @ arm.pose.bones[p].head
        v = a - b
        if v.length > 1e-9: out[t] = v.normalized()
    return out

def sample(arm, times, fps, fr):
    res = []
    for t in times:
        bpy.context.scene.frame_set(int(round(fr[0] + t*fps)))
        bpy.context.view_layer.update()
        res.append(dirs(arm, False))
    return res

def sample_src(arm, times, fps, fr):
    res = []
    for t in times:
        bpy.context.scene.frame_set(int(round(fr[0] + t*fps)))
        bpy.context.view_layer.update()
        res.append(dirs(arm, True))
    return res

MIRROR = Matrix(((-1,0,0),(0,1,0),(0,0,1)))
def rotz(deg): return Matrix.Rotation(math.radians(deg), 3, 'Z')
CANDS_T = []
for deg in (0,90,180,-90):
    CANDS_T.append(("rotZ%+d" % deg, rotz(deg), False))
    CANDS_T.append(("rotZ%+d+X镜像" % deg, rotz(deg), True))

def best_fit(src_s, tgt_s):
    """穷举变换，返回最优的平均/最大夹角及对应变换名"""
    best = None
    for nm, R, mir in CANDS_T:
        vals = []
        for i in range(len(src_s)):
            for k, v in src_s[i].items():
                if k not in tgt_s[i]: continue
                w = v.copy()
                if mir: w = MIRROR @ w
                w = R @ w
                c = max(-1.0, min(1.0, w.dot(tgt_s[i][k])))
                vals.append(math.degrees(math.acos(c)))
        if not vals: continue
        m, mx = sum(vals)/len(vals), max(vals)
        if best is None or m < best[0]: best = (m, mx, nm)
    return best

SRC = os.path.join(ROOT, "input", "source", "ue_mannequin", "clips_basic", args.clip + ".fbx")
src_arm, src_fps, src_fr = load(SRC)
src_dur = (src_fr[1]-src_fr[0])/src_fps
print("\n=== %s ===" % args.clip)
print("源 UE5 : %s  fps=%d  帧 %.0f~%.0f  = %.3f s" % (args.clip+".fbx", src_fps, src_fr[0], src_fr[1], src_dur))
N = 25
times = [src_dur*i/(N-1) for i in range(N)]
S = sample_src(src_arm, times, src_fps, src_fr)

for spec in args.cands.split(","):
    nm, _, sub = spec.partition(":")
    folder = sub or nm
    cand = os.path.join(ROOT, folder, SUB.strip("/"), args.clip + ".fbx") if SUB else os.path.join(ROOT, folder, args.clip + ".fbx")
    if not os.path.exists(cand):
        print("  (缺 %s)" % cand); continue
    c_arm, c_fps, c_fr = load(cand)
    c_dur = (c_fr[1]-c_fr[0])/c_fps
    T = sample(c_arm, times, c_fps, c_fr)
    bf = best_fit(S, T)
    print("  %-22s fps=%d 时长 %.3f s (比源 %.3f) | 最优姿态差 mean %.2f° max %.2f°  [%s]"
          % (nm, c_fps, c_dur, c_dur/src_dur, bf[0], bf[1], bf[2]))

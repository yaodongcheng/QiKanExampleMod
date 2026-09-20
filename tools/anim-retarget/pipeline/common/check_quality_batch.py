# -*- coding: utf-8 -*-
"""批量质量校验（口径 = glb_check.py，参考侧 = 源 FBX，不是源 GLB）
   ① 逐段导入 exported_slim/<clip>.fbx，采 21 个时间点取「映射祖骨→骨」单位方向
   ② 载入 bannerlord_{ground|src}.glb（每个只导入一次，切 action 采样）逐段比较
   ③ 穷举 4 个绕 Z 旋转 × 2 镜像 = 8 种全局变换，取平均误差最小的（对两边公平）
   ④ 顺带用同样口径量「ue_mannequin_{ground|src}.glb（查看器左侧）」的忠实度
   输出 work_slim/quality_fbx.json
"""
import bpy, os, json, math, sys
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

ROOT  = PROJECT_ROOT
SLIM  = os.path.join(ROOT, "input", "source", "ue_mannequin", "clips_slim")
WEB   = os.path.join(ROOT, "output/glb/ue_slim")
MAN   = os.path.join(WEB, "manifest.json")
MAP   = json.load(open(os.path.join(ROOT, "pipeline", "rigs", "ue_mannequin", "map.json"), encoding="utf-8"))["bone_map"]
TWIST = {"upperarm_twist_01_l":"l_upperarm_twist1","upperarm_twist_01_r":"r_upperarm_twist1",
         "lowerarm_twist_01_l":"l_foretwist1","lowerarm_twist_01_r":"r_foretwist1"}
ALLMAP = dict(MAP); ALLMAP.update(TWIST)
S_NAMES = set(ALLMAP.keys()); T_NAMES = set(ALLMAP.values())
NS = 21

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30

def arm_of():
    return next(o for o in bpy.data.objects if o.type == 'ARMATURE')

def mp(arm, name, is_src):
    b = arm.data.bones.get(name)
    while b and b.parent:
        b = b.parent
        if (b.name in S_NAMES) if is_src else (b.name in T_NAMES): return b.name
    return None

def dirs(arm, is_src):
    out = {}
    for s, t in ALLMAP.items():
        nm = s if is_src else t
        if nm not in arm.pose.bones: continue
        p = mp(arm, nm, is_src)
        if not p: continue
        v = (arm.matrix_world @ arm.pose.bones[nm].head) - (arm.matrix_world @ arm.pose.bones[p].head)
        if v.length > 1e-9: out[t] = v.normalized()
    return out

F = [Matrix.Rotation(math.radians(d), 3, 'Z') for d in (0, 90, 180, -90)]
MIR = Matrix(((-1,0,0),(0,1,0),(0,0,1)))
def best(S, T):
    bv = None
    for i, R in enumerate(F):
        for mir in (False, True):
            vals = []
            for k in range(len(S)):
                for b, v in S[k].items():
                    if b not in T[k]: continue
                    w = MIR @ v if mir else v.copy()
                    w = R @ w
                    vals.append(math.degrees(math.acos(max(-1, min(1, w.dot(T[k][b]))))))
            if not vals: continue
            m = sum(vals)/len(vals)
            if bv is None or m < bv[0]: bv = (m, max(vals))
    return bv

def sample_fbx(clip, times_s):
    """导入源 FBX，按给定相对时间采样方向向量"""
    reset()
    fp = os.path.join(SLIM, clip + ".fbx")
    if not os.path.exists(fp): return None, None
    bpy.ops.import_scene.fbx(filepath=fp)
    arm = arm_of(); fps = bpy.context.scene.render.fps
    act = arm.animation_data.action if arm.animation_data else None
    if act is None: return None, None
    fs, fe = act.frame_range; dur = (fe-fs)/fps
    out = []
    for t in times_s:
        fr = fs + t*fps
        bpy.context.scene.frame_set(int(fr), subframe=float(fr-int(fr))); bpy.context.view_layer.update()
        out.append(dirs(arm, True))
    return out, dur

def sample_glb(path, clip, times_s):
    reset()
    if not os.path.exists(path): return None
    bpy.ops.import_scene.gltf(filepath=path)
    arm = arm_of()
    act = bpy.data.actions.get(clip)
    if act is None: return None
    if arm.animation_data is None: arm.animation_data_create()
    arm.animation_data.action = act
    try:
        if hasattr(act, "slots") and len(act.slots): arm.animation_data.action_slot = act.slots[0]
    except Exception: pass
    fs, fe = act.frame_range; dur = (fe-fs)/30.0
    out = []
    for t in times_s:
        f = fs + t*30
        bpy.context.scene.frame_set(int(f), subframe=float(f-int(f))); bpy.context.view_layer.update()
        out.append(dirs(arm, False))
    return out

def main():
    man = json.load(open(MAN, encoding="utf-8"))
    times = [i/(NS-1) for i in range(NS)]
    clips = list(man["clips"].keys())
    by_mode = {"ground": [], "src": []}
    for c in clips: by_mode[man["clips"][c].get("mode", "ground")].append(c)
    print("[slim] 待检 %d 段: ground %d / src %d" % (len(clips), len(by_mode["ground"]), len(by_mode["src"])))

    # ① 源侧：逐段导 FBX 采样
    SRC = {}; DUR = {}
    for i, c in enumerate(clips, 1):
        S, dur = sample_fbx(c, times)
        if S: SRC[c] = S; DUR[c] = dur
        if i % 25 == 0: print("  [源] %d/%d" % (i, len(clips)), flush=True)
    print("  [源] 完成 %d 段" % len(SRC), flush=True)

    out = {}
    for mode, glbfile in (("ground", "bannerlord_ground.glb"), ("src", "bannerlord_src.glb")):
        p = os.path.join(WEB, glbfile)
        for c in by_mode[mode]:
            if c not in SRC: continue
            T = sample_glb(p, c, times)
            b = best(SRC[c], T) if T else None
            if b: out[c] = {"mode": mode, "mean": b[0], "max": b[1], "ref": "source FBX"}
        print("  [骑砍 %s] 完成 %d 段" % (mode, sum(1 for c in out if out[c]["mode"] == mode)), flush=True)

    # ④ 查看器左侧 GLB 的忠实度（同口径，参考仍是源 FBX）
    # 查看器左侧的源 GLB（分片，逐片载入并在片内查动作）
    ue = {}
    SHARDS = {"ground": ["ue_gnd_c0.glb","ue_gnd_c1.glb","ue_gnd_c2.glb","ue_gnd_c3.glb"],
              "src":    ["ue_mannequin_src.glb","ue_src_c0.glb","ue_src_c1.glb"]}
    for mode, files in SHARDS.items():
        for c in by_mode[mode]:
            if c not in SRC: continue
            T = None
            for fn in files:
                T = sample_glb(os.path.join(WEB, fn), c, times)
                if T: break
            b = best(SRC[c], T) if T else None
            if b: ue[c] = {"mean": b[0], "max": b[1], "shard": fn}
        print("  [源GLB %s] 完成 %d 段" % (mode, sum(1 for k in ue if k not in out or True)), flush=True)

    ok = [v for v in out.values() if v is not None]
    gm = sum(v["mean"] for v in ok)/max(1, len(ok))
    gx = max((v["max"] for v in ok), default=0)
    um = sum(v["mean"] for v in ue.values())/max(1, len(ue))
    print("\n================ 汇总（参考 = 源 FBX）================")
    print("  骑砍2 重定向 %d 段: mean %.2f°  max %.2f°" % (len(ok), gm, gx))
    print("  查看器左侧源 GLB %d 段: mean %.2f°  (越小=左侧越忠实)" % (len(ue), um))
    rep = {"ref": "source FBX", "samples": NS, "bannerlord": out, "ue_glb_fidelity": ue,
           "bannerlord_mean_deg": gm, "bannerlord_max_deg": gx, "ue_glb_mean_deg": um, "durations": DUR}
    op = os.path.join(ROOT, "output/verify", "quality_fbx.json")
    json.dump(rep, open(op, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("  写出 " + op)

main()

# -*- coding: utf-8 -*-
"""把【已重定向 FBX】的逐帧【世界姿态】搬到【官方骨架 human_lod_4.fbx】上，再烘 GLB。
   为什么不用 glb_pack_retargeted.py 的"复制 fcurve"：实测重定向产物 FBX 的静姿 ≠ 官方骨架静姿
   （pelvis 头差 0.065 m、head 差 0.18 m，方向差数十度）→ 直接搬局部曲线会形变（实测下沉 0.37 m）。
   搬世界姿态则与静姿无关：目标骨的世界矩阵 = 源骨的世界矩阵（经 rigs/ue_mannequin/map.json 映射）。
   用法: blender -b --python bake_glb_posetransfer.py -- --base <human_lod_4.fbx> --fbx <retargeted.fbx>
        --map <map.json> --clip <动画名> --out <o.glb> [--fps 30]"""
import bpy, os, sys, json

def _args():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    d = {}; i = 0
    while i < len(a):
        if a[i].startswith("--"): d[a[i][2:]] = a[i+1] if i+1 < len(a) else ""; i += 2
        else: i += 1
    return d
A = _args()
BASE, FBX, MAPF, CLIP, OUT = A.get("base"), A.get("fbx"), A.get("map"), A.get("clip"), A.get("out")
FPS = int(A.get("fps", "30"))
def log(m): print("[posetransfer] %s" % m, flush=True)
MAP = json.load(open(MAPF, encoding="utf-8"))["bone_map"]

def fcurves_of(act):
    direct = getattr(act, "fcurves", None)
    if direct is not None: return list(direct)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in getattr(layer, "strips", []):
            for cb in getattr(strip, "channelbags", []): out.extend(list(cb.fcurves))
    return out

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=BASE)
base = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
if base.animation_data: base.animation_data.action = None
log("基底 %s 骨 %d" % (os.path.basename(BASE), len(base.data.bones)))
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=FBX)
src = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before)
sact = src.animation_data.action
f0, f1 = int(round(sact.frame_range[0])), int(round(sact.frame_range[1]))
log("源 %s 帧 %d..%d 骨 %d" % (os.path.basename(FBX), f0, f1, len(src.data.bones)))

order = []
def walk(b):
    order.append(b.name)
    for c in b.children: walk(c)
for b in base.data.bones:
    if b.parent is None: walk(b)
# ① 同名直接对（重定向产物里就是目标骨名：l_thigh/spine/l_clavicle…）
pairs = [(n, n) for n in base.data.bones.keys() if n in src.pose.bones]
# ② 若源仍是源骨名（UE: thigh_l/spine_01/…），用 map.json 补
_hit = {t for _, t in pairs}
pairs += [(s, t) for s, t in MAP.items() if s in src.pose.bones and t in base.data.bones and t not in _hit]
log("映射骨 %d：%s" % (len(pairs), [t for _, t in pairs][:6]))

if base.animation_data is None: base.animation_data_create()
act = bpy.data.actions.new(CLIP)
base.animation_data.action = act
try:
    if hasattr(act, "slots"):
        act.slots.new(id_type='OBJECT', name=base.name); base.animation_data.action_slot = act.slots[0]
except Exception as e: log("slot %s" % e)

W2B = base.matrix_world.inverted()
for i in range(0, f1 - f0 + 1):
    sc.frame_set(f0 + i); bpy.context.view_layer.update()
    SM = {s: (src.matrix_world @ src.pose.bones[s].matrix) for s, _ in pairs}
    for s, t in pairs:
        pb = base.pose.bones[t]
        pb.matrix = W2B @ SM[s]
        bpy.context.view_layer.update()
        pb.rotation_mode = 'QUATERNION'
        pb.keyframe_insert(data_path="rotation_quaternion", frame=i)
    pb = base.pose.bones.get('pelvis')
    if pb is not None: pb.keyframe_insert(data_path="location", frame=i)
for a in list(bpy.data.actions):
    if a is not act:
        try: bpy.data.actions.remove(a)
        except Exception: pass
keep = {base} | set(base.children)
for o in list(bpy.data.objects):
    if o not in keep:
        try: bpy.data.objects.remove(o, do_unlink=True)
        except Exception: pass
sc.frame_start, sc.frame_end = 0, f1 - f0
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_animations=True,
                          export_animation_mode='ACTIONS', export_skins=True, export_apply=False,
                          use_selection=False, export_frame_step=1)
log("导出 %s（%.2f MB）" % (OUT, os.path.getsize(OUT)/1048576.0))

"""把一个已重定向好的骑砍动画 FBX 按 ModKit 导入规格重导。

用法:
  blender -b --python reexport_for_modkit.py -- --src <in.fbx> --out <out.fbx> [--unit m|cm]

修的东西（对照 自定义战斗.md「第 1 步：造动画」硬规格 + 官方 animations 文档）:
  1) 删网格        —— 动画导出只导骨架（原脚本 object_types={'ARMATURE','MESH'} 连网格一起导了）
  2) 删 *_end 叶骨 —— Blender add_leaf_bones 默认 True 会凭空多出 5 根骨（28 → 33）
  3) 根节点改名    —— human_skeleton → human_skeleton_notused（让引擎忽略骨架、只吃动画）
  4) 轴 Z-up       —— 原导出是 Y-up（UpAxis=1），与游戏自带 FBX（UpAxis=2）不一致
  5) 单位          —— --unit cm 时按厘米导出（游戏自带 FBX UnitScaleFactor=100）
"""
import bpy, sys, os, math

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default


SRC = arg("--src")
OUT = arg("--out")
FPS = int(arg("--fps", "30"))
UNIT = arg("--unit", "cm")
ROOT_NAME = arg("--root", "human_skeleton_notused")


def log(m):
    print("[reexport] %s" % m, flush=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
if UNIT == "cm":
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 0.01

bpy.ops.import_scene.fbx(filepath=SRC)
log("导入完成；导入把场景 fps 改成了 %d，重置为 %d" % (sc.render.fps, FPS))
sc.render.fps = FPS

# 1) 删网格
for o in list(bpy.data.objects):
    if o.type == 'MESH':
        log("删网格: %s (%d 顶点)" % (o.name, len(o.data.vertices)))
        bpy.data.objects.remove(o, do_unlink=True)

arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
if len(arms) != 1:
    log("!! 期望 1 个骨架，实际 %d 个: %s" % (len(arms), [a.name for a in arms]))
    sys.exit(1)
arm = arms[0]
log("骨架 %s，骨数 %d" % (arm.name, len(arm.data.bones)))

# 2) 删叶骨（与子骨重合、恒等旋转，删除不影响姿态）
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
removed = []
for b in list(arm.data.edit_bones):
    if b.name.endswith("_end") or b.name.endswith("_nub_notused"):
        removed.append(b.name)
        arm.data.edit_bones.remove(b)
bpy.ops.object.mode_set(mode='OBJECT')
log("删叶骨 %d 根: %s" % (len(removed), removed))
log("剩 %d 骨: %s" % (len(arm.data.bones), sorted(b.name for b in arm.data.bones)))

# 3) 根节点改名
arm.name = ROOT_NAME
arm.data.name = ROOT_NAME
log("骨架改名为 %s" % ROOT_NAME)

# 4) 帧范围对齐动作
act = arm.animation_data.action if arm.animation_data else None
if act:
    fr = act.frame_range
    sc.frame_start = int(math.floor(fr[0]))
    sc.frame_end = int(math.ceil(fr[1]))
    log("动作 %s 范围 %.1f..%.1f  ->  场景帧 %d..%d (%.2fs @%dfps)"
        % (act.name, fr[0], fr[1], sc.frame_start, sc.frame_end,
           (sc.frame_end - sc.frame_start) / FPS, FPS))
else:
    log("!! 骨架没有动作 —— 导出会是空的")

# 5) 按规格导出
bpy.ops.export_scene.fbx(
    filepath=OUT,
    use_selection=False,
    object_types={'ARMATURE'},
    add_leaf_bones=False,
    axis_up='Z', axis_forward='-Y',
    primary_bone_axis='Y', secondary_bone_axis='X',
    apply_unit_scale=True,
    global_scale=1.0,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    path_mode='AUTO',
)
log("导出 %s (%.1f KB)" % (OUT, os.path.getsize(OUT) / 1024))

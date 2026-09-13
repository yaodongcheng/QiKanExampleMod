# v6：修 v5 的致命错误 —— 编译出来头放大 100 倍 + 绕 X 翻 180°。
#
# 根因（2026-09-13 实测，见 Knowledge/蒂法换头工程.md §12）：
#   v5 为了让骨架追上网格尺度，把骨架对象乘了 k≈99.77 倍 → 网格节点跟着带上 scale=100
#   → 编辑器把节点变换烘进顶点 → 100 倍。
#   且导出声明 axis_up='Y' → 文件里已转过一次，引擎又转一次 → 绕 X 翻 180°（脸朝后、翻到地下）。
#
# v6 的三处修正：
#   1) 网格不做任何对象级缩放/旋转：直接把 mesh 的 matrix_world 设成单位阵
#      → 顶点数值 = FBX 里存的那套（z=1.466~1.673、脸朝 +Y），已是引擎要的空间。
#      🔴 不能用 transform_apply —— 网格带 shape key，Blender 会拒绝执行。
#   2) 骨架改用 global_scale 校准到米级，缩放在【导入期】完成，不留对象级残留。
#   3) 导出声明 axis_up='Z' + 场景单位=米（→ UnitScaleFactor=100），与 TpacTool 导出的
#      社区验证规格一致 = 引擎只转一次。
#
# 出口门禁：跑完务必用 fbx_probe.py 复核（UnitScaleFactor=100 / UpAxis=2 / 无非单位缩放）。

import bpy, sys, mathutils

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"
V5   = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources\head_tifa_a_v5.fbx"
OUT  = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources\head_tifa_a_v6.fbx"

HEAD_BONE = "bip01_head_13"
# 目标落点（对照 head_xxfemale_a 的编译产物，见 §12.2）
TARGET_HEAD_Z = 1.569
# 网格必须落在这个窗口里，否则中止（宁可不出文件，也不出一个坏的）
MESH_WINDOW = {'x': (-0.15, 0.15), 'y': (-0.15, 0.20), 'z': (1.30, 1.90)}


def fail(msg):
    print("FATAL: " + msg)
    sys.stdout.flush()
    sys.exit(1)


def world_bbox(objs):
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for ob in objs:
        for c in ob.bound_box:
            p = ob.matrix_world @ mathutils.Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    return lo, hi


def fmt(lo, hi):
    return ("x[%8.4f,%8.4f] y[%8.4f,%8.4f] z[%8.4f,%8.4f]  size %.4f x %.4f x %.4f"
            % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2],
               hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]))


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0

# ---------- 1) 网格：导入 v5 后剥掉一切对象级变换 ----------
bpy.ops.import_scene.fbx(filepath=V5)
bpy.context.view_layer.update()
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if not meshes:
    fail("v5 里没导入到网格")
print("导入 %d 个网格: %s" % (len(meshes), sorted(o.name for o in meshes)))
print("  导入后世界包围盒: " + fmt(*world_bbox(meshes)))

# 删掉 v5 带进来的骨架/空节点/相机/灯——骨架我们用官方的那份重新来
for ob in [o for o in bpy.data.objects if o.type != 'MESH']:
    print("  移除 非网格对象: %s (%s)" % (ob.name, ob.type))
    bpy.data.objects.remove(ob, do_unlink=True)
# 悬空的 Armature 修改器会污染导出
for ob in meshes:
    for md in [m for m in ob.modifiers if m.type == 'ARMATURE']:
        ob.modifiers.remove(md)
    # 🔴 单位阵，不是 transform_apply（网格带 shape key，apply 会被 Blender 拒绝）
    ob.parent = None
    ob.matrix_world = mathutils.Matrix.Identity(4)
bpy.context.view_layer.update()

lo, hi = world_bbox(meshes)
print("归一化后世界包围盒: " + fmt(lo, hi))
for i, ax in enumerate('xyz'):
    w = MESH_WINDOW[ax]
    if hi[i] < w[0] or lo[i] > w[1]:
        fail("%s 轴越界 [%.4f,%.4f]，期望落在 [%.2f,%.2f] —— 基底 FBX 不是预期的空间"
             % (ax, lo[i], hi[i], w[0], w[1]))
if hi[1] <= 0:
    fail("脸朝反了（+Y 最大 %.4f ≤ 0）" % hi[1])
print("  [OK] 网格落点正常（脸朝 +Y、高度为正、米级尺度）")

# ---------- 2) 骨架：导入期就换算到米级，不留对象级缩放 ----------
# 官方骨架 FBX 声明厘米、数值是米；Blender 默认会再除 100（头骨掉到 0.0157）。
# 先按默认导入量一次，算出倍率，再按倍率重导 —— 缩放发生在导入期，不进节点变换。
bpy.ops.import_scene.fbx(filepath=SKEL)
bpy.context.view_layer.update()
arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm is None:
    fail("官方骨架里没有 Armature")
head_z_raw = (arm.matrix_world @ arm.data.bones[HEAD_BONE].head_local).z
print("骨架首导: %s  obj_scale=%s  头骨世界 z = %.6f"
      % (arm.name, tuple(round(v, 4) for v in arm.scale), head_z_raw))
if abs(head_z_raw) < 1e-9:
    fail("头骨 z 为 0，骨架结构不对")
k = TARGET_HEAD_Z / head_z_raw
print("  校准倍率 k = %.4f" % k)

bpy.data.objects.remove(arm, do_unlink=True)   # 丢掉首导结果，用校准倍率重导
bpy.ops.import_scene.fbx(filepath=SKEL, global_scale=k)
bpy.context.view_layer.update()
arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
head_z = (arm.matrix_world @ arm.data.bones[HEAD_BONE].head_local).z
print("骨架重导: %s  obj_scale=%s  头骨世界 z = %.6f (目标 %.4f)"
      % (arm.name, tuple(round(v, 4) for v in arm.scale), head_z, TARGET_HEAD_Z))
if abs(head_z - TARGET_HEAD_Z) > 0.01:
    fail("头骨 z 校准失败：%.4f" % head_z)

# 🔴 关键一步：Blender 会把官方骨架的单位声明转成【对象缩放 0.01】，global_scale 只是叠乘上去，
#    结果对象上永远留着 ≈0.9975 的缩放。这层缩放一旦被导出，引擎就会烘进顶点 —— 正是 v5 的病根。
#    骨架没有 shape key，可以安全 transform_apply 把缩放烘进骨骼数据，让对象缩放恒等于 1。
if any(abs(s - 1.0) > 1e-4 for s in arm.scale):
    for ob in bpy.data.objects:
        ob.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()
    head_z = (arm.matrix_world @ arm.data.bones[HEAD_BONE].head_local).z

print("骨架烘平后: obj_scale=%s  头骨世界 z = %.6f" % (tuple(round(v, 4) for v in arm.scale), head_z))
if any(abs(s - 1.0) > 1e-4 for s in arm.scale):
    fail("骨架对象仍有非单位缩放 %s —— 正是 v5 的病根，必须为 1"
         % (tuple(round(v, 4) for v in arm.scale),))
if abs(head_z - TARGET_HEAD_Z) > 0.01:
    fail("烘平后头骨 z 漂了：%.4f" % head_z)
print("  [OK] 骨架在米级、对象缩放为 1")

# ---------- 3) 绑定：全部刚性绑到头骨 ----------
for ob in meshes:
    world = ob.matrix_world.copy()
    vg = ob.vertex_groups.get(HEAD_BONE) or ob.vertex_groups.new(name=HEAD_BONE)
    vg.add([v.index for v in ob.data.vertices], 1.0, 'REPLACE')
    md = ob.modifiers.new(name="Armature", type='ARMATURE')
    md.object = arm
    md.use_vertex_groups = True
    ob.parent = arm
    ob.parent_type = 'OBJECT'
    ob.matrix_world = world          # 骨架是单位阵 → 不产生补偿缩放
    print("  绑定 %-18s 顶点=%-5d shape_keys=%d"
          % (ob.name, len(ob.data.vertices), len(ob.data.shape_keys.key_blocks) if ob.data.shape_keys else 0))
    if any(abs(s - 1.0) > 1e-4 for s in ob.scale):
        fail("%s 绑定后出现非单位缩放 %s" % (ob.name, tuple(round(v, 4) for v in ob.scale)))
bpy.context.view_layer.update()
print("绑定后网格世界包围盒: " + fmt(*world_bbox(meshes)) + "   (应与归一化后一致)")

# ---------- 4) 导出：TpacTool 规格 = UpAxis=Z + 米制 + 节点无变换 ----------
for ob in bpy.data.objects:
    ob.select_set(False)

# 🔴 必须在【所有导入之后】重设一次场景单位：Blender 的 FBX 导入器会按文件的
#    UnitScaleFactor 改写 scene.unit_settings（官方骨架声明厘米 → 场景被改成 0.01）。
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.context.view_layer.update()
if abs(bpy.context.scene.unit_settings.scale_length - 1.0) > 1e-6:
    fail("场景单位不是米，导出会写出 100 倍节点缩放")

# 🔴 两个参数是实测出来的（2026-09-13，见 §12.3），不是默认值、也不是 §10.3 记录的那套：
#   apply_scale_options='FBX_SCALE_UNITS'
#       FBX_SCALE_NONE 会把「1 米 = 100 厘米」烘成节点缩放 scale=100 并声明 UnitScaleFactor=1
#       → 引擎认节点缩放、不认单位声明 → 顶点被放大 100 倍（v5 的病根）。
#       FBX_SCALE_UNITS 才是写成 UnitScaleFactor=100、节点缩放留 1。
#   axis_forward='Y'（不是 '-Y'！）
#       用 '-Y' 会同时写出 CoordAxisSign=-1 和节点 rot=[0,0,180] → 脸朝后。
#       'Y' + axis_up='Z' 才得到 UpAxis=2 / FrontAxisSign=-1 / CoordAxisSign=1 / 节点零旋转，
#       与 TpacTool 导出器（社区验证过能进游戏）逐项一致。
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=False, object_types={'MESH', 'ARMATURE'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='Y', axis_up='Z',
    use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
    mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
)
print("EXPORTED -> " + OUT)
print("下一步门禁：python tools/face-pipeline/scripts/fbx_probe.py \"%s\" --full" % OUT)
sys.stdout.flush()

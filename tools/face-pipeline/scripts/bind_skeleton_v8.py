# v8：在 v7 基础上，给【附属件】做逐子网格标定。
#
# v7 只对脸壳做了锚点对齐（眼/嘴/头顶）。附属件没标定 → 蒂法的眼球是完整球体、
# 按 1.238 放大后凸出眼窝 2.5cm（实机症状：眼睛长到脸外面）。
#
# 对位关系（原版 head_female_a 的材质映射，实测 mat_map）：
#   _eye  眼球  ↔  `.6` eye_mat        x±0.0492 y[0.1212,0.1370] z[1.6632,1.6956]
#   _lash 睫毛  ↔  `.7` eyelashes_mat  x±0.0542 y[0.1242,0.1443] z[1.6702,1.6888]
#   （_brow 眉 / _shadow 眼影 原版无对位——原版眉毛走 eyebrow_meshes 独立件，本轮不动）
#
# 做法：按材质取顶点组 → **等比缩放**（按宽度对齐，实测缩放后 z 跨度与原版几乎吻合，
#       说明只是整体大了 1.33×，不是比例问题）→ 平移让 x 中心、**y 最前点**、z 中心对齐。
#       y 对齐"最前点"是关键：不凸出脸外靠的就是这个。
#
# v7：在 v6 基础上，按原版头的面部特征把蒂法头"摆正"
#
# v6 修好了"空间/朝向/单位"（头能出现、能跟骨架动），但头的位置和大小仍不对：
#   蒂法头 = 原版 head_female_a 的 0.808 倍大，且整体偏低约 10cm → 看起来"搁在脖子上"。
# 标定依据（2026-09-13 实测原版 head_female_a / 编译产物坐标）：
#   原版: 嘴中心 z=1.6067  眼中心 z=1.6795  (嘴→眼间距 0.0728)
#   蒂法: 嘴中心 z=1.5142  眼中心 z=1.5730  (嘴→眼间距 0.0588)  → 比值 0.808 → 放大 1.238
#   1.238 正是 §3 记录的 ratio（Tifa/xxFemale ≈ 1.238）—— 该因子在往返中丢了。
# 变换：绕头壳包围盒中心放大 1.238，再平移让「眼睛」落在原版位置。
#   核对：眼睛 1.6795 ✓ 嘴 1.6067 ✓ 头顶 1.8031（原版 1.8019）✓ 三锚点同时命中。
#
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
# 🔴 输入必须是【未标定的 v6】，不能是已标定的 v7 —— 否则脸壳标定会做两遍
# 🔴 输入读【备份目录】，不读 AssetSources —— ModKit 删网格资产时会连带删掉源 FBX（实机确认）
V6   = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v6.fbx"
OUT  = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources\head_tifa_a_v8.fbx"

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
    # 🔴 必须逐顶点算，不能用 ob.bound_box —— 那是缓存，改完顶点后不刷新，
    #    会报出过期坐标（本工程被它骗过两次：一次误判没改、一次误判改了）
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for ob in objs:
        for v in ob.data.vertices:
            p = ob.matrix_world @ v.co
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
bpy.ops.import_scene.fbx(filepath=V6)
bpy.context.view_layer.update()
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if not meshes:
    fail("v6 里没导入到网格")
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

# ---------- 1.5) 标定：按原版头的面部特征放大 + 摆正 ----------
# 参照物 = 原版 head_female_a（实测，见文件头注释）。眼/嘴用子网格包围盒中心，比整体包围盒稳。
FIT_SCALE = 1.238
EYES_TARGET = (0.0, 0.13425, 1.6795)     # 原版 head_female_a.7（眼）中心
MOUTH_TARGET_Z = 1.6067                  # 原版 head_female_a.2（嘴）中心 z —— 仅用于自检

def verts_of_mat(pred):
    """收集所有网格里、材质名满足 pred 的顶点世界坐标（Blender 侧对象按材质分组，
    眼球和眉毛在同一个对象里，只有靠材质才分得开）。"""
    pts = []
    for ob in meshes:
        idx = set(i for i, m in enumerate(ob.data.materials) if m and pred(m.name.lower()))
        if not idx:
            continue
        seen = set()
        for p in ob.data.polygons:
            if p.material_index in idx:
                seen.update(p.vertices)
        for vi in seen:
            pts.append(ob.matrix_world @ ob.data.vertices[vi].co)
    return pts

def pts_center(pts):
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    return [(lo[i] + hi[i]) / 2.0 for i in range(3)]

shell_pts = verts_of_mat(lambda n: n.endswith("head_tifa_a"))
eyes_pts  = verts_of_mat(lambda n: n.endswith("_eye"))
mouth_pts = verts_of_mat(lambda n: n.endswith("_mouth"))
for nm, pts in (("脸壳", shell_pts), ("眼球", eyes_pts), ("嘴", mouth_pts)):
    if not pts:
        fail("按材质取不到「%s」的顶点" % nm)

C = pts_center(shell_pts)
E = pts_center(eyes_pts)
M = pts_center(mouth_pts)
print("标定前: 眼中心 z=%.4f  嘴中心 z=%.4f  头壳中心 z=%.4f (顶点数 %d/%d/%d)"
      % (E[2], M[2], C[2], len(shell_pts), len(eyes_pts), len(mouth_pts)))

# 放大后眼睛会落到哪，据此算平移量
E_scaled = [C[i] + FIT_SCALE * (E[i] - C[i]) for i in range(3)]
T = [EYES_TARGET[i] - E_scaled[i] for i in range(3)]
print("放大 %.3f 倍（绕头壳中心）+ 平移 (%.4f, %.4f, %.4f)" % (FIT_SCALE, T[0], T[1], T[2]))

# 🔴 变换要写两处，且不能写重：
#   ① ob.data.vertices —— 导出器真正读的基础几何
#      **上一版漏了这步**，于是带 shape key 的头壳没动、只有嘴/眼珠飞了 10cm
#   ② shape key 块 1..N —— 形态键存的是位移增量，等比缩放后必须同步，否则表情错位
#   ③ **跳过块 0（Basis）** —— 它与 ob.data.vertices 是同一份数据，两边都写 = 变换做两遍
def _fit(co):
    return mathutils.Vector([
        C[0] + FIT_SCALE * (co[0] - C[0]) + T[0],
        C[1] + FIT_SCALE * (co[1] - C[1]) + T[1],
        C[2] + FIT_SCALE * (co[2] - C[2]) + T[2],
    ])

for ob in meshes:
    for v in ob.data.vertices:
        v.co = _fit(v.co)
    sk = ob.data.shape_keys
    if sk:
        for kb in [sk.key_blocks[i] for i in range(1, len(sk.key_blocks))]:
            for pt in kb.data:
                pt.co = _fit(pt.co)
    ob.data.update()
bpy.context.view_layer.update()

# 自检：头壳顶点必须真的动了（上一版正是这里漏查，才把坏文件放出去）
_shell = next(o for o in meshes if o.name.endswith(".0"))
_sz = [( _shell.matrix_world @ v.co).z for v in _shell.data.vertices]
print("脸壳实测 z: %.4f ~ %.4f  (标定前 1.4661~1.6728，标定后应到 ~1.8030)" % (min(_sz), max(_sz)))
if max(_sz) < 1.79:
    fail("脸壳顶点没被变换（z 最大仅 %.4f）" % max(_sz))

# 自检：三个锚点必须同时命中（偏差 > 5mm 说明标定错了）
E2 = pts_center(verts_of_mat(lambda n: n.endswith("_eye")))
M2 = pts_center(verts_of_mat(lambda n: n.endswith("_mouth")))
lo2, hi2 = world_bbox(meshes)
print("标定后: 眼中心 z=%.4f (目标 %.4f)  嘴中心 z=%.4f (目标 %.4f)  头顶 z=%.4f (原版 1.8019)"
      % (E2[2], EYES_TARGET[2], M2[2], MOUTH_TARGET_Z, hi2[2]))
if abs(E2[2] - EYES_TARGET[2]) > 0.005:
    fail("眼睛落点偏差 %.4f m" % (E2[2] - EYES_TARGET[2]))
if abs(M2[2] - MOUTH_TARGET_Z) > 0.010:
    fail("嘴落点偏差 %.4f m（说明头型比例与原版差异过大，需人工确认）" % (M2[2] - MOUTH_TARGET_Z))
print("  [OK] 面部三锚点已对齐原版")

# ---------- 1.6) 附属件标定：按材质组等比缩放 + 对齐原版对位包围盒 ----------
# 目标 bbox 来自原版 head_female_a（见文件头注释）。y 对齐【最前点】= 保证不凸出脸外。
AFIX = [
    ("_eye",  (-0.0492, 0.0492, 0.1212, 0.1370, 1.6632, 1.6956), "眼球 ← 原版 .6 eye_mat"),
    ("_lash", (-0.0542, 0.0542, 0.1242, 0.1443, 1.6702, 1.6888), "睫毛 ← 原版 .7 eyelashes_mat"),
]

def verts_by_suffix(suffix):
    """返回 [(ob, [顶点索引...])]，按材质名后缀匹配"""
    out = []
    for ob in meshes:
        idx = set(i for i, m in enumerate(ob.data.materials) if m and m.name.lower().endswith(suffix))
        if not idx:
            continue
        vs = set()
        for p in ob.data.polygons:
            if p.material_index in idx:
                vs.update(p.vertices)
        if vs:
            out.append((ob, sorted(vs)))
    return out

for suffix, tgt, label in AFIX:
    groups = verts_by_suffix(suffix)
    if not groups:
        fail("附属件标定：找不到材质组 %s" % suffix)
    pts = [ob.matrix_world @ ob.data.vertices[i].co for ob, vs in groups for i in vs]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    s_fit = (tgt[1] - tgt[0]) / (hi[0] - lo[0])          # 按宽度等比缩放
    C = [(lo[i] + hi[i]) / 2.0 for i in range(3)]        # 绕组中心缩放
    y_max_after = C[1] + s_fit * (hi[1] - C[1])
    T = [ (tgt[0]+tgt[1])/2.0 - C[0],                     # x 中心对齐
          tgt[3] - y_max_after,                           # y 最前点对齐 ← 不凸出的关键
          (tgt[4]+tgt[5])/2.0 - C[2] ]                    # z 中心对齐
    print("附属件 %-6s %s" % (suffix, label))
    print("   标定前 bbox: x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo[0],hi[0],lo[1],hi[1],lo[2],hi[2]))
    print("   等比缩放 %.4f  平移 (%.4f, %.4f, %.4f)" % (s_fit, T[0], T[1], T[2]))
    for ob, vs in groups:
        for i in vs:
            v = ob.data.vertices[i]
            v.co = mathutils.Vector([
                C[0] + s_fit * (v.co[0] - C[0]) + T[0],
                C[1] + s_fit * (v.co[1] - C[1]) + T[1],
                C[2] + s_fit * (v.co[2] - C[2]) + T[2],
            ])
        ob.data.update()
    bpy.context.view_layer.update()
    # 自检
    pts2 = [ob.matrix_world @ ob.data.vertices[i].co for ob, vs in groups for i in vs]
    lo2 = [min(p[i] for p in pts2) for i in range(3)]
    hi2 = [max(p[i] for p in pts2) for i in range(3)]
    print("   标定后 bbox: x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo2[0],hi2[0],lo2[1],hi2[1],lo2[2],hi2[2]))
    err = max(abs(lo2[0]-tgt[0]), abs(hi2[0]-tgt[1]), abs(hi2[1]-tgt[3]), abs(lo2[2]-tgt[4]), abs(hi2[2]-tgt[5]))
    if err > 0.004:
        fail("%s 标定偏差 %.4f m（应 < 4mm）" % (suffix, err))
    print("   [OK] 与目标最大偏差 %.4f m" % err)

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

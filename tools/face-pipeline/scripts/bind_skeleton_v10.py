# v10：在 v9 基础上，把【眼球 / 睫毛】改回"源模型忠实"的位置（2026-09-13 深夜）
#
# 病因：v8 那轮把眼球/睫毛往【原版 head_female_a 的眼贴片】上对齐——但原版那一件是
#       **扁平贴片**（y 深 1.6cm），蒂法的是**完整球体**（y 深 2.6cm），拿贴片的前沿当基准
#       → 眼球被硬拉回去 1cm、缩小 14%。编辑器里看就是"眼球陷进眼窝"。
#
# 正解 = 回到蒂法源模型的装配关系。做法：源 head 的包围盒 → 我们 head 的包围盒 反解出变换
#       （绕 x 翻转 + 1.238 缩放 + 平移），把源里各配件折算到我们空间当目标。
#       **自检锚点：嘴**——v8 没动过嘴，而折算出嘴的目标 bbox 与我们当前的嘴【逐位吻合】
#       （y[0.0752,0.1489] vs [0.0752,0.1490]）→ 证明这套换算是准的。
#
# 目标（源×1.238 折算，实测输出见 _probe_source_eye.py）：
#   眼球 x±0.0574  y[0.1213,0.1472]  z[1.6582,1.7009]   （当前 y 前沿只到 0.1370 → 前移 10.2mm，放大 1.167×）
#   睫毛 x±0.0675  y[0.1293,0.1618]  z[1.6659,1.6909]   （当前 y 前沿只到 0.1443 → 前移 17.5mm，放大 1.246×）
#   嘴   —— 已经是源忠实的，不动
#
#   ⚠️ 源模型的眼球本来就比眼皮前沿凸出约 1cm（蒂法的大眼风格）。v7 那会儿看着"眼睛长到脸外"，
#      实为形变错位（脸壳吃 morph 权重、五官不吃）造成的，不是静态位置错——那条由 v9 的通道补全解决。
#
# —— 以下为 v9 原有内容（结构 / 形变通道 / 形状键顺序 / 导出规格）——
#
# v9：把蒂法头修成"能用的头"——四件事一次做完（2026-09-13 深夜，见 Knowledge/蒂法换头工程.md §17）
#
# 输入 v8（已标定、已绑骨架），只做增量改造，不重跑标定：
#
#   ① 结构：删掉 eyeshadow / eyebrow 两组面 + 材质；对象改名为
#      `head_tifa_a.0`(脸壳) / `.1`(嘴) / `.2`(眼球) / `.3`(睫毛)
#      —— 引擎按【子网格顺序】分配脸部件贴图，原版与参照 mod 都是 4 件、顺序 脸→嘴→眼→睫；
#         以前是 6 件（脸→嘴→睫→眼影→眼→眉），眼排第 5 → 超范围 → 眼球糊上脸皮（§16 实证）。
#
#   ② 形变：给嘴/眼/睫补上与脸壳同一套的形变通道
#      —— 脸壳的 59 条通道是位移场（单条最大 ±2cm），实机脸壳会被拉动而五官不动 →
#         眼球跑到眼眶上方、眉毛错位、鼻子前凸（§17 实证）。参照 mod 的嘴/眼/睫各有 53~65 帧带形变。
#      做法：按【最近邻 3 点反距离加权】把脸壳的位移场搬到附属件顶点（§3 同款技术；1 近邻会起皱）。
#
#   ③ 形状键顺序：重建为 Basis → KeyTime_0(零位移占位) → KeyTime_1 … KeyTime_59
#      —— 编辑器【按位置】把非 Basis 键编成帧 0,1,2…（实测：名字里的编号不影响帧号），
#         而皮肤 deform_keys 用 key_time_point=N 取帧 N → 必须有占位键顶在帧 0，
#         否则整体错位一格（实测我们的 f0 = KeyTime_1、f2 = KeyTime_10，因为 FBX 里顺序是乱的）。
#
#   ④ 导出规格沿用 v8（§13.1）：FBX_SCALE_UNITS + axis_forward='Y' + axis_up='Z' + 场景单位=米
#
# 出口门禁：fbx_probe.py <新fbx> --full  → 期望 UnitScaleFactor=100 / UpAxis=2 / 网格节点零变换

import bpy, sys, re, os, shutil, mathutils
from mathutils import kdtree

V8  = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v8.fbx"
OUT = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources\head_tifa_a_v10.fbx"
BAK = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"

# 附属件目标包围盒（源模型 × 1.238 折算；推导见文件头）
#   (x_min, x_max, y_min, y_max, z_min, z_max)
AFIX = [
    ("_eye",  (-0.0574, 0.0574, 0.1213, 0.1472, 1.6582, 1.7009), "眼球 ← 蒂法源模型"),
    ("_lash", (-0.0675, 0.0675, 0.1293, 0.1618, 1.6659, 1.6909), "睫毛 ← 蒂法源模型"),
]

# 要丢掉的两组：原版头里没有对位子网格（原版眉毛走 eyebrow_meshes 独立件）
DROP_SUFFIX = ("_shadow", "_brow")
# 保留的三件 + 目标名（顺序 = 引擎认部件的顺序：脸→嘴→眼→睫）
RENAME = [("_mouth", "head_tifa_a.1"), ("_eye", "head_tifa_a.2"), ("_lash", "head_tifa_a.3")]
SHELL_NAME = "head_tifa_a.0"

CH = 59            # 脸形通道数（KeyTime_1..59）
NN = 3             # 最近邻数（3 = 平顺；1 = 硬贴会起皱，§3 实测）


def fail(msg):
    print("FATAL: " + msg)
    sys.stdout.flush()
    sys.exit(1)


def obj_bbox(ob):
    lo = [1e9] * 3; hi = [-1e9] * 3
    for v in ob.data.vertices:
        p = ob.matrix_world @ v.co
        for i in range(3):
            lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
    return lo, hi


def bbox_str(ob):
    lo, hi = obj_bbox(ob)
    return ("x[%8.4f,%8.4f] y[%8.4f,%8.4f] z[%8.4f,%8.4f]"
            % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))


def mat_suffix(me, i):
    m = me.materials[i] if 0 <= i < len(me.materials) else None
    return m.name.lower() if m else ""


# ---------- 0) 导入 v8 ----------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=V8)
bpy.context.view_layer.update()

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
if not meshes:
    fail("v8 里没导入到网格")
if len(arms) != 1:
    fail("期望 1 个骨架，实际 %d 个（v8 的骨架必须保留，否则蒙皮绑定丢失）" % len(arms))
arm = arms[0]
print("导入网格 %d 个：%s" % (len(meshes), sorted(o.name for o in meshes)))
print("骨架 %s  obj_scale=%s" % (arm.name, tuple(round(v, 4) for v in arm.scale)))
if any(abs(s - 1.0) > 1e-4 for s in arm.scale):
    fail("骨架对象带非单位缩放 %s —— 会被烘进顶点" % (tuple(round(v, 4) for v in arm.scale),))


# ---------- 1) 删掉 eyeshadow / eyebrow 两组面 ----------
import bmesh

for ob in meshes:
    me = ob.data
    drop = [i for i, m in enumerate(me.materials)
            if m and m.name.lower().endswith(DROP_SUFFIX)]
    if not drop:
        continue
    keep_map = {}          # 旧材质槽 → 新槽
    for i, m in enumerate(me.materials):
        if i not in drop:
            keep_map[i] = len(keep_map)
    kept_mats = [me.materials[i] for i in range(len(me.materials)) if i not in drop]
    drop_names = [me.materials[i].name for i in drop]

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    dead = [f for f in bm.faces if f.material_index in drop]
    print("  %-18s 删除 %s：面 %d" % (ob.name, drop_names, len(dead)))
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    # 材质槽重映射
    for f in bm.faces:
        f.material_index = keep_map.get(f.material_index, 0)
    # 清掉孤立顶点（bmesh 的 FACES 删除会留下它们）
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
        print("      顺带清掉孤立顶点 %d 个" % len(loose))
    bm.to_mesh(me)
    bm.free()
    me.update()
    while me.materials:
        me.materials.pop(index=len(me.materials) - 1)
    for m in kept_mats:
        me.materials.append(m)
bpy.context.view_layer.update()


# ---------- 2) 认对象：脸壳 / 嘴 / 眼 / 睫 ----------
def find_by_suffix(suffix):
    hit = [ob for ob in meshes
           if any(m and m.name.lower().endswith(suffix) for m in ob.data.materials)]
    if len(hit) != 1:
        fail("按材质 %s 找到 %d 个对象（应为 1）" % (suffix, len(hit)))
    return hit[0]


shell = find_by_suffix("head_tifa_a")
parts = {s: find_by_suffix(s) for s, _ in RENAME}
print("脸壳 = %s   附属件 = %s" % (shell.name, {s: o.name for s, o in parts.items()}))
if len(meshes) != 4:
    fail("网格对象应为 4 个（脸+嘴+眼+睫），实际 %d 个：%s" % (len(meshes), [o.name for o in meshes]))


# ---------- 2.5) 附属件：回到"源模型忠实"的位置（v10 新增，见文件头） ----------
for ob in meshes:
    mw = ob.matrix_world
    if any(abs(mw[i][j] - (1.0 if i == j else 0.0)) > 1e-5 for i in range(4) for j in range(4)):
        fail("%s 的对象矩阵不是单位阵，本步的局部坐标换算不成立" % ob.name)

for suffix, tgt, label in AFIX:
    ob = parts[suffix]
    pts = [v.co for v in ob.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    s_fit = (tgt[1] - tgt[0]) / (hi[0] - lo[0])            # 按宽度等比缩放
    C = [(lo[i] + hi[i]) / 2.0 for i in range(3)]          # 绕组中心缩放
    y_max_after = C[1] + s_fit * (hi[1] - C[1])
    T = [(tgt[0] + tgt[1]) / 2.0 - C[0],                   # x 中心对齐
         tgt[3] - y_max_after,                             # y 最前点对齐 ← 眼睛深度的关键
         (tgt[4] + tgt[5]) / 2.0 - C[2]]                   # z 中心对齐
    print("附属件 %-6s %s" % (suffix, label))
    print("   改前 bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))
    print("   等比缩放 %.4f  平移 (%.4f, %.4f, %.4f)" % (s_fit, T[0], T[1], T[2]))
    for v in ob.data.vertices:
        v.co = mathutils.Vector([
            C[0] + s_fit * (v.co[0] - C[0]) + T[0],
            C[1] + s_fit * (v.co[1] - C[1]) + T[1],
            C[2] + s_fit * (v.co[2] - C[2]) + T[2],
        ])
    ob.data.update()
    bpy.context.view_layer.update()
    pts2 = [v.co for v in ob.data.vertices]
    lo2 = [min(p[i] for p in pts2) for i in range(3)]
    hi2 = [max(p[i] for p in pts2) for i in range(3)]
    print("   改后 bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo2[0], hi2[0], lo2[1], hi2[1], lo2[2], hi2[2]))
    err = max(abs(lo2[0] - tgt[0]), abs(hi2[0] - tgt[1]), abs(lo2[1] - tgt[2]),
              abs(hi2[1] - tgt[3]), abs(lo2[2] - tgt[4]), abs(hi2[2] - tgt[5]))
    if err > 0.002:
        fail("%s 标定偏差 %.4f m（应 < 2mm）" % (suffix, err))
    print("   [OK] 与源模型目标最大偏差 %.4f m" % err)


# ---------- 3) 读脸壳的形状键 → 按编号排序（FBX 里是乱序的，§17.5） ----------
sk = shell.data.shape_keys
if sk is None:
    fail("脸壳没有形状键")
chans = {}
others = []
for kb in sk.key_blocks:
    m = re.match(r'^KeyTime_(\d+)$', kb.name)
    if m:
        chans[int(m.group(1))] = kb
    else:
        others.append(kb.name)
print("脸壳形状键 %d 个：非 KeyTime 的 = %s；编号 %s..%s 共 %d 条"
      % (len(sk.key_blocks), others,
         min(chans), max(chans), len(chans)))
missing = [n for n in range(1, CH + 1) if n not in chans]
if missing:
    fail("脸壳缺通道 %s" % missing)

shell_base = [v.co.copy() for v in shell.data.vertices]
# delta[n][i] = 第 n 条通道在第 i 个顶点上的位移
delta = {}
for n in range(1, CH + 1):
    kb = chans[n]
    delta[n] = [kb.data[i].co - shell_base[i] for i in range(len(shell_base))]


def rebuild_shapes(ob, deltas_by_channel):
    """按目标顺序重建形状键：Basis → KeyTime_0(零位移) → KeyTime_1..59。
    🔴 编辑器按【位置】把非 Basis 键编成帧 0,1,2…，所以要有一个零位移键顶在帧 0，
       皮肤的 deform_key(key_time_point=N) 才会正好取到第 N 条通道。"""
    base = [v.co.copy() for v in ob.data.vertices]
    co = [v.co.copy() for v in ob.data.vertices]
    ob.shape_key_clear()
    kb = ob.shape_key_add(name="Basis", from_mix=False)
    flat = []
    for c in base:
        flat += [c.x, c.y, c.z]
    kb.data.foreach_set("co", flat)

    def add(name, deltas):
        k = ob.shape_key_add(name=name, from_mix=False)
        f = []
        for i, c in enumerate(base):
            d = deltas[i]
            f += [c.x + d.x, c.y + d.y, c.z + d.z]
        k.data.foreach_set("co", f)

    # 🔴 占位键必须【非退化】：完全零位移的通道在 FBX 里会被压成 1 个顶点（实测），
    #    编辑器可能直接跳过它 → 帧号又错位。给 0.1mm 的微小位移：肉眼不可见，
    #    而且皮肤 deform_keys 里【没有任何 key_time_point=0 的条目】→ 这帧永远不会被应用到，
    #    它的内容是什么都无所谓，存在的意义只是把后面 59 条顶到帧 1..59。
    add("KeyTime_0", [mathutils.Vector((0.0, 0.0001, 0.0))] * len(base))
    for n in range(1, CH + 1):
        add("KeyTime_%d" % n, deltas_by_channel[n])
    return base


rebuild_shapes(shell, delta)
_ks = [kb.name for kb in shell.data.shape_keys.key_blocks]
print("  [OK] 脸壳形状键重建：%d 个 = %s … %s" % (len(_ks), _ks[:4], _ks[-2:]))


# ---------- 4) 附属件：最近邻 3 点反距离加权，把脸壳的位移场搬过去 ----------
kd = kdtree.KDTree(len(shell_base))
for i, c in enumerate(shell_base):
    kd.insert(c, i)
kd.balance()

for suffix, _ in RENAME:
    ob = parts[suffix]
    base = [v.co.copy() for v in ob.data.vertices]
    xfer = {n: [] for n in range(1, CH + 1)}
    for c in base:
        hits = kd.find_n(c, NN)
        ws = [1.0 / (d + 1e-4) for (_, _, d) in hits]
        s = sum(ws)
        ws = [w / s for w in ws]
        for n in range(1, CH + 1):
            dv = mathutils.Vector((0, 0, 0))
            for (_, idx, _), w in zip(hits, ws):
                dv = dv + delta[n][idx] * w
            xfer[n].append(dv)
    rebuild_shapes(ob, xfer)
    nz = sum(1 for n in range(1, CH + 1)
             if max(abs(d.x) + abs(d.y) + abs(d.z) for d in xfer[n]) > 1e-4)
    print("  [OK] %-16s 补形变通道 %d 条（其中 %d 条有实际位移）"
          % (ob.name, CH, nz))


# ---------- 5) 改名：顺序 脸→嘴→眼→睫 ----------
order = [shell] + [parts[s] for s, _ in RENAME]
for i, ob in enumerate(order):
    tmp = "TMP_%d" % i
    ob.name = tmp
    ob.data.name = tmp
for i, ob in enumerate(order):
    final = [SHELL_NAME] + [nm for _, nm in RENAME]
    ob.name = final[i]
    ob.data.name = final[i]
bpy.context.view_layer.update()
print("对象顺序：%s" % [o.name for o in order])


# ---------- 6) 自检 ----------
for ob in order:
    if any(abs(s - 1.0) > 1e-4 for s in ob.scale):
        fail("%s 带非单位缩放 %s" % (ob.name, tuple(round(v, 4) for v in ob.scale)))
    ks = [kb.name for kb in ob.data.shape_keys.key_blocks]
    want = ["Basis", "KeyTime_0"] + ["KeyTime_%d" % n for n in range(1, CH + 1)]
    if ks != want:
        fail("%s 形状键顺序不对：%s" % (ob.name, ks[:6]))
    print("  %-16s verts=%-6d polys=%-6d shapes=%-3d  %s"
          % (ob.name, len(ob.data.vertices), len(ob.data.polygons), len(ks), bbox_str(ob)))

# 位移签名（前 3 帧）——编译后拿 morphinfo 的「逐帧位移签名」对号，验证帧 0 = 零、帧 1 = 脸宽
print("位移签名（应：KeyTime_0 全零；KeyTime_1 纯 X；KeyTime_2 纯 Y）：")
for nm in ("KeyTime_0", "KeyTime_1", "KeyTime_2"):
    kb = shell.data.shape_keys.key_blocks[nm]
    dx = [kb.data[i].co - shell_base[i] for i in range(min(4000, len(shell_base)))]
    print("   %-12s x[%7.1f,%7.1f] y[%7.1f,%7.1f] z[%7.1f,%7.1f] mm"
          % (nm,
             min(d.x for d in dx) * 1000, max(d.x for d in dx) * 1000,
             min(d.y for d in dx) * 1000, max(d.y for d in dx) * 1000,
             min(d.z for d in dx) * 1000, max(d.z for d in dx) * 1000))


# ---------- 7) 导出（规格同 v8，§13.1） ----------
for ob in bpy.data.objects:
    ob.select_set(False)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0     # 🔴 导入器会按 USF 改写场景单位，导出前必须重设
bpy.context.view_layer.update()
if abs(bpy.context.scene.unit_settings.scale_length - 1.0) > 1e-6:
    fail("场景单位不是米")

bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=False, object_types={'MESH', 'ARMATURE'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='Y', axis_up='Z',
    use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
    mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
)
print("EXPORTED -> " + OUT)
try:
    shutil.copy2(OUT, BAK)
    print("已备份 -> " + BAK)
except Exception as e:
    print("备份失败（不致命）：%s" % e)
print("下一步门禁：python tools/face-pipeline/scripts/fbx_probe.py \"%s\" --full" % OUT)
sys.stdout.flush()

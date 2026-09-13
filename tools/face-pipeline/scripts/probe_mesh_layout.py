# 一次性探针：打印 v8 FBX 的对象 / 材质 / 形状键布局（v9 脚本的输入规格）
# 用法: blender --background --python _probe_v8_layout.py
import bpy, re, sys

SRC = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v8.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

print("=" * 70)
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        print("非网格: %-24s type=%s scale=%s" % (ob.name, ob.type, tuple(round(v, 4) for v in ob.scale)))
        continue
    me = ob.data
    mats = [m.name if m else "(None)" for m in me.materials]
    # 每个材质用到的顶点/面数
    per_mat = {}
    for p in me.polygons:
        per_mat[p.material_index] = per_mat.get(p.material_index, 0) + 1
    print("网格 %-24s verts=%-6d polys=%-6d scale=%s" % (ob.name, len(me.vertices), len(me.polygons), tuple(round(v, 4) for v in ob.scale)))
    for i, m in enumerate(mats):
        print("      mat[%d] %-24s polys=%d" % (i, m, per_mat.get(i, 0)))
    sk = me.shape_keys
    if sk:
        names = [kb.name for kb in sk.key_blocks]
        kt = [n for n in names if re.match(r'^KeyTime_\d+$', n)]
        nums = sorted(int(n.split('_')[1]) for n in kt)
        print("      shape_keys=%d  前3=%s  末3=%s" % (len(names), names[:3], names[-3:]))
        print("      🔴 完整顺序: %s" % ",".join(n.replace("KeyTime_", "") for n in names))
        print("      KeyTime 数=%d  编号范围 %s..%s  是否连续=%s"
              % (len(kt), nums[0] if nums else '-', nums[-1] if nums else '-',
                 (nums == list(range(nums[0], nums[-1] + 1))) if nums else '-'))
        # 每帧位移签名（前3帧）：和基础网格的差
        base = [v.co.copy() for v in me.vertices]
        for kb in sk.key_blocks[:3]:
            n = len(base)
            dx = [kb.data[i].co - base[i] for i in range(n)]
            print("      帧 %-12s 位移 x[%.1f,%.1f] y[%.1f,%.1f] z[%.1f,%.1f] mm"
                  % (kb.name,
                     min(d.x for d in dx) * 1000, max(d.x for d in dx) * 1000,
                     min(d.y for d in dx) * 1000, max(d.y for d in dx) * 1000,
                     min(d.z for d in dx) * 1000, max(d.z for d in dx) * 1000))
    else:
        print("      shape_keys=0")
    lo = [1e9] * 3; hi = [-1e9] * 3
    for v in me.vertices:
        p = ob.matrix_world @ v.co
        for i in range(3):
            lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
    print("      bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))
print("=" * 70)
sys.stdout.flush()

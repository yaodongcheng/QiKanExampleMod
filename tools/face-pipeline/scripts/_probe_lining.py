# _probe_lining.py —— 试验：把脸壳里的「口腔内衬」连通域在表情帧里**滞后**处理
# （位移 × 比例），看开口能不能凹进去、读起来像"张嘴"。
#
# 背景：嘴唇分开后，紧贴唇后那片衬里如果与嘴唇同步下移，开口就被"抹平" → 看着像没张嘴。
# 用法：blender --background --python _probe_lining.py -- <源FBX> <输出FBX> <比例> [帧...]
import sys

import bpy
import bmesh

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, K = args[0], args[1], float(args[2])
FRAMES = [int(x) for x in args[3:]] or list(range(60, 101))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def patch_fbx_importer():
    import inspect
    import io_scene_fbx.import_fbx as mod
    s = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in s:
        s = s.replace(bad, "pass  # patched")
        exec(compile(s, mod.__file__, "exec"), mod.__dict__)


patch_fbx_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

shell = None
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name.endswith(".0"):
        shell = o
if shell is None:
    raise SystemExit("找不到脸壳（*.0）")

me = shell.data
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
seen = [False] * len(bm.verts)
islands = []
for v in bm.verts:
    if seen[v.index]:
        continue
    stack = [v]
    seen[v.index] = True
    comp = []
    while stack:
        cur = stack.pop()
        comp.append(cur.index)
        for e in cur.link_edges:
            o2 = e.other_vert(cur)
            if not seen[o2.index]:
                seen[o2.index] = True
                stack.append(o2)
    islands.append(comp)
bm.free()
print("脸壳连通域 %d 个，最大的几个：%s"
      % (len(islands), sorted([(len(c), min(me.vertices[i].co.z for i in c),
                                max(me.vertices[i].co.z for i in c)) for c in islands], reverse=True)[:6]))

# 认「口腔内衬」：顶点数 300~900 且 z 落在嘴区 [1.55,1.65]、且 y 在脸前方
lining = None
for c in islands:
    if not (300 <= len(c) <= 900):
        continue
    zs = [me.vertices[i].co.z for i in c]
    ys = [me.vertices[i].co.y for i in c]
    if 1.55 <= min(zs) and max(zs) <= 1.66 and sum(ys) / len(ys) > 0.10:
        lining = c
        break
if lining is None:
    raise SystemExit("没认出内衬连通域（把上面的连通域清单发我）")
print("内衬连通域：%d 顶点，z[%.3f,%.3f]"
      % (len(lining), min(me.vertices[i].co.z for i in lining), max(me.vertices[i].co.z for i in lining)))

kb_by_n = {}
for kb in me.shape_keys.key_blocks:
    t = kb.name.rsplit("_", 1)
    if len(t) == 2 and t[1].isdigit():
        kb_by_n[int(t[1])] = kb
base = [v.co.copy() for v in me.vertices]
touched = 0
for n in FRAMES:
    kb = kb_by_n.get(n)
    if kb is None:
        continue
    for i in lining:
        p = kb.data[i].co
        d = p - base[i]
        if d.length < 1e-6:
            continue
        kb.data[i].co = base[i] + d * K
        touched += 1
print("内衬已按 ×%.2f 滞后：%d 个顶点坐标被改" % (K, touched))

bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=False, object_types={'MESH', 'ARMATURE'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='Y', axis_up='Z', use_mesh_modifiers=False, add_leaf_bones=False,
    bake_anim=False, mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False)
print("EXPORTED -> " + OUT)
sys.stdout.flush()

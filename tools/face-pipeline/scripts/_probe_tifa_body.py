import bpy, mathutils
SRC = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
try:
    bpy.ops.import_scene.fbx(filepath=SRC)
except Exception as e:
    print("IMPORT-FAIL:", e); raise SystemExit(1)
bpy.context.view_layer.update()
print("=== 对象清单 ===")
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        print("  [%s] %s" % (ob.type, ob.name)); continue
    lo=[1e9]*3; hi=[-1e9]*3
    for v in ob.data.vertices:
        p = ob.matrix_world @ v.co
        for i in range(3):
            lo[i]=min(lo[i],p[i]); hi[i]=max(hi[i],p[i])
    mats = [m.name if m else "None" for m in ob.data.materials]
    print("  MESH %-22s verts=%-7d z[%.4f,%.4f] y[%.4f,%.4f] x[%.4f,%.4f]  mats=%s"
          % (ob.name, len(ob.data.vertices), lo[2],hi[2], lo[1],hi[1], lo[0],hi[0], mats))
print()
print("=== 材质 → 用到的贴图 ===")
for m in bpy.data.materials:
    if not m.use_nodes: continue
    tex=[n.image.name for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
    if tex: print("  %-28s %s" % (m.name, tex))

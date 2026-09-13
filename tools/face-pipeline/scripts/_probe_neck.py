import bpy, mathutils
SRC = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()
body = next(o for o in bpy.data.objects if o.type=='MESH' and o.name=='body')
pts = [body.matrix_world @ v.co for v in body.data.vertices]
print("body bbox: z[%.4f,%.4f]  x[%.4f,%.4f] y[%.4f,%.4f]" % (
    min(p.z for p in pts), max(p.z for p in pts),
    min(p.x for p in pts), max(p.x for p in pts),
    min(p.y for p in pts), max(p.y for p in pts)))
print("\n按高度切片看横截面（判断哪里是脖子、哪里是肩膀/胸）:")
print("   z中心    顶点数   x宽     y深     |x|中位")
import statistics
z0,z1 = min(p.z for p in pts), max(p.z for p in pts)
step = 0.02
z = z1
while z > 1.28:
    band=[p for p in pts if z-step <= p.z < z]
    if band:
        xs=[abs(p.x) for p in band]; ys=[p.y for p in band]
        print("   %.3f   %5d   %.4f  %.4f   %.4f" % (
            z-step/2, len(band), max(p.x for p in band)-min(p.x for p in band),
            max(ys)-min(ys), statistics.median(xs)))
    z -= step
# 用躯干材质的顶点单独看
idx=[i for i,m in enumerate(body.data.materials) if m and 'torso' in m.name.lower()]
print("\n躯干材质顶点数 =", len(idx) and sum(1 for p in body.data.polygons if p.material_index in idx) or 0)

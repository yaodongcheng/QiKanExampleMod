import bpy, mathutils
V8 = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources\head_tifa_a_v8.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=V8)
bpy.context.view_layer.update()
meshes=[o for o in bpy.data.objects if o.type=='MESH']
for ob in meshes:
    ob.parent=None; ob.matrix_world=mathutils.Matrix.Identity(4)
bpy.context.view_layer.update()

def pts_of(suffix):
    out={}
    for ob in meshes:
        idx=set(i for i,m in enumerate(ob.data.materials) if m and m.name.lower().endswith(suffix))
        if not idx: continue
        vs=set()
        for p in ob.data.polygons:
            if p.material_index in idx: vs.update(p.vertices)
        out[ob.name]=[ob.data.vertices[i].co.copy() for i in vs]
    return out

def bbox(ps):
    return (min(p.x for p in ps),max(p.x for p in ps),min(p.y for p in ps),max(p.y for p in ps),min(p.z for p in ps),max(p.z for p in ps))

shell = pts_of("head_tifa_a")
shellpts=[p for v in shell.values() for p in v]
print("脸壳 bbox: x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % bbox(shellpts))

for suf,label in [("_eye","眼球"),("_brow","眉毛"),("_lash","睫毛"),("_shadow","眼影"),("_mouth","嘴")]:
    g=pts_of(suf)
    if not g: print("  %s(%s): 无"%(label,suf)); continue
    ps=[p for v in g.values() for p in v]
    b=bbox(ps)
    # 脸壳在该部件 z 带、中轴 ±2cm 内的最前点 = 该处脸面
    zlo,zhi=b[4],b[5]
    surf=[p.y for p in shellpts if zlo-0.005<=p.z<=zhi+0.005 and abs(p.x)<=0.02]
    smax=max(surf) if surf else float('nan')
    print("  %-4s(%s): x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]  | 该处脸面 y_max=%.4f  → %s" % (
        label,suf,b[0],b[1],b[2],b[3],b[4],b[5],smax,
        ("🔴 凸出 %.4f m"%(b[3]-smax)) if smax==smax and b[3]>smax+0.002 else "OK"))

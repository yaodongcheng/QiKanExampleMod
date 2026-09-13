import bpy, inspect, collections

# 🔴 TWT 的 FBX 带 morph 但缺 FullWeights → Blender 5.2 导入器断言崩溃。
#    内存级补丁：读源码、把 assert 换成 pass、重新 exec 进模块命名空间（不落盘改安装文件）。
def patch_fbx_importer():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
        print("[patch] import_fbx 断言已内存级替换")
    else:
        print("[patch] 未找到断言（版本可能已改）")
patch_fbx_importer()

SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\core_game\fbx\head\head_female_a.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()
ms=[o for o in bpy.data.objects if o.type=='MESH']
ar=[o for o in bpy.data.objects if o.type=='ARMATURE']
print("网格 %d 个, 骨架 %d 个" % (len(ms), len(ar)))
for ob in ms:
    print("  obj=%-24s verts=%-6d 顶点组=%d" % (ob.name, len(ob.data.vertices), len(ob.vertex_groups)))
    neck=collections.Counter(); allw=collections.Counter()
    for v in ob.data.vertices:
        for g in v.groups:
            if g.weight>0.01:
                allw[ob.vertex_groups[g.group].name]+=1
                p=ob.matrix_world @ v.co
                if 1.40<=p.z<=1.56: neck[ob.vertex_groups[g.group].name]+=1
    print("      整体权重:", dict(allw.most_common(6)))
    print("      脖子段(z1.40~1.56)权重:", dict(neck.most_common(6)))

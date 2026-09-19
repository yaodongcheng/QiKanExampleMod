# _probe_mouth_apart.py —— 量「张嘴」到底开没开：把正面中线附近的顶点按 z 分层，
# 逐层打印某帧的 Δz（负 = 往下走）。张嘴 = 上唇几乎不动、下唇明显往下 → 两层之间出现落差。
#
# 用法：blender --background --python _probe_mouth_apart.py -- <fbx> [对象名子串] [帧号...]
import bpy, sys


def patch_fbx_importer():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


patch_fbx_importer()
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
path = args[0]
hint = args[1] if len(args) > 1 else None
frames = [int(x) for x in args[2:]] or [71, 99]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
bpy.context.view_layer.update()

print("文件: %s" % path)
for ob in [o for o in bpy.data.objects if o.type == 'MESH']:
    if hint and hint.lower() not in ob.name.lower():
        continue
    co = [v.co for v in ob.data.vertices]
    kb = {}
    for k in ob.data.shape_keys.key_blocks:
        tail = k.name.rsplit("_", 1)
        if len(tail) == 2 and tail[1].isdigit():
            kb[int(tail[1])] = k
    print("\n===== %s  顶点 %d" % (ob.name, len(co)))
    # 正面中线区：|x| ≤ 0.03、y ≥ 0.10（朝向 +Y 是脸的正前方）
    band = [i for i, c in enumerate(co) if abs(c.x) <= 0.03 and c.y >= 0.10]
    print("  正面中线顶点 %d 个" % len(band))
    for f in frames:
        k = kb.get(f)
        if k is None:
            print("  帧 %d：缺" % f)
            continue
        print("  ── f%d ──  z 分层（10mm）: 平均Δz(mm) / 该层顶点数" % f)
        for z0 in [1.50, 1.53, 1.56, 1.59, 1.62, 1.65, 1.68, 1.71]:
            sel = [i for i in band if z0 <= co[i].z < z0 + 0.03]
            if not sel:
                continue
            dz = [(k.data[i].co.z - co[i].z) * 1000 for i in sel]
            mz = sum(dz) / len(dz)
            print("    z %.2f~%.2f  %+7.2f mm  (%d 点, 最大下移 %.2f)"
                  % (z0, z0 + 0.03, mz, len(sel), -min(dz)))
    # 咬合线：上下唇在同一 x=0 剖面上的最近距离（张嘴时这个距离会变大）
sys.stdout.flush()

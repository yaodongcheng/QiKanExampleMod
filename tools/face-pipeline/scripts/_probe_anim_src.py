# _probe_anim_src.py —— 打印一个 FBX 里【每个子网格】的 60..100 号（表情/口型）帧位移签名。
#
# 为什么要分件看：原版头的三个件各自带自己的表情位移场 ——
#   脸壳（嘴唇/下巴/眼睑）、眼球件（眼球转动 EyesRight/Left/Up/Down）、嘴件（牙/舌跟下颌走）。
#   搬通道时若把"脸壳的场"无脑套到眼球件上，眼球就不会转（只能被眼睑拖着走）。
#
# 用法：blender --background --python _probe_anim_src.py -- <fbx> [起始帧] [结束帧]
import bpy, sys


def patch_fbx_importer():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


patch_fbx_importer()

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
path = args[0]
lo = int(args[1]) if len(args) > 1 else 60
hi = int(args[2]) if len(args) > 2 else 100

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
bpy.context.view_layer.update()

print("文件: %s" % path)
for ob in [o for o in bpy.data.objects if o.type == 'MESH']:
    co = [v.co for v in ob.data.vertices]
    sk = ob.data.shape_keys
    print("\n===== %-28s 顶点 %d   形状键 %d" % (ob.name, len(co), len(sk.key_blocks) if sk else 0))
    if not sk:
        continue
    bynum = {}
    for kb in sk.key_blocks:
        nm = kb.name
        if not nm.startswith("KeyTime_"):
            continue
        try:
            bynum[int(nm.split("_")[1])] = kb
        except ValueError:
            pass
    if not bynum:      # 原版 dump 保留的是 FaceWidth_1/JawDrop_71 这种名字 → 按尾部数字取
        for kb in sk.key_blocks:
            parts = kb.name.rsplit("_", 1)
            if len(parts) == 2 and parts[1].isdigit():
                bynum[int(parts[1])] = kb
    print("  %-6s %-9s %-11s %-28s %s" % ("帧", "移动点", "最大位移mm", "位移质心", "位移最大点"))
    for n in range(lo, hi + 1):
        kb = bynum.get(n)
        if kb is None:
            print("  f%-5d (缺)" % n)
            continue
        cnt = 0
        sx = sy = sz = 0.0
        mx = 0.0
        mxp = None
        for i in range(len(co)):
            d = kb.data[i].co - co[i]
            m = d.length
            if m > mx:
                mx = m; mxp = co[i]
            if m >= 5e-5:
                cnt += 1; sx += co[i].x; sy += co[i].y; sz += co[i].z
        if cnt == 0:
            print("  f%-5d %-9d %-11.2f (未形变)" % (n, 0, mx * 1000))
            continue
        print("  f%-5d %-9d %-11.2f (%6.3f,%6.3f,%6.3f)          (%6.3f,%6.3f,%6.3f)"
              % (n, cnt, mx * 1000, sx / cnt, sy / cnt, sz / cnt, mxp.x, mxp.y, mxp.z))
sys.stdout.flush()

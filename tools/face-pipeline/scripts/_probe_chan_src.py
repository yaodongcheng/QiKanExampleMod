# _probe_chan_src.py —— 探一个 FBX 的基础网格 + 每条 KeyTime_N 位移场「落在哪」。
#
# 用途：定位「拉杆错位」发生在管线哪一环。
#   导入方式与 transfer_channels.py 的 load() **逐字一致**（Blender FBX 默认轴），
#   所以在这里看到的坐标 = 搬通道时 KD 树看到的坐标。
#
# 用法：
#   blender --background --python _probe_chan_src.py -- <fbx> [对象名子串]
import bpy, sys


def patch_fbx_importer():
    """tpac → FBX 导出的 morph 缺 FullWeights，Blender 5.2 导入器会断言崩。
    内存级替换断言（不碰 Blender 安装文件）——范本 _probe_vanilla_head.py。"""
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
        print("[patch] import_fbx 断言已内存级替换")
    else:
        print("[patch] 未找到断言（版本可能已改）")


patch_fbx_importer()


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def main():
    args = argv_after_ddash()
    path = args[0]
    hint = args[1] if len(args) > 1 else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    objs = [o for o in bpy.data.objects if o.type == 'MESH']
    print("文件: %s" % path)
    print("网格对象: %s" % [(o.name, len(o.data.vertices)) for o in objs])
    # 🔴 材质名清单：换 FBX 时必须逐字不变（变了 = 编辑器建新材质 = 白板 + 材质配方对不上，铁律 32）
    print("材质清单:")
    for o in objs:
        print("   %-30s %s" % (o.name, [m.name if m else "(None)" for m in o.data.materials]))
    for o in objs:
        if hint and hint.lower() not in o.name.lower():
            continue
        mw = o.matrix_world
        print("\n===== %s  顶点 %d =====" % (o.name, len(o.data.vertices)))
        print("对象世界矩阵（transfer_channels 会忽略它！）:")
        for row in mw:
            print("    [%9.5f %9.5f %9.5f %9.5f]" % tuple(row))
        co = [v.co for v in o.data.vertices]
        xs = [c.x for c in co]; ys = [c.y for c in co]; zs = [c.z for c in co]
        print("局部坐标 bbox  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]"
              % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
        # 世界坐标 bbox（把矩阵算上）
        wco = [mw @ v.co for v in o.data.vertices]
        print("世界坐标 bbox  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]"
              % (min(c.x for c in wco), max(c.x for c in wco),
                 min(c.y for c in wco), max(c.y for c in wco),
                 min(c.z for c in wco), max(c.z for c in wco)))

        sk = o.data.shape_keys
        if sk is None:
            print("  （无形状键）")
            continue
        names = [kb.name for kb in sk.key_blocks]
        print("形状键 %d 个: %s%s" % (len(names), names[:6], " ..." if len(names) > 6 else ""))
        print("\n  %-14s %-8s %-10s %-28s %s" % ("形状键", "移动点", "最大位移mm", "位移质心(局部坐标)", "位移最大点"))
        for kb in sk.key_blocks:
            nm = kb.name
            if not nm.startswith("KeyTime_"):
                continue
            n = int(nm.split("_")[1])
            if not (0 <= n <= 100):
                continue
            n_move = 0
            sx = sy = sz = 0.0
            mx = 0.0
            mxp = None
            for i in range(len(co)):
                d = kb.data[i].co - co[i]
                mag = d.length
                if mag > mx:
                    mx = mag; mxp = co[i]
                if mag >= 5e-5:
                    n_move += 1
                    sx += co[i].x; sy += co[i].y; sz += co[i].z
            if n_move == 0:
                print("  %-14s %-8d %-10.2f %s" % (nm, 0, mx * 1000, "(未形变)"))
                continue
            print("  %-14s %-8d %-10.2f (%7.3f,%7.3f,%7.3f)          (%6.3f,%6.3f,%6.3f)"
                  % (nm, n_move, mx * 1000, sx / n_move, sy / n_move, sz / n_move,
                     mxp.x, mxp.y, mxp.z))


main()

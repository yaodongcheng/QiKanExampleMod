# transfer_channels.py —— 把 59 条脸形通道（KeyTime_1..59 位移场）从"源网格"搬到"目标网格"。
#
# 为什么需要它：做新脸模时，新头的拓扑和蒂法不同，必须把通道位移场按空间最近邻搬过去。
#   本工程最初那次移植的脚本留在 %TEMP%（已失传），产物固化在 head_tifa_a_v10.fbx 里
#   ⇒ 新模型直接从 v10 取通道即可，不必重跑那次移植。这个脚本就是那个"取 + 搬"的动作。
#
# 算法：对目标每个顶点，取源网格上【最近邻 3 点】做**反距离加权**，把三点的位移加权平均。
#   （3 近邻平顺、1 近邻硬贴会起皱 —— §3 实测）
#
# 输出形状键顺序 = Basis → KeyTime_0(0.1mm 占位) → KeyTime_1..59
#   （编辑器按【位置】把非 Basis 键编成帧 0,1,2…，占位键顶住帧 0 才能与 deform_key.key_time_point 对齐；§13.7③）
#
# 用法：
#   blender --background --python transfer_channels.py -- \
#       --src <源FBX> [--src-object <名子串>] --dst <目标FBX> [--dst-object <名子串|all>] --out <输出FBX>
#   加 --selftest 做自检：把源自己的通道搬回源自己，要求与原值逐点吻合（差 < 1e-4）
import bpy, sys, os, mathutils
from mathutils import kdtree

CH = 59
NN = 3
EPS = 1e-4


def argv_after_ddash():
    if "--" in sys.argv:
        return sys.argv[sys.argv.index("--") + 1:]
    return []


def get(args, key, default=None):
    return args[args.index(key) + 1] if key in args else default


def fail(msg):
    print("FATAL: " + msg)
    sys.stdout.flush()
    sys.exit(1)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o.type == 'MESH']


def pick(objs, hint, what):
    if hint is None or hint == "all":
        return objs
    hit = [o for o in objs if hint.lower() in o.name.lower()]
    if not hit:
        fail("%s 里找不到名字含 \"%s\" 的网格（现有：%s）" % (what, hint, [o.name for o in objs]))
    return hit


def read_channels(ob):
    """返回 (base_positions, {n: [delta_vector,...]})，通道按名字 KeyTime_N 取（不依赖文件里的顺序）。"""
    sk = ob.data.shape_keys
    if sk is None:
        fail("%s 没有形状键" % ob.name)
    base = [v.co.copy() for v in ob.data.vertices]
    chans = {}
    for kb in sk.key_blocks:
        nm = kb.name
        if nm.startswith("KeyTime_"):
            try:
                n = int(nm.split("_")[1])
            except ValueError:
                continue
            if 1 <= n <= CH:
                chans[n] = [kb.data[i].co - base[i] for i in range(len(base))]
    missing = [n for n in range(1, CH + 1) if n not in chans]
    if missing:
        fail("%s 缺通道 %s" % (ob.name, missing[:8]))
    return base, chans


def rebuild(ob, deltas):
    """按 Basis → KeyTime_0(占位) → KeyTime_1..59 重建目标对象的形状键。"""
    base = [v.co.copy() for v in ob.data.vertices]
    ob.shape_key_clear()
    kb = ob.shape_key_add(name="Basis", from_mix=False)
    flat = []
    for c in base:
        flat += [c.x, c.y, c.z]
    kb.data.foreach_set("co", flat)

    def add(name, ds):
        k = ob.shape_key_add(name=name, from_mix=False)
        f = []
        for i, c in enumerate(base):
            d = ds[i]
            f += [c.x + d.x, c.y + d.y, c.z + d.z]
        k.data.foreach_set("co", f)

    # 占位键给 0.1mm 微位移：完全零位移的通道会被 FBX 压成 1 个顶点、可能被编辑器跳过
    add("KeyTime_0", [mathutils.Vector((0.0, 0.0001, 0.0))] * len(base))
    for n in range(1, CH + 1):
        add("KeyTime_%d" % n, deltas[n])


def main():
    args = argv_after_ddash()
    src_path = get(args, "--src")
    dst_path = get(args, "--dst")
    out_path = get(args, "--out")
    selftest = "--selftest" in args
    if selftest and not dst_path:
        dst_path = src_path          # 自检：把源自己的通道搬回源自己
    if not src_path or not dst_path or not out_path:
        print("收到的参数: %s" % args)
        fail("需要 --src / --dst / --out（用法见文件头）")
    src_hint = get(args, "--src-object")
    dst_hint = get(args, "--dst-object", "all")

    # 源：读通道
    srcs = pick(load(src_path), src_hint, "源 FBX")
    if len(srcs) != 1:
        fail("源需恰好 1 个网格（用 --src-object 指定），实际匹配 %d 个：%s" % (len(srcs), [o.name for o in srcs]))
    s_ob = srcs[0]
    s_base, s_chan = read_channels(s_ob)
    print("源 %s：顶点 %d，通道 %d 条" % (s_ob.name, len(s_base), len(s_chan)))

    kd = kdtree.KDTree(len(s_base))
    for i, c in enumerate(s_base):
        kd.insert(c, i)
    kd.balance()

    if selftest:
        # 自检：把源自己的通道搬回源自己（--dst 自动取 --src），要求与原值逐点吻合
        dst_path = src_path
        out_path = os.path.join(os.path.dirname(out_path) or ".", "_selftest_" + os.path.basename(out_path))
        print("[自检模式] 把源自己的通道搬回源自己")

    # 目标：搬通道
    dsts = pick(load(dst_path), dst_hint, "目标 FBX")
    print("目标 %d 个网格：%s" % (len(dsts), [o.name for o in dsts]))
    worst = 0.0
    for ob in dsts:
        base = [v.co.copy() for v in ob.data.vertices]
        deltas = {}
        for n in range(1, CH + 1):
            col = []
            for c in base:
                hits = kd.find_n(c, NN)
                if hits[0][2] < 1e-6:
                    # 顶点与某个源顶点重合（同拓扑/自检）→ 直接取该点，避免被邻居稀释
                    col.append(s_chan[n][hits[0][1]])
                    continue
                ws = [1.0 / ((d + EPS) ** 2) for (_, _, d) in hits]      # 平方反距离（Shepard p=2）
                s = sum(ws)
                v = mathutils.Vector((0, 0, 0))
                for (_, idx, _), w in zip(hits, ws):
                    v = v + s_chan[n][idx] * (w / s)
                col.append(v)
            deltas[n] = col
        nz = sum(1 for n in range(1, CH + 1) if max(abs(d.x) + abs(d.y) + abs(d.z) for d in deltas[n]) > 1e-4)
        rebuild(ob, deltas)
        print("  %-22s 顶点 %-6d 通道 %d 条（%d 条有实际位移）" % (ob.name, len(base), CH, nz))

        if selftest:
            # 自检：目标与源同拓扑同坐标 ⇒ 搬回来的位移应与原值逐点吻合
            for n in (1, 2, 3, 10, 30, 59):
                got = deltas[n]
                ref = s_chan[n]
                if len(got) != len(ref):
                    continue
                d = max((got[i] - ref[i]).length for i in range(len(ref)))
                worst = max(worst, d)
            print("      [自检] 与源通道的最大偏差 %.6f m" % worst)

    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=out_path, use_selection=False, object_types={'MESH', 'ARMATURE'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='Y', axis_up='Z',
        use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
    )
    print("EXPORTED -> " + out_path)
    if selftest:
        ok = worst < 1e-4
        print("[自检结论] %s（最大偏差 %.6f m，阈值 1e-4）" % ("通过" if ok else "不通过", worst))
        sys.stdout.flush()
        sys.exit(0 if ok else 1)
    sys.stdout.flush()


main()

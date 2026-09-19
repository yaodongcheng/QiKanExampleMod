# transfer_channels.py —— 把【101 条】头部顶点位移场（KeyTime_1..100）从"源网格"搬到"目标网格"。
#
# 为什么需要它：做新脸模时，新头的拓扑和源头不同，必须把位移场按空间最近邻搬过去。
#   本工程最初那次移植的脚本留在 %TEMP%（已失传），产物固化在 head_tifa_a_v10.fbx 里。
#
# ## 101 条通道分两段（🔴 2026-09-19 补第二段）
#   1..59   脸形通道 —— 捏脸拉杆（皮肤 deform_keys 的 key_time_point）驱动。
#   60..100 表情/口型通道 —— 引擎的 morph_anims 片段（face_01..12 / Speak / JawDrop …）驱动。
# 🔴 只搬了前一段 = **说话时嘴不动、脸没有表情**（实机 2026-09-19 用户报的"亨利没表情"）。
#   帧号↔语义的全表在 `TpacTool.IO/Model/MorphNameMapping.cs`（f60 EyesRight … f71 JawDrop … f99 Speak / f100 Yell）。
#
# ## 🔴 按件对位搬（2026-09-19）
#   原版头的**每个子网格各带自己的位移场**：脸壳 = 唇/下巴/眼睑/眉，眼球件 = 眼球转动，
#   嘴件 = 牙/舌（跟下颌走）。所以表情段按件对位搬（--anim-objects 按名字序 → 目标件按名字序）。
#   若把脸壳的场无脑套到眼球件上，眼球就只会被眼睑蹭一下（1mm 级），**转不起来**（原版是整颗转 ~10mm）。
#
# 算法：对目标每个顶点，取源网格上【最近邻 3 点】做**反距离加权**，把三点的位移加权平均。
#   （3 近邻平顺、1 近邻硬贴会起皱 —— 蒂法文档 §3 实测）
#
# 输出形状键顺序 = Basis → KeyTime_0(0.1mm 占位) → KeyTime_1..100
#   （编辑器按【位置】把非 Basis 键编成帧 0,1,2…，占位键顶住帧 0 才能与 deform_keys / morph_anims 的帧号对齐；§13.7③）
#
# 用法：
#   blender --background --python transfer_channels.py -- \
#       --src <脸形源FBX> [--src-object <名子串>] \
#       [--anim-src <表情源FBX>] [--anim-objects <名,名,名>] \
#       --dst <目标FBX> [--dst-object <名子串|all>] --out <输出FBX>
#   加 --selftest 做自检：把源自己的通道搬回源自己，要求与原值逐点吻合（差 < 1e-4）
import bpy, sys, os, mathutils
from mathutils import kdtree

FACE_CH = 59          # 脸形通道上限（1..59）= 皮肤 deform_keys 能拉到的范围
CH_MAX = 100          # 总通道上限（60..100 = 表情/口型，引擎 morph_anims 驱动）
NN = 3
EPS = 1e-4

_PATCHED = False


def patch_fbx_importer():
    """tpac → FBX 导出的 morph 缺 FullWeights，Blender 5.2 导入器会断言崩（本工程老坑）。
    内存级替换断言，不碰 Blender 安装文件 —— 范本 `_probe_vanilla_head.py`。
    🔴 换了通道源（xxFemale / 原版男头的 dump）之后**必须**有这道：源的 101 个 morph 全带这个毛病。"""
    global _PATCHED
    if _PATCHED:
        return
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
    _PATCHED = True


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
    patch_fbx_importer()
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


def chan_index(kb_name):
    """形状键名 → 帧号。

    🔴 两种命名都要认（同一个 tpac，不同 dump 出来的 FBX 名字不一样）：
      · `KeyTime_71`    —— xxFemale dump（本工程现行脸形源）
      · `JawDrop_71`    —— 原版 core_game dump（保住了原版帧名，见 MorphNameMapping）
    两种都是【尾部数字 = 帧号】，所以直接按尾部数字取，不依赖前缀。
    """
    tail = kb_name.rsplit("_", 1)
    if len(tail) == 2 and tail[1].isdigit():
        return int(tail[1])
    return None


def read_channels(ob, lo=1, hi=CH_MAX):
    """返回 (base_positions, {n: [delta_vector,...]})，通道按名字尾号取（不依赖文件里的顺序）。"""
    sk = ob.data.shape_keys
    if sk is None:
        fail("%s 没有形状键" % ob.name)
    base = [v.co.copy() for v in ob.data.vertices]
    chans = {}
    for kb in sk.key_blocks:
        n = chan_index(kb.name)
        if n is None or not (lo <= n <= hi):
            continue
        chans[n] = [kb.data[i].co - base[i] for i in range(len(base))]
    missing = [n for n in range(lo, hi + 1) if n not in chans]
    if missing:
        fail("%s 缺通道 %s%s" % (ob.name, missing[:8], " …" if len(missing) > 8 else ""))
    return base, chans


def make_kd(base):
    kd = kdtree.KDTree(len(base))
    for i, c in enumerate(base):
        kd.insert(c, i)
    kd.balance()
    return kd


def remap(target_pts, kd, src_chan):
    """把源的一条位移场按最近邻 3 点反距离加权搬到目标顶点上。"""
    col = []
    for c in target_pts:
        hits = kd.find_n(c, NN)
        if hits[0][2] < 1e-6:
            # 顶点与某个源顶点重合（同拓扑/自检）→ 直接取该点，避免被邻居稀释
            col.append(src_chan[hits[0][1]])
            continue
        ws = [1.0 / ((d + EPS) ** 2) for (_, _, d) in hits]      # 平方反距离（Shepard p=2）
        s = sum(ws)
        v = mathutils.Vector((0, 0, 0))
        for (_, idx, _), w in zip(hits, ws):
            v = v + src_chan[idx] * (w / s)
        col.append(v)
    return col


def rebuild(ob, deltas):
    """按 Basis → KeyTime_0(占位) → KeyTime_1..100 重建目标对象的形状键。"""
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
    for n in range(1, CH_MAX + 1):
        add("KeyTime_%d" % n, deltas[n])


def count_real(deltas, lo, hi):
    return sum(1 for n in range(lo, hi + 1)
               if max(abs(d.x) + abs(d.y) + abs(d.z) for d in deltas[n]) > 1e-4)


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
    anim_path = get(args, "--anim-src") or src_path
    anim_objs_arg = get(args, "--anim-objects")

    # ---- 脸形段（1..59）的源：单件（脸壳），沿用现状 ----
    # 🔴 `load()` 会 read_factory_settings 重置场景 —— 源的数据必须在**下一次 load 之前**
    #    全部拷成纯 Python 数据（Vector 是拷贝）。留对象引用跨 load 使用 = ReferenceError 崩。
    srcs = pick(load(src_path), src_hint, "源 FBX")
    if len(srcs) != 1:
        fail("源需恰好 1 个网格（用 --src-object 指定），实际匹配 %d 个：%s" % (len(srcs), [o.name for o in srcs]))
    s_base, s_chan = read_channels(srcs[0])
    src_name = srcs[0].name          # 🔴 名字也要在 load 之前拷出来（load 之后对象就失效了）
    print("脸形源 %s：顶点 %d，通道 %d 条（1..%d）" % (src_name, len(s_base), len(s_chan), FACE_CH))

    # ---- 表情段（60..100）的源：可另给，且按件对位 ----
    # anim_parts[i] = (base, chan) 纯数据，与目标件 dsts[i] 一一对应
    anim_parts = None
    if anim_objs_arg:
        want = [x.strip() for x in anim_objs_arg.split(",")]
        a_objs = pick(load(anim_path), None, "表情源 FBX")
        by_name = {}
        for o in a_objs:
            by_name.setdefault(o.name, o)
        miss = [w for w in want if w not in by_name]
        if miss:
            fail("表情源 %s 里找不到 %s（现有：%s）" % (anim_path, miss, list(by_name)))
        anim_parts = [read_channels(by_name[w], FACE_CH + 1, CH_MAX) for w in want]
        print("表情源 %s：按件对位 %s（各 %d 条通道）"
              % (anim_path, want, CH_MAX - FACE_CH))
    else:
        a_base, a_chan = read_channels(srcs[0], FACE_CH + 1, CH_MAX)   # 同一件套到所有目标件（旧行为）
        anim_parts = None
        print("表情源：未指定 --anim-objects → 用 %s 这一件套到所有目标件（旧行为）" % src_name)

    if selftest:
        # 自检：把源自己的通道搬回源自己（--dst 自动取 --src），要求与原值逐点吻合
        dst_path = src_path
        out_path = os.path.join(os.path.dirname(out_path) or ".", "_selftest_" + os.path.basename(out_path))
        print("[自检模式] 把源自己的通道搬回源自己")

    # ---- 目标：搬通道 ----
    dsts = pick(load(dst_path), dst_hint, "目标 FBX")
    dsts = sorted(dsts, key=lambda o: o.name)          # 🔴 名字序 = 引擎的件序（脸 .0 / 眼 .1 / 嘴 .2）
    print("目标 %d 个网格：%s" % (len(dsts), [o.name for o in dsts]))
    dst_part = None
    if anim_parts is not None:
        if len(anim_parts) == 1:
            dst_part = [0] * len(dsts)                 # 单件源兜底：套到所有目标件
        elif len(anim_parts) == len(dsts):
            dst_part = list(range(len(dsts)))
        else:
            fail("表情源件数 %d ≠ 目标件数 %d —— 逐件对位必须数量相同（目标：%s）"
                 % (len(anim_parts), len(dsts), [o.name for o in dsts]))
        print("对位：%s" % ", ".join("%s ← %s" % (dsts[i].name, want[dst_part[i]])
                                    for i in range(len(dsts))))

    kd_face = make_kd(s_base)
    worst = 0.0
    for i, ob in enumerate(dsts):
        base = [v.co.copy() for v in ob.data.vertices]
        deltas = {}
        for n in range(1, FACE_CH + 1):
            deltas[n] = remap(base, kd_face, s_chan[n])
        if anim_parts is None:
            for n in range(FACE_CH + 1, CH_MAX + 1):
                deltas[n] = remap(base, kd_face, a_chan[n])
            cur_anim_chan = a_chan
            anim_note = "同脸形源 %s" % src_name
        else:
            a_base, a_chan = anim_parts[dst_part[i]]
            kd_anim = make_kd(a_base)
            for n in range(FACE_CH + 1, CH_MAX + 1):
                deltas[n] = remap(base, kd_anim, a_chan[n])
            cur_anim_chan = a_chan
            anim_note = "表情源 %s" % want[dst_part[i]]
        nf = count_real(deltas, 1, FACE_CH)
        na = count_real(deltas, FACE_CH + 1, CH_MAX)
        rebuild(ob, deltas)
        print("  %-22s 顶点 %-6d 脸形 %d/%d 条有位移 · 表情 %d/%d 条有位移（%s）"
              % (ob.name, len(base), nf, FACE_CH, na, CH_MAX - FACE_CH, anim_note))

        if selftest:
            # 自检：目标与源同拓扑同坐标 ⇒ 搬回来的位移应与原值逐点吻合
            for n in (1, 2, 3, 10, 30, 59, 71, 88, 99):
                got = deltas[n]
                ref = (s_chan if n <= FACE_CH else cur_anim_chan)[n]
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

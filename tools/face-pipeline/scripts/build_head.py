# build_head.py —— 全身/任意源模型 FBX → 骑砍2 可用头部 FBX
#                  （一条龙：挑件 → 归并 4 件 → 自动定向 → 锚点标定 → 绑官方骨架 → 导出）
#
# 这是 §19 工作流的【阶段 ①装配对齐 + ②结构 + ⑤骨架 + ⑥导出】。
#   阶段 ③（59 条形变通道）随后跑 transfer_channels.py 即可。
#
# 为什么需要它：源模型通常是**全身**（含身体/衣服/头发/首饰），而引擎要的是**只有头的 4 件 metamesh**，
#   且必须落在游戏头部空间（脸朝 +Y、米级、站在脖子上）。这一步以前是人工 + 失传脚本，现在脚本化。
#
# 标定依据（与 §12.10 / §13.7④ 一致）：
#   锚点 = 眼球中心 E 与嘴中心 M
#     · 缩放 k = 原版"眼↔嘴"竖直间距 0.0728 / 源模型的同距离
#     · 平移：把 E 送到原版眼球件中心 (0, 0.1291, 1.6795)（= 原版 `.6` eye_mat 包围盒中心）
#   定向：脸朝 = 头壳中心 C → 眼球中心 E 的方向；上 = 嘴 → 眼 的方向（正交化）
#   🔴 整段变换对【全部保留件】统一施加 —— 保持源模型自己的相对装配（蒂法那次就是在这里栽的）
#
# 用法：
#   blender --background --python build_head.py -- \
#       --src <源FBX> --out <输出FBX> --name head_tifa_a \
#       [--drop "hair,body,dress,eyebrow,eyeshadow,earring,necklace,bracelet,anklet,panty,shoe,sock,weapon,cloth,armor"] \
#       [--scale auto|<k>] [--no-skeleton] [--list-only]
#
# 输出：4 个网格 `<name>.0`(脸) `.1`(嘴) `.2`(眼) `.3`(睫)，材质同名 `_mouth/_eye/_lash` 后缀，
#       已刚性绑定官方骨架 `bip01_head_13`，导出规格 = USF 100 / UpAxis 2 / 节点零变换。
import bpy, bmesh, sys, os, math, mathutils
from mathutils import Vector, Matrix

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"
HEAD_BONE = "bip01_head_13"
TARGET_EYE = Vector((0.0, 0.1291, 1.6795))      # 原版 head_female_a `.6`(eye_mat) 包围盒中心
TARGET_EYE_MOUTH_DZ = 1.6795 - 1.6067           # 0.0728 = 原版"眼中心 → 嘴中心"竖直距离

DROP_DEFAULT = ("hair", "beard", "body", "dress", "cloth", "armor", "shoe", "sock",
                "panty", "underwear", "necklac", "earring", "bracelet", "anklet", "ring",
                "weapon", "sword", "shield", "eyebrow", "brow", "eyeshadow", "shadow")

ROLE_ORDER = ["face", "mouth", "eye", "lash"]
ROLE_RULES = [                       # 顺序敏感：lash 必须在 eye 之前判（eyelash 含 eye 子串）
    ("lash",  ("eyelash", "lash", "cilia")),
    ("eye",   ("eyeball", "eyeball", "eye")),
    ("mouth", ("mouth", "lip", "teeth", "tongue")),
    ("face",  ("head", "face", "skull")),
]


def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def get(args, key, default=None):
    return args[args.index(key) + 1] if key in args else default


def fail(msg):
    print("FATAL: " + msg)
    sys.stdout.flush()
    sys.exit(1)


def v3(v):
    return "(%.3f, %.3f, %.3f)" % (v[0], v[1], v[2])


def bbox(objs):
    lo = [1e9] * 3; hi = [-1e9] * 3
    for ob in objs:
        for v in ob.data.vertices:
            p = ob.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
    return Vector(lo), Vector(hi)


def center(objs):
    lo, hi = bbox(objs)
    return (lo + hi) / 2.0


def classify(name, drop):
    n = name.lower()
    for k in drop:
        if k in n:
            return None
    for role, keys in ROLE_RULES:
        for k in keys:
            if k in n:
                return role
    return None


def main():
    a = args_after_ddash()
    src = get(a, "--src")
    out = get(a, "--out")
    name = get(a, "--name", "head_custom")
    if not src or not out:
        fail("需要 --src / --out（用法见文件头）")
    drop = tuple(x.strip().lower() for x in get(a, "--drop", ",".join(DROP_DEFAULT)).split(",") if x.strip())
    scale_opt = get(a, "--scale", "auto")
    list_only = "--list-only" in a
    no_skel = "--no-skeleton" in a

    # ---------- 1) 导入 + 挑件 ----------
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("导入 %d 个网格" % len(meshes))

    picked = {}
    dropped = []
    for ob in meshes:
        role = classify(ob.name, drop)
        if role is None:
            dropped.append(ob.name)
        else:
            picked.setdefault(role, []).append(ob)
    print("保留：%s" % {r: [o.name for o in v] for r, v in picked.items()})
    print("丢弃：%s" % dropped)
    for r in ("face", "eye", "mouth"):
        if r not in picked:
            fail("没挑到「%s」件 —— 用 --drop 调整关键字，或看上面的「保留/丢弃」清单" % r)
    if "lash" not in picked:
        print("  ⚠️ 没挑到睫毛件（可选件）；若源模型确实有睫毛，检查 --drop 是否误伤")
    for ob in [o for o in meshes if o not in sum(picked.values(), [])]:
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.view_layer.update()

    # ---------- 2) 归并同角色多件（如 eyelashes + eyelashes.2） ----------
    joined = {}
    for role, objs in picked.items():
        if len(objs) > 1:
            print("  归并 %s：%s" % (role, [o.name for o in objs]))
            for o in bpy.data.objects:
                o.select_set(False)
            for o in objs:
                o.select_set(True)
            bpy.context.view_layer.objects.active = objs[0]
            bpy.ops.object.join()
        joined[role] = objs[0]
    bpy.context.view_layer.update()

    # 落地对象级变换 + 清悬空修改器/父级（源骨架不要，我们用官方骨架重新绑）
    for ob in joined.values():
        ob.parent = None
        ob.matrix_world = Matrix.Identity(4)
        for md in [m for m in ob.modifiers if m.type == 'ARMATURE']:
            ob.modifiers.remove(md)
    for ob in [o for o in bpy.data.objects if o.type != 'MESH']:
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.view_layer.update()

    if list_only:
        lo, hi = bbox(list(joined.values()))
        print("包围盒 x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
        return

    # ---------- 3) 定向 + 标定（对全部保留件统一施加） ----------
    C = center([joined["face"]])
    E = center([joined["eye"]])
    M = center([joined["mouth"]])
    print("源锚点：头壳中心 %s  眼球中心 %s  嘴中心 %s"
          % (tuple(round(v, 4) for v in C), tuple(round(v, 4) for v in E), tuple(round(v, 4) for v in M)))

    # 定向：只做【偏航 yaw】——把"头壳中心 → 眼球中心"的水平投影转到 +Y。
    #   🔴 不要用三维的 C→E 当朝向：那个向量天生带一点上仰（眼球在头壳包围盒中心之上），
    #      拿它当"前方"会多转几度、顺带把尺度也带偏（本脚本实测：算出 1.264，而正确值是 1.238）。
    #      上方向保留模型自己的 +Z（Blender 的 FBX 导入器已把 Y-up 文件转成 Z-up）。
    f = Vector((E.x - C.x, E.y - C.y, 0.0))
    if f.length < 1e-6:
        fail("眼球中心与头壳中心的水平投影重合，无法定向")
    yaw = -math.atan2(f.x, f.y)                       # 使 f 转到 +Y
    R = Matrix.Rotation(yaw, 3, 'Z')
    print("定向：源脸朝 %s（水平 %s）→ 绕 Z 转 %.1f°"
          % (v3((E - C).normalized()), v3(f.normalized()), math.degrees(yaw)))

    # 🔴 缩放用【竖直】距离（z 向），不是三维距离 —— 原版 0.0728 本身就是"眼中心 z − 嘴中心 z"。
    #    用三维距离会把"眼球比嘴靠前"那一段也算进去 → 头会偏小约 5%（本脚本实测踩到过）。
    up = Vector((0.0, 0.0, 1.0))                       # 偏航旋转不动 z，上方向恒为 +Z
    d_em = abs((E - M).dot(up))
    if d_em < 1e-6:
        fail("眼球与嘴在竖直方向重合，无法标定")
    if scale_opt == "auto":
        k = TARGET_EYE_MOUTH_DZ / d_em
    else:
        k = float(scale_opt)
    print("标定：源眼↔嘴竖直距离 %.4f（源单位）→ 缩放 %.5f（自动）；眼球送到 %s"
          % (d_em, k, v3(TARGET_EYE)))

    def xform(p):
        return (R @ (p - E)) * k + TARGET_EYE

    for role, ob in joined.items():
        for v in ob.data.vertices:
            v.co = xform(v.co)
        sk = ob.data.shape_keys
        if sk:                                          # 源模型自带形状键也一起搬（本管线后面会重建，这里只为不丢信息）
            for kb in sk.key_blocks:
                for pt in kb.data:
                    pt.co = xform(pt.co)
        ob.data.update()
    bpy.context.view_layer.update()

    # 自检：三个锚点必须落位
    E2 = center([joined["eye"]]); M2 = center([joined["mouth"]])
    lo, hi = bbox(list(joined.values()))
    print("落位：眼球 %s（目标 %s）" % (tuple(round(v, 4) for v in E2), tuple(round(v, 4) for v in TARGET_EYE)))
    print("      嘴   %s（目标 z %.4f）" % (tuple(round(v, 4) for v in M2), TARGET_EYE.z - TARGET_EYE_MOUTH_DZ))
    print("      全头包围盒 x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
    if abs(E2.z - TARGET_EYE.z) > 0.003 or abs(E2.y - TARGET_EYE.y) > 0.003:
        fail("眼球落位偏差过大：%s" % (tuple(round(v, 4) for v in E2),))
    if hi.y <= 0:
        fail("脸朝反了（+Y 最大 %.4f ≤ 0）" % hi.y)

    # ---------- 4) 改名 + 材质命名（编辑器按名字建材质资产） ----------
    for i, role in enumerate(ROLE_ORDER):
        ob = joined.get(role)
        if ob is None:
            continue
        new = "%s.%d" % (name, i)
        ob.name = new
        ob.data.name = new
        mat_name = name if role == "face" else "%s_%s" % (name, role)
        for m in list(ob.data.materials):
            pass
        if not ob.data.materials:
            ob.data.materials.append(bpy.data.materials.new(mat_name))
        else:
            ob.data.materials[0].name = mat_name
            while len(ob.data.materials) > 1:
                ob.data.materials.pop(index=len(ob.data.materials) - 1)
        for p in ob.data.polygons:
            p.material_index = 0
        print("  %-16s -> %s   材质 %s（面 %d）" % (role, new, mat_name, len(ob.data.polygons)))

    # ---------- 5) 绑官方骨架（刚性 bone 13） ----------
    if not no_skel:
        if not os.path.exists(SKEL):
            fail("找不到官方骨架：%s" % SKEL)
        bpy.ops.import_scene.fbx(filepath=SKEL)
        bpy.context.view_layer.update()
        arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        if arm is None:
            fail("官方骨架里没有 Armature")
        head_z_raw = (arm.matrix_world @ arm.data.bones[HEAD_BONE].head_local).z
        k2 = 1.569 / head_z_raw                          # 目标头骨 z（与网格中心同空间）
        bpy.data.objects.remove(arm, do_unlink=True)
        bpy.ops.import_scene.fbx(filepath=SKEL, global_scale=k2)
        bpy.context.view_layer.update()
        arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        if any(abs(s - 1.0) > 1e-4 for s in arm.scale):
            for o in bpy.data.objects:
                o.select_set(False)
            arm.select_set(True)
            bpy.context.view_layer.objects.active = arm
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            bpy.context.view_layer.update()
        hz = (arm.matrix_world @ arm.data.bones[HEAD_BONE].head_local).z
        print("骨架：头骨世界 z = %.4f（目标 1.569）  对象缩放 %s"
              % (hz, tuple(round(v, 4) for v in arm.scale)))
        if abs(hz - 1.569) > 0.01 or any(abs(s - 1.0) > 1e-4 for s in arm.scale):
            fail("骨架校准失败（头骨 z=%.4f，对象缩放 %s）" % (hz, tuple(arm.scale)))
        for role in ROLE_ORDER:
            ob = joined.get(role)
            if ob is None:
                continue
            world = ob.matrix_world.copy()
            vg = ob.vertex_groups.get(HEAD_BONE) or ob.vertex_groups.new(name=HEAD_BONE)
            vg.add([v.index for v in ob.data.vertices], 1.0, 'REPLACE')
            md = ob.modifiers.new(name="Armature", type='ARMATURE')
            md.object = arm
            md.use_vertex_groups = True
            ob.parent = arm
            ob.matrix_world = world
            if any(abs(s - 1.0) > 1e-4 for s in ob.scale):
                fail("%s 绑定后出现非单位缩放" % ob.name)
        print("  [OK] 4 件已刚性绑到 %s" % HEAD_BONE)

    # ---------- 6) 导出 ----------
    for ob in bpy.data.objects:
        ob.select_set(False)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0   # 🔴 导入器会按 USF 改写场景单位
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=False, object_types={'MESH', 'ARMATURE'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='Y', axis_up='Z',
        use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
    )
    print("EXPORTED -> " + out)
    print("下一步：transfer_channels.py 搬 59 条形变通道 → 关卡 1 fbx_probe.py → 编辑器")
    sys.stdout.flush()


main()

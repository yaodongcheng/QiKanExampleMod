# build_head.py —— 全身/任意源模型（FBX 或 .blend）→ 骑砍2 可用头部 FBX
#                  （一条龙：挑件 → 归并 → 自动定向 → 锚点标定 → 绑官方骨架 → 导出）
#
# 这是 §19 工作流的【阶段 ①装配对齐 + ②结构 + ⑤骨架 + ⑥导出】。
#   阶段 ③（59 条形变通道）随后跑 transfer_channels.py 即可。
#
# 为什么需要它：源模型通常是**全身**（含身体/衣服/头发/首饰），而引擎要的是**只有头的 metamesh**，
#   且必须落在游戏头部空间（脸朝 +Y、米级、站在脖子上）。这一步以前是人工 + 失传脚本，现在脚本化。
#
# 标定依据（与 §12.10 / §13.7④ 一致）：
#   锚点 = 眼球中心 E 与嘴中心 M
#     · 缩放 k = 原版"眼↔嘴"竖直间距 / 源模型的同距离（🔴 男女数值不同，见 GENDER_TARGETS）
#     · 平移：把 E 送到原版眼球件包围盒中心
#   定向：脸朝 = 头壳中心 C → 眼球中心 E 的方向（只取水平投影）；上 = 模型自己的 +Z
#   🔴 整段变换对【全部保留件】统一施加 —— 保持源模型自己的相对装配（蒂法那次就是在这里栽的）
#
# 🔴 件数不是恒定的 4 —— 按【原版同类头】照抄（§13.7① 的前提是"女头"）：
#     女头 head_female_a = 脸/嘴/眼/睫 4 件  →  --parts face,mouth,eye,lash
#     男头 head_male_a   = 脸/眼/嘴   3 件  →  --parts face,eye,mouth
#   （唯一在售的自定义男头 Shokuho `sho_head_male_japanese` 同样是 3 件：脸/eyes/mouth）
#
# 用法：
#   blender --background --python build_head.py -- \
#       --src <源FBX 或 .blend> --out <输出FBX> --name head_tifa_a \
#       [--gender female|male] [--parts face,mouth,eye,lash] \
#       [--pick "face=body.cut,eye=Eyeballs,mouth=mouth"]   # 显式挑件（.blend 常用；给了就不走关键字分类）\
#       [--cut-z 1.4144]                                    # 变换后按目标空间 z 裁掉下半（去胸/领口）
#       [--drop "hair,body,dress,..."] [--scale auto|<k>] [--no-skeleton] [--list-only]
#
# 输出：按 --parts 给序命名 `<name>.0/.1/.2…`，材质同名（脸=裸名，其余加 `_<role>` 后缀），
#       已刚性绑定官方骨架 `bip01_head_13`，导出规格 = USF 100 / UpAxis 2 / 节点零变换。
import bpy, bmesh, sys, os, math, mathutils
from mathutils import Vector, Matrix, kdtree

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"
HEAD_BONE = "bip01_head_13"

# 原版头标定基准（从 core_game.tpac dump 的 head_*_a.obj 量得，见 Knowledge/蒂法换头工程.md）
#   eye  = 眼球子件包围盒中心；dz = 眼球件中心 z − 嘴件中心 z
GENDER_TARGETS = {
    "female": dict(eye=Vector((0.0, 0.1291, 1.6795)), dz=1.6795 - 1.6067),   # head_female_a `.6`/`.2` → 0.0728
    "male":   dict(eye=Vector((0.0, 0.1280, 1.6839)), dz=1.6839 - 1.6044),   # head_male_a   `.1`/`.2` → 0.0795
}

DROP_DEFAULT = ("hair", "beard", "body", "dress", "cloth", "armor", "shoe", "sock",
                "panty", "underwear", "necklac", "earring", "bracelet", "anklet", "ring",
                "weapon", "sword", "shield", "eyebrow", "brow", "eyeshadow", "shadow")

ROLE_ORDER = ["face", "mouth", "eye", "lash"]

# 原版身体的【领口内沿】轮廓（角度 → (半径, z)），从 core_game dump 的 body_*_a.obj 的
# 自由边环实测。骑砍2 的身体只有个大 V 领口，"脖子+胸兜"是头网格给的；源模型的脸
# 常连着一截肩膀，比这个口沿宽 → 会从肩膀里穿出来。fit-rim 步骤按这张表把它收进去。
#   male:  正前(90°) z=1.4144 r=0.1232 ／ 肩侧(0/180°) z≈1.53~1.54 r≈0.085 ／ 背后(270°) z=1.5324 r=0.0710
RIM_TABLE = {
    "male": [(0.0, 0.0870, 1.5271), (15.0, 0.0974, 1.4986), (30.0, 0.1020, 1.4930),
             (45.0, 0.1068, 1.4883), (60.0, 0.1113, 1.4555), (75.0, 0.1180, 1.4350),
             (90.0, 0.1232, 1.4144), (105.0, 0.1113, 1.4555), (120.0, 0.1068, 1.4883),
             (135.0, 0.1020, 1.4930), (150.0, 0.0974, 1.4986), (165.0, 0.0870, 1.5271),
             (180.0, 0.0849, 1.5444), (195.0, 0.0896, 1.5410), (210.0, 0.0808, 1.5364),
             (225.0, 0.0750, 1.5340), (240.0, 0.0711, 1.5326), (255.0, 0.0710, 1.5325),
             (270.0, 0.0710, 1.5324), (285.0, 0.0711, 1.5326), (300.0, 0.0750, 1.5340),
             (315.0, 0.0808, 1.5364), (330.0, 0.0896, 1.5410), (345.0, 0.0849, 1.5444)],
}
RIM_BAND = 0.030      # 领口上方留 3cm 过渡带（带外不再收，免得把下巴/颧骨压扁）
RIM_SLOPE = -0.35     # 过渡带内半径随 z 递减的斜率（越往上越细 → 接到脖子）
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


def patch_importer():
    """原版头 FBX 的 TWT morph 缺 FullWeights，Blender 导入器会断言崩溃 → 内存级补丁（不落盘）。
    范本来自 build_neck.py / _probe_vanilla_head.py。"""
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


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
    fit_rim = "--fit-rim" in a
    weld_seam = "--weld-seam" in a
    weights_from = get(a, "--weights-from")          # 原版同类头 FBX：抄它的骨骼权重
    neck_z = float(get(a, "--neck-z", "1.60"))       # 低于这个 z 的顶点改抄原版权重
    neck_band = float(get(a, "--neck-band", "0.05"))  # 过渡带高度（避免硬边）

    gender = get(a, "--gender", "female")
    if gender not in GENDER_TARGETS:
        fail("--gender 只能是 %s，收到 %r" % (list(GENDER_TARGETS), gender))
    tgt = GENDER_TARGETS[gender]
    TARGET_EYE, TARGET_EYE_MOUTH_DZ = tgt["eye"], tgt["dz"]
    parts = [p.strip() for p in get(a, "--parts", ",".join(ROLE_ORDER)).split(",") if p.strip()]
    pick_spec = get(a, "--pick")                     # "face=body.cut,eye=Eyeballs,mouth=mouth"
    cut_spec = get(a, "--cut-z")                     # "1.4144"（只裁 face）或 "face=1.4144,mouth=1.3"

    # ---------- 1) 导入源模型（FBX 或 .blend） + 挑件 ----------
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if src.lower().endswith(".blend"):
        bpy.ops.wm.open_mainfile(filepath=src)
    else:
        bpy.ops.import_scene.fbx(filepath=src)
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("导入 %d 个网格（源类型 %s）" % (len(meshes), "blend" if src.lower().endswith(".blend") else "fbx"))

    picked = {}
    if pick_spec:
        # 显式挑件：role=名字子串[+名字子串...]，多个名字的同角色件会被归并
        wanted = {}
        for item in pick_spec.split(","):
            if "=" not in item:
                fail("--pick 格式应为 role=名字[+名字]，收到 %r" % item)
            role, val = item.split("=", 1)
            wanted[role.strip()] = [x.strip() for x in val.split("+") if x.strip()]
        for role, keys in wanted.items():
            hit = [o for o in meshes if any(k.lower() in o.name.lower() for k in keys)]
            if not hit:
                fail("--pick 的「%s」没匹配到网格（关键字 %s）；现有网格：%s"
                     % (role, keys, [o.name for o in meshes]))
            picked[role] = hit
        used = sum(picked.values(), [])
        dropped = [o.name for o in meshes if o not in used]
    else:
        dropped = []
        for ob in meshes:
            role = classify(ob.name, drop)
            if role is None:
                dropped.append(ob.name)
            else:
                picked.setdefault(role, []).append(ob)
    print("保留：%s" % {r: [o.name for o in v] for r, v in picked.items()})
    print("丢弃：%s" % dropped)
    for r in parts:
        if r not in picked:
            fail("没挑到「%s」件 —— 用 --pick/--drop 调整，或看上面的「保留/丢弃」清单" % r)
    for r in picked:
        if r not in parts:
            print("  · 「%s」不在 --parts 里，稍后丢弃" % r)
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
    E = center([joined["eye"]])
    M = center([joined["mouth"]])
    # 🔴 头壳中心 C 只用【头部】顶点算：源模型的"脸"对象通常连着一截脖子/胸/领口，
    #    把它整个算进来会把 C 拉低拉后 → C→E 的水平方向偏掉。实测萨菲罗斯源偏 7.8°
    #    （症状：标定后 x 不对称，一侧凸出 7cm）。取 E 以下 band 米以内的顶点即为头部。
    band = float(get(a, "--head-band", "0.12"))
    fac = joined["face"]
    cand = [v.co for v in fac.data.vertices if v.co.z >= E.z - band]
    if len(cand) < 10:
        print("  ⚠️ 头部带内只有 %d 个顶点，退回用整个 face 对象求 C" % len(cand))
        cand = [v.co for v in fac.data.vertices]
    lo_c = Vector((min(c.x for c in cand), min(c.y for c in cand), min(c.z for c in cand)))
    hi_c = Vector((max(c.x for c in cand), max(c.y for c in cand), max(c.z for c in cand)))
    C = (lo_c + hi_c) / 2.0
    print("头壳中心 C %s（用 %d/%d 个头部顶点，带高 %.3f）"
          % (tuple(round(v, 4) for v in C), len(cand), len(fac.data.vertices), band))
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

    # ---------- 3b) 按【目标空间】的 z 裁掉下半 ----------
    # 用途：源模型的"脸"常常连着一大截身体（胸/领口），引擎的头只需到脖子。
    # 默认裁脸壳到原版头的最低点（女 1.4066 / 男 1.4144），多留的部分藏在身体里没害处。
    if cut_spec:
        cuts = {}
        for item in cut_spec.split(","):
            if "=" in item:
                r, z = item.split("=", 1)
                cuts[r.strip()] = float(z)
            else:
                cuts["face"] = float(item)
        for role, z in cuts.items():
            ob = joined.get(role)
            if ob is None:
                fail("--cut-z 指定的「%s」不在已挑到的件里（现有 %s）" % (role, list(joined)))
            bm = bmesh.new(); bm.from_mesh(ob.data)
            kill = [v for v in bm.verts if v.co.z < z]
            bmesh.ops.delete(bm, geom=kill, context='VERTS')
            loose = [e for e in bm.edges if not e.link_faces]
            if loose:
                bmesh.ops.delete(bm, geom=loose, context='EDGES')
            lone = [v for v in bm.verts if not v.link_faces]
            if lone:
                bmesh.ops.delete(bm, geom=lone, context='VERTS')
            bm.to_mesh(ob.data); bm.free(); ob.data.update()
            print("  裁 %s：z < %.4f 删 %d 顶点 → 剩 %d 顶点 %d 面"
                  % (role, z, len(kill), len(ob.data.vertices), len(ob.data.polygons)))
        bpy.context.view_layer.update()

    # ---------- 3c) 把源模型带下来的肩膀收进原版身体的领口 ----------
    # 源模型的"脸"对象常连着一截肩膀/斜方肌，比原版身体那个 V 领口宽 → 实机里会从肩膀穿出来。
    # 按 RIM_TABLE 的实测口沿逐顶点收半径：口沿以下收到口沿半径（藏进身体里），
    # 口沿上方 3cm 过渡带内线性收细（接到脖子）；再往上不动（免得压扁下巴/颧骨）。
    if fit_rim:
        tab = RIM_TABLE.get(gender)
        if not tab:
            fail("RIM_TABLE 里没有 %s 的领口轮廓" % gender)

        def rim_at(adeg):
            adeg = adeg % 360.0
            for k in range(len(tab)):
                a0, r0, z0 = tab[k]
                a1, r1, z1 = tab[(k + 1) % len(tab)]
                if a1 <= a0:
                    a1 += 360.0
                if a0 <= adeg <= a1:
                    t = (adeg - a0) / (a1 - a0)
                    return r0 + (r1 - r0) * t, z0 + (z1 - z0) * t
            return tab[0][1], tab[0][2]

        ob = joined["face"]
        moved, worst = 0, 0.0
        for v in ob.data.vertices:
            r = math.hypot(v.co.x, v.co.y)
            if r < 1e-6:
                continue
            rr, rz = rim_at(math.degrees(math.atan2(v.co.y, v.co.x)))
            if v.co.z <= rz:
                rmax = rr
            elif v.co.z < rz + RIM_BAND:
                rmax = rr + (v.co.z - rz) * RIM_SLOPE
            else:
                continue
            if r > rmax:
                s = rmax / r
                v.co.x *= s
                v.co.y *= s
                moved += 1
                worst = max(worst, r - rmax)
        ob.data.update()
        bpy.context.view_layer.update()
        print("  收领口：收进 %d 个顶点（最大收进 %.1fmm）" % (moved, worst * 1000))

    # ---------- 3d) UV 折回 [0,1) ----------
    # 源模型的嘴件用的是负 V（v[-0.989,-0.007]，靠纹理 wrap 采样）。引擎与贴图工具对负 UV
    # 的处理不一致，统一按整周期折回 [0,1)——wrap 语义等价，采样结果不变。
    for role, ob in joined.items():
        uvl = ob.data.uv_layers.active
        if uvl is None:
            continue
        shifted = 0
        for d in uvl.data:
            u0, v0 = d.uv[0], d.uv[1]
            u = u0 - math.floor(u0)
            v = v0 - math.floor(v0)
            if abs(u - u0) > 1e-9 or abs(v - v0) > 1e-9:
                d.uv[0], d.uv[1] = u, v
                shifted += 1
        if shifted:
            uvl.data.update()
            print("  UV 折回 [0,1)：%s 改了 %d 个 loop" % (role, shifted))

    # ---------- 3e) 合缝：把源模型"前后两块不相连的壳"缝起来 ----------
    # 症状（实机 2026-09-14）：耳朵后面一条明显裂缝。
    # 根因：萨菲罗斯源模型的"脸"是两块【不相连】的壳（前面脸壳 + 后面后脑/脖子），接口处
    #   实测裂开 0.66~10.15mm（中位 4.66mm）。源模型里被长发盖住，骑砍的短发盖不住 → 露出来。
    # 做法：找出彼此最近的那一对自由边环 → 按顺序走环 → 旋转对齐 → 每对顶点都移到【中点】
    #   → remove_doubles 焊成一个点。这样两半共用同一圈顶点 = 真正连续，不是拿一条带子糊上。
    if weld_seam:
        ob = joined["face"]
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bm.verts.ensure_lookup_table()
        bnd = [e for e in bm.edges if len(e.link_faces) == 1]

        # 1) 自由边环分组
        adj = {}
        for e in bnd:
            adj.setdefault(e.verts[0], []).append(e)
            adj.setdefault(e.verts[1], []).append(e)
        seen, loops = set(), []
        for e in bnd:
            if e.index in seen:
                continue
            stack, lp = [e], []
            while stack:
                x = stack.pop()
                if x.index in seen:
                    continue
                seen.add(x.index); lp.append(x)
                for v in x.verts:
                    for y in adj.get(v, []):
                        if y.index not in seen:
                            stack.append(y)
            loops.append(lp)
        loops = [lp for lp in loops if len(lp) >= 12]
        if len(loops) < 2:
            print("  [warn] 合缝：自由边环不足 2 个（%d），跳过" % len(loops))
        else:
            def loop_verts(lp):
                s = set()
                for e in lp:
                    s.update(e.verts)
                return list(s)

            lv = [loop_verts(lp) for lp in loops]
            # 2) 找彼此最近的一对环（= 那道缝）
            best = None
            for i in range(len(lv)):
                for j in range(i + 1, len(lv)):
                    if len(lv[i]) != len(lv[j]):
                        continue
                    kd = kdtree.KDTree(len(lv[j]))
                    for k, v in enumerate(lv[j]):
                        kd.insert(v.co, k)
                    kd.balance()
                    d = min(kd.find(v.co)[2] for v in lv[i])
                    if best is None or d < best[0]:
                        best = (d, i, j)
            if best is None:
                print("  [warn] 合缝：没找到点数相同的候选环对，跳过")
            else:
                d0, i0, j0 = best
                va, vb = lv[i0], lv[j0]
                print("  合缝：候选环对 %d/%d，各 %d 点，最近距离 %.2fmm" % (i0, j0, len(va), d0 * 1000))

                # 3) 按网格邻接走成有序环
                def walk(lp):
                    nb = {}
                    for e in lp:
                        nb.setdefault(e.verts[0], []).append(e.verts[1])
                        nb.setdefault(e.verts[1], []).append(e.verts[0])
                    if any(len(v) != 2 for v in nb.values()):
                        return None
                    start = lp[0].verts[0]
                    order, prev, cur = [start], None, start
                    while True:
                        nxt = [v for v in nb[cur] if v is not prev][0]
                        if nxt is start:
                            break
                        order.append(nxt)
                        prev, cur = cur, nxt
                    return order if len(order) == len(nb) else None

                oa, ob_ = walk(loops[i0]), walk(loops[j0])
                if oa is None or ob_ is None:
                    print("  [warn] 合缝：环不是简单闭合圈（有分叉），跳过")
                else:
                    # 4) 旋转 + 反向对齐，取总距离最小
                    n = len(oa)
                    bestal = None
                    for rev in (False, True):
                        bl = list(reversed(ob_)) if rev else ob_
                        for off in range(n):
                            tot = sum((oa[k].co - bl[(k + off) % n].co).length for k in range(n))
                            if bestal is None or tot < bestal[0]:
                                bestal = (tot, rev, off)
                    tot, rev, off = bestal
                    bl = list(reversed(ob_)) if rev else ob_
                    pairs = [(oa[k], bl[(k + off) % n]) for k in range(n)]
                    mx = max((a.co - b.co).length for a, b in pairs)
                    avg = tot / n
                    print("  合缝：对齐后 平均间距 %.2fmm 最大 %.2fmm（%s）"
                          % (avg * 1000, mx * 1000, "反向" if rev else "同向"))
                    if mx > 0.030:
                        print("  [warn] 合缝：对齐后最大间距 >30mm，疑非对应环，跳过（不动几何）")
                    else:
                        for a, b in pairs:                 # 两端都移到中点 → 再焊
                            mid = (a.co + b.co) / 2.0
                            a.co = mid
                            b.co = mid
                        verts = [v for pr in pairs for v in pr]
                        res = bmesh.ops.remove_doubles(bm, verts=verts, dist=1e-4)
                        bm.to_mesh(ob.data); bm.free(); ob.data.update()
                        bpy.context.view_layer.update()
                        print("  合缝：焊掉 %d 个顶点 → 脸壳剩 %d 顶点 %d 面"
                              % (n, len(ob.data.vertices), len(ob.data.polygons)))
                        bm = None
        if bm is not None:
            bm.free()
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
    for role in [r for r in joined if r not in parts]:   # 不在 --parts 里的件丢掉（如男头不要睫毛）
        bpy.data.objects.remove(joined.pop(role), do_unlink=True)
    bpy.context.view_layer.update()
    for i, role in enumerate(parts):
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

    # ---------- 5) 绑官方骨架（脸壳刚性 bone 13；脖子/领口段从原版头抄权重） ----------
    if not no_skel:
        if not os.path.exists(SKEL):
            fail("找不到官方骨架：%s" % SKEL)

        # 5a) 先把"权重来源"（原版同类头）读进来 —— 原版头的顶点组名是 head/neck/spine1/… ，
        #     要映射到官方骨架的 bip01_* 名字；用子串匹配，两边命名怎么变都能对上。
        vkd = vwts = None
        if weights_from:
            if not os.path.exists(weights_from):
                fail("找不到 --weights-from 的 FBX：%s" % weights_from)
            before_o = set(bpy.data.objects)
            patch_importer()
            bpy.ops.import_scene.fbx(filepath=weights_from)
            bpy.context.view_layer.update()
            cand = [o for o in bpy.data.objects if o not in before_o and o.type == 'MESH']
            if not cand:
                fail("--weights-from 里没有网格：%s" % weights_from)
            vhead = max(cand, key=lambda o: len(o.data.vertices))
            vnames = [g.name for g in vhead.vertex_groups]
            vpos, vwts = [], []
            for v in vhead.data.vertices:
                vpos.append(vhead.matrix_world @ v.co)
                d = {}
                for g in v.groups:
                    if g.weight > 1e-4:
                        nm = vnames[g.group]
                        d[nm] = d.get(nm, 0.0) + g.weight
                vwts.append(d)
            vkd = kdtree.KDTree(len(vpos))
            for i, p in enumerate(vpos):
                vkd.insert(p, i)
            vkd.balance()
            print("  权重来源 %s：%d 顶点，组 %s"
                  % (os.path.basename(weights_from), len(vpos), vnames))
            for o in [o for o in bpy.data.objects if o not in before_o]:
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.context.view_layer.update()

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

        # 5b) 原版组名 → 本骨架骨骼名（子串匹配；对不上的组直接丢弃，后面归一化补回来）
        bone_by_key = {}
        if vkd is not None:
            arm_names = [b.name for b in arm.data.bones]
            for vn in set(k for d in vwts for k in d):
                hit = next((n for n in arm_names if vn.lower() in n.lower()), None)
                if hit:
                    bone_by_key[vn] = hit
            print("  权重组映射：%s" % bone_by_key)
            if not bone_by_key:
                fail("原版头的顶点组名一个都没映射到本骨架，检查 --weights-from")

        for role in parts:
            ob = joined.get(role)
            if ob is None:
                continue
            world = ob.matrix_world.copy()
            vg = ob.vertex_groups.get(HEAD_BONE) or ob.vertex_groups.new(name=HEAD_BONE)
            vg.add([v.index for v in ob.data.vertices], 1.0, 'REPLACE')

            # 5c) 脸壳下半（脖子/领口）改抄原版权重：整体刚性绑 13 = 脖子不跟脊柱/锁骨动，
            #     身体呼吸时胸廓扩张而领口不动 → 皮从身体里穿出来（实机 2026-09-14 实测）。
            if vkd is not None and role == "face":
                fixed = 0
                grp = {}
                for vn, bn in bone_by_key.items():
                    grp[bn] = ob.vertex_groups.get(bn) or ob.vertex_groups.new(name=bn)
                for v in ob.data.vertices:
                    t = (neck_z + neck_band - v.co.z) / neck_band      # z<=neck_z → 1；z>=neck_z+band → 0
                    t = max(0.0, min(1.0, t))
                    if t <= 1e-4:
                        continue
                    hits = vkd.find_n(v.co, 3)
                    ws = [1.0 / ((d + 1e-4) ** 2) for (_, _, d) in hits]
                    s = sum(ws)
                    acc = {}
                    for (_, idx, _), w in zip(hits, ws):
                        for vn, val in vwts[idx].items():
                            bn = bone_by_key.get(vn)
                            if bn:
                                acc[bn] = acc.get(bn, 0.0) + val * (w / s) * t
                    acc[HEAD_BONE] = acc.get(HEAD_BONE, 0.0) + (1.0 - t)   # 头骨那份
                    tot = sum(acc.values())
                    if tot <= 1e-6:
                        continue
                    for g in ob.vertex_groups:                             # 先摘干净再写
                        g.remove([v.index])
                    for bn, val in acc.items():
                        if val / tot > 1e-4:
                            ob.vertex_groups[bn].add([v.index], val / tot, 'REPLACE')
                    fixed += 1
                print("  脖子/领口权重：抄了 %d 个顶点（z < %.3f，过渡带 %.2f）" % (fixed, neck_z, neck_band))

            md = ob.modifiers.new(name="Armature", type='ARMATURE')
            md.object = arm
            md.use_vertex_groups = True
            ob.parent = arm
            ob.matrix_world = world
            if any(abs(s - 1.0) > 1e-4 for s in ob.scale):
                fail("%s 绑定后出现非单位缩放" % ob.name)
        print("  [OK] %d 件（%s）已绑到 %s" % (len(parts), ",".join(parts), HEAD_BONE))

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

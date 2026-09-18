# build_neck.py —— 给自建头加「脖子 + 领口」接段（v11）
#
# 目标：让蒂法的头衔接到骑砍2 女子身体（body_female_a）。
#
# 三块几何：
#   ① 脖子   = 蒂法源模型 body 的脖子（同一模型自带，形状/肤色天然接她的头）
#   ② 领口    = 从脖子底环扇形铺到身体领口轮廓（骑砍2 女子身体颈口是个大 V 形开口，
#              原版靠头网格自带的"胸兜"盖住；我们不抄原版几何，改为按实测轮廓现铺）
#   ③ 权重   = 从原版 head_female_a 的最近点抄（k=3 反距离加权）→ 头/颈/脊/锁骨 平滑过渡
#
# 用法：
#   blender --background --python build_neck.py -- --stage diag     # 只打印诊断
#   blender --background --python build_neck.py -- --stage build    # 生成 v11
#
import bpy, bmesh, sys, os, math, inspect, json
from mathutils import Vector, Matrix, kdtree
from mathutils.bvhtree import BVHTree

# ---------------- 路径 ----------------
BK = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913"
HEAD_FBX = BK + "/head_tifa_a_v10.fbx"                      # 输入：当前能用的头（4 件 + 骨架）
SRC_FBX = r"F:/下载/Tifa lockhart In Drees - FBX/Tifa.fbx"   # 蒂法源模型（取脖子）
VAN_FBX = (r"H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/"
           r"LivingWorldNpcs/Debug/offline/自定义头/core_game/fbx/head/head_female_a.fbx")   # 原版权重来源
BODY_OBJ = (r"H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/"
            r"LivingWorldNpcs/Debug/offline/自定义头/core_game/out/body/body_female_a.obj")   # 骑砍2 女子身体
OUT_FBX = (r"H:/SteamLibrary/steamapps/common/MB2_Version/MB2_1.2.12/Mount & Blade II Bannerlord/"
           r"Modules/TifaHead2/AssetSources/head_tifa_a_v11.fbx")
OUT_BAK = BK + "/head_tifa_a_v11.fbx"
RENDER_DIR = (r"H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/"
              r"LivingWorldNpcs/Debug/offline/自定义头/neck_probe")

# ---------------- 参数 ----------------
Z_CUT = 1.522        # 脖子下沿（提上去试过 1.536：形状略好但会漏 6 处极小的缝，退回）
N_ANG = 48           # 角向分辨率（领口与脖子底环都按这个重采样）
COLLAR_RINGS = 12    # 领口中间环数（越多越平顺）
BURY = 0.007         # 领口外沿往身体里埋 7mm（保证两条曲面确实相交、不留悬空边）
SMOOTH_K = 3         # 抄权重时的近邻数
SMOOTH_PASSES = 6    # 下沿环 z 抹平次数（剪切会留下 16mm 锯齿，不抹平领口一圈是波浪的）
BULGE = 0.012        # 领口曲面外凸量：直线放样是凹的，会折出棱；真人的斜方肌是鼓的

# 蒂法源 → 我们空间（§17.7 实测标定）
S, BY, BZ = 1.238, 0.01685, -0.2678
def src2our(p):
    return Vector((S * p.x, -S * p.y + BY, S * p.z + BZ))

# 组名映射：原版头的顶点组名 → 官方 human_skeleton 的骨骼名
BONE_ALIAS = {
    "head": "bip01_head_13",
    "neck": "bip01_neck_12",
    "spine1": "bip01_spine1_10",
    "spine2": "bip01_spine2_11",
    "l_clavicle": None, "r_clavicle": None,
    "l_upperarm_twist": None, "r_upperarm_twist": None,
}


def fail(msg):
    print("FATAL: " + msg)
    sys.stdout.flush()
    sys.exit(1)


def patch_importer():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


def imp_fbx(p):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p)
    bpy.context.view_layer.update()
    objs = [o for o in bpy.data.objects if o not in before and o.type in ('MESH', 'ARMATURE')]
    for o in objs:
        if o.type != 'MESH':
            continue
        if o.animation_data:
            o.animation_data_clear()
        sk = o.data.shape_keys
        if sk and sk.animation_data:
            sk.animation_data_clear()
    bpy.context.view_layer.update()
    return objs


def obj_bbox(ob):
    ps = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return ([min(p[i] for p in ps) for i in range(3)], [max(p[i] for p in ps) for i in range(3)])


def bbox_str(ob):
    lo, hi = obj_bbox(ob)
    return "x[%8.4f,%8.4f] y[%8.4f,%8.4f] z[%8.4f,%8.4f]" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2])


def boundary_loops(ob):
    """返回网格的所有自由边环（每个环是一串按顺序相连的顶点索引）"""
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    adj = {}
    for e in edges:
        a, b = e.verts[0].index, e.verts[1].index
        adj.setdefault(a, []).append(b)
        adj.setdefault(b, []).append(a)
    seen = set()
    loops = []
    for start in adj:
        if start in seen:
            continue
        loop = [start]; seen.add(start)
        cur, prev = start, None
        while True:
            nxts = [n for n in adj[cur] if n != prev]
            nxt = None
            for n in nxts:
                if n not in seen:
                    nxt = n; break
            if nxt is None:
                break
            seen.add(nxt); loop.append(nxt); prev, cur = cur, nxt
        if len(loop) >= 3:
            loops.append(loop)
    bm.free()
    return loops


def sample_polyline(pts, s):
    """按累计弧长在闭合折线上取参数 s∈[0,1) 处的点"""
    n = len(pts)
    seg = []
    tot = 0.0
    for i in range(n):
        d = (pts[(i + 1) % n] - pts[i]).length
        seg.append(d); tot += d
    target = s * tot
    acc = 0.0
    for i in range(n):
        if acc + seg[i] >= target:
            t = (target - acc) / seg[i] if seg[i] > 1e-9 else 0.0
            return pts[i].lerp(pts[(i + 1) % n], t)
        acc += seg[i]
    return pts[0]


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


# =====================================================================================
# 领口轮廓：z_rim(θ) —— 从外侧朝中轴打射线，看第一击是正面还是背面
#   正面 = 身体表面在；背面 = 打穿到了对侧内壁（= 这里是个洞）
# =====================================================================================
def measure_rim(body_bvh, n_ang, z_lo=1.36, z_hi=1.56, step=0.002, R=0.8, axis_y=0.0):
    """返回 [(θ, z_rim, 命中点 Vector), ...]"""
    out = []
    for k in range(n_ang):
        th = 2 * math.pi * k / n_ang
        d = Vector((math.sin(th), math.cos(th), 0.0))          # 由内向外
        origin = Vector((0.0, axis_y, 0.0)) + d * R
        ray = -d
        z = z_lo
        last_solid = None
        while z <= z_hi:
            origin.z = z
            hit = body_bvh.ray_cast(origin, ray, R * 2)
            solid = False
            if hit[0] is not None:
                n = hit[1]
                solid = n.dot(ray) < 0        # 正面朝向射线源 = 外侧表面
            if solid:
                last_solid = (z, hit[0].copy())
            elif last_solid is not None and z > last_solid[0] + 0.01:
                break
            z += step
        if last_solid is None:
            out.append((th, None, None))
        else:
            out.append((th, last_solid[0], last_solid[1]))
    return out


def stage_diag():
    patch_importer()
    bpy.ops.wm.read_factory_settings(use_empty=True)

    print("=" * 90); print("① 我们的 v10 头 —— 对象 / 骨架骨名"); print("=" * 90)
    objs = imp_fbx(HEAD_FBX)
    for o in objs:
        if o.type == 'ARMATURE':
            names = [b.name for b in o.data.bones]
            print("骨架 %s（%d 骨）" % (o.name, len(names)))
            for n in names:
                low = n.lower()
                if any(k in low for k in ("head", "neck", "spine", "clavicle", "pelvis")):
                    print("   ", n)
            BONE_ALIAS["l_clavicle"] = next((n for n in names if "l_clavicle" in n.lower()), None)
            BONE_ALIAS["r_clavicle"] = next((n for n in names if "r_clavicle" in n.lower()), None)
            BONE_ALIAS["l_upperarm_twist"] = next((n for n in names if "upperarm" in n.lower() and n.lower().startswith("bip01_l")), None)
            BONE_ALIAS["r_upperarm_twist"] = next((n for n in names if "upperarm" in n.lower() and n.lower().startswith("bip01_r")), None)
        else:
            print("  %-18s %s" % (o.name, bbox_str(o)))
    print("骨骼名映射：%s" % json.dumps(BONE_ALIAS, ensure_ascii=False))

    print()
    print("=" * 90); print("② 蒂法源 body 脖子 —— 裁到 z>%.3f 后的底环" % Z_CUT); print("=" * 90)
    src = imp_fbx(SRC_FBX)
    tb = next(o for o in src if o.name == 'body')
    me = tb.data
    mw = tb.matrix_world
    for v in me.vertices:
        v.co = src2our(mw @ v.co)
    tb.matrix_world = Matrix.Identity(4)
    me.update()
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    dead = [f for f in bm.faces if any(v.co.z < Z_CUT for v in f.verts)]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me); bm.free(); me.update()
    print("脖子网格 %d 顶点 %d 面" % (len(me.vertices), len(me.polygons)))
    print("  bbox %s" % bbox_str(tb))
    loops = boundary_loops(tb)
    loops.sort(key=len, reverse=True)
    for i, lp in enumerate(loops[:3]):
        ps = [tb.matrix_world @ me.vertices[j].co for j in lp]
        zs = sorted(p.z for p in ps)
        print("  自由边环%d: %d 点 z[%.4f,%.4f] x[%+.4f,%+.4f]" % (
            i, len(lp), zs[0], zs[-1], min(p.x for p in ps), max(p.x for p in ps)))
    for o in src:
        if o is not tb:
            bpy.data.objects.remove(o, do_unlink=True)

    print()
    print("=" * 90); print("③ 骑砍2 女子身体领口轮廓 z_rim(θ)"); print("=" * 90)
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=BODY_OBJ, forward_axis='Y', up_axis='Z')
    body = [o for o in bpy.data.objects if o not in before][0]
    bm = bmesh.new(); bm.from_mesh(body.data); bm.transform(body.matrix_world)
    bvh = BVHTree.FromBMesh(bm)
    rim = measure_rim(bvh, N_ANG)
    print("  θ(度)  z_rim     命中点(x,y,z)")
    ok = 0
    for th, z, p in rim:
        deg = math.degrees(th) - 180.0     # 0 = 正前(+Y)
        if z is None:
            print("  %+7.1f   ----" % deg)
        else:
            ok += 1
            print("  %+7.1f  %.4f   (%+.4f,%+.4f,%+.4f)" % (deg, z, p.x, p.y, p.z))
    print("  成功 %d/%d" % (ok, N_ANG))
    bm.free()
    print("\nDIAG DONE")


if "--stage" in sys.argv:
    _st = sys.argv[sys.argv.index("--stage") + 1]
else:
    _st = "diag"
if _st == "diag":
    stage_diag()


# =====================================================================================
# 脸贴图的「可用皮肤区」扫描（决定脖子/领口 UV 铺到哪儿）
# =====================================================================================
def stage_uvscan():
    img = bpy.data.images.load(r"F:/下载/Tifa lockhart In Drees - FBX/Tifa_Head_D.jpg")
    W, H = img.size
    px = list(img.pixels)
    print("贴图 %dx%d" % (W, H))
    G = 32
    print("    " + "".join("%4d" % int(i * 100 / G) for i in range(G)))
    for gy in range(G):
        row = ""
        for gx in range(G):
            x0 = int(gx * W / G); x1 = int((gx + 1) * W / G)
            y0 = int(gy * H / G); y1 = int((gy + 1) * H / G)
            r = g = b = n = 0
            for y in range(y0, y1, 4):
                for x in range(x0, x1, 4):
                    i = (y * W + x) * 4          # Blender 图像坐标：y 自下而上
                    r += px[i]; g += px[i + 1]; b += px[i + 2]; n += 1
            r /= n; g /= n; b /= n
            lum = (r + g + b) / 3
            if lum < 0.12:
                c = "."                      # 近黑（未用区）
            elif lum < 0.35:
                c = "h"                      # 深色（头发）
            elif r > g > b and (r - b) > 0.06:
                c = "S"                      # 肤色
            else:
                c = "?"
            row += "%4s" % c
        print("%3d %s" % (int(gy * 100 / G), row))
    print("（行号自上而下 0→100；列号自左而右 0→100）")


if _st == "uvscan":
    stage_uvscan()


# =====================================================================================
# 主构建：脖子 + 领口 + 权重 + UV → v11
# =====================================================================================
import re

Y_AXIS = 0.02                  # 角度参数化的中轴 y（蒂法脖子中心 +0.042 与身体中心 0 之间）
UV_U0, UV_U1 = 0.22, 0.78      # 脖子柱面展开到贴图的 u 区间（u=0.5 对应正前方）
UV_Z0, UV_Z1 = 1.40, 1.64      # v 对应的 z 区间
UV_V0, UV_V1 = 0.02, 0.255     # 🔴 上限必须 < 脸壳 UV 的下界 0.2666，否则脖子顶部会蹭到脸的下巴
                               #    （实测：脸只用到贴图上部 73%，v<0.2666 才是干净的颈/胸皮肤）


def nearest_weights(pos, kd, wts, k=SMOOTH_K):
    """从原版头抄权重：k 近邻反距离加权"""
    hits = kd.find_n(pos, k)
    ws = [1.0 / (d + 1e-5) for (_, _, d) in hits]
    tot = sum(ws)
    out = {}
    for (_, idx, _), w in zip(hits, ws):
        for name, val in wts[idx].items():
            out[name] = out.get(name, 0.0) + val * w / tot
    s = sum(out.values())
    return {n: v / s for n, v in out.items()} if s > 1e-9 else {}


def rebuild_shapes(ob, delta_map, n_old, ch=59):
    """重建形状键：Basis → KeyTime_0(占位) → KeyTime_1..59；新顶点无位移"""
    base = [v.co.copy() for v in ob.data.vertices]
    n_all = len(base)
    ob.shape_key_clear()
    kb = ob.shape_key_add(name="Basis", from_mix=False)
    flat = []
    for c in base:
        flat += [c.x, c.y, c.z]
    kb.data.foreach_set("co", flat)

    def add(name, deltas):
        k = ob.shape_key_add(name=name, from_mix=False)
        f = []
        for i, c in enumerate(base):
            d = deltas[i] if i < len(deltas) else Vector((0, 0, 0))
            f += [c.x + d.x, c.y + d.y, c.z + d.z]
        k.data.foreach_set("co", f)

    add("KeyTime_0", [Vector((0.0, 0.0001, 0.0))] * n_all)
    for n in range(1, ch + 1):
        add("KeyTime_%d" % n, delta_map[n])


def stage_build():
    patch_importer()
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # ---------- 1) 我们的头 ----------
    objs = imp_fbx(HEAD_FBX)
    arm = next(o for o in objs if o.type == 'ARMATURE')
    meshes = [o for o in objs if o.type == 'MESH']
    shell = next(o for o in meshes if o.name.endswith(".0"))
    bone_names = [b.name for b in arm.data.bones]
    BONE_ALIAS["l_clavicle"] = next((n for n in bone_names if "l_clavicle" in n.lower()), None)
    BONE_ALIAS["r_clavicle"] = next((n for n in bone_names if "r_clavicle" in n.lower()), None)
    BONE_ALIAS["l_upperarm_twist"] = next((n for n in bone_names if "l_upperarm" in n.lower()), None)
    BONE_ALIAS["r_upperarm_twist"] = next((n for n in bone_names if "r_upperarm" in n.lower()), None)
    print("骨骼名映射：%s" % json.dumps(BONE_ALIAS, ensure_ascii=False))
    if any(v is None for v in BONE_ALIAS.values()):
        fail("骨骼名映射不全")

    sk = shell.data.shape_keys
    if sk is None:
        fail("脸壳没有形状键")
    chans = {}
    for kb in sk.key_blocks:
        m = re.match(r'^KeyTime_(\d+)$', kb.name)
        if m:
            chans[int(m.group(1))] = kb
    # KeyTime_0 = v9 加的占位键（顶在帧 0 用），真正的 59 条脸形通道是 1..59
    chans = {n: kb for n, kb in chans.items() if 1 <= n <= 59}
    if len(chans) != 59:
        fail("脸壳通道数 %d ≠ 59（缺 %s）" % (len(chans), [n for n in range(1, 60) if n not in chans]))
    base0 = [v.co.copy() for v in shell.data.vertices]
    delta = {n: [chans[n].data[i].co - base0[i] for i in range(len(base0))] for n in chans}
    print("脸壳 %d 顶点 / %d 条形变通道" % (len(base0), len(delta)))

    # ---------- 2) 原版头：权重来源 ----------
    vobjs = imp_fbx(VAN_FBX)
    vhead = max([o for o in vobjs if o.type == 'MESH'], key=lambda o: len(o.data.vertices))
    vnames = [g.name for g in vhead.vertex_groups]
    vpos = [vhead.matrix_world @ v.co for v in vhead.data.vertices]
    vwts = []
    for v in vhead.data.vertices:
        d = {}
        for g in v.groups:
            if g.weight > 1e-4:
                d[vnames[g.group]] = d.get(vnames[g.group], 0.0) + g.weight
        vwts.append(d)
    vkd = kdtree.KDTree(len(vpos))
    for i, p in enumerate(vpos):
        vkd.insert(p, i)
    vkd.balance()
    print("权重来源：原版 head_female_a %d 顶点，组 %s" % (len(vpos), vnames))
    for o in vobjs:
        bpy.data.objects.remove(o, do_unlink=True)

    # ---------- 3) 骑砍2 女子身体：领口轮廓 ----------
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=BODY_OBJ, forward_axis='Y', up_axis='Z')
    body = [o for o in bpy.data.objects if o not in before][0]
    bm = bmesh.new(); bm.from_mesh(body.data); bm.transform(body.matrix_world)
    body_bvh = BVHTree.FromBMesh(bm)
    bm.free()
    rim_fine = measure_rim(body_bvh, 192, axis_y=Y_AXIS)
    okr = [r for r in rim_fine if r[1] is not None]
    print("领口轮廓：%d/192 成功，z_rim ∈ [%.4f, %.4f]" % (len(okr), min(r[1] for r in okr), max(r[1] for r in okr)))
    if len(okr) < 180:
        fail("领口轮廓测量失败过半")

    def rim_at(th):
        t = (th % (2 * math.pi)) / (2 * math.pi) * len(rim_fine)
        i0 = int(t) % len(rim_fine); f = t - int(t)
        p0 = rim_fine[i0][2]; p1 = rim_fine[(i0 + 1) % len(rim_fine)][2]
        if p0 is None:
            return p1
        if p1 is None:
            return p0
        return p0.lerp(p1, f)

    # ---------- 4) 蒂法源脖子 ----------
    src = imp_fbx(SRC_FBX)
    tb = next(o for o in src if o.name == 'body')
    me = tb.data
    mw = tb.matrix_world
    print("源 body 世界矩阵对角线 %s（父级=%s）" % (
        [round(mw[i][i], 6) for i in range(4)], tb.parent.name if tb.parent else None))
    for v in me.vertices:
        v.co = src2our(mw @ v.co)
    # 🔴 源 FBX 的对象带 0.01 单位缩放（Blender 按 cm 解释），必须彻底摘干净：
    #    只赋 matrix_world 不够（有父级时会被父级矩阵覆盖）→ 断父 + 设 matrix_basis
    tb.parent = None
    tb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    ident = all(abs(tb.matrix_world[i][j] - (1.0 if i == j else 0.0)) < 1e-6
                for i in range(4) for j in range(4))
    if not ident:
        fail("源 body 的世界矩阵仍非单位阵：%s" % [round(tb.matrix_world[i][i], 6) for i in range(4)])
    zs = [v.co.z for v in me.vertices]
    print("源 body 局部坐标 z[%.4f,%.4f]（应 ≈1.4~1.7；若是 140~170 说明还带着 100 倍）" % (min(zs), max(zs)))
    if max(zs) > 10.0:
        fail("源 body 的局部坐标量级不对")
    me.update()
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    dead = [f for f in bm.faces if any(v.co.z < Z_CUT for v in f.verts)]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me); bm.free(); me.update()
    for o in src:
        if o is not tb:
            bpy.data.objects.remove(o, do_unlink=True)
    print("脖子网格 %d 顶点 %d 面  %s" % (len(me.vertices), len(me.polygons), bbox_str(tb)))

    loops = [lp for lp in boundary_loops(tb) if len(lp) >= 8]
    loops.sort(key=lambda lp: min(me.vertices[i].co.z for i in lp))
    neck_loop = loops[0]
    zlo = min(me.vertices[i].co.z for i in neck_loop)
    zhi = max(me.vertices[i].co.z for i in neck_loop)
    print("脖子下沿环 %d 点，z[%.4f,%.4f]（剪切锯齿 %.1fmm）" % (len(neck_loop), zlo, zhi, (zhi - zlo) * 1000))
    for _ in range(SMOOTH_PASSES):                       # 抹平锯齿
        zs = [me.vertices[i].co.z for i in neck_loop]
        for k, i in enumerate(neck_loop):
            me.vertices[i].co.z = (zs[(k - 1) % len(zs)] + 2 * zs[k] + zs[(k + 1) % len(zs)]) / 4.0
    me.update()

    # 🔴🔴 源→我们的变换含 y 镜像（our_y = −S·src_y + …，行列式为负）→ 从源 body 搬过来的
    #      脖子【所有面的朝向都反了】。不翻 = 实机背面剔除后脖子是个洞（实测：那道裂缝
    #      在脖子上而不是领口上，射线体检抓到的"真洞"全落在脖子面片的高度带）。
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    bm.to_mesh(me); bm.free(); me.update()
    print("脖子面朝向：已整份翻转（抵消镜像变换）")
    # 🔴 下沿环必须【保持原始先后顺序】，绝不能按角度重排！
    #    重排会让"相邻"变成"角度相邻"而不是"网格上真的挨着"——领口的面就接不到相邻顶点上，
    #    脖子上会留下一圈断边（实测 19 条自由边），编辑器/实机里就是一道缝。
    #    角度只用来查领口轮廓（rim_at），不参与排序。
    ring = [(math.atan2(me.vertices[i].co.x, me.vertices[i].co.y - Y_AXIS) % (2 * math.pi), i)
            for i in neck_loop]

    # ---------- 5) 领口：直接长在脖子网格上（共用下沿顶点，不留重复顶点/接缝） ----------
    n_ring = len(ring)
    rim_pts = []
    for (th, vi) in ring:
        rp = rim_at(th)
        if rp is None:
            fail("角度 %.1f° 处领口轮廓缺失" % math.degrees(th))
        hit = body_bvh.find_nearest(rp)
        nrm = hit[1] if (hit and hit[1]) else Vector((0, 0, 1))
        rim_pts.append(rp - nrm * BURY)                  # 外沿埋进身体 4mm

    n_inter = COLLAR_RINGS + 2
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    neck_bvh = BVHTree.FromBMesh(bm)          # 量脖子壁方向用

    def radius_at(bvh, th, z, rmax=0.5):
        """在 (θ, z) 处从外朝中轴打射线，取正面命中的半径（= 该处表面离中轴多远）"""
        d = Vector((math.sin(th), math.cos(th), 0.0))
        org = Vector((0.0, Y_AXIS, z)) + d * rmax
        h = bvh.ray_cast(org, -d, rmax * 2)
        if h[0] is None or h[1].dot(-d) > 0:
            return None
        p = h[0]
        return math.hypot(p.x, p.y - Y_AXIS)

    def curve(th, k, e):
        """领口截面曲线：x-y 平面上沿 θ 的一条三次 Hermite。
        🔴 两端都取真实表面切向（起点沿脖子壁、终点沿身体表面）→ C1 连续 → 不会折出棱。
        直线放样两端都不相切，必然折棱（实测：背面一道明显的褶）。"""
        p0 = me.vertices[ring[k][1]].co
        r0 = math.hypot(p0.x, p0.y - Y_AXIS); z0 = p0.z
        p1 = rim_pts[k]
        r1 = math.hypot(p1.x, p1.y - Y_AXIS); z1 = p1.z
        # 起点切向：脖子壁（取环上方 15mm 处的半径）
        ra = radius_at(neck_bvh, th, z0 + 0.015)
        t0 = ((r0 - ra) if ra is not None else 0.0, -0.015)
        # 终点切向：身体表面（取环口下方 20mm 处的半径）
        rb = radius_at(body_bvh, th, z1 - 0.020)
        t1 = ((rb - r1) if rb is not None else (r1 - r0), -0.020)
        n0 = math.hypot(*t0) or 1.0
        n1 = math.hypot(*t1) or 1.0
        L = math.hypot(r1 - r0, z1 - z0)
        m0, m1 = 0.50 * L, 0.50 * L                    # 切向长度（按弦长缩放；太大↗会过冲插进身体 → 口沿附近漏缝）
        t0 = (t0[0] / n0 * m0, t0[1] / n0 * m0)
        t1 = (t1[0] / n1 * m1, t1[1] / n1 * m1)
        e2, e3 = e * e, e * e * e
        h00 = 2 * e3 - 3 * e2 + 1
        h10 = e3 - 2 * e2 + e
        h01 = -2 * e3 + 3 * e2
        h11 = e3 - e2
        rr = h00 * r0 + h10 * t0[0] + h01 * r1 + h11 * t1[0]
        zz = h00 * z0 + h10 * t0[1] + h01 * z1 + h11 * t1[1]
        return Vector((rr * math.sin(th), Y_AXIS + rr * math.cos(th), zz))

    prev = [bm.verts[vi] for (th, vi) in ring]
    new_faces = []
    for r in range(1, n_inter):
        e = r / (n_inter - 1)
        row = [bm.verts.new(curve(th, k, e)) for k, (th, vi) in enumerate(ring)]
        for k in range(n_ring):
            k2 = (k + 1) % n_ring
            new_faces.append(bm.faces.new((prev[k], prev[k2], row[k2], row[k])))
        prev = row
    bm.normal_update()
    # 法线朝向自检：领口第一圈的外法线应当背离中轴（径向外）；不对就把新增面全部反向
    fc = new_faces[0]
    c = fc.calc_center_median()
    rad = Vector((c.x, c.y - Y_AXIS, 0.0)).normalized()
    if fc.normal.dot(rad) < 0:
        print("[warn] 领口面朝向反了，已全部翻转")
        for f in new_faces:
            f.normal_flip()
        bm.normal_update()
    bm.to_mesh(me); bm.free(); me.update()
    print("领口：在脖子网格上新增 %d 环 × %d 段，网格合计 %d 顶点 %d 面"
          % (n_inter - 1, n_ring, len(me.vertices), len(me.polygons)))

    # 🔴 脖子来自蒂法 source body，自带 4 个身体材质槽 —— 必须清掉换成脸壳的材质 0，
    #    否则并进脸壳后材质槽变多，编辑器会按材质把网格拆成多个子网格，破坏"4 件"结构。
    me.materials.clear()
    me.materials.append(shell.data.materials[0])
    for p in me.polygons:
        p.material_index = 0

    # 🔴🔴 头壳自己的底口（源模型就带着的一圈开口）若有一小段露在脖子曲面【外面】，
    #      从外面就能顺着这道口看进头壳内部 —— 实机/编辑器里就是脖子根上的一道缝
    #      （品红背景 + 背面剔除渲染抓到的）。修法：把这些底口顶点收进脖子体内 3mm。
    #      它们本来就被脖子挡着，收进去外面看不出来。
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    neck_bvh = BVHTree.FromBMesh(bm)
    bm.free()
    shell_loops = [lp for lp in boundary_loops(shell) if len(lp) >= 8]
    shell_loops.sort(key=len, reverse=True)
    rim_ids = set()
    for lp in shell_loops:
        if min(shell.data.vertices[i].co.z for i in lp) < 1.62:      # 头壳底口那一圈
            rim_ids.update(lp)
    print("头壳底口候选顶点 = %d 个（共 %d 个自由边环）" % (len(rim_ids), len(shell_loops)))
    tucked = 0
    for i in rim_ids:
        v = shell.data.vertices[i]
        loc, nrm, fidx, dist = neck_bvh.find_nearest(v.co)
        if loc is None or nrm is None:
            continue
        if (v.co - loc).dot(nrm) > 0:          # 在脖子外面 = 会露缝
            v.co = loc - nrm * 0.003
            base0[i] = v.co.copy()             # 同步形状键基准（否则后面的顺序自检会误报）
            tucked += 1
    shell.data.update()
    bpy.context.view_layer.update()
    print("头壳底口：收进脖子内部的顶点 = %d 个（露在外面会漏缝）" % tucked)

    # ---------- 6) 合并：脖子 + 领口 并进脸壳 ----------
    n_shell = len(shell.data.vertices)
    bpy.ops.object.select_all(action='DESELECT')
    tb.select_set(True)
    shell.select_set(True)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.join()
    merged = shell
    n_all = len(merged.data.vertices)
    print("合并后 %d 顶点（脸壳 %d + 脖子领口 %d）" % (n_all, n_shell, n_all - n_shell))
    mv = merged.data.vertices
    if max((mv[i].co - base0[i]).length for i in range(n_shell)) > 1e-5:
        fail("合并后脸壳顶点顺序变了（前 %d 个不再对应原顶点）" % n_shell)

    # ---------- 7) 权重：从原版头最近点抄 ----------
    for bn in set(BONE_ALIAS.values()):
        if bn not in merged.vertex_groups:
            merged.vertex_groups.new(name=bn)
    inside = 0
    for i in range(n_shell, n_all):
        p = mv[i].co
        w = nearest_weights(p, vkd, vwts)
        for vn, val in w.items():
            bn = BONE_ALIAS.get(vn)
            if bn and val > 1e-4:
                merged.vertex_groups[bn].add([i], val, 'ADD')   # ADD=新增或覆盖；REPLACE 对未入组的顶点是空操作（踩过）
        hit = body_bvh.find_nearest(p)
        if hit and hit[0] and (p - hit[0]).dot(hit[1]) < 0:
            inside += 1
    print("权重抄写完成；新顶点中【落在身体内部】的 %d/%d（外沿埋入的那一圈属正常）" % (inside, n_all - n_shell))

    # ---------- 8) UV：柱面展开到脸贴图的颈胸区 ----------
    # 🔴 先删掉多余的 UV 层：脖子来自蒂法 source body，自带 2 套身体 UV（Base Female /
    #    Golden Palace），并进来后 FBX 会有 3 套 UV。脸材质带 doubleuv 标记 → 引擎会读第 2 套，
    #    而新增面在第 2/3 套上是垃圾坐标（源身体那套 u∈[1.11,1.89]，超出 0~1）→ 领口糊出怪纹。
    while len(merged.data.uv_layers) > 1:
        nm_ = merged.data.uv_layers[len(merged.data.uv_layers) - 1].name
        merged.data.uv_layers.remove(merged.data.uv_layers[len(merged.data.uv_layers) - 1])
        print("  删掉多余 UV 层：%s" % nm_)
    print("  剩余 UV 层：%s" % [l.name for l in merged.data.uv_layers])

    uvl = merged.data.uv_layers[0]
    for poly in merged.data.polygons:
        if poly.vertices[0] < n_shell:
            continue
        for li in poly.loop_indices:
            vi = merged.data.loops[li].vertex_index
            p = mv[vi].co
            th = math.atan2(p.x, p.y - Y_AXIS)                 # (-π, π]，0 = 正前
            u = 0.5 + th / (2 * math.pi) * (UV_U1 - UV_U0)
            v = UV_V0 + (p.z - UV_Z0) / (UV_Z1 - UV_Z0) * (UV_V1 - UV_V0)
            uvl.data[li].uv = (max(UV_U0, min(UV_U1, u)), max(UV_V0, min(UV_V1, v)))

    # ---------- 9) 形状键重建 ----------
    # 🔴 custom split normals（自定义法线）：源模型自带、v10 也带着。bmesh 新加的面拿不到
    #    正确法线 → 导出后编辑器里领口是一格一格的面片（实机也是一样）。正解 = 用
    #    "按顶点平滑"的法线把 custom normals 重设一遍。脸壳本来就是 8742/8742 全平滑，
    #    重设后与源模型完全一致，无副作用。
    for p in merged.data.polygons:
        p.use_smooth = True
    merged.data.update()
    vnorms = [v.normal.copy() for v in merged.data.vertices]
    merged.data.normals_split_custom_set_from_vertices(vnorms)
    print("法线：已用按顶点平滑的法线重设 custom normals（%d 顶点）" % len(vnorms))
    rebuild_shapes(merged, delta, n_shell)
    ks = [kb.name for kb in merged.data.shape_keys.key_blocks]
    want = ["Basis", "KeyTime_0"] + ["KeyTime_%d" % n for n in range(1, 60)]
    if ks != want:
        fail("形状键顺序不对：%s" % ks[:6])
    print("形状键重建 OK：%d 个" % len(ks))
    print("材质槽检查：%s（应只有 1 个 = 脸材质）" % [m.name if m else None for m in merged.data.materials])

    # ---------- 10) 自检 ----------
    for ob in meshes:
        if any(abs(s - 1.0) > 1e-4 for s in ob.scale):
            fail("%s 带非单位缩放" % ob.name)
    print("合并后四件：")
    for ob in meshes:
        print("  %-18s verts=%-6d %s" % (ob.name, len(ob.data.vertices), bbox_str(ob)))
    newz = [mv[i].co.z for i in range(n_shell, n_all)]
    print("新几何 z[%.4f,%.4f]" % (min(newz), max(newz)))

    # 接缝自检：脖子与领口交界那一圈【不许有自由边】
    #   （领口最外圈是故意开口的——它埋在身体里；所以只查 z 1.51~1.56 这一带）
    bm = bmesh.new(); bm.from_mesh(merged.data)
    bm.edges.ensure_lookup_table()
    jun = []
    for e in bm.edges:
        if len(e.link_faces) == 1:
            zm = (e.verts[0].co.z + e.verts[1].co.z) / 2.0
            if 1.51 <= zm <= 1.56 and e.verts[0].index >= n_shell and e.verts[1].index >= n_shell:
                jun.append(zm)
    bm.free()
    print("脖子↔领口交界自由边 = %d 条 %s" % (len(jun), "✓" if not jun else "✗ 会有缝！"))
    if jun:
        fail("脖子与领口交界处有 %d 条自由边（几何没接上）" % len(jun))

    # ---------- 11) 导出（必须在预览之前！） ----------
    # 🔴 顺序铁律：render_preview 会替换材质（改成预览用的贴图材质），
    #    导出前跑预览 = 交付的 FBX 里脸壳材质名变成 "texprev"（实测踩过）。
    #    参考物也必须先清掉：骑砍2 女子身体 OBJ 是量领口用的，留着会被一起导出
    #    （实测：FBX 里混进 body_female_a.lod1~5）。
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o not in meshes:
            print("  移除参考物：%s" % o.name)
            bpy.data.objects.remove(o, do_unlink=True)
    print("导出对象清单：%s" % sorted("%s(%s)" % (o.name, o.type) for o in bpy.data.objects))
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.view_layer.update()
    if abs(bpy.context.scene.unit_settings.scale_length - 1.0) > 1e-6:
        fail("场景单位不是米")
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=False, object_types={'MESH', 'ARMATURE'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='Y', axis_up='Z',
        use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
    )
    print("EXPORTED -> " + OUT_FBX)
    try:
        import shutil
        shutil.copy2(OUT_FBX, OUT_BAK)
        print("已备份 -> " + OUT_BAK)
    except Exception as e:
        print("备份失败（不致命）：%s" % e)

    # ---------- 12) 渲染预览（必须在导出之后：它会替换材质、切 REST 姿态） ----------
    try:
        render_preview(merged, meshes)
    except Exception as e:
        print("预览渲染失败（不致命）：%s" % e)
    print("\nBUILD DONE")


def render_preview(merged, meshes):
    """导出【之后】才跑：会替换材质 + 切 REST 姿态，绝不能污染交付的 FBX。
    身体参考物已被清掉，这里自己重新导一份。"""
    import os
    os.makedirs(RENDER_DIR, exist_ok=True)
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=BODY_OBJ, forward_axis='Y', up_axis='Z')
    body = [o for o in bpy.data.objects if o not in before][0]
    # 🔴 预览前要把"形变"停掉，但【不能摘 Armature 修改器】—— 摘了导出就没有蒙皮数据了
    #    （实测：v11 第一次导出四件全丢顶点组）。正解 = 把骨架切到 REST 姿态。
    for ob in [merged, body] + list(meshes):
        if ob.animation_data:
            ob.animation_data_clear()
        sk = ob.data.shape_keys
        if sk:
            if sk.animation_data:
                sk.animation_data_clear()
            for kb in sk.key_blocks:
                kb.value = 0.0
    for a in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
        a.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = merged.evaluated_get(dg)
    ps = [ev.matrix_world @ v.co for v in ev.to_mesh().vertices]
    print("  [预览自检] 求值后网格 bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]"
          % (min(p.x for p in ps), max(p.x for p in ps), min(p.y for p in ps),
             max(p.y for p in ps), min(p.z for p in ps), max(p.z for p in ps)))

    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_object_outline = True
    sc.display.shading.show_backface_culling = False
    sc.render.resolution_x = 640; sc.render.resolution_y = 640
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.10, 0.10, 0.12)
    cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'
    cam = bpy.data.objects.new("cam", cd)
    sc.collection.objects.link(cam); sc.camera = cam

    def tint(objs, rgb):
        m = bpy.data.materials.new("t"); m.use_nodes = True
        b = m.node_tree.nodes.get("Principled BSDF")
        if b:
            b.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1)
        m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1)
        for o in objs:
            o.data.materials.clear(); o.data.materials.append(m)

    tint([body], (0.55, 0.58, 0.62))
    tint([merged], (0.88, 0.68, 0.58))

    def shoot(name, loc, target, scale):
        cd.ortho_scale = scale
        cam.location = Vector(loc)
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(RENDER_DIR, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("  preview -> " + name)

    shoot("p_v11_front", (0, 1.2, 1.45), (0, 0.03, 1.52), 0.42)
    shoot("p_v11_side", (1.2, 0, 1.45), (0, 0.03, 1.52), 0.42)
    shoot("p_v11_low", (0, 0.9, 1.10), (0, 0.03, 1.50), 0.42)
    shoot("p_v11_wide", (0, 1.6, 1.30), (0, 0.03, 1.30), 0.90)
    shoot("p_v11_back", (0, -1.2, 1.45), (0, 0.03, 1.52), 0.42)

    # 贴图预览：挂上脸贴图，检查脖子/领口的 UV 采样对不对（进编辑器前最重要的一关）
    try:
        img = bpy.data.images.load(r"F:/下载/Tifa lockhart In Drees - FBX/Tifa_Head_D.jpg")
        tm = bpy.data.materials.new("texprev")
        tm.use_nodes = True
        nt = tm.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        merged.data.materials.clear()
        merged.data.materials.append(tm)
        sc.display.shading.color_type = 'TEXTURE'
        sc.display.shading.light = 'FLAT'
        shoot("p_v11_tex_front", (0, 1.2, 1.55), (0, 0.03, 1.58), 0.45)
        shoot("p_v11_tex_low", (0, 0.9, 1.15), (0, 0.03, 1.52), 0.45)
        shoot("p_v11_tex_wide", (0, 1.6, 1.35), (0, 0.03, 1.35), 0.85)
    except Exception as e:
        print("贴图预览失败（不致命）：%s" % e)


if _st == "build":
    stage_build()

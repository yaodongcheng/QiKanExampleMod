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
import bpy, sys, os, math, mathutils
from mathutils import kdtree

FACE_CH = 59          # 脸形通道上限（1..59）= 皮肤 deform_keys 能拉到的范围
anim_smooth_w = 0.5   # 表情段平滑权重（--anim-smooth-w 覆盖）
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


def deepen_mouth(ob, mm):
    """把嘴内壁沿 −y（向后）推 mm，**所有形状键一起平移**（含 Basis，保持一致）。

    为什么：自建头的嘴"张不开"未必是位移问题 —— 内唇面紧贴唇线，小幅度张嘴时看到的是
    **被打亮的内壁**而不是凹进去的暗道，观感就是"没张嘴"。把内壁推后几毫米 = 加深口腔，
    开口立刻读得出来（推的是不可见的内壁，闭嘴时完全看不到）。

    渐隐：按"比该 (x,z) 格子的最前面低多少"给权重（>4mm 全推、唇线处 0），避免唇缘撕裂。
    """
    if mm <= 0:
        return
    me = ob.data
    base = [v.co.copy() for v in me.vertices]
    CELL = 0.004
    maxy = {}
    for p in base:
        key = (int(round(p.x / CELL)), int(round(p.z / CELL)))
        if p.y > maxy.get(key, -9.0):
            maxy[key] = p.y
    shifts = []
    for p in base:
        depth = maxy[(int(round(p.x / CELL)), int(round(p.z / CELL)))] - p.y
        if not (1.560 <= p.z <= 1.650 and abs(p.x) <= 0.030 and p.y >= 0.08 and depth > 0.0015):
            shifts.append(0.0)
            continue
        w = min(1.0, (depth - 0.0015) / 0.004)      # 唇线处 0 → 深处 1
        shifts.append(w)
    n = sum(1 for w in shifts if w > 0)
    d = mathutils.Vector((0.0, -mm, 0.0))
    for i, w in enumerate(shifts):
        if w > 0:
            me.vertices[i].co = base[i] + d * w
    for kb in me.shape_keys.key_blocks:
        for i, w in enumerate(shifts):
            if w > 0:
                kb.data[i].co = kb.data[i].co + d * w
    print("  口腔加深：内壁 %d 个顶点后移最多 %.1fmm（唇缘渐隐）" % (n, mm * 1000))


JAW_Z = [0.0]      # jaw_field 算出的唇线高度（回传给主循环用）


def split_lips(ob, z_lip, tag=""):
    """沿唇缝把上下唇**切成两片独立的面**（不然下颌一动，整片嘴唇的皮只会被拉长 = 实机"嘴张不开"）。

    为什么必须切：原版头的上下唇是**两片分开的壳**，所以 morph 一拉就裂开一道缝；
    亨利/KCD 这类模型的嘴是**一整片连续面**（靠骨骼驱动），搬进 morph 体系后下颌一动
    只能把中间那片皮拉长 —— 实机症状就是"嘴唇被扯长、但没张开"（2026-09-20 用户两次实锤）。
    """
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ob.data)

    def in_mouth(c):
        return abs(c.x) <= 0.050 and c.y >= 0.050

    # ① **沿唇线分段切**：唇线是弯的（嘴角比中间高），用一个水平面切会切歪 → 嘴角留一圈还连着的皮
    #    （2026-09-20 实锤："中间一个小洞、两侧紧闭带褶"）。逐 x 带用该带自己的唇缝高度切。
    bands = mouth_line_bands(ob)
    ncut = 0
    for (bx0, bx1, bz) in bands:
        straddle = [f for f in bm.faces
                    if any(v.co.z < bz for v in f.verts) and any(v.co.z > bz for v in f.verts)
                    and all(in_mouth(v.co) and bx0 - 1e-6 <= v.co.x <= bx1 + 1e-6 for v in f.verts)]
        if not straddle:
            continue
        geom = set()
        for f in straddle:
            geom.add(f)
            geom.update(f.verts)
            geom.update(f.edges)
        bmesh.ops.bisect_plane(bm, geom=list(geom), dist=1e-6,
                               plane_co=(0.0, 0.0, bz), plane_no=(0.0, 0.0, 1.0))
        ncut += len(straddle)
    # ② 沿切线把上下唇切开（各自成环，不再共享顶点）→ 两片独立的面
    edges = []
    for e in bm.edges:
        a, b = e.verts
        if abs(a.co.z - b.co.z) > 1e-9 or not in_mouth(a.co) or not in_mouth(b.co):
            continue
        z = a.co.z
        for (bx0, bx1, bz) in bands:
            if bx0 - 1e-6 <= a.co.x <= bx1 + 1e-6 and abs(z - bz) < 1e-5:
                edges.append(e)
                break
    if not edges:
        bm.free()
        print("  [split] %s：没找到可切的边（跳过）" % tag)
        return 0
    bmesh.ops.split_edges(bm, edges=edges)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    print("  [split] %s：沿唇线分 %d 段切（每段各自高度）+ 切开 %d 条边 → 上下唇两片独立"
          % (tag, len(bands), len(edges)))
    return len(edges)


def mouth_line_bands(ob, nband=7):
    """唇缝高度**逐 x 带**各算一个（唇线是弯的：嘴角比中间高 2~4mm，一个平面切不干净）。

    返回 [(x0, x1, z), ...]；取不到就回退成"整条一个值"。
    """
    import bmesh
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    ring = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack = [v]
        seen[v.index] = True
        comp = []
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    stack.append(o2)
        zs = [c.co.z for c in comp]
        ys = [c.co.y for c in comp]
        if 300 <= len(comp) <= 900 and 1.55 <= min(zs) and max(zs) <= 1.67 and sum(ys) / len(ys) > 0.08:
            ring = [c.co.copy() for c in comp if any(len(e.link_faces) == 1 for e in c.link_edges)]
            break
    bm.free()
    if not ring:
        z = mouth_line_z(ob)
        return [(-0.08, 0.08, z)] if z > 0 else []
    xs = [c.x for c in ring]
    x0, x1 = min(xs), max(xs)
    step = (x1 - x0) / nband
    out = []
    for i in range(nband):
        a, b = x0 + i * step, x0 + (i + 1) * step
        zz = sorted(c.z for c in ring if a <= c.x <= b or (i == nband - 1 and c.x >= a))
        if zz:
            out.append((a, b, zz[len(zz) // 2]))
    return out


def mouth_line_z(ob):
    """嘴缝（上下唇相接）的高度 = 「口腔内衬」连通域**边界那圈**的平均 z。

    为什么不能用"最前突点"：自建头的**上唇**往往才是全嘴最前突的（亨利上唇 z≈1.643），
    拿它当"零位移线"会让整张嘴（含上唇）一起往下平移 = 实机看到"嘴唇被扯长、嘴没张开"
    （2026-09-20 实锤）。口腔内衬是独立连通域，它贴着唇内侧，**它的自由边界就是唇缝**。
    回退（认不出内衬）：取嘴区最前突点下移 25mm。
    """
    import bmesh
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    target = None
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack = [v]
        seen[v.index] = True
        comp = []
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    stack.append(o2)
        zs = [c.co.z for c in comp]
        ys = [c.co.y for c in comp]
        if 300 <= len(comp) <= 900 and 1.55 <= min(zs) and max(zs) <= 1.67 and sum(ys) / len(ys) > 0.08:
            target = comp
            break
    if target is None:
        bm.free()
        mouth = [v.co for v in me.vertices if abs(v.co.x) <= 0.035 and v.co.y >= 0.10 and 1.58 <= v.co.z <= 1.70]
        if not mouth:
            return 0.0
        return max(mouth, key=lambda c: c.y).z - 0.025
    zb = []
    for v in target:
        if any(len(e.link_faces) == 1 for e in v.link_edges):      # 边界点
            zb.append(v.co.z)
    bm.free()
    if not zb:
        return 0.0
    zb.sort()
    return zb[len(zb) // 2]


def jaw_field(base, amount_mm, tag="", z_lip_in=None):
    """按【绕颌关节旋转】直接算一张"张嘴"位移场（不靠搬运）。

    为什么：从原版头搬下颌动作要过"源网格太粗 → 折线被抹平 → 撕裂 → 平滑 → 又钝掉"这一串，
    而**下颌本来就是绕颌关节的刚体旋转** —— 直接算，嘴必然开、上唇必然不动（这才是张嘴的本质）。

    参数全自动：
      · 唇线高度 z_lip = 嘴部最前面那批点的中位 z
      · 颌关节 H = (0, y_back, z_lip + 45mm)（耳前那一点，取个常识值）
      · 旋转量 θ 由"这一帧下巴该下移多少"（amount_mm）反解：θ ≈ amount / |下巴 − H|
      · 权重 w：唇线处 0 → 唇下 20mm 处 1；下巴底(1.545)以下再渐隐（脖子不跟着转）
    """
    import math as _m
    z_lip = z_lip_in if z_lip_in else 0.0
    if z_lip <= 0:
        mouth = [p for p in base if abs(p.x) <= 0.03 and p.y >= 0.10 and 1.58 <= p.z <= 1.645]
        if not mouth:
            print("  [jaw] 认不出嘴部（跳过）")
            return None
        ymax = max(p.y for p in mouth)
        lip_z = sorted(p.z for p in mouth if p.y > ymax - 0.002)
        z_lip = lip_z[len(lip_z) // 2]
    H = mathutils.Vector((0.0, 0.02, z_lip + 0.045))
    chin_z = z_lip - 0.055
    r = abs(H.z - chin_z)
    theta = (amount_mm / 1000.0) / max(r, 1e-4)

    def w_of(p):
        if p.z > z_lip:
            return 0.0
        d = z_lip - p.z                                   # 唇线以下多深
        # 🔴 过渡宽度**随 |x| 变**：嘴中央那一段已经**沿唇缝切开**（两片独立面）→ 可以硬切（2mm）；
        #    嘴角一带没切开（切了会连嘴角一起豁口）→ 必须宽渐变，否则硬切会把嘴角的面拉成长条
        #    （2026-09-20 实锤：整圈硬切 = 嘴角两撮"拉丝"）
        ax = abs(p.x)
        ramp = 0.002 if ax <= 0.025 else min(0.030, 0.002 + 0.028 * ((ax - 0.025) / 0.030))
        w = min(1.0, d / ramp)
        if p.z < 1.545:                                   # 下巴底以下 → 渐隐（脖子不跟转）
            w *= max(0.0, min(1.0, (p.z - 1.500) / 0.045))
        return w

    cz, sz = _m.cos(theta), _m.sin(theta)
    out = []
    for p in base:
        w = w_of(p)
        if w <= 0:
            out.append(mathutils.Vector((0, 0, 0)))
            continue
        rel = p - H
        # 绕 X 轴转：上唇在前不动、下巴往下后（θ>0 = 下颌向下）
        ry = rel.y * cz + rel.z * sz
        rz = -rel.y * sz + rel.z * cz
        out.append(mathutils.Vector((0.0, ry - rel.y, rz - rel.z)) * w)
    if tag:
        print("  [jaw] %s：唇线 z=%.3f 关节 z=%.3f 旋转 %.2f° → 下巴下移 %.1fmm"
              % (tag, z_lip, H.z, _m.degrees(theta), amount_mm))
    JAW_Z[0] = z_lip
    return out


def count_real(deltas, lo, hi):
    return sum(1 for n in range(lo, hi + 1)
               if max(abs(d.x) + abs(d.y) + abs(d.z) for d in deltas[n]) > 1e-4)


# ---------------- 表情段的两种映射（2026-09-19 新增 proj） ----------------
# 背景：3 点反距离加权会把源网格上的**折线**（"上唇不动 / 下唇下移"）抹成渐变 →
#   目标上唇跟着下唇走 = 嘴张不开（实测唇线位移只有源头的一半）。
# proj = 沿目标顶点**法线**投影到源网格、取命中三角形的重心坐标插值（原样复制三角形内的线性场），
#   命中不了（表面相距太远/朝向不符）就回退到 nn。法线投影比"最近三角形"稳：
#   我们的头壳含脖子/肩、源男头没有那一大块，纯最近三角形会找到远处的错三角形（实测撕碎）。
PROJ_EPS = 2e-4      # 起投偏移 0.2mm（防自命中）
# 🔴 投影最远距离：实测**两颗头的表面相距 7~13mm**（亨利下半脸比原版男头前突 1cm+），
#    6mm 门限会让嘴部几乎全回退到最近邻 = 白改。偏移主要沿 ±y（正是法线方向），
#    放宽到 15~18mm 仍落在解剖对应的那块面上。用 --proj-max 调（单位 mm）。
PROJ_MAX = 15e-3
PROJ_DOT = 0.3       # 源三角形法线与目标法线的最小夹角余弦


def triangulate(mesh):
    """把多边形拆成三角形列表（扇形），返回 [(i0,i1,i2), ...]，与 BVH 的 index 一一对应。"""
    tris = []
    for p in mesh.polygons:
        vs = list(p.vertices)
        for k in range(1, len(vs) - 1):
            tris.append((vs[0], vs[k], vs[k + 1]))
    return tris


def bary_coords(p, a, b, c):
    v0 = b - a; v1 = c - a; v2 = p - a
    d00 = v0.dot(v0); d01 = v0.dot(v1); d11 = v1.dot(v1)
    d20 = v2.dot(v0); d21 = v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-20:
        return 1.0, 0.0, 0.0
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    return 1.0 - v - w, v, w


def build_mapper(target_ob, src_base, src_chan, src_tris, mode, tag):
    """给一个目标件建"取位移"的映射（**只算一次**，41 帧共用）。

    🔴 `src_*` 必须全是**纯数据**（源对象在载入目标时已被场景重置销毁，见 main 里的 load 说明）。
    返回 get(frame) -> [Vector,...]（与目标顶点一一对应）。
    """
    t_pts = [v.co.copy() for v in target_ob.data.vertices]
    t_nrm = [v.normal.copy() for v in target_ob.data.vertices]
    n_t = len(t_pts)
    kd = make_kd(src_base)
    plan = [None] * n_t
    stat = {"proj": 0, "nn": 0}
    if mode == "proj":
        from mathutils.bvhtree import BVHTree
        bvh = BVHTree.FromPolygons(src_base, src_tris, all_triangles=True)
        # 🔴 「是不是外表面」判据：同一 (x,z) 格子里 y 最大的那层才是外表面，
        #    比它靠后 >3mm 的顶点 = **内部面**（口腔内衬、下巴内侧、后脑勺…）→ 一律走最近邻。
        #    不这么分：内衬的法线也朝前，18mm 投影会打到前面的下巴面 → 内衬拿到下巴的场 → 撕出尖刺（实测）。
        CELL = 0.004
        maxy = {}
        for p in t_pts:
            key = (int(round(p.x / CELL)), int(round(p.z / CELL)))
            if p.y > maxy.get(key, -9.0):
                maxy[key] = p.y
        interior = [maxy[(int(round(p.x / CELL)), int(round(p.z / CELL)))] - p.y > 0.003 for p in t_pts]
        for i, p in enumerate(t_pts):
            if interior[i]:
                plan[i] = ("N", kd.find_n(p, NN))
                stat["nn"] += 1
                continue
            n = t_nrm[i]
            hit = None
            for origin, direction in ((p + n * PROJ_EPS, -n), (p - n * PROJ_EPS, n)):
                loc, nrm, idx, dist = bvh.ray_cast(origin, direction, PROJ_MAX)
                if idx is None or loc is None:
                    continue
                if nrm is not None and abs(nrm.dot(n)) < PROJ_DOT:
                    continue
                hit = (idx, loc)
                break
            if hit is not None:
                idx, loc = hit
                a, b, c = src_tris[idx]
                plan[i] = ("P", a, b, c, bary_coords(loc, src_base[a], src_base[b], src_base[c]))
                stat["proj"] += 1
            else:
                plan[i] = ("N", kd.find_n(p, NN))
                stat["nn"] += 1
    else:
        for i, p in enumerate(t_pts):
            plan[i] = ("N", kd.find_n(p, NN))
            stat["nn"] += 1

    def get(frame):
        chan = src_chan[frame]
        out = []
        for pl in plan:
            if pl[0] == "P":
                _, a, b, c, (wa, wb, wc) = pl
                out.append(chan[a] * wa + chan[b] * wb + chan[c] * wc)
            else:
                hits = pl[1]
                if hits[0][2] < 1e-6:
                    out.append(chan[hits[0][1]])
                    continue
                ws = [1.0 / ((h[2] + EPS) ** 2) for h in hits]
                s = sum(ws)
                v = mathutils.Vector((0, 0, 0))
                for h, w in zip(hits, ws):
                    v = v + chan[h[1]] * (w / s)
                out.append(v)
        return out

    print("    %-22s 映射：投影 %d / 回退最近邻 %d（共 %d 顶点）"
          % (tag, stat["proj"], stat["nn"], n_t))
    return get


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
    anim_map = get(args, "--anim-map", "nn")
    global PROJ_MAX, PROJ_DOT
    if get(args, "--proj-max"):
        PROJ_MAX = float(get(args, "--proj-max")) / 1000.0
    if get(args, "--proj-dot"):
        PROJ_DOT = float(get(args, "--proj-dot"))
    anim_smooth = int(get(args, "--anim-smooth", "0"))
    mouth_deepen = float(get(args, "--mouth-deepen", "0"))   # 单位 mm
    anim_jaw = "--anim-jaw" in args                          # 下颌用解析旋转
    anim_jaw_gain = float(get(args, "--anim-jaw-gain", "1.0"))  # 张嘴幅度倍率
    anim_gain_k = float(get(args, "--anim-gain-k", "0"))     # 非线性增益系数
    anim_gain_d = float(get(args, "--anim-gain-d", "0.006")) # 增益的衰减尺度（m）
    global anim_smooth_w
    anim_smooth_w = float(get(args, "--anim-smooth-w", "0.5"))
    if anim_map not in ("nn", "proj"):
        fail("--anim-map 只认 nn / proj，收到 %s" % anim_map)

    # ---- 脸形段（1..59）的源：单件（脸壳），沿用现状 ----
    # 🔴 `load()` 会 read_factory_settings 重置场景 —— 源的数据必须在**下一次 load 之前**
    #    全部拷成纯 Python 数据（Vector 是拷贝）。留对象引用跨 load 使用 = ReferenceError 崩。
    srcs = pick(load(src_path), src_hint, "源 FBX")
    if len(srcs) != 1:
        fail("源需恰好 1 个网格（用 --src-object 指定），实际匹配 %d 个：%s" % (len(srcs), [o.name for o in srcs]))
    s_base, s_chan = read_channels(srcs[0])
    s_tris = triangulate(srcs[0].data)     # 🔴 三角形列表也必须在 load 之前拷成纯数据
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
        anim_parts = [read_channels(by_name[w], FACE_CH + 1, CH_MAX) + (triangulate(by_name[w].data),)
                      for w in want]
        print("表情源 %s：按件对位 %s（各 %d 条通道）"
              % (anim_path, want, CH_MAX - FACE_CH))
    else:
        a_base, a_chan = read_channels(srcs[0], FACE_CH + 1, CH_MAX)   # 同一件套到所有目标件（旧行为）
        a_tris = s_tris
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
        # 🔴 唇缝切开必须**最前面**做：切开后顶点数变了，后面所有按顶点下标的计算（脸形段/映射/形状键）
        #    都得在切开后的网格上进行（放中间做会 IndexError —— 实测踩过）
        if anim_jaw and ob.name.endswith(".0"):
            _z = mouth_line_z(ob)
            split_lips(ob, _z, ob.name)
        base = [v.co.copy() for v in ob.data.vertices]
        deltas = {}
        for n in range(1, FACE_CH + 1):
            deltas[n] = remap(base, kd_face, s_chan[n])
        if anim_parts is None:
            a_base, a_chan, a_tris = s_base, a_chan, a_tris
            anim_note = "同脸形源 %s" % src_name
        else:
            a_base, a_chan, a_tris = anim_parts[dst_part[i]]
            anim_note = "表情源 %s" % want[dst_part[i]]
        # 🔴 表情段一律走 build_mapper：映射**只算一次**、41 帧共用；
        #    --anim-map proj 时按法线投影到源表面取重心坐标（保住唇线那道折线），命中不了回退最近邻。
        mapper = build_mapper(ob, a_base, a_chan, a_tris, anim_map, "%s ← %s" % (ob.name, anim_note))
        for n in range(FACE_CH + 1, CH_MAX + 1):
            deltas[n] = mapper(n)
        # 表情段的轻量平滑（--anim-smooth N，默认 0 关闭）：
        #   🔴 投影会在"上唇/下唇分界"处让相邻顶点落到源头不同三角形上 → 各差 10~20mm → 唇缘撕出尖刺。
        #      沿网格边做 N 轮 1 环平均（权重 0.5）能把尖刺抹掉，代价是唇线略钝（比原方案的"完全不张"好得多）。
        if anim_smooth > 0:
            adj = [[] for _ in range(len(base))]
            for e in ob.data.edges:
                a, b = e.vertices
                adj[a].append(b)
                adj[b].append(a)
            for n in range(FACE_CH + 1, CH_MAX + 1):
                col = deltas[n]
                for _ in range(anim_smooth):
                    new = []
                    for i, dv in enumerate(col):
                        nb = adj[i]
                        if not nb:
                            new.append(dv)
                            continue
                        acc = mathutils.Vector((0, 0, 0))
                        for j in nb:
                            acc = acc + col[j]
                        new.append(dv * (1.0 - anim_smooth_w) + acc * (anim_smooth_w / len(nb)))
                    col = new
                deltas[n] = col
        cur_anim_chan = a_chan
        # 非线性增益（--anim-gain-k / --anim-gain-d）：小幅位移放大、大幅不动。
        #   为什么：自建头的嘴"小幅度时读不出张嘴"（内壁被打亮 = 看着没开），而原版在同样的小幅度下
        #   能读出暗缝。放大小幅、不动大幅 → 小声说话也能看出嘴在动，而大喊时不会夸张到撕裂。
        # 🔴 下颌动作**直接算**（绕颌关节旋转），不再依赖搬运：
        #    搬运来的下颌只当"这一帧该张多大"的量纲（取下唇/下巴区的平均下移），
        #    然后用解析旋转替换下半脸的场 —— 嘴必然开、上唇必然不动。
        if anim_jaw:
            z_lip_ob = mouth_line_z(ob) if ob.name.endswith(".0") else 0.0
            if ob.name.endswith(".0"):
                print("    嘴缝高度（内衬边界）= z %.4f" % z_lip_ob)
            for n in range(FACE_CH + 1, CH_MAX + 1):
                col = deltas[n]
                sel = [i for i, p in enumerate(base)
                       if abs(p.x) <= 0.03 and p.y >= 0.10 and 1.545 <= p.z <= 1.585]
                if not sel:
                    continue
                amt = -sum(col[i].z for i in sel) / len(sel) * 1000.0      # 正值 = 该下移多少
                if amt < 0.4:                                              # 这一帧本来就不动下颌
                    continue
                # 🔴 小幅度放大、大幅度不动：实机那条 clip 只用了很小权重（原版看得出、我们看不出），
                #    线性放大满幅度会夸张；这条曲线把 "本来只张 1~3mm" 的帧抬到看得见，满幅度的帧保持原样。
                if anim_gain_k > 0:
                    amt *= 1.0 + anim_gain_k * math.exp(-(amt / 1000.0) / anim_gain_d)
                jf = jaw_field(base, amt * anim_jaw_gain, "" if n % 10 else "f%d" % n, z_lip_ob)
                JZ = JAW_Z if JAW_Z[0] > 0 else None      # 第一帧过后唇线高度就有了
                if n == 71 and ob.name.endswith(".0"):
                    for zz in (1.590, 1.605, 1.614, 1.618, 1.630):
                        cand = [i for i, pp in enumerate(base)
                                if abs(pp.x) <= 0.012 and pp.y >= 0.10 and abs(pp.z - zz) < 0.0015]
                        if not cand:
                            continue
                        i0 = cand[0]
                        w0 = min(1.0, max(0.0, (JZ[0] - base[i0].z) / 0.002)) if JZ else 0.0
                        print("    [w] %s z=%.3f JZ0=%.4f 权重=%.2f  搬运=%.1fmm  jf=%.1fmm"
                              % (ob.name, base[i0].z, JZ[0] if JZ else -1, w0, col[i0].length * 1000, jf[i0].length * 1000))
                if n == 71:
                    _in_band = [i for i, pp in enumerate(base) if abs(pp.x) <= 0.02 and pp.y >= 0.10 and 1.59 <= pp.z <= 1.61]
                    _mx = max((jf[i].length for i in _in_band), default=-1)
                    _mcol = max((col[i].length for i in _in_band), default=-1)
                    print("  [jaw-debug] f71: amt=%.1fmm theta_ok JZ=%s 唇带顶点 %d 个  jaw场最大 %.1fmm  搬运场最大 %.1fmm"
                          % (amt, JZ[0] if JZ else None, len(_in_band), _mx * 1000, _mcol * 1000))
                if jf is None:
                    continue
                # 下半脸：解析旋转占主导（唇线处仍是搬运值，避免与上唇接不上）
                for i, p in enumerate(base):
                    if jf[i].length < 1e-9:
                        continue
                    # 权重同样"唇线以下立刻满"（唇线高度由 jaw_field 现算，别硬写 z —— 两头唇线差 3cm）
                    w = min(1.0, max(0.0, (JZ[0] - p.z) / 0.002)) if JZ else 0.0
                    if w > 0:
                        col[i] = col[i] * (1.0 - w * 0.85) + jf[i] * (w * 0.85)
                deltas[n] = col
        if anim_gain_k > 0:
            for n in range(FACE_CH + 1, CH_MAX + 1):
                col = deltas[n]
                out = []
                for dv in col:
                    m = dv.length
                    f = 1.0 + anim_gain_k * math.exp(-m / anim_gain_d) if m > 1e-6 else 1.0
                    out.append(dv * f)
                deltas[n] = out
        nf = count_real(deltas, 1, FACE_CH)
        na = count_real(deltas, FACE_CH + 1, CH_MAX)
        rebuild(ob, deltas)
        # 🔴 口腔加深必须在 rebuild 之后（那之前还没有形状键；且要把平移施加到**全部**键上）
        if mouth_deepen > 0 and ob.name.endswith(".0"):
            deepen_mouth(ob, mouth_deepen / 1000.0)
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

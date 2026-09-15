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
import bpy, bmesh, sys, os, re, math, mathutils
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
RIM_BURY = 0.008      # 🔴 口沿再往里收 8mm：收到"恰好等于口沿半径"会贴边穿出（实机见过孤立色块），
                      #    多收这 8mm 让整圈藏进身体内侧，边缘不外露
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


def prune_far(ob, k=4.0):
    """【清理飞出去的碎片】—— 战无2 源模型的零件里常混着离主体很远的零星碎块。

    为什么必须做：包围盒被碎片撑大 → 后面全靠包围盒的两步一起崩：
      · `--cut-mouth` 的嘴位估到胸口/腿上去 → 「一个面都没选中」直接失败
      · `--fit-rim` 把碎片当领口猛收 → 实测最大收进 1057mm
    实测（28 人）：不做这步有 11 个人的「收领口」超过 100mm（干净的应该 <30mm），3 个人直接失败。

    判据用**稳健离群**，不写死绝对尺寸（零件大小差异太大）：
      锚点 = 全体顶点的中位数位置（碎片是少数，中位数落在主体上）
      尺度 = 各顶点到锚点距离的中位数（≈ 主体半径）
      丢掉「重心离锚点 > k × 尺度」的连通域
    返回丢掉的顶点数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    n0 = len(bm.verts)
    if n0 == 0:
        bm.free()
        return 0
    seen = [False] * n0
    comps = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack = [v]; seen[v.index] = True; comp = []
        while stack:
            cur = stack.pop(); comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    stack.append(o2)
        comps.append(comp)
    co = [v.co.copy() for v in bm.verts]
    ax = sorted(c.x for c in co)[n0 // 2]
    ay = sorted(c.y for c in co)[n0 // 2]
    az = sorted(c.z for c in co)[n0 // 2]
    anchor = Vector((ax, ay, az))
    dists = sorted((c - anchor).length for c in co)
    scale = dists[n0 // 2]
    if scale <= 1e-9:
        bm.free()
        return 0
    keep = set()
    for comp in comps:
        cc = Vector((0, 0, 0))
        for i in comp:
            cc += co[i]
        cc /= len(comp)
        if (cc - anchor).length <= k * scale:
            keep.update(comp)
    if not keep:                                   # 兜底：一个都不留时保最大的那一块
        keep = set(max(comps, key=len))
    drop = n0 - len(keep)
    if drop:
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return drop


def head_cluster(ob, arm, face_re):
    """算一件的【面部骨族中心与半径】。
    🔴 必须由【脸壳】算一次、全体共用 —— 每块件各自算会不稳：
       头发件只沾 2 根面部骨，簇极小 → 半径过小 → 连头骨 bone_11 都被判成"太远"、整个头发被削掉
       （实测信长：削掉 349/387 个顶点）。
    """
    wsum = {}
    for v in ob.data.vertices:
        for g in v.groups:
            nm = ob.vertex_groups[g.group].name
            wsum[nm] = wsum.get(nm, 0.0) + g.weight
    pts = []
    for nm in wsum:
        b = arm.data.bones.get(nm)
        if b is not None and face_re.match(nm):
            pts.append(arm.matrix_world @ b.head_local)
    if len(pts) < 2:
        return None
    c = Vector((0.0, 0.0, 0.0))
    for q in pts:
        c = c + q
    c = c / len(pts)
    return c, max((q - c).length for q in pts)


def keep_head_only(ob, arm, face_re, cluster=None, radius_k=1.6, gate_k=4.0):
    """【按骨骼剔掉非头部的碎片】—— 复合件（脸件里连着兜帽/外套/手臂）的解法。

    战无2 的骨名是 `bone_N`（纯数字、无语义），但**有位置**：头部各骨挤在头上，
    脊柱/手臂/腿的骨在下面。所以判据 = **顶点的主导骨离"面部骨族中心"多远**：
      · 取该件用到的面部骨族（bone_46~bone_62），算它们的中心 c 与最大半径 r
      · 保留「主导骨位置在 c 的 radius_k×r 之内」的顶点，其余连面一起删

    🔴 别用 z 阈值：头发绑的是**头骨 bone_11，它不在面部骨族里**，一刀切会把整个头发削掉
       （实测信长那次削掉 349/387 个顶点，头直接变秃 + 后续切嘴失败）。空间距离则天然包含它。

    实测（28 人）：不做这步有 18 个人的头是坏的 —— 归蝶那个包围盒从 z=−0.389 到 1.873、宽 1.78 米。
    返回丢掉的顶点数。
    """
    me = ob.data
    wsum = {}
    for v in me.vertices:
        for g in v.groups:
            nm = ob.vertex_groups[g.group].name
            wsum[nm] = wsum.get(nm, 0.0) + g.weight
    if cluster is None:
        cluster = head_cluster(ob, arm, face_re)
    if cluster is None:
        print("  [keep-head] %s：面部骨族不足 2 根，跳过（不能安全判定）" % ob.name)
        return 0
    c, r = cluster
    # 🔴 只有【确实复合】的件才清：件本身的尺度跟头簇差不多 → 它本来就是块头，别动它。
    #    不加这道闸会误伤：信长的脸壳本来是干净的，硬清会削掉他 6cm 的下巴/脖子
    #    （实测：底部从 z=1.4644 抬到 1.5383，回归闸门当场抓出来）。
    #  🔴 判"确实复合"要用【包围盒对角线】，不能用顶点距中位数 ——
    #     中位数对离群不敏感，恰恰把"拖着外套/手臂"的件判成正常（实测岛津义弘：
    #     该清没清，头宽 1.61 米）。包围盒对离群敏感，正好是这里要的性质。
    co = [v.co for v in me.vertices]
    if co:
        lo = [min(q[i] for q in co) for i in range(3)]
        hi = [max(q[i] for q in co) for i in range(3)]
        d_obj = math.sqrt(sum((hi[i] - lo[i]) ** 2 for i in range(3)))
        if d_obj <= gate_k * (2.0 * r):
            print("  [keep-head] %s：判为「非复合」跳过（件对角 %.1f ≤ %.1f）" % (ob.name, d_obj, 5.0 * 2.0 * r))
            return 0
    lim = max(radius_k * r, 1e-6)
    bone_pos = {}
    for nm in wsum:
        b = arm.data.bones.get(nm)
        if b is not None:
            bone_pos[nm] = arm.matrix_world @ b.head_local
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    kill = []
    for v in bm.verts:
        best, bestw = None, -1.0
        for g in me.vertices[v.index].groups:
            if g.weight > bestw:
                bestw, best = g.weight, ob.vertex_groups[g.group].name
        pp = bone_pos.get(best)
        if pp is not None and (pp - c).length > lim:
            kill.append(v)
    n = len(kill)
    # 🔴 安全网：一刀切掉 ≥90% 说明这条判据不适合这块件（前田庆次的长发就是这样被判成"杂质"
    #    全削光的 → 空网格 → 导出时多出一个没名字的对象 → transfer_channels 崩）。
    #    宁可不清，也不能清光。
    if n >= 0.9 * len(bm.verts):
        print("  [keep-head] %s：判据要削掉 %d/%d（≥90%%）→ 判为误伤，跳过"
              % (ob.name, n, len(bm.verts)))
        bm.free()
        return 0
    if n:
        bmesh.ops.delete(bm, geom=kill, context='VERTS')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return n

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
    pick_idx = get(a, "--pick-idx")                  # "face=11,eye=12,hair=5"（序号 = 对象名排序行号）
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
    if pick_spec or pick_idx:
        # 🔴 两套挑件方式，选一个：
        #   --pick     "role=名字子串[+名字子串]"  —— 源模型零件名有意义时用（蒂法/萨菲罗斯）
        #   --pick-idx "role=序号[+序号]"          —— 源模型零件名全是 `model_0_submesh_N_...` 时用。
        #        序号 = 按【对象名排序】的行号，正是零件识别工具 `sw2-pipeline` 产出的零件表行序。
        #        为什么不用子串挑：`submesh_1` 是 `submesh_10`/`submesh_11` 的子串，会误命中一串。
        if pick_idx:
            ordered = sorted(meshes, key=lambda o: o.name)
            wanted = {}
            for item in pick_idx.split(","):
                if "=" not in item:
                    fail("--pick-idx 格式应为 role=序号[+序号]，收到 %r" % item)
                role, val = item.split("=", 1)
                wanted[role.strip()] = [int(x) for x in val.split("+") if x.strip()]
            for role, idxs in wanted.items():
                hit = []
                for ix in idxs:
                    if ix < 0 or ix >= len(ordered):
                        fail("--pick-idx 的「%s」给了序号 %d，但源模型只有 %d 块零件"
                             % (role, ix, len(ordered)))
                    hit.append(ordered[ix])
                picked[role] = hit
        else:
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
            # mouth 件源模型通常没有 —— 给了 --cut-mouth 就由第 2.5 步从脸壳切出来，不算缺件
            if r == "mouth" and "--cut-mouth" in a:
                continue
            fail("没挑到「%s」件 —— 用 --pick/--drop 调整，或看上面的「保留/丢弃」清单" % r)
    for r in picked:
        if r not in parts:
            print("  · 「%s」不在 --parts 里，稍后丢弃" % r)
    for ob in [o for o in meshes if o not in sum(picked.values(), [])]:
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.view_layer.update()

    # ---------- 1.2) 清理飞出去的碎片（战无2 源模型的通病，见 prune_far 注释） ----------
    #   必须跑在【切嘴之前】：嘴位是靠脸壳包围盒估的，包围盒被碎片撑歪就全废。
    if "--no-prune" not in a:
        kk = float(get(a, "--prune-k", "4.0"))
        for role, objs in list(picked.items()):
            for ob in objs:
                d = prune_far(ob, kk)
                if d:
                    print("  清碎片：%s 丢掉 %d/%d 个顶点（重心离主体 > %.1f×主体半径）"
                          % (ob.name, d, len(ob.data.vertices) + d, kk))

    # ---------- 1.3) 按骨骼剔掉非头部的碎片（复合件；见 keep_head_only 注释） ----------
    if "--no-head-only" not in a:
        _arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        _fr = re.compile(r"^bone_(4[6-9]|5[0-9]|6[0-2])$")
        _hk = float(get(a, "--head-k", "1.6"))    # 清理半径系数：越小越狠（复合件重的角色调小）
        _gk = float(get(a, "--head-gate", "4.0"))  # 「确实复合」闸门系数：越小越容易触发清理
        if _arm is not None:
            # 簇心/半径由【脸壳】算一次（面部骨族权重最高的那块），全体共用 —— 见 head_cluster 注释
            _cl, _best = None, -1.0
            for _ob in picked.get("face", []):
                _c2 = head_cluster(_ob, _arm, _fr)
                if _c2 is None:
                    continue
                _w = sum(g.weight for v in _ob.data.vertices for g in v.groups
                         if _fr.match(_ob.vertex_groups[g.group].name))
                if _w > _best:
                    _best, _cl = _w, _c2
            for _role in ("face", "eye", "mouth"):
                for _ob in picked.get(_role, []):
                    _d = keep_head_only(_ob, _arm, _fr, _cl, _hk, _gk)
                    if _d:
                        print("  剔非头部：%s 丢掉 %d/%d 个顶点（主导骨离面部骨族中心过远）"
                              % (_ob.name, _d, len(_ob.data.vertices) + _d))

    # ---------- 1.5) --cut-mouth：源模型没有「嘴」件时，从脸壳上切出唇周 ----------
    #   🔴 必须跑在【归并之前】：脸壳和头发常常是两块，并起来之后包围盒会被头发拉高，
    #      切嘴的估算基准（脸高）跟着变大 → 嘴位估偏 → 眼↔嘴距离偏大 → **缩放算错，头小一圈**
    #      （实测信长：正确脸高 34.1，并了头发变 42.5，缩放从 0.01144 掉到 0.00976）。
    #   为什么要有这一步：标定要用「眼↔嘴」的竖直距离算缩放，而战无2 的源模型只有
    #   脸/眼/发——嘴是脸壳上的一块。信长当年是手工切的（|x|<=3.2, z∈[170.6,174.4], y<0），
    #   27 个角色不能手工切，所以按几何自动切。
    #   切盒的三条比例都是从信长那次实测反推的（见 Knowledge/战国无双换装工程.md §7.1）：
    #     嘴中心  = 眼球中心往下 0.20 × 脸壳高度（眼 179.33 / 嘴 172.5 / 脸高 34.1 → 6.83/34.1）
    #     宽度    = 0.175 × 脸宽（信长 |x|<=3.2，脸宽 17.87 → 3.2/17.87 = 0.179）
    #     半高    = 0.056 × 脸高（信长 z 跨度 3.8 / 34.1 = 0.111 → 半高 0.056）
    # 🔴 只认【面部骨族】（bone_46~bone_62）—— 战无2 全角色共用同一套面部骨架
    #    （实测信长/幸村逐根只差一个常数）。用来①选哪块是脸壳 ②定嘴位。
    FACE_BONE = re.compile(r"^bone_(4[6-9]|5[0-9]|6[0-2])$")
    if "--cut-mouth" in a and "mouth" not in picked:
        eyev0 = center(picked["eye"])
        # 脸件可能有多块（脸壳 + 头发 + 兜）——选【面部骨族权重占比最高】的那一块当脸壳。
        #  🔴 别用「包围盒中心离眼球最近」：岛津义弘的脸件含双臂把中心拉低，而头发件
        #     正好盖在眼上、中心更近 → 会选错件（实测嘴位算到 183、一个面都选不中）。
        #     面部骨族占比这条与我们已验证的「脸 = bone_46 占比最高」是同一条规则。
        def _face_share(o):
            tot_ = 0.0
            tot_face = 0.0
            for v in o.data.vertices:
                for g in v.groups:
                    nm = o.vertex_groups[g.group].name
                    tot_ += g.weight
                    if FACE_BONE.match(nm):
                        tot_face += g.weight
            return tot_face / tot_ if tot_ > 0 else 0.0
        fac = max(picked["face"], key=_face_share)
        lo, hi = bbox([fac])
        fh, fw = hi.z - lo.z, hi.x - lo.x

        # 嘴中心估计 —— 🔴 优先用【面部骨族】当锚点，不要用「脸壳包围盒高度 × 0.20」。
        #   为什么换：那条比例是从信长一个人反推的，而脸壳包围盒随角色差很多
        #   （信长脸壳高 34.1，幸村只有 20.4）→ 幸村那次嘴位估偏一半，缩放算错一倍（0.0203 vs 0.0114）。
        #   为什么骨骼可以跨角色：实测信长与幸村的面部骨族（bone_46~60）**逐根只差一个常数 5.9**
        #   —— 同一套面部骨架，相对位置完全一致。所以「眼睛往下 0.83 ×（眼睛到最低面部骨）」
        #   是可迁移的（信长实测：眼 179.33、最低面部骨 170.95、真嘴中心 172.38 → 0.829）。
        arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        jawz = None
        if arm is not None:
            wsum = {}
            for v in fac.data.vertices:
                for g in v.groups:
                    nm = fac.vertex_groups[g.group].name
                    wsum[nm] = wsum.get(nm, 0.0) + g.weight
            tot = sum(wsum.values()) or 1.0
            # （FACE_BONE 过滤见上面：不加的话脸件里连着兜帽的角色会把
            #   头骨→颈骨→脊柱骨一路走通，嘴位算到胸口）
            zs = []
            for nm, w in wsum.items():
                if w / tot < 0.03:          # 忽略零星权重
                    continue
                if not FACE_BONE.match(nm):
                    continue
                b = arm.data.bones.get(nm)
                if b is not None:
                    zs.append((arm.matrix_world @ b.head_local).z)
            # 🔴 沿脸部骨链【从眼睛往下走，断开就停】——不要用"脸壳包围盒内"当范围：
            #    脸件上常连着兜帽/披风（服部半藏、岛津义弘那种），它们的骨在胸口高度，
            #    但只要用包围盒当前界就会混进来，嘴位算到胸口 → 一个面都选不中。
            #    脸部骨族在空间上是连续的（实测信长：182.4/181.7/179.7/…/171.0/170.9），
            #    而兜帽骨离最近的面部骨差 20+ → 用「3×中位间距」当断链阈值，自动分开。
            if len(zs) >= 2:
                zs.sort(reverse=True)
                gaps = sorted(zs[i] - zs[i + 1] for i in range(len(zs) - 1))
                med_gap = gaps[len(gaps) // 2]
                i0 = min(range(len(zs)), key=lambda i: abs(zs[i] - eyev0.z))
                run = [zs[i0]]
                for i in range(i0 + 1, len(zs)):
                    if zs[i - 1] - zs[i] > 3.0 * med_gap:
                        break
                    run.append(zs[i])
                low = [z for z in run if z < eyev0.z]
                if low:
                    jawz = min(low)
        if jawz is not None and (eyev0.z - jawz) > 1e-6:
            mz = eyev0.z - 0.83 * (eyev0.z - jawz)
            how = "面部骨锚点（眼 %.2f − 最低面部骨 %.2f）" % (eyev0.z, jawz)
        else:
            mz = eyev0.z - 0.20 * fh        # 兜底：没有骨架时退回旧比例
            how = "脸壳高度比例（兜底：没找到合格的面部骨——该件可能是复合件）"

        ymid = (lo.y + hi.y) / 2.0
        for o in bpy.data.objects:
            o.select_set(False)
        bpy.context.view_layer.objects.active = fac
        fac.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bm = bmesh.from_edit_mesh(fac.data)
        bm.faces.ensure_lookup_table()
        sel = 0
        for f in bm.faces:
            c = f.calc_center_median()
            ok = (abs(c.x) <= 0.175 * fw and abs(c.z - mz) <= 0.056 * fh and c.y <= ymid)
            f.select_set(ok)
            sel += 1 if ok else 0
        bmesh.update_edit_mesh(fac.data)
        if sel == 0:
            bpy.ops.object.mode_set(mode='OBJECT')
            fail("--cut-mouth 一个面都没选中（脸壳 %s bbox z=%.3f~%.3f，估计嘴中心 z=%.3f）—— "
                 "源模型可能本来就有嘴件，或脸壳朝向与预期不同" % (fac.name, lo.z, hi.z, mz))
        before = {o.name for o in bpy.data.objects}
        bpy.ops.mesh.separate(type='SELECTED')
        bpy.ops.object.mode_set(mode='OBJECT')
        new = [o for o in bpy.data.objects if o.name not in before]
        if not new:
            fail("--cut-mouth：separate 之后没拿到新对象")
        mouth_ob = new[0]
        mouth_ob.name = fac.name.rsplit(".", 1)[0] + ".mouth"
        picked["mouth"] = [mouth_ob]
        print("  切嘴：从 %s 选中 %d 面（%s → 嘴中心 z=%.3f）"
              % (fac.name, sel, how, mz))
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
                rmax = rr - RIM_BURY
            elif v.co.z < rz + RIM_BAND:
                rmax = rr - RIM_BURY + (v.co.z - rz) * RIM_SLOPE
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
        # 🔴 必须 copy 一份再改名（2026-09-14 修）：源模型常常**整身共用一张材质**
        #    （战国无双2 的 L02 就是：全身 12 个子网格同指 `mat_L02_nobunaga`）。
        #    直接 `materials[0].name = mat_name` 改的是**那个共享 datablock** —— 三个件轮流改名，
        #    最后只剩最后一个名字（实测：三件全是 `head_nobunaga_a_mouth`），编辑器里就是一个材质。
        #    蒂法/萨菲罗斯的源恰好每件自带材质，所以这个坑一直没暴露。
        if ob.data.materials:
            m = ob.data.materials[0].copy()
            m.name = mat_name
            ob.data.materials[0] = m
            while len(ob.data.materials) > 1:
                ob.data.materials.pop(index=len(ob.data.materials) - 1)
        else:
            ob.data.materials.append(bpy.data.materials.new(mat_name))
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

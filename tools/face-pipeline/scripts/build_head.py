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
#       [--pick "face=body.cut,eye=Eyeballs,mouth=mouth"]   # 显式挑件（.blend 常用；给了就不走关键字分类）
#                                                          # 关键字前缀 `=` = 整名精确匹配（见 pick_match）。\
#                                                          # 例："face==m_head_henry"（KCD：子串会连睫毛一起捞进来）\
#       [--cut-z 1.4144]                                    # 变换后按目标空间 z 裁掉下半（去胸/领口）
#       [--drop "hair,body,dress,..."] [--scale auto|<k>] [--no-skeleton] [--list-only]
#
# 输出：按 --parts 给序命名 `<name>.0/.1/.2…`，材质同名（脸=裸名，其余加 `_<role>` 后缀），
#       已刚性绑定官方骨架 `bip01_head_13`，导出规格 = USF 100 / UpAxis 2 / 节点零变换。
import bpy
import json, bmesh, sys, os, re, math, mathutils
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

# 🔴 抠脖子要排除的**头骨族**（与 tools/armor-pipeline/scripts/build_armor.py 的 `HEAD_SW` 同一口径）：
#   bone_10/11（头/颈）+ 面部骨 bone_46..62。**戴在头上的东西**（兜/头巾/面罩）主导骨都落在这族里，
#   而脖子皮肤的主导骨是 **bone_9**（胸/颈族）—— 所以按主导骨能把「兜」和「脖子」分开。
#   2026-09-17 用户裁定：抠脖子时整块丢掉这族碎片（原因见 carve_neck_part 的 ⑦）。
NECK_EXCL_BONES = set(["bone_10", "bone_11"] + ["bone_%d" % i for i in range(46, 63)])

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
    # 🔴 女表 = 2026-09-16 现测（`Debug/offline/_rim_table.py` 跑 `body_female_a.fbx` 的自由边环，
    #    口径与男表相同）。实测三点与 `Knowledge/蒂法换头工程.md` §20.2 记录一致：
    #    正前 z=1.4066 / 肩侧 z=1.5059 / 背后 z=1.4734。
    #    ⚠️ 0°/15°/165°/180° 四档原始测量混进了**斜方肌**（15° 量到 r=0.16），已按邻档趋势手工压平；
    #    60/120/255/285 四档原测量没有自由边，取邻档插值。
    "female": [(0.0, 0.1091, 1.4921), (15.0, 0.1020, 1.4880), (30.0, 0.0952, 1.4813),
               (45.0, 0.0895, 1.4643), (60.0, 0.0900, 1.4520), (75.0, 0.0914, 1.4399),
               (90.0, 0.1006, 1.4066), (105.0, 0.0913, 1.4399), (120.0, 0.0900, 1.4520),
               (135.0, 0.0895, 1.4643), (150.0, 0.0952, 1.4813), (165.0, 0.1020, 1.4880),
               (180.0, 0.1091, 1.4921), (195.0, 0.1142, 1.5059), (210.0, 0.1203, 1.5009),
               (225.0, 0.1163, 1.4752), (240.0, 0.0901, 1.4737), (255.0, 0.0820, 1.4735),
               (270.0, 0.0781, 1.4734), (285.0, 0.0820, 1.4735), (300.0, 0.0901, 1.4737),
               (315.0, 0.1163, 1.4752), (330.0, 0.1203, 1.5010), (345.0, 0.1142, 1.5059)],
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
    """Blender FBX 导入器的内存级补丁（不落盘）—— 源模型各踩过一个坑，两道都要打。

    ① 原版头 / 战无2 源的 TWT morph 缺 FullWeights → 导入器断言崩溃。
       范本来自 build_neck.py / _probe_vanilla_head.py。
    ② 🔴 KCD（3ds Max 导出）的**武器/盾等件蒙皮到的骨头不在骨架子树下**（在
       `RightWeaponRoot` 那一支）→ 导入器给它们建的登记表键是 `None`，之后按骨架对象
       去查就抛 `KeyError: None @ link_hierarchy` —— **整个文件导不进来**。
       修法照抄 `tools/sw2-pipeline/scripts/identify_parts.py` 的同一道（两处保持一字不差）。
       ⚠️ 两道补丁都只在该崩的情况下兜底，正常文件一行都不变；
          `inspect.getsource` 读的是**磁盘原文**（补丁只在内存），所以重复调用是幂等的。
    """
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    orig = src
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
    bad2 = "                    (mmat, amat) = mesh.armature_setup[self]"
    if bad2 in src:
        src = src.replace(bad2, (
            "                    if self not in mesh.armature_setup:\n"
            "                        print('[IMPATCH] no armature_setup: mesh=%s arm=%s keys=%s'\n"
            "                              % (mesh.fbx_name, getattr(self, 'fbx_name', '?'),\n"
            "                                 [getattr(k, 'fbx_name', k) for k in mesh.armature_setup]))\n"
            "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
            + bad2))
    elif "armature_setup[self]" in src:
        print("[IMPATCH] ⚠️ 没匹配到 armature_setup 那行（Blender 导入器源码变了？）"
              "—— KCD 一类源模型会导不进来")
    if src != orig:
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


def pick_match(name, keys):
    """`--pick` 的关键字匹配。
    · 关键字以 `=` 开头 → **整名精确匹配**（`=m_head_henry` 只命中 `m_head_henry`）
    · 否则 → 子串匹配（老行为，战无2/蒂法/萨菲罗斯的配方一字未改）
    🔴 为什么要精确匹配（2026-09-19）：KCD 亨利的 `m_head_henry` 是 `m_head_henry_Teeth` /
       `_Eyelashes` / `_Eyeshadows` / `_Tearline` 的**子串** —— 子串匹配会把睫毛/眼影/泪线
       一起并进脸壳，而男头只有 3 个子网格位（脸/眼/嘴），多出来的件会回落到脸皮材质 → 眼睛嘴全糊。
    写法：`--pick "face==m_head_henry,mouth==m_head_henry_Teeth"`（role 与关键字之间多一个 `=`）。
    """
    n = name.lower()
    for k in keys:
        k = k.strip().lower()
        if k.startswith("="):
            if n == k[1:]:
                return True
        elif k and k in n:
            return True
    return False


def keep_neck_frag(ob, r_max, z0):
    """【从身体件里只抠出脖子】—— 战无2 把脖子画在身体件里，脸壳件只到下巴下面一点。

    判据（源坐标）分两步：
      ① **选碎片**：连通域「最宽 ≤ r_max」且「最高 ≥ z0」= 细而伸到头部的一条 = 脖子。
         雑賀实测 submesh_0：脖子 = 两块 bone_9 碎片（33 顶点 / z 129~171 / |x| ≤ 9.7）
         同件的肩甲 |x|=20、手臂 |x|=93、腿裙 z=47~113 —— 都被排除。
      ② **裁高度**：选中碎片里 z < z0 的顶点**删掉**（留下一个开口筒）。
         🔴 必须有这一步：源模型的脖子一直画到胸口（z=129），不裁的话头会拖出一条
         0.5 米长的脖子**穿进身体**。原版头的脖子底大约在目标空间 z=1.44（= 眼下方 0.24 米），
         身体领口正好盖住它。`neck_z0` 就是"目标 z=1.44"换算回源坐标的值。
    🔴 为什么不用骨骼判：脖子和胸/肩共用 bone_9，按骨分不开。
    返回留下的顶点数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    mw = ob.matrix_world
    keep = set()
    seen = [False] * len(bm.verts)
    for v in bm.verts:
        if seen[v.index]:
            continue
        st, comp = [v], []
        seen[v.index] = True
        while st:
            cur = st.pop()
            comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    st.append(o2)
        pts = [mw @ bm.verts[i].co for i in comp]
        xm = max(abs(q.x) for q in pts)
        zm = max(q.z for q in pts)
        if xm <= r_max and zm >= z0:
            keep.update(comp)
    kill = [v for v in bm.verts
            if v.index not in keep or (mw @ v.co).z < z0]      # ② 裁高度
    n = len(bm.verts) - len(kill)
    if n and kill:
        bmesh.ops.delete(bm, geom=kill, context='VERTS')
        bm.to_mesh(me)
        me.update()
    elif not n:
        bm.free()
        return 0
    bm.free()
    return n


def _uv_mean_color(ob, loops, img, w, h, px, maxn=400):
    """取一个对象若干 loop 的 UV 在源图集上的平均色（RGBA→RGB，0~1）。"""
    if img is None or not loops:
        return None
    step = max(1, len(loops) // maxn)
    uvl = ob.data.uv_layers.active
    if uvl is None:
        return None
    acc = [0.0, 0.0, 0.0]
    n = 0
    for li in loops[::step]:
        u, v = uvl.data[li].uv
        x = min(w - 1, max(0, int((u % 1.0) * w)))
        y = min(h - 1, max(0, int((v % 1.0) * h)))          # Blender 的像素是自下而上存的
        i = (y * w + x) * 4
        acc[0] += px[i]; acc[1] += px[i + 1]; acc[2] += px[i + 2]
        n += 1
    if not n:
        return None
    return (acc[0] / n, acc[1] / n, acc[2] / n)


def neck_uv_to_skin(neck_ob, face_ob, atlas):
    """把**脖子件的 UV 全部重设到「脸壳上的肤色点」**（2026-09-17 用户裁定）。

    为什么：脖子件的 UV 是从源模型原样搬来的，实测跨度 u[0.002,0.955]（脸壳只到 0.53）——
    跨到了图集**非头区**（右半边是身体/衣服），采样均值偏暗；而且它的材质是独立名
    `<头名>_neck`，编辑器工程里**没有这个材质资源**（材质是首次导入时按当时的件数建的）
    → 编辑器用默认白材质渲染 = 实机外看到的「脖子白板」。
    于是：① 材质名改用脸壳的（见步骤 4）② UV 全部落到脸壳的肤色点（本函数）。

    取点口径与 carve 的 ⑤ 参照肤色**同一套**：脸壳 z∈[1.56,1.65]（颧骨/鼻梁带）的 UV 采样均值 = 参照色，
    再在脸壳里挑**最接近参照色且最亮**的那个顶点，用它一个 UV 铺满脖子件（单一肤色点）。

    为什么用「单一点」而不是逐顶点吸最近肤色点：后者会把贴图上鼻子/眼睛的纹素也带进来
    （颅骨填充那轮实测：从背后看是一张「脸」的图案）。脖子是简单柱面，单点最稳。
    """
    if neck_ob is None or face_ob is None or not neck_ob.data.uv_layers:
        return False
    if not (atlas and os.path.isfile(atlas)) or not face_ob.data.uv_layers:
        return False
    try:
        img = bpy.data.images.load(atlas, check_existing=True)
        w, h = img.size
        px = img.pixels[:]
    except Exception:
        return False

    def _col(u, v):
        x = min(w - 1, max(0, int((u % 1.0) * w)))
        y = min(h - 1, max(0, int((v % 1.0) * h)))
        i = (y * w + x) * 4
        return (px[i], px[i + 1], px[i + 2])

    fu = face_ob.data.uv_layers.active
    band, allv = [], []
    for li, lp in enumerate(face_ob.data.loops):
        p = face_ob.matrix_world @ face_ob.data.vertices[lp.vertex_index].co
        u, v = fu.data[li].uv
        c = _col(u, v)
        allv.append((c, (u, v)))
        if 1.56 <= p.z <= 1.65:
            band.append(c)
    if not allv:
        return False
    if band:
        ref = tuple(sum(c[i] for c in band) / len(band) for i in range(3))
    else:
        ref = tuple(sum(c[i] for c, _ in allv) / len(allv) for i in range(3))
    # 最接近参照色（容差内）里挑最亮的
    cand = [t for t in allv if max(abs(t[0][i] - ref[i]) for i in range(3)) <= 0.07]
    if not cand:
        cand = allv
    best = max(cand, key=lambda t: 0.30 * t[0][0] + 0.59 * t[0][1] + 0.11 * t[0][2])
    for d in neck_ob.data.uv_layers.active.data:
        d.uv = best[1]
    print("  脖子 UV → 肤色点 (%.3f, %.3f)  参照色 (%.3f, %.3f, %.3f)  候选 %d/%d"
          % (best[1][0], best[1][1], ref[0], ref[1], ref[2], len(cand), len(allv)))
    return True


def frag_dominant_bone(ob, comp):
    """一个碎片的**主导骨** = 全片权重求和后最大的那条骨。
       都是「连通域 + 权重求和取最大」，不是「逐顶点取最大组再投票」——两套算法在混合权重处
       会给出不同的名字，用错就在头/甲两侧对不上账。返回 None = 该片一个顶点组都没有。
    """
    tot = {}
    for vi in comp:
        for g in ob.data.vertices[vi].groups:
            nm = ob.vertex_groups[g.group].name
            tot[nm] = tot.get(nm, 0.0) + g.weight
    return max(tot.items(), key=lambda kv: kv[1])[0] if tot else None


def carve_neck_part(objs, s, z_sole, r_max=0.10, y_max=0.13, z_top=1.49,
                    n_sect=6, z_cut=1.41, tag="neck", atlas=None, face_objs=(),
                    skin_tol=0.16, excl_head=True):
    """【从身体/甲件里抠出脖子那一段】作为头的**独立 part**（2026-09-16 用户裁定「脖子归头」）。

    为什么要有这一步：战无2 把**脖子画在身体件里**，脸壳件只画到下巴下面一点（底下是断口）。
    实机表现 = 穿甲时颈圈整圈停在甲领口上方、中间那段谁都没有几何 → 「脖子悬空」
    （2026-09-16 用户报，28 人全中）。原来的补法是在脸壳上人工铺一圈"脖子管"（--neck-fill /
    --neck-tube-to），那是权宜；治本是**把源模型自己的脖子捡起来**——它和源模型的甲领口
    本来就是配套的，同一个 T 切出来天然对得上（「拼得上」从补丁变成构造上成立）。

    🔴 不许把这段并进脸壳件：它的 UV 在**身体图集**上，脸壳的材质按脸图集采样 → 会花。
       所以单独一件、单独材质（`<头名>_neck`），UV 保持原样（战无2 是**一张全身图集**，
       脸/眼/嘴/脖子本来就在同一张图上，贴图不用另裁，见 make_sw2_textures.py）。

    判据（全部在 **T 空间**量，米）——六条，缺一不可：
      ① 薄：碎片 max|x| ≤ r_max；② 不深：max|y| ≤ y_max（挡掉前后垂下的衣片）
      ③ 够高：max z ≥ z_top（伸到下颌附近）
      ④ **绕轴有角向覆盖**：≥ n_sect 个 24 分扇区（平贴的衣服片只占 1~2 个）
      ⚠️ **①②③④ 的阈值必须与 `tools/sw2-pipeline/check_assembly.py` 里调本函数时传的一致**
      （闸门要复现同一套选取），改这里就要同步改那里 —— 优先做法是**两边都不传、用默认**。
      🔴 **⑤ 的参照色锚点 = 脸壳「颧骨/鼻梁带」（T 空间 z ∈ [1.56,1.65]），这是试出来的**：
      锚「下巴一带」会被**胡子/覆面**污染、锚「手部」会被**籠手**污染（实测信长/半藏的手部
      参照色 ≈ (0.10,0.10,0.10) 近黑，反而把真脖子判成"非肤色"）；颧骨/鼻梁带是唯一胡子
      长不到、面罩盖不住的位置。两个兜底顺序：手部 → 下巴带。
      ⚠️ **不是每个人都有脖子件**（脸壳自带颈部的角色抠不出来，属正常；实测 28 人里 6 人有）。
      ⑤ 的容差可按角色覆盖（`skin_tol`），标定依据见 `plans/战国无双换装批量落地.md` §10.3/§10.8。
      ⑤ 🔴 **肤色判据**：碎片 UV 在源图集上的平均色必须与**脸壳下巴一带**的平均色接近
         （容差 skin_tol）。**为什么必须有这条**：实测宁宁的几何判据选中的是**甲领口那个
         金铜色箍**（源 submesh_1 的领圈，45 顶点、|x|≤0.072、11 扇区 —— 几何上完全像脖子），
         它若进头资产就会与甲自己的领口**重复**、且不穿甲时露出一圈金箍。
         绝对肤色阈值挡不住金铜色（也是 R>G>B），所以参照色取**脸自己**。
      ⑥ **底切在「原版身体领口线」上**（z_cut ≈ 1.41）：只留露在身体外的部分。
         留多了会与**原版身体自己的皮**打架（两块皮贴在同一位置 = z-fighting，实测宁宁
         躯干皮肤碎片一直延伸到 z 1.235）；留少了则在领口上方留缝。
    然后 z_cut 以下全删（藏进甲/身体里）。
      ⑦ **主导骨排除**（`excl_head`，默认开）：碎片的**主导骨 ∈ bone_10/11 ∪ bone_46..62**
         （= 甲侧 `HEAD_SW` 那一族）→ 整块丢掉。**为什么必须有这条**：脖子件是按**连通域**抠的，
         戴在头上的东西（兜/头巾/面罩）主导骨是 **bone_11**（头骨），几何上又细又绕轴（|x| 小、
         绕满扇区），①②③④ 全部通过、⑤ 肤色判据也拦不住（白布/金饰的 RGB 也满足容差）——
         实测谦信 39 / 秀吉 68 / 半藏 31 个顶点**与兜资产完全重合**（渲图实锤：藏掉 `_neck`
         件，兜的顶刺/双角跟着消失），两边都穿上 = 同深度打架（2026-09-17）。
         **为什么按骨分得开**：脖子皮肤绑 **bone_9**（胸/颈共用），兜绑 **bone_11**（头）——
         这两族不重叠（老注释里"按骨分不开"说的是 `bone_9` vs `bone_10`，那是脖子与肩膀）。

    ⚠️ 判据**不按骨骼挑「是不是头」**：实测宁宁/信长/兰丸的脖子皮肤**全部绑 bone_9**（胸/颈共用，
    bone_10 权重是 0）—— ⑦ 用的是**反向**判据（是头骨族就丢），它不负责认出脖子。

    返回 (新网格对象或 None, [(源对象, [被抠走的顶点下标])])。
    """
    # ---- 参照色：脸壳【下巴一带】（z_T ∈ [1.45, 1.62]）的 UV 平均色
    # ---- 参照色：**手部**（纯肤色、无毛发/面罩）----
    #   🔴 2026-09-17 改：原来取「脸壳下巴一带」，被**胡子（信长）/覆面（半藏）**污染 → 把真脖子
    #      也一起拒掉（实测：把 --neck-atlas 指到不存在的路径 = 关掉肤色判据，信长/兰丸立刻
    #      各抠出 26 顶点）。手是纯肤色，且源模型一定带手部件（普查里那批「+去手」）。
    #   兜底：一个手部顶点都找不到时，退回原来的「脸壳下巴一带」。
    HAND_BONES = set(["bone_18", "bone_19"] + ["bone_%d" % i for i in
                     (26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45)])
    img = w = h = px = None
    ref = None
    ref_src = "手部"
    if atlas and os.path.isfile(atlas):
        try:
            img = bpy.data.images.load(atlas, check_existing=True)
            w, h = img.size
            px = img.pixels[:]
            # 🔴 参照色必须**逐件**算再平均：loop 下标只对**它自己那个对象**的 UV 层有效，
            #    把多个件的 loop 下标混在一个对象上查会越界
            #    （实测光秀/归蝶：`bpy_prop_collection[index]: index 2033 out of range, size 2028`）。
            # 首选：脸壳的**颧骨/鼻梁带**（T 空间 z ∈ [1.56,1.65]）—— 胡子长不到、
            # 面罩通常也只盖下半脸，比"下巴一带"和"手部"都干净（实测：下巴带被信长的胡子污染、
            # 手部被**籠手**污染成近黑色 0.10；而颧骨带取到的是真肤色）。
            def _nose_band(_fo, _vi):
                _z = (_fo.matrix_world @ _fo.data.vertices[_vi].co).z
                return 1.56 <= (_z - z_sole) * s <= 1.65

            def _gather(objs_, pick):
                out_ = []
                for _fo in objs_:
                    if _fo.data.uv_layers.active is None:
                        continue
                    _loops = []
                    for _p in _fo.data.polygons:
                        for _li in _p.loop_indices:
                            if pick(_fo, _fo.data.loops[_li].vertex_index):
                                _loops.append(_li)
                    _c = _uv_mean_color(_fo, _loops, img, w, h, px)
                    if _c:
                        out_.append((_c, len(_loops)))
                return out_

            def _is_hand(_fo, _vi):
                _v = _fo.data.vertices[_vi]
                if not _v.groups:
                    return False
                _g = max(_v.groups, key=lambda x: x.weight)
                return _fo.vertex_groups[_g.group].name in HAND_BONES
            _per = _gather(face_objs, _nose_band)
            if _per:
                ref_src = "脸壳颧骨/鼻梁带"
            else:                              # 兜底 1：手部（纯肤色，但**有籠手/手甲的角色会被污染**）
                _per = _gather(list(objs) + list(face_objs), _is_hand)
                ref_src = "手部（兜底：颧骨带没取到）"
            if not _per:                       # 兜底 2：脸壳下巴一带
                def _z_band(_fo, _vi):
                    _z = (_fo.matrix_world @ _fo.data.vertices[_vi].co).z
                    return 1.45 <= (_z - z_sole) * s <= 1.62
                _per = _gather(face_objs, _z_band)
                ref_src = "脸壳下巴一带（兜底 2）"
            if _per:
                _tot = sum(n for _, n in _per)
                ref = tuple(sum(c[i] * n for c, n in _per) / _tot for i in range(3))
            print("  抠脖子：参照肤色（%s %d 件 / %d 个 loop）= %s"
                  % (ref_src, len(_per), sum(n for _, n in _per),
                     tuple(round(c, 3) for c in ref) if ref else "取不到"))
        except Exception as _e:                                        # noqa
            print("  ⚠️ 抠脖子：读源图集失败（%s）→ 肤色判据跳过" % _e)
            ref = None

    picked_v = []          # [(源对象, [顶点下标])]
    for ob in objs:
        me = ob.data
        mw = ob.matrix_world

        def T(p):
            return Vector((p.x * s, -p.y * s, (p.z - z_sole) * s))

        # 连通域（碎片）
        n = len(me.vertices)
        par = list(range(n))

        def find(x):
            while par[x] != x:
                par[x] = par[par[x]]
                x = par[x]
            return x
        for e in me.edges:
            a, b = find(e.vertices[0]), find(e.vertices[1])
            if a != b:
                par[a] = b
        grp = {}
        for vi in range(n):
            grp.setdefault(find(vi), []).append(vi)

        keep = set()
        for vs in grp.values():
            q = [T(mw @ me.vertices[vi].co) for vi in vs]
            xm = max(abs(p.x) for p in q)
            ym = max(abs(p.y) for p in q)
            z1 = max(p.z for p in q)
            if xm > r_max or ym > y_max or z1 < z_top:
                continue
            sect = set()
            for p in q:
                sect.add(int((math.degrees(math.atan2(p.y, p.x)) % 360.0) // 15.0) % 24)
            _why = ""
            if len(sect) < n_sect:
                _why = "扇区 %d<%d" % (len(sect), n_sect)
            # ⑦ 主导骨排除：戴在头上的东西（兜/头巾/面罩）主导骨是头骨族 → 整块丢掉
            #    （必须先算，别等到肤色判据之后：白布/金饰也能过肤色容差，实测踩过）
            _dom = frag_dominant_bone(ob, vs) if excl_head else None
            if not _why and _dom in NECK_EXCL_BONES:
                _why = "主导骨 %s ∈ 头骨族（兜/头巾，不是脖子）" % _dom
            # ⑤ 肤色判据：与脸同色才算皮肤（挡掉甲领口/衣领那种"几何上像脖子"的东西）
            col = None
            if not _why and ref is not None and img is not None:
                _vs = set(vs)
                # 取「有任何顶点落在这块碎片里」的面的 UV —— 下标只对本对象有效（同上）
                _loops = [li for _p in me.polygons if any(v in _vs for v in _p.vertices)
                          for li in _p.loop_indices]
                col = _uv_mean_color(ob, _loops, img, w, h, px)
                if col is not None:
                    _d = max(abs(col[i] - ref[i]) for i in range(3))
                    _why = "" if _d <= skin_tol else ("肤色差 %.2f>%.2f（%s vs 参照 %s）"
                                                      % (_d, skin_tol,
                                                         tuple(round(c, 2) for c in col),
                                                         tuple(round(c, 2) for c in ref)))
            if not _why:
                # ⑥ 只在「原版身体领口线」以上留（以下的藏进身体，留着会与身体自己的皮 z-fighting）
                for vi in vs:
                    if T(mw @ me.vertices[vi].co).z >= z_cut:
                        keep.add(vi)
            # 候选级诊断（判据①~③已过、被④⑤否掉的都打出来）—— 排查"这个人为什么没脖子"看这行
            print("      · 候选 %s n=%-4d |x|=%.3f |y|=%.3f z1=%.3f 扇区%-3d %s"
                  % (ob.name.split("_")[-1][:6], len(vs), xm, ym, z1, len(sect),
                     "取" if not _why else ("丢：" + _why)))
        if keep:
            picked_v.append((ob, keep))
        print("  抠脖子：%s 命中 %d 顶点（|x|≤%.3f |y|≤%.3f 顶≥%.2f 扇区≥%d 底切 %.2f%s）"
              % (ob.name, len(keep), r_max, y_max, z_top, n_sect, z_cut,
                 "，肤色 ✓" if (ref is not None and keep) else
                 ("，⚠️ 肤色判据不可用" if ref is None else "")))

    if not picked_v:
        print("  ⚠️ 抠脖子：一块都没命中 —— 检查 --neck-src 序号与判据")
        return None, []
    return _neck_make(picked_v, tag), picked_v


def carve_skin_region(objs, s, z_sole, r_max, z0, z1, skin=True, atlas=None,
                      face_objs=(), skin_tol=0.16, tag="neck_src", ref_override=None,
                      bones=None, ratio=None):
    """【区域裁剪】从**大片段**里逐顶点裁出「脖子/胸口那片皮肤」（2026-09-17 晚新增）。

    为什么必须有它（用户实机实锤两例）：
      · 稻姬：那片胸口皮**和长发同一连通域**；小太郎：那块领口/皮肤**连着锁子甲肩片**
        （138 顶点、z 1.097~1.600）。`carve_neck_part` 的判据是**整块连通域取舍** ——
        要么连甲/头发一起搬进来、要么整块不要，拿不到"只要里面那一小块"。
    本通道改成**逐顶点**：
      · **留**：位置落在「近轴圆柱（水平半径 ≤ r_max）+ z ∈ [z0, z1]」内，且
        （`skin=1` 时）该顶点 UV 的平均色与脸壳参照色差 ≤ `skin_tol`；
      · **面**：**顶点全留才留**（不留半边面 = 不出破洞）；只保留被保留面用到的顶点（不留孤立点）；
      · **不再做 z_cut**（z0 就是底切）；**UV 原样保留**（那片皮本来就在皮肤贴图上，
        塌点会拍成一块平色 —— 调用方负责不要在后面再调 neck_uv_to_skin）。
    与甲侧配套：同一区域由 `build_armor.py --skin-drop-region` 从**甲**里剔掉，
    两头共用 parts_table 里同一行的一份参数（防"分界不一致"）。
    """
    ref = None
    img = w = h = px = None
    if ref_override:
        # 🔴 逐人显式参照色（`--skin-ref-color "r,g,b"`）：脸壳颧骨带被**头发**污染的角色
        #    （实测稻姬：她那带长发 → 取到暗色 → 真皮全被拒）。值写在 parts_table 那一行。
        ref = tuple(ref_override)
        print("  区域裁剪：参照肤色 = **显式指定** %s" % (tuple(round(c, 3) for c in ref),))
    if atlas and os.path.isfile(atlas) and skin:
        try:
            img = bpy.data.images.load(atlas, check_existing=True)   # 🔴 图集总是要读（逐顶点采样要用）
            w, h = img.size
            px = img.pixels[:]
            _per = [] if not ref_override else None
            if _per is None:
                raise StopIteration
            for _fo in face_objs:
                if _fo.data.uv_layers.active is None:
                    continue
                _ls = [li for _p in _fo.data.polygons for li in _p.loop_indices
                       if 1.56 <= (((_fo.matrix_world @ _fo.data.vertices[
                           _fo.data.loops[li].vertex_index].co).z - z_sole) * s) <= 1.65]
                _c = _uv_mean_color(_fo, _ls, img, w, h, px)
                if _c:
                    _per.append((_c, max(1, len(_ls))))
            if _per:
                _tot = sum(n for _, n in _per)
                ref = tuple(sum(c[i] * n for c, n in _per) / _tot for i in range(3))
                print("  区域裁剪：参照肤色（脸壳颧骨/鼻梁带 %d 件）= %s"
                      % (len(_per), tuple(round(c, 3) for c in ref)))
        except StopIteration:
            pass
        except Exception as _e:                                        # noqa
            print("  ⚠️ 区域裁剪：读源图集失败（%s）→ 肤色判据跳过" % _e)
            ref = None
    picked_v = []
    for ob in objs:
        me = ob.data
        mw = ob.matrix_world
        uvl = me.uv_layers.active
        vloops = {}
        for _p in me.polygons:
            for li in _p.loop_indices:
                vloops.setdefault(me.loops[li].vertex_index, []).append(li)
        ok = set()
        for vi in range(len(me.vertices)):
            p = mw @ me.vertices[vi].co
            q = Vector((p.x * s, -p.y * s, (p.z - z_sole) * s))
            if math.hypot(q.x, q.y) > r_max or not (z0 <= q.z <= z1):
                continue
            if bones:      # 🔴 主导骨白名单（不给 = 不管骨骼）：只有脖子/胸那一族才算"脖子那块"
                #    —— 区域是圆柱，肩甲片也落在里面（实测小太郎：肩甲被一起收进头）。
                #    衣领/脖子绑 bone_9/10（胸颈族），肩甲绑手臂骨（12~19）→ 按主导骨分得开。
                _g = me.vertices[vi].groups
                if not _g:
                    continue
                _bn = ob.vertex_groups[max(_g, key=lambda x: x.weight).group].name
                if _bn not in bones:
                    continue
            if ref is not None and uvl is not None:
                # 🔴 逐**loop** 判（不是"所有相邻面的平均色"）：一个顶点会粘着好几张面
                #    （皮肤面 + 衣服面），取平均会把真皮也拉出容差（实测稻姬：平均口径 0 命中）。
                #    改成「只要粘到**任一张**肤色面就算」——顶点落在皮肤面上就留。
                _hit_skin = False
                for li in (vloops.get(vi) or [])[:16]:
                    u, v = uvl.data[li].uv
                    xx = min(w - 1, max(0, int((u % 1.0) * w)))
                    yy = min(h - 1, max(0, int((v % 1.0) * h)))
                    ii = (yy * w + xx) * 4
                    if ratio:
                        # 🔴 色比口径（2026-09-17 晚加）：皮肤 R/G≈1.36 / 灰布 R/G≈1.06 ——
                        #    "通道最大差 ≤ 容差"分不开这两类（实测只差 0.10，被 0.16 容差吞掉）。
                        #    判据：R >= rg·G 且 R >= rb·B 才算皮肤（不看绝对明暗）。
                        _c = (px[ii], px[ii + 1], px[ii + 2])
                        if _c[0] >= ratio[0] * _c[1] and _c[0] >= ratio[1] * _c[2]:
                            _hit_skin = True
                            break
                    elif max(abs(px[ii + j] - ref[j]) for j in range(3)) <= skin_tol:
                        _hit_skin = True
                        break
                if not _hit_skin:
                    continue
            ok.add(vi)
        keep = set()
        for _p in me.polygons:
            if all(v in ok for v in _p.vertices):
                keep.update(_p.vertices)
        print("  区域裁剪：%s —— 圆柱内 %d 顶点 → 成面保留 %d 顶点（%d 面）%s"
              % (ob.name, len(ok), len(keep),
                 sum(1 for _p in me.polygons if all(v in keep for v in _p.vertices)),
                 "" if ref is not None else "（⚠️ 未过肤色）"))
        if keep:
            picked_v.append((ob, keep))
    if not picked_v:
        print("  ⚠️ 区域裁剪：一块都没命中 —— 检查 --skin-region 的 r/z 与肤色判据")
        return None, []
    return _neck_make(picked_v, tag), picked_v


def _neck_make(picked_v, tag):

    # 合成一个新网格：顶点 + UV + 顶点组（源骨名，后面会被原版头的权重替换）
    co, faces, uvs, vgs = [], [], [], {}
    for ob, keep in picked_v:
        me = ob.data
        base = len(co)
        remap = {}
        uvl = me.uv_layers.active
        gname = [g.name for g in ob.vertex_groups]
        for vi in sorted(keep):
            remap[vi] = base + len(remap)
            co.append(me.vertices[vi].co.copy())
            for g in me.vertices[vi].groups:
                if g.weight > 1e-4:
                    vgs.setdefault(gname[g.group], {})[base + len(remap) - 1] = g.weight
        for poly in me.polygons:
            vv = [remap[v] for v in poly.vertices if v in remap]
            if len(vv) == len(poly.vertices):
                faces.append(vv)
                if uvl is not None:
                    uvs.append([tuple(uvl.data[li].uv) for li in poly.loop_indices])
    nob = bpy.data.meshes.new(tag)
    nob.from_pydata(co, [], faces)
    nob.update()
    # 🔴 平滑着色（2026-09-17 晚加）：这件是**新建的网格**（从源件摘顶点重建），
    #    `from_pydata` 出来的面默认 `use_smooth=False` = **逐面法线** → 导出到实机就是
    #    "硬棱大三角"（用户实机截图：脖子上一块明显多边形的板）。
    #    源模型那片皮本身是**平滑着色**的曲面，所以这里必须跟着平滑，否则形状对、观感错。
    for _p in nob.polygons:
        _p.use_smooth = True
    obj = bpy.data.objects.new(tag, nob)
    bpy.context.scene.collection.objects.link(obj)
    if uvs:
        ul = nob.uv_layers.new(name="UVMap")
        k = 0
        for poly in nob.polygons:
            for li in poly.loop_indices:
                ul.data[li].uv = uvs[k][li - poly.loop_start]
            k += 1
    for bn, d in vgs.items():
        g = obj.vertex_groups.new(name=bn)
        for vi, w in d.items():
            g.add([vi], min(1.0, w), 'REPLACE')
    print("  抠脖子：合成 %s —— %d 顶点 %d 面 %d 个顶点组"
          % (tag, len(co), len(faces), len(obj.vertex_groups)))
    return obj


def prune_far(ob, k=4.0, arm=None, keep_bones=("bone_10", "bone_11")):
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
    kbone = set(keep_bones or ())
    for comp in comps:
        # 🔴 头骨上的碎片一律留（2026-09-15 深夜）：战无2 的「脸壳件」里常混着**整头头发**
        #    （脸 + 发同一块），头发离脸远得很，正好落进这条"离群"判据 → 被当飞出去的碎片清掉。
        #    实测风魔小太郎：脸壳件 submesh_2 在这里被删 **152/595 顶点**，他的头发就是这么没的。
        #    「绑在头骨上的东西就是头的一部分」是确定的事实，不需要靠距离去猜。
        if arm is not None and kbone:
            w = {}
            for i in comp:
                for g in ob.data.vertices[i].groups:
                    nm = ob.vertex_groups[g.group].name
                    w[nm] = w.get(nm, 0.0) + g.weight
            if w and max(w, key=w.get) in kbone:
                keep.update(comp)
                continue
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


def keep_head_only(ob, arm, face_re, cluster=None, radius_k=1.6, gate_k=4.0,
                   keep_bones=("bone_10", "bone_11")):
    """【按骨骼剔掉非头部的碎片】—— 复合件（脸件里连着兜帽/外套/手臂）的解法。

    战无2 的骨名是 `bone_N`（纯数字、无语义），但**有位置**：头部各骨挤在头上，
    脊柱/手臂/腿的骨在下面。所以判据 = **顶点的主导骨离"面部骨族中心"多远**：
      · 取该件用到的面部骨族（bone_46~bone_62），算它们的中心 c 与最大半径 r
      · 保留「主导骨位置在 c 的 radius_k×r 之内」的顶点，其余连面一起删

    🔴 别用 z 阈值：头发绑的是**头骨 bone_11，它不在面部骨族里**，一刀切会把整个头发削掉
       （实测信长那次削掉 349/387 个顶点，头直接变秃 + 后续切嘴失败）。空间距离则天然包含它。

    实测（28 人）：不做这步有 18 个人的头是坏的 —— 归蝶那个包围盒从 z=−0.389 到 1.873、宽 1.78 米。

    🔴 2026-09-15 深夜两处修改（用户报"头发残缺/半张脸消失"，视觉验收实锤）：
      · **逐【碎片】判，不再逐顶点判** —— 源模型是"碎片云"（不焊顶点），逐顶点删会在
        一整片头发中间开出洞（光秀实测：头发碎成互不相连的板条）。改成整域留/整域删。
        归蝶那验证过：bone_11 的域全留、躯干四肢的域全删，干净。
      · **`keep_bones` 里的骨，其碎片一律留**（默认 = 头骨 bone_10/bone_11）。
        半藏实测：他的**布头罩整个绑 bone_11**，而 bone_11 距面部骨簇心 **12.1**、阈值 **11.8**
        —— 只差 0.3 就被判成"非头部"，111 个顶点全削掉，脸壳只剩 202 面（用户报"半张脸消失"）。
        🔴 别用「把阈值整体放宽」来治（试过：把 bone_10/11 并进簇 → 簇半径 7→15、阈值 11→25）
        —— 治好了半藏，却把訚千代的**籠手**、阿市的**肩衣**、宁宁的甲片一起放回头上（实测出图）。
        「绑在头骨上的东西就是头的一部分」是确定的事实，不需要靠距离去猜。
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
    # 删除阈值用哪个簇：默认与闸门同簇（面部骨簇）
    kc, kr = cluster
    lim = max(radius_k * kr, 1e-6)
    kbone = set(keep_bones or ())
    bone_pos = {}
    for nm in wsum:
        b = arm.data.bones.get(nm)
        if b is not None:
            bone_pos[nm] = arm.matrix_world @ b.head_local
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    # ---- 连通域（碎片）：整域留 / 整域删 ----
    seen = [False] * len(bm.verts)
    kill = []
    protected = 0
    for v in bm.verts:
        if seen[v.index]:
            continue
        st, comp = [v], []
        seen[v.index] = True
        while st:
            cur = st.pop()
            comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    st.append(o2)
        # 该域的主导骨 = 域内【合计权重】最大的骨（不是逐顶点投票 —— 大顶点的那根说了算）
        wdom = {}
        for i in comp:
            for g in me.vertices[i].groups:
                nm = ob.vertex_groups[g.group].name
                wdom[nm] = wdom.get(nm, 0.0) + g.weight
        best = max(wdom, key=wdom.get) if wdom else None
        if best in kbone:
            protected += 1
            continue                       # 🔴 绑在头骨上的碎片 = 头的一部分，一律留（见 docstring）
        pp = bone_pos.get(best)
        if pp is not None and (pp - kc).length > lim:
            kill.extend(bm.verts[i] for i in comp)
    n = len(kill)
    # 🔴 安全网：一刀切掉 ≥90% 说明这条判据不适合这块件（前田庆次的长发就是这样被判成"杂质"
    #    全削光的 → 空网格 → 导出时多出一个没名字的对象 → transfer_channels 崩）。
    #    宁可不清，也不能清光。
    # 🔴 但**有头骨保护碎片时跳过安全网**（2026-09-15 深夜）：安全网是给"距离判据不可靠"设的，
    #    而"绑在头骨上就是头"是确定的事实，不该被一个比例阈值挡掉。
    #    实测稻姬的发绳件 idx4：302 顶点里 **只有 16 个绑 bone_11（发绳）**，其余 286 是手臂+手 ——
    #    判据想删 184/200（92% ≥ 90%）→ 整块跳过 → **手被一起搬到头上了**（用户实机截图）。
    #    同一件里 idx5：129 顶点里 36 个是额环，其余肩甲，同样被挡住。
    if protected == 0 and n >= 0.9 * len(bm.verts):
        print("  [keep-head] %s：判据要削掉 %d/%d（≥90%%）→ 判为误伤，跳过"
              % (ob.name, n, len(bm.verts)))
        bm.free()
        return 0
    if protected:
        print("  [keep-head] %s：头骨保护 %d 块碎片，按骨骼照删 %d/%d"
              % (ob.name, protected, n, len(bm.verts)))
    if n:
        bmesh.ops.delete(bm, geom=kill, context='VERTS')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return n


def drop_frag_by_seed(ob, seeds, max_d=12.0):
    """【按碎片重心最近】整块删掉 —— 用来把"头盔颏带"从脸壳件里摘出去。

    🔴 为什么需要（2026-09-15）：战无2 里**戴盔的角色**，把头盔的**颏带**画在了【脸壳】这一块里
       （不是兜件里；实测家康的兜 submesh_4 里没有带子）。原模型里它被兜的吹返挡着看不见，
       换头只取"头" → 带子就横在脸上（用户实机截图：下巴到耳朵一条编织带）。
    🔴 判据不能用盒子：带子碎片和第 3 号"嘴部"碎片的包围盒**互相重叠**（盒选会连带选错），
       而按【碎片重心】选是干净的 —— 种子点由人眼在渲染图上定（每块料单独渲出来看过）。
    返回删掉的顶点数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    comps = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        st = [v]
        seen[v.index] = True
        c = []
        while st:
            cur = st.pop()
            c.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    st.append(o2)
        comps.append(c)
    kill = set()
    for sd in seeds:
        sv = Vector(sd)
        best, bd = None, 1e18
        for c in comps:
            ce = Vector((0, 0, 0))
            for i in c:
                ce += bm.verts[i].co
            ce /= len(c)
            d = (ce - sv).length
            if d < bd:
                bd, best = d, c
        if best is not None and bd <= max_d:
            kill.update(best)
        else:
            print("    ⚠️ 颏带种子 (%.1f,%.1f,%.1f) 附近没有碎片（最近 %.2f），跳过" % (sd[0], sd[1], sd[2], bd))
    if kill:
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in sorted(kill)], context='VERTS')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return len(kill)


def strap_uv_seed_idx(ob, seeds, max_d=12.0):
    """种子点 → 带子碎片的顶点索引（不删，只用来算 UV 包围盒）。"""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    comps = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        st = [v]
        seen[v.index] = True
        c = []
        while st:
            cur = st.pop()
            c.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    st.append(o2)
        comps.append(c)
    out = []
    for sd in seeds:
        sv = Vector(sd)
        best, bd = None, 1e18
        for c in comps:
            ce = Vector((0, 0, 0))
            for i in c:
                ce += bm.verts[i].co
            ce /= len(c)
            d = (ce - sv).length
            if d < bd:
                bd, best = d, c
        if best is not None and bd <= max_d:
            out += best
    bm.free()
    return out


def retarget_uv_region(ob, bones, skin_bone="bone_46", extra_idx=()):
    """把【主导骨属于 bones】的顶点所带的 loop，UV 逐个改成【离该顶点最近的肤色顶点】的 UV。

    🔴 为什么不删几何（2026-09-15 用户实机两轮反馈）：
       颏带的**下巴那一横条就是下颌皮面本身**（美术把带子纹理画在下颌上，不是贴上去的独立条）。
       删 ⇒ 下颌镂空（用户："你把下巴都镂空了"）；删完补洞 ⇒ 补不干净。
       ⇒ 只能改 UV，几何一个不动。
    🔴 为什么逐 loop 取最近点的 UV、不整片一个色：整片一个色 ⇒ 下颌一块死板色块，和周围皮肤接不上
       （用户："皮肤出现较多不自然情况"）。逐个取最近肤色顶点的 UV ⇒ UV 跟着邻居走，纹理自然过渡。
    🔴 定位只用【骨骼】，不用 UV 区域：实测下巴条的 UV 跨度覆盖大半个图集，按 UV 区域会误伤整张脸
       （试过：改了 976 个 loop = 半张脸）。
    返回改动的 loop 数。
    """
    me = ob.data
    uvl = me.uv_layers.active
    if uvl is None or (not bones and not extra_idx):
        return 0
    extra = set(extra_idx)
    dom_of = []
    for v in me.vertices:
        best, bw = None, -1.0
        for g in v.groups:
            if g.weight > bw:
                bw, best = g.weight, ob.vertex_groups[g.group].name
        dom_of.append(best)
    # 🔴 候选只取【目标附近 NEAR_R 以内的肤色顶点】—— 取"全脸最近的"会跨 UV 缝挑到后脑/头顶，
    #    那些地方的 UV 落在图集的占位区（棋盘格），改完脸上会出现一块棋盘（实测踩到）。
    NEAR_R = 3.0
    skin = []
    for poly in me.polygons:
        for k, vi in enumerate(poly.vertices):
            if dom_of[vi] == skin_bone:
                skin.append((me.vertices[vi].co.copy(), uvl.data[poly.loop_start + k].uv.copy()))
    if not skin:
        return 0
    n = 0
    for poly in me.polygons:
        for k, vi in enumerate(poly.vertices):
            if dom_of[vi] not in bones and vi not in extra:
                continue
            co = me.vertices[vi].co
            best, bd = None, 1e18
            for sco, suv in skin:
                d = (co - sco).length_squared
                if d < bd:
                    bd, best = d, suv
            if best is not None and bd <= NEAR_R * NEAR_R:
                uvl.data[poly.loop_start + k].uv = best
                n += 1
    me.update()
    return n


def strap_uv_box_bone(ob, bones):
    """从【主导骨属于 bones 的顶点】算出 UV 包围盒（下巴那条带子用这个）。"""
    me = ob.data
    uvl = me.uv_layers.active
    if uvl is None or not bones:
        return None
    us, vs = [], []
    for poly in me.polygons:
        for k, vi in enumerate(poly.vertices):
            v = me.vertices[vi]
            best, bw = None, -1.0
            for g in v.groups:
                if g.weight > bw:
                    bw, best = g.weight, ob.vertex_groups[g.group].name
            if best in bones:
                uv = uvl.data[poly.loop_start + k].uv
                us.append(uv.x)
                vs.append(uv.y)
    if not us:
        return None
    return (min(us), max(us), min(vs), max(vs))


def strap_uv_box(ob, frags):
    """从【已知的带子碎片顶点索引】算出它在图集里的 UV 包围盒。"""
    me = ob.data
    uvl = me.uv_layers.active
    if uvl is None or not frags:
        return None
    fs = set(frags)
    us, vs = [], []
    for poly in me.polygons:
        for k, vi in enumerate(poly.vertices):
            if vi in fs:
                uv = uvl.data[poly.loop_start + k].uv
                us.append(uv.x)
                vs.append(uv.y)
    if not us:
        return None
    return (min(us), max(us), min(vs), max(vs))


def rim_lookup(gender):
    """RIM_TABLE 的插值器：角度（度）→ (领口半径, 领口高度)。
    没有该性别的轮廓时返回 None（调用方自己兜底）。"""
    tab = RIM_TABLE.get(gender)
    if not tab:
        return None

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
    return rim_at


def fill_neck_to_rim(ob, rim_at, z_top=1.545, r_max=0.14, k=1.0, bury=RIM_BURY,
                     flat_z=None, grow_max=1.25, tube_to=None):
    """【补脖子下摆】—— 头网格的脖子够不到身体领口时，从脖子的自由边往下铺一圈"下摆"。

    🔴 为什么需要（2026-09-16 用户实机报「所有人颈部都没有贴合肩部，往上抬了一点」）：
       骑砍2 的身体**只有一个大 V 领口**，「脖子 + 胸兜」整块**是头网格给的**
       （见 RIM_TABLE 上面那段注释）。原版头因此一直长到世界 z=1.4144（V 领口最低处），
       而**战无2 的脸壳件只到下巴下面一点点**（实测：雑賀 12.2 源单位 = 最短，
       信長 19.2；换算到目标空间都比原版头**短 5cm 左右**，目标空间里脖子底下是断的）。
       实机表现 = 头看着像"往上抬了一点、架在肩上"，领口一圈能看见里面的空腔。

    做法：取脸壳**颈部区域里的自由边**（只挂 1 个面的边 —— 就是脖子那个断口），
       每一条自由边 (a,b) 与它在领口轮廓上的落点 (a',b') 桥成一个四边形，
       得到一片从脖子铺到领口内沿的「下摆」。
       · 落点 = `RIM_TABLE` 的 (半径×k − 埋深, z) —— 与 3c 的 `--fit-rim` **同一条轮廓**：
         3c 是把宽出来的肩膀**收进去**，这一步是把缺的脖子**铺下去**，两者互补。
       · UV 沿用上方顶点的（脖子那一段是近似均匀的肤色，拉伸看不出来）。
       · 下摆铺到口沿以下 → 下端藏在身体/甲里面，外面只看得到"脖子接到领口"。

    `flat_z` 给了就改成**竖直下摆**（落点 z 一律 = flat_z、半径沿用原轮廓半径）——
       用于领口轮廓不适用的情况（战无2 的甲是立领和服，领口比原版 V 领高）。

    🔴 `tube_to`（2026-09-16 晚加，实机「脖子悬空」的正解）：**从落点环再直着往下拉一圈"脖子管"**
       伸到 z=tube_to。为什么光有下摆不够：
       · `RIM_TABLE` 是**原版身体**的 V 领轮廓 —— 正前低(1.4066)、两侧高(1.5059)。
         所以下摆在前侧能把脖子拉下来 6cm，**在两侧等于没做**（落点本来就贴着原版领口）。
       · 而我们的人穿的是战无2 的甲：甲的**领口上沿两侧只有 1.415**（比原版领口低 9cm）
         → 两侧脖子在 1.502 就断了，底下 9cm 谁都没有几何 = **露空腔 = 脖子悬空**。
       · 管子半径取落点半径（≈脖子自己的半径，`grow_max` 已经卡过），所以它是**插进甲领口里面**，
         不是罩在甲外面；不穿甲时又整段藏在身体里（正前 z<1.4066 处身体是闭合的）。
       · 走竖直而不是跟着甲的轮廓：甲是按角色换的，头不能对某一件甲写死。

    返回新建的面数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    uvl = bm.loops.layers.uv.active

    def in_neck(v):
        c = v.co
        return c.z < z_top and math.hypot(c.x, c.y) < r_max

    edges = [e for e in bm.edges if len(e.link_faces) == 1
             and in_neck(e.verts[0]) and in_neck(e.verts[1])]
    if not edges:
        bm.free()
        return 0

    nv = {}
    for e in edges:
        for v in e.verts:
            if v in nv:
                continue
            c = v.co
            ang = math.atan2(c.y, c.x)
            rr, rz = rim_at(math.degrees(ang))
            rt = max((rr - bury) * k, 1e-4)
            # 🔴 半径上限：不许把下摆往外撑过原半径的 grow_max 倍。
            #    原因：RIM_TABLE 是**原版身体**的领口，而我们的人穿的是战无2 的甲（立领和服），
            #    两者的领口轮廓并不重合 —— 不设上限时，正前方那一段会顶到甲领口外面去。
            rt = min(rt, max(math.hypot(c.x, c.y), 1e-4) * grow_max)
            zt = flat_z if flat_z is not None else rz
            nv[v] = bm.verts.new((rt * math.cos(ang), rt * math.sin(ang), zt))
    bm.verts.ensure_lookup_table()

    pairs = []
    for e in edges:
        f0 = e.link_faces[0]
        lp = next((l for l in f0.loops if l.edge is e), None)
        if lp is None:
            continue
        a = lp.vert
        b = e.other_vert(a)
        if a in nv and b in nv:
            pairs.append((a, b, f0))

    made = 0
    land_uv = {}                      # 落点顶点 -> 它继承到的 UV（给下面那圈管子用）
    for a, b, f0 in pairs:
        na, nb = nv[a], nv[b]
        # 🔴 绕序：必须与 f0 **反向**走共用边 (a,b)（f0 走 a→b），新面才和脖子同朝向。
        #    2026-09-16 实测：原来的 (a, b, nb, na) 是同向 → 整片下摆法线朝里
        #    （z<1.40 段 外1/里13），而骑砍材质是单面的 → 实机里这一片根本看不见。
        try:
            nf = bm.faces.new((na, nb, b, a))
        except ValueError:
            continue
        made += 1
        if uvl is not None:
            src = {}
            for l in f0.loops:
                src[l.vert] = l[uvl].uv.copy()
            for l in nf.loops:
                u = src.get(l.vert)
                if u is None:                 # 新顶点：沿用它在老边上的那一端
                    u = src.get(a if l.vert is na else b)
                if u is not None:
                    l[uvl].uv = u
                    land_uv[l.vert] = u       # 落点顶点继承到的 UV，管子照抄

    # ---------- 第二段：从落点环直着往下拉"脖子管"（伸进甲领口） ----------
    if tube_to is not None and made:
        lo = {}
        for v, nvp in nv.items():
            lo[v] = bm.verts.new((nvp.co.x, nvp.co.y, float(tube_to)))
        bm.verts.ensure_lookup_table()
        for a, b, _f0 in pairs:
            na, nb = nv[a], nv[b]
            # 与下摆面在共用边 (na,nb) 上反向：下摆走 na→nb，管子走 nb→na
            try:
                nf = bm.faces.new((nb, na, lo[a], lo[b]))
            except ValueError:
                continue
            made += 1
            if uvl is not None:
                for l in nf.loops:
                    u = land_uv.get(na if l.vert in (na, lo[a]) else nb)
                    if u is not None:
                        l[uvl].uv = u

    if made:
        bm.to_mesh(me)
        me.update()
    bm.free()
    return made


def close_neck_slit(ob, z_top=1.56, r_max=0.13, lim=0.020):
    """【闭脖子正中的竖缝】—— 战无2 的脸是**前后两片壳**，在脖子正前方根本没接上。

    实测（宁宁，2026-09-16 晚）：两片壳的底边在正中相距 **17mm**（右壳 x=+0.0071 / 左壳 x=-0.0102），
    各自往下补出来的下摆/脖子管之间就一直留着一道竖缝 —— 实机表现 = 脖子正面一条黑缝
    （用户 2026-09-16 报的"接缝"）。

    做法：取颈部一带（z < z_top、r < r_max）的**边界顶点**，按距离贪心配对（<lim、且不共边），
    每对都移到中点再焊。环上相邻点的间距实测 ~30mm > lim，所以不会误焊正常环。
    返回焊掉的顶点对数。
    """
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()

    def region(v):
        return v.co.z < z_top and math.hypot(v.co.x, v.co.y) < r_max

    # 🔴 第一趟必须先焊**重合点**：两片壳在颈部一带各自带一份 0.0mm 的重复点，
    #    贪心配对会被这些 0mm 对吃光名额，跨缝的 ~18mm 对就永远轮不到（2026-09-16 实测踩到）。
    r0 = [v for v in bm.verts if region(v)]
    if r0:
        bmesh.ops.remove_doubles(bm, verts=r0, dist=1e-4)
    bm.verts.ensure_lookup_table()

    def is_bnd(v):
        return any(len(e.link_faces) == 1 for e in v.link_edges)

    vs = [v for v in bm.verts if region(v) and is_bnd(v)]
    cand = []
    for i in range(len(vs)):
        for j in range(i + 1, len(vs)):
            a, b = vs[i], vs[j]
            if any(e for e in a.link_edges if e.other_vert(a) is b):
                continue                      # 共边 = 同一条折线上的邻居，别焊
            # 🔴 只配**跨正中线**的点对（x 异号）：这条缝就是"左右两片壳在正前没接上"，
            #    不设这条，同侧相隔 15mm 的点会先把名额占掉，跨缝的反而配不上（实测踩到）。
            if a.co.x * b.co.x >= 0 or abs(a.co.x) > 0.045 or abs(b.co.x) > 0.045:
                continue
            d = (a.co - b.co).length
            if d <= lim:
                cand.append((d, a, b))
    cand.sort(key=lambda t: t[0])
    print("  闭正中缝：颈部边界顶点 %d 个，≤%.0fmm 候选对 %d（最近 %s）"
          % (len(vs), lim * 1000, len(cand),
             "%.1fmm" % (cand[0][0] * 1000) if cand else "-"))
    used, pairs = set(), []
    for d, a, b in cand:
        if a in used or b in used:
            continue
        used.add(a); used.add(b)
        pairs.append((a, b))
    if pairs:
        for a, b in pairs:
            mid = (a.co + b.co) / 2.0
            a.co = mid
            b.co = mid
        verts = []
        for pr in pairs:
            for v in pr:
                if v not in verts:
                    verts.append(v)
        bmesh.ops.remove_doubles(bm, verts=verts, dist=1e-4)
        bm.to_mesh(ob.data)
        ob.data.update()
    bm.free()
    return len(pairs)


def flip_inward_neck_faces(ob, z_top=1.56, r_max=0.14, twin_tol=3e-4):
    """【把颈部区法线朝里的面翻过来】—— 实机"脖子正面一条黑缝"的正解。

    实测（宁宁，2026-09-16 晚，用**洋红背景**渲图判定）：脖子正前方那条黑带**不是洞**
    （洞会露背景色，实测不露），而是 13 个**法线朝里**的面：它们填在前后两片壳之间的缝里，
    法线反了 → 离线渲成黑的；实机单面材质下被背剔 → 露内腔。翻过来即可，几何一点不动。

    判据：面中心在颈部区域（z < z_top、r < r_max）且 `法线·径向 < 0`。
    返回翻转的面数。

    🔴 **孪生面豁免（2026-09-17 补，实锤）**：`make_sheets_double_sided`（薄片补背面 1.7）
       会把单面板**复制一份并翻面** —— 副本**天生朝内，是设计如此**（引擎材质没有双面开关，
       只能靠几何补）。所以「朝内的面」里有一大类根本不是缺陷。
       **判据**：面心 0.3mm 内有另一个面且 `法线·法线 < -0.5`（反平行）= 一对双面副本 → **跳过不翻**。
       🔴 **不加这道守卫的实测后果**（宁宁，2026-09-17）：副本被翻正 → 一对「一正一反」变成
       **两片同向重合**（实测 6 组 `n·n = +1.000`，z 1.543~1.551）→ **z-fighting**，
       且薄片**丢掉背面**（从另一侧看直接透过去）—— 把 1.7「补背面」的成果就地毁掉。
    """
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()

    # 孪生面索引（KDTree：面心 → 面序号）
    cents = [f.calc_center_median() for f in bm.faces]
    kd = kdtree.KDTree(len(cents))
    for i, c in enumerate(cents):
        kd.insert(c, i)
    kd.balance()

    def has_antiparallel_twin(f, c):
        for _co, idx, _d in kd.find_range(c, twin_tol):
            g = bm.faces[idx]
            if g is f:
                continue
            if f.normal.dot(g.normal) < -0.5:
                return True
        return False

    n = 0
    skipped = 0
    for f in bm.faces:
        c = f.calc_center_median()
        if c.z > z_top or math.hypot(c.x, c.y) > r_max:
            continue
        if f.normal.x * c.x + f.normal.y * c.y < 0:
            if has_antiparallel_twin(f, c):
                skipped += 1
                continue
            f.normal_flip()
            n += 1
    if n:
        bm.to_mesh(ob.data)
        ob.data.update()
    bm.free()
    return n, skipped


def make_sheets_double_sided(ob, open_ratio=0.5):
    """【把薄片复制一份并翻面】—— 单面板在引擎里背面被剔除，从另一侧看就是"没有"。

    🔴 为什么必须做（2026-09-15 深夜，用户实机发现）：战无2 的**布条/发带/飘带/头绳**
       大量是**单个平面**（没有厚度）。而 Bannerlord **材质层没有双面开关** ——
       `TaleWorlds.Engine.Material.MBMaterialShaderFlags` 全部 21 个标志里
       **没有** TwoSided / NoCull 之类（已反编译核对），所以只能靠几何补：
       复制一份、把面绕序翻过来。
       症状：头发上那条布条从一侧看得见，转到另一侧就没了。

    判据用「边界边比例」区分**薄片**和**壳**：
      · 平面/布条：绝大多数边只挂 1 个面 → 比例接近 1 → 复制
      · 脸壳：只有脖子那一圈是边界 → 比例很低 → **不复制**
        （复制了面数翻倍、两个面还互相打架）
    返回复制的面数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen = [False] * len(bm.faces)
    dup = []
    for f0 in bm.faces:
        if seen[f0.index]:
            continue
        stack, comp = [f0], []
        seen[f0.index] = True
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf is not cur and not seen[nf.index]:
                        seen[nf.index] = True
                        stack.append(nf)
        be = sum(1 for c in comp for e in c.edges if len(e.link_faces) == 1)
        te = sum(len(c.edges) for c in comp)
        if te and be / float(te) >= open_ratio:
            dup.extend(comp)
    n = len(dup)
    if n:
        geom = (list({v for f in dup for v in f.verts})
                + list({e for f in dup for e in f.edges}) + dup)
        ret = bmesh.ops.duplicate(bm, geom=geom)
        newf = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.reverse_faces(bm, faces=newf)
        bm.to_mesh(me)
        me.update()
    bm.free()
    return n


def seal_open_bottom(ob, min_gap=1.0, atlas=None):
    """【把开口的底边竖直补到该件最低点】—— 用于底边带方形缺口的发块。

    🔴 为什么需要（2026-09-15，光秀）：战无2 有些角色的**发块底边天生是一个方口**
       （光秀：源 z157~161 一段空着），这个口子在原模型里由**和服立领**挡着，所以看不出来。
       但换头只取「头」——领子是身上的衣服，不该进头网格 → 头上就露出一条方形缺口，
       剪影从 16cm 突然缩到 3cm，看着像"头发被横切了一刀"。
       实测数据：缺口 = 世界 z 1.554~1.609，领子顶边 = 1.6038（源 157.8 × 标定 0.013）。

    做法：只动**【边界边】**（只有一个面的边）。把高于「最低点 + min_gap」的边界顶点，
       在它正下方最低点处复制一个点，与相邻边桥成四边形。UV 沿用上方顶点的 ——
       深色头发上等于把发丝纹理往下拉一小段，肉眼可接受。

    🔴 阈值 min_gap 的作用：正常发梢的底边是一条**平滑曲线**（最低点和相邻点只差几毫米），
       不会被压平；只有真正的"方口"（高出 1cm 以上）才会被补上。
    返回新增的面数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bnd = [e for e in bm.edges if len(e.link_faces) == 1]
    if not bnd:
        bm.free()
        return 0
    zmin = min(v.co.z for v in bm.verts)
    zfloor = zmin + min_gap
    up = [v for v in bm.verts if v.co.z > zfloor and any(e in bnd for e in v.link_edges)]
    if not up:
        bm.free()
        return 0
    uv_lay = bm.loops.layers.uv.active
    # 🔴 补出来的面的 UV 必须落在【头发区】—— 踩过两次：
    #    ① 沿用边界顶点自己的 UV → 边界 UV 常落在图集的亮区（脸/衣服），补的面成了亮带；
    #    ② 取包围盒中心最近的顶点 → 那个点常在头壳【内表面】，UV 也可能是肤色区 → 正面一大块肉色。
    #    现在改为：**拿图集采样，在边界顶点里挑最暗的那个 UV**（头发是近黑，最暗 = 一定有头发）。
    uv_src = None
    if uv_lay:
        cands = []
        for e in bnd:
            for l in e.link_loops:
                if l.vert.co.z > zfloor:
                    cands.append(l[uv_lay].uv.copy())
        if atlas and os.path.isfile(atlas) and cands:
            try:
                img = bpy.data.images.load(atlas)
                w, h = img.size
                px = list(img.pixels)
                def lum(u):
                    x = min(w - 1, max(0, int(u.x % 1.0 * w)))
                    y = min(h - 1, max(0, int(u.y % 1.0 * h)))
                    i = (y * w + x) * 4
                    return 0.299 * px[i] + 0.587 * px[i + 1] + 0.114 * px[i + 2]
                cands.sort(key=lum)
                uv_src = cands[len(cands) // 8] if len(cands) >= 8 else cands[0]   # 取最暗的 1/8 里偏上的那个
                print("    封底 UV：图集采样取最暗（%d 个候选）" % len(cands))
            except Exception as ex:
                print("    ⚠️ 图集采样失败（%s）→ 退回边界 UV" % ex)
        if uv_src is None and cands:
            uv_src = cands[0]
    down = {}
    for v in up:
        down[v] = bm.verts.new((v.co.x, v.co.y, zmin))
    bm.verts.ensure_lookup_table()
    made = 0
    for e in bnd:
        v1, v2 = e.verts
        a1, a2 = down.get(v1), down.get(v2)
        if a1 is None and a2 is None:
            continue
        if a1 is None:                      # 只一端高：连到它自己的正下方
            a1 = down[v1] = bm.verts.new((v1.co.x, v1.co.y, zmin))
        if a2 is None:
            a2 = down[v2] = bm.verts.new((v2.co.x, v2.co.y, zmin))
        try:
            f = bm.faces.new((v1, v2, a2, a1))
        except ValueError:
            continue
        if uv_lay:
            for l in f.loops:
                l[uv_lay].uv = uv_src.copy() if uv_src is not None else mathutils.Vector((0.0, 0.0))
        made += 1
    if made:
        bm.to_mesh(me)
        me.update()
    bm.free()
    return made


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
    # --seal-bottom：这几件（同样按对象名排序行号）跑「封发块底口」——底边有方形缺口时补上
    seal_idx = [int(x) for x in (get(a, "--seal-bottom", "") or "").split(",") if x.strip().isdigit()]
    # --strap-seed：这几颗种子点所在的【碎片】从脸壳里整块摘掉（头盔颏带，见 drop_frag_by_seed）
    strap_bones = set(x.strip() for x in (get(a, "--strap-bone", "") or "").split(",") if x.strip())
    strap_seeds = []
    for _s in (get(a, "--strap-seed", "") or "").split(";"):
        _s = _s.strip()
        if _s:
            strap_seeds.append(Vector([float(v) for v in _s.split(",")]))
    cut_spec = get(a, "--cut-z")                     # "1.4144"（只裁 face）或 "face=1.4144,mouth=1.3"

    # ---------- 0) 源变换 T（2026-09-16，三件共用一把尺；见 src_transform.py） ----------
    # 🔴 给了 --t-s 就走 T 模式：定向/缩放/落位全部改成「Y 轴镜像 + 等比缩放（源原点=地面）」，
    #    不再用下面的「偏航 + 眼↔嘴标定 + 眼球送眼位」。理由：三件（头/甲/兜）必须共用同一份变换，
    #    否则拼不回原角色（实测宁宁脖子比甲领口高 8.7cm）。
    #    没给 = 老行为（蒂法/萨菲罗斯那条线一行不变）。
    t_s = float(get(a, "--t-s", "0") or 0)
    t_zsole = float(get(a, "--t-z-sole", "0") or 0)
    t_mode = t_s > 0

    # ---------- 1) 导入源模型（FBX 或 .blend） + 挑件 ----------
    # 🔴 先打导入器补丁再导源模型（2026-09-19）：KCD 源**必须**这道补丁才导得进来
    #    （缺则 `KeyError: None @ link_hierarchy` 直接崩）。以前只在导 --weights-from 时打，
    #    对蒂法/萨菲罗斯那种"只有 morph 断言"的源够用，对 KCD 不够。
    patch_importer()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if src.lower().endswith(".blend"):
        bpy.ops.wm.open_mainfile(filepath=src)
    else:
        bpy.ops.import_scene.fbx(filepath=src)
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("导入 %d 个网格（源类型 %s）" % (len(meshes), "blend" if src.lower().endswith(".blend") else "fbx"))
    # 源骨架的头骨（bone_11）世界 z —— T 自检要用：第 2 步末尾会把源骨架整个删掉（换官方骨架），
    # 所以在这里先记下来（s 的定义就是「让头骨落到原版 1.569」，落不上说明 --t-s 传错了）。
    _src_arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    src_head_z = None
    if _src_arm is not None and _src_arm.data.bones.get("bone_11") is not None:
        src_head_z = (_src_arm.matrix_world @ _src_arm.data.bones["bone_11"].head_local).z

    picked = {}
    seal_targets = []
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
            # 封底目标：按与 --pick-idx 相同的行号取件（封底跑在 1.3 剔头之后，只补留下来的净发块）
            if seal_idx:
                for ix in seal_idx:
                    if 0 <= ix < len(ordered) and ordered[ix] not in seal_targets:
                        seal_targets.append(ordered[ix])
        else:
            # 显式挑件：role=名字子串[+名字子串...]，多个名字的同角色件会被归并
            wanted = {}
            for item in pick_spec.split(","):
                if "=" not in item:
                    fail("--pick 格式应为 role=名字[+名字]，收到 %r" % item)
                role, val = item.split("=", 1)
                wanted[role.strip()] = [x.strip() for x in val.split("+") if x.strip()]
            for role, keys in wanted.items():
                hit = [o for o in meshes if pick_match(o.name, keys)]
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
        if r not in parts and r != "hair":
            print("  · 「%s」不在 --parts 里，稍后丢弃" % r)
    for ob in [o for o in meshes if o not in sum(picked.values(), [])]:
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.view_layer.update()

    # ---------- 1.1a) T 模式：从身体件里抠脖子，作为头的**独立 part**（用户裁定「脖子归头」） ----------
    #   `--pick-idx "…,neck=<身体/甲件的序号>"` 把身体件带进 picked（第 1 步会把没挑中的件全删掉，
    #   所以必须靠 pick 保住它）；这里把它的**细而绕轴**的那几块抠出来，其余丢弃。
    #   🔴 抠出来的脖子**不再过 1.2/1.3**（那两步是给脸/发用的离群判据，会把脖子整块判成杂质）。
    if t_mode and picked.get("neck"):
        _srcs = picked.pop("neck")
        # 🔴 判据参数一律**显式传**（值来自 tools/sw2-pipeline/parts_table.neck_args(key)，
        #    闸门 check_assembly.py 传的是同一份）——默认值与原版一字未改，这里只是把
        #    「逐人覆写」这条通道打开（2026-09-17 收尾轮：有人脖子那圈是布料色/比 10cm 粗一点，
        #    吃默认值抠不出来）。--neck-skin-tol 是 carve 的 ⑤ 肤色容差，默认 0.16 不变。
        #    --neck-excl-head 是 carve 的 ⑦ 主导骨排除（1=开，默认；0=关，给"脖子整圈都是头骨"
        #    的极端角色留的退路）。逐人开关写在 parts_table 的 neck_args（与闸门同一份）。
        # 🔴 `--skin-region "r,z0,z1[,skin]"`（2026-09-17 晚加）：**区域裁剪模式** ——
        #    改用 carve_skin_region（逐顶点），不再用 carve_neck_part（整块连通域）。
        #    给「皮肤/衣领长在大片段里」的角色用（稻姬胸口那片皮、小太郎那块领口…）。
        _sr = get(a, "--skin-region")
        if _sr:
            _f = [float(x) for x in _sr.split(",")[:3]]
            _sk = str(get(a, "--skin-region-skin", "1")) not in ("0", "false", "False", "no")
            _rc = get(a, "--skin-ref-color")
            _nb, _moved = carve_skin_region(_srcs, t_s, t_zsole, _f[0], _f[1], _f[2], skin=_sk,
                                            atlas=get(a, "--neck-atlas"),
                                            face_objs=picked.get("face", []),
                                            skin_tol=float(get(a, "--neck-skin-tol", "0.16")),
                                            tag="neck_src",
                                            ref_override=[float(x) for x in _rc.split(",")] if _rc else None,
                                            bones=(["bone_%s" % b for b in get(a, "--skin-region-bones").split(",")]
                                                   if get(a, "--skin-region-bones") else None),
                                            ratio=([float(x) for x in get(a, "--skin-ratio").split(",")]
                                                   if get(a, "--skin-ratio") else None))
        else:
            _nb, _moved = carve_neck_part(_srcs, t_s, t_zsole,
                                          r_max=float(get(a, "--neck-r-max", "0.10")),
                                          y_max=float(get(a, "--neck-y-max", "0.13")),
                                          z_top=float(get(a, "--neck-z-top", "1.49")),
                                          n_sect=int(get(a, "--neck-sect", "6")),
                                          z_cut=float(get(a, "--neck-z-cut", "1.41")),
                                          skin_tol=float(get(a, "--neck-skin-tol", "0.16")),
                                          excl_head=str(get(a, "--neck-excl-head", "1")) not in
                                          ("0", "false", "False", "no"),
                                          atlas=get(a, "--neck-atlas"),
                                          face_objs=picked.get("face", []),
                                          tag="neck_src")
        _dump = get(a, "--dump-neck-src")
        if _dump:
            _pts = []
            for _ob, _vs in _moved:
                _mw = _ob.matrix_world
                for _vi in _vs:
                    _q = _mw @ _ob.data.vertices[_vi].co
                    _pts.append([round(_q.x, 6), round(_q.y, 6), round(_q.z, 6)])
            with open(_dump, "w") as _fh:
                json.dump({"tol": 0.0005, "n": len(_pts), "pts": _pts}, _fh)
            print("  脖子源顶点清单：%d 个 → %s" % (len(_pts), _dump))
        for _ob, _vs in _moved:
            _shared = any(_ob in _lst for _r2, _lst in picked.items())
            if _shared:
                # 🔴 该件**同时是脸件/发件**（实测光秀：源 submesh 5/6/13 既在 face 又在 neck 序号里）
                #    → 脖子那几块从原件里**搬走**（不是复制），否则同一块几何在头资产里出现两遍、
                #    实机 z-fighting。
                _bm = bmesh.new(); _bm.from_mesh(_ob.data); _bm.verts.ensure_lookup_table()
                bmesh.ops.delete(_bm, geom=[_bm.verts[i] for i in _vs if i < len(_bm.verts)],
                                 context='VERTS')
                _bm.to_mesh(_ob.data); _bm.free(); _ob.data.update()
                print("  抠脖子：%s 里搬走 %d 顶点（该件同时是脸/发件，避免重复几何）"
                      % (_ob.name, len(_vs)))
            else:
                bpy.data.objects.remove(_ob, do_unlink=True)     # 纯身体件：整块用完就丢
        # 🔴🔴 兜底清理：把「被 pop 出来、但没被上面那圈处理到」的源对象删掉。
        #    2026-09-17 实锤 —— **28/28 人全中**：每人 5~9 件 `model_0_submesh_*`
        #    （材质 `mat_<角色>`、坐标还在**源空间** z 0~186 厘米）原样进了产出 FBX。
        #    用户在编辑器里一眼看到一排带感叹号的脏资产。
        #    为什么会有这个漏：`carve_neck_part` 的返回值 `picked_v` **只收集「抠到顶点的」源对象**；
        #    抠到 0 个时直接 `return None, []`（宁宁就是这样）→ 调用处一个都不删；
        #    抠到一些时，**没被抠中的那些源对象同样不在 `_moved` 里**（信长泄漏 9 件）。
        #    而导出是 `export_scene.fbx(use_selection=False)`（**整场景**）→ 谁没删谁就进包。
        #    根因是「脖子归头」这个功能：在那之前身体件没被 `--pick-idx` 挑中，
        #    会被上面「删掉没挑中的件」那一步清掉；现在被挑中了，就得自己负责删干净。
        #    判据：**仍被别的角色（face/hair…）引用的一律留下**（实测光秀：源 submesh 5/6/13
        #    既在 face 又在 neck 序号里，它们是脸件，删了脸就没了）；新抠出来的 `_nb` 也在 picked 里。
        _live = set()
        for _r3, _lst in picked.items():
            for _o3 in _lst:
                _live.add(id(_o3))
        _dropped = 0
        for _ob in _srcs:
            if id(_ob) in _live:
                continue
            try:
                bpy.data.objects.remove(_ob, do_unlink=True)
                _dropped += 1
            except ReferenceError:
                pass          # 已被上面那圈删掉了（对已删对象取 .name 会抛，别拿它当判据）
        if _dropped:
            print("  抠脖子：清掉 %d 件没用上的源身体件（不删会原样导出成脏资产）" % _dropped)
        if _nb is not None:
            picked.setdefault("neck", []).append(_nb)
    # ---------- 1.1) --neck：从身体件里只抠出脖子那一段，并进脸壳（见 keep_neck_frag） ----------
    #   🔴 必须跑在 1.2 之前：身体件整块进 prune_far 会被当"离群碎片"清掉（它本来就大而散）。
    #   ⚠️ 老路径（并进脸壳）：UV 在身体图集上、脸壳材质按脸图集采样，只对「整身一张图集」的战无2 成立。
    #      T 模式下走 1.1a 的独立 part 路线，这条不再用。
    elif picked.get("neck"):
        _nr = float(get(a, "--neck-r", "0") or 0)
        _nz = float(get(a, "--neck-z0", "0") or 0)
        _kept = []
        for ob in picked.pop("neck"):
            _n = keep_neck_frag(ob, _nr, _nz)
            if _n:
                print("  抠脖子：%s 留 %d/%d 顶点（最宽 ≤%.1f 且最低 ≥%.1f）"
                      % (ob.name, _n, len(ob.data.vertices), _nr, _nz))
                _kept.append(ob)
            else:
                print("  ⚠️ 抠脖子：%s 一个碎片都没命中（检查 neck_r/neck_z0）" % ob.name)
                bpy.data.objects.remove(ob, do_unlink=True)
        picked.setdefault("face", []).extend(_kept)

    # ---------- 1.2) 清理飞出去的碎片（战无2 源模型的通病，见 prune_far 注释） ----------
    #   必须跑在【切嘴之前】：嘴位是靠脸壳包围盒估的，包围盒被碎片撑歪就全废。
    if "--no-prune" not in a:
        kk = float(get(a, "--prune-k", "4.0"))
        _arm0 = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        for role, objs in list(picked.items()):
            if role == "neck":          # 脖子是刚精挑出来的，别再当碎片清一遍
                continue
            for ob in objs:
                d = prune_far(ob, kk, arm=_arm0)
                if d:
                    print("  清碎片：%s 丢掉 %d/%d 个顶点（重心离主体 > %.1f×主体半径，头骨碎片除外）"
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
            # 🔴 2026-09-15：**hair 也要过**（战无2 有角色的头发件里混着甲饰 —— 庆次 idx6 =
            #    长发 + 金色前立/胸前绳结；不滤 → 头旁边飘着甲片；滤太狠 → 长发没了）
            # ⚠️ 但【走了 hard 路线】的角色（picked 里有 "hair" 角色）例外：
            #    他们的头发归 1.4b 的硬判据管，1.3 一律不碰 —— 前田庆次是这条的
            #    第一个用户验收先例，改判据不能动他。
            for _role in ("face", "eye", "mouth", "hair"):
                if _role == "hair" and picked.get("hair"):
                    continue
                for _ob in picked.get(_role, []):
                    _d = keep_head_only(_ob, _arm, _fr, _cl, _hk, _gk)
                    if _d:
                        print("  剔非头部：%s 丢掉 %d/%d 个顶点（主导骨离面部骨簇过远，不含头骨碎片）"
                              % (_ob.name, _d, len(_ob.data.vertices) + _d))

    # ---------- 1.4b) 头发件：只留【主导骨 = 头骨】的碎片（确定性判据，2026-09-15 加） ----------
    #  🔴 为什么不能靠 1.3 的 keep_head_only：它是**离群判据**（半径比值 + 闸门系数），
    #     实测对"长发 + 甲饰混件"（庆次 idx6 / sub11：53% 头骨 + 47% 脊柱/手臂）**不触发**，
    #     结果甲饰跟着头发一起并进头，头旁边飘着金色甲片。
    #  这里用**硬判据**：碎片的主导骨落在 {bone_10, bone_11, bone_46..62} 之外 → 丢。
    #     · 头发绑 bone_11（头骨）；甲饰/前立/绳结绑脊柱或手臂 → 一刀两断
    #     · 布料驱动骨（nuno*）不在白名单里 → 连同它的碎片一起丢（那是披风/外套的布，不是头发）
    if "--no-head-only" not in a:
        _arm2 = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        if _arm2 is not None:
            # 🔴 白名单只认【头骨】：bone_10/bone_11。
            #    不收面部骨族（bone_46..62）—— 脸是【脸壳】的责任，混装件里绑面部骨的碎块
            #    通常是别人的嘴里那点东西（实测慶次 sub11 有 38 顶点绑 bone_46/59，
            #    并进头以后脸上多出一块扁条，与 --cut-mouth 切出来的嘴重复）。
            #    ⚠️ 将来若有人「刘海框」也走 hard 路线（像光秀 idx13 那样绑 bone_11 + 面部骨），
            #       要在这里按那个角色放开面部骨族。
            _HEAD_BONES = {"bone_10", "bone_11"}
            for _ob in picked.get("hair", []):
                _n0 = len(_ob.data.vertices)
                _dom = {}
                for _v in _ob.data.vertices:
                    for _g in _v.groups:
                        _nm = _ob.vertex_groups[_g.group].name
                        _dom[_nm] = _dom.get(_nm, 0.0) + _g.weight
                # 逐碎片判（用 bmesh 连通域）
                _bm = bmesh.new(); _bm.from_mesh(_ob.data); _bm.verts.ensure_lookup_table()
                _seen = [False] * len(_bm.verts); _kill = []
                for _v in _bm.verts:
                    if _seen[_v.index]:
                        continue
                    _st, _comp = [_v], []
                    _seen[_v.index] = True
                    while _st:
                        _c = _st.pop(); _comp.append(_c.index)
                        for _e in _c.link_edges:
                            _o2 = _e.other_vert(_c)
                            if not _seen[_o2.index]:
                                _seen[_o2.index] = True; _st.append(_o2)
                    _tot = {}
                    for _vi in _comp:
                        for _g in _ob.data.vertices[_vi].groups:
                            _nm = _ob.vertex_groups[_g.group].name
                            _tot[_nm] = _tot.get(_nm, 0.0) + _g.weight
                    _b = max(_tot.items(), key=lambda kv: kv[1])[0] if _tot else None
                    if _b not in _HEAD_BONES:
                        _kill += _comp
                _bm.free()
                if _kill:
                    _bm2 = bmesh.new(); _bm2.from_mesh(_ob.data); _bm2.verts.ensure_lookup_table()
                    bmesh.ops.delete(_bm2, geom=[_bm2.verts[i] for i in _kill], context='VERTS')
                    _bm2.to_mesh(_ob.data); _bm2.free(); _ob.data.update()
                    print("  头发去非头碎片：%s 丢 %d/%d 顶点" % (_ob.name, _n0 - len(_ob.data.vertices), _n0))

    # ---------- 1.3c) 摘颏带：把头盔的颏带从脸壳里摘掉 ----------
    if strap_seeds or strap_bones:
        for _ob in picked.get("face", []):
            if len(_ob.data.vertices) == 0:
                continue
            # 耳侧那几块 = 独立碎片：按种子点整块摘掉
            n = drop_frag_by_seed(_ob, strap_seeds) if strap_seeds else 0
            if n:
                print("  摘颏带（耳侧碎片）：%s 删 %d 顶点" % (_ob.name, n))
            # 🔴 下巴那一横条：**只改 UV，绝不删几何**。
            #    2026-09-15 深夜试过"整条剪掉并进兜"（用户当时选的方案），**实测失败已回退**：
            #      ① 剪掉当场在下巴开一个黑腔（`holes_fill` 补不上 —— 那是下颌壳的背面开口，
            #         不是闭合环，实测 36 条边界边补完洞还在）；
            #      ② 并进兜的那批顶点是**下颌皮肤**（渲染出来是块肉色），不是带子 ——
            #         即 `bone_59` 覆盖的是下颌皮面本身，原注释是对的。
            #    ⇒ 结论：这条带子是**画在下颌皮面上的贴图**，几何上它就是下巴。
            #       要它"消失"只能改 UV（改色），要它"搬到兜上"则无解（那等于把下巴搬走）。
            if strap_bones:
                m = retarget_uv_region(_ob, strap_bones)
                if m:
                    print("  下巴条改 UV：%s 把 %d 个 loop 指到最近的肤色区（几何不动）" % (_ob.name, m))

    # ---------- 1.4c) 头发件并进脸壳 ----------
    #  🔴 头网格只有 3 件（脸/眼/嘴）—— 头发必须【并进脸壳】。但并的时机很关键：
    #     必须在 1.3 之后（1.3 是「离面部骨族远就删」的离群判据，会把整块头发判成杂质 ——
    #     实测慶次：submesh_9 是 100% 绑头骨的头发，1.3 要删 153/153，靠 90% 安全网才没出事）
    #     且必须在 1.4b 之后（1.4b 的硬判据就是冲 `picked["hair"]` 去的）。
    if picked.get("hair"):
        print("  头发并进脸壳：%s" % [o.name for o in picked["hair"]])
        picked.setdefault("face", []).extend(picked.pop("hair"))

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

    # ---------- 1.6) --seal-bottom：把发块的方形底口封上（见 seal_open_bottom 注释） ----------
    if seal_targets:
        gap = float(get(a, "--seal-gap", "1.0"))
        for _ob in seal_targets:
            if not _ob.name or len(_ob.data.vertices) == 0:
                continue
            n = seal_open_bottom(_ob, gap, atlas=get(a, "--seal-atlas"))
            print("  封发块底口：%s 补 %d 面（余 %d 顶点）"
                  % (_ob.name, n, len(_ob.data.vertices)))

    # ---------- 1.7) 薄片补背面：单面板复制+翻面（布条/发带/飘带，见函数注释） ----------
    if "--no-double-sided" not in a:
        for _role in ("face", "eye", "mouth"):
            for _ob in picked.get(_role, []):
                _n0 = len(_ob.data.polygons)
                _n = make_sheets_double_sided(_ob)
                if _n:
                    print("  薄片补背面：%s 复制 %d/%d 面并翻面（引擎材质无双面开关，只能靠几何）"
                          % (_ob.name, _n, _n0))

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
    # 🔴 `--apply-src-xform`（2026-09-19）：把**对象的世界矩阵烘进网格数据**，而不是丢掉。
    #    为什么需要：Blender 的 FBX 导入器把「单位换算 + 轴转换（Y-up → Z-up）」放在**对象矩阵**里，
    #    网格顶点还是 FBX 原生空间（KCD 源 = **厘米 + Y-up**）。下面这行 `matrix_world = Identity`
    #    对 .blend 源（矩阵本来就是单位阵）毫无影响，对 KCD 这种 FBX 却等于把 0.01 缩放和 90° 转轴
    #    一起扔了 —— 结果「头壳中心」算到 y=161（厘米）、"眼↔嘴"标定算出 0.00968（当成厘米制缩小 100 倍）。
    #    ⚠️ 默认不开：战无2 那条线一直是在"丢掉对象矩阵"的前提下调通的（它按源空间的厘米调参），
    #       改默认会把它整套判据打乱。KCD 一类源在配方里显式传这个开关。
    _apply_xf = "--apply-src-xform" in a
    for ob in joined.values():
        _w = ob.matrix_world.copy()                 # 🔴 必须在清 parent 之前取（清了 parent，matrix_world 就退回 matrix_basis）
        ob.parent = None
        if _apply_xf:
            try:
                ob.data.transform(_w, shape_keys=True)   # 形状键一起搬（源自带 blendshape 时不丢信息）
            except TypeError:
                ob.data.transform(_w)
        ob.matrix_world = Matrix.Identity(4)
        for md in [m for m in ob.modifiers if m.type == 'ARMATURE']:
            ob.modifiers.remove(md)
    if _apply_xf:
        print("  已把对象世界矩阵烘进网格数据（%d 件）：源空间 → 米级 Z-up" % len(joined))
    for ob in [o for o in bpy.data.objects if o.type != 'MESH']:
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.view_layer.update()

    if list_only:
        lo, hi = bbox(list(joined.values()))
        print("包围盒 x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
        return

    # ---------- 3) 定向 + 标定（对全部保留件统一施加） ----------
    if t_mode:
        # 🔴 T 模式（2026-09-16）：定向 = **Y 轴镜像**（源脸 −Y → 骑砍 +Y，翻前后不翻左右；
        #    绕 Z 转 180° 会把左右一起翻，实测源左腿会被甩到 +x），缩放 = s（源原点=地面），
        #    不做器官对眼位的平移。三件（头/甲/兜）共用这一份 T。
        k = t_s
        R = None
        print("定向/标定：T 模式 —— Y 轴镜像 + 等比缩放 s=%.6f（源原点=地面，不平移）；"
              "不用眼↔嘴标定" % t_s)

        def xform(p):
            return (p.x * t_s, -p.y * t_s, (p.z - t_zsole) * t_s)
    else:
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

    if t_mode:
        # 🔴 镜像是反射（行列式 −1）→ 每个件的面绕序都反了，必须翻回来，否则法线朝里、
        #    实机整片不可见（骑砍材质单面）。与 build_armor.py 的处理同款。
        for role, ob in joined.items():
            _bm = bmesh.new()
            _bm.from_mesh(ob.data)
            bmesh.ops.reverse_faces(_bm, faces=_bm.faces[:])
            _bm.to_mesh(ob.data)
            _bm.free()
            ob.data.update()
        print("  已反转面绕序（镜像补偿）：%d 件" % len(joined))

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
        rim_at = rim_lookup(gender)

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

    # ---------- 3c-bis) 补脖子下摆（--neck-fill） ----------
    # 🔴 与 3c 互补：3c 是把宽出来的肩膀**收进**领口，这一步是把**短掉的脖子铺到**领口。
    #    战无2 的脸壳件比原版头短约 5cm（见 fill_neck_to_rim 注释），不补的话颈部有个断口，
    #    实机表现 = 「颈部没有贴合肩部，往上抬了一点」（2026-09-16 用户报，28 人全中）。
    def _neck_fill():
        rim_at = rim_lookup(gender)
        if rim_at is None:
            fail("--neck-fill 需要 RIM_TABLE 里 %s 的领口轮廓" % gender)
        _top = float(get(a, "--neck-fill-top", "1.545"))
        _rm = float(get(a, "--neck-fill-rmax", "0.14"))
        _kk = float(get(a, "--neck-fill-k", "1.0"))
        _fz = get(a, "--neck-fill-flat-z")
        _fz = float(_fz) if _fz else None
        _tube = get(a, "--neck-tube-to")
        _tube = float(_tube) if _tube else None
        _n = fill_neck_to_rim(joined["face"], rim_at, z_top=_top, r_max=_rm, k=_kk,
                              flat_z=_fz, tube_to=_tube)
        if _n:
            print("  补脖子下摆：%s 铺 %d 个面（自由边 → 领口内沿，k=%.2f%s%s）"
                  % (joined["face"].name, _n, _kk,
                     "，竖直 z=%.2f" % _fz if _fz else "",
                     "，再往下拉脖子管到 z=%.2f" % _tube if _tube else ""))
            _w = close_neck_slit(joined["face"])
            print("  闭正中缝：焊掉 %d 对顶点（前后两片壳在脖子正前方的断口）" % _w)
            # 🔴 老模式（--neck-fill，非 T）专用的一份：T 模式不走进来，它的那一份在 3c-bis-2
            #    **无条件**跑（别再把这行当"只在补下摆时才需要"而跟着退役 —— 2026-09-17 踩过）。
            _f, _sk = flip_inward_neck_faces(joined["face"])
            print("  翻颈部朝里面：%d 个（不翻的话实机=脖子正面一条黑缝）；孪生面豁免 %d 个"
                  "（双面副本朝内是设计如此，翻了会把薄片毁成单面）" % (_f, _sk))
        else:
            print("  ⚠️ 补脖子下摆：%s 颈部一条自由边都没有（该件可能已长到领口以下）"
                  % joined["face"].name)
        return _n

    # 🔴 顺序（2026-09-16 晚）：开了 `--weld-seam` 时，补下摆**推迟到合缝之后**跑。
    #    原因：合缝靠"点数相同的最近环对"认缝，而补下摆会先在那道缝的两侧铺出新边 ——
    #    实测 nene 就挑到了 4/7 号环（补出来的落点环）→ 正前那道壳缝反而永远焊不上。
    #
    # 🔴🔴 退役（2026-09-16 用户裁定「脖子归头」）：**T 模式下不再补脖子下摆、不再拉脖子管**。
    #    脖子改从源模型身体件里抠（1.1a 的 carve_neck_part）——那是源模型自己的脖子，
    #    与源模型的甲领口本来就配套；而这圈人造几何的落点跟的是**原版身体**的 V 领轮廓，
    #    留着只会多出一截藏在甲里的皮肤，把"治本了没有"这个判断盖住。
    #    按退役两步走：先停用（代码保留）→ 实机验证 → 通过才删函数。
    _legacy_neck_fill = ("--neck-fill" in a) and not t_mode
    if _legacy_neck_fill and not weld_seam:
        _neck_fill()

    # ---------- 3c-bis-2) T 模式：把颈部【法线朝里】的面翻过来（无条件跑一次） ----------
    # 🔴 2026-09-17 实锤（`plans/战国无双换装批量落地.md` §10.12）：这一步原来只长在
    #    `_neck_fill()` 里，而 T 模式下 `--neck-fill` 已退役、不再传 ⇒ 它**根本不执行**。
    #    后果：战无2 源模型颈部那圈壳一大半是朝里的面（宁宁颈部 144 面里 **87 面朝内**），
    #    骑砍材质**单面 + 背面剔除** → 朝里的面实机里看不见 → 直接透出背景 =
    #    用户报的「穿甲时领口与下巴之间一圈看得见的缝」。
    #    翻的只是**面绕序**（几何一个顶点都不动），判据不变：面心 z<1.56、离轴 r<0.14、
    #    `法线·径向 < 0`（沿用 flip_inward_neck_faces 的默认值）。
    #    放在这里 = 3c 收领口 / 3b 切一刀之后（这两步会删面/移点），量到的是**最终几何**。
    if t_mode:
        _f, _sk = flip_inward_neck_faces(joined["face"])
        print("  翻颈部朝里面：%d 个（不翻的话实机=脖子正面一条黑缝/透背景）；孪生面豁免 %d 个"
              "（双面副本朝内是设计如此，翻了会把薄片毁成单面）" % (_f, _sk))

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
        if "--weld-debug" in a:
            _lv = []
            for lp in loops:
                s = set()
                for e in lp:
                    s.update(e.verts)
                _lv.append(list(s))
            print("  [weld-debug] 自由边环（≥12 边）共 %d 个：" % len(loops))
            for k, vs in enumerate(_lv):
                zs = [v.co.z for v in vs]; ys = [v.co.y for v in vs]
                near = []
                for m2, ws in enumerate(_lv):
                    if m2 == k:
                        continue
                    kd = kdtree.KDTree(len(ws))
                    for q, v in enumerate(ws):
                        kd.insert(v.co, q)
                    kd.balance()
                    near.append((min(kd.find(v.co)[2] for v in vs), m2, len(ws)))
                near.sort()
                print("     #%d 边%-4d 点%-4d z %.3f..%.3f y %+.3f..%+.3f  最近: %s"
                      % (k, len(loops[k]), len(vs), min(zs), max(zs), min(ys), max(ys),
                         " / ".join("→#%d(%d点) %.1fmm" % (b, c, a * 1000) for a, b, c in near[:3])))
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
            #    🔴 2026-09-16 放宽：不再要求「点数相同」—— 战无2 的壳缝实测 77 vs 78 点，
            #    老条件直接跳过、挑到无关环（nene 挑到 4/7 号：两组发片边）。
            best = None
            for i in range(len(lv)):
                for j in range(i + 1, len(lv)):
                    kd = kdtree.KDTree(len(lv[j]))
                    for k, v in enumerate(lv[j]):
                        kd.insert(v.co, k)
                    kd.balance()
                    d = min(kd.find(v.co)[2] for v in lv[i])
                    if best is None or d < best[0]:
                        best = (d, i, j)
            if best is None:
                print("  [warn] 合缝：没有可选环对，跳过")
            else:
                d0, i0, j0 = best
                va, vb = lv[i0], lv[j0]
                print("  合缝：候选环对 %d/%d，各 %d 点，最近距离 %.2fmm" % (i0, j0, len(va), d0 * 1000))

                # 3) 逐点就近配对（🔴 不要求环是有序简单闭合圈）：
                #    战无2 的壳缝实测有分叉（77 点/85 边），原来那套「走环 + 旋转对齐」直接判死。
                #    两环本来就是同一条缝的两侧、多数点已经重合（最近 0.00mm），
                #    所以「各自找对面最近点、双双移到中点、再焊」既简单又不会错配。
                LIM = 0.025
                pairs, far = [], 0
                for v in va:
                    w = min(vb, key=lambda q: (v.co - q.co).length)
                    if (v.co - w.co).length <= LIM:
                        pairs.append((v, w))
                    else:
                        far += 1
                if not pairs:
                    print("  [warn] 合缝：两环没有 ≤%.0fmm 的配对，跳过" % (LIM * 1000))
                else:
                    ds = [(a.co - b.co).length for a, b in pairs]
                    mx, avg = max(ds), sum(ds) / len(ds)
                    print("  合缝：配对 %d 对（%d 点超过 %.0fmm 未配），平均间距 %.2fmm 最大 %.2fmm"
                          % (len(pairs), far, LIM * 1000, avg * 1000, mx * 1000))
                    if mx > LIM:
                        print("  [warn] 合缝：最大间距 >%.0fmm，疑非对应环，跳过（不动几何）"
                              % (LIM * 1000))
                    else:
                        for a, b in pairs:                 # 两端都移到中点 → 再焊
                            mid = (a.co + b.co) / 2.0
                            a.co = mid
                            b.co = mid
                        # 🔴 去重（2026-09-19）：两个 va 点可能配到**同一个** vb 点（就近配对的必然结果），
                        #    直接喂 remove_doubles 会 `ValueError: verts: found the same (BMVert) used
                        #    multiple times` 整脚本崩掉。萨菲罗斯那条链 `--weld-seam` 实测 72 对里就有
                        #    重配 → 崩溃是**既有**问题（拿 HEAD 版单跑同样崩），不是本轮引入的。
                        verts = list({v for pr in pairs for v in pr})
                        res = bmesh.ops.remove_doubles(bm, verts=verts, dist=1e-4)
                        bm.to_mesh(ob.data); bm.free(); ob.data.update()
                        bpy.context.view_layer.update()
                        print("  合缝：焊掉 %d 个顶点 → 脸壳剩 %d 顶点 %d 面"
                              % (len(pairs), len(ob.data.vertices), len(ob.data.polygons)))
                        bm = None
        if bm is not None:
            bm.free()
        bpy.context.view_layer.update()
        # 合缝做完再补下摆（顺序理由见 3c-bis 的注释；T 模式下已停用，见那里的退役说明）
        if _legacy_neck_fill:
            _neck_fill()

    # 自检：三个锚点必须落位（T 模式没有"眼位"这个锚，改判「脸朝 +Y」+ 头骨高度）
    E2 = center([joined["eye"]]); M2 = center([joined["mouth"]])
    lo, hi = bbox(list(joined.values()))
    print("落位：眼球 %s（%s）" % (tuple(round(v, 4) for v in E2),
                                 ("目标 %s" % (tuple(round(v, 4) for v in TARGET_EYE),)) if not t_mode else "T 模式不对眼位"))
    print("      嘴   %s" % (tuple(round(v, 4) for v in M2),))
    print("      全头包围盒 x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
    if not t_mode:
        if abs(E2.z - TARGET_EYE.z) > 0.003 or abs(E2.y - TARGET_EYE.y) > 0.003:
            fail("眼球落位偏差过大：%s" % (tuple(round(v, 4) for v in E2),))
    else:
        # T 模式：头骨（源 bone_11）应当正好落在原版头骨高度 1.569 —— 这是 s 的定义，落不上说明 T 传错了
        if src_head_z is None:
            print("      ⚠️ T 自检跳过：源模型没有骨架 / 没有 bone_11")
        else:
            _hz = src_head_z * t_s - t_zsole * t_s
            print("      T 自检：源头骨 %.3f → 骑砍空间 z = %.4f（应 ≈ 1.569）" % (src_head_z, _hz))
            if abs(_hz - 1.569) > 0.01:
                fail("T 缩放不对：源头骨落在 %.4f，应为 1.569" % _hz)
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
        # 🔴 脖子件用**脸壳的材质名**（2026-09-17 用户裁定「用头部贴图」）：
        #    编辑器工程里没有 `<头名>_neck` 这个材质资源（材质是首次导入时按当时的件数建的，
        #    后来件数变 4 却没建）→ 编辑器拿默认白材质渲染 = 实机外看到的「脖子白板」。
        #    运行时本来就是脸壳配方（MatRole 兜底），改成同名 = 编辑器能解析、语义不变、少一个 ⚠。
        mat_name = name if role in ("face", "neck") else "%s_%s" % (name, role)
        # 🔴 必须 copy 一份再改名（2026-09-14 修）：源模型常常**整身共用一张材质**
        #    （战国无双2 的 L02 就是：全身 12 个子网格同指 `mat_L02_nobunaga`）。
        #    直接 `materials[0].name = mat_name` 改的是**那个共享 datablock** —— 三个件轮流改名，
        #    最后只剩最后一个名字（实测：三件全是 `head_nobunaga_a_mouth`），编辑器里就是一个材质。
        #    蒂法/萨菲罗斯的源恰好每件自带材质，所以这个坑一直没暴露。
        if role == "neck" and joined.get("face") is not None and joined["face"].data.materials:
            # 🔴 脖子**直接引用脸壳的材质 datablock**（不 copy、不改名）：
            #    若各自 new/copy 一个同名材质，Blender 会去重成 `head_<名>_a.001`
            #    → 导出写的就是带 `.001` 的名字 → 编辑器照样找不到 = 白板没修掉（实测踩过）。
            #    共用同一个 datablock 才会导出成**同一个材质名**（件也随材质的合并而合并）。
            ob.data.materials.clear()
            ob.data.materials.append(joined["face"].data.materials[0])
        elif ob.data.materials:
            m = ob.data.materials[0].copy()
            m.name = mat_name
            ob.data.materials[0] = m
            while len(ob.data.materials) > 1:
                ob.data.materials.pop(index=len(ob.data.materials) - 1)
        else:
            ob.data.materials.append(bpy.data.materials.new(mat_name))
        for p in ob.data.polygons:
            p.material_index = 0
        # 🔴 `--neck-keep-uv`（2026-09-17 晚加）：**保留脖子件的原 UV**，不塌到肤色点。
        #    为什么：脖子件有两种来源 ——
        #      ① 从**身体/甲件**抠出来的那圈皮：它的 UV 会跨到图集的衣服/非头区（实测 u[0.002,0.955]）
        #         → 必须塌点（neck_uv_to_skin），否则采样发暗（这是原修法）。
        #      ② 从**头部件的皮肤层**（如战无2 稻姬 sub6「头发+脖子胸口皮」）抠出来的：它本来就是
        #         真皮肤几何、UV 落在图集的皮肤区 → **塌点反而把它拍成一块平色**（用户实机对比源模型：
        #         "精细度能一样吗"）。这一类要**保留原 UV**。
        if role == "neck" and "--neck-keep-uv" not in a:
            neck_uv_to_skin(ob, joined.get("face"), get(a, "--neck-atlas"))
        elif role == "neck":
            print("  脖子 UV：**保留原 UV**（--neck-keep-uv）")
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

            # 5c) 脸壳下半 + **脖子件**改抄原版权重：整体刚性绑 13 = 脖子不跟脊柱/锁骨动，
            #     身体呼吸时胸廓扩张而领口不动 → 皮从身体里穿出来（实机 2026-09-14 实测）。
            #     🔴 脖子件（1.1a 抠出来的）**整件**都在颈部高度 → t 恒为 1 → 全部抄原版头颈权重，
            #        这就是「几何 + 权重两件套」里的权重那一半（只做几何 = 呼吸时脖子从肩里冒出来）。
            if vkd is not None and role in ("face", "neck"):
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

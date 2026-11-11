# -*- coding: utf-8 -*-
"""build_char.py — RDR2 角色 GLB → 骑砍2 可导入的蒙皮 FBX（+ 一张图集贴图）。

一句话原理
----------
RDR2 的网格自带关节索引/权重（GLB 的 JOINTS_0/WEIGHTS_0，**服装单件 GLB 也有**），
RDR2 的骨架静止姿态（`yft_skel.json` + `yft_bind.json`）与网格**同一空间、只差 +1.0 m 高度**（实测）。所以：

  ① 用「关节权重质心 → 最近的骨架线段」反解 **drawable 局部关节号 → RDR2 骨名**
     （候选限定 28 根主骨 + 解剖位置先验：A-pose 里手垂在胯边，纯按距离会把衣摆判给前臂）
  ② RDR2 骨名 → 骑砍 28 骨（显式表 → 手指族 → 父链兜底）
  ③ 逐**源**骨摆正：`T_b = Trans(bl_head) ∘ R_b ∘ Trans(-S·src_head)`
     · `R_b` 只给**上臂**一个角（锁骨/前臂/手是"骨轴约定不同"，各转一个角会把肩肘撕开）
     · `--anchor` 控制是否额外把骨 head 搬到骑砍骨 head（默认 none：实测 all 会把网格撕碎）
  ④ 顶点按原权重混合 → 顶点组合并成骑砍骨名 → 绑 official human_skeleton
  ⑤ 多材质 → **一张图集 + 一个材质**（合原版范式；UV 按格子重排，贴图现拼）

轴（实测，别改）
----------------
RDR2 bind (x, y, z=高度) → Blender **(-x, -y, z+1.0)**，单位米
  · 骑砍左 = -X ✓ · 骑砍前 = +Y ✓ · 行列式 = +1（纯旋转 180°）⇒ **不反转面绕序**
  · glTF 顶点 (x,y,z) → 同一空间 = **(-x, z, y)**（不是 (-x,-z,y)，也不是再加一次 +1）

🔴 UV：glTF 原点在**左上**、Blender 在**左下** ⇒ V 必须 `1-v`。不翻 = 马甲/衬衫对调。

用法
----
  blender -b --factory-startup --python build_char.py -- ^
      --glb <角色.glb> --part body|head|hat|all --name rdr2_body_xxx --out <目录> ^
      [--skel <human_skeleton.fbx>] [--anchor none|limbs|all] [--scale auto|<数>] ^
      [--radial 1.22] [--cell 512] [--render] [--with-body] [--pose-test] [--no-export]

产物：<out>/<name>.fbx（蒙皮网格，**单材质**，材质名 = <name>）
      <out>/<name>_d.png / <name>_n.png（图集漫反射 / 图集法线）
      <out>/<name>_<视图>.png（--render 时）
"""
import bpy
import json
import math
import os
import struct
import sys
from mathutils import Matrix, Vector

# ------------------------------------------------------------------ 参数
A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def get(args, key, default=None):
    return args[args.index(key) + 1] if key in args else default


GLB = get(A, "--glb")
PART = get(A, "--part", "body")
NAME = get(A, "--name", "rdr2_char")
OUT = get(A, "--out", os.path.join("Debug", "offline", "_rdr2_char_out"))
SKEL = get(A, "--skel",
           r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
           r"\modding_resources\skeletons\human_skeleton.fbx")
ANCHOR = get(A, "--anchor", "none")
SCALE_ARG = str(get(A, "--scale", "auto"))
RADIAL = float(get(A, "--radial", "1.0"))    # 额外整体加胖（默认不加；贴合靠 --fit-body 缩包裹）
RZ_MAX = float(get(A, "--radial-z-max", "1.58"))     # 到这个高度以上不再放大（头/帽子保持原样）
RZ_FADE = float(get(A, "--radial-z-fade", "0.10"))
CELL = int(get(A, "--cell", "512"))
SM_ITERS = int(get(A, "--smooth", "3"))
CULL = "--no-cull" not in A           # 单面渲染（默认）：双面会把内衬层透出来，与外壳打架成斑块
FIT_BODY = "--fit-body" in A          # 缩包裹（默认关：会把大衣内衬顶出来与外面穿插，形成明暗斑块）
FIT_MARGIN = float(get(A, "--fit-margin", "0.012"))
RENDER = "--render" in A
EXPORT = "--no-export" not in A
RDR2 = get(A, "--rdr2-root", os.path.join("D:" + os.sep, "BrainMaker", "大镖客2解包分析"))

BIND_JSON = os.path.join(RDR2, "export", "skeleton_runtime", "yft_bind.json")
SKEL_JSON = os.path.join(RDR2, "export", "skeleton_runtime", "yft_skel.json")

if not GLB or not os.path.exists(GLB):
    print("!! 缺 --glb 或文件不存在: %r" % GLB)
    sys.exit(2)

# ------------------------------------------------------------------ GLB 解析
CT = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def load_glb(path):
    data = open(path, 'rb').read()
    off, jc, bc = 12, None, None
    while off < len(data):
        clen, ctype = struct.unpack('<II', data[off:off + 8])
        off += 8
        chunk = data[off:off + clen]
        if ctype == 0x4E4F534A:
            jc = chunk
        elif ctype == 0x004E4942:
            bc = chunk
        off += clen
    return json.loads(jc.decode('utf-8')), bc


def read_acc(j, bc, ai):
    a = j['accessors'][ai]
    bv = j['bufferViews'][a['bufferView']]
    fmt, sz = CT[a['componentType']]
    nc = NC[a['type']]
    start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride') or sz * nc
    return [struct.unpack_from('<' + fmt * nc, bc, start + i * stride) for i in range(a['count'])]


def extract_images(j, bc, outdir, only_names):
    """把 GLB 内嵌的贴图存成 PNG（只存 only_names 里的）。GLB 里 albedo+法线都有，
    而交付包的 `<名>.fbm/` 只有 albedo —— 所以贴图一律从 GLB 取。"""
    os.makedirs(outdir, exist_ok=True)
    written = {}
    for im in j.get('images', []):
        nm = im.get('name', '')
        if nm not in only_names:
            continue
        bv = j['bufferViews'][im['bufferView']]
        st = bv.get('byteOffset', 0)
        ln = bv['byteLength']
        fp = os.path.join(outdir, nm + '.png')
        with open(fp, 'wb') as f:
            f.write(bc[st:st + ln])
        written[nm] = fp
    return written


# ------------------------------------------------------------------ 骨映射
RDR2_TO_BL = {
    'SKEL_ROOT': 'bip01_pelvis_0',
    'SKEL_Pelvis': 'bip01_pelvis_0',
    'SKEL_Spine_Root': 'bip01_pelvis_0',
    'SKEL_Spine0': 'bip01_spine_9',
    'SKEL_Spine1': 'bip01_spine_9',
    'SKEL_Spine2': 'bip01_spine1_10',
    'SKEL_Spine3': 'bip01_spine2_11',
    'SKEL_Neck0': 'bip01_neck_12',
    'SKEL_Neck1': 'bip01_head_13',
    'SKEL_Neck2': 'bip01_head_13',
    'SKEL_Head': 'bip01_head_13',
    'PH_HeadProp': 'bip01_head_13',
    'SKEL_L_Clavicle': 'bip01_l_clavicle_14',
    'SKEL_L_UpperArm': 'bip01_l_upperarm_twist_15',
    'SKEL_L_Forearm': 'bip01_l_foretwist_17',
    'SKEL_L_Hand': 'bip01_l_hand_19',
    'SKEL_R_Clavicle': 'bip01_r_clavicle_21',
    'SKEL_R_UpperArm': 'bip01_r_upperarm_twist_22',
    'SKEL_R_Forearm': 'bip01_r_foretwist_24',
    'SKEL_R_Hand': 'bip01_r_hand_26',
    'SKEL_L_Thigh': 'bip01_l_thigh_1',
    'SKEL_L_Calf': 'bip01_l_calf_2',
    'SKEL_L_Foot': 'bip01_l_foot_3',
    'SKEL_L_Toe0': 'bip01_l_toe0_4',
    'SKEL_R_Thigh': 'bip01_r_thigh_5',
    'SKEL_R_Calf': 'bip01_r_calf_6',
    'SKEL_R_Foot': 'bip01_r_foot_7',
    'SKEL_R_Toe0': 'bip01_r_toe0_8',
}
FINGER = {'SKEL_L': 'bip01_l_finger0_20', 'SKEL_R': 'bip01_r_finger0_27'}
# 姿态修正只给上臂（锁骨/前臂/手是骨轴约定不同，各转一个角会把肩肘撕开）
# 姿态修正只给**上臂**一个角，前臂/手刚性跟随。
# 🔴 逐骨各转一个角（前臂/手也算自己的）实测把袖子甩到身体外面 —— 那是骨轴约定差异不是姿势差。
ROT_BONES = set() if '--no-rot' in A else {'SKEL_L_UpperArm', 'SKEL_R_UpperArm'}
LIMB_PREFIX = ('bip01_l_thigh', 'bip01_l_calf', 'bip01_l_foot', 'bip01_l_toe0',
               'bip01_r_thigh', 'bip01_r_calf', 'bip01_r_foot', 'bip01_r_toe0',
               'bip01_l_clavicle', 'bip01_l_upperarm', 'bip01_l_twist1',
               'bip01_l_foretwist', 'bip01_l_foretwist1', 'bip01_l_hand', 'bip01_l_finger0',
               'bip01_r_clavicle', 'bip01_r_upperarm', 'bip01_r_twist1',
               'bip01_r_foretwist', 'bip01_r_foretwist1', 'bip01_r_hand', 'bip01_r_finger0')


def rdr2_to_bl(v, dz=1.0):
    """RDR2 bind (x,y,z=高度) -> Blender (-x, -y, z+1.0)。"""
    return Vector((-v[0], -v[1], v[2] + dz))


def load_rdr2_bones():
    sk = json.load(open(SKEL_JSON, encoding='utf-8'))
    bind = json.load(open(BIND_JSON, encoding='utf-8'))
    bones, by_name, parent_of, name_of = {}, {}, {}, {}
    for b in sk['bones']:
        parent_of[b['i']] = b['parent']
        name_of[b['i']] = b['name']
    for b in sk['bones']:
        t = str(b['tag'])
        if t in bind:
            bones[b['i']] = dict(i=b['i'], name=b['name'], parent=b['parent'],
                                 tail=rdr2_to_bl(bind[t]['pos']))
    for b in bones.values():
        p = bones.get(b['parent'])
        b['head'] = p['tail'].copy() if p else b['tail'].copy()
        by_name.setdefault(b['name'], b)
    return bones, by_name, parent_of, name_of


def bl_name_for(name, depth=0):
    if name in RDR2_TO_BL:
        return RDR2_TO_BL[name]
    if '_Finger' in name or '_Thumb' in name or '_Hand_' in name:
        for k, v in FINGER.items():
            if name.startswith(k + '_'):
                return v
    if depth > 40 or name not in NAME2IDX:
        return None
    pi = PARENT_OF.get(NAME2IDX[name], -1)
    pn = NAME_OF.get(pi)
    return bl_name_for(pn, depth + 1) if pn else None


def seg_dist(p, a, b):
    d = b - a
    L2 = d.length_squared
    if L2 < 1e-12:
        return (p - a).length
    t = max(0.0, min(1.0, (p - a).dot(d) / L2))
    return (p - (a + d * t)).length


def _cand_ok(name, c):
    """解剖先验：A-pose 手垂在胯边，纯距离匹配会把衣摆判给前臂（实测腰段 70% 权重落在前臂）。"""
    if c.z > 0.75 and ('_Foot' in name or '_Toe' in name):
        return False
    if c.z > 0.85 and abs(c.x) < 0.26 and ('_Hand' in name or '_Forearm' in name):
        return False
    # 胸口/肚子这一带（|x|<0.20）也不该被**上臂/锁骨**驱动：
    # 实测大衣前襟有 20%+ 的权重落到上臂骨上，一抬胳膊衣襟就被掰开，胸前露出一条黑缝。
    if 0.95 < c.z < 1.50 and abs(c.x) < 0.20 and ('_UpperArm' in name or '_Clavicle' in name):
        return False
    # 胯以下（z<0.90）一律不可能是**任何**手臂骨：实测裤脚有 5% 权重落在手骨上，
    # 手臂一做姿态修正，那几个顶点就被拽走 —— 表面出现一片片碎三角。
    if c.z < 0.90 and ('_Hand' in name or '_Forearm' in name or '_UpperArm' in name
                       or '_Clavicle' in name or '_Finger' in name):
        return False
    return True


def nearest_main(c):
    best, bd = None, 1e9
    for nm, (a, b) in segs.items():
        if not _cand_ok(nm, c):
            continue
        d = seg_dist(c, a, b)
        if d < bd:
            best, bd = nm, d
    return best


# ------------------------------------------------------------------ 1 读 GLB
print("== 1/7 读 GLB ==")
j, bc = load_glb(GLB)
mats = [m.get('name', '?') for m in j.get('materials', [])]
HEAD_KEYS = ('head', 'eyes', 'teeth', 'eyebrow', 'hair', 'beard')
prim_list = []
for mesh in j['meshes']:
    for pm in mesh['primitives']:
        at = pm['attributes']
        mat = mats[pm['material']] if 'material' in pm else '?'
        low = mat.lower()
        if PART == 'all':
            keep = True
        elif PART == 'body':
            keep = not any(k in low for k in HEAD_KEYS) and 'hat' not in low
        elif PART == 'head':
            # 头 = 脸壳 + 眼 + 牙 + 眉 + 头发（只按 'head' 一个字串筛会漏掉眼睛/头发）
            keep = any(k in low for k in HEAD_KEYS)
        else:
            keep = PART in low
        if not keep:
            continue
        prim_list.append(dict(mat=mat, pos=read_acc(j, bc, at['POSITION']),
                              nrm=read_acc(j, bc, at['NORMAL']) if 'NORMAL' in at else None,
                              uv=read_acc(j, bc, at['TEXCOORD_0']) if 'TEXCOORD_0' in at else None,
                              idx=[x[0] for x in read_acc(j, bc, pm['indices'])] if 'indices' in pm else [],
                              joints=read_acc(j, bc, at['JOINTS_0']) if 'JOINTS_0' in at else None,
                              weights=read_acc(j, bc, at['WEIGHTS_0']) if 'WEIGHTS_0' in at else None))
print("   prims 选中 %d 个（part=%s）材质 %s" % (
    len(prim_list), PART, sorted(set(p['mat'] for p in prim_list))))
if not prim_list:
    print("!! 没有 prim 命中 part=%s" % PART)
    sys.exit(2)

# ------------------------------------------------------------------ 2 骨架
print("== 2/7 读 RDR2 骨架 ==")
rdr2, rdr2_by_name, PARENT_OF, NAME_OF = load_rdr2_bones()
NAME2IDX = {v: k for k, v in NAME_OF.items()}
segs = {b['name']: (b['head'], b['tail']) for b in rdr2.values() if b['name'] in RDR2_TO_BL}
print("   骨 %d 根（有静止位置），主骨候选 %d 根" % (len(rdr2), len(segs)))

# ------------------------------------------------------------------ 3 反解关节号
print("== 3/7 反解 局部关节号 -> RDR2 骨 ==")
for pr in prim_list:
    pr['l2b'] = {}
    if not pr['joints']:
        continue
    acc = {}
    for p, jj, ww in zip(pr['pos'], pr['joints'], pr['weights']):
        pv = Vector((-p[0], p[2], p[1]))
        for k in range(4):
            w = ww[k]
            if w <= 0:
                continue
            a = acc.setdefault(jj[k], [Vector((0, 0, 0)), 0.0])
            a[0] += pv * w
            a[1] += w
    pr['l2b'] = {ji: nearest_main(s / w) for ji, (s, w) in acc.items()}
    print("   %-46s %2d 关节 -> %s" % (pr['mat'], len(pr['l2b']),
                                       ', '.join(sorted(set(pr['l2b'].values()))[:5])))

# ------------------------------------------------------------------ 4 官方骨架
print("== 4/7 导入官方骨架 ==")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SKEL, global_scale=100.0)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
BL = {b.name: b for b in arm.data.bones}
head_bone = rdr2_by_name.get('SKEL_Head')
src_head_z = head_bone['head'].z if head_bone else 1.65
bl_head_z = BL['bip01_head_13'].head_local.z
S = bl_head_z / src_head_z if SCALE_ARG == 'auto' else float(SCALE_ARG)
print("   head 骨：源 z=%.4f 骑砍 z=%.4f -> 全局缩放 S=%.5f" % (src_head_z, bl_head_z, S))


def radial_k(z):
    """径向放大随高度渐变：脖子以下放大，头和帽子不变（否则帽子会变宽一圈）。"""
    if z >= RZ_MAX:
        return 1.0
    if z <= RZ_MAX - RZ_FADE:
        return RADIAL
    return 1.0 + (RADIAL - 1.0) * (RZ_MAX - z) / RZ_FADE


# ------------------------------------------------------------------ 5 网格 + 权重
def bl_dir(bb):
    """骑砍骨方向 = head -> 最长子骨 head。🔴 不能用 tail_local（骑砍骨轴不沿肢体）。"""
    kids = [c for c in bb.children if 'nub' not in c.name and 'notused' not in c.name]
    if not kids:
        return None
    d = max(kids, key=lambda c: (c.head_local - bb.head_local).length).head_local - bb.head_local
    return d.normalized() if d.length > 1e-6 else None


ARM_CHAINS = [('SKEL_L_Clavicle', 'SKEL_L_UpperArm', 'SKEL_L_Forearm', 'SKEL_L_Hand'),
              ('SKEL_R_Clavicle', 'SKEL_R_UpperArm', 'SKEL_R_Forearm', 'SKEL_R_Hand')]


def abs_rot(src_name):
    if src_name not in ROT_BONES:
        return Matrix.Identity(3)
    sb = rdr2_by_name.get(src_name)
    bname = bl_name_for(src_name)
    bb = BL.get(bname) if bname else None
    if sb is None or bb is None:
        return Matrix.Identity(3)
    d_s = (sb['tail'] - sb['head'])
    d_b = bl_dir(bb)
    if d_s.length < 1e-6 or d_b is None:
        return Matrix.Identity(3)
    d_s.normalize()
    ax = d_s.cross(d_b)
    ang = math.atan2(ax.length, d_s.dot(d_b))
    if ax.length < 1e-6 or ang < 1e-4:
        return Matrix.Identity(3)
    return Matrix.Rotation(ang, 3, ax.normalized())


def build_chain_k():
    K = {}
    for chain in ARM_CHAINS:
        prev_k = Matrix.Identity(4)
        prev_a = Matrix.Identity(3)
        for nm in chain:
            sb = rdr2_by_name.get(nm)
            if sb is None:
                break
            j = sb['head'] * S
            _k = radial_k(j.z)
            j = Vector((j.x * _k, j.y * _k, j.z))
            # 没列进 ROT_BONES 的链上骨 = 继承父骨旋转（写成单位会把父骨的旋转抵消掉）
            a = abs_rot(nm) if nm in ROT_BONES else prev_a
            prev_k = prev_k @ (Matrix.Translation(j) @ (prev_a.inverted() @ a).to_4x4()
                               @ Matrix.Translation(-j))
            prev_a = a
            K[nm] = prev_k
        hand = chain[-1]
        if hand in K:
            side = hand.rsplit('_', 1)[0]
            for nm in rdr2_by_name:
                if ('Finger' in nm or 'Thumb' in nm) and nm.startswith(side + '_'):
                    K.setdefault(nm, K[hand])
    return K


CHAIN_K = build_chain_k()


def bone_matrix(src_name):
    sb = rdr2_by_name.get(src_name)
    bname = bl_name_for(src_name)
    bb = BL.get(bname) if bname else None
    if sb is None or bb is None:
        return None
    M = CHAIN_K.get(src_name, Matrix.Identity(4)).copy()
    anchored = ANCHOR == 'all' or (ANCHOR == 'limbs' and bname.startswith(LIMB_PREFIX))
    if anchored:
        p_src = sb['head'] * S
        p_bl = bb.head_local.copy()
        if src_name.endswith('_Thigh'):     # 两根大腿共用骨盆 tail，右腿会横移 19cm
            p_src = Vector((p_bl.x / S if S else p_src.x, p_src.y, p_src.z))
        M = Matrix.Translation(p_bl - p_src) @ M
    return M


print("== 5/7 建网格 + 重定向 ==")
verts, faces, uvs, mat_of_face = [], [], [], []
src_w = {}
for pr in prim_list:
    base = len(verts)
    for p in pr['pos']:
        v = Vector((-p[0], p[2], p[1])) * S
        k = radial_k(v.z)
        verts.append(Vector((v.x * k, v.y * k, v.z)))
    for i in range(0, len(pr['idx']) - 2, 3):
        a_, b_, c_ = pr['idx'][i], pr['idx'][i + 1], pr['idx'][i + 2]
        # 🔴 绕序纠正：RDR2 引擎按**法线属性**着色，源网格的绕序本身就不一致（实测 14.3% 反过来）。
        #    我们只按绕序算面法线 ⇒ 那 14% 会「正面被剔除 + 明暗发黑」。用源 NORMAL 把绕序掰正。
        if pr['nrm'] is not None:
            pa, pb, pc = pr['pos'][a_], pr['pos'][b_], pr['pos'][c_]
            ga = Vector((-pa[0], pa[2], pa[1]))
            gb = Vector((-pb[0], pb[2], pb[1]))
            gc = Vector((-pc[0], pc[2], pc[1]))
            gn = (gb - ga).cross(gc - ga)
            na, nb, nc = pr['nrm'][a_], pr['nrm'][b_], pr['nrm'][c_]
            vn = Vector((-(na[0] + nb[0] + nc[0]), na[2] + nb[2] + nc[2], na[1] + nb[1] + nc[1]))
            if gn.length > 1e-12 and vn.length > 1e-9 and gn.dot(vn) < 0:
                a_, c_ = c_, a_
        faces.append((base + a_, base + b_, base + c_))
        mat_of_face.append(pr['mat'])
    uvs.extend(pr['uv'] or [(0.0, 0.0)] * len(pr['pos']))
    if pr['joints']:
        for vi, (jj, ww) in enumerate(zip(pr['joints'], pr['weights'])):
            for k in range(4):
                w = ww[k]
                if w <= 0:
                    continue
                src = pr['l2b'].get(jj[k])
                if src is None or bl_name_for(src) not in BL:
                    continue
                src_w.setdefault(src, []).append((base + vi, w))

Tcache = {}
new_co = [Vector((0.0, 0.0, 0.0)) for _ in verts]
acc_w = [0.0] * len(verts)
for src, lst in src_w.items():
    if src not in Tcache:
        Tcache[src] = bone_matrix(src)
    M = Tcache[src]
    for vi, w in lst:
        v = verts[vi]
        new_co[vi] += ((M @ v) if M is not None else v) * w
        acc_w[vi] += w
for i, v in enumerate(verts):
    verts[i] = (new_co[i] / acc_w[i]) if acc_w[i] > 1e-6 else v

vgroups = {}
for src, lst in src_w.items():
    bn = bl_name_for(src)
    for vi, w in lst:
        vgroups.setdefault(bn, {}).setdefault(vi, 0.0)
        vgroups[bn][vi] += w

# 🔴 逐**顶点**（不是逐关节）剔掉解剖上不可能的骨：
#    按关节质心过滤漏掉"质心在腰上、顶点垂到裤腿"的布料骨，那些顶点会被手臂旋转拽走，
#    表面就出现一片片孤立碎三角。这里按顶点自身位置兜底，剔完重新归一化。
ARM_TAGS = ('upperarm', 'foretwist', 'hand', 'clavicle', 'finger')
_vw = {}
for bn, d in vgroups.items():
    for vi, w in d.items():
        _vw.setdefault(vi, {})[bn] = w
_removed = 0
for vi, d in _vw.items():
    z = verts[vi].z
    x = verts[vi].x
    bad = set()
    if z < 0.90:
        bad = {bn for bn in d if any(t in bn for t in ARM_TAGS)}
    elif 0.95 < z < 1.50 and abs(x) < 0.20:
        bad = {bn for bn in d if ('upperarm' in bn or 'clavicle' in bn)}
    if bad and len(bad) < len(d):
        for bn in bad:
            del d[bn]
            _removed += 1
if _removed:
    vgroups = {}
    for vi, d in _vw.items():
        t = sum(d.values()) or 1.0
        for bn, w in d.items():
            vgroups.setdefault(bn, {})[vi] = w / t
    print("   剔骨：%d 处非法臂骨权重已清掉并重新归一化" % _removed)

# 权重平滑：RDR2 布料骨稀疏，逐顶点转换会在"某几个顶点吃 A 骨、邻居吃 B 骨"处撕尖刺
if SM_ITERS > 0:
    vw = [dict() for _ in verts]
    for bn, d in vgroups.items():
        for vi, w in d.items():
            vw[vi][bn] = w
    adj = [set() for _ in verts]
    for a, b, c in faces:
        adj[a].update((b, c))
        adj[b].update((a, c))
        adj[c].update((a, b))
    for _ in range(SM_ITERS):
        out = []
        for vi, cur in enumerate(vw):
            acc = {bn: w * 0.5 for bn, w in cur.items()}
            nb = adj[vi]
            if nb:
                f = 0.5 / len(nb)
                for j2 in nb:
                    for bn, w in vw[j2].items():
                        acc[bn] = acc.get(bn, 0.0) + w * f
            s = sum(acc.values())
            if s > 1e-9:
                top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
                s2 = sum(w for _, w in top)
                out.append({bn: w / s2 for bn, w in top})
            else:
                out.append(cur)
        vw = out
    vgroups = {}
    for vi, d in enumerate(vw):
        for bn, w in d.items():
            if w > 1e-4:
                vgroups.setdefault(bn, {})[vi] = w

mesh = bpy.data.meshes.new(NAME)
mesh.from_pydata([tuple(v) for v in verts], [], faces)
mesh.validate()
uvl = mesh.uv_layers.new(name='UVMap')
for li, loop in enumerate(mesh.loops):
    vi = loop.vertex_index
    if vi < len(uvs):
        u, v = uvs[vi]
        uvl.data[li].uv = (u, 1.0 - v)      # glTF 左上原点 -> Blender 左下
ob = bpy.data.objects.new(NAME, mesh)
bpy.context.collection.objects.link(ob)
for bn, d in vgroups.items():
    g = ob.vertex_groups.new(name=bn)
    for vi, w in d.items():
        g.add([vi], min(1.0, w), 'REPLACE')
print("   顶点 %d / 面 %d / 顶点组 %d（anchor=%s）" % (len(verts), len(faces), len(ob.vertex_groups), ANCHOR))

# ---- 缩包裹：把"埋进原版身体里"的顶点贴到身体表面（只动埋在里面的，露在外面的一个不碰）
# 🔴 为什么这么做：用户要的是**衣服**，不是把 RDR2 的人体搬过来 —— 衣服就该穿在原版身体上。
#    实测整体径向放大不管用（1.22 仍露 42% 顶点 / 最远 12.6 cm，1.45 人已走形），
#    而"只把埋进去的顶出来"既盖得住又不改轮廓。
# 🔴 只有 body 件才做缩包裹：原版身体网格只覆盖躯干+胳膊，把头也拿去贴会把下巴和脖子压瘪。
if FIT_BODY and PART == "body":
    body_fbx = get(A, "--body-fbx", os.path.join(
        os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))),
        'Debug', 'offline', '自定义头', 'core_game', 'fbx', 'body', 'body', 'body_male_a.fbx'))
    if not os.path.exists(body_fbx):
        print("   !! 找不到原版身体，跳过缩包裹: %s" % body_fbx)
    else:
        before = set(o.name for o in bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=body_fbx, global_scale=1.0)
        tgt = None
        for o in bpy.data.objects:
            if o.name in before or o.type != 'MESH':
                continue
            o.parent = None
            o.matrix_world = Matrix.Identity(4)
            if tgt is None:
                tgt = o
        if tgt is not None:
            # 手写"最近点顶出"：只处理**判定在里面**的顶点（(p-loc)·n < 0），推 loc + n*margin。
            # 🔴 不要用 Shrinkwrap 修改器：实测 wrap_mode='INSIDE' 会把整件衣服压成贴身皮，
            #    连"露在身体外面的衣摆/袖子"也一起吸过去，衣服就不像衣服了。
            pushed = 0
            me = ob.data
            # ① 先算每个"埋进去"的顶点的位移量（不直接改）
            disp = [None] * len(me.vertices)
            for v in me.vertices:
                ok, loc, nor, _i = tgt.closest_point_on_mesh(v.co)
                if not ok:
                    continue
                if (v.co - loc).dot(nor) < 0.0:
                    disp[v.index] = (loc + nor * FIT_MARGIN) - v.co
                    pushed += 1
            # ② 🔴 位移必须**平滑**再施加：逐顶点各自顶到最近点会让相邻顶点往不同方向跳，
            #    三角形被拉成长条、法线乱飞 —— 实测整件衣服明暗碎成一地。
            adj = [set() for _ in me.vertices]
            for p_ in me.polygons:
                a, b, c = p_.vertices
                adj[a].update((b, c))
                adj[b].update((a, c))
                adj[c].update((a, b))
            Z = Vector((0.0, 0.0, 0.0))
            for _ in range(int(get(A, "--fit-smooth", "6"))):
                nd = []
                for i, d in enumerate(disp):
                    acc = (d if d is not None else Z) * 0.5
                    nb = adj[i]
                    if nb:
                        f = 0.5 / len(nb)
                        for j2 in nb:
                            acc = acc + (disp[j2] if disp[j2] is not None else Z) * f
                    nd.append(acc)
                disp = nd
            for i, d in enumerate(disp):
                if d is not None and d.length > 1e-6:
                    me.vertices[i].co = me.vertices[i].co + d
            me.update()
            print("   缩包裹：%d 个埋在身体里的顶点顶到表面外 %.3f m（位移已平滑）" % (pushed, FIT_MARGIN))
        for o in [o for o in bpy.data.objects if o.name not in before]:
            bpy.data.objects.remove(o, do_unlink=True)

# 🔴 平滑着色 + 源法线：
#    `from_pydata` 建出来的面默认是 **flat（每面一个法线）**，渲出来每一格三角都看得到棱
#    （预览图里那种"明显的多边角"就是它）。glTF 导入器会自动开平滑，我们手搓网格没有。
#    再叠加源模型自带的 NORMAL 当自定义法线，明暗才和原版一致。
for _p in mesh.polygons:
    _p.use_smooth = True
print("   平滑：%d/%d 个面已开平滑" % (sum(1 for _p in mesh.polygons if _p.use_smooth), len(mesh.polygons)))
_normals = None
if all(pr['nrm'] is not None for pr in prim_list):
    _normals = [None] * len(verts)
    _off = 0
    for pr in prim_list:
        for _i, _n in enumerate(pr['nrm']):
            _normals[_off + _i] = (-_n[0], _n[2], _n[1])   # 与顶点同一条轴变换
        _off += len(pr['pos'])
try:
    # 默认**不写**自定义法线：绕序已按源 NORMAL 掰正，Blender 自动算的平滑法线与源基本一致；
    # 而自定义法线是"逐顶点写死"的，只要后面动过顶点（例如缩包裹）就会和几何对不上，
    # 表现是满身明暗碎三角（实测）。要保真再加 --custom-normals。
    if '--custom-normals' in A and _normals and all(n is not None for n in _normals):
        mesh.normals_split_custom_set_from_vertices(_normals)
        print("   法线：已写入源模型法线（%d 个）" % len(_normals))
except Exception as _e:
    print("   !! 自定义法线写入失败（不影响导出）: %s" % _e)
mesh.update()

mod = ob.modifiers.new('Armature', 'ARMATURE')
mod.object = arm
ob.parent = arm

# ------------------------------------------------------------------ 6 材质（多材质，不拼图集）
# 🔴 不做图集：拼图集要重排 UV，而 RDR2 的 UV 是**平铺**的（大衣 u 到 1.999），
#    一格错位就整片面贴错图（实测满身碎三角）。多材质 Mesh 引擎是支持的（建筑资产就是多材质），
#    代价只是编辑器里每个材质要单独建一次。
print("== 6/7 材质（每种源材质一个）==")
used = sorted(set(mat_of_face))
texdir = os.path.join(OUT, '_tex_' + NAME)
want = set(used) | set(m.replace('_ab_albedo', '_nm_normal') for m in used)
tex_files = extract_images(j, bc, texdir, want)
def _copy_tex(src_name, out_path):
    fp = tex_files.get(src_name)
    if not fp:
        return None
    import shutil
    shutil.copyfile(fp, out_path)
    return out_path
os.makedirs(OUT, exist_ok=True)
mat_index = {}
for mn in used:
    m = bpy.data.materials.new(mn)
    m.use_nodes = True
    m.use_backface_culling = CULL
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    dp = _copy_tex(mn, os.path.join(OUT, mn + '_d.png'))
    if dp and bsdf:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(dp, check_existing=True)
        m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    mat_index[mn] = len(ob.data.materials)
    ob.data.materials.append(m)
    _copy_tex(mn.replace('_ab_albedo', '_nm_normal'), os.path.join(OUT, mn + '_n.png'))
print("   材质 %d 个；贴图 %d 张 -> %s" % (len(used), len(used) * 2, OUT))

prim_ranges, _v = [], 0
for pr in prim_list:
    prim_ranges.append((_v, _v + len(pr['pos']), pr['mat']))
    _v += len(pr['pos'])


def prim_mat_of_vertex(vi):
    for a, b, m in prim_ranges:
        if a <= vi < b:
            return m
    return used[0]


mesh.polygons.foreach_set('material_index',
                          [mat_index[prim_mat_of_vertex(p.vertices[0])] for p in mesh.polygons])
mesh.update()

# ------------------------------------------------------------------ 7 渲染 / 导出
print("== 7/7 渲染 / 导出 ==")
if RENDER:
    os.makedirs(OUT, exist_ok=True)
    body_fbx = get(A, "--body-fbx", os.path.join(
        os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))),
        'Debug', 'offline', '自定义头', 'core_game', 'fbx', 'body', 'body', 'body_male_a.fbx'))
    if '--with-body' in A and os.path.exists(body_fbx):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=body_fbx, global_scale=1.0)
        for o in bpy.data.objects:
            if o in before or o.type != 'MESH':
                continue
            o.parent = None
            o.matrix_world = Matrix.Identity(4)
            o.data.materials.clear()
            mm = bpy.data.materials.new('ref_body')
            mm.use_nodes = True
            mm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.75, 0.12, 0.12, 1)
            o.data.materials.append(mm)
    if '--coverage' in A and os.path.exists(body_fbx):
        # 硬判据：原版身体的每个顶点，是否落在我们网格内部（closest_point_on_mesh 法向符号）
        bm = [o for o in bpy.data.objects if o.type == 'MESH' and o is not ob and not o.name.startswith(NAME)]
        inside = outside = 0
        worst = []
        for o in bm:
            mw = o.matrix_world
            for v in o.data.vertices:
                p = mw @ v.co
                ok, loc, nor, _i = ob.closest_point_on_mesh(p)
                if not ok:
                    continue
                if (p - loc).dot(nor) < 0:
                    inside += 1
                else:
                    outside += 1
                    worst.append((p - loc).length)
        tot = inside + outside
        worst.sort(reverse=True)
        print("   覆盖率：原版身体 %d 顶点，被包住 %d (%.1f%%)，露在外面 %d (%.1f%%)，最远露出 %.3f m"
              % (tot, inside, 100.0 * inside / max(1, tot), outside, 100.0 * outside / max(1, tot),
                 worst[0] if worst else 0.0))
    if '--pose-test' in A:
        bpy.context.view_layer.objects.active = arm
        for bn, axis, deg in (('bip01_l_thigh_1', 'X', -35), ('bip01_l_calf_2', 'X', 55),
                              ('bip01_r_clavicle_21', 'Y', -18), ('bip01_r_upperarm_twist_22', 'Z', -55),
                              ('bip01_r_foretwist_24', 'Z', -45), ('bip01_spine1_10', 'X', -12)):
            pb = arm.pose.bones.get(bn)
            if pb is None:
                continue
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler.rotate_axis(axis, math.radians(deg))
        print("   姿态测试：已摆 6 根骨")
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 640, 900
    cam_data = bpy.data.cameras.new('cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 2.1
    cam = bpy.data.objects.new('cam', cam_data)
    scn.collection.objects.link(cam)
    scn.camera = cam
    light = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    light.data.energy = 4.0
    # 🔴 预览用的 SUN 必须关阴影：EEVEE 在低模服装上会出**阴影麻点**（一片片亮/暗三角），
    #    看着像模型坏了，其实是渲染器的锅（同机位换 3/4 视角就没）。别拿它当资产质量的判据。
    light.data.use_shadow = False
    light.rotation_euler = (math.radians(55), 0, math.radians(35))
    scn.collection.objects.link(light)
    world = bpy.data.worlds.new('w')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.20, 0.20, 0.22, 1)
    scn.world = world
    for tag, loc, rot in (('front', (0.0, 6.0, 0.95), (math.radians(90), 0, math.radians(180))),
                          ('side', (-6.0, 0.0, 0.95), (math.radians(90), 0, math.radians(-90)))):
        cam.location = loc
        cam.rotation_euler = rot
        scn.render.filepath = os.path.join(OUT, '%s_%s.png' % (NAME, tag))
        bpy.ops.render.render(write_still=True)
        print("   %s" % scn.render.filepath)

if EXPORT:
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    fp = os.path.join(OUT, NAME + '.fbx')
    bpy.ops.export_scene.fbx(filepath=fp, use_selection=True, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='Y', axis_up='Z', object_types={'ARMATURE', 'MESH'},
                             use_mesh_modifiers=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X',
                             bake_anim=False, path_mode='STRIP', embed_textures=False)
    print("   导出 %s" % fp)
print("== 完成 ==")

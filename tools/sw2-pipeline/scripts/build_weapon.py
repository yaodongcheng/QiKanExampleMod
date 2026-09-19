# -*- coding: utf-8 -*-
"""build_weapon.py —— 战无2 源模型 → 骑砍2 武器网格（单件核心）。

为什么要有这一步（不能直接拿源件）
------------------------------------
战无2 的模型是 **T-pose**，武器**横躺在手上**（实测 28 人：长轴几乎全是 ±X）。
而骑砍2 的武器网格约定（实测原版 `torch_g` + 织丰 `sho_bokken_katana` 归纳）：
    · **原点 = 握持点**（原版握把跨原点：torch z[-0.08, 0.28]）
    · **长轴 = +Z**（刀尖朝 +Z）
    · **刃宽沿 X、刃厚沿 Y**（原版 blade：x±2.8cm 宽 / y±0.42cm 厚 / z 长）
所以必须做一次刚体变换，把源件摆正。

变换怎么算（全自动，判据都是实测出来的）
----------------------------------------
  1. **握持点** = 离武器最近的那根**手骨**（战无2 手骨族 bone_18/19/26~45）投影到长轴上的点。
     实测 28 人：手骨到武器表面 0.2~40cm（多数 <3cm）——武器确实是握在手里的。
  2. **长轴** = 武器顶点 PCA 第一主成分；**次轴** = 第二主成分（扁武器的刃宽方向）。
  3. **刀尖朝哪端** = 长轴上离握持点**更远**的那一端。
  4. **旋转** R：长轴(刀尖向)→+Z、次轴→+X、第三轴→+Y（三轴正交，det=+1，天然是旋转）。
  5. **平移**：握持点 → 原点。**缩放**：源件是厘米，×0.01 → 米。

用法（Blender）:
    blender -b --python build_weapon.py -- --src <源.fbx> --out <输出目录> --name taikou_xxx_weapon_a
"""
import bpy
import sys
import os
import re
import math
import numpy as np
from mathutils import Vector, Matrix

def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d

def patch_importer():
    """导入器内存补丁（两种源格式各踩过一个坑）：
    ① 战无2 FBX 的 morph 通道缺 FullWeights → 断言崩溃。
    ② KCD（3ds Max 导出）的武器/盾蒙皮到的骨头不在骨架子树下 → `mesh.armature_setup`
       里没有对应登记 → `link_hierarchy` 抛 KeyError: None。只在会崩的场合兜底。
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
            "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
            + bad2))
    if src != orig:
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)

def make_hand_matcher(spec):
    """把手骨规格编译成判定函数。spec = 逗号分隔的 token，每个 token 是：
         · 精确骨名            `RightHand`
         · 前缀+编号范围       `bone_26-45`  → bone_26..bone_45
       默认值 `bone_18,bone_19,bone_26-45` 与战无2 原行为逐字等价。
    """
    exact, ranges = set(), []
    for tok in (t.strip() for t in spec.split(",")):
        if not tok:
            continue
        m = re.fullmatch(r"(.+)_(\d+)-(\d+)", tok)
        if m:
            ranges.append((m.group(1) + "_", int(m.group(2)), int(m.group(3))))
        else:
            exact.add(tok)

    def is_hand(name):
        if not name:
            return False
        if name in exact:
            return True
        for pre, lo, hi in ranges:
            if name.startswith(pre) and name[len(pre):].isdigit() \
                    and lo <= int(name[len(pre):]) <= hi:
                return True
        return False
    return is_hand

# ---------------- 参数 ----------------
# 三个参数化开关（默认值 = 战无2 原行为，逐字不变；换源只改这三个）
#   --pick-mat    按【材质名子串】挑武器件（战无2 约定 mat_w_）
#   --pick-name   按【对象名子串】挑武器件（给了就优先用它；KCD 用这个）
#   --hand-bones  手骨规格（定握持点用）
#   --scale       源单位 → 米（战无2 是厘米给 0.01；KCD 是米给 1.0）
a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = get(a, "--src")
OUTDIR = os.path.abspath(get(a, "--out"))
NAME = get(a, "--name")
PICK_MAT = (get(a, "--pick-mat", "mat_w_") or "").lower()
PICK_NAME = get(a, "--pick-name") or ""
HAND_SPEC = get(a, "--hand-bones", "bone_18,bone_19,bone_26-45")
# 握持点怎么定：
#   hand  = 找离武器最近的手骨（战无2：武器握在手里，T-pose 横躺）
#   guard = 找**最宽的横截面（十字护手）**，握持点 = 护手与近端（配重端）的中点
#           —— 给"武器摆在原点的道具散件"用（KCD 就是），手骨启发式在那里必然失效
GRIP_MODE = get(a, "--grip-mode", "hand")
SCALE = float(get(a, "--scale", "0.01"))
if not (SRC and OUTDIR and NAME):
    sys.exit("用法: --src <源.fbx> --out <目录> --name <资源名>\n"
             "      [--pick-mat mat_w_] [--pick-name <对象名子串>]\n"
             "      [--hand-bones bone_18,bone_19,bone_26-45] [--grip-mode hand|guard] [--scale 0.01]")
if GRIP_MODE not in ("hand", "guard"):
    sys.exit("!! --grip-mode 只能是 hand 或 guard，收到 %r" % GRIP_MODE)
os.makedirs(OUTDIR, exist_ok=True)
CM = SCALE * 100.0          # 1 源单位 = CM 厘米（打印用）
IS_HAND = make_hand_matcher(HAND_SPEC)

# ---------------- 导入 + 挑武器件 ----------------
patch_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

weapon_obs = []
for ob in bpy.data.objects:
    if ob.type != "MESH":
        continue
    if PICK_NAME:
        hit = PICK_NAME.lower() in ob.name.lower()
    else:
        mats = " ".join((m.name if m else "") for m in ob.data.materials).lower()
        hit = PICK_MAT in mats
    if hit:
        weapon_obs.append(ob)
if not weapon_obs:
    sys.exit("!! 源模型里没有命中挑件条件的武器件（--pick-name=%r / --pick-mat=%r）"
             % (PICK_NAME, PICK_MAT))
print("[WEAP] 武器件 %d 个：%s" % (len(weapon_obs), [o.name for o in weapon_obs]))

W = np.array([list(ob.matrix_world @ v.co) for ob in weapon_obs for v in ob.data.vertices])
print("[WEAP] 顶点 %d，包围盒（源单位）x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]"
      % (len(W), W[:, 0].min(), W[:, 0].max(), W[:, 1].min(), W[:, 1].max(),
         W[:, 2].min(), W[:, 2].max()))

# ---------------- PCA 定长轴 / 次轴 ----------------
c = W.mean(axis=0)
X = W - c
ev, evec = np.linalg.eigh(np.cov(X.T))
order = np.argsort(ev)[::-1]
u, v = evec[:, order[0]], evec[:, order[1]]
t = X @ u

# ---------------- 握持点 + 刀尖向 ----------------
if GRIP_MODE == "guard":
    NB = 40
    edges = np.linspace(t.min(), t.max(), NB + 1)
    sp = []
    for i in range(NB):
        m = (t >= edges[i]) & (t <= edges[i + 1] if i == NB - 1 else t < edges[i + 1])
        if m.sum() < 4:
            sp.append(-1.0)
            continue
        Q = X[m] - np.outer(X[m] @ u, u)
        sp.append(float(np.percentile(np.linalg.norm(Q, axis=1), 90)))
    gi = int(np.argmax(sp))
    if sp[gi] <= 0:
        sys.exit("!! 找不到有效横截面，--grip-mode guard 不适用这个源")
    tg = 0.5 * (edges[gi] + edges[gi + 1])
    print("[GRIP] 最宽横截面 t=%+.1fcm（侧向半径 %.1fcm）→ 判为护手" % (tg * CM, sp[gi] * CM))
    far_up = (t.max() - tg) >= (tg - t.min())
    u_tip = u if far_up else -u
    th = 0.5 * (tg + (t.min() if far_up else t.max()))
    print("[GRIP] 刀尖朝%s端 · 握持点 t=%+.1fcm（护手与配重端的中点）"
          % ("上(+)" if far_up else "下(-)", th * CM))
    print("[GRIP] 护手→握持点 %.1fcm / 握持点→配重端 %.1fcm"
          % (abs(tg - th) * CM, abs(th - (t.min() if far_up else t.max())) * CM))
    P = c + u * th              # 与 hand 模式口径一致：P 在长轴上、投影 = th
    dmin = 0.0
else:
    hands = []
    for arm in bpy.data.objects:
        if arm.type != "ARMATURE":
            continue
        for b in arm.data.bones:
            if IS_HAND(b.name):
                hands.append((b.name, np.array(list(arm.matrix_world @ b.head_local))))
    if not hands:
        sys.exit("!! 骨架里没有手骨（--hand-bones=%s），无法定握持点" % HAND_SPEC)
    P, dmin, pname = None, 1e9, ""
    for nm, p in hands:
        d = float(np.linalg.norm(W - p, axis=1).min())
        if d < dmin:
            P, dmin, pname = p, d, nm
    print("[HAND] 握持手 %s，到手表面最近 %.1fcm" % (pname, dmin * CM))
    if dmin * CM > 25.0:
        print("   !! 手离武器 %.0fcm —— 握持点可疑，务必渲染核对" % (dmin * CM))
    th = float((P - c) @ u)
    # 刀尖朝离握持点更远的那端
    u_tip = u if (t.max() - th) >= (th - t.min()) else -u

print("[AXIS] 长轴 %.3f/%.3f/%.3f（刀尖向）· 次轴 %.3f/%.3f/%.3f · 全长 %.1fcm"
      % (u_tip[0], u_tip[1], u_tip[2], v[0], v[1], v[2], (t.max() - t.min()) * CM))
print("[AXIS] 握持点两侧杆长：柄侧 %.0fcm / 尖侧 %.0fcm"
      % (min(th - t.min(), t.max() - th) * CM, max(th - t.min(), t.max() - th) * CM))

# ---------------- 构造旋转 R ----------------
e1 = u_tip
e2 = v - e1 * float(e1 @ v)              # 正交化次轴
n2 = np.linalg.norm(e2)
if n2 < 1e-6:
    sys.exit("!! 次轴退化，PCA 结果不可用")
e2 /= n2
e3 = np.cross(e1, e2)                    # 右手系
R = np.array([e2, e3, e1])               # 行 = e2/e3/e1 ⇒ R·e2=x̂, R·e3=ŷ, R·e1=ẑ
print("[XFORM] det(R) = %.6f（应为 +1）" % np.linalg.det(R))

# 握持点 = 长轴上参数为 th 的那个点（让武器轴线穿过原点）
# 🔴 必须用 u 不能用 e1(=u_tip)：th 是沿 +u 量的，u_tip 反向时用 e1 会把握持点算到另一头
hold = c + u * th
M = Matrix([list(R[0]), list(R[1]), list(R[2])]).to_4x4()

# ---------------- 变换顶点 + 合并成一件 ----------------
me_new = bpy.data.meshes.new(NAME)
all_v, all_f, all_uv = [], [], []
for ob in weapon_obs:
    me = ob.data
    mw = ob.matrix_world
    base = len(all_v)
    for vt in me.vertices:
        w = np.array(list(mw @ vt.co))
        q = R @ (w - hold) * SCALE        # 源单位 → 米
        all_v.append(q)
    uvl = me.uv_layers.active
    for poly in me.polygons:
        all_f.append([i + base for i in poly.vertices])
    # 🔴 缺 UV 的件**不能整件放弃 UV**（踩过：小次郎/政宗/归蝶各有一个无 UV 的小件，
    #    于是整把武器都不导 UV）——按环逐个取，没有的补 (0,0)。
    for li in range(len(me.loops)):
        all_uv.append(tuple(uvl.data[li].uv) if uvl else (0.0, 0.0))
    if uvl is None:
        print("   ⚠️ 件 %s 没有 UV 层 → 该件 UV 全部记 (0,0)" % ob.name[-24:])
me_new.from_pydata([Vector(x) for x in all_v], [], all_f)
me_new.update()
if len(all_uv) == len(me_new.loops):
    uvl = me_new.uv_layers.new(name="UVMap")
    for i, uv in enumerate(all_uv):
        uvl.data[i].uv = uv
else:
    sys.exit("!! UV 数与面环数不符（%d vs %d）—— 合并逻辑出错，别硬导"
             % (len(all_uv), len(me_new.loops)))

ob_new = bpy.data.objects.new(NAME, me_new)
bpy.context.scene.collection.objects.link(ob_new)

# 材质名 = 资源裸名（铁律 27 同制式；编辑器按材质名找材质资产）
mat = bpy.data.materials.new(NAME)
mat.use_nodes = True
me_new.materials.append(mat)

# 清场：只留武器（编辑器导入只认这一个）
for ob in list(bpy.data.objects):
    if ob is not ob_new:
        bpy.data.objects.remove(ob, do_unlink=True)

q = np.array([list(v.co) for v in me_new.vertices])
print("[OUT ] 变换后包围盒 x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]（米）"
      % (q[:, 0].min(), q[:, 0].max(), q[:, 1].min(), q[:, 1].max(),
         q[:, 2].min(), q[:, 2].max()))
print("[OUT ] 握把跨原点：z 负侧 %.3fm / 正侧 %.3fm" % (-q[:, 2].min(), q[:, 2].max()))

# ---------------- 导出 FBX（规格照 build_armor.py）----------------
bpy.context.scene.unit_settings.scale_length = 1.0
fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
kw = dict(
    filepath=fbx_path, use_selection=False, object_types={'MESH'},
    global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
    axis_forward='Y', axis_up='Z', bake_anim=False, path_mode='COPY',
    embed_textures=False, use_custom_props=False,
)
try:
    bpy.ops.export_scene.fbx(**kw)
except TypeError as e:
    print("   fbx kwarg 问题:", e)
    kw.pop('apply_scale_options', None)
    bpy.ops.export_scene.fbx(**kw)
print("[OUT ] 导出 FBX -> %s (%.0f KB)" % (fbx_path, os.path.getsize(fbx_path) / 1024.0))

blob = open(fbx_path, 'rb').read()
tex = sorted(set(m.decode('latin1') for m in
                 re.findall(rb'[ -~]{4,120}\.(?:png|tga|dds|jpg|jpeg)', blob)))
print("[OUT ] 自检 贴图引用: %s" % (tex if tex else "无 ✓"))
print("[OUT ] 自检 UV: %s" % ("有 ✓" if (b'LayerElementUV' in blob or b'UVMap' in blob)
                              else "!! 缺 —— 贴图不会生效"))
print("DONE", NAME)

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""pose_mediapipe -> 骑砍2：把「33 个 3D 关键点」解算成「28 骨骨骼动画」。

== 这份脚本在整个链路里的位置 ==
    视频/图片 --[pipeline/tools/vid2pose.py]--> pose.json --[本脚本]--> FBX + TRF

== 解算原理（纯几何，无神经网络、无 IK 库）==
  一句话：**把每根骨"瞄准"到它对应的人体骨段方向**，扭转交给父链继承。

  符号：W_rest(b) = 该骨静止时的世界旋转；E(b) = 世界空间的【相对静止的旋转增量】。
        d_rest(b) = 静止时"从 b 的骨头指向其子骨骨头"的方向（世界系）
        d_now(b)  = 关键点算出来的同一段方向
  每根骨要求：            E(b) @ d_rest(b) = d_now(b)
  层级关系：              E(b) = E(parent) @ G(b)
  两式相减（把父的转掉）：  G(b) = RotationBetween( d_rest(b), E(parent)⁻¹ @ d_now(b) )
  于是：                  basis(b) = W_rest(b)⁻¹ @ G(b) @ W_rest(b)      ← 写进 Blender 的 pose quaternion

  ⭐ 为什么这样可以：G 是"两点向量的最小旋转"(swing-only)，**不含 twist** ——
     twist 天然由父链继承，所以不会像逐轴欧拉那样把关节转飞。
     这就是 GitHub 上 mediapipe-pose2bvh 的做法（computeR + SetRbyCalculatingJoints）。

== 三个必须知道的取舍 ==
  1. **只用方向，不用长度** ⇒ 骨骼长度/体型一律是目标骨架自己的。源视频里那人腿多长都无所谓。
  2. **多骨段要分摊**：骑砍2 的 spine/spine1/spine2 对应"髋→肩"同一段，三根都瞄同一个方向会
     让第一根独自转完剩下的（数学上自洽但难看），所以用 `frac` 把总转角按比例分摊。
  3. **叶骨无子骨**：head/toe0/finger0 没有子骨可瞄 —— head 用它自己的静止轴（=头朝前），
     其余直接保持静止（继承父骨）。**骑砍2 每只手只有 1 根静态手指骨，手型本来就驱动不了。**

用法（一般走 pipeline/run_retarget.py 调，不必手敲）
    blender -b --python retarget.py -- --clip dance01 --name pose_dance01 --outdir output
"""
import json
import math
import os
import sys

import numpy as np

import bpy
from mathutils import Matrix, Quaternion, Vector

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def _project_root(p):
    for _ in range(6):
        if os.path.isdir(os.path.join(p, "pipeline")) and os.path.isdir(os.path.join(p, "input")):
            return p
        p = os.path.dirname(p)
    return os.path.dirname(p)


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            o[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return o


ARGS = _args()
PROJECT_ROOT = _project_root(os.path.dirname(os.path.abspath(__file__)))
DATA_ROOT = r"D:\BrainMaker\骑砍2动画重定向"
if not (os.path.isdir(os.path.join(PROJECT_ROOT, "input")) and os.path.isdir(os.path.join(PROJECT_ROOT, "output"))):
    if os.path.isdir(os.path.join(DATA_ROOT, "input")):
        PROJECT_ROOT = DATA_ROOT
HERE = os.path.dirname(os.path.abspath(__file__))

CLIP = ARGS.get("clip")
NAME = ARGS.get("name") or ("pose_" + str(CLIP))
OUTDIR = ARGS.get("outdir") or os.path.join(PROJECT_ROOT, "output")
ANIMDIR = ARGS.get("animdir") or os.path.join(PROJECT_ROOT, "input", "source", "pose_mediapipe")
SKEL = ARGS.get("skel") or os.path.join(PROJECT_ROOT, "input", "target", "bannerlord", "human_lod_4.fbx")
MAPF = ARGS.get("map") or os.path.join(HERE, "map.json")
TGT_FPS = int(ARGS.get("fps") or 30)
PELVIS = (ARGS.get("pelvis") or "none").lower()      # none | ground
YAW = float(ARGS.get("yaw") or 0.0)                  # 整体绕世界 Z 转（受试者背对镜头时补 180）
AUTO_YAW = str(ARGS.get("auto_yaw") or "false").lower() in ("1", "true", "yes")
# 🔴 根骨朝向用哪套基准。默认 hip（髋线 × 髋→肩），**侧视素材下髋线又短又抖、会偏几十度**；
#    eye = 用【双眼连线中线的垂线】——这是本仓既定的"前向基准"（见 docs/骨骼经验、双人摆位 skill）。
ROOT_BASIS = (ARGS.get("root_basis") or "hip").lower()
KEEP_ROOT_H = str(ARGS.get("keep_root_h") or "false").lower() in ("1", "true", "yes")
NO_TRF = str(ARGS.get("no_trf") or "false").lower() in ("1", "true", "yes")


def log(m):
    print("[pose-align] %s" % m, flush=True)


def read_json(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


MAPD = read_json(MAPF)
POSE = read_json(os.path.join(ANIMDIR, CLIP + ".json"))
AXIS = Matrix([[float(v) for v in r] for r in MAPD["axis"]["matrix"]]).to_4x4()
AXIS3 = AXIS.to_3x3()
LAND = POSE["world"]                                  # [T][33][3]  米制, 髋中心, y 向下
NF = len(LAND)
log("源关键点: %s（%d 帧 @ %.1f fps, %s）" % (os.path.basename(CLIP), NF, POSE.get("fps", 0), POSE.get("source", "")))

# ── 朝向 / 正反面（由 vid2pose 用"面部可见度 + 髋线方位角"独立判出来的，不依赖骨骼）──
FACING = POSE.get("facing") or {}
if FACING:
    log("源朝向: 中位 yaw %+.1f°（范围 %+.1f~%+.1f°）｜ 逐帧最大跳变 %.1f° ｜ 面部可见度 %.2f"
        % (FACING.get("median_yaw_deg", 0.0), *FACING.get("yaw_range_deg", [0.0, 0.0]),
           FACING.get("max_frame_jump_deg", 0.0), FACING.get("face_vis_mean", 0.0)))
    if FACING.get("n_jumps_gt60"):
        log("  [!] 朝向有 %d 次 >60° 突跳（帧 %s）—— 大概率 MediaPipe 左右判反，结果会整段镜像；"
            "请回看 vid2pose 的叠加图" % (FACING["n_jumps_gt60"], FACING.get("jump_frames", [])[:8]))
    if not FACING.get("likely_front_facing", True) and not AUTO_YAW and abs(YAW) < 1e-6:
        log("  [!] 素材疑似【背对镜头】：解出来角色会背朝标准前方 +Y。"
            "用 --auto-yaw 自动转正，或手工 --yaw 180")
    if AUTO_YAW:
        _auto = float(FACING.get("median_yaw_deg", 0.0))
        log("自动朝向: 把中位 yaw %+.1f° 转回 0°（角色转到骨架标准前方 +Y），施加 %+.1f°"
            % (FACING.get("median_yaw_deg", 0.0), _auto))
        YAW += _auto

# ── 🔴 整体转身：**必须旋转【关键点数据】，不能旋转根骨** ──────────────────────────
#   本算法的每一根骨都被"瞄准"到源方向（E(b) @ d_rest(b) = d_now(b)，见文件头公式），
#   所以根骨上的旋转会被子骨的瞄准**抵消**：实测把 -69.4° 加在根骨上，FK 量出来的
#   骨盆朝向确实转了 139°，但手的位置一动没动 ⇒ 结果是"骨盆拧了、四肢原地"的变形骨架。
#   正解：先把 33 个关键点整体绕【世界 Z（竖直）轴】转 YAW 度，再照常解算 —— 这时
#   源方向本身就转了，全链一致。已用 FK 复测：--yaw +69.4 ⇒ 末帧朝向 yaw ≈ 0°（正对 +Y）。
#   旋转在 MP 系里做：Rz(θ)_arm = AXIS3 ∘ M ∘ AXIS3⁻¹（M 就是 MP 系里的等价旋转）。
if abs(YAW) > 1e-6:
    _Rz = Matrix.Rotation(math.radians(YAW), 3, 'Z')
    _M = np.array(AXIS3.inverted() @ _Rz @ AXIS3, dtype=float)
    _W = np.array(LAND, dtype=float)
    LAND = (_M @ _W.reshape(-1, 3).T).T.reshape(_W.shape)
    log("整体转身: 关键点绕世界 Z 旋转 %+.1f°（源朝向 yaw %+.1f° → %+.1f°）"
        % (YAW, float(FACING.get("median_yaw_deg", 0.0)) if FACING else 0.0,
           (float(FACING.get("median_yaw_deg", 0.0)) - YAW) if FACING else -YAW))


# ─────────────────────────── 点 / 方向 工具 ───────────────────────────

def mid(pts, spec):
    """spec = 关键点下标(int) 或 下标列表(取中点)。返回世界系(Vector，已转轴）。"""
    if isinstance(spec, int):
        spec = [spec]
    v = Vector((0.0, 0.0, 0.0))
    for i in spec:
        v += AXIS3 @ Vector(tuple(LAND[pts][i]))
    return v / float(len(spec))


def safe_dir(a, b):
    """a->b 的单位方向；退化时返回 None。"""
    d = b - a
    n = d.length
    if n < 1e-6:
        return None
    return d / n


# ─────────────────────────────── 骨架装载 ───────────────────────────────

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
bpy.ops.import_scene.fbx(filepath=SKEL)
sc.render.fps = TGT_FPS                      # 硬约束 8：导入骑砍 FBX 会把 fps 改成 24，必须还原
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
arm.name = "human_skeleton_notused"          # 硬约束 2
try:
    arm.data.name = "human_skeleton_notused"
except Exception:
    pass
if arm.animation_data:
    arm.animation_data.action = None
PB = list(arm.pose.bones)
IDX = {b.name: i for i, b in enumerate(PB)}
log("目标骨架: %s（%d 骨, fps=%d）" % (arm.name, len(PB), sc.render.fps))


def rest_local(pb):
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()


REST_LOCAL_Q = [rest_local(pb).to_quaternion() for pb in PB]
# 世界静止旋转矩阵（armature 空间）
REST_WORLD = []
for pb in PB:
    if pb.parent:
        REST_WORLD.append(REST_WORLD[IDX[pb.parent.name]] @ rest_local(pb).to_3x3())
    else:
        REST_WORLD.append(pb.bone.matrix_local.to_3x3())
REST_WORLD_Q = [m.to_quaternion() for m in REST_WORLD]

# 每根骨的"静止瞄准方向"：优先用【子骨 head 偏移】（这才是有动画意义的方向），
# 没有子骨（真叶骨）才退回它自己的静止轴。
CHILDREN = {}
for pb in PB:
    if pb.parent:
        CHILDREN.setdefault(pb.parent.name, []).append(pb)
ANCHOR = {}
for pb in PB:
    ch = CHILDREN.get(pb.name)
    if ch:
        ANCHOR[pb.name] = ("child", safe_dir(pb.bone.head_local, ch[0].bone.head_local))
    else:
        ANCHOR[pb.name] = ("self", safe_dir(pb.bone.head_local, pb.bone.tail_local))

# 驱动表：{骨名: (a_spec, b_spec, frac)}
DRIVE = {}
DRIVE_FULL = {}      # 带 "up" 参考的骨：走【全基】驱动（方向 + roll 都定），见 solve_frame
for nm, d in MAPD["bones"].items():
    if nm not in IDX:
        log("  !! map.json 里的骨名在目标骨架找不到: %s（跳过）" % nm)
        continue
    DRIVE[nm] = (d["a"], d["b"], float(d.get("frac", 1.0)))
    if d.get("up"):
        DRIVE_FULL[nm] = (d["a"], d["b"], d["up"][0], d["up"][1], d.get("lat"))
if DRIVE_FULL:
    log("全基驱动（方向+roll）%d 骨: %s" % (len(DRIVE_FULL), ", ".join(sorted(DRIVE_FULL))))
KEEP = set(MAPD.get("keep_rest", []))
ROOTB = MAPD["root"]["bone"]
ROOT_UP = MAPD["root"]["up"]
ROOT_LEFT = MAPD["root"]["left"]
log("驱动 %d 骨，保持静止 %d 骨，根骨 = %s" % (len(DRIVE), len(KEEP), ROOTB))


# ─────────────────────────────── 解算 ───────────────────────────────

def solve_root_basis(fi):
    """根骨要完整基（含 yaw）：用 髋→肩 当 up、髋线当 left。"""
    up = safe_dir(mid(fi, ROOT_UP["a"]), mid(fi, ROOT_UP["b"]))
    lf = safe_dir(mid(fi, ROOT_LEFT["a"]), mid(fi, ROOT_LEFT["b"]))
    if ROOT_BASIS == "eye":
        # 左方向 = 右眼中心 -> 左眼中心（与髋线同向约定：都指向角色左侧）
        _lfe = safe_dir(mid(fi, [4, 5, 6]), mid(fi, [1, 2, 3]))
        if _lfe is not None:
            lf = _lfe
    if up is None or lf is None:
        return None
    lf = lf - up * lf.dot(up)
    if lf.length < 1e-6:
        return None
    lf.normalize()
    fwd = lf.cross(up)
    if fwd.length < 1e-6:
        return None
    fwd.normalize()
    left = up.cross(fwd).normalized()
    # 该骨静止时的世界基：列 = [localX, localY, localZ]
    Mrest = REST_WORLD[IDX[ROOTB]]
    # 实测该骨架 pelvis 的静止基是 X=上 / Y=朝前 / Z=角色左（不是常规约定，别想当然）
    Mnew = Matrix((
        (up.x, fwd.x, left.x),
        (up.y, fwd.y, left.y),
        (up.z, fwd.z, left.z),
    ))
    E = Mnew @ Mrest.inverted()
    return E.to_quaternion()


# ── 全基驱动：静止参考帧（用于带 "up" 参考的骨，如脚）──────────────────────────
#   为什么需要：aim 驱动只约束"骨段方向"，**绕该方向的 roll 是空出来的**（由最小旋转+父链凑），
#   实测脚被抬高 ~35°、脚掌后翻（和 neck/head 同一个病）。
#   脚的关键点里有 踝(27/28) 趾(31/32) 跟(29/30) ⇒ **脚掌平面是可定的**，于是方向与 roll 都能定。
#   rest 侧没有"脚跟骨"，用"静止时脚是平放的"这条成立的前提取 脚掌法线 ≈ +Z。
FULL_REST = {}
for nm, (a_spec, b_spec, ua, ub, _lat) in DRIVE_FULL.items():
    d_rest = ANCHOR[nm][1]
    if d_rest is None:
        continue
    # 静止帧的"侧向/朝外"参考：优先用 map 里声明的 `lat`（世界系提示向量，取静止矩阵里最贴它的一根轴）；
    # 没声明才回落到"该侧朝外"（左脚 -X / 右脚 +X）。
    # 🔴 别用 "fwd × (踝→跟)" 当静止侧向：踝→跟 ≈ −up，cross 出来正好反号 ⇒ 会翻 180°（脚那次踩过）。
    # 手用 lat=[0,±1,0]：静止 A-pose 手心是薄片正对镜头 ⇒ 手心法线 ≈ ±Y。
    out = Vector(tuple(_lat)) if _lat else (
        Vector((-1.0, 0.0, 0.0)) if nm.startswith("l_") else Vector((1.0, 0.0, 0.0)))
    Rl = REST_WORLD[IDX[nm]]
    cols = [Rl.col[i].copy() for i in range(3)]
    best = max(cols, key=lambda c: abs(c.dot(out)))
    lat0 = best if best.dot(out) > 0 else -best
    lat0.normalize()
    fwd0 = d_rest - lat0 * d_rest.dot(lat0)          # 骨段方向里去掉侧向分量
    if fwd0.length < 1e-6:
        continue
    fwd0.normalize()
    FULL_REST[nm] = (fwd0, lat0, fwd0.cross(lat0))   # (前, 外, 上)


def solve_full_basis(nm, fi):
    """返回该骨【世界系】的目标旋转增量 E_target（相对静止姿势），roll 由 up 参考定死。"""
    if nm not in FULL_REST:
        return None
    a_spec, b_spec, ua, ub, _lat = DRIVE_FULL[nm]
    fwd = safe_dir(mid(fi, a_spec), mid(fi, b_spec))          # 踝→趾
    ref = safe_dir(mid(fi, ua), mid(fi, ub))                  # 踝→跟
    if fwd is None or ref is None:
        return None
    lat = fwd.cross(ref)                                      # 踝→趾 × 踝→跟 = 脚掌平面法线 = 侧向
    if lat.length < 1e-6:
        return None
    lat.normalize()
    fwd = fwd - lat * fwd.dot(lat)                            # 去掉侧向分量，保证正交
    if fwd.length < 1e-6:
        return None
    fwd.normalize()
    upn = fwd.cross(lat)                                      # 右手系：上 = 前 × 外
    if upn.length < 1e-6:
        return None
    upn.normalize()
    f0, l0, u0 = FULL_REST[nm]                                # 静止帧的 (前, 外, 上)
    # 两帧的列都按 (前, 外, 上) 摆 —— 同一个约定，roll 才对齐
    Mnow = Matrix(((fwd.x, lat.x, upn.x), (fwd.y, lat.y, upn.y), (fwd.z, lat.z, upn.z)))
    Mrest = Matrix(((f0.x, l0.x, u0.x), (f0.y, l0.y, u0.y), (f0.z, l0.z, u0.z)))
    return (Mnow @ Mrest.inverted()).to_quaternion()


def solve_frame(fi):
    """返回 {骨名: 相对父的 basis 四元数}。

    🔴 这里**不再接受 yaw 参数**：整体转身已改为"先旋转关键点"（见文件中部 YAW 段）。
       根骨上再加旋转会被子骨的瞄准抵消（骨盆转了、四肢原地）—— 实测过的坑。
    """
    basis = {}
    E = {}
    rq = solve_root_basis(fi)
    if rq is None:
        return None
    E[ROOTB] = rq
    basis[ROOTB] = (REST_WORLD_Q[IDX[ROOTB]].inverted() @ rq @ REST_WORLD_Q[IDX[ROOTB]])

    for nm, (a_spec, b_spec, frac) in DRIVE.items():
        pb = PB[IDX[nm]]
        pnm = pb.parent.name if pb.parent else None
        # 父的 E：父没被驱动就沿链找最近的已解算祖先
        Ep = E.get(pnm)
        if Ep is None:
            q = pnm
            Ep = Quaternion((1, 0, 0, 0))
            while q is not None and q not in E:
                q = PB[IDX[q]].parent.name if PB[IDX[q]].parent else None
            if q is not None:
                Ep = E[q]
        # 🔴 全基骨（脚）：方向 + roll 一起定，**不**走下面的 aim 分支
        if nm in DRIVE_FULL:
            qE = solve_full_basis(nm, fi)
            if qE is None:
                basis[nm] = Quaternion((1, 0, 0, 0))
                E[nm] = Ep
                continue
            rst = REST_WORLD_Q[IDX[nm]]
            basis[nm] = rst.inverted() @ (Ep.inverted() @ qE) @ rst
            E[nm] = qE
            continue
        kind, d_rest = ANCHOR[nm]
        if d_rest is None:
            basis[nm] = Quaternion((1, 0, 0, 0))
            continue
        d_now = safe_dir(mid(fi, a_spec), mid(fi, b_spec))
        if d_now is None:
            basis[nm] = Quaternion((1, 0, 0, 0))
            E[nm] = Ep
            continue
        if kind == "self":
            # 叶骨：静止轴只在世界系有意义，而 d_now 也是世界系 —— 两者都转到父系再比
            tgt = Ep.inverted() @ d_now
            G = d_rest.rotation_difference(tgt)
        else:
            tgt = Ep.inverted() @ d_now
            if 0.0 < frac < 0.999:
                tgt = d_rest.slerp(tgt, frac)
            G = d_rest.rotation_difference(tgt)
        E[nm] = Ep @ G
        rst = REST_WORLD_Q[IDX[nm]]
        basis[nm] = rst.inverted() @ G @ rst
    return basis



def _ground_trf(trf_path, foot_bones=("l_toe0", "r_toe0", "l_foot", "r_foot")):
    """TRF 落地补偿（常量世界 Z 偏移，**幂等**）：先 FK 量出首帧脚底的高度，再整体压到 0。

    为什么放在 TRF 上而不是 Blender 里：实测在脚本里写 pose_bone.location 后导出，
    TRF 的位置轨恒为 0（UE/SW2 线不走这条路径所以没暴露）。
    TRF 的位置轨存的**本来就是世界空间的纯增量**（见 docs/TRF规范.md），所以直接加减即可。
    """
    import sys as _s
    _s.path.insert(0, os.path.join(PROJECT_ROOT, "pipeline", "tools"))
    _s.path.insert(0, os.path.join(PROJECT_ROOT, "pipeline", "common"))
    try:
        from scan_pose_metrics import build, fk
        from trf_compose import read_trf, write_trf
    except Exception as e:
        log("  [warn] 落地补偿跳过（import 失败：%s）" % e)
        return
    skel_json = os.path.join(PROJECT_ROOT, "output", "verify", "bannerlord_skel.json")
    if not os.path.isfile(skel_json):
        log("  [warn] 落地补偿跳过（缺 %s，先跑 dump_skeleton.py）" % skel_json)
        return
    skel = json.load(open(skel_json, encoding="utf-8"))
    bones, rl, invq = build(skel)
    names = [b["name"] for b in bones]
    root3 = np.array([b for b in bones if b["name"] == ROOTB][0]["rest_local"], dtype=float)[:3, :3]
    rootinv = np.linalg.inv(root3)
    t = read_trf(trf_path)
    q = {names[bi]: t.bones[bi][0][1] for bi in range(len(names))}
    p = fk(skel, rl, invq, q, rootinv @ np.array(t.root_pos[0][1]), [ROOTB],
           [b for b in foot_bones if b in names])
    zs = [float(p[b][0][2]) for b in p if b in foot_bones] or [0.0]
    dz = -min(zs)
    if abs(dz) < 1e-4:
        log("落地补偿: TRF 首帧脚底已在 %.4f m，无需补偿" % min(zs))
        return
    t.root_pos = [(f, (v[0], v[1], v[2] + dz)) for (f, v) in t.root_pos]
    write_trf(t, trf_path)
    log("落地补偿(TRF后处理): 首帧脚底 %.4f m -> 整体沿世界 Z 平移 %+.4f m（常量，保留起跳）"
        % (min(zs), dz))



# ─────────────────────────────── 主循环 ───────────────────────────────

src_fps = float(POSE.get("fps") or TGT_FPS)
step = 1.0
if src_fps > 0 and abs(src_fps - TGT_FPS) > 0.5:
    step = src_fps / float(TGT_FPS)
    log("重采样: %.2f fps -> %d fps (step=%.4f)" % (src_fps, TGT_FPS, step))
N_OUT = int(NF / step) if step != 1.0 else NF
N_OUT = max(1, N_OUT)

act = bpy.data.actions.new(NAME)
act.use_fake_user = True
if arm.animation_data is None:
    arm.animation_data_create()
arm.animation_data.action = act
try:
    if hasattr(act, "slots") and len(act.slots) == 0:
        act.slots.new(id_type='OBJECT', name=arm.name)
    if hasattr(act, "slots") and len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
except Exception as e:
    log("  槽位告警（一般无害）: %s" % e)

FOOTB = [n for n in ("l_toe0", "r_toe0", "l_foot", "r_foot") if n in IDX]
n_bad = 0
# 🔴 重采样：**相邻源帧 slerp 插值**，不要「就近取整」。
#    实测（2026-09-28，24fps 源 → 30fps 输出）：就近取样让 106 帧里有 **21 帧是重复帧**
#    （同一帧连播两遍），播放起来就是「走走停停」的顿挫，肉眼像抽搐。
#    step=0.8 时索引序列是 0,1,2,2,3,4,5,6,6,... —— 每隔几帧就卡一下。
#    改成对相邻两帧各自的解算结果做 slerp，输出的 30fps 才是真正平滑的。
for k in range(N_OUT):
    _x = k * step
    fi = min(NF - 1, int(math.floor(_x)))
    _u = _x - fi
    sol = solve_frame(fi)
    if sol is not None and _u > 1e-6:
        _fi1 = min(NF - 1, fi + 1)
        if _fi1 != fi:
            _s1 = solve_frame(_fi1)
            if _s1 is not None:
                for _nm, _q0 in list(sol.items()):
                    _q1 = _s1.get(_nm)
                    if _q1 is not None:
                        sol[_nm] = _q0.slerp(_q1, _u)
    fno = k + 1
    if sol is None:
        n_bad += 1
        for pb in PB:
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = pb.rotation_quaternion if fno > 1 else Quaternion((1, 0, 0, 0))
            pb.keyframe_insert(data_path="rotation_quaternion", frame=fno)
        continue
    for pb in PB:
        pb.rotation_mode = 'QUATERNION'
        if pb.name in KEEP:
            q = Quaternion((1, 0, 0, 0))
        else:
            q = sol.get(pb.name, Quaternion((1, 0, 0, 0)))
        pb.rotation_quaternion = q
        pb.keyframe_insert(data_path="rotation_quaternion", frame=fno)
    if fno == 1:
        bpy.context.view_layer.update()
        try:
            zs = [(arm.matrix_world @ PB[IDX[n]].head).z for n in FOOTB]
            FIRST_FOOT_Z = min(zs) if zs else 0.0
        except Exception:
            FIRST_FOOT_Z = 0.0

if n_bad:
    log("  !! %d 帧解算失败（关键点退化），已留空" % n_bad)

# ---- 根骨位移 ----
# 🔴 2026-09-27 实测结论：**在本脚本里写 pose_bone.location 再导出，TRF 的位置轨会全是 0**
#    （UE/SW2 那两条线不是这么走的，所以没暴露）。落地补偿改为**在 TRF 上做后处理**（见文件末尾
#    `_ground_trf`），那里是纯 Python、可测、且**幂等**（先量再补，Blender 侧侥幸生效也不会补两次）。
log("根位移: %s（落地补偿在 TRF 上做后处理）" % PELVIS)

sc.frame_start, sc.frame_end = 1, N_OUT
sc.render.fps = TGT_FPS
sc.frame_set(1)
os.makedirs(OUTDIR, exist_ok=True)
fbx_dir = os.path.join(OUTDIR, "fbx")
os.makedirs(fbx_dir, exist_ok=True)
out = os.path.join(fbx_dir, NAME + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=out, use_selection=False, object_types={'ARMATURE'},
    add_leaf_bones=False, axis_up='Z', axis_forward='-Y',
    primary_bone_axis='Y', secondary_bone_axis='X',
    apply_unit_scale=True, global_scale=1.0,
    bake_anim=True, bake_anim_use_all_bones=True,
    bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0, path_mode='AUTO')
log("导出(ModKit 规格): %s（%d 帧 @ %d fps = %.3fs）" % (out, N_OUT, TGT_FPS, N_OUT / float(TGT_FPS)))

# ---- FBX -> TRF（最终交付物；语义见 docs/TRF规范.md）----
if not NO_TRF:
    import subprocess
    trf_script = os.path.join(PROJECT_ROOT, "pipeline", "common", "fbx_to_trf.py")
    if os.path.exists(trf_script):
        trf_out = os.path.join(OUTDIR, "trf", NAME + ".trf")
        os.makedirs(os.path.dirname(trf_out), exist_ok=True)
        cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
               "--python", trf_script, "--", "--fbx", out, "--out", trf_out, "--name", NAME]
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8",
                               errors="replace", timeout=900)
            for ln in (r.stdout or "").splitlines():
                if ln.startswith(("CHECK_", "TRF_", "CONVERT_")) or "ERROR" in ln.upper():
                    log("  " + ln.strip())
            if os.path.exists(trf_out):
                if PELVIS == "ground":
                    _ground_trf(trf_out)
                log("TRF: %s" % trf_out)
            else:
                log("  !! TRF 没产出（见上）")
        except Exception as e:
            log("  !! TRF 失败: %s" % e)
log("DONE")

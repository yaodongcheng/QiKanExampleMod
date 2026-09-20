#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""UE5 -> 骑砍2 批量重定向（修好版），两路产出：
   --mode ue  : 用 Mannequin_src.fbx 的网格+骨架 + 29 段源动画（自映射，保真）  -> UE 源 GLB
   --mode bl  : 用 human_lod_4.fbx + 29 段 align 重定向动画                    -> 骑砍2 GLB
   关键修复：① 导入每个 FBX 后都会改掉 scene fps，采样完立刻还原成统一重采样帧率
             ② 按"时间"重采样（p=i/n），保住每段的真实时长（源 25/30fps 混用也不怕）
             ③ align 模式：世界增量 + 逐骨静止对齐
"""
import bpy, sys, os, json, math, argparse, glob
from mathutils import Euler

# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

ROOT   = PROJECT_ROOT
ANIMD  = os.path.join(ROOT, "input", "source", "ue_mannequin", "clips_basic")
MANN   = os.path.join(ROOT, "input", "source", "ue_mannequin", "rig", "Mannequin_src.fbx")
BLFBX  = os.path.join(ROOT, "input", "target", "bannerlord", "human_lod_4.fbx")
UEMAP  = json.load(open(os.path.join(ROOT, "pipeline", "rigs", "ue_mannequin", "map.json"), encoding="utf-8"))["bone_map"]

def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="bl"); ap.add_argument("--out", required=True)
    ap.add_argument("--repro", type=int, default=30)
    ap.add_argument("--pelvis", default="ground")   # ground=逐帧贴地 / src=复制源骨盆位移(飞行用) / none
    ap.add_argument("--bones", default="all")       # all=全部骨（源侧必须 all）/ core=仅映射表内的骨
    ap.add_argument("--twist", default="false")   # 是否把 UE 旁支扭骨映射到骑砍内联扭骨（实测会更差，默认关）
    ap.add_argument("--animdir", default="")        # 空=默认 exported_fbx；也可指向 exported_flight
    ap.add_argument("--clipfile", default="")       # 只烘焙清单里列出的 clip（每行一个名字，缺省=目录内全部）
    return ap.parse_args(a)
args = parse()
REPRO = args.repro
def log(m): print("[ue-batch] %s" % m, flush=True)
def rot3(m): return m.to_3x3().normalized()

ANIM_DIR = args.animdir or ANIMD
_ALL = sorted(os.path.splitext(os.path.basename(p))[0]
              for p in glob.glob(os.path.join(ANIM_DIR, "*.fbx")))
if args.clipfile and os.path.exists(args.clipfile):
    _want = [l.strip() for l in open(args.clipfile, encoding="utf-8") if l.strip()]
    _miss = [w for w in _want if w not in set(_ALL)]
    if _miss:
        log("!! 清单里 %d 条在 animdir 中找不到: %s" % (len(_miss), _miss[:5]))
    CLIPS = [w for w in _want if w in set(_ALL)]
    log("clipfile 生效: %d / 目录 %d" % (len(CLIPS), len(_ALL)))
else:
    CLIPS = _ALL
log("片段 %d 段: %s ..." % (len(CLIPS), ", ".join(CLIPS[:4])))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
BASE_FBX = MANN if args.mode == "ue" else BLFBX
bpy.ops.import_scene.fbx(filepath=BASE_FBX)
base = next(o for o in bpy.data.objects if o.type=='ARMATURE')
log("基底 %s  骨 %d  场景fps=%d" % (os.path.basename(BASE_FBX), len(base.data.bones), sc.render.fps))

POSE = "delta" if args.mode == "ue" else "align"
if args.mode == "ue":
    # !!! Mannequin_src.fbx 的骨架被套在一个 scale=0.01 的空物体下：
    #     结果是"骨骼静止位置在 cm、而动画/导出值在 m"，单位不一致会让整人塌到地面。
    #     直接把骨架从那个空物体上摘下来（保留各自局部变换），三者就处在同一空间了。
    if base.parent is not None:
        print("[ue-batch] 摘掉骨架父级 %s (scale=%s)" % (base.parent.name, tuple(round(v,4) for v in base.parent.scale)))
        base.parent = None
        bpy.context.view_layer.update()   # !!! 必须刷新，否则 matrix_world 还是旧的(带0.01)，基准高度会算错
    # UE 动画把 67 根骨全动了（含扭骨 upperarm_twist_01_*/lowerarm_twist_01_* 与手指）。
    # 只映射 22 根标准骨会漏掉扭骨 -> 手/前臂朝向偏 ~40°。这里全骨自映射，
    # 同一套骨架下用 delta(F=I) 就是精确复现。
    # !!! 修 bug：这里原来写的是 list(UEMAP.values())，而那些是【骑砍的骨名】，
    #     在本(源)骨架里只有 pelvis/head 命中 -> 源侧 GLB 实际只动了 10 根骨
    #     （pelvis、head + 8 根扭骨），上臂/前臂/大腿/小腿/脚/脊柱全冻在 bind pose，
    #     左侧"源"根本不是真源动画。源侧就该用【源骨架自己的骨名】全骨自映射。
    CORE = [b.name for b in base.data.bones]
    want = [b.name for b in base.data.bones] if args.bones=="all" else CORE
    MAP = {b: b for b in want if b in base.data.bones}
    F   = Euler((0,0,0),'XYZ').to_matrix()
else:
    MAP = dict(UEMAP)
    # 补：UE 的扭骨 -> 骑砍的多级扭骨（原映射表把它们放在 target_keep_rest 里丢掉了，
    # 导致小臂/手朝向偏差；这里接上）
    # 实测：UE 扭骨是"旁支叶骨"、骑砍扭骨是"内联骨"，硬接过去会给手部链路多注入一次旋转
    #     含扭骨 6.93° vs 不含 3.30°（010_01）—— 所以默认不映射
    if str(args.twist).lower() in ("1","true","yes"):
        for L in ("l","r"):
            MAP["upperarm_twist_01_"+L] = L+"_upperarm_twist1"
            MAP["lowerarm_twist_01_"+L] = L+"_foretwist1"
    F   = Euler((0,0,math.radians(180)),'XYZ').to_matrix()
MAP = {s:t for s,t in MAP.items() if t in base.data.bones}
log("模式=%s 映射基数=%d F=%s" % (args.mode, len(MAP), "I" if args.mode=="ue" else "Rot(Z,180)"))

tgt_rest_w = {t: rot3(base.matrix_world @ base.data.bones[t].matrix_local) for t in MAP.values()}
order=[]
def _walk(b):
    order.append(b.name)
    for c in b.children: _walk(c)
for b in base.data.bones:
    if b.parent is None: _walk(b)

def mapped_kids(arm, nm, is_src):
    kid=[]; st=[c for c in arm.data.bones[nm].children]
    while st:
        c=st.pop(0)
        if (c.name in MAP) if is_src else (c.name in MAP.values()): kid.append(c.name)
        else: st.extend(c.children)
    return kid
def sub_has(arm, nm, want):
    st=[arm.data.bones[nm]]
    while st:
        b=st.pop()
        if b.name==want: return True
        st.extend(b.children)
    return False
def out_dir(arm, nm, kid):
    v=(arm.matrix_world @ arm.data.bones[kid].matrix_local.translation) - \
      (arm.matrix_world @ arm.data.bones[nm].matrix_local.translation)
    return v if v.length>1e-6 else None

A_CACHE = {}
def align_of(src_arm):
    """逐骨静止对齐修正 A（源静止肢段方向 -> 目标静止肢段方向）"""
    key = tuple(sorted(MAP.items()))
    if key in A_CACHE: return A_CACHE[key]
    A={}
    for s,t in MAP.items():
        if s not in src_arm.data.bones: continue
        ks=mapped_kids(src_arm,s,True); kt=mapped_kids(base,t,False)
        if not ks or not kt: continue
        if len(ks)==1 and len(kt)==1: cs,ct=ks[0],kt[0]
        else:
            cs=next((k for k in ks if sub_has(src_arm,k,"head")),None)
            ct=next((k for k in kt if sub_has(base,k,"head")),None)
            if not cs or not ct: continue
        vs=out_dir(src_arm,s,cs); vt=out_dir(base,t,ct)
        if vs is None or vt is None: continue
        A[t]=((F@vs).normalized()).rotation_difference(vt.normalized())
    log("  静止对齐修正覆盖 %d 骨" % len(A))
    A_CACHE[key]=A
    return A

def rig_span(arm):
    """骨架尺度基准 = 骨盆静止位置 → 头静止位置 的世界距离（人形骨架之间比值即单位换算系数）。
       为什么不能再用某个坐标分量：源(米/厘米混用)·目标(米) 两套骨架的"某个轴分量"根本不是同一物理量，
       实测 base.y/|src.y| = 1.91，而真值(骨盆→头比) = 0.957 —— 差 2 倍，位移会被整体放大 2 倍。"""
    try:
        p = (arm.matrix_world @ arm.data.bones["pelvis"].matrix_local).translation
        h = (arm.matrix_world @ arm.data.bones["head"].matrix_local).translation
        d = (h - p).length
        return d if d > 1e-6 else None
    except Exception:
        return None

GB = [b for b in ("foot_l","foot_r","ball_l","ball_r","l_foot","l_toe0","r_foot","r_toe0") if b in base.pose.bones]
ROOTB = "pelvis"
base_rest_head = (base.matrix_world @ base.data.bones["pelvis"].matrix_local).translation.copy()
src_arm_holder = [None]
ROOTB = "pelvis"

def bake_clip(clip):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ANIM_DIR, clip + ".fbx"))
    U = next(o for o in bpy.data.objects if o.type=='ARMATURE' and o not in before)
    fps_src = sc.render.fps                      # 这个 FBX 自带帧率（25 或 30）
    sc.render.fps = REPRO                        # 统一重采样帧率
    act = U.animation_data.action if U.animation_data else None
    if act is None: return None
    fs, fe = act.frame_range
    dur = (fe - fs)/fps_src
    n = max(1, int(round(dur*REPRO)))
    A = align_of(U) if POSE == "align" else {}
    src_arm_holder[0] = U
    new = bpy.data.actions.new(clip)
    if base.animation_data is None: base.animation_data_create()
    base.animation_data.action = new
    try:
        if hasattr(new,"slots"):
            new.slots.new(id_type='OBJECT', name=base.name); base.animation_data.action_slot = new.slots[0]
    except Exception: pass
    for i in range(n+1):
        p = i/n
        sf = fs + p*(fe-fs)
        sc.frame_set(int(math.floor(sf)), subframe=float(sf-math.floor(sf)))
        bpy.context.view_layer.update()
        W={}
        for s,t in MAP.items():
            if s not in U.pose.bones: continue
            sp = rot3(U.matrix_world @ U.pose.bones[s].matrix)
            rp = rot3(U.matrix_world @ U.data.bones[s].matrix_local)
            R  = F @ (sp @ rp.inverted()) @ F.transposed()
            Aw = A.get(t) if POSE == "align" else None
            W[t] = (R @ Aw.inverted().to_matrix()) @ tgt_rest_w[t] if Aw is not None else (R @ tgt_rest_w[t])
        for t in order:
            if t not in W: continue
            pb=base.pose.bones[t]
            cur=base.matrix_world @ pb.matrix
            nw=W[t].to_4x4(); nw.translation=cur.translation
            pb.matrix = base.matrix_world.inverted() @ nw
            bpy.context.view_layer.update()
            pb.rotation_mode='QUATERNION'
            pb.keyframe_insert(data_path="rotation_quaternion", frame=i)
    ROOTB = "pelvis"
    if args.pelvis == "ground" and GB:
        for i in range(n+1):
            sc.frame_set(i); bpy.context.view_layer.update()
            mz=min((base.matrix_world @ base.pose.bones[b].head).z for b in GB)
            pb=base.pose.bones[ROOTB]; m=pb.matrix.copy(); m.translation.z -= mz
            pb.matrix=m; bpy.context.view_layer.update()
            pb.keyframe_insert(data_path="location", frame=i)
    elif args.pelvis == "src":
        # 飞行类：不贴地，改为复制源骨盆的世界位移增量（保留升降/漂移），
        # 目标骨盆沿用它自己的静止高度
        # 基准必须取"源的静止姿态"而不是首帧：
        # 飞行/闪避类首帧本身就是低姿态，用首帧当基准会让目标站在自己的静止高度上、
        # 整体比源高一截（截图里两人高度对不上就是这个原因）。
        SA = src_arm_holder[0]
        src_rest_head = (SA.matrix_world @ SA.data.bones["pelvis"].matrix_local).translation.copy()
        _bad_v = [0]      # 统计"源竖直位移不合理"的帧数，便于审计
        _fix = [0]        # 统计"最低点单向上限修正"生效的帧数
        # 源(米) 与 目标(厘米) 单位不同，位移增量必须按「骨架尺度比」换算。
        # !!! 修 bug：原来用 base_rest_head.y / |src_rest_head.y|（某个坐标分量），
        #     实测 bl 侧算出 1.9098，而真值（骨盆→头 长度比）是 0.9572 —— 差 2 倍，
        #     导致 ALL src 段（跳跃/攀爬/闪避/倒地…）的骨盆位移被整体放大 2 倍：
        #     人冲出去、垂直过冲、脚穿到地面以下（Anim_CS_DODGE_R 最低脚 -0.72m 就是这么来的）。
        # 单位换算：源 FBX 与目标 base 的【骨骼数据单位】本身不同
        # （实测：Mannequin_src 的 rig_span=68.83 vs 动画 FBX=0.6883，差 100 倍），
        # 所以位移必须按尺度比换算，否则两侧"位移/身高"会差 100 倍。
        _ts, _ss = rig_span(base), rig_span(SA)
        if _ts and _ss:
            unit = _ts / _ss
        else:
            unit = 1.0
        log("  骨盆位移换算 unit=%.5f (目标骨架尺度 %.4f / 源 %.4f)" % (unit, _ts or 0, _ss or 0))
        for i in range(n+1):
            p_ = i/n; sf_ = fs + p_*(fe-fs)
            sc.frame_set(int(math.floor(sf_)), subframe=float(sf_-math.floor(sf_)))
            bpy.context.view_layer.update()
            h = SA.matrix_world @ SA.pose.bones["pelvis"].head
            off_raw = (h - src_rest_head)          # 世界空间位移（物理量，米）
            # 护栏：个别素材的源数据本身不可信（例：Anim_PR_Up 的源骨盆在 1.27s 内从 0.34m 升到 5.77m，
            # Anim_VUp 升到 2.31m）。人体姿态下骨盆相对站立位的竖直位移不会超过 ~1.5m
            # （躺倒约 -0.77m，大跳约 +0.6m），超出即判为源数据异常：
            # 该帧只传水平位移，竖向保持目标的静止高度，避免"人冲到地下/飞上天"。
            if abs(off_raw.z) > 1.5:               # 护栏按【物理量】判断，与单位换算无关
                _bad_v[0] += 1
                off_raw.z = 0.0
            off = off_raw * unit                    # 再换算到 base 所在的数值空间
            pb = base.pose.bones[ROOTB]
            m = pb.matrix.copy()
            # !!! 修 bug：off / base_rest_head 都是【世界坐标】，而 pb.matrix 是【Armature 空间】。
            #     直接赋值会让矩阵依赖 base.matrix_world：源侧 base(Mannequin_src) 带 0.01 缩放，
            #     位移被缩掉 100 倍（4.5m -> 4.5cm）；目标侧 base(human_lod_4) 是单位阵，位移原样保留 4.2m
            #     -> 同一 clip 两侧相差 100 倍，查看器里就是"一前一后"。
            m.translation = base.matrix_world.inverted() @ (base_rest_head + off)
            pb.matrix = m; bpy.context.view_layer.update()
            # 单向上限修正：目标"任何骨的最低点"不得比源同相位的最低点再低 0.05m 以上。
            # 只抬不压（不会把本来正常/更高的段落压下去），因此对姿态无副作用；
            # 专治"人穿到地面以下"（VUp −1.04m / PR_Up −0.47m / Dash −0.36m / R_JU −0.28m 这类）。
            _tm = min((base.matrix_world @ _b.head).z for _b in base.pose.bones)
            _sm = min((SA.matrix_world @ _b.head).z for _b in SA.pose.bones)
            if _tm < _sm - 0.05:
                _fix[0] += 1
                _dz = (_sm - 0.05 - _tm)
                m2 = pb.matrix.copy()
                _w = base.matrix_world @ m2.translation      # armature -> world
                _w.z += _dz                                   # 在世界空间抬升
                m2.translation = base.matrix_world.inverted() @ _w
                pb.matrix = m2; bpy.context.view_layer.update()
            pb.keyframe_insert(data_path="location", frame=i)
    if _bad_v[0]:
        log("     ! 本段有 %d/%d 帧的源竖直位移>1.5m，已按水平位移处理" % (_bad_v[0], n + 1))
    if _fix[0]:
        log("     ! 本段有 %d/%d 帧触发'最低点单向上限修正'（原本比源更低）" % (_fix[0], n + 1))
    new.use_fake_user = True
    # 清掉本段导入进来的临时骨架（保留 base 及其子级）
    keep = set([base]) | set(base.children)
    for o in list(bpy.data.objects):
        if o not in keep:
            try: bpy.data.objects.remove(o, do_unlink=True)
            except Exception: pass
    for a in list(bpy.data.actions):
        if a is not new and a.name != clip and not a.name in CLIPS:
            try: bpy.data.actions.remove(a)
            except Exception: pass
    return n, dur

t0=__import__('time').time()
ok=0
for idx, clip in enumerate(CLIPS,1):
    try:
        r = bake_clip(clip)
        if r: ok+=1
        log("  [%2d/%d] %-24s %s" % (idx, len(CLIPS), clip,
            ("%d 帧 / %.3f s" % r) if r else "跳过"))
    except Exception as e:
        log("  [%2d/%d] %-24s 失败: %s" % (idx, len(CLIPS), clip, e))
log("烘焙完成 %d/%d 段，用时 %.0fs" % (ok, len(CLIPS), __import__('time').time()-t0))

# 只留 base 及其子级
keep = set([base]) | set(base.children)
for o in list(bpy.data.objects):
    if o not in keep:
        try: bpy.data.objects.remove(o, do_unlink=True)
        except Exception: pass
sc.render.fps = REPRO
sc.frame_start, sc.frame_end = 0, 300
os.makedirs(os.path.dirname(args.out), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=args.out, export_format='GLB',
    export_animations=True, export_animation_mode='ACTIONS',
    export_skins=True, export_apply=False, use_selection=False, export_frame_step=1)
log("导出 %s (%.1f MB, fps=%d)" % (args.out, os.path.getsize(args.out)/1048576, sc.render.fps))
log("DONE")

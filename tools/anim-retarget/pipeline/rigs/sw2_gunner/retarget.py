#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
retarget_sw2_to_bannerlord.py
==============================================================================
战国无双2 (G1A/G1M) 动画  ->  骑马与砍杀2 (Bannerlord human_skeleton) 重定向

与 骑砍2动画重定向/ue_to_bannerlord_retarget_world.py 同源（世界空间 rest-aware 增量法），
针对 SW2 骨架的差异点做了三处适配：

  1) 源是 glTF（Noesis 从 G1M/G1A 导出），骨名 bone_<ID>，左右语义靠骨骼静止位置判定：
        SW2 右侧 = -X (bone_2/4/6/24/12/14/16/18)
        SW2 左侧 = +X (bone_3/5/7/25/13/15/17/19)
        SW2 朝向 = -Y (脚尖指向 -Y)；Y-up 已在 glTF 导入时转成 Blender Z-up
  2) 目标骑砍：Z-up、朝向 +Y、右侧 = +X
        => 两套骨架的世界帧差 = Rot(Z, 180deg)。该旋转是正旋转，右->右、左->左，
           因此 **不需要** UE 那条链路里的 X 镜像 (M = diag(-1,1,1))。
  3) 源单位厘米 / 目标米 => 根骨位移 x0.01；且 SW2 glTF 的根骨位移是
     "髋空间增量"（帧0 被归零，直接播会下沉 ~114cm），故取"相对首帧增量"。

两种姿态传递模式：
  --pose delta     世界空间旋转增量 rest-aware（保"转了多少"，目标保持自身 rest 站姿）
  --pose absolute  源骨绝对世界朝向（四肢朝向与源一致，适合举枪/瞄准类动作）
"""
import bpy, sys, os, json, argparse, math
from mathutils import Matrix, Vector, Euler


# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULTS = dict(
    source=os.path.join(PROJECT_ROOT, "input/source/sw2_gunner/L256_GUNNER_anim.gltf"),
    action="p006",
    target=os.path.join(PROJECT_ROOT, "input/target/bannerlord/human_lod_4.fbx"),
    map=os.path.join(HERE, "map.json"),
    outdir=os.path.join(PROJECT_ROOT, "output"),
    name="sw2_gunner_p006",
    pose="align",        # align | delta | absolute
    pelvis="ground",     # ground | delta | none
    face_fix="false",    # SW2 用帧变换已对齐朝向，默认不再整体转 180
    fps=30,
    dump="",             # 非空则 dump 逐帧源/目标世界旋转增量 JSON（供校验）
    debug="false",
)
def log(m): print("[sw2-retarget] %s" % m, flush=True)

def parse_args():
    argv = sys.argv
    if "--" not in argv: return argparse.Namespace(**DEFAULTS)
    a = argv[argv.index("--")+1:]
    ap = argparse.ArgumentParser()
    for k in ("source","action","target","map","outdir","name","pose","pelvis","face_fix","dump","debug"):
        ap.add_argument("--"+k, default=DEFAULTS[k])
    ap.add_argument("--fps", type=int, default=DEFAULTS["fps"])
    ap.add_argument("--no_trf", default="false")
    ns = ap.parse_args(a)
    ns.no_trf = str(getattr(ns, "no_trf", "false")).lower() in ("1","true","yes")
    ns.face_fix = str(ns.face_fix).lower() in ("1","true","yes")
    ns.debug    = str(ns.debug).lower()    in ("1","true","yes")
    return ns

def rot3(m): return m.to_3x3().normalized()

def ordered_bones(arm):
    out=[]
    def walk(b):
        out.append(b.name)
        for c in b.children: walk(c)
    for r in arm.data.bones:
        if r.parent is None: walk(r)
    return out

def assign_action(obj, act):
    if obj.animation_data is None: obj.animation_data_create()
    obj.animation_data.action = act
    try:
        if hasattr(act, "slots") and len(act.slots):
            obj.animation_data.action_slot = act.slots[0]
    except Exception as e:
        log("  slot warn %s" % e)

def main():
    args = parse_args()
    cfg = json.load(open(args.map, encoding="utf-8"))
    mapping = cfg["bone_map"]
    src_root = cfg["meta"]["root_bone_source"]
    src_root_rot = cfg["meta"].get("root_rotation_source", src_root)
    tgt_root = cfg["meta"]["root_bone_target"]
    uscale = float(cfg["meta"].get("unit_scale_root_translation", 1.0))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = args.fps          # 让 glTF 的秒 -> 帧 按 30fps 换算
    log("fps=%d" % args.fps)

    # ---------- 1) 导入源 (glTF) ----------
    log("导入源: %s" % args.source)
    bpy.ops.import_scene.gltf(filepath=args.source)
    src = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    act = bpy.data.actions.get(args.action)
    if act is None:
        cands = [a.name for a in bpy.data.actions if args.action.lower() in a.name.lower()]
        log("!! 找不到动作 %s，候选=%s" % (args.action, cands[:10])); return
    assign_action(src, act)
    fs, fe = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    log("源=%s 动作=%s 帧 %d..%d (%d帧, %.2fs)" % (src.name, args.action, fs, fe, fe-fs+1, (fe-fs)/args.fps))

    # ---------- 2) 导入目标 (FBX) ----------
    before = set(bpy.data.objects)
    log("导入目标: %s" % args.target)
    bpy.ops.import_scene.fbx(filepath=args.target)
    tgt = next(o for o in bpy.data.objects if o.type=='ARMATURE' and o not in before)
    log("目标=%s bones=%d" % (tgt.name, len(tgt.data.bones)))
    # !!! 骑砍2 的 FBX 自带 24fps，导入后会把场景 fps 改回 24；
    #     不重置回 30 的话，导出时帧->秒 会按 24 换算，整段动画慢 25%（1.25x）。
    sc.render.fps = args.fps
    log("重置场景 fps = %d" % sc.render.fps)

    miss_s = [s for s in mapping if s not in src.pose.bones]
    miss_t = [t for t in mapping.values() if t not in tgt.data.bones]
    if miss_s: log("!! 源缺骨 %s" % miss_s)
    if miss_t: log("!! 目标缺骨 %s" % miss_t)
    mapping = {s:t for s,t in mapping.items() if s in src.pose.bones and t in tgt.data.bones}
    log("有效映射 %d 骨" % len(mapping))
    for a in list(bpy.data.actions):
        if a is not act:
            try: bpy.data.actions.remove(a)
            except Exception: pass

    # ---------- 3) 帧变换 F : 源世界 -> 目标世界 ----------
    F = Euler((0.0, 0.0, math.radians(180)), 'XYZ').to_matrix()
    log("帧变换 F = Rot(Z,180) ; 源right(-X)->目标right(+X)")

    tgt_rest_w = {t: rot3(tgt.matrix_world @ tgt.data.bones[t].matrix_local) for t in mapping.values()}
    src_rest_w = {s: rot3(src.matrix_world @ src.data.bones[s].matrix_local) for s in mapping}

    sc.frame_set(fs); bpy.context.view_layer.update()
    src_root_head0 = (src.matrix_world @ src.pose.bones[src_root].matrix).translation.copy()
    tgt_rest_head  = (tgt.matrix_world @ tgt.data.bones[tgt_root].matrix_local).translation.copy()
    log("源根骨首帧 world=%s ; 目标 pelvis rest world=%s" %
        (tuple(round(v,2) for v in src_root_head0), tuple(round(v,3) for v in tgt_rest_head)))

    if tgt.animation_data: tgt.animation_data_clear()
    tgt.animation_data_create()
    newact = bpy.data.actions.new("Retarget_%s" % args.name)
    tgt.animation_data.action = newact
    try:
        if hasattr(newact, "slots"):
            newact.slots.new(id_type='OBJECT', name=tgt.name)
            tgt.animation_data.action_slot = newact.slots[0]
    except Exception as e:
        log("slot warn %s" % e)

    frames = list(range(fs, fe+1))
    tgt_order = ordered_bones(tgt)
    dump_rows = []

    # --- 肢体段方向（与骨骼轴向约定无关的度量）的父骨查找 ---
    def mapped_parent(arm, nm, is_src):
        b = arm.data.bones.get(nm)
        while b and b.parent:
            b = b.parent
            if is_src:
                if b.name in mapping: return b.name
            else:
                if b.name in mapping.values(): return b.name
        return None
    SEGPAR = {}       # tgt_name -> tgt_parent_name
    SEGPAR_SRC = {}   # src_name -> src_parent_name
    T2S = {t: s for s, t in mapping.items()}
    for s, t in mapping.items():
        ps = mapped_parent(src, s, True)
        if ps: SEGPAR_SRC[s] = ps
        pt = mapped_parent(tgt, t, False)
        if pt: SEGPAR[t] = pt
    def seg_dir(arm, nm, pmap, F=None):
        p = pmap.get(nm)
        if not p: return None
        v = (arm.matrix_world @ arm.pose.bones[nm].head) - (arm.matrix_world @ arm.pose.bones[p].head)
        if v.length < 1e-9: return None
        v = v.normalized()
        if F is not None: v = F @ v
        return [round(v.x,6), round(v.y,6), round(v.z,6)]
    log("段方向可度量骨: %d / %d" % (len(SEGPAR), len(mapping)))

    # rest 基线（量化"源 rest vs 目标 rest"的肢体朝向差）
    rest_dirs = {}
    for s, t in mapping.items():
        if t not in SEGPAR or s not in SEGPAR_SRC: continue
        vs = (src.matrix_world @ src.data.bones[s].matrix_local.translation) -              (src.matrix_world @ src.data.bones[SEGPAR_SRC[s]].matrix_local.translation)
        vt = (tgt.matrix_world @ tgt.data.bones[t].matrix_local.translation) -              (tgt.matrix_world @ tgt.data.bones[SEGPAR[t]].matrix_local.translation)
        vs = (F @ vs).normalized(); vt = vt.normalized()
        c = max(-1.0, min(1.0, vs.dot(vt)))
        rest_dirs[t] = {"src": [round(x,6) for x in vs], "tgt": [round(x,6) for x in vt],
                        "rest_gap_deg": round(math.degrees(math.acos(c)), 2)}

    # --- rest 对齐修正 A(i)：源静止肢段方向(F旋转后) -> 目标静止肢段方向 ---
    #   肢段方向 = 该骨 head -> 其"已映射子骨" head；叶子骨无出向 -> 不做对齐(用纯增量)
    OUT_SRC = {}   # src -> 出向单位向量(源世界系)
    OUT_TGT = {}   # tgt -> 出向单位向量(目标世界系)
    A_align = {}   # tgt -> 对齐四元数(最小旋转, 源->目标)
    def mapped_kids(arm, nm, is_src):
        kid = []; stack = [c for c in arm.data.bones[nm].children]
        while stack:
            c = stack.pop(0)
            if (c.name in mapping) if is_src else (c.name in mapping.values()):
                kid.append(c.name)          # 命中最近的映射子骨后不再下钻
            else:
                stack.extend(c.children)
        return kid
    def subtree_has(arm, nm, want):
        stack = [arm.data.bones[nm]]
        while stack:
            b = stack.pop()
            if b.name == want: return True
            stack.extend(b.children)
        return False
    def out_dir(arm, nm, kid):
        v = (arm.matrix_world @ arm.data.bones[kid].matrix_local.translation) -             (arm.matrix_world @ arm.data.bones[nm].matrix_local.translation)
        return v if v.length > 1e-6 else None

    for s, t in mapping.items():
        b = src.data.bones.get(s); tb = tgt.data.bones.get(t)
        if not b or not tb: continue
        ks = mapped_kids(src, s, True); kt = mapped_kids(tgt, t, False)
        if not ks or not kt: continue
        if len(ks) == 1 and len(kt) == 1:
            cs, ct = ks[0], kt[0]
        else:
            # 多分支骨(pelvis/spine2)：只认"躯干延续"子骨(子树含 head)，否则不做对齐
            cs = next((k for k in ks if subtree_has(src, k, "bone_11")), None)
            ct = next((k for k in kt if subtree_has(tgt, k, "head")), None)
            if not cs or not ct: continue
        vs = out_dir(src, s, cs); vt = out_dir(tgt, t, ct)
        if vs is None or vt is None: continue
        vs = (F @ vs).normalized(); vt = vt.normalized()
        OUT_SRC[s] = vs; OUT_TGT[t] = vt
        A_align[t] = vs.rotation_difference(vt)      # 源 -> 目标
    log("对齐修正覆盖骨: %s" % sorted(A_align))
    if args.pose == "align":
        log("align 模式: 已算 %d 根骨的对齐修正 A" % len(A_align))

    for f in frames:
        sc.frame_set(f); bpy.context.view_layer.update()
        W = {}
        for s, t in mapping.items():
            # pelvis 的旋转改取"躯干支基骨"(bone_8)，位移仍来自总根 bone_0
            s_use = src_root_rot if (t == tgt_root and s == src_root and src_root_rot in src.pose.bones) else s
            sp = rot3(src.matrix_world @ src.pose.bones[s_use].matrix)
            rest_pre = rot3(src.matrix_world @ src.data.bones[s_use].matrix_local)
            R = F @ (sp @ rest_pre.inverted()) @ F.transposed()          # 世界增量(目标帧)
            if args.pose == "absolute":
                W[t] = F @ sp @ F.transposed()
            elif args.pose == "align":
                A = A_align.get(t)
                W[t] = (R @ A.inverted().to_matrix()) @ tgt_rest_w[t] if A is not None \
                       else (R @ tgt_rest_w[t])
            else:
                W[t] = R @ tgt_rest_w[t]
        for t in tgt_order:
            if t not in W: continue
            pb = tgt.pose.bones[t]
            cur = tgt.matrix_world @ pb.matrix
            new = W[t].to_4x4()
            new.translation = cur.translation
            pb.matrix = tgt.matrix_world.inverted() @ new
            bpy.context.view_layer.update()
            pb.rotation_mode = 'QUATERNION'
            pb.keyframe_insert(data_path="rotation_quaternion", frame=f)

        if args.pelvis == "delta" and tgt_root in tgt.pose.bones:
            head = (src.matrix_world @ src.pose.bones[src_root].matrix).translation
            off = F @ ((head - src_root_head0) * uscale)
            pb = tgt.pose.bones[tgt_root]
            m = pb.matrix.copy()
            m.translation = tgt_rest_head + off
            pb.matrix = m
            bpy.context.view_layer.update()
            pb.keyframe_insert(data_path="location", frame=f)

        if args.dump:
            row = {"f": f}
            for s, t in sorted(mapping.items(), key=lambda kv: kv[1]):
                sp = rot3(src.matrix_world @ src.pose.bones[s].matrix)
                tp = rot3(tgt.matrix_world @ tgt.pose.bones[t].matrix)
                sp_f = F @ sp @ F.transposed()          # 源绝对朝向(换到目标帧)
                sd = F @ (sp @ src_rest_w[s].inverted()) @ F.transposed()
                td = tp @ tgt_rest_w[t].inverted()
                row[t] = {"src": [round(x,6) for r in sd for x in r],
                          "tgt": [round(x,6) for r in td for x in r],
                          "src_abs": [round(x,6) for r in sp_f for x in r],
                          "tgt_abs": [round(x,6) for r in tp for x in r]}
            for s2, t2 in mapping.items():
                if t2 not in SEGPAR: continue
                row[t2]["src_dir"] = seg_dir(src, s2, SEGPAR_SRC, F)
                row[t2]["tgt_dir"] = seg_dir(tgt, t2, SEGPAR)
            dump_rows.append(row)

    # ---------- 4) 贴地 ----------
    if args.pelvis == "ground" and tgt_root in tgt.pose.bones:
        ground_bones = [b for b in ("l_toe0","r_toe0","l_foot","r_foot") if b in tgt.pose.bones]
        log("自动贴地 (脚最低点 -> 地面 0)")
        for f in frames:
            sc.frame_set(f); bpy.context.view_layer.update()
            minz = min((tgt.matrix_world @ tgt.pose.bones[b].head).z for b in ground_bones)
            pb = tgt.pose.bones[tgt_root]
            m = pb.matrix.copy()
            m.translation.z -= minz
            pb.matrix = m
            bpy.context.view_layer.update()
            pb.keyframe_insert(data_path="location", frame=f)
        log("贴地完成")

    # ---------- 5) 朝向整体修正（SW2 默认不需要）----------
    if args.face_fix:
        log("朝向修正: 目标绕Z 180deg (apply)")
        tgt.rotation_mode='XYZ'; tgt.rotation_euler[2]=math.radians(180)
        try:
            bpy.ops.object.select_all(action='DESELECT')
            tgt.select_set(True); bpy.context.view_layer.objects.active=tgt
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        except Exception as e: log("rot apply err %s" % e)

    # ---------- 调试：导出前打印姿态实况 ----------
    if args.debug:
        from mathutils import Vector as _V
        mesh = next((o for o in tgt.children if o.type=='MESH'), None) or \
               next((o for o in bpy.data.objects if o.type=='MESH'), None)
        def _ax(m):
            r=(m.to_3x3())
            return "X=(%.2f,%.2f,%.2f) Y=(%.2f,%.2f,%.2f)"%(r.col[0].x,r.col[0].y,r.col[0].z,r.col[1].x,r.col[1].y,r.col[1].z)
        for f in (fs, fs+(fe-fs)//4, fs+(fe-fs)//2, fe):
            sc.frame_set(f); bpy.context.view_layer.update()
            pz = (tgt.matrix_world @ tgt.pose.bones[tgt_root].head).z
            fz = min((tgt.matrix_world @ tgt.pose.bones[b].head).z
                     for b in ("l_toe0","r_toe0","l_foot","r_foot") if b in tgt.pose.bones)
            zs = ""
            if mesh:
                dg = bpy.context.evaluated_depsgraph_get(); eo = mesh.evaluated_get(dg)
                bb = [eo.matrix_world @ _V(c) for c in eo.bound_box]
                zs = "meshZ=[%.3f,%.3f]" % (min(p.z for p in bb), max(p.z for p in bb))
            log("  DBG f%-3d pelvis_z=%.3f foot_z=%.3f %s" % (f, pz, fz, zs))
            log("      pelvis world axes %s" % _ax(tgt.matrix_world @ tgt.pose.bones[tgt_root].matrix))
            log("      pelvis loc=%s" % (tuple(round(v,4) for v in tgt.pose.bones[tgt_root].location),))

    # ---------- 6) 导出（只保留目标骨架 + 其网格 + 目标 action）----------
    try: newact.use_fake_user = True
    except Exception: pass
    for a in list(bpy.data.actions):
        if a is not newact:
            try: bpy.data.actions.remove(a)
            except Exception: pass
    tgt.animation_data.action = newact
    try:
        if hasattr(newact, "slots") and len(newact.slots):
            tgt.animation_data.action_slot = newact.slots[0]
    except Exception: pass
    log("导出前 action 列表: %s" % [a.name for a in bpy.data.actions])

    keep = set([tgt])
    for c in tgt.children: keep.add(c)
    for o in list(bpy.data.objects):
        if o not in keep:
            try:
                if o.type == 'ARMATURE' and o.animation_data: o.animation_data_clear()
            except Exception: pass
            bpy.data.objects.remove(o, do_unlink=True)
    log("清理后保留对象: %s" % [o.name for o in bpy.data.objects])

    # ---------- ModKit 规格导出（骑砍 human_skeleton 28 骨）----------
    # 规格来源：reexport_for_modkit.py / 自定义战斗.md「第1步：造动画」
    #   ① object_types={'ARMATURE'} 只导骨架，不把 mesh 塞进动画文件
    #   ② add_leaf_bones=False      不补 *_end 叶骨（否则 28+5=33，ModKit 报骨骼数不一致）
    #   ③ 根节点改名 human_skeleton_notused（让引擎忽略骨架、只吃动画）
    #   ④ Z-up + ⑤ 单位 cm（unit scale 0.01）
    tgt.name = "human_skeleton_notused"; tgt.data.name = "human_skeleton_notused"
    sc.unit_settings.system = 'METRIC'; sc.unit_settings.scale_length = 1.0
    sc.frame_start, sc.frame_end = fs, fe
    os.makedirs(args.outdir, exist_ok=True)
    out = os.path.join(args.outdir, args.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=False,
        object_types={'ARMATURE'},
        add_leaf_bones=False,
        axis_up='Z', axis_forward='-Y',
        primary_bone_axis='Y', secondary_bone_axis='X',
        apply_unit_scale=True, global_scale=1.0,
        bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='AUTO')
    log("导出(ModKit 规格): %s" % out)
    # ---------- 最后一步：FBX → TRF（ModKit 用的骨骼动画容器）----------
    # 语义要点（见 docs/TRF规范.md）：TRF 存【绝对局部变换】不是增量；
    # 骨序=唯一契约（不存骨骼名，纯索引）；时间列=Blender 帧号（决定 ModKit 的 Source1/2）。
    if not args.no_trf:
        import subprocess
        trf_script = os.path.join(PROJECT_ROOT, "pipeline/common/fbx_to_trf.py")
        if os.path.exists(trf_script):
            trf_out = os.path.join(args.outdir, args.name + ".trf")
            # 🔴 --name = TRF 第 3 行的【动画名】（= 本次输出名，如 fly_A_Flight_Idle_A）。
            #    不传会退化成骨架对象名 human_skeleton_notused → ModKit 里所有动画资源撞名。
            cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
                   "--python", trf_script, "--", "--fbx", out, "--out", trf_out,
                   "--name", args.name]
            try:
                r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
                for ln in (r.stdout or "").splitlines():
                    if ln.startswith(("CHECK_", "TRF_", "CONVERT_")) or "ERROR" in ln.upper():
                        log("  " + ln.strip())
                log("导出 TRF: %s %s" % (trf_out, "OK" if os.path.exists(trf_out) else "!! 未生成"))
            except Exception as e:
                log("!! TRF 导出失败: %s" % e)
        else:
            log("!! 找不到 TRF 转换脚本: %s" % trf_script)

    if args.dump:
        json.dump({"meta":{"name":args.name,"pose":args.pose,"frames":[fs,fe],
                           "map":args.map,"bones":sorted(mapping.values()),
                           "seg_parent":SEGPAR,"seg_bones":sorted(SEGPAR)},
                   "rest_dirs":rest_dirs,
                   "rows":dump_rows},
                  open(args.dump,"w",encoding="utf-8"), ensure_ascii=False)
        log("dump: %s (%d 帧)" % (args.dump, len(dump_rows)))
    log("ALL DONE")

if __name__ == "__main__":
    main()

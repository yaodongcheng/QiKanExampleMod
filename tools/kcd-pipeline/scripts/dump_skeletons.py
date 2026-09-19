# dump_skeletons.py —— 两侧骨架对照 dump（KCD 亨利源件 / 骑砍官方 human_skeleton）
#
#   ① 内存补丁修 Blender FBX 导入器的 KeyError: None（KCD 的 FBX 直接导会崩）
#   ② 打印每根骨的「骨名 + 父骨 + 世界坐标（头/尾）」，供人工复核映射
#   ③ `pair` 模式：两侧一起导，按映射表 json 打印
#        · 逐关节残差（米）
#        · 逐解剖段的「源长 / 目标长 / 比值」→ 标定缩放因子
#        · 逐段的「方向夹角（做完全局翻面后）」→ 标定重定向旋转
#
# 用法:
#   blender -b --factory-startup --python dump_skeletons.py -- kcd   [--filter Arm] [--csv out.csv]
#   blender -b --factory-startup --python dump_skeletons.py -- bl    [--filter bip01_] [--csv out.csv]
#   blender -b --factory-startup --python dump_skeletons.py -- pair  [--map 映射.json]
#   （无参数 = both：先 dump KCD 再 dump 骑砍）
#
# 🔴 单位坑：官方 human_skeleton.fbx 的 FBX 单位标 centimeter、数值其实是米。
#    Blender 按标称换算 → 头骨落到 z≈0.0157。本脚本用 --skel-k（默认 100）把它乘回米。
import bpy, sys, os, csv, json, inspect, math
from mathutils import Vector

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

_here = os.path.dirname(os.path.abspath(__file__))
KCD_SRC = os.path.normpath(os.path.join(_here, "..", "..", "..", "Debug", "offline", "_kcd_recon", "NPC_Henry.fbx"))
DEFAULT_MAP = os.path.normpath(os.path.join(_here, "..", "map_kcd_henry.json"))
SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"


def get(args, key, default=None):
    if key in args:
        i = args.index(key)
        return args[i + 1] if i + 1 < len(args) else default
    return default


MODE = ARGV[0] if ARGV and not ARGV[0].startswith("--") else "both"
FILTER = get(ARGV, "--filter")
CSVOUT = get(ARGV, "--csv")
SKEL_K = float(get(ARGV, "--skel-k", "100"))
KCD_LEAF = "--kcd-leaf" in ARGV          # 保留 FBX 导入器补的叶骨（默认丢弃）
MAPFILE = get(ARGV, "--map", DEFAULT_MAP)
FLIP = get(ARGV, "--flip", None)          # z180 / ymirror / none（缺省取 map json 的 "flip"，再缺省 z180）


# ------------------------------------------------------------------ 导入器补丁
def patch_importer():
    """KCD 的 FBX 缺 armature_setup 登记 → Blender 导入器 KeyError: None。照抄 _kcd_probe.py。"""
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    orig = src

    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: morph without FullWeights")
        print("[KCDPATCH] morph assert patched")

    bad2 = "                    (mmat, amat) = mesh.armature_setup[self]"
    good2 = (
        "                    if self not in mesh.armature_setup:\n"
        "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
        "                    (mmat, amat) = mesh.armature_setup[self]"
    )
    if bad2 in src:
        src = src.replace(bad2, good2)
        print("[KCDPATCH] armature_setup lookup patched")
    else:
        print("[KCDPATCH] !! armature_setup line NOT FOUND (importer changed?)")

    if src != orig:
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


def apply_all(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def import_kcd(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False, ignore_leaf_bones=not KCD_LEAF)
    return next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)


def import_bl(path, k=1.0):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, global_scale=k)
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    if arm:
        apply_all(arm)
    return arm


def bone_rows(arm):
    """[(name, parent, head_w, tail_w, length)]，世界坐标"""
    out = []
    for b in arm.data.bones:
        head = arm.matrix_world @ b.head_local
        tail = arm.matrix_world @ b.tail_local
        out.append((b.name, b.parent.name if b.parent else "-",
                    tuple(head), tuple(tail), (tail - head).length))
    return out


def local_of(arm, name):
    """骨空间（armature 对象空间，未乘 matrix_world）坐标 —— 骨架自身单位"""
    b = arm.data.bones.get(name)
    return None if b is None else tuple(b.head_local)


def dump(arm, tag, rows=None):
    rows = rows if rows is not None else bone_rows(arm)
    sel = [r for r in rows if (not FILTER or FILTER.lower() in r[0].lower())]
    print("\n===== %s : %d 骨（显示 %d）=====" % (tag, len(rows), len(sel)))
    print("对象变换 scale=%s  rot_deg=%s  loc=%s"
          % (tuple(round(v, 6) for v in arm.matrix_world.to_scale()),
             tuple(round(math.degrees(v), 3) for v in arm.matrix_world.to_euler()),
             tuple(round(v, 6) for v in arm.matrix_world.to_translation())))
    print("%-40s %-34s %-30s %-30s %8s" % ("bone", "parent", "head(world)", "head(骨空间)", "len"))
    for n, p, h, t, L in sel:
        lo = local_of(arm, n)
        print("%-40s %-34s (%8.4f,%8.4f,%8.4f) (%8.3f,%8.3f,%8.3f) %8.4f"
              % (n[:40], p[:34], h[0], h[1], h[2], lo[0], lo[1], lo[2], L))
    if CSVOUT:
        with open(CSVOUT, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["bone", "parent", "head_x", "head_y", "head_z",
                        "tail_x", "tail_y", "tail_z", "length",
                        "local_x", "local_y", "local_z"])
            for n, p, h, t, L in rows:
                lo = local_of(arm, n) or (0.0, 0.0, 0.0)
                w.writerow([n, p] + ["%.6f" % v for v in h] + ["%.6f" % v for v in t]
                           + ["%.6f" % L] + ["%.6f" % v for v in lo])
        print("   -> %s" % CSVOUT)
    sys.stdout.flush()
    return rows


# ------------------------------------------------------------------ pair 模式
def flip_vec(v, mode):
    """把源件坐标翻到骑砍朝向。z180 = 绕 Z 转 180°（左右+前后同时翻，是**刚体旋转**，不改绕序）"""
    if mode == "z180":
        return Vector((-v.x, -v.y, v.z))
    if mode == "ymirror":
        return Vector((v.x, -v.y, v.z))
    return Vector(v)


def angle_between(a, b):
    if a.length < 1e-9 or b.length < 1e-9:
        return float("nan")
    c = max(-1.0, min(1.0, a.normalized().dot(b.normalized())))
    return math.degrees(math.acos(c))


def pair_mode():
    patch_importer()
    cfg = {}
    if MAPFILE and os.path.exists(MAPFILE):
        cfg = json.load(open(MAPFILE, encoding="utf-8"))
    mp = cfg.get("map", cfg) if isinstance(cfg, dict) else {}
    segs = cfg.get("segments", []) if isinstance(cfg, dict) else []
    flip = FLIP or cfg.get("flip", "z180")
    print("== 映射表 %s（%d 条，flip=%s）==" % (MAPFILE, len(mp), flip))

    kcd = import_kcd(KCD_SRC)
    if kcd is None:
        print("!! KCD 骨架导入失败"); sys.exit(2)
    kr = {r[0]: r for r in bone_rows(kcd)}
    # 🔴 父骨表必须在导入骑砍骨架**之前**取：import_bl 里 read_factory_settings 会清场，
    #    之后再用 kcd 对象 = ReferenceError: StructRNA ... has been removed
    par = {r[0]: r[1] for r in kr.values()}
    print("== KCD 骨架 %s：%d 骨（%s）  对象变换 %s ==" % (kcd.name, len(kr),
          "保留叶骨" if KCD_LEAF else "丢弃叶骨", [round(v, 6) for v in kcd.matrix_world.to_scale()]))

    bl = import_bl(SKEL, SKEL_K)
    if bl is None:
        print("!! 骑砍骨架导入失败"); sys.exit(2)
    br = {r[0]: r for r in bone_rows(bl)}
    print("== 骑砍骨架 %s：%d 骨（global_scale=%g）==" % (bl.name, len(br), SKEL_K))

    # ---- 1) 逐关节残差 ----
    # 🔴 原始残差被两件事抬高，都不是映射错误：
    #    ① 两套 rig 的**盆骨原点位置**差 ~4.3cm（源件 Hips 头 y=-0.0427，即骨盆原点整体偏前）→ 用去盆骨平移列消掉
    #    ② 四肢**静止姿态**不同（源件手臂比骑砍再低 ~14.5°）→ 远端关节（手/指/趾）天然偏大，靠段长比 0.98~1.00 反证映射本身正确
    p_k = flip_vec(Vector(kr["Hips"][2]), flip) if "Hips" in kr else Vector()
    p_b = Vector(br["bip01_pelvis_0"][2]) if "bip01_pelvis_0" in br else Vector()
    print("\n---- 1) 映射表逐关节残差（米）----")
    print("%-38s %-26s %-30s %-26s %8s %8s"
          % ("KCD 骨", "KCD 翻面后(m)", "骑砍骨", "骑砍(m)", "原始", "去盆骨"))
    res = []
    for k, v in mp.items():
        if k not in kr or v not in br:
            print("%-38s %-26s %-30s   MISSING(%s)" % (k[:38], "", v[:30],
                  "KCD" if k not in kr else "骑砍"))
            continue
        kh = flip_vec(Vector(kr[k][2]), flip)
        bh = Vector(br[v][2])
        d0 = (kh - bh).length
        d1 = ((kh - p_k) - (bh - p_b)).length
        res.append((k, v, d0, d1))
        print("%-38s (%7.4f,%7.4f,%7.4f) %-30s (%7.4f,%7.4f,%7.4f) %8.4f %8.4f"
              % (k[:38], kh.x, kh.y, kh.z, v, bh.x, bh.y, bh.z, d0, d1))
    if res:
        for i, tag in ((2, "原始"), (3, "去盆骨平移")):
            ds = sorted(r[i] for r in res)
            print("  %s统计：n=%d  中位 %.3f  均值 %.3f  最大 %.3f  (>0.05m 有 %d 条)"
                  % (tag, len(ds), ds[len(ds) // 2], sum(ds) / len(ds), ds[-1],
                     sum(1 for d in ds if d > 0.05)))

    # ---- 1b) 全骨覆盖：显式映射 + 父链兜底；没兜到 = 会被丢弃 ----
    anchor = {}
    for n in par:
        if n in mp and mp[n] in br:
            anchor[n] = (n, mp[n]); continue
        p, hit = par.get(n), None
        while p and p != "-":
            if p in anchor:
                hit = anchor[p]; break
            p = par.get(p)
        if hit:
            anchor[n] = hit
    direct = [n for n in par if n in mp and mp[n] in br]
    fb = [n for n in par if n not in direct and n in anchor]
    dead = sorted(n for n in par if n not in anchor)
    print("\n---- 1b) 全骨覆盖（显式 %d / 父链兜底 %d / 丢弃 %d / 共 %d）----"
          % (len(direct), len(fb), len(dead), len(par)))
    grp = {}
    for n in fb:
        a = anchor[n][0]
        pre = n
        while pre and pre[-1].isdigit():
            pre = pre[:-1]
        grp.setdefault((anchor[n][1], a, pre.rstrip("_")), []).append(n)
    for (tgt, anc, pre), lst in sorted(grp.items(), key=lambda x: -len(x[1]))[:20]:
        print("  %-24s <- %-40s  %4d 根" % (tgt, "%s (前缀 %s*)" % (anc, pre), len(lst)))
    if dead:
        print("  丢弃（无映射祖先，权重会被丢掉）：%s" % dead)

    # ---- 2) 解剖段长比（标定缩放）----
    if segs:
        print("\n---- 2) 解剖段长比（标定缩放；比 = 骑砍段长 / 源段长）----")
        print("%-26s %-14s %14s %14s %10s %9s"
              % ("段", "侧", "源长(m)", "骑砍长(m)", "比", "方向夹角°"))
        ratios, angs = [], []
        for s in segs:
            ka, kb, ba, bb, label = s[0], s[1], s[2], s[3], s[4]
            use = s[5] if len(s) > 5 else True    # False = 两侧关节定义不同(锁骨/腰)，不进统计
            if ka not in kr or kb not in kr or ba not in br or bb not in br:
                print("%-26s   MISSING" % label); continue
            va = flip_vec(Vector(kr[ka][2]), flip)
            vb = flip_vec(Vector(kr[kb][2]), flip)
            sv, bv = vb - va, Vector(br[bb][2]) - Vector(br[ba][2])
            r = bv.length / sv.length if sv.length > 1e-9 else float("nan")
            ang = angle_between(sv, bv)
            if use:
                ratios.append((label, r)); angs.append((label, ang))
            print("%-26s %-14s %14.4f %14.4f %10.4f %9.2f%s"
                  % (label, ka + "->" + kb, sv.length, bv.length, r, ang,
                     "" if use else "   (定义不同,不进统计)"))
        for tag, lst in (("段长比", ratios), ("方向夹角", angs)):
            vs = sorted(v for _, v in lst)
            if vs:
                print("  %s统计：n=%d  最小 %.4f  中位 %.4f  最大 %.4f  离散(极差/中位) %.1f%%"
                      % (tag, len(vs), vs[0], vs[len(vs) // 2], vs[-1],
                         100.0 * (vs[-1] - vs[0]) / max(1e-9, vs[len(vs) // 2])))
        # 躯干整体（高度）比：源 Hips->Head 与骑砍 pelvis->head
        if "Hips" in kr and "Hips" in mp and "bip01_head_13" in br:
            pass
    sys.stdout.flush()


def main():
    if MODE == "pair":
        pair_mode(); return
    patch_importer()
    if MODE in ("kcd", "both"):
        arm = import_kcd(KCD_SRC)
        if arm is None:
            print("!! KCD 骨架导入失败"); sys.exit(2)
        dump(arm, "KCD 亨利 %s" % os.path.basename(KCD_SRC))
    if MODE in ("bl", "both"):
        arm = import_bl(SKEL, SKEL_K)
        if arm is None:
            print("!! 骑砍骨架导入失败"); sys.exit(2)
        dump(arm, "骑砍 human_skeleton (k=%g)" % SKEL_K)
    sys.stdout.flush()


main()

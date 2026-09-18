# 一次性探针：量"眼球相对眼窝边缘"的深度关系，给 v10 定眼球前移量。
#   原版 head_female_a：眼睛贴片前点 y=0.1370，量它的眼窝开口边缘 → 得到"贴片比边缘靠前/靠后多少"
#   我们的头：量同样的边缘 → 套用同一个关系，算出眼球前点应该在哪
# 用法: blender --background --python _probe_eye_depth.py
import bpy, bmesh, sys, inspect

# 🔴 TWT/原版 FBX 带 morph 但缺 FullWeights → Blender 导入器断言崩溃。
#    内存级补丁：读源码、把 assert 换掉、重新 exec（不落盘改安装文件）。范本 _probe_vanilla_head.py
def patch_fbx_importer():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
        print("[patch] import_fbx 断言已内存级替换")
    else:
        print("[patch] 未找到断言（版本可能已改）")
patch_fbx_importer()

VANILLA = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\自定义头\core_game\fbx\head\head_female_a.fbx"
OURS    = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v9.fbx"

EYE_X = 0.075      # 眼窝区域：|x| 上限
EYE_Z = (1.640, 1.715)
EYE_Y = 0.02       # 只看前脸（排除后脑）


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o.type == 'MESH']


def shell_of(objs, mat_suffix):
    for ob in objs:
        for m in ob.data.materials:
            if m and m.name.lower().endswith(mat_suffix):
                return ob
    return None


def rim_stats(ob, label):
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bound = [e for e in bm.edges if len(e.link_faces) == 1]
    pts = []
    for e in bound:
        for v in e.verts:
            p = ob.matrix_world @ v.co
            if abs(p.x) < EYE_X and EYE_Z[0] < p.z < EYE_Z[1] and p.y > EYE_Y:
                pts.append(p)
    # 去重（同一顶点被两条边引用）
    uniq = {}
    for p in pts:
        uniq[(round(p.x, 5), round(p.y, 5), round(p.z, 5))] = p
    pts = list(uniq.values())
    if not pts:
        print("  %s: 眼窝区域没找到边界边（开口可能被缝合）" % label)
        bm.free()
        return None
    ys = sorted(p.y for p in pts)
    zs = sorted(p.z for p in pts)
    # 按 z 分上下眼睑：上 1/3 / 下 1/3
    z_lo, z_hi = zs[0], zs[-1]
    upper = [p for p in pts if p.z > z_lo + (z_hi - z_lo) * 0.66]
    lower = [p for p in pts if p.z < z_lo + (z_hi - z_lo) * 0.33]
    print("  %s: 开口边缘 %d 点  y[%.4f,%.4f] (开口前沿 %.4f)  z[%.4f,%.4f]  x[%.4f,%.4f]"
          % (label, len(pts), ys[0], ys[-1], ys[-1], z_lo, z_hi,
             min(p.x for p in pts), max(p.x for p in pts)))
    if upper:
        uy = sorted(p.y for p in upper)
        print("      上眼睑边缘 y[%.4f,%.4f]（最大 = 最靠前 %.4f）" % (uy[0], uy[-1], uy[-1]))
    if lower:
        ly = sorted(p.y for p in lower)
        print("      下眼睑边缘 y[%.4f,%.4f]（最大 = 最靠前 %.4f）" % (ly[0], ly[-1], ly[-1]))
    bm.free()
    return max(ys)


print("=" * 72)
print("【原版 head_female_a】")
objs = load(VANILLA)
sh = shell_of(objs, "head_female_a")
van_rim = rim_stats(sh, "脸壳") if sh else None
eye = shell_of(objs, "eye_mat") or shell_of(objs, "eye")
if eye:
    ys = [(eye.matrix_world @ v.co).y for v in eye.data.vertices]
    zs = [(eye.matrix_world @ v.co).z for v in eye.data.vertices]
    print("  眼球件: 前点 y=%.4f  z[%.4f,%.4f]" % (max(ys), min(zs), max(zs)))
    if van_rim:
        print("  ⇒ 原版关系: 眼贴片前点 %.4f − 开口前沿 %.4f = %+.4f m（负 = 眼在边缘之后）"
              % (max(ys), van_rim, max(ys) - van_rim))

print()
print("【我们的 v9】")
objs = load(OURS)
sh = shell_of(objs, "head_tifa_a")
our_rim = rim_stats(sh, "脸壳") if sh else None
eye = shell_of(objs, "_eye")
if eye:
    ys = [(eye.matrix_world @ v.co).y for v in eye.data.vertices]
    cz = sum((eye.matrix_world @ v.co).z for v in eye.data.vertices) / len(eye.data.vertices)
    print("  眼球件: 前点 y=%.4f  中心 z=%.4f" % (max(ys), cz))
    if our_rim:
        print("  ⇒ 我们当前: 眼球前点 %.4f − 开口前沿 %.4f = %+.4f m" % (max(ys), our_rim, max(ys) - our_rim))
print("=" * 72)
sys.stdout.flush()

# 一次性探针：量【蒂法源模型】里眼球相对眼窝开口的位置关系。
#   —— 这是唯一权威的相对关系：源模型是原作者的装配，眼球/眼窝必然自洽。
#   得到 offset 后按 1.238（我们的放大倍率）换算，就能算出 v9 头里眼球应该前移多少。
# 用法: blender --background --python _probe_source_eye.py
import bpy, bmesh, inspect, sys

def patch_fbx_importer():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
        print("[patch] import_fbx 断言已内存级替换")
patch_fbx_importer()

SRC = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

objs = [o for o in bpy.data.objects if o.type == 'MESH']
print("对象：%s" % [o.name for o in objs])

head = next((o for o in objs if o.name.lower().startswith("head")), None)
eyeb = next((o for o in objs if "eyeball" in o.name.lower()), None)
if not head or not eyeb:
    print("FATAL: 找不到 head / Eyeballs")
    sys.stdout.flush()
    sys.exit(1)

# 源模型里蒂法脸朝 −Y（§3），所以"最前点"是 y 的【最小】值
def ymin(ob):
    return min((ob.matrix_world @ v.co).y for v in ob.data.vertices)

def bbox(ob):
    ps = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return ([min(p[i] for p in ps) for i in range(3)], [max(p[i] for p in ps) for i in range(3)])

lo, hi = bbox(head)
print("head  bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))

# 源 → 我们的变换（由脸壳包围盒反解：绕 x 翻转 + 1.238 缩放 + 平移）
#   our_x =  1.238 * src_x
#   our_y = -1.238 * src_y + 0.01685
#   our_z =  1.238 * src_z - 0.2678
S, BY, BZ = 1.238, 0.01685, -0.2678
print("源→我们的变换: x*%.3f  y*(-%.3f)+%.5f  z*%.3f+%.5f" % (S, S, BY, S, BZ))

print()
print("各配件：源 bbox → 折算到我们空间的【目标 bbox】")
for name in ["Eyeballs", "Mouth", "eyelashes", "eyelashes.2", "Eyebrow", "eyeshadow"]:
    ob = next((o for o in objs if o.name == name), None)
    if ob is None:
        print("  %-14s 不存在" % name)
        continue
    l, h = bbox(ob)
    # 翻转 y：src y 的 min 对应 our y 的 max
    ty0 = -S * h[1] + BY
    ty1 = -S * l[1] + BY
    tz0 = S * l[2] + BZ
    tz1 = S * h[2] + BZ
    print("  %-14s 源 x[%7.4f,%7.4f] y[%7.4f,%7.4f] z[%7.4f,%7.4f]"
          % (name, l[0], h[0], l[1], h[1], l[2], h[2]))
    print("  %-14s ⇒ 目标 x[%7.4f,%7.4f] y[%7.4f,%7.4f] z[%7.4f,%7.4f]  尺寸 %.4f x %.4f x %.4f"
          % ("", S * l[0], S * h[0], ty0, ty1, tz0, tz1,
             S * (h[0] - l[0]), ty1 - ty0, tz1 - tz0))
print()
lo2, hi2 = bbox(eyeb)
print("Eyeballs bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (lo2[0], hi2[0], lo2[1], hi2[1], lo2[2], hi2[2]))

# 眼窝开口：head 的边界边（只连一个面的边），限定在眼睛高度带、前半部分
bm = bmesh.new()
bm.from_mesh(head.data)
bm.verts.ensure_lookup_table()
z_mid = (hi2[2] + lo2[2]) / 2.0
z_half = (hi2[2] - lo2[2]) / 2.0
pts = []
for e in bm.edges:
    if len(e.link_faces) != 1:
        continue
    for v in e.verts:
        p = head.matrix_world @ v.co
        if abs(p.x) < abs(lo2[0]) * 1.6 and abs(p.z - z_mid) < z_half * 1.5 and p.y < 0:
            pts.append(p)
bm.free()
uniq = {(round(p.x, 5), round(p.y, 5), round(p.z, 5)): p for p in pts}
pts = list(uniq.values())
print("眼窝开口候选边界点 %d 个" % len(pts))
if pts:
    ys = sorted(p.y for p in pts)
    zs = sorted(p.z for p in pts)
    print("   边缘 y[%.4f,%.4f]（最前 = %.4f）  z[%.4f,%.4f]  x[%.4f,%.4f]"
          % (ys[0], ys[-1], ys[0], zs[0], zs[-1],
             min(p.x for p in pts), max(p.x for p in pts)))
    off = ymin(eyeb) - ys[0]
    print("⇒ 源模型关系：眼球前点 %.4f − 开口最前点 %.4f = %+.4f（源空间）" % (ymin(eyeb), ys[0], off))
    print("   按我们的放大倍率 1.238 换算 = %+.4f m" % (off * 1.238))
sys.stdout.flush()

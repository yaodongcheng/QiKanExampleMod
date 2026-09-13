# 一次性探针：量【眼球虹膜朝向】——用 UV 采样眼球贴图，算虹膜顶点在球面上的平均方向。
#   期望：虹膜朝 +Y（脸的前方）。若量出来是 +Z 等，就是眼球被转错了，需要绕 X 转回来。
# 用法: blender --background --python _probe_iris_dir.py
import bpy, sys, math

FBX = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
TEX = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\tifa_diff\obj_ours\(unparsed)\head_tifa_a_eye_d.png"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
bpy.context.view_layer.update()

eye = None
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        continue
    for m in ob.data.materials:
        if m and m.name.lower().endswith("_eye"):
            eye = ob
if eye is None:
    print("FATAL: 找不到眼球对象")
    sys.stdout.flush()
    sys.exit(1)

me = eye.data
img = bpy.data.images.load(TEX)
W, H = img.size
px = list(img.pixels)          # RGBA float，原点在左下（与 Blender UV 一致）


def sample(u, v):
    x = int((u % 1.0) * W)
    y = int((v % 1.0) * H)
    i = (y * W + x) * 4
    return px[i], px[i + 1], px[i + 2], px[i + 3]


uvl = me.uv_layers.active.data
# 顶点 → 平均 UV
acc = {}
for loop in me.loops:
    vi = loop.vertex_index
    uv = uvl[loop.index].uv
    a = acc.setdefault(vi, [0.0, 0.0, 0])
    a[0] += uv[0]; a[1] += uv[1]; a[2] += 1

center = [0.0, 0.0, 0.0]
for v in me.vertices:
    p = eye.matrix_world @ v.co
    for i in range(3):
        center[i] += p[i]
n = len(me.vertices)
center = [c / n for c in center]

# 虹膜判定：贴图里虹膜是棕橙色（R 明显大于 B），巩膜是浅粉白（R≈G≈B 且很亮），瞳孔是黑
iris_pts, sclera_pts = [], []
for v in me.vertices:
    a = acc.get(v.index)
    if not a or a[2] == 0:
        continue
    u, vv = a[0] / a[2], a[1] / a[2]
    r, g, b, al = sample(u, vv)
    p = eye.matrix_world @ v.co
    d = (p[0] - center[0], p[1] - center[1], p[2] - center[2])
    L = math.sqrt(sum(x * x for x in d)) or 1.0
    d = tuple(x / L for x in d)
    if r > 0.25 and r > b * 1.6 and g < r * 0.8:      # 棕橙 = 虹膜
        iris_pts.append((d, (r, g, b)))
    elif r > 0.6 and g > 0.6 and b > 0.6:
        sclera_pts.append(d)


def mean_dir(pts):
    if not pts:
        return None
    s = [0.0, 0.0, 0.0]
    for d in pts:
        for i in range(3):
            s[i] += d[i]
    L = math.sqrt(sum(x * x for x in s)) or 1.0
    return tuple(x / L for x in s)


mi = mean_dir([d for d, _ in iris_pts])
ms = mean_dir(sclera_pts)
print("=" * 72)
print("眼球对象 %s  顶点 %d  中心 (%.4f, %.4f, %.4f)" % (eye.name, n, center[0], center[1], center[2]))
print("虹膜顶点 %d 个   巩膜顶点 %d 个" % (len(iris_pts), len(sclera_pts)))
if mi:
    print("⇒ 虹膜平均朝向 = (%.3f, %.3f, %.3f)" % mi)
    names = ["+X", "+Y", "+Z"]
    ax = max(range(3), key=lambda i: abs(mi[i]))
    print("   主方向 = %s%s（+Y = 脸的前方；+Z = 朝上；±X = 左右）"
          % ("-" if mi[ax] < 0 else "+", names[ax][1]))
if ms:
    print("   巩膜平均朝向 = (%.3f, %.3f, %.3f)" % ms)
print("=" * 72)
sys.stdout.flush()

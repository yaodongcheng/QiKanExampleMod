"""验证重导没有破坏动画。

口径：**肢段方向**（父骨 head -> 子骨 head 的世界向量），与骨骼局部轴约定无关 ——
不用「骨的世界朝向」，那会把导出轴约定/rest 差异算成动画误差（README §8.3 已作废该口径）。

用法: blender -b --python verify_reexport.py -- --a <原.fbx> --b <重导.fbx>
"""
import bpy, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
A = argv[argv.index("--a") + 1]
B = argv[argv.index("--b") + 1]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    return arm, arm.animation_data.action


def sample(path, frames):
    """返回 {帧: {骨名: 世界坐标 head}}"""
    arm, act = load(path)
    out = {}
    for f in frames:
        bpy.context.scene.frame_set(f)
        bpy.context.view_layer.update()
        out[f] = {b.name: (arm.matrix_world @ arm.pose.bones[b.name].head).copy()
                  for b in arm.data.bones}
    names = sorted(b.name for b in arm.data.bones)
    parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
    return out, names, parent, act.frame_range


armA, actA = load(A)
frA = actA.frame_range
namesA0 = sorted(b.name for b in armA.data.bones)
n = 12
frames = [int(round(frA[0] + (frA[1] - frA[0]) * i / (n - 1))) for i in range(n)]
print("[verify] 原文件骨数 %d，采样帧 %s" % (len(namesA0), frames))

SA, namesA, parA, _ = sample(A, frames)
SB, namesB, parB, _ = sample(B, frames)

print("[verify] 只在原文件: %s" % sorted(set(namesA) - set(namesB)))
print("[verify] 只在重导文件: %s" % sorted(set(namesB) - set(namesA)))

common = set(namesA) & set(namesB)
pairs = [(c, parA[c]) for c in common if parA.get(c) in common]
print("[verify] 可比对肢段 %d 条" % len(pairs))

tot = 0.0
cnt = 0
worst = (0.0, None)
per_bone = {}
for f in frames:
    for c, p in pairs:
        va = SA[f][c] - SA[f][p]
        vb = SB[f][c] - SB[f][p]
        if va.length < 1e-6 or vb.length < 1e-6:
            continue
        cos = max(-1.0, min(1.0, va.normalized().dot(vb.normalized())))
        deg = math.degrees(math.acos(cos))
        tot += deg
        cnt += 1
        per_bone.setdefault(c, []).append(deg)
        if deg > worst[0]:
            worst = (deg, "%s @f%d" % (c, f))

print("[verify] 共 %d 个 (帧,肢段) 采样" % cnt)
print("[verify] 肢段方向差:  mean %.3f°   max %.3f°  (%s)" % (tot / cnt, worst[0], worst[1]))
print("[verify] 各骨均值（只列 >0.5°）:")
for k in sorted(per_bone, key=lambda k: -sum(per_bone[k]) / len(per_bone[k])):
    m = sum(per_bone[k]) / len(per_bone[k])
    if m > 0.5:
        print("    %-16s mean %.3f°  max %.3f°" % (k, m, max(per_bone[k])))

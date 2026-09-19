# dump_bone_usage.py —— KCD 源件「哪根骨真的在驱动哪块网格」权重统计
#
# 为什么需要：骨映射表决定"哪些骨必须映射对"，而**判断依据只能是权重**——
#   一根没被任何顶点引用的骨（如 rig 根 Human_Male）映射错了也不影响出甲；
#   反过来一根只占 1% 权重的碎骨（如 vcloth）映射错了就是穿模。
#
# 用法: blender -b --factory-startup --python dump_bone_usage.py -- <src.fbx> [--csv out.csv] [--top N]
import bpy, sys, os, csv, inspect, collections

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = ARGV[0] if ARGV and not ARGV[0].startswith("--") else None
CSVOUT = ARGV[ARGV.index("--csv") + 1] if "--csv" in ARGV else None
TOP = int(ARGV[ARGV.index("--top") + 1]) if "--top" in ARGV else 999
if SRC is None:
    print("!! 需要源件路径"); sys.exit(2)


def patch_importer():
    """KCD 的 FBX 缺 armature_setup 登记 → Blender 导入器 KeyError: None"""
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    o = src
    b1 = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if b1 in src:
        src = src.replace(b1, "pass  # patched")
    b2 = "                    (mmat, amat) = mesh.armature_setup[self]"
    if b2 in src:
        src = src.replace(b2, b2.replace(
            "mesh.armature_setup[self]",
            "mesh.armature_setup.setdefault(self, (mesh.bind_matrix, self.bind_matrix))"))
    if src != o:
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


patch_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, use_anim=False, ignore_leaf_bones=True)

arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
all_bones = set(b.name for b in arm.data.bones) if arm else set()
used_global = collections.Counter()      # 骨 -> 累计权重（跨所有网格）
rows = []
for ob in sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name):
    w = collections.Counter()
    nv = len(ob.data.vertices)
    for v in ob.data.vertices:
        for g in v.groups:
            if g.weight > 1e-6:
                w[ob.vertex_groups[g.group].name] += g.weight
    tot = sum(w.values()) or 1.0
    used_global.update(w)
    print("\n=== %-34s verts=%-6d 骨=%-4d 权重和=%.0f" % (ob.name[:34], nv, len(w), tot))
    for n, s in w.most_common(TOP):
        mark = "" if n in all_bones else "  !! 不在骨架里"
        print("     %-42s %6.2f%%%s" % (n[:42], 100 * s / tot, mark))
        rows.append([ob.name, n, "%.4f" % s, "%.2f" % (100 * s / tot)])
    sys.stdout.flush()

print("\n=== 全件合计：被用到的骨 %d / 骨架 %d ===" % (len(used_global), len(all_bones)))
print("=== 权重为 0（骨架里有、网格没引用）的骨 %d 根 ===" % len(all_bones - set(used_global)))
z = sorted(all_bones - set(used_global))
for i in range(0, len(z), 4):
    print("   " + " | ".join("%-38s" % x for x in z[i:i + 4]))

if CSVOUT:
    with open(CSVOUT, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["mesh", "bone", "weight_sum", "pct"])
        w.writerows(rows)
    print("-> %s" % CSVOUT)
sys.stdout.flush()

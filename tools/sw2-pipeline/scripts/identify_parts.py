# identify_parts.py —— 零件识别工具：战无2 源模型 → 零件身份表 + 接触图
#
# 为什么需要它：战无2 的 58 个源模型，零件全是无名编号
#   （`model_0_submesh_6_noesis_meshnode_0006`），没有 head/eye/armor 这种字。
#   换头管线的 --pick 靠关键字、盔甲管线的 --parts 靠序号，两个都不通用 →
#   28 个武将 × 平均 13 块件，人工认不出来，必须先程序化把「哪块是谁」量出来。
#
# 本工具只做【测量 + 初判 + 出图】三件事，**最终认件由人看接触图拍板**（写进覆盖表）。
#   机器量：包围盒 / 顶点数 / 顶点组 → 主导骨及其世界高度 / UV 采样主色
#   机器猜：按下面的 RULES 给一个初判 + 置信度 + 依据字符串
#   出图  ：接触图（每块零件单显、等比缩放到各自格子里）+ 全模型正/背视图
#
# 用法（Blender）:
#   blender -b --python identify_parts.py -- --src <源.fbx> --out <输出目录> [--no-render]
#
# 产出:
#   <out>/<stem>_parts.csv    零件表（给人看 + 给下游脚本读）
#   <out>/<stem>_sheet.png    接触图（格子按 CSV 行序，行优先；F 面）
#   <out>/<stem>_sheet_B.png  接触图（背面）
#   <out>/<stem>_full.png     全模型正视图
import bpy, sys, os, csv, math
import numpy as np
from mathutils import Vector

# ---------------- 基础 ----------------

def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d

def patch_importer():
    """导入器内存补丁（两种源格式各踩过一个坑）：

    ① 战无2 FBX 的 morph 通道缺 FullWeights → Blender 导入器断言崩溃。
    ② KCD（3ds Max 导出）的武器/盾等件蒙皮到的骨头**不在骨架子树下** →
       `mesh.armature_setup` 里没有对应登记 → `link_hierarchy` 抛 KeyError: None。
       补丁只在这种「该崩的情况」下兜底，正常文件一行都不变。
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
            "                        print('[IMPATCH] no armature_setup: mesh=%s arm=%s keys=%s'\n"
            "                              % (mesh.fbx_name, getattr(self, 'fbx_name', '?'),\n"
            "                                 [getattr(k, 'fbx_name', k) for k in mesh.armature_setup]))\n"
            "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
            + bad2))
    if src != orig:
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)

# ---------------- 判定规则 ----------------
# 顺序敏感：先判「一定不是角色件」的（武器/驱动件），再判部位。
# 每条规则返回 (身份, 置信度, 依据) 或 None。

EYE_V, EYE_F = 38, 60          # 实测 27/28 命中：眼球固定 38 顶点 / 60 面

def bone_z(arm, gname):
    """顶点组名 → 该骨在静止姿态的世界 z（没有则 None）。"""
    if arm is None:
        return None
    b = arm.data.bones.get(gname)
    if b is None:
        return None
    return (arm.matrix_world @ b.head_local).z

def classify(rec):
    mats = " ".join(rec["mats"]).lower()
    if "mat_w_" in mats:
        return "weapon", 0.99, "材质名含 mat_w_"
    if "driver_" in mats:
        return "driver", 0.99, "材质名含 driver_"
    if rec["v"] == EYE_V and rec["f"] == EYE_F:
        return "eye", 0.95, "顶点/面数 = 38/60（眼球地标）"
    # 🔴 高度一律用【占全身的比例】，不用绝对 z ——
    #    战无2 源模型是厘米（骨在 z≈165 而不是 1.65），写死米制阈值会让判定全塌（已踩）。
    bz = rec["bone_z"]
    if bz:
        top = max(bz.values())
        if top > 0.86:
            return "head_area", 0.70, "主导骨最高 %.2f 身高（≈头）" % top
        if rec["zr1"] < 0.12:
            return "foot_area", 0.60, "包围盒顶 %.2f 身高（≈脚）" % rec["zr1"]
        if top < 0.46:
            return "leg_area", 0.55, "主导骨最高 %.2f 身高（≈腿）" % top
        return "body_area", 0.50, "主导骨 %.2f~%.2f 身高" % (min(bz.values()), top)
    return "unknown", 0.10, "无顶点组可用"

# ---------------- 导入 + 测量 ----------------

def sample_color(ob):
    """对每个面的 UV 中心采样材质图，取平均色（给"是皮肤还是金属"一个客观数）。"""
    me = ob.data
    if not me.uv_layers or not me.materials:
        return None
    # 每种材质的图只取一次像素（🔴 放在面循环外——放里面会对每个面复制整张图，慢到不可用）
    cache = {}
    def pixels_for(mat):
        if mat is None or not mat.use_nodes:
            return None
        if mat.name in cache:
            return cache[mat.name]
        im = None
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                im = n.image; break
        px = None
        if im is not None:
            try:
                if im.size[0] and im.size[1]:
                    px = (list(im.pixels), im.size[0], im.size[1])
            except Exception:
                px = None
        cache[mat.name] = px
        return px
    uv = me.uv_layers.active.data
    acc = [0.0, 0.0, 0.0]; n = 0
    for p in me.polygons:
        slot = min(p.material_index, len(me.materials) - 1)
        got = pixels_for(me.materials[slot])
        if got is None:
            continue
        px, W, H = got
        for li in p.loop_indices:
            u, v = uv[li].uv
            x = min(W - 1, max(0, int((u % 1.0) * W)))
            y = min(H - 1, max(0, int((v % 1.0) * H)))
            i = (y * W + x) * 4
            acc[0] += px[i]; acc[1] += px[i + 1]; acc[2] += px[i + 2]
            n += 1
        if n > 6000:
            break
    if n == 0:
        return None
    return tuple(int(round(c / n * 255)) for c in acc)

def collect(arm):
    raw = []
    for ob in sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name):
        me = ob.data
        lo = [1e9] * 3; hi = [-1e9] * 3
        for v in me.vertices:
            p = ob.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
        # 顶点组 → 累计权重
        wsum = {}
        for v in me.vertices:
            for g in v.groups:
                nm = ob.vertex_groups[g.group].name
                wsum[nm] = wsum.get(nm, 0.0) + g.weight
        tot = max(1e-9, sum(w for _, w in wsum.items()))
        top_bones = sorted(wsum.items(), key=lambda kv: -kv[1])[:6]
        bz_abs = {}
        for nm, _ in top_bones:
            z = bone_z(arm, nm)
            if z is not None:
                bz_abs[nm] = z
        raw.append({
            "name": ob.name, "v": len(me.vertices), "f": len(me.polygons),
            "mats": [m.name if m else "(None)" for m in me.materials],
            "x0": lo[0], "x1": hi[0], "y0": lo[1], "y1": hi[1], "z0": lo[2], "z1": hi[2],
            "top_bones": ";".join("%s(%.0f%%)" % (nm, 100.0 * w / tot) for nm, w in top_bones[:3]),
            "bone_z_abs": bz_abs, "ob": ob,
        })

    # 🔴 高度一律换算成【占全身的比例】：源模型是厘米（骨在 z≈165 而非 1.65），
    #    写死米制阈值会把所有零件都判成"头"（已踩）。全身高度只按角色件算，
    #    排除武器/驱动件——长枪会把 z 范围撑出去。
    def is_prop(r):
        m = " ".join(r["mats"]).lower()
        return ("mat_w_" in m) or ("driver_" in m)
    body = [r for r in raw if not is_prop(r)] or raw
    zmin = min(r["z0"] for r in body)
    zmax = max(r["z1"] for r in body)
    span = max(1e-6, zmax - zmin)

    recs = []
    for r in raw:
        r["zr0"] = (r["z0"] - zmin) / span
        r["zr1"] = (r["z1"] - zmin) / span
        r["bone_z"] = {nm: (z - zmin) / span for nm, z in r["bone_z_abs"].items()}
        r["color"] = sample_color(r["ob"])
        r["ident"], r["conf"], r["why"] = classify(r)
        recs.append(r)
    print("[PARTS] 全身高度换算：%.3f~%.3f（源单位），比例化完成" % (zmin, zmax))
    return recs

# ---------------- 接触图 ----------------

def setup_render(w, h):
    scn = bpy.context.scene
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            continue
    scn.view_settings.view_transform = 'Standard'
    scn.render.image_settings.file_format = 'PNG'
    scn.render.resolution_x = w
    scn.render.resolution_y = h
    scn.render.film_transparent = False
    world = bpy.data.worlds.new('W')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.30, 0.32, 0.36, 1)
    scn.world = world
    for nm, rot, en in (('k', (0.9, 0.0, 0.5), 5.0), ('f', (1.3, 0.0, -1.1), 2.5),
                        ('b', (1.0, 0.0, 3.1), 2.0)):
        ld = bpy.data.lights.new(nm, 'SUN')
        ld.energy = en; ld.use_shadow = False
        lo = bpy.data.objects.new(nm, ld)
        lo.rotation_euler = rot
        scn.collection.objects.link(lo)
    cd = bpy.data.cameras.new('C')
    cd.type = 'ORTHO'; cd.sensor_fit = 'HORIZONTAL'
    cd.clip_start = 0.01; cd.clip_end = 100000.0
    cam = bpy.data.objects.new('C', cd)
    scn.collection.objects.link(cam)
    scn.camera = cam
    return scn, cd, cam

def add_label(idx, loc, size):
    """在格子里放一个编号（接触图必须能直接对号入座，否则 28 张图靠猜）。"""
    bpy.ops.object.text_add(location=loc)
    ob = bpy.context.object
    ob.data.body = str(idx)
    ob.data.size = size
    ob.data.align_x = 'LEFT'
    ob.data.align_y = 'TOP'
    ob.rotation_euler = (math.radians(90), 0, 0)      # 文字默认朝 +Z，转成朝 -Y（相机在 -Y 侧）
    m = bpy.data.materials.new("lbl%d" % idx)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Color'].default_value = (1.0, 0.85, 0.0, 1)   # 黄字，背景灰 → 一眼可见
    em.inputs['Strength'].default_value = 2.0
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
    ob.data.materials.append(m)
    return ob


def flat_material(tag, rgb):
    """纯色材质——给"原材质渲染不出来"的件兜底用。"""
    m = bpy.data.materials.new("flat_%s" % tag)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    df = nt.nodes.new('ShaderNodeBsdfDiffuse')
    df.inputs['Color'].default_value = (rgb[0], rgb[1], rgb[2], 1)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(df.outputs['BSDF'], out.inputs['Surface'])
    return m


def build_sheet(recs, cell=340, cols=None):
    """把每块零件复制一份，等比缩放到自己格子里，排成网格，并标上序号。返回 (W, H)。"""
    from mathutils import Matrix
    n = len(recs)
    if cols is None:
        cols = int(math.ceil(math.sqrt(n)))
    rows = int(math.ceil(n / float(cols)))
    # 🔴 武器件的原材质渲染出来是**空白格**（实测 28 人全中，见 README 坑表）——
    #    判定它是不是武器靠材质名，不靠看图；但要做武器管线时必须看得见形状，
    #    所以这里给武器/驱动件套一层纯色兜底材质。
    fallback = {"weapon": flat_material("w", (0.85, 0.72, 0.35)),
                "driver": flat_material("d", (0.35, 0.55, 0.85))}
    for i, r in enumerate(recs):
        ob = r["ob"]
        cx, cy = i % cols, i // cols
        d = max(r["x1"] - r["x0"], r["y1"] - r["y0"], r["z1"] - r["z0"])
        s = (cell * 0.80) / max(1e-6, d)
        ctr = Vector(((r["x0"] + r["x1"]) / 2, (r["y0"] + r["y1"]) / 2, (r["z0"] + r["z1"]) / 2))
        ox = (cx - (cols - 1) / 2.0) * cell
        oz = -((cy - (rows - 1) / 2.0) * cell)
        dst = Vector((ox, 0.0, oz))
        new = bpy.data.objects.new("%s_cell%d" % (ob.name[:16], i), ob.data)
        bpy.context.scene.collection.objects.link(new)
        # 先把零件重心搬到原点 → 等比缩放 → 平移到格子中心
        # 🔴 末尾必须再乘 `ob.matrix_world`：包围盒是按**世界**坐标算的，而上面那条链是
        #    对**局部**顶点做的。FBX 导入的零件各自带对象级变换（尤其 `.001` 武器件），
        #    不补这一项 → 那些件会画到别的格子里去（实测：武器格是空白，枪落到两格下面）。
        new.matrix_world = (Matrix.Translation(dst) @ Matrix.Scale(s, 4)
                            @ Matrix.Translation(-ctr) @ ob.matrix_world)
        if r["ident"] in fallback:
            new.data = ob.data.copy()          # 换材质要动网格数据，不能改原件的
            new.data.materials.clear()
            new.data.materials.append(fallback[r["ident"]])
        r["ob"].hide_render = True            # 原模型不参与接触图渲染
    # 🔴 编号牌要放在【所有格子件的最前面】。踩过：写死 y 偏移，结果超出相机 clip_end
    #    被裁掉，接触图上完全没有编号。
    ymin = 1e9
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.name.endswith(tuple("cell%d" % i for i in range(n))):
            continue
        for v in o.data.vertices:
            ymin = min(ymin, (o.matrix_world @ v.co).y)
    if ymin > 1e8:
        ymin = 0.0
    ylab = ymin - cell * 0.06   # 比所有零件更靠近相机（相机在 y=-900）
    for i in range(n):
        cx, cy = i % cols, i // cols
        ox = (cx - (cols - 1) / 2.0) * cell
        oz = -((cy - (rows - 1) / 2.0) * cell)
        add_label(i, (ox - cell * 0.45, ylab, oz + cell * 0.45), cell * 0.18)
    return cols * cell, rows * cell

def render_views(scn, cd, cam, outbase, ctr, W, H, tag):
    d = Vector((0, -1, 0))
    cd.ortho_scale = W * 1.02
    cam.location = ctr + d * 900.0
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    fp = "%s_%s.png" % (outbase, tag)
    scn.render.filepath = fp
    bpy.ops.render.render(write_still=True)
    return fp

# ---------------- 主流程 ----------------

def main():
    a = args_after_ddash()
    src = get(a, "--src"); out = get(a, "--out")
    if not src or not out:
        print("FATAL: 需要 --src / --out"); sys.exit(1)
    no_render = "--no-render" in a
    os.makedirs(out, exist_ok=True)
    stem = os.path.splitext(os.path.basename(src))[0]
    out = os.path.abspath(out)          # 🔴 渲染输出必须绝对路径：Blender 的 filepath 是相对 blend 文件解析的

    patch_importer()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    bpy.context.view_layer.update()
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)

    recs = collect(arm)
    print("[PARTS] %s: %d 块零件, 骨架=%s" % (stem, len(recs), arm.name if arm else "无"))

    # ---- CSV ----
    dst = os.path.join(out, "%s_parts.csv" % stem)
    with open(dst, "w", newline="", encoding="utf-8-sig") as fh:
        w = csv.writer(fh)
        w.writerow(["idx", "ident", "conf", "why", "name", "verts", "faces",
                    "mats", "top_bones", "color", "zrel", "bbox"])
        for i, r in enumerate(recs):
            w.writerow([i, r["ident"], "%.2f" % r["conf"], r["why"], r["name"], r["v"], r["f"],
                        "|".join(r["mats"]), r["top_bones"],
                        "" if not r["color"] else "%d,%d,%d" % r["color"],
                        "%.2f~%.2f" % (r["zr0"], r["zr1"]),
                        "%.3f,%.3f %.3f,%.3f %.3f,%.3f" % (r["x0"], r["x1"], r["y0"], r["y1"], r["z0"], r["z1"])])
    print("[PARTS] wrote", dst)

    if no_render:
        return 0
    scn, cd, cam = setup_render(900, 900)
    W, H = build_sheet(recs)
    scn.render.resolution_x = int(W)
    scn.render.resolution_y = int(H)
    ctr = Vector((0, 0, 0))
    # 正面（源模型脸朝 -Y → 相机放在 -Y 侧）
    fp = render_views(scn, cd, cam, os.path.join(out, stem), ctr, W, H, "sheet")
    print("[PARTS] wrote", fp)
    # 背面
    d = Vector((0, 1, 0))
    cam.location = ctr + d * 900.0
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = os.path.join(out, stem + "_sheet_B.png")
    bpy.ops.render.render(write_still=True)
    print("[PARTS] wrote", scn.render.filepath)
    return 0

sys.exit(main())

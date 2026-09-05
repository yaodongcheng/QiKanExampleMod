# -*- coding: utf-8 -*-
"""
trf_meta_mesh_exporter
---
把 Blender 场景里的网格对象导出为 Bannerlord 样式 .trf (Text Resource Files)。

与 try_meta_mesh_importer.py 严格对称 —— 导出的文件可被 importer 读回。

.trf 文件结构 (rfver 4):
  rfver 4
  mesh <mesh_count>
  <每段一个 mesh>
    mesh_name  <flag>  <material_name>
    <vertex_count>
    <vertex_count 行: x y z>                    # 位置顶点
    <flag>                                       # 恒为 0
    <vertex_fvf_count>
    <vertex_fvf_count 段, 每段 3 行:>
        行1: vertex_index  vertex_color  normal_x normal_y normal_z
        行2: uv_x uv_y
        行3: uv2_x uv2_y
    < morph_key_count >
    <morph_key_count 段 ...>
    <face_count>
    <face_count 行: i j k>                       # 面索引引用 vertex_fvfs (空格分隔)
    <bone ...>                                   # 骨骼段(可选)

路径约定 (OpenTrf 工具):
  - 脚本目录: H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/LivingWorldNpcs/tools/OpenTrf/
  - TRF 目录: H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/MyMapTest/EmAssetPackages/TRF/
  end

用法:
  在 Blender 打开脚本编辑器粘贴运行 (当前场景里的 mesh 对象会被导出).
"""

import bpy

# ---------------------------------------------------------------------------
# 数据结构定义 (与 importer 一致, 另含骨骼权重)
# ---------------------------------------------------------------------------
class TrfMesh:
    def __init__(self):
        self.mesh_name = ''
        self.mesh_materials = []
        self.vertex_count = 0
        self.vertex_fvf_count = 0
        self.morph_key_count = 0
        self.bone_count = 0
        self.face_count = 0
        self.vertices = []
        self.vertex_fvfs = []
        self.faces = []
        self.morph_keys = []
        self.bones = []
        self.bone_weights = []
        self.vertex_src = []      # trf 共享顶点 -> 源 Blender 顶点索引 (morph key 导出用)


class TrfVertex:
    def __init__(self):
        self.x = 0.0
        self.y = 0.0
        self.z = 0.0


class TrfVertexFvf:
    def __init__(self):
        self.vertex_index = 0
        self.vertex_color = 4294967295
        self.normal_x = 0.0
        self.normal_y = 0.0
        self.normal_z = 0.0
        self.uv_x = 0.0
        self.uv_y = 0.0
        self.uv2_x = 0.0
        self.uv2_y = 0.0


class TrfFace:
    def __init__(self):
        self.vertex_fvf_indices = []


class TrfMorphKey:
    def __init__(self):
        self.morph_key_time = 0.0
        self.vertex_count = 0
        self.vertices = []
        self.vertex_fvf_count = 0
        self.vertex_fvfs = []


class TrfBoneWeight:
    def __init__(self):
        self.bone1_index = -1
        self.bone1_weight = 0.0
        self.bone2_index = -1
        self.bone2_weight = 0.0


# ---------------------------------------------------------------------------
# 颜色 / 精度辅助
# ---------------------------------------------------------------------------
def color_to_argb_int(r, g, b, a=1.0):
    """RGBA float(0~1) -> ARGB int (与 importer 的 color_to_argb 对称)"""
    rr = min(255, max(0, round(r * 255)))
    gg = min(255, max(0, round(g * 255)))
    bb = min(255, max(0, round(b * 255)))
    aa = min(255, max(0, round(a * 255)))
    return (aa << 24) | (rr << 16) | (gg << 8) | bb


def _f(num):
    """float -> 6 位小数 (与原始 .trf 一致, 控制导出体积)"""
    return "%.6f" % float(num)


# ---------------------------------------------------------------------------
# 从 Blender mesh 构造 TrfMesh
# ---------------------------------------------------------------------------
def _get_corner_normal(mesh, loop_index):
    """返回 (nx,ny,nz)。优先用顶点法线(贴合 .trf 的顶点级 FVF 语义),
    失败时回退到 corner normals / loop normal。"""
    vidx = mesh.loops[loop_index].vertex_index
    try:
        n = mesh.vertices[vidx].normal
        return (n.x, n.y, n.z)
    except Exception:
        pass
    try:
        n = mesh.corner_normals[loop_index].vector
        return (n.x, n.y, n.z)
    except Exception:
        try:
            mesh.calc_normals_split()
        except Exception:
            pass
        try:
            n = mesh.loops[loop_index].normal
            return (n.x, n.y, n.z)
        except Exception:
            return (0.0, 0.0, 0.0)


def _get_corner_color(mesh, color_attr, alpha_attr, loop_index, vertex_index):
    """返回 (r,g,b,a) float。CORNER 域用 loop_index, POINT 域用 vertex_index。
    alpha 独立属性存在时覆盖第 4 通道(importer 的 rgb/alpha 拆分版)。"""
    if color_attr is None:
        return (1.0, 1.0, 1.0, 1.0)
    try:
        if color_attr.domain == 'CORNER':
            c = color_attr.data[loop_index].color
        else:
            c = color_attr.data[vertex_index].color
        a = c[3]
        if alpha_attr is not None:
            if alpha_attr.domain == 'CORNER':
                a = alpha_attr.data[loop_index].value
            else:
                a = alpha_attr.data[vertex_index].value
        return (c[0], c[1], c[2], a)
    except Exception:
        return (1.0, 1.0, 1.0, 1.0)


def _get_vertex_fvf_index(trf_mesh, index, normal, color, uv, uv2):
    """查找相同属性的已有 FVF, 没有则新建。参照截图去重逻辑(法线/UV/UV2 按 3 位小数比较)。"""
    for i, vertex_fvf in enumerate(trf_mesh.vertex_fvfs):
        if vertex_fvf.vertex_index != index:
            continue
        is_normal_equal = (
            round(vertex_fvf.normal_x, 3) == round(normal[0], 3)
            and round(vertex_fvf.normal_y, 3) == round(normal[1], 3)
            and round(vertex_fvf.normal_z, 3) == round(normal[2], 3)
        )
        is_uv_equal = (
            round(vertex_fvf.uv_x, 3) == round(uv[0], 3)
            and round(vertex_fvf.uv_y, 3) == round(uv[1], 3)
        )
        is_uv2_equal = (
            round(vertex_fvf.uv2_x, 3) == round(uv2[0], 3)
            and round(vertex_fvf.uv2_y, 3) == round(uv2[1], 3)
        )
        is_color_equal = vertex_fvf.vertex_color == color
        if is_normal_equal and is_uv_equal and is_uv2_equal and is_color_equal:
            return i
    fvf = TrfVertexFvf()
    fvf.vertex_index = index
    fvf.normal_x, fvf.normal_y, fvf.normal_z = normal
    fvf.uv_x, fvf.uv_y = uv
    fvf.uv2_x, fvf.uv2_y = uv2
    fvf.vertex_color = color
    trf_mesh.vertex_fvfs.append(fvf)
    return len(trf_mesh.vertex_fvfs) - 1


def create_trf_mesh(obj):
    """从单个 Blender 对象构造一个 TrfMesh"""
    me = obj.data
    trf_mesh = TrfMesh()
    trf_mesh.mesh_name = obj.name

    # 材质名
    if me.materials:
        trf_mesh.mesh_materials = [m.name for m in me.materials]
    else:
        trf_mesh.mesh_materials = []

    # 位置顶点: 按坐标去重成共享 position (恢复 .trf 的共享顶点结构, 控制体积)
    pos_map = {}
    vertex_to_pos = []
    for v in me.vertices:
        key = (round(v.co.x, 6), round(v.co.y, 6), round(v.co.z, 6))
        if key not in pos_map:
            pos_map[key] = len(trf_mesh.vertices)
            tv = TrfVertex()
            tv.x, tv.y, tv.z = v.co.x, v.co.y, v.co.z
            trf_mesh.vertices.append(tv)
            trf_mesh.vertex_src.append(v.index)
        vertex_to_pos.append(pos_map[key])
    trf_mesh.vertex_count = len(trf_mesh.vertices)

    # UV 层
    uv_layer = me.uv_layers.active if me.uv_layers else None
    uv2_layer = None
    if me.uv_layers and len(me.uv_layers) > 1:
        uv2_layer = me.uv_layers[1]

    # 顶点色: rgb (color attribute) + alpha (普通 FLOAT 属性), 兼容旧版 'Col'
    color_attr = None
    alpha_attr = None
    try:
        if me.color_attributes:
            color_attr = me.color_attributes.get('rgb') or me.color_attributes.get('Col') or me.color_attributes.active_color
        alpha_attr = me.attributes.get('alpha') if me.attributes else None
    except Exception:
        color_attr = None

    # 遍历三角化面 -> loops -> 去重 FVF
    try:
        me.calc_loop_triangles()
    except Exception:
        pass

    for tri in me.loop_triangles:
        loop_ids = list(tri.loops)
        fvf_indices = []
        for li in loop_ids:
            loop = me.loops[li]
            vidx = loop.vertex_index
            pos_idx = vertex_to_pos[vidx]   # 去重后的共享 position 索引
            normal = _get_corner_normal(me, li)
            uv = uv_layer.data[li].uv if uv_layer else (0.0, 0.0)
            uv2 = uv2_layer.data[li].uv if uv2_layer else (0.0, 0.0)
            r, g, b, a = _get_corner_color(me, color_attr, alpha_attr, li, vidx)
            color_int = color_to_argb_int(r, g, b, a)
            fi = _get_vertex_fvf_index(trf_mesh, pos_idx, normal, color_int, uv, uv2)
            fvf_indices.append(fi)
        face = TrfFace()
        face.vertex_fvf_indices = fvf_indices
        trf_mesh.faces.append(face)

    trf_mesh.face_count = len(trf_mesh.faces)
    trf_mesh.vertex_fvf_count = len(trf_mesh.vertex_fvfs)
    return trf_mesh


def export_shape_keys_as_morph(trf_mesh, obj):
    """把 Blender shape keys 追加为 TRF morph keys (跳过 Basis)。

    结构约定(未验证, 见文件头说明): 假设 morph 段与 base 同构 —— 顶点数组与
    base 共享顶点按索引一一对应, 顶点数必须一致; FVF 数组复制 base(位置变,
    法线/UV/色不变)。不一致的 key 跳过。"""
    sk = obj.data.shape_keys
    if not sk:
        return
    if len(trf_mesh.vertex_src) != len(trf_mesh.vertices):
        print("morph 跳过: 共享顶点映射缺失")
        return
    for kb in sk.key_blocks:
        if kb.name == 'Basis':
            continue
        if len(kb.data) < len(trf_mesh.vertex_src):
            print("morph key '%s' 顶点数不足, 跳过" % kb.name)
            continue
        mk = TrfMorphKey()
        mk.morph_key_time = MORPH_TIME_OVERRIDES.get(kb.name, len(trf_mesh.morph_keys) * MORPH_TIME_STEP)
        for src in trf_mesh.vertex_src:
            co = kb.data[src].co
            v = TrfVertex()
            v.x, v.y, v.z = co.x, co.y, co.z
            mk.vertices.append(v)
        mk.vertex_count = len(mk.vertices)
        mk.vertex_fvfs = list(trf_mesh.vertex_fvfs)
        mk.vertex_fvf_count = len(mk.vertex_fvfs)
        trf_mesh.morph_keys.append(mk)
        print("morph key '%s' time=%s v=%d" % (kb.name, mk.morph_key_time, mk.vertex_count))
    trf_mesh.morph_key_count = len(trf_mesh.morph_keys)


def add_morph_key_to_trf_mesh(trf_mesh, morph_name, morph_key_time, trf_mesh2):
    """添加形态键(骨架, 按需启用)。"""
    mk = TrfMorphKey()
    mk.morph_key_time = morph_key_time
    mk.vertex_count = len(trf_mesh2.vertices)
    mk.vertices = list(trf_mesh2.vertices)
    mk.vertex_fvf_count = len(trf_mesh2.vertex_fvfs)
    mk.vertex_fvfs = list(trf_mesh2.vertex_fvfs)
    trf_mesh.morph_keys.append(mk)
    trf_mesh.morph_key_count = len(trf_mesh.morph_keys)


# ---------------------------------------------------------------------------
# 写出 .trf
# ---------------------------------------------------------------------------
def export_trf_file(trf_meta_mesh, path):
    """把 [TrfMesh] 列表写到 .trf 文件"""
    with open(path, 'w', encoding='utf-8', errors='replace') as file:
        file.write('rfver 4\n')
        file.write('mesh ' + str(len(trf_meta_mesh)) + '\n')

        for trf_mesh in trf_meta_mesh:
            # 头部: name 0 material1 [material2 ...]
            header = trf_mesh.mesh_name + ' 0'
            for mat in trf_mesh.mesh_materials:
                header += ' ' + mat
            file.write(header + '\n')

            # 顶点
            file.write(str(trf_mesh.vertex_count) + '\n')
            for v in trf_mesh.vertices:
                file.write(f"{_f(v.x)} {_f(v.y)} {_f(v.z)}\n")

            # flag(恒0) + FVF
            file.write('0\n')
            file.write(str(trf_mesh.vertex_fvf_count) + '\n')
            for fvf in trf_mesh.vertex_fvfs:
                file.write(
                    f"{fvf.vertex_index} {fvf.vertex_color} "
                    f"{_f(fvf.normal_x)} {_f(fvf.normal_y)} {_f(fvf.normal_z)}\n"
                )
                file.write(f"{_f(fvf.uv_x)} {_f(fvf.uv_y)}\n")
                file.write(f"{_f(fvf.uv2_x)} {_f(fvf.uv2_y)}\n")

            # 形态键
            file.write(str(trf_mesh.morph_key_count) + '\n')
            for mk in trf_mesh.morph_keys:
                file.write(f"{_f(mk.morph_key_time)}\n")
                file.write(str(mk.vertex_count) + '\n')
                for v in mk.vertices:
                    file.write(f"{_f(v.x)} {_f(v.y)} {_f(v.z)}\n")
                file.write(str(mk.vertex_fvf_count) + '\n')
                for fvf in mk.vertex_fvfs:
                    file.write(
                        f"{fvf.vertex_index} {fvf.vertex_color} "
                        f"{_f(fvf.normal_x)} {_f(fvf.normal_y)} {_f(fvf.normal_z)}\n"
                    )
                    file.write(f"{_f(fvf.uv_x)} {_f(fvf.uv_y)}\n")
                    file.write(f"{_f(fvf.uv2_x)} {_f(fvf.uv2_y)}\n")

            # 面 (空格分隔, 与样例一致)
            file.write(str(trf_mesh.face_count) + '\n')
            for f in trf_mesh.faces:
                file.write(' '.join(str(i) for i in f.vertex_fvf_indices) + '\n')

        file.write('end\n')


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------
import os

# ===== 导出配置 (在这里改) =====
# 默认导出目录: 弹窗会定位到这里, 可改
DEFAULT_EXPORT_DIR = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\MyMapTest\EmAssetPackages\TRF"
# 要导出的 .trf 路径: 填了就直接用它; 留空 "" 则弹出"保存文件"选择框
OUT_PATH = ""
# 白名单: 只导出这些名称的子 mesh; 留空 [] = 全部
EXPORT_MESH_NAMES = []
# 黑名单: 这些名称的子 mesh 会被忽略(跳过)
SKIP_MESH_NAMES = []

# ===== 顶点动画 (morph key) =====
# Blender shape keys -> TRF morph keys 的接线开关
EXPORT_SHAPE_KEYS = True    # 是否把 shape keys 导成 morph key
MORPH_TIME_STEP = 0.1       # 未指定时逐 key 递增的时间(骑砍引擎按 time 播放/插值)
MORPH_TIME_OVERRIDES = {}   # {"key名": 时间} 手动指定某个 key 的时间


def export_meshes(out_path):
    """遍历场景 mesh 并按过滤名单导出到 out_path"""
    trf_meta_mesh = []
    exported, skipped = [], []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        name = obj.name
        # 黑名单命中 -> 跳过
        if SKIP_MESH_NAMES and name in SKIP_MESH_NAMES:
            skipped.append(name)
            continue
        # 白名单非空且不在名单内 -> 跳过
        if EXPORT_MESH_NAMES and name not in EXPORT_MESH_NAMES:
            skipped.append(name)
            continue

        trf_mesh = create_trf_mesh(obj)
        if EXPORT_SHAPE_KEYS:
            export_shape_keys_as_morph(trf_mesh, obj)
        print("mesh name:" + trf_mesh.mesh_name)
        print("mesh vertex count:" + str(trf_mesh.vertex_count))
        print("mesh vertex fvf count:" + str(trf_mesh.vertex_fvf_count))
        print("mesh face count:" + str(trf_mesh.face_count))
        trf_meta_mesh.append(trf_mesh)
        exported.append(name)

    export_trf_file(trf_meta_mesh, out_path)
    print("----------------------------------")
    print("导出 mesh (%d): %s" % (len(exported), exported))
    if skipped:
        print("跳过 mesh (%d): %s" % (len(skipped), skipped))
    print("已导出到:", out_path)


class TRF_OT_export(bpy.types.Operator):
    """导出 Bannerlord .trf 文件"""
    bl_idname = "trf.export_trf"
    bl_label = "导出 TRF (OpenTrf)"
    bl_description = "把当前场景的 mesh 导出为 .trf"
    filepath: bpy.props.StringProperty(subtype='FILE_PATH')

    def invoke(self, context, event):
        if not self.filepath:
            self.filepath = os.path.join(DEFAULT_EXPORT_DIR, "building.trf")
        context.window_manager.fileselect_add(self)   # 保存文件对话框
        return {'RUNNING_MODAL'}

    def execute(self, context):
        export_meshes(self.filepath)
        return {'FINISHED'}


def menu_func_export(self, context):
    self.layout.operator(TRF_OT_export.bl_idname, text="TRF (OpenTrf)...")


def register():
    try:
        bpy.utils.register_class(TRF_OT_export)
    except Exception:
        pass
    try:
        bpy.types.TOPBAR_MT_file_export.append(menu_func_export)
    except Exception:
        pass


def unregister():
    try:
        bpy.types.TOPBAR_MT_file_export.remove(menu_func_export)
    except Exception:
        pass
    try:
        bpy.utils.unregister_class(TRF_OT_export)
    except Exception:
        pass


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------
if __name__ == '__main__':
    # 命令行带参(可选):  blender --background --python 本脚本 -- <输出路径> <mesh名...>
    import sys
    argv = sys.argv
    if '--' in argv:
        args = argv[argv.index('--') + 1:]
        if args:
            OUT_PATH = args[0]
        if len(args) > 1:
            EXPORT_MESH_NAMES = args[1:]

    if OUT_PATH:
        # 填了路径 -> 直接用, 不弹窗
        export_meshes(OUT_PATH)
    elif not bpy.app.background and bpy.context.window is not None:
        # 留空 & GUI -> 弹出保存文件选择框
        register()
        bpy.ops.trf.export_trf('INVOKE_DEFAULT')
    else:
        # 留空 & 后台 -> 用默认样例兜底
        export_meshes(os.path.join(DEFAULT_EXPORT_DIR, "building.trf"))

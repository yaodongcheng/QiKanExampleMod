# -*- coding: utf-8 -*-
"""
try_meta_mesh_importer
---
在 Blender 中导入 Bannerlord 样式 .trf (Text Resource Files) 网格。

文件结构 (rfver 4) —— 经 test.trf 实际样例逐行对账验证:
  rfver 4
  mesh <mesh_count>
  <每段一个 mesh>
    mesh_name  <flag>  <material_name>
    <vertex_count>
    <vertex_count 行: x y z>                    # 位置顶点
    <flag>                                       # 样例恒为 0
    <vertex_fvf_count>
    <vertex_fvf_count 段, 每段 3 行:>
        行1: vertex_index  vertex_color  normal_x normal_y normal_z
        行2: uv_x uv_y
        行3: uv2_x uv2_y
    < morph_key_count >                          # 样例恒为 0
    <morph_key_count 段 ...>
    <face_count>
    <face_count 行: i,j,k>                        # 面索引引用 vertex_fvfs, 逗号分隔
    <bone ...>                                   # 骨骼段(可选, 样例未含, 见 read_trf_mesh_skinned_bone)

路径约定 (OpenTrf 工具):
  - 脚本目录: H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/LivingWorldNpcs/tools/OpenTrf/
  - TRF 目录: H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/MyMapTest/EmAssetPackages/TRF/

用法:
  1) 在 Blender 打开脚本编辑器粘贴运行; 或
  2) 命令行:  blender --background --python try_meta_mesh_importer.py
"""

import bpy
import re

# ---------------------------------------------------------------------------
# 数据结构定义 (与截图一致)
# ---------------------------------------------------------------------------
class TrfMesh:
    def __init__(self):
        self.mesh_name = ''
        self.mesh_materials = []
        self.vertex_count = 0
        self.vertex_fvf_count = 0
        self.morph_key_count = 0
        self.face_count = 0
        self.vertices = []        # list[TrfVertex]
        self.vertex_fvfs = []     # list[TrfVertexFvf]
        self.faces = []           # list[TrfFace]
        self.morph_keys = []      # list[TrfMorphKey]
        self.bones = []           # list[str]
        self.bone_weights = []    # list[list[tuple]]  每顶点 (bone_index, weight)


class TrfVertex:
    def __init__(self):
        self.x = 0.0
        self.y = 0.0
        self.z = 0.0


class TrfVertexFvf:
    def __init__(self):
        self.vertex_index = 0
        self.vertex_color = 4294967295   # 0xFFFFFFFF = 不透明白
        self.normal_x = 0.0
        self.normal_y = 0.0
        self.normal_z = 0.0
        self.uv_x = 0.0
        self.uv_y = 0.0
        self.uv2_x = 0.0
        self.uv2_y = 0.0


class TrfMorphKey:
    def __init__(self):
        self.morph_key_time = 0.0
        self.vertex_count = 0
        self.vertices = []
        self.vertex_fvf_count = 0
        self.vertex_fvfs = []


class TrfFace:
    def __init__(self):
        self.vertex_fvf_indices = []


def color_to_argb(color):
    """0xRRGGBBAA / ARGB 整数 -> [a, r, g, b] (0~1 float)"""
    hex_color = int(color)
    b = (hex_color & 0x000000FF) / 255.0
    g = ((hex_color >> 8) & 0x000000FF) / 255.0
    r = ((hex_color >> 16) & 0x000000FF) / 255.0
    a = ((hex_color >> 24) & 0x000000FF) / 255.0
    return [a, r, g, b]


# ---------------------------------------------------------------------------
# 读取函数
# ---------------------------------------------------------------------------
def read_trf(path):
    """读取 .trf 文件, 返回 [TrfMesh]"""
    trf_meta_mesh = []
    with open(path, 'r', encoding='utf-8', errors='replace') as file:
        header = file.readline().strip()          # rfver 4
        if not header:
            return trf_meta_mesh
        version = header.split()[-1]              # "4"
        count_line = file.readline().strip()      # mesh 4
        mesh_count = int(count_line.split()[-1])
        print("rfver:", version, " mesh_count:", mesh_count)

        for _ in range(mesh_count):
            trf_mesh = TrfMesh()
            header_line = file.readline().strip()
            parts = header_line.split()
            trf_mesh.mesh_name = parts[0]
            # 头部: name <flag> <material1> [<material2> ...]
            # 第二个 token 通常为 0 (材质相关标志), 其后为材质名
            for mat in parts[2:]:
                trf_mesh.mesh_materials.append(mat)

            # --- 顶点位置 ---
            trf_mesh.vertex_count = int(file.readline().strip())
            for _ in range(trf_mesh.vertex_count):
                x, y, z = file.readline().strip().split()
                v = TrfVertex()
                v.x, v.y, v.z = float(x), float(y), float(z)
                trf_mesh.vertices.append(v)

            # --- 标志(样例恒为0), 跳过 ---
            file.readline().strip()

            # --- FVF (生成顶点: 位置索引 + 法线 + 顶点色 + UV + UV2) ---
            trf_mesh.vertex_fvf_count = int(file.readline().strip())
            for _ in range(trf_mesh.vertex_fvf_count):
                fvf = TrfVertexFvf()
                # 第1行: vertex_index  vertex_color  normal_x normal_y normal_z
                idx, color, nx, ny, nz = file.readline().strip().split()
                fvf.vertex_index = int(idx)
                fvf.vertex_color = int(color)
                fvf.normal_x = float(nx)
                fvf.normal_y = float(ny)
                fvf.normal_z = float(nz)
                # 第2行: uv_x uv_y
                uvx, uvy = file.readline().strip().split()
                fvf.uv_x = float(uvx)
                fvf.uv_y = float(uvy)
                # 第3行: uv2_x uv2_y
                uv2x, uv2y = file.readline().strip().split()
                fvf.uv2_x = float(uv2x)
                fvf.uv2_y = float(uv2y)
                trf_mesh.vertex_fvfs.append(fvf)

            # --- 形态键 (样例恒为0) ---
            trf_mesh.morph_key_count = int(file.readline().strip())
            for _ in range(trf_mesh.morph_key_count):
                read_trf_morph_key(file, trf_mesh)

            # --- 面 ---
            trf_mesh.face_count = int(file.readline().strip())
            for _ in range(trf_mesh.face_count):
                toks = re.split(r'[,\s]+', file.readline().strip())
                face = TrfFace()
                face.vertex_fvf_indices = [int(toks[0]), int(toks[1]), int(toks[2])]
                trf_mesh.faces.append(face)

            # --- 骨骼 (可选段) ---
            read_trf_mesh_skinned_bone(file, trf_mesh)

            trf_meta_mesh.append(trf_mesh)
    return trf_meta_mesh


def read_trf_morph_key(file, trf_mesh):
    """读取一个形态键(样例未含, 逻辑据结构推断, 未验证)"""
    mk = TrfMorphKey()
    mk.morph_key_time = float(file.readline().strip())
    mk.vertex_count = int(file.readline().strip())
    for _ in range(mk.vertex_count):
        x, y, z = file.readline().strip().split()
        v = TrfVertex()
        v.x, v.y, v.z = float(x), float(y), float(z)
        mk.vertices.append(v)
    mk.vertex_fvf_count = int(file.readline().strip())
    for _ in range(mk.vertex_fvf_count):
        fvf = TrfVertexFvf()
        idx, color, nx, ny, nz = file.readline().strip().split()
        fvf.vertex_index = int(idx)
        fvf.vertex_color = int(color)
        fvf.normal_x, fvf.normal_y, fvf.normal_z = float(nx), float(ny), float(nz)
        uvx, uvy = file.readline().strip().split()
        fvf.uv_x, fvf.uv_y = float(uvx), float(uvy)
        uv2x, uv2y = file.readline().strip().split()
        fvf.uv2_x, fvf.uv2_y = float(uv2x), float(uv2y)
        mk.vertex_fvfs.append(fvf)
    trf_mesh.morph_keys.append(mk)


def read_trf_mesh_skinned_bone(file, trf_mesh):
    """读取蒙皮骨骼段(可选)。样例 .trf 无骨骼段, 用 peek + 回退判断边界:
    若 face 之后下一行是纯数字则视为骨骼段; 否则(如下一个 mesh 头)回退并返回。"""
    pos = file.tell()
    line = file.readline()
    if line == '' or line.strip() == '':
        return
    s = line.strip()
    if not s.lstrip('-').isdigit():
        # 非骨骼段(下一个 mesh 头或 end), 回退到读前位置
        file.seek(pos)
        return
    bone_count = int(s)
    if bone_count == 0:
        return
    # 读取骨骼名
    bone_list = []
    for _ in range(bone_count):
        bone_list.append(file.readline().strip())
    trf_mesh.bones = bone_list
    skinned_vertex_count = int(file.readline().strip())
    for _ in range(skinned_vertex_count):
        # 每顶点: bone_index weight (格式依引擎而定, 未验证)
        file.readline().strip()


# ---------------------------------------------------------------------------
# 在 Blender 中建网格
# ---------------------------------------------------------------------------
def create_blender_mesh(trf_mesh):
    """把一个 TrfMesh 生成 Blender mesh 对象"""
    mesh = bpy.data.meshes.new(trf_mesh.mesh_name)
    obj = bpy.data.objects.new(trf_mesh.mesh_name, mesh)
    bpy.context.collection.objects.link(obj)

    # 顶点: 用 vertex_fvfs 作为实际渲染顶点, 位置取自其 vertex_index 对应的 position
    verts = []
    for fvf in trf_mesh.vertex_fvfs:
        src = trf_mesh.vertices[fvf.vertex_index]
        verts.append((src.x, src.y, src.z))

    faces = [f.vertex_fvf_indices for f in trf_mesh.faces]
    mesh.from_pydata(verts, [], faces)
    mesh.update()

    # 逐顶点法线
    normals = []
    for fvf in trf_mesh.vertex_fvfs:
        normals.append((fvf.normal_x, fvf.normal_y, fvf.normal_z))
    try:
        mesh.normals_split_custom_set_from_vertices(normals)
    except Exception as e:
        print("设置法线跳过:", e)

    # UV 层 (主 UV) —— 按 loop(corner) 索引分配, 保证同一顶点所有 loop 取同一 UV
    if trf_mesh.vertex_fvfs:
        uv_layer = mesh.uv_layers.new(name="UVMap")
        for li, loop in enumerate(mesh.loops):
            vidx = loop.vertex_index
            if vidx < len(trf_mesh.vertex_fvfs):
                fvf = trf_mesh.vertex_fvfs[vidx]
                uv_layer.data[li].uv = (fvf.uv_x, fvf.uv_y)

    # 顶点色: 拆分创建两个属性 rgb (BYTE_COLOR, POINT 域) + alpha (FLOAT, POINT 域)
    try:
        rgb_layer = mesh.color_attributes.new(name="rgb", type='BYTE_COLOR', domain='POINT')
        alpha_layer = mesh.attributes.new(name="alpha", type='FLOAT', domain='POINT')
        for i, fvf in enumerate(trf_mesh.vertex_fvfs):
            a, r, g, b = color_to_argb(fvf.vertex_color)
            rgb_layer.data[i].color = (r, g, b, a)
            alpha_layer.data[i].value = a
    except Exception as e:
        print("设置顶点色跳过:", e)

    # 材质
    for mat_name in trf_mesh.mesh_materials:
        mat = bpy.data.materials.get(mat_name)
        if mat is None:
            mat = bpy.data.materials.new(name=mat_name)
            mat.use_nodes = True
        if mat.name not in [m.name for m in mesh.materials]:
            mesh.materials.append(mat)

    mesh.update()
    return obj


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------
# ---- 默认 TRF 目录: 弹窗会定位到这里, 可改 ----
DEFAULT_TRF_DIR = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\MyMapTest\EmAssetPackages\TRF"

# ===== 要导入的 .trf 路径: 填了就直接用它; 留空 "" 则弹出文件选择框 =====
TRF_PATH = ""


def run_import(path):
    """读 .trf 并导入 Blender 场景"""
    trf_meta_mesh = read_trf(path)
    print("解析到 mesh 数量:", len(trf_meta_mesh))
    for trf_mesh in trf_meta_mesh:
        print("mesh name:" + trf_mesh.mesh_name)
        print("mesh vertex count:" + str(trf_mesh.vertex_count))
        print("mesh vertex fvf count:" + str(trf_mesh.vertex_fvf_count))
        print("mesh face count:" + str(trf_mesh.face_count))
        create_blender_mesh(trf_mesh)
    print("导入完成。")


class TRF_OT_import(bpy.types.Operator):
    """导入 Bannerlord .trf 文件"""
    bl_idname = "trf.import_trf"
    bl_label = "导入 TRF (OpenTrf)"
    bl_description = "导入 Bannerlord .trf 文本资源文件"
    filepath: bpy.props.StringProperty(subtype='FILE_PATH')

    def invoke(self, context, event):
        # 没指定路径时, 弹窗默认定位到 TRF 目录
        if not self.filepath:
            self.filepath = DEFAULT_TRF_DIR
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        run_import(self.filepath)
        return {'FINISHED'}


def menu_func_import(self, context):
    self.layout.operator(TRF_OT_import.bl_idname, text="TRF (OpenTrf)...")


def register():
    try:
        bpy.utils.register_class(TRF_OT_import)
    except Exception:
        pass
    try:
        bpy.types.TOPBAR_MT_file_import.append(menu_func_import)
    except Exception:
        pass


def unregister():
    try:
        bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    except Exception:
        pass
    try:
        bpy.utils.unregister_class(TRF_OT_import)
    except Exception:
        pass


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------
if __name__ == '__main__':
    if TRF_PATH:
        # 填了路径 -> 直接用, 不弹窗
        run_import(TRF_PATH)
    elif not bpy.app.background and bpy.context.window is not None:
        # 留空 & GUI -> 弹出文件选择框
        register()
        bpy.ops.trf.import_trf('INVOKE_DEFAULT')
    else:
        # 留空 & 后台 -> 用默认样例兜底
        run_import(DEFAULT_TRF_DIR + r"\test.trf")

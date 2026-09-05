# OpenTrf

Bannerlord（《骑马与砍杀2：霸主》）`.trf` 文本资源文件的 **Blender 导入 / 导出工具**。

> `.trf`（Text Resource Files）是 Bannerlord 资产管线里以纯文本存放的网格数据格式，记录了顶点的位置、法线、UV、顶点色、三角面索引与材质信息。本工具让 Blender 可以直接读取、修改、再导回这种网格。

---

## 📁 目录 / 文件

| 文件 | 作用 |
|---|---|
| `trf_meta_mesh_importer.py` | **导入器**：把 `.trf` 读入 Blender，重建网格（顶点/法线/UV/顶点色/材质） |
| `trf_meta_mesh_exporter.py` | **导出器**：把 Blender 场景里的 mesh 对象导出成 `.trf`（支持路径/子 mesh 过滤） |

两者**严格对称**：导入器导出的文件可被导出器读回，反之亦然（已做往返验证，几何/面数/FVF 数/包围盒一致）。

---

## 📌 路径约定

- **脚本目录**：`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\OpenTrf\`
- **TRF 目录**：`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\MyMapTest\EmAssetPackages\TRF\`

TRF 目录下：
- `test.trf` —— 原始样例（4 个子 mesh：`mi_ship_2.0` ~ `2.3`）
- `building.trf`、`building_filtered.trf` —— 导出器生成的产物
- `trf_preview/preview.png` —— 导入示例预览图

---

## ⚙️ 环境要求

- **Blender 5.2**（脚本用到 `mesh.corner_normals`、`loop_triangles`、`color_attributes` 等 4.x+ API）
- 无需额外 Python 库（只有 `bpy` / `re`）

---

## 🚀 使用方法

### ① 导入 `.trf` → Blender

打开 Blender → `Scripting` 工作区 → 打开 `trf_meta_mesh_importer.py` → 运行（或 `Alt+P`）。

脚本默认读取 `TRF_PATH`（见脚本末尾配置区），把文件里所有 mesh 导入当前场景。

```python
TRF_PATH = r"H:...\MyMapTest\EmAssetPackages\TRF\test.trf"   # 可改
```

命令行：
```
blender --background --python trf_meta_mesh_importer.py
```

### ② 导出 Blender mesh → `.trf`

打开 `trf_meta_mesh_exporter.py` 运行，导出当前场景所有 mesh 对象，输出到 `OUT_PATH`。

脚本末尾有**配置区**，可设置：

```python
OUT_PATH = r"H:...\MyMapTest\EmAssetPackages\TRF\building.trf"   # 导出路径
EXPORT_MESH_NAMES = ['mi_ship_2.0', 'mi_ship_2.2']               # 白名单(只导出这些, 留空=全部)
SKIP_MESH_NAMES = ['mi_ship_2.1']                                 # 黑名单(忽略这些)
```

命令行带参（可选）：
```
blender --background --python trf_meta_mesh_exporter.py -- D:/out.trf mi_ship_2.0 mi_ship_2.2
```
第一个参数是输出路径，后面是要导出的 mesh 名（白名单）。

运行后会自动打印「导出/跳过」清单，方便核对。

---

## 📐 `.trf` 文件结构（rfver 4）

```
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
    <morph_key_count>
    <morph_key_count 段...>
    <face_count>
    <face_count 行: i j k>                       # 面索引引用 vertex_fvfs
    <bone ...>                                   # 骨骼段(可选)
end
```

**要点**：面索引引用的是 **FVF（生成顶点）** 而不是位置顶点；一个 FVF 通过 `vertex_index` 指向具体位置顶点，并携带自身法线/UV/顶点色。

---

## ⚠️ 已知说明

- 本样例（`test.trf`）里的 4 个 mesh 都是**静态网格**，不含骨骼段与形态键；相关读取/导出逻辑已按结构预留，若遇到带蒙皮/形态键的 `.trf` 需按实际格式微调。
- 导入时保留 `.trf` 原始坐标（未做居中/轴转换）。如需居中到原点或 Y-up→Z-up，可自行在 `create_blender_mesh` 中调整。

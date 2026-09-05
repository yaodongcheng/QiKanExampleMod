# 地形 / 地图资产工具

> 域：战役大地图制作工具（素材 → 高度图/材质图、.trf 网格 Blender 编辑）。2026-09-05 登记。

## ExportHeightMatMap — 地图素材 → 高度图 + 材质图管线

**解决什么问题**：任意分辨率日本地图素材（hires 15840×10080 / TaikouMap2 704×448）→ ModKit 可导入的 16bit 高度图 + RGBA 材质图（四通道语义：R草/G林/B沙/A雪），并保证「不同分辨率素材 → 同一世界尺寸（2048×1280m）、世界坐标逐像素对齐」。

**文件**：`tools/ExportHeightMatMap/make_heightmap.py`（素材 `SourceMap/`、产物 `Output/`，两者 gitignore）

**用法**：
```bash
python tools/ExportHeightMatMap/make_heightmap.py                # 主档 4096x2560
python tools/ExportHeightMatMap/make_heightmap.py 1024 640      # 任意档
python tools/ExportHeightMatMap/make_heightmap.py 1024 640 <src.png> <out_dir>   # 换素材/输出目录
```

**关键参数**（脚本内常量，注释均含依据）：
- `MASTER_W = 15840` — 几何阈值校准尺度；**小素材自动 LANCZOS 放大到该尺度再跑**（尺度标定，防 704 图阈值全军覆没）
- `SIG_BASE_SRC_PX = 150.0` — 山影差分基底 σ（150=山域级平顺基线 / 60=山脊级细纹；**待 ModKit 实测选定**）
- `PEAKS = [(cx, cy, 锥半径px, 峰高, 山体基底高, 山体半径px)]` — 地标表（富士 == (10500,7000) 用户亲报）
- `T_BEACH=150`、`OPEN_ITERS=2`、白点簇雪帽规则（≥最大簇 30%）

**核心坑（全踩过，判断与修法见 Knowledge/骑砍2战役地形制作管线.md §六）**：
- 🔴 **富士蓝白雪 ≠ 水**（蓝判会被当湖判 0 → 黑斑/蓝湖）→ 雪帽检测（最大白点簇）+ PEAKS 地标区豁免海清零；**高度图/材质图两边同源禁用分叉**
- 材质图 A=雪 被 PNG 查看器当 alpha → **人看用 `matpreview_*.png`**，引擎版保持 RGBA
- 素材与产物已 gitignore，产出=脚本重跑

## OpenTrf — .trf 网格 Blender 导入/导出器

**解决什么问题**：Bannerlord `.trf`（Text Resource Files，纯文本网格：顶点/法线/UV/顶点色/三角面/材质）可直接用 Blender 读取、编辑、导回——素材网格资产的 Blender 化修改链路。

**文件**：`tools/OpenTrf/trf_meta_mesh_importer.py`（导入器）+ `trf_meta_mesh_exporter.py`（导出器，支持路径/子 mesh 过滤）

**关键事实**：
- 两器**严格对称**（导入器导出的文件可被导出器读回，已往返验证：几何/面数/FVF/包围盒一致）
- `.trf` 结构（rfver 4）：`rfver 4 → mesh <count> → <mesh_name/flag/material_name + 顶点数据...>`，经 test.trf 逐行对账验证
- **路径约定**：脚本目录 = `tools/OpenTrf/`；TRF 目录 = `Modules/MyMapTest/EmAssetPackages/TRF/`（test.trf 样例 4 子 mesh `mi_ship_2.0~2.3`；building.trf/trf_preview 为产物示例）

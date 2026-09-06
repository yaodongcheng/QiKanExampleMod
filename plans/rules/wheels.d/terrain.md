# 地形 / 地图资产工具

> 域：战役大地图制作工具（素材 → 高度图/材质图、.trf 网格 Blender 编辑）。2026-09-05 登记。

## ExportHeightMatMap — 地图素材 → 高度图 + 材质图管线

**解决什么问题**：任意分辨率日本地图素材（hires 15840×10080 / TaikouMap2 704×448）→ ModKit 可导入的 16bit 高度图 + RGBA 材质图（四通道语义：R草/G林/B沙/A雪），并保证「不同分辨率素材 → 同一世界尺寸（2048×1280m）、世界坐标逐像素对齐」。

**文件**：`tools/ExportHeightMatMap/make_heightmap.py`（素材 `SourceMap/`、产物 `Output/`，两者 gitignore）；配套 `native_quantiles.py`（Native 陆地 CDF 预计算表，生成物勿手改）；实验沙盒 `proto_relief.py`（参数调优迭代用，产物 `Output/_probe/`）

**用法**：
```bash
python tools/ExportHeightMatMap/make_heightmap.py                # 主档 4096x2560
python tools/ExportHeightMatMap/make_heightmap.py 1024 640      # 任意档
python tools/ExportHeightMatMap/make_heightmap.py 1024 640 <src.png> <out_dir>   # 换素材/输出目录
```

**🔴 高度细节 v3（2026-09-06，Native 卡拉迪亚水准；对用户两轮打回的终版）**：验收 = 陆地 CDF 逐分位对齐（q05 0.025/q50 0.255/q95 0.567）+ 山窗 std 0.124（native 0.069-0.164）+ 形态（黑谷带/亮峰块/树状支脉）。模型：
- **宏观**：σ=200源px 大平滑 → 分档 h_base（山档加陡 0.38-0.74，γ1.4 后 0.26-0.65）——画法明暗≠海拔（v1 病根）
- **细节**：多倍频 fbm **整域山脊变换**（1-|2n-1| 过零=连通排水网）+ 源图笔触方向场 + 半分辨软烤 + 宏脊/山块/宽谷带三层
- **分布匹配（枢轴）**：陆地 rank 匹配 **Native CDF**（`native_quantiles.py` 129 分位预计算表），匹配前 core+**3.6×massif 结构放大**，**必须放平滑之后**，豁免区（富士/雪帽）保留原裁定值
- **三处必死坑**：①评估口径=分布CDF+窗级+形态+**熵（B/land-px）**四验；②rank 匹配不动空间形状（先结构放大）；③hmv 全流程必须 float 域——经 uint8 中段量化=16bit 图只有 8bit 信息（熵/压缩率暴低）；
- **熵维度（2026-09-06 v3G 硬指标）**：PNG 大小对等口径=**每陆地像素字节**（native 1.39 vs 我们 1.87——总字节差 5 倍主因是陆地占屏 17% vs 76% + 像素数 10.5M vs 16.8M，非细节量级）；熵钩子=σ0.006 全陆白噪（×4·h·(1-h) 两端保护窗）+ 重排后细节回补
- 材质图（草/林）用未大平滑的 `h_base_cls`（σ25）——分类是画法语义，两把尺子

**关键参数**（脚本内常量，注释均含依据）：
- `MASTER_W = 15840` — 几何阈值校准尺度；**小素材自动 LANCZOS 放大到该尺度再跑**（尺度标定）
- `SEED = 20260906` — 细节噪声固定种子（同素材同图，可复现）
- `HEIGHT_GAMMA = 1.4` — 压平曲线（普通地表压平、富士独大，用户裁定）；`NQ`（native_quantiles.py，缺失自动跳过匹配——发布环境兼容）
- `PEAKS = [(10500,7000,…富士…)]` — 地标表（用户亲报坐标）+ 雪帽检测（已内置）

**坑点回炉**：①单一频段/单层噪声=斑点或绒毛；②局部对比归一（det/局部rms）会把对比洗没；③方向场用宏观渐变=指纹同心环（熔坏了），必须用源图笔触方向；④r≈0 处（|2n−1| 的奇异点）会产生窄深无底洞——clip [−0.36, 0.34] 防虫洞。其余 v1 坑见 Knowledge/骑砍2战役地形制作管线.md §六。

**核心坑（全踩过，判断与修法见 Knowledge/骑砍2战役地形制作管线.md §六）**：
- 🔴 **富士蓝白雪 ≠ 水**（蓝判会被当湖判 0 → 黑斑/蓝湖）→ 雪帽检测（最大白点簇）+ PEAKS 地标区豁免海清零；**高度图/材质图两边同源禁用分叉**
- 材质图 A=雪 被 PNG 查看器当 alpha → **人看用 `matpreview_*.png`**，引擎版保持 RGBA
- 素材与产物已 gitignore，产出=脚本重跑

## 场景黑/白诊断 — atmosphere time_of_day / tileset 贴图争议（2026-09-05~06 登记，BigMapLearn 实机）

**解决什么问题**：ModKit 打开战役主图场景，地形整体**黑**或整片**白**（材质/层/权重配置"正确"却不对）——确定性排查顺序与修法。

**黑 = `atmosphere.xml` 的 `time_of_day` = 22.000（深夜 10 点）**（实锤）。
- **原版 main_map 的 atmosphere.xml 存的就是 22:00**——游戏里被游戏时间覆盖正常显示；**编辑器按存储值渲染 → 全黑剪影**（"用原版 atmosphere.xml" ≠ "白天大气"）
- 🔴 **UI 无 time_of_day 设置项**（Atmosphere Inspector 属性面板里没有）——只能改 XML
- 修法：`Modules/<mod>/SceneObj/<Scene>/atmosphere.xml` → `<value name="time_of_day" value="10.000"/>`（6~12 = 白昼）。同文件易混"夜/晨大气组"：`color_grade_name`（harsh / cg_50c_5b）、`is_indoor`、`fog_density/fog_color`、`global_ambient`、`middle_gray`。

**白 = 层引用 WorldMap 图集页贴图 + `vista_tileset` 为空**（2026-09-06 单步控制实验实锤）：
- 判定链：单层 desert_a（普通贴图 `desert_floor_*`）彩色 → 仅加原版 default 层（图集贴图 `ground_grass_b_d_mainmap`）→ 全白 → 仅挂 `vista_tileset="WorldMap"` → 全彩
- 规则：**层纹理名带 `*_mainmap` / `main_map_*`（图集页资源）→ 场景必须挂 `vista_tileset="WorldMap"`**，否则整层渲染白/丢失；`desert_floor_*` 等普通贴图不依赖 tileset
- 陷阱：`references.txt`、node masks（`layer_is_used_mask_*` 位图→255 全用）、terrain.bin WGHT（旧权重通道映射到新层表第一层——加层后必须逐层 Import 权重复写）都被怀疑过、**均非根因**——教训：黑/白问题先在 atmosphere.xml 与 tileset/贴图组合上隔离，再做掩码/权重理论
- **观感差异（"同一个数据两种色调"）→ 先对 Vista Textures 段**：`vista_diffuse_blend_type`（1=原版）/ `vista_layer_detail_distance`（10000=原版）/ `vista_albedo_multiplier`（0.67=原版）/ `colormap_detail_level`（0=原版）——**原版"雪山白"观感 = 白岩贴图 × Vista 冷调 × 0.67 明度，不是雪线/动态雪**（BigMapLearn 半残值 blend 0/layerdist 1/albedo 1/colormap -1 = 黄土木；对齐后即雪白，2026-09-06 实机）
- **方法学经验**：单变量逐步实验（每步只动一处 + 每步备份 + 一次打开看结果）是定位 scene 渲染问题的最快路径——"一把梭注入×N 字段"必然无法定位

**文件**：`Knowledge/骑砍2战役地形制作管线.md` 三·十五（BigMapLearn vs BigMapLearn2 逐字段完整对照实录——黑/正常两场景仅剩字段清单）。

## OpenTrf — .trf 网格 Blender 导入/导出器

**解决什么问题**：Bannerlord `.trf`（Text Resource Files，纯文本网格：顶点/法线/UV/顶点色/三角面/材质）可直接用 Blender 读取、编辑、导回——素材网格资产的 Blender 化修改链路。

**文件**：`tools/OpenTrf/trf_meta_mesh_importer.py`（导入器）+ `trf_meta_mesh_exporter.py`（导出器，支持路径/子 mesh 过滤）

**关键事实**：
- 两器**严格对称**（导入器导出的文件可被导出器读回，已往返验证：几何/面数/FVF/包围盒一致）
- `.trf` 结构（rfver 4）：`rfver 4 → mesh <count> → <mesh_name/flag/material_name + 顶点数据...>`，经 test.trf 逐行对账验证
- **路径约定**：脚本目录 = `tools/OpenTrf/`；TRF 目录 = `Modules/MyMapTest/EmAssetPackages/TRF/`（test.trf 样例 4 子 mesh `mi_ship_2.0~2.3`；building.trf/trf_preview 为产物示例）

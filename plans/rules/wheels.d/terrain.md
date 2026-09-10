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
- ⚠️ **该结论的前提**：**层数=1 时成立**（BigMapLearn 新建 1 层时代的实验）——tileset 修好≠层数问题修好。
- 🔴 **第二种白（2026-09-06 用户实机抓到，上午结论套错场景的教训）**：**层数被注入/增加后，节点 `layer_is_used_mask_*` 不自动更新**——新建场景 mask 按「新建时层数」写入（1 层 → 全 1 = 只认 layer0），8 层时代 mask 仍是 1 → **除 default 外 7 层全不渲染 + default 冬季变体雪白贴图 = 全图白**（三·十五头号嫌疑/copy1 实机白）。修法：**层数变化后节点 mask ×4季 → 255**（scene.xscene 正则替换 layer_is_used_mask_(summer|fall|winter|spring)="1" → "255"；改前备份）。判据：**mask 值唯一值=255（多值=内容化原版）**。
- 🔴 **注入场景后必须同批对齐 Vista Textures 段**：`vista_diffuse_blend_type=1 / vista_layer_detail_distance=10000.000 / vista_albedo_multiplier=0.670 / vista_detail_tile=1.000 / colormap_detail_level=0`（原版组）；半残值（blend 0/layerdist 35/tile 20）=「黄土木」色调。
- 陷阱：`references.txt`、terrain.bin WGHT（旧权重通道映射到新层表第一层——加层后必须逐层 Import 权重复写）都被怀疑过、**均非根因**——教训：黑/白问题先在 atmosphere.xml 与 tileset/贴图组合上隔离，再做掩码/权重理论；**每个白案例先问「层数=1 还是 >1」再选修法**
- **观感差异（"同一个数据两种色调"）→ 先对 Vista Textures 段**：`vista_diffuse_blend_type`（1=原版）/ `vista_layer_detail_distance`（10000=原版）/ `vista_albedo_multiplier`（0.67=原版）/ `colormap_detail_level`（0=原版）——**原版"雪山白"观感 = 白岩贴图 × Vista 冷调 × 0.67 明度，不是雪线/动态雪**（BigMapLearn 半残值 blend 0/layerdist 1/albedo 1/colormap -1 = 黄土木；对齐后即雪白，2026-09-06 实机）
- **方法学经验**：单变量逐步实验（每步只动一处 + 每步备份 + 一次打开看结果）是定位 scene 渲染问题的最快路径——"一把梭注入×N 字段"必然无法定位

**文件**：`Knowledge/骑砍2战役地形制作管线.md` 三·十五（BigMapLearn vs BigMapLearn2 逐字段完整对照实录——黑/正常两场景仅剩字段清单）。

## 地图场景必备实体 border_min / border_max — 大地图相机边界（2026-09-08 登记，Taikou 日本图实机）

**解决什么问题**：克隆/新建战役大地图（Main_map）时**忘记抄这两个实体** → 相机"空气墙"（v1.2.12 静默降级）/ 进图即崩（v1.5.x）——不崩不报错、纯行为异常，曾误判成"操作习惯"。

> 🔴 **同域姊妹篇（2026-09-09 京交互链实锤，雷 37）——定居点实体组 4 娃**：`bo_town` **只碰射线碰撞体**（bo_sphere_collider + `body_flag name="only_collide_with_raycast"`，地图拾取唯一入口——`SelectEntitiesCollidedWith` 掩码 79617 只认它，普通 mesh/贴花不作为拾取目标）+ `town_circle_decal`（tag map_settlement_circle，黄圈视觉+CircleLocalFrame）+ `gate_position`（tag main_map_city_gate）+ `banner_pos`（tag map_banner_placeholder，墙级门旗组）。**缺 = 无黄圈/无 hover 卡/点击无反应/进不了城**；且探针/寻路/可通行性全绿极具迷惑性（京实机 4 轮误判实录）。参 `Knowledge/自定义世界内容包从零起步必备清单.md` 1.5。

**关键事实**（全反编译实锤）：
- **地图边界 = 两个普通场景实体的坐标**，没有配置文件、没有属性：`SandBox.MapScene.GetMapBorders`（SandBox.dll）按名字硬查 `GetFirstEntityWithName("border_min"/"border_max")`；`MapCamera.ComputeMapCamera`（SandBox.View.dll）**每帧**把相机目标钳进矩形 [min,max]
- 🔴 **版本分岔（同一缺实体两种死法）**：**v1.2.12 有兜底**——缺实体 → min=(0,0) / max=**(900,900)** / height=670（静默，零提示）→ 相机墙在 x=900/y=900；**v1.5.x 无兜底**——直接解引用 null → 进图崩。任何内容包造图都必须在两个版本都检查
- **取值 = 地形实际范围**：scene `<terrain>` 节点 `node_dimension × node_size`（Taikou 例：16×10 × 128m = 2048×1280，与 physics_world / flora_bounding_rect 三处互证）
- **border_max 的 z 值 = 相机最大缩放距离 + 远裁剪面（远面 = z×4）**：官方 bigmap 620 / 织丰 1000——别抄官方值，按地图尺寸斟酌
- 边界实体 = 纯坐标标记，**不影响 navmesh，加/删无需重生成**

**调用**：无代码调用——纯场景数据，官方原样写法（<entities> 顶部）：
```xml
<game_entity name="border_min" old_prefab_name="">
  <transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>
</game_entity>
<game_entity name="border_max" old_prefab_name="">
  <transform position="2048.000, 1280.000, 1000.000" rotation_euler="0.000, 0.000, 0.000"/>
</game_entity>
```
参考值：官方 bigmap (62,30,0)/(790,640,620)；织丰 (87.4,105.4,-7.98)/(2100,2100,1000)

**文件**：场景 = `Modules/<mod>/SceneObj/Main_map/scene.xscene`；诊断日志 = `CampaignMode/MapBorderDiagnosticPatch.cs`（`GetMapBorders` Postfix，`[MapBorder]` 打一行 min/max/height，命中引擎兜底值 (0,0)/(900,900)/670 自动警示——重建地图后的验证入口）

**配套纪律**：地图场景克隆必备实体清单 = **border_min/border_max + 12 个官方地图脚本实体**（完整清单与排雷实录见 `plans/太阁数据加载taikou-campaign-boot-20260907.md` 雷 28/34）；出图前 `grep scene.xscene border_min` 必查；坑点格式全文见 `plans/rules/pitfalls.md`「自定义地图相机"空气墙"」

**🔴 2026-09-10 补充（相机到底怎么定位的 —— 别再去场景里找"相机锚点"）**：
- 相机**初始目标 = 主队运行时坐标**（`MapCameraView.Initialize` 读 `MobileParty.MainParty.Position2D` + 地形高度），**不是**场景实体；`camera_top`（官方 Main_map 里有）是**死实体**，全游戏 2 万个文件 0 引用。
- 所以「进图看不见玩家」有两种病因，**先分诊再动手**：①**border 缺实体** → 相机被钳在兜底盒 `900×900`，玩家在盒外（真凶，数据修）；②**出生点写晚了** → 相机读坐标时玩家还在引擎默认坐标（治本 = 世界创建期写，见 `wheels.d/campaign-mode.md` 卷七）。
- 原 `MapScreenCameraPatch`（Harmony 补丁，事后拉相机）**已退役删除**——它与建号内容的 teleport 重复，且掩盖了 ① 这个真因。

## OpenTrf — .trf 网格 Blender 导入/导出器

**解决什么问题**：Bannerlord `.trf`（Text Resource Files，纯文本网格：顶点/法线/UV/顶点色/三角面/材质）可直接用 Blender 读取、编辑、导回——素材网格资产的 Blender 化修改链路。

**文件**：`tools/OpenTrf/trf_meta_mesh_importer.py`（导入器）+ `trf_meta_mesh_exporter.py`（导出器，支持路径/子 mesh 过滤）

**关键事实**：
- 两器**严格对称**（导入器导出的文件可被导出器读回，已往返验证：几何/面数/FVF/包围盒一致）
- `.trf` 结构（rfver 4）：`rfver 4 → mesh <count> → <mesh_name/flag/material_name + 顶点数据...>`，经 test.trf 逐行对账验证
- **路径约定**：脚本目录 = `tools/OpenTrf/`；TRF 目录 = `Modules/MyMapTest/EmAssetPackages/TRF/`（test.trf 样例 4 子 mesh `mi_ship_2.0~2.3`；building.trf/trf_preview 为产物示例）

## 实机地形导出 — custom.export_heightmap（2026-09-07 定案，织丰实机闭环）

**解决什么问题**：ModKit 打不开的场景（织丰 Main_map 依赖 ButterLib/Harmony/MCM/UIExtenderEx 四前置，编辑器加载即失败）或客户端黑盒数据，如何拿到**地形高度图 16bit PNG + 真实规格五元组（X/Y/Size/Dim/Scale）**——游戏运行中一条命令搞定。

**文件**：`CampaignMode/Tools/TerrainExportCommands.cs`（namespace LivingWorldNpcs；命令组 `custom`，参数一律忽略）。产物 `Debug/HeightmapExport/`（`heightmap_16bit.png` + `info.txt` + `tracelog.txt`），**该目录已 gitignore**。

**调用**：游戏内 `~` → `custom.export_heightmap`（横在战役大地图或任意 Mission；同步执行，4096² 采样数十秒内完成，进度写日志）。

**关键 API 三段论（实机逐一字验证，2026-09-07）**：

| 类别 | API | 客户端行为 |
|---|---|---|
| ✅ 有效 | `Scene.GetTerrainData`（nodeDim/nodeSize/layerCount/layerVersion） | 两版一致：真值（原版/织丰都 OK） |
| ✅ 有效 | `Scene.GetTerrainNodeData(x,y, out vtx, out quadLength, out min, out max)` | **原版=垃圾未初始化（vtx=-394260642）→ fallback 256 quads/节点；织丰=真值（vtx=257, quad=0.516）**——值入口自动双判 |
| ✅ 有效 | `Scene.GetTerrainHeight(Vec2, bool checkHoles)` | 逐点采样，客户端唯一安全活路（运行时寻路/射线同源）。**注意 checkHoles 参数保留 true** |
| ✅ 有效 | `Scene.GetTerrainMinMaxHeight`（Scale/min） | 全版本有效 = 场景 max_height 参数（导入面板 min/max 口径） |
| 💣 炸弹 | `Scene.GetTerrainHeightData` **永久禁用** | 原版=空壳；**织丰=direct native 崩溃（托管 catch 不住、引擎 crash handler 都不弹、tracelog 冻结于调用行）**——只禁不调 |
| 💣 炸弹 | `Scene.GetTerrainMemoryUsage` | 同族（原版返回 0）；禁用 |
| ⚠️ 空壳族 | 材质层权重（GetTerrainWeight/Materialmap/Weightmap/SplatLayer/LayerWeight） | **client 引擎 DLL 全 0 命中——运行时无材质权重 API**；唯一候选 `GetTerrainPhysicsMaterialIndexData`（PHYM 段，物理材质索引 short[]）待 probe 定案（见 custom.export_terrainlayers） |

**坑点回炉（全踩过）**：
- 🔴 **崩溃定位法**：DebugLogger 每行独立 Write 但有缓冲，进程崩溃丢尾行 → `TraceLog`（AutoFlush StreamWriter 直写 `tracelog.txt`）逐调用打点，**冻结行 = native 崩溃点**（本次 3 轮崩溃全部靠它一行定位）
- 🔴 **朝向**：引擎世界 Y+ 指向**南** → 采样 y 需反转（`wy = (H-1-y+0.5)*quad`），否则输出图上下颠倒（织丰实机目验：翻转后北海道上/九州下/四国左中下、日本海在上侧）
- 🔴 **拼图/精度**：采样网格 = nodeDim × 每节点格数（有真值 quadLength 时 = 世界尺寸/quadLength；无则 256 格/节点——与原版精密度对齐）；织丰 = 16×16 节点 × 257 顶点（256 quads）→ 4096²、世界 2112×2112m、0.516m/顶点、13 层、Scale 20.968m（min -8）
- **归一化**：GetTerrainMinMaxHeight 区间 → [0, 65535]（info.txt 记录区间，导入面板照抄）
- **PNG 编码零依赖**（PNG 签名 + IHDR/IDAT/IEND + zlib(DeflateStream raw) + CRC32/Adler32 手写）；**离线同源验证过**（PIL 打开 I;16 逐像素一致）。注：Filter=0 保守档 4.7MB；PIL 自适应过滤 3.8MB——纯编码差异不伤数据
- **getter 逐调用分离打 trace**（ContainsTerrain/HasTerrainHeightmap/GetTerrainData/…），任何一步崩一查即知
- 已知规格：原版 16×16@53m/848m/8 层/Scale 25；织丰 16×16@**132m**/2112m/13 层/Scale 20.968——织丰世界 2.5 倍大、顶点密度 0.516m、**织丰地图列岛全图 = 4096² (h>0 判定)**；海域 ~76%

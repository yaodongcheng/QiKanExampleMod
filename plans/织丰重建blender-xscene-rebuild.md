# Blender 还原织丰全部 SceneObj 场景

> 姊妹篇：**[织丰重建-资源替换机制.md](织丰重建-资源替换机制.md)**（开场动画/BGM/UI 素材替换，本 plan 的运行时资源篇）。
> 2026-09-06 定稿。目标：在 Blender 工程里逐个还原织丰（Shokuho/织丰太阁扩展）**全部 125 个 SceneObj 场景**——每个场景一个 `.blend`，含全部实体摆位、网格、骨架。

---

## 一、结论（一句话）

**可行，且已完成 60% 基础件**：织丰全部资产（纹理/网格/材质）已从 pack0-6.tpac 无损提取；xscene+Prefabs XML 解析管线已通；Blender 5.2 唯一稳定通道 = **纯 Python obj 解析 + from_pydata**（官方 fbx/obj/collada 导入器全军覆没，见第三节铁律）。**卡死教训已定位**：Main_map 全量还原 34.7 万对象导致 Blender 保存时系统卡死——**后续一切构建必须设对象数上限并按场景分批**。

## 二、已完成（可复核）

### 2.1 资源提取（`MB2_Version\MB2_1.2.12\extracted_sho\`）
| 目录 | 内容 | 数量 |
|---|---|---|
| textures/ | PNG 按源组分类（GauntletUI/items/sceneobjects/basemesh…） | 4498 |
| meshes/ | OBJ 按名前缀分类（body/head/sho/bo…） | 3203 |
| materials/ | 材质元数据 `.mat.txt`（shader/贴图引用全字段） | 842 |
| meta/ | 占位（PhysicsShape/Particle 等工具不可导出物） | 621 |
| fbx/ | 带骨骼角色（body/head/hands/feet/japan；人体用 Native human_skeleton，56 骨） | 35 |

### 2.2 tpaccli 工具增强（`tools/face-pipeline/tpactool/TpacToolCLI/`）
- 新命令：`groups`（按 Source 源目录分组统计）、`segs`（段类型分布）、`missingrefs`（跨包材质依赖诊断）、`listformats`（Assimp 导出格式）
- `dump` 加固：**多目录 packdir**（逗号分隔）、**缺失依赖占位 resolver**（防 UnresolvedDependence 中断）、子目录分类输出、纹理在 fbx/obj 模式跳过、`--filter ""` 全量
- fbx 导出管线：骨架选择（优先 human 名→动画多数派→兜底）、蒙皮网格才绑骨（SkinDataSize>0）、**TPAC_NO_SKEL=1 环境开关**（去蒙皮演示）
- 引擎取证结论（全部二进制证据）：`rm_instance_`/RDC/双包冲突/EmAssetPackages 组包形态——织丰 pack0-6 是**客户端构建**，ModKit 编辑器装配必炸，编辑路线永久放弃

### 2.3 场景解析（`tools/face-pipeline/scene_rebuild/`）
- `parse_scene.py`：xscene 实体 → Prefabs XML 树（meta_mesh_component = 真网格名）→ 展开统计（Main_map：2580 实体 / 486 metamesh，OBJ 覆盖率 74.3%，缺的是 Native 资源/编辑器概念物）
- `build_blends.py`：Blender 5.2 batch 脚本（实体遍历→prefab 树递归→obj 摆位）
- 数据确认：`Prefabs/*.xml`（sшo_architecture/map_icons…）为整合定义；xscene transform = position/rotation_euler(弧度)/scale(可选)；实体格式有带/不带 name 两种 + children 嵌套

## 三、铁律（本工程不可违反的硬事实/教训）

1. **Blender 5.2 只有 fbx/gltf 两个导入算子**（无 obj、无 collada；io_scene_obj 模块不存在）。obj+from_pydata 是唯一通用通道。
2. **fbx 导入必崩**：我们 FbxSharpie 产出的蒙皮 fbx（BlendShape/Deformer 通道）触发 Blender 5.2 导入器断言 `len(full_weights) >= num_shapes_assigned_to_channel`；参数无可绕过；dae/gltf2（Assimp 原生）也崩/不可用。
3. **对象数必须设上限**（教训：Main_map 34.7 万对象 → 保存写盘+内存击穿，系统卡死；用户重启）。今后每场景构建**先定量预估（实体数×平均 135 件），上限默认 5 万对象/场景**，超限自动截断并标注。
4. UV 解析：f 面 `v/vt/vn` 三段；取 uv 时 `[1]` 下标（tuple 只有 0/1 两段时注意）
5. 后台任务不得 overwrite 脚本运行中文件（确保 log 单独文件、flush 每 500 对象）。
6. **ModKit 战线永久关闭**：织丰客户端构建（pack0-6）无法在编辑器装配（引擎编辑器路径 rm_instance_ 断言，客户端无此逻辑 → 客户端正常）；不再耗费。
7. 所有代码改动回归 tpactool 工程；**git 提交归用户**（铁律 23）。

## 四、TODO（按优先级）

### P0 — 构建稳化（半天）
- [ ] `build_blends.py`：每场景对象上限（默认 50k）；实体循环每 200 个打印实体进度（flush）；保存用压缩 blend（`save_as_mainfile(compress=True)`）
- [ ] 每场景独立进程跑（不并行多个 blender）；失败重试单场景
- [ ] 输出规范化：`blend_projects/场景名.blend` + `场景名_report.txt`（placed/missing/对象数/耗时）
- [ ] 保存时的磁盘占用预估（对象数 × ~20KB）→ 超限提前告警

### P1 — 125 场景批量（2-3 天，超时 60 分钟）
- [ ] 场景清单排序（实体数降序）；小场景打包批跑（task batch 每批 10 场景）
- [ ] Main_map 专项：对象截断策略（抽样 per prefab / 限定 top-40% 实体）——标注"非全量"
- [ ] 每场景人工抽查 1 个（打开 blend 截图：原始 scene 对照，检查比例/旋向）
- [ ] missing 网格清单归并：分出 native 资源（后续用 Native 包补导出）与真缺（报作者）

### P2 — 角色骨架 + 动画（2 天，独立子任务）
- [ ] tpaccli 新命令 `skel`：dump human_skeleton（bones/RestFrame/Parent）→ JSON
- [ ] tpaccli 新命令 `anim`：dump SkeletalAnimation 22 条（BoneAnims 帧/时间）→ JSON
- [ ] Blender 端 `char_blend.py`：JSON → Armature（rest pose 建骨）+ 网格（obj 几何）+ 关键帧重放（Timeline 每帧插值）
- [ ] 展示工程：`01_characters.blend`（男/女/日式/儿童/头/手/脚 + 坐姿/战斗动画采样）
- [ ] 材质贴图挂接（二阶）：按 mat.txt 的 diffuse/normal/specular 引纹理名 → Blender Principled BSDF；PVR 简化 flat color 兜底

### P3 — 其他展示工程（半天）
- [ ] `02_settlement_prefabs.blend`：16-20 个大地图 prefab 陈列阵（每 prefab 独立分格）
- [ ] `05_showcase.blend`：道具 12 件（武器/旗帜/船/建筑件）
- [ ] 视口 Camera/光照预设（每工程自动放置太阳+俯视相机）

### P4 — 收尾（半天）
- [ ] 产物索引 README：每 blend 内容摘要表（场景名/实体数/对象数/缺失网格）
- [ ] tpactool 改动提交清单（列文件，用户自行 git）
- [ ] 可选：Native 资源补导（map_rock/mi_barrels 等 125 个缺失项，用 Native/EmAssetPackages 对应组包导出）

## 五、执行顺序汇总

```
P0 稳化脚本(设上限) → P1 批量场景(先小后大, Main_map 降级策略) → P2 角色/动画 → P3 展示工程 → P4 索引文档
```

## 六、风险与未知

- 125 场景中部分（如 cutscene/battle）实体极少或依赖 native 场景资源 → 还原度打折（标注）
- Prefab 互相嵌套（A 含 B prefab）当前实现只展开一层注册树——已验证 castle/village 展开是完整子树，但**个别 prefab 可能引用未注册名 → 缺件落入 missing**（P1 分析报告）
- 比例/朝向：xscene rotation_euler 弧度直用；无 scale 的实体默认 1（游戏内世界单位与 Blender 米相同）
- 大场景（tau/…）对象截断后与原场景 **非逐实体对应**，仅展示性还原（如实标注）

## 七、验收标准

- 每个场景 blend 可打开（或报告截断原因）；打开后顶视图能辨认出城堡/城镇布局
- report 文件齐全（placed/missing/对象数/耗时）
- 角色工程：Armature 骨骼树可播放（旋转/坐姿动画可见）

---

## 附：战役大地图（Main_map 地形）子工程快照（2026-09-06 更新）

> 与 Blender 主线并列的另一条战线：**在 ModKit 里重建战役世界地图地形**（Taikou 新世界 / 日本列岛）。本快照记录状态与下一步；现场档案 = `Knowledge/骑砍2战役地形制作管线.md`（三·八~三·十五 全套逐字段实录）+ `plans/rules/wheels.d/terrain.md`（轮子）。实验场：`MB2_1.2.12\...\Taikou\SceneObj\`（BigMapLearn=重建 / BigMapLearn2=备份基线 / Main_map_onlyTerrian=原版剪枝基准）。

### 状态：还原原版 ≈ 90% 达成
| 项 | 状态 |
|---|---|
| 高度图（4097 导入）/ 四件套 16×16@53 / min-max 0-25 | ✅ |
| 8 层材质（XML 注入原版层块）+ tileset=WorldMap + 掩码 255 | ✅ |
| 8 张原版权重逐层导入（default←layer0…river←layer7） | ✅ |
| 大气（白天 time_of_day）、水（1.8/true/ocean_g? 待水材质）、Vista 段（雪山观感） | ✅（水材质 `wat_main_map_ocean_g` 待改一行） |
| **树（flora.bin 原版样本 76,048 棵）** | ✅ 拷贝 OnlyTerrian | 
| 季节雪流图 / path 河道曲线 / 背景实体装饰 | ⏳ 后续 |

### 三大渲染谜（已闭环，凡"黑/白/色调异"先对表）
1. **黑** = `atmosphere.xml` time_of_day=22（深夜；UI 无此属性只能改 XML）
2. **白** = 层贴图为图集页（`*_mainmap`）但 `vista_tileset` 空 → 挂 WorldMap
3. **雪山白观感** = Vista Textures 段（blend_type=1/layerdist=10000/albedo=0.67/colormap=0），非雪线非动态雪
4. 层序：序号大 = 上层覆盖高；第 1 层 default 垫底

### 树的机制（关键认知，2026-09-06）
- **公式在 XML**：每层 `<meshes><mesh name=... density size_min/max seed_index colony_radius colony_threshold weight_offset albedo_multiplier/>` —— density=树多寡、size=树尺寸、weight_offset=最小权重要求
- **成品在 flora.bin**：原版 7.6 万棵 = 烘焙存档（茂密来源）；**实时生成 ≠ 烘焙**（BigMapLearn 现场生成 = 小+稀疏，参数全同却失败）
- 复原=拷 flora.bin：✅ 已验证可行

### 下一步（P0）：flora 实时生成密度调优实验
- [ ] 目标：调 `<meshes>` 参数 → 重导入 forest 材质图 → 密度接近原版（复现"烘焙感"）
- [ ] 实验设计：单变量（density → size → weight_offset → colony → seed）每改一次重导+对照 OnlyTerrian
- [ ] 参照物：OnlyTerrian flora.bin 每类型计数（map_pine_a 10376 / pine_b 7658 / pine_c 22217 / acacia 22936 / beech 10847）
- [ ] 反编译备选：wEditor/SandBox.dll 找 Flora 生成器实现定公式
- [ ] 收尾：通 → 参数固化进 `make_flora.py`（日本图用）



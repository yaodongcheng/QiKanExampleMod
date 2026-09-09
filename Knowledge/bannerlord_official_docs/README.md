# Bannerlord 官方 Mod 文档库（TaleWorlds，抓取快照）

> 来源：https://moddocs.bannerlord.com/ （官方站）＝ 源仓库 https://github.com/TaleWorlds/Documentations 的 `docs/content/english/`
> 抓取日期：2026-09-09 ｜ 页面数：86 ｜ 语言：英文原文（官方）
> 更新方式：`python tools/fetch_bannerlord_docs.py official` 重跑刷新；`--lang schinese` 可取源仓内置的中文版（写入本目录同结构，注意目录会混两种语言，建议先清空再取）

**本库是什么**：TaleWorlds 官方 mod 文档的 raw markdown 源文件，按网站板块目录储存（`Asset Management/`、`Editor/` 等）。与 .lt 社区站相比更「官方规范」，偏**资产/编辑器/场景制作**流程（XML 规则、XSLT、性能指南），代码 API 类内容少。

**已知限制**：①文档面向游戏图文站年代版本（版权页 2005-2022，正文多数讲 v1.x 早期约定，**API 签名以项目反编译为准**）；②`_index.md` 是板块索引页（含侧边栏结构），非正文；③源仓也含俄语/简体中文版（未纳入本快照）。

---

## 与本项目相关度高的页面（中文速览）

| 页面 | 一句话 | 关联项目 |
|---|---|---|
| `BestPractices/merging_module_xml_files_with_native.md` | 用 XSLT 把 mod XML 与原生 XML 合并 | 织丰据点增删（[[shokuho-settlement-add-remove]]） |
| `BestPractices/xslt_usage_tutorial.md` | XSLT 用法教程（合并/替换原生文件的标准答案） | 同上 |
| `BestPractices/xml_editor.md` | XML 编辑工具对比 | 数据表维护 |
| `Authoring Mission Scenes/Script Components/callbacks.md` | Mission 脚本组件回调函数表 | Mission 层行为 |
| `Authoring Mission Scenes/Script Components/scene_spawn_points_guide.md` | 场景出生点（spawn point）tag 规范 | 场景实体放置、战斗刷兵 |
| `Authoring Mission Scenes/Script Components/spawn_point_debug_tool.md` | 出生点调试工具 | 同上 |
| `Authoring Mission Scenes/sieges.md` / `hideouts.md` / `arenas.md` / `villages.md` | 攻城/藏身处/竞技场/村庄场景制作规范 | 据点场景 |
| `Authoring Mission Scenes/tactical_positions.md` | 战术位置（战场 AI 站位点） | 战斗系统 |
| `Editor/Scene Editor/terrain_*.md`（creation/blend/resize） | 地形创建/混合/重置 | 日本地形管线（[[terrain-heightmap-v2]]） |
| `Editor/Scene Editor/nav_mesh.md` | NavMesh 生成与检查 | 场景制作 |
| `Editor/Scene Editor/level_system.md` | Level 系统（场景分区/卸载） | 大地图场景性能 |
| `Editor/Scene Editor/entity_inspector.md` | Entity 属性面板 | 编辑器使用 |
| `Editor/Resource Editors/*.md`（7 页） | 材质/网格/骨骼/贴图/模型查看器等资源编辑器 | 资产管线 |
| `Asset Management/quickguide_create_a_mod.md` | 建 mod 速览（Module.xml/SubModule.xml） | SubModule 装配 |
| `Asset Management/faq.md` | 常见问题（资源 id、打包、加载顺序） | 通用 |
| `Asset Management/asset_naming_conventions.md` | 资产命名规范 | 资源 ID 稳定（铁律 5/20） |
| `War Sails/*.md`（4 页） | 战船/海上村掠场景/世界地图 | 未来可选内容 |

## 全量索引（按板块分组）

### Asset Management（16 页）
quickguide_create_a_mod · faq · asset naming conventions · Asset Browser（_index + FilterQueries）· Asset Types（animations/bodies/materials/meshes/path/prefabs/scripts/shaders/skeletons/textures/overriding_assets/overriding_scenes_prefabs）· creating_custom_banner_icons · generating_and_loading_ui_sprite_sheets · horse_reins_simulation_creation · how_to_add_custom_fonts · implementing_flora · weapon_smithing

### Authoring Mission Scenes（11 页 + 5 页脚本组件）
arenas · hideouts · sieges · villages · tactical_positions · custom_water_materials_for_non_naval_scenes ｜ Script Components：callbacks · scene_spawn_points_guide · spawn_point_debug_tool · scene_barrier_builder · bannerlord_destructible_component

### BestPractices（5 页）
xml_editor · merging_module_xml_files_with_native · xslt_usage_tutorial · scene_performance_guide · sealed_class_extension

### Editor（20 页）
Resource Editors（7 页：material/skeleton/meta_mesh/model_viewer/mipmap/decals/cloth_simulation）· Scene Editor（13 页：alignment_and_snapping / creating_entity / distance_tool / editor_shortcuts / entity_inspector / level_system / mass_selection / nav_mesh / nav_mesh_inspector / path_editing / prt / terrain_creation / terrain_mesh_blend / terrain_resize）

### Audio Modding（5 页）· Multiplayer（4 页）· Steam Workshop（1 页）· War Sails（4 页）
audio-modding-overview · event-setup · scene-audio · systems · adding-custom-sounds ｜ hosting_server · custom_game_mode · multiplayer_scenes ｜ uploading_updating_mod ｜ ship_creation · wsworldmap · what_makes_seaborne_village_raid_scene

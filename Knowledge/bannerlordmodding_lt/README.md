# BannerlordModding.LT 社区文档库（抓取快照）

> 来源：https://docs.bannerlordmodding.lt/ （社区文档站，站主 Litauen，MkDocs 生成）
> 抓取日期：2026-09-09 ｜ 页面数：162 ｜ 语言：英文原文
> 更新方式：`python tools/fetch_bannerlord_docs.py all` 重跑刷新（生成物，禁止手改内容，改抓取行为改脚本）

**本库是什么**：该站文档的本地快照，按网站板块目录储存。内容质量参差（部分是短视频演示 + 要点），但对「引擎能力边界 / 编辑器和资产流程」类问题覆盖比官方站更实操。

**已知限制**：①站内图片/视频未下载（正文中路径已改为绝对 URL 可点击）；②HTML→Markdown 转换可能丢失原始排版（表格转成普通文本流的页面占比很小）。

**🔴 网页导航 → 本地路径对照（2026-09-09 校验：162 页零漏抓）**：网站侧边栏 11 个一级目录中，**Entities / World Map / Custom Creatures / Troubleshooting 四个是虚拟分组头**，其页面真实挂在别的路径下，本地目录与网页导航对应关系如下——

| 网页导航一级目录 | 本地位置 |
|---|---|
| Modding | `modding/`（56 页，含 Entities 分组的 24 个实体页）|
| Entities（分组头） | `modding/agents.md` `armies.md` `bandit_clans.md` `clans.md` `cultures.md` `equipment.md` `formations.md` `heroes.md` `hideouts.md` `items.md` `kingdoms.md` `locations.md` `npc_character.md` `parties.md` `quests.md` `races.md` `settlements.md` `settlements_xml.md` `ships.md` `tournaments.md` `trooproster.md` `weapons.md` `workshops.md` `buildings.md` |
| GauntletUI | `gauntletui/`（10 页）|
| Editor | `editor/`（28 页，含 World Map 分组的 8 页）|
| World Map（分组头） | `editor/world_map_general.md` `world_map_settlements.md` `settlementpositionscript.md` `battle_scene_grid.md` `weather_effects.md` `scene.xscene.md` `mesh2hmap.md` `world_map_problems.md` |
| 3D | `3d/`（20 页）|
| Modules | `modules/`（5 页）|
| Resources | `resources/`（8 页）|
| Custom Creatures（分组头） | `guides/custom_creatures.md` `custom_creature_skeleton.md` `custom_creature_animation.md` `custom_creature_xml.md` `custom_creature_troubleshooting.md` `custom_creature_reference.md` |
| Guides | `guides/`（35 页）|
| Troubleshooting（分组头） | `modding/crashes.md`、`resources/dnspy.md`、`guides/troubleshooting_siege_scene.md` `troubleshooting_hang.md` `human_bullet.md` `advanced_stacktrace_analytics_of_crash_reports.md` `how_to_send_a_save_file.md` `how_to_report_a_crash.md` |

---

## 与咱们项目相关度高的页面（中文速览）

| 页面 | 一句话 | 关联项目 |
|---|---|---|
| `modding/campaign_events.md` | 战役事件（CampaignEvents）绑定 / 触发方式 | 剧本引擎、事件系统 |
| `modding/dialogs.md` | 对话系统改造（PlayerSentence/冒泡/选项） | LLM 对话、原版对话流 |
| `modding/persuasion.md` | 说服任务（PersuasionTask）机制 | 对话检定 |
| `modding/localization.md` | 本地化：Languages 目录 / {=} key / id 高亮 | 语言 XML（铁律 13/14） |
| `modding/textobject.md` | TextObject 构造、替换符、多语言 | LWNTextHelper |
| `modding/save_system.md` | 存档：SaveableField / 读档重建 | 存档架构 |
| `modding/game_states.md` | GameState/GameManager 切换机制 | 通用自定义战役 GameType |
| `modding/mbsubmodulebase.md` | 模块生命周期入口 | SubModule 装配 |
| `modding/harmony.md` | Harmony 补丁写法与坑 | 全部 Harmony 补丁 |
| `modding/missions.md` / `missionbehaviour.md` / `missionview.md` / `missionlogic.md` | Mission 四件套：行为/视图/逻辑/生命周期 | Mission 层功能（战斗、偷窃） |
| `modding/map.md` + `editor/world_map_*.md` | 大地图（Map）与战役地图实体 | Taikou 日本图 |
| `editor/heightmap.md` | 地形高度图编辑（导入/导出） | 地形管线（[[terrain-heightmap-v2]] 实测对照） |
| `editor/mesh2hmap.md` | Mesh 转高度图流程 | 日本地形导入 |
| `editor/scene.xscene.md` | 场景 .xscene 文件结构 | 场景跳转/建筑 |
| `editor/navmesh.md` | 导航网格生成与修复 | 场景实体放置 |
| `editor/scenes.md` / `siege_scenes.md` | 场景制作 / 攻城场景规则 | 据点场景 |
| `guides/custom_culture.md` | 自建文化全套（服饰/语音/命名） | Taikou 数据包 spcultures |
| `guides/custom_character_backgrounds.md` | 自定义角色背景（建号） | 通用建号管线 |
| `guides/custom_start_positions.md` | 自定义开局位置 | 时代剧本入口 |
| `guides/change_game_version.md` | 切换游戏版本的操作流程 | 双机多版本工作流 |
| `guides/read_data_from_xml.md` | 运行时读 XML 数据 | 剧情/事件数据 |
| `resources/version_changes.md` | 各版本 API 变更速记（1.1.6→1.2.10 等） | VersionCompat 对照 |
| `resources/console.md` | 控制台指令大全 | 调试 |
| `resources/dnspy.md` | dnSpy 修改/调试教程 | 反汇编分析 |
| `gauntletui/*.md`（10 页） | GauntletUI：Widget/Prefab/Brush/Sprite/UIExtenderEx | UI 开发（MCM/设置界面等） |
| `3d/*.md`（20 页） | Blender→FBX→编辑器导入全流程（骨骼/权重/LOD/材质） | 换头/模型资产管线（[[blender-fbx-viewing]] 对照） |

## 全量索引（按板块分组，标题为原文）

### modding/（56 页：引擎 API / 实体操作）
Agents · Animations · Armies · Bandit Clans · Buildings · CampaignEvents · Character Customization · Clans · Crashes · Cultures · Cutscenes · Dialogs · Equipment · Formations · Game States · Gold · Harmony · Heroes · Hideouts · Items · Kingdoms · Localization · Locations · Map · MapEvent · MBSubModuleBase · MissionBehaviour · MissionLogic · Missions · MissionView · Models · NPC Character · Parties · Persuasion · Quests · Races · Save System · Settlements · settlements.xml · Ships · Modding sound · TextObject · Time / Date · Tournaments · Troop Roster · Various · Weapons · Workshops · XML Modding ·（+ 详见文件名清单）

### gauntletui/（10 页）
Brushes · Drag and Drop · Menus · Movies · Popups / Messages · Prefabs · Scrollable Panel · Sprites · UIExtenderEx · Widgets

### editor/（28 页：场景与资产编辑器）
Assets · Battle Scene Grid · Brushes · Color Grade · Controls · Cutscenes · Decals · Editor · Editor Test Mode Mod · Entities · Flora · GulagEnabler 脚本 · Heightmap · Mesh to Heightmap · Navigation Mesh · Paint Layers · Paths · Prefabs · Resource Browser · scene.xscene · Scenes · Settlement Position Script · Shaders · Siege Scenes · Terrain · Textures · Water · Dynamic Weather Effects · World Map · World Map Problems · World Map Settlements

### 3d/（20 页）
3D Workflow · Armature/Skeleton · Blender FBX Import · Cloth Physics · Collision Body · Create LODs · Editor FBX Import · Environment · Export to FBX · Material · Material Issues · Mesh Adjustment · Model Viewer · Polycount · Rename Material · Shields · Sigil · Team Colors · Terms · Weight Painting

### guides/（35 页：配方型教程）
Advanced Stacktrace · Change game version · Custom Audio with LipSync · Custom Banners · Custom Battle Faction · Custom Campaign Intro · Custom character backgrounds · Custom Creatures（5 页系列）· Custom Culture · Custom Dialog Backgrounds · Custom First Loading Screen · Custom Load Screens · Custom Logo · Custom Main Menu · Custom Menu Background · Custom Mount · Custom pictures for Encyclopedia · Custom Popup · Custom Progress bar · Custom Round Popup · Custom Start Positions · Engine Notes · How to remove native kingdoms · How to report a crash · How to send a save file · Human-Bullet problem · Naval Travel · Read data from XML · Scale World Map Entities · Troubleshooting Game Hang · Troubleshooting Siege Scene · Use Decompiled sources with AI

### modules/（5 页）· resources/（8 页）· index.md（欢迎页）
Create a new Module · Include other modules · Check for other mods · Mod Release · Useful mods for development ｜ Console · dnSpy · Guides · NativeTextureExporter · Tools · TpacTool · Useful Links · Version changes · Video Tutorials

# 自定义战役模式 / 内容包自给域轮子（2026-09-08 登记）

> **场景**：LWN 作为通用基座挂自定义 GameType（TaikouCampaign 打头，三国等后续走同通路）。内容包 = 纯数据、必须在**自己体系内自洽**；战役启动链的崩法 = "半成品对象"型 NRE。本卷收这两类排雷/校验轮子。
> **来源**：2026-09-07 TaikouCampaign 启动链排雷（Companion NRE 三段链结案，详见 `plans/太阁数据加载taikou-campaign-boot-20260907.md`「T1 结案」）。
> 注意事项：路径约定——脚本在**仓库根 `Scripts/`**，C# 在 `ExampleModVS/ExampleMod/ExampleMod/` 下（与索引默认前缀一致）。

## 〇、🔴 版本警示（2026-09-08 教训：campaign-mode 代码只编译过 1.2.12 机，1.5.2 机 11 条 CS1061）

**战役模式 = 1.2.12 独有 API 最密集区**。CampaignMode/ 的排雷诊断大量使用 **v1.3.0 起被移除/改名** 的 Campaign API（别处永远碰不到，宏体系里也没有它们的对照表）：

| 1.2.12 独有（别用） | 1.5.2 等价物 |
|---|---|
| `Clan.InitialPosition` | （无，诊断改用 `InitialHomeSettlement`） |
| `Kingdom.InitialHomeLand` | `Kingdom.InitialHomeSettlement` |
| `Clan.UpdateHomeSettlement` | `Clan.SetInitialHomeSettlement` |
| `HeroCreator.CreateHeroAtOccupation` | `HeroCreator.CreateNotable(occupation, settlement)` |
| `GameModels.SettlementConsumptionModel` | （无） |
| `Module.CurrentModule.SubModules` | 既有轮子 `V.CollectSubModules()`（1.3+ = `CollectSubModules()`） |
| `Campaign(CampaignGameMode)` 构造 | 1.5.0+ 双参 `(CampaignGameMode, AdvancedStartOptionsData)`（占位传 null） |

**铁律**：
1. **任何改 CampaignMode/ 的提交，1.5.2 机必须 `dotnet build -c Debug` 验一遍**——排雷闭环只在 1.2.12 机"编译→跑→崩→修"，1.5.2 从未编译 = 本次漏检根因；别绕开已有的 `V.*` 轮子。
2. **写法 = `#if MB2_V1212` 全类/整段分叉**（1.2.12 全量 / 1.5.x 注释占位），范本 = `LivingWorldCharacterCreationContent`；1.5.x 建号 = CharacterCreationManager 新体系，v0 不接入（`CampaignModeActivator` 裁定）。
3. 裸 `#if` 新位置必须登记 VersionCompat 注册表（已登记：`LivingWorldCampaign.cs` 全类 / `LivingWorldCampaignGameManager.cs:OnLoadFinished`）。

## 〇·五、🔴 1.2.12 ↔ 1.5.2 战役场景迁移判据（2026-09-08 实机，T3 前置）

- **结构层**：1.2.12 ModKit 场景与 1.5.2 资源头同构（xscene version=2 / navmesh RNM1 v3 / terrain ZGR6RTRN / flora FLR2），**外壳已排除**。
- **编辑器侧可行**：1.5.2 ModKit 可打开 1.2.12 Main_map；保存 = 重写 xscene + ShaderCache，**terrain.bin 同时被重写**（3,895,164B → 6,619,168B）；**navmesh.bin 保存不动**——需 Navmesh 工具重生（生成器输出带 inverted-normal/clockwise 警告面属编辑器校验警告）或走 `nav_mesh_auto_generated_="true"` 通路（删 bin → 引擎自动重算）。
- **客户端侧（实锤差异）**：1.5.2 **client** 读 1.2.12 场景 = `SandBox.MapScene.Load → _scene.Read("Main_map", "Taikou", ...)` **native 无栈 NRE**（Source=无法计算异常源；三种断点排查法见 Load() 内分段：CreateNewScene 链 / DisableUnwalkableNavigationMeshes(Models 链) / Scene.Read 之后）；**同批文件 wEditor 全程正常打开** = 编辑器可迁移 ≠ 客户端可读；ModKit 重生 navmesh（1.5.2 格式）后 client 仍崩。
- **选图逻辑**：`GetMainMapModule()` 遍历 active modules **无 break = 最后一个**有 `SceneObj/Main_map/scene.xscene` 的胜出（Launcher 排序决定；SandBox/Taikou 皆有 → 默认 SandBox 胜，要日本图 = launcher 让 Taikou 顺序在前）。
- **裁定（2026-09-08 用户）**：先回 1.2.12 完成正常功能（该机场景自洽）；1.5.2 转场（T3）搁置重审。⚠️ 排查时本机无引擎日志（无 rgl_log），1.5.2 侧依赖 LWN `[MapSceneTrace]` 类分段探针。

## 一、内容包数据自给校验 —— 交叉引用闭合检查（每次改数据必跑）

**解决什么问题**：自定义 GameType 下官方段被 IncludedGameTypes 白名单过滤，但拷贝官方文件残留的引用（物品/工艺件/音乐/装备模板带 1835 处 `Culture.<原版八文化>`）会经引擎 **`MBObjectManager.GetPresumedObject`（引用创建：对象不存在只建"裸对象"，只记 id、从不 Deserialize）** 生成空壳桩（模板列表 null）→ 原版行为无 null 保护 → NRE。任何新内容包（三国等）都会复刻这个坑。

**判定模型（图结构，实现为查字典）**：引用 = 有向边 `类型.名字`，闭合判定 = 边另一端落账于三区块之一：
1. **Taikou 自给定义**（每文件根的直接子元素 `id=`，类型映射表含 Items→Item、SPCultures→Culture 等）
2. **引擎基础层**（Native 段**无 IncludedGameTypes** = 所有游戏类型都装载：Monster/ItemModifier/ItemModifierGroup/WeaponDescription/CraftingTemplate/SkeletonScale/SiegeEngine + Skill/Perk/Trait 等代码枚举）
3. **别名边**（Faction→Clan——引擎里 Faction 就是 Clan 的别称）

落不了账 = **悬空**（报错退出码 1）；前缀连认都不认 = **未知区**（单独列出供人工过目——防"白名单藏漏网之鱼"）。

**关键签名**（仓库根 `Scripts/check_taikou_xml_references.py`）：
```python
python Scripts/check_taikou_xml_references.py            # 默认 1.2.12 机 Taikou 路径
# XML-aware（ElementTree，跳过注释）；退出码 0=无悬空
# 首跑即抓 14 类悬空：Culture×8（1835 处）/ NPCCharacter×5（今川真空+幽灵 id）/ Clan.clan_imagawa×2
```
**配套清洗**：`Scripts/sanitize_taikou_cultures.py`（8 类原版文化引用 → `Culture.ikoku`，替换+minidom parse 双验证，`--dry-run` 可用）。

**验收话术**：`check ... = dangling 0 / unknown 0` = 数据改动达标（"引用的任何东西都必须在自我体系内"）。
**铁则**：新内容包数据 = 手写或生成器产出后先过本检查；**拷贝官方文件 = 默认带原版引用尾巴，必须清洗**。

## 二、~~CultureTemplateNullFix~~ —— 文化模板列表 null 兜底（✅ 已退役 2026-09-10）

**退役结论（先看这条）**：该兜底**已删除**（文件 + `LivingWorldCampaign.OnInitialize` 调用 + csproj 行全清）。理由 = 数据侧已治本，且离线证明它不可能再触发：TaikouCampaign 下**实际加载的 42 段零悬空 `Culture.*` 引用**（裸文化桩只由悬空引用产生）＋ 反编译证 `CultureObject.Deserialize` 对三个模板列表**必赋非 null**（`new MBList<>()` → 末尾整体赋值）。不变量由 `Scripts/check_culture_references.py` 常驻守（数据改动必跑）。
⚠️ **退役时踩到的坑（别再踩）**：原验证法「跑旧档看日志无修复行」= **假绿**——调用点在 `SavedCampaign` 早退**之后**，读档根本不执行那段代码。验证任何兜底补丁前，先确认「调用点在不在你的验证路径上」。

**以下为原始轮子记录（模式仍有参考价值：新内容包若确需加载期兜底，照此实现）**：

**解决什么问题**：即使数据自给到位，第三方引用/历史档案仍可能产生裸文化桩；引擎 `CompanionsCampaignBehavior.InitializeCompanionTemplateList`（及 LordTemplates/RebelliousHeroTemplates 的消费点）无 null 保护。

**关键点**：
- 反射**按属性名存在性**修复（`GetProperty` null 即跳过）——🔴 1.5.x 属性名/归属已变（`NotableAndWandererTemplates` 字符串在 1.5.1 DLL 0 命中），硬编码 Harmony 属性补丁会静默失败；反射 + 日志为跨版本安全解
- `{ get; private set; }` 的 SetValue 必须显式取非公共 setter：`GetSetMethod(true).Invoke`（默认 `SetValue` 只走 public setter 会抛）
- 空列表构造用 `new MBReadOnlyList<CharacterObject>(new List<CharacterObject>())`（公开构造，已验证 1.2.12）
- 日志：`[CultureTemplateNullFix]`（null→空 / 剔除 null 条目 / 属性不存在的版本差异提示）

**文件**：`~~ExampleModVS/ExampleMod/ExampleMod/Debug/CultureTemplateNullFix.cs~~`（已删；恢复 = `git checkout <退役前 commit> -- 原路径` + csproj 补登记行）。

## 三、同族兜底索引（别重复造）

文化类 NRE 已有三层战线，本卷只持新轮子：
| 轮子 | 层 | 文件 |
|---|---|---|
| `AgentDamageModelCultureNullFix` | 运行时 Transpiler（伤害模型 `.Culture.IsBandit` 裸解引用） | `CampaignMode/AgentDamageModelCultureNullFix.cs` |
| `CharacterCultureBackfill` | 生成期 MissionLogic（角色缺 culture 按生成地点补全） | `CampaignMode/CharacterCultureBackfill.cs` |
| ~~本卷 `CultureTemplateNullFix`~~（**已退役** 2026-09-10） | ~~加载期（文化模板列表 null→空）~~ → 数据侧 `check_taikou_xml_references.py` + `check_culture_references.py` 常驻守不变量 | 见上（第二节） |
| `HorseSpawnNullGuardPatch`（**已删** 2026-09-10） | 场景消费兜底（`SpawnHorses` 前缀替换：Tags 缺项/物品未装载 → 跳过该出生点）——**数据优先原则下退役**：缺失改由 `Scripts/check_scene_consumables.py` 离线抓（见第四节） | ~~Debug/HorseSpawnNullGuardPatch.cs~~ |

**联动**：改内容包发现新的"半成品对象"NRE = 先查 `check` 脚本有没有抓到同型悬空 → 数据根治为主、LWN 兜底为辅。

## 四、🔴 官方场景消费清单（2026-09-09 登记，雷 40/41 总根）

**解决什么问题**：用官方城市场景的代价——场景文件不受 GameType 过滤（可直接用），**但场景内 prefab 实例引用的原版物品/角色/兵种全部被过滤**；引擎硬编码消费（`SpawnHorses`/`DefaultAlleyModel` 等）→ GetObject null → NRE。**症状信号**：进城场景刚加载就崩（`TroopRoster.AddToCountsAtIndex` / `ItemRosterElement`）。

**排查三步法**（一次补齐，不欠第二颗雷）：
1. `grep -o 'prefab="sp_[a-z]*"' <官方场景>.xscene`（场景用了哪些 spawn prefab）
2. `Modules/Native/Prefabs/editor_spawnpoints.xml`（+ `Modules/SandBox/Prefabs/sp_editor_spawnpoints.xml`）找 `<game_entity name="sp_xxx">` 的 `<tags>`——**Tags[1] = 被引用对象 id**
3. 对象 id → 查自家物品/角色库 → 缺失 = 拷官方定义入自家库（culture 洗 ikoku）+ 跑 `check_taikou_xml_references.py` 0 悬空

**已知消费面**（新内容包直接勾）：黑巷地痞 `gangster_1/2/3`（`DefaultAlleyModel`，Level 6/11/16 + 升级链 + 民用套装）· 马 `sp_horse_*`（6 种：aserai/battania/empire/khuzait/sturgia/vlandia_horse）· 动物 5 只（sp_sheep/sp_cow/sp_hog/sp_goose/sp_chicken → sheep/cow/hog/goose/chicken 物品）。

**织丰对照**：织丰从不用官方城市场景（自有 sho_* 场景 + 自有 sp_horse_kiso prefab + 自家物品）→ 天然免疫；不想补清单就学织丰做自有场景（成本高，v0 建议白名单补齐）。

**详细记录**：`Knowledge/自定义世界内容包从零起步必备清单.md` §1.4 / §1.4b（+ 雷 40/41）；`plans/太阁数据加载…md` 排雷链 37/38。

## 五、🔴 文本与语言线体检（2026-09-10 登记；雷 45/47/48/50/51 一天的产物）

**解决什么问题**：自定义 GameType 下文本/语言有五种**静默失效**——都不崩、控制台零报错，玩家只看到英文或 ERROR 文本：
1. 官方文本段被 GameType 白名单过滤（雷 35 → 9 段原样拷贝 + 自家段注册）
2. 「按文化取 variation」的文本族（`GameTexts.FindText(id, Culture.StringId)`）官方只给八文化变体、**无 `.default`** → 自定义文化不自备就把 `ERROR: Text with id … doesn't exist!` 印进玩家可见正文（雷 45）
3. 语言文件是**清单式**加载：`Languages/<lang>/language_data.xml` 没登记该文件 = **整文件不加载**（雷 47）
4. 语言文件 XML 声明与根元素之间**夹注释** → 引擎按 `xmlDocument.ChildNodes[1].FirstChild` 取 `<strings>` 取到 null → 整文件静默不加载（雷 50）
5. **自有键从没进过语言文件**（C# 里写死的 `{=LWN_cc_bg_*}` 那种，house 校验器看不见）→ 回落英文 fallback（雷 51）

**三件套（`Scripts/`，进「数据改动必跑七件套」）**：

| 脚本 | 查什么 | 判据 |
|---|---|---|
| `check_culture_text_variants.py` | 「无 `.default` 的文化变体族」全集 → 本包是否自备 `<族名>.<自家文化>`（族有 `_f` 约定则一并要） | missing=0 |
| `check_language_registration.py` | ①每个 `Languages/<lang>/` 有 `language_data.xml` ②目录↔清单**双向**对齐 ③铁律 14 emoji/超 BMP ④**自有键中文覆盖（扫数据 XML + C# 双源，剥注释；排除官方键与反编译副本）** ⑤声明后紧跟注释 | problems=0 |
| `gen_taikou_english_strings.py` | 英文层**生成器**：扫数据 XML 的 `{=KEY}English fallback` → 产出 `Languages/std_<包>_strings.xml` + 根级 `language_data.xml`（生成物禁手改，`--check` 校验最新） | `--check` 通过 |

**语言目录两层结构**（照 LWN/官方）：根级 = 默认语言（英文）+ `CNs/` = 中文；**每层各带一份 `language_data.xml`**，`xml_path` 一律相对 `Languages/` 写（`CNs/std_X_strings.xml`）。

**详细记录**：必备清单 §1.6 + 雷 45/47/48/50/51。

## 六、🔴 引擎「盲读子节点」的元素 —— 注释/裸文本都禁止塞（2026-09-10 雷 52；雷 11 当场复发）

**踩坑实录**：给 `spcultures.xml` 加"名字池来源标注"时把 8 行注释塞进 `<clan_names>` **元素里面** → 建新档 → `CompanionsCampaignBehavior.InitializeCompanionTemplateList` NRE（正是退役兜底所防的雷 11 表象；真因是这条注释）。

**机理**（引擎读这些列表是**盲读所有子节点**的）：
- `clan_names` / `male_names` / `female_names`：`foreach (child in X.ChildNodes) new TextObject(child.Attributes["name"].Value)` → 注释节点没有 `name` 属性 → **NRE**
- `notable_and_wanderer_templates` / `lord_templates` / `rebellion_hero_templates` / `basic_mercenary_troops` / `banner_bearer_replacement_weapons` 等模板表：`ReadObjectReferenceFromXml` 缺属性返回 **null** → 列表混入 null 条目 → 消费侧 `.Occupation` **NRE**
- 关键放大效应：反序列化**中途抛出** → 方法末尾「把列表赋给文化对象」**不执行** → 该文化三个模板列表**保持 null** → 所有消费点全炸（表象与"裸文化桩"一模一样，极具误导性）

**纪律**：注释只能放在**引擎按子节点名字分支处理**的层（如 `<Culture>` 的直接子节点）——判定标准＝引擎读该元素时是"按名字 switch"（安全）还是"盲读所有子节点取属性"（禁止注释/裸文本）。

**防线**：`check_taikou_xml_references.py` 的「列表污染体检」。⚠️ 实现坑（当天各踩一次）：**必须用 `ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))`** —— ET 默认丢弃注释节点（查不出来），改用正则又会把**注释里写的字面标签**（如说明文字里的 `<clan_names>`）当成真标签（假报）。

**详细记录**：必备清单雷 52 + 排雷链；同族陷阱（语言文件版，机理同为"引擎按固定位置取节点"）= 雷 50。

## 七、🔴 出生点与地图相机（2026-09-10 登记；实机验证通过）

**解决什么问题**：自定义战役里「进图看不见玩家 / 相机在错误位置 / 空气墙」这一族症状——2026-09-08 曾误判为「teleport 时序问题」并加了 Harmony 补丁，2026-09-10 查明真凶是**数据缺口**（border 实体），补丁属重复劳动已删。

**机制（反编译实锤，1.2.12；1.5.1 同构）**：
1. **相机初始目标 = 主队运行时坐标**——`MapCameraView.Initialize()`：`IdealCameraTarget = new Vec3(MobileParty.MainParty.Position2D, 地形高度+1, -1)`；构造默认 `CameraDistance = 2.5f`。场景**不参与**（`camera_top` 是死实体，全游戏 2 万个文件 0 引用）。
2. **读取时点早于建号完成回调**——`CharacterCreationState.FinalizeCharacterCreation()` 顺序：`ApplyFinalEffects` → **`CleanAndPushState(MapState)`（MapScreen 构造 + OnInitialize → 相机读坐标）** → `_handler?.OnCharacterCreationFinalized()` → `Content.OnCharacterCreationFinalized()`（我们的钩子）。所以**在回调里写坐标已经晚了**（只能靠事后 teleport 补救）。
3. **场景唯一的相机输入 = `border_min`/`border_max`**——只**钳制**相机目标盒（`ComputeMapCamera`），不设定位置；缺实体 1.2.12 静默兜底 `900×900`（= 玩家在盒外时"怎么拉都拉不过去"的真相）、1.5.x 直接崩。
4. **`ResetCamera(resetDistance: true, …)` 兼管缩放复位**——`TargetCameraDistance = 15f; CameraDistance = 15f`（构造默认 2.5 = 贴脸）→ 这条**不能删**。

**正解（零 Harmony）**：出生点改到**世界创建期**写。
- 时点安全性（`Campaign.DoLoadingForGameType` NewCampaign 分支顺序实锤）：`LoadMapScene()`（更早的 LoadVisualsThirdState）→ `InitializeMainParty()`（引擎放默认坐标 685.3,410.9）→ **`OnNewGameCreated(gameStarter)`（我们在这里写）** → 建号。写入时地图已加载 → `Position2D` setter 里的导航面能正常算出。
- 基类不引 `Campaign.DefaultStartingPosition`：该属性 **1.3.15 起已移除**（1.2.12 = 1 命中 / 1.3.15、1.4.6、1.5.1 = 0 命中），引用它会挂 1.5.x 编译 → 用可空虚属性让内容包自己声明。

**关键签名 / 调用范例**：
```csharp
// 基类（ExampleModVS/ExampleMod/ExampleMod/CampaignMode/LivingWorldCampaign.cs）
public virtual Vec2? StartingPosition => null;      // null = 不改（保持引擎默认）；1.5.x 也能编译

private void OnNewGameCreatedPartialFollowUp(CampaignGameStarter starter, int i)   // 已注册于 OnInitialize
{
    if (i == 0 && MobileParty.MainParty != null)     // i==0 = 本事件最早一次（引擎循环 100 次）
    {
        Vec2? spawn = StartingPosition;
        if (spawn.HasValue) { MobileParty.MainParty.Position2D = spawn.Value; /* 打 [LWN-campaign] 日志 */ }
    }
    ...
}

// 内容包 thin 子类（TaikouCampaign.cs）——只需一行
public override Vec2? StartingPosition => TaikouStartingPosition;   // new Vec2(985f, 428f)
```

**保留项（别跟着删）**：`LivingWorldCharacterCreationContent.OnCharacterCreationFinalized` 里的 `ResetCamera(true,true) + TeleportCameraToMainParty` —— 缩放复位必需，原版同款。

**已退役**：~~`CampaignMode/MapScreenCameraPatch.cs`~~（2026-09-10 删；它的动作与建号内容里的 teleport 完全重复）。恢复 = git 历史捞回 + csproj 补登记行。

**记录**：必备清单雷 30/33 更正 + 雷 34 + §1.5；台账「出生点（世界创建期置位）」「地图相机对准（已退役）」行。

## 八、🔴 离线地图场景体检 `check_scene_entities.py`（2026-09-10 登记）

**解决什么问题**：地图场景是**最容易静默出错**的地方——缺实体不报错、不崩，只是"玩法悄悄没了"。本脚本把这些检查从"进游戏才发现"提前到离线（替代已停用的运行期 `[MapBorder]` 诊断补丁）。

**查六项**（`errors=0` 才过；`banner_pos` 缺失只 WARN）：
1. `border_min`/`border_max` 在位**且与 `<terrain>` 规格对账**（期望 `max = node_dimension_x×node_size, node_dimension_y×node_size`）——缺/错 = 空气墙 900×900（1.2.12）或进图崩（1.5.x），雷 34
2. 引擎硬查询的 5 个脚本实体：`CampaignMapSiegePrefabEntityCache`（缺 = MapScreen.OnInitialize NRE）/`MapColorGradeManager`/`SettlementPositionScript`/`Town Scene Manager`/`SceneLeveler`，雷 28
3. navmesh：`navmesh.bin` 存在 或 `nav_mesh_auto_generated_=true`
4. **有地图组件的据点必须有同名 `game_entity`**——判定 = 组件含 `Town`/`Village`/`Castle`/`Hideout`；缺 = 不进距离缓存 → 据点对变少 → 全局最大据点距可能恒 0 → 家宅选不中，雷 53。**服务性据点豁免**（如 `retirement_retreat`＝只有 `RetirementSettlementComponent`）：官方 493 据点实测 Town 120/Village 273/Hideout 99 **全有实体**，唯一无实体者就是退休据点 ⇒ **不在地图上是官方设计，别去给它补实体**（脚本打印理由后跳过）
5. 城镇（含 `<Town>` 组件）实体子树要有交互链三件套：`bo_town`（拾取碰撞体）/`map_settlement_circle`（黄圈）/`main_map_city_gate`（门），雷 37
6. （WARN）`map_banner_placeholder`（旗帜占位，纯视觉）

**关键实现点**（写脚本时踩过的结构坑）：
- 场景 XML 结构：`<entities>` → `game_entity` → **子实体包在 `<children>` 里**（不是直接嵌套）、标签是 `<tags><tag name=.../>`、脚本是 `<scripts><script name=.../>`——`e.iter()` 会把后代的标签一起捞上来，要按**直接子路径**取
- `<Town>` 组件判定：`s.find("Components")` 可能为 None，**别写 `find(...) or []`**（Element 真值判断在新版 py 会报 DeprecationWarning，且空元素为假）

**调用**：
```bash
python Scripts/check_scene_entities.py                 # 默认 1.2.12 机 Taikou / Main_map
python Scripts/check_scene_entities.py --module <路径> --scene Main_map
```
**首跑价值实证 + 一条教训**：写完第一次跑就报 `retirement_retreat` 无实体 = 红线——**追查后推翻**：那是官方设计（见上第 4 条）。所以**写检查规则前先拿官方数据跑一遍**，否则会把官方设计当缺陷、把错误结论写进清单。修正后 errors=0。

## 九、🔴 离线体检全家桶（2026-09-10 登记；「每条检查必须有脚本」纪律的产物）

**解决什么问题**：必备清单里曾有一批条目**只有文字、没有脚本**（引擎硬编码 id、必填字段、段注册、距离缓存、官方拷贝原样）——全靠人背，忘了就运行期崩或静默失效。本卷登记 2026-09-10 补齐的脚本 + 一键入口。

**一键跑**：`python Scripts/run_all_checks.py`（13 项，约 5 秒；`--quick` 跳慢检查；`--module` 换内容包；红的当场打印输出尾巴）。清单侧逐条对照见 `Knowledge/自定义世界内容包从零起步必备清单.md`「清单条目 ↔ 脚本对照表」。

| 脚本 | 查什么 | 关键设计点（照抄时别踩） |
|---|---|---|
| `check_required_ids.py` | 引擎硬编码点名的 38 条 id（含退休据点 / 地痞三档 / 捏脸模板 0-9 / 主队模板 / 5 马 5 动物） | **判定基准 = 目标 GameType 下真正会加载的段**（闭包 + 白名单）——定义在未加载段里要报「未加载」（雷 3/4/5 形态），只有这样才能防假绿 |
| `check_data_fields.py` | 据点必填（按组件类型分档）/ 城防 level≤3 / occupation 枚举 / 文化必备 / **商队护卫引擎硬查询** / 势力 owner 链 | 🔴 **每条规则的边界都先拿官方数据验过**（493 据点 / 16 文化 / 94 家族）：owner 只有城镇要、CommonAreas 只有城镇要、Locations 巢穴不要、Faction 字段只对「拥有据点的家族」要（官方 94 个里 21 个缺）；`--module <SandBox> --game-type Campaign` = 负面对照，应 0 错 |
| `check_module_registration.py` | 必需段注册 / 段 path 可解析 / 孤儿数据文件 / **csproj 漏登记 .cs** | 「有意未登记」的判别 = **csproj 注释里提过这个名字**（含不带 `.cs` 的写法）→ INFO；完全没提 → ERROR |
| `check_settlement_distance_cache.py` | 距离缓存 id 集合与 settlements.xml 一致 + 据点对 ≥1 | .NET `BinaryWriter` 格式解析（7-bit 变长长度前缀 + UTF-8，不是 int32 长度）；「期望集合」= 有地图组件的据点 ∩ 有同名场景实体（引擎生成缓存时的真实口径） |
| `check_official_copies.py` | 9 个 GameText 段与本包同名文件逐行比对（差异应为 0） | 🔴 **源头模块必须声明**：Native 与 SandBox 有同名但内容完全不同的 `module_strings.xml`（6887 行 vs 663 行），自动挑源头 = 必然误报 |

**三条纪律（写新 checker 时照做）**：
1. **先拿官方数据跑一遍**再定规则（否则把官方设计当缺陷——退休据点误判实录，见卷八）。
2. **必须做负面测试**：故意造坏数据，确认脚本真抓到且 exit 1（本轮 5 个脚本全部做过：三种坏法全中才算过）。
3. **规则实现坑**：`e.iter()` 会把后代标签一起捞（要按直接子路径取）；`find(...) or []` 在 Element 上有 DeprecationWarning（用 `is not None`）；元数据类名字符串在 DLL 里是 UTF-8、字面量是 UTF-16LE（串搜验证要分两种编码各搜一次）。


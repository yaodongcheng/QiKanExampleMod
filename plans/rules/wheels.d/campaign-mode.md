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


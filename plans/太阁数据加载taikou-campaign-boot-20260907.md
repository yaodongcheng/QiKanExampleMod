# TaikouCampaign 启动链排雷交接（2026-09-07 深夜）

> 本文件 = 会话交接。**下个 session 从这里开始**，不用重跑任何反编译（结论全部实锤并附出处）。

## 🔴 TODO（下一步从这里开始，2026-09-08 更新）

- [x] **T2（完成）**：CC 全链实机通过 = 文化 → FaceGen（捏脸）→ 命名/出身 → Review（雷 23-27 全修，探针全绿，用户截图确认——主英雄脸 10 号、原生捏脸界面出）
- [ ] **T3（进行中）**：进日本图已实机验证大半——**进图不崩（雷 28 基建补全）✓ / 地图 tick 正常（雷 29 wall level 3）✓ / 相机开局对准京（雷 30/33）✓ / 京图标可见 ✓**；剩：①存档确认（雷 32 定义器待用户 F5 实测——若仍报缺类型 → OnSubModuleLoad 加 `new LivingWorldSaveableTypeDefiner();`）②**相机"东移"操作确认 = 已实锤非操作习惯**（雷 34：场景缺 border_min/border_max → 引擎兜底 900×900 相机墙；修复已落盘，待用户实机复测走全图）③MCM 主菜单页定位（去勾 Taikou 对照组）
- [ ] T4：全量数据（TK5 布局表 → place_settlements.py 批量 + settlements.xml 同步 + **兵种树/日式物品自建**——原料已备 112 SkillSet/38 物品/25 BodyProperty）
- [ ] T5：日式素材（先 Native mi_*，织丰 sho_* 不可用——后续决断来源）

## 一句话现状（2026-09-08 傍晚）

**进步**：T2 CC 全链完赛（雷 23-27）、T3 进图链路踢完首轮（雷 28-33：进图不崩/地图基建全/相机对准/京可见/退休据点/存档定义器）。**实机验证状态**：CC 全链 ✓（截图）、进图+京 ✓（截图）、地图 tick ✓；**待验证**：存档 F5（雷 32）、相机双击操作（雷 33）、MCM 页（独立问题）。**下一动作 = 用户三项确认** → T4 布局表冲刺。

**⚠️ 雷区备忘（已修但需知）**：
- 生成器重跑会覆盖手改（铁律 22 现场教训——**改数据必须改生成器**；`gen_taikou_culture_full.py` 幂等已修；`gen_taikou_workshop_items.py` 是一次性脚本（重跑会重复插入——用前先看代码）
- 数据改动后必跑：`python Scripts/check_taikou_xml_references.py`（0 悬空）+ `check_taikou_field_coverage.py`（七类交集，跑完后人工甄别误报/特例——见 Knowledge/内容包最小字段交集.md）

## T1 结案（2026-09-07 深夜，三颗雷合一）

**现象**：`CompanionsCampaignBehavior.InitializeCompanionTemplateList` NRE（GameManager case5）。

**根因三段链**（全部实锤）：
1. **入口**：官方 SPCultures 段被 GameType 白名单正确过滤 👌，但 **Taikou 自己拷贝的 13 个文件（物品/工艺件/音乐/装备模板）带 1835 处 `Culture.<原版八文化>` 引用** —— XML 引用解析经 `GetPresumedObject`（对象不存在时创建"裸对象"，只记 id 不 Deserialize）→ 内存里 8 个裸文化桩（模板列表 null）——验证脚本/证据：`Scripts/check_taikou_xml_references.py`
2. **引擎无防**：`InitializeCompanionTemplateList` 无 null 保护（`foreach culture.NotableAndWandererTemplates`）→ 迭代到桩文化 = NRE
3. **第二源**：spcultures 模板引用 5 个角色但 spnpccharacters 只定义 3 个（今川 2 真空引用）+ `basic_troop=peasant_farmer`/`lord_template_empire_*` 幽灵 id —— 同样的"引用不存在对象"模式

**修复清单（三层，全部已落盘）**：
| 层 | 内容 | 文件 |
|---|---|---|
| 数据·入口 | 13 文件 1835 处文化引用 → `Culture.ikoku`（脚本+parse 验证） | `Scripts/sanitize_taikou_cultures.py` |
| 数据·自洽 | spcultures 删今川 2 模板引用/lord 小节改空；taikou_heroes 删今川 2（"今川全删"裁定落地）；spnpccharacters 增 `peasant_farmer`（基础兵，全字段引用自有资源） | 3 个数据 XML |
| 代码·加固 | `CultureTemplateNullFix`：战役启动把所有文化的 N&W/LordT/RebelliousHeroT 模板列表 null→空/剔除 null 条目（反射，跨版本安全；任何内容包同问题兜底） | `Debug/CultureTemplateNullFix.cs` + LivingWorldCampaign.OnInitialize 调用 |
| 校验 | **数据改动后必跑** `python Scripts/check_taikou_xml_references.py`（0 悬空 = 入门条件） | `Scripts/check_taikou_xml_references.py` |

**1.5.x 对照**：1.5.1 DLL 中 `NotableAndWandererTemplates` 字符串 0 命中（属性名/归属已变）——加固代码设计为"属性存在才修"（GetProperty null 即跳过），天然兼容；1.5.x 机首测时留意 `[CultureTemplateNullFix] 属性 xxx 在本版本不存在` 日志。

## 决策表：每个环节按「织丰做了什么」分类（2026-09-08 用户裁定维度）

> 实施纪律：再遇到「缺 X」类问题，先反编译织丰（Shokuho.dll + spcultures/）拿到它是"做了/不做/怎么做"的证据，再定我们的动作。

| 环节 | 织丰证据 | 分类 | 我们的动作 |
|---|---|---|---|
| BackstoryCampaignBehavior（卡拉迪亚前史） | Shokuho.dll:99600 实锤：`[HarmonyPatch "RegisterEvents"]` + Prefix false | **A 织丰做+我们必做** | ✅ 完成（`BackstoryCampaignBehaviorPatch` 同款） |
| neutral_culture 文化 | 织丰**有定义**（spcultures/shokuho_main_cultures.xml：basic/elite=guard 复用、militia_template、encounter_mesh、roster×2、banner 武器——最小 7 字段） | **A**（引擎 fallback 消费点 32472/32477/47645 等 4 处 + 织丰证词 = 自定义世界需要它存在） | 待做（照织丰最小字段集） |
| PartyTemplate 引线（militia/villager/caravan/elite_caravan/rebels/vassal_reward 6 个） | 织丰有 militia_template | **A**（世界 tick 一跑商队/守军就消费） | 待做（T3 进图前） |
| 兵种（militia melee/ranged/elite） | **织丰做派 = basic=elite=guard 单兵复用**（不造多兵种树） | A | 待做（同做派最少化：guard 1 + militia 2） |
| C 组路人 40 职业（townsman/townswoman/villager/blacksmith…） | 织丰有完整 sho_* NPC 体系 | **B 织丰做+我们最小集不做**（后续 T4 数据层）；但最小 4 个（guard/villager/townsman/townswoman）场景生成必撞 → 提前到 A | 核心 4 个随 Party 线做，其余 T4 |
| gear_practice_dummy_<culture>（43826）/ nervous_caravanmaster | 织丰有 | B（触发 = 进练习场/商队事件——当前 v0 触发面小） | 留 T3 观察/随场景线 |
| 未成年变体 / tournament_master / notary / 舞女 / beggar | 织丰有 | **C 都不做**（v0-T4 不再评；T4 数据层重审） | 不做 |
| 日式素材（mesh/face/banner_key 日本风） | 织丰有（encounter_sho_lord 等） | B/T5 | 先抄官方值（能跑），T5 日化 |
| 卡拉迪亚 LRS 文本（world_lore_strings 注释块） | 织丰 n/a | C | 不做（注释态） |

## 织丰经验索引（2026-09-08 系统盘点）

> **完整版见 `Knowledge/织丰自定义世界观经验.md`（65 条世界工程补丁全清单 + 数据做派方法论 + 证据行号）。发现新经验 → 先更新 Knowledge 文档，再同步本表。**
> 用法：到 T3/T4 环节先回查 Knowledge 对应面，按织丰同款适配。

| 面 | 织丰做法 | 采纳时机 |
|---|---|---|
| 建号/捏脸 | FaceGen 种族名收窄（否则 CC 显示 Imperial/Aserai 出戏） | **T2 可用，建议现在抄** |
| 建号阶段 | 原版 CC 框架 + 自加 ClanNamingStageView（家族命名阶段） | T2+（可选第 3 阶段——太阁5 玩家家名对味） |
| 遭遇-战斗全链 | PlayerEncounter×6 + Encounter 菜单 + MapEvent×5 + BattleEndLogic 等 | T3（进图后第一战前回查 Knowledge §5.2） |
| 城镇体验 | PlayerTownVisit×5 + Barber×3 + SettlementMenuOverlay | T3（§5.3） |
| 地图视觉 | SettlementNameplate×5 + PartyVisual×4 + 天气/相机 | T3（§5.4） |
| 战争层 | 攻城机械×8 + SiegeAftermath + CustomBattle×6 | T4（§5.5） |
| 政治 UI | KingdomManagementVM/Encyclopedia/BannerEditor 系列 | T4+（§5.6） |
| 玩法扩展 | Diplomacy 系 TPatch | ❌ 不采纳：LWN 走自家玩法线 |
| 母本工程 | GameManager 6 步/CC 框架/主线接线/出生点 | ✅ 已采纳 |

## 一句话现状

**太阁5 还原工程的自定义战役模式（TaikouCampaign）已过"建世界事件链"第 3 颗雷（Companion NRE，根因=拷贝文件残留原版引用×2 层）**——DLL+数据已就位，**下一站 = 跑游戏 → 建号界面（T2）**。

## 🔴 三大架构裁定（用户拍板，已入 CLAUDE.md / memory）

1. **三单元**：LivingWorldNpcs = 通用基座（功能只写 LWN）；Taikou = 纯数据包（1.2.12 机 `MB2_Version/MB2_1.2.12/.../Modules/Taikou`，**不依赖织丰**）；ShokuhoTaikouExpansionPack = **已归档**（织丰城池不合格）；未来三国走同通路
2. **双模式开关**：LWN 启动时 `ModuleHelper.GetModuleInfo("Taikou")?.IsSelected`——**不行！判据 = `LivingWorldNpcs.ModuleActivationHelper.IsModuleEnabled`（既有轮子，判据 = `TaleWorlds.Engine.Utilities.GetModulesNames()` 启用列表）**。勾选 Taikou → 主菜单接线战役模式；否则纯功能包
3. **GameType 匹配键 = 战役类名**（实证：官方 `new Campaign()`→"Campaign"；织丰 `ShokuhoCampaign`；我们 `TaikouCampaign`）——`IncludedGameTypes` 与 `GetType().Name` 对比（`MBObjectManagerExtensions.LoadXML` cs 实锤）

## 反编译实证结论（贵，下 session 别重挖）

| 主题 | 结论 | 出处 |
|---|---|---|
| 织丰完整建号链 | `ShokuhoCampaignGameManager : MBGameManager`（DoLoadingForGameManager 6 步）+ `ShokuhoCampaign : Campaign`（namespace `Shokuho.ShokuhoCustomCampaign`）+ 主菜单接线（剔 SandBoxNewGame/StoryModeNewGame/ContinueCampaign + AddInitialStateOption）+ CC = `CharacterCreationContentBase`（namespace `TaleWorlds.CampaignSystem.CharacterCreationContent`）| /tmp/shokuho.txt 已丢（一次性），结论在本表 |
| 沙盒官方初始化 | `SandBoxGameManager`（SandBox.dll）case0-5 与我们的实现一字不差；`SandBoxManager.Initialize` = 99 个 Default* 模型清单（含 `DefaultCharacterDevelopmentModel` @cs12:41112）；`InitializeSandboxXMLs` load 顺序 = NPCCharacters→Heroes→Kingdoms→Factions→WorkshopTypes→LocationComplexTemplates→Settlements | |
| 出生点 | `Campaign.DefaultStartingPosition` **非 virtual**，引擎两调用点锁定基类 → 出生坐标在 CC 的 `OnCharacterCreationFinalized` 里写 `MobileParty.MainParty.Position2D`（织丰同法 @shokuho:164613） | |
| LoadBasicFiles id 清单 | Monsters/SkeletonScales/ItemModifiers/ItemModifierGroups/CraftingPieces/WeaponDescriptions/CraftingTemplates/BodyProperties/SkillSets（TaleWorlds.Core.Game 436-447） | |
| `CreateMergedXmlFile` 越界 | 空 toBeMerged → `toBeMerged[0]` 越界 = 某 id 在当前 GameType 下 0 段匹配 | objsys:837 |
| Culture 必备组 | lord_templates + rebellion_hero_templates + **notable_and_wanderer_templates**（缺 = `CompanionsCampaignBehavior` NRE）；`default_party_template` 缺 = CalculateAverageWage NRE | |
| navmesh | 战役图必须 `navmesh.bin`（RNM1 v3）或 `nav_mesh_auto_generated_="true"`；缺失 = native AccessViolation | |
| 场景实体绑定 | 实体 name=settlement StringId + `<tags><tag name="town"/></tags>`（子标签非属性）+ 世界坐标=XML posX/posY（0.02m 实证）+ 外层 `campaign_icon_capsule_NN` 惯例（Z=20 哨兵，93 组 261 据点）| |

## 已落盘改动清单（全部完成）

**A. LWN（ExampleModVS/ExampleMod/ExampleMod/Core/CampaignMode/）**：
`CampaignModeActivator.cs`（双模式开关+主菜单接线）/ `LivingWorldCampaign.cs`（:Campaign，含 dump 诊断）/ `LivingWorldCampaignGameManager.cs`（:MBGameManager，DoLoadingForGameManager 全链）/ `LivingWorldCharacterCreationContent.cs`（CC 2 阶段 + OnCharacterCreationFinalized 出生点）/ `TaikouCampaign.cs`（thin，`TaikouStartingPosition=(973,421)`）——csproj 已加 Compile；MySubModule.cs 已调 TryActivateCampaignMode。

**B. Taikou 数据包（1.2.12 机 Modules/Taikou/）**：
- `SubModule.xml`：**19 个 TaikouCampaign 段**（世界数据 8 + 基础 11：BodyProperties/SkillSets/EquipmentRosters/Concepts/CraftingPieces/MusicInstruments/MusicTracks/GameText×2/…）
- `ModuleData/taikou_*` 文件：items（目录 10 文件）/bodyproperties/skill_sets/equipment_sets/concepts/crafting_pieces/location_complex_templates/music_instruments/music_tracks/module_strings/world_lore_strings/heroes（最小 7 条）
- `spcultures.xml`（Culture.ikoku：default_party_template + notable_and_wanderer 5 引用 + lord/rebellion 模板）
- `spkingdoms.xml`（kingdom_oda + kingdom_imagawa 已删——今川精简实验）
- `spclans.xml`（clan_oda + player_faction；clan_imagawa 已删）
- `settlements.xml`（只 town_kyoto，pos=969.424/421.563，**已闭合**，XML parse OK）
- `spnpccharacters.xml`（main_hero=官方全量版 + 织田 2 + 今川 2）
- `partyTemplates.xml`（main_hero_party_template）
- `SceneObj/Main_map/`：scene.xscene（nav_mesh_auto_generated_="true"+town_kyoto 实体 969.4/421.6/z4.6+tags）+ **navmesh.bin 已生成（3.58MB RNM1 ✅）**

## 排雷链记录（每颗一行：根因→修）

1. 主菜单点 NewGame → `CreateMergedXmlFile` 越界 = BodyProperties/SkillSets/… 段缺失（GameType 过滤后 0 段）→ 补 4 段
2. 同点再崩 = CraftingPieces 0 段 → 补
3. 同点再崩 = Heroes/LocationComplexTemplates 0 段 → 补
4. 同点再崩 = MusicInstruments/MusicTracks（SandBoxSubModule.OnRegisterTypes 无条件调）→ 补
5. `LoadBasicFiles` 仍崩（误判）→ 实际 = Items（SandBoxCore 物品库被过滤 + 自身空壳）→ 拷贝官方物品目录全量 + GameText 补 2 段
6. `CalculateAverageWage` NRE = Culture.ikoku 缺 default_party_template → 补
7. `Campaign.InitializeGamePlayReferences` NRE（PlayerTroop=main_hero null 链）→ main_hero 条目换官方全量版
8. `Clan.ValidateInitialPosition` NRE（**真凶 = 我们删村庄时把 settlements.xml 弄坏了（缺 `</Settlements>`）→ 首装失败 → Settlement.All=0**）——修复+全量 XML 校验（教训：脚本改 XML 必 parse）
9. 同点再崩（误判今川）→ 今川全删（最小世界实验，设计保持）
10. `CompanionsCampaignBehavior.InitializeCompanionTemplateList` NRE = culture 缺 notable_and_wanderer_templates → 补（引用世界内存在的 5 角色）
11. 同函数再崩 → **结案（见 T1 结案）**：真因 = 拷贝文件 1835 处原版文化引用 → GetPresumedObject 创 8 裸文化桩；+ 模板引用 5 角色只定义 3（今川真空）+ 幽灵 id（peasant_farmer 实为织丰角色 / lord_template_empire_male = 自编 id）→ 修复三层（数据清洗/数据自洽/代码加固）+ 校验脚本
12. `BackstoryCampaignBehavior.OnNewGameCreated` NRE（原版卡拉迪亚前史硬编码 8 领主+town_V6）→ **结案：织丰同款屏蔽**（Debug/BackstoryCampaignBehaviorPatch 打 RegisterEvents prefix false，实证 Shokuho.dll:99600）
13. `Kingdom.OnNewGameCreated` NRE（Leader=null 链）→ **结案**：英雄带装备+neutral/party/roster 数据 → 置位 partial-followup（王都=京，反射 private setter）；⚠️ 两个坑记录：①OnInitialize 置位被引擎 OnNewGameCreated 洗白（时点必须 partial-followup）②**GetObjectTypeList<T> 在 partial 时点返回 null**（Kingdom 被吞教训——改 Campaign.Current 合集）
14. `Settlement.SpawnMilitiaParty` NRE（Culture.MilitiaPartyTemplate 空）→ Party 引线全挂（militia_template 复用）
15. `WorkshopsCampaignBehavior.BuildWorkshopForHeroAtGameStart` NRE（城镇无 Notables → ChooseWeighted(空)）→ **根因 = 文化 N&W 模板池只有 Lord**（CreateHeroAtOccupation 从 N&W 挑职业）→ N&W 补 merchant/artisan；**证据三连：名流非 XML 预定义**（heroes.xml 0 商人/settlement 0 Notables/Hero cell 8 属性）
16. `CaravanPartyComponent.InitializeCaravanOnCreation`（First(CaravanGuard&&Lv26&&ikoku) 零命中）→ **根因 = occupation 枚举非法**（12+ 个非 33 成员）→ **枚举解析失败 = NPCCharacters 段静默截断** → occupation 全合法化 + caravan_guard 等级 1→26
17. `Workshop.InitializeWorkshop`（type null）→ spworkshops 空壳 + SubModule 未注册 WorkshopTypes 段 → 拷官方 14 类型 + 注册
18. `TownMarketData.GetPrice` NRE（分类表空）→ 根因 = 29 物品 merchandise=0 + Outputs 38 分类无匹配 → 6 资源物品转 ikoku+merch + Outputs 裁 9 类
19. `AlleyCampaignBehavior.OnNewGameCreated` DivideByZero（`settlement.Alleys.Count=0` → `i % 0`）→ **Alleys 来源 = `<CommonAreas><Area>` 子节**（Settlement.Deserialize 145801 实锤）→ town_kyoto +2 巷；+`gang_leader` 模板（occupation=**GangLeader**——IsGangLeader 判定→黑帮名流 SetOwner 另一 `% source.Count()` 防炸）
20. CC 启动崩 `CharacterCreationCultureStageVM.SortCultureList`（`Single(x=>CultureID.Contains("vlan"/"stur"/...))`——原版六大文化摆拍排序器）→ **LWN 补丁 prefix 跳过**（`CharacterCreationCultureStageSortPatch`——自定义世界无原版文化）
21. `RecruitmentCampaignBehavior.FindTotalMercenaryProbability` NRE（`Culture.BasicMercenaryTroops` 空节→GetRandomElementInefficiently null）→ +`mercenary_ikoku`（occupation=Mercenary）→ Culture 挂 1 引用
22. `NameGenerator.GenerateClanName` NRE（Culture 名字池空——clan_names/male_names/female_names 空节）→ **织丰日式名池植入**（21/390/214——对味）——崩在我们自己的 OnCultureSelected（生成家名）
23. CC 文化界面**没翻译/没大图/按钮小字**（2026-09-08）→ 三合一：①**文本** = `CharacterCreationCultureVM` 构造时 `FindText("str_culture_rich_name"/"str_culture_description", Culture.StringId)` variation=ikoku——织丰做派 = module_strings.xml 加 `id="str_culture_rich_name.ikoku"`（实锤 Shokuho module_strings.xml:193-207；Native 同构 5947 起）→ Taikou/taikou_module_strings.xml 补 2 词条 + 新建 `ModuleData/Languages/CNs/taikou_culture_CNs.xml`（照 Shokuho_CNs 格式）；②**大图** = `CharacterCreationCultureVisualBrushWidget.SetCultureVisual(id)` → 4 层 `Culture.Banner.Layer.1..4` 每文化一个 `<Style Name=id>`（Native 仅 7 官方 Style——brush XML 实锤 Style 集）→ 无 ikoku state = 空图只留边框；**修法 = LWN `Debug/CharacterCreationCultureVisualFallbackPatch`**（Prefix：id 在 Layer.1 Style 集里不存在 → 换官方兜底 `empire`——判据数据驱动，不硬编码内容包文化；1.2.12/1.5.2 同名同参同私方法已验证，csproj 新引用 TaleWorlds.MountAndBlade.GauntletUI.Widgets）；③**按钮小字** = ShortenedNameText 解析失败残留——补文本自动消失。织丰对照：织丰把文化选择界面整套自建（Sho*View + 地图选文化艺术 + ShoCulture.MapButton.Nankai 自家 brush），v0 我们 = 引擎线 + 官方 art 兜底，T5 日化时再定自建/自美术。**实测通过**（截图：大图/Japanese/描述/无 ERROR）
24. 选完文化进 **Generic 属性阶段即崩**：`CharacterCreationGenericStageVM` 构造 → `CharacterCreationOnInit(0)` → `CharacterCreationMenus[0]` 越界 = **内容套餐列表空**。链路实锤：GenericStageView 按菜单序号 0..MenuCount-1 逐页消费；菜单由 ContentBase.`OnInitialized(CharacterCreation)` 里 `AddNewMenu` 建造（原生 SandboxCharacterCreationContent / 织丰 ShokuhoCharacterCreationContent.OnInitialized 6 菜单全链——织丰 = Family/Childhood/Education/Youth/Adulthood/AgeSeletion 全包 + 11 职业）；我们没实现 OnInitialized = 0 菜单 = 崩溃。**修法**：LivingWorldCharacterCreationContent 加 OnInitialized → v0 最小「出身」菜单（1 菜单 3 选项：武士从者/商人之子/浪人——技能+属性加成，引擎选项自带 ApplySkillAndAttributeEffects；General 世界 2 选项兜底）。彩蛋：Culture 阶段不需要菜单（CultureVM 不查菜单）——所以文化阶段能过、Generic 才崩
25. Generic 阶段 **Tick NRE（`_playerOrParentAgentVisuals` null）**：模型数据 FaceGenChars 为空——填入方 = FaceGen 阶段（`CharacterCreationFaceGeneratorView` 完成时 `new FaceGenChar(...)` → `ChangeFaceGenChars` 实锤）；我们的阶段链是 [Culture, Generic, Review] **缺 FaceGenerator**。原版顺序（1.2.12 SandboxCharacterCreationContent 实锤）= Culture → FaceGenerator → Generic → BannerEditor → ClanNaming → Review → Options；织丰同款（+ RacesPatch 收窄种族）。**修法**：阶段链插入 `CharacterCreationFaceGeneratorStage`（在 Culture 与 Generic 之间）；模型预览场景 = Native character_menu_new（引擎硬编码，任何自定义战役共用）
26. FaceGen 阶段崩 **`MBGlobals.GetActionSet` NRE**：真凶 = `MBGlobals._actionSets` 静态词典 **从未初始化**（`InitializeReferences()` 没人调 → null.TryGetValue 崩）——不是动作库资源缺失（Native 模块永远在，你的原则没错）。必调点实锤：SandBox `EditorSceneMissionManager.DoLoadingForGameManager` case0（紧跟 ModuleData 加载）；织丰自家 GameManager 同样补调（Shokuho.dll:121150）。**我们漏因**：LivingWorldCampaignGameManager.DoLoading 状态机 case0 与母本分叉时跳过了该行。**修法**：case 0 加 `MBGlobals.InitializeReferences()`（幂等：_initialized 守卫；任何内容包战役共通）
27. FaceGen 阶段崩（26 修后新 NRE）——**捏脸模板段被 GameType 白名单过滤**：探针三连（InitBodyGenerator/OpenScene/AddCharacterEntity 全过）→ 死点在 ctor 尾部模板盘查段：`GetObject<BasicCharacterObject>("facgen_template_test_char_0").GetBodyProperties()` NRE——`facgen_template_test_char_0..9` 定义在 **SandBoxCore `spnpccharactertemplates.xml`（NPCCharacters 段），只注册 Campaign/CampaignStoryMode/CustomGame/EditorGame**，TaikouCampaign 下为 null。织丰做派 = 自家副本+自家 GameType 注册（Shokuho SubModule.xml:417-423 实锤）。**修法**：①拷 SandBoxCore 模板文件进 Taikou 模块 ②sanitize 文化引用→ikoku（140 处）③SubModule.xml 注册 `<XmlName id="NPCCharacters" path="spnpccharactertemplates"/>`（仅 TaikouCampaign）④⚠️ 纠偏记录：模板引用 120 件官方物品——我此轮曾全量恢复官方物品库（1759 件）违背"**模仿织丰做自建物品体系**"裁定，被用户当场纠正 → **回滚**（prune 重跑回 69 件精选集）+ **模板装备槽/升级线清空**（887 处）——模板 = 容貌样板，T4 自建物品后再挂自家装备；物品库裁剪真源 = prune_taikou_items.py 不可绕过。checker：0 悬空 0 未知
29. **进图后解除 pause 即崩 `PartyVisual.RefreshPartyIcon` KeyNotFound**（2026-09-08）：`gateBannerEntitiesWithLevels[wallLevel]`——`GetWallLevel()` = 城防建筑（Fortifications）**当前等级**（1.2.12 `Town.GetWallLevel` 实锤；官方场景门旗组（`banner_pos` placeholder + `banner_l1/l2/l3` 墙旗实体）按 {1,2,3} 建组）——**京 town_comp 配了 `level="4"` 超出官方体系上限 3** → wallLevel=4 → dict[4] 缺 key 崩。**修法**：`town_comp_kyoto level 4→3`（数据；checker pass）。织丰对照：织丰场景只有 banner_pos（无 l1/2/3——他们城墙等级天生 ≤3 且未触发 4 级城）。**教训**：官方城墙等级体系上界 = 3；内容包"4 级城"需要自己做第 4 层门旗实体（T4 布局表时决定要不要）
30. **进图后相机看不到角色**（2026-09-08 用户实测）：CC 完成落场时大地图默认机位 = 官方地图坐标（camera_top/默认锚点），与玩家出生点脱节。织丰母本实锤做法 = `OnCharacterCreationFinalized` 里 `MapState.Handler.ResetCamera(true,true) + TeleportCameraToMainParty()`（Shokuho.dll 反编译实锤；织丰场景无 camera_top 实体——他们"看得见角色"靠的就是这段）。**修法**：LivingWorldCharacterCreationContent.OnCharacterCreationFinalized Taikou 分支出生点设置后照抄（编译 0 错）
31. **进图每小时 tick 崩 `RetirementCampaignBehavior.CheckRetirementSettlementVisibility` NRE**（2026-09-08）：`_retirementSettlement = Settlement.Find("retirement_retreat")`（SandBox.dll 硬编码）——官方退休据点 `retirement_retreat`（RetirementSettlementComponent + map_icon bandit_hideout_b + gui_bg_village_battania + retreat_complex + scn_retirement 全官方资源）我们世界没有 → null → tick NRE（相机 WASD 失灵 = 崩在每 tick 的连带效果）。织丰做派 = 自建整套退休体系（ShokuhoRetirementCampaignBehavior+RetirementEncounter+OpenRetirementMission——KCD 水准）。**修法（v0 选 A：补数据，不屏蔽）**：官方最小条目追加进 settlements.xml（position 放京边 1090/500；culture 洗 ikoku；checker 0 悬空）——退休菜单/对话随组件自动注册；T4 布局表再统一管位置
32. **无法存档 `SaveFailed: Could not find type definition of type: LivingWorldNpcs.CampaignMode.TaikouCampaign`**（2026-09-08）：战役类（Campaign 子类）进存档必须有类型注册——机制实锤 = `SaveableTypeDefiner` 派生类由引擎启动自动发现实例化（StoryMode `SaveableStoryModeTypeDefiner` base=320000 注册 CampaignStoryMode id=1；织丰 `ShokuhoSaveableTypeDefiner` base=3564814 注册 ShokuhoCampaign id=69——两者均无显式 new 调用点 = 自动发现实证）。**修法**：新增 `Core/CampaignMode/LivingWorldSaveableTypeDefiner.cs`（base=4455667；注册 TaikouCampaign=1 + LivingWorldCampaign=2——通用战役同样需要）
33. **相机不框住玩家**（2026-09-08 用户实测三轮）：OnCharacterCreationFinalized 里 teleport 时序不生效——CC 完成回调时 ActiveState 仍为建号状态、MapState 未推入 → 织丰同款代码被 `if (val != null)` 空检查跳过。织丰等效做法 = 自建 MapView（`ShokuhoMapView`，AddMapView 注入）初始化后再拉相机。**修法（轻量等价）**：`Debug/MapScreenCameraPatch.cs`——`MapScreen.OnInitialize` Postfix（地图就绪时刻，即织丰 MapView 初始化时机）→ `Handler.ResetCamera(true,true)+TeleportCameraToMainParty`。出生点同步外移（973,421→985,428：原坐标落城圈内贴塔，视线遮挡；新坐标 = 京门前一箭地）
34. **日本图相机「空气墙」：到京都（x≈969）以东就动不了**（2026-09-08 用户实测，T3 TODO②反转实锤）：相机目标位置每帧被钳进 `[Campaign.MapMinimumPosition, MapMaximumPosition]`（SandBox.View.dll `ComputeMapCamera` 反编译实锤），而这两个值来自 `SandBox.MapScene.GetMapBorders` = **读场景里 border_min / border_max 两个命名实体**。Taikou Main_map 克隆时主体地形+脚本实体都搬了、但**两个边界实体没带**：
   - **v1.2.12**：引擎对缺失**有兜底**——min=(0,0)、max=(**900,900**)、height=670（SandBox.dll 反编译实锤）→ 相机墙在 x=900 / y=900；京都(969,421) 恰好落在墙外一点 → 症状 100% 吻合
   - **v1.5.x**：`GetFirstEntityWithName("border_min").GetGlobalFrame()` **无 null 保护** → 缺实体 = 进图直接崩（比空气墙更狠，1.5.2 机必撞）
   - 织丰对照：Shokuho Main_map **有** border_min=(87,105,-7.98) / border_max=(2100,2100,1000)
   - **修法（数据）**：scene.xscene `<entities>` 顶部插入 `border_min`(0,0,0) / `border_max`(2048,1280,1000)——地形实体规格 = 16×10 节点 × 128m = 2048×1280（scene `terrain` 节点+`physics_world_max`+`flora_bounding_rect` 三处相互印证）；z=1000 取织丰值（控制最大缩放距离+远裁剪面，1.2.12/1.5.2 通用）——已落盘（1.2.12 游玩库与 1.5.2 主环境两份 xscene 实为同一文件），minidom parse 通过
   - **修法（日志）**：`Debug/MapBorderDiagnosticPatch.cs`——`GetMapBorders` Postfix 打一行 `[MapBorder]`（一次/局），识别引擎兜底信号（(0,0)/(900,900)/670 = 场景又缺实体），PatchAll 自动生效，1.2.12/1.5.2 同名方法二进制 grep 双命中
   - **教训**：蓝图自查表（本表 → 知识库）：**地图场景克隆必带实体 = border_min/border_max + 12 脚本实体清单（见 28）；缺 border 实体 1.2.12 静默降级成 900×900 相机墙、1.5.x 直接崩**——两种版本都要出图前 grep scene.xscene `border_min` 确认
28. **进大地图首步崩 `MapScreen.OnInitialize` NRE**（2026-09-08，CC 全链通过后的下一站=T3 第一雷）：`PrefabEntityCache = _mapScene.GetFirstEntityWithScriptComponent<CampaignMapSiegePrefabEntityCache>().GetFirstScriptOfType(...)` 无 null 守卫——**我们的 Main_map 缺官方 12 个地图脚本实体（官方 37906 实体：CampaignMapSiegePrefabEntityCache/MapColorGradeManager/SceneLeveler/SettlementPositionScript/Town Entity Manager/Town Scene Manager/river_generator/water_body/path_converger/sound_emitter/VolumeBox——我们只保住了 ReflectionCapturer）。修法（v0 一次补基建 5 + 京胶囊）**：①`CampaignMapSiegePrefabEntityCache`（引擎硬查询无守卫——NRE 元凶）②`MapColorGradeManager` ③`SettlementPositionScript`（地图锚点）④`Town Scene Manager` ⑤`SceneLeveler`——全部以官方原样空对象块插入 Taikou Main_map xscene；⑥京 = 官方 capsule 结构重拼：`campaign_icon_capsule_1`（Z=20 + Town Entity Manager，与官方 capsule_36 同构）→ children `town_kyoto`（转相对坐标 0/0/-15.399）。**未搬（刻意）**：river_generator/water_body（卡拉迪亚河/水，日本图上出现=出戏）、path_converger（路网）、sound_emitter（卡拉迪亚声）、VolumeBox——留 T3 配日本风。**教训**：地图场景 = 实体+脚本的"内容原型"，从 bigmap 基底克隆地形图时必须带官方脚本实体清单（写进 knowledge——地图场景脚本实体清单是内容包造图的必查表）；xscene 实体插入后 XML parse 必验（已过）。⚠️ navmesh 未受影响（地形未动，实体变化不要求重生成）

**🔴🔴 为"生成器真源"教训加强（16 雷两踩，不再犯）**：gen_taikou_culture_full.py 重跑会覆盖对 spcultures/spnpccharacters 的一切手改（N&W 增补被覆盖/佣兵被覆盖——两现场）。**铁令：凡改 Culture/NPC 数据 → 先改生成器 → 重跑 → checker**；生成器幂等判断的"已存在"检查要**覆盖所有新增 id**。

## 🔴🔴 不再犯清单（16 雷沉淀——改动前逐条自查）

1. **生成器唯一真源**：Culture/NPC 数据增补 = 先改 `gen_taikou_culture_full.py` → 重跑 → checker。**禁止直接改 spcultures/spnpccharacters 再指望存活**（两种死法：手改被下次重跑覆盖〔N&W 增补、佣兵——双现场〕；生成器幂等检查漏新 id〔gang_leader〕）。新增模板后立即把 id 纳入幂等判断。
2. **枚举字段必须过全集校验**：occupation 等枚举值对照引擎枚举全集（Occupation=33 成员）；写错 = NPCCharacters 段**静默截断**（不报错、只少加载——探针才知道）。
3. **每次数据改动后必跑**：`check_taikou_xml_references.py`（0 悬空）+ parse 所有改过 XML（minidom）。
4. **脚本/程序改 XML 必 parse**（09-07 未闭合教训 + 本次两处）。
5. **裁剪前建引擎白名单**（物品 51 / roster 5 / neutral 文化——prune 脚本常量；将来扩展内容包先查）。
6. **时点坑**：Kingdom.InitialHomeLand 置位必须 partial-followup（OnInitialize 会被洗白）；partial 时点 `GetObjectTypeList<T>()` 返回 null——用 `Campaign.Current.*` 合集。
7. **行为链数据依赖一次给全**：N&W 池（名流生成）/名字池（家名生成）/basic_mercenary_troops（雇佣兵）/Alleys（黑巷）/工坊 Outputs 分类——这些组从雷链里学到的"文化必备组"，新内容包照清单配。

## 纪律提醒（这轮踩过的）

- **脚本/程序改 XML 必 parse 验证**（11:30 的未闭合 = 15 轮空跑的导火索）——改完立即 minidom.parse
- 世界数据「半成品型 NRE」模式：**每次 = 某 id/属性没给它就炸**——排雷方法 = 反编译错误函数看 null 点 → 补数据，不该猜
- 数据变动 = 无需编译；DLL 变动 = dotnet build 验语法（本机 MB2_PATH=1.2.12 = 真验证）+ VS2022 最终编译
- LWN 模块 = **1.2.12 环境经 junction 与主环境共享**（Modules/LivingWorldNpcs），改哪边都生效；**只在 1.2.12 编译目标下验证**
- 铁律 22（生成物免手改）：settlements 等数据现在是手写档，**接入布局表生成器后**转为生成物（重跑生成）

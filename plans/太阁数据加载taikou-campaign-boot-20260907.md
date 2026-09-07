# TaikouCampaign 启动链排雷交接（2026-09-07 深夜）

> 本文件 = 会话交接。**下个 session 从这里开始**，不用重跑任何反编译（结论全部实锤并附出处）。

## 🔴 TODO（下一步从这里开始）

- [x] **T1（已完成 2026-09-07）**：Companion NRE = 三段链（详见「T1 结案」）——数据根治 + LWN 加固层已落盘，**下一步 = 跑游戏验证**
- [ ] **T2**：过了行为链 → **建号界面**（CC 2 阶段）→ 预期下一雷：CC 阶段/视图问题 → 崩点照旧处理（**先读 `Modules/LivingWorldNpcs/Debug/StoryEngine_RuntimeLog.txt`**）
- [ ] T3：建号完成 → **日本图** → 京塔图标可见（实体 z=4.6）/出生点 (973,421)
- [ ] T4：全量数据（TK5 布局表 → place_settlements.py 批量 + settlements.xml 同步）——**场景实体 = 从 `Main_map_native` 整组拷贝改名的路线**（知识文档五节已有教程）
- [ ] T5：日式素材（先 Native mi_*，织丰 sho_* 因"不依赖织丰"裁定不可用——后续决断来源）

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
13. **CC 升级（2026-09-08，追问"主角文化/外观怎么定"触发）**：原 CC 2 阶段砍掉了属性分配/名字 → 升 **Culture → Generic（属性/技能分配）→ Review**；`OnCultureSelected` 补织丰同款家名生成（GenerateClanNameforPlayer）——**下一雷预告：Generic 阶段页（引擎自带 VM）实测**

## 纪律提醒（这轮踩过的）

- **脚本/程序改 XML 必 parse 验证**（11:30 的未闭合 = 15 轮空跑的导火索）——改完立即 minidom.parse
- 世界数据「半成品型 NRE」模式：**每次 = 某 id/属性没给它就炸**——排雷方法 = 反编译错误函数看 null 点 → 补数据，不该猜
- 数据变动 = 无需编译；DLL 变动 = dotnet build 验语法（本机 MB2_PATH=1.2.12 = 真验证）+ VS2022 最终编译
- LWN 模块 = **1.2.12 环境经 junction 与主环境共享**（Modules/LivingWorldNpcs），改哪边都生效；**只在 1.2.12 编译目标下验证**
- 铁律 22（生成物免手改）：settlements 等数据现在是手写档，**接入布局表生成器后**转为生成物（重跑生成）

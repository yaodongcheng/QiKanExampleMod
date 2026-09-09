# 🔴 场景跳转与 Mission 展开机制分析（原版 + 织丰全链路）

> 分析对象：Bannerlord v1.5.2 引擎（`TaleWorlds.CampaignSystem.dll` / `SandBox.dll` 反编译）+ 织丰 v 当前版（`Modules/Shokuho`，1.5.2 兼容版）
> 验证手段：`ilspycmd -t <类型>` 反编译 + 模块 XML 原文。**反编译目标版本以各 DLL 实际为准**：主线 API 家族在 1.2.12 与 1.5.2 均存在（下文 §6）。

**一句话结论**：骑砍 2「进场景（打开 Mission）」只有 **7 个 API 家族，全部由原版引擎提供**；织丰**没有改动任何跳转代码**，只做数据替换（settlements.xml 换日本场景）+ 自注册菜单 + 一个自定义 Encounter 子类。**不只是定居点会展开场景**——野战、藏身处、攻城、比武大会、竞技场决斗、棋盘游戏等业务功能走的是同一套「Mission 打开」机制。

---

## 0. 先分清三个概念（最容易混的地方）

| 概念 | 本质 | 例子 | 会不会加载场景 |
|---|---|---|---|
| **菜单位 GameMenu** | 一个「菜单页面」（id + 选项列表），可在大地图/场景内弹出 | `GameMenu.SwitchToMenu("town")`、`GameMenu.ActivateGameMenu("town_backstreet")` | ❌ 只换菜单页 |
| **场景位 Mission/Scene** | 加载一个 `.sco` 场景并跑 Mission 层（实体、AI、时间流逝） | 进城镇/酒馆、野战、攻城 | ✅ 本分析主题 |
| **UI 屏幕 Screen** | 弹窗面板，无场景无世界 | 市场交易 `InventoryScreenHelper.OpenScreenAsTrade`、城镇管理 `MenuContext.OpenTownManagement`、招募 `OpenRecruitVolunteers` | ❌（归类为「假场景」，见 §3.11） |

> 🔴 **菜单切换 ≠ 进场景**。`SwitchToMenu` 只是换一个菜单页；真正进场景只有 §1 的 API。看到业务代码 `SwitchToMenu("xxx")` 别误以为它「打开了场景」。

---

## 1. 进场景 API 总表（原版 1.5.2，全部 7 个家族）

| # | API（静态入口） | 用于 | 场景从哪来 |
|---|---|---|---|
| 1 | `PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(Location nextLocation, Location? prevLocation=null, CharacterObject? talkToChar=null, string? playerSpecialSpawnTag=null)` | **地点场景总入口**：进城（主街/酒馆/主城府/地牢/竞技场/民居/后街）| 由 `Location` 决定（= settlement 数据里该地点配的 `scene_name`）|
| 2 | `CampaignMission.OpenBattleMission(scene, usesTownDecalAtlas, sceneLevels)` 或 `(MissionInitializerRecord rec)` | 野战/遭遇战（撞队、劫商队、伏击战）| 战场地图（程序选地形） |
| 3 | `CampaignMission.OpenHideoutBattleMission(scene, troops, isTutorial)` / `OpenHideoutAmbushMission(sceneName, troops, Location)` | 藏身处两阶段（正面清剿 / 潜行伏击）| **动态指定场景名**（如 `sea_bandit_a`），不读 settlement 数据 |
| 4 | `CampaignMission.OpenSiegeMissionWithDeployment(scene, wallHitPoints, engines, isPlayerAttacker)` / `OpenSiegeMissionNoDeployment(...)` / `OpenSiegeLordsHallFightMission(scene, troops)` | 攻城三态（部署攻城 / 突围 sally-out / 攻破后领主殿肉搏）| 🔴 **场景 = 该 settlement 的 `"center"` 地点场景**（`besiegedSettlement.LocationComplex.GetLocationWithId("center").GetSceneName(wallLevel)`）——攻城与定居点数据强绑定 |
| 5 | `CampaignMission.OpenArenaDuelMission(scene, arenaLocation, duelCharacter, requireCivilianEquipment, ..., customAgentHealth)` | 竞技场 1v1 决斗（玩家挑战/被挑衅）| 该城 arena 地点的场景 |
| 6 | `SandBoxManager.Instance.SandBoxMissionManager.OpenTournamentFightMission/OpenTournamentArcheryMission/OpenTournamentHorseRaceMission/OpenTournamentJoustingMission(scene, tournamentGame, settlement, culture, ...)` | 比武大会 4 类（格斗/射箭/赛马/长矛）| 该城 arena/马场场景 |
| 7 | （实现层）`SandBox.dll` 的 `CampaignMissionManager : ICampaignMissionManager` | §1 各 API 的真正实现者 | — |

**关键认识**：
- 入口分两层：**业务层**（Quest/Behavior/菜单回调）→ `CampaignMission.Open*` 静态门面 → **接口层** `ICampaignMissionManager` → 实现 `CampaignMissionManager`（SandBox.dll）。
- 织丰/任何 mod 想开场景，**只需要调 §1 这些 API**，不需要懂场景加载细节。

---

## 2. Settlement（定居点）地点跳转 —— 数据 + 菜单 + 链路

### 2.1 数据层：两个文件决定「每个地点 = 哪个场景」

**① 模板：`ModuleData/location_complex_templates.xml`（模板打底）**

定义「一类定居点有哪些地点 + 每个地点的属性」。属性全表：

```xml
<LocationComplexTemplate id="town_complex">
  <Location id="center"
    name="{=centertowncomplex}Center"
    scene_name="empire_town_a" scene_name_1 scene_name_2 scene_name_3  <!-- 三档繁荣度场景 -->
    indoor="false" max_prosperity="500"
    ai_can_enter="CanAlways" ai_can_exit="CanAlways"
    player_can_enter="CanIfSettlementAccessModelLetsPlayer" player_can_see="CanAlways" />
  <!-- ...其他地点... -->
  <Passages>  <Passage location_1="center" location_2="tavern" /> </Passages>  <!-- 场景内通道 -->
</LocationComplexTemplate>
```

- `scene_name` 无下划线 = 各繁荣度通用；`scene_name_1/2/3` = 按繁荣度档位选。
- `Passages` = 地点间通道（城门/传送点），脚本命令触发地点切换。

**② 定型：`settlements.xml`（内嵌覆盖）**

每个 `<Settlement>` 内嵌 `<Locations complex_template="...">`，子节点 `<Location id=...>` **只覆盖两样**：`scene_name(_1/2/3)` + `max_prosperity`（引擎反编译实证：先 `new LocationComplex(complexTemplate)` 建全量模板地点，再按 id 覆盖这两个字段，**其余属性一律取自模板**）：

```xml
<Locations complex_template="LocationComplexTemplate.town_complex">
  <Location id="center" scene_name_1="sho_town_a" scene_name_2="sho_town_a" scene_name_3="sho_town_a"/>
  <Location id="tavern" scene_name="sho_tavern_a"/>
  <Location id="lordshall" scene_name_1="sho_keep_scene" .../>
  <Location id="prison" scene_name="sho_prison_b"/>
  <Location id="house_1" scene_name="empire_house_d_interior_house"/>
</Locations>
```

**③ 模板覆盖合并语义（引擎模块系统）**：同名 `LocationComplexTemplate` id 由**后加载模块整对象覆盖**，不同名保留。原版（SandBox）complex 清单：`town(center/arena/tavern/alley/lordshall/prison/house_1-3)`、`village(village_center)`、`castle(center/lordshall/prison)`、`ambush(center=ambush_scene_2)`、`hideout(hideout_center, scene="")`、`retreat(retirement_retreat=scn_retirement)`。织丰覆盖了前 5 个同名模板（其模板还新增 `practice_arena`→`dojo_test`），`hideout_complex` 未被织丰定义 → 保留原版。

> 🔴 **模板是默认值权威**：`indoor`/`ai_can_enter`/`player_can_enter` 等全部属性只读模板。谁想改地点属性，改模板文件，不是 settlements.xml。

### 2.2 菜单 id 表（原版 1.5.2，settlement 相关全量）

| 域 | 菜单 id |
|---|---|
| 城镇 | `town`（主街）、`town_outside`、`town_wait`、`town_guard`、`town_inside_criminal`（犯罪中被抓）、`town_keep`（城主府）、`town_backstreet`（后街酒馆区）、`town_arena`（竞技场）|
| 城堡 | `castle`、`castle_outside`、`castle_guard`、`castle_enemy_keep`、`castle_dungeon`、`town_keep_dungeon` |
| 村庄 | `village`、`village_outside`、`village_looted`、`raiding_village`、`village_hostile_action` |
| 渡口 | `port_menu`（🕐 1.4+ 新增，1.2.12 无）|
| 潜行/乔装 | `menu_sneak_into_town_caught/succeeded`、`disguise_not_first_time` |
| 藏身处 | `hideout_place` |
| 战斗相关 | `encounter`、`join_encounter`、`army_encounter`、`menu_siege_strategies`、`break_out_menu`、`join_siege_event`、`menu_castle_entry_granted/denied`、`army_wait`、`army_wait_at_settlement` |

### 2.3 跳转三式（进的地点场景的三种写法）

1. **显式**（菜单回调用，标准姿势）：`PlayerTownVisitCampaignBehavior.OpenMissionWithSettingPreviousLocation("center", "tavern")` —— 私有助手，内部：
   ```csharp
   GameMenuManager.NextLocation = LocationComplex.Current.GetLocationWithId(missionLocationId);   // 目标地点
   GameMenuManager.PreviousLocation = LocationComplex.Current.GetLocationWithId(previousLocationId);  // 返回地
   PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(GameMenuManager.NextLocation);
   GameMenuManager.NextLocation = null; GameMenuManager.PreviousLocation = null;
   ```
2. **自动闸**：`Helpers.CheckAndOpenNextLocation(args)`（菜单每 tick 检查）——只要 `GameMenuManager.NextLocation != null && 当前是 MapState` 就自动开场景，并按 `NextLocation.StringId` 分支**决定回来后落在哪个菜单**（`tavern`→`town_backstreet`、`arena`→`town_arena`、……）。**业务代码只设 `NextLocation` 即可在任何菜单外自动入场景**。
3. **直接**：业务代码自己 `new Encounter` + `CreateAndOpenMissionController(Location, ...)`（织丰决斗就这写法，见 §4）。

### 2.4 典型链路（以「进酒馆」「进地牢」为例）

```
大地图点击城镇 → 菜单 "town"（主街）
  ├─ 选项「Go to the tavern district」 → GameMenu.SwitchToMenu("town_backstreet")
  │      → 选项「Visit the tavern」 → OpenMissionWithSettingPreviousLocation("center","tavern")
  │            → CreateAndOpenMissionController(tavern Location) → 场景 = sho_tavern_a（织丰）✓
  ├─ 选项「Enter the keep」 → SwitchToMenu("town_keep")
  │      → 选项「Enter the dungeon」 → OpenMissionWithSettingPreviousLocation("center","prison") → sho_prison_b ✓
  ├─ 城镇管理/市场/招募 = UI 屏幕（非场景）
  └─ 选项「Enter the castle/town」 → ("center" 直接进主街) ✓
```

竞技场：`game_menu_town_town_arena_on_consequence` → `("center","arena")`。城堡侧：`castle` → 「Go to the dungeon」→ `("center","prison")`（`castle_dungeon` 菜单）；敌方城堡走 `castle_enemy_keep`。

---

## 3. 非定居点的业务场景（🔴 重点：场景不止定居点）

| 业务 | 触发 | 打开 API | 场景来源 |
|---|---|---|---|
| 野战/遭遇战 | 大地图撞敌方队伍、劫商队、伏击 | `OpenBattleMission(rec)` | 程序地图（非 settlement 数据）|
| 藏身处清剿 | 藏身处据点菜单 → 进攻 | `OpenHideoutBattleMission(scene,...)` | 动态指定（如 `sea_bandit_a`）|
| 藏身处潜行 | 藏身处菜单 → 潜行突袭 | `OpenHideoutAmbushMission(sceneName,troops,location)` | 动态指定 |
| 攻城（攻城部署） | 包围 → `menu_siege_strategies` 选策略 | `OpenSiegeMissionWithDeployment(...)` | 🔴 **目标 settlement 的 `center` 场景** |
| 攻城（突围/守方出城） | sally out | `OpenSiegeMissionNoDeployment(...)` | 同上 |
| 攻城（领主殿肉搏） | 城墙破后进殿 | `OpenSiegeLordsHallFightMission(scene,troops)` | 领主殿场景 |
| 比武大会 | 城镇报名 → 4 种赛事 | `OpenTournamentFight/Archery/HorseRace/JoustingMission(...)` | 该城 arena/马场场景 |
| 竞技场决斗 | 城内挑战/被挑衅 | `OpenArenaDuelMission(scene, arenaLocation, duelCharacter, ...)` | arena 地点场景 |
| 伏击（埋伏） | 村外埋伏战 | `OpenBattleMission`（场景 `ambush_scene_2`，`ambush_complex`）| 模板场景 |
| 退休 | 退休机制（`retirement_place` 菜单）| `retreat_complex` / `scn_retirement` | 模板场景 |
| 任务定制场景 | 各 Quest 的自定义 mission | 任务系统内部调 §1 API | 任务数据指定 |
| 棋盘游戏（酒馆小游戏） | `BoardGameCampaignBehavior` | **BoardGame 是一套独立 Mission**（`SandBox.BoardGames` + `MissionLogics`）| 棋盘场景 prefab |
| 战俘/越狱全套 | `menu_captivity_*` 菜单群 | **纯菜单 + UI，不开场景** | — |
| 突入/突围菜单 | `break_in_menu`/`break_out_menu` | 菜单 → 战斗 mission | — |
| **UI 屏幕（假场景）** | 市场/交易 `InventoryScreenHelper.OpenScreenAsTrade`、城镇管理 `MenuContext.OpenTownManagement`、招募 `OpenRecruitVolunteers`、铁匠/工坊面板 | **不开场景不开菜单，弹面板** | — |

> 🔴 **攻城场景与定居点数据强绑定**：`OpenSiegeMissionWithDeployment` 的第一个参数 = `settlement.LocationComplex.GetLocationWithId("center").GetSceneName(wallLevel)`。**定居点数据里的 `center` 场景名同时决定「日常进城」和「攻城战场」**——给 mod 做日本城时，`center` 配对场景，野战/围城自动对接。

---

## 4. 织丰侧（Modules/Shokuho）

### 4.1 总则：织丰没改跳转链

全量反编译 `Shokuho.dll`：无任何 `SettlementMenuManager/SwitchToMenu/MenuContext` 等 Harmony patch；其代码只是**调用方**：
- `GameMenu.ActivateGameMenu("town")`（任务剧情衔接）
- `PlayerEncounter.EnterSettlement()` + `LocationEncounter.CreateAndOpenMissionController(locationOfCharacter, locationOfCharacter2, targetHero.CharacterObject, null)` ——「从菜单找人 → 进对应地点对话」模式（shokuho.dll ~10400）

### 4.2 织丰自定义地点类型范式（制造新地点 = 四件套）

**以决斗地点为例（织丰标准姿势）**：

1. **模板** `duel_location_complex_templates.xml`：
   ```xml
   <LocationComplexTemplate id="duel_location_complex">
     <Location id="duel_location_id" name="..." scene_name="sho_duel_map_a" indoor="false" max_prosperity="500"
               ai_can_enter="CanNever" ai_can_exit="CanNever" player_can_enter="CanAlways" player_can_see="CanAlways"/>
     <Passages/>
   </LocationComplexTemplate>
   ```
2. **地点数据** `duel_location_settlements.xml`（织丰把决斗场绑到特定 settlement；港口 `port_location_settlements.xml`、寺庙 `temple_location_settlements.xml` 同构；另有 `meeting_scenes.xml` 定义会面场景 `meeting_castle_sho_a`）
3. **菜单**：自注册 `duel_location_menu`（+ `duel_location_post_menu`），`duel_location_menu_enter_on_consequence` 实现（shokuho.dll ~149637）：
   ```csharp
   PlayerEncounter.LocationEncounter = new CustomLocationsEncounter(Settlement.CurrentSettlement);   // 🔴 织丰自定义 Encounter 子类
   PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(
       Settlement.CurrentSettlement.LocationComplex.GetLocationWithId("duel_location_id"), null, null, null);
   ```
4. **场景**：`.sco`（`sho_duel_map_a` —— port/temple/postbattle 模板**共用了同一个决斗场景**占位，属数据完整性问题，非机制问题）。

同类织丰新增 complex：`port_location_complex`（`port_location_id`）、`temple_location_complex`（`temple_location_id`）、`postbattle_location_complex`（`postbattle_location_id`，战后事件场景）。

### 4.3 织丰自注册菜单（66 个 AddGameMenu，关键增量）

- **地点类新增**：`port`、`temple_location_menu`、`duel_location_menu/post_menu`、`postbattle_location_menu`、`take_ferry_selection`（渡轮选线）
- **会面类**：`request_meeting`、`request_meeting_with_besiegers`、`encounter_meeting`
- **征兵三件套**：`enlistment_main`、`enlistment_siege`、`enlistment_siege_aftermath`、`enlistment_mock_battle`
- **潜行/犯罪**：`menu_sneak_into_town_caught/succeeded`、`town_caught_by_guards`、`fortification_crime_rating`
- **战俘全套**：`menu_captivity_*`（castle_remain/taken_prisoner/end_*/transfer_to_town/escape…）
- **突防菜单**：`break_in_menu/debrief_menu`、`break_out_menu/debrief_menu`、`try_to_get_away/debrief`
- 行为覆盖链：`town`/`castle`/`village`/`encounter` 等原版 id 全部保留同名，织丰在其上加了自己的选项（如原版 `town` 菜单在织丰下仍有镇管理/市场，另加德川任务线入口之类——以运行时为准）

### 4.4 织丰数据替换总表（跳转目的地）

| 文件 | 内容 |
|---|---|
| `settlements.xml` | 全部 700+ settlement 换 `sho_*` 场景（`sho_town_a/sho_arena_b/sho_tavern_a/sho_keep_scene/sho_prison_b/sho_village_a/sho_castle_map_k` 等）；城镇/城堡/村庄/退休 complex 引用 |
| `location_complex_templates.xml` | 覆盖 template（+新增 `practice_arena`→`dojo_test` 训练场地点；**但 settlements.xml 内嵌 Locations 无一引用 practice_arena —— 模板地点是"打底"，未覆盖即生效，训练场实际存在**，属未验证点）|
| `port/temple/duel/postbattle_location_complex_templates.xml` | 织丰新增 4 个独立 complex（见 §4.2）|
| `port/temple/duel_location_settlements.xml`、`meeting_scenes.xml` | 新地点类型绑定的 settlement/会面场景 |

---

## 5. 对 LWN / Taikou 的启示（可复用结论）

1. **造场景跳转永远三步**：数据（模板 + settlements.xml）→ 菜单（`AddGameMenu`/`AddGameMenuOption`）→ 调 §1 API 开场景。**禁止自己写"开场景"逻辑**。
2. **新地点类型四件套 = 织丰范式**：complex 模板（新 Location id）→ settlement 绑定 → 菜单 + 自定义 Encounter 子类 → `.sco` 场景。
3. **攻城自动吃 `center` 场景**：Taikou 的城只需把 `center` 场景做好，围城/领主殿（lordshall 场景）自动接入。
4. **模板是唯一权威**：地点 `indoor`/`ai_can_enter`/`player_can_enter`/`Passages` 只认模板；settlements.xml 只覆盖 scene 名与数量上限。换属性 = 改模板、重跑生成。
5. **菜单 id 是引擎契约**：`town`/`town_outside`/`town_keep`/`town_backstreet`/`town_keep_dungeon`/`castle_dungeon`/`village` 等不改不改名——其他 mod/任务系统按 id 引用，改名即断链。
6. **一个易踩坑**：「兵营训练场」原版**没有** location 级训练场景；织丰自己造的 `practice_arena`（dojo_test）实现了它——想仿照做「武馆/训练场」直接抄这套。

---

## 6. 版本差异（1.2.12 vs 1.5.2）

| 项 | 1.2.12（repo `Modules/1.2.12DLL` 验证）| 1.5.2（当前引擎）|
|---|---|---|
| 主线 API 家族（§1 的 1-6）| ✅ 全部存在（`OpenArenaDuelMission`/`OpenHideoutBattleMission`/`OpenSiegeMissionWithDeployment`/`OpenTournamentFightMission`/`CheckAndOpenNextLocation`）| ✅ 同构 |
| 菜单 id | 同款短 id（`town_backstreet`/`town_keep_dungeon` 均命中）| 同 |
| `port_menu`（渡口）| ❌ 无 | ✅（1.4+ 海运系统）|
| handler 注册风格 | 旧命名方法（`game_menu_town_menu_on_init` 等）+ attribute 已用 | attribute 化（`[GameMenuInitializationHandler("town")]` / `[GameMenuEventHandler("town","option_id",...)]`）|
| 织丰 1.2.12 机 | 「1.2.12 游玩库」版本使用同款机制（`CustomLocationsEncounter` 是织丰自定义类，不存在于原版）| — |

> 1.2.12 织丰机的具体 DLL 行为以该机实测为准；此处仅按仓库 1.2.12 参考 DLL 与 1.5.2 反编译的对比结论。

---

## 附：反编译锚点（复检用）

| 内容 | 位置 |
|---|---|
| `PlayerTownVisitCampaignBehavior` 菜单注册/选项 | `TaleWorlds.CampaignSystem.dll` 该类型（1.5.2 反编译 214620+ 行区）|
| `OpenMissionWithSettingPreviousLocation` | 同上 214812 |
| `Helpers.CheckAndOpenNextLocation` | 同上（`Helpers.cs` 207 行起）|
| `LocationEncounter` 继承体系（Town/Castle/Village/Hideout）| 同上 101641-101830 |
| settlement `<Locations>` 模板+覆盖读取 | 同上 166340-166380（`complex_template` 读取 + 内嵌覆盖 scene/max_prosperity）|
| `CampaignMission` 全家桶 | 同上 24100-24300 |
| 攻城触发 | 同上 97941（`OpenSiegeMissionWithDeployment(besiegedSettlement.LocationComplex.GetLocationWithId("center").GetSceneName(wallLevel), ...)`）|
| 战役 Mission 实现层 | `SandBox.dll` `CampaignMissionManager : ICampaignMissionManager`（3231+）|
| 织丰决斗/入口 | `Shokuho.dll` 149548-149650（菜单+`CustomLocationsEncounter`+开场景）、10400（找人进场景）|
| 织丰模板 XML | `Modules/Shokuho/ModuleData/location_complex_templates.xml`（45 行）及 port/temple/duel/postbattle 模板 |

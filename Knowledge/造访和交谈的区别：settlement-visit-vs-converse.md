# 定居点 NPC 交互：造访 vs 交谈

> 发现日期：2026-06-22
> 相关 DLL：`TaleWorlds.CampaignSystem.ViewModelCollection.dll`, `SandBox.View.dll`, `TaleWorlds.MountAndBlade.View.dll`

---

## 一、两个按钮的核心区别

在定居点界面选中 NPC 后，有两个交互按钮：

| 按钮 | 枚举值 | 核心 API | 效果 |
|------|--------|----------|------|
| **造访 (Visit)** | `MenuOverlayContextList.Conversation` | `PlayerEncounter.LocationEncounter.CreateAndOpenMissionController` | 加载 NPC 实际所在的定居点 3D 场景，传送到 NPC 身边，可自由走动 |
| **交谈 (Talk)** | `MenuOverlayContextList.QuickConversation` | `CampaignMapConversation.OpenConversation` → `OpenMapConversation` | 在 2D UI 上弹出对话窗口，背景用 Tableau 渲染 3D 角色（不可走动） |

### 代码位置

**`GameMenuOverlay.ExecuteTroopAction`**（`TaleWorlds.CampaignSystem.ViewModelCollection.dll`，约 48825 行）：

```csharp
// 造访 — 进入 NPC 真实所在的 3D 场景
case MenuOverlayContextList.Conversation:
    Location location = LocationComplex.Current.GetLocationOfCharacter(
        _contextMenuItem.Character.HeroObject);
    PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(
        location, null, _contextMenuItem.Character);
    break;

// 交谈 — 打开 2D 对话 overlay
case MenuOverlayContextList.QuickConversation:
    CampaignMapConversation.OpenConversation(
        new ConversationCharacterData(CharacterObject.PlayerCharacter, null, ...),
        new ConversationCharacterData(_contextMenuItem.Character, null, ...));
    break;
```

---

## 二、交谈的背景渲染机制：MapConversationTableau

### 不是 2D 图片，是 3D 场景渲染到纹理

`CampaignMapConversation.OpenConversation` 最终触发 `MapConversationTableau`（[SandBox.View.dll](SandBox.View.dll) 的 `SandBox.View.Map.MapConversationTableau` 类）。

### 场景：全局缓存复用

**`TableauCacheManager`**（`TaleWorlds.MountAndBlade.View.dll`）初始化时加载一次：

```csharp
Current._mapConversationScene = Scene.CreateNewScene(true, false, ...);
Current._mapConversationScene.SetName("MapConversationTableau");
Current._mapConversationScene.Read("scn_conversation_tableau", ...);  // 固定场景文件
Current._mapConversationScene.SetShadow(true);
```

- **所有 NPC 共用一个场景** `scn_conversation_tableau`
- NPC 模型在固定生成点 `player_infantry_spawn` 实例化
- 摄像机在固定机位 `player_infantry_to_infantry`
- 渲染目标：`TableauView.AddTableau("MapConvTableau", CharacterTableauContinuousRenderFunction, ...)`

### 关键：氛围（Atmosphere）按 NPC 实际环境动态切换

**`DefaultMapConversationDataProvider.GetAtmosphereNameFromData`**（[SandBox.View.dll](SandBox.View.dll) 第 11089-11121 行）：

```csharp
string IMapConversationDataProvider.GetAtmosphereNameFromData(MapConversationTableauData data)
{
    string timeOfDay = (data.TimeOfDay <= 3f || data.TimeOfDay >= 21f) ? "night"
        : (data.TimeOfDay > 8f && data.TimeOfDay < 16f) ? "noon" : "sunset";

    // 不在定居点 → 按地形切换
    if (data.Settlement == null || data.Settlement.IsHideout)
    {
        if (data.IsCurrentTerrainUnderSnow)
            return "conv_snow_" + timeOfDay + "_0";
        switch (data.ConversationTerrainType)
        {
            case Desert: return "conv_desert_" + timeOfDay + "_0";
            case Steppe: return "conv_steppe_" + timeOfDay + "_0";
            case Forest: return "conv_forest_" + timeOfDay + "_0";
            default:     return "conv_plains_" + timeOfDay + "_0";
        }
    }

    // 在定居点 → 按 NPC 所在城镇的真实文化 + 室内/室外切换
    string culture = data.Settlement.Culture.GetCultureCode().ToString().ToLower();

    if (data.IsInside)
        return "conv_" + culture + "_lordshall_0";        // 室内：empire/sturgia/aserai...
    else
        return "conv_" + culture + "_town_" + timeOfDay + "_0"; // 室外：文化+时段
}
```

切换方式：

```csharp
_tableauScene.SetAtmosphereWithName(atmosphereName);
var entities = _tableauScene.FindEntitiesWithTag(atmosphereName);
foreach (var entity in entities)
    entity.SetVisibilityExcludeParents(true);  // 只显示匹配的，其余隐藏
```

### 氛围变体命名规则

`scn_conversation_tableau` 场景预制了所有变体实体，按 tag 区分：

- `conv_{culture}_lordshall_0` — 各文化领主大厅室内（empire, sturgia, aserai, khuzait, vlandia, battania）
- `conv_{culture}_town_{timeOfDay}_0` — 各文化城镇室外 + 时段（noon/night/sunset）
- `conv_{terrain}_{timeOfDay}_0` — 各地形野外 + 时段（plains/desert/steppe/forest/snow）
- `raining_entity` / `snowing_entity` — 天气效果

---

## 三、核心轮子：单场景多皮肤 — Atmosphere + Entity Tag 可见性切换

### 模式总结

**一个 `.xscene` 场景文件 = 所有视觉变体的容器。** 每种变体对应场景中一组打了特定 tag 的 `GameEntity`，运行时通过 `SetAtmosphereWithName` + `SetVisibilityExcludeParents` 只显示匹配的那组，其余全部隐藏。

```
                      scn_conversation_tableau
                    ┌─────────────────────────────┐
                    │  [conv_empire_lordshall_0]  │──► 帝国领主大厅布景
                    │  [conv_sturgia_lordshall_0] │──► 斯特吉亚领主大厅布景
                    │  [conv_aserai_town_noon_0]  │──► 阿塞莱城镇·正午布景
                    │  [conv_desert_night_0]      │──► 沙漠·夜晚布景
          加载一次  │  [conv_forest_sunset_0]     │──► 森林·黄昏布景       运行时
         ──────────►│  [player_infantry_spawn]    │──► 角色生成点      ──────────►
                    │  [player_infantry_to_...]   │──► 摄像机机位        只显示一组
                    │  [raining_entity]           │──► 雨效
                    │  [snowing_entity]           │──► 雪效
                    │  ...更多变体...             │
                    └─────────────────────────────┘
```

### 三步调用范式

```csharp
// ① 根据上下文拼出氛围名
string atmosphereName = GetAtmosphereNameFromData(data);
// 结果示例: "conv_empire_town_noon_0" / "conv_desert_night_0" / "conv_sturgia_lordshall_0"

// ② 设置场景氛围（可能影响光照、天空球、后处理等全局参数）
scene.SetAtmosphereWithName(atmosphereName);

// ③ 遍历场景中所有打此 tag 的实体 → 显示；其余实体默认隐藏或手动隐藏
var entities = scene.FindEntitiesWithTag(atmosphereName);
foreach (var entity in entities)
    entity.SetVisibilityExcludeParents(true);
```

### Atmosphere 名决定逻辑（完整决策树）

来自 `DefaultMapConversationDataProvider.GetAtmosphereNameFromData`：

```
输入: MapConversationTableauData
  ├── 时段 → timeOfDay: "noon" | "night" | "sunset"
  │
  ├── 不在定居点? (data.Settlement == null || IsHideout)
  │   ├── 雪地? → "conv_snow_{timeOfDay}_0"
  │   └── 地形: Desert → "conv_desert_{timeOfDay}_0"
  │             Steppe → "conv_steppe_{timeOfDay}_0"
  │             Forest → "conv_forest_{timeOfDay}_0"
  │             其他    → "conv_plains_{timeOfDay}_0"
  │
  └── 在定居点? (data.Settlement != null)
      ├── culture = Settlement.Culture.GetCultureCode()  // empire/sturgia/aserai/khuzait/vlandia/battania
      ├── 室内? → "conv_{culture}_lordshall_0"
      └── 室外? → "conv_{culture}_town_{timeOfDay}_0"
```

### 全量变体枚举

**时段 (timeOfDay) 取值：**

| 游戏时间 | timeOfDay 值 | 含义 |
|----------|-------------|------|
| 21:00 – 3:00 | `night` | 夜晚 |
| 3:00 – 8:00 | `sunset` | 黎明 |
| 8:00 – 16:00 | `noon` | 白昼 |
| 16:00 – 21:00 | `sunset` | 黄昏 |

> 注意：`sunset` 覆盖黎明和黄昏两个时段，没有单独的 `sunrise`。

**文化 (CultureCode) 枚举**（来自 `TaleWorlds.Core.dll`）：

```csharp
enum CultureCode {
    Invalid = -1,  Empire,  Sturgia,  Aserai,  Vlandia,
    Khuzait,  Battania,  Nord,  Darshi,  Vakken,  AnyOtherCulture
}
```

> 实际游戏中 `scn_conversation_tableau` 场景内只预制了 6 个主要文化的实体（Empire/Sturgia/Aserai/Vlandia/Khuzait/Battania）。Nord/Darshi/Vakken 在代码逻辑上会生成 atmosphere 名，但场景中未必有对应实体。

**地形 (TerrainType) 取值**（来自代码 switch 分支）：

| 枚举值 | 地形 | 对应 atmosphere |
|--------|------|----------------|
| `Desert` (5) | 沙漠 | `conv_desert_` |
| `Steppe` (3) | 草原 | `conv_steppe_` |
| `Forest` (10) | 森林 | `conv_forest_` |
| `Snow` (特殊路径) | 雪地 | `conv_snow_` |
| 其他 | 平原（默认） | `conv_plains_` |

---

#### A. 定居点室内 — `conv_{culture}_lordshall_0`（6 个）

```
conv_empire_lordshall_0
conv_sturgia_lordshall_0
conv_aserai_lordshall_0
conv_vlandia_lordshall_0
conv_khuzait_lordshall_0
conv_battania_lordshall_0
```

#### B. 定居点室外 — `conv_{culture}_town_{timeOfDay}_0`（6 × 3 = 18 个）

```
conv_empire_town_noon_0      conv_empire_town_night_0      conv_empire_town_sunset_0
conv_sturgia_town_noon_0     conv_sturgia_town_night_0     conv_sturgia_town_sunset_0
conv_aserai_town_noon_0      conv_aserai_town_night_0      conv_aserai_town_sunset_0
conv_vlandia_town_noon_0     conv_vlandia_town_night_0     conv_vlandia_town_sunset_0
conv_khuzait_town_noon_0     conv_khuzait_town_night_0     conv_khuzait_town_sunset_0
conv_battania_town_noon_0    conv_battania_town_night_0    conv_battania_town_sunset_0
```

#### C. 野外（无定居点）— `conv_{terrain}_{timeOfDay}_0`（5 × 3 = 15 个）

```
conv_plains_noon_0           conv_plains_night_0           conv_plains_sunset_0
conv_desert_noon_0           conv_desert_night_0           conv_desert_sunset_0
conv_steppe_noon_0           conv_steppe_night_0           conv_steppe_sunset_0
conv_forest_noon_0           conv_forest_night_0           conv_forest_sunset_0
conv_snow_noon_0             conv_snow_night_0             conv_snow_sunset_0
```

#### D. 天气叠加实体（独立 toggled，非 atmosphere 名）

```
raining_entity     — 雨效（根据 data.IsRaining 控制显隐）
snowing_entity     — 雪效（根据 data.IsSnowing 控制显隐）
```

#### E. 固定功能实体（始终存在，非 atmosphere 名）

```
player_infantry_spawn              — 对话 NPC 生成点
player_infantry_to_infantry        — 摄像机机位
player_bodyguard_infantry_spawn    — 护卫生成点（0..N，按 memberRoster 人数动态生成）
```

---

**总计：6 + 18 + 15 = 39 个 atmosphere 变体**，全部存在于同一个 `scn_conversation_tableau` 场景文件中。

### 这套模式的核心价值

| 特性 | 说明 |
|------|------|
| **零加载时间切换** | 所有变体在同一场景内，切换只是改 entity visibility，不涉及 `Scene.Read()` / 资源加载 |
| **美术制作友好** | 场景师在一个 `.xscene` 里摆放所有变体，打 tag 即可，无需维护多个场景文件 |
| **扩展简单** | 新增一种文化/地形，只需在场景里加一组 entity + 新 tag，代码里加一个 case |
| **全局缓存复用** | 场景只 `Scene.Read()` 一次，存 `TableauCacheManager`，所有对话实例共享 |

### 可复用的场景

任何需要"同一个功能框架 + 多种视觉风格"的地方都可以套这个模式：
- 对话背景（本案例）
- 角色创建/捏脸背景
- 物品展示/背包界面背景
- 任何 Tableau 类 UI 的背景
- 甚至全屏 Mission 也可以用（`SetAtmosphereWithName` 不是 Tableau 专属）

---

## 四、造访 vs 交谈：完整对比

| 维度 | 造访 (Visit) | 交谈 (Quick Talk) |
|------|-------------|-------------------|
| **场景** | NPC 实际所在位置的完整 3D 场景 | 全局缓存 `scn_conversation_tableau`，氛围动态匹配 |
| **场景加载** | 每次进入新建，退出销毁 | 全局唯一，缓存复用 |
| **玩家控制** | WASD 移动，自由旋转视角 | 不能移动，只是 2D UI 的背景 |
| **NPC 位置** | 场景中实际注册的 Location 坐标 | 固定生成点 `player_infantry_spawn` |
| **沉浸感** | 高（真·面对面） | 中（视频通话感） |
| **加载耗时** | 长 | 短 |
| **适用场景** | 想逛街、触发场景事件 | 快速交任务/买卖/对话 |

---

## 五、关键类与文件索引

| 类/方法 | 所在 DLL | 作用 |
|---------|---------|------|
| `GameMenuOverlay.ExecuteTroopAction` | `TaleWorlds.CampaignSystem.ViewModelCollection.dll` | 分发造访/交谈两个 case |
| `SettlementMenuOverlayVM` | 同上 | 定居点 overlay，构建 `_overlayTalkItem` 和 `_overlayQuickTalkItem` |
| `CampaignMapConversation.OpenConversation` | `TaleWorlds.CampaignSystem.dll` | 交谈入口，调用 `OpenMapConversation` |
| `MapConversationTableau` | `SandBox.View.dll` | 交谈背景的 3D→纹理渲染器 |
| `TableauCacheManager` | `TaleWorlds.MountAndBlade.View.dll` | 缓存 `scn_conversation_tableau` 场景 |
| `DefaultMapConversationDataProvider` | `SandBox.View.dll` | 根据 Settlement.Culture / 地形 / 时间 决定氛围名 |
| `PlayerTownVisitCampaignBehavior` | `TaleWorlds.CampaignSystem.dll` | 城镇菜单（town_keep, town_streets 等），最终都走 `CreateAndOpenMissionController` |
| `TownEncounter.CreateAndOpenMissionController` | `TaleWorlds.CampaignSystem.dll` | 造访时根据 location ID 分发子场景（center→OpenTownCenterMission, lordshall→OpenIndoorMission 等） |

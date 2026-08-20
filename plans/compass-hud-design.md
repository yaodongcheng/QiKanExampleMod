# 顶部罗盘（老滚5 风格）设计 — Compass HUD

> 状态：设计定稿，待实现。日期：2026-08-20。版本兼容：v1.4.8（开发机；API 均经二进制 grep + ilspycmd 反编译验证）。
> 🔴 **一期范围（用户裁定 2026-08-20）：只实现 Mission 内的罗盘。大地图指北针 = 二期，文档保留不实现。**

## Context

玩家在场景（Mission）里转视角时缺少方位感：不知道北在哪、不知道带任务的重要人物在哪个方向、距离多远。

目标：屏幕顶部加老滚5 风格罗盘——Mission 形态（刻度带 + 视野内重要人物图标带距离），全部走现有 UI 轮子（V.NewLayer / MissionView / MCM 四件套 / LWNTextHelper）。

## 用户裁定（2026-08-20）

1. **一期只做 Mission 内罗盘**；大地图指北针徽标（见文末「二期」）不在本期
2. **Mission 图标只显示当前方向（视野）范围内的**（±90° 带范围外不显示、不贴边），**图标附距离文本（xx米）**——不做老滚5 的"身后贴边 clamp"
3. 方向字母**跟随语言本地化**（英文 N/E/S/W / 中文 北/东/南/西，LWNTextHelper）

## 设计总览（一期）

**Mission 完整罗盘带**（顶部居中横带，宽 ~1000 高 ~52）：
- N/E/S/W 字母 + 45° 刻度线随相机 yaw 滚动；中心固定金色指针
- 视野内重要人物图标（原版金色 ! 图标）+ 距离文本
- 图标数据源（与原版 Alt 标记同一判定）：`CampaignUIHelper.GetQuestStateOfHero(hero)` 非空 → `QuestMarkerVM`（金色 !，IconType 由 IssueQuestFlags 决定）。遍历 `Mission.Current.Agents` 过滤 `IsActive() && Character.IsHero && != Agent.Main`；最多 8 个按距离近优先，防挤

## 引擎 API 事实（v1.4.8 反编译验证，探索代理 2026-08-20）

```csharp
// Mission yaw（或直接读 MissionScreen.CameraBearing，public float）
float yaw = (-Mission.Current.GetCameraFrame().rotation.u).RotationZ;   // Atan2(-x, y)，0°=+Y
// 目标方位角（Vec3 水平投影后）
float targetBearing = (targetPos2D - camPos2D).RotationInRadians;        // 同约定
float relAngle = NormalizeAngleDeg(targetBearing - yaw);                 // [-180,180]
// 图标 x（带坐标系，半带 = 90°）
float x = halfBand + relAngle * (halfBand / 90f);   // |relAngle|>90 → 隐藏（用户裁定）
```

**确认存在**：`Mission.GetCameraFrame()`、`MissionScreen.CombatCamera/CameraBearing/SceneLayer`、`MBWindowManager.WorldToScreen`（P1 增强用）、`ScreenBase.AddLayer(ScreenLayer)` public、`MissionView.OnMissionScreenInitialize/OnMissionTick/OnMissionScreenFinalize`。

**确认不存在**（别浪费时间找）：`MissionCameraManager`、`Campaign.CameraPosition/CameraFacing`、`AltMarker`、`WorldMarker`、`NamePlateLayer`、`MissionBehavior.OnMissionScreenInitialize`（那是 MissionView 的）。

**原版 Alt 标记系统（可完整抄）**：`SandBox.View.Missions.NameMarkers.DefaultMissionNameMarkerHandler`（Provider，过滤 `agent.Character.IsHero`）→ `MissionAgentMarkerTargetVM.UpdateQuestStatus()` 用 `CampaignUIHelper.GetQuestStateOfHero(h)` → `QuestMarkerVM`；UI 层 = `MissionGauntletNameMarkerView`（`GauntletLayer("MissionNameMarker", 1, false)` + movie "NameMarker" + `IsGameKeyDown(5)` 显隐）；图标 = `QuestMarkerBrushWidget QuestMarkerType`；prefab = `Native/GUI/Prefabs/Mission/NameMarker.xml`（ListView + ItemTemplate + `Position="@ScreenPosition"`）。

## 文件清单

### 新建 `ExampleModVS/ExampleMod/ExampleMod/Compass/` 域

| 文件 | 职责 |
|---|---|
| `Compass/CompassMissionView.cs` | `MissionView`：层生命周期（照抄 `AgentHUD/AgentHudMissionView.cs` 完整模式——OnMissionScreenInitialize 建层 order **8** + LoadMovie("Compass") + AddLayer；OnMissionTick 扫描/刷新；OnMissionScreenFinalize 移除）；IM 打开（`ImChatView.IsOpen`）或 `ModInput.IsSystemModalActive()` → 隐藏 |
| `Compass/CompassVM.cs` | 罗盘 VM：刻度列表（4 字母 + 8 刻度线位置注入）+ `MBBindingList<CompassIconVM>` + 中心指针；VM↔XML 同步铁律（ui.md） |
| `Compass/CompassIconVM.cs` | 图标条目：`PosX`/`PosY`/`IsVisible`/`DistanceText`（`{DIST}m` 本地化）/`IconType`（int → QuestMarkerBrushWidget） |
| `GUI/Prefabs/Compass.xml` | 根 `DoNotAcceptEvents="true"`（纯显示不吃事件）；底带 `BlankWhiteSquare_9` `#00000066`；图标列表照抄原版 NameMarker.xml 的 ListView+ItemTemplate+Position 绑定；字母/刻度线绑 `PosX`+`IsVisible` |

### 修改文件

| 文件 | 改动 |
|---|---|
| `Core/MySubModule.cs` | `OnMissionBehaviorInitialize` 加 `mission.AddMissionBehavior(new CompassMissionView())`（:86-125 列表加一行） |
| `Core/Settings.cs` | `[JsonIgnore] public bool ShowCompass { get; set; } = true;`（MCM 唯一来源）+ `ShowCompassIcons`（图标独立开关，默认 true） |
| `Core/MCMSettings.cs` | 两个 `[SettingPropertyBool]` 透传（照抄 `ShowAgentHealthBar` 模板 :161-168，key `{=LWN_mcm_show_compass}` / `{=LWN_mcm_show_compass_icons}`） |
| `ModuleData/Languages/std_LivingWorldNpcs_strings.xml` + `CNs/` 同名 | key：`LWN_compass_north/east/south/west`、`LWN_compass_dist`（`{DIST}m`）、2 个 MCM key |

## 关键实现细节

1. **挂载与驱动**：`CompassMissionView`（注册进 MySubModule）——与 AgentHud 完全同构，生命周期由 Mission 托管，无需重挂逻辑。层序 **8**（AgentHud=5 之上、Interaction=10 之下、IM=400 与系统菜单 4400 之下 → 菜单/对话自动盖住罗盘）。
2. **刻度滚动**：刻度线 8 根（45° 间隔）+ 4 字母（90° 间隔）全部位置注入（VM 算 `x` 每帧 set，AgentHudVM 同款 PosX 注入模式）；窗口 ±100° 显示、超出隐藏。中心指针固定不动。
3. **图标**：relAngle ∈ [-90, 90] 才显示（用户裁定：范围外不显示不贴边）；距离 = `agent.Position.Distance(cameraPos)` 取整。扫描分频：agents 过滤每 30 帧、位置/距离每 2 帧。UI scale 换算照抄 `AgentHudMissionView`（`_layer.UIContext.Scale` + `invUiScale`）。
4. **本地化**：字母/距离全走 `LWNTextHelper.Resolve`（铁律 13：禁止裸中文字面量）。
5. **颜色纪律（ui.md）**：全部 `#RRGGBBAA` 9 字符；绑 `Color` 的 string 声明处初始化合法色串；距离文字初始值非 null（防绑定崩溃）。

## 复用轮子清单（不重复造）

- `V.NewLayer(order, name)` + `V.LoadMov`（`Core/VersionCompat.cs:396,409`）
- `AgentHudMissionView` 的 MissionView 层生命周期 + 位置注入 + UI scale 换算 + 分频模式
- 原版 `QuestMarkerVM` + `QuestMarkerBrushWidget`（与原版 Alt 完全一致的图标样式）
- 原版 NameMarker.xml 的 ListView+ItemTemplate+Position 绑定结构
- MCM 开关四件套（Settings + MCMSettings + 双语言 XML）
- `LWNTextHelper.Resolve` 本地化

## 实施步骤

1. `Core/Settings.cs` + `Core/MCMSettings.cs` + 双语言 XML：`ShowCompass`/`ShowCompassIcons` 四件套 + 罗盘语言 key
2. `Compass/CompassIconVM.cs` + `Compass/CompassVM.cs` + `GUI/Prefabs/Compass.xml`（先静态刻度 XML 再位置注入）
3. `Compass/CompassMissionView.cs` + `Core/MySubModule.cs` 注册
4. 控制台指令 `custom.compass_debug`（打印 yaw/relAngle/图标数/距离），配合实测

## 验证方案

- **编译**：`dotnet build -c Debug`（语法验证；正式测试按项目约定用 VS2022 手动编译）
- **控制台验证**：`custom.compass_debug` 打印当前 yaw（朝北=0°）、图标列表
- **Mission 实测清单**：①转视角 N/E/S/W 字母与真实方位一致（朝北时 N 在中心）②带 issue 的 NPC 图标出现在罗盘对应方位、距离文本正确 ③转身 90°+ 图标从边缘滑出消失 ④视野外/身后 NPC 不显示 ⑤中心指针固定 ⑥IM 打开罗盘隐藏、关闭恢复 ⑦系统菜单打开罗盘被盖 ⑧MCM 两开关生效
- **回归**：IM、AgentHud、交互层（10-16）显示不受影响（层序无冲突）

## 风险 / 待实测点

1. 图标距离文本的视觉密度（8 个图标 + 距离字在带内是否挤）→ 实测后调半带宽度/字体
2. `QuestMarkerBrushWidget` 在原版 NameMarker.xml 之外的 prefab 里能否正常引用原版 brush（跨模块 brush 引用照抄原版写法）→ 实现时验证

## 二期（本期不实现，文档保留）

**大地图指北针徽标**（用户裁定：俯视视角无"前方"概念，不用完整罗盘带；只需分清地图是否旋转；任务地点已有原版名标+quest 标记）：
- 形态：顶部居中，宽 ~120 高 ~48，箭头指向地图北方（随相机旋转）+ 方位文字（北/东北/…8 方位）
- 相机 yaw：`(-MapScreen.Instance.MapCameraView.Camera.Frame.rotation.u).RotationZ`（`MapCameraView.CameraBearing` 是 protected；`Campaign.CameraFacing` 不存在）
- 挂载：静态控制器 + `ScreenBase.OnFrameTick` Postfix（照抄 `ImChat/ImScreenFrameTickPatch.cs`，暂停也跑）或 MapView 子类 `CreateLayout()`；层序 91（MapNameplate=90 之上、MapMenu=100 之下）
- 箭头旋转：优先 `RotatedImageWidget`（实现时二进制 grep "RotatedImage" 验证）；不存在则退化「固定 N + 方位文字」
- 文件预留：`Compass/CompassMapVM.cs`、`GUI/Prefabs/CompassMap.xml`

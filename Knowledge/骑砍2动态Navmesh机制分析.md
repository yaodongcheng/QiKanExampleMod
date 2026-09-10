# 骑砍2 动态 Navmesh 机制分析

> 来源：**1.2.12 DLL 反编译实证**（2026-09-10）。行号缩写对照：
> `mb:` = TaleWorlds.MountAndBlade.dll ｜ `engine:` = TaleWorlds.Engine.dll ｜ `lib:` = TaleWorlds.Library.dll ｜ `sandbox:` = SandBox.dll
> 复现方式：`ilspycmd Modules/1.2.12DLL/<DLL> > 文件` 后按行号查（见 CLAUDE.md「API 探索」章节流程）。
> 关联：本 mod 调试工具 `CampaignMode/Tools/NavMeshDebugCommands.cs` + `CampaignMode/Tools/NavMeshDebugMissionView.cs`；大地图侧探针 `CampaignMode/Tools/NavMeshProbeCommands.cs`。

---

## 一、总览：navmesh 不做运行时重建，只做"面组开关"

**核心结论（三条）**：

1. **所有可能状态的路，在场景烘焙期就全部存在于 navmesh 里**——城墙上的行走面、梯子面、门洞面、壕沟桥面、攻城塔内部面……各分配一个 **NavMeshId 组**，初始大多处于禁用状态。
2. **运行时只有"整组使能/禁用"**：`Scene.SetAbilityOfFacesWithId(id, enabled)`。禁用 = 寻路视为不存在，AI 自动绕开。**没有**"运行时给新网格烤 navmesh"的 C# API。
3. **动态物体要造路，走"导航件"通道**：`Scene.ImportNavigationMeshPrefab(导航件名, idShift)` 运行时导入预制的 navmesh 面组，配合 `GameEntity.AttachNavigationMeshFaces(id, isConnected, isBlocker)` 挂到实体上——native 设计内能力（攻城塔/攻城槌就是这么干的）。

> 工程含义：**"空地模板 + 运行时摆城 + AI 正常寻路"可行**——但走路面必须是预制的导航件（或模板里预烘的默认路面），不是随意动态 mesh。

---

## 二、面组 ID 体系（原生语义常量）

| 常量 | 值 | 出处 | 语义 |
|---|---|---|---|
| `TeamAISiegeComponent.InsideCastleNavMeshID` | 1 | mb:44450 | 城内 / 墙上（`faceId % 10 == 1` 判定"在城里"，见 mb:17180、20522） |
| `GateNavMeshId`（移动攻城武器字段） | 7 | mb:106951 | 门（该武器可过的门面） |
| `DisabledNavMeshID` | 8 | mb:106953 | 默认禁用组（武器移动时整组开关） |
| `_ditchNavMeshID_1 / _2` | 9 / 10 | mb:106959-106961 | 壕沟面 |
| `_groundToBridgeNavMeshID_1 / _2` | 12 / 13 | mb:106963-106965 | 地面→桥面坡道 |
| `SiegeLadder.OverTheWallNavMeshID` | 13 | mb:113384 | 梯子跨越城墙的面 |
| `Mission.GetNextDynamicNavMeshIdStart()` | 运行时分配 | mb:52525 | 动态对象领新 id 段（从动态区起分配） |
| `MaxNavMeshPerDynamicObject` | 10 | mb:71479 | 每个动态对象最多占 10 个 id（一组动态槽位） |

**面组 ID 与"面序号"是两个东西**：`PathFaceRecord.FaceIndex` = 面序号（遍历用），`Scene.GetIdOfNavMeshFace(faceIndex)` = 该面所属**面组 ID**（开关语义）。`PathFaceRecord` 定义（lib:8142）：`FaceIndex / FaceGroupIndex / FaceIslandIndex`，`IsValid() = FaceIndex != -1`。

---

## 三、运行时开关：读写 API 全清单

### 写侧（唯一的开关入口）

```csharp
scene.SetAbilityOfFacesWithId(int navMeshId, bool isEnabled)   // 整组开关，本分析的核心 API
```

### 读侧（Scene 类，engine:11474 区）

| API | 出处 | 用途 |
|---|---|---|
| `GetNavMeshFaceIndex(ref PathFaceRecord, Vec2 pos, bool checkIfDisabled, bool ignoreHeight=false)` | engine:11474 | 坐标→面；**`checkIfDisabled=true` 时查不到 = 该面被禁用** |
| `GetNavMeshFaceIndex(ref PathFaceRecord, Vec3 pos, bool checkIfDisabled)` | engine:11479 | 同上（3D） |
| `GetNavMeshFaceCount()` | engine:11561 | 面总数（遍历面用） |
| `GetIdOfNavMeshFace(int faceIndex)` | engine:11592 | 面→面组 ID |
| `GetNavMeshCenterPosition(int faceIndex, ref Vec3)` | engine:11602 | 面中心（可视化画点） |
| `GetNavMeshFaceFirstVertexZ(int faceIndex)` | engine:11654 | 面高度（把 Vec2 路径点补成 3D） |
| `DoesPathExistBetweenFaces(int a, int b, bool ignoreDisabled)` | engine:11944 | 连通性判定 |
| `GetPathBetweenAIFaces(int startFace, int endFace, Vec2 s, Vec2 e, float agentRadius, NavigationPath path, int[] excludedFaceIds=null, float extraCostMultiplier=1f)` | engine:11457 | **寻路**；结果写进 `path`，`path.Size` = 点数，`path.PathPoints` = `Vec2[128]` |
| `GetPathBetweenAIFaces(UIntPtr, UIntPtr, ...)`（native 指针重载） | engine:11445 | 同上 |
| `GetNavigationMeshCRC()`（1.5.1 新增） | 1.5.1 engine | navmesh 数据校验 |

**Agent 侧**（mb）：

- `Agent.GetCurrentNavigationFaceId()`（mb:12810）——agent 当前踩的面组 ID；`% 10 == 1` = 在城内（原生惯用判定）。
- `Agent.CheckPathToAITargetAgentPassesThroughNavigationFaceIdFromDirection(faceId, ref dir, overriddenCost)`（mb:13440）——**带成本覆盖**的路径询问：调低某 face 成本 = 引导 AI 走它（梯子导流原理）。
- `Agent.HasPathThroughNavigationFaceIdFromDirection / HasPathThroughNavigationFacesIDFromDirection`（mb:14074 / 14853）——路径是否经过指定面。

### 效果联动

被禁用的面对上层系统是"不存在"：
- 站位点失效：`Chair`/`Passage` 的 StandingPoint 用 `GetNavMeshFaceIndex(checkIfDisabled: true)` 自检，查不到就把 `IsDeactivated = true`（sandbox:11947-11960）。
- 寻路绕行 / 无路径返回 false。
- 编辑器工具 `Remove Unreachable Faces` 也是同一判定的离线版。

---

## 四、动态导航件管线（MissionObject 内置机制）

`MissionObject`（所有可交互场景道具基类）自带完整动态 navmesh 挂接（mb:71478 区）：

```csharp
public abstract class MissionObject : ScriptComponentBehavior
{
    protected enum DynamicNavmeshLocalIds
    {
        Inside = 1, Enter, Exit, Blocker, Extra1, Extra2, Extra3, Reserved1, Reserved2  // 一"可进入结构"的槽位语义
    }

    [EditableScriptComponentVariable(true)]
    protected string NavMeshPrefabName = "";     // 非空 = 该对象带动态导航件
    protected int DynamicNavmeshIdStart;         // 运行时领到的 id 段起点

    protected virtual void AttachDynamicNavmeshToEntity()
    {
        if (NavMeshPrefabName.Length > 0)
        {
            DynamicNavmeshIdStart = Mission.Current.GetNextDynamicNavMeshIdStart();
            GameEntity.Scene.ImportNavigationMeshPrefab(NavMeshPrefabName, DynamicNavmeshIdStart);   // 运行时导入导航件
            GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 1, isConnected: false);                    // Inside
            GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 2, isConnected: true);                     // Enter
            GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 3, isConnected: true);                     // Exit
            GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 4, isConnected: false, isBlocker: true);    // Blocker
            SetAbilityOfFaces(GameEntity != null && GameEntity.GetPhysicsState());      // 物理还在 → 面才亮
        }
    }
}
```

要点：
- **导航件 = 编辑器里导出的面预制**（`Scene.ImportNavigationMeshPrefab(name, idShift)`，engine:12214 → native `LoadNavMeshPrefab`，engine:3685）。
- `AttachNavigationMeshFaces(id, isConnected, isBlocker)`：`isConnected` = 与主 navmesh 连通（可走通）；`isBlocker` = **阻挡面**（AI 不可穿 → "移动物体挡住原路"的实现单元）。
- 类内另有 `SetAbilityOfFacesWithId(DynamicNavmeshIdStart + 1..7, enabled)` 的批量开关（mb:71504-71510），与扩展槽位（Extra/Reserved）对应。
- 武器被打坏 → `SetAbilityOfFaces(physicsState=false)` 整组关掉（物理没了面也别用）。
- **导航件制作侧**：编辑器 NavMesh Inspector 的 `Export Faces as Prefab` / `Import Faces From Prefabs`（见第七节）。

---

## 五、AI 导流：不是只有"能走/不能走"

- **成本覆盖**：`CheckPathToAITargetAgentPassesThroughNavigationFaceIdFromDirection(faceId, dir, overriddenCost)`（mb:13440）——AI 规划时对指定面用覆盖成本，调低 = 引导走（例：让步兵优先爬梯子）。
- **困难面集合**：`TeamAISiegeComponent.DifficultNavmeshIDs`（mb:44490）——攻城塔的"难走面"（`CollectGetDifficultNavmeshIDsForAttackers/ForDefenders`，mb:44440 / 44801），AI 尽量避开（等价于给这些面加高成本）。
- **主攻城武器面**：`PrimarySiegeWeaponNavMeshFaceIDs` + `IsPrimarySiegeWeaponNavmeshFaceId(id)`（mb:44649）——判定"这个面是某主力攻城武器的"，用于 AI 行为分支（如 mb:17184 骑乘/推挤判定、35295 攻城方/守方分流）。
- **可开关面**：`checkIfDisabled` 全链路贯通（读 api、站位点、寻路）。

---

## 六、原生实例解剖

| 实例 | 机制 | 出处 |
|---|---|---|
| **云梯（SiegeLadder）** | `OnWallNavMeshId`（spawner 分配，mb:113382）；`OverTheWallNavMeshID => 13`；`AssignParametersFromSpawner(sideTag, targetWallSegment, onWallNavMeshId, downStateRotation, upperStateRotation, barrierTagToRemove, indestructibleMerlonsTag)`（mb:114467）；状态机 `LadderState`（mb:113203）；专用 AI `SiegeLadderAI`（mb:34497，含 `ladder.WaitFrame` 排队爬） | mb:113163 区 |
| **移动攻城武器**（攻城槌/攻城塔族） | 字段：`GateNavMeshId=7`、`DisabledNavMeshID=8`、`_bridgeNavMeshID_1/2=8`、`_ditchNavMeshID_1/2=9/10`、`_groundToBridgeNavMeshID_1/2=12/13`、`NavMeshIdToDisableOnDestination=-1`（mb:106951-106967）；`AssignParametersFromSpawner(gateTag, sideTag, bridgeNavMeshID_1, bridgeNavMeshID_2, ditchNavMeshID_1, ditchNavMeshID_2, groundToBridgeNavMeshID_1, groundToBridgeNavMeshID_2, pathEntityName)`（mb:107576）；到位后 `SetAbilityOfFacesWithId(_bridgeNavMeshID_1/2, true)`（mb:107190-107191），离开/收回 `false`（mb:107201-107202）；`SetAbilityOfFacesWithId(DisabledNavMeshID, true/false)`（mb:107093 / 107278） | mb:106930 区 |
| **"阻挡原路"** | `NavMeshIdToDisableOnDestination`：武器**到达目的地后把某 id 整组禁用**（mb:106967 字段 + 使用处）——原路径面关掉，AI 不再往武器占位处走 | 同上 |
| **门** | `NavigationMeshIdToDisableOnOpen`（mb:107949）——门开时把某面组使能（`SetAbilityOfFacesWithId`，mb:108152/108219 区），门关再关掉 | mb:107949 区 |
| **通道 / 椅子（Passage / Chair）** | StandingPoint 与面状态联动（禁用面 → 站位点自动失效），sandbox:11947-11960 | sandbox |

---

## 七、调试与可视化

### 7.1 编辑器（ModKit，Ctrl+E）——官方唯一可视化工作台

NavMesh Inspector（官方文档：`Knowledge/bannerlord_official_docs/Editor/Scene Editor/nav_mesh_inspector.md`）：

| 工具 | 用途 |
|---|---|
| Auto Generate / Generate Grids | 按 Recast 参数生成 navmesh（Cell Size / Agent Radius / Max Climb / Slope 等参数可调） |
| Create New Face / Extrude / Fill / Connect / Subdivide / Weld | 手工编辑面 |
| Make Quads When Possible | 三角面并四边形（大幅减面数） |
| **X-Z Keys** | 放两个球 → 输出两点间**路径统计**（编辑器内看路径） |
| **Find Path** | 找路径 |
| **Export Faces as Prefab / Import Faces From Prefabs** | 🎯 **导航件的导出/导入**（`ImportNavigationMeshPrefab` 的编辑器侧对应物） |
| Mark Elevation Problem Faces | 悬空 >1.2m 的面标红（防"面在空中，AI 走不到"） |
| Remove Unreachable Faces / Select Unconnected Faces / Find Tight Faces | 清理孤立/断连/过碎面 |
| Select Vertices Below Entities / Select Inverted Faces / Ensure Faces Are Not Below Ground | 面质量检查 |

编辑器里还能进 mission 模式实跑（`MBEditor.EnterEditMissionMode`，mb:56908 区）。

### 7.2 游戏内正式版：**没有现成的 navmesh 显示开关**（实证）

- `MBDebug.IsDisplayingHighLevelAI`（engine:8963）= **假线索**：使用它的 `DebugTick`/`DebugMore` 全带 `[Conditional("DEBUG")]`（mb:24304 区 / 44101 区 / 123566 区），**正式发布 DLL 编译期整体剥离**；且原代码只是"取值占位"（`_ = standingPoint.GameEntity.GlobalPosition;`）给内部调试器断点用，不是渲染。
- Debug 热键体系存在（`Input.DebugInput.IsHotKeyPressed`），但全是内部攻城测试热键（`DebugSiegeBehaviorHotkey*`、`UsableMachineAiBaseHotkey*`、`SwapToEnemy`），**无 navmesh 相关**。
- 控制台命令表（`plans/native_commands.md`）与二进制命令注册扫描：**无 navmesh 命令**。
- native 层 navmesh 字符串只有数据名：`navmesh.bin`（场景同目录数据文件）、`NavMeshPrefabs`（导航件容器）——无调试 UI。
- 唯二"开关"：`SceneInitializationData.LoadNavMesh`（engine:11151，**加载**与否，非显示）；`MBDebug.ShowDebugInfoState`（性能信息屏，不含 navmesh）。

### 7.3 游戏内实时光显示 = 自建（本 mod 已实现）

引擎提供全部原语，缺的只是"用它们的人"：

- **绘制通道**：`MBDebug.RenderDebugLine / RenderDebugSphere / RenderDebugBoxObject / RenderDebugText3D / RenderDebugFrame / RenderDebugCapsule`（engine:9153 区，`time` 参数 = 持续秒数，每帧重画即实时）。
  🔴 **这些方法全部带 `[Conditional("_RGL_KEEP_ASSERTS")]`——csproj 的 DefineConstants 必须定义 `_RGL_KEEP_ASSERTS`，否则调用点被编译器静默剥离**（代码编译通过但画面什么都不显示）。本 mod 已在 `ExampleMod.csproj` Debug/Release 两配置中定义。
- **颜色打包** = `0xAARRGGBB`（默认 `uint.MaxValue` = 不透明白）。若实机色相异常（红蓝互换）改色表即可。

**本 mod 工具**（`CampaignMode/Tools/NavMeshDebug*.cs`，2026-09-10）——**双世界自动分派**：

> 骑砍2 的 Campaign 与 Mission 是两个宿主：场景有 Agent（无 MobileParty），大地图有 MobileParty（无 Agent）；两边是**同一套引擎 navmesh**（`MapSceneWrapper` 内部转调同一 `Scene` API，`SandBox.MapScene.Scene` 即大地图 scene），仅载体不同。
> 绘制触发：场景 = `NavMeshDebugMissionView`（MissionBehavior.OnMissionTick）；大地图 = `NavMeshDebugMapTickPatch`（Harmony 补丁 `ScreenBase.OnFrameTick`——暂停也触发，理由同 ImScreenFrameTickPatch）。
> 共享绘制核心 = `NavMeshDebugRenderer`（两宿主共用面球/路径/监视绘制）。

| 命令（游戏内 `~` 控制台） | 适用世界 | 功能 |
|---|---|---|
| `custom.nav_debug [on\|off\|reset]` | 两者 | 面球显示开关；`radius <米>`（默认 30）；`faces <n>` 每帧面上限（默认 400，分片轮转）；`text on\|off` 叠加面组 ID；**`reset` = 一键全关**（面球+路径+agent监视+party监视 + `ClearRenderObjects` 立即清屏） |
| `custom.nav_path x y` / `x1 y1 x2 y2` / `clear` | 两者 | 两点寻路折线（**每帧重算**）；两点模式起点 = 场景玩家 Agent / 大地图主队 |
| `custom.nav_face x y` | 两者 | 坐标→面详情（面序号/面组 ID/是否禁用/高度/岛）；场景与大地图共用 Scene API |
| `custom.nav_agent [<index>\|nearest\|list\|clear]` | 仅场景 | 监视 agent：白球 + 品红箭头（`GetTargetDirection`）+ 品红球（`GetTargetPosition`）+ 青线（脚下→目标 实时路径）；`list` 列 40m 内候选 |
| `custom.nav_party [<seq>\|nearest\|list\|clear]` | 仅大地图 | 监视 party：白球 + 名称 + 品红箭头（朝 `TargetPosition` 自算方向）+ 品红球 + 青线（`Position2D`→`TargetPosition` 实时路径）；`list` 列主队 + 60m 内候选 |

配色：面组 1 城内=蓝、7 门=金、8 默认禁用组=红、13 梯桥=绿、其余按 id 取调色板；**运行时被禁用的面画暗红**（动态开关效果肉眼可见）；监视对象专用色（白=本体 / 青=实时路径 / 品红=目标方向与目标点）。

> 🔴 agent/party 路线口径：内部路径在 native 不暴露 —— 青线画的是「脚下 → 当前目标」的实时寻路结果，同起终点下引擎寻路确定性，即"它接下来最可能走的路线"。
> 🔴 **实机结果（2026-09-10）：未显示** —— 游戏内执行命令后画面无任何调试图元。排查清单、已排除证据与备选通道见 **§7.4（归档，未排查完）**。

另有大地图侧探针 `custom.probe_face`（`CampaignMode/Tools/NavMeshProbeCommands.cs`，2026-09-09）——地图点击寻路三环验证（面有效/玩家面/同岛）。

### 7.4 实机归档（2026-09-10）：调试渲染未显示 —— 未排查完

**结果**：游戏内执行 `custom.nav_debug on` / `nav_path` / `nav_agent` 后，画面**无任何调试图元显示**。
（未区分场景 / 大地图，待下次定位——两世界绘制循环不同，排查先分清。）

**已排除（三项，证据充分）**：

| 项 | 证据 |
|---|---|
| 编译符号没被剥离 | `_RGL_KEEP_ASSERTS` 已定义；反编译编译产物确认 `MBDebug.RenderDebugSphere/Line/DirectionArrow`、`ClearRenderObjects` 调用真实存在于 IL |
| 命令注册 | 5 个命令（nav_debug/nav_path/nav_face/nav_agent/nav_party）反编译确认全部注册；返回/异常均有 try-catch |
| 触发链 | 场景 = MissionBehavior.OnMissionTick（与项目其他 MissionView 同款）；大地图 = ScreenBase.OnFrameTick 补丁（与 ImScreenFrameTickPatch 同款目标类） |

**待排查方向（按嫌疑排序）**：

1. **native 侧是否消费调试图元（最大嫌疑）** —— `MBDebug.RenderDebug*` → `EngineApplicationInterface.IDebug` → C++ 实现；怀疑 release 构建的游戏根本不渲染调试图元（TaleWorlds 内部开发模式才画）。验证法：二进制搜 `TaleWorlds.Native.dll` 的 debug 渲染开关串；查游戏启动参数 / rgl_config 开发模式项；对照 `MBDebug.IsTestMode()` 等开发态判定。
2. **`time` 参数语义** —— 实现假设"持续秒数（每帧重画 time=0.5s）"；若实际是延迟/帧数/其它 → 看不到或闪。验证法：最小实验用 time=10。
3. **坐标系 / 尺度** —— navmesh 的 Z 与渲染世界坐标可能有偏移（地图场景尤甚）→ 球画在地下；或 radius 0.25 太小。验证法：脚下画半径 3~5m 大球、抬高 2m。
4. **深度 / 剔除** —— 已设 depthCheck=false，但图元可能仍吃相机距离剔除。

**最小复现实验（下次第一步）**：

> `OnMissionTick` 里无条件画 `MBDebug.RenderDebugSphere(Agent.Main.Position + (0,0,2), 3f, 0xFFFF0000, false, 10f)`
> —— ① 能看到 → 通道活着，问题在面球逻辑（坐标/密度/时间参数）；② 看不到 → native 通道整体不可用，走备选。

**备选绘制通道（若方向 1 成立）**：

- **动态实体方案（首选备选）**：`Scene.AddItemEntity / AddEntityWithMultiMesh` + 现成小球/标记模型（MetaMesh，查 `Prefabs/editor_*` 类编辑器辅助模型）→ 走**正常渲染管线**，必定可见；代价 = 每个标记一个实体（量大需控制）+ 模型资源。上层 `NavMeshDebugRenderer` 架构不用动（DrawXxx 内部换成实体池）。
- GauntletUI 3D→2D 投影自绘（成本最高，最后选择）。

**代码状态**：`CampaignMode/Tools/NavMeshDebug{Commands,Renderer,MissionView,MapTickPatch}.cs` 四文件的功能逻辑（命令解析 / 双世界分派 / 面数据与寻路查询 / 监视对象解析）**全部有效可复用**——只有"最后一步画到屏幕"存疑。

**续接入口**：本文档 §7.3 + §7.4 + 上述四文件 + memory `navmesh-debug-overlay-unresolved`。

---

## 八、对内容包建城的应用

### 8.1 动态摆城的可行边界（结合三单元架构）

| 层 | 做法 | 理由 |
|---|---|---|
| 地形 / 出生点 / 战斗逻辑点位 / **Agent 可达区域的行走面** | **静态烘焙进 xscene** | navmesh 无运行时重建；这些面必须烘焙期存在 |
| 重复建筑构件（石垣段/门/塔） | 动态实体 + **各带导航件**（`ImportNavigationMeshPrefab` + `AttachNavigationMeshFaces`） | 复用省包体；AI 可正常通行 |
| 纯装饰（灯笼/旗帜/杂物，Agent 走不到处） | 动态实体，**不带导航件** | 不影响寻路 |
| 可变状态（攻城梯/桥/门开关） | 面组开关：`SetAbilityOfFacesWithId(id, enabled)` | 原生机制，零重建成本 |

### 8.2 建城验收清单

1. **编辑器**：NavMesh Inspector 生成 → `Make Quads` → `Find Tight Faces` 删碎面 → `Remove Unused Vertices` → `Mark Elevation Problem Faces` 修悬空面。
2. **导航件**：需要的面 → `Export Faces as Prefab`。
3. **游戏内**（本 mod 工具）：
   - `custom.nav_debug on` + `radius` 调范围 → 看玩家周围面的覆盖与分组；
   - `custom.nav_path <城门坐标> <天守坐标>` → 折线出现 = 可达；红色 = 不可达（面缺失或被禁用）；
   - `custom.nav_face x y` → 逐点查关键坐标（门洞/桥/坡道）的面归属；
   - 调动态开关后路径即时变化 = 机制生效验证。
4. **攻城场景**：确认门（7）/梯（13）/壕沟（9,10）/桥（8,12,13）面组按原生约定布置（复用原生攻城 AI 的判断逻辑）。

### 8.3 待验证项（实机）

- `ImportNavigationMeshPrefab` 的导航件来源 = 编辑器 Export Faces as Prefab 产物（编辑器侧确认；运行时路径待实测）。
- 导航件挂到**运行时动态创建的实体**上（`AttachNavigationMeshFaces`）的时序——先建实体后挂面，建议在 `Mission.OnInitialize` 之后。
- 每城导入导航件的性能开销（面数增长对寻路的影响）。

---

## 附：API 速查（本文全部签名一览）

```
── 开关 ──
Scene.SetAbilityOfFacesWithId(int navMeshId, bool isEnabled)
GameEntity.AttachNavigationMeshFaces(int navMeshId, bool isConnected, bool isBlocker = false)
Scene.ImportNavigationMeshPrefab(string navMeshPrefabName, int navMeshGroupShift)   // engine:12214
Mission.GetNextDynamicNavMeshIdStart()                                               // mb:52525

── 查询（Scene）──
GetNavMeshFaceCount()                                                                // engine:11561
GetNavMeshFaceIndex(ref PathFaceRecord, Vec2, bool checkIfDisabled, bool ignoreHeight=false)  // engine:11474
GetIdOfNavMeshFace(int faceIndex)                                                    // engine:11592
GetNavMeshCenterPosition(int faceIndex, ref Vec3)                                    // engine:11602
GetNavMeshFaceFirstVertexZ(int faceIndex)                                            // engine:11654
DoesPathExistBetweenFaces(int a, int b, bool ignoreDisabled)                         // engine:11944
GetPathBetweenAIFaces(int, int, Vec2, Vec2, float radius, NavigationPath, int[] excluded=null, float extraCost=1f)  // engine:11457
Utilities.ExportNavMeshFaceMarks(string file_name)                                   // engine:14414（面标记导出文件）

── 查询（Agent）──
agent.GetCurrentNavigationFaceId()                                                   // mb:12810
agent.GetTargetPosition() → Vec2                                                     // mb:12593（当前 AI 目标位置）
agent.GetTargetDirection() → Vec3                                                    // mb:12598（当前 AI 目标方向，可直接画箭头）
agent.CheckPathToAITargetAgentPassesThroughNavigationFaceIdFromDirection(int faceId, ref Vec3 dir, float overridenCost)  // mb:13440
agent.HasPathThroughNavigationFaceIdFromDirection(int faceId, Vec2 dir)             // mb:14074

── 数据结构 ──
PathFaceRecord { FaceIndex, FaceGroupIndex, FaceIslandIndex }  // lib:8142
NavigationPath { Vec2[] PathPoints (128), int Size, this[i] }  // lib:7752

── 绘制（需 _RGL_KEEP_ASSERTS 编译符号）──
MBDebug.RenderDebugLine(Vec3 pos, Vec3 direction, uint color, bool depthCheck, float time)
MBDebug.RenderDebugSphere(Vec3 pos, float radius, uint color, bool depthCheck, float time)
MBDebug.RenderDebugText3D(Vec3 pos, string text, uint color, int offX, int offY, float time)
MBDebug.RenderDebugBoxObject(Vec3 min, Vec3 max, uint color, bool depthCheck, float time)
```

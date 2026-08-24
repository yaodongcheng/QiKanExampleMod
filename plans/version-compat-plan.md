# 版本兼容策略 — 三锚点验证，禁止交叉编译

## 当前状态（2026-08-03）

### ✅ 已完成

| 项目 | 状态 | 说明 |
|------|:----:|------|
| **v1.3.15 三锚点验证** | ✅ | 用 1.3.15 DLL（游戏目录）+ 1.2.12/1.4.6 备份 DLL 逐 API 反编译对比，27 个 V 方法 + 14 处注册表 #if 全部验证 |
| **v1.5.1 第四锚点验证（2026-08-23）** | ✅ | 开发机升级 v1.5.1（Latest）后 `dotnet build -c Debug` 0 错误 0 警告；27 个 Harmony 字符串补丁目标二进制 grep 全存活；1.5.x 与 1.4.x 签名一致，`MB2_GE_150` 尚无分支使用 |
| **RaidSettlement 修复** | ✅ | VersionCompat.cs：`GetActionForRaidingSettlement` 1.3.x=4 参 / 1.4.x=5 参，新增 `#elif MB2_GE_130` 分支 |
| **CanPlayerTakeQuestConditions 修复** | ✅ | CommissionHubIssue.cs:413：1.2.12~1.3.x 基类 4 参 / 1.4.x 基类 5 参，override 改 `MB2_GE_140` 三分支 |
| **本机 v1.3.15 编译** | ✅ | `dotnet build -c Debug` **0 errors 0 warnings** |
| csproj 累积阈值宏 | ✅ | v1.3.x → `MB2_V1212`+`MB2_GE_130` 自动侦测，无需改动 |
| VersionCompat.cs 注册表注释 | ✅ | CommissionHubIssue 行更新为三分支说明 |

### ❌ 待办

| 项目 | 优先级 | 说明 |
|------|:------:|------|
| **v1.2.12 编译验证** | 🔴 P0 | 在 v1.2.12 电脑上 `dotnet build -c Release` 确认 0 errors |
| ~~v1.4.6+ 编译验证~~ | ✅ | 已随 v1.4.8 开发机验证；1.5.1 升级后再度验证（RaidSettlement 5 参 / requiredGold 分支均编译通过） |
| ~~发布策略确认~~ | ✅ | **已确认：三版全出**（1.2.12 / 1.3.15 / 1.5.x 各一台机器出 DLL；1.4.x 成为历史，需要时可用 MB2_1.4.8 备份客户端临时编译） |
| CampaignAgentComponent 死代码 | 🟢 P2 | MyCommands.cs 中 4 处被 `#if false` 包裹，需找到正确 API 或彻底删除 |

---

## 核心原则

**不支持跨版本编译。** 不要试图用一台装了 v1.3.15 的电脑去编译 v1.2.12 的 DLL，反之亦然。API 差异不是简单的 `#if` 能完全隔离的（DLL 引用本身就不兼容）。

## 三锚点编译工作流

每台电脑装一个目标版本，**同一份源码**，分别在每台电脑上编译，产出多份 DLL，分别打包发布（标注版本号）。
**每台电脑的实际版本 = 其 `MB2_PATH` 指向的游戏安装版本（csproj 自动读 Version.xml 检测，无需手动指定）**：

| 机器 | 游戏版本 | 产出 |
|------|---------|------|
| A | v1.2.12 | `LivingWorldNpcs.dll`（v1.2.12 版） |
| B | v1.5.x（当前开发机，实测 v1.5.1） | `LivingWorldNpcs.dll`（Latest 版） |
| C | v1.3.15（备份客户端 MB2_1.3.15） | `LivingWorldNpcs.dll`（v1.3.15 版） |

### 🔴 必选检查项：备份客户端 Modules 下必须有 LivingWorldNpcs junction（2026-08-24）

备份游戏（`MB2_Version\MB2_1.2.12` / `MB2_1.3.15` / `MB2_1.4.8`）**不做独立拷贝**，
`Modules\LivingWorldNpcs` 一律是 **junction** 指向主游戏
`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs`
（三份已建，2026-08-24 核对：1.2.12 ✅ / 1.3.15 ✅ / 1.4.8 ✅）。

**每次出现新版本备份目录 / 换机 / 目录改动后，编译前必查**（缺失 = 游戏加载不到 mod；
普通目录 = 版本隔离失效，改源码两处不同步）：

```powershell
Get-Item "<备份版>\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs" -Force | fl LinkType, Target
# 期望：LinkType=Junction，Target=主游戏 LivingWorldNpcs 路径；Test-Path 为 False = 死链需重建
```

创建命令：

```powershell
New-Item -ItemType Junction -Path "<备份版>\...\Modules\LivingWorldNpcs" -Target "<主游戏>\...\Modules\LivingWorldNpcs"
```

## 版本检测机制（自动，无需手动干预）

csproj 在编译时自动读取本地游戏的 `Version.xml`：

```
$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml
```

### 累积阈值宏体系（GE = "Greater or Equal"）

**不做精确版本匹配，而是定义"从哪个版本开始有这个 API"的累积宏**：

| 本地游戏版本 | 自动定义的宏 |
|-------------|------------|
| v1.2.12 | `MB2_V1212` |
| v1.3.x | `MB2_GE_130` |
| v1.4.x | `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x | `MB2_GE_130` + `MB2_GE_140` + `MB2_GE_150` |

🔴 `MB2_V1212` 是**精确匹配** v1.2.12（csproj 判定 = Version.xml.Contains('v1.2.12')），不是累积宏；
`#else` 分支语义 = "≤ v1.2.12"（v1.2.12 是支持的最低版本）。只有 GE_* 宏才是累积的。

代码按阈值从高到低写分支：`#if MB2_GE_150 / #elif MB2_GE_140 / #elif MB2_GE_130 / #else`。

🔴 **1.5.x 已实机验证（2026-08-23）**：开发机升级 v1.5.1 后 `dotnet build -c Debug` **0 错误 0 警告**——
项目全部 API 在 1.5.x 签名与 1.4.x 一致，`MB2_GE_130`/`MB2_GE_140` 分支继续覆盖；27 个 Harmony
字符串补丁目标二进制 grep 全存活（唯一 MISSING 为 1.2.12-only 条件编译的 FillPartyStacks）。
`MB2_GE_150` 已自动定义但**尚无代码分支使用**。

## 🔴 三锚点验证结论（2026-08-03；2026-08-23 增补 1.5.1 第四锚点）

之前只有 1.2.12 / 1.4.6 两个端点，所有差异都归到 `MB2_GE_130` 且无法验证 1.3.x 中间形态。
现在有 **1.2.12 / 1.3.15 / 1.4.6 / 1.5.1 四个锚点**（1.3.15 用游戏目录 DLL，1.2.12/1.4.6 用备份 DLL，
1.5.1 用升级后的开发机），用 ilspycmd 逐 API 反编译对比，结论：

🔴 **1.5.1 第四锚点验证（2026-08-23）**：开发机升级 v1.5.1（Latest，Version.xml 实测）后
`dotnet build -c Debug` **0 错误 0 警告**——项目全部 API 在 1.5.x 签名与 1.4.x 一致；
27 个 Harmony 字符串补丁目标二进制 grep 全存活（唯一 MISSING = 1.2.12-only 条件编译的
`FillPartyStacks`，预期）。`MB2_GE_150` 已自动定义但尚无代码分支使用；今后发现 1.5.x
独有差异时在 VersionCompat.cs 对应方法插入 `#if MB2_GE_150` 分支即可（阈值从高到低排）。

### 与 1.4.6 一致的 API（`MB2_GE_130` 分支正确，无需改动）

以下 API 在 **1.3.15 与 1.4.6 签名完全一致**（均已反编译验证）：

- `MobileParty.GetPosition2D` / `Position`(CampaignVec2 setter) / `SetMove*` 家族 / `MoveTargetParty` / `CreateParty(2参)` / `InitializeMobilePartyAtPosition(CampaignVec2)`
- `DestroyPartyAction.Apply` / `ChangeKingdomAction.ApplyByJoinToKingdomByDefection(5参带CampaignTime)`
- `Kingdom.CurrentTotalStrength` / `Kingdom.All` + `IsAtWarWith`（`FactionManager.GetEnemyKingdoms` 已删）
- `Campaign.Models.CampaignTimeModel.CampaignStartTime` / `TextObject.GetEmpty()`
- `Agent.IsAIControlled` / `AgentControllerType`（1.3.15 定义在 **TaleWorlds.Core.dll**）/ `GetPrimaryWieldedItemIndex` / `GetCurrentActionType`
- `Mission.RayCastForClosestAgent(out 在最后)` / `Scene.RayCastForClosestEntityOrTerrain(out WeakGameEntity)` / `GetNavigationMeshForPosition(in, UIntPtr)`
- `GauntletLayer(string, int)` 构造 / `LoadMovie` 返回 `GauntletMovieIdentifier`
- `SetPartyAiAction.GetActionForPatrollingAroundSettlement(5参)` / `BesiegingSettlement(4参)` / `EngagingParty(4参)`
- `ChangeKingdomAction.ApplyByJoinToKingdom`（4参带 CampaignTime）/ `EndCaptivityAction.ApplyByEscape`（3参带 showNotification）/
  `CampaignEvents.HeroPrisonerReleased`（5参带 bool isPlayer）/ `CampaignEvents.BeforeHeroesMarried`（1.2.12 无此事件，同名同签名为 `HeroesMarried`）/
  `MapWeatherModel.WeatherEvent.Storm`（枚举成员 1.3.0+ 新增，1.2.12 无）—— **2026-08-17 三版本 ilspycmd 实锤**，
  新增 `V.JoinKingdom` / `V.EndCaptivityEscape` / `V.WeatherWord`（Storm 分支必须 `#if MB2_GE_130`）
- `IMapScene.GetAccessiblePointNearPosition(in CampaignVec2)` / `GetFaceIndex(in CampaignVec2)` / `GetPathDistanceBetweenAIFaces(10参)`
- `IMapStateHandler.StartCameraAnimation(CampaignVec2, float)`
- 注册表各位置：`OnRegisterBlow(WeakGameEntity)` / `GetDefaultComponentBanner` / `GameMenu.MenuOverlayType` / `MissionObject.GameEntity→WeakGameEntity` / `AgentInteractionInterfaceVM(Missions.Interaction)` / `DisguiseMissionLogic`+`StealthFailCounterMissionLogic` / `MobilePartyHelper.FillPartyManuallyAfterCreation` / `SandBox.Missions` 命名空间

### 🔴 需要 `MB2_GE_140` 分支的 API（1.3.15 ≠ 1.4.6）

| API | 1.2.12 | 1.3.15 | 1.4.6 | 处理 |
|-----|--------|--------|-------|------|
| `SetPartyAiAction.GetActionForRaidingSettlement` | 2 参 | **4 参**（navType, isFromPort） | **5 参**（+isTargetingPort） | `V.RaidSettlement` 三分支 ✅ |
| `IssueBase.CanPlayerTakeQuestConditions` | 4 参 | **4 参**（同 1.2.12） | **5 参**（+out int requiredGold） | CommissionHubIssue override 三分支 ✅ |

**注意**：这两个 API 的 1.3.x 形态是 1.3.x **独有**（既不是 1.2.12 也不是 1.4.6）——
RaidingSettlement 的 4 参版本、CanPlayerTakeQuestConditions 的 4 参版本只在 1.3.x 存在。
`MB2_GE_140` 不再只是"预留"，已是实际使用的分支。

### 1.2.12 独有（`#else` / `MB2_V1212` 分支，已验证 1.3.15 无）

`MobileParty.Position2D`(Vec2 setter) / `Ai.SetMove*` / `Ai.MoveTargetParty` / `CreateParty(3参)` / `RemoveParty()` /
`Agent.ControllerType` 嵌套枚举 / `TextObject.Empty` / `GetWieldedItemIndex(HandIndex)` / `FactionManager.GetEnemyKingdoms` /
`RayCastForClosestAgent(out 在第3位)` / `Scene.RayCastForClosestEntityOrTerrain(out GameEntity)` /
`GetNavigationMeshForPosition(ref, bool)` / `GauntletLayer(int, string)` / `ChangeKingdomAction(3参)` /
`IMapScene.AreFacesOnSameIsland` / `SetPartyAiAction.GetActionFor*(2参)` / `Vec2` 版 IMapScene/StartCameraAnimation /
`InventoryManager.OpenScreenAsLoot`（搜刮流）/ `FillPartyStacks`

## VersionCompat.cs：版本差异统一入口

[Core/VersionCompat.cs](../ExampleModVS/ExampleMod/ExampleMod/Core/VersionCompat.cs) — `V` 静态类封装了全部 API 差异。

**纪律**：
- 凡是跨版本 API 不同的调用，**一律走 `V.xxx()`**，禁止在业务代码里裸写 `#if`
- 新增 V 方法后，**必须在每台目标版本电脑上分别编译通过**
- 四锚点已验证：除 RaidSettlement 外所有 V 方法的 `MB2_GE_130` 分支覆盖 v1.3.0~v1.5.x 正确（1.5.1 编译验证，2026-08-23）
- 遇到 1.3.x 与 1.4.x 不同而 1.3.x 与 1.2.12 相同的 API（如 `CanPlayerTakeQuestConditions`），**必须用 `MB2_GE_140` 三分支**，不能沿用 `!MB2_V1212` 二分

### 不可迁入 V 的 #if（合规例外登记表）

以下类别的 `#if` **不能**封装为 `V.xxx()` 方法，直接写在业务文件里是合法的。每次新增版本时必须逐条核查：

| 类别 | 文件:行号 | 原因 |
|------|----------|------|
| override | `SafeLordPartyComponent.cs:41` | `GetDefaultComponentBanner()` 只存在于 1.3.0+ 基类虚方法 |
| override | `CustomPartyComponent.cs:47` | 同上 |
| override | `AttackTriggerMissionLogic.cs:391` | `OnRegisterBlow` 第三参 `GameEntity`→`WeakGameEntity`（1.3.15 已验证） |
| override | `CommissionHubIssue.cs:413` | 🔴 `CanPlayerTakeQuestConditions`：**1.2.12~1.3.x 4 参 / 1.4.x 5 参**，用 `MB2_GE_140` 三分支（不是 `!MB2_V1212` 二分） |
| type | `MySubModule.cs:344` | 字段类型 `IGauntletMovie`→`GauntletMovieIdentifier`（1.3.15 已验证） |
| type | `CameraDebuggerView.cs:34` | 同上 |
| type | `SpringArmCameraView.cs:40` | 同上 |
| type | `NinjaNotificationMissionView.cs:19` | 同上 |
| type | `MyCommands.cs:646` | `MissionObject.GameEntity` 返回 `WeakGameEntity`（1.3.15 已验证） |
| type | `PlayerDetentionBehavior.cs:9,358` | `GameOverlays.MenuOverlayType`→`GameMenu.MenuOverlayType`（1.3.15 已验证） |
| Harmony | `InteractionMissionView.cs:2550` | F-to-talk 补丁：`AgentInteractionInterfaceVM` 命名空间从顶层移到 `Missions.Interaction`（1.3.15 已验证） |
| Harmony | `InteractionMissionView.cs:2582` | 村庄交易日志补丁：`InventoryManager.OpenScreenAsTrade` 三版本都存在（1.2.12 第 4 参 `DoneLogicExtrasDelegate` vs 1.3.15+ `Action`），补丁只在 1.2.12 编译，功能缺失不影响 |
| Harmony | `DebugLogger.cs:18` | `FillPartyStacks`→`FillPartyManuallyAfterCreation`（1.3.15 已验证 MobilePartyHelper 存在） |
| structural | `WorldEventSimulator.cs:1668,1719` | `AreFacesOnSameIsland` 移除（1.3.15 已验证）；`GetPathDistanceBetweenAIFaces` 1.3.15 已是 10 参 |
| structural | `MyBehavior.cs:33,45` | `CampaignEvents` 事件注册差异（2026-08-17 三版本实锤）：`HeroPrisonerReleased` 4参(1.2.12) / 5参(1.3+，lambda 适配)；`BeforeHeroesMarried` 1.3+ / 1.2.12 为同名同签名 `HeroesMarried`（婚后触发） |
| structural | `InteractionMissionView.cs:1930,2385` | 搜刮 Loot 流（`InventoryManager.OpenScreenAsLoot` 1.2.12 only，1.3.15 走自研 fallback） |
| structural | `MyCommands.cs:1619` | stealth_debug 命令（`DisguiseMissionLogic` 等 1.3.15 已存在，同 1.4.6） |
| namespace | `MyCommands.cs:30` | `SandBox.Missions` 命名空间三版本都存在，仅 1.2.12 用不上 |

## Modules/ 目录：仅用于 ilspycmd，不参与编译

| 目录 | 版本 | 用途 |
|------|------|------|
| `Modules/1.2.12DLL/` | v1.2.12 | **反编译对比 API 差异**（在任意电脑上查 1.2.12 的方法签名） |
| `Modules/1.3.15DLL/` | v1.3.15 | **反编译对比 API 差异**（在非 1.3.15 电脑上查 1.3.15 的签名） |
| `Modules/1.4.6DLL/` | v1.4.6 | **反编译对比 API 差异**（1.4.x 历史锚点；1.4.6/1.4.7/1.4.8 签名一致） |
| `Modules/1.5.1DLL/` | v1.5.1 | 🔴 **反编译查 Latest 的 API 签名**（2026-08-23 备份；可代表整套 1.5.x） |

```bash
# 对比四个版本的同个方法
ilspycmd Modules/1.2.12DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.5.1DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
# 当前机器的 1.3.15 用游戏目录 DLL：
ilspycmd bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```

**这些 DLL 不参与编译。** 不要试图用备份 DLL 配置去交叉编译——该配置已废弃。

**ilspycmd 注意**：`-t` 参数一次只能传一个类型（传多个会参数解析失败，输出 "Specify --help"）。
类型全名以 `ilspycmd <dll> -l c`（类）/ `-l e`（枚举）列出的为准，例如 `MobileParty` 的全名是
`TaleWorlds.CampaignSystem.Party.MobileParty`（中间有 `Party`），`AgentControllerType` 定义在
`TaleWorlds.Core.dll` 而非常见的 MountAndBlade。

## 发布步骤

```bash
# 任意电脑：版本 = 本机 MB2_PATH 指向的游戏版本（自动检测）
dotnet build -c Release
# → 本机游戏版本的 DLL（版本见 Version.xml）

# 发布多版本：到对应版本的电脑上
git pull
dotnet build -c Release
# → 该电脑游戏版本的 DLL
```

各版本 DLL 分别打包，发布时标注版本号。

## 新增游戏版本时

TaleWorlds 出新版本时，执行以下检查清单（🔴 v1.5.0 检查已按此清单执行完毕，2026-08-23 记于各步）：

### 1. 更新 csproj 版本侦测
```xml
<!-- 新增版本系列侦测 -->
<MB2_IsV15x Condition="$(MB2_VersionFileContent.Contains('v1.5.'))">true</MB2_IsV15x>
<!-- 已有 GE_* 的 Or 链追加新版本 -->
<MB2_VersionDefines Condition="... Or '$(MB2_IsV15x)' == 'true'">...</MB2_VersionDefines>
<!-- 新增 GE_150 阈值 -->
<MB2_VersionDefines Condition="'$(MB2_IsV15x)' == 'true'">$(MB2_VersionDefines);MB2_GE_150</MB2_VersionDefines>
```
✅ v1.5 侦测已提前就位（升级前 csproj 就带 v1.5 分支），升级后自动生效，未改动。

### 2. 对比 API 差异
用 ilspycmd 逐条对比 VersionCompat.cs 中所有 `V` 方法涉及的 API：
```bash
ilspycmd Modules/1.5.1DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```
✅ v1.5.1 实测：**编译 0 错误 0 警告**（快于逐条反编译——编译即覆盖全部用到的 API 签名），无需逐条 ilspycmd。

### 3. 根据差异等级行动

| 情况 | 行动 |
|------|------|
| 新版本 API 与 1.4.x 完全一致 | 无需改动，`MB2_GE_130`/`MB2_GE_140` 分支继续覆盖 |
| 某个 API 在新版本变更 | 在 VersionCompat.cs 对应方法中插入 `#if MB2_GE_150` 分支（阈值从高到低排） |
| 某个 API 在新版本被删除 | 可能需要 `#if !MB2_GE_150` 排除新版本，或新增 V 方法封装替代方案 |

✅ v1.5.1 = 第一行情形（签名完全一致），`MB2_GE_150` 无分支使用。

### 4. 核查 #if 注册表
逐条检查本文件「不可迁入 V 的 #if」登记表的每一行，确认：
- override/abstract 基类签名是否变化
- type-level 字段类型是否需要新增分支
- Harmony 补丁目标是否移动
- structural 差异是否需要调整算法

✅ v1.5.1：注册表逐条核查 + **27 个 Harmony 字符串补丁目标二进制 grep 全存活**
（唯一 MISSING = 1.2.12-only 条件编译的 `FillPartyStacks`，预期），注册表无改动。

### 5. 更新备份 DLL
把 `Modules/<旧 Latest>DLL/` 的 Latest 地位替换为新版本（旧版保留作历史锚点），更新此文档中的版本号引用。
✅ v1.5.1：已新建 `Modules/1.5.1DLL/`（41 文件，清单 = 三套旧备份并集；唯一缺失
`TaleWorlds.GauntletUI.TooltipExtensions.dll` 为 1.2.12-only，csproj Exists 守卫自动跳过）；
`Modules/1.4.6DLL/` 保留为 1.4.x 历史锚点。1.5.1 备份流程见 `.claude/skills/backup-version-dlls.md`。

## 踩过的坑（不要重犯）

1. **交叉编译**：试图在一台电脑上用备份 DLL 编译另一个版本的 DLL。DLL 引用级别就不兼容，编译报错会铺天盖地，且修复了也不代表运行时正确。
2. **正则批量替换 C# 代码**：嵌套括号、lambda 会错位。
3. **只在一台电脑上验证**：改完 VersionCompat.cs 必须每台目标版本电脑分别 build。
4. **`!MB2_V1212` 二分陷阱**：默认假设"1.3.x 与 1.4.x 一样"；遇到 1.3.x 与 1.2.12 相同的 API（如 `CanPlayerTakeQuestConditions`）时 `!MB2_V1212` 分支会编译失败（override 签名不匹配）。**有 1.3.x 的锚点前，此类差异不可见**——这正是本次三锚点验证的价值。
5. **GauntletLayer 参数顺序**：v1.2.12 是 `(int order, string name)`，v1.3.0+ 是 `(string name, int order)`——两个参数反了，不是增加/减少参数。
6. **ControllerType 枚举**：v1.2.12 是 `Agent.ControllerType` 嵌套枚举，v1.3.0+ 是 `TaleWorlds.Core.AgentControllerType` 顶层枚举（注意在 Core.dll，不在 MountAndBlade.dll）。
7. **ilspycmd 多类型**：`-t` 一次只能一个类型，多传会整体失败（输出 "Specify --help"）；类型全名要先 `-l c`/`-l e` 确认（如 `Party.MobileParty` 的中间命名空间易漏）。

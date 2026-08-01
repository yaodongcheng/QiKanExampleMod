# 版本兼容策略 — 两机编译，禁止交叉编译

## 当前状态（2026-08-01）

### ✅ 已完成

| 项目 | 状态 | 说明 |
|------|:----:|------|
| csproj 累积阈值宏 | ✅ | `MB2_GE_130/140/150`，版本系列自动侦测 + Or 链 |
| VersionCompat.cs V 方法 | ✅ | 原有 25 个 + 新增 8 个（PatrolAround/RaidSettlement/BesiegeSettlement/EngageParty/NavMeshSnap/AccessiblePointNear/FaceIndex/CameraAnimate） |
| VersionCompat.cs #if 注册表 | ✅ | class doc comment 中登记了全部 18 处不可迁 #if |
| 业务代码裸 #if 清理 | ✅ | **22 处** could-be-V 迁入 V.xxx()，文件：WorldEventSimulator / InvestigationEngine / AccountabilityIntents / AgentControlHelper / AiPatrollingNullFix / AtomicAction / GroupStageManager / WorldEventNotificationController |
| 预存错误修复 | ✅ | PlayerDetentionBehavior（GameOverlays→GameMenu）、InteractionMissionView（TradeScreenAnimalLoggerPatch 包裹）、MyCommands（CampaignAgentComponent 死代码包裹） |
| v1.4.7 编译 | ✅ | **0 errors 0 warnings** |
| CLAUDE.md 同步 | ✅ | 阈值宏 + 注册表 + 发布步骤 |
| wheels.md 同步 | ✅ | VersionCompat API 参考 + csproj 检测 + #if 注册表 |

### ❌ 待办

| 项目 | 优先级 | 说明 |
|------|:------:|------|
| **v1.2.12 编译验证** | 🔴 P0 | 必须在 v1.2.12 电脑上 `dotnet build -c Release` 确认 0 errors |
| WorldEventNotificationController StartCameraAnimation | ✅ | 第 522 行 → 改用 `V.CameraAnimate` |
| AgentControlHelper NavMeshSnap 替换 | ✅ | 第 140/382 行 → 改用 `V.NavMeshSnap` |
| GroupStageManager NavMeshSnap 替换 | ✅ | 第 116 行 → 改用 `V.NavMeshSnap` |
| AtomicAction NavMeshSnap 替换 | ✅ | 第 629 行 → 改用 `V.NavMeshSnap` |
| CampaignAgentComponent 死代码 | 🟢 P2 | MyCommands.cs 中 4 处被 `#if false` 包裹，需找到正确 API 或彻底删除 |
| WorldEventSimulator structural | 🟢 P2 | 第 1668/1719 行 `AreFacesOnSameIsland` 多语句差异——评估是否可抽象为 V 方法 |
| 发布 build | 🔴 P0 | 两台电脑分别 `dotnet build -c Release`，产出两份 DLL 打包 |

---

## 核心原则

**不支持跨版本编译。** 不要试图用一台装了 v1.4.7 的电脑去编译 v1.2.12 的 DLL，反之亦然。每次交叉编译尝试都会踩坑——API 差异不是简单的 `#if` 能完全隔离的（DLL 引用本身就不兼容）。

## 两机编译工作流

两台电脑，各自装一个目标版本：

| 机器 | 游戏版本 | 产出 |
|------|---------|------|
| A | v1.2.12 | `LivingWorldNpcs.dll`（v1.2.12 版） |
| B | v1.4.7（当前 Latest） | `LivingWorldNpcs.dll`（Latest 版） |

**同一份源码**，分别在两台电脑上编译，产出两份 DLL，分别打包发布。

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
| v1.3.x | `MB2_V1212` + `MB2_GE_130` |
| v1.4.x | `MB2_V1212` + `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x | `MB2_V1212` + `MB2_GE_130` + `MB2_GE_140` + `MB2_GE_150` |

csproj 实现：先侦测版本系列（`MB2_IsV12x`/`MB2_IsV13x`/`MB2_IsV14x`/...），再用 `Or` 链组合出累积阈值：
```xml
<!-- 侦测 -->
<MB2_IsV14x Condition="$(MB2_VersionFileContent.Contains('v1.4.'))">true</MB2_IsV14x>
<!-- MB2_GE_130：v1.3.x 或更高（即所有非 v1.2.x 的版本） -->
<MB2_VersionDefines Condition="'$(MB2_IsV13x)' == 'true' Or '$(MB2_IsV14x)' == 'true' Or '$(MB2_IsV15x)' == 'true'">$(MB2_VersionDefines);MB2_GE_130</MB2_VersionDefines>
<!-- MB2_GE_140：v1.4.x 或更高 -->
<MB2_VersionDefines Condition="'$(MB2_IsV14x)' == 'true' Or '$(MB2_IsV15x)' == 'true'">$(MB2_VersionDefines);MB2_GE_140</MB2_VersionDefines>
```

各配置引用 `$(MB2_VersionDefines)` 即可，不需要在每个 PropertyGroup 里重复版本检测逻辑。

### 为什么用阈值宏而不是精确版本匹配

99% 的 API 变更只发生在**一个版本边界**（v1.2.12 → v1.3.0）。阈值宏让你只为**真正发生了变更的边界**写分支：

```csharp
// ✅ 阈值宏：只在变更点分支
#if MB2_GE_130
    return party.GetPosition2D;    // v1.3.0+
#else
    return party.Position2D;       // v1.2.12
#endif

// ❌ 精确匹配：即使 1.3~1.5 API 完全一样也得穷举
#if MB2_V1212
    ...
#elif MB2_V130
    ...  // 完全一样的代码
#elif MB2_V140
    ...  // 完全一样的代码
#elif MB2_V150
    ...  // 完全一样的代码
#endif
```

如果未来 v1.5.0 又改了一个 API，只需在对应方法里插入一行：
```csharp
#if MB2_GE_150
    return party.GetPosition3D();  // v1.5.0+
#elif MB2_GE_130
    return party.GetPosition2D;    // v1.3.0 - v1.4.x
#else
    return party.Position2D;       // v1.2.12
#endif
```

**开发者不需要手动选配置**，日常 `dotnet build -c Debug` 或 `dotnet build -c Release` 即可。

### ⚠️ 阈值宏的局限性：只有两个端点，没有中间版本

`MB2_GE_130` 标注的是「API 在 v1.2.12 → v1.3.0 之间发生了变更」，但这个结论**不是靠官方 changelog，而是靠反编译对比两个端点 DLL 推断出来的**：

| 可用 DLL | 版本 |
|----------|------|
| `Modules/1.2.12DLL/` | v1.2.12 |
| `Modules/1.4.6DLL/` | v1.4.6（1.4.7 经逐方法对比确认完全一致） |

**没有 v1.3.0 / v1.3.1 / v1.4.0 等中间版本的 DLL 做精确验证。** 推断逻辑：

1. v1.2.12 用旧签名（如 `party.Position2D`）
2. v1.4.6/1.4.7 用新签名（如 `party.GetPosition2D`）
3. 1.4.6 和 1.4.7 逐方法对比确定 API 一致
4. → 变更发生在 v1.2.12 和 v1.4.6 之间的某个版本
5. → 最晚可能在 v1.3.0（第一个 1.3.x）→ 打 `MB2_GE_130` 标签

**这意味着**：
- `MB2_GE_130` 实际上是一个**下界估计**："这个 API 不晚于 v1.3.0 改了，之后到 v1.4.7 没再变过"
- 如果某个 API 其实是 v1.4.0 才改的，那 `MB2_GE_130` 这个名字就**名不副实**——功能没问题（v1.3.x 上会走旧路径），但标签有误导性
- 除非 TaleWorlds 发布中间版本的完整 changelog，或者有人用 Steam depot 下载每个小版本对比，否则无法做到精确标注

**应对**：目前所有 API 变更都归到 `MB2_GE_130`，因为实测 v1.2.12 和 v1.4.7 之间只有这一个分水岭需要分支。如果未来某个 API 发现是在更晚的版本（如 v1.4.0）才变的，届时再引入 `MB2_GE_140` 分支，把该 API 从 `MB2_GE_130` 移到 `MB2_GE_140`。

## VersionCompat.cs：版本差异统一入口

[Core/VersionCompat.cs](../ExampleModVS/ExampleMod/ExampleMod/Core/VersionCompat.cs) — `V` 静态类封装了 v1.2.12 ↔ Latest 的全部 API 差异。

```csharp
// 阈值宏分支：MB2_GE_130 = v1.3.0 及以上
public static Vec2 Pos(MobileParty party)
{
#if MB2_GE_130
    return party.GetPosition2D;    // v1.3.0+
#else
    return party.Position2D;       // v1.2.12
#endif
}
```

**纪律**：
- 凡是两版本 API 不同的调用，**一律走 `V.xxx()`**，禁止在业务代码里裸写 `#if`
- 新增 V 方法后，**必须在两台电脑上分别编译通过**
- 1.4.6 和 1.4.7 的 API 签名经逐方法对比确认**完全一致**，`MB2_GE_130` 分支覆盖 v1.3.0 ~ v1.4.x 全系列
- `MB2_GE_140` / `MB2_GE_150` 已由 csproj 定义，仅当未来实际 API 变更需要时才使用

### 不可迁入 V 的 #if（合规例外登记表）

以下类别的 `#if` **不能**封装为 `V.xxx()` 方法，直接写在业务文件里是合法的。每次新增版本时必须逐条核查：

| 类别 | 文件:行号 | 原因 |
|------|----------|------|
| override | `SafeLordPartyComponent.cs:41` | `GetDefaultComponentBanner()` 只存在于 Latest 基类虚方法 |
| override | `CustomPartyComponent.cs:42` | 同上 |
| override | `AttackTriggerMissionLogic.cs:395` | `OnRegisterBlow` 第三参 `GameEntity`→`WeakGameEntity` |
| override | `CommissionHubIssue.cs:388,399` | `CanPlayerTakeQuestConditions` 多一个 `out int requiredGold` |
| type | `MySubModule.cs:344` | 字段类型 `IGauntletMovie`→`GauntletMovieIdentifier` |
| type | `CameraDebuggerView.cs:34` | 同上 |
| type | `SpringArmCameraView.cs:40` | 同上 |
| type | `NinjaNotificationMissionView.cs:19` | 同上 |
| type | `MyCommands.cs:646` | `MissionObject.GameEntity` 返回类型变化 |
| type | `PlayerDetentionBehavior.cs:9,312` | `GameOverlays.MenuOverlayType`→`GameMenu.MenuOverlayType` |
| Harmony | `InteractionMissionView.cs:2529` | F-to-talk 补丁目标类+方法+属性类型全不同 |
| Harmony | `InteractionMissionView.cs:2559` | `InventoryManager.OpenScreenAsTrade`（v1.2.12 only） |
| Harmony | `DebugLogger.cs:18` | `FillPartyStacks`→`FillPartyManuallyAfterCreation` |
| structural | `WorldEventSimulator.cs:1668,1719` | `AreFacesOnSameIsland` 移除，多语句路径差异 |
| structural | `InteractionMissionView.cs:1909` | 搜刮 Loot 流（`InventoryManager` 不可用） |
| structural | `InteractionMissionView.cs:2364` | 开箱搜刮流（同上） |
| structural | `MyCommands.cs:1619` | stealth_debug 命令（依赖的类仅 Latest 存在） |
| namespace | `MyCommands.cs:30` | `SandBox.Missions.*` 命名空间仅 Latest 存在 |

## Modules/ 目录：仅用于 ilspycmd，不参与编译

| 目录 | 版本 | 用途 |
|------|------|------|
| `Modules/1.2.12DLL/` | v1.2.12 | **反编译对比 API 差异**（在 Latest 电脑上查 1.2.12 的方法签名） |
| `Modules/1.4.6DLL/` | v1.4.6 | **反编译对比 API 差异**（在 1.2.12 电脑上查 Latest 的方法签名） |

```bash
# 对比两个版本的同个方法
ilspycmd Modules/1.2.12DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```

**这些 DLL 不参与编译。** 不要试图用 `Debug_v1.2.12` 配置去交叉编译——该配置已废弃。

## 发布步骤

```bash
# 在 v1.4.7 电脑上
dotnet build -c Release
# → LivingWorldNpcs.dll（Latest 版，支持 1.4.6+）

# 在 v1.2.12 电脑上
git pull
dotnet build -c Release
# → LivingWorldNpcs.dll（v1.2.12 版）
```

两份 DLL 分别打包，发布时标注版本号。

## 新增游戏版本时

TaleWorlds 出新版本（如 v1.5.0）时，执行以下检查清单：

### 1. 更新 csproj 版本侦测
```xml
<!-- 新增版本系列侦测 -->
<MB2_IsV15x Condition="$(MB2_VersionFileContent.Contains('v1.5.'))">true</MB2_IsV15x>
<!-- 已有 GE_* 的 Or 链追加新版本 -->
<MB2_VersionDefines Condition="... Or '$(MB2_IsV15x)' == 'true'">...</MB2_VersionDefines>
<!-- 新增 GE_150 阈值 -->
<MB2_VersionDefines Condition="'$(MB2_IsV15x)' == 'true'">$(MB2_VersionDefines);MB2_GE_150</MB2_VersionDefines>
```

### 2. 对比 API 差异
用 ilspycmd 逐条对比 VersionCompat.cs 中所有 `V` 方法涉及的 API：
```bash
ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```

### 3. 根据差异等级行动

| 情况 | 行动 |
|------|------|
| 新版本 API 与 1.4.x 完全一致 | 无需改动，`MB2_GE_130` 分支继续覆盖 |
| 某个 API 在新版本变更 | 在 VersionCompat.cs 对应方法中插入 `#if MB2_GE_150` 分支（阈值从高到低排） |
| 某个 API 在新版本被删除 | 可能需要 `#if !MB2_GE_150` 排除新版本，或新增 V 方法封装替代方案 |

### 4. 核查 #if 注册表
逐条检查版本兼容 plan 中「不可迁入 V 的 #if」登记表的每一行，确认：
- override/abstract 基类签名是否变化
- type-level 字段类型是否需要新增分支
- Harmony 补丁目标是否移动
- structural 差异是否需要调整算法

### 5. 更新备份 DLL
把 `Modules/1.4.6DLL/` 替换为新版本 DLL（旧版可在 Steam depot 回溯下载），更新此文档中的版本号引用。

## 踩过的坑（不要重犯）

1. **交叉编译**：试图在一台电脑上用备份 DLL 编译另一个版本的 DLL。DLL 引用级别就不兼容，编译报错会铺天盖地，且修复了也不代表运行时正确。
2. **正则批量替换 C# 代码**：嵌套括号、lambda 会错位。
3. **只在一台电脑上验证**：改完 VersionCompat.cs 必须两台电脑分别 build。
4. **GauntletLayer 参数顺序**：v1.2.12 是 `(int order, string name)`，Latest 是 `(string name, int order)`——两个参数反了，不是增加/减少参数。
5. **ControllerType 枚举在 Latest 被删除**：不要试图在 Latest 路径里设 `agent.Controller = ...`，Agent 控制必须走其他方式。

# input — 轮子速查分卷（wheels.md 索引导航）

---

## 玩法行输入模型（短/长按状态机 + config.json 键位化）— `Input/ModInput.cs`

**解决什么问题**：① press 沿无法区分短按/长按——犯罪/战斗决策/搜刮等"需要确认"的互动应长按（给玩家确认时间，铁律 12），普通互动短按；② 键位冲突（手柄 X=踢人）无法用 Harmony 修复（native 层），只能改绑；③ 改键要满世界找 `IsKeyPressed`。

**玩法行 = 配置一等公民**：每个玩法交互一行 `(键盘键, 手柄键, 按法)`，同一物理键可挂多行（默认 F 挂 7 行），同一次按下各按各自阈值触发。

```csharp
// 玩法 ID 常量（config.json 键名 = 玩法行；Settings.DefaultInteractions 内置默认表）
InteractionIds.Talk / Loot / Knockout / Pickpocket / StealAnimal / Lockpick
InteractionIds.PlayerSurrender / AcceptSurrender / Inspect / StealAttempt / StealLeave

// 每帧驱动（InteractionMissionView.OnMissionTick 顶部，先于一切消费点）：
ModInput.Tick(dt);                        // 状态机：按行跟踪，短/长互斥

// 消费（一次按下只短或长其一；不同键各触发各的，不互相吞事件）：
ModInput.ShortFired(id);   // 一次性：松开且按住时长 < 阈值
ModInput.LongFired(id);    // 一次性：按住跨阈值
ModInput.IsHeld(id);       // 按住中
ModInput.HoldProgress(id); // 0..1（进度 UI 每帧喂给 4 段方框）
ModInput.Reset(id);        // 上下文退出/目标丢失时取消按住（进度框消退、不误触发）
ModInput.ResetAll();       // Mission Finalize 兜底
ModInput.RebuildBindings();// Settings.Reload() 后重建（控制台 custom.input_reload）
ModInput.GetBinding(id);   // → InteractionBinding（UI 读键位/按法，与输入共享同一份配置）
ModInput.Glyph(id);        // 按最近设备返回键盘/Xbox/PS 字形
```

**关键纪律**：
- **帧窗口一次性触发**：Tick 每帧先清上一帧未消费的标志 → 消费即清。模态覆盖（对话/剧情）期间的按压不会陈旧触发——这是防"陈旧触发"的根。
- **短按 = 阈值前松开**；长按行阈值前松开无触发。Short 行按住超阈值 = 取消（转入同键其他行的长按路径）。
- **手柄配置写逻辑键**（`Y`/`Triangle` 等价解析到 `ControllerRUp`，别名表大小写不敏感；显示 = 引擎键反查 `_engineDisplay` 表，Xbox 显示 Y、PS 显示 △）；键盘直接 `Enum.TryParse<InputKey>`（单词键 Space/Tab 走 `LWN_input_key_*` 本地化，铁律 13）。
- **解析失败回落内置默认 + `DebugLogger.Log` 警告**（空值 = 内置默认，config 文档约定；非法值 = 回落 + 警告）。
- **阈值**：玩法级 `HoldMs`（>0 覆盖）→ 全局 `Settings.LongPressDurationMs`（默认 450ms，KCD 手感）。
- **同键同按法冲突检查在"上下文构建时"而非加载时**：默认配置 F/Long 挂 6 行但各上下文互斥（永不同时可用），加载时检查会刷屏；改为 available 列表变化时对"同键同按法且同时可用"的行对告警，只在真实危险时发声。

**上下文唯一真相源**（`InteractionMissionView`）：`PerformPerformanceHeavyLogic` 构建一次 `_availableIds`（可用玩法列表），**同一份列表同时驱动 UI 显示（`_uiItems`）与输入响应**（`HandleInput` 遍历 available，`ExecuteInteraction(id)` 静态 switch 分发）——杜绝"显示不响应 / 响应不显示"。门控 = `available 非空`（`IsVisible` 只管 UI 显示）：无目标场景 `available=[Inspect]` 且 UI 隐藏时探查键照常响应"看自己"。列表变化 → **退出项与进入项均 `Reset`**（退出 = 长按作废、进度框消退、不误触发；进入 = 清零跨上下文继承的按住电荷——状态机按物理键计时，同键挂多行同时计时，若沿用上一上下文的电荷会"对话长按 → 目标转身 → 击晕凭空满金、松手犯罪"；新电荷从该行在本上下文重新按下算起，不影响"满框待命"）。偷窃条（StealAttempt/StealLeave）= **独立输入通道**（available 体系之外，开条时先 Reset 清陈旧状态）。

**文件位置**：`Input/ModInput.cs`（InteractionIds/ModInputPressMode/InteractionBinding/状态机/别名表）、`Core/Settings.cs`（`InteractionBindingConfig` + `DefaultInteractions` + `LongPressDurationMs`）、`Interaction/InteractionMissionView.cs`（available 上下文构建 + ExecuteInteraction 分发 + SyncAvailable）、`Interaction/InteractionVM.cs`（`InteractionItemVM` 玩法行绑定 + 4 段方框进度）。

---

## 三件套

```csharp
// ① 语义动作枚举：加新互动 = 加一个枚举值 + 映射表加一行
public enum ModInputAction { Interact, AltInteract, Inspect, StealAttempt, StealLeave }

// ② 输入轮询（键盘+手柄双通道同时监听，玩家中途换设备无需切换逻辑）
ModInput.Pressed(ModInputAction.Interact);    // 按下沿，键盘 F 或手柄 X/□ 任一命中即算
ModInput.Released(ModInputAction.StealLeave); // 松开沿

// ③ 提示字形（按"最近一次输入设备"自动分键盘/Xbox/PS 三套文本）
ModInput.Glyph(ModInputAction.Interact);  // → "F" / "X" / "□"
ModInput.UsingGamepad;                    // 当前设备是否手柄
ModInput.IsPlayStation;                   // 手柄是否 PS 系
```


---

## 当前映射表（改键唯一入口）

| 动作 | 键盘 | Xbox | PS | 用途 |
|------|------|------|-----|------|
| Interact | F | X | □ | 对话/偷窃/击晕/搜刮/撬锁 |
| AltInteract | G | Y | △ | 闲聊/接受认输 |
| Inspect | H | L3 | L3 | 探查 NPC 信息板 |
| StealAttempt | 空格 | A | ✕ | 偷窃条出手 |
| StealLeave | Tab | B | ○ | 偷窃条收手 |

**键位选择纪律**：主互动用 X/□ 不用 A/✕——A 是跳跃，漫游时会误触；偷窃条内可以用 A（条打开时玩家控制已冻结）。手柄键位对照：`RDown=A/✕` `RRight=B/○` `RUp=Y/△` `RLeft=X/□` `LThumb=L3`。


---

## 设备检测原理（引擎原生，与原版 UI 判定一致）

- 最近设备：`Input.IsGamepadActive`（= `IsControllerConnected && !IsMouseActive`，引擎每帧 `Input.Update()` 维护）——**不要自己造键盘/手柄检测**。
- Xbox/PS 区分：`Input.ControllerType.IsPlaystation()`（DualShock/DualSense → true）。
- PS 字形 □△✕○ 走 CJK 符号区，中文字体可渲染；若 ✕ 实机豆腐块，改映射表 `PsGlyph` 一行即可。
- v1.2.12 / v1.4.6 双版本 API 一致（已核实），无需 `V.` 包装。


---

## 接入范式（UI 按键提示随设备切换）

```csharp
// View 侧：缓存设备状态逐帧对比，变化时刷新全部按键提示（InteractionMissionView.OnMissionTick 为范本）
bool usingGamepad = ModInput.UsingGamepad;
if (usingGamepad != _lastUsingGamepad)
{
    _lastUsingGamepad = usingGamepad;
    _interactVM?.RefreshGlyphs();        // InteractArea：item 存 ModInputAction?，重算 KeyText
    _stealBarVM?.RefreshButtonTexts();   // 偷窃条：重算 AttemptButtonText/LeaveButtonText
}
```

**VM 侧纪律**：按键提示文本**禁止**写死 `"F"`/`"[空格]"` 字串——item/按钮存 `ModInputAction`，显示时 `ModInput.Glyph()` 实时解析（范本：`InteractionItemVM.RefreshKeyText`、`StealBarVM.RefreshButtonTexts`）。XML 一律绑 `@KeyText`/`@XxxButtonText`，不写裸文本。

**文件位置**：`Input/ModInput.cs`（枚举 + 映射表 + 轮询/字形 API）、`Interaction/InteractionVM.cs`（`RefreshGlyphs` 范本）、`Stealth/StealBarVM.cs`（`RefreshButtonTexts` 范本）、`Interaction/InteractionMissionView.cs`（设备切换检测范本）。

---

# 版本兼容层 — `Core/VersionCompat.cs`

**同一份源码，多版本编译。** `V` 静态类封装了全部跨版本 API 差异。每个方法用**累积阈值宏**（`MB2_GE_130`、`MB2_GE_140`）分支，而非精确版本匹配。

**宏体系**：csproj 编译时读 `Version.xml` 自动定义累积宏：
| 游戏版本 | 定义的宏 |
|----------|---------|
| v1.2.12 | `MB2_V1212` |
| v1.3.x  | `MB2_V1212` + `MB2_GE_130` |
| v1.4.x  | `MB2_V1212` + `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x  | 全部上述 + `MB2_GE_150` |

GE = "Greater or Equal"。代码按阈值从高到低写：`#if MB2_GE_150` / `#elif MB2_GE_130` / `#else`（v1.2.12）。

**使用纪律**：
- 凡是两个版本 API 不一样的调用，**一律走 `V.xxx()`，禁止在业务代码里裸写版本 `#if`**（Harmony 补丁 / override / type-level 差异例外，详见「不可迁 #if 注册表」）
- 新加 V 方法后**必须在两台电脑上分别编译通过**（v1.2.12 + Latest 各 build 一次）
- 1.4.6 和 1.4.7 的 API 经逐方法对比确认**完全一致**，`MB2_GE_130` 分支覆盖 v1.3.0 ~ v1.4.x 全系列

```csharp
// ── 位置（v1.2.12: .Position2D / Latest: .GetPosition2D）
V.Pos(party)              // Vec2 — MobileParty
V.Pos(settlement)         // Vec2 — Settlement
V.SetPos(party, pos)      // void — 设置 party 位置

// ── 部队移动（v1.2.12: party.Ai.SetMove* / Latest: party.SetMove*）
V.SetMoveTo(party, pos)           V.SetMoveEngage(party, target)
V.SetMoveToTown(party, settlement) V.SetMovePatrol(party, pos)
V.SetMoveEscort(party, target)     V.MoveTarget(party) → MobileParty

// ── 部队生命周期
V.MakeParty(id, component)          // CreateParty 3参/2参
V.DelParty(party)                   // RemoveParty / DestroyPartyAction
V.InitPartyPos(party, template, pos) // InitializeMobilePartyAtPosition Vec2/CampaignVec2
V.SetPartyName(party, name)         // SetCustomName / Party.SetCustomName

// ── Agent 控制
V.IsAgentAI(agent) → bool           V.SetAgentAI(agent)
V.IsAgentPlayer(agent) → bool       V.SetAgentPlayer(agent)
V.SetPlayerControlFrozen(agent, frozen)  // 冻结/恢复玩家控制（v1.2.12: ControllerType.AI/Player；Latest: AgentControllerType.AI/Player）

// ── 武器 / 动作
V.MainWpn(agent) → EquipmentIndex   V.OffWpn(agent) → EquipmentIndex
V.ActName(agent, channelIndex = 0) → string

// ── UI
V.NewLayer(order, name = null) → GauntletLayer  // 构造参数顺序反了
V.LoadMov(layer, name, vm)                       // 返回类型不同，v1.2.12 存 object

// ── 其他
V.GetStartTime() → CampaignTime      V.KingdomStr(kingdom) → float
V.EmptyText() → TextObject           V.NavMesh(scene, pos, out faceIndex) → bool
V.JoinDefect(clan, from, to)         V.GetEnemyKingdoms(kingdom) → IEnumerable<Kingdom>

// ── SetPartyAiAction 重载（v1.2.12: 2参 / v1.3.0+: 3~5参）
V.PatrolAround(party, settlement)     // GetActionForPatrollingAroundSettlement
V.RaidSettlement(party, settlement)   // GetActionForRaidingSettlement
V.BesiegeSettlement(party, settlement)// GetActionForBesiegingSettlement
V.EngageParty(party, target)          // GetActionForEngagingParty

// ── 导航网格 / 地图
V.NavMeshSnap(scene, ref pos)         // GetNavigationMeshForPosition in/ref 差异
V.AccessiblePointNear(wrapper, pos, r)→ Vec2  // GetAccessiblePointNearPosition Vec2/CampaignVec2
V.FaceIndex(wrapper, pos) → PathFaceRecord    // GetFaceIndex Vec2/CampaignVec2
V.CameraAnimate(mapState, pos, dur)   // StartCameraAnimation Vec2/CampaignVec2
```

**文件位置**：`Core/VersionCompat.cs`（约 420 行）。

**版本参考 DLL**：`Modules/1.2.12DLL/` 和 `Modules/1.4.6DLL/` 存放了另一版本的 DLL 副本，**仅 `ilspycmd` 反编译用，不参与编译**。在 v1.2.12 电脑上开发时查 `1.4.6DLL/` 看 Latest API，反之亦然。方法：`ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "Method"`。

---

# csproj 版本自动检测（累积阈值宏）

**一个配置通吃所有版本**，无需手动切换。编译时读 `Version.xml`，通过版本系列侦测 + `Or` 链自动定义累积阈值宏。

```xml
<!-- 版本系列侦测（精确到 minor） -->
<MB2_IsV12x Condition="$(MB2_VersionFileContent.Contains('v1.2.'))">true</MB2_IsV12x>
<MB2_IsV14x Condition="$(MB2_VersionFileContent.Contains('v1.4.'))">true</MB2_IsV14x>

<!-- 累积阈值：GE_130 = v1.3.x OR v1.4.x OR ... -->
<MB2_VersionDefines Condition="'$(MB2_IsV14x)' == 'true'">$(MB2_VersionDefines);MB2_GE_130;MB2_GE_140</MB2_VersionDefines>
<!-- 各配置引用 $(MB2_VersionDefines) -->
<DefineConstants>DEBUG;TRACE;$(MB2_VersionDefines)</DefineConstants>
```

**结果**：
| 电脑 | Version.xml | 定义的宏 |
|------|-----------|---------|
| v1.2.12 | `v1.2.12` | `DEBUG;TRACE;MB2_V1212` |
| v1.4.7  | `v1.4.7`  | `DEBUG;TRACE;MB2_V1212;MB2_GE_130;MB2_GE_140` |

**新增版本**：TaleWorlds 出新版本（如 v1.5.0）时：
1. 加 `<MB2_IsV15x>` 侦测行
2. 在已有 `GE_*` 的 `Or` 链中追加 `'$(MB2_IsV15x)' == 'true'`
3. 加 `MB2_GE_150` 的定义行

完整清单见 [plans/version-compat-plan.md](../version-compat-plan.md)。

**文件位置**：`ExampleMod.csproj` PropertyGroup 段。

---

# 不可迁入 V 的 #if（合规例外注册表）

以下类别的 `#if` **不能**封装为 `V.xxx()`，直接写在业务文件里是合法的。每次新增版本时必须逐条核查：

| 类别 | 典型文件:行号 | 原因 |
|------|-------------|------|
| override | `SafeLordPartyComponent.cs:41` 等 4 处 | 基类虚方法签名跨版本不同 |
| type | `MySubModule.cs:344` 等 5 处 | 字段类型 `IGauntletMovie`→`GauntletMovieIdentifier` |
| type | `PlayerDetentionBehavior.cs:9,312` | `GameOverlays.MenuOverlayType`→`GameMenu.MenuOverlayType` |
| Harmony | `InteractionMissionView.cs:2529,2559` | 补丁目标类/方法跨版本不同 |
| Harmony | `DebugLogger.cs:18` | `FillPartyStacks`→`FillPartyManuallyAfterCreation` |
| structural | `InteractionMissionView.cs:1909,2364` | 搜刮 Loot 流（`InventoryManager` 不可用） |
| structural | `WorldEventSimulator.cs:1716,1771` | `AreFacesOnSameIsland` 移除 |
| structural | `MyCommands.cs:1619` | stealth_debug 命令（依赖仅 Latest 存在） |
| namespace | `MyCommands.cs:30` | `SandBox.Missions.*` 命名空间仅 Latest 存在 |

完整注册表在 [VersionCompat.cs class doc comment](../../ExampleModVS/ExampleMod/ExampleMod/Core/VersionCompat.cs) 和 [version-compat-plan.md](../version-compat-plan.md)。

---

# Harmony 补丁版本兼容

Harmony 补丁在 `PatchAll()` 时如果找不到目标方法会**直接抛异常崩溃**。跨版本时必须处理：

1. **方法消失了** → `#if MB2_V1212` / `#else` 写两套，各版本补各自的目标
2. **方法签名变了** → 同上，用 `#if` 分支写不同的 Prefix/Postfix 参数
3. **编译时找不到类型**（如全局命名空间 vs using 冲突）→ 用 `AccessTools.Method("TypeName:MethodName")` 动态查找
4. **类型所在子命名空间变了** → `typeof()` 用完全限定名 + `#if` 分支

```csharp
// 场景 1：两版本各补各的方法
#if MB2_V1212
[HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
public static class DebugCrashPatch
{
    public static void Prefix(MobileParty __instance, PartyTemplateObject pt, int troopNumberLimit) { ... }
}
#else
[HarmonyPatch]
public static class DebugCrashPatch
{
    // AccessTools 运行时查找，绕过编译时类型不可见
    private static MethodBase TargetMethod() => AccessTools.Method("MobilePartyHelper:FillPartyManuallyAfterCreation");
    public static void Prefix(MobileParty mobileParty, PartyTemplateObject partyTemplate, int desiredMenCount) { ... }
}
#endif

// 场景 2：方法拆分了，每个版本补一个
#if MB2_V1212
[HarmonyPatch(typeof(AgentInteractionInterfaceVM), "SetAgent")]
public static class ChangeInteractionTextPatch
{
    public static void Postfix(AgentInteractionInterfaceVM __instance, Agent focusedAgent)
    {
        __instance.SecondaryInteractionMessage = "";
        __instance.PrimaryInteractionMessage = "";
    }
}
#else
[HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM), "SetHumanAgent")]
public static class ChangeInteractionTextPatch
{
    public static void Postfix(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM __instance, Agent focusedAgent)
    {
        // ⚠️ 不能 Clear()！ResetFocus() 会按索引访问 [0]/[1]，列表空了就 ArgumentOutOfRangeException
        __instance.PrimaryInteractionMessages?.ApplyActionOnAllItems(x => x.ResetData());
        __instance.SecondaryInteractionMessages?.Clear(); // Secondary 安全，只被 .Count 检查
    }
}
#endif
```

**排查方法**：`ilspycmd <DLL> -t <TypeName> | grep "方法名"` 确认目标方法在两个版本中的存在性和签名。如果 `typeof()` 编译报错但 ilspycmd 确定类型存在（可能是命名空间遮蔽），用 `AccessTools.Method` 绕过。

**注意**：编译通过不代表运行时能跑——Harmony 是运行时绑定的。必须在目标版本的实际游戏里测试。

**MBBindingList 坑点**：v1.4.6 里 `PrimaryInteractionMessages` / `SecondaryInteractionMessages` 从 `string` 变成了 `MBBindingList<T>`。清空内容时**不能 `Clear()`**——后续代码（如 `ResetFocus()`）可能按索引 `[0]`/`[1]` 直接访问，列表空了直接 `ArgumentOutOfRangeException`。正确做法是 `ApplyActionOnAllItems(x => x.ResetData())` 清空内容但保留占位。

---

# 原版对话流注入 — `Interaction/Dialogue/DialogueInjector.cs`

**JSON 驱动的原版 `ConversationManager` 对话注入器。当 NPC 对话需要走原版 UI（而不是 StoryDialogVM）时，优先用 JSON 注入，禁止硬编码 `DialogFlow` 链式调用。**

## 密谋命令系统玩法行（2026-08-07）

- `InteractionIds.Plot`（G 长按）= 密谋：对随从下达自然语言命令 → `PlanCommandFlow.Start(agent)`（available 条件 = 随从关系 `brain.Leader==Main || Following/ExecutingCommand`；密谋进行中该随从 Talk 行互斥移除）。
- `InteractionIds.StopPlan`（G 长按，与 Plot 同键）= 停止键：对执行中的随从喊停（`PlanExecutor.GetExecutorFor(agent) != null` 才显示；近距离当面冒泡 / 远距离密信，双通道对称）。**同键安全**：StopPlan 与 Plot 互斥（执行中显示 StopPlan、空闲显示 Plot），同一时刻只有一个 available → `LogBindingConflicts` 零冲突。
- 两行都在 `Settings.DefaultInteractions` 注册（config.json 可热重载）。

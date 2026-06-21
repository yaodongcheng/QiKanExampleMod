# 版本兼容中转站 — 执行计划 v3（从零重建版）

## 前提

用户执行 `git checkout .` 后，代码回到 `4bfac5f 补充csdn文章` 的干净状态。本计划所有步骤都从零开始，不依赖任何之前改动的残留。

---

## Phase 0：回退 + 验证

**用户操作**：
```bash
git checkout .
git status  # 确认 clean
```

**我验证**：
```bash
dotnet build -c Debug           # 预期：9 errors（原始6个预存错误 + 缺少的引用）
dotnet build -c Debug_v1.2.12   # 不能编译（还没配这个配置）
```

---

## Phase 1：重建 csproj（动 1 个文件）

添加以下内容到 `ExampleMod.csproj`：

1. `LangVersion` 8.0 → 12.0
2. 新增 `MB2_CORE_REF` / `MB2_SANDBOX_REF` / `MB2_NATIVE_REF` 三个属性，默认指向 `$(MB2_PATH)`
3. 新增 `Debug_v1.2.12` 和 `Release_v1.2.12` 两个 PropertyGroup，覆写三个 REF 属性指向 `Modules\1.2.12DLL\`
4. `Debug` / `Release` 的 `DefineConstants` 加 `LATEST`
5. 所有 `<HintPath>` 里的 `$(MB2_PATH)\bin\Win64_Shipping_Client\` → `$(MB2_CORE_REF)\`
6. `$(MB2_PATH)\Modules\SandBox\bin\...\` → `$(MB2_SANDBOX_REF)\`
7. `$(MB2_PATH)\Modules\Native\bin\...\` → `$(MB2_NATIVE_REF)\`
8. `TaleWorlds.GauntletUI.TooltipExtensions` 引用加条件（只给 v1.2.12）

**验证**：
```bash
dotnet build -c Debug_v1.2.12   # 预期：0 errors（1.2.12 DLL 下应该干净）
dotnet build -c Debug           # 预期：~40 类错误（Latest DLL 的 API 不兼容）
```

---

## Phase 2：直接源码修复 — 不改签名就不能编译的（动 6-7 个文件）

有些错误不修连一个文件都编译不过去（抽象方法未实现、override 了不存在的成员）。这些必须优先修：

| 文件 | 修改 | 方式 |
|------|------|------|
| `CommissionQuest.cs:41` | 删除 `override bool IsSpecialQuest => false;` | 删行 |
| `QuestManager.cs:275` | 删除 `override bool IsSpecialQuest => false;` | 删行 |
| `CommissionHubIssue.cs:57` | `CanPlayerTakeQuestConditions` 签名加 `out int requiredGold` | `#if LATEST` |
| `SafeLordPartyComponent.cs` | 加 `GetDefaultComponentBanner()` override | `#if LATEST` |
| `CustomPartyComponent.cs` | 加 `GetDefaultComponentBanner()` override | `#if LATEST` |
| `AttackTriggerMissionLogic.cs:214` | `OnRegisterBlow` 的 `GameEntity` → `WeakGameEntity` | `#if LATEST` |
| `InteractionMissionView.cs` | 加 `using ...Missions.Interaction;` | `#if LATEST` |

**验证**：
```bash
dotnet build -c Debug_v1.2.12   # 预期：0 errors
dotnet build -c Debug           # 预期：错误减少（以上7个文件的错误消失）
```

---

## Phase 3：创建 VersionCompat.cs（动 1 个新文件）

创建 `Core/VersionCompat.cs`，包含以下静态方法。每个方法内部用 `#if LATEST` / `#else` 分支。

**写入后立即编译两个配置验证**，确认 VersionCompat.cs 本身没有语法错误。

### 包含的方法清单

```
V.Pos(MobileParty)          → Position2D / Position.ToVec2()
V.Pos(Settlement)           → 同上
V.IsAgentAI(Agent)          → ControllerType / Controller
V.GetStartTime()            → CampaignStartTime moved
V.KingdomStr(Kingdom)       → TotalStrength / CurrentTotalStrength
V.EmptyText()               → TextObject.Empty / GetEmpty()
V.MainWpn(Agent)            → GetWieldedItemIndex / GetPrimaryWieldedItemIndex
V.OffWpn(Agent)             → GetWieldedItemIndex / GetOffhandWieldedItemIndex

V.SetMoveTo(party, pos)     → Ai.SetMoveGoToPoint → party.SetMoveGoToPoint
V.SetMoveEngage(p, target)  → Ai.SetMoveEngageParty → party.SetMoveEngageParty
V.SetMoveToTown(p, s)       → Ai.SetMoveGoToSettlement → party.SetMoveGoToSettlement
V.SetMovePatrol(p, pos)     → Ai.SetMovePatrolAroundPoint → party.SetMovePatrolAroundPoint
V.SetMoveEscort(p, target)  → Ai.SetMoveEscortParty → party.SetMoveEscortParty
V.MoveTarget(party)         → Ai.MoveTargetParty / party.MoveTargetParty

V.MakeParty(id, comp)       → CreateParty 3参 / 2参
V.DelParty(party)           → RemoveParty / DestroyPartyAction
V.JoinDefect(clan,from,to)  → ChangeKingdomAction 2参 / 3参

V.ActName(agent, ch)        → GetCurrentActionValue.Name / GetCurrentActionType
V.RayBlocked(from,to,dist)  → RayCast GameEntity / WeakGameEntity
V.NewLayer(order,name,clr)  → new GauntletLayer 参数顺序反了
V.LoadMov(layer,name,vm)    → LoadMovie 返回类型不同，存 object
V.NavMesh(scene,pos,out id) → GetNavigationMeshForPosition ref→in + 新参数
V.SaveNavMesh(...)          → GetNavigationMeshForPosition 的 bool 返回
```

**验证**：
```bash
dotnet build -c Debug           # VersionCompat.cs 本身无错误
dotnet build -c Debug_v1.2.12   # VersionCompat.cs 本身无错误
```

---

## Phase 4：逐文件修 call site

**纪律**：每次只动一个文件，改完立刻 `dotnet build -c Debug`，确认错误减少再改下一个。

每文件操作流程：
1. `dotnet build -c Debug` → 看该文件有哪些错误
2. `Read` 找到报错行
3. `Edit` 精确替换 → `V.xxx()`
4. 重复 1

### 文件顺序（报错数量从多到少）

| # | 文件 | 主要替换 |
|---|------|----------|
| 1 | `WorldEventSimulator.cs` | `Ai.SetMove*`→V + `.Position2D`→V + `RemoveParty`→V |
| 2 | `CommissionQuest.cs` | `Ai.SetMove*`→V + `.Position2D`→V + `CreateParty`→V |
| 3 | `HeroNemesisTracker.cs` | `Ai.SetMove*`→V + `.Position2D`→V + `CreateParty`→V |
| 4 | `MyBehavior.cs` | `Ai.SetMove*`→V + `.Position2D`→V + `CreateParty`→V |
| 5 | `WorldEventDatabase.cs` | `.Position2D`→V + `RemoveParty`→V |
| 6 | `WorldEventDirector.cs` | `.Position2D`→V |
| 7 | `WorldEventNotificationController.cs` | `.Position2D`→V |
| 8 | `CommissionGenerator.cs` | `.Position2D`→V |
| 9 | `StageDirector.cs` | `ControllerType`→V + `.Ai.SetMove*`→V + `NavMesh`→V |
| 10 | `StoryEngine.cs` | `ControllerType`→V + `GauntletLayer`→V |
| 11 | `StoryContext.cs` | `CampaignStartTime`→V |
| 12 | `NPCProfile.cs` | `TotalStrength`→V + `GetEnemyKingdoms`→V |
| 13 | `AgentControlHelper.cs` | `GetCurrentActionValue`→V + `ControllerType`→V |
| 14 | `InteractionMissionView.cs` | `ControllerType`→V + `GauntletLayer`→V + `LoadMovie`→V + Harmony patch |
| 15 | `CombatManager.cs` | `ControllerType`→V |
| 16 | `AgentBrain.cs` | `GetWieldedItemIndex`→V |
| 17 | `NpcSightSystem.cs` | `GameEntity` `out` → `V.RayBlocked()` |
| 18 | `VisualCommands.cs` | `ActionIndexValueCache`→V + `TextObject.Empty`→V |
| 19 | `DiplomacyIntents.cs` | `ChangeKingdomAction`→V |
| 20 | `MyCommands.cs` | `ActionIndexCache.Name`→V.ActionName |

### GauntletLayer 系列（集中处理）

这些文件都是同一个模式 `new GauntletLayer(N)` → `V.NewLayer(N)`：
- `BubbleSayMissionView.cs`
- `DuelMissionView.cs`
- `MySubModule.cs`
- `NinjaNotificationMissionView.cs`
- `CameraDebuggerView.cs`
- `SpringArmCameraView.cs`
- `InteractionMissionView.cs`（已在上面）

`LoadMovie` 返回值问题：把接收变量类型改为 `object` 或直接删掉不需要的变量。

### Harmony Patch — AgentInteractionInterfaceVM

`InteractionMissionView.cs` 里的 `PrimaryInteractionMessage` / `SecondaryInteractionMessage` 在新版变成 MBBindingList。需要改写法。

---

## Phase 5：最终验证

```bash
dotnet build -c Debug           # 预期：0 errors
dotnet build -c Debug_v1.2.12   # 预期：0 errors
```

进游戏：进场景 → 招募 NPC → 触发世界事件 → 接委托 → 切磋格挡 → 潜行调试指令。

---

## 不再犯的错

1. **绝不用正则批量替换 C# 代码** — 嵌套括号、lambda、变量名差异都不可靠
2. **一个文件改完立即编译** — 出错一眼定位
3. **VersionCompat.cs 先独立编译通过** — 不等到 call site 都改了才发现 V 方法本身有语法错
4. **两个配置一起验证** — 不只在 Latest 下编译

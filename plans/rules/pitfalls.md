# 坑点速查（疑难杂症）

> **按需加载**：不是每次会话必读。踩到诡异症状（AccessViolation、引擎 native 崩溃、状态错乱）时来这里查有没有同款坑。
> 每条格式：**症状 → 根因 → 规避**。根因尽量记到反编译确认的调用链，别凭名字猜。

---

## 玩家攻击 NPC 后无法攻击/格挡（移动正常）→ NinjaNotification 圆环拦鼠标

**症状**（实机 2026-08-11 16:15 复现）
- 玩家攻击任何 NPC（随从/守卫都一样）：**第一刀能打出去，之后左键攻击、右键格挡全部失效**；移动（WASD）正常；可正常掏武器。
- 8-09 18:00 版 DLL 同操作正常（能持续战斗）——纯代码回归。
- 与 CombatManager 移队**无关**（旧 DLL 同样 Spar 移队、犯罪+5，玩家照常战斗——已对照验证）。

**根因**（代码链实锤）

```
玩家攻击 NPC（第一刀命中）
  └─ NPC 说台词 → AgentHudMissionView.AgentSay
        └─ NearbyFeed.Forward（🔴 8-11 b086b91 新增：场景冒泡转发到 IM 附近频道）
              └─ ImChatStore 广播 → ImChatView.OnMessageArrived → NotifyIncoming
                    └─ NinjaNotificationManager.Show（通知圆环）
                          └─ _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse)
                                └─ 🔴 拦截鼠标左键（攻击）/右键（格挡）/滚轮，键盘（移动）不拦
```

- 通知层设计初衷：圆环可点击（打开 IM）→ 拦鼠标防穿透。但 NPC 被打必然说台词（冒泡）→ 8-11 接入 nearby 频道后**任何战斗都会弹通知**。
- **NinjaNotificationVM 无自动消失**（无 timer）→ 通知永久挂着 → 鼠标输入永久被吞，直到点掉/换场景。
- 旧 DLL 的 AgentSay 没有 `NearbyFeed.Forward`（b086b91 才加）→ 无此触发路径 → 正常。

**规避**
- `NinjaNotificationManager.Show` 入口加守卫：**`if (Mission.Current != null) return;`**（Mission 内一律不弹；消息仍在频道里，IM 面板可看；大地图保留）。
- 后续若想在 Mission 内恢复通知：通知层输入限制必须降级（如战斗中 `InputUsageMask.None`）或加自动超时消失——**任何 Gauntlet 层只要含 Mouse 拦截，在战斗场景都是攻击/格挡杀手**。
- 排查口诀：**"移动正常、仅鼠标键失效" = 有 Gauntlet 层拦了 Mouse**——先查屏幕上挂着的层（InputRestrictions），别往 Agent 控制/队伍方向查（这次移队是烟雾弹）。

---

## 引擎回调栈内（OnAgentHit/OnRegisterBlow）同步触发 native 战斗状态重入 → AccessViolation

**症状**（实机 2026-08-13 切磋判负崩溃）
- `System.AccessViolationException`，HResult=0x80004003，`Source=<无法计算异常源>`（托管栈丢失）——异常从 native 泄漏上来。
- 崩溃点在游戏主循环 `Mission.TickMission → TickMissionAux(..., asyncAITick: true)`，看不到 mod 侧堆栈。
- 时机：切磋判负瞬间（OnAgentHit 内执行收场时）；**非必现**（竞态）——同一套代码早前跑过没崩。

**根因**（代码链实锤）

```
OnAgentHit（引擎 HandleBlow 处理栈内）── 判负
  └─ EndDuel 同步收场：
       SetMortalityState ×2 / Health 写入（native）
       └─ SendEventToAgent("event_stop_combat")  ← 同步（brain.ReceiveEvent 直接分发）
            └─ ClearAllActions
                 └─ FightEnemyAction.OnEnd
                      └─ CombatManager.EndFight
                           └─ RestoreSideFightMembers（全员 SetTeam）
                           └─ InterruptCombatMotion（SetAttackState / SetMovementDirection / SetScriptedCombatFlags）
                           └─ SetTargetAgent(null)
       └─ 恢复 Mortal（native）
```

- 引擎的 blow 处理（HandleBlow）中途，我们**重入修改同一 agent 的战斗状态**（队伍/攻击状态机/索敌）——native 内部状态机被打断，后续访问损坏内存。
- `asyncAITick: true` 时 agent 的行为 AI 在**后台线程** tick——主线程 blow 栈内改战斗状态与后台线程并发 → 竞态 AccessViolation（非必现的原因）。
- ⚠️ 托管层看起来"按顺序调用"，崩溃却随机——因为损坏发生在 native 侧，下一次任意 native 调用才炸（栈丢失）。

**规避**
- **引擎回调栈内（OnAgentHit/OnRegisterBlow/OnAgentRemoved）只允许两类操作**：① 托管状态标记（bool/引用/时间戳）；② 保命类极小 native 操作（判负回血 `Health = HealthLimit` 必须在栈内——引擎 HandleBlow 在 OnAgentHit 之后检查 `if (Health < 1f) Die()`，延后 = 必死）。
- **禁止在回调栈内同步触发任何会重入 agent 战斗状态的调用链**：`SetTeam` / `SetAttackState` / `SetMovementDirection` / `SetScriptedCombatFlags` / `SetTargetAgent` / `EndFight` / `StopAgentCombat`。
- 收场类逻辑延后一帧：`_pendingDuel` 标记 → 下一帧 `MissionBehavior.OnMissionTick` 执行（正常 tick 栈，与引擎其他主线程操作同线程安全）。
- 落地范例：`Combat/AttackTriggerMissionLogic.cs` → `OnDuelLoser`（blow 栈内保命 + 播报）/ `EndPendingDuel`（下一帧收场：停战事件 → 恢复 Mortal → 冷却登记）。
- 判别口诀：**栈丢失的 AccessViolation + 崩溃点在主循环 = native 内存被更早的调用破坏了**——回头查"最近一次引擎回调里做了什么"，而不是查崩溃点本身。
- 附带收益：拆段后"判负那一击的晚到伤害事件"到达时行为尚未清空（chivalry 早退不反击），防反击冷却反而更稳——回调栈内收场本身也是事件时序的隐患。

---

## 对尸体/昏迷 Agent 调 `UpdateSpawnEquipmentAndRefreshVisuals` → AccessViolation

**症状**
- `agent.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment)` 抛 `System.AccessViolationException`（读写受保护内存）。
- 只在**死人/昏迷**的 Agent 上发生；活人正常。
- ~~"全部拿走/扒光"不崩~~ **（2026-08-02 修正：全部拿走也会崩！）**，"自己挑选只拿一部分"也崩；"一件没拿"（不触发刷新）不崩。

**根因**（反编译 `TaleWorlds.MountAndBlade.Agent` 确认）

```
UpdateSpawnEquipmentAndRefreshVisuals(newEquipment)
  └─ WieldInitialWeapons()                        // 仅当 newEquipment 里还留着武器才往下走
        └─ TryToWieldWeaponInSlot(GetPtr(), ...)   // 纯 native，无 IsActive 守卫
```

- 死人骨骼已交给物理系统（ragdoll），native 方法内部不止 `WieldInitialWeapons` 碰骨骼——**detach 旧 mesh / 刷新骨骼引用**阶段也会操作已被物理接管的 ragdoll 内存 → 崩。
- ~~"全部拿走"安全的真正原因不是时机，而是武器被拿光、`WieldInitialWeapons` 空操作~~ **（2026-08-02 修正：此假设错误。即使 newEquipment 里空无一物，native 方法仍在 ragdoll 骨骼上崩——说明崩溃点不止武器 wield 一个环节。）**

**规避**
- **正解：对 `!agent.IsActive()` 的 Agent（死亡/昏迷）直接跳过 `UpdateSpawnEquipmentAndRefreshVisuals`**。死人不需要刷新外观，`_lootedCorpses` 已防重复搜刮，尸体很快被引擎清理。
- 活人不受限制，照常刷新。
- 落地范例：`Stealth/StealManager.cs` → `StripAgentEquipment`（`if (agent.IsActive() && ...)` 守卫，Inactive agent 跳过整段 native 调用）。
- ~~旧规避（清空武器槽）~~ 已在 2026-08-02 废弃——清空武器槽不足以防止崩溃。

---

## `InitializeMobilePartyAtPosition + Clear()` → 0xc00000ff 栈溢出

**症状**
- `Bannerlord.exe` 崩溃在 `ntdll.dll`，异常码 **`0xc00000ff` = `STATUS_STACK_BUFFER_OVERRUN`**（栈金丝雀检测到越界写）。
- 不在任何 mod DLL 里崩，而是在 Windows 系统调用层——栈被更早的 native 操作破坏，下一次系统调用时才触发 canary。
- 崩溃时机随机：可能在生成 party 后几秒到几分钟，游戏 tick 更新 party 时触发。

**根因**
```csharp
// ❌ 危险模式：
party.InitializeMobilePartyAtPosition(template, party.Position2D);  // native，按模板在本地分配 N 个 troop 槽
party.MemberRoster.Clear();      // 只清 C# 管理侧列表，本地内存大小仍为 N
party.MemberRoster.AddToCounts(looterTier1, M);  // 写入 M 个 troop（M ≠ N）
// → 本地 buffer 大小与写入量不匹配 → 引擎后续读 roster 时越界写栈 → 0xc00000ff
```

- `InitializeMobilePartyAtPosition` 是 native C++ 方法，按 `PartyTemplateObject` 在本地堆/栈上分配 troop 数组。
- `MemberRoster.Clear()` 只操作管理侧（C# wrapper），**不会同步释放/缩小本地 buffer**。
- 随后 `AddToCounts` 往本地 buffer 写不同数量 → 如果 M > N，写越界；如果 M < N，留下未初始化的空洞。

**规避**
- **`MobileParty.CreateParty` 已经返回合法空 party**，不需要再调 `InitializeMobilePartyAtPosition`。
- 自定义部队直接用 `Clear()` + `AddToCounts()` 即可，跳过模板初始化。
- 落地范例：`WorldEventSimulator.FillPartyTroops` / `FillGenericPartyTroops`（删掉了 `InitializeMobilePartyAtPosition` 调用）。
- 如果确有场景需要模板初始化，则 **不要 Clear**，在模板部队之上叠加即可。

---

## WorldEvent → 委托匹配失败：职业/venue 过滤与事件匹配的断层

**症状**
- 玩家在城镇看到事件相关 NPC 头上有 `!`，但问到的委托和该事件完全无关。
- 日志：`[CommissionIntent] RequestCommission Evaluate` 显示 `Show`，但生成的委托全是随机类型。

**根因**
事件匹配（`TryMatchWorldEvent`）和委托可用性检查（`GetAvailableDefsForHero`）之间存在两层过滤，事件匹配的 CommissionDef 可能根本**没进入候选池**：

```
HasCommissionsFor → IsHeroInNearbyWorldEvent → count=1, 显示 "!"
GenerateCommissions → GetAvailableDefsForHero
  ├─ ① ValidGiverOccupations 不含此 NPC 职业 → 过滤掉
  ├─ ② IsVenueMatch 不含此 NPC 职业 → 70% 概率过滤掉（30% 随机放行）
  └─ ③ 剩余 defs 走到 GenerateCommissionData → TryMatchWorldEvent
       → 但事件匹配的 def 早已在 ①/② 被过滤，根本不会执行到这里
```

- Kidnapping 匹配 BountyHunt + DecoyMission，但这俩的 `ValidGiverOccupations` 和 `IsVenueMatch` 都不含 `RuralNotable`。
- 受害人 NPC（RuralNotable）能显示 `!`（因为 `IsHeroInNearbyWorldEvent` 只看事件存在与否），但**开不出匹配的委托**（因为职业过滤把事件相关 def 全拦掉了）。
- 这是一条设计原则：**事件系统的"可见性"和"可用性"必须共享同一份职业门禁逻辑，否则就会出现看得见但摸不着的断层**。

**规避**
- 新增 WorldEvent 类型时，反查其 `MatchingCommissions` 列表，确认每个匹配的 CommissionDef 的 `ValidGiverOccupations` 和 `IsVenueMatch` 都覆盖了目标 NPC 可能的职业。
- 事件受害人最可能是 `RuralNotable` / `Headman`，这两个职业应始终在事件相关委托的职业白名单中。
- 落地范例：
  - [CommissionData.cs](ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionData.cs) — BountyHunt 的 `ValidGiverOccupations` 加了 `RuralNotable`
  - [CommissionGenerator.cs](ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionGenerator.cs) — `IsVenueMatch` 的 BountyHunt 簇加了 `RuralNotable`
- 另一个匹配阻断点：`TryMatchWorldEvent` 里 generic instigator（找不到真人 bandit）直接 `return false`。修复为设置 `TargetSettlementId` 代替 `TargetHero`，让委托叙事层通过 `WorldEventId` 输出事件文本。

---

## .NET Framework 4.8 不支持的 API

**症状**：编译错误 `CS0117: "Math"未包含"Clamp"的定义` / `CS1061: "MobileParty"未包含"Leader"的定义`

**根因**：Bannerlord 基于 .NET Framework 4.8（非 .NET Core）。以下 API 不存在：
- `Math.Clamp(int, min, max)` → 使用项目中已有的 `ClampInt(value, min, max)`（`WorldEventSimulator.cs` 末尾）
- `MathF.Abs() / MathF.Clamp()` → 使用 `Math.Abs()`（但 `StoryDialogVM` 里已用了 `MathF`——那是 TaleWorlds 自带的兼容层，OK）
- `MobileParty.Leader` → Bannerlord API 属性名是 **`LeaderHero`**
- `IMapStateHandler.TeleportCameraToPosition()` → 不存在，只有 `TeleportCameraToMainParty()`。镜头移动用 `mapState.Handler.TeleportCameraToMainParty()` + `InformationMessage` 提示方向

**规避**：写新代码时，不确定 API 名称先 `grep` 项目中的已有用法；不确定是否存在先反编译 DLL。

---

## `GameStateManager` 需要 `using TaleWorlds.Core`

**症状**：`CS0103: 当前上下文中不存在名称"GameStateManager"`

**根因**：`GameStateManager` 在 `TaleWorlds.Core` 命名空间，不在 `TaleWorlds.CampaignSystem.GameState`。两个 using 都要加。

---

## Edit 工具 `replace_all: true` 可能吃掉其他代码

**症状**：一次 Edit 后大片方法消失，后续出现 `CS1022: 应输入类型、命名空间定义或文件尾`。

**根因**：`replace_all: true` 匹配到的 `old_string` 如果不是全局唯一的，会在**所有匹配位置**做替换。如果某处的上下文不同（缩进不同、注释不同），替换结果可能破坏代码结构。

**规避**：
- `replace_all` 前确认 `old_string` 在所有匹配位置**逐字符一致**（含缩进、注释）
- `old_string` 尽量包含足够的上下文行（前后各 2-3 行）以保证唯一性
- 如果文件是 untracked（`??`），git checkout 无法恢复——只能在 IDE 里 Ctrl+Z
- 一次改多处时，用多次独立 Edit 比一次 replace_all 更安全

---

## CampaignEvents 委托签名必须完全匹配

**症状**：`CS0407: "bool XXX.OnCheckForIssue(Hero)"的返回类型错误`

**根因**：`CampaignEvents.OnCheckForIssueEvent` 的委托签名是 `void`。给事件处理函数加 `bool` 返回值会导致签名不匹配。

**规避**：事件处理器保持原始签名。需要返回值的逻辑包装成内部方法（如 `TryAddIssue` → 事件处理器 `OnCheckForIssue` 调它）。

---

## 模态 UI 键盘输入拦不住（空格穿透到游戏）

**症状**：自定义 Gauntlet 模态层打开时按空格，UI 响应了，主角**同时也跳起来**。层 `InputRestrictions.SetInputRestrictions(true, InputUsageMask.All)` 看着像"我在管输入"，实际什么都拦不住。

**根因**（反编译 `TaleWorlds.ScreenSystem.ScreenManager` 事件分发确认）

- 键盘事件**不走 mask**——分发路径只看 `FocusTest(layer)`（即 `FocusedLayer == layer`）。模态层加上去后 `FocusedLayer` 仍是 `MissionScreen`，键盘照常进游戏。
- `InputUsageMask` 的 `Keyboardkeys=4` 位在键盘分发代码里**根本不被检查**（mask 只管鼠标按钮/滚轮的命中消费）。
- **剥 `Agent.Main.EventControlFlags` 也无效**：原生玩家控制器在**托管 tick 之后**才写动作标志——`OnMissionTick` 里清零，它随后再写，剥了个寂寞（已实机验证）。
- `ControllerType.None` 同样**无效**（已实机验证）——MainAgent 疑被原生特判，无控制器时仍处理其输入。

**规避**

- 正解 = **切控制器** `Agent.Main.Controller = ControllerType.AI`（v1.2.12）：输入处理权移交 AI 组件，主角无指令源原地待机。恢复 `Player` 时 `Mission.MainAgent` 自动重指 + 广播 `OnAgentControllerSetToPlayer`，官方可逆。
- 封装：`V.SetPlayerControlFrozen(agent, bool)`（`Core/VersionCompat.cs`）；接线范本：`InteractionMissionView.FreezePlayerControl/UnfreezePlayerControl`（`_playerControlFrozen` 幂等标志 + Finalize 兜底）。
- 安全性：`AgentBrain.Tick` 对 `Owner == Agent.Main` 早退，本 mod brain 不会接管切了 AI 的主角；SandBox 官方有同款切 AI 用法。
- 空格/ESC 的 `Input.IsKeyPressed` 轮询是原始设备状态，与 mask/控制器无关，照常可用。
- Latest（1.4.6）`ControllerType` 嵌套枚举改为顶层 `AgentControllerType`，但 `agent.Controller` setter 仍在，等效写法 `AgentControllerType.AI`/`AgentControllerType.Player`。

---

## `Mission.RemoveTimeSpeedRequest` 对未知 ID 抛异常

**症状**：子弹时间/击杀镜头类时间减速收尾时 `ArgumentOutOfRangeException`，`RemoveAt(-1)`。

**根因**（反编译 `TaleWorlds.MountAndBlade.Mission` 确认，v1.2.12 + v1.4.6 同实现）

```csharp
public void RemoveTimeSpeedRequest(int timeSpeedRequestID)
{
    int index = -1;
    for (...) { if (_timeSpeedRequests[i].RequestID == timeSpeedRequestID) index = i; }
    _timeSpeedRequests.RemoveAt(index);   // 找不到 → index 仍 -1 → 炸
}
```

没有任何"未知 ID 为 no-op"的幂等保护。

**规避**

```csharp
// 先查后删（GetRequestedTimeSpeed 两版本签名一致）：
if (mission.GetRequestedTimeSpeed(requestId, out _))
    mission.RemoveTimeSpeedRequest(requestId);
// 再配一个 bool 标志记录"我加过"，关闭路径幂等收口 + OnMissionScreenFinalize 兜底。
```

落地范本：`InteractionMissionView.StartStealSlowmo/StopStealSlowmo`（`_stealSlowmoActive` + requestId 常量）。

---

## Gauntlet `ItemGap` 静默无效（ListPanel 间距）

**症状**：`<ListPanel ItemGap="20">` 子项挤在一起，间距完全不生效；Gauntlet 对未知属性**不报错**，静默忽略。

**根因**（反编译 `TaleWorlds.GauntletUI.Layout.StackLayout` 确认）：`StackLayout` 只有 `LayoutMethod` 和 `DefaultItemDescription`，**没有任何间距/Gap 属性**。`ItemGap` 是臆造属性。

**规避**：间距写在**子项**上——横排 `MarginRight`、竖排 `MarginBottom`（项目先例：`MyCustomPopup.xml` 按钮 `MarginRight="10"`）。ItemTemplate 内同样适用（末位多一个边距，对 CoverChildren 居中行影响可忽略）。

---

## `Team != null` 挡不住 `Team.Invalid` → `IsEnemyOf` NRE

**症状**
- `System.NullReferenceException` 抛在 `TaleWorlds.MountAndBlade.Team.IsEnemyOf` **内部**，即使调用前已判 `agent.Team != null`。
- 典型触发：地牢（prison location）里与守卫对话/攻击守卫——守卫的 Team 是无效单例。

**根因**（反编译 `TaleWorlds.MountAndBlade.Team` / `MBTeam` 确认）

```csharp
// Team 有一个 non-null 的"无效"单例：
public static Team Invalid => _invalid ??= new Team(MBTeam.InvalidTeam, BattleSideEnum.None, null);
// MBTeam.InvalidTeam = new MBTeam(null, -1)  →  _mission = null, Index = -1

public bool IsEnemyOf(Team otherTeam) => MBTeam.IsEnemyOf(otherTeam.MBTeam);
// MBTeam.IsEnemyOf 内部：
//   MBAPI.IMBTeam.IsEnemy(_mission.Pointer, ...)   // 💥 _mission = null → NRE
```

- `agent.Team != null` 对 `Team.Invalid` **通过**（它是真实对象），但内部 mission 引用是 null。
- 地牢等无阵营场景，守卫/平民 Agent 的 Team 就是这个单例；玩家 MainAgent 在部分特殊 Mission 里也可能是。

**规避**
- 任何 `Team` 操作（`IsEnemyOf` / `SetIsEnemyOf` 等走 MBTeam 的 API）前必须**双重检查**：

```csharp
if (agent.Team != null && agent.Team.IsValid   // IsValid => MBTeam.Index >= 0，双版本公开 API
    && other.Team != null && other.Team.IsValid)
    agent.Team.IsEnemyOf(other.Team);
```

- 落地范例：`Interaction/Intents/IntentContext.cs`（ctor 士兵敌对判定）、`Combat/AttackTriggerMissionLogic.cs`（OnAgentHit 两处）、`Combat/CombatManager.cs`（仇恨锁定）。
- 无效 Team 的语义兜底：按中立处理（非敌非友），符合"未被激怒前非敌"的直觉。

---

## PowerShell `Set-Content` 不指定 `-Encoding utf8` → 中文乱码

**症状**
- `Set-Content` / `Out-File` 写入 C# 文件后，所有中文字符（注释、字符串字面量）变成乱码（`����` / mojibake）。
- `git diff` 里 `-` 行中文正常、`+` 行乱码 —— 文件已被重编码为系统 ANSI codepage（中文 Windows = GBK）。
- `Read` 工具看到的也是乱码。

**根因**
- Windows PowerShell 5.1 中 `Set-Content` / `Add-Content` **默认使用系统 ANSI codepage**（中文 Windows = GBK/CP936），而非 UTF-8。
- `Out-File` 默认 UTF-8 with BOM，但也可能在不带参数时走系统 codepage。
- 项目 C# 文件全部是 **UTF-8** 编码，一旦被 `Set-Content`（无 `-Encoding`）写入 → GBK 编码 → 中文字节不可逆损坏。
- `[System.IO.File]::WriteAllLines(path, lines, [System.Text.Encoding]::UTF8)` 也有坑：Windows PowerShell 5.1 中不带 BOM 的 UTF-8 写入，`git diff` 可能把整个文件标为修改（行尾/编码差异）。

**规避**
- **任何写文件的 PowerShell 命令，一律显式加 `-Encoding utf8`**：
  ```powershell
  $content | Set-Content -Path $file -Encoding utf8 -NoNewline
  $content | Out-File -FilePath $file -Encoding utf8
  ```
- **编辑包含中文的文件时，绝对不要用 PowerShell**，用 `Edit` 工具（它保留原文件编码不变）。
- **如果已经损坏**：`git restore <file>` 恢复（已跟踪文件）；或 `git show HEAD:<path> > file`（新文件）。
- ⚠️ 注意 `>` 重定向在 PowerShell 5.1 中默认 **UTF-16 LE**！用 `git show HEAD:path | Out-File -Encoding utf8 file.cs` 代替。
- 2026-07-29 实踩此坑：对 `ConversationEntryPatch.cs` 跑 `Set-Content` 未加 `-Encoding utf8` → 全文中文变乱码 → 只得 `git restore` 后重新 Edit 逐块应用。

---

## `ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker)` 模板 NPC 为 null → NRE

**症状**
- `System.NullReferenceException` 抛在 `TaleWorlds.CampaignSystem.GameComponents.DefaultDiplomacyModel.GetHeroesForEffectiveRelation` 内部。
- 堆栈追溯到 `ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, ...)` 调用，`ctx.Speaker` 为 null。
- 触发场景：与模板 NPC（村民/守卫等无 HeroObject 的 CharacterObject）对话中，Intent 的 OnSuccess/OnFail/OnInstant 裸调 `ApplyPlayerRelation`。

**根因**
- 模板 NPC 的 `CharacterObject.HeroObject == null`，`IntentContext.Speaker` 为 null。
- `ChangeRelationAction.ApplyPlayerRelation(Hero, int)` 第一个参数不能为 null——底层 `GetHeroesForEffectiveRelation` 会直接 NRE。
- 铁律 8 要求所有互动入口兼容模板 NPC，但 `ApplyPlayerRelation` 只对 Hero 有意义（模板 NPC 无好感度系统）。

**规避**
- 任何 `ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, ...)` 调用前必须 null guard：
  ```csharp
  if (ctx.Speaker != null)
      ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -10, false, true);
  ```
- 或用 C# 模式匹配：
  ```csharp
  var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
  if (npc is Hero n)
      ChangeRelationAction.ApplyPlayerRelation(n, -5, false, true);
  ```
- 模板 NPC 没有好感度系统，跳过关系惩罚是合理的——惩罚通过其他机制体现（事件升级、Infamy 等）。
- 新增 Intent 时检查所有 `ApplyPlayerRelation` / `ApplyInternal` 调用是否已 null guard。
- 落地范例：`AccountabilityIntents.cs` 里 `CharmDefenseIntent.OnFail` 曾裸调 → 加 `if (ctx.Speaker != null)` 守卫（2026-07-29 实踩）。

**全量扫描结论**（2026-07-29）：
- 项目 35 处 `ApplyPlayerRelation` 调用中仅此一处漏守卫。其余已通过 `if (ctx.Speaker != null)` / `if (npc is Hero n)` / `if (authority != null)` / `Evaluate` 的 `IsHero` 检查覆盖。

---

## `FindOnGoing` 的 `??` 语义：旧事件遮蔽 Pending → 对话选项消失

**症状**
- NPC 目击玩家犯罪后主动质问，对话注入成功、NPC 报了价，但 **"行，就按这个价"（PayRestitution）选项不显示**。
- 日志：`[IntentEval] PayRestitution → Hide (stage=Emerging, suspectIsPlayer=False)`
- 但日志前几行明确记录：`[RegisterWitness] … witnessed crime → WorldEvent … Stage → Active (suspect=player)` —— 玩家刚被目击，事件理应是 Active + suspect=player。
- 同时存在多个同村 Misconduct 事件（旧暗罪 Emerging + 新目击 Active）。

**根因**

`WorldEventStore.FindOnGoing` 三个重载都是 `stored ?? pending` 模式：

```csharp
// 旧实现：
return _allEvents.FirstOrDefault(e => ...) ?? MatchPending(settlementId);
```

`??` 只在 `stored == null` 时走 Pending。如果同村存在一个旧事件（在 `_allEvents` 里排在前面 → `FirstOrDefault` 命中），**Pending 就永远不会被选中**，即使 Pending 才是刚被目击到的、嫌犯=玩家、阶段更靠前的活跃事件。

调用链：
1. `FindOnGoing` → 返回旧事件 (Emerging, suspect=null)
2. `AccountabilityIntents.cs:172` → `stage==Emerging && SuspectHeroId != player` → **Hide**
3. 玩家在对话里看到 NPC 报了价，但没有"接受"按钮——只能砍价（失败后回到报价节点还是没有接受按钮）、或者拒赔——死循环

**规避**

用 `PickBest` 替代 `??`：同时取 stored 和 pending，选更相关的返回：
- suspect=player 优先（被目击的事件 > 匿名暗罪）
- 同 suspect 则阶段高的优先（Confrontation > Active > Emerging > Dormant）

```csharp
// 三个 FindOnGoing 重载统一改为：
var stored = _allEvents.FirstOrDefault(...);
var pending = MatchPending(...);
return PickBest(stored, pending);

// PickBest:
static WorldEvent PickBest(WorldEvent stored, WorldEvent pending)
{
    if (ReferenceEquals(stored, pending)) return stored;
    if (stored == null) return pending;
    if (pending == null) return stored;
    if (pending.SuspectIsPlayer && !stored.SuspectIsPlayer) return pending;
    if (stored.SuspectIsPlayer && !pending.SuspectIsPlayer) return stored;
    if (pending.Stage > stored.Stage) return pending;
    return stored;
}
```

- `ReferenceEquals` 守卫：当 Pending 已持久化进 `_allEvents` 时，stored 和 pending 是同一个对象，直接返回。
- 落地：`WorldEvent/WorldEvent.cs` → `FindOnGoing`（三个重载 + `PickBest`）。
- 2026-07-30 实踩：地牢暗罪 (Emerging) + LordsHall 目击 (Active)，旧事件遮蔽新事件 → PayRestitution 消失。

---

## `AddDialogLineMultiAgent` 不设 `RelatedObject` → NPC 台词残留 + 跨对话抢占 `start` token

**症状**
- 一场对话结束、下一场对话开始时，NPC 说的台词是**上一场对话的旧文本**（如旧事件的目标名、旧赔款金额）。
- 引擎在 `start` token 上选中了旧 NPC 台词 → 输出到旧的 outputToken → 新对话的玩家选项永远走不到，玩家只有原版选项（"我建议我们两家联姻。"之类）。
- 日志：`[DialogueInjector] RemoveRelatedLines label="crime_xxx"` 报了"清理完毕"，但旧台词照样出现。
- 注入日志显示正确的新文本，但 `[VanillaDialog]` 日志显示引擎实际播放的是旧文本。

**根因**（反编译 `TaleWorlds.CampaignSystem.dll` 确认）

```
ConversationSentence 构造函数:
  relatedObject 参数默认 null → RelatedObject = relatedObject

AddDialogLineMultiAgent(id, inputToken, outputToken, text, condition, consequence,
                        agentIndex, nextAgentIndex, priority, clickableConditionDelegate):
  → new ConversationSentence(..., 0u, priority, agentIndex, nextAgentIndex)
  → relatedObject 没传！默认为 null

RemoveRelatedLines(object o):
  → _sentences.RemoveAll(s => s.RelatedObject == o)
  → 匹配 RelatedObject，但 NPC 台词全是 null → 永远匹配不上 → 永远清不掉
```

- PlayerLine 走 `DialogFlow.AddPlayerLine` → `cm.AddDialogFlow(df, owner)` — 这个路径**会**把 `owner` 当 `relatedObject` 传进 `ConversationSentence` 构造函数 → 能清掉。
- NPC 台词走 `AddDialogLineMultiAgent` — 这个 **没有 `relatedObject` 参数** → 所有 NPC 台词 `RelatedObject = null` → `RemoveRelatedLines` 匹配不到 → 永远残留在 `_sentences`。
- 残留的旧 NPC 台词与新 NPC 台词同 token（都是 `start`）、同 priority（200），引擎按 `_sentences` 列表顺序选第一个 → 旧台词抢占。

**注意**：`AddDialogLineMultiAgent` 的最后一个参数是 `OnClickableConditionDelegate clickableConditionDelegate`，不是 `object relatedObject`。不要把 InjectOwner 当成最后一个参数传进去——会被解释为点击条件委托，类型不匹配且无效。

**规避**

在 `AddDialogLineMultiAgent` 返回后，用反射把 `ConversationSentence.RelatedObject` 补设上：

```csharp
ConversationSentence sentence = cm.AddDialogLineMultiAgent(id, inputToken, outputToken,
    textObj, condition, null, 0, -1, priority);

// 反射补设 RelatedObject（AddDialogLineMultiAgent 不传 relatedObject → 默认为 null）
if (owner != null && sentence != null)
{
    try
    {
        typeof(ConversationSentence)
            .GetProperty("RelatedObject",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.SetValue(sentence, owner);
    }
    catch { }
}
```

- 落地：`Interaction/Dialogue/DialogueInjector.cs` → `AddNodeNpcLine`（所有 NPC 台词注册的统一出口）。
- 修复后 `RemoveRelatedLines` 原生逻辑直接生效，NPC 台词不再残留。
- 2026-07-31 实踩：town_ES4 对话结束后，village_ES3_2 新对话 NPC 仍在说 "你把帝国步兵打晕了"

---

## GauntletUI XML `Id`/`Tag` 在父级 `<Window>` 上设 → 子 `ListPanel` 读不到

**症状**：Harmony patch 中沿 `ParentWidget` 链找 `Id="LWN"` 永远找不到。`Tag` 同样不生效。但去掉守卫全局 swap 后自定义 UI 正常——ListPanel 确实经过 patch，只是标识方式不对。

**根因**：
1. `<Window>` 是 `CustomWidgetType`，内部结构导致 `ParentWidget` 链不保证贯通
2. `Tag` 属性：`widget.Tag = Tag` 代码存在但不被 XML 解析器填充
3. `Id` 设在父 `<Window>` 上，子 `ListPanel` 是不同 widget，它的 `widget.Id` 不是父的 Id

**规避**：`Id="LWN_xxx"` 直接写在目标 `<ListPanel>` 自身上，patch 里 `widget.Id.StartsWith("LWN")` 直接命中。
- 落地：`GUI/StackLayoutVerticalSwapPatch.cs` + `InteractArea.xml` / `AgentHudNearby.xml`
- 2026-08-01 实踩：四种方案轮番失败（Id on Window / Tag on Window / Tag on ListPanel / HashSet），最终 `Id on ListPanel + StartsWith` 成功。

---

## Harmony Patch `ConversationMissionLogic.OnMissionTick` → v1.4.7 角色模型横置

**症状**
- v1.4.7：新建战役角色创建界面人形横过来（躺平），进游戏后物品界面也是如此
- v1.2.12：完全正常
- 注释 `harmony.PatchAll()` 后恢复正常
- 二分排查锁定凶手：`SuppressVanillaConversationMissionPatch`（`Interaction/Dialogue/ConversationEntryPatch.cs`）

**根因**
- 对 `ConversationMissionLogic.OnMissionTick` 打 Harmony Prefix，无论哪种形式（`bool Prefix()` / `void Prefix()` / `Prefix(float dt)` / `Prefix(ref bool __runOriginal)`），在 v1.4.7 中均触发角色模型横置
- 问题不在 Prefix 的返回值逻辑，而在 Harmony 对这个方法的 **detour 机制本身**——角色创建和物品界面底层竟然也复用了 `ConversationMissionLogic`，Harmony 的方法重定向在 v1.4.7 运行时下破坏了引擎的渲染初始化
- 该补丁原始目的：抑制我们的大地图遭遇对话 Mission 中原版 `ConversationMissionLogic` 的"自动对话初始化"和"对话结束后自动结束 Mission"。分析 `OnMissionTick` 源码后确认这两件事可能不需要抑制——初始化只执行一次（`_conversationStarted` 守卫），自动结束在对话进行中不会触发

**规避**
- 用 `#if false` 永久禁用此补丁
- 如果之后大地图遭遇对话出现异常（NPC 不自动说话、Mission 不结束等），优先怀疑此补丁缺失 → 换用非 Harmony 方案（如移除 `ConversationMissionLogic` behavior、用 Transpiler 代替 Prefix、或在 `InteractionMissionView` 中自行处理原版行为）
- 2026-08-01 排查记录：`plans/harmony-patch-bug-hunt.md`

---

## Harmony 字符串式补丁目标静默失效：编译通过 ≠ 方法存在

**症状**
- 补丁对应的功能**无报错、无日志地不工作**（不崩游戏，纯静默）。排查半天找不到异常——因为根本没异常可找。
- 曾误判"游戏更新后 API 变了"：游戏更新到 v1.4.8 后核对 22 个补丁目标，`RefreshBehaviorGroups` 在 `TaleWorlds.MountAndBlade.dll` 里二进制搜索 0 次命中，一度以为 1.4.8 删了方法。

**根因**
- `[HarmonyPatch(typeof(X), "字符串方法名")]` 的字符串目标是**运行期反射解析**，编译期不校验——编译通过 ≠ 方法存在。目标缺失时 Harmony 静默跳过该补丁。
- 误判放大器：**类型归属不能靠猜**。`AgentNavigator` / `AgentBehavior` / `AgentBehaviorGroup` 在 **SandBox.dll**（namespace 仍是 `TaleWorlds.MountAndBlade`，跨程序集共用命名空间），只搜 MountAndBlade.dll 自然全 0；且 `ilspycmd -t <类型>` 在类型不存在的 DLL 上**静默输出空、无报错**，容易误判"类型不存在/工具坏了"。

**规避**
- 核对/新增任何 Harmony 字符串补丁目标时，先**二进制 grep 全游戏目录定位**（0 次 = 肯定不在；≥1 次 = 再用 `ilspycmd -t` 确认签名）：

```bash
grep -c -a "RefreshBehaviorGroups" "$MB2_PATH/bin/Win64_Shipping_Client/"*.dll \
  "$MB2_PATH/Modules/SandBox/bin/Win64_Shipping_Client/"*.dll \
  "$MB2_PATH/Modules/Native/bin/Win64_Shipping_Client/"*.dll 2>/dev/null | grep -v ":0"
```

- 完整流程（含 `RefreshBehaviorGroups` 到底在哪的实录）见 [CLAUDE.md「反编译 DLL 禁止瞎猜」](CLAUDE.md#L100-L118)。


---

## 文本含 `{...}`（JSON/大括号）走 TextObject → 从第一个 `{` 起整体截断

**症状**
- 用 `LWNTextHelper.ResolveText` / `TextObject("{=key}...")` 渲染含 JSON 大括号的文本（如 LLM prompt 模板 `{"type": "distance"}`）→ 输出从第一个 `{` 起全部丢失（或只剩 `{` 之前的片段）。
- 含 `{` 的普通中文文本同样中招——不只是 JSON。

**根因**（反编译 `TaleWorlds.Localization.dll` 的 `Tokenizer` 确认）

```
Tokenizer.FindTokenMatchesAndText
  └─ 遇 '{' → FindExpressionEnd 数括号配平取表达式
        └─ FindTokenMatches：表达式内每个字符都要匹配 44 个 token 定义
              └─ JSON 引号 '"' 无任何 token 定义 → ThrowLocalizationError + return false
                    └─ mbTokenMatches.Clear() + return → 整个字符串从 '{' 起被丢弃
```

- `{...}` 会被当变量表达式解析，而 44 个 token 定义里没有双引号——JSON/任意含引号的大括号内容必然解析失败。
- 失败后的"恢复"是**截断**（清空 token 列表、只保留 `{` 之前的缓冲文本），不是保留原文。
- TextObject 的 `{=key}` 翻译标记本身正常，问题在标记之外的文本内容。

**规避**
- **含 `{`/JSON 的文本禁止走 TextObject**（`ResolveText`/`Resolve`/`ResolveCompound` 全不行——最终都过 `MBTextManager.ProcessTextToString`）。
- LLM prompt 等非玩家可见文本 → 用 `LWNTextHelper.ResolvePrompt(key)` **纯字典读取**（启动时 `InitializeEnglishFallback` 已加载全部 `std_*.xml`，含 `Languages/CNs/` 子目录；`
` 字面量 → 换行；缺 key → 日志 + 空串，不崩）。落地范例：`LLM/PromptBuilder.cs` `BuildPlanPrompt` 的 `LWN_plan_*` 静态块。
- 玩家可见文本（无大括号）照常走 `ResolveText`。
- 2026-08-08 排查记录：`plans/llm-goap-plan-execution.md` 顶部待办 3（本地化改造）。


---

## HttpWebRequest POST OpenAI 兼容网关 → 400 Bad Request（chunked 被 nginx 拒）

**症状**：连接测试用 `HttpWebRequest` POST 到 OpenAI 兼容端点（雷火 ai.leihuo.netease.com 等）返回 `WebException: (400) Bad Request`；**同一 URL/key/body 用 curl 或 HttpClient 原样请求 → 200**（游戏内 ChatAsync 一直正常，仅测试按钮失败——"测试失败但生产正常"的假象）。

**根因**（2026-08-08 另一 session 用 chunked 复现验证）：.NET Framework `HttpWebRequest` 用流写入请求体时**未显式设置 `ContentLength`** → 退化为 `Transfer-Encoding: chunked` 传输；部分 OpenAI 兼容网关的 nginx 前置**拒绝 chunked 请求** → 400。`HttpClient` 自动计算 Content-Length 所以不受影响；curl 也自动 Content-Length。

```csharp
// ❌ 旧写法（chunked → 400）：
var body = JsonConvert.SerializeObject(...);
using (var stream = req.GetRequestStream())
using (var writer = new StreamWriter(stream, Encoding.UTF8))   // 无 ContentLength → chunked
{ writer.Write(body); }

// ✅ 正解：body 先转 UTF-8 字节数组 + 显式 ContentLength + 直接写字节流：
var bodyBytes = Encoding.UTF8.GetBytes(body);
req.ContentLength = bodyBytes.Length;   // 关键：显式声明长度，避免 chunked
using (var stream = req.GetRequestStream())
{ stream.Write(bodyBytes, 0, bodyBytes.Length); }
```

**规避**：
- 用 `HttpWebRequest` 发带 body 的 POST → **必须显式设 `req.ContentLength`**（写字节数组，别用 StreamWriter 流式写）。
- 更稳：请求通道与生产一致（`HttpClient`），测试通道不一致 = 假失败/假成功的温床（LLMService.TestConnection 注释有完整踩坑史，2026-08-08）。
- 排查 HTTP 错误先读响应 body/用复现对比（curl vs 代码），别猜头（Expect/UA 均非根因，已实测排除）。

**实机复现对照（2026-08-08 双版本验证）**：
- 版本 A（提交 23e111b，HttpClient + `ConfigureAwait(false)` + `GetResult`）→ 连接**正常**（绿字"LLM 连接正常"，日志无异常）。
- 版本 B（HttpWebRequest + StreamWriter 流式写、无 ContentLength）→ **400**（日志 `WebException: (400) Bad Request`）。
- 版本 C（HttpWebRequest + `bodyBytes` + 显式 `ContentLength`）→ 连接**正常**。
- **结论：同一同步方法，差别只在 ContentLength——chunked 是唯一变量**（其余 Expect/UA 等头均实测排除）。

**异步机制的真实坑（`PostAsync(...).GetAwaiter().GetResult()` 在 UI 线程）**：
- 版本 A 确实用了异步机制：async HttpClient 调用 + 同步阻塞等待（GetResult）。
- **真实坑 = 死锁**：`GetAwaiter().GetResult()` 阻塞 UI 线程 → await 的 continuation 需要回 UI 线程执行（若没 `ConfigureAwait(false)`）→ 互相等待 → 死锁 → 请求永远不完成 → 10s 超时 `TaskCanceledException` → **假"连接失败"**（最初版本实测过此症状，代码注释有记录）。
- **解法 = `ConfigureAwait(false)`**：continuation 改在线程池执行，不被 UI 线程阻塞 → 无死锁（版本 A 实测 F5 后绿字正常 = 死锁已解除）。
- **结论：async + GetResult 不是不能用，但必须 `ConfigureAwait(false)`**；纯同步（HttpWebRequest）则完全绕开该问题——两个方向都验证可行。

**「VS 弹出异常断点」的定性（2026-08-08 排查教训）**：
- 症状：断点停住、**无 $exception**、**catch 日志未写**；F5 继续后一切正常（版本 A 实测绿字"LLM 连接正常"）。
- 定性：**代码没有抛异常**——无 $exception（异常断点必有）、无 catch 日志（真抛会被 catch 记录）、F5 后正常（真抛会红字/失败）。这是 VS「抛出时中断（Thrown）」断点命中了**游戏其他代码**（引擎/其他 mod 每天大量被 try-catch 捕获的常规异常），停住时显示位置 = 当前正在查看的代码行，与异常实际位置无关。
- 与异步机制**无关**：版本 A（异步）与版本 B/C（纯同步）都弹过——弹不弹取决于 VS 异常设置，不取决于代码写法。
- 辨识三步：① 看调用堆栈顶部帧是不是当前方法（多半不是）② 看 $exception 是否存在（无 = 不是该帧异常）③ F5 继续看是否正常（正常 = 无 bug）。
- 第四步（最终确认）：**脱离调试器（直接 Steam 启动）复测**——正常 = 100% 确认与代码无关（2026-08-08 实测）。
- 规避：VS 异常设置只留 User-unhandled；代码侧 catch 全打日志——日志无痕 = 无异常。

---

## 中文输入法组词期间退格删掉已上屏的字 → 消息路由盲区 + 轮询延迟（IME 三坑）

**症状**（实机 2026-08-21，搜狗输入法）
- 输入框已有上屏文字（如"你好"），用搜狗打"nihao"组合到一半按退格改拼音 → **已上屏的"你好"被删掉**。
- 组合期间的 Enter 还会误触发发送/确认（上屏候选字被当成回车）。

**根因**（三层，全实锤，缺一必漏检）

```
① TSF 型输入法（搜狗/微软拼音）不走 IMM32 上下文：
   ImmGetCompositionString(GCS_COMPSTR) 返回 0 → 单 IMM32 轮询检测漏检（静默！）
② WM_IME 消息路由有盲区：
   用户组合 "nihao"+2退格（~2s）期间，主窗口 WndProc 钩子只收到一对 14ms 的 START/END——
   组合消息根本不完整到达主窗口（TSF 消息路由不同/被 native 层消耗）→ 依赖组合消息门控从根上错
③ 游戏轮询比物理键晚 1~2 帧：
   Input.IsKeyPressed(BackSpace) 的沿在按键后 1~2 帧才出现——
   「上一帧组合、这一帧结束」的帧级过渡判定错过沿，必漏
```

- 骑砍2 输入是**原始按键轮询**：`EditableTextWidget.HandleInput` 用 `Input.IsKeyPressed(BackSpace)` 删字（反编译实锤）——IME 消费退格（改拼音）的同时，游戏轮询也看到退格 → 删已上屏的字。
- 三个坑叠加：检测不到组合（①/②）+ 检测到了也挡不住延迟沿（③）。

**规避**（落地：`Input/EditableTextImePatch.cs`，三信号 + 前缀吞键）

- **主信号 = VK_PROCESSKEY 按键消费**：被输入法消费的键，`WM_KEYDOWN` 的 wParam = 0xE5（非真实 VK 码）——**按键级、事件时刻、与消息路由无关**（键盘消息必到聚焦窗口）。最近一次 VK_PROCESSKEY 后 **150ms 宽限**内门关闭：覆盖轮询延迟 1~2 帧的残余沿；组合结束后的新按键（人类反应 >200ms）不受影响。
- 叠加信号：WM_IME 消息组合态 + IMM32 轮询（经典输入法）+ 武装键（组合结束瞬间 `GetAsyncKeyState` 读物理按下键，按住期间锁门）。
- **WndProc 子类化纪律**：委托/函数指针静态保活（GC 回收后回调崩）；钩**本进程全部顶层窗口**（`EnumWindows` 按 PID 过滤——消息路由到哪个窗口不确定）；组合中 >5s 无 IME 消息 = 超时开门（防钩子失效后永久锁死输入）。
- **Enter 双沿**：IM/确认路径有自己的 `IsKeyReleased(Enter)` 轮询，补丁挡不住抬起沿——按「按下时是否组合」标记（`_imeEnterHeld`）吞掉上屏候选字的 Enter。
- 排查口诀：**"组合期间删字"先别怀疑代码路径，先查组合检测信号在不在**——日志 `[ImeInput]` 的 `按键被输入法消费 vk=0xE5` 行是主证据（组合期间每个字母/退格都该有一条）；没有 = 检测信号没触发；有但删字 = 补丁未生效。
- 版本兼容：补丁目标 `EditableTextWidget.HandleInput(IReadOnlyList<int>)` + 命名空间 + `InputKey` 成员 + `Input.IsKeyDown/IsKeyPressed` 三锚点（1.2.12/1.3.15/1.4.6）一致，无需 `#if`。

---

## 屏幕销毁窗口期给 widget 设 IsVisible → GauntletUI 内部 NRE（`_widgetContainers` 已置 null）

**症状**（实机 2026-08-21）
- `System.NullReferenceException`，`Source=TaleWorlds.GauntletUI`，栈：
  ```
  EventManager.RegisterWidgetForEvent(ContainerType, Widget)
  → ImageWidget.RefreshState → ButtonWidget.RefreshState
  → Widget.set_IsHidden → Widget.set_IsVisible
  → LivingWorldNpcs.SecretLetterButtonInjector.UpdateLive（第 262 行，`it.Button.IsVisible = ...`）
  ```
- 触发：家族屏给随从设置军需官 → 点「完成」→ 屏幕/面板收尾销毁时崩。
- 前置征兆日志（同帧）：`[SecretLetter] 家族 tableau 定位成功但 CharStringId 读不到: tableau=False`——详情面板已从树中消失，销毁已开始。

**根因**（反编译 `TaleWorlds.GauntletUI.dll` 确认）

```
EventManager.OnFinalize()（UIContext/GauntletLayer 销毁，屏幕关闭时触发）
  └─ _widgetContainers = null                 // 容器字典整体置空

其后窗口期内（widget 树尚未拆完）：
it.Button.IsVisible = ...                     // 注入型 UI 的每帧可见性同步
  └─ IsHidden setter → RefreshState()
        └─ ButtonWidget.RefreshState → ImageWidget.RefreshState → SetState(...)
              └─ EventManager.RegisterWidgetForEvent(Update, widget)
                    └─ _widgetContainers[type].Add(widget)   // 已 null → NRE
```

- 关键盲点：销毁窗口期 widget 的 **`ParentWidget` 仍然非 null**（树拆到一半），`ParentWidget == null` 存活检查会被骗过。
- 可靠判据：`Widget.EventManager => Context.EventManager`（反编译确认），`Context == null` = 已脱离活树。

**规避**
- 注入型 UI 每帧操作 widget 属性前，存活检查加 `Context == null`：
  ```csharp
  if (btn.ParentWidget == null || btn.Context == null) { 自清理; continue; }
  ```
- 保险丝：会触发 `RefreshState` 的属性写入（IsVisible/IsHidden 等）用 try/catch 包裹——捕获 = 树已死 → 从注入列表自清理（含 hover 清理），**不要每帧重试**；若只是面板刷新（非关屏），节流 Scan 幂等重注入即可恢复。
- 落地范例：`GUI/SecretLetterButtonInjector.cs` → `UpdateLive`（2026-08-21 实踩：家族屏设军需官点完成后崩）。
- 判别口诀：**栈底是自己代码的 `IsVisible =` 赋值 + 栈顶是引擎 `RegisterWidgetForEvent` 内部 NRE = 屏幕销毁窗口期**——不是字段判空漏了，是别碰即将销毁的树。

---

## `Environment.TickCount - int.MinValue` 溢出为负 → 时间窗比较恒成立（状态机永久锁死）

**症状**（实机 2026-08-22，PC 上 MCM 文本框）
- 输入框**打字（123）正常，退格/Delete/方向键/Enter 全部失效**；粘贴也受影响。
- 日志：游戏启动后第一帧即出现「组合态吞键」，**全程无任何 WM_IME 消息、无输入法活动**——状态机从启动起就永远判定"组合中"。
- 可打印字符正常（被按"组合中上屏"放行），非可打印轮询键（退格等）全被吞。

**根因**（代码级实锤，[Input/EditableTextImePatch.cs](ExampleModVS/ExampleMod/ExampleMod/Input/EditableTextImePatch.cs)）

```csharp
private static int _lastVkProcessKeyTick = int.MinValue;              // 初始哨兵
if (Environment.TickCount - _lastVkProcessKeyTick < 150) return true;  // ❌
```

- `Environment.TickCount`（int，uptime ms）减去 `int.MinValue` 必然 **int 溢出为负数**（`TickCount - (-2147483648)` 超出 int 上界回绕）。
- **负数 `< 150` 恒成立** → 时间窗判断永远命中 → 组合态从启动起永远 true。
- 该模式常用于"最近一次事件 X 后 N ms 内"的门控——哨兵初始值 + 直接相减 = 一启动就锁死，直到 24.8 天 uptime TickCount 翻转才可能偶发自愈（实为随机）。
- 症状伪装性极强：**一半逻辑正常（可打印字符放行）、一半失效（轮询键被吞）**，看起来像输入法在组合、像按键被拦截，实际是时间窗误判。

**规避**

- 哨兵值必须先排除，差值比较用 `(uint)` 转换（无符号回绕 = 时间差正确语义，同时免疫 TickCount 翻转）：

```csharp
private static int _lastEventTick = int.MinValue;                    // 哨兵：从未发生
if (_lastEventTick != int.MinValue
    && (uint)(Environment.TickCount - _lastEventTick) < windowMs)     // ✅ 哨兵跳过 + 无符号差值
    return true;   // 窗口内
```

- 初始值不用哨兵也可用 `int.MinValue + 1` 等不影响判断的"远古时间"，但**哨兵跳过最明确**。
- 新建任何 `TickCount` 时间窗/冷却/宽限/节流字段时按此模板写；`>=` 反向判断（如"超过 5s 超时"）同样受溢出影响——初始哨兵 `int.MinValue` 时 `TickCount - 哨兵 < 0` 恒负，`< 5000` 恒真。
- 排查口诀：**「启动即处于某时间窗状态 + 日志无对应事件」= 先查哨兵初始值与 TickCount 相减**。同类模式全项目扫描（`int.MinValue` grep）：`ImeCompositionHelper._lastVkProcessKeyTick`（本坑）与 `ImChatSoftKeyboardContextDonePatch.FillVerifyWindowStart`（诊断窗口常开刷屏，同修）已修复。

## Harmony 补丁「显式接口实现」方法 → PatchAll 抛 ArgumentException 崩游戏启动

**症状**：`PatchAll()` 时 `ArgumentException: Undefined target method for patch method ...`（游戏启动即崩，无日志可查）。

**原因（2026-08-22 实机）**：`TwoDimensionEnginePlatform.OpenOnScreenKeyboard` 是 `ITwoDimensionPlatform` 的**显式接口实现**——IL 方法名 = `TaleWorlds.TwoDimension.ITwoDimensionPlatform.OpenOnScreenKeyboard`（带完整接口前缀），Harmony 字符串 `"OpenOnScreenKeyboard"` 匹配不到。**「抽象接口方法不能补丁」不限于接口本身**——补丁具体类的显式实现同样中招（显式实现无裸方法名）。

**规避**：补丁前用 `ilspycmd -l c` / 反编译确认方法名（显式实现必带接口前缀）；落点日志改用**链上公有静态方法**（如 `ScreenManager.OnPlatformScreenKeyboardRequested`——只在引擎链请求到达后才被调用 = 落点证明，返回值 = 平台结果）。

## Steam Deck 弹窗时有时无（同一 DLL 两次启动一次好一次坏）→ IsSteamDeck 检测竞态

**症状**：同一 DLL 会话 A 弹窗正常、会话 B 弹窗全灭（`Steam Deck 检测: False`）；重启后又正常。

**原因（2026-08-22 实机 16:28/16:36 两会话对比实锤）**：`IsSteamRunningOnSteamDeck()`（Steamworks.NET）在 SteamAPI 未初始化时**抛异常**（`TestIfAvailableClient`）——启动早期首次聚焦输入框触发检测，与 SteamAPI.Init 完成形成**竞态**。若异常被 catch 后**缓存 false**，整个会话不再重试 → 弹窗请求链全灭（PC 同理：Epic/GOG 下 Type.GetType 返回 null 属正常降级，**只有「异常」才需要重试**）。

**规避**：检测失败**不缓存** + 冷却重试（3s，防刷日志）：

```csharp
private static int _retryTick = int.MinValue;
if (_cached) return _isSteamDeck;
if (_retryTick != int.MinValue && (uint)(Environment.TickCount - _retryTick) < 3000) return false;
try { _isSteamDeck = ...; _cached = true; }   // 只有成功（含正常返回 false = 非 Deck）才缓存
catch (Exception) { _retryTick = Environment.TickCount; }   // 失败：冷却后重试
```

**排查口诀**：外部 API 首次调用抛异常被 catch 降级时，先问「这个降级结果要不要缓存」——**初始化型竞态（API 就绪需要时间）一律失败不缓存**。

---

## Steam Deck 桌面模式软键盘弹不出（`ShowGamepadTextInput` 恒 false）→ Steam 客户端模式限制，mod 无解

**症状**：Deck 桌面模式跑游戏，聚焦文本框（IM/MCM/原版）无软键盘；日志链：`Steam Deck 检测: True` → 聚焦行守卫全过 → `Steamworks 直连 ShowGamepadTextInput → False` → 无 Done/Cancel 回调 → 约 0.7s 后 `请求判定: IsOnScreenKeyboardActive=False`；**昨晚游戏模式同 DLL 一切正常**。Steam+X 系统键盘在游戏运行中也弹不出（普通软件正常）。

**根因（2026-08-24 实机闭环）**：Steam 客户端**桌面模式无大屏幕键盘 UI 服务**（游戏模式才有）→ `SteamUtils.ShowGamepadTextInput` 直接返回 false（静默，无异常无回调）。**Steam+X 在「Steam 启动的游戏」运行时被路由给游戏进程**（游戏进程桌面模式无键盘可弹 → 无响应）——Steam 客户端行为，游戏/mod 都改不了。`IsSteamRunningOnSteamDeck()` 两种模式都返回 True，**无法用 Steamworks 区分模式**。

**判定技巧**：mod 侧 **Steamworks 直连**调用（反射调 `SteamUtils.ShowGamepadTextInput`，绕过引擎桥/`PlatformServices.Instance`）返回值 = Steam 亲口回答——True = 键盘已弹（引擎桥坏假说成立）；False = Steam 拒绝（环境无解）。引擎桥 `Input.IsOnScreenKeyboardActive = ScreenManager.OnPlatformScreenKeyboardRequested(...)` = `ShowGamepadTextInput` 返回值（反编译实锤）——日志 `请求判定: False` 同义。**别在桌面模式排查 mod 键盘代码**（白费），先确认模式：游戏模式能弹 = 环境问题实锤。

**处置**：桌面模式打字 = 游戏模式 / 实体键盘；`[KbDiag]` 链路日志排查时开（`Settings.KbDiagEnabled`，config.json），平时关。

---

## Gauntlet TextWidget StretchToParent + 超宽文本 → 引擎自动压字号（"裁剪即止"结论作废）

**症状**（实机 2026-08-23 用户反馈：IM 左栏频道最近消息预览"还是太小"）
- XML 里 `FontSize` 已设 18/19，但**长文本看起来明显比短文本小**——同一行里"短预览正常、长预览被压扁"。
- `ClipContents="true"` 已设，文本不会溢出，但**字号照样被压**——裁剪不阻止缩放。
- 换 CoverChildren 后同字号恒定为期望大小（长文本改为像素裁剪）。

**根因**
- TextWidget 在 `WidthSizePolicy="StretchToParent"` 布局下，文本测量超出可用宽时**引擎自动缩放字号**（2026-08-19 标题修复时实机证实：长频道名被压扁、短名字正常，观感不齐）。
- **🔴 2026-08-20 的"StretchToParent 布局无压字号问题，超宽由 ClipContents 像素裁剪兜底"结论错误**（当时记在 `NameDisplayRules.cs` / `ImChatView.cs` 注释里）——裁剪只裁绘制、不阻止字号缩放，2026-08-23 实机推翻。引擎没有"裁剪即止"这回事。
- 量化判据：可用宽 ≈ 206px（260 左栏 − 内边距），截断阈值 14 字 @19px ≈ 266px > 206px → 预览稍长必触发压字号。

**规避**（标题同款修复，2026-08-19 先例）
- TextWidget 改 `WidthSizePolicy="CoverChildren"`（宽度=内容测量值，**引擎无缩放空间**）+ `MaxWidth`（=该处可用宽，防挤占兄弟元素）+ `ClipContents` 兜底。
- C# 侧按显示位置可用宽 + 目标字号校准截断阈值（省略号占 1 格）：`可用宽 ÷ 字号 − 1`。落地：`NameDisplayRules.MaxChannelTitleChars=14`（标题，2026-08-19）/ `MaxChannelSubtitleChars=10`（副标题 9 正文+… ≈190px ≤ 206px，2026-08-23 从 14 校准）。
- 落地 XML：`GUI/Prefabs/ImChat.xml` 左栏频道行标题 + 副标题；`HorizontalAlignment` 显式 Left（CoverChildren 无拉伸对齐）。
- 排查口诀：**「短文本正常、长文本变小」= 引擎压字号，不是字号没改对**——先查宽度策略，StretchToParent 一律改 CoverChildren+MaxWidth 后恒字号。

---

## 保管箱反复撬锁后结算崩溃（日志戛然而止、零异常）→ 静态财富状态残留 + 结算不干净

**症状**（玩家日志 2026-08-26 贾尔马律斯实机）
- 同一领主大厅**连续撬锁 4 次**（19:09:58 → 19:11:22，无 Mission 切换），前 3 次弹窗后 0.8~3 秒内必有 `[TheftLedger]` 结算日志，**第 4 次弹窗后零输出**——崩在 `ShowChestInquiry` 的「全部拿走」回调里。
- 崩前累计转移 **769 件物品 + 16044 金币**（单次最多 712 件/105 种），队伍严重超重；**无任何 FirstChance/异常记录** → 引擎层（native）崩溃特征。
- 附赠怪象：结算后弹窗金币显示 **`保管箱。83`**（正常格式是 `保管箱。\n金币: X 第纳尔`，缺「金币:」前缀）——goldLine 走了异常路径，疑似残留状态（未完全定位，修复后观察是否消失）。

**根因**（两个状态洞叠加，代码链实锤）
```
洞① 拘留路径绕过 Mission Finalize：
  玩家被制住（AttackTriggerMissionLogic 倒地→菜单落定居点）→ 放人（ReleaseContinueOnConsequence）
    └─ 只清 PlayerDetentionBehavior 自身状态，不清 StealManager 静态财富状态
    → _lastDistributedSettlementId 残留（正常路径由 OnMissionScreenFinalize→ClearWealthDistribution 清）
    → 再进同场景：DistributeSettlementWealth 防重键命中提前 return（不打 [Wealth]）
    → 用旧分配数据复刷箱子（实锤：19:08:47 [Chest] Spawned gold=8227/items=105 与 19:08:08 一字不差）

洞② 结算不干净 → 箱子不销毁 → 反复可撬：
  「全部拿走」→ LootChestItem：actual = Math.Min(count, settlement.ItemRoster.GetItemNumber(item))
    └─ settlement 库存不足（已被前面结算扣光）时 actual=0 → ChestItemRoster.AddToCounts(-actual) 不减
    → ChestItemRoster 残留 → RemoveChestEntityIfEmpty 判定 IsEmpty()=false → 箱子不移除
    → 同一场景无限撬同一箱子（实锤：第 3 次弹窗 7 种物品全部在第 2 次 TheftLedger 里出现过）
    → 第 4 次结算对已扣光的定居点库存反复 AddToCounts+TransferItems → 引擎层崩溃
```

**规避**（2026-08-26 已修复）
- ① 拘留放人 `PlayerDetentionBehavior.ReleaseContinueOnConsequence` 开头主动 `StealManager.ClearWealthDistribution()`（不依赖 Finalize，玩家已在大地图，状态作废必清）。
- ② `LootChestItem` 的 `ChestItemRoster.AddToCounts` 改按**请求量 count** 扣（非 actual）——「全部拿走」语义 = 清空，箱子侧必归零，结算后箱子必移除。
- ③ 箱子填充数量上限（`DistributeSettlementWealth`）：单种 ≤10 件、总件数 ≤120、种类 ≤40——杜绝 712 件/105 种一锅端，压住批量结算的引擎压力。
- ④ `ShowChestInquiry`「全部拿走」回调整体 try/catch + DebugLogger（finally 保证 `IsUIOpen` 复位），再崩也有日志。
- 排查口诀：**「弹窗后零输出 + 无异常记录」= 结算回调里引擎层崩溃**——先查结算路径有没有 try/catch、再查静态状态是否残留（同场景能反复撬锁 = 结算没清干净）。

---

## 动态插入按钮叠在目标按钮上（插入点是无布局的普通 Widget + 行结构跨版本不一致）

**症状**（2026-08-29 队伍屏密信按钮实机诊断实锤）
- 注入按钮与原版交谈按钮**渲染位置完全重合**：诊断日志 `talk=(1489,590) 54x43` vs `btn=(1489,586) 50x50`——同一个 X。
- 点交谈按钮 → 点击被注入按钮偷走（日志 `[SecretLetter] 点击密信按钮` 出现在交谈按钮矩形的落点，IM 打开）；注入按钮隐藏后（传讯开关关）点击恢复正常。

**根因**：插入目标不是「有布局算法的容器」。
- 队友行 `ButtonsList` 的父容器 **ButtonCarrier 是普通 Widget（无 StackLayout）**——子树没有布局驱动，子节点 `Left/Top` 保持默认 0 → 渲染在容器原点 = 目标按钮的位置。
- 为什么会插到 ButtonCarrier：**行结构跨版本不一致**——H盘 1.5.2 XML 是 `TalkButton → 容器 → ButtonsList(ListPanel) → ButtonCarrier`；玩家实机（旧版）是 `TalkButton → ButtonsList(ListPanel) → ButtonCarrier`（无中间容器）。旧代码 `slot = talkWidget.ParentWidget; slot.ParentWidget.AddChild(...)` 在实机上把 `slot.ParentWidget` 算成了 ButtonCarrier。

**防法**（已修复 `GUI/SecretLetterButtonInjector.cs`，详见 wheels.d/ui.md「动态插入」条）：
- **插入点必须上溯到最近 `ListPanel`**，不要赌固定行结构：
  ```csharp
  Widget wrapper = talkWidget;
  if (!(talkWidget.ParentWidget is ListPanel))
      wrapper = talkWidget.ParentWidget;    // 有容器包装形态：跟在容器后
  Widget insertInto = wrapper.ParentWidget; // 两形态下都是列表本体
  if (!(insertInto is ListPanel)) return false;  // 结构未知 → 安全跳过
  ```
- 排查口诀：**「注入按钮和原版按钮同坐标」= 插进了无布局的普通 Widget**——打印插入目标的类型名（`btn.ParentWidget?.GetType().Name(Id)`），不是 `ListPanel` 就错了。

---

## 引擎 StackLayout 不给不可见子节点分配布局盒 / IsVisible 翻转不触发重排（动态插入按钮的延迟错位）

**症状**（2026-08-29，与上一条同事故鉴定时发现的第二条引擎行为）：
- 动态插入列表的按钮在「隐藏→显示」翻转瞬间，位置仍是旧值/默认值（悬在列表 (0,0) 或上一次分配的位置），叠压带布局的邻居——即使插入点是对的列表。

**根因**（反编译 `TaleWorlds.GauntletUI.dll` `StackLayout` / `Widget`，v1.5.1 实锤）：
- `LayoutLinearHorizontal` / `MeasureLinear` **只处理 `IsVisible=true` 的子节点**：不可见子节点既不推进 x 也不调用 `Layout()`（布局盒空缺，`Left/Top` 保持上次值或默认 0）。
- `Widget.IsHidden` setter **只改字段不触发重排**（`SetMeasureAndLayoutDirty` 只在 `ParentWidget=`/`SetSiblingIndex`/尺寸属性变更时调用）——「行数据变化 → 锚点变可见 → 按钮 IsVisible=true」这个翻转引擎完全不知情，布局不会补跑。

**防法**（`GUI/SecretLetterButtonInjector.cs` UpdateLive 已实现）：
- **可见性 false→true 翻转后立即 `SetSiblingIndex(GetSiblingIndex(), force: true)`**——引擎公开 API，强制整树 measure+layout，以可见态重分配布局盒；签名 1.2.12~1.5.x 一致。
- 低频事件（行数据变化才触发），成本可忽略；反转方向（→隐藏）不需要——不可见不渲染、且再次显示时翻转判定会重新触发。
- 排查口诀：**「注入按钮长在对的位置上（列表内）但翻出来就错位」= 布局盒在隐藏态空缺**——翻转时加强制重排。

---

## ShowHint/tooltip 展示有寿命自动淡出 + 屏关闭销毁窗口期旧矩形 → 手动 hover 提示两病（凭空出现 / 悬停不显）

**症状**（2026-08-29 密信按钮 hover 实机反馈）
- 在大地图上随意移动鼠标，会「凭空」弹出密信按钮的 hover 提示（按钮明明不在屏幕上）。
- 鼠标停在密信按钮上，提示有时不出现——特别是一开始出现、后来自行消失后就不再出现。

**根因**（两条独立引擎行为叠加）
- **① 销毁窗口期旧矩形**：队伍/家族屏关闭后，widget 树要等 `HandleFinalize` 才拆——期间按钮 `ParentWidget` 仍非 null（`_live` 自清理分支未触发），`GlobalPosition` 仍是**旧屏幕坐标**。手动 hit-test 每帧只判 `鼠标 ∈ 按钮矩形`——鼠标扫过大地图上的旧矩形位置 = `over=true` → 弹提示。
- **② tooltip 展示寿命**：`MBInformationManager.ShowHint`（→ `InformationManager.ShowTooltip(typeof(string), …)`）显示后**自身淡出**；而 hover 代码只在「进入矩形瞬间」Show 一次——淡出后鼠标仍停在按钮上 = 永不重显（除非移出再进）。

**防法**（`GUI/SecretLetterButtonInjector.cs` UpdateLive 已实现）
- **屏激活门控**：hover 判定前先查 `ScreenManager.TopScreen` 是注入按钮所属屏（Party/ClanScreen）——不是 → `over=false`（隐藏 + 复位）。注入按钮的矩形只在它自己的屏存在意义。
- **周期重发**：`over && _hoverOn == 按钮` 期间每 ~3s 重发一次 `ShowHint`；`_hoverShowTimer` 在进入瞬间清零、离开即停。
- 排查口诀：**「提示出现在别的屏/大地图上」= 按钮矩形来自已关闭屏的销毁窗口**；**「提示第一次出、后面不出」= tooltip 淡出后未重发**。二者都是「每帧判定 + 一次性 Show」的必然结果——手动 hit-test 的 hover 都要配门控 + 重发。

---

## 启动即崩 `Cannot bind to the target method...` → 控制台指令签名写成 `string[]` 而不是 `List<string>`

**症状**（实机 2026-08-31 1.2.12 启动报错）
- 游戏启动即崩，异常 `System.ArgumentException: Cannot bind to the target method because its signature or security transparency is not compatible with that of the delegate type.`
- 栈只有引擎侧：`CommandLineFunctionality.CollectCommandLineFunctions()` → `Delegate.CreateDelegate`，看不到任何 mod 方法名（**栈里没有 = 反射扫描中招，不是某个调用点出错**）。
- 毫无先兆：DLL 编译 0 错误 0 警告，旧版本同源代码能跑。

**根因**（反编译实锤，1.2.12 与 1.5.1 的 `TaleWorlds.Library.CommandLineFunctionality` 逻辑一致）

```
CollectCommandLineFunctions()  // 启动时反射扫描所有程序集
  └─ 每个带 [CommandLineArgumentFunction] 特性的方法：
        Delegate.CreateDelegate(typeof(Func<List<string>, string>), methodInfo)  ← 引擎要求 List<string>
             └─ 方法写成了 (string[] args) → 签名不匹配 → ArgumentException
```

- 引擎委托是 `Func<List<string>, string>`，**不是** `Func<string[], string>`——`string[]` 和 `List<string>` 是不同类型，委托绑定直接失败。
- **为什么编译能过**：特性不校验签名，绑定是运行期反射行为——与 Harmony 字符串式补丁同款「编译不校验、运行期才判断」陷阱。
- 2026-08-31 肇事点：`Scenario/ScenarioCommands.cs` 新写的 12 个指令全用了 `string[] args`（MyCommands.cs 等其他 70+ 指令均为 `List<string>`，同库对照一秒钟就能看出异常）。

**规避**（已修复 `Scenario/ScenarioCommands.cs`）
- 签名固定：`public static string Xxx(List<string> args)`；内部用 `args.Count`，`string.Join(" ", args)` 直接可用。
- **新增任何控制台指令后自查**：grep `string\[\] args`，命中必炸启动。
- 排查口诀：**「启动即崩 + 栈里只有 CollectCommandLineFunctions」= 特性方法签名不匹配**——全库搜 `CommandLineArgumentFunction` 列表逐方法看签名，别去查崩溃点。已登轮子：wheels.d/config.md「控制台调试指令」。

## 同步事件重入清空执行字段：`if (x != null)` 检查后仍 NRE（OnTick 内发事件给自己脑）

**症状**（实机 2026-09-02 22:02，乞丐残血认输）
- `AgentBrain.Tick` 2112 行 NRE：栈指向 `_currentAction.IsFinished(Owner)`，但 2108 行明明有 `if (_currentAction != null)` 保护——「检查过了怎么还是 null」。
- 宿主无 [Crash] 记录（该局崩溃没走到 unhandled 处理器），只能靠运行时日志还原时序（[Brain-Tick] 开始执行 → 断档 → 无「完成」行）。

**根因**（代码链实锤）

```
AgentBrain.Tick (2108 if != null 检查通过)
  └─ 2110 _currentAction.OnTick()   ← 关键：字段在 OnTick 执行期间被改
       └─ FightEnemyAction.OnTick 残血 (<30%) → SendEventToAgent("event_npc_surrender")
            └─ SendEventToAgent 同步投递（AgentAIController.cs:739 直接 brain.ReceiveEvent）
                 └─ 自脑 ReceiveEvent → event_npc_surrender 分支 → ClearAllActions()
                      → _currentAction.OnEnd 代调 + _currentAction = null + 入队 StayAction
  └─ 2112 _currentAction.IsFinished()  ← 字段早已 null → NRE
```

- 本质 = **字段读-用之间的同步重入**：单线程内、`if` 检查与第二次访问之间隔着一次完整函数调用，调用链里事件同步分发回来清掉了字段。2108 的检查只保护「检查前」，不保护「检查后」。
- `_currentAction = null` 的**唯一**写入点 = `ClearAllActions`（AgentBrain.cs:1248），排查只认它。
- 副伤：重入发生后**本动作的 OnEnd 已被代调**（`_targetEnemy` 等状态已清），但 OnTick 剩余代码还在跑——FightEnemyAction 后续 `SetTargetAgent(_targetEnemy=null)` 会错误清掉引擎目标。

**规避**（已修复，双层防御）
- ① **Tick 侧最终防线**（AgentBrain.cs:2112）：`OnTick` 后补 `_currentAction != null &&`——被清空时 OnEnd 已由 ClearAllActions 代调，跳过收尾即可，下一帧 Tick 自动 Dequeue 新动作，无泄漏。
- ② **动作侧自终结**（AtomicAction.cs 残血认输分支）：OnTick 内同步投递**会终结自己**的事件后，`_isFinished = true; return;`——重入路径 OnEnd 恰好一次（ClearAllActions 代调），非重入路径由 Tick 标准清理（IsFinished → OnEnd → 置 null）也是恰好一次，两种时序对上都成立。
- ③ **排查口诀**：Tick 内 NRE 且目标字段是「执行字段」→ 搜该字段唯一写入点 → 找出同步重入路径 → OnTick 里搜 `SendEventToAgent`/`StartConversation` 等同步调用。
- 2026-09-02 全量扫描结论：AtomicAction.cs 所有 OnTick 中**唯一**的同步自投递就是残血认输一处（276 行在 UI 回调不插帧；AlertForceConversationAction 的 StartConversation 在 OnStart，2108 检查在后无影响；SpeechChannel 只入队 + async 润色不同步）。写新动作时按「OnTick 内发事件 = 发完自终结」自检。

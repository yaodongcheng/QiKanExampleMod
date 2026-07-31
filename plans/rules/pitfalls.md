# 坑点速查（疑难杂症）

> **按需加载**：不是每次会话必读。踩到诡异症状（AccessViolation、引擎 native 崩溃、状态错乱）时来这里查有没有同款坑。
> 每条格式：**症状 → 根因 → 规避**。根因尽量记到反编译确认的调用链，别凭名字猜。

---

## 对尸体/昏迷 Agent 刷新带武器的装备 → AccessViolation

**症状**
- `agent.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment)` 抛 `System.AccessViolationException`（读写受保护内存）。
- 只在**死人/昏迷**的 Agent 上发生；活人正常。
- "全部拿走/扒光"不崩，"自己挑选只拿一部分"才崩；"一件没拿"（不触发刷新）也不崩。

**根因**（反编译 `TaleWorlds.MountAndBlade.Agent` 确认）

```
UpdateSpawnEquipmentAndRefreshVisuals(newEquipment)
  └─ WieldInitialWeapons()                        // 仅当 newEquipment 里还留着武器才往下走
        └─ TryToWieldWeaponInSlot(GetPtr(), ...)   // 纯 native，无 IsActive 守卫
```

- 死人骨骼已交给物理系统（ragdoll），native 再去"把武器握进手里"就操作到失效骨骼内存 → 崩。
- **崩的只有「武器 wield」这一步**。防具留在 `newEquipment` 里**不崩**——防具走 `AddSkinMeshes`，纯渲染挂网格，不碰骨骼物理。
- "全部拿走"安全的真正原因不是时机（不是因为尸体新鲜），而是**武器被拿光、`WieldInitialWeapons` 空操作**。

**规避**
- 对 `!agent.IsActive()` 的 Agent（死亡/昏迷都算）刷新前，**无条件清空所有武器槽**，让刷新等价于"全部拿走"——无武器可 wield 即安全。防具仍可按需精准扒/保留。
- 活人不受限制，照常刷新（活人能正常重新 wield 剩余武器）。
- 落地范例：`Stealth/StealManager.cs` → `StripAgentEquipment`（`bool isCorpse = !agent.IsActive();` 时武器槽过滤器传 `null`）。
- 同理：任何对尸体调 `UpdateSpawnEquipmentAndRefreshVisuals` 的新路径（如未来 `StealSpecificItem` 作用到尸体），都要先清武器槽。

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
- ⚠️ Latest（1.4.6）`ControllerType` 枚举已被官方删除，等效冻结 API 待查（VersionCompat TODO）——Latest 侧此坑暂存。

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
- 2026-07-31 实踩：town_ES4 对话结束后，village_ES3_2 新对话 NPC 仍在说 "你把帝国步兵打晕了"（旧事件目标名），玩家只有原版联姻选项。

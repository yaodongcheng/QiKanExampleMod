# 坑点速查（疑难杂症）

> **按需加载**：不是每次会话必读。踩到诡异症状（AccessViolation、引擎 native 崩溃、状态错乱）时来这里查有没有同款坑。
> 每条格式：**症状 → 根因 → 规避**。根因尽量记到反编译确认的调用链，别凭名字猜。

---

## 🔴 通用纪律·写规则 / 改管线前（2026-09-16 蒂法 flags 事故后立，**与子系统无关，一切改动适用**）

> **背景一句话**：那天按「自定义头必须清标记」一刀切改了装机管线，把已经验收通过的蒂法 / 萨菲罗斯弄糊了。
> 而**正确答案（还带着「别一刀切」的警告）早两天就写在我自己的记忆和本仓库文档里** —— 不是不知道，
> 是**没对账、没取证、没算影响面**。三条都不是脸/头专用。

**① 写「必须 / 一律 / 全都」这类全称规则前，先扫全量找反例。**
- 成本 30 秒。本案例：`tpaccli metaparts --filter head_` 列全包 30 个头，一眼能看出「28 个该清、2 个该留」。
- 🔴 **新学到的规则必须和已有规则对账**：学到 X 时先问一句「这条推翻了我之前存的哪条？」——
  记忆/文档里两条互相矛盾的规则共存时，**谁在上下文里谁就赢**，这是最隐蔽的错法。

**② 改全局管线前，先列出它的全部服务对象，逐个过规则。**
- 本案例：`install_pack.py` 是**所有头共用**的后处理入口，加一步无条件操作 = 对包里每一个头生效；
  我只想了要修的那 28 个。
- **验证结论必须写清「验了谁、没验谁」**——「验证通过」四个字不带范围 = 下游会读成「全部通过」。
  本案例：用户当时在看战无2 的英雄，蒂法不在视野里 → 「已修 + 实机验证通过」是**假绿**。

**③ 关于「过去是什么样」的断言，一律先查归档；查不到就标「待查」，不许当论据。**
- 本案例：注释里那句「蒂法 / 萨菲罗斯验收时标记也是空的」**我从没查过** —— 来源是把不相干的
  **件位顺序**结论当成了 flags 结论。
- 一手证据通常唾手可得：历史包就躺在 `Debug/offline/`，一条 `tpaccli metaparts` 就能验。
  **二手结论在链子上传一手就变成了论据**，这是最贵的错。
- 推论：**「某某之前是好的 / 空的 / 通过验收的」这类回忆型事实，永远先标「待查」。**

**事故四步（复现用）**

| 步 | 做了什么 | 本该怎么拦 |
|---|---|---|
| 1 | 28 张脸糊了、查不出 → **用户提醒**才在 `Knowledge/蒂法换头工程.md` §16 找到答案 | 长文档**别按节跳读**；跳掉的那节可能就是答案 |
| 2 | 「这 28 个该清」→ 抽象成「自定义头必须清」 | 规矩①：扫全量找反例（反例就在我 09-14 自己写的记忆里：「**三个头的 flags 状态正好相反，别一刀切**」） |
| 3 | 为支撑新规则，编了一句「验收时也是空的」 | 规矩③：查归档（成本 = 一条命令 + 一个就在 `Debug/offline/` 的历史包） |
| 4 | 写成 `install_pack.py` 第 3.5 步**无条件**跑，还写进两处**规则文档** | 规矩②：枚举服务对象。⚠️ **一次误判写进规则文档 = 变成制度，会让下一次继续错** |

⚠️ **当时手边就有刀没用**：同一天我刚写下「诊断顺序：先 `tpaccli metaparts` dump 产物真身，再谈理论」——
真到要下**全局判断**时一次都没跑。**纪律写下来 ≠ 会用它；下全局判断之前，把相关纪律念一遍。**

（本次事故的完整技术记录见下方「自定义头的脸部贴图糊成一片」条 + `plans/战国无双换装批量落地.md` R2 追加段。）

---

## 🔴 建号点「完成」就崩（`CampaignUIHelper` 静态构造 NRE）→ 一个「运行时零动作」的补丁（2026-09-23）

**症状**
- **1.3.x 起都会崩（1.3.15 与 1.4.8 实测复现；1.5.x 未测但同理）**，纯功能包模式（没装内容包）走原版剧情战役建号：**捏脸阶段点「完成」→ 立即崩**。
- VS 栈：`ButtonWidget.HandleClick → GauntletView.OnCommand → … → CharacterCreationNarrativeStageView..ctor → CharacterCreationGainedPropertiesVM..ctor → CampaignUIHelper..cctor → GameTexts.FindText → NRE`。
- 引擎日志（`Documents\…\Configs\ModLogs\default<日期>.log`）里同一触发点写作 `Exception occurred inside invoke: ExecuteDone / Target type: FaceGenVM`。
- **1.2.12 上同样操作不崩**（原因见下，已验证）。

**根因**：补丁 `CampaignMode/EncyclopediaHelmetPatch.cs` 的 `EncyclopediaHeroHelmetPatch`（补 `EncyclopediaHeroPageVM.Refresh`，给自建 race `lwn_` 补百科立绘头盔）。

- 🔴 **崩溃那次运行里它"运行时零动作"**：一条 `[EncHelmet]` 日志都没有（原版 race 上它按设计静默早退）；6 个诊断探针也显示 `GameTexts._gameTextManager` **全程非 null**。
- 🔴 **机制未查明（不许编）**。主假说（能解释全部矛盾，**未证实**）：补丁类挂载时，其静态初始化器 `AccessTools.Field(typeof(EncyclopediaHeroPageVM), "_hero")` 强制解析了那个类型 → **提前触发了同程序集（`CampaignSystem.ViewModelCollection`）里 `CampaignUIHelper` 的静态构造**，而那一刻 `GameTexts` 还没初始化（`Game.Initialize` 尚未跑）→ cctor NRE → **CLR 把「类型初始化失败」永久缓存** → 到叙事阶段第一次真正用到它时，抛的是**当年那个被缓存的异常**（所以栈指向 `FindText`；而探针再也看不到 null —— 那次 null 发生在探针挂上之前）。
- **坐实手段（下次做）**：把 `GameTextsFindNullProbe` 排到补丁挂载顺序**最前面**再复现 —— 就能抓到"谁在 GameTexts 未初始化时提前碰了它"的调用栈。

**为什么 1.2.12 不报错（已验证，不是推断）**
- 崩溃所在那条路径 **1.2.12 根本不存在**：`CharacterCreationNarrativeStage` 这个类型在 1.2.12 的 `SandBox.GauntletUI.dll` 里**没有**、1.3.15 里**有**（叙事式建号 = 1.3.x 新体系）。
- ⇒ 不是「兼容性差异」，而是**触发点不存在**。同一个补丁在 1.2.12 上照样挂着，只是没有流程去踩它。

**为什么版本兼容体系抓不到它**
- `VersionCompat` 管的是「API 签名在某版本开始存在 / 改名 / 变参」；**这个坑零签名差异**：`EncyclopediaHeroPageVM.Refresh` 与 `_hero` 在 1.2.12 / 1.3.15 / 1.4.8 / 1.5.x **四档全在**（补丁目标扫描器四档全绿），编译也过 —— **签名层面它"完全兼容"**。
- 失败发生在**运行期某条流程的第一次触发**上，在所有离线检查器（补丁目标存在性 / XML / 清单 / 数据）的射程之外：没有一条能在"装之前"预测"某个补丁会不会引发第三方类型的初始化失败"。
- ⚠️ 旧版本**没有那条流程** ⇒ 在 1.2.12 上做再多验证也永远看不到它。

**排查手法（这次花了 7 轮二分，记下来复用）**
1. 启动器里**取消勾选 LWN** 跑一遍 → 不崩 ⇒ **确定是我们**（最快、信息量最大的一步）。
2. 用 `config.json` 的 **`DisabledPatchClasses`**（逗号分隔类名；`*` = 全关但保留诊断探针）逐类二分 —— **改配置重启即生效，零重编**。实现 = `Core/MySubModule.cs` 的逐类挂载循环（顺带修掉了 `PatchAll` "一个类挂失败掐断全部"的隐患）。
3. 每轮**先看日志再切**：崩溃快照 `Debug/crash/*.txt` 里存着**崩溃当时的整段运行日志**，谁在那一刻真动过手一目了然。
4. 🔴 **本条最反直觉的教训：真凶恰恰是"日志里零动作"的那个。** 候选砍到少数时，**"它没打日志"不能作为排除依据** —— 补丁的**挂载/静态初始化本身**就有副作用；而且日志节流（如 `FaceGenRace` 只在数值变化时打）会掩盖更早的真实动作。

**规避 / 修法（已落地，实机验证通过）**
- `Core/MySubModule.cs` 增「**内容包专属补丁**」名单：**没装内容包（纯功能包模式）时不挂**。`EncyclopediaHeroHelmetPatch` 入列（只对 `lwn_` 自建 race 有意义，没内容包就没有这种 race）。
- **同类越界待收窄**（在纯原版战役上也会生效、按同判据也该收窄；**与本次崩溃无关**，单独关掉都不崩）：`BackstoryCampaignBehaviorPatch`（掐原版前史）、`CharacterCreationCultureStageSortPatch`（跳过原版文化排序）、`CharacterCreationCultureVisualFallbackPatch`、`FaceGenOnSelectRaceGuard` / `FaceGenRaceDefaultBodyPatch` / `FaceGenRaceGenderFilterPatch`。
- 通用教训：**写补丁前先问一句「没有内容包时它有意义吗」** —— 没意义就别挂。

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

## PowerShell 正则替换串 `$1`/`$2` 没展开 → XML 里被写成字面量 `$1`

**症状**
- 用 `[regex]::Replace($txt, $pat, '…$1…$2…', 6)` 做「回退一处改动」后，文件里出现字面量 `$1equipmentType="Civilian"$2 id="Item.…"`
  —— 6 行 `<EquipmentSet>` 的标签头被吃掉、属性行成垃圾 → **整份 XML parse 失败**（`mismatched tag: line 62`）。
- 表面看是"替换没生效"，实际是**替换生效了但替换串本身是脏的**——文件比改前更坏。

**根因**
- `$` 在 PowerShell 里有多层解释者（PS 自己的变量展开 / .NET `Regex.Replace` 的分组引用）：
  双引号串里 PS 先吃一遍；即使写单引号"原样传给 .NET"，经方法参数绑定后也可能没按 .NET 分组语法生效——
  实测就是**原样写进了文件**。这类"谁在解释 `$`"的问题不值得现场推理。

**规避**
- 🔴 **改数据文件（XML/CSV/JSON）一律用 Python 小脚本，不用命令行字符串替换**：`re.sub` 的 `\1` 语义确定，
  且能**写盘前** `minidom.parseString(out)` 验证 + 幂等复跑（范本 `Scripts/fix_neutral_culture_templates.py`）。
- 已经用 PowerShell 做过批量替换的：**改完立刻 parse + `git diff` 对账**——`git diff` 是唯一能证明
  「只改了我以为的那几行」的手段（本次就是靠它定位到 6 行、并复核复原后只剩预期改动）。
- **损坏后的正确复原姿势**：`git diff` 定位坏行 → 按 `git show HEAD:<file>` 的原内容 `Write`/`Edit` 精确复原
  → parse 通过 + `git diff` 只剩预期改动 才算完（**别**再用一次字符串替换去"反向修"）。
- 2026-09-13 实踩：改 `taikou_equipment_sets.xml` 回退一步 → 6 行写坏 → parse 报错 → 按上述复原，全程 3 分钟。

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

---

## `Campaign.Current` 先置后初始化：getter 无空守卫 → 加载窗口期 NRE（正确修法 = 零异常就绪检测）

**症状**（实机 2026-09-03，1.2.12 开新战役）
- `System.NullReferenceException`，`Source=TaleWorlds.CampaignSystem`，栈顶 = `Campaign.get_CampaignEntityComponents()`——**栈顶是 getter = Current 非 null**（若 Current 为 null，异常标在调用点行、不进 getter 帧），是 getter 内部字段为 null。
- 触发点：mod 在 `Campaign.Current != null` 时立即访问 `Campaign.Current.CampaignEntityComponents`。
- 隐藏危害①：安装逻辑「先标记完成再 try」→ 失败一次后**整局不再重试** → 那层性能包裹（campaign 行为 `OnTick`）永远没装上——比弹窗更隐蔽：功能静默缺失，无人知道。
- 隐藏危害②：改成 `catch (NRE) + 下帧重试` 后游戏不崩，但**被 catch 的预期异常在每帧热路径上反复抛，VS「抛出时中断」按次弹窗刷屏**——看起来"还是有问题"。

**根因**（1.2.12 ~ 1.5.1 四版本反编译确认，同形态）

```csharp
// Campaign.cs（全版本如此）
public MBReadOnlyList<CampaignEntityComponent> CampaignEntityComponents
    => _campaignEntitySystem.Components;   // 无空守卫，null 直接炸

// 时序窗口（1.2.12 实锤）：
SetLoadingParameters → Current = this（Current 先置 ✔，此时 _campaignEntitySystem 还是 null）
  → …加载过程（多帧）…
OnNewGameCreatedInternal / OnLoad → _campaignEntitySystem = new EntitySystem<>()（后初始化）
```

- 引擎自己从不调这个 getter → 无守卫没人踩；mod 拿 `Campaign.Current != null` 当就绪信号 → 撞上窗口。
- getter 是唯一公开访问口，`?.` 救不了（NRE 发生在 getter 内部）。

**规避**（已修复 `Diagnostics/PerfWrapper.cs`）

- 此类「Current 先置、内部字段后初始化」的引擎属性：**就绪检测必须是零异常方式**——反射读引擎初始化所依赖的私有字段，就绪前根本不碰 getter；字段名验证过 1.2.12~1.5.1 同为 `_campaignEntitySystem`：

```csharp
private static readonly FieldInfo _campaignEntitySystemField =
    typeof(Campaign).GetField("_campaignEntitySystem", BindingFlags.Instance | BindingFlags.NonPublic);

private static bool CampaignEntitySystemReady()
{
    Campaign campaign = Campaign.Current;
    if (campaign == null) return false;
    if (_campaignEntitySystemField == null) return true; // 未来版本字段改名：放行，外层 catch 兜底
    return _campaignEntitySystemField.GetValue(campaign) != null;
}

// Tick：
if (!_campaignDone && Campaign.Current != null && CampaignEntitySystemReady())
{
    _campaignDone = true;
    try { InstallCampaignTargets(); }
    catch (Exception ex) { DebugLogger.Log($"[PerfWrap] campaign install failed: {ex.Message}"); }
}
// 未就绪 → 下帧重试（无异常可抛，VS 不再弹窗）；就绪后一次性安装
```

- 落地：`Diagnostics/PerfWrapper.cs` Tick + `CampaignEntitySystemReady()`。
- 排查口诀：**「`Campaign.Current != null` 检查过了还 NRE」= 引擎对象先置后初始化**——去反编译 getter 看它访问的字段在哪条初始化路径上，再用「反射读该字段」做零异常就绪门。
- 同类先例：`Team.Invalid` 单例（non-null 但内部 `_mission` 为 null，见本文顶部条目）——**「非 null ≠ 就绪」是引擎对象常态**。

## Module Editor 启动 RGL 报 `Invalid submodule tag in .../SubModule.xml` → 第三方 mod 的社区扩展 tag（BUTR/BLSE）

**症状**：打开 Module Editor（Ctrl+E）时弹 RGL WARNING：`Invalid submodule tag in file:///.../Modules/Bannerlord.MBOptionScreen/SubModule.xml`（名字点名哪份 XML），**点了确定还是进不去**，或编辑器卡在加载。1.5.2 与 1.2.12 Modding Kit 均复现。

**机理**：编辑器的 RGL **扫描 Modules 目录下所有 SubModule.xml**（不读 Launcher 勾选态、**无视 Windows 隐藏属性**），按**引擎 SubModule schema** 校验元素。带社区扩展块**`<DependedModuleMetadatas>`**/**`<Tags>`**（BUTR/BLSE 的社区依赖元数据，`key="DumpXML"` 等）的 mod——典型 = `Bannerlord.MBOptionScreen`、`Bannerlord.UIExtenderEx`——被判定非法 tag → 编辑器拒载。

**规避**（已在 1.2.12 库 + 1.5.2 主库修过，均 `.bak` 备份）：

```xml
<!-- DependedModuleMetadatas commented out (engine schema rejects); prev .bak available -->
<!-- Tags commented out (engine schema rejects); prev .bak available -->
```

- 这两个块**引擎游戏运行不读**（仅供社区加载器/BLSE 元数据）→ 注释掉后 mod 照常玩（织丰/PCL 环境验证过）。
- 三选一：① 正则注释这两块（`(?s)<DependedModuleMetadatas>.*?</DependedModuleMetadatas>`，`Tags` 同理）；② 把 mod 文件夹整体改名 `.off`（编辑完改回）；③ Windows 隐藏目录**无效**（编辑器按路径树扫，不按属性）。
- 排查口诀：**「编辑器扫描 ≠ Launcher 勾选」**；报错点名哪份 xml，改哪份。
- 相关：本坑与"编辑器只认 schema 内元素"同源于社区 mod 生命周期；Keep `SubModule.xml.bak` 备还原。

---

## 内容包音乐工程（Psai / mbproj）—— 启动四连坑到主菜单 BGM 静默（2026-09-08 实机，Taikou + 1.5.2）

**症状链**（同一天 4 次实机，全部已修）：
1. 启动即弹报错框：`ArgumentException: An item with the same key has already been added`，线程池栈：`psai.Editor.PsaiProject.BuildPsaiDotNetSoundtrackFromProject` → `psai.net.Logik.LoadSoundtrackFromProjectFile` → `MBMusicManager..ctor`。
2. 修完再弹：`KeyNotFoundException`（`MBObjectManager.MergeElements` → `CreateMergedXmlFile` → `Module.CreateProcessedActionSetsXMLForNative`）。
3. 两个都修完：主菜单 BGM 仍原生/全静默——**日志显示"重映射命中"但无声**。

**根因（四层 + 第五层，全部反编译确认）**：
1. **mbproj 节点名坑**：引擎 `XmlResources.GetMbprojxmls` 用 `SelectSingleNode("base").SelectNodes("file")` —— **只认 `<file>` 节点**。数据包原用 `<Module id=...>`（11 行）→ 0 匹配 = 整个文件静默失效（1.2.12 与 1.5.2 引擎一字不差）。
2. **mbproj 按 soln id 跨模块合并**：`GetMergedXmlForNative("soln_action_sets")` 合并**所有**声明该 id 的模块文件（MyMapTest/Native/Shokuho/Taikou/…）。拷贝模板死档（physics_materials/action_sets/skins…）**一次性全激活** → Taikou 的 action_sets.xml 与 Native 同源冲突 → `MergeElements` 的 ToDictionary 缺 key → KeyNotFoundException → 启动崩。**铁律：mbproj 启用一个为一个**；数据包正路 = SubModule.xml 段（19 个 TaikouCampaign 段），mbproj 只管引擎级 soln。
3. **音乐工程主题 id 与 Native 模板同源**：Taikou 的 Main Theme 与 Native **逐行同 id**（ThemeId=5 / SegmentId=17）→ psai 合并 `soundtrack.m_themes.Add(theme.Id)` / `m_snippets.Add(segment.Id)` 重复键。规避 = 工程加 `<ModuleIdPrefix>`（1.3+ 才有该机制；**1.2.12 的 psai 无此机制**，二进制 0 命中，走 LWN `MenuSoundtrackReload` 单文件重载链）。
4. **🔴 ModuleIdPrefix 是【字符串】前缀，不是数字**：psai `theme.Id = int.Parse(prefix + theme.Id)` = `int.Parse("1000000" + 5)` = **10000005**；写成数值加法 `1000000 + 5` = 1000005（少一个 0）→ 按 1000005 查主题 = 查无 = **静默无声**——且 LWN 日志"主题重映射成功"照打，**日志命中 ≠ 成功**。
5. **引擎选菜单主题 = 硬编码枚举**（为什么上了前缀还被忽略）：`MBMusicManager.ActivateMenuMode` 实锤 `PsaiCore.Instance.MenuModeEnter((int)MusicTheme.MainTheme, 0.5f)`，MainTheme=5（枚举实测）→ 永远查 Native 主题；模块主题 id 平移后（10000005）**永不命中**。官方 NavalDLC = 同型先例（模块激活 → 换枚举值）。规避 = LWN Harmony 补丁重映射（`PsaiMenuThemeRemapPatch`，Core/MenuSoundtrackPatch.cs）：内容包激活时 `5 → int.Parse("1000000"+5)`。

**引擎能接受的模块音乐最小结构**：`ModuleData/project.mbproj`（`<base type="solution">/<file id="soln_soundtrack" name="music/soundtrack.xml"/>`，解析器路径 = `ModuleHelper.GetMbprojPath` = `Modules/<Id>/ModuleData/project.mbproj`）+ `music/soundtrack.xml`（PsaiProject 1.0）+ `music/PC/*.ogg`（psai 相对工程目录解析）。

**排查口诀**：
- 启动早期弹报错框（无主线程栈、栈在 ThreadPool）= 先怀疑 **MBMusicManager 后台线程**（`ProcessCreation` = QueueUserWorkItem 创建，主线程死等）——psai 工程加载发生在这。
- 主菜单 BGM 排查三步：① `[MenuSoundtrack] ... project.mbproj 存在 = 是`（加载链路通不通）② `[MenuSoundtrack] 主题重映射`（补丁命中否）③ **id 位数核对**（字符串拼接：10000005 ≠ 1000005）。

---

## 引擎"可选"参数传 null ≠ 安全空值（Campaign 构造 AdvancedStartOptionsData，2026-09-08 实机）

**症状**：1.5.0+ `Campaign(CampaignGameMode, AdvancedStartOptionsData)` 传 `null` → `CampaignOptions..ctor → AdvancedStartOptionsExtensions.TryGetSeed(null, out _)` 解引用 NRE，新游戏启动即崩（栈在引擎内，mod 侧看不到）。

**规避**：传引擎构造/静态入口参数前，**反编译看消费点**是否 null 安全（`TryGetSeed` 一类扩展方法普遍无 null 保护）。给空实例 `new AdvancedStartOptionsData()`（少字段 = 引擎走默认分支），不要给 null。

**口诀**：**宁可给空实例，不要给 null**——引擎"可选参数"的契约基本是非 null 假定；"编译能过" ≠ "运行安全"。

---

## "以为是新文件"的 Write 覆盖 —— 先 git status 再写（2026-09-08 实机自踩）

**症状**：排查中误判"Taikou 没有 project.mbproj"（`find -maxdepth 3` 漏掉深度 4 的 `ModuleData/`）→ 直接 Write 覆盖——事后 `git status` 发现它是**已跟踪文件**（显示 ` M` 而非 `??`）→ 已有内容被覆盖，需 `git show` 找回。

**根因**：部署目录（`Modules/Taikou`）本身是 git 仓库 + find 深度不足，两个误判叠加。

**规避**：
- 写任何"疑似新文件"前：`git status --short <path>`（` M` = 已跟踪——立刻停下核对原内容引）＋ `git log --oneline -1 -- <path>` 看出处。
- "文件不存在"结论必须在足够深度的搜索后下（`ModuleData/` 在 maxdepth 4）。
- 覆盖后立刻 `git diff --stat` 核对差异；改坏恢复 = `git show HEAD:<path> > <path>`（字节级）；恢复前先 `git show HEAD:path > path.orig_backup` 取证。

---

## 地图拾取只认「only_collide_with_raycast 碰撞体」——定居点交互链（黄圈/hover/点击/进城）的统一入口（2026-09-09 京实机）

**症状**（四轮误判实录）：新地图上定居点**无黄圈、hover 不弹卡、点击图标无反应**；点地面可移动部队；而**探针/寻路/门可通行性全部正常运行**——即"世界一切正常，唯独玩家跟城隔着一层看不见的膜"。甚至往场景里加圈贴花实体后引擎能识别（`CircleLocalFrame=True`），点击依旧无效。

**根因**（反编译 1.2.12 实锤链）
- 地图射线拾取 `SelectEntitiesCollidedWith` / `GetCursorIntersectionPoint`（掩码 79617）**只命中带 `only_collide_with_raycast` body flag 的 physics 碰撞体**；普通 mesh / 贴花**不参与拾取**。
- 交互链全程走这一条入口：hover 信息卡（`OnHover`）/ 点击移动（`OnMapClick`）/ 移动目标黄圈（`TickCircles` 读 CircleLocalFrame）——**一处不命中 = 三处同时失效**。
- 官方定居点实体组必带：**`bo_town`**（`<physics shape="bo_sphere_collider"><body_flags><body_flag name="only_collide_with_raycast"/>`）+ `town_circle_decal`（tag map_settlement_circle）+ `gate_position`（main_map_city_gate）+ `banner_pos`（map_banner_placeholder）——织丰 Main_map 同款（bo_town ×8）。
- 我们在 v0 只摆了 capsule+tower mesh，**0 个 physics** → 0 命中。误判教训：先误判遭遇链/导航链/圈贴花 tag……四轮后才落到"拾取先决条件"——**排查顺序应反推：先确认"射线拾取是否命中"，再查数据链**。

**规避**
1. 场景造图对照表加「每定居点：bo_town 只碰射线碰撞体（尺寸≈城footprint）+ 圈/门/旗 4 娃」；拷贝官方场景时**逐字段抄**（包括 physics/body_flags/additional_features），不要只抄 tag/transform。
2. 排查口诀：**"探针全绿+实体编辑器能看到+但交互全无" = 拾取层没入口**——先 grep 场景 `only_collide_with_raycast` 是否存在于该定居点组（1.2.12/1.5.x 同机制）。
3. 该坑与 border/border_max、12 脚本实体同属「地图场景克隆必备实体清单」（`Knowledge/自定义世界内容包从零起步必备清单.md` 1.5）——新世界造图三条一起查。

---

## 进战斗部署即崩 KeyNotFoundException（CalculateTeamPowers 字典缺键）→ 引擎「先全量登记、后按键查」模式 + 队关系未成立（2026-09-09 实机）

**症状**（Taikou 遭遇 Oda 部队 → 菜单「攻击！」）：进战斗部署 Phase 即 `System.Collections.Generic.KeyNotFoundException`（栈：`BattlePowerCalculationLogic.CalculateTeamPowers` → `TeamQuerySystem` 惰性 Evaluate → `TacticCharge.GetTacticWeight`），玩家卡死在战斗加载。

**根因**（反编译 1.2.12 `TaleWorlds.MountAndBlade.dll` + `[BattlePowerGuard]` 实测日志实锤）
- `BattlePowerCalculationLogic.CalculateTeamPowers` 的模式：**第一遍**把 `Mission.Teams` 全队按**队伍自身 Side** `Add(team, 0f)` 进字典（team 落进 `dicts[team.Side]`）→ **第二遍**按**循环侧 i** 取 `dicts[i]` 当桶，对每个兵源 `Mission.GetAgentTeam(...)` → `dictionary[agentTeam]`。
- **键桶错位 = 崩溃条件**：当兵源归属的队与"循环侧桶"不一致 → `Dictionary.get_Item` 抛 KeyNotFound。
- **实测日志（Taikou 攻击 Oda 部队，10:54）**：`team=Mission Team: 1 side=0 playerSide=False playerEnemy=Mission Team: 0 playerTeam=Mission Team: 1 missionTeamCount=2` ——
  - 缺键 = **玩家队（Team 1）出现在敌方侧（side=0）枚举**；`PlayerEnemyTeam` **非 null**（=Team 0，排除"空键"方向）
  - 链路：`Mission.GetAgentTeam(origin, isPlayerSide=false)` **首分支 `origin.IsUnderPlayersCommand == true` → 直接返回 `PlayerTeam`（玩家队）** → 而查表桶是敌方侧 `dicts[0]`，玩家队登记在 `dicts[1]` → KeyNotFound
  - 即：**敌方侧兵源池里混入了 `IsUnderPlayersCommand=true` 的 origin**（自定义世界兵源组建/两军构造顺序不同导致；vanilla 同条件不触发=顺序天然成立）。该兵源的具体身份（哪类 origin）留待 T4 自建兵种数据时复核，现象与兜底已实锤。
- 触发链：部署侧完成 → `DeployFormationsOfTeam` → `Team.ResetTactic` → 战术权重查询 → 惰性 Evaluate——"看似随机"的字典错误，实为固定的键桶错位。

**规避**
1. **LWN 通用兜底**（已落地）：`CampaignMode/BattlePowerCalculationGuardPatch.cs` —— `CalculateTeamPowers` 前缀替换（完整重实现 + 空键兜底：`GetAgentTeam` 拿不到/字典没登记 → 补登记 0 战力，战斗照常；同时打 `[BattlePowerGuard]` 诊断日志确认缺失键身份）。类型+方法名双字符串运行期解析；1.5.x 若抽走该类 = 静默跳过。
2. **真根因修复**：让玩家阵营与敌队的关系正常成立（数据/时点修复）——先看 `[BattlePowerGuard]` 日志确认空键是 null 还是缺队，再定数据动作（参考织丰 player_faction `is_minor_faction="true"`、玩家开局王国归属等）。
3. **排查口诀**：**"进图/进战斗一部署就崩 + 字典 KeyNotFound + 栈尾是引擎惰性查询" = 有查询键依赖的运行时状态（队关系/登记表）没成立**——查"谁填键、键从哪来"，别在异常帧上找原因。
4. ⚠️ **编译验证版本坑（2026-09-09 自踩）**：`dotnet build` 环境变量取的是 **Bash 进程快照**（=主环境 Steam 1.5.2），而真目标 = 1.2.12 —— 曾静默对着 1.5.2 DLL"编译通过"（`MissionAgentSpawnLogic` 类在 1.5.2 已不存在，`IMissionAgentSpawnLogic` 接口两版同构、`GetAllTroopsForSide` 不在公共接口 → 该类要用**反射调用**）。验证必须显式 `MB2_PATH="…MB2_1.2.12…" dotnet build`；跨版本类型一律走"接口存在性验证 + 反射"（本例 = `GetMissionBehavior<IMissionAgentSpawnLogic>()` + 反射调 GetAllTroopsForSide）。

---

## 自定义 GameType 下某说话/文本"消失了"或对话崩溃 → SandBox 文本段带 GameType 白名单被过滤（2026-09-09 实机）

**症状**（Taikou 找织田信长对话）：玩家开场台词正常显示 → 玩家一开口（进领主介绍句）即 `NullReferenceException`（栈在 `LordConversationsCampaignBehavior.conversation_lord_introduction_on_condition`），对话无法继续。

**根因**（反编译实锤 + SubModule.xml 实锤）
- `FindMatchingTextOrNull(id, character)`（1.2.12 ConversationManager 反编译）：**文本键不存在 → 返回 null**（无空保护）。
- 领主介绍句用的 `str_comment_*` 文本定义在 **SandBox `ModuleData/comment_strings.xml`**，其在 SandBox SubModule.xml 注册段带 `IncludedGameTypes` 白名单 = `Campaign` / `CampaignStoryMode`（SandBox SubModule.xml:227-231）——**自定义 GameType（TaikouCampaign 等）不在白名单 → 整个文件被过滤 → 文本缺失 → null → NRE**。
- 玩家开场台词"正常"是因为它们属于 module_strings 系（不同文件/加载路径），属"半通半堵"的迷惑观感。
- 织丰对照：织丰自备全套文本（自家行为类 + 自家 str_comment_*），故无此坑。

**规避**
1. **内容包自备文本（织丰做派）**：把官方 copy 进 `Modules/<mod>/ModuleData/<文件>.xml` + 在包 SubModule.xml 注册 `<XmlName id="GameText" path="..."/>`（挂自己 GameType 段）。Taikou 落地 = 拷贝官方 `comment_strings.xml`（155KB，322 条 str_comment_*）。
2. **LWN 通用兜底**：`LordIntroConditionGuardPatch`（该条件前缀：文本/Clan/城镇 OwnerClan 任一缺失 → 跳过该句不崩 + `[LordIntroGuard]` 日志）——任何内容包都能被兜住。
3. **排查口诀**：自定义战役里"某引擎功能缺数据就该崩溃/某句话没了"——先反查**该对象/文本所在文件的 SubModule `<XmlName>` 段是否带 GameType 白名单**，再谈数据自洽。
4. 🔴 **全量盘底（2026-09-09 二次实锤）**：**SandBox 的 9 个 GameText 文本段全部**带白名单（`module_strings` / `world_lore_strings` / `companion_strings` / `wanderer_strings` / `comment_strings` / `comment_on_action_strings` / `trait_strings` / `voice_strings` / `action_strings` —— SandBox SubModule.xml 扫描实锤，Native 侧仅 multiplayer_strings 白名单、其余全量放行）。自定义 GameType 下**缺任何一个 = 对应 str_* 查询报错/NRE**（开战新闻 = action_strings、领主介绍 = comment_strings、对话台词 = module_strings…）。**内容包修法 = 9 文件官方原样拷贝 + 自家 SubModule GameText 段注册**（Taikou 已全量落地）；**排查方法**：扫描 SubModule.xml 里 `XmlName id="GameText"` 且带 `IncludedGameTypes` 的 path 列表，逐一拷贝。**NEW GAME 前必跑清单**：新建战役后看一眼运行日志末段（Text id 报错是运行期才炸，启动不报）。

---

## 自定义地图相机"空气墙"：到某条坐标线就动不了 → 场景缺 border_min/border_max 实体（2026-09-08 实机）

**症状**（日本图，实机复现）
- 相机（WASD/双击跳镜）能在地图中部自由移动，**到一条直线就永远停住**——没有边框、没有报错、画面正常，就是"空气墙"。
- 初始机位正常、城图标（京）看得见——**但京都恰好在墙外一点点**：能看见，靠近不了。
- 本坑无任何异常日志/崩溃，纯行为异常，属于最容易当成"操作习惯"误判的一类。

**根因**（反编译实锤，调用链）
```
相机目标位置每帧被钳进 [Campaign.MapMinimumPosition, MapMaximumPosition]
  └─ SandBox.View.dll MapCamera `ComputeMapCamera`（ClampFloat，反编译实锤）
       └─ Campaign.MapMinimumPosition/MapMaximumPosition ← Campaign.LoadMapScene
            └─ SandBox.MapScene.GetMapBorders（SandBox.dll）
                 └─ 读场景内两个命名实体：border_min / border_max（名字硬编码，反编译实锤）
```

- **border_min / border_max = 大地图摄像机边界**：两个普通实体，各拿一个坐标，引擎按名字硬查（`GetFirstEntityWithName`）。官方 bigmap = (62,30,0)/(790,640,620)；织丰 Main_map = (87.4,105.4,-7.98)/(2100,2100,1000)。
- **版本行为差异（本坑核心）**：
  - **v1.2.12**：缺实体 → 引擎**静默兜底** min=(0,0) / max=**(900,900)** / height=670（SandBox.dll 反编译实锤）→ 相机被钳在 0~900 矩形 → "空气墙"在 x=900 / y=900。**无任何提示**，模拟出"活着的系统"。
  - **v1.5.x**：**无兜底**，`GetFirstEntityWithName("border_min").GetGlobalFrame()` 直接解引用 → 缺一个实体 = **进图即崩**（比空气墙更狠）。
- 为什么"看得见京却过不去"：京 (969,421)，墙面 x=900——城图标悬在墙外 69m。
- 为什么克隆时缺：从 bigmap 基底克隆地形常见清单里只带地形 + 脚本实体，**边界实体属于"看不见的东西"，克隆时天然被遗漏**。

**规避**
1. **地图场景必备实体清单 + border_min/border_max**：任何克隆/重建的大地图，出图前 `grep scene.xscene` 确认两个实体存在（连同 12 个官方地图脚本实体——完整清单在 `plans/太阁数据加载taikou-campaign-boot-20260907.md` 雷 28）。
2. **取值 = 地形实际范围**：读 scene `<terrain>` 的 `node_dimension × node_size`（日本图 16×10 节点 × 128m → (0,0,0)-(2048,1280)），`border_min` 用 (0,0,0)；`border_max` 的 **z 值管相机最大缩放距离 + 远裁剪面**（取织丰 1000，别抄官方 620——那是卡拉迪亚地图尺寸）。
3. **判别口诀**：
   - "相机沿一个正交矩形边界停住、零报错" = 缺 border 实体（1.2.12 引擎兜底 900×900）；
   - "进图就崩、栈在引擎侧" = 同因的 1.5.x 表现。
   - 一听到"空气墙/走不出去"先查场景 grep `border_min`，再去查输入/操作。
4. **实装防线**：`CampaignMode/MapBorderDiagnosticPatch.cs`（`GetMapBorders` Postfix，进图打一行 `[MapBorder]` min/max/height；命中引擎兜底值 (0,0)/(900,900)/670 时打警示）——重建地图后看一眼日志即知边界是否健全。
5. 边界实体只是坐标标记，**不动 navmesh**：加/删实体无需重新生成 navmesh（与雷 28 实体插入同结论）。

---

## 🔴 内容包资产"全部就位"却一个纹理都取不到 → 模块下多了个空 `Assets/`

**症状**（2026-09-12 实机，Taikou 立绘）
- 立绘/头像全部空白，但**所有中间层都"正常"**：sprite 名在 `GUI/*SpriteData.xml` 里查得到、`SpritePart` 的 SheetID/尺寸对得上、`cat.IsLoaded=true`、`SpriteSheets[i] != null`、`[SpriteAssets] 加载 sheet ...` 日志照打。
- **零 C# 异常**。唯一的线索在**引擎自己的日志**里（`C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt`）：`Cannot find texture: lwnprof_bustup_517`。
- 运行期拿到的纹理对象非 null，但**引擎纹理名 = `material_error`**、尺寸 512×512、像素全 0 —— 是引擎"找不到纹理"的占位图。

**根因**（引擎原生字符串实证）
- 引擎在模块目录下找资产目录，候选名字是**有序清单**：`Assets` → `AssetPackages` → `DsAssetPackages` → `EmAssetPackages` → `AssetsBackups` → `AssetSources`，**取第一个存在的**。
- `Modules/Taikou/` 下同时有 ModKit 编辑器建的**空 `Assets/`** 和放 tpac 的 `AssetPackages/` → 空 `Assets/` 占住第一位，4 个 tpac **永远不被加载**。
- 判据（一眼看穿）：引擎日志里该模块那行 —— `Loading packages $BASE/Modules/Taikou/Assets...` ❌ 应为 `.../AssetPackages...`。

**规避**
1. **内容包的资产目录只留一个**：`AssetPackages/`。编辑器的 `Assets/` / `EmAssetPackages/` / `AssetSources/` 空目录**一律改名或删除**（改名 `Assets_disabled` 更稳，可随时退回）。
2. ⚠️ **ModKit 编辑器会重建 `Assets/`** → 此坑会复发。复发信号固定：立绘空白 → 查引擎日志 `Loading packages` 那行。
3. **诊断动作**：`custom.texprobe <spriteName>`（导出运行期真实纹理 + 打印纹理指针链）——对象/日志全绿时的唯一出路，见 [wheels.d/ui.md](wheels.d/ui.md)「立绘/任意 Sprite 上屏」。

---

## 🔴 UI 控件"值都设对了"却什么都不画 → 尺寸算成了 0

**症状**（2026-09-12 实机，立绘面板）
- `ImageWidget` 的 `Sprite` 已赋值（打出来就是那个 sprite 对象）、`IsVisible=true`、`IsEnabled=true`、父容器在渲染、同容器的**另一个同类控件画得出来**——就是不显示。
- 零异常、零日志。

**根因**：**0 宽的控件什么都不画**。等比缩放写成「以高为准反推宽，再 `if (w > boxW)` 改以宽为准」，当 `boxW/boxH` 恰好等于 sprite 宽高比时（立绘 2:3 撞目标框 560:840）边界擦边走进分支，**把宽压成 0**。
- 同类控件没事是因为它撞不到这个边界（正方形）。
- 症状极具迷惑性：看起来像"资源没加载"，实际跟纹理毫无关系。

**规避**
1. **等比缩放永远用「两方向各算系数取小值」**，不要写"先定一边再修正另一边"：
   ```csharp
   float k = Math.Min(boxW / sw, boxH / sh);   // 塞得进且不变形，数学上不可能出 0
   w = sw * k;  h = sh * k;
   ```
2. **控件体检日志**（排查任何"设了值但不画"）：打 `SuggestedWidth/SuggestedHeight`（**任何一个是 0 就是它**）+ 两个同名 `Sprite` 属性（`ImageWidget` 的 `Sprite` 是 `new` 出来的，基类 `Widget.Sprite` 是另一个）+ `IsVisible/IsEnabled`。
3. 判别口诀：**"另一个同类控件能画，这个不能" → 先比两者的建议尺寸，再去怀疑资源。** 先查资源是南辕北辙（本次就是这么绕了一大圈）。

---

## 自建头部网格 → `face_generator.cpp:864` 断言 / `AddSkinMeshes` AccessViolation（2026-09-13，TifaHead）

**症状**：换上自建头网格后 —— wEditor 版启动即断言 `face_generator.cpp:864 Expression: non-tested code execution!`；release 版进捏脸界面时 native `AccessViolationException`（栈顶 `AgentVisuals.AddSkinMeshesToEntity`，托管栈丢失）。

**根因是一串"声明值 vs 实际数据"对不上**，逐个修才逐个露头（前 8 轮打补丁全失败的原因：**在替编辑器伪造它本该自己算的数据**）：

| # | 缺什么 | 后果 |
|---|---|---|
| 1 | **FBX 没带骨架** → 编辑器算不出 `SkinDataSize=0` / `UnknownInt2=0` / `VertexStreamData` 内容错 | 引擎拿"声明 0 根骨"的网格去绑骨架 → 越界 |
| 2 | `Assets/` 是编辑器半成品（无 `VertexStreamData`、贴图无像素）却因"第一个存在的目录"被引擎优先读 | 读到半成品 |
| 3 | `Mesh.VertexKeyCount`（**引擎分配 morph 缓冲的键数**）与实际 morph 帧数不符（声明 59 装 101） | 缓冲越界 |
| 4 | `Mesh.MaterialFlags` 缺 `face_base_mesh`/`face_mouth_mesh`/`face_eye_mesh`/`face_eyelash_mesh` 角色标记 | 生成器认不出哪个子网格是脸/嘴/眼/睫 |
| 5 | 材质配方偏离（缺 `skinning`/`doubleuv` 顶点布局、shader 用错） | 顶点布局与蒙皮数据对不上 |

**规避**：
- 🔴 **正解 = 让编辑器自己算**：给 FBX **绑上官方骨架**（`modding_resources\skeletons\human_skeleton.fbx`）再导入。编辑器一次算对全部字段（实测 `UnknownInt2=28` 与能跑的参考 mod `xxFemaleHead` 完全一致）。
- 🔴 **官方骨架 FBX 单位标 `centimeter`、数值其实是米** → Blender 导入后头骨落在 **1.57 厘米**，与 1.57 **米**的网格差 **100 倍**。绑骨前必须把骨架缩放到与网格同空间（`k = 网格中心z / 头骨z`）。骨骼名自带引擎编号：`bip01_head_13` = 骨骼 13。
- **跑游戏前让 `Assets` 改名让位**（编辑器要用它，游戏不能读它）——做成一对 bat，见 [CLAUDE.md](../../CLAUDE.md)「模块资产目录的编辑器/游戏模式切换」。
- **诊断工具**（`tools/tpactool/`）：`tpaccli morphinfo`（网格诊断）/ `morphfix`（补 morph 帧+键数）/ `skinfix --fullmat`（补蒙皮+角色标记+四角色材质配方）/ **`meshdiff`（全字段反射差分，和能跑的参照物逐行对比）**。
- **排查口诀**：**"自建资产 + 引擎启动断言/捏脸崩" → 先和能跑的同类 mod 做全字段差分，别逐个猜**（本轮逐个猜烧了 8 次实机启动，换成差分后一轮定位）。

**工具链自己的坑（本轮修）**：`TpacTool.Lib` 的 `VertexStreamData.WriteData` **读写不对称**（读端不读计数前缀、写端写）→ 任何重写顶点流都会整体错位；已修。另：改 `MeshEditData`/`VertexStreamData` 内容必须**新建 ExternalLoader 顶替数据段**（就地改不被写回）。

**完整交接**：[Knowledge/蒂法换头工程.md](../../Knowledge/蒂法换头工程.md) §11。

---

## 悬空 junction 让游戏启动即崩 `DirectoryNotFoundException @ ModuleHelper.GetModulePaths`（2026-09-13 实机）

**症状**
- 游戏启动瞬间抛 `System.IO.DirectoryNotFoundException`，栈顶是 `ModuleHelper.GetModulePaths → GetPhysicalModules → GetModules → GetModuleInfo` → `Module.Initialize()`。
- 报错路径指向一个**明明"存在"的模块目录**（例：`Modules\TifaHead`）。

**根因**
- 该目录是 **junction（目录联接）**，**它的目标已被删除** → 悬空链接。
- 🔴 **`Test-Path` 对悬空 junction 返回 `True`**（链接本身在），但任何**枚举目录内容**的操作（`Directory.GetFiles`）直接抛 `DirectoryNotFoundException`。
- 引擎启动时要遍历 `Modules\*` 找模块 → 撞上断链 → 崩。

**规避**
- **删 junction 的目标时，必须连链接一起删。** 用 `cmd /c rmdir "<链接路径>"`（只删链接、不碰目标）。
- 排查悬空链接（一条命令扫全部）：
  ```powershell
  Get-ChildItem <Modules目录> -Force | Where-Object { $_.LinkType } | ForEach-Object {
    $ok=$true; foreach ($t in $_.Target) { if (-not (Test-Path $t)) { $ok=$false } }
    if (-not $ok) { Write-Output "悬空: $($_.FullName) -> $($_.Target -join ';')" }
  }
  ```
- **判定目录是否存在，对 junction 要用 `Get-Item -Force | fl LinkType,Target` 看目标存活**，`Test-Path` 不可信。

---

## Blender FBX ⇄ 骑砍2 资产管线四坑（坐标 100 倍 / 翻转 / 不蒙皮 / 重导入刷材质）

**症状**（2026-09-13 蒂法换头工程实测，连续卡了三轮）
1. 网格**完全看不见**（飞到模型外 150 米处）；
2. 或**大小位置都对，但完全不跟骨架动**；
3. 或**脸朝向反了**（朝后、或翻到地面以下）；
4. 重新导入 FBX 后，编辑器里手工配的**材质设置全丢**（shader/flags/VertexLayout/纹理槽全变默认）。
5. **游戏里眼球像贴了脸皮、眼睛周围是个皮肤色肉球，而 ModKit 里看着完全正常**（2026-09-13 晚实测）。

**根因**（逐条实锤，详见 `Knowledge/蒂法换头工程.md` §13）
1. **引擎会把 FBX 的 Model 节点变换烘进顶点**。`apply_scale_options='FBX_SCALE_NONE'` 会把「1米=100厘米」烘成节点 `scale=100` 而声明 `UnitScaleFactor=1` → **顶点放大 100 倍**。
2. **引擎还会按文件声明的 `UpAxis` 再做一次轴转换**。文件里节点已转过一次、引擎又转一次 = **转两遍 = 绕 X 翻 180°**。用 `axis_forward='-Y'`（而非 `'Y'`）就会触发。
3. **`skinning` 是"材质"上的 `VertexLayoutFlags`，不是网格属性**。缺它 → 引擎按无蒙皮渲染 → 网格钉死在绑定姿势。**FBX 不携带该 flag，换新工程/重导入后必须重新在编辑器里勾**。
4. **FBX 重导入只还原 FBX 自己带的材质槽**，编辑器里手工配的一切都会没。"材质是独立资产、同名会复用"这个直觉是**错的**。
5. **morph 帧（`MeshEditData.VertexFrame.Positions`）存的是【绝对位置】，不是位移增量**。补"不形变的帧"必须**复制基础网格的位置**；填全 0 在绝对位置语义下 = **把整块网格拉向原点** —— 实机症状是"**面部持续下坠，但眼睛正常**"（眼睛不吃 morph 权重所以不受影响）。判据：正常包的帧 0（Basis）数值应 ≈ 基础网格 `Positions`。

**规避**
- **Blender 导出固定用这套**（实测 5×5 参数矩阵得出）：`apply_scale_options='FBX_SCALE_UNITS'` + `axis_forward='Y'` + `axis_up='Z'`。
- **导出前重设场景单位**：`scene.unit_settings.scale_length = 1.0`。🔴 **Blender 的 FBX 导入器会按文件里的 `UnitScaleFactor` 改写场景单位**（官方骨架声明厘米 → 场景被悄悄改成 0.01），所以必须在**所有导入之后**再设一次。
- **导出后先过门禁再进编辑器**：`tools/face-pipeline/scripts/fbx_probe.py <f.fbx> --full`，期望 `UnitScaleFactor=100` / `UpAxis=2` / **网格节点无任何非单位变换（含纯平移）**。
- **编译后、启动前再过第二道**：`check_head_space.py --pack <AssetPackages>`，和参照物量包围盒。
- **别用"烧一次实机启动"当排查手段**；也别信编辑器预览的尺度（自动取景，放大 100 倍看着和正常一样）。
6. **ModKit 删除 mesh 资产会【连带删掉 AssetSources 里创建它的源 FBX】**（删除框里的 `Geometry file xxx.fbx` 就是磁盘文件，点确定后真删）。迭代几轮源文件就空了。**应对**：备份目录当权威副本、生成脚本的输入读备份不读 AssetSources、每轮进编辑器前跑一次回填（本项目：`tools/face-pipeline/scripts/restore_fbx.py`）。
7. 🔴 **编辑器 Publish 出来的包是"白编译"——材质设置全按 FBX 默认值写，必须再打两道补丁才进游戏**（`morphfix` 补 morph 帧到 101 + `skinfix --fullmat` 刷四角色材质配方 / `MaterialFlags` / `VertexKeyCount`）。漏打的后果：`MaterialFlags` 空 → 脸部生成器认不出部件、**把脸皮合成贴到眼球上**；材质 shader 全变默认 → **睫毛/眉/眼影三个透明件按不透明渲染**（这三块几何正好盖在眼睛上方，合起来就是个皮肤色肉球）；缺 `skinning` → 部件不跟骨架动。
   - **一眼判据 = 包体大小**：本项目原始产物 **≈9.0 MB**（坏）／打过补丁的成品 **≈25.8 MB**（好）。**装机前先看大小。**
   - 🔴 **别拿 ModKit 的观感当发布包验收**：编辑器读工程 `Assets\`（材质设置在），游戏读 `AssetPackages\pack0.tpac`（材质被刷默认）——**两边不是同一份东西**，ModKit 正常 ≠ 游戏正常。判据细节见 `Knowledge/蒂法换头工程.md` §15。
8. 🔴🔴 **换头时脸部件的子网格【顺序 / 数量】必须与目标游戏一致——贴图是按位置分配的，不是按 `MaterialFlags`**（2026-09-13 深夜实测结论）。
   - 原版 `head_female_a` 与能跑的参照 mod `xxFemaleHead` 都是 **4 件、顺序 = 脸→嘴→眼→睫**；我们做成了 **6 件（脸→嘴→睫→眼影→眼→眉）**，眼排第 5 → 超范围部件回落到**脸皮材质**。
   - **症状极具辨识度**：眼球上贴着一张脸、整张脸"像糊了一层皮、鼻子嘴巴往前凸"——**因为那几件在渲染脸的贴图**。判据：把包里的脸皮贴图抠出来（`tpaccli dump --format png`），与实机截图上的"小脸"对比，同源即坐实。
   - **别去折腾材质**：flags / shader / alphaTest 全设对了也没用（本项目为此白跑两轮实机）。先数子网格件数与顺序。
   - **零成本验证**：`tpaccli metaparts --packdir <dir> --filter <名> --out <outdir> --order 0,1,4,2`（只重排元数据，不碰几何）——改完装机直接看，不必重导 FBX。
9. 🔴🔴 **自建头：附属件必须带形变通道 + 形状键顺序必须是"Number 序且带占位键"**（2026-09-13 实机验证后总结，完整版见 `Knowledge/蒂法换头工程.md` §13.7）。
   - **附属件无 morph 数据 → 脸动它不动**。脸壳的 50+ 条形变通道是位移场（单条最大 ±2cm），角色脸形参数会把脸壳拉走，五官留在原地 → **眼球跑到眼眶上方、眉毛与画上去的眉影错开、鼻子比"底下的脸"更凸**。编辑器预览（basis 状态）永远看不到这个。
     **做法**：按**最近邻 3 点反距离加权**把脸壳的位移场搬到附属件顶点（1 近邻会起皱）。**判据**：`morphinfo` 里附属件的"未形变帧"不该等于总帧数。
   - **形状键顺序 = 帧号**：编辑器**按位置**把非 Basis 键编成帧 0,1,2…（名字里的编号不影响帧号），而皮肤 `deform_keys` 用 `key_time_point=N` 取帧 N。所以 FBX 里必须是 `Basis → 占位键 → KeyTime_1…59` 数字序，**占位键还要非零位移**（零位移通道会被 FBX 压成 1 个顶点、可能被编辑器跳过）。诊断：`morphinfo` 的「逐帧位移签名」——帧 1 应为纯 X、帧 2 应为纯 Y。
   - **标定别拿不对位的参照物**：拿原版的**扁平眼贴片**当我方**完整眼球**的基准 → 眼球被拉回去还缩小，编辑器里看就是"陷进眼窝"。正解是**从源模型自身反解变换**，并留一件**没被动过的配件当锚点**验证换算（本工程用"嘴"，折算目标与当前位吻合 → 换算对，那偏离的几件就是被错改的）。
10. 🔴🔴 **接脖子：（a）镜像变换会把整份面朝向搞反 （b）下沿环绝不能按角度重排**（2026-09-13 深夜，蒂法换头 §20）。
    - **骑砍2 的脖子不是身体给的，是「头」给的**：身体 `body_female_a` 在胸口开了个大 V 领口（真洞，从前胸打射线会穿到后背内壁）；原版头自带「颈 + 胸兜」，其外沿与身体领口逐点重合。自建头没有这段 → 缺 4.1cm 脖子 + 整个领口没人盖。
    - **镜像变换**：源→我们的变换含 y 翻转（`our_y = −S·src_y + …`，行列式为负）→ 从源模型搬来的几何**整份面法线朝内**。**症状**：实机背面剔除后那块是个洞（编辑器里一道缝）。**修法**：搬完 `bmesh.ops.reverse_faces`。判据：逐面算 `法线 · 径向`，朝内的即反了。
    - **下沿环重排**：把边界环**按角度排序** = 把"角度相邻"当成"网格相邻" → 新接的面落到不相邻的顶点上，脖子上留一圈断边（实测 19 条）。**修法**：保持边界环**原始顺序**，角度只用来查轮廓。
    - **直线放样必折棱**（两端都不与表面相切；调环数/抹平/外凸只是减轻）→ 截面走**三次 Hermite**，两端取**真实表面切向**（都从网格实测）。切向长度取 **0.5×弦长**，太大会过冲插进身体 → 口沿漏缝。
    - **诊断法（比烧实机快）**：沿领口一圈逐角度逐高度从外朝中轴打射线，判据是**整条射线上有没有「正面可渲染」的命中**——🔴 **只看第一击会误判**（第一击是背面、后面还有正面 = 没洞，我在这上面虚报过 3008 个"洞"）。再配一发**品红背景 + 背面剔除**渲染：露品红才是真洞。**必须同时跑原版头+身体做对照，同为 0 才算同档。**
11. 🔴 **自建几何并进已有网格时的三个"静默"坑**（不报错，只在编辑器里显示怪东西，2026-09-13 蒂法换头 §20）：
    - **多余 UV 层**：源对象自带多套 UV（如身体有 `Base Female`/`Golden Palace`）并进来后 FBX 会有 3 套；而脸材质带 `doubleuv` = **引擎会去读第 2 套** → 新面读到垃圾坐标（编辑器里糊出一圈怪花纹）。**修法**：并进来后删到只剩 1 套。
    - **UV 区间撞车**：新几何的 UV 若与原有 UV 重叠（本项目：脸的 UV 用到 `v≥0.2666`，脖子 UV 上限 0.348）→ 脖子顶部蹭到下巴的贴图。**修法**：把新 UV 压进未占用区间。
    - **custom split normals**：网格带自定义法线时，**bmesh 新加的面拿不到正确法线** → 导出后编辑器/实机里是**一格一格的面片**。**修法**：`normals_split_custom_set_from_vertices` 用「按顶点平滑」的法线重设一遍（源本来就全平滑则无副作用）。
    - **导出顺序**：预览渲染常会临时替换材质 → **在导出前跑预览 = 交付物里材质名变成预览材质名**。预览一律放导出之后。
    - **参考物混进导出**：`use_selection=False` 时，场景里任何参考对象（量尺寸用的身体 OBJ 等）都会被一起导出。导出前清场。
12. 🔴 **`stage_dir()` 型"清空目录"辅助函数别在外部工具写完之后调**（2026-09-13 实锤）：`install_pack.py` 里 `stage_dir()` 会 `rmtree` 再重建，而它在 `morphfix` 写出包**之后**被调用 → **把刚生成的包删了** → 下一行 copy 抛 `FileNotFoundError`。skinfix 那步同样中招，只是写了 fallback → **静默跳过皮肤补丁**（更阴：不报错，装了个白编译包）。**纪律**：清空目录只用于"给外部工具腾输出目录"，**产物目录一律单独处理**。
---

## Python 脚本写 Windows 路径 → `\b`/`\t` 被当转义符，控制字符混进文档、字母被吃（2026-09-13）

**症状**
- 文档里的 Windows 路径读起来**缺字、错位**：`D:\BrainMakerlend_projectsifa_exportackup_20260913`（本该是 `D:\BrainMaker\blend_projects\tifa_export\backup_20260913`）
- 编辑器 / 工具提示 **"文件无法写入"、拒绝保存**（文件里混进了控制字符）
- `file` 命令报 `with overstriking`

**根因**
- 用 Python（heredoc / 普通字符串）拼含 Windows 路径的文本时，`\b` `\t` `\n` `\r` `\f` `\v` `\a` 被解析成**控制字符**，而且**紧随其后的那个字母被吃掉**：
  `"...\blend_projects..."` → `D:\BrainMaker` + `0x08(退格)` + `lend_projects`
- 🔴 **二次坑：上层 shell 还会再折一次反斜杠。** 经 Bash 工具传 heredoc 时，Python 源码里的 `b'\\b'` 到达 Python 已变成 `b'\b'`（= 退格本身）→ **"用 `\\` 转义"这招静默失效**（替换等于没做，且不报错、看不出来）。

**规避**
1. **写路径一律用 raw 字符串** `r"D:\BrainMaker\blend_projects"`；批处理脚本里更稳的做法是用 `chr(92)` 或 `bytes([92, 98])` 构造，**源码里不出现反斜杠字面量**。
2. **批量写 / 改文档后必扫控制字符**：
   ```python
   b = open(path, 'rb').read()
   [hex(c) for c in b if c < 32 and c != 10]     # 只放过 0x0A（真换行）
   ```
   ⚠️ 别把 `0x09(TAB)` / `0x0D(CR)` 也当合法放过 —— `\t` 吃出来的正是 TAB，本工程就漏扫过一次。
3. **`0x0A` 是真换行、字节上无法区分** → 用**行首残片**反查：扫 `^\s*(ative|ifa_export|ools|aikou|ew_)` 之类（分别对应 `\Native` / `\tifa_export` / `\tools` / `\taikou` / `\new_`）。
4. **修复**：`0x08 → \b`（反斜杠 + 字母 b）、`0x09 → \t`。替换串**必须用字节值构造**（`bytes([92, 98])`），别写 `b'\\b'`（会被上层 shell 折掉）。
5. **验证闭环**：修完再扫一遍应全 0，并 `git diff --stat` 看改动行数是否 = 发现问题数（本工程 6 个 0x08 + 5 个 0x09 = 5 行受影响）。

---

## 进据点领主大厅/主楼 → `Mission.SpawnAgent` NullReferenceException（**栈里没有下层帧**）（2026-09-14，战国无双换装·织田信长换头）

**症状**
- 大地图能走、菜单能点，**一进据点主楼 / 领主大厅就崩**：`System.NullReferenceException`，栈顶
  `TaleWorlds.MountAndBlade.Mission.SpawnAgent`（`Mission.cs` 第 3860 行），下一帧直接是
  `SandBox...MissionAgentHandler.SpawnWanderingAgentWithInitialFrame`。
- 🔴 **指纹 = 崩在 `SpawnAgent` 自己体内、栈里没有它调用的下层帧**（不是 `CreateAgent` 崩的）。
- 崩之前日志一切正常，就停在点菜单那一行。

**根因：自建 race 只复刻了 Monster 的**基础 id**，没复刻「后缀变体族」**

引擎取 `Monster` 有**两种问法**，只满足第一种就会崩：

| 问法 | 出处 | 缺了会怎样 |
|---|---|---|
| `FaceGen.GetBaseMonsterFromRace(race)` | `TaleWorlds.Core.AgentData` 构造函数 | 基础 id 缺失才走这条 |
| `FaceGen.GetMonsterWithSuffix(race, "_settlement")` 等 | `LocationCharacter` / `LocationComplex.AddHeroToDecidedLocation` / `Hero` 多处 | **取到 null 且不回落** → 崩 |

- Native 给 `human` 定义的是**一族 5 个** id：`human` · `human_child` · `human_settlement` ·
  `human_settlement_fast` · `human_settlement_slow`。**自建 race 必须整族照搬。**
- 后缀用量实测（1.2.12 全 DLL 反编译计数）：`_settlement` **46** · `_child` 12 · `_settlement_slow` 5 · `_settlement_fast` 1。
- 🔴 **兜底规则**：`GetRaceOrDefault` 只在 **race 本身不存在**时回落 human；**Monster 变体缺失不回落**。
- 🔴 **触发路径躲不掉**：`AddHeroToDecidedLocation` 判 `Occupation == Lord → LordsHall`，紧接着就取 `_settlement` 变体
  —— 领主进领主大厅必走这条。
- 🔴 **为什么栈里没有下层帧**：`Mission.SpawnAgent` 里
  `CreateAgent(agentBuildData.AgentMonster, …, agentBuildData.AgentMonster.Weight, …)`
  按 C# **从左到右**求值，`AgentMonster.Weight` 先炸 → `CreateAgent` 根本没进去。

**规避**
1. **整族复刻**：`grep` Native `monsters.xml` 里该 race 名的**全部** `前缀` / `前缀_*` id，逐块复制、只改**开标签**里的 id。
   范本 = `Scripts/gen_taikou_nobunaga_head.py` 的 `build_monsters()`（从 Native **动态取整族**，Native 以后加变体会自动跟上）。
2. **改生成器重跑，禁手改产物**（铁律 22）；重跑参数必须与落盘一致，否则 `--check` 报 OUT OF SYNC（该脚本要求参数一致）。
3. **排查口诀**：**"自建 race / 换头之后，进场景刷人时崩" → 先数 Monster 变体族齐不齐，别去查 skin 和资产**。
   反过来，`skins.xml` / race 本身的症状是**静默回落**（角色长相没变但游戏不崩），**不会崩**——崩了就说明 race 已生效、缺的是 Monster 变体。

**完整交接**：[Knowledge/战国无双换装工程.md](../../Knowledge/战国无双换装工程.md) §5 末两行 · 必备清单雷 120 · 轮子
[wheels.d/campaign-mode.md](wheels.d/campaign-mode.md) 卷十三。

---

## 大地图走时间突然 `AccessViolationException`，**日志戛然而止**（2026-09-14 实机，Taikou；雷 105 复发）

**症状**
- 在大地图上走时间（不一定是玩家动作触发的）**突然段错误**，调试器显示
  `System.AccessViolationException` / `Attempted to read or write protected memory`，**`Source` 显示"无法计算异常源"、托管栈丢失**。
- 🔴 **指纹 = 日志没有任何异常直接断**：`Debug/StoryEngine_RuntimeLog.txt` 停在崩溃前最后一条**正常**记录
  （原生 AV 不走 C# 异常通道，`[Crash]` 之类都来不及打）。
- 崩溃栈（VS 附加时能看到）：`SandBox.MapScene.GetNavigationMeshCenterPosition`（`SandBox.dll:339`
  `_scene.GetNavMeshCenterPosition`）← `DefaultMapDistanceModel.GetClosestSettlementForNavigationMesh`
  ← `GetDistance` ← `DefaultDelayedTeleportationModel.GetTeleportationDelayAsHours` ←
  `TeleportHeroAction` ← `ChangeGovernorAction.Apply` ← `ClanVariablesCampaignBehavior.UpdateGovernorsOfClan`
  ← `DailyTickClan`。

**根因：引擎零守卫，把非法导航面索引直送原生**

- `Settlement.CurrentNavigationFace = MapSceneWrapper.GetFaceIndex(GatePosition)`（`Settlement.OnGameInitialized`）。
  **城门点不在导航网格上** → 返回 `PathFaceRecord.NullFaceRecord` = `(-1,-1,-1)`。
- `GetClosestSettlementForNavigationMesh(face)` 的缓存 `_navigationMeshClosestSettlementCache` **按 `FaceIndex` 建键**，
  而缓存文件 face 段的格式**以负数终止**（引擎 `for (i = ReadInt32(); i >= 0; …)`）⇒ **键恒 ≥0 ⇒ -1 永远查不到**
  ⇒ 必然走「取面中心」分支 ⇒ 原生 `GetNavMeshCenterPosition(-1)` 越界读 ⇒ AV。
- 🔴 **`DefaultMapDistanceModel` 全类零 `IsValid` 守卫**（反编译实证）。
- **触发者是引擎自己的每日结算**（与 mod 代码无关）：`DailyTickClan` → 给 AI 家族封地派总督（挑「手上没带兵」的领主）
  → `ChangeGovernorAction.Apply` → 延迟传送 → 算传送耗时 → `GetDistance(IMapPoint, Settlement, …)` → 两面不等 → 本方法。
- 根的根在**地图**：据点门位不在网格上。离线体检能看出征兆 = `check_settlement_distance_cache.py` 报
  「N 对据点走不通（1e30 哨兵）+ 完全孤立的据点」。

**规避 / 定位法（下次原生越界崩照这个走）**
1. **完整反编译**目标类型，把调那个原生函数的地方**全列出来**（本例只有 2 处：缓存重建循环恒用合法下标 + 本方法）。
2. **全 DLL 二进制 grep** 那个原生函数名，确认只有「**调用方 + 声明方**」两个程序集出现它
   （本例 = `TaleWorlds.CampaignSystem.dll` + `SandBox.dll`）→ 无第三方调用者。
3. 两条独立实证都指向同一点 ⇒ **封住它 = 全部入口封住**，不必再去补下层那一圈。
4. 范本 = `CampaignMode/MapDistanceInvalidFaceGuardPatch.cs`（Harmony Prefix）。**两条纪律**：
   - **日志即清单**：非法面没有坐标，唯一定位途径就是「谁的面是这个」→ 打 **面下标 / 原因 / 面主据点清单 / 兜底给了谁**，
     每面下标只报一次 + 上限条数。这样**不用复现崩溃**就能拿到改地图的清单。
   - **兜底值可以猜但必须有界、禁返回 null**（返回 null 等于把 AV 换成 NRE）。
5. **治本在地图不在代码**：编辑器 `CheckPositions` 定位 → 门位挪回网格 → **删 `settlements_distance_cache.bin` 重烤**。

**完整交接**：必备清单雷 104/105/119 · 轮子 [wheels.d/campaign-mode.md](wheels.d/campaign-mode.md) 卷十三。

---

## 自定义头的脸部贴图糊成一片（眼睛/嘴/头发错乱）→ `MaterialFlags` 搞反了

**症状**（实机 2026-09-14 织田信长首次、2026-09-16 全 28 人复现，用户报「眼睛不对 + 嘴开花」；同日再以**反方向**复发一次，用户报「蒂法眼睛不对」）
- 自定义头的**眼睛/嘴糊成一片**；眼睛上像是贴了脸皮、嘴的位置纹理错乱；**头发那块被涂成肤色**。
- 用的明明是自己的贴图，看着却像"被引擎重新画过一遍"。
- ⚠️ 诡异点：**同一颗头昨天还是好的，今天装机之后就糊了** —— 因为标记是**装机时被改的**。
- ⚠️🔴 **两个方向都会糊、症状一模一样** —— 所以极难归因，别看到"眼睛糊"就认定方向。

**根因**（工具链实证，不是引擎反编译）
- 子网格上的 `face_base_mesh` / `face_mouth_mesh` / `face_eye_mesh` / `face_eyelash_mesh`
  是给**引擎的脸部贴图生成器**看的：**带标记 = 引擎按原版画布布局把五官画到脸贴上**。
- 🔴 **该不该带，取决于这颗头的 UV 走哪套布局，两个方向正好相反**：

  | 头的类型 | UV 布局 | `MaterialFlags` | 搞反的症状 |
  |---|---|---|---|
  | 战无2 的 28 张脸 / 织田信长 | 源模型自带（脸挤在图集角落） | 🔴 **必须为空** | 带标记 → 引擎按原版画布重画五官 → 糊 |
  | 蒂法 / 萨菲罗斯 | **对齐原版画布**（当年专门做的对齐） | 🔴 **必须保留** | 清掉 → 生成器认不出脸/嘴/眼/睫 → **把脸皮合成贴到眼球上** → 糊 |

- **「自定义头必须清标记」只对第一类成立**。2026-09-16 装机脚本按它一刀切跑全包，
  把蒂法 / 萨菲罗斯的标记一起扫了 → 用户报「蒂法眼睛不对」。
- 🔴 **标记是装机脚本主动补的**：`install_pack.py` → `morphfix` 有一段「为空就按材质名补标记」
  （当年为防脸部生成器空指针加的），`skinfix --fullmat` 也会刷 ⇒ **每次装机都会补回来**。
  2026-09-16 那次装机就是这么把已经验收通过的信长弄糊的。

**判据（"该不该清"怎么定）**
1. **看 UV**：贴图是**源模型图集原样**（脸只占图集一角）→ 清；是**按原版画布重排过的** → 留。
2. **查历史包（最稳）**：拿一个该头"验收通过"的包跑 `tpaccli metaparts --filter <头名>` ——
   **验收态就是正确答案**，别推。
   ⚠️ 本条就是被一句**记错的转述**坑的：旧注释写「蒂法/萨菲罗斯验收时标记都是空的」，
   历史包实测**证伪**（两人四个/三个标记齐全）；误判来源 = 把 SW2 侧的**件位顺序**结论
   （`[0]脸[1]嘴[2]眼`）当成了"标记为空"。

**规避**
1. 🔴 **装机脚本默认不清标记**（`install_pack.py` 第 3.5 步）—— 只有明确要装"源模型 UV"的头时才加
   `--clear-flags`。⚠️ 顺序仍不能倒：清在 `morphfix`/`skinfix` 之前 = 白清。
2. **修包（把被清掉的标记补回来）**：`tpaccli skinfix --packdir <包目录> --filter <头名> --out <新目录> --fullmat`
   —— 标记为空的子网格会**按材质名**补回、材质配方幂等重刷；**filter 窄 = 只动那一颗头**（不碰同包其它头）。
   零成本：不重导 FBX、不开编辑器，拷回 `AssetPackages/pack0.tpac` 重启即生效。
   范本：`Debug/offline/自定义头/tifa_flag_repair/`（2026-09-16 修蒂法 + 萨菲罗斯，含修复前备份包 + 逐字段对账转储）。
3. **清标记（应急/验证）**：`tpaccli metaparts --packdir <包目录> --filter <头名> --out <新目录> --clearflags`
   —— 同样只动元数据。
4. **诊断顺序纪律**（这次绕远的教训）：**先 dump 产物真身，再谈理论**。
   `tpaccli metaparts --filter <mesh名>` 一条命令就能看到「子网格顺序 + 每格的 flags + 材质 + 顶点数」——
   比渲一堆对照图快得多，而且那是**引擎真正读到的东西**。
   要证明"到底改动了哪几项"用 `tpaccli meshdiff` 与已知好包逐字段差分（本轮靠它一句锁定"只有 flags 变了"）。

**顺带记住的两个事实**（同一次排查查实，写在 [wheels.d/assets.md](wheels.d/assets.md) 换头那节）
- **子网格顺序分性别**：女头 = 脸→嘴→眼→睫；**男头 = 脸→眼→嘴**。
  实证：原版 `head_male_a`（`.1`=eye_mat/`.2`=mouth_mat）、在售的织丰 `sho_head_male_japanese`、
  以及**本工程已验收的萨菲罗斯**（实测 `[0]脸 [1]眼(458v) [2]嘴(3698v)`）。
- **形状键污染离线渲染**：产物 FBX 里 `KeyTime_0..59` 的 value 全写着 1.0，Blender 直接渲 =
  59 条形变全叠加的变形头（实测信长 x ±0.132 被压到 ±0.083）。**做几何判断前先归零**。

**完整来龙去脉**：`Knowledge/蒂法换头工程.md` §16（引擎按顺序/数量认部件、flags 不参与分配）
+ §13.7①（4 件顺序）+ `tools/tpactool/TpacToolCLI/MetaParts.cs` 的类注释。

## 离线 Blender 探针读图集：`bpy.data.images.load` 不认**相对路径**（2026-09-16）

**症状**：探针脚本里用相对路径加载 PNG → `RuntimeError: Error: Cannot read 'Debug/offline/xxx\yyy_d.png': No such file or directory`
—— 文件明明在（PIL 能打开、md5 正常），Blender 就是读不到。**换绝对路径立刻好**。

**根因**：Blender 进程的工作目录与脚本里拼出来的相对路径不总是同一个基准（`-b --python` 从任意目录启动都成立）。

**修法**：所有喂给 Blender 的路径（`--python` 的脚本路径、`import_scene.fbx(filepath=...)`、`images.load(...)`）
**一律传绝对路径**；探针里用 `os.path.abspath()` 或 `$(pwd -W)`（Git Bash）/`(Get-Location).Path`（PowerShell）现算。

**同时**：`bpy.data.images.load` 的 `image.pixels` 是**自下而上**存的 —— UV 采样时 `y = int(v*height)`
（不要写成 `(1-v)*height`），否则取样上下颠倒，用"颜色"做判据（如脖子肤色判据）时会把结果全判反。

---

## 甲侧**按骨骼**剔「头骨族」剔不掉脖子皮 → 甲/头两块几何**完全重合**（z-fighting）（2026-09-17，战国无双换装）

**症状**：穿甲时脖子那一圈**闪烁 / 发脏**（两块皮同深度打架）。只在**头侧有 `_neck` 件**的角色上出现。

**根因**：剔件判据是**骨骼** —— `build_armor.py` 的 `HEAD_SW = {bone_10, bone_11} ∪ {bone_46..62}`（头骨族），
而实测**脖子皮肤的主导骨是胸骨 `bone_9`**（宁宁/信长/兰丸三人都是 `bone_10` 权重为 0 ——
与老 `keep_neck_frag` 的注释"脖子和胸/肩共用 bone_9，按骨分不开"一致）⇒ **甲根本没把脖子剔掉**。
头的 `_neck` 件（从源件抠出来、走同一个 T）与甲里留下的那一份**位置完全重合** ——
实测 6 人共 **222** 个顶点（幸村 61 / 光秀 53 / 义弘 36 / 谦信 34 / 兰丸 24 / 阿市 14，量的是成品 FBX 主网格）。
**"甲把脖子丢掉了"这个前提是错的** —— 上一轮的方案文档就是据此写的，直到动手量才发现前提不成立。

**修法**：甲侧**按几何**剔，不按骨骼 —— `build_armor.py --drop-coincident <头 v1 FBX>`：
读该 FBX 里 `_neck` 件的顶点，甲侧把 **≤1mm**（`--drop-coincident-tol`，用 KDTree）的顶点删掉
（删在"顶点位置已算完、导出前"）。**纪律**：只在**该头真有 `_neck` 件**时才传
（判据 = `tools/sw2-pipeline/build_armors.py` 的 `head_neck_fbx()`；没有脖子件的角色传了 = 拿整张头去删甲）。
**实测（修前 → 修后）**：甲侧各删 **61 / 34 / 14 / 24 / 18 / 21 = 172** 个顶点，成品 FBX 重合顶点 **0/6 全清零**；
**兰丸同头 A/B**（同一份头 FBX、只差这一个参数）**24 → 0**。
**计量工具** = `Debug/offline/_neck_dup_count.py`（比成品 FBX 主网格的顶点）。
**判据没清干净时往哪查**：`--drop-coincident-tol`（默认 1mm）—— 调大一点点看还剩几个。

---

## 「取参照色」这类判据的锚点会被**遮挡物**污染（下巴带←胡子 / 手部←籠手 / 面罩←覆面）（2026-09-17，战国无双换装抠脖子）

**症状**（两个方向都会出）：
- **该选中的一件都选不出来** —— 从身体件里抠脖子时，信长 / 兰丸在旧口径下抠出 **0 顶点**，
  被误判成"这个人没有脖子件"；
- 或**选错了件** —— 宁宁的几何判据（薄/不深/够高/绕轴覆盖）选中的是**甲领口那个金铜色箍**
  （45 顶点 / \|x\|≤0.072 / 11 扇区 —— 几何上完全像脖子）。它若进头资产 → 与甲自己的领口重复、
  且不穿甲时脖子上一圈金箍。

**根因**：参照色取的是「**脸壳下巴一带**」—— 会被**胡子**（信长）/ **覆面**（半藏）染成非肤色
→ 真脖子过不了肤色容差。换「**手部**」也不行 —— 手上戴**籠手**，实测平均色被染成近黑 **0.10**。
（绝对肤色阈值救不了：金铜色也是 R>G>B。）

**修法**：参照色锚在**结构上不可能被遮的部位** = 脸壳的**颧骨 / 鼻梁带**（T 空间 `z ∈ [1.56,1.65]`）
—— 胡子长不到、面罩只盖下半脸，实测取到的是真肤色。被污染的锚点**降级为兜底**
（取不到颧骨带才依次退到「手部」「下巴一带」），且**打印用的是哪一个**（判读看 `抠脖子：参照肤色（…）` 行）。
**实测名单变化（28 人）**：旧口径 5 人（光秀 90 / 谦信 59 / 幸村 37 / 义弘 18 / 阿市 10）；
新口径 6 人（谦信 59 / 幸村 37 / **兰丸 26** / 义弘 18 / 阿市 10 / **兼续 9**）——
**光秀掉了**（他颧骨带比下巴带亮，过不了容差）、**兰丸与兼续是新进来的**。

**🔴 判据要能逐人覆盖**：谁被污染谁单独换锚点 —— 把件集/参数放在**逐角色表**里
（本工程 = `tools/sw2-pipeline/parts_table.py` 一行一个角色；图集走逐人参数 `--neck-atlas`），
**不要为了救一个人放宽阈值**（放宽 = 把金铜箍那类假阳性放进来）。
**这条通用**：凡是"拿某处的颜色 / 位置当基准"的判据，先问一句「这块地方会不会被别的东西盖住」，
并优先选**遮挡物到不了**的部位。

---

## 🔴 整套甲的**四肢整体错开**、看着像"源游戏的站姿" → T 模式把「按骨锚定 + 沿骨轴 λ」一起丢了（2026-09-17，战国无双换装·宁宁实机）

**症状**（用户实机 + ModKit 里看模型）：穿甲后**手臂 / 腿和甲错开** —— 四肢朝**两侧外围 + 后方**偏；
甲本身形状对、粗细对，就是"没套在身体上"。**躯干完全正常**，只有四肢。用户原话："甲看起来是战无的站姿，但骑砍2不是那个"。

**根因**：T 换基准那一刀**取消了对四肢的两项补偿**（写在 `build_armor.py` 的 T 模式分支里）：
1. **按骨锚定** —— 老模式每根骨那行是 `Trans(骑砍骨头位置) @ rot @ S @ Trans(−源骨头位置)`
   （**把该骨的几何搬到骑砍骨头所在处**）；T 模式**只留了 `rot`** ⇒ 甲的四肢停在**源模型的位置**。
2. **沿骨轴 λ** —— 老模式 `ARM_ALONG` / `LEG_ALONG` 把每根骨段拉到**骑砍骨段长**；T 模式一起取消
   ⇒ 末端（膝/腕/踝）落不到骑砍关节上（实例：大腿源 45.6 单位 × s = 0.486m，骑砍只有 0.417m，**长 14%**）。

**量化**（重心对齐，左侧小腿）：原版身体 x 中心 **−0.1375** / 老模式甲 **−0.1325（差 5mm）** / T 模式甲 **−0.0765（差 61mm）**。
按高度切片比 x 外沿（脚/踝）：**916 那版 ±0.245 → 坏的那版 ±0.149**。

**为什么只在四肢显形**：T 是"整装等比缩放 + **只锚头骨**"，它**不知道骑砍骨架的四肢长在哪**；
躯干/颈那两处 T 恰好对得上（领口 vs 身体颈顶只差 16.6mm）⇒ 只有四肢露馅。

**修法**（`tools/armor-pipeline/scripts/build_armor.py`，**只在 T 模式 + 四肢骨**生效）：
`M_b = Trans(骑砍骨头部) ∘ rot_b ∘ S_沿骨轴 ∘ Trans(−p_b)`（`p_b = T(源骨头部)`）
· 性质① 源骨头部恰好落到骑砍骨头部 → 锚定；② 旋转部分不变 → **手臂姿态修正不丢**；③ 腿 rot_b = 单位 → 纯平移。
**躯干 / 头颈一律不动**（T 在那里已经对上，动了反而坏）。开关 `--no-anchor-limb`（关掉 = 回到"只有 T"，A/B 用）。
**验收**：修后 脚/踝 ±0.236 / 小腿 ±0.205 / 手·腕 ±0.652 —— 与 916 那版（±0.245 / ±0.214 / ±0.659）对齐。

**🔴 通用教训**：**"整装等比缩放"与"逐骨锚定"是互斥的两条路** —— 只要目标骨架与源骨架的**骨段长度/站姿不同**，
丢掉逐骨锚定就必然在**末端**露出累积误差。躯干看不出来不代表整套都对，**必须逐部位量**。

---

## 🔴 改了源文件 ≠ 生效：`AssetSources` 有两套布局，编辑器只认**镜像**那一套（2026-09-17，同一个坑栽多次）

**症状**：管线跑完了、脚本报"已归拢 `/AssetSources/sw2/<角色>`"、文件时间戳也是新的，
**但编辑器和实机里一点变化都没有**（或只有一部分变了）。

**根因**：`AssetSources/` 下**两套布局**：
| 布局 | 例子 | 谁读 |
|---|---|---|
| **镜像**（与 `Assets/` **同目录同名**） | `AssetSources/armor/nene/…` · `head/nene/…` · `weapon/<名>/…` | 🔴 **编辑器自动同步的唯一入口** |
| 管线落点 `sw2/<角色键>/` | `AssetSources/sw2/L47_nene/…` | 只有人 |

三个 builder / `stage_for_import.py` 写的都是 `sw2/<键>/` ⇒ **写完什么都没发生**。

**两个附加坑**：
1. 🔴 **同步只对「ModKit 开着那段时间」的改动有效**（文件监视，不补扫）——
   实测：甲镜像 09:39 更新、ModKit 12:5x 才开 ⇒ 编辑器里一直是 09-16 的老甲（发布包 z 顶 1.484，而 T 版 1.538），
   实机两侧各露 **+49.5mm** 的缝 —— **用户反复报的"脖子不贴甲"的真身就是这个**。
2. 🔴 **mtime 刷新不一定够**：实测 13:08 重编过一次、内容一字不差 → 不行就**在编辑器里删掉该资产、重新导入 FBX**。

**交付判据（只有这两条，别的都不算数）**：
① `Assets/<类>/<名>/*_geo.tpac` **时间戳**更新；② **最终产物上量出来的数字**对得上
（`tpaccli dump --packdir <模块>\AssetPackages --filter <名> --format obj`）。
**禁止拿"已归拢 AssetSources"当证据**（CLAUDE.md 铁律 31）。
一个资产 = **多个文件**（FBX + `_d/_n/_s` 贴图），只同步 FBX = 半截。

---

## 🔴 量「甲 vs 原版身体」容易出**假数**：三个坑（2026-09-17，宁宁四肢排查）

| 坑 | 表现 | 正解 |
|---|---|---|
| **最近邻平均位移** | 每根骨都报 "98mm / 327mm"，方向一致性 0.2~0.99 乱跳 | 它同时吃进了**甲的厚度**和**形状差** → 换成 **按高度切片比 x 外沿** 或 **重心对齐** |
| **同一个 Blender 场景里导甲+身体** | 甲的 **LOD 网格**被当成"身体" → 最近邻全落回甲自己（距离≈0）→ **一片假绿** | **两次导入之间必须 `read_factory_settings` 清场景** |
| **拿 `body_female_a` 当完整身体** | 脚的位移报 **327mm** | 该网格**没有手/脚**（顶点组只有 calf/thigh/upperarm/foretwist，最低 z=0.38）→ 脚根本量不了 |

**⇒ 判断"甲和身体有没有错开"，唯一可靠的两把尺子**：
① **按高度切片比 x 外沿**（z 分桶，取 |x| 最大）；② **重心对齐**（同部位几何重心之差）。
**两者都要有"参照版"**（本工程 = `Modules/TifaHead2_916` 那版 pack，即用户认可的站姿）——
跨版本比同一把尺子，比"跟身体比"稳得多。

---

## 🔴 搬含 `.csproj` 的目录 → `Move-Item` 报 "item is in use" / 子目录改名报 Access denied（2026-09-20，tpactool 搬家）

**症状**：`Move-Item` 搬一个装着 `.csproj` 的目录，整目录报
`Cannot move item because the item ... is in use`；改成逐个给子目录改名探测，则报
`Access to the path ... is denied`。此时没有任何编译在跑，`Get-Process MSBuild,VBCSCompiler` 也是干净的。

**根因**：占用者是 VSCode 的 C# 语言服务器两件套 —— `Microsoft.CodeAnalysis.LanguageServer.exe`
（扩展 `ms-dotnettools.csharp`）+ Dev Kit 的 CPS 宿主 `dotnet.exe`（`ms-dotnettools.csdevkit` 组件）。
它长期驻留、且**只加载 SDK 式工程**：老式 csproj（如 TpacTool 的 WPF 工程）它不认、不锁 ——
所以「哪几个子目录被锁」精确等于「它加载了哪几个工程」，可用来反证占用者是谁。

**规避**：
1. **诊断**：逐个把子项 `Rename-Item` 到临时名再改回来，报 Access denied 的就是被锁的那几个。
2. 🔴 `dotnet build-server shutdown` **治不了这个**（它只管 MSBuild / VBCSCompiler 节点，实测白跑一趟）。
3. **关掉那两个进程**（`Stop-Process`；自愈 —— 下次打开/编辑 C# 文件会自动重启）→ **紧接着**搬，
   中间别停，否则它重启后又锁回去。kill 的是用户自己的编辑器工具，动手前说一声。
4. 搬完提醒用户：VS / VSCode 里缓存的还是旧路径，工程要重新打开。

## 🔴 离线下"把动作搬给**另一具骨架**"会**静默失败**，姿势停在绑定姿势（2026-09-22，飞行增量预览）

**症状**：Blender 脚本把 A 骨架的 action 赋给同名的 B 骨架（为了合成一个"多动画 GLB"），
导出一切正常、动画通道也都在 —— 但**渲染出来是绑定姿势**（看着像根本没动画）。
最坑的是"检查有没有数据"**查不出来**（数据在文件里，只是没绑到骨架上）。

**根因**：Blender ≥4.4 的 action 带 **slot**；跨骨架直接赋 action 时 slot 不匹配 ⇒ **不生效、不报错**。

**修法**：
1. 别赋 action —— **逐帧拷姿势**（`rotation_quaternion` / `location` / `scale`）写进"为 B 新建的空动作"；
2. 更省事：**根本不经 FBX**（本项目改走"打包器直接读 `.trf`"）。

**同一个脚本里还有第二个坑**：**FBX 导入器会按文件自带帧率改写场景 fps** ——
`sc.render.fps = 30` 必须写在**导入基底之后**再设一次；否则关键帧按错帧率换算成秒
（实测：31 帧的 clip 变成 1.25 s = 按 24 fps 写的），而查看器又按"时长比"给两侧锁相 ⇒ 两个模型错位。

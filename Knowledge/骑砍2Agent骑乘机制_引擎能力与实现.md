# 骑砍2 Agent 骑乘机制 — 引擎能力与实现

> **谁读这份**：要让 NPC / 随从上马、下马，以及要控制"马上行为"（移动 / 转圈 / 射击 / 挥砍 / 落马）的人。
> **结论来源**：反编译（`TaleWorlds.MountAndBlade` / `SandBox` / `TaleWorlds.CampaignSystem` × 1.2.12 / 1.3.15 / 1.5.x 三版比对）+ **2026-10-13 实机验证通过**。
> **落地件**：`Combat/GrappleLogic.cs`（请求保持 + 控制权还原）· `CampaignMode/Tools/GrappleCommands.cs`（`custom.grapple horse` 族）。

---

## 一、结论速查（先看这张表）

| 要做的事 | 走哪条路 | 状态 |
|---|---|---|
| **让一个已有 agent 上马** | ① `Controller = None` ② `EventControlFlags |= Mount` + `SetInteractionAgent(马)` ③ native 演上马动画 ④ 把 `Controller` 还回去 | ✅ **2026-10-13 实机通过** |
| **让他下马** | 同一条链，旗标换 `Dismount`（**每帧重发**；玩家侧旗标会被输入层每帧清零） | ✅ **2026-10-13 实机通过** |
| **把骑手掀下马**（被动落马） | 手搓 blow + `BlowFlags.CanDismount`(0x800)：**native 直接执行、不看马速** | ✅ **2026-10-13 实机通过**（见 §5.6） |
| **勾住骑手 → 拉下马**（钩索用法） | 上面那条 blow + 引擎下马事件兜底；落地直接进「拉倒」 | ✅ **2026-10-13 实机通过**（0.20 秒落地） |
| **判"这匹马能不能骑"** | 🔴 `物品.HorseComponent.IsRideable`（**不是** `Agent.IsMount`，也**不是** `HorseComponent.IsMount`） | ✅ 实机踩过两次 |
| **凭空刷一匹马** | `Mission.SpawnMonster(new ItemRosterElement(马物品, 1, null), default, pos, dir, -1)`（官方 `SandBoxHelpers.MissionHelper.SpawnHorses` 同款） | ✅ |
| **让人"一出生就骑着"** | `AgentBuildData` + spawn 装备的**马槽**（`ArmorItemEndSlot`）里放马 ⇒ 引擎自己造马并挂上 | ✅（本项目 `horse rider` 夹具） |
| **上马之后的移动/战斗** | 两条路都通：**交回引擎**（AI 驱动，骑马版动画 native 自己选）/ **我们驱动**（与步兵同一个 `SetScriptedPosition` 入口）—— 移动/跟随/转圈/骑射/骑砍/下马/落马**六件事的机制全在 §五**（2026-10-13 调研完成） | ✅ 机制已证 |

---

## 二、上马的正规链路（**为什么少一步就不行**）

```csharp
// ① 先把 AI 控制器摘掉 —— 这一步是命门
V.SetAgentControllerNone(agent);          // 版本兼容包装已存在（飞行 spike 立的）
// ② 点旗标（Agent.Mount 内部就是这两行 + 三道检查）
agent.Mount(horse);                       // = EventControlFlags |= Mount; SetInteractionAgent(horse);
// ③ native 接手：挑 act_mount_horse_from_left/right → 播完把人挂到马上 → 回调 Mission.OnAgentMount
// ④ 上完（或超时）把控制权还回去 → 引擎按 Controller 重新挂上 AI 组件
V.SetAgentAI(agent);
```

**为什么必须有 ①**（反编译证据链）：

- `Agent.Tick` 里：`foreach (component) component.OnTick(dt);` + `if (Mission.AllowAiTicking && IsAIControlled) TickAsAI();`
- **AI 单位的输入旗标每帧由 AI 重算**（`OnAIInputSet(ref eventFlag, …)` 把整个旗标字算出来再写回）
  ⇒ 我们外部 `|= Mount` 的位**活不过一帧**，native 根本看不到。
- `Controller != AI` 时 `TickAsAI` 不跑 ⇒ 旗标没人覆盖 ⇒ native 消费得到。**这就是"先摘控制器"的全部理由。**
- 那条旗标**不是"只给玩家"**：写 `EventControlFlag` 的只有三处（`Run` / `Mount` / `Dismount`，都在 `Agent.Mount()` 里），
  而 `Agent.Mount()` 在引擎里唯一的内部调用点是 `Mission.OnAgentInteraction` 的"玩家 + 马"分支（= 玩家按 E 上马）
  ⇒ 结论是「**谁的控制权不在 AI 手上，谁就能用这条旗标**」——玩家天然满足（Player 控制器），NPC 得先把控制器摘掉。
- **摘了再还回去是引擎设计的正规往返**：`AgentCommonAILogic.OnAgentControllerChanged` 里
  离 AI 就 `RemoveComponent(CommonAIComponent)`、回 AI 就 `AddComponent(...)`（`HumanAI` 那套同构）。

`Agent.Mount()` 自带的三道 managed 检查（不满足就静默不点旗标）：

| 检查 | 含义 | 我们怎么过 |
|---|---|---|
| `CheckSkillForMounting(mount)` | 有 `AgentFlag.CanRide` **且** 骑术 ≥ 马的 `MountDifficulty` | 补旗标 + 把马难度压 0（夹具行为，回执里注明） |
| 马没在扬蹄 | `mount.GetCurrentActionType(0) != Rear` | — |
| `GetCurrentAction(0) == act_none` | 他此刻"手上没事" | 日志里 `clip-before` 可查 |

---

## 三、关键 API 与判据

| 概念 | API | 备注 |
|---|---|---|
| 控制权 | `Agent.Controller` | 🔴 **版本差异**：1.2.12 = 嵌套枚举 `Agent.ControllerType`；1.3.0+ = 顶级 `AgentControllerType`。**一律走 `V.SetAgentAI` / `V.SetAgentControllerNone` / `V.SetAgentPlayer`**，业务代码禁止裸写 |
| 上/下马旗标 | `Agent.EventControlFlags \|= Agent.EventControlFlag.Mount / Dismount` | 见 §二 |
| 交互对象 | `Agent.SetInteractionAgent(马)` | `Agent.Mount()` 内部会一并设 |
| 骑乘关系 | `Agent.MountAgent`（我骑的马）/ `Agent.RiderAgent`（马上的人）/ `HasMount` | `MountAgent` 的 setter 是 **private**（native 推的），别反射去写 |
| **能不能骑** | 🔴 `ItemObject.IsMountable` → `HorseComponent.IsRideable`（= 物品 XML 的 `is_mountable`） | `sumpter_horse`（驮马）是 `is_mountable=true is_pack_animal=true` ⇒ **能骑**；`HorseComponent.IsMount`（= `IsRideable && !IsPackAnimal`）会把驮马判死 |
| 那匹马是哪件物品 | `agent.SpawnEquipment[EquipmentIndex.ArmorItemEndSlot].Item` | 引擎自己也是这么取的（`HorseItem = agent.SpawnEquipment.GetEquipmentFromSlot(...)`） |
| 无主马登记表 | `Mission.Current.MountsWithoutRiders` | 引擎的 AI 只从这张表挑马（`AddMountWithoutRider` 在 `InitializeAgentProperties` 里、`IsMount && RiderAgent == null` 时调） |
| 坐骑令 | `Agent.SetRidingOrder(RidingOrder.RidingOrderEnum.Mount/Dismount/Free)`、`GetRidingOrder()` | `{ Free=0, Mount=1, Dismount=2 }`；**AI 自己找马那条链**的开关（见 §四末） |
| 选中的马 | `Agent.SetSelectedMountIndex(int)` / `GetSelectedMountIndex()` | 引擎 `HumanAIComponent.ReserveMount` 内部的**骑手侧那一半**（马侧 `OnMountReserved` 是 internal） |
| 刷马 | `Mission.SpawnMonster(ItemRosterElement, ItemRosterElement, in Vec3, in Vec2, int)` | 马是**物品**驱动的（`HasHorseComponent`） |
| 马上生成 | `AgentBuildData.Equipment(带马的装备)` → `Mission.SpawnAgent` | spawn 分支原文：`if (item != null && item.HasHorseComponent && item.HorseComponent.IsRideable) agent2 = CreateHorseAgentFromRosterElements(...)` |

---

## 四、骑上之后归谁控制（架构）

```
上马完成 ──> Controller = AI（我们还回去的）
              │
              ├─ 我们（AgentBrain）没派动作 ⇒ 引擎原版 DailyBehaviorGroup 驱动（城镇/村庄：闲逛/站桩/跟随）
              │                             骑乘移动/转向/碰撞 → native（monster_usage_sets 里马那套）
              └─ 我们派了动作           ⇒ 我们的脑自动 SuspendVanillaAI（停掉日常行为）后用 AgentNavigator 驱动
                                            但"怎么骑马"仍是引擎干（我们只设移动目标/播动作）
```

🔴 **我们自己的玩法层从来没考虑过"骑乘 NPC"**（`AI/` 与 `Planner/` 里 `HasMount` / `MountAgent` 出现 **0 次**）：
- 移动类动作（跟随/去某地/巡逻）给骑乘 NPC 用，**移动不会坏**（引擎骑马带他走），但**动画层**（状态机强制 0 号通道那套）没有骑乘分支；
- **捆人 / 扛人**这套明确没适配：动画、摆位、落点全部假定目标站在/趴在平地。

🔴 **AI 自找马那条链（`HumanAIComponent.OnTick`）单独不管用**（2026-10-13 实测）：
它要求 `Mission.AllowAiTicking` + `IsAIControlled` + `CommonAIComponent != null` + `!IsRetreating` +
`GetRidingOrder() == 1`，再每 2~3 秒（`MissionTimer(2f + 随机)`）从 `MountsWithoutRiders` 挑马 `ReserveMount`——
**全部门都开着、值也都设了，仍然什么都不发生**（托管层只负责"预定"，走路+上马是 native 干的；
而 native 对"和平场景里的平民/随从"不跑那段）。⇒ **上马一律用 §二那条旗标链**，坐骑令只当"还回 AI 之后的备选"。

---

## 五、控制"马上行为" —— 六件事的机制（2026-10-13 反编译调研完成，1.2.12 / 1.3.15 / 1.4.6 / 1.5.1 四版比对）

> **进度**：**第 5、6 件（主动下马 / 被动落马）已实机通过**；第 1~4 件（移动 / 跟随 / 转圈 / 骑射骑砍）
> 机制已查明、**验收命令尚未建**（提议清单见 §七）。

> **总纲一句话**：**引擎对"骑手"几乎没有专门的托管分支** —— 移动 / 战斗 / 下马全部复用步兵那套入口，
> 骑乘差异只落在 **native 内部**和**几个常数**上。所以"控制骑乘 NPC"=
> **用我们已有的入口 + 三个骑乘修正 + 两条骑乘专属通道**：
> 修正 = ① 到达阈值按官方骑乘值 **2.2 m** ② 限速要**人 + 马双写** ③ 动作名用 `*_horseback` 一族；
> 专属通道 = ④ **`GoWithoutMount`**（带着移动令一起下马） ⑤ **`BlowFlags.CanDismount`**（把他掀下马）。

| # | 要做的 | 走哪条路 | 关键证据 | 状态 |
|---|---|---|---|---|
| 1 | **马上移动**（走/跑到点） | `SetScriptedPosition(GoToPosition\|NoAttack\|NeverSlowDown[|DoNotRun])` —— **与步兵同一个入口**，托管层零骑乘分支；引擎自己把马带走 | 官方跑马比赛 `TownHorseRaceAgentController` 就是这么驱一整队骑手的 | 机制已证，待实机 |
| 2 | **跟随** | 同 1（我们自己的 `FollowAgentAction`）；或挂**原版** `FollowAgentBehavior`（它自带骑乘间距，白捡） | 官方跟随骑乘常数：到达 **2.2 m** / 纵向 1.25 m / 横向 1.5 m（步兵是 1.2 / 0.6 / 1.0） | 机制已证 |
| 3 | **转圈 / 绕圈** | 环形路点"**到达即喂下一个**"（带 `NeverSlowDown`）；椭圆轨迹算法可整套移植 | 官方跑马比赛（检查点闭环跑 2 圈）+ `BehaviorMountedSkirmish.Ellipse`（骑兵骚扰的椭圆轨道，纯数学无引擎依赖） | 机制已证，待实机 |
| 4 | **骑射 / 骑砍** | 首选**交回引擎**：敌对 Team + `Alarmed` + 弓/箭在手 ⇒ 原生战斗 AI 自动选骑马版动作；脚本路 = 强制 `*_horseback` 动作 + 自管投射物 | §5.4 | 机制已证，待实机 |
| 5 | **主动下马** | ① 下马旗标链（**已实现**，**每帧重发**）② `GoWithoutMount` 随移动令一步到位（native-only；探针命令已撤，见 §5.5）③ `RidingOrder.Dismount`（只有 AI 找马链读它，对无阵型单人**无效**） | §5.5 | ✅ **① 实机通过**（2026-10-13） |
| 6 | **被动落马** | ① 手搓 blow + `BlowFlags.CanDismount`(0x800)：**判定层可完全绕开** ② 马死掀人（**全 native**，不用管）③ blow 打**马** + `MakesRear`(0x200) 让马扬蹄 | §5.6 | ✅ **实机通过**（2026-10-13，`custom.grapple horse down blow\|rear\|kill`） |

### 5.1 马上移动 —— 与步兵同一个入口，托管层没有任何骑乘分支

- **入口照旧**：`agent.SetScriptedPosition(ref pos, false, flags)` 在托管层就是**一行 native 直通**
  （`1.2.12/Agent.cs:2140` 与 `1.5.1/Agent.cs:2468` **逐字相同**），没有任何骑乘分支、没有托管状态机。
  ⇒ 给骑手和给步兵发的是同一个调用，差别全在 native 怎么驱动那匹马。
- **官方先例（照抄对象）**：`SandBox.Tournaments.AgentControllers.TownHorseRaceAgentController`
  —— 跑马比赛里整队骑手就是 `SetScriptedPosition(下一个检查点, false, NeverSlowDown(8))` 驱动的；
  "停住不动"的官方写法 = `SetScriptedPositionAndDirection(自己当前位置, 当前朝向, false, 0)`。
- **`AgentNavigator`（SandBox）是原版唯一的路点驱动器，全类零 `MountAgent` 引用**（实测 grep = 0）——
  它的唯一出口是 `SetScriptedPositionAndDirection(ref pos, rot, false, flags)`，骑手与步兵走同一条路。
- **到达阈值按官方骑乘值 2.2 m**（步兵 1.2 m）：三个原版和平行为类（`FollowAgentBehavior` /
  `EscortAgentBehavior` / `ChangeLocationBehavior`）里 `HasMount ? 2.2f : 1.2f`。
- 🔴 **限速要"人 + 马"双写**：`SetMaximumSpeedLimit` 只管被调用的那一个 agent；
  引擎自己的路径是双写（`HumanAIComponent.AdjustSpeedLimit`：`Agent.SetMaximumSpeedLimit(...)`
  + `agent.MountAgent?.SetMaximumSpeedLimit(...)`）⇒ **我们对骑手限速时照抄双写**。
  ⚠️ `NeverSlowDown` 会绕过限速 ⇒ 想限速就别带这个 flag。
- **可读的反馈量**（外部驱动转圈/巡路时用）：`agent.MountAgent.GetCurrentVelocity()`（2D，官方比武用它判到达）、
  `.Velocity`（3D 世界系）、`.GetMovementDirection()` / `.MovementDirectionAsAngle` / `.GetTurnSpeed()`。
  人与马**各有独立的运动方向**（`AttackInformation` 官方就分别记录两者）。
- **骑手做不了的几件事（托管层硬拦）**：不能占 `StandingPoint`（`UsableMachine`：`MountAgent != null ⇒ 禁用`）、
  不能穿 `PassageUsePoint`（门/传送点）、不能撑矛（`_canPerformBrace = !HasMount`）。
- **`Agent.Controller` setter 的副作用**（我们摘/还控制权时会踩到）：新值 ≠ AI 且人形时，
  **清掉人与马双方的限速** + 点 `Run` 旗标；设成 Player 时顺手补 `AgentFlag.CanRide`。

### 5.2 跟随

- **两条路都通**：① 我们自己的 `FollowAgentAction`（= 5.1 的移动入口，只是目标点在动）；
  ② 原版 `FollowAgentBehavior`（`AgentControlHelper.StartVanillaFollow` 已接线）——**它自带骑乘间距，白捡**：
  到达 2.2 m / 纵向 1.25 m / 横向 1.5 m，且"跟随者排队"时按骑/步分别计数。
- **我们的 `FollowAgentAction` 是"演出式跟随"**：stopDistance 按步兵定（~1 m 级），跟骑马目标会贴太近
  ⇒ 骑乘场景把 stopDistance 往 2.2 m 靠；`keepFollow:false` 的 5 秒追赶瞬移兜底在骑马目标上更容易触发。
- 编队级的跟随偏移（`MovementOrder.FollowState.GetPositionAuxFollow`）：跟"骑马的队友"时整体后撤 2 m；
  骑兵跟随带速度前瞻（`+2 × 目标速度`）；停止阈值骑乘 4 m / 步下 2.5 m。
- ⚠️ 原版 `FollowAgentBehavior` 挂在 `DailyBehaviorGroup` 里，与 `SuspendVanillaAI` 互斥（已知口径）——
  "脑在干活时"用我们的跟随，"日常陪逛"用原版跟随。

### 5.3 转圈 / 绕圈 —— 官方有现成范本

- **范本 = 跑马比赛控制器**（见 5.1）：路点表 + "到达就喂下一个" + `NeverSlowDown`，跑满圈数收工。
  它判到达用的是场景里的 `VolumeBox` 触发器；我们退化用**距离**（骑乘阈值 2.2 m）即可。
- **椭圆轨迹的现成算法**：`BehaviorMountedSkirmish` 内嵌 `private struct Ellipse` ——
  `GetTargetPos(position, distance)` 沿敌阵外侧椭圆轨道推进（半径 = 敌骑兵 35 m / 敌步兵 20 m），
  **约 60 行纯数学、无引擎依赖，可直接移植**成逐 agent 版本。
  ⚠️ 但 `Behavior*` 那套本身是**编队级**的（构造要 `Formation`、只对 AI 编队生效）——
  散人/随从场景**用不上它**，只用它的数学。
- 🔴 **"转圈时怎么让马转向"仍未决**：`SetMovementDirection` 写的是人还是马，托管层查不到证据
  （全 DLL 只有 3 个调用点，全是传送后修朝向）⇒ **先走"喂路点"这条已被官方验证的路**，
  想用 `SetMovementDirection` 直接转向再单独做 5 分钟实验（判据 = 读人与马各自的 `GetMovementDirection()` 夹角）。
- ⚠️ 别朝"会被逼近的点"转身（历史坑，见 agent.md）——转圈喂点天然是"朝行进方向"，不踩这个坑。

### 5.4 骑射 / 骑砍（战斗）

- 🔴 **动画是引擎"按骑没骑"自己换的，选择权在 native**：`item_usage_sets.xml` **由 C++ 解析**
  （字符串 `item_usage_sets` 只存在于 `TaleWorlds.Native.dll`），同一 `<usage>` 条目按
  `is_mounted="True"/"False"` **成对**（`act_ready_bow` ↔ `act_ready_bow_horseback`）
  ⇒ **走"交回引擎"这条路，我们一行动画代码都不用写**。
- 🔴 **C# 的 `HumanAIComponent` 不是"战斗大脑"**：它只干四件事——推 7 条"距离→意愿"曲线、
  捡装备、找马、阵型速度；**"什么时候瞄准/出手/射箭"全在 native**。
  ⇒ "让骑手肯打/肯射"的杠杆 = **曲线 + 警戒 + 敌对**，不是切动作表。
  - 骑马战斗是**独立的两条曲线**（`Knowledge/Agent_AI底层原理.md` §3.4）：
    `ChargeHorseback`（骑马冲锋）默认 (0,2)→(25,5)→(30,5)、`RangedHorseback`（骑射）默认 (0,2)→(15,6.5)→(30,5.5)。
    可 `SetAIBehaviorParams` 直推；**阵型外（`Formation == null`）不会被预设刷回**。
- **"让和平场景里的骑手开始打人"的最小配方**（官方 `SandBox.MissionFightHandler` 就是这套，
  我们的 `CombatManager.StartFight` 已具备）：敌对 Team + `SetWatchState(Alarmed)` + 手上有武器/弹药。
  - ⚠️ 两个潜闸门：① `AIScriptedFrameFlags.NoAttack`（脚本移动锁常带，别压着进战斗）；
    ② **阵型 `FiringOrder`**：`HoldYourFire` 会让骑射**不开火**（原版骑射行为
    `BehaviorHorseArcherSkirmish` 第一件事就是 `Formation.SetFiringOrder(FireAtWill)`）。
  - 🔴 **官方收尾比我们多三步**（建议补进 `CombatManager.EndFight`，待实测）：
    `ResetEnemyCaches()` + `InvalidateTargetAgent()` + `InvalidateAIWeaponSelections()`
    ——治"打完还盯着对方 / 还举着旧枪"。
- **骑射的装备硬前提（数据层，不是代码层）**：
  - 弓与箭都放 `Weapon0..3`（没有独立弹药槽）；引擎认"有箭"= 弹药堆叠上限 > 1 且数量 > 0。
  - 🔴 **普通 `bow` 有全套 `_horseback` 动作；`long_bow` 带 `requires_no_mount`（长弓禁止骑马用）**；
    弩类有 `WeaponFlags.CantReloadOnHorseback`；`AgentFlag.CanUseAllBowsMounted` 托管层从无写点
    （native 按技能/perk 置位，别指望我们自己点）。
- **动作名清单**（脚本路强制播用；🔴 **全部已在 `as_human_warrior` 登记** ⇒ `SetActionChannel` 直接播，
  不用换 action set）：
  - 骑射 8 条：`act_ready_bow_horseback` / `act_ready_continue_bow_horseback` /
    `act_ready_continue_bow_shake_horseback` / `act_release_bow_horseback` /
    `act_reload_bow_top_horseback` / `act_reload_bow_right_horseback` / `act_reload_crossbow_horseback`(×2)
  - 骑砍挥击：`act_release_overswing_horseback` / `act_release_slash_horseback_left|right` /
    `act_release_thrust_1h_horseback` / `act_release_thrust_2h_horseback` /
    `act_release_thrust_1h_lance_horseback` / `act_release_slash_2h_horseback_left|right`（各带 `_balanced`）
  - 格挡/招架：`act_blocked_*_horseback`、`act_quick_blocked_*_horseback`、`act_defend_*_staff_*_horseback`
- **脚本路"出箭"**：播动作 ≠ 出箭。原生射击回调 `Mission.OnAgentShootMissile` 是 **native→C#**（脚本调不到）。
  自造弹的公开入口 = `Mission.AddCustomMissile(...)`（**public，1.2.12 同签名，返回 `Missile`**）
  + 弹道工具 `Mission.GetMissileVerticalAimCorrection` / `GetMissileRange`（public static，两版都有）
  + 手动扣弹药（`Agent.SetWeaponAmountInSlot` / `MissionWeapon.Consume`）。
- **战场小队骑射**（编队场景）：原版有公开的 `BehaviorHorseArcherSkirmish` / `BehaviorMountedSkirmish`
   C# 阵型行为 —— 给编队下行为 + `FireAtWill` 即可，不用碰个体 AI。（散人/随从场景用不上，见 5.3。）
- ⚠️ **待实测（脚本路三问）**：① 强制播 release 动作，native 会不会自己补一次射击回调（会 ⇒ 不用造弹）；
  ② 步下强播 horseback 动作的观感（预期"站着做骑乘动作"）；③ 骑射动作的上下半身分层规则是否与步战一致。

### 5.5 主动下马 —— 三条路（✅ 2026-10-13 实机通过：`custom.grapple horse dismount`）

1. **下马旗标（已实现）**：`EventControlFlags |= EventControlFlag.Dismount`（值 = 1）。
   引擎播 `act_dismount_horse_to_left/right`（`monster_usage_sets.xml` 的 `is_mounted="True"` 两条）。
   - 🔴 **玩家侧旗标每帧被清零重建**（`MissionMainAgentController`，View 层：先 `EventControlFlags = 0`
     再按输入重建）⇒ **玩家必须每帧重发**；引擎放行的门槛 = **前进速度 < 0.5 m/s 且马不在扬蹄**。
     ✅ 2026-10-13 已改成**每帧重发**并实机通过（玩家慢下来那一刻引擎就受理）。
   - NPC 侧：先摘 `Controller`（否则 AI 每帧覆写旗标），点完等 native 演完再还 AI（已实现）。
   - `Agent.Mount(horse)` 的 **else 分支就是下马**（必须把**自己正骑着的那匹**传进去）；
     下马分支不查骑术、不查当前动作，**只查"马没扬蹄"**。
   - ⚠️ `ActionCodeType.Rear` 的值 **1.2.12 = 46 / 1.5.1 = 47**，禁硬编码。
2. 🔴 **`GoWithoutMount`（0x20）—— 本轮新发现**：`SetScriptedPosition(目标, false, GoWithoutMount)`
   = "**别骑着过去**"。全游戏**唯一用法**在马上比武（`JoustingAgentController`）：比武结束要去跟对手
   **步战决斗**时，先 `Controller = AI` 再发这条带 flag 的走位，随后代码**等 `MountAgent == null`** 才继续
   ⇒ 语义 = 下马 + 走过去（托管层零消费者，纯 native）。
   ⚠️ 待实测：是"立刻下马再走"还是"到了才下"。
   🔴 **语义探针命令已撤**（2026-10-13 用户裁定"多此一举"——"下马 + 移动"我们本来就有两条命令）。
   **旗标知识留着**：哪天要"骑着到地方再自己下马"这类一条令搞定顺序的需求，再回来查这条。
3. **`RidingOrder.Dismount`（弱）**：托管层**唯一读者**是 `HumanAIComponent` 的 AI 找马链
   （它读 `GetRidingOrder() == 1`）；对无阵型的单个 NPC = 什么都不发生（与本项目实机结论一致）。

**观测上下马的正规钩子**（四版都有）：`MissionBehavior.OnAgentMount(Agent)` / `OnAgentDismount(Agent)`；
更细的每-agent 钩子 = `Agent.OnAgentMountedStateChanged`（public `Action` 字段，上下马都调）。

### 5.5.1 🔴 玩家**自己**下马：旗标不可靠 ⇒ 用 blow（2026-10-19 实机定案）

**症状**：骑马对墙开钩索（语义 = 自动下马 → 起飞），连打两枪全部
`⚠ 骑马勾到地形：2.5 s 内没能下马 ⇒ 这一钩作废`（`Debug/StoryEngine_RuntimeLog.txt` 09:47:23.776 / 09:47:31.058）。
**每帧重发旗标也拉不回来**。

**结论（修正 §5.5 里"每帧重发就能下"的乐观口径）**
- **旗标**（`EventControlFlags |= Dismount`）只在**你几乎停住**的时刻才被受理
  （门槛 = 前进速度 < 0.5 m/s 且马不在扬蹄）⇒ **骑着 / 走着 = 一直拒**。"边骑边下马"不成立。
- **`CanDismount` blow 是唯一可靠路径**：native 直接执行、**不看马速**，实测 **0.2 s** 摘马
  （与 §5.6 目标侧结论同源）。
- **三种下马的动画时长**（`tpaccli clipinfo` 实测，1.2.12 装机包）：

  | 动作 | clip | 时长 |
  |---|---|---|
  | 主动下马 | `dismount_rider_to_left/right` | **1.2 s** 主段（+0.5 s 站稳续段；带 `lock_camera`/`lock_movement`） |
  | 被掀下马 | `rider_only_fall_left/right_*` | **0.2 s** 主段（后面 1.7~2.3 s 是 `_get_up`，**不用等**） |

  ⇒ 快 6 倍；而且被掀的观感（"绳一绷把人扯下来"）更贴动作语义。

**给玩家自己打 blow 的配方**（范本 = `GrappleLogic`"骑马等马"分支 + `GrappleBind.FireDismountBlow`）
- **只打一发**（重复打 = 反复踉跄）；
- `attackerIndex = -1`（**不归属任何人** —— 否则会被伤害/犯罪链记成"玩家攻击玩家"）；
- 血不掉：`FireDismountBlow` 内部临时 `Immortal`、`finally` 还原；
- 方向朝**钩点**（读作"绳绷紧把人往墙那边扯"，与接下来起飞同向）；
- **旗标照旧每帧重发当兜底**（blow 万一被拒还有老路）；
- 旋钮：`custom.grapple mountedselfblow 0|1`（默认开；关掉 = 只点旗标，A/B 用）。

**代价（已知并接受）**：玩家身上走一次真实打击管线 —— 会播"被击落"的受击反应、`OnAgentHit` 广播一发
（犯罪/警戒体系可能收到，攻击者 −1 不归属）。

### 5.6 被动落马 —— 手搓 blow 是正道（✅ 2026-10-13 实机通过）

- 🔴🔴 **实机结论（最要紧的一条）：`BlowFlags.CanDismount` 在 native 侧不做二次判定** ——
  手搓 blow 打出去的**那一刻**骑手就进了摔落动画（日志实测 clip 立刻变 `act_rider_only_fall_*`，
  `MountAgent` 随即变 null），**不看马速、不看伤害与抗性**。反编译时推断的"flag = 判决书"成立：
  引擎自己的判定（武器类型/部位/穿透 vs 骑术）只发生在"它自己决定要不要置这一位"的时候，
  **位一旦置上，native 就执行**。⇒ "被绳扯下马""被撞下马"这类玩法，一条 blow 就能落地。
- **自然路 ①（武器击落）**：`CanWeaponDismount` 只认「**步下**攻击者 + 挥砍 + `WeaponFlags.CanHook`」
  或「戳刺 + `WeaponFlags.CanDismount`」，再过 `DecideAgentDismountedByBlow`
  （这一击不能打死 + 没被 `ShrugOff` + `穿透 vs 抗性`，抗性 = 骑术）——**很窄，别指望**。
- **自然路 ②（马死）**：**全 native，托管层零处理**；马的善后 = `UpdateRiderAgentCache(null)`
  ⇒ 进 `Mission.MountsWithoutRiders`（变无主马）。我们不用做任何事；
  `Mission.KillAgentCheat(马)` 是现成的"杀马验掀人"入口（实机可用）。
- 🔴 **手搓 blow + `BlowFlags.CanDismount`（0x800）**：
  - `BlowFlags.CanDismount` 在**托管层只写不读**（唯二写点 = `Mission` 构造近战/导弹 blow 处；
    名字相近的 `WeaponFlags.CanDismount` 是**另一个枚举**，别混）⇒ 它是**给 native 的指令位**。
  - managed 侧全部门槛链 = `Agent.RegisterBlow` → `HandleBlow`：**唯一硬门槛 `if (InflictedDamage <= 0) return;`**
    （所以伤害数要 > 0，`Immortal` 顶着不扣血）→ `Mission.OnAgentHit` 广播 → `HandleBlowAux(ref b)`（native 读 flag）。
  - ⚠️ 走这条路**不会**触发 `MissionBehavior.OnRegisterBlow`（那个只在原生管线 `Mission.RegisterBlow` 里调），
    但**会**触发 `OnAgentHit`（犯罪/警戒体系可能重复记账）。
  - 现成实现 = `GrappleBind.FireDismountBlow(victim, rear, damage, dirToward)`；验收 `custom.grapple horse down blow`。
- **让马扬蹄（`BlowFlags.MakesRear` 0x200，blow 打**马**）**：同样手搓 blow 绕过判定层；
  验收 `custom.grapple horse down rear`（2026-10-13 实机确认命令可用）。
  ⚠️ 连锁：**马扬蹄期间原版禁上马也禁下马**。
  （未验备选：直接给马点 `EventControlFlag.Rear`(4) —— 该位托管层从无写点，且马是 AI 控制 ⇒ 同"旗标活不过一帧"，要先摘控制器。）

---

## 六、坑与纪律（都是实机踩出来的）

1. 🔴 **旗标会被 AI 每帧覆写** ⇒ 任何"外部点旗标"的玩法（Mount / Dismount / Jump…）都必须先摘 `Controller`。
2. 🔴 **能骑判据只有 `IsRideable`**（`Agent.IsMount` 太粗；`HorseComponent.IsMount` 会把驮马判死）。
3. 🔴 **摘 AI 的那 1~2 秒他没有任何 AI**（站着等动画）—— 收尾**必须**把控制权还回去，否则他永远是木偶；
   还回去的时机 = "确认上/下马成功"或"超时"（本项目由 `GrappleLogic.TickMountRequest` 每帧盯着）。
4. **组件是引擎按 `Controller` 自动增删的**（`AgentCommonAILogic` / `AgentHumanAILogic`），别自己去 `AddComponent`。
5. **马物品决定马的属性**（血量/速度/机动/难度）：`saddle_horse` 最普通，`difficulty=0` 的马谁都能骑。
6. **刷出来的马记得清 `AgentFlag.CanWander`**，否则它会自己溜达走（官方 `AnimalSpawnSettings.CheckAndSetAnimalAgentFlags` 干的就是这件事）。
7. 🔴 **玩家侧的一切旗标都是"每帧清零重建"的**（`MissionMainAgentController` 先把 `EventControlFlags` 归零
   再按输入重建）⇒ 给玩家点 `Dismount` / `Rear` 之类的旗标**必须每帧重发**（NPC 侧摘了 Controller 才不用）。
   引擎放行下马的门槛 = **前进速度 < 0.5 m/s 且马不在扬蹄**。
8. 🔴 **对骑手限速要人 + 马双写**（`SetMaximumSpeedLimit` 只管被调的那一个 agent，引擎自己的路径就是双写）；
   带 `NeverSlowDown` 的移动令会绕过限速。
9. 🔴 **骑手做不了**：占 `StandingPoint`（用器械/工位）、穿 `PassageUsePoint`（门）、撑矛（brace）——托管层直接禁用。
10. **`Agent.Controller` setter 有副作用**：新值 ≠ AI 且人形 ⇒ **清人与马双方的限速** + 点 `Run` 旗标；
    设成 Player ⇒ 补 `AgentFlag.CanRide`。（摘/还控制权时会踩到，别以为限速还在。）
11. 🔴 **手搓 blow 走的是 `Agent.RegisterBlow`，不是 `Mission.RegisterBlow`** ⇒ `MissionBehavior.OnRegisterBlow`
    **不会**触发（别指望那个钩子观测），但 `OnAgentHit` **会**（犯罪/警戒体系可能重复记账）。
    唯一硬门槛 = `InflictedDamage > 0`（`Immortal` 顶着即可）。
12. **马扬蹄（Rear）期间原版禁上马也禁下马**；`ActionCodeType.Rear` 的值 **1.2.12 = 46 / 1.5.1 = 47**，禁硬编码。
13. **`GoWithoutMount`(0x20) 是 native-only 旗标**（托管层零消费者，全游戏唯一用法 = 马上比武的"下马走位"）；
    想查它的用法时注意：反编译里写成数值 `(AIScriptedFrameFlags)32`，**按名字 grep 找不到**。
14. 🔴 **骑射要配普通 `bow`，别配 `long_bow`** —— `long_bow` 的用法组带 `<flag name="requires_no_mount"/>`
    （数据层直接禁骑马用）；弩类有 `WeaponFlags.CantReloadOnHorseback`。
15. **骑射有一条隐藏闸门 = 阵型的 `FiringOrder`**：`HoldYourFire` ⇒ 骑手不射；
    原版骑射行为第一步就是设 `FireAtWill`。排查"戒严了但不开火"先查它。
16. **`Mission.OnAgentShootMissile` 是 native→C# 回调（internal），脚本调不到** ——
    "强制播 release 动作就出箭"这句话**没被证实**；要自己出箭走 `Mission.AddCustomMissile`（public）。
17. **官方战斗收尾比我们多三步**：`ResetEnemyCaches()` + `InvalidateTargetAgent()` +
    `InvalidateAIWeaponSelections()`（`MissionFightHandler.ResetTeamsForFightAndDuel`）——
    治"打完还盯着对方 / 还举着旧枪"，我们的 `CombatManager.EndFight` 可考虑补（待实测）。

---

## 七、落地件与验收命令

| 件 | 位置 |
|---|---|
| 请求保持 + 控制权还原 | `Combat/GrappleLogic.cs`（`RequestMount` / `RequestDismount` / `TickMountRequest` / `FinishHorseRequest`） |
| 命令族 | `CampaignMode/Tools/GrappleCommands.cs`（`custom.grapple horse …`） |

```
custom.grapple horse                      状态行：谁骑着什么 / 身边几匹空马（异步动作隔一秒再敲一次就是判据）
custom.grapple horse spawn [数量] [物品id]  面前 3 米刷马（站着不走）
custom.grapple horse mount [#编号|名字|me]   让目标上最近那匹能骑的空马（先摘 AI → 点旗标 → 还 AI）
custom.grapple horse dismount [#编号|名字]   让骑着的那个落马（同一条链）
custom.grapple horse rider [模板id]         刷一个"已经骑在马上"的 NPC（引擎 spawn 时就挂马，不依赖 AI）
custom.grapple horse why [#编号|名字]        AI 找马那条链的逐门读数（排查用）
```

**日志标签**：`[GrappleHorse] mount:`（一次打全：选中哪匹马/什么物品/距离/动作/旗标/有没有摘控制器）·
`[GrappleHorse] why:`（逐门读数）· `[GrappleHorse] mount request held:`（每秒回读）·
`[GrappleHorse] … request finished: MOUNTED` + `controller restored to AI`（成功判据）。

### 控制马上行为的验收命令 —— 第一批已建并**实机通过**（下马 / 落马，2026-10-13）

> 沿用现有命令族 `custom.grapple horse …`（不新开顶层名）。✅ = 2026-10-13 用户实机确认可用。

| 命令 | 干什么 |
|---|---|
| ✅ `custom.grapple bind test mounted` | **勾住骑手 = 拉下马**（只挑骑马的目标走整套）。**实测 0.20 秒落地**，随后**直接进「拉倒 → 趴缚」**（被 blow 掀下来的不再走缠/受击/站缚） |
| ✅ `custom.grapple bind mountedblow 0\|1` | "拉下马"要不要**同时打一发 `CanDismount` blow**（默认 **开**）：开了 = native 直接执行、**不看马速**；关掉 = 只走引擎下马事件（A/B 对照用） |
| ✅ `custom.grapple horse down [#编号\|名字] [blow\|rear\|kill]` | **被动落马**三条路：`blow` = 手搓 `CanDismount` blow 掀人 / `rear` = 打马 + `MakesRear` 扬蹄 / `kill` = 杀马（native 掀人）。判据 = 5 秒后日志 `DownWatch request finished: watch done: …`。🔴 **模式词可在任意位置**（`down rear` 与 `down #2 rear` 都行） |
| ✅ `custom.grapple horse dismount [#编号\|名字\|me]` | 主动下马（引擎下马事件，**每帧重发旗标** —— 玩家侧旗标会被输入层每帧清零 ⇒ 只点一次无效）；玩家侧引擎只在**速度 < 0.5 m/s** 时放行 |
| ✅ `custom.grapple horse mount [#编号\|名字\|me]` | 让人上马（每帧重发；**上马失败会撤回坐骑令**，马被别人骑走时立刻中止并说明） |
| ⚠️ 别名变更 | `fall` / `unhorse` 从"主动下马"改挂到 **`down`（被动落马）** 上 |

**其余命令（移动 / 跟随 / 转圈 / 战斗）尚未建** —— 提议清单与判据见下表，**待拍板**：

| 提议命令 | 干什么 | 覆盖哪件事 | 验收判据（现成读数） |
|---|---|---|---|
| `custom.grapple horse goto <#\|名字> [距离]` | 让骑手走到玩家面前/指定距离（**到达阈值按骑乘 2.2 m**） | 马上移动 | 到位后状态行显示距离 ≤2.2 m；中途不卡 |
| `custom.grapple horse follow <#\|名字>` | 挂原版跟随（自带骑乘间距） | 跟随 | `[VanillaFollow]` 日志 + 目视间距 |
| `custom.grapple horse circle <#\|名字> [半径] [圈数]` | 绕玩家/某点转圈（路点闭环 + `NeverSlowDown`） | 转圈 | 目视圈形；日志打每圈耗时与到达判定 |
| `custom.grapple horse fight <#\|名字> [目标#]` | 让骑手进入战斗（敌对 + `Alarmed` + 武器检查 + **FiringOrder 检查**） | 骑射 / 骑砍 | `[CombatManager] StartFight teams:` 行；目视他骑马挥砍/放箭 |

**新窗口/开关类**（观感项）：`circle` 支持 `hold <半径>` 锁死半径静观；`fight` 支持 `will 0|1` 压/放 `FiringOrder`（用于验证"戒严不开火"这条闸门）。


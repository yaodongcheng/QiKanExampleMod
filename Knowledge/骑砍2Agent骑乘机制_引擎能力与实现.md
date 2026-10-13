# 骑砍2 Agent 骑乘机制 — 引擎能力与实现

> **谁读这份**：要让 NPC / 随从上马、下马，以及要控制"马上行为"（移动 / 转圈 / 射击 / 挥砍 / 落马）的人。
> **结论来源**：反编译（`TaleWorlds.MountAndBlade` / `SandBox` / `TaleWorlds.CampaignSystem` × 1.2.12 / 1.3.15 / 1.5.x 三版比对）+ **2026-10-13 实机验证通过**。
> **落地件**：`Combat/GrappleLogic.cs`（请求保持 + 控制权还原）· `CampaignMode/Tools/GrappleCommands.cs`（`custom.grapple horse` 族）。

---

## 一、结论速查（先看这张表）

| 要做的事 | 走哪条路 | 状态 |
|---|---|---|
| **让一个已有 agent 上马** | ① `Controller = None` ② `EventControlFlags |= Mount` + `SetInteractionAgent(马)` ③ native 演上马动画 ④ 把 `Controller` 还回去 | ✅ **2026-10-13 实机通过** |
| **让他下马** | 同一条链，旗标换 `Dismount`（AI 那条另有 `SetRidingOrder(Dismount)`，见 §五.5） | ⏳ 已落盘、待实机 |
| **判"这匹马能不能骑"** | 🔴 `物品.HorseComponent.IsRideable`（**不是** `Agent.IsMount`，也**不是** `HorseComponent.IsMount`） | ✅ 实机踩过两次 |
| **凭空刷一匹马** | `Mission.SpawnMonster(new ItemRosterElement(马物品, 1, null), default, pos, dir, -1)`（官方 `SandBoxHelpers.MissionHelper.SpawnHorses` 同款） | ✅ |
| **让人"一出生就骑着"** | `AgentBuildData` + spawn 装备的**马槽**（`ArmorItemEndSlot`）里放马 ⇒ 引擎自己造马并挂上 | ✅（本项目 `horse rider` 夹具） |
| **上马之后的移动/战斗** | **不归我们** —— 控制权还回 AI 后由引擎自己驱动 | ✅ |

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

## 五、控制"马上行为"的初查（下一步方向）

> 每条给出：**走哪条路 + 现成轮子 + 待验证**。⚠️ 都还没实机验证过。

| # | 要做的 | 走哪条路 | 现成轮子 / 证据 | 待验证 |
|---|---|---|---|---|
| 1 | **马上移动**（走到某点 / 跟随） | 设移动目标，native 骑马过去 | `AgentControlHelper.ScriptedMoveToPoint` / `SmartMoveToPoint` / `ScriptedMoveToAgent`；动作层 `MoveToPositionAction` / `FollowAgentAction` | 骑乘状态下这些还灵不灵（预期灵，native 负责转向与速度） |
| 2 | **转圈 / 绕圈跑** | 候选 A：连续喂**环形移动目标**（每 0.3~0.5 s 更新一次目标点，native 自然转向）；候选 B：每帧 `SetMovementDirection` 直接写朝向（飞行工程验证过的写法） | `Agent.SetMovementDirection(Vec2)`（`LookDirection` 赋值实测转不动） | A/B 哪个更像"骑马绕圈"；A 的更新频率 |
| 3 | **射击**（骑射） | 候选 A（推荐）：交回引擎 AI —— `SetTargetAgent(敌)` + 保证有弓/箭 + 别压 `NoAttack`；候选 B（脚本演出）：强制动作通道播骑马版 release + 自己造弹（我们有"自管实体投射物"的成熟经验） | 🔴 **骑马版动作是"用法组"里另写的一段**：`item_usage_sets.xml` 的 `<rider_idles>` / `<rider_* mount_id="horse">`（与原版 `act_ready_bow_horseback` / `act_release_bow_horseback` 一族对应）⇒ **引擎按"骑没骑"自己挑**，走 A 不用管；走 B 得手挑 horseback 变体 | A 的索敌/射击节奏由谁定；B 要自己算弹道 |
| 4 | **挥砍**（骑砍） | 同 3：A = 引擎战斗 AI（`CombatManager` 那套已跑通步战）；B = 强制通道播 `act_*_horseback` | 骑马版近战动作在 vanilla 的 `act_*_horseback` 族（`act_blocked_overswing_horseback` 等）；**用法组里同样是 `rider_*` 段**（见 3） | 骑马攻击是否也吃"上下半身分层"（通道 0/1）那套规则 |
| 5 | **主动下马** | ① 旗标链（同 §二，换 `Dismount`）—— 已落盘；② AI 那条：`SetRidingOrder(Dismount)`（`RidingOrderEnum.Dismount = 2`，引擎给阵型下达的"全员下马"令） | `custom.grapple horse dismount` | ②对单个 agent 灵不灵（阵型是引擎推给单位的） |
| 6 | **被动落马** | ① 引擎自己：马死 / 受击够重（native 掀人）；② 我们造一发带 `BlowFlags.CanDismount`(0x800) 的 blow（`RegisterBlow` → native），或让**马**吃 blow（`MakesRear` = 扬蹄） | 见 `plans/钩索-实施计划.md` §13.5.1（判定层可绕过、执行层在 native） | 手搓 `CanDismount` 到底认不认（判据 = `LogBlowResult` 打的"引擎选了哪条受击动画"，落在 `act_rider_only_fall_*` / `act_fall_rider_*` 就是生效） |

---

## 六、坑与纪律（都是实机踩出来的）

1. 🔴 **旗标会被 AI 每帧覆写** ⇒ 任何"外部点旗标"的玩法（Mount / Dismount / Jump…）都必须先摘 `Controller`。
2. 🔴 **能骑判据只有 `IsRideable`**（`Agent.IsMount` 太粗；`HorseComponent.IsMount` 会把驮马判死）。
3. 🔴 **摘 AI 的那 1~2 秒他没有任何 AI**（站着等动画）—— 收尾**必须**把控制权还回去，否则他永远是木偶；
   还回去的时机 = "确认上/下马成功"或"超时"（本项目由 `GrappleLogic.TickMountRequest` 每帧盯着）。
4. **组件是引擎按 `Controller` 自动增删的**（`AgentCommonAILogic` / `AgentHumanAILogic`），别自己去 `AddComponent`。
5. **马物品决定马的属性**（血量/速度/机动/难度）：`saddle_horse` 最普通，`difficulty=0` 的马谁都能骑。
6. **刷出来的马记得清 `AgentFlag.CanWander`**，否则它会自己溜达走（官方 `AnimalSpawnSettings.CheckAndSetAnimalAgentFlags` 干的就是这件事）。

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

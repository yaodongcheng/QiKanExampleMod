# stealth — 轮子速查分卷（wheels.md 索引导航）
## 动物识别 — `InteractionMissionView.IsAnimalAgent` / `GetLivestockItemForAnimal`

```csharp
// 判断是否为动物 Agent（村庄场景中 IsHuman=false 的牲畜）
InteractionMissionView.IsAnimalAgent(agent);  // → bool

// Monster.StringId → 牲畜 ItemObject 静态缓存（两轮查找：精确 ID + 遍历 Animal 类型 + 兜底名字匹配）
ItemObject item = InteractionMissionView.GetLivestockItemForAnimal(monsterId, animalName);
```

动物 monster ID 白名单：`sheep`, `cow`, `hog`, `goose`, `chicken`（`InteractionMissionView.AnimalMonsters`）。


---

## 偷窃持久化 — `VillageAnimalTracker`

三层持久化数据（按 `"settlementId|monsterId"` 为 key，JSON 序列化，`MyBehavior.SyncData` 跨存档）：

```csharp
// 记录偷窃
VillageAnimalTracker.RecordTheft(settlementId, monsterId, count = 1);
// 查询被偷数
int stolen = VillageAnimalTracker.GetStolenCount(settlementId, monsterId);
// 每日自然恢复（每种每天恢复 1 只）
VillageAnimalTracker.DecayDaily();  // MyBehavior.DailyTick 中调用

// 缓存/读取场景自然生成数（首次进场景时记录）
VillageAnimalTracker.SetNaturalCount(settlementId, monsterId, count);
int natural = VillageAnimalTracker.GetNaturalCount(settlementId, monsterId);
bool has = VillageAnimalTracker.HasNaturalCache(settlementId);
```


---

## ItemRoster 补足 — `InteractionMissionView.TopUpRosterToNaturalCounts`

```csharp
// 按缓存自然数补足村庄 ItemRoster：expected = naturalCount - stolenCount，只补不删
// 可从场景进入或村庄菜单调用（无场景依赖）
InteractionMissionView.TopUpRosterToNaturalCounts(settlement);
```


---

## 偷动物 — `InteractionMissionView.TryStealAnimal` → 抓动物偷窃条 → `CompleteAnimalSteal`

```csharp
// 蹲下才能偷：UI 三层门槛（站着提示「蹲下才能偷」/输入层拦截/TryStealAnimal 防御守卫）
// TryStealAnimal：守卫 → 开 StealBar Animal 模式（子弹时间+冻结，一次出手定胜负）
//   命中（AnimalCaught）→ CompleteAnimalSteal(async)：面向 → act_pickup_down_begin → 400ms
//     → 动物存活+距离≤5m 复查 → GetLivestockItemForAnimal → StealManager.StealAnimal
//     → animal.FadeOut → act_pickup_down_end → 「获得了 xx！」
//   手滑（AnimalFled）→ VM 内 OnAnimalStruggleFlee：目击者警戒脉冲 + WitnessCrime 围堵 + 逃跑，动物留下可再抓
// _isStealingAnimal 并发守卫贯穿条+动画全程；CloseStealInterface 统一复位（含 _stealAnimalTarget 清空）
```

**犯罪记账对齐**：`RecordAnimalTheft` 有目击时除证词登记外，还会 `BroadcastEventInRange("WitnessCrime")` 立即围堵（victim=null，与保管箱偷窃同一处理方式）。


---

## 动物近距离检测

`ProcessAgentCandidate` 中 `NpcSightSystem.IsPlayerSeeing` 对动物永远返回 false（`TickTrackedTarget` 过滤非人类 Agent），动物跳过此预检，只依赖距离+点积判定。


---

## 价格修正 — `VillageAnimalPricePatch`

非本地特产动物（不在 `Village.VillageType.Productions` 中）：买入 5 倍、卖出 0.3 倍。只对玩家交易生效。

```csharp
// Harmony Postfix on VillageMarketData.GetPrice(EquipmentElement, MobileParty, bool, PartyBase)
// 自动生效，PatchAll 注册
```


---

## 触发点一览

| 触发时机 | 补丁 / 方法 | 作用 |
|----------|------------|------|
| 进村庄场景 | `SyncSceneAnimalsWithInventory` (MissionView.OnMissionTick 首帧) | 缓存自然数 + 裁剪被偷动物 + 补 Roster |
| 开村庄菜单 | `VillageMenuAnimalPatch` (Harmony Postfix on `GameMenu.SwitchToMenu("village")`) | 补 Roster（读缓存，不进场景也能触发） |
| 交易界面打开 | `TradeScreenAnimalLoggerPatch` (Harmony Prefix on `InventoryManager.OpenScreenAsTrade`) | 打印 ItemRoster 动物日志 |
| 价格查询 | `VillageAnimalPricePatch` (Harmony Postfix on `VillageMarketData.GetPrice`) | 非本地动物价格修正 |

**文件位置**：`Stealth/VillageAnimalTracker.cs`、`Interaction/InteractionMissionView.cs`（`SyncSceneAnimalsWithInventory` / `TryStealAnimal` / `TopUpRosterToNaturalCounts` / `ProcessAgentCandidate` 及全部 Patch 类）、`Core/MyBehavior.cs`（`DailyTick` 衰减 + `SyncData` 持久化）。

---

# 场景感知分发模式（ChestContext）— `Stealth/StealManager.cs`

**解决什么问题**：定居点有多个子场景（城镇 = 中心/酒馆/领主大厅/暗巷/竞技场），任何「按场景差异化」的系统（偷窃金库、贿赂、情报、锚点 NPC）都要回答四个问题：**①我在哪个场景 ②这个场景分多少资源 ③这个场景出什么内容 ④文案/锚点怎么随场景变**。这个模式用一张枚举 + 四张 switch 表统一回答，加新场景 = 每张表加一行，不写 if-else 链。


---

## 四件套结构

```csharp
// ① 场景枚举
public enum ChestContext { Village, TownTavern, TownCenter, LordsHall, Alley, Arena, Dungeon, Castle, Unknown }

// ② 场景识别：Location.StringId 优先（精确到子场景），回退定居点类型
ChestContext ctx = StealManager.GetCurrentChestContext();
//   locId 含 "tavern"→TownTavern / "lordshall"→LordsHall / "alley"→Alley / "arena"→Arena
//   "prison"|"dungeon"→Dungeon（原版地牢 id 为 "prison"，dungeon 兼容其他 mod 命名）
//   "center" 或含 "village" → 按 Settlement.IsVillage 分 Village/TownCenter
//   回退：IsVillage→Village / IsTown→TownCenter / IsCastle→Castle / else Unknown

// ③ 资源权重表 + 内容过滤表（各一张 switch）
float w = StealManager.GetChestContextGoldWeight(ctx);   // 中心.40/大厅.30/酒馆.15/暗巷.10/竞技场0/村·城堡1.0/Unknown.20
bool ok = StealManager.IsItemAllowedInContext(item, ctx); // 酒馆=食物杂货，大厅=武器防具马匹书籍，暗巷=赃物轻甲，动物一律false

// ④ 场景化锚点 + 文案
Agent a = StealManager.FindChestAnchorAgent();              // 酒馆→Tavernkeeper（优先级有序），大厅/城堡→IsLord，暗巷→GangLeader，村庄→Headman，多级兜底
Vec3 pos = StealManager.ResolveChestSpawnPosition(scene, a); // 锚点正后方 2.0m→1.2m 逐级收缩 + navmesh 验证，兜底 +X 2m
var (hint, title, prefix) = InteractionMissionView.GetChestTexts(ctx);  // (提示语, 标题, 内容前缀) 三元组
// 生成：StealManager.SpawnStorageChestProp(scene, pos, anchor?.Position) — 0.5× 缩放（ChestScale 常量），正面朝向锚点
```


---

## 复合键防重复

同一「定居点+场景」只分配一次，用 `$"{settlement.StringId}|{locationId}"` 复合键替代纯定居点 ID——Town 内部各子场景独立分配（进酒馆分一次、进大厅再分一次），村庄/城堡单场景行为不变。


---

## 复用要点（搬到新系统时）

- **`CampaignMission.Current.Location.StringId` 是第一手信号**，比 Settlement 类型精确——能区分同一城镇的不同室内场景。
- **每个维度一张 switch 表**（权重/过滤/文案/锚点），维度之间不交叉引用。
- **禁用场景三处一致关闭**：权重返回 0 + 过滤返回 false + 文案返回空串（参考 Arena / Dungeon 不刷保管箱）。
- **Unknown 兜底给保守值**：权重 0.20、只放行 Goods+Food，宁可少给不出戏。


---

## 实体名黑名单（扫描场景实体防误伤）

扫描场景实体做「储物道具克隆」时，引擎内部实体会命中名字关键词评分（`__skybox__` 含 "box" → 85 分误伤夺冠，天空盒被克隆成保管箱）。`IsBlacklistedEntityName(name)` 统一拦截：

| 匹配方式 | 名单 |
|---------|------|
| 精确 | `__skybox__` |
| 前缀 | `torch_` `flame_` `light_` `smoke_` `sound_` `fire_` `particle_` `vfx_` |
| 包含 | `_collision_` `_hitbox_` `_water_` `_trigger_` |

任何「遍历 Scene 实体 → 按名字打分选候选」的逻辑都应先过这道黑名单再评分。

**文件位置**：`Stealth/StealManager.cs`（`ChestContext` 枚举 + `GetCurrentChestContext` + `GetChestContextGoldWeight` + `IsItemAllowedInContext` + `FindChestAnchorAgent` + `ResolveChestSpawnPosition` + `IsBlacklistedEntityName`）、`Interaction/InteractionMissionView.cs`（`GetChestTexts` + SpawnChest/开箱 Inquiry 消费点）。


---

## 附：场景锁簧片数表

`StealManager.GetLockpickPinCount(ctx)` —— 撬锁难度的场景分发（沿用本模式）：村庄 2 / 城镇中心·酒馆·暗巷 3 / 城堡·领主大厅 4 / 兜底 2。锁难度 = 世界规则，与场景枚举同住 StealManager，不放 View。

---

# 时机判定条小游戏引擎（StealBar）— `Stealth/StealBarVM.cs` + `GUI/Prefabs/StealBar.xml`

**解决什么问题**：「光标-目标区」时机判定小游戏（大侠立志传式偷窃/撬锁/抓动物）。一个 VM **三模式**（枚举切换），纯动画状态在 C# 侧由 View 每帧驱动，空格/按钮共用同一出手方法。任何新的「抓时机」玩法（钓鱼、打铁、拆解、追踪）都可复用这套骨架。


---

## 结构（开/关/tick 三段式）

```csharp
// VM（继承 ViewModel；运动状态纯 C# 非绑定，绑定只同步渲染值）
var vm = new StealBarVM(StealBarMode.Pickpocket, targetAgent, closeAction);
var vm = new StealBarVM(StealBarMode.Lockpick, pinCount, title, closeAction);
var vm = new StealBarVM(StealBarMode.Animal, animalAgent, animalName, isLarge, closeAction); // 抓动物：大动物判定区右扣 40%，一次出手定胜负
vm.UpdateFrame(dt);          // ← MissionView.OnMissionTick 每帧驱动；dt 为 Scene 缩放时间（子弹时间自洽）
vm.ExecuteAttempt();         // 空格/按钮共用的出手（命中判定），Command.Click 可直接绑
vm.CloseReason               // StealBarCloseReason 枚举 — VM 不直接关 UI，View 轮询消费统一收口

// View（InteractionMissionView 为范本）
_stealLayer = V.NewLayer(201);
V.LoadMov(_stealLayer, "StealBar", vm);
_stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse); // 鼠标留给按钮
// tick 内：vm.UpdateFrame(dt) → Input.IsKeyPressed(Space/Tab) → vm.CloseReason 消费 → 关层
```

**CloseReason 轮询收口**：VM 想关 UI（目标走开/警觉拉满/完成）只置 `CloseReason`，不碰 Layer；View 每帧读它统一走 `CloseStealInterface`——所有关闭路径（收手 Tab/强制/完成）一个收口函数，配套资源（子弹时间/输入冻结/IsUIOpen）不可能漏回收。**收手键 = Tab**（ESC 与游戏菜单冲突，已让出）。⚠️ Tab 原版占用：`Mission.Tick` 中 `IsFriendlyMission`（城镇/村庄漫游）下长按 Tab 0.6s 离开场景（`IsGameKeyDown(4)` 按住状态计时，松开清零；敌人 5m 内拦截）——我们用按下沿 `IsKeyPressed`，轻点收手不触发离场，长按则先收手再离场，语义自洽无需屏蔽。

**CloseReason 全量**：`TargetGone`（目标走开/死亡）/ `Alarmed`（警觉拉满或质问锁占用）/ `Completed`（撬锁完成→接开箱 Inquiry）/ `NothingLeft`（摸空→自动收口+提示）/ `AnimalCaught`（抓动物命中→View 接 `CompleteAnimalSteal`，**关层前先抢出 `_stealAnimalTarget`**，收口会清空它）/ `AnimalFled`（手滑，VM 内已惊叫逃跑，View 只收口）。


---

## 扒窃盲盒：钱袋独立目标 + 摸空自动收口

**金钱 = 特殊物品，独立偷窃（铁律 4），禁止「偷装备顺手白摸钱」。**

```csharp
// StealManager
StealManager.HasPurseGold(agent);       // 身上分配金 > 0？
StealManager.StealPurseGold(agent);     // → int 实偷面额；整袋端走（分配金全拿）。族长 Hero.Gold 摸不到（=全族资金，只能战场击败部队获得）
StealManager.HasAnythingToSteal(agent); // 任一装备槽或钱袋 → 开条前预检

// StealBarVM.NextPickpocketRound（盲盒回合）：
//   装备槽随机抽 → 身上有钱时 PurseChance(0.35) 覆盖为「钱袋回合」(_pendingIsPurse)
//   装备摸空但有钱 → 必摸钱袋；全空 → CloseReason = NothingLeft
//   钱袋预览「摸到一个沉甸甸的钱袋。」，难度定价 1.0（标准）
```

- **开条前**：`TryStealFromAgent` 先查 `HasAnythingToSteal`，没东西 → DisplayMessage 直接不开条。
- **偷到一半摸空**：`NextPickpocketRound` 置 `NothingLeft` → View 自动关条 + 「他身上已经没什么可偷的了。」


---

## Animal 模式（抓动物）与「动物无 Brain」边界

- 难度表达：动物**无 AgentBrain**（`AgentAIController` 只给 `IsHuman` 注册脑）→ 无警戒值 → 判定区不扣警戒、不游动，难度纯由体型定价（`_itemTierFactor`：大动物 0.6 / 小动物 1.0）；Roguery 减速浮标照常生效。
- **动物行为不走 Brain 事件体系**：惊叫/逃跑由 `StealManager.OnAnimalStruggleFlee` 一次性处理（目击者警戒脉冲 + WitnessCrime 广播 + `ScriptedMoveToPoint` 脚本移动逃跑 8~14m），不入队 IAtomicAction。
- `PollForceClose` 对 Animal 生效（溜走 4.5m/死亡 → TargetGone），`GetBrainForAgent` 返回 null 天然跳过警觉检查。


---

## 减法五色条（信号贡献可视化）

基础宽**左端**扣警戒、**右端**扣物品，剩余 = 有效判定区，下限 = 完美区宽（钳满 = 全或无，每次命中即完美）。每层一个 Widget 绑 `float MarginLeft/SuggestedWidth`。**二元色相分离**：可偷=琥珀黄 `#D4AF37`、完美=绿芯 `#3DA53D`（安全暖色族）；不可偷=红族（界外=最深黑红 `#2E1010` / 潜在区黑红亮一档 `#4A1C1C`——红族底色必须全不透明且明度拉开，半透明或太暗会被面板底色吃成纯黑 / 警戒血红 `#A81F1F` / 物品橙红 `#B5502A`——橙红与黄区相邻必须偏红偏暗防混淆）。结果闪烁 = 所中区域变亮：成功亮金 `#FFE97F` / **完美白闪 `#FFFFFF`**（不用绿闪——会把黄区染成"全是绿芯"摧毁语义）/ 失败红 `#DD4444`。⚠️ 闪烁计时走缩放 dt：`ResultFlashSeconds = 0.1` 缩放秒在 0.35× 慢动作下 ≈0.3 真实秒（体感一闪而过）——**此类计时常量必须按"真实时长 = 常量/0.35"换算**，且闪烁期间新一回合已开始，颜色不能伪装成任何区域语义色。成因区分降为红族内明度差，解释交给动态文本行。条下两行说明：规则行①固定（`RuleText`，构造时按模式设）、规则行②动态（`CursorZoneText/Color`，`UpdateCursorZoneHint` 每帧按浮标位置判定完美/有效/警戒扣/物品扣/界外，文本+颜色跟随）。
**铁律**：宽度域每个色块成因必须唯一可读——技能等加成**禁止混进宽度**（技能走浮标速度通道：`260 ×(1−Roguery/300×25%)`）。结果文本不占控件，走 `InformationManager.DisplayMessage`，条上只留颜色闪烁做即时反馈。
**文本变色通道**：TextWidget 无 `TextColor` 属性（写了静默无效）；动态文本色走 `Brush.FontColor="@ColorProp"`（原版 MPMissionMarkerFlag 有绑定先例）。


---

## 双动体 + 2.5× 铁律

| | 浮标（玩家的手） | 子横条（猎物心神） |
|---|---|---|
| 运动 | 线性 ping-pong（匀速撞墙折返） | 正弦游弋（缓入缓出，仅 Cautious≥1.0 起） |
| 速度 | 260px/s ×技能减速（撬锁 pin ×1.15ⁿ） | 55→100px/s 随警戒，且 **≤浮标/2.5 动态封顶** |

**铁律**：浮标速度 ≥ 游动 2.5 倍——同速双动体追踪超出人类反应，退化成运气。由**游动侧动态封顶**实现（`DriftSpeed ≤ CursorSpeed/2.5`），技能减速浮标后比例自动成立。


---

## 子弹时间 — `Mission.AddTimeSpeedRequest`（含坑）

```csharp
Mission.Current.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(0.35f, requestId));  // 请求队列，取最小值
// ⚠️ 移除必须先查！RemoveTimeSpeedRequest 对未知 ID 直接 RemoveAt(-1) 抛 ArgumentOutOfRangeException：
if (mission.GetRequestedTimeSpeed(requestId, out _))
    mission.RemoveTimeSpeedRequest(requestId);
```

- dt 缩放链路（反编译确认）：`Scene.TimeSpeed` → `OnTick(dt=缩放, realDt=真实)` → `OnMissionTick(dt)`——VM 动画/警戒累积/节流计时全走缩放 dt，子弹时间下世界与 UI 同步变慢，难度不被白嫖。
- 配 `_stealSlowmoActive` 幂等标志；关闭每条路径 + `OnMissionScreenFinalize` 兜底回收。


---

## 玩家输入冻结 — `V.SetPlayerControlFrozen(agent, bool)`

模态小游戏 UI 打开时，键盘拦不住（ScreenManager 键盘路径只看 `FocusedLayer`，层 mask 无效），剥 `EventControlFlags` 也无效（原生控制器在托管 tick 之后才写标志）。**正解：切控制器**（详见 [pitfalls.md](pitfalls.md)「模态 UI 键盘输入拦不住」）。

```csharp
V.SetPlayerControlFrozen(Agent.Main, true);   // 开 UI：v1.2.12 切 ControllerType.AI → 主角待机
V.SetPlayerControlFrozen(Agent.Main, false);  // 关 UI：切回 Player（自动重指 MainAgent + 广播）
// Latest：ControllerType→AgentControllerType 改名，setter 仍可用。配 _playerControlFrozen 幂等标志。
```

安全性：`AgentBrain.Tick` 对 `Owner == Agent.Main` 早退——本 mod brain 不会接管主角；SandBox 官方有同款切 AI 用法。

**文件位置**：`Stealth/StealBarVM.cs`（引擎 + `StealPinVM`）、`GUI/Prefabs/StealBar.xml`（五层条 + 双按钮行）、`Interaction/InteractionMissionView.cs`（`TickStealBar`/`StartStealSlowmo`/`FreezePlayerControl` 为接线范本）、`Core/VersionCompat.cs`（`V.SetPlayerControlFrozen`）。

---

# 统一输入映射层 — `Input/ModInput.cs`

**解决什么问题**：① 业务代码裸写 `InputKey.F` 导致手柄完全无法游玩；② 按键提示（InteractArea 圆徽、偷窃条按钮文本）硬编码键盘字串，手柄玩家看到错误提示；③ 改键要满世界找 `IsKeyPressed`。UE4 风格 Action Mapping：**业务层只认语义动作，不认物理键**。

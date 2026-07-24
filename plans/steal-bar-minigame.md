# 进度条偷窃小游戏（大侠立志传式）+ 保管箱撬锁 + 犯罪统一接线

> 2026-07-23。废弃旧 StealVM 按钮式偷窃 UI，替换为「光标-子横条」时机判定小游戏；偷人、偷保管箱（撬锁）共用同一引擎；同时把保管箱偷窃接进 StealManager 统一犯罪逻辑（目击→证词→围堵→campaign 追责）。

---

## 一、需求拆解

### 1. 偷人（Pickpocket 模式）
- 横条（父条）内有一个子横条（目标区），浮标在父条内左右往复移动
- 按空格：浮标命中子横条 → 偷窃成功（一次一件）；未命中 → 失败
- 被偷者警戒值 ↑ → 子横条宽度 ↓；每次出手（无论成败）都提升对方警戒值 → 越偷越难
- UI 打开期间 = `StealManager.IsUIOpen = true`（现有语义：目击者警戒累积）
- 按下空格瞬间才真正执行偷窃（不是预先选好）
- 对方警戒到 **Cautious** → 子横条开始左右游动
- 对方身上多件物品 → 多次偷窃直到掏空
- **子弹时间**：开 UI 期间 Mission 时间减速（0.35×），给玩家反应窗口；关 UI 恢复
- **目标走开 → 强制收手**：目标离开互动范围/死亡 → UI 强制关闭，提示偷窃失败，**不算被发现**（无脉冲无广播）
- **目标中途转身**：走现有警戒认知——受害者自己能看见玩家 + IsUIOpen → 警戒值实时累积 → 子横条当场变窄（无需新代码，`UpdateAlertCognition` 对所有 brain 生效，受害者不豁免）

### 2. 保管箱（Lockpick 模式）
- 同一引擎，但子横条 = 锁簧（pin），**多个 pin 依次开**，全部命中才解锁
- 先撬锁成功 → 才进入现有开箱 Inquiry（全部拿走/自己挑选）
- 撬锁失败的噪音 → 附近能看见玩家的 NPC 警戒脉冲

### 3. 保管箱犯罪统一接线（此前已确认「抓现行围堵」强度）
- 开箱 loot 走 `RecordAnimalTheft` 同款：目击检测 → `RegisterTheftWitnesses` → TheftLedger(worldEventId) → WitnessCrime 广播（victim=null）→ 目击者围堵质问

---

## 二、现状与差距（已核实）

| 环节 | 现状 | 差距 |
|------|------|------|
| 偷人 UI | `StealVM` + `Steal.xml`：摸索/拿走按钮 + 风险掷骰 | 整个废弃，换新引擎 |
| 保管箱目击 | `OpenChest` 里 `GetWitnesses` 结果只拼了一行提示文字 | 证词从未注册 |
| 保管箱账本 | `LootChestItem`/`DeductSettlementItemsOnly` 记 TheftLedger 但无 worldEventId；`LootStash` 什么都不记 | 断链 |
| Mission 即时反应 | 保管箱：零（无广播、无脉冲、IsUIOpen 不设） | 全缺 |
| `WitnessCrime` victim=null | 控制层/舞台分配已判空；**唯一炸点** `AgentBrain.cs:399` `GetBrainForAgent(victim)` 对 null 取 `.Index` → NRE 被静默吞掉 | 一行 null guard |

**可复用轮子（已核实，零新造）：**
- `AgentBrain.AlertValue / .AlertPhase / .AddAlert(PlayerActionType, float)`（wheels.md §NPC 警戒值系统）；AlarmPhase 阈值 Suspicious 0.25 / Cautious 1.0 / Alarmed 2.0
- `AgentAIController.GetBrainForAgent(agent)`（null-unsafe，调用方判空）
- `StealManager.GetRandomStealableItemIndex` / `StealSpecificItem`（单件偷窃全流程：转移+账本+视觉刷新+顺手牵羊）
- `StealManager.GetWitnesses` / `NpcSightSystem.GetObserversOf`（FOV+RayCast）
- `AgentAIController.RegisterTheftWitnesses` / `BroadcastEventInRange("WitnessCrime", …)`（victim 可 null，API 注释明示）
- `AgentHudVM.UpdateFrame(dt)` 模式：VM 由 MissionView tick 驱动做逐帧动画
- Gauntlet 绑定：`SuggestedWidth/MarginLeft` 等 float 属性可绑 VM 动态值（旧 Steal.xml 的 SuspicionHeight 已验证）
- **`Mission.AddTimeSpeedRequest` / `RemoveTimeSpeedRequest`**（已反编译核实，v1.2.12 + v1.4.6 签名一致，无需 V 封装）：请求队列式 Scene 时间减速，多请求取最小值、按 requestID 移除——击杀镜头同款机制，只影响 Mission 内时间流速，不碰 Campaign

---

## 三、架构设计

### 3.1 新引擎：StealBar（一个 VM + 一个 XML，双模式）

```
┌─────────────────────────────────────────────────────────┐
│  标题行：正在偷:张三 ／ 正在撬锁:村庄保管箱                  │
│  ┌───────────────────────────────────────────────┐      │
│  │██████████░░░░░░░██████████████████████████████│ ← 父条│
│  │           ↑ 子横条(目标区，可游动)   ║ ← 浮标    │      │
│  └───────────────────────────────────────────────┘      │
│  ●●○  (仅撬锁：pin 进度点)                                │
│  [[空格] 出手]  [[ESC] 收手]   ← 双按钮，对标 Inquiry       │
└─────────────────────────────────────────────────────────┘
  结果文本（得手了:铁剑／手滑了！）走 DisplayMessage 信息流，
  条上即时反馈由子横条颜色闪烁承担（成功绿/金区青/失败红 1.2s）。
```

**新文件 `Stealth/StealBarVM.cs`**（继承 ViewModel）：
- 模式枚举 `StealBarMode { Pickpocket, Lockpick }`
- 运动状态（纯 C#，非绑定）：`_cursorPos`(0~1)、`_cursorDir`、`CursorSpeed`(px/s 等效)、`_zoneCenter`、`_zoneHalfWidth`、pin 索引、`ZoneDriftAmplitude/Frequency`（Cautious 游动）
- 出手目标状态（Pickpocket）：`_pendingSlot`（本回合预摸的槽位）+ 预览文本
- `UpdateFrame(float dt)` — 由 InteractionMissionView.OnMissionTick 每帧驱动：浮标 ping-pong、子横条游动（警戒≥Cautious 时）、结果闪现计时、关闭条件轮询（目标走开/死亡/Alarmed/质问锁）
- `ExecuteAttempt()` — 空格/出手按钮回调：命中判定（|cursorPos − zoneCenter| ≤ zoneHalfWidth；金区再判 |Δ| ≤ PerfectHalfWidth）→ 走模式分支
- 绑定属性（XML 同步，铁律：加属性必改 XML）：
  - `CursorMarginLeft`（float px）— 浮标位置
  - `BaseMarginLeft` / `BaseWidth`（float px）— 基础底色区（潜在判定区）
  - `AlertLossWidth`（float px）— 左扣警戒损失（与基础区同起点）
  - `ItemLossMarginLeft` / `ItemLossWidth`（float px）— 右扣物品损失
  - `ZoneMarginLeft` / `ZoneWidth`（float px）— 有效判定区位置/宽度
  - `PerfectMarginLeft`（float px）— 完美区位置（居中于有效区，宽固定 12px）
  - `ZoneColor`（string）— 有效区：成功绿闪/失败红闪/常态金
  - `TitleText` / `AttemptButtonText` / `PreviewText`（预摸预览：类型+重量档，不给确切名字）
  - 结果文本不占用控件——走 `InformationManager.DisplayMessage`（条上颜色闪烁保留作即时反馈）
  - `Pins` — `MBBindingList<StealPinVM>`（撬锁进度点：Locked/Unlocked 两态）
  - `IsLockpickMode` / `IsPickpocketMode`（bool）— 行显隐
- 构造参数：Pickpocket → `Agent target`；Lockpick → `int pinCount`（由 ChestContext 表决定）+ 完成回调
- 宽度模型（**减法可视化**，常量集中在 VM 顶部）：
  - `BaseZoneWidth = 110px`（偷人）/ `80px`（撬锁），撬锁再 ×0.85ⁿ
  - 基础宽**左端**扣警戒损失：`base × 0.75 × clamp(alert/2.2)`（红褐块）
  - 基础宽**右端**扣物品损失：`base × max(0, 1 − ItemTierFactor)`（蓝灰块；撬锁无此项）
  - **有效判定区 = 基础 − 两扣，下限 = 完美区宽 12px**——钳满时每次命中即完美（极限难度 = 全或无）
  - 五色：基础暗灰 / 警戒红褐（人） / 物品蓝灰（物） / 有效金（+结果闪烁） / 完美亮金——贡献量直接可见
  - `ItemTierFactor` = `WeightFactor × ValueFactor`（预摸物品的双维定价，见 §3.2b）
  - `CursorSpeed = 260 px/s` 基准 ×(1 − clamp(Roguery/300)×25%)（**流氓技能走「手」通道减速浮标**，满技能 260→195——不污染宽度域的五色贡献）；恒定不挂钩警戒，保住肌肉记忆；撬锁 pin 递进 ×1.15ⁿ
  - `ZoneDriftSpeed = 55 + (alert − 1.0) × 45 px/s`（仅 alert ≥ 1.0 启用），上限 min(100, 浮标速度/2.5)——**铁律由游动动态封顶保证，技能减速后 2.5 倍比仍成立**
- Cautious 游动：`_zoneCenter = baseCenter + sin(t × 1.6) × 0.06`（0~1 坐标；正弦缓入缓出，与浮标线性格挡形态区分）

**新文件 `GUI/Prefabs/StealBar.xml`**：
- 半透黑底 + 居中面板（风格对齐旧 Steal.xml 的 BlankWhiteSquare_9 色系）
- 父条 Widget（Fixed 640×26）→ 子横条 Widget（`SuggestedWidth="@ZoneWidth"` `MarginLeft="@ZoneMarginLeft"` `Color="@ZoneColor"`）→ 金区 Widget（12px 亮金，`MarginLeft="@PerfectMarginLeft"`）→ 浮标 Widget（6px 宽，`MarginLeft="@CursorMarginLeft"`，亮白+金边）
- 预摸预览行（Pickpocket）：`Text="@PreviewText"`
- pin 进度行（Lockpick）：ListPanel + ItemTemplate 绑 `MBBindingList<StealPinVM>`
- 文本：TitleText / PreviewText（提示并入按钮文本，结果走 DisplayMessage）
- 双按钮（对标 Inquiry 确定/取消）：`[空格] 出手` → `ExecuteAttempt`（点击与空格等效）；`[ESC] 收手` → `ExecuteLeave`（点击与 ESC 等效）

**删除**：`Stealth/StealVM.cs`、`GUI/Prefabs/Steal.xml`（确认无其他引用后删；MyCommands 若有引用一并清理）。

### 3.2 偷人接入（InteractionMissionView 改造）

`OpenStealInterface(target)`：
1. `new StealBarVM(StealBarMode.Pickpocket, target, closeAction)` + `V.NewLayer(201)` + `V.LoadMov(layer, "StealBar", vm)`
2. `StealManager.IsUIOpen = true`（同旧）；`IsHandlingInteraction = true`
3. **子弹时间开启**：`Mission.Current.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(0.35f, StealSlowmoRequestId))`，`StealSlowmoRequestId` 取唯一常量（如 731007）。世界慢下来，受害者漂移/转身减速，玩家获得操作窗口
4. 关闭（任何路径：收手/ESC/强制/质问接管/MissionFinalize）必须 `RemoveTimeSpeedRequest(StealSlowmoRequestId)` —— 幂等安全（未知 ID 为 no-op），`OnMissionScreenFinalize` 兜底再调一次防泄漏

`OnMissionTick` 新增（bar 打开期间）：
- `vm.UpdateFrame(dt)`（浮标随 Scene 时间流速走——子弹时间下浮标同样变慢，手感一致、相对难度不被白嫖）
- `Input.IsKeyPressed(InputKey.Space)` → `vm.ExecuteAttempt()`
- `Input.IsKeyPressed(InputKey.Escape)` → 关闭

**输入隔离**（空格误跳修复，已实机验证）：键盘事件路由给 `FocusedLayer`（MissionScreen），Gauntlet 层 mask **拦不住键盘**（ScreenManager 键盘路径只看 FocusTest 不看 mask）；每帧剥 `Agent.Main.EventControlFlags` 的方案实机验证**无效**（原生控制器在托管 tick 之后才写标志）→ 最终方案：**冻结玩家控制器** `Agent.Main.Controller = ControllerType.AI`（v1.2.12 官方 API，输入处理权移交 AI 组件，主角无 AI 指令 = 原地待机；`ControllerType.None` 实测无效——MainAgent 疑被原生特判仍处理输入）。安全性：SandBox 官方有同款切 AI 用法；`AgentBrain.Tick` 对 `Agent.Main` 早退，本 mod brain 不接管。恢复 `Player` 时 `Mission.MainAgent` 自动重指 + 广播 `OnAgentControllerSetToPlayer`，可逆。封装 `V.SetPlayerControlFrozen(agent, bool)`，开/关/Finalize 三处接线（幂等标志 `_playerControlFrozen`）；层 mask 顺带收紧为 `InputUsageMask.Mouse`。**Latest 待解**：`ControllerType` 枚举已被官方删除，冻结 API 待查（VersionCompat TODO），Latest 侧空格误跳暂存。

`ExecuteAttempt()`（Pickpocket 分支）：
- **命中金区**（子横条中心 12px 金色小区）：`StealSpecificItem` 正常执行，**受害者警戒零脉冲**（完美窃取）→ 青闪 + DisplayMessage `神不知鬼不觉:{itemName}`
- **命中普通区**：`StealSpecificItem`（现有全流程）→ 绿闪 + DisplayMessage `得手了:{itemName}` → 受害者 `brain?.AddAlert(Steal, +0.35)`
- **未命中**：红闪 + DisplayMessage `手滑了！` → 受害者 `brain?.AddAlert(Steal, +1.0)`
- 每次 attempt 后重算子横条宽度/速度（读 `GetBrainForAgent(target)?.AlertValue ?? 0`）

**强制关闭条件**（UpdateFrame 轮询）：
- 目标不活跃 / 距离 > 4.5m → 关 + `DisplayMessage("对方走开了，偷窃失败。", 灰)` —— **无脉冲无广播，不算被发现**
- 目标 brain `AlertPhase >= Alarmed` 或 `AgentBrain.ConfrontingBrain != null` → 关（现有质问机器接管：RegisterWitness → L3 对话）
- ESC / 收手按钮 → 静默关闭（无失败提示）

**受害者转身**（设计内行为，零新代码）：受害者自己也是 brain，转身看见玩家 + IsUIOpen → `UpdateAlertCognition` 按 dt×0.30×距离倍率 累积 → 子横条**实时**变窄，玩家肉眼可见"他起疑了" → 决策：收手还是赌一把

### 3.2b 乐趣设计：排序、预览与金区（骑砍2 UI 发挥空间内）

**Q: 偷的内容靠什么排序体验更好？A: 不排序——随机发牌 + 摸牌预览 + 不可逆决策。**

- **每回合预摸一件**（`GetRandomStealableItemIndex` 现成），UI 显示**盲摸预览**：类型 + 重量档（"摸到一件沉甸甸的金属物件（像是武器）"），**不给确切名字**——保留盲盒心跳
- 预览驱动风险定价：子横条宽度 = 警戒公式 **× ItemTierFactor**（轻小 1.10 / 中等 0.90 / 笨重 0.60）——玩家从条宽就能读出"这家伙值钱/难搞"，**见好就收 vs 再贪一手**的决策天然浮现
- 没有"换一个"：摸到什么就是什么，出手或收手，决策不可逆才有张力（弃牌重来会把警戒系统变成免费抽奖）
- 成功后才揭晓确切名字（`得手了:钢制长剑`）——结果反馈即奖励

**金区（完美窃取）**：子横条中心 12px 金色小区。命中 = 正常得物 + **受害者零警戒脉冲**（目击者 IsUIOpen 累积不受影响）。高手可以靠精准操作"白偷"，技术上限；普通人每次出手 +0.35 警戒，四五次后条已窄如针——自然形成"该走了"的节奏感。

**警戒值的信号通道分配**（已确认，减法可视化后）：**左扣红褐块主通道**——警戒 ↑ → 左端红块变大、有效区被顶窄右移（出手前可读的损失量）；**游动辅通道**——Cautious 起整个组合条左右漂移（时机挑战叠加）；**浮标速度恒定 260px/s**——不挂钩警戒，保住肌肉记忆。难度曲线 = 精度轴（有效区收窄）→ 时机轴（窗口游动）两阶段递进。

**物品价值的通道归属**（已确认）：重量与价值**都只影响宽度，不影响任何速度**。重量 = 物理难度（板甲难顺手）；价值 = 贪欲旋钮（fiction：贵重物主人更上心、贴身勤检查）——盲摸预览"小巧精致的物件"配右侧大额蓝灰扣块 = "这玩意值钱"的无声报价，贪/收张力拉满。
`WeightFactor`：轻小(<2kg) 1.10 / 中等 0.90 / 笨重(>8kg) 0.60；`ValueFactor`：便宜(<50金) 1.10 / 一般 0.90 / 贵重(>500金) 0.65。

**双动体异质化设计**（浮标 vs 子横条是两个概念，已确认）：

| | 浮标（玩家的手） | 子横条（猎物的心神） |
|---|---|---|
| 运动形态 | 线性 ping-pong（匀速撞墙折返） | 正弦游弋（缓入缓出，端点减速=可抓节奏窗口） |
| 速度 | 快：260px/s ×(1−Roguery/300×25%)（撬锁 pin n ×1.15ⁿ） | 慢：55 → 100px/s（随警戒 1.0→2.0，且 ≤浮标/2.5 动态封顶） |
| 何时动 | 永远 | 仅 Cautious(≥1.0) 起 |
| 幅度 | 全条幅 | ±6% 条宽（≈±38px） |

铁律：浮标速度 ≥ 子横条 2.5 倍——同速双动体追踪超出人类反应，游戏退化成运气。**由游动侧动态封顶实现**（`DriftSpeed ≤ CursorSpeed/2.5`），技能减速浮标后此律自动成立。

**撬锁 pin 递增难度**（Lockpick 专属手感）：第 n 个 pin 宽度 ×0.85ⁿ、浮标速度 ×1.15ⁿ——一把锁内部就有"快开了但越来越悬"的 escalation 曲线。

**为什么不加更多**（连击加成/蓄力/音效反馈等）：骑砍2 Gauntlet 可发挥空间有限，先把「条宽=风险可视化 + 浮标=操作空间」这一对核心做扎实；连击类留给实机手感验证后的二期。

### 3.3 保管箱撬锁接入

`OpenChest()` 改造（保持空箱早退）：
1. F → **先开 StealBar 撬锁模式**：pin 数按 ChestContext 表（沿用场景感知分发模式——每维度一张 switch）：
   `Village 2 / TownCenter 3 / TownTavern 3 / Alley 3 / Castle 4 / LordsHall 4`
2. **子弹时间同样开启**（同一个 requestId）——箱子不动，但围观者会走动、转头，慢动作给玩家同样的操作窗口
3. 难度固定：锁的难度只由锁本身决定（pin 数 ×0.85ⁿ 宽度衰减 ×1.15ⁿ 浮标加速），**不读目击者警戒**——目击压力由 NPC 自身警戒系统承担（IsUIOpen 累积 → Alarmed → 质问机器强制收手），后果链已完整，无需额外通道
4. **命中** → 当前 pin 解锁（进度点变亮），推进下一 pin；**未命中** → 红闪 + 噪音脉冲：对当前能看见玩家的观察者 brain 各 `AddAlert(Steal, +0.5)`（0.5s 节流防连按刷爆）
5. pin 递增难度：第 n 个 pin 宽度 ×0.85ⁿ、浮标速度 ×1.15ⁿ（见 §3.2b）
6. 全部 pin 开 → 关 bar（收子弹时间）→ 进入**现有**开箱 Inquiry（全部拿走/自己挑选，UI 不变）
7. `IsUIOpen = true` 贯穿「撬锁 bar + Inquiry + 战利品界面」全程，loot 收尾才复位

### 3.4 保管箱犯罪统一接线（StealManager）

新增 `StealManager.RecordChestTheft(Settlement settlement, List<(string itemId, string itemName, int count)> items, int gold)`（照 `RecordAnimalTheft` 模板）：
1. `Settings.Instance.WitnessSystemEnabled` 闸 → `GetWitnesses(Agent.Main, null, 15f)`
2. 有目击者 → `RegisterTheftWitnesses(heroIds, templateWitness, itemId/itemName 循环, targetName: "保管箱")`
3. 有目击者 → `BroadcastEventInRange(Agent.Main.Position, 25f, "WitnessCrime", exclude: null, requireSight: true, Agent.Main, null)` —— victim=null 抓现行围堵（**依赖 AgentBrain.cs:399 的 null guard**，见下）
4. 无目击者 → 仅日志（偷干净了，没人知道）

修改现有三处：
- `LootChestItem` / `DeductSettlementItemsOnly` 的 `TheftLedger.Record` 补 `worldEventId: AgentAIController.Instance?.PendingWorldEvent?.EventId`
- `LootStash` 不记 TheftLedger（金钱非物品、无法标赃，与扒窃金钱路径一致）——金的失窃由证词 ActionRecord 承载
- `OpenChest` 两条 loot 路径收集 taken 清单：
  - 全部拿走 → 循环结束一次 `RecordChestTheft(settlement, taken, takenGold)`
  - 自己挑选 → gold 先入 `_pendingChestGold`，`ProcessPendingChestLoot` 里 items + gold 一次记录；纯金无物品分支立即记录

修改 `AgentBrain.cs:399`：
```csharp
var victimBrain = victim != null ? AgentAIController.GetBrainForAgent(victim) : null;
```

---

## 四、文件变更清单

| 文件 | 动作 | 内容 |
|------|------|------|
| `ExampleModVS/ExampleMod/ExampleMod/Stealth/StealBarVM.cs` | 新增 | 双模式小游戏 VM + `StealPinVM` |
| `GUI/Prefabs/StealBar.xml` | 新增 | 父条/子横条/浮标/pin 行/文本/收手钮 |
| `ExampleModVS/ExampleMod/ExampleMod/Stealth/StealVM.cs` | 删除 | 旧按钮式 UI（废弃） |
| `GUI/Prefabs/Steal.xml` | 删除 | 旧 UI 布局 |
| `Interaction/InteractionMissionView.cs` | 改 | OpenStealInterface→新 VM；tick 驱动 UpdateFrame + 空格/ESC 轮询；**子弹时间 AddTimeSpeedRequest/RemoveTimeSpeedRequest（含 Finalize 兜底）**；OpenChest 前置撬锁；两条 loot 路径接 RecordChestTheft + `_pendingChestGold`；CloseStealInterface/OnMissionEnd 复位 IsUIOpen |
| `Stealth/StealManager.cs` | 改 | 新增 `RecordChestTheft`；两处 TheftLedger.Record 补 worldEventId |
| `AI/AgentBrain.cs` | 改 | 第 399 行 victim null guard（一行） |

**版本兼容**：层创建走 `V.NewLayer`/`V.LoadMov`（已封装）；新 UI 不用 ImageIdentifier（旧 UI 的版本炸点），纯文本+色块，双版本通吃。编译验证 `MB2_V1212` + 默认（Latest）双配置。

**设计哲学四原则对照**：
- ①反馈明确：命中/失误即闪+文字；警戒→条宽变化肉眼可见；目击者头顶警戒眼睛（现有 HUD）同步涨
- ②自由感：可收手随时走；蹲位/遮挡影响目击（现有 GetObserversOf RayCast）；失败不即死
- ③任意 NPC 接得住：偷模板 NPC 走同一入口（StealSpecificItem 本就兼容）；撬锁无受害者 Agent，围堵走 victim=null 路径
- ④信息塑造目标：风险提示（现有"有 N 双眼睛"）从谎言变真实威胁

---

## 五、验证清单

1. **双配置编译**：`MB2_V1212` + Latest 均通过
2. **偷人**：村庄找村长 → F 偷窃 → 条 UI 开（**世界明显变慢 0.35×**，IsUIOpen 生效，旁观者警戒眼涨）→ 空格命中得一件装备+零钱；连偷数次条明显变窄；Cautious 后子横条游动；故意连续失误 → 对方 Alarmed → UI 关闭进质问对话
3. **子弹时间恢复**：收手/ESC/强制关闭/被质问接管 每条路径关闭后，世界时间流速恢复正常（村民走路不再慢放）；直接离开 Mission 也无残留
4. **走开强制收手**：偷到一半目标走远（>4.5m）→ UI 强制关闭 + 提示"对方走开了，偷窃失败" → **无目击注册、无广播、无质问**（不是被发现）
5. **转身起疑**：受害者中途转身面向玩家 → 其警戒值实时上涨、子横条当场变窄（RuntimeLog 可见 StealUIOpen 累积）
6. **金区**：精准命中金区 → 得物但受害者警戒纹丝不动（`custom.alert_status` 验证）
7. **无人目击偷窃**：深夜/遮挡后 → 无质问、RuntimeLog 无 witness 记录
8. **保管箱撬锁**：村庄箱子 → F → 2 pin 撬锁（世界变慢）→ 故意按空 → 附近 NPC 警戒眼跳 → 全开 → Inquiry → 全部拿走 → **村长当面** → 目击证词注册 + WitnessCrime 围堵质问触发
9. **保管箱 campaign 层**：被目击偷箱后离开场景 → RuntimeLog 出现 `FinalizePendingWorldEvent`/证词持久化；再进村与村民对话出现犯罪指控入口；TheftLedger 记录带 worldEventId
10. **边缘**：ESC 随时关闭无泄漏（IsUIOpen 复位 + 子弹时间恢复）；目标中途死亡 UI 自闭；空箱不开撬锁直接提示；OnMissionEnd 强制复位
11. **日志**:`Debug/StoryEngine_RuntimeLog.txt` 搜 `[ChestTheft]`/`[StealBar]` 关键节点齐全（命中/失误/pin 进度/目击名单），无 per-frame 垃圾日志

---

## 六、待确认解释点（实现时按此默认）

- **pin 是依次开还是同时显示多个子横条？** → 按「依次开」实现（单条+当前 pin 游动+进度点），最贴合"撬锁一个个簧片"的幻想；若要同时多区显示后续可调
- **失误不加目击者广播**：扒窃失误只给受害者本人脉冲；围堵质问走既有 Alarmed→L3 机器（不额外广播，避免双重触发）
- 数值全部集中在 StealBarVM 顶部常量区，实机手感后再调

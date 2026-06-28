# 村庄动物偷窃后续反应系统 — 完整设计

> **状态**：设计阶段（对齐中）
> **关联文件**：`plans/rules/wheels.md`（已造轮子）、`plans/rules/narrative-design.md`（叙事铁律）、`Knowledge/原版骑砍2任务系统分析.md`（Issue-Quest 架构）
> **配套系统**：`Stealth/VillageAnimalTracker.cs`（偷窃追踪）、`Quests/Commissions/CommissionHubIssue.cs`（信号 Issue 范本）、`Quests/WorldEvents/HeroNemesisTracker.cs`（宿敌追踪）、`Quests/WorldEvents/WorldEventConfig.cs`（世界事件配置）

---

## 零、设计目标

**玩家偷了村庄动物后，村庄不会"无事发生"。** 村民会经历发现→调查→锁定→报复四个阶段，每个阶段都是一个独立的 **Issue-Quest 对**（对标原版双层架构），玩家**随时可以介入**——无论他自己是不是贼。

核心原则：
1. **每阶段都是可见的 Issue（`!`）+ 可接的 Quest** — 玩家可以观察、接取、或误导
2. **AI 自动推进不依赖玩家** — 玩家不接 Quest，调查也会进行
3. **调查结果有不确定性** — 可能查错人、可能冷案、不是必然指向玩家
4. **叙事遵守铁律** — 通知里不写 "你是贼"，只写 "村民怀疑你"
5. **以 KCD2/RDR2 水准要求** — 开放世界该有的反应都要有

---

## 零-B、偷窃当场检测：统一目击系统

**偷动物和偷村民装备都属于偷窃行为，在偷窃动作当场就需要检测目击者。**
目击者看到 → 当场锁定嫌犯。没人看到 → 事后才发现东西少了 → 进入猜测流程。

### 统一目击检测（偷窃动作执行时调用）

```csharp
// 偷动物或偷 NPC 装备时，统一走这个入口：
TheftWitnessResult result = TheftWitnessDetection.Check(
    thief: Agent.Main,
    victim: targetAnimalOrNpc,  // 被偷的动物 Agent 或 NPC Agent
    actionPosition: thief.Position,
    maxDistance: 15f
);
```

**检测逻辑**：
1. 遍历 Mission 内所有 Human Agent（`IsHuman && IsActive && !IsPlayer`）
2. 对每个村民做两层检查：
   - **距离检测**：村民到偷窃位置 ≤ 15m
   - **视线检测**：村民是否面朝玩家方向（点积）+ 是否有障碍物遮挡（RayCast）
3. 通过两层检查的 = 目击者

**返回值 `TheftWitnessResult`**：
```csharp
public class TheftWitnessResult
{
    public bool WasWitnessed;              // 是否有人目击
    public int WitnessCount;               // 目击者人数
    public List<Agent> Witnesses;          // 目击者 Agent 列表
    public List<Hero> WitnessHeroes;       // 目击者对应的 Hero（大地图层可用）
    public bool IsDefinitivelyIdentified;  // 是否被确定身份（有人看清了脸）
}
```

### 目击后果分流

```
偷窃动作执行
    │
    ├─ 有人目击 (WasWitnessed = true)
    │   → ThiefHeroId = Hero.MainHero（当场确定）
    │   → 目击者可能当场大喊（触发 Alarm）
    │   → 如果玩家没被当场抓住（跑掉了）→ 直接进阶段2（嫌犯=玩家，跳过调查）
    │   → 如果被当场抓住 → 直接进阶段3（村民当场围攻/报复）
    │
    └─ 没人目击 (WasWitnessed = false)
        → ThiefHeroId = null（未知嫌犯）
        → 玩家安全离开
        → 下次 DailyTick 村民发现东西少了 → 进阶段1（完整调查流程）
```

### 与现有系统的整合

| 偷窃类型 | 调用点 | 现有目击检测 | 改动 |
|---------|--------|------------|------|
| 偷 NPC 装备 | `StealManager.StealSpecificItem` | `StealManager.GetWitnesses`（已有，FOV+RayCast） | 替换为统一 `TheftWitnessDetection.Check` |
| 偷动物 | `InteractionMissionView.TryStealAnimal` | 无（新增） | 偷动物动作前调用 `TheftWitnessDetection.Check` |
| 偷 NPC 金钱 | 未来 | 无 | 同上 |

> **注意**：`StealManager.GetWitnesses` 使用 `NpcSightSystem.GetObserversOf`（FOV + RayCast），但其内部 `ProcessAgentCandidate` 过滤了非人类 Agent。偷动物时 victim 是动物 Agent，但检测的是**村民是否看到玩家**（不是看动物），所以可以复用同一套视线检测——把 `thief` Agent 传入即可。

---

## 零-C、NPC 警戒值系统 + 头顶警戒条

**偷窃过程中，玩家需要实时观察每个 NPC 的警戒程度——谁在看我？谁快要去报警了？**

### 术语约定

| 术语 | 含义 | 英文 |
|------|------|------|
| **警戒值** | NPC 对玩家当前行为的警觉程度 0.0~1.0 | Alertness |
| **嫌犯** | 调查后锁定的偷窃嫌疑人 | Suspect |

> "警戒"是 NPC 对玩家的态度（他有多警觉），"嫌犯"是调查结论（谁被怀疑偷了东西）。两个概念不同。

### 架构三层

```
┌─ 数据层: NpcAlertnessData ──────────────────────────────┐
│  每个 NPC Agent → 警戒值 (0.0 ~ 1.0)                     │
│  因子: IsVisible / IsInPersonalZone / IsPlayerInCrimeAct │
│  累积速度 + 衰减速度                                      │
├─ 逻辑层: NpcAlertnessMissionLogic : MissionLogic ────────┤
│  OnMissionTick:                                          │
│    遍历场景内 Human Agent                                │
│    → CanSeePlayer? (复用 NpcSightSystem)                  │
│    → IsPlayerInPersonalZone? (距离 < 3.5m)                │
│    → IsPlayerInCrimeAct? (偷窃/击晕动画中)                 │
│    更新每个 NPC 的 警戒值                                  │
│    警戒值 = 1.0 → 触发当场目击                            │
├─ 视觉层: NpcAlertnessMissionView : MissionView ──────────┤
│  GauntletLayer + 世界坐标→屏幕投影                        │
│  每个 NPC 头顶渲染一个 FillBar                            │
│  警戒值 0→1 对应空→满                                    │
│  颜色: 绿(低) → 黄(中) → 红(高) → 红闪(满)               │
└──────────────────────────────────────────────────────────┘
```

### 警戒值计算公式

```csharp
// 每帧更新
float alertDelta = 0f;

if (CanSeePlayer(npc))
    alertDelta += 0.15f * dt;          // NPC 能看到玩家 → 涨

if (IsInPersonalZone(npc, 3.5f))
    alertDelta += 0.2f * dt;           // 玩家闯入近身区 → 快涨

if (IsPlayerInCrimeAct)                 // 玩家正在偷窃/击晕/拔刀
    alertDelta += 0.3f * dt;           // 现行犯 → 极速涨

// 注意：多因子叠加 —— 在 NPC 眼皮底下偷东西 = 0.15+0.2+0.3=0.65/dt
//      1.5 秒就能从 0 涨到 1.0

// 衰减
if (!CanSeePlayer && !IsInPersonalZone)
    alertDelta = -0.1f * dt;           // NPC 失去视野 → 慢慢降

npcAlertness[npc] = Clamp(npcAlertness[npc] + alertDelta, 0, 1);
```

### 警戒值阶段与视觉

```
警戒值     颜色      视觉表现              NPC 行为
─────────────────────────────────────────────────
0.0~0.3    (隐藏)    不显示条              没注意到你
0.3~0.55   绿色      25%~50% 填充          NPC 有所察觉，偶尔看一眼
0.55~0.75  黄色      50%~75% 填充          NPC 盯着你，手摸向武器
0.75~0.95  橙色      75%~95% 填充          快要去报警了！
0.95~1.0   红色闪烁  95%~100% 填充         → 触发报警！
  = 1.0    (触发)    NPC 大喊 / 跑去找守卫    → TheftWitnessResult.WasWitnessed
```

### 视觉实现

```
         ┌────────────────┐
         │ ■■■■■■░░░░░░░ │ ← 头顶警戒条（世界坐标→屏幕投影）
         │     ⚔ 守卫     │
         └────────────────┘
```

- 使用 `GauntletLayer` + XML prefab（对标 `NinjaNotificationMissionView` / `CustomNotify.xml`）
- 每帧 `Agent.GetChestGlobalPosition()` → `WorldToScreenPoint` → 更新 Widget 位置
- `FillBar` widget 绑定 `Alertness` 属性（0→1）
- 警戒值 < 0.3 时隐藏（减少视觉噪音）
- 红色闪烁用 `Brush` 动画（`CustomNotify.xml` 已有类似的动画写法可参考）

### 与当场目击的联动

```
NPC 警戒值达到 1.0
    ↓
触发: TheftWitnessDetection.OnNPCMaxAlert(npc)
    ↓
→ WasWitnessed = true
→ 该 NPC 大喊 / AlarmFactor 提升（触发原版 AlarmedBehaviorGroup）
→ ThiefHeroId = Hero.MainHero（当场锁定玩家为贼）
→ 如果玩家跑掉了 → 直接进阶段2（嫌犯=玩家，跳过调查阶段）
→ 如果没跑掉 → 守卫来抓 / 村民围攻
```

### 需要新增的文件

| 文件 | 职责 |
|------|------|
| `AI/NpcAlertnessMissionLogic.cs` | MissionLogic：每帧计算所有 NPC 警戒值 |
| `Notify/NpcAlertnessBarVM.cs` | ViewModel：单个 NPC 的警戒条数据绑定 |
| `Notify/NpcAlertnessMissionView.cs` | MissionView：渲染头顶警戒条（世界→屏幕投影） |
| `GUI/Prefabs/NpcAlertnessBar.xml` | Gauntlet XML prefab：警戒条的视觉样式 |

### 对标已有轮子

- **视线检测** → `NpcSightSystem.GetObserversOf`（已有，FOV+RayCast）
- **MissionView 模式** → `NinjaNotificationMissionView`（GauntletLayer + VM + LoadMovie）
- **Gauntlet XML** → `GUI/Prefabs/CustomNotify.xml`（FillBar 样式 + Brush 动画）
- **世界投影** → `Agent.GetChestGlobalPosition()` + `Camera.WorldPointToScreenPoint()`
- **Alarm 联动** → `Mission.Current.GetMissionBehavior<AlarmedBehaviorGroup>()`（原版引擎）

---

## 一、玩家体验总览：四条典型路径

### 路径 A：做贼心虚，主动误导（Roguery 流）

```
偷羊 → 第二天回村 → 看到 `!` → 找村长接 Quest（调查任务）
→ Roguery 检定：吓唬/收买目击者
→ 在现场"发现"强盗的箭矢（栽赃）
→ 向村长汇报："是附近藏身处的强盗干的！"
→ 嫌犯 = 强盗头子 → 玩家接追捕 Quest → 清藏身处
→ 拿双份报酬（Quest 奖励 + 藏身处战利品）
→ 村长感激不尽，Trust +10
→ 完美犯罪 ✓
```

### 路径 B：被查出来了，想办法摆平（Charm 流）

```
偷羊 → 没管 → AI 调查 → 有人目击了你 → 嫌犯 = 玩家
→ NinjaNotification: "急报——{village}村民认定是你偷的……"
→ 玩家回村找村长 → 村长冷脸："你还敢来？"
→ 对话选项:
  [Charm 50] "你们搞错了" → 40% 成功率（玩家没做过 = 必过检定？有趣）
  [付钱] "这是赔偿够不够？" → 必定成功，但 ×3 动物价值
  [威胁] "你再说一遍？" → 恶名+1，直接进阶段3
→ 摆平了 → Issue 关闭，Trust -5（村民心里还有疙瘩）
→ 下次再偷 → 直接锁定玩家（跳过调查）
```

### 路径 C：嫁祸仇人，一石二鸟（Roguery + BountyHunt 混合）

```
偷羊 → 接调查 Quest → 把从 NPC-X 身上偷来的物品"遗留"在现场
→ 汇报："是 NPC-X 干的！"
→ 嫌犯 = NPC-X → 接追捕 Quest → 活捉 NPC-X 带回
→ NPC-X 入狱 → 玩家拿报酬 + Trust +10
→ NPC-X 出狱 → HeroNemesisTracker 记录 → 以后复仇
→ 同时偷羊完全脱身 ✓
```

### 路径 D：完全不管，后果自己找上门

```
偷羊 → 走人 → AI 调查 → 嫌犯 = 玩家 → 阶段3报复部队 spawn
→ 大地图追猎玩家 → 遭遇战斗！
→ 赢了 → 恶名+2，但下次来的人更多更强
→ 输了 → 被活捉，动物还回去 + 罚金 + 坐牢
→ 最差：不管 → Trust -30 → 该文化圈传开
```

---

## 二、三阶段 Issue-Quest 链

```
┌──────────────────────────────────────────────────────────────────────┐
│ 阶段 1: 发现 — "村里的牲口被偷了！"                                    │
│                                                                       │
│ Issue_1: VillageTheftDiscovery                                        │
│  Owner: 村庄 Headman                                                  │
│  `!` 标记: 蓝色（普通）                                                │
│  Effect: Security -1, 牲畜产量 -10%                                    │
│  持续时间: 1~7 天（调查期间）                                          │
│                                                                       │
│ Quest_1: InvestigateVillageTheft  ← 玩家可接                          │
│  发布者: Headman                                                      │
│  目标: 查清楚是谁偷了牲口                                              │
│  任务链:                                                              │
│    ① 在村里找目击者谈话（可选 Roguery 检定误导目击者）                  │
│    ② 搜索现场找线索（Scout 检定）                                      │
│    ③ 拿着线索向村长汇报 → 给出嫌疑人结论                               │
│                                                                       │
│  玩家介入点:                                                          │
│    • 接任务 → 认真查 → 发现真凶                                        │
│    • 接任务 → 故意指错人（嫁祸 NPC/强盗）                              │
│    • 接任务 → 找不到线索 → 冷案（Trust 小降）                          │
│    • 不接 → AI 自动调查（每日掷骰推进）                                 │
│    • 直接找村长 → 私下赔钱封口（不进 Quest，跳过调查）                  │
│                                                                       │
│  自动调查公式（不接 Quest 时）:                                        │
│    目击修正: witnessBonus = witnessCount × 0.15                        │
│    反侦察: rogueDefense = min(0.5, playerRoguery / 300 × 0.5)         │
│    每日推进: 0.25 + witnessBonus - rogueDefense                         │
│    进度满 1.0 → 调查结束                                               │
│                                                                       │
│    ⚠ 关键: 进度推进后会锁定一个嫌犯，不一定是玩家！                      │
│    锁定逻辑:                                                           │
│      - 玩家 Roguery < 40 且 witnessCount > 0 → 大概率锁定玩家           │
│      - 玩家 Roguery > 150 → 大概率锁定附近强盗/随机 NPC                 │
│      - 玩家接了 Quest 并成功误导 → 100% 锁定玩家指定目标                │
│      - 7 天未锁定 → 冷案                                               │
└──────────────────────────────────────────────────────────────────────┘
    ↓ 调查结束，嫌疑人确定
┌──────────────────────────────────────────────────────────────────────┐
│ 阶段 2: 锁定 — "是 {嫌犯} 干的！"                                      │
│                                                                       │
│ Issue_2: VillageTheftSuspectIdentified                                │
│  Owner: 村庄 Headman                                                  │
│  `!` 标记: 黄色/橙色（更急迫）                                         │
│  Effect: Security -2, Prosperity -1                                    │
│  持续时间: 5~15 天（悬赏有效期间）                                      │
│  特殊: 如果嫌犯=玩家 → 村长对玩家对话 cold                              │
│                                                                       │
│ Quest_2: ApprehendVillageThief  ← 玩家可接（如果嫌犯≠自己）            │
│  发布者: Headman                                                      │
│  目标: 把 {嫌犯} 抓回来（对标 BountyHunt CommissionQuest）             │
│  任务链:                                                              │
│    ① 追踪嫌犯位置（Scout 或问 NPC）                                    │
│    ② 找到嫌犯 → 战斗 → 击败/活捉                                      │
│    ③ 带回村庄交付 → 领报酬                                            │
│                                                                       │
│  ── 核心分支体验 ──                                                    │
│                                                                       │
│  ★ 嫌犯 = 玩家:                                                       │
│    玩家接不了 Quest（不能抓自己）                                       │
│    替代选项（对话中）:                                                  │
│      💰 赔钱消灾: TransferGold(玩家→村长, 动物价值×3)                  │
│         → Issue 关闭, Trust 归零                                       │
│      🗣 Charm 辩护: 检定成功 → 嫌犯改为"待定" → 回到阶段1              │
│         失败 → Trust -10, 直接进阶段3                                  │
│      🤐 威胁: 恶名+1, 直接进阶段3                                      │
│      🏃 直接走人: 不回应 → 进阶段3                                     │
│                                                                       │
│  ★ 嫌犯 = 嫁祸的 NPC（玩家故意指错的）:                                 │
│    玩家可以接 Quest → 抓那个无辜 NPC                                   │
│    抓到后: NPC 入狱, 玩家拿报酬, Trust +5~10                           │
│    深层后果: NPC 出狱 → HeroNemesisTracker 记录冤情                    │
│    "他没有偷！但他知道你陷害了他。"                                      │
│                                                                       │
│  ★ 嫌犯 = 真实强盗/NPC（自然调查结果或玩家指认正确）:                    │
│    玩家可以接 Quest → 正常 BountyHunt                                  │
│    嫌犯 = 强盗头子 → 清了藏身处还能顺带完成                              │
│    Trust +10~15（抓到了真凶！）                                         │
│                                                                       │
│  ★ 嫌犯 = 未确定（冷案从阶段1过渡而来）:                                │
│    Quest 不可接（没有嫌犯可以追）                                       │
│    Issue 挂起, 村庄 Security -1 持续                                    │
│    玩家后续再偷 → 调查速度 ×3（警觉状态）                               │
└──────────────────────────────────────────────────────────────────────┘
    ↓ 嫌犯逍遥法外 —— 进入报复阶段
┌──────────────────────────────────────────────────────────────────────┐
│ 阶段 3: 报复 — "你不给公道，我们自己讨！"                               │
│                                                                       │
│ Issue_3: VillageRetaliation                                           │
│  Owner: 村庄 Headman                                                  │
│  `!` 标记: 红色（危机）                                                │
│  Effect: Security -3, Prosperity -2                                    │
│  持续时间: 15~20 天（报复部队活跃期间）                                 │
│  同时: WorldEvent 自动 spawn 报复部队                                  │
│                                                                       │
│ Quest_3: LeadRetaliationParty  ← 玩家可接（如果嫌犯≠自己）             │
│  发布者: Headman                                                      │
│  目标: 带领村民报复队找到 {嫌犯} → 教训/活捉                           │
│  任务: 玩家指挥报复部队 → 寻找嫌犯 → 战斗                              │
│  其实对标 CommissionQuest 而非新类型                                    │
│                                                                       │
│  同时: WorldEvent 自动 spawn 报复部队（对标 NemesisRevenge 模式）       │
│    部队命名: "{village}的复仇队" / "{嫌犯名}讨伐队"                     │
│    部队规模: 村庄民兵 5~8 人 + 雇佣打手 3~5 人                         │
│    部队 AI: SetPartyAiAction → EngageParty(嫌犯 party)                 │
│    部队持续: 15 天 → 如果没找到目标自动解散                             │
│                                                                       │
│  ── 分支体验 ──                                                        │
│                                                                       │
│  ★ 嫌犯 = 玩家:                                                       │
│    报复部队在大地图追猎玩家                                            │
│    玩家选项:                                                           │
│      💰 回村找村长赔钱: ×5 动物价值 + 罚金 + 安抚费                    │
│         → 报复部队解散, Trust 归零                                      │
│      ⚔ 击败报复部队: 打赢了 → 恶名+2, 宿敌追踪激活                     │
│        下次再偷 → 报复队更强更多                                         │
│      🗣 Charm 说服村长: 成功率更低（愤怒中）→ Trust -15                 │
│      🏃 不理会: 部队追 15 天自散 → Trust -30, 恶名+3, 村庄永久关系恶化  │
│                                                                       │
│  ★ 嫌犯 = NPC（嫁祸或真实）:                                           │
│    玩家可以接 Quest → 带报复部队去打那个 NPC                            │
│    打完 → 村民感激涕零, Trust +10~20                                    │
│    那个 NPC → NemesisRecord（恨死玩家）                                 │
│                                                                       │
│  ★ 嫌犯 = 未知（冷案升级）:                                            │
│    报复无目标 → 村民随机找"附近可疑人物"出气                            │
│    可能打错人 → 连锁事件（冤案引发二次冲突）                            │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 三、数据结构设计

### 3.1 VillageTheftCase（单一案件状态机）

```csharp
[Serializable]
public class VillageTheftCase
{
    // 身份
    public string CaseId;              // $"theft_{settlementId}_{timestamp}"
    public string SettlementId;
    public string HeadmanHeroId;       // 村长（Issue Owner）

    // 偷窃事实（偷窃时写入，不可变）
    public int TotalAnimalsStolen;     // 共偷了几只
    public Dictionary<string, int> StolenByMonster; // "sheep"→2, "cow"→1
    public float TheftDay;             // 偷窃发生的游戏日
    public string ThiefHeroId;         // 实际贼（可 null！系统知道但村民不一定知道）
    public int WitnessCount;           // 目击者数量（来自 StealManager.GetWitnesses）

    // 调查阶段 (阶段1)
    public float InvestigationProgress; // 0.0 → 1.0
    public string SuspectHeroId;       // 当前嫌犯调查进度 0.5 为"嫌疑人"1.0 为"确认"
    public List<string> ClueList;      // 玩家收集的线索（"footprint", "witness_saw_stranger"...）
    public bool IsColdCase;            // 冷案（7天未破）

    // 锁定阶段 (阶段2)
    public string IdentifiedSuspectId; // 锁定嫌犯的 HeroId（null = 未锁定）
    public bool IsSuspectPlayer;       // 嫌犯是否 = 玩家
    public float SuspectIdentifiedDay; // 锁定日期

    // 报复阶段 (阶段3)
    public bool RetaliationSpawned;    // 报复部队已生成
    public string RetaliationPartyId;  // 报复部队的 MobileParty.StringId
    public float RetaliationSpawnDay;
    public bool RetaliationResolved;   // 报复已解决（击败/解散/赔钱了事）

    // 状态
    public TheftCaseStage Stage;       // Discovery / SuspectIdentified / Retaliation / Resolved
    public float LastUpdateDay;

    // 玩家介入
    public bool PlayerTookQuest1;      // 玩家接了调查 Quest
    public bool PlayerTookQuest2;      // 玩家接了追捕 Quest
    public bool PlayerTookQuest3;      // 玩家接了报复 Quest
    public bool PlayerPaidRestitution; // 玩家已赔钱
}

public enum TheftCaseStage
{
    Discovery,          // 阶段1: 发现+调查中
    SuspectIdentified,  // 阶段2: 已锁定嫌犯
    Retaliation,        // 阶段3: 报复部队活跃
    Resolved            // 已解决（赔钱/抓到贼/报复完成/冷案超时）
}
```

### 3.2 持久化

- 多个 `VillageTheftCase` 以 `List<VillageTheftCase>` JSON 序列化
- 存入 `MyBehavior.SyncData`（对标 `HeroNemesisTracker` 的序列化方式）
- 存档字段 ID: 待分配（需要查 `StoryContext.SaveDefiner` 确认下一个可用 ID）

### 3.3 与 VillageAnimalTracker 的关系

`VillageAnimalTracker` 只管 "被偷了多少 → 还剩多少 → 自然恢复" 的**数值层**。

`VillageTheftCase` 负责 "谁偷的 → 村民怎么反应 → 报复链" 的**叙事层**。

`RecordTheft` 触发时，两套数据同时更新：

```csharp
// 现状
VillageAnimalTracker.RecordTheft(settlementId, monsterId, count);

// 新增：同时打开/更新案件
VillageTheftCase case = VillageTheftCase.OpenOrUpdate(
    settlementId, headmanHero, monsterId, count,
    thiefHero: Hero.MainHero,
    witnessCount: StealManager.GetWitnesses(playerAgent, victimAgent).Count
);
```

---

## 四、Issue-Quest 类设计

### 4.1 三个 Issue 类

| 类 | 阶段 | Owner | `!` 颜色 | IssueEffect | `GenerateIssueQuest` |
|---|------|-------|---------|-------------|---------------------|
| `VillageTheftDiscoveryIssue` | 1 | Headman | 蓝 | Security-1 | → `InvestigateVillageTheftQuest` |
| `VillageTheftSuspectIssue` | 2 | Headman | 黄/橙 | Security-2 | → `ApprehendVillageThiefQuest`（仅嫌犯≠玩家时） |
| `VillageTheftRetaliationIssue` | 3 | Headman | 红 | Security-3 | → `LeadRetaliationQuest`（仅嫌犯≠玩家时） |

三个 Issue 都继承 `IssueBase`，对标 `CommissionHubIssue` 的实现方式。

Issue 生命周期管理：`CommissionIssueBehavior` 或新增 `VillageTheftIssueBehavior : CampaignBehaviorBase` 负责：
- `OnCheckForIssue` / `OnSettlementEntered` → 检查村庄是否有活跃案件 → 注册对应 Issue
- `DailyTick` → 推进案件调查进度 → 阶段迁移 → 生成下一阶段 Issue

### 4.2 三个 Quest 类

| 类 | 对标 | 独特玩法 |
|---|------|---------|
| `InvestigateVillageTheftQuest` | `CommissionQuest` 的调查类 | **Roguery 检定误导目击者**、Scout 搜寻线索、玩家选择汇报目标（真凶/嫁祸/查不出） |
| `ApprehendVillageThiefQuest` | `CommissionQuest.BountyHunt` | 如果玩家接了这个去抓无辜 NPC → 出狱后 `NPC冤情→NemesisRecord` |
| `LeadRetaliationQuest` | `CommissionQuest` + WorldEvent 联动 | 带领村民报复队出击，对标带领友军作战 |

所有 Quest 继承 `QuestBase`（对标 `CommissionQuest`），使用已有的三种委托步骤（大地图追踪 → 战斗 → 交付）。

### 4.3 对话集成

**村长对话选项**（对标 `IntentBase` 注册模式）：

```
阶段1: "听说村里丢了牲口？"  →  介绍案情 → "我可以帮忙查"
        [接 Quest_1]
        ---------------------------
        "我听说你们在找偷牲口的……"（玩家是贼，主动来误导）
        → [接 Quest_1] → 故意指错人

阶段2: "关于那个偷牲口的贼……"
        → (嫌犯≠玩家) "我可以去抓他" → [接 Quest_2]
        → (嫌犯=玩家) "你们搞错了——"
            → [Charm 辩护]
            → [赔钱]
        → (嫌犯=NPC，玩家是贼) "我会把他带回来" → [接 Quest_2]

阶段3: "牲口的事还没解决？"
        → (嫌犯≠玩家) "我带你们的人去" → [接 Quest_3]
        → (嫌犯=玩家) "我们可以商量……" → [赔钱/Charm]
```

对话注册走 `IntentRegistry`（对标 `CommissionIntent`）或新建 `VillageTheftIntent`。

---

## 五、通知与叙事（遵守叙事铁律）

### 5.1 暗探情报（阶段变化时推送）

**阶段1 — 村民发现:**
> "暗探来报——{village}近日有村民私下议论，说圈里的牲口不知怎的少了好几只。村长正在挨家挨户问话……看样子是要查个水落石出。"

**阶段2 — 嫌犯锁定（玩家）:**
> "急报——{village}传来消息：村民认定是你偷了他们的牲口。村长已经向附近放话，要找人来'讨个公道'。"

**阶段2 — 嫌犯锁定（NPC）:**
> "暗探来报——{village}村民认定是 {suspect} 偷了他们的牲口，正在悬赏捉拿此人。据说赏金已有 {reward} 第纳尔。"

**阶段3 — 报复部队出发:**
> "前线急报——{village}村民自己凑了钱，雇了几个打手，正满世界找 {suspect}。这事儿怕是不能善了了。"

### 5.2 酒馆传闻

```
阶段1: "听说{village}那边遭了贼……丢了牲口。也不知是哪个不长眼的干的。"
阶段2: "商队的人说，{village}的村民认定{suspect}是贼。悬赏都挂出来了……"
阶段3: "{village}的人火气大得很，雇了打手在全境找人。"
```

### 5.3 路途拦截（报复阶段 — 嫌犯=玩家时）

```
→ 地图上遇到一个从{village}方向来的旅人。
→ "前面有个村子……村里人都在说要找一个叫{Hero.MainHero.Name}的人。
   你最好别往那边走——他们正悬赏捉人呢。"
```

### 5.4 需要新增的 Narrative.csv 条目

| ID | 用途 |
|----|------|
| `VillageTheft_Discovery_Headman` | 村长介绍案情 |
| `VillageTheft_Suspect_Headman_Player` | 村长对玩家冷脸 |
| `VillageTheft_Suspect_Headman_NPC` | 村长悬赏 NPC |
| `VillageTheft_Retaliation_Headman` | 村长动员报复 |
| `VillageTheft_Resolved_Restitution` | 玩家赔钱后村长回应 |
| `VillageTheft_Resolved_Caught` | 抓到真贼后村长感谢 |
| `VillageTheft_Resolved_ColdCase` | 冷案 |
| `VillageTheft_Gossip_1/2/3` | 酒馆传闻（3 个阶段各一条） |

---

## 六、新增/改动文件清单

### 新增文件

| 文件 | 职责 |
|------|------|
| `Stealth/VillageTheftCase.cs` | 案件数据模型 + 状态机 + List<VillageTheftCase> 管理器 + JSON 序列化 |
| `Quests/Commissions/VillageTheftIssues.cs` | 3 个 Issue 类 + IssueBehavior（创建/阶段迁移/DailyTick） |
| `Quests/Commissions/VillageTheftQuests.cs` | 3 个 Quest 类（调查/追捕/带队报复） |
| `Quests/WorldEvents/VillageTheftRetaliation.cs` | 报复部队 PartyComponent（如需自定义 AI）+ 部队 spawn 逻辑 |

### 改动文件

| 文件 | 改动点 | 复杂度 |
|------|--------|--------|
| `VillageAnimalTracker.cs` | `RecordTheft` 增加参数 → 触发 `VillageTheftCase.OpenOrUpdate` | 低 |
| `InteractionMissionView.cs` | `TryStealAnimal` 传 thief + 目击者数量入 `RecordTheft` | 低 |
| `StealManager.cs` | 已有 `GetWitnesses`，偷动物时调用以记录目击人数 | 低（复用） |
| `WorldEventConfig.cs` | 新增 `WorldEventType.VillageRetaliation` 配置 | 低 |
| `WorldEventDatabase.cs` | 枚举增加 `VillageRetaliation` | 低 |
| `MyBehavior.cs` | `DailyTick` → `VillageTheftCase.ProcessDaily`；`SyncData` → 序列化案件列表 | 中 |
| `StoryContext.cs`（SaveDefiner） | 新增存档字段 | 低 |

---

## 七、实施阶段（建议分 3 步）

### Step 1：数据层 + 基础链路（DDD：数据驱动）
- `VillageTheftCase` 数据模型 + JSON 持久化
- `VillageAnimalTracker.RecordTheft` → 触发案件创建
- `MyBehavior.DailyTick` → 每日推进调查进度
- 验证：偷羊 → 存档 → 读档 → 案件仍在

### Step 2：Issue-Quest 三层（Issue-Quest 双层模型）
- 3 个 Issue + 3 个 Quest 实现
- 阶段迁移逻辑
- 玩家接 Quest 的各类分支
- 验证：偷羊 → 等到明天 → 村庄出现 `!` → 玩家可接调查 Quest

### Step 3：报复部队 + 通知（WorldEvent + Narrative）
- `WorldEventType.VillageRetaliation` 配置
- 报复部队 spawn + AI（对标 `HeroNemesisTracker.SpawnNemesisParty`）
- NinjaNotification + 酒馆传闻 + 路途拦截
- 验证：偷羊 → 被锁定 → 报复部队追玩家 → 战斗/赔钱/说服

---

## 八、开放问题（待对齐）

1. **目击系统**：`StealManager.GetWitnesses` 目前用于偷 NPC 装备。偷动物时能否复用？动物 Agent 不是 Human，`NpcSightSystem.IsPlayerSeeing` 是否对动物生效？
   - 可能需要一个简化版的目击检测：偷动物时检查周围一定距离内有多少村民 Agent。
   
2. **如果玩家不被识别为贼**（Roguery 高 + 无目击），`ThiefHeroId` 应该为 null。但 `VillageTheftCase` 需要知道实际贼是谁（用于嫁祸逻辑）。是否用两个字段：`ActualThiefId`（永远真实）+ `SuspectHeroId`（调查结果）？

3. **IssueEffect 的具体数值**（Security/Prosperity 降多少）需要对齐原版的平衡。是否直接复用原版的 `IssueEffect` 模板（如 `security` / `prosperity` effect）？

4. **村民报复部队的文化/装备**：是否需要自定义 PartyTemplate，还是直接复用村庄所属文化的民兵模板？

5. **如果玩家接了阶段1的 Quest 但自己就是贼**：玩家可以在找到线索后选择汇报假结论。这个"汇报"环节的 UI 用什么？是否有现有的多选对话可供复用？还是需要用 Inquiry 弹窗？

---

## 九、参考资料

- 原版 Issue-Quest 架构：[Knowledge/原版骑砍2任务系统分析.md](../Knowledge/原版骑砍2任务系统分析.md)
- 可复用模式目录：[Knowledge/vanilla_quests/04_patterns_catalog.md](../Knowledge/vanilla_quests/04_patterns_catalog.md)
- 完整 API 参考：[Knowledge/vanilla_quests/05_interface_reference.md](../Knowledge/vanilla_quests/05_interface_reference.md)
- 叙事设计铁律：[plans/rules/narrative-design.md](rules/narrative-design.md)
- 已造轮子速查：[plans/rules/wheels.md](rules/wheels.md)
- 偷盗系统全链路：[Knowledge/偷盗系统分析与优化方案.md](../Knowledge/偷盗系统分析与优化方案.md)
- 击晕机制踩坑：[Knowledge/击晕机制_引擎能力与实现踩坑.md](../Knowledge/击晕机制_引擎能力与实现踩坑.md)

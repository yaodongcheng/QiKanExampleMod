# NPC 视线系统 + 多因素台词 + 据点荣誉 + 招募增强

> 计划文档，供后续开发和维护参考。最后更新：2026-06-11。

---

## 一、背景与目标

### 触发需求

1. 已招募的 NPC 不再弹 inquiry，改为对话提示"已应募"
2. 女性 NPC 招募后说一句木兰从军台词
3. 小孩 NPC 不弹 inquiry，说自己太小
4. 每个 Settlement 记录玩家荣誉值，影响 NPC 寒暄内容和冒泡打招呼
5. 荣誉值高 → 征兵打折（以 20 第纳尔为基础价）

### 架构洞察

上述需求暴露了三个架构缺口：

- **NPC 视线检测散落各处**：`BubbleSayMissionView`（camera dot + 距离）、`StealManager.GetWitnesses`（FOV + RayCast）、`InteractionMissionView.ProcessAgentCandidate`（准星锁定）各自实现了一遍"距离+角度"——应统一为 `NpcSightSystem`
- **台词系统是平面 key**：当前 `DialogueTemplateHelper` 只用 `{Goal}_{Success/Fail}` 一维查 CSV，无法表达"同一事件、不同身份/性别/荣誉下说不同话"——应升级为多因素框架
- **荣誉值无存储**：`PlayerResources.Reputation/Notoriety` 硬编码默认值不持久化——应建立 `SettlementHonorStore`

---

## 二、架构总览

```
┌─────────────────────────────────────────────────────────────┐
│ NpcSightSystem (统一视线引擎, MissionBehavior)                │
│                                                             │
│  静态查询（无状态，各处按需调）:                                │
│    CanAgentSeeTarget(observer, target, radius, fov)          │
│    GetObserversOf(target, radius, fov)                       │
│    CanPlayerSee(npc)        ── 玩家→NPC，替代 BubbleSay 的    │
│                                  camera dot                  │
│    CanNpcSeePlayer(npc)     ── NPC→玩家，替代 StealManager    │
│    GetNpcsPlayerSees()                                       │
│    GetNpcsObservingPlayer()                                  │
│                                                             │
│  事件（tick 驱动，按注册的 tracked target 触发）:            │
│    RegisterTrackedTarget(agent, obsRadius, viewRadius)        │
│    OnAgentStartObserving(observer, target)                     │
│    OnAgentStopObserving(observer, target)                      │
│    OnTargetStartSeeing(target, seenAgent)                      │
│    OnTargetStopSeeing(target, seenAgent)                       │
│                                                             │
│  玩家默认注册: RegisterTrackedTarget(Agent.Main, 15f, 50f)    │
│  后续可注册任意 Agent（哨兵、Boss…），同一套 API              │
│                                                             │
│  替代三个旧实现:                                              │
│    ← BubbleSayMissionView 的 camera dot + 距离               │
│    ← StealManager.GetWitnesses()                            │
│    ← InteractionMissionView.ProcessAgentCandidate()          │
└─────────────────────────────────────────────────────────────┘
        │ 消费者
        ├── SightBubbleConsumer (NPC 看到玩家→冒泡，内容由多因素框架决定)
        ├── BubbleSayMissionView (冒泡可见性)
        ├── StealManager.GetWitnesses (偷窃目击检测)

┌─────────────────────────────────────────────────────────────┐
│ MultiFactorDialogue (台词框架)                                │
│                                                             │
│ CSV ID = {EventKey}_{Honor}_{Gender}_{Identity}              │
│                                                             │
│ 查表 fallback: exact → 逐维改 Any → 代码兜底                   │
│                                                             │
│ 维度: HonorLevel(High/Neutral/Low), NpcGender(Male/Female),  │
│       NpcIdentity(Lord/Soldier/Civilian)                    │
└─────────────────────────────────────────────────────────────┘
        │ 消费者
        ├── 寒暄话题 (OpenChatTopicMenu)
        ├── 冒泡台词 (SightBubbleConsumer)
        ├── 招募台词 (RecruitSoldierIntent)
        └── (未来: 所有 NPC 说的话)

┌─────────────────────────────────────────────────────────────┐
│ SettlementHonorStore (数据层, 跨存档持久化)                     │
│                                                             │
│ Dictionary<settlementId, int>  → JSON → MyBehavior.SyncData │
│                                                             │
│ Modify(s, delta) 可正可负  |  Set(s, value) 直接设           │
│                                                             │
│ 荣誉段: ≥5→High  -4~4→Neutral  ≤-5→Low                      │
└─────────────────────────────────────────────────────────────┘
        │ 消费者
        ├── 征兵打折 (RecruitSoldierIntent)
        ├── 台词选择 (MultiFactorDialogue)
        ├── NPC 态度 (未来)
        └── (未来: 犯罪扣荣誉, 任务涨荣誉, …)
```

---

## 三、实现步骤

### Step 1: NpcSightSystem — 统一视线引擎

**文件**: `AI/NpcSightSystem.cs`（新建，MissionBehavior）

**设计原则**: 任意 Agent 对任意 Agent 的视线检测，玩家只是被追踪的"重点目标"之一。后续 NPC 间战术协同（如哨兵看到敌人通知队友）直接复用同一套 API。

**核心算法**（复刻自 `StealManager.GetWitnesses`）：
```
CanAgentSeeTarget(observer, target, radius, fov):
  1. 距离: observer.Position.Distance(target.Position) <= radius
  2. 高度差: |observer.z - target.z| <= 3m
  3. FOV: dot(observer.LookDirection, normalize(target.Pos - observer.Pos)) >= cos(fov/2)
  4. 遮挡: Scene.RayCastForClosestEntityOrTerrain(observer.EyeGlobal, target.ChestGlobal)
         → 碰撞距离 >= 目标距离 - 0.2m → 无遮挡
```

**公开 API**：

```csharp
public class NpcSightSystem : MissionLogic
{
    // ── 通用静态查询（任意 Agent → 任意 Agent）──
    public static bool CanAgentSeeTarget(Agent observer, Agent target,
        float radius = 15f, float fovDegrees = 120f);
    public static List<Agent> GetObserversOf(Agent target,
        float radius = 15f, float fovDegrees = 120f);

    // ── 玩家快捷包装（内部调通用方法，只设不同默认参数）──
    public static bool CanPlayerSee(Agent npc);         // radius=50f, fov=140°
    public static bool CanNpcSeePlayer(Agent npc);      // radius=15f, fov=120°
    public static List<Agent> GetNpcsPlayerSees();
    public static List<Agent> GetNpcsObservingPlayer();

    // ── 重点目标注册（tick 缓存 + 事件）──
    // 性能边界: 只注册少量重要 Agent（玩家 + 随从等，预期 ≤5 个）。
    // 每个 tracked target 每 tick 扫描周边 Agent，O(targets × nearby)。
    // 场景里的大群 NPC 不注册，只在需要时用静态查询按需检测。
    // 后续如做 NPC 间战术协同，只注册哨兵/Boss/随从，不搞全员感知。
    public void RegisterTrackedTarget(Agent target, float observerRadius, float targetViewRadius);
    public void UnregisterTrackedTarget(Agent target);

    // ── 事件（对每个已注册的 tracked target 触发）──
    event Action<Agent, Agent> OnAgentStartObserving;    // (observer, target)
    event Action<Agent, Agent> OnAgentStopObserving;
    event Action<Agent, Agent> OnTargetStartSeeing;      // (target, seenAgent)
    event Action<Agent, Agent> OnTargetStopSeeing;
}
```

**Tick 逻辑**（~1s 间隔）：
```
对每个已注册的 trackedTarget:
  1. GetNearbyAgents(trackedTarget.Position, observerRadius, candidates)
  2. 过滤非人类、非活跃
  3. 对每个 candidate:
     - CanAgentSeeTarget(candidate, trackedTarget, ...) → _observers[target]
     - CanAgentSeeTarget(trackedTarget, candidate, ...) → _targetSees[target]
  4. diff 上一帧 → 触发 OnAgentStart/StopObserving、OnTargetStart/StopSeeing
```

**初始化**: `MySubModule.OnMissionCreated` → `RegisterTrackedTarget(Agent.Main, 15f, 50f)` 注册玩家

**重构三个旧调用点**：

| 文件 | 旧实现 | 改为 |
|---|---|---|
| `Bubble/BubbleSayMissionView.cs` | 自算 camera dot + 距离 | `NpcSightSystem.CanPlayerSee(agent)` |
| `Stealth/StealManager.cs` | `GetWitnesses()` 内含 FOV+RayCast | `NpcSightSystem.GetNpcsObservingPlayer()` |
| `Interaction/InteractionMissionView.cs` | `ProcessAgentCandidate()` 自算距离+角度 | `NpcSightSystem.CanPlayerSee(agent)` 辅助 |

**未来扩展示例**（本轮不实现，证明通用性）：
```csharp
// 哨兵看到敌人 → 通知队友
sightSystem.RegisterTrackedTarget(sentryAgent, 30f, 30f);
sightSystem.OnAgentStartObserving += (observer, target) => {
    if (target == sentryAgent && observer.Team != sentryAgent.Team)
        sentryBrain.RaiseAlarm(observer);
};
```

---

### Step 2: MultiFactorDialogue — 多因素台词框架

**文件**: `Intents/DialogueTemplateHelper.cs`（重写）

**维度枚举**：

```csharp
public enum HonorLevel  { High, Neutral, Low }   // ≥5 / -4~4 / ≤-5
public enum NpcGender   { Male, Female }
public enum NpcIdentity { Lord, Soldier, Civilian }

public struct DialogueFactors
{
    public HonorLevel Honor;
    public NpcGender Gender;
    public NpcIdentity Identity;

    // 静态工厂: 从 IntentContext + SettlementHonorStore 构建
    public static DialogueFactors FromContext(IntentContext ctx);
}
```

**CSV ID 命名规则**: `{EventKey}_{Honor}_{Gender}_{Identity}`

例:
- `Chat_Greeting_High_Male_Lord` — 高荣誉下，男性领主对你的问候
- `BubbleGreet_Low_Female_Civilian` — 低荣誉下，女性平民看到你的冒泡
- `Chat_Greeting_Neutral_Male_Any` — 中等荣誉，任意男性

**查表 fallback 链**（从具体到宽泛）：

```
1. {EventKey}_{Honor}_{Gender}_{Identity}     ← 最精确
2. {EventKey}_{Honor}_{Gender}_Any
3. {EventKey}_{Honor}_Any_Any
4. {EventKey}_Any_Any_Any                     ← 通用兜底
5. 代码硬编码兜底句                            ← 表为空时
```

**新 API**（旧 API 保持兼容）：

```csharp
// 多因素版
public static string Get(string eventKey, DialogueFactors factors,
    out string emotion, Hero target = null, Agent agent = null);

// 旧 API 不变，内部转调多因素版（factors 取 Neutral/Male/Civilian 兜底）
public static string Get(string dialogueKey, bool success,
    out string emotion, Hero target, Agent agent);
```

---

### Step 3: SettlementHonorStore — 据点荣誉存储

**位置**: `InteractionOptionManager.cs` 同文件（紧挨 `InteractionOptionCategoryMap`）

```csharp
public static class SettlementHonorStore
{
    private static Dictionary<string, int> _honor = new();

    public static int Get(Settlement s);
    public static int Get(string settlementId);
    public static void Modify(Settlement s, int delta);   // 可正可负
    public static void Set(Settlement s, int value);      // 直接设

    public static string Serialize();    // → JSON
    public static void Deserialize(string json);
}
```

**持久化**（`Core/MyBehavior.cs`）:

```csharp
public override void SyncData(IDataStore dataStore)
{
    // 已有: 意图冷却
    string cooldownJson = Story.IntentCooldownStore.Serialize();
    dataStore.SyncData("lwn_intent_cooldowns", ref cooldownJson);
    if (dataStore.IsLoading) Story.IntentCooldownStore.Deserialize(cooldownJson);

    // 新增: 据点荣誉
    string honorJson = SettlementHonorStore.Serialize();
    dataStore.SyncData("lwn_settlement_honor", ref honorJson);
    if (dataStore.IsLoading) SettlementHonorStore.Deserialize(honorJson);
}
```

**荣誉段映射**（给台词框架用）:

| 荣誉值 | HonorLevel |
|---|---|
| ≥ 5 | High |
| -4 ~ 4 | Neutral |
| ≤ -5 | Low |

**本轮荣誉变化点**: 招募成功 → `Modify(currentSettlement, +1)`

---

### Step 4: 招募意图增强

**文件**: `Intents/RecruitSoldierIntent.cs`

#### 4.1 已招募追踪

- 静态 `HashSet<int>` 按 `Agent.Index` 记录（运行时，场景销毁即失效，与 `ClearTemporaryMemories` 同一生命周期）
- `Evaluate()`: `_recruitedAgents.Contains(ctx.Agent.Index)` → `Eligibility.Grey("此人已应募入伍")`
- `OnInstant()` 兜底: 台词 + 回主菜单

#### 4.2 小孩不可招募

- `IntentContext` 新增 `IsChild: bool`
  - Hero: `Target.Age < 16`
  - 非 Hero: `agent.Character.IsChild`（TaleWorlds API）
- `Evaluate()`: `ctx.IsChild` → `Eligibility.Hide()`
- `OnInstant()` 兜底: 台词（`RecruitSoldier_TooYoung`）

#### 4.3 女性台词

- 招募成功后，若 `ctx.Agent.Character.IsFemale` → 用多因素框架查 `RecruitSoldier_Female` 台词
- 通过 `BubbleSayMissionView.AgentBubbleSay` 显示（不阻塞流程）

#### 4.4 荣誉打折

- 基础价: **20 第纳尔**（替代 `PartyWageModel.GetTroopRecruitmentCost`）
- 荣誉打折: `discount% = min(honor * 2, 50)` → 每点荣誉 -2%，最高半价
- 魅力砍价在荣誉折后**乘法叠加**: `final = 20 * (1 - honor%) * (1 - charm%)`
- 最低 5 第纳尔
- 荣誉不够钱不够 → HUD 提示"你的钱不够 / 声望不够"

---

### Step 5: 寒暄联动荣誉

**文件**: `InteractionController.cs` — `OpenChatTopicMenu`

- 获取当前 settlement 荣誉 → 映射 HonorLevel
- 四个话题（问候/近况/打听/恭维）仍存在，但不再硬编码 `Chat_Greeting`——改用多因素框架按 `{EventKey}_{Honor}_{Gender}_{Identity}` 查
- 好感增益微调: 高荣誉 +2, 中 +1, 低 0

**EventKey 对应**:

| 话题 | EventKey |
|---|---|
| 问候 | `Chat_Greeting` |
| 近况 | `Chat_Weather` |
| 打听 | `Chat_Gossip` |
| 恭维 | `Chat_Praise` |

---

### Step 6: SightBubbleConsumer — 通用"看到玩家→冒泡"

**位置**: `Interaction/InteractionMissionView.cs` 内或独立小类

- 在 `OnMissionCreated` 时获取 `NpcSightSystem` 并注册 `OnAgentStartObserving`，过滤 `target == Agent.Main`
- 触发时:
  1. 查当前 settlement 荣誉 → 映射 HonorLevel
  2. 构建 `DialogueFactors`（荣誉 + 性别 + 身份）
  3. 概率 = `min(0.05 + honor * 0.01, 0.20)` → 掷骰
  4. 命中 → 用多因素框架查 `BubbleGreet` 台词
  5. `BubbleSayMissionView.AgentBubbleSay(agent, line)`
- **通用性**: 冒泡本身不绑定荣誉——任何"NPC 看到玩家想说点什么"的场景都走这里。说什么由多因素 CSV 决定，荣誉只是因素之一
- 注：冒泡**可见性**（玩家是否能看到这个 NPC 头上的泡泡）仍由 `BubbleSayMissionView` 管理，它订阅 `OnTargetStartSeeing`（target=Player）

---

### Step 7: Dialogue.csv 新增台词

旧行全部保留。新增行按 `{EventKey}_{Honor}_{Gender}_{Identity}` 命名：

| ID | 用途 | 示例内容 |
|---|---|---|
| `BubbleGreet_High_Any_Any` | 冒泡: 高荣誉招呼 | "{PLAYER} 大人，恭迎！" |
| `BubbleGreet_Neutral_Any_Any` | 冒泡: 寻常招呼 | "来了啊…" |
| `BubbleGreet_Low_Any_Any` | 冒泡: 低荣誉躲避 | "（赶紧低头走开）" |
| `Chat_Greeting_High_Any_Any` | 寒暄: 高荣誉 | "大人光临，蓬荜生辉" |
| `Chat_Greeting_Low_Any_Any` | 寒暄: 低荣誉 | "……有何贵干" |
| `RecruitSoldier_AlreadyRecruited_Any_Any_Any` | 已应募 | "某已应募，莫要再问" |
| `RecruitSoldier_TooYoung_Any_Any_Any` | 太小 | "某年纪尚小，再过几年吧" |
| `RecruitSoldier_Female_Any_Female_Any` | 木兰台词 | "女儿身多有不便，且学那木兰从军" |

后三项 `_Any_Any_Any` 表示与荣誉/性别/身份无关（最宽泛 fallback）。

---

## 四、涉及文件汇总

| # | 文件 | 改动 |
|---|---|---|
| 1 | `AI/NpcSightSystem.cs` | **新建** — 统一视线引擎 |
| 2 | `Intents/DialogueTemplateHelper.cs` | **重写** — 多因素框架 |
| 3 | `InteractionOptionManager.cs` | **新增** — `SettlementHonorStore` |
| 4 | `Intents/RecruitSoldierIntent.cs` | 修改 — 4 条分支 |
| 5 | `Intents/IntentContext.cs` | 新增 `IsChild` |
| 6 | `Interaction/InteractionController.cs` | `OpenChatTopicMenu` 荣誉联动 |
| 7 | `Interaction/InteractionMissionView.cs` | 注册 SightBubbleConsumer |
| 8 | `Bubble/BubbleSayMissionView.cs` | 可见性改为调 `NpcSightSystem` |
| 9 | `Stealth/StealManager.cs` | `GetWitnesses` 改为调 `NpcSightSystem` |
| 10 | `Core/MyBehavior.cs` | `SyncData` 新增荣誉持久化 |
| 11 | `Core/MySubModule.cs` | 注册 `NpcSightSystem` 到 mission |
| 12 | `ModuleData/DesignData/Dialogue.csv` | 新增多因素台词行 |

---

## 五、验证方法

1. **已招募**: 找村民 → 招募成功 → 再次对话 → 置灰"已应募"
2. **女性**: 找女村民 → 招募 → 弹确认 → 确认后冒泡木兰台词
3. **小孩**: 找小孩 → 菜单无招募选项
4. **荣誉持久化**: 招募多人 → 存档读档 → 荣誉仍在 → 征兵价格降低
5. **打折**: 高荣誉据点（≥5）→ 征兵价格 < 20；低荣誉 → 原价
6. **寒暄**: 高荣誉据点 → 寒暄台词尊敬；低荣誉 → 冷淡
7. **冒泡**: 高荣誉据点 → NPC 频繁冒泡打招呼（10%+）；低荣誉 → 少且负面
8. **视线系统回归**: 偷窃 `GetWitnesses` 行为不变；冒泡显示不受影响

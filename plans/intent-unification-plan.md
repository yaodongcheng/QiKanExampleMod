# 意图/行动/任务三大系统统一重构计划

> **修订版**：根据用户反馈，增加了① InteractionOptionManager 去留分析、② 叙事层全部走 narrative-placeholder-system.md、③ ActionHandler 升级为通用执行层。

---

## Context

当前项目存在三套割裂的系统：
1. **玩家意图** (`IntentBase` + `IntentRegistry`) — 注册式，约 30 子类，有完整的资格判定/执行/后果闭环
2. **NPC 意图** (`NpcInitiative` + `InitiativeType`) — 无注册、无资格判定、无继承体系，纯数据类手动创建
3. **NPC 动作** (`IAtomicAction`) — Mission 层原子行为，与战略意图无结构化映射
4. **任务类型三套并存** — `CommissionCategory`(委托) / `QuestType`(主命) / 原版 Issue，底层玩法重叠但代码独立

**目标**：NPC 和玩家共享同一套 IntentBase 注册体系；所有任务类型以 `CommissionQuest` 为标准统一；叙事走 `PlaceholderResolver` 管道；ActionHandler 升级为所有意图后果的统一执行层。

---

## Part 0: InteractionOptionManager — 去留分析

### 现状

**定义**：[InteractionOptionManager.cs](ExampleModVS/ExampleMod/ExampleMod/Interaction/InteractionOptionManager.cs)（~110 行）

**唯一调用方**：[InteractionController.cs](ExampleModVS/ExampleMod/ExampleMod/Interaction/InteractionController.cs) 构造函数 `_optionManager = new InteractionOptionManager(this)`，之后 `_optionManager.BuildOptionVMs(targetAgent)` 产出 `StoryOptionVM[]`。

**职责**：桥接层 — 查询 `IntentRegistry.GetVisible(ctx)`，把意图列表转成 UI 层的 `StoryOptionVM`（含成功率显示、置灰状态、`DisableReason`）。

### 判定：保留，但职责微调

`InteractionOptionManager` 是**薄壳**（设计如此），它的存在价值是**分离关注点**：
- `IntentRegistry` — 关心"有哪些意图、哪些可见"
- `InteractionOptionManager` — 关心"怎么显示给玩家"（VM 构造、成功率文案、置灰原因）
- `InteractionController` — 关心"玩家选了之后怎么执行"

**改造点**：
1. `BuildOptionVMs` 加 `IntentSource` 过滤参数（默认 `Player`），当 NPC 对话中需要展示选项时传 `Both`
2. 原 `InteractionOptionType` 枚举中的 `InteractionOptionCategoryMap`（Type → Category 映射）保留——这是 UI 分类逻辑，属于 OptionManager 的职责范围
3. `SettlementHonorStore` 从 `InteractionOptionManager.cs` 末尾抽到独立文件（它本质是数据存储，不是交互管理，当前放这里是历史原因）

---

## Part 1: IntentBase 统一 — NPC 与玩家平权

### 1.1 `IntentBase` 增加 `IntentSource` 标志

**文件**：`Interaction/Intents/IntentBase.cs`

```csharp
[Flags]
public enum IntentSource
{
    None    = 0,
    Player  = 1 << 0,  // 玩家可用（菜单选项）
    Npc     = 1 << 1,  // NPC 可用（主动发起）
    Both    = Player | Npc
}

public abstract class IntentBase
{
    /// <summary>此意图对谁可用。默认 Both = 玩家和 NPC 都能用。</summary>
    public virtual IntentSource Source => IntentSource.Both;

    // —— 以下已有属性不变 ——
    public abstract InteractionOptionType Type { get; }
    public virtual InteractionCategory Category => InteractionOptionCategoryMap.GetCategory(Type);
    public abstract string DisplayName { get; }
    public virtual NegotiationGoalType? Goal => null;
    public virtual NegotiationTactic Tactic => NegotiationTactic.Flatter;
    public abstract Eligibility Evaluate(IntentContext ctx);
    public virtual void OnInstant(IntentContext ctx) { }
    public virtual void OnSuccess(IntentContext ctx) { }
    public virtual void OnFail(IntentContext ctx) { }
}
```

### 1.2 `IntentContext` 扩展 — NPC 视角 + 身份降级

**文件**：`Interaction/Intents/IntentContext.cs`

**问题**：NPC 的 Agent 不一定有效（大地图对话无 Agent），Hero 也不一定存在（模板士兵/农夫/强盗无 HeroObject）。`BuildForNpc` 必须处理三种身份等级：

| 场景 | Agent | Hero | 示例 |
|------|-------|------|------|
| 城镇场景 NPC | ✅ | ✅ | 有名有姓的守卫/商人 |
| 大地图遭遇对话 | ❌ | ✅ | 领主在大地图上对话 |
| 模板 NPC | ✅ | ❌ | 无名士兵、农民、强盗 |

三种等级决定意图能做什么：
- **有 Hero**：可以改关系、写记忆、检查通缉、发任务
- **仅 Agent**：只能做 Mission 层行为（拦截/说话/战斗），不能改关系/写记忆
- **都没有**：不触发任何 NPC 意图（没有交互对象）

**设计**：

```csharp
/// <summary>
/// NPC 意图发起方的身份等级。
/// </summary>
public enum NpcIdentityLevel
{
    None,       // 既无 Agent 也无 Hero — 不触发意图
    AgentOnly,  // 仅有 Agent（模板士兵/农民）— 只能做 Mission 行为
    Full,       // 有 Hero（可能有也可能没有 Agent）— 完整功能
}

public class IntentContext
{
    // —— 已有字段 ——
    public Agent Agent { get; private set; }       // 交互目标（玩家视角 = NPC；NPC 视角 = 玩家）
    public Hero Hero { get; private set; }         // 交互目标的 HeroObject
    public bool IsHero { get; private set; }

    // —— 新增：NPC 视角字段 ——
    /// <summary>NPC 发起方身份等级。非 null 表示这是 NPC 视角的上下文。</summary>
    public NpcIdentityLevel? NpcLevel { get; private set; }
    /// <summary>NPC 发起方的 Agent（模板士兵有，大地图对话为 null）</summary>
    public Agent NpcAgent { get; private set; }
    /// <summary>NPC 发起方的 Hero（模板士兵为 null）</summary>
    public Hero NpcHero { get; private set; }

    // —— 已有 Build（玩家视角，不改）——
    public static IntentContext Build(Agent targetAgent, InteractionController controller) { ... }

    // —— 新增 BuildForNpc（NPC 视角）——
    /// <summary>
    /// 构建 NPC 发起意图时的上下文。
    /// npcAgent 和 npcHero 至少需要一个非 null，否则返回 null（无法发起意图）。
    /// </summary>
    public static IntentContext BuildForNpc(Agent npcAgent = null, Hero npcHero = null)
    {
        // 1. 确定 NPC 身份等级
        NpcIdentityLevel level;
        Hero resolvedHero = npcHero ?? npcAgent?.Character?.HeroObject;

        if (resolvedHero != null)
            level = NpcIdentityLevel.Full;
        else if (npcAgent != null)
            level = NpcIdentityLevel.AgentOnly;
        else
            return null; // 什么都没有，不构建上下文

        var ctx = new IntentContext
        {
            NpcLevel = level,
            NpcAgent = npcAgent,
            NpcHero = resolvedHero,

            // 2. "交互目标"是玩家
            Agent = Agent.Main,
            Hero = Hero.MainHero,
            IsHero = true, // 玩家永远是 Hero
        };

        // 3. 关系相关字段（仅当 NPC 有 Hero 时有意义）
        if (resolvedHero != null)
        {
            ctx.Relation = resolvedHero.GetRelation(Hero.MainHero);
            ctx.SameFaction = resolvedHero.Clan?.Kingdom == Hero.MainHero.Clan?.Kingdom;
            ctx.IsLiege = resolvedHero.Clan?.Leader == Hero.MainHero;
            // ... 其他已有字段同理
        }
        else
        {
            // 模板 NPC：关系默认 0，无阵营概念
            ctx.Relation = 0;
            ctx.SameFaction = false;
        }

        return ctx;
    }
}
```

**意图类的 Evaluate 中如何使用**：

```csharp
public override Eligibility Evaluate(IntentContext ctx)
{
    // NPC 视角意图 — 检查 NPC 侧身份
    if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
        return Eligibility.Hide();

    // 仅 AgentOnly 的意图（如士兵拦截）— 不需要 Hero 也能工作
    // Full 的意图（如寻仇）— 需要 Hero 来查记忆/关系
    if (ctx.NpcLevel == NpcIdentityLevel.AgentOnly && RequiresHeroMemory)
        return Eligibility.Hide();

    // ... 具体判定逻辑
}
```

### 1.3 `IntentRegistry` 改造

**文件**：`Interaction/Intents/IntentRegistry.cs`

新增方法：

```csharp
/// <summary>取玩家可见的意图（现有逻辑，不改）</summary>
public static List<KeyValuePair<IntentBase, Eligibility>> GetVisible(IntentContext ctx);

/// <summary>取 NPC 可发起的意图（Source 含 Npc 标志，且 Evaluate 通过）</summary>
public static List<KeyValuePair<IntentBase, Eligibility>> GetNpcInitiatives(IntentContext ctx);

/// <summary>按意图类名查找 NPC 意图（用于 AgentBrain 迁移过渡）</summary>
public static IntentBase FindNpcIntent(string intentClassName);
```

### 1.4 消除 `AccountabilityOptionType` — 合并进 `InteractionOptionType`

**文件**：`Interaction/InteractionOptionManager.cs`

逐一比对后，`AccountabilityOptionType`（18 个值）中只有 2 个与 `InteractionOptionType` 真正重叠：

| AccountabilityOptionType | 映射到 | 判定 |
|---|---|---|
| `FightVillagers` | `Assault`（已有） | 同义——物理攻击 |
| `WalkAwayIntent`（类名） | `Leave`（已有） | 同义——离开对话 |

其余 16 个是犯罪场景独有的回应方式，`InteractionOptionType` 中没有对应项，需新增：

```csharp
public enum InteractionOptionType
{
    // —— 已有 ——
    Chat, Leave, Info, ProposalMarriage, Gift, TeaCeremony, Spar,
    StudySkill, ReportMission, RequestFunds, Resign, RequestWork,
    FindWork, Slander, SolicitSupport, RecruitHero, Betrayal,
    Assault, Order, Order_Follow, RecruitSoldier, PersuadeSurrender,

    // —— 新增：问责/犯罪（从 AccountabilityOptionType 迁移，仅无重叠的 16 个）——
    PayRestitution, CharmDefense, FrameSuspect, Threat, Investigate,
    Confess, SilenceWitness, LeadRetaliation, PayOnTheSpot,
    WorkOffDebt, FleeFromConfrontation, BetrayQuest,
    InnocenceProof, Settle, AcceptBountyQuest, LureArrest, Arrest,
}
```

`InteractionOptionCategoryMap` 新增分类：

```csharp
case InteractionOptionType.PayRestitution:
case InteractionOptionType.CharmDefense:
// ... 其他 14 个
    return InteractionCategory.Accountability;  // 新增分类
```

`InteractionCategory` 枚举新增：

```csharp
public enum InteractionCategory
{
    General,        // 基础
    Social,         // 社交/个人
    Official,       // 公务/主命
    Diplomacy,      // 外交/谋略
    Hostile,        // 敌对/暴力
    Accountability, // 🆕 犯罪追责
}
```

**文件**：`Interaction/Intents/AccountabilityIntents.cs`

- 删 `AccountabilityOptionType` 枚举
- `FightVillagersIntent.Type` → 返回 `InteractionOptionType.Assault`
- 其余 17 个问责意图类 `Type` → 返回 `InteractionOptionType` 对应新值
- 类名不变、逻辑不变、注册不变——只改 `Type` 返回值

### 1.5 NPC 意图类 — 新建 `NpcInitiativeIntents.cs`

**文件**：`Interaction/Intents/NpcInitiativeIntents.cs`（新建）

`InitiativeType` 枚举保留在 `NegotiationSystem.cs`（作为语义标签），7 个 NPC 意图类按 `IntentBase` 格式重写：

| InitiativeType | IntentBase 子类 | Source | 核心逻辑 |
|---|---|---|---|
| `NewsConflict` | `NewsConflictIntent` | `Npc` | 世界事件相关新闻 → 主动告知玩家 |
| `GuardIntercept` | `GuardInterceptIntent` | `Npc` | 守卫拦截：通缉/违禁品/未缴罚款检查 |
| `CrimeAccusation` | `CrimeAccusationIntent` | `Npc` | 犯罪指控开场白 |
| `Revenge` | `RevengeIntent` | `Npc` | 寻仇 NPC 主动找玩家 |
| `Greeting` | `GreetingIntent` | `Both` | 熟人打招呼（玩家侧对应 ChatIntent，同样适用） |
| `OfficialBusiness` | `OfficialBusinessIntent` | `Npc` | 税务官/传令兵公务通知 |
| `Crush` | `CrushIntent` | `Npc` | 爱慕者搭讪 |

每个类实现：
- `Evaluate(IntentContext ctx)` → NPC 是否应发起此意图（检查记忆事件、冷却、关系等）
- `OnInstant(IntentContext ctx)` → 创建 `PrepareOpeningAction`（入队到 AgentBrain），启动 LLM 开场白
- **叙事文本**：不硬编码，走 `NarrativeResolver` → CSV（按 `IntentType + Phase + NPC性格` 匹配），无 CSV 时走 LLM 兜底，LLM 不可用时走 `PlaceholderResolver` 拼接
- `Source` → 对应值，GreetingIntent 为 `Both` 允许玩家也主动寒暄

### 1.5.1 NPC 与玩家意图的执行路径分离

**核心规则**：
- **玩家侧意图**：不需要行为编排（`GetBehavior` 返回空）。玩家点击菜单选项本身就是"行为"——状态变更（ConsequenceExecutor）在点击后立即执行。
- **NPC 侧意图**：需要行为编排（`GetBehavior` 返回 IAtomicAction[]）。NPC 必须通过 Mission 层动作（走过去/说话/拔刀）来"表现"这个意图——AgentBrain 入队执行，动作完成后触发状态变更。

```
玩家意图执行路径：
  玩家点菜单 → InteractionController.DispatchIntent
    → ConsequenceExecutor.Execute(intent.GetInstantConsequences(ctx))
    // 一行，瞬时完成。没有 IAtomicAction。

NPC 意图执行路径：
  外部事件 → AgentBrain.ReceiveEvent
    → IntentRegistry 匹配意图
    → ConsequenceExecutor.Execute(intent.GetInstantConsequences(ctx))  // 如有瞬时效果
    → EnqueueAction(intent.GetBehavior(ctx))                           // NPC 物理表现
    // 两步：先状态后行为。行为在 Mission 帧循环中逐 tick 执行。
```

**`IntentBase` 新增方法**：

```csharp
/// <summary>
/// NPC 执行此意图需要的 Mission 层原子行为链。
/// 玩家侧意图返回空——玩家点击菜单就是"行为"，不需要 IAtomicAction。
/// </summary>
public virtual IAtomicAction[] GetBehavior(IntentContext ctx)
{
    return Array.Empty<IAtomicAction>();
}
```

**典型对比**：

| | 玩家送礼（GiftIntent） | 守卫拦截（GuardInterceptIntent） |
|---|---|---|
| Source | Player | Npc |
| GetBehavior | 空（玩家点菜单即行为） | [PrepareOpeningAction, ForceTalkAction] |
| GetInstantConsequences | TransferGold + ChangeRelation | 无（状态变更在对话选项中） |
| 调用方 | InteractionController.DispatchIntent | AgentBrain.ReceiveEvent |

### 1.6 `AgentBrain` 改造 — 收事件 → 匹配意图 → 编排行为

AgentBrain **只负责收事件 + 匹配意图 + 编排行为链**，不关心意图内部逻辑。

**文件**：`AI/AgentBrain.cs`

当前 `ReceiveEvent` 是硬编码 `if/else` 链（`ComeHere` → `FollowAgentAction`、`order_attack` → `FightEnemyAction`…）。

改造后：

```csharp
public void ReceiveEvent(AIEvent aiEvent)
{
    // 1. 查 IntentRegistry 有没有匹配的 NPC 意图
    var ctx = IntentContext.BuildForNpc(Owner);
    var initiative = IntentRegistry.GetNpcInitiatives(ctx)
        .FirstOrDefault(pair => MatchesEvent(pair.Key, aiEvent));

    if (initiative.Key != null)
    {
        // 走统一意图执行管道
        initiative.Key.OnInstant(ctx);
        return;
    }

    // 2. 兜底：纯 Mission 层动作（ComeHere / order_follow / order_attack）
    //    这些不是"意图"而是"动作执行"——IAtomicAction 队列保持不变
    HandleLegacyAtomicAction(aiEvent);
}
```

**关键**：`IAtomicAction` 体系不动——它们是 Mission 层最小行为单元，不是战略意图。AgentBrain 的角色变为"意图分发器 + 原子动作执行器"。

#### 1.6.1 `MatchesEvent` 匹配函数设计（🔴 关键——必须明确）

`AIEvent` 携带的是运行时裸数据（`EventType` 字符串 + `object[]` Args），`IntentBase` 是静态注册的类。匹配逻辑需要两层：

**第一层：`IntentBase` 声明自己响应的事件类型**

```csharp
// IntentBase 新增 virtual 属性
/// <summary>此意图响应哪些 AIEvent.EventType。空数组 = 不响应任何事件（纯玩家侧意图）。</summary>
public virtual string[] TriggerEvents => Array.Empty<string>();
```

**第二层：`IntentBase` 新增 `CanHandle` 方法做深度匹配**

```csharp
/// <summary>
/// 收到匹配的 EventType 后，检查事件参数是否满足此意图的触发条件。
/// 基类默认返回 true（EventType 匹配即可）。子类可 override 做更细的判断。
/// </summary>
public virtual bool CanHandle(AIEvent aiEvent, IntentContext ctx)
{
    return true;
}
```

**`MatchesEvent` 静态函数**

```csharp
// AgentBrain 中的静态匹配函数
private static bool MatchesEvent(IntentBase intent, AIEvent aiEvent)
{
    // 第一层：EventType 白名单
    if (intent.TriggerEvents == null || intent.TriggerEvents.Length == 0)
        return false;
    if (!intent.TriggerEvents.Contains(aiEvent.EventType))
        return false;

    // 第二层：深度参数匹配（由子类 override CanHandle）
    var ctx = IntentContext.BuildForNpc(/* agent */); // 注意：这里 ctx 是预构建的，CanHandle 可能需要额外参数
    return intent.CanHandle(aiEvent, ctx);
}
```

**各 NPC 意图的 `TriggerEvents` 映射**

| IntentBase 子类 | TriggerEvents | CanHandle 额外判断 |
|---|---|---|
| `GuardInterceptIntent` | `"WitnessCrime_GatherOnLook"`, `"GuardCheck"` | 检查 `Args[0]` thief 是否为目标玩家、Args[1] victim 是否有效 |
| `GreetingIntent` | `"PlayerApproach"`, `"DailyTick_Social"` | 检查关系/上次见面时间冷却 |
| `CrimeAccusationIntent` | `"WitnessCrime_GatherOnLook"` | 检查 Owner==victim（只有受害方才触发指控开场白） |
| `RevengeIntent` | `"NemesisDetected"`, `"PlayerApproach"` | 检查是否有宿敌记录 |
| `NewsConflictIntent` | `"WorldEvent_NewsArrived"` | 检查是否有相关世界事件可告知 |
| `OfficialBusinessIntent` | `"LordCommand_Deliver"`, `"TaxCollection"` | 检查 Owner 的 Hero 是否有待下发的命令 |
| `CrushIntent` | `"DailyTick_Social"`, `"PlayerApproach"` | 检查是否有"爱慕"记忆 + 冷却 |

**`CanHandle` 实现示例——`GuardInterceptIntent`**

```csharp
public class GuardInterceptIntent : IntentBase
{
    public override IntentSource Source => IntentSource.Npc;
    public override string[] TriggerEvents => new[] { "WitnessCrime_GatherOnLook", "GuardCheck" };

    public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
    {
        if (aiEvent.Args == null || aiEvent.Args.Length < 2) return false;
        var thief = aiEvent.Args[0] as Agent;
        if (thief != Agent.Main) return false; // 只拦截玩家
        // 检查是否有活跃通缉/未缴罚款/违禁品
        var wanted = WorldEventStore.FindActiveWanted(Hero.MainHero);
        return wanted != null;
    }
}
```

**`CanHandle` 需要 `Agent` 参数——`IntentContext.BuildForNpc` 的签名调整**

`CanHandle` 的 `IntentContext` 需要在调用前构建，但 `AIEvent.Args` 的解析需要 `Agent` 引用。解决方案：`IntentContext` 中 `NpcAgent` 在 `BuildForNpc` 时填入（已设计），但 `AIEvent` 本身作为参数传入 `CanHandle`，子类解析 `Args` 时直接访问 `aiEvent.Args`，不需要 ctx 携带事件参数。

### 1.7 `PrepareOpeningAction` — 构造函数改为接收 `IntentBase` + 保留 `PendingConflict`

**文件**：`AI/Actions/AtomicAction.cs`

```csharp
public class PrepareOpeningAction : IAtomicAction
{
    private IntentBase _intent;
    private IntentContext _ctx;
    private PendingConflict _conflict; // 🔑 保留——运行时数据，IntentBase 静态属性无法覆盖

    /// <summary>新构造函数：接收意图 + 上下文 + 可选运行时冲突数据</summary>
    public PrepareOpeningAction(IntentBase intent, IntentContext ctx, PendingConflict conflict = null)
    {
        _intent = intent;
        _ctx = ctx;
        _conflict = conflict; // 世界事件运行时数据（eventId/topicName/goalDesc/severity/type）
    }

    // 旧构造函数保留 + [Obsolete]，过渡期两端都可用
    [Obsolete("Use PrepareOpeningAction(IntentBase, IntentContext, PendingConflict?)")]
    public PrepareOpeningAction(InitiativeType type, string desc) { ... }

    // 迁后使用时：PendingConflict 由 NPC 意图的 OnInstant 内部创建并传入
    // 见 GuardInterceptIntent.OnInstant 示例
}
```

**`PendingConflict` 数据流**：`NPC 意图.OnInstant` → 创建 `PendingConflict`（eventId/topicName/goalDesc/severity → 从 WorldEvent/记忆系统提取）→ `new PrepareOpeningAction(this, ctx, conflict)` → 入队 AgentBrain。

**为什么不能并入 `IntentContext`**：`PendingConflict` 是"运行时对话主题"，属于单次交互的临时数据。`IntentContext` 是"对话上下文快照"（身份/关系/冷却），语义不同。合并会导致 `IntentContext` 膨胀为"万能上下文袋"。

**`GuardInterceptIntent.OnInstant` 示例**

```csharp
public override void OnInstant(IntentContext ctx)
{
    var evt = WorldEventStore.FindActiveWanted(Hero.MainHero);
    var conflict = new PendingConflict(
        eventId: evt?.EventId ?? $"GuardCheck_{CampaignTime.Now.ToHours}",
        topicName: evt?.GetConfig().DisplayName ?? "守卫盘查",
        goalDesc: ctx.NpcAgent.Name + "拦住了你的去路",
        severity: evt?.Severity ?? 50f,
        type: NegotiationGoalType.ResolveConflict_Apology
    );

    var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
    brain.ClearAllActions();
    brain.EnqueueAction(new PrepareOpeningAction(this, ctx, conflict));
    brain.EnqueueAction(new ForceTalkAction());
}
```

### 1.8 叙事文本：走 `narrative-placeholder-system.md` 管道

**所有新意图的 `DisplayName`、`ToolTip`、开场白、NPC 台词**一律走叙事占位符系统：

- **静态文本**（DisplayName/ToolTip）：走 `NarrativeResolver.Resolve(NarrativeFilters)` → CSV 模板匹配，以 `IntentType_Phase_NpcPersonality` 为 key
- **动态对话**（NPC 开场白/场景模板）：走 `PlaceholderResolver` + `CrimeDialogueBuilder` 模式，参考 [narrative-placeholder-system.md](plans/narrative-placeholder-system.md)
- **LLM 兜底**：CSV 未命中 + `IsLLMReady` → LLM 生成（已有模式，`RequestCommissionIntent.GenerateNarrative` 为范本）
- **硬编码兜底**：无 LLM → `PlaceholderResolver.Resolve(template)` 拼接占位符（信息完整，不强求风味）

**新增意图的叙事扩展流程**：参考 `plans/narrative-placeholder-system.md` 和 `.claude/skills/narrative-placeholder-extension.md`：
1. 分析新意图引入的独特信息维度 → 确定是否需要新占位符
2. 对照 8 个说话者身份（Authority/Witness/Suspect/Victim/Bystander/Companion/Mission/Retaliation）检查场景模板覆盖
3. 产出扩充清单（新占位符 + 新场景模板编号 + CrimeDialogueBuilder 改动 + Intent 列表）

### 1.9 Part 1 文件变更清单

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ 修改 | `Interaction/Intents/IntentBase.cs` | 加 `IntentSource` 枚举和 `Source` 属性；加 `TriggerEvents` 虚属性（NPC 意图声明响应的事件类型）；加 `CanHandle(AIEvent, IntentContext)` 虚方法 |
| ✏️ 修改 | `Interaction/Intents/IntentContext.cs` | 加 `BuildForNpc()` / `NpcAgent` 字段 |
| ✏️ 修改 | `Interaction/Intents/IntentRegistry.cs` | 加 `GetNpcInitiatives()` / `FindNpcIntent()` |
| ✏️ 修改 | `Interaction/InteractionOptionManager.cs` | `InteractionOptionType` 合并 Accountability 值；`SettlementHonorStore` 抽到独立文件 |
| ✏️ 修改 | `Interaction/Intents/AccountabilityIntents.cs` | 删 `AccountabilityOptionType`；`FightVillagersIntent.Type` → `Assault`；其余 17 个 `Type` 改为 `InteractionOptionType` 对应新值 |
| ➕ 新建 | `Interaction/Intents/NpcInitiativeIntents.cs` | 7 个 NPC 意图类（含 `TriggerEvents` + `CanHandle` 实现） |
| ✏️ 修改 | `AI/AgentBrain.cs` | `ReceiveEvent` 改为先查 IntentRegistry（`MatchesEvent` 两层匹配）；加 `HandleLegacyAtomicAction` 兜底；旧 if/else 保留在兜底路径 |
| ✏️ 修改 | `AI/Actions/AtomicAction.cs` | `PrepareOpeningAction` 新构造函数（`IntentBase` + `IntentContext` + `PendingConflict?`）；旧打 Obsolete |
| ✏️ 修改 | `Negotiation/NegotiationSystem.cs` | `NpcInitiative` 类打 `[Obsolete]` |
| ➕ 新建 | `Interaction/SettlementHonorStore.cs` | 从 InteractionOptionManager.cs 抽出 |
| 📋 参考 | `plans/narrative-placeholder-system.md` | 新增意图的叙事模板按此规范扩展 |
| 📋 参考 | `.claude/skills/narrative-placeholder-extension.md` | 叙事扩展检查清单 |

---

## Part 2: ActionHandler → 统一执行层（ConsequenceExecutor）

### 2.1 问题诊断

当前状态：
- **ActionHandler**（`InteractionController.cs` 29-237 行）：7 个硬编码 `string → Execute` 动作，**仅 LLM 路径使用**
- **IntentBase.OnSuccess/OnFail/OnInstant**：直接调用 Bannerlord Action API，**完全绕过 ActionHandler**
- **LLM prompt** 只能告诉 LLM 7 个可选 action——因为 `GetActionSpacePrompt` 只列出这些
- 新增一个能改变游戏状态的意图 → 需要**两处分别实现**（IntentBase 子类 + ActionHandler 注册）

### 2.2 方案：`ConsequenceExecutor` — 所有后果的统一执行层

**设计原则**：IntentBase 子类不再直接调用 Bannerlord API，而是声明"我需要的后果"，由 `ConsequenceExecutor` 统一执行。LLM 返回的 `npc_action` 也映射到同一套后果类型。

**新文件**：`Interaction/ConsequenceExecutor.cs`

#### 2.2.1 后果类型枚举

```csharp
/// <summary>后果类型（对应 Bannerlord Action APIs 的语义封装）</summary>
public enum ConsequenceType
{
    ChangeRelation,      // 改变双方关系
    TransferGold,        // 金钱转移（走 AgentControlHelper）
    TransferItem,        // 物品转移
    Marry,               // 结婚
    RecruitToClan,       // 招募入队
    LeaveFaction,        // 脱离阵营
    JoinFaction,         // 加入阵营
    StartCombat,         // 开战
    TakePrisoner,        // 俘虏
    ApplyInfamy,         // 恶名
    ApplyTrust,          // 信任
    ApplyHonor,          // 据点荣誉
    SetCooldown,         // 意图冷却
    RecordToMemory,      // 写入 NPC 记忆
    SpawnQuest,          // 生成任务
    // ... 按需扩展
}
```

#### 2.2.2 类型化后果基类（🔑 替代 `Dictionary<string, object>`）

**设计决策**：不使用 `Dictionary<string, object>`——它丢失编译期类型安全、key 拼错静默失败、无法 IDE 跳转。改用**类型化子类**，每个 `ConsequenceType` 对应一个子类，携带强类型参数 + 内置执行逻辑。

```csharp
/// <summary>后果基类：每个后果 = 类型 + 守卫条件 + 执行逻辑</summary>
public abstract class Consequence
{
    /// <summary>此后果的类型</summary>
    public abstract ConsequenceType Type { get; }

    /// <summary>是否关键——关键后果失败时抛异常（阻止静默半截状态），非关键后果 catch+log</summary>
    public virtual bool IsCritical => true;

    /// <summary>守卫条件 — null = 无条件执行。返回 false 时跳过此后果。</summary>
    public Func<IntentContext, bool> Guard { get; set; }

    /// <summary>执行此后果</summary>
    public abstract void Execute(IntentContext ctx);

    /// <summary>LLM prompt 用的简短描述（如 "改变双方关系 ±N"）</summary>
    public abstract string LlmDescription { get; }

    /// <summary>LLM prompt 用的参数说明（如 "delta: 正数=好感上升, 负数=好感下降, 范围 [-20, 20]"）</summary>
    public abstract string LlmParamHint { get; }
}

// ── 具体后果子类 ──

public class ChangeRelationConsequence : Consequence
{
    public override ConsequenceType Type => ConsequenceType.ChangeRelation;
    public override bool IsCritical => false; // 好感微调失败不致命
    public override string LlmDescription => "改变双方好感度 ±N";
    public override string LlmParamHint => "delta: 正数=好感上升, 负数=好感下降, 建议范围 [-20, 20]";

    public int Delta { get; set; }
    public bool ShowNotification { get; set; } = true;

    public override void Execute(IntentContext ctx)
    {
        if (ctx.Hero == null) return;
        ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, Delta, ShowNotification, true);
    }
}

public class TransferGoldConsequence : Consequence
{
    public override ConsequenceType Type => ConsequenceType.TransferGold;
    public override bool IsCritical => true; // 🔴 钱错了 = 经济崩坏，必须抛
    public override string LlmDescription => "金钱转移（第纳尔）";
    public override string LlmParamHint => "from: 付款方 (null=玩家), to: 收款方 (null=NPC), amount: 金额";

    public Hero From { get; set; }
    public Hero To { get; set; }
    public int Amount { get; set; }

    public override void Execute(IntentContext ctx)
    {
        var from = From ?? Hero.MainHero;
        var to = To ?? ctx.Hero;
        // 🔴 走 AgentControlHelper 保证守恒（铁律 4）
        AgentControlHelper.TransferGold(from, to, Amount);
    }
}

public class SetCooldownConsequence : Consequence
{
    public override ConsequenceType Type => ConsequenceType.SetCooldown;
    public override bool IsCritical => false;
    public override string LlmDescription => "设置意图冷却（天内不可再次使用）";
    public override string LlmParamHint => "days: 冷却天数, 通常 3-7";

    public Hero TargetHero { get; set; }
    public NegotiationGoalType Goal { get; set; }
    public float Days { get; set; }

    public override void Execute(IntentContext ctx)
    {
        var hero = TargetHero ?? ctx.Hero;
        if (hero != null)
            IntentCooldownStore.Set(hero, Goal, Days);
    }
}

public class RecordToMemoryConsequence : Consequence
{
    public override ConsequenceType Type => ConsequenceType.RecordToMemory;
    public override bool IsCritical => false; // 记忆写入失败不影响 gameplay
    public override string LlmDescription => "记录到 NPC 记忆系统";
    public override string LlmParamHint => "memoryType: DirectExperience/Dynamic, contentKey: 记忆分类, summary: 一句话摘要";

    public string MemoryType { get; set; } = "Dynamic";
    public string ContentKey { get; set; }
    public string Summary { get; set; }

    public override void Execute(IntentContext ctx)
    {
        var memory = AllNpcMemoryManager.GetMemoryForAgent(ctx.NpcAgent ?? /* 玩家侧从 ctx.Hero 推 */ null);
        if (memory == null) return;
        memory.Record(ContentKey, Summary);
    }
}

// …按需扩展更多子类：MarryConsequence, SpawnQuestConsequence, ApplyInfamyConsequence, TransferItemConsequence, StartCombatConsequence, TakePrisonerConsequence…
```

#### 2.2.3 `ConsequenceExecutor` 静态执行引擎

```csharp
/// <summary>统一执行层：唯一调用 Bannerlord Action API 的地方</summary>
public static class ConsequenceExecutor
{
    /// <summary>执行一组后果。Guard 失败的跳过。关键后果失败抛异常，非关键 catch+log。</summary>
    public static void Execute(IEnumerable<Consequence> consequences, IntentContext ctx)
    {
        foreach (var c in consequences)
        {
            if (c == null) continue;
            if (c.Guard != null && !c.Guard(ctx)) continue;

            try
            {
                c.Execute(ctx);
            }
            catch (Exception ex)
            {
                if (c.IsCritical)
                {
                    // 关键后果失败 → 抛出去，让调用方决定怎么处理（回滚/通知玩家/写入错误日志）
                    DebugLogger.Log($"[ConsequenceExecutor] CRITICAL failure: {c.Type} — {ex.Message}");
                    throw; // 不吞
                }
                else
                {
                    // 非关键后果失败 → log + 继续
                    DebugLogger.Log($"[ConsequenceExecutor] Non-critical failure: {c.Type} — {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 生成 LLM prompt 用的后果空间描述（替代旧的 GetActionSpacePrompt）。
    /// 遍历所有注册的 ConsequenceType + LlmDescription + LlmParamHint → 自动生成。
    /// </summary>
    public static string GetConsequenceSpacePrompt(IntentContext ctx)
    {
        var sb = new StringBuilder();
        // 枚举所有具体后果子类（via 反射扫描 Assembly 中 Consequence 子类）
        foreach (var consequenceType in GetAllConsequenceTypes())
        {
            var instance = (Consequence)Activator.CreateInstance(consequenceType);
            sb.AppendLine($"- \"{instance.Type}\": {instance.LlmDescription}");
            if (!string.IsNullOrEmpty(instance.LlmParamHint))
                sb.AppendLine($"    参数说明: {instance.LlmParamHint}");
        }
        return sb.ToString();
    }

    private static IEnumerable<Type> GetAllConsequenceTypes()
    {
        return typeof(Consequence).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Consequence)) && !t.IsAbstract);
    }
}
```

#### 2.2.4 🔴 铁律 4 审计机制（Debug-only）

**问题**：`Consequence.Execute` 是开放的——未来开发者可能在其中直接调 `GiveGoldAction.ApplyBetweenCharacters` 绕过 `AgentControlHelper`，违反铁律 4。

**方案**：Debug 模式下在 `ConsequenceExecutor.Execute` 前后做堆栈审计——检测是否有裸调禁止 API 的行为。非 Debug 编译时此检查完全不存在，零性能影响。

```csharp
#if DEBUG
/// <summary>铁律 4 违规检测：Debug 编译时扫描后果执行中是否绕过了 AgentControlHelper</summary>
private static readonly HashSet<string> _forbiddenResourceApis = new HashSet<string>
{
    "GiveGoldAction.ApplyBetweenCharacters",
    "GiveGoldAction.ApplyForHero",
    "GiveItemAction.ApplyForHero",
    "ChangeHeroGold",
    "ItemRoster.AddToCounts",
    "Equipment.AddEquipmentToSlotWithoutAgent",
};

public static void Execute(IEnumerable<Consequence> consequences, IntentContext ctx)
{
    foreach (var c in consequences)
    {
        // … 正常执行逻辑同上 …
#if DEBUG
        // 执行后检查：有没有裸调禁止 API？（通过堆栈帧分析）
        var stackTrace = new System.Diagnostics.StackTrace();
        foreach (var frame in stackTrace.GetFrames())
        {
            var methodSig = frame.GetMethod().DeclaringType?.Name + "." + frame.GetMethod().Name;
            foreach (var forbidden in _forbiddenResourceApis)
            {
                if (methodSig.Contains(forbidden))
                {
                    DebugLogger.Log($"[IRON_RULE_4_VIOLATION] {c.Type}.Execute() 直接调用了 {forbidden}！" +
                                    $"请改为走 AgentControlHelper.TransferGold/TransferItems。");
                }
            }
        }
#endif
    }
}
#endif
```

**注意**：堆栈审计是尽力而为的检测（不能 100% 拦截），真正的防线仍在代码审查 + 开发者纪律。但加上这条日志后，开发期间一旦踩坑立即会在 `RuntimeLog.txt` 看到 `[IRON_RULE_4_VIOLATION]` 前缀的错误。

### 2.3 IntentBase 改为声明后果

**文件**：`Interaction/Intents/IntentBase.cs`

```csharp
public abstract class IntentBase
{
    // —— 已有 ——
    public abstract Eligibility Evaluate(IntentContext ctx);

    /// <summary>声明此意图成功后产生的后果（替代直接调 API 的 OnSuccess）</summary>
    public virtual IEnumerable<Consequence> GetSuccessConsequences(IntentContext ctx)
    {
        yield break;
    }

    /// <summary>声明失败后果</summary>
    public virtual IEnumerable<Consequence> GetFailConsequences(IntentContext ctx)
    {
        // 基类默认：掉好感 + 进冷却
        if (ctx.Hero != null && FailRelationPenalty > 0)
            yield return new ChangeRelationConsequence
            {
                Delta = -FailRelationPenalty,
                ShowNotification = true
            };
        if (ctx.Hero != null && Goal.HasValue)
            yield return new SetCooldownConsequence
            {
                TargetHero = ctx.Hero,
                Goal = Goal.Value,
                Days = CooldownDays
            };
    }

    /// <summary>即时类后果</summary>
    public virtual IEnumerable<Consequence> GetInstantConsequences(IntentContext ctx)
    {
        yield break;
    }

    // —— 旧 OnSuccess/OnFail/OnInstant 保留 + [Obsolete] ——
    [Obsolete("Override GetSuccessConsequences instead")]
    public virtual void OnSuccess(IntentContext ctx) { }
    [Obsolete("Override GetFailConsequences instead")]
    public virtual void OnFail(IntentContext ctx) { }
    [Obsolete("Override GetInstantConsequences instead")]
    public virtual void OnInstant(IntentContext ctx) { }
}
```

### 2.4 迁移策略：渐进式

1. **Phase A**：新建 `ConsequenceExecutor`，旧 `ActionHandler` 保留
2. **Phase B**：`InteractionController.DispatchIntent` 改为：调 `intent.GetXxxConsequences(ctx)` → `ConsequenceExecutor.Execute(consequences, ctx)`。如果子类仍 override 旧 `OnSuccess/OnFail/OnInstant`，优先调新方法，无则 fallback 到旧方法
3. **Phase C**：逐个迁移 IntentBase 子类（从简单到复杂），每迁移一个删一个旧 override
4. **Phase D**：`ActionHandler` 的 7 个硬编码动作改为映射到 `ConsequenceType`，`GetActionSpacePrompt` 替换为 `GetConsequenceSpacePrompt`
5. **Phase E**：全部迁移完成后，删 `ActionHandler` 旧代码 + `OnSuccess/OnFail/OnInstant` virtual 方法

### 2.5 Part 2 文件变更清单

| 操作 | 文件 | 说明 |
|------|------|------|
| ➕ 新建 | `Interaction/ConsequenceExecutor.cs` | `ConsequenceType` 枚举 + `Consequence` 抽象基类 + 类型化子类（`ChangeRelationConsequence` / `TransferGoldConsequence` / `SetCooldownConsequence` / `RecordToMemoryConsequence` …）+ `ConsequenceExecutor` 静态执行引擎 + `GetConsequenceSpacePrompt` |
| ✏️ 修改 | `Interaction/Intents/IntentBase.cs` | 加 `GetSuccessConsequences`/`GetFailConsequences`/`GetInstantConsequences`（返回 `IEnumerable<Consequence>`）；旧方法打 Obsolete |
| ✏️ 修改 | `Interaction/InteractionController.cs` | `DispatchIntent` 改为走 `ConsequenceExecutor.Execute(consequences, ctx)`；`ActionHandler` 保留但打 Obsolete（LLM 路径临时兼容） |
| ✏️ 修改 | `Interaction/Intents/*Intents.cs` | 各意图子类逐个迁移到 `GetXxxConsequences` 模式（优先迁移 LLM 可见的 5-8 个意图，其余保持旧 override） |
| ✏️ 修改 | `LLM/PromptBuilder.cs` | `GetActionSpacePrompt` → `GetConsequenceSpacePrompt` |

---

## Part 3: 任务类型统一 — 以 CommissionQuest 为标准

### 3.0 前置验证：`QuestBase` 承载能力分析（🔴 必须先做）

**在写任何迁移代码前，必须验证 `CommissionQuest`（继承 `QuestBase`）能承载当前全部 25 种 QuestType 的生命周期。**

#### 3.0.1 `QuestBase` 生命周期接口

`QuestBase`（TaleWorlds.CampaignSystem）提供的虚方法：

| 生命周期节点 | 虚方法 | 说明 |
|-------------|--------|------|
| 任务创建 | `SetDialogs()` | 注册对话 |
| 任务开始 | `OnStartQuest()` | 初始化目标/日志/事件 |
| 每帧/周期 | `RegisterEvents()` / `HourlyTick()` / `DailyTick()` | 进度检查 |
| 完成任务 | `OnCompleteQuest()` | 结算奖励 |
| 任务失败 | `OnFailQuest()` / `OnCancelQuest()` | 超时/主动放弃/条件破坏 |
| 任务清理 | `OnFinalize()` | 清理事件/日志 |
| 存档 | `SyncData(IDataStore)` | 持久化 |

`CommissionQuest` 已实现以上全部节点。**所有 QuestType 变体的关键差异仅在 `OnStartQuest` 初始化和 `RegisterEvents` 完成条件检查**——`QuestBase` 架构完全能承载。

#### 3.0.2 各 QuestType → CommissionCategory 承载验证

| QuestType | 目标 CommissionCategory | 可用 QuestBase | 特殊需求 | 风险 |
|-----------|------------------------|---------------|---------|------|
| `DeliverItem_Food/Horse/Gun` | `DeliverGoods` | ✅ | 物品交付检查 → `RegisterEvents` 监听 `PlayerInventoryUpdated` | 低 |
| `EarnMoney` / `Fundraise` | `Fundraise` | ✅ | 纯数值目标（金钱阈值）→ `DailyTick` 检查 | 低 |
| `CollectDebt` | `CollectDebt` | ✅ | 目标 Hero 追踪 → 大地图 AI 移动 | 低 |
| `HuntBandits` | `BountyHunt` / `HideoutClear` | ✅ | 杀敌计数 → `RegisterEvents` 监听 `OnPartyDestroyed` | 低 |
| `RecruitTroops` / `TrainTroops` | `RecruitCharacter` / `Training` | ✅ | 部队检查 → `DailyTick` / `OnTroopRecruited` | 低 |
| `RaidVillage` | `RaidTarget` | ✅ | 村庄状态变更 → `RegisterEvents` 监听 `VillageStateChanged` | 中 |
| `CaptureSetlement` | `ConquerLocation` | ⚠️ | **攻城是跨场景操作**（大地图→Mission→大地图），完成条件检查需要 `OnSettlementOwnerChanged` 事件 | **中高** |
| `Assault` | （新增）`AssaultCharacter` | ✅ | 目标 Hero 状态检查（受伤/俘虏/死亡） | 低 |
| `DevelopSettlement_*` | `DevelopSettlement` (子类型) | ⚠️ | **非战斗数值增长型**（需要 N 天持续投入资源），时间跨度大（可能 10-30 天），与 CommissionQuest 现有的"去某地做某事"模式不同 | **中** |
| `DiplomacyTalk_*` | `DiplomaticMission` | ⚠️ | **多阶段对话型**（到达 → 谈判对话 → 结果），需要嵌入谈判系统 | **中高** |
| `ScoutSettlement` | （已有）`Investigation` | ✅ | 现有 CommissionQuest 已支持 | 低 |
| `EscortCaravan` | （已有）`CaravanEscort` | ✅ | 现有 CommissionQuest 已支持 | 低 |

**结论**：`QuestBase` 承载全部类型可行。`CaptureSetlement` 和 `DiplomacyTalk` 需要额外事件注册（`OnSettlementOwnerChanged`、谈判系统钩子），`DevelopSettlement` 需要扩展 `CommissionQuest` 支持"多日累积型"进度模式——但这三者都可以在 `CommissionQuest` 框架内解决，不需要单独的 Quest 基类。

### 3.1 `QuestType`（主命）与 `CommissionCategory`（委托）重叠分析

| QuestType | CommissionCategory | 底层玩法 |
|---|---|---|
| `HuntBandits` | `BountyHunt`, `HideoutClear` | 野外杀敌 |
| `EscortCaravan` | `CaravanEscort` | 护送 |
| `DeliverItem_Food`, `EarnMoney` | `SupplyEmergency`, `ProcurementAgent` | 经济/物资 |
| `ScoutSettlement` | `Investigation` | 情报/调查 |
| `RecruitHero` | — | 找人（Commission 无对应） |
| `RaidVillage` | `VillageDefense`（反向） | 村庄军事行动 |
| `CaptureSetlement` | — | 攻城（Commission 无对应） |
| `DiplomacyTalk_*` | — | 外交（Commission 无对应） |
| `DevelopSettlement_*` | — | 建设（Commission 无对应） |

差异仅在**发布者身份**（委托 = 民间 NPC，主命 = 领主）和**叙事语气**，不是玩法差异。

### 3.2 方案：`CommissionQuest` 作为唯一 QuestBase 子类

#### Step 1：`CommissionCategory` 扩展 + `IssuerType` 区分

**文件**：`Quests/Commissions/CommissionData.cs`

```csharp
/// <summary>发布者身份 — 决定 UI 分类标签和叙事语气</summary>
public enum CommissionIssuerType
{
    Commoner,   // 民间委托人（村民/工匠/商人）
    Notable,    // 要人（帮派头目/村长）
    Lord,       // 领主/大名 直接命令（= 原 QuestType 主命）
}

// CommissionCategory 新增（覆盖原 QuestType 独有玩法）：
DeliverGoods,       // 物资供应
CollectDebt,        // 收债
Fundraise,          // 筹款
RaidTarget,         // 劫掠村庄
ConquerLocation,    // 占领据点
DevelopSettlement,  // 内政建设（子类型区分：Food/Prosperity/Security）
DiplomaticMission,  // 外交任务（子类型：War/Alliance/Peace/SubOrdination/Dominate）
RecruitCharacter,   // 招募/人才调查
PersuadeLord,       // 劝诱
Training,           // 训练/修业
TournamentWin,      // 竞技场
EscortCaravan,      // 护送（已有 CaravanEscort，统一）
```

同步在 `CommissionDef` 静态列表补全 Def，`CommissionQuest.OnStartQuest` 的 switch 加新 case。

#### Step 1.1：`CommissionQuest` 新增"进度模式"枚举

**关键问题**：原 `QuestType` 中有几类任务的进度模式与现有 `CommissionQuest`（去某地→做某事→回来交）不同。

```csharp
/// <summary>
/// 任务进度模式。决定 CommissionQuest.OnStartQuest 如何初始化进度检查。
/// </summary>
public enum CommissionProgressMode
{
    /// <summary>标准：去目标地点 → 执行动作 → 回报（现有模式，不变）</summary>
    GoAndDo,

    /// <summary>累积型：在期限内累计 N 个单位（金钱/物资/杀敌数），不绑定地点</summary>
    Accumulate,

    /// <summary>长期型：在期限内每天 tick 渐进式增长进度（建设类），不绑定地点</summary>
    DailyGrowth,

    /// <summary>多阶段型：任务有 ≥2 个阶段（如外交：到达→谈判→回报结果），每个阶段有独立目标</summary>
    MultiStage,
}
```

```csharp
// CommissionDef 新增字段
public CommissionProgressMode ProgressMode { get; set; } = CommissionProgressMode.GoAndDo;

// CommissionQuest.OnStartQuest 中按模式初始化
switch (_data.Def.ProgressMode)
{
    case CommissionProgressMode.GoAndDo:
        // 现有逻辑：SetMoveGoToSettlement + 等待到达
        break;
    case CommissionProgressMode.Accumulate:
        // 注册 DailyTick → 检查累计值（金钱/杀敌数/物资）
        break;
    case CommissionProgressMode.DailyGrowth:
        // 注册 DailyTick → 每日进度 +1/N → 可加速（投入更多资源）
        break;
    case CommissionProgressMode.MultiStage:
        // 初始化 Stage 0 → 每阶段完成后切换目标 → Stage N 完成 → 回报
        break;
}
```

#### Step 1.2：新 Category 生命周期规格（🔑 关键——不能省略）

每个新 Category 必须明确定义以下 6 个生命周期节点：

| Category | ProgressMode | 目标 | 完成条件 | 超时天数 | 失败后果 | 奖励公式 |
|----------|-------------|------|---------|---------|---------|---------|
| `DeliverGoods` | Accumulate | 累计 N 单位指定物品 | `PlayerInventory.Contains(item, N)` | 15 | 赔款 = 物品价值 × 2 | 固定赏金 + 好感 |
| `CollectDebt` | GoAndDo | 找到欠债人，收钱 | `TransferGold(debtor→player, amount)` 完成 | 10 | 掉关系（领主对玩家） | 债务额 × 20% |
| `Fundraise` | Accumulate | 累计筹集 N 第纳尔 | `Gold >= targetAmount`（只算任务开始后的增量） | 20 | 掉影响力 | 筹款额 × 5% + 好感 |
| `RaidTarget` | GoAndDo | 劫掠目标村庄 | `Village.VillageState == Looted` | 10 | 军事失败惩罚 | 战利品 + 关系变化 |
| `ConquerLocation` | GoAndDo | 占领目标据点 | `Settlement.OwnerClan == PlayerClan` | 30 | 掉影响力 -50 | 封地/影响力 |
| `DevelopSettlement` | DailyGrowth | 据点数值达标 | `Settlement.Prosperity/Food/Security >= target` | 60 | 投资打水漂 | 影响力 + 关系 |
| `DiplomaticMission` | MultiStage | 到达→谈判→回报 | 阶段 0: 到达目标领主处; 阶段 1: 谈判检定通过 | 15 | 外交恶化（原目标反向效果） | 影响力 ± 关系变化 |
| `RecruitCharacter` | GoAndDo | 找到并说服目标 | `TargetHero.Clan == PlayerClan` | 20 | 目标警觉消失 | 影响力 + 好感 |
| `PersuadeLord` | GoAndDo | 说服目标领主改变立场 | 谈判检定通过 | 10 | 目标关系 -20 | 影响力 |
| `Training` | Accumulate | 训练 N 名士兵到指定等级 | 部队中满足条件的士兵 ≥ N | 30 | 无 | 经验 + 好感 |
| `TournamentWin` | GoAndDo | 在指定城镇竞技场获胜 | `TournamentWon && Settlement == target` | 5 | 无 | 赏金 + 声望 |

#### Step 1.3：`IssuerType.Lord`（主命）的接受/拒绝流程

**关键差异**：委托（Commoner/Notable）是 NPC 问"能帮我吗？"→ 玩家可选。主命（Lord）是领主下令——接受/拒绝的 gameplay 逻辑不同。

```csharp
// CommissionIntent 中检测 IssuerType 分支
if (commission.IssuerType == CommissionIssuerType.Lord)
{
    // 主命对话流：
    // NPC: "（威严地）{SpeakerPlayerAddr}，{SpeakerSelfRef}有任务给你。{QuestDesc}"
    // 选项:
    //   [接令] → 正常 StartQuest
    //   [婉拒] → Charm DC 检定 → 成功:轻微好感惩罚(-5) / 失败:重罚(-20关系 + -10影响力)
    //   [推辞——推荐他人] → 推荐一个同伴去执行 → 成功率取决于同伴技能匹配度

    // 超时失败的额外惩罚：
    // 普通委托超时: 赔款/掉关系
    // 主命超时: 赔款 + 掉关系 + 掉影响力 + 可能被剥夺封地（如果 Severity >= 80）
}
```

**主命特有 CommissionDef 字段**：
```csharp
// CommissionDef 新增
public bool IsMandatory { get; set; } = false;     // 领主命令=true，不可拒绝（极少用，推荐 false）
public int RefuseRelationPenalty { get; set; } = 5; // 婉拒成功时的轻微惩罚
public int RefuseFailPenalty { get; set; } = 20;    // 婉拒失败时的重罚
public int RefuseFailInfluencePenalty { get; set; } = 10;
public int TimeoutInfluencePenalty { get; set; } = 20; // 超时额外掉影响力
public int TimeoutFiefRiskSeverity { get; set; } = 80; // Severity >= 此值 → 封地风险
```

#### Step 2：旧代码打 Obsolete（不删，存档兼容）

**文件**：`Quests/QuestManager.cs`

- `QuestType` 枚举 → `[Obsolete]`
- `QuestData` 类 → `[Obsolete]`
- `GenericQuest` 类 → `[Obsolete]`
- `GetQuestDescription()` 的 ~120 行硬编码日本战国字串 → **删**（违反铁律 3），叙事全部走 `NarrativeResolver` → CSV

#### Step 3：叙事迁移到 `PlaceholderResolver` 管道 + CSV 模板

**3.3.1 硬编码字串 → CSV 模板的完整映射**

`GetQuestDescription()` 中当前约 120 行硬编码字串的迁移映射（节选关键条目）：

| 旧代码位置（QuestManager.cs 行号） | 旧文本模式 | QuestType | 新 CSV Key | 占位符 |
|----------------------------------|----------|-----------|-----------|--------|
| DeliverItem_Special | "主公命令你寻找{Item}" | DeliverItem_Special | `QuestDesc_DeliverGoods_Special` | `{QuestTitle}` `{TargetItem}` `{TimeLimit}` |
| DeliverItem_Food | "需在{TimeLimit}天内筹集{Quantity}{Item}" | DeliverItem_Food | `QuestDesc_DeliverGoods_Food` | `{QuestTitle}` `{TimeLimit}` `{Quantity}` `{ItemName}` |
| HuntBandits | "领地附近山贼猖獗，令你{N}天内讨伐" | HuntBandits | `QuestDesc_BountyHunt_Field` | `{QuestTitle}` `{TimeLimit}` `{EnemyType}` |
| CaptureSetlement | "令你攻打{Settlement}，期限{TimeLimit}" | CaptureSetlement | `QuestDesc_ConquerLocation` | `{QuestTitle}` `{TargetSettlement}` `{TimeLimit}` |
| DiplomacyTalk_War | "遣你为使者前往{TargetKingdom}宣战" | DiplomacyTalk_War | `QuestDesc_DiplomaticMission_War` | `{QuestTitle}` `{TargetKingdom}` `{TargetLord}` |
| DevelopSettlement_Food | "命你{N}天内开发{Settlement}新田" | DevelopSettlement_Food | `QuestDesc_DevelopSettlement_Food` | `{QuestTitle}` `{TimeLimit}` `{TargetSettlement}` |
| … | … | … | … | … |

**完整映射表建议作为独立附件**（如 `plans/quest-narrative-migration.csv`），不在本计划中展开全部 120 行。

**3.3.2 CSV 模板表设计**

**新文件**：`ModuleData/DesignData/QuestNarratives.csv`

| 列名 | 类型 | 说明 | 示例 |
|------|------|------|------|
| `Key` | string | 唯一标识：`{Category}_{IssuerType}_{Phase}` | `DeliverGoods_Lord_Offer` |
| `Phase` | string | Offer / Accept / InProgress / Complete / Timeout / Refuse | `Offer` |
| `Category` | string | CommissionCategory 名 | `DeliverGoods` |
| `IssuerType` | string | Commoner / Notable / Lord | `Lord` |
| `NpcLine_Template` | string | NPC 台词模板（含占位符） | `"{SpeakerSelfRef}命你筹集{Quantity}单位{ItemName}，期限{TimeLimit}天。"` |
| `PlayerOption_Accept` | string | 玩家接受选项文本 | `"领命"` |
| `PlayerOption_Refuse` | string | 玩家拒绝选项文本 | `"请主公另寻他人"` |
| `PlayerOption_Refuse_Hint` | string | 拒绝选项的 tooltip | `"婉拒主命（需 Charm DC {SkillCheckDC} 否则掉关系 -{RefuseFailPenalty}）"` |
| `PlayerOption_Accept_Hint` | string | 接受选项的 tooltip | `"接受任务，期限{TimeLimit}天。奖励：{RewardGold}第纳尔 + {RewardRelation}好感"` |
| `Journal_Description` | string | 任务日志描述模板 | `"{GiverName}命令我筹集{Quantity}单位{ItemName}，期限{TimeLimit}天。"` |
| `Journal_Complete` | string | 完成时的日志 | `"我按时筹集了{Quantity}单位{ItemName}，{GiverName}很满意。"` |
| `Journal_Timeout` | string | 超时时的日志 | `"我没能在{TimeLimit}天内完成{ItemName}的筹集，{GiverName}大发雷霆。"` |

**运行时匹配逻辑**：
```csharp
// NarrativeResolver 中新增方法
public static string ResolveQuestNarrative(CommissionData data, string phase)
{
    string key = $"{data.Category}_{data.IssuerType}_{phase}";
    var row = GameDatabase.QuestNarratives?.GetByID(key);
    if (row != null)
        return PlaceholderResolver.Resolve(row.GetString("NpcLine_Template"), BuildQuestContext(data));

    // 兜底：通用模板（去掉 IssuerType）
    key = $"{data.Category}_Generic_{phase}";
    row = GameDatabase.QuestNarratives?.GetByID(key);
    if (row != null)
        return PlaceholderResolver.Resolve(row.GetString("NpcLine_Template"), BuildQuestContext(data));

    // 最后兜底：硬编码最小模板（仅占位符拼接，无风味）
    return $"{data.Def.DisplayName}: {data.Description}";
}
```

**3.3.3 新增占位符（任务叙事专用）**

| 占位符 | C# 查询来源 | 示例值 |
|--------|------------|--------|
| `{QuestTitle}` | `CommissionData.Def.DisplayName` | "筹集军粮" |
| `{GiverName}` | `QuestGiver.Name.ToString()` | "织田信长" |
| `{GiverIdentity}` | `GetSocialIdentity(QuestGiver)` | "领主" / "村长" |
| `{RewardGold}` | `CommissionData.NegotiatedReward` | "500" |
| `{RewardRelation}` | `CommissionData.Def.RelationReward` | "5" |
| `{TimeLimit}` | `CommissionData.TimeLimitDays` | "15" |
| `{TargetName}` | `TargetHero?.Name?.ToString()` | "山贼头目" |
| `{TargetSettlement}` | `TargetSettlement?.Name?.ToString()` | "青木村" |
| `{TargetItem}` | `MBObjectManager.Instance.GetObject<ItemObject>(data.TargetItemId)?.Name?.ToString()` | "铁炮" |
| `{Quantity}` | `CommissionData.TargetQuantity` | "50" |
| `{EnemyType}` | `data.Def.EnemyPartyTemplate?.Name?.ToString()` | "山贼" |
| `{IssuerTitle}` | `data.IssuerType switch { Lord → "主公", Notable → "委托人", Commoner → "" }` | "主公" |

#### Step 4：`RequestCommissionIntent` 统一检测

**文件**：`Quests/Commissions/CommissionIntent.cs`

已在 v1 重写为检测原版 Issue → `StartIssueQuest`。扩展为也检测待下发的领主命令（`CommissionIssuerType.Lord`），统一走同一个 StoryDialog 流。

### 3.3 Part 3 文件变更清单

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ 修改 | `Quests/Commissions/CommissionData.cs` | `CommissionCategory` 扩展 + `CommissionIssuerType` 枚举 + `CommissionProgressMode` 枚举 + `CommissionDef` 补全（含新字段：IsMandatory/RefusePenalty/TimeoutInfluencePenalty/ProgressMode） |
| ✏️ 修改 | `Quests/Commissions/CommissionQuest.cs` | 新 Category 的 `OnStartQuest` + `RegisterEvents`（含 Accumulate/DailyGrowth/MultiStage 三种进度模式）；`OnCompleteQuest` 补充奖励结算 |
| ✏️ 修改 | `Quests/QuestManager.cs` | `QuestType`/`QuestData`/`GenericQuest` 打 `[Obsolete]`；删 `GetQuestDescription()` 硬编码战国字串 |
| ✏️ 修改 | `Quests/Commissions/CommissionIntent.cs` | 统一检测委托 + 主命；主命对话流（接令/婉拒/推辞）+ IssuerType.Lord 特殊处理 |
| ➕ 新建 | `ModuleData/DesignData/QuestNarratives.csv` | 任务叙事模板表（按 Category + IssuerType + Phase 匹配），含全部 11 个新 Category 的 Offer/Accept/InProgress/Complete/Timeout/Refuse 六个阶段 |
| ➕ 新建 | `plans/quest-narrative-migration.csv` | 旧 120 行硬编码字串 → CSV Key 完整映射表（独立附件，实施时逐行对照迁移） |

---

## Part 4: 谈判系统 — 未来方向（仅文档，不动代码）

当前不碰。原因：`NegotiationCard`/`SkillCheckOption`/`NegotiationState` 是"战术执行层"，意图系统是"战略目标层"——边界清晰，改动风险高。

### 未来调整记录

1. **`NegotiationGoalType` 双向化**：当前已是双向设计（`NegotiationState` 构造函数接收 Goal 作为参数，无论来自玩家意图还是 NPC 意图），不需要改
2. **`NegotiationTactic` NPC 侧使用**：NPC 在谈判中选 Tactic 只需在 NPC 意图的 `GetSuccessConsequences` 中构建对应的 `Consequence` 即可，不需要改谈判系统本身
3. **`SkillCheckOption` vs `NegotiationCard` 统一**：长期可考虑统一为 `NegotiationMove` 类（分 `SkillCheckPhase` / `BargainingPhase` 子类），当前不合并
4. **`Goal2Info` 字典补全**：只注册了 5/15 个 Goal，补全时参考 `NegotiationRegistry` 现有格式

---

## Part 5: 迁移步骤

### Phase A（基础层，预计改动最大，先做）
1. `IntentBase` 加 `IntentSource` + `Source` — 零风险（默认 Both）
2. `IntentRegistry` 加 `GetNpcInitiatives()` — 零风险（新方法）
3. `ConsequenceExecutor` 新建 — 零风险（新文件，尚无调用方）
4. `IntentBase` 加 `GetXxxConsequences` 虚方法 + 旧方法打 Obsolete — 零风险（基类默认实现 fallback）

### Phase B（NPC 平权）
5. `AccountabilityOptionType` 删 + `InteractionOptionType` 扩展 — 中等风险（改枚举值引用，约 19 处）
6. 新建 `NpcInitiativeIntents.cs` — 零风险（新文件）
7. `AgentBrain.ReceiveEvent` 改造 — **高风险**（事件分发核心路径，`MatchesEvent` 匹配逻辑需充分测试 7 种事件类型；分三步：①加新路径不删旧 → ②逐事件类型切换 → ③全部切换后删旧 if/else）
8. `PrepareOpeningAction` 新构造函数 — 中等风险（调用方有限，`PendingConflict` 数据路径需验证）

### Phase C（任务统一）
9. `CommissionCategory` 扩展 + `CommissionIssuerType` + `CommissionProgressMode` — 低风险（枚举新增）
10. `QuestType`/`GenericQuest` 打 Obsolete — 零风险（存档兼容）
11. 硬编码字串迁移到 CSV — **中风险**（120 行迁移，每行需对照 `quest-narrative-migration.csv` 逐条验证，遗漏会导致 NPC 说空字符串）
12. `CommissionQuest` 新 Category 方法 — **高风险**（11 个新 Category 各含独立生命周期；`ConquerLocation`/`DiplomaticMission`/`DevelopSettlement` 三种特殊模式需额外事件注册和多阶段支持；建议先实现 3 个简单 Category 验证框架 → 再补完其余）

### Phase D（清理）
13. 各 IntentBase 子类逐个迁移到 Consequence 模式
14. `ActionHandler` → 删除，`GetActionSpacePrompt` → 替换
15. `NpcInitiative` 类删除
16. `wheels.md` 更新

---

## 验证

### Part 1 验证
1. 编译通过（v1.2.12 + Latest 双版本）
2. 跟任意 NPC 对话 → 玩家菜单正常（`GetVisible` 未破坏）
3. 犯罪事件 → 守卫拦截 → 开场白正常（`GuardInterceptIntent`）
4. NPC 主动打招呼 → `GreetingIntent` 正常
5. 7 种 `TriggerEvents` 事件类型逐一触发验证（ComeHere / order_follow / order_attack / event_agent_damaged / WitnessCrime_GatherOnLook / WitnessCrime_StayStare / event_agent_knocked_out）
6. `AgentBrain.MatchesEvent` 对不匹配事件正确跳过（不做无响应/不停顿）

### Part 2 验证
1. 编译通过（v1.2.12 + Latest 双版本）
2. 任意意图 `OnSuccess`/`OnFail` → ConsequenceExecutor 执行日志正确（`DebugLogger.Log` 可见 `[ConsequenceExecutor]` 前缀）
3. LLM 路径 `npc_action` → 映射到 ConsequenceType → 执行正确
4. `GetConsequenceSpacePrompt` → LLM prompt 中后果列表含 `LlmDescription` + `LlmParamHint`
5. **关键后果异常**（如玩家在 TransferGold 执行中途变 null）→ 异常抛出到 `DispatchIntent`，不会静默半截状态
6. **Debug 编译** → `[IRON_RULE_4_VIOLATION]` 日志在裸调 `GiveGoldAction` 等禁止 API 时出现

### Part 3 验证
1. 编译通过（v1.2.12 + Latest 双版本）
2. 领主接主命 → CommissionQuest（IssuerType.Lord）完整生命周期：接令→执行→回报→奖励
3. 领主主命拒绝 → 婉拒检定 + 惩罚生效
4. 平民接委托 → CommissionQuest（IssuerType.Commoner）正常
5. 旧存档 GenericQuest 加载不崩（读档后 `[Obsolete]` 标注的类仍能反序列化）
6. CSV 叙事模板匹配正确（11 个新 Category × 3 种 IssuerType × 6 个 Phase = 198 条模板全部命中）
7. CSV 未命中时兜底硬编码模板生效（不含战国 flavor，信息完整）
8. `ConquerLocation` / `DiplomaticMission` / `DevelopSettlement` 三种特殊 ProgressMode 完整生命周期测试

### 🔴 存档回归测试（每次 Phase 结束时必做）

> **原则**：重构不能坏存档。以下测试用例覆盖关键状态迁移场景。

| 测试编号 | 旧存档场景 | 操作 | 预期结果 |
|---------|-----------|------|---------|
| S1 | 玩家有一个进行中的 `GenericQuest` | 读档 → 打开任务日志 | Quest 显示正常，文本不走 CSV（走旧 `GetQuestDescription`），`[Obsolete]` 不崩 |
| S2 | 玩家有一个进行中的 `CommissionQuest`（旧 Category） | 读档 → 继续执行 → 完成 | 旧 Category 枚举值仍被识别（int 序列化），生命周期正常 |
| S3 | 玩家与某 NPC 有 `IntentCooldownStore` 记录 | 读档 → 找该 NPC → 检查冷却 | 冷却数据不丢失，`Goal` 枚举值升级后仍正确匹配 |
| S4 | 玩家有未了结的犯罪事件（`WorldEvent`） | 读档 → 进入村庄 → 守卫拦截 | `GuardInterceptIntent` 正确触发，`PendingConflict` 数据完整 |
| S5 | 旧 NPC 意图（`NpcInitiative`）正在进行中 | 读档 → NPC 完成行为 | `[Obsolete]` 旧路径仍工作，NPC 不会卡在半截状态 |
| S6 | 玩家已接领主主命（旧 `QuestType` 枚举） | 读档 → 完成主命 → 回报 | 主命完成逻辑不依赖 `QuestType` 枚举（已迁移到 `CommissionCategory`） |

**测试策略**：每次 Phase 完成后，用包含上述场景的旧存档读档验证。Phase C 后额外测试 S6（主命迁移验证）。

---

## 不在此次重构范围内的

- `IAtomicAction` 体系 — Mission 层原子动作，保持现状
- 谈判系统 — 见 Part 4
- 世界事件系统 — 已相对独立
- `TaikouContent` 内容包 — 纯数据包，无需改

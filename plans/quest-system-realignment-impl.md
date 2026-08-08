# Quest 系统重新定位 — 实施方案（保守版）

> **源文件**: `plans/quest-system-realignment.md`
> **核心策略**: 旧代码保留编译但不执行，新代码并行添加。不删文件，不删枚举值。
>
> **状态更新**: 2026-06-24 — Phase 1 + Phase 2 已完整实施 ✅
> - Phase 1.1~1.4: 全部落地
> - Phase 1.5: 按计划延后
> - Phase 2.1~2.5: 全部落地
> - Phase 2.6: 按计划延后
> - Phase 3~4: 延后

---

## 背景

当前 CommissionQuest 系统与原版 40 种 Issue **完全并行、互相不知晓**。新架构改为**融入原版**：

```
原版 IssuesCampaignBehavior（链首，保留不动）
  → 40 种 Issue → 原版 Quest → 玩家完成
       ↓
QuestConsequenceResolver（我们新增）
  → 查因果表 → 生成后续 WorldEvent → SyncEventToNpcMemory
       ↓
下一个 Tick：IssuesCampaignBehavior 扫描此 NPC
  → NPC 有 CurrentUrgentEvent → Harmony 过滤日常类 Issue
  → 玩家接取关联类 Issue → 循环
```

**旧代码处理原则**：不删除任何文件。`CommissionQuest.cs`、`CommissionGenerator.cs`、`CommissionHubIssue.cs` 等全部保留编译，但切断调用路径使其不再执行。等新系统跑稳后再清理。

---

## Phase 1：切断旧调用 + 建立新桥接

### 1.1 停用 CommissionIssueBehavior（不再注册 ! 信号）

**文件**: `Core/MySubModule.cs` 约第 149 行

```csharp
// 删除这行（不再注册旧的信号 Issue 行为）
// campaignGameStarter.AddBehavior(new CommissionIssueBehavior());

// 新增：事件感知的 Issue 过滤行为
campaignGameStarter.AddBehavior(new IssueFilterBehavior());
```

**效果**：旧 `CommissionHubIssue` 不再被创建，NPC 不再通过旧路径显示 `!`。原版 `IssuesCampaignBehavior` 接管全部 `!` 标记。

### 1.2 新增 Harmony 过滤：紧急事件期间阻止无关 Issue

**新文件**: `Quests/IssueFilterPatch.cs`

**新文件**: `Quests/IssueFilterBehavior.cs`（CampaignBehavior，负责注册 patch 和加载映射表）

Harmony prefix 挂在 `IssueManager.AddPotentialIssueData(Hero hero, PotentialIssueData pid)`：

```
入参: hero, pid（其中 pid.IssueType 是具体 IssueBase 子类的 Type）
逻辑:
  1. 读 mem = AllNpcMemoryManager.GetMemory(hero.StringId)
  2. 若 mem?.CurrentUrgentEvent == null → 放行（return true）
  3. 若有紧迫事件 → 查映射表：当前 IssueType 与该 EventType 是否兼容
  4. 兼容 → 放行 / 不兼容 → return false（跳过原始方法）
```

**映射表结构**（`Dictionary<WorldEventType, HashSet<Type>>`，只记"阻止"的）：

```csharp
// EventType → 被阻止的 Issue 类型（日常经营类等与危机无关的）
BanditRaid → { HeadmanNeedsGrainIssue, VillageNeedsToolsIssue, 
              HeadmanNeedsToDeliverAHerdIssue, VillageNeedsDraughtAnimalsIssue,
              VillageNeedsCraftingMaterialsIssue, LandlordNeedsAccessToVillageCommonsIssue,
              LandLordNeedsManualLaborersIssue, LandLordTheArtOfTheTradeIssue,
              LandlordTrainingForRetainersIssue, ArtisanCantSellProductsAtAFairPriceIssue,
              ArtisanOverpricedGoodsIssue }
NobleConflict → { LordNeedsHorsesIssue, LordsNeedsTutorIssue, LadysKnightOutIssue,
                  ProdigalSonIssue }
Famine → { HeadmanNeedsToDeliverAHerdIssue, LandlordTrainingForRetainersIssue,
           BettingFraudIssue }
// 其他 EventType 暂不限制（全部放行）
```

**日志**：每个 DailyTick 最多输出一条汇总：
```
[QuestFilter] BanditRaid@雷别莱特村: 阻止了 3 种日常Issue（HeadmanNeedsGrain等），保留了 ExtortionByDeserters
```

### 1.3 重写 CommissionIntent — 闲聊中接原版 Quest

**文件**: `Quests/Commissions/CommissionIntent.cs`

**核心设计**：同一个原版 Quest，两条路径都能接取：
- **路径 A**：原版对话 "Is there anything I can do for you?" → 原版 TextObject 模板 → 原版 Quest
- **路径 B**：StoryDialog "【闲聊】" → `RequestCommissionIntent` → CSV/LLM 叙事包装 → **同一个原版 Quest**

**已验证的 API**（经 ilspycmd 反编译确认，均为公开属性/方法）：

```csharp
// ── 获取原版对话文本 ──
var issue = hero.Issue;  // IssueBase 实例（null = 无 Issue）
issue.IssueBriefByIssueGiver                   // NPC 简短介绍（TextObject）
issue.IssueQuestSolutionExplanationByIssueGiver // NPC 详细解释任务（TextObject，核心！）
issue.IssueQuestSolutionAcceptByPlayer          // 玩家确认接取（TextObject）
issue.IssueAcceptByPlayer                       // 玩家表示感兴趣（TextObject）
issue.Title                                     // 任务标题
issue.Description                               // 任务描述

// ── 启动原版 Quest ──
Campaign.Current.IssueManager.StartIssueQuest(hero);
// 内部调 issue.StartIssueWithQuest() → 创建 QuestBase → Quest 进入 Journal
```

**`RequestCommissionIntent`**（保留，重写核心逻辑）:

`Evaluate()`:
```csharp
// 不再调 CommissionGenerator.GenerateCommissions()
// 检查 ctx.Hero.Issue != null → Show
// 或有 CurrentUrgentEvent → Show
// 都没有 → Hide
```

`OnInstant()` — **在 StoryDialog 中直接接原版 Quest**:
```csharp
var issue = ctx.Hero.Issue;
if (issue != null)
{
    // 1. 读原版文本（用于叙事生成 + fallback）
    string vanillaBrief = issue.IssueBriefByIssueGiver?.ToString();
    string vanillaExplanation = issue.IssueQuestSolutionExplanationByIssueGiver?.ToString();
    string questTitle = issue.Title?.ToString();
    string issueTypeName = issue.GetType().Name; // e.g. "HeadmanNeedsGrainIssue"
    
    // 2. 叙事来源（三层 fallback）：
    //    ① 查 NarrativeResolver → CSV 模板（按 issueTypeName + NPC性格 匹配）
    //    ② CSV 未命中 + IsLLMConfigured → LLM 生成（prompt 含 vanillaExplanation）
    //    ③ 都不可用 → SceneSay 直接展示 vanillaExplanation（原版 TextObject 兜底）
    
    // 3. 展示选项：【接取】/【拒绝】
    //    接取 → Campaign.Current.IssueManager.StartIssueQuest(ctx.Hero);
    //    拒绝 → 关闭对话
}
else if (ctx.HasUrgentWorldEvent)
{
    // NPC 有紧迫事件但无 Issue → 描述困境，暂无 Quest 可接
}
```

**`ConfirmCommissionIntent`**（整个类）：
- 不再注册（在 IntentRegistry 中注释掉），类保留编译

**`CollectCommissionRewardIntent`**（保留，泛化）：
- `Evaluate()` 改为检查任意已完成的 Quest 是否未领报酬
- `OnInstant()`: 展示报酬叙事（CSV/LLM），完成后调 Quest 结算

### 1.4 更新 IntentRegistry

**文件**: `Interaction/Intents/IntentRegistry.cs` 约第 45-47 行

```csharp
// 旧代码注释掉，保留可编译：
// Register(new ConfirmCommissionIntent());  // [已废弃] 原版无两段式流程

// RequestCommissionIntent 和 CollectCommissionRewardIntent 保留
// 但类内部已重写（见 1.3）
```

### 1.5（延后）WorldEvent 战略一致性检查 + NobleConflict Army + WorldEventConfig 清理

> **全部延后到二期**。一期聚焦因果事件链，这三项暂时不动：
> - 战略一致性检查（`WorldEventSimulator.CheckStrategicConsistency`）
> - NobleConflict 走原版 Army（`SpawnEventParty` 分叉 + `WorldEventData.ArmyId`）
> - `WorldEventConfig` 中 commission 数组清理

---

## Phase 2：因果引擎（一期重点）

### 2.1 QuestConsequenceResolver

**新文件**: `Quests/Causality/QuestConsequenceResolver.cs`

```csharp
public static class QuestConsequenceResolver
{
    public enum QuestCompletionOutcome { Success, Fail, Betrayal, Timeout, Cancel }

    public struct FollowUpQuest
    {
        public string QuestId;           // VANILLA_* / LWNPCS_*
        public int DelayDaysMin, DelayDaysMax;
        public float Probability;        // 0~1
        public bool RequireTargetAlive;
        public bool RequireTargetDead;
        public bool RequireGiverAlive;
        public int? MinInfamy;
    }

    // 从 JSON 加载
    private static Dictionary<string, 
        Dictionary<QuestCompletionOutcome, List<FollowUpQuest>>> _causalityTable;

    public static void LoadFromJson(string path) { ... }

    public static void ResolveConsequences(
        string questId,
        QuestCompletionOutcome outcome,
        string outcomeDetail,
        Hero questGiver,
        Hero targetHero,
        Settlement targetSettlement)
    {
        // 1. 查因果表
        if (!_causalityTable.TryGetValue(questId, out var outcomeDict)) return;
        if (!outcomeDict.TryGetValue(outcome, out var followUps)) return;

        // 2. 逐个检查条件 + 执行
        int scheduled = 0;
        foreach (var fu in followUps)
        {
            if (!CheckConditions(fu, targetHero, questGiver)) continue;
            if (MBRandom.RandomFloat > fu.Probability) continue;

            ScheduleFollowUp(fu, questGiver, targetHero, targetSettlement);
            scheduled++;
        }

        if (scheduled > 0)
            DebugLogger.Log($"[QuestConsequence] {questId} + {outcome} → 排入 {scheduled} 个后续");
    }
}
```

### 2.2 QuestConsequenceBehavior

**新文件**: `Quests/Causality/QuestConsequenceBehavior.cs`

```csharp
public class QuestConsequenceBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.QuestCompletedEvent.AddNonSerializedListener(this, OnQuestCompleted);
    }

    private void OnQuestCompleted(QuestBase quest, QuestBase.QuestCompleteDetails detail)
    {
        // 1. 映射原版 Quest → VANILLA_* ID
        string questId = VanillaQuestMapping.MapQuestToId(quest);
        if (questId == null) return; // 不是我们关注的 Quest 类型

        // 2. 映射完成方式
        var outcome = VanillaQuestMapping.MapCompletionDetail(detail);

        // 3. 检测活捉 vs 击杀
        string outcomeDetail = "";
        if (quest.TargetHero != null)
        {
            if (quest.TargetHero.IsPrisoner) outcomeDetail = "Capture";
            else if (!quest.TargetHero.IsAlive) outcomeDetail = "Kill";
        }

        // 4. 调因果引擎
        QuestConsequenceResolver.ResolveConsequences(
            questId, outcome, outcomeDetail,
            quest.QuestGiver, quest.TargetHero, quest.TargetSettlement);
    }
}
```

注册到 `MySubModule.cs`：`campaignGameStarter.AddBehavior(new QuestConsequenceBehavior());`

### 2.3 因果链 JSON

**新文件**: `ModuleData/DesignData/causality_chains.json`

首发覆盖链 1-10（最关键 10 条），链 11-14 后续补。结构与设计文档一致。

### 2.4 原版 Quest 类型映射

**新文件**: `Quests/Causality/VanillaQuestMapping.cs`

```csharp
public static class VanillaQuestMapping
{
    // IssueBase 子类 Type → 我们的 VANILLA_* ID
    private static readonly Dictionary<Type, string> IssueTypeToId = new()
    {
        { typeof(HeadmanNeedsGrainIssue), "VANILLA_HeadmanNeedsGrain" },
        { typeof(NearbyBanditBaseIssue), "VANILLA_NearbyBanditBase" },
        // ... 共 40 个（按 quest-system-realignment.md 第四章全集）
    };

    public static string MapQuestToId(QuestBase quest) { ... }
    public static QuestConsequenceResolver.QuestCompletionOutcome MapCompletionDetail(...) { ... }
}
```

### 2.5 因果上下文注入对话

**关键**：后续 NPC 的对话里能看出"因为之前…所以现在…"，需要因果引擎在生成后续事件时写入 NPC 记忆。

**实现**：`QuestConsequenceResolver.ResolveConsequences()` 中，对每个排入的后续事件：

```csharp
// 1. 创建 WorldEvent（物理层：spawn party / 调战略参数）
var worldEvent = new WorldEventData { ... };
WorldEventDatabase.AddEvent(worldEvent);
// AddEvent 内部调 SyncEventToNpcMemory → 设置 CurrentUrgentEvent

// 2. 写入因果记忆（NPC 的 RecentHistory）— 这是对话因果上下文的数据源
foreach (var affectedHero in GetAffectedHeroes(followUp, worldEvent))
{
    var mem = AllNpcMemoryManager.GetMemory(affectedHero.StringId);
    // 记录：是谁的什么行为导致了现在的局面
    mem.AddRecentHistory(new SocialEvent
    {
        Type = "CausalityContext",
        Summary = $"玩家完成了{sourceQuestId}，导致{followUp.QuestId}",
        RelatedHeroId = player.StringId,
        RelatedEventId = worldEvent.EventId,
        ChainDepth = currentDepth + 1,
        PreviousQuestId = sourceQuestId,
        CauseHeroId = instigatorHero?.StringId,
        CauseEventType = worldEvent.EventType.ToString(),
    });
}
```

**对话时注入**：`RequestCommissionIntent.OnInstant()` 叙事生成时，从 `mem.RecentHistory` 提取最近的 `CausalityContext`，填充变量：

| 变量 | 来源 | 示例值 |
|------|------|--------|
| `{PREVIOUS_QUEST}` | mem.RecentHistory[].PreviousQuestId | "清剿匪穴" |
| `{CAUSE_HERO}` | mem.RecentHistory[].CauseHeroId → Hero.Name | "德瑟特·哈米尔" |
| `{CAUSE_EVENT}` | mem.RecentHistory[].CauseEventType | "匪患劫掠" |
| `{CHAIN_DEPTH}` | mem.RecentHistory[].ChainDepth | 2 |

CSV 模板示例（带因果变量）：
```
{CHAIN_DEPTH > 1}上次你帮我{ PREVIOUS_QUEST }之后，{CAUSE_HERO}的{CAUSE_EVENT}引发了现在的局面。
现在我需要你{ QUEST_DESC }。{/CHAIN_DEPTH}
```

NPC 实际说出（填充后）：
> "上次你帮我清剿了匪穴之后，逃散的匪帮聚集起来开始劫掠商队。现在我需要你护送我的货物去城里。"

### 2.6（延后）口碑传播

> **延后**。效果是"完成委托后同定居点名人关系微调"，后台数值变化，玩家难以直接感知。因果链核心体验不依赖它。等后续有更明显的口碑表现形式（如 NPC 主动提及"听说你帮某某做了某事"）再一起做。

---

## Phase 3-4（延后）

- PrisonBreak/Theft/Scavenge 实现
- CSV/LLM 叙事统一
- 旧代码最终清理（新系统跑稳后）

---

## 文件变更总览

### 修改的文件
| 文件 | 改动 |
|------|------|
| `Core/MySubModule.cs` | 停用 CommissionIssueBehavior，注册 IssueFilterBehavior + QuestConsequenceBehavior |
| `Quests/Commissions/CommissionIntent.cs` | 重写 RequestCommissionIntent 和 CollectCommissionRewardIntent；ConfirmCommissionIntent 标记废弃 |
| `Interaction/Intents/IntentRegistry.cs` | 注释掉 ConfirmCommissionIntent 注册 |

### 新增的文件
| 文件 | 用途 |
|------|------|
| `Quests/IssueFilterPatch.cs` | Harmony prefix on AddPotentialIssueData |
| `Quests/IssueFilterBehavior.cs` | CampaignBehavior 注册 filter + 加载映射表 |
| `Quests/Causality/QuestConsequenceResolver.cs` | JSON 驱动的因果引擎 |
| `Quests/Causality/QuestConsequenceBehavior.cs` | CampaignBehavior 监听 QuestCompleted + DailyTick 处理延迟 Issue |
| `Quests/Causality/VanillaQuestMapping.cs` | 原版 Issue 类型 → VANILLA_* ID 映射 |
| `Quests/Causality/IssueFactory.cs` | 🆕 统一反射构造原版 Issue，桥接 ScheduleIssue → CreateNewIssue |
| `Quests/Causality/ReputationPropagation.cs` | 口碑传播（延后） |
| `ModuleData/DesignData/causality_chains.json` | 因果链配置 |

### 不删除、不动、保留编译的文件
- `Quests/Commissions/CommissionQuest.cs` — 不再实例化，保留编译
- `Quests/Commissions/CommissionGenerator.cs` — 不再调用，保留编译
- `Quests/Commissions/CommissionHubIssue.cs` — 不再注册，保留编译
- `Quests/Commissions/CommissionData.cs` — 不再使用，保留编译
- `Quests/Commissions/ComplicationTable.cs` — 保留编译（可复用模式）
- `Quests/Commissions/JourneyEvents.cs` — 保留编译（可复用模式）
- `Quests/Commissions/CommissionNarrative.cs` — 保留编译
- `Quests/Commissions/TrustSystem.cs` — 保留编译（因果引擎会用）
- `Quests/Commissions/InfamySystem.cs` — 保留编译（因果引擎会用）
- `ModuleData/DesignData/Narrative.csv` — 不动
- `Interaction/Intents/NarrativeResolver.cs` — 不动

---

## 日志策略

1. **只记关键节点，严禁 per-NPC 循环日志**
2. **每个 DailyTick 最多一条汇总**
3. **日志前缀规范**：

| 前缀 | 含义 | 记录时机 |
|------|------|---------|
| `[QuestFilter]` | Issue 过滤决策 | DailyTick 汇总：阻止了多少 Issue |
| `[QuestConsequence]` | 因果链触发 | 源 Quest 完成 → 后续排入 |

4. **不记录**：每次 Evaluate()、OnCheckForIssue、因果表加载成功、映射成功

---

## 实施状态（2026-06-24 核实）

### Phase 1：切断旧调用 + 建立新桥接

| 子项 | 文件 | 状态 |
|------|------|:---:|
| 1.1 停用 CommissionIssueBehavior | `Core/MySubModule.cs:149` | ✅ `// campaignGameStarter.AddBehavior(new CommissionIssueBehavior())` |
| 1.1 注册 IssueFilterBehavior | `Core/MySubModule.cs:151` | ✅ `campaignGameStarter.AddBehavior(new IssueFilterBehavior())` |
| 1.1 注册 QuestConsequenceBehavior | `Core/MySubModule.cs:154` | ✅ `campaignGameStarter.AddBehavior(new QuestConsequenceBehavior())` |
| 1.2 Harmony prefix on AddPotentialIssueData | `Quests/IssueFilterPatch.cs` | ✅ 拦截紧急事件期间不兼容的 Issue 类型 |
| 1.2 Issue 过滤映射表 + 日志汇总 | `Quests/IssueFilterBehavior.cs` | ✅ BanditRaid(11种)/NobleConflict(4种)/Famine(3种) 阻止列表 |
| 1.3 CommissionIntent 重写 | `Quests/Commissions/CommissionIntent.cs` | ✅ RequestCommissionIntent 接入原版 Quest |
| 1.3 ConfirmCommissionIntent 废弃 | `Quests/Commissions/CommissionIntent.cs` | ✅ 标记 `[Obsolete]`，Evaluate 返回 Hide |
| 1.4 IntentRegistry 更新 | `Interaction/Intents/IntentRegistry.cs:46` | ✅ `// Register(new ConfirmCommissionIntent())` |
| 1.5 战略一致性检查 | — | ⏸️ 按计划延后 |

### Phase 2：因果引擎

| 子项 | 文件 | 状态 |
|------|------|:---:|
| 2.1 QuestConsequenceResolver | `Quests/Causality/QuestConsequenceResolver.cs` | ✅ 因果引擎，2 种 action：ScheduleIssue + Suppress（BoostWeight 已废弃） |
| 2.2 QuestConsequenceBehavior | `Quests/Causality/QuestConsequenceBehavior.cs` | ✅ 监听 `OnQuestCompletedEvent`，DailyTick 处理延迟 Issue |
| 2.3 因果链 JSON | `ModuleData/DesignData/causality_chains.json` | ✅ BoostWeight 已全改为 ScheduleIssue / Suppress |
| 2.4 VanillaQuestMapping | `Quests/Causality/VanillaQuestMapping.cs` | ✅ 40 种 Issue 类型名 → VANILLA_* ID |
| ~~2.5 因果上下文注入对话~~ | — | ⏸️ 已在 CommissionIntent 中实现，无独立文件 |
| 2.6 IssueFactory（ScheduleIssue 桥接） | `Quests/Causality/IssueFactory.cs` | ✅ **新增**：统一反射构造原版 Issue，对接 CreateNewIssue |
| 2.7 延迟 Issue 队列 | `QuestConsequenceResolver.PendingIssues` + `QuestConsequenceBehavior.OnDailyTick` | ✅ DailyTick 检查到期 Issue 并调用 IssueFactory 创建 |
| 2.8 口碑传播 | — | ⏸️ 按计划延后 |

### Phase 3-4

| 子项 | 状态 |
|------|:---:|
| PrisonBreak/Theft/Scavenge | ⏸️ 延后 |
| CSV/LLM 叙事统一 | ⏸️ 延后 |
| 旧代码清理 | ⏸️ 延后（新系统跑稳后） |

### 已知差距

> **2026-06-24 更新**：ScheduleIssue 和 Suppress 两路已全部桥接完毕。BoostWeight 已废弃移除。
>
> 现在两个 action 都能实际影响 Issue 生成：
> - **ScheduleIssue** → `IssueFactory.CreateVanillaIssue()` → `IssueManager.CreateNewIssue()` → NPC 头上出 `!`
> - **Suppress** → `IssueFilterBehavior.RegisterSuppression()` → `IssueFilterPatch.IsIssueSuppressed()` → 拦截 `AddPotentialIssueData`
>
> 无剩余差距。

---

## 测试验证

### 测试 1：编译
```bash
dotnet build -c Debug
dotnet build -c Debug_v1.2.12
```
**预期**: 两个配置均编译通过，无 error，warning 只有旧代码的 unreachable code 提示。

### 测试 2：原版 Quest 不受影响
1. 新战役，去任意村庄找有 `!` 的 NPC
2. 用原版对话 "Is there anything I can do for you?" 接任务
3. **预期**: DialogFlow 正常，Quest 正常接取/追踪/完成，日志无异常

### 测试 3：StoryDialog 闲聊中接原版 Quest
1. 去任意村庄找有 `!` 的 NPC（如村长有 HeadmanNeedsGrain）
2. 用 StoryDialog 互动键开始对话 → 选 "【找工作】打听委托"
3. **预期**: 
   - NPC 用 CSV 叙事文本（或 LLM 生成）描述委托内容（而非原版 TextObject 模板）
   - 选项中出现 `【接取】` 和 `【拒绝】`
   - 点接取 → 原版 Quest 正常启动，出现在 Journal 中
4. 对比测试：同一个 NPC 用原版对话 "Is there anything I can do for you?" 也能接同一个 Quest
5. **验证**: 两条路径接到的是同一个 Quest 类型，JournalLog 一致

### 测试 4：紧急事件期间 Issue 过滤
1. 控制台：`custom.worldevent_force BanditRaid`
2. 看目标村庄村长的 `!`
3. **预期**: 只显示 `ExtortionByDeserters` 等关联 Issue，不显示 `HeadmanNeedsGrain` 等日常类
4. 日志：`[QuestFilter]` 一条汇总

### 测试 5：因果链 — 端到端

**场景**：完成 `NearbyBanditBase`（清剿匪穴）→ 触发 `EscortMerchantCaravan`（商队护送）因果链

**步骤**：

1. **接取源 Quest**
   - 去任意村庄，用 StoryDialog → "【找工作】" → 接取 `NearbyBanditBase`（清剿匪穴）
   - 或原版对话接取，均可
   - 日志应有 `[QuestConsequence]` 无输出（接取不触发，完成后才触发）

2. **完成源 Quest**
   - 去目标匪穴清剿，完成后回村庄领报酬
   - **立即检查**：
     ```
     日志搜 [QuestConsequence] →
       应显示: "VANILLA_NearbyBanditBase + Success → 排入 2 个后续"
       具体: ① VANILLA_ExtortionByDeserters Suppress 30天
             ② VANILLA_EscortMerchantCaravan BoostWeight×2 30天
     ```

3. **验证物理层 — 后续事件出现在世界里**
   - 控制台：`custom.worldevent_list`
   - **预期**：没有新的 WorldEvent party（这两个后续都是 Suppress/BoostWeight，不 spawn party）
   - 对于 spawn party 的后续（如 `VANILLA_GangNeedsRecruits` 来自链 1 的第三条）：
     - 15 天后同区域应有新 party 出现
     - 或控制台 `custom.worldevent_force` 推进时间验证

4. **验证社会层 — NPC 出现新的关联 `!`**
   - 去同区域/同村庄的商人 NPC
   - **预期**：
     - 商人头顶出现 `!`（`EscortMerchantCaravan` Issue）
     - 同村庄村长**不**出现 `ExtortionByDeserters`（被 Suppress 抑制了 30 天）
   - 日志：`[QuestFilter]` 无阻止记录（无紧迫事件，全放行）

5. **验证对话层 — 能看出因果**
   - StoryDialog 互动键 → "【找工作】" → 商人 NPC 开口
   - **预期叙事包含因果关系**：
     ```
     CSV 或 LLM 生成:
     "上次你帮隔壁村清剿了匪穴之后，这片的商路安全多了。
      我想趁这个机会送一批货去城里。你愿意护送我的商队吗？"
     ```
     而不是：
     ```
     "我需要人护送商队。"  ← 无因果上下文
     ```
   - 对话中应自然提到 `{PREVIOUS_QUEST}`（清剿匪穴）或 `{CAUSE_EVENT}`（匪患被清除）

6. **接取后续 Quest**
   - 点 `【接取】` → `EscortMerchantCaravan` Quest 启动，进入 Journal
   - 日志：`[QuestConsequence]` 无新输出（接取不触发因果，完成后才触发下一环）

7. **验证链的自然断裂**
   - `EscortMerchantCaravan` 完成后查因果表 → 触发链 3 的后续（`ArtisanOverpricedGoods` / `RevenueFarming`）
   - 再完成后再查 → 如 JSON 中无后续定义 → `[QuestConsequence]` 输出 "无后续条目 → 断链"
   - **链长由 JSON 决定，不是硬编码**

**通过标准**：
- ✅ 步骤 2：日志显示因果表命中
- ✅ 步骤 4：后续 Issue 确实出现在世界里的 NPC 头上
- ✅ 步骤 5：对话中包含因果上下文（能看出"因为上次…所以…"）
- ✅ 步骤 7：链自然延续或断裂，不由代码限制

### 测试 6：无刷屏
1. 跑 10 分钟，检查 `StoryEngine_RuntimeLog.txt`
2. **预期**: 日志以 `[标签]` 汇总为主，无逐 NPC 循环

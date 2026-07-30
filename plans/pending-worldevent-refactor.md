# PendingWorldEvent 重构计划

**目标**：Mission 层犯罪（警戒行为 + 偷窃）不直接写 WorldEventStore，而是先写入 Mission 作用域的 PendingWorldEvent。**仅 Alarmed 阶段的 NPC 才作为目击者写入**（过滤掉仅仅是 Cautious/Suspicious 的无聊观察）。离开场景时一次性持久化。同时将 `ActionBreakdown` 升级为 per-witness `WitnessTestimony`，对话直接按 speaking NPC 的证词精准生成台词。

## 关键设计决策

1. **不考虑存档迁移** — mod 未发布，直接改结构
2. **`StolenItems` 从 `WitnessTestimonies` 派生** — 过滤 `ActionType == "Steal"` 的 ActionRecord 即可，不用另存或用 TheftLedger
3. **仅 Alarmed 写入 PendingWorldEvent** — Cautious/Suspicious 只是 HUD 上有反应，不留下持久记录，避免无聊琐事污染 WorldEvent
4. **一次性提交** — 所有文件改完一起交

---

## 一、数据结构

### 1.1 `WitnessTestimony` + `ActionRecord`

文件：[AlertTypes.cs](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\AI\AlertTypes.cs)

```csharp
/// <summary>单个目击者的证词：这位目击者看到了玩家哪些行为。仅 Alarmed 阶段才写入。</summary>
[Serializable]
public class WitnessTestimony
{
    public string WitnessHeroId;   // null = 模板村民
    public string TemplateId;      // null = 有脸英雄
    public List<ActionRecord> Actions;
}

/// <summary>目击者看到的单条行为</summary>
[Serializable]
public class ActionRecord
{
    public string ActionType;      // PlayerActionType 名称
    public float AlertValue;       // 目击者对此行为的警戒值
    public string TargetName;      // 受害者名（Knockout/AttackAlly）；Crouching/WeaponDrawn 为 null
    public string ItemId;          // Steal 赃物 ID
    public string ItemName;        // Steal 赃物显示名
}
```

### 1.2 WorldEvent 变更

文件：[WorldEvent.cs](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\WorldEvent\WorldEvent.cs)

```diff
- public Dictionary<string, float> ActionBreakdown;
- public Dictionary<string, int> StolenItems;

+ public List<WitnessTestimony> WitnessTestimonies;

+ // ── 以下全部从 WitnessTestimonies 派生 ──

+ [JsonIgnore]
+ public List<string> WitnessHeroIds => WitnessTestimonies
+     ?.Where(t => t.WitnessHeroId != null)
+     .Select(t => t.WitnessHeroId).Distinct().ToList() ?? new List<string>();

+ [JsonIgnore]
+ public Dictionary<string, int> TemplateWitness => WitnessTestimonies
+     ?.Where(t => t.TemplateId != null)
+     .GroupBy(t => t.TemplateId)
+     .ToDictionary(g => g.Key, g => g.Count());

+ [JsonIgnore]
+ public int WitnessCount => WitnessHeroIds.Count
+     + (WitnessesSilenced ? 0 : TemplateWitness.Values.Sum());

+ // StolenItems 直接从 WitnessTestimonies 中 Steal 类 ActionRecord 聚合
+ [JsonIgnore]
+ public Dictionary<string, int> StolenItems => WitnessTestimonies
+     ?.SelectMany(t => t.Actions)
+     .Where(a => a.ActionType == "Steal" && !string.IsNullOrEmpty(a.ItemId))
+     .GroupBy(a => a.ItemId)
+     .ToDictionary(g => g.Key, g => g.Count())
+     ?? new Dictionary<string, int>();

+ // ActionDescription 从 WitnessTestimonies 全局聚合
+ [JsonIgnore]
+ public string ActionDescription { get { /* 遍历 WitnessTestimonies 所有 ActionRecord，按 ActionType 聚合排序输出 */ } }
```

`StolenItems`、`StolenItemsSnapshot`、`TotalStolenCount`、`TotalStolenValue` 等方法保留签名不变（调用方不需要改），实现改为从 `WitnessTestimonies` 派生。

`WitnessHeroIds`、`TemplateWitness` 原来是序列化字段，现在改为计算属性。序列化时只存 `WitnessTestimonies`，反序列化自动还原。

### 1.3 合并策略：Stage 不兼容时不开 Merge

文件：[WorldEventStore.cs](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\WorldEvent\WorldEventStore.cs)


```

合并判断矩阵：

| 已有 Stage | 新 Event Stage | 行为 | 理由 |
|-----------|---------------|------|------|
| Dormant | Dormant | ✅ 合并 | 都没人看见，累积等发现，嫌疑人未知 |
| Dormant | Emerging | ✅ 合并 | 这次有人看见了，旧案一并曝光，但是嫌疑人未知|
| Emerging | Dormant | ✅ 合并 | 保持 Emerging，追加无目击罪行，嫌疑人未锁定|
| Emerging | Emerging | ✅ 合并 | 更多目击者 / 更多行为，嫌疑人未锁定|
| Active | Dormant | ✅ 合并 | 保持 Active，嫌犯已锁定，追加罪证 |
| Active | Emerging | ✅ 合并 | 追加新目击证词 |
| Active/Confrontation | Active/Confrontation | 取决于嫌疑人是不是同一个 | 嫌疑人相同则合并，否则不合并 |

| Resolved / Unsolved | — | — | `FindOnGoing` 已过滤，自然新建 |

> 上表是 `AddOrMerge` 的**完整决策矩阵**（Misconduct 事件的实际入参 Stage 只有 Dormant/Emerging/Active）。表外情况（其他 EventType 或未知 Stage 组合）走默认分支：**不合并，直接 Add**。代码兜底：
>
> ```csharp
> if (existing != null && existing.Type == evt.Type)
> {
>     if (existing.Stage >= EventStage.Confrontation)
>     {
>         TransitionStage(existing, EventStage.Resolved);
>         existing.ResolvedBy = "superseded";
>         // fall through → _allEvents.Add(evt)
>     }
>     else if (existing.Stage <= EventStage.Active && evt.Stage <= EventStage.Active)
>     {
>         // 表中 6 种组合：Dormant/Emerging/Active 任意排列 → 合并
>         MergeWitnessTestimonies(existing, evt);
>         existing.Stage = (EventStage)Math.Max((int)existing.Stage, (int)evt.Stage);
>         existing.Severity = Math.Min(100, existing.Severity + evt.Severity / 2);
>         existing.LastUpdateDay = (float)CampaignTime.Now.ToDays;
>         return;
>     }
>     // else: 表外 → 不合并，继续往下 Add
> }
> _allEvents.Add(evt);
> ```
>
> 合并后 Stage 取 `Max(existing.Stage, incoming.Stage)`：Dormant+Emerging → Emerging，Dormant+Active → Active。

> `AgentAIController.AfterStart()` 中加载 PendingWorldEvent 时也执行了同样的 Stage 判断（见第二节），两头互保。

### 1.4 PlaceholderResolver 调整

```diff
- public string TargetName;
- public string ItemName;
+ /// <summary>当前对话 NPC 的目击证词。null = 不是目击者。</summary>
+ public WitnessTestimony SpeakingWitness;
```

`TARGET` / `ITEM` / `StolenItemName` 占位符从 `SpeakingWitness` 的主要 ActionRecord 中取。
`PrimaryWitnessName` / `WitnessCount` 从 `WitnessTestimonies` 派生。

---

## 二、PendingWorldEvent

### 2.1 AgentAIController

文件：[AgentAIController.cs](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\AI\AgentAIController.cs)

```csharp
public class AgentAIController : MissionLogic
{
    public WorldEvent PendingWorldEvent { get; private set; }

    public override void AfterStart()
    {
        var settlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
        if (settlement == null) return;

        var existing = WorldEventStore.FindOnGoing(settlement.StringId);

        // 🔑 旧案已在 Confrontation（了结阶段）→ 关掉旧案，开新案
        if (existing != null && existing.Type == EventType.Misconduct
            && existing.Stage >= EventStage.Confrontation)
        {
            WorldEventStore.TransitionStage(existing, EventStage.Resolved);
            existing.ResolvedBy = "superseded";
            existing = null; // 强制走新建
        }

        PendingWorldEvent = (existing != null && existing.Type == EventType.Misconduct)
            ? existing  // 续档：Dormant/Emerging/Active 的旧案
            : new WorldEvent
            {
                EventId = $"misconduct_{settlement.StringId}_{(int)CampaignTime.Now.ToHours}",
                Category = EventCategory.Crime, Type = EventType.Misconduct,
                InitiatorId = Hero.MainHero?.StringId ?? "player",
                TargetSettlementId = settlement.StringId,
                OccurredDay = (float)CampaignTime.Now.ToDays,
                Stage = EventStage.Dormant,
                WitnessTestimonies = new List<WitnessTestimony>(),
            };
    }

    public override void OnRemoveBehavior()
    {
        FinalizePendingWorldEvent();
        base.OnRemoveBehavior();
    }

    /// <summary>AgentBrain 到达 Alarmed 时调用：将此 NPC 注册为目击者。</summary>
    public void RegisterWitness(AgentBrain brain)
    {
        var pending = PendingWorldEvent;
        if (pending == null) return;

        var hero = (brain.Owner.Character as CharacterObject)?.HeroObject;
        string heroId = hero?.StringId;
        string templateId = hero == null ? brain.Owner.Character?.StringId : null;

        // 已有同 NPC 的 testimony → 不重复创建
        var existing = pending.WitnessTestimonies.FirstOrDefault(t =>
            (heroId != null && t.WitnessHeroId == heroId) ||
            (templateId != null && t.TemplateId == templateId));
        if (existing != null) { SyncActions(brain, existing); return; }

        // 新建 testimony 并从 brain._alertBreakdown 同步
        var testimony = new WitnessTestimony { WitnessHeroId = heroId, TemplateId = templateId, Actions = new List<ActionRecord>() };
        SyncActions(brain, testimony);
        pending.WitnessTestimonies.Add(testimony);
    }

    void SyncActions(AgentBrain brain, WitnessTestimony testimony)
    {
        foreach (var kv in brain.AlertBreakdown)
        {
            var entry = kv.Value;
            var existing = testimony.Actions.FirstOrDefault(a => a.ActionType == kv.Key.ToString());
            if (existing != null)
            {
                existing.AlertValue = entry.Value;
                existing.TargetName = entry.TargetName ?? existing.TargetName;
                existing.ItemName = entry.ItemName ?? existing.ItemName;
            }
            else
            {
                testimony.Actions.Add(new ActionRecord
                {
                    ActionType = kv.Key.ToString(), AlertValue = entry.Value,
                    TargetName = entry.TargetName, ItemName = entry.ItemName,
                    // ItemId: Steal 类行为由 StealManager 侧写入
                });
            }
        }
    }

    void FinalizePendingWorldEvent()
    {
        if (PendingWorldEvent == null) return;
        if (PendingWorldEvent.WitnessTestimonies.Count == 0) return;

        PendingWorldEvent.Stage = EventStage.Emerging;
        PendingWorldEvent.SuspectHeroId = Hero.MainHero?.StringId;
        PendingWorldEvent.InvestigationProgress = 1.0f;
        PendingWorldEvent.PublicAwareness = 0.3f;
        WorldEventStore.AddOrMerge(PendingWorldEvent);
    }

    // 偷窃目击者（StealManager 调用）；witnessHeroIds/templateWitness 来自 GetWitnesses()
    public void RegisterTheftWitnesses(List<string> witnessHeroIds, Dictionary<string, int> templateWitness,
        string itemId, string itemName, string targetName = null)
    {
        var pending = PendingWorldEvent;
        if (pending == null) return;

        foreach (var heroId in witnessHeroIds)
            AddStealAction(pending, heroId, null, itemId, itemName, targetName);
        foreach (var kv in templateWitness)
            AddStealAction(pending, null, kv.Key, itemId, itemName, targetName);

        pending.Stage = EventStage.Emerging;
        pending.SuspectHeroId = Hero.MainHero?.StringId;
    }

    static void AddStealAction(WorldEvent pending, string heroId, string templateId,
        string itemId, string itemName, string targetName)
    {
        var testimony = pending.WitnessTestimonies.FirstOrDefault(t =>
            (heroId != null && t.WitnessHeroId == heroId) ||
            (templateId != null && t.TemplateId == templateId));
        if (testimony == null)
        {
            testimony = new WitnessTestimony { WitnessHeroId = heroId, TemplateId = templateId, Actions = new List<ActionRecord>() };
            pending.WitnessTestimonies.Add(testimony);
        }
        // 同物品累加 Count（通过追加同名 ActionRecord，派生时 GroupBy 自动合并）
        testimony.Actions.Add(new ActionRecord
        {
            ActionType = "Steal", AlertValue = 3.0f,
            TargetName = targetName, ItemId = itemId, ItemName = itemName,
        });
    }
}
```

---

## 三、AgentBrain 集成

**核心原则：只有进入 Alarmed 时才把 NPC 注册为目击者。** Cautious/Suspicious 只在 HUD 上显示气泡，不污染 WorldEvent。

### 3.1 `CheckPhaseTransition` 中注册目击者

```csharp
// AgentBrain.CheckPhaseTransition() — 在阶段迁移到 Alarmed 时：
if (newPhase == AlarmPhase.Alarmed && _lastAlertPhase < AlarmPhase.Alarmed)
{
    // 新进入 Alarmed → 注册为目击者
    AgentAIController.Instance?.RegisterWitness(this);
}
```

### 3.2 删除 `CreateOrUpdateMisconductEvent`

这个方法直接删除。`CurrentMisconductEventId` 改为读 `PendingWorldEvent.EventId`：

```csharp
string CurrentMisconductEventId => AgentAIController.Instance?.PendingWorldEvent?.EventId;
```

### 3.3 Stage 推进直接操作 PendingWorldEvent

`StartL3CombatJoin` / `DeferredCombat` / `ReEngageConfrontation` 中推进 Stage 的逻辑：

```diff
- var evt = WorldEventStore.Find(CurrentMisconductEventId);
+ var pending = AgentAIController.Instance?.PendingWorldEvent;
+ if (pending != null && pending.Stage < EventStage.Confrontation)
+ {
+     // 如果已在 WorldEventStore 中（续档），走 TransitionStage 触发 UI 刷新
+     var existing = WorldEventStore.Find(pending.EventId);
+     if (existing != null)
+         WorldEventStore.TransitionStage(existing, EventStage.Confrontation);
+     else
+         pending.Stage = EventStage.Confrontation;
+ }
```

---

## 四、StealManager 集成

文件：[StealManager.cs](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\Stealth\StealManager.cs)

**旧逻辑**：`StealManager` 创建 `WorldEvent` 直接 `WorldEventStore.AddOrMerge()`。

**新逻辑**：偷窃目击者写入 `PendingWorldEvent`，无目击者时也创建记录（留在 Dormant 等过夜）：

```csharp
// 有目击者 → PendingWorldEvent.RegisterTheftWitnesses()
if (wasWitnessed)
{
    AgentAIController.Instance?.RegisterTheftWitnesses(
        witnessHeroIds, templateWitness, itemId, itemName);
}
// TheftLedger 照样记账（赃物标注、栽赃系统依赖它）
TheftLedger.Record(initiatorId, victimHeroId, settlementId, itemId, count,
    locationName, AgentAIController.Instance?.PendingWorldEvent?.EventId);
```

`WorldEventStore.AddOrMerge()` 调用删除。偷窃的直接持久化延迟到 `OnRemoveBehavior` → `FinalizePendingWorldEvent()`。

---

## 五、IntentContext 清理死代码

文件：[IntentContext.cs](h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\Interaction\Intents\IntentContext.cs)

```diff
- public IReadOnlyDictionary<PlayerActionType, AlertEntry> AlertBreakdown;
- public PlayerActionType? PrimaryAlertAction;
- public float AlertValue;
```

全项目无读取，直接删。

---

## 六、CrimeDialogueBuilder 重构

### 6.1 `BuildIntentContext` 简化

删除 AlertBreakdown 赋值整块。`BuildAlertInterceptScript` 中 L3 质问的 `ConfrontationType` 和 `primaryAction` 直接从 `speakerAgent` 的 brain 取即可（调用方已传入这些参数）。

### 6.2 `BuildWitnessScript` — 从 WitnessTestimony 精准生成

```csharp
private static DialogueInjector.DialogueInjectScript BuildWitnessScript(
    PlaceholderResolver r, IntentContext ctx)
{
    var evt = ctx.ActiveEvent;
    var speaker = ctx.Speaker;

    // 从 WitnessTestimonies 匹配当前 NPC 的证词
    var testimony = evt.WitnessTestimonies?
        .FirstOrDefault(t => t.WitnessHeroId == speaker.StringId);
    r.SpeakingWitness = testimony;

    string witnessedDesc = BuildWitnessedActionDescription(testimony);

    string npcLine = evt.InitiatorIsPlayer
        ? r.Resolve($"（{{SpeakerEmotion}}地）{{SpeakerPlayerAddr}}是来问{{CrimeScene}}的事？{{SpeakerSelfRef}}看见了——{witnessedDesc}。")
        : r.Resolve($"（{{SpeakerEmotion}}地）{{SpeakerSelfRef}}{{TimeWord}}在{{CrimeScene}}附近看见了——{witnessedDesc}");

    // ... 选项逻辑不变
}
```

```csharp
/// <summary>从单条 WitnessTestimony 构建中文描述（如"偷了村民甲的鸡，还把人打晕了"）</summary>
public static string BuildWitnessedActionDescription(WitnessTestimony testimony)
{
    if (testimony?.Actions == null || testimony.Actions.Count == 0)
        return "有人在闹事";

    var parts = new List<string>();
    foreach (var a in testimony.Actions.OrderByDescending(a => a.AlertValue))
    {
        string desc = a.ActionType switch
        {
            "Crouching" => "鬼鬼祟祟蹲了半天",
            "WeaponDrawn" => "在村里拔刀",
            "StealUIOpen" => "翻箱倒柜",
            "Steal" when a.ItemName != null =>
                a.TargetName != null
                    ? $"偷了{a.TargetName}的{a.ItemName}"
                    : $"偷了{a.ItemName}",
            "Steal" => "偷了东西",
            "AttackAlly" when a.TargetName != null => $"动手打了{a.TargetName}",
            "AttackAlly" => "动手打人",
            "Knockout" when a.TargetName != null => $"把{a.TargetName}打晕了",
            "Knockout" => "把人打晕了",
            _ => null
        };
        if (desc != null) parts.Add(desc);
    }
    return parts.Count switch
    {
        0 => "有人在闹事",
        1 => parts[0],
        2 => $"{parts[0]}，还{parts[1]}",
        _ => $"{parts[0]}、{parts[1]}，还{parts[2]}"
    };
}
```

### 6.3 `BuildAuthorityScript` & 全局视角

权威 NPC 用全局聚合（所有目击者的汇总）。`WorldEvent.ActionDescription` 属性改为从 `WitnessTestimonies` 全局聚合，已有占位符 `{ActionDescription}` 无需改动。

### 6.4 `BuildAlertInterceptScript` (L3 质问)

L3 质问发生时，`RegisterWitness` **已经调完**（在 `CheckPhaseTransition` 进入 Alarmed 时触发）。所以 `PendingWorldEvent.WitnessTestimonies` 中已有当前 NPC 的证词，直接从 PendingWorldEvent 取即可，不需要从 brain 临时构建：

```csharp
public static DialogueInjector.DialogueInjectScript BuildAlertInterceptScript(
    Hero speaker, ConfrontationType npcIntent, PlayerActionType primaryAction,
    WorldEvent worldEvt = null, Agent speakerAgent = null)
{
    var r = new PlaceholderResolver(speaker, Hero.MainHero);

    // 🔑 从 PendingWorldEvent 取刚写入的证词（RegisterWitness 已在前一步执行）
    var pending = AgentAIController.Instance?.PendingWorldEvent;
    r.SpeakingWitness = pending?.WitnessTestimonies?
        .FirstOrDefault(t => t.WitnessHeroId == speaker.StringId);

    // ... 后续台词构建逻辑不变
}
```

调用链保证顺序：

```
CheckPhaseTransition()
  │  newPhase == Alarmed && _lastAlertPhase < Alarmed
  │
  ├─ 1. AgentAIController.Instance.RegisterWitness(this);   // ← 先写
  │      PendingWorldEvent.WitnessTestimonies.Add(testimony)
  │
  └─ 2. StartL3Confrontation()                               // ← 后质问
         │
         └─ AlertForceConversationAction
              └─ CrimeDialogueBuilder.BuildAlertInterceptScript
                   └─ 从 PendingWorldEvent.WitnessTestimonies 取 ← 已有数据
```

不需要 `BuildTestimonyFromBrain` 这个临时方法了。

---

## 七、PlaceholderResolver 调整

```diff
- public string TargetName;
- public string ItemName;
+ public WitnessTestimony SpeakingWitness;

  // 构造器简化：不再需要单独的 targetName/itemName 参数
  // 保留兼容旧调用方，内部从 SpeakingWitness 取
```

`ResolveOne` 中：
- `TARGET` → `SpeakingWitness?.Actions.OrderByDescending(a=>a.AlertValue).FirstOrDefault()?.TargetName`
- `ITEM` → 同上 `?.ItemName`
- `StolenItemName` → 同上 `?.ItemName`

---

## 八、涉及文件清单

| 文件 | 变更类型 |
|------|----------|
| `AI/AlertTypes.cs` | 新增 `WitnessTestimony`、`ActionRecord` |
| `WorldEvent/WorldEvent.cs` | 删除 `ActionBreakdown`/`StolenItems`，新增 `WitnessTestimonies`，旧属性改为派生 |
| `WorldEvent/WorldEventStore.cs` | `AddOrMerge` 新增 Stage 不兼容判断 + `MergeWitnessTestimonies`；`TryUpsertMisconductEvent` 废弃 |
| `AI/AgentAIController.cs` | 新增 `PendingWorldEvent`、`RegisterWitness`、`RegisterTheftWitnesses`、`FinalizePendingWorldEvent` |
| `AI/AgentBrain.cs` | `CheckPhaseTransition` 中 Alarmed 时调用 `RegisterWitness`；删除 `CreateOrUpdateMisconductEvent`；`CurrentMisconductEventId` 改为读 Pending；Stage 推进逻辑适配 |
| `Stealth/StealManager.cs` | 改写 `PendingWorldEvent.RegisterTheftWitnesses()`，删 `WorldEventStore.AddOrMerge` |
| `Interaction/Intents/IntentContext.cs` | 删除 `AlertBreakdown`/`PrimaryAlertAction`/`AlertValue` |
| `Interaction/Dialogue/CrimeDialogueBuilder.cs` | `BuildIntentContext` 简化；`BuildWitnessScript` 用 `WitnessTestimony`；`BuildAlertInterceptScript` 从 brain 构建临时 testimony |
| `Interaction/Dialogue/PlaceholderResolver.cs` | `TargetName`/`ItemName` → `SpeakingWitness`；占位符适配 |
| `WorldEvent/InvestigationEngine.cs` | `WitnessHeroIds`/`TemplateWitness` 访问改为派生属性（调用方基本不变） |

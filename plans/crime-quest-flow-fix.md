# 犯罪 Quest 流程修复：调查→悬赏 断层修复 + 任务日志叙事对齐

> **触发**：2026-07-02 犯罪后果引擎瀑布测试
> **实施**：2026-07-02 ~ 2026-07-03

---

## 最终架构：三层职责分离

```
玩家行为（因）→ Intent → WorldEvent 数据变更（果）
                              ↓
                    OnEventStageChanged（副作用广播）
                              ↓
                 Issue 刷新 ✅（信号层：被动反映状态）
                 Quest 日志  ✅（仅 NPC 后台调查 case，不完成 Quest）

Quest 完成 → Intent 直接调用（行为层）
```

| 层 | 职责 | 触发方式 |
|---|------|---------|
| **Intent（行为层）** | 执行玩家操作 + 管理 Quest 生命周期 | 玩家交互，直接调用 |
| **WorldEvent（数据层）** | 存储案件客观状态 | 被 Intent 写入 |
| **Issue（信号层）** | 反映"有事可做" | `OnEventStageChanged` 事件驱动 |

**原则**：Quest 完成只由 Intent 负责。事件回调只处理"无玩家 Intent"的 case（NPC 后台调查出结果），且只加日志、不完成 Quest。

---

## 问题诊断

### 问题 A：调查→悬赏 Quest 链断裂

玩家在对话中接调查 Quest → 栽赃嫌犯成功 → WorldEvent 进入 Active → 玩家再次对话接悬赏。此时：

1. **调查 Quest 未完成**："找出真凶"目标永远是 0/1，占着 quest 槽位
2. **悬赏 Quest 未创建**：`AcceptBountyQuestIntent.OnInstant()` 只设 `PlayerTookBountyQuest = true`，没有任何创建 Quest 的代码
3. **新 Issue 可能被阻塞**：`TryAddIssue` 检查 `GetActiveCommissionCount() >= maxQuests` 时，未完成的调查 Quest 占着槽位 → 黄色 `!` 创建失败

**根因**：`AcceptBountyQuestIntent.OnInstant()` 缺少两件事——完成调查 Quest、创建悬赏 Quest。对比 `InvestigateIntent.OnInstant()` 做了完整的事（创建 CommissionQuest + StartQuest）。

### 问题 B：任务日志叙事贫瘠

对话系统通过 `PlaceholderResolver` 解析 ~80 个占位符，产出丰富叙事：
> "（焦虑地）昨儿特维亚的牲口圈牲口被偷了，一只肉猪不见了。刚开始查。没人看见，不知道是谁。你这小子能帮忙查查吗？"

任务日志使用完全独立的三条硬编码/静态模板管线，与 WorldEvent 数据脱钩：
> "【委托】调查：特维亚失窃案\n委托人：…\n前往特维亚附近搜集线索。"

**根因**：`CommissionData.GetFlavorDescription()` / `CommissionHubIssue.DescriptionForContext()` / `CommissionQuest.OnStartInvestigation()` 都不读 `WorldEvent` 数据。

### 问题 C：SuspectDescription 语境不一致

玩家选"是强盗干的"，但对话里 NPC 说"是**流浪汉**"雌豹"瓦拉干的"。`GetSocialIdentity()` 只看 Hero 属性（`IsWanderer`），不知道这个 Hero 是作为"藏身处强盗头子"被找到的。

---

## 修复范围

| # | 修改 | 文件 | 行号 |
|---|------|------|------|
| 1 | `AcceptBountyQuestIntent` 补全 Quest 创建 | `AccountabilityIntents.cs` | 670-676 |
| 2 | Investigation DailyTick 检测阶段变化 | `CommissionQuest.cs` | 230-232 |
| 3a | Issue 标题/描述叙事化 | `CommissionHubIssue.cs` | 18-41, 75-145 |
| 3b | Quest 标题叙事化 | `CommissionData.cs` | 423-448 |
| 3c | Quest 日志条目叙事化 | `CommissionQuest.cs` | 1189-1197 |

---

## 修改 1：AcceptBountyQuestIntent — 完成调查 Quest + 创建悬赏 Quest

**文件**：`ExampleModVS/ExampleMod/ExampleMod/Interaction/Intents/AccountabilityIntents.cs`
**位置**：`AcceptBountyQuestIntent.OnInstant()`（约第 670 行）

**当前**：
```csharp
public override void OnInstant(IntentContext ctx)
{
    var evt = ctx.ActiveEvent;
    if (evt == null) return;
    evt.PlayerTookBountyQuest = true;
    DebugLogger.Log($"[Accountability] Player accepted bounty quest for {evt.EventId}");
}
```

**改为**：
```csharp
public override void OnInstant(IntentContext ctx)
{
    var evt = ctx.ActiveEvent;
    if (evt == null) return;
    evt.PlayerTookBountyQuest = true;

    // 1. 完成调查 Quest（如果存在）
    foreach (var q in Campaign.Current.QuestManager.Quests)
    {
        if (q is CommissionQuest cq
            && cq.Data?.WorldEventId == evt.EventId
            && cq.Data?.Category == CommissionCategory.Investigation
            && !cq.Data.IsObjectivesComplete)
        {
            cq.CompleteObjectivesFromExternal();
            DebugLogger.Log($"[Accountability] Investigation quest completed via bounty acceptance: {cq.StringId}");
            break;
        }
    }

    // 2. 创建悬赏 Quest
    var authority = WorldEventStore.GetAuthorityNpc(evt);
    if (authority != null)
    {
        var data = CommissionGenerator.TryGenerateAccountabilityQuest(authority);
        if (data != null)
        {
            string questId = $"bounty_{evt.EventId}";
            var quest = new CommissionQuest(questId, data);
            quest.StartQuest();
            DebugLogger.Log($"[Accountability] Bounty quest STARTED: {questId} giver={authority.Name} suspect={data.TargetHero?.Name}");
        }
        else
        {
            DebugLogger.Log($"[Accountability] TryGenerateAccountabilityQuest returned null for bounty on {evt.EventId} (stage={evt.Stage})");
        }
    }
}
```

**注意**：`TryGenerateAccountabilityQuest` 在第 944 行检查是否已有同 WorldEvent 的 Quest——所以必须先完成调查 Quest，否则这里会 return null。

---

## 修改 2：CommissionQuest.DailyTick — Investigation 检测 WorldEvent 阶段变化

**文件**：`ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionQuest.cs`
**位置**：`OnDailyTick()` 中 `RegisterEvents()` 的 Investigation case（约第 228-232 行）

**当前**：
```csharp
case CommissionCategory.Investigation:
    // 调查委托不需要额外事件——DailyTick 已注册
    break;
```

这里 `RegisterEvents` 只注册了 DailyTick，没有注册其他事件。DailyTick 本身（约第 440 行）也不检测 WorldEvent 阶段。

**在 DailyTick 方法中增加**（约第 557 行，return 之前）：

```csharp
// ── Investigation: 检测 WorldEvent 阶段变化 → 自动完成 ──
if (_data.Category == CommissionCategory.Investigation
    && !string.IsNullOrEmpty(_data.WorldEventId)
    && !_data.IsObjectivesComplete)
{
    var evt = WorldEventStore.FindEvent(_data.WorldEventId);
    if (evt != null && evt.Stage == EventStage.Active && !string.IsNullOrEmpty(evt.SuspectHeroId))
    {
        var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
        AddLog(new TextObject($"案件有了突破——嫌犯锁定为{suspect?.Name?.ToString() ?? "某人"}。回去向{QuestGiver.Name}汇报调查结果。"));
        UpdateProgress(_totalProgress);
        DebugLogger.Log($"[CommissionQuest] Investigation auto-completed: {StringId} suspect={evt.SuspectHeroId}");
    }
}
```

---

## 修改 3a：CommissionHubIssue — Issue 标题/描述叙事化

**文件**：`ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionHubIssue.cs`

### 3a-1：`CommissionIssueContext` 增加 WorldEvent 数据字段（约第 18-41 行）

```csharp
public struct CommissionIssueContext
{
    // ... 现有字段保持不变 ...
    
    // 新增：叙事细节（从 WorldEvent 提取，用于 PlaceholderResolver 模板）
    public string CrimeScene;        // "牲口圈" / "谷仓" / "身上"
    public string StolenItemName;    // "肉猪" / "银戒指"
    public string CrimeVerb;         // "偷" / "扒"
    public int WitnessCount;         // 目击人数
}
```

### 3a-2：`ResolveIssueContext()` 填充新字段（约第 547-555 行）

在 `evt.Stage != EventStage.Dormant` 分支中，从 WorldEvent 提取叙事数据填入 context。

### 3a-3：`DescriptionForContext()` 使用叙事模板（约第 136 行）

当前：
```csharp
case EventStage.Emerging:
    return new TextObject($"{_context.SettlementName}的案子需要调查…");
```

改为：
```csharp
case EventStage.Emerging:
    return new TextObject(
        $"{_context.SettlementName}的{_context.CrimeScene}{_context.CrimeVerb}了{_context.StolenItemName}。" +
        (_context.WitnessCount > 0
            ? $"{_context.WitnessCount}人目击，急需调查。"
            : "无人目击，需要能干的佣兵查案。"));
```

---

## 修改 3b：CommissionData.GetFlavorDescription — Quest 标题叙事化

**文件**：`ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionData.cs`
**位置**：`GetFlavorDescription()`（约第 423 行）

当 `WorldEventId` 不为空时，优先用 WorldEvent 数据生成叙事标题：

```csharp
public string GetFlavorDescription()
{
    // 优先：从关联的 WorldEvent 生成叙事标题
    if (!string.IsNullOrEmpty(WorldEventId))
    {
        var evt = WorldEventStore.FindEvent(WorldEventId);
        if (evt != null)
        {
            string settlementName = Settlement.Find(evt.TargetSettlementId)?.Name?.ToString() ?? "本地";
            switch (evt.Stage)
            {
                case EventStage.Emerging:
                    return $"调查：{settlementName}{evt.Config?.CrimeScene ?? ""}失窃案";
                case EventStage.Active:
                    var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                    return $"悬赏缉拿：{suspect?.Name?.ToString() ?? "嫌犯"}";
                default:
                    break;
            }
        }
    }

    // 回退：静态模板（原有逻辑）
    var def = GetDef();
    // ... 保持原有代码 ...
}
```

---

## 修改 3c：CommissionQuest.OnStartInvestigation — Quest 日志叙事化

**文件**：`ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionQuest.cs`
**位置**：`OnStartInvestigation()`（约第 1189 行）

当前硬编码：
```csharp
AddLog(new TextObject($"前往 {locationName} 附近搜集线索。与当地人交谈或回现场调查，找出是谁干的。"));
AddLog(new TextObject("提示：时间有限——调查窗口关闭后案件将陷入僵局。可用 Scouting 技能加速线索搜集。"));
```

改为从 WorldEvent 提取案情细节：
```csharp
var evt = !string.IsNullOrEmpty(_data.WorldEventId)
    ? WorldEventStore.FindEvent(_data.WorldEventId) : null;

if (evt != null)
{
    string itemName = "赃物";
    if (evt.StolenItemIds?.Count > 0)
    {
        var item = MBObjectManager.Instance.GetObject<ItemObject>(evt.StolenItemIds[0]);
        itemName = item?.Name?.ToString() ?? "财物";
    }
    string scene = evt.Config?.CrimeScene ?? "现场";
    string verb = evt.Config?.CrimeVerb ?? "丢失";
    int witnessCount = evt.EvidenceList?.Count(e => e.Type == EvidenceType.Witness) ?? 0;
    int windowDays = evt.Config?.InvestigationWindowDays ?? 7;

    AddLog(new TextObject(
        $"前往 {locationName} 的{scene}附近搜集线索。{itemName}被{verb}了，" +
        (witnessCount > 0
            ? $"有{witnessCount}人目击了事发经过。"
            : "暂时无人目击。") +
        $"与当地人交谈或回现场调查，找出是谁干的。"));
    AddLog(new TextObject(
        $"提示：调查窗口约{windowDays}天，超时后案件将陷入僵局。可用 Scouting 技能加速线索搜集。"));
}
else
{
    // 回退：保持现有硬编码
    AddLog(new TextObject($"前往 {locationName} 附近搜集线索。与当地人交谈或回现场调查，找出是谁干的。"));
    AddLog(new TextObject("提示：时间有限——调查窗口关闭后案件将陷入僵局。可用 Scouting 技能加速线索搜集。"));
}
```

---

## 不影响的范围

- **Phase 5 (LLM)**：不在本次修复范围
- **Issue 系统 `StageChanged` → `TryAddIssue`**：已有逻辑正确，本次不修改
- **CrimeDialogueBuilder / PlaceholderResolver**：对话系统叙事已完善，不修改
- **SuspectDescription 语境问题（"流浪汉" vs "强盗"）**：属于 `GetSocialIdentity` 的语境感知增强，另开 Issue

---

## 测试验证

完成修改后，按以下步骤验证：

1. 偷动物（无目击）→ DailyTick → Emerging → 蓝色 `!` 出现
2. 与村长对话 → **验证 Issue 描述含案情细节**（非纯模板）
3. 接调查 Quest → **验证 Quest 日志含具体案情**（非通用文本）
4. 再次对话 → 栽赃强盗 → 对话结束
5. **验证黄色 `!` 出现**（"悬赏缉拿：XXX"）
6. 与村长对话 → 接悬赏
7. **验证调查 Quest 完成**（日志不再显示"找出真凶"）
8. **验证悬赏 Quest 创建**（日志显示"悬赏缉拿：XXX"）

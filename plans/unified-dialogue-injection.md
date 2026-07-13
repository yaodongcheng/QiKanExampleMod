# 对话注入统一入口重构

> 状态：待实施
> 创建：2026-07-13

## 问题诊断

当前 `CrimeDialogueBuilder` 有四条调用路径，注入决策和脚本构建分散在三处：

```
路径① 玩家主动交谈（WorldEvent 驱动）
  TryInjectCrimeDialogue → BuildScript → InjectScript

路径② NPC 主动质问（Alert）
  AlertForceConversationAction.OnStart:
    1. BuildAlertInterceptScript → InjectScript  ← 自己构建+注入
    2. StartConversation → Postfix → TryInjectCrimeDialogue  ← 又走一遍！

路径③-a 玩家向 NPC 投降
  CombatManager.PlayerSurrenderToAgent:
    1. BuildPlayerSurrenderScript → InjectScript  ← 自己构建+注入
    2. StartConversation → Postfix → TryInjectCrimeDialogue  ← 又走一遍！

路径③-b NPC 向玩家投降
  CombatManager.AcceptAgentSurrender:
    1. BuildNpcSurrenderScript → InjectScript  ← 自己构建+注入
    2. StartConversation → Postfix → TryInjectCrimeDialogue  ← 又走一遍！
```

**三个具体问题**：

1. **双重注入风险**：路径②③在 `StartConversation` 前自己调了 `InjectScript`，然后 Postfix 又触发 `TryInjectCrimeDialogue`。如果 settlement 刚好有活跃 WorldEvent，会再注入一次 `BuildScript`，两个脚本竞争 `start` token。

2. **模板 NPC 的 BystanderScript 白设计了**：`TryInjectCrimeDialogue` 第一行 `if (partner == null) return;` 直接拦截了所有模板 NPC。但 `BuildScript` 里有 `BuildBystanderScript`，设计意图是让 settlement 里任意 NPC 都能八卦犯罪事件。

3. **分派逻辑分散**：调用方（AlertForceConversationAction / CombatManager）替 `BuildScript` 做了"该用哪个脚本"的决策——`BuildScript` 本应是唯一分派点，但实际上 Alert/Surrender 绕过了它。

## 核心思路

**`BuildScript` 成为唯一分派点。** 新增 `DialogueTrigger` 参数，四种场景在 `BuildScript` 内部统一 switch：

```
                    ┌─ Normal ──────────→ 按 speaker 身份分派（Authority/Witness/Suspect/Bystander）
BuildScript(evt,    ├─ Alert ───────────→ BuildAlertInterceptScript
  speaker,          ├─ PlayerSurrender ─→ BuildPlayerSurrenderScript
  trigger,          └─ NpcSurrender ────→ BuildNpcSurrenderScript
  alertCtx?)
```

调用方**只设 trigger**，不构建脚本、不注入。`TryInjectCrimeDialogue` 找 WorldEvent → 读 trigger → 调 `BuildScript` → 注入。一条线走到底。

```
重构后：

路径① 玩家主动交谈
  (无) → StartConversation → TryInjectCrimeDialogue
    ① 找 WorldEvent（WorldEventStore + PendingWorldEvent）
    ② trigger=Normal → BuildScript(evt, speaker, Normal)
    ③ InjectScript

路径② NPC 主动质问
  设 _pendingTrigger=Alert → StartConversation → TryInjectCrimeDialogue
    ① 找 WorldEvent → 应找到！（找不到打 ERROR）
    ② trigger=Alert → BuildScript(evt, speaker, Alert)
       → BuildAlertInterceptScript（内部构建 r + ctx）
    ③ InjectScript

路径③-a 玩家向 NPC 投降
  设 _pendingTrigger=PlayerSurrender → StartConversation → TryInjectCrimeDialogue
    ① 同上
    ② trigger=PlayerSurrender → BuildScript → BuildPlayerSurrenderScript
    ③ InjectScript

路径③-b NPC 向玩家投降
  同上，trigger=NpcSurrender
```

## 不改动的部分

- **`DialogueInjector` 核心引擎**：不碰。
- **`CrimeDialogueBuilder` 内部子树逻辑**：不碰（`BuildAuthorityScript` / `BuildWitnessScript` / `BuildSuspectScript` / `BuildBystanderScript` / `BuildAlertInterceptScript` / `BuildPlayerSurrenderScript` / `BuildNpcSurrenderScript` 全部保留）。

## 改动清单

### 1. `CrimeDialogueBuilder.cs` — `BuildScript` 成为唯一分派点（~25 行变更）

**新增 `DialogueTrigger` 枚举**：

```csharp
public enum DialogueTrigger
{
    Normal,           // 玩家主动交谈 → 按 speaker 身份分派
    Alert,            // NPC 主动质问 → BuildAlertInterceptScript
    PlayerSurrender,  // 玩家认输 → BuildPlayerSurrenderScript
    NpcSurrender      // NPC 投降 → BuildNpcSurrenderScript
}
```

**`BuildScript` 加 `trigger` 参数**：

```csharp
public static DialogueInjector.DialogueInjectScript BuildScript(
    Hero speaker, Hero listener, WorldEvent evt,
    DialogueTrigger trigger = DialogueTrigger.Normal,
    ConfrontationType? alertConfrontation = null,
    PlayerActionType? alertTriggerAction = null)
{
    // evt 为 null 时仅 Alert trigger 放行（纯警戒质问，无关联犯罪事件）
    if (evt == null && trigger != DialogueTrigger.Alert) return null;

    // ── trigger 优先分派 ──
    switch (trigger)
    {
        case DialogueTrigger.Alert:
            return BuildAlertInterceptScriptInternal(speaker, listener, evt,
                alertConfrontation, alertTriggerAction);

        case DialogueTrigger.PlayerSurrender:
            return BuildPlayerSurrenderScript();

        case DialogueTrigger.NpcSurrender:
            return BuildNpcSurrenderScript(
                speaker?.Name?.ToString() ?? listener?.Name?.ToString() ?? "对方");
    }

    // ── Normal：按 speaker 身份分派 ──
    PlaceholderResolver r = new PlaceholderResolver(evt, speaker, listener);
    Agent speakerAgent = Campaign.Current?.ConversationManager?.OneToOneConversationAgent as Agent;
    IntentContext ctx = new IntentContext(speakerAgent, speaker: speaker, worldEvent: evt);

    DialogueInjector.DialogueInjectScript result;

    // ── 模板 NPC（speaker==null）兼容性审计 ──
    //   IsAuthority → null-safe（npc?.Occupation），模板 NPC 永远不命中 ✅
    //   Witness    → speaker.StringId 匹配证词 + SilenceWitness 记录身份
    //                模板 NPC 可当目击者（RegisterWitness 已支持 TemplateId），
    //                但 BuildWitnessScript 未适配 → 暂落 Bystander ⚠️ TODO
    //   Suspect    → SuspectHeroId 是 Hero StringId，模板 NPC 当嫌疑人需改
    //                数据模型 → 暂落 Bystander ⚠️
    //   Bystander  → 全程 PlaceholderResolver，完全兼容 ✅
    //   扩展方式：加 TemplateId 匹配的 else if 即可，不需改结构。
    if (IsAuthority(speaker, evt))                             // null-safe: npc?.Occupation
        result = BuildAuthorityScript(r, ctx);
    else if (evt.WitnessHeroIds?.Contains(speaker?.StringId) == true)  // 🆕 speaker?
        result = BuildWitnessScript(r, ctx);                   // ⚠️ 仅 Hero 目击者
    else if (evt.SuspectHeroId == speaker?.StringId)                  // 🆕 speaker?
        result = BuildSuspectScript(r, ctx);                   // ⚠️ 仅 Hero 嫌疑人
    else
        result = BuildBystanderScript(r, ctx);                 // 自然兜底（模板 NPC ✅）

    LogScript(result, $"[CrimeDialog] speaker={speaker?.Name ?? "(template)"} stage={evt.Stage}");
    return result;
}

/// <summary>Alert 路径的内部适配：从原始参数构建 PlaceholderResolver + IntentContext，调 BuildAlertInterceptScript。
/// evt 可为 null（纯警戒质问，无关联犯罪事件）。</summary>
private static DialogueInjector.DialogueInjectScript BuildAlertInterceptScriptInternal(
    Hero speaker, Hero listener, WorldEvent evt,
    ConfrontationType? confrontation, PlayerActionType? triggerAction)
{
    // evt 为 null 时用无 WorldEvent 的 PlaceholderResolver 构造器，{CRIME} 等占位符回落空串
    var r = evt != null
        ? new PlaceholderResolver(evt, speaker, listener)
        : new PlaceholderResolver(speaker, listener, targetName: null, itemName: null);
    r.SpeakingWitness = AgentAIController.Instance?.PendingWorldEvent
        ?.WitnessTestimonies?.FirstOrDefault(t => t.WitnessHeroId == speaker?.StringId);

    var agent = Campaign.Current?.ConversationManager?.OneToOneConversationAgent as Agent;
    var ctx = new IntentContext(agent, speaker: speaker, worldEvent: evt);
    ctx.Confrontation = confrontation ?? ConfrontationType.Deter;
    ctx.TriggerAction = triggerAction ?? PlayerActionType.Crouching;

    return BuildAlertInterceptScript(r, ctx);
}
```

**关键变化**：
- Alert 路径的 `PlaceholderResolver` + `IntentContext` 构建从 `AlertForceConversationAction.OnStart` 移入 `BuildScript`，调用方只需传 `ConfrontationType` + `PlayerActionType` 两个原始值。
- Surrender 路径不需要额外上下文，直接分派。
- Normal 路径逻辑不变，只是加了 null speaker → BystanderScript 守卫。

### 2. `ConversationEntryPatch.cs` — TryInjectCrimeDialogue 简化（~50 行变更）

**新增静态字段**：

```csharp
internal static DialogueTrigger _pendingTrigger = DialogueTrigger.Normal;

// 以下两个字段仅 Alert trigger 使用；其他 trigger 保持 default 即可
internal static ConfrontationType _pendingConfrontation;
internal static PlayerActionType _pendingTriggerAction;
```

**重构 `TryInjectCrimeDialogue`**：

```csharp
internal static void TryInjectCrimeDialogue(Hero partner)
{
    // ── 1. 查找关联 WorldEvent（两层：持久化存储 + Mission 作用域）──
    Settlement settlement = Settlement.CurrentSettlement
        ?? partner?.CurrentSettlement
        ?? Hero.MainHero?.CurrentSettlement;

    WorldEvent evt = null;
    if (settlement != null)
    {
        evt = WorldEventStore.FindActive(settlement.StringId)
            ?? AgentAIController.Instance?.PendingWorldEvent;
    }

    // ── 2. 消费 trigger ──
    var trigger = _pendingTrigger;
    var confrontation = _pendingConfrontation;
    var triggerAction = _pendingTriggerAction;
    _pendingTrigger = DialogueTrigger.Normal;
    _pendingConfrontation = default;
    _pendingTriggerAction = default;

    // ── 3. 设计契约：Surrender 必须有关联 WorldEvent；Alert 可以无事件（纯警戒质问）──
    if (trigger != DialogueTrigger.Normal && evt == null)
    {
        if (trigger == DialogueTrigger.Alert)
        {
            // 无 WorldEvent 的纯警戒质问（玩家蹲下/拔刀被看见，但尚未造成犯罪事件）。
            // BuildAlertInterceptScriptInternal 内部用无 evt 的 PlaceholderResolver 构造器，
            // {CRIME} 等占位符回落空串，对话仍然正常注入。
            DebugLogger.Log($"[ConvEntry] Alert trigger without WorldEvent — proceeding with generic confrontation.");
        }
        else
        {
            // Surrender 必须有关联 WorldEvent（投降一定发生在战斗/犯罪现场）
            DebugLogger.Log($"[ConvEntry] ERROR: trigger={trigger} but no WorldEvent (store or pending)! " +
                "Dialogue will be skipped — this is a design contract violation.");
            return;
        }
    }

    // ── 4. 无事件 + Normal trigger → 清理退出（Alert 已在步骤 3 放行，继续往下）──
    if (evt == null && trigger == DialogueTrigger.Normal)
    {
        if (_lastInjectedTag != null)
        {
            DialogueInjector.RemoveRelatedLines(_lastInjectedTag);
            _lastInjectedTag = null;
            _lastInjectedEventId = null;
        }
        return;
    }

    // ── 5. 防重复注入 ──
    string partnerKey = partner?.StringId ?? "(template)";
    string eventKey = evt.EventId;
    if (_lastInjectedEventId == eventKey + "_" + partnerKey)
        return;

    // ── 6. 统一走 BuildScript ──
    string tag = $"crime_{evt.EventId}";
    DialogueInjector.RemoveRelatedLines(tag);

    var script = CrimeDialogueBuilder.BuildScript(
        partner, Hero.MainHero, evt, trigger, confrontation, triggerAction);

    if (script != null && script.Nodes?.Count > 0)
    {
        DialogueInjector.InjectScript(script, tag);
        _lastInjectedEventId = eventKey + "_" + partnerKey;
        _lastInjectedTag = tag;
        DebugLogger.Log($"[ConvEntry] Injected dialogue: event={evt.EventId} stage={evt.Stage} " +
            $"trigger={trigger} partner={partner?.Name ?? "(template)"} nodes={script.Nodes.Count}");
    }
}
```

**`EndConversation` Postfix**：清理 `_pendingTrigger` / `_pendingConfrontation` / `_pendingTriggerAction`，以及删除 `AlertForceConversationAction.PendingAlertScript` / `PendingAlertLabel` 残留清理代码（这两个字段将不再使用）。

**`StartConversation` Prefix（双重保险）**：每次新对话开始时，如果上一个 trigger 未被消费（异常退出/脚本错误导致 EndConversation 未触发），强制重置并打 WARNING：
```csharp
// StartConversation Prefix: 防御 stale trigger 泄漏
if (_pendingTrigger != DialogueTrigger.Normal)
{
    DebugLogger.Log($"[ConvEntry] WARNING: stale trigger {_pendingTrigger} not consumed, force-resetting");
    _pendingTrigger = DialogueTrigger.Normal;
    _pendingConfrontation = default;
    _pendingTriggerAction = default;
}
```

**可删除**：`AlertForceConversationAction.PendingAlertScript` / `PendingAlertLabel` 静态字段及其在 EndConversation 中的清理代码。`AlertScriptDeferredInjectionPatch` 改用 `ConversationEntryPatch._pendingTrigger` 判断是否需要延迟注入。

### 3. `AtomicAction.cs` — AlertForceConversationAction.OnStart（~15 行删除 + 4 行新增）

删除：
```csharp
var r = new PlaceholderResolver(worldEvt, npcHero, Hero.MainHero);
r.SpeakingWitness = pending?.WitnessTestimonies?.FirstOrDefault(...);
var ctx = new IntentContext(agent, speaker: npcHero, worldEvent: worldEvt);
ctx.Confrontation = detail;
ctx.TriggerAction = primaryAction ?? PlayerActionType.Crouching;
var script = CrimeDialogueBuilder.BuildAlertInterceptScript(r, ctx);
string injectResult = DialogueInjector.InjectScript(script, label);
```

改为：
```csharp
ConversationEntryPatch._pendingTrigger = DialogueTrigger.Alert;
ConversationEntryPatch._pendingConfrontation = detail;
ConversationEntryPatch._pendingTriggerAction = primaryAction ?? PlayerActionType.Crouching;
```

（`ConfrontationType detail` 的推导逻辑保留，它是从 `brain.PrimaryAction` 算出来的，`BuildScript` 不持有 brain 引用）

### 4. `CombatManager.cs` — 两处投降方法（~6 行删除 + 4 行新增）

**`PlayerSurrenderToAgent`**：删除 `BuildPlayerSurrenderScript()` + `InjectScript()` → 改为：
```csharp
ConversationEntryPatch._pendingTrigger = DialogueTrigger.PlayerSurrender;
```

**`AcceptAgentSurrender`**：删除 `BuildNpcSurrenderScript(npcName)` + `InjectScript()` → 改为：
```csharp
ConversationEntryPatch._pendingTrigger = DialogueTrigger.NpcSurrender;
```

### 5. `ConversationEntryPatch.cs` — `AlertScriptDeferredInjectionPatch` 字段名替换（~3 行变更）

`AlertScriptDeferredInjectionPatch`（`ProcessSentence` Postfix）逻辑不变，仅将判断条件从 `AlertForceConversationAction.PendingAlertScript != null` 改为 `_pendingTrigger == DialogueTrigger.Alert`。不再读取 `PendingAlertScript` / `PendingAlertLabel`。

## 不改动的文件

- `DialogueInjector.cs` — 核心引擎不变
- `PlaceholderResolver.cs` — 已有 null 守卫
- `AttitudeSystem.cs` — 已有 null 守卫
- `IntentContext.cs` — 已有 null 守卫
- 所有 Intent 类 — 不碰

## null-safety 审计（CrimeDialogueBuilder.BuildScript Normal 路径）

| 方法 | 为什么 null speaker 安全 |
|------|--------------------------|
| `AttitudeSystem.GetSocialIdentity(null)` | 返回 `""` |
| `AttitudeSystem.GetSelfReference(null)` | 返回 `"我"` |
| `AttitudeSystem.GetPlayerAddress(null)` | 返回 `"你"` |
| `AttitudeSystem.ComputeStance(null, evt)` | 返回 `new NpcStance()`（全零 → 情绪 `"冷淡"`、身份 `"村民"`） |
| `PlaceholderResolver(evt, null, listener)` | 所有 speaker 字段用 `speaker?.Name` |
| `IntentContext(agent, speaker: null, ...)` | `resolvedSpeaker` 为 null → `IsHero=false`，走非 Hero 分支 |
| `NpcSpeechResolver.Resolve(id, null, ...)` | 内部只创建 PlaceholderResolver，同上 |
| `BuildBystanderScript` | 全程通过 PlaceholderResolver 间接访问 speaker |
| `BuildAlertInterceptScript` | 同上 |

## 验证要点

1. **模板 NPC + 活跃 WorldEvent**：进村庄跟模板 NPC 对话 → BystanderScript 八卦对话
2. **Alert 质问**：偷东西被目击 → NPC 主动走过来 → Alert 质问对话
3. **Alert + 活跃 WorldEvent**：已有犯罪事件的村里再偷东西 → Alert 优先，不触发 WorldEvent 路径
4. **玩家投降**：战斗中玩家向 NPC 认输 → `BuildPlayerSurrenderScript` 对话
5. **NPC 投降**：战斗中接受 NPC 认输 → `BuildNpcSurrenderScript` 对话
6. **普通犯罪对话不受影响**：进有活跃事件的村，跟村长/目击者/嫌疑人/路人说话 → 各自正确分派
7. **Alert/Surrender 无 WorldEvent**：
   - Alert 无 WorldEvent（纯警戒质问，如蹲下被看见）：正常注入质问对话，不崩不跳过
   - Surrender 无 WorldEvent：打 ERROR 日志，跳过注入（不崩）
8. **`_pendingTrigger` 不泄漏**：对话正常结束/异常退出后，下次对话不会消费到旧的 trigger（EndConversation Postfix + StartConversation Prefix 双重保险）

## wheels.md 待更新内容

重构完成后更新以下章节：

- **"对话中标记 → EndConversation 延迟处理"**：删除 `PendingAlertScript`/`PendingAlertLabel`，改为 `_pendingTrigger`/`_pendingConfrontation`/`_pendingTriggerAction`
- **"原版对话流注入"** 的 `CrimeDialogueBuilder` 小节：`BuildScript` 新增 `DialogueTrigger` 参数，四路径统一分派拓扑图，调用方只设 trigger 不构建脚本
- **新增**："对话注入统一入口"小节 — `TryInjectCrimeDialogue` 两级 WorldEvent 查找 + trigger 消费 + BuildScript 唯一分派点

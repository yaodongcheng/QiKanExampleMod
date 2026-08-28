# dialogue — 轮子速查分卷（wheels.md 索引导航）
## 大世界地图对话 → 真对话 Mission 接入

**咽喉补丁 `CampaignMapConversation.OpenConversation` + inquiry 分流 + 真 Mission + 自定义对话管线复用。**

覆盖场景：玩家在大世界沙盘遇到中立/未开战部队时，弹 inquiry 让玩家选「原版对话 / 新版对话」，选新版则开真对话 Mission（真实 Agent + MissionScreen），自动触发本 mod 的 `InteractionMissionView` 自定义对话管线，零重构复用现有 Agent 演出/镜头/意图引擎。

```csharp
// 1. 设置静态标志（在 inquiry 回调里，开 mission 前）
MapEncounterDialogState.Active = true;
MapEncounterDialogState.Partner = conversationPartnerData.Character;
CampaignMission.OpenConversationMission(p, q);   // 开真对话 mission

// 2. Harmony 拦截咽喉（自动生效，PatchAll 注册）
[HarmonyPatch(typeof(CampaignMapConversation), nameof(CampaignMapConversation.OpenConversation))]
public static class ConversationEntryPatch
{
    [HarmonyPrefix]  // 大地图遇敌 → 弹 inquiry 分流
    [HarmonyPostfix] // 定居点对话 → 犯罪事件注入
}

// 3. Harmony 抑制原版 ConversationMissionLogic.OnMissionTick（仅对我们的 mission）
[HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
public static class SuppressVanillaConversationMissionPatch
{
    [HarmonyPrefix]
    public static bool Prefix() => !MapEncounterDialogState.Active; // Active → 跳过原版 tick
}

// 4. InteractionMissionView 自动触发 + 收尾（已在 OnMissionTick/OnDialogueEnded/Finalize 中集成）
//    - OnMissionTick：检测 Active → 按 Partner CharacterObject 在 Mission.Current.Agents 中精确定位 partner Agent
//    - StartFreeConversationFlow(partnerAgent)：复用现有对话管线（VM/控制器/镜头/意图引擎）
//    - OnDialogueEnded：MapEventHelper.OnConversationEnd() → Mission.Current.EndMission() → 回大地图
//    - OnMissionScreenFinalize：安全清标志（防 ESC 退出泄漏）
```

**关键文件**：`Interaction/Dialogue/MapEncounterDialogState.cs`（静态标志）、`Interaction/Dialogue/ConversationEntryPatch.cs`（对话入口统一拦截 + 犯罪对话注入 + 原版 tick 抑制）、`Interaction/InteractionMissionView.cs`（自动触发/收尾）。

**边界**：只对 Hero 生效（无 Hero 放行原版）；仅自家的 conversation mission 抑制（静态 gate）；settlement 内点 NPC / 请求会面不受影响；LLM 路径走 `IsLLMConfigured` 总闸。

---

# 对话中标记 → EndConversation 延迟处理 — `Interaction/Dialogue/ConversationEntryPatch.cs`

**Intent 在对话中途（OnSuccess/OnInstant）触发了 Mission 层副作用（Agent FadeOut / 战斗 / 关押），但副作用如果在对话窗口关闭前执行会导致视觉异常（NPC 一边说话一边消失、战斗覆盖对话 UI）。解决方案：Intent 只设静态标记，副作用延迟到 `ConversationManager.EndConversation` Postfix 统一执行。**


---

## 已接入的延迟操作

| 标记字段 | 设置位置 | EndConversation 消费 |
|----------|---------|---------------------|
| `WalkAwayIntent.PendingInquiryTitle/Body` | `WalkAwayIntent.OnInstant` | `InformationManager.ShowInquiry` 弹窗 |
| `AlertForceConversationAction.PendingAlertScript/Label` | Alert 注入流程 | 清理残留 |
| `AlertForceConversationAction.ActiveConversationAgent` | Alert 注入流程 | `BroadcastEvent("EndInteraction")` 释放 NPC |
| `ThreatIntent.PendingCombatAgent` | `ThreatIntent.OnFail` | `SendEventToAgent("DeferredCombat")` 延迟开战 |
| `SurrenderJailIntent.PendingJailExit` | `SurrenderJailIntent.OnSuccess` | `TakePrisonerAction.Apply(settlement.Party, Hero.MainHero)` 坐牢 |
| `LureArrestIntent.PendingFadeAgent` | `LureArrestIntent.OnSuccess` | `Agent.FadeOut(false, true)` 淡出消失 |


---

## 模式模板

```csharp
// 1. Intent 侧 — 设标记（不直接执行 Mission 层操作）
public class MyIntent : IntentBase
{
    public static Agent PendingFadeAgent; // 或其它待消费的状态

    public override void OnSuccess(IntentContext ctx)
    {
        // Campaign 层操作可以立即执行（与 Mission 视觉无关）
        TakePrisonerAction.Apply(...);
        InformationManager.DisplayMessage(...);

        // Mission 层副作用 → 只设标记，不立即执行
        if (ctx.IsInMission && ctx.Agent != null)
            PendingFadeAgent = ctx.Agent;
    }
}

// 2. ConversationEntryPatch.ResetCrimeDialogueOnConversationEndPatch.Postfix — 消费标记
[HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.EndConversation))]
public static class ResetCrimeDialogueOnConversationEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        // ... 其它清理 ...

        var fadeAgent = MyIntent.PendingFadeAgent;
        if (fadeAgent != null)
        {
            MyIntent.PendingFadeAgent = null;
            try
            {
                if (fadeAgent.IsActive())
                    fadeAgent.FadeOut(false, true);
            }
            catch (Exception ex) { DebugLogger.Log($"[ConvEnd] FadeOut failed: {ex.Message}"); }
        }
    }
}
```


---

## 为什么不能直接在 Intent 里 FadeOut

`ExecuteIntentAction` → `OnSuccess` 在 `AddPlayerLine` 的 `onConsequence` 回调中执行，早于对话引擎推进到下一句。如果此时 FadeOut，NPC Agent 会在一句台词还没说完时就消失——视觉出戏。延后到 `EndConversation` 则确保所有对话文本播放完毕、窗口关闭后 Agent 才淡出。

**关键文件**：`Interaction/Dialogue/ConversationEntryPatch.cs`（EndConversation Patch + 所有延迟消费）、`Interaction/Intents/AccountabilityIntents.cs`（LureArrestIntent / ThreatIntent / WalkAwayIntent 等标记字段）。

---

# 世界事件引擎 — `WorldEvent/`


---

## KCD2 式轮次对话

**所有 NPC 交互统一流程**：NPC 先说开场白（右侧无选项）→ 玩家点"继续" → 选项出现。

```csharp
// StartInteraction 模式：
_vm.Show(name, openingLine);       // NPC 说话
_vm.AreOptionsVisible = false;      // 隐藏选项

_vm.OnClickContinue = () =>         // 玩家点"继续"
{
    RefreshInitialOptions();         // 选项出现
};
```

**关键文件**：`Interaction/StoryDialogVM.cs`（`OnClickContinue` 回调 + `ShowContinueHint` 属性）、`Interaction/InteractionController.cs`（`StartInteraction`）。

---

# 屏幕上方快速提示 — `MBInformationManager.AddQuickInformation`

**静态方法，屏幕上方弹出简短提示，几秒后自动消失（toast 风格）。与 `InformationManager.DisplayMessage`（左下角消息日志，持久保留）是不同显示位置。**

```csharp
// 静态方法，直接调
MBInformationManager.AddQuickInformation(TextObject text);

// 典型用法：任务进度、检定结果、系统瞬间通知
MBInformationManager.AddQuickInformation(new TextObject("{=...}你已消灭 {COUNT}/{TOTAL} 队匪徒")
    .SetTextVariable("COUNT", 3)
    .SetTextVariable("TOTAL", 5));

// 简单文本
MBInformationManager.AddQuickInformation(new TextObject("潜行检定成功"));

// 和 DisplayMessage 的区别：
//   AddQuickInformation → 屏幕上方，短暂弹出，自动消失（类似成就弹出）
//   DisplayMessage      → 左下角消息日志，持久保留，可翻阅
```

**DLL**: `TaleWorlds.CampaignSystem.dll` → `MBInformationManager`（namespace `TaleWorlds.CampaignSystem`）

**适用场景**：任务进度更新、技能检定成功/失败、瞬间反馈通知。**不适用**：需要玩家回顾查阅的长文本、历史记录。

**调试日志**：所有 `AddQuickInformation` 调用已通过 `AddQuickInformationLoggerPatch`（Harmony Prefix）自动写入 `DebugLogger`，搜 `[AddQuickInformation]` 即可追踪。

**文件位置**：`Debug/AddQuickInformationLoggerPatch.cs`（Harmony 日志补丁）

---

# 日志纪律

**铁律**：① `DebugLogger.Log` 只记录玩家可感知的事 + 关键后台状态变更。② Per-NPC 循环日志是垃圾——每轮扫描最多一条汇总。③ 错误始终记录。

```csharp
// ✅ 好的日志
DebugLogger.Log($"[Player] NinjaReport: {summary}");            // 玩家看到通知
DebugLogger.Log($"[Player] Inquiry: '去看看' — {loc}");          // 玩家做出选择
DebugLogger.Log($"[Player] Talk to: {npcName}");                 // 玩家跟谁说话
DebugLogger.Log($"[WorldEvent] New event: {type} at {loc}");     // 事件创建
DebugLogger.Log($"[WorldEvent] Motivated conflict: A → B");     // 真人冲突
DebugLogger.Log($"[CommissionIssue] {settlement}: scanned {n} NPCs, created {m} issues"); // 轮次汇总

// ❌ 垃圾日志（每条占一行，2500行淹没13行有用信息）
// GetAvailableDefs / HasCommissionsFor / OnCheckForIssue — 逐NPC日志全砍
```

**日志前缀约定**：`[Player]` = 玩家感知事件，`[WorldEvent]` = 世界事件生命周期，`[Commission*]` = 委托系统关键节点。

---

# 村庄动物偷窃与库存同步 — `Stealth/VillageAnimalTracker.cs` + `Interaction/InteractionMissionView.cs`

场景动物（羊/牛/猪/鹅/鸡）偷窃系统，带持久化追踪、自然恢复、ItemRoster 自动同步、价格修正。


---

## 设计原则

| 场景 | 对话 UI | 何时用 |
|------|---------|--------|
| **Quest / Issue 对话** | 🔴 原版 `ConversationManager` + JSON 注入 | 任务接取、进行中讨论、任务目标对话——老玩家熟悉的原版体验 |
| **闲聊 / 自由对话** | `StoryDialogVM`（已有轮子） | 非任务场景的 NPC 互动、LLM 自由生成 |

**优先原版**：凡是能挂到 `hero_main_options` / `issue_offer` / `quest_offer` token 的，走 JSON 注入。只有原版 token 体系覆盖不了的场景（如大地图偶遇、无 Hero 的平民）才用 StoryDialogVM。


---

## 🔴 对话注入铁律

> **总原则见 CLAUDE.md 铁律 8「所有 Agent 平等互动」。** 以下为对话系统的具体落地要求。

### 铁律 A：对话入口不因"无 Hero"拒绝

所有对话入口（`TryInjectCrimeDialogue`、`BuildScript`、`PlaceholderResolver`、`IntentContext`）必须兼容 `speaker == null`。模板 NPC 自然走完身份分派链，不命中 Hero 身份检查时落 `BuildBystanderScript`。

```csharp
// ❌ 禁止：在对话入口处拦截模板 NPC
if (partner == null) return;

// ✅ 正确：模板 NPC 自然走完分派链，null-conditional 防 NRE
if (IsAuthority(speaker, evt)) ...                          // null-safe: npc?.Occupation
else if (evt.WitnessHeroIds?.Contains(speaker?.StringId) == true) ...
else if (evt.SuspectHeroId == speaker?.StringId) ...
else result = BuildBystanderScript(r, ctx);                  // 自然兜底
```

拦截白名单：**只有必须记录 Hero StringId 的场景才拦截**（如栽赃陷害 `INTENT:FrameSuspect`），通用互动（战斗、偷窃、贿赂、威胁、投降、八卦）一律放行。

### 铁律 B：对话注入统一收口 `StartConversation`

**所有对话注入——不管是玩家主动交谈、NPC 主动质问、还是战斗投降——都必须经过 `MissionConversationLogic.StartConversation`（或其 Prefix/Postfix `TryInjectCrimeDialogue`）统一处理。** 禁止调用方自己调 `DialogueInjector.InjectScript` 然后自己调 `StartConversation`。

```csharp
// ❌ 禁止：调用方自己注入 + 自己开对话
BuildAlertInterceptScript(r, ctx);
DialogueInjector.InjectScript(script, label);
conversationLogic.StartConversation(agent, true, false);

// ✅ 正确：调用方只设 trigger，TryInjectCrimeDialogue 统一注入
ConversationEntryPatch._pendingTrigger = DialogueTrigger.Alert;
ConversationEntryPatch._pendingConfrontation = detail;
ConversationEntryPatch._pendingTriggerAction = primaryAction;
conversationLogic.StartConversation(agent, true, false);
// → Prefix/Postfix 触发 TryInjectCrimeDialogue → BuildScript(trigger=Alert) → InjectScript
```

**为什么**：`StartConversation` 的 Patch 是唯一能保证"每次对话启动时只注入一次"的关口。调用方各自注入会导致双重注入、token 竞争、以及 `_lastInjectedEventId` 防重复机制失效。

**Prefix vs Postfix 分工**：

| Patch | 处理哪些 Trigger | 注入时机 | 注入模式 |
|-------|-----------------|---------|---------|
| **Prefix** | `PlayerSurrender` / `NpcSurrender` / `Alert` | `StartConversation` **之前** | `SkipVanillaOpening=true` — NPC 台词挂在 `start` token（优先级 200）覆盖原版开场白 |
| **Postfix** | `Normal` | `StartConversation` **之后** | Gateway 模式 — 在 `hero_main_options` 挂 PlayerLine入口，保留原版开场白 |

**为什么 Prefix 必须处理 SkipVanillaOpening 的 trigger**：`InjectScriptNoOpening` 往 `start` token 注入高优先级 NPC 台词来覆盖原版开场白。这必须在 `StartConversation` 处理 `start` token **之前**完成。Postfix 注入时 `start` token 已经被原版引擎评估完毕，注入的台词要到下一轮对话才生效——原版开场白已经播放了。

**防重复注入**：Prefix 消费 trigger 后会设 `_lastInjectedEventId`。Postfix 中的 `TryInjectCrimeDialogue` 检查 dedup 命中 → 跳过，不会二次注入。


---

## v2 新模型（2026-07-12 重构）

**核心原则：Transition 只管路由，NPC 台词统一在 DialogueNode.NpcLine。**

```
Node = NPC 说一句话 + 玩家可选的动作集合
Transition = 玩家选了一个动作 → 执行 → 路由到下一个 Node（或关窗）
```

### 数据类型

```csharp
public enum TransitionCheckType { None, SkillCheck }

public class DialogueNode {
    public string Id = "injectedStart";
    public string NpcLine;                        // NPC 台词唯一入口
    public Func<string> LazyNpcLine;              // 惰性求值（设置后覆盖 NpcLine）
    public List<DialogueTransition> Transitions;  // [] = terminal, null = 非法
}

public class DialogueTransition {
    public string PlayerLine;                     // 玩家选项文本
    public TransitionCheckType CheckType = None;  // None=直连 / SkillCheck=检定分叉
    public string Action = "NONE";                // NONE / INTENT:xxx
    public string ActionParam = null;             // 字符串参数（系统 Intent 用数值的字符串表示）
    public string NextNodeOnSuccess;              // 成功/无检定 → 目标 Node Id。""/null = 关窗
    public string NextNodeOnFail;                 // 检定失败 → 目标 Node Id（仅 SkillCheck）
}
```

### 路由逻辑

- **CheckType.None**：PlayerLine 直连目标 Node 的 entry token（或 close_window）
- **CheckType.SkillCheck**：桥接 token + 3 条 silent DialogLine（成功/失败/安全网）→ 目标 Node

### JSON 格式（v2）

文件放在 `ModuleData/DesignData/Dialogues/*.json`。

```json
{
  "InjectAtToken": null,
  "EntryOption": "（闲聊）…",
  "EntryNode": "injectedStart",
  "Nodes": [
    {
      "Id": "injectedStart",
      "NpcLine": "啊，你来得正好！",
      "Transitions": [
        {
          "PlayerLine": "什么怪事？",
          "NextNodeOnSuccess": "more_detail"
        },
        {
          "PlayerLine": "帮你处理，有什么好处？",
          "Action": "INTENT:GiveGold",
          "ActionParam": "100",
          "NextNodeOnSuccess": "give_gold_ack"
        }
      ]
    },
    {
      "Id": "give_gold_ack",
      "NpcLine": "你果然是个精明人——100第纳尔，怎么样？",
      "Transitions": [
        { "PlayerLine": "…", "NextNodeOnSuccess": "more_detail" }
      ]
    }
  ]
}
```

**已删除的旧字段**：`SpeakerIndex`（硬编码 0）、`NpcResponse` / `NpcResponseOnSuccess` / `NpcResponseOnFail` / `LazyNpcResponse`（台词统一在 Node.NpcLine）、`NextNode`（→ NextNodeOnSuccess）、`ActionValue`（→ ActionParam）。

**旧式 Action 迁移**：`INCREASE_RELATION` → `INTENT:IncreaseRelation`、`DECREASE_RELATION` → `INTENT:DecreaseRelation`、`GIVE_GOLD` → `INTENT:GiveGold`、`TAKE_GOLD` → `INTENT:TakeGold`。数值从 `ActionValue` 迁移到 `ActionParam`。


---

## 核心 API

```csharp
// JSON 注入
DialogueInjector.InjectFromJson(jsonPath);    // → string 结果描述
// 运行时构建注入（CrimeDialogueBuilder 用）
//   script.SkipVanillaOpening == false → Gateway 模式：在 hero_main_options 挂 PlayerLine 入口
//   script.SkipVanillaOpening == true  → 直挂模式：NPC 台词直接挂在 start token（优先级 200），覆盖原版开场白
DialogueInjector.InjectScript(script, debugLabel);
// 清理
DialogueInjector.ClearAll();
DialogueInjector.RemoveRelatedLines(label);
// 调试
DialogueInjector.LogScript(script, label);
```

> **已删除**：`InjectScriptAsOpening` 已合并到 `InjectScript`。旧代码设 `InjectAtToken = "start"` + 调 `InjectScriptAsOpening`，新代码设 `SkipVanillaOpening = true` + 调 `InjectScript`。`InjectScript` 内部读取 `SkipVanillaOpening` 自动选择 `InjectScriptNoOpening`（直挂）或 `InjectScriptGateway`（入口选项）。


---

## CrimeDialogueBuilder 辅助方法

```csharp
// Node 工厂
Node(id, npcLine, next=null)         // NPC 说一句 → next 为 null 关窗，非 null 跳转
LazyNode(id, lazyNpcLine, next=null) // 惰性求值版，同上

// Transition 工厂
WalkAway(playerLine)               // INTENT:WalkAway → 关窗
ContinueOptions(walkAwayLine)      // ["我得走了。"→关窗]

// continue_chat
BuildContinueChatNode(r)           // "还有什么别的想说的?" + walk→farewell
BuildFarewellNode(r)               // 惰性告别语（阶段感知）
AddContinueChatWithFarewell(nodes, r) // 同时加两个
```


---

## CrimeDialogueBuilder 子树自包含原则

**一个函数内构造的 `DialogueNode`，其 `Transitions` 中出现的每一个 `NextNodeOnSuccess` / `NextNodeOnFail`，必须在本函数内有明确的归宿——要么 `nodes.Add()` 创建，要么显式声明复用已有节点。读一个函数就应该能看到完整的对话子图，不需要跳到调用方去拼。**

### 正确 vs 错误

```csharp
// ❌ 错误：BuildConfessNode 定义了 transition → "charm_ok"，
//    但 charm_ok 在本函数内既没有创建也没有声明依赖——
//    读到这里不知道 "charm_ok" 是什么、谁加的、加了没有。
nodes.Add(BuildConfessNode(r, ctx));         // 函数返回就结束了，NextNode 下落不明
nodes.Add(AckNode("charm_ok", ...));         // 目标节点在调用方——跳来跳去拼图
nodes.Add(AckNode("charm_fail", ...));

// ✅ 正确：子树方法内，每个 NextNode 都能在同函数里找到 nodes.Add() 或嵌套子树调用。
BuildConfessSubtree(nodes, r, ctx);          // 一行，所有下游归宿在函数体内可见
```

### 实现方式

**方式 A — 子树方法（推荐）**：`void` 方法，接收 `List<DialogueNode> nodes`。本函数内构造的 Node，其 Transition 指向的每个目标，都在本函数内通过 `nodes.Add()` 或嵌套子树完成注册。

```csharp
static void BuildConfessSubtree(List<DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
{
    // 本函数创建的 confess 节点，transition 引用了三个 NextNode：
    //   "charm_ok"      → 下一行 nodes.Add(AckNode(...)) 创建 ✅
    //   "charm_fail"    → 再下一行 nodes.Add(AckNode(...)) 创建 ✅
    //   "restitution_detail" → BuildRestitutionSubtree 内部创建 ✅
    nodes.Add(new DialogueNode { Id = "confess", NpcLine = "...", Transitions = {
        new() { PlayerLine = "我愿意赔。", NextNodeOnSuccess = "restitution_detail" },
        new() { PlayerLine = "开个玩笑…", NextNodeOnSuccess = "charm_ok", NextNodeOnFail = "charm_fail" },
    }});
    nodes.Add(AckNode("charm_ok", "..."));
    nodes.Add(AckNode("charm_fail", "..."));
    BuildRestitutionSubtree(nodes, r, ctx);  // 嵌套子树，restion_detail + pay_ack 在内部创建
}
```

**方式 B — 依赖共享 Node**：目标 Node 是 `continue_chat`、`farewell` 等全局共享节点。本函数不重复创建，但**必须在注释中显式声明依赖**，且调用方通过 `AddContinueChatWithFarewell` 等统一入口添加。

```csharp
/// <summary>证人对话。依赖调用方已添加 continue_chat / farewell（通过 AddContinueChatWithFarewell）。</summary>
static void BuildWitnessScript(...)
{
    // "witness_silence_ack" → next="continue_chat"  ← 共享节点，注释已声明依赖
    nodes.Add(AckNode("witness_silence_ack", "……好吧，我什么也没看见。"));
}
```

### 模式速查

| 模式 | 何时用 | NextNode 归宿在哪 |
|------|--------|-------------------|
| `AckNode(id, line, next)` 工厂 | 单节点，下游是共享节点 | 调用方添加（`next` 默认 `"continue_chat"`） |
| `TerminalNode(id, line)` 工厂 | 单节点无 transition | 无 NextNode，自包含 |
| `BuildXxxSubtree(nodes, ...)` void | 节点有复杂 transition | **全部在本函数内** `nodes.Add()` 或嵌套子树 |
| `BuildXxxSubtree(nodes, ...)` void 共用 | 被多个子树复用 | **全部在本函数内**，调用方不感知细节 |
| ❌ `BuildXxxTransitions(...)` 返回 `List<DialogueTransition>` | — | **禁止**：返回的 Transition 引用外部 Node Id，对调用方有隐式依赖（见下方反模式） |

### ❌ 反模式：返回裸 Transition 列表

**"当前函数" = 构造 Transition 的函数**（`new DialogueTransition { NextNodeOnSuccess = "xxx" }` 写在哪，哪就是当前函数）。这与返回的是 Node 还是 `List<DialogueTransition>` 无关——**只看谁写了 `NextNodeOnSuccess`/`NextNodeOnFail` 的字面值，谁就必须能兑现。**

唯一合法形式：`void BuildXxxSubtree(List<DialogueNode> nodes, ...)`。Transition 构造和 Node 定义在同一函数内闭环。

### 新增/修改对话构建方法时自查

1. **本函数内每个 `new DialogueTransition { NextNodeOnSuccess = "xxx" }`**，**"xxx" 能在本函数体内找到 `nodes.Add()` 或嵌套 `BuildXxxSubtree(nodes, ...)` 吗？** 找不到 → **违规**。
2. 如果有共享依赖（如 `continue_chat`），本函数的注释里**显式声明**了吗？


---

## AckNode 使用纪律：禁止无意义"…"拆句

**`AckNode`（NPC 说一句 → 玩家点"…" → 跳到 next）只能用于收束对话分支，禁止用来把同一段 NPC 发言拆成两个气泡。**

### AckNode 的合法用途

| 场景 | 示例 | 为什么合法 |
|------|------|-----------|
| 分支收束 → 闲聊 | `AckNode("xxx_ack", "知道了。")` → `continue_chat` | 玩家做了选择，NPC 给了最终回应，对话自然收束 |
| 分支收束 → 告别 | `AckNode("xxx_ack", "好，去吧。")` → `farewell` | 同上 |
| 信息确认后继续 | `AckNode("witness_desc_ack", "那人……高个子，红头发。")` → `continue_chat` | 玩家问了具体问题，NPC 回答，合理停顿 |

### ❌ 非法用法：用"…"当胶水

```csharp
// ❌ 同一段 NPC 发言被 "…" 拆成两句——玩家要多点一次，纯摩擦
nodes.Add(AckNode("confess_ack", "你？！……好，既然认了，咱们可以商量。", "confess"));
nodes.Add(BuildConfessNode(r, ctx));  // NpcLine = "有什么要说的？"

// 实机体验：
//   NPC: "你？！……好，既然认了，咱们可以商量。"    ← 情绪反应
//   玩家: "…"                                      ← 无意义点击
//   NPC: "有什么要说的？"                            ← 本应和上一句连在一起
//   玩家: ①赔钱 ②狡辩 ③走人                         ← 终于有选择了
```

### ✅ 修复：合并为一个节点

```csharp
// NPC 的情绪反应和提问在同一句 NpcLine 里，直接给玩家选项
nodes.Add(new DialogueNode {
    Id = "confess",
    NpcLine = "你？！……好，既然认了，咱们可以商量。有什么要说的？",
    Transitions = { 赔钱 / 狡辩 / 走人 }
});
// 实机体验：
//   NPC: "你？！……好，既然认了，咱们可以商量。有什么要说的？"
//   玩家: ①赔钱 ②狡辩 ③走人                         ← 直接选
```

### 自查

给对话图加 AckNode 时问自己：

1. **这个 AckNode 的 next 指向 `continue_chat` / `farewell` / 关窗吗？** → 如果不是（比如指向另一个有实质内容的 node），**大概率是非法拆句**。
2. **能把 AckNode 的 NpcLine 和 next 指向的那个 Node 的 NpcLine 合并成一句吗？** → 如果能合并且不失自然，**就不该拆**。
3. **这个"…"是玩家在确认收到信息（OK），还是 NPC 还没说完话？** → 前者合法，后者必须合并。


---

## Transition 检定纪律：影响 NPC 决策的选项必须有 SkillCheck

**凡是玩家试图影响 NPC 决策的选项——说服、贿赂、威胁、欺骗、讨价还价——必须加 `CheckType = SkillCheck`，禁止写死 NPC 必然接受。玩家不能靠点一个选项就无代价地改变 NPC 行为。**

### 需要检定的典型场景

| 玩家行为 | 对应 Intent 示例 | 不检定的后果 |
|----------|-----------------|-------------|
| 给钱封口 | `INTENT:SilenceWitness` + `ActionParam="bribe"` | 花 50 块钱就能让所有目击者闭嘴——零风险零难度 |
| 威胁恐吓 | `INTENT:SilenceWitness` + `ActionParam="threat"` | 威胁不需要魅力/威慑力，点就有效 |
| 花言巧语开脱 | `INTENT:CharmDefense` | NPC 永远吃这套，毫无挑战 |
| 栽赃陷害 | `INTENT:FrameSuspect` | 随便指一个人 NPC 就信 |

### 正确 vs 错误

```csharp
// ❌ 错误：写死了 NPC 必然接受——贿赂变成免费午餐
new DialogueInjector.DialogueTransition
{
    PlayerLine = "（给些钱）这事你别往外说……",
    Action = "INTENT:SilenceWitness",
    NextNodeOnSuccess = "witness_silence_ack"   // 无检定，必然成功
    // 缺 CheckType、NextNodeOnFail
}

// ✅ 正确：检定决定 NPC 是否被说服，失败有对应的 NPC 回应
new DialogueInjector.DialogueTransition
{
    PlayerLine = "（给些钱）这事你别往外说……",
    CheckType = TransitionCheckType.SkillCheck,   // 检定决定成败
    Action = "INTENT:SilenceWitness",
    ActionParam = "bribe",
    NextNodeOnSuccess = "witness_silence_ack",    // NPC 收了钱
    NextNodeOnFail = "witness_silence_fail"       // NPC 拒绝并扬言举报
}
```

### 不需要检定的例外

| 场景 | 理由 |
|------|------|
| 玩家表示"我先想想"/"我还有事" | 玩家不做决策，只是推迟/离开 |
| NPC 主动提出的交易（悬赏等） | NPC 已经决定，玩家只是接受/不接受 |
| 玩家认栽自首 | 玩家放弃抵抗，NPC 接受是合理的 |
| 无关紧要的信息询问（"详细说说？"） | 不涉及 NPC 利益权衡 |

### 自查

新增 Transition 时问自己：

1. **这个选项是在改变 NPC 的意愿吗？** → 如果是（让他闭嘴、相信你、原谅你、配合你），**必须检定**。
2. **检定失败 NPC 会说什么？** → 必须提供 `NextNodeOnFail` 指向的节点，NPC 拒绝 + 可能有后果。
3. **相关的 Intent 有 Goal 吗？** → 没有 Goal 的 Intent 不会掷骰，检查 `Intent.Goal` 是否已配置。


---

## 控制台指令

```
custom.inject_dialogue test_talk       → 加载并注入 test_talk.json
custom.inject_dialogue clear           → 清除所有注入
```

**文件位置**：`Interaction/Dialogue/DialogueInjector.cs`（注入引擎）、`Interaction/Dialogue/CrimeDialogueBuilder.cs`（运行时构建器）、`Interaction/Intents/SystemIntents.cs`（系统 Intent）。JSON 示例：`ModuleData/DesignData/Dialogues/test_talk.json`。

---

# 🆕 意图/行动/任务统一重构（2026-07-04 新增）


---

## NpcSpeechResolver — 模板台词统一数据源（XML 本地化）

模板思路替代枚举思路。**数据源已从 CSV 迁移到 XML 本地化系统**（`NpcSpeech.csv` / `Narrative.csv` 已废弃，`DesignDataLoad.cs` 不再加载，注释明确"已迁移到 XML"）。
**两阶段回落**：`NpcSpeechResolver.Resolve(id, speaker, listener, evt, targetName, itemName, narrativeFallback)` 内部先查 XML key `LWN_speech_{templateId}` → 未命中且 narrativeFallback 非 null 时回落 `NarrativeResolver.TryResolveText(narrativeFallback)` → 均未命中返回 null。**调用方只需 `??` 硬编码兜底**，不应再手动调 NarrativeResolver。

```csharp
// ✅ 调用方标准写法：两阶段回落
string line = NpcSpeechResolver.Resolve(templateId, speaker, listener,
    narrativeFallback: new NarrativeFilters { ... })
    ?? HardcodedFallback(r, intent, action);

// ❌ 禁止：调用方手动写三层 if-null 回落
// ❌ 禁止：调用方直接调 NarrativeResolver.Resolve/NarrativeResolver.TryResolveText
```

**长期方向（已完成）**：CSV → XML 迁移已全部完成（`DesignDataLoad.cs` 不再加载 Narrative/NpcSpeech）。模板文本统一进 `LWN_speech_*` key，翻译走 `Languages/{lang}/std_*.xml` 分语言覆盖（铁律 13）。

**文件位置**：
- `ModuleData/Languages/std_LivingWorldNpcs_strings.xml`（`LWN_speech_*` keys：12 BubbleSay + 6 L3 开场白等）+ `ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml`（中文翻译）
- `Interaction/Dialogue/NpcSpeechResolver.cs`


---

## PlaceholderResolver 增强 — Mission 层脉冲上下文

新增构造参数 `targetName`/`itemName`，新增占位符：
- `{PLAYER}` / `{SPEAKER}` / `{SPEAKER_SELF}` / `{SPEAKER_PLAYER_ADDR}` / `{SPEAKER_EMOTION}`
- `{TARGET}` / `{ITEM}` / `{StolenItemName}` / `{LOCATION}`

**文件位置**：`Interaction/Dialogue/PlaceholderResolver.cs`


---

## PlaceholderResolver 扩展指南 — 新增占位符两步流程

**调用链路**：`NpcSpeechResolver.Resolve(id, speaker, listener, evt, targetName, itemName, narrativeFallback)` → ① `LWNTextHelper.TryResolveText("LWN_speech_" + id)` 判 key 存在 → `LWNTextHelper.Resolve(xmlKey, new PlaceholderResolver(...))` → ② 未命中且 narrativeFallback 非 null → `NarrativeResolver.TryResolveText(narrativeFallback)` → `r.Resolve(text)` → 正则 `\{(\w+)\}` 扫描 `{KEY}` → 逐个调 `ResolveOne(key)` 替换。

**三种构造 → 三种数据可用范围**：

| 构造 | 使用场景 | Speaker/Listener | TargetName/ItemName | WorldEvent |
|------|---------|:-:|:-:|:-:|
| `(speaker, listener, targetName, itemName)` | 警戒 BubbleSay | ✅ | ✅ | ❌ null |
| `(evt, speaker, listener)` | Campaign 犯罪对话 | ✅ | ❌ null | ✅ |
| `(evt, speaker, listener, targetName, itemName)` | L3 质问台词 | ✅ | ✅ | ✅ |

**新增占位符两步**：

1. **`ResolveOne` 加 case**（[PlaceholderResolver.cs:94](Interaction/Dialogue/PlaceholderResolver.cs:94)）：在 `switch (key)` 中添加 `case "NEW_KEY": return ...;`。注意判断数据来源是否可能为 null（`evt?.` / `TargetName ?? ""`）。
2. **XML 模板用上**：在 `LWN_speech_*` 模板文本中写入 `{NEW_KEY}`，`Resolve` 自动替换。

**关键守卫**：`ResolveOne` 返回 `null` 时，正则替换**保留原样 `{KEY}`**（玩家会看到原始占位符 = bug）。新增占位符后务必在对应场景实测，确保不会走到 `default: return null`。


---

## AlertForceConversationAction — L3 路径 B 原子 Action

走到玩家面前后强制开启原版对话。**不再自己调 `InjectScript`**，只设 `_pendingTrigger = DialogueTrigger.Alert` + 调 `StartConversation`，由 `MissionConversationStartPatch.Prefix` 统一注入。

```csharp
// 用法（AgentBrain 内部）：
EnqueueAction(new AlertForceConversationAction());
// OnStart 中自动：查 brain.PrimaryAction → 确定 ConfrontationType + PlayerActionType
// → 设 _pendingTrigger/Confrontation/TriggerAction → StartConversation
// → Prefix 触发 TryInjectCrimeDialogue → BuildScript(trigger=Alert) → InjectScriptNoOpening
```

**文件位置**：`AI/Actions/AtomicAction.cs`（新增在文件末尾）


---

## CrimeDialogueBuilder.BuildAlertInterceptScript — L3 质问对话构建

与 `BuildAuthorityScript` / `BuildWitnessScript` 同属 `CrimeDialogueBuilder`。
台词通过 `NpcSpeechResolver.Resolve(..., narrativeFallback:)` 内部两阶段回落（XML `LWN_speech_*` → NarrativeResolver），调用方仅 `?? HardcodedAlertLine()` 兜底。

```csharp
var script = CrimeDialogueBuilder.BuildAlertInterceptScript(
    speaker, NpcInterceptIntent.Recover, PlayerActionType.Steal);
if (script != null) DialogueInjector.InjectScript(script, "AlertL3_NpcName");
```

**文件位置**：`Interaction/Dialogue/CrimeDialogueBuilder.cs`（新增约 200 行）

## 大义灭亲对话（2026-08-13 Phase F）— 嫌疑=玩家队伍随从的犯罪对话

`NeedsEarlyInjection` / `skipOpening` 在 `Active && (SuspectIsPlayer || IsCompanionSuspect(evt))` 时跳过原版开场（`IsCompanionSuspect` = SuspectHeroId 是玩家队伍成员）。`BuildCompanionCrimeNode` 权威 NPC 开场「你的随从 {NAME} 偷了我的东西！」→ 三出口（铁律 12 各有代价）：
- A 交出随从：`TakePrisonerAction.Apply(settlement.Party, companion)` + 事件 Resolved（⚠️ 不注册赎回菜单——交人=自愿放弃，村庄牢房无赎回路，town/castle 可走原版地牢救人）
- B 替随从赔钱：`NextNodeOnSuccess = "restitution_demand"` 复用 `BuildRestitutionSubtree`（铁律 10 不标价，NPC 开价）
- C 拒不认账：`ApplyDenyConsequence` = 权威对玩家好感 -10（关系惩罚）

触发入口 = 原版对话流注入管道（与既有犯罪对话同管道），对话目标 = `WorldEventStore.GetAuthorityNpc(evt)`。新 key `LWN_dialogue_companion_crime_*` 系列 EN/CN 双份。

---

## 🔴 对话入口禁止普适重定向对话对象到 party leader —— `ConversationEntryPatch.Prefix`

**坑（2026-08-28 实机）**：`ConversationEntryPatch.Prefix` 曾有「party 有 LeaderHero 就把对话对象换成队长」的重定向逻辑（原意图：队伍杂兵 → 换成队长谈；可能为复仇队场景设想）。但普适放在对话入口危害面极大：

- **主队成员（家族成员/随从）也在 Party 里**，主队 leader = `Hero.MainHero` = 玩家本人 → 对话对象被换成**玩家自己**
- vanilla `conversation_unmet_lord_main_party_on_condition`（反编译确认：`OneToOneConversationHero.PartyBelongedTo == MainParty && !HasMet`）被自洽满足 → 走 `lord_meet_in_main_party_player_response` 会话流 → 该 token 只有一句玩家选项「有你作为我们一员真好」→ NPC（=你自己）回「我效忠于您。」→ **死循环，无退出选项**

**已核实的事实链（反编译 + 日志）**：
- vanilla 自己的调用方**从不传非队长成员**——商队走 `ConversationHelper.GetConversationCharacterPartyLeader(party)`（无 LeaderHero 时自动选最高兵阶模板 NPC，模板 NPC 对话完全合法；强盗直接传 `BanditLeader`），所以 vanilla 不需要 hero 也不需重定向
- 真正传入「队伍里某成员」的只有**主队成员对话**一种情况（家族/随从）

**裁定（用户 2026-08-28）**：整段注释禁用，保留代码供恢复。若复仇队等特殊场景需要「杂兵→队长」重定向，放**该场景自身调用链**做特判，不进普适入口。恢复前先重读本条目。

**文件位置**：`Interaction/Dialogue/ConversationEntryPatch.cs`（Prefix，两段：`partnerChar` 重定向 + `q` 重建分支，均已注释）

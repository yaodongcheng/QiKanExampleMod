# 对话图结构设计

> 日期：2026-07-12
> 状态：设计阶段

## 一、问题诊断

当前 `DialogueInjector` 的对话图模型存在五个结构性问题：

### 1.1 Transition 是否有检定分支 — 隐式推断

一个 `DialogueTransition` 是否有成败双线，由三个分散的条件隐式决定：
- `Action` 以 `INTENT:` 开头
- 对应 `IntentBase.Goal != null`（机制层判断）
- `NpcResponseOnSuccess` / `NpcResponseOnFail` 是否非空（表现层判断）

写对话的人不看 Intent 源码，根本不知道这个 Transition 会不会分叉。

### 1.2 NextNode 语义过载

同一个字段在不同上下文意思不同：
- 无检定 Transition：`NextNode` = 唯一去向
- 有检定 Transition：`NextNode` = 成功去向，失败走 `NextNodeOnFail`（不设则 fallback）

名字没说清楚它是"成功去向"还是"默认去向"。

### 1.3 NpcResponse 与 DialogueNode.NpcLine 概念重叠

NPC 在玩家选择后的回应，有两种写法：
- 写在 `Transition.NpcResponse`（内联在边里）
- 写在目标 `Node.NpcLine`（独立节点）

两者语义完全等价——都是"NPC 说一句话，然后展示玩家选项"。概念重叠导致：
- 读写代码时要追踪两个地方
- 检定成败的 NPC 回应有时内联（`NpcResponseOnSuccess/OnFail`），有时走 Node，没有一致规则
- 严重时成败两句完全不同的话却指向同一个 `NextNode`，叙事断裂

### 1.4 SpeakerIndex — 死字段

`DialogueNode.SpeakerIndex` 始终为 `0`（NPC 说话），所有调用方无条件设 `0`，从未被设为其他值。注入时传给 `AddDialogLineMultiAgent` 的 `speakerIndex` 参数可以直接硬编码 `0`。

### 1.5 ActionValue — 旧式 Action 的遗留参数

`DialogueTransition.ActionValue` 仅在 4 个旧式非 INTENT Action 中使用：

```csharp
case "INCREASE_RELATION":  // ActionValue != 0 ? ActionValue : 5
case "DECREASE_RELATION":  // ActionValue != 0 ? -ActionValue : -5
case "GIVE_GOLD":          // ActionValue > 0 ? ActionValue : 100
case "TAKE_GOLD":          // ActionValue > 0 ? ActionValue : 100
```

对 `INTENT:xxx`（绝大多数 Transition）完全忽略——参数传递走的是 `ActionParam` 字符串。这 4 个 case 是 INTENT 体系统一之前的遗留代码，应迁移到 INTENT 体系，然后删除 `ActionValue`。

此外 `InteractionController.cs` 中也有 `INCREASE_RELATION` / `DECREASE_RELATION` 的 `ActionDefinition`，但那是 NPC 交互行为系统的独立定义，不走 DialogueInjector，**不在本次改动范围内**。

---

## 二、新数据模型

### 2.1 核心原则

**Transition 只管路由，NPC 台词统一在 DialogueNode.NpcLine。**

```
Node = NPC 说一句话 + 玩家可选的动作集合
Transition = 玩家选了一个动作 → 执行 → 路由到下一个 Node（或关窗）
```

### 2.2 TransitionCheckType 枚举

```csharp
public enum TransitionCheckType
{
    /// <summary>无检定。Action 为 NONE 或 INTENT:xxx（Intent.Goal==null）。
    /// 路由走 NextNodeOnSuccess。</summary>
    None,

    /// <summary>单次技能检定。Intent.Goal != null。掷骰决定成败。
    /// 路由走 NextNodeOnSuccess / NextNodeOnFail。</summary>
    SkillCheck,
}
```

### 2.3 DialogueTransition（新）

```csharp
public class DialogueTransition
{
    /// <summary>玩家选项的显示文本。</summary>
    public string PlayerLine;

    /// <summary>此选项是否有技能检定分支。</summary>
    public TransitionCheckType CheckType = TransitionCheckType.None;

    /// <summary>动作标识。NONE / INTENT:xxx。</summary>
    public string Action = "NONE";

    /// <summary>字符串参数。INTENT:xxx 执行时注入 IntentContext.ActionParam。
    /// 对于系统 Intent（IncreaseRelation 等），承载数值的字符串表示（如 "5"、"100"）。</summary>
    public string ActionParam = null;

    /// <summary>成功（或无检定）后的目标 Node Id。"" 或 null = 关闭对话。</summary>
    public string NextNodeOnSuccess;

    /// <summary>检定失败后的目标 Node Id。仅 CheckType.SkillCheck 时有效。
    /// 不设则 fallback 到 NextNodeOnSuccess。</summary>
    public string NextNodeOnFail;

    /// <summary>[内部] 注入时分配的 afterPlayer token，用作检定结果回写 key。外部不需要设置。</summary>
    internal string ResultKey = null;

    // ── 已删除的字段 ──
    // public string NextNode;               → NextNodeOnSuccess
    // public string NpcResponse;            → 目标 Node.NpcLine
    // public string NpcResponseOnSuccess;   → NextNodeOnSuccess 目标 Node.NpcLine
    // public string NpcResponseOnFail;      → NextNodeOnFail 目标 Node.NpcLine
    // public Func<string> LazyNpcResponse;  → 目标 Node.LazyNpcLine
    // public int ActionValue;               → ActionParam 字符串（系统 Intent 自行 int.TryParse）
}
```

### 2.4 DialogueNode（新）

```csharp
public class DialogueNode
{
    /// <summary>唯一标识。其他 node 的 transition 通过 NextNodeOnSuccess/OnFail 引用此 ID。</summary>
    public string Id = "start";

    /// <summary>NPC 的台词。所有 NPC 说话的唯一入口。</summary>
    public string NpcLine;

    /// <summary>延迟求值：引擎展示此行前才调 delegate 拿最新文本。
    /// 设置后覆盖 NpcLine。</summary>
    [JsonIgnore]
    public Func<string> LazyNpcLine;

    /// <summary>玩家可选的回应。空列表 [] = terminal（NPC 说完直接关窗）。
    /// null = 未初始化（非法）。</summary>
    public List<DialogueTransition> Transitions;

    // ── 已删除的字段 ──
    // public int SpeakerIndex = 0;   → 注入时硬编码 0（NPC 始终是说话者）
}
```

### 2.5 图结构示意

```
Node "confess" {
    NpcLine: "有什么要说的？"
    Transitions: [
        { PlayerLine: "我愿意赔",         CheckType: None,       NextNodeOnSuccess: "restitution_detail" },
        { PlayerLine: "开玩笑",           CheckType: SkillCheck, NextNodeOnSuccess: "charm_ok",
                                           Action: "INTENT:CharmDefense", NextNodeOnFail: "charm_fail" },
        { PlayerLine: "（转身就走）",      CheckType: None,       Action: "INTENT:WalkAway", NextNodeOnSuccess: "" },
    ]
}

Node "charm_ok"   { NpcLine: "说清楚？我倒要听听",     Transitions: [{ PlayerLine: "…", NextNodeOnSuccess: "continue_chat" }] }
Node "charm_fail" { NpcLine: "证据确凿，没什么好说的", Transitions: [] }   // ← terminal
```

### 2.6 为什么"拆成更多 Node"不是倒退

直观反应：旧模型里 `NpcResponse = "拜托了！必有重谢。"` 只是一行字符串，新模型要变成一整块 Node——这不更啰嗦了吗？

**不是。旧模型的简洁是假的。** 对比同一个对话片段：

**旧模型**（NPC 台词藏在 Transition 里）：
```csharp
// Node "start"
new DialogueNode {
    Id = "start", NpcLine = "能帮忙查查吗？",
    Transitions = {
        { PlayerLine = "我接", NpcResponse = "拜托了！必有重谢。", NextNode = "continue_chat" },
        { PlayerLine = "没空", NpcResponse = "那你忙吧……", NextNode = "continue_chat" },
        { PlayerLine = "是我干的", NpcResponse = "你？！……商量。", NextNode = "confess" },
    }
}
// 问题：3 句不同的 NPC 回应藏在一个对象的 3 个内联字段里。
//       它们各自导向不同（或相同）的下一站，因为藏在 Transition 里，
//       读代码时必须脑补"NPC 说了这句之后 → 跳到那里"。
```

**新模型**（NPC 台词统一在 Node）：
```csharp
// Node "start"
new DialogueNode {
    Id = "start", NpcLine = "能帮忙查查吗？",
    Transitions = {
        { PlayerLine = "我接",   NextNodeOnSuccess = "accept_ack" },
        { PlayerLine = "没空",   NextNodeOnSuccess = "decline_ack" },
        { PlayerLine = "是我干的", NextNodeOnSuccess = "confess" },
    }
}
new DialogueNode { Id = "accept_ack",  NpcLine = "拜托了！必有重谢。", Transitions = ContinueOptions() },
new DialogueNode { Id = "decline_ack", NpcLine = "那你忙吧……",       Transitions = ContinueOptions() },
// "confess" 已有独立定义
```

**对比**：

| 维度 | 旧模型 | 新模型 |
|------|--------|--------|
| NPC 台词查找 | 两个地方（Node.NpcLine + Transition.NpcResponse*） | 一个地方（Node.NpcLine） |
| 检定分支可读性 | 隐式推断（有没有 NpcResponseOnFail? Intent.Goal 是不是 null?） | 显式字段（CheckType.SkillCheck + NextNodeOnFail） |
| 图结构可视化 | Transition 夹带私货，图不是纯的"节点→边→节点" | 纯二分图，每个 Node 是完整对话帧 |
| C# 构建代码行数 | 短（内联字符串） | 稍长（每个响应一个 Node） |
| 运行时行为 | NPC 回应后立即展示目标 Node 的选项（无缝衔接） | NPC 回应作为独立 Node 展示，需多一次点击"继续" |

**唯一真正的 tradeoff**：新模型在对话节奏上会多一帧。旧模型里 `NpcResponse` 是一句"过场台词"——NPC 说完立即展示 `continue_chat` 的选项。新模型里这句台词变成了一个完整 Node，玩家需要点一下才能继续。

**缓解方案**：
1. **共享 Transitions 列表**：C# 构建代码中提取 `ContinueOptions()` / `CloseOptions()` 等 helper，多个 ack node 复用同一套选项，不增加维护负担。
2. **对于纯路由的 ack Node**（如"好，钱留下"→ continue），用 helper 一行搞定：
   ```csharp
   DialogueNode Ack(string id, string npcLine, string next = "continue_chat")
       => new() { Id = id, NpcLine = npcLine, Transitions = SingleContinue(next) };
   ```
3. **JSON 手写场景**确实会变长——但 JSON 作者（人或 LLM）获得的是**完全显式的图结构**，每个状态自包含，不再需要去 Transition 里翻 NPC 台词。

**结论**：新模型不增加概念总量（NPC 台词本来就存在），只把它们从"藏在 Transition 里"变成"摆在 Node 上"。代价是对话多一帧点击，收益是图中的每个状态都自描述。

---

## 三、旧式 Action 迁移到 INTENT

### 3.1 迁移映射

| 旧 Action | 新 INTENT | ActionParam 语义 | 默认值 |
|-----------|-----------|-----------------|--------|
| `INCREASE_RELATION` | `INTENT:IncreaseRelation` | 增加的数值（字符串） | 5 |
| `DECREASE_RELATION` | `INTENT:DecreaseRelation` | 减少的数值（字符串） | 5 |
| `GIVE_GOLD` | `INTENT:GiveGold` | 给予玩家的金额（字符串） | 100 |
| `TAKE_GOLD` | `INTENT:TakeGold` | 从玩家收取的金额（字符串） | 100 |

### 3.2 新增 Intent 类（`SystemIntents.cs`）

四个 Intent 共同特征：
- `Type = InteractionOptionType.System` — 需要新增此枚举值
- `Category = InteractionCategory.System` — 需要新增此枚举值
- `Goal = null` — 即时类，不检定
- `Evaluate`：检查 `ctx.ActionParam != null`（以此区分对话注入 vs 交互菜单，确保不出现在菜单中）
- `OnInstant`：`int.TryParse(ctx.ActionParam, out var val)` 取数值，用默认值兜底

```csharp
// IncreaseRelationIntent
public override Eligibility Evaluate(IntentContext ctx)
{
    if (string.IsNullOrEmpty(ctx.ActionParam)) return Eligibility.Hide();
    if (ctx.Speaker == null) return Eligibility.Hide();
    return Eligibility.Show();
}
public override void OnInstant(IntentContext ctx)
{
    int amount = int.TryParse(ctx.ActionParam, out var a) ? a : 5;
    ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, amount, false, true);
}

// DecreaseRelationIntent — 同上，取负值
// GiveGoldIntent — 不需要 ctx.Speaker，GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, amount)
// TakeGoldIntent — 不需要 ctx.Speaker，GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount)
```

### 3.3 新增枚举值

```csharp
// InteractionOptionType 新增
System,  // 系统级操作（仅对话 Transition 触发，不出现在交互菜单）

// InteractionCategory 新增
System,  // 系统级操作
```

`InteractionOptionCategoryMap` 的 switch 中 `System` 走 `default → General`，无需额外 case。`BuildOptionVMs` 不按 Category 过滤——系统 Intent 通过 `Evaluate` 检查 `ActionParam != null` 自动对菜单隐藏（菜单构造 `IntentContext` 时不传 `actionParam`）。

### 3.4 ExecuteAction 简化

删除 4 个旧 case 后，`ExecuteAction` 的 switch 只剩：

```csharp
switch (transition.Action.ToUpperInvariant())
{
    case "CLOSE_DIALOG":  // no-op marker
        break;
    default:
        if (transition.Action.StartsWith("INTENT:", ...))
            ExecuteIntentAction(...);
        else if (transition.Action != "NONE")
            // unknown action warning
        break;
}
```

`CLOSE_DIALOG` 保留——它是个合法的 no-op 标记（Transition 仅用于关窗，无副作用）。

---

## 四、注入逻辑简化

### 4.1 核心变化

删除 `NpcResponse*` 后，PlayerLine 的 `outputToken` 不再需要经过"NPC 回应行"中转。路由逻辑按 `CheckType` 分为两条路径：

### 4.2 CheckType.None（无检定）→ 直连

```
PlayerLine outputToken = targetNode 的 entry token（或 "close_window"）
```

无需任何桥接。ConversationManager 到达 targetNode 的 entry token 时，自然触发该 Node 的 `AddDialogLineMultiAgent` → NPC 说话 → 展示选项。

```csharp
// 伪代码
string afterPlayer;
if (!string.IsNullOrEmpty(transition.NextNodeOnSuccess))
{
    var targetNode = FindNode(transition.NextNodeOnSuccess);
    if (targetNode.Transitions is [_, ..])  // 有选项 → 直连
        afterPlayer = NodeToken(fileTag, transition.NextNodeOnSuccess);
    else  // terminal → 直关
        afterPlayer = "close_window";
}
else
{
    afterPlayer = "close_window";
}

pdf.AddPlayerLine(id, afterNpcLine, afterPlayer, text, condition, onConsequence, owner, 125);
```

### 4.3 CheckType.SkillCheck（有检定）→ 条件路由桥接

成败走不同 Node，`outputToken` 无法静态确定 → **仍需要一个桥接 token + 条件路由**，但桥接线**不夹带 NPC 台词**（台词已移到目标 Node），纯粹做路由：

```
PlayerLine outputToken = afterPlayer（桥接 token）

桥接 token 上注册 3 条 silent DialogLine:
  ┌─ if 检定通过   → NodeToken(NextNodeOnSuccess) 或 close_window
  ├─ if 检定失败   → NodeToken(NextNodeOnFail) 或 close_window
  └─ if 结果缺失   → close_window（安全网：Intent 被 Disabled 未执行）
```

```csharp
// 伪代码
string afterPlayer = NextToken(fileTag);
string capturedKey = afterPlayer;
transition.ResultKey = capturedKey;

pdf.AddPlayerLine(id, afterNpcLine, afterPlayer, text, condition, onConsequence, owner, 125);

// 成功路由
string successDest = !string.IsNullOrEmpty(transition.NextNodeOnSuccess)
    ? NodeToken(fileTag, transition.NextNodeOnSuccess) : "close_window";
cm.AddDialogLineMultiAgent(
    $"inj_route_succ_{Guid.NewGuid():N}", afterPlayer, successDest,
    new TextObject(""),  // 空文本 — 不展示，只路由
    () => _intentResults.TryGetValue(capturedKey, out var r) && r,
    null, 0, -1, 125);

// 失败路由
string failDest = !string.IsNullOrEmpty(transition.NextNodeOnFail)
    ? NodeToken(fileTag, transition.NextNodeOnFail)
    : (!string.IsNullOrEmpty(transition.NextNodeOnSuccess)
        ? NodeToken(fileTag, transition.NextNodeOnSuccess) : "close_window");
cm.AddDialogLineMultiAgent(
    $"inj_route_fail_{Guid.NewGuid():N}", afterPlayer, failDest,
    new TextObject(""),
    () => _intentResults.TryGetValue(capturedKey, out var r) && !r,
    null, 0, -1, 125);

// 安全网：Intent 被 Disabled → _intentResults 无 key → 防死锁
cm.AddDialogLineMultiAgent(
    $"inj_safety_{Guid.NewGuid():N}", afterPlayer, "close_window",
    new TextObject(""),
    () => !_intentResults.ContainsKey(capturedKey),
    null, 0, -1, 125);
```

> **注意**：桥接线用 `TextObject("")` 而非 `TextObject("…")`。引擎在 `AddDialogLineMultiAgent` 的 condition 全部为 true 的多条线中选第一条，空文本不产生可见输出——玩家看到的是"点了选项 → 目标 Node 的 NPC 说话"。`"…"` 会导致画面短暂闪现省略号。

### 4.4 LazyNpcLine 的惰性求值

`LazyNpcResponse` 从 Transition 移到 `DialogueNode.LazyNpcLine`。注入时若 Node 的 `LazyNpcLine != null`，`AddDialogLineMultiAgent` 使用一个 TextObject，其 condition 委托在引擎展示前调 `LazyNpcLine()` 更新 Value + 反射清缓存：

```csharp
var textObj = new TextObject("…");
cm.AddDialogLineMultiAgent(
    id, nodeEntryToken, afterNpcLine, textObj,
    () => {
        textObj.Value = node.LazyNpcLine();
        // 清缓存确保 GetCachedTokens() 重新解析
        var tokensField = typeof(TextObject).GetField("cachedTokens", ...);
        var langField = typeof(TextObject).GetField("cachedTextLanguageId", ...);
        tokensField?.SetValue(textObj, null);
        langField?.SetValue(textObj, -1);
        return true;
    },
    null, 0, -1, 125);
```

### 4.5 旧方法清理

`RegisterNpcResponseLines` 方法整体删除。其职责分散到：
- **路由**：§4.3 的 3 条 silent DialogLine（仅 SkillCheck 需要）
- **NPC 台词**：目标 Node.NpcLine（所有情况统一）
- **惰性求值**：§4.4 的 Node 注入逻辑

### 4.6 新旧对比

```
旧：afterPlayer → [RegisterNpcResponseLines: 最多4条线，夹带 NPC 台词 + 条件路由]
新：afterPlayer → [3条 silent 路由（仅 SkillCheck）] → targetNode.NpcLine 展示

旧：玩家点选项 → NPC 说 Transition 里藏着的台词 → 下一个 Node 的选项
新：玩家点选项 → 下一个 Node 说台词 → 下一个 Node 的选项
```

新模型的对话节奏多一拍（目标 Node 的 NPC 台词 + 选项是独立一帧），但换来的是**图中每个 Node 自己说了算，不用去 Transition 里翻**。

---

## 五、校验规则

以下规则由 skill `dialogue-graph-validation` 执行：

### 5.1 CheckType 与 Intent 对齐
- `CheckType.SkillCheck` → Intent 必须存在且 `Goal != null`
- `CheckType.None` + `INTENT:xxx` → Intent 的 `Goal` 应为 `null`（否则应声明 `SkillCheck`）

### 5.2 路由完整性
- `CheckType.SkillCheck` → `NextNodeOnFail` 应显式设置（不设则 fallback，可能非作者本意）
- 所有 `NextNodeOnSuccess` / `NextNodeOnFail` 引用的 Node.Id 必须存在

### 5.3 图拓扑
- 所有 Node（除 EntryNode）必须被至少一个 Transition 引用（可达性）
- 禁止单 Node 内死循环（`NextNodeOnSuccess` 指向自身）

### 5.4 NPC 台词完整性
- 每个 Node 的 `NpcLine`（或 `LazyNpcLine`）必须非空（terminal 也不例外）
- 不允许 `NpcLine == null && LazyNpcLine == null`

### 5.5 Node 终结点
- `Transitions` 为空数组 `[]` = terminal，合法
- `Transitions` 为 `null` = 未初始化，非法

### 5.6 系统 Intent 参数完整性
- `INTENT:IncreaseRelation` / `INTENT:DecreaseRelation` / `INTENT:GiveGold` / `INTENT:TakeGold` → `ActionParam` 应为可解析的正整数字符串（否则 warning：使用默认值）

---

## 六、迁移路径

### 6.1 枚举扩展
- `InteractionOptionType` 加 `System`
- `InteractionCategory` 加 `System`
- `InteractionOptionCategoryMap` 不需要改动（`System` 走 `default → General`）

### 6.2 新增文件
- `SystemIntents.cs`：4 个系统 Intent 类
- `IntentRegistry.cs`：注册 4 个新 Intent

### 6.3 DialogueInjector 改动
- `DialogueTransition`：加 `CheckType`；`NextNode`→`NextNodeOnSuccess`；删 `NpcResponse` / `NpcResponseOnSuccess` / `NpcResponseOnFail` / `LazyNpcResponse` / `ActionValue`
- `DialogueNode`：删 `SpeakerIndex`；加 `LazyNpcLine`
- `ExecuteAction`：删 4 个旧 case（INCREASE_RELATION / DECREASE_RELATION / GIVE_GOLD / TAKE_GOLD）
- `RegisterNpcResponseLines`：删除。拆分为：
  - **SkillCheck 路由**：3 条 silent `AddDialogLineMultiAgent`（inline 在 `RegisterNodeTransitions` 中，见 §4.3）
  - **LazyNpcLine**：Node 注入时处理（见 §4.4）
- `RegisterNodeTransitions`：按 `CheckType` 走直连（None）或条件桥接（SkillCheck）
- `InjectScriptInternal` / `RegisterNodeTransitions`：`speakerIndex` 硬编码 `0`
- `LogScript`：适配新字段

### 6.4 CrimeDialogueBuilder 改动

`CrimeDialogueBuilder` 是 DialogueInjector 的最大调用方，包含两条独立的构建路径：

| 路径 | 入口方法 | 场景 | 注入方式 |
|------|----------|------|----------|
| 大地图（Issue-Quest） | `BuildScript(Hero, Hero)` → 按身份+Stage 分派 | 玩家主动找 NPC 对话 | `InjectScript`（gateway 挂 hero_main_options） |
| Mission（L3 警戒） | `BuildAlertInterceptScript(Hero, ConfrontationType, PlayerActionType, …)` | NPC 主动拦截玩家 | `InjectScriptAsOpening`（NPC 台词碾压 start token） |

另有 `BuildPlayerSurrenderScript()` ——战斗认输对话，结构简单，同样的改法。

两条路径底层用相同的数据类型和旧模型字段（NpcResponse、SpeakerIndex 等），重构分三层推进：

#### 6.4.1 P0：字段适配（机械改动，必须做）

所有 builder 方法无差别适用：

- 删所有 `SpeakerIndex = 0`
- `NextNode` → `NextNodeOnSuccess`；`""` / `null` 语义不变（close_window）
- `NextNodeOnFail` → 原来 `NextNodeOnFail` 已有少数使用（如 `player_lose_counteroffer`），保持不变
- `NpcResponse = "…"` → 提取为独立 ack Node
- `NpcResponseOnSuccess / OnFail` + 对应 `NextNode` → 拆为两个独立 Node（成功/失败各一），Transition 加 `CheckType = SkillCheck`，分别设 `NextNodeOnSuccess` / `NextNodeOnFail`
- `LazyNpcResponse` → 对应 Node 设 `LazyNpcLine`
- `Action` 有 `INCREASE_RELATION` / `DECREASE_RELATION` / `GIVE_GOLD` / `TAKE_GOLD` → 改为 `INTENT:IncreaseRelation` 等，`ActionValue` → `ActionParam`

#### 6.4.2 P1：基础设施统一（低代价，顺手做）

两条路径散落着相同模式的重复代码。新模型下统一抽取：

```csharp
// ── Node 工厂 ──

/// <summary>纯 ack Node：NPC 说一句话 → 玩家点"继续" → 跳到 next</summary>
static DialogueNode AckNode(string id, string npcLine, string next = "continue_chat")
    => new() { Id = id, NpcLine = npcLine, Transitions = SingleContinue(next) };

/// <summary>terminal Node：NPC 说一句话 → 关窗</summary>
static DialogueNode TerminalNode(string id, string npcLine)
    => new() { Id = id, NpcLine = npcLine, Transitions = CloseOptions() };

// ── Transitions 工厂 ──

/// <summary>共享的"继续/离开"选项列表。替代原来的 BuildContinueChatNode。</summary>
static List<DialogueTransition> ContinueOptions(string walkAwayLine = "我得走了。")
    => new() { new() { PlayerLine = walkAwayLine, Action = "INTENT:WalkAway", NextNodeOnSuccess = "" } };

/// <summary>单选项"…（继续）"→ next。用于 ack Node。</summary>
static List<DialogueTransition> SingleContinue(string next)
    => new() { new() { PlayerLine = "…", NextNodeOnSuccess = next } };

/// <summary>terminal 选项（关窗）。</summary>
static List<DialogueTransition> CloseOptions(string line = "…")
    => new() { new() { PlayerLine = line, NextNodeOnSuccess = "" } };

// ── 原子 Transition 工厂（常用模式）──

static DialogueTransition WalkAway(string playerLine = "（转身就走）")
    => new() { PlayerLine = playerLine, Action = "INTENT:WalkAway", NextNodeOnSuccess = "" };

static DialogueTransition PayRestitution(string playerLine, string actionParam = null)
    => new() { PlayerLine = playerLine, Action = "INTENT:PayRestitution",
               ActionParam = actionParam, NextNodeOnSuccess = "" };
```

**统一 continue_chat**：

当前 `BuildScript` 用 `BuildContinueChatNode(r)` 返回带 `LazyNpcResponse` 的 Node（根据 evt 状态动态选 NPC 台词），`BuildAlertInterceptScript` 却自己 inline 写两套（escalated / 非 escalated）。统一为：

```csharp
static DialogueNode ContinueChatNode(string npcLine, List<DialogueTransition> extraOptions = null)
{
    var transitions = ContinueOptions();
    if (extraOptions != null) transitions.InsertRange(0, extraOptions);
    return new() { Id = "continue_chat", NpcLine = npcLine, Transitions = transitions };
}
```

`BuildContinueChatNode(r)` 删除。调用方自己决定 `npcLine`（静态字符串或 `LazyNpcLine`）。

#### 6.4.3 P2：跨路径子图复用（可选，不阻塞重构）

两条路径有语义重叠的子图，可以在新模型下拉通：

| 子图 | BuildScript 已有 | BuildAlertInterceptScript 现状 | 复用方案 |
|------|-----------------|-------------------------------|----------|
| 认栽→赔偿协商 | `BuildConfessNode` + `BuildRestitutionDetailNode` | Alert 的 Stop/Recover 场景扁平写在一层 Transitions 里 | Alert 的 Stop/Recover 也走 Confess→RestitutionDetail 子图 |
| 搜查→结果分支 | 无（Campaign 层不搜身） | `BuildSearchResultNode` — 仅 Alert 层 | 保持独立 |
| 检定成败叙事 | `CharmDefense` 成败走同一个 `NextNode`（叙事断裂） | `Threat` 成败都走 `NextNode=""`（关窗），叙事合理 | P0 已通过 NextNodeOnFail 修复 Campaign 层断裂 |

**P2 不是本次重构的阻塞项**——Alert 的扁平写法功能正确，只是不如 Campaign 线完整。当前优先做 P0+P1，P2 后续迭代。

### 6.5 JSON / LLM 模板
- `test_talk.json`：`"Action": "GIVE_GOLD"` → `"Action": "INTENT:GiveGold"`，`"ActionValue"` → `"ActionParam"`；`"nextNode"` → `"nextNodeOnSuccess"`
- LLM 生成 JSON 的 prompt 模板同步更新

### 6.6 向后兼容

**不做向后兼容。** JSON 文件、LLM 输出、所有 C# 调用方同步更新。

---

## 七、影响范围总览

### 7.1 文件改动清单

| 文件 | 改动 | 分层 |
|------|------|------|
| `InteractionOptionManager.cs` | `InteractionOptionType.System` + `InteractionCategory.System` | P0 |
| `Intents/SystemIntents.cs` | **新增**：4 个系统 Intent 类 | P0 |
| `Intents/IntentRegistry.cs` | 注册 4 个新 Intent | P0 |
| `DialogueInjector.cs` — `DialogueTransition` | 加 `CheckType`；`NextNode`→`NextNodeOnSuccess`；删 5 个字段 | P0 |
| `DialogueInjector.cs` — `DialogueNode` | 删 `SpeakerIndex`；加 `LazyNpcLine` | P0 |
| `DialogueInjector.cs` — `ExecuteAction` | 删 4 个旧 case | P0 |
| `DialogueInjector.cs` — `RegisterNpcResponseLines` | 删除；SkillCheck 条件路由 inline 到 `RegisterNodeTransitions` | P0 |
| `DialogueInjector.cs` — `RegisterNodeTransitions` | 按 `CheckType` 分支：None→直连 / SkillCheck→条件桥接 | P0 |
| `DialogueInjector.cs` — 注入方法 | 适配新字段 + speakerIndex 硬编码 0 + LazyNpcLine 惰性求值 | P0 |
| `DialogueInjector.cs` — `LogScript` | 适配新字段：`NextNode`→`NextNodeOnSuccess`，删 `SpeakerIndex`/`NpcResponse` 引用 | P0 |
| `CrimeDialogueBuilder.cs` — 字段适配 | 全部 builder 方法：删 `SpeakerIndex`、`NextNode`→`NextNodeOnSuccess`、`NpcResponse*`→独立 Node、`LazyNpcResponse`→`LazyNpcLine`、加 `CheckType` | P0 |
| `CrimeDialogueBuilder.cs` — 基础设施 | 新增 `AckNode()` / `TerminalNode()` / `ContinueOptions()` / `SingleContinue()` / `CloseOptions()` / 原子 Transition helper；删除 `BuildContinueChatNode()` | P1 |
| `CrimeDialogueBuilder.cs` — 子图复用 | Alert 的 Stop/Recover 走 Confess→RestitutionDetail 子图（可选，不阻塞） | P2 |
| `ModuleData/DesignData/Dialogues/test_talk.json` | 旧 Action + ActionValue → INTENT + ActionParam；`nextNode`→`nextNodeOnSuccess` | P0 |
| `plans/rules/wheels.md` | 更新 DialogueInjector 相关示例 | P1 |
| `.claude/skills/dialogue-graph-validation.md` | 校验 skill（已创建） | P0 |

### 7.2 分层执行顺序

```
P0（本次必须完成）
  ├─ 枚举扩展 (InteractionOptionType.System, InteractionCategory.System)
  ├─ SystemIntents.cs + IntentRegistry 注册
  ├─ DialogueTransition / DialogueNode 字段改动
  ├─ ExecuteAction switch 简化
  ├─ RegisterNpcResponseLines → inline SkillCheck 路由
  ├─ RegisterNodeTransitions 双路径
  ├─ LogScript 适配
  ├─ CrimeDialogueBuilder 全部 builder 方法字段适配
  └─ test_talk.json 更新

P1（顺手做，代价很低）
  ├─ CrimeDialogueBuilder 共享 Node/Transition helper
  ├─ 统一 continue_chat（删除 BuildContinueChatNode）
  └─ wheels.md 更新

P2（后续迭代，不阻塞）
  └─ Alert 线走 Confess→RestitutionDetail 子图
```

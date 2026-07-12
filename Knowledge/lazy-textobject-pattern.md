---
name: lazy-textobject-pattern
description: 对话文本延迟求值模式 — 注入时存 delegate，引擎展示前才读最新游戏状态
metadata:
  type: reference
---

# 对话文本延迟求值（LazyTextObject 模式）

## 问题

`CrimeDialogueBuilder.BuildScript` 中 `r.Resolve(template)` 在**注入时**就把 `{placeholder}` 替换成了字符串。如果占位符依赖的游戏状态在对话中途变更（如 `PlayerTookInvestigationQuest` 从 false 变 true），已注入的文本不会更新——同一段对话内 NPC 说的话是旧的。

## 解决方案

利用 `ConversationManager` 的条件回调（`Func<bool>`）在引擎展示 NPC 台词**之前**被调用的特性，在条件回调中更新 `TextObject.Value`，再用反射清除内部缓存（`cachedTokens`、`cachedTextLanguageId` 是 internal 字段），引擎渲染时 `GetCachedTokens()` 就用新值重新 tokenize。

## 已实现的机制：`LazyNpcResponse`

### 模型层

[`DialogueInjector.cs` — `DialogueTransition`](ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/DialogueInjector.cs)：

```csharp
[Newtonsoft.Json.JsonIgnore]
public Func<string> LazyNpcResponse = null;  // 设置后覆盖 NpcResponse
```

### 注入层（`RegisterNpcResponseLines`）

```csharp
// 延迟求值：condition 回调在引擎展示 NPC 行前触发 → 更新 Value → GetCachedTokens() 拿到最新文本
var textObj = new TextObject("…");
cm.AddDialogLineMultiAgent(
    $"inj_lazy_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
    textObj,
    () =>
    {
        textObj.Value = transition.LazyNpcResponse();
        // 清除内部缓存，确保 GetCachedTokens() 从新 Value 重新 tokenize
        typeof(TextObject).GetField("cachedTokens",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(textObj, null);
        typeof(TextObject).GetField("cachedTextLanguageId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(textObj, -1);
        return true;
    },
    null, node.SpeakerIndex, -1, 125);
```

### 使用方式（`CrimeDialogueBuilder`）

```csharp
new DialogueInjector.DialogueTransition
{
    PlayerLine = "我得走了。",
    LazyNpcResponse = () =>
    {
        if (evt.PlayerTookInvestigationQuest)
            return r.Resolve("快去查，有消息了来告诉{SpeakerSelfRef}。");
        // ...
    },
    Action = "INTENT:WalkAway",
    NextNode = ""
}
```

闭包捕获的 `evt` 是 `WorldEvent` **引用**，`PlayerTookInvestigationQuest` 在对话中途变化后 delegate 能读到最新值。`r`（`PlaceholderResolver`）同理，`r.Resolve()` 在 delegate 被调时才执行。

## 扩展：通用延迟文本

### 可行的字段

| 字段 | 注入方式 | 可延迟？ |
|------|----------|----------|
| `node.NpcLine` | `cm.AddDialogLineMultiAgent(..., new TextObject(...))` | ✅ 自己创建 TextObject |
| `transition.NpcResponse` | 同上 | ✅ 已实现 `LazyNpcResponse` |
| `transition.NpcResponseOnSuccess/Fail` | 同上 | ⚠️ 可与条件逻辑组合，但较复杂 |
| `transition.PlayerLine` | `pdf.AddPlayerLine(..., string)` | ❌ `AddPlayerLine` 只收 string |
| `script.EntryOption` | `gateDf.AddPlayerLine(..., string)` | ❌ 同上 |

### 扩展 `LazyNpcLine`

`DialogueNode` 加字段：

```csharp
[Newtonsoft.Json.JsonIgnore]
public Func<string> LazyNpcLine = null;
```

`InjectScriptInternal` 中 `AddDialogLineMultiAgent` 处检测该字段，用同样的反射 trick。

### `PlaceholderResolver` 加便捷工厂

```csharp
/// <summary>创建延迟求值委托：引擎展示前才调 Resolve，拿到最新游戏状态。</summary>
public Func<string> Lazy(string template) => () => Resolve(template);
```

### 使用方式

```csharp
// 静态 — 注入时求值
NpcLine = r.Resolve("（{SpeakerEmotion}地）..."),

// 动态 — 展示时才求值（状态可能已变）
LazyNpcLine = r.Lazy("{InvestigationProgressWord}。{SuspectDescription}。"),
```

## 限制

1. **PlayerLine / EntryOption** 走 `AddPlayerLine(string)`，无法延迟（API 只收 string）
2. **反射依赖**：`cachedTokens` 和 `cachedTextLanguageId` 是 `internal` 字段，游戏更新可能改名
3. **只在 `needsBridge=true` 路径生效**：无桥接路径（`afterPlayer = afterNpcResponse`）不创建 condition 回调

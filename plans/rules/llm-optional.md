# LLM 是可选功能

## 核心原则

玩家**不需要配置 LLM** 也能正常玩游戏。`Settings.Instance.IsLLMConfigured` 是所有 LLM 功能的总闸。

```csharp
// Settings.cs — 只有三个字段都非空才返回 true
public bool IsLLMConfigured => !string.IsNullOrWhiteSpace(LLMBaseUrl)
                       && !string.IsNullOrWhiteSpace(LLMApiKey)
                       && !string.IsNullOrWhiteSpace(LLMModel);
```

## 铁律

### 每条 LLM 代码路径必须在入口处检查 IsLLMConfigured

```csharp
if (!Settings.Instance.IsLLMConfigured)
{
    InformationManager.DisplayMessage(new InformationMessage("请先在 config.json 中配置 LLM。"));
    return; // 或走 vanilla 降级路径
}
```

### 已知需要守卫的入口点

> 🔴 **别按行号认入口点**（行号必然过期，旧版本这张表写的就是废弃行号）。自检一律跑：
> `grep -rn "LLMService.Instance" ExampleModVS --include=*.cs`
> 逐个确认：**要么本方法自己有 `IsLLMConfigured` 闸；要么调用链上游有，且本方法不在"后台自动触发"路径上**。

| 入口 | 位置（按方法名找） | 触发方式 |
|------|------|---------|
| G 键闲聊 | `InteractionMissionView` 对话入口 | 玩家按键 |
| NPC 主动对话 | `AtomicAction` | NPC 找玩家（偷窃被逮、事件触发等） |
| 记忆总结 | `SingNpcMemorySystem.MaintainMemoryAsync` / `MaintainNarrationAsync` / `CheckAndPromoteToPermanent` | **后台自动触发**（`AddHistory` → `_ = MaintainMemoryAsync()`） |
| 人设精炼 | `SingNpcMemorySystem.EnsureProfileSummary` | 首次有素材后懒触发 |
| 剧情生成 | `AIStoryGenerator.GenerateTaskAsync` | 事件驱动 |
| IM / 群聊 / 广播 / 计划 / 喊话润色 | `ImReplyService` / `ImEventBroadcaster` / `ImCommandFlow` / `AutonomyProposal` / `PlanReplan` / `ReactiveAgent` / `SpeechChannel` | 各自入口 |

### 🔴 已知坑：漏网入口不报错，只会被「没配 LLM」的玩家撞到（2026-10-24 玩家反馈实锤）

**为什么"没开 LLM"反而是触发条件**：没配 LLM = `IsLLMConfigured == false`，不等于"相关代码不跑"。
记忆系统平时是**纯数据功能**（感知把"主公在附近交战"写进 NPC 记忆），跟 LLM 无关，照常运行；
只有"历史攒太多要压缩"那一步才需要 LLM。**配了 LLM 的玩家早早走通、永远撞不到这条漏网分支**，
所以开发机（配了 LLM）自测是**假绿**。

**事故三环（缺一不成立）**：

| 环 | 事实 |
|---|---|
| ① | `LLMService.Instance` 的 getter **没 Key 就直接 `throw`**，不是返回 null（设计意图：逼调用方 try-catch） |
| ② | 记忆总结管道漏了 `IsLLMConfigured` 闸，直接去拿单例 |
| ③ | `AddHistory` 是 `_ = MaintainMemoryAsync()` **fire-and-forget** ⇒ 异常没人 await ⇒ 终结器线程 `UnobservedTaskException` ⇒ 被第三方 mod「崩溃跳过」抓到、弹红字 + 生成报告（**游戏其实没崩**） |

**附带损害**：`_isSummarizing` 抛点之前已置 true、旧实现只在最后一段 `try/finally` 复位 ⇒
抛一次就**永久卡 true**，该 NPC 此后再不总结记忆、历史只增不减（JSON 解析失败那条提前 `return` 同病）。

**两条通用纪律**：

1. **凡是 `_ = XxxAsync()` 的 fire-and-forget，方法内部必须自带 `try/catch`** —— 抛出就等于在别人眼里"崩溃"。
2. **状态标记（正在跑 / 已处理 / 冷却中）必须走 `try/finally` 复位**，不能靠"每条 return 前手动置回"。

**验收命令**（不配 LLM 也能跑）：`custom.memory status` / `custom.memory fill 100` / `custom.memory maintain`

### 何时检查、何时不检查

- **必须在入口检查**：任何可能首次触发 LLM 调用的用户操作或 NPC 行为
- **下游不需要重复检查**：如果调用链上游已检查过，被调用方可以假设 LLM 可用
- **try-catch 是兜底不是替代**：不能用 try-catch 代替 IsLLMConfigured 检查

## 验证标准

删除 `config.json`（或字段全空）→ 启动游戏 → 所有非 LLM 功能正常：
- F 键 vanilla 对话 ✓
- 偷窃/搜刮 ✓
- 挥刀触发战斗 ✓
- 气泡/巡逻 AI ✓
- 不弹 NullReferenceException ✓
- **进战场**（会触发感知写入 → 记忆管道）：日志不出现 `[FirstChance] LLMService not initialized`、崩溃跳过不弹红字 ✓
- **`custom.memory fill 100` → `custom.memory status`**：`IsSummarizing` 回到 `False`；
  日志出现 `记忆维护：LLM 未配置，不总结，直接淘汰最旧 N 条历史。` ✓

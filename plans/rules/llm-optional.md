# LLM 是可选功能

## 核心原则

玩家**不需要配置 LLM** 也能正常玩游戏。`Settings.Instance.IsLLMReady` 是所有 LLM 功能的总闸。

```csharp
// Settings.cs — 只有三个字段都非空才返回 true
public bool IsLLMReady => !string.IsNullOrWhiteSpace(LLMBaseUrl)
                       && !string.IsNullOrWhiteSpace(LLMApiKey)
                       && !string.IsNullOrWhiteSpace(LLMModel);
```

## 铁律

### 每条 LLM 代码路径必须在入口处检查 IsLLMReady

```csharp
if (!Settings.Instance.IsLLMReady)
{
    InformationManager.DisplayMessage(new InformationMessage("请先在 config.json 中配置 LLM。"));
    return; // 或走 vanilla 降级路径
}
```

### 已知需要守卫的入口点

| 入口 | 位置 | 触发方式 |
|------|------|---------|
| G 键闲聊 | `InteractionMissionView.cs:237` | 玩家按键 |
| NPC 主动对话 | `AtomicAction.cs:143` | NPC 找玩家（偷窃被逮、事件触发等） |
| 记忆总结 | `SingNpcMemorySystem.cs:324,435` | 后台自动触发 |
| 剧情生成 | `AIStoryGenerator.cs:107,111` | 事件驱动 |

### 何时检查、何时不检查

- **必须在入口检查**：任何可能首次触发 LLM 调用的用户操作或 NPC 行为
- **下游不需要重复检查**：如果调用链上游已检查过，被调用方可以假设 LLM 可用
- **try-catch 是兜底不是替代**：不能用 try-catch 代替 IsLLMReady 检查

## 验证标准

删除 `config.json`（或字段全空）→ 启动游戏 → 所有非 LLM 功能正常：
- F 键 vanilla 对话 ✓
- 偷窃/搜刮 ✓
- 挥刀触发战斗 ✓
- 气泡/巡逻 AI ✓
- 不弹 NullReferenceException ✓

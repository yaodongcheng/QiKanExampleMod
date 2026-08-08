# 编码风格与设计原则

> 本文档总结作者在 64 个 .cs 文件中一致遵循的约定。**AI 写新代码时必须照做**，让新增代码读起来和既有代码一致。

## 命名约定

| 元素 | 约定 | 例 |
|------|------|----|
| 类名 / 方法名（含私有方法） | PascalCase | `InteractionController`、`BuildOpeningPrompt()` |
| 私有字段 | `_camelCase`（**必须带 `_` 前缀**） | `_currentNpc`、`_isProcessing` |
| 公共属性 | PascalCase | `SpeakerName`、`IsVisible` |
| 枚举值 | PascalCase | `QuestType.HuntBandits` |

**反例，勿模仿**：`AI/Actions/AtomicAction.cs:29-31` 的 `self`/`memory`/`ContextDesc` 没带 `_` 前缀，是历史遗留，不要照抄。

## 单例模式

统一写法：

```csharp
public static XXX Instance { get; private set; }
```

现有单例（11 个）：`Settings`、`LLMService`、`InteractionController`、`InteractionMissionView`、`BubbleSayMissionView`、`AttackTriggerMissionLogic`、`StoryEngine`、`AgentAIController`、`NewsSpreadSystem`、`AIStoryGeneratorBehavior`、`StageDirector`。

**反模式（勿模仿）**：`Story/StageDirector.cs:219` 用裸字段 `public static StageDirector Instance;`（无 getter 保护，可被任意覆写）。新单例一律用 `{ get; private set; }`。

**使用前必须判空**：单例可能在某些 Mission 生命周期阶段为 null，调用前 `if (X.Instance != null)`。

## 静态 Helper vs 实例

- **无状态工具** → 全 `static class`：`AgentControlHelper`、`PromptBuilder`、`DebugLogger`、`CombatManager`、`CsvLoader`、`GameDatabase`。
- **有状态系统** → 单例实例。

加新工具方法时，先看能不能塞进已有的静态 Helper（尤其 NPC 操作进 `AgentControlHelper`、prompt 进 `PromptBuilder`），不要新建零散的工具类。

## ViewModel 数据绑定模板

所有 `ViewModel` 子类的可绑定属性走这个模板（范本：`Interaction/StoryDialogVM.cs`）：

```csharp
private bool _areOptionsVisible;

[DataSourceProperty]
public bool AreOptionsVisible
{
    get => _areOptionsVisible;
    set
    {
        if (value != _areOptionsVisible)
        {
            _areOptionsVisible = value;
            OnPropertyChangedWithValue(value, "AreOptionsVisible");
        }
    }
}
```

集合用 `MBBindingList<T>`。不引入第三方（如 PropertyChanged.Fody）——保持与现有代码一致。

## 异常处理

- **网络 / IO / LLM** → `try-catch` 后 `DebugLogger.Log(...)` + 返回可用的降级默认值，**不要让异常冒泡到引擎主循环**。
- **JSON 反序列化** → 单独 `try-catch`，catch 里置 `null` / 空对象，由上游的 null-guard 兜底（见 [defensive-coding.md](defensive-coding.md)）。
- 不要用裸 `catch {}` 吞掉异常而不记日志（除非确实是预期内的解析失败且上游有兜底）。

## async / LLM 与引擎主线程

- LLM 调用一律 `await LLMService.Instance.ChatAsync(...)`（返回 `Task<string>`）。
- **不阻塞引擎主线程**的后台触发用 fire-and-forget：`_ = Task.Run(() => SomethingAsync());`（范本 `AI/Actions/AtomicAction.cs`、`Story/AIStoryGenerator.cs:95`）。
- LLM 回来后若要改 UI / 动 Agent，由调用方负责切回 Gauntlet 主线程上下文，**不要在后台线程直接操作引擎对象**。
- 任何可能首次触发 LLM 的入口，先查 `Settings.Instance.IsLLMConfigured`（见 [llm-optional.md](llm-optional.md)）。

## 注释与日志

- **注释用中文**，标识符（类/方法/字段名）用英文。
- 玩家可见提示：`InformationManager.DisplayMessage(new InformationMessage("[LivingWorldNpcs] ..."))`。
- 调试落盘：统一 `DebugLogger.Log(...)`（写到 `Documents/Mount and Blade II Bannerlord/Configs/StoryEngine_RuntimeLog.txt`，首次清空后追加，线程安全）。
- 日志前缀只用 `[LivingWorldNpcs]`，禁止 `[Shokuho*]`（见 [architecture.md](architecture.md)）。

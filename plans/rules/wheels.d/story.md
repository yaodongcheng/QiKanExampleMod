# story — 轮子速查分卷（wheels.md 索引导航）
## Story 命令引擎 — `Story/`

JSON 脚本驱动的剧情演出引擎，**命令模式**。`CommandManager`（注册分发）+ `StoryEngine`（栈式执行）+ `VisualCommands`/`SystemCommands`/`LogicCommands`（指令实现）+ `StageDirector`（站位/出入场）。

**加一条新剧情指令的正确姿势**：

```csharp
public delegate bool CommandHandler(ScriptNode node, StoryEngine engine);

// 1. 写 handler（放进对应的 Visual/System/Logic Commands 类）
public static bool HandleMyCmd(ScriptNode node, StoryEngine engine) { /* ... */ return true; }

// 2. 在 CommandManager.RegisterAll() 里注册
Register("我的指令", VisualCommands.HandleMyCmd);
```

- 返回值约定：`true` = 阻塞、等玩家输入/动画；`false` = 立即执行下一行。
- **禁止**改 `CommandManager.Execute` 或写 if-else 指令链——只 `Register`。
- 已有指令：對話/自語/旁白/對話選擇、人物登场/退场/別、選擇、變量賦值/分歧/更新/代入、ＢＧＭ變更/ＳＥ開始/進入設施 等。

LLM 自动生成剧本走 `Story/AIStoryGenerator.cs`（`StartGeneration` → 后台 `GenerateTaskAsync` → `PromptBuilder.BuildDirectorPrompt`/`BuildShowPrompt` → `AIStoryAdapt` 转成 `ScriptNode[]` → `StoryEngine.StartEvent`）。

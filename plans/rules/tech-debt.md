# 架构待调整清单

> 回顾全库后发现的、值得后续修的问题。**本表只记录，不在记录它的这次任务里修。**
> 每条带「证据 → 风险 → 建议」。路径相对 `ExampleModVS/ExampleMod/ExampleMod/`。
> 优先级：🔴 违反铁律（应尽早修）｜🟡 质量/可维护性｜⚪ 知悉即可。

---

## 🔴 1. 世界观硬编码泄漏（违反铁律 #3）

代码里仍残留日本战国 flavor 字串，破坏 LivingWorldNpcs 的通用性。

**证据：**
- `Story/VisualCommands.cs:1038` — `return "妾身";`
- `LLM/PromptBuilder.cs:355 / 364 / 391` — 台词参考里硬写 "妾身"
- `LLM/PromptBuilder.cs:905` — `"...称为\"我妻子\"、\"我主公\"等"`
- `Interaction/InteractionOptionManager.cs:234` — `"主公，我们需要更多支援。"`
- `Quest/QuestManager.cs:291 / 387 / 396 / 415 / 466 / 621 / 684 / 693` — 大量 "主公"

**建议：**
- "妾身" → 引用**已存在**的 `Settings.Instance.FemaleSelfAddress`（默认空，TaikouContent 注入 "妾身"）。
- "主公" → 给 `Settings` **新增** `LordTerm` 字段（卡拉迪亚默认 "领主"/"大人"，TaikouContent 注入 "主公"），全部改为引用。
- 改完用 [worldview.md](worldview.md) 末尾的 grep 验证为空。

## 🔴 2. IsLLMReady 守卫缺失（违反铁律 #1）

部分 LLM 调用入口没有总闸检查，LLM 未配置时会走空调用 / 潜在崩溃。

**证据：**
- `Story/AIStoryGenerator.cs:107 / 111` — `GenerateTaskAsync` 内直接 `ChatAsync`，其入口 `StartGeneration`（:79）无 `IsLLMReady` 检查。
- `Memory/SingNpcMemorySystem.cs:325`（`SummarizeAsync`）/ `:436`（`MergeMemoryAsync`）— 记忆总结/合并触发点无入口守卫。

**建议：** 在 `StartGeneration` 和记忆总结/合并的触发入口处加 `if (!Settings.Instance.IsLLMReady) { /* 降级 return */ }`。参考已正确守卫的 `AI/Actions/AtomicAction.cs:49`、`Interaction/InteractionMissionView.cs:236`。

## 🟡 3. 单例反模式

**证据：** `Story/StageDirector.cs:219` — `public static StageDirector Instance;`（裸字段，无保护）。
**建议：** 改为 `public static StageDirector Instance { get; private set; }`，与其余 10 个单例统一。

## 🟡 4. 巨型文件（列为待重构项，不强制立即做）

> **红线：新功能不得继续往这些文件堆。** 新逻辑开新类/新文件。

| 文件 | 行数 | 拆分建议 |
|------|------|---------|
| `Negotiation/NegotiationSystem.cs` | 1758 | 拆出 SkillCheck、NegotiationTrait、筹码计算、谈判状态机 |
| `Interaction/InteractionController.cs` | 1540 | 提取 `SkillCheckHandler`、`NegotiationController`、`DialogueEventGenerator` |
| `Memory/NPCProfile.cs` | 1270 | 数据容器与 `GetXxxPrompt` 生成方法分离（可用 partial class） |
| `Debug/MyCommands.cs` | 1213 | 调试命令集，建议整体 `#if DEBUG` 隔离 |
| `LLM/PromptBuilder.cs` | 1190 | 按场景拆 partial class（开场/谈判/社交/总结…） |

## 🟡 5. 可抽取的重复模式

- **LLM 安全调用样板**：「`ChatAsync` → `CleanJson` → `try 反序列化` → catch 降级」在多处重复（`SingNpcMemorySystem`、`InteractionController`、`AtomicAction` 等）。建议抽 `LLMService.SafeCallAsync<T>(prompt, fallback)` 泛型方法统一。
- **ViewModel 绑定样板**：44 处 `if (value != _field){...OnPropertyChangedWithValue...}`。记录即可，**不引第三方库**（保持现状一致性）。

## 🟡 6. 死代码 / 调试残留

- 注释掉的代码：`AI/AgentBrain.cs:14`、`Debug/MyCustomUIVM.cs:61`（`LoadImageTest("H:\\taikou.png")`，注意还含硬编码路径+战国字样）、`AgentControlHelper` / `InteractionMissionView` 中注释掉的调用。
- `Debug/` 整目录是开发用，建议 `#if DEBUG` 包裹或发布前剔除。

## ⚪ 7. 命名遗留（暂不动，知悉即可）

目录仍是 `ExampleModVS/ExampleMod/ExampleMod/`，但 namespace 已全部是 `LivingWorldNpcs.*`，二者不一致。重命名目录/工程是高风险操作（牵动 .csproj、SubModule.xml、引用路径），**暂不动**，知悉即可。

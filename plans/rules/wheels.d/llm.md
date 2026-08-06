# llm — 轮子速查分卷（wheels.md 索引导航）
## LLM 调用 — `LLM/LLMService.cs`

单例，内置 3 次重试、HttpClient 复用。

```csharp
await LLMService.Instance.ChatAsync(systemPrompt, max_tokens = 150, needJson = true);  // 通用
await LLMService.Instance.SummarizeAsync(systemPrompt);    // 记忆总结（短）
await LLMService.Instance.MergeMemoryAsync(systemPrompt);  // 远期记忆合并
LLMService.CleanJson(raw);             // 静态，剥离 markdown ```json 包裹
```

调用前查 `IsLLMReady`；返回的 JSON 必须防御性处理（见 [defensive-coding.md](defensive-coding.md)）。


---

## Prompt 构建 — `LLM/PromptBuilder.cs`

按场景的静态 prompt 工厂。加新对话场景 = 在这里加一个 `BuildXxxPrompt` 静态方法，**不要在业务代码里拼 prompt 字串**。现有方法覆盖：开场冲突、技能检定结果、闲聊、谈判（核心）、社交事件分析、记忆长期化、对话总结、导演梗概、演出脚本生成。

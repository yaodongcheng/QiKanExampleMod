# NPC 实时对话 + 记忆整合 plan（BC-006 v2 后续）

> **状态**：🟡 待审批——基于本地 diff（未提交）+ 既有 `SingNpcMemorySystem` 整体考虑。
> **目标**：随从/目标 NPC 的实时对话（respond）上下文统一走既有三层记忆系统，不重复造轮子；台词态度与公式演算一致（已实现）。
> **关联**：[llm-goap-plan-execution.md](llm-goap-plan-execution.md)（计划系统设计）、[plan-badcases.md](plan-badcases.md) BC-006、`Memory/SingNpcMemorySystem.cs`（三层记忆）、`Memory/AllNpcMemoryManager.cs`（按 Agent 取记忆）。

---

## 一、本地 diff 现状（本次改动，未提交）

| 文件 | 改动 | 性质 |
|------|------|------|
| `Planner/ReactiveAgent.cs` | +221 行：`respond` 反应动作、`StartRespond`（实时 LLM 回应）、`TickAll`（主线程播放）、`BuildRespondPrompt`（上下文）、`DescribePersonality`、默认模板补 tavernkeeper/merchant/chief、演算意图（score→态度） | 新功能 |
| `LLM/LLMService.cs` | +51 行：`ChatOnceAsync`（单次请求、2s 超时 CTS、失败静默 null、无重试） | 新功能 |
| `Planner/InlineSteps.cs` | say_to 广播带随从台词（Args[1]）+ 计划摘要主题（Args[2]） | 小改 |
| `AI/AgentAIController.cs` | `OnMissionTick` 挂 `ReactiveAgent.TickAll` | 接线 |
| `Interaction/PlanCommandFlow.cs` | BuildGrammar reactions 说明加 respond | prompt |
| 本地化 ×4（EN/CN prompts/strings） | occupation 补 key、trait 描述、respond 模板台词、回应 prompt 骨架（含态度段） | 本地化 |
| `Scripts/test_llm_plan.py` | REACTIVE_ACTIONS + 词表段加 respond | 同步 |
| `Scripts/test_respond_latency.py`（新建） | 2s 预算延迟实测 | 工具 |
| `plans/plan-badcases.md` / `llm-goap-plan-execution.md` | BC-006 台账 v2 + 性能铁律/台词来源表/§6.3 更新 | 文档 |

**respond 上下文现状（简易版，本轮已含演算意图）**：
```
【世界观】WorldDescription
【你的身份】职业名 + 人格数值→词（DescribePersonality）
【你此刻的态度】score 阈值 → 热情/正常/敷衍（公式演算结果，已实现）
【对话主题】plan.summary（随从执行意图）
【对方】requester 名字
【对话历史】ReactiveAgent.DialogueHistory 最近 4 句（简易 List，60s 超时清空）
【对方刚说】最后一句
```

## 二、发现：与既有记忆系统重复（整体考虑的核心）

`Memory/` 已有完整轮子（服务玩家-NPC 对话）：

| 层 | 结构 | 说明 |
|----|------|------|
| `RecentHistory` | `List<ChatMessage>`（Role 自由字符串 + Content + 时间戳），保留 10 轮 | 原始对话 |
| `DynamicMemories` | `LinkedList<RecentMemory>`，最多 5 条、每条 ≤30 字 | 对话超 20 句 → LLM 异步总结（`MaintainMemoryAsync`） |
| `PermanentMemory` | `StringBuilder`，≤300 字 | 动态记忆挤出 → LLM 合并（`CheckAndPromoteToPermanent`） |
| `AllNpcMemoryManager.GetMemoryForAgent(agent)` | Hero → StringId 持久记忆；**模板 NPC → `TEMP_AGENT_{index}_{name}` Mission 级兜底**（`ClearTemporaryMemories` 清理） | 所有 Agent 可拿 |

**重复点**：`ReactiveAgent.DialogueHistory`（4 句）≈ `RecentHistory`（10 轮）的轻量子集——两个系统并存 = 目标 NPC 对"和谁聊过什么"的记忆分叉（玩家对话进三层记忆、随从对话进简易列表），违反"情报统一"与 wheels 复用原则。

**差异/冲突盘点**：
1. **角色标记**：玩家对话 `AddHistory("user"/"assistant"/"system", "名字: 内容")`（名字拼在 Content）；随从对话可同构（Role 自由字符串）——**兼容**
2. **记忆维护是 LLM 调用**：`SummarizeAsync`/`MergeMemoryAsync` 走 `CallApiAsync`（3 次重试 + 失败弹 `ShowConnectionMessage` 红字）——NPC-NPC 闲聊触发总结失败会**打扰玩家**，且与 respond 请求并发加剧 429 限流
3. **`GetPrompt_History_Memory_Events` 太重**：为玩家对话设计（远期+动态+事件+新闻+quest 历史全段），respond 2s 预算只需裁剪版（最近 4-6 句 + 动态最新 1-2 条 + 永久记忆摘要）
4. **人设双源**：`NPCProfile.GetPersonaPrompt()`（人设文案）vs `ReactivePersonality`（演算权重）——respond 身份段现状用 ReactivePersonality 描述（与公式演算同源，**保持**）；可评估补充 persona 精华
5. **会话状态**：`DialogueRound`（6 轮上限）与 `LastDialogueTime`（60s 超时）是 respond 专用会话控制，与记忆系统正交——**保留**，但历史读写切到 memory
6. **模板 NPC 记忆生命周期**：`TEMP_AGENT_` 键含 agent.Index（Mission 内唯一，跨 Mission 重建）——随从对话记忆对模板 NPC 不跨场景，可接受（与 ReactiveAgent 注册表同生命周期）

## 三、设计决策

- **D1（主）**：respond 上下文与写入统一走 `AllNpcMemoryManager.GetMemoryForAgent(目标)`。`ReactiveAgent.DialogueHistory` 退役；`DialogueRound`/`LastDialogueTime` 保留（会话控制）。
- **D2（写入）**：respond 侧记录双方——`StartRespond` 写入 `AddHistory(role, "随从名: 台词")`，回复成功再写目标自己一句；与玩家对话共用 `RecentHistory`（同一 NPC 记忆统一，情境连续）。
- **D3（上下文裁剪）**：新增 `BuildRespondContext(memory)`（或 `PromptBuilder.GetPrompt_RespondContext`）：RecentHistory 最近 4-6 句 + DynamicMemories 最新 1-2 条 + PermanentMemory（若有）+ 演算意图段（已实现，保留在 ReactiveAgent）。**不**复用 `GetPrompt_History_Memory_Events`（太重）。
- **D4（记忆维护静默）**：`SummarizeAsync`/`MergeMemoryAsync` 失败路径对玩家弹提示是玩家对话场景需要；随从对话触发的总结失败必须静默——方案：`AllNpcMemoryManager` 侧加"来源标记"或 `SingNpcMemorySystem` 加 `SuppressFailureAlerts` 开关（随从对话时置位），或给 `CallApiAsync` 加静默参数。**避免与 respond 请求同帧并发**（429 风险）：记忆总结已有 `_isSummarizing` 单飞，追加"总结进行中跳过新总结"已有；评估 respond 与总结错峰（总结 60s 冷却）。
- **D5（身份段）**：保持 `DescribePersonality`（ReactivePersonality 演算同源，台词态度与公式一致）；可选补 `memory._profile` 名字/人设一句话——进 plan 的"可选增强"。
- **D6（目标方为玩家时的边界）**：`say_to target=player`（随从对玩家说话）不触发 respond（玩家真人），现状已天然排除（玩家无 brain 事件消费）；随从对模板 NPC/其他随从正常。

## 四、执行清单（审批后按序实施）

1. **ReactiveAgent.cs**：`StartRespond` 改用 `AllNpcMemoryManager.GetMemoryForAgent(agent)`——写入双方对话（D2，`AddHistory(role, "名字: 台词", speakerId)`）、`BuildRespondPrompt` 用裁剪上下文（D3，按 SpeakerId 过滤）；删 `DialogueHistory` 字段（保留 `DialogueRound`/`LastDialogueTime`）；`_registry` 清理时不动 memory（memory 由 AllNpcMemoryManager 管理）
2. **PromptBuilder.cs**：新增 `GetPrompt_RespondContext(memory, otherId)`（裁剪版上下文段：最近 4-6 句[按 otherId 过滤] + 动态最新 1-2 条 + 永久记忆摘要；单一事实源，与 respond 骨架 key 一致走本地化）；`BuildPromptForSummary` 泛化（对方名字从 messages/SpeakerId 提取，去 `Agent.Main` 硬编码——§八）
3. **ChatMessage.cs**：加 `SpeakerId`（可选，向后兼容）；`AddHistory` 重载 `(role, content, speakerId = null)`（§八）
4. **SingNpcMemorySystem.cs**：`SuppressFailureAlerts` 开关（D4）——`MaintainMemoryAsync`/`CheckAndPromoteToPermanent` 失败不再弹红字（DebugLogger 保留）；触发源标记由调用方（respond）设置
5. **429 冷却**：respond 收到 429 → `NextRespondAllowedAt` 冷却（如 10s 内不再发请求，直接模板）——评估后实施
6. **本地化**：respond 上下文段 key（若新增段）
7. **py 同步**：无词表变化（respond 已同步）；`test_respond_latency.py` prompt 结构若变则同步
8. **回归**：`check_vocab_sync.py` + `validate_localization.py` + 延迟实测复跑 + 实机 TALK_TO 验证（酒馆老板记得玩家/随从先前对话）
9. **存档（§七）**：B 先行 = 本 plan 无存档动作；A 后续 = 单独 plan（`MyBehavior.SyncData("lwn_npc_memories")` + 读档重建 + 兼容/大小验收）
10. **文档**：更新 BC-006 台账（记忆整合）、设计文档 §6.3（respond 上下文来源）、wheels.d/planner.md（respond 实时回应 + 记忆复用轮子登记）

## 五、风险与对策

| 风险 | 对策 |
|------|------|
| 记忆总结（LLM）与 respond 并发 → 429 限流加剧 | D4 错峰 + 429 冷却 + 降级模板兜底（现有） |
| 玩家对话与随从对话混在 RecentHistory，respond 上下文误读玩家私密内容 | 角色标记区分 + 上下文裁剪只取"随从/目标"角色行（D3 过滤） |
| `GetMemoryForAgent` 对高频随从对话创建 TEMP 记忆膨胀 | 随从对话写入仅在 respond 时（目标已被搭话），量级低；`ClearTemporaryMemories` 已有 |
| 模板 NPC 记忆不跨 Mission | 可接受（与 ReactiveAgent 同生命周期），文档注明 |
| 记忆总结红字打扰玩家 | D4 静默开关（核心） |

## 六、验收

- [ ] `dotnet build` 0 错误；`check_vocab_sync.py` / `validate_localization.py` 新增项清零
- [ ] `Scripts/test_respond_latency.py`：2s 预算 ≥ 90% 达标（现状 3/3）
- [ ] 实机：随从搭话 → 目标 respond（LLM 台词，态度与演算一致）；目标与玩家聊过 → 随从搭话时目标能接住上下文（RecentHistory 生效）；断网/429 → 降级模板无红字
- [ ] 玩家对话触发的记忆总结红字行为不变（开关默认关，只随从对话置位）

---

## 七、记忆存档决策（2026-08-08 补充）

**现状**：`SingNpcMemorySystem` 全程内存、不进存档（`IntentCooldownStore.cs:11` 注释确认）；玩家对话记忆游戏重启即失。NPC-NPC 对话记忆同理。

**存档接入方式（若做）**：本项目惯用第 2 层 `CampaignBehavior.SyncData` + JSON 字符串模式（`MyBehavior.cs` 统一归档点，`lwn_` 前缀 key，读档 `IsLoading` 反序列化）——记忆系统是普通类 + 静态字典，正好走此模式。**只存 Hero 记忆**（StringId 稳定；TEMP 模板 NPC 键含 agent.Index 不稳定，不存）。

**存档大小估算**：每 NPC = RecentHistory 10 轮（~50-100B/轮）+ DynamicMemories 5×30 字 + PermanentMemory 300 字 ≈ **1-2KB/NPC**；全 Hero 按 ~100-200 有名 NPC ≈ **200-400KB 上限**；惰性优化（仅序列化有对话记录的 NPC，`RecentHistory/DynamicMemories/PermanentMemory` 全空的跳过）实际远小于此。对照：存档本体 MB 级——**影响可接受**。

**决策：两阶段**：
- **B 先行（本 plan）**：保持不进存档——NPC-NPC 对话记忆是氛围层，重启即忘可接受；规避存档兼容/加载验证风险（存档机制是独立风险域：`SaveErrorReporter` 取证流程 + 字段 ID 步进惯例）。
- **A 后续（单独 plan）**：Hero 记忆 JSON → `MyBehavior.SyncData("lwn_npc_memories", ref json)`；读档重建 `AllNpcMemoryManager._activeMemories`（按 StringId 填回）；验收含"旧存档无 key → 空字典"兼容 + 存档大小实测。

## 八、记忆从"玩家中心"泛化到"任意人"（2026-08-08 补充）

**现状盘点**（好消息：基础结构已天然支持）：
- `ChatMessage`：Role（自由字符串）+ **Content 已拼"说话人名字: 台词"**（`InteractionController` 写入时拼 `Name: input`）+ 时间戳
- `GetPrompt_History_Memory_Events` 的历史段：`foreach (var msg in RecentHistory) → "-{msg.Content}"`——**不按 Role 过滤、全量输出**，LLM 从 Content 里自行理解说话人——NPC-NPC 历史混入后天然可读

**玩家中心假设残留（3 处）**：
1. `BuildPromptForSummary`（记忆总结）：硬编码 `你刚刚和{Agent.Main.Name}进行了一段对话`——NPC-NPC 总结会把随从对话写成"和玩家聊过"（错误记忆！）
2. `GetPrompt_History_Memory_Events` 传闻段：`Hero.MainHero.StringId` 过滤（respond 用裁剪版上下文 D3 避开，玩家对话路径不动）
3. `GetPlayerDescription`：玩家专属描述段（玩家对话用；respond 不需要）

**改动清单**：
1. `ChatMessage` 加 `SpeakerId`（说话人标识：Hero StringId / TEMP 键 / "player"，**可选**——玩家对话不传向后兼容，Content 已有名字）
2. `AddHistory` 重载：`AddHistory(role, content, speakerId = null)`；随从对话写入传 `requester` 的标识
3. `BuildPromptForSummary` 泛化：对方名字从 messages 提取（对比 `memory._profile` 找出"非我"的说话人）或优先用 `SpeakerId`——不再硬编码 `Agent.Main`
4. respond 裁剪版上下文（D3）按 `SpeakerId` 过滤"与当前对方相关"的行；"对方刚说" = 过滤后最后一句（混合多人对话时不错位）
5. 玩家对话路径零改动（Role=user/assistant 语义不变）

**验证**：玩家对话回归（历史/总结行为不变）+ 随从对话记录进 RecentHistory + 总结正确归因（不写成"和玩家聊过"）

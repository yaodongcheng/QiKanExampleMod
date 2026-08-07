# planner — 轮子速查分卷（wheels.md 索引导航）
## 密谋命令系统（LLM 计划生成 + 确定性执行）— `Planner/` + `Interaction/PlanCommandFlow.cs`

设计文档：`plans/llm-goap-plan-execution.md`（已实施，状态行含已知偏差）。

**一句话**：玩家对随从下自然语言命令 → LLM 一次调用出「意图分类 + 计划 JSON + 反应计划」→ 玩家批准 → `PlanExecutor` 确定性执行（零 LLM）→ Guardrail R1-R7 安全网 + Replan 低频重入。

**四件套**（新增玩法 = 意图枚举加一行 + GoalTemplate 加一个 + prompt 描述，框架零改动）：

| 部件 | 文件 | 关键签名 |
|------|------|---------|
| 计划语法 | `Planner/PlanGrammar.cs` | `PlanValidator.Validate(Plan, string) → PlanValidationResult`（步骤级降级：未知动作丢弃该步，>50% 拒收；跳转 S1-S4 双向校验；参数钳制） |
| 世界状态 | `Planner/RuntimeWorldState.cs` | `Evaluate(Condition, Agent) → bool`（13 谓词 + sustained/was 修饰 + query 动态解析）；was 记录**基础谓词**（不含 op），与当前 op 值 AND |
| 执行器 | `Planner/PlanExecutor.cs` + `InlineSteps.cs` | `PlanExecutor.Create(ownerAgent, plan, intentType, roleAgents)` + `AgentAIController.SendEventToAgent(npc, "order_execute_plan", planJson, intentType, target, originalCommand)`；`AgentAIController.OnMissionTick` 已挂 `PlanExecutor.TickAll(dt)` |
| 对抗方 | `Planner/ReactiveAgent.cs` | 触发词 `spoken_to`/`asked_to_follow`/`left_post_seconds`… → 人格演算（weight×修正取最高）→ 反应动作；决策结果广播 `plan_decision`（refused/followed）；职业默认模板兜底 |

**新增玩法意图的接入点**（三处）：
1. `PlanGrammar.cs` `PlanVocab.Actions`（动作词表）/ `Predicates`（谓词词表）/ `Queries`（动态查询）
2. `GoalTemplates.cs` `CommandIntentType` 枚举 + `GoalTemplates.IsCombatIntent/IsEventDriven/IsKeepType`
3. `PlanCommandFlow.BuildIntentTable()`（prompt 意图词表描述）

**常用调用范例**：

```csharp
// 下达计划（LLM 批准后 / 调试注入）
AgentAIController.Instance?.SendEventToAgent(companion, "order_execute_plan", planJson, intentType, target, originalCommand);

// 停止键（R3）
PlanCommandFlow.StopPlan(companion);   // 当面冒泡 / 远距离密信，双通道

// 调试跑示例
// custom.plan_debug run A_DISTRACT          → 注入并执行示例计划
// custom.plan_debug snapshot / status / stop

// 密谋对话壳入口（Plot 玩法行分发）
PlanCommandFlow.Start(companion);       // 需 Settings.Instance.IsLLMReady（铁律 1 总闸）
```

**关键纪律**（踩过的坑）：
- **执行器挂接**：`ExecutePlanAction : IAtomicAction` 挂 brain 队列（IsFinished=计划完成）；执行器本体由 `AgentAIController.OnMissionTick → TickAll` 独立驱动（与队列解耦，收尾当面报告流程也能跑）。护主/战斗会 ClearAllActions 踢掉队列项——无妨，执行器独立 tick。
- **收尾三路一函数**：`Finish(state, message, needFaceReport)` → 密信（DisplayMessage，**非 NinjaNotification——那是模态锁鼠标**）或当面报告（恢复跟随走回玩家 3m 冒泡转述，60s 超时密信兜底）。`end_plan` 的 `report` 触发当面报告。
- **双通道**：决策结果（refused/followed）走 `plan_decision` 事件 → brain 转发 `NotifyDecisionEvent` → 步骤 `on_event` 即时跳转（队列按步骤开始时刻过滤过期事件，**勿在 CompleteStep 清空队列**——say_to 广播的事件要留给下一步消费）；持续事实走 100ms 轮询谓词。
- **was 修饰**：记录"基础谓词曾成立"（忽略 op），与 op 结果 AND——`following==false && was:true` 在折返瞬间才成立，计划开局不误触发。contingency 触发后 `ForgetWasEver` 恢复掉线重触发（one_shot:false 语义）。
- **when 门控超时**：门控不成立期间 `StepElapsed` 照常累计并检查超时（防挂死到 R6 总闸）。
- **角色解析**：`SceneSnapshot` 按 StringId 关键词自动打标（guard/villager/merchant…）；`FindAgent` 全等+子串+职业匹配。铁律 5 两轮策略的语义版。
- **文本本地化**：执行器/安全网玩家可见消息走 `PlanTexts` 静态表（`LWN_plan_abort_*` 等）；LLM 生成的 signal 文本豁免（运行时内容直接显示）。
- **R4 豁免**：当前步骤 target/zone 距玩家 >30m = 独行任务不叫回（按世界状态判定，不用意图白名单）。
- **Replan**：`PlanReplan.Wire(executor, originalCommand, intentType)` 在 order_execute_plan 分支自动接；成功产出新计划才消耗额度（≤2）。
- **validator 谎报硬检查**：条件等待步骤（带 until 的 wait/move_to）的 `on_timeout`/`on_event[].then` 指向 `result="success"` 的 end_plan = 谎报 → 忽略跳转（按 @abort_gracefully）；纯时长等待（wait seconds）不查。**py `validate_plan`（test_llm_plan.py）与 C# `PlanValidator` 双份同步**。
- **validator 谓词词表检查**：until/when/goal/triggers/contingencies/loop.until 的 type ∈ 16 个谓词；**事件词（approach_by/player_suspicious_near 等）写进条件 = 未定义谓词**（v10 实测）→ 丢弃条件；`then` 字段必须是字符串（模型会把 trigger 对象结构写进 contingency → 类型报错不崩溃）。
- **保持型纪律（prompt 纪律 15）**：望风/压阵/缠住/盯梢 = 无限 wait（省略 seconds/until/timeout）+ triggers，**不设 goal、不 success 收尾**（"等 N 秒没人来 = 成功"是高频错误），结束 = 玩家 R3 叫停。任务型 vs 保持型：有成功时刻 → goal + success 收尾；无成功时刻 → 保持 + 叫停。
- **prompt-代码同步维护**（改一边必须同步全部）：C# `PromptBuilder.BuildPlanPrompt`（纪律 1-18 + 质量要求 + 双示范）、`PlanCommandFlow.BuildIntentTable`（意图 few-shot）/`BuildGrammar`（词表）、`PlanGrammar`（Actions/Predicates/Queries/ActionAliases）、`ReactiveAgent`（反应词表）↔ py `test_llm_plan.py`（INTENT_TABLE/GRAMMAR/ALLOWED_ACTIONS/PREDICATES/REACTIVE_EVENTS/REACTIVE_ACTIONS）+ `test_llm_plan_stress.py`（命令集与期望标注）。每份都有"与 C# xxx 同步"注释。

**接线改动清单**（改这些文件时别漏）：
- `AgentBrain.cs`：`order_execute_plan` / `plan_decision` 事件分支、`RunReactiveAction`（ReactiveAgent 反应通道）、`EnqueueActionInternal/ClearAllActionsInternal`（plan_debug 专用）、`OnPlanExecutorFinished`（收尾恢复 Following，仅当意图仍为 ExecutingCommand）
- `NpcIntent.cs`：`ExecutingCommand` + `CommandIntentType? CommandDetail`（复用 Confronting+InterceptDetail 模式，ToString 拼接同构）
- `AgentAIController.cs`：`OnMissionTick` 加 `PlanExecutor.TickAll(dt)`；`OnRemoveBehavior` 加 `PlanExecutor.ShutdownAll()`（Mission 结束统一收尾）
- `InteractionMissionView.cs`：Plot/StopPlan 玩法行（available 条件 = 随从关系 `brain.Leader==Main || Following/ExecutingCommand`；密谋中该随从 Talk 行互斥移除）+ `PlanCommandFlow.Tick()`/`PlanReplan.Tick()`（主线程消费 LLM 结果）
- `AgentHudVM.cs` + `AgentHudNearby.xml`：`ShowPlanSummary`/`PlanSummaryText` 执行摘要（三处联动 + FOV 外防残留）
- `LLMService.ChatAsync(systemPrompt, max_tokens, needJson, temperature)`；`PromptBuilder.BuildPlanPrompt(snapshotText, command, persona, history, intentTable, grammar)`

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

**新增玩法意图的接入点**（2026-08-08 重构为单一事实源：词表 = `*InPromptOrder` 数组，prompt 自动读到；注册后跑 `Scripts/check_vocab_sync.py` 验证 C#/py 词表一致）：
1. **新动作**（如"下毒"）：🔴 2026-08-13 重构后 = **`ActionRegistry.cs` 主表加一行**（`InPlanVocab=true` 自动进计划词表，`InChatSpace=true` + ChatOrder 自动进闲聊空间；`ActionsInPromptOrder` 等全部派生）→ `InlineSteps.ExecuteStep` 补执行 case（闲聊侧如需单步点火则执行委托走 `ChatActionFlow.TryExecute`）→ py `ALLOWED_ACTIONS` 加行（回归测试）
2. **新意图**：`GoalTemplates.cs` `CommandIntentType` 枚举加行 + `PlanCommandFlow.IntentPhrases` 加行（**prompt 意图词表自动读到**）→ `BuildIntentTable` 手写区补 few-shot 判定基准（分类示范知识）→ GoalTemplates 表（Success/Maintain）
3. **新谓词/新查询**：`PlanGrammar.cs` `PredicatesInPromptOrder`/`QueriesInPromptOrder` 加行（**prompt 自动读到**）→ `RuntimeWorldState.Evaluate` 补求值实现 → py `PREDICATES`/`QUERIES` 加行
4. **新触发词/新反应**：`ReactiveAgent.cs` `TriggerEventsInPromptOrder`/`ReactionActionsInPromptOrder` 加行（**prompt 自动读到**）→ `TryHandleEvent`/`ExecuteReaction` 补分支 → py `REACTIVE_EVENTS`/`REACTIVE_ACTIONS` 加行

## 🔴 ActionRegistry 动作主表（2026-08-13 起为动作注册单一事实源）— `Planner/ActionRegistry.cs`

**解决什么问题**：动作注册曾分散 6+ 处（PlanVocab 21 码 + ActionHandler 27 大写码 + 标签表 + prompt 注入 + 参数填充 switch + 播报 switch），新增动作改 5+ 处，且出现过词表注册但执行器未实现的失同步。现在 **34 行主表 = 全部事实，其余全派生**。

**主表字段**（`ActionSpec`）：`Code`（统一小写码，计划/闲聊共用）/ `Description`（闲聊 prompt 描述）/ `LabelKey`+`LabelFallback`（标签表）/ `InPlanVocab`（进计划词表）/ `InChatSpace`（进闲聊空间）/ `ChatOrder`（闲聊展示序 1..27 钉死）/ `Spaces`（空间位掩码）/ `NeedsCooldown` / `RequiresConfirm`+`InquiryTitleKey`+`InquiryMsgKey`（确认弹窗/卡片）/ `Aliases[]`（计划侧 LLM 容错）/ `ResultKeys`（result 路由）/ `IsTerminal` / `ExecutorImplemented`（shadow/negotiate/duel=false）/ `IsValid` / `Execute`（闲聊点火或 hero/party 行为）/ `ExecuteCore`（卡片批准后核心执行）/ `FillParams(step, level, sayText)` / `AnnounceParam(level)`。

**34 行结构**：前 21 行计划原序（82% LLM 回归基线，静态构造 `Debug.Assert` 钉死）+ 后 13 行闲聊-only。交集 14 行合并（如 `order_attack` 行 Aliases["attack"] 承载旧码、duel 双语义一行承载）。

**派生面清单**（全部只读主表）：`PlanVocab.ActionsInPromptOrder/Actions/ActionAliases/AllowedResultKeys/TerminalActions`（PlanGrammar.cs）、`ActionHandler` 五入口（HandleAction/HandleImAction/GetActionSpacePrompt/AnnounceDecision/PostActionProposal + RunActionCore 第三处查找）、`PlanActionLabel`（ImCommandFlow.cs，FindByLabelCode 先 ByCode 回落别名）、`ChatActionFlow.TryExecute`（FindByCode?.FillParams）、`Scripts/check_vocab_sync.py`（按大括号深度配对提取 ActionSpec 块）。

**关键纪律**：
- **单码统一**：Code 全小写；旧存档大写码（`ImMessage.ActionCode="MOVE_TO"`）由 `FindByCode` 的 **OrdinalIgnoreCase** 兼容（漏 = 旧决策卡片批准后执行失败）
- "NONE" 哨兵大写保留（三处跳过判据：HandleImAction 提前返回 / GetActionSpacePrompt 跳过 / AnnounceDecision 跳过）
- **执行职责边界**：主表只注册 + 闲聊入口接线；agent 动作行为语义归 PlanExecutor（`Execute` = 包装单步 Plan 走 `ChatActionFlow.TryExecute` → 既有 TryCreateSubAction 分支）；hero/party 动作 `Execute` 才是行为实现。RequiresConfirm 的 `ExecuteCore` = 卡片批准后直接执行（不再二次确认）
- **双身份**：`order_attack` 主表动作码 ≠ AIEvent 事件名（AgentBrain.cs:387 白名单协议）——Brain 层协议不注册，行注释标明桥接
- 新动作若需新行为 = 主表一行 + AtomicAction/InlineState 新类 + TryCreateSubAction case；仅需已有行为 = 主表一行（Execute 委托指向既有通道）
- 静态构造自检五连（🔴 失败只写日志不弹窗——Debug.Assert 实机 Debug 构建弹断言框崩游戏，2026-08-13 崩溃实录；ExecutorImplemented 字段默认值必须 `= true`，bool 默认 false 会让 34 行全判"未实现"）：计划 21 码字面量序 / ChatOrder 1..27 连续 / ExecutorImplemented=false=={shadow,negotiate,duel} / 别名无重复 / IsValid+闲聊空间行 Execute 非空

**新增动作流程**（详见上方接入点第 1 条）：主表一行（Code/Description/标签/空间/布尔位/ChatOrder/IsValid/Execute）→ 执行语义落点 → py 词表加行 → 跑 `check_vocab_sync.py`。

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
PlanCommandFlow.Start(companion);       // 需 Settings.Instance.IsLLMConfigured（铁律 1 总闸）
```

**关键纪律**（踩过的坑）：
- **实时回应（respond，BC-006）**：目标被搭话的回应 = 每回合一次 LLM（`ReactiveAgent` respond 分支 → `StartRespond` → `LLMService.ChatOnceAsync` 单次 2s 预算、失败静默 null）→ 结果入队 → `AgentAIController.OnMissionTick → ReactiveAgent.TickAll` 主线程 `FaceToActor` + `AgentSay` 播放（轻量冒泡不接管 brain）。**降级链**：LLM 未配置/超时/失败/回合超限（6 轮）→ 职业模板台词（`LWN_reactive_respond_*`）。**429 → 10s 全局冷却**（`ChatOnceAsync` 内建）。上下文 = 世界观 + 身份（职业+人格描述）+ **演算意图**（score→热情/正常/敷衍，台词与公式一致）+ 主题/轮次 + 对方 + **三层记忆裁剪**（`PromptBuilder.GetPrompt_RespondContext`，按 SpeakerId 过滤）。**记忆统一走 `AllNpcMemoryManager.GetMemoryForAgent`**（Hero 持久/TEMP 兜底），对话写入 `AddHistory(role, "名字: 台词", speakerId)`；随从对话置 `memory.SuppressFailureAlerts = true`（记忆维护失败静默，玩家对话路径不变）。详细见 `plans/npc-live-dialogue-memory-plan.md`。
- **对话模式（say_to + outline，BC-006 v4）**：TALK_TO 多轮对话 = `say_to` 省略 text、带 `topic` + `outline`（2-5 段走向数组）——**计划期只定话题走向，双方台词执行期 LLM 实时生成**（4-7 句来回）。执行：`SayInlineState` 对话状态机（开场[LLM 生成或预写 text]→ 广播 spoken_to 带走向段 → 轮询目标记忆等回应 → 随从续话[`ReactiveAgent.GenerateCompanionLine`，2s 预算 + 走向模板 `LWN_plan_chat_fallback` 兜底] → 走向完收尾）。`PlanGrammar.OutlineSegments` JToken 容错（非法 → 退化单句）+ validator 2-5 段。prompt 纪律 11/quality + `LWN_plan_example_chat` 示范（EN/CN）。单句模式完全向后兼容（有 text 不变）。
- **执行器挂接（单脑化，2026-08-11 重构后）**：`order_execute_plan` 分支直接 `executor.Start(agent)` **不入队**（占位动作 ExecutePlanAction 已删）；「计划执行中」哨兵 = `NpcIntentType.ExecutingCommand` 意图（AgentBrain Tick 空脑分支的 **D2 空窗守卫**：`_currentIntent?.Type != ExecutingCommand` 才跑 DecideDefaultBehavior——否则 100ms 轮询空窗期跟随/原版 AI 抢跑）。行为步骤由执行器逐个 `brain.EnqueuePlanAction(action)` 入队，**生命周期归脑**（OnStart/OnTick/OnEnd/IsFinished 全部脑侧，D4b）。执行器侧只做 100ms 轮询三路径判定：①IsFinished → CompleteStep（只摘引用，不 OnEnd）②`!brain.IsActionAlive(action)`（外部清除 = 战斗/护主/击晕/搭话）→ 计划中止 + 收尾报告（战斗/击晕中 → 密信；玩家在场脱得开身 → 当面报告）③自身 RequestInterrupt（Pause/超时/跳转）。**迁移纪律**：所有迁移/终止（until/Jump/超时/Pause/Finish/end_plan 失败/Tick 异常）对当前动作 `RequestInterrupt()` + 摘引用，**禁止再调 OnEnd**；`ClearSubAction` 拆两态——`DetachSubAction`（正常迁移，teardown 归脑）/ `TeardownSubAction`（异常兜底：无脑入队失败/Agent 不活跃脑已死时补 OnEnd）。**行为性内联**（lead/steal_attempt/knockout/emote，`IsBehavioral=true`）= 状态机 + `InlinePlanAction` 适配器（AtomicAction.cs）入队；非行为性内联（say_to/wait 等）排序器侧直驱、Pause 保留状态。**Pause 必须摘除 SubAction 引用**（不摘 → Resume 后首轮询 IsFinished==true 误判完成 → 跳步；行为性内联重建全新状态机）。`TickInner` 顶部镜像 `IsInteractionDisabled` 门控（含超时冻结）。**脑侧 Callers**：`EnqueuePlanAction`/`IsActionAlive`（internal）；收尾 `OnPlanExecutorFinished` 意图复位 None（仅当意图仍 ExecutingCommand——不覆盖玩家新命令）。
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
- `AgentBrain.cs`：`order_execute_plan` / `plan_decision` 事件分支、`RunReactiveAction`（ReactiveAgent 反应通道）、`ClearAllActions`（internal，plan_debug 直接调用）、`EnqueuePlanAction`（计划动作入队，单脑化 M2）、`OnPlanExecutorFinished`（收尾意图复位 None——DecideDefaultBehavior 护卫跟随已注释，恢复 Following 无对应动作、HUD 误导；仅当意图仍 ExecutingCommand 才复位，不覆盖玩家新命令）
- `NpcIntent.cs`：`ExecutingCommand` + `CommandIntentType? CommandDetail`（复用 Confronting+InterceptDetail 模式，ToString 拼接同构）
- `AgentAIController.cs`：`OnMissionTick` 加 `PlanExecutor.TickAll(dt)`；`OnRemoveBehavior` 加 `PlanExecutor.ShutdownAll()`（Mission 结束统一收尾）
- `InteractionMissionView.cs`：Plot/StopPlan 玩法行（available 条件 = 随从关系 `brain.Leader==Main || Following/ExecutingCommand`；密谋中该随从 Talk 行互斥移除）+ `PlanCommandFlow.Tick()`/`PlanReplan.Tick()`（主线程消费 LLM 结果）
- `AgentHudVM.cs` + `AgentHudNearby.xml`：`ShowPlanSummary`/`PlanSummaryText` 执行摘要（三处联动 + FOV 外防残留）
- `LLMService.ChatAsync(systemPrompt, max_tokens, needJson, temperature)`；`PromptBuilder.BuildPlanPrompt(snapshotText, command, persona, history, intentTable, grammar)`

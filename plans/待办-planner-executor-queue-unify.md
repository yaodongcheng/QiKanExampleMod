# PlanExecutor 单脑化重构 — 执行器降级为步骤排序器

> 状态：设计稿（未实施）。关联设计：`plans/llm-goap-plan-execution.md`（现执行架构）。
> 目标版本：当前开发机 v1.4.8。重构完成前本地功能照旧（阶段 0 现状可玩）。

## Context — 为什么要动

**现状（两个大脑）**：`ExecutePlanAction` 挂脑队列（IsFinished=计划完成），执行器本体由 `AgentAIController.OnMissionTick → PlanExecutor.TickAll` **独立驱动**——行为步骤（SubAction）由执行器自己 OnStart/OnTick，完全绕过脑队列。

**实锤**（[wheels.d/planner.md:43](plans/rules/wheels.d/planner.md#L43)，设计文档原文）：

> 护主/战斗会 ClearAllActions 踢掉队列项——**无妨，执行器独立 tick。**

——即脑被打断后，脑的战斗动作与执行器的计划步骤**同时驱动同一 Agent**（一边 SetTargetAgent 战斗、一边 0.2s 一次 ScriptedMoveToPoint 走计划路线）。

**三个硬伤**：

| 问题 | 具体表现 |
|---|---|
| 调度竞争 | 脑动作 + 执行器 SubAction 同时给同一 Agent 下指令 |
| 状态分歧 | 脑的 `EffectiveAction` = ExecutePlanAction（占位），实际行为 = 执行器的 SubAction——警戒/意图/质问锁对"NPC 正在做什么"认知失真 |
| 门控不对称 | 脑三道门控（不活跃 / IsInteractionDisabled / 质问锁）对执行器全部失效——被打晕计划照跑（不符合直觉） |

**设计哲学**：NPC 的每个行动都必须走 `AgentBrain` 自己的调度。计划应服从游戏既有语义（[AgentBrain.cs:466](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L466)"已在战斗中：只感知不换目标"）——被打断就该放下计划去保命/逃跑/报告，而不是机械执行。

## 目标架构

```
PlanExecutor（保留，降级为排序器）            AgentBrain（唯一调度者）
├─ 步骤排序：何时开始下一步                     ├─ 行为步骤全部 EnqueueAction（FIFO）
├─ 内联步骤：wait / say_to 对话状态机 /         ├─ 被打断 → ClearAllActions 自然中断计划步
│    make_noise（非行为性，无 Agent 控制冲突）    └─ 门控天然生效：不活跃/战斗/质问 = 计划暂停
├─ 超时 / until / when / on_event 跳转
└─ 收尾报告（密信/当面，独立于队列——本就该独立）
```

**行为步骤定义**：一切驱动 Agent 的动作（FightEnemyAction / FollowAgentAction / MoveToPositionAction / LookAtAction / TurnToDirectionAction / StayAction…）。判定标准 = 该动作是否写 Agent 移动/朝向/战斗指令。

## 实施阶段

### 阶段 1：止血（~10 行，可先行落地）

执行器 `TickCursor` 驱动 SubAction 前检查脑是否被接管：`GetBrainForAgent(cursor.Agent)` 的当前执行动作（`EffectiveAction`）不是本计划的 ExecutePlanAction → **暂停行为步骤的 OnTick**（排序/超时/收尾照跑）。

- 消除"两个驱动打架"的即时冲突
- 不改架构，回归风险极低

### 阶段 2：统一（大改，本文件主体）

**2.1 行为步骤 → 脑队列**
- `TryCreateSubAction` 产出动作后**不再**执行器侧 OnStart/OnTick，改 `AgentBrain.EnqueuePlanAction(action)`（新增 internal 方法，或复用 `EnqueueActionInternal`——plan_debug 已在用）
- `ExecutePlanAction` 语义不变：脑队列里它仍是"计划进行中"占位（IsFinished=计划完成）——**但**占位与行为步骤的关系需要定：行为步骤排在占位之后？还是占位即队列空位？
  - 推荐：`ExecutePlanAction` 出队后即完成（占位不再持有），行为步骤动作直接入队——脑队列里就是"动作 → 动作 → …"的普通队列，ExecutePlanAction 只负责启动 + 启动失败兜底

**2.2 步骤完成检测（执行器侧轮询）**
- 执行器持有当前步骤动作引用，每 100ms（谓词轮询同节奏）检查 `action.IsFinished(agent)`
- 完成 → `CompleteStep`（until/when/超时检查照旧）→ 排序下一步
- 引用失效（不在队列、不是 current、且未完成）= 被 ClearAllActions 清掉 = **步骤中断**

**2.3 中断语义（关键决策）**
- 被清 = 计划**中止**（graceful，走既有 `@abort_gracefully` 词汇）——被打断意味着外部世界介入，计划前提已失效，不做"战斗结束恢复计划"
- 收尾报告**立即发**（密信通道，不依赖当面）——报告逻辑保持独立 tick，这是"报告独立"的正当用途
- 脑停摆（被击晕/倒地）→ 队列不推进 → 计划自然暂停；醒来后由中止流程收尾
- R3 玩家叫停 / Replan 逻辑不受影响（它们本来就走事件/轮询，不经队列）

**2.4 执行器保留的内联路径**
- `InlineSteps.ExecuteStep` 全部（wait / say_to SayInlineState / make_noise / end_plan / report…）——非行为性，继续执行器侧驱动
- `on_event` 跳转、`was` 修饰、`when` 门控超时、R4/R6 安全网——全在排序器侧，不动

## 接线清单（参照 wheels.d/planner.md:57 现清单）

| 文件 | 改动 |
|---|---|
| `Planner/PlanExecutor.cs` | `TickCursor` 重构（创建→enqueue；OnTick 改 100ms 轮询完成检测）；`TryCreateSubAction` 不再 OnStart；`CompleteStep`/`HandleStepTimeout` 触发点调整；新增"动作引用被清 → 中止"判定 |
| `AI/AgentBrain.cs` | 新增 `EnqueuePlanAction(IAtomicAction)`（internal）；`order_execute_plan` 分支不变 |
| `AI/Actions/AtomicAction.cs` | `ExecutePlanAction.OnEnd` 补语义（当前空 OnEnd 是"脑清掉占位但不通知执行器"的源头）——改为经执行器通知中止（或依赖轮询，二选一，倾向轮询零侵入） |
| `Planner/InlineSteps.cs` | 不动（内联路径保留） |
| `Planner/ReactiveAgent.cs` | 不动（respond 消费链独立） |
| `Interaction/PlanCommandFlow.cs` / `Planner/PlanReplan.cs` | 验证全链路（StopPlan / Replan / 密谋对话壳） |

**不动**：计划语法（PlanGrammar/PlanValidator）、世界状态（RuntimeWorldState）、prompt 词表（`test_llm_plan.py` 同步面）——语法层与执行层解耦，本重构零触碰。

## 风险与回归点

| 回归点 | 验证手段 |
|---|---|
| say_to 对话状态机（SayInlineState） | `custom.plan_debug run` 对话类计划，多轮对话完整走完 |
| until 提前完成 / when 门控 / 超时 | 条件等待计划（wait with until），触发与不触发两侧 |
| 收尾报告（密信 + 当面恢复跟随） | 计划成功/失败/中止三态各跑一次 |
| Replan（≤2 次额度） | 失败触发 replan，额度消耗正确 |
| 计划被战斗打断 | 执行中攻击随从 → 计划中止 + 密信报告，无脚本指令打架 |
| 被打晕期间 | 计划暂停/中止，醒来无残留状态（跟随恢复正确） |

**验收主线**：执行中随从被砍 → 脑战斗动作是唯一驱动者（无计划脚步残留）→ 计划 graceful 中止 → 密信报告到达。

## 阶段 1 单独评估

若只想先止血：阶段 1 落地（~10 行）+ 本重构延后。止血后"两个大脑"仍存在（状态分歧、门控不对称未解决），但即时冲突消失。建议阶段 1 先行，验证无回归后再评估阶段 2 投入。

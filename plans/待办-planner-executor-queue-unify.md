# PlanExecutor 单脑化重构 — 执行器降级为步骤排序器（彻底重构版 v3）

> 状态：**已实施**（2026-08-11 落地，M0–M2 全部完成，M3 静态回归通过；Debug/Release 编译 0 错误）。
> 🔴 落地偏差说明（2 处实现取舍，均已按设计文档意图收敛）：
> ① D4 外部清除的报告通道：战斗/击晕中（脱不开身）→ 密信；其余（含 ReactiveAgent 搭话，玩家在场时）→ 当面报告（60s 超时密信兜底）——「玩家在场当面报告」是 D4 主表语义，D2 例外段落的"密信报告"按脱不开身场景收敛。
> ② 无脑 actor（如玩家被指定为 actor，玩家永不注册 brain）：TeardownSubAction 兜底 + 步骤失败（on_timeout/abort），不再执行器直驱玩家（直驱本来就是"两个大脑"问题的玩家变体）。
> 关联设计：`plans/llm-goap-plan-execution.md`（现执行架构）。
> 目标版本：当前开发机 v1.4.8。
> **评审结论（v3）**：v1 三硬伤判断正确；v2 补两个致命缺口（内联「非行为性」假设不成立、脑空窗竞态）与一个不成立模型（FIFO 占位）；v3 评审（源码逐条核验）补**第四个致命缺口：动作生命周期所有权未界定**——僵尸动作 / 双 OnEnd / 暂停跳步三个漏洞同根，统一收口为 D4b 不变式。
> **范围决策**：**不做止血**（原阶段 1 删除）。单次彻底重构，拆四个里程碑逐步落地验证。附章：IAtomicAction 动作类合并评估（解耦，独立落地）。

## Context — 为什么要动

**现状（两个大脑）**：`ExecutePlanAction` 挂脑队列（IsFinished=计划完成），执行器本体由 `AgentAIController.OnMissionTick → PlanExecutor.TickAll` **独立驱动**——行为步骤（SubAction）由执行器自己 OnStart/OnTick，完全绕过脑队列。

**实锤**（[wheels.d/planner.md](plans/rules/wheels.d/planner.md#L43)，设计文档原文）：

> 护主/战斗会 ClearAllActions 踢掉队列项——**无妨，执行器独立 tick。**

——即脑被打断后，脑的战斗动作与执行器的计划步骤**同时驱动同一 Agent**（一边 SetTargetAgent 战斗、一边 0.2s 一次 ScriptedMoveToPoint 走计划路线）。

### 五个硬伤（原稿三个 + 评审补充两个）

| 问题 | 具体表现 |
|---|---|
| 调度竞争 | 脑动作 + 执行器 SubAction 同时给同一 Agent 下指令 |
| 状态分歧 | 脑的 `EffectiveAction` = ExecutePlanAction（占位），实际行为 = 执行器的 SubAction——警戒/意图/质问锁对"NPC 正在做什么"认知失真 |
| 门控不对称 | 脑三道门控（不活跃 / IsInteractionDisabled / 质问锁）对执行器全部失效——被打晕计划照跑（不符合直觉） |
| 🔴 空窗竞态（评审新增） | 执行器 100ms 轮询完成检测，脑**每帧**出队：动作完成瞬间脑就 OnEnd 出队，执行器要等下一轮 100ms 才调度下一步——**空窗期脑见 `EffectiveAction==null` 跑 `DecideDefaultBehavior()`**（入队跟随/恢复原版 AI，[AgentBrain.cs:1526](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L1526)），跟随动作占住队头，下一步计划动作排在其后先执行跟随——**比现状的显式打架更隐蔽** |
| 🔴 内联非行为性假设不成立（评审新增） | 内联步骤 10 个中 4 个（`lead`/`steal_attempt`/`knockout`/`emote`）直接驱动表现层（ScriptedMoveToPoint / SetPose / 击晕姿势）——只把 IAtomicAction 入队，`LeadInlineState` 与队列移动动作**照旧打架** |

**设计哲学**：NPC 的每个行动都必须走 `AgentBrain` 自己的调度。计划应服从游戏既有语义（[AgentBrain.cs:466](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L466)"已在战斗中：只感知不换目标"）——被打断就该放下计划去保命/逃跑/报告，而不是机械执行。

## 目标架构

```
PlanExecutor（降级为纯排序器）                 AgentBrain（唯一调度者）
├─ 步骤排序：何时开始下一步                     ├─ 一切表现层动作 EnqueueAction（FIFO）
├─ 编排步骤：wait / say_to / signal_player /    ├─ 被打断 → ClearAllActions 自然中断计划步
│    end_plan / make_noise（纯逻辑/通信，       ├─ 门控天然生效：不活跃/战斗/击晕 = 计划暂停或中止
│    不写表现层，排序器侧直接驱动）              └─ 🔴 空窗守卫：ExecutingCommand 意图 = 「计划执行中」
├─ 行为性内联：lead/steal/knockout/emote        　　哨兵，空脑不跑 DecideDefaultBehavior
│    = 状态机 + InlinePlanAction 队列适配器
├─ 超时 / until / when / on_event 跳转
├─ 生命周期收口（D4b）：任何迁移/终止 = RequestInterrupt + 摘引用，不调 OnEnd
└─ 收尾报告（密信/当面，独立于队列）
```

**表现层步骤定义**（评审修正）：一切写 Agent 表现层的动作——移动（ScriptedMoveToPoint）/ 朝向（SetMovementDirection）/ 视线（SetLookAgent/LookAtAgent）/ 战斗（SetTargetAgent/CombatManager）/ 姿势（SetPose/TryToWieldWeaponInSlot）——**全部**进脑队列。纯逻辑/通信（计时、冒泡台词、事件广播、音效、跳转）留在排序器侧。

## 核心设计决策（评审修订 v3）

**D1. 占位删除，意图当哨兵** — `ExecutePlanAction` **整个删除**（[AtomicAction.cs:1605](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L1605)）。原稿"占位出队后即完成"模型在 FIFO Queue 上**不成立**：脑只在 current 完成后才取队列下一个（[AgentBrain.cs:1533](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L1533)），占位若 IsFinished=false 会永远卡在 current，队列里的行为步骤**永远不出队**（死锁）。替代：
- `order_execute_plan` 分支（[AgentBrain.cs:350](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L350)）改为 `executor.Start(agent)` + `SetNpcIntent(ExecutingCommand)`，不入队
- 「计划执行中」哨兵 = `NpcIntentType.ExecutingCommand`（计划结束时 `OnPlanExecutorFinished` 意图复位 None 自动放行）
- 队列里就是「动作 → 动作 → …」的普通 FIFO，执行器逐个入队

**D2. 空窗守卫（重构成败关键，原稿完全缺失）** — 脑 Tick 空脑分支（[AgentBrain.cs:1526](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L1526)）加意图守卫，零新增状态：

```csharp
if (EffectiveAction == null)
{
    // 计划执行中：不恢复默认行为（空窗守卫）。null-guard 对齐 AgentBrain.cs:73 既有模式
    if (_currentIntent?.Type != NpcIntentType.ExecutingCommand)
        DecideDefaultBehavior();
}
```

执行器 100ms 轮询节奏不变；动作完成的空窗期（≤100ms）NPC 原地等待，玩家无感。

**守卫安全性论证（已验证全量 `SetNpcIntent` 调用点）**：计划活着期间意图内容恒定（`order_execute_plan` 设一次，无二次设置）；所有计划中断路径（ComeHere/order_follow/order_attack/护主/目击质问/击晕/StartCombatAgainst）均为「ClearAllActions + 改意图 + 立即入队新动作」三件套同步——意图失效时刻 = 计划已死 + 队列已占，**不存在「计划活着但守卫失效」窗口**。

**🔴 例外：ReactiveAgent 反应链（`RunReactiveAction`，[AgentBrain.cs:87](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L87)）是唯一「ClearAllActions 不改意图」路径**——执行中随从被搭话 → 计划动作被清 → 计划中止 + 密信报告；收尾 `OnPlanExecutorFinished` 因意图仍 ExecutingCommand 正常复位 None（无残留）。**语义变化（重构引入，明示）**：重构前反应台词与计划步骤并行（两个大脑打架实例），重构后被搭话 = 计划中止。符合「计划前提已破」哲学，行为可预期。

**D3. 行为性内联 = 状态机 + 队列适配器** — 状态机（Phase 枚举）是成熟逻辑，不动；加薄适配器走队列，使中断语义覆盖到它们：

```csharp
/// IInlineStep 需补 Interrupt() 接口（现状只有 Finished/Ok/OnTick）
class InlinePlanAction : IAtomicAction
{
    private readonly IInlineStep _inline;
    public void OnStart(Agent a) { }                                  // 状态机构造时已初始化
    public void OnTick(Agent a, float dt) => _inline.OnTick(dt);
    public bool IsFinished(Agent a) => _inline.Finished || _inline.Interrupted;
    public void OnEnd(Agent a) { }
    public void RequestInterrupt() => _inline.Interrupt();
}
```

**D4. 中断检测 = 三条路径（原稿只写了一条）** — 执行器轮询（100ms，与谓词轮询同节奏）时对当前步骤动作判定：

| 路径 | 判定 | 处理 |
|---|---|---|
| 正常完成 | `action.IsFinished(agent)` | `CompleteStep`（until/when/超时检查照旧）→ 排序下一步 |
| 外部清除 | `!brain.IsActionAlive(action)` 且**未完成**（不在 current 不在队列） | 被 ClearAllActions 清掉 = **计划中止**（graceful，走既有 `@abort_gracefully` 词汇）+ 收尾报告立即发（**玩家在场 → 当面报告**（`needFaceReport`，玩家就在旁边却收密信出戏）；脱不开身 → 密信通道） |
| 主动中断 | 执行器自己 RequestInterrupt（Pause/超时清除） | **暂停或超时路径**，不是中止 |

**判定顺序（v3 明示）**：先 `IsFinished`（正常完成）再 `IsActionAlive`（外部清除）——动作被脑完成后**同样不在队列**，必须先查 IsFinished 才能区分"完成了"与"被清了"。

**D4b. 🔴 生命周期所有权不变式（v3 新增，M2 成败关键）** — 动作一旦入队，**生命周期归脑**（OnStart/OnTick/OnEnd/IsFinished 全部由脑驱动）。executor 侧任何步骤迁移/终止，都必须对自己当前动作 `RequestInterrupt()` + 摘除 cursor 引用，**且不再调 OnEnd**：

| 场景 | 处理 |
|---|---|
| until / on_event 跳转、超时、Pause、R3 叫停、Abort、Finish、end_plan 失败、Tick 异常 | 对当前动作 `RequestInterrupt()`（脑下一帧见 IsFinished 自清出队；中断标记使动作 OnTick 直接结束、**不会真执行**）+ `DetachSubAction()`（只摘引用，不调 OnEnd） |
| 脑侧完成（脑调 OnEnd 出队） | executor 轮询见 IsFinished → `CompleteStep`（只摘引用，**不再 OnEnd**） |

由此 **`ClearSubAction` 拆两态**：`DetachSubAction`（正常迁移：只摘引用，teardown 归脑）+ `TeardownSubAction`（异常兜底：动作从未入队/脑已死时补 OnEnd）。接线清单 M2 必须写明此拆分。

**推导出的三条具体漏洞（不修必踩）**：
- ① 僵尸动作：R3 叫停时脑里跑的是真动作（如跨城 move_to）→ 只摘引用不打断 = **随从继续走完计划路线整段路**（现状占位出队即停 = 立即收手，重构后这是回归）。超时 / Replan 中止 / end_plan 失败 / Tick 异常同理。
- ② 双 OnEnd：脑每帧调 OnEnd（[AgentBrain.cs:1551](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L1551)）+ `ClearSubAction` 也调（[PlanExecutor.cs:1356-1364](ExampleModVS/ExampleMod/ExampleMod/Planner/PlanExecutor.cs#L1356)）→ 同一动作 OnEnd 两次；`MoveToPositionAction.OnEnd` 的 `MoveEndAndInteractPrepare`（[AtomicAction.cs:521-529](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L521)）/ `FightEnemyAction.OnEnd` 的 CombatManager 清理可能双触发。
- ③ 暂停跳步：见 D5 的 v3 修正（Pause 不摘引用 → Resume 误判完成跳步）。

**D5. 暂停/恢复重定义（原稿缺失，v3 补引用摘除）** — R1（玩家战斗）/ R4（玩家走远）/ R7（玩家模态）语义与现状严格等价，只是改走队列：
- `Pause` = 对当前已入队动作 `RequestInterrupt()`（脑下一帧 OnEnd 清出队）+ **摘除 cursor 引用（SubAction 与行为性内联的 `cursor.Inline` 都要摘）** + 排序器冻结调度
- `Resume` = 排序器重新 `TryCreateSubAction` 入队**同一步骤**（步骤重跑——与现状「Pause 清 SubAction、Resume 重创建」等价）；行为性内联因引用已摘 → 重建**全新状态机**（Interrupt 不可逆，复用旧实例 = 立即 IsFinished 死路）
- 非行为性内联（say_to/wait/end_plan）保留状态跨 Pause（恢复后不重播、不重计时，与现状一致）
- 被打断的移动步骤重跑从当前位置重新走，可接受
- 🔴 v3 实锤：Pause 若不摘引用 → Resume 后首轮询见 `IsFinished==true` → 误判"正常完成"→ **跳步**（前进到下一步而非重跑本步）

**D6. 三种战斗三种结局（语义边界写明）**：

| 场景 | 机制 | 计划结局 |
|---|---|---|
| 玩家战斗（随从旁观） | 排序器 R1 → Pause（步骤中断，Resume 重跑） | **暂停**，战斗结束恢复 |
| 随从被攻击/护主参战 | 脑 `event_agent_damaged` → ClearAllActions 清掉计划动作 | **中止** + 密信报告（不恢复——计划前提已破，符合 KCD2 真实感） |
| `IsInteractionDisabled`（全局战斗模式，脑不 tick） | **执行器 `TickInner` 顶部镜像脑门控直接 return（v3 修正：含超时冻结）**——否则 `StepElapsed` 照常累计，bounded step 会被超时中止而非暂停 | **自然暂停**，脑恢复 tick 后继续（门控天然生效 = 特性非 bug） |

## 实施里程碑（原阶段 1 止血删除；原阶段 2 拆四里程碑，每步可编译可验证）

| 里程碑 | 内容 | 验证 |
|---|---|---|
| **M0** 行为性内联适配 | 表现层判定标准定稿；`IInlineStep` 补 `Interrupt()`/`Interrupted`；`InlinePlanAction` 适配器（**未接线**，纯新增） | 编译过，行为不变 |
| **M1** 脑侧接线 | 空窗守卫（D2）；`EnqueuePlanAction`（internal，复用 `EnqueueActionInternal` 语义）+ `IsActionAlive(IAtomicAction)`；删 `ExecutePlanAction`（D1）+ **同步改 [PlanDebugCommands.cs:201](ExampleModVS/ExampleMod/ExampleMod/Debug/PlanDebugCommands.cs#L201) 的 plan_debug 注入**（v3 补漏，漏改 = M1 编译断）；`order_execute_plan` 分支改直接 Start | `custom.plan_debug run` 全动作跑通，跟随/原版 AI 无抢跑 |
| **M2** 执行器侧改造 | `TryCreateSubAction` 产出的 IAtomicAction 不再 OnStart/OnTick，改入队；行为性内联走 `InlinePlanAction`；`TickCursor` 改 100ms 轮询完成检测 + 三路径判定（D4，**IsFinished 先于 IsActionAlive**）；生命周期收口（D4b：所有迁移/终止 RequestInterrupt + 摘引用 + `ClearSubAction` 拆 detach/teardown 两态）；`TickInner` 顶部加 `IsInteractionDisabled` 门控（D6 修正）；Pause/Resume 重定义（D5，含引用摘除） | until/when/超时两侧 + 暂停恢复 + 中断中止 + **R3 叫停无僵尸动作 + 步骤正常完成 OnEnd 无双跑 + 暂停恢复不跳步** |
| **M3** 全链路验证 | 收尾报告链（密信 + 当面恢复跟随）；Replan（≤2 额度）；多 actor（subjects 一带多，每个 actor 步骤进各自脑队列）；击晕/倒地回归 | 回归矩阵全表 |

## 接线清单（参照 wheels.d/planner.md:57 现清单）

| 文件 | 改动 |
|---|---|
| `AI/AgentBrain.cs` | 空窗守卫（D2）；`EnqueuePlanAction` + `IsActionAlive`；`order_execute_plan` 分支去占位（D1） |
| `Planner/PlanExecutor.cs` | `TickCursor` 重构（创建→入队；OnTick 改 100ms 轮询完成检测 + 三路径中断，**IsFinished 先于 IsActionAlive**）；`TryCreateSubAction` 不再 OnStart；**`ClearSubAction` 拆 `DetachSubAction`（正常迁移：只摘引用不 OnEnd）/ `TeardownSubAction`（异常兜底补 OnEnd）两态（D4b）**；所有迁移/终止路径（until / on_event / 超时 / Pause / R3 / Abort / Finish）对当前动作 `RequestInterrupt()`（D4b）；`TickInner` 顶部镜像 `IsInteractionDisabled` 门控（D6 修正）；Pause/Resume 重定义（D5，含引用摘除） |
| `Debug/PlanDebugCommands.cs` | 🔴 v3 补漏：`plan_debug run` 的 `new ExecutePlanAction(executor)`（[:201](ExampleModVS/ExampleMod/ExampleMod/Debug/PlanDebugCommands.cs#L201)）随 D1 删除同步改为直接 `executor.Start` 注入——漏改 = M1 编译断 |
| `Planner/InlineSteps.cs` | `IInlineStep` 补 `Interrupt()`/`Interrupted`；行为性内联（lead/steal_attempt/knockout/emote）标记 |
| `AI/Actions/AtomicAction.cs` | **删除 `ExecutePlanAction`**（原稿写"OnEnd 补语义"——方向错，应删）；新增 `InlinePlanAction` 适配器 |
| `Planner/ReactiveAgent.cs` | 不动（respond 消费链独立；注意其动作类 `ReactiveFollowAction` 等**读** `Agent.IsFollowingNow`/`TargetAgent` 供 following 谓词——合并动作类时（附章）要保此属性） |
| `Interaction/PlanCommandFlow.cs` / `Planner/PlanReplan.cs` | 验证全链路（StopPlan / Replan / 密谋对话壳） |
| `AgentHUD/AgentHudVM.cs` | 🔴 显示互斥一行：`ShowIntentDebug` 加 `&& !ShowPlanSummary`——计划执行中摘要行独占、意图行让位（玩家刚下令已知在执行命令，意图行零新信息，两行叠加 = 反馈冗余）；XML 不动（VM 层互斥，符合 VM↔XML 同步铁律）。重构后 `ExecutingCommand` 意图保留为 D2 空窗守卫内部哨兵，不再上屏——HUD 行为来源 = 执行器 `CurrentSummary`（真实步骤进展），消除「意图行说执行命令、实际在走路」的状态分歧 |

**不动**：计划语法（PlanGrammar/PlanValidator）、世界状态（RuntimeWorldState）、prompt 词表（`test_llm_plan.py` 同步面）、事件通道（`plan_decision` → `on_event`）、`was` 修饰、`when` 门控超时、R4/R6 安全网。

## 风险与回归点

| 回归点 | 验证手段 |
|---|---|
| say_to 对话状态机（SayInlineState） | `custom.plan_debug run` 对话类计划，多轮对话完整走完 |
| until 提前完成 / when 门控 / 超时 | 条件等待计划（wait with until），触发与不触发两侧 |
| 收尾报告（密信 + 当面恢复跟随） | 计划成功/失败/中止三态各跑一次 |
| Replan（≤2 次额度） | 失败触发 replan，额度消耗正确 |
| 计划被战斗打断 | 执行中攻击随从 → 计划中止 + 密信报告，无脚本指令打架 |
| 🔴 空窗竞态 | 步骤切换瞬间无跟随/原版 AI 抢跑（M1 验证点，重点看 move_to→move_to 连续步骤） |
| 🔴 僵尸动作（D4b 回归点） | R3 叫停 / 超时 / end_plan 失败后随从**立即收手**——不继续走计划路线、动作不入队残留；中断标记动作不真执行 |
| 🔴 OnEnd 双跑（D4b 回归点） | 步骤正常完成后动作 OnEnd 只执行一次（`MoveEndAndInteractPrepare` / CombatManager 清理无双触发；可用日志对比重构前 [Brain-Tick] 完成序列） |
| 🔴 暂停跳步（D4b 回归点） | Pause→Resume 后**同一步骤重跑**，不前进到下一步；行为性内联重跑为全新状态机（lead 中途暂停恢复不跳段） |
| 全局战斗超时（D6 修正） | 长时间 `IsInteractionDisabled` 期间计划不被超时中止，结束后恢复原步骤（含不冻结的对照：bounded step 会被中止） |
| 被打晕期间 | 计划暂停/中止，醒来无残留状态（跟随恢复正确）；🔴 验证 `FinalizeExecutor` 的 `ForceUnlockAgent` 不会让击晕者站起来（**既有路径**，重构前排序器 Abort 也会走，非新增风险） |
| 行为性内联（lead/steal/knockout/emote） | 各跑一次：执行中被打断 → 计划中止，无移动指令残留 |
| OnTick 频率变化（100ms→每帧） | 入队动作内部自带节流（`_fixedTimer` 0.2s / 动态重算间隔）——逐个动作验证无行为差异 |

**验收主线**：执行中随从被砍 → 脑战斗动作是唯一驱动者（无计划脚步残留）→ 计划 graceful 中止 → 密信报告到达。

---

# 附章：IAtomicAction 动作类合并评估（解耦重构，可独立落地）

> 状态：**②③ 全部落地**（2026-08-11，随主重构同批）：`MoveToPositionAction` 参数化（`maxTime` 固定超时 / `skipGetupDelay` 起身延迟 / `EndBehavior` 收尾行为 / `narration` 旁白 / `lookTarget` 边走边盯，现有调用点全兼容）→ **删 `FleeFromAction`/`ReactiveFleeAction`/`ReactiveReturnPostAction`/`ReactiveInvestigateAction`/`ReactiveFollowAction` 五个反应链动作**（Follow 场景拆两步入队：`FollowAgentAction` 加 `optionalDuration` 参数 [>0 = 到时完成忽略距离] + `MoveToPositionAction` 折返；`RunReactiveAction` 改 `params` 支持多动作；following 谓词删 ReactiveFollowAction 分支——`FollowAgentAction.TargetAgent` 已覆盖）。**未落地**：① `MoveTargetAction` 基类抽取（移动三兄弟样板下沉——现样板仍留在 MoveToPositionAction 叶子，可后续抽）④ `OneShotAction` 基类（DrawWeapon/PlayAnim）⑤ following 谓词回归（跟走/折返两阶段谓词已静态核对，待实机）。
> 评审结论：**动作类数量多 ≠ 冗余**——每个叶子的灵魂是**完成判定（IsFinished）**：移动类判"到点/跟丢/超时"、姿态类判"精度/时长/无限"、战斗判"目标倒下"。参数化只能覆盖"怎么动"，覆盖不了"何时算完"，揉成一个全参数大动作类 = 判定 switch 爆炸 + 实例携带大量无用参数（参数化反模式）。**真正的冗余在别处**：①移动族三兄弟的驱动样板重复（起身延迟/刷新节流/清理/卡死兜底）②反应链四个专用动作是标准原语的组合/参数化。按这两个方向合并，19 类 → **13 叶子 + 2 基类**。
> 🔴 2026-08-11 落地修订：经实践检验，①"样板下沉到基类"与②"同类参数化"的边界是**行为语义族**——flee 三兄弟（FleeFrom/ReactiveFlee/ReturnPost）与 MoveToPosition 同属"走到点"，差异（起身延迟/超时/收尾）全部参数可覆盖，故直接并入 MoveToPositionAction 而非抽基类；真正需要基类的是完成判定不同的移动族（到点 vs 距离状态机 vs 逃跑放弃）。

## 合并矩阵（19 类现状）

**人话总览（合并方向一句话）**：动作类**不合并**（完成判定各不同，揉一起 = 判定 switch 爆炸）；合并的是**复制粘贴的样板**和**套壳组合**：
- ① 移动三兄弟（走到点 / 跟随 / 逃跑）的 OnStart/OnTick/OnEnd 骨架约 50 行/类重复 → 抽 `MoveTargetAction` 基类，样板一份；
- ② 反应链四个专用动作（flee / return_post / investigate / follow）其实 = 标准动作加参数或两步入队 → **删**，直接用标准动作；
- ③ 战斗 / 姿势 / 台词等真正独立的原语 → 保留不动。

| 动作 | 维度 | 完成判定 | 处置 |
|---|---|---|---|
| `MoveToPositionAction` | 移动 | 到 stopDistance / 卡死瞬移兜底 | **基类** `MoveTargetAction`（固定 0.2s 刷新等样板下沉） |
| `FollowAgentAction` | 移动 | 距离状态机 / keepFollow 无限 | 基类 + **加 `optionalDuration` 参数**（≤0 = 无限；>0 = 到时 IsFinished）；🔴 **动态节流（`ComputeRepathInterval` 双因子 + Settings clamp）留在叶子**——差异化行为，不下沉 |
| `FleeFromAction` | 移动 | 到点 / 超时 | 基类 + **加目标源参数**（按威胁算点 or 指定点）+ `isRun` |
| `ReactiveFleeAction` | 移动 | 到点 / 超时 | 🔴 **删** → `FleeFromAction(指定点, isRun:true)`——恐慌逃跑 ≈ 原版逃跑类只差三个开关（目标：自算 vs 外部给点；姿势：walk vs run；超时 10s vs 15s），骨架完全同构（锁定→0.2s 刷移动→到点/超时结束→解锁，[AtomicAction.cs:1557](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L1557) vs [:535](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L535)）——删掉后逃跑逻辑只剩一处，改逃跑节奏只改一个类 |
| `ReactiveReturnPostAction` | 移动 | 到岗点 | 🔴 **删** → `MoveToPositionAction(岗点)` |
| `ReactiveInvestigateAction` | 移动 | 到点 / 30s | 🔴 **删** → `MoveToPosition` + `LookAt` 两步入队 |
| `ReactiveFollowAction` | 移动 | Follow→Return 状态机 | 🔴 **删** → `FollowAgentAction(duration)` + `MoveToPosition(岗点)` 两步入队（目标没了 → Follow IsFinished → 自然推进折返，语义等价；⚠️ following 谓词读 `IsFollowingNow`/`TargetAgent`，[wheels.d/planner.md](plans/rules/wheels.d/planner.md#L46) `following` 谓词——合并要保属性：给 `FollowAgentAction` 加同构属性） |
| `TurnToDirectionAction` | 姿态 | 点积精度 / 3s 超时 | 保留（姿态原语） |
| `LookAtAction` | 姿态 | duration | 保留（姿态原语） |
| `StayAction` | 姿态 | 永不（中断/击晕） | 保留——`IsKnockout` 被 `brain.IsKnockedOut`/`IsCurrentOrPending<StayAction>` 模式匹配，合并破坏既有判型 |
| `FightEnemyAction` | 战斗 | 目标倒下/离场 | 保留（唯一战斗原语：CombatManager 生命周期 + 残血认输 + 目标纠偏 + 胜负记忆） |
| `DrawWeaponAction` | 一次性 | 固定 2s | **基类** `OneShotAction`（OnStart 执行指令 + 计时完成） |
| `PlayAnimAction` | 一次性 | 动画播完 / 超时 | 基类（完成判定子类化） |
| `ReactiveSayAction` | 台词 | 计时 | 保留（冒泡台词，质不同） |
| `PrepareOpeningAction` / `ForceTalkAction` / `AlertForceConversationAction` | 对话点火 | 异步/等待/持有 | 保留（机制异构，合并牵强） |
| `ReactionDecisionAction` | 逻辑 | 延迟回调 | 保留（通用工具，样板小） |
| `ExecutePlanAction` | 计划 | — | 🔴 主重构 D1 已删 |

## 关键理由

1. **完成判定是行为的本质差异**——`MoveToPosition`（到点）vs `Follow`（距离状态机）vs `Flee`（一次性方向）三个完成判定完全不同，一个类三套判定 = 三个类塞一个壳。
2. **样板重复是真冗余，但要点名边界**：三个移动动作的 OnStart（起身延迟 + MovePrepare）/OnTick（固定 0.2s 刷新节流）/OnEnd（MoveEndAndInteractPrepare/ForceUnlockAgent）约 50 行/类重复 → `MoveTargetAction` 基类一份。**例外：Follow 的动态重算间隔（`ComputeRepathInterval`：目标平均速度 + 欧氏距离双因子，clamp `FollowRepathMin/Max`）是差异化节流策略，必须留在叶子**——节流策略 = 叶子可重写的虚方法，这本身就是「基类 + 叶子」结构的意义。
3. **反应链四个专用类是「策略产物」不是「原语」**——ReactiveAgent 的 `ExecuteReaction`（[ReactiveAgent.cs:375](ExampleModVS/ExampleMod/ExampleMod/Planner/ReactiveAgent.cs#L375)）直接 `RunReactiveAction(new MoveToPositionAction(...))` 更诚实：反应动作 = 标准原语的组合。
4. **prompt 词表零影响**：`return_post`/`follow_for_a_bit` 等是字符串词表（`ReactionActionsInPromptOrder`），映射到 `ExecuteReaction` 分支，分支内部换动作类即可，词表/脚本/回归测试**全不动**。
5. **StayAction 不合并**：`IsKnockout` 是击晕判型（`brain.IsKnockedOut` 优先查 `IsStunned`，[AgentBrain.cs:139](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L139)），合并进 LookAt 会破坏 `IsCurrentOrPending<StayAction>` 全链。

## 落地建议

- **与主重构解耦**：动作类合并不碰队列/执行器机制，可先做或后做，两边无依赖（唯一交点 = `ExecutePlanAction` 删除已归入主重构 D1——**含 [PlanDebugCommands.cs:201](ExampleModVS/ExampleMod/ExampleMod/Debug/PlanDebugCommands.cs#L201) 的引用点**，随 M1 同步改）。
- **步骤**：①`MoveTargetAction` 基类抽取（纯重构，行为零变化，逐动作回归）→ ②FleeFrom 参数化 + 删 `ReactiveFleeAction`（恐慌逃跑回归：到点/超时/跑姿与现状一致）→ ③Follow 加 `optionalDuration` + 删 `ReactiveFollowAction`/`ReactiveReturnPostAction`/`ReactiveInvestigateAction`（改 `ExecuteReaction` 6 分支）→ ④`OneShotAction` 基类（DrawWeapon/PlayAnim）→ ⑤following 谓词回归（`IsFollowingNow`/`TargetAgent` 属性迁移）。
- **风险**：`MoveToPositionAction.OnEnd` 调 `MoveEndAndInteractPrepare`——回岗（原 ReactiveReturnPost 路径）可能触发附近互动准备，验证无副作用；`FleeFromAction` 儿童 walk 与 ReactiveFlee run 参数化后互不影响。

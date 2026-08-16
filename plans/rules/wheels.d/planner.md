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

**34 行结构**：前 21 行计划原序（82% LLM 回归基线，静态构造 `Debug.Assert` 钉死）+ 后 13 行闲聊-only。交集 14 行合并（如 `order_attack` 行 Aliases["attack"] 承载旧码、duel 双语义一行承载）。（2026-08-14 实测为 **36 行**：后 15 行闲聊-only，含 crouch/stand 瞬时姿态动作。）

**派生面清单**（全部只读主表）：`PlanVocab.ActionsInPromptOrder/Actions/ActionAliases/AllowedResultKeys/TerminalActions`（PlanGrammar.cs）、`ActionHandler` 五入口（HandleAction/HandleImAction/GetActionSpacePrompt/AnnounceDecision/PostActionProposal + RunActionCore 第三处查找）、`PlanActionLabel`（ImCommandFlow.cs，FindByLabelCode 先 ByCode 回落别名）、`ChatActionFlow.TryExecute`（FindByCode?.FillParams）、`Scripts/check_vocab_sync.py`（按大括号深度配对提取 ActionSpec 块）。

**关键纪律**：
- **单码统一**：Code 全小写；旧存档大写码（`ImMessage.ActionCode="MOVE_TO"`）由 `FindByCode` 的 **OrdinalIgnoreCase** 兼容（漏 = 旧决策卡片批准后执行失败）
- "NONE" 哨兵大写保留（三处跳过判据：HandleImAction 提前返回 / GetActionSpacePrompt 跳过 / AnnounceDecision 跳过）
- **执行职责边界**：主表只注册 + 闲聊入口接线；agent 动作行为语义归 PlanExecutor（`Execute` = 包装单步 Plan 走 `ChatActionFlow.TryExecute` → 既有 TryCreateSubAction 分支）；hero/party 动作 `Execute` 才是行为实现。RequiresConfirm 的 `ExecuteCore` = 卡片批准后直接执行（不再二次确认）
- **双身份**：`order_attack` 主表动作码 ≠ AIEvent 事件名（AgentBrain.cs:387 白名单协议）——Brain 层协议不注册，行注释标明桥接
- 新动作若需新行为 = 主表一行 + AtomicAction/InlineState 新类 + TryCreateSubAction case；仅需已有行为 = 主表一行（Execute 委托指向既有通道）
- 静态构造自检五连（🔴 失败只写日志不弹窗——Debug.Assert 实机 Debug 构建弹断言框崩游戏，2026-08-13 崩溃实录；ExecutorImplemented 字段默认值必须 `= true`，bool 默认 false 会让 34 行全判"未实现"）：计划 21 码字面量序 / ChatOrder 1..27 连续 / ExecutorImplemented=false=={shadow,negotiate,duel} / 别名无重复 / IsValid+闲聊空间行 Execute 非空

**新增动作流程**（详见上方接入点第 1 条）：主表一行（Code/Description/标签/空间/布尔位/ChatOrder/IsValid/Execute）→ 执行语义落点 → py 词表加行 → 跑 `check_vocab_sync.py`。

## 免确认瞬时动作（RequiresConfirm=false 白名单）— `Planner/ActionRegistry.cs` + `Planner/InlineSteps.cs` — 2026-08-14

**解决什么问题**：玩家给随从下瞬时、可逆、零风险命令（蹲下/站起/手势/喊叫），不该走「提议卡片 → 玩家批准」重流程。34 行主表里 `RequiresConfirm=true` 只有 **4 个高风险动作**（order_attack / knockout / steal_attempt / duel——会进战斗或动钱包）；其余全默认 false = IM 下达**立即执行**（`HandleImAction` 拦截条件 `RequiresConfirm && !bypassConfirm` 不命中 → 直接 `HandleAction` → `ChatActionFlow.TryExecute` 单步计划，"无需批准"）。

**新增免确认动作三步走**（crouch/stand 为范本）：
1. 主表加行：`InChatSpace=true, ChatOrder=下一连续号, Spaces=InScene, RequiresConfirm` 留默认 false，`IsValid=(npc,player,agent)=>agent!=null`，`Execute = (…)=>ChatActionFlow.TryExecute(agent, "crouch", null, null, null)`；**注意静态自检 `ChatOrder 1..N` 连续序列要同步 +N**
2. `Planner/InlineSteps.cs` 新 `XxxInlineState : IInlineStep`（瞬时动作 = 构造即执行 + OnTick 立即 Finished；降级语义照 EmoteInlineState：失败 Ok 保留 + Finished，装饰性动作不改世界状态）
3. `PlanExecutor.TryCreateSubAction` switch 加 case → `cursor.SubAction = new InlinePlanAction(cursor.Inline)`（行为性内联经脑入队，与 emote/lead 同）

**行为性 vs 非行为性**：驱动表现层（姿态/移动/动画）→ `IsBehavioral=true` 经脑入队（生命周期归脑，中断语义覆盖）；纯逻辑/通信（喊叫/传话）→ `false` 留排序器侧。

🔴 **两个连带坑（2026-08-14 实机：crouch 被拦「未知动作」→ 随从没蹲）**：
1. **计划校验词表源**：`PlanGrammar.ValidateStep` 动作合法性原查 `PlanVocab.Actions`（= 计划词表 21 码）——闲聊-only 动作此前从未走计划管线（ChatActionFlow 单动作包裹），crouch/stand 是第一个踩雷的，校验期被当未知动作丢弃。**已改查 `ActionRegistry.FindByCode`（单一事实源）**，仅保留 `ExecutorImplemented=false` 特判（shadow/negotiate/duel 仍丢弃）。
2. **SelfTargeted 无目标语义**：瞬时自身状态切换（蹲下/站起）不应解析 defender——defender=null 会让 `HandleAction` 的 `ResolveSpace` 误判 Remote 再拦一次。三处联动：`ActionSpec.SelfTargeted=true` → `HandleImAction` 跳过 defender 解析（heroHit=true 防模板路径）→ `HandleAction` 空间特判 `SelfTargeted ? InScene` → `AnnounceDecision` 不拼目标（播报「决定：蹲下」而非「蹲下（目标：努勒丹）」）。新增此类动作照抄 crouch/stand 行。

## 🔴 扒窃绕背走位（人变体 Behind 阶段死等修复）— `Planner/InlineSteps.cs` StealAttemptInlineState — 2026-08-14

**实机 bug**：随从扒窃走到目标**侧面就原地不动**（日志：8.0s 整"完成"且无任何偷窃痕迹）。根因：`ApproachAgent` 目标点 = `target.Position`（直撞碰撞体停侧面）+ Behind 阶段 `dist≤2.5m 且不在背后` 时**不派发任何移动指令**（死等 8s 超时 → impossible → 单动作包裹无 result 路由 → 静默成功）。

**修复**（绕背定位，设计文档原意落地）：
```csharp
// 未在背后（任何距离）→ 目标点 = 目标正后方 ~2.2m（必须 ≤ 2.5m 判定圈内，每帧重算跟随转身）
Vec3 back = target.Position - new Vec3(target.LookDirection.X, target.LookDirection.Y, 0f) * 2.2f;
AgentControlHelper.ScriptedMoveToPoint(_agent, back, dist > 5f);   // 跑/走切换沿用通用接近语义
// behind && dist ≤ 2.5 → Rolling；8s 超时保留为诚实兜底（绕不到背后 = impossible）
```
**判据**：`behind = dot(target.LookDirection, toSelf) < -0.4`（目标面朝 113° 外）。击晕无背后要求（≤1.8m 直接挥击）——只有扒窃有。

## 🔴 检定成功率公式（d20 风格全局统一）— `Planner/InlineSteps.cs` 击晕/偷窃 + `Interaction/InteractionMissionView.cs` — 2026-08-13

**判定方向**（铁律 17，用户裁定）：`success = roll >= threshold`，`threshold = 1 − 成功率`——掷点越大越容易成功。播报只显示「掷点 {ROLL} vs 门槛 {THRESHOLD}」。

**成功率公式**（随从路径，ratio 式，对齐玩家路径 `ComputeKnockoutChance`）：
```csharp
// 模板 NPC 属性按 Level 均分估算：(3 + Level/3) / 2（农民 ≈4+4、女农民更低；禁止硬编码 10+10）
// ratio 式：0.5 × (己方 Vigor+Control ÷ 目标 Vigor+Control)，钳制 [5%, 85%]（随从）/ [5%, 95%]（玩家）
```
**踩坑**（实机两次连败）：旧公式 `0.25 + (selfSum − tSum)×0.03` 对模板 NPC 恒劣——模板默认 20 vs Hero 属性上限合计 20 → 成功率 ≤25%，低属性直接钳到 5% 保底（偷袭农民「门槛 95%」）。ratio 式后：农民 ≈60%、女农民 ≈85%（上限）、资深步兵 ≈30%，门槛数字 = 目标强度直观刻度（越弱门槛越小）。

## 🔴 击晕单管线（玩家/NPC 平权范本，铁律 18）— `Combat/KnockoutFlow.cs` + `Core/AgentStatsHelper.cs` — 2026-08-13

**解决**：玩家 `TryKnockoutAgent` 与 NPC `KnockoutInlineState` 曾各写一份判定公式 + 属性估算 + 结算（"同口径"注释 = 复制，改公式要改两遍）。现在**判定 + 结算全共享**，壳层只留动画节奏与播报。

**关键签名**：
```csharp
public static class KnockoutFlow
{
    public sealed class RollResult { public bool Success; public float SuccessRate; public float Roll; public float Threshold; public bool IsChild; }
    public static float ComputeSuccessRate(Agent attacker, Agent target, float maxRate = 0.85f);  // 纯计算（UI 预览用，不掷点）
    public static RollResult Roll(Agent attacker, Agent target, float maxRate = 0.85f);            // 判定（MBRandom，儿童 100% 免疫）
    public static void PlayStrikeAnim(Agent attacker, Agent target);  // 挥击：Main→SetPose / NPC→ForcePlayAction（内部判断 IsMainAgent）
    public static void Resolve(Agent attacker, Agent target, RollResult r);  // 记账→击晕落地/反击→目击广播
}
public static class AgentStatsHelper
{
    public static (int vigor, int control) GetAgentStats(Agent agent);  // Hero 读属性 / 模板 (3+Level/3)/2
}
```

**调用范式**（玩家 async 壳 vs NPC 状态机壳）：
```csharp
// 玩家：Roll（判定先行）→ PlayStrikeAnim → await 400ms → Resolve → 播报
// NPC（KnockoutInlineState）：_roll = KnockoutFlow.Roll(...); PlayStrikeAnim → _timer 0.5s → Resolve → ReportResult
```

**必要差异化（参数化，不复制逻辑）**：① maxRate 玩家 0.95 / NPC 0.85 ② 挥击动画 Main→SetPose（避 async AI tick 竞态）/ NPC→ForcePlayAction（村民 action set 战斗动作不可达，SetPose 静默失败）③ 起手延迟 400ms / 0.5s ④ 播报文案第一人称 vs 第三人称（留壳）。**执行模型**：玩家 async+Task.Delay / NPC 脑驱动 OnTick 定时——共享层 = 判定+结算纯函数，节奏留壳。

**顺带修复**：NPC 击晕儿童免疫（原可击晕儿童）、已晕目标不再误发反击事件、随机源统一 MBRandom（原 NPC 用 Random）。**新增 NPC 动作范本**：按此结构抽共享管线（判定+结算进管线，壳留节奏与播报），对齐铁律 18。

## 执行期目标解析（快照匹配口径）— `Planner/SceneSnapshot.cs` FindAgent

`TryResolveAgent` 解析链：self/player 特判 → `RoleAgents`（explicitTarget 注册的 "target"）→ 快照 `FindAgent`。快照匹配五层：① Role 精确 ② 显示名精确 ③ StringId/Character.Name 精确 ④ 职业关键词子串 ⑤ **显示名子串**（2026-08-13 加——与 defender 解析 `NameMatchesHero` 同口径；"那弥斯" ⊂ "卡诺洛斯的那弥斯"，多匹配取最近）。**纪律**：卡片阶段能解析的目标，执行期必须同口径解析——否则"卡片发出、执行瞬死"（实机 44.510→44.512）。

## 🔴 Agent.Index 目标唯一标记（`[#N]`，2026-08-15 用户裁定）— `Core/AgentControlHelper.cs` + `SceneSnapshot.FindAgent` + `ActionHandler.FindTemplateNpcCandidates`

**问题**：玩家/LLM 说法与场景角色名不一致（「酒馆老板」vs「酒馆店主」）→ 字符串匹配 0 候选 → 告知「没找到」+ 决策卡被拦（实机 08:40）。字符串归一化是死办法（覆盖不了所有别名）。

**方案**：prompt 给**每个场景个体标 `[#N]`**（N = 引擎 `Agent.Index`，Mission 内稳定），LLM 基于**场景语义**指认（它知道「酒馆老板」= 场景里标着 [#3] 的「酒馆店主」），action_target 照抄标记 → C# 用 Index 精确查 Agent。

**关键签名**：
```csharp
// Core/AgentControlHelper.cs —— 统一解析工具（剥 #N → Agent.Index 精确查；Mission null 直接未命中 = 非 InScene 无意义；失效回退 cleanName）
public static bool TryResolveIndexedTarget(string text, out Agent agent, out string cleanName)
```

**解析链接入**（三处同口径）：
- `SceneSnapshot.FindAgent`（#N 优先 → 失效回退名字）→ 执行器 `TryResolveAgent` **自动受益**
- `ActionHandler.FindTemplateNpcCandidates`（#N 命中单候选直接返回）
- `WorldFactProvider.BuildRiskSceneContext` 目标解析（输入是玩家命令文本，无 #N，天然不需要）

**prompt 标注**：`【目之所及】/【场景当前人员】`个体行 `名字[#N]`；im_reply_rule 目标名纪律要求 LLM **照抄 #N**（「不在你眼前的人没有标记，只能用名字」——InScene 边界）。**plan_needed 目标传递**：回复轮 action_target（含 #N）存消息 `ResolvedTargetText` → 点按钮 → RequestCommand → 计划轮【目标指认】段（「玩家说的目标 = 场景里的 酒馆店主#3，target 直接写它」）——计划轮不再二次解析玩家原话。

**别名归一化兜底**：`SceneSnapshot.NormalizeTargetAlias`（老板/掌柜→店主、卫兵→守卫、tavernkeeper/innkeeper→店主）保留作 #N 失效后的第三层兜底。

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

## 🔴 执行摘要本地化 + 意图行合并（2026-08-13）— `Planner/PlanExecutor.cs` + `AgentHUD/AgentHudVM.cs`

- **PauseReason 是状态标识符**（Resume 匹配用），不可换本地化文本——改 key 常量 `PauseReasonModal/Fight/Far`（`player_modal/player_fight/player_far`），`Pause()` 内 switch 映射本地化后写 `CurrentSummary`（未知 reason 原样兜底）。
- 执行器全部玩家可见文本 LWNTextHelper：`LWN_plan_step_decision` / `goal_done` / `chaseback` / `done` / `cancel_player` / `step_talk` / `abort_down`（CompanionDown）/ `goal_notmet`（GoalNotMet）。
- **HUD 单行合并**：`AgentHudVM.UpdateLogic` 计划执行中（`executor.CurrentSummary` 非空）→ `NpcIntentDebugText = 执行计划中：{STEP}`；否则 → 意图文本。一行一开关 `ShowNpcIntent`（行为变化：计划行并入后受此开关门控）。XML 青蓝行 `FontSize=14`。

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

---

## 🔴 respond 链路多对象认知注入（2026-08-16 方案 H + I + S2）— `Planner/ReactiveAgent.cs` + `Planner/DialogueComponent.cs`

**解决什么问题**：当面对话/附近喊话（respond 链路）**完全没有 RAG 事实注入**（问战争/物价/位置答不了）——IM 侧有，respond 侧零。方案 H 把认知注入按**对话对象身份分级**（L1 同行全量 / L2 同场景普世+场景锚点 / L3 邻军互见（函数预留）/ L4 遥距现状）。

**关键实现**：
- `DialogueComponent.GenerateLine` 新可选参数：`worldFacts`（RAG 事实段）/ `sceneAnchor`（场景锚点）/ `distressSection`（S2 受困处境）——插在【对方】段后、记忆段前；既有调用点零改动
- `ReactiveAgent.StartRespond` 主线程构建：
  - `WorldFactProvider.BuildFactsForIm(companionLine, responderIsPartyMember)` — **同一个函数按对象身份传参**（随从被当面对话 → L1 全量；路人 → 普世裁剪；模板 NPC 无 Hero 记忆 → 只注入普世 RAG + 场景采样）
  - `BuildCurrentStatusLine`（I1 触发式现状行，历史提及检测）+ `BuildPlayerRelationSection`/`BuildPartyRelationSection`（L1 常态段）
  - 场景锚点：Hero → `BuildSceneAwareness(heroId)`；模板 NPC → `BuildSceneAwarenessForAgent(agent)`（无 Hero 入口）
  - 身份互认增强 `BuildOtherIdentityDesc(requester, self, other)` — "主公是咱们队伍的首领" vs "你是瓦兰迪亚的兵" vs "敌国的人"（C# 确定性阵营判定）
  - S2 受困处境：`DistressFlow.IsInDistress()` + 对象非队伍成员 → `BuildDistressSection(agent)`（看守的认知里玩家是囚犯 + 欠的账）
- **口嗨检测接入**：respond 台词也过 `ChatClaimChecker.CheckAndMark(result, dline.ActionCode, …)`（当面对话同样声称 vs 决策比对）

## 🔴 随从自身经历写入方（2026-08-16 方案 L）— `Memory/SingNpcMemorySystem.cs` + `Core/PlayerMissionEventLogic.cs` + `Core/PartySplitFlow.cs`

**解决什么问题**：`RecordNarration` 通道存在（进【近期经历】段）但写入方只有 AgentBrain 的"被攻击/目击/奉命"——全是**被动承受**，随从像摄像头不像人。L 只补写入方（通道/注入/存档全复用）：
- **L1 战斗表现旁白**：`PlayerMissionEventLogic`（Mission 期间累计击杀数（`OnAgentRemoved` + `Agent.KillCount` 引擎原生）+ 负伤（血 <0.5））→ `MyBehavior.WriteBattleNarration(won)` 消费（battle_win/lose 挂载点）：「我随主公在 {place} 打了一仗，砍翻了 N 个敌人，我负了伤」——第一人称只写本人记忆，不广播（与 D 的玩家视角广播双通道互补）
- **L2 分兵见闻**：`PartySplitFlow.Execute`/`MergeBack` 写「我领了一队人马离了主队，跟着主公走」/「我带着队伍回来了」——归队后【近期经历】自然带出，玩家问「这趟怎么样」能答
- **L3 差事所见**：`move_to(Party)` 执行写「我正带队前往 {X}，在那边等主公」；`engage` 写「我带队去追击 {X}」（ActionRegistry Execute 内）

## 🔴 Party 动作 IsValid/Execute 的 attacker 侧 dual-check（2026-08-16 归队/巡逻修复）— `Planner/ActionRegistry.cs`

**解决什么问题**：IM 闲聊动作的 attacker = 说话 NPC、defender = 解析目标（私聊/群聊语境下恒为 `Hero.MainHero`）。旧 `gather_to_player`/`party_patrol` 的 `IsValid` 只查 defender（`defender != Hero.MainHero && …独立 party`）→ **defender==MainHero 恒 false → 动作永远不进动作空间**——分兵随从想归队只能选 move_to 填玩家名 → 定居点解析失败 → 静默降级 NONE（实机 18:06/18:07 两连降级）。

**关键模式**（范本：`move_to` 的 dual-check，2026-08-16 修复时给 gather_to_player/party_patrol 补上）：
```csharp
// IsValid：attacker 侧（分兵随从自己带队）OR defender 侧（命令他人部队）
IsValid = (attacker, defender, agent) => PartySplitFlow.IsSplitPartyLeader(attacker)
    || (defender != null && defender != Hero.MainHero && defender.Clan == Clan.PlayerClan
        && defender.PartyBelongedTo != null && defender.PartyBelongedTo != MobileParty.MainParty),
// Execute：attacker 分支**先于** defender 资格检查——分兵随从说话 → 直接动自己的 party
Execute = (attacker, defender, agent, l, t, s) =>
{
    if (PartySplitFlow.IsSplitPartyLeader(attacker)) { PartySplitFlow.MergeBack(attacker); return; }  // gather
    if (PartySplitFlow.IsSplitPartyLeader(attacker)) { V.PatrolAround(attacker.PartyBelongedTo, …); return; }  // patrol
    …
}
```

**配套兜底**：`move_to` 大地图分支——定居点解析失败但 `defender == Hero.MainHero`（LLM 把"归队"目标填成玩家名）→ `V.GatherToPlayer(p)` 跟随语义（**不是** MergeBack——拆散 vs 跟随语义分离，防"跟着我"误拆散部队）；真正合并走 gather_to_player。

**判定链**：动作空间注入（`GetActionSpacePrompt` 第 91 行 `(action.Spaces & ActionSpace.Party) != 0 && !action.IsValid(attacker, defender, agent)`）与执行（`HandleImAction` → `HandleAction`）用同一 IsValid 约定。

**DebugLogger 前缀**：`[ActionHandler]`（目标解析/资格降级）/ `[Party]`（分兵/归队/巡逻执行结果）。

**教训**：新增 Party 空间动作时**两条腿都要查**——动作空间可见性（IsValid 进 prompt）和执行路径（Execute 资格）各查一次 attacker/defender；IM 语境 defender 恒为玩家，凡动作执行者是"队伍成员自己"的一律补 attacker 侧。

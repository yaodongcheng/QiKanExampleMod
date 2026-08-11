# IM 计划交互升级 + 闲聊行动系统 — 设计方案

> **状态**：✅ **v1 + Phase F 全部已实施（2026-08-10~11，待实机验证）**：Phase A（入口 IM 化）/ C1（闲聊单条指令执行）/ B（三态+陈述）/ C2（闲聊剩余）/ D（群聊提议）/ E（打磨）/ **F-1 附近频道（§5.7）** / **F-2 DialogueComponent 对话流统一（§5.6）** 全部编译通过 + 词表/本地化回归通过。
> **F-2 实施边界（用户裁定 2026-08-10~11）**：统一「说话」（台词+动作+记忆+播放出口），不统一「行为决策回流」（plan_decision 走 AIEvent）。**对话体系架构收敛（2026-08-11）**：DialogueSession（统一对话实例）+ IDialogueSlot 三钩子真实契约（OnStart/OnTick/OnEnd）+ SayToSlot（ChatPhase 行为等价平移，SayInlineState 薄壳）+ SocialSlot（威胁/NPC 闲聊跟进——attacker 跟进缺已补）+ 续话器（RegisterSession/EndSession/TickContinuations）；PlayerLedSlot 已删除（无生命周期不需要插槽）。respond JSON 化带动作 + 模板降级不写记忆（§5.6 纪律）。
> **主题**：① 行动密令入口 IM 化（Q1）② 计划批准/中断/修改三态（Q2）③ NPC 计划陈述 + 应急边界（Q3）+ 思考中进度条（3.3）④ 群聊兼容（Q4）⑤ 闲聊行动系统（Q5）⑥ action defender 双向化（Q6）+ **空间感知动作空间（5.2）/ 原子动作库平移（5.3）/ FollowAgentAction 动态重算间隔（5.4）/ 对抗反应链（5.5）/ DialogueComponent 对话流统一（5.6）/ 附近频道（5.7）**~~计划审查员（3.4）~~（2026-08-10 裁定暂不实现，见 §3.4）
> **关联**：[im-chat-system.md](im-chat-system.md)（IM 主文档 §五命令模式/§十三群聊）、[llm-goap-plan-execution.md](llm-goap-plan-execution.md)（计划/Replan/执行器）、[npc-live-dialogue-memory-plan.md](npc-live-dialogue-memory-plan.md)（对话 npc_action 上下文）、[rules/wheels.d/im.md](rules/wheels.d/im.md) + [rules/wheels.d/planner.md](rules/wheels.d/planner.md)

---

## 背景与现状（查证结论）

| 现状 | 位置 | 说明 |
|------|------|------|
| 行动密令 = Plot 玩法行（G 长按）→ `PlanCommandFlow.Start(companion)` → 冒泡开场 → **`ShowTextInquiry` 系统输入框** → 澄清轮（冒泡问句 + 输入框 ≤2 轮）→ **`ShowInquiry` 批准弹窗** → `SendEventToAgent("order_execute_plan")` | `Interaction/PlanCommandFlow.cs` | 三条通道全是 vanilla 系统弹窗，与 IM 双轨并存 |
| IM 密令模式 = 密令模式发文本 → `ImCommandFlow.RequestCommand(conv, command)` → LLM 计划 → **PlanCard 消息（同意/拒绝/中止按钮）** → 执行 → 回报回 IM | `ImChat/ImCommandFlow.cs` | 已有「玩家下命令 → 卡片 → 三态」管线，缺「修改」 |
| 说话带 action = LLM 回复 JSON 的 `npc_action` 字段 → `ActionHandler.HandleAction(code, npc, player, agent)` | `Interaction/InteractionController.cs:28`（ActionHandler）、`LLMService.cs`（LLMResponse_Casual） | 动作：NONE/INCREASE_RELATION(+5)/DECREASE_RELATION(-5)/ATTACK/DUEL/MARRY_SUCCESS/JOIN_CLAN。**单向 npc→player、无 defender、数值硬编码 ±5、IM 未接入** |
| 好感官方 API | 反编译确认 | `ChangeRelationAction.ApplyPlayerRelation(hero, delta)`（玩家↔NPC）；**`ApplyRelationChangeBetweenHeroes(hero, gainedRelationWith, delta)`（NPC↔NPC，官方存在）** |
| 修改计划轮子已存在 | `Planner/PlanReplan.cs` | `PlanReplan.Wire(executor, originalCommand, intentType)`，成功产出新计划才消耗额度（≤2） |

**Q7 裁定：开新 plan（本文档），不续 im-chat-system.md**。理由：① im-chat-system.md 状态行是「已实施/待实机」的现状文档（实施总览表对应已实现功能），本次是新功能设计，混入会破坏现状语义；② 体量 ≈ 三子系统 + 实施 + 验证，独立成文；③ 项目惯例：每个大功能一个 plan（im-chat / llm-goap / memory 各自独立），关联旧文档。

---

## 一、行动密令入口 IM 化（Q1）

**目标**：G 长按 Plot 交互后，不再弹 vanilla `ShowTextInquiry`/`ShowInquiry`，直接呼出 IM 面板并定位到该随从的私聊会话、自动切「计划」模式。

**流程（终版）**：

```
G 长按 Plot 行（available 条件不变：随从关系 + PlotEnabled + IsLLMConfigured 总闸）
→ PlanCommandFlow.Start(companion)
→ 随从冒泡开场保留（"Quiet... tell me what you need."，仪式感，KCD2 式）
→ ImChatView.Open(direct_{heroStringId} 会话, mode=Command)   ← 🔴 替换 ShowTextInquiry
→ 玩家在 IM 输入框下达命令 → ImCommandFlow 现有管线（计划卡片/执行/回报）全复用
```

**改动点**：
1. **`PlanCommandFlow.Start` 改造为「IM 入口」**：保留 `_isActive` 互斥语义（Talk 行互斥移除、PlanCommandFlow.Tick 消费），删掉 `PromptForCommand()`（ShowTextInquiry）、澄清轮输入框、批准 `ShowInquiry` 三条 vanilla 通道。命令输入 → 转发 `ImCommandFlow.RequestCommand(conv, command)`。
2. **`ImChatView.Open` 加参数**：现有签名 `Open(ImConversation selectConv = null)` → 增加 `mode` 参数（`Open(conv, mode=null)`：null=保持当前模式，Command=切计划模式）。直接私聊会话不存在 → `TouchDirectChat` 建立（运行时索引已有此函数）。
3. **澄清轮 IM 化**：PlanResponse 的 `questions`（现 ≤2 轮冒泡问句 + 输入框）→ 改成 IM 消息流：NPC 问句 = 一条 NPC 消息（ImReplyService 或直接写消息），玩家回复 = 普通发消息 → `ImCommandFlow` 把回复并入命令上下文继续生成计划（替代 `_awaitingClarifyAnswer` 机制）。
4. **门控**：G 长按本身非战斗才可触发 + IM 打开已有战斗门控（`Settings.IsInteractionDisabled()`），天然兼容；`EnableInputBlock` 纯输入屏蔽层在 IM 打开后可退役（IM 模态层已有拦截）。
5. **退役清单**：`PromptForCommand`、`_history` 输入框路径、澄清轮状态机（`_clarifyRound/_awaitingClarifyAnswer`）。**保留**：`StopPlan(companion)`（中止键）、开场冒泡、`IsActiveFor`。

**为什么这样设计**：IM 密令模式与当面 Plot 是同一套 LLM 计划管线（BuildPlanPrompt + PlanResponse + PlanExecutor），差别只在「输入通道」。入口 IM 化 = 通道统一，玩家少学一套系统弹窗；计划卡片/执行/回报的既有实现零改动复用（KCD2 水准：一个世界内通讯渠道，不弹系统窗打断节奏）。

---

## 二、计划批准/中断/修改三态控制（Q2）

**现状**：PlanCard 已有【同意】【拒绝】；执行期【中止】。缺「修改」。

**方案**：

| 玩家操作 | 现状 | 设计 |
|---------|------|------|
| 同意实施 | ✅ PlanCard【同意】→ `ImCommandFlow.Resolve(msg, true)` | 不变 |
| 拒绝 | ✅ 【拒绝】→ `Resolve(msg, false)` | 不变 |
| 中断执行中 | ✅ 【中止】→ `Abort(msg)` → `executor.CancelByPlayer()` | 不变 |
| **修改计划** | ❌ | **新增**：卡片【修改】按钮 → 底部输入框聚焦 + placeholder 联动「修改计划：…」→ 玩家输入修改意见 → 发送 → **走 `PlanReplan` 管线**（原命令 + 修改意见拼成新命令文本）→ 新 PlanCard（标记「修改版 v2」）→ 再批准 |

**关键纪律**：
- **修改额度复用 PlanReplan 的 ≤2 次**（成功产出才消耗），不新造计数器——防止玩家无限修改刷 LLM（自由感有界，与 Replan 语义一致）。
- 修改入口覆盖两态：**批准前**（卡片待批）与**执行中**（卡片执行态）都可修改。执行中修改 = 现有 PlanReplan.Wire 机制（order_execute_plan 分支自动接）已支持，IM 侧只加 UI 触发点。
- 多人协作（subjects 一带多）：修改作用于该卡片的全部执行者（ResolveExecutors 已有），replan 目标集合复用。
- 铁律 12 检查：批准/拒绝/中止/修改均为「玩家-随从协作流」，与当面 Plot 一致，非冲突博弈零成本出口，合法例外。
- UI 反馈：点【修改】后卡片按钮区变「修改中」态（禁重复点）；发送后恢复；新卡片带「修改版」徽标（复用成员色/系统行样式，LWN_im_* 本地化）。
- 🔴 **记忆纪律（用户裁定 2026-08-10）**：批准/修改/拒绝/中止等**计划元数据一律不写 NPC 记忆**（瞬态决策，写进去会形成树/网：修改版挂旧版、中止的旧计划残留）；只有**实际执行完成的步骤**才写——见下节「计划执行记忆纪律：单向链条」。

### 2.1 计划执行记忆纪律：单向链条（🔴 用户裁定）

**原则：记忆只记录「实际发生过的事」——每一步计划执行节点。** 这样 NPC 的记忆是线性时间线（链条），不是计划版本树（树）或互相引用的中止/修改记录（网）。NPC 记得「我做过什么」，不记得「我打算过什么」。

| 时刻 | 是否写记忆 | 理由 |
|------|:---:|------|
| 计划生成（LLM 计划 JSON） | ❌ | 瞬态意图，未发生 |
| 批准卡片（同意/拒绝/修改按钮） | ❌ | 决策元数据，不构成经历 |
| 修改出「修改版 v2」 | ❌ | v1 未执行 → 零记录，天然无痕；replan 只延续时间线 |
| **步骤执行完成** | ✅ **唯一写入点** | 实际发生的事实，按执行顺序逐条追加 = 单向链条 |
| 计划成功 | ❌ 不额外写总结 | 链条本身已完整，总结会形成层级（树） |
| 计划中止/失败 | ❌ 不追加任何计划级记录 | 已写过的步骤保留（发生过的事，链条不截断）；未执行的步骤零写入 |
| 正在执行（步骤未完成） | ❌ | 完成时刻才写，杜绝半截记录 |

**实现**：
- 挂接点 = `PlanExecutor.CompleteStep(cursor, step)`（[PlanExecutor.cs:581](ExampleModVS/ExampleMod/ExampleMod/Planner/PlanExecutor.cs#L581)，全部步骤完成路径的唯一汇合点）→ 新增 `public event Action<PlanExecutor, PlanStep> OnStepCompleted`，IM 侧挂接写记忆（OnFinished/OnAborted 同款模式，零侵入既有执行器逻辑）。
- **写谁**：每个执行者写**自己的记忆**（多人协作时各写各的步骤，owner 与协作者按各自 ActorCursor 的步骤分别写入）。
- **写什么**：步骤口语化记录 = `{动作标签} {目标} {结果}`，纯 C# 渲染（复用 §3.2 的 `LWN_plan_action_*` 动作名标签表 + SceneSnapshot 角色关键词），**不信任 LLM**。例：「按主公吩咐，去东门望风，等到了信号」。结果（成功/超时跳转）从 world 状态取，中止步骤不带结果。
- **写哪里**：执行者 `SingNpcMemorySystem.AddHistory(role: "plan_step", content, speakerId)` —— role 独立前缀，私聊 UI 按 `im_user/im_npc` 过滤天然隔离（既有机制）；`plan_step` 行进 LLM 上下文 → 后续对话 NPC 能接住「上次望风后来怎样了」（信息塑造目标）；总结漏斗对 `plan_step` 行按普通经历处理（BuildPromptForSummary 可加「（行动）」来源标注，v1 可不加）。
- **与既有偏差②的关系**：im-chat-system.md「密令消息不写 NPC 记忆」**仍然成立**——它管的是**聊天流消息**（IM 里说的话）；本设计是**执行记录**（做过的事），独立写入点，两不相扰。replan 后续步骤继续追加 = 链条在时间线上天然连续，无分支。

---

## 三、NPC 计划陈述 + 应急边界展示（Q3）

**现状**：卡片只显示 `Plan.Summary` 一句话；Plan JSON 有完整 steps/contingencies/guardrails 但玩家看不到。玩家对「随从打算怎么干、出岔子怎么办」无感知 → 铁律 6（KCD2 水准）不达标。

**方案（LLM 口语陈述 + C# 确定性渲染双轨）**：

### 3.1 LLM 口语化陈述（叙事层）

- `PlanResponse` 增加 `narration` 字段：NPC 口吻的一段话（**≤100 字**），内容约束 = **只许转述计划内容**（做什么、分几步、出岔子怎么办），禁止计划外新增行动（防幻觉，铁律 2 延伸）。
- 生成：`BuildPlanPrompt` 输出格式加 narration 字段 + prompt 纪律「以第一人称用自己的口吻转述计划，必须提到应急安排」；null 兜底 → C# 用 Summary 渲染（铁律 1/2）。
- 展示：**narration = 卡片上方的一条 NPC 消息**（带发言人气泡，走消息流管道），卡片本身仍是结构化摘要 + 按钮。「NPC 亲口讲计划」= 当事人自述，非上帝视角（叙事铁律合规）。
- 多人协作：prompt 约束 narration 说明分工（「我和张三…」）。

### 3.2 C# 确定性步骤渲染（信息层，卡片详情）

- PlanCard 增加可展开「详情」区：从 **Plan JSON 结构化渲染**（纯 C#，不信任 LLM 文案）：
  - **步骤列表**：① `move_to 东门` ② `wait 等信号` ③ …（PlanGrammar 动作名 → 中文标签表，`LWN_plan_action_*` 本地化）
  - **应急行**：`contingencies` → 「若 {trigger} 则 {then}」；`on_timeout` → 「等待超时 → {分支}」（S1-S4 跳转）
  - **安全网**：guardrails R1-R7 摘要（如「计划外 NPC 靠近会暂停」）
- 详情区默认收起（卡片不膨胀），点「详情」展开/收起（复用 IsPlanCard 分支的按钮区逻辑）。
- 保证：渲染的是**实际会被执行的逻辑**（PlanExecutor 同一份 JSON），玩家看到的 = 执行器跑的，杜绝「演示计划 ≠ 执行计划」。

### 3.3 计划生成进度反馈：思考中进度条（🔴 2026-08-10 用户裁定）

**现状**：`RequestCommand` 发出后 5~15s（LLM max_tokens 4000）零进度反馈，PlanCard 才上屏 → 玩家焦虑、以为卡死。等待反馈是前置必要条件（3.4 审查员已裁定暂不实现；若未来重启，同样依赖此反馈）。

**方案**：玩家命令消息后**自动插入「生成中」占位行**（消息流内，灰底卡片样式；标题带 TypingText 是私聊回复专用，不混用）：

- **占位行内容**：阶段文案（TextWidget）+ 进度条（**色条 Widget 宽度绑定方案**：`Progress% × MaxWidth`，项目无 ProgressBarWidget 先例且引擎样式未验证，色条确定可行、样式可控；双版本 XML swap 补丁纪律照旧）+ 「XX 正在思考…」灰字。
- **阶段化模拟进度**（LLM 无真实进度，游戏加载条同款成熟做法；阶段文案掩盖模拟单调、KCD2 质感）：

  | 阶段 | 进度区间 | 文案（`LWN_im_*` 本地化） | 推进方式 |
  |------|---------|------------------------|---------|
  | 快照构建 | 0→10% | 「正在观察地形…」 | 秒级直接跳 |
  | LLM 计划生成 | 10→85% | 「正在推演步骤…」 | 模拟：每 1.5s +2~5%（随机抖动），**封顶 85%**（防长时间卡 99% 假象） |
  | 格式校验 | 85→90% | 「正在核对细节…」 | 秒级 |
  | 完成 | 100% | — | 占位行替换为 PlanCard |

- **状态与生命周期**：进度值存 `PendingRequest`（瞬态，Mission 级，不存档——与计划不存档决策一致）；`ImCommandFlow.Tick` 推进（已在主线程，加 progress 步进）；面板重开/切会话 → 按已耗时重新映射（纯展示值）。**v1 不给「取消」按钮**（LLM 调用 fire-and-forget，生成单向如密信送达，玩家关面板不取消、回来卡片仍在）；要取消 v2 加。
- **失败/降级**：占位行替换为系统消息「XX 想不出主意」（既有降级链），进度条消失。
- **VM/XML**：`ImMessageVM` 加 `IsGenerating` / `Progress` / `GenerateText`；消息行模板加生成中分支（IsPlanCard 旁，排除双渲染——既有纪律）。🔴 VM 加属性必同步 XML（双版本）。

### 3.4 计划审查员 agent（🔴 2026-08-10 用户裁定：暂不实现）

> **裁定**：本方案**暂不实现**——v1 空插入点也不留（零调用点最干净，避免死代码与文档误导）。若未来重启，方向待定：候选为**执行端纠错**（计划失败 → 带失败原因重生成，信息增量大）或**计划摘要 + 风险标注**，**而非本节这种「第二遍 LLM 审查」**。
>
> **裁定理由**（2026-08-10）：审查 LLM 与生成 LLM 共享完全相同的信息输入（同一份快照 + 初版计划），信息增量为零，纯靠二次解释挑毛病 → 幻觉建议命中率低；玩家三态控制（§二「修改」）已是最高质量审查通道（玩家有游戏内信息，比审查 LLM 更全）；每次指令多付一次 LLM 往返（IM 场景延迟翻倍）是每单都付的税。真正的价值点在执行端反馈回路，不在静态审查。
>
> 以下为原始设计，**保留备查**（未来重启必须先推翻上述裁定）：

**定位（原始设计）**：计划解析校验通过后、PlanCard 生成前，第二个 LLM 调用——基于初版计划 + 场景快照提出优化建议。**v1 只留架构插入点，空实现返回 null（零开销零改动），v2 填真实审查 LLM。**

```
RequestCommand → 快照 → LLM 计划生成 → 解析+校验（PlanValidator）
→ 🔴 插入点：PlanReviewer.Suggest(plan, snapshotText)   ← v1 空实现 null
→ 拼装 PlanCard（含「计划优化建议」可展开区，v1 无该区）→ 玩家三态控制（§二）
```

- **接口**：`static class PlanReviewer { Task<PlanReview> Suggest(Plan plan, string snapshotText); }`，`PlanReview { string[] Advice; }`（结构化建议列表，C# 解析 null-guard）。
- **降级纪律（铁律 1/2）**：审查 LLM 失败/超时（预算 10s）/未配置 → null，计划照常——**审查是增强不是阻塞**。v1 空实现天然零风险。
- **输出形态裁定（推荐）**：建议**只读展示**（PlanCard「计划优化建议」可展开区，`LWN_im_*` 标题），**不自动改计划**——自动合并 = LLM 改 LLM，双重幻觉叠加（铁律 2）；玩家采纳建议 → 走 §二「修改」流程（人工闭环，审查员的话由玩家裁决）。
- **不引入叙事身份**：审查员非游戏内 NPC（无记忆/关系/人格），UI 标题即身份——现实里随从队伍也没有「计划委员会」，KCD2 出戏风险最小化。进度条阶段文案「正在完善计划…」即其全部叙事存在。
- **触发条件（v2 细化）**：仅复杂计划触发审查（步骤 ≥ `PlanReviewerMinSteps`，Settings 可配，默认 3）——简单计划审查纯浪费。
- **耗时预算**：审查 max_tokens ≤1000、超时 10s、失败静默。总耗时 = 计划生成 + 审查（可能翻倍）→ 3.3 进度条的 90→98% 阶段即为审查预留。

### 3.5 执行期

- 保持现状：头顶 HUD 执行摘要 + 回报消息（成功/失败/EndMessage）+ 中止汇报。不做「每步完成刷一条系统消息」（刷屏，违背 KCD2 信息节流）。

---

## 四、群聊兼容（Q4）

| 新机制 | 群聊适配 |
|--------|---------|
| 计划陈述（narration） | 主执行者（owner）发 narration 消息；多人协作时 prompt 已约束分工口吻；卡片与 narration 都是普通消息流成员（PlanCard 分支已排除双渲染） |
| 修改计划 | 群聊中【修改】走同一 replan 管线，目标 = 该卡片执行者集合（既有） |
| 闲聊 action（§五） | attacker = 说话者；**defender 解析优先级：@提及成员（ImTopicMatcher 候选匹配）> 消息内成员名匹配（WorldFactProvider 实体识别复用）> 默认玩家** |
| NPC 主动提议计划（🔴 新增联动） | NPC 想行动 → 群聊发消息「我想去望风」→ 触发 `RequestCommand`（NPC 提议计划 = 玩家批准后执行）→ 走 PlanCard。**这是 Q2「NPC 思考完行动之后」的正解闭环**：NPC 提议 → 玩家 同意/修改/拒绝 |
| 记忆纪律 | 维持既有偏差②：聊天流消息（密令/narration）不写 NPC 记忆；**执行记录按 §2.1 单向链条规则写**——多人协作时每个执行者各写自己完成的步骤（plan_step 行） |

**NPC 主动提议的触发源（v1 候选）**：ReactiveAgent 反应（`asked_to_follow` 等触发词已有 → 决策结果广播）+ NpcInitiativeIntents（7 个 NPC 主动意图，如 NewsConflictIntent 的后续行动）+ 事件广播器（ImEventBroadcaster 话题上升级为行动提议）。**v1 范围裁定**：只接 ReactiveAgent 的「被搭话后想做事」+ 事件广播的「大事件后想主动行动」，意图引擎留作 v2。

---

## 五、闲聊行动系统（Q5）

**既有轮子**：当面对话的 `npc_action`（LLMResponse_Casual.NpcAction）+ `ActionHandler`；`ImChatManager.IsPresentInMission(heroId)`（Q3 已建，左栏在场状态）提供「defender 是否同场景」判定。**升级 = IM 回复管线接入 + 空间感知动作空间**。

### 5.1 接入

- `BuildPrompt_ImReply` 输出格式增加可选字段：`npc_action`（注入动作空间）+ `action_target`（defender 名字文本，**不是 StringId**——LLM 输出 ID 不可信，铁律 2）+ `action_level`（档位，仅关系类用）。
- 回复 JSON 解析（复用 LLMResponse_Casual，null-guard）→ 投递消息后调用升级版 `ActionHandler.HandleAction(code, attacker, defender, agent, level)`。
- 降级链：LLM 失败 → 模板台词，action 强制 NONE（确定性优先，模板不做动作）。

### 5.2 空间感知动作空间（🔴 空间裁决优先，2026-08-10 用户裁定）

**原则**：动作空间不由「IM 还是当面对话」决定，而由 **attacker 与 defender 的空间关系**决定。同一句 IM 消息，对方在不在场、玩家在不在大地图，LLM 能选的动作完全不同——**随从常驻跟随 = 玩家在 Mission 时随从大概率同场景，此时必须能用场景内动作**。

**裁决（C# 确定性，不交 LLM）**：

```csharp
[Flags] enum ActionSpace { InScene = 1, ImRemote = 2, Party = 4 }

// 复用既有轮子 ImChatManager.IsPresentInMission(heroId)
ActionSpace ResolveSpace(Hero defender) {
    if (Mission.Current == null) return ActionSpace.Party;      // 玩家在 Campaign：部队动作为主
    if (defender != null && ImChatManager.IsPresentInMission(defender.StringId))
        return ActionSpace.InScene;                             // 随从/在场 NPC：场景内可执行
    return ActionSpace.ImRemote;                                // 不在场：远距语义
}
```

**执行顺序**：defender 解析（§四优先级：@提及 > 成员名 > 默认玩家）→ `ResolveSpace` → 按空间裁剪注入动作空间（**LLM 只看到该空间的合法动作**，无空间概念）→ 回复生成 → `HandleAction(code, attacker, defender, agent, level)`（签名见 §5.1；**space 由内部 `ResolveSpace(defender)` 裁决，调用方不传**；IM 投递时 `agent = FindAgentByHeroId(defender.StringId)`，InScene 物理动作的执行载体）。

**三空间动作表**（一张注册表，`SpaceMask` 位掩码裁剪——Q6 的 ActionDefinition 加 `Spaces` 字段）：

| Code | InScene | ImRemote | Party | 效果 |
|------|:---:|:---:|:---:|------|
| `NONE` | ✅ | ✅ | ✅ | 默认，普通寒暄必选 |
| `RELATION_UP` / `RELATION_DOWN` | ✅ | ✅ | ✅ | 好感档位 **small=±3 / medium=±5 / large=±10，LLM 只选档位不选数值**（铁律 2）→ `ApplyRelationChangeBetweenHeroes(A,B,delta)`，attacker→defender |
| `PRAISE` | ✅ | ✅ | ✅ | 夸赞：defender 本地声望小升（SettlementHonorStore，在场=当众/不在场=背后说好话） |
| `SPREAD_RUMOR` | ✅ | ✅ | ✅ | 造谣：defender 本地声望小降 + 写双方记忆 |
| `THREATEN_VERBAL` / `PROMISE` | ✅ | ✅ | ✅ | 纯记忆类：写 defender 记忆（「A 威胁过我/答应过…」→ 后续对话接得住） |
| `ATTACK` / `DUEL` | ✅ | ❌ | ❌ | **物理动作，仅 defender 在场**（`agent != null` 自动满足）；IM 触发保留确认弹窗（风险动作确认，与当面对话一致） |
| `MARRY_SUCCESS` / `JOIN_CLAN` | ✅ | ❌ | ❌ | **当面仪式，仅在场**（隔空结婚/入伙 = 出戏） |
| *物理行为类*（EMOTE/FACE/LOOK/FOLLOW/KNOCKOUT/STEAL/GIVE_GOLD/SIGNAL…，**复用原子动作库**） | ✅ | ❌ | ❌ | 见下方「InScene 扩展：原子动作库平移」 |
| `GATHER_TO_PLAYER` | ❌ | ❌ | ✅ | **部队动作**：defender party 移向玩家 party（大地图可见反馈） |
| `PARTY_PATROL` | ❌ | ❌ | ✅ | **部队动作**：defender party 巡逻其当前所在 settlement（`CurrentSettlement == null` → 降级 NONE + DebugLogger；巡逻目标 **C# 确定，不解析 LLM 文本**） |

**空间语义要点**：
- **InScene（玩家在 Mission + 对方同场景）**：**三空间里动作最丰富**——语义类（关系/声望/记忆）+ 当面仪式 + **原子动作库物理行为类（见下）**。随从就在旁边：能拔刀、能当面求婚/入伙、能打手势行礼、能当面塞钱、能背后下手偷——全部物理可执行。这是玩家点名的主场景（随从常驻跟随 = 同 Mission 高发）。
- **ImRemote（玩家在 Mission + 对方不在场）**：物理动作与当面仪式全部排除（不能隔空打人/结婚/入伙，出戏）；保留关系/声望/记忆类（背后说坏话、散布谣言——远距离语义自洽）。
- **Party（玩家在 Campaign 大地图）**：物理动作无意义（无场景）；关系/记忆类仍可；**主角是部队动作**——聊完天随从的部队真的去集结/巡逻（部队移动 = 大地图自然可见反馈）。
- 🔴 **Party 动作资格守卫**：`IsValid = defender.Clan == Clan.PlayerClan && defender.PartyBelongedTo != null`——**非玩家家族 NPC（商队头领/领主）不听玩家调遣**，聊不动 party（铁律 8 平权）；攻击性部队动作（RAID/BESIEGE/ENGAGE）**不注入闲聊空间**——一句话烧村子 = 出戏 + LLM 权限爆炸，攻击性行动走既有 PlanCommandFlow 玩家批准管线。
- **战斗门控**：Mission 战斗中 IM 已有 `Settings.IsInteractionDisabled()` 门控（既有），空间裁决无需额外处理。

**频率纪律**（防 LLM 滥用，掉好感贬值，跨空间统一）：
- prompt 硬纪律：「只有这句话真的有实际后果时才选非 NONE，普通寒暄一律 NONE」；
- C# 侧冷却：同 attacker→同 defender 的关系/声望/party 类 action 每 60s 最多 1 次（`Settings` 可配）——超频 → 该次降级 NONE + DebugLogger；**演出类（EMOTE/FACE/LOOK_AT/SIGNAL）不参与冷却**（演出是对话的一部分，冷却会阉割表现力）；高风险类（ATTACK/DUEL/KNOCKOUT/STEAL_ATTEMPT）确认弹窗兜底，无需冷却；party 动作重复触发 = AI 目标覆盖，防抖 + 纪律双保险。

**反馈明确（原则一）裁定**：关系/声望类**静默执行 + 后续可见**——不弹系统行「张三对李四的印象变差了」（刷屏 + 上帝视角）；态度变化由后续言行体现（下次对话 NPC 语气/回应模式变化、关系档位描述）。**例外**：涉及玩家自己的关系变化可弹轻量提示（「你与张三的关系变差了」，玩家可见的合理反馈，`ApplyRelationChangeBetweenHeroes` 的 showQuickNotification 参数即官方此意）。物理动作 = 确认弹窗 + 战斗演出自然反馈；party 动作 = 部队移动/巡逻为大地图自然可见反馈，不弹系统行，执行失败（无 party/目标 null）→ 降级 NONE + DebugLogger。

**叙事铁律检查**：action 是 NPC 言行的一部分（说坏话 → 关系变差；「我去集结了」→ 部队真动），效果是行为自然后果，无上帝视角注入；LLM 只见自己的记忆/频道上下文 + **当前空间的合法动作列表**，不知道 defender 的实际数值（防「知道 B 好感 32 于是精准 -3」的上帝数值）。

### 5.3 InScene 扩展：原子动作库平移（🔴 2026-08-10 用户裁定）

**总原则**：InScene 空间的物理行为 = **PlanGrammar 原子动作库的子集平移**，执行层**零新代码**——闲聊动作被包装成**单步 Plan**（1 个 step：action + target=defender agent）走 `PlanExecutor.TryCreateSubAction` 既有分支（[PlanExecutor.cs:755](ExampleModVS/ExampleMod/ExampleMod/Planner/PlanExecutor.cs#L755)），InlineSteps/IAtomicAction 全复用，由既有 `TickAll` 驱动。密令模式能跑的每个原子行为，闲聊一句话同样能触发（同空间同能力，出戏为零）。

**可平移清单**（对照 `ActionsInPromptOrder` 21 个原子动作逐一裁定）：

| 闲聊动作码（= 原子动作名） | 执行类（既有） | 参数（C# 确定，铁律 2） | 风险 | v1 |
|------|------|------|:---:|:---:|
| `ATTACK` / `DUEL` | `FightEnemyAction` | target=defender | 高 | ✅ |
| `KNOCKOUT` | `KnockoutInlineState` | target=defender（背后击晕轮子） | 高 | ✅ |
| `STEAL_ATTEMPT` | `StealAttemptInlineState` | target=defender；result 路由（success/empty/impossible/interrupted）既有 | 高 | ✅ |
| `EMOTE` | `EmoteInlineState` | **白名单 9 动画**：nod/shake/wave/cheer/bow/shrug/point/threaten/disappointed（LLM 选 key，C# 映射动画） | 低 | ✅ |
| `FACE` / `LOOK_AT` | `TurnToDirectionAction` / `LookAtAction` | target=defender；时长默认 2s | 低 | ✅ |
| `FOLLOW` / `STOP_FOLLOWING` | `FollowAgentAction` / `StayAction` | target=defender；无限保持；**重算间隔动态化见 §5.4** | 中 | ✅ |
| `SIGNAL_PLAYER` | `SignalInlineState` | 无参 | 低 | ✅ |
| `GIVE_GOLD` | `GiveInlineState` | 接收者=玩家（**既有实现固定 Agent.Main**，与 defender 无关）；金额**档位制** small/medium/large → C# 映射（LLM 不输出数值） | 中 | ✅ |
| `MAKE_NOISE` | `MakeNoiseInlineState` | 无参 | 低 | 可选 |
| `MOVE_TO` | `MoveToPositionAction` | **target 文本 → C# 解析（既有 `TryResolvePosition` 链）**：agent 名（`ResolveStepTarget` 自动朝向）→ 语义 tag zone（`SceneSnapshot.SemanticZoneTags` 运行时探测，LLM 快照可见）——「走到张三旁边」「去城门口」都成立 | 低 | ✅ |
| `SAY_TO` | `SayInlineState` | **target=defender agent**（`ResolveStepAgent` 既有解析）；台词 v1 = **IM 回复正文复述**（一句话两用：对玩家说 + 转头对 defender 转述；prompt 约束正文须可转述、避免指向玩家），v2 = 独立 text 字段 | 低 | ✅ |
| `GIVE_ITEM` | `GiveInlineState` | 物品名文本 → C# 解析（铁律 5 两轮策略） | 中 | v2 |
| `LEAD` | `LeadInlineState` | 目标解析体系同 move_to（agent/zone 均可），带路闲聊语义待验证 | 中 | v2 |
| `SHADOW` / `NEGOTIATE` | — | **计划执行器本身未实现**（走明确失败路径）→ 平移前先补执行分支 | 中 | v2 |

**排除（闲聊语境语义不成立）**：`wait` / `end_plan`（编排类，无单行为语义）。

**关键纪律**：
- **参数全 C# 确定**：默认 target = `FindAgentByHeroId(defender.StringId)` 兜底；MOVE_TO/SAY_TO 的 target = LLM 名字文本 → **既有 `TryResolveAgent`/`TryResolvePosition` 解析链**（agent → 快照对象/语义 tag zone），解析失败 → 降级 NONE + DebugLogger；EMOTE 白名单、金额档位、时长默认——LLM 只给动作码 + 档位 + 名字文本（铁律 2）。
- **风险分级反馈**：高风险（ATTACK/DUEL/KNOCKOUT/STEAL_ATTEMPT）→ 确认弹窗（与既有 ATTACK 一致，弹窗与 IM 模态共存）；中风险（FOLLOW/GIVE_GOLD）→ 自然可见反馈（AI 行为/金币变动）；低风险演出类（EMOTE/FACE/LOOK_AT/SIGNAL）→ 静默演出，不打断消息流。（频率纪律统一见 §5.2）
- **记忆纪律**：闲聊单步行为**不落 plan_step 记忆**（§2.1 只管计划执行步骤）；KNOCKOUT/STEAL_ATTEMPT 走既有行为时由既有犯罪/事件系统自然捕获（PendingWorldEvent 等），闲聊层不重复写——防双写抢戏。
- **叙事铁律**：动作 = 说完那句话的即时行为（「主公，这个给你」→ 真掏钱），言行一致，无上帝视角。

### 5.4 FollowAgentAction 动态重算间隔优化（🔴 2026-08-10 用户裁定）

**现状问题**：[AtomicAction.cs:682](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L682) moving 态**每 0.2s 无条件**重算理想点 + 重发寻路；`ScriptedMoveToPoint` = `SetScriptedPosition` **native 全量路径重发**（AgentControlHelper.cs:175）。远距离目标动 1m → 理想点动 1m，对百米外的执行者毫无意义，重算纯浪费；而近距离快速目标（0.2s 间隔）又可能跟不上冲刺/骑马。

**目标类型路由（职责契约）**：
- **静止目标（gameentity tag / zone / 坐标）→ `move_to` 分支**（`TryResolvePosition` 既有解析链，一次性寻路到点）——`follow` 分支**只接会动的 agent**。当前 `ResolveStepAgent` 天然如此（静止实体解析不出 agent），本设计将契约固化：静止实体永不进 FollowAgentAction。
- **会动的 agent → `FollowAgentAction` + 动态间隔**（下方）。

**动态重算间隔**（目标速度 + 距离双因子，C# 确定性）：

```csharp
// 目标平面速度：Native 内建 AverageVelocity（比瞬时 MovementVelocity 稳，防间隔乱跳）
float targetSpeed = new Vec2(_target.AverageVelocity.x, _target.AverageVelocity.y).Length;
float dist = MathF.Sqrt(_currentDistanceSq);   // 欧氏直线距离（Vec3.DistanceSquared 含 z，既有量）；间隔公式只做量级分级，直线 ≈ 寻路长度×曲折系数，曼哈顿无网格前提不适用
// 距离因子：越远间隔越大（远距微小位移无意义）；速度因子：越快间隔越小（目标点变化快）
float repathInterval = MathF.Clamp(0.15f * (1f + dist / 10f) / MathF.Max(targetSpeed / 2.5f, 0.25f), 0.15f, 3.0f);
```

| 距离 | 目标速度 | 重算间隔 | vs 现状 0.2s |
|------|---------|---------|:---:|
| 3.5m（正常跟随位，stopDistance） | 跑 4m/s | 0.15s（下限） | 更灵敏（跟上冲刺） |
| 3.5m | 步行 2.2m/s | 0.23s | ≈ 持平 |
| 3.5m | 静止 | 0.8s | ↓ 4 倍 |
| 10m | 步行 | 0.34s | ↓ 1.7 倍 |
| 30m | 步行 | 0.7s | ↓ 3.5 倍 |
| 100m | 跑 4m/s | 1.0s | ↓ 5 倍 |
| 100m | 步行 | 1.9s | ↓ 9.5 倍 |
| 超远 / 静止 | — | 3.0s（上限） | ↓ 15 倍 |

**关键纪律**：
- **静止 ≠ 永动**：速度近零只放大间隔到心跳（3s 上限），**不跳过重算**——目标（守卫/玩家）随时可能动起来，心跳保证自动恢复高频。与 move_to 路由的差别 = follow 保留朝向偏移/极坐标语义（目标动起来自动跟回）。
- **上限 3s = 安全网**：目标不可达（跳崖/绕路/卡墙）时执行者仍周期性自愈纠偏，不会永久走错方向（对应既有 5s 瞬移兜底的互补）。
- **速度用 `AverageVelocity` 不用 `MovementVelocity`**：native 内建平均窗口，瞬时速度抖动会让间隔在 0.15~3s 间乱跳。
- **影响面 = 全部 follow 调用点（5 处）**：AgentBrain 4 处（`:281`「过来」/ `:291` 跟随玩家 / `:992` 跟随 Leader / `:1456`）+ PlanExecutor `:826` follow 步骤——一处生效全局受益，**行为不变**（只改重发频率，不动寻路逻辑/停止距离/朝向偏移/keepFollow）。
- **不改**：5s 超时瞬移兜底（`_maxTime`）、stopDistance 迟滞区（`_startDistanceSq` 防抖）、OnEnd 朝向修正。
- 数值落地后实机验证（城镇人堆跟随不绕圈、骑马目标不脱跟），参数进 `Settings` 可配（`FollowRepathMin/Max`）。

### 5.5 对抗反应链裁定：defender 侧零新 AI（🔴 2026-08-10 用户裁定）

**结论：不新造对抗性 AI 层**——随从执行对抗性动作后，defender 的反应**全部走既有三层机制**。玩家规则透明（可预测：被偷了守卫会来、被打了会反击），随从与玩家同一套规则（铁律 8 平权）——随从的「聪明」来自 ReactiveAgent 人格演算权重，「灵活」来自 16 个 ReactionActions 反应动作库。

| 随从动作 | defender 反应机制 | 现状 |
|---------|------------------|:---:|
| `ATTACK` / `DUEL` / `KNOCKOUT`（物理对抗） | **既有完整链路**：被打事件 → AgentBrain 分支（受害者台词 `CombatJoin_Victim` + 玩家打人警戒脉冲）→ `EnqueueAction(FightEnemyAction(attacker))`（[AgentBrain.cs:481](ExampleModVS/ExampleMod/ExampleMod/AI/AgentBrain.cs#L481)）→ **`CombatManager.StartFight` 统一队伍管理**（多队模型：队2 玩家侧/队3 敌方/队4 切磋/PlayerTeam 旁观、纯 NPC 战斗→遗留 faction 容器；`_originalTeams` 原队记录 + EndFight 恢复 + 战斗计数器）→ 引擎驱动打斗细节；**已在战斗中 → 只感知不换目标**（EffectiveAction is FightEnemyAction → return，索敌交原版 AI，防多攻击者切换抖动）；儿童 → `FleeFromAction` 恐惧逃离 | ✅ 既有 |
| `STEAL_ATTEMPT`（偷窃） | 既有偷窃系统完整反应链（守卫阻止/受害者发现/result 路由）| ✅ 既有 |
| 攻击平民（犯罪） | 犯罪系统（PendingWorldEvent → WorldEventStore）+ ReactiveAgent `see_crime`/`combat_nearby`/`see_ally_killed` 触发词 → 人格演算（报警/逃/围观）| ✅ 既有 |
| `THREATEN_VERBAL` / `SPREAD_RUMOR` / `PRAISE`（说话类，**InScene**） | 🔴 **复用 `BroadcastSpokenTo` 广播链**（InlineSteps.cs:226 同款：`SendEventToAgent(defender, "spoken_to", attacker, line, ...)`）→ defender 人格演算反应（愤怒/畏惧/叫守卫/记仇——ReactionActions 16 个）| ✅ 已接线（C2+F-2） |
| 同上（**ImRemote**，人不在场） | 不广播，纯记忆写入（设计已定）——「A 威胁过我」后续对话接得住 | ✅ 已实施 |

**对话层归属（问题 2 裁定 → 2026-08-11 架构收敛）**：场景对话 = **统一框架（DialogueComponent）+ 差异化插槽**——① AgentBrain 事件分支 `BubbleSay` 单句台词（CombatJoin_* 等模板，保留无状态快捷播放）；② **ReactiveAgent respond**（触发词 → 人格演算 → LLM 实时台词，双方共享记忆接力[对方的话写 user/回应写 assistant]、60s 会话超时重置轮次、回合上限降级模板、台词态度 = 演算分数）；③ **SayToSlot**（计划模式 say_to 专属：ChatPhase 状态机平移，走向段推进 + 轮询对方记忆续话，执行器驱动；与 social 共用 DialogueSession 契约）。**威胁 → spoken_to → respond = 符合情景的一轮对话已成立**（「你敢威胁我？」态度由演算决定）；**attacker 跟进续话已实施（2026-08-11）**——SocialSlot 续话器：威胁/夸/造谣 InScene 广播后注册，发起方轮询对方记忆自由跟进 2~3 轮收敛（原 v2 候选 = 同 SayInlineState 的轮询续话模式，已落地为通用续话器）。

**适配点（v1 接受，v2 扩展）**：犯罪分类脉冲写死 `criminal == Agent.Main`（AgentBrain.cs:512/519——「区分偷窃 vs 攻击 vs 击晕（criminal==玩家时）」）——**随从（玩家友方）打人走 see_crime 目击链但无犯罪分类脉冲**。v2 把 criminal 判定扩展为「玩家侧」（attacker 是玩家或玩家友方）。

### 5.6 对话流统一架构：DialogueComponent（✅ 已实施，2026-08-10）

**裁定**：对话流统一成一个通用组件 **`DialogueComponent`**。现状三层分裂：respond（`DialogueRound` 应答器）、SayInlineState（`ChatPhase` 会话编排器）、BubbleSay（无状态模板）——**同一件事的会话状态拆在两处**；「续话」机制只在 SayInlineState 有，respond 应答后对话链即断（5.5 的「attacker 跟进缺」根源即此）。

**✅ 已实施范围**（`Planner/DialogueComponent.cs`，用户裁定 2026-08-10）：

```csharp
// ① 统一台词生成管线（respond / 随从续话 / 事件台词 共享）
static Task<DialogueLine> GenerateLine(world, identity, attitude, topic, roundText,
    outlineSection, otherName, history, lastLine, ruleKey, ruleFallback,
    actionSpace = null, maxTokens = 220, timeoutMs = 8000);
//    actionSpace 非空 → JSON 通道（npc_reply/npc_action/action_target/action_level，与 IM 群聊同构）；
//    解析失败 → 原文当台词、动作 NONE（降级链与 IM 一致）。DialogueLine: Reply/FromLlm/ActionCode/...
// ② 统一对话发起入口（广播收敛：spoken_to + 旁观者 seen_speaking）
static void HandleDialogue(Agent requester, Agent target, string topic, string line, string outlineStep = null);
//    调用点：SayInlineState.BroadcastSpokenTo / 说话类对抗（THREATEN/PRAISE/SPREAD_RUMOR InScene）/
//    附近频道玩家喊话 —— 三处广播全收敛于此，单点维护
```

- **respond 升级**：JSON 通道带**动作决策**（拔刀/威胁手势/做表情——ActionHandler 空间裁决/冷却全复用；设计 §5.6 未含此点，实施补充，与 IM 群聊同构）。
- 🔴 **台词管线记忆纪律（用户裁定）**：**有 LLM → 实时生成台词 → 写记忆**（user/assistant 接力，对话续得上）；**无 LLM（未配置/超时/失败）→ 模板台词 → 不写记忆**——模板是重复无个性内容，写进记忆会稀释真实事件记忆、且污染续话轮询。适用全部对话入口，与 IM 侧降级链同构。
- **旁观者插话**（用户裁定，实施补充）：`HandleDialogue` 内置 `seen_speaking` 广播（15m 内排除双方）→ ReactiveAgent 演算（触发词表已有；默认模板无反应 = 不插话，插话是人格化的）→ 插话 = respond 复用 → AgentSay → nearby 投影自动可见。**防双响纪律**：一个触发词一个回应——附近频道对 NPC 只读，玩家喊话是唯一「频道 → 场景」入口。
- **BubbleSay 保留为无状态快捷播放**（CombatJoin_* 等模板单句不进会话状态机，零开销）。

**⚠️ 设计演进（2026-08-11 用户裁定：架构收敛）**：
- **原「DialogueSession 会话状态合并」设计已废弃**——生命周期分析表明 respond 的 DialogueRound（NPC 级应答轮次）与 SayInlineState 的 ChatPhase（对话实例级编排进度）**不是同一件事**，强行合并 = 状态相互污染（NPC 正在 respond 闲聊时计划 say_to 来了怎么切）。最终收敛形态（**一个对象 + 一个真实契约 + 两种驱动**，无伪实现无空占位）：

```csharp
// 统一对话实例（一段对话的全部通用状态，两种驱动共用）
class DialogueSession {
    Agent Initiator, Target; string Topic;
    List<string> Outline; int OutlineIndex;
    int Round; float LastResponseAt; float LastResponseCheck;
    IDialogueSlot Slot;
}
// 生命周期插槽（真实契约 = 三钩子，驱动无关，无伪实现）
interface IDialogueSlot {
    void OnStart(DialogueSession s);          // 对话启动钩子
    void OnTick(DialogueSession s, float dt); // 每帧推进：续话/中止/收尾逻辑全在槽内自决
    void OnEnd(DialogueSession s);            // 对话结束钩子
}
// 两种驱动（同一个 session，不同的每帧调用者）：
//   执行器驱动：say_to（SayInlineState 持有 Session 每帧调 Slot.OnTick；时序字段为 say_to 专属保留槽内）
//   续话器驱动：social（注册表 RegisterSession/EndSession + TickContinuations 每帧调 Slot.OnTick）
// 差异矩阵：SayToSlot = 走向推进 + 步骤收尾（BC-006 行为等价平移）；SocialSlot = 轮询跟进 2~3 轮收敛 + 60s 超时
// PlayerLedSlot 已删除——没有生命周期的东西不需要插槽（玩家发起 = 无 session，框架只管应答）
```

- **统一边界（用户裁定）**：统一的是「说话」（台词+动作+记忆+播放出口 AgentSay → nearby 投影）；**不统一**「行为决策回流」（follow/refuse → `plan_decision` → 执行器 `on_event` 控制流，永远走 AIEvent）；say_to 编排 = 执行器驱动（非续话器）。
- **配套继承矩阵（对话产物）**：统一后场景对话免费继承 IM 的**动作决策**（同一张 ActionHandler 注册表 + 空间裁决 + 冷却）与**记忆体系**（SingNpcMemorySystem 三层）；**不继承**选人/延迟调度（场景对话即时性要求 2s 预算），选人接口（ImTopicMatcher）保留给旁观者插话的未来增强（沉寂补偿等）。
- **收益达成情况**：②③ 已达成（台词/记忆/降级管线单点维护 + 新场景免费对话流 + **续话通用化——对话链不再单轮即断**）；① 由 DialogueSession 统一对话实例达成（say_to 与 social 共用同一 session 类型与契约，NPC 级应答轮次保持独立——资源保护，非对话内容）。
- **成本**：respond + SayInlineState 重构 + 实机回归（BC-006 respond / say_to 对话模式是已验证行为，重构必须行为等价）。

### 5.7 附近频道：场景冒泡接入 IM（🔴 2026-08-10 用户裁定）

**定义**：IM 新增**「附近」频道**（Nearby）——场景内所有 AgentSay（冒泡台词）实时流入玩家 IM；玩家可在频道里说话（场景喊话），**响应不确定**（离玩家很近的 NPC 才可能应声）。沉浸感：玩家开 IM 能看到守卫聊天、目击反应、NPC 互怼，且能插话。

**关键设计**：

| 维度 | 裁定 |
|------|------|
| 生效条件 | **仅 InScene**（玩家在 Mission 时左栏可见；Campaign 隐藏） |
| 消息来源 | **`AgentHudMissionView.AgentSay` 单一出口转发**（BubbleSay / respond 播放 / say_to 播放全部汇聚于此——与 5.6 DialogueComponent 播放出口同点，挂接一次全局生效） |
| 距离管理（🔴 2026-08-11 用户裁定） | **AgentSay 前置距离分层**（nearby 无成员名单 = 动态说话者，靠分层保证"都在玩家附近"）：**≤30m**（FarHearDistance）→ 3D 冒泡 + nearby 频道（视觉 + 频道）；**>30m 且视野外** → 只弹屏幕消息「远处传来声音」（听觉语义，不冒泡不进频道——远处 3D 冒泡玩家看不见，创建 HUD 纯浪费）；**>30m 但视野内** → 无声（原版语义：看得见听不见）。玩家自己冒泡恒播放。转发时点判定（说话时在附近才进），历史消息保留（消息流语义）——频道反映"玩家当前所在场景的可听范围" |
| 玩家说话 | ① 玩家头顶冒泡喊话（AgentSay(Agent.Main, text)）；② **广播 `spoken_to` 给范围内最近的 NPC**（`Settings` 可配 `NearbyRespondRadius`，默认 6m）→ ReactiveAgent 演算：respond（LLM/模板）或 ignore/refuse → **「不一定有人响应」天然成立**（范围内无 NPC / 最近者演算 ignore → 无人应声，频道静默） |
| 模板 NPC | **允许说话**——身份 = `agent.Index`（Mission 级临时编号，无 Hero StringId 的兜底）+ `agent.Name` 显示名；响应走既有 TEMP_AGENT 记忆兜底（respond 既有机制） |
| 防刷屏 | 同 sender 200ms 合并（场景事件台词低频，日常闲聊无冒泡来源，仅为保险）；「通知防刷屏」纪律照旧 |
| 生命周期 | **Mission 级会话**（固定 ID `nearby`，非持久化频道）：进场景创建/可用，Mission 结束消息流归档（重进场景新流，Index 重置身份不复用） |
| 记忆纪律 | **频道消息不进任何 NPC 记忆**（场景瞬态对话，写记忆 = 全员拷贝爆炸，同群聊旁观者不写先例）；玩家喊话的 respond 对话按 5.6 纪律（LLM 生成才写响应者记忆） |

**与既有轮子的关系**：玩家喊话 = 复用 spoken_to 触发词链（零新增词表）；响应 = respond 既有管线（人格演算 + LLM/模板 + TEMP 记忆）；冒泡播放 = AgentSay 既有。**新增量仅**：AgentSay 转发挂接 + 频道会话 + 玩家喊话广播，三处小改。

**叙事铁律检查**：频道内容 = 场景内真实发生的冒泡（玩家亲耳可闻的对话换个载体），无上帝视角注入；玩家喊话 = 场景内真实喊话，NPC 按人格演算回应（听见了不一定要理你，KCD2 同款）。

**接线约束**（已实施，收敛于 `DialogueComponent.HandleDialogue`）：说话类对抗（THREATEN/SPREAD_RUMOR/PRAISE）的 InScene 版执行后，**广播 `spoken_to` 给 defender**（speaker=attacker，line=触发台词）——威胁也是一种「搭话」，走既有触发词零新增词表。ImRemote 版不广播（人不在场，隔空威胁 = 记忆事件）。

**旁观者插话**（🔴 F-2 实施补充，2026-08-11 对话裁定）：`HandleDialogue` 内置 seen_speaking 广播——A 对 B 说话时，**15m 内所有旁观者独立按距离加权概率中签**（`chance = max(1 - dist/15m, 5%)`，距离越近概率越高：0m≈100%、15m≈5%——近的几乎必有机会插话、远的偶尔，围观现实感），中签者收到 `seen_speaking(说话者, 被搭话者, 台词)` → ReactiveAgent 演算（触发词表已有；默认模板无反应 = 不插话，插话是人格化的，由 LLM 反应计划决定）→ 插话 = respond 复用 → AgentSay → nearby 投影自动可见。玩家在频道看到的就是「A → B → C 插话」完整对话流。位置基准 = 对话双方中点。**防双响纪律**：中签 → 演算 → respond 一个触发词一个回应；附近频道对 NPC 只读，玩家喊话是唯一「频道 → 场景」入口（不引入频道驱动的 NPC 互聊）。

**v1 明确不做**：
- **战斗后「被打记忆」**：被决斗/被击晕者事后记仇（「上次你砍我」）涉及战斗结束事件挂接，v2 候选——v1 内 defender 对被打的记忆仅由犯罪系统捕获（平民被打 = 犯罪事件），合法决斗无记录（可接受）。
- **新增「被攻击」ReactiveAgent 触发词**：不需要——被打已由 **AgentBrain 内联事件分支**覆盖（→ FightEnemyAction，见上表），触发词表无需重复登记；战役等 DisabledInteractionMissionModes 场景 brain 关闭，引擎原生 AI 处理（大战场士兵互砍本就不该 mod 干预）。

**叙事铁律检查**：defender 反应 = 人格演算（既有规则）+ 引擎 AI，全部是「看见/听见什么 → 怎么反应」的行为自然后果，无上帝视角注入；玩家看到的反应与规则表一致（可预测性 = 明确规则的正面体验，KCD2 守卫体系同款）。

---

## 六、action defender 双向化（Q6）

**现状**：`ActionDefinition.IsValid/Execute(Hero npc, Hero player, Agent)` —— 隐式 defender = 玩家，单向。

**升级**：

```csharp
// ActionDefinition 签名升级（当面对话 + IM 共用一份注册表，动作空间按 §5.2 位掩码裁剪）
class ActionDefinition {
    string Code; string Description;
    ActionSpace Spaces;     // 位掩码：InScene / ImRemote / Party（§5.2），IsValid 前先按空间过滤
    Func<Hero attacker, Hero defender, Agent, bool> IsValid;    // defender 可 null
    Action<Hero attacker, Hero defender, Agent> Execute;         // 同上
    bool AffectsBoth;   // 防御性效果开关（v1 默认 false）
}
```

**LLM JSON**：`action_target` = defender **名字文本**（非 ID）→ C# 解析：复用 `WorldFactProvider` 实体识别（Hero.AllAliveHeroes 遍历 FirstName/Name/本地化名，长度≥2 防单字误伤）→ 匹配失败 → 动作降级（NONE 或默认作用于玩家）。解析是 C# 确定性逻辑，LLM 只给文本。**群聊场景按 §四优先级**（@提及 > 消息内成员名 > 默认玩家），私聊按本段。

**方向语义（C# 决定，不交 LLM）**：
- 核心效果 = attacker 表达的态度：`RELATION_DOWN, defender=B` → `ApplyRelationChangeBetweenHeroes(A, B, -delta)`（**官方 API，NPC↔NPC，反编译确认**）；
- `AffectsBoth=true` 的动作（如侮辱/公开嘲讽）：防御性效果 defender→attacker 同档反向（A 公开骂 B → B 也记恨 A）；v1 默认 false（谣言只单向往 defender 声望，不双向）；
- 模板 NPC：关系类 action `IsValid` 守卫（无好感系统，pitfalls.md 既有结论）；IM 侧天然全 Hero（模板 NPC 不进 IM，既有决策）。

---

## 七、实施步骤

> ✅ **全部 Phase 已实施（2026-08-10）**，标注状态如下：

1. ✅ **Phase A 行动密令入口 IM 化**（Q1）：`PlanCommandFlow.Start` 改造 → `ImChatView.Open` 加 mode 参数 → 澄清轮 IM 化 → 退役 vanilla 三通道
2. ✅ **Phase C1 闲聊单条指令执行（提前，2026-08-10 用户裁定）——执行层验证门**：`ActionDefinition` 签名升级（加 `Spaces` 位掩码）→ `ResolveSpace` 空间裁决（复用 `ImChatManager.IsPresentInMission`）→ 单步 Plan 通道（`ChatActionFlow` → `PlanExecutor.TryCreateSubAction` 既有分支）→ **§5.4 FollowAgentAction 动态重算间隔** → **13 个 InScene 原子动作 + 语义类动作注册表**（ATTACK/DUEL/KNOCKOUT/STEAL_ATTEMPT/EMOTE/FACE/LOOK_AT/FOLLOW/STOP_FOLLOWING/MOVE_TO/SAY_TO/GIVE_GOLD/SIGNAL_PLAYER/MAKE_NOISE + RELATION_UP/DOWN/PRAISE/SPREAD_RUMOR/THREATEN_VERBAL/PROMISE + MARRY/JOIN_CLAN + GATHER_TO_PLAYER/PARTY_PATROL）。**单指令实机逐一验证仍待用户（验证清单）**
3. ✅ **Phase B 计划三态 + 陈述**（Q2/Q3）：PlanCard【修改】按钮 + 输入框联动 + 修改管线（额度 ≤2）→ `PlanResponse.narration` + BuildPlanPrompt 输出格式 → 卡片详情（C# 步骤/应急渲染 + 动作名本地化表）→ **§2.1 步骤级记忆写入（PlanExecutor.OnStepCompleted 事件 + plan_step 渲染 + 写执行者记忆）** → **§3.3 生成中占位行 + 阶段化模拟进度**
4. ✅ **Phase C2 闲聊剩余**：party 动作执行（`PARTY_PATROL` 复用 `V.PatrolAround`；`GATHER_TO_PLAYER` → `V.GatherToPlayer` = EscortParty 语义，反编译确认 + VersionCompat 三分支）→ 记忆类 action 写 defender 记忆 → 冷却 + 群聊 defender 解析优先级（成员名/称号/FirstName）
5. ✅ **Phase D 群聊联动**（Q4）：NPC 主动提议计划（ReactiveAgent 新反应动作 `propose_plan` → LLM 提议 → 私聊 Proposal 消息 → 玩家批准后走 PlanCard）
6. ✅ **Phase E 打磨**：本地化全量（~70 key 双语言）、py 词表同步（check_vocab_sync.py）、验证脚本回归（26 PASS）
7. ✅ **Phase F**：**§5.7 附近频道**（`NearbyFeed`：AgentSay 转发挂接 + 频道会话 + 玩家喊话广播）→ **§5.6 DialogueComponent**（`GenerateLine` 台词管线统一 + `HandleDialogue` 广播收敛 + respond JSON 化带动作 + seen_speaking 旁观者插话 + 模板降级不写记忆）→ **架构收敛（2026-08-11）**：`DialogueSession`（统一对话实例）+ `IDialogueSlot` 三钩子真实契约（OnStart/OnTick/OnEnd）+ `SayToSlot`（ChatPhase 行为等价平移，SayInlineState 薄壳）+ `SocialSlot`（威胁/NPC 闲聊跟进——attacker 跟进缺补上）+ 续话器（`RegisterSession`/`EndSession`/`TickContinuations`，OnMissionTick 挂接）；`PlayerLedSlot` 删除——原「DialogueSession 会话状态合并」设计废弃（生命周期分析：NPC 级轮次 ≠ 对话级编排，见 §5.6 设计演进）

## 八、验证清单（实机）

> ⏳ **全部待实机验证**（编译/静态回归已过；以下需进游戏逐一确认）：

- G 长按 → IM 直接打开定位该随从私聊 + 计划模式，无 vanilla 弹窗；打字不误关；战斗门控正常
- 计划卡片：同意 → 执行；中止 → 停止；【修改】→ 输入框 → 新卡片「修改版」→ 批准；修改超 2 次 → 拒绝并提示额度用尽
- narration：NPC 消息口语化讲清步骤+应急；多人协作讲清分工；LLM 失败 → Summary 兜底
- **进度条（§3.3）**：发送命令 → 立即出现生成中占位行（阶段文案+进度条）；进度 10→85% 缓慢增长不卡 99%；卡片上屏时占位行被替换；LLM 失败 → 占位行变「想不出主意」系统消息；中途关面板重开 → 进度按已耗时重映射；面板重开聊天记录无占位残留
- **审查员（§3.4，已裁定暂不实现）**：无代码无验证项（若未来重启，先更新本节设计再补验证清单）
- **记忆链条（§2.1）**：成功计划 → 执行者记忆出现按序 plan_step 行（动作+目标+结果），无「计划总结」行；中止/失败 → 已写步骤保留、无计划级残留；批准前修改 → v1 零记录；执行中 replan → 链条连续无分支；群聊多人 → 各执行者只写自己的步骤；私聊 UI 无 plan_step 混入（Role 过滤）
- 卡片详情：步骤/应急/安全网渲染与 PlanExecutor 执行逻辑一致；详情默认收起
- 闲聊 action：夸/损 → 好感档位变化（±3/5/10）；「张三对李四说了坏话」→ 李四对张三关系下降（A↔B 官方 API 生效）；LLM 名字解析错误 → 降级 NONE 无崩溃；60s 冷却生效；模板降级无动作。**Phase C1 门**：13 个 InScene 单指令逐一实机验证（触发 → 执行 → 复位，肉眼 + DebugLogger 确认），全通才进 Phase B
- **空间路由（§5.2/§5.3）**：① 玩家在城镇 Mission + 随从在场私聊 → 注入含 ATTACK/DUEL + 物理行为类（EMOTE/FACE/LOOK_AT/FOLLOW/MOVE_TO/SAY_TO/KNOCKOUT/STEAL_ATTEMPT/GIVE_GOLD/SIGNAL_PLAYER），随从「主公这个给你」→ 真给钱、EMOTE「威胁」→ 做出威胁手势动画、MOVE_TO「去城门口」→ 语义 tag zone 解析走到城门、SAY_TO → 转头对 defender 转述回复正文、KNOCKOUT/STEAL → 确认弹窗 + 犯罪系统自然捕获；② 玩家在 Mission + 不在场 NPC 私聊 → 注入无任何物理行为类（LLM 不可能选出）；③ 玩家在 Campaign + 随从私聊 → 注入含 GATHER_TO_PLAYER/PARTY_PATROL，随从 party 真的移向玩家/开始巡逻；④ 玩家在 Campaign + 商队头领私聊 → 注入无 party 动作（家族资格守卫）；⑤ 非家族 NPC 的 party 动作 LLM 硬选 → 降级 NONE + DebugLogger（IsValid 兜底）；⑥ 演出类动作无冷却正常触发、关系类 60s 冷却生效
- **动态重算间隔（§5.4）**：跟随静止守卫 → 重发频率降到心跳（≈0.7s+，日志/帧调试可查）；跟随跑动目标 → 频率升到 0.15s 不脱跟；100m 外跟随 → 间隔放大（≤3s）；目标从静止变跑动 → 间隔自动恢复高频；5 处调用点（AgentBrain 4 + PlanExecutor 1）行为不变（跟随不绕圈、停止距离不漂移）
- **对抗反应链（§5.5）**：随从攻击在场 NPC → 被打者 AgentBrain 分支（CombatJoin 台词 + 警戒脉冲）→ FightEnemyAction 参战（CombatManager 队伍管理正常、EndFight 恢复原队）；随从偷窃 → 守卫/受害者既有反应链；随从当面向守卫威胁 → 守卫收到 spoken_to → 人格演算反应（报警/对峙/无视，冒泡可查）；ImRemote 威胁 → defender 无即时反应、记忆有记录
- **附近频道（§5.7）**：InScene 左栏出现「附近」、Campaign 隐藏；场景冒泡台词实时流入（守卫聊天/目击反应可读）；玩家喊话 → 最近 NPC 响应（respond 冒泡）或无人应声（演算 ignore / 范围内无 NPC）；范围内无 NPC 时喊话静默不崩；模板 NPC 冒泡显示 agent.Name；Mission 结束频道归档、重进场景新流（Index 身份不复用）
- **对话流统一（§5.6，v2）**：重构后 respond / say_to 行为等价回归（BC-006 respond 回复、say_to 对话模式走向、模板降级无记忆）；续话能力在非计划场景可用（闲聊威胁 attacker 跟进）
- 群聊：NPC 提议计划 → 玩家批准/修改/拒绝闭环；action defender = @提及者优先
- **对话流统一（§5.6，F-2）**：respond JSON 化后台词质量回归（BC-006 respond/say_to 行为等价）；respond 带动作（威胁手势/拔刀/做表情触发）；模板降级不写记忆 → 续话轮询不再接住模板；随从续话（say_to 对话模式走向）行为不变
- **对话体系收敛（§5.6，2026-08-11）**：① say_to 对话模式行为等价回归（SayToSlot：开场/广播延迟/走向推进/终止——BC-006）；② **SocialSlot 威胁跟进**：A 威胁 B → B respond → A 轮询跟进 1~2 句（对峙吵起来）→ 3 轮收敛；B 不理 → 60s 超时结束；③ SocialSlot NPC 闲聊跟进：夸/造谣后发起方跟进；④ 同对（A↔B）重复注册不叠叠乐；⑤ Mission 结束 ClearContinuations 无残留（日志可查）
- **旁观者插话（§5.6/§5.7）**：A 对 B 说话（say_to/威胁/当众夸/当众造谣/玩家喊话）→ 有 LLM 反应计划的旁观者插话（respond 冒泡 + nearby 可见）；默认模板 NPC 不插话；插话不双响（一个触发词一个回应）
- **附近频道（§5.7，F-1）**：InScene 左栏出现「附近」、Campaign 隐藏；场景冒泡台词实时流入（守卫聊天/目击反应可读）；玩家喊话 → 最近 NPC 响应（respond 冒泡）或无人应声（演算 ignore / 范围内无 NPC）；范围内无 NPC 时喊话静默不崩；模板 NPC 冒泡显示 agent.Name；Mission 结束频道归档、重进场景新流（Index 身份不复用）
- Debug+Release 编译 0 错误；validate_localization.py / check_vocab_sync.py / test_im_topics.py 回归

## 九、设计自检

| 检查项 | 结论 |
|--------|------|
| 四原则①反馈明确 | 入口即反馈（IM 直接打开）；**思考中进度条 + 阶段文案（§3.3）**；修改有「修改版」徽标；关系/声望类静默 + 玩家侧关系变化轻提示；**物理动作 = 确认弹窗 + 战斗演出、party 动作 = 部队移动可见（§5.2 分空间反馈）** |
| 四原则②自由感 | 同意/拒绝/中止/修改 + 修改额度有界（防滥用） |
| 四原则③NPC 接得住 | 记忆类 action 写 defender 记忆 → 后续对话接得住；群聊全员可接 |
| 四原则④信息塑造目标 | narration/详情让玩家看懂计划 → 决策有依据；行动后果塑造关系目标 |
| 铁律 1 | 全部入口 IsLLMConfigured 总闸；降级链齐全 |
| 铁律 2 | 所有新 JSON 字段 null-guard；LLM 只给档位/名字文本，数值与 ID 全 C# 解析 |
| 铁律 6 | 计划陈述/修改全在 IM 世界内完成，零系统弹窗打断 |
| 铁律 12 | 三态控制 = 协作流合法例外，非零成本博弈 |
| 铁律 13 | 所有新玩家可见文本走 LWN_* 本地化 |
| 叙事铁律 | narration = 当事人自述计划（非上帝视角）；行动效果 = 行为自然结果；无数值上帝视角；**记忆只写实际发生的步骤（§2.1），NPC 记得做过什么而非打算过什么** |

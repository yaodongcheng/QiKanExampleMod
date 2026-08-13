# 动作注册单一事实源重构（ActionRegistry 主表）

> 状态：✅ 已完成（2026-08-13 实施；编译 0 错误 0 警告 + check_vocab_sync.py 六项全绿 + 主表静态自检五连（失败写日志不弹窗））
> 事后修复（同日实机）：`ExecutorImplemented` bool 字段默认值陷阱（默认 false 导致 34 行全判"未实现"→ Debug.Assert 实机弹窗崩）——已改默认值 `= true` 并将五连自检由 Debug.Assert 改为日志式（铁律 1）
> 对应问题：动作注册分散 6+ 处，新增动作改 5+ 处；已出现词表注册但执行器未实现的失同步（shadow/negotiate/duel）

## Context

重构前动作注册分散多处：**PlanVocab 计划词表**（PlanGrammar.cs，21 码）、**ActionHandler 闲聊动作**（InteractionController.cs，27 大写码）各自独立注册同一批物理行为，加上标签映射（PlanActionLabel）、prompt 注入（GetActionSpacePrompt）、参数填充（ChatActionFlow switch）、播报/卡片文案 switch——新增一个动作要改 5+ 处。

目标：**策划只维护一张主表，其余全部派生**。用户已定决策：
1. 只合并 A+B 两套（PlanVocab 计划词表 + ActionHandler 闲聊动作）；ReactiveAgent 反应词表（独立机制）不动
2. 执行委托进表——简单动作执行逻辑写成主表委托，复杂行为（move_to/say_to 等）留执行器 switch 加注释关联
3. **单码统一**（2026-08-13 用户裁定）：不保留 Code/ImCode 双码，统一一个小写码（理由见下）

## 单码统一说明

现状双码（计划小写 `move_to` vs 闲聊大写 `MOVE_TO`）是历史沿革，本质同一事物——闲聊侧 LLM 只从注入列表照抄（prompt 给什么选什么），展示码本可任意定。统一为小写单码后：

- 闲聊动作空间注入改为 `- "move_to": desc`（GetActionSpacePrompt 输出 Code），LLM 回包 npc_action 就是统一码
- `HandleAction`/`HandleImAction`/`RunActionCore` 查 `ByCode` 字典（**StringComparer.OrdinalIgnoreCase** —— 兼容旧存档 `ImMessage.ActionCode="MOVE_TO"` 等大写码，卡片批准路径不破）
- `PlanActionLabel` 单套 LabelKey（ATTACK 双标签问题消失：闲聊展示即 `order_attack`，标签统一 "attack"）
- 计划侧 21 码 + 顺序不变 → **82% 基线零影响**；闲聊 prompt 展示码大写→小写，无基线数据，无回归风险
- 别名表保留（`attack→order_attack` 计划侧 LLM 容错），"ATTACK" 旧码由大小写不敏感查询兜底

## 架构

新增 `Planner/ActionRegistry.cs`：静态只读数组 = 全部事实（34 行：21 行计划码按原序在前 + 13 行闲聊-only 在后）。派生面六处，全部只读主表：

```
ActionRegistry.All（34 行）
 ├─ PlanVocab.ActionsInPromptOrder/Actions/ActionAliases/AllowedResultKeys/TerminalActions ← 派生
 ├─ ActionHandler（HandleAction/HandleImAction/GetActionSpacePrompt/AnnounceDecision/PostActionProposal/RunActionCore）← 查表
 ├─ PlanActionLabel ← 查表（签名不变，调用方零改动）
 ├─ ChatActionFlow.TryExecute ← 查表 FillParams
 └─ Scripts/check_vocab_sync.py ← 改提取源
```

**ActionSpec 关键字段**（单码方案）：`Code`（统一小写码，计划词表与闲聊动作空间共用）/ `Description` / `LabelKey`+`LabelFallback` / `InPlanVocab`（进计划词表）/ `InChatSpace`（进闲聊动作空间）/ `Spaces` / `NeedsCooldown` / `RequiresConfirm` / `InquiryTitleKey`+`InquiryMsgKey`（弹窗卡片文案）/ `Aliases[]` / `ResultKeys` / `IsTerminal` / `ExecutorImplemented` / **`ChatOrder`**（闲聊 prompt 展示序，钉死 1..27）/ `IsValid` / `Execute` / `ExecuteCore` / `FillParams(PlanStep, level, sayText)` / `AnnounceParam(Func<string,string>)`。

## 重复注册合并清单（48 身份 → 34 行）

现状注册身份：PlanVocab 21 + ActionHandler 27 = **48 个注册身份**；其中 **14 个是同一物理行为的双重身份**（计划码 + 闲聊大写码），合并为 1 行 → 主表 **34 行**。

**交集（14 行合并，同一行为两个身份 → 一行，统一小写码）**：move_to/MOVE_TO、follow/FOLLOW、stop_following/STOP_FOLLOWING、order_attack/ATTACK（别名 attack 承载旧大写）、knockout/KNOCKOUT、face/FACE、look_at/LOOK_AT、say_to/SAY_TO、emote/EMOTE、make_noise/MAKE_NOISE、signal_player/SIGNAL_PLAYER、steal_attempt/STEAL_ATTEMPT、give_gold/GIVE_GOLD、duel/DUEL（⚠️ 双语义一行承载：计划侧=判定型未实现；闲聊侧=切磋开打经 ExecuteCore 发 order_attack 事件，互不干扰）。

**仅计划侧（7 行，无闲聊入口）**：lead、wait、give_item、deliver_item、shadow（未实现）、negotiate（未实现）、end_plan（IsTerminal）。

**仅闲聊侧（13 行，无计划词表）**：NONE（空操作）、RELATION_UP、RELATION_DOWN、INCREASE_RELATION、DECREASE_RELATION（LabelKey 承载别名→relation_up/down）、PRAISE、SPREAD_RUMOR、THREATEN_VERBAL、PROMISE、MARRY_SUCCESS、JOIN_CLAN、PARTY_PATROL、GATHER_TO_PLAYER。

## 每动作执行条件表（34 行逐行前置条件）

载体维度（谁执行）：**agent** = 需 attacker 在场景的物理载体 Agent；**hero** = 只需 Hero 对象（铁律 8：模板 NPC 无 Hero → 关系/记忆/声望类天然被 IsValid 挡住，物理动作模板 NPC 可执行）；**party** = defender 的部队（仅 Campaign）。空间由 ActionSpace 位掩码裁决。Code 列 = 统一小写码（闲聊/计划共用；旧大写码由 OrdinalIgnoreCase 查询兼容）。

| Code（统一） | 载体 | 空间 | IsValid 前置条件 | 高风险 | 冷却 |
|---|---|---|---|---|---|
| move_to | agent | InScene | agent != null | | |
| follow | agent | InScene | agent != null | | |
| stop_following | agent | InScene | agent != null | | |
| order_attack | agent | InScene | agent != null | ✅ | |
| knockout | agent | InScene | agent != null | ✅ | |
| lead | 执行器 | 仅计划 | 计划语义（执行器） | | |
| face | agent | InScene | agent != null | | |
| look_at | agent | InScene | agent != null | | |
| say_to | agent | InScene | agent != null | | |
| wait | 执行器 | 仅计划 | 计划语义 | | |
| emote | agent | InScene | agent != null | | |
| make_noise | agent | InScene | agent != null | | |
| signal_player | agent | InScene | agent != null | | |
| steal_attempt | agent | InScene | agent != null | ✅ | |
| give_item | 执行器 | 仅计划 | 计划语义 | | |
| give_gold | hero | InScene | npc != null（实施时对照原 560-580 逐字搬） | | ✅ |
| deliver_item | 执行器 | 仅计划 | 计划语义 | | |
| shadow | 执行器 | 仅计划 | **未实现** | | |
| negotiate | 执行器 | 仅计划 | **未实现** | | |
| duel | agent | InScene | 闲聊侧 agent != null；计划侧未实现 | ✅ | |
| end_plan | 执行器 | 仅计划 | IsTerminal | | |
| NONE | 任意 | 全 | 恒真 | | |
| relation_up / relation_down | hero | 全空间 | **a != null && d != null**（好感系统需双方 Hero） | | ✅ |
| increase_relation / decrease_relation | hero | 全空间 | a != null && d != null | | ✅ |
| praise | hero | 全空间 | d != null（本地声望需 defender 在定居点） | | ✅ |
| spread_rumor | hero | 全空间 | d != null | | ✅ |
| threaten_verbal | hero | 全空间 | d != null | | ✅ |
| promise | hero | 全空间 | d != null | | ✅ |
| marry_success | hero | InScene | 异性 + 双方单身（npc/player 均非 null） | | |
| join_clan | hero | InScene | npc 无族 或 流浪者 | | |
| party_patrol | party | Party | **资格守卫**：defender≠玩家本人、Clan==PlayerClan、有独立 party（≠MainParty） | | ✅ |
| gather_to_player | party | Party | 同上 | | ✅ |

主表 34 行要点：move_to…end_plan 21 行计划序**严格按原 ActionsInPromptOrder 抄**（82% LLM 回归基线依赖）；`order_attack` 行 Aliases["attack"] 承载旧码；NONE 行保留（HandleAction("NONE") 静默 no-op）；静态构造 `Debug.Assert` 钉死（计划 21 码字面量序、ChatOrder 1..27 序列、ExecutorImplemented=false == {shadow,negotiate,duel}、alias 无重复、IsValid/Execute 非空）——**零游戏 API 调用**，无静态初始化环（PlanVocab 单向依赖 ActionRegistry）。

## agent 载体动作与 AgentBrain / AtomicAction 衔接（执行链路契约）

**总原则**：ActionRegistry 只负责「注册 + 闲聊侧入口接线」，**不负责行为实现**——agent 动作的行为语义仍归 PlanExecutor 执行器（用户决策 2）。主表 `Execute` 委托对 agent 类动作的职责 = 点火（包装单步 Plan），对 hero/party 类动作才是行为实现。

**agent 载体动作两条执行通道（主表行归属哪个通道由 Execute 委托决定，实施时逐行核对）：**

### 通道 A：计划执行通道（14 个交集动作，闲聊/计划共用一条管线）
```
闲聊侧: ActionSpec.Execute → ChatActionFlow.TryExecute（单步 Plan 包装，日志"包裹为单步 Custom 计划"）
计划侧: LLM 计划 JSON 步骤
        └─→ 汇合于 PlanExecutor.TryCreateSubAction（Planner/PlanExecutor.cs:855-989）
             ├─ IAtomicAction 原子分支（6 码）→ new XXXAction → cursor.SubAction → AgentBrain.EnqueueAction
             │    move_to    → 目标=agent → FollowAgentAction(keepFollow:false)（追踪式，target 在动不走空点）
             │                目标=坐标点 → MoveToPositionAction（快照寻路）
             │    follow     → FollowAgentAction(keepFollow:timeout<=0)（relpos 解析 behind/left/right/line 相对位）
             │    stop_following → StayAction（语义化清理）
             │    order_attack   → FightEnemyAction（计划侧战斗）
             │    face       → TurnToDirectionAction
             │    look_at    → LookAtAction(seconds)
             │    shadow/negotiate/duel → 未实现 → 步骤失败（不走动作表注册逻辑）
             └─ InlineState 内联分支（12 码）→ 执行器内联状态机
                  say_to/wait/signal_player/end_plan/make_noise/give_item/give_gold/deliver_item
                  emote/lead/steal_attempt/knockout —— 行为性内联包 InlinePlanAction 适配器入队由脑驱动
```
**AgentBrain 驱动契约**（AI/AgentBrain.cs + AI/Actions/AtomicAction.cs）：IAtomicAction 生命周期 `OnStart → OnTick(dt) → IsFinished → OnEnd`，由 AgentBrain 逐帧驱动；入队时 `Suspend` vanilla AI（`[AI-Debug] Suspend`），完成出队 `Resume`（`[AI-Debug] Resume`）。日志形态实机已验证：`[Brain-Enqueue] 入队 FollowAgentAction → [Brain-Tick] 开始执行/完成 → [PlanExecutor] 步骤完成 → 计划结束`。**主表不注册 IAtomicAction 子类**——AtomicAction 类是行为实现层（AI/Actions/ 目录），与注册表正交；新增"需要新行为"的动作 = 主表一行 + AtomicAction 新类 + TryCreateSubAction case。

### 通道 B：AgentBrain 事件分发通道（order_attack/duel 闲聊侧特例，不进计划）
```
ActionSpec.ExecuteCore（order_attack/duel 行）→ AgentAIController.SendEventToAgent(agent, "order_attack", target)
        → AgentBrain 事件分发（AgentBrain.cs:387 aiEvent.EventType=="order_attack"）
        → ClearAllActions → FightEnemyAction 入队 → CombatManager 战斗链
```
设计意图（原注释）：**战斗是持续行为，由 Brain 管理生命周期，执行器不该介入**——所以闲聊侧的"攻击/切磋"不走单步 Plan 通道。`order_attack` 因此是双身份：主表动作码（计划词表）≠ AIEvent 事件名（Brain 层协议）。**AIEvent 事件名不属于动作注册表**（是 Brain 层协议，AgentBrain.cs:387 白名单），主表 order_attack 行 ExecuteCore 注释标明此桥接即可，不注册。

### 其余载体不涉 Brain
- **hero 载体**（relation_up/down/increase/decrease/praise/spread_rumor/threaten_verbal/promise/marry_success/join_clan）：Execute 委托直接 C# 实现（ChangeRelationAction / SettlementHonorStore / WriteMemory / MarriageAction），不经 AgentBrain/计划。其中 praise/spread_rumor/threaten_verbal 的 InScene 版调 DialogueComponent.HandleDialogue 说话广播链（defender 人格反应）——这是对话链不是行为链，与注册表正交。
- **party 载体**（party_patrol/gather_to_player）：Execute 委托调 V.PatrolAround / V.GatherToPlayer（SetPartyAiAction 大地图部队 AI），Campaign 层无 Agent/Brain。

### 主表行与执行器的对应关系（实施时每个 agent 行核对两项）
1. `Execute` 委托 = `ChatActionFlow.TryExecute(agent, Code, targetText, level, sayText)` 形态（闲聊入口点火）
2. 执行语义落点 = PlanExecutor.TryCreateSubAction case（新动作若行为已存在 = 复用；新行为 = 主表一行 + AtomicAction/InlineState + case）

## 实施步骤

### 0. 基线捕获（先做）
- 在 `PlanCommandFlow.BuildGrammar()`（Interaction/PlanCommandFlow.cs:242-261）临时加 `DebugLogger.Log("[vocab-check] " + 拼接结果)`；PlanDebugCommands 临时加 `lwn.dumpaction` 控制台指令（三种空间 defender + 一个 party 资格不符者调 GetActionSpacePrompt）。
- 进游戏触发一次密令 prompt + 三条 dump，保存 `Debug/vocab_before.txt`（82% 基线的 C# 侧对比基准；py 侧 `test_llm_plan.py:154` 硬编码 `_GRAMMAR_VOCAB` 不受重构影响，无需重跑 68 例压测）。

### 1. 新增 `Planner/ActionRegistry.cs`
- ActionSpec + 34 行主表（Execute/IsValid/ExecuteCore 正文从 InteractionController.cs:110-690 **逐字搬运**，内联 lambda 风格保持）+ `All`/`ByCode`(StringComparer.OrdinalIgnoreCase)/`FindByCode`/`FindByLabelCode`（ByCode 优先回落别名）+ `PlanActions`（Where(InPlanVocab)）派生 + 静态构造 Debug.Assert 四连。
- 字段 `InChatSpace`（是否进闲聊动作空间）+ `InPlanVocab` 两个布尔正交：14 交集双 true；7 仅计划 InPlanVocab 单 true；13 仅闲聊 InChatSpace 单 true。
- 委托里引用的 `WriteMemory`/`FindAgentByHeroId`/`LevelDelta`/`RunActionCore` 需 ActionHandler 侧 internal 化（见步骤 3）。
- **ExampleMod.csproj 第 360 行附近 Planner 组加 `<Compile Include="Planner\ActionRegistry.cs" />`**（显式列表，漏掉不编译）。

### 2. 改 `Planner/PlanGrammar.cs`（311-384 区段）
- `ActionsInPromptOrder` 改派生：`ActionRegistry.PlanActions.Select(s => s.Code).ToArray()`（类型/名字不变 → BuildGrammar 零改动，21 项原序保证）。
- `ActionAliases` / `AllowedResultKeys` / `TerminalActions` 改从主表派生；`Actions` 保持现有派生式。
- Predicates/Queries/EntityKeywords/ReservedDirectives 不动；验证器（623/630/672 行）零改动。

### 3. 改 `Interaction/InteractionController.cs`
- 删除 `ActionDefinition` 类（54-72）、`_actions`（75）、`static ActionHandler()`（81-84）、`InitializeActions()`（109-691）。
- `WriteMemory`/`FindAgentByHeroId`/`LevelDelta`/`RunActionCore` 改 `internal static`；`RunActionCore` 内查找改 `ActionRegistry.FindByCode`（OrdinalIgnoreCase 天然兼容旧大写码）。
- `HandleAction`：`_actions.FirstOrDefault(...)` → `FindByCode(actionCode)`（OrdinalIgnoreCase，旧存档 "ATTACK"/"MOVE_TO" 自动命中对应小写码行）；空间/冷却/IsValid/AnnounceDecision/Execute 流程逐字保留。
- `HandleImAction`（906 行查表 + 886 行 NONE 提前返回保留）；`PostActionProposal`（930 预检 + 936-943 content switch → `"LWN_ui_interact_inquiry_" + InquiryMsgKey + "_msg"`）。
- `GetActionSpacePrompt`（708-729）：遍历 `All.Where(InChatSpace).OrderBy(ChatOrder)`，跳过 `Code == "NONE"`，输出 Code（小写统一码）；空间裁剪 + Party IsValid 资格裁剪（722 行）逐字保留。
- `AnnounceDecision`（738-783）：NONE 判据改 Code；标签取 Code（已小写，无需 ToLowerInvariant）；参数 switch（751-757）→ `AnnounceParam?.Invoke(level)`。
- 四个 RequiresConfirm 确认弹窗（406/437/460/483）抽共享 helper `ConfirmDialog(spec, ...)`（title=InquiryTitleKey，msg=InquiryMsgKey，按钮恒 LWN_ui_interact_btn_fight）。
- `_actionCooldown`/`IsCooledDown` 原地保留（运行时状态非注册表）。

### 4. 改 `ImChat/ImCommandFlow.cs` `PlanActionLabel`（928-957）
签名 internal static 不变；内部改查 `FindByLabelCode`（OrdinalIgnoreCase）：命中 → `LWNTextHelper.ResolveText("LWN_plan_action_" + LabelKey, LabelFallback)`；未知码兜底 `ResolveText("LWN_plan_action_" + action, action)` 保留。既有行为保持：increase_relation→relation_up（LabelKey 承载）、order_attack→"attack" 统一标签。三处调用点（746/976-977/1150）零改动。

### 5. 改 `Interaction/ChatActionFlow.cs`（57-77 switch）
→ `ActionRegistry.FindByCode(actionCode)?.FillParams?.Invoke(step, level, sayText);`（6 个 FillParams 迁主表：follow/look_at/say_to/emote/steal_attempt/give_gold）。`GoldLevelAmount` 留在原类，主表 lambda 引用。

### 6. 改 `Scripts/check_vocab_sync.py`
- 提取源改 ActionRegistry.cs：正则按 `new ActionSpec\s*\{([^}]*)\}` 块提取 `Code = "..."` + `InPlanVocab = true`（顺序无关的集合比较不变）。文件头 docstring 更新单一事实源指针。py 侧 ALLOWED_ACTIONS/ACTION_ALIASES 副本不动。

### 7. 收尾
- 删步骤 0 临时钩子；`git diff` 逐行 review（27 条 lambda 搬运防手滑改字）；文档注释更新（PlanGrammar.cs:325-327、PlanCommandFlow.cs:244-246 注册指向主表）。
- 更新 `plans/rules/wheels.d/`：登记「ActionRegistry 动作主表」轮子（解决什么问题 + 字段表 + 新增动作流程 + 派生面清单）。

## 边界清单（易漏）

1. `Scripts/check_vocab_sync.py` 正则（漏改 = 校验脚本永久失败）
2. `ExampleMod.csproj` 显式 Compile Include（漏改 = 编译失败）
3. `RunActionCore`（854 行）是第三处 `_actions` 查找
4. 4 个 private helper 的 internal 化（漏 = 编译失败）
5. 旧存档大写码兼容：ByCode 必须 OrdinalIgnoreCase（漏 = 旧决策卡片批准后执行失败）
6. NONE 三条跳过规则（886 提前返回 / GetActionSpacePrompt 跳过 / AnnounceDecision 跳过）逐条保留
7. `AffectsBoth` 死字段直接删
8. 外部调用方签名不变需验证：HandleAction（2744/ReactiveAgent:997/PersuadeSlot:486）、HandleImAction（ImReplyService:273/ImChatView:820）、GetActionSpacePrompt（PromptBuilder x4/ReactiveAgent:783/PersuadeSlot:452/ImReplyService:467）、PlanActionLabel（746/976-977/1150）
9. 闲聊 prompt 展示码大写→小写是**有意变化**（无基线数据）；密令计划 prompt 21 码必须**逐字节不变**（82% 基线）

## 验证

1. **构建**：`dotnet build ExampleModVS\ExampleMod\ExampleMod\ExampleMod.csproj -c Debug` 零错误零新警告；启动游戏 Debug.Assert 全过（DEBUG 构建生效）。
2. **82% 基线**：重构后重跑步骤 0 捕获，diff vocab_before/vocab_after——密令 prompt「动作（action）：move_to / … / end_plan」**逐字节相同**；闲聊 prompt 三空间顺序/文案与预期一致（ChatOrder 钉死，展示码统一小写）。
3. **工具链**：`python Scripts/check_vocab_sync.py -v` 退出码 0；`python Scripts/validate_plan_json.py --json Debug/PlanExamples/*.json` 全过（含 W_COLLECT negotiate result 路由）。
4. **实机抽查**：闲聊 relation_down 冷却二连降级 NONE；emote/give_gold 播报参数正确；order_attack 卡片同意后 ExecuteCore 不二次确认；Party 资格裁剪生效（招募同伴看不到 party_patrol/gather_to_player）；密令卡片 order_attack→"attack" 标签不变；unknown action 丢弃日志；negotiate 计划执行时明确失败（与重构前一致）。

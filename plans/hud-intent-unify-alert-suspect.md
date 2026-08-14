# HUD 意图行统一 + 警戒值 suspect 化（视觉区分 + 任何人犯法闭环 + 调停/逮捕/大义灭亲责任链）

> 状态：**已实施**（2026-08-13 主体 + 2026-08-14 嫌疑人单一事实源修正；Debug 编译 0 错误；实机验证项见「验证」章节待做）。关联：`wheels.d/ui.md`、`wheels.d/agent.md`、`wheels.d/planner.md`、`wheels.d/input.md`、`wheels.d/dialogue.md`、`wheels.d/worldevent.md`
> 日期：2026-08-13。用户裁定：非玩家警戒眼青蓝冷色系；调停机制全做；未调停时 WorldEvent 嫌疑人锁随从本人；随从被击败 → 定居点俘虏（复用原版 hero 俘虏机制）；犯罪对话支持「大义灭亲交出随从」。
> 🔴 **2026-08-14 修正（嫌疑人单一事实源）**：原 §2.6.1「显式 null = unknown，不回落玩家」在 C# 层面不可行——默认参数与显式 null 无法区分，实现照抄后模板随从犯法仍回落玩家。定案：**嫌疑人来源只有两条——目击者脑内（RegisterWitness 从 TopSuspectAgent 推导，三态：null=玩家 / Hero=随从 / `""` 哨兵=无名 unknown）与玩家自首（ConfessIntent）**；`RegisterTheftWitnesses` 砍回纯记账（只记赃物证词，不推进阶段、不锁嫌疑人）；`FinalizePendingWorldEvent` 删「有真目击 → 写死玩家 Active」兜底（无人拉满 → Dormant 过夜 → Emerging 无头案）；`TransitionStage` Active 分支加 `""` 哨兵（跳过 InferSuspect）。已知边界：Emerging 调查路径 `TryLockSuspect` 的目击者描述匹配仍返回 InitiatorId（PendingWorldEvent 创建时写死玩家）——随从犯法无人拉满的事件隔日调查可能查回玩家，待实机后决策。

---

## 🔴 实机测试清单（2026-08-14，按依赖顺序）

**前置准备**：进入村庄/城镇场景；`ShowNpcIntent` 开关打开（HUD 意图行验证用）；随从执行偷窃 = 密谋命令下达（或 `custom.plan_debug run` 注入计划）；测试后看 `Debug/StoryEngine_RuntimeLog.txt` 对应日志行。

### T1 回归：玩家偷窃一切照旧
- **操作**：玩家蹲下扒窃/偷猪，目击者在场
- **预期**：目击者警戒眼**黄/红暖色**；拉满后守卫冒泡质问玩家；赔偿对话正常
- **异常**：眼变青蓝或守卫无视玩家 → 玩家路径被误标 suspect，看 `[RegisterWitness]` 日志应显示 suspect=player

### T2 主场景：有名随从偷窃被目击（suspect 化闭环）
- **操作**：让有名随从偷窃，守卫/村民在场
- **预期**：目击者眼**青蓝色系**；守卫拉满后冒泡「站住，XX！」→ 拔刀**追打随从**；**不再质问玩家**；玩家其他随从围观不涨警戒（友方豁免）
- **日志**：`[RegisterWitness]` 显示 `suspect=随从名(Idx=..)`
- **异常**：守卫质问玩家 → 三态推导有问题（suspect 显示 player）

### T3 🔴 本次修复核心：模板随从（无 Hero 士兵）偷窃被目击
- **操作**：普通士兵随从偷窃，被守卫目击
- **预期**：守卫照常追打（Mission 层不依赖 Hero）；案件嫌疑人 = **unknown**——玩家**不被问责、不涨犯罪等级**；次日最多无头案
- **日志**：`[RegisterWitness]` 后 `TransitionStage` 无 auto-assigned（`""` 哨兵生效）
- **异常**：次日事件嫌疑人=玩家 → 检查 `TransitionStage` Active 分支 `""` 哨兵（WorldEvent.cs:1531）

### T4 调停（Phase D，责任转移）
- **操作**：守卫追打随从时面向守卫按 F（行应替换为「调停」）
- **预期**：玩家冒泡「住手！他是我的人！」→ 守卫收刀 → 立即质问玩家 → 赔偿对话（开价/砍价/拒赔开战）；**次日事件嫌疑人=玩家**
- **日志**：`[Intervene] 调停：停战 + 嫌疑转移 + 质问玩家`
- **异常**：按 F 没反应 → `IsInterveneEligible`（守卫 TopSuspectAgent 须为友方随从）；守卫不收刀 → `AbortCurrentAction` 未触发 OnEnd 收刀

### T5 逮捕转押（Phase E，跨天，放最后）
- **操作**：随从犯法被守卫打（不调停）→ 随从被击晕 → 玩家离场
- **预期**：MissionEnd 提示「你的随从 {NAME} 被关进了 {SETTLEMENT} 的牢房」；随从从队伍消失；回村庄菜单「赎回随从」（罚金，付不起灰掉）/ 城镇城堡地牢救人；赎人后事件 Resolved
- **日志**：`[CompanionDetention] 登记被关随从` / `赎回`
- **异常**：没提示 → `TransferArrestedCompanionsToJail` 四条件（ArrestedByLaw+倒地+Hero 非空+队伍成员）逐个查，重点确认守卫设了随从脑 ArrestedByLaw（AgentBrain.cs:905）

### T6 大义灭亲（Phase F）
- **操作**：随从犯法后跟玩家跑了（Mission 内没被抓，嫌疑人已锁随从）→ 与权威 NPC 对话
- **预期**：「你的随从 {NAME} 偷了我的东西！」→ 三出口：交人（进牢房+事件了结）/ 替赔（NPC 开价）/ 拒认（好感 -10）
- **日志**：`[CompanionCrime] 大义灭亲` / `拒认代价`
- **边界确认**：交人后村庄牢房无赎回菜单（设计裁定）；town/castle 可原版地牢救人

### T7 HUD 意图行
- **操作**：随从执行计划途中看头顶 → 单条青蓝 14 号「执行计划中：前往目标」；玩家战斗/走远 → 「玩家战斗中」/「玩家走远了」；完成 → 行消失
- **异常**：两条行 → XML 残留；英文 → CN key 缺失

### T8 已知边界观察（不修，确认体验可接受）
- 无人目击偷窃 → 次日村庄失窃案（Emerging 无头案），对话有调查/悬赏话题
- 随从犯法但无人拉满（如友方看到）→ 隔日调查可能查回玩家（`TryLockSuspect` 用 InitiatorId=玩家）——**出戏则再设计 InitiatorId 方案**

### 测试记录

| 用例 | 日期 | 结果 | 备注 |
|------|------|------|------|
| T1 | | | |
| T2 | | | |
| T3 | | | |
| T4 | | | |
| T5 | | | |
| T6 | | | |
| T7 | | | |
| T8 | | | |

---

## Context

用户诉求（2026-08-13，四轮裁定合并）：

1. **HUD 意图显示**：现在有两个行——青蓝意图行（`NpcIntentDebugText`）+ 橙色计划摘要行（`PlanSummaryText`，互斥显示）。用户只要一条青蓝行、一个文本变量；执行计划时写「计划执行(步骤简述)」；当前计划摘要里还有硬编码中文（`PlanExecutor.CurrentSummary` 多处），且字号 12 可读性差。要求本地化（EN fallback + CNs 双份）。

2. **警戒体系**：随从犯法 → 目击者涨警戒值，但 (a) 视觉上警戒眼（白/黄/红）看不出「不是针对我」；(b) 体系只认玩家犯法（`WitnessCrime_GatherOnLook` 分类块被 `criminal == Agent.Main` 门控，L3 质问/参战恒对玩家——随从犯罪被目击拉满后会**质问玩家**，体验 bug）。本轮做「非玩家嫌疑犯 → 直接参战」闭环；非玩家警戒眼用**青蓝冷色系**（暖=针对我，冷=围观别人）。

3. **责任语义**：随从犯法**不自动认定玩家**——只有玩家主动**调停**（交互键介入，守卫才知道随从是玩家的）后责任才落玩家头上。不调停 = 随从被守卫打到 0 血（击晕即止）。全做：嫌疑转移 + 调停交互行 + 复用质问/赔偿对话链；未调停时 WorldEvent 嫌疑人**锁随从本人**（随从非 Hero 则 unknown）。

4. **逮捕与善后**：随从犯法被击倒、玩家离场 → 随从被**定居点俘虏**（从队伍扣除，复用原版 hero 俘虏机制）；随从犯法后跟玩家跑了 → 嫌疑人锁定随从，犯罪对话允许玩家**大义灭亲交出随从**。

5. **审查要求**：① 玩家犯案不能被误判成非玩家；② 随从犯法时玩家其他随从（友方）看到要兼容豁免。

铁律 13（玩家可见文本走 LWNTextHelper）、铁律 18（NPC↔NPC 无对话，质问只能对玩家；NPC 侧=AgentSay 冒泡）、铁律 12（每个对话出口有代价或检定）。

---

## 改动一：HUD 意图行统一（青蓝单行 + 本地化）

### 1.1 语言文件新增/修改 key（EN `ModuleData/Languages/std_LivingWorldNpcs_strings.xml` + CNs 同文件双份同步）

| key | EN | CN |
|---|---|---|
| `LWN_hud_plan_executing` | `Executing plan: {STEP}` | `执行计划中：{STEP}` |
| `LWN_plan_step_decision` | `Step {STEP}: decision received ({TYPE})` | `步骤 {STEP} 收到决策结果（{TYPE}）` |
| `LWN_plan_pause_modal` | `The player is busy` | `玩家在忙别的` |
| `LWN_plan_pause_fight` | `The player is in combat` | `玩家战斗中` |
| `LWN_plan_pause_far` | `The player is too far away` | `玩家走远了` |
| `LWN_plan_goal_done` | `Goal achieved` | `目标达成` |
| `LWN_plan_chaseback` | `Catching up to the player` | `追上玩家` |
| `LWN_plan_done` | `Done` | `完成了` |
| `LWN_plan_cancel_player` | `Called off by the player` | `玩家叫停` |
| `LWN_plan_step_talk`（改文案，去 {TEXT}） | `Talking` | `交谈中` |

复用已有：`LWN_plan_abort_down`（随从倒下）、`LWN_plan_goal_notmet`（事情没办成）。

### 1.2 PlanExecutor.cs 中文清理 + PauseReason 改 key 常量

`ExampleModVS/ExampleMod/ExampleMod/Planner/PlanExecutor.cs`

- 🔴 **PauseReason 是状态标识符**（307/312/320 行 `PauseReason == "玩家走远了"` 做 Resume 匹配），不能换本地化文本。改 key 常量：`PauseReasonModal="player_modal"` / `PauseReasonFight="player_fight"` / `PauseReasonFar="player_far"`；`Pause()`（1209 行）内按常量映射本地化后写 `CurrentSummary`。
- 539 行决策结果 → `ResolveCompound("LWN_plan_step_decision", ..., ("STEP", step.Id), ("TYPE", ev.Type))`
- 743 行 `Finish(Failed, "事情没办成")` → `PlanTexts.GoalNotMet`（已存在）
- 1246 行 `CancelByPlayer(reason = null)` → 默认 null，内部 `?? LWN_plan_cancel_player`
- 1380 行 `report ?? "完成了"` → `LWN_plan_done`
- 1457 行 → `LWN_plan_goal_done`；1581 行 → `LWN_plan_chaseback`；1572 行 → `PlanTexts.CompanionDown`（已存在）
- 102 行 `FinalizeExecutor("计划随场景结束而中止")`：message 参数未使用，不动

### 1.3 AgentHudVM.cs 合并单行

- 删 `ShowPlanSummary`/`PlanSummaryText` 属性对（547-561 行）与 UpdateLogic 计划块（123-137 行）
- UpdateLogic 合并为一个块（无互斥、消除顺序依赖）：

```csharp
var executor = PlanExecutor.GetExecutorFor(TargetAgent);
bool planActive = executor != null && !string.IsNullOrWhiteSpace(executor.CurrentSummary)
    && !TargetAgent.IsMainAgent && !Settings.Instance.IsInteractionDisabled();
if (planActive)
    NpcIntentDebugText = LWNTextHelper.ResolveCompound("LWN_hud_plan_executing", "Executing plan: {STEP}", ("STEP", executor.CurrentSummary));
else
{
    var brain = AgentAIController.GetBrainForAgent(TargetAgent);
    var intent = brain?.CurrentIntent;
    NpcIntentDebugText = (intent != null && intent.Type != NpcIntentType.None) ? intent.ToString() : "";
}
ShowIntentDebug = Settings.Instance.ShowNpcIntent && !TargetAgent.IsMainAgent
    && !Settings.Instance.IsInteractionDisabled() && !string.IsNullOrWhiteSpace(NpcIntentDebugText);
```

- 164 行名字总领规则删 `|| ShowPlanSummary`；229 行 UpdateFrame 兜底删 `&& !ShowPlanSummary`
- 行为变化（有意）：计划行并入后受 `Settings.ShowNpcIntent` 开关门控（一行一开关，语义统一）

### 1.4 AgentHudMissionView.cs + XML

- `AgentHudMissionView.cs:225`：删 `hud.ShowPlanSummary = false;`
- `GUI/Prefabs/AgentHudNearby.xml`：删橙色 RichTextWidget（73-80 行）；青蓝行 `Brush.FontSize="12"` → `"14"`（颜色 `#00FFBFFF` 已合法 9 字符）

---

## 改动二：警戒 suspect 化 + 视觉区分 + 任何人犯法闭环 + 调停/逮捕/大义灭亲责任链

### 2.1 AlertEntry 加 SuspectAgentIndex（Phase A）

`AI/AlertTypes.cs`：`AlertEntry` struct 加 `public int SuspectAgentIndex = -1;`（**必须字段初始化器**——`new AlertEntry()` 时 int 默认 0 会被误判为某 agent 的 Index；LangVersion 12 支持）。非持久化结构，无存档兼容问题。

`AI/AgentBrain.cs`：
- `SetPulseTarget`（1424 行）签名加可选参 `int suspectAgentIndex = -1`（既有调用点零改动）
- 新增：`int TopSuspectAgentIndex`（值最大条目的 suspect，-1=未知）、`bool AlertTargetIsPlayer => TopSuspectAgentIndex < 0 || TopSuspectAgentIndex == Agent.Main.Index`、`Agent TopSuspectAgent()`（复用 `FindAgentByIndex`）、`RemapSuspectToPlayer()`（调停用：顶条目 suspect → Agent.Main.Index）、`bool ArrestedByLaw`（逮捕标记，Phase E）

### 2.2 随从犯罪站点传 suspect（Phase A）

- `Planner/InlineSteps.cs:696`：`SetPulseTarget(Steal, ..., suspectAgentIndex: _agent.Index)`（作案随从；顺带在注释标注 targetName 传目击者自己的名字是遗留笔误，仅日志用途，不改动）
- `Planner/ReactiveAgent.cs:621`（alert_raise）：`SetPulseTarget(AttackAlly, null, null, -1, requester?.Index ?? -1)` + 注释「requester = 事件源、非必然犯罪者」（requester=null 回落 -1 = 玩家语义，与现状一致）

### 2.3 WitnessCrime 分类块泛化 + 犯罪广播标记（Phase C）

🔴 **防回归**：`AIEvent` 加 `public bool IsCrime = true;`；`AgentAIController.BroadcastEventInRange` 加可选参 `bool isCrime = true` 透传到 WitnessCrime 舞台分配（656/669 行）。**仅两个调用点传 false**：`InlineSteps.cs` make_noise（371-374 行，随从喊一嗓子不能算犯罪）、`AgentBrain.cs:993-994`（NPC 投降广播）。其余广播点默认 true。

`AgentBrain.cs`：
- `WitnessCrime_GatherOnLook` 分类块门控（681 行）`if (criminal == Agent.Main)` → `if (criminal != null && criminal != Owner && aiEvent.IsCrime)`；三处 `SetPulseTarget` 追加 `suspectAgentIndex: criminal.Index`（玩家作案时 == Agent.Main.Index，判定自然成立）
- 730 行受害者直指块保持 `criminal == Agent.Main` 不动（受害者路径的警戒走本人脉冲，其 BecomeAlarmed 走新 suspect 分支参战，闭环成立）
- `BecomeAlarmed`（870-897 行）**suspect 分支必须插在 `CombatManager.IsPlayerInCombat` 检查之前**（否则玩家碰巧在战斗时随从犯罪会被抢走参战玩家）：

```csharp
if (_pulseSuppressedUntil > 0 && Mission.Current?.CurrentTime < _pulseSuppressedUntil) return;
if (IsCurrentOrPending<FightEnemyAction>()) return;

Agent suspect = TopSuspectAgent();   // 从 _alertBreakdown 顶条目推导，不依赖易被覆盖的 InteractedAgent
if (suspect != null && suspect != Agent.Main)
{
    BubbleSay(LWNTextHelper.ResolveCompound("LWN_brain_crime_shout", "Stop, {NAME}!", ("NAME", suspect.Name.ToString())), "seen_crime", suspect);
    ArrestedByLaw = true;            // 守卫执法语义 = 逮捕标记（Phase E 用）
    StartCombatAgainst(suspect);
    return;
}
if (CombatManager.IsPlayerInCombat) { StartL3CombatJoin(); return; }
if (Settings.Instance.AlarmedDirectCombat) { StartL3CombatJoin(); return; }
StartL3Confrontation();
```

- `BecomeCautious`（862-867 行）跟随目标 → `TopSuspectAgent() ?? Agent.Main`（站位劝阻=无对话 UI，铁律 18 允许；随从犯罪时对着随从喝止）
- 新 key：`LWN_brain_crime_shout` — EN `Stop, {NAME}!` / CN `站住，{NAME}！`

**闭环自动覆盖**：随从击晕（KnockoutFlow.cs:107-111，criminal=随从）泛化后自动走 Knockout 3.0 + suspect=随从 → 参战，零额外改动。

### 2.4 友方豁免泛化（Phase C + 用户点 2）

`AddAlert`（1380 行）豁免条件扩展：`IsFriendlyToPlayer(Owner) && !IsVictimFriendlyPulse(type) && !IsSuspectHostile(type)` → 豁免。新增 `IsSuspectHostile(type)`：entry 的 suspect 存在且**非**玩家友方 → true（不豁免）。语义矩阵：suspect 未知/玩家/随从（友方）→ 豁免（随从代表玩家阵营，**玩家其他随从看到随从犯法 → 不涨警戒**）；suspect=非友方 NPC → 不豁免（任何人犯法）。与 IsVictimFriendlyPulse OR 关系。

`WitnessCrime_GatherOnLook` 围观门控（672 行）同步扩展：`if ((criminal == Agent.Main || FriendlinessHelper.IsFriendlyToPlayer(criminal)) && IsAllyBystander(victim)) return;`

🔴 **验证点**：实机确认 `FriendlinessHelper.IsFriendlyToPlayer` 对玩家随从返回 true；若 false → `IsSuspectHostile` 改用 `brain.Leader == Agent.Main` 或玩家阵营判定（实现时验证后决定，不硬猜）。

### 2.5 警戒眼双色（Phase B）

- `AgentHudVM.cs`：加普通属性 `public bool AlertTargetIsPlayer { get; set; } = true;`（**不加 [DataSourceProperty]**，参照 AlertValue 模式；bool 非 Color 无初始化崩溃风险）；`UpdateAlertVisuals` 分两套色系（全部 9 字符 `#RRGGBBAA`）：

| 档位 | 针对玩家（暖=危险，现状） | 非玩家（冷=围观） |
|---|---|---|
| ≤1 | 底 `#FFFFFFFF` / 填 `#FFD700FF` | 底 `#FFFFFFFF` / 填 `#00FFBFFF` |
| ≤2 | 底 `#FFD700FF` / 填 `#FF0000FF` | 底 `#00FFBFFF` / 填 `#0040D0FF` |
| >2 | 底 `#FF0000FF` / 填 `#FF0000FF` | 底 `#00FFBFFF` / 填 `#00FFBFFF` |

- `AgentHudMissionView.cs`（160-164 行）注入顺序**先 target 后 value**（AlertValue setter 内部触发 UpdateAlertVisuals）：`hud.AlertTargetIsPlayer = brain?.AlertTargetIsPlayer ?? true;` → `hud.AlertValue = ...`

### 2.6 责任语义：嫌疑转移 + 调停交互（Phase D）

**流程总览**：随从犯罪被目击 → 目击者警戒（suspect=随从）→ Alarmed 打随从（Phase C）→ 玩家二选一：
- **不调停** → 随从被击晕（0 血即止）→ WorldEvent 嫌疑人=随从 Hero（SuspectIsPlayer=false → 无犯罪等级、无玩家问责，村庄次日知道「XX 偷了东西」）；离场后随从被关进定居点（Phase E）
- **调停**（面向执法守卫按 F，行替换 Talk）→ 玩家冒泡「住手！他是我的人！」→ 守卫停战收刀 → 嫌疑转移到玩家（WorldEvent + breakdown）→ 守卫走现有 L3 质问链（`StartL3Confrontation` → `AlertForceConversationAction` → `BuildAlertInterceptScript` + `BuildRestitutionSubtree` 赔偿），复用 ConfrontingBrain 锁/赔偿计算/拒赔开战分支

#### 2.6.1 WorldEvent 嫌疑转移（🔴 2026-08-14 定案：嫌疑人单一事实源，见文件头修正记录）
- **嫌疑人来源只有两条**：目击者脑内（`RegisterWitness` 在 Alarmed 时自动调用，从 `brain.TopSuspectAgent()` 三态推导：null=玩家 MainHero / Hero=随从 StringId / `""` 哨兵=无名 unknown）与玩家自首（`ConfessIntent`）
- `RegisterTheftWitnesses` **纯记账**（只写赃物证词，不推进阶段、不锁嫌疑人——原「加 suspectHeroId 参数」方案废弃，C# 默认参数与显式 null 无法区分，模板随从会回落玩家）
- `FinalizePendingWorldEvent` 删「有真目击 → 写死玩家 Active」兜底：无人拉满 → Dormant 入档 → 过夜 Emerging 无头案；证词只作调查资产（有真目击 → InvestigationProgress=1.0）
- `TransitionStage` Active 分支三态：非空=锁定 / null=InferSuspect（既有兜底）/ `""`=显式 unknown 跳过推断
- **调停时转移**：`WorldEventStore.TransitionStage(pending, EventStage.Confrontation, Hero.MainHero?.StringId)`（Confrontation 阶段接受嫌疑人更新；SuspectIsPlayer 由此置真 → 后续赔偿/犯罪等级链正常）

#### 2.6.2 调停交互行
- `Input/ModInput.cs`：`public const string Intervene = "Intervene";`
- `Core/Settings.cs` `DefaultInteractions`：默认 `Keyboard="F", Gamepad="Y", PressMode="Short"`（与 Talk 同键——**上下文互斥替换**，永不共存，无冲突警告）
- `InteractionMissionView.cs` 面向分支（~734 行）：条件 `brain.AlertTargetIsPlayer == false && TopSuspectAgent() 存在且玩家友方且非玩家本人` 时**用 Intervene 行替换 Talk 行**（仿 `PlanCommandFlow.IsActiveFor` 互斥写法）
- `ExecuteInteraction` 加 `case InteractionIds.Intervene:` → `ExecuteIntervene(当前聚焦 agent)`

#### 2.6.3 调停行为 `ExecuteIntervene(guard)`
1. 守卫停战：`guardBrain` 当前动作 `RequestInterrupt()` + `ClearAllActions()`（FightEnemyAction.OnEnd 自带收刀）；清 `ArrestedByLaw`
2. 玩家冒泡：`AgentHudMissionView.AgentSay(Agent.Main, LWN_ui_intervene_bubble)`（头顶气泡反馈明确，铁律 13）
3. 嫌疑转移：`RemapSuspectToPlayer()`（breakdown 顶条目 suspect → Agent.Main.Index）；PendingWorldEvent `TransitionStage(Confrontation, MainHero)`
4. 守卫质问：`guardBrain.StartL3Confrontation()`（internal 改可调）——现有链：Follow(player)+LookAt+AlertForceConversationAction → 原版对话流注入质问脚本 → 赔偿子树
- 叙事闭环：玩家公然护人 → 守卫推理「随从是玩家的」→ 质问玩家（目击者推理，非上帝视角）
- 边界：不做「否认」对话分支——调停=认领；不调停=随从挨揍；质问对话内拒赔 → 既有开战分支（守卫打玩家）

#### 2.6.4 新 key（EN + CNs 双份）
| key | EN | CN |
|---|---|---|
| `LWN_ui_interact_intervene` | `Intervene` | `调停` |
| `LWN_ui_intervene_bubble` | `Stop! He's my man!` | `住手！他是我的人！` |

### 2.7 玩家路径与友方旁观审查（用户点 1，audit 清单）

**玩家犯案不能误判非玩家**（全站 suspect 来源审查，实现时逐点 grep `SetPulseTarget` 验证）：
- 持续源（蹲下/拔刀/偷窃 UI，1204-1223 行）：不传 suspect → -1 → `AlertTargetIsPlayer=true` ✅
- 玩家偷窃路径（StealBarVM/StealManager）：不传 suspect → -1 → 玩家 ✅
- 玩家攻击事件（PlayerAttackedAlly/PlayerAttackedCivilian，damaged 链 575/630/649 行）：不传 suspect → -1 → 玩家 ✅
- 分类块泛化后（2.3）：`suspectAgentIndex: criminal.Index` ——玩家犯罪时 criminal == Agent.Main → suspect=玩家 index → `AlertTargetIsPlayer` 自然成立 ✅
- 新增 site 一律默认 -1（=玩家语义，注释写明）

### 2.8 Phase E：随从逮捕（Mission 层闭环 + 原版俘虏机制，用户点 3）

**语义**：随从犯罪被目击 → 守卫参战（Phase C，`ArrestedByLaw=true`）→ 随从被击倒 → 玩家**不调停**离场 → MissionEnd 时随从被**定居点俘虏**（原版 hero 俘虏机制），从玩家队伍扣除。玩家可回定居点赎人/救人。

**复用轮子**：
- `TakePrisonerAction.Apply(settlement.Party, Hero)` — 原版俘虏 API（`PlayerDetentionBehavior.StartJail` 755 行已用）：hero 自动进 settlement 的 PrisonRoster、从原队伍移除、原版 captivity 状态机接管
- `CampaignEvents.HeroPrisonerTaken/HeroReleased` 监听已有（MyBehavior 56/66 行，目前只广播玩家事件；随从走原版释放流）
- `SettlementMenuIdOf`/`SETTLEMENT_MENUS` 菜单注入模式（村庄柴房无原版地牢 → 自定义「赎回」选项）
- 时机：MissionEnd 后、Campaign tick 前（仿 PlayerDetentionBehavior 注释：源头防生成）

**实现**：
1. **逮捕标记**（Phase C 已加）：守卫 `StartCombatAgainst(suspect)` 前设 `suspect brain.ArrestedByLaw = true`（守卫执法语义 = 逮捕而非私刑）
2. **转押**：`AgentAIController.OnMissionEnd`（CombatManager.OnMissionEnd 旁，287 行）遍历 brain：`ArrestedByLaw && !Owner.IsActive()`（被击倒）→ 查 `WorldEventStore.FindOnGoing(settlementId)` 找到事件 → `TakePrisonerAction.Apply(evt.TargetSettlement.Party, companionHero)`；**随从 Hero 非空才执行**（模板 NPC 随从无 Hero → 跳过，仅 Mission 层倒地）；事件保持 Active（嫌疑人=随从）
3. **释放路径**：城镇/城堡 = 原版地牢交互（玩家进地牢救人，原版 dungeon 机制，实现时验证交互入口）；村庄 = 仿 `lwn_detention_release` 在 `SETTLEMENT_MENUS`（village/town/castle）注入「赎回随从」选项：`CrimePenaltyCalculator.ComputeCost(evt, Restitution)` 定价 → `AgentControlHelper.TransferGold` 扣钱（归口铁律 4）→ `PrisonRoster.RemoveTroop` 释放 + 事件 Resolved
4. **提示**：转押时玩家可见消息「你的随从 {NAME} 被关进了 {SETTLEMENT} 的牢房」（`LWN_ui_arrest_msg`，铁律 13）
5. **边界**：随从真死 → hero 死亡系统接管，不逮捕；玩家调停救下 → 不逮捕（嫌疑已转玩家）

### 2.9 Phase F：大义灭亲对话（嫌疑人=随从的犯罪对话，用户点 4）

**语义**：随从犯法后**和玩家一起跑了**（Mission 内未被抓）→ WorldEvent 嫌疑人锁定随从 → 玩家与受害者/权威 NPC 对话时出现「随从犯法」话题 → 玩家可选**交出随从**（大义灭亲：随从被关进定居点牢房 + 事件 Resolved）/ 替随从赔钱 / 拒不认账。

**现状**：`CrimeDialogueBuilder.NeedsEarlyInjection`（232 行）与 `skipOpening`（299 行）均门控 `evt.SuspectIsPlayer`——嫌疑=随从的事件**不会触发现有犯罪对话**，需要新分支。

**实现**：
1. **触发扩展**：`NeedsEarlyInjection` 增加 `|| (evt.Stage == Active && evt.SuspectHeroId 是玩家队伍随从 Hero)`；`ConversationEntryPatch` 触发路径不动（复用注入管道）
2. **新脚本分支**：`CrimeDialogueBuilder` 加 suspect=companion 变体（`BuildCompanionCrimeScript`）：
   - 开场（守卫/受害者）：「你的随从 {NAME} 偷了我的东西！」（LWN key）
   - 选项 A 交出随从（大义灭亲）：`TakePrisonerAction.Apply(settlement.Party, companion)` + 事件 Resolved + 阵营好感影响
   - 选项 B 替随从赔钱：`BuildRestitutionSubtree` 复用（赔款从玩家金库扣，`AgentControlHelper` 归口）
   - 选项 C 拒不认账：关系惩罚 + 可能升级（复用 Threat/拒赔→开战既有分支）
   - 铁律 12：每个出口有代价（交人=损失随从、赔钱=资源、拒认=关系/战斗）
3. **入口**：原版对话流注入（与现有犯罪对话同管道）；对话目标 = 事件权威 NPC（`WorldEventStore.GetAuthorityNpc`）
4. **新 key**：`LWN_dialogue_companion_crime_*` 系列（开场/三选项/结果提示），EN+CNs 双份

---

## 不做的事（边界）

- **调停「否认」分支**：调停=认领责任；玩家想撇清 = 不调停，随从挨揍（后果承担，铁律 12 出口有代价）
- **damaged 链泛化**（随从 order_attack 当街斗殴无旁观者警戒）：高频事件、改动面大（护主链共存）、且涉及「村民互殴是否也触发」的语义决策——留待后续单独设计
- 不手改 `release/` 打包产物；`plans/待办-planner-executor-queue-unify.md` 顺手加注即可

## 验证

1. `cd ExampleModVS/ExampleMod && dotnet build ExampleMod.sln`（Debug）
2. grep 归零：`ShowPlanSummary|PlanSummaryText` 全仓库无代码残留
3. grep 复核：`SuspectAgentIndex` 覆盖全部 SetPulseTarget 站点；`IsCrime` 仅两个调用点传 false；`ArrestedByLaw` 设置点 = Phase C 参战分支 + 调停清除
4. 语言文件新 key EN/CN 双份成对；颜色全 9 字符 `#RRGGBBAA`
5. 实机（用户）：
   - 随从偷窃被抓 → 守卫眼变青蓝系、Alarmed 后对随从拔刀冒泡「站住，XX！」、**不再质问玩家**；玩家其他随从在场围观不涨警戒（友方豁免）
   - 玩家自己偷窃/拔刀 → 眼保持黄/红系、质问玩家不变（**玩家路径无 suspect 误标**）
   - 随从被守卫打时面向守卫按 F → 行替换为「调停」→ 玩家冒泡 → 守卫收刀质问玩家 → 赔偿对话（赔钱/拒赔开战）→ 次日事件嫌疑人=玩家
   - 不调停、随从被击倒、玩家离场 → MissionEnd 后随从被关进事件定居点（提示消息）→ 玩家回城镇/城堡地牢救人（原版）或村庄菜单「赎回随从」（罚金）
   - 随从犯罪后跟玩家跑了 → 与受害者对话出现「随从犯法」话题 → 交出随从（进牢房+事件了结）/ 替赔 / 拒认
   - 意图行单条青蓝 14 号、执行计划显示「执行计划中：前往目标」；暂停显示「玩家走远了」（本地化）

## wheels 登记

- `plans/rules/wheels.d/ui.md`：五元素表（意图行并入计划分支 + AlertTargetIsPlayer 双色）、名字总领规则三处联动更新、AgentHudVM 关键属性列表
- `plans/rules/wheels.d/agent.md`：警戒值系统章节补 SuspectAgentIndex/AlertTargetIsPlayer/任何人犯法闭环（IsCrime 标记、BecomeAlarmed suspect 分支、AddAlert 豁免泛化、ArrestedByLaw 逮捕标记）
- `plans/rules/wheels.d/planner.md`：接线清单（127 行）改「合并后单行」+ 文本本地化条目
- `plans/rules/wheels.d/input.md`：调停交互行（Intervene 替换 Talk 的上下文互斥范式）
- `plans/rules/wheels.d/dialogue.md`：大义灭亲对话（嫌疑=随从的犯罪对话分支）
- `plans/rules/wheels.d/worldevent.md`：随从逮捕链（TakePrisonerAction 转押 + 村庄赎回菜单）

## 附录：扛人表现 — 技术可行性调研（用户追加，仅调研不实现）

反编译 `TaleWorlds.Engine.dll` / `TaleWorlds.MountAndBlade.dll`（v1.4.8 实机）结论：

**① Ragdoll 布娃娃 —— 原生支持 ✅**
- `Agent.StartRagdollAsCorpse()` / `EndRagdollAsCorpse()` / `IsAddedAsCorpse()` / `AddAsCorpse()`
- `Agent.ApplyForceOnRagdoll(sbyte boneIndex, in Vec3 force)` — 对布娃娃指定骨骼施力（击飞/尸体物理）
- `Agent.SetVelocityLimitsOnRagdoll(float linearLimit, float angularLimit)`
- ragdoll 骨骼配置在 AgentBuildData 类（`IndicesOfRagdollBonesToCheckForCorpses` / `RagdollFallSoundBoneIndices` / `SpineLowerBoneIndex` / `SpineUpperBoneIndex` 等，骨骼索引 sbyte）
- ⚠️ ragdoll = native 物理模拟（受重力/碰撞），强制 frame 会与物理对抗——扛人**不能**直接对 ragdoll 写位置

**② Attach 腰部挂载 —— 原生无 attach-to-bone 接口 ⚠️**
- `AttachTo` 全库仅 `Agent.TryAttachToFormation()`（阵型，无关）；`GameEntity.AddChild(entity, autoLocalizeFrame)` 是空间父子关系，无骨骼挂点语义
- **可行构件**：
  - `GameEntity.GetGlobalFrame()/SetGlobalFrame(in MatrixFrame, bool isTeleportation)` — 实体全局 frame 读写
  - `Skeleton.GetBoneIndexFromName(skeletonModelName, boneName)` + `Skeleton.GetParentBoneIndex`
  - `GameEntity.CreateSkeletonWithActionSet(this GameEntity, ref AnimationSystemData)`（MB 扩展方法）— 任意实体建可播放骨骼
  - `Agent.SetVisible(bool)` / `SetVisibleWithAllSynched` — 隐藏原 agent 视觉
- **推荐实现路径（视觉替身 + 每帧 frame 同步，后续扛人功能时做）**：原 agent `SetVisible(false)`（保持原地 ragdoll 或直接隐藏）→ 新建子 GameEntity 挂玩家 entity 下 → `CreateSkeletonWithActionSet` + 被扛者骨骼模型（`CharacterObject`/Monster 的 skeletonModel，实现时验证）→ 每帧把子实体 `SetGlobalFrame(玩家骨骼挂点 GetGlobalFrame)`（腰部 = SpineLower/SpineUpper bone index）→ 「扛着昏迷者」表现。被扛者数据状态（昏迷/死亡）仍由原 agent 脑持有
- 挂点骨骼 frame 查询 API（Skeleton 的 bone global frame）实现时再反编译确认（Skeleton 类含 `GetGlobalFrame()` 至少 entity 级；bone 级查询以实机 ilspycmd 为准）

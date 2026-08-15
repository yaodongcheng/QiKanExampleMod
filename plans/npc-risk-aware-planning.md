# 随从风险感知与思考可见性（Risk-Aware Planning & Think-Aloud）

> 状态：设计稿（2026-08-14）。对应实机日志 `Debug/StoryEngine_RuntimeLog.txt`（吕卡隆酒馆三连实机）。
> 关联轮子：[[wheels.d/planner.md]]（计划管线/击晕单管线/扒窃绕背）、[[wheels.d/llm.md]]（prompt 单一事实源）、[[wheels.d/im.md]]（IM 回复管线/决策卡片）。

## 一、三个实机问题（本设计要解决的事）

### P1 赎回随从后不归队（玩家在队伍里看不到人）

**日志**：赎回流程表面全绿（付 50 → 事件 Resolved → 播报「领了回来」），但 party 里没人。

**反编译证据链**（TaleWorlds.CampaignSystem.dll）：

1. `Hero.OnAddedToPartyAsPrisoner(party)` → `PartyBelongedToAsPrisoner = party; **PartyBelongedTo = null**` —— 随从入狱瞬间就被引擎从玩家队伍摘除；
2. `TroopRoster.RemoveTroop` → `AddToCountsAtIndex(-1)` → `OwnerParty.OnHeroRemoved` → `hero.OnRemovedFromParty` → `PartyBelongedTo = null` —— **RemoveTroop 只是计数操作，永远不会"自动回原队伍"**（`CompanionDetentionBehavior.cs:25` 的注释是错的）；
3. 官方释放语义 = `EndCaptivityAction.ApplyByRansom(hero, facilitator)`（`ApplyInternal`：RemoveTroop → `ChangeState(Hero.CharacterStates.Released)` → 玩家随从走 `MakeHeroFugitiveAction` 潜逃回队 → `OnHeroPrisonerReleased` 广播）。

**根因**：本 mod 赎回结算（`CompanionDetentionBehavior.cs:259`）裸调 `PrisonRoster.RemoveTroop`，缺 `ChangeState(Released)`、缺归队/潜逃流程 → 随从卡在"囚犯"状态，永远游离在队伍之外。

**修复（M1）**：见下。

### P2 随从偷窃被目击中断，全程零反馈

**日志**：随从蹲下 → 8 名在场者目击（吟游诗人×2/赎金经纪人/赌徒/镇民×4）→ `_resultKey="interrupted"` → 计划 `Finish(Succeeded, null)` **静默**。

**根因三层**（`InlineSteps.cs` StealAttemptInlineState + `ChatActionFlow.cs`）：

1. 聊天单步计划收尾静默（`ChatActionFlow` 设计注释自认：`Finish(Succeeded, null)` 无密信无报告，不刷屏）——但**偷窃是有结局的判定型动作**，不该静默；
2. steal_attempt 结算出口（Settled 阶段）只 `SetStepResultKey` + 收姿，无玩家可见播报。对照击晕：2026-08-13 已补 `ReportResult`（成功/失败带 ROLL/THRESHOLD 红字）——**偷窃漏了这同一类修复**；
3. 聊天单步计划只有 `steal_attempt` 一步、**没有 `give_gold` 步骤**——偷成功赃款只记在执行器 `_stolenGold` 字段，永不转给玩家（`GiveInlineState` 是唯一消费点）。聊天路径偷窃 = 半截执行（且成功还写暗账 `RegisterUnwitnessedTheft`，账目声称偷了但钱没动，守恒违例）。

### P3 随从决策是"盲的"：prompt 里没有真实危险感知

**日志**：随从拒绝时说「这店主是本地有头脸的人物，光天化日之下动手，怕是要惹来祸事」——这是 LLM 常识泛化（知道偷东西危险），不是场景感知（**店里 27 人、8 个盯着他蹲下、店主身边全是人**，它一概不知）。

**现状盘点**：

| 拼图 | 状态 |
|---|---|
| 场景数据 | ✅ `SceneSnapshot` 完整存在（人员/位置/朝向/状态/职业 + 可见性矩阵 `CanSee` + 语义区域），`ToPromptText()` 渲染「【场景当前人员】（N 人）」 |
| 战斗对比 | ✅ `AgentStatsHelper.GetAgentStats`（vigo/control）+ `KnockoutFlow` 检定公式（随从上限 85%） |
| 引开机制 | ✅ `make_noise` 已存在（喊叫 + WitnessCrime 围观聚集，`InlineSteps.cs:398`）——"先引开再偷"执行层零新代码 |
| 计划语法 | ✅ `make_noise → move_to → wait → steal_attempt → give_gold` 全在 PlanGrammar/InlineSteps |
| **决策输入** | ❌ 聊天动作路径的 LLM prompt 只有【此刻处境】（地点+玩家距离，`WorldFactProvider.BuildSceneAwareness`）。**场景快照只喂给密令路径**（`need_plan=true` 玩家点「制定计划」按钮 → `BuildPlanPrompt(snapshotText)`），闲聊动作路径（本次走的路）完全盲 |

## 二、设计铁律（用户裁定 2026-08-14）：思考必须可见（Think-Aloud）

> **任何随从的思考/决策节点，都必须有玩家可见的反馈出口。禁止黑箱决策。**

随从"想"的过程逐节点对照表（M1-M5 全部按此检查）：

| 思考节点 | 可见出口（现状/新增） |
|---|---|
| **风险审视（每次命令）** | 随从 IM 消息/头顶冒泡说出**判断依据**（"店主身边围了四五个人，我一下手准被看见"）——**新增 M4**，risk_analysis 字段直出随从台词 |
| **稳健方案** | plan_needed 时直接出计划卡片（含撤退步骤/ask_help 配合，玩家批准后执行）——**新增 M4/M6** |
| **风险告知** | risky 时风险讲透（台词）+ 决策卡带风险摘要 → 玩家确认后坚定执行——**新增 M4** |
| **分头配合** | 计划讲解显示「让阿速甘引开人群」+ B 冒泡「交给我」+ 完成回执——**新增 M6** |
| 计划生成 | 计划卡片 + 人话讲解（已有） |
| 计划执行中 | 执行摘要 HUD（已有 `AgentHudVM` 计划行） |
| 执行中途调整/中止 | 密信/当面报告（已有 `Finish` 双通道） |
| **动作结局（偷/击晕成败）** | 播报（击晕已有 `ReportResult`；偷窃 **新增 M2**） |
| 决策被玩家确认/拒绝 | 决策卡片（已有） |

**检查纪律**：设计评审每个节点时问「玩家这一刻能看出随从在想什么/在做什么吗？」，答不上来 = 不过。

## 二·五、思考深度论证（2026-08-14 用户质询：要不要 harness 式多步思考 / reasoning 模式）

**结论：不做决策期多步循环（harness/Codex 式）；按风险分级深度；reasoning 默认关、L1 留开关。**

**为什么多步循环是伪需求**——harness/Claude Code 多步的核心动因是"用工具发现世界状态"（读文件/跑命令/看结果）。本 mod 的信息获取已由 C# 完成，LLM 无信息缺口：

| harness 工具调用 | 本 mod 对应物 |
|---|---|
| `ls`/`grep`/`Read`（探索世界） | `SceneSnapshot.Build`（C# 已采集全部在场者/位置/朝向/状态） |
| `bash`（查运行时状态） | `CanSee` 视线矩阵 + `AgentStatsHelper` 战力 + 警戒系统 |
| 观察结果（反馈） | 直接拼成【目之所及】段注入 prompt |

**一步结构化思考已够深**：`risk_analysis → verdict → plan` 单 JSON 内强制思考序（先看目标→再看自己→想后果→找稳法→下结论）= 显式 chain-of-thought，且正好满足 think-aloud。**思考的深 = 输入完整 + 纪律强制，不 = 轮数多。**

**多步循环的代价**（所以不做）：延迟 ×2-3（聊天场景 6-12s 出戏）、成本 ×2-3、flash 级模型多步误差累积 + 每步 JSON 崩坏风险翻倍（铁律 2 防御成本）、think-aloud 被隐藏推理稀释。

**真正的回环点在执行期，且已存在**：`PlanExecutor.Replan`（额度 ≤2）+ `on_timeout`/`abort`（"拖太久先撤"）——执行结果 = 真实反馈，这是最有价值的"干→看结果→调整"循环，不新建决策期循环。

**Token 预算**：风险审视轮 ≈ 1500（现状 IM 回复）+ 300（目之所及）+ 400（风险纪律）≈ 2500 输入 + 400 输出/次——flash 毫无压力；真正贵的是多次串行（3000×N），单次长度不是问题。

**深度分级（渐进，避免每次命令都上重思考）**：

| 级 | 覆盖 | 深度 |
|---|---|---|
| L0 | 低风险动作（move_to/emote/follow/蹲站……） | 现状一轮回复直发，不注入风险段、不烧推理 |
| L1 | 高危动作（偷/击晕/打/抓/抢……）与命令 | 风险审视（M4 全流程） |
| L2 | 多步复杂任务（need_plan 语义） | 既有 `BuildPlanPrompt` 管线 + 风险段，一次生成完整计划 |

**reasoning 模式**：现状全链路 `reasoning_effort:"none"`。默认保持——开 reasoning 的代价是思考进隐藏区、`risk_analysis` 变薄（think-aloud 冲突）+ 延迟暴涨；收益在 flash 级模型未验证。**实验性开关**（Settings 可配，默认关）：对 L1 轮单独 `reasoning_effort:"low"`，先验证 deepseek-v4-flash 该参数真实生效（日志已传参但无效果记录）再定。启用时 max_tokens 预留推理区（输出预算 350 → 1000+）。

## 三、改动清单

### M1 赎回后随从归队 — `WorldEvent/CompanionDetentionBehavior.cs`

**改动**：`RansomItemOnConsequence` 的释放段（现 258-260 行）替换为官方语义 + 立即归队：

```csharp
// 官方释放序列（EndCaptivityAction 语义，反编译实锤）：ChangeState(Released) 必须，否则英雄卡 Prisoner 状态
try { hero.ChangeState(Hero.CharacterStates.Released); } catch { }
settlement.Party.PrisonRoster.RemoveTroop(hero.CharacterObject, 1);
// 立即归队（交付感：玩家赎回 = 当场领人；不走原版 fugitive 潜逃数日）
if (hero.PartyBelongedTo == null)
{
    MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, 1);
    // AddToCounts 走 OnHeroAdded → PartyBelongedTo = MainParty（反编译确认）
}
```

- **候选替代方案**（记录不采用）：官方 `EndCaptivityAction.ApplyByRansom` 全流程 —— 玩家随从走 `MakeHeroFugitiveAction` 潜逃回队，要好几天地图漫游，赎金买的是"人还没回来"，交付感差（KCD2 水准不合格）。采用「官方状态 + 显式归队」。
- 🔴 `ChangeState` 有英雄状态机校验风险：先反编译 `Hero.ChangeState` 确认 `Prisoner → Released` 合法（若校验严格则改走 `EndCaptivityAction.ApplyByReleasedByChoice(hero)` + 归队，二选一实现期定）。
- **验证**：赎回后立即打开 party 界面（队伍 roster 出现该随从）；`hero.PartyBelongedTo == MobileParty.MainParty`；`hero.State == Released`。

### M2 偷窃结局播报 + 聊天路径赃款移交 — `Planner/InlineSteps.cs` + `Planner/ChatActionFlow.cs` + `Planner/PlanExecutor.cs`

**M2a 结局播报**（对齐击晕 `ReportResult`，`StealAttemptInlineState.Settled` 出口前补）：

| resultKey | 玩家可见文案（LWN key） | 颜色 |
|---|---|---|
| `success`（钱） | 「{NAME} 从 {TARGET} 身上摸到了 {AMOUNT}{GOLD}」 | Green |
| `success`（装备，M7 卸装备用） | 「{NAME} 从 {TARGET} 身上摸走了 {ITEM}」 | Green |
| `empty` | 「{NAME} 没在 {TARGET} 身上摸到东西」 | Gray |
| `impossible` | 「{NAME} 绕不到 {TARGET} 背后，没法下手」 | Gray |
| `interrupted` | 「{NAME} 被人看见，没敢下手，先撤了」 | Yellow/Red |

播报纪律（铁律 17）：roll 失败（empty）带 `掷点 {ROLL} vs 门槛 {THRESHOLD}`——玩家要看到败在哪（与击晕失败同款信息量）。roll 前中断（interrupted）无 ROLL（没到判定环节）。

**M2b 聊天路径移交（`ChatActionFlow.TryExecute` 对 `steal_attempt` 自动补尾）**：

```csharp
// 聊天单步计划缺尾步骤，赃物到不了玩家手上——按目标类型处理（2026-08-14 二轮审视修正）：
//   模板 NPC 目标（无 Hero）：StealPurseGold 内部当场守恒移交（金库→玩家）→ 无尾步骤
//   Hero 目标：补 give_gold(stolen) 尾步骤（give_gold 时 TransferGold 个人钱包）
if (actionCode == "steal_attempt" && StolenSourceIsHero)
    plan.Steps.Add(new PlanStep { Id = "chat_2", Action = "give_gold", Amount = JToken "stolen" });
```

- 🔴 防双移交：移交成功后置标记（`_goldHanded`），`Finish` 收尾与 `GiveInlineState` 双侧检查——模板 NPC 路径（StealPurseGold 当场移交）与 Hero 路径（give_gold 步骤）互不重叠；give_gold 本身是"没摸到钱 → 步骤失败"（现语义）。
- **M2c interrupted 世界后果**（最小化）：目击者已从 crouch 警戒脉冲拿到 +0.00x——不够。改为 interrupted 时目击者警戒按「目睹蹲行靠近目标」给一次轻微怀疑脉冲（对齐 `NpcSightSystem` 既有脉冲接口，`AddAlert(PlayerActionType.Steal, 1.0f)` 级），且 `SetPulseTarget` 指向随从（suspect 化闭环已有）。不做犯罪脉冲（未遂不犯罪，符合叙事）。
- **M2d 🔴 模板 NPC 目标走钱袋路径（2026-08-14 二轮审视修正）**：本 mod 的 Wealth 系统给每个场景 NPC 分配了定居点份额（`_agentGold`，日志实锤"NPC池2600：村民25人=1950"），并已有玩家侧「偷钱袋」路径 `StealPurseGold(agent)`（StealManager.cs:407：`GetAgentGold` 查 → `ConsumeAgentGold` 整袋端走 → 懒扣除定居点金库→玩家，守恒）。**修正**：人变体结算按目标分叉——
  - 目标有 Hero → 现状路径（RecordStolenGold+StolenSource，give_gold 步骤 `TransferGold` 个人钱包）；
  - 目标无 Hero（模板 NPC，如酒馆店主）→ **`StealPurseGold(target)` 钱袋路径**（铁律 18 平权：玩家盲盒同款函数）：分配金 > 0 → success（整袋端走，**当场已守恒移交**——金库扣、玩家加，无需尾步骤）；分配金 == 0（钱被先摸走/池耗尽）→ empty。
  - **原「模板 NPC → 装备路径」方案撤销**（装备归 M7 显式卸装备战术，两件事分开）。
- **M2b 尾步骤只对 Hero 目标**：模板 NPC 目标钱已当场移交，聊天路径无尾步骤；Hero 目标补 give_gold(stolen)。

### M3 方案 A：命令注入场景感知 — `LLM/WorldFactProvider.cs`（新方法）+ `ImChat/ImReplyService.cs`（注入点）

**新方法** `WorldFactProvider.BuildRiskSceneContext(npcHeroId, commandText)` —— 在 `BuildSceneAwareness` 基础上扩展，返回「【目之所及】」段（**M4 风险审视的输入，也是随从 think-aloud 的事实来源**）：

```
【目之所及】（你是局中人，以下是你此刻一眼扫到的场面——人随时在走动，印象可能已过时）
- 酒馆内约 25 人在场：……（SceneSnapshot 精简：目标 15m 内的人，按角色/职业合并；坐着喝酒的镇民位置大致稳定，走动的人除外）
- 酒馆店主在你左前方约 6 米，他正在走动（位置随时会变）；他身边 3 米内有 4 人站着
- 至少有 2 人的视线落在他身上（此刻如此，转头就可能变）
- 可能有 1 人正看着你（你自己也会被看见）
- 阵营（谁站谁那边，与你在场时的实际行为同口径）：
  - 在场 3 人是你的同袍（玩家队伍的人）：「求知客」阿速甘、士兵×2 —— 你犯事他们假装没看见，你被打他们会帮你
  - 店主身边 2 人是他的同伴（会帮他、会告发你）
  - 店里有 1 名守卫：你若犯事他会抓你
  - 其余 19 人是中立旁观者：不参战，但会看见并告发
- 战力对比（真打起来算两边合计）：
  - 你这边的总战力 vs 对面的总战力（己方 = 你 + 在场同袍；敌方 = 目标 + 他的同伴 + 守卫）——结论词：稳赢/略占上风/势均力敌/略处下风/悬殊
- 但店主一喊，店外可能还有 4 名守卫/佣兵 10 秒内赶到（在场潜在援军：职业剑客×4）
```

构成规则：
1. **触发范围 = 命令**：命中动作命令关键词（偷/击晕/打/跟/抓/抢/收拾……中英双语词表，对齐 WorldFactProvider 主题表惯例）**或历史/本回合判定为命令**（见 M4 触发规则）才注入（省 token，普通闲聊零开销）；
2. 目标解析：命令文本里的目标名 → `SceneSnapshot.FindAgent`（复用五层匹配）；解析不到 → 只给「在场概况」（无目标段）；
3. **战力段（双方合计 + 武装档位，2026-08-14 两次升级）**：`AgentStatsHelper.GetAgentStats` 取个体 vigo/control，**按阵营合计后给结论词**（己方 = 自己 + 在场友方；敌方 = 目标 + 目标友方 + 守卫【保守计入：守卫站秩序 = 随从犯事即敌】；措辞档位统一为**五档：稳赢/略占上风/势均力敌/略处下风/悬殊**——与 key 清单同词，禁止给数字公式给 LLM 编——给结论词）；**🔴 武装档位（新增）**：属性 ≠ 战力——守卫全副武装（重甲+长矛）与空手农民属性相近但实际战力天差地别。新方法 `AgentStatsHelper.GetArmorProfile(agent)`（读 `agent.SpawnEquipment`：武器槽伤害 + 头/身/腿/手护甲值 → 档位：徒手/轻装/武装/全副武装），战力段分列属性与武装（"守卫：全副武装（重甲+长矛），Vigor 12/Control 13"）——LLM 才能看出"悬殊来自装备"并推理出"先卸其甲兵"；**附加「在场潜在援军」**（30m 内可能赶来的人——**阵营用 `IsFriendlyBetween` 分列**：帮你的 / 帮他的 / 中立观望，禁止按职业一刀切（玩家雇佣的职业剑客 = 友方，店主的打手 = 敌方））；事前概率展示豁免铁律 17（{CHANCE} 允许事前展示）；
4. **阵营段（🔴 与 AgentBrain 实际行为同口径）**——随从的预判必须等于执行时的真实反应，逐条对应：
   - 己方友方 = `FriendlinessHelper.IsFriendlyToPlayer`（随从是玩家阵营成员，玩家友方 = 随从友方）→ 对应 AgentBrain 行为：**友方旁观者豁免**（AgentBrain.cs:682/795——随从犯罪时在场友方不围观/不质问/不告发）→ prompt 写「你犯事他们假装没看见」；
   - 友方被攻击 → 护主参战（AgentBrain 战斗回调）→ prompt 写「你被打他们会帮你」；
   - 目标友方 = `FriendlinessHelper.IsFriendlyBetween(target, other)`（**新扩展**，见 M3.5）→ 会帮目标、会告发你；
   - 守卫 = SceneSnapshot.BuildRole 的 "guard" 标记 → 站秩序：随从犯事 = 抓随从；
   - 中立者 = 其余 → 不参战但目击告发（WitnessCrime 系统，日志 19:39:03 实锤 8 目击者）；
   - **受害者是友方时无豁免**（AgentBrain.cs:682 注释：受害者是玩家友方 → 照常围观/质问）——随从伤害自己人 = 高危险，prompt 纪律 3 后果链已覆盖；
5. **快慢变量分层 + 时效声明（🔴 2026-08-14 用户质询：目标在移动，快照不准）**：
   - **慢变量（可信，作判断依据）**：人群构成/人数、职业、阵营归属、守卫位置、战力合计——酒馆里坐着喝酒的人不会秒秒换位置，快照够用；
   - **快变量（弱化措辞 + 随时变）**：视线「正看着谁」（转头就变 → prompt 写「至少有 N 人的视线落在他身上（此刻）」）、精确距离（目标走动中 → prompt 写「正在走动，位置随时会变」）；
   - **移动状态判定**：复用 `agent.Velocity.LengthSquared > 0.25f`（RuntimeWorldState.cs:285 同口径）→ State 追加「走动中/驻足」标注，目标与在场者都标；
   - **决策/执行职责分层（防实现期误改执行层）**：风险审视 = **方向性判断**（这活干不干得成、要不要引开、会不会被俘——全部基于低频变化的慢变量）；执行层 = **临场时机**（绕背走位每帧重算、目击检查 Rolling 时实时 `GetWitnesses`——**已经是实时的，执行器零改动**）。快照过期只影响决策层的"大方向"，不影响执行正确性；
6. 叙事铁律：全是该 NPC 亲见（快照 = 它的眼睛）；**禁止注入它看不到的东西**（背对的人视线状态按 `CanSee` 为准；阵营信息 = 同袍穿同款甲胄/熟面孔、守卫的制式装备——看得出来的）；
7. 主线程构建（引擎对象只读主线程）——`ScheduleReply` 现有 `BuildSceneAwareness` 调用点同位置追加。

**效果**：随从拒绝/计划时理由有事实依据（"店主身边站满人，我一伸手准被看见"）；同意时会自己提"人多，先让谁引开"——把 P3 的"常识泛化"升级成"感知判断"。阵营段让战力评估是**真实的**（己方 1 打敌方 6 时随从必须拒绝，而不是只看店主一个）；时效声明让判断**留余量**（不会因为"现在没人"就莽，也不会因为"现在有人"就死板拒绝——执行时以当场为准）。

### M3.5 `FriendlinessHelper.IsFriendlyBetween`（通用友方判定扩展）— `Core/FriendlinessHelper.cs`

**解决**：现有 `IsFriendlyToPlayer` 只支持「→ 玩家」方向；风险审视需要任意两方（随从 vs 在场者、目标 vs 在场者）的友方判定。

**改动**：把四个维度（Party 同队伍 / Clan 同家族 / Kingdom 同王国 / Relation 好感阈值）泛化为 `IsFriendlyBetween(Agent a, Agent b)`（含 Hero 版），原 `IsFriendlyToPlayer` 保留为 `IsFriendlyBetween(x, MainHero)` 的委托（零行为变化，全项目调用点不动）。关系维度方向性：`b.GetRelation(a)`（b 视角对 a 的好感）——实现期定，文档先记方向性问题。

### M4 方案 B：风险审视（Risk Review）——**每次命令先想后做**，深度思考 + 稳健方案 — 改 `LLM/PromptBuilder.cs`（回复轮）+ 新 `Planner/RiskAssessor.cs`（裁决）+ `Planner/ActionHandler.cs`（挂点）

**🔴 触发范围：每次玩家提出要求（命令），不只是 RequiresConfirm 高危动作。** 命令判定 = 回复轮 LLM 输出 `npc_action != NONE` 或 `need_plan=true`（既有字段，零新判定逻辑）。闲聊不触发（零开销）。
**🔴 深度分级（二·五节）**：深度审视的触发 = 【目之所及】段实际注入（M3 命令词表命中 = L1/L2）；L0 低风险命令无风险段 → 回包无 risk 字段 → 裁决默认 feasible 走现状。分级零额外判定逻辑（注入与否天然分级）。

**🔴 合并进回复轮（一次 LLM 调用，不叠加延迟）**：评估**不是**回复之后的第二次调用——`BuildPrompt_ImReply` 注入【目之所及】段 + 【风险审视纪律】段，输出 JSON 扩展**两个**字段（**不含计划**——计划生成职责归计划轮，见下）。

**🔴 管线边界（三份 JSON 各归其位 + 职责分工，2026-08-14 澄清）**：

| 管线 | LLM 调用点 | 输出结构 | 职责 | 本设计是否改动 |
|---|---|---|---|---|
| 闲聊回复轮（现有） | `BuildPrompt_ImReply`（ImReplyService） | `{npc_reply, npc_action, action_target, action_level, need_plan}` 5 字段 | 回话 + 选动作 | 扩展 2 字段（下方 JSON） |
| 闲聊回复轮（**M4 扩展后**） | 同上 | 5 字段 + `risk_analysis` + `risk_verdict` | **风险审视**：要不要计划（verdict）+ 战术方向（analysis 含"我打算先引开再下手"） | **本设计的落点** |
| 计划生成轮（现有，**结构不动**） | `BuildPlanPrompt`（ImCommandFlow/PlanCommandFlow） | `PlanResponse`：`{intent, plan, questions, ...}`——意图分类 + **完整 Plan**（steps 带 on_timeout/on_event/goal/loop） | **计划生成**：LLM 出完整计划，py/C# 只校验+补缺省值（现状不变） | 不改结构，仅新增一个自动触发入口 |

**🔴 职责边界（用户裁定 2026-08-14）**：`timeout/goal/on_event` 等计划内容**由计划轮 LLM 生成，脚本只做检查和补全**——本设计**不新增任何"简化数组 → 完整 Plan"的 C# 翻译层**（C# 补策略 = 模板化计划，且回复轮 LLM 没有完整语法上下文）。风险审视只决定**要不要计划**，计划本身永远由计划轮出。

```json
{
  "npc_reply": "台词（plan_needed 时含战术方向：如"人太多，我先喊一嗓子把人引开，再绕到他身后下手"）",
  "npc_action": "动作码",
  "risk_analysis": "我对局势的判断（1-2 句，含依据：人多人少/谁在看着/打不打得过/被抓会怎样）",
  "risk_verdict": "feasible | plan_needed | risky | refuse"
}
```

（以上 JSON 是 M4 扩展后的**闲聊回复轮**输出——**不是**计划模式的 `PlanResponse`。`risk_plan` 字段**已移除**——计划由计划轮生成，见下。）

**plan_needed 的衔接（🔴 2026-08-15 用户裁定改全手动：不再自动触发，挂按钮等玩家确认）**：回复轮出 `verdict=plan_needed` → RiskAssessor 给随从回复挂「制定计划/先不用」按钮（`TryAttachSuggestion`，战术方向 risk_analysis 随按钮存储于消息 `RiskAnalysisText`）→ 玩家点「制定计划」→ 既有计划生成入口（`ImCommandFlow.RequestCommand(companionIntention)` 复用）。衔接细节：
- **npc_action 忽略**：plan_needed 时回复轮选的动作码**不执行**（计划接管，计划卡批准后按计划跑）——RiskAssessor 分流直接拦截，不发动作卡；
- **命令文本构造**：玩家原命令进【命令】段（语义 = 主公的命令）；随从战术方向（risk_analysis）**单独成段【随从的打算】**（第一人称，随从自己说的话）——**不混入【命令】段**，防止"谁的命令"语义混淆（BuildPlanPrompt 加段，prompt XML 单一事实源）；
- **状态机衔接**：点按钮在主线程 Tick 发起（ImCommandFlow 消费），过 `ImCommandFlow.IsBusy` 锁——计划生成中玩家发新消息 → 既有 `suppressNeedPlan` 抑制机制天然防并发；
- 计划轮 LLM 出完整 `PlanResponse` → 计划卡片（讲解）→ 玩家批准 → 执行；计划轮失败（LLM 故障/JSON 崩）→ 降级为 feasible 直发动作卡。
- **代价 = 两次 LLM 调用（回复轮 + 计划轮，串行 6-10s）**——仅 plan_needed 付这个代价（玩家要审阅批准计划卡片，等待合理）；feasible/risky 仍一次调用。「制定计划」按钮保留（低风险时玩家自愿点，与自动触发同入口）。
- 🔴 **全手动裁定依据（2026-08-15 实机）**：原设计"自动触发"实机出现双入口（按钮 + 自动思考中并存，日志 06:47:56 实锤——旧 need_plan 挂按钮路径与 M4 自动触发路径同轮回包都执行）。裁定：**计划生成必须玩家手动触发**（与普通 need_plan 同入口，规则统一），自动触发 = 玩家不要该命令时 LLM 调用白烧 + 双入口混淆。

**深度思考结构**（`risk_analysis` 强制模型先想再下结论，JSON 字段即 think-aloud 出口；该字段原样成为随从 IM 消息/冒泡，玩家看到"为什么"）：

```
【风险审视纪律】（LWN_plan_risk_rules，XML 单一事实源）
1. 先看目标：能不能接近、身边几个人、谁正看着它、有没有守卫
2. 记住【目之所及】只是你一眼扫到的印象，不是地图——
   人随时在走动、坐下、离开；「现在没人」不等于「一直没人」，「现在有人」也可能马上走开。
   判断留余量：优先看慢变量（这地方人多不多/有没有守卫/谁站谁那边），别把快变量（谁正看着谁）当铁律
3. 再看自己：真打起来算**两边合计**（【目之所及】的战力段：你+同袍 vs 目标+他同伴+守卫）——
   明显不如 + 对方有人帮 → 不要硬来，但不要轻易放弃，看第 5 条怎么绕
4. 想后果（这是真实世界，不是儿戏）：
   - 偷窃/袭击被当众目击 → 你会被治罪、被关进牢房 → 主公要花钱把你赎出来
   - 打不过对方 → 你会被俘、被关起来 → 主公来赎你之前你可能在牢里待很多天
   - 在场同袍会帮你，但他们不会为你去坐牢；伤自己人（玩家友方）没有豁免，会被自己人告发
5. 🔴 满足主公是第一位（2026-08-14 用户裁定）——不要用危险当借口拒绝：
   - 战力悬殊先想**为什么悬殊**：装备差距 → 能不能先卸他的武器（steal_equipment，抽走剑/甲他就只剩拳头）；人数差距 → **请同袍引开**/换时机；时机不利 → 等。**想削弱办法永远先于判死刑**。⚠️ 引开是别人的活——自己喊一嗓子再偷 = 招围观找死
   - 有万全之策 → 想出来，拆成几步（绕后/卸装备/请同袍引开/等时机），npc_reply 说出战术方向，完整计划随后拟定
   - 实在没有万全之策 → risk_verdict=risky，在 risk_analysis 里**把风险讲透**（会怎样被抓、会不会被打伤、会不会坐牢），
     然后照办——主公的意志优先，你只负责把风险讲清楚，选择权在主公手里
   - 「太危险」不是拒绝的理由，只有**办不到**（目标不在跟前/东西没有/人不在地图）才是 refuse
6. risk_verdict 选择：
   - feasible：判断稳，直接干
   - plan_needed：有万全之策但要拆几步（先引开再偷/先望风再动手/请同袍配合/先卸他武器再打）→
     在 npc_reply 里说出你的**战术方向**（"我打算先…再…"），完整计划由你随后拟定
   - risky：没有万全之策但办得到 → 讲清风险后照办（等主公点头或收回）
   - refuse：只有办不到才拒绝（目标不在/东西没有），并说明原因
7. 计划的完整语法（on_timeout/on_event/goal 等）由拟定计划时生成——你现在只需要说清**战术方向**
```

**稳健方案落点**：
- **计划内容由计划轮 LLM 生成**（完整 Plan 语法：`make_noise → wait → steal_attempt → give_gold` 全支持），`PlanValidator` 校验 + 补缺省值（现状不变）——本设计**不新增 C# 计划翻译层**（见管线边界）;
- **计划必须含撤退/失败应对**：语法层已有 `on_timeout`/`on_event`/abort 机制（"拖太久先撤"），计划轮 prompt 纪律强制写入；
- 成功率门槛：战力段明显不如 + 无援 → 纪律 3 强制 risky/plan_needed（不硬送）。

**裁决（`RiskAssessor`，C# 确定性壳）**：
- 挂点：`ImReplyService` 回包解析处——`npc_action != NONE || need_plan` → 读 `risk_verdict`；
- `plan_needed`（🔴 2026-08-15 改全手动）→ **挂「制定计划」按钮**（`TryAttachSuggestion`，战术方向随按钮存储）→ 玩家点按钮 → `ImCommandFlow.RequestCommand`（命令 = 玩家原命令 + 随从战术方向）→ 完整 `PlanResponse` → 计划卡片（讲解）→ 玩家批准 → 执行；计划轮失败（LLM 故障/JSON 崩）→ 降级为 feasible 直发动作卡；
- `risky`（🔴 2026-08-14 用户裁定：告知风险后坚定执行）→ `risk_analysis` 作为随从台词/冒泡（"把风险讲透"）→ **决策卡文案带风险摘要** → 玩家确认 → **坚定执行**（不再拒绝、不再二次确认）；玩家拒绝/撤回 → 不执行（这是玩家自己的选择，不是 NPC 拒绝）。
  **卡片机制（2026-08-14 二轮审视补齐）**：RequiresConfirm 动作的决策卡（PostActionProposal）新增**风险变体**——InquiryMsgKey 加 `_risk` 后缀 key（「{NAME} 警告：{RISK}——仍要动手吗？」），`risk_analysis` 原文注入 {RISK} 变量（LLM 生成文本豁免本地化，框架句本地化）；非确认动作 risky 时借用同一卡片通道（v1 仅 RequiresConfirm + ask_help 类高危，低危动作 risky 几乎不出现——实现期对每个动作裁定是否需要卡片）；
- `refuse`（缩窄为「办不到」）→ 随从 IM 消息 = `risk_analysis`（"目标不在跟前/东西没有"），动作不执行（拒绝纪律沿用现有"只拒绝一次，主公重申必须执行"作为最后兜底）；
- `feasible` / 字段缺失 / verdict 非法 → 现状（动作卡/直接执行）。

**🔴 同步修改 `LWN_plan_im_reply_rule`（2026-08-14 二轮审视，A3）**：现有回复纪律的拒绝条款（"目标太强、不便出手可拒绝一次"）与 M4 新纪律（"太危险不是拒绝理由"）冲突——**必须同步改写**：① 拒绝条款改为"只有办不到（目标不在跟前/东西没有）才拒绝；太危险 → 按【风险审视纪律】走 risky（告知风险后执行）"；② JSON 输出模板加 `risk_analysis`/`risk_verdict` 两个字段说明；③ max_tokens 220 → 300。改 EN/CN 两份 prompts XML + py 回归基线同步。

**降级链（铁律 1）**：`!IsLLMConfigured` / 超时 / JSON 解析失败 → 按现状执行（`npc_action` 直发），**LLM 是增强不是门禁**；`risk_analysis` 缺失 → 不播（卡片本身就是出口）。

**频率纪律**：无额外冷却（并入回复轮，回复轮已有 `ImReplyCooldownSeconds`）；回复轮 max_tokens 上调（220 → 300，容纳分析字段）。

**think-aloud**：`risk_analysis` 以随从 IM 消息呈现（玩家先看到"它的判断"，再看到卡片/动作）；`feasible` 时 analysis 也可作动作前一句台词（随从边答边干，不拖沓）。

### M5 单步 Chat 计划收尾去静默 — `Planner/PlanExecutor.cs`（`Finish` 微调）

**问题**：`ChatActionFlow` 单步计划无 `end_plan` 步骤 → 走 `Finish(Succeeded, null)` 静默。判定型动作（steal/knockout）有结局不该静默。

**改动**：`Finish` 的 `silent` 判定加条件——当 `state==Succeeded && _stepResultKey` 非空（判定型动作有结果）时不允许静默；结局播报归 M2a（InlineSteps 内播），此处只保证"有结局必有出口"。聊天路径 `Succeeded` 且有 step result 时若 InlineSteps 已播 → 不重复播（`_resultBroadcast` 标记）。

### M6 多随从分头配合 — `Planner/InlineSteps.cs`（ask_help 内联）+ `Planner/ActionRegistry.cs`（主表一行）+ `AI/AgentBrain.cs`（配合指令事件）+ `AI/AgentAIController.cs`（广播）

**🔴 2026-08-14 用户裁定转正**（原"不做清单"）：队伍频道下多个随从在场时，计划可以**分头配合**——执行人计划中请求同袍执行单个动作（引开/望风/停留），自己继续主任务。

**v1 范围（刻意收窄）**：配合 = **单动作**，配合者**不生成计划、不风险审视**（引开=喊叫 isCrime:false 低危，望风=停留零风险）；高危配合动作（帮忙击晕/偷）v2 再说。执行器本身仍是**单执行人**——配合者是临时挂载，不是双 cursor。
**🔴 配合白名单核实（2026-08-14 二轮审视）**：白名单 = **`make_noise` / `follow` / `emote`**（均已确认在 ActionRegistry 主表且 ExecutorImplemented——`stay` **不在主表**，去掉；`emote` 白名单 9 动画低危可作"手势示意"配合）。assist_request 只接受白名单动作码。

**语法（AskHelpInlineState，非行为性内联——通信类留在排序器侧）**：

```json
{ "动作": "ask_help", "目标": "求知客阿速甘", "内容": "帮我把人引开" }
```

**执行链**：
1. 执行人 A 的计划走到 ask_help 步骤 → `AgentAIController.BroadcastEventInRange` 或直接事件发给目标 B 的 agent：`"assist_request"`（Args：请求动作码 + 目标文本 + 请求者）；
2. B 的 AgentBrain 收到 `assist_request` → 校验（B 空闲：无计划/无战斗/非昏迷）→ 调既有 `ChatActionFlow.TryExecute(B, 动作码, 目标)` 单步执行（复用免确认直发通道，B 冒泡台词「交给我」+ 执行动作）；
3. A 的 ask_help 步骤**等待 B 完成信号**：B 的单步计划完成后发 `assist_done` 事件 → A 的步骤 `on_event: assist_done` 继续；**🔴 `assist_done` 需注册进 on_event 事件词表**（PlanGrammar 事件注册处，否则"未定义事件 → 条件丢弃"——与谓词词表同纪律）；**超时兜底语义（2026-08-14 二轮审视）**：ask_help 步骤的 `on_timeout` 由**计划轮生成时写好**（prompt 示范：「ask_help 若没人应（超时）→ 自己 make_noise 顶上 / 或直接继续下一步」，**不是执行器魔法**）——执行器只负责既有 on_timeout 机制；
4. A 的计划后续步骤正常推进（wait 等人群转向 → 绕背 steal → give_gold）。

**think-aloud**：计划卡片讲解显示「让阿速甘引开人群」（复用 PlanActionLabel 标签表）；执行时 B 的 AgentSay 冒泡（make_noise 自带台词）可见；B 完成回执可冒泡一句「成了」。

**词表/校验同步**：ActionRegistry 主表注册 `ask_help`（InPlanVocab=true，InChatSpace=false——v1 只允许计划语法出现，闲聊一句话直接调不动多随从）；PlanGrammar + py `ALLOWED_ACTIONS` 同步（check_vocab_sync.py 跑通）。

**风险审视与配合的交互**：执行人 A 的战术方向含 ask_help（纪律 5「请同袍配合」已写入风险审视 prompt）→ 计划轮把战术方向转成计划（含 ask_help 步骤）；B 侧不做审视（低危单动作，见范围）。

**防滥用**：assist_request 只接受白名单动作码（make_noise/follow/emote）——LLM 无法让 B 干高危活；B 忙碌 → 忽略 + A 的 on_timeout 兜底（计划轮生成）。

**🔴 战术语义纠偏（2026-08-14 二轮审视，A1）**：`make_noise` = 喊叫 + **围观聚集**（人群聚过来看，不是散开）——**"引开"必须由配合者做**（B 喊叫把目光引走，A 趁机下手）；执行人**自己喊了再偷 = 招围观找死**。纪律第 5 条与示例计划措辞同步（"引开" = 请同袍喊叫 / 换时机，禁止"自己喊一嗓子再偷"的自欺战术）。单随从场景（无 B 可配合）→ 不用引开，走绕背/等时机/卸装备。

### M7 偷装备动作 + 策略推理（"先削弱再打"，2026-08-14 用户质询：打全副武装守卫）— `Planner/InlineSteps.cs`（新变体）+ `Planner/ActionRegistry.cs`（主表一行）+ `Stealth/StealManager.cs`（NPC 侧结算）+ `Core/AgentStatsHelper.cs`（武装档位）

**用户场景**：命令「打那个全副武装的守卫」。直接打 = 战力悬殊。聪明做法 = **先偷走守卫的剑/甲（削弱）→ 再攻击（徒手对徒手）**。三步拆解：

1. **评估能看出悬殊**（M3 武装档位）——"守卫全副武装（重甲+长矛）vs 你徒手" → LLM 知道差距在装备；
2. **语法能表达削弱**（本 M7）——新动作 `steal_equipment`（人变体扒窃扩展：偷**装备**而非钱包）；
3. **纪律引导推理**（风险纪律第 5 条升级）——"战力悬殊先想为什么悬殊：装备差距 → 能不能先卸他的武器（steal_equipment）；人数差距 → 引开/请同袍"。

**动作定义**（ActionRegistry 主表一行）：`Code="steal_equipment"`，InPlanVocab=true，InChatSpace=false（v1 只计划语法，闲聊一句话不直接触发），RequiresConfirm=true（扒窃目标本人 = 高危，同 steal_attempt），Spaces=InScene。

**执行层（复用扒窃判定管线，铁律 18 平权）**：StealAttemptInlineState 加 `variant="equipment"`——Behind 绕背 → 目击检查 → 成功率（复用扒窃公式，v1 同档；装备比钱重，实现期可微调）→ 结算走共享管线（**与 M2d 的模板 NPC 装备路径共用同一结算** `StealEquipmentForNpc`——M7 是"显式指定偷装备"，M2d 是"模板 NPC 自动降级到装备"，一条管线两个入口）：

```csharp
// NPC 侧结算（StealManager 新方法 StealEquipmentForNpc，玩家路径 StealSpecificItem 的镜像）：
// ① 目标 SpawnEquipment[武器槽或护甲槽] 卸下（武器槽优先——削攻最直观）
// ② 目标真实损失：Hero 清 campaign 装备层（复用 ClearHeroEquipmentSlot）
// ③ 物品去向：进玩家队伍背包（TransferItems，与玩家扒窃守恒一致——随从不武装自己，
//    运行时改 agent 装备复杂，v2 再做「夺刀自用」）
// ④ RecordStolen 记账（归还复原路径共用）
// 引擎原生行为：目标武器槽空 → 徒手攻击（战力真实下降，无需我们实现）
```

失败 → 目标察觉 → 警戒脉冲 + 计划 abort（撤退，既有路径）。

**目标战力翻转的真实性**：卸甲后战力段评估的对象变了——但战力段是计划期快照，执行期目标已徒手。**计划期 LLM 的推演依据** = 武装档位分列（"守卫全副武装，徒手时 Vigor 12/13 对你不占优"）——prompt 给事实，推演归 LLM。

**示例计划**（写入 LWN_plan 示范，防 LLM 想得到写不出；🔴 2026-08-14 二轮审视：引开由**同袍**做，执行人自己喊 = 招围观）：

```json
"计划步骤": [
  { "动作": "ask_help", "目标": "求知客阿速甘", "内容": "帮我把人引开" },  // B 喊叫引开人群视线
  { "动作": "wait", "秒数": 2 },                                           // 等 B 制造动静、人群转向
  { "动作": "steal_equipment", "目标": "守卫" },                           // 抽走他的剑（绕背盲区）
  { "动作": "attack", "目标": "守卫" }                                     // 徒手对徒手，上
]
```

**风险审视纪律第 5 条升级**（LWN_plan_risk_rules 文本）：
> 战力悬殊先想**为什么悬殊**：装备差距 → 能不能先卸他的武器（steal_equipment）；人数差距 → 请同袍引开/换时机；时机不利 → 等。想削弱办法永远先于判死刑。**注意：自己喊一嗓子再偷 = 招围观找死——引开是别人（同袍）的活，你只管下手。**

### M8 新战术动作接入指南（可插拔，2026-08-14 用户质询：下毒能否被随从自动认识）

**目标**：新增战术动作（下毒/放火/缴械/堵路……）时，**推理自动化是赠品**——不修改任何推理逻辑/prompt 纪律，LLM 自动学会在合适场景使用。三个机制缺一不可：

| 机制 | 作用 | 位置 | 现状 |
|---|---|---|---|
| ① 词表单一事实源 | LLM 知道"有这个方法" | ActionRegistry 主表 InPlanVocab 一行 | ✅ 已就位（自动进计划轮词表段） |
| ② 场景事实对齐 | LLM 知道"什么时候能用"——触发条件必须在【目之所及】可见，否则场景事实纪律（禁编造细节）会拦死 | SceneSnapshot/WorldFactProvider | ⚠️ 需逐动作核对（下毒 = 物件分类加 cup/bar/kitchen） |
| ③ 开放方法目录 | 推理桥——把"方法目录"绑定到**词表**而非纪律文本 | LWN_plan_risk_rules 第 5 条 | 🔴 需升级（见下） |

**纪律第 5 条升级为开放模板**（M7 的封闭清单改开放；🔴 2026-08-14 二轮审视：绑定**可执行**词表——`shadow/negotiate/duel` 在主表但 ExecutorImplemented=false，LLM 引用会被 PlanValidator 丢弃，模板措辞防误引）：

> 战力悬殊先想**为什么悬殊**，针对差距想办法：装备差距 → 卸他的武器/甲（steal_equipment）；饮食在侧 → 下毒（poison）；人数差距 → 请同袍引开/换时机；防御严密 → 绕背/等时机。**词表里能执行的办法都用上，不要局限于这几条。**

最后一句话绑定词表——加 poison 后 LLM 看到「目标在吧台旁端着酒杯 + 战力悬殊」自然推理出下毒，**零 prompt 改动**。

**新动作接入清单（下毒为范本，M7 卸装备为第一个已实现的实例）**：

| 步骤 | 下毒示例 |
|---|---|
| 1. 主表一行 | `poison`：InPlanVocab=true, RequiresConfirm=true（犯罪动作）, Spaces=InScene |
| 2. 执行层 | poison 内联步骤：接近目标饮食（酒杯/食物）→ 目击检查（复用扒窃判定管线）→ 结算目标中毒 debuff（HP/属性下降，执行期战力翻转） |
| 3. 场景感知对齐 | ClassifyObject 加 `cup`/`bar`/`kitchen` 关键词 → 「店主在吧台旁（端着酒杯）」进【目之所及】 |
| 4. 效果结算 | 中毒状态（任务期/短期 debuff + 战力段执行期翻转）——实现期定 |
| 5. 播报 key + py 词表同步 | LWN_npc_poison_* + check_vocab_sync.py |

**边界（刻意）**：LLM 不会发明词表外的方法（铁律 2——封闭词表保证可执行性，PlanValidator 可校验）。「放火烧店」不在词表 = 随从不会提出；想支持 = 填表接入。

## 四、本地化 key 清单（铁律 13，全部 LWN_*，无 emoji）

| key | 用途 |
|---|---|
| `LWN_npc_steal_success` / `LWN_npc_steal_equip_success` / `LWN_npc_steal_empty` / `LWN_npc_steal_impossible` / `LWN_npc_steal_interrupted` | M2a/M2d 偷窃结局播报（钱版 + 装备版 + 摸空/绕不到/被目击；EN fallback + CN 翻译） |
| `LWN_npc_steal_fail_roll` | empty 带 ROLL/THRESHOLD 的播报 |
| `LWN_npc_steal_equip_fail` | M7 卸装备失败（被察觉）播报 |
| `LWN_plan_risk_rules`（prompts XML） | M4 风险审视纪律（py/C# 同源；含 2026-08-14 修订：尽力满足/削弱优先/引开是别人活/开放方法目录） |
| `LWN_ui_interact_inquiry_*_risk_msg`（风险变体，四动作各一） | M4 risky 决策卡带风险摘要（{RISK} = risk_analysis 原文，框架句本地化） |
| `LWN_plan_im_reply_rule`（prompts XML） | 🔴 同步改写：拒绝条款（办不到才拒绝）+ JSON 模板加 2 字段（EN/CN 双份 + py 基线） |
| `LWN_fact_title_risk` | M3 【目之所及】段标题（prompt 文本进 prompts XML） |
| 战力五档词（稳赢/略占上风/势均力敌/略处下风/悬殊）+ 武装档位词（徒手/轻装/武装/全副武装） | C# 拼段用，进 strings XML |
| `LWN_npc_assist_accept` / `LWN_npc_assist_done`（「交给我」「成了」） | M6 B 侧配合台词 |

prompt 静态段（风险纪律）进 `std_LivingWorldNpcs_prompts.xml`（py/C# 同源单一事实源，改 prompt 只改 XML）。

## 五、验证计划

1. **P1**：使随从犯法坐牢 → 赎回 → 打开 party 界面确认归队 + `hero.State == Released`（日志 `[CompanionDetention] 赎回` 行后加归队日志）；
2. **P2**：酒馆场景让随从偷店主（人多的环境）→ 必现 interrupted 播报 + 目击者警戒脉冲；偷无人注视的目标 → 验证成功播报 + 赃物到玩家背包（守恒：目标侧扣、玩家侧加，日志核对双方）；
3. **P2/M2d（🔴 2026-08-14 二轮审视修正）**：酒馆店主是模板 NPC（无 Hero）→ 偷店主走**钱袋路径**（`StealPurseGold`，分配金 > 0）→ 验证「摸到了 X 金」播报 + 守恒（定居点金库扣、玩家钱包加，日志核对双方）；分配金已被先摸走/池耗尽 → empty 播报；
4. **P3/M3**：日志 grep `[RiskScene]` 注入段，确认动作命令命中注入、闲聊不注入；
5. **M4 深度思考**：命令「去偷店主」在 8 人目击场景 → 随从应回 `plan_needed`（risk_analysis 含"人多/会被看见"依据 + npc_reply 含战术方向"请阿速甘引开再偷"）→ 挂「制定计划」按钮（全手动裁定）→ 玩家点按钮 → 计划卡片（引开→偷→撤）→ 批准 → 执行全链路日志核对；
6. **M4 稳健性**：命令「去打那职业剑客」（战力合计悬殊 + 有援军）→ 随从应 `plan_needed`（先卸装备/请同袍）或 `risky`（告知风险后执行），不应轻易 refuse；命令「去偷守卫」→ 守卫在侧 → plan_needed（换时机）或 risky；
7. **M3 阵营同口径**：队伍成员在场时命令「去偷店主」→ prompt 应标注同袍（"你犯事他们假装没看见"）；执行时实测——随从犯罪，在场同袍确实不告发（AgentBrain 豁免）；无同袍 + 目标有同伴 + 守卫 → 应 refuse；
8. **M3 移动目标时效**：命令「去偷正在走动的目标」→ prompt 应标注「正在走动，位置随时会变」；执行时随从绕背走位仍能追上移动目标（执行器 Behind 阶段每帧重算，已是实时）；
9. **M4 尽力满足（拒绝纠偏）**：连续下 10 个不同高危命令（偷守卫/打剑客/当众偷）→ 随从拒绝次数应极少（≤2），多数走 plan_needed（万全之策）或 risky（告知风险后执行）；「目标不在场」类命令 → refuse 并说明原因；
10. **M4 risky 路径**：命令「当众偷店主」（无万全之策）→ 随从台词讲透风险（risk_analysis）→ 决策卡带风险摘要（_risk 变体文案）→ 玩家确认 → 坚定执行（动作/计划照常跑）；玩家拒绝卡 → 不执行且随从不二次纠缠；
11. **M6 多随从配合**：队伍频道下两随从在场，命令「偷店主」→ 战术方向含"请同袍引开" → 计划轮生成含 ask_help 的计划 → 计划卡 → 批准 → B 冒泡「交给我」+ make_noise 引开 → A 绕背偷 → 移交全链路日志核对；B 忙碌（战斗中）→ A 的 on_timeout 兜底（自己顶上或继续），计划不中止；
12. **M7 策略推理（先削弱再打）**：命令「打那个全副武装的守卫」（随从徒手）→ 应 plan_needed（战术方向含"先卸他武器"，计划轮生成 卸装备 → 攻击），执行后守卫武器槽空（徒手）、随从打赢；直接攻击（无削弱步骤）且战力悬殊 → 视为失败用例；steal_equipment 失败（守卫察觉）→ 警戒脉冲 + 计划 abort 撤退；
13. **M6 战术语义（🔴 2026-08-14 二轮审视新增）**：单随从场景命令「偷店主」→ 随从**不得**生成"自己 make_noise 再偷"计划（招围观）；引开只出现在有同袍可配合的计划里；
14. **降级**：关 LLM → 命令照常执行（无风险段，现状直发），无崩溃（铁律 1）；LLM 回包缺 risk 字段 → 默认 feasible；
15. **回归**：`Scripts/validate_localization.py`（新 key 无错）、`check_vocab_sync.py`（ask_help/steal_equipment 词表同步）、LLM 回归基线（prompt 改动只增段不删段，先字节 diff 再跑）。

## 六、不做清单（明确排除）

- ❌ 高危配合动作（帮忙击晕/帮忙偷窃）——M6 v1 白名单只放低危单动作，v2 再议；
- ❌ 方案 C（计划预演回环：谓词模拟校验 → 回灌修正）——A+B 跑顺后再议；
- ❌ 原版 fugitive 潜逃赎回归队——交付感差，采用显式归队；
- ❌ 偷窃 UI 慢动作/扒窃条给 NPC——玩家 UI 专属（铁律 18 UI 边界），NPC 侧 = AgentSay 冒泡 + 播报；
- ❌ `need_plan` 默认打开——仍由 LLM 判断 + 玩家按钮确认。

## 七、改动文件清单

| 文件 | 改动 |
|---|---|
| `WorldEvent/CompanionDetentionBehavior.cs` | M1 释放序列（ChangeState + RemoveTroop + 归队） |
| `Planner/InlineSteps.cs` | M2a 偷窃结局播报 + M2d 人变体按目标分叉（Hero → 现状；模板 NPC → `StealPurseGold` 钱袋路径）+ M7 steal_equipment 变体 |
| `Planner/ChatActionFlow.cs` | M2b 偷窃尾步骤（仅 Hero 目标补 give_gold；`_goldHanded` 防双移交） |
| `Planner/PlanExecutor.cs` | M2b 移交标记 / M5 Finish 判定型动作去静默 |
| `LLM/WorldFactProvider.cs` | M3 `BuildRiskSceneContext`（命令词表 + 目之所及段：目标/人群/视线/**阵营**/双方合计战力+**武装档位**/**援军按阵营分列**） |
| `Core/FriendlinessHelper.cs` | M3.5 `IsFriendlyBetween(a, b)`（对称语义：party/clan/kingdom 任一命中 + 关系任一方向 ≥ 阈值；原 IsFriendlyToPlayer 委托化，零行为变化） |
| `Core/AgentStatsHelper.cs` | M7 `GetArmorProfile(agent)`（SpawnEquipment 武器/护甲 → 武装档位） |
| `ImChat/ImReplyService.cs` | M3 注入点 + M4 回包解析（risk_analysis/verdict）+ verdict 分流 + plan_needed 自动触发（主线程 Tick，过 IsBusy 锁） |
| `LLM/PromptBuilder.cs` | M4 回复轮注入风险段（BuildPrompt_ImReply）+ **【随从的打算】段**（BuildPlanPrompt，战术方向独立注入）+ max_tokens 上调 |
| `Planner/RiskAssessor.cs`（新） | M4 裁决：verdict 分流（feasible 现状 / plan_needed → 挂「制定计划」按钮（2026-08-15 改全手动）且 npc_action 忽略 / risky → 风险台词+_risk 决策卡 / refuse → 拒绝消息） |
| `Stealth/StealManager.cs` | M7 `StealEquipmentForNpc`（卸装备结算：目标装备层清空 + 物品进玩家背包 + RecordStolen）；M2d 复用既有 `StealPurseGold`（零新代码） |
| `Planner/InlineSteps.cs` | M6 ask_help 内联状态机（发送配合事件 + 等回执 + 超时兜底） |
| `Planner/ActionRegistry.cs` | M6 `ask_help` 主表一行（InPlanVocab=true，白名单配合动作）+ M7 `steal_equipment` 主表一行 |
| `AI/AgentBrain.cs` | M6 `assist_request`/`assist_done` 事件分支（B 侧收到 → ChatActionFlow.TryExecute 单步执行 → 回执；白名单 make_noise/follow/emote） |
| `AI/AgentAIController.cs` | M6 配合事件广播/转发通道 |
| `Planner/PlanGrammar.cs` | M6 `assist_done` 注册进 on_event 事件词表 |
| `ModuleData/Languages/*/std_*.xml`（strings + prompts 两个文件各 EN/CN） | 第四节 key 清单（含 `LWN_plan_im_reply_rule` 改写与 `_risk` 卡片变体） |

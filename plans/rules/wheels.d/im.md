# im — 轮子速查分卷（wheels.md 索引导航）

IM 即时传讯系统的**行为层**轮子（群聊回复管线 / 记忆写入 / 选人 / 事件广播 / 线程模型）。
UI 表现层轮子（层序/气泡/滚动）在 [ui.md](ui.md)。完整设计见 `plans/im-chat-system.md` §十三。

---

## 群聊回复管线（延迟调度 + 三层丢弃纪律）— `ImChat/ImReplyService.cs`

**解决**：LLM 回复是异步的（1-3s），玩家连发消息会产生"过期链条"（针对旧消息的跟随/往返上屏）。

```csharp
// 调度（主线程）：主回复者任务挂上跟随者，跟随者**等主回复者投递后才生成**
ImReplyService.ScheduleReply(primaryId, name, text, conv, followUpId, followUpName);
// 主回复者投递后（Tick 内）：延迟调度跟随者，prior = 实际台词
// 跟随者投递后：25% 概率 bounce 主回复者再回一句（IsBounceReply 防无限循环；2026-08-13 50%→25%）
```

**三层丢弃**（全部带 `[ImReply] 丢弃…` 日志）：
| 层 | 机制 |
|----|------|
| pending（未生成） | 玩家发新消息 → `CancelStaleFollowUps(convId)`（PriorPeerId 非空 = 链条任务） |
| 在途（已生成） | DeliverItem 入队记 `EnqueueMsgCount`（频道消息数），投递时频道已更新 → 丢 |
| 重复 | 与频道最后一条逐字相同（= 模板降级重复，正常对话逐字相同概率≈0）→ 丢 |

**线程模型**：LLM continuation 在线程池 → **结果只入队，主线程 Tick 消费投递**（Store/UI 全主线程）。
🔴 **禁止在主线程 `task.GetAwaiter().GetResult()` 等 LLM** —— async-over-sync 死锁（主线程阻塞，continuation 回不来 → 游戏冻结无崩溃，实机踩过）。

---

## 群聊记忆方案 B（参与度写入）— `ImChatManager.WriteGroupMessageToMemory`

**解决**：群聊消息全部写进每个成员记忆 = N 份拷贝 + 私聊漏斗被频道刷屏；完全不写 = NPC 私聊时想不起频道说过什么。

```csharp
// 群聊消息按参与度写入：只写给「说话人 + 相邻说话人」，旁观者不写
// 防重复补写：新说话人不在上一消息参与者集合（{S_{i-1}, S_{i-2}}）才补写（A-B-A 三连不重复）
ImChatManager.WriteGroupMessageToMemory(conv, speakerId, speakerName, content);  // public，事件广播也走它
```

- Role = `channel_{channelId}`，与私聊 `im_user/im_npc` 隔离——**私聊 UI 按 Role 过滤天然不混入**
- 旁观者的频道认知靠**公区注入**（回复 prompt 的【频道近期消息】段，ImChatStore 最近 8 条带发言人）
- 记忆总结（BuildPromptForSummary）给 channel 行加「（频道）」来源标注 + "没参与的只记梗概"
- 叙事边界：频道消息是成员亲历（在场），旁观者不写是"认知选择"不是上帝视角

---

## 回应模式人格化 — `ImChatManager.GetResponseMode(self, peer)`

**解决**：跟随回复"自由发挥"→ LLM 默认平庸复读；且随机分配会与 NPC 性格画像打架（LLM 服从 persona 拒绝执行冲突指令）。

```csharp
string mode = ImChatManager.GetResponseMode(self, peer);   // 反驳/附和/阴阳/感同身受/随和
```

**分配规则（优先级从高到低）**：
1. **trait 强信号**：Valor≥2 / Honor≤-1 → 反驳；Mercy≥2 → 附和；Calculating≥2 → 阴阳
2. **性格画像关键词修正**（弱 trait 时）：Persona 含"平和/心软/恻隐"→ 附和；"急/直/冲/嘴贫"→ 反驳（人设一致性优先）
3. **稳定 hash 加权**（同一个人永远同一人格）：反驳 40% / 阴阳 30% / 附和 20% / 感同身受 10%（"一部分抬杠，少部分顺着说"）
4. **关系极值**：至交（≥50）强制附和；宿怨（≤-30）强制反驳

配套：`DescribeRelation(self, peer)`（好感档位 + trait 性格调色）；prompt 指令**禁止固定句式**（"XX说得在理"= AI 味，LLM 会复用最省力的句式，必须显式禁止）；反驳型要"**抓话里具体破绽**"（不切实际/吹牛/自相矛盾）而非泛泛而喷。

---

## 事件广播线程模型（三段式）— `ImChat/ImEventBroadcaster.cs`

**解决**：玩家大事件（战斗/坐牢/任务/新人/洗劫/王国兴灭）→ NPC 主动挑起话题；🔴 事件回调在主线程，同步等 LLM 会死锁。

```csharp
// 主线程入口（真实事件 MyBehavior + 调试指令 custom.im_test_event 共用）：
// ① 同步段：防刷屏闸门（每 NPC 180s / 同类型 300s / 每日 10 条）+ 挑人（热度最高者）
// ② 异步 fire-and-forget：LLM 生成评论（3s 预算，失败走 LWN_im_event_* 模板）
// ③ 主线程 Tick 消费：写频道 + 记忆 + 未读 + NotifyNewMessage（NinjaReport 横幅）
ImEventBroadcaster.BroadcastPlayerEvent(eventKey, description);
```

- 30% 概率另一 NPC 接话（`ImReplyService.ScheduleFollowUp`，prior = 话题发言者 + 台词 + 回应模式）
- 挂载事件：`OnPlayerBattleEnd` / `HeroPrisonerTaken/Released` / `QuestLogAdded` / `NewCompanionAdded` / `VillageBeingRaided`（仅玩家村庄）/ `KingdomDestroyed`

---

## 选人增强（@提及 + bigram 相似度 + 沉寂补偿 + 随机+保底）— `ImTopicMatcher`

**解决**：关键词主题表对"人名/复杂表达"全 miss → 打分退化成纯热度+随机。

```
score = Σ 命中主题×职业亲和 + @提及(5) + 相似度(bigram×3) + 热度(≤2.5) + 沉寂(0~2.5) + 抖动(0~2)
```

- **@提及候选**：全名 / 去引号全名 / 引号内称号 / FirstName——玩家打"百草药僧"（称号）也能点名（全名含引号 IndexOf 必失败）
- **bigram 相似度**：个体指纹 = 名字/职业/家族王国/人设三字段/百科/本人最近发言（5 分钟缓存）——"谁说过什么谁回应"（玩家问"谁说的百来号人"→ 当事人被选中而不是背锅者）；指纹只取**本人发言**（玩家连发会把别人挤掉）
- **沉寂补偿**：没回过话 +2.5（新人必开口）；久未回话按墙钟递增
- **热度上限 4→2.5**：热度衰减按游戏日（同一天会话内恒定），高互动者会永久垄断
- **随机（纯随机，无保底）**：跟随 ~6% 随机（2026-08-13：保底 `ImFollowUpGuaranteeEvery` 移除——"满 N 条必跟"是可预测的假随机，出戏；纯随机 7 连不中实机出现过，玩家可接受，主回复兜底频道活力）

---

## 🔴 闲聊高风险动作 → 决策卡片（IM 确认面板复用，与计划卡片同构）— 2026-08-11 / 2026-08-12

**解决**：NPC 闲聊回复带高风险动作（ATTACK/DUEL/KNOCKOUT/STEAL）时，原生 `ShowInquiry` 弹窗只有一个"来战！"按钮（玩家不能拒绝），且与 IM 聊天流割裂。改为投递 **决策卡片**（同意/拒绝）——与密令 PlanCard、NPC 主动提议（ReactiveAgent）同一套确认 UI。

🔴 **2026-08-12（用户裁定：计划模式消息按钮 = 通用交互结构）**：闲聊决策卡片 / NPC 主动提议 / 计划卡片 三套 UI 合并为**一张「卡片气泡」**——NPC 自述形态（名字行 → [修改版徽标(仅计划)] → 正文 → 通用按钮行），按钮行**数据驱动**：

```csharp
// ImButtonVM（ImChatVM.cs）：通用按钮数据项
//   Text / IsEnabled（置灰）/ Execute()（Command.Click="Execute" 绑定）——一个按钮一个委托
// ImMessageVM.CardButtons（MBBindingList<ImButtonVM>）：按钮集按 AnchorCard 种类/状态重建
//   RebuildCardButtons()：计划卡片 = 同意/拒绝（执行中 = 中止；🔴 2026-08-19 计划自审/重拟按钮已删，
//                          PlanCard 自带 narration 即人话，语义问题交给执行期 Guardrail R1-R7 + Replan）；
//                         提议卡片 = 同意/拒绝（批准 = HandleProposal(approve:true) → 直接执行动作或走计划管线）
// ImMessageVM.IsCardAnchor：按钮行可见性（ImChatView.UpdateCardAnchors 每次刷新重算）
// 锚点规则（合并旧 UpdateLatestProposalFlag + UpdatePlanAnchors 为 UpdateCardAnchors 单规则）：
//   会话内「最新可操作卡片」（最新未决 Proposal 或最新待批/执行中 PlanCard）→ 锚点消息 =
//   计划卡片自身（链机制保留：拒绝抛弃时按 ChainId 抹除）；提议无链 = 卡片自身。旧卡按钮行隐藏（视觉保留）。
//   两种卡片并存时新者接管锚点，旧卡未了结、回流后恢复可点。
```

**关键纪律**：
- **两条路径彻底分离**：IM 路径（`HandleImAction`）拦截为卡片；当面对话路径（ReactiveAgent → `HandleAction` 直接）保留原生弹窗。拦截只放 `HandleImAction`，`HandleAction` 不动。
- **防死循环**：批准后的再执行必须 `bypassConfirm: true`（否则二次拦截 → 无限投卡）。
- **防二次弹窗**：批准后走 `ExecuteCore`（`alreadyConfirmed: true`），否则 Execute 的弹窗包装又弹一次。
- **文案复用**：卡片 Content = 各动作现有确认弹窗 key（`LWN_ui_interact_inquiry_duel_msg` 等），零新增本地化。按钮文案 = 计划卡片同款 同意/拒绝（`LWN_im_btn_approve/reject`）。
- **发卡前预检**：空间裁剪（`ResolveSpace`）+ `IsValid` 不过 → 不发卡（防"同意后无法执行"的死卡），与 HandleAction 降级 NONE 同语义。
- **载荷字段**：`ImMessage` 加 `ActionCode/ActionTarget/ActionLevel`（全 JSON 存档，向后兼容；空 ActionCode = 既有 NPC 主动提议 → RequestCommand 计划管线，行为不变）。
- 卡片投递：`ImChatStore.AppendGroupMessage(conv.Id, ...)` + `IncUnread` + `BroadcastMessageArrived`（私聊/群聊通用——**频道群聊中某人对玩家做动作同样走此结构**）。

**UI 层纪律（2026-08-11 实机三修 + 2026-08-12 统一）**：
1. **卡片内部必须 ListPanel 垂直堆叠**（名字行 → 内容 → 按钮行）——普通 Widget 子元素全部 Layout 到同一 rect 会叠字（ui.md「贴内容气泡」同坑）。Id 带 `LWN_` 前缀 + `VerticalBottomToTop` 声明（走 StackLayout swap patch，v1.2.12/v1.3+ 一致）。
2. **多卡并存：UI 全保留（流式历史），效用上仅最新未决卡按钮有效**——`ImMessageVM.IsCardAnchor`（UpdateCardAnchors 每次刷新重算：最新可操作卡片 + 锚点位置 = 链内最新/自身），旧卡按钮行 IsVisible=false 隐藏不可点；作废卡片置 `ExecutorId="done"` 后**必须全量重建消息列表**（按钮行是重建式数据，增量追加不刷新已存在消息）。
3. **同意后自动 `Close()`**（拒绝不关）——执行完动作直接关面板，开打了玩家该盯屏幕。

---

## 闲聊动作决策播报（谁决定要干嘛 + 参数）— `Interaction/InteractionController.cs`（ActionHandler）— 2026-08-13

**解决**：LLM 回复 JSON 决策出非 NONE 动作时，玩家只看到台词（冒泡 / IM 消息流），不知道 NPC 决定要干嘛——DebugLogger 有 `[ChatActionFlow]` 行但玩家不可见。要求：**任何动作决策落地都 DisplayMessage 播报**「谁决定要干嘛 + 参数」。

**挂点（唯一汇合点，一处覆盖所有入口）**：`ActionHandler.HandleAction` 内、`IsValid` 通过后、`Execute`/`ExecuteCore` 前。IM 回复（`HandleImAction`）/ 当面对话 respond / 说服会话（`PersuadeSlot`）/ 旧对话路径全部经过此处，零散播报点无需新增。

**播报格式**（`LWN_action_decide*` 四键，铁律 13 全本地化）：
```
{NAME} 决定：{ACTION}（目标：{TARGET}，{PARAM}）。
斯唐纳夫 决定：前往（目标：努勒丹）。   ← 日志示例：move_to（target: 努勒丹）
阿速甘 决定：送上（150 金币）。
```
- **NAME** = attacker 名（`attacker?.Name`；模板 NPC 无 Hero 退回 agent 名，铁律 8）
- **ACTION** = `ImCommandFlow.PlanActionLabel`（改为 internal 共用 `LWN_plan_action_*` 同表——闲聊动作码与计划步骤标签一份映射，防两份漂移）
- **TARGET** = LLM 目标文本优先，缺省用解析出的 defender 名（私聊语境 LLM 常省略目标）
- **PARAM** = 按动作码 C# 注入（铁律 2，LLM 只给档位）：`GIVE_GOLD`→金币数（`GoldLevelAmount` 换算）+ `LWN_action_gold_unit`；关系类→档位词（`LWN_action_level_*` 小/中/大）；`EMOTE`→动画 key

**纪律**：
- 只播**实际执行**的动作：IsValid 之后调用；降级 NONE（冷却中 / 空间不符 / 条件不满足）不播——没决策就没播报
- 🔴 `alreadyConfirmed`（IM 决策卡片已批准）**不播**——卡片本身就是决策展示，双报刷屏
- 高风险当面对话（ATTACK/DUEL/KNOCKOUT/STEAL）：播报 + 原生确认弹窗并存（弹窗确认回调走 `RunActionCore` 不再回 HandleAction，无二次播报）
- 异常 catch 进 DebugLogger，播报不炸执行链

**新增本地化**：`LWN_plan_action_*` 补 11 个闲聊动作码标签（attack/relation_up/relation_down/praise/spread_rumor/threaten_verbal/promise/marry_success/join_clan/gather_to_player/party_patrol）+ `LWN_action_decide*` 4 键 + `LWN_action_level_*` 3 键 + `LWN_action_gold_unit`；`{PARAM}` 占位符已登记 `validate_localization.py` 白名单。

---

## 🔴 闲聊动作空间模型（ActionSpace 三态）— `Planner/ActionHandler.cs` ResolveSpace — 2026-08-13

**解决**：动作空间由**执行人 attacker 与目标 defender 双方**是否在 Mission 内决定（不是玩家 `Mission.Current`——执行人/目标可能没进场景）：

```csharp
[Flags] enum ActionSpace { InScene=1, Remote=2, Party=4 }
// InScene = 双方都在 Mission 内（场景动作：走位/物理/当面仪式——目标在不在跟前由动作自身
//           IsValid/执行器判断，如 move_to 走过去即可，不进空间位掩码）
// Remote  = 一人在 Mission 内、一人在 Mission 外（跨场景远程语义：关系/声望/记忆/传话）
// Party   = 双方都在 Mission 外（Campaign 大地图：部队动作）
ResolveSpace(attacker, defender)：双方 IsPresentInMission → (in,in)=InScene / (!in,!in)=Party / 其余=Remote
```

**动作 Spaces 归类**：物理动作（move_to/follow/knockout/steal/duel/emote/say_to 等 16 个）= `InScene` 只——目标不在场景天然降级（如场景内随从 A 找没进场景的 B → Remote → move_to 降级 NONE）；关系/声望/记忆类（relation_*/praise/spread_rumor/threaten/promise 等 8 个）= 全空间；部队动作（party_patrol/gather_to_player）= `Party`。

**踩坑**：① 旧模型用玩家状态判空间 → 玩家在场景 IM 没进场景的随从被误判 InScene（执行人无 agent 载体，动作无法执行）；② `ImRemote` 概念（玩家在+对方不在场）被废除——"目标不在跟前"不是空间维度（场景内走过去即可），只有"不在 Mission 内"才是。

---

## 🔴 defender 解析：场景优先 + 执行期目标解析同口径 — `ActionHandler.ResolveImDefender` / `SceneSnapshot.FindAgent` — 2026-08-13

**解决（实机两次失败修复）**：LLM 回包 `action_target` 是简称（"那弥斯"），从文本解析到目标 agent 要过两关，两关口径必须一致：

1. **卡片阶段 `ResolveImDefender`**（名字→Hero）：**世界 Hero 匹配两轮——先匹配当前 Mission 场景内的同名 Hero，再全局兜底**。骑砍2 NPC 名 = 「地名+名字」组合（卡诺洛斯的那弥斯），多个村庄有同名乡绅——`AllAliveHeroes` 遍历先撞上别的村庄的同名 Hero（不在场景）→ `defenderIn=False` → Remote → knockout 拦截「不行动」（实机：匹配到 CharacterObject_1772 而非当前村的 2186）。
2. **执行阶段 `SceneSnapshot.FindAgent`**（名字→agent）：**显示名子串匹配**（"那弥斯" ⊂ "卡诺洛斯的那弥斯"）——卡片阶段 `NameMatchesHero` 是子串匹配，执行期必须同口径；原来只精确匹配 → 卡片发出去、执行期解析失败 → 步骤 2ms 瞬死（实机：44.510 开始 → 44.512 超时）。多匹配取最近（bestDist 既有）。

**纪律**：defender 解析与执行期目标解析必须同一匹配口径（子串）；模板 NPC（无 Hero）走 `FindTemplateNpcCandidates` 不经过 Hero 匹配。

## 🔴 多消息分时投递（说话节奏，2026-08-15 实机）— `ImChat/ImChatManager.cs` 延迟队列

**问题**：回复链多条消息（npc_reply + risk_analysis + 告知/决策卡）同帧同步投递 → 11ms 三句齐发，像机关枪（实机 08:40）。

**方案**：主线程延迟队列（Tick 消费），间隔**与前句字数挂钩 + 随机抖动**——模拟真人读句。

**关键签名**：
```csharp
// 间隔估算：前句字数 × 0.05s + 0.3s 基准 + 随机 0~0.6s，钳制 [0.6, 3.5]s
public static float SpeechPauseFor(string prevText);
// 延迟投递 NPC 消息（到点 → DeliverNpcMessage）
public static void ScheduleDelayedNpcMessage(ImConversation conv, string npcHeroId, string npcName, string content, float delaySec);
// 延迟执行主线程动作（决策卡投递/动作执行；Mission 已切换由动作内部 null-guard 自保）
public static void ScheduleDelayedAction(Action action, float delaySec);
```

**范式**（`RiskAssessor.RouteRisky` 范本）：npc_reply 立即投递 → 台词延迟 d1（= SpeechPauseFor(npc_reply)）→ 决策卡/执行再延迟 d2（= SpeechPauseFor(台词)）。

## 🔴 卡片按钮可见性时序（2026-08-15 实机 08:38 两连 bug）— `ImChat/ImChatView.cs` + `ImChatVM.cs`

**问题**：按钮数据构建成功（CardButtons=2）但按钮行不可见——`[SuggestBtn] 已构建 2 按钮 → IsCardAnchor=False` 而后 `[CardAnchor] anchor=True`。

**根因**：`UpdateCardAnchors` 旧顺序 `vm.AnchorCard = card`（触发 NotifyPlanState → RebuildCardButtons，此时 IsCardAnchor 还是旧值 False）→ `vm.IsCardAnchor = true`（后设置）——按钮构建时可见性未就绪，联动丢失。

**修复（双保险）**：
1. **顺序反转**：先 `vm.IsCardAnchor = ...` 再 `vm.AnchorCard = card`——可见性先就绪，按钮数据后到（MBBindingList 添加自动刷新数据源）
2. **构建后强制广播**：`RebuildCardButtons` 末尾 `OnPropertyChanged(IsHorizontalButtons)` + `OnPropertyChanged(IsVerticalButtonsVisible)`——无论构建时序，最终状态重评估

**调试日志**：`[CardButtons]`（重建入口+锚点种类）/ `[SuggestBtn]`（构建判定变量+可见性）/ `[CardAnchor]`（锚点竞争结果，**节流：latestCard 引用变化才打**——UpdateCardAnchors 每 0.3s 轮询，不节流会刷屏）。

---

## 🔴 认知同步与反馈层（2026-08-16 方案 A/B/C/D/K/M/N/O/P/S/R）— `ImChat/` + `Core/PlayerMissionEventLogic.cs` + `ImChat/ImEventBroadcaster.cs`

**解决什么问题**：随从"该知道但没人说出口"的事实无通道；玩家即时状态（残血/被抓现行）随从无秒级反应；事件广播只有事实没有情绪；大事被日常 FIFO 挤掉。本次补齐感知层 + 反馈层。

**感知层（D2，机制核心）**：`BroadcastPlayerEvent(eventKey, description, chatComment = true, memberFilter = null, important = false)` 两段式——① 感知层（总是）：写入全部队伍成员 `RecordDynamicMemory`（进【近期回忆】段，不产生幽灵聊天行）；闸门独立于话题层（同 key 300s + 描述去重 + 每日 30 条，`[Sense]` 日志）；② 话题层（chatComment=true 才走）：既有防刷屏 → 挑最健谈者（`PickSpeaker(memberFilter)`）→ LLM 评论。**调用约定**：mission_*/level_up → `chatComment=false` 只感知；大事 → true。**分兵口径（J3）**：亲历级（mission_*/crime/level_up）只写主队成员；公开级（battle/王国/任务/关系）扩写分兵随从。

**事件源**：
- `Core/PlayerMissionEventLogic.cs`（MissionLogic，D1/K/P/L 统计）：首帧分类（settlement→hideout/siege 攻守分流（`SiegeEvent.BesiegerCamp.LeaderParty == MainParty` 才"随军攻打"，否则"抵御围攻"——实锤 `Settlement.BesiegerCamp` 不存在）/settlement(+子地点)；野战→mission_battle 带最近定居点锚点+参战人数）+ K1 血线关切（<0.6 挂彩/<0.35 重伤，每档一次，回血 ≥0.7 重置，90s 冷却，15m 距离上限，SpeechChannel Warning 优先级）+ G3①/K2 犯罪感知（`ReportPlayerMisconduct(actionTypeWord)`——Steal/AttackAlly/Knockout 复用 LWN_crime_witness_act_* 模板；同场景随从记忆照写，无第三方目击只影响世界层不影响随从亲历）+ P1 行为亲见（场景 tag smithy/tavern + 位置 + 静止，300s 冷却）+ L1 战斗统计（`OnAgentRemoved` 击杀计数 + 血 <0.5 负伤标记，`TakeBattleKills/TakeBattleWounded` 供 battle_win/lose 消费）
- `MyBehavior.cs`（D3/O/Q）：5 个新事件挂载（实锤签名：`KingdomCreatedEvent(Kingdom)` / `HeroLevelledUp(Hero, bool)` / `OnSettlementOwnerChangedEvent(Settlement, bool, Hero, Hero, Hero, Detail)` / `BeforeHeroesMarried(Hero, Hero, bool)` / `OnChildConceivedEvent(Hero)`——只广播 MainHero 分支）+ O 关系动态（`HeroRelationChanged(Hero, Hero, int, bool, Detail, Hero, Hero)`——涉及 MainHero + |Δ|≥25 或跨档位（友好≥20↔中立↔反感≤-10），话题层 30%）+ Q 画像计数（`Memory/PlayerImageStore.cs`，SyncData JSON 小 key 纪律）

**情绪推导（M）**：`EmotionClause(eventKey)` — C# 确定性映射（battle_lose→"（主公此刻心情低落…）"等 9 组），描述 = 事实 + 情绪句两段；GetFallback 纯事实不动（兜底不携带情绪）。

**大事记（N）**：`SingNpcMemorySystem.RecordImportantMemory(desc)` — ≤12 FIFO，写入时 C# 白名单分级（kingdom_created/fief_granted/marriage/child_born/imprison/release + 限定版 battle_win——调用方传 `important:` 判定：攻城战胜利或大捷参战人数比 ≥2（`MapEvent.IsSiegeAssault` + `GetNumberOfInvolvedMen(side)`））；存档走 NpcMemorySaveEntry 新字段（旧档空 → 不补写）；prompt【大事记】段（GetPrompt_RespondContext 顶部）。

**口嗨检测（C）**：`ChatClaimChecker.CheckAndMark(reply, actionCode, needPlan, adjustPlan, speakerName)` — 声称表（带路/请客/这就动身/时间承诺/包办/动手/去办某事/必当定当 + "我一定"+动作后缀收紧 + 英文兜底）× 守卫（前 4 字符内 否定/转述/过去时/条件式）→ 声称+零执行路径 → `LWN_im_bragging_tag` 前缀（（吹牛））+ `[Bragging]` 日志。接入点：ImReplyService（SanitizeReply 后）+ ReactiveAgent respond。**与 J/R 联动**：动作注册了才是真的、没注册就是吹牛。

**即时关切（K）**：确定性模板 + SpeechChannel（护主不告发）；与 M（异步 LLM 情绪长句）分工：K 先到、M 后到。

**政治动作空间（R）**：`Core/KingdomPoliticsFlow.cs` — 身份判定（`IsLord`/`IsKing`/`HasDefectionTendency`）+ 4 动作（persuade_join 检定 `SingleRollResolver.Roll` + `ChangeKingdomAction.ApplyByJoinToKingdom`（实锤 4 参）；propose_war/negotiate_peace 走原版决策管道（`DeclareWarDecision(Clan, IFaction)` / `MakePeaceKingdomDecision(Clan, IFaction, …)` + `Kingdom.AddDecision`——**禁止直改战争状态**）；order_march `SetPartyAiAction` 全家桶）；ActionRegistry 新增 `IdentityGated` 字段（GetActionSpacePrompt 任何空间跑 IsValid——身份维度过滤）。**决策结果广播（R 反馈链，2026-08-16 补）**：`CampaignEvents.KingdomDecisionConcluded`（实锤 `IMbEvent<KingdomDecision, DecisionOutcome, bool>`，类型在 `TaleWorlds.CampaignSystem.Election` 命名空间；1.2.12 亦存在）——`ProposerClan == Clan.PlayerClan` 才广播（他人提案不广播），outcome 实例化判断通过/否决（`DeclareWarDecisionOutcome.ShouldWarBeDeclared` / `MakePeaceDecisionOutcome.ShouldPeaceBeDeclared`），key=`kingdom_decision`——设计哲学原则一：投票 1-3 天出结果，结果必须让随从知道（禁止静默）。

**受困求情（S）**：`Interaction/Dialogue/DistressFlow.cs` — `IsPlayerCaptive`（IsPrisoner/被押解/`sp_prisoner` tag）/`IsPlayerCaught`（PendingWorldEvent 有真实目击者）+【受困处境】段（respond 链路看守 prompt）+ 3 动作（pay_ransom/beg_mercy/bribe_guard——`IdentityGated` 受困门控；金额 = 对方说了算（`ComputeCost(Restitution)` 统一入口或勒索基础值）；转账守恒 `AgentControlHelper.TransferGold`（看守无 Hero → null 虚空 Sink，铁律 4 合法）+ 释放 `EndCaptivityAction.ApplyByRansom(character, facilitator)` 实锤；贿赂检定可失败：钱没了罪还在）。

**DebugLogger 前缀**：`[Sense]`（感知写入+闸门）/ `[Care]`（关切触发+冷却+跳过原因）/ `[Bragging]` / `[StaleFact]` / `[Kingdom]` / `[Distress]` / `[Party]` / `[ImEvent]`。

## 🔴 墙钟驱动：后台任务禁止依赖 CampaignEvents.TickEvent（2026-08-17 二次踩坑）— `ImChat/ImScreenFrameTickPatch.cs` + `ImChat/ImChatView.cs` + `LLM/WorldBackgroundBehavior.cs`

**解决什么问题**：`CampaignEvents.TickEvent` 的 dt 是 CampaignTime 增量——**游戏暂停（ESC/家族屏/任何菜单）时 dt=0 事件停发**。依赖它的后台任务在玩家暂停时永不运转（IM 08 月、世界背景生成 08-17 两次实机踩坑，日志零痕迹）。**墙钟轮子 = `ScreenBase.OnFrameTick`（Harmony postfix，引擎渲染循环 UI 层回调，暂停照常每帧触发）**——wheels 索引已登记，新后台任务先查这里。

**双端入口（挂这里，一次覆盖 Mission + Campaign）**：
- Campaign/菜单：`ImScreenFrameTickPatch`（`[HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]` postfix → `ImChatView.OnScreenFrameTick`，门控：`Campaign.Current == null` 跳过 / `Mission.Current != null` 跳过（Mission 由 MissionView 驱动，防双驱动））
- Mission：`ImChatMissionView.OnMissionTick`
- 统一汇入 **`ImChatView.Tick(float dt)`**——后台任务都在这挂一行：`WorldBackgroundBehavior.Instance?.OnFrameTick(dt)`

**WorldBackgroundBehavior 范式（后台 LLM 生成任务骨架，照抄）**：
- **静态实例挂接**：CampaignBehaviorBase 的 `RegisterEvents` 设 `Instance = this`（SyncData 存档生命周期保留；退档悬挂由 `?.` 空安全兜底，进档新实例覆盖）
- 状态机 `Idle / Generating / Done / Failed`（实例字段，进档天然复位；Failed = 本会话不再重试，防 LLM 宕机时重试风暴）
- **快速首检 + 低频重试（2026-08-17 用户裁定）**：首次 `FirstCheckIntervalSeconds=5s`，之后 `GenerateIntervalSeconds=15s`；**Done 后保留 300s 指纹巡检**（`RecheckIntervalSeconds`——会话内阵营覆灭/新建/领袖更替 → 指纹变 → 重新生成，动态世界漂移兜底；巡检空指纹跳过防误重生成）；Failed 停止轮询（失败不重试，防重试风暴）；未配置 LLM / 数据未就绪 → 保持 Idle 按 15s 重试（MCM 填好配置后下个 tick 自动触发，无需重进档）
- 结果消费：LLM 线程池回写 → `lock` 入队 → 主线程每帧 `ConsumeResult`（async-over-sync 死锁纪律同「事件广播线程模型」三段式，wheels.d/im.md 本卷）
- **指纹失效判定**：`culture/kingdom/hero(StringId 排序序列，每王国 ≤3 关键英雄)+lang` 指纹——blob 空或指纹变 → 重生成；结果回来**复核指纹 + 战役纪元**（`Campaign.Current` 实例引用比较）不符丢弃（语言切换/读档跨战役污染防护）；lang 口径必须 `LWNTextHelper.GetReplyLanguageInstruction()`（禁裸传 ActiveTextLanguage，口径错位误重生成）
- 铁律 1：`!IsLLMConfigured` → 保持 Idle 等待

**调试**：`custom.worldbg_status / regenerate / dump`；日志 `[WorldBg]`（读档初始化 blob/指纹 / 指纹匹配含**存档 vs 当前两个比对值** / 触发 / 生成成功 / 失败）。

**小坑（validator A 节）**：`DebugLogger.Log($"...中文" + $"...")` **续行**里没有 `DebugLogger.` 字样会被误报硬编码 CJK——续行加 `// lwn-ignore: A`（WorldBackgroundBehavior.cs:139 实录）。

## 🔴 光标锚定止血补丁：全局光标可见性聚合规则（2026-08-18）— `ImChat/ImChatCursorPatch.cs` + `ImChatView.ShouldForceHideCursor`

**解决什么问题**：手柄 IM 导航态下系统光标被原生锚定模式锁死屏幕中央，**alt+tab 到游戏外鼠标仍被锁死**（每帧 set_cursor_position，失焦不停），玩家无法正常做其他事。

**根因链（反编译实锤）**：
1. `ScreenManager.UpdateMouseVisibility()` 聚合规则 =「**任一活跃层 `InputRestrictions.MouseVisibility=true` → 全局光标显示**」（第一个命中即 return）——vanilla MapScreen 层恒 true（大地图光标悬停交互是原版设计），IM 层 `SetInputRestrictions(false, ...)` **藏不住**
2. 「手柄在用 + 可见光标」→ native 锚定模式：光标 = 屏幕中心 + 摇杆向量，每帧覆盖 `SetMousePosition`（原生 `IInput` 无管理侧开关，见 im-gamepad-navigation.md §11.2 坑 2），且**失焦不停止**

**轮子（在聚合源头拦截，层级藏不住）**：
```csharp
[HarmonyPatch(typeof(ScreenManager), "UpdateMouseVisibility")]   // internal static，字符串补丁
static bool Prefix()
{
    if (!ImChatView.ShouldForceHideCursor()) return true;        // 放行原聚合
    AccessTools.Method(typeof(ScreenManager), "SetMouseVisible") // private static，反射调
        ?.Invoke(null, new object[] { false });
    return false;                                                // 跳过聚合 → 破锚定
}
```
门控 = `ShouldForceHideCursor()` = **IM 打开 + 手柄（去抖值 `_lastUsingGamepad`）+ 非输入框聚焦**（导航态）。**放行边界**：输入框聚焦态（原生速度模式，光标需要可见可点）/ 鼠标态 / IM 关闭。

**教训**：`SetInputRestrictions(false)` 只是"本层声明"，全局光标可见性由 ScreenManager 按「任一活跃层 true」聚合——**想藏光标必须看有没有别的层在拉**。多版本兼容：ScreenSystem 的这两个方法各版本签名一致，无需 `#if`（编译时二进制 grep 验证过）。

## 🔴 mask 缓存跨层生命周期残留（2026-08-19 实机：IM 第二次打开光标消失）— `ImChatView.Close` 重置 `_lastCompactMask`

**解决什么问题**：同进程内「开 IM → 关 → 再开」，第二次打开光标消失（Mission 内鼠标转镜头 + 光标钉屏幕中央）。第一次打开永远正常——**只有第二次及以后坏**。

**根因链（日志实锤）**：
1. 8-15 性能优化：`ApplyInputMask` 加缓存（`_lastCompactMask`/`_lastCompactMaskVisible`），**mask 变化才调 `SetInputRestrictions`**——每次 Open 都无条件调用改成了"变化才调用"
2. **Close 只重置了层实例的 InputRestrictions（层随后销毁），静态缓存没重置**
3. 第二次 Open：新建 GauntletLayer → `ApplyInputMask` 目标 mask == 残留缓存 → **`SetInputRestrictions` 被跳过** → 新层用**默认 InputRestrictions（光标隐藏）** → `ScreenManager.UpdateMouseVisibility` 聚合无层拉 true → 全局光标隐藏 → Mission 相机恢复 `GetMouseMoveX/Y` 转镜头

**修复**：`Close()` 里层销毁处重置缓存（`_lastCompactMask = InputUsageMask.Invalid; _lastCompactMaskVisible = false;`）——层销毁 = 缓存失效，下次 Open 强制重新应用。

**诊断方法**（本次实测流程，可复用）：在 `SetInputRestrictions` 调用处打无条件日志（`[ImChatMask]` 只在实际应用时打印）——**跳过时没有日志行 = 实锤缓存命中**；配合 2s 节流采样 `ScreenManager.GetMouseVisibility()`（`cursor=`）+ `Input.MousePositionPixel`（光标是否钉住）一眼定案。

**通用纪律**：任何「变化才应用」的缓存（性能优化产物），**跨对象生命周期（层销毁/重建、屏切换）必须重置**——"变化才调用"的前提是缓存与对象状态同步；对象销毁后缓存成为孤儿，比较永远命中跳过。

## 🔴 手柄导航：光标跟随焦点（A 键 native 点击命中焦点项，2026-08-19 用户裁定）— `ImChatView.SetMouseToWidget` + `ImChatSoftKeyboardPatch`

**解决什么问题**：手柄 A 键聚焦输入框/激活按钮后，设备判定翻键鼠 → 导航门控死锁（坑 13，实机 09:48/09:52/09:59 三证）。

**根因链（反编译全链实锤）**：
1. `FocusInputWidget` 设 `EventManager.FocusedWidget = input` → setter 里 `IsControllerActive && EditableTextWidget` → `_isOnScreenKeyboardRequested=true`
2. 引擎 `EventManager.LateUpdate` 消费 → `Platform.OpenOnScreenKeyboard(...)` → **PC 无软键盘 → native 立即回调取消**
3. 取消回调（可能走 `GauntletLayer.OnOnScreenKeyboardCanceled` 也可能**直接调 `UIContext`**——两层都要补丁）→ `CancelMouseClick()` → ① `ClearFocus()` 清掉我们的焦点 ② 模拟鼠标抬起 → **`IsMouseActive` 持续 true**
4. `IsGamepadActive = IsControllerConnected && !IsMouseActive`（Input.Update 每帧）→ 裸值翻 false → 去抖 0.2s 提交 → 门控死锁

**修复三件套**：
```csharp
// ① 核心（用户裁定：焦点变化 → 光标跟随）：A 键 = native「点击」语义，点在鼠标位置——
//    焦点转移时把光标挪到新焦点 widget 中心 → 点击命中焦点项本体（输入框=引擎点击聚焦路径，
//    与手动 FocusedWidget 一致）→ 不落空、不清焦点、不翻
private static void SetMouseToWidget(PadItem item)   // MovePad 转移后 + FocusInputWidget 前置调用
{
    var w = item.GetWidget?.Invoke();
    var gp = w.GlobalPosition; var sz = w.Size;
    Input.SetMousePosition((int)(gp.X + sz.X * 0.5f), (int)(gp.Y + sz.Y * 0.5f));
}
// ② 软键盘取消链双层补丁（IM 层跳过）：GauntletLayer.OnOnScreenKeyboardCanceled/Done
//    + UIContext.OnOnScreenKeyboardCanceled/OnOnScreenkeyboardTextInputDone
//    （native 可能直接调 UIContext 不走层——只补层拦不住，实机 09:59 证）
// ③ 设备硬锚：手柄键按下沿 0.5s 窗口 + 输入聚焦态 → 钉住手柄语义（真实切鼠标窗口过期放行）
```

**坑中坑**：① Harmony **不能补丁抽象接口方法**（`ITwoDimensionPlatform.OpenOnScreenKeyboard`）——PatchAll 直接 HarmonyException 崩游戏启动；② 光标可见 = 锚定覆盖 SetMousePosition（坑 2），**光标隐藏时 SetMousePosition 有效**（⚠️ 诊断注意：光标隐藏时 `Input.MousePositionPixel` 读数是**冻结的旧值**——实机 A 键点击落在输入框本体证明 OS 光标已挪位，但读数停在 (960,540)，别据此误判 SetMousePosition 失败）；③ 程序 SetMousePosition 不算「鼠标活动」（十字键导航全程实测未触发 IsMouseActive）；④ 诊断铁证格式：`设备翻转未保护: 裸值= False 聚焦=False IsMouseActive=True 光标可见=False 鼠标位置=(960,540)`——鼠标位置残留屏幕中央 = A 键点击打空。已登记 im-gamepad-navigation.md §11.2 坑 13。

## 🔴 Steam Deck 弹窗键盘提交回填（2026-08-22 实装，门控 = SteamDeckKeyboard.IsSteamDeck()）— `ImChat/ImChatSoftKeyboardPatch.cs`

**背景**：PC 上 Done 链 = 取消回调（ClearFocus+模拟抬起 → 设备翻转死锁），IM 层整体跳过；**Deck 上有真软键盘，Done 链 = 提交回填（SetAllText），跳过 = 提交文字被吞（用户实测功能阻断）**。取消链（Canceled）Deck/PC 都跳过（焦点保持）。

**机制（反编译全链）**：Steam 提交 → `GamepadTextInputDismissed(提交)` → `OnTextEnteredFromPlatform` → `ScreenManager.OnOnscreenKeyboardDone(text)` → `FocusedLayer.OnOnScreenKeyboardDone` → `UIContext.OnOnScreenkeyboardTextInputDone` → **`FocusedWidget.SetAllText(text)`**（引擎内置回填，vanilla/MCM 层无需改动）+ `CancelMouseClick()`。

**IM 层 deck 分支**（两层都改，缺一必丢字——见坑 1）：
- `ImChatSoftKeyboardDonePatch`（GauntletLayer.OnOnScreenKeyboardDone）Prefix：IM 层 + Deck → **放行**（方法体 = base 空实现 + UIContext 调用——整体跳过 = 回填也跳，日志特征「只有 Done→Layer 没有 Done→Ctx」实锤）；PC 保持跳过。
- `ImChatSoftKeyboardContextDonePatch`（UIContext.OnOnScreenkeyboardTextInputDone）Prefix：`IsCurrentContext` + `IsSteamDeck()` → 自己 `SetAllText(inputText)`（绑定推送 VM InputText）+ **跳过 CancelMouseClick**（防设备翻转坑）；非 IM 层原版；PC 保持原跳过语义。`SetAllText` 引用包 `#if MB2_GE_130`（1.2.12 无软键盘机制）。

**配套**：`Input/SteamDeckKeyboardPatch.cs`（补丁 A：Deck 上鼠标/触屏点击 EditableTextWidget 也弹浮动键盘——引擎只在 `IsControllerActive && EditableTextWidget` 时请求，点击那帧 IsMouseActive=true 必 false → 直通盲打；补丁 set_FocusedWidget postfix 直接调 `Context.TwoDimensionContext.Platform.OpenOnScreenKeyboard`，参数复制引擎消费块）+ `Input/EditableTextBackspacePatch.cs`（直通软键盘退格 \b 吞键，见 input.md）。

**Deck 检测**：`SteamDeckKeyboard.IsSteamDeck()`——Steamworks 官方 API 反射调用（`Type.GetType("Steamworks.SteamUtils, Steamworks.NET")` → `IsSteamRunningOnSteamDeck`），无 csproj 硬引用，Epic/GOG 缺失时降级 false。**门控必须存在**：PC 上 OpenOnScreenKeyboard 立即走取消回调链 → 点中文本框即失焦（无条件触发会破坏 PC 文本框）。

## 🔴 手柄导航：自绘焦点准星（2026-08-19 用户裁定）— `ImChatView.UpdateNavCursor/HideNavCursor` + prefab `LWN_NavCursor`（GUI/Prefabs/ImChat.xml + ImChatCompact.xml）

**解决什么问题**：导航态系统光标被强制隐藏（见上节坑 2），焦点辨识只剩 hover 高亮——手柄玩家看不清焦点在哪个项。自绘准星 = 焦点框指示器（frame_small_9 sprite），导航态显示并跟随焦点 widget，框中心对齐控件中心。

**实现**：
- prefab：`LWN_NavCursor` = ImageWidget（Fixed 28×28，Sprite=frame_small_9，对齐显式 Left/Top，初始 IsHidden）——🔴 必须放**全屏根 Children 最后一位**：PositionOffset 相对父 = 根(0,0) = 屏幕坐标；放面板 Children 里会整体加上面板居中偏移（实机偏右下）；对齐默认 Center 会推偏，必须显式 Left/Top
- 定位：`PositionXOffset/YOffset` 是**逻辑坐标** = 屏幕物理坐标 / `UIContext.Scale`（`ScaledPositionOffset` 只读 = 逻辑×scale）——框左上 = 控件中心(物理/scale + size/2) − 框尺寸/2；框尺寸 = 控件尺寸 + 4px 余量
- 驱动：`UpdateNavCursor()` 在 `UpdatePadFocus` 顶部每帧调——显示条件 = 手柄（去抖值）+ 非输入聚焦 + 焦点项 GetWidget 非 null；其余隐藏；位置变化 >1px 才打 `[NavCursor]` 诊断行

**🔴 坑中坑（2026-08-19 实机 10:38:10 三连日志）**：准星 visible 时会把 **native 点击命中测试**吸走——`DoNotAcceptEvents="true"` 只挡 managed 命中（`EventManager.AnyWidgetsAt` 检查该 flag），**native 命中（`CollectVisibleWidgetsAt`，反编译实锤）不检查**；准星 = 根下最顶层 widget，盖在焦点项上时 A 键 native 点击先命中准星（ImageWidget 不可聚焦）→ 点击焦点链被吸走 → 手动设的 `FocusedWidget` 被清 → 0.5s A 键窗口过期 → 设备翻转死锁。**纪律：任何 A 键激活路径必须先 `SetMouseToWidget(焦点项)` + `HideNavCursor()` 再 OnActivate**——`ActivatePad` 统一入口已做（SetMouseToWidget 保点击命中项本体；HideNavCursor 保点击路径 = 无准星提交版逐字节一致；下一帧 UpdateNavCursor 自动恢复显示）。


---

## 🔴 私聊频道上下文：`GetPrompt_RespondContext(includeChannelRows)` — 2026-08-21 实机修复

**解决**：玩家在频道里说话后私聊 NPC"你知道我刚刚说什么了吗"，NPC 答"没听真切"——私聊 prompt 完全看不到频道消息。

**根因（两层过滤叠加）**：① `BuildChannelRecentSection` 对 Direct 会话直接 return null（私聊无【频道近期消息】段）；② 【对话历史】段显式跳过 `channel_` 角色行（设计是"频道段全量承担"——但私聊没有频道段）。

**修复**：`GetPrompt_RespondContext(memory, otherId, includeChannelRows)`——**群聊传 false**（有频道段，跳过防"同一批对话打印两遍"）；**私聊/无频道段传 true**（`BuildPrompt_ImReply` 按 `string.IsNullOrEmpty(channelRecent)` 判定）。true 时 channel_ 行不跳过，按 SpeakerId 过滤（玩家在频道的发言 `SpeakerId=PlayerId` 直接入选第一轮，NPC 自己发的走补足轮）。

**验证锚点**（实机）：私聊"我刚刚在说什么话" → NPC 完整复述"您方才说的是要偷那帝国弩手#55的兵器，又问我这频道是什么地界儿"。

---

## 🔴 频道段频道名标注 — 2026-08-21 实机修复

**解决**：玩家在家族频道问"这里是什么频道"，NPC 答"队伍说话的公区"——LLM 猜错频道身份。

**根因**：`BuildChannelRecentSection` 只拼消息行（`- {SenderName}: {Content}`），段标题固定"## Recent Channel Messages"——**prompt 里没有任何频道身份信息**。

**修复**：`ImReplyService.BuildChannelRecentSection` 内容首行标注 `（这里是{conv.Title ?? conv.Id}）`（`conv.Title` = 本地化频道名：队伍频道/家族频道/王国频道）。LLM 从此能区分三个频道。

**验证锚点**（实机）：家族频道问 → "咱们家族的密信频道"；队伍频道问 → "随从凑在一块儿说话的地界儿"。

---

## 🔴 私聊显示 = 记忆同源（无独立容量）— 2026-08-21 确认

**解决**：理解"私聊能显示多少条"——`GetDirectMessages`（ImChatManager）**从对方记忆 `RecentHistory` 的 im_user/im_npc 行构建**（需求 6：显示与记忆同步，上限随记忆容量）+ 本会话 store 命令消息（PlanCard/System）按时间戳合并去重（`(SenderName, Content)` 键，store 优先）。记忆总结断层 → 插入「淡忘」系统行。

**语义**：
- **私聊没有独立容量**——`RecentHistory` 是单一容器（私聊/频道/事件/计划/当面混装，上限 = `MaxRecentHistoryCount` 热度分档 40/20/8），私聊显示条数 = 容器内 im_* 行份额（频道活跃 NPC 的私聊行会被挤掉）。
- 前端消息流**全量渲染无二次截断**（`foreach msgs Add(new ImMessageVM)` + 滚动）——"后端取出多少，前端显示多少"；唯一 Take 是左栏索引 `Take(6)` 与预览字符串。
- 群聊显示 = store（每频道上限 100 条 FIFO，读档恢复同样收缩）——与记忆 tab 数据源不同是设计（公区流水全量 vs 参与度过滤 + 总结沉淀）。

---

## 🔴 传讯入口双闸 + 发送失败提示分层（2026-08-22 用户裁定）— `ImChat/ImChatView.cs` + `ImChat/ImChatManager.cs` + `Notify/ImChatOpenButtonManager.cs` + `GUI/SecretLetterButtonInjector.cs`

**解决**：IM 传讯依赖 LLM 才能正常玩——未配置 LLM 时模板回复是不得已的体验，不给入口；配置了但连不上要像测试连接一样红字提示理由；世界玩法交互（事件广播/背景/提议/respond）的模板降级保持静默不打扰。

**三层分层**：
1. **未配置（`!IsLLMConfigured`）→ 入口整体封死**：O/↑ 键（`ImChatView.OnScreenFrameTick` + `ImChatMissionView.OnMissionTick`）= `ShortFired(IM) && PlotEnabled && IsLLMConfigured`；呼出按钮 `ShouldShow` + 密信按钮（SecretLetterButtonInjector）同双闸；🔴 **`CanOpen()` 兜底**（`PlotEnabled && IsLLMConfigured && …`）——通知点击/按钮点击等一切入口统一汇入
2. **已配置但连不上 → 发送时红字提示（测试连接同款）**：IM 回复走 `ChatOnceAsync(..., showFailureAlert: true)`（见 llm.md）；`SendPlayerMessage` 兜底 `!IsLLMConfigured` → `ShowConnectionMessage(NotConfigured, BuildMissingFieldsText())`——覆盖「面板开着中途清空配置/读档异常」等边缘场景，**无 tick 监听关面板**（发送动作即检查点）
3. **非自由输入玩法 → 静默**：事件广播评论/世界背景/提议/respond 保持 `showFailureAlert=false` 模板降级

**纪律**：入口双闸与提示分层不可拆——未配置时入口已封，正常流程走不到提示环节；提示统一走 `ShowConnectionMessage`（5 分钟同原因冷却 + `LWN_llm_fail_*` 分类文案，零新增 key）。

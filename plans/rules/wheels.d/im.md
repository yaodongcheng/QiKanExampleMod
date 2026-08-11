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
// 跟随者投递后：50% 概率 bounce 主回复者再回一句（IsBounceReply 防无限循环）
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
- **随机 + 保底**：跟随 25% 随机 + 满 N 条消息必触发（`ImFollowUpGuaranteeEvery`）——纯随机 0.75⁷≈13% 连不中实机出现过，体验不能靠赌

---

## 🔴 闲聊高风险动作 → Proposal 提议卡片（IM 确认面板复用）— 2026-08-11

**解决**：NPC 闲聊回复带高风险动作（ATTACK/DUEL/KNOCKOUT/STEAL）时，原生 `ShowInquiry` 弹窗只有一个"来战！"按钮（玩家不能拒绝），且与 IM 聊天流割裂。改为投递 **Proposal 卡片**（同意/拒绝）——与密令 PlanCard、NPC 主动提议（ReactiveAgent）同一套确认 UI。

```csharp
// ActionDefinition 新字段（InteractionController.cs）：
RequiresConfirm = true;   // 高风险物理动作：IM 路径拦截为卡片
ExecuteCore = ...;        // 核心执行（卡片批准后直接跑，不再弹二次确认）
// Execute = 当面对话的原生弹窗包装，确认回调 → RunActionCore("CODE", ...)
// HandleAction(..., alreadyConfirmed: true) → 直接 ExecuteCore

// HandleImAction（IM 入口）：RequiresConfirm && !bypassConfirm → PostActionProposal（投卡片 return）
// ImChatView.HandleProposal：msg.ActionCode 非空 → 同意 = HandleImAction(bypassConfirm: true)
//   直接执行（空间/冷却/IsValid 复检全保留，NPC 离场自然降级）；拒绝 = ExecutorId="done" 了结
```

**关键纪律**：
- **两条路径彻底分离**：IM 路径（`HandleImAction`）拦截为卡片；当面对话路径（ReactiveAgent → `HandleAction` 直接）保留原生弹窗。拦截只放 `HandleImAction`，`HandleAction` 不动。
- **防死循环**：批准后的再执行必须 `bypassConfirm: true`（否则二次拦截 → 无限投卡）。
- **防二次弹窗**：批准后走 `ExecuteCore`（`alreadyConfirmed: true`），否则 Execute 的弹窗包装又弹一次。
- **文案复用**：卡片 Content = 各动作现有确认弹窗 key（`LWN_ui_interact_inquiry_duel_msg` 等），零新增本地化。
- **发卡前预检**：空间裁剪（`ResolveSpace`）+ `IsValid` 不过 → 不发卡（防"同意后无法执行"的死卡），与 HandleAction 降级 NONE 同语义。
- **载荷字段**：`ImMessage` 加 `ActionCode/ActionTarget/ActionLevel`（全 JSON 存档，向后兼容；空 ActionCode = 既有 NPC 主动提议 → RequestCommand 计划管线，行为不变）。
- 卡片投递：`ImChatStore.AppendGroupMessage(conv.Id, ...)` + `IncUnread` + `BroadcastMessageArrived`（私聊/群聊通用，与 ReactiveAgent 提议同款）。

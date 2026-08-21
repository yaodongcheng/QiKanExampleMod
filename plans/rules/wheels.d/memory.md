# memory — 轮子速查分卷（wheels.md 索引导航）
## 记忆系统三件套 — `Memory/`

```csharp
// 入口：拿某 NPC 的记忆系统（惰性创建）
SingNpcMemorySystem mem = AllNpcMemoryManager.GetMemoryForAgent(agent);
SingNpcMemorySystem mem = AllNpcMemoryManager.GetMemory(stringId);   // 按英雄 id
AllNpcMemoryManager.ClearTemporaryMemories();   // 清临时士兵记忆，防泄漏
```

- `SingNpcMemorySystem`：单 NPC 的 `RecentHistory`（对话）/`DynamicMemories`（近期）/`PermanentMemory`（远期）/`GlobalNews`/`CurrentNegotiationState`/`KnownEvents`。
- `NPCProfile`：人设容器。`GetPersonaPrompt()` 聚合全部人设；`CalCurrentMotivation()` 推动机；`CalculateEstimatedValue()` 算身价；`GetCloseRelations(...)` 取关系网。
- 给 NPC 加新「记忆维度 / 人设字段」时往这三件套加，不要另起 NPC 数据类。


---

## 🔴 确定性事件写记忆：`RecordDynamicMemory`（同步入口）— 2026-08-11 / 🔴 2026-08-21 改道

**解决**：战斗结果等主线程确定性事件要让 NPC 知道（LLM 总结管道 `AddDynamicMemory` 是 private async，且依赖对话历史素材）。

```csharp
mem.RecordDynamicMemory("刚与努勒丹交手，我赢了。");   // 🔴 2026-08-21 改道：写对话历史（Role="system"），不再直接写短期记忆
```

**🔴 通道语义变更（2026-08-21 用户裁定：短期记忆必须是从对话历史 LLM 提炼的）**：
- 旧行为（已废弃）：裸事件直接写 `DynamicMemories`（短期记忆）——实机出现"主公进了主公进了隆城中心"这类未提炼的裸内容。
- 新行为：`RecordDynamicMemory` = `AddHistory("system", content, "system")` → 进 `RecentHistory`（对话历史）→ 由既有 `MaintainMemoryAsync` 总结管道 **LLM 提炼后**才进短期记忆。
- `SpeakerId="system"` 的三个设计理由：① respond 选行（`IsNullOrEmpty(SpeakerId) || == otherId` 才入选）→ 事件行只在**补足轮**进【对话历史】段——真实对话优先，高频事件不挤占预算；② IM 渲染（`GetDirectMessages` 只认 `im_user`/`im_npc`）→ 不显示为聊天行（无幽灵消息）；③ 参与总结（与 `channel_nearby` 的"剔除不总结"相反——事件是有价值事实）。
- 延迟语义：事件要等对话历史超 2× 上限被总结才进短期记忆——期间由【对话历史】段兜底可见。
- 内容 = 第一人称 LLM prompt 材料（豁免铁律 13），中性表述交给 LLM 调口吻。
- 调用范例：`FightEnemyAction.OnEnd` 的战斗结果记录（见 agent.md「战斗结果 → 当事人记忆 + 队伍广播」）。


---

## 🔴 经历旁白记录（Experience Narration）— 2026-08-11

**解决**：玩家攻击 NPC 后，NPC 的 LLM 对话（IM 群聊/私聊、当面 respond）对被攻击一事一无所知——罪案档案（WorldEventStore）只被原版剧本对话读取，`GetPrompt_RespondContext` 没有任何"经历"通道。

**要点**：
- **主记录点 = 动作出队执行处**（AgentBrain.Tick 出队后 `RecordActionNarration`）——**只在动作真正开始执行时记录，无幽灵**：队列里被 ClearAllActions 丢弃的动作永远不会出队；且与实际行为一致（小孩的 `MoveToPositionAction.FleeFrom` 记为逃跑，不会错记成"上前相助"——事件决策点方案的教训，2026-08-11 实改）。
- **旁白定义在动作自身**：`IAtomicAction.GetNarration(Agent owner)`（AtomicAction.cs 各动作定义处）——值得记住的经历返回第一人称文本（`FightEnemyAction`："与X交战"；`MoveToPositionAction` 逃跑模式 [narration 参数/FleeFrom 工厂]："吓得逃走了"），机械/台词/持续状态动作返回 null（零噪声）。**🔴 所有 IAtomicAction 实现统一集中在 `AI/Actions/AtomicAction.cs`**（含反应链 Reactive* 与行为性内联适配器 InlinePlanAction，2026-08-11 迁移；原 FleeFromAction/ReactiveFleeAction 已并入 MoveToPositionAction）——新增动作只改该文件自身定义处，AgentBrain 零改动。
- **"被攻击" = 事件事实**（与击晕/认输同类）：`event_agent_damaged` 受害者分支直记 `"我遭到了X的攻击"`，门控 `!(EffectiveAction is FightEnemyAction)` 防战斗中刷屏，且覆盖"被打了但没打起来"——不搞 flag 消费机制（2026-08-11 简化）。
- **事件事实类记录保留在 handler**（非动作，无出队概念）：击晕（`_currentAction` 捕获战斗目标，event 无参）、认输/被认输、WitnessCrime 三分支目击。
- 旁白 = 会话级 NarrationLog（**不存档**），prompt【近期经历】段读最新 3 条（新→旧）；超 2× 容量触发 `MaintainNarrationAsync`（镜像对话历史总结纪律：解析失败作废保留）→ 总结进 DynamicMemories（持久化）。
- **战斗结果不收编**：`RecordFightResultIfPlayerInvolved` 写持久化 DynamicMemories，旁白会话级——收编会丢失持久化。

**关键签名**：
```csharp
brain.RecordNarration(string line);           // AgentBrain 内部 helper → _memory.RecordNarration
brain.RecordActionNarration(IAtomicAction);   // AgentBrain 出队翻译：通用调用器，分发到各动作自身 GetNarration
string GetNarration(Agent owner);             // IAtomicAction 接口成员：每个动作在自身定义处声明旁白（null = 不记）
mem.RecordNarration(string line);             // SingNpcMemorySystem：锁内 FIFO + 硬上限 3× + 超限触发异步总结
mem.SnapshotNarrationLog();                   // 线程安全快照（prompt 读取）
PromptBuilder.BuildPromptForNarrationSummary(memory, lines);  // 总结 prompt
```

**调用范例**（AgentBrain.cs，出队点 + 事件事实点；🔴 2026-08-11 单脑化后密谋子动作也走脑队列，执行器侧不再有补点）：
```csharp
// 出队点（Tick 内，OnStart 之后）——动作经历主记录点：
RecordActionNarration(_currentAction);   // 分发到动作自身 GetNarration：FightEnemyAction → "与X交战"；MoveToPositionAction(FleeFrom) → "吓得逃走了"
// 事件事实点（handler 内，无出队概念的事件）：
RecordNarration($"我遭到了{attacker.Name}的攻击"); // event_agent_damaged 受害者（门控：非交战中）
RecordNarration($"我被{knockedBy}打晕了");        // event_agent_knocked_out（ClearAllActions 前捕获战斗目标）
RecordNarration($"我向{Agent.Main.Name}认输了");  // event_npc_surrender
RecordNarration($"我看见{criminal.Name}在偷窃");  // WitnessCrime_GatherOnLook 分类三分支
// PlanExecutor.cs order_attack 步骤（绕过队列，创建即执行）：
// （已迁移：密谋旁白统一在子动作 OnStart 处走 RecordActionNarration 分发，不再创建处特例）
```

**文件**：`AI/Actions/AtomicAction.cs`（**接口 + 全部 19 个动作定义 + 各动作自声明旁白**）、`AI/AgentBrain.cs`（出队调用器 + 事件事实记录点）、`Memory/SingNpcMemorySystem.cs`（NarrationLog 本体）、`LLM/PromptBuilder.cs`（【近期经历】段 + 总结 prompt）、`Planner/PlanExecutor.cs`（密谋补点）。


---

## 叙事迁移 — QuestManager 硬编码字串清理

`QuestManager.GetQuestDescription()` 的 ~120 行日本战国硬编码字串已替换为通用简化描述。`GetQuestTitle()` 同步清理。叙事全部走 `NarrativeResolver` → CSV 管道。

---

# 🆕 NPC 警戒值系统 — 三级响应（2026-07-07）


---

## 🔴 记忆容量分档（热度驱动三件套上限）— 2026-08-21

**解决**：对话历史/短期/永久记忆的上限由互动热度分档（Hot/Normal/Cold），不设固定值。

**热度机制**（`ImChat/ImHeatTracker.cs`）：
- 加分：玩家私聊对方 +1 / 群聊被挑中回复 +1 / 跟随回复 +0.5（`ImChatManager`）
- 衰减：每游戏日 -1（`Settings.ImHeatDecayPerDay`，`MyBehavior.DailyTick`）
- 分档阈值（config.json 可调）：heat ≥ 10 → Hot；≥ 3 → Normal；< 3 → Cold

**三件套容量**（`SingNpcMemorySystem.ComputeCap` 同一分档驱动）：

| 容量 | Hot | Normal | Cold |
|---|---|---|---|
| `MaxRecentHistoryCount`（对话历史容器总量） | 40 | 20 | 8 |
| `MaxDynamicMemoryCount`（短期记忆条数） | 8 | 5 | 2 |
| `MaxPermanentLength`（永久记忆字符） | 500 | 300 | 100 |

**🔴 语义要点**：
- `RecentHistory` 是**单一容器**（私聊 im_user/im_npc + 频道 channel_* + 事件 system + 计划 plan_step + 当面 user/assistant 混装）——**私聊没有独立容量**，私聊显示条数 = 容器内 im_* 行份额（`GetDirectMessages` 同源过滤）。
- 总结触发 = 容器总条数 > 2× 容量（显示区间 [X, 2X+1]），总结后回落到 X 条；2026-08-21 容量翻倍（原 20/10/4）后 Cold 档每 8 条新消息总结一次（原每 4 条）。
- 全量历史 prompt 路径（`GetPrompt_History_Memory_Events`）按**最近 30 条截断**防爆（respond 6 句路径不受影响）。
- 前端显示 = 后端取出量（消息流全量渲染 + 滚动），无二次截断（唯一 Take 是左栏索引 `Take(6)` 与预览字符串）。

---

## 🔴 记忆调试面板 + SeqId 调试编号 — 2026-08-21

**解决**：玩家/开发者排查「记忆为什么丢/为什么重复/先后关系」——探查面板记忆 Tab 提供全量可读视图 + 日志快照。

**SeqId（`Memory/ChatMessage.cs` + `Memory/RecentMemory.cs`）**：
- 构造时 `Interlocked.Increment` 自增（会话内唯一）；存档随 JSON 保留（`NpcMemorySaveEntry` 拷贝时显式带过）；读档后 `EnsureSeqCounterAbove` 钳制计数器防进程重启撞号；旧档条目 = 0（显示 #0）。
- 用途：区分「同一编号 UI 显示多次」（UI bug）vs「不同编号内容重复」（重复写入）——排查重复的锚点。

**调试面板（`Interaction/NpcInfoVM.cs` BuildMemoryDebugText）**：
- 记忆 Tab 全量展开：长期记忆（字符/上限）/短期记忆（条数/上限）/对话历史（条数/上限）/人设三字段/大事记/委托记录/新闻/传闻，每行 `#编号 现实时间(MM-dd HH:mm:ss) · 游戏时间(D游戏日 时:分) 内容`。
- 只读快照（`SnapshotPermanentMemory/SnapshotImportantEvents/SnapshotQuestHistory` 走实例锁，与 `SnapshotRecentHistory` 同模式）；整个构建器 try/catch 降级兜底文案。

**诊断日志**（对时间戳定先后）：
- `[NPCInfo-Mem]`：探查面板打开时的记忆快照（History/Dynamic 条数 + 每行编号/时间/内容）
- `[ImChatStore]`：IM 打开时的各频道现有消息（store 视角，最近 3 条/私聊 2 条）——与 `[NPCInfo-Mem]` 对比判断「频道有而记忆无 = 写入问题；两者皆无 = 时序」

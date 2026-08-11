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

## 🔴 确定性事件写记忆：`RecordDynamicMemory`（同步入口）— 2026-08-11

**解决**：战斗结果等主线程确定性事件要让 NPC 知道（LLM 总结管道 `AddDynamicMemory` 是 private async，且依赖对话历史素材）。

```csharp
mem.RecordDynamicMemory("刚与努勒丹交手，我赢了。");   // 锁内 FIFO + 超限淘汰，不触发耗时重总结
```

**通道语义（关键）**：
- 动态记忆进 prompt 的【近期回忆】段（`GetPrompt_RespondContext` 最新 2 条，IM 私聊/当面对话都带）→ LLM 用自己口吻说出来，**不要**硬编码台词。
- 动态记忆**不渲染为私聊聊天行**（`GetDirectMessages` 只认 RecentHistory 的 `im_user`/`im_npc` 角色）→ "NPC 该知道但没说出口"的事实（胜负/目击）走这里，写 RecentHistory 会出现玩家没见过的幽灵消息。
- 内容 = 第一人称 LLM prompt 材料（豁免铁律 13），中性表述交给 LLM 调口吻。
- 调用范例：`FightEnemyAction.OnEnd` 的战斗结果记录（见 agent.md「战斗结果 → 当事人记忆 + 队伍广播」）。


---

## 🔴 经历旁白记录（Experience Narration）— 2026-08-11

**解决**：玩家攻击 NPC 后，NPC 的 LLM 对话（IM 群聊/私聊、当面 respond）对被攻击一事一无所知——罪案档案（WorldEventStore）只被原版剧本对话读取，`GetPrompt_RespondContext` 没有任何"经历"通道。

**要点**：
- **主记录点 = 动作出队执行处**（AgentBrain.Tick 出队后 `RecordActionNarration`）——**只在动作真正开始执行时记录，无幽灵**：队列里被 ClearAllActions 丢弃的动作永远不会出队；且与实际行为一致（小孩的 FleeFromAction 记为逃跑，不会错记成"上前相助"——事件决策点方案的教训，2026-08-11 实改）。
- **旁白定义在动作自身**：`IAtomicAction.GetNarration(Agent owner)`（AtomicAction.cs 各动作定义处）——值得记住的经历返回第一人称文本（`FightEnemyAction`："与X交战"；`FleeFromAction`/`ReactiveFleeAction`："吓得逃走了"），机械/台词/持续状态动作返回 null（零噪声）。**🔴 所有 IAtomicAction 实现统一集中在 `AI/Actions/AtomicAction.cs`**（含反应链 Reactive* 与 ExecutePlanAction，2026-08-11 迁移）——新增动作只改该文件自身定义处，AgentBrain 零改动。
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

**调用范例**（AgentBrain.cs，出队点 + 事件事实点 + PlanExecutor 1 处密谋补点）：
```csharp
// 出队点（Tick 内，OnStart 之后）——动作经历主记录点：
RecordActionNarration(_currentAction);   // 分发到动作自身 GetNarration：FightEnemyAction → "与X交战"；FleeFromAction → "吓得逃走了"
// 密谋路径（PlanExecutor 子动作 OnStart 后，绕过脑队列——必须同源接入否则密谋动作无旁白）：
AgentAIController.GetBrainForAgent(cursor.Agent)?.RecordActionNarration(cursor.SubAction);
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

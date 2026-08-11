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

## 叙事迁移 — QuestManager 硬编码字串清理

`QuestManager.GetQuestDescription()` 的 ~120 行日本战国硬编码字串已替换为通用简化描述。`GetQuestTitle()` 同步清理。叙事全部走 `NarrativeResolver` → CSV 管道。

---

# 🆕 NPC 警戒值系统 — 三级响应（2026-07-07）

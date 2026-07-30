# Plan: 清理 RealScene 对峙中的 WalkAway 选项

## 设计原则（已录入 CLAUDE.md 铁律 12）

**每个选项必须有代价或检定——禁止零成本最优解。**

对话中的每一个出口，要么考验玩家能力（技能检定），要么付出资源（赔钱/坐牢），要么承担后果（拔剑开打/关系恶化/追击部队）。绝不允许出现"既不用检定、又不付代价、还能安全脱身"的选项。这种选项一旦存在，其他所有选项都失去意义——玩家永远会选它。

RealScene 对峙中"我走了"就是典型的零成本脱身。大地图 WalkAway = 关系惩罚 + 追击 party → 有代价，合法。

---

## 前置：已完成的 continue_chat_safe 修复

**Bug**：玩家赔钱后从 `continue_chat_safe`（"还有什么想说的？"）走人 → `WalkAwayIntent.OnInstant` 的 `evt == null` 分支误判为"Alert 逃跑" → 设 `PendingEscalationAgent` → 对话关闭后同一个 NPC 立刻发起第二次质问。

**修复**：`continue_chat_safe` 的 WalkAway 加 `ActionParam = "safe"`，`OnInstant` 开头检查 `ActionParam == "safe"` 则直接 return。✅ 已改。

---

## 修改清单

### 文件 1: `CrimeDialogueBuilder.cs` — 删除/替换 WalkAway 选项

**背景**：12 处 WalkAway 用法中，8 处在地图对话（`DialogueTrigger.Normal`，`InRealScene==false`）——合法保留。4 处在 Alert 对峙（`DialogueTrigger.Alert`，`InRealScene==true`）——其中 1 处是已修复的 `continue_chat_safe`，剩下 3 处需要改。

**修改 1** — `BuildSearchSubtree` injectedStart (line 1081)
- 删除 transition：`new() { PlayerLine = "转身就走", Action = "INTENT:WalkAway", NextNodeOnSuccess = "alert_search_walk_ack" }`
- 删除 ack node (line 1087)：`Node("alert_search_walk_ack", r.Resolve("站住！"))` —— 无其他入口
- 玩家在 Search 对峙中只剩下：提交搜查 / 拒绝搜查 / 贿赂

**修改 2** — `BuildSearchSubtree` recover_confront (line 1100)
- `"推开就跑"` 从 `INTENT:WalkAway` → `INTENT:FightVillagers`
- `NextNodeOnSuccess` = `"alert_esc_fight_ack"`（已有，"你疯了！快叫人！"）
- 武力挣脱 = 打架，语义自洽

**修改 3** — `BuildRecoverSubtree` injectedStart (line 1133)
- 同上：`"推开就跑"` 从 `INTENT:WalkAway` → `INTENT:FightVillagers`
- `NextNodeOnSuccess` = `"alert_esc_fight_ack"`

### 文件 2: `AccountabilityIntents.cs` — 清理 WalkAwayIntent 死代码

改动后 `PendingEscalationAgent` 的全部四个 `SetPendingEscalation` 调用点失去入口：

| 调用点 | 行号 | 触发条件 | 死因 |
|--------|------|----------|------|
| `evt == null + InRealScene` | 698 | 无事件 + 场景内 | Alert 中非 safe WalkAway 全删；地图 InRealScene=false |
| `Emerging + InRealScene` | 735 | 怀疑阶段 + 场景内 | Emerging WalkAway 只在地图对话 |
| `OnFleeSuccess` | 809 | Active + InRealScene + 检定成功 | Active WalkAway 只在地图对话 |
| `OnFleeFail` | 821 | Active + InRealScene + 检定失败 | 同上 |

**删除**：
- 静态字段（line 651-659）：`PendingEscalationAgent`, `PendingEscalationDetail`, `PendingEscalationAction`, `PendingEscalationGatherOnly`
- 方法（line 662-668）：`SetPendingEscalation()`
- 方法（line 794-810）：`OnFleeSuccess()`
- 方法（line 815-822）：`OnFleeFail()`
- `OnInstant` 中 `Active + InRealScene` 的 Intimidate 检定 + flee 分支 (line 749-774)

**保留**：
- `PendingInquiryTitle` / `PendingInquiryBody` — 大地图 WalkAway 后的弹出警告，仍有入口
- `OnInstant` 其余分支 — 按事件阶段判定后果（地图对话路径）

### 文件 3: `ConversationEntryPatch.cs` — 清理消费端

**删除** — `ResetCrimeDialogueOnConversationEndPatch.Postfix` 中 (line 396-433)：
- `PendingEscalationAgent` 消费块：`WitnessCrime` 广播 + `ReEngageConfrontation` 发送
- `PendingEscalationDetail/Action/GatherOnly` 读取

**保留**（紧邻但独立）：
- `PendingInquiryTitle/Body` 消费块 (line 292-323) — 仍然有入口
- `ThreatIntent.PendingCombatAgent` 消费 → `DeferredCombat` (line 390-393) — FightVillagers 用
- `SurrenderJailIntent.PendingJailExit` 消费 (line 437+) — 坐牢用

### 文件 4: `AgentBrain.cs` — 清理 ReEngageConfrontation handler（新发现）

`ReEngageConfrontation` 事件**唯一的发送者**就是 `ConversationEntryPatch` 的 `PendingEscalationAgent` 消费块。发送端删除后，`AgentBrain.ReceiveEvent` 中的 handler (line 259-309) 也变成死代码。

**删除** (line 259-309)：
- `if (aiEvent.EventType == "ReEngageConfrontation")` 整个分支
- `ConfrontingBrain` 锁获取
- `detailOverride` / `actionOverride` 推导
- `SetNpcIntent(Confronting, ...)`
- `PendingWorldEvent` 推进到 Active
- `FollowAgentAction` → `LookAtAction` → `AlertForceConversationAction` → `StayAction` 入队

### 文件 5: `plans/rules/wheels.md` — 更新已造轮子

删除 `PendingEscalationAgent` 条目 (line 574)：
```
| `WalkAwayIntent.PendingEscalationAgent` | `WalkAwayIntent.OnInstant` | `BroadcastEvent("WitnessCrime")` + `SendEventToAgent("ReEngageConfrontation")` |
```

---

## 验证

1. 酒馆击晕 NPC → Alert 拦截（Deter/Stop）→ 确认无"我走了"/"转身就走"
2. Search 对峙 → 只有提交搜查/拒绝/贿赂三个选项
3. Recover 对峙 → "推开就跑" 触发 FightVillagers 战斗
4. 赔钱后 continue_chat_safe → "我走了" → 干净退出，不触发二次质问
5. 大地图权威 NPC 对话 → WalkAway 选项仍在，走后弹出警告 + 追击 party
6. 编译通过，无 `PendingEscalationAgent` / `ReEngageConfrontation` 残留引用

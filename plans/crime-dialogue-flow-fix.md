# 对话流体验 & Quest 叙事日志 — 修复计划

> **关联测试**：[crime-test-plan.md](crime-test-plan.md)
> **测试日志**：`Debug/StoryEngine_RuntimeLog.txt`（2026-07-03 二轮测试）
> **状态**：待审批

---

## 问题诊断

### P0 — 对话强关（玩家被踢出对话）

**根因**：`CrimeDialogueBuilder.BuildClosingTurn` 创建的 `confess_close` / `confront_close` 是死胡同 turn——唯一选项 `"……"` → `close_window`，玩家无法继续和 NPC 交流。

受影响路径（全部阶段 × 全部 Intent）：

| 阶段 | 玩家选择 | 当前路由 | 玩家期望 |
|------|---------|---------|---------|
| Emerging 自首 | CharmDefense（成功/失败） | → `confess_close` → `close_window` ❌ | NPC 回应后继续聊 |
| Emerging 自首 | ConfessWalkAway | → `confess_close` → `close_window` ❌ | 走人（见 P1） |
| Emerging 自首 | PayRestitution | → `close_window` ❌ | 赔完钱继续聊 |
| Active 对峙 | CharmDefense（成功/失败） | → `confront_close` → `close_window` ❌ | NPC 回应后继续聊 |
| Active 对峙 | Threat（成功/失败） | → `confront_close` → `close_window` ❌ | NPC 回应后继续聊 |
| Active 对峙 | PayRestitution | → `close_window` ❌ | 赔完钱继续聊 |
| Active 对峙 | "转身就走" | → `confront_close` → `close_window` ❌ | 走人（见 P1） |
| Confrontation | Settle（失败） | → `close_window` ❌ | NPC 回应后继续聊 |
| Confrontation | PayRestitution | → `close_window` ❌ | 赔完钱继续聊 |
| Confrontation | "我走了" | → `close_window` ❌ | 走人（见 P1） |

**例外（正确的）**：Confrontation 阶段 `"太贵了，不赔"` → `declineTurn = "start"`，回到对话起点，玩家可以继续选择。这证明了"回到 start"是可行的模式。

### P1 — "转身就走"不是通用 Intent

**现状**：
- `ConfessWalkAwayIntent` 只处理"自首后走人"（Emerging/Active + SuspectIsPlayer），设了个 pending Inquiry 等 EndConversation 弹
- 其他阶段的"转身就走"/"我走了"是裸 `Action = "NONE"` + `NextTurn = "close_window"`，**没有任何后果**
- `ConfessIntent.OnInstant` 会在**每次自首时**都预设 `ConfessWalkAwayIntent.PendingInquiryTitle`，导致即使玩家最后赔了钱，EndConversation 时仍然弹"站住！" Inquiry（本次日志中观察到了这个 bug）

**用户期望**：
- "转身就走"是一个**通用 Intent**，不限于自首场景
- 是否放玩家走，取决于玩家是否已是嫌疑对象（`SuspectIsPlayer`）
- 如果玩家是嫌犯 → NPC 不甘心放人 → 有后果（Inquiry 警告、声望惩罚、stage 推进等）
- 如果玩家不是嫌犯 → NPC 无所谓 → 自然结束对话

### P2 — EndConversation 无日志

`ResetCrimeDialogueOnConversationEndPatch` 没有 `DebugLogger.Log`，无法从日志判断对话何时结束、新对话何时开始。

### P3 — Quest 日志无叙事过程

Quest Journal 只记录了"开始"和"结束"两条模板日志。中间玩家的每一个选择、每一次 Intent 成功/失败、阶段变化，全部丢失。玩家回头看 Quest 日志，完全读不出自己的游玩故事。

---

## 修复方案

### 1. P0：对话不强制关闭

#### 1.1 新增 `continue_chat` turn

在 `CrimeDialogueBuilder` 中新增一个通用"继续聊"turn：

```
Turn "continue_chat":
  NPC line: ""（空 — NPC 刚才已经说了回应，不需要再说话）
  Options:
    - "说点别的……" → NextTurn = ""（空 → DialogueInjector 将其解释为回到 vanilla hero_main_options）
    - "我得走了。" → Action = "INTENT:WalkAway"
```

**DialogueInjector 改动**：当 option 的 `NextTurn` 为空/空字符串时，不路由到 `close_window`，而是路由到 vanilla `hero_main_options` token（让玩家回到原版对话主菜单，可以说"我得走了"、交易、招募等原版选项）。

> **为什么路由到 `hero_main_options` 而不是留在犯罪对话里？**
> 因为 Intent 执行后 WorldEvent stage 可能已经变了（Emerging→Confrontation / Active→Resolved），继续留在同一批注入的 turn 里会展示过时文本。回到 `hero_main_options` 后：
> 1. 玩家可以继续跟 NPC 聊原版内容（交易、招募、闲聊…）
> 2. 如果玩家想再谈犯罪事件，选入口句 → `ConvEntry` Postfix 会用**最新 stage**重新注入对话
> 3. 玩家不想聊了 → 选原版"我得走了" → 走 WalkAwayIntent

#### 1.2 修改 `BuildClosingTurn` 的使用

- **删除** `BuildClosingTurn` 对 `confess_close` / `confront_close` 的调用
- **保留** `BuildClosingTurn` 方法本身（未来可能有用），但不再在自首/对峙流中调用
- 所有原本路由到 `confess_close` / `confront_close` 的选项，改为路由到 `continue_chat`

#### 1.3 统一 `restitution_detail` 的 `declineTurn`

| 阶段 | 当前 declineTurn | 改为 |
|------|-----------------|------|
| Emerging（自首） | `"confess_close"` | `"continue_chat"` |
| Active（对峙） | `"confront_close"` | `"continue_chat"` |
| Confrontation（报复） | `"start"` | `"continue_chat"` |

`"太贵了，不赔"` 统一回到 `continue_chat`，所有阶段体验一致。

#### 1.4 PayRestitution 的 NextTurn

赔钱后 NPC 说"好，钱留下，这事就算了。" → 然后到 `continue_chat`（而不是 `close_window`）。

---

### 2. P1：通用 WalkAwayIntent

#### 2.1 设计

```
Intent: WalkAway
  类型: 即时类（不检定）
  资格: 始终可见（任何对话、任何 NPC）

  OnInstant(ctx):
    1. 查找当前 settlement 是否有活跃 WorldEvent
    2. 如果没有活跃犯罪事件 → 直接结束对话（NPC 无所谓）
    3. 如果有，且 SuspectIsPlayer:
       a. Emerging/Active stage:
          - 预设 Inquiry（"站住！"），在 EndConversation 时弹出
          - 如果 stage == Emerging → TransitionStage(Active)
          - 如果 stage == Active → 维持，信任度 -10
          - 通知调查 Quest 嫌犯已锁（如果玩家还没自首过）
       b. Confrontation stage:
          - 直接弹 Inquiry 更严厉的警告
          - 触发报复 spawn（如果还没触发）
          - 信任度 -20
    4. 如果有活跃事件但 SuspectIsPlayer == false:
       - 无后果，直接结束对话（NPC 没理由拦你）
```

#### 2.2 替换现有"转身就走"

- `BuildConfessTurn` 中 `ConfessWalkAway` → 改为 `WalkAway`
- `BuildConfrontPlayerTurn` 中 "（转身就走）" → 改为 `WalkAway`
- `BuildRetaliationTurn` 中 "我走了。" → 改为 `WalkAway`
- `BuildDiscoveryTurn` / `BuildReportTurn` 中 "我还有事。" → 改为 `WalkAway`

#### 2.3 删除 ConfessWalkAwayIntent

合并入通用 `WalkAwayIntent`，删除旧的 `ConfessWalkAwayIntent` 类。

#### 2.4 修复 ConfessIntent 的 bug

`ConfessIntent.OnInstant` 不再预设 `PendingInquiryTitle`。Inquiry 只在玩家**实际选择** WalkAway 时才由 `WalkAwayIntent.OnInstant` 设置。

---

### 3. P2：EndConversation 日志

在 `ResetCrimeDialogueOnConversationEndPatch.Postfix` 开头加一行：

```csharp
DebugLogger.Log($"[ConvEnd] Conversation ended. lastEvent={_lastInjectedEventId} lastTag={_lastInjectedTag}");
```

---

### 4. P3：Quest 叙事日志

#### 4.1 方针

给 Quest Journal 加日志 = 给玩家写"冒险日记"。每条日志应当是**玩家视角的叙事**，不是技术状态的 dump。

**原则**：
- 日志文本用**自然语言**，像一个冒险者在日记里记录今天发生了什么
- 用 `AddLog(new TextObject(...))` 写入关联的 `CommissionQuest`
- 查找关联 Quest 的方法：遍历 `Campaign.Current.QuestManager.Quests`，找 `CommissionQuest` 且 `Data.WorldEventId == evt.EventId`

#### 4.2 具体加日志的节点

| 触发点 | 日志内容示例 | 在哪个文件加 |
|--------|------------|------------|
| 接调查任务 | ✅ 已有（`OnStartQuest`） | — |
| 玩家自首 | "我向{村长}坦白了——那只{物品}确实是我拿的。" | `ConfessIntent.OnInstant` |
| CharmDefense 成功 | "我设法说服了{村长}，暂时洗脱了嫌疑。但他看我的眼神还是不太对……" | `CharmDefenseIntent.OnSuccess` |
| CharmDefense 失败 | "辩解没用。{村长}根本不买账，事态反而更严重了。" | `CharmDefenseIntent.OnFail` |
| Threat 成功 | "我放了狠话。{村长}退缩了，不敢再追究。但我在这地方的名声怕是完了。" | `ThreatIntent.OnSuccess` |
| Threat 失败 | "威胁没吓住{村长}——他叫人了。事情彻底闹大了。" | `ThreatIntent.OnFail` |
| 赔钱 | "赔了{金额}第纳尔。{村长}收了钱，这事总算翻篇了。" | `PayRestitutionIntent.OnInstant` |
| 栽赃成功 | "我成功把嫌疑推给了{目标}。{村长}信了。" | `FrameSuspectIntent.OnSuccess` |
| 栽赃失败 | "栽赃被识破了。{村长}开始怀疑我……" | `FrameSuspectIntent.OnFail` |
| Stage → Confrontation | "事态升级了——{村长}已经雇了人，不会再跟我客气。" | `OnWorldEventStageChanged` |
| Stage → Resolved | "偷牲口的事终于尘埃落定。" | `OnWorldEventStageChanged` |
| 转身就走（嫌犯） | "我转身走了。身后传来{村长}的怒吼——这事没完。" | `WalkAwayIntent.OnInstant` |

#### 4.3 CommissionQuest 需要新增辅助方法

```csharp
/// <summary>给关联当前 WorldEvent 的 CommissionQuest 加一条叙事日志</summary>
public static void AddNarrativeLog(WorldEvent evt, string message)
{
    foreach (var q in Campaign.Current.QuestManager.Quests)
    {
        if (q is CommissionQuest cq
            && cq.Data?.WorldEventId == evt.EventId
            && cq.Data?.Category == CommissionCategory.Investigation)
        {
            cq.AddLog(new TextObject(message));
            return;
        }
    }
}
```

放在 `CommissionQuest` 或 `WorldEventStore` 中作为静态 helper。

---

## 涉及文件

| 文件 | 改动类型 | 内容 |
|------|---------|------|
| `Interaction/Dialogue/CrimeDialogueBuilder.cs` | 🔴 重构 | 新增 `continue_chat` turn；删除 `confess_close`/`confront_close` closing turn 调用；所有 resolution option → `continue_chat`；统一 declineTurn；"转身就走" → WalkAway |
| `Interaction/Dialogue/DialogueInjector.cs` | 🟡 改动 | 空 NextTurn 路由到 `hero_main_options` 而非 `close_window` |
| `Interaction/Dialogue/ConversationEntryPatch.cs` | 🟡 改动 | EndConversation 加日志；`ConfessWalkAwayIntent.PendingInquiry*` 改为 `WalkAwayIntent.PendingInquiry*` |
| `Interaction/Intents/AccountabilityIntents.cs` | 🔴 重构 | 新增 `WalkAwayIntent`；删除 `ConfessWalkAwayIntent`；`ConfessIntent.OnInstant` 不再预设 PendingInquiry；所有 Intent 的 OnSuccess/OnFail/OnInstant 补叙事日志 |
| `Quests/Commissions/CommissionQuest.cs` | 🟡 新增 | 静态 helper `AddNarrativeLog(WorldEvent, string)`；`OnWorldEventStageChanged` 补阶段变化叙事日志 |

---

## 设计哲学四原则 对照检查

| 原则 | 本次修复 |
|------|---------|
| ① 反馈明确 | ✅ Quest 叙事日志让玩家看到自己每个选择的结果；WalkAway 意图让玩家清楚知道"走不走得掉" |
| ② 自由感 | ✅ **核心修复**——不再强关对话，玩家可以继续和 NPC 聊；赔钱/辩护/威胁后可以留在对话里 |
| ③ 任意 NPC 接得住 | ✅ WalkAway 是通用 Intent，任何 NPC 对话都可以用 |
| ④ 信息塑造目标 | ✅ 叙事日志从玩家视角记录冒险，塑造"我的故事" |

---

## 测试验证点

1. **对话不强制关闭**：自首→CharmDefense（成功/失败）后，回到 `continue_chat`，可选"说点别的"回到原版对话菜单
2. **WalkAway 嫌犯场景**：玩家是嫌犯时选"转身就走"→ EndConversation 弹出 Inquiry 警告 → stage 推进
3. **WalkAway 非嫌犯场景**：玩家不是嫌犯时选"我走了"→ 自然结束对话，无 Inquiry
4. **Quest 叙事日志**：打开 Quest Journal，能看到从接任务→自首→狡辩失败→对峙→赔钱的全过程日志
5. **EndConversation 日志**：每次对话结束都有一条 `[ConvEnd]` 日志
6. **"太贵了，不赔"统一行为**：所有阶段选"太贵了"都回到 `continue_chat`（不会有的回 start 有的强关）

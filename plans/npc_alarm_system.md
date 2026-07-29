# NPC 警戒值系统 — 从 UI 到行为的三级响应

> **状态**：UI 已完成（AgentHUD 警戒眼睛）。本计划定义 UI 之下的行为层——警戒值如何影响 NPC 的行为决策。

---

## 一、设计概览：三级响应模型

```
AlarmFactor:  0.0 ────── 0.25 ──────── 1.0 ──────── 2.0+
阶段:         正常        怀疑            警戒            质问
UI颜色:        白底        白→黄           黄→橙           纯红

NPC反应:      无          BubbleSay      转身看你        主动上前对话
                         不改行为        轻度改变        完全打断原行为
```

| 级别 | 警戒值 | NPC 做什么 | NPC 说什么（示例） |
|------|--------|-----------|------------------|
| **L1** | 0.25→1.0 | `AgentSay()` 一句 BubbleSay，**不打断当前行为** | "（嘀咕）这人在干嘛……" |
| **L2** | 1.0→2.0 | 插入 `LookAtAction` 转身盯着玩家，可能再 BubbleSay 一句 | "（提高声音）喂！你在干什么！" |
| **L3** | ≥2.0 | `ClearAllActions()` → 走到玩家面前 → 开启对话 | "站住！你是不是在偷东西？" |

---

## 二、核心数据结构：AgentBrain 维护自己的分类警戒值

### 2.1 设计原则

| 层 | 谁负责 | 做什么 |
|----|--------|--------|
| **感知** | `NpcSightSystem`（纯工具，无状态） | "NPC 能不能看到玩家？"（FOV + RayCast） |
| **认知** | `AgentBrain`（每个 NPC 自己的大脑） | "我看到了什么？我该多警觉？说什么？" |

`_alertBreakdown` 从 `NpcSightSystem` 的静态字典**移到每个 `AgentBrain` 实例**。每个 NPC 独立维护自己对玩家的警戒值明细——我看到什么、我多怀疑、我该说什么，都是"我"的事。旧 `NpcSightSystem._alertValues` 字典和 `UpdateAlertValue` 方法**直接删除**，`AgentHudMissionView` 改从 `AgentAIController.GetBrainForAgent(agent).AlertValue` 读值。不做桥接，git 回滚兜底。

### 2.2 数据结构

```csharp
public enum PlayerActionType
{
    Crouching,      // 蹲下
    WeaponDrawn,    // 武器出鞘（和平区域）
    StealUIOpen,    // 偷窃界面打开未确认
    Steal,          // 偷窃（脉冲）
    AttackAlly,     // 攻击友方（脉冲）
    Knockout,       // 击晕（脉冲）
}

/// <summary>单条警戒条目：值 + 脉冲附加信息（供台词拼接）</summary>
struct AlertEntry
{
    public float Value;
    public string TargetName;  // 脉冲事件附加：受害者名（持续累加时为空）
    public string ItemName;    // 脉冲事件附加：被盗物品名（持续累加时为空）
}

// ── AgentBrain 新增字段 ──

/// <summary>我对玩家的分类警戒值明细。一个字典替代原来的 _alertBreakdown + _pulseContext。</summary>
private Dictionary<PlayerActionType, AlertEntry> _alertBreakdown = new Dictionary<PlayerActionType, AlertEntry>();

/// <summary>上一帧的警戒阶段（用于检测穿越，包括向下穿越）</summary>
private AlarmPhase _lastAlertPhase = AlarmPhase.Normal;

/// <summary>脉冲抑制截止时间（Mission.Current.CurrentTime），0=无抑制</summary>
private float _pulseSuppressedUntil;

/// <summary>已触发过 BubbleSay 的 (Action, Phase) 组合。降级后清零对应条目，允许重新触发。</summary>
private HashSet<(PlayerActionType, AlarmPhase)> _bubbledPhases = new HashSet<(PlayerActionType, AlarmPhase)>();

// ── 公开查询（AgentHudMissionView 每帧读 AlertValue / AlertPhase）──

public float AlertValue => _alertBreakdown.Values.Sum(e => e.Value);
public AlarmPhase AlertPhase => AlertValue switch
{
    >= 2.0f => AlarmPhase.Alarmed,
    >= 1.0f => AlarmPhase.Cautious,
    >= 0.25f => AlarmPhase.Suspicious,
    _ => AlarmPhase.Normal
};
/// <summary>当前最高警戒值对应的行为类型。BubbleSayOnce 内部用（阶段转换时调用，非每帧）。</summary>
public PlayerActionType? PrimaryAction => _alertBreakdown.Count == 0 ? null
    : _alertBreakdown.OrderByDescending(kv => kv.Value.Value).First().Key;
```

### 2.3 持续累加 — AgentBrain.Tick 节流计算

警戒值不需要每帧计算——NPC 不会在 1/60 秒内改变对玩家的看法。**用可配置的节流间隔替代逐帧 Tick**，累积 dt，间隔到了才跑一次认知更新。

```csharp
// ── AgentBrain 新增字段 ──

/// <summary>警戒认知更新间隔（秒）。默认 0.1s = 100ms。设 0 退化为逐帧。</summary>
private float _alertCognitionInterval = 0.1f;

/// <summary>认知更新计时器（累积 dt），达到 _alertCognitionInterval 时触发一次 UpdateAlertCognition 然后归零。</summary>
private float _alertCognitionTimer;

// 在 AgentBrain.Tick 中：
void Tick(float dt)
{
    if (Owner == Agent.Main) return;
    // ... 现有逻辑 ...

    // ── 🆕 节流认知更新 ──
    _alertCognitionTimer += dt;
    if (_alertCognitionTimer >= _alertCognitionInterval)
    {
        UpdateAlertCognition(_alertCognitionTimer);  // 传入累积 dt，不是原始帧 dt
        _alertCognitionTimer = 0f;
    }
}

void UpdateAlertCognition(float dt)
{
    if (!NpcSightSystem.CanNpcSeePlayer(Owner))
    {
        DecayAlertBreakdown(dt);
        // 看不到玩家时仍需检测向下穿越（衰减可能导致降级）
        CheckPhaseTransition();
        return;
    }

    // 我能看到玩家——他在干什么？
    if (Agent.Main.CrouchMode)
        AddAlert(PlayerActionType.Crouching, dt * 0.15f);

    if (IsPlayerWeaponDrawn())
        AddAlert(PlayerActionType.WeaponDrawn, dt * 0.20f);

    if (StealManager.IsUIOpen)
        AddAlert(PlayerActionType.StealUIOpen, dt * 0.30f);

    // 阶段穿越检测（向上或向下）
    CheckPhaseTransition();
}
```

**节流影响**：
- `_alertCognitionInterval = 0.1f`（默认）：100ms 一次，10Hz。`dt` 传入 ~0.1，累加速度与逐帧一致。
- `_alertCognitionInterval = 0.2f`：200ms 一次，5Hz。适合低端机或 NPC 密集场景。
- `_alertCognitionInterval = 0f`：退化为逐帧（调试用）。

> **为什么传入累积 dt 而不是帧 dt？** 如果用帧 dt × 累加速度，200ms 一次就只加了 16ms 的量，感知更新会变慢 12 倍。传入累积 dt 保证 **每秒累加总量不变**，只是更新频率降低。

// ── AddAlert：无缓存，纯累加 ──

void AddAlert(PlayerActionType type, float amount)
{
    if (!_alertBreakdown.TryGetValue(type, out var entry))
        entry = new AlertEntry();

    entry.Value += amount;
    _alertBreakdown[type] = entry;  // struct 是值类型，写回
}

float GetAlertValue(PlayerActionType type)
{
    return _alertBreakdown.TryGetValue(type, out var entry) ? entry.Value : 0f;
}

// ── 阶段穿越：发独立事件走 ReceiveEvent（详见第三节）──

void CheckPhaseTransition()
{
    var newPhase = AlertPhase;
    if (newPhase == _lastAlertPhase) return;

    if (newPhase > _lastAlertPhase)
    {
        string eventType = newPhase switch
        {
            AlarmPhase.Suspicious => "BecomeSuspicious",
            AlarmPhase.Cautious   => "BecomeCautious",
            AlarmPhase.Alarmed    => "BecomeAlarmed",
            _ => null
        };
        if (eventType != null)
            ReceiveEvent(new AIEvent { EventType = eventType, Sender = this });
    }
    else
    {
        ReceiveEvent(new AIEvent
        {
            EventType = "CalmDown",
            Sender = this,
            Args = new object[] { _lastAlertPhase, newPhase }
        });
    }

    _lastAlertPhase = newPhase;
}
```

### 2.3.1 武器出鞘检测 — 走 VersionCompat

```csharp
bool IsPlayerWeaponDrawn()
{
    var main = Agent.Main;
    if (main == null) return false;
    // 走 VersionCompat 封装，不在业务代码里裸写 #if
    return V.MainWpn(main) != EquipmentIndex.None
        || V.OffWpn(main) != EquipmentIndex.None;
}
```

### 2.4 脉冲 — ReceiveEvent 内与行为合并处理

脉冲不新建事件类型、不新建 IntentBase 子类。**复用现有的 `"WitnessCrime_GatherOnLook"` 事件**——`StealVM` 被抓现行时已经通过 `BroadcastEventInRange` 广播给所有目击者。

**设计原则**：一个事件 → 一处代码 → 按角色分流。脉冲（+警戒值）和行为（指控/围观）在 `ReceiveEvent` 的同一个 `if` 块内完成，不拆分到 IntentRegistry。

```csharp
// ReceiveEvent 中：
if (aiEvent.EventType == "WitnessCrime_GatherOnLook")
{
    try
    {
        Agent criminal = (Agent)aiEvent.Args[0];
        Agent victim = (Agent)aiEvent.Args[1];
        Vec3 assignedPos = (Vec3)aiEvent.Args[2];
        Vec2 turnDir = (Vec2)aiEvent.Args[3];
        float delay = GroupStageManager.CalculateReactionDelay(Owner, criminal, victim);

        // ── 🆕 警戒脉冲：所有目击者统一加值（criminal==玩家时）──
        if (criminal == Agent.Main)
        {
            AddAlert(PlayerActionType.Steal, 2.0f);
            SetPulseTarget(PlayerActionType.Steal, victim.Name, null);
            _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
        }

        ClearAllActions();
        InteractedAgent = criminal;

        // ── 角色分流 ──
        if (Owner == victim && criminal == Agent.Main)
        {
            // 受害者：直接指控
            var conflictData = new PendingConflict(
                eventId: $"Theft_{CampaignTime.Now.ToHours}",
                topicName: "当众行窃",
                goalDesc: $"要求 {criminal.Name} 立刻归还财物并赔偿精神损失",
                severity: 70.0f,
                type: NegotiationGoalType.ResolveConflict_Apology
            );
            EnqueueAction(new PrepareOpeningAction(InitiativeType.CrimeAccusation, conflictData));
        }

        EnqueueAction(new ReactionDecisionAction(delay, (agent) =>
        {
            EnqueueAction(new LookAtAction(criminal, 0.5f));
            EnqueueAction(new MoveToPositionAction(assignedPos, turnDir));
            if (Owner == victim)
                EnqueueAction(new ForceTalkAction());
            EnqueueAction(new StayAction(criminal));
        }));
    }
    catch (Exception)
    {
        // silent fail
    }
}
```

**一条事件，一处代码，按角色分流**：

| 目击者 | 警戒脉冲 | 行为 |
|--------|---------|------|
| 受害者 | +2.0 + TargetName | PrepareOpeningAction → ReactionDecisionAction(移动到位置 → LookAt → ForceTalkAction → StayAction) |
| 普通平民 | +2.0 + TargetName | ReactionDecisionAction(移动到位置 → LookAt → StayAction) |

> **守卫拦截**：当前未实现。守卫角色待后续直接在同一个 `if` 块内加 `else if` 分支。

**脉冲抑制**：脉冲加值同时设 `_pulseSuppressedUntil = now + 3.0f`。偷窃瞬间到 2.0 → 受害者已在处理主质问 → 3 秒内不触发其他目击者的独立 L3 上前。围观流程（`GroupStageManager` 编排）不受影响。

**与现有流程的关系**：`StealVM` → `BroadcastEventInRange("WitnessCrime", Agent.Main, victim)` 这条链路**不动**。`AgentAIController.BroadcastEventInRange` 内部的 `GroupStageManager.PrecalculateAllocations` + 事件分发**不动**。`IntentRegistry.GetNpcInitiatives` 分发已从 `ReceiveEvent` 移除——NPC 事件响应不再走 Intent 抽象层，直接在 `ReceiveEvent` 的 flat if/else 中处理。

### 2.4.1 AgentBrain 脉冲辅助方法

```csharp
// AgentBrain 新增：

public void AddAlert(PlayerActionType type, float amount) { /* 见 2.3 */ }

/// <summary>脉冲上下文：设置 AlertEntry 的 TargetName（不改变 Value，Value 由 AddAlert 加）</summary>
void SetPulseTarget(PlayerActionType type, string targetName, string itemName)
{
    if (!_alertBreakdown.TryGetValue(type, out var entry))
        entry = new AlertEntry();
    entry.TargetName = targetName;
    entry.ItemName = itemName;
    _alertBreakdown[type] = entry;
}
```

> **IntentBase 上已无 `TriggerEvents` / `CanHandle`**。NPC 事件响应直接在 `ReceiveEvent` 的 flat if/else 中处理，不走 Intent 抽象层。脉冲逻辑是 `WitnessCrime_GatherOnLook` 块内的 ~4 行代码。

### 2.5 衰减 — 看不到玩家时按比例降

```csharp
void DecayAlertBreakdown(float dt)
{
    if (_alertBreakdown.Count == 0) return;

    float alertTotal = AlertValue;  // Sum() 按需计算
    float totalDecay = dt * 0.15f;
    if (alertTotal <= 0.0001f) { _alertBreakdown.Clear(); return; }

    var keys = _alertBreakdown.Keys.ToList();
    foreach (var key in keys)
    {
        var entry = _alertBreakdown[key];
        float proportion = entry.Value / alertTotal;
        entry.Value -= totalDecay * proportion;
        if (entry.Value <= 0.0001f)
        {
            _alertBreakdown.Remove(key);  // 移除条目时 TargetName/ItemName 自动清理
        }
        else
        {
            _alertBreakdown[key] = entry;  // struct 值类型，写回
        }
    }
}
```

### 2.6 复合行为示例

| 玩家在做什么 | Crouching | StealUIOpen | Steal | Sum | PrimaryAction |
|-------------|-----------|-------------|-------|-----|---------------|
| 蹲下 3 秒 | 0.45 | 0 | 0 | 0.45 | Crouching |
| 蹲下 3s + 翻包 3s | 0.45 | 0.90 | 0 | 1.35 | StealUIOpen |
| 蹲下 3s + 偷窃脉冲 | 0.45 | 0 | 2.0 | 2.45 | Steal |
| 脉冲后看不到玩家 10s | ~0.15 | 0 | ~0.65 | ~0.80 | Steal |

> **脉冲不设硬过期**。看不到玩家后按比例自然衰减，偷窃 +2.0 约 13 秒归零。

---

## 三、阶段穿越 → 发独立事件走 ReceiveEvent

阶段穿越检测在 `UpdateAlertCognition` → `CheckPhaseTransition` 内部完成。**不 `switch`、不传 `old/new` 枚举**——向上穿越发具体事件（`"BecomeSuspicious"` / `"BecomeCautious"` / `"BecomeAlarmed"`），向下穿越发 `"CalmDown"`。每个事件在 `ReceiveEvent` 里有自己的 `if` 块，与其他事件平级。

### 3.1 CheckPhaseTransition — 只做检测 + 发事件

```csharp
void CheckPhaseTransition()
{
    var newPhase = AlertPhase;
    if (newPhase == _lastAlertPhase) return;

    if (newPhase > _lastAlertPhase)
    {
        // 向上穿越：每个目标阶段一个独立事件
        string eventType = newPhase switch
        {
            AlarmPhase.Suspicious => "BecomeSuspicious",
            AlarmPhase.Cautious   => "BecomeCautious",
            AlarmPhase.Alarmed    => "BecomeAlarmed",
            _ => null
        };
        if (eventType != null)
            ReceiveEvent(new AIEvent { EventType = eventType, Sender = this });
    }
    else
    {
        // 向下穿越：统一 CalmDown（带 from/to 供清理用）
        ReceiveEvent(new AIEvent
        {
            EventType = "CalmDown",
            Sender = this,
            Args = new object[] { _lastAlertPhase, newPhase }
        });
    }

    _lastAlertPhase = newPhase;
}
```

### 3.2 ReceiveEvent 中的四个 if 块

```csharp
// ── 在 ReceiveEvent 的 flat if/else 中，与其他事件平级 ──

if (aiEvent.EventType == "BecomeSuspicious")
{
    BubbleSayOnce(AlarmPhase.Suspicious);
}
if (aiEvent.EventType == "BecomeCautious")
{
    if (_currentAction == null || _currentAction is StayAction)
        EnqueueAction(new LookAtAction(Agent.Main, 2.0f));
    BubbleSayOnce(AlarmPhase.Cautious);
}
if (aiEvent.EventType == "BecomeAlarmed")
{
    if (_pulseSuppressedUntil > 0 && Mission.Current?.CurrentTime < _pulseSuppressedUntil)
        return;
    StartL3Confrontation();
}
if (aiEvent.EventType == "CalmDown")
{
    var fromPhase = (AlarmPhase)aiEvent.Args[0];
    var toPhase   = (AlarmPhase)aiEvent.Args[1];

    // 清除高位 bubbled 记录，允许重新升级后再次触发
    _bubbledPhases.RemoveWhere(k => k.Item2 > toPhase);

    // Alarmed→* 或 →Normal：完全清理行为链
    if (fromPhase >= AlarmPhase.Alarmed || toPhase == AlarmPhase.Normal)
    {
        ClearAllActions();
        AgentControlHelper.ResumeVanillaAI(Owner);
    }
    // Cautious→Suspicious：只取消 LookAt
    else if (fromPhase == AlarmPhase.Cautious && _currentAction is LookAtAction)
    {
        _currentAction.OnEnd(Owner);
        _currentAction = null;
    }
}
```

**四个独立事件，零 switch，与其他事件（`"WitnessCrime_GatherOnLook"`、`"ComeHere"` 等）完全平级。**

| 设计 | 为什么 |
|------|--------|
| 每种向上穿越独立事件 | 行为不同——Sus 是 BubbleSay，Cau 是 LookAt+BubbleSay，Alm 是质问链。不值得用一个枚举 `switch` 聚在一起 |
| 向下统一 `"CalmDown"` | 降级行为相似（清行为链/取消动作），`fromPhase` 判断即可，不值得拆成多个事件 |
| `CheckPhaseTransition` 不发 `"AlertPhaseChanged"` | 避免旧通用事件再引出 `HandleAlertPhaseChanged`→`switch` 的套娃 |

> **不会循环**：这些 handler 只改 Action 队列和 `_bubbledPhases`，不改 `_alertBreakdown` 值，不会触发新的 phase 变化。`_lastAlertPhase` 在 `CheckPhaseTransition` 发事件**之后**才更新。

### BubbleSay 辅助方法

```csharp
/// <summary>通用 BubbleSay 入口。传入已组装好的文本，直接显示冒泡。</summary>
public void BubbleSay(string text)
{
    if (!string.IsNullOrEmpty(text))
        AgentHudMissionView.AgentSay(Owner, text);
}

/// <summary>
/// 尝试对当前 phase + PrimaryAction 发 BubbleSay。
/// 同 (action, phase) 组合只触发一次。降级后清空高位记录，重新升级可再次触发。
/// </summary>
void BubbleSayOnce(AlarmPhase phase)
{
    var action = PrimaryAction;
    if (action == null) return;

    var key = (action.Value, phase);
    if (_bubbledPhases.Contains(key)) return;

    _bubbledPhases.Add(key);
    BubbleSay(ResolveAlertBubble(phase));
}
```

---

## 四、L3 质问实现

> 脉冲事件的接收已在 2.4 通过 `ReceiveEvent` 入口直接处理。本章聚焦 L3 质问——`"BecomeAlarmed"` 事件 → `StartL3Confrontation` 的行为链和对话路径。

### 4.1 L3 质问 — "BecomeAlarmed" 事件触发

L3 到达时不需要走事件系统——`CheckPhaseTransition` 检测到阶段变化直接调 `StartL3Confrontation`：

```csharp
void StartL3Confrontation()
{
    Agent player = Agent.Main;
    if (player == null) return;

    ClearAllActions();
    InteractedAgent = player;

    if (Settings.Instance.AlertDialogueMode == AlertDialogueMode.VanillaConversation)
    {
        // 路径 B：原版对话 UI
        EnqueueAction(new FollowAgentAction(player, false, radius: 0f, angleOffset: 0f, stopDistance: 1.5f));
        EnqueueAction(new LookAtAction(player, 0.5f));
        EnqueueAction(new AlertForceConversationAction());
        EnqueueAction(new StayAction(player));
    }
    else
    {
        // 路径 A：StoryDialogVM（默认）
        var conflict = BuildAlarmConflict(PrimaryAction);
        EnqueueAction(new FollowAgentAction(player, false, radius: 0f, angleOffset: 0f, stopDistance: 1.5f));
        EnqueueAction(new LookAtAction(player, 0.5f));
        EnqueueAction(new PrepareOpeningAction(InitiativeType.CrimeAccusation, conflict));
        EnqueueAction(new ForceTalkAction());
        EnqueueAction(new StayAction(player));
    }
}
```

---

## 五、NPC 模板台词统一 — `NpcSpeech.csv`

### 5.0 为什么不用 Narrative.csv？

**Narrative.csv 是枚举思路**：每种对话情境一行，Honor × Gender × Identity × PersonalityTrait × Trust 维度组合爆炸。247 行很难维护，每加一种新情境就要穷举。

**模板思路**才是对的：`（{SPEAKER_EMOTION}地）{SPEAKER_PLAYER_ADDR}！把刀收起来！` — 一个模板，占位符填不同值覆盖所有 NPC 身份。`PlaceholderResolver`（80+ 占位符）和 `CrimeDialogueBuilder` 走的就是这个方向。

**统一决策**：新建 `NpcSpeech.csv` 作为所有 NPC 模板台词的唯一数据源——极简三列 `ID,Template,Emotion`。Narrative.csv 保留给现有 Intent 系统过渡，新功能不再往里加。LLM 可用时实时生成，不可用时回落模板。

### 5.1 `NpcSpeech.csv` 格式

极简三列 `ID,Template,Emotion`。**`Emotion` 值必须是 `Emotion.csv` 中已定义的 ID**（见 `wheels.md` Emotion ↔ NpcSpeech 一致性铁律）。**每个 ID 唯一**。

> **实施前验证**：Phase 0 第一步对照 `Emotion.csv` 确认以下 emotion ID 全部存在：`alert`, `threat`, `nervous`, `aggres`, `surprise`, `rage`。在 `GameDatabase` 加载 `NpcSpeech` 表时统一校验，未命中记错误日志 + 回落 `normal`（wheels.md 已有校验代码）。

```csv
ID,Template,Emotion
AlertBubble_Crouching_Suspicious,（嘀咕）{PLAYER}在这做什么……,alert
AlertBubble_Crouching_Cautious,（提高声音）喂！{PLAYER}！蹲着干什么！,threat
AlertBubble_WeaponDrawn_Suspicious,（不安地看了一眼）怎么还拔刀了……,nervous
AlertBubble_WeaponDrawn_Cautious,（后退半步）{SPEAKER_PLAYER_ADDR}！把刀收起来！,threat
AlertBubble_StealUIOpen_Suspicious,（瞟了一眼）在翻什么呢……,alert
AlertBubble_StealUIOpen_Cautious,（盯着）喂！{SPEAKER_PLAYER_ADDR}！你在翻什么！,aggres
AlertBubble_Steal_Suspicious,咦，{TARGET}的{ITEM}呢？,surprise
AlertBubble_Steal_Cautious,（惊呼）{SPEAKER_PLAYER_ADDR}！你偷了{ITEM}！,rage
AlertBubble_AttackAlly_Suspicious,（惊）怎么回事？！,surprise
AlertBubble_AttackAlly_Cautious,（后退）{SPEAKER_PLAYER_ADDR}打人了！,rage
AlertBubble_Knockout_Suspicious,（惊恐）出人命了……,nervous
AlertBubble_Knockout_Cautious,来人！{SPEAKER_PLAYER_ADDR}把{TARGET}打倒了！,threat
```

> **L3 质问对话不在这里**。L3 走 `CrimeDialogueBuilder.BuildAlertInterceptScript` → `PlaceholderResolver` → `DialogueInjector` → `ConversationManager` 管道。与 `BuildAuthorityScript` / `BuildWitnessScript` 同属一个类——输入不同（WorldEvent vs PlayerActionType），产出相同（`DialogueInjectScript`），共享 `PlaceholderResolver` 引擎。

**`NpcSpeechResolver` 不替代 `PlaceholderResolver`——它只是 CSV 模板查找 + 委托。** 核心解析引擎仍然是 `PlaceholderResolver`（~80 占位符，含 WorldEvent 上下文），`NpcSpeechResolver` 补上数据驱动的模板存储这一环。

```csharp
/// <summary>
/// NPC 台词模板的统一 CSV 查询入口。
/// 查 NpcSpeech.csv 取模板文本 → 委托 PlaceholderResolver 做占位符替换。
/// PlaceholderResolver 的核心能力（WorldEvent 语境、Campaign 层占位符）完整保留。
/// </summary>
public static class NpcSpeechResolver
{
    /// <summary>
    /// 查模板 + 解析占位符。所有占位符统一走 PlaceholderResolver（不拆分 extra 步骤）。
    /// targetName / itemName 为 Mission 层脉冲上下文，传 null 时对应占位符解析为空字符串。
    /// </summary>
    public static string Resolve(string templateId, Hero speaker, Hero listener = null,
        WorldEvent evt = null, string targetName = null, string itemName = null)
    {
        // ① 查 NpcSpeech.csv 取模板文本
        string template = LookupTemplate(templateId);
        if (string.IsNullOrEmpty(template)) return null;

        // ② 委托 PlaceholderResolver 做占位符替换（含 Campaign 语境 + Mission 层脉冲上下文）
        var r = new PlaceholderResolver(evt, speaker, listener, targetName, itemName);
        return r.Resolve(template);
    }

    static string LookupTemplate(string templateId)
    {
        var row = GameDatabase.NpcSpeech?.GetByID(templateId);
        if (row != null)
        {
            string template = row.GetString("Template");
            if (string.IsNullOrEmpty(template)) return null;
            // 变体语法：单行内 | 分隔，随机取一
            var variants = template.Split('|');
            return variants.Length == 1
                ? variants[0]
                : variants[MBRandom.RandomInt(variants.Length)];
        }
        // CSV 未命中 → 返回 null，让调用方决定回落策略（NarrativeResolver 或 PlaceholderResolver 直接解析）
        return null;
    }
}
```

**与 `PlaceholderResolver` 的关系**：

```
NpcSpeech.csv（新增：数据驱动的模板存储）
       │
       ▼
NpcSpeechResolver.Resolve(id, speaker, listener, evt, targetName, itemName)
       │
       ├─ ① LookupTemplate(id) → 从 CSV 取模板文本
       └─ ② new PlaceholderResolver(evt, speaker, listener, targetName, itemName).Resolve(template)
              ↑
         核心引擎完整保留，含 WorldEvent / Campaign 层 ~80 占位符
```

| | PlaceholderResolver（保留+增强） | NpcSpeechResolver（新建） |
|----|-----|------|
| 职责 | 占位符 → 真实值的解析引擎 | CSV 模板查找 + 委托 |
| 占位符 | ~80（完整保留） | 0（全部委托给 PlaceholderResolver） |
| 模板来源 | 调用方传入字符串 | NpcSpeech.csv（按 ID 查） |
| WorldEvent 支持 | ✅ 核心能力 | ✅ 透传给 PlaceholderResolver |
| 当前消费者 | CrimeDialogueBuilder | 警戒 BubbleSay |

### 5.3 调用方

```csharp
// ── AgentBrain 通用 BubbleSay（任何系统组装好文本后调用）──
public void BubbleSay(string text)
{
    if (!string.IsNullOrEmpty(text))
        AgentHudMissionView.AgentSay(Owner, text);
}

// ── 警戒专用：查 NpcSpeech.csv → 委托 PlaceholderResolver ──
string ResolveAlertBubble(AlarmPhase phase)
{
    var action = PrimaryAction;
    if (action == null) return null;

    string targetName = null, itemName = null;
    if (_alertBreakdown.TryGetValue(action.Value, out var entry))
    {
        targetName = entry.TargetName;
        itemName = entry.ItemName;
    }

    // 所有占位符（含 {TARGET}/{ITEM}）统一走 PlaceholderResolver
    return NpcSpeechResolver.Resolve(
        $"AlertBubble_{action}_{phase}",
        speaker: Owner.Character?.HeroObject,
        listener: Hero.MainHero,
        evt: null,
        targetName: targetName,
        itemName: itemName
    );
}

// 调用：
BubbleSay(ResolveAlertBubble(phase));

// ── L3 质问开场白 ──
// L3 走 CrimeDialogueBuilder.BuildAlertInterceptScript → PlaceholderResolver → DialogueInjector。
// 与 BuildAuthorityScript / BuildWitnessScript 同属 CrimeDialogueBuilder——
// 输入不同（PlayerActionType vs WorldEvent），产出相同（DialogueInjectScript），共享 PlaceholderResolver。
```

### 5.4 统一数据流

```
NpcSpeech.csv（数据驱动的模板存储 — 新增）
       │
       ▼
NpcSpeechResolver.Resolve(id, speaker, listener, evt, targetName, itemName)  ← CSV 查询薄层
       │
       ▼
PlaceholderResolver.Resolve(template)  ← 核心引擎（完整保留，~80 占位符）
       │
       ▼
AgentHudMissionView.AgentSay  /  ConversationManager  ← 显示出口

LLM 路径（IsLLMReady = true）：
  跳过模板系统 → LLM 实时生成 → 直接显示
```

### 5.5 占位符标准 + Emotion 管线

| 占位符 | 含义 | 来源 |
|--------|------|------|
| `{PLAYER}` | 玩家名 | `Hero.MainHero.Name` |
| `{SPEAKER}` | 说话 NPC 名 | `speaker.Name` |
| `{SPEAKER_SELF}` | NPC 自称 | `AttitudeSystem.GetSelfReference` |
| `{SPEAKER_PLAYER_ADDR}` | NPC 对玩家的称呼 | `AttitudeSystem.GetPlayerAddress` |
| `{SPEAKER_EMOTION}` | NPC 情绪词 | `Emotion.csv` 查 `ScriptName` 列（如 `alert→"警戒"`） |
| `{TARGET}` | 目标/受害者名 | PlaceholderResolver（`targetName` 参数） |
| `{ITEM}` | 物品名 | PlaceholderResolver（`itemName` 参数） |
| `{StolenItemName}` | 被盗物品名 | PlaceholderResolver（`itemName` 参数，L3 台词用） |
| `{LOCATION}` | 当前定居点 | `Settlement.Current.Name` |

**Emotion → 动画管线**：

```
NpcSpeech.csv Emotion列（如 alert / threat / rage）
        │
        ▼
  Emotion.csv 查行
        │
        ├─ Animations: act_conversation_warrior_start（对话动作）
        ├─ Weight: 3（同 emotion 多动画时的随机权重）
        ├─ Keywords: "小心"（LLM 路径匹配用）
        └─ ScriptName: "警戒"（{SPEAKER_EMOTION} 占位符显示用）
                │
                ▼
  AgentControlHelper.ForcePlayAction(agent, animationId)
```

> 模板路径和 LLM 路径共享同一套 `Emotion.csv`——模板用 `Emotion` 列直接指定，LLM 用 `Keywords` 列从生成文本中反查。

> **与 Narrative.csv 的关系**：`NpcSpeechResolver` 内部第一优先查 `NpcSpeech.csv`，未命中自动回落 `NarrativeResolver`。过渡期内两套并存，现有 Intent 系统不受影响。长期目标是把 Narrative.csv 中适合模板化的行逐步迁到 `NpcSpeech.csv`，Narrative.csv 缩减为纯委托（Commission）叙事专用。
>
> **TaikouContent 覆盖**：替换 `NpcSpeech.csv` 整行文本。占位符 `{SPEAKER_SELF}` / `{SPEAKER_PLAYER_ADDR}` 底层已走 `AttitudeSystem` → `Settings.Instance` 世界观参数。两层独立，互不干扰。

---

## 六、L3 质问对话 — 双路径开关

`Settings.Instance.AlertDialogueMode` 控制：

### 路径 A：StoryDialogVM（默认）
```
FollowAgentAction(player, keepFollow:false, stopDistance:1.5f) → PrepareOpeningAction → ForceTalkAction
  ├─ IsLLMReady → StartFreeConversationFlow（自由对话）
  └─ !IsLLMReady → ShowVanillaConfrontation（InquiryData 弹窗兜底）
```

### 路径 B：VanillaConversation（新增）
```
FollowAgentAction(player, keepFollow:false, stopDistance:1.5f) → AlertForceConversationAction
  ├─ CrimeDialogueBuilder.BuildAlertInterceptScript(primaryAction)
  ├─ DialogueInjector.InjectScriptAsNpcInitiative(script)  // 跳过 gateway PlayerLine
  ├─ 强制 MissionConversationLogic.StartConversation(npc, player)
  └─ 对话结束 → ResetCrimeDialogueOnConversationEndPatch 自动清理
```

### 6.1 NPC 意图优先 — NPC 为什么走过来？想要什么？

每个 `PlayerActionType` 触发 L3 时，NPC 带着一个明确的意图。这个意图决定了 NPC 要求什么、接受什么、不接受什么。

```csharp
public enum NpcInterceptIntent
{
    /// <summary>
    /// 威慑 — NPC 看到可疑但非犯罪的行为（蹲下/拔刀）。
    /// 要求：停止行为 + 给解释。
    /// </summary>
    Deter,

    /// <summary>
    /// 搜查 — 玩家翻了半天包，NPC 怀疑偷了东西但没亲眼看见。
    /// 要求：打开背包接受检查。
    /// 拒绝搜查 → 升级为 Recover。搜到赃物 → 升级为 Recover。没搜到 → 道歉退开。
    /// </summary>
    Search,

    /// <summary>
    /// 追回 — NPC 亲眼目击了偷窃（脉冲触发）。
    /// 要求：归还物品 + 赔偿。
    /// </summary>
    Recover,

    /// <summary>
    /// 制止 — NPC 目击了暴力行为（攻击/击晕）。
    /// 要求：立刻住手 + 赔偿 + 离开。
    /// </summary>
    Stop,
}

// AgentBrain 根据 PrimaryAction 确定 NPC 意图：
NpcInterceptIntent npcIntent = PrimaryAction switch
{
    PlayerActionType.Crouching or PlayerActionType.WeaponDrawn => NpcInterceptIntent.Deter,
    PlayerActionType.StealUIOpen => NpcInterceptIntent.Search,
    PlayerActionType.Steal => NpcInterceptIntent.Recover,
    PlayerActionType.AttackAlly or PlayerActionType.Knockout => NpcInterceptIntent.Stop,
    _ => NpcInterceptIntent.Deter
};
```

### 6.2 四种意图详解

#### Deter（威慑）— 触发：蹲下 / 武器出鞘

NPC 看到玩家行为异常（蹲下鬼鬼祟祟 / 拔刀在村子里晃），**没有证据**证明干了坏事，但需要制止这种行为。

| 触发 | 开场白 |
|------|--------|
| 蹲下 | "喂！{PlayerName}！蹲在那鬼鬼祟祟干什么？" |
| 武器出鞘 | "把刀收起来！这是村子，不是战场！" |

| 要素 | 内容 |
|------|------|
| NPC 要求 | 停止行为 + 给解释 |
| 玩家配合 | 蹲下→"没什么，我这就走。" / 拔刀→"好，我收起来。" → NPC："别再让我看见。"（警戒值开始衰减） |
| 玩家挑衅 | "关你什么事？" → Roguery 检定。成功→NPC 退缩；失败→周围 NPC 警戒值+0.5 |
| NPC **不会**做 | 要求赔钱、搜身、直接动手 |

#### Search（搜查）— 触发：翻包（StealUIOpen）

玩家反复翻包/打开偷窃界面，NPC 怀疑偷了东西但**没亲眼看见**。NPC 的核心诉求是验证——"让我看看你有没有偷东西"。

| 要素 | 内容 |
|------|------|
| NPC 开场 | "{PlayerName}在翻什么？把手拿开，让{SpeakerSelfRef}看看你的包。" |
| NPC 要求 | **打开背包接受检查** |
| 玩家接受搜查 | 系统检查背包中是否有赃物（遍历 `MobileParty.MainParty.ItemRoster`，`TheftLedger.GetSourceTag(itemId, Hero.MainHero.StringId)` 判空）： |
| …搜到赃物 | NPC："这是什么？！还说没偷！" → 意图升级为 Recover，对话进入 Recover 选项 |
| …没搜到 | NPC："……行吧。是{SpeakerSelfRef}多心了。" → 搜查失败，NPC 道歉，警戒值清空 |
| 玩家拒绝搜查 | NPC："不敢让人看？那就是有鬼了！" → 意图直接升级为 Recover（NPC 认定你有问题） |
| 玩家花钱私了 | "别查了，我赔你点钱行了吧。" → 等于变相承认，关系-5，付钱后 NPC 不搜但也不信你 |
| 玩家挑衅 | "你凭什么翻我东西？" → Charm/Roguery 检定。成功→NPC 退缩；失败→同上，拒绝搜查逻辑 |
| NPC **不会**做的事 | 直接动手（没证据）、叫守卫（还不够严重） |

#### Recover（追回）— 触发：偷窃脉冲

NPC **亲眼目击**了偷窃，有明确损失。目标只有一个——止损。

| 要素 | 内容 |
|------|------|
| NPC 开场 | "{SpeakerSelfRef}看见了！你偷了{ItemName}！交出来！" |
| NPC 要求 | 归还物品 + 赔偿 |
| 玩家归还/赔钱 | "好，还给你。" → TransferGold，物品归还，关系-3。NPC："算你识相。别再来了。" |
| 玩家抵赖 | "你哪只眼睛看见的？" → Charm 检定（DC 50）。成功→NPC 动摇（"可能看错了"），警戒值清空；失败→关系-10 |
| 玩家逃跑 | "（推开就跑）" → Mission 内力量检定。成功→逃脱（嫌犯锁定）；失败→被捕 |
| NPC **不会**接受 | "我这就走"（"站住！东西先还了！"） |

#### Stop（制止）— 触发：攻击/击晕脉冲

NPC 目击了暴力行为。目标——终止暴力、保护社区。**没有"抵赖"选项**——打人不可能看错。

| 要素 | 内容 |
|------|------|
| NPC 开场 | 攻击："{PlayerName}竟敢动手打人？！住手！" / 击晕："{PlayerName}把{TargetName}打晕了！来人！" |
| NPC 要求 | 立刻住手 + 赔偿 + 离开 |
| 玩家赔钱 | "我愿意赔。" → TransferGold(×3)，关系-10。NPC："光赔钱就完了？拿了钱快滚。" |
| 玩家解释 | "他先惹我的。" → Charm 检定（DC 70，比 Recover 高）。成功→关系-5，勉强放过；失败→关系-15 |
| 玩家拔剑 | "谁敢拦我！" → 进战斗，恶名+5，报复部队 spawn，村庄永久敌对 |
| NPC **不会**接受 | "我这就走"（"打了人想跑？！"）、抵赖（"你哪只眼睛看见的"——打架怎么可能看错） |

### 6.3 BuildAlertInterceptScript 实现

> **台词查找顺序**：① 优先 `NpcSpeechResolver.Resolve($"L3_{Intent}_{Action}", …)` 查 `NpcSpeech.csv` → ② 回落 `NarrativeResolver`（过渡） → ③ 下方 `r.Resolve("硬编码模板")` 兜底。
> **注意**：Phase 0 先在 `NpcSpeech.csv` 中加入 L3 开场白模板行（ID 如 `L3_Deter_WeaponDrawn`、`L3_Recover_Steal` 等），否则始终走回落路径。代码中 `r.Resolve()` 的硬编码字符串是最后的安全网，不是主路径。

```csharp
public static DialogueInjectScript BuildAlertInterceptScript(
    Hero speaker, NpcInterceptIntent npcIntent, PlayerActionType primaryAction)
{
    var r = new PlaceholderResolver(speaker, Hero.MainHero);
    var turns = new List<DialogueNode>();

    // ① 优先查 NpcSpeech.csv
    string csvTemplateId = $"L3_{npcIntent}_{primaryAction}";
    string npcOpening = NpcSpeechResolver.Resolve(csvTemplateId, speaker, Hero.MainHero);

    // ② CSV 未命中 → 回落 NarrativeResolver（过渡）
    if (string.IsNullOrEmpty(npcOpening))
    {
        var narrResult = NarrativeResolver.Resolve(new NarrativeFilters
        {
            EventName = "L3AlertIntercept",
            GoalType = npcIntent.ToString(),
            Outcome = primaryAction.ToString(),
        });
        if (narrResult != null && !NarrativeResolver.IsFallbackText(narrResult.Text))
            npcOpening = narrResult.Text;
    }

    // ③ 最终兜底：PlaceholderResolver 直接解析硬编码模板
    if (string.IsNullOrEmpty(npcOpening))
    {
        npcOpening = npcIntent switch
        {
        NpcInterceptIntent.Deter => primaryAction switch
        {
            PlayerActionType.WeaponDrawn =>
                r.Resolve("（{SpeakerEmotion}地）把刀收起来！{SpeakerPlayerAddr}！这是村子，不是战场！"),
            _ => // Crouching
                r.Resolve("（{SpeakerEmotion}地）喂！{SpeakerPlayerAddr}！蹲在那鬼鬼祟祟干什么？"),
        },

        NpcInterceptIntent.Search =>
            r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}在翻什么？把手拿开，让{SpeakerSelfRef}看看你的包。"),

        NpcInterceptIntent.Recover =>
            r.Resolve("（{SpeakerEmotion}地）{SpeakerSelfRef}看见了！{SpeakerPlayerAddr}偷了{StolenItemName}！交出来！"),

        NpcInterceptIntent.Stop => primaryAction switch
        {
            PlayerActionType.AttackAlly =>
                r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}竟敢动手打人？！住手！"),
            PlayerActionType.Knockout =>
                r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}把{TargetName}打晕了！来人！"),
            _ => r.Resolve("（{SpeakerEmotion}地）住手！")
        },
        _ => r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}！你在干什么？")
    };
    } // ③ 硬编码兜底结束

    turns.Add(new DialogueNode
    {
        Id = "start", SpeakerIndex = 0, NpcLine = npcOpening,
        Transitions = BuildTransitionsByIntent(r, npcIntent, primaryAction)
    });

    // Search 成功后如果搜到赃物 → 插入一个额外 turn 把意图切换为 Recover
    if (npcIntent == NpcInterceptIntent.Search)
    {
        bool hasStolen = PlayerHasStolenItems();
        turns.Add(BuildSearchResultNode(r, hasStolen));
    }

    // continue_chat
    turns.Add(new DialogueNode
    {
        Id = "continue_chat", SpeakerIndex = 0,
        NpcLine = "还有什么想说的？",
        Transitions = new List<DialogueTransition>
        {
            new() { PlayerLine = "我走了。", Action = "INTENT:WalkAway", NextNode = "" }
        }
    });

    return new DialogueInjectScript { EntryNode = "start", Nodes = turns };
}

static List<DialogueTransition> BuildTransitionsByIntent(
    PlaceholderResolver r, NpcInterceptIntent intent, PlayerActionType action)
{
    var transitions = new List<DialogueTransition>();

    switch (intent)
    {
        case NpcInterceptIntent.Deter:
            // 配合行为决定台词基调
            string complyLine = action == PlayerActionType.WeaponDrawn
                ? "好，我收起来。"
                : "没什么，我这就走。";
            string complyResp = action == PlayerActionType.WeaponDrawn
                ? "……别再让{SpeakerSelfRef}看见你在这拔刀。"
                : "……别再让{SpeakerSelfRef}看见你鬼鬼祟祟的。";
            transitions.Add(new() { PlayerLine = complyLine, NpcResponse = r.Resolve(complyResp), Action = "NONE", NextNode = "" });
            transitions.Add(new() { PlayerLine = "关你什么事？（挑衅）", NpcResponseOnSuccess = r.Resolve("……算了。"), NpcResponseOnFail = r.Resolve("来人！这有个闹事的！"), Action = "INTENT:Threat", NextNode = "continue_chat" });
            transitions.Add(new() { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextNode = "" });
            break;

        case NpcInterceptIntent.Search:
            transitions.Add(new() { PlayerLine = "……行，你看吧。", Action = "INTENT:SubmitToSearch", NextNode = "search_result" });
            transitions.Add(new() { PlayerLine = "凭什么翻我东西？（拒绝）", NpcResponse = r.Resolve("不敢让人看？那就是有鬼了！"), Action = "INTENT:RefuseSearch", NextNode = "recover_confront" });
            transitions.Add(new() { PlayerLine = "别查了，我赔你点钱。", NpcResponse = r.Resolve("……做贼心虚。拿了钱滚。"), Action = "INTENT:PayRestitution", NextNode = "" });
            transitions.Add(new() { PlayerLine = "（转身就走）", NpcResponse = r.Resolve("站住！"), Action = "INTENT:WalkAway", NextNode = "" });
            break;

        case NpcInterceptIntent.Recover:
            transitions.Add(new() { PlayerLine = r.Resolve("好，还给你。（{RestitutionCost} 第纳尔）"), NpcResponse = r.Resolve("算你识相。别再来了。"), Action = "INTENT:PayRestitution", NextNode = "" });
            transitions.Add(new() { PlayerLine = r.Resolve("你哪只眼睛看见的？"), NpcResponseOnSuccess = r.Resolve("……{SpeakerSelfRef}可能看错了。"), NpcResponseOnFail = r.Resolve("{SpeakerSelfRef}两只眼睛都看见了！"), Action = "INTENT:CharmDefense", NextNode = "continue_chat" });
            transitions.Add(new() { PlayerLine = "（推开就跑）", Action = "INTENT:WalkAway", NextNode = "" });
            break;

        case NpcInterceptIntent.Stop:
            transitions.Add(new() { PlayerLine = r.Resolve("我愿意赔钱。（{RestitutionCost} 第纳尔）"), NpcResponse = r.Resolve("光赔钱就完了？拿了钱快滚。"), Action = "INTENT:PayRestitution", NextNode = "" });
            transitions.Add(new() { PlayerLine = "他先惹我的。", NpcResponseOnSuccess = r.Resolve("……下次再动手没这么好说话。"), NpcResponseOnFail = r.Resolve("在{SpeakerSelfRef}眼皮底下动手，就得有个说法！"), Action = "INTENT:CharmDefense", NextNode = "continue_chat" });
            transitions.Add(new() { PlayerLine = "(威胁)谁拦着我就杀谁！", NpcResponse = r.Resolve("{SpeakerPlayerAddr}疯了！快叫人！"), Action = "INTENT:FightVillagers", NextNode = "" });
            break;
    }

    return transitions;
}

/// <summary>搜查结果 turn（统一 ID "search_result"）：接受搜查后，系统查 TheftLedger 判定玩家背包是否有赃物。
/// 有赃物 → NPC 质问（意图升级为 Recover）
/// 无赃物 → NPC 道歉（警戒值清空）
/// 调用前预判 hasStolenItems，生成时选择对应的内容分支。NextNode 始终指向 "search_result"。</summary>
static DialogueNode BuildSearchResultNode(PlaceholderResolver r, bool hasStolenItems)
{
    return new DialogueNode
    {
        Id = "search_result",  // 统一 ID，内部按 hasStolenItems 分支内容
        SpeakerIndex = 0,
        NpcLine = hasStolenItems
            ? r.Resolve("（{SpeakerEmotion}地）这是什么？！还说没偷！")
            : r.Resolve("（{SpeakerEmotion}地）……行吧。是{SpeakerSelfRef}多心了。"),
        Transitions = hasStolenItems
            ? new List<DialogueTransition>
            {
                new() { PlayerLine = "……（无言以对）", Action = "INTENT:Confess", NextNode = "continue_chat" },
                new() { PlayerLine = "那是我的东西！", NpcResponse = r.Resolve("你的？上面还写着{TargetName}的名字呢！"), Action = "NONE", NextNode = "continue_chat" },
            }
            : new List<DialogueTransition>
            {
                new() { PlayerLine = "我说了没拿吧。", Action = "NONE", NextNode = "" },
            }
    };
}

/// <summary>查 TheftLedger 判定玩家背包是否有赃物</summary>
static bool PlayerHasStolenItems()
{
    if (MobileParty.MainParty?.ItemRoster == null) return false;
    foreach (var item in MobileParty.MainParty.ItemRoster)
    {
        if (item.EquipmentElement.Item == null) continue;
        string tag = TheftLedger.GetSourceTag(
            item.EquipmentElement.Item.StringId, Hero.MainHero.StringId);
        if (!string.IsNullOrEmpty(tag)) return true;
    }
    return false;
}
```

---

## 七、Console 调试指令

```
custom.alert_status <agentStringId>    # 查看某 NPC 的分类警戒值明细
custom.alert_set_action <type>         # 手动触发脉冲（测试）
custom.alert_force_intercept <npcId>   # 强制触发 L3 质问
custom.alert_dialogue_mode <mode>      # StoryVM / Vanilla
```

---

## 八、实现路线图

| Phase | 内容 |
|-------|------|
| **0 对齐** | `NpcSpeech.csv` + `NpcSpeechResolver`（委托 `PlaceholderResolver`）；`GameDatabase` 加载 `NpcSpeech` 表 + Emotion 一致性校验；`PlaceholderResolver` 增强（新增 `{TARGET}`/`{ITEM}`/`{StolenItemName}` 占位符）；`ReceiveEvent` 的 `WitnessCrime_GatherOnLook` 块内加脉冲逻辑（~4 行）+ 角色分流 |
| **1 数据层** | `AlarmPhase` / `PlayerActionType` / `AlertEntry` 类型定义；`AgentBrain` 新增 `_alertBreakdown`（`Dictionary<PlayerActionType, AlertEntry>`）+ `_bubbledPhases`；`AddAlert` / `AlertValue` / `PrimaryAction` / `AlertPhase`（仅阶段转换时调用，非每帧）；每帧 Tick 累加 + 按比例衰减；旧 `NpcSightSystem._alertValues` 字典和 `UpdateAlertValue` 方法删除；`AgentHudMissionView` 改从 `brain.AlertValue` / `brain.AlertPhase` 读警戒值（不读 `PrimaryAction`） |
| **2 L1+L2** | `UpdateAlertCognition` 节流累加 + `CheckPhaseTransition` 发独立事件（`"BecomeSuspicious"` / `"BecomeCautious"` / `"BecomeAlarmed"` / `"CalmDown"`）；L1/L2 的 `BubbleSayOnce`（同 phase 同 key 只触发一次）+ LookAt |
| **3 L3 路径 A** | `StartL3Confrontation` → `PrepareOpeningAction` → `ForceTalkAction`；`FollowAgentAction(player, keepFollow: false, stopDistance: 1.5f)`；脉冲抑制校验 |
| **4 L3 路径 B** | `Settings.AlertDialogueMode`；`BuildAlertInterceptScript`（CSV 优先 + `PlaceholderResolver` 兜底）；`DialogueInjector.InjectScriptAsNpcInitiative`；`AlertForceConversationAction`；反编译 `StartConversation` API |
| **5 打磨** | BubbleSay 频率调优；脉冲抑制验证；`PlaceholderResolver` 增强（新增 `{TARGET}`/`{ITEM}`/`{StolenItemName}` 占位符 + `targetName`/`itemName` 构造参数）；`NpcSightSystem` 旧代码清理 |

---

## 九、与现有系统的关系

| 系统 | 关系 | 说明 |
|------|------|------|
| **NpcSightSystem** | 🔵 **纯感知工具** | 只提供 `CanNpcSeePlayer` / `GetObserversOf`，旧 `_alertValues` 字典和 `UpdateAlertValue` 方法删除，警戒值状态全部迁移到 AgentBrain |
| **AgentBrain** | 🔴 **认知所有者** | `_alertBreakdown` 实例字段；Tick 中自行判断 + 累加 + 衰减 + 阶段穿越；`ReceiveEvent` 的 `WitnessCrime_GatherOnLook` 块内同时处理脉冲加值和行为分流（受害者指控/普通围观），**不走 IntentRegistry 分发** |
| **IntentRegistry** | 🔵 **不受影响（仅玩家菜单）** | NPC 事件响应不再走 `GetNpcInitiatives` 分发。`CrimeAccusationIntent` / `GuardInterceptIntent` 已移除。IntentRegistry 继续服务玩家交互菜单（`GetVisible`） |
| **IntentContext** | 🔵 **不受影响** | 脉冲不走 IntentBase → 不需要 `TriggeringEvent`/`Brain` 字段（原计划新增的两个字段取消） |
| **AgentAIController** | 🔵 **不受影响** | `BroadcastEventInRange("WitnessCrime", …)` 链路不动；脉冲在 `ReceiveEvent` 的 `WitnessCrime_GatherOnLook` 块内处理 |
| **AgentHudMissionView** | 🔵 **读值渲染** | 从 `brain.AlertValue` / `brain.AlertPhase` 读警戒值 + 颜色（Phase 1，与数据层同步）。不读 `PrimaryAction` |
| **PlaceholderResolver** | 🔵 **增强** | 统一处理 Campaign 层 + Mission 层全部占位符。新增构造参数 `targetName`/`itemName`，对应 `{TARGET}`/`{ITEM}`/`{StolenItemName}`。`BuildAlertInterceptScript` 与 `CrimeDialogueBuilder` 共享同一引擎 |
| **NpcSpeechResolver** | 🆕 **新建** | BubbleSay 的 CSV 查询薄层，委托 `PlaceholderResolver` 做占位符解析 |
| **NpcSpeech.csv** | 🆕 **新建** | ~12 行 `AlertBubble_*` + ~6 行 `L3_*` 模板（`ID,Template,Emotion`），Emotion 值必须是 `Emotion.csv` 中已定义的 ID |
| **Settings.Instance** | 🆕 **新开关** | `AlertDialogueMode` 控制 L3 对话走 StoryDialogVM 还是 ConversationManager |
| **CrimeDialogueBuilder** | 🆕 **新方法** | `BuildAlertInterceptScript(primaryAction)` — 与 `BuildAuthorityScript` / `BuildWitnessScript` 并列，统一走 `PlaceholderResolver.Resolve()` |
| **DialogueInjector** | 🆕 **新重载** | `InjectScriptAsNpcInitiative` — NPC 主动开场，不走 `hero_main_options` |
| **PrepareOpeningAction / ForceTalkAction** | 🔵 **路径 A 复用** | StoryDialogVM 默认路径，已有轮子 |
| **WitnessCrime_GatherOnLook** | 🔵 **复用现有事件** | 脉冲 + 行为分流全部在 `ReceiveEvent` 的同一个 `if` 块内完成。不新建事件类型，不走 IntentRegistry。`StealVM` → `BroadcastEventInRange` → `GroupStageManager` 链路不动 |

---

## 十、警戒值公式（总结）

```
// AgentBrain.Tick → UpdateAlertCognition
// 能看到玩家 → 持续累加：
_alertBreakdown[Crouching]   += dt * 0.15f   (if crouching)
_alertBreakdown[WeaponDrawn] += dt * 0.20f   (if weapon drawn in civilian area)
_alertBreakdown[StealUIOpen] += dt * 0.30f   (if steal UI open)

// 脉冲事件 → AgentBrain.ReceiveEvent 的 WitnessCrime_GatherOnLook 块内：
_alertBreakdown[Steal]      += 2.0f   (if Args[0]==Agent.Main)
+ _pulseSuppressedUntil = now + 3.0f

// 看不到玩家 → DecayAlertBreakdown：
每个条目 -= dt * 0.15 * (该条目值 / 总值)
衰减到 ≤0.0001f 时从字典中移除（AlertEntry 整体移除，TargetName/ItemName 自动清理）

// 公开属性（每帧按需计算）：
AlertValue    = _alertBreakdown.Values.Sum(e => e.Value)
PrimaryAction = _alertBreakdown.OrderByDescending(kv => kv.Value.Value).First().Key
AlertPhase    = AlertValue switch { >=2.0→Alarmed, >=1.0→Cautious, >=0.25→Suspicious, _→Normal }
```

---
## 十一、存档与生命周期

**`_alertBreakdown` 是 Mission 层数据，不跨存档持久化。** 决策理由：
- 警戒值是 NPC 对玩家**实时**行为的反应，依赖 Mission 层可见性（FOV + RayCast）
- 玩家离开场景（退出 Mission）→ 3D 世界中的"被目击"状态自然重置
- 跨场景的长期后果（NPC 记住了你的可疑行为）应通过 **Campaign 层记忆系统**（`SingNpcMemorySystem`）实现，不在本系统范围内

若未来需要"这个 NPC 上次在场景里看到我做贼 → 这次见面态度变差"的跨场景记忆，在 `ReceiveEvent` 脉冲入口额外写入 `SingNpcMemorySystem.AddExperience()` 即可——不需要改变本系统的 Mission 层设计。

---
## 十二、统一台词架构：`NpcSpeech.csv` + `PlaceholderResolver`

### 架构决策：CSV 存模板，PlaceholderResolver 做解析

| 组件 | 职责 | 状态 |
|------|------|------|
| **NpcSpeech.csv** | 数据驱动的模板存储（`ID,Template,Emotion`） | 🆕 新建 |
| **NpcSpeechResolver** | CSV 查询薄层 → 委托 `PlaceholderResolver` | 🆕 新建 |
| **PlaceholderResolver** | 核心占位符解析引擎（~80 占位符，WorldEvent 语境 + Mission 层 targetName/itemName） | 🔵 增强（新增 `{TARGET}`/`{ITEM}`/`{StolenItemName}`） |
| **Narrative.csv** | Intent 系统枚举式对话（Honor×Gender×Identity 维度） | 🔵 现有系统继续使用 |
| **NarrativeResolver** | Narrative.csv 查询引擎 | 🔵 现有系统继续使用 |

### 统一数据流

```
NpcSpeech.csv（模板存储）
       │
       ▼
NpcSpeechResolver.Resolve(id, …)  ← 薄层：查 CSV + 委托
       │
       ▼
PlaceholderResolver(evt, speaker, listener, targetName, itemName).Resolve(template)  ← 核心引擎：全部占位符 → 真实值
       │                                           ↑
       │                              {SpeakerEmotion} → Emotion.csv → Animation
       │                              {CrimeScene} → WorldEvent
       │                              {SpeakerSelfRef} → AttitudeSystem → Settings
       ▼
AgentHudMissionView.AgentSay / ConversationManager
```

### 本计划涉及的文件

| 文件 | 动作 | 内容 |
|------|------|------|
| `NpcSpeech.csv` | 🆕 新建 | ~12 BubbleSay + ~6 L3 开场白 |
| `NpcSpeechResolver.cs` | 🆕 新建 | CSV 查询 + 委托 PlaceholderResolver |
| `PlaceholderResolver.cs` | 🔵 增强 | 新增构造参数 `targetName`/`itemName`，统一处理 `{TARGET}` / `{ITEM}` / `{StolenItemName}` 占位符（不再依赖调用方 extra dict 预处理） |
| `GameDatabase.cs` | 🔵 增强 | 加载 `NpcSpeech` 表 + Emotion 一致性校验 |
| `wheels.md` | 🔵 新增规则 | Emotion ↔ NpcSpeech 一致性铁律 |

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

`_alertBreakdown` 从 `NpcSightSystem` 的静态字典**移到每个 `AgentBrain` 实例**。每个 NPC 独立维护自己对玩家的警戒值明细——我看到什么、我多怀疑、我该说什么，都是"我"的事。

### 2.2 数据结构

```csharp
public enum PlayerActionType
{
    Crouching,      // 蹲下
    WeaponDrawn,    // 武器出鞘（和平区域）
    StealUIOpen,    // 偷窃界面打开未确认
    Trespassing,    // 闯入禁地（守卫区域/贵族私宅）
    Steal,          // 偷窃（脉冲）
    AttackAlly,     // 攻击友方（脉冲）
    Knockout,       // 击晕（脉冲）
}

// ── AgentBrain 新增字段 ──

/// <summary>我对玩家的分类警戒值明细</summary>
private Dictionary<PlayerActionType, float> _alertBreakdown = new Dictionary<PlayerActionType, float>();

/// <summary>缓存总值，加/减/衰减时同步更新，避免每帧 Sum() 分配枚举器</summary>
private float _alertTotal;

/// <summary>上一帧的警戒阶段（用于检测穿越）</summary>
private AlarmPhase _lastAlertPhase = AlarmPhase.Normal;

// ── 公开查询（AgentHudMissionView 每帧读）──

public float AlertValue => _alertTotal;
public AlarmPhase AlertPhase => _alertTotal switch
{
    >= 2.0f => AlarmPhase.Alarmed,
    >= 1.0f => AlarmPhase.Cautious,
    >= 0.25f => AlarmPhase.Suspicious,
    _ => AlarmPhase.Normal
};
public PlayerActionType? PrimaryAction =>
    _alertBreakdown.Count == 0 ? null
    : _alertBreakdown.OrderByDescending(kv => kv.Value).First().Key;
```

### 2.3 持续累加 — AgentBrain.Tick 自己判断

```csharp
// 在 AgentBrain.Tick 中：
void Tick(float dt)
{
    if (Owner == Agent.Main) return;
    // ... 现有逻辑 ...

    UpdateAlertCognition(dt);  // 🆕
}

void UpdateAlertCognition(float dt)
{
    if (!NpcSightSystem.CanNpcSeePlayer(Owner))
    {
        DecayAlertBreakdown(dt);
        return;
    }

    // 我能看到玩家——他在干什么？
    if (Agent.Main.CrouchMode)
        AddAlert(PlayerActionType.Crouching, dt * 0.15f);

    if (IsPlayerWeaponDrawn())
        AddAlert(PlayerActionType.WeaponDrawn, dt * 0.20f);

    if (StealManager.IsUIOpen)
        AddAlert(PlayerActionType.StealUIOpen, dt * 0.30f);

    if (IsPlayerTrespassing())
        AddAlert(PlayerActionType.Trespassing, dt * 0.25f);

    // 阶段穿越检测
    var newPhase = AlertPhase;
    if (newPhase != _lastAlertPhase)
    {
        OnAlertPhaseChanged(_lastAlertPhase, newPhase);
        _lastAlertPhase = newPhase;
    }
}

void AddAlert(PlayerActionType type, float amount)
{
    _alertBreakdown.TryGetValue(type, out float cur);
    _alertBreakdown[type] = cur + amount;
    _alertTotal += amount;
}
```

### 2.3 持续累加 + 辅助检测

```csharp
// 武器出鞘检测：骑砍原生 API
bool IsPlayerWeaponDrawn()
{
    var main = Agent.Main;
    if (main == null) return false;
    // 检查主手或副手是否有武器在手中（非收鞘状态）
    return main.WieldedWeaponIndex != EquipmentIndex.None
        || main.GetWieldedWeaponInfo(Agent.HandIndex.MainHand).IsValid();
}

// 闯入禁地检测：利用场景中的 AreaTrigger 或距离判定
// 实现方式：在 Mission 初始化时缓存守卫/禁区的 AreaTrigger 列表，
// 每帧检查 Agent.Main 是否在任意一个 trigger 范围内。
// 不需要精确到每个 NPC——只要玩家在禁区里，所有能看到他的 NPC 都会累加 Trespassing 警戒。
bool IsPlayerTrespassing()
{
    return TrespassZoneManager.IsPlayerInRestrictedZone();
}
```

### 2.4 脉冲 — 外部发事件 → AgentBrain.ReceiveEvent

脉冲不直接调 `AddAlert`。外部系统找到目击者，给每个目击者的 AgentBrain 发事件：

```csharp
// ── 调用方（StealManager / AttackTriggerMissionLogic）──
var observers = NpcSightSystem.GetObserversOf(Agent.Main);
foreach (var observer in observers)
{
    AgentAIController.Instance.SendEventToAgent(observer, "PlayerStole",
        targetName, itemName);  // 附加信息供台词拼接
}

// ── AgentBrain.ReceiveEvent ──
if (aiEvent.EventType == "PlayerStole")
{
    string targetName = aiEvent.Args[0] as string;
    string itemName = aiEvent.Args[1] as string;
    _pulseContext[PlayerActionType.Steal] = (targetName, itemName);  // 存附加信息
    AddAlert(PlayerActionType.Steal, 2.0f);
}
else if (aiEvent.EventType == "PlayerAttackedAlly")
{
    string targetName = aiEvent.Args[0] as string;
    _pulseContext[PlayerActionType.AttackAlly] = (targetName, null);
    AddAlert(PlayerActionType.AttackAlly, 2.0f);
}
else if (aiEvent.EventType == "PlayerKnockout")
{
    string targetName = aiEvent.Args[0] as string;
    _pulseContext[PlayerActionType.Knockout] = (targetName, null);
    AddAlert(PlayerActionType.Knockout, 2.0f);
}
```

### 2.5 衰减 — 看不到玩家时按比例降

```csharp
void DecayAlertBreakdown(float dt)
{
    if (_alertBreakdown.Count == 0) return;

    float totalDecay = dt * 0.15f;
    if (_alertTotal <= 0.0001f) { _alertBreakdown.Clear(); _alertTotal = 0f; return; }

    var keys = _alertBreakdown.Keys.ToList();
    foreach (var key in keys)
    {
        float proportion = _alertBreakdown[key] / _alertTotal;
        float decayed = totalDecay * proportion;
        _alertBreakdown[key] -= decayed;
        _alertTotal -= decayed;
        if (_alertBreakdown[key] <= 0.0001f)
        {
            _alertBreakdown.Remove(key);
            _pulseContext.Remove(key);
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

## 三、阶段穿越 → AgentBrain 自己触发行为

阶段穿越检测在 `AgentBrain.UpdateAlertCognition` 内部完成（见 2.3），不需要 NpcSightSystem 参与。

```csharp
// AgentBrain 内部：
void OnAlertPhaseChanged(AlarmPhase oldPhase, AlarmPhase newPhase)
{
    switch (newPhase)
    {
        case AlarmPhase.Suspicious:
            // L1: 仅 BubbleSay，不发送 AgentBrain 事件
            var primary = PrimaryAction;
            string text = ResolveBubbleSay(primary, AlarmPhase.Suspicious);
            if (!string.IsNullOrEmpty(text))
                AgentHudMissionView.AgentSay(Owner, text);
            break;

        case AlarmPhase.Cautious:
            // L2: 插入 LookAt + BubbleSay
            if (_currentAction == null || _currentAction is StayAction)
                EnqueueAction(new LookAtAction(Agent.Main, 2.0f));
            string text2 = ResolveBubbleSay(PrimaryAction, AlarmPhase.Cautious);
            if (!string.IsNullOrEmpty(text2))
                AgentHudMissionView.AgentSay(Owner, text2);
            break;

        case AlarmPhase.Alarmed:
            // L3: 脉冲抑制检查
            if (_pulseSuppressedUntil > 0 && Mission.Current?.CurrentTime < _pulseSuppressedUntil)
                return;
            StartL3Confrontation();
            break;

        case AlarmPhase.Normal:
            ClearAllActions();
            AgentControlHelper.ResumeVanillaAI(Owner);
            break;
    }
}
```

**脉冲抑制**：脉冲事件（`ReceiveEvent("PlayerStole")` 等）在加 2.0 的同时设 `_pulseSuppressedUntil = now + 3.0f`。偷窃脉冲瞬间到 2.0 → `WitnessCrime_GatherOnLook` 已在处理围观流程 → 3 秒内不触发独立的 L3 单人上前。

---

## 四、脉冲事件接收 + L3 质问实现

### 4.1 脉冲事件 → ReceiveEvent

外部系统找到目击者后发事件，AgentBrain 收事件 → 加脉冲到自己的 `_alertBreakdown`：

```csharp
// HandleLegacyAtomicAction 新增：

if (aiEvent.EventType == "PlayerStole")
{
    string targetName = aiEvent.Args[0] as string;
    string itemName = aiEvent.Args[1] as string;
    _pulseContext[PlayerActionType.Steal] = (targetName, itemName);
    AddAlert(PlayerActionType.Steal, 2.0f);
    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
}
else if (aiEvent.EventType == "PlayerAttackedAlly")
{
    string targetName = aiEvent.Args[0] as string;
    _pulseContext[PlayerActionType.AttackAlly] = (targetName, null);
    AddAlert(PlayerActionType.AttackAlly, 2.0f);
    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
}
else if (aiEvent.EventType == "PlayerKnockout")
{
    string targetName = aiEvent.Args[0] as string;
    _pulseContext[PlayerActionType.Knockout] = (targetName, null);
    AddAlert(PlayerActionType.Knockout, 2.0f);
    _pulseSuppressedUntil = (Mission.Current?.CurrentTime ?? 0f) + 3.0f;
}
```

### 4.2 L3 质问 — OnAlertPhaseChanged 直接调用

L3 到达时不需要走事件系统——`UpdateAlertCognition` 检测到阶段变化直接调 `StartL3Confrontation`：

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
        EnqueueAction(new MoveToPlayerAction(player, 1.5f));
        EnqueueAction(new LookAtAction(player, 0.5f));
        EnqueueAction(new AlertForceConversationAction());
        EnqueueAction(new StayAction(player));
    }
    else
    {
        // 路径 A：StoryDialogVM（默认）
        var conflict = BuildAlarmConflict(PrimaryAction);
        EnqueueAction(new MoveToPlayerAction(player, 1.5f));
        EnqueueAction(new LookAtAction(player, 0.5f));
        EnqueueAction(new PrepareOpeningAction(InitiativeType.CrimeAccusation, conflict));
        EnqueueAction(new ForceTalkAction());
        EnqueueAction(new StayAction(player));
    }
}
```

---

## 五、BubbleSay 台词映射

`NpcSightSystem.GetPrimaryAction(npc)` 决定说哪类台词。**台词由 NPC 说，不是玩家说。**

| PrimaryAction | L1 BubbleSay | L2 BubbleSay |
|---------------|-------------|-------------|
| `Crouching` | "（嘀咕）{PlayerName}鬼鬼祟祟干嘛……" | "（提高声音）喂！{PlayerName}！蹲着干什么！" |
| `WeaponDrawn` | "（不安地看了一眼）怎么还拔刀了……" | "（后退半步）{PlayerName}！把刀收起来！" |
| `StealUIOpen` | "（瞟了一眼）在翻什么呢……" | "（盯着）喂！你在翻什么！" |
| `Trespassing` | "（警惕）这人来这干什么……" | "喂！这里不许外人进来！出去！" |
| `Steal` | "咦，{TargetName}的{ItemName}呢？" | "（惊呼）{PlayerName}！你偷了{ItemName}！" |
| `AttackAlly` | "（惊）怎么回事？！" | "（后退）{PlayerName}打人了！" |
| `Knockout` | "（惊恐）出人命了……" | "来人！{PlayerName}把{TargetName}打倒了！" |

`{TargetName}` / `{ItemName}` 从脉冲调用方传入，NpcSightSystem 在 `_alertBreakdown` 旁维护一个等 key 的附加信息字典。

---

## 六、L3 质问对话 — 双路径开关

`Settings.Instance.AlertDialogueMode` 控制：

### 路径 A：StoryDialogVM（默认）
```
MoveToPlayerAction → PrepareOpeningAction → ForceTalkAction
  ├─ IsLLMReady → StartFreeConversationFlow（自由对话）
  └─ !IsLLMReady → ShowVanillaConfrontation（InquiryData 弹窗兜底）
```

### 路径 B：VanillaConversation（新增）
```
MoveToPlayerAction → AlertForceConversationAction
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
    /// 驱逐 — 玩家在禁地（守卫区/贵族私宅），不应该在这里。
    /// 要求：立刻离开该区域。不接受解释——不管你什么理由，先出去再说。
    /// </summary>
    Expel,

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
    PlayerActionType.Trespassing => NpcInterceptIntent.Expel,
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

#### Expel（驱逐）— 触发：闯入禁地

玩家站在不该出现的地方——守卫哨站、贵族内宅、军营内部。NPC 不管你来干什么，先让你出去再说。

| 要素 | 内容 |
|------|------|
| NPC 开场 | "站住！这里不许外人进来。出去。" |
| NPC 要求 | **立刻离开该区域** |
| 玩家配合 | "好，我这就出去。" → NPC 盯着你直到离开禁区范围，警戒值开始衰减 |
| 玩家解释 | "我迷路了。" / "我在找人。" → Charm 检定（DC 40）。成功→NPC："……找完赶紧走。"；失败→NPC："不管找谁，先出去！" |
| 玩家挑衅 | "你管得着吗？" → NPC："来人！有外人闯进来了！" → 周围守卫警戒值+1.0，意图升级为 Stop 级别对待 |
| NPC **不会**做 | 直接动手（除非挑衅后升级）、搜身（没怀疑你偷东西） |

#### Search（搜查）— 触发：翻包（StealUIOpen）

玩家反复翻包/打开偷窃界面，NPC 怀疑偷了东西但**没亲眼看见**。NPC 的核心诉求是验证——"让我看看你有没有偷东西"。

| 要素 | 内容 |
|------|------|
| NPC 开场 | "{PlayerName}在翻什么？把手拿开，让{SpeakerSelfRef}看看你的包。" |
| NPC 要求 | **打开背包接受检查** |
| 玩家接受搜查 | 系统检查背包中是否有赃物： |
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

```csharp
public static DialogueInjectScript BuildAlertInterceptScript(
    Hero speaker, NpcInterceptIntent npcIntent, PlayerActionType primaryAction)
{
    var r = new PlaceholderResolver(speaker, Hero.MainHero);
    var turns = new List<DialogueInjectTurn>();

    // NPC 开场白 — 由意图 + 具体行为决定
    string npcOpening = npcIntent switch
    {
        NpcInterceptIntent.Deter => primaryAction switch
        {
            PlayerActionType.WeaponDrawn =>
                r.Resolve("（{SpeakerEmotion}地）把刀收起来！{SpeakerPlayerAddr}！这是村子，不是战场！"),
            _ => // Crouching
                r.Resolve("（{SpeakerEmotion}地）喂！{SpeakerPlayerAddr}！蹲在那鬼鬼祟祟干什么？"),
        },

        NpcInterceptIntent.Expel =>
            r.Resolve("（{SpeakerEmotion}地）站住！这里不许外人进来。出去。"),

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

    turns.Add(new DialogueInjectTurn
    {
        Id = "start", SpeakerIndex = 0, NpcLine = npcOpening,
        Options = BuildOptionsByIntent(r, npcIntent, primaryAction)
    });

    // Search 成功后如果搜到赃物 → 插入一个额外 turn 把意图切换为 Recover
    if (npcIntent == NpcInterceptIntent.Search)
        turns.Add(BuildSearchResultTurn(r));

    // continue_chat
    turns.Add(new DialogueInjectTurn
    {
        Id = "continue_chat", SpeakerIndex = 0,
        NpcLine = "还有什么想说的？",
        Options = new List<DialogueInjectOption>
        {
            new() { PlayerLine = "我走了。", Action = "INTENT:WalkAway", NextTurn = "" }
        }
    });

    return new DialogueInjectScript { EntryTurn = "start", Turns = turns };
}

static List<DialogueInjectOption> BuildOptionsByIntent(
    PlaceholderResolver r, NpcInterceptIntent intent, PlayerActionType action)
{
    var opts = new List<DialogueInjectOption>();

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
            opts.Add(new() { PlayerLine = complyLine, NpcResponse = r.Resolve(complyResp), Action = "NONE", NextTurn = "" });
            opts.Add(new() { PlayerLine = "关你什么事？（挑衅）", NpcResponseOnSuccess = r.Resolve("……算了。"), NpcResponseOnFail = r.Resolve("来人！这有个闹事的！"), Action = "INTENT:Threat", NextTurn = "continue_chat" });
            opts.Add(new() { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextTurn = "" });
            break;

        case NpcInterceptIntent.Expel:
            opts.Add(new() { PlayerLine = "好，我这就出去。", NpcResponse = r.Resolve("{SpeakerSelfRef}看着你呢。赶紧走。"), Action = "NONE", NextTurn = "" });
            opts.Add(new() { PlayerLine = "我迷路了 / 在找人。", NpcResponseOnSuccess = r.Resolve("……找完赶紧走。"), NpcResponseOnFail = r.Resolve("不管找谁，先出去！"), Action = "INTENT:CharmDefense", NextTurn = "" });
            opts.Add(new() { PlayerLine = "你管得着吗？（挑衅）", NpcResponse = r.Resolve("来人！有外人闯进来了！"), Action = "INTENT:Threat", NextTurn = "continue_chat" });
            break;

        case NpcInterceptIntent.Search:
            opts.Add(new() { PlayerLine = "……行，你看吧。", Action = "INTENT:SubmitToSearch", NextTurn = "search_result" });
            opts.Add(new() { PlayerLine = "凭什么翻我东西？（拒绝）", NpcResponse = r.Resolve("不敢让人看？那就是有鬼了！"), Action = "INTENT:RefuseSearch", NextTurn = "recover_confront" });
            opts.Add(new() { PlayerLine = "别查了，我赔你点钱。", NpcResponse = r.Resolve("……做贼心虚。拿了钱滚。"), Action = "INTENT:PayRestitution", NextTurn = "" });
            opts.Add(new() { PlayerLine = "（转身就走）", NpcResponse = r.Resolve("站住！"), Action = "INTENT:WalkAway", NextTurn = "" });
            break;

        case NpcInterceptIntent.Recover:
            opts.Add(new() { PlayerLine = r.Resolve("好，还给你。（{RestitutionCost} 第纳尔）"), NpcResponse = r.Resolve("算你识相。别再来了。"), Action = "INTENT:PayRestitution", NextTurn = "" });
            opts.Add(new() { PlayerLine = r.Resolve("你哪只眼睛看见的？"), NpcResponseOnSuccess = r.Resolve("……{SpeakerSelfRef}可能看错了。"), NpcResponseOnFail = r.Resolve("{SpeakerSelfRef}两只眼睛都看见了！"), Action = "INTENT:CharmDefense", NextTurn = "continue_chat" });
            opts.Add(new() { PlayerLine = "（推开就跑）", Action = "INTENT:WalkAway", NextTurn = "" });
            break;

        case NpcInterceptIntent.Stop:
            opts.Add(new() { PlayerLine = r.Resolve("我愿意赔钱。（{RestitutionCost} 第纳尔）"), NpcResponse = r.Resolve("光赔钱就完了？拿了钱快滚。"), Action = "INTENT:PayRestitution", NextTurn = "" });
            opts.Add(new() { PlayerLine = "他先惹我的。", NpcResponseOnSuccess = r.Resolve("……下次再动手没这么好说话。"), NpcResponseOnFail = r.Resolve("在{SpeakerSelfRef}眼皮底下动手，就得有个说法！"), Action = "INTENT:CharmDefense", NextTurn = "continue_chat" });
            opts.Add(new() { PlayerLine = "（拔剑）谁敢拦我！", NpcResponse = r.Resolve("{SpeakerPlayerAddr}疯了！快叫人！"), Action = "INTENT:FightVillagers", NextTurn = "" });
            break;
    }

    return opts;
}

/// <summary>搜查结果 turn：接受搜查后，系统检查玩家背包是否有赃物</summary>
static DialogueInjectTurn BuildSearchResultTurn(PlaceholderResolver r)
{
    // 赃物判定由 IntentContext 中的 ActiveEvent / TheftLedger 驱动
    // 有赃物 → NPC 发现证据 → 意图升级为 Recover
    // 无赃物 → NPC 道歉，警戒值清空
    return new DialogueInjectTurn
    {
        Id = "search_result",
        SpeakerIndex = 0,
        NpcLine = null, // LazyNpcResponse 动态求值：搜到→"这是什么？！" / 没搜到→"……行吧"
        Options = new List<DialogueInjectOption>
        {
            // 搜到赃物时可见
            new() { PlayerLine = "……（无言以对）", Action = "INTENT:Confess", NextTurn = "continue_chat" },
            new() { PlayerLine = "那是我的东西！", NpcResponse = r.Resolve("你的？上面还写着{TargetName}的名字呢！"), Action = "NONE", NextTurn = "continue_chat" },
            // 没搜到时可见
            new() { PlayerLine = "我说了没拿吧。", Action = "NONE", NextTurn = "" },
        }
    };
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
| **1 数据层** | `AgentBrain` 新增 `_alertBreakdown` + `_pulseContext`；`AddAlert` / `AlertValue` / `PrimaryAction` / `AlertPhase`；每帧 Tick 累加 + 按比例衰减 |
| **2 L1+L2** | `UpdateAlertCognition` 持续累加 + 阶段穿越检测 → `OnAlertPhaseChanged`；L1/L2 的 BubbleSay + LookAt；BubbleSay 冷却 |
| **3 脉冲事件** | `ReceiveEvent` 新增 `PlayerStole` / `PlayerAttackedAlly` / `PlayerKnockout`；调用方通过 `GetObserversOf` + `SendEventToAgent` 广播 |
| **4 L3 路径 A** | `StartL3Confrontation` → `PrepareOpeningAction` → `ForceTalkAction`；`MoveToPlayerAction`；脉冲抑制标记 |
| **5 L3 路径 B** | `Settings.AlertDialogueMode`；`BuildAlertInterceptScript`；`DialogueInjector.InjectScriptAsNpcInitiative`；`AlertForceConversationAction`；反编译 `StartConversation` API |
| **6 打磨** | BubbleSay 频率调优；脉冲抑制验证；`PlaceholderResolver` 扩充 |

---

## 九、与现有系统的关系

| 系统 | 关系 | 说明 |
|------|------|------|
| **NpcSightSystem** | 🔵 **纯感知工具** | 只提供 `CanNpcSeePlayer` / `GetObserversOf`，不维护警戒值状态 |
| **AgentBrain** | 🔴 **认知所有者** | `_alertBreakdown` 实例字段；Tick 中自行判断 + 累加 + 衰减 + 阶段穿越 |
| **AgentAIController** | 🔵 **脉冲广播** | `SendEventToAgent` 投递 `PlayerStole` 等脉冲事件到目击者的 AgentBrain |
| **AgentHudMissionView** | 🔵 **读值渲染** | 从 `brain.AlertValue` / `brain.PrimaryAction` 读警戒值 + 颜色，不变 |
| **Settings.Instance** | 🆕 **新开关** | `AlertDialogueMode` 控制 L3 对话走 StoryDialogVM 还是 ConversationManager |
| **CrimeDialogueBuilder** | 🆕 **新方法** | `BuildAlertInterceptScript(primaryAction)` |
| **DialogueInjector** | 🆕 **新重载** | `InjectScriptAsNpcInitiative` — NPC 主动开场，不走 `hero_main_options` |
| **PrepareOpeningAction / ForceTalkAction** | 🔵 **路径 A 复用** | StoryDialogVM 默认路径，已有轮子 |
| **WitnessCrime_GatherOnLook** | 🔶 **脉冲抑制互斥** | 脉冲事件设 `_pulseSuppressedUntil`，3 秒内 L3 不重复触发 |

---

## 十、警戒值公式（总结）

```
// AgentBrain.Tick → UpdateAlertCognition
// 能看到玩家：
_alertBreakdown[Crouching]   += dt * 0.15f   (if crouching)
_alertBreakdown[WeaponDrawn] += dt * 0.20f   (if weapon drawn in civilian area)
_alertBreakdown[StealUIOpen] += dt * 0.30f   (if steal UI open)
_alertBreakdown[Trespassing] += dt * 0.25f   (if in restricted zone)

// 脉冲事件 → ReceiveEvent：
_alertBreakdown[Steal]      += 2.0f
_alertBreakdown[AttackAlly] += 2.0f
_alertBreakdown[Knockout]   += 2.0f

// 看不到玩家 → DecayAlertBreakdown：
每个条目 -= dt * 0.15 * (该条目值 / 总值)

// 派生：
AlertValue    = _alertBreakdown.Values.Sum()
PrimaryAction = _alertBreakdown.OrderByDescending(kv => kv.Value).First().Key
AlertPhase    = Sum switch { >=2.0→Alarmed, >=1.0→Cautious, >=0.25→Suspicious, _→Normal }
```

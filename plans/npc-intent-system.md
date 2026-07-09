# NpcIntent — AgentBrain 通用 NPC 意图系统

## Context

目前 AgentBrain 的状态散落在多个独立字段中（`IsStunned`、`_isGuardMode` 等），没有统一的"NPC 当前想干什么"的概念。玩家交互系统 `IntentBase`（`InteractionOptionType` 枚举 + `IntentBase` 子类）是对话层级的——"玩家能对 NPC 做什么"——缺少 NPC 自身的高层意图表达。

**需求**：
1. 创建通用的 `NpcIntent`（NPC 自身内在状态），与 `IntentBase`（玩家交互选项）对应但不同，**互斥状态机**
2. AgentBrain 收到事件 → 设置意图 → 意图影响行为 + 外部查询
3. **战斗中交互**：玩家与目标 NPC 交战时，只显示 **认输(F)**；NPC 意图为 `Surrendering` 时额外显示 **接受认输(G)**
4. **NPC 残血决策认输**：FightEnemyAction 检测血量 < 30% → 冒泡 + 发事件 → Brain.CurrentIntent = Surrendering
5. **玩家认输对话注入**：按 F → `INTENT:PlayerSurrender`（扣钱 + 发事件结束战斗）
6. **接受认输对话注入**：按 G → `INTENT:ResolveNpcSurrender`（4 种 ActionParam 模式：接受/侮辱/索钱/拒绝）
7. **零 NONE**：所有对话选项通过 `INTENT:xxx` 委托到 IntentBase 子类
8. **AgentHud 调试**：显示 NPC 的 `CurrentIntent`

---

## 1. NpcIntentType 状态机（互斥）

```
None ──→ Fighting ──→ Surrendering ──→ None
  │         │              │
  │         │              ├──(accept/humiliate/ransom)──→ None
  │         │              └──(refuse)──→ Fighting
  │         └──(战斗结束/Tick)──→ None
  ├──→ Following ──→ None
  ├──→ Confronting ──→ None     （携带 ConfrontationType: Deter/Search/Recover/Stop）
  ├──→ Interacting ──→ None
  └──→ KnockedOut ──→ None
```

**核心理念**：`Fighting → Surrendering` 是替换关系——NPC 仍在战斗中，但**意图**已从"我要打赢"变为"我想投降"。

---

## 2. 文件改动总览

| 文件 | 改动 |
|------|------|
| `AI/NpcIntent.cs` | **新建** — NpcIntent 类 + NpcIntentType 枚举 |
| `AI/AlertTypes.cs` | `NpcInterceptIntent` 重命名为 `ConfrontationType`（值不变） |
| `AI/AgentBrain.cs` | CurrentIntent 属性 + SetNpcIntent + ReceiveEvent 各分支设置 + 4 新事件处理 |
| `AI/Actions/AtomicAction.cs` | FightEnemyAction 残血认输检测 + `_surrenderTriggered`；AlertForceConversationAction 用 SetNpcIntent |
| `Interaction/Dialogue/CrimeDialogueBuilder.cs` | `BuildAlertInterceptScript` 参数类型 NpcInterceptIntent → ConfrontationType |
| `Interaction/Intents/CombatSurrenderIntents.cs` | **新建** — PlayerSurrenderPayIntent + PlayerSurrenderBegIntent + PlayerSurrenderThreatenIntent + ResolveNpcSurrenderIntent |
| `Interaction/Intents/IntentRegistry.cs` | 注册 4 个新 Intent |
| `Interaction/InteractionMissionView.cs` | intentChanged 刷新 + 战斗选项逻辑 + 2 新方法 |
| `AgentHUD/AgentHudVM.cs` | NpcIntentDebugText 属性 |
| `ExampleMod.csproj` | 添加 2 个 .cs 编译 |

---

## 3. 新建 `AI/NpcIntent.cs`

```csharp
namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 的当前高层意图。
    /// 与 IntentBase（玩家交互选项）对应但不同：
    ///   IntentBase = 玩家能对 NPC 做什么（对话菜单选项）
    ///   NpcIntent  = NPC 自己此刻的内在状态 / 想干什么
    /// 互斥状态机，由 AgentBrain.SetNpcIntent 设置。
    /// </summary>
    public class NpcIntent
    {
        public NpcIntentType Type { get; }
        public Agent Target { get; }  // 意图针对的目标（可为 null）

        /// <summary>
        /// 质问子类型（仅在 Type == Confronting 时有值）。
        /// 复用自原 NpcInterceptIntent，融合后改名为 ConfrontationType。
        /// </summary>
        public ConfrontationType? InterceptDetail { get; }

        public NpcIntent(NpcIntentType type, Agent target = null, ConfrontationType? interceptDetail = null)
        {
            Type = type;
            Target = target;
            InterceptDetail = interceptDetail;
        }

        public override string ToString() => InterceptDetail != null
            ? $"{Type}({Target?.Name}, {InterceptDetail})"
            : Target != null
                ? $"{Type}({Target.Name})"
                : Type.ToString();
    }

    /// <summary>
    /// NPC 高层意图类型。所有值互斥。
    /// </summary>
    public enum NpcIntentType
    {
        None,           // 无特定意图（默认/空闲）
        Fighting,       // 战斗中（正在与某人交战）
        Surrendering,   // 想要认输（仍处于战斗中，但意图已转变）
        Confronting,    // 质问/对峙玩家（L3 警戒触发）。携带 ConfrontationType detail。
                        //   与 Interacting 的核心区分：alertBreakdown 或 WorldEvent 有无该玩家的罪行记录。
                        //   Confronting = alertBreakdown 中有 PlayerActionType（偷窃/击晕/攻击等）或有活跃 WorldEvent
                        //   Interacting = alertBreakdown 为空，无待处理罪行，纯中性对话
                        //   互斥：alertBreakdown 清空时 Confronting → None；新罪行产生时 Interacting → Confronting
        Following,      // 跟随某人（护卫/命令跟随）
        Interacting,    // 正在与玩家交互/对话中
        KnockedOut,     // 被击晕（StayAction 占位）
    }
}
```

### 3a. 融合 `NpcInterceptIntent` → `ConfrontationType`

现有 `AlertTypes.cs` 中的 `NpcInterceptIntent` 枚举重命名为 `ConfrontationType`，值保持不变：

```csharp
/// <summary>质问子类型 — 当 NpcIntent.Type == Confronting 时，决定 NPC 开场白和玩家选项。</summary>
public enum ConfrontationType
{
    Deter,    // 威慑 — 可疑但非犯罪（蹲下/拔刀）
    Search,   // 搜查 — 怀疑偷窃但没目击
    Recover,  // 追回 — 目击偷窃
    Stop,     // 制止 — 目击暴力（攻击/击晕）
}
```

**显示时机**：四个值全部对应 `NpcIntentType.Confronting`。`ConfrontationType` 是 detail——UI 层只看 `Type == Confronting` 决定显示质问菜单，`CrimeDialogueBuilder` 读 `InterceptDetail` 决定具体台词和选项。

**迁移影响**：
- `CrimeDialogueBuilder.BuildAlertInterceptScript(speaker, npcIntent, ...)` → 参数从 `NpcInterceptIntent` 改为 `ConfrontationType`，未来可进一步改为直接从 `brain.CurrentIntent.InterceptDetail` 读取
- `AlertForceConversationAction` → `SetNpcIntent(Confronting, Agent.Main, interceptDetail: Deter/Search/Recover/Stop)`
- `AtomicAction.cs:978` 的 switch 表达式类型从 `NpcInterceptIntent` 改为 `ConfrontationType`

---

## 4. 修改 `AI/AgentBrain.cs`

### 4a. 新增属性（`IsStunned` 附近，约 line 32）

```csharp
private NpcIntent _currentIntent = new NpcIntent(NpcIntentType.None);
private NpcIntent _previousIntent;

/// <summary>NPC 当前高层意图。只读，变更必须走 SetNpcIntent。</summary>
public NpcIntent CurrentIntent => _currentIntent;

/// <summary>上一个意图。只读，用于回退（如 refuse 后回到 Fighting）或调试。</summary>
public NpcIntent PreviousIntent => _previousIntent;

/// <summary>
/// 设置 NPC 当前意图，同时记录上一个意图。
/// 所有意图变更必须走此方法，类内部也不允许直接写 _currentIntent。
/// </summary>
public void SetNpcIntent(NpcIntentType type, Agent target = null, ConfrontationType? interceptDetail = null)
{
    _previousIntent = _currentIntent;
    _currentIntent = new NpcIntent(type, target, interceptDetail);
}
```

### 4b. ReceiveEvent 新增事件处理

```csharp
// ── 战斗投降相关新事件 ──

if (aiEvent.EventType == "event_npc_surrender")
{
    // NPC 自己决定认输（残血触发）
    SetNpcIntent(NpcIntentType.Surrendering, Agent.Main);
}

if (aiEvent.EventType == "event_player_surrendered")
{
    // 玩家主动认输 → 战斗结束
    SetNpcIntent(NpcIntentType.None);
    ClearAllActions();  // FightEnemyAction.OnEnd → UnregisterCombatant
}

if (aiEvent.EventType == "event_surrender_accepted")
{
    // 玩家接受 NPC 认输 → 战斗结束
    SetNpcIntent(NpcIntentType.None);
    ClearAllActions();
}

if (aiEvent.EventType == "event_surrender_refused")
{
    // 玩家拒绝 NPC 认输 → 回到战斗
    SetNpcIntent(NpcIntentType.Fighting, Agent.Main);
    // 不 ClearAllActions，FightEnemyAction 继续运行
}
```

### 4c. ReceiveEvent 全分支 → NpcIntent 映射

| 事件 | 当前行为 | SetNpcIntent | 备注 |
|------|---------|-------------|------|
| `ComeHere` | 被玩家叫过来 | `SetNpcIntent(Interacting, Agent.Main)` | NPC 进入交互状态 |
| `order_follow` | 跟随命令 | `SetNpcIntent(Following, leader)` | leader = Args[0] |
| `order_attack` | 攻击命令 | `SetNpcIntent(Fighting, target)` | |
| `DeferredCombat` | 延迟战斗 | `SetNpcIntent(Fighting, target)` | WorldEvent→Confrontation |
| `ReEngageConfrontation` | 重新追上质问 | `SetNpcIntent(Confronting, Agent.Main, detail)` | ☆ 新加，detail 由 PrimaryAction 确定 |
| `event_agent_damaged` (shouldHelp) | 护主/同族参战 | `SetNpcIntent(Fighting, attacker)` | only when shouldHelp==true |
| `EndInteraction` | 交互结束 | `SetNpcIntent(None)` | ClearAllActions + ResumeVanillaAI |
| `WitnessCrime_GatherOnLook` | 目击犯罪围观 | `SetNpcIntent(Confronting, Agent.Main, detail)` | ☆ 新加。NPC 已决定干预（走过去盯着看），detail 当场由犯罪类型确定（Knockout→Stop, Steal→Recover）。后续 BecomeAlarmed 可能再次触发但 detail 不变 |
| `WitnessCrime_StayStare` | 原地吃瓜 | **不设** | 没挤进围观位，反应强度不足以构成干预意图 |
| `event_agent_knocked_out` | 被击晕 | `SetNpcIntent(KnockedOut)` | ☆ 新加 |
| `StartObservingPlayer` | 概率冒泡问候 | **不设** | 只是 BubbleSay，意图不变 |
| `BecomeSuspicious` | 警戒→可疑 | **不设** | 只有冒泡 |
| `BecomeCautious` | 警戒→谨慎 | **不设** | LookAt + 冒泡 |
| `BecomeAlarmed` | 警戒→警告 | `SetNpcIntent(Confronting, Agent.Main, detail)` | ☆ 新加，在 `StartL3Confrontation()` 中设置。detail 由 PrimaryAction switch 确定 |
| `CalmDown` (from≥Alarmed 或 to Normal) | 降级/恢复 | `SetNpcIntent(None)` | ☆ 新加 |

### 4c-2. ConfrontationType detail 确定逻辑

在 `StartL3Confrontation()` 和 `ReEngageConfrontation` 中，根据 `PrimaryAction` 确定 detail：

```csharp
var detail = PrimaryAction switch
{
    PlayerActionType.Crouching or PlayerActionType.WeaponDrawn => ConfrontationType.Deter,
    PlayerActionType.StealUIOpen => ConfrontationType.Search,
    PlayerActionType.Steal => ConfrontationType.Recover,
    PlayerActionType.AttackAlly or PlayerActionType.Knockout => ConfrontationType.Stop,
    _ => ConfrontationType.Deter
};
SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: detail);
```

### 4d. Tick 中清除战斗意图

在约 line 1037-1042（FightEnemyAction 结束且无排队时），若 `CurrentIntent.Type == Fighting` → `SetNpcIntent(NpcIntentType.None)`。

---

## 5. 修改 `AI/Actions/AtomicAction.cs` — FightEnemyAction

### 新增字段
```csharp
private bool _surrenderTriggered = false;
```

### OnTick 残血认输检测

在现有终止条件之后、强制纠偏之前插入：

```csharp
// ── 残血认输：仅当目标是玩家时 ──
if (!_surrenderTriggered && _targetEnemy == Agent.Main)
{
    float healthRatio = agent.Health / agent.HealthLimit;
    if (healthRatio < 0.30f)
    {
        _surrenderTriggered = true;
        AgentAIController.Instance?.SendEventToAgent(agent, "event_npc_surrender", Agent.Main);
        AgentHudMissionView.AgentSay(agent, "我认输！别打了！");
    }
}
```

### 5b. AlertForceConversationAction — 用 SetNpcIntent 替代直接设意图

现有 `AlertForceConversationAction.OnEnd` 中通过 `brain.PrimaryAction` switch 确定 `NpcInterceptIntent` 来构建对话。改为：

```csharp
// OnEnd 中：
var detail = brain.PrimaryAction switch
{
    PlayerActionType.Crouching or PlayerActionType.WeaponDrawn => ConfrontationType.Deter,
    PlayerActionType.StealUIOpen => ConfrontationType.Search,
    PlayerActionType.Steal => ConfrontationType.Recover,
    PlayerActionType.AttackAlly or PlayerActionType.Knockout => ConfrontationType.Stop,
    _ => ConfrontationType.Deter
};
brain.SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: detail);
```

`CrimeDialogueBuilder.BuildAlertInterceptScript` 后续可直接从 `brain.CurrentIntent.InterceptDetail` 读取，不再需要单独传参。

### 5c. `NpcInterceptIntent` → `ConfrontationType` 全量迁移清单

| 文件 | 行号 | 当前写法 | 迁移后 |
|------|------|---------|--------|
| `AlertTypes.cs` | 26 | `public enum NpcInterceptIntent` | `public enum ConfrontationType`（值不变） |
| `AtomicAction.cs` | 978 | `NpcInterceptIntent npcIntent = primaryAction switch { ... }` | `var detail = primaryAction switch { ... ConfrontationType.Deter/Search/Recover/Stop }` — 然后 `brain.SetNpcIntent(Confronting, Agent.Main, detail)` |
| `AtomicAction.cs` | 993-994 | `BuildAlertInterceptScript(npcHero, npcIntent, primaryAction, worldEvt)` | `BuildAlertInterceptScript(npcHero, detail, primaryAction, worldEvt)` |
| `CrimeDialogueBuilder.cs` | 560 | `Hero speaker, NpcInterceptIntent npcIntent, ...` | `Hero speaker, ConfrontationType npcIntent, ...`（参数类型改名，内部 switch 同理） |
| `CrimeDialogueBuilder.cs` | 649 | `NpcInterceptIntent intent` | `ConfrontationType intent` |
| `CrimeDialogueBuilder.cs` | 587-689 | `NpcInterceptIntent.Deter/Search/Recover/Stop` | `ConfrontationType.Deter/Search/Recover/Stop` |

---

## 6. 新建 `Interaction/Intents/CombatSurrenderIntents.cs`

**对标 `AccountabilityIntents.cs` 设计模式**：每个 Intent 继承 `IntentBase`，声明 `Evaluate` + `OnInstant`，通过 `ActionParam` 分化模式。由 `DialogueInjector` 的 `INTENT:xxx` 委托调用。

### 6a. PlayerSurrenderPayIntent — 乖乖交钱

```csharp
/// <summary>
/// 玩家认输 → 交钱保命。无检定，必定执行。
/// 正常罚金 200G；counteroffer 后罚金翻倍 400G。
/// 后果：罚金 + 荣誉 -1 + 勇敢 -1 + 战斗结束。
/// </summary>
public class PlayerSurrenderPayIntent : IntentBase
{
    public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
    public override string DisplayName => "（交出钱袋）";
    public override NegotiationGoalType? Goal => null; // 无条件，不检定

    public override Eligibility Evaluate(IntentContext ctx)
    {
        if (Mission.Current == null) return Eligibility.Hide();

        // counteroffer 后：罚金翻倍
        bool isCounteroffer = ctx.ActionParam == "counteroffer_beg"
                           || ctx.ActionParam == "counteroffer_threaten";
        int baseCost = isCounteroffer ? 400 : 200;
        int penalty = Math.Min(Hero.MainHero.Gold, baseCost);

        if (Hero.MainHero.Gold < baseCost)
            return Eligibility.Grey($"钱不够（需要 {baseCost} 第纳尔，你只有 {Hero.MainHero.Gold}）");
        return Eligibility.Show();
    }

    public override void OnInstant(IntentContext ctx)
    {
        bool isCounteroffer = ctx.ActionParam == "counteroffer_beg"
                           || ctx.ActionParam == "counteroffer_threaten";
        int baseCost = isCounteroffer ? 400 : 200;
        int penalty = Math.Min(Hero.MainHero.Gold, baseCost);
        if (penalty > 0)
            AgentControlHelper.TransferGold(Hero.MainHero, null, penalty);

        // ② 荣誉惩罚
        int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
        Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);
        int valor = Hero.MainHero.GetTraitLevel(DefaultTraits.Valor);
        Hero.MainHero.SetTraitLevel(DefaultTraits.Valor, valor - 1);

        // ③ 战斗结束
        AgentAIController.Instance?.SendEventToAgent(
            ctx.Agent, "event_player_surrendered", Agent.Main, ctx.Agent);

        DebugLogger.Log($"[Combat] SurrenderPay: penalty={penalty}G{(isCounteroffer ? " (counteroffer x2)" : "")}, honor={honor}→{honor-1}, valor={valor}→{valor-1}");
    }
}
```

### 6b. PlayerSurrenderBegIntent — 求饶说服

```csharp
/// <summary>
/// 玩家认输 → 魅力求饶。检定通过 = 免单放人；失败 = NPC 嘲讽 + 罚金翻倍 → 弹回投降菜单。
/// OnFail 不扣任何东西——玩家还没同意接受翻倍的代价。
/// </summary>
public class PlayerSurrenderBegIntent : IntentBase
{
    public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
    public override string DisplayName => "求你放过我……";
    public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
    public override NegotiationTactic Tactic => NegotiationTactic.Flatter;
    public override float CooldownDays => 0f; // 每次战斗仅一次
    public override bool ReofferOnFail => true; // 🆕 失败后重新渲染选项

    public override Eligibility Evaluate(IntentContext ctx)
    {
        if (Mission.Current == null) return Eligibility.Hide();
        // 已经求饶失败过了 → 置灰
        if (ctx.ActionParam == "counteroffer_beg")
            return Eligibility.Grey("已经求饶过了");
        return Eligibility.Show();
    }

    public override void OnSuccess(IntentContext ctx)
    {
        // 魅力说服成功：免单放人，但荣誉仍 -1（求饶本身就不光彩）
        int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
        Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);

        AgentAIController.Instance?.SendEventToAgent(
            ctx.Agent, "event_player_surrendered", Agent.Main, ctx.Agent);

        DebugLogger.Log($"[Combat] SurrenderBeg SUCCESS: 免单放人, honor={honor}→{honor-1}");
    }

    public override void OnFail(IntentContext ctx)
    {
        base.OnFail(ctx); // 正常掉好感 + 冷却

        // ⭐ 关键：不扣钱、不扣属性、不结束战斗！
        // 只标记 counteroffer 状态，让下一轮 PayIntent 读到翻倍的罚金。
        // ReofferOnFail=true → ResolveAdversarialIntent 会调 RefreshInitialOptions()
        // → BuildOptionVMs 重新跑所有 Evaluate → PayIntent 读 ActionParam 显示 400G
        ctx.ActionParam = "counteroffer_beg";

        DebugLogger.Log($"[Combat] SurrenderBeg FAIL: counteroffer — 罚金翻倍至 400G");
    }
}
```

**设计要点**（对标 KCD2）：
- `OnFail` 是检定失败，玩家还没同意接受失败的代价 → **禁止单方面扣钱扣属性**
- `OnFail` 只做一件事：改变谈判条件（罚金 ×2），然后弹回选项菜单
- 真正的罚金延后到玩家**自愿**点"交钱"时，由 `PlayerSurrenderPayIntent.OnInstant` 扣
- NPC 台词 `DialogueTemplateHelper.Get(DialogueKey, success=false, ...)` 自动读到失败文本（"哈！现在翻倍！"），在 `ResolveAdversarialIntent` 播放，不会被跳过

### 6c. PlayerSurrenderThreatenIntent — 虚张声势

```csharp
/// <summary>
/// 玩家认输 → 破口大骂虚张声势。检定通过 = NPC 怂了放人，失败 = 继续打。
/// </summary>
public class PlayerSurrenderThreatenIntent : IntentBase
{
    public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
    public override string DisplayName => "你这条狗！……";
    public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
    public override NegotiationTactic Tactic => NegotiationTactic.Intimidate;
    public override float CooldownDays => 0f;

    /// <summary>威胁失败后延迟进入战斗的 Agent（对标 AccountabilityIntents.ThreatIntent 模式）</summary>
    internal static Agent PendingCombatAgent;

    public override Eligibility Evaluate(IntentContext ctx)
    {
        if (Mission.Current == null) return Eligibility.Hide();
        // counteroffer 阶段：已经求饶失败过了，威胁选项不可用
        if (ctx.ActionParam == "counteroffer_beg")
            return Eligibility.Grey("已经求饶过了");
        return Eligibility.Show();
    }

    public override void OnSuccess(IntentContext ctx)
    {
        // 成功：NPC 怂了，不付钱，荣誉 -1（不扣勇敢——至少还有骨气）
        int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
        Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);

        AgentAIController.Instance?.SendEventToAgent(
            ctx.Agent, "event_player_surrendered", Agent.Main, ctx.Agent);

        DebugLogger.Log($"[Combat] SurrenderThreaten SUCCESS: NPC 怂了, honor={honor}→{honor-1}");
    }

    public override void OnFail(IntentContext ctx)
    {
        // 失败：NPC 暴怒，战斗继续（对话关闭后由 Patch 消费 PendingCombatAgent）
        PendingCombatAgent = ctx.Agent;

        AgentAIController.Instance?.SendEventToAgent(
            ctx.Agent, "event_surrender_refused");

        DebugLogger.Log($"[Combat] SurrenderThreaten FAIL: NPC 暴怒，继续战斗");
    }
}
```

### 6d. ResolveNpcSurrenderIntent

```csharp
/// <summary>
/// 处决 NPC 认输请求。通过 ActionParam 区分四种模式。
/// 对标 PayRestitutionIntent 的 "alert_fine" 分化模式。
/// </summary>
public class ResolveNpcSurrenderIntent : IntentBase
{
    public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
    public override string DisplayName => "…"; // 被 DialogueInjector PlayerLine 覆盖
    public override NegotiationGoalType? Goal => null; // 即时类

    public override Eligibility Evaluate(IntentContext ctx)
    {
        if (Mission.Current != null && ctx.Agent != null)
            return Eligibility.Show();
        return Eligibility.Hide();
    }

    public override void OnInstant(IntentContext ctx)
    {
        var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
        if (brain == null) return;

        switch (ctx.ActionParam)
        {
            case "accept":
                // 宽宏大量：好感 +2
                if (ctx.Hero != null)
                    ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, 2, false, true);
                AgentAIController.Instance?.SendEventToAgent(
                    ctx.Agent, "event_surrender_accepted");
                DebugLogger.Log("[Combat] ResolveNpcSurrender: accept (+2 relation)");
                break;

            case "humiliate":
                // 侮辱：好感 -10 + 嗑头动画
                if (ctx.Hero != null)
                    ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, -10, false, true);
                AgentControlHelper.ForcePlayAction(ctx.Agent, "act_kneel");
                AgentAIController.Instance?.SendEventToAgent(
                    ctx.Agent, "event_surrender_accepted");
                DebugLogger.Log("[Combat] ResolveNpcSurrender: humiliate (-10 relation, kneel)");
                break;

            case "ransom":
                // 索钱：NPC → 玩家转账
                if (ctx.Hero != null)
                {
                    int ransom = Math.Min(ctx.Hero.Gold, 500);
                    if (ransom > 0)
                        AgentControlHelper.TransferGold(ctx.Hero, Hero.MainHero, ransom);
                }
                AgentAIController.Instance?.SendEventToAgent(
                    ctx.Agent, "event_surrender_accepted");
                DebugLogger.Log("[Combat] ResolveNpcSurrender: ransom");
                break;

            case "refuse":
                // 拒绝认输：NPC 意图回到 Fighting，继续战斗
                AgentAIController.Instance?.SendEventToAgent(
                    ctx.Agent, "event_surrender_refused");
                AgentHudMissionView.AgentSay(ctx.Agent, "不——！！");
                DebugLogger.Log("[Combat] ResolveNpcSurrender: refuse (back to Fighting)");
                break;
        }
    }
}
```

---

## 6e. IntentBase 新增：ReofferOnFail 机制

**问题**：`OnFail` 后默认展示【离开】/【继续】。但投降求饶这类场景，检定失败不应该是终点——NPC 应该"还价"，玩家应该有机会接受或拒绝。

**方案**：IntentBase 加一个虚属性，`ResolveAdversarialIntent` 据此决定 `OnFail` 后是走默认收尾还是重新渲染选项。

```csharp
// IntentBase.cs 新增：
/// <summary>
/// OnFail 后是否重新渲染初始选项（而非默认的【离开】/【继续】）。
/// 适用场景：检定失败 = NPC 还价 / 改变谈判条件 / 重新要价，而非直接惩罚。
/// 默认 false（走默认收尾）。
/// </summary>
public virtual bool ReofferOnFail => false;
```

```csharp
// InteractionController.ResolveAdversarialIntent 修改：
public void ResolveAdversarialIntent(IntentBase intent, IntentContext ctx)
{
    // ... roll ...
    if (success) intent.OnSuccess(ctx);
    else intent.OnFail(ctx);

    // NPC 台词始终播放（成败不同文本，见 DialogueTemplateHelper）
    string emotion;
    string line = DialogueTemplateHelper.Get(intent.DialogueKey, success, out emotion, ctx.Hero, ctx.Agent);
    UpdateNpcVisuals(line, emotion, "NONE", "");

    // ── 🆕 ReofferOnFail：失败后重新渲染选项 ──
    if (!success && intent.ReofferOnFail)
    {
        // OnFail 已修改 ctx.ActionParam → BuildOptionVMs 重新求值
        RefreshInitialOptions();
        return;
    }

    // ── 默认收尾：【离开】/【继续】──
    var opts = new List<StoryOptionVM>();
    opts.Add(new StoryOptionVM("【离开】 告辞", () =>
    {
        AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
        GroupStageManager.Reset(Agent.Main);
        _vm.Close();
    }));
    opts.Add(new StoryOptionVM("【继续】 再说点别的", () => RefreshInitialOptions()));
    opts.Reverse();
    _vm.ShowOptions(opts.ToArray());
}
```

**流程**：
> 玩家点"求饶" → roll 失败 → `OnFail` 设 `ctx.ActionParam = "counteroffer_beg"` → NPC 台词"哈！现在翻倍！" → `ReofferOnFail=true` → `RefreshInitialOptions()` → `BuildOptionVMs` 重新跑所有 `Evaluate` → `PayIntent` 读到 counteroffer → 显示 400G

### 6f. DialogueInjectOption 新增：NextTurnOnFail（DialogueInjector 路径用）

上述 `ReofferOnFail` 依赖 `InteractionController.RefreshInitialOptions()`（动态重求值），仅适用于 `InteractionOptionManager` 路径。**对话注入路径**（`DialogueInjector.InjectScriptAsOpening`）的 turn 是预构建的静态结构，无法运行时重求值。因此需要 `NextTurnOnFail`：

```csharp
// DialogueInjectOption 新增字段：
/// <summary>检定失败后跳转的 turn（覆盖 NextTurn）。不设则走 NextTurn（现有行为兼容）。</summary>
public string NextTurnOnFail = null;
```

```csharp
// RegisterNpcResponseLines 中，失败线用 NextTurnOnFail 目标：
string afterNpcOnFail = !string.IsNullOrEmpty(opt.NextTurnOnFail)
    ? TurnToken(fileTag, opt.NextTurnOnFail)
    : afterNpcResponse;

// 失败线
if (!string.IsNullOrEmpty(opt.NpcResponseOnFail))
{
    cm.AddDialogLineMultiAgent(
        $"inj_resp_fail_{Guid.NewGuid():N}", afterPlayer, afterNpcOnFail,  // ← 可能去不同 turn
        new TextObject(opt.NpcResponseOnFail),
        () => _intentResults.TryGetValue(capturedKey, out var r) && !r,
        null, turn.SpeakerIndex, -1, 125);
}
```

**两种机制对照**：

| | ReofferOnFail | NextTurnOnFail |
|---|---|---|
| 用在 | `InteractionOptionManager` 路径 | `DialogueInjector` 路径 |
| 原理 | `RefreshInitialOptions()` 动态重求值 | 预构建的 counteroffer turn |
| 选项 | 动态（`BuildOptionVMs` 每次重跑 Evaluate） | 静态（turn 构建时固定） |
| NPC 台词 | `DialogueTemplateHelper` 自动分化 | `NpcResponseOnFail` 手动指定 |
| 适用 | 选项需要根据状态动态变化的场景 | 对话流复杂、多 turn 跳转的场景 |

---

## 7. `IntentRegistry.cs` 注册

在 `RegisterDefaults()` 中追责意图区域附近：

```csharp
// ═══ 战斗投降 ═══
Register(new PlayerSurrenderPayIntent());
Register(new PlayerSurrenderBegIntent());
Register(new PlayerSurrenderThreatenIntent());
Register(new ResolveNpcSurrenderIntent());
```

---

## 8. 修改 `Interaction/InteractionMissionView.cs`

### 8a. 删除（不再需要 `_lastNpcIntentType` 缓存字段）

Brain 已有 `PreviousIntent`，View 层直接用 `brain.PreviousIntent?.Type` 判断变更，无需自己维护缓存。

### 8b. 修改 `PerformPerformanceHeavyLogic()`

在 alive + 正面分支中（约 line 446），用 `CurrentIntent` 判断显示内容：

```csharp
else if (isAlive)
{
    if (isBehind)
    {
        // 不变：蹲伏=偷窃，站立=击晕
        if (isCrouching) actions.Add(("偷窃", "F"));
        else actions.Add(("击晕", "F"));
    }
    else
    {
        var brain = AgentAIController.GetBrainForAgent(currentAgent);
        var intentType = brain?.CurrentIntent?.Type ?? NpcIntentType.None;

        if (intentType == NpcIntentType.Fighting || intentType == NpcIntentType.Surrendering)
        {
            actions.Add(("认输", "F"));
            if (intentType == NpcIntentType.Surrendering)
                actions.Add(("接受认输", "G"));
        }
        else
        {
            actions.Add(("对话", "F"));
            actions.Add(("闲聊", "G"));
            actions.Add(("探查", "H"));
        }
    }
}
```

状态变化检测追加：
```csharp
var brain = AgentAIController.GetBrainForAgent(currentAgent);
var currentNpcIntentType = brain?.CurrentIntent?.Type ?? NpcIntentType.None;
var prevNpcIntentType = brain?.PreviousIntent?.Type ?? NpcIntentType.None;
bool intentChanged = (currentNpcIntentType != prevNpcIntentType);
// 刷新条件：targetChanged || ... || intentChanged || !_interactVM.IsVisible
```

（缓存更新 `_lastNpcIntentType = ...` 不再需要，已删除。）

### 8c. 修改 `HandleInput()`

**F 键** — `_lastAgentWasAlive` 分支中，`_lastIsBehind` 检查之后加入战斗分支：
```csharp
else if (_lastNpcIntentType == NpcIntentType.Fighting || _lastNpcIntentType == NpcIntentType.Surrendering)
{
    PlayerSurrenderToAgent(_lastFocusedAgent);
}
```

**G 键** — 区分战斗认输与普通闲聊：
```csharp
else if (TaleWorlds.InputSystem.Input.IsKeyReleased(InputKey.G))
{
    if (_lastAgentWasAlive)
    {
        if (_lastNpcIntentType == NpcIntentType.Surrendering)
            AcceptAgentSurrender(_lastFocusedAgent);
        else if (_lastNpcIntentType != NpcIntentType.Fighting)
            _ = StartFreeConversationFlow(_lastFocusedAgent);
    }
}
```

### 8d. 新增方法：PlayerSurrenderToAgent

对标 `AlertForceConversationAction.OnStart` 模式：`InjectScriptAsOpening` + `StartConversation`。

```csharp
/// <summary>玩家向目标 NPC 认输</summary>
private void PlayerSurrenderToAgent(Agent target)
{
    if (target == null || !target.IsActive()) return;

    var script = new DialogueInjector.DialogueInjectScript
    {
        InjectAtToken = "start",
        EntryTurn = "player_lose",
        Turns = new List<DialogueInjector.DialogueInjectTurn>
        {
            // ── Turn 1: 投降菜单 ──
            new DialogueInjector.DialogueInjectTurn
            {
                Id = "player_lose",
                SpeakerIndex = 0,
                NpcLine = "（喘着粗气，收起武器）哼，知道打不过了吧？把钱袋交出来，饶你一命。",
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    // ① 乖乖交钱 — 安全，但屈辱
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "……（交出钱袋）",
                        NpcResponse = "算你识相。下次长点眼力见，滚吧！",
                        Action = "INTENT:PlayerSurrenderPay",
                        ActionParam = "pay"
                    },
                    // ② 求饶说服 — 魅力检定，成功免单，失败 → counteroffer turn
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "求你放过我，我只是路过……",
                        NpcResponseOnSuccess = "……啧，算你运气好。滚，别让我再看见你。",
                        NpcResponseOnFail = "废话少说！求饶？现在翻倍——400 第纳尔，一个子儿不能少！",
                        Action = "INTENT:PlayerSurrenderBeg",
                        ActionParam = "beg",
                        NextTurn = "",                                // 成功 → 关闭对话
                        NextTurnOnFail = "player_lose_counteroffer"   // 失败 → 翻倍还价
                    },
                    // ③ 破口大骂 — 胆魄检定，成功 NPC 怂了，失败继续打
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "你这条狗！杀了我你也别想好过！",
                        NpcResponseOnSuccess = "……疯子。滚，别让我再看见你。",
                        NpcResponseOnFail = "找死！！（暴怒地扑了上来）",
                        Action = "INTENT:PlayerSurrenderThreaten",
                        ActionParam = "threaten"
                    }
                }
            },
            // ── Turn 2: 求饶失败后的 counteroffer turn ──
            new DialogueInjector.DialogueInjectTurn
            {
                Id = "player_lose_counteroffer",
                SpeakerIndex = 0,
                NpcLine = "（冷笑）最后一次机会——400 第纳尔，或者咱们接着打。你选。",
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "……（交出 400 第纳尔）",
                        NpcResponse = "算你识相。滚吧！",
                        Action = "INTENT:PlayerSurrenderPay",
                        ActionParam = "counteroffer_beg"  // PayIntent 读到 → 罚金 400G
                    },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "（拼死一战）",
                        NpcResponse = "好！那就打到你爬不起来！",
                        Action = "NONE",
                        NextTurn = ""  // 关闭对话，继续战斗
                    }
                }
            }
        }
    };

    string label = $"Surrender_Player_{target.Index}";
    DialogueInjector.InjectScriptAsOpening(script, label);

    var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
    conversationLogic?.StartConversation(target, true, false);

    DebugLogger.Log($"[Combat] 玩家向 {npcName} 认输");
}
```

**对话流**：
```
player_lose: "把钱袋交出来！"
  ├─ ① 交钱 → INTENT:Pay?pay → 扣 200G → close
  ├─ ② 求饶 → INTENT:Beg → roll
  │   ├─ 成功 → "算你运气好" → close
  │   └─ 失败 → NextTurnOnFail → player_lose_counteroffer
  └─ ③ 威胁 → INTENT:Threaten → roll → 成功/失败

player_lose_counteroffer: "400 第纳尔！"
  ├─ ① 交 400G → INTENT:Pay?counteroffer_beg → 扣 400G → close
  └─ ② 拼死一战 → close → 继续战斗
```

### 8e. 新增方法：AcceptAgentSurrender

```csharp
/// <summary>接受目标 NPC 的认输请求</summary>
private void AcceptAgentSurrender(Agent target)
{
    if (target == null || !target.IsActive()) return;
    string npcName = target.Name?.ToString() ?? "目标";

    var script = new DialogueInjector.DialogueInjectScript
    {
        InjectAtToken = "start",
        EntryTurn = "npc_beg",
        Turns = new List<DialogueInjector.DialogueInjectTurn>
        {
            new DialogueInjector.DialogueInjectTurn
            {
                Id = "npc_beg",
                SpeakerIndex = 0,
                NpcLine = "（丢下武器，踉跄后退，举起双手）别、别打了……我认输！",
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    // ① 宽宏大量
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "你走吧。",
                        NpcResponse = "多、多谢！我这就走……",
                        Action = "INTENT:ResolveNpcSurrender",
                        ActionParam = "accept"
                    },
                    // ② 羞辱对方
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "给我跪下磕头认错！",
                        NpcResponse = $"（{npcName}屈辱地跪倒在地，额头重重磕在地上……）",
                        Action = "INTENT:ResolveNpcSurrender",
                        ActionParam = "humiliate"
                    },
                    // ③ 索要赎金
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "把钱交出来，饶你一命。",
                        NpcResponse = "好、好……都给你！求你放过我……",
                        Action = "INTENT:ResolveNpcSurrender",
                        ActionParam = "ransom"
                    },
                    // ④ 拒绝认输
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "太迟了。继续打！",
                        NpcResponse = $"不——！（{npcName}绝望地重新抓起武器）",
                        Action = "INTENT:ResolveNpcSurrender",
                        ActionParam = "refuse"
                    }
                }
            }
        }
    };

    string label = $"Surrender_NPC_{target.Index}";
    DialogueInjector.InjectScriptAsOpening(script, label);

    var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
    conversationLogic?.StartConversation(target, true, false);

    DebugLogger.Log($"[Combat] 玩家与投降的 {npcName} 开始对话");
}
```

---

## 9. 修改 `AgentHUD/AgentHudVM.cs`

在 `UpdateLogic()` 中（`isFighting` 读取处附近）：

```csharp
var brain = AgentAIController.GetBrainForAgent(TargetAgent);
NpcIntentDebugText = brain?.CurrentIntent?.ToString() ?? "";
```

新增属性：
```csharp
private string _npcIntentDebugText;
[DataSourceProperty]
public string NpcIntentDebugText
{
    get => _npcIntentDebugText;
    set { if (value != _npcIntentDebugText) { _npcIntentDebugText = value; OnPropertyChangedWithValue(value, "NpcIntentDebugText"); } }
}
```

（AgentHud 的 XML prefab 可后续加上此字段的 TextWidget 绑定，本次先做 C# 侧）

---

## 10. 对话选项后果总结（零 NONE）

| 场景 | 选项 | Action | 后果 |
|------|------|--------|------|
| 玩家认输 ① | "……（交出钱袋）" | `INTENT:PlayerSurrenderPay?pay` | -200G，荣誉 -1，勇敢 -1，战斗结束 |
| 玩家认输 ①' | （counteroffer 后）"……（交出 400 第纳尔）" | `INTENT:PlayerSurrenderPay?counteroffer_beg` | **-400G**（翻倍），荣誉 -1，勇敢 -1，战斗结束 |
| 玩家认输 ② | "求你放过我……（说服）" | `INTENT:PlayerSurrenderBeg?beg` | **成功**: 免单，荣誉 -1，战斗结束；**失败**: 不扣任何东西 → 跳转 counteroffer turn（罚金翻倍至 400G） |
| 玩家认输 ③ | "你这条狗！……（威胁）" | `INTENT:PlayerSurrenderThreaten?threaten` | **成功**: 免单，荣誉 -1（勇敢不变，有骨气）；**失败**: NPC 暴怒，继续战斗 |
| 接受认输 ① | "你走吧。" | `INTENT:ResolveNpcSurrender?accept` | 好感 +2，战斗结束 |
| 接受认输 ② | "给我跪下磕头！" | `INTENT:ResolveNpcSurrender?humiliate` | 好感 -10，NPC 嗑头动画，战斗结束 |
| 接受认输 ③ | "钱交出来，饶你一命。" | `INTENT:ResolveNpcSurrender?ransom` | NPC→玩家 ≤500G，战斗结束 |
| 接受认输 ④ | "太迟了，继续打！" | `INTENT:ResolveNpcSurrender?refuse` | 拒绝，NpcIntent→Fighting，继续战斗 |

**关键设计变更**：
- 求饶失败 **不再自动扣钱**。检定失败 ≠ 玩家同意支付。改为 NPC 还价（罚金翻倍），玩家在 counteroffer turn 中自主选择接受或拒绝。
- 对标 KCD2：先认罪再讨价还价——认罪/求饶本身不是终结，是谈判的起点。

---

## 11. 调用链总览

### 流程 A：NPC 残血 → 玩家接受认输
```
FightEnemyAction.OnTick (healthRatio < 0.3)
  → SendEventToAgent(agent, "event_npc_surrender")
    → Brain.SetNpcIntent(Surrendering, Agent.Main)
    → AgentHudMissionView.AgentSay("我认输！别打了！")
  → InteractionMissionView 检测到 intentChanged
    → UI 刷新：认输(F) + 接受认输(G)

玩家按 G
  → AcceptAgentSurrender(target)
    → DialogueInjector.InjectScriptAsOpening(script)
      → NPC: "别打了……我认输！"
      → 4 个 INTENT:ResolveNpcSurrender 选项
    → MissionConversationLogic.StartConversation(target)

玩家选 "你走吧"
  → DialogueInjector.ExecuteAction → INTENT:ResolveNpcSurrender?accept
    → ResolveNpcSurrenderIntent.OnInstant
      → ChangeRelationAction (+2)
      → SendEventToAgent("event_surrender_accepted")
        → Brain.SetNpcIntent(None), ClearAllActions
          → FightEnemyAction.OnEnd → UnregisterCombatant
```

### 流程 B：玩家主动认输

```
玩家按 F
  → PlayerSurrenderToAgent(target)
    → DialogueInjector.InjectScriptAsOpening(script)
      → NPC: "哼，知道打不过了吧？把钱袋交出来，饶你一命。"
      → 三个选项：
        ① "……（交出钱袋）" → INTENT:PlayerSurrenderPay?pay
           → 无检定 → -200G, honor-1, valor-1 → 战斗结束
        ② "求你放过我……（说服）" → INTENT:PlayerSurrenderBeg?beg
           → 魅力检定 → 成功: 免单 honor-1 / 失败: -300G honor-2 valor-2 → 战斗结束
        ③ "你这条狗！……（威胁）" → INTENT:PlayerSurrenderThreaten?threaten
           → 胆魄检定 → 成功: 免单 honor-1 / 失败: NPC 暴怒 → 战斗继续
    → MissionConversationLogic.StartConversation(target)
```

### 流程 C：玩家拒绝认输
```
玩家选 "太迟了，继续打！"
  → INTENT:ResolveNpcSurrender?refuse
    → ResolveNpcSurrenderIntent.OnInstant
      → SendEventToAgent("event_surrender_refused")
        → Brain.SetNpcIntent(Fighting)（不 ClearAllActions）
      → AgentHudMissionView.AgentSay("不——！！")
```

---

## 12. 验证

1. 编译通过
2. Mission 场景与 NPC 交战 → 准星对准 → UI 显示 **认输(F)**
3. 把 NPC 打到残血（< 30%）→ NPC 冒泡"我认输！"→ UI 显示 **认输(F) + 接受认输(G)**
4. 按 G → 对话弹出"别打了……"→ 4 个选项逐一验证后果：
   - "你走吧" → +2 好感，战斗结束
   - "跪下磕头" → -10 好感，嗑头动画，战斗结束
   - "钱交出来" → 金币入账，战斗结束
   - "继续打" → NPC 绝望冒泡，回到战斗
5. 按 F → 对话弹出"知道打不过了吧？" → 扣 200G，战斗结束
6. AgentHud 可见 NPC 的 `CurrentIntent` 调试文本
7. 对准非战斗 NPC → 正常显示对话/闲聊/探查

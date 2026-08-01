# 偷窃玩法数值平衡 — 发布前调整计划

## Context

发布前审查发现偷窃系统存在多个数值/机制问题：
1. **搜刮尸体零风险**：满街扒尸体无人反应，与扒窃活人形成巨大风险断层
2. **偷窃过程中警戒值衰减过快**：玩家在扒窃时，受害者看不到玩家 → 警戒衰减 0.15/s → 黄区越来越宽→越偷越容易（与设计意图相反）
3. **族长金库全偷**：击晕族长搜刮 → 一次拿走几十万第纳尔
4. **WitnessSystemEnabled 默认 false**：目击系统整个关掉，犯罪无人看见
5. **普通士兵铠甲价值远超其身价**：一件 T5 甲可能值几千，但士兵身价才几百——偷装备回报远超偷钱袋，且零额外风险
6. **黄区整体偏宽**：110px 判定区在 640px 条上偏大
7. **没有负重检查**：偷到的东西可能超出 party 负重

## 修改清单

### 1. WitnessSystemEnabled 默认开启 — P0

**文件**: `Core/Settings.cs:43`, `config.json`

- `Settings.cs`: `WitnessSystemEnabled` 默认值 `false` → `true`
- `config.json`: 新增 `"WitnessSystemEnabled": true`

### 2. 搜刮尸体统一走偷窃犯罪管线 — P0

**文件**: `Interaction/InteractionMissionView.cs`

**问题**：`LootAgent(targetAgent, isStealing: false)` 搜刮死尸完全跳过犯罪系统——不查目击、不记赃物、不被围堵。而击晕后搜刮走 `RecordUnconsciousLootTheft` 有完整犯罪记账。死/活搜刮应统一视为偷窃。

**方案**：删除 `IsUnconsciousAlive` 的死/活分叉，所有和平场景搜刮（`isStealing:false`）统一走 `RecordUnconsciousLootTheft`。

**涉及点**（`LootAgent` 及 `ProcessPendingLoot` 中）：
- `"自己挑选"`路径：移除 `if (IsUnconsciousAlive(corpse))` 条件，所有搜刮都走 `StealManager.RecordUnconsciousLootTheft`
- `"全部拿走"`路径：同上，移除 `!isStealing && IsUnconsciousAlive` 条件
- `_lootedCorpses` 标记照常（防重复搜刮）

**效果**：和平场景搜刮尸体 = 偷窃，与扒窃/击晕搜刮共享同一目击→WitnessCrime→L3质问→赔偿对话管线。战场搜刮不受影响（`IsInteractionDisabled` 跳过整个 HandleInput）。

### 3. 偷窃过程中警戒值黏性 — P0 🔴 用户反馈核心问题

**文件**: `Stealth/StealBarVM.cs`

**问题**：扒窃时受害者看不到玩家（玩家在背后），`AgentBrain.UpdateAlertCognition` 走衰减分支 → 0.15/s 衰减 → 黄区越来越宽。

**方案**：在 `StealBarVM` 中引入"会话峰值黏性"——记录此次偷窃会话中受害者的最高警戒值，`RecalcZoneSize` 用 `max(current, peak * 0.8)` 计算宽度。

```csharp
// StealBarVM 新增字段
private float _stealSessionPeakAlert = 0f;

// GetCurrentAlert 改为：
private float GetCurrentAlert()
{
    if (_mode == StealBarMode.Lockpick || _target == null) return 0f;
    float current = AgentAIController.GetBrainForAgent(_target)?.AlertValue ?? 0f;
    _stealSessionPeakAlert = MathF.Max(_stealSessionPeakAlert, current);
    // 黏性：取当前与峰值 80% 的较大值，衰减 20% 后不再下降
    return MathF.Max(current, _stealSessionPeakAlert * 0.8f);
}
```

效果：受害者警戒峰值 1.5 → 实际衰减到 1.2 就停了 → 黄区不会越偷越宽。关 UI 后重置。

### 4. 黄区整体缩短 — P1

**文件**: `Stealth/StealBarVM.cs:61`

```csharp
// 当前
private const float BaseZoneWidthPickpocket = 110f;
// 改为
private const float BaseZoneWidthPickpocket = 90f;  // 缩短 ~18%
```

同时调整 `AlertWidthMax` 保持比例：
```csharp
// 当前
private const float AlertWidthMax = 2.2f;
// 改为
private const float AlertWidthMax = 1.8f;  // 警戒更快达到最大左扣
```

### 5. 偷窃前 Party 负重检查 — P1

**文件**: `Stealth/StealBarVM.cs` — `AttemptPickpocket` 方法

**方案**：在命中判定后、实际转移物品前，检查 `MobileParty.MainParty` 负重。

```csharp
// 在 AttemptPickpocket 命中分支中，StealSpecificItem/StealPurseGold 之前
if (MobileParty.MainParty != null)
{
    float currentWeight = MobileParty.MainParty.ItemRoster.TotalWeight;
    float capacity = MobileParty.MainParty.InventoryCapacity;
    if (currentWeight >= capacity)
    {
        // 本地化：负重已满提示
        InformationManager.DisplayMessage(new InformationMessage(
            LWNTextHelper.ResolveText("LWN_ui_steal_msg_overburdened", 
            "Your party is overburdened and cannot carry any more."), Colors.Red));
        return; // 不转移物品，关条
    }
}
```

### 6. 族长金库搜刮上限 — P0

**文件**: `Interaction/InteractionMissionView.cs` — `LootAgent` 方法

**问题**：`clanGold = targetHero.Gold` 全拿。

**方案**：加合理上限。
```csharp
// 当前 (line 1792)
clanGold = targetHero.Gold;
// 改为
clanGold = Math.Min(targetHero.Gold, 5000); // 上限 5000 第纳尔
```

### 7. 撬锁失误惩罚提高到 1.5 — P1

**文件**: `Stealth/StealBarVM.cs:82`

```csharp
// 当前
private const float NoiseWitnessAlert = 0.5f;
// 改为
private const float NoiseWitnessAlert = 1.5f;
```

### 8. 完美扒窃加微量脉冲 — P2

**文件**: `Stealth/StealBarVM.cs`

在 `AttemptPickpocket` 的 perfect 分支中，给受害者 0.1 脉冲：
```csharp
// 完美窃取也加微量——NPC "隐约觉得不对"
brain?.AddAlert(PlayerActionType.Steal, 0.1f);
```

### 9. 🆕 普通士兵装备降质 — P1 🔴 用户新增需求

**文件**: `Stealth/StealManager.cs` — `StealSpecificItem` 方法

**问题**：偷 T5 士兵身上的甲值几千，但士兵身价（`EstimateVictimValue`）才几百。需要比较物品价值与 NPC 身价，超出时给装备加"生锈的/破损的"前缀。

**方案**：

```
StealSpecificItem 中，转移物品之前：
① 获取受害者的"身价"：CrimePenaltyCalculator.EstimateVictimValue(agent)
② 获取物品市值：itemToSteal.Item.Value
③ 若 itemValue > victimValue × 1.5（物品明显比人值钱）：
   → 查找合适的低品质 ItemModifier
   → 用新的 EquipmentElement(item, poorModifier) 替代原 element 转移
```

**ItemModifier 查找逻辑**（两轮策略，铁律 5）：
- 第一轮：按物品类型选固定 modifier ID 列表尝试
  - 武器 → `dull_sword`, `rusty_sword`, `bent_cheap`, `cracked_cheap`
  - 铠甲 → `rusty_plate`, `dented_plate`, `rusty_chain`, `worn_leather`, `battered_leather`, `worn_cloth`
  - 盾牌 → `battered_shield`, `cracked_shield`
- 第二轮：遍历 `MBObjectManager.Instance.GetObject<ItemModifier>(m => m.PriceMultiplier < 1.0f)` 取第一个
- 兜底：modifier 为 null（无匹配则不加前缀，不影响转移）

**关键实现细节**：
- `EquipmentElement` 构造：`new EquipmentElement(itemToSteal.Item, poorModifier)`
- 传给 `AgentControlHelper.TransferItems(null, Hero.MainHero, degradedElement, 1)`
- `RecordStolen` 也记带 modifier 的元素（归还时也是劣质品）
- Hero 目标不受此规则影响（Hero 的装备本来就值钱，身价也高）

**注意**：此项修改需要新增本地化 key（提示玩家被降质），以及 `LWN_ui_steal_msg_degraded` 的 DisplayMessage。

---

## 文件修改汇总

| 文件 | 改动 |
|------|------|
| `Core/Settings.cs` | ① `WitnessSystemEnabled` 默认 true |
| `config.json` | ① 加 `WitnessSystemEnabled: true` |
| `Stealth/StealBarVM.cs` | ② 会话峰值黏性 ③ BaseZoneWidthPickpocket 110→90 + AlertWidthMax 2.2→1.8 ④ 负重检查 ⑤ NoiseWitnessAlert 0.5→1.5 ⑥ 完美脉冲 0.1 |
| `Interaction/InteractionMissionView.cs` | ⑦ 搜刮死尸目击检查 ⑧ 族长金库上限 |
| `Stealth/StealManager.cs` | ⑨ 士兵装备降质（ItemModifier 查找+替换） |
| `ModuleData/Languages/std_LivingWorldNpcs_strings.xml` | ⑩ 新增 `LWN_ui_steal_msg_overburdened`, `LWN_ui_steal_msg_degraded` |
| `ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml` | ⑩ 中文翻译 |

## 验证方式

1. **目击系统**：config.json 设 `WitnessSystemEnabled: true` → 游戏中偷动物/扒窃被目击 → 围堵触发
2. **黏性**：扒窃时蹲在受害者背后等 5 秒 → 黄区宽度不应明显变宽（对比修改前）
3. **黄区宽度**：0 警戒+轻物品 → 判定区约 90px（vs 旧 110px）
4. **负重**：party 满负重时偷东西 → DisplayMessage "负重已满"
5. **族长金库**：击晕族长搜刮 → gold ≤ 5000
6. **尸体搜刮**：广场中间搜刮死尸 → 有目击者 → WitnessCrime 广播 → L3 质问围堵（与扒窃/击晕搜刮完全相同的犯罪管线）
7. **装备降质**：偷 T5 士兵的甲（value > 身价×1.5）→ 获得"生锈的 xxx"
8. **撬锁失误**：撬锁失误 → 目击者 +1.5（vs 旧 0.5）

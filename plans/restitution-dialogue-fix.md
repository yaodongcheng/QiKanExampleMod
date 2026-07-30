# 赔偿对话体验修复

## 全量审计：8 处 INTENT:PayRestitution

| # | 位置 | PlayerLine | 问题 |
|---|------|-----------|------|
| 1 | L384 `BuildRestitutionSubtree` | `"我愿意赔{cost}第纳尔。"` | ✅ NPC 已在 `restitution_demand` 开过价 |
| 2 | L407 `BuildRestitutionSubtree` | `"行，就这个数——{haggleCost}第纳尔。"` | ✅ 砍价成功，NPC 已确认新价格 |
| 3 | L893 escalate `continue_chat` | `"我认罚，{AlertFineCost}第纳尔。"` | ❌ 没走 restitution_demand |
| 4 | L1007 Deter Layer 2 | `"我认罚，{AlertFineCost}第纳尔。"` | ❌ 同上 |
| 5 | L1037 Search 行贿 | `"别查了，我赔你点钱。"` | ✅ 不标具体金额 |
| 6 | L1055 Search `recover_confront` | `"我愿意赔{RestitutionCost}第纳尔。"` | ❌ 没走 restitution_demand |
| 7 | L1088 `BuildRecoverSubtree` | `"我愿意赔{RestitutionCost}第纳尔。"` | ❌ 同上 |
| 8 | L1114 `BuildStopSubtree` | `"我愿意付{RestitutionCost}第纳尔。"` | ❌ 同上 |

**统一修法**：#3, #4, #6, #7, #8 全部改为路由到 `restitution_demand`——NPC 在 `restitution_demand` 里统一算账开价，玩家再决定接受/砍价/拒绝。

## 方案

### 改动 1：所有赔钱入口统一路由到 `restitution_demand`

**文件**：`CrimeDialogueBuilder.cs`

**#3 escalate `continue_chat`**（:893）：
- `PlayerLine`：`"我认罚，{AlertFineCost}第纳尔。"` → `"我愿意赔偿。"`
- `Action`：`"INTENT:PayRestitution"` → `"NONE"`
- `NextNodeOnSuccess`：`"alert_esc_fine_ack"` → `"restitution_demand"`
- 删 `alert_esc_fine_ack` node（:899）
- `BuildAlertInterceptScript` 末尾加 `BuildRestitutionSubtree(nodes, r, ctx)`

**#4 Deter Layer 2**（:1007）：
- `PlayerLine` → `"我愿意赔偿。"`，`Action` → `"NONE"`，`NextNodeOnSuccess` → `"restitution_demand"`
- 删 `alert_deter_fine_ack` node（:1017）
- `BuildDeterSubtree` 末尾加 `BuildRestitutionSubtree(nodes, r, ctx)`

**#6 Search recover_confront**（:1055）：
- `PlayerLine` → `"我愿意赔偿。"`，`Action` → `"NONE"`，`NextNodeOnSuccess` → `"restitution_demand"`
- 删 `alert_recover_pay_ack` 从 `AddRecoverAckNodes`
- `BuildSearchSubtree` 末尾加 `BuildRestitutionSubtree(nodes, r, ctx)`

**#7 BuildRecoverSubtree**（:1088）：同 #6

**#8 BuildStopSubtree**（:1114）：
- `PlayerLine` → `"我愿意赔偿。"`，`Action` → `"NONE"`，`NextNodeOnSuccess` → `"restitution_demand"`
- 删 `alert_stop_pay_ack`（:1120）
- 末尾加 `BuildRestitutionSubtree(nodes, r, ctx)`

改完后，所有赔钱路径的对话流统一为：
```
NPC: 质问/警告
Player: "我愿意赔偿。"          ← 不标价
  └─ NPC: "{RestitutionBreakdown}"  ← 算账 + 开价
     Player: 接受 / 砍价 / 不赔
```

### 改动 2：GetRestitutionBreakdown — NPC 逐项算账

**文件**：`WorldEvent.cs`

新增 `BuildDetailedHarmBreakdown()`（注意 NPC 情报边界：旧案赃物 NPC **没看见是谁偷的**→ 用"村里丢了XX"而非"你偷了XX"；袭击是当场抓住的 → 可以直说"你把XX打晕了"）：
```csharp
public string BuildDetailedHarmBreakdown()
{
    bool hasTheft = TotalStolenCount > 0;
    bool hasAssault = AssaultVictimNames?.Count > 0;
    
    string theftPart = hasTheft
        ? $"丢了{BuildStolenItemsDescription()}，市值{TotalStolenValue}第纳尔，一直没找到是谁干的"
        : "";
    string assaultPart = "";
    if (hasAssault)
    {
        string victimDesc = AssaultVictimNames.Count == 1
            ? AssaultVictimNames[0]
            : $"{string.Join("、", AssaultVictimNames)}等{AssaultVictimNames.Count}人";
        assaultPart = $"你把{victimDesc}打晕了，身价{AssaultRestitutionValue}第纳尔";
    }
    
    if (hasTheft && hasAssault)
        return $"前阵子村里{theftPart}。今天{assaultPart}——既然抓着的是你，两笔账一起算";
    if (hasTheft) return $"村里{theftPart}";
    if (hasAssault) return assaultPart;
    return "闹了事";
}
```

`GetRestitutionBreakdown()` 显式倍率：
```csharp
$"{harm}。{crimeGerund}按规矩罚{multiplier}倍，一共{total}第纳尔。你认不认？"
```

### 改动 3：统一金额

1. `ComputePenalty(evt)` 有事件时走 `ComputeCost(evt, CostType.Restitution)`
2. `{AlertFineCost}` 占位符 → `{RestitutionCost}`
3. `PayRestitutionIntent.OnInstant` alert_fine 路径用 `ComputeCost(Restitution)`

（现在 alert_fine 路径只有通过 escalate continue_chat → restitution_demand → PayRestitution 进入，统一走 Restitution。）

## 涉及文件

| 文件 | 改动 |
|------|------|
| `WorldEvent.cs` | 新增 `BuildDetailedHarmBreakdown()`；重写 `GetRestitutionBreakdown()` |
| `CrimeDialogueBuilder.cs` | #3/#4/#6/#7/#8 全部路由到 `restitution_demand`；5 个子树加 `BuildRestitutionSubtree` 调用；删废弃 ack nodes |
| `CrimePenaltyCalculator.cs` | `ComputePenalty(evt)` → Restitution |
| `AccountabilityIntents.cs` | alert_fine 路径 → Restitution |
| `PlaceholderResolver.cs` | `{AlertFineCost}` → Restitution |

## 验证

所有场景下玩家选"赔偿"后：
1. 不显示价格
2. NPC 算账（明细 + 倍率 + 总价）
3. 玩家选择接受/砍价/不赔
4. 旧案合并时 NPC 说清"前阵子丢了X…今天你又伤了人…两笔账一起算"

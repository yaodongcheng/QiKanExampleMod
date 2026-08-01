# Harmony 补丁排查计划 — v1.4.7 角色模型横置 Bug

## 结论

**凶手**：`SuppressVanillaConversationMissionPatch`（`ConversationEntryPatch.cs`）

```csharp
[HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
public static class SuppressVanillaConversationMissionPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        return !MapEncounterDialogState.Active;
    }
}
```

## 根因分析

该补丁用途：我们 mod 的大地图遭遇对话中，抑制原版 `ConversationMissionLogic.OnMissionTick`（防止原版自动推进对话/自动结束 Mission）。

正常时 `MapEncounterDialogState.Active == false` → Prefix 返回 `true`（放行），理论上完全无害。

**但在 v1.4.7 中出了问题**。角色创建和物品界面底层竟然也用了 `ConversationMissionLogic`（引擎内部渲染角色模型时可能复用了对话 Mission 的基础设施）。补丁的方法签名 `Prefix()` 与原始方法 `OnMissionTick(float dt)` 参数不匹配，在 v1.4.7 的 Harmony/.NET 版本中可能导致返回值传递异常，原始方法被无条件跳过。

## 修复方案

加 `MissionMode` 守卫——只在真正的对话 Mission 中才检查 `MapEncounterDialogState.Active`，其他场景一律放行：

```csharp
[HarmonyPrefix]
public static bool Prefix()
{
    // 只在我们 mod 的遭遇对话 Mission 中抑制原版 tick
    if (Mission.Current?.Mode != MissionMode.Conversation)
        return true;
    return !MapEncounterDialogState.Active;
}
```

角色创建用默认模式(0)或 Startup，物品界面用 Barter 或默认模式，都不会被误伤。

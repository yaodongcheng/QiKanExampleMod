using HarmonyLib;
using TaleWorlds.CampaignSystem.Inventory;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 玩家在物品栏里换装后，把**场景里那个 Agent 的外观**同步刷新（2026-11-16）。
    ///
    /// 为什么需要：引擎只在生成/潜行换装等少数时机调
    /// `Agent.UpdateSpawnEquipmentAndRefreshVisuals`，玩家在物品栏里改 `Hero.CivilianEquipment`
    /// 之后场景里的自己**不会**跟着变（下次进场景才生效）。剧情探针要靠"穿没穿贵族戏服"
    /// 判定守卫认不认人，所以必须当场刷新。
    ///
    /// 🔴 事件驱动、零轮询：挂 `InventoryLogic.OnAfterTransfer`（每次转移提交后）和
    /// `InventoryLogic.ResetLogic`（取消交易恢复装备后）。两者都只在
    /// **装备真的变了**时才调刷新（`SyncPlayerAppearanceFromInventory` 里做逐槽比较）。
    /// 三个目标方法在 1.2.12 / 1.3.15 / 1.4.6 / 1.5.1 四个版本的反编译里都存在
    /// （补丁目标找不到会抛异常，见 CLAUDE.md 那条 Harmony 纪律）。
    /// </summary>
    [HarmonyPatch(typeof(InventoryLogic), "OnAfterTransfer")]
    internal static class StoryProbeInventoryTransferPatch
    {
        private static void Postfix()
            => StoryProbeMissionBehavior.SyncPlayerAppearanceFromInventory("inventory_transfer");
    }

    /// <summary>取消交易会把装备恢复成打开物品栏前的样子 —— 场景外观也要跟着退回去。</summary>
    [HarmonyPatch(typeof(InventoryLogic), "ResetLogic")]
    internal static class StoryProbeInventoryResetPatch
    {
        private static void Postfix()
            => StoryProbeMissionBehavior.SyncPlayerAppearanceFromInventory("inventory_reset");
    }
}

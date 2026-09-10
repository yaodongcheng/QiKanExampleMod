using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 大地图相机对准玩家（2026-09-08 用户实测：Finalize 时 teleport 的时序不生效——CC 完成回调时
	/// ActiveState 仍为建号状态，MapState 尚未推入 → 织丰同款代码在 OnCharacterCreationFinalized 里被
	/// 空检查跳过。织丰等效做法 = 自建 MapView（ShokuhoMapView）初始化后再拉相机；
	/// 我们轻量版 = MapScreen.OnInitialize（地图就绪）后 Postfix 拉一次——与织丰时机等价）。
	/// 保底幂等：只触发一次拉相机（地图初始化仅一次）。
	/// </summary>
	[HarmonyPatch(typeof(MapScreen), "OnInitialize")]
	public static class MapScreenCameraPatch
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (GameStateManager.Current.ActiveState is MapState mapState && mapState.Handler != null)
			{
				DebugLogger.Log("[MapCameraPatch] 地图就绪 → 相机对准出生点。");
				mapState.Handler.ResetCamera(true, true);
				mapState.Handler.TeleportCameraToMainParty();
			}
		}
	}
}

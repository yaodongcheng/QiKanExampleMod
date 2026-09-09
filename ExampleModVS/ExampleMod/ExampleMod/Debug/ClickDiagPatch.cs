using System;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 地图点击链诊断（2026-09-09，京点不开面板/点击图标无反应）。
	///
	/// 基准（1.2.12 SandBox.View.dll MapScreen.HandleLeftMouseButtonClick 反编译）：
	///   点击 → visualOfSelectedEntity（raycast 命中实体才有）→ GetMapEntity()
	///   →（定居点）InteractionPosition=GatePosition → GetFaceIndex → DoesPathExistBetweenFaces(...)
	///   → 全过才 mapEntity.OnMapClick（发移动指令）。
	/// 🔴 语义：无 [ClickDiag] 行 = raycast 压根没命中定居点图标实体；
	///   有行 + sameIsland=False = 寻路守卫拦下；有行 + 全绿 = 守卫过、指令已发。
	/// 版本：类型名+方法名字符串运行期解析（改名 = 静默跳过，以日志缺失即可察觉）。
	/// </summary>
	[HarmonyPatch("SandBox.View.Map.MapScreen", "HandleLeftMouseButtonClick")]
	public static class ClickDiagPatch
	{
		[HarmonyPrefix]
		private static void Prefix(MapScreen __instance, UIntPtr selectedSiegeEntityID, PartyVisual visualOfSelectedEntity)
		{
			try
			{
				// 只关心：点中带图实体的情形（过滤空白点击/纯地面点击）
				if (visualOfSelectedEntity == null)
				{
					return;
				}
				IMapEntity mapEntity = visualOfSelectedEntity.GetMapEntity();
				if (mapEntity == null)
				{
					return;
				}

				if (mapEntity is Settlement settlement)
				{
					bool faceValid = false;
					bool sameIsland = false;
					try
					{
						PathFaceRecord gateFace = Campaign.Current.MapSceneWrapper.GetFaceIndex(settlement.GatePosition);
						faceValid = gateFace.IsValid();
						sameIsland = faceValid && MobileParty.MainParty.CurrentNavigationFace.IsValid() &&
							Campaign.Current.MapSceneWrapper.AreFacesOnSameIsland(gateFace, MobileParty.MainParty.CurrentNavigationFace, false);
					}
					catch (Exception inner)
					{
						DebugLogger.Log($"[ClickDiag] 守卫实测异常: {inner.Message}");
					}
					DebugLogger.Log($"[ClickDiag] 点击定居点图标: {settlement.StringId} gate=({settlement.GatePosition.X:F1},{settlement.GatePosition.Y:F1}) " +
						$"gateFaceValid={faceValid} sameIsland={sameIsland} playerPos=({MobileParty.MainParty.Position2D.X:F1},{MobileParty.MainParty.Position2D.Y:F1}) " +
						$"playerFaceValid={MobileParty.MainParty.CurrentNavigationFace.IsValid()}");
				}
				else
				{
					DebugLogger.Log($"[ClickDiag] 点击非定居点图标: {mapEntity.GetType().Name}");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[ClickDiag] 诊断段异常: {ex.Message}");
			}
		}
	}
}

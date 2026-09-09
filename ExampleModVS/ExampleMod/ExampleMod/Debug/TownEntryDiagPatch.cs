using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 进城场景 NRE 诊断（2026-09-09，进京城镇中心 MissionAgentHandler.GetAllProps NullReference）。
	///
	/// 反编译基准（1.2.12 SandBox.dll MissionAgentHandler.GetAllProps 实锤）三个高危空引用点：
	///   ① Settlement.CurrentSettlement.IsTown        —— CurrentSettlement null？
	///   ② CommonAreaMarker.Tag → GetAlley → PlayerEncounter.LocationEncounter.Settlement —— LocationEncounter null？
	///   ③ WorkshopAreaMarker.Tag → GetWorkshop → settlement.Town.Workshops.Length   —— Workshops null？
	/// 本补丁 = 前缀逐一实测（绝不 NRE），日志直指空引用来源。
	/// 版本：类型+方法名字符串运行期解析（改名 = 静默跳过，以 [TownEntryDiag] 缺失即可察觉）。
	/// </summary>
	[HarmonyPatch("SandBox.Missions.MissionLogics.MissionAgentHandler", "GetAllProps")]
	public static class TownEntryDiagPatch
	{
		[HarmonyPrefix]
		private static void Prefix()
		{
			try
			{
				Settlement current = Settlement.CurrentSettlement;
				LocationEncounter locationEncounter = PlayerEncounter.LocationEncounter;
				var workshops = (current?.Town != null) ? current.Town.Workshops : null;
				int notableCount = (current?.Notables != null) ? current.Notables.Count : -1;

				DebugLogger.Log($"[TownEntryDiag] GetAllProps 前置状态: " +
					$"CurrentSettlement={(current?.StringId ?? "NULL⚠")} " +
					$"LocationEncounter={(locationEncounter != null ? locationEncounter.Settlement?.StringId ?? "sett=null" : "NULL⚠")} " +
					$"townWorkshops={(workshops == null ? "NULL⚠" : "len=" + workshops.Length)} " +
					$"notables={notableCount} " +
					$"isTown={(current != null ? current.IsTown.ToString() : "-")} " +
					$"isVillage={(current != null ? current.IsVillage.ToString() : "-")}");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[TownEntryDiag] 诊断段异常: {ex.Message}");
			}
		}
	}
}

using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 城镇区标记 Tag 空值兜底（通用基座，2026-09-09 雷 38 —— 老存档进城 NRE 收尾）。
	///
	/// 机制（反编译 1.2.12 SandBox.dll + 京实机 15:58 日志）：
	///   MissionAgentHandler.GetAllProps 对每个 AreaMarker：
	///     item2.Tag（CommonAreaMarker.Tag → GetAlley() 匹配不到巷 = null）→
	///     item2.GetUsableMachinesInRange(item2.Tag.Contains("workshop") ? ...) ——
	///     Tag 为 null 时 .Contains 直接 NRE。成因 = 存档未刷新（Alleys 3→2 项）+ 场景区标记索引超界。
	/// 本补丁 = 兜底：GetAlley/GetWorkshop 匹配不到时回退基类 Tag（"area_marker_N"）
	///   —— Tag 永不 null → 老存档/任何内容包进所有城镇场景均不崩（区功能按缺省退化，v0 可接受）。
	/// 版本：类型+属性名字符串运行期解析（改名 = 静默跳过））。
	/// </summary>
	public class AreaMarkerTagGuard
	{
		[HarmonyPatch("SandBox.Objects.AreaMarkers.CommonAreaMarker", "get_Tag")]
		public static class CommonAreaTagGuard
		{
			[HarmonyPostfix]
			private static void Postfix(object __instance, ref string __result)
			{
				try
				{
					if (__result == null && __instance is AreaMarker marker)
					{
						__result = "area_marker_" + marker.AreaIndex; // 基类兜底 Tag，永不 null
						DebugLogger.Log($"[AreaMarkerGuard] CommonAreaMarker 巷匹配失败 → 回退基类 Tag={__result}（AreaIndex={marker.AreaIndex}）");
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[AreaMarkerGuard] CommonArea 兜底异常: {ex.Message}");
				}
			}
		}

		[HarmonyPatch("SandBox.Objects.AreaMarkers.WorkshopAreaMarker", "get_Tag")]
		public static class WorkshopAreaTagGuard
		{
			[HarmonyPostfix]
			private static void Postfix(object __instance, ref string __result)
			{
				try
				{
					if (__result == null && __instance is AreaMarker marker)
					{
						__result = "area_marker_" + marker.AreaIndex;
						DebugLogger.Log($"[AreaMarkerGuard] WorkshopAreaMarker 工坊匹配失败 → 回退基类 Tag={__result}（AreaIndex={marker.AreaIndex}）");
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[AreaMarkerGuard] Workshop 兜底异常: {ex.Message}");
				}
			}
		}
	}
}

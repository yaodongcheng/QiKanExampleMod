using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 引擎裸解引用守卫：<c>Kingdom.OnNewGameCreated</c> 无条件写
	/// <c>InitialHomeLand = Leader.HomeSettlement;</c>，而 <c>Leader => _rulingClan?.Leader</c>。
	/// 🔴 触发条件（2026-09-10 实机）：同一会话里**第二次开局**时报
	///   NullReferenceException @ Kingdom.OnNewGameCreated（选 1582 转变之卷）。
	/// 根因链：`RulingClan` 全程只有一条赋值路径——**读 Kingdom 节点时**
	///   `RulingClan = (ReadObjectReferenceFromXml("owner", typeof(Hero)) as Hero)?.Clan`；
	///   而 SubModule 的段顺序是 Kingdoms(7) → Factions(9) → Heroes(19)，
	///   读王国时英雄还没读 → 该表达式的 `?.Clan` 落空 → RulingClan 留着 null → Leader null → 崩。
	/// 处置：本补丁在 Leader 为 null 时不写 InitialHomeLand（**不做任何猜测性兜底**），
	///   留给 LivingWorldCampaign.OnNewGameCreatedPartialFollowUp 的既有家宅置位段处理
	///   （那里按「家族第一个据点」置位，与引擎打分同解）。
	/// 版本：1.2.12 反编译实证该方法存在（TaleWorlds.CampaignSystem.Kingdom.OnNewGameCreated(CampaignGameStarter)）。
	/// </summary>
	[HarmonyPatch(typeof(Kingdom), "OnNewGameCreated")]
	public static class KingdomOnNewGameCreatedGuardPatch
	{
		[HarmonyPrefix]
		public static bool Prefix(Kingdom __instance)
		{
			if (__instance == null)
			{
				return false;
			}
			Hero leader = __instance.Leader;
			if (leader == null)
			{
				DebugLogger.Log($"[KingdomHomeGuard] Kingdom {__instance.StringId} 的 Leader 为 null"
					+ "（RulingClan 未由 XML 读取链赋值）→ 跳过引擎 InitialHomeLand 置位，交由内容包置位段处理");
				return false;
			}
			return true;
		}
	}
}

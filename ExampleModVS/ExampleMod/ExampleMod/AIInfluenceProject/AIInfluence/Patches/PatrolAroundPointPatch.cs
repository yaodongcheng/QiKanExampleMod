using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace AIInfluence.Patches
{
	// Token: 0x020002CF RID: 719
	[HarmonyPatch(typeof(SetPartyAiAction), "GetActionForPatrollingAroundPoint")]
	public static class PatrolAroundPointPatch
	{
		// Token: 0x06001442 RID: 5186 RVA: 0x00126A30 File Offset: 0x00124C30
		private static bool Prefix(MobileParty owner, CampaignVec2 position, MobileParty.NavigationType navigationType, bool isFromPort)
		{
			return !PatrolActionGuard.ShouldBlockPointOrder(owner);
		}
	}
}

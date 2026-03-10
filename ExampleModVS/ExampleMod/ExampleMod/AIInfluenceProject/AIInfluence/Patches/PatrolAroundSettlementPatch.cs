using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIInfluence.Patches
{
	// Token: 0x020002CE RID: 718
	[HarmonyPatch(typeof(SetPartyAiAction), "GetActionForPatrollingAroundSettlement")]
	public static class PatrolAroundSettlementPatch
	{
		// Token: 0x06001441 RID: 5185 RVA: 0x00126A14 File Offset: 0x00124C14
		private static bool Prefix(MobileParty owner, Settlement settlement, MobileParty.NavigationType navigationType, bool isFromPort, bool isTargetingPort)
		{
			return !PatrolActionGuard.ShouldBlockSettlementOrder(owner, settlement);
		}
	}
}

using System;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x02000206 RID: 518
	public static class DiplomacyManagerHelpers
	{
		// Token: 0x06000FAC RID: 4012 RVA: 0x000FE25C File Offset: 0x000FC45C
		public static bool IsPlayerKingdom(Kingdom kingdom)
		{
			bool flag = kingdom == null;
			return !flag && kingdom.Leader == Hero.MainHero;
		}
	}
}

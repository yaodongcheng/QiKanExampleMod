using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace AIInfluence.Diplomacy
{
	// Token: 0x02000203 RID: 515
	public static class DiplomacyHelpers
	{
		// Token: 0x06000F91 RID: 3985 RVA: 0x000FDD30 File Offset: 0x000FBF30
		public static bool VerifyBattleSides(PartyBase party1, PartyBase party2, out Kingdom kingdom1, out Kingdom kingdom2)
		{
			kingdom1 = null;
			kingdom2 = null;
			bool flag = party1.IsMobile && party1.MobileParty != null && party1.MobileParty.IsBandit;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				bool flag2 = party2.IsMobile && party2.MobileParty != null && party2.MobileParty.IsBandit;
				if (flag2)
				{
					result = false;
				}
				else
				{
					bool flag3 = party1.MapFaction == null || party1.MapFaction.IsBanditFaction;
					if (flag3)
					{
						result = false;
					}
					else
					{
						bool flag4 = party2.MapFaction == null || party2.MapFaction.IsBanditFaction;
						if (flag4)
						{
							result = false;
						}
						else
						{
							kingdom1 = (party1.MapFaction as Kingdom);
							kingdom2 = (party2.MapFaction as Kingdom);
							bool flag5 = kingdom1 == null || kingdom2 == null;
							if (flag5)
							{
								result = false;
							}
							else
							{
								bool flag6 = kingdom1 == kingdom2;
								result = !flag6;
							}
						}
					}
				}
			}
			return result;
		}

		// Token: 0x06000F92 RID: 3986 RVA: 0x000FDE1C File Offset: 0x000FC01C
		public static bool VerifyHeroEventSides(Hero hero1, Hero hero2, out Kingdom kingdom1, out Kingdom kingdom2)
		{
			kingdom1 = null;
			kingdom2 = null;
			bool flag = hero1 == null || hero1.MapFaction == null || hero1.MapFaction.IsBanditFaction;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				bool flag2 = hero2 == null || hero2.MapFaction == null || hero2.MapFaction.IsBanditFaction;
				if (flag2)
				{
					result = false;
				}
				else
				{
					kingdom1 = (hero1.MapFaction as Kingdom);
					kingdom2 = (hero2.MapFaction as Kingdom);
					bool flag3 = kingdom1 == null || kingdom2 == null;
					if (flag3)
					{
						result = false;
					}
					else
					{
						bool flag4 = kingdom1 == kingdom2;
						result = !flag4;
					}
				}
			}
			return result;
		}
	}
}

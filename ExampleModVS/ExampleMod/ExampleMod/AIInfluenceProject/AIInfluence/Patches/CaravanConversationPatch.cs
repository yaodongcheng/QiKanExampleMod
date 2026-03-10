using System;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace AIInfluence.Patches
{
	// Token: 0x020002D9 RID: 729
	[HarmonyPatch(typeof(CaravanConversationsCampaignBehavior), "conversation_caravan_build_clickable_condition")]
	public static class CaravanConversationPatch
	{
		// Token: 0x0600145C RID: 5212 RVA: 0x00127B48 File Offset: 0x00125D48
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool Prefix(ref bool __result, ref TextObject explanation)
		{
			bool result;
			try
			{
				Settlement currentSettlement = Settlement.CurrentSettlement;
				bool flag = currentSettlement == null;
				if (flag)
				{
					explanation = null;
					__result = false;
					result = false;
				}
				else
				{
					Hero oneToOneConversationHero = Hero.OneToOneConversationHero;
					bool flag2 = oneToOneConversationHero != null && AIActionManager.Instance.IsActionActive(oneToOneConversationHero, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1791229499));
					if (flag2)
					{
						Settlement settlement;
						if ((settlement = oneToOneConversationHero.HomeSettlement) == null)
						{
							settlement = (oneToOneConversationHero.StayingInSettlement ?? oneToOneConversationHero.CurrentSettlement);
						}
						Settlement settlement2 = settlement;
						bool flag3 = settlement2 != null && currentSettlement != settlement2;
						if (flag3)
						{
							explanation = null;
							__result = false;
							return false;
						}
					}
					result = true;
				}
			}
			catch (Exception ex)
			{
				AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
				if (instance != null)
				{
					instance.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(432871147) + ex.Message);
				}
				explanation = null;
				__result = false;
				result = false;
			}
			return result;
		}
	}
}

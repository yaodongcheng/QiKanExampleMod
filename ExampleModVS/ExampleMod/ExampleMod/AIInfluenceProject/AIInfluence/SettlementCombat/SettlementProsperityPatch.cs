using System;
using AIInfluence.DynamicEvents;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200015C RID: 348
	[HarmonyPatch(typeof(DefaultSettlementProsperityModel))]
	public class SettlementProsperityPatch
	{
		// Token: 0x06000B13 RID: 2835 RVA: 0x000C66DC File Offset: 0x000C48DC
		[HarmonyPatch("CalculateHearthChange")]
		[HarmonyPostfix]
		public static void CalculateHearthChange_Postfix(Village village, ref ExplainedNumber __result)
		{
			try
			{
				bool flag = Campaign.Current == null;
				if (!flag)
				{
					bool flag2 = village == null || village.Settlement == null;
					if (!flag2)
					{
						bool flag3 = SettlementPenaltyManager.Instance == null && EconomicEffectsManager.Instance == null;
						if (!flag3)
						{
							bool flag4 = SettlementPenaltyManager.Instance != null;
							if (flag4)
							{
								ActivePenalty activePenalty = SettlementPenaltyManager.Instance.GetActivePenalty(village.Settlement);
								bool flag5 = activePenalty != null && activePenalty.IsActive();
								if (flag5)
								{
									TextObject description = new TextObject(activePenalty.Reason, null);
									__result.Add(-activePenalty.ProsperityPenaltyPerDay, description, null);
								}
							}
							bool flag6 = EconomicEffectsManager.Instance != null;
							if (flag6)
							{
								float value;
								float num;
								string value2;
								bool flag7 = EconomicEffectsManager.Instance.TryGetSettlementDailyEffect(village.Settlement, out value, out num, out value2);
								if (flag7)
								{
									bool flag8 = Math.Abs(value) > 0.001f;
									if (flag8)
									{
										TextObject description2 = new TextObject(value2, null);
										__result.Add(value, description2, null);
									}
								}
							}
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}

		// Token: 0x06000B14 RID: 2836 RVA: 0x000C66DC File Offset: 0x000C48DC
		[HarmonyPatch("CalculateProsperityChange")]
		[HarmonyPostfix]
		public static void CalculateProsperityChange_Postfix(Town fortification, ref ExplainedNumber __result)
		{
			try
			{
				bool flag = Campaign.Current == null;
				if (!flag)
				{
					bool flag2 = fortification == null || fortification.Settlement == null;
					if (!flag2)
					{
						bool flag3 = SettlementPenaltyManager.Instance == null && EconomicEffectsManager.Instance == null;
						if (!flag3)
						{
							bool flag4 = SettlementPenaltyManager.Instance != null;
							if (flag4)
							{
								ActivePenalty activePenalty = SettlementPenaltyManager.Instance.GetActivePenalty(fortification.Settlement);
								bool flag5 = activePenalty != null && activePenalty.IsActive();
								if (flag5)
								{
									TextObject description = new TextObject(activePenalty.Reason, null);
									__result.Add(-activePenalty.ProsperityPenaltyPerDay, description, null);
								}
							}
							bool flag6 = EconomicEffectsManager.Instance != null;
							if (flag6)
							{
								float value;
								float num;
								string value2;
								bool flag7 = EconomicEffectsManager.Instance.TryGetSettlementDailyEffect(fortification.Settlement, out value, out num, out value2);
								if (flag7)
								{
									bool flag8 = Math.Abs(value) > 0.001f;
									if (flag8)
									{
										TextObject description2 = new TextObject(value2, null);
										__result.Add(value, description2, null);
									}
								}
							}
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}
	}
}

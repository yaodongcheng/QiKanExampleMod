using System;
using AIInfluence.DynamicEvents;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000155 RID: 341
	[HarmonyPatch(typeof(DefaultSettlementFoodModel))]
	public class SettlementFoodPatch
	{
		// Token: 0x06000ACB RID: 2763 RVA: 0x000C51B8 File Offset: 0x000C33B8
		[HarmonyPatch("CalculateTownFoodStocksChange")]
		[HarmonyPostfix]
		public static void CalculateTownFoodStocksChange_Postfix(Town town, bool includeMarketStocks, bool includeDescriptions, ref ExplainedNumber __result)
		{
			try
			{
				bool flag = Campaign.Current == null;
				if (!flag)
				{
					bool flag2 = town == null || town.Settlement == null;
					if (!flag2)
					{
						bool flag3 = EconomicEffectsManager.Instance == null;
						if (!flag3)
						{
							float num;
							float value;
							string value2;
							bool flag4 = EconomicEffectsManager.Instance.TryGetSettlementDailyEffect(town.Settlement, out num, out value, out value2);
							if (flag4)
							{
								bool flag5 = Math.Abs(value) > 0.001f;
								if (flag5)
								{
									TextObject description = new TextObject(value2, null);
									__result.Add(value, description, null);
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

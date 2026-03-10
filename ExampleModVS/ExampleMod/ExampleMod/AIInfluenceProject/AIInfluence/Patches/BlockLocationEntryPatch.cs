using System;
using System.Runtime.CompilerServices;
using AIInfluence.SettlementCombat;
using HarmonyLib;
using SandBox.Objects;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace AIInfluence.Patches
{
	// Token: 0x020002CC RID: 716
	[HarmonyPatch(typeof(PassageUsePoint), "OnUse")]
	public static class BlockLocationEntryPatch
	{
		// Token: 0x0600143F RID: 5183 RVA: 0x00126788 File Offset: 0x00124988
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool Prefix(PassageUsePoint __instance, Agent userAgent, sbyte agentBoneIndex)
		{
			bool result;
			try
			{
				bool flag = __instance == null || Campaign.Current == null || CampaignMission.Current == null;
				if (flag)
				{
					result = true;
				}
				else
				{
					bool flag2 = __instance.IsMissionExit || __instance.ToLocation == null;
					if (flag2)
					{
						result = true;
					}
					else
					{
						bool flag3 = userAgent == null || !userAgent.IsMainAgent;
						if (flag3)
						{
							result = true;
						}
						else
						{
							Location location = CampaignMission.Current.Location;
							Location toLocation = __instance.ToLocation;
							bool flag4 = location == null || toLocation == null;
							if (flag4)
							{
								result = true;
							}
							else
							{
								bool flag5 = location.IsIndoor || !toLocation.IsIndoor;
								if (flag5)
								{
									result = true;
								}
								else
								{
									Settlement settlement;
									if ((settlement = Settlement.CurrentSettlement) == null)
									{
										LocationEncounter locationEncounter = PlayerEncounter.LocationEncounter;
										if ((settlement = ((locationEncounter != null) ? locationEncounter.Settlement : null)) == null)
										{
											MobileParty mainParty = MobileParty.MainParty;
											if ((settlement = ((mainParty != null) ? mainParty.CurrentSettlement : null)) == null)
											{
												Hero mainHero = Hero.MainHero;
												if ((settlement = ((mainHero != null) ? mainHero.CurrentSettlement : null)) == null)
												{
													MobileParty mainParty2 = MobileParty.MainParty;
													settlement = ((mainParty2 != null) ? mainParty2.LastVisitedSettlement : null);
												}
											}
										}
									}
									Settlement settlement2 = settlement;
									bool flag6 = settlement2 == null;
									if (flag6)
									{
										result = true;
									}
									else
									{
										AIInfluenceBehavior campaignBehavior = Campaign.Current.GetCampaignBehavior<AIInfluenceBehavior>();
										bool flag7 = campaignBehavior == null;
										if (flag7)
										{
											result = true;
										}
										else
										{
											SettlementCombatManager settlementCombatManager = campaignBehavior.GetSettlementCombatManager();
											bool flag8 = settlementCombatManager == null || !settlementCombatManager.IsActiveCombat();
											if (flag8)
											{
												result = true;
											}
											else
											{
												Settlement activeCombatSettlement = settlementCombatManager.GetActiveCombatSettlement();
												bool flag9 = activeCombatSettlement == null || activeCombatSettlement != settlement2;
												if (flag9)
												{
													result = true;
												}
												else
												{
													bool flag10 = !activeCombatSettlement.IsTown && !activeCombatSettlement.IsCastle;
													if (flag10)
													{
														result = true;
													}
													else
													{
														TextObject textObject = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-877118130), null);
														MBInformationManager.AddQuickInformation(textObject, 3000, null, null, "");
														SettlementCombatLogger instance = SettlementCombatLogger.Instance;
														if (instance != null)
														{
															instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1394941994), location.StringId, toLocation.StringId, settlement2.Name));
														}
														result = false;
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				SettlementCombatLogger instance2 = SettlementCombatLogger.Instance;
				if (instance2 != null)
				{
					instance2.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(994524438), ex.Message, ex);
				}
				result = true;
			}
			return result;
		}
	}
}

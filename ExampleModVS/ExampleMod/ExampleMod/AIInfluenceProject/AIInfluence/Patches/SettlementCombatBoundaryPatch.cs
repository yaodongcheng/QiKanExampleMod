using System;
using System.Runtime.CompilerServices;
using AIInfluence.SettlementCombat;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace AIInfluence.Patches
{
	// Token: 0x020002D4 RID: 724
	[HarmonyPatch(typeof(MissionBoundaryCrossingHandler))]
	[HarmonyPatch("HandleAgentStateChange")]
	public class SettlementCombatBoundaryPatch
	{
		// Token: 0x0600144E RID: 5198 RVA: 0x001270B4 File Offset: 0x001252B4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool Prefix(Agent agent, bool isAgentOutside, bool isTimerActiveForAgent, MissionTimer timerInstance)
		{
			bool result;
			try
			{
				bool flag = agent != Agent.Main;
				if (flag)
				{
					result = true;
				}
				else
				{
					bool flag2 = !isAgentOutside || !isTimerActiveForAgent || timerInstance == null;
					if (flag2)
					{
						result = true;
					}
					else
					{
						bool flag3 = !timerInstance.Check(false);
						if (flag3)
						{
							result = true;
						}
						else
						{
							Campaign campaign = Campaign.Current;
							AIInfluenceBehavior aiinfluenceBehavior = (campaign != null) ? campaign.GetCampaignBehavior<AIInfluenceBehavior>() : null;
							bool flag4 = aiinfluenceBehavior == null;
							if (flag4)
							{
								result = true;
							}
							else
							{
								SettlementCombatManager settlementCombatManager = aiinfluenceBehavior.GetSettlementCombatManager();
								bool flag5 = settlementCombatManager == null || !settlementCombatManager.IsActiveCombat();
								if (flag5)
								{
									result = true;
								}
								else
								{
									bool flag6 = Mission.Current != null && Mission.Current.IsPlayerCloseToAnEnemy(90f);
									if (flag6)
									{
										TextObject textObject = new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1551213479), null);
										MBInformationManager.AddQuickInformation(textObject, 0, null, null, "");
										SettlementCombatLogger instance = SettlementCombatLogger.Instance;
										result = false;
									}
									else
									{
										try
										{
											bool flag7 = Mission.Current != null;
											if (flag7)
											{
												int num = (Agent.Main != null) ? 1 : 0;
												int num2 = (Agent.Main != null) ? 1 : 0;
												Agent main = Agent.Main;
												Vec3 v = (main != null) ? main.Position : Vec3.Zero;
												Team playerTeam = Mission.Current.PlayerTeam;
												foreach (Agent agent2 in (((playerTeam != null) ? playerTeam.ActiveAgents : null) ?? new MBList<Agent>()))
												{
													bool flag8 = agent2 == null || agent2 == Agent.Main || !agent2.IsHuman || !agent2.IsActive();
													if (!flag8)
													{
														IAgentOriginBase origin = agent2.Origin;
														PartyBase partyBase = null;
														PartyAgentOrigin partyAgentOrigin = origin as PartyAgentOrigin;
														bool flag9 = partyAgentOrigin != null;
														if (flag9)
														{
															partyBase = partyAgentOrigin.Party;
														}
														else
														{
															PartyGroupAgentOrigin partyGroupAgentOrigin = origin as PartyGroupAgentOrigin;
															bool flag10 = partyGroupAgentOrigin != null;
															if (flag10)
															{
																partyBase = partyGroupAgentOrigin.Party;
															}
														}
														bool flag11 = partyBase != PartyBase.MainParty;
														if (!flag11)
														{
															bool flag12 = agent2.State != AgentState.Active || agent2.Health <= 0f;
															if (!flag12)
															{
																num++;
																bool flag13 = (agent2.Position - v).LengthSquared <= 15f;
																if (flag13)
																{
																	num2++;
																}
															}
														}
													}
												}
												settlementCombatManager.RegisterPlayerEscapeAttempt(num, num2);
											}
										}
										catch (Exception ex)
										{
											SettlementCombatLogger instance2 = SettlementCombatLogger.Instance;
											if (instance2 != null)
											{
												instance2.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1241787963), ex.Message, ex);
											}
										}
										result = true;
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex2)
			{
				SettlementCombatLogger instance3 = SettlementCombatLogger.Instance;
				if (instance3 != null)
				{
					instance3.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1377278011), ex2.Message, ex2);
				}
				result = true;
			}
			return result;
		}
	}
}

using System;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions;
using AIInfluence.Behaviors.AIActions.TaskSystem;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace AIInfluence.Patches
{
	// Token: 0x020002D2 RID: 722
	[HarmonyPatch(typeof(Army), "AddPartyToMergedParties")]
	public static class PreventArmyJoinOnPlayerTaskPatch
	{
		// Token: 0x0600144A RID: 5194 RVA: 0x00126DE4 File Offset: 0x00124FE4
		[HarmonyPrefix]
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static bool Prefix(Army __instance, MobileParty mobileParty)
		{
			bool result;
			try
			{
				bool flag = mobileParty == null || mobileParty.IsMainParty;
				if (flag)
				{
					result = true;
				}
				else
				{
					Hero leaderHero = mobileParty.LeaderHero;
					bool flag2 = leaderHero == null;
					if (flag2)
					{
						result = true;
					}
					else
					{
						bool flag3 = __instance != null && __instance.LeaderParty != null && __instance.LeaderParty.IsMainParty;
						TaskManager instance = TaskManager.Instance;
						bool flag4 = instance == null;
						if (flag4)
						{
							result = true;
						}
						else
						{
							HeroTask activeTask = instance.GetActiveTask(leaderHero);
							bool flag5 = activeTask != null && activeTask.IsActive();
							if (flag5)
							{
								bool flag6 = flag3;
								if (flag6)
								{
									instance.CancelTask(leaderHero);
									AIActionManager instance2 = AIActionManager.Instance;
									bool flag7 = instance2 != null;
									if (flag7)
									{
										instance2.StopAllActions(leaderHero);
									}
									AIInfluenceBehavior instance3 = AIInfluenceBehavior.Instance;
									if (instance3 != null)
									{
										instance3.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1665630752), leaderHero.Name));
									}
									result = true;
								}
								else
								{
									AIInfluenceBehavior instance4 = AIInfluenceBehavior.Instance;
									if (instance4 != null)
									{
										string format = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(965858747);
										object name = leaderHero.Name;
										object obj;
										if (__instance == null)
										{
											obj = null;
										}
										else
										{
											MobileParty leaderParty = __instance.LeaderParty;
											if (leaderParty == null)
											{
												obj = null;
											}
											else
											{
												Hero leaderHero2 = leaderParty.LeaderHero;
												if (leaderHero2 == null)
												{
													obj = null;
												}
												else
												{
													TextObject name2 = leaderHero2.Name;
													obj = ((name2 != null) ? name2.ToString() : null);
												}
											}
										}
										instance4.LogMessage(string.Format(format, name, obj ?? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1604186482), activeTask.Description ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-928050822)));
									}
									result = false;
								}
							}
							else
							{
								result = true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				AIInfluenceBehavior instance5 = AIInfluenceBehavior.Instance;
				if (instance5 != null)
				{
					instance5.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1539947040) + ex.Message);
				}
				result = true;
			}
			return result;
		}
	}
}

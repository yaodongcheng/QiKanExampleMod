using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MCM.Abstractions.Base.Global;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace AIInfluence.Diplomacy
{
	// Token: 0x02000207 RID: 519
	public static class DiplomacyPatches
	{
		// Token: 0x06000FAD RID: 4013 RVA: 0x000FE288 File Offset: 0x000FC488
		public static void WithBypass(Action action)
		{
			DiplomacyPatches._bypassCounter++;
			try
			{
				if (action != null)
				{
					action();
				}
			}
			finally
			{
				DiplomacyPatches._bypassCounter--;
			}
		}

		// Token: 0x06000FAE RID: 4014 RVA: 0x000FE2D4 File Offset: 0x000FC4D4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void TryPatchMethod(Harmony harmony, Type type, string methodName, MethodInfo prefix)
		{
			try
			{
				MethodInfo methodInfo = AccessTools.Method(type, methodName, null, null);
				bool flag = methodInfo != null && prefix != null;
				if (flag)
				{
					harmony.Patch(methodInfo, new HarmonyMethod(prefix), null, null, null);
					DiplomacyPatches.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(151292760) + type.Name + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-905337654) + methodName);
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(string.Concat(new string[]
				{
					<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-799094250),
					(type != null) ? type.Name : null,
					<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1768135376),
					methodName,
					<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(291623514),
					ex.Message
				}));
			}
		}

		// Token: 0x06000FAF RID: 4015 RVA: 0x000FE3B4 File Offset: 0x000FC5B4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void CalculateClanGoldChange_Postfix(Clan clan, bool includeDescriptions, bool applyWithdrawals, bool includeDetails, ref ExplainedNumber __result)
		{
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy || !includeDescriptions || clan == null;
				if (!flag)
				{
					bool flag2 = clan != Clan.PlayerClan;
					if (!flag2)
					{
						Kingdom kingdom = clan.Kingdom;
						bool flag3 = kingdom == null;
						if (!flag3)
						{
							TributeSystem instance = TributeSystem.Instance;
							bool flag4 = instance == null;
							if (!flag4)
							{
								List<TributeAgreement> tributesPaidBy = instance.GetTributesPaidBy(kingdom);
								List<TributeAgreement> tributesReceivedBy = instance.GetTributesReceivedBy(kingdom);
								bool flag5 = tributesPaidBy != null && tributesPaidBy.Count > 0;
								bool flag6 = tributesReceivedBy != null && tributesReceivedBy.Count > 0;
								bool flag7 = !flag5 && !flag6;
								if (!flag7)
								{
									bool flag8 = flag5;
									if (flag8)
									{
										using (List<TributeAgreement>.Enumerator enumerator = tributesPaidBy.GetEnumerator())
										{
											while (enumerator.MoveNext())
											{
												TributeAgreement tribute = enumerator.Current;
												Kingdom kingdom2 = Kingdom.All.Find((Kingdom k) => k.StringId == tribute.ReceiverKingdomId);
												TextObject textObject = new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1595399138), null);
												textObject.SetTextVariable(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1915857409), (kingdom2 != null) ? kingdom2.Name : TextObject.GetEmpty());
												bool flag9 = tribute.DailyAmount > 0;
												if (flag9)
												{
													__result.Add(-(float)tribute.DailyAmount, textObject, null);
												}
											}
										}
									}
									bool flag10 = flag6;
									if (flag10)
									{
										using (List<TributeAgreement>.Enumerator enumerator2 = tributesReceivedBy.GetEnumerator())
										{
											while (enumerator2.MoveNext())
											{
												TributeAgreement tribute = enumerator2.Current;
												Kingdom kingdom3 = Kingdom.All.Find((Kingdom k) => k.StringId == tribute.PayerKingdomId);
												TextObject textObject2 = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-893017131), null);
												textObject2.SetTextVariable(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1523719272), (kingdom3 != null) ? kingdom3.Name : TextObject.GetEmpty());
												bool flag11 = tribute.DailyAmount > 0;
												if (flag11)
												{
													__result.Add((float)tribute.DailyAmount, textObject2, null);
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
				DiplomacyPatches.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(616308055) + ex.Message);
			}
		}

		// Token: 0x06000FB0 RID: 4016 RVA: 0x000FE684 File Offset: 0x000FC884
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void ApplyPatches(Harmony harmony)
		{
			bool patchesApplied = DiplomacyPatches._patchesApplied;
			if (patchesApplied)
			{
				DiplomacyPatches.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(538561663));
			}
			else
			{
				try
				{
					DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1611298789));
					MethodInfo methodInfo = AccessTools.Method(typeof(KingdomDecisionProposalBehavior), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(398485509), null, null);
					MethodInfo methodInfo2 = AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-109332935), null, null);
					bool flag = methodInfo != null && methodInfo2 != null;
					if (flag)
					{
						harmony.Patch(methodInfo, new HarmonyMethod(methodInfo2), null, null, null);
						DiplomacyPatches.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1690700749));
					}
					else
					{
						DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1830630878));
					}
					MethodInfo methodInfo3 = AccessTools.Method(typeof(KingdomDecisionProposalBehavior), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-380447138), null, null);
					MethodInfo methodInfo4 = null;
					bool flag2 = methodInfo3 != null;
					if (flag2)
					{
						ParameterInfo[] parameters = methodInfo3.GetParameters();
						bool flag3 = parameters.Length == 4;
						if (flag3)
						{
							methodInfo4 = AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1795347462), null, null);
							DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(441464465));
						}
						else
						{
							bool flag4 = parameters.Length == 3;
							if (flag4)
							{
								methodInfo4 = AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(777647998), null, null);
								DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1113538481));
							}
							else
							{
								DiplomacyPatches.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(276953299), parameters.Length));
							}
						}
					}
					bool flag5 = methodInfo3 != null && methodInfo4 != null;
					if (flag5)
					{
						harmony.Patch(methodInfo3, new HarmonyMethod(methodInfo4), null, null, null);
						DiplomacyPatches.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1687017918));
					}
					else
					{
						DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(310545340));
					}
					MethodInfo methodInfo5 = AccessTools.Method(typeof(KingdomDecisionProposalBehavior), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1610109528), null, null);
					MethodInfo methodInfo6 = AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(712898651), null, null);
					bool flag6 = methodInfo5 != null && methodInfo6 != null;
					if (flag6)
					{
						harmony.Patch(methodInfo5, new HarmonyMethod(methodInfo6), null, null, null);
						DiplomacyPatches.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-174985769));
					}
					else
					{
						DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(140230062));
					}
					DiplomacyPatches.TryPatchMethod(harmony, typeof(DeclareWarAction), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1952012997), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1896035295), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(DeclareWarAction), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1453898065), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1009786829), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(DeclareWarAction), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(461340600), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1896035295), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(MakePeaceAction), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1783214613), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1016734333), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(MakePeaceAction), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1125804750), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(52021522), null, null));
					Type type = Type.GetType(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(777367085));
					bool flag7 = type != null;
					if (flag7)
					{
						DiplomacyPatches.TryPatchMethod(harmony, type, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1426678798), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1315795730), null, null));
						DiplomacyPatches.TryPatchMethod(harmony, type, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-458484100), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-213537920), null, null));
					}
					else
					{
						DiplomacyPatches.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(744023032));
					}
					Type type2 = Type.GetType(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1466372641));
					bool flag8 = type2 != null;
					if (flag8)
					{
						DiplomacyPatches.TryPatchMethod(harmony, type2, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1103192850), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-983054253), null, null));
						DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1923416134));
					}
					else
					{
						DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1732994138));
					}
					DiplomacyPatches.TryPatchMethod(harmony, typeof(DeclareWarDecision), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1040388168), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2115661492), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(MakePeaceKingdomDecision), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1755046273), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1632508049), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(StartAllianceDecision), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1755046273), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1632508049), null, null));
					DiplomacyPatches.TryPatchMethod(harmony, typeof(TradeAgreementDecision), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1032829985), AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-346912785), null, null));
					try
					{
						MethodInfo methodInfo7 = AccessTools.Method(typeof(DefaultClanFinanceModel), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(814619258), null, null);
						MethodInfo methodInfo8 = AccessTools.Method(typeof(DiplomacyPatches), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1821202678), null, null);
						bool flag9 = methodInfo7 != null && methodInfo8 != null;
						if (flag9)
						{
							harmony.Patch(methodInfo7, null, new HarmonyMethod(methodInfo8), null, null);
							DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-399837909));
						}
						else
						{
							DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2008336465));
						}
					}
					catch (Exception ex)
					{
						DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1799266093) + ex.Message);
					}
					DiplomacyPatches._patchesApplied = true;
					DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(289508632));
				}
				catch (Exception ex2)
				{
					DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-939426841) + ex2.Message);
					DiplomacyPatches.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1355563128) + ex2.StackTrace);
				}
			}
		}

		// Token: 0x06000FB1 RID: 4017 RVA: 0x000FED98 File Offset: 0x000FCF98
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool ConsiderWar_Prefix(Clan clan, Kingdom kingdom, IFaction otherFaction, ref bool __result)
		{
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					bool flag2 = kingdom == null || otherFaction == null || !otherFaction.IsKingdomFaction;
					if (flag2)
					{
						result = true;
					}
					else
					{
						DiplomacyPatches.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1839074818), kingdom.Name, otherFaction.Name));
						__result = false;
						result = false;
					}
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1850645109) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x06000FB2 RID: 4018 RVA: 0x000FEE38 File Offset: 0x000FD038
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool ConsiderPeace_Prefix(Clan clan, IFaction otherFaction, out MakePeaceKingdomDecision decision, ref bool __result)
		{
			decision = null;
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					Kingdom kingdom = (clan != null) ? clan.Kingdom : null;
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(836288926), (kingdom != null) ? kingdom.Name : null, (otherFaction != null) ? otherFaction.Name : null));
					__result = false;
					result = false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-638000967) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x06000FB3 RID: 4019 RVA: 0x000FEED8 File Offset: 0x000FD0D8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool ConsiderPeace_132_Prefix(Clan clan, Clan otherClan, IFaction otherFaction, out MakePeaceKingdomDecision decision, ref bool __result)
		{
			decision = null;
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					Kingdom kingdom = (clan != null) ? clan.Kingdom : null;
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-235601994), (kingdom != null) ? kingdom.Name : null, (otherFaction != null) ? otherFaction.Name : null));
					__result = false;
					result = false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1541090259) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x06000FB4 RID: 4020 RVA: 0x000FEF78 File Offset: 0x000FD178
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool ConsiderTradeAgreement_Prefix(Clan clan, Kingdom kingdom, Kingdom otherKingdom, ref bool __result)
		{
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1523707337), (kingdom != null) ? kingdom.Name : null, (otherKingdom != null) ? otherKingdom.Name : null));
					__result = false;
					result = false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1601494356) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x06000FB5 RID: 4021 RVA: 0x0001E836 File Offset: 0x0001CA36
		private static void LogMessage(string message)
		{
			AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
			if (instance != null)
			{
				instance.LogMessage(message);
			}
		}

		// Token: 0x06000FB6 RID: 4022 RVA: 0x000FF008 File Offset: 0x000FD208
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool DeclareWarAction_Prefix(IFaction faction1, IFaction faction2)
		{
			try
			{
				bool flag = DiplomacyPatches._bypassCounter > 0;
				if (flag)
				{
					return true;
				}
				bool flag2 = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag2)
				{
					return true;
				}
				bool flag3 = faction1 != null && faction1.IsKingdomFaction && faction2 != null && faction2.IsKingdomFaction;
				bool flag4 = !flag3;
				if (flag4)
				{
					return true;
				}
				bool flag5 = faction1 == Hero.MainHero.MapFaction || faction2 == Hero.MainHero.MapFaction;
				bool flag6 = !flag5;
				if (flag6)
				{
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1416561526), (faction1 != null) ? faction1.Name : null, (faction2 != null) ? faction2.Name : null));
					return false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(206023576) + ex.Message);
			}
			return true;
		}

		// Token: 0x06000FB7 RID: 4023 RVA: 0x000FF108 File Offset: 0x000FD308
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool MakePeaceAction_Prefix(IFaction faction1, IFaction faction2)
		{
			try
			{
				bool flag = DiplomacyPatches._bypassCounter > 0;
				if (flag)
				{
					return true;
				}
				bool flag2 = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag2)
				{
					return true;
				}
				bool flag3 = faction1 == Hero.MainHero.MapFaction || faction2 == Hero.MainHero.MapFaction;
				bool flag4 = !flag3;
				if (flag4)
				{
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1211182141), (faction1 != null) ? faction1.Name : null, (faction2 != null) ? faction2.Name : null));
					return false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2023901064) + ex.Message);
			}
			return true;
		}

		// Token: 0x06000FB8 RID: 4024 RVA: 0x000FF1DC File Offset: 0x000FD3DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool StartAlliance_Prefix(Kingdom proposerKingdom, Kingdom receiverKingdom)
		{
			try
			{
				bool flag = DiplomacyPatches._bypassCounter > 0;
				if (flag)
				{
					return true;
				}
				bool flag2 = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag2)
				{
					return true;
				}
				bool flag3 = proposerKingdom == Hero.MainHero.MapFaction || receiverKingdom == Hero.MainHero.MapFaction;
				bool flag4 = !flag3;
				if (flag4)
				{
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1350436008), (proposerKingdom != null) ? proposerKingdom.Name : null, (receiverKingdom != null) ? receiverKingdom.Name : null));
					return false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1881259491) + ex.Message);
			}
			return true;
		}

		// Token: 0x06000FB9 RID: 4025 RVA: 0x000FF2B0 File Offset: 0x000FD4B0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool EndAlliance_Prefix(Kingdom kingdom1, Kingdom kingdom2)
		{
			try
			{
				bool flag = DiplomacyPatches._bypassCounter > 0;
				if (flag)
				{
					return true;
				}
				bool flag2 = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag2)
				{
					return true;
				}
				bool flag3 = kingdom1 == Hero.MainHero.MapFaction || kingdom2 == Hero.MainHero.MapFaction;
				bool flag4 = !flag3;
				if (flag4)
				{
					DiplomacyPatches.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(103894937), (kingdom1 != null) ? kingdom1.Name : null, (kingdom2 != null) ? kingdom2.Name : null));
					return false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2029703580) + ex.Message);
			}
			return true;
		}

		// Token: 0x06000FBA RID: 4026 RVA: 0x000FF384 File Offset: 0x000FD584
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool GetAreProposalActionsEnabledWithReason_Prefix(float actionInfluenceCost, ref TextObject disabledReason, ref bool __result)
		{
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableModification || !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					disabledReason = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-962042691), null);
					__result = false;
					DiplomacyPatches.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1138214924));
					result = false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2105582117) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x06000FBB RID: 4027 RVA: 0x000FF41C File Offset: 0x000FD61C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool DiplomaticDecision_OnShowDecision_Prefix(KingdomDecision __instance, ref bool __result)
		{
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableModification || !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					string name = __instance.GetType().Name;
					DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1572062345) + name + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(639149630));
					__result = false;
					result = false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(15939731) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x06000FBC RID: 4028 RVA: 0x000FF4BC File Offset: 0x000FD6BC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool TradeAgreementDecision_OnShowDecision_Prefix(TradeAgreementDecision __instance, ref bool __result)
		{
			bool result;
			try
			{
				bool flag = !GlobalSettings<ModSettings>.Instance.EnableModification || !GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					result = true;
				}
				else
				{
					DiplomacyPatches.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1750454590));
					__result = false;
					result = false;
				}
			}
			catch (Exception ex)
			{
				DiplomacyPatches.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2019080104) + ex.Message);
				result = true;
			}
			return result;
		}

		// Token: 0x04000A76 RID: 2678
		private static bool _patchesApplied;

		// Token: 0x04000A77 RID: 2679
		private static int _bypassCounter;
	}
}

using System;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x0200031C RID: 796
	public static class TaskStepExecutor
	{
		// Token: 0x06001670 RID: 5744 RVA: 0x001522BC File Offset: 0x001504BC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void ExecuteNextTaskStep(Hero hero, TaskStep nextStep)
		{
			bool flag;
			if (hero == null)
			{
				flag = true;
				goto IL_5D;
			}
			IL_04:
			int num = 208397818;
			IL_09:
			int num2 = num;
			switch ((944412664 - -(num2 ^ ~(~1447346323) + (-1432409030 + -687046421 - (6114810 ^ -1955050693))) * -631008483) % 4)
			{
			case 1:
				flag = (nextStep == null);
				goto IL_5D;
			case 2:
				goto IL_04;
			case 3:
				return;
			}
			try
			{
				Settlement targetSettlement;
				int num3;
				Settlement targetSettlement2;
				Settlement targetSettlement3;
				switch (nextStep.StepType)
				{
				case TaskStepType.GoToSettlement:
				case TaskStepType.WaitInSettlement:
					IL_40C:
					targetSettlement = nextStep.GetTargetSettlement();
					num3 = ((targetSettlement != null) ? -30757069 : 1938389206);
					break;
				case TaskStepType.ReturnToPlayer:
					IL_254:
					AIActionManager.Instance.StartAction(hero, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-433153296), false);
					num3 = -2018218517;
					break;
				case TaskStepType.FollowPlayer:
					IL_1AA:
					AIActionManager.Instance.StartAction(hero, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1791229499), false);
					num3 = -1421301881;
					break;
				case TaskStepType.AttackParty:
				{
					IL_4F7:
					bool flag2 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(nextStep.TargetPartyId);
					num3 = ((!flag2) ? -1462510136 : 467802944);
					break;
				}
				case TaskStepType.SiegeSettlement:
					IL_3DF:
					num3 = ((!\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(nextStep.TargetSettlementId)) ? -358004696 : -1892764400);
					break;
				case TaskStepType.PatrolSettlement:
				{
					IL_3B7:
					targetSettlement2 = nextStep.GetTargetSettlement();
					bool flag3 = targetSettlement2 != null;
					num3 = ((!flag3) ? 895419533 : -879475128);
					break;
				}
				case TaskStepType.WaitNearSettlement:
				{
					IL_209:
					targetSettlement3 = nextStep.GetTargetSettlement();
					bool flag4 = targetSettlement3 != null;
					num3 = ((!flag4) ? 1876641122 : -433747309);
					break;
				}
				case TaskStepType.RaidVillage:
					IL_1DC:
					num3 = ((!\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(nextStep.TargetSettlementId)) ? 1087502402 : -2107508296);
					break;
				case TaskStepType.Custom:
					goto IL_522;
				default:
					IL_C1:
					num3 = 165203739;
					break;
				}
				for (;;)
				{
					num2 = num3;
					uint num4;
					float num5;
					float num6;
					float num7;
					switch ((num4 = (uint)(944412664 - -(num2 ^ ~(~1447346323) + (-1432409030 + -687046421 - (6114810 ^ -1955050693))) * -631008483)) % 28U)
					{
					case 0U:
						goto IL_C1;
					case 1U:
						goto IL_1AA;
					case 2U:
						goto IL_3DF;
					case 3U:
						AIActionManager.Instance.StartAction(hero, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1617932017), false);
						num3 = -1421301881;
						continue;
					case 4U:
						if (nextStep.WaitNearDurationDays <= 0f)
						{
							num3 = (int)(num4 * 3144911744U ^ 3943445065U);
							continue;
						}
						num5 = nextStep.WaitNearDurationDays;
						goto IL_454;
					case 5U:
						RaidVillageAction.PrepareRaidTarget(hero, nextStep.TargetSettlementId);
						num3 = (int)(num4 * 386939204U ^ 1919134876U);
						continue;
					case 6U:
						goto IL_40C;
					case 7U:
						AIActionManager.Instance.StartAction(hero, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-41546607), false);
						num3 = -1421301881;
						continue;
					case 8U:
						num3 = (int)(num4 * 1122298676U ^ 3535785175U);
						continue;
					case 9U:
						goto IL_4F7;
					case 11U:
						goto IL_254;
					case 14U:
						num6 = 10f;
						goto IL_33D;
					case 15U:
						SiegeSettlementAction.PrepareSiegeTarget(hero, nextStep.TargetSettlementId, false);
						num3 = (int)(num4 * 671247598U ^ 3513731018U);
						continue;
					case 16U:
						num3 = (int)(num4 * 3362196673U ^ 3407302886U);
						continue;
					case 18U:
						num5 = 2f;
						goto IL_454;
					case 19U:
						AIActionManager.Instance.StartAction(hero, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-481678347), false);
						num3 = -1421301881;
						continue;
					case 20U:
						goto IL_1DC;
					case 21U:
						goto IL_3B7;
					case 22U:
						goto IL_209;
					case 23U:
						if (nextStep.PatrolDurationDays <= 0f)
						{
							num3 = (int)(num4 * 556766782U ^ 643914483U);
							continue;
						}
						num7 = nextStep.PatrolDurationDays;
						goto IL_4B2;
					case 24U:
						GoToSettlementAction.PrepareDestination(hero, targetSettlement, 3f);
						AIActionManager.Instance.StartAction(hero, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1628643870), false);
						num3 = 1938389206;
						continue;
					case 26U:
						num7 = 7f;
						goto IL_4B2;
					case 27U:
						AttackPartyAction.PrepareAttackTarget(hero, nextStep.TargetPartyId);
						num3 = (int)(num4 * 399833903U ^ 2736867861U);
						continue;
					}
					break;
					IL_33D:
					float desiredRadius = num6;
					float waitDays;
					WaitNearSettlementAction.PrepareWaitRequest(hero, targetSettlement3, waitDays, desiredRadius);
					AIActionManager.Instance.StartAction(hero, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1157104545), false);
					num3 = 1876641122;
					continue;
					IL_454:
					waitDays = num5;
					if (nextStep.WaitNearRadius <= 0f)
					{
						num3 = -1821573563;
						continue;
					}
					num6 = nextStep.WaitNearRadius;
					goto IL_33D;
					IL_4B2:
					float durationDays = num7;
					bool patrolAutoReturn = nextStep.PatrolAutoReturn;
					PatrolSettlementAction.PreparePatrolRequest(hero, \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(targetSettlement2), durationDays, patrolAutoReturn);
					AIActionManager.Instance.StartAction(hero, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1002932543), false);
					num3 = 895419533;
				}
				IL_522:;
			}
			catch (Exception ex)
			{
				AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
				if (instance == null)
				{
					goto IL_5BB;
				}
				instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1255406814), (hero != null) ? \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.\u0002ËfaÃ(hero) : null, \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)));
				IL_56D:
				int num8 = 803651610;
				IL_572:
				num2 = num8;
				switch ((944412664 - -(num2 ^ ~(~1447346323) + (-1432409030 + -687046421 - (6114810 ^ -1955050693))) * -631008483) % 3)
				{
				case 0:
					goto IL_56D;
				case 2:
					IL_5BB:
					num8 = -784358403;
					goto IL_572;
				}
			}
			return;
			IL_5D:
			num = (flag ? 965080600 : 1229498075);
			goto IL_09;
		}
	}
}

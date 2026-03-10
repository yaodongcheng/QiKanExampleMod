\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1791229499), false);
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
						AIActionManager.Instance.StartAction(hero, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u20
					{
						case 0U:
							goto IL_1F7;
						case 1U:
						{
							TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E;
							this._activeTasks.Remove(u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E.Key);
							num = (int)(num3 * 3393279642U ^ 3453498071U);
							continue;
						}
						case 2U:
						{
							TaskStep currentStep;
							if (currentStep.StepType == TaskStepType.WaitNearSettlement)
							{
								num = (int)(num3 * 3718061482U ^ 2586367486U);
								continue;
							}
							goto IL_17A;
						}
						case 3U:
						{
							HeroTask value;
							num = (int)(((!value.IsActive()) ? 314138310U : 4184945060U) ^ num3 * 2551845174U);
							continue;
						}
						case 4U:
						{
							TaskStep currentStep;
							flag = (currentStep.WaitUntilTime == null);
							goto IL_17B;
						}
						case 5U:
						{
							TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\
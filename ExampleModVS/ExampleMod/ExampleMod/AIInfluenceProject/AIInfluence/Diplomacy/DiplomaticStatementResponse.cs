u206F\u200F\u206D\u202C\u206B\u206C\u206B\u200D\u206B\u202C\u202E != null)
			{
				num2 = 1697888173;
				goto IL_3D;
			}
			flag2 = true;
			IL_F6:
			bool flag3 = flag2;
			num2 = ((!flag3) ? 1499599217 : 1565168815);
			goto IL_3D;
		}

		// Token: 0x060015A9 RID: 5545 RVA: 0x0014A46C File Offset: 0x0014866C
		private string \u200F\u206A\u202E\u200C\u206B\u202D\u200B\u202B\u202D\u206E\u200D\u206A\u206A\u200D\u200F\u200D\u202A\u202E\u206F\u202E\u200C\u202A\u200C\u202E\u206E\u206F\u206B\u206B\u202B\u206B\u200E\u202B\u206C\u206D\u206E\u200F\u202C\u206A\u202A\u200D\u202E()
		{
			TaskManager instance = TaskManager.Instance;
			HeroTask heroTask = (instance != null) ? instance.GetActiveTask(base.TargetHero) : null;
			if (heroTask == null)
			{
				goto IL_1F;
			}
			TaskStep taskStep = heroTask.GetCurrentStep();
			goto IL_AC;
			int num2;
			TaskStep taskStep2;
			string result;
			for (;;)
			{
				IL_24:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-1863914426 - -(uint)(~num + (--1233725874 ^ -100964233)))) % 6U)
				{
				case 1U:
					result = taskStep2.TargetSettlementId;
					num2 = (int)(num3 * 2230603311U ^ 675927753U);
					continue;
				case 2U:
					goto IL_A3;
				case 3U:
					goto IL_6A;
				case 4U:
					goto IL_1F;
				case 5U:
					result = null;
					num2 = 789241626;
					continue;
				}
				break;
			}
			return result;
			IL_6A:
			bool flag = taskStep2.StepType == TaskStepType.RaidVillage;
			goto IL_76;
			IL_A3:
			taskStep = null;
			goto IL_AC;
			IL_1F:
			num2 = 31561624;
			goto IL_24;
			IL_76:
			num2 = (flag ? -251816851 : -311976161);
			goto IL_24;
			IL_AC:
			taskStep2 = taskStep;
			if (taskStep2 != null)
			{
				num2 = -752217813;
				goto IL_24;
			}
			flag = false;
			goto IL_76;
		}

		// Token: 0x060015AA RID: 5546 RVA: 0x0014A534 File Offset: 0x00148734
		private Settlement \u202B\u202E\u206E\u202D\u200E\u200F\u206F\u206F\u200B\u200F\u200B\u206F\u206D\u200F\u200E\u206F\u206C\u202C\u200B\u202B\u202C\u206C\u202E\u206E\u206E\u202D\u206F\u206E\u200E\u202C\u206A\u206C\u206E\u206D\u202E\u200E\u200B\u202C\u200E\u202D\u202E(string A_1)
		{
			RaidVillageAction.\u200F\u206B\u202D\u200F\u206F\u202E\u206D\u206B\u200C\u202A\u200C\u202C\u202D\u206F\u202C\u202B\u206A\u200F\u202B\u206F\u206B\u200B\u206C\u202A\u206D\u206C\u206C\u200B\u200C\u206A\u202B\u206E\u202B\u202E\u202E\u202D\u202C\u200F\u202D\u202C\u202E u200F_u206B_u202D_u200F_u206F_u202E_u206D_u206B_u200C_u202A_u200C_u202C_u202D_u206F_u202C_u202B_u206A_u200F_u202B_u206F_u206B_u200B_u206C_u202A_u206D_u206C_u206C_u200B_u200C_u206A_u202B_u206E_u202B_u202E_u202E_u202D_u202C_u200F_u202D_u202C_u202E = new RaidVillageAction.\u200F\u206B\u202D\u200F\u206F\u202E\u206D\u206B\u200C\u202A\u200C\u202C\u202D\u206F\u202C\u202B\u206A\u200F\u202B\u206F\u206B\u200B\u206C\u202A\u206D\u206C\u206C\u200B\u200C\u206A\u202B\u206E\u202B\u202E\u202E\u202D\u20
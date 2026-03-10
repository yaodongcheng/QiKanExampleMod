) + -1589508204)) ^ ~707529589) * -1486081565)) % 9U)
				{
				case 0U:
					goto IL_7D;
				case 1U:
				{
					TaskStep currentStep = activeTask.GetCurrentStep();
					TaskStepExecutor.ExecuteNextTaskStep(base.TargetHero, currentStep);
					num2 = (int)(num3 * 1028618493U ^ 2629887922U);
					continue;
				}
				case 3U:
					goto IL_CF;
				case 4U:
					num2 = (int)(num3 * 3709272975U ^ 4225089832U);
					continue;
				case 5U:
					goto IL_14;
				case 6U:
					goto IL_E9;
				case 7U:
					num2 = (instance.MoveToNextStep(base.TargetHero) ? 620434496 : -1273700860);
					continue;
				case 8U:
					num2 = (int)(num3 * 758314439U ^ 2970110603U);
					continue;
				}
				break;
			}
			return;
			IL_7D:
			TaskStep taskStep = null;
			goto IL_86;
			IL_E9:
			TaskStep taskStep2;
			bool flag2 = taskStep2.StepType != TaskStepType.RaidVillage;
			goto IL_F8;
			IL_14:
			num2 = -866384486;
			goto IL_19;
			IL_86:
			taskStep2 = taskStep;
			if (taskStep2 != null)
			{
				num2 = -1936750414;
				goto IL_19;
			}
			flag2 = true;
			goto IL_F8;
			IL_CF:
			activeTask = instance.GetActiveTask(base.TargetHero);
			if (activeTask == null)
			{
				num2 = -1744899551;
				goto IL_19;
			}
			taskStep = activeTask.GetCurrentStep();
			goto IL_86;
			IL_F8:
			num2 = (flag2 ? 572512759 : 1872185243);
			goto IL_19;
		}

		// Token: 0x060015AC RID: 5548 RVA: 0x0014A728 File Offset: 0x00148928
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override Dictionary<string, string> GetStateDataForSave()
		{
			Dictionary<string, string> dictionary = base.GetStateDataForSave();
			bool flag = dictionary == null;
			if (flag)
			{
				goto IL_10;
			}
			goto IL_77;
			int num2;
			Dictionary<string, string> result;
			for (;;)
			{
				IL_15:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((num + (-363094217 ^ (-982692747 ^ 1982400511)) ^ 1954130920) + (-1519432515 + 1909974781))) % 5U)
				{
				case 0U:
					goto IL_10;
				case 1U:
					dictionary[<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-250574505)] = this.\u200B\u200C\u206A\u206F\u202C\u206D\u202B\u206B\u202D\u206E\u206E\u202B\u202C\u202A\u206F\u200F\u202D\u206B\u202E\u202C\u206C\u206E\u200B\u202A\u206E\u206F\u200F\u206A\u206F\u206F\u202B\u200C\u206C\u200F\u202E\u206E\u206D\u200D\u206F\u202E.ToString();
					result = dictionary;
					num2 = -1212422226;
					continue;
				case 3U:
					dictionary = new Dictionary<string, string>();
					num2 = (int)(num3 * 519806944U ^ 1579912419U);
					continue;
				case 4U:
					goto IL_77;
				}
				break;
			}
			return result;
			IL_10:
			num2 = -755700269;
			goto IL_15;
			IL_77:
			Dictionary<string, string> dictionary2 = dictionary;
			string key = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\
instance.LogMessage(\u202D\u200E\u206D\u206A\u200C\u200C\u206C\u202B\u206F\u202E\u202D\u202B\u206A\u200D\u206D\u206E\u202A\u206B\u202D\u200E\u206F\u206E\u200C\u206D\u202D\u206F\u200F\u206E\u206C\u202B\u206E\u206F\u206B\u202B\u202A\u206F\u206F\u200C\u200B\u202C\u202E.\u200C\u206A\u202D\u202E\u200B\u202C\u202D\u202D\u206F\u202A\u206D\u202D\u200C\u202A\u202A\u202A\u202B\u200D\u206B\u202D\u202C\u206C\u202D\u202D\u206B\u200F\u202E\u200D\u200E\u206A\u206F\u202D\u206A\u206A\u206B\u200E\u202A\u206F\u200F\u206B\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(216369203), new object[]
					{
						activeTask.TaskId,
						\u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero),
						activeTask.CurrentStepIndex + 1,
						activeTask.Steps.Count
					}));
					num2 = 412743186;
					continue;
				}
				case 2U:
					goto IL_14;
				case 3U:
					result = flag2;
					num2 = -243763;
					continue;
				case 4U:
					this.CompleteTask(hero);
					num2 = (int)(num3 * 4001743069U ^ 1051980141U);
					continue;
				case 5U:
					result = false;
					num2 = (int)(num3 * 1766010365U ^ 3998121332U);
					continue;
				case 6U:
					goto IL_8E;
				}
				break;
				IL_8E:
				num2 = 130636441;
			}
			return result;
			IL_14:
			num2 = -579339885;
			goto IL_19;
			IL_96:
			flag2 = activeTask.MoveToNextStep();
			bool flag3 = !flag2;
			num2 = ((!flag3) ? -1150278985 : 499867064);
			goto IL_19;
		}

		// Token: 0x0600163E RID: 5694 RVA: 0x00151420 File Offset: 0x0014F620
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnHourlyTick()
		{
			List<HeroTask> list = new List<HeroTask>(this._activeTasks.Values);
			using (List<HeroTask>.Enumerator enumerator = list.GetEnumerator())
			{
				for (;;)
				{
					IL_164:
					int num = (!enumerator.MoveNext()) ? -1805074128 : 951327626;
					for (;;)
					{
						int num2 = num;
						uint num3;
						Hero hero;
						bool flag2;
						switch ((num3 = (uint)(-(uint)((-629697965 - (num2 ^ -(-853790590 + -413209183 - (-162461911 - -291654494)))) * 552261363))) % 14U)
						{
						case 0U:
						{
							HeroTask heroTask;
							hero = heroTask.GetHero();
							bool flag = hero != null;
							num = (int)(((!flag) ? 2888476044U : 3834563643U) ^ num3 * 2297961954U);
							continue;
						}
						case 1U:
							num = 1081029378;
							continue;
						case 2U:
							num = (int)(num3 * 2186744059U ^ 1061983590U);
							continue;
						case 4U:
							num = 951327626;
							continue;
						case 5U:
							goto IL_164;
						case 6U:
						{
							TaskStep currentStep;
							flag2 = (currentStep.StepType == TaskStepType.WaitInSettlement);
							goto IL_1A0;
						}
						case 7U:
						{
							TaskStep currentStep;
							num = (int)((currentStep.IsWaitCompleted() ? 1885717424U : 3297782639U) ^ num3 * 2918882937U);
							continue;
						}
						case 8U:
						{
							AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
							if (instance == null)
							{
								goto IL_1F6;
							}
							HeroTask heroTask;
							instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1639095247), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero), heroTask.CurrentStepIndex + 1));
							num = -961653909;
							continue;
						}
						case 9U:
							num = 183554909;
							continue;
						case 10U:
							goto IL_1F6;
						case 11U:
						{
							HeroTask heroTask = enumerator.Current;
							bool flag3 = !heroTask.IsActive();
							num = ((!flag3) ? 729452868 : -1617797857);
							continue;
						}
						case 12U:
							num = 7517262;
							continue;
						case 13U:
						{
							HeroTask heroTask;
							TaskStep currentStep = heroTask.GetCurrentStep();
							if (currentStep != null)
							{
								num = 2124953197;
								continue;
							}
							flag2 = false;
							goto IL_1A0;
						}
						}
						goto Block_3;
						IL_1A0:
						bool flag4 = flag2;
						num = ((!flag4) ? 183554909 : -726460722);
						continue;
						IL_1F6:
						this.MoveToNextStep(hero);
						num = -758358808;
					}
				}
				Block_3:;
			}
		}

		// Token: 0x0600163F RID: 5695 RVA: 0x000417F4 File Offset: 0x0003F9F4
		private void OnDailyTick()
		{
		}

		// Token: 0x06001640 RID: 5696 RVA: 0x00151664 File Offset: 0x0014F864
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RestoreTasksAfterLoad()
		{
			AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
			if (instance != null)
			{
				instance.LogMessage(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E./\u000FÙü\u0008(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1769757270), this._activeTasks.Count));
			}
			using (List<KeyValuePair<string, HeroTask>>.Enumerator enumerator = Enumerable.ToList<KeyValuePair<string, HeroTask>>(this._activeTasks).GetEnumerator())
			{
				for (;;)
				{
					IL_2F5:
					int num = (!enumerator.MoveNext()) ? 674791548 : -1286868238;
					for (;;)
					{
						int num2 = num;
						uint num3;
						bool flag;
						switch ((num3 = (uint)(~(uint)(~(num2 - (2076157337 + -1200971951 + 773334045 * -1322237388 - (-1269533353 * 200006524 - (739991023 - -1855878059)))) - (-1806837018 - 456546845)))) % 17U)
	
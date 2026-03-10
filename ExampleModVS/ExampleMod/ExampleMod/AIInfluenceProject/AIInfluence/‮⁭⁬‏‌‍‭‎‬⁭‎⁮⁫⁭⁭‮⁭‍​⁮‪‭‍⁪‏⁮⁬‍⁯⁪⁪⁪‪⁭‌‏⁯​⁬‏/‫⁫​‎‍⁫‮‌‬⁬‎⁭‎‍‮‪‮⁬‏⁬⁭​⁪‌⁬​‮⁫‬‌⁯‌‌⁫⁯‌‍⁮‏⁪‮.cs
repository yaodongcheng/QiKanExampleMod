tlement;
					num2 = (int)(((!flag2) ? 279627412U : 607370422U) ^ num3 * 3972851813U);
					continue;
				}
				case 5U:
				{
					HeroTask heroTask;
					result = heroTask;
					num2 = 205266891;
					continue;
				}
				case 6U:
					goto IL_11;
				case 7U:
				{
					HeroTask heroTask;
					heroTask.Steps.AddRange(this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E);
					bool flag3 = heroTask.Steps.Count > 0;
					num2 = ((!flag3) ? 1071314633 : 530822332);
					continue;
				}
				case 8U:
					num2 = 1071314633;
					continue;
				case 9U:
					result = null;
					num2 = (int)(num3 * 2155634601U ^ 2084684726U);
					continue;
				case 10U:
				{
					HeroTask heroTask;
					heroTask.Steps[0].StartWaiting();
					num2 = (int)(num3 * 1621127549U ^ 476866442U);
					continue;
				}
				case 11U:
					result = null;
					num2 = (int)(num3 * 627721500U ^ 4154876927U);
					continue;
				}
				break;
			}
			return result;
			IL_11:
			num2 = 1036561077;
			goto IL_16;
			IL_183:
			instance = TaskManager.Instance;
			bool flag4 = instance == null;
			num2 = ((!flag4) ? 1236646992 : 72697274);
			goto IL_16;
		}

		// Token: 0x06001632 RID: 5682 RVA: 0x001507D0 File Offset: 0x0014E9D0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static HeroTask CreateGoToSettlementAndWait(Hero hero, Settlement settlement, float waitDays, string description = null)
		{
			return new TaskBuilder(hero).WithDescription(description ?? \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(959515075), (settlement != null) ? \u200F\u202A\u200F\u200D\u202
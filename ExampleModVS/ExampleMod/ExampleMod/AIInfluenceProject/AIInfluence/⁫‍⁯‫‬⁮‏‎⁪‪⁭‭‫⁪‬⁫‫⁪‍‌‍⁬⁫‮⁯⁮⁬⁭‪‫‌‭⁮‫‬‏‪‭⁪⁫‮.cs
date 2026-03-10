				case 9U:
					goto IL_0C;
				}
				break;
				IL_78:
				bool flag2 = !this._completedTasks.ContainsKey(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
				num2 = ((!flag2) ? 1032120954 : 1554024520);
			}
			return result;
			IL_0C:
			num2 = -261054623;
			goto IL_11;
			IL_15D:
			bool flag3 = this._activeTasks.TryGetValue(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), out heroTask);
			num2 = ((!flag3) ? 80125266 : 801268012);
			goto IL_11;
		}

		// Token: 0x0600163C RID: 5692 RVA: 0x001510DC File Offset: 0x0014F2DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void CompleteTask(Hero hero)
		{
			bool flag = hero == null;
			if (flag)
			{
				goto IL_0C;
			}
			goto IL_1C2;
			int num2;
			HeroTask heroTask;
			for (;;)
			{
				IL_11:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((943101068 - 210947230) * -2121435613 + (-1053186406 * 1746774745 + --1072708029) - num + ~(~-1202929215) - -477428302 ^ -321203581)) % 10U)
				{
				case 0U:
					heroTask.Status = TaskStatus.Completed;
					num2 = (int)(num3 * 2759375430U ^ 4285260566U);
					continue;
				case 1U:
					num2 = (int)(num3 * 3407858980U ^ 2283762229U);
					continue;
				case 3U:
				{
					heroTask.CompletedTime = new CampaignTime?(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u
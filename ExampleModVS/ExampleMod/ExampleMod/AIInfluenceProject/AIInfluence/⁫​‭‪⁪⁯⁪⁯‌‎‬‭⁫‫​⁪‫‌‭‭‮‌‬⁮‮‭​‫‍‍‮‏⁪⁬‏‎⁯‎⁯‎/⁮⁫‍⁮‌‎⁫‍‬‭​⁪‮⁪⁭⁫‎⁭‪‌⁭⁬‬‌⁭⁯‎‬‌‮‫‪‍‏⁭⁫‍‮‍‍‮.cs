02A\u200F\u200F\u206B\u202A\u202C\u206C\u206D\u206A\u202E\u206C\u202D\u200B\u200B\u202D\u206F\u206D\u206C\u200F\u202E.Äf\u008EÐU(), this, new Action(this.OnDailyTick));
		}

		// Token: 0x06001638 RID: 5688 RVA: 0x00150A4C File Offset: 0x0014EC4C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void SyncData(IDataStore dataStore)
		{
			dataStore.SyncData<Dictionary<string, HeroTask>>(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1081901359), ref this._activeTasks);
			dataStore.SyncData<Dictionary<string, List<HeroTask>>>(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-687926793), ref this._completedTasks);
			bool flag = \u206D\u200B\u206D\u202A\u206A\u206A\u206D\u202E\u202B\u200E\u206D\u200E\u202A\u206B\u206E\u206E\u202E\u202C\u200E\u200E\u202C\u206D\u206F\u200C\u206A\u202B\u200C\u200E\u206C\u202D\u202E\u206D\u200E\u200D\u202E\u202E\u200C\u202B\u206A\u200D\u202E._&\\u0086n(dataStore);
			if (flag)
			{
				for (;;)
				{
					IL_42:
					int num = -251335953;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(uint)(~(-num2) * 232260261))) % 3U)
						{
						case 1U:
							this.RestoreTasksAfterLoad();
							num = (int)(num3 * 2921199646U ^ 3518617221U);
							continue;
						case 2U:
							goto IL_42;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06001639 RID: 5689 RVA: 0x00150ADC File Offset: 0x0014ECDC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public HeroTask CreateTask(Hero hero, string description = null)
		{
			HeroTask result;
			for (;;)
			{
				IL_01:
				int num = 414954790;
				for (;;)
				{
					int num2 = num;
					uint num3;
					bool flag3;
					bool flag4;
					HeroTask heroTask;
					switch ((num3 = (uint)(-(uint)(~(num2 ^ ~(-1645896943) + ~1487212508) + -817746839))) % 16U)
					{
					case 0U:
					{
						bool flag2;
						bool flag = flag2;
						num = ((!flag) ? -348075878 : 76293330);
						continue;
					}
					case 1U:
						result = null;
		
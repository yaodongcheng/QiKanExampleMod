 4U:
					goto IL_09;
				case 6U:
					result = heroTask;
					num2 = (int)(num3 * 4060881278U ^ 2759098707U);
					continue;
				}
				break;
			}
			return result;
			IL_CC:
			bool flag2 = heroTask.IsActive();
			goto IL_D5;
			IL_09:
			num2 = 1609181452;
			goto IL_0E;
			IL_7D:
			if (this._activeTasks.TryGetValue(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), out heroTask))
			{
				num2 = -1772008092;
				goto IL_0E;
			}
			flag2 = false;
			IL_D5:
			num2 = (flag2 ? 1091589760 : -17000556);
			goto IL_0E;
		}

		// Token: 0x0600163B RID: 5691 RVA: 0x00150F04 File Offset: 0x0014F104
		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool CancelTask(Hero hero)
		{
			bool flag = hero == null;
			if (flag)
			{
				goto IL_0C;
			}
			goto IL_15D;
			int num2;
			HeroTask heroTask;
			bool result;
			for (;;)
			{
				IL_11:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(2030945635 - (num - (-1508114511 + 297811717 - -1235305613 - (~-35378337 ^ -1127570313 + 1279874569))))) % 10U)
				{
				case 0U:
				{
					heroTask.Cancel();
					AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
					if (instance == null)
					{
						goto IL_78;
					}
					instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C
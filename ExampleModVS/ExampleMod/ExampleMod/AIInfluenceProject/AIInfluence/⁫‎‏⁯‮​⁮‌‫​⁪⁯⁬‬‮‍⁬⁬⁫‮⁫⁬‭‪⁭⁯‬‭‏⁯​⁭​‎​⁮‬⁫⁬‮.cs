206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ());
					AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
					if (instance == null)
					{
						goto IL_160;
					}
					instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1503694177), heroTask.TaskId, \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero)));
					num2 = -2119671583;
					continue;
				}
				case 4U:
					goto IL_160;
				case 5U:
					goto IL_0C;
				case 6U:
					this._completedTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)] = new List<HeroTask>();
					num2 = (int)(num3 * 3975101814U ^ 1464636488U);
					continue;
				case 7U:
					goto IL_1C2;
				case 8U:
					num2 = (int)(num3 * 1139552579U ^ 255107199U);
					continue;
				case 9U:
					this._completedTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)].Add(heroTask);
					this._activeTasks.Remove(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
					num2 = 919546399;
					continue;
				}
				break;
				IL_160:
				bool flag2 = !this._completedTasks.ContainsKey(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
				num2 = ((!flag2) ? 2025821064 : -1867461443);
			}
			return;
			IL_0C:
			num2 = 1464634616;
			goto IL_11;
			IL_1C2:
			num2 = (this._activeTasks.TryGetValue(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), out heroTask) ? 446930147 : 508845113);
			goto IL_11;
		}

		// Token: 0x0600163D RID: 5693 RVA: 0x001512DC File Offset: 0x0014F4DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool MoveToNextStep(Hero hero)
		{
			HeroTask activeTask = this.GetActiveTask(hero);
			bool flag = activeTask == null;
			if (flag)
			{
				goto IL_14;
			}
			goto IL_96;
			int num2;
			bool flag2;
			bool result;
			for (;;)
			{
				IL_19:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(uint)((num ^ -(~1569368227 - ~1189540525)) - 328755922))) % 8U)
				{
				case 0U:
					goto IL_96;
				case 1U:
				{
					AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
					if (instance == null)
					{
						goto IL_8E;
					}
					
97 RVA: 0x001519B0 File Offset: 0x0014FBB0
		public static void ResetInstance()
		{
			TaskManager._instance = null;
		}

		// Token: 0x06001642 RID: 5698 RVA: 0x001519C4 File Offset: 0x0014FBC4
		public static void SetInstance(TaskManager instance)
		{
			TaskManager._instance = instance;
		}

		// Token: 0x04000F98 RID: 3992
		private static TaskManager _instance;

		// Token: 0x04000F99 RID: 3993
		[SaveableField(1)]
		private Dictionary<string, HeroTask> _activeTasks;

		// Token: 0x04000F9A RID: 3994
		[SaveableField(2)]
		private Dictionary<string, List<HeroTask>> _completedTasks;

		// Token: 0x02000318 RID: 792
		[CompilerGenerated]
		private sealed class \u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E
		{
			// Token: 0x06001644 RID: 5700 RVA: 0x001519D8 File Offset: 0x0014FBD8
			internal bool \u206C\u206F\u200E\u200D\u202D\u206D\u206F\u200D\u200D\u202D\u206F\u206B\u206F\u206E\u206F\u200D\u200B\u202E\u200E\u202C\u206C\u202C\u206A\u206E\u206D\u202B\u202B\u200C\u202B\u206E\u202D\u206E\u206B\u200F\u200E\u206B\u202E\u206A\u200C\u206F\u202E(Hero A_1)
			{
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E.Key);
			}

			// Token: 0x04000F9B RID: 3995
			public KeyValuePair<string, HeroTask> \u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B
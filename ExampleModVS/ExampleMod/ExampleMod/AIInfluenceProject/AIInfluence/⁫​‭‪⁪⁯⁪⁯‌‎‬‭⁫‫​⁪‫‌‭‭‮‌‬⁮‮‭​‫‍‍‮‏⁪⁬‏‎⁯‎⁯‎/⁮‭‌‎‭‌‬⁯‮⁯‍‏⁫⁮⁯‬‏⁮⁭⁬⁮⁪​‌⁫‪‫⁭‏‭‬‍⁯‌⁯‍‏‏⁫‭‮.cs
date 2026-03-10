7 RID: 791
	public class TaskManager : CampaignBehaviorBase
	{
		// Token: 0x17000405 RID: 1029
		// (get) Token: 0x06001635 RID: 5685 RVA: 0x0015090C File Offset: 0x0014EB0C
		public static TaskManager Instance
		{
			get
			{
				bool flag = \u200E\u202D\u202C\u206F\u206A\u202A\u202A\u200F\u202D\u206B\u206C\u200E\u202B\u200C\u202E\u202B\u200E\u202E\u206E\u200C\u206C\u202E\u200F\u206C\u200D\u206B\u200C\u206D\u206B\u206D\u200B\u200B\u200B\u202A\u202E\u200B\u202C\u202C\u206D\u206C\u202E.ã`ÿ\u00ADÇ() != null;
				if (flag)
				{
					goto IL_12;
				}
				goto IL_48;
				int num2;
				TaskManager instance;
				for (;;)
				{
					IL_17:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(-(-(-num)) ^ -40828275)) % 6U)
					{
					case 0U:
					{
						TaskManager campaignBehavior;
						TaskManager._instance = campaignBehavior;
						instance = TaskManager._instance;
						num2 = (int)(num3 * 1776472444U ^ 3564746880U);
						continue;
					}
					case 1U:
					{
						TaskManager campaignBehavior = \u200E\u202D\u202C\u206F\u206A\u202A\u202A\u200F\u202D\u206B\u206C\u200E\u202B\u200C\u202E\u202B\u200E\u202E\u206E\u200C\u206C\u202E\u200F\u206C\u200D\u206B\u200C\u206D\u206B\u206D\u200B\u200B\u200B\u202A\u202E\u200B\u202C\u202C\u206D\u206C\u202E.ã`ÿ\u00ADÇ().GetCampaignBehavior<TaskManager>();
						num2 = (int)(((campaignBehavior != null) ? 4179239649U : 2609660849U) ^ num3 * 3153061896U);
						continue;
					}
					case 2U:
						goto IL_12;
					case 3U:
						goto IL_48;
					case 4U:
						num2 = 656573098;
						continue;
					}
					break;
				}
				return instance;
				IL_12:
				num2 = 299552620;
				goto IL_17;
				IL_48:
				instance = TaskManager._instance;
				num2 = 551629080;
				goto IL_17;
			}
		}

		// Token: 0x06001636 RID: 5686 RVA: 0x001509CC File Offset: 0x0014EBCC
		public TaskManager()
		{
			this._activeTasks = new Dictionary<string, HeroTask>();
			this._completedTasks = new Dictionary<string, List<HeroTask>>();
		}

		// Token: 0x06001637 RID: 5687 RVA: 0x001509F8 File Offset: 0x0014EBF8
		public override void RegisterEvents()
		{
			\u200E\u200E\u200B\u200B\u202B\u206B\u206F\u200C\u202A\u206A\u206A\u200B\u202B\u200C\u200F\u200F\u206C\u202D\u206D\u202A\u200B\u202A\u200F\u202C\u206D\u206B\u202D\u200F\u202D\u206F\u202A\u200D\u202A\u202A\u200C\u206F\u206C\u200B\u200E\u202B\u202E.\u00B4\u008Aòúr(\u206F\u202E\u200E\u206D\u202A\u202D\u200B\u200C\u200B\u206E\u202D\u206C\u200E\u200C\u200F\u206B\u206E\u202E\u202D\u206A\u202A\u200F\u200F\u206B\u202A\u202C\u206C\u206D\u206A\u202E\u206C\u202D\u200B\u200B\u202D\u206F\u206D\u206C\u200F\u202E.bØy\u000DU(), this, new Action(this.OnHourlyTick));
			\u200E\u200E\u200B\u200B\u202B\u206B\u206F\u200C\u202A\u206A\u206A\u200B\u202B\u200C\u200F\u200F\u206C\u202D\u206D\u202A\u200B\u202A\u200F\u202C\u206D\u206B\u202D\u200F\u202D\u206F\u202A\u200D\u202A\u202A\u200C\u206F\u206C\u200B\u200E\u202B\u202E.\u00B4\u008Aòúr(\u206F\u202E\u200E\u206D\u202A\u202D\u200B\u200C\u200B\u206E\u202D\u206C\u200E\u200C\u200F\u206B\u206E\u202E\u202D\u206A\u2
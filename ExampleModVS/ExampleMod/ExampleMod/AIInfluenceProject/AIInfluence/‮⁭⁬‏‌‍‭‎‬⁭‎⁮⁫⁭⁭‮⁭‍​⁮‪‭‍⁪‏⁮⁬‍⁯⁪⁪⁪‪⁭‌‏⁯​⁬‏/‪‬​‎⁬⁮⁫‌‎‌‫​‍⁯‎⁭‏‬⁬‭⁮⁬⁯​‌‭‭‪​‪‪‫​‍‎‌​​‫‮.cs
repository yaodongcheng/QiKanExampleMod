Token: 0x0600162C RID: 5676 RVA: 0x00150540 File Offset: 0x0014E740
		public TaskBuilder AttackParty(string targetPartyStringId, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateAttackParty(targetPartyStringId, description));
			return this;
		}

		// Token: 0x0600162D RID: 5677 RVA: 0x00150568 File Offset: 0x0014E768
		public TaskBuilder SiegeSettlement(Settlement settlement, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateSiegeSettlement(settlement, description));
			return this;
		}

		// Token: 0x0600162E RID: 5678 RVA: 0x00150590 File Offset: 0x0014E790
		public TaskBuilder PatrolSettlement(Settlement settlement, float durationDays, bool autoReturn, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreatePatrolSettlement(settlement, durationDays, autoReturn, description));
			return this;
		}

		// Token: 0x0600162F RID: 5679 RVA: 0x001505BC File Offset: 0x0014E7BC
		public TaskBuilder WaitNearSettlement(Settlement settlement, float durationDays, float radius, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateWaitNearSettlement(settlement, durationDays, radius, description));
			return this;
		}

		// Token: 0x06001630 RID: 5680 RVA: 0x001505E8 File Offset: 0x0014E7E8
		public TaskBuilder RaidVillage(Settlement village, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateRaidVillage(village, description));
			return this;
		}

		// Token: 0x06001631 RID: 5681 RVA: 0x00150610 File Offset: 0x0014E810
		public HeroTask Build()
		{
			bool flag = this.\u206F\u202D\u206F\u202E\u200C\u202E\u200C\u200B\u202C\u200C\u206D\u202D\u202A\u200F\u200E\u202E\u202E\u202E\u202E\u200C\u206B\u202C\u206C\u200F\u202E\u202D\u202E\u206D\u202C\u200D\u200C\u206F\u206C\u206E\u200B\u200C\u206C\u200D\u202C\u206B\u202E == null;
			if (flag)
			{
				goto IL_11;
			}
			goto IL_183;
			int num2;
			TaskManager instance;
			HeroTask result;
			for (;;)
			{
				IL_16:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)(~(uint)(~(uint)(~(uint)num))))) % 12U)
				{
				case 0U:
				{
					HeroTask heroTask = instance.CreateTask(this.\u206F\u202D\u206F\u202E\u200C\u202E\u200C\u200B\u202C\u200C\u206D\u202D\u202A\u200F\u200E\u202E\u202E\u202E\u202E\u200C\u206B\u202C\u206C\u200F\u202E\u202D\u202E\u206D\u202C\u200D\u200C\u206F\u206C\u206E\u200B\u200C\u206C\u200D\u202C\u206B\u202E, this.\u200C\u206E\u200B\u206E\u202D\u202C\u206B\u206B\u206C\u206F\u202D\u206E\u206E\u202B\u206B\u200B\u202A\u206E\u202A\u200C\u202A\u202E\u202C\u202A\u206F\u200F\u200E\u202E\u202B\u206F\u200C\u202D\u206D\u200F\u202A\u202C\u202E\u206F\u200E\u202B\u202E);
					num2 = ((heroTask == null) ? 98768651 : 730368247);
					continue;
				}
				case 1U:
					goto IL_183;
				case 2U:
					result = null;
					num2 = (int)(num3 * 732273136U ^ 960731051U);
					continue;
				case 4U:
				{
					HeroTask heroTask;
					heroTask.Steps[0].Status = TaskStepStatus.InProgress;
					bool flag2 = heroTask.Steps[0].StepType == TaskStepType.WaitInSet
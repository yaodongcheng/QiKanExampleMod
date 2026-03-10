using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x02000316 RID: 790
	public class TaskBuilder
	{
		// Token: 0x06001626 RID: 5670 RVA: 0x0015045C File Offset: 0x0014E65C
		public TaskBuilder(Hero A_1)
		{
			this.\u206F\u202D\u206F\u202E\u200C\u202E\u200C\u200B\u202C\u200C\u206D\u202D\u202A\u200F\u200E\u202E\u202E\u202E\u202E\u200C\u206B\u202C\u206C\u200F\u202E\u202D\u202E\u206D\u202C\u200D\u200C\u206F\u206C\u206E\u200B\u200C\u206C\u200D\u202C\u206B\u202E = A_1;
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E = new List<TaskStep>();
		}

		// Token: 0x06001627 RID: 5671 RVA: 0x00150484 File Offset: 0x0014E684
		public TaskBuilder WithDescription(string description)
		{
			this.\u200C\u206E\u200B\u206E\u202D\u202C\u206B\u206B\u206C\u206F\u202D\u206E\u206E\u202B\u206B\u200B\u202A\u206E\u202A\u200C\u202A\u202E\u202C\u202A\u206F\u200F\u200E\u202E\u202B\u206F\u200C\u202D\u206D\u200F\u202A\u202C\u202E\u206F\u200E\u202B\u202E = description;
			return this;
		}

		// Token: 0x06001628 RID: 5672 RVA: 0x001504A0 File Offset: 0x0014E6A0
		public TaskBuilder GoToSettlement(Settlement settlement, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateGoToSettlement(settlement, description));
			return this;
		}

		// Token: 0x06001629 RID: 5673 RVA: 0x001504C8 File Offset: 0x0014E6C8
		public TaskBuilder WaitInSettlement(Settlement settlement, float waitDays, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateWaitInSettlement(settlement, waitDays, description));
			return this;
		}

		// Token: 0x0600162A RID: 5674 RVA: 0x001504F0 File Offset: 0x0014E6F0
		public TaskBuilder ReturnToPlayer(string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateReturnToPlayer(description));
			return this;
		}

		// Token: 0x0600162B RID: 5675 RVA: 0x00150518 File Offset: 0x0014E718
		public TaskBuilder FollowPlayer(string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateFollowPlayer(description));
			return this;
		}

		// Token: 0x0600162C RID: 5676 RVA: 0x00150540 File Offset: 0x0014E740
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
					bool flag2 = heroTask.Steps[0].StepType == TaskStepType.WaitInSettlement;
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
			return new TaskBuilder(hero).WithDescription(description ?? \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(959515075), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null, waitDays)).GoToSettlement(settlement, null).WaitInSettlement(settlement, waitDays, null).Build();
		}

		// Token: 0x06001633 RID: 5683 RVA: 0x00150830 File Offset: 0x0014EA30
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static HeroTask CreateGoToSettlementWaitThenGoToAnother(Hero hero, Settlement firstSettlement, float waitDays, Settlement secondSettlement, string description = null)
		{
			return new TaskBuilder(hero).WithDescription(description ?? \u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.\u200D\u206D\u206F\u202A\u200B\u202B\u206B\u200F\u202B\u202E\u202B\u202E\u206E\u200F\u202C\u200F\u202A\u206D\u200D\u206C\u202A\u200F\u202D\u206F\u206E\u206C\u206B\u206D\u202E\u200E\u206D\u206E\u206C\u206B\u200F\u200D\u202A\u202A\u202C\u200F\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-784975705), (firstSettlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((firstSettlement) : null, waitDays, (secondSettlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((secondSettlement) : null)).GoToSettlement(firstSettlement, null).WaitInSettlement(firstSettlement, waitDays, null).GoToSettlement(secondSettlement, null).Build();
		}

		// Token: 0x06001634 RID: 5684 RVA: 0x001508A8 File Offset: 0x0014EAA8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static HeroTask CreateGoToSettlementWaitThenReturn(Hero hero, Settlement settlement, float waitDays, string description = null)
		{
			return new TaskBuilder(hero).WithDescription(description ?? \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(531736684), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null, waitDays)).GoToSettlement(settlement, null).WaitInSettlement(settlement, waitDays, null).ReturnToPlayer(null).Build();
		}

		// Token: 0x04000F95 RID: 3989
		private Hero \u206F\u202D\u206F\u202E\u200C\u202E\u200C\u200B\u202C\u200C\u206D\u202D\u202A\u200F\u200E\u202E\u202E\u202E\u202E\u200C\u206B\u202C\u206C\u200F\u202E\u202D\u202E\u206D\u202C\u200D\u200C\u206F\u206C\u206E\u200B\u200C\u206C\u200D\u202C\u206B\u202E;

		// Token: 0x04000F96 RID: 3990
		private List<TaskStep> \u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E;

		// Token: 0x04000F97 RID: 3991
		private string \u200C\u206E\u200B\u206E\u202D\u202C\u206B\u206B\u206C\u206F\u202D\u206E\u206E\u202B\u206B\u200B\u202A\u206E\u202A\u200C\u202A\u202E\u202C\u202A\u206F\u200F\u200E\u202E\u202B\u206F\u200C\u202D\u206D\u200F\u202A\u202C\u202E\u206F\u200E\u202B\u202E;
	}
}

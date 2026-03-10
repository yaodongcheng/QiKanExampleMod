using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using \u200F\u202A\u206C\u202C\u202B\u206B\u206C\u202A\u200B\u200D\u202D\u202A\u200F\u200E\u202D\u200F\u202E\u200E\u206F\u202A\u200C\u206A\u206F\u206F\u202A\u202D\u206A\u200F\u202C\u206A\u206A\u206B\u200B\u206D\u206A\u200D\u206D\u200F\u206B\u202B\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x02000314 RID: 788
	[Serializable]
	public class HeroTask
	{
		// Token: 0x170003FD RID: 1021
		// (get) Token: 0x06001609 RID: 5641 RVA: 0x0014FDA4 File Offset: 0x0014DFA4
		// (set) Token: 0x0600160A RID: 5642 RVA: 0x0014FDB8 File Offset: 0x0014DFB8
		public string TaskId { get; set; }

		// Token: 0x170003FE RID: 1022
		// (get) Token: 0x0600160B RID: 5643 RVA: 0x0014FDCC File Offset: 0x0014DFCC
		// (set) Token: 0x0600160C RID: 5644 RVA: 0x0014FDE0 File Offset: 0x0014DFE0
		public string HeroId { get; set; }

		// Token: 0x170003FF RID: 1023
		// (get) Token: 0x0600160D RID: 5645 RVA: 0x0014FDF4 File Offset: 0x0014DFF4
		// (set) Token: 0x0600160E RID: 5646 RVA: 0x0014FE08 File Offset: 0x0014E008
		public TaskStatus Status { get; set; }

		// Token: 0x17000400 RID: 1024
		// (get) Token: 0x0600160F RID: 5647 RVA: 0x0014FE1C File Offset: 0x0014E01C
		// (set) Token: 0x06001610 RID: 5648 RVA: 0x0014FE30 File Offset: 0x0014E030
		public List<TaskStep> Steps { get; set; }

		// Token: 0x17000401 RID: 1025
		// (get) Token: 0x06001611 RID: 5649 RVA: 0x0014FE44 File Offset: 0x0014E044
		// (set) Token: 0x06001612 RID: 5650 RVA: 0x0014FE58 File Offset: 0x0014E058
		public int CurrentStepIndex { get; set; }

		// Token: 0x17000402 RID: 1026
		// (get) Token: 0x06001613 RID: 5651 RVA: 0x0014FE6C File Offset: 0x0014E06C
		// (set) Token: 0x06001614 RID: 5652 RVA: 0x0014FE80 File Offset: 0x0014E080
		public CampaignTime CreatedTime { get; set; }

		// Token: 0x17000403 RID: 1027
		// (get) Token: 0x06001615 RID: 5653 RVA: 0x0014FE94 File Offset: 0x0014E094
		// (set) Token: 0x06001616 RID: 5654 RVA: 0x0014FEA8 File Offset: 0x0014E0A8
		public CampaignTime? CompletedTime { get; set; }

		// Token: 0x17000404 RID: 1028
		// (get) Token: 0x06001617 RID: 5655 RVA: 0x0014FEBC File Offset: 0x0014E0BC
		// (set) Token: 0x06001618 RID: 5656 RVA: 0x0014FED0 File Offset: 0x0014E0D0
		public string Description { get; set; }

		// Token: 0x06001619 RID: 5657 RVA: 0x0014FEE4 File Offset: 0x0014E0E4
		public HeroTask()
		{
			this.Steps = new List<TaskStep>();
			this.CurrentStepIndex = 0;
			this.Status = TaskStatus.Active;
			this.CreatedTime = \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
			this.TaskId = \u206A\u200C\u200F\u200C\u202C\u200B\u206D\u202B\u206A\u206F\u206D\u206C\u202E\u200D\u200F\u206E\u200C\u206F\u200C\u206A\u206F\u200E\u200E\u206B\u206C\u206D\u206A\u200C\u206B\u206E\u200B\u206A\u200E\u202B\u202C\u202A\u202E\u200C\u200D\u202C\u202E.åËø\u00A6Ó().ToString();
		}

		// Token: 0x0600161A RID: 5658 RVA: 0x0014FF48 File Offset: 0x0014E148
		public HeroTask(string A_1, string A_2 = null) : this()
		{
			this.HeroId = A_1;
			this.Description = A_2;
		}

		// Token: 0x0600161B RID: 5659 RVA: 0x0014FF70 File Offset: 0x0014E170
		public TaskStep GetCurrentStep()
		{
			if (this.CurrentStepIndex >= 0)
			{
				goto IL_0D;
			}
			bool flag = false;
			goto IL_93;
			int num2;
			TaskStep result;
			for (;;)
			{
				IL_12:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)(~(uint)(-(uint)(-(~(--120766150)) - num))))) % 6U)
				{
				case 0U:
					goto IL_0D;
				case 1U:
					result = null;
					num2 = 670352990;
					continue;
				case 3U:
					num2 = (int)(num3 * 2823518894U ^ 296490959U);
					continue;
				case 4U:
					result = this.Steps[this.CurrentStepIndex];
					num2 = (int)(num3 * 3007081055U ^ 1005617941U);
					continue;
				case 5U:
					goto IL_7D;
				}
				break;
			}
			return result;
			IL_7D:
			flag = (this.CurrentStepIndex < this.Steps.Count);
			goto IL_93;
			IL_0D:
			num2 = 1547480410;
			goto IL_12;
			IL_93:
			bool flag2 = flag;
			num2 = ((!flag2) ? 318401094 : 1895282511);
			goto IL_12;
		}

		// Token: 0x0600161C RID: 5660 RVA: 0x0015002C File Offset: 0x0014E22C
		public bool MoveToNextStep()
		{
			TaskStep currentStep = this.GetCurrentStep();
			bool flag = currentStep != null;
			if (flag)
			{
				goto IL_13;
			}
			goto IL_FF;
			int num2;
			int currentStepIndex;
			bool result;
			for (;;)
			{
				IL_18:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(-(num ^ -(-126712874 + -674652370 - (1973618695 + -1539176680)))) - 245943015)) % 10U)
				{
				case 0U:
				{
					bool flag2;
					num2 = (int)(((!flag2) ? 2307546435U : 3186732309U) ^ num3 * 3900650799U);
					continue;
				}
				case 1U:
				{
					TaskStep currentStep2 = this.GetCurrentStep();
					bool flag2 = currentStep2 != null;
					num2 = 875326668;
					continue;
				}
				case 2U:
				{
					this.CurrentStepIndex = currentStepIndex + 1;
					bool flag3 = this.CurrentStepIndex >= this.Steps.Count;
					num2 = (int)(((!flag3) ? 1246406473U : 910605039U) ^ num3 * 3485701959U);
					continue;
				}
				case 3U:
					result = true;
					num2 = 1626796549;
					continue;
				case 4U:
					goto IL_FF;
				case 6U:
					goto IL_13;
				case 7U:
				{
					TaskStep currentStep2;
					currentStep2.Status = TaskStepStatus.InProgress;
					num2 = (int)(num3 * 2263930958U ^ 2939787409U);
					continue;
				}
				case 8U:
					currentStep.Status = TaskStepStatus.Completed;
					num2 = (int)(num3 * 2029501043U ^ 3252350120U);
					continue;
				case 9U:
					this.Status = TaskStatus.Completed;
					this.CompletedTime = new CampaignTime?(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ());
					result = false;
					num2 = (int)(num3 * 2260590033U ^ 4094494654U);
					continue;
				}
				break;
			}
			return result;
			IL_13:
			num2 = 489785988;
			goto IL_18;
			IL_FF:
			currentStepIndex = this.CurrentStepIndex;
			num2 = 593650026;
			goto IL_18;
		}

		// Token: 0x0600161D RID: 5661 RVA: 0x001501B8 File Offset: 0x0014E3B8
		public bool HasMoreSteps()
		{
			return this.CurrentStepIndex < this.Steps.Count;
		}

		// Token: 0x0600161E RID: 5662 RVA: 0x001501E0 File Offset: 0x0014E3E0
		public void Cancel()
		{
			this.Status = TaskStatus.Cancelled;
			TaskStep currentStep = this.GetCurrentStep();
			if (currentStep != null)
			{
				goto IL_13;
			}
			bool flag = false;
			goto IL_55;
			int num2;
			for (;;)
			{
				IL_18:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(num ^ ~-1420146062) - (2092415194 ^ 1428046405))) % 4U)
				{
				case 0U:
					goto IL_13;
				case 2U:
					goto IL_49;
				case 3U:
					currentStep.Status = TaskStepStatus.Cancelled;
					num2 = (int)(num3 * 1121233683U ^ 1074919148U);
					continue;
				}
				break;
			}
			return;
			IL_49:
			flag = (currentStep.Status == TaskStepStatus.InProgress);
			goto IL_55;
			IL_13:
			num2 = -572533406;
			goto IL_18;
			IL_55:
			bool flag2 = flag;
			num2 = ((!flag2) ? -1063064119 : -374955785);
			goto IL_18;
		}

		// Token: 0x0600161F RID: 5663 RVA: 0x00150270 File Offset: 0x0014E470
		public Hero GetHero()
		{
			bool flag = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(this.HeroId);
			if (flag)
			{
				goto IL_15;
			}
			goto IL_6F;
			int num2;
			Hero result;
			for (;;)
			{
				IL_1A:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(uint)((num ^ 1107134113 * (~1290930223 ^ --634951520)) * -1768787057 ^ 1172276169 - 146007637))) % 4U)
				{
				case 0U:
					goto IL_15;
				case 2U:
					result = null;
					num2 = (int)(num3 * 1674183429U ^ 2506956079U);
					continue;
				case 3U:
					goto IL_6F;
				}
				break;
			}
			return result;
			IL_15:
			num2 = 2075632314;
			goto IL_1A;
			IL_6F:
			result = Enumerable.FirstOrDefault<Hero>(\u206C\u206C\u206D\u200C\u206B\u200D\u202E\u206E\u206B\u200D\u202E\u206C\u200E\u206F\u202A\u202D\u200B\u200F\u206A\u202D\u200D\u202A\u206B\u206B\u206C\u202A\u206E\u200B\u202D\u206C\u206E\u200C\u202E\u206C\u206E\u200F\u206D\u200E\u200E\u202C\u202E.\u200D\u206F\u202D\u200B\u200F\u200C\u202C\u200C\u202A\u200E\u202A\u206E\u200D\u206A\u200D\u200B\u200D\u206F\u200D\u200E\u200F\u206C\u206C\u200C\u202C\u206E\u206E\u202A\u206C\u206E\u202C\u200B\u200E\u202E\u202A\u206D\u206A\u206B\u200C\u200F\u202E(), new Func<Hero, bool>(this.\u200C\u200B\u202D\u200F\u206D\u202B\u206D\u200C\u200B\u202D\u200D\u202B\u206C\u200F\u200C\u200E\u202C\u206B\u202E\u202A\u202B\u206D\u200F\u206F\u202A\u206C\u202B\u200E\u202C\u206D\u206E\u202E\u200E\u202E\u206B\u200E\u206C\u202D\u200C\u206D\u202E));
			num2 = 532373177;
			goto IL_1A;
		}

		// Token: 0x06001620 RID: 5664 RVA: 0x0015030C File Offset: 0x0014E50C
		public bool IsActive()
		{
			bool flag;
			if (this.Status != TaskStatus.Active)
			{
				flag = false;
				goto IL_56;
			}
			IL_09:
			int num = 1888544774;
			IL_0E:
			int num2 = num;
			switch ((-num2 - (--1836918990 + (1192136844 - 1780013237)) - (-1187627979 + 1336096242)) * 325510103 % 3)
			{
			case 0:
				goto IL_09;
			case 1:
				flag = this.HasMoreSteps();
				goto IL_56;
			}
			bool result;
			return result;
			IL_56:
			result = flag;
			num = 1116628153;
			goto IL_0E;
		}

		// Token: 0x06001621 RID: 5665 RVA: 0x00150378 File Offset: 0x0014E578
		public float GetProgress()
		{
			bool flag = this.Steps.Count == 0;
			if (flag)
			{
				goto IL_13;
			}
			goto IL_53;
			int num2;
			float result;
			for (;;)
			{
				IL_18:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-num * -1403184585)) % 4U)
				{
				case 0U:
					goto IL_13;
				case 1U:
					goto IL_53;
				case 3U:
					result = 1f;
					num2 = (int)(num3 * 1861119759U ^ 2871707311U);
					continue;
				}
				break;
			}
			return result;
			IL_13:
			num2 = -1848732537;
			goto IL_18;
			IL_53:
			int num4 = Enumerable.Count<TaskStep>(this.Steps, new Func<TaskStep, bool>(HeroTask.\u202D\u206D\u202D\u206B\u206C\u206D\u200D\u206E\u200B\u202E\u202C\u200E\u200C\u200D\u202A\u202D\u200C\u200E\u206A\u206E\u206A\u206F\u202D\u202A\u202D\u206F\u206F\u202C\u206A\u200F\u202C\u202D\u202A\u202E\u200F\u206C\u200E\u200E\u202A\u202E.<>9.\u200D\u200B\u200F\u202A\u200F\u200D\u206D\u206D\u202D\u200F\u200B\u202D\u206E\u206B\u200C\u206F\u202C\u202A\u202D\u206F\u202C\u206F\u200E\u202E\u200F\u202A\u202E\u202E\u206B\u206C\u202C\u200F\u200F\u200C\u206B\u200D\u206F\u200F\u202E\u202E));
			result = (float)num4 / (float)this.Steps.Count;
			num2 = 810809694;
			goto IL_18;
		}

		// Token: 0x06001622 RID: 5666 RVA: 0x0015041C File Offset: 0x0014E61C
		[CompilerGenerated]
		private bool \u200C\u200B\u202D\u200F\u206D\u202B\u206D\u200C\u200B\u202D\u200D\u202B\u206C\u200F\u200C\u200E\u202C\u206B\u202E\u202A\u202B\u206D\u200F\u206F\u202A\u206C\u202B\u200E\u202C\u206D\u206E\u202E\u200E\u202E\u206B\u200E\u206C\u202D\u200C\u206D\u202E(Hero A_1)
		{
			return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.HeroId);
		}

		// Token: 0x02000315 RID: 789
		[CompilerGenerated]
		[Serializable]
		private sealed class \u202D\u206D\u202D\u206B\u206C\u206D\u200D\u206E\u200B\u202E\u202C\u200E\u200C\u200D\u202A\u202D\u200C\u200E\u206A\u206E\u206A\u206F\u202D\u202A\u202D\u206F\u206F\u202C\u206A\u200F\u202C\u202D\u202A\u202E\u200F\u206C\u200E\u200E\u202A\u202E
		{
			// Token: 0x06001625 RID: 5669 RVA: 0x0007C094 File Offset: 0x0007A294
			internal bool \u200D\u200B\u200F\u202A\u200F\u200D\u206D\u206D\u202D\u200F\u200B\u202D\u206E\u206B\u200C\u206F\u202C\u202A\u202D\u206F\u202C\u206F\u200E\u202E\u200F\u202A\u202E\u202E\u206B\u206C\u202C\u200F\u200F\u200C\u206B\u200D\u206F\u200F\u202E\u202E(TaskStep A_1)
			{
				return A_1.Status == TaskStepStatus.Completed;
			}

			// Token: 0x04000F93 RID: 3987
			public static readonly HeroTask.\u202D\u206D\u202D\u206B\u206C\u206D\u200D\u206E\u200B\u202E\u202C\u200E\u200C\u200D\u202A\u202D\u200C\u200E\u206A\u206E\u206A\u206F\u202D\u202A\u202D\u206F\u206F\u202C\u206A\u200F\u202C\u202D\u202A\u202E\u200F\u206C\u200E\u200E\u202A\u202E <>9 = new HeroTask.\u202D\u206D\u202D\u206B\u206C\u206D\u200D\u206E\u200B\u202E\u202C\u200E\u200C\u200D\u202A\u202D\u200C\u200E\u206A\u206E\u206A\u206F\u202D\u202A\u202D\u206F\u206F\u202C\u206A\u200F\u202C\u202D\u202A\u202E\u200F\u206C\u200E\u200E\u202A\u202E();

			// Token: 0x04000F94 RID: 3988
			public static Func<TaskStep, bool> <>9__40_0;
		}
	}
}

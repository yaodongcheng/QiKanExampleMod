using System;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x0200031B RID: 795
	[Serializable]
	public class TaskStep
	{
		// Token: 0x17000406 RID: 1030
		// (get) Token: 0x06001645 RID: 5701 RVA: 0x00151A08 File Offset: 0x0014FC08
		// (set) Token: 0x06001646 RID: 5702 RVA: 0x00151A1C File Offset: 0x0014FC1C
		public TaskStepType StepType { get; set; }

		// Token: 0x17000407 RID: 1031
		// (get) Token: 0x06001647 RID: 5703 RVA: 0x00151A30 File Offset: 0x0014FC30
		// (set) Token: 0x06001648 RID: 5704 RVA: 0x00151A44 File Offset: 0x0014FC44
		public TaskStepStatus Status { get; set; }

		// Token: 0x17000408 RID: 1032
		// (get) Token: 0x06001649 RID: 5705 RVA: 0x00151A58 File Offset: 0x0014FC58
		// (set) Token: 0x0600164A RID: 5706 RVA: 0x00151A6C File Offset: 0x0014FC6C
		public string TargetSettlementId { get; set; }

		// Token: 0x17000409 RID: 1033
		// (get) Token: 0x0600164B RID: 5707 RVA: 0x00151A80 File Offset: 0x0014FC80
		// (set) Token: 0x0600164C RID: 5708 RVA: 0x00151A94 File Offset: 0x0014FC94
		public string TargetPartyId { get; set; }

		// Token: 0x1700040A RID: 1034
		// (get) Token: 0x0600164D RID: 5709 RVA: 0x00151AA8 File Offset: 0x0014FCA8
		// (set) Token: 0x0600164E RID: 5710 RVA: 0x00151ABC File Offset: 0x0014FCBC
		public float WaitDays { get; set; }

		// Token: 0x1700040B RID: 1035
		// (get) Token: 0x0600164F RID: 5711 RVA: 0x00151AD0 File Offset: 0x0014FCD0
		// (set) Token: 0x06001650 RID: 5712 RVA: 0x00151AE4 File Offset: 0x0014FCE4
		public CampaignTime? WaitStartTime { get; set; }

		// Token: 0x1700040C RID: 1036
		// (get) Token: 0x06001651 RID: 5713 RVA: 0x00151AF8 File Offset: 0x0014FCF8
		// (set) Token: 0x06001652 RID: 5714 RVA: 0x00151B0C File Offset: 0x0014FD0C
		public CampaignTime? WaitUntilTime { get; set; }

		// Token: 0x1700040D RID: 1037
		// (get) Token: 0x06001653 RID: 5715 RVA: 0x00151B20 File Offset: 0x0014FD20
		// (set) Token: 0x06001654 RID: 5716 RVA: 0x00151B34 File Offset: 0x0014FD34
		public string CustomData { get; set; }

		// Token: 0x1700040E RID: 1038
		// (get) Token: 0x06001655 RID: 5717 RVA: 0x00151B48 File Offset: 0x0014FD48
		// (set) Token: 0x06001656 RID: 5718 RVA: 0x00151B5C File Offset: 0x0014FD5C
		public float PatrolDurationDays { get; set; }

		// Token: 0x1700040F RID: 1039
		// (get) Token: 0x06001657 RID: 5719 RVA: 0x00151B70 File Offset: 0x0014FD70
		// (set) Token: 0x06001658 RID: 5720 RVA: 0x00151B84 File Offset: 0x0014FD84
		public bool PatrolAutoReturn { get; set; }

		// Token: 0x17000410 RID: 1040
		// (get) Token: 0x06001659 RID: 5721 RVA: 0x00151B98 File Offset: 0x0014FD98
		// (set) Token: 0x0600165A RID: 5722 RVA: 0x00151BAC File Offset: 0x0014FDAC
		public float WaitNearDurationDays { get; set; }

		// Token: 0x17000411 RID: 1041
		// (get) Token: 0x0600165B RID: 5723 RVA: 0x00151BC0 File Offset: 0x0014FDC0
		// (set) Token: 0x0600165C RID: 5724 RVA: 0x00151BD4 File Offset: 0x0014FDD4
		public float WaitNearRadius { get; set; }

		// Token: 0x17000412 RID: 1042
		// (get) Token: 0x0600165D RID: 5725 RVA: 0x00151BE8 File Offset: 0x0014FDE8
		// (set) Token: 0x0600165E RID: 5726 RVA: 0x00151BFC File Offset: 0x0014FDFC
		public string Description { get; set; }

		// Token: 0x0600165F RID: 5727 RVA: 0x00151C10 File Offset: 0x0014FE10
		public TaskStep()
		{
			this.Status = TaskStepStatus.Pending;
		}

		// Token: 0x06001660 RID: 5728 RVA: 0x00151C30 File Offset: 0x0014FE30
		public TaskStep(TaskStepType A_1, string A_2 = null)
		{
			this.StepType = A_1;
			this.Status = TaskStepStatus.Pending;
			this.Description = A_2;
		}

		// Token: 0x06001661 RID: 5729 RVA: 0x00151C60 File Offset: 0x0014FE60
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateGoToSettlement(Settlement settlement, string description = null)
		{
			return new TaskStep(TaskStepType.GoToSettlement, description ?? \u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E.\u206B\u206E\u200F\u200B\u202C\u202E\u202A\u202B\u206F\u206F\u202D\u200F\u202B\u202B\u202E\u200B\u206D\u200C\u206B\u206F\u200F\u200D\u206D\u202B\u200B\u206F\u202C\u200B\u200D\u206C\u200F\u202C\u200E\u200D\u202A\u206F\u206D\u200E\u200F\u206A\u202E(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(279859592), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null)
			};
		}

		// Token: 0x06001662 RID: 5730 RVA: 0x00151CB8 File Offset: 0x0014FEB8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateWaitInSettlement(Settlement settlement, float waitDays, string description = null)
		{
			return new TaskStep(TaskStepType.WaitInSettlement, description ?? \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1050920133), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null, waitDays))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null),
				WaitDays = waitDays
			};
		}

		// Token: 0x06001663 RID: 5731 RVA: 0x00151D20 File Offset: 0x0014FF20
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateReturnToPlayer(string description = null)
		{
			return new TaskStep(TaskStepType.ReturnToPlayer, description ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-278349109));
		}

		// Token: 0x06001664 RID: 5732 RVA: 0x00151D4C File Offset: 0x0014FF4C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateFollowPlayer(string description = null)
		{
			return new TaskStep(TaskStepType.FollowPlayer, description ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-410138479));
		}

		// Token: 0x06001665 RID: 5733 RVA: 0x00151D78 File Offset: 0x0014FF78
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateAttackParty(string targetPartyStringId, string description = null)
		{
			return new TaskStep(TaskStepType.AttackParty, description ?? \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1028867998), targetPartyStringId))
			{
				TargetPartyId = targetPartyStringId
			};
		}

		// Token: 0x06001666 RID: 5734 RVA: 0x00151DB4 File Offset: 0x0014FFB4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateSiegeSettlement(Settlement settlement, string description = null)
		{
			return new TaskStep(TaskStepType.SiegeSettlement, description ?? \u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E.\u206B\u206E\u200F\u200B\u202C\u202E\u202A\u202B\u206F\u206F\u202D\u200F\u202B\u202B\u202E\u200B\u206D\u200C\u206B\u206F\u200F\u200D\u206D\u202B\u200B\u206F\u202C\u200B\u200D\u206C\u200F\u202C\u200E\u200D\u202A\u206F\u206D\u200E\u200F\u206A\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(883932475), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null)
			};
		}

		// Token: 0x06001667 RID: 5735 RVA: 0x00151E0C File Offset: 0x0015000C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreatePatrolSettlement(Settlement settlement, float durationDays, bool autoReturn, string description = null)
		{
			return new TaskStep(TaskStepType.PatrolSettlement, description ?? \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1844734529), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null, durationDays))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null),
				PatrolDurationDays = durationDays,
				PatrolAutoReturn = autoReturn
			};
		}

		// Token: 0x06001668 RID: 5736 RVA: 0x00151E7C File Offset: 0x0015007C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateWaitNearSettlement(Settlement settlement, float durationDays, float radius, string description = null)
		{
			return new TaskStep(TaskStepType.WaitNearSettlement, description ?? \u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.\u200D\u206D\u206F\u202A\u200B\u202B\u206B\u200F\u202B\u202E\u202B\u202E\u206E\u200F\u202C\u200F\u202A\u206D\u200D\u206C\u202A\u200F\u202D\u206F\u206E\u206C\u206B\u206D\u202E\u200E\u206D\u206E\u206C\u206B\u200F\u200D\u202A\u202A\u202C\u200F\u202E(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1071548971), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null, durationDays, radius))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null),
				WaitNearDurationDays = durationDays,
				WaitNearRadius = radius
			};
		}

		// Token: 0x06001669 RID: 5737 RVA: 0x00151EF0 File Offset: 0x001500F0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateRaidVillage(Settlement village, string description = null)
		{
			return new TaskStep(TaskStepType.RaidVillage, description ?? \u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E.\u206B\u206E\u200F\u200B\u202C\u202E\u202A\u202B\u206F\u206F\u202D\u200F\u202B\u202B\u202E\u200B\u206D\u200C\u206B\u206F\u200F\u200D\u206D\u202B\u200B\u206F\u202C\u200B\u200D\u206C\u200F\u202C\u200E\u200D\u202A\u206F\u206D\u200E\u200F\u206A\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(43661270), (village != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((village) : null))
			{
				TargetSettlementId = ((village != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(village) : null)
			};
		}

		// Token: 0x0600166A RID: 5738 RVA: 0x00151F48 File Offset: 0x00150148
		public Settlement GetTargetSettlement()
		{
			bool flag = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(this.TargetSettlementId);
			Settlement result;
			for (;;)
			{
				IL_12:
				int num = -1323134193;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)(~num2 - (~-932507661 + 1491524598 * 1924596037))) % 7U)
					{
					case 0U:
						goto IL_12;
					case 1U:
						result = Enumerable.FirstOrDefault<Settlement>(\u200F\u200D\u200D\u200C\u200F\u200B\u206A\u202E\u202A\u200F\u202A\u206F\u206D\u202D\u200F\u200C\u202C\u200F\u200B\u206B\u202A\u206B\u202D\u200D\u202B\u202A\u206D\u200C\u200C\u202A\u202A\u202C\u202A\u206C\u206A\u202D\u206C\u202B\u202D\u206E\u202E.\u202A\u206F\u206B\u206D\u200D\u202A\u206C\u200C\u200C\u202E\u206F\u206E\u202A\u200C\u202E\u206C\u206C\u200D\u206B\u206A\u202E\u200D\u200B\u200B\u206C\u206E\u206A\u206F\u202D\u206E\u202D\u202D\u206E\u206A\u202B\u206A\u206D\u200C\u206F\u202B\u202E(), new Func<Settlement, bool>(this.\u200D\u200C\u200B\u206D\u202A\u200F\u202E\u200F\u202D\u202C\u206A\u200C\u206D\u202B\u202D\u206F\u200C\u200F\u206E\u200B\u200E\u206D\u206D\u200E\u206C\u206B\u206D\u200E\u206E\u202D\u200E\u200D\u206A\u206B\u206D\u200D\u202C\u202C\u200E\u200D\u202E));
						num = -615031445;
						continue;
					case 3U:
						num = (int)(num3 * 3684211742U ^ 2392683878U);
						continue;
					case 4U:
						num = (int)(num3 * 3291487893U ^ 1254615813U);
						continue;
					case 5U:
						result = null;
						num = (int)(num3 * 1641486891U ^ 2215708905U);
						continue;
					case 6U:
						num = (int)((flag ? 221056762U : 3622201319U) ^ num3 * 2332238629U);
						continue;
					}
					return result;
				}
			}
			return result;
		}

		// Token: 0x0600166B RID: 5739 RVA: 0x0015201C File Offset: 0x0015021C
		public MobileParty GetTargetParty()
		{
			bool flag = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(this.TargetPartyId);
			if (flag)
			{
				goto IL_15;
			}
			goto IL_73;
			int num2;
			MobileParty result;
			for (;;)
			{
				IL_1A:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)(num * -1337040955 + ((-232827183 ^ -1542369145) + (-1290429809 ^ -1700409742)) - -773369251 * -1048356490))) % 4U)
				{
				case 0U:
					goto IL_15;
				case 2U:
					result = null;
					num2 = (int)(num3 * 455156019U ^ 4168599819U);
					continue;
				case 3U:
					goto IL_73;
				}
				break;
			}
			return result;
			IL_15:
			num2 = -18852584;
			goto IL_1A;
			IL_73:
			result = Enumerable.FirstOrDefault<MobileParty>(\u200D\u206B\u200C\u200D\u206B\u202C\u200F\u206C\u206D\u202A\u202A\u202B\u202D\u202C\u206C\u206A\u202E\u206B\u206C\u206A\u202E\u202C\u200D\u200E\u206A\u206F\u202B\u206E\u206D\u200B\u202B\u206D\u202D\u200B\u202A\u200D\u202B\u202E\u206A\u200F\u202E.\u200E\u202D\u200F\u200E\u206D\u206D\u206A\u202A\u202A\u206E\u206D\u200C\u202C\u206E\u202B\u202D\u206C\u200B\u202A\u202E\u202B\u200E\u206D\u202C\u206F\u206D\u200D\u200C\u202A\u206F\u202B\u200B\u200F\u200F\u202E\u206F\u202B\u202D\u206E\u206A\u202E(), new Func<MobileParty, bool>(this.\u206B\u206C\u206A\u200C\u206C\u206D\u202E\u202B\u206F\u202E\u202E\u200D\u202E\u200B\u202B\u200E\u200F\u202C\u200F\u206C\u206D\u202D\u202C\u202D\u200B\u202A\u202B\u200E\u200D\u206E\u206E\u206F\u200B\u206E\u206E\u206F\u206B\u200D\u206E\u206C\u202E));
			num2 = 1597744541;
			goto IL_1A;
		}

		// Token: 0x0600166C RID: 5740 RVA: 0x001520BC File Offset: 0x001502BC
		public bool IsWaitCompleted()
		{
			if (this.StepType == TaskStepType.WaitInSettlement)
			{
				goto IL_0D;
			}
			bool flag = true;
			goto IL_98;
			int num2;
			bool result;
			for (;;)
			{
				IL_12:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((~num * 1209115649 ^ -1389442152 - -525499966) - -621909689)) % 5U)
				{
				case 0U:
					goto IL_0D;
				case 1U:
					result = \u206F\u206C\u206F\u202C\u206A\u206A\u200F\u200F\u202E\u206B\u206C\u200E\u202D\u200C\u206A\u206A\u206D\u206C\u200D\u202A\u200C\u200F\u200E\u200D\u202B\u202C\u206F\u206C\u206E\u206F\u200F\u202E\u200B\u200E\u206F\u202C\u200F\u200C\u202B\u200E\u202E.b\u00B0y\u0013ö(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E(), this.WaitUntilTime.Value);
					num2 = 259876173;
					continue;
				case 2U:
					goto IL_84;
				case 3U:
					result = false;
					num2 = (int)(num3 * 598081979U ^ 3646752638U);
					continue;
				}
				break;
			}
			return result;
			IL_84:
			flag = (this.WaitUntilTime == null);
			goto IL_98;
			IL_0D:
			num2 = -1810233987;
			goto IL_12;
			IL_98:
			num2 = (flag ? -919066343 : -1374557127);
			goto IL_12;
		}

		// Token: 0x0600166D RID: 5741 RVA: 0x0015217C File Offset: 0x0015037C
		public void StartWaiting()
		{
			if (this.StepType == TaskStepType.WaitInSettlement)
			{
				goto IL_0A;
			}
			bool flag = false;
			goto IL_6C;
			int num2;
			for (;;)
			{
				IL_0F:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-2124640875 - ~(uint)(num + (-1487108194 ^ -889511552 + ~2121211129) - (~-987153864 ^ 930712678 - 1125080744)))) % 4U)
				{
				case 0U:
					goto IL_0A;
				case 1U:
					goto IL_5C;
				case 3U:
					this.WaitStartTime = new CampaignTime?(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ());
					this.WaitUntilTime = new CampaignTime?(\u206A\u206C\u206E\u206A\u206B\u206B\u200B\u206E\u206E\u206B\u200C\u202A\u200C\u206D\u200B\u200E\u202C\u206C\u200D\u206F\u206A\u202E\u202D\u200C\u206F\u206F\u200E\u200F\u202B\u200D\u206C\u202E\u206D\u206E\u202A\u200E\u200D\u200C\u200E\u202E.M"\u00B1\u00AD\u008B(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ(), \u200B\u202B\u202D\u202C\u206F\u206D\u200F\u206E\u200E\u206B\u202B\u206B\u200B\u206A\u206D\u206D\u200E\u202D\u206A\u206F\u202E\u206F\u206A\u202C\u206D\u202C\u200D\u206A\u200B\u202E\u206A\u202A\u206B\u202E\u206F\u206C\u206E\u200F\u202A\u200D\u202E.~\u000Dh\u0082ü(this.WaitDays)));
					this.Status = TaskStepStatus.InProgress;
					num2 = (int)(num3 * 3310215747U ^ 2905787404U);
					continue;
				}
				break;
			}
			return;
			IL_5C:
			flag = (this.WaitDays > 0f);
			goto IL_6C;
			IL_0A:
			num2 = -1333935228;
			goto IL_0F;
			IL_6C:
			bool flag2 = flag;
			num2 = ((!flag2) ? -1871505455 : -1120956630);
			goto IL_0F;
		}

		// Token: 0x0600166E RID: 5742 RVA: 0x0015226C File Offset: 0x0015046C
		[CompilerGenerated]
		private bool \u200D\u200C\u200B\u206D\u202A\u200F\u202E\u200F\u202D\u202C\u206A\u200C\u206D\u202B\u202D\u206F\u200C\u200F\u206E\u200B\u200E\u206D\u206D\u200E\u206C\u206B\u206D\u200E\u206E\u202D\u200E\u200D\u206A\u206B\u206D\u200D\u202C\u202C\u200E\u200D\u202E(Settlement A_1)
		{
			return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.TargetSettlementId);
		}

		// Token: 0x0600166F RID: 5743 RVA: 0x00152294 File Offset: 0x00150494
		[CompilerGenerated]
		private bool \u206B\u206C\u206A\u200C\u206C\u206D\u202E\u202B\u206F\u202E\u202E\u200D\u202E\u200B\u202B\u200E\u200F\u202C\u200F\u206C\u206D\u202D\u202C\u202D\u200B\u202A\u202B\u200E\u200D\u206E\u206E\u206F\u200B\u206E\u206E\u206F\u206B\u200D\u206E\u206C\u202E(MobileParty A_1)
		{
			return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.TargetPartyId);
		}
	}
}

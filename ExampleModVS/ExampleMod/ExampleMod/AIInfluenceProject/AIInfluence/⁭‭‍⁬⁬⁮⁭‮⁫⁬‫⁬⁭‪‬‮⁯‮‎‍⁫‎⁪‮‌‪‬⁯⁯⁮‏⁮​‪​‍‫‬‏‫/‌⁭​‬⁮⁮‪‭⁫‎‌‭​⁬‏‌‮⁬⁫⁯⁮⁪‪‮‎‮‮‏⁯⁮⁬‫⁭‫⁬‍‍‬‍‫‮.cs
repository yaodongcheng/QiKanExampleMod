static \u200E\u202A\u200D\u202B\u206E\u200E\u200C\u200D\u200E\u200F\u206B\u206B\u200C\u200D\u206D\u200C\u202A\u202A\u202A\u206E\u202C\u200C\u206C\u200E\u200B\u206A\u202E\u202D\u200E\u200E\u206B\u206B\u200F\u206B\u206E\u200F\u202E\u206B\u206C\u200C\u202E úÎ\u00BBGC;
	}
}
﻿using System;
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
		public float WaitNearRadius { 
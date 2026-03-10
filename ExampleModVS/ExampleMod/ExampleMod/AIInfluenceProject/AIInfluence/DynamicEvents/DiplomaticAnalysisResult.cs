using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x02000166 RID: 358
	[JsonSerializable]
	public class DiplomaticAnalysisResult
	{
		// Token: 0x17000257 RID: 599
		// (get) Token: 0x06000BA9 RID: 2985 RVA: 0x000C9468 File Offset: 0x000C7668
		// (set) Token: 0x06000BAA RID: 2986 RVA: 0x000C947C File Offset: 0x000C767C
		[JsonProperty("should_continue_event")]
		public bool ShouldContinueEvent { get; set; }

		// Token: 0x17000258 RID: 600
		// (get) Token: 0x06000BAB RID: 2987 RVA: 0x000C9490 File Offset: 0x000C7690
		// (set) Token: 0x06000BAC RID: 2988 RVA: 0x000C94A4 File Offset: 0x000C76A4
		[JsonProperty("should_end_event")]
		public bool ShouldEndEvent { get; set; }

		// Token: 0x17000259 RID: 601
		// (get) Token: 0x06000BAD RID: 2989 RVA: 0x000C94B8 File Offset: 0x000C76B8
		// (set) Token: 0x06000BAE RID: 2990 RVA: 0x000C94CC File Offset: 0x000C76CC
		[JsonProperty("kingdoms_to_add")]
		public List<string> KingdomsToAdd { get; set; } = new List<string>();

		// Token: 0x1700025A RID: 602
		// (get) Token: 0x06000BAF RID: 2991 RVA: 0x000C94E0 File Offset: 0x000C76E0
		// (set) Token: 0x06000BB0 RID: 2992 RVA: 0x000C94F4 File Offset: 0x000C76F4
		[JsonProperty("kingdoms_to_remove")]
		public List<string> KingdomsToRemove { get; set; } = new List<string>();

		// Token: 0x1700025B RID: 603
		// (get) Token: 0x06000BB1 RID: 2993 RVA: 0x000C9508 File Offset: 0x000C7708
		// (set) Token: 0x06000BB2 RID: 2994 RVA: 0x000C951C File Offset: 0x000C771C
		[JsonProperty("event_update")]
		public string EventUpdate { get; set; }

		// Token: 0x1700025C RID: 604
		// (get) Token: 0x06000BB3 RID: 2995 RVA: 0x000C9530 File Offset: 0x000C7730
		// (set) Token: 0x06000BB4 RID: 2996 RVA: 0x000C9544 File Offset: 0x000C7744
		[JsonProperty("actions_to_execute")]
		public List<DiplomaticActionInfo> ActionsToExecute { get; set; } = new List<DiplomaticActionInfo>();

		// Token: 0x1700025D RID: 605
		// (get) Token: 0x06000BB5 RID: 2997 RVA: 0x000C9558 File Offset: 0x000C7758
		// (set) Token: 0x06000BB6 RID: 2998 RVA: 0x000C956C File Offset: 0x000C776C
		[JsonProperty("relation_changes")]
		public List<RelationChangeInfo> RelationChanges { get; set; } = new List<RelationChangeInfo>();

		// Token: 0x1700025E RID: 606
		// (get) Token: 0x06000BB7 RID: 2999 RVA: 0x000C9580 File Offset: 0x000C7780
		// (set) Token: 0x06000BB8 RID: 3000 RVA: 0x000C9594 File Offset: 0x000C7794
		[JsonProperty("applicable_npcs")]
		public List<string> ApplicableNPCs { get; set; } = new List<string>();

		// Token: 0x1700025F RID: 607
		// (get) Token: 0x06000BB9 RID: 3001 RVA: 0x000C95A8 File Offset: 0x000C77A8
		// (set) Token: 0x06000BBA RID: 3002 RVA: 0x000C95BC File Offset: 0x000C77BC
		[JsonProperty("economic_effects")]
		public List<EconomicEffect> EconomicEffects { get; set; } = new List<EconomicEffect>();

		// Token: 0x17000260 RID: 608
		// (get) Token: 0x06000BBB RID: 3003 RVA: 0x000C95D0 File Offset: 0x000C77D0
		// (set) Token: 0x06000BBC RID: 3004 RVA: 0x000C95E4 File Offset: 0x000C77E4
		[JsonProperty("retry_delay_days")]
		public int RetryDelayDays { get; set; } = 0;

		// Token: 0x0400072D RID: 1837
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200F\u206F\u202E\u200E\u206C\u202C\u200C\u200E\u200F\u200C\u206E\u200E\u206E\u206B\u202E\u200C\u206B\u206D\u206D\u200B\u206F\u202E\u202E\u206E\u202E\u202B\u206A\u206A\u202E\u200E\u206C\u206C\u202A\u200B\u202B\u202B\u206A\u202D\u200B\u206B\u202E;

		// Token: 0x0400072E RID: 1838
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200C\u200D\u206C\u202B\u202D\u206A\u202C\u202D\u200F\u200E\u200B\u200C\u206D\u206E\u206B\u200D\u200B\u200F\u206B\u206D\u206D\u206D\u202B\u206E\u200D\u200D\u206C\u206B\u202C\u200D\u206B\u200C\u200C\u200B\u206B\u202E\u200E\u206F\u202D\u206B\u202E;

		// Token: 0x0400072F RID: 1839
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200B\u206B\u200F\u200C\u206C\u200E\u200B\u200E\u200D\u206C\u200C\u206D\u206E\u206D\u206E\u206D\u206B\u202D\u206D\u206F\u200F\u200B\u200B\u200F\u206A\u200E\u202A\u202B\u202B\u206B\u206E\u200B\u202D\u200F\u200E\u202E\u200E\u202A\u202E\u206B\u202E;

		// Token: 0x04000730 RID: 1840
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u202D\u200B\u200F\u202B\u206F\u206C\u206E\u206C\u206B\u200D\u200E\u202D\u200D\u202C\u200E\u202C\u200F\u206A\u206B\u206F\u206A\u206D\u202E\u200B\u202C\u206A\u202D\u200B\u202A\u206C\u200C\u202C\u200F\u200E\u202D\u206C\u206B\u200C\u202B\u202D\u202E;

		// Token: 0x04000731 RID: 1841
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u202B\u200B\u200B\u202E\u202C\u200B\u200F\u206C\u202A\u206F\u202D\u206C\u206E\u200E\u202C\u206E\u202D\u200E\u202E\u202A\u202A\u206C\u206F\u206B\u206E\u206B\u206D\u200E\u202B\u200B\u206D\u202C\u206B\u200C\u200C\u200D\u206D\u202C\u202A\u202E;

		// Token: 0x04000732 RID: 1842
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<DiplomaticActionInfo> \u200D\u200B\u206C\u200B\u206A\u206C\u206D\u202C\u200C\u206D\u206A\u206E\u202E\u202E\u206A\u206B\u200E\u200C\u202E\u206A\u200C\u206E\u200D\u206B\u206E\u200F\u200C\u206F\u206E\u202D\u200F\u206F\u200C\u206C\u200B\u200F\u206E\u206A\u206E\u202B\u202E;

		// Token: 0x04000733 RID: 1843
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<RelationChangeInfo> \u202C\u206C\u202B\u206F\u200C\u202A\u202A\u202A\u206B\u206E\u206C\u202A\u200C\u202A\u202E\u206E\u202C\u206D\u206D\u206F\u202D\u206D\u202B\u200B\u206F\u202A\u206E\u206E\u202B\u200F\u202C\u202C\u206B\u206D\u202A\u206D\u206D\u200B\u206D\u206E\u202E;

		// Token: 0x04000734 RID: 1844
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u206C\u200D\u200B\u206A\u202B\u206A\u200D\u206F\u206D\u202A\u206D\u206A\u202E\u202A\u202A\u206A\u202B\u202E\u206E\u206C\u200C\u200F\u206A\u202A\u200C\u200F\u202C\u206C\u202C\u206E\u206B\u206D\u202B\u206B\u206C\u202E\u202A\u206A\u202B\u202B\u202E;

		// Token: 0x04000735 RID: 1845
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<EconomicEffect> \u202C\u206B\u206E\u200D\u206A\u202C\u202E\u200D\u200D\u202E\u202B\u202D\u202C\u200F\u202B\u206B\u200F\u202A\u206A\u200D\u202E\u200C\u202A\u200E\u202B\u206C\u200F\u206B\u202E\u202E\u202A\u200B\u206C\u206F\u206B\u200B\u200B\u202A\u206D\u200E\u202E;

		// Token: 0x04000736 RID: 1846
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202C\u200D\u202D\u206E\u206E\u200B\u200F\u202B\u200B\u206B\u200C\u206E\u202E\u202E\u206D\u202C\u206D\u202A\u206A\u202E\u202C\u200E\u200C\u200F\u202E\u200B\u206F\u202A\u206C\u200F\u206C\u206F\u202B\u202E\u202C\u206D\u206F\u206E\u206E\u202C\u202E;
	}
}

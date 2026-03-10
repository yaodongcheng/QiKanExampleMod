using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x0200016B RID: 363
	[JsonSerializable]
	public class SettlementPenaltyData
	{
		// Token: 0x17000275 RID: 629
		// (get) Token: 0x06000BEB RID: 3051 RVA: 0x000C9A68 File Offset: 0x000C7C68
		// (set) Token: 0x06000BEC RID: 3052 RVA: 0x000C9A7C File Offset: 0x000C7C7C
		[JsonProperty("prosperity_penalty_per_day")]
		public float ProsperityPenaltyPerDay { get; set; }

		// Token: 0x17000276 RID: 630
		// (get) Token: 0x06000BED RID: 3053 RVA: 0x000C9A90 File Offset: 0x000C7C90
		// (set) Token: 0x06000BEE RID: 3054 RVA: 0x000C9AA4 File Offset: 0x000C7CA4
		[JsonProperty("penalty_duration_days")]
		public int PenaltyDurationDays { get; set; }

		// Token: 0x17000277 RID: 631
		// (get) Token: 0x06000BEF RID: 3055 RVA: 0x000C9AB8 File Offset: 0x000C7CB8
		// (set) Token: 0x06000BF0 RID: 3056 RVA: 0x000C9ACC File Offset: 0x000C7CCC
		[JsonProperty("penalty_reason")]
		public string PenaltyReason { get; set; }

		// Token: 0x0400074A RID: 1866
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206C\u200B\u206A\u206E\u206F\u202E\u200B\u206B\u206C\u202D\u200E\u200F\u200C\u200F\u206C\u206A\u200E\u200F\u200D\u202B\u206A\u202A\u200F\u202B\u206B\u202E\u200D\u206A\u202D\u202D\u206F\u200B\u206E\u202D\u202B\u206C\u200F\u200B\u202E\u200C\u202E;

		// Token: 0x0400074B RID: 1867
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202D\u206D\u200C\u202D\u202D\u206D\u202D\u206E\u206D\u200B\u206C\u202D\u206F\u200F\u202C\u206B\u200F\u200F\u202E\u206F\u200C\u202E\u206D\u202B\u200E\u202C\u202C\u202E\u206D\u206F\u206B\u200B\u202C\u200B\u206E\u206C\u206B\u206E\u206A\u206F\u202E;

		// Token: 0x0400074C RID: 1868
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202E\u206C\u200B\u206F\u206C\u200B\u206B\u202C\u202D\u206D\u200C\u206D\u206F\u206B\u200C\u206E\u202C\u200F\u200C\u200B\u206C\u202D\u200E\u200E\u202C\u202A\u206A\u206B\u200D\u202D\u200D\u200E\u206B\u206B\u202D\u200B\u206C\u202C\u202B\u206D\u202E;
	}
}

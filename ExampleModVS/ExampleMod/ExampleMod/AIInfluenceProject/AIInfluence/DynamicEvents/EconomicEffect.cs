using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x0200016C RID: 364
	[JsonSerializable]
	public class EconomicEffect
	{
		// Token: 0x17000278 RID: 632
		// (get) Token: 0x06000BF2 RID: 3058 RVA: 0x000C9AE0 File Offset: 0x000C7CE0
		// (set) Token: 0x06000BF3 RID: 3059 RVA: 0x000C9AF4 File Offset: 0x000C7CF4
		[JsonProperty("target_type")]
		public string TargetType { get; set; }

		// Token: 0x17000279 RID: 633
		// (get) Token: 0x06000BF4 RID: 3060 RVA: 0x000C9B08 File Offset: 0x000C7D08
		// (set) Token: 0x06000BF5 RID: 3061 RVA: 0x000C9B1C File Offset: 0x000C7D1C
		[JsonProperty("target_id")]
		public string TargetId { get; set; }

		// Token: 0x1700027A RID: 634
		// (get) Token: 0x06000BF6 RID: 3062 RVA: 0x000C9B30 File Offset: 0x000C7D30
		// (set) Token: 0x06000BF7 RID: 3063 RVA: 0x000C9B44 File Offset: 0x000C7D44
		[JsonProperty("target_ids")]
		public List<string> TargetIds { get; set; }

		// Token: 0x1700027B RID: 635
		// (get) Token: 0x06000BF8 RID: 3064 RVA: 0x000C9B58 File Offset: 0x000C7D58
		// (set) Token: 0x06000BF9 RID: 3065 RVA: 0x000C9B6C File Offset: 0x000C7D6C
		[JsonProperty("target_scope")]
		public string TargetScope { get; set; }

		// Token: 0x1700027C RID: 636
		// (get) Token: 0x06000BFA RID: 3066 RVA: 0x000C9B80 File Offset: 0x000C7D80
		// (set) Token: 0x06000BFB RID: 3067 RVA: 0x000C9B94 File Offset: 0x000C7D94
		[JsonProperty("prosperity_delta")]
		public float ProsperityDelta { get; set; }

		// Token: 0x1700027D RID: 637
		// (get) Token: 0x06000BFC RID: 3068 RVA: 0x000C9BA8 File Offset: 0x000C7DA8
		// (set) Token: 0x06000BFD RID: 3069 RVA: 0x000C9BBC File Offset: 0x000C7DBC
		[JsonProperty("prosperity_delta_per_day")]
		public float ProsperityDeltaPerDay { get; set; }

		// Token: 0x1700027E RID: 638
		// (get) Token: 0x06000BFE RID: 3070 RVA: 0x000C9BD0 File Offset: 0x000C7DD0
		// (set) Token: 0x06000BFF RID: 3071 RVA: 0x000C9BE4 File Offset: 0x000C7DE4
		[JsonProperty("food_delta")]
		public float FoodDelta { get; set; }

		// Token: 0x1700027F RID: 639
		// (get) Token: 0x06000C00 RID: 3072 RVA: 0x000C9BF8 File Offset: 0x000C7DF8
		// (set) Token: 0x06000C01 RID: 3073 RVA: 0x000C9C0C File Offset: 0x000C7E0C
		[JsonProperty("food_delta_per_day")]
		public float FoodDeltaPerDay { get; set; }

		// Token: 0x17000280 RID: 640
		// (get) Token: 0x06000C02 RID: 3074 RVA: 0x000C9C20 File Offset: 0x000C7E20
		// (set) Token: 0x06000C03 RID: 3075 RVA: 0x000C9C34 File Offset: 0x000C7E34
		[JsonProperty("security_delta")]
		public float SecurityDelta { get; set; }

		// Token: 0x17000281 RID: 641
		// (get) Token: 0x06000C04 RID: 3076 RVA: 0x000C9C48 File Offset: 0x000C7E48
		// (set) Token: 0x06000C05 RID: 3077 RVA: 0x000C9C5C File Offset: 0x000C7E5C
		[JsonProperty("loyalty_delta")]
		public float LoyaltyDelta { get; set; }

		// Token: 0x17000282 RID: 642
		// (get) Token: 0x06000C06 RID: 3078 RVA: 0x000C9C70 File Offset: 0x000C7E70
		// (set) Token: 0x06000C07 RID: 3079 RVA: 0x000C9C84 File Offset: 0x000C7E84
		[JsonProperty("income_multiplier")]
		public float IncomeMultiplier { get; set; } = 1f;

		// Token: 0x17000283 RID: 643
		// (get) Token: 0x06000C08 RID: 3080 RVA: 0x000C9C98 File Offset: 0x000C7E98
		// (set) Token: 0x06000C09 RID: 3081 RVA: 0x000C9CAC File Offset: 0x000C7EAC
		[JsonProperty("duration_days")]
		public int DurationDays { get; set; }

		// Token: 0x17000284 RID: 644
		// (get) Token: 0x06000C0A RID: 3082 RVA: 0x000C9CC0 File Offset: 0x000C7EC0
		// (set) Token: 0x06000C0B RID: 3083 RVA: 0x000C9CD4 File Offset: 0x000C7ED4
		[JsonProperty("reason")]
		public string Reason { get; set; }

		// Token: 0x0400074D RID: 1869
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u200D\u206E\u200D\u206C\u206B\u200F\u200F\u206C\u200D\u200F\u206A\u200D\u202E\u200D\u200B\u206E\u202C\u206E\u200C\u206D\u206C\u206E\u202C\u202B\u202B\u202C\u200B\u206E\u200C\u206C\u200F\u200B\u200F\u200F\u200B\u202E\u206A\u202E\u206E\u202E;

		// Token: 0x0400074E RID: 1870
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u206A\u200C\u200C\u202A\u206F\u200F\u206C\u206D\u200F\u200F\u202D\u200E\u202C\u200E\u202D\u202E\u206A\u200C\u200D\u200E\u200D\u202B\u206E\u206E\u200F\u200B\u202C\u202E\u206B\u202C\u200B\u200D\u202E\u206A\u200C\u202D\u202E\u202E\u206F\u202E;

		// Token: 0x0400074F RID: 1871
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200E\u202D\u200B\u202C\u202A\u206E\u200B\u200F\u206C\u202A\u206F\u200C\u206A\u200D\u206D\u206C\u200B\u206A\u200F\u206B\u202A\u200B\u206A\u206D\u200D\u202C\u202A\u206C\u202C\u202E\u206D\u200F\u200F\u206A\u200F\u206C\u200B\u200C\u200E\u206C\u202E;

		// Token: 0x04000750 RID: 1872
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u202C\u200B\u206C\u200D\u206E\u206A\u200B\u206B\u206A\u206D\u202A\u202D\u206D\u200B\u200B\u200C\u200B\u202D\u200F\u202B\u200E\u202B\u202C\u200E\u200D\u200B\u202B\u206F\u200C\u200D\u200C\u206A\u206F\u200F\u206E\u206F\u200E\u206A\u200C\u202E;

		// Token: 0x04000751 RID: 1873
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202C\u200D\u206C\u200E\u200F\u202E\u200F\u200C\u206A\u206C\u206B\u206F\u200D\u200B\u200C\u202C\u206A\u202B\u206C\u206B\u202A\u206C\u202C\u206C\u206C\u206E\u200D\u200E\u202B\u200D\u202D\u202B\u200C\u206F\u202E\u200F\u202C\u200F\u206E\u200C\u202E;

		// Token: 0x04000752 RID: 1874
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202C\u202C\u200C\u200E\u206A\u202C\u200B\u206D\u206F\u200B\u200D\u202C\u206A\u206B\u200C\u206E\u202B\u206F\u206E\u206C\u200E\u206C\u200E\u200B\u206A\u206E\u200C\u202E\u206C\u206B\u202A\u206C\u200D\u206F\u202D\u206A\u200B\u200F\u202D\u206F\u202E;

		// Token: 0x04000753 RID: 1875
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206B\u206F\u202E\u206C\u200F\u202E\u206F\u206F\u206E\u200E\u200B\u206B\u200D\u202E\u202B\u206A\u206A\u206F\u206B\u200C\u200E\u202C\u200F\u206F\u200D\u206C\u202C\u206A\u206E\u200D\u206B\u202D\u202E\u200E\u200B\u206F\u200C\u206B\u202A\u206F\u202E;

		// Token: 0x04000754 RID: 1876
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200E\u202A\u202C\u206E\u202B\u200F\u202A\u200E\u206F\u202A\u206E\u206D\u202C\u200F\u202E\u202A\u202B\u200C\u206C\u202C\u202D\u206A\u206D\u202D\u206F\u200E\u206F\u202C\u202B\u206E\u206D\u202D\u206B\u202E\u202D\u206E\u202C\u200D\u200B\u200C\u202E;

		// Token: 0x04000755 RID: 1877
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206A\u202B\u202C\u202E\u202D\u202D\u200F\u206F\u200F\u202E\u206B\u206D\u200B\u200F\u200C\u202D\u200D\u200D\u202D\u202D\u206D\u200D\u206A\u206C\u202D\u206E\u200B\u202B\u206E\u200B\u200C\u206B\u206A\u200C\u200F\u200D\u202B\u206D\u200B\u200F\u202E;

		// Token: 0x04000756 RID: 1878
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202D\u200B\u206C\u206E\u206D\u202B\u206F\u206C\u200C\u200E\u206B\u200C\u206E\u200C\u202C\u202A\u202A\u206E\u202C\u202A\u206A\u206B\u206F\u206B\u206E\u202A\u202B\u206E\u206C\u206D\u202A\u206C\u206B\u206A\u202B\u202E\u200B\u200E\u200C\u206B\u202E;

		// Token: 0x04000757 RID: 1879
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200F\u206A\u206D\u200E\u206F\u206B\u206B\u206B\u202C\u200B\u202E\u200C\u200D\u200E\u206C\u200C\u202B\u200E\u206E\u206C\u202E\u200D\u202D\u200E\u206B\u202A\u200C\u202C\u202D\u206F\u206B\u202E\u200D\u200D\u206E\u202A\u200D\u206D\u206D\u202A\u202E;

		// Token: 0x04000758 RID: 1880
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202A\u202A\u206D\u200C\u200E\u202B\u202E\u200B\u206B\u200F\u202C\u202C\u206F\u202C\u206B\u200C\u206C\u202D\u200C\u200F\u206C\u206B\u206E\u202E\u200F\u206B\u206C\u200F\u206E\u200D\u200C\u206B\u200E\u202B\u206D\u200D\u206D\u202A\u200C\u206F\u202E;

		// Token: 0x04000759 RID: 1881
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u206A\u206C\u206D\u206C\u200B\u200C\u206F\u206E\u202C\u206C\u202E\u202D\u200D\u206C\u200D\u206A\u202C\u200B\u206D\u200C\u202D\u200E\u206A\u200D\u200C\u200C\u202E\u202E\u202D\u202B\u206E\u202C\u200C\u206B\u202A\u202E\u200D\u206E\u202C\u202E;
	}
}

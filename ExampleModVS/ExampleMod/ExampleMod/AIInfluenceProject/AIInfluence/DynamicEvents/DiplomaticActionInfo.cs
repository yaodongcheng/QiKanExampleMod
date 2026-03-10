using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x02000168 RID: 360
	[JsonSerializable]
	public class DiplomaticActionInfo
	{
		// Token: 0x17000265 RID: 613
		// (get) Token: 0x06000BC7 RID: 3015 RVA: 0x000C96F8 File Offset: 0x000C78F8
		// (set) Token: 0x06000BC8 RID: 3016 RVA: 0x000C970C File Offset: 0x000C790C
		[JsonProperty("action")]
		public DiplomaticAction Action { get; set; }

		// Token: 0x17000266 RID: 614
		// (get) Token: 0x06000BC9 RID: 3017 RVA: 0x000C9720 File Offset: 0x000C7920
		// (set) Token: 0x06000BCA RID: 3018 RVA: 0x000C9734 File Offset: 0x000C7934
		[JsonProperty("source_kingdom_id")]
		public string SourceKingdomId { get; set; }

		// Token: 0x17000267 RID: 615
		// (get) Token: 0x06000BCB RID: 3019 RVA: 0x000C9748 File Offset: 0x000C7948
		// (set) Token: 0x06000BCC RID: 3020 RVA: 0x000C975C File Offset: 0x000C795C
		[JsonProperty("target_kingdom_id")]
		public string TargetKingdomId { get; set; }

		// Token: 0x17000268 RID: 616
		// (get) Token: 0x06000BCD RID: 3021 RVA: 0x000C9770 File Offset: 0x000C7970
		// (set) Token: 0x06000BCE RID: 3022 RVA: 0x000C9784 File Offset: 0x000C7984
		[JsonProperty("target_clan_id")]
		public string TargetClanId { get; set; }

		// Token: 0x17000269 RID: 617
		// (get) Token: 0x06000BCF RID: 3023 RVA: 0x000C9798 File Offset: 0x000C7998
		// (set) Token: 0x06000BD0 RID: 3024 RVA: 0x000C97AC File Offset: 0x000C79AC
		[JsonProperty("reason")]
		public string Reason { get; set; }

		// Token: 0x1700026A RID: 618
		// (get) Token: 0x06000BD1 RID: 3025 RVA: 0x000C97C0 File Offset: 0x000C79C0
		// (set) Token: 0x06000BD2 RID: 3026 RVA: 0x000C97D4 File Offset: 0x000C79D4
		[JsonProperty("settlement_id")]
		public string SettlementId { get; set; }

		// Token: 0x1700026B RID: 619
		// (get) Token: 0x06000BD3 RID: 3027 RVA: 0x000C97E8 File Offset: 0x000C79E8
		// (set) Token: 0x06000BD4 RID: 3028 RVA: 0x000C97FC File Offset: 0x000C79FC
		[JsonProperty("daily_tribute_amount")]
		public int DailyTributeAmount { get; set; }

		// Token: 0x1700026C RID: 620
		// (get) Token: 0x06000BD5 RID: 3029 RVA: 0x000C9810 File Offset: 0x000C7A10
		// (set) Token: 0x06000BD6 RID: 3030 RVA: 0x000C9824 File Offset: 0x000C7A24
		[JsonProperty("tribute_duration_days")]
		public int TributeDurationDays { get; set; }

		// Token: 0x1700026D RID: 621
		// (get) Token: 0x06000BD7 RID: 3031 RVA: 0x000C9838 File Offset: 0x000C7A38
		// (set) Token: 0x06000BD8 RID: 3032 RVA: 0x000C984C File Offset: 0x000C7A4C
		[JsonProperty("reparations_amount")]
		public int ReparationsAmount { get; set; }

		// Token: 0x1700026E RID: 622
		// (get) Token: 0x06000BD9 RID: 3033 RVA: 0x000C9860 File Offset: 0x000C7A60
		// (set) Token: 0x06000BDA RID: 3034 RVA: 0x000C9874 File Offset: 0x000C7A74
		[JsonProperty("trade_agreement_duration_years")]
		public float TradeAgreementDurationYears { get; set; } = 1f;

		// Token: 0x0400073B RID: 1851
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private DiplomaticAction \u202B\u200C\u202A\u202A\u200D\u202C\u202A\u202A\u206D\u200F\u200B\u200C\u202C\u200C\u202E\u202E\u200E\u202D\u206E\u200B\u206D\u206F\u202B\u200D\u206F\u200B\u200D\u206F\u202A\u202E\u200C\u206C\u202C\u206A\u200F\u202E\u200B\u200F\u202E\u206D\u202E;

		// Token: 0x0400073C RID: 1852
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u200C\u200F\u206B\u200C\u200D\u206B\u206B\u200E\u202B\u206A\u202C\u206B\u202E\u200D\u206C\u202B\u200B\u200D\u200B\u206E\u200B\u200C\u206E\u206A\u206A\u200D\u206D\u200E\u202C\u200B\u206B\u202D\u202E\u202E\u202C\u206A\u206F\u202A\u206F\u202E;

		// Token: 0x0400073D RID: 1853
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u200F\u202D\u202D\u200C\u206E\u206E\u200E\u206D\u200D\u200B\u200C\u200F\u202D\u206C\u206E\u206E\u202D\u202E\u206F\u206B\u206F\u206F\u206E\u206F\u206A\u206C\u206A\u200F\u206D\u206F\u200D\u206F\u206D\u202C\u202E\u206E\u206B\u206E\u202A\u202E;

		// Token: 0x0400073E RID: 1854
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u200D\u206A\u206A\u202C\u202B\u202D\u206E\u202A\u202A\u206C\u206E\u206B\u202D\u200E\u206E\u200C\u206F\u202B\u200E\u206C\u200F\u206E\u206D\u202D\u200B\u206A\u200D\u202C\u202C\u200B\u202B\u200F\u200E\u206C\u202D\u202E\u206E\u200E\u206B\u202E;

		// Token: 0x0400073F RID: 1855
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u202B\u206B\u206F\u200D\u202C\u206C\u206A\u202D\u206E\u202E\u206A\u206B\u206F\u202C\u206A\u202B\u206D\u202D\u206C\u206B\u206F\u206A\u202C\u200F\u206D\u200F\u206F\u200D\u200E\u206F\u206A\u200B\u206C\u206F\u202B\u200B\u206E\u200B\u200C\u202E;

		// Token: 0x04000740 RID: 1856
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u202E\u206D\u200C\u206D\u206F\u200D\u200D\u202A\u206C\u206D\u200F\u206A\u206F\u200B\u206B\u206A\u200B\u200E\u200E\u206A\u206F\u202E\u200E\u202A\u200D\u200D\u200C\u202E\u200C\u200C\u200C\u206E\u202D\u200D\u206B\u202A\u200B\u200B\u206F\u202E;

		// Token: 0x04000741 RID: 1857
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200F\u206A\u206D\u202E\u200F\u200E\u206D\u206F\u202E\u206C\u206D\u206B\u206E\u200F\u200F\u200E\u202B\u206F\u202D\u206B\u206F\u202E\u206C\u202D\u202C\u202B\u206A\u200E\u202D\u200D\u206A\u202A\u206F\u202D\u206C\u206A\u200F\u200B\u200F\u202E;

		// Token: 0x04000742 RID: 1858
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200B\u200C\u206A\u200F\u206E\u206B\u202A\u200F\u202B\u200D\u202A\u202C\u200E\u206F\u202A\u200F\u200C\u202C\u202E\u202D\u200F\u206D\u202B\u202E\u202E\u200E\u200B\u200C\u206A\u206A\u200C\u200E\u200D\u202C\u202A\u202D\u206C\u206F\u206B\u202E;

		// Token: 0x04000743 RID: 1859
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202E\u206D\u202D\u202D\u200E\u200E\u206C\u206D\u206A\u200C\u206D\u200B\u206E\u200E\u202E\u200B\u200E\u200C\u202C\u202A\u206F\u200D\u202C\u202D\u206B\u202C\u202E\u200F\u202C\u200C\u206C\u202A\u200D\u206D\u206E\u200E\u202B\u206B\u202E\u202C\u202E;

		// Token: 0x04000744 RID: 1860
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200F\u200F\u202C\u200F\u202A\u206C\u202E\u206B\u206D\u200F\u202D\u206C\u202D\u202E\u206A\u202C\u202D\u200F\u200D\u202B\u206A\u206A\u206F\u206B\u200B\u202E\u202D\u200D\u202B\u206B\u206B\u200D\u200F\u202A\u200C\u206F\u200D\u202D\u206F\u202E;
	}
}

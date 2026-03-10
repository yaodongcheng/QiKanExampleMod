using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x02000164 RID: 356
	[JsonSerializable]
	public class KingdomStatement
	{
		// Token: 0x17000247 RID: 583
		// (get) Token: 0x06000B88 RID: 2952 RVA: 0x000C9184 File Offset: 0x000C7384
		// (set) Token: 0x06000B89 RID: 2953 RVA: 0x000C9198 File Offset: 0x000C7398
		[JsonProperty("kingdom_id")]
		public string KingdomId { get; set; }

		// Token: 0x17000248 RID: 584
		// (get) Token: 0x06000B8A RID: 2954 RVA: 0x000C91AC File Offset: 0x000C73AC
		// (set) Token: 0x06000B8B RID: 2955 RVA: 0x000C91C0 File Offset: 0x000C73C0
		[JsonProperty("statement_text")]
		public string StatementText { get; set; }

		// Token: 0x17000249 RID: 585
		// (get) Token: 0x06000B8C RID: 2956 RVA: 0x000C91D4 File Offset: 0x000C73D4
		// (set) Token: 0x06000B8D RID: 2957 RVA: 0x000C91E8 File Offset: 0x000C73E8
		[JsonProperty("action")]
		public DiplomaticAction Action { get; set; } = DiplomaticAction.None;

		// Token: 0x1700024A RID: 586
		// (get) Token: 0x06000B8E RID: 2958 RVA: 0x000C91FC File Offset: 0x000C73FC
		// (set) Token: 0x06000B8F RID: 2959 RVA: 0x000C9210 File Offset: 0x000C7410
		[JsonProperty("actions")]
		public List<DiplomaticAction> Actions { get; set; } = new List<DiplomaticAction>();

		// Token: 0x1700024B RID: 587
		// (get) Token: 0x06000B90 RID: 2960 RVA: 0x000C9224 File Offset: 0x000C7424
		// (set) Token: 0x06000B91 RID: 2961 RVA: 0x000C9238 File Offset: 0x000C7438
		[JsonProperty("target_kingdom_id")]
		public string TargetKingdomId { get; set; }

		// Token: 0x1700024C RID: 588
		// (get) Token: 0x06000B92 RID: 2962 RVA: 0x000C924C File Offset: 0x000C744C
		// (set) Token: 0x06000B93 RID: 2963 RVA: 0x000C9260 File Offset: 0x000C7460
		[JsonProperty("target_kingdom_ids")]
		public List<string> TargetKingdomIds { get; set; } = new List<string>();

		// Token: 0x1700024D RID: 589
		// (get) Token: 0x06000B94 RID: 2964 RVA: 0x000C9274 File Offset: 0x000C7474
		// (set) Token: 0x06000B95 RID: 2965 RVA: 0x000C9288 File Offset: 0x000C7488
		[JsonProperty("target_clan_id")]
		public string TargetClanId { get; set; }

		// Token: 0x1700024E RID: 590
		// (get) Token: 0x06000B96 RID: 2966 RVA: 0x000C929C File Offset: 0x000C749C
		// (set) Token: 0x06000B97 RID: 2967 RVA: 0x000C92B0 File Offset: 0x000C74B0
		[JsonProperty("reason")]
		public string Reason { get; set; }

		// Token: 0x1700024F RID: 591
		// (get) Token: 0x06000B98 RID: 2968 RVA: 0x000C92C4 File Offset: 0x000C74C4
		// (set) Token: 0x06000B99 RID: 2969 RVA: 0x000C92D8 File Offset: 0x000C74D8
		[JsonProperty("campaign_days")]
		public float CampaignDays { get; set; }

		// Token: 0x17000250 RID: 592
		// (get) Token: 0x06000B9A RID: 2970 RVA: 0x000C92EC File Offset: 0x000C74EC
		// (set) Token: 0x06000B9B RID: 2971 RVA: 0x000C9320 File Offset: 0x000C7520
		[JsonIgnore]
		public CampaignTime Timestamp
		{
			get
			{
				return \u200B\u202B\u202D\u202C\u206F\u206D\u200F\u206E\u200E\u206B\u202B\u206B\u200B\u206A\u206D\u206D\u200E\u202D\u206A\u206F\u202E\u206F\u206A\u202C\u206D\u202C\u200D\u206A\u200B\u202E\u206A\u202A\u206B\u202E\u206F\u206C\u206E\u200F\u202A\u200D\u202E.àz>\u00F7\u0081(this.CampaignDays - (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays);
			}
			set
			{
				this.CampaignDays = (float)value.ToDays;
			}
		}

		// Token: 0x17000251 RID: 593
		// (get) Token: 0x06000B9C RID: 2972 RVA: 0x000C933C File Offset: 0x000C753C
		// (set) Token: 0x06000B9D RID: 2973 RVA: 0x000C9350 File Offset: 0x000C7550
		[JsonProperty("event_id")]
		public string EventId { get; set; }

		// Token: 0x17000252 RID: 594
		// (get) Token: 0x06000B9E RID: 2974 RVA: 0x000C9364 File Offset: 0x000C7564
		// (set) Token: 0x06000B9F RID: 2975 RVA: 0x000C9378 File Offset: 0x000C7578
		[JsonProperty("settlement_id")]
		public string SettlementId { get; set; }

		// Token: 0x17000253 RID: 595
		// (get) Token: 0x06000BA0 RID: 2976 RVA: 0x000C938C File Offset: 0x000C758C
		// (set) Token: 0x06000BA1 RID: 2977 RVA: 0x000C93A0 File Offset: 0x000C75A0
		[JsonProperty("daily_tribute_amount")]
		public int DailyTributeAmount { get; set; }

		// Token: 0x17000254 RID: 596
		// (get) Token: 0x06000BA2 RID: 2978 RVA: 0x000C93B4 File Offset: 0x000C75B4
		// (set) Token: 0x06000BA3 RID: 2979 RVA: 0x000C93C8 File Offset: 0x000C75C8
		[JsonProperty("tribute_duration_days")]
		public int TributeDurationDays { get; set; }

		// Token: 0x17000255 RID: 597
		// (get) Token: 0x06000BA4 RID: 2980 RVA: 0x000C93DC File Offset: 0x000C75DC
		// (set) Token: 0x06000BA5 RID: 2981 RVA: 0x000C93F0 File Offset: 0x000C75F0
		[JsonProperty("reparations_amount")]
		public int ReparationsAmount { get; set; }

		// Token: 0x17000256 RID: 598
		// (get) Token: 0x06000BA6 RID: 2982 RVA: 0x000C9404 File Offset: 0x000C7604
		// (set) Token: 0x06000BA7 RID: 2983 RVA: 0x000C9418 File Offset: 0x000C7618
		[JsonProperty("trade_agreement_duration_years")]
		public float TradeAgreementDurationYears { get; set; } = 1f;

		// Token: 0x04000704 RID: 1796
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u206F\u200E\u202D\u202E\u200F\u206B\u206A\u202D\u200C\u200E\u202C\u200F\u200D\u202A\u206F\u206D\u206D\u202E\u206B\u200D\u202D\u202B\u200D\u202E\u206C\u200D\u206E\u200B\u206A\u202A\u200B\u200F\u206A\u206F\u200C\u202C\u206E\u202D\u200D\u202E;

		// Token: 0x04000705 RID: 1797
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u206D\u200F\u200B\u200F\u206C\u202D\u200E\u202B\u202C\u206A\u206E\u206C\u202C\u200D\u202B\u206B\u206E\u206C\u206F\u200D\u206B\u206A\u206C\u202D\u200F\u206F\u206C\u200D\u202E\u206A\u206C\u200E\u200D\u202D\u206F\u206B\u200F\u200D\u200E\u202E;

		// Token: 0x04000706 RID: 1798
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private DiplomaticAction \u202C\u202A\u202D\u200F\u202C\u200B\u202C\u206F\u200C\u206A\u206B\u206C\u200C\u206B\u200E\u202E\u206B\u206E\u202D\u200C\u202A\u200F\u200D\u202A\u206B\u200D\u200C\u206A\u200F\u206E\u206D\u202E\u200B\u206A\u202B\u206A\u206B\u206A\u200C\u200D\u202E;

		// Token: 0x04000707 RID: 1799
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<DiplomaticAction> \u206B\u202D\u200E\u202E\u206B\u202E\u206F\u202C\u206E\u202D\u200B\u200E\u206E\u202B\u206C\u202E\u200B\u200D\u202D\u206A\u206F\u206D\u200E\u206A\u200B\u202A\u202C\u206F\u206B\u202A\u206B\u200C\u202B\u206F\u200F\u206B\u206C\u206A\u202C\u206B\u202E;

		// Token: 0x04000708 RID: 1800
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u202D\u206B\u200B\u206B\u200B\u202C\u202A\u206D\u206B\u206D\u202C\u200E\u202C\u200E\u202A\u202B\u200E\u200F\u200F\u200D\u206D\u202A\u202A\u202E\u206B\u200F\u200E\u200C\u202C\u206F\u206F\u200C\u202A\u206C\u206C\u202B\u206C\u200F\u206F\u202E;

		// Token: 0x04000709 RID: 1801
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u206C\u206D\u206A\u206B\u206E\u202A\u202A\u200D\u200C\u200E\u202B\u206E\u200F\u200B\u202E\u200F\u206F\u200E\u206C\u206B\u206D\u202A\u202A\u202D\u202D\u202D\u206C\u206B\u200E\u206F\u206D\u200F\u206F\u206D\u202A\u206A\u200B\u206E\u206E\u206F\u202E;

		// Token: 0x0400070A RID: 1802
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u202D\u206F\u200E\u206A\u206F\u200E\u202B\u200F\u206C\u200B\u200D\u200D\u202C\u206E\u202D\u206E\u202E\u202C\u200D\u200E\u200F\u206E\u206E\u206E\u206E\u206A\u202B\u200E\u202E\u202D\u206B\u206C\u200B\u202D\u206C\u206C\u202C\u206F\u206B\u202E;

		// Token: 0x0400070B RID: 1803
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u202D\u206A\u200B\u200C\u206E\u200B\u206F\u206B\u202D\u200B\u206B\u206D\u200D\u202A\u202C\u202D\u202D\u206D\u200E\u206C\u202B\u206F\u202B\u202A\u200D\u206C\u200B\u200E\u200F\u200B\u200E\u200E\u202C\u206B\u200D\u206D\u206E\u200B\u202C\u202E;

		// Token: 0x0400070C RID: 1804
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202B\u202A\u206F\u206F\u200D\u200D\u202D\u202B\u200F\u202A\u202B\u206A\u206F\u202B\u206C\u200C\u200E\u206D\u202D\u200C\u202C\u206D\u200F\u206A\u202C\u202E\u202D\u202A\u206E\u206D\u202B\u206F\u206B\u206E\u200E\u206A\u206F\u202D\u206B\u202E;

		// Token: 0x0400070D RID: 1805
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u200B\u206C\u200D\u206D\u200B\u206D\u206D\u202A\u200D\u200E\u206A\u200B\u206F\u200C\u206B\u206D\u206B\u202E\u206A\u206C\u200C\u206A\u200C\u200B\u206B\u206C\u202E\u206B\u200C\u206C\u206C\u206A\u200E\u206C\u202C\u206F\u206F\u200F\u202B\u202E;

		// Token: 0x0400070E RID: 1806
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u202E\u202C\u202A\u200D\u202A\u206E\u206C\u206E\u202C\u202D\u200C\u206F\u200B\u200E\u202C\u206F\u200F\u206B\u200C\u206A\u200B\u206A\u206B\u202C\u206B\u206A\u202A\u200D\u202E\u202C\u202A\u200D\u206B\u202A\u200F\u202D\u206B\u200E\u206F\u202E;

		// Token: 0x0400070F RID: 1807
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202D\u206D\u202C\u202A\u200B\u200E\u206B\u202A\u206C\u202E\u206A\u200F\u200F\u206E\u202D\u202B\u206A\u206A\u202C\u200D\u202B\u202E\u200D\u202E\u206C\u206E\u200B\u206D\u200C\u200D\u202B\u206F\u200C\u200D\u202A\u206D\u202B\u202C\u206E\u202E\u202E;

		// Token: 0x04000710 RID: 1808
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200E\u206E\u202A\u200C\u206E\u206A\u206A\u200C\u200D\u206C\u206F\u202A\u202E\u206A\u206E\u206D\u206A\u200C\u200B\u206E\u206A\u200F\u202C\u202E\u202D\u200C\u200C\u200F\u206B\u202C\u200E\u206F\u202A\u200B\u200B\u202E\u206F\u202B\u200F\u200D\u202E;

		// Token: 0x04000711 RID: 1809
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206E\u202B\u202A\u200C\u200D\u202B\u206F\u200B\u200E\u206E\u202C\u206E\u202C\u200E\u206D\u200E\u202A\u206F\u206B\u200C\u202A\u202A\u200D\u200B\u202E\u206E\u206B\u206F\u202C\u202D\u202C\u202D\u206F\u200C\u206A\u200F\u202A\u202A\u206E\u206A\u202E;

		// Token: 0x04000712 RID: 1810
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202E\u206D\u202A\u200F\u202B\u200E\u200F\u206E\u202B\u202B\u202B\u202D\u202A\u200D\u202B\u206D\u200C\u206E\u202C\u206C\u202B\u206E\u206F\u200F\u206C\u200D\u206A\u200B\u206D\u202C\u206A\u206D\u202A\u206D\u200F\u200F\u202C\u202D\u202D\u202E;
	}
}

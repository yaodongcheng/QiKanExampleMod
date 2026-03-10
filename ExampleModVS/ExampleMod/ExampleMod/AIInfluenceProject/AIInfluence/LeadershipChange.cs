using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence
{
	// Token: 0x0200005E RID: 94
	public class LeadershipChange
	{
		// Token: 0x1700002B RID: 43
		// (get) Token: 0x06000228 RID: 552 RVA: 0x000503D4 File Offset: 0x0004E5D4
		// (set) Token: 0x06000229 RID: 553 RVA: 0x000503E8 File Offset: 0x0004E5E8
		[JsonProperty("PreviousLeaderId")]
		public string PreviousLeaderId { get; set; }

		// Token: 0x1700002C RID: 44
		// (get) Token: 0x0600022A RID: 554 RVA: 0x000503FC File Offset: 0x0004E5FC
		// (set) Token: 0x0600022B RID: 555 RVA: 0x00050410 File Offset: 0x0004E610
		[JsonProperty("PreviousLeaderName")]
		public string PreviousLeaderName { get; set; }

		// Token: 0x1700002D RID: 45
		// (get) Token: 0x0600022C RID: 556 RVA: 0x00050424 File Offset: 0x0004E624
		// (set) Token: 0x0600022D RID: 557 RVA: 0x00050438 File Offset: 0x0004E638
		[JsonProperty("PreviousLeaderClanId")]
		public string PreviousLeaderClanId { get; set; }

		// Token: 0x1700002E RID: 46
		// (get) Token: 0x0600022E RID: 558 RVA: 0x0005044C File Offset: 0x0004E64C
		// (set) Token: 0x0600022F RID: 559 RVA: 0x00050460 File Offset: 0x0004E660
		[JsonProperty("PreviousLeaderClanName")]
		public string PreviousLeaderClanName { get; set; }

		// Token: 0x1700002F RID: 47
		// (get) Token: 0x06000230 RID: 560 RVA: 0x00050474 File Offset: 0x0004E674
		// (set) Token: 0x06000231 RID: 561 RVA: 0x00050488 File Offset: 0x0004E688
		[JsonProperty("NewLeaderId")]
		public string NewLeaderId { get; set; }

		// Token: 0x17000030 RID: 48
		// (get) Token: 0x06000232 RID: 562 RVA: 0x0005049C File Offset: 0x0004E69C
		// (set) Token: 0x06000233 RID: 563 RVA: 0x000504B0 File Offset: 0x0004E6B0
		[JsonProperty("NewLeaderName")]
		public string NewLeaderName { get; set; }

		// Token: 0x17000031 RID: 49
		// (get) Token: 0x06000234 RID: 564 RVA: 0x000504C4 File Offset: 0x0004E6C4
		// (set) Token: 0x06000235 RID: 565 RVA: 0x000504D8 File Offset: 0x0004E6D8
		[JsonProperty("NewLeaderClanId")]
		public string NewLeaderClanId { get; set; }

		// Token: 0x17000032 RID: 50
		// (get) Token: 0x06000236 RID: 566 RVA: 0x000504EC File Offset: 0x0004E6EC
		// (set) Token: 0x06000237 RID: 567 RVA: 0x00050500 File Offset: 0x0004E700
		[JsonProperty("NewLeaderClanName")]
		public string NewLeaderClanName { get; set; }

		// Token: 0x17000033 RID: 51
		// (get) Token: 0x06000238 RID: 568 RVA: 0x00050514 File Offset: 0x0004E714
		// (set) Token: 0x06000239 RID: 569 RVA: 0x00050528 File Offset: 0x0004E728
		[JsonProperty("ChangeDate")]
		public CampaignTime ChangeDate { get; set; }

		// Token: 0x17000034 RID: 52
		// (get) Token: 0x0600023A RID: 570 RVA: 0x0005053C File Offset: 0x0004E73C
		// (set) Token: 0x0600023B RID: 571 RVA: 0x00050550 File Offset: 0x0004E750
		[JsonProperty("ChangeReason")]
		public string ChangeReason { get; set; }

		// Token: 0x17000035 RID: 53
		// (get) Token: 0x0600023C RID: 572 RVA: 0x00050564 File Offset: 0x0004E764
		// (set) Token: 0x0600023D RID: 573 RVA: 0x00050578 File Offset: 0x0004E778
		[JsonProperty("KillerId")]
		public string KillerId { get; set; }

		// Token: 0x17000036 RID: 54
		// (get) Token: 0x0600023E RID: 574 RVA: 0x0005058C File Offset: 0x0004E78C
		// (set) Token: 0x0600023F RID: 575 RVA: 0x000505A0 File Offset: 0x0004E7A0
		[JsonProperty("KillerName")]
		public string KillerName { get; set; }

		// Token: 0x04000187 RID: 391
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u206F\u200F\u202B\u206B\u202B\u200D\u206B\u202D\u202D\u206C\u206D\u200C\u200F\u200D\u200F\u200C\u200E\u206B\u202B\u202D\u200F\u202C\u200D\u200E\u200C\u200B\u202D\u202D\u202D\u202C\u200D\u206C\u202C\u206F\u206B\u206A\u206F\u206C\u206C\u202E;

		// Token: 0x04000188 RID: 392
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u200C\u202A\u202D\u200C\u206C\u206C\u206E\u202D\u206D\u206A\u202D\u206C\u206A\u202C\u200F\u202E\u206D\u200E\u206B\u202C\u206C\u200D\u206A\u206F\u200C\u202C\u200B\u202D\u206C\u200C\u206E\u206A\u202E\u202A\u206E\u206F\u200B\u206C\u202D\u202E;

		// Token: 0x04000189 RID: 393
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u200F\u200C\u206C\u202E\u200B\u200D\u206C\u202A\u202A\u206C\u206B\u206B\u202D\u202E\u200C\u206D\u202E\u202A\u200E\u200B\u206D\u202A\u206E\u200D\u206C\u200B\u206C\u202E\u202E\u202D\u206A\u200D\u200C\u206A\u200F\u206E\u202D\u202C\u202E\u202E;

		// Token: 0x0400018A RID: 394
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u206F\u200F\u202D\u206E\u200D\u200D\u202B\u202E\u200F\u202E\u206C\u200D\u200E\u206D\u206E\u200C\u200C\u200D\u206E\u206A\u202D\u202B\u200D\u206B\u200F\u200C\u206B\u200E\u202A\u206E\u200E\u200E\u200B\u202D\u200C\u200F\u206A\u202D\u206A\u202E;

		// Token: 0x0400018B RID: 395
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u206D\u202B\u202E\u206A\u202A\u200F\u202A\u200E\u202C\u200F\u206E\u202E\u202E\u200F\u200B\u202C\u200D\u200B\u206D\u202E\u200C\u206E\u200E\u200F\u202C\u206B\u202E\u202A\u200F\u206A\u200E\u206E\u200D\u202E\u202D\u202D\u200C\u206A\u206C\u202E;

		// Token: 0x0400018C RID: 396
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u206A\u206A\u202C\u206D\u206D\u206D\u202C\u200B\u202D\u200B\u200B\u202E\u202E\u200C\u206D\u206F\u202C\u206F\u202E\u206B\u202B\u206A\u202A\u200E\u200E\u202D\u202E\u202A\u200B\u206D\u202D\u206B\u206A\u202D\u200D\u202B\u200B\u200C\u206F\u202E;

		// Token: 0x0400018D RID: 397
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u200D\u202C\u206C\u206C\u200D\u202B\u202C\u200D\u206F\u200F\u202B\u206B\u200D\u206E\u202E\u206B\u200E\u200C\u206D\u206C\u200D\u202D\u202B\u206E\u202E\u200C\u202A\u200F\u202C\u200C\u206E\u206E\u200C\u206F\u202A\u200D\u206F\u202A\u202B\u202E;

		// Token: 0x0400018E RID: 398
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u200D\u206F\u202D\u202A\u206F\u200D\u202B\u206B\u202C\u202B\u200F\u202A\u200D\u200C\u202C\u200F\u206F\u200B\u200B\u202D\u206C\u202D\u202A\u206B\u202D\u206A\u206C\u202C\u200B\u200E\u202B\u206C\u200F\u206E\u206E\u200D\u206F\u206D\u206F\u202E;

		// Token: 0x0400018F RID: 399
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u206D\u202B\u206E\u206B\u206C\u202A\u200E\u200F\u206A\u200F\u200B\u202C\u206B\u200F\u202D\u202C\u200B\u206F\u200B\u200F\u202E\u200C\u202D\u202A\u206E\u200F\u206B\u202B\u206B\u206D\u206F\u206B\u200E\u200C\u206E\u200E\u202C\u202E\u200B\u202D\u202E;

		// Token: 0x04000190 RID: 400
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u206F\u202A\u206E\u202B\u202E\u202C\u202D\u206E\u202A\u202A\u202B\u202C\u202D\u206D\u202D\u202E\u206E\u200B\u202D\u202C\u200B\u206F\u206B\u202D\u200F\u206B\u202D\u200D\u206A\u202A\u206D\u202C\u206F\u202B\u202D\u206B\u206A\u202A\u200F\u202E;

		// Token: 0x04000191 RID: 401
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u206E\u206B\u206D\u206B\u206A\u206C\u206B\u200B\u200C\u202B\u200B\u200D\u202B\u206A\u200D\u200C\u206A\u200D\u202C\u206D\u200E\u206E\u202B\u202E\u206C\u202E\u202E\u206C\u202D\u202D\u200F\u206B\u202E\u206B\u202C\u202D\u202D\u206B\u206A\u202E;

		// Token: 0x04000192 RID: 402
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u202D\u206E\u206F\u206B\u206B\u202C\u206A\u200C\u202E\u206C\u200E\u206D\u202E\u206A\u206E\u206B\u206F\u200C\u202A\u202D\u202B\u206E\u202A\u200C\u202C\u200D\u202D\u202A\u200C\u206B\u206E\u206F\u202E\u206C\u202A\u200B\u200E\u202D\u206A\u202E;
	}
}

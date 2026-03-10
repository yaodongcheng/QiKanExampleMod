using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence
{
	// Token: 0x020000BD RID: 189
	public class OwnershipChange
	{
		// Token: 0x17000173 RID: 371
		// (get) Token: 0x06000665 RID: 1637 RVA: 0x0008274C File Offset: 0x0008094C
		// (set) Token: 0x06000666 RID: 1638 RVA: 0x00082760 File Offset: 0x00080960
		[JsonProperty("FromKingdomId")]
		public string FromKingdomId { get; set; }

		// Token: 0x17000174 RID: 372
		// (get) Token: 0x06000667 RID: 1639 RVA: 0x00082774 File Offset: 0x00080974
		// (set) Token: 0x06000668 RID: 1640 RVA: 0x00082788 File Offset: 0x00080988
		[JsonProperty("FromKingdomName")]
		public string FromKingdomName { get; set; }

		// Token: 0x17000175 RID: 373
		// (get) Token: 0x06000669 RID: 1641 RVA: 0x0008279C File Offset: 0x0008099C
		// (set) Token: 0x0600066A RID: 1642 RVA: 0x000827B0 File Offset: 0x000809B0
		[JsonProperty("ToKingdomId")]
		public string ToKingdomId { get; set; }

		// Token: 0x17000176 RID: 374
		// (get) Token: 0x0600066B RID: 1643 RVA: 0x000827C4 File Offset: 0x000809C4
		// (set) Token: 0x0600066C RID: 1644 RVA: 0x000827D8 File Offset: 0x000809D8
		[JsonProperty("ToKingdomName")]
		public string ToKingdomName { get; set; }

		// Token: 0x17000177 RID: 375
		// (get) Token: 0x0600066D RID: 1645 RVA: 0x000827EC File Offset: 0x000809EC
		// (set) Token: 0x0600066E RID: 1646 RVA: 0x00082800 File Offset: 0x00080A00
		[JsonProperty("ChangeDate")]
		public CampaignTime ChangeDate { get; set; }

		// Token: 0x17000178 RID: 376
		// (get) Token: 0x0600066F RID: 1647 RVA: 0x00082814 File Offset: 0x00080A14
		// (set) Token: 0x06000670 RID: 1648 RVA: 0x00082828 File Offset: 0x00080A28
		[JsonProperty("ChangeReason")]
		public string ChangeReason { get; set; }

		// Token: 0x0400041C RID: 1052
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u200D\u202B\u200B\u202A\u202B\u206C\u206B\u200E\u202B\u202C\u206E\u202E\u202D\u200E\u202A\u202B\u206B\u202A\u206B\u206E\u200D\u206A\u200E\u200E\u206A\u202E\u200C\u206C\u206A\u206D\u202E\u206D\u202B\u200D\u206C\u202D\u206E\u202B\u202A\u202E;

		// Token: 0x0400041D RID: 1053
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u206D\u202C\u200D\u206B\u200C\u202C\u202E\u202B\u200C\u202A\u200C\u200E\u202B\u202D\u206C\u206C\u206E\u206E\u202E\u202A\u202D\u202E\u200B\u206C\u202B\u200B\u200D\u202C\u202A\u206F\u206E\u200F\u202A\u202C\u206C\u206A\u206B\u206E\u206E\u202E;

		// Token: 0x0400041E RID: 1054
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u202E\u206E\u202B\u202C\u206D\u202E\u206D\u206E\u202A\u200E\u200F\u206F\u200F\u200B\u200D\u200E\u202B\u206F\u202C\u206A\u202C\u206F\u206E\u200C\u206A\u206B\u202E\u202B\u202C\u200F\u206C\u202D\u206A\u206F\u206B\u206D\u206D\u206C\u206C\u202E;

		// Token: 0x0400041F RID: 1055
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u206F\u206D\u202A\u206C\u200E\u202C\u206A\u202A\u200E\u206D\u200D\u200F\u206C\u200B\u202C\u202E\u200E\u206D\u202D\u200D\u206A\u206C\u206C\u202C\u200C\u206C\u200E\u202D\u200B\u202B\u200E\u206B\u202D\u202D\u202B\u202A\u200C\u202B\u206C\u202E;

		// Token: 0x04000420 RID: 1056
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u202D\u206D\u200D\u200F\u202E\u206A\u206D\u200E\u206F\u200E\u206A\u202D\u206B\u202E\u200D\u202A\u206F\u206D\u206C\u206A\u206C\u200D\u200E\u206F\u206B\u206E\u206C\u202D\u206D\u200E\u206E\u200E\u206F\u206E\u206A\u202E\u206B\u200D\u206D\u206B\u202E;

		// Token: 0x04000421 RID: 1057
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u202B\u200B\u206B\u200B\u200B\u202C\u206B\u202A\u206E\u200E\u200E\u202D\u200B\u206B\u206E\u202C\u202C\u206A\u206C\u206A\u202E\u200B\u200F\u206E\u200C\u206F\u202C\u206B\u200C\u206A\u206A\u206F\u202E\u206F\u202E\u200C\u200C\u200F\u202A\u202E;
	}
}

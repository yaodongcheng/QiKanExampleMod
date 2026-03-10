using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x0200005D RID: 93
	public class KingdomLeadershipHistory
	{
		// Token: 0x17000024 RID: 36
		// (get) Token: 0x06000219 RID: 537 RVA: 0x0005029C File Offset: 0x0004E49C
		// (set) Token: 0x0600021A RID: 538 RVA: 0x000502B0 File Offset: 0x0004E4B0
		[JsonProperty("KingdomId")]
		public string KingdomId { get; set; }

		// Token: 0x17000025 RID: 37
		// (get) Token: 0x0600021B RID: 539 RVA: 0x000502C4 File Offset: 0x0004E4C4
		// (set) Token: 0x0600021C RID: 540 RVA: 0x000502D8 File Offset: 0x0004E4D8
		[JsonProperty("KingdomName")]
		public string KingdomName { get; set; }

		// Token: 0x17000026 RID: 38
		// (get) Token: 0x0600021D RID: 541 RVA: 0x000502EC File Offset: 0x0004E4EC
		// (set) Token: 0x0600021E RID: 542 RVA: 0x00050300 File Offset: 0x0004E500
		[JsonProperty("CurrentLeaderId")]
		public string CurrentLeaderId { get; set; }

		// Token: 0x17000027 RID: 39
		// (get) Token: 0x0600021F RID: 543 RVA: 0x00050314 File Offset: 0x0004E514
		// (set) Token: 0x06000220 RID: 544 RVA: 0x00050328 File Offset: 0x0004E528
		[JsonProperty("CurrentLeaderName")]
		public string CurrentLeaderName { get; set; }

		// Token: 0x17000028 RID: 40
		// (get) Token: 0x06000221 RID: 545 RVA: 0x0005033C File Offset: 0x0004E53C
		// (set) Token: 0x06000222 RID: 546 RVA: 0x00050350 File Offset: 0x0004E550
		[JsonProperty("CurrentLeaderClanId")]
		public string CurrentLeaderClanId { get; set; }

		// Token: 0x17000029 RID: 41
		// (get) Token: 0x06000223 RID: 547 RVA: 0x00050364 File Offset: 0x0004E564
		// (set) Token: 0x06000224 RID: 548 RVA: 0x00050378 File Offset: 0x0004E578
		[JsonProperty("CurrentLeaderClanName")]
		public string CurrentLeaderClanName { get; set; }

		// Token: 0x1700002A RID: 42
		// (get) Token: 0x06000225 RID: 549 RVA: 0x0005038C File Offset: 0x0004E58C
		// (set) Token: 0x06000226 RID: 550 RVA: 0x000503A0 File Offset: 0x0004E5A0
		[JsonProperty("LeadershipChanges")]
		public List<LeadershipChange> LeadershipChanges { get; set; } = new List<LeadershipChange>();

		// Token: 0x04000180 RID: 384
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u202D\u200E\u202C\u206E\u200C\u202C\u202E\u206C\u206B\u200C\u200F\u202B\u202B\u202D\u202E\u200C\u206D\u200C\u206F\u202B\u202B\u202B\u200C\u206C\u206D\u200C\u202B\u200D\u206B\u200F\u200E\u206C\u206D\u206B\u200C\u200F\u200E\u202E\u206C\u202E;

		// Token: 0x04000181 RID: 385
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u206C\u202A\u202D\u200F\u206F\u200B\u202B\u202B\u202E\u206F\u200C\u206E\u202D\u202A\u206D\u202E\u206D\u200E\u202C\u202E\u206D\u202C\u206B\u206A\u202D\u206E\u206D\u206B\u206E\u206C\u202E\u202C\u200F\u200E\u206D\u200E\u206A\u202A\u206F\u202E;

		// Token: 0x04000182 RID: 386
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u206F\u200B\u202E\u206B\u200C\u200C\u206A\u202C\u206E\u206A\u200C\u200F\u202A\u202E\u202D\u206B\u202B\u202B\u206B\u206D\u200C\u206C\u202E\u200E\u200F\u200F\u206C\u202B\u200B\u200F\u206D\u206C\u202A\u200B\u200D\u206A\u202E\u202B\u206A\u202E;

		// Token: 0x04000183 RID: 387
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200F\u206C\u206B\u200E\u200B\u202C\u206A\u200C\u206C\u202A\u206D\u202B\u202B\u200F\u206E\u206E\u202B\u200E\u206E\u202C\u206C\u202E\u200F\u206F\u206A\u200C\u202A\u206D\u202E\u202A\u200E\u202D\u206D\u200E\u206C\u202B\u200C\u206B\u202E\u206E\u202E;

		// Token: 0x04000184 RID: 388
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u202E\u202C\u202D\u200D\u202D\u200E\u206A\u206B\u202D\u206A\u200E\u206F\u200C\u206E\u206C\u202D\u206A\u202E\u206A\u202C\u200E\u202A\u206C\u202C\u206A\u200F\u202C\u202C\u200C\u206E\u202E\u200F\u202C\u206A\u206D\u200B\u200C\u206F\u206D\u202E;

		// Token: 0x04000185 RID: 389
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u200F\u206B\u202A\u206D\u206D\u206B\u206C\u206D\u206F\u202B\u202E\u200D\u202E\u202C\u206F\u206D\u206F\u206A\u206C\u202A\u200C\u202A\u200C\u206E\u202A\u200E\u200F\u206B\u202C\u202D\u206E\u200D\u200C\u206B\u206E\u206B\u206E\u202D\u200E\u202E;

		// Token: 0x04000186 RID: 390
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<LeadershipChange> \u200C\u202D\u206E\u200B\u202B\u202C\u206A\u202D\u200F\u200E\u202A\u202B\u206F\u200D\u200E\u206E\u206F\u202B\u202B\u200F\u206C\u202C\u202E\u206E\u200F\u202C\u202B\u200B\u202E\u200F\u200D\u200B\u202C\u200B\u202C\u206C\u206A\u200F\u200D\u202E;
	}
}

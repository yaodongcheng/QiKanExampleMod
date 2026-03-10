using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIInfluence
{
	// Token: 0x0200006F RID: 111
	[JsonSerializable]
	public class PlayerInfo
	{
		// Token: 0x17000102 RID: 258
		// (get) Token: 0x0600041B RID: 1051 RVA: 0x0005BB38 File Offset: 0x00059D38
		// (set) Token: 0x0600041C RID: 1052 RVA: 0x0005BB4C File Offset: 0x00059D4C
		public string ClaimedName { get; set; }

		// Token: 0x17000103 RID: 259
		// (get) Token: 0x0600041D RID: 1053 RVA: 0x0005BB60 File Offset: 0x00059D60
		// (set) Token: 0x0600041E RID: 1054 RVA: 0x0005BB74 File Offset: 0x00059D74
		public string ClaimedClan { get; set; }

		// Token: 0x17000104 RID: 260
		// (get) Token: 0x0600041F RID: 1055 RVA: 0x0005BB88 File Offset: 0x00059D88
		// (set) Token: 0x06000420 RID: 1056 RVA: 0x0005BB9C File Offset: 0x00059D9C
		public int ClaimedAge { get; set; }

		// Token: 0x17000105 RID: 261
		// (get) Token: 0x06000421 RID: 1057 RVA: 0x0005BBB0 File Offset: 0x00059DB0
		// (set) Token: 0x06000422 RID: 1058 RVA: 0x0005BBC4 File Offset: 0x00059DC4
		public int ClaimedGold { get; set; }

		// Token: 0x17000106 RID: 262
		// (get) Token: 0x06000423 RID: 1059 RVA: 0x0005BBD8 File Offset: 0x00059DD8
		// (set) Token: 0x06000424 RID: 1060 RVA: 0x0005BBEC File Offset: 0x00059DEC
		public string RealName { get; set; }

		// Token: 0x17000107 RID: 263
		// (get) Token: 0x06000425 RID: 1061 RVA: 0x0005BC00 File Offset: 0x00059E00
		// (set) Token: 0x06000426 RID: 1062 RVA: 0x0005BC14 File Offset: 0x00059E14
		public string RealClan { get; set; }

		// Token: 0x17000108 RID: 264
		// (get) Token: 0x06000427 RID: 1063 RVA: 0x0005BC28 File Offset: 0x00059E28
		// (set) Token: 0x06000428 RID: 1064 RVA: 0x0005BC3C File Offset: 0x00059E3C
		public int RealAge { get; set; }

		// Token: 0x17000109 RID: 265
		// (get) Token: 0x06000429 RID: 1065 RVA: 0x0005BC50 File Offset: 0x00059E50
		// (set) Token: 0x0600042A RID: 1066 RVA: 0x0005BC64 File Offset: 0x00059E64
		public string RealCulture { get; set; }

		// Token: 0x1700010A RID: 266
		// (get) Token: 0x0600042B RID: 1067 RVA: 0x0005BC78 File Offset: 0x00059E78
		// (set) Token: 0x0600042C RID: 1068 RVA: 0x0005BC8C File Offset: 0x00059E8C
		public string RealGender { get; set; }

		// Token: 0x1700010B RID: 267
		// (get) Token: 0x0600042D RID: 1069 RVA: 0x0005BCA0 File Offset: 0x00059EA0
		// (set) Token: 0x0600042E RID: 1070 RVA: 0x0005BCB4 File Offset: 0x00059EB4
		public bool SuspectedLie { get; set; }

		// Token: 0x1700010C RID: 268
		// (get) Token: 0x0600042F RID: 1071 RVA: 0x0005BCC8 File Offset: 0x00059EC8
		// (set) Token: 0x06000430 RID: 1072 RVA: 0x0005BCDC File Offset: 0x00059EDC
		public string RealKingdom { get; set; }

		// Token: 0x1700010D RID: 269
		// (get) Token: 0x06000431 RID: 1073 RVA: 0x0005BCF0 File Offset: 0x00059EF0
		// (set) Token: 0x06000432 RID: 1074 RVA: 0x0005BD04 File Offset: 0x00059F04
		public string RealKingdomId { get; set; }

		// Token: 0x1700010E RID: 270
		// (get) Token: 0x06000433 RID: 1075 RVA: 0x0005BD18 File Offset: 0x00059F18
		// (set) Token: 0x06000434 RID: 1076 RVA: 0x0005BD2C File Offset: 0x00059F2C
		public bool IsMercenary { get; set; }

		// Token: 0x1700010F RID: 271
		// (get) Token: 0x06000435 RID: 1077 RVA: 0x0005BD40 File Offset: 0x00059F40
		// (set) Token: 0x06000436 RID: 1078 RVA: 0x0005BD54 File Offset: 0x00059F54
		public string PlayerStringId { get; set; }

		// Token: 0x0400027C RID: 636
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u206B\u206B\u206D\u206E\u200E\u206A\u200B\u206B\u200F\u200B\u206D\u206B\u206C\u202E\u200C\u200D\u200E\u206A\u206C\u206B\u206F\u200C\u206E\u206F\u206B\u206B\u206C\u206B\u206A\u206C\u200F\u206A\u200C\u206B\u200E\u200E\u202E\u202E\u202E\u202E;

		// Token: 0x0400027D RID: 637
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u200B\u202C\u202B\u200B\u202B\u202D\u200F\u200D\u206C\u206B\u202B\u206B\u206B\u202A\u200F\u202C\u206E\u206E\u200B\u200D\u200E\u206A\u202A\u206E\u206A\u202A\u200C\u200E\u202A\u202E\u206D\u200F\u202D\u202A\u206C\u200D\u206A\u202A\u202D\u202E;

		// Token: 0x0400027E RID: 638
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202B\u202E\u202D\u206F\u202E\u206F\u206F\u202C\u202C\u206D\u200C\u200B\u200F\u200C\u200C\u206F\u200F\u206D\u206A\u200E\u200D\u202E\u200F\u202C\u200E\u206D\u206F\u200D\u200B\u200D\u200D\u200E\u206B\u206B\u206B\u206C\u202D\u206C\u200D\u200D\u202E;

		// Token: 0x0400027F RID: 639
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202A\u206D\u206B\u206D\u202A\u206A\u206F\u206C\u200F\u206B\u200B\u206F\u200C\u206C\u202C\u206B\u206D\u200B\u202C\u206D\u200E\u202D\u202D\u202B\u202D\u206A\u200B\u206C\u200B\u206C\u202E\u206C\u206D\u206A\u206C\u206D\u202E\u200E\u200B\u202A\u202E;

		// Token: 0x04000280 RID: 640
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u202D\u206D\u206F\u202A\u206C\u206A\u200B\u202D\u206B\u206F\u200C\u206F\u202D\u202D\u206A\u200F\u206E\u202A\u200E\u206D\u206C\u202E\u200F\u202D\u200D\u206A\u200D\u202D\u206B\u206B\u206A\u202A\u206D\u206A\u206F\u202B\u200E\u202C\u200C\u202E;

		// Token: 0x04000281 RID: 641
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u200E\u202E\u200F\u202E\u202A\u206B\u200E\u202C\u202C\u200E\u206C\u206C\u206B\u202A\u206B\u206A\u202C\u206C\u206D\u202D\u202A\u206D\u200F\u200F\u200C\u202A\u206B\u202C\u206C\u202A\u206F\u202A\u200B\u206D\u206C\u206A\u202D\u206D\u202E\u202E;

		// Token: 0x04000282 RID: 642
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200D\u206A\u202E\u202D\u206F\u206B\u202E\u200C\u202D\u202B\u200C\u206D\u200F\u206A\u200D\u200F\u206A\u206A\u206F\u202C\u206C\u200F\u202C\u206B\u202A\u206A\u200B\u202C\u200D\u200F\u206E\u202D\u206C\u206E\u202D\u206E\u206A\u206A\u206C\u202A\u202E;

		// Token: 0x04000283 RID: 643
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u206C\u200D\u200B\u202B\u206E\u200F\u206E\u206A\u206A\u200F\u206E\u200F\u202C\u202D\u202A\u206B\u206A\u200B\u200B\u202A\u206A\u200B\u200B\u202A\u202C\u206C\u200E\u200F\u200E\u200C\u200E\u202B\u200B\u206D\u206C\u202C\u206D\u206B\u206C\u202E;

		// Token: 0x04000284 RID: 644
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u200C\u200D\u200F\u206B\u200C\u206E\u200F\u206D\u202B\u206A\u206E\u200D\u206B\u202B\u200E\u206E\u202C\u200D\u206C\u200E\u202D\u206E\u206E\u200C\u202B\u200B\u200C\u200B\u206D\u202A\u200C\u200C\u202C\u200E\u202D\u200B\u200B\u202A\u200E\u202E;

		// Token: 0x04000285 RID: 645
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206F\u200F\u206D\u200B\u200E\u206B\u200E\u206E\u206E\u206F\u200F\u202A\u206E\u200E\u206E\u202E\u206B\u200E\u206F\u206B\u200C\u206B\u202B\u206D\u206D\u202E\u202A\u200F\u202E\u206E\u202B\u200E\u200F\u200E\u200E\u202D\u206A\u206D\u206D\u206C\u202E;

		// Token: 0x04000286 RID: 646
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u206C\u206B\u202B\u206A\u202C\u202D\u206D\u206A\u202A\u206C\u206F\u200B\u200B\u202C\u206E\u200B\u202E\u206B\u202D\u202A\u206A\u202D\u202D\u202A\u200E\u200C\u200F\u206F\u206E\u206F\u200B\u200E\u200C\u206F\u200B\u200B\u202C\u206B\u200C\u202E;

		// Token: 0x04000287 RID: 647
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u202E\u206F\u200D\u206E\u206A\u200E\u206B\u206F\u202B\u206A\u202D\u202E\u202B\u206B\u202C\u202D\u206D\u206D\u206A\u206A\u202A\u200D\u206F\u202B\u206F\u202D\u206F\u202B\u206C\u206F\u206B\u206D\u206A\u206D\u202E\u202B\u206C\u200D\u202D\u202E;

		// Token: 0x04000288 RID: 648
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202C\u200F\u202D\u206D\u206F\u200B\u202E\u206D\u206A\u206E\u206F\u206C\u202C\u206E\u202C\u206B\u202C\u202A\u206D\u206E\u200E\u206E\u200F\u206F\u200D\u206E\u206A\u202B\u200D\u206F\u200C\u202B\u206C\u200B\u206F\u200F\u200B\u200B\u200F\u202C\u202E;

		// Token: 0x04000289 RID: 649
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u206C\u202E\u202E\u206D\u202B\u206C\u200C\u206C\u200E\u206C\u202B\u200E\u202A\u200D\u202D\u206B\u206B\u200F\u206C\u202C\u200F\u200C\u206B\u200D\u200E\u206D\u200C\u206D\u202A\u202C\u202D\u200E\u200E\u202E\u206D\u202D\u206B\u206D\u202E;
	}
}

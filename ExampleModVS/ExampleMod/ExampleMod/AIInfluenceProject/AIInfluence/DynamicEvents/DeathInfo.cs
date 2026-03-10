using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x020001AA RID: 426
	[JsonSerializable]
	public class DeathInfo
	{
		// Token: 0x170002AB RID: 683
		// (get) Token: 0x06000D8F RID: 3471 RVA: 0x000EAEF4 File Offset: 0x000E90F4
		// (set) Token: 0x06000D90 RID: 3472 RVA: 0x000EAF08 File Offset: 0x000E9108
		public string HeroName { get; set; }

		// Token: 0x170002AC RID: 684
		// (get) Token: 0x06000D91 RID: 3473 RVA: 0x000EAF1C File Offset: 0x000E911C
		// (set) Token: 0x06000D92 RID: 3474 RVA: 0x000EAF30 File Offset: 0x000E9130
		public string HeroStringId { get; set; }

		// Token: 0x170002AD RID: 685
		// (get) Token: 0x06000D93 RID: 3475 RVA: 0x000EAF44 File Offset: 0x000E9144
		// (set) Token: 0x06000D94 RID: 3476 RVA: 0x000EAF58 File Offset: 0x000E9158
		public string Title { get; set; }

		// Token: 0x170002AE RID: 686
		// (get) Token: 0x06000D95 RID: 3477 RVA: 0x000EAF6C File Offset: 0x000E916C
		// (set) Token: 0x06000D96 RID: 3478 RVA: 0x000EAF80 File Offset: 0x000E9180
		public string DeathCause { get; set; }

		// Token: 0x170002AF RID: 687
		// (get) Token: 0x06000D97 RID: 3479 RVA: 0x000EAF94 File Offset: 0x000E9194
		// (set) Token: 0x06000D98 RID: 3480 RVA: 0x000EAFA8 File Offset: 0x000E91A8
		public string KillerName { get; set; }

		// Token: 0x170002B0 RID: 688
		// (get) Token: 0x06000D99 RID: 3481 RVA: 0x000EAFBC File Offset: 0x000E91BC
		// (set) Token: 0x06000D9A RID: 3482 RVA: 0x000EAFD0 File Offset: 0x000E91D0
		public string KillerStringId { get; set; }

		// Token: 0x170002B1 RID: 689
		// (get) Token: 0x06000D9B RID: 3483 RVA: 0x000EAFE4 File Offset: 0x000E91E4
		// (set) Token: 0x06000D9C RID: 3484 RVA: 0x000EAFF8 File Offset: 0x000E91F8
		public string KingdomName { get; set; }

		// Token: 0x170002B2 RID: 690
		// (get) Token: 0x06000D9D RID: 3485 RVA: 0x000EB00C File Offset: 0x000E920C
		// (set) Token: 0x06000D9E RID: 3486 RVA: 0x000EB020 File Offset: 0x000E9220
		public string KingdomStringId { get; set; }

		// Token: 0x170002B3 RID: 691
		// (get) Token: 0x06000D9F RID: 3487 RVA: 0x000EB034 File Offset: 0x000E9234
		// (set) Token: 0x06000DA0 RID: 3488 RVA: 0x000EB048 File Offset: 0x000E9248
		public int DaysAgo { get; set; }

		// Token: 0x040008B3 RID: 2227
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u206E\u206C\u202A\u200F\u200E\u202A\u206F\u200E\u200B\u202A\u200E\u206A\u200F\u202A\u202D\u200B\u202E\u200D\u206B\u206B\u200C\u202E\u206E\u200E\u200B\u202A\u200E\u206D\u202A\u206C\u202B\u202A\u200E\u202A\u200B\u206E\u200F\u200D\u202E;

		// Token: 0x040008B4 RID: 2228
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u206D\u206A\u202E\u200F\u206D\u206B\u202D\u206F\u200F\u206B\u202B\u202E\u206B\u202D\u206F\u200D\u202B\u206C\u206C\u202E\u200D\u202E\u202D\u202A\u202D\u202B\u200D\u202E\u202B\u206B\u206D\u202E\u202E\u202E\u202B\u202B\u202B\u206D\u200D\u202E;

		// Token: 0x040008B5 RID: 2229
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u202D\u206A\u200D\u202A\u200C\u202B\u206F\u200D\u206D\u206C\u206F\u206C\u202C\u202B\u206E\u200E\u202A\u202C\u206A\u202E\u206F\u202E\u206E\u200B\u206E\u202A\u206C\u202D\u206D\u200E\u200F\u200D\u202C\u206A\u200E\u200E\u202C\u200C\u200F\u202E;

		// Token: 0x040008B6 RID: 2230
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u206F\u206F\u202D\u202A\u202B\u202C\u200D\u200F\u206F\u200F\u200F\u206B\u200F\u202B\u200B\u200B\u200D\u202D\u202A\u200E\u202C\u206B\u200B\u200B\u202E\u206C\u206E\u202D\u206D\u206C\u202A\u202A\u202C\u206D\u206F\u200E\u202D\u200D\u200E\u202E;

		// Token: 0x040008B7 RID: 2231
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u202A\u200C\u200D\u206A\u206F\u206A\u200F\u202B\u202D\u202B\u206A\u202A\u202A\u206D\u200B\u202C\u202C\u206D\u200D\u206A\u202B\u200E\u202C\u206E\u206C\u202E\u200D\u200D\u202E\u200B\u206F\u200E\u202E\u202C\u206C\u206B\u200B\u202E\u202A\u202E;

		// Token: 0x040008B8 RID: 2232
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202E\u202E\u200E\u206A\u206B\u200D\u200B\u206A\u206F\u200E\u206A\u206F\u206D\u200F\u206D\u200C\u206A\u202A\u206D\u200E\u206C\u206B\u206D\u200D\u202A\u206B\u200E\u200B\u202C\u200E\u200F\u206A\u202A\u200B\u202D\u200C\u206B\u202C\u202C\u200E\u202E;

		// Token: 0x040008B9 RID: 2233
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u202C\u200B\u202C\u206F\u200B\u202D\u206A\u202B\u206D\u200F\u200E\u200B\u206B\u200E\u206D\u202D\u202D\u206C\u206F\u200F\u206D\u200F\u206C\u200D\u206C\u200D\u206D\u202D\u206F\u200C\u202C\u202C\u206D\u202D\u202A\u202E\u206D\u206D\u202D\u202E;

		// Token: 0x040008BA RID: 2234
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u202E\u200F\u206F\u206F\u206C\u202D\u202D\u200D\u200D\u206A\u200F\u200D\u206D\u202B\u202B\u202A\u202D\u206A\u202A\u206D\u206B\u206C\u202A\u200D\u206D\u202A\u206A\u200D\u202E\u206D\u206B\u202E\u200C\u200B\u206C\u202C\u200C\u206E\u206D\u202E;

		// Token: 0x040008BB RID: 2235
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206D\u200C\u200D\u200D\u202B\u202A\u206A\u202B\u206C\u202E\u202B\u200C\u202C\u202D\u206E\u200E\u206C\u202E\u202A\u206C\u202B\u206D\u202A\u200D\u200B\u206D\u206F\u206E\u206B\u202B\u202E\u206D\u206C\u206E\u200B\u202A\u206C\u200E\u206A\u206E\u202E;
	}
}

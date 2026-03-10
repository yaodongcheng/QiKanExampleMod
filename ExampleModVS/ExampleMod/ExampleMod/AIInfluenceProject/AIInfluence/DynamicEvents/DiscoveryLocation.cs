using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x020001AB RID: 427
	[JsonSerializable]
	public class DiscoveryLocation
	{
		// Token: 0x170002B4 RID: 692
		// (get) Token: 0x06000DA2 RID: 3490 RVA: 0x000EB05C File Offset: 0x000E925C
		// (set) Token: 0x06000DA3 RID: 3491 RVA: 0x000EB070 File Offset: 0x000E9270
		public string SettlementName { get; set; }

		// Token: 0x170002B5 RID: 693
		// (get) Token: 0x06000DA4 RID: 3492 RVA: 0x000EB084 File Offset: 0x000E9284
		// (set) Token: 0x06000DA5 RID: 3493 RVA: 0x000EB098 File Offset: 0x000E9298
		public string SettlementStringId { get; set; }

		// Token: 0x170002B6 RID: 694
		// (get) Token: 0x06000DA6 RID: 3494 RVA: 0x000EB0AC File Offset: 0x000E92AC
		// (set) Token: 0x06000DA7 RID: 3495 RVA: 0x000EB0C0 File Offset: 0x000E92C0
		public string SettlementType { get; set; }

		// Token: 0x170002B7 RID: 695
		// (get) Token: 0x06000DA8 RID: 3496 RVA: 0x000EB0D4 File Offset: 0x000E92D4
		// (set) Token: 0x06000DA9 RID: 3497 RVA: 0x000EB0E8 File Offset: 0x000E92E8
		public string KingdomName { get; set; }

		// Token: 0x170002B8 RID: 696
		// (get) Token: 0x06000DAA RID: 3498 RVA: 0x000EB0FC File Offset: 0x000E92FC
		// (set) Token: 0x06000DAB RID: 3499 RVA: 0x000EB110 File Offset: 0x000E9310
		public string KingdomStringId { get; set; }

		// Token: 0x170002B9 RID: 697
		// (get) Token: 0x06000DAC RID: 3500 RVA: 0x000EB124 File Offset: 0x000E9324
		// (set) Token: 0x06000DAD RID: 3501 RVA: 0x000EB138 File Offset: 0x000E9338
		public string Culture { get; set; }

		// Token: 0x170002BA RID: 698
		// (get) Token: 0x06000DAE RID: 3502 RVA: 0x000EB14C File Offset: 0x000E934C
		// (set) Token: 0x06000DAF RID: 3503 RVA: 0x000EB160 File Offset: 0x000E9360
		public string DiscoveryPotential { get; set; }

		// Token: 0x170002BB RID: 699
		// (get) Token: 0x06000DB0 RID: 3504 RVA: 0x000EB174 File Offset: 0x000E9374
		// (set) Token: 0x06000DB1 RID: 3505 RVA: 0x000EB188 File Offset: 0x000E9388
		public string RulerName { get; set; }

		// Token: 0x170002BC RID: 700
		// (get) Token: 0x06000DB2 RID: 3506 RVA: 0x000EB19C File Offset: 0x000E939C
		// (set) Token: 0x06000DB3 RID: 3507 RVA: 0x000EB1B0 File Offset: 0x000E93B0
		public string RulerStringId { get; set; }

		// Token: 0x040008BC RID: 2236
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u200B\u206C\u200C\u202D\u200D\u200B\u200D\u206A\u206A\u200C\u200F\u202E\u200F\u200C\u200D\u206A\u200C\u202E\u200E\u200B\u202A\u200D\u200E\u200E\u202D\u206F\u206B\u202D\u200E\u200B\u200F\u200D\u200B\u206C\u200E\u200B\u202E\u200B\u206B\u202E;

		// Token: 0x040008BD RID: 2237
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u206C\u206A\u200F\u206C\u206D\u202E\u200D\u206A\u200F\u202B\u206D\u206E\u202B\u206D\u202D\u200B\u200F\u200B\u200D\u202D\u200E\u206A\u206B\u206A\u206C\u202A\u206F\u202A\u202C\u200C\u206D\u202B\u206C\u200F\u202A\u200B\u202C\u202D\u200E\u202E;

		// Token: 0x040008BE RID: 2238
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u202B\u206C\u206C\u202D\u202A\u206B\u202B\u206A\u206E\u206A\u206D\u202B\u202A\u206B\u200B\u206A\u202A\u200C\u202D\u202D\u200F\u202D\u200D\u206D\u206B\u206A\u206C\u206E\u200F\u206F\u206D\u206C\u202C\u202E\u202B\u202E\u200D\u202E\u206B\u202E;

		// Token: 0x040008BF RID: 2239
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202D\u200E\u200F\u202A\u202E\u200B\u202A\u200C\u202D\u206A\u200E\u202B\u206C\u202B\u200B\u202D\u206C\u202A\u202A\u202C\u202C\u202B\u202A\u202E\u202C\u206E\u200F\u206E\u206F\u202E\u202A\u202B\u206F\u200E\u206E\u200B\u206F\u206A\u202C\u202E;

		// Token: 0x040008C0 RID: 2240
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202C\u206D\u200E\u202B\u200B\u202E\u206F\u202D\u200B\u202C\u200C\u200F\u202D\u206D\u206B\u206C\u202C\u206C\u206A\u202B\u202E\u200E\u200F\u202E\u202A\u206C\u202C\u202E\u202C\u206B\u200E\u202C\u200B\u206E\u202E\u200C\u202A\u202A\u202E\u202E;

		// Token: 0x040008C1 RID: 2241
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u206A\u202B\u200F\u200E\u200E\u202C\u206D\u202E\u200E\u206E\u206B\u202E\u200F\u202B\u200F\u206F\u200F\u202E\u200E\u206E\u202C\u200F\u200C\u202A\u206C\u202A\u206A\u200D\u202D\u200E\u200D\u202A\u202B\u202B\u200D\u200B\u206B\u202D\u200E\u202E;

		// Token: 0x040008C2 RID: 2242
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u206D\u202A\u200D\u202D\u202B\u206E\u206E\u202D\u200C\u202D\u200F\u206D\u200D\u200F\u202D\u202D\u202C\u200E\u200D\u206B\u200E\u200C\u202E\u202B\u202C\u200D\u200E\u206D\u202E\u200C\u206A\u200E\u202B\u206F\u202B\u202C\u206F\u202B\u206A\u202E;

		// Token: 0x040008C3 RID: 2243
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u206A\u206E\u200F\u202D\u200E\u202B\u200E\u200F\u206F\u202C\u206C\u206E\u206F\u206F\u206A\u206D\u202A\u202C\u206B\u202B\u206B\u200F\u206D\u202A\u206F\u206A\u202A\u206B\u202D\u206B\u206D\u200F\u206A\u206D\u202A\u202B\u202D\u200F\u200E\u202E;

		// Token: 0x040008C4 RID: 2244
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u202E\u206C\u202C\u206A\u202A\u202E\u202E\u200D\u200F\u200B\u206B\u202B\u206B\u200B\u200B\u202C\u200C\u206D\u202D\u206D\u206A\u202E\u202A\u202C\u200E\u200C\u206A\u206D\u206D\u206B\u200F\u206E\u202A\u202D\u202C\u200B\u200D\u202A\u206F\u202E;
	}
}

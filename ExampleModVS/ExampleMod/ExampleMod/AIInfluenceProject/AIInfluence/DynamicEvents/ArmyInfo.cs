using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x020001A5 RID: 421
	[JsonSerializable]
	public class ArmyInfo
	{
		// Token: 0x17000295 RID: 661
		// (get) Token: 0x06000D4B RID: 3403 RVA: 0x000E7BF8 File Offset: 0x000E5DF8
		// (set) Token: 0x06000D4C RID: 3404 RVA: 0x000E7C0C File Offset: 0x000E5E0C
		public string ArmyName { get; set; }

		// Token: 0x17000296 RID: 662
		// (get) Token: 0x06000D4D RID: 3405 RVA: 0x000E7C20 File Offset: 0x000E5E20
		// (set) Token: 0x06000D4E RID: 3406 RVA: 0x000E7C34 File Offset: 0x000E5E34
		public int TotalTroops { get; set; }

		// Token: 0x17000297 RID: 663
		// (get) Token: 0x06000D4F RID: 3407 RVA: 0x000E7C48 File Offset: 0x000E5E48
		// (set) Token: 0x06000D50 RID: 3408 RVA: 0x000E7C5C File Offset: 0x000E5E5C
		public string LeaderName { get; set; }

		// Token: 0x17000298 RID: 664
		// (get) Token: 0x06000D51 RID: 3409 RVA: 0x000E7C70 File Offset: 0x000E5E70
		// (set) Token: 0x06000D52 RID: 3410 RVA: 0x000E7C84 File Offset: 0x000E5E84
		public string LeaderStringId { get; set; }

		// Token: 0x17000299 RID: 665
		// (get) Token: 0x06000D53 RID: 3411 RVA: 0x000E7C98 File Offset: 0x000E5E98
		// (set) Token: 0x06000D54 RID: 3412 RVA: 0x000E7CAC File Offset: 0x000E5EAC
		public string KingdomName { get; set; }

		// Token: 0x1700029A RID: 666
		// (get) Token: 0x06000D55 RID: 3413 RVA: 0x000E7CC0 File Offset: 0x000E5EC0
		// (set) Token: 0x06000D56 RID: 3414 RVA: 0x000E7CD4 File Offset: 0x000E5ED4
		public string KingdomStringId { get; set; }

		// Token: 0x1700029B RID: 667
		// (get) Token: 0x06000D57 RID: 3415 RVA: 0x000E7CE8 File Offset: 0x000E5EE8
		// (set) Token: 0x06000D58 RID: 3416 RVA: 0x000E7CFC File Offset: 0x000E5EFC
		public string Location { get; set; }

		// Token: 0x1700029C RID: 668
		// (get) Token: 0x06000D59 RID: 3417 RVA: 0x000E7D10 File Offset: 0x000E5F10
		// (set) Token: 0x06000D5A RID: 3418 RVA: 0x000E7D24 File Offset: 0x000E5F24
		public string Target { get; set; }

		// Token: 0x1700029D RID: 669
		// (get) Token: 0x06000D5B RID: 3419 RVA: 0x000E7D38 File Offset: 0x000E5F38
		// (set) Token: 0x06000D5C RID: 3420 RVA: 0x000E7D4C File Offset: 0x000E5F4C
		public int PartyCount { get; set; }

		// Token: 0x1700029E RID: 670
		// (get) Token: 0x06000D5D RID: 3421 RVA: 0x000E7D60 File Offset: 0x000E5F60
		// (set) Token: 0x06000D5E RID: 3422 RVA: 0x000E7D74 File Offset: 0x000E5F74
		public bool IsDisorganized { get; set; }

		// Token: 0x1700029F RID: 671
		// (get) Token: 0x06000D5F RID: 3423 RVA: 0x000E7D88 File Offset: 0x000E5F88
		// (set) Token: 0x06000D60 RID: 3424 RVA: 0x000E7D9C File Offset: 0x000E5F9C
		public float Morale { get; set; }

		// Token: 0x170002A0 RID: 672
		// (get) Token: 0x06000D61 RID: 3425 RVA: 0x000E7DB0 File Offset: 0x000E5FB0
		// (set) Token: 0x06000D62 RID: 3426 RVA: 0x000E7DC4 File Offset: 0x000E5FC4
		public string Objective { get; set; }

		// Token: 0x04000898 RID: 2200
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u206E\u206F\u206D\u202B\u200C\u202D\u206D\u200D\u200C\u200F\u202A\u206D\u206A\u202E\u202B\u200B\u202E\u202E\u200B\u206B\u200F\u206B\u202B\u200F\u200C\u202B\u202E\u200F\u200D\u202D\u200F\u200D\u206A\u206F\u202D\u202A\u206C\u202D\u200C\u202E;

		// Token: 0x04000899 RID: 2201
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206A\u206F\u206E\u206F\u206D\u206E\u202B\u206A\u206F\u200F\u206A\u200D\u202B\u206B\u206E\u202B\u200F\u200C\u202D\u206E\u200C\u206E\u200D\u202E\u202A\u206D\u202A\u200D\u200D\u200F\u202C\u206A\u206D\u206B\u202E\u200F\u206D\u202E\u206D\u202E\u202E;

		// Token: 0x0400089A RID: 2202
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200F\u202E\u206C\u200C\u206A\u200C\u202D\u206B\u202A\u206B\u206C\u206C\u206C\u200D\u200C\u200E\u200C\u202D\u202C\u200E\u206F\u202D\u202E\u200C\u206E\u206B\u200B\u206C\u206D\u202E\u206A\u200E\u202D\u200B\u200F\u206B\u206D\u206F\u206E\u206E\u202E;

		// Token: 0x0400089B RID: 2203
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u202A\u206B\u202A\u206C\u200D\u206A\u200C\u202E\u202E\u206D\u202C\u200B\u202C\u206A\u200C\u200F\u200F\u200B\u206F\u206F\u200D\u206A\u206E\u202C\u200B\u202D\u200D\u202C\u202E\u202A\u202C\u200D\u206F\u202B\u200D\u200E\u206E\u200D\u200F\u202E;

		// Token: 0x0400089C RID: 2204
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u200E\u200F\u200F\u202E\u202D\u206B\u202E\u200B\u206B\u202E\u200B\u200B\u200E\u206D\u200E\u206A\u202D\u200F\u202B\u206F\u202C\u200F\u200E\u200B\u200B\u200F\u202E\u200F\u202B\u206E\u206F\u206E\u206B\u200E\u206F\u200D\u206E\u202E\u202B\u202E;

		// Token: 0x0400089D RID: 2205
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u206C\u206A\u202C\u200C\u202C\u202C\u206A\u206D\u202D\u200B\u202E\u206F\u202C\u200D\u202D\u200F\u202B\u200D\u206C\u200F\u206C\u206A\u200C\u206A\u206D\u206D\u206E\u206A\u200D\u206C\u206F\u206A\u200B\u200C\u200B\u206A\u200C\u200F\u200C\u202E;

		// Token: 0x0400089E RID: 2206
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202E\u206A\u206D\u200B\u206F\u200C\u202A\u206B\u202C\u206F\u200C\u202A\u202A\u200F\u206B\u206F\u206A\u200B\u202C\u206F\u206F\u206A\u206A\u202C\u200F\u200C\u206F\u202A\u206F\u200B\u206D\u206A\u200D\u206A\u206B\u202E\u200E\u206E\u200F\u202E;

		// Token: 0x0400089F RID: 2207
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u206E\u206D\u200F\u206D\u200F\u200D\u206B\u202D\u202C\u202B\u200B\u200E\u200C\u200D\u200C\u200E\u202E\u202A\u200D\u206D\u202E\u202C\u202C\u200F\u202D\u206D\u202D\u206C\u206A\u206A\u206F\u202A\u202E\u200F\u202D\u202C\u202D\u202E\u200D\u202E;

		// Token: 0x040008A0 RID: 2208
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202A\u202C\u200D\u202D\u200E\u206B\u200C\u202D\u206F\u200E\u202A\u206E\u206B\u200C\u200C\u206A\u206E\u206F\u202D\u200C\u202B\u200F\u206D\u200F\u200F\u206A\u202A\u200B\u202B\u202B\u200D\u202C\u206D\u202D\u206A\u200C\u202E\u202E\u200C\u202C\u202E;

		// Token: 0x040008A1 RID: 2209
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206B\u202C\u206E\u202A\u200F\u206E\u202D\u206A\u202E\u200D\u206C\u202C\u200F\u206F\u202B\u200E\u206B\u202D\u200B\u206D\u206B\u202D\u206E\u202B\u200F\u200B\u200C\u202B\u200D\u206A\u202B\u200C\u200C\u200F\u200F\u202B\u206A\u202A\u200C\u206E\u202E;

		// Token: 0x040008A2 RID: 2210
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202B\u202E\u206A\u206D\u206C\u200E\u206C\u202B\u202C\u202A\u206D\u202D\u206A\u206B\u202B\u206F\u200B\u202E\u200F\u202D\u206B\u200E\u200C\u200C\u200D\u200B\u202D\u206F\u206B\u202C\u202E\u206D\u206F\u200B\u200E\u206E\u206B\u200D\u200B\u206B\u202E;

		// Token: 0x040008A3 RID: 2211
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u202E\u206B\u202D\u202A\u206D\u202E\u200E\u206A\u200F\u202D\u200D\u202E\u206B\u206C\u200F\u206C\u206D\u202C\u200E\u206A\u202C\u200C\u200D\u206E\u200C\u206F\u200B\u206E\u202C\u202E\u200E\u200C\u206A\u200C\u202C\u200F\u202C\u206D\u202D\u202E;
	}
}

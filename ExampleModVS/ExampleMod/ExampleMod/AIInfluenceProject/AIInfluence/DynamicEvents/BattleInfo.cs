using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x020001A4 RID: 420
	[JsonSerializable]
	public class BattleInfo
	{
		// Token: 0x17000288 RID: 648
		// (get) Token: 0x06000D30 RID: 3376 RVA: 0x000E79F0 File Offset: 0x000E5BF0
		// (set) Token: 0x06000D31 RID: 3377 RVA: 0x000E7A04 File Offset: 0x000E5C04
		public string AttackerKingdom { get; set; }

		// Token: 0x17000289 RID: 649
		// (get) Token: 0x06000D32 RID: 3378 RVA: 0x000E7A18 File Offset: 0x000E5C18
		// (set) Token: 0x06000D33 RID: 3379 RVA: 0x000E7A2C File Offset: 0x000E5C2C
		public string DefenderKingdom { get; set; }

		// Token: 0x1700028A RID: 650
		// (get) Token: 0x06000D34 RID: 3380 RVA: 0x000E7A40 File Offset: 0x000E5C40
		// (set) Token: 0x06000D35 RID: 3381 RVA: 0x000E7A54 File Offset: 0x000E5C54
		public string BattleType { get; set; }

		// Token: 0x1700028B RID: 651
		// (get) Token: 0x06000D36 RID: 3382 RVA: 0x000E7A68 File Offset: 0x000E5C68
		// (set) Token: 0x06000D37 RID: 3383 RVA: 0x000E7A7C File Offset: 0x000E5C7C
		public string Winner { get; set; }

		// Token: 0x1700028C RID: 652
		// (get) Token: 0x06000D38 RID: 3384 RVA: 0x000E7A90 File Offset: 0x000E5C90
		// (set) Token: 0x06000D39 RID: 3385 RVA: 0x000E7AA4 File Offset: 0x000E5CA4
		public string AttackerLeader { get; set; }

		// Token: 0x1700028D RID: 653
		// (get) Token: 0x06000D3A RID: 3386 RVA: 0x000E7AB8 File Offset: 0x000E5CB8
		// (set) Token: 0x06000D3B RID: 3387 RVA: 0x000E7ACC File Offset: 0x000E5CCC
		public string DefenderLeader { get; set; }

		// Token: 0x1700028E RID: 654
		// (get) Token: 0x06000D3C RID: 3388 RVA: 0x000E7AE0 File Offset: 0x000E5CE0
		// (set) Token: 0x06000D3D RID: 3389 RVA: 0x000E7AF4 File Offset: 0x000E5CF4
		public string Location { get; set; }

		// Token: 0x1700028F RID: 655
		// (get) Token: 0x06000D3E RID: 3390 RVA: 0x000E7B08 File Offset: 0x000E5D08
		// (set) Token: 0x06000D3F RID: 3391 RVA: 0x000E7B1C File Offset: 0x000E5D1C
		public int DaysAgo { get; set; }

		// Token: 0x17000290 RID: 656
		// (get) Token: 0x06000D40 RID: 3392 RVA: 0x000E7B30 File Offset: 0x000E5D30
		// (set) Token: 0x06000D41 RID: 3393 RVA: 0x000E7B44 File Offset: 0x000E5D44
		public int AttackerTroops { get; set; }

		// Token: 0x17000291 RID: 657
		// (get) Token: 0x06000D42 RID: 3394 RVA: 0x000E7B58 File Offset: 0x000E5D58
		// (set) Token: 0x06000D43 RID: 3395 RVA: 0x000E7B6C File Offset: 0x000E5D6C
		public int DefenderTroops { get; set; }

		// Token: 0x17000292 RID: 658
		// (get) Token: 0x06000D44 RID: 3396 RVA: 0x000E7B80 File Offset: 0x000E5D80
		// (set) Token: 0x06000D45 RID: 3397 RVA: 0x000E7B94 File Offset: 0x000E5D94
		public int AttackerLosses { get; set; }

		// Token: 0x17000293 RID: 659
		// (get) Token: 0x06000D46 RID: 3398 RVA: 0x000E7BA8 File Offset: 0x000E5DA8
		// (set) Token: 0x06000D47 RID: 3399 RVA: 0x000E7BBC File Offset: 0x000E5DBC
		public int DefenderLosses { get; set; }

		// Token: 0x17000294 RID: 660
		// (get) Token: 0x06000D48 RID: 3400 RVA: 0x000E7BD0 File Offset: 0x000E5DD0
		// (set) Token: 0x06000D49 RID: 3401 RVA: 0x000E7BE4 File Offset: 0x000E5DE4
		public CampaignTime BattleTime { get; set; }

		// Token: 0x0400088B RID: 2187
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u206B\u206F\u206E\u206D\u200D\u200F\u200F\u200E\u206D\u202E\u202C\u202C\u200E\u202C\u202A\u202B\u200C\u202C\u202A\u200C\u200F\u202D\u202A\u206A\u206A\u200C\u202A\u206C\u206A\u202D\u200C\u202D\u206A\u200E\u200B\u202C\u202E\u206D\u206D\u202E;

		// Token: 0x0400088C RID: 2188
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u200F\u206B\u206F\u200E\u202E\u202A\u200C\u200F\u206A\u200B\u200B\u206B\u206F\u202D\u200D\u200F\u200B\u206B\u202A\u200D\u200E\u200D\u200D\u206F\u202A\u200D\u202D\u206A\u202E\u202E\u206B\u206E\u206F\u206A\u206E\u200D\u206E\u206F\u206C\u202E;

		// Token: 0x0400088D RID: 2189
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u200F\u200B\u200D\u206A\u206A\u202A\u202C\u202B\u206A\u206B\u200B\u206F\u200D\u200B\u200D\u202C\u206F\u202B\u206B\u202E\u206C\u202D\u200E\u200F\u200E\u202E\u202E\u206A\u202E\u202A\u202D\u202D\u206B\u202E\u206A\u202C\u200C\u200E\u200F\u202E;

		// Token: 0x0400088E RID: 2190
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u202D\u200B\u200C\u200C\u202C\u200C\u206D\u206F\u206A\u202C\u202B\u200D\u200D\u206D\u202A\u202B\u206C\u206E\u206F\u200E\u206D\u202A\u206A\u200F\u200B\u202A\u202E\u200D\u200B\u202E\u200B\u202B\u200B\u206C\u206F\u206A\u206F\u206D\u200F\u202E;

		// Token: 0x0400088F RID: 2191
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u200C\u200F\u206C\u206E\u202A\u202A\u206D\u206E\u200D\u200C\u206E\u206C\u202C\u202E\u200B\u206C\u202A\u202D\u206D\u202C\u200D\u202E\u200C\u200F\u206E\u206B\u202A\u206A\u206F\u202E\u200C\u202C\u206C\u206A\u206A\u202B\u206C\u202A\u206D\u202E;

		// Token: 0x04000890 RID: 2192
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u206F\u206E\u202D\u202E\u206F\u206C\u206C\u206D\u202D\u200D\u206C\u202C\u200E\u202D\u200F\u202B\u200F\u206F\u200E\u202D\u200B\u206C\u200C\u206A\u200E\u202B\u200B\u202A\u200B\u202C\u206F\u202C\u202C\u206A\u206C\u200B\u200C\u202C\u202B\u202E;

		// Token: 0x04000891 RID: 2193
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202E\u202E\u200E\u200C\u202B\u200E\u202E\u202A\u206C\u206B\u206D\u202A\u200B\u200E\u206C\u206E\u202A\u200D\u200F\u206D\u206C\u206F\u200B\u202E\u206B\u200E\u206E\u202E\u202B\u206A\u206C\u206C\u206C\u206A\u202B\u200C\u202E\u202A\u202C\u200F\u202E;

		// Token: 0x04000892 RID: 2194
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202B\u206F\u206B\u206D\u202C\u206D\u202E\u202A\u202B\u206E\u200E\u206A\u200F\u206C\u202E\u202A\u206A\u202D\u200E\u202C\u202D\u206F\u200F\u202E\u202C\u200E\u206C\u200C\u200D\u200D\u206A\u202E\u202C\u200D\u202A\u202B\u200C\u200D\u200E\u206B\u202E;

		// Token: 0x04000893 RID: 2195
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206B\u202B\u200C\u200E\u206B\u202E\u206E\u206E\u206E\u202B\u206A\u206B\u200E\u202B\u206C\u206C\u206F\u206A\u202E\u202C\u200E\u206E\u202B\u200D\u202C\u200B\u200D\u202A\u200E\u206F\u202D\u202C\u206A\u202C\u200C\u200E\u202A\u206B\u206B\u206C\u202E;

		// Token: 0x04000894 RID: 2196
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202D\u206D\u206D\u200C\u200D\u202B\u200B\u200F\u200E\u206C\u206C\u200C\u202D\u202B\u202D\u206F\u206C\u206B\u206E\u200F\u202B\u206C\u206D\u206C\u202C\u200D\u206C\u206E\u206B\u206C\u202B\u202C\u206C\u202B\u206E\u202C\u200C\u206F\u206D\u202C\u202E;

		// Token: 0x04000895 RID: 2197
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206E\u206A\u206F\u200C\u200D\u206B\u206C\u206E\u206E\u200B\u206A\u206B\u206E\u206E\u200B\u206E\u206C\u206F\u206D\u200C\u206B\u202C\u200B\u202C\u206B\u202E\u206B\u202E\u206A\u200F\u202C\u206F\u200B\u206B\u200F\u200E\u206C\u200F\u206A\u200D\u202E;

		// Token: 0x04000896 RID: 2198
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202C\u202C\u206B\u206C\u206F\u202B\u200B\u206D\u202B\u202C\u202B\u200C\u202B\u206B\u206C\u202D\u206B\u202D\u206B\u206B\u200D\u202D\u202D\u206C\u202E\u206C\u200C\u202C\u206A\u202D\u200B\u200D\u200C\u206A\u202B\u200C\u202D\u202A\u206C\u206D\u202E;

		// Token: 0x04000897 RID: 2199
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u206A\u202D\u200B\u200C\u206F\u200D\u202B\u206E\u206B\u202A\u202C\u202B\u202A\u202E\u200B\u206F\u206E\u206A\u200E\u200C\u200C\u202A\u200E\u200D\u202D\u206A\u202A\u206F\u206D\u202D\u202D\u200C\u200C\u200C\u200C\u206C\u206D\u200D\u206D\u200D\u202E;
	}
}

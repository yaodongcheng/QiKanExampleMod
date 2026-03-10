using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence
{
	// Token: 0x02000074 RID: 116
	[JsonSerializable]
	public class TimeContext
	{
		// Token: 0x1700011C RID: 284
		// (get) Token: 0x06000457 RID: 1111 RVA: 0x0005C118 File Offset: 0x0005A318
		// (set) Token: 0x06000458 RID: 1112 RVA: 0x0005C12C File Offset: 0x0005A32C
		public string Season { get; set; }

		// Token: 0x1700011D RID: 285
		// (get) Token: 0x06000459 RID: 1113 RVA: 0x0005C140 File Offset: 0x0005A340
		// (set) Token: 0x0600045A RID: 1114 RVA: 0x0005C154 File Offset: 0x0005A354
		public int Year { get; set; }

		// Token: 0x1700011E RID: 286
		// (get) Token: 0x0600045B RID: 1115 RVA: 0x0005C168 File Offset: 0x0005A368
		// (set) Token: 0x0600045C RID: 1116 RVA: 0x0005C17C File Offset: 0x0005A37C
		public int Month { get; set; }

		// Token: 0x1700011F RID: 287
		// (get) Token: 0x0600045D RID: 1117 RVA: 0x0005C190 File Offset: 0x0005A390
		// (set) Token: 0x0600045E RID: 1118 RVA: 0x0005C1A4 File Offset: 0x0005A3A4
		public string TimeOfDay { get; set; }

		// Token: 0x17000120 RID: 288
		// (get) Token: 0x0600045F RID: 1119 RVA: 0x0005C1B8 File Offset: 0x0005A3B8
		// (set) Token: 0x06000460 RID: 1120 RVA: 0x0005C1CC File Offset: 0x0005A3CC
		public int Hour { get; set; }

		// Token: 0x17000121 RID: 289
		// (get) Token: 0x06000461 RID: 1121 RVA: 0x0005C1E0 File Offset: 0x0005A3E0
		// (set) Token: 0x06000462 RID: 1122 RVA: 0x0005C1F4 File Offset: 0x0005A3F4
		[JsonIgnore]
		public CampaignTime LastUpdated { get; set; }

		// Token: 0x04000295 RID: 661
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u206A\u206A\u206A\u200C\u206C\u206E\u206B\u202A\u206D\u206B\u206D\u202B\u206B\u202D\u202B\u202E\u200C\u200E\u202C\u206D\u202D\u206E\u202B\u200B\u200F\u202E\u206B\u200E\u206E\u200C\u206D\u206E\u206C\u206C\u206A\u206F\u200B\u206F\u206E\u202E;

		// Token: 0x04000296 RID: 662
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200B\u206A\u200D\u206F\u200C\u202B\u206A\u202A\u202D\u202E\u206F\u202D\u202B\u200E\u202A\u206F\u206F\u206D\u200C\u200D\u202D\u200D\u206D\u202B\u200D\u200C\u200C\u206E\u200E\u200F\u206A\u200B\u206B\u202B\u206A\u200F\u206A\u202A\u200F\u206E\u202E;

		// Token: 0x04000297 RID: 663
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206D\u202D\u200F\u202B\u206B\u206C\u206F\u206D\u206E\u202C\u200C\u202B\u202B\u200F\u200F\u206E\u200B\u200F\u202C\u200E\u206E\u202B\u206D\u200F\u200D\u206F\u200D\u202E\u202C\u202C\u202B\u202C\u200C\u202E\u206A\u200F\u206F\u200F\u202D\u202E\u202E;

		// Token: 0x04000298 RID: 664
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u206B\u202E\u200C\u200F\u206F\u206E\u206D\u206F\u206C\u200F\u202D\u206D\u200C\u206F\u202B\u206A\u200D\u206A\u206F\u202C\u206E\u202A\u200F\u202D\u200D\u202A\u206D\u202E\u206F\u200B\u200E\u202B\u206F\u202A\u202D\u202A\u206A\u202C\u202C\u202E;

		// Token: 0x04000299 RID: 665
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202A\u202D\u202B\u200F\u206F\u200F\u206C\u206B\u206D\u202E\u202D\u200E\u206C\u200F\u202B\u206E\u200D\u206A\u200F\u206A\u206F\u200F\u202E\u206B\u206D\u202E\u200F\u206B\u202E\u206C\u202E\u200C\u202B\u206C\u200B\u202E\u200F\u206F\u200C\u202E\u202E;

		// Token: 0x0400029A RID: 666
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u200F\u200C\u200C\u206D\u200F\u202B\u206C\u206E\u200E\u206F\u202E\u206B\u200C\u200E\u206A\u206D\u206F\u202E\u202C\u200F\u202B\u206B\u206F\u200F\u206E\u206D\u202E\u200B\u202B\u202B\u202E\u206A\u206F\u206D\u202D\u202C\u206D\u200F\u206E\u206E\u202E;
	}
}

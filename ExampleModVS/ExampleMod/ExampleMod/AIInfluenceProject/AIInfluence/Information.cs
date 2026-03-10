using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x0200007C RID: 124
	[JsonSerializable]
	public class Information
	{
		// Token: 0x1700014F RID: 335
		// (get) Token: 0x060004C5 RID: 1221 RVA: 0x0005C910 File Offset: 0x0005AB10
		// (set) Token: 0x060004C6 RID: 1222 RVA: 0x0005C924 File Offset: 0x0005AB24
		[JsonProperty("id")]
		public string Id { get; set; }

		// Token: 0x17000150 RID: 336
		// (get) Token: 0x060004C7 RID: 1223 RVA: 0x0005C938 File Offset: 0x0005AB38
		// (set) Token: 0x060004C8 RID: 1224 RVA: 0x0005C94C File Offset: 0x0005AB4C
		[JsonProperty("description")]
		public string Description { get; set; }

		// Token: 0x17000151 RID: 337
		// (get) Token: 0x060004C9 RID: 1225 RVA: 0x0005C960 File Offset: 0x0005AB60
		// (set) Token: 0x060004CA RID: 1226 RVA: 0x0005C974 File Offset: 0x0005AB74
		[JsonProperty("usageChance")]
		public int UsageChance { get; set; }

		// Token: 0x17000152 RID: 338
		// (get) Token: 0x060004CB RID: 1227 RVA: 0x0005C988 File Offset: 0x0005AB88
		// (set) Token: 0x060004CC RID: 1228 RVA: 0x0005C99C File Offset: 0x0005AB9C
		[JsonProperty("applicableNPCs")]
		public List<string> ApplicableNPCs { get; set; }

		// Token: 0x17000153 RID: 339
		// (get) Token: 0x060004CD RID: 1229 RVA: 0x0005C9B0 File Offset: 0x0005ABB0
		// (set) Token: 0x060004CE RID: 1230 RVA: 0x0005C9C4 File Offset: 0x0005ABC4
		[JsonProperty("category")]
		public string Category { get; set; }

		// Token: 0x040002C8 RID: 712
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u202A\u202D\u206F\u200B\u206D\u200B\u206E\u202B\u202B\u206B\u206E\u200E\u202D\u200E\u202E\u206C\u202A\u200F\u200F\u202A\u202E\u206C\u206C\u200F\u206A\u202C\u206A\u206E\u202E\u206B\u202D\u206A\u200E\u200E\u206B\u202B\u206D\u200D\u200C\u202E;

		// Token: 0x040002C9 RID: 713
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u200D\u200C\u206D\u206C\u200D\u200F\u200F\u200E\u200D\u206E\u200B\u202D\u200D\u200F\u206E\u202C\u200B\u206A\u206D\u202D\u202B\u200F\u200E\u202A\u200B\u202B\u206E\u206C\u206F\u206D\u206B\u206E\u200E\u206A\u200D\u202C\u202D\u206E\u200C\u202E;

		// Token: 0x040002CA RID: 714
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206C\u206B\u202D\u202E\u206B\u202E\u206F\u202C\u202D\u200D\u206E\u206B\u200B\u202A\u200D\u202D\u202B\u206F\u202D\u200E\u202D\u206B\u200C\u206A\u200C\u202D\u200B\u202A\u206C\u200B\u206D\u202C\u206A\u202E\u202E\u206B\u200F\u206A\u202E;

		// Token: 0x040002CB RID: 715
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200C\u206D\u202E\u200F\u206E\u202D\u206D\u202E\u206D\u200C\u206F\u202E\u202D\u206E\u200F\u202D\u206E\u200C\u206C\u202C\u202C\u206F\u200B\u206D\u200E\u200F\u200B\u200D\u206C\u200D\u200B\u200E\u202B\u200D\u200D\u206A\u206C\u200E\u200F\u200E\u202E;

		// Token: 0x040002CC RID: 716
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u200E\u206D\u202B\u206D\u202A\u206A\u206F\u202C\u206A\u202A\u202D\u202E\u206E\u200E\u206E\u200B\u200F\u202D\u202B\u202C\u206C\u200E\u202C\u200C\u202E\u206F\u200C\u202E\u206D\u202B\u200E\u202B\u202C\u202A\u206C\u202E\u202A\u202C\u202E\u202E;
	}
}

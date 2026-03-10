using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x0200007B RID: 123
	[JsonSerializable]
	public class WorldSecret
	{
		// Token: 0x17000149 RID: 329
		// (get) Token: 0x060004B8 RID: 1208 RVA: 0x0005C820 File Offset: 0x0005AA20
		// (set) Token: 0x060004B9 RID: 1209 RVA: 0x0005C834 File Offset: 0x0005AA34
		[JsonProperty("id")]
		public string Id { get; set; }

		// Token: 0x1700014A RID: 330
		// (get) Token: 0x060004BA RID: 1210 RVA: 0x0005C848 File Offset: 0x0005AA48
		// (set) Token: 0x060004BB RID: 1211 RVA: 0x0005C85C File Offset: 0x0005AA5C
		[JsonProperty("description")]
		public string Description { get; set; }

		// Token: 0x1700014B RID: 331
		// (get) Token: 0x060004BC RID: 1212 RVA: 0x0005C870 File Offset: 0x0005AA70
		// (set) Token: 0x060004BD RID: 1213 RVA: 0x0005C884 File Offset: 0x0005AA84
		[JsonProperty("knowledgeChance")]
		public int KnowledgeChance { get; set; }

		// Token: 0x1700014C RID: 332
		// (get) Token: 0x060004BE RID: 1214 RVA: 0x0005C898 File Offset: 0x0005AA98
		// (set) Token: 0x060004BF RID: 1215 RVA: 0x0005C8AC File Offset: 0x0005AAAC
		[JsonProperty("applicableNPCs")]
		public List<string> ApplicableNPCs { get; set; }

		// Token: 0x1700014D RID: 333
		// (get) Token: 0x060004C0 RID: 1216 RVA: 0x0005C8C0 File Offset: 0x0005AAC0
		// (set) Token: 0x060004C1 RID: 1217 RVA: 0x0005C8D4 File Offset: 0x0005AAD4
		[JsonProperty("accessLevel")]
		public string AccessLevel { get; set; }

		// Token: 0x1700014E RID: 334
		// (get) Token: 0x060004C2 RID: 1218 RVA: 0x0005C8E8 File Offset: 0x0005AAE8
		// (set) Token: 0x060004C3 RID: 1219 RVA: 0x0005C8FC File Offset: 0x0005AAFC
		[JsonProperty("tags")]
		public List<string> Tags { get; set; }

		// Token: 0x040002C2 RID: 706
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u206B\u200E\u200E\u202B\u200E\u206A\u202B\u206E\u206F\u202B\u206E\u206D\u202C\u206E\u202D\u200E\u200D\u206F\u206D\u200D\u200B\u206E\u202D\u206F\u206B\u202E\u202B\u206C\u200D\u206E\u206A\u202C\u206B\u202B\u200D\u202C\u202E\u200D\u206A\u202E;

		// Token: 0x040002C3 RID: 707
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u202D\u200C\u200C\u202A\u206A\u202E\u206A\u206A\u202E\u202B\u200F\u202C\u202B\u200D\u206B\u200B\u206D\u206F\u200D\u202D\u200B\u200E\u206A\u202B\u202A\u200D\u206D\u200F\u200E\u202E\u200C\u206B\u206E\u206B\u202E\u206C\u202D\u206D\u206D\u202E;

		// Token: 0x040002C4 RID: 708
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200E\u206D\u200B\u200E\u202B\u202E\u200E\u200C\u200F\u202A\u200F\u200D\u200C\u202A\u200C\u206B\u202D\u200D\u202D\u206E\u202B\u202E\u200C\u200E\u206E\u200D\u202B\u206C\u200C\u206B\u206B\u206E\u202C\u202C\u200F\u206B\u200D\u200F\u200F\u206C\u202E;

		// Token: 0x040002C5 RID: 709
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u202D\u206A\u206D\u200D\u206C\u206F\u200D\u202D\u206F\u200F\u202A\u200E\u206D\u200F\u202B\u202E\u200E\u200B\u200D\u206F\u206D\u200F\u202B\u206B\u200E\u206A\u202A\u200B\u200F\u202E\u202C\u206E\u202C\u206D\u200B\u202B\u200C\u200B\u200B\u206A\u202E;

		// Token: 0x040002C6 RID: 710
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u206A\u202B\u202D\u200B\u202B\u200E\u206E\u206D\u200E\u202E\u200B\u202C\u200C\u202A\u206A\u206D\u206C\u206C\u202E\u202D\u202D\u200F\u200C\u200E\u202B\u200E\u206F\u202D\u200F\u200D\u206E\u202D\u206E\u200D\u202A\u206B\u200D\u206D\u202E\u202E;

		// Token: 0x040002C7 RID: 711
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u206F\u206D\u200F\u200D\u202B\u202C\u206C\u202A\u206D\u202D\u202C\u202A\u200C\u200E\u206E\u200F\u202E\u200F\u202C\u202B\u206B\u206C\u206A\u200C\u202E\u206A\u206E\u200F\u206B\u206D\u200C\u202D\u200F\u202B\u200E\u200F\u206F\u202E\u202B\u206C\u202E;
	}
}

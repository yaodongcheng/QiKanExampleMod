using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x0200007A RID: 122
	[JsonSerializable]
	public class ItemTransferData
	{
		// Token: 0x17000146 RID: 326
		// (get) Token: 0x060004B1 RID: 1201 RVA: 0x0005C7A8 File Offset: 0x0005A9A8
		// (set) Token: 0x060004B2 RID: 1202 RVA: 0x0005C7BC File Offset: 0x0005A9BC
		[JsonProperty("item_id")]
		public string ItemId { get; set; }

		// Token: 0x17000147 RID: 327
		// (get) Token: 0x060004B3 RID: 1203 RVA: 0x0005C7D0 File Offset: 0x0005A9D0
		// (set) Token: 0x060004B4 RID: 1204 RVA: 0x0005C7E4 File Offset: 0x0005A9E4
		[JsonProperty("amount")]
		public int Amount { get; set; }

		// Token: 0x17000148 RID: 328
		// (get) Token: 0x060004B5 RID: 1205 RVA: 0x0005C7F8 File Offset: 0x0005A9F8
		// (set) Token: 0x060004B6 RID: 1206 RVA: 0x0005C80C File Offset: 0x0005AA0C
		[JsonProperty("action")]
		public string Action { get; set; }

		// Token: 0x040002BF RID: 703
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u200C\u202A\u202A\u200E\u202B\u200B\u206B\u200F\u200B\u206C\u202C\u202B\u200F\u206B\u202D\u206A\u200E\u206D\u200E\u202C\u206F\u202E\u200D\u202D\u206A\u202D\u206A\u202A\u206F\u202A\u202C\u206C\u206C\u200C\u206C\u206C\u200E\u202B\u200D\u202E;

		// Token: 0x040002C0 RID: 704
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202C\u202C\u200D\u200E\u200C\u206F\u206D\u206A\u206A\u202A\u202B\u202D\u202B\u206E\u206D\u202C\u206B\u202B\u200E\u206B\u206D\u206E\u206A\u200E\u202C\u202B\u206D\u200E\u202B\u200C\u206B\u206F\u200D\u206B\u206C\u206E\u200F\u200B\u202A\u202B\u202E;

		// Token: 0x040002C1 RID: 705
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u202D\u202C\u206A\u200F\u206A\u206D\u202C\u202A\u202D\u206B\u200E\u200B\u206A\u200B\u200C\u206C\u206C\u206C\u202D\u200D\u200C\u206B\u200C\u202D\u202C\u206F\u202E\u200D\u200F\u200F\u200D\u200C\u206C\u202E\u200E\u200D\u200F\u206C\u200D\u202E;
	}
}

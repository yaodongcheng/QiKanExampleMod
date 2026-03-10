using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x02000079 RID: 121
	[JsonSerializable]
	public class MoneyTransferInfo
	{
		// Token: 0x17000144 RID: 324
		// (get) Token: 0x060004AC RID: 1196 RVA: 0x0005C758 File Offset: 0x0005A958
		// (set) Token: 0x060004AD RID: 1197 RVA: 0x0005C76C File Offset: 0x0005A96C
		[JsonProperty("action")]
		public string Action { get; set; }

		// Token: 0x17000145 RID: 325
		// (get) Token: 0x060004AE RID: 1198 RVA: 0x0005C780 File Offset: 0x0005A980
		// (set) Token: 0x060004AF RID: 1199 RVA: 0x0005C794 File Offset: 0x0005A994
		[JsonProperty("amount")]
		public int Amount { get; set; }

		// Token: 0x040002BD RID: 701
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u202A\u200E\u206F\u200E\u200E\u206C\u202D\u200E\u206D\u206C\u206A\u206D\u200F\u200D\u202D\u206A\u200C\u202B\u200D\u206B\u200F\u200C\u202E\u200F\u200C\u200F\u206C\u206C\u206B\u200D\u200E\u206F\u200C\u206A\u200B\u206B\u206B\u202D\u200E\u202E;

		// Token: 0x040002BE RID: 702
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206C\u200B\u206E\u202C\u200D\u206F\u200F\u200D\u206E\u202C\u206B\u202B\u200E\u200F\u202E\u200D\u206B\u200C\u202E\u202E\u206C\u202D\u206E\u202D\u206D\u202E\u206D\u200D\u200E\u206F\u200F\u202D\u206F\u200D\u200E\u202D\u202A\u200E\u202B\u206C\u202E;
	}
}

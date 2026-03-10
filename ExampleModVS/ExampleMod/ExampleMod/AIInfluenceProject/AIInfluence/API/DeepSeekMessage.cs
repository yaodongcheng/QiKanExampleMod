using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002A7 RID: 679
	public class DeepSeekMessage
	{
		// Token: 0x170003B1 RID: 945
		// (get) Token: 0x06001372 RID: 4978 RVA: 0x0011BF24 File Offset: 0x0011A124
		// (set) Token: 0x06001373 RID: 4979 RVA: 0x0011BF38 File Offset: 0x0011A138
		[JsonProperty("role")]
		public string Role { get; set; } = string.Empty;

		// Token: 0x170003B2 RID: 946
		// (get) Token: 0x06001374 RID: 4980 RVA: 0x0011BF4C File Offset: 0x0011A14C
		// (set) Token: 0x06001375 RID: 4981 RVA: 0x0011BF60 File Offset: 0x0011A160
		[JsonProperty("content")]
		public string Content { get; set; } = string.Empty;

		// Token: 0x04000D9F RID: 3487
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u206D\u206D\u200F\u202E\u206E\u202A\u200C\u206D\u206C\u206F\u206E\u206A\u206B\u202B\u200D\u202C\u206A\u202D\u200E\u202C\u200C\u202C\u200B\u206B\u206E\u200C\u200D\u202C\u202C\u200B\u200E\u200B\u202D\u200C\u206F\u200E\u200B\u200E\u206F\u202E;

		// Token: 0x04000DA0 RID: 3488
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u200D\u202C\u202A\u200F\u202C\u206A\u200C\u206C\u200B\u206F\u206D\u200C\u206F\u202D\u202B\u200D\u200B\u202A\u202A\u206B\u202D\u206F\u200B\u206F\u200D\u202D\u202A\u206F\u206C\u206B\u206A\u202D\u200D\u202D\u200C\u200D\u206F\u206B\u206D\u202E;
	}
}

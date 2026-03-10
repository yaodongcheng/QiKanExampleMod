using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002AA RID: 682
	public class DeepSeekChatResponse
	{
		// Token: 0x170003B6 RID: 950
		// (get) Token: 0x0600137F RID: 4991 RVA: 0x0011C044 File Offset: 0x0011A244
		// (set) Token: 0x06001380 RID: 4992 RVA: 0x0011C058 File Offset: 0x0011A258
		[JsonProperty("choices")]
		public List<DeepSeekChatChoice> Choices { get; set; } = new List<DeepSeekChatChoice>();

		// Token: 0x04000DA4 RID: 3492
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<DeepSeekChatChoice> \u202D\u206E\u200F\u206A\u202A\u200C\u200F\u200E\u202E\u206D\u206B\u202C\u200E\u200E\u202A\u202A\u202A\u206F\u206E\u202A\u200F\u202C\u202B\u200B\u206F\u200C\u202B\u206F\u202E\u206D\u202D\u202C\u206A\u200D\u202C\u206B\u202C\u202A\u202B\u200C\u202E;
	}
}

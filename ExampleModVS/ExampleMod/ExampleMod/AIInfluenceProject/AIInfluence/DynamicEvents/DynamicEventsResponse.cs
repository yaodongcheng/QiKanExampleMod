using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x0200016A RID: 362
	[JsonSerializable]
	public class DynamicEventsResponse
	{
		// Token: 0x17000274 RID: 628
		// (get) Token: 0x06000BE8 RID: 3048 RVA: 0x000C9A1C File Offset: 0x000C7C1C
		// (set) Token: 0x06000BE9 RID: 3049 RVA: 0x000C9A30 File Offset: 0x000C7C30
		[JsonProperty("events")]
		public List<DynamicEvent> Events { get; set; }

		// Token: 0x06000BEA RID: 3050 RVA: 0x000C9A44 File Offset: 0x000C7C44
		public DynamicEventsResponse()
		{
			this.Events = new List<DynamicEvent>();
		}

		// Token: 0x04000749 RID: 1865
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<DynamicEvent> \u200D\u206C\u200D\u206A\u202A\u202D\u202A\u202B\u206C\u200B\u206E\u206C\u202C\u206D\u206E\u200B\u200D\u200F\u202A\u206E\u200B\u206D\u200D\u200E\u202E\u206E\u206C\u206F\u200B\u202B\u206C\u206D\u206B\u200B\u206F\u202E\u200D\u200F\u200C\u202E\u202E;
	}
}

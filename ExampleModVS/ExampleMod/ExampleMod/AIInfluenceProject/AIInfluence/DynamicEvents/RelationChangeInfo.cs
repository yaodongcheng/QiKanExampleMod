using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x02000167 RID: 359
	[JsonSerializable]
	public class RelationChangeInfo
	{
		// Token: 0x17000261 RID: 609
		// (get) Token: 0x06000BBE RID: 3006 RVA: 0x000C9658 File Offset: 0x000C7858
		// (set) Token: 0x06000BBF RID: 3007 RVA: 0x000C966C File Offset: 0x000C786C
		[JsonProperty("kingdom1_id")]
		public string Kingdom1Id { get; set; }

		// Token: 0x17000262 RID: 610
		// (get) Token: 0x06000BC0 RID: 3008 RVA: 0x000C9680 File Offset: 0x000C7880
		// (set) Token: 0x06000BC1 RID: 3009 RVA: 0x000C9694 File Offset: 0x000C7894
		[JsonProperty("kingdom2_id")]
		public string Kingdom2Id { get; set; }

		// Token: 0x17000263 RID: 611
		// (get) Token: 0x06000BC2 RID: 3010 RVA: 0x000C96A8 File Offset: 0x000C78A8
		// (set) Token: 0x06000BC3 RID: 3011 RVA: 0x000C96BC File Offset: 0x000C78BC
		[JsonProperty("change")]
		public int Change { get; set; }

		// Token: 0x17000264 RID: 612
		// (get) Token: 0x06000BC4 RID: 3012 RVA: 0x000C96D0 File Offset: 0x000C78D0
		// (set) Token: 0x06000BC5 RID: 3013 RVA: 0x000C96E4 File Offset: 0x000C78E4
		[JsonProperty("reason")]
		public string Reason { get; set; }

		// Token: 0x04000737 RID: 1847
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u202C\u202B\u206E\u206B\u206A\u202E\u202C\u202C\u200B\u200B\u202B\u200B\u200E\u200E\u200C\u206C\u200B\u202E\u200E\u200B\u202D\u206C\u206D\u206F\u202D\u200E\u202C\u202B\u200C\u200F\u200C\u206A\u200C\u202B\u200B\u202B\u206D\u206F\u202E;

		// Token: 0x04000738 RID: 1848
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u200F\u206D\u202D\u202E\u206D\u200F\u200E\u200C\u202E\u202C\u200C\u200F\u206C\u206A\u206D\u200E\u200E\u202B\u206C\u202D\u206A\u202A\u202A\u202D\u200B\u202C\u200C\u202C\u206B\u206B\u202D\u200E\u202E\u202A\u200F\u202A\u202A\u206A\u206F\u202E;

		// Token: 0x04000739 RID: 1849
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200F\u206E\u200B\u200E\u202A\u206E\u202A\u202B\u200D\u200C\u200C\u200E\u200C\u200C\u206F\u202D\u200D\u206B\u206C\u206D\u206F\u202C\u206A\u200D\u206E\u202B\u206A\u202A\u200F\u202D\u202B\u200B\u200B\u200B\u200B\u202C\u202D\u202B\u202E\u200E\u202E;

		// Token: 0x0400073A RID: 1850
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u202B\u200F\u202B\u200F\u202A\u206B\u202B\u206D\u200C\u202C\u200F\u200D\u206E\u206A\u200E\u200E\u206D\u206B\u200B\u206E\u206C\u206E\u200D\u206D\u206D\u206A\u202D\u202E\u200F\u202C\u206A\u206C\u200E\u202A\u206C\u200D\u202E\u206F\u206C\u202E;
	}
}

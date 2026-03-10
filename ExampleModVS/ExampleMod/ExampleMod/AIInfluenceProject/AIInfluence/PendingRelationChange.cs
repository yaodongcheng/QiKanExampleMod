using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.Library;

namespace AIInfluence
{
	// Token: 0x0200006D RID: 109
	[JsonSerializable]
	public class PendingRelationChange
	{
		// Token: 0x170000FD RID: 253
		// (get) Token: 0x0600040F RID: 1039 RVA: 0x0005BA70 File Offset: 0x00059C70
		// (set) Token: 0x06000410 RID: 1040 RVA: 0x0005BA84 File Offset: 0x00059C84
		public int RelationChange { get; set; }

		// Token: 0x170000FE RID: 254
		// (get) Token: 0x06000411 RID: 1041 RVA: 0x0005BA98 File Offset: 0x00059C98
		// (set) Token: 0x06000412 RID: 1042 RVA: 0x0005BAAC File Offset: 0x00059CAC
		public string Message { get; set; }

		// Token: 0x170000FF RID: 255
		// (get) Token: 0x06000413 RID: 1043 RVA: 0x0005BAC0 File Offset: 0x00059CC0
		// (set) Token: 0x06000414 RID: 1044 RVA: 0x0005BAD4 File Offset: 0x00059CD4
		public Color Color { get; set; }

		// Token: 0x04000277 RID: 631
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200B\u206D\u200B\u206F\u202A\u200F\u200D\u202E\u202C\u200E\u200E\u202A\u200B\u202C\u206C\u200E\u206A\u206D\u202C\u206A\u200C\u202E\u202A\u206C\u206B\u206C\u200C\u206C\u206D\u206F\u200E\u200C\u206A\u206F\u202C\u200C\u206F\u200C\u202A\u200E\u202E;

		// Token: 0x04000278 RID: 632
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u202D\u206C\u200E\u202E\u200D\u206E\u200F\u200E\u202A\u202A\u202B\u206D\u206B\u200B\u200F\u206D\u202C\u202B\u206E\u202A\u206E\u206A\u200C\u206D\u206C\u200B\u202C\u200F\u202D\u200E\u202B\u202A\u206A\u200B\u206C\u206C\u200D\u200F\u206D\u202E;

		// Token: 0x04000279 RID: 633
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Color \u206C\u206B\u202A\u206E\u200E\u202A\u200B\u200F\u202E\u202E\u206C\u206E\u202D\u206C\u200F\u206E\u200D\u200F\u206D\u206F\u206A\u206E\u202A\u200E\u206D\u206F\u200E\u200F\u202E\u206F\u202C\u206F\u200C\u206F\u206F\u202C\u206D\u200B\u202C\u206F\u202E;
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AIInfluence;
using Newtonsoft.Json;

// Token: 0x020000BE RID: 190
internal class \u200E\u202D\u206F\u200E\u200F\u202D\u206B\u206B\u202B\u206D\u206B\u202B\u206A\u202B\u200C\u200F\u206E\u202C\u206D\u206A\u202E\u200E\u206F\u206A\u202E\u200E\u206B\u200E\u206F\u206D\u206C\u200F\u202D\u200D\u200E\u206E\u206C\u202D\u200B\u202D\u202E
{
	// Token: 0x17000179 RID: 377
	// (get) Token: 0x06000672 RID: 1650 RVA: 0x0008283C File Offset: 0x00080A3C
	// (set) Token: 0x06000673 RID: 1651 RVA: 0x00082850 File Offset: 0x00080A50
	[JsonProperty("ownershipHistory")]
	public Dictionary<string, SettlementOwnershipHistory> ownershipHistory { get; set; }

	// Token: 0x1700017A RID: 378
	// (get) Token: 0x06000674 RID: 1652 RVA: 0x00082864 File Offset: 0x00080A64
	// (set) Token: 0x06000675 RID: 1653 RVA: 0x00082878 File Offset: 0x00080A78
	[JsonProperty("isInitialized")]
	public bool isInitialized { get; set; }

	// Token: 0x04000422 RID: 1058
	[CompilerGenerated]
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private Dictionary<string, SettlementOwnershipHistory> \u202E\u200E\u200C\u200B\u200F\u200E\u206E\u206E\u202B\u202B\u200E\u200F\u200B\u202D\u200B\u206B\u202C\u200C\u200B\u206C\u202E\u200F\u206B\u200E\u202E\u206C\u206C\u202C\u202C\u200F\u200C\u206B\u200B\u200B\u200F\u200D\u206F\u206C\u206F\u202D\u202E;

	// Token: 0x04000423 RID: 1059
	[CompilerGenerated]
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private bool \u206F\u202D\u206A\u202B\u200D\u202D\u206B\u200C\u202D\u206B\u202C\u200B\u200F\u200E\u206A\u206C\u206F\u202D\u206F\u206E\u206B\u206A\u202B\u206F\u200B\u200F\u206D\u200B\u202D\u202A\u202A\u202C\u206A\u202B\u206C\u200D\u202B\u206D\u200D\u206E\u202E;
}

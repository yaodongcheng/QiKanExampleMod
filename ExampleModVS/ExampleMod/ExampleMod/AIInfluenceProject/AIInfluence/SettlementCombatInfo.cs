using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;

namespace AIInfluence
{
	// Token: 0x020000B6 RID: 182
	[JsonSerializable]
	public class SettlementCombatInfo
	{
		// Token: 0x17000165 RID: 357
		// (get) Token: 0x06000626 RID: 1574 RVA: 0x0007F630 File Offset: 0x0007D830
		// (set) Token: 0x06000627 RID: 1575 RVA: 0x0007F644 File Offset: 0x0007D844
		public string SettlementName { get; set; }

		// Token: 0x17000166 RID: 358
		// (get) Token: 0x06000628 RID: 1576 RVA: 0x0007F658 File Offset: 0x0007D858
		// (set) Token: 0x06000629 RID: 1577 RVA: 0x0007F66C File Offset: 0x0007D86C
		public SettlementCombatHandler.SettlementCombatType CombatType { get; set; }

		// Token: 0x17000167 RID: 359
		// (get) Token: 0x0600062A RID: 1578 RVA: 0x0007F680 File Offset: 0x0007D880
		// (set) Token: 0x0600062B RID: 1579 RVA: 0x0007F694 File Offset: 0x0007D894
		public CampaignTime StartTime { get; set; }

		// Token: 0x17000168 RID: 360
		// (get) Token: 0x0600062C RID: 1580 RVA: 0x0007F6A8 File Offset: 0x0007D8A8
		// (set) Token: 0x0600062D RID: 1581 RVA: 0x0007F6BC File Offset: 0x0007D8BC
		public List<string> Events { get; set; } = new List<string>();

		// Token: 0x040003FE RID: 1022
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200F\u200E\u206E\u206E\u202C\u206F\u202E\u206D\u200D\u200D\u206B\u206B\u206F\u200B\u200E\u206A\u202C\u206A\u202C\u200D\u200D\u202D\u200D\u206E\u202B\u202B\u206F\u206B\u206F\u202C\u206C\u206B\u202C\u206C\u202D\u200D\u206A\u200E\u202B\u202D\u202E;

		// Token: 0x040003FF RID: 1023
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private SettlementCombatHandler.SettlementCombatType \u206D\u202C\u206A\u206D\u202A\u200B\u202E\u206C\u202C\u206A\u206E\u200E\u202E\u202B\u206B\u206A\u200F\u202C\u202C\u206C\u206B\u206E\u200C\u200B\u206F\u200D\u206F\u202A\u206D\u206B\u206B\u200B\u200E\u202E\u202E\u206D\u202D\u200F\u206F\u202D\u202E;

		// Token: 0x04000400 RID: 1024
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u206A\u202B\u202E\u200C\u202D\u206B\u200D\u206B\u202B\u202B\u206C\u206A\u202A\u206B\u206E\u206C\u202A\u200C\u202B\u200C\u206B\u200B\u200C\u206F\u206B\u206A\u206B\u200C\u202E\u202C\u202C\u200E\u206F\u202B\u206C\u202C\u200B\u200C\u202D\u206B\u202E;

		// Token: 0x04000401 RID: 1025
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200E\u206A\u200D\u206D\u200D\u200D\u206D\u206C\u200C\u206E\u206C\u202D\u206D\u206F\u206A\u202B\u200D\u200C\u206D\u206E\u206D\u200F\u200B\u200D\u206D\u202B\u202E\u206F\u200D\u200C\u202B\u200C\u206A\u200F\u206A\u202D\u206A\u206E\u206A\u202D\u202E;
	}
}

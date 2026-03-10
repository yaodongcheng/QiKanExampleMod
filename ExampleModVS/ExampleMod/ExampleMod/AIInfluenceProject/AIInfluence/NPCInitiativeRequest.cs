using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;

namespace AIInfluence
{
	// Token: 0x02000090 RID: 144
	public class NPCInitiativeRequest
	{
		// Token: 0x17000158 RID: 344
		// (get) Token: 0x06000534 RID: 1332 RVA: 0x00066184 File Offset: 0x00064384
		// (set) Token: 0x06000535 RID: 1333 RVA: 0x00066198 File Offset: 0x00064398
		public Hero NPC { get; set; }

		// Token: 0x17000159 RID: 345
		// (get) Token: 0x06000536 RID: 1334 RVA: 0x000661AC File Offset: 0x000643AC
		// (set) Token: 0x06000537 RID: 1335 RVA: 0x000661C0 File Offset: 0x000643C0
		public NPCContext Context { get; set; }

		// Token: 0x1700015A RID: 346
		// (get) Token: 0x06000538 RID: 1336 RVA: 0x000661D4 File Offset: 0x000643D4
		// (set) Token: 0x06000539 RID: 1337 RVA: 0x000661E8 File Offset: 0x000643E8
		public bool IsInParty { get; set; }

		// Token: 0x1700015B RID: 347
		// (get) Token: 0x0600053A RID: 1338 RVA: 0x000661FC File Offset: 0x000643FC
		// (set) Token: 0x0600053B RID: 1339 RVA: 0x00066210 File Offset: 0x00064410
		public CampaignTime CreatedTime { get; set; }

		// Token: 0x04000352 RID: 850
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Hero \u206A\u202E\u206C\u206D\u200D\u206D\u206D\u200B\u206B\u206C\u200F\u200C\u206F\u206E\u202D\u206E\u202C\u206A\u202D\u206C\u200D\u206F\u200C\u200E\u206C\u200F\u200C\u200E\u206C\u200E\u202C\u200E\u206D\u206B\u206F\u206E\u206A\u200D\u200B\u200D\u202E;

		// Token: 0x04000353 RID: 851
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private NPCContext \u202B\u202E\u206F\u206D\u200B\u202B\u202E\u200E\u206D\u202B\u202D\u206F\u200D\u200D\u206D\u202A\u202D\u206E\u206D\u202D\u202C\u200B\u206E\u202A\u206A\u202D\u202C\u206B\u202A\u202D\u200C\u202B\u202A\u200B\u200E\u206A\u200C\u202C\u202E\u202D\u202E;

		// Token: 0x04000354 RID: 852
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206B\u206E\u202D\u200D\u206B\u202E\u200D\u200C\u202C\u206C\u200E\u202E\u206A\u206F\u200E\u200D\u206C\u206C\u206F\u200B\u206A\u200D\u206C\u206E\u200B\u206C\u206C\u200D\u200C\u200D\u200C\u200D\u200B\u200B\u206B\u202D\u200D\u206F\u202D\u200C\u202E;

		// Token: 0x04000355 RID: 853
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u200C\u206E\u206C\u202E\u206F\u206D\u206F\u202B\u200C\u202D\u206F\u206F\u202E\u202A\u202D\u202D\u200E\u200C\u206D\u200E\u206A\u206D\u200E\u200E\u206A\u202E\u206C\u206D\u206F\u206A\u202B\u206C\u202B\u202A\u202E\u206D\u202D\u200F\u200D\u206C\u202E;
	}
}

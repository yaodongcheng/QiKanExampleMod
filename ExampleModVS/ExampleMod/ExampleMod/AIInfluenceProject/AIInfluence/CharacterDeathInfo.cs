using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x02000078 RID: 120
	[JsonSerializable]
	public class CharacterDeathInfo
	{
		// Token: 0x17000141 RID: 321
		// (get) Token: 0x060004A5 RID: 1189 RVA: 0x0005C6E0 File Offset: 0x0005A8E0
		// (set) Token: 0x060004A6 RID: 1190 RVA: 0x0005C6F4 File Offset: 0x0005A8F4
		[JsonProperty("should_die")]
		public bool ShouldDie { get; set; }

		// Token: 0x17000142 RID: 322
		// (get) Token: 0x060004A7 RID: 1191 RVA: 0x0005C708 File Offset: 0x0005A908
		// (set) Token: 0x060004A8 RID: 1192 RVA: 0x0005C71C File Offset: 0x0005A91C
		[JsonProperty("death_reason")]
		public string DeathReason { get; set; }

		// Token: 0x17000143 RID: 323
		// (get) Token: 0x060004A9 RID: 1193 RVA: 0x0005C730 File Offset: 0x0005A930
		// (set) Token: 0x060004AA RID: 1194 RVA: 0x0005C744 File Offset: 0x0005A944
		[JsonProperty("killer_string_id")]
		public string KillerStringId { get; set; }

		// Token: 0x040002BA RID: 698
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206A\u206B\u206F\u206C\u206F\u206A\u202A\u200F\u202D\u202D\u200E\u206F\u202E\u206D\u206A\u200E\u202C\u200F\u206B\u202D\u206E\u200E\u202A\u206A\u200E\u206E\u206D\u206C\u200C\u202E\u206B\u200B\u202A\u202A\u202E\u206B\u200B\u206D\u206E\u206D\u202E;

		// Token: 0x040002BB RID: 699
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202E\u206C\u206B\u200D\u202B\u202B\u202A\u206B\u206D\u200B\u202A\u202A\u202E\u206F\u206F\u202C\u200C\u200F\u202B\u206A\u200F\u206D\u200B\u200D\u206D\u202E\u206E\u202D\u206F\u202B\u206F\u206E\u200D\u206B\u206F\u200F\u200B\u200D\u206E\u206A\u202E;

		// Token: 0x040002BC RID: 700
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u200D\u206C\u202B\u206B\u202E\u202C\u200C\u206A\u202A\u200F\u202C\u200C\u206D\u202A\u202B\u206C\u206C\u206C\u202B\u202E\u206D\u200F\u206F\u202E\u206B\u202D\u202A\u206F\u206A\u206E\u200E\u202C\u202E\u200E\u206E\u206B\u206C\u206E\u202E\u202E;
	}
}

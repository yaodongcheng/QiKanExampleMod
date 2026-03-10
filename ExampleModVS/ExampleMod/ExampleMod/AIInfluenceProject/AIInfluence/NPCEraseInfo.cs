using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;

namespace AIInfluence
{
	// Token: 0x02000017 RID: 23
	public class NPCEraseInfo
	{
		// Token: 0x17000011 RID: 17
		// (get) Token: 0x06000083 RID: 131 RVA: 0x0002BD04 File Offset: 0x00029F04
		// (set) Token: 0x06000084 RID: 132 RVA: 0x0002BD18 File Offset: 0x00029F18
		public string StringId { get; set; }

		// Token: 0x17000012 RID: 18
		// (get) Token: 0x06000085 RID: 133 RVA: 0x0002BD2C File Offset: 0x00029F2C
		// (set) Token: 0x06000086 RID: 134 RVA: 0x0002BD40 File Offset: 0x00029F40
		public string Name { get; set; }

		// Token: 0x17000013 RID: 19
		// (get) Token: 0x06000087 RID: 135 RVA: 0x0002BD54 File Offset: 0x00029F54
		// (set) Token: 0x06000088 RID: 136 RVA: 0x0002BD68 File Offset: 0x00029F68
		public string FilePath { get; set; }

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x06000089 RID: 137 RVA: 0x0002BD7C File Offset: 0x00029F7C
		// (set) Token: 0x0600008A RID: 138 RVA: 0x0002BD90 File Offset: 0x00029F90
		public Hero Hero { get; set; }

		// Token: 0x17000015 RID: 21
		// (get) Token: 0x0600008B RID: 139 RVA: 0x0002BDA4 File Offset: 0x00029FA4
		// (set) Token: 0x0600008C RID: 140 RVA: 0x0002BDB8 File Offset: 0x00029FB8
		public NPCContext Context { get; set; }

		// Token: 0x17000016 RID: 22
		// (get) Token: 0x0600008D RID: 141 RVA: 0x0002BDCC File Offset: 0x00029FCC
		// (set) Token: 0x0600008E RID: 142 RVA: 0x0002BDE0 File Offset: 0x00029FE0
		public int InteractionCount { get; set; }

		// Token: 0x17000017 RID: 23
		// (get) Token: 0x0600008F RID: 143 RVA: 0x0002BDF4 File Offset: 0x00029FF4
		// (set) Token: 0x06000090 RID: 144 RVA: 0x0002BE08 File Offset: 0x0002A008
		public bool HasConversationHistory { get; set; }

		// Token: 0x17000018 RID: 24
		// (get) Token: 0x06000091 RID: 145 RVA: 0x0002BE1C File Offset: 0x0002A01C
		// (set) Token: 0x06000092 RID: 146 RVA: 0x0002BE30 File Offset: 0x0002A030
		public bool HasKnownSecrets { get; set; }

		// Token: 0x17000019 RID: 25
		// (get) Token: 0x06000093 RID: 147 RVA: 0x0002BE44 File Offset: 0x0002A044
		// (set) Token: 0x06000094 RID: 148 RVA: 0x0002BE58 File Offset: 0x0002A058
		public bool HasKnownInfo { get; set; }

		// Token: 0x1700001A RID: 26
		// (get) Token: 0x06000095 RID: 149 RVA: 0x0002BE6C File Offset: 0x0002A06C
		// (set) Token: 0x06000096 RID: 150 RVA: 0x0002BE80 File Offset: 0x0002A080
		public bool HasEvents { get; set; }

		// Token: 0x0400003F RID: 63
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u200F\u200F\u200D\u200C\u202C\u202A\u200F\u202A\u202A\u202E\u206E\u202A\u200B\u202A\u206C\u202E\u202D\u202A\u200B\u206E\u200E\u202E\u202A\u202E\u206B\u206F\u206D\u202E\u206A\u200B\u206C\u206E\u202A\u202A\u202D\u202C\u200D\u202C\u202D\u202E;

		// Token: 0x04000040 RID: 64
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u206C\u200D\u202C\u206F\u202B\u202E\u200E\u206C\u200B\u200F\u200E\u206A\u206C\u200F\u202C\u202C\u202C\u200F\u200C\u206E\u206D\u206D\u206C\u202B\u200E\u202E\u202E\u200C\u202B\u202A\u202D\u206C\u202E\u200B\u202A\u200B\u206A\u206A\u202E;

		// Token: 0x04000041 RID: 65
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202D\u200B\u206F\u200D\u202A\u206A\u202E\u206D\u206D\u206F\u202E\u206E\u200B\u206F\u200F\u206E\u200F\u206B\u206B\u202E\u202C\u202B\u200C\u200C\u206E\u206E\u206C\u202B\u206B\u206E\u206C\u206E\u206B\u202C\u202B\u200C\u202D\u200D\u200C\u202E;

		// Token: 0x04000042 RID: 66
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Hero \u206E\u200B\u206F\u206C\u200C\u206E\u200C\u202B\u202B\u200B\u200D\u200B\u202B\u200E\u202A\u200E\u206E\u200E\u200B\u202C\u200C\u202B\u202C\u202D\u202D\u200F\u200D\u202D\u206D\u206D\u206F\u206C\u206D\u202D\u200F\u206C\u206F\u200E\u200C\u206A\u202E;

		// Token: 0x04000043 RID: 67
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private NPCContext \u206B\u202D\u206A\u206D\u202E\u200C\u202C\u202E\u206B\u206E\u206B\u206E\u206A\u200F\u206A\u206E\u202C\u202E\u202C\u200C\u202B\u202E\u202A\u202C\u202E\u206D\u202B\u200C\u200F\u206C\u202A\u200B\u202B\u206B\u206C\u202C\u202E\u200C\u206A\u202E;

		// Token: 0x04000044 RID: 68
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202E\u200E\u200C\u206D\u206D\u202A\u206C\u200F\u200F\u206B\u206D\u206B\u206F\u202C\u206F\u200C\u206C\u200E\u202C\u202E\u200B\u200C\u200C\u202A\u206E\u202C\u202E\u200B\u202B\u206E\u206A\u206F\u206A\u206A\u202C\u202E\u206C\u200F\u200E\u200F\u202E;

		// Token: 0x04000045 RID: 69
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202D\u202B\u200F\u200D\u202E\u206E\u202C\u206A\u200B\u206D\u206C\u200D\u202D\u206F\u202E\u206B\u206F\u206E\u200E\u202E\u202E\u206F\u200B\u202D\u206A\u202A\u206F\u202E\u200F\u206F\u202B\u202E\u200D\u206F\u206B\u202C\u200B\u206D\u206B\u200E\u202E;

		// Token: 0x04000046 RID: 70
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200B\u206D\u206D\u202A\u202E\u206B\u206F\u206B\u202D\u200B\u200B\u202C\u206D\u206D\u202C\u206A\u206D\u200B\u206C\u200E\u200D\u202D\u200E\u200B\u202E\u200D\u206C\u202D\u202C\u206E\u200C\u200B\u206C\u206F\u202B\u206C\u206D\u200D\u202D\u202E;

		// Token: 0x04000047 RID: 71
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206B\u206A\u202C\u202A\u202B\u202C\u202D\u202D\u200B\u202D\u202A\u200B\u206E\u202D\u202C\u206E\u202C\u206C\u200F\u206A\u202D\u200C\u206A\u202D\u200E\u202B\u200C\u200F\u206F\u206E\u206D\u206C\u206A\u200D\u202A\u200C\u200D\u202A\u200E\u200D\u202E;

		// Token: 0x04000048 RID: 72
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206B\u206E\u200B\u202A\u200F\u206C\u206C\u202A\u206E\u206D\u202C\u206C\u206F\u206E\u206D\u200B\u202D\u202A\u202A\u206C\u200C\u206E\u206B\u202D\u206B\u206A\u202D\u202A\u200B\u206D\u206D\u206F\u206E\u200F\u200B\u206B\u206F\u200D\u206D\u200C\u202E;
	}
}

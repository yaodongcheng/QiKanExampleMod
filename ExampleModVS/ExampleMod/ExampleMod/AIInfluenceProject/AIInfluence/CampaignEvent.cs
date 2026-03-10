using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x02000071 RID: 113
	[JsonSerializable]
	public class CampaignEvent
	{
		// Token: 0x17000112 RID: 274
		// (get) Token: 0x0600043D RID: 1085 RVA: 0x0005BDB8 File Offset: 0x00059FB8
		// (set) Token: 0x0600043E RID: 1086 RVA: 0x0005BDCC File Offset: 0x00059FCC
		public string Type { get; set; }

		// Token: 0x17000113 RID: 275
		// (get) Token: 0x0600043F RID: 1087 RVA: 0x0005BDE0 File Offset: 0x00059FE0
		// (set) Token: 0x06000440 RID: 1088 RVA: 0x0005BDF4 File Offset: 0x00059FF4
		public string Description { get; set; }

		// Token: 0x17000114 RID: 276
		// (get) Token: 0x06000441 RID: 1089 RVA: 0x0005BE08 File Offset: 0x0005A008
		// (set) Token: 0x06000442 RID: 1090 RVA: 0x0005BE1C File Offset: 0x0005A01C
		public CampaignTime Timestamp { get; set; }

		// Token: 0x17000115 RID: 277
		// (get) Token: 0x06000443 RID: 1091 RVA: 0x0005BE30 File Offset: 0x0005A030
		// (set) Token: 0x06000444 RID: 1092 RVA: 0x0005BE98 File Offset: 0x0005A098
		[JsonProperty("EventTimeDays")]
		public double EventTimeDays
		{
			get
			{
				double result;
				try
				{
					double num = \u206F\u206C\u206F\u202C\u206A\u206A\u200F\u200F\u202E\u206B\u206C\u200E\u202D\u200C\u206A\u206A\u206D\u206C\u200D\u202A\u200C\u200F\u200E\u200D\u202B\u202C\u206F\u206C\u206E\u206F\u200F\u202E\u200B\u200E\u206F\u202C\u200F\u200C\u202B\u200E\u202E.\u000Ai\u0099ëÕ(this.Timestamp, \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.ÂPèÍn()) ? this.Timestamp.ToDays : 0.0;
					result = num;
				}
				catch
				{
					result = 0.0;
				}
				return result;
			}
			set
			{
				bool flag = value > 0.0;
				if (flag)
				{
					goto IL_14;
				}
				goto IL_99;
				int num2;
				for (;;)
				{
					IL_19:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(num - (~1720567000 + 1337521919 ^ ~(-1496411703)) ^ (-231413997 + 2102642270) * -1048483837)) % 4U)
					{
					case 0U:
						goto IL_14;
					case 1U:
						goto IL_99;
					case 3U:
					{
						double num4 = \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays - value;
						this.Timestamp = \u200B\u202B\u202D\u202C\u206F\u206D\u200F\u206E\u200E\u206B\u202B\u206B\u200B\u206A\u206D\u206D\u200E\u202D\u206A\u206F\u202E\u206F\u206A\u202C\u206D\u202C\u200D\u206A\u200B\u202E\u206A\u202A\u206B\u202E\u206F\u206C\u206E\u200F\u202A\u200D\u202E.àz>\u00F7\u0081(-(float)num4);
						num2 = (int)(num3 * 669740084U ^ 266552009U);
						continue;
					}
					}
					break;
				}
				return;
				IL_14:
				num2 = -914938428;
				goto IL_19;
				IL_99:
				this.Timestamp = \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
				num2 = 416642085;
				goto IL_19;
			}
		}

		// Token: 0x06000445 RID: 1093 RVA: 0x00046720 File Offset: 0x00044920
		public bool ShouldSerializeTimestamp()
		{
			return false;
		}

		// Token: 0x0400028C RID: 652
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u202A\u202E\u206E\u200F\u206D\u200C\u206D\u206A\u202E\u200F\u200F\u202D\u206B\u202A\u202C\u206D\u206F\u206D\u202D\u200D\u206B\u206C\u200B\u200C\u206C\u206D\u206E\u200C\u200D\u200C\u200E\u206C\u200D\u200E\u206F\u202D\u206E\u206A\u202E;

		// Token: 0x0400028D RID: 653
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u202D\u206F\u206C\u206F\u202C\u200C\u202C\u200B\u202E\u202E\u202C\u206E\u200B\u202A\u202D\u202B\u200D\u202B\u200C\u206A\u200C\u200E\u206C\u206A\u206F\u206D\u202E\u206E\u206C\u206A\u206D\u202A\u200B\u206E\u202C\u206E\u202E\u206E\u206C\u202E;

		// Token: 0x0400028E RID: 654
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CampaignTime \u206E\u206B\u200B\u206C\u202E\u206A\u202B\u200E\u206A\u206B\u206C\u206B\u202D\u206E\u202A\u206D\u200D\u202D\u206E\u200B\u202E\u206C\u202A\u206E\u202D\u206F\u206D\u206E\u206A\u206A\u200F\u200D\u202C\u206E\u206C\u206C\u200C\u202B\u206F\u200F\u202E;
	}
}

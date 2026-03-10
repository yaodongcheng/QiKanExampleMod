using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x02000072 RID: 114
	[JsonSerializable]
	public class SimpleCampaignTime
	{
		// Token: 0x17000116 RID: 278
		// (get) Token: 0x06000447 RID: 1095 RVA: 0x0005BF5C File Offset: 0x0005A15C
		// (set) Token: 0x06000448 RID: 1096 RVA: 0x0005BF70 File Offset: 0x0005A170
		[JsonProperty("Year")]
		public int Year { get; set; }

		// Token: 0x17000117 RID: 279
		// (get) Token: 0x06000449 RID: 1097 RVA: 0x0005BF84 File Offset: 0x0005A184
		// (set) Token: 0x0600044A RID: 1098 RVA: 0x0005BF98 File Offset: 0x0005A198
		[JsonProperty("DayOfYear")]
		public int DayOfYear { get; set; }

		// Token: 0x17000118 RID: 280
		// (get) Token: 0x0600044B RID: 1099 RVA: 0x0005BFAC File Offset: 0x0005A1AC
		// (set) Token: 0x0600044C RID: 1100 RVA: 0x0005BFC0 File Offset: 0x0005A1C0
		[JsonProperty("Hour")]
		public int Hour { get; set; }

		// Token: 0x17000119 RID: 281
		// (get) Token: 0x0600044D RID: 1101 RVA: 0x0005BFD4 File Offset: 0x0005A1D4
		// (set) Token: 0x0600044E RID: 1102 RVA: 0x0005BFE8 File Offset: 0x0005A1E8
		[JsonProperty("TotalDays")]
		public double TotalDays { get; set; }

		// Token: 0x0600044F RID: 1103 RVA: 0x0005BFFC File Offset: 0x0005A1FC
		public SimpleCampaignTime()
		{
		}

		// Token: 0x06000450 RID: 1104 RVA: 0x0005C014 File Offset: 0x0005A214
		public SimpleCampaignTime(CampaignTime A_1)
		{
			try
			{
				bool flag = \u206F\u206C\u206F\u202C\u206A\u206A\u200F\u200F\u202E\u206B\u206C\u200E\u202D\u200C\u206A\u206A\u206D\u206C\u200D\u202A\u200C\u200F\u200E\u200D\u202B\u202C\u206F\u206C\u206E\u206F\u200F\u202E\u200B\u200E\u206F\u202C\u200F\u200C\u202B\u200E\u202E.\u000Ai\u0099ëÕ(A_1, \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.ÂPèÍn());
				if (flag)
				{
					this.Year = A_1.GetYear;
					this.DayOfYear = A_1.GetDayOfYear;
					this.Hour = A_1.GetHourOfDay;
					this.TotalDays = A_1.ToDays;
				}
			}
			catch
			{
			}
		}

		// Token: 0x06000451 RID: 1105 RVA: 0x0005C09C File Offset: 0x0005A29C
		public double GetDaysFromNow()
		{
			return \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays - this.TotalDays;
		}

		// Token: 0x0400028F RID: 655
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200D\u206C\u202B\u206D\u206E\u200F\u202E\u200C\u206D\u206A\u206F\u202C\u202B\u206F\u200E\u202A\u202A\u202D\u202A\u206D\u200C\u206C\u206D\u200C\u202D\u202E\u206E\u206B\u206A\u206B\u202D\u200F\u202E\u206C\u202E\u206C\u200C\u206D\u202D\u202B\u202E;

		// Token: 0x04000290 RID: 656
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200D\u206D\u200F\u202E\u202A\u200C\u202B\u206B\u200F\u202C\u206A\u206F\u202C\u206F\u202E\u200D\u200C\u200F\u200D\u200D\u200C\u202D\u202E\u206A\u206B\u202C\u206E\u202E\u202C\u206F\u202C\u202D\u202E\u206F\u202E\u202B\u206C\u206F\u202D\u200E\u202E;

		// Token: 0x04000291 RID: 657
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200F\u202B\u202D\u202A\u206B\u200F\u202D\u202E\u202A\u206C\u206D\u206B\u200B\u202B\u206D\u206D\u200E\u200D\u200E\u206D\u200B\u206D\u206F\u200B\u202C\u200F\u200F\u206C\u200D\u200B\u206A\u200E\u202B\u206C\u202C\u206B\u206A\u202A\u202A\u202E\u202E;

		// Token: 0x04000292 RID: 658
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private double \u200E\u206F\u200C\u202A\u206F\u202A\u202A\u206C\u206C\u202C\u200B\u202D\u200E\u202C\u202D\u206B\u202D\u206D\u206A\u200D\u200B\u202B\u202D\u202E\u206E\u200C\u200D\u200E\u200F\u200E\u202B\u202D\u206F\u200C\u202E\u206F\u206C\u206A\u200E\u202E;
	}
}

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x02000169 RID: 361
	[JsonSerializable]
	public class EventUpdate
	{
		// Token: 0x1700026F RID: 623
		// (get) Token: 0x06000BDC RID: 3036 RVA: 0x000C98A8 File Offset: 0x000C7AA8
		// (set) Token: 0x06000BDD RID: 3037 RVA: 0x000C98BC File Offset: 0x000C7ABC
		[JsonProperty("campaign_days")]
		public float CampaignDays { get; set; }

		// Token: 0x17000270 RID: 624
		// (get) Token: 0x06000BDE RID: 3038 RVA: 0x000C98D0 File Offset: 0x000C7AD0
		// (set) Token: 0x06000BDF RID: 3039 RVA: 0x000C98E4 File Offset: 0x000C7AE4
		[JsonProperty("description")]
		public string Description { get; set; }

		// Token: 0x17000271 RID: 625
		// (get) Token: 0x06000BE0 RID: 3040 RVA: 0x000C98F8 File Offset: 0x000C7AF8
		// (set) Token: 0x06000BE1 RID: 3041 RVA: 0x000C990C File Offset: 0x000C7B0C
		[JsonProperty("update_reason")]
		public string UpdateReason { get; set; }

		// Token: 0x17000272 RID: 626
		// (get) Token: 0x06000BE2 RID: 3042 RVA: 0x000C9920 File Offset: 0x000C7B20
		// (set) Token: 0x06000BE3 RID: 3043 RVA: 0x000C9934 File Offset: 0x000C7B34
		[JsonProperty("days_since_creation")]
		public int DaysSinceCreation { get; set; }

		// Token: 0x17000273 RID: 627
		// (get) Token: 0x06000BE4 RID: 3044 RVA: 0x000C9948 File Offset: 0x000C7B48
		// (set) Token: 0x06000BE5 RID: 3045 RVA: 0x000C997C File Offset: 0x000C7B7C
		[JsonIgnore]
		public CampaignTime Timestamp
		{
			get
			{
				return \u200B\u202B\u202D\u202C\u206F\u206D\u200F\u206E\u200E\u206B\u202B\u206B\u200B\u206A\u206D\u206D\u200E\u202D\u206A\u206F\u202E\u206F\u206A\u202C\u206D\u202C\u200D\u206A\u200B\u202E\u206A\u202A\u206B\u202E\u206F\u206C\u206E\u200F\u202A\u200D\u202E.àz>\u00F7\u0081(this.CampaignDays - (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays);
			}
			set
			{
				this.CampaignDays = (float)value.ToDays;
			}
		}

		// Token: 0x06000BE6 RID: 3046 RVA: 0x000C9998 File Offset: 0x000C7B98
		[MethodImpl(MethodImplOptions.NoInlining)]
		public EventUpdate()
		{
			this.CampaignDays = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
			this.UpdateReason = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1492381408);
		}

		// Token: 0x06000BE7 RID: 3047 RVA: 0x000C99DC File Offset: 0x000C7BDC
		public EventUpdate(string A_1, string A_2 = "Event Update")
		{
			this.CampaignDays = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
			this.Description = A_1;
			this.UpdateReason = A_2;
		}

		// Token: 0x04000745 RID: 1861
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206D\u200E\u200D\u206B\u206F\u206B\u206A\u206C\u200E\u200B\u206D\u202B\u200C\u206A\u202E\u200F\u200D\u206B\u200F\u200E\u206C\u202E\u202A\u202A\u206F\u200F\u206B\u202C\u202C\u200C\u206D\u202D\u202D\u200C\u200F\u202E\u206D\u200D\u206A\u200E\u202E;

		// Token: 0x04000746 RID: 1862
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u202E\u202B\u200D\u206D\u202B\u206F\u202E\u202C\u202D\u200F\u206F\u202A\u200F\u202A\u200F\u206D\u206B\u206D\u206E\u206B\u206C\u200C\u206C\u206A\u202B\u200F\u206A\u202B\u206C\u206D\u206B\u206C\u202B\u202A\u200C\u206F\u206D\u206B\u200E\u202E;

		// Token: 0x04000747 RID: 1863
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u206D\u206D\u200C\u206C\u206F\u202E\u200D\u206C\u200E\u206A\u200F\u200B\u200C\u202A\u206A\u206F\u206C\u200B\u206F\u206C\u202D\u206B\u202C\u206E\u206D\u200B\u202A\u206F\u206F\u200B\u202A\u200D\u200C\u202D\u200B\u202E\u202A\u206C\u202C\u202E;

		// Token: 0x04000748 RID: 1864
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202A\u206F\u202E\u202E\u200C\u206E\u200C\u200C\u206C\u202D\u202C\u202E\u206E\u206C\u200D\u202A\u206D\u206E\u206D\u202E\u202D\u206D\u200B\u202D\u202C\u202A\u200E\u206E\u202D\u200E\u200D\u202D\u200E\u200B\u206D\u200E\u206D\u206E\u206E\u202E;
	}
}

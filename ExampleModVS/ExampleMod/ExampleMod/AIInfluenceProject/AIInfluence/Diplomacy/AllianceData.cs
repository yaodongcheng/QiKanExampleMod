using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x0200020C RID: 524
	public class AllianceData
	{
		// Token: 0x170002F9 RID: 761
		// (get) Token: 0x06000FE0 RID: 4064 RVA: 0x0001E8C0 File Offset: 0x0001CAC0
		// (set) Token: 0x06000FE1 RID: 4065 RVA: 0x0001E8C8 File Offset: 0x0001CAC8
		[JsonProperty("alliances")]
		public Dictionary<string, List<string>> Alliances { get; set; }

		// Token: 0x170002FA RID: 762
		// (get) Token: 0x06000FE2 RID: 4066 RVA: 0x0001E8D1 File Offset: 0x0001CAD1
		// (set) Token: 0x06000FE3 RID: 4067 RVA: 0x0001E8D9 File Offset: 0x0001CAD9
		[JsonProperty("alliance_times")]
		public Dictionary<string, CampaignTime> AllianceTimes { get; set; }

		// Token: 0x170002FB RID: 763
		// (get) Token: 0x06000FE4 RID: 4068 RVA: 0x0001E8E2 File Offset: 0x0001CAE2
		// (set) Token: 0x06000FE5 RID: 4069 RVA: 0x0001E8EA File Offset: 0x0001CAEA
		[JsonProperty("save_time")]
		public DateTime SaveTime { get; set; }

		// Token: 0x170002FC RID: 764
		// (get) Token: 0x06000FE6 RID: 4070 RVA: 0x0001E8F3 File Offset: 0x0001CAF3
		// (set) Token: 0x06000FE7 RID: 4071 RVA: 0x0001E8FB File Offset: 0x0001CAFB
		[JsonProperty("campaign_days")]
		public float CampaignDays { get; set; }
	}
}

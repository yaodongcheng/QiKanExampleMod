using System;
using System.Collections.Generic;
using AIInfluence.DynamicEvents;
using Newtonsoft.Json;

namespace AIInfluence.Diplomacy
{
	// Token: 0x0200020E RID: 526
	public class DiplomaticEventsData
	{
		// Token: 0x17000300 RID: 768
		// (get) Token: 0x06000FF0 RID: 4080 RVA: 0x0001E937 File Offset: 0x0001CB37
		// (set) Token: 0x06000FF1 RID: 4081 RVA: 0x0001E93F File Offset: 0x0001CB3F
		[JsonProperty("diplomatic_events")]
		public List<DynamicEvent> DiplomaticEvents { get; set; }

		// Token: 0x17000301 RID: 769
		// (get) Token: 0x06000FF2 RID: 4082 RVA: 0x0001E948 File Offset: 0x0001CB48
		// (set) Token: 0x06000FF3 RID: 4083 RVA: 0x0001E950 File Offset: 0x0001CB50
		[JsonProperty("save_time")]
		public DateTime SaveTime { get; set; }

		// Token: 0x17000302 RID: 770
		// (get) Token: 0x06000FF4 RID: 4084 RVA: 0x0001E959 File Offset: 0x0001CB59
		// (set) Token: 0x06000FF5 RID: 4085 RVA: 0x0001E961 File Offset: 0x0001CB61
		[JsonProperty("campaign_days")]
		public float CampaignDays { get; set; }
	}
}

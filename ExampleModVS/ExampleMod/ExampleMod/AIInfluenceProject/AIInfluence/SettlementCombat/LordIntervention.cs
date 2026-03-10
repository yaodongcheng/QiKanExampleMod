using System;
using Newtonsoft.Json;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000151 RID: 337
	[JsonSerializable]
	public class LordIntervention
	{
		// Token: 0x17000211 RID: 529
		// (get) Token: 0x06000AB3 RID: 2739 RVA: 0x0001DCD5 File Offset: 0x0001BED5
		// (set) Token: 0x06000AB4 RID: 2740 RVA: 0x0001DCDD File Offset: 0x0001BEDD
		[JsonProperty("string_id")]
		public string StringId { get; set; }

		// Token: 0x17000212 RID: 530
		// (get) Token: 0x06000AB5 RID: 2741 RVA: 0x0001DCE6 File Offset: 0x0001BEE6
		// (set) Token: 0x06000AB6 RID: 2742 RVA: 0x0001DCEE File Offset: 0x0001BEEE
		[JsonProperty("side")]
		public string Side { get; set; }

		// Token: 0x17000213 RID: 531
		// (get) Token: 0x06000AB7 RID: 2743 RVA: 0x0001DCF7 File Offset: 0x0001BEF7
		// (set) Token: 0x06000AB8 RID: 2744 RVA: 0x0001DCFF File Offset: 0x0001BEFF
		[JsonProperty("intervention_reason")]
		public string InterventionReason { get; set; }

		// Token: 0x17000214 RID: 532
		// (get) Token: 0x06000AB9 RID: 2745 RVA: 0x0001DD08 File Offset: 0x0001BF08
		// (set) Token: 0x06000ABA RID: 2746 RVA: 0x0001DD10 File Offset: 0x0001BF10
		[JsonProperty("arrival_phrase")]
		public string ArrivalPhrase { get; set; }
	}
}

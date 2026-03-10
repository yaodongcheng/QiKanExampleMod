using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002B6 RID: 694
	public class OllamaOptions
	{
		// Token: 0x170003C8 RID: 968
		// (get) Token: 0x060013BA RID: 5050 RVA: 0x000202B7 File Offset: 0x0001E4B7
		// (set) Token: 0x060013BB RID: 5051 RVA: 0x000202BF File Offset: 0x0001E4BF
		[JsonProperty("num_predict")]
		public int? NumPredict { get; set; }

		// Token: 0x170003C9 RID: 969
		// (get) Token: 0x060013BC RID: 5052 RVA: 0x000202C8 File Offset: 0x0001E4C8
		// (set) Token: 0x060013BD RID: 5053 RVA: 0x000202D0 File Offset: 0x0001E4D0
		[JsonProperty("temperature")]
		public double? Temperature { get; set; }

		// Token: 0x170003CA RID: 970
		// (get) Token: 0x060013BE RID: 5054 RVA: 0x000202D9 File Offset: 0x0001E4D9
		// (set) Token: 0x060013BF RID: 5055 RVA: 0x000202E1 File Offset: 0x0001E4E1
		[JsonProperty("top_p")]
		public double? TopP { get; set; }
	}
}

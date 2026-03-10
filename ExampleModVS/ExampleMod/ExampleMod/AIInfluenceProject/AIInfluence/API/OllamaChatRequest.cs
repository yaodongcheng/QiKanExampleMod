using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002B4 RID: 692
	public class OllamaChatRequest
	{
		// Token: 0x170003C2 RID: 962
		// (get) Token: 0x060013AC RID: 5036 RVA: 0x00020251 File Offset: 0x0001E451
		// (set) Token: 0x060013AD RID: 5037 RVA: 0x00020259 File Offset: 0x0001E459
		[JsonProperty("model")]
		public string Model { get; set; }

		// Token: 0x170003C3 RID: 963
		// (get) Token: 0x060013AE RID: 5038 RVA: 0x00020262 File Offset: 0x0001E462
		// (set) Token: 0x060013AF RID: 5039 RVA: 0x0002026A File Offset: 0x0001E46A
		[JsonProperty("messages")]
		public List<OllamaMessage> Messages { get; set; }

		// Token: 0x170003C4 RID: 964
		// (get) Token: 0x060013B0 RID: 5040 RVA: 0x00020273 File Offset: 0x0001E473
		// (set) Token: 0x060013B1 RID: 5041 RVA: 0x0002027B File Offset: 0x0001E47B
		[JsonProperty("stream")]
		public bool Stream { get; set; }

		// Token: 0x170003C5 RID: 965
		// (get) Token: 0x060013B2 RID: 5042 RVA: 0x00020284 File Offset: 0x0001E484
		// (set) Token: 0x060013B3 RID: 5043 RVA: 0x0002028C File Offset: 0x0001E48C
		[JsonProperty("options")]
		public OllamaOptions Options { get; set; }
	}
}

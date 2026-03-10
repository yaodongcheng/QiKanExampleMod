using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002B3 RID: 691
	public class OllamaGenerateRequest
	{
		// Token: 0x170003BE RID: 958
		// (get) Token: 0x060013A3 RID: 5027 RVA: 0x0002020D File Offset: 0x0001E40D
		// (set) Token: 0x060013A4 RID: 5028 RVA: 0x00020215 File Offset: 0x0001E415
		[JsonProperty("model")]
		public string Model { get; set; }

		// Token: 0x170003BF RID: 959
		// (get) Token: 0x060013A5 RID: 5029 RVA: 0x0002021E File Offset: 0x0001E41E
		// (set) Token: 0x060013A6 RID: 5030 RVA: 0x00020226 File Offset: 0x0001E426
		[JsonProperty("prompt")]
		public string Prompt { get; set; }

		// Token: 0x170003C0 RID: 960
		// (get) Token: 0x060013A7 RID: 5031 RVA: 0x0002022F File Offset: 0x0001E42F
		// (set) Token: 0x060013A8 RID: 5032 RVA: 0x00020237 File Offset: 0x0001E437
		[JsonProperty("stream")]
		public bool Stream { get; set; }

		// Token: 0x170003C1 RID: 961
		// (get) Token: 0x060013A9 RID: 5033 RVA: 0x00020240 File Offset: 0x0001E440
		// (set) Token: 0x060013AA RID: 5034 RVA: 0x00020248 File Offset: 0x0001E448
		[JsonProperty("options")]
		public OllamaOptions Options { get; set; }
	}
}

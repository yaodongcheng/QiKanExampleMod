using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002AF RID: 687
	public class KoboldCppGenerateRequest
	{
		// Token: 0x170003B7 RID: 951
		// (get) Token: 0x0600138B RID: 5003 RVA: 0x0002018A File Offset: 0x0001E38A
		// (set) Token: 0x0600138C RID: 5004 RVA: 0x00020192 File Offset: 0x0001E392
		[JsonProperty("prompt")]
		public string Prompt { get; set; }

		// Token: 0x170003B8 RID: 952
		// (get) Token: 0x0600138D RID: 5005 RVA: 0x0002019B File Offset: 0x0001E39B
		// (set) Token: 0x0600138E RID: 5006 RVA: 0x000201A3 File Offset: 0x0001E3A3
		[JsonProperty("max_length")]
		public int? MaxLength { get; set; }

		// Token: 0x170003B9 RID: 953
		// (get) Token: 0x0600138F RID: 5007 RVA: 0x000201AC File Offset: 0x0001E3AC
		// (set) Token: 0x06001390 RID: 5008 RVA: 0x000201B4 File Offset: 0x0001E3B4
		[JsonProperty("max_context_length")]
		public int? MaxContextLength { get; set; }

		// Token: 0x170003BA RID: 954
		// (get) Token: 0x06001391 RID: 5009 RVA: 0x000201BD File Offset: 0x0001E3BD
		// (set) Token: 0x06001392 RID: 5010 RVA: 0x000201C5 File Offset: 0x0001E3C5
		[JsonProperty("temperature")]
		public double? Temperature { get; set; }

		// Token: 0x170003BB RID: 955
		// (get) Token: 0x06001393 RID: 5011 RVA: 0x000201CE File Offset: 0x0001E3CE
		// (set) Token: 0x06001394 RID: 5012 RVA: 0x000201D6 File Offset: 0x0001E3D6
		[JsonProperty("top_p")]
		public double? TopP { get; set; }

		// Token: 0x170003BC RID: 956
		// (get) Token: 0x06001395 RID: 5013 RVA: 0x000201DF File Offset: 0x0001E3DF
		// (set) Token: 0x06001396 RID: 5014 RVA: 0x000201E7 File Offset: 0x0001E3E7
		[JsonProperty("stream")]
		public bool Stream { get; set; }

		// Token: 0x170003BD RID: 957
		// (get) Token: 0x06001397 RID: 5015 RVA: 0x000201F0 File Offset: 0x0001E3F0
		// (set) Token: 0x06001398 RID: 5016 RVA: 0x000201F8 File Offset: 0x0001E3F8
		[JsonProperty("stop_sequence")]
		public string[] StopSequence { get; set; }
	}
}

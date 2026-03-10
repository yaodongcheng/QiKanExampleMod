using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002A4 RID: 676
	public class KoboldCppResult
	{
		// Token: 0x170003AD RID: 941
		// (get) Token: 0x06001364 RID: 4964 RVA: 0x0002011B File Offset: 0x0001E31B
		// (set) Token: 0x06001365 RID: 4965 RVA: 0x00020123 File Offset: 0x0001E323
		[JsonProperty("text")]
		public string Text { get; set; } = "";

		// Token: 0x170003AE RID: 942
		// (get) Token: 0x06001366 RID: 4966 RVA: 0x0002012C File Offset: 0x0001E32C
		// (set) Token: 0x06001367 RID: 4967 RVA: 0x00020134 File Offset: 0x0001E334
		[JsonProperty("finish_reason")]
		public string FinishReason { get; set; } = "";

		// Token: 0x170003AF RID: 943
		// (get) Token: 0x06001368 RID: 4968 RVA: 0x0002013D File Offset: 0x0001E33D
		// (set) Token: 0x06001369 RID: 4969 RVA: 0x00020145 File Offset: 0x0001E345
		[JsonProperty("prompt_tokens")]
		public int PromptTokens { get; set; }

		// Token: 0x170003B0 RID: 944
		// (get) Token: 0x0600136A RID: 4970 RVA: 0x0002014E File Offset: 0x0001E34E
		// (set) Token: 0x0600136B RID: 4971 RVA: 0x00020156 File Offset: 0x0001E356
		[JsonProperty("completion_tokens")]
		public int CompletionTokens { get; set; }
	}
}

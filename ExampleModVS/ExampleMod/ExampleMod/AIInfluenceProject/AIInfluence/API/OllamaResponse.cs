using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002A2 RID: 674
	public class OllamaResponse
	{
		// Token: 0x170003A3 RID: 931
		// (get) Token: 0x0600134E RID: 4942 RVA: 0x00020048 File Offset: 0x0001E248
		// (set) Token: 0x0600134F RID: 4943 RVA: 0x00020050 File Offset: 0x0001E250
		[JsonProperty("response")]
		public string Response { get; set; } = "";

		// Token: 0x170003A4 RID: 932
		// (get) Token: 0x06001350 RID: 4944 RVA: 0x00020059 File Offset: 0x0001E259
		// (set) Token: 0x06001351 RID: 4945 RVA: 0x00020061 File Offset: 0x0001E261
		[JsonProperty("done")]
		public bool Done { get; set; }

		// Token: 0x170003A5 RID: 933
		// (get) Token: 0x06001352 RID: 4946 RVA: 0x0002006A File Offset: 0x0001E26A
		// (set) Token: 0x06001353 RID: 4947 RVA: 0x00020072 File Offset: 0x0001E272
		[JsonProperty("context")]
		public int[] Context { get; set; }

		// Token: 0x170003A6 RID: 934
		// (get) Token: 0x06001354 RID: 4948 RVA: 0x0002007B File Offset: 0x0001E27B
		// (set) Token: 0x06001355 RID: 4949 RVA: 0x00020083 File Offset: 0x0001E283
		[JsonProperty("total_duration")]
		public long TotalDuration { get; set; }

		// Token: 0x170003A7 RID: 935
		// (get) Token: 0x06001356 RID: 4950 RVA: 0x0002008C File Offset: 0x0001E28C
		// (set) Token: 0x06001357 RID: 4951 RVA: 0x00020094 File Offset: 0x0001E294
		[JsonProperty("load_duration")]
		public long LoadDuration { get; set; }

		// Token: 0x170003A8 RID: 936
		// (get) Token: 0x06001358 RID: 4952 RVA: 0x0002009D File Offset: 0x0001E29D
		// (set) Token: 0x06001359 RID: 4953 RVA: 0x000200A5 File Offset: 0x0001E2A5
		[JsonProperty("prompt_eval_count")]
		public int PromptEvalCount { get; set; }

		// Token: 0x170003A9 RID: 937
		// (get) Token: 0x0600135A RID: 4954 RVA: 0x000200AE File Offset: 0x0001E2AE
		// (set) Token: 0x0600135B RID: 4955 RVA: 0x000200B6 File Offset: 0x0001E2B6
		[JsonProperty("prompt_eval_duration")]
		public long PromptEvalDuration { get; set; }

		// Token: 0x170003AA RID: 938
		// (get) Token: 0x0600135C RID: 4956 RVA: 0x000200BF File Offset: 0x0001E2BF
		// (set) Token: 0x0600135D RID: 4957 RVA: 0x000200C7 File Offset: 0x0001E2C7
		[JsonProperty("eval_count")]
		public int EvalCount { get; set; }

		// Token: 0x170003AB RID: 939
		// (get) Token: 0x0600135E RID: 4958 RVA: 0x000200D0 File Offset: 0x0001E2D0
		// (set) Token: 0x0600135F RID: 4959 RVA: 0x000200D8 File Offset: 0x0001E2D8
		[JsonProperty("eval_duration")]
		public long EvalDuration { get; set; }
	}
}

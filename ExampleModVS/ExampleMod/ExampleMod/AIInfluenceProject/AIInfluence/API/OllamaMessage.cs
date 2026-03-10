using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002B5 RID: 693
	public class OllamaMessage
	{
		// Token: 0x170003C6 RID: 966
		// (get) Token: 0x060013B5 RID: 5045 RVA: 0x00020295 File Offset: 0x0001E495
		// (set) Token: 0x060013B6 RID: 5046 RVA: 0x0002029D File Offset: 0x0001E49D
		[JsonProperty("role")]
		public string Role { get; set; }

		// Token: 0x170003C7 RID: 967
		// (get) Token: 0x060013B7 RID: 5047 RVA: 0x000202A6 File Offset: 0x0001E4A6
		// (set) Token: 0x060013B8 RID: 5048 RVA: 0x000202AE File Offset: 0x0001E4AE
		[JsonProperty("content")]
		public string Content { get; set; }
	}
}

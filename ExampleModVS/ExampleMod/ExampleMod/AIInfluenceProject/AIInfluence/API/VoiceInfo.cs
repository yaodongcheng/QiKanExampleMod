using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002B7 RID: 695
	public class VoiceInfo
	{
		// Token: 0x170003CB RID: 971
		// (get) Token: 0x060013C1 RID: 5057 RVA: 0x000202EA File Offset: 0x0001E4EA
		// (set) Token: 0x060013C2 RID: 5058 RVA: 0x000202F2 File Offset: 0x0001E4F2
		[JsonProperty("id")]
		public string Id { get; set; }

		// Token: 0x170003CC RID: 972
		// (get) Token: 0x060013C3 RID: 5059 RVA: 0x000202FB File Offset: 0x0001E4FB
		// (set) Token: 0x060013C4 RID: 5060 RVA: 0x00020303 File Offset: 0x0001E503
		[JsonProperty("name")]
		public string Name { get; set; }

		// Token: 0x170003CD RID: 973
		// (get) Token: 0x060013C5 RID: 5061 RVA: 0x0002030C File Offset: 0x0001E50C
		// (set) Token: 0x060013C6 RID: 5062 RVA: 0x00020314 File Offset: 0x0001E514
		[JsonProperty("gender")]
		public string Gender { get; set; }

		// Token: 0x170003CE RID: 974
		// (get) Token: 0x060013C7 RID: 5063 RVA: 0x0002031D File Offset: 0x0001E51D
		// (set) Token: 0x060013C8 RID: 5064 RVA: 0x00020325 File Offset: 0x0001E525
		[JsonProperty("language")]
		public string Language { get; set; }
	}
}

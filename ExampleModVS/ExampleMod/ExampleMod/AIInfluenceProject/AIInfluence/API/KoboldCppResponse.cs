using System;
using Newtonsoft.Json;

namespace AIInfluence.API
{
	// Token: 0x020002A3 RID: 675
	public class KoboldCppResponse
	{
		// Token: 0x170003AC RID: 940
		// (get) Token: 0x06001361 RID: 4961 RVA: 0x000200F5 File Offset: 0x0001E2F5
		// (set) Token: 0x06001362 RID: 4962 RVA: 0x000200FD File Offset: 0x0001E2FD
		[JsonProperty("results")]
		public KoboldCppResult[] Results { get; set; } = new KoboldCppResult[0];
	}
}

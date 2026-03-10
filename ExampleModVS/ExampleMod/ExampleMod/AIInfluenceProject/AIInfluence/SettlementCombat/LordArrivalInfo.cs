using System;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000119 RID: 281
	public class LordArrivalInfo
	{
		// Token: 0x170001DB RID: 475
		// (get) Token: 0x06000926 RID: 2342 RVA: 0x0001D225 File Offset: 0x0001B425
		// (set) Token: 0x06000927 RID: 2343 RVA: 0x0001D22D File Offset: 0x0001B42D
		public string LordStringId { get; set; }

		// Token: 0x170001DC RID: 476
		// (get) Token: 0x06000928 RID: 2344 RVA: 0x0001D236 File Offset: 0x0001B436
		// (set) Token: 0x06000929 RID: 2345 RVA: 0x0001D23E File Offset: 0x0001B43E
		public string LordName { get; set; }

		// Token: 0x170001DD RID: 477
		// (get) Token: 0x0600092A RID: 2346 RVA: 0x0001D247 File Offset: 0x0001B447
		// (set) Token: 0x0600092B RID: 2347 RVA: 0x0001D24F File Offset: 0x0001B44F
		public bool OnPlayerSide { get; set; }

		// Token: 0x170001DE RID: 478
		// (get) Token: 0x0600092C RID: 2348 RVA: 0x0001D258 File Offset: 0x0001B458
		// (set) Token: 0x0600092D RID: 2349 RVA: 0x0001D260 File Offset: 0x0001B460
		public int TroopsSpawned { get; set; }

		// Token: 0x170001DF RID: 479
		// (get) Token: 0x0600092E RID: 2350 RVA: 0x0001D269 File Offset: 0x0001B469
		// (set) Token: 0x0600092F RID: 2351 RVA: 0x0001D271 File Offset: 0x0001B471
		public int TroopsLost { get; set; }
	}
}

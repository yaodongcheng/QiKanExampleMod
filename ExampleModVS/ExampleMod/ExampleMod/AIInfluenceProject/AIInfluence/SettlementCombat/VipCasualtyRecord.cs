using System;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200011D RID: 285
	public class VipCasualtyRecord
	{
		// Token: 0x170001E8 RID: 488
		// (get) Token: 0x06000945 RID: 2373 RVA: 0x0001D302 File Offset: 0x0001B502
		// (set) Token: 0x06000946 RID: 2374 RVA: 0x0001D30A File Offset: 0x0001B50A
		public string VictimName { get; set; }

		// Token: 0x170001E9 RID: 489
		// (get) Token: 0x06000947 RID: 2375 RVA: 0x0001D313 File Offset: 0x0001B513
		// (set) Token: 0x06000948 RID: 2376 RVA: 0x0001D31B File Offset: 0x0001B51B
		public string VictimStringId { get; set; }

		// Token: 0x170001EA RID: 490
		// (get) Token: 0x06000949 RID: 2377 RVA: 0x0001D324 File Offset: 0x0001B524
		// (set) Token: 0x0600094A RID: 2378 RVA: 0x0001D32C File Offset: 0x0001B52C
		public CombatSide Side { get; set; }

		// Token: 0x170001EB RID: 491
		// (get) Token: 0x0600094B RID: 2379 RVA: 0x0001D335 File Offset: 0x0001B535
		// (set) Token: 0x0600094C RID: 2380 RVA: 0x0001D33D File Offset: 0x0001B53D
		public bool IsKilled { get; set; }

		// Token: 0x170001EC RID: 492
		// (get) Token: 0x0600094D RID: 2381 RVA: 0x0001D346 File Offset: 0x0001B546
		// (set) Token: 0x0600094E RID: 2382 RVA: 0x0001D34E File Offset: 0x0001B54E
		public string KillerName { get; set; }

		// Token: 0x170001ED RID: 493
		// (get) Token: 0x0600094F RID: 2383 RVA: 0x0001D357 File Offset: 0x0001B557
		// (set) Token: 0x06000950 RID: 2384 RVA: 0x0001D35F File Offset: 0x0001B55F
		public string KillerStringId { get; set; }

		// Token: 0x170001EE RID: 494
		// (get) Token: 0x06000951 RID: 2385 RVA: 0x0001D368 File Offset: 0x0001B568
		// (set) Token: 0x06000952 RID: 2386 RVA: 0x0001D370 File Offset: 0x0001B570
		public string KillerType { get; set; }

		// Token: 0x06000953 RID: 2387 RVA: 0x000B1984 File Offset: 0x000AFB84
		public VipCasualtyRecord Clone()
		{
			return new VipCasualtyRecord
			{
				VictimName = this.VictimName,
				VictimStringId = this.VictimStringId,
				Side = this.Side,
				IsKilled = this.IsKilled,
				KillerName = this.KillerName,
				KillerStringId = this.KillerStringId,
				KillerType = this.KillerType
			};
		}
	}
}

using System;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200011C RID: 284
	public class CivilianCasualtySummary
	{
		// Token: 0x170001E2 RID: 482
		// (get) Token: 0x06000937 RID: 2359 RVA: 0x0001D29C File Offset: 0x0001B49C
		// (set) Token: 0x06000938 RID: 2360 RVA: 0x0001D2A4 File Offset: 0x0001B4A4
		public int MenKilled { get; set; }

		// Token: 0x170001E3 RID: 483
		// (get) Token: 0x06000939 RID: 2361 RVA: 0x0001D2AD File Offset: 0x0001B4AD
		// (set) Token: 0x0600093A RID: 2362 RVA: 0x0001D2B5 File Offset: 0x0001B4B5
		public int WomenKilled { get; set; }

		// Token: 0x170001E4 RID: 484
		// (get) Token: 0x0600093B RID: 2363 RVA: 0x0001D2BE File Offset: 0x0001B4BE
		// (set) Token: 0x0600093C RID: 2364 RVA: 0x0001D2C6 File Offset: 0x0001B4C6
		public int ChildrenKilled { get; set; }

		// Token: 0x170001E5 RID: 485
		// (get) Token: 0x0600093D RID: 2365 RVA: 0x0001D2CF File Offset: 0x0001B4CF
		// (set) Token: 0x0600093E RID: 2366 RVA: 0x0001D2D7 File Offset: 0x0001B4D7
		public int MenWounded { get; set; }

		// Token: 0x170001E6 RID: 486
		// (get) Token: 0x0600093F RID: 2367 RVA: 0x0001D2E0 File Offset: 0x0001B4E0
		// (set) Token: 0x06000940 RID: 2368 RVA: 0x0001D2E8 File Offset: 0x0001B4E8
		public int WomenWounded { get; set; }

		// Token: 0x170001E7 RID: 487
		// (get) Token: 0x06000941 RID: 2369 RVA: 0x0001D2F1 File Offset: 0x0001B4F1
		// (set) Token: 0x06000942 RID: 2370 RVA: 0x0001D2F9 File Offset: 0x0001B4F9
		public int ChildrenWounded { get; set; }

		// Token: 0x06000943 RID: 2371 RVA: 0x000B191C File Offset: 0x000AFB1C
		public CivilianCasualtySummary Clone()
		{
			return new CivilianCasualtySummary
			{
				MenKilled = this.MenKilled,
				WomenKilled = this.WomenKilled,
				ChildrenKilled = this.ChildrenKilled,
				MenWounded = this.MenWounded,
				WomenWounded = this.WomenWounded,
				ChildrenWounded = this.ChildrenWounded
			};
		}
	}
}

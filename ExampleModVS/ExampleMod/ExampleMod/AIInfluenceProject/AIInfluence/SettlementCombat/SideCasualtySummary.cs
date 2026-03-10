using System;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200011B RID: 283
	public class SideCasualtySummary
	{
		// Token: 0x170001E0 RID: 480
		// (get) Token: 0x06000931 RID: 2353 RVA: 0x0001D27A File Offset: 0x0001B47A
		// (set) Token: 0x06000932 RID: 2354 RVA: 0x0001D282 File Offset: 0x0001B482
		public int Killed { get; set; }

		// Token: 0x170001E1 RID: 481
		// (get) Token: 0x06000933 RID: 2355 RVA: 0x0001D28B File Offset: 0x0001B48B
		// (set) Token: 0x06000934 RID: 2356 RVA: 0x0001D293 File Offset: 0x0001B493
		public int Wounded { get; set; }

		// Token: 0x06000935 RID: 2357 RVA: 0x000B18E8 File Offset: 0x000AFAE8
		public SideCasualtySummary Clone()
		{
			return new SideCasualtySummary
			{
				Killed = this.Killed,
				Wounded = this.Wounded
			};
		}
	}
}

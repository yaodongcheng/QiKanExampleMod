using System;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000117 RID: 279
	public class WoundRecord
	{
		// Token: 0x170001B4 RID: 436
		// (get) Token: 0x060008D6 RID: 2262 RVA: 0x0001CF8E File Offset: 0x0001B18E
		// (set) Token: 0x060008D7 RID: 2263 RVA: 0x0001CF96 File Offset: 0x0001B196
		public string VictimStringId { get; set; }

		// Token: 0x170001B5 RID: 437
		// (get) Token: 0x060008D8 RID: 2264 RVA: 0x0001CF9F File Offset: 0x0001B19F
		// (set) Token: 0x060008D9 RID: 2265 RVA: 0x0001CFA7 File Offset: 0x0001B1A7
		public string VictimName { get; set; }

		// Token: 0x170001B6 RID: 438
		// (get) Token: 0x060008DA RID: 2266 RVA: 0x0001CFB0 File Offset: 0x0001B1B0
		// (set) Token: 0x060008DB RID: 2267 RVA: 0x0001CFB8 File Offset: 0x0001B1B8
		public string VictimType { get; set; }

		// Token: 0x170001B7 RID: 439
		// (get) Token: 0x060008DC RID: 2268 RVA: 0x0001CFC1 File Offset: 0x0001B1C1
		// (set) Token: 0x060008DD RID: 2269 RVA: 0x0001CFC9 File Offset: 0x0001B1C9
		public string AttackerStringId { get; set; }

		// Token: 0x170001B8 RID: 440
		// (get) Token: 0x060008DE RID: 2270 RVA: 0x0001CFD2 File Offset: 0x0001B1D2
		// (set) Token: 0x060008DF RID: 2271 RVA: 0x0001CFDA File Offset: 0x0001B1DA
		public string AttackerName { get; set; }

		// Token: 0x170001B9 RID: 441
		// (get) Token: 0x060008E0 RID: 2272 RVA: 0x0001CFE3 File Offset: 0x0001B1E3
		// (set) Token: 0x060008E1 RID: 2273 RVA: 0x0001CFEB File Offset: 0x0001B1EB
		public string AttackerType { get; set; }

		// Token: 0x170001BA RID: 442
		// (get) Token: 0x060008E2 RID: 2274 RVA: 0x0001CFF4 File Offset: 0x0001B1F4
		// (set) Token: 0x060008E3 RID: 2275 RVA: 0x0001CFFC File Offset: 0x0001B1FC
		public bool IsCivilian { get; set; }

		// Token: 0x170001BB RID: 443
		// (get) Token: 0x060008E4 RID: 2276 RVA: 0x0001D005 File Offset: 0x0001B205
		// (set) Token: 0x060008E5 RID: 2277 RVA: 0x0001D00D File Offset: 0x0001B20D
		public CampaignTime WoundTime { get; set; }

		// Token: 0x170001BC RID: 444
		// (get) Token: 0x060008E6 RID: 2278 RVA: 0x0001D016 File Offset: 0x0001B216
		// (set) Token: 0x060008E7 RID: 2279 RVA: 0x0001D01E File Offset: 0x0001B21E
		public CombatSide VictimSide { get; set; }

		// Token: 0x170001BD RID: 445
		// (get) Token: 0x060008E8 RID: 2280 RVA: 0x0001D027 File Offset: 0x0001B227
		// (set) Token: 0x060008E9 RID: 2281 RVA: 0x0001D02F File Offset: 0x0001B22F
		public bool IsCivilianFemale { get; set; }

		// Token: 0x170001BE RID: 446
		// (get) Token: 0x060008EA RID: 2282 RVA: 0x0001D038 File Offset: 0x0001B238
		// (set) Token: 0x060008EB RID: 2283 RVA: 0x0001D040 File Offset: 0x0001B240
		public bool IsCivilianChild { get; set; }

		// Token: 0x170001BF RID: 447
		// (get) Token: 0x060008EC RID: 2284 RVA: 0x0001D049 File Offset: 0x0001B249
		// (set) Token: 0x060008ED RID: 2285 RVA: 0x0001D051 File Offset: 0x0001B251
		public bool IsImportant { get; set; }
	}
}

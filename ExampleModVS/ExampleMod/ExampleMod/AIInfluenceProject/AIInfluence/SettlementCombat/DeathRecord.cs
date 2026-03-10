using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000116 RID: 278
	public class DeathRecord
	{
		// Token: 0x170001A7 RID: 423
		// (get) Token: 0x060008BB RID: 2235 RVA: 0x0001CEB1 File Offset: 0x0001B0B1
		// (set) Token: 0x060008BC RID: 2236 RVA: 0x0001CEB9 File Offset: 0x0001B0B9
		public string VictimStringId { get; set; }

		// Token: 0x170001A8 RID: 424
		// (get) Token: 0x060008BD RID: 2237 RVA: 0x0001CEC2 File Offset: 0x0001B0C2
		// (set) Token: 0x060008BE RID: 2238 RVA: 0x0001CECA File Offset: 0x0001B0CA
		public string VictimName { get; set; }

		// Token: 0x170001A9 RID: 425
		// (get) Token: 0x060008BF RID: 2239 RVA: 0x0001CED3 File Offset: 0x0001B0D3
		// (set) Token: 0x060008C0 RID: 2240 RVA: 0x0001CEDB File Offset: 0x0001B0DB
		public string VictimType { get; set; }

		// Token: 0x170001AA RID: 426
		// (get) Token: 0x060008C1 RID: 2241 RVA: 0x0001CEE4 File Offset: 0x0001B0E4
		// (set) Token: 0x060008C2 RID: 2242 RVA: 0x0001CEEC File Offset: 0x0001B0EC
		public string KillerStringId { get; set; }

		// Token: 0x170001AB RID: 427
		// (get) Token: 0x060008C3 RID: 2243 RVA: 0x0001CEF5 File Offset: 0x0001B0F5
		// (set) Token: 0x060008C4 RID: 2244 RVA: 0x0001CEFD File Offset: 0x0001B0FD
		public string KillerName { get; set; }

		// Token: 0x170001AC RID: 428
		// (get) Token: 0x060008C5 RID: 2245 RVA: 0x0001CF06 File Offset: 0x0001B106
		// (set) Token: 0x060008C6 RID: 2246 RVA: 0x0001CF0E File Offset: 0x0001B10E
		public string KillerType { get; set; }

		// Token: 0x170001AD RID: 429
		// (get) Token: 0x060008C7 RID: 2247 RVA: 0x0001CF17 File Offset: 0x0001B117
		// (set) Token: 0x060008C8 RID: 2248 RVA: 0x0001CF1F File Offset: 0x0001B11F
		public bool IsCivilian { get; set; }

		// Token: 0x170001AE RID: 430
		// (get) Token: 0x060008C9 RID: 2249 RVA: 0x0001CF28 File Offset: 0x0001B128
		// (set) Token: 0x060008CA RID: 2250 RVA: 0x0001CF30 File Offset: 0x0001B130
		public CampaignTime DeathTime { get; set; }

		// Token: 0x170001AF RID: 431
		// (get) Token: 0x060008CB RID: 2251 RVA: 0x0001CF39 File Offset: 0x0001B139
		// (set) Token: 0x060008CC RID: 2252 RVA: 0x0001CF41 File Offset: 0x0001B141
		public KillCharacterAction.KillCharacterActionDetail DeathDetail { get; set; }

		// Token: 0x170001B0 RID: 432
		// (get) Token: 0x060008CD RID: 2253 RVA: 0x0001CF4A File Offset: 0x0001B14A
		// (set) Token: 0x060008CE RID: 2254 RVA: 0x0001CF52 File Offset: 0x0001B152
		public CombatSide VictimSide { get; set; }

		// Token: 0x170001B1 RID: 433
		// (get) Token: 0x060008CF RID: 2255 RVA: 0x0001CF5B File Offset: 0x0001B15B
		// (set) Token: 0x060008D0 RID: 2256 RVA: 0x0001CF63 File Offset: 0x0001B163
		public bool IsCivilianFemale { get; set; }

		// Token: 0x170001B2 RID: 434
		// (get) Token: 0x060008D1 RID: 2257 RVA: 0x0001CF6C File Offset: 0x0001B16C
		// (set) Token: 0x060008D2 RID: 2258 RVA: 0x0001CF74 File Offset: 0x0001B174
		public bool IsCivilianChild { get; set; }

		// Token: 0x170001B3 RID: 435
		// (get) Token: 0x060008D3 RID: 2259 RVA: 0x0001CF7D File Offset: 0x0001B17D
		// (set) Token: 0x060008D4 RID: 2260 RVA: 0x0001CF85 File Offset: 0x0001B185
		public bool IsImportant { get; set; }
	}
}

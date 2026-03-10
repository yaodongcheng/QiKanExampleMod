using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000118 RID: 280
	public class CombatResult
	{
		// Token: 0x170001C0 RID: 448
		// (get) Token: 0x060008EF RID: 2287 RVA: 0x0001D05A File Offset: 0x0001B25A
		// (set) Token: 0x060008F0 RID: 2288 RVA: 0x0001D062 File Offset: 0x0001B262
		public Settlement Settlement { get; set; }

		// Token: 0x170001C1 RID: 449
		// (get) Token: 0x060008F1 RID: 2289 RVA: 0x0001D06B File Offset: 0x0001B26B
		// (set) Token: 0x060008F2 RID: 2290 RVA: 0x0001D073 File Offset: 0x0001B273
		public CampaignTime Duration { get; set; }

		// Token: 0x170001C2 RID: 450
		// (get) Token: 0x060008F3 RID: 2291 RVA: 0x0001D07C File Offset: 0x0001B27C
		// (set) Token: 0x060008F4 RID: 2292 RVA: 0x0001D084 File Offset: 0x0001B284
		public int TotalKilled { get; set; }

		// Token: 0x170001C3 RID: 451
		// (get) Token: 0x060008F5 RID: 2293 RVA: 0x0001D08D File Offset: 0x0001B28D
		// (set) Token: 0x060008F6 RID: 2294 RVA: 0x0001D095 File Offset: 0x0001B295
		public int TotalWounded { get; set; }

		// Token: 0x170001C4 RID: 452
		// (get) Token: 0x060008F7 RID: 2295 RVA: 0x0001D09E File Offset: 0x0001B29E
		// (set) Token: 0x060008F8 RID: 2296 RVA: 0x0001D0A6 File Offset: 0x0001B2A6
		public int CiviliansKilled { get; set; }

		// Token: 0x170001C5 RID: 453
		// (get) Token: 0x060008F9 RID: 2297 RVA: 0x0001D0AF File Offset: 0x0001B2AF
		// (set) Token: 0x060008FA RID: 2298 RVA: 0x0001D0B7 File Offset: 0x0001B2B7
		public int CiviliansWounded { get; set; }

		// Token: 0x170001C6 RID: 454
		// (get) Token: 0x060008FB RID: 2299 RVA: 0x0001D0C0 File Offset: 0x0001B2C0
		// (set) Token: 0x060008FC RID: 2300 RVA: 0x0001D0C8 File Offset: 0x0001B2C8
		public List<DeathRecord> Deaths { get; set; } = new List<DeathRecord>();

		// Token: 0x170001C7 RID: 455
		// (get) Token: 0x060008FD RID: 2301 RVA: 0x0001D0D1 File Offset: 0x0001B2D1
		// (set) Token: 0x060008FE RID: 2302 RVA: 0x0001D0D9 File Offset: 0x0001B2D9
		public List<WoundRecord> Wounds { get; set; } = new List<WoundRecord>();

		// Token: 0x170001C8 RID: 456
		// (get) Token: 0x060008FF RID: 2303 RVA: 0x0001D0E2 File Offset: 0x0001B2E2
		// (set) Token: 0x06000900 RID: 2304 RVA: 0x0001D0EA File Offset: 0x0001B2EA
		public List<string> Participants { get; set; } = new List<string>();

		// Token: 0x170001C9 RID: 457
		// (get) Token: 0x06000901 RID: 2305 RVA: 0x0001D0F3 File Offset: 0x0001B2F3
		// (set) Token: 0x06000902 RID: 2306 RVA: 0x0001D0FB File Offset: 0x0001B2FB
		public List<string> CapturedHeroes { get; set; } = new List<string>();

		// Token: 0x170001CA RID: 458
		// (get) Token: 0x06000903 RID: 2307 RVA: 0x0001D104 File Offset: 0x0001B304
		// (set) Token: 0x06000904 RID: 2308 RVA: 0x0001D10C File Offset: 0x0001B30C
		public bool SimpleDefendersArrived { get; set; }

		// Token: 0x170001CB RID: 459
		// (get) Token: 0x06000905 RID: 2309 RVA: 0x0001D115 File Offset: 0x0001B315
		// (set) Token: 0x06000906 RID: 2310 RVA: 0x0001D11D File Offset: 0x0001B31D
		public int SimpleDefendersSpawned { get; set; }

		// Token: 0x170001CC RID: 460
		// (get) Token: 0x06000907 RID: 2311 RVA: 0x0001D126 File Offset: 0x0001B326
		// (set) Token: 0x06000908 RID: 2312 RVA: 0x0001D12E File Offset: 0x0001B32E
		public bool MilitiaArrived { get; set; }

		// Token: 0x170001CD RID: 461
		// (get) Token: 0x06000909 RID: 2313 RVA: 0x0001D137 File Offset: 0x0001B337
		// (set) Token: 0x0600090A RID: 2314 RVA: 0x0001D13F File Offset: 0x0001B33F
		public int MilitiaSpawned { get; set; }

		// Token: 0x170001CE RID: 462
		// (get) Token: 0x0600090B RID: 2315 RVA: 0x0001D148 File Offset: 0x0001B348
		// (set) Token: 0x0600090C RID: 2316 RVA: 0x0001D150 File Offset: 0x0001B350
		public bool GuardsArrived { get; set; }

		// Token: 0x170001CF RID: 463
		// (get) Token: 0x0600090D RID: 2317 RVA: 0x0001D159 File Offset: 0x0001B359
		// (set) Token: 0x0600090E RID: 2318 RVA: 0x0001D161 File Offset: 0x0001B361
		public int GuardsSpawned { get; set; }

		// Token: 0x170001D0 RID: 464
		// (get) Token: 0x0600090F RID: 2319 RVA: 0x0001D16A File Offset: 0x0001B36A
		// (set) Token: 0x06000910 RID: 2320 RVA: 0x0001D172 File Offset: 0x0001B372
		public List<LordArrivalInfo> LordsArrived { get; set; } = new List<LordArrivalInfo>();

		// Token: 0x170001D1 RID: 465
		// (get) Token: 0x06000911 RID: 2321 RVA: 0x0001D17B File Offset: 0x0001B37B
		// (set) Token: 0x06000912 RID: 2322 RVA: 0x0001D183 File Offset: 0x0001B383
		public int MilitiaKilled { get; set; }

		// Token: 0x170001D2 RID: 466
		// (get) Token: 0x06000913 RID: 2323 RVA: 0x0001D18C File Offset: 0x0001B38C
		// (set) Token: 0x06000914 RID: 2324 RVA: 0x0001D194 File Offset: 0x0001B394
		public int SimpleDefendersKilled { get; set; }

		// Token: 0x170001D3 RID: 467
		// (get) Token: 0x06000915 RID: 2325 RVA: 0x0001D19D File Offset: 0x0001B39D
		// (set) Token: 0x06000916 RID: 2326 RVA: 0x0001D1A5 File Offset: 0x0001B3A5
		public int GuardsKilled { get; set; }

		// Token: 0x170001D4 RID: 468
		// (get) Token: 0x06000917 RID: 2327 RVA: 0x0001D1AE File Offset: 0x0001B3AE
		// (set) Token: 0x06000918 RID: 2328 RVA: 0x0001D1B6 File Offset: 0x0001B3B6
		public SideCasualtySummary DefenderCasualties { get; set; } = new SideCasualtySummary();

		// Token: 0x170001D5 RID: 469
		// (get) Token: 0x06000919 RID: 2329 RVA: 0x0001D1BF File Offset: 0x0001B3BF
		// (set) Token: 0x0600091A RID: 2330 RVA: 0x0001D1C7 File Offset: 0x0001B3C7
		public SideCasualtySummary AttackerCasualties { get; set; } = new SideCasualtySummary();

		// Token: 0x170001D6 RID: 470
		// (get) Token: 0x0600091B RID: 2331 RVA: 0x0001D1D0 File Offset: 0x0001B3D0
		// (set) Token: 0x0600091C RID: 2332 RVA: 0x0001D1D8 File Offset: 0x0001B3D8
		public CivilianCasualtySummary CivilianCasualties { get; set; } = new CivilianCasualtySummary();

		// Token: 0x170001D7 RID: 471
		// (get) Token: 0x0600091D RID: 2333 RVA: 0x0001D1E1 File Offset: 0x0001B3E1
		// (set) Token: 0x0600091E RID: 2334 RVA: 0x0001D1E9 File Offset: 0x0001B3E9
		public List<VipCasualtyRecord> ImportantCasualties { get; set; } = new List<VipCasualtyRecord>();

		// Token: 0x170001D8 RID: 472
		// (get) Token: 0x0600091F RID: 2335 RVA: 0x0001D1F2 File Offset: 0x0001B3F2
		// (set) Token: 0x06000920 RID: 2336 RVA: 0x0001D1FA File Offset: 0x0001B3FA
		public bool PlayerEvacuated { get; set; }

		// Token: 0x170001D9 RID: 473
		// (get) Token: 0x06000921 RID: 2337 RVA: 0x0001D203 File Offset: 0x0001B403
		// (set) Token: 0x06000922 RID: 2338 RVA: 0x0001D20B File Offset: 0x0001B40B
		public bool PlayerCaptured { get; set; }

		// Token: 0x170001DA RID: 474
		// (get) Token: 0x06000923 RID: 2339 RVA: 0x0001D214 File Offset: 0x0001B414
		// (set) Token: 0x06000924 RID: 2340 RVA: 0x0001D21C File Offset: 0x0001B41C
		public Settlement PlayerPrisonSettlement { get; set; }
	}
}

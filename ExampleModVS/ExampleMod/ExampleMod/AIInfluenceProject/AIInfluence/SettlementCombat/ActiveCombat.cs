using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200014F RID: 335
	public class ActiveCombat
	{
		// Token: 0x06000A77 RID: 2679 RVA: 0x0001DAC6 File Offset: 0x0001BCC6
		public ActiveCombat()
		{
			this.PlayerCompanions = new List<Hero>();
			this.CompanionDecisions = new Dictionary<string, CompanionCombatDecision>();
		}

		// Token: 0x170001F4 RID: 500
		// (get) Token: 0x06000A78 RID: 2680 RVA: 0x0001DAE8 File Offset: 0x0001BCE8
		// (set) Token: 0x06000A79 RID: 2681 RVA: 0x0001DAF0 File Offset: 0x0001BCF0
		public Settlement Settlement { get; set; }

		// Token: 0x170001F5 RID: 501
		// (get) Token: 0x06000A7A RID: 2682 RVA: 0x0001DAF9 File Offset: 0x0001BCF9
		// (set) Token: 0x06000A7B RID: 2683 RVA: 0x0001DB01 File Offset: 0x0001BD01
		public Hero TriggerNPC { get; set; }

		// Token: 0x170001F6 RID: 502
		// (get) Token: 0x06000A7C RID: 2684 RVA: 0x0001DB0A File Offset: 0x0001BD0A
		// (set) Token: 0x06000A7D RID: 2685 RVA: 0x0001DB12 File Offset: 0x0001BD12
		public NPCContext TriggerContext { get; set; }

		// Token: 0x170001F7 RID: 503
		// (get) Token: 0x06000A7E RID: 2686 RVA: 0x0001DB1B File Offset: 0x0001BD1B
		// (set) Token: 0x06000A7F RID: 2687 RVA: 0x0001DB23 File Offset: 0x0001BD23
		public CombatTriggerType TriggerType { get; set; }

		// Token: 0x170001F8 RID: 504
		// (get) Token: 0x06000A80 RID: 2688 RVA: 0x0001DB2C File Offset: 0x0001BD2C
		// (set) Token: 0x06000A81 RID: 2689 RVA: 0x0001DB34 File Offset: 0x0001BD34
		public string TriggerResponse { get; set; }

		// Token: 0x170001F9 RID: 505
		// (get) Token: 0x06000A82 RID: 2690 RVA: 0x0001DB3D File Offset: 0x0001BD3D
		// (set) Token: 0x06000A83 RID: 2691 RVA: 0x0001DB45 File Offset: 0x0001BD45
		public CampaignTime StartTime { get; set; }

		// Token: 0x170001FA RID: 506
		// (get) Token: 0x06000A84 RID: 2692 RVA: 0x0001DB4E File Offset: 0x0001BD4E
		// (set) Token: 0x06000A85 RID: 2693 RVA: 0x0001DB56 File Offset: 0x0001BD56
		public CombatSituationAnalysis Analysis { get; set; }

		// Token: 0x170001FB RID: 507
		// (get) Token: 0x06000A86 RID: 2694 RVA: 0x0001DB5F File Offset: 0x0001BD5F
		// (set) Token: 0x06000A87 RID: 2695 RVA: 0x0001DB67 File Offset: 0x0001BD67
		public LocationType LocationType { get; set; }

		// Token: 0x170001FC RID: 508
		// (get) Token: 0x06000A88 RID: 2696 RVA: 0x0001DB70 File Offset: 0x0001BD70
		// (set) Token: 0x06000A89 RID: 2697 RVA: 0x0001DB78 File Offset: 0x0001BD78
		public int DefendersSpawnedInSmallLocation { get; set; }

		// Token: 0x170001FD RID: 509
		// (get) Token: 0x06000A8A RID: 2698 RVA: 0x0001DB81 File Offset: 0x0001BD81
		// (set) Token: 0x06000A8B RID: 2699 RVA: 0x0001DB89 File Offset: 0x0001BD89
		public List<string> NPCsFromSmallLocation { get; set; }

		// Token: 0x170001FE RID: 510
		// (get) Token: 0x06000A8C RID: 2700 RVA: 0x0001DB92 File Offset: 0x0001BD92
		// (set) Token: 0x06000A8D RID: 2701 RVA: 0x0001DB9A File Offset: 0x0001BD9A
		public Vec3 PlayerEntryPosition { get; set; }

		// Token: 0x170001FF RID: 511
		// (get) Token: 0x06000A8E RID: 2702 RVA: 0x0001DBA3 File Offset: 0x0001BDA3
		// (set) Token: 0x06000A8F RID: 2703 RVA: 0x0001DBAB File Offset: 0x0001BDAB
		public bool VillageLooted { get; set; }

		// Token: 0x17000200 RID: 512
		// (get) Token: 0x06000A90 RID: 2704 RVA: 0x0001DBB4 File Offset: 0x0001BDB4
		// (set) Token: 0x06000A91 RID: 2705 RVA: 0x0001DBBC File Offset: 0x0001BDBC
		public bool VillageBurned { get; set; }

		// Token: 0x17000201 RID: 513
		// (get) Token: 0x06000A92 RID: 2706 RVA: 0x0001DBC5 File Offset: 0x0001BDC5
		// (set) Token: 0x06000A93 RID: 2707 RVA: 0x0001DBCD File Offset: 0x0001BDCD
		public List<Hero> PlayerCompanions { get; set; }

		// Token: 0x17000202 RID: 514
		// (get) Token: 0x06000A94 RID: 2708 RVA: 0x0001DBD6 File Offset: 0x0001BDD6
		// (set) Token: 0x06000A95 RID: 2709 RVA: 0x0001DBDE File Offset: 0x0001BDDE
		public Dictionary<string, CompanionCombatDecision> CompanionDecisions { get; set; }
	}
}

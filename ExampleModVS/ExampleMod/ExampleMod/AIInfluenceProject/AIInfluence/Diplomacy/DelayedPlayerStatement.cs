using System;
using System.Collections.Generic;
using System.Linq;
using AIInfluence.DynamicEvents;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x02000202 RID: 514
	public class DelayedPlayerStatement
	{
		// Token: 0x170002E7 RID: 743
		// (get) Token: 0x06000F7F RID: 3967 RVA: 0x0001E6B4 File Offset: 0x0001C8B4
		// (set) Token: 0x06000F80 RID: 3968 RVA: 0x0001E6BC File Offset: 0x0001C8BC
		public string StatementText { get; set; }

		// Token: 0x170002E8 RID: 744
		// (get) Token: 0x06000F81 RID: 3969 RVA: 0x0001E6C5 File Offset: 0x0001C8C5
		// (set) Token: 0x06000F82 RID: 3970 RVA: 0x0001E6CD File Offset: 0x0001C8CD
		public DiplomaticAction Action { get; set; } = DiplomaticAction.None;

		// Token: 0x170002E9 RID: 745
		// (get) Token: 0x06000F83 RID: 3971 RVA: 0x0001E6D6 File Offset: 0x0001C8D6
		// (set) Token: 0x06000F84 RID: 3972 RVA: 0x0001E6DE File Offset: 0x0001C8DE
		public List<DiplomaticAction> Actions { get; set; } = new List<DiplomaticAction>();

		// Token: 0x170002EA RID: 746
		// (get) Token: 0x06000F85 RID: 3973 RVA: 0x0001E6E7 File Offset: 0x0001C8E7
		// (set) Token: 0x06000F86 RID: 3974 RVA: 0x0001E6EF File Offset: 0x0001C8EF
		public List<string> TargetKingdomIds { get; set; } = new List<string>();

		// Token: 0x170002EB RID: 747
		// (get) Token: 0x06000F87 RID: 3975 RVA: 0x0001E6F8 File Offset: 0x0001C8F8
		public string TargetKingdomId
		{
			get
			{
				return Enumerable.FirstOrDefault<string>(this.TargetKingdomIds);
			}
		}

		// Token: 0x170002EC RID: 748
		// (get) Token: 0x06000F88 RID: 3976 RVA: 0x0001E705 File Offset: 0x0001C905
		// (set) Token: 0x06000F89 RID: 3977 RVA: 0x0001E70D File Offset: 0x0001C90D
		public string Reason { get; set; }

		// Token: 0x170002ED RID: 749
		// (get) Token: 0x06000F8A RID: 3978 RVA: 0x0001E716 File Offset: 0x0001C916
		// (set) Token: 0x06000F8B RID: 3979 RVA: 0x0001E71E File Offset: 0x0001C91E
		public string Tone { get; set; }

		// Token: 0x170002EE RID: 750
		// (get) Token: 0x06000F8C RID: 3980 RVA: 0x0001E727 File Offset: 0x0001C927
		// (set) Token: 0x06000F8D RID: 3981 RVA: 0x0001E72F File Offset: 0x0001C92F
		public Kingdom PlayerKingdom { get; set; }

		// Token: 0x170002EF RID: 751
		// (get) Token: 0x06000F8E RID: 3982 RVA: 0x0001E738 File Offset: 0x0001C938
		// (set) Token: 0x06000F8F RID: 3983 RVA: 0x0001E740 File Offset: 0x0001C940
		public CampaignTime PublicationTime { get; set; }
	}
}

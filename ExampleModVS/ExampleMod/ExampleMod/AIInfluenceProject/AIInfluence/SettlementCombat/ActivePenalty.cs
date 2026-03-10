using System;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200015B RID: 347
	public class ActivePenalty
	{
		// Token: 0x17000219 RID: 537
		// (get) Token: 0x06000B06 RID: 2822 RVA: 0x0001DEFD File Offset: 0x0001C0FD
		// (set) Token: 0x06000B07 RID: 2823 RVA: 0x0001DF05 File Offset: 0x0001C105
		public string SettlementId { get; set; }

		// Token: 0x1700021A RID: 538
		// (get) Token: 0x06000B08 RID: 2824 RVA: 0x0001DF0E File Offset: 0x0001C10E
		// (set) Token: 0x06000B09 RID: 2825 RVA: 0x0001DF16 File Offset: 0x0001C116
		public float ProsperityPenaltyPerDay { get; set; }

		// Token: 0x1700021B RID: 539
		// (get) Token: 0x06000B0A RID: 2826 RVA: 0x0001DF1F File Offset: 0x0001C11F
		// (set) Token: 0x06000B0B RID: 2827 RVA: 0x0001DF27 File Offset: 0x0001C127
		public float StartDay { get; set; }

		// Token: 0x1700021C RID: 540
		// (get) Token: 0x06000B0C RID: 2828 RVA: 0x0001DF30 File Offset: 0x0001C130
		// (set) Token: 0x06000B0D RID: 2829 RVA: 0x0001DF38 File Offset: 0x0001C138
		public int DurationDays { get; set; }

		// Token: 0x1700021D RID: 541
		// (get) Token: 0x06000B0E RID: 2830 RVA: 0x0001DF41 File Offset: 0x0001C141
		// (set) Token: 0x06000B0F RID: 2831 RVA: 0x0001DF49 File Offset: 0x0001C149
		public string Reason { get; set; }

		// Token: 0x06000B10 RID: 2832 RVA: 0x000C6684 File Offset: 0x000C4884
		public int GetRemainingDays()
		{
			float num = (float)CampaignTime.Now.ToDays;
			int val = this.DurationDays - (int)(num - this.StartDay);
			return Math.Max(0, val);
		}

		// Token: 0x06000B11 RID: 2833 RVA: 0x000C66C0 File Offset: 0x000C48C0
		public bool IsActive()
		{
			return this.GetRemainingDays() > 0;
		}
	}
}

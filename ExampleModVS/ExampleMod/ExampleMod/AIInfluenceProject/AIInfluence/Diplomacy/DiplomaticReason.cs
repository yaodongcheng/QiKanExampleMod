using System;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x02000287 RID: 647
	[JsonSerializable]
	public class DiplomaticReason
	{
		// Token: 0x1700039D RID: 925
		// (get) Token: 0x060012EF RID: 4847 RVA: 0x0001FF28 File Offset: 0x0001E128
		// (set) Token: 0x060012F0 RID: 4848 RVA: 0x0001FF30 File Offset: 0x0001E130
		[JsonProperty("action_type")]
		public string ActionType { get; set; }

		// Token: 0x1700039E RID: 926
		// (get) Token: 0x060012F1 RID: 4849 RVA: 0x0001FF39 File Offset: 0x0001E139
		// (set) Token: 0x060012F2 RID: 4850 RVA: 0x0001FF41 File Offset: 0x0001E141
		[JsonProperty("target_kingdom_id")]
		public string TargetKingdomId { get; set; }

		// Token: 0x1700039F RID: 927
		// (get) Token: 0x060012F3 RID: 4851 RVA: 0x0001FF4A File Offset: 0x0001E14A
		// (set) Token: 0x060012F4 RID: 4852 RVA: 0x0001FF52 File Offset: 0x0001E152
		[JsonProperty("reason")]
		public string Reason { get; set; }

		// Token: 0x170003A0 RID: 928
		// (get) Token: 0x060012F5 RID: 4853 RVA: 0x0001FF5B File Offset: 0x0001E15B
		// (set) Token: 0x060012F6 RID: 4854 RVA: 0x0001FF63 File Offset: 0x0001E163
		[JsonProperty("timestamp_days")]
		public float TimestampDays { get; set; }

		// Token: 0x170003A1 RID: 929
		// (get) Token: 0x060012F7 RID: 4855 RVA: 0x00112598 File Offset: 0x00110798
		// (set) Token: 0x060012F8 RID: 4856 RVA: 0x001125F0 File Offset: 0x001107F0
		[JsonIgnore]
		public CampaignTime Timestamp
		{
			get
			{
				bool flag;
				if (this.TimestampDays > 0f)
				{
					CampaignTime now = CampaignTime.Now;
					flag = true;
				}
				else
				{
					flag = false;
				}
				bool flag2 = flag;
				CampaignTime result;
				if (flag2)
				{
					float num = (float)CampaignTime.Now.ToDays;
					float num2 = num - this.TimestampDays;
					result = CampaignTime.DaysFromNow(-num2);
				}
				else
				{
					result = CampaignTime.Now;
				}
				return result;
			}
			set
			{
				this.TimestampDays = (float)value.ToDays;
			}
		}

		// Token: 0x170003A2 RID: 930
		// (get) Token: 0x060012F9 RID: 4857 RVA: 0x0001FF6C File Offset: 0x0001E16C
		// (set) Token: 0x060012FA RID: 4858 RVA: 0x0001FF74 File Offset: 0x0001E174
		[JsonProperty("statement_text")]
		public string StatementText { get; set; }

		// Token: 0x060012FB RID: 4859 RVA: 0x0001FF7D File Offset: 0x0001E17D
		public DiplomaticReason()
		{
			this.Timestamp = CampaignTime.Now;
		}

		// Token: 0x060012FC RID: 4860 RVA: 0x0001FF93 File Offset: 0x0001E193
		public DiplomaticReason(string actionType, string targetKingdomId, string reason, string statementText)
		{
			this.ActionType = actionType;
			this.TargetKingdomId = targetKingdomId;
			this.Reason = reason;
			this.StatementText = statementText;
			this.Timestamp = CampaignTime.Now;
		}
	}
}
